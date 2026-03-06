# Brett — History

## Project Context

- **Project:** taggeragent — Azure resource tagging agent
- **Stack:** C# (.NET 10), Azure Foundry Hosted Agents, azd, Bicep
- **Description:** An agent that scans Azure resources across a subscription and helps tag them according to configurable rules stored per-subscription in blob storage
- **User:** Matthew Henderson
- **Latest:** Project upgraded to .NET 10 (2026-03-06, Dallas) — all C# projects use net10.0 TFM; Azure Functions Worker SDK v2.x; Dockerfile base images updated to 10.0

## Key Files

- `src/TaggerAgent/` — Core agent project (Foundry hosted agent)
- `src/TaggerAgent.Functions/` — Azure Functions project (timer trigger)
- `src/TaggerAgent/Shared/EnvironmentConfig.cs` — Shared env var contract
- `src/TaggerAgent/Agent/` — Agent logic, instructions, tools
- `src/TaggerAgent/Services/` — RulesService, AuditService, ResourceService

## Learnings

### 2025-01-XX — .NET Best Practices Pass on TaggerAgent.Functions

**Task:** Comprehensive .NET best practices and naming standards pass on the Functions project.

**Changes made:**
1. **Program.cs** - Added explicit `Main` method with proper signature, file-scoped namespace, comprehensive XML documentation, and `internal static` access modifier for the Program class
2. **TimerScanFunction.cs** - Changed method name from `Run` to `RunAsync` following async naming conventions, added `CancellationToken` parameter, applied `ConfigureAwait(false)` to all async calls, made class `sealed`, refactored inline logic into helper methods (`FindAgentByNameAsync`, `WaitForRunCompletionAsync`, `LogAgentResponseAsync`) for better separation of concerns and testability
3. **Access modifiers** - Program class is `internal static`, TimerScanFunction is `public sealed`, helper methods are `private`
4. **XML documentation** - Added comprehensive XML doc comments to public APIs including method parameters
5. **Modern patterns** - File-scoped namespaces, nullable reference types enabled, primary constructor used

**Key findings:**
- Azure.AI.Agents.Persistent SDK methods do NOT universally accept `CancellationToken` - only use where supported (Task.Delay, GetRunAsync polling loop)
- ConfigureAwait(false) applied consistently to avoid deadlocks in hosted environments
- host.json is already minimal and correct for .NET isolated worker
- EnvironmentConfig location: `src/TaggerAgent/EnvironmentConfig.cs` (not in Shared subfolder)

**Build status:** ✅ Verified clean build with no warnings
