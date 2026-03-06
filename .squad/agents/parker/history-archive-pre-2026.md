# Parker — History Archive (Pre-2026-03-06)

This file contains archived learning entries from Parker's history. Active history is maintained in `history.md`.

## Archived Entries

### 2025-07-24
- Reworked infrastructure to use `azd ai agent` extension for Foundry hosted agent deployment. Updated `azure.yaml` and created `src/TaggerAgent/agent.yaml` with agent declaration. Consolidated `foundry.bicep`, `acr.bicep`, and `model-deployment.bicep` into single module. Switched agent identity from user-assigned to Foundry account system-assigned managed identity. Removed AcrPull RBAC; extension handles ACR. Extended `main.bicep` with extension parameters for deployments and connections.

### 2025-07-24: Reworked to azd ai agent Extension

Adopted the `azd ai agent` extension (`azure.ai.agents >= 0.1.0-preview`) for Foundry hosted agent deployment. Key changes:

**What the extension handles (removed from our Bicep):**
- ACR provisioning and image build/push — deleted `acr.bicep`
- Model deployments from `azure.yaml` config — deleted `model-deployment.bicep`
- Agent registration and container lifecycle
- The extension populates `AI_PROJECT_DEPLOYMENTS`, `AI_PROJECT_CONNECTIONS`, `AI_PROJECT_DEPENDENT_RESOURCES`, `ENABLE_HOSTED_AGENTS` as environment variables that flow into Bicep parameters via `main.parameters.json`

**What we still manage in Bicep:**
- Foundry account + project (consolidated into `foundry.bicep` with system-assigned identity)
- Storage account with `rules` blob container and `TagAuditLog` table
- Function app (Flex Consumption, timer trigger)
- Function managed identity (user-assigned)
- Subscription-scoped RBAC: Reader + Tag Contributor for agent, Cognitive Services User + Storage Table Data Reader for function

**Identity model change:**
- Agent identity changed from user-assigned to Foundry account's system-assigned managed identity
- `identity.bicep` now only creates the function identity
- Subscription-scoped RBAC assignments use `foundry.outputs.accountPrincipalId`

**File changes:**
- Created: `src/TaggerAgent/agent.yaml` (hosted agent definition with responses protocol)
- Updated: `azure.yaml` (host: azure.ai.agent, requiredVersions, config block)
- Updated: `infra/main.bicep` (extension parameter pattern, removed acr/model modules)
- Updated: `infra/main.parameters.json` (extension hook parameters)
- Updated: `infra/modules/foundry.bicep` (consolidated: account + project + ACR + deployments)
- Updated: `infra/modules/identity.bicep` (function identity only)
- Updated: `infra/modules/role-assignments.bicep` (removed AcrPull, uses system-assigned identity)
- Deleted: `infra/modules/acr.bicep`, `infra/modules/model-deployment.bicep`

**Key constraints:**
- Hosted agents require `northcentralus` region
- Extension is in preview — schema may change
- `agent.yaml` uses `template.kind: hosted` with `protocols: [{ protocol: responses, version: v1 }]`

### 2025-01-05: Initial Infrastructure Scaffold

Created complete azd project structure and Bicep infrastructure for TaggerAgent:

**Architecture decisions:**
- Subscription-scoped deployment (`targetScope = 'subscription'`) in main.bicep to support subscription-level RBAC assignments for Reader and Tag Contributor roles
- Two separate managed identities: one for agent (resource access), one for function (agent invocation)
- All authentication via managed identity - explicitly disabled shared key access on storage, ACR admin user
- Flex Consumption plan for Functions with dotnet-isolated runtime
- gpt-4o model deployment (changed from gpt-4.1 as gpt-4o is the latest available model)
- Storage configured with 'rules' blob container and 'TagAuditLog' table

**Key patterns:**
- Resource naming uses `environmentName` prefix with deterministic suffixes
- Storage account name strips hyphens for naming compliance
- Subscription-scoped role assignments use `guid()` for deterministic assignment names
- Function app configured with user-assigned identity and app settings injected via Bicep
- All outputs from main.bicep use SCREAMING_CASE for azd environment variable convention

### 2025-01-05: Infrastructure Validation & .NET 10 Upgrade

Comprehensive infrastructure validation after .NET 10 upgrade. Fixed 7 issues for deployment readiness:

**1. Runtime version:** Updated function-app.bicep from .NET 8.0 to 10.0. Confirmed .NET 10 supported on Flex Consumption with dotnet-isolated worker model (preview, but stable).

**2. Role assignment scoping:** Fixed overly broad storage RBAC. Created `storage-roles.bicep` (resource-scoped) to separate from `role-assignments.bicep` (subscription-scoped). Storage roles now scoped to storage account only:
- Agent: Blob Data Reader, Table Data Contributor
- Function: Table Data Reader, Blob Data Contributor (for deployment container)

**3. Missing deployment container:** Added `function-deployments` blob container to storage.bicep. Flex Consumption requires this for deployment package storage.

**4. Monitoring infrastructure:** Created `monitoring.bicep` module with Log Analytics workspace and Application Insights. Function app now has proper observability (telemetry was configured in code but infra was missing).

**5. App settings alignment:** Fixed environment variable naming mismatches between Bicep and EnvironmentConfig.cs:
- `FOUNDRY_ENDPOINT` → `AZURE_AI_PROJECT_ENDPOINT`
- Added `RULES_STORAGE_URL` (blob endpoint)
- Added `APPLICATIONINSIGHTS_CONNECTION_STRING`

