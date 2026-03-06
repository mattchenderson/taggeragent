# TaggerAgent — Decisions Log

## User Directives

### 2026-03-05T23:34Z: Rules Ownership & Scoping
**By:** Matthew Henderson (via Copilot)

1. Remove default/seed rules from the repo. Users should define their own rules — no opinionated defaults shipped.
2. Rule persistence should be tied to the target scope (e.g., subscription ID), not global. Different subscriptions may have different tagging policies.

**Rationale:** User wants full control over tagging policy. Rules are organizational decisions, not tool defaults.

---

### 2026-03-05T23:27Z: Use Agent Framework
**By:** Matthew Henderson (via Copilot)

Use Microsoft Agent Framework (`Microsoft.Agents.AI.*`) instead of `Azure.AI.Projects` for the agent implementation.

**Key packages:**
- `Microsoft.Agents.AI.AzureAI.Persistent`
- `Microsoft.Agents.AI.Hosting`
- `Azure.AI.Agents.Persistent`

**Rationale:** Agent Framework provides higher-level abstractions (`AIAgent`, built-in hosting, function tools) better suited for the hosted agent pattern.

---

## Architecture Decisions

### 2025-07-18: TaggerAgent Architecture
**Author:** Ripley (Lead)  
**Status:** Established

1. **Hosted agent over prompt agent** — Need custom C# code for Azure SDK calls. Prompt agents can't run arbitrary code.
2. **No App Service** — Foundry hosted agents run on Foundry-managed infrastructure (Container Apps). ACR holds the image; Foundry pulls and runs it.
3. **Azure Resource Graph for scanning** — Fast KQL queries across all resources in a subscription. Includes tag data in results.
4. **Tag Contributor role (not Contributor)** — Least-privilege. Agent can modify tags but cannot touch resource configuration.
5. **Rules as JSON in Blob Storage** — Decouples tagging policy from agent code. Users update rules without redeployment.
6. **Merge-only tag writes** — Never remove existing tags. Uses ARM Tags API with `Merge` operation.
7. **Confirmation before apply** — Agent always proposes changes first. No silent writes.
8. **gpt-4o model** — (Updated from gpt-4.1; latest stable available) Balances cost and tool-calling quality for structured tagging decisions.

---

### 2025-07-18: Microsoft Agent Framework for Foundry Agent
**Author:** Ripley (Lead)  
**Status:** Active (User-approved)

**Decision:** Use Microsoft Agent Framework instead of lower-level Azure.AI.Projects SDK.

**Rationale:**
- Higher-level abstraction — framework handles tool discovery, function calling loop, API hosting automatically
- Less boilerplate — no manual JSON tool definitions; tools discovered from C# method signatures
- Better ASP.NET Core integration via `Microsoft.Agents.AI.Hosting`
- Recommended path forward by Microsoft for Foundry agents

**Consequences:**
- Positive: Reduced code complexity, automatic tool registration, better alignment with Microsoft patterns
- Negative: Prerelease dependency (`Microsoft.Agents.AI.AzureAI.Persistent`)
- Neutral: No change to infrastructure, RBAC model, or operational behavior

---

## Infrastructure Decisions

### 2025-01-05: Infrastructure Scaffolding
**Author:** Parker  
**Status:** Implemented

**Context:** TaggerAgent requires Foundry hosted agent, managed identities, subscription-scoped RBAC, storage for rules and audit log, timer-triggered Function App, and container registry.

**Decision:** Subscription-scoped Bicep infrastructure with modular design.

**Implementation:**
- `main.bicep` with `targetScope = 'subscription'` to enable subscription-scoped role assignments
- Separate modules for identity, storage, ACR, Foundry, model deployment, RBAC, and function app
- All authentication via managed identity (no shared keys/secrets)
- azd integration via `azure.yaml` with two services

**Alternatives considered:**
- Resource group scoped deployment — would require manual subscription-level assignments
- Single managed identity — violates least privilege principle
- Consumption plan for Functions — Flex Consumption chosen for cold-start performance

