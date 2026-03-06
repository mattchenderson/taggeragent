# Parker — History

## Project Context

- **Project:** taggeragent — An Azure resource tagging agent using Foundry hosted agents
- **Stack:** C#, Azure Foundry Hosted Agents, azd, Bicep
- **What it does:** Scans Azure resources across a subscription and helps tag them according to rules (rules TBD)
- **User:** Matthew Henderson

## Learnings

_Older entries (2025-01-05 through 2025-07-25) have been archived to `history-archive-pre-2026.md` to maintain active history below 15 KB._

### 2026-03-05: Fixed agent.yaml Validation Error

**Problem:** `azd up` failed with error: `template.kind must be one of: [prompt hosted workflow], got ''`

**Root Cause:** The `agent.yaml` was using an `AgentManifest` wrapper structure (with `template:` as a child containing the agent definition), but the azd extension expects a bare `ContainerAgent` object at the root level.

**Incorrect structure:**
```yaml
name: tagger-agent
template:           # <-- This wrapper was the problem
  kind: hosted
  protocols: ...
```

**Correct structure:**
```yaml
kind: hosted        # <-- ContainerAgent directly at root
name: tagger-agent
protocols: ...
```

**Key Learnings:**
- The `agent.yaml` for azd extension (v0.1.13-preview) expects the AgentDefinition (e.g., `ContainerAgent`) directly at root, NOT wrapped in an `AgentManifest` structure
- AgentManifest (with `template:` field) is the full AgentSchema spec, but azd extension wants just the inner ContainerAgent
- The valid `kind` values are: `prompt`, `hosted`, `workflow` (case-sensitive, lowercase)
- When `kind` is at the root, the extension validates it correctly
- Extension version 0.1.13-preview requires this structure; schema may evolve

**Files changed:**
- `src/TaggerAgent/agent.yaml` - Removed `template:` wrapper, moved `kind: hosted` to root level

### 2026-03-06: Fixed KuduSpecializer Deployment Failure — Missing AzureWebJobsStorage

**Problem:** `azd deploy` for the `functions` service failed with:
`[KuduSpecializer] Kudu has been restarted after package deployed`

**Root Cause:** The Function App had NO `AzureWebJobsStorage` configuration. While the deployment storage was correctly configured in `functionAppConfig.deployment.storage` (blob container with managed identity), the Functions **runtime** needs a separate storage connection for internal operations (timer trigger state, lease management, internal queues). Without it, the host can't initialize after package deploy, causing specialization failure.

Additionally, the function identity's storage RBAC roles were insufficient for the Functions runtime:
- Had `Storage Blob Data Contributor` — needs `Storage Blob Data Owner` (for lease management)
- Had `Storage Table Data Reader` — needs `Storage Table Data Contributor` (for timer state)
- Missing `Storage Queue Data Contributor` (for internal queue management)

**Fix (2 files):**

1. `infra/modules/function-app.bicep` — Added identity-based `AzureWebJobsStorage` app settings:
   - `AzureWebJobsStorage__accountName` = storage account name
   - `AzureWebJobsStorage__credential` = `managedidentity`
   - `AzureWebJobsStorage__clientId` = user-assigned identity client ID
   These three settings together enable identity-based storage connection for the Functions runtime when `allowSharedKeyAccess: false`.

2. `infra/modules/storage-roles.bicep` — Upgraded function identity roles:
   - `Storage Blob Data Contributor` → `Storage Blob Data Owner` (`b7e6dc6d-f1e8-4753-8033-0f276bb0955b`)
   - `Storage Table Data Reader` → `Storage Table Data Contributor` (`0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3`)
   - Added `Storage Queue Data Contributor` (`974c5e8b-45b9-4653-ba55-5f855dd0fb88`)

**Verification:** Fresh `azd up` with environment `resourcetagger-fresh` — Functions service deployed successfully with no KuduSpecializer error.

**Key Learnings:**
- Flex Consumption `functionAppConfig.deployment.storage` is for **deploying packages** only — the Functions **runtime** still needs `AzureWebJobsStorage` separately
- With `allowSharedKeyAccess: false`, must use identity-based connection: `__accountName` + `__credential` + `__clientId` (for user-assigned identity)
- Functions runtime requires Storage Blob Data **Owner** (not Contributor) for lease management, plus Queue Data Contributor for internal operations
- The KuduSpecializer error is a symptom of the Functions host failing to start, not a deployment mechanism issue

