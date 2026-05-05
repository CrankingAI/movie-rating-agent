#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# run-local.sh — Start the Aspire AppHost for local development.
#
# What you get:
#   * Functions worker on http://localhost:7071
#   * Azurite (storage emulator) for queues/blobs
#   * OpenTelemetry Collector exporting to ./otel-export/*.jsonl
#   * Aspire dashboard at https://localhost:15888 with full distributed
#     traces (gen_ai.* spans included via Microsoft.Extensions.AI)
#
# Pre-flight (one-time):
#   ./scripts/set-local-creds.sh    # pulls Foundry endpoint+key from Azure
#   ./scripts/start-docker.sh       # ensures Docker is running for Azurite
#
# Toggle prompt/completion capture on gen_ai spans:
#   GENAI_CAPTURE_MESSAGE_CONTENT=true ./scripts/run-local.sh
# ---------------------------------------------------------------------------
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
  echo "Error: 'aspire' CLI not found. Install with: dotnet workload install aspire"
  exit 1
fi

# Make sure Docker Desktop is awake — Azurite runs as a container, and a
# Resource-Saver-asleep daemon causes the Functions worker to hang on the
# first blob/queue write. start-docker.sh is a no-op if Docker is already up.
"${REPO_ROOT}/scripts/start-docker.sh"

mkdir -p "${REPO_ROOT}/otel-export"

echo "==> Starting Aspire AppHost for local development..."
echo ""
echo "    Project       : ${APPHOST_PROJECT}"
echo "    Dashboard     : https://localhost:15888"
echo "    OTel exports  : ${REPO_ROOT}/otel-export/{traces,metrics,logs}.jsonl"
if [[ "${GENAI_CAPTURE_MESSAGE_CONTENT:-}" == "true" ]]; then
  echo "    GenAI capture : ON  (prompts + completions will appear on chat spans)"
else
  echo "    GenAI capture : off (set GENAI_CAPTURE_MESSAGE_CONTENT=true to enable)"
fi
echo ""
echo "    After Aspire boots, view spans in the dashboard or run:"
echo "      ./scripts/view-otel.sh --local --genai"
echo ""
echo "    Press Ctrl+C to stop."
echo ""

cd "$APPHOST_PROJECT"
aspire run
