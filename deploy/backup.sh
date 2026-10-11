#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
mkdir -p backups
chmod 700 backups
stamp=$(date -u +%Y%m%dT%H%M%SZ)
if grep -qx 'DATABASE_PROVIDER=mysql' .env; then
    file="backups/autopost-$stamp.json"
    transfer="${DATA_ROOT:-/mnt/storage/auto-post/state}/transfer"
    mkdir -p "$transfer"
    chown 1654:1654 "$transfer"
    chmod 700 "$transfer"
    umask 077
    docker compose -f deploy/compose.mysql.yaml --env-file .env --profile tools run --rm migrate export-db /transfer/backup.json > "$file.report.tmp"
    cp "$transfer/backup.json" "$file.tmp"
    chmod 600 "$file.tmp" "$file.report.tmp"
    mv "$file.tmp" "$file"
    mv "$file.report.tmp" "$file.report.json"
    find backups -maxdepth 1 -type f -name 'autopost-*.json' -mtime +7 -delete
    printf '%s\n' "$file"
    exit 0
fi
file="backups/autopost-$stamp.dump"
umask 077
docker compose -f deploy/compose.dotnet.yaml --env-file .env exec -T postgres pg_dump -U autopost -d autopost -Fc > "$file.tmp"
docker compose -f deploy/compose.dotnet.yaml --env-file .env exec -T postgres pg_restore --list < "$file.tmp" > /dev/null
mv "$file.tmp" "$file"
find backups -maxdepth 1 -type f -name 'autopost-*.dump' -mtime +7 -delete
printf '%s\n' "$file"
