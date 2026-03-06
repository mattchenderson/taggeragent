# Skill: azd ai agent Extension

## What it is

The `azd ai agent` extension (`azure.ai.agents`) enables deploying Foundry hosted agents via `azd`.
It handles container image build/push, model deployments, ACR provisioning, and agent registration.

## Installation

```bash
azd extension install azure.ai.agents
```

## azure.yaml pattern

```yaml
requiredVersions:
  extensions:
    azure.ai.agents: ">=0.1.0-preview"

services:
  my-agent:
    project: ./src/MyAgent
    host: azure.ai.agent
    language: docker
    docker:
      remoteBuild: true
    config:
      container:
        resources:
          cpu: "1"
          memory: 2Gi
        scale:
          maxReplicas: 3
          minReplicas: 1
      deployments:
        - model:
            format: OpenAI
            name: gpt-4o
            version: "2024-08-06"
          name: gpt-4o
          sku:
            capacity: 30
            name: Standard
```

## agent.yaml pattern

Place in the agent's source directory (e.g., `src/MyAgent/agent.yaml`):

```yaml
name: my-agent
description: Agent description
metadata:
  authors: [Team]
  tags: [tag1, tag2]
template:
  kind: hosted
  name: my-agent
  protocols:
    - protocol: responses
      version: v1
  environment_variables:
    - name: AZURE_OPENAI_ENDPOINT
      value: ${AZURE_OPENAI_ENDPOINT}
    - name: AZURE_OPENAI_DEPLOYMENT_NAME
      value: "{{chat}}"
resources:
  - name: chat
    kind: model
    id: gpt-4o
```

## Bicep integration

The extension populates these environment variables via pre-provision hooks, which flow into Bicep
parameters through `main.parameters.json`:

| Environment Variable | Bicep Parameter | Description |
|---|---|---|
| `AI_PROJECT_DEPLOYMENTS` | `aiProjectDeploymentsJson` | JSON array of model deployment specs |
| `AI_PROJECT_CONNECTIONS` | `aiProjectConnectionsJson` | JSON array of project connections |
| `AI_PROJECT_DEPENDENT_RESOURCES` | `aiProjectDependentResourcesJson` | JSON array of dependent resources |
| `ENABLE_HOSTED_AGENTS` | `enableHostedAgents` | Boolean flag for ACR provisioning |

## Standard Bicep outputs

The extension expects these outputs from `main.bicep`:

- `AZURE_AI_PROJECT_ID` — Foundry project ARM resource ID (required for `azd deploy`)
- `AZURE_AI_PROJECT_ENDPOINT` — Foundry project endpoint
- `AZURE_OPENAI_ENDPOINT` — OpenAI endpoint
- `AZURE_CONTAINER_REGISTRY_ENDPOINT` — ACR login server

**Critical:** `AZURE_AI_PROJECT_ID` must be set as an azd env variable for the extension to
know which Foundry project to deploy the hosted agent to. azd automatically maps Bicep outputs
to env variables, so outputting it from `main.bicep` is sufficient. The value is the full ARM
resource ID: `/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{account}/projects/{project}`.
Get it from `foundryProject.id` in Bicep.

**Critical:** `AZURE_AI_PROJECT_ENDPOINT` must be the Foundry **project** endpoint, NOT the
account endpoint. The account endpoint (`foundryAccount.properties.endpoint`) returns a
`.cognitiveservices.azure.com` URL; the agents API expects the project endpoint from
`foundryProject.properties.endpoints['AI Foundry API']` which returns a
`.services.ai.azure.com/api/projects/{project}` URL. Using the account endpoint causes a 404
with a double-slash in the URL path.

Similarly, `AZURE_OPENAI_ENDPOINT` should come from
`foundryAccount.properties.endpoints['OpenAI Language Model Instance API']` rather than
hardcoding the URL pattern.

## Constraints

- **Region:** Hosted agents currently require `northcentralus`
- **Preview:** Extension is `>= 0.1.0-preview` — schema may change
- **Identity:** Agent authenticates via Foundry account's system-assigned managed identity

## Reference repos

- Starter template: `Azure-Samples/azd-ai-starter-basic`
- Hosted agent samples: `microsoft-foundry/foundry-samples/samples/csharp/hosted-agents/`
