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

// Capability host — enables the Agents API on the Foundry account
resource capabilityHost 'Microsoft.CognitiveServices/accounts/capabilityHosts@2025-10-01-preview' = if (enableHostedAgents) {
  parent: foundryAccount
  name: 'agents'
  properties: {
    capabilityHostKind: 'Agents'
    enablePublicHostingEnvironment: true
  }
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

// ACR connection on the project so the agent service can pull images
resource acrConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-04-01-preview' = if (enableHostedAgents) {
  parent: foundryProject
  name: 'acr-connection'
  properties: {
    category: 'ContainerRegistry'
    target: acr!.properties.loginServer
    authType: 'ManagedIdentity'
    isSharedToAll: true
    credentials: {
      clientId: foundryProject.identity.principalId
      resourceId: acr!.id
    }
    metadata: {
      ResourceId: acr!.id
    }
  }
}

// AcrPull role for the project identity so the agent can pull container images
resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableHostedAgents) {
  scope: acr!
  name: guid(acr!.id, foundryProject.name, '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  properties: {
    principalId: foundryProject.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
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

// Cognitive Services User role for the project identity on the account
resource projectCogServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: foundryAccount
  name: guid(foundryAccount.id, foundryProject.name, 'a97b65f3-24c7-4388-baec-2e87135dc908')
  properties: {
    principalId: foundryProject.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
  }
}

output accountId string = foundryAccount.id
output accountName string = foundryAccount.name
output accountPrincipalId string = foundryAccount.identity.principalId
output projectPrincipalId string = foundryProject.identity.principalId
output endpoint string = foundryProject.properties.endpoints['AI Foundry API']
output openAiEndpoint string = foundryAccount.properties.endpoints['OpenAI Language Model Instance API']
output projectId string = foundryProject.id
output projectName string = foundryProject.name
output acrLoginServer string = enableHostedAgents ? acr!.properties.loginServer : ''
