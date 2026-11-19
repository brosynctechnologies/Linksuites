#!/usr/bin/env bash
# OrderLink custom domains on the host (Wave 3): certificates and nginx sites for the stores' own domains.
#
# The API decides which domains are served (DNS verified, plan includes custom domains, business active); this script makes nginx
# match that list. Each run, for every listed domain: get a Let's Encrypt certificate if there is none (certbot, webroot), write
# /etc/nginx/sites-available/linksuite-custom/<domain>.conf, check it with `nginx -t`, and report the result back to the API.
# Sites for domains no longer listed (removed, changed, plan lapsed, business suspended) are deleted. nginx is reloaded only after
# `nginx -t` passes; a new site that fails the test is removed again and reported as failed, so the running config never breaks.
# If the API can't be reached or answers anything unexpected, nothing is changed.
#
# Idempotent; run from root's crontab every 5 minutes (one-time setup in docs/deploy-vps-handoff.md):
#   */5 * * * * /opt/linksuite/deploy/custom-domains.sh >> /var/log/linksuite-custom-domains.log 2>&1
#
# Settings (environment, else read from /opt/linksuite/.env):
#   CUSTOM_DOMAINS_HOST_SECRET  the API's CustomDomains:HostSecret (required)
#   CUSTOM_DOMAINS_EMAIL        Let's Encrypt account / expiry email (required)
#   CUSTOM_DOMAINS_API          default http://127.0.0.1:5080 (the API container, not through nginx)
#   CUSTOM_DOMAINS_UPSTREAM     default 127.0.0.1:3700 (the OrderLink web container)
set -uo pipefail
umask 022

ENV_FILE="${CUSTOM_DOMAINS_ENV_FILE:-/opt/linksuite/.env}"
CONF_DIR="${CUSTOM_DOMAINS_CONF_DIR:-/etc/nginx/sites-available/linksuite-custom}"
WEBROOT="${CUSTOM_DOMAINS_WEBROOT:-/var/www/certbot}"
LE_LIVE="${CUSTOM_DOMAINS_LE_LIVE:-/etc/letsencrypt/live}"
LOCK_FILE="${CUSTOM_DOMAINS_LOCK:-/run/linksuite-custom-domains.lock}"

log() { echo "$(date -u +%FT%TZ) $*"; }
die() { log "ERROR: $*"; exit 1; }

env_value() {
  [ -r "$ENV_FILE" ] || return 0
  grep -E "^$1=" "$ENV_FILE" | tail -n 1 | cut -d= -f2- | sed -e 's/^"\(.*\)"$/\1/' -e "s/^'\(.*\)'$/\1/"
}

SECRET="${CUSTOM_DOMAINS_HOST_SECRET:-$(env_value CUSTOM_DOMAINS_HOST_SECRET)}"
EMAIL="${CUSTOM_DOMAINS_EMAIL:-$(env_value CUSTOM_DOMAINS_EMAIL)}"
API="${CUSTOM_DOMAINS_API:-http://127.0.0.1:5080}"
UPSTREAM="${CUSTOM_DOMAINS_UPSTREAM:-127.0.0.1:3700}"

# One run at a time (a slow certbot must not overlap the next cron run).
exec 9>"$LOCK_FILE" || die "cannot open lock $LOCK_FILE"
flock -n 9 || { log "another run is still going; skipped"; exit 0; }

[ -n "$SECRET" ] || die "CUSTOM_DOMAINS_HOST_SECRET is not set"
[ -n "$EMAIL" ] || die "CUSTOM_DOMAINS_EMAIL is not set"
command -v certbot >/dev/null || die "certbot is not installed"
command -v nginx >/dev/null || die "nginx is not installed"
mkdir -p "$CONF_DIR" "$WEBROOT"

# Never build on a config that is already broken: someone else's change must be fixed first.
nginx -t >/dev/null 2>&1 || die "nginx -t fails before any change; nothing done"

# Only bare lower-case host names are ever used in paths and commands.
valid_domain() { [[ "$1" =~ ^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$ ]] && [ "${#1}" -le 253 ]; }

json_escape() { printf '%s' "$1" | tr -d '\000-\037' | cut -c1-300 | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }

report() { # domain ok(true|false) [error]
  local body
  if [ "$2" = true ]; then body="{\"domain\":\"$1\",\"ok\":true}"
  else body="{\"domain\":\"$1\",\"ok\":false,\"error\":\"$(json_escape "${3:-}")\"}"; fi
  curl -fsS --max-time 20 -o /dev/null -X POST -H "X-Host-Secret: $SECRET" -H "Content-Type: application/json" \
    --data "$body" "$API/api/internal/custom-domains/report" || log "could not report $1 ($2) to the API; will repeat next run"
}

