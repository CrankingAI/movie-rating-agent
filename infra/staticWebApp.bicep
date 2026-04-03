@description('Environment name (e.g. dev, staging, prod).')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Function App hostname for API backend.')
param functionAppHostname string

var staticWebAppName = 'swa-movie-rating-agent-${environmentName}'

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    stagingEnvironmentPolicy: 'Enabled'
    allowConfigFileUpdates: true
    buildProperties: {
      appLocation: '/swa'
      outputLocation: '/swa'
    }
  }
}

resource backendLink 'Microsoft.Web/staticSites/linkedBackends@2023-12-01' = {
  parent: staticWebApp
  name: 'functionAppBackend'
  properties: {
    backendResourceId: resourceId('Microsoft.Web/sites', 'func-movie-rating-agent-${environmentName}')
    region: location
  }
}

@description('Static Web App default hostname.')
output staticWebAppHostname string = staticWebApp.properties.defaultHostname
