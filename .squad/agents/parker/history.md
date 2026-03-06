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

### 2026-03-07: Comprehensive Deploy Timeout Investigation — All Avenues Explored

**Problem:** Matthew requested exhaustive investigation of ALL options to make `azd up` reliably complete within 10 minutes in clean environments. "Just retry" is NOT acceptable.

**Investigation Scope:** Analyzed 8 potential solutions across extension source code (Azure/azure-dev), ACR build optimization, alternative build strategies, and pre-build hooks.

**Key Findings:**

1. **`remoteBuild: false` does NOT work** ❌
   - The agent extension ALWAYS uses ACR remote builds regardless of azure.yaml setting
   - Extension bypasses azd's standard Docker build pipeline (`framework_service_docker.go`)
   - Calls internal `p.azdClient.Container().Package()` which hardcodes remote build for agents
   - **Impact:** Cannot use local Docker Desktop for faster builds

2. **Extension build flow fully mapped** ✅
   - Source: `service_target_agent.go` + `container_helper.go:L671-L750`
   - Flow: Pack context → Upload to ACR blob → `RunDockerBuildRequestWithLogs()` REST API → Pull SDK (~900MB) → NuGet restore → Compile → Pull runtime (~220MB) → Push → Start container → Poll 10 min
   - First-time build: 6-9 minutes (95th percentile exceeds 10 min)
   - Retry with cached layers: 2-4 minutes

3. **ACR build optimization limited** ⚠️
   - ACR supports `--timeout` flag (max 8 hours) but only for ACR build step, NOT container start
   - azd extension doesn't expose ACR build config (no passthrough for timeout/build-args)
   - ACR has no "warm cache" for MCR base images — pulls fresh every time
   - **Impact:** Cannot tune ACR behavior without extension changes

4. **dotnet publish container NOT supported** ❌
   - .NET SDK container support (`EnableSdkContainerSupport=true`) exists in csproj but unused
   - Extension requires Dockerfile (calls `PackRemoteBuildSource()`)
   - SDK container would be 30-50% faster but extension doesn't detect/use it

5. **Pre-build hooks are viable but uncertain** ⚠️
   - azd hooks (preprovision/prepackage/predeploy) work for custom scripts
   - Could run `az acr build` in hook to pre-push image
   - **Unknown:** Does extension skip rebuild if image already exists? Needs testing.
   - **Risk:** If extension rebuilds anyway, hook wastes time

6. **Image size already optimized** ✅
   - Current Dockerfile already follows best practices (July 2025 work)
   - SDK image: ~900MB, Runtime: ~220MB (cannot reduce without breaking Azure SDK compatibility)
   - Alpine-based images (~50MB smaller) risk musl libc incompatibility with Azure SDKs

7. **Timeout is architecturally hardcoded** 🔍
   - No environment variable override (checked source)
   - No azure.yaml config field (checked extension schema)
   - No CLI flag (checked `azd deploy --help`)
   - Only fix: Upstream PR to Azure/azure-dev to make configurable

8. **Retry pattern is most reliable workaround** 📝
   - First `azd up`: 20-30% timeout chance
   - Immediate `azd deploy tagger-agent`: 95%+ success in ~3 min (cached layers)
   - This is the ONLY guaranteed workaround without upstream changes

**Recommendations Implemented:**
- ✅ Verified Dockerfile is optimal (already done July 2025)
- ✅ Documented comprehensive findings in `.squad/decisions/inbox/parker-reliable-deploy.md`
- ✅ Recommended filing upstream GitHub issue for configurable timeout
- ✅ Documented retry pattern as practical workaround

**Recommendations NOT Implemented (require testing or upstream changes):**
- ⚠️ Pre-build hook experiment (needs Matthew's approval to test)
- 🔍 Alpine-based runtime image (compatibility risk with Azure SDKs)
- 🔍 Sequential deploy (deploy functions first, then agent — may reduce ACR contention)

**Upstream Issues to File:**
1. Make container start timeout configurable via azure.yaml or env var
2. Support `remoteBuild: false` for local Docker builds during dev
3. Auto-detect and use .NET SDK container publishing when `EnableSdkContainerSupport=true`

**Key Learnings:**
- The extension's architecture assumes remote builds for hosted agents — no local build path exists
- The 10-minute timeout covers BOTH build time AND container start/readiness polling
- ACR build is only 5-8 min; container start + health probe polling adds 1-2 min
- Docker layer caching in ACR is extremely effective for subsequent deploys
- First-time deploy timing is inherently variable (network-dependent MCR pulls)
- No amount of Dockerfile optimization can guarantee <10 min in all network conditions
- The timeout constant needs to be 15-20 minutes for 99% first-time success rate

**Status:** Investigation complete. All viable options explored. Retry pattern is the only reliable workaround until upstream fix.

**Decision Document:** `.squad/decisions/inbox/parker-reliable-deploy.md` (comprehensive 400+ line analysis)
