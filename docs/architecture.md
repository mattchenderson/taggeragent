# TaggerAgent — Architecture

## 1. Overview

TaggerAgent is an Azure Foundry hosted agent that scans Azure resources across a
subscription and recommends or applies tags based on configurable rules. It is
deployed via `azd` with Bicep infrastructure and uses `DefaultAzureCredential`
for all Azure access — no secrets stored or managed by the operator.

```text
┌──────────────────────────────────────────────────────────────┐
│                     Azure Foundry                            │
│  ┌────────────────────────────────────────────────────────┐  │
│  │            TaggerAgent (hosted agent)                  │  │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │  │
│  │  │ Instructions │  │    Tools     │  │    Model     │  │  │
│  │  │  (system     │  │  - scan      │  │ gpt-4o-mini  │  │  │
│  │  │   prompt)    │  │  - tag       │  │              │  │  │
│  │  │              │  │  - rules     │  │              │  │  │
│  │  └──────────────┘  └──────┬───────┘  └──────────────┘  │  │
│  └───────────────────────────┼────────────────────────────┘  │
│                              │                               │
│  ┌───────────────────────────┼────────────────────────────┐  │
│  │   Foundry Account + Project  ·  ACR (container image)  │  │
│  └───────────────────────────┼────────────────────────────┘  │
└──────────────────────────────┼───────────────────────────────┘
                               │
          ┌────────────────────┼────────────────────┐
          │                    │                    │
  ┌───────▼───────┐   ┌───────▼───────┐   ┌───────▼───────┐
  │ Azure Resource│   │  ARM API      │   │  Blob Storage │
  │ Graph         │   │  (tag write)  │   │  (rules file) │
  │ (scan/read)   │   │               │   │               │
  └───────────────┘   └───────────────┘   ├───────────────┤
                                          │  Table Storage│
          ┌───────────────────┐           │  (audit log)  │
          │  Azure Functions  │           └───────────────┘
          │  (timer trigger)  │                    ▲
          │                   ├────────────────────┘
          │  Invokes agent    │   writes audit summary
          │  via Foundry API  │
          └───────────────────┘
```

### Key interaction flow

**Interactive (user-initiated):**

