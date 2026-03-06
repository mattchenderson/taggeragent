# Parker — History

## Project Context

- **Project:** taggeragent — An Azure resource tagging agent using Foundry hosted agents
- **Stack:** C#, Azure Foundry Hosted Agents, azd, Bicep
- **What it does:** Scans Azure resources across a subscription and helps tag them according to rules (rules TBD)
- **User:** Matthew Henderson

## Learnings

### 2026-03-05: Fixed agent.yaml Validation Error

**Problem:** `azd up` failed with error: `template.kind must be one of: [prompt hosted workflow], got ''`

**Root Cause:** The `agent.yaml` was using an `AgentManifest` wrapper structure (with `template:` as a child containing the agent definition), but the azd extension expects a bare `ContainerAgent` object at the root level.

**Incorrect structure:**
```yaml
name: tagger-agent
template:           # <-- This wrapper was the problem
  kind: hosted
  protocols: ...
```

**Correct structure:**
```yaml
kind: hosted        # <-- ContainerAgent directly at root
name: tagger-agent
protocols: ...
```

**Key Learnings:**
- The `agent.yaml` for azd extension (v0.1.13-preview) expects the AgentDefinition (e.g., `ContainerAgent`) directly at root, NOT wrapped in an `AgentManifest` structure
- AgentManifest (with `template:` field) is the full AgentSchema spec, but azd extension wants just the inner ContainerAgent
- The valid `kind` values are: `prompt`, `hosted`, `workflow` (case-sensitive, lowercase)
- When `kind` is at the root, the extension validates it correctly
- Extension version 0.1.13-preview requires this structure; schema may evolve

**Files changed:**
- `src/TaggerAgent/agent.yaml` - Removed `template:` wrapper, moved `kind: hosted` to root level

### 2025-07-24
- Reworked infrastructure to use `azd ai agent` extension for Foundry hosted agent deployment. Updated `azure.yaml` and created `src/TaggerAgent/agent.yaml` with agent declaration. Consolidated `foundry.bicep`, `acr.bicep`, and `model-deployment.bicep` into single module. Switched agent identity from user-assigned to Foundry account system-assigned managed identity. Removed AcrPull RBAC; extension handles ACR. Extended `main.bicep` with extension parameters for deployments and connections.

### 2025-07-24: Reworked to azd ai agent Extension

Adopted the `azd ai agent` extension (`azure.ai.agents >= 0.1.0-preview`) for Foundry hosted
agent deployment. Key changes:

**What the extension handles (removed from our Bicep):**
- ACR provisioning and image build/push — deleted `acr.bicep`
- Model deployments from `azure.yaml` config — deleted `model-deployment.bicep`
- Agent registration and container lifecycle
- The extension populates `AI_PROJECT_DEPLOYMENTS`, `AI_PROJECT_CONNECTIONS`,
  `AI_PROJECT_DEPENDENT_RESOURCES`, `ENABLE_HOSTED_AGENTS` as environment variables that
  flow into Bicep parameters via `main.parameters.json`

**What we still manage in Bicep:**
- Foundry account + project (consolidated into `foundry.bicep` with system-assigned identity)
- Storage account with `rules` blob container and `TagAuditLog` table
- Function app (Flex Consumption, timer trigger)
- Function managed identity (user-assigned)
- Subscription-scoped RBAC: Reader + Tag Contributor for agent, Cognitive Services User +
  Storage Table Data Reader for function

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

**File paths:**
- `azure.yaml` - azd manifest defining agent (containerapp) and functions services
- `infra/main.bicep` - subscription-scoped orchestration template
- `infra/main.parameters.json` - environment parameters with azd token substitution
- `infra/modules/identity.bicep` - two user-assigned managed identities
- `infra/modules/storage.bicep` - storage account with blob/table resources
- `infra/modules/acr.bicep` - container registry (Basic SKU)
- `infra/modules/foundry.bicep` - AI Services account + project
- `infra/modules/model-deployment.bicep` - gpt-4o deployment
- `infra/modules/role-assignments.bicep` - subscription and resource-scoped RBAC
- `infra/modules/function-app.bicep` - Flex Consumption function app

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

