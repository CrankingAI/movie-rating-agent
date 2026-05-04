#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# deploy-config.sh — single source of truth for deployment-time names.
#
# Sourced by every script in scripts/ and by the GitHub Actions deploy
# workflow. Override individual values via environment variables before
# sourcing this file (useful for ad-hoc deploys to a different env).
# ---------------------------------------------------------------------------

# Logical environment name — drives all resource names below.
ENVIRONMENT="${ENVIRONMENT:-dev}"

# Azure region. eastus2 has all required Foundry models for this project.
LOCATION="${LOCATION:-eastus2}"

# Target Azure subscription. Defaults to BillDev (the standalone
# community-example deployment). Override to point at a different sub.
#   Subscription name: BillDev
#   Subscription id  : 379168a0-b9fc-4fa0-a3cd-ce32ab20ee70
#   Tenant id        : 5c369887-a4a0-4a67-a8d6-a78e017216fc
AZURE_SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID:-379168a0-b9fc-4fa0-a3cd-ce32ab20ee70}"
AZURE_TENANT_ID="${AZURE_TENANT_ID:-5c369887-a4a0-4a67-a8d6-a78e017216fc}"
AZURE_SUBSCRIPTION_NAME="BillDev"

# Short deterministic suffix for globally-unique resource names. The same
# value is computed inside infra/main.bicep so re-deploys are idempotent and
# two different subs can land on different names without coordination:
#   Bicep:  take(replace(subscription().subscriptionId, '-', ''), 6)
#   Bash:   echo "$AZURE_SUBSCRIPTION_ID" | tr -d - | cut -c1-6
RESOURCE_TOKEN="$(echo "$AZURE_SUBSCRIPTION_ID" | tr -d '-' | cut -c1-6)"

# Resource names — derived from $ENVIRONMENT (+ $RESOURCE_TOKEN where global
# uniqueness is required). Keep in sync with infra/*.bicep.
RESOURCE_GROUP="rg-movie-rating-agent-${ENVIRONMENT}"
FUNCTION_APP="func-movie-rating-agent-${ENVIRONMENT}-${RESOURCE_TOKEN}"
FUNCTION_HOST="${FUNCTION_APP}.azurewebsites.net"
SWA_NAME="swa-movie-rating-agent-${ENVIRONMENT}"
AI_NAME="ai-movie-rating-agent-${ENVIRONMENT}-${RESOURCE_TOKEN}"
APPI_NAME="appi-movie-rating-agent-${ENVIRONMENT}"
LOG_ANALYTICS_NAME="log-movie-rating-agent-${ENVIRONMENT}"
STORAGE_ACCOUNT="stmra${ENVIRONMENT}${RESOURCE_TOKEN}"

# Bicep entry points.
BICEP_FILE="infra/main.bicep"
BICEP_PARAMS="infra/main.bicepparam"
BICEP_PARAMS_WITH_DOMAIN="infra/main.with-domain.bicepparam"

# Code paths.
FUNCTIONS_PROJECT="src/MovieRatingAgent.Functions/MovieRatingAgent.Functions.csproj"
SWA_DEPLOY_ENV="default"

# Custom domain. Empty during initial bring-up; set to 'movieratingagent.com'
# only after Cloudflare DNS records are in place (see TRANSITION.md).
CUSTOM_DOMAIN="${CUSTOM_DOMAIN:-}"
