@description('Environment name (e.g. dev, staging, prod).')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Tags applied to all resources in this module.')
param tags object = {}

@description('Short, deterministic suffix used to make the Cognitive Services custom subdomain globally unique. See main.bicep for derivation.')
param resourceToken string

@description('When true, deploy the gpt-5.4 model. Requires the subscription to have quota for that preview model — most subs do not. Leave false to deploy only the GA models (gpt-4o, gpt-4o-mini).')
param deployGpt54 bool = false

@description('Which deployment to expose as the default model id (passed to the Function App). Must be one of the deployments below.')
@allowed(['gpt-5.4', 'gpt-4o', 'gpt-4o-mini'])
param defaultModelId string = 'gpt-4o-mini'

var aiServicesName = 'ai-movie-rating-agent-${environmentName}-${resourceToken}'

resource aiServices 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: aiServicesName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: aiServicesName
    publicNetworkAccess: 'Enabled'
  }
}

// Sequence the deployments so the Cognitive Services backend doesn't reject
// concurrent capacity reservations within the same account.
resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiServices
  name: 'gpt-4o'
  sku: {
    name: 'GlobalStandard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
  }
}

resource gpt4oMiniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiServices
  name: 'gpt-4o-mini'
  dependsOn: [gpt4oDeployment]
  sku: {
    name: 'GlobalStandard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      version: '2024-07-18'
    }
  }
}

// gpt-5.4 is gated behind a preview model allow-list. Only deploy when the
// caller explicitly opts in (and the subscription has quota approved).
resource gpt54Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (deployGpt54) {
  parent: aiServices
  name: 'gpt-5.4'
  dependsOn: [gpt4oMiniDeployment]
  sku: {
    name: 'GlobalStandard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-5.4'
      version: '2026-03-05'
    }
  }
}

@description('Azure AI Services endpoint URL.')
output foundryEndpoint string = aiServices.properties.endpoint

@secure()
@description('Azure AI Services primary key.')
output foundryKey string = aiServices.listKeys().key1

@description('Azure AI Services resource name.')
output aiServicesName string = aiServices.name

@description('The deployment name passed to the Function App as Foundry__ModelId.')
output modelDeploymentName string = defaultModelId
