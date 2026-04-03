#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# run-batch-eval.sh — Run the batch evaluation matrix
#
# Usage:
#   ./scripts/run-batch-eval.sh
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"

# Fetch credentials if not already set
if [[ -z "${FOUNDRY_ENDPOINT:-}" || -z "${FOUNDRY_API_KEY:-}" ]]; then
  echo "==> Fetching Foundry credentials from Azure..."
  export FOUNDRY_ENDPOINT="$(az cognitiveservices account show --name "${AI_NAME}" --resource-group "${RESOURCE_GROUP}" --query properties.endpoint -o tsv)"
  export FOUNDRY_API_KEY="$(az cognitiveservices account keys list --name "${AI_NAME}" --resource-group "${RESOURCE_GROUP}" --query key1 -o tsv)"
  echo "    Endpoint: ${FOUNDRY_ENDPOINT}"
fi

echo "==> Running batch evaluation..."
echo "    Results will be written to ${REPO_ROOT}/eval-results/"
echo ""

dotnet run --project "${REPO_ROOT}/tools/MovieRatingAgent.BatchEval"
