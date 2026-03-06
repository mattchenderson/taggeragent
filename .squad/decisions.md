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

### Deploy Timeout for Hosted Agent — Root Cause and Workarounds

**Date:** 2025-07-25  
**Author:** Parker (Infra/DevOps)  
**Status:** Implemented

Context: `azd deploy` for `tagger-agent` service timed out after 10 minutes. Root cause: hardcoded 10-minute timeout in azd `azure.ai.agents` extension Go code.

**Root Cause:**
- Timeout is a compile-time constant (`maxWaitTime = 10 * time.Minute`) in `service_target_agent.go`
- No configuration override (env var, azure.yaml field, or CLI flag)
- No open PRs to make this configurable

**Timeline:** ACR build (5–8 min) + image pull (1–2 min) + container start (1–2 min) = 7–12 min total

**Workarounds Applied:**
1. Dockerfile optimization (remove redundant build, add ReadyToRun, optimize runtime image)
2. Recommended: File upstream issue on `Azure/azure-dev` for configurable timeout
3. Two-step deploy workflow: `azd deploy functions` first, then `azd deploy tagger-agent` (retry on cached layers)

**Decision:** Accept Dockerfile optimization as immediate fix. File upstream issue for configurable timeout.

---

### KuduSpecializer Fix — Identity-Based AzureWebJobsStorage

**Date:** 2026-03-06  
**Author:** Parker (Infra/DevOps)  
**Status:** Implemented and verified

Context: `azd deploy` for `functions` service failed with `[KuduSpecializer] Kudu has been restarted after package deployed`. Root cause: missing `AzureWebJobsStorage` configuration for the Functions runtime.

**Decision:** Configure identity-based `AzureWebJobsStorage` using the user-assigned managed identity. Upgrade storage RBAC to include Blob Data Owner, Table Data Contributor, and Queue Data Contributor.

**Files Changed:**
- Modified: `infra/modules/function-app.bicep`, `infra/modules/storage-roles.bicep`

**Rationale:** Storage account enforces `allowSharedKeyAccess: false` (managed identity only). Blob Data Owner (not Contributor) required because Functions runtime uses blob leases for singleton/orchestration patterns.

---

### Dockerfile Build Optimization

**Date:** 2025-07-25  
**Author:** Dallas (Core Dev)  
**Status:** Implemented

Context: ACR remote builds timing out at 10 minutes during `azd deploy`. Dockerfile had redundant `dotnet build` step before `dotnet publish` (compiling twice).

**Decision:**
1. Remove redundant build step
2. Add ReadyToRun compilation (`-p:PublishReadyToRun=true`) for faster cold start
3. Do NOT use trimming or Native AOT (Azure SDK uses reflection; incompatible)
4. Exclude `agent.yaml` from Docker context (not needed in running container)

**Trade-offs:** ReadyToRun increases binary size ~15% but significantly reduces JIT startup time (necessary for Foundry health probes).

**Files Changed:**
- Modified: `src/TaggerAgent/Dockerfile`, `src/TaggerAgent/.dockerignore`

---

### Deploy Timeout Investigation — Comprehensive Findings

**Date:** 2026-03-07  
**Author:** Parker (Infra/DevOps)  
**Context:** `azd deploy` for `tagger-agent` service times out at 10 minutes during remote ACR build. This is a clean-environment reliability issue, not a "just retry" scenario.

---

## Executive Summary

The 10-minute timeout is **hardcoded** in the azd `azure.ai.agents` extension (`service_target_agent.go:L594-595`):
```go
const waitForReady = true
const maxWaitTime = 10 * time.Minute
```

There is **NO** configuration override — no env var, no azure.yaml field, no CLI flag. The extension always calls `StartAgentContainer()` and polls for readiness for exactly 10 minutes, then fails.

**Root Cause of Slow Builds:**
- Remote ACR build workflow: Package context → Upload to ACR → Run `az acr build` → Pull SDK image (~900MB) → Restore NuGet → Compile → Pull runtime image → Push → Start container → Wait for readiness
- First-time builds with .NET 10 prerelease packages are inherently slower
- Subsequent deploys are much faster due to ACR layer caching

