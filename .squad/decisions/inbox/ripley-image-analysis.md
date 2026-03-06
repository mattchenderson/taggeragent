# Container Image Analysis — Deployment Timeout Root Cause

**Date:** 2026-03-06  
**Author:** Ripley  
**Status:** Implemented

## Problem

`azd deploy` for `tagger-agent` consistently times out at 10 minutes during ACR remote build. Matthew observed this shouldn't take so long for a small C# agent with ~10 NuGet packages.

## Analysis

### 1. Application Complexity (Actual)

**Source code:** 31 C# files, 54 KB total  
**Published output:** 35 files, **15.53 MB** (framework-dependent)  
**Core application:** TaggerAgent.dll is ~10 KB

This is a TINY application. The published output is just agent code + NuGet dependencies. No bloat.

### 2. NuGet Dependency Analysis

Largest assemblies in published output (Top 10):
1. `OpenAI.dll` — 4.25 MB (agent framework dependency)
2. `Azure.AI.Agents.Persistent.dll` — 1.53 MB (Foundry SDK)
3. `Azure.ResourceManager.dll` — 1.39 MB (ARM SDK)
4. `Azure.Storage.Blobs.dll` — 1.31 MB (rules storage)
5. `Microsoft.Agents.AI.Hosting.OpenAI.dll` — 1.15 MB (hosting)
6. `Microsoft.Identity.Client.dll` — 0.99 MB (authentication)

**Total package weight:** ~15 MB. This is NORMAL for Azure SDK workloads. No unexpected bloat.

All packages are essential:
- Azure.ResourceManager* — Required for Resource Graph and tag operations
- Azure.Storage.Blobs — Required for rules persistence
- Azure.AI.Agents.Persistent + Microsoft.Agents.AI.* — Required for Foundry hosting
- Azure.Identity — Required for managed identity auth

**Verdict:** No dependency trimming possible without breaking core functionality.

### 3. Base Image Analysis

**Current Dockerfile:**
- Build stage: `mcr.microsoft.com/dotnet/sdk:10.0` (~900 MB compressed from MCR)
- Runtime stage: `mcr.microsoft.com/dotnet/aspnet:10.0` (~220 MB compressed from MCR)

**ACR remote build implications:**
- Every build pulls images from MCR (no local cache)
- SDK pull alone is ~3-5 minutes on cold ACR
- Runtime pull is ~1-2 minutes

**Alpine alternative:**
- `mcr.microsoft.com/dotnet/sdk:10.0-alpine` (~200 MB compressed)
- `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` (~110 MB compressed)
- **Saves ~800 MB on SDK pull, ~110 MB on runtime pull**
- **Estimated time savings: 2-4 minutes**

**Chiseled alternative (considered, rejected):**
- `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (~80 MB)
- Ultra-minimal (no shell, package manager)
- **Risk:** May break Foundry health checks if they rely on shell or standard utilities
- **Decision:** Too risky for 30 MB savings over Alpine

### 4. Self-Contained vs Framework-Dependent

**Current:** Framework-dependent (`--self-contained false`)
- Relies on aspnet runtime image (220 MB)
- Published output: 15.53 MB

**Alternative:** Self-contained + trimming
- Includes .NET runtime in app (no aspnet base image)
- **Problem:** Azure SDK assemblies use reflection heavily
- Trimming would break Azure.ResourceManager, Azure.Identity, Azure.Storage.Blobs
- **Previous analysis (history.md, 2025-07-25):** "Do NOT use trimming or Native AOT (Azure SDK uses reflection; incompatible)"

**Verdict:** Framework-dependent is correct choice. Self-contained would increase image size and break SDKs.

### 5. Direct Container Build (dotnet publish --os linux --arch x64 /t:PublishContainer)

**Current approach:** Dockerfile + ACR remote build
- `azd deploy` triggers ACR build (pulls SDK, builds, pushes)
- ACR has to pull images from MCR (no cache)

**Alternative:** Local build + ACR push
- `dotnet publish /t:PublishContainer` produces OCI image locally
- Push directly to ACR
- **Pros:** No SDK pull in ACR, uses local Docker cache, much faster
- **Cons:** Requires azd support for non-Dockerfile container builds

**Problem:** azd extension for hosted agents expects Dockerfile. Would require custom build script.

**Verdict:** Not viable without azd extension changes. File upstream issue.

### 6. Alternative Hosting (Non-Container)

**Could this run as App Service or Azure Function instead of hosted container?**

**NO.** Foundry hosted agents use a specific container protocol:
- Agent responds to POST requests at `/responses` endpoint
- Request format: OpenAI responses API schema
- Foundry manages lifecycle, scaling, health checks
- Agent Framework (`Microsoft.Agents.AI.Hosting.OpenAI`) implements this protocol

App Service or Functions would require:
- Complete rewrite of hosting layer
- Manual implementation of responses protocol
- Lose Foundry's built-in agent management, scaling, monitoring

**Verdict:** Hosted container is the correct architecture. No alternatives.

### 7. ReadyToRun Compilation

**Current Dockerfile:** `-p:PublishReadyToRun=true`
- Pre-JITs assemblies for faster startup
- **Increases binary size ~15%** (15.53 MB → ~18 MB)
- **Reduces cold start time significantly** (no JIT warmup)

**Necessary for Foundry:** Health probes happen immediately after container start. Slow JIT startup could fail probes.

**Verdict:** Keep ReadyToRun. Worth the size trade-off.

## Proposed Fixes

### Fix 1: Switch to Alpine Base Images ✅ IMPLEMENT

**Change:**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
...
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
```

