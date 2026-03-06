targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment used for resource naming')
param environmentName string

@minLength(1)
@description('Primary location for all resources (must be northcentralus for hosted agents)')
param location string

@description('Target subscription ID for resource tagging operations')
param targetSubscriptionId string = subscription().subscriptionId

@description('Timer schedule for automated scans (NCRONTAB format)')
param timerSchedule string = '0 0 2 * * *'

// Parameters populated by the azd ai agent extension pre-provision hooks
@description('JSON array of model deployments from azure.yaml config')
param aiProjectDeploymentsJson string = ''

@description('JSON array of project connections from azd extension')
param aiProjectConnectionsJson string = ''

@description('JSON array of dependent resources from azd extension')
param aiProjectDependentResourcesJson string = ''

@description('Whether hosted agents are enabled (set by azd extension)')
param enableHostedAgents bool = false

var resourceGroupName = '${environmentName}-rg'

// Generate unique resource token to prevent naming collisions across environments
// At subscription scope, we can't use resourceGroup().id, so use subscription + location + environmentName
var resourceToken = uniqueString(subscription().subscriptionId, environmentName, location)

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

// Foundry account, project, ACR, and model deployments (extension-integrated)
module foundry './modules/foundry.bicep' = {
  name: 'foundry'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    resourceToken: resourceToken
    aiProjectDeploymentsJson: aiProjectDeploymentsJson
    enableHostedAgents: enableHostedAgents
  }
}

// Function app managed identity
module identities './modules/identity.bicep' = {
  name: 'identities'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    resourceToken: resourceToken
  }
}

// Storage account with rules blob container and audit table
module storage './modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    resourceToken: resourceToken
  }
}

// Application Insights and Log Analytics
module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    resourceToken: resourceToken
  }
}

// Subscription-scoped RBAC for agent and function identities
module subscriptionRoles './modules/role-assignments.bicep' = {
  name: 'subscription-roles'
  params: {
    agentIdentityPrincipalId: foundry.outputs.accountPrincipalId
    functionIdentityPrincipalId: identities.outputs.functionIdentityPrincipalId
    foundryAccountId: foundry.outputs.accountId
    targetSubscriptionId: targetSubscriptionId
  }
}

// Storage-scoped RBAC for agent and function identities
module storageRoles './modules/storage-roles.bicep' = {
  name: 'storage-roles'
  scope: rg
  params: {
    agentIdentityPrincipalId: foundry.outputs.accountPrincipalId
    functionIdentityPrincipalId: identities.outputs.functionIdentityPrincipalId
    storageAccountName: storage.outputs.storageAccountName
  }
}

// Azure Functions timer service
module functionApp './modules/function-app.bicep' = {
  name: 'function-app'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    resourceToken: resourceToken
    functionIdentityId: identities.outputs.functionIdentityId
    foundryEndpoint: foundry.outputs.endpoint
    storageAccountName: storage.outputs.storageAccountName
    blobEndpoint: storage.outputs.blobEndpoint
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    targetSubscriptionId: targetSubscriptionId
    timerSchedule: timerSchedule
  }
  dependsOn: [
    storageRoles
  ]
}

// Standard outputs consumed by azd and the agent extension
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_SUBSCRIPTION_ID string = subscription().subscriptionId

output AZURE_AI_PROJECT_ID string = foundry.outputs.projectId
output AZURE_AI_PROJECT_ENDPOINT string = foundry.outputs.endpoint
output AZURE_OPENAI_ENDPOINT string = foundry.outputs.openAiEndpoint
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = foundry.outputs.acrLoginServer

output STORAGE_ACCOUNT_NAME string = storage.outputs.storageAccountName
output RULES_STORAGE_URL string = storage.outputs.blobEndpoint
output FUNCTION_APP_NAME string = functionApp.outputs.functionAppName
output FUNCTION_IDENTITY_CLIENT_ID string = identities.outputs.functionIdentityClientId
output APPLICATIONINSIGHTS_CONNECTION_STRING string = monitoring.outputs.appInsightsConnectionString
