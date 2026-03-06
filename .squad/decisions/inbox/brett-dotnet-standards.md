# .NET Best Practices Standards for TaggerAgent.Functions

**Date:** 2025-01-XX  
**Author:** Brett (Core Dev)  
**Status:** Applied  

## Decision

Established and applied comprehensive .NET best practices standards to the TaggerAgent.Functions project.

## Standards Applied

### Async Method Naming
- All async methods MUST use the `Async` suffix (e.g., `RunAsync`, not `Run`)
- Even Azure Functions timer triggers follow this convention

### CancellationToken Usage
- Timer trigger methods accept `CancellationToken` parameter
- CancellationToken passed to async operations where the SDK supports it
- Notable: Azure.AI.Agents.Persistent SDK has LIMITED CancellationToken support - only use where accepted

### ConfigureAwait Pattern
- ALL async calls in library/function code use `.ConfigureAwait(false)` to avoid context capture
- Prevents potential deadlocks in hosted environments (Azure Functions, ASP.NET Core)

### Access Modifiers
- Program class: `internal static` (not exposed outside assembly)
- Function classes: `public sealed` (Azure Functions runtime needs public access)
- Helper methods: `private` (implementation details)

### Code Organization
- Extract complex inline logic into private helper methods for:
  - Better testability (helper methods can be unit tested)
  - Better readability (each method has single responsibility)
  - Better maintainability (easier to understand flow)

### XML Documentation
- Public APIs require comprehensive XML doc comments
- Include `<summary>`, `<param>`, and `<returns>` tags
- Parameter descriptions should be clear and concise

### Modern C# Patterns
- File-scoped namespaces (reduces indentation)
- Nullable reference types enabled project-wide
- Primary constructors for dependency injection
- Modern using directive ordering

## Rationale

These standards ensure:
1. Code is maintainable and follows .NET ecosystem conventions
2. No deadlock risks in hosted environments
3. Better testability through method extraction
4. Clear API documentation for future developers
5. Consistency with Dallas's work on the main TaggerAgent project

## Team Impact

- Kane (Test Engineer) - Helper methods are easier to unit test
- Dallas (Agent Lead) - Standards align with TaggerAgent project patterns
- Parker (Infra) - No infrastructure changes required
