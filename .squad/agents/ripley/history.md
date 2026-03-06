# Ripley — History

## Project Context

- **Project:** taggeragent — An Azure resource tagging agent using Foundry hosted agents
- **Stack:** C#, Azure Foundry Hosted Agents, azd, Bicep
- **What it does:** Scans Azure resources across a subscription and helps tag them according to rules (rules TBD)
- **User:** Matthew Henderson

## Learnings

### 2025-07-18
- Defined shared environment variable contract in `src/TaggerAgent/EnvironmentConfig.cs` as C# constants (single source of truth). Updated `docs/architecture.md` section 5. Standardized on azd convention names: `AZURE_AI_PROJECT_ENDPOINT` (was `FOUNDRY_ENDPOINT`), `STORAGE_ACCOUNT_NAME` (was `AUDIT_STORAGE_ACCOUNT_NAME`), and added missing `RULES_STORAGE_URL`. Wrote detailed reconciliation instructions for Dallas (code) and Parker (infra) to align their implementations. Designed subscription-scoped rules architecture with `rules/{subscription-id}/rules.json` pattern. Deprecated default rules; tool is framework not policy. Upgraded agent identity to `Storage Blob Data Contributor` and added `save_tagging_rules` and `copy_tagging_rules` tools for user rule management. Updated `docs/architecture.md` (sections 2, 4, 5, 6, 7, 8). Rules redesign enables user-owned tagging and multi-subscription support.

### 2025-07-18 — Environment Variable Contract

- **Decision:** Created canonical env var contract in `src/TaggerAgent/EnvironmentConfig.cs` as C# constants class — single source of truth for all env var names.
- **Key renames:** `FOUNDRY_ENDPOINT` → `AZURE_AI_PROJECT_ENDPOINT` (azd convention), `AUDIT_STORAGE_ACCOUNT_NAME` → `STORAGE_ACCOUNT_NAME` (match infra).
- **New infra output:** `RULES_STORAGE_URL` — was completely missing from Bicep. Piped from `storage.outputs.blobEndpoint`.
- **Rule: code adapts to azd conventions.** When code and infra disagree on names, azd/Foundry conventions win. Code changes to match.
- **Agent container env vars:** Unlike Function Apps (which have Bicep app settings), hosted agent containers get env vars from Foundry runtime injection, azd extension, or agent definition YAML. Parker must confirm which are auto-injected.
- **Architecture doc updated:** `docs/architecture.md` section 5 now has an "Environment Variable Contract" subsection.
- **Decision doc:** `.squad/decisions/inbox/ripley-env-var-contract.md` — includes exact instructions for Dallas (code) and Parker (infra).
- **Key files:** `src/TaggerAgent/EnvironmentConfig.cs`, `docs/architecture.md`, `.squad/decisions/inbox/ripley-env-var-contract.md`

### 2025-07-18 — Architecture Design

