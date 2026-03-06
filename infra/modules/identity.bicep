@description('Name of the environment used for resource naming')
param environmentName string

@description('Primary location for all resources')
param location string

@description('Unique resource token for naming')
param resourceToken string

// Function managed identity (for agent invocation and storage access).
// The agent identity is now the Foundry account's system-assigned managed identity,
// provisioned by the foundry.bicep module.
resource functionIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${environmentName}-${take(resourceToken, 6)}-function-identity'
  location: location
}

output functionIdentityId string = functionIdentity.id
output functionIdentityPrincipalId string = functionIdentity.properties.principalId
output functionIdentityClientId string = functionIdentity.properties.clientId