1. User sends a message to the agent (e.g., "scan my subscription and find
   untagged resources").
2. The agent invokes its **scan tool** which queries Azure Resource Graph.
3. The agent loads **tagging rules** from a rules file in Blob Storage.
4. The agent reasons over resources vs. rules and proposes tag changes.
5. On user approval, the agent invokes its **tag tool** to apply tags via ARM.
6. All changes are logged to the audit table.

**Automated (timer-initiated):**

1. Azure Function fires on schedule and sends an automated-mode message to the
   agent via Foundry API.
2. The agent scans, loads rules, and assesses — same as interactive.
3. High-confidence changes with `autoApply=true` are applied immediately.
4. Low-confidence and `autoApply=false` changes are reported but not applied.
5. All changes (applied and pending-review) are logged to the audit table.

---

## 2. Foundry Agent Design

### Agent type: Hosted (container-based) with Microsoft Agent Framework

We use a hosted agent with the Microsoft Agent Framework
(`Microsoft.Agents.AI.*`) rather than a prompt-based agent because:

- We need custom C# code for Azure SDK calls (Resource Graph, ARM).
- Container hosting gives us full control over dependencies and runtime.
- The Agent Framework handles the tool-calling loop, function registration, and
  OpenAI-compatible API hosting automatically.
- The `responses` protocol provides a clean request/response model.

### Agent definition

```json
{
  "kind": "hosted",
  "image": "<acr-name>.azurecr.io/taggeragent:latest",
  "cpu": "1",
  "memory": "2Gi",
  "container_protocol_versions": [
    { "protocol": "responses", "version": "2025-03-26" }
  ],
  "environment_variables": {
    "AZURE_SUBSCRIPTION_ID": "<subscription-id>",
    "RULES_STORAGE_URL": "<blob-url>"
  }
}
```

### Model

Use `gpt-4o` for the backing model. It balances cost and capability for
tool-calling workflows. The agent itself runs in the container; the model is
invoked from the container code via the Agent Framework, which integrates with
Foundry using `PersistentAgentsClient` from `Azure.AI.Agents.Persistent`.

### Instructions (system prompt)

The agent's system prompt establishes:

- Role: Azure resource tagging assistant
- Scope: operates within a single Azure subscription
- Behavior: always propose changes before applying; never apply without
  confirmation
- Safety: never delete resources, never modify resource configuration beyond
  tags
- Rule management: the agent can help users **create, update, and review**
  tagging rules interactively. If no rules exist for the current subscription,
  the agent should offer to help the user define them conversationally.
- Empty ruleset: if no rules are found for the target subscription, the agent
  informs the user and offers to help create an initial ruleset through
  conversation rather than failing or using hardcoded defaults.

### Tools

The agent exposes function tools to the model:

| Tool | Purpose | Azure API |
| --- | --- | --- |
| `ScanResources` | Query resources, filter by type/group/tag status | Azure Resource Graph SDK |
| `ApplyTags` | Write tags to one or more resources | ARM REST API (`Tags` resource) |
| `GetTaggingRules` | Load the tagging ruleset for the current subscription | Blob Storage SDK |
| `SaveTaggingRules` | Persist new or updated rules for the current subscription | Blob Storage SDK |
| `CopyTaggingRules` | Copy a subscription's ruleset to another subscription | Blob Storage SDK |

The `GetTaggingRules` tool reads from `rules/{subscription-id}/rules.json` in
Blob Storage. If no rules exist for the subscription, it returns an empty
ruleset — the agent then offers to help the user create rules interactively.

The `SaveTaggingRules` tool writes rules to the same subscription-scoped path.
This enables the agent to persist rules that the user defines conversationally.
The agent can walk users through creating rules by asking about their tagging
policies, then generate and save the rules JSON.

The `CopyTaggingRules` tool reads rules from a source subscription and writes
them to a target subscription. This supports scenarios where users want
consistent policies across subscriptions (e.g., copying prod rules to a new
subscription) without forcing global rules.

Tool implementations live in the C# container as regular C# methods. The
Microsoft Agent Framework automatically handles tool registration, function
calling loops, and parameter marshaling. Tools are defined as methods on the
agent class and decorated with appropriate attributes for the framework to
discover and register them.

---

## 3. Azure Resource Enumeration

### Approach: Azure Resource Graph

Resource Graph is the correct choice for subscription-wide resource enumeration
because:

- It supports KQL queries across all resource types in a subscription.
- It returns results in seconds regardless of resource count.
- It includes tag data in the response, avoiding per-resource GET calls.
- It handles pagination via skip tokens.

### Key queries

```kql
// All resources with their tags
Resources
| project id, name, type, resourceGroup, location, tags, subscriptionId

// Resources missing a specific required tag
Resources
| where isnull(tags['environment']) or isnull(tags['owner'])
| project id, name, type, resourceGroup, tags

// Tag coverage summary
Resources
| summarize
    total = count(),
    tagged = countif(isnotempty(tags)),
    untagged = countif(isempty(tags))
```

### C# SDK usage

Use the `Azure.ResourceManager.ResourceGraph` package with
`DefaultAzureCredential`:

```csharp
var client = new ResourceGraphClient(new DefaultAzureCredential());
var query = new QueryRequest(
    subscriptions: new[] { subscriptionId },
    query: "Resources | project id, name, type, resourceGroup, tags"
);
var response = await client.ResourcesAsync(query);
```

Pagination is handled via the `SkipToken` on the response. Batch subscriptions
in groups of 100 per the throttling guidance.

---

## 4. Tagging Strategy

### Design principle: rules are assessments, not schemas

The agent is an LLM — it can reason, not just pattern-match. Rules are
expressed as natural language assessment criteria stored in a JSON document in
Azure Blob Storage. The agent reads the rules and uses judgment to evaluate
each resource, rather than performing rigid schema validation.

This is a deliberate choice. If all we needed was "is tag X present with value
from list Y," we would use Azure Policy. The agent adds value by **inferring**
tag values from context (resource names, resource group conventions, resource
type), **inheriting** from parent scopes, and **reasoning** about edge cases.

### Design principle: user-owned rules, no defaults

The tool ships as a **framework**, not an opinionated policy. No default or
seed rules are included in the repository. Users define their own tagging rules
for each subscription they manage. This ensures the tool adapts to the user's
policies rather than imposing assumptions.

The agent handles the empty-ruleset case gracefully:

1. If no rules exist for the target subscription, the agent informs the user.
2. The agent offers to help create rules interactively through conversation —
   asking about the user's tagging policies, required tags, inference patterns,
   and auto-apply preferences.
3. Once the user is satisfied, the agent persists the rules to Blob Storage via
   the `SaveTaggingRules` tool.

### Rules storage: subscription-scoped

Rules are scoped per subscription. Different subscriptions can have completely
different tagging policies (production vs. development vs. sandbox).

**Blob path pattern:** `rules/{subscription-id}/rules.json`

Example paths:

```text
rules/00000000-0000-0000-0000-000000000001/rules.json   # prod
rules/00000000-0000-0000-0000-000000000002/rules.json   # dev
rules/00000000-0000-0000-0000-000000000003/rules.json   # sandbox
```

When the agent connects to a subscription, it loads **that subscription's**
rules. When the user creates or updates rules, they are saved to that
subscription's scope. The `CopyTaggingRules` tool enables copying a ruleset
from one subscription to another for consistency.

Rules remain in Blob Storage (data, not code) so users can update them without
redeploying the agent. The agent's system prompt defines **how** to assess
(methodology); the rules file defines **what** to assess (policy).

### Rules format (v2 — natural language assessments)

The following is an **example** of what a user-defined ruleset looks like. This
is not shipped with the tool — users create their own rules, either manually or
with the agent's help via conversation.

```json
{
  "$schema": "https://taggeragent/rules/v2",
  "version": "2.0",
  "rules": [
    {
      "tag": "environment",
      "assessment": "Every resource should have an 'environment' tag. Infer from the resource group name if possible (e.g., rg-myapp-prod means production, rg-myapp-dev means development). If the resource group has an 'environment' tag, inherit it. If ambiguous, report as needs-review.",
      "confidence": "high-when-inferable",
      "autoApply": true
    },
    {
      "tag": "owner",
      "assessment": "Every resource should have an 'owner' tag identifying the responsible team or individual. Check if the resource group has an 'owner' tag and inherit it. Do not guess — if no owner is determinable, report as needs-review.",
      "confidence": "high-when-inherited",
      "autoApply": true
    },
    {
      "tag": "cost-center",
      "assessment": "Resources should have a 'cost-center' tag in the format CC-#### (four digits). Inherit from the resource group if available. Do not infer.",
      "confidence": "high-when-inherited",
      "autoApply": true
    },
    {
      "tag": "data-classification",
      "assessment": "Storage accounts, SQL databases, Cosmos DB accounts, and Key Vaults should have a 'data-classification' tag with a value of public, internal, confidential, or restricted. This requires human judgment — always report as needs-review unless inherited from the resource group.",
      "confidence": "low",
      "autoApply": false
    }
  ]
}
```

Each rule includes:

- **`tag`**: The tag key being assessed.
- **`assessment`**: Natural language instructions for the agent. Describes when
  the tag applies, how to infer its value, and what to do when uncertain.
- **`confidence`**: Hint to the agent about how deterministic this rule is.
  Values: `high-when-inferable`, `high-when-inherited`, `low`.
- **`autoApply`**: Whether automated mode can apply this tag without human
  review. Only meaningful in automated execution mode (see section 4a).

### How rules are evaluated

1. The agent loads the rules JSON from Blob Storage via the `GetTaggingRules`
   tool, reading from `rules/{subscription-id}/rules.json`.
2. If no rules exist for the subscription, the agent informs the user and offers
   to help define them interactively (see "user-owned rules" above).
3. The rules are injected into the agent's reasoning context alongside the
   scanned resource data.
4. For each resource, the agent **assesses** each rule using natural language
   reasoning:
   - Can the tag value be inferred from resource metadata (name, type, group)?
   - Can it be inherited from the parent resource group?
   - Is the resolution unambiguous (high confidence) or uncertain (needs review)?
5. The agent classifies each proposed change:
   - **`auto`**: High confidence, unambiguous. Can be applied in automated mode.
   - **`needs-review`**: Requires human judgment. Queued for review in automated
     mode; presented for confirmation in interactive mode.
6. The agent presents a compliance report and proposed changes.
7. Changes are applied based on the current execution mode (see section 4a).

### Tag application

Tags are applied using the ARM `Tags` API at the resource scope:

```http
PATCH https://management.azure.com/{resourceId}/providers/Microsoft.Resources/tags/default?api-version=2024-03-01
```

This uses `TagsPatchResource` with `operation: Merge` to add/update tags without
removing existing ones.

---

## 4a. Execution Modes

The agent supports two execution modes. The mode is determined by **how the
agent is invoked**, not by a configuration flag — the same agent definition
handles both.

### Interactive mode (default)

Triggered by a user conversing with the agent directly (Foundry playground,
API call from a chat UI, etc.).

- Agent scans resources and proposes tag changes.
- **All changes require explicit user confirmation** before applying.
- The agent presents changes grouped by confidence level.
- The user can approve all, approve selectively, or reject.

This is the existing behavior. No change.

### Automated mode (timer-triggered)

Triggered by an Azure Function on a schedule (see section 8). The Function
sends a structured message to the agent:

```text
Run automated tagging scan for subscription {subscription-id}.
Apply all changes where the rule has autoApply=true and your confidence is high.
Do not apply changes where confidence is low or the rule has autoApply=false.
Output a JSON report of all changes (applied and pending-review).
```

In automated mode:

- **High-confidence, autoApply=true**: Applied immediately. Logged to the audit
  table.
- **Everything else**: Written to the report as `pending-review`. Not applied.
- The agent never guesses in automated mode. When in doubt, it reports rather
  than applies.

### Confidence model

The agent's confidence in a proposed tag value comes from two sources:

1. **Rule hint** (`confidence` field): The rule author's assessment of how
   deterministic the rule is.
2. **Agent reasoning**: The agent's own assessment based on the specific resource
   context. A rule marked `high-when-inferable` might still be low-confidence
   for a resource with an ambiguous name.

The agent is instructed: "In automated mode, only auto-apply when **both** the
rule allows it (`autoApply: true`) and your own confidence is high. When in
doubt, report."

### Audit logging

All tag changes (applied and skipped) are logged to an Azure Storage Table
(`TagAuditLog`) with:

| Column | Description |
| --- | --- |
| `PartitionKey` | Subscription ID |
| `RowKey` | `{resourceId}_{tag}_{timestamp}` |
| `ResourceId` | Full ARM resource ID |
| `TagKey` | Tag key |
| `TagValue` | Applied or proposed value |
| `Action` | `applied` or `pending-review` |
| `Confidence` | `high` or `low` |
| `RuleTag` | Which rule triggered this |
| `ExecutionMode` | `interactive` or `automated` |
| `Timestamp` | ISO 8601 |

This table serves as a complete audit trail for compliance and rollback
analysis. Both execution modes write to it.

---

## 5. Infrastructure

### Azure resources required

| Resource | Purpose | Bicep module |
| --- | --- | --- |
| **Foundry Account** | Hosts the AI agent service (system-assigned identity) | `foundry.bicep` |
| **Foundry Project** | Scopes agent, model deployments | `foundry.bicep` (child of account) |
| **Model Deployment** | gpt-4o for agent reasoning | `foundry.bicep` (from extension JSON) |
| **Azure Container Registry** | Stores the agent container image | `foundry.bicep` (conditional on `enableHostedAgents`) |
| **Storage Account** | Holds tagging rules JSON + audit table | `storage.bicep` |
| **Azure Functions (Flex Consumption)** | Timer-triggered automated scans | `function-app.bicep` |
| **Managed Identity (Function)** | Function identity for agent invocation | `identity.bicep` |

The Storage Account hosts:

- **Blob container (`rules`)**: Tagging rules JSON documents, partitioned by
  subscription ID. Path pattern: `rules/{subscription-id}/rules.json`. Each
  subscription has its own ruleset. The container starts empty — users create
  rules via the agent or upload them directly.
- **Table (`TagAuditLog`)**: Audit log for all tag changes (see section 4a).

### Identity and RBAC

Two managed identities are used — one for the agent (Azure resource access) and
one for the Azure Function (agent invocation):

**Agent application's identity:**

| Role | Scope | Purpose |
| --- | --- | --- |
| `Reader` | Subscription | Read resources for scanning |
| `Tag Contributor` | Subscription | Apply tags to resources |
| `Storage Blob Data Contributor` | Storage Account | Read and write tagging rules |
| `Storage Table Data Contributor` | Storage Account | Write audit log entries |
| `AcrPull` | Container Registry | Pull agent container image |

**Function identity:**

| Role | Scope | Purpose |
| --- | --- | --- |
| `Cognitive Services User` | Foundry Account | Invoke the agent via Foundry API |
| `Storage Table Data Reader` | Storage Account | Read audit log for reporting |

> **Note:** `Tag Contributor` is preferred over `Contributor` because it follows
> least-privilege — it can only modify tags, not resource configuration.

### Why not App Service?

The original brief suggested App Service. However, Foundry hosted agents run on
Foundry-managed infrastructure (Container Apps under the hood). The agent
container is pushed to ACR and Foundry handles the hosting. There is **no need
for a separate App Service**. This simplifies the architecture and aligns with
the Foundry deployment model.

### Environment Variable Contract

All environment variable names are defined in `src/TaggerAgent/EnvironmentConfig.cs`
as C# constants. That file is the single source of truth — Bicep app settings,
`main.bicep` outputs, and this documentation must stay aligned with it.

**Agent container** (`src/TaggerAgent`):

| Variable | Description | Source |
| --- | --- | --- |
| `AZURE_AI_PROJECT_ENDPOINT` | Foundry project endpoint URL | azd output (may be auto-injected by Foundry runtime) |
| `AZURE_SUBSCRIPTION_ID` | Target subscription to scan/tag | azd output |
| `RULES_STORAGE_URL` | Blob service endpoint for rules | azd output |
| `STORAGE_ACCOUNT_NAME` | Storage account for audit table | azd output |

**Function App** (`src/TaggerAgent.Functions`):

| Variable | Description | Source |
| --- | --- | --- |
| `AZURE_AI_PROJECT_ENDPOINT` | Foundry project endpoint URL | Bicep app setting |
| `AZURE_SUBSCRIPTION_ID` | Target subscription to scan/tag | Bicep app setting |
| `AZURE_CLIENT_ID` | Function managed identity client ID | Bicep app setting |
| `AGENT_NAME` | Hosted agent name (default: `tagger-agent`) | Bicep app setting |
| `TIMER_SCHEDULE` | NCRONTAB cron expression | Bicep app setting |
| `STORAGE_ACCOUNT_NAME` | Storage account name | Bicep app setting |

**Naming conventions:**

- Variables that follow azd/Foundry conventions use the `AZURE_` prefix
  (e.g., `AZURE_AI_PROJECT_ENDPOINT`, `AZURE_SUBSCRIPTION_ID`).
- Application-specific variables use descriptive names without the `AZURE_`
  prefix (e.g., `RULES_STORAGE_URL`, `STORAGE_ACCOUNT_NAME`, `AGENT_NAME`).
- The Foundry hosted agent runtime may auto-inject `AZURE_AI_PROJECT_ENDPOINT`.
  The code should still read it explicitly so it works in local development.

**How env vars reach the agent container:**

The hosted agent container does NOT have Bicep app settings like a Function App.
Its environment variables come from:

1. **Foundry runtime injection** — the project endpoint and possibly identity
   vars are injected automatically.
2. **azd extension** — `azd ai agent create` can pass `main.bicep` outputs as
   container env vars.
3. **Agent definition** — custom env vars specified in the agent YAML/definition
   file (`agent.yaml`).

For variables that are NOT auto-injected (e.g., `RULES_STORAGE_URL`,
`STORAGE_ACCOUNT_NAME`), the agent definition or azd extension must set them
from `main.bicep` outputs.

---

## 6. azd Project Structure

```text
taggeragent/
├── azure.yaml                    # azd project manifest (host: azure.ai.agent)
├── infra/
│   ├── main.bicep                # Orchestration template (extension-integrated)
│   ├── main.parameters.json      # Environment + extension hook parameters
│   └── modules/
│       ├── foundry.bicep          # Foundry account + project + ACR + model deployments
│       ├── function-app.bicep     # Azure Functions for timer
│       ├── identity.bicep         # Function managed identity
│       ├── monitoring.bicep       # Application Insights + Log Analytics
│       ├── role-assignments.bicep # RBAC role assignments
│       ├── storage-roles.bicep    # Storage RBAC for agent identity
│       └── storage.bicep          # Storage account for rules + audit
├── src/
│   ├── TaggerAgent/
│   │   ├── TaggerAgent.csproj     # C# project
│   │   ├── Program.cs             # Entry point, hosting adapter setup
│   │   ├── EnvironmentConfig.cs   # Environment variable constants
│   │   ├── agent.yaml             # Agent definition for azd
│   │   ├── Agent/
│   │   │   ├── TaggerAgentTools.cs  # Agent tool definitions
│   │   │   └── AgentInstructions.cs # System prompt
│   │   ├── Tools/
│   │   │   ├── ScanResourcesTool.cs
│   │   │   ├── ApplyTagsTool.cs
│   │   │   └── GetTaggingRulesTool.cs
│   │   ├── Models/
│   │   │   ├── TaggingRule.cs
│   │   │   ├── ResourceInfo.cs
│   │   │   ├── TagChange.cs
│   │   │   └── AuditEntry.cs
│   │   ├── Services/
│   │   │   ├── ResourceGraphService.cs
│   │   │   ├── TaggingService.cs
│   │   │   ├── RulesService.cs
│   │   │   └── AuditService.cs
│   │   └── Dockerfile             # Container image definition
│   └── TaggerAgent.Functions/
│       ├── TaggerAgent.Functions.csproj
│       ├── Program.cs             # Functions host setup
│       ├── TimerScanFunction.cs   # Timer-triggered scan
│       └── host.json              # Functions host configuration
├── docs/
│   └── architecture.md           # This document
└── tests/
    └── TaggerAgent.Tests/
        ├── TaggerAgent.Tests.csproj
        ├── Tools/
        │   ├── ScanResourcesToolTests.cs
        │   ├── ApplyTagsToolTests.cs
        │   └── GetTaggingRulesToolTests.cs
        └── Services/
            ├── ResourceGraphServiceTests.cs
            ├── TaggingServiceTests.cs
            ├── RulesServiceTests.cs
            └── AuditServiceTests.cs
```

### azure.yaml

```yaml
requiredVersions:
    extensions:
        azure.ai.agents: '>=0.1.0-preview'
name: taggeragent
metadata:
    template: taggeragent@0.0.2-beta
services:
    functions:
        project: ./src/TaggerAgent.Functions
        host: function
        language: dotnet
    tagger-agent:
        project: ./src/TaggerAgent
        host: azure.ai.agent       # Foundry hosted agent via azd extension
        language: docker
        docker:
            remoteBuild: true
        config:
            container:
                resources:
                    cpu: "1"
                    memory: 2Gi
                scale:
                    maxReplicas: 3
                    minReplicas: 1
            deployments:
                - model:
                    format: OpenAI
                    name: gpt-4o
                    version: "2024-08-06"
                  name: gpt-4o
                  sku:
                    capacity: 30
                    name: Standard
infra:
    provider: bicep
    path: ./infra
```

---

## 7. Key Decisions

| # | Decision | Rationale |
| --- | --- | --- |
| 1 | **Hosted agent, not prompt agent** | Need custom C# code for Azure SDK calls. Prompt agents cannot execute arbitrary code. |
| 2 | **Microsoft Agent Framework** | Higher-level abstraction with automatic tool registration, function calling loop, and ASP.NET Core hosting. Agent Framework handles tool discovery from C# methods, parameter marshaling, and OpenAI-compatible API exposure. Reduces boilerplate while maintaining full control over business logic. |
| 3 | **Azure Resource Graph for scanning** | Fast, subscription-wide KQL queries with tag data. Avoids N+1 API calls. |
| 4 | **Rules as natural language assessments in Blob Storage** | LLM can reason over natural language rules — inference, inheritance, fuzzy matching. Rigid JSON schemas would just replicate Azure Policy. Rules stay in blob for editability without redeployment. |
| 5 | **Tag Contributor role, not Contributor** | Least-privilege. Agent can only modify tags, not resource config. |
| 6 | **No App Service** | Foundry hosted agents run on Foundry-managed infrastructure. App Service would be redundant. |
| 7 | **gpt-4o model** | Good balance of cost and tool-calling capability. Sufficient for structured tagging decisions. |
| 8 | **Responses protocol** | Standard Foundry protocol for hosted agents. Clean request/response model. |
| 9 | **System-assigned identity for agent** | Foundry account's system-assigned managed identity is used by the hosted agent. Function retains a user-assigned identity for explicit RBAC. |
| 10 | **Merge-only tag operations** | Never remove existing tags. `TagsPatchResource` with `Merge` ensures additive-only behavior. |
| 11 | **Two execution modes** | Interactive (confirm-before-apply) for human users. Automated (confidence-based auto-apply) for timer. Same agent, different invocation message. |
| 12 | **Confidence-based auto-apply** | In automated mode, only apply when both the rule allows it (`autoApply`) and the agent's confidence is high. When in doubt, report. Safety without bottleneck. |
| 13 | **Azure Function for timer** | Flex Consumption plan, timer-triggered. Calls agent via Foundry SDK. Separate identity with minimal RBAC (Cognitive Services User). |
| 14 | **Audit table for all changes** | Both modes log to Storage Table. Compliance trail, rollback analysis, operational visibility. |
| 15 | **User-owned rules, no defaults** | The tool is a framework, not an opinionated policy. No seed/default rules shipped in the repo. Users define their own tagging rules, either manually or with the agent's help via conversation. |
| 16 | **Subscription-scoped rules** | Rules are persisted per-subscription in Blob Storage (`rules/{subscription-id}/rules.json`). Different subscriptions can have different policies (prod vs dev vs sandbox). The agent loads the correct ruleset based on the target subscription. |
| 17 | **Agent as rule manager** | The agent can help users create, update, and copy rules interactively. `SaveTaggingRules` persists user-defined rules; `CopyTaggingRules` copies between subscriptions. This is critical since there are no defaults — the agent IS the onboarding path for new subscriptions. |
| 18 | **Blob Data Contributor for agent** | Agent application's identity upgraded from `Storage Blob Data Reader` to `Storage Blob Data Contributor` to support writing rules via the `SaveTaggingRules` tool. |

---

## 8. Automated Execution (Timer Pattern)

### Architecture

An Azure Function on a timer schedule invokes the agent for automated tagging
scans. The Function does not contain tagging logic — it is a thin trigger that
calls the Foundry agent API and collects the response.

```text
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│   Azure Functions (Flex Consumption)                                │
│   ┌─────────────────────────────────────────────────────────────┐   │
│   │  TimerScanFunction                                          │   │
│   │  Schedule: 0 0 2 * * *  (daily at 2:00 AM UTC)              │   │
│   │                                                             │   │
│   │  1. Invoke agent via Foundry API (automated mode message)   │   │
│   │  2. Parse agent response (JSON report)                      │   │
│   │  3. Write summary to audit table                            │   │
│   │  4. (Future) Send notification with pending-review items    │   │
│   └────────────────────────────┬────────────────────────────────┘   │
│                                │                                    │
└────────────────────────────────┼────────────────────────────────────┘
                                 │
                    ┌────────────▼────────────┐
                    │  Foundry Agent API      │
                    │  (agent_invoke)         │
                    └────────────┬────────────┘
                                 │
                    ┌────────────▼────────────┐
                    │  TaggerAgent            │
                    │  (same agent as         │
                    │   interactive mode)     │
                    └─────────────────────────┘
```

### How the Function invokes the agent

The Function uses the Foundry SDK (`Azure.AI.Agents.Persistent`) with its own
managed identity to call the agent:

```csharp
using Azure.AI.Agents.Persistent;

var client = new PersistentAgentsClient(
    new Uri(foundryEndpoint),
    new DefaultAzureCredential());

var agent = await client.GetAIAgentAsync("tagger-agent");

var response = await agent.RunAsync($"""
    Run automated tagging scan for subscription {subscriptionId}.
    Load the tagging rules for this subscription from Blob Storage.
    If no rules exist for this subscription, skip the scan and report
    that no rules are configured.
    Apply all changes where the rule has autoApply=true and your
    confidence is high.
    Do not apply changes where confidence is low or the rule has
    autoApply=false.
    Output a JSON report of all changes (applied and pending-review).
    """);
```

### Schedule

Default: daily at 2:00 AM UTC (`0 0 2 * * *`). Configurable via the
`TIMER_SCHEDULE` application setting on the Function App. The schedule uses
NCrontab format (6 fields: second, minute, hour, day, month, day-of-week).

### Why Azure Functions, not a Logic App or durable workflow

- **Flex Consumption plan**: Per-execution billing with zero cost when idle. The
  scan runs once a day for a few minutes — ideal for this workload. Flex
  Consumption also supports VNet integration and optional always-ready instances
  if cold-start latency ever becomes a concern.
- **C# consistency**: Same language as the agent. Shared models if needed.
- **Minimal code**: The Function is ~30 lines. It is a trigger, not a workflow.
- **azd integration**: Functions deploy naturally alongside the agent via `azd`.

### Future: notification pipeline

The timer pattern can be extended with notifications for pending-review items:

- Email via Azure Communication Services or SendGrid.
- Teams message via Microsoft Graph webhook.
- Azure DevOps work item creation.

These are out of scope for v1 but the audit table provides the data source.

---

## Appendix: NuGet Packages

### Agent container (src/TaggerAgent)

| Package | Purpose |
| --- | --- |
| `Azure.Identity` | `DefaultAzureCredential` for all Azure auth |
| `Azure.ResourceManager` | ARM client for tag operations |
| `Azure.ResourceManager.ResourceGraph` | Resource Graph queries |
| `Azure.Storage.Blobs` | Read tagging rules from Blob Storage |
| `Azure.Data.Tables` | Write audit log to Azure Table Storage |
| `Microsoft.Agents.AI.AzureAI.Persistent` (prerelease) | Foundry persistent agent integration |
| `Azure.AI.Agents.Persistent` | Underlying PersistentAgentsClient |
| `Microsoft.Agents.AI.Hosting` | ASP.NET Core hosting for agent API |
| `Microsoft.Extensions.AI` | IChatClient abstraction |
| `Microsoft.Extensions.Hosting` | ASP.NET hosting infrastructure |

### Function App (src/TaggerAgent.Functions)

| Package | Purpose |
| --- | --- |
| `Azure.Identity` | `DefaultAzureCredential` for Foundry auth |
| `Azure.AI.Agents.Persistent` | Invoke agent via Foundry API |
| `Azure.Data.Tables` | Read audit log for reporting |
| `Microsoft.Azure.Functions.Worker` | Functions isolated worker runtime |
| `Microsoft.Azure.Functions.Worker.Extensions.Timer` | Timer trigger binding |
