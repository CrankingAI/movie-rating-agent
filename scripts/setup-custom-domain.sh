#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# setup-custom-domain.sh — Walk a user through binding movieratingagent.com
# (apex + www) to the Static Web App that lives in BillDevPlayground.
#
# This script implements the four-step custom-domain dance described in
# TRANSITION.md:
#
#   1. Read the SWA default hostname.
#   2. Print the Cloudflare DNS records the user must create.
#   3. Pause for confirmation that DNS is in place.
#   4. Run the apex/www validation handshake (creating the SWA customDomains
#      via az, which fetches the TXT token from Azure when needed).
#
# Usage:
#   ./scripts/setup-custom-domain.sh                       # full flow
#   ./scripts/setup-custom-domain.sh --print-records-only  # just emit DNS rows
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"

usage() {
  sed -n '2,20p' "$0"
  exit 0
}

DOMAIN="${CUSTOM_DOMAIN:-movieratingagent.com}"
WWW_DOMAIN="www.${DOMAIN}"
PRINT_ONLY=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --print-records-only)
      PRINT_ONLY=true
      shift
      ;;
    --domain)
      shift
      if [[ $# -eq 0 ]]; then
        echo "Error: --domain requires a value."
        usage
      fi
      DOMAIN="$1"
      WWW_DOMAIN="www.${DOMAIN}"
      shift
      ;;
    --help|-h|help)
      usage
      ;;
    *)
      echo "Error: unknown option: $1"
      usage
      ;;
  esac
done

# ── Subscription guardrail ───────────────────────────────────────────────
# All az calls below pass --subscription explicitly so this script is not
# sensitive to whatever sub happens to be "current" in the az context.
if ! az account show --subscription "$AZURE_SUBSCRIPTION_ID" --query id -o tsv >/dev/null 2>&1; then
  echo "==> ERROR: cannot access subscription ${AZURE_SUBSCRIPTION_ID} (${AZURE_SUBSCRIPTION_NAME:-?})."
  echo "    Run 'az login' first."
  exit 1
fi

# ── Step 1: read SWA default hostname ────────────────────────────────────
echo "==> Reading SWA default hostname..."
SWA_HOST="$(az staticwebapp show \
  --subscription "$AZURE_SUBSCRIPTION_ID" \
  --name "$SWA_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query defaultHostname -o tsv)"
echo "    Default hostname: ${SWA_HOST}"

# ── Step 2: print the Cloudflare DNS records ─────────────────────────────
cat <<EOF

================================================================================
 Cloudflare DNS records to create for ${DOMAIN}
================================================================================

  1. Apex (CNAME flattening — Cloudflare supports this for the zone root):
       Type:    CNAME
       Name:    @                       (the apex)
       Target:  ${SWA_HOST}
       Proxy:   DNS only (gray cloud)   ← required for SWA validation

  2. www subdomain:
       Type:    CNAME
       Name:    www
       Target:  ${SWA_HOST}
       Proxy:   DNS only (gray cloud)

After Azure issues the apex validation TXT token (next step), you will also
need to add:

  3. Apex TXT validation:
       Type:    TXT
       Name:    @
       Value:   <token printed below>

================================================================================
EOF

if [[ "$PRINT_ONLY" == true ]]; then
  exit 0
fi

read -r -p "Have you created the apex + www CNAME records in Cloudflare? [y/N] " confirm
case "$confirm" in
  [Yy]) ;;
  *)
    echo "==> Aborted. Re-run after the records are in place."
    exit 1
    ;;
esac

# ── Step 3: kick off apex (dns-txt-token) and read the validation token ──
echo "==> Creating apex custom domain (${DOMAIN}) on SWA..."
az staticwebapp hostname set \
  --subscription "$AZURE_SUBSCRIPTION_ID" \
  --name "$SWA_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --hostname "$DOMAIN" \
  --validation-method "dns-txt-token" \
  --no-wait \
  -o none || true

# Poll for the validation token (it appears once the resource enters Validating).
echo "==> Waiting for Azure to issue the apex validation token..."
TOKEN=""
for i in $(seq 1 12); do
  TOKEN="$(az staticwebapp hostname show \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --name "$SWA_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --hostname "$DOMAIN" \
    --query validationToken -o tsv 2>/dev/null || true)"
  if [[ -n "$TOKEN" && "$TOKEN" != "null" ]]; then
    break
  fi
  sleep 5
done

if [[ -z "$TOKEN" || "$TOKEN" == "null" ]]; then
  echo "==> ERROR: validation token never arrived. Inspect with:"
  echo "    az staticwebapp hostname show --subscription $AZURE_SUBSCRIPTION_ID --name $SWA_NAME --resource-group $RESOURCE_GROUP --hostname $DOMAIN"
  exit 1
fi

cat <<EOF

================================================================================
 Apex validation TXT record
================================================================================

  Type:    TXT
  Name:    @                  (apex of ${DOMAIN})
  Value:   ${TOKEN}

Add this record in Cloudflare, then press Enter to continue.

================================================================================
EOF

read -r -p "Press Enter when the TXT record is in place..."

echo "==> Waiting for Azure to validate the apex domain (this can take 5-10 minutes)..."
for i in $(seq 1 60); do
  STATUS="$(az staticwebapp hostname show \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --name "$SWA_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --hostname "$DOMAIN" \
    --query status -o tsv 2>/dev/null || true)"
  if [[ "$STATUS" == "Ready" ]]; then
    echo "==> Apex domain ready: ${DOMAIN}"
    break
  fi
  echo "    [$i/60] status=${STATUS}"
  sleep 10
done

# ── Step 4: bind www (cname-delegation) ──────────────────────────────────
echo "==> Creating www subdomain (${WWW_DOMAIN}) on SWA..."
az staticwebapp hostname set \
  --subscription "$AZURE_SUBSCRIPTION_ID" \
  --name "$SWA_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --hostname "$WWW_DOMAIN" \
  --validation-method "cname-delegation" \
  -o none

echo ""
echo "==> Done. Verify with:"
echo "    curl -sI https://${DOMAIN}/"
echo "    curl -sI https://${WWW_DOMAIN}/"
echo ""
echo "==> When verified, switch infra deploys to use ${BICEP_PARAMS_WITH_DOMAIN}"
echo "    so subsequent Bicep runs keep the custom domain bound declaratively."
