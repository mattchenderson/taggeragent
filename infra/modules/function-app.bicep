@description('Name of the environment used for resource naming')
param environmentName string

@description('Primary location for all resources')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource ID of the function managed identity')
param functionIdentityId string

@description('Foundry endpoint URL')
param foundryEndpoint string

@description('Storage account name for function app state')
param storageAccountName string

@description('Blob endpoint URL for rules storage')
param blobEndpoint string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Target subscription ID for resource tagging')
param targetSubscriptionId string

@description('Timer schedule in NCRONTAB format')
param timerSchedule string

var functionAppName = '${environmentName}-${take(resourceToken, 6)}-func'
var hostingPlanName = '${environmentName}-${take(resourceToken, 6)}-func-plan'

// Flex Consumption plan for Azure Functions
resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: hostingPlanName
  location: location
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: {
    'azd-service-name': 'functions'
  }
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${functionIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}/function-deployments'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: functionIdentityId
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      appSettings: [
        {
          name: 'AZURE_CLIENT_ID'
          value: reference(functionIdentityId, '2023-01-31').clientId
        }
        {
          name: 'AZURE_AI_PROJECT_ENDPOINT'
          value: foundryEndpoint
        }
        {
          name: 'AGENT_NAME'
          value: 'tagger-agent'
        }
        {
          name: 'AZURE_SUBSCRIPTION_ID'
          value: targetSubscriptionId
        }
        {
          name: 'TIMER_SCHEDULE'
          value: timerSchedule
        }
        {
          name: 'STORAGE_ACCOUNT_NAME'
          value: storageAccountName
        }
        {
          name: 'RULES_STORAGE_URL'
          value: blobEndpoint
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

output functionAppId string = functionApp.id
output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