---

### 2025-07-17: Use Flex Consumption Plan for Timer Function
**Author:** Ripley (Lead)  
**Requested by:** Matthew Henderson  
**Status:** Accepted

**Decision:** Use Azure Functions Flex Consumption plan instead of regular Consumption.

**Rationale:**
- Timer triggers are supported
- Per-execution billing with zero cost when idle (same economic model as Consumption)
- VNet integration available out of box
- Optional always-ready instances can eliminate cold-start latency
- Linux Consumption retiring September 2028; Flex Consumption is the forward path

---

## Code Architecture Decisions

### 2025-01-XX: C# Code Scaffold — Service-Oriented Architecture
**Author:** Dallas  
**Status:** Complete

**Decision:** Service-oriented architecture with clear separation of concerns.

**Implementation:**

1. **TaggerAgent (hosted agent):**
   - Agent orchestrator with TODO placeholders for Foundry SDK (responses protocol)
   - Three tool implementations (scan, apply tags, get rules)
   - Four service classes (ResourceGraph, Tagging, Rules, Audit)
   - Models as C# records for immutability
   - Dockerfile with multi-stage build

2. **TaggerAgent.Functions:**
   - Timer trigger function for automated scans
   - TODO placeholders for Foundry agent invocation
   - Default schedule: daily at 2 AM UTC

3. **Rules seed data:**
   - v2 format with natural language assessment criteria
   - Example rules (environment, owner, cost-center, data-classification)

