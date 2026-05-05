// First-pass parameters: deploy the full stack with no custom domain.
// After Cloudflare DNS records are in place (see TRANSITION.md), switch to
// main.with-domain.bicepparam to bind movieratingagent.com to the SWA.
//
// gpt-5.4 and gpt-5.5 are gated behind quota that BillDev (and most fresh
// subs) does not have by default. Leave the deployGpt5* flags false until
// you have requested and been granted quota; the agent runs fine on
// gpt-4o-mini. gpt-5.5 region availability is limited — eastus2 is fine.
using './main.bicep'

param environmentName = 'dev'
param location = 'eastus2'
param customDomain = ''
param includeWwwSubdomain = true
param deployGpt54 = false
param deployGpt55 = false
param defaultModelId = 'gpt-4o-mini'
