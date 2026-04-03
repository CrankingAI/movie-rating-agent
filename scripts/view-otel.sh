#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# view-otel.sh — View OpenTelemetry data from Aspire (local) or Azure Monitor
#
# Usage:
#   ./scripts/view-otel.sh                  # Azure Monitor, last 1 hour
#   ./scripts/view-otel.sh --local          # Aspire local file export
#   ./scripts/view-otel.sh --timespan PT4H  # Azure Monitor, last 4 hours
#   ./scripts/view-otel.sh --genai          # Azure Monitor, gen_ai focus
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${REPO_ROOT}/scripts/deploy-config.sh"
TIMESPAN="PT1H"
MODE="monitor"

usage() {
  cat <<EOF
Usage: $0 [options]

Modes:
  (default)      Query Azure Monitor / Application Insights
  --local        Read Aspire local file export (./otel-export/)

Options:
  --timespan <d> ISO 8601 duration for Azure Monitor queries (default: PT1H)
  --genai        Focus on gen_ai / LLM spans
  --help         Show this help
EOF
}

GENAI_FOCUS=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --local)     MODE="local"; shift ;;
    --timespan)  TIMESPAN="$2"; shift 2 ;;
    --genai)     GENAI_FOCUS=true; shift ;;
    --help|-h)   usage; exit 0 ;;
    *)           echo "Unknown flag: $1"; usage; exit 1 ;;
  esac
done

# ═══════════════════════════════════════════════════════════════════════════════
# LOCAL MODE — read Aspire file export
# ═══════════════════════════════════════════════════════════════════════════════
if [[ "$MODE" == "local" ]]; then
  OTEL_DIR="${REPO_ROOT}/otel-export"

  if [[ ! -d "$OTEL_DIR" ]]; then
    echo "Error: ${OTEL_DIR} not found. Start Aspire first: ./scripts/run-local.sh"
    exit 1
  fi

  echo "==> Aspire local OTel export (${OTEL_DIR})"
  echo ""

  for FILE in traces.jsonl metrics.jsonl logs.jsonl; do
    FPATH="${OTEL_DIR}/${FILE}"
    if [[ -f "$FPATH" ]]; then
      LINES=$(wc -l < "$FPATH" | tr -d ' ')
      SIZE=$(du -h "$FPATH" | cut -f1)
      echo "── ${FILE} (${LINES} lines, ${SIZE}) ──"

      if [[ "$GENAI_FOCUS" == true && "$FILE" == "traces.jsonl" ]]; then
        # Filter for gen_ai spans
        grep -i "gen_ai\|MovieRatingAgent\|chat\|Scorer\|Rollup" "$FPATH" 2>/dev/null \
          | tail -20 \
          | python3 -c "
import sys, json
for line in sys.stdin:
    try:
        obj = json.loads(line.strip())
        for rs in obj.get('resourceSpans', []):
            for ss in rs.get('scopeSpans', []):
                for span in ss.get('spans', []):
                    name = span.get('name', '?')
                    dur_ns = int(span.get('endTimeUnixNano', 0)) - int(span.get('startTimeUnixNano', 0))
                    dur_ms = dur_ns / 1_000_000
                    attrs = {a['key']: a.get('value', {}).get('stringValue', a.get('value', {}).get('intValue', '?')) for a in span.get('attributes', [])}
                    model = attrs.get('gen_ai.request.model', '')
                    agent = attrs.get('gen_ai.agent.name', '')
                    print(f'  {name:50s} {dur_ms:8.0f}ms  model={model}  agent={agent}')
    except:
        pass
" 2>/dev/null || echo "  (no gen_ai spans found)"
      else
        # Show last 10 entries
        tail -10 "$FPATH" | python3 -c "
