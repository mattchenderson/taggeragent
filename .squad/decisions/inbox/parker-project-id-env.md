# Decision: AZURE_AI_PROJECT_ID Output from Bicep

**Date:** 2025-07-25
**Author:** Parker (Infra/DevOps)
**Status:** Implemented

## Context

`azd deploy` failed for the `tagger-agent` service because the azd `azure.ai.agents`
extension requires `AZURE_AI_PROJECT_ID` as an environment variable. This is the full ARM
resource ID of the Foundry project, distinct from `AZURE_AI_PROJECT_ENDPOINT` (the HTTP
endpoint).

## Decision

Output `AZURE_AI_PROJECT_ID` from Bicep so azd automatically sets it as an env variable:

- `foundry.bicep` outputs `projectId` = `foundryProject.id`
- `main.bicep` outputs `AZURE_AI_PROJECT_ID` = `foundry.outputs.projectId`

No changes needed to `azure.yaml` or `main.parameters.json` — azd maps Bicep outputs to
env variables automatically.

## Impact

- Unblocks `azd deploy` for the `tagger-agent` service
- No breaking changes to existing infrastructure
- Two files changed: `infra/modules/foundry.bicep`, `infra/main.bicep`
