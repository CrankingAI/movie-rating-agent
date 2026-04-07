#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# test-cloud.sh — Submit a movie to the deployed SWA and poll for the result.
#
# Usage:
#   ./scripts/test-cloud.sh                       # default: The Godfather
#   ./scripts/test-cloud.sh "Heat"                # custom movie
#   ./scripts/test-cloud.sh "Heat" custom.host    # custom host (e.g. www.movieratingagent.com)
#
# Hits the SWA hostname (NOT the Function App hostname) because the linked
# backend lockdown only allows traffic that comes through the SWA proxy.
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"

MOVIE="${1:-The Godfather}"
HOST="${2:-}"

if [[ -z "$HOST" ]]; then
  HOST="$(az staticwebapp show \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --name "$SWA_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query defaultHostname -o tsv)"
fi

BASE="https://${HOST}/api/jobs"

echo "==> Submitting: \"${MOVIE}\" to ${BASE}"

SUBMIT_RESPONSE="$(curl -s -X POST "${BASE}" -H 'Content-Type: application/json' -d "{\"topic\": \"${MOVIE}\"}" 2>&1)" || true
JOB_ID="$(echo "$SUBMIT_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('jobId',''))" 2>/dev/null)" || true

if [[ -z "$JOB_ID" ]]; then
  echo "Error: Failed to submit job. Response was:"
  echo "$SUBMIT_RESPONSE"
  exit 1
fi

echo "    Job ID: ${JOB_ID}"
while true; do
  RESPONSE="$(curl -s "${BASE}/${JOB_ID}")"
  STATUS="$(echo "$RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin)['meta']['Status'])" 2>/dev/null)" || true
  case "$STATUS" in
    0) echo "    [$(date +%H:%M:%S)] Queued..." ;;
    1) echo "    [$(date +%H:%M:%S)] Running..." ;;
    2) echo "    [$(date +%H:%M:%S)] Completed!"; echo "$RESPONSE" | python3 -m json.tool; exit 0 ;;
    3) echo "    [$(date +%H:%M:%S)] Failed!"; echo "$RESPONSE" | python3 -m json.tool; exit 1 ;;
    *) echo "    [$(date +%H:%M:%S)] Unknown status: ${STATUS}"; exit 1 ;;
  esac
  sleep 2
done
