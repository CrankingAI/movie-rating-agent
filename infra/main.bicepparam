// First-pass parameters: deploy the full stack with no custom domain.
// After Cloudflare DNS records are in place (see TRANSITION.md), switch to
// main.with-domain.bicepparam to bind movieratingagent.com to the SWA.
//
// gpt-5.4 is gated behind preview-model quota that BillDevPlayground (and
// most fresh subs) does not have. Leave deployGpt54=false until you have
// requested and been granted that quota; the agent runs fine on gpt-4o-mini.
using './main.bicep'

param environmentName = 'dev'
param location = 'eastus2'
param customDomain = ''
param includeWwwSubdomain = true
param deployGpt54 = false
param defaultModelId = 'gpt-4o-mini'
