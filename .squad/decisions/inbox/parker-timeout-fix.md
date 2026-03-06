# Deploy Timeout for Hosted Agent — Root Cause and Workarounds

**Date:** 2025-07-25
**Author:** Parker (Infra/DevOps)
**Status:** Proposed

## Problem

`azd deploy` for the `tagger-agent` service times out after 10 minutes:
```
ERROR: timeout waiting for operation (id: ...) to complete after 10m0s
```

The deploy gets stuck at "Starting agent container" because the combined time for
ACR remote build + Foundry container pull + container start exceeds 10 minutes.

## Root Cause Analysis

**The 10-minute timeout is hardcoded** in the azd `azure.ai.agents` extension
(Go constants in `service_target_agent.go`):

```go
const waitForReady = true
const maxWaitTime = 10 * time.Minute
```

There is **no configuration override** — no env var, no azure.yaml field, no CLI
flag. Both `waitForReady` and `maxWaitTime` are compile-time constants.

**`minReplicas: 0` does not help.** The extension always calls
`startAgentContainer()` for hosted agents regardless of replica config. Setting
minReplicas to 0 just results in the value not being sent to the API (nil
pointer), defaulting to the Foundry service default.

**No open issues or PRs** exist on `Azure/azure-dev` to make this configurable.

## Timeline Breakdown (first deploy)

| Step | Estimated Time |
|---|---|
| ACR remote build (dotnet restore prerelease + compile) | 5-8 min |
| Foundry pulls image from ACR | 1-2 min |
| Container starts and passes health check | 1-2 min |
| **Total** | **7-12 min** |

Subsequent deploys with cached NuGet layers should be ~3-5 min total.

## Workarounds Applied

### 1. Dockerfile optimization (applied)

Reduced build time by:
- Eliminating redundant `dotnet build` step (publish includes build)
- Adding `--runtime linux-x64` to restore for targeted package graph
- Adding `--no-restore` to publish to reuse cached restore
- Switching runtime image to `aspnet:10.0-noble-chiseled` (smaller image = faster pull)

Expected savings: ~1-2 minutes on ACR build, ~30s on image pull.

### 2. Recommended: File upstream issue

File an issue on `Azure/azure-dev` requesting:
- Configurable timeout via azure.yaml (`timeout: 20m` under service config)
- Or environment variable override (`AZD_AGENT_DEPLOY_TIMEOUT`)
- Or `--no-wait` flag to skip container readiness polling

### 3. Recommended: Two-step deploy workflow

If timeout persists after Dockerfile optimization:
1. Run `azd deploy functions` first (fast, no container)
2. Run `azd deploy tagger-agent` second — if it times out, the ACR image is
   already built and cached, so a retry will complete quickly
3. Or: build image manually via `az acr build` before `azd deploy`

### 4. Not recommended: minReplicas: 0

Does not skip the container start step. The extension always calls
StartAgentContainer regardless.

## Decision Needed

Accept Dockerfile optimization as immediate fix. File upstream issue for
configurable timeout. If first deploy still times out, retry is the pragmatic
workaround (second attempt uses cached layers).
