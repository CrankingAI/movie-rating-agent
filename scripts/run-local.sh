#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APPHOST_PROJECT="${REPO_ROOT}/aspire/MovieRatingAgent.AppHost"
export PATH="$HOME/.dotnet/tools:$PATH"
if [[ -d "$HOME/.docker/bin" ]]; then
  export PATH="$HOME/.docker/bin:$PATH"
fi

if ! command -v dotnet &>/dev/null; then
  echo "Error: 'dotnet' CLI not found."
  exit 1
fi

if ! command -v aspire &>/dev/null; then
  echo "Error: 'aspire' CLI not found."
  exit 1
fi

mkdir -p "${REPO_ROOT}/otel-export"

echo "==> Starting Aspire AppHost for local development..."
echo "    Project : ${APPHOST_PROJECT}"
echo "    Dashboard: https://localhost:15888"
echo "    Press Ctrl+C to stop."
echo ""

cd "$APPHOST_PROJECT"
aspire run
