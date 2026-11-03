#!/bin/bash
# Prompt for a secret without echoing it, store it in /opt/linksuite/.env, and restart the API.
#   ssh -t healthtutor-vps bash /opt/linksuite/deploy/set-secret.sh SMTP_PASSWORD
# Spaces are removed (Google shows app passwords as four groups of four letters).
set -e
key="$1"
[[ "$key" =~ ^[A-Z0-9_]+$ ]] || { echo "Usage: set-secret.sh NAME   (e.g. SMTP_PASSWORD)"; exit 2; }
env=/opt/linksuite/.env
read -rsp "Value for $key: " value; echo
# Some terminals wrap pasted text in bracketed-paste markers (ESC[200~ ... ESC[201~); drop them and any control characters.
value="${value//$'\e'[200~/}"
value="${value//$'\e'[201~/}"
value=$(printf '%s' "$value" | tr -d '[:cntrl:] ')
[ -n "$value" ] || { echo "Empty value, nothing changed."; exit 1; }
tmp=$(mktemp)
grep -v "^$key=" "$env" > "$tmp" || true
printf '%s=%s\n' "$key" "$value" >> "$tmp"
cat "$tmp" > "$env" && rm -f "$tmp"
chmod 600 "$env"
cd /opt/linksuite
docker compose -f docker-compose.vps.yml up -d api
echo "$key saved (${#value} characters) and API restarted."
