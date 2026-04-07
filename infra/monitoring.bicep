@description('Environment name (e.g. dev, staging, prod).')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Tags applied to all resources in this module.')
param tags object = {}

var logAnalyticsName = 'log-movie-rating-agent-${environmentName}'
var appInsightsName  = 'appi-movie-rating-agent-${environmentName}'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
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
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    // Disable adaptive sampling at the App Insights level so the
    // Functions host (host.json) and the OpenTelemetry exporter remain
    // the single source of truth for sampling decisions.
    SamplingPercentage: 100
    DisableIpMasking: false
  }
}

@description('Application Insights connection string.')
output appInsightsConnectionString string = appInsights.properties.ConnectionString

@description('Application Insights instrumentation key.')
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey

@description('Application Insights resource name.')
output appInsightsName string = appInsights.name

@description('Log Analytics workspace ID.')
output logAnalyticsWorkspaceId string = logAnalytics.id

@description('Log Analytics workspace name.')
output logAnalyticsName string = logAnalytics.name
