#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# set-local-creds.sh — Fetch Foundry + App Insights credentials from the
# deployed BillDev stack and pipe them into the Aspire AppHost's
# user-secrets store so local runs can hit the real LLMs.
#
# Every az call pins --subscription explicitly; never relies on the
# current-az-context being set correctly.
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"
APPHOST_PROJECT="${REPO_ROOT}/aspire/MovieRatingAgent.AppHost"

if ! az account show --subscription "$AZURE_SUBSCRIPTION_ID" --query id -o tsv >/dev/null 2>&1; then
  echo "==> ERROR: cannot access subscription ${AZURE_SUBSCRIPTION_ID} (${AZURE_SUBSCRIPTION_NAME:-?})."
  echo "    Run 'az login' first."
  exit 1
fi

echo "==> Fetching Foundry credentials from ${AZURE_SUBSCRIPTION_NAME:-$AZURE_SUBSCRIPTION_ID}..."
ENDPOINT="$(az cognitiveservices account show \
  --subscription "$AZURE_SUBSCRIPTION_ID" \
  --name "${AI_NAME}" \
  --resource-group "${RESOURCE_GROUP}" \
  --query properties.endpoint -o tsv)"
API_KEY="$(az cognitiveservices account keys list \
  --subscription "$AZURE_SUBSCRIPTION_ID" \
  --name "${AI_NAME}" \
  --resource-group "${RESOURCE_GROUP}" \
  --query key1 -o tsv)"

echo "    Endpoint: ${ENDPOINT}"

echo "==> Fetching Application Insights connection string..."
APPI_CONN_STR="$(az monitor app-insights component show \
  --subscription "$AZURE_SUBSCRIPTION_ID" \
  --app "${APPI_NAME}" \
  --resource-group "${RESOURCE_GROUP}" \
  --query connectionString -o tsv 2>/dev/null)" || true

echo "==> Setting user secrets for Aspire AppHost..."
dotnet user-secrets set "Parameters:foundry-endpoint" "${ENDPOINT}" --project "${APPHOST_PROJECT}"
dotnet user-secrets set "Parameters:foundry-apikey" "${API_KEY}" --project "${APPHOST_PROJECT}"
dotnet user-secrets set "Parameters:foundry-modelid" "gpt-5.4" --project "${APPHOST_PROJECT}"

if [[ -n "$APPI_CONN_STR" ]]; then
  dotnet user-secrets set "APPLICATIONINSIGHTS_CONNECTION_STRING" "${APPI_CONN_STR}" --project "${APPHOST_PROJECT}"
fi

echo "==> Done."