**6. API versions:** Updated all resources to latest stable API versions:
- Microsoft.Web/* → 2024-04-01
- Microsoft.Storage/* → 2024-01-01

**7. Parameters file:** Reviewed and confirmed correct (azd token substitution, no changes needed).

**Files created:**
- `infra/modules/monitoring.bicep` (Log Analytics + App Insights)
- `infra/modules/storage-roles.bicep` (resource-scoped storage RBAC)

**Files modified:**
- `infra/modules/function-app.bicep` (runtime 10.0, app settings, AI param)
- `infra/modules/storage.bicep` (deployment container, API versions)
- `infra/modules/role-assignments.bicep` (removed storage roles)
- `infra/main.bicep` (wired monitoring/storage-roles, outputs)

**Key learning:** Subscription-scoped templates can't directly assign roles to resource-group resources. Need separate resource-group-scoped module for storage RBAC. Main.bicep orchestrates both scopes.

### 2025-01-06: Added resourceToken Pattern for Globally Unique Names

**Problem:** Resource names were deterministic (e.g., `${environmentName}-foundry`), causing "resource already exists" errors on redeployment to different environments or after soft-delete. Resources with global uniqueness requirements (storage accounts, ACR, Foundry account) conflicted across deployments.

**Root Cause:** No uniqueness token in resource naming. Standard azd pattern uses `uniqueString()` to generate deterministic-but-unique suffixes based on deployment context.

**Solution Implemented:**
1. Added `resourceToken` variable to `main.bicep`: `uniqueString(subscription().subscriptionId, environmentName, location)`
   - Uses subscription scope inputs since template is subscription-scoped (can't reference `resourceGroup().id` before RG exists)
   - Generates short, deterministic token unique per subscription/environment/location combination
2. Passed `resourceToken` parameter to all child modules
3. Updated resource naming in each module:
   - Foundry account: `${environmentName}-${take(resourceToken, 6)}-foundry`
   - ACR: `${environmentName}${resourceToken}acr` (lowercase, no hyphens, truncated to 50 chars)
   - Storage account: `take(toLower(replace('${environmentName}${resourceToken}st', '-', '')), 24)` (24-char limit, alphanumeric only)
   - Function app: `${environmentName}-${take(resourceToken, 6)}-func`
   - Managed identity: `${environmentName}-${take(resourceToken, 6)}-function-identity`
   - Monitoring: `${environmentName}-${take(resourceToken, 6)}-logs/insights`

**Key Patterns:**
- `uniqueString()` uses subscription-scoped inputs only (no resourceGroup reference)
- `take(resourceToken, 6)` for readable 6-char suffix in most resources
- Storage accounts require special handling: lowercase, no hyphens, 24-char max
- ACR names: lowercase, alphanumeric only, 50-char max
- Token makes names deterministic per environment but unique across environments/subscriptions

### 2025-07-25: Added AZURE_AI_PROJECT_ID Output for Agent Deployment

**Problem:** `azd deploy` succeeded for the `functions` service but failed for `tagger-agent` with: `ERROR: Microsoft Foundry project ID is required: AZURE_AI_PROJECT_ID is not set`.

**Root Cause:** The azd `azure.ai.agents` extension needs `AZURE_AI_PROJECT_ID` as an env variable to know which Foundry project to deploy the hosted agent to. The Foundry project resource ID was never output from Bicep.

**Fix (2 lines):**
1. `infra/modules/foundry.bicep` — Added `output projectId string = foundryProject.id` (the full ARM resource ID)
2. `infra/main.bicep` — Added `output AZURE_AI_PROJECT_ID string = foundry.outputs.projectId`

azd automatically maps Bicep outputs to env variables, so no changes needed to `azure.yaml` or `main.parameters.json`.

**Key Learning:** The azd agent extension requires `AZURE_AI_PROJECT_ID` (the ARM resource ID, not the endpoint). This is distinct from `AZURE_AI_PROJECT_ENDPOINT` which was already output. Both are needed.

### 2025-07-25: Fixed AZURE_AI_PROJECT_ENDPOINT — Account vs Project Endpoint

**Problem:** `azd deploy` for the tagger-agent returned 404 with a double-slash in the URL:
`POST https://...foundry.cognitiveservices.azure.com//agents/tagger-agent/versions`

**Root Cause:** `AZURE_AI_PROJECT_ENDPOINT` was set to `foundryAccount.properties.endpoint` (the Cognitive Services *account* endpoint), not the Foundry *project* endpoint. The agents API lives under the project endpoint, not the account.

- Account endpoint: `https://{name}.cognitiveservices.azure.com/` (wrong — causes 404 + double-slash)
- Project endpoint: `https://{name}.services.ai.azure.com/api/projects/{project}` (correct)

**Fix (foundry.bicep, 2 lines):**
1. `output endpoint` → `foundryProject.properties.endpoints['AI Foundry API']`
2. `output openAiEndpoint` → `foundryAccount.properties.endpoints['OpenAI Language Model Instance API']`

The previous OpenAI endpoint was a hardcoded URL construction (`https://${name}.openai.azure.com`); the reference sample uses the `endpoints` dictionary from the resource properties.

**Key Learnings:**
- Foundry account and project both have an `endpoint` property, but they are different URLs
- Use `properties.endpoints['AI Foundry API']` on the **project** resource for `AZURE_AI_PROJECT_ENDPOINT`
- Use `properties.endpoints['OpenAI Language Model Instance API']` on the **account** resource for `AZURE_OPENAI_ENDPOINT`
- Reference: `Azure-Samples/azd-ai-starter-basic` uses this exact pattern
- The function app also receives this endpoint via the `foundryEndpoint` parameter — both consumers (azd extension + function app) need the project endpoint

