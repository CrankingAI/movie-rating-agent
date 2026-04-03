#!/usr/bin/env bash
set -euo pipefail

APP_NAME="sp-movie-rating-agent-github"

GITHUB_REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
echo "GitHub repo: ${GITHUB_REPO}"

SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
TENANT_ID="$(az account show --query tenantId -o tsv)"

echo "==> Creating Azure AD app registration: ${APP_NAME}..."
CLIENT_ID="$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)"
echo "    App (client) ID: ${CLIENT_ID}"

echo "==> Creating service principal..."
az ad sp create --id "$CLIENT_ID" --output none 2>/dev/null || true
OBJECT_ID="$(az ad sp show --id "$CLIENT_ID" --query id -o tsv)"
echo "    Service principal object ID: ${OBJECT_ID}"

echo "==> Assigning Contributor role on subscription ${SUBSCRIPTION_ID}..."
az role assignment create \
  --assignee "$CLIENT_ID" \
  --role Contributor \
  --scope "/subscriptions/${SUBSCRIPTION_ID}" \
  --output none

echo "==> Creating federated credential for branch:main..."
az ad app federated-credential create \
  --id "$CLIENT_ID" \
  --parameters "{
    \"name\": \"github-main-branch\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"repo:${GITHUB_REPO}:ref:refs/heads/main\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }" \
  --output none

echo "==> Setting GitHub repository secrets..."
gh secret set AZURE_CLIENT_ID        --body "$CLIENT_ID"
gh secret set AZURE_TENANT_ID        --body "$TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID  --body "$SUBSCRIPTION_ID"

echo "==> Done."
