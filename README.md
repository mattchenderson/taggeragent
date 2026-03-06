# TaggerAgent

An AI-powered Azure resource tagging agent built with the Microsoft Agent Framework and deployed via Azure Developer CLI (azd). TaggerAgent scans Azure resources across a subscription and recommends or applies tags based on configurable natural language rules.

## What It Does

TaggerAgent helps you maintain consistent tagging practices across Azure resources by:

- **Scanning** all resources in an Azure subscription using Azure Resource Graph
- **Analyzing** resources against user-defined tagging rules
- **Recommending** tag changes based on natural language rules and resource properties
- **Applying** tags with optional user confirmation (interactive mode) or automatic application (timer-based mode)
- **Auditing** all tagging activity to Azure Table Storage

The agent operates within a single Azure subscription at a time, with per-subscription rule management to support different tagging policies across environments.

## Key Features

- **Natural Language Tagging Rules** — Define tagging policies as conversational assessments rather than rigid schemas. Users describe their tagging intent, and the agent learns and applies those rules.

- **Per-Subscription Rule Scoping** — Each Azure subscription maintains independent tagging rulesets, enabling flexible policies across development, staging, and production environments.

- **Dual Execution Modes**
  - *Interactive* — Chat-based conversation with the agent; all proposed changes require user confirmation before application.
  - *Automated* — Timer-triggered execution via Azure Functions with confidence-based auto-apply for high-confidence changes; low-confidence recommendations are logged for review.

- **Merge-Only Tag Semantics** — TaggerAgent never removes or overwrites existing tags; it only adds or updates tags according to rules. Existing tags are always preserved.

- **Audit Logging** — All tagging operations are logged to Azure Table Storage with timestamps, operator, confidence scores, and outcomes for compliance and troubleshooting.

## Technology Stack

- **Runtime** — C# / .NET 10
- **AI Framework** — Microsoft Agent Framework with OpenAI-compatible APIs (gpt-4o-mini model, configurable)
- **Model Hosting** — Azure AI Foundry (hosted agent with container deployment)
- **Resource Discovery** — Azure Resource Graph (KQL-based resource queries)
- **Data Storage** — Azure Blob Storage (rules), Azure Table Storage (audit logs)
- **Serverless** — Azure Functions (Flex Consumption plan) with timer trigger for automated runs
- **Infrastructure as Code** — Bicep with Azure Verified Modules
- **Deployment** — Azure Developer CLI (azd) with `azd ai agent` extension

## Project Structure

```
.
├── .squad/                      # Squad team configuration and decision history
├── src/
│   ├── TaggerAgent/             # Main hosted agent (Microsoft Agent Framework)
│   │   ├── Agent/
│   │   │   ├── TaggerAgentTools.cs  # Tool methods (scan, tag, rules)
│   │   │   └── AgentInstructions.cs # System prompt
│   │   ├── Models/              # Data models (TaggingRule, ResourceInfo, etc.)
│   │   ├── Services/            # Azure SDK service implementations
│   │   ├── Tools/               # Tool wrapper classes
│   │   ├── Program.cs           # Agent startup and DI
│   │   ├── EnvironmentConfig.cs # Shared environment variable contract
│   │   ├── agent.yaml           # Foundry agent definition
│   │   └── Dockerfile           # Container image definition
│   └── TaggerAgent.Functions/   # Azure Function for timer-based execution
│       ├── TimerScanFunction.cs # Timer trigger, invokes agent via Foundry API
│       └── Program.cs           # Functions host setup
├── infra/
│   ├── main.bicep               # Root infrastructure template
│   └── modules/                 # Bicep modules (foundry, storage, function, etc.)
├── tests/
│   └── TaggerAgent.Tests/       # Unit tests
├── docs/
│   └── architecture.md          # Detailed architecture and design decisions
├── azure.yaml                   # azd environment and service configuration
└── taggeragent.sln              # Visual Studio solution
```

## Getting Started

### Prerequisites

- Azure CLI or Azure Developer CLI (azd)
- .NET 10 SDK
- An active Azure subscription with Owner or Contributor role
- Docker (for local testing of the hosted agent container)

### Quick Start

1. **Clone and navigate to the repository:**
   ```bash
   git clone https://github.com/mattchenderson/taggeragent.git
   cd taggeragent
   ```

2. **Deploy to Azure:**
   ```bash
   azd up
   ```
   This command provisions all infrastructure (Foundry account, Function app, storage), builds and pushes the agent container, and deploys the solution end-to-end.
   
   **Note:** The first deploy may take 8-10 minutes as ACR pulls base images. See [Deployment](#deployment) section below for details.

3. **Interact with the agent:**
   - **Interactive mode:** Use Azure Portal, Azure CLI, or a custom chat interface to send messages to the agent (e.g., "Scan my subscription for untagged resources").
   - **Automated mode:** The timer function automatically runs on schedule and applies high-confidence tag changes.

4. **Define rules:**
   The agent guides you through creating tagging rules conversationally. Rules are stored per subscription in Blob Storage and can be managed, reviewed, and copied across subscriptions.

## Deployment

### First Deploy

The first `azd up` from a clean environment may take 8-10 minutes as Azure Container Registry (ACR) pulls base images from Microsoft Container Registry. This is expected behavior.

**Why it takes time:**
- ACR remote build pulls .NET SDK Alpine image (~200 MB compressed)
- ACR pulls .NET runtime Alpine image (~110 MB compressed)
- Builds and publishes the C# agent (~15 MB application code)
- Pushes final image to ACR
- Foundry creates the hosted agent container

**If the first deploy times out (10-minute limit):**
1. The infrastructure is already provisioned (Foundry, storage, Function App)
2. Run `azd deploy tagger-agent` again — cached layers complete in 2-3 minutes
3. OR run `azd deploy functions` first (warms ACR), then `azd deploy tagger-agent`

**Incremental deploys (code changes):**
Subsequent deploys use cached base image layers and complete in 2-3 minutes. Only the application layer rebuilds.

### Configuration

Key environment variables (set via `azd` and Bicep parameters):
- `AZURE_SUBSCRIPTION_ID` — Target subscription ID
- `RULES_STORAGE_URL` — Blob Storage URL for rules files
- `AZURE_AI_PROJECT_ENDPOINT` — Azure AI Foundry endpoint
- `STORAGE_ACCOUNT_NAME` — Storage account name for rules and audit logs

**Model Configuration:**

The default model is `gpt-4o-mini` (widely available, cost-efficient, excellent for structured tool-based tasks). To use a different model:

1. Edit `azure.yaml` → `services.tagger-agent.config.deployments[0].model.name`
2. Edit `src/TaggerAgent/agent.yaml` → `resources[0].id`
3. Run `azd up` to redeploy

Alternative models: `gpt-4o`, `gpt-35-turbo`. Ensure the model supports function calling and is available in your Azure subscription region.

See `azure.yaml` and `infra/main.parameters.json` for configuration details.

## Squad Integration

This project uses [Squad](https://github.com/bradygaster/squad/) for AI-assisted team orchestration and decision tracking. The `.squad/` directory contains:

- Team member definitions and roles
- Architecture and design decisions
- Agent conversation history and outcomes
- Project context and knowledge base

Squad helps the development team collaborate efficiently by capturing decisions, tracking work, and providing AI-assisted context across the project lifecycle. For more information, visit the Squad repository.

## Documentation

- **[Architecture Guide](./docs/architecture.md)** — Detailed design, flow diagrams, Foundry agent setup, tool implementations, and data storage patterns.

## License

See LICENSE file for details.

## Support

For issues, questions, or contributions, please open an issue or pull request on the repository.