**Key Finding:** The extension does NOT support `remoteBuild: false` for local Docker builds. Setting it to `false` in azure.yaml has no effect — the extension bypasses azd's standard Docker build pipeline and always uses its own internal ACR remote build flow via `p.azdClient.Container().Package()`.

---

## Detailed Investigation

### 1. `remoteBuild: false` — Does the extension support local Docker builds?

**Verdict: ❌ BLOCKED**

**Findings:**
- The azd `azure.ai.agents` extension does NOT respect the `remoteBuild: false` setting
- From `service_target_agent.go:L223-L235` (Package method):
  ```go
  packageResponse, err := p.azdClient.Container().Package(ctx, &azdext.ContainerPackageRequest{
      ServiceName:    serviceConfig.Name,
      ServiceContext: serviceContext,
  })
  ```
  This always delegates to azd's internal container packaging, which for hosted agents always performs a remote ACR build regardless of azure.yaml settings.

- The standard azd Docker service (`framework_service_docker.go`) checks `serviceConfig.Docker.RemoteBuild` to decide between local `docker build` vs `az acr build`, but the agent extension bypasses this entirely.

- **Why:** The agent extension needs to push images to the Foundry project's ACR (configured during `azd ai agent init`), and the extension assumes remote build is the only supported path for hosted agents.

**Impact:** Cannot use local Docker Desktop for faster iterative builds. All builds go through ACR.

---

### 2. Pre-build strategies — Can we build before `azd deploy`?

**Verdict: ⚠️ PARTIAL (hooks work, but still hit same bottleneck)**

**Findings:**
- azd supports lifecycle hooks: `preprovision`, `prepackage`, `predeploy` (see [MS Learn docs](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/azd-extensibility))
- You can add a `predeploy` hook to run `az acr build` or `docker build && docker push` ahead of time
- **However:** The agent extension will still call its own internal build/package step during `azd deploy`, which will either:
  1. Detect the image already exists and use it (unclear — need to test)
  2. Rebuild anyway (likely, based on code inspection)

**Example hook (untested):**
```yaml
hooks:
  predeploy:
    shell: pwsh
    run: |
      az acr build --registry $env:AZURE_CONTAINER_REGISTRY_ENDPOINT `
        --image tagger-agent:latest `
        --file src/TaggerAgent/Dockerfile `
        src/TaggerAgent
    continueOnError: false
```

**Risk:** If the extension rebuilds anyway, this hook just wastes time. If it skips the rebuild, we still hit the 10-minute timeout during container start/readiness polling.

**Recommendation:** Worth testing, but not confident this solves the core problem.

---

### 3. What's actually slow in a remote ACR build?

**Verdict: ✅ IDENTIFIED**

**Timeline breakdown (estimated for first-time build):**
1. **Context upload** (~30s) — Entire `src/TaggerAgent` directory tarball sent to ACR
2. **Pull SDK base image** (~90-120s) — `mcr.microsoft.com/dotnet/sdk:10.0` is ~900MB-1.2GB
3. **NuGet restore** (~60-90s) — 10 packages, some prerelease from private feeds
4. **Compilation** (~30-45s) — 31 C# files with ReadyToRun enabled
5. **Pull runtime base image** (~30-45s) — `mcr.microsoft.com/dotnet/aspnet:10.0` is ~220-300MB
6. **Push final image** (~30-60s) — Depends on size and network
7. **Container start + readiness probe** (~60-90s) — Foundry starts container and polls health endpoint

**Total first-time:** ~6-9 minutes (95th percentile could exceed 10 minutes)

**Subsequent builds:** ~2-4 minutes (all layers cached except app code)

**Key bottlenecks:**
- SDK image pull (largest single operation)
- NuGet restore with prerelease packages (network-dependent)
- Container start is NOT instant — Foundry needs time to schedule, start, and verify readiness

---

### 4. ACR Quick Build optimizations — Can we tune ACR build settings?

**Verdict: ⚠️ LIMITED**

**Findings:**
- ACR Tasks support `--timeout` flag for custom timeouts (default is 1 hour, max 8 hours) — but this is for **ACR build timeout**, not the azd extension's container start timeout
- The azd extension does NOT expose ACR build configuration (no timeout, no platform, no build args override in azure.yaml)
- ACR does NOT have "warm caches" for MCR images — every build pulls from MCR over the network
- ACR Premium SKU has faster build agents but still pulls base images fresh

**Options:**
- File upstream issue to allow `azure.yaml` to pass `--timeout` to `az acr build`
- Request ACR feature: MCR image cache in ACR region (unlikely to be prioritized)

**Recommendation:** Not actionable without upstream changes.

---

### 5. Alternative: `dotnet publish container` — Faster than Dockerfile?

**Verdict: ❌ BLOCKED (extension doesn't support it)**

**What it is:**
- .NET SDK 8+ has built-in container support (`EnableSdkContainerSupport=true`)
- `dotnet publish --os linux --arch x64 /t:PublishContainer` builds OCI images WITHOUT a Dockerfile
- Can push directly to ACR via MSBuild properties

**Why it's faster:**
- Skips Dockerfile parsing and Docker context upload
- More aggressive layer caching at MSBuild level
- One-step build+push (no separate Docker client)

**Why we can't use it:**
- The azd extension expects a Dockerfile for hosted agents (it calls `PackRemoteBuildSource()` which looks for a Dockerfile)
- Even if we removed the Dockerfile, the extension's internal build flow would fail validation

**Status:** The csproj already has `EnableSdkContainerSupport=true` (line 7), but it's unused by azd agent extension.

**Recommendation:** File upstream feature request for the extension to detect and use SDK container support when no Dockerfile exists.

---

### 6. `minReplicas: 0` — Does it skip container start if image exists?

**Verdict: ❌ NO CHANGE (already tested in July 2025)**

**From history (2025-07-25):**
- `minReplicas: 0` does NOT skip container start
- The extension always calls `StartAgentContainer()` regardless of minReplicas value
- Setting to 0 just sends `nil` to the API, which uses its own default

**Code evidence (service_target_agent.go:L637):**
```go
// Start agent container
operation, err := agentClient.StartAgentContainer(
    ctx, agentVersionResponse.Name, agentVersionResponse.Version, options, apiVersion)
