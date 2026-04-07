@description('Environment name (e.g. dev, staging, prod).')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Tags applied to all resources in this module.')
param tags object = {}

@description('Short, deterministic suffix used to make the Function App hostname (*.azurewebsites.net) globally unique. See main.bicep for derivation.')
param resourceToken string

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

var functionAppName = 'func-movie-rating-agent-${environmentName}-${resourceToken}'
var appServicePlanName = 'plan-movie-rating-agent-${environmentName}'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
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
  tags: tags
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
        // The SWA linked-backend proxy is the supported path for browser
        // traffic, so direct cross-origin calls to *.azurewebsites.net
        // should not be needed. Keep '*' for diagnostic curl/httpie use
        // during development; tighten before going public.
        allowedOrigins: ['*']
      }
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        // Honor OpenTelemetry semconv: identify the service for App Insights
        // role-name / cloud_RoleName so spans are grouped sensibly.
        { name: 'OTEL_SERVICE_NAME', value: functionAppName }
        { name: 'OTEL_RESOURCE_ATTRIBUTES', value: 'service.namespace=movie-rating-agent,deployment.environment=${environmentName}' }
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