site_conf() { # domain
  local d="$1" ssl_opts=""
  [ -f /etc/letsencrypt/options-ssl-nginx.conf ] && ssl_opts+="    include /etc/letsencrypt/options-ssl-nginx.conf;"$'\n'
  [ -f /etc/letsencrypt/ssl-dhparams.pem ] && ssl_opts+="    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;"$'\n'
  cat <<EOF
# Managed by /opt/linksuite/deploy/custom-domains.sh: an OrderLink store's own domain. Rewritten or deleted by the script; do not edit.
server {
    listen 80;
    listen [::]:80;
    server_name $d;
    location /.well-known/acme-challenge/ {
        root $WEBROOT;
    }
    location / {
        return 301 https://\$host\$request_uri;
    }
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    server_name $d;
    ssl_certificate $LE_LIVE/$d/fullchain.pem;
    ssl_certificate_key $LE_LIVE/$d/privkey.pem;
${ssl_opts}    client_max_body_size 50m;
    add_header X-Content-Type-Options nosniff always;
    add_header Referrer-Policy strict-origin-when-cross-origin always;

    location / {
        proxy_pass http://$UPSTREAM;
        include /etc/nginx/proxy_params;
    }
}
EOF
}

# The list: "<domain> <Verified|Live>" lines, closed by "# end" so a cut-off answer is never mistaken for a short list.
LIST="$(curl -fsS --max-time 20 -H "X-Host-Secret: $SECRET" "$API/api/internal/custom-domains?format=text")" \
  || die "the API did not answer; nothing changed"
[ "$(printf '%s\n' "$LIST" | tail -n 1)" = "# end" ] || die "incomplete answer from the API; nothing changed"

declare -A WANTED=()
RELOAD=0
REPORT_OK=()
TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT

while read -r domain status _; do
  [ -z "${domain:-}" ] && continue
  [[ "$domain" == \#* ]] && continue
  if ! valid_domain "$domain"; then log "skipped a malformed name from the API"; continue; fi
  WANTED["$domain"]=1
  conf="$CONF_DIR/$domain.conf"

  if [ ! -s "$LE_LIVE/$domain/fullchain.pem" ]; then
    log "$domain: requesting a certificate"
    if ! out="$(certbot certonly --webroot -w "$WEBROOT" -d "$domain" --non-interactive --agree-tos -m "$EMAIL" \
        --deploy-hook "systemctl reload nginx" 2>&1)"; then
      log "$domain: certbot failed"
      printf '%s\n' "$out" | tail -n 5
      # A site without its certificate would fail nginx -t for every site, so it goes.
      if [ -e "$conf" ]; then rm -f "$conf"; RELOAD=1; fi
      unset 'WANTED[$domain]'
      report "$domain" false "certbot: $(printf '%s' "$out" | grep -iE 'detail|error|problem' | tail -n 2 | tr '\n' ' ')"
      continue
    fi
  fi

  site_conf "$domain" > "$TMP"
  if [ -f "$conf" ] && cmp -s "$TMP" "$conf"; then
    [ "$status" = "Verified" ] && REPORT_OK+=("$domain") # in place already; the earlier report may have been lost
    continue
  fi

  backup=""
  if [ -f "$conf" ]; then backup="$(mktemp)"; cp -p "$conf" "$backup"; fi
  cp "$TMP" "$conf"
  if nginx -t >/dev/null 2>&1; then
    log "$domain: site written"
    RELOAD=1
    REPORT_OK+=("$domain")
    [ -n "$backup" ] && rm -f "$backup"
  else
    err="$(nginx -t 2>&1 | grep -E 'emerg|error' | head -n 2 | tr '\n' ' ')"
    if [ -n "$backup" ]; then mv "$backup" "$conf"; else rm -f "$conf"; fi
    log "$domain: nginx -t failed with the new site; it was taken out again: $err"
    report "$domain" false "nginx: $err"
  fi
done <<< "$LIST"

# Sites for domains the API no longer lists.
shopt -s nullglob
for conf in "$CONF_DIR"/*.conf; do
  domain="$(basename "$conf" .conf)"
  if [ -z "${WANTED[$domain]:-}" ]; then
    rm -f "$conf"
    log "$domain: site removed"
    RELOAD=1
  fi
done

if [ "$RELOAD" = 1 ]; then
  if nginx -t >/dev/null 2>&1; then
    systemctl reload nginx || die "systemctl reload nginx failed"
    log "nginx reloaded"
  else
    die "nginx -t fails after the changes; not reloaded ($(nginx -t 2>&1 | grep -E 'emerg|error' | head -n 1))"
  fi
fi

for domain in "${REPORT_OK[@]}"; do report "$domain" true; done
exit 0