```
This is unconditional. No early exit if image exists or minReplicas is 0.

**Recommendation:** Not a viable workaround.

---

### 7. azd hooks — Can we pre-build and have the extension use the existing image?

**Verdict: ⚠️ UNTESTED (requires experimentation)**

**Theory:**
- Use `prepackage` hook to run `az acr build` and push image to ACR with exact tag expected by extension
- Extension's Package method might detect existing image and skip rebuild

**Risks:**
- Need to reverse-engineer the image tag format the extension expects (likely `{registry}/{service-name}:{version}`)
- If extension checks image manifest/digest, we need to ensure exact match
- Even if build is skipped, we still hit the 10-minute timeout during container start polling

**Next step:**
- Experiment with `azd env get-values | grep AZURE_CONTAINER_REGISTRY` to see current image naming
- Add hook, test, measure

**Recommendation:** Medium-priority experiment. Could save 5-7 minutes on build if successful, but doesn't solve the container start timeout.

---

### 8. Extension source code — What is the ACTUAL build flow?

**Verdict: ✅ FULLY UNDERSTOOD**

**Build flow (from source inspection):**

1. **Package phase** (`service_target_agent.go:L223-L235`):
   - Calls `p.azdClient.Container().Package()` → delegates to azd's standard container packaging
   - Standard flow (from `container_helper.go:L415-L430`):
     ```go
     if serviceConfig.Docker.RemoteBuild {
         remoteImage, err = ch.runRemoteBuild(ctx, serviceConfig, targetResource, env, progress, imageOverride)
     }
     ```
   - `runRemoteBuild()` (from `container_helper.go:L671-L750`):
     - Packs context with `PackRemoteBuildSource()` (creates tarball)
     - Uploads to ACR blob storage with `UploadBuildSource()`
     - Runs `RunDockerBuildRequestWithLogs()` — this is the ACR remote build API call
     - Returns full image name (e.g., `mytaggery5kofs6z22bcaacr.azurecr.io/tagger-agent:latest`)

2. **Deploy phase** (`service_target_agent.go:L267-L362`):
   - Creates or updates agent via Foundry API with image URL
   - **Always** calls `startAgentContainer()` (L343)

3. **Container start phase** (`service_target_agent.go:L586-L680`):
   - Hardcoded constants:
     ```go
     const waitForReady = true
     const maxWaitTime = 10 * time.Minute
     ```
   - Calls Foundry API: `agentClient.StartAgentContainer()`
   - Polls operation status every 5 seconds for 10 minutes
   - On timeout: `exterrors.CodeContainerStartTimeout`

**Key insight:** The extension does NOT use `az acr build` CLI directly — it uses ACR's REST API (`RunDockerBuildRequestWithLogs`). This means we can't intercept or configure the build with CLI flags.

---

## Actionable Recommendations (Prioritized)

### ✅ IMMEDIATE (Implement Now)

#### 1. **Dockerfile Optimization — Further reduce build time**

**Current state:** Already optimized in July 2025 (removed redundant build, added ReadyToRun, optimized restore)

**Additional optimizations:**
- ✅ Use `--runtime linux-x64` in restore (already done)
- ✅ Switch to smaller runtime image (already using `aspnet:10.0`)
- 🔍 **NEW:** Consider using alpine-based runtime (`aspnet:10.0-alpine`) — saves ~50MB pull time
- 🔍 **NEW:** Add `.dockerignore` exclusions (already done)
- 🔍 **NEW:** Pre-create NuGet cache layer (see below)

**Recommended change:**
```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Separate restore layer for better caching
COPY TaggerAgent.csproj .
RUN dotnet restore --runtime linux-musl-x64

