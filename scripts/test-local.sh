#!/usr/bin/env bash
set -euo pipefail

MOVIE="${1:-The Godfather}"
PORT="${2:-}"

get_probe_status() {
  local port="$1"
  curl -s -o /dev/null -w '%{http_code}' "http://localhost:${port}/api/jobs/probe" 2>/dev/null || true
}

if [[ -z "$PORT" ]]; then
  CANDIDATES="$(lsof -iTCP -sTCP:LISTEN -P -n 2>/dev/null | awk '/^func / && $9 ~ /^\*:/ { sub(/^\*:/, "", $9); print $9 }' | sort -u)" || true
  if [[ -z "$CANDIDATES" ]]; then
    CANDIDATES="$(lsof -iTCP -sTCP:LISTEN -P -n 2>/dev/null | grep '^func ' | grep -oE ':\d+' | tr -d ':' | sort -u)" || true
  fi
  for P in $CANDIDATES; do
    if [[ "$(get_probe_status "$P")" == "404" ]]; then PORT="$P"; break; fi
  done
  if [[ -z "$PORT" ]]; then
    echo "Error: Could not detect Functions port."
    exit 1
  fi
fi

BASE="http://localhost:${PORT}/api/jobs"
echo "==> Submitting: \"${MOVIE}\" to ${BASE}"

SUBMIT_RESPONSE="$(curl -s -X POST "${BASE}" -H 'Content-Type: application/json' -d "{\"topic\": \"${MOVIE}\"}" 2>&1)" || true
JOB_ID="$(echo "$SUBMIT_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('jobId',''))" 2>/dev/null)" || true

if [[ -z "$JOB_ID" ]]; then
  echo "Error: Failed to submit job."
  exit 1
fi

echo "    Job ID: ${JOB_ID}"
while true; do
  RESPONSE="$(curl -s "${BASE}/${JOB_ID}")"
  STATUS="$(echo "$RESPONSE" | grep -o '"Status":[0-9]*' | cut -d: -f2)"
  case "$STATUS" in
    0) echo "    [$(date +%H:%M:%S)] Queued..." ;;
    1) echo "    [$(date +%H:%M:%S)] Running..." ;;
    2) echo "    [$(date +%H:%M:%S)] Completed!"; echo "$RESPONSE" | python3 -m json.tool 2>/dev/null || echo "$RESPONSE"; exit 0 ;;
    3) echo "    [$(date +%H:%M:%S)] Failed!"; echo "$RESPONSE" | python3 -m json.tool 2>/dev/null || echo "$RESPONSE"; exit 1 ;;
    *) echo "    [$(date +%H:%M:%S)] Unknown status: ${STATUS}"; exit 1 ;;
  esac
  sleep 2
done
