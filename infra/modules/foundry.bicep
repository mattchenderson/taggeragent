@description('Name of the environment used for resource naming')
param environmentName string

@description('Primary location for all resources')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('JSON array of model deployments from the azd ai agent extension')
param aiProjectDeploymentsJson string = ''

@description('Whether hosted agents are enabled (controls ACR provisioning)')
param enableHostedAgents bool = false

// Use resourceToken to ensure globally unique names
var accountName = '${environmentName}-${take(resourceToken, 6)}-foundry'
var projectName = 'tagger-project'
// ACR names: lowercase alphanumeric only, max 50 chars
var acrName = take(replace('${environmentName}${resourceToken}acr', '-', ''), 50)
var deployments = !empty(aiProjectDeploymentsJson) ? json(aiProjectDeploymentsJson) : []

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: accountName
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundryAccount
  name: projectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

// ACR — conditionally provisioned when hosted agents are enabled
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = if (enableHostedAgents) {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    networkRuleBypassOptions: 'AzureServices'
  }
}

// Model deployments driven by the extension's JSON parameter
@batchSize(1)
resource modelDeployments 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = [
  for deployment in deployments: {
    parent: foundryAccount
    name: deployment.name
    sku: deployment.sku
    properties: {
      model: deployment.model
    }
  }
]

output accountId string = foundryAccount.id
output accountName string = foundryAccount.name
output accountPrincipalId string = foundryAccount.identity.principalId
output endpoint string = foundryAccount.properties.endpoint
output openAiEndpoint string = 'https://${accountName}.openai.azure.com'
output projectId string = foundryProject.id
output projectName string = foundryProject.name
output acrLoginServer string = enableHostedAgents ? acr.properties.loginServer : ''
