#!/usr/bin/env bash
# Nightly LinkSuite database backup: a gzipped pg_dump of the "saas" database into /root/linksuite-backups.
# Keeps 14 days of nightly dumps; dumps taken by hand before a deploy (any name without "-nightly") are never deleted.
#
# Install (root crontab on the VPS), 02:30 Lagos time = 01:30 UTC:
#   30 1 * * * /opt/linksuite/deploy/backup-db.sh >> /root/linksuite-backups/backup.log 2>&1
# Restore: gunzip -c FILE | docker exec -i linksuite-db-1 psql -U postgres -d saas   (into an empty database)
set -euo pipefail

DIR="${BACKUP_DIR:-/root/linksuite-backups}"
KEEP_DAYS="${KEEP_DAYS:-14}"
CONTAINER="${DB_CONTAINER:-linksuite-db-1}"

mkdir -p "$DIR"
chmod 700 "$DIR"
OUT="$DIR/saas-$(date -u +%Y%m%d-%H%M%S)-nightly.sql.gz"
TMP="$OUT.partial"
trap 'rm -f "$TMP"' EXIT

docker exec "$CONTAINER" pg_dump -U postgres -d saas | gzip -9 > "$TMP"

# A dump only counts when the archive is whole and pg_dump wrote its closing line.
if ! gzip -t "$TMP" || ! gunzip -c "$TMP" | tail -n 5 | grep -q "PostgreSQL database dump complete"; then
  echo "$(date -u +%FT%TZ) FAILED: incomplete dump, previous backups kept" >&2
  exit 1
fi

mv "$TMP" "$OUT"
chmod 600 "$OUT"
find "$DIR" -maxdepth 1 -name 'saas-*-nightly.sql.gz' -mtime +"$KEEP_DAYS" -delete
echo "$(date -u +%FT%TZ) ok $OUT $(du -h "$OUT" | cut -f1)"
