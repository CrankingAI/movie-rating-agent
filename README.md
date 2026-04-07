# Movie Rating Agent

Rates movies using an AI Agent.

Not claiming to be awesome at rating movies (that's not currently a goal); intent is to demonstrate building an AI Agent.

## Deployment Note

In the deployed Azure setup, the Static Web App is linked to the Function App as its backend. Deployment verification should use the Static Web App route at `/api/readyz`, not the direct `https://func-...azurewebsites.net/api/readyz` host.

The direct Function App host can return `401 Unauthorized` in the linked-backend configuration even when the deployment is healthy. The user-facing and CI-valid health check is the Static Web App backend route.