**Cross-subscription RBAC limitation:** Bicep subscription-scoped templates can only create role assignments within their own subscription. Added conditional logic: if `targetSubscriptionId != subscription().subscriptionId`, the Reader and Tag Contributor role assignments are skipped. For cross-subscription scenarios, those roles must be assigned manually or via separate deployment to target subscription.

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

**Additional Fix - Foundry Project Location:**
Added missing `location: location` property to `foundryProject` resource in `foundry.bicep`. The project resource requires explicit location (was causing "LocationRequired" deployment error).

**Files Changed:**
- Modified: `infra/main.bicep` (added resourceToken variable, passed to all modules)
- Modified: `infra/modules/foundry.bicep` (resourceToken param, updated names, added project location)
- Modified: `infra/modules/storage.bicep` (resourceToken param, updated name with 24-char limit)
- Modified: `infra/modules/identity.bicep` (resourceToken param, updated name)
- Modified: `infra/modules/monitoring.bicep` (resourceToken param, updated names)
- Modified: `infra/modules/function-app.bicep` (resourceToken param, updated names)

**Key Patterns:**
- `uniqueString()` uses subscription-scoped inputs only (no resourceGroup reference)
- `take(resourceToken, 6)` for readable 6-char suffix in most resources
- Storage accounts require special handling: lowercase, no hyphens, 24-char max
- ACR names: lowercase, alphanumeric only, 50-char max
- Token makes names deterministic per environment but unique across environments/subscriptions

**Validation:** All Bicep modules build successfully with 0 errors, only expected warnings (unused azd params, type validation limitations for preview APIs).

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

### 2026-03-06: Fixed KuduSpecializer Deployment Failure — Missing AzureWebJobsStorage

**Problem:** `azd deploy` for the `functions` service failed with:
`[KuduSpecializer] Kudu has been restarted after package deployed`

**Root Cause:** The Function App had NO `AzureWebJobsStorage` configuration. While the deployment storage was correctly configured in `functionAppConfig.deployment.storage` (blob container with managed identity), the Functions **runtime** needs a separate storage connection for internal operations (timer trigger state, lease management, internal queues). Without it, the host can't initialize after package deploy, causing specialization failure.

Additionally, the function identity's storage RBAC roles were insufficient for the Functions runtime:
- Had `Storage Blob Data Contributor` — needs `Storage Blob Data Owner` (for lease management)
- Had `Storage Table Data Reader` — needs `Storage Table Data Contributor` (for timer state)
- Missing `Storage Queue Data Contributor` (for internal queue management)

**Fix (2 files):**

1. `infra/modules/function-app.bicep` — Added identity-based `AzureWebJobsStorage` app settings:
   - `AzureWebJobsStorage__accountName` = storage account name
   - `AzureWebJobsStorage__credential` = `managedidentity`
   - `AzureWebJobsStorage__clientId` = user-assigned identity client ID
   These three settings together enable identity-based storage connection for the Functions runtime when `allowSharedKeyAccess: false`.

2. `infra/modules/storage-roles.bicep` — Upgraded function identity roles:
   - `Storage Blob Data Contributor` → `Storage Blob Data Owner` (`b7e6dc6d-f1e8-4753-8033-0f276bb0955b`)
   - `Storage Table Data Reader` → `Storage Table Data Contributor` (`0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3`)
   - Added `Storage Queue Data Contributor` (`974c5e8b-45b9-4653-ba55-5f855dd0fb88`)

**Verification:** Fresh `azd up` with environment `resourcetagger-fresh` — Functions service deployed successfully with no KuduSpecializer error.

**Key Learnings:**
- Flex Consumption `functionAppConfig.deployment.storage` is for **deploying packages** only — the Functions **runtime** still needs `AzureWebJobsStorage` separately
- With `allowSharedKeyAccess: false`, must use identity-based connection: `__accountName` + `__credential` + `__clientId` (for user-assigned identity)
- Functions runtime requires Storage Blob Data **Owner** (not Contributor) for lease management, plus Queue Data Contributor for internal operations
- The KuduSpecializer error is a symptom of the Functions host failing to start, not a deployment mechanism issue