import sys, json
for line in sys.stdin:
    try:
        obj = json.loads(line.strip())
        # Traces
        for rs in obj.get('resourceSpans', []):
            for ss in rs.get('scopeSpans', []):
                scope = ss.get('scope', {}).get('name', '?')
                for span in ss.get('spans', []):
                    name = span.get('name', '?')
                    kind = span.get('kind', '?')
                    dur_ns = int(span.get('endTimeUnixNano', 0)) - int(span.get('startTimeUnixNano', 0))
                    dur_ms = dur_ns / 1_000_000
                    print(f'  [{scope}] {name:50s} {dur_ms:8.0f}ms')
        # Logs
        for rl in obj.get('resourceLogs', []):
            for sl in rl.get('scopeLogs', []):
                for lr in sl.get('logRecords', []):
                    sev = lr.get('severityText', '?')
                    body = lr.get('body', {}).get('stringValue', '?')[:100]
                    print(f'  [{sev}] {body}')
        # Metrics
        for rm in obj.get('resourceMetrics', []):
            for sm in rm.get('scopeMetrics', []):
                for m in sm.get('metrics', []):
                    name = m.get('name', '?')
                    print(f'  {name}')
    except:
        pass
" 2>/dev/null || tail -3 "$FPATH"
      fi
      echo ""
    else
      echo "── ${FILE}: not found ──"
      echo ""
    fi
  done

  echo "==> Done."
  exit 0
fi

# ═══════════════════════════════════════════════════════════════════════════════
# AZURE MONITOR MODE — query Application Insights via KQL
# ═══════════════════════════════════════════════════════════════════════════════
echo "==> Querying App Insights (${APPI_NAME}) — timespan: ${TIMESPAN}"
echo ""

run_query() {
  local title="$1"
  local query="$2"

  echo "── ${title} ──────────────────────────────────────────────"
  az monitor app-insights query \
    --app "$APPI_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --analytics-query "$query" \
    --offset "$TIMESPAN" \
    --output json 2>/dev/null \
  | jq -r '
      .tables[0] as $t |
      if ($t.rows | length) == 0 then
        "  (no results)"
      else
        ([$t.columns[].name] | join(" | ")),
        ([$t.columns[] | "---"] | join(" | ")),
        ($t.rows[] | [.[] | tostring] | join(" | "))
      end
    ' 2>/dev/null || echo "  (query returned no data or App Insights is unavailable)"
  echo ""
}

if [[ "$GENAI_FOCUS" == true ]]; then
  # Gen AI focused queries
  run_query "Gen AI Dependency Spans (last ${TIMESPAN})" \
    "dependencies | where customDimensions has 'gen_ai' or type has 'gen_ai' or name has 'chat' or name has 'Scorer' or name has 'invoke_agent' | order by timestamp desc | take 20 | project timestamp, name, duration, customDimensions"

  run_query "Agent Job Traces (last ${TIMESPAN})" \
    "traces | where message has 'Job' or message has 'movie' or message has 'Score' | order by timestamp desc | take 20 | project timestamp, message, severityLevel"

  run_query "LLM Latency Summary (last ${TIMESPAN})" \
    "dependencies | where name has 'chat' | summarize avg(duration), percentile(duration, 50), percentile(duration, 95), count() by name | order by avg_duration desc"

  run_query "Agent Errors (last ${TIMESPAN})" \
    "traces | where severityLevel >= 3 and (message has 'fail' or message has 'error' or message has 'exception') | order by timestamp desc | take 10 | project timestamp, message, severityLevel"
else
  # General overview queries
  run_query "Recent Requests (last ${TIMESPAN})" \
    "requests | order by timestamp desc | take 20 | project timestamp, name, resultCode, duration, success"

  run_query "Recent Job Traces (last ${TIMESPAN})" \
    "traces | where message has 'Job' or message has 'movie' or message has 'Score' | order by timestamp desc | take 20 | project timestamp, message, severityLevel"

  run_query "Dependencies (last ${TIMESPAN})" \
    "dependencies | order by timestamp desc | take 20 | project timestamp, type, target, name, duration, success"

  run_query "Gen AI Spans (last ${TIMESPAN})" \
    "dependencies | where customDimensions has 'gen_ai' or name has 'chat' or name has 'Scorer' or name has 'invoke_agent' | order by timestamp desc | take 20 | project timestamp, name, duration, customDimensions"

  run_query "Request Summary (last ${TIMESPAN})" \
    "requests | summarize count(), avg(duration), percentile(duration, 95) by name, resultCode | order by count_ desc"

  run_query "Errors (last ${TIMESPAN})" \
    "traces | where severityLevel >= 3 | order by timestamp desc | take 10 | project timestamp, message, severityLevel"
fi

echo "==> Done."
