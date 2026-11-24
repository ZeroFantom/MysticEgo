#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 ]]
root=/opt/mysticego/app/runtime/app
api="$root/bin/Api.Orchestra.dll"
web="$root/web/orchestra"
service=mysticego-orchestra.service
systemctl is-active --quiet "$service"
command -v gh >/dev/null
sha=$(sha256sum < "$api")
expected=$(cat baseline.sha256)
updated=$(sha256sum < payload/Api.Orchestra.dll)
if [[ ${sha%% *} != "$expected" && ${sha%% *} != "${updated%% *}" ]]; then
  echo 'Установлена другая сборка Orchestra. Примените исходники к её релизу.' >&2
  exit 1
fi
health() {
  curl -fsS --max-time 3 http://127.0.0.1:5500/health/live |
    python3 -c 'import json,sys; h=json.load(sys.stdin); assert h["status"]=="alive" and h["kind"].lower()=="orchestra"; print(h["node"])'
}
id=$(health)
backup=$(mktemp -d /opt/mysticego/.bootstrap-r31.XXXXXXXX)
cp -p "$api" "$backup/Api.Orchestra.dll"
cp -p "$web/index.html" "$backup/index.html"
rollback() {
  trap - ERR
  set +e
  systemctl stop "$service"
  cp -p "$backup/Api.Orchestra.dll" "$api"
  cp -p "$backup/index.html" "$web/index.html"
  systemctl start "$service"
  echo "Восстановлена прежняя сборка. Резервная копия: $backup" >&2
  exit 1
}
trap rollback ERR
systemctl stop "$service"
install -m 0644 payload/Api.Orchestra.dll "$api"
install -m 0644 payload/web/assets/* "$web/assets/"
install -m 0644 payload/web/favicon.ico "$web/favicon.ico"
install -m 0644 payload/web/index.html "$web/index.html"
systemctl start "$service"
for ((attempt=0; attempt<40; attempt++)); do
  if [[ $(health 2>/dev/null) == "$id" ]]; then
    trap - ERR
    bash install-worker.sh v1.0.0-beta.7-r3.1
    echo "Orchestra отвечает. Резервная копия: $backup"
    exit 0
  fi
  sleep 2
done
rollback