- **Architecture doc:** `docs/architecture.md` — full system design
- **Agent type:** Foundry hosted agent (container-based, C#, `responses` protocol)
- **Model:** gpt-4.1 for tool-calling workflows
- **Resource scanning:** Azure Resource Graph SDK (`Azure.ResourceManager.ResourceGraph`)
- **Tag application:** ARM Tags API with Merge-only operations
- **Rules storage:** JSON in Azure Blob Storage — decoupled from code
- **Identity:** User-assigned managed identity with Tag Contributor + Reader at subscription scope
- **No App Service:** Foundry manages hosting; ACR holds the container image
- **Key NuGet packages:** Azure.Identity, Azure.ResourceManager, Azure.ResourceManager.ResourceGraph, Azure.Storage.Blobs, Azure.AI.Projects
- **azd structure:** `infra/` (Bicep with AVM modules), `src/TaggerAgent/` (C# agent), `rules/` (default rules JSON), `tests/`
- **User preference:** Matthew prefers identity over secrets, American English, azd deployment, Bicep with AVM, mermaid for diagrams

### 2025-07-18 — Rules and Timer Architecture Decisions

- **Rules v2:** Shifted from rigid JSON schema (v1) to natural language assessment criteria (v2). Rules are still JSON in Blob Storage, but the `assessment` field is natural language the LLM reasons over, not a schema the code validates. This is the whole point of using an LLM — if we just pattern-matched, we'd use Azure Policy.
- **Two execution modes:** Interactive (confirm-before-apply, for human users) and Automated (confidence-based auto-apply, for timer). Same agent definition, different invocation message. The mode is determined by the caller, not by config.
- **Confidence model:** Two-factor — rule author's `autoApply` flag + agent's own confidence assessment. Both must agree for auto-apply in automated mode.
- **Azure Function timer:** Consumption plan, daily at 2 AM UTC. Thin trigger (~30 lines) that calls agent via Foundry SDK. Separate managed identity with `Cognitive Services User` role.
- **Audit table:** `TagAuditLog` in Azure Table Storage. Both execution modes log all changes (applied and skipped). Compliance trail.
- **New infra:** `function-app.bicep` module, second managed identity for the Function, `Storage Table Data Contributor` role for agent, `Cognitive Services User` for Function.
- **New project:** `src/TaggerAgent.Functions/` — C# Azure Functions project with `TimerScanFunction.cs`.
- **Matthew's preference:** Wants rules to be "things the agent assesses," not rigid schema validation. Wants automated timer-based execution. Comfortable with confidence-based auto-apply as long as there's an audit trail.

### 2025-07-18 — Microsoft Agent Framework Adoption

- **SDK change:** Replaced `Azure.AI.Projects` with Microsoft Agent Framework (`Microsoft.Agents.AI.*`) across architecture documentation.
- **Key packages for Foundry persistent agents:** `Microsoft.Agents.AI.AzureAI.Persistent` (prerelease), `Azure.AI.Agents.Persistent`, `Microsoft.Agents.AI.Hosting`, `Microsoft.Extensions.AI`.
- **Pattern shift:** Agent Framework handles tool registration automatically from C# methods. Tools are discovered via attributes/conventions rather than manual JSON tool definitions. The framework manages the tool-calling loop, parameter marshaling, and OpenAI-compatible API hosting.
- **Agent creation:** Use `PersistentAgentsClient` to connect to Foundry, create `AIAgent` instances. The agent container runs ASP.NET Core with the Agent Framework hosting library.
- **Function invocation:** Timer function uses `Azure.AI.Agents.Persistent` to invoke the agent via `agent.RunAsync()` instead of lower-level `AgentsClient.InvokeAgentAsync()`.
- **Rationale:** Higher-level abstraction reduces boilerplate while maintaining full control over business logic. Agent Framework handles infrastructure concerns (tool discovery, function loop, hosting) so we can focus on domain logic (Resource Graph queries, tag application, rules assessment).
- **Architecture stability:** No changes to the overall system architecture, infrastructure, RBAC model, or rules system — this is purely an SDK/framework choice for how the C# agent is implemented.

### 2025-07-18 — User-Owned Rules and Subscription Scope

- **No default rules:** Matthew explicitly does NOT want seed/default rules shipped with the repo. Removed `rules/` folder from project structure and all references to deploying seed rules at `azd up` time.
- **Subscription-scoped rules:** Rules persisted per-subscription at `rules/{subscription-id}/rules.json` in Blob Storage. Different subscriptions can have completely different tagging policies.
- **Agent as rule manager:** Added `save_tagging_rules` and `copy_tagging_rules` tools. The agent can help users create rules interactively via conversation — critical since there are no defaults.
- **RBAC change:** Agent identity upgraded from `Storage Blob Data Reader` to `Storage Blob Data Contributor` to support writing rules.
- **Empty ruleset handling:** Agent informs user and offers to help define rules. In automated mode, timer scans skip gracefully if no rules exist.
- **Matthew's philosophy:** The tool is a framework, not an opinionated policy. "I kind of want the prompt to help manage those rules" — the agent is the onboarding path.
- **Key files updated:** `docs/architecture.md` (sections 2, 4, 5, 6, 7, 8)
- **Decision doc:** `.squad/decisions/inbox/ripley-rules-user-control.md`

### 2025-07-18 — Agent Prompt Polish for Production

- **Polished system prompt** in `src/TaggerAgent/Agent/AgentInstructions.cs` for production readiness
- **Tool name corrections:** Updated all tool references from snake_case (`scan_resources`, `apply_tags`) to PascalCase C# method names (`ScanResources`, `ApplyTags`, `GetTaggingRules`, `SaveTaggingRules`, `CopyTaggingRules`) to match actual Agent Framework tool registration
- **Ad hoc tagging guidance:** Added explicit section explaining that rules are optional — users can request one-off tag operations without creating rules first. Examples: "tag this VM with owner:finance" or "add cost-center to all resources in this RG"
- **Improved output format section:** Added specific guidance for scan results (tables with resource counts), proposed changes (confidence-grouped with reasons), applied changes (structured JSON for audit), and error reporting (never silently skip failures)
- **Common user intents:** Added pattern recognition for typical requests like "tag all my VMs", "show untagged resources", "set up environment tagging", "copy rules to another subscription"
- **Error handling guidance:** Added specific instructions for handling partial failures, missing rules, permission errors, and uncertain assessments. Agent should report partial success with details, never retry automatically
- **Workflow clarity:** Distinguished rule-based (recurring patterns) vs ad hoc (one-off requests) tagging workflows to guide the agent's decision on whether to load rules
- **Key insight:** The Microsoft Agent Framework automatically discovers tools from C# method names, so prompt tool names must match exactly. Previous snake_case names would have caused tool-calling failures
- **Production-ready:** Prompt is now concise, well-structured, covers error cases, and aligns with actual tool signatures

### 2025-07-18 — Documentation Cleanup: Tool Naming and Environment Variables

- **Scope:** Systematic review and fix of three project-facing markdown files (README.md, docs/architecture.md, tests/TaggerAgent.Tests/README.md)
- **Findings:**
  - **Systematic tool naming drift in architecture.md**: All 5 tools documented in snake_case throughout the ~36.4 KB file, appearing in tool table (lines 129-133), inline references in rules sections, and key decisions table. Actual C# code uses PascalCase (`ScanResources`, `ApplyTags`, `GetTaggingRules`, `SaveTaggingRules`, `CopyTaggingRules`). This inconsistency created confusion and would cause documentation-to-code misalignment.
  - **Outdated environment variables in README.md**: `FOUNDRY_ENDPOINT` and `FOUNDRY_AGENT_NAME` are not current. Correct names are `AZURE_AI_PROJECT_ENDPOINT` (azd convention) and `STORAGE_ACCOUNT_NAME`.
  - **Vague IaC description**: README.md stated "Bicep modules" without clarifying use of Azure Verified Modules (AVM), which is our actual infrastructure pattern.
  - **Decision table clarity**: architecture.md decision #2 referenced obsolete SDK ("Azure.AI.Projects", now Microsoft Agent Framework); decision #4 needed "assessments" added for consistency with rules philosophy.
- **Fixes applied:** 11 edits across README.md (3) and docs/architecture.md (9), zero changes to tests/TaggerAgent.Tests/README.md (validated as accurate)
  - Converted 5 tools in tool table from snake_case to PascalCase
  - Fixed 6 inline tool references in rules sections and decisions
  - Corrected 2 environment variable names in README.md
  - Enhanced IaC description and decision text for clarity
- **Root cause:** Documentation drift at scale — when code changes (especially SDK/framework decisions), docs scattered across multiple files fall out of sync. Architecture.md was last updated for Agent Framework adoption but missed systematic tool naming in the tool table.
- **Lesson learned:** Per-section review during semantic changes catches inconsistencies. Tool names should be treated as part of the "API contract" and reviewed whenever agent implementation changes.
- **Commit:** `126f839` — docs: fix tool naming and environment variable references
- **Key files:** README.md (lines 39, 103-104), docs/architecture.md (11 edits), .squad/agents/ripley/history.md (this section)

### 2025-01-05 — Model Default: Switch to gpt-4o-mini

- **Problem:** Project hardcoded `gpt-4o` as default model, causing deployment failures on subscriptions without gpt-4o access (e.g., westus2 only had `gpt-oss-120b` available).
- **Decision:** Switched default model from `gpt-4o` to `gpt-4o-mini`.
- **Rationale:**
  - **Wider availability** — gpt-4o-mini is available on most Azure subscriptions, including basic/free tiers. Ensures `azd up` succeeds out of the box.
  - **Sufficient capability** — TaggerAgent performs structured, tool-heavy workflows (Resource Graph queries, tag operations, rule evaluation). Doesn't need gpt-4o's advanced reasoning. Needs reliable function calling — gpt-4o-mini excels at this.
  - **Cost efficiency** — ~60x cheaper than gpt-4o. For automated daily timer scans across large subscriptions, this is significant.
  - **Production pattern** — Many production AI agents use -mini models for structured tasks.
  - **Easy override** — Users who want gpt-4o can edit `azure.yaml` and `agent.yaml`, then redeploy.
- **Files changed:**
  - `azure.yaml` — model.name: `gpt-4o` → `gpt-4o-mini`, version: `2024-08-06` → `2024-07-18`
  - `src/TaggerAgent/agent.yaml` — resources[0].id: `gpt-4o` → `gpt-4o-mini`
  - `README.md` — Updated tech stack reference, added Model Configuration subsection with override instructions
  - `docs/architecture.md` — Updated model name in overview diagram
- **Alternative rejected:** Keep gpt-4o as default + document overrides. Bad first-run experience (deployment fails), unnecessary cost for this workload.
- **Key insight:** Default should optimize for "works out of the box," not "maximum capability." The -mini suffix doesn't mean "toy model" — it means "optimized for structured tasks." For tagging workflows with function calling, -mini is the right choice.
- **Decision doc:** `.squad/decisions/inbox/ripley-model-default.md`

### 2026-03-06 — Squad Places Enlistment

- **Activity:** Enlisted squad on Squad Places social network as "MU-TH-UR 6000"
- **Published:** capabilityHosts Bicep fix lesson post with code walkthrough
- **Engagement:** Commented on 2 peer squads' Azure infrastructure posts
- **Objective:** Build community visibility for taggeragent and establish cross-squad knowledge sharing
- **Outcome:** Squad profile active, lesson published, social footprint established
- **Decision Recorded:** `.squad/decisions.md` — Squad Places Enlistment (2026-03-06)

### 2026-07-15 — Flex Consumption Deployment Fix

- **Problem:** Azure Functions deployment failing with `[KuduSpecializer] Kudu has been restarted after package deployed`. Root cause: Flex Consumption (FC1) uses OneDeploy and blob-based deployment, which has known incompatibilities with `azd` (tracked in azure-dev#3658). Kudu is recycled rapidly after deployment, causing azd's status polling to fail with 404s.
- **Decision:** Switch from Flex Consumption (FC1, Linux) to Standard App Service Plan (S1, Windows).
- **Rationale:** (1) User explicitly prefers Windows + Standard plan. (2) Deployment reliability > cost optimization. (3) Daily timer trigger doesn't need Flex's scale-to-zero. (4) S1 + Windows is the most battle-tested azd deployment path.
- **Alternatives considered:** Y1 Consumption (viable but no always-on), B1 Basic (~$13/mo, good budget option), fixing Flex deployment (rejected — azd support immature).
- **Changes scoped to single file:** `infra/modules/function-app.bicep`. SKU from FC1/FlexConsumption to S1/Standard, kind from `functionapp,linux` to `functionapp`, remove entire `functionAppConfig` block (Flex-only), add `FUNCTIONS_EXTENSION_VERSION`, `FUNCTIONS_WORKER_RUNTIME`, `alwaysOn`, `netFrameworkVersion`, `use32BitWorkerProcess`. All other infra files unchanged.
- **Key learning:** Flex Consumption requires OneDeploy (not zip deploy). azd defaults to zip deploy for function apps. This mismatch is the root cause. Standard/Dedicated plans use zip deploy, which azd handles natively.
- **Cost:** S1 ~$73/mo. Can downgrade to B1 (~$13/mo) with one-line SKU change if needed.
- **Assignment:** Parker implements. File: `infra/modules/function-app.bicep`.
- **Decision doc:** `.squad/decisions/inbox/ripley-flex-consumption-fix.md`
- **Status:** ✅ IMPLEMENTED (2026-03-06). Parker successfully applied Bicep changes (commit 284cc73). Orchestration logs at `.squad/orchestration-log/2026-03-06-ripley-flex-fix.md` and `2026-03-06-parker-bicep-impl.md`. Session log at `.squad/log/2026-03-06-flex-consumption-fix.md`.
