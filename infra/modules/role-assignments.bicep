targetScope = 'subscription'

@description('Principal ID of the Foundry account system-assigned managed identity (used by the hosted agent)')
param agentIdentityPrincipalId string

@description('Principal ID of the function managed identity')
param functionIdentityPrincipalId string

@description('Resource ID of the Foundry account')
param foundryAccountId string

@description('Target subscription ID for tagging operations')
param targetSubscriptionId string

// Built-in role definitions
var readerRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
var tagContributorRoleId = '4a9ae827-6dc8-4573-8ac7-8239d42aa03f'
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

// --- Agent identity (Foundry account system-assigned) ---

// Reader on target subscription (for Azure Resource Graph queries)
resource agentReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (targetSubscriptionId == subscription().subscriptionId) {
  name: guid(subscription().id, agentIdentityPrincipalId, readerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleId)
    principalId: agentIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Tag Contributor on target subscription (for applying tags to resources)
resource agentTagContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (targetSubscriptionId == subscription().subscriptionId) {
  name: guid(subscription().id, agentIdentityPrincipalId, tagContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tagContributorRoleId)
    principalId: agentIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// --- Function identity (user-assigned) ---

// Cognitive Services User on Foundry account (for invoking the agent)
resource functionCognitiveServicesUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccountId, functionIdentityPrincipalId, cognitiveServicesUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: functionIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
