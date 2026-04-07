#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# setup-oidc.sh — Create (or update) the Entra app + federated credentials
# that GitHub Actions uses to authenticate to Azure via OIDC.
#
# Idempotent: safe to re-run. The app reg, SP, role assignment, and federated
# credential are created on first run and refreshed on subsequent runs.
#
# Usage:
#   ./scripts/setup-oidc.sh                    # uses sub from deploy-config.sh
#   AZURE_SUBSCRIPTION_ID=<id> ./scripts/setup-oidc.sh
#
# Every az call that has a subscription scope passes --subscription explicitly,
# so this script is not sensitive to whichever sub happens to be "current" in
# the az context. AAD / Entra operations (az ad ...) are tenant-scoped and
# therefore don't take a --subscription flag.
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"

APP_NAME="${APP_NAME:-sp-movie-rating-agent-github}"

GITHUB_REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
echo "==> GitHub repo: ${GITHUB_REPO}"

# Use the explicitly configured target subscription, not whatever az happens
# to have selected. This protects against accidentally federating to the
# wrong sub when several are signed in.
SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID}"
TENANT_ID="$(az account show --subscription "$SUBSCRIPTION_ID" --query tenantId -o tsv)"
SUB_NAME="$(az account show --subscription "$SUBSCRIPTION_ID" --query name -o tsv)"

# Sanity check the friendly name from deploy-config.sh against what Azure
# reports — protects against renames or copy/paste mistakes in the sub ID.
if [[ -n "${AZURE_SUBSCRIPTION_NAME:-}" && "$SUB_NAME" != "$AZURE_SUBSCRIPTION_NAME" ]]; then
  echo "==> WARNING: deploy-config.sh expects subscription '${AZURE_SUBSCRIPTION_NAME}'"
  echo "    but Azure reports '${SUB_NAME}' for ID ${SUBSCRIPTION_ID}."
fi

echo "==> Target subscription: ${SUB_NAME} (${SUBSCRIPTION_ID})"
echo "==> Tenant: ${TENANT_ID}"

# ── App registration ─────────────────────────────────────────────────────
EXISTING_CLIENT_ID="$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv 2>/dev/null || true)"
if [[ -n "$EXISTING_CLIENT_ID" ]]; then
  CLIENT_ID="$EXISTING_CLIENT_ID"
  echo "==> Reusing existing app registration: ${APP_NAME} (${CLIENT_ID})"
else
  echo "==> Creating Azure AD app registration: ${APP_NAME}..."
  CLIENT_ID="$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)"
  echo "    App (client) ID: ${CLIENT_ID}"
fi

# ── Service principal ────────────────────────────────────────────────────
az ad sp create --id "$CLIENT_ID" --output none 2>/dev/null || true
OBJECT_ID="$(az ad sp show --id "$CLIENT_ID" --query id -o tsv)"
echo "==> Service principal object ID: ${OBJECT_ID}"

# ── Role assignment (Contributor on the target subscription) ─────────────
EXISTING_ROLE="$(az role assignment list \
  --assignee "$CLIENT_ID" \
  --scope "/subscriptions/${SUBSCRIPTION_ID}" \
  --role Contributor \
  --query "[0].id" -o tsv 2>/dev/null || true)"
if [[ -z "$EXISTING_ROLE" ]]; then
  echo "==> Assigning Contributor role on subscription ${SUBSCRIPTION_ID}..."
  az role assignment create \
    --assignee "$CLIENT_ID" \
    --role Contributor \
    --scope "/subscriptions/${SUBSCRIPTION_ID}" \
    --output none
else
  echo "==> Contributor role already assigned."
fi

# ── Federated credential (branch:main) ───────────────────────────────────
FED_NAME="github-main-branch"
EXISTING_FED="$(az ad app federated-credential list \
  --id "$CLIENT_ID" \
  --query "[?name=='${FED_NAME}'].id" -o tsv 2>/dev/null || true)"
if [[ -z "$EXISTING_FED" ]]; then
  echo "==> Creating federated credential for branch:main..."
  az ad app federated-credential create \
    --id "$CLIENT_ID" \
    --parameters "{
      \"name\": \"${FED_NAME}\",
      \"issuer\": \"https://token.actions.githubusercontent.com\",
      \"subject\": \"repo:${GITHUB_REPO}:ref:refs/heads/main\",
      \"audiences\": [\"api://AzureADTokenExchange\"]
    }" \
    --output none
else
  echo "==> Federated credential ${FED_NAME} already exists."
fi

# ── Federated credential (pull_request — for PR validation jobs) ─────────
PR_FED_NAME="github-pull-request"
EXISTING_PR_FED="$(az ad app federated-credential list \
  --id "$CLIENT_ID" \
  --query "[?name=='${PR_FED_NAME}'].id" -o tsv 2>/dev/null || true)"
if [[ -z "$EXISTING_PR_FED" ]]; then
  echo "==> Creating federated credential for pull_request..."
  az ad app federated-credential create \
    --id "$CLIENT_ID" \
    --parameters "{
      \"name\": \"${PR_FED_NAME}\",
      \"issuer\": \"https://token.actions.githubusercontent.com\",
      \"subject\": \"repo:${GITHUB_REPO}:pull_request\",
      \"audiences\": [\"api://AzureADTokenExchange\"]
    }" \
    --output none
else
  echo "==> Federated credential ${PR_FED_NAME} already exists."
fi

# ── GitHub repository secrets ────────────────────────────────────────────
echo "==> Setting GitHub repository secrets..."
gh secret set AZURE_CLIENT_ID        --body "$CLIENT_ID"
gh secret set AZURE_TENANT_ID        --body "$TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID  --body "$SUBSCRIPTION_ID"

echo ""
echo "==> Done."
echo "    Client ID:        ${CLIENT_ID}"
echo "    Tenant ID:        ${TENANT_ID}"
echo "    Subscription ID:  ${SUBSCRIPTION_ID}"
