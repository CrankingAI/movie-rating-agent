// Second-pass parameters: bind movieratingagent.com (apex + www) to the SWA.
// Use ONLY after the Cloudflare DNS records described in TRANSITION.md are in
// place — otherwise the cname-delegation validation will fail.
using './main.bicep'

param environmentName = 'dev'
param location = 'eastus2'
param customDomain = 'movieratingagent.com'
param includeWwwSubdomain = true
param deployGpt54 = false
param defaultModelId = 'gpt-4o-mini'
