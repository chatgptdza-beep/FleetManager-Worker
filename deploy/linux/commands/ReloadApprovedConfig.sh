#!/usr/bin/env bash
set -euo pipefail

PAYLOAD_FILE="${1:-}"
RESTART_DELAY_SECONDS="3"

if [[ -n "$PAYLOAD_FILE" && -f "$PAYLOAD_FILE" ]]; then
  value="$(python3 - "$PAYLOAD_FILE" <<'PY'
import json, sys
with open(sys.argv[1], 'r', encoding='utf-8') as fh:
    data = json.load(fh)
print(data.get("restartDelaySeconds", ""))
PY
)"
  if [[ -n "$value" ]]; then
    RESTART_DELAY_SECONDS="$value"
  fi
fi

AGENT_PARENT_PID="$PPID"
nohup bash -lc "sleep $RESTART_DELAY_SECONDS; kill -TERM $AGENT_PARENT_PID >/dev/null 2>&1 || true" >/dev/null 2>&1 &

echo "Config reload scheduled via agent restart in $RESTART_DELAY_SECONDS seconds."
