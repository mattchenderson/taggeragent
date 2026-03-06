# TaggerAgent Tests

This project contains unit and integration tests for the TaggerAgent application.

## Test Framework

- **xUnit** — Modern .NET test framework
- **Moq** — Mocking framework for Azure SDK services
- **FluentAssertions** — Readable assertion library

## Test Structure

```
tests/TaggerAgent.Tests/
├── Tools/              # Tests for MCP tools
│   ├── ScanResourcesToolTests.cs
│   ├── ApplyTagsToolTests.cs
│   └── GetTaggingRulesToolTests.cs
├── Services/           # Tests for service layer
│   ├── ResourceGraphServiceTests.cs
│   ├── TaggingServiceTests.cs
│   ├── RulesServiceTests.cs
│   └── AuditServiceTests.cs
└── TaggerAgent.Tests.csproj
```

## Running Tests

### Run all tests
```bash
dotnet test
```

### Run tests from project root
```bash
dotnet test tests/TaggerAgent.Tests/TaggerAgent.Tests.csproj
```

### Run tests with detailed output
```bash
dotnet test --verbosity normal
```

### Run only unit tests (skip integration tests)
```bash
dotnet test --filter "Category!=Integration"
```

### Run specific test class
```bash
dotnet test --filter "FullyQualifiedName~ScanResourcesToolTests"
```

## Test Categories

- **Unit Tests** — Fast, mocked tests (default, no trait required)
- **Integration Tests** — Require real Azure resources, marked with `[Trait("Category", "Integration")]`

Integration tests are skipped in CI by default. To run them locally:
```bash
dotnet test --filter "Category=Integration"
```

## Test Patterns

All tests follow the **Arrange-Act-Assert** pattern:

```csharp
[Fact]
public void MethodName_Condition_ExpectedResult()
{
    // Arrange
    var mockService = new Mock<IService>();
    mockService.Setup(x => x.Method()).Returns(expectedValue);
    
    // Act
    var result = systemUnderTest.Method();
    
    // Assert
    result.Should().Be(expectedValue);
}
```

## Mocking Azure SDK

Use Moq to mock Azure SDK interfaces:

```csharp
var mockClient = new Mock<ResourceGraphClient>();
mockClient
    .Setup(x => x.ResourcesAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(mockResponse);
```

## Coverage

Run tests with coverage:
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```
