# Dallas — History

## Project Context

- **Project:** taggeragent — An Azure resource tagging agent using Foundry hosted agents
- **Stack:** C#, Azure Foundry Hosted Agents, azd, Bicep
- **What it does:** Scans Azure resources across a subscription and helps tag them according to rules (rules TBD)
- **User:** Matthew Henderson

## Learnings

### 2025-07-18
- Applied environment variable contract renames (in progress): `FOUNDRY_ENDPOINT` → `AZURE_AI_PROJECT_ENDPOINT`, `AUDIT_STORAGE_ACCOUNT_NAME` → `STORAGE_ACCOUNT_NAME`. Implementing rules redesign wave: removed default rules from project structure, updated `RulesService` for subscription-scoped blob paths (`rules/{subscription-id}/rules.json`), added agent tools for rule save/copy operations. Validation: Agent Framework API changes working correctly; both TaggerAgent and TaggerAgent.Functions projects build clean. Coordinating with Parker on env var contract infra changes and with Kane on test updates.

### 2025-01-XX: C# Code Scaffold Complete

**Architecture patterns implemented:**
- Hosted Foundry agent using responses protocol (containerized)
- Azure Functions timer trigger for automated scans
- Service-based architecture with DI (ResourceGraph, Tagging, Rules, Audit)
- Tool implementations (scan, apply, get-rules) as first-class services
- Models using C# records for immutability (TaggingRule, ResourceInfo, TagChange)
- AuditEntry implements ITableEntity for Azure Table Storage

**Key technical decisions:**
- DefaultAzureCredential everywhere — zero secrets in code
- Merge-only tag semantics via TagPatchResourceOperation.Merge
- Pagination handling in Resource Graph via SkipToken
- Audit logging uses PartitionKey = SubscriptionId, RowKey = {resourceId}_{tag}_{timestamp}
- System prompt embedded in AgentInstructions.cs as const string
- Foundry SDK integration marked as TODO pending final API design

**File structure:**
```
src/
  TaggerAgent/
    Agent/          AgentInstructions, TaggerAgent orchestrator
    Models/         TaggingRule, ResourceInfo, TagChange, AuditEntry
    Tools/          ScanResourcesTool, ApplyTagsTool, GetTaggingRulesTool
    Services/       ResourceGraphService, TaggingService, RulesService, AuditService
    Program.cs      DI setup, hosted service configuration
    Dockerfile      Multi-stage build for container
  TaggerAgent.Functions/
    TimerScanFunction.cs    Timer trigger with TODO for Foundry invocation
    Program.cs              Functions host setup
    host.json               Functions runtime config
rules/
  default-rules.json        v2 rules format (4 example rules)
```

**User preferences observed:**
- Modern C# patterns (file-scoped namespaces, primary constructors, records)
- Concise but clear comments — only where needed
- System.Text.Json for all serialization
- ILogger<T> for structured logging throughout

### 2025-01-24: Build Success — TODOs Documented

**Final status:**
- ✅ Both TaggerAgent and TaggerAgent.Functions build successfully
- ✅ Simplified ResourceGraphService and TaggingService with clear TODO markers
- ✅ TODOs document expected SDK patterns where APIs are still evolving
- ✅ Ready for infrastructure (Parker) and tests (Kane)

**TODOs left for SDK finalization:**
- `TaggerAgent.cs` lines 10-35: Foundry responses protocol integration
- `ResourceGraphService.cs` lines 28-42: Resource Graph query API
- `TaggingService.cs` lines 28-47: ARM tagging merge operation
- `TimerScanFunction.cs` lines 20-47: Foundry agent invocation from Functions

**Build warnings (expected):**
- Unread constructor parameters in TaggerAgent.cs and TimerScanFunction.cs
  - These will be used once Foundry SDK integration is implemented

### 2026-03-06: .NET 10 Upgrade

**What changed:**
- Upgraded all projects from .NET 8 to .NET 10 (net10.0 TFM)
- Updated project files: TaggerAgent.csproj, TaggerAgent.Functions.csproj, TaggerAgent.Tests.csproj (TargetFramework net8.0 → net10.0)
- Updated Dockerfile base images: mcr.microsoft.com/dotnet/sdk:8.0 → 10.0, mcr.microsoft.com/dotnet/aspnet:8.0 → 10.0
- Updated documentation: README.md (runtime & SDK version references), Dallas & Brett charter.md files
- Azure Functions Worker SDK compatibility: Updated to v2.x packages for .NET 10 support
  - Microsoft.Azure.Functions.Worker 1.23.0 → 2.51.0
  - Microsoft.Azure.Functions.Worker.Sdk 1.18.1 → 2.0.7
  - Microsoft.Azure.Functions.Worker.ApplicationInsights 1.4.0 → 2.50.0
  - Azure.Identity 1.13.1 → 1.18.0 (to satisfy ApplicationInsights dependency)

**Build verification:**
- All three projects (TaggerAgent, TaggerAgent.Functions, TaggerAgent.Tests) build successfully on net10.0
- No breaking changes in application code required
- Only package version updates needed for Azure Functions compatibility

**Requested by:** Matthew Henderson

## Cross-Agent Context

### 2026-03-05: Infrastructure Ready (Parker, Session 2333)
- Parker completed Bicep infrastructure scaffold with 7 modules (identity, storage, ACR, Foundry, RBAC, functions)
- Both managed identities now provisioned; azd integration ready for deployment
- You can now implement SDK integration against real services