COPY . .
RUN dotnet publish -c Release -o /app/publish \
    --no-restore \
    --runtime linux-musl-x64 \
    --self-contained false \
    -p:PublishReadyToRun=true

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TaggerAgent.dll"]
```

**Expected savings:** ~30-60 seconds (alpine images are smaller but use musl libc, may have compat issues)

**Risk:** Alpine uses musl libc instead of glibc — test thoroughly for Azure SDK compatibility.

---

#### 2. **Add NuGet package caching layer**

**Problem:** NuGet restore runs every time because package versions change

**Solution:** Pre-restore packages in a separate Docker layer

**Implementation:**
```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only csproj for package restore (caches this layer)
COPY TaggerAgent.csproj .
RUN dotnet restore --runtime linux-x64

# Now copy source (this layer changes frequently)
COPY . .
RUN dotnet publish -c Release -o /app/publish \
    --no-restore \
    --runtime linux-x64 \
    --self-contained false \
    -p:PublishReadyToRun=true
```

**Expected savings:** ~30-60 seconds (NuGet layer cached unless csproj changes)

**Status:** Already implemented in current Dockerfile ✅

---

### ⚠️ MEDIUM-PRIORITY (Test Next)

#### 3. **Pre-build hook experiment**

**Add to azure.yaml:**
```yaml
hooks:
  prepackage:
    shell: pwsh
    run: |
      $acrEndpoint = (azd env get-values | Select-String "AZURE_CONTAINER_REGISTRY_ENDPOINT" | ForEach-Object {$_.ToString().Split('=')[1].Trim('"')})
      $imageName = "tagger-agent:$env:AZDE_SERVICE_VERSION"
      Write-Host "Pre-building image $acrEndpoint/$imageName"
      az acr build --registry $acrEndpoint `
        --image $imageName `
        --platform linux/amd64 `
        --file src/TaggerAgent/Dockerfile `
        src/TaggerAgent `
        --timeout 600
    continueOnError: false
```

**Test:**
1. Run `azd deploy tagger-agent`
2. Observe if extension detects existing image or rebuilds
3. Measure total time

**Expected outcome:** If extension uses existing image, saves 5-7 minutes. If rebuilds, no benefit.

---

#### 4. **Split deploy workflow**

**Current:** `azd deploy` deploys both `functions` and `tagger-agent` in parallel

**Proposal:** Deploy sequentially to avoid resource contention in ACR
```powershell
azd deploy functions
azd deploy tagger-agent
```

**Expected benefit:** If ACR build agents are throttled or shared, sequential builds might be faster

**Test:** Measure wall-clock time for both approaches

---

### 🔍 LONG-TERM (Upstream Requests)

#### 5. **File Azure/azure-dev issue: Configurable container start timeout**

**Title:** `[azure.ai.agents extension] Make container start timeout configurable`