### 2026-03-06 — Implemented Flex Consumption → Standard App Service Plan

**Assignment from Ripley:** Switch Functions hosting from Flex Consumption (FC1, Linux) to Standard (S1, Windows) due to OneDeploy + blob deployment incompatibilities with azd.

**Implementation Summary:**
1. **Modified file:** `infra/modules/function-app.bicep`
   - SKU: `FC1` → `S1` (Flex Consumption → Standard)
   - Kind: `functionapp,linux` → `functionapp` (switches to Windows)
   - Removed entire `functionAppConfig` block (Flex-only configuration)
   - Added runtime configuration: `FUNCTIONS_EXTENSION_VERSION = '~4'`, `FUNCTIONS_WORKER_RUNTIME = 'dotnet-isolated'`
   - Added siteConfig: `alwaysOn: true`, `netFrameworkVersion: 'v10.0'`, `use32BitWorkerProcess: false`

2. **Validated:** Ran `az bicep build --file infra/main.bicep` — clean build, no errors.

3. **Unchanged files verified:**
   - `azure.yaml` — azd auto-selects zip deploy for Standard plans (correct)
   - `infra/main.bicep` — module reference unchanged
   - `infra/modules/storage.bicep` — function-deployments container remains valid (used by all plan types)
   - `infra/modules/storage-roles.bicep` — RBAC unchanged
   - `infra/modules/identity.bicep` — identity unchanged

4. **Committed:** Deployed changes as commit 284cc73 with decision reference.

**Key Learning:** Flex Consumption uses OneDeploy (proprietary Microsoft blob-based deployment), not standard zip deploy. azd defaults to zip deploy for all function apps — this mismatch causes Kudu recycles and 404 polling errors. Standard/Dedicated plans use zip deploy natively, aligning with azd expectations.

**Cost Implications:** S1 ~$73/mo (can downgrade to B1 at ~$13/mo with one-line SKU change if needed post-validation).

**Cross-Agent Context:** Ripley's root cause analysis (azure-dev#3658) was decisive. Implementation followed Ripley's exact specification. Bicep validates clean and ready for deployment testing.

**Orchestration Log:** `.squad/orchestration-log/2026-03-06-parker-bicep-impl.md`

### 2025-07-25: Investigated azd Deploy Timeout for Hosted Agent

**Problem:** `azd deploy` timed out after 10 minutes at "Starting agent container" step. First ACR remote build for .NET 10 with prerelease NuGet packages is slow.

**Root Cause:** The 10-minute timeout is **hardcoded** in the azd `azure.ai.agents` extension (`service_target_agent.go`):
- `const waitForReady = true`
- `const maxWaitTime = 10 * time.Minute`

There is no config override — no env var, no azure.yaml field, no CLI flag. No open issues or PRs on Azure/azure-dev for this.

**`minReplicas: 0` does NOT help.** The extension always calls `startAgentContainer()` for hosted agents. Setting minReplicas to 0 just results in nil being sent (API uses its own default). The container start step cannot be skipped.

**Fix Applied — Dockerfile Optimization:**
- Eliminated redundant `dotnet build` step (publish includes build)
- Added `--runtime linux-x64` to restore for targeted package graph
- Added `--no-restore` to publish step
- Switched to `aspnet:10.0-noble-chiseled` runtime image (smaller = faster pull)

Expected savings: ~1.5-2 minutes total (build + pull).

**Key Learnings:**
- azd agent extension timeout is not configurable — hardcoded at 10 minutes in Go constants
- `waitForReady` is also hardcoded to `true` — no way to skip container readiness polling
- `minReplicas: 0` does not skip container start; StartAgentContainer is always called for hosted agents
- Subsequent deploys are much faster due to Docker layer caching in ACR
- If first deploy still times out, a retry will succeed quickly since the ACR image is already built
- Recommend filing upstream issue on Azure/azure-dev for configurable timeout

**Files Changed:**
- `src/TaggerAgent/Dockerfile` — Optimized build steps, switched to chiseled runtime image

**Decision:** `.squad/decisions/inbox/parker-timeout-fix.md`
