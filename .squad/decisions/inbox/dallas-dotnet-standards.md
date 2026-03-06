# .NET Best Practices and Naming Standards

**Decided by:** Dallas (Core Dev)  
**Date:** 2025-01-XX  
**Status:** Implemented

## Decision

Applied comprehensive .NET best practices and naming standards across the TaggerAgent project:

1. **File/class name alignment** - All .cs files now contain a class matching the filename
2. **Sealed classes by default** - All classes not designed for inheritance marked `sealed`
3. **Async method naming** - All async methods follow the `Async` suffix convention
4. **XML documentation** - All public APIs documented with comprehensive XML comments
5. **Modern C# patterns** - File-scoped namespaces, primary constructors, sealed records

## Key Changes

- **TaggerAgent.cs → TaggerAgentTools.cs** - Fixed major naming violation (class name didn't match file)
- **Tool method names** - Added Async suffix: `ScanResourcesAsync`, `ApplyTagsAsync`, `GetTaggingRulesAsync`, `SaveTaggingRulesAsync`, `CopyTaggingRulesAsync`
- **Sealed modifier** - Applied to all Services, Tools, and Models classes
- **Enhanced XML docs** - Added parameter descriptions and return value documentation

## Rationale

- **File/class alignment** - Standard .NET convention for maintainability and discoverability
- **Sealed classes** - Performance optimization and design clarity (prevents unintended inheritance)
- **Async suffix** - .NET convention that makes async methods immediately recognizable
- **XML documentation** - Enables IntelliSense and API documentation generation
- **Modern patterns** - Aligns with .NET 10 and C# 13 best practices

## Interface Decision

**Did NOT add interfaces for Services** because:
- Current tests are placeholder tests, not mocks
- Tests use concrete classes directly
- Adding interfaces would be premature without a clear testing strategy
- Kane (Test Engineer) should drive testing architecture decisions

If mocking is needed in the future, interfaces can be added without breaking changes (dependency injection already in use).

## Impact

- Zero breaking changes to runtime behavior
- All builds successful
- All 52 tests pass
- Improved code maintainability and discoverability
- Better IntelliSense support for API consumers

## Files Modified

**Agent:**
- `Agent/TaggerAgent.cs` → `Agent/TaggerAgentTools.cs` (renamed)
- `Agent/AgentInstructions.cs` (XML docs)

**Services:**
- `Services/ResourceGraphService.cs` (sealed, XML docs)
- `Services/TaggingService.cs` (sealed, XML docs)
- `Services/RulesService.cs` (sealed, XML docs)
- `Services/AuditService.cs` (sealed, XML docs)

**Tools:**
- `Tools/ScanResourcesTool.cs` (sealed)
- `Tools/ApplyTagsTool.cs` (sealed)
- `Tools/GetTaggingRulesTool.cs` (sealed)

**Models:**
- `Models/TaggingRule.cs` (sealed record, XML docs)
- `Models/ResourceInfo.cs` (sealed record, XML docs)
- `Models/TagChange.cs` (sealed record, XML docs)
- `Models/AuditEntry.cs` (sealed class, XML docs)

**Program:**
- `Program.cs` (updated method references to Async versions)

## Follow-up

None required. Standards are now aligned with .NET best practices.
