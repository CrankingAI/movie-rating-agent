@description('Environment name (e.g. dev, staging, prod).')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Storage account connection string for AzureWebJobsStorage.')
@secure()
param storageConnectionString string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Azure AI Foundry endpoint URL.')
param foundryEndpoint string

@description('Azure AI Foundry API key.')
@secure()
param foundryKey string

@description('Azure AI Foundry model deployment name.')
param foundryModelId string = 'gpt-5.4'

var functionAppName = 'func-movie-rating-agent-${environmentName}'
var appServicePlanName = 'plan-movie-rating-agent-${environmentName}'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      cors: {
        allowedOrigins: ['*']
      }
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'Foundry__Endpoint', value: foundryEndpoint }
        { name: 'Foundry__ApiKey', value: foundryKey }
        { name: 'Foundry__ModelId', value: foundryModelId }
      ]
    }
  }
}

@description('Function App default hostname.')
output functionAppHostname string = functionApp.properties.defaultHostName

@description('Function App resource name.')
output functionAppName string = functionApp.name
