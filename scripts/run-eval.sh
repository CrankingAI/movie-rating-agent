#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"
PROJECT_PATH="${REPO_ROOT}/tests/MovieRatingAgent.Eval/MovieRatingAgent.Eval.csproj"

MODE="all"
FILTER=""
RUNS="${MOVIE_EVAL_RUNS:-7}"
VERBOSITY="normal"
LOGGER_VERBOSITY="${MOVIE_EVAL_LOGGER_VERBOSITY:-detailed}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    all|range|stability|quality) MODE="$1"; shift ;;
    --runs) RUNS="$2"; shift 2 ;;
    --verbosity) VERBOSITY="$2"; shift 2 ;;
    --logger-verbosity) LOGGER_VERBOSITY="$2"; shift 2 ;;
    --filter) FILTER="$2"; shift 2 ;;
    --help|-h) echo "Usage: $0 [mode] [options]"; exit 0 ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

if [[ -z "$FILTER" ]]; then
  case "$MODE" in
    range) FILTER="FullyQualifiedName~RangeCorrectnessEvalTests" ;;
    stability) FILTER="FullyQualifiedName~StabilityVarianceEvalTests" ;;
    quality) FILTER="FullyQualifiedName~QualityEvalTests" ;;
    all) FILTER="" ;;
  esac
fi

export MOVIE_EVAL_RUNS="$RUNS"

if [[ -z "${FOUNDRY_ENDPOINT:-}" || -z "${FOUNDRY_API_KEY:-}" ]]; then
  echo "==> Fetching Foundry credentials from ${AZURE_SUBSCRIPTION_NAME:-$AZURE_SUBSCRIPTION_ID}..."
  export FOUNDRY_ENDPOINT="$(az cognitiveservices account show \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --name "${AI_NAME}" --resource-group "${RESOURCE_GROUP}" \
    --query properties.endpoint -o tsv)"
  export FOUNDRY_API_KEY="$(az cognitiveservices account keys list \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --name "${AI_NAME}" --resource-group "${RESOURCE_GROUP}" \
    --query key1 -o tsv)"
  echo "    Endpoint: ${FOUNDRY_ENDPOINT}"
fi

echo "==> Running eval tests (Mode: ${MODE}, Runs: ${MOVIE_EVAL_RUNS})"
if [[ -n "$FILTER" ]]; then
  dotnet test "$PROJECT_PATH" --filter "$FILTER" --verbosity "$VERBOSITY" --logger "console;verbosity=${LOGGER_VERBOSITY}"
else
  dotnet test "$PROJECT_PATH" --verbosity "$VERBOSITY" --logger "console;verbosity=${LOGGER_VERBOSITY}"
fi
