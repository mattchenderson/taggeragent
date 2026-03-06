@description('Principal ID of the Foundry account system-assigned managed identity (used by the hosted agent)')
param agentIdentityPrincipalId string

@description('Principal ID of the function managed identity')
param functionIdentityPrincipalId string

@description('Storage account name')
param storageAccountName string

// Built-in role definitions
var storageBlobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var storageTableDataReaderRoleId = '76199698-9eea-4c19-bc75-cec21354c6b6'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

// --- Agent identity storage roles (resource-scoped) ---

// Storage Blob Data Reader (for reading tagging rules)
resource agentBlobReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, agentIdentityPrincipalId, storageBlobDataReaderRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
    principalId: agentIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Table Data Contributor (for writing audit log entries)
resource agentTableContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, agentIdentityPrincipalId, storageTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
    principalId: agentIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// --- Function identity storage roles (resource-scoped) ---

// Storage Table Data Reader (for reading audit log entries)
resource functionTableReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionIdentityPrincipalId, storageTableDataReaderRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataReaderRoleId)
    principalId: functionIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Blob Data Contributor (for function deployment container - required by Flex Consumption)
resource functionBlobContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionIdentityPrincipalId, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: functionIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
