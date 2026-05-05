// =============================================================================
// Movie Rating Agent — root orchestrator (subscription-scoped Bicep)
// -----------------------------------------------------------------------------
// Provisions one resource group containing the full stack:
//   * monitoring  (Log Analytics + Application Insights)
//   * storage     (blob container + queue)
//   * foundry     (Azure AI Services + 3 model deployments)
//   * functionApp (Linux App Service Plan + Function App)
//   * staticWebApp (SWA with linked-backend route to the Function App)
//
// Design notes:
//   - Each module is independent enough that the script wrappers (deploy.sh
//     --func-only / --swa-only) can re-run main.bicep idempotently while only
//     re-deploying the code for one tier. This keeps IaC simple while giving
//     us a true split deploy story.
//   - The custom domain on the SWA is opt-in via the customDomain parameter.
//     Leave it empty on first run; populate after Cloudflare DNS records are
//     in place (see TRANSITION.md).
//   - Resource tags propagate via the resourceGroup, giving every child a
//     consistent set of cost/ownership tags out of the box.
// =============================================================================

targetScope = 'subscription'

@description('Environment name used for resource naming.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string = 'dev'

@description('Azure region for all resources.')
param location string = 'eastus2'

@description('Custom apex domain to bind to the Static Web App. Leave empty to skip custom domain configuration on this deployment.')
param customDomain string = ''

@description('When true and customDomain is set, also bind the www.<customDomain> subdomain.')
param includeWwwSubdomain bool = true

@description('When true, deploy the gpt-5.4 preview model alongside the GA models. Requires preview-model quota in the target subscription.')
param deployGpt54 bool = false

@description('When true, deploy the gpt-5.5 model alongside the GA models. Tier 5 / Tier 6 subs have quota by default; lower tiers must request it first.')
param deployGpt55 bool = false

@description('Which Azure AI deployment to expose to the Function App as the default Foundry__ModelId.')
@allowed(['gpt-5.5', 'gpt-5.4', 'gpt-4o', 'gpt-4o-mini'])
param defaultModelId string = 'gpt-4o-mini'

@description('Tags applied to the resource group (and inherited by children that opt in).')
param tags object = {
  application: 'movie-rating-agent'
  environment: environmentName
  managedBy: 'bicep'
  repo: 'CrankingAI/movie-rating-agent'
}

// A short, deterministic token derived from the subscription ID. Used as a
// suffix on resource names that must be GLOBALLY unique (storage account,
// Cognitive Services custom subdomain, Function App hostname). Two different
// subs deploying this same template land on different resource names without
// any manual coordination, while a re-deploy in the same sub is fully
// idempotent. Bash can compute the same value with:
//   echo "$AZURE_SUBSCRIPTION_ID" | tr -d - | cut -c1-6
var resourceToken = take(replace(subscription().subscriptionId, '-', ''), 6)

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-movie-rating-agent-${environmentName}'
  location: location
  tags: tags
}

module monitoring 'monitoring.bicep' = {
  name: 'monitoring'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module storage 'storage.bicep' = {
  name: 'storage'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    resourceToken: resourceToken
  }
}

module foundry 'foundry.bicep' = {
  name: 'foundry'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    resourceToken: resourceToken
    deployGpt54: deployGpt54
    deployGpt55: deployGpt55
    defaultModelId: defaultModelId
  }
}

module functionApp 'functionApp.bicep' = {
  name: 'functionApp'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    resourceToken: resourceToken
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
    tags: tags
    functionAppName: functionApp.outputs.functionAppName
    customDomain: customDomain
    includeWwwSubdomain: includeWwwSubdomain
  }
}

module workbook 'workbook.bicep' = {
  name: 'workbook'
  scope: resourceGroup
  params: {
    appInsightsName: monitoring.outputs.appInsightsName
    location: location
    tags: tags
  }
}

@description('The deterministic suffix appended to globally-unique resource names.')
output resourceToken string = resourceToken

@description('Resource group name.')
output resourceGroupName string = resourceGroup.name

@description('Function App hostname (azurewebsites.net).')
output functionAppHostname string = functionApp.outputs.functionAppHostname

@description('Function App resource name.')
output functionAppName string = functionApp.outputs.functionAppName

@description('Azure AI Services endpoint.')
output foundryEndpoint string = foundry.outputs.foundryEndpoint

@description('Storage account name.')
output storageAccountName string = storage.outputs.storageAccountName

@description('Static Web App resource name.')
output staticWebAppName string = staticWebApp.outputs.staticWebAppName

@description('Static Web App default hostname.')
output staticWebAppHostname string = staticWebApp.outputs.staticWebAppHostname

@description('Application Insights resource name.')
output appInsightsName string = monitoring.outputs.appInsightsName

@description('Log Analytics workspace name.')
output logAnalyticsName string = monitoring.outputs.logAnalyticsName
