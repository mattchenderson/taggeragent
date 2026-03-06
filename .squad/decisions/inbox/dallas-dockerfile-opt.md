# Dockerfile Build Optimization

**Date:** 2025-07-25
**Author:** Dallas (Core Dev)
**Status:** Implemented

## Context

ACR remote builds were timing out at 10 minutes during `azd deploy`. The Dockerfile
had a redundant `dotnet build` step before `dotnet publish`, effectively compiling
the project twice.

## Decision

1. **Remove redundant build step** — `dotnet publish` already compiles; the separate
   `dotnet build` was a full wasted SDK pass.
2. **Add ReadyToRun compilation** (`-p:PublishReadyToRun=true`) — pre-JITs assemblies
   at publish time for faster container cold start. Foundry health checks need the
   container responding quickly.
3. **Do NOT use trimming or Native AOT** — Azure SDK packages rely on reflection
   (Azure.Identity, Azure.ResourceManager) and are incompatible with both. Agent
   Framework preview packages are also untested with AOT.
4. **Exclude `agent.yaml` from Docker context** — Foundry deployment descriptor is
   not needed inside the running container.

## Trade-offs

- ReadyToRun increases published binary size by ~15% but significantly reduces
  JIT time at startup. For a Foundry hosted agent that must pass health probes
  within seconds, this is the right trade.
- Keeping `aspnet:10.0` (not chiseled) per team constraint. Chiseled would save
  ~50MB image size but is a separate decision.

## Files Changed

- `src/TaggerAgent/Dockerfile`
- `src/TaggerAgent/.dockerignore`
