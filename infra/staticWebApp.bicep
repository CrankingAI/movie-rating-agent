@description('Environment name (e.g. dev, staging, prod).')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Function App resource name for API backend.')
param functionAppName string

@description('Tags applied to the Static Web App resource.')
param tags object = {}

@description('Custom domain name (apex). Empty string disables custom domain configuration.')
param customDomain string = ''

@description('Whether to also bind the www subdomain. Only applies when customDomain is set.')
param includeWwwSubdomain bool = true

var staticWebAppName = 'swa-movie-rating-agent-${environmentName}'
var hasCustomDomain = !empty(customDomain)
var wwwDomain = 'www.${customDomain}'

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
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
    backendResourceId: resourceId('Microsoft.Web/sites', functionAppName)
    region: location
  }
}

// Apex custom domain. Validation method 'dns-txt-token' issues a TXT token that
// must be present at the apex; once verified, point CNAME (with flattening) at
// the SWA's default hostname. Cloudflare supports CNAME flattening for apex.
resource apexDomain 'Microsoft.Web/staticSites/customDomains@2023-12-01' = if (hasCustomDomain) {
  parent: staticWebApp
  name: customDomain
  properties: {
    validationMethod: 'dns-txt-token'
  }
}

// www subdomain. Validation method 'cname-delegation' requires the CNAME record
// to already exist before this resource deploys, so the runbook step is to
// create the Cloudflare CNAME first, then run the SWA deploy.
resource wwwSubdomain 'Microsoft.Web/staticSites/customDomains@2023-12-01' = if (hasCustomDomain && includeWwwSubdomain) {
  parent: staticWebApp
  name: wwwDomain
  dependsOn: [apexDomain]
  properties: {
    validationMethod: 'cname-delegation'
  }
}

@description('Static Web App default hostname (e.g. nice-pebble-123.azurestaticapps.net).')
output staticWebAppHostname string = staticWebApp.properties.defaultHostname

@description('Static Web App resource name.')
output staticWebAppName string = staticWebApp.name

@description('Custom domain configured (empty if none).')
output customDomain string = customDomain
