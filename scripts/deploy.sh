#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# deploy.sh — EMERGENCY / LOCAL DEPLOY ONLY
#
# The canonical deploy path is the GitHub Actions workflow at
# .github/workflows/deploy.yml. Push to main, or run:
#
#   gh workflow run deploy.yml -f target=all
#   gh workflow run deploy.yml -f target=infra
#   gh workflow run deploy.yml -f target=functions
#   gh workflow run deploy.yml -f target=swa
#   gh workflow run deploy.yml -f target=infra -f with_domain=true
#
# This script exists as a break-glass tool for the cases where CI is broken
# or you need to iterate without pushing. It performs the same steps the
# workflow does and pins --subscription on every az call so it is not
# sensitive to whatever sub happens to be "current" in the az context.
#
# Usage:
#   ./scripts/deploy.sh                  # deploy infra + functions + swa
#   ./scripts/deploy.sh --infra-only     # infra only
#   ./scripts/deploy.sh --code-only      # functions + swa only (skip Bicep)
#   ./scripts/deploy.sh --func-only      # functions only
#   ./scripts/deploy.sh --swa-only       # swa only
#   ./scripts/deploy.sh --with-domain    # use main.with-domain.bicepparam
#                                        # (only after Cloudflare DNS is set)
#
# Reads scripts/deploy-config.sh for subscription, region, and resource names.
# Honors AZURE_SUBSCRIPTION_ID / ENVIRONMENT / CUSTOM_DOMAIN env-var overrides.
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"

DEPLOY_INFRA=true
DEPLOY_FUNC=true
DEPLOY_SWA=true
WITH_DOMAIN=false

usage() {
  cat <<EOF
Usage: $0 [option]

Options:
  --infra-only   Deploy infrastructure only
  --code-only    Deploy Functions and SWA only
  --func-only    Deploy Functions only
  --swa-only     Deploy SWA only
  --with-domain  Use main.with-domain.bicepparam (binds custom domain to SWA)
  --help, -h     Show this help
EOF
}

for arg in "$@"; do
  case "$arg" in
    --infra-only)  DEPLOY_FUNC=false; DEPLOY_SWA=false ;;
    --code-only)   DEPLOY_INFRA=false ;;
    --func-only)   DEPLOY_INFRA=false; DEPLOY_SWA=false ;;
    --swa-only)    DEPLOY_INFRA=false; DEPLOY_FUNC=false ;;
    --with-domain) WITH_DOMAIN=true ;;
    --help|-h|-help) usage; exit 0 ;;
    *) echo "Unknown flag: $arg"; usage; exit 1 ;;
  esac
done

VERSION="$(cat "${REPO_ROOT}/VERSION" | tr -d '[:space:]')"
COMMIT="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo 'unknown')"

# ── Subscription guardrail ───────────────────────────────────────────────
# Never rely on which subscription happens to be "current" in the local az
# context — another shell or script can flip it at any time. Every az call
# below must pass --subscription explicitly. Here we just confirm that the
# caller has a valid token for the target sub.
if ! az account show --subscription "$AZURE_SUBSCRIPTION_ID" --query id -o tsv >/dev/null 2>&1; then
  echo "==> ERROR: cannot access subscription ${AZURE_SUBSCRIPTION_ID} (${AZURE_SUBSCRIPTION_NAME:-?})."
  echo "    Run 'az login' first and make sure your account has rights to this sub."
  exit 1
fi

poll_readyz() {
  local url="$1"
  local label="$2"
  local expected_version="$3"
  local deployed_version=""
  local deployed_commit=""

  for attempt in $(seq 1 12); do
    local readyz
    readyz="$(curl -sf "$url" 2>/dev/null || true)"
    deployed_version="$(echo "$readyz" | python3 -c "import sys,json; print(json.load(sys.stdin).get('version',''))" 2>/dev/null || true)"
    deployed_commit="$(echo "$readyz" | python3 -c "import sys,json; print(json.load(sys.stdin).get('commit','')[:7])" 2>/dev/null || true)"

    if [[ "$deployed_version" == "$expected_version" ]]; then
      echo "==> Verified ${label}: v${deployed_version} (${deployed_commit:-unknown})"
      return 0
    fi

    echo "==> Waiting for ${label} (${attempt}/12)..."
    sleep 10
  done

  echo "==> ERROR: ${label} did not report expected version."
  echo "    Expected: v${expected_version} (${COMMIT})"
  echo "    Deployed: v${deployed_version:-?} (${deployed_commit:-?})"
  return 1
}

