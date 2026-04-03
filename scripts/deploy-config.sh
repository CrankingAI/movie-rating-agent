#!/usr/bin/env bash

ENVIRONMENT="dev"
LOCATION="eastus2"

RESOURCE_GROUP="rg-movie-rating-agent-${ENVIRONMENT}"
FUNCTION_APP="func-movie-rating-agent-${ENVIRONMENT}"
FUNCTION_HOST="${FUNCTION_APP}.azurewebsites.net"
SWA_NAME="swa-movie-rating-agent-${ENVIRONMENT}"
AI_NAME="ai-movie-rating-agent-${ENVIRONMENT}"
APPI_NAME="appi-movie-rating-agent-${ENVIRONMENT}"
LOG_ANALYTICS_NAME="log-movie-rating-agent-${ENVIRONMENT}"

BICEP_FILE="infra/main.bicep"
FUNCTIONS_PROJECT="src/MovieRatingAgent.Functions/MovieRatingAgent.Functions.csproj"
SWA_DEPLOY_ENV="default"