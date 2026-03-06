# Squad Decisions

## Active Decisions

### Squad Places Enlistment

**Date:** 2026-03-06  
**Author:** Ripley  
**Status:** Implemented

Context: Enlisted squad on Squad Places social network to build community visibility and establish cross-squad knowledge sharing.

**Activity:**
1. Created squad profile as "MU-TH-UR 6000"
2. Published lesson post on capabilityHosts Bicep fix with code walkthrough
3. Engaged with 2 peer squads' Azure infrastructure posts

**Outcome:** Squad profile active, lesson published, social footprint established

---

### Infrastructure Validation — Design Decisions

**Date:** 2025-01-05  
**Author:** Parker (Infra/DevOps)  
**Status:** Implemented

Context: Validated infrastructure for deployment readiness after .NET 10 upgrade.

**Issues Resolved:**
1. Runtime Version — Updated `function-app.bicep` from `version: '8.0'` to `version: '10.0'` to match project target `net10.0`
2. Role Assignment Scoping — Created `storage-roles.bicep` module with resource-scoped RBAC (Blob Data Reader, Table Data Contributor/Reader on storage account instead of subscription scope)
3. Deployment Container — Added `functionDeploymentsContainer` to `storage.bicep` + Storage Blob Data Contributor role assignment
4. Monitoring Stack — Created `monitoring.bicep` module with Log Analytics workspace (PerGB2018, 30-day retention) + Application Insights
5. App Settings Alignment — Updated function app settings to match code: `FOUNDRY_ENDPOINT` → `AZURE_AI_PROJECT_ENDPOINT`, added `RULES_STORAGE_URL` and `APPLICATIONINSIGHTS_CONNECTION_STRING`
6. API Versions — Updated to latest stable (Web 2024-04-01, Storage 2024-01-01)
7. Parameters — Reviewed `main.parameters.json`; correct

**Files Changed:**
- New: `infra/modules/monitoring.bicep`, `infra/modules/storage-roles.bicep`
- Modified: `infra/modules/function-app.bicep`, `infra/modules/storage.bicep`, `infra/modules/role-assignments.bicep`, `infra/main.bicep`

**Status:** APPROVED for deployment. All Bicep builds clean, 0 warnings.

### Agent Prompt Polish for Production

**Date:** 2025-01-05  
**Author:** Ripley  
**Status:** Implemented

Context: System prompt in `src/TaggerAgent/Agent/AgentInstructions.cs` needed production polish for tool signature alignment, workflow clarity, and error handling.

**Problems Addressed:**
1. Tool name mismatch — Prompt referenced snake_case (`scan_resources`) but actual C# methods are PascalCase (`ScanResources`). Fixed all references to match actual method signatures.
2. Ad hoc tagging unclear — Added section explaining rule-based vs. ad hoc tagging with workflow examples
3. Vague output format — Added specific guidance for scan results, proposed changes, applied changes, and error reporting
4. Missing error handling — Added instructions for partial failures, missing rules, permission errors, API failures
5. No user intent patterns — Added common request patterns to help agent recognize workflows

**Changes:**
- Tool names: `scan_resources` → `ScanResources`, `apply_tags` → `ApplyTags`, etc. (all 5 tools)
- New "Tagging workflows" section with ad hoc tagging examples
- Enhanced output format guidance per result type
- Error handling section with specific instructions
- Common user intent patterns

**Files Changed:**
- Modified: `src/TaggerAgent/Agent/AgentInstructions.cs`

**Status:** APPROVED. Tool names prevent runtime failures.

### Rules as natural language assessments (v2)

Rules shifted from rigid JSON schema (v1) to natural language assessment
criteria (v2). Rules are still JSON in Blob Storage, but the `assessment`
field is natural language the LLM reasons over. Decided by Ripley,
2025-07-18. See `decisions/inbox/ripley-rules-and-timer.md`.

### Two execution modes with confidence-based auto-apply

Interactive mode (confirm-before-apply) for human users. Automated mode
(confidence-based auto-apply) for timer-triggered scans. Same agent, different
invocation message. Decided by Ripley, 2025-07-18.

### Azure Function timer for automated scans

Consumption plan Function App, daily at 2 AM UTC. Thin trigger that calls
agent via Foundry SDK. Separate managed identity. Adds `function-app.bicep`
to infra. Decided by Ripley, 2025-07-18.

### User-owned rules, no defaults

The tool ships as a framework, not an opinionated policy. No seed/default rules
in the repo. Users define their own tagging rules per subscription. The agent
helps create rules interactively when none exist. Decided by Ripley, 2025-07-18.
See `decisions/inbox/ripley-rules-user-control.md`.

### Subscription-scoped rules persistence

Rules stored at `rules/{subscription-id}/rules.json` in Blob Storage. Each
subscription has an independent ruleset. Agent loads the correct rules based on
target subscription. `copy_tagging_rules` tool enables cross-subscription
consistency. Decided by Ripley, 2025-07-18.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
