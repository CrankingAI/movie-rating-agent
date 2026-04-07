#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# warmup-new-sub.sh — Prepare a fresh Azure subscription to host the Movie
# Rating Agent.
#
# What it does (idempotent, safe to re-run):
#   1. Verifies the caller can reach the target subscription.
#   2. Registers the resource providers Bicep needs.
#   3. Polls until each provider is in 'Registered' state.
#   4. Sanity-checks the Cognitive Services SKU + Foundry model availability
#      in the target region for the GA models the project deploys
#      (gpt-4o, gpt-4o-mini). Warns about gpt-5.4 (preview, opt-in via Bicep).
#   5. Prints next steps (run setup-oidc.sh, then trigger the GH Actions
#      deploy workflow).
#
# Usage:
#   ./scripts/warmup-new-sub.sh                      # uses sub from deploy-config.sh
#   AZURE_SUBSCRIPTION_ID=<id> ./scripts/warmup-new-sub.sh
#
# Every az call passes --subscription explicitly so this script is not
# sensitive to whichever sub happens to be "current" in the az context.
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"

# Providers Bicep modules need.
PROVIDERS=(
  Microsoft.Resources
  Microsoft.Web
  Microsoft.Storage
  Microsoft.CognitiveServices
  Microsoft.OperationalInsights
  Microsoft.Insights
)

GA_MODELS=(
  "gpt-4o:2024-11-20"
  "gpt-4o-mini:2024-07-18"
)

# ── Step 1: caller can reach the target subscription ────────────────────────
echo "==> Step 1/4: verifying access to target subscription..."
if ! az account show --subscription "$AZURE_SUBSCRIPTION_ID" --query id -o tsv >/dev/null 2>&1; then
  echo "    ERROR: cannot access subscription ${AZURE_SUBSCRIPTION_ID} (${AZURE_SUBSCRIPTION_NAME:-?})."
  echo "    Run 'az login' first and confirm your account has Owner or Contributor + UAA on the sub."
  exit 1
fi
SUB_NAME="$(az account show --subscription "$AZURE_SUBSCRIPTION_ID" --query name -o tsv)"
TENANT_ID="$(az account show --subscription "$AZURE_SUBSCRIPTION_ID" --query tenantId -o tsv)"
echo "    OK — '${SUB_NAME}' (${AZURE_SUBSCRIPTION_ID})"
echo "    Tenant: ${TENANT_ID}"
echo "    Region: ${LOCATION}"
if [[ -n "${AZURE_SUBSCRIPTION_NAME:-}" && "$SUB_NAME" != "$AZURE_SUBSCRIPTION_NAME" ]]; then
  echo "    WARNING: deploy-config.sh expects '${AZURE_SUBSCRIPTION_NAME}', Azure reports '${SUB_NAME}'."
fi
echo ""

# ── Step 2: register required providers ────────────────────────────────────
echo "==> Step 2/4: registering ${#PROVIDERS[@]} resource providers..."
for ns in "${PROVIDERS[@]}"; do
  CURRENT_STATE="$(az provider show --subscription "$AZURE_SUBSCRIPTION_ID" --namespace "$ns" --query registrationState -o tsv 2>/dev/null || echo "Unknown")"
  if [[ "$CURRENT_STATE" == "Registered" ]]; then
    echo "    [skip] $ns already Registered"
  else
    echo "    [register] $ns (was: $CURRENT_STATE)"
    az provider register --subscription "$AZURE_SUBSCRIPTION_ID" --namespace "$ns" -o none
  fi
done
echo ""

