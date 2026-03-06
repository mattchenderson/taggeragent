# Skill: Environment Variable Contract Pattern

## When to use

When a project has multiple components (agent container, Function App, Bicep
infra) that share environment variable names and those names drift apart.

## Pattern

1. **Create a C# constants class** (`EnvironmentConfig.cs`) as the single source
   of truth. All env var names are string constants with XML doc comments.

2. **Code references the constants** instead of string literals.

3. **Bicep app settings and outputs** use the same string values by convention
   (Bicep has no way to reference C# constants, so this is enforced by review).

4. **Architecture docs** include a table listing each variable, which component
   uses it, and where it comes from.

## Resolution rule

When code and infra disagree on a name, check this priority:

1. **azd convention** (e.g., `AZURE_AI_PROJECT_ENDPOINT`) — always wins
2. **Foundry runtime convention** — second priority
3. **Infra output name** — third priority (cheaper to change code than infra
   outputs that other tooling depends on)
4. **Code name** — lowest priority; code adapts

## Files involved

- `src/{Project}/EnvironmentConfig.cs` — constants class
- `infra/main.bicep` — outputs
- `infra/modules/*.bicep` — app settings
- `docs/architecture.md` — documentation table