echo "==> Deploying Movie Rating Agent v${VERSION} (${COMMIT})"
echo "    Subscription:   ${AZURE_SUBSCRIPTION_NAME:-?} (${AZURE_SUBSCRIPTION_ID})"
echo "    Resource group: ${RESOURCE_GROUP}"
echo "    Region:         ${LOCATION}"
echo ""

# ── Infrastructure ────────────────────────────────────────────────────────
if [[ "$DEPLOY_INFRA" == true ]]; then
  if [[ "$WITH_DOMAIN" == true ]]; then
    PARAMS_FILE="$BICEP_PARAMS_WITH_DOMAIN"
    echo "==> Deploying infrastructure WITH custom domain (${PARAMS_FILE})..."
  else
    PARAMS_FILE="$BICEP_PARAMS"
    echo "==> Deploying infrastructure (${PARAMS_FILE})..."
  fi

  az deployment sub create \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --location "$LOCATION" \
    --template-file "${REPO_ROOT}/${BICEP_FILE}" \
    --parameters "${REPO_ROOT}/${PARAMS_FILE}" \
    --name "movie-rating-agent-${ENVIRONMENT}-$(date +%Y%m%d%H%M%S)"
  echo "==> Infrastructure deployment complete."
  echo ""
fi

# ── Functions ─────────────────────────────────────────────────────────────
if [[ "$DEPLOY_FUNC" == true ]]; then
  echo "==> Building Functions (v${VERSION})..."
  PUBLISH_DIR="${REPO_ROOT}/.deploy/functions"
  rm -rf "$PUBLISH_DIR"
  dotnet publish "${REPO_ROOT}/${FUNCTIONS_PROJECT}" \
    --configuration Release \
    --output "$PUBLISH_DIR" \
    -v quiet

  echo "==> Deploying Functions to ${FUNCTION_APP}..."
  ZIP_PATH="/tmp/movie-rating-agent-functions-${VERSION}.zip"
  rm -f "$ZIP_PATH"
  (cd "$PUBLISH_DIR" && zip -r "$ZIP_PATH" . -q)
  az functionapp deployment source config-zip \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --name "$FUNCTION_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --src "$ZIP_PATH" \
    --timeout 120 \
    -o none
  rm -f "$ZIP_PATH"

  echo "==> Functions deployed. Restarting..."
  az functionapp restart --subscription "$AZURE_SUBSCRIPTION_ID" --name "$FUNCTION_APP" --resource-group "$RESOURCE_GROUP" -o none 2>/dev/null || true
  echo "==> Functions deployment complete."
  echo ""
fi

# ── SWA ───────────────────────────────────────────────────────────────────
if [[ "$DEPLOY_SWA" == true ]]; then
  echo "==> Deploying SWA..."
  DEPLOY_TOKEN="$(az staticwebapp secrets list \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --name "$SWA_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query "properties.apiKey" -o tsv)"
  npx -y @azure/static-web-apps-cli deploy "${REPO_ROOT}/swa" \
    --deployment-token "$DEPLOY_TOKEN" \
    --env "$SWA_DEPLOY_ENV" 2>&1 | tail -3
  echo "==> SWA deployment complete."
  echo ""
fi

# ── Verify ────────────────────────────────────────────────────────────────
if [[ "$DEPLOY_FUNC" == true || "$DEPLOY_SWA" == true ]]; then
  echo "==> Verifying SWA route and linked backend..."
  SWA_HOST="$(az staticwebapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$SWA_NAME" --resource-group "$RESOURCE_GROUP" --query defaultHostname -o tsv)"
  curl -sfI "https://${SWA_HOST}/" >/dev/null
  poll_readyz "https://${SWA_HOST}/api/readyz" "Static Web App backend" "$VERSION"
fi

echo ""
echo "==> Done."
