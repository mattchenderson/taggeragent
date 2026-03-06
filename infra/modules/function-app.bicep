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

// Standard App Service plan for Azure Functions (Windows)
resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: hostingPlanName
  location: location
  sku: {
    name: 'S1'
    tier: 'Standard'
  }
  properties: {}
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: {
    'azd-service-name': 'functions'
  }
  kind: 'functionapp'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${functionIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v10.0'
      use32BitWorkerProcess: false
      alwaysOn: true
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: reference(functionIdentityId, '2023-01-31').clientId
        }
        // Identity-based AzureWebJobsStorage — required for Functions runtime
        // (timer state, lease management, internal queues). Uses user-assigned identity
        // since allowSharedKeyAccess is disabled on the storage account.
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__clientId'
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
