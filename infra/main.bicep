targetScope = 'subscription'

@description('Environment name used for resource naming.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string = 'dev'

@description('Azure region for all resources.')
param location string = 'eastus2'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-movie-rating-agent-${environmentName}'
  location: location
}

module monitoring 'monitoring.bicep' = {
  name: 'monitoring'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
  }
}

module storage 'storage.bicep' = {
  name: 'storage'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
  }
}

module foundry 'foundry.bicep' = {
  name: 'foundry'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
  }
}

module functionApp 'functionApp.bicep' = {
  name: 'functionApp'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    storageConnectionString: storage.outputs.storageConnectionString
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    foundryEndpoint: foundry.outputs.foundryEndpoint
    foundryKey: foundry.outputs.foundryKey
    foundryModelId: foundry.outputs.modelDeploymentName
  }
}

module staticWebApp 'staticWebApp.bicep' = {
  name: 'staticWebApp'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    functionAppHostname: functionApp.outputs.functionAppHostname
  }
}

@description('Resource group name.')
output resourceGroupName string = resourceGroup.name

@description('Function App hostname.')
output functionAppHostname string = functionApp.outputs.functionAppHostname

@description('Azure AI Services endpoint.')
output foundryEndpoint string = foundry.outputs.foundryEndpoint

@description('Storage account name.')
output storageAccountName string = storage.outputs.storageAccountName

@description('Static Web App hostname.')
output staticWebAppHostname string = staticWebApp.outputs.staticWebAppHostname