### 2026-03-05: Framework Update (Agent 4 in progress)
- Matthew approved switch from `Azure.AI.Projects` to **Microsoft Agent Framework** (`Microsoft.Agents.AI.*`)
- Key packages: `Microsoft.Agents.AI.AzureAI.Persistent`, `Microsoft.Agents.AI.Hosting`, `Azure.AI.Agents.Persistent`
- Framework handles tool discovery, hosting, function calling loop automatically
- **Impact:** Reduce boilerplate in TaggerAgent.cs; tools are C# methods, framework marshals parameters
- Continue marking Foundry integration as TODO; decision consolidated in `.squad/decisions/decisions.md`

### 2025-07-14: Agent Framework Rework Complete

**What changed:**
- Replaced `Azure.AI.Projects` SDK with Microsoft Agent Framework (`Microsoft.Agents.AI.*`) packages
- TaggerAgent.cs rewritten from `TaggerAgentOrchestrator` (TODO placeholder) to `TaggerAgentTools` — tool methods with `[Description]` attributes
- Program.cs moved from `Host.CreateApplicationBuilder` to `WebApplication.CreateBuilder` with Agent Framework hosting (`AddAIAgent`, `AddOpenAIResponses`, `MapOpenAIResponses`)
- TimerScanFunction.cs now uses `PersistentAgentsClient` with `CreateAIAgentAsync`/`RunAsync` pattern
- Bumped `Microsoft.Extensions.Hosting` from `8.0.1` to `10.0.0` (required by Agent Framework packages)

**NuGet packages (real versions discovered via `dotnet package search`):**
- `Azure.AI.Agents.Persistent` 1.2.0-beta.9
- `Microsoft.Agents.AI.AzureAI.Persistent` 1.0.0-preview.260304.1
- `Microsoft.Agents.AI.Hosting` 1.0.0-preview.260304.1
- `Microsoft.Agents.AI.Hosting.OpenAI` 1.0.0-alpha.260304.1
- `Microsoft.Extensions.AI` 10.3.0

**Key API patterns learned:**
- `AIFunctionFactory.Create(Delegate)` wraps C# methods as AITool for the framework
- `builder.AddAIAgent("name", "prompt")` registers an agent for hosting
- `AddOpenAIResponses()` / `MapOpenAIResponses()` expose OpenAI responses protocol endpoint
- `PersistentAgentsClient` constructor takes `(string endpoint, TokenCredential credential)` — endpoint is string, not Uri
- Tool methods return `string` (JSON) rather than typed objects for clean function-calling compatibility

**Build status:**
- TaggerAgent: builds clean (0 warnings, 0 errors)
- TaggerAgent.Functions: builds clean (0 warnings, 0 errors)
- TaggerAgent.Tests: has pre-existing compilation errors (tests were written against old API — Kane's domain)

**Preserved unchanged:**
- All Services (ResourceGraphService, TaggingService, RulesService, AuditService)
- All Models (TaggingRule, ResourceInfo, TagChange, AuditEntry)
- All Tool classes (ScanResourcesTool, ApplyTagsTool, GetTaggingRulesTool)
- AgentInstructions.cs, Dockerfile, Functions/Program.cs


### 2025-01-XX: .NET Standards and Best Practices Pass

**What changed:**
- Renamed `TaggerAgent.cs` to `TaggerAgentTools.cs` to match the class name (major .NET convention violation fixed)
- Added `sealed` modifier to all classes that aren't designed for inheritance (TaggerAgentTools, all Services, all Tools, all Models except AuditEntry)
- Added `Async` suffix to all async tool methods (ScanResourcesAsync, ApplyTagsAsync, GetTaggingRulesAsync, SaveTaggingRulesAsync, CopyTaggingRulesAsync)
- Enhanced XML documentation across all Services, Tools, and Models with comprehensive parameter and return value documentation
- Updated Program.cs to reference renamed async methods

**Code quality improvements:**
- All service classes now marked `sealed` (ResourceGraphService, TaggingService, RulesService, AuditService)
- All tool classes now marked `sealed` (ScanResourcesTool, ApplyTagsTool, GetTaggingRulesTool)
- All record types now marked `sealed` (TaggingRule, ResourceInfo, TagChange)
- AuditEntry class marked `sealed` (implements ITableEntity, so inheritance not needed)
- Primary constructors already in use throughout
- File-scoped namespaces already in use throughout

**Build verification:**
- TaggerAgent project: builds clean (0 errors, 0 warnings)
- TaggerAgent.Tests project: builds clean (12 expected warnings about unused variables in placeholder tests)
- All 52 tests pass

**Decisions made:**
- Did NOT add interfaces for Services - tests use concrete classes, not mocks
- Tests are placeholder tests documenting expected behavior, not integration/unit tests requiring abstractions
- Adding interfaces would be premature until real testing strategy is defined by Kane

**Requested by:** Matthew Henderson

### 2025-07-24: .dockerignore Deployment Optimization (Ignore Files Pass)

**What changed:**
- Created `.dockerignore` for `src/TaggerAgent/` excluding build artifacts (`bin/`, `obj/`), IDE files, docs, git metadata
- Context reduction: ACR remote builds no longer upload ~50MB of unnecessary artifacts

**Technical rationale:**
- Dockerfile uses `COPY . .` which uploads entire context
- local `bin/` and `obj/` from previous builds are pure waste (Dockerfile does fresh `dotnet restore` + `dotnet build`)
- Reduces Docker build context from ~50MB to source files (few hundred KB)

**Cross-agent context:**
- Brett concurrently created `.funcignore` for the Functions project as complementary optimization
- Both decisions merged into decisions.md, inbox files removed
- Coordinated deployment efficiency improvement across core agent and timer function

