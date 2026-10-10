#!/bin/sh
# Run from the reviewed release on the designated server, after loading images.
set -eu
cd "$(dirname "$0")/.."
root=/mnt/storage/auto-post
mkdir -p "$root/state/postgres" "$root/state/keys"
mkdir -p "$root/backups"
chmod 700 "$root/backups"
if [ ! -e backups ]; then ln -s "$root/backups" backups; fi
chown 1654:1654 "$root/state/keys"
chmod 700 "$root/state/keys"
if [ ! -f "$root/private.env" ]; then
    umask 077
    python3 - "$root/private.env" <<'PY'
import secrets,sys
password=secrets.token_hex(24)
lines={
 'APP_MODE':'demo','ADMIN_TOKEN':secrets.token_hex(32),'POSTGRES_PASSWORD':password,
 'DATABASE_URL':f'Host=postgres;Database=autopost;Username=autopost;Password={password}',
 'OPENAI_BASE_URL':'https://api.siliconflow.cn/v1','OPENAI_MODEL':'zai-org/GLM-5.3-Flash',
 'OPENAI_API_KEY':'','X_BEARER_TOKEN':'','X_USER_ACCESS_TOKEN':'','TELEGRAM_BOT_TOKEN':'','TELEGRAM_CHAT_ID':'',
 'TRUSTED_PROXY':'172.30.86.1','KEYS_PATH':'/keys','DATA_ROOT':'/mnt/storage/auto-post/state',
 'Logging__LogLevel__Microsoft.EntityFrameworkCore':'Warning'}
with open(sys.argv[1],'x') as f:
 for k,v in lines.items(): f.write(f'{k}={v}\n')
PY
fi
if [ ! -e .env ]; then ln -s "$root/private.env" .env; fi
docker compose --env-file .env -f deploy/compose.dotnet.yaml up -d postgres
docker compose --env-file .env -f deploy/compose.dotnet.yaml --profile tools run --rm migrate
if grep -qx 'APP_MODE=demo' "$root/private.env"; then
    docker compose --env-file .env -f deploy/compose.dotnet.yaml --profile tools run --rm migrate seed-demo
fi
docker compose --env-file .env -f deploy/compose.dotnet.yaml up -d --no-build api worker
attempt=0
until curl --fail --silent http://127.0.0.1:8090/api/health; do
    attempt=$((attempt+1)); [ "$attempt" -lt 30 ] || exit 1
    sleep 2
done
printf '\nPrivate configuration: %s/private.env\n' "$root"
sh deploy/backup.sh
install -m 644 deploy/auto-post-backup.service /etc/systemd/system/auto-post-backup.service
install -m 644 deploy/auto-post-backup.timer /etc/systemd/system/auto-post-backup.timer
systemctl daemon-reload
systemctl enable --now auto-post-backup.timer