**Impact:**
- SDK pull: 900 MB → 200 MB (saves ~700 MB, ~2-3 min)
- Runtime pull: 220 MB → 110 MB (saves ~110 MB, ~1 min)
- **Total estimated savings: 3-4 minutes**

**Risk:** Low. Alpine is well-tested for .NET workloads. Foundry agents run ASP.NET Core, which is Alpine-compatible.

**Compatibility check:**
- Azure SDK packages are pure managed code (no native dependencies)
- Foundry health checks use HTTP (no shell required)
- Alpine has musl libc, which .NET runtime supports fully

### Fix 2: Optimize Dockerfile Layer Caching ✅ IMPLEMENT

**Current issue:** `COPY . .` invalidates cache on ANY file change (including agent.yaml, README, etc.)

**Proposed changes:**
1. Copy only `.csproj` first (restore layer caches unless dependencies change)
2. Copy only source files (excludes agent.yaml, docs)
3. Update `.dockerignore` to exclude non-build files

**Impact:** Incremental builds skip restore (~30s savings per redeploy)

### Fix 3: Document azd Retry Workflow ✅ DOCUMENT ONLY

**ACR builds are inherently slow on first deploy.** Even with Alpine, first deploy could hit 8-9 minutes if ACR has no cached layers.

**Matthew's concern:** "No retries as solution" — Agreed. But azd has no configuration for timeout.

**Workaround:**
1. First deploy: May timeout (ACR pulls all base images)
2. Second deploy: Uses cached layers, completes in 2-3 min
3. OR: Deploy functions first (`azd deploy functions`), THEN agent (ACR warming)

**Action:** Document this in README.md under "Deployment" section.

### Fix 4: File Upstream Issue ✅ RECOMMEND

**Issue:** azd `azure.ai.agents` extension hardcodes 10-minute timeout with no configuration.

**Recommendation:** File issue on `Azure/azure-dev` repo requesting:
- Configurable timeout via env var (e.g., `AZURE_AI_AGENT_DEPLOY_TIMEOUT`)
- OR increase default to 15 minutes for hosted agents

## What WON'T Help

1. **Dependency trimming** — All packages are essential, already minimal
2. **Self-contained builds** — Would break Azure SDK reflection, increase size
3. **Native AOT** — Incompatible with Azure SDK and Agent Framework
4. **Different hosting model** — Foundry container protocol is required
5. **Local SDK publish** — azd extension doesn't support non-Dockerfile builds

## Implementation

### Immediate (this session):
1. Switch Dockerfile to Alpine images
2. Improve .dockerignore to exclude unnecessary files
3. Update README.md with deployment guidance

### Follow-up (recommend to Matthew):
1. File azd upstream issue for configurable timeout
2. Consider pre-warming ACR by pulling base images during infra provisioning (Bicep script)

## Expected Outcome

**Before:** 10+ minutes (timeout)  
**After Alpine:** 6-8 minutes (first deploy), 2-3 minutes (incremental)

Alpine won't guarantee sub-10-minute first deploys (ACR still has to pull images from MCR), but it dramatically reduces the surface area. Combined with layer caching, incremental deploys should be reliable.

**Key insight:** The project itself is NOT the problem. It's 15 MB of assemblies with zero bloat. The timeout is caused by ACR remote build pulling 1+ GB of base images from MCR. Alpine cuts this by ~65%.