**Patterns:**
- DefaultAzureCredential throughout (zero secrets in code)
- Merge semantics for tags (never remove existing tags)
- TODO markers for Foundry SDK (API still evolving)
- Service-based DI for clean separation
- Records for models (immutable, modern C#)

**Alternatives considered:**
- Newtonsoft.Json → rejected for System.Text.Json
- Classes instead of records → rejected (records provide value equality)
- Direct ARM calls → rejected (SDK handles auth, retries, pagination)

---

## Testing Decisions

### 2025-01-18: Test Framework & Patterns
**Author:** Kane (Tester)  
**Status:** Implemented

**Decision:** xUnit + Moq + FluentAssertions for comprehensive test coverage.

**Framework choices:**
- **Test Framework:** xUnit (modern .NET standard, better async support)
- **Mocking:** Moq (industry standard, good Azure SDK interface support)
- **Assertions:** FluentAssertions (readable, chainable, better error messages)

**Test patterns:**
- Naming convention: `MethodName_Condition_ExpectedResult`
- Structure: Arrange-Act-Assert with explicit comments
- Integration test markers: `[Trait("Category", "Integration")]` (skipped in CI by default)

**Critical test scenarios:**
1. Pagination handling (Resource Graph max 1000/page, SkipToken continuation)
2. Throttling and retries (429 with exponential backoff, respect Retry-After)
3. Authorization errors (403 graceful handling)
4. Locked resources (409 conflicts in batch operations)
5. Merge-only tag semantics (PATCH not PUT, preserve unrelated tags)
6. Audit logging (PartitionKey=subscriptionId, RowKey={resource}_{tag}_{timestamp})
7. Large result sets (1500+ resources)
8. Partial failures (some resources succeed, others fail)

---

## Rules & Tagging Decisions

### 2025-07-18: Rules Format & Two Execution Modes
**Author:** Ripley (Lead)  
**Requested by:** Matthew Henderson  
**Status:** Proposed

**Decisions:**

1. **Rules format: v1 (schema) → v2 (natural language assessments)**
   - Rules remain in Blob Storage as JSON
   - Shift from rigid schema validation to natural language assessment criteria
   - Each rule has: `assessment` field (for LLM reasoning), `autoApply` flag, `confidence` hint

2. **Two execution modes: Interactive + Automated**
   - **Interactive:** User confirms all changes before apply
   - **Automated:** Agent auto-applies high-confidence changes where `autoApply=true`; everything else goes to pending-review report
   - Confidence is two-factor: rule author's `autoApply` flag AND agent's own confidence must both be high

3. **Audit table for all changes**
   - `TagAuditLog` in Azure Table Storage
   - Both modes log all changes for compliance trail and rollback

**Rationale:** LLM adds value through inference (naming conventions), inheritance (resource group tags), and contextual reasoning — can't be replaced by pattern matching.

**Impact:**
- Infra: Add function-app.bicep, second managed identity, new RBAC assignments
- Code: New `src/TaggerAgent.Functions/` project, new `AuditService.cs` and `AuditEntry.cs`
- Rules: `rules/default-rules.json` updated to v2 format
- azd: `azure.yaml` gets a `functions` service entry

**Alternatives considered:**
- Rules in system prompt — rejected (harder to version, audit, update without redeployment)
- Auto-apply everything — rejected (too risky without confidence gating)
- Approval queue (no auto-apply) — deferred (confidence-based model is simpler for v1)
- Logic App instead of Functions — rejected (more complexity for a simple timer trigger)

---

---

### 2026-03-05T23:56:00Z: User Directive — Agent Application Identity Terminology
**By:** Matthew Henderson (via Copilot)

Use "agent application's identity" instead of "agent identity" in documentation to avoid confusion with Entra Agent Identity.

---

### 2025-07-14: Agent Framework Rework Complete
**Author:** Dallas (Core Dev)  
**Status:** Implemented

Replaced `Azure.AI.Projects` SDK with Microsoft Agent Framework (`Microsoft.Agents.AI.*`) packages across TaggerAgent and TaggerAgent.Functions projects.

**Changes:**
- `TaggerAgent.csproj` — 5 new Agent Framework packages; `Microsoft.Extensions.Hosting` bumped to 10.0.0
- `Agent/TaggerAgent.cs` — Rewritten as `TaggerAgentTools` with tool methods using `[Description]` attributes
- `Program.cs` — Changed to `WebApplication.CreateBuilder` with `AddAIAgent`, `AddOpenAIResponses`, `MapOpenAIResponses`
- `TimerScanFunction.cs` — Now uses `PersistentAgentsClient` with `CreateAIAgentAsync`/`RunAsync` pattern
- `TaggerAgent.Functions.csproj` — Added `Azure.AI.Agents.Persistent` and `Microsoft.Agents.AI.AzureAI.Persistent`

**Impact on other agents:**
- **Kane (Tests):** Test project needs updates for new API (TaggerAgentTools class, constructor signatures, tool method signatures)
- **Parker (Infra):** No changes needed
- **Ripley (Architect):** No doc updates needed

---

### 2025-07-24: Adopt azd ai agent Extension for Foundry Hosted Agent Deployment
**Author:** Parker (Infra/DevOps)  
**Status:** Implemented

Reworked infrastructure to use the `azd ai agent` extension (preview) to eliminate boilerplate.

**Changes:**
- `azure.yaml` — Changed agent service from `host: containerapp` to `host: azure.ai.agent` with inline `config` block and `requiredVersions`
- `src/TaggerAgent/agent.yaml` — Created agent definition with `kind: hosted`, `responses` protocol, environment variables, and model resource
- Bicep consolidation — Merged `foundry.bicep`, `acr.bicep`, and `model-deployment.bicep` into single `foundry.bicep`
- Identity simplification — Agent now authenticates via Foundry account's system-assigned managed identity (removed manual agent-identity user-assigned identity)
- RBAC cleanup — Removed AcrPull assignment (extension handles ACR); kept Reader, Tag Contributor, Cognitive Services User, Storage Table Data Reader roles
- Extension parameters — `main.bicep` now accepts `aiProjectDeploymentsJson`, `aiProjectConnectionsJson`, `aiProjectDependentResourcesJson`, and `enableHostedAgents`

**Consequences:**
- Deployment requires `azd extension install azure.ai.agents` (>= 0.1.0-preview)
- Location must be `northcentralus`
- Model deployments now declarative in `azure.yaml`; Bicep receives as JSON
- Container image build/push and agent registration fully managed by extension
- Functions timer service remains independently deployed

---

### 2025-07-18: Environment Variable Contract
**Author:** Ripley (Lead)  
**Status:** Active
**Requested by:** Matthew Henderson

Defined canonical environment variable names in `src/TaggerAgent/EnvironmentConfig.cs` as C# constants (single source of truth).

**Canonical variables:**
| Variable | Used By | Notes |
| --- | --- | --- |
| `AZURE_AI_PROJECT_ENDPOINT` | Agent, Function | azd convention name (was `FOUNDRY_ENDPOINT`) |
| `AZURE_SUBSCRIPTION_ID` | Agent, Function | azd convention name |
| `AZURE_CLIENT_ID` | Function only | Managed identity |
| `RULES_STORAGE_URL` | Agent only | Blob service endpoint (was missing from infra) |
| `STORAGE_ACCOUNT_NAME` | Agent, Function | Shared storage account (was `AUDIT_STORAGE_ACCOUNT_NAME`) |
| `AGENT_NAME` | Function only | Default: `tagger-agent` |
| `TIMER_SCHEDULE` | Function only | NCRONTAB cron expression |

**Rule: Code adapts to azd conventions.** All naming follows azd/Foundry standards.

**Instructions for Dallas (code):** Update `Program.cs` and `TimerScanFunction.cs` to use constants from `EnvironmentConfig.cs` and rename env var references to match canonical names.

**Instructions for Parker (infra):** Rename `FOUNDRY_ENDPOINT` to `AZURE_AI_PROJECT_ENDPOINT` in function-app.bicep app settings; add new `RULES_STORAGE_URL` output from blob endpoint; confirm agent container env var mapping.

---

### 2025-07-18: User-Owned Rules with Subscription Scope
**Author:** Ripley (Lead)  
**Status:** Active

Rules should be user-owned with no defaults shipped by the tool.

**Decisions:**
- **No default rules:** `rules/` folder removed; no `default-rules.json` deployed at `azd up` time. Agent gracefully handles empty rulesets and helps users create rules interactively.
- **Subscription-scoped rules:** Blob path pattern `rules/{subscription-id}/rules.json`. Each subscription has independent ruleset.
- **Agent as rule manager:** New tools `save_tagging_rules` and `copy_tagging_rules`. Agent identity upgraded to `Storage Blob Data Contributor` (was Reader).
- **Automated mode with no rules:** Timer-triggered scans skip gracefully with "no rules configured" message; no fallback or guessing.

**Impact:**
- `docs/architecture.md` updated (sections 2, 4, 5, 6, 7, 8)
- `rules/` folder removed
- RBAC: `Storage Blob Data Reader` → `Storage Blob Data Contributor`
- Two new tools in agent definition
- Two new test files added

---

### 2026-03-06T00:15:00Z: .NET 10 Upgrade
**By:** Dallas (Core Dev)  
**Requested by:** Matthew Henderson  
**Status:** Complete

Updated entire project from .NET 8 to .NET 10 target framework (net10.0).

**Changes:**
- All 3 project files (.csproj): net8.0 → net10.0
- Dockerfile base images: 8.0 → 10.0
- Azure Functions Worker SDK upgraded to v2.x for .NET 10 compatibility
- All projects build cleanly
- README.md and agent charters updated

---

## Status Summary

- **Architecture:** ✅ Established (Austin-approved Agent Framework update)
- **Infrastructure:** ✅ Implemented (Parker complete; azd ai agent extension integrated)
- **Code:** ✅ Implemented (Dallas complete; Agent Framework rework + .NET 10 upgrade done)
- **Tests:** ✅ In progress (Kane updating test project for API changes)
- **Documentation:** ✅ Current (Ripley complete; architecture.md updated)
- **Next Phase:** Test updates in progress (Kane)