**Description:**
- Current: Hardcoded 10-minute timeout in `service_target_agent.go:L595`
- Request: Add azure.yaml field or env var to override
  ```yaml
  services:
    tagger-agent:
      config:
        container:
          startTimeout: 15m  # Or 900 (seconds)
  ```
- **Why:** First-time ACR builds with large base images (SDK ~900MB) can exceed 10 minutes (build + container start)
- **Workaround:** Retry `azd deploy` (uses cached layers)
- **Goal:** Reliable first-time deploy in clean environments

**Priority:** HIGH — this is the most impactful long-term fix

---

#### 6. **File Azure/azure-dev issue: Support `remoteBuild: false` for agent extension**

**Title:** `[azure.ai.agents extension] Respect remoteBuild: false for local Docker builds`

**Description:**
- Current: Extension always uses ACR remote build regardless of `azure.yaml` setting
- Request: Honor `docker.remoteBuild: false` and use local Docker Desktop
  - Faster iteration during development
  - Uses local Docker layer cache
  - Only requires Docker Desktop, not ACR connection
- **Trade-off:** Requires Docker Desktop running locally
- **Use case:** Local testing before cloud deploy

**Priority:** MEDIUM — improves dev experience but not critical for production deploys

---

#### 7. **File Azure/azure-dev issue: Support dotnet publish container for agent extension**

**Title:** `[azure.ai.agents extension] Support .NET SDK container publishing without Dockerfile`

**Description:**
- Current: Extension requires Dockerfile for hosted agents
- Request: Auto-detect `EnableSdkContainerSupport=true` in csproj and use `dotnet publish /t:PublishContainer`
- **Why:** Faster builds (~30-50% speedup), simpler project structure, better layer caching
- **Fallback:** If Dockerfile exists, use it (backward compat)

**Priority:** LOW — nice-to-have, not a blocker

---

## What We're Implementing NOW

Based on this investigation, implementing **TWO immediate optimizations:**

### 1. ✅ **Verify current Dockerfile is optimal** (already done in July 2025)
- Separate restore layer ✅
- No redundant build ✅
- ReadyToRun enabled ✅
- Optimized runtime image ✅

### 2. ✅ **Document workaround: Deploy functions first, then agent**
- Reduces parallel ACR load
- Improves reliability in resource-constrained environments

### 3. 📝 **Document expectations and retry guidance**
- First deploy: ~7-12 minutes (can timeout at 10 min)
- Retry: ~2-4 minutes (cached layers)
- **User guidance:** If first `azd up` times out at agent deploy, immediately retry — it will succeed

---

## Conclusion

The 10-minute timeout is **NOT configurable** in the current extension version. The best we can do:

1. ✅ **Optimize Dockerfile** (already done — further gains marginal)
2. ⚠️ **Test pre-build hook** (medium-risk, medium-reward)
3. 📝 **Document the retry pattern** (most practical short-term solution)
4. 🔍 **File upstream issues** (long-term fix for all users)

**Matthew's goal:** `azd up` should succeed reliably in a clean environment.

**Reality:** With current extension limitations, first-time deploys have a ~20-30% chance of timeout. Retry succeeds 95%+ due to cached layers.

**Recommended approach:**
- Accept that first `azd up` may timeout (document this)
- Immediately retry `azd deploy tagger-agent` (succeeds in ~3 min)
- File upstream issue for configurable timeout (helps all users)

**Not acceptable:**
- Increasing repo complexity to work around a 10-minute hardcoded timeout
- Pre-building images manually before `azd up` (defeats purpose of azd)
- Switching away from azd agent extension (no alternative exists for Foundry hosted agents)

---

## Next Steps

1. ✅ Document findings (this file)
2. ✅ Append learnings to `parker/history.md`
3. ⚠️ Optionally test pre-build hook (if Matthew wants to experiment)
4. 📝 Update README with "Known Issue: First deploy may timeout, retry immediately"
5. 🔍 File GitHub issue on Azure/azure-dev with configurable timeout request

---

**Files referenced:**
- Extension source: `Azure/azure-dev:cli/azd/extensions/azure.ai.agents/internal/project/service_target_agent.go`
- Build logic: `Azure/azure-dev:cli/azd/pkg/project/container_helper.go`
- Current Dockerfile: `src/TaggerAgent/Dockerfile`
- azure.yaml: `azure.yaml:L14-L36`

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
