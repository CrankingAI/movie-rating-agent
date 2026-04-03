#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"
APPHOST_PROJECT="${REPO_ROOT}/aspire/MovieRatingAgent.AppHost"

echo "==> Fetching Foundry credentials from Azure..."
ENDPOINT="$(az cognitiveservices account show --name "${AI_NAME}" --resource-group "${RESOURCE_GROUP}" --query properties.endpoint -o tsv)"
API_KEY="$(az cognitiveservices account keys list --name "${AI_NAME}" --resource-group "${RESOURCE_GROUP}" --query key1 -o tsv)"

echo "    Endpoint: ${ENDPOINT}"

echo "==> Fetching Application Insights connection string..."
APPI_CONN_STR="$(az monitor app-insights component show --app "${APPI_NAME}" --resource-group "${RESOURCE_GROUP}" --query connectionString -o tsv 2>/dev/null)" || true

echo "==> Setting user secrets for Aspire AppHost..."
dotnet user-secrets set "Parameters:foundry-endpoint" "${ENDPOINT}" --project "${APPHOST_PROJECT}"
dotnet user-secrets set "Parameters:foundry-apikey" "${API_KEY}" --project "${APPHOST_PROJECT}"

if [[ -n "$APPI_CONN_STR" ]]; then
  dotnet user-secrets set "APPLICATIONINSIGHTS_CONNECTION_STRING" "${APPI_CONN_STR}" --project "${APPHOST_PROJECT}"
fi

echo "==> Done."
