@description('Environment name (e.g. dev, staging, prod).')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Tags applied to all resources in this module.')
param tags object = {}

@description('Short, deterministic suffix used to make the storage account name globally unique. See main.bicep for derivation.')
param resourceToken string

var storageAccountName = 'stmra${environmentName}${resourceToken}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource jobsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'jobs'
  properties: {
    publicAccess: 'None'
  }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource jobRequestsQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'job-requests'
}

@secure()
@description('Storage account connection string for AzureWebJobsStorage.')
output storageConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

@description('Storage account name.')
output storageAccountName string = storageAccount.name