# ── Step 3: poll until all providers are Registered ────────────────────────
echo "==> Step 3/4: polling until all providers reach 'Registered'..."
MAX_WAIT_SECONDS=300
WAITED=0
while true; do
  ALL_READY=true
  for ns in "${PROVIDERS[@]}"; do
    STATE="$(az provider show --subscription "$AZURE_SUBSCRIPTION_ID" --namespace "$ns" --query registrationState -o tsv 2>/dev/null || echo "Unknown")"
    if [[ "$STATE" != "Registered" ]]; then
      ALL_READY=false
      break
    fi
  done
  if [[ "$ALL_READY" == true ]]; then
    echo "    All providers Registered."
    break
  fi
  if (( WAITED >= MAX_WAIT_SECONDS )); then
    echo "    ERROR: providers did not reach Registered within ${MAX_WAIT_SECONDS}s. Current states:"
    for ns in "${PROVIDERS[@]}"; do
      STATE="$(az provider show --subscription "$AZURE_SUBSCRIPTION_ID" --namespace "$ns" --query registrationState -o tsv 2>/dev/null || echo "Unknown")"
      echo "      $ns: $STATE"
    done
    exit 1
  fi
  echo "    waiting... (${WAITED}s)"
  sleep 10
  WAITED=$((WAITED + 10))
done
echo ""

# ── Step 4: sanity-check Cognitive Services SKU + Foundry model availability ──
echo "==> Step 4/4: checking Foundry model availability in ${LOCATION}..."

# Cognitive Services account creation requires Microsoft.CognitiveServices/accounts
# to be allowed in the target region. The list-skus command also surfaces region
# availability, so use it as a quick smoke test.
if ! az cognitiveservices account list-skus --subscription "$AZURE_SUBSCRIPTION_ID" --location "$LOCATION" --kind AIServices --query "[0].name" -o tsv >/dev/null 2>&1; then
  echo "    WARNING: cannot list AIServices SKUs in ${LOCATION}. The region may not support AIServices for this sub."
fi

ANY_MISSING=false
for spec in "${GA_MODELS[@]}"; do
  model="${spec%%:*}"
  version="${spec##*:}"
  hit="$(az cognitiveservices model list \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --location "$LOCATION" \
    --query "[?model.name=='${model}' && model.version=='${version}'] | [0].model.name" \
    -o tsv 2>/dev/null || true)"
  if [[ -z "$hit" ]]; then
    echo "    MISSING: ${model} v${version} not listed in ${LOCATION}"
    ANY_MISSING=true
  else
    echo "    OK     : ${model} v${version}"
  fi
done

# gpt-5.4 is opt-in (deployGpt54 param). Warn if it's not available; do not fail.
GPT54_HIT="$(az cognitiveservices model list \
  --subscription "$AZURE_SUBSCRIPTION_ID" \
  --location "$LOCATION" \
  --query "[?model.name=='gpt-5.4'] | [0].model.name" \
  -o tsv 2>/dev/null || true)"
if [[ -z "$GPT54_HIT" ]]; then
  echo "    INFO   : gpt-5.4 not available in ${LOCATION} (preview model). Leave deployGpt54=false in main.bicepparam."
else
  echo "    INFO   : gpt-5.4 IS available in ${LOCATION}. You may set deployGpt54=true if you want it."
fi

if [[ "$ANY_MISSING" == true ]]; then
  echo ""
  echo "==> One or more required models are not available in ${LOCATION}."
  echo "    Either pick a different region (set LOCATION in deploy-config.sh)"
  echo "    or request quota in this region via the Azure portal."
  exit 1
fi
echo ""

# ── Done ────────────────────────────────────────────────────────────────────
cat <<EOF
==> Subscription warmup complete.

Next steps:
  1. (One-time) Federate GitHub Actions to this subscription:
       ./scripts/setup-oidc.sh
  2. Trigger the GitHub Actions deploy workflow:
       gh workflow run deploy.yml -f target=all
     Or push a change to main and let path filters route the work.
  3. After Cloudflare DNS records are in place, bind the custom domain:
       gh workflow run deploy.yml -f target=infra -f with_domain=true
     (See TRANSITION.md for the DNS records and validation handshake.)
EOF
