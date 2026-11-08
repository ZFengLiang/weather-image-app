@description('Location for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Base name used to derive the Function App name.')
param appBaseName string = 'weatherimg'

@description('Globally-unique Function App name.')
param functionAppName string = '${appBaseName}-${uniqueString(resourceGroup().id)}'

@description('Globally-unique Storage Account name (<=24 lowercase chars).')
param storageAccountName string = 'st${uniqueString(resourceGroup().id)}'

var hostingPlanName = '${appBaseName}-plan'
var appInsightsName = '${appBaseName}-ai'
var logAnalyticsName = '${appBaseName}-logs'

// ---------------------------------------------------------------------------
// Storage account (blobs + queues + table)
// ---------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // Container is private; finished images are served via SAS tokens.
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'images'
  properties: {
    publicAccess: 'None'
  }
}

// Flex Consumption stores the deployed app package in this blob container.
resource deployContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'app-package'
  properties: {
    publicAccess: 'None'
  }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// Two queues: one to start a job, one per station image (fan-out).
resource startQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'start-queue'
}

resource imageQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'image-queue'
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource statusTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: 'jobstatus'
}

// ---------------------------------------------------------------------------
// Observability
// ---------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------------
// Function App (Linux Flex Consumption)
//
// The "Azure for Students" subscription has zero quota for the classic
// Consumption plan (Y1), so this uses the Flex Consumption plan (FC1), which
// is serverless/pay-per-use and does not draw on the Y1 quota.
// ---------------------------------------------------------------------------
resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: hostingPlanName
  location: location
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  kind: 'functionapp'
  properties: {
    // Linux
    reserved: true
  }
}

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deployContainer.name}'
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          value: storageConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
      ]
    }
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
output storageAccountName string = storage.name
output apiBaseUrl string = 'https://${functionApp.properties.defaultHostName}/api'
