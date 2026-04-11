#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  register-node.sh \
    --api https://api.example.com \
    --name VPS-01 \
    --ip 10.0.0.21 \
    --ssh-user fleetmgr \
    --os Ubuntu \
    --region eu-west \
    [--ssh-port 22] \
    [--control-port 9001] \
    [--appsettings /opt/fleetmanager-agent/appsettings.json]
EOF
}

API_URL=""
NAME=""
IP_ADDRESS=""
SSH_USER=""
OS_TYPE="Ubuntu"
REGION=""
SSH_PORT="22"
CONTROL_PORT="9001"
APPSETTINGS_PATH=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --api) API_URL="$2"; shift 2 ;;
    --name) NAME="$2"; shift 2 ;;
    --ip) IP_ADDRESS="$2"; shift 2 ;;
    --ssh-user) SSH_USER="$2"; shift 2 ;;
    --os) OS_TYPE="$2"; shift 2 ;;
    --region) REGION="$2"; shift 2 ;;
    --ssh-port) SSH_PORT="$2"; shift 2 ;;
    --control-port) CONTROL_PORT="$2"; shift 2 ;;
    --appsettings) APPSETTINGS_PATH="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

for value_name in API_URL NAME IP_ADDRESS SSH_USER; do
  if [[ -z "${!value_name}" ]]; then
    echo "Missing required argument: ${value_name}" >&2
    usage
    exit 1
  fi
done

payload="$(python3 - <<PY
import json
print(json.dumps({
  "name": ${NAME@Q},
  "ipAddress": ${IP_ADDRESS@Q},
  "sshPort": int(${SSH_PORT@Q}),
  "controlPort": int(${CONTROL_PORT@Q}),
  "sshUsername": ${SSH_USER@Q},
  "authType": "SshKey",
  "osType": ${OS_TYPE@Q},
  "region": ${REGION@Q} if ${REGION@Q} else None
}))
PY
)"

response_file="$(mktemp)"
http_code="$(curl -sS -o "$response_file" -w "%{http_code}" \
  -H 'Content-Type: application/json' \
  -X POST "${API_URL%/}/api/nodes" \
  -d "$payload")"

if [[ "$http_code" != "201" ]]; then
  echo "Node registration failed. HTTP $http_code" >&2
  cat "$response_file" >&2
  rm -f "$response_file"
  exit 1
fi

node_id="$(python3 - "$response_file" <<'PY'
import json, sys
with open(sys.argv[1], 'r', encoding='utf-8') as fh:
    data = json.load(fh)
print(data["id"])
PY
)"

echo "Registered node: $node_id"

if [[ -n "$APPSETTINGS_PATH" ]]; then
  python3 - "$APPSETTINGS_PATH" "$node_id" "$API_URL" <<'PY'
import json, sys
path, node_id, api_url = sys.argv[1:]
with open(path, 'r', encoding='utf-8') as fh:
    data = json.load(fh)
data.setdefault("Agent", {})
data["Agent"]["NodeId"] = node_id
data["Agent"]["BackendBaseUrl"] = api_url
with open(path, 'w', encoding='utf-8') as fh:
    json.dump(data, fh, indent=2)
    fh.write("\n")
PY
  echo "Updated agent appsettings: $APPSETTINGS_PATH"
fi

rm -f "$response_file"