### 2026-03-06 — Implemented Flex Consumption → Standard App Service Plan

**Assignment from Ripley:** Switch Functions hosting from Flex Consumption (FC1, Linux) to Standard (S1, Windows) due to OneDeploy + blob deployment incompatibilities with azd.

**Implementation Summary:**
1. **Modified file:** `infra/modules/function-app.bicep`
   - SKU: `FC1` → `S1` (Flex Consumption → Standard)
   - Kind: `functionapp,linux` → `functionapp` (switches to Windows)
   - Removed entire `functionAppConfig` block (Flex-only configuration)
   - Added runtime configuration: `FUNCTIONS_EXTENSION_VERSION = '~4'`, `FUNCTIONS_WORKER_RUNTIME = 'dotnet-isolated'`
   - Added siteConfig: `alwaysOn: true`, `netFrameworkVersion: 'v10.0'`, `use32BitWorkerProcess: false`

2. **Validated:** Ran `az bicep build --file infra/main.bicep` — clean build, no errors.

3. **Unchanged files verified:**
   - `azure.yaml` — azd auto-selects zip deploy for Standard plans (correct)
   - `infra/main.bicep` — module reference unchanged
   - `infra/modules/storage.bicep` — function-deployments container remains valid (used by all plan types)
   - `infra/modules/storage-roles.bicep` — RBAC unchanged
   - `infra/modules/identity.bicep` — identity unchanged

4. **Committed:** Deployed changes as commit 284cc73 with decision reference.

**Key Learning:** Flex Consumption uses OneDeploy (proprietary Microsoft blob-based deployment), not standard zip deploy. azd defaults to zip deploy for all function apps — this mismatch causes Kudu recycles and 404 polling errors. Standard/Dedicated plans use zip deploy natively, aligning with azd expectations.

**Cost Implications:** S1 ~$73/mo (can downgrade to B1 at ~$13/mo with one-line SKU change if needed post-validation).

**Cross-Agent Context:** Ripley's root cause analysis (azure-dev#3658) was decisive. Implementation followed Ripley's exact specification. Bicep validates clean and ready for deployment testing.

**Orchestration Log:** `.squad/orchestration-log/2026-03-06-parker-bicep-impl.md`

### 2025-07-25: Investigated azd Deploy Timeout for Hosted Agent

**Problem:** `azd deploy` timed out after 10 minutes at "Starting agent container" step. First ACR remote build for .NET 10 with prerelease NuGet packages is slow.

**Root Cause:** The 10-minute timeout is **hardcoded** in the azd `azure.ai.agents` extension (`service_target_agent.go`):
- `const waitForReady = true`
- `const maxWaitTime = 10 * time.Minute`

There is no config override — no env var, no azure.yaml field, no CLI flag. No open issues or PRs on Azure/azure-dev for this.

**`minReplicas: 0` does NOT help.** The extension always calls `startAgentContainer()` for hosted agents. Setting minReplicas to 0 just results in nil being sent (API uses its own default). The container start step cannot be skipped.

**Fix Applied — Dockerfile Optimization:**
- Eliminated redundant `dotnet build` step (publish includes build)
- Added `--runtime linux-x64` to restore for targeted package graph
- Added `--no-restore` to publish step
- Switched to `aspnet:10.0-noble-chiseled` runtime image (smaller = faster pull)

Expected savings: ~1.5-2 minutes total (build + pull).

**Key Learnings:**
- azd agent extension timeout is not configurable — hardcoded at 10 minutes in Go constants
- `waitForReady` is also hardcoded to `true` — no way to skip container readiness polling
- `minReplicas: 0` does not skip container start; StartAgentContainer is always called for hosted agents
- Subsequent deploys are much faster due to Docker layer caching in ACR
- If first deploy still times out, a retry will succeed quickly since the ACR image is already built
- Recommend filing upstream issue on Azure/azure-dev for configurable timeout

**Files Changed:**
- `src/TaggerAgent/Dockerfile` — Optimized build steps, switched to chiseled runtime image

**Decision:** `.squad/decisions/inbox/parker-timeout-fix.md`
