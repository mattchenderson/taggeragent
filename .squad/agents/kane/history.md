# Kane — History

## Project Context

- **Project:** taggeragent — An Azure resource tagging agent using Foundry hosted agents
- **Stack:** C#, Azure Foundry Hosted Agents, azd, Bicep
- **What it does:** Scans Azure resources across a subscription and helps tag them according to rules (rules TBD)
- **User:** Matthew Henderson

## Learnings

### 2025-07-18
- Updating test project for Agent Framework API changes: renaming test classes to reflect new `TaggerAgentTools` class name, adjusting constructor signatures to use concrete service types with ILogger, updating tool method signatures (string params instead of anonymous types). Framework API fully validated in main project; both builds are clean. Test updates enable Kane to progress on test implementation wave.

### 2025-01-18: Test Project Scaffolding

**Created comprehensive test suite structure:**
- `tests/TaggerAgent.Tests/TaggerAgent.Tests.csproj` — xUnit test project with Moq, FluentAssertions
- Test coverage for all 3 tools and 4 services Dallas is building
- All test methods scaffolded with Arrange-Act-Assert pattern
- TODO comments mark implementation dependencies on Dallas's work

**Key file paths:**
- Tools: `tests/TaggerAgent.Tests/Tools/{ScanResources,ApplyTags,GetTaggingRules}ToolTests.cs`
- Services: `tests/TaggerAgent.Tests/Services/{ResourceGraph,Tagging,Rules,Audit}ServiceTests.cs`
- README: `tests/TaggerAgent.Tests/README.md` — how to run tests locally

**Test patterns adopted:**
- xUnit [Fact] for simple tests, [Theory] + [InlineData] for parameterized tests
- Moq for mocking Azure SDK interfaces (ResourceGraphClient, BlobClient, TableClient)
- FluentAssertions for readable assertions (`.Should().BeEquivalentTo()`)
- [Trait("Category", "Integration")] for tests needing real Azure resources
- Naming: `MethodName_Condition_ExpectedResult`

**Critical test scenarios covered:**
- Pagination handling (Resource Graph max 1000/page, SkipToken continuation)
- Merge-only tag semantics (PATCH not PUT, never remove existing tags)
- Error handling: throttling (429), permissions (403), locked resources (409)
- V2 rules format: natural language assessment field
- Audit logging: PartitionKey=subscriptionId, RowKey={resource}_{tag}_{timestamp}
- Large result sets (1500+ resources)
- Confidence-based auto-apply logic
- Partial failures (some resources succeed, others fail)

## Cross-Agent Context

### 2026-03-05: Infrastructure Ready (Parker, Session 2333)
- Parker completed Bicep infrastructure scaffold with 7 modules (identity, storage, ACR, Foundry, RBAC, functions)
- Both managed identities provisioned; azd integration ready for deployment
- Test stubs may need updates as Dallas implements real service interfaces

### 2026-03-05: Framework Update & Agent-4 Rework (Dallas ongoing)
- Matthew approved switch to **Microsoft Agent Framework** — Dallas agent-4 is reworking SDK integration
- Tool implementations will change: tools are now C# methods, framework auto-discovers and marshals parameters
- Test expectations: Service interfaces (IResourceGraphService, ITaggingService, IRulesService) remain stable
- Rules format is v2 (natural language assessments); test stubs already expect this format
- Confidence-based auto-apply logic for rules is now part of core design (agent-4 implementation)
- See `.squad/decisions/decisions.md` for complete framework rationale

