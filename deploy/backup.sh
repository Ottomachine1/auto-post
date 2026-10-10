#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
mkdir -p backups
chmod 700 backups
stamp=$(date -u +%Y%m%dT%H%M%SZ)
file="backups/autopost-$stamp.dump"
umask 077
docker compose -f deploy/compose.dotnet.yaml --env-file .env exec -T postgres pg_dump -U autopost -d autopost -Fc > "$file.tmp"
docker compose -f deploy/compose.dotnet.yaml --env-file .env exec -T postgres pg_restore --list < "$file.tmp" > /dev/null
mv "$file.tmp" "$file"
find backups -maxdepth 1 -type f -name 'autopost-*.dump' -mtime +7 -delete
printf '%s\n' "$file"
