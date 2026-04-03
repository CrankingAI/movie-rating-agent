#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# deploy.sh — Deploy infrastructure (Bicep), Functions code, and SWA to dev
#
# Usage:
#   ./scripts/deploy.sh              # deploy infra + functions + swa
#   ./scripts/deploy.sh --infra-only # deploy infra only
#   ./scripts/deploy.sh --code-only  # deploy functions + swa only
#   ./scripts/deploy.sh --func-only  # deploy functions only
#   ./scripts/deploy.sh --swa-only   # deploy swa only
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"

DEPLOY_INFRA=true
DEPLOY_FUNC=true
DEPLOY_SWA=true

usage() {
  cat <<EOF
Usage: $0 [option]

Options:
  --infra-only  Deploy infrastructure only
  --code-only   Deploy Functions and SWA only
  --func-only   Deploy Functions only
  --swa-only    Deploy SWA only
  --help, -h    Show this help
EOF
}

for arg in "$@"; do
  case "$arg" in
    --infra-only) DEPLOY_FUNC=false; DEPLOY_SWA=false ;;
    --code-only)  DEPLOY_INFRA=false ;;
    --func-only)  DEPLOY_INFRA=false; DEPLOY_SWA=false ;;
    --swa-only)   DEPLOY_INFRA=false; DEPLOY_FUNC=false ;;
    --help|-h|-help) usage; exit 0 ;;
    *) echo "Unknown flag: $arg"; exit 1 ;;
  esac
done
VERSION="$(cat "${REPO_ROOT}/VERSION" | tr -d '[:space:]')"
COMMIT="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo 'unknown')"

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
echo ""

# ── Infrastructure ────────────────────────────────────────────────────────
if [[ "$DEPLOY_INFRA" == true ]]; then
  echo "==> Deploying infrastructure via Bicep..."
  az deployment sub create \
    --location "$LOCATION" \
    --template-file "${REPO_ROOT}/${BICEP_FILE}" \
    --parameters environmentName="$ENVIRONMENT" location="$LOCATION" \
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
    --name "$FUNCTION_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --src "$ZIP_PATH" \
    --timeout 120 \
    -o none
  rm -f "$ZIP_PATH"

  echo "==> Functions deployed. Restarting..."
  az functionapp restart --name "$FUNCTION_APP" --resource-group "$RESOURCE_GROUP" -o none 2>/dev/null || true
  echo "==> Functions deployment complete."
  echo ""
fi

# ── SWA ───────────────────────────────────────────────────────────────────
if [[ "$DEPLOY_SWA" == true ]]; then
  echo "==> Deploying SWA..."
  DEPLOY_TOKEN="$(az staticwebapp secrets list \
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
  SWA_HOST="$(az staticwebapp show --name "$SWA_NAME" --resource-group "$RESOURCE_GROUP" --query defaultHostname -o tsv)"
  curl -sfI "https://${SWA_HOST}/" >/dev/null
  poll_readyz "https://${SWA_HOST}/api/readyz" "Static Web App backend" "$VERSION"
fi

echo ""
echo "==> Done."
