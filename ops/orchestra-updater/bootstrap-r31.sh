#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 ]]
root=/opt/mysticego/app/runtime/app
api="$root/bin/Api.Orchestra.dll"
world="$root/bin/MysticEgo.WorldAI.dll"
installer="$root/bin/MysticEgo.Installer.dll"
web="$root/web/orchestra"
service=mysticego-orchestra.service
systemctl is-active --quiet "$service"
command -v gh >/dev/null
sha=$(sha256sum < "$api")
expected=$(cat baseline.sha256)
updated=$(sha256sum < payload/Api.Orchestra.dll)
# R3.2.4 used a CI build of the same installation layout.
installed_tag=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("installed",""))' "$web/release-info.json" 2>/dev/null || true)
if [[ $installed_tag != v1.0.0-beta.7-r3.2.4 && $installed_tag != v1.0.0-beta.7-r3.2.5 && $installed_tag != v1.0.0-beta.7-r3.2.6 && $installed_tag != v1.0.0-beta.7-r3.2.7 && ${sha%% *} != "$expected" && ${sha%% *} != "bdd659c0da8a3afb4524b87930ed409a70b37ef5bb20b61ef71efda9c36ca538" && ${sha%% *} != "4141a486ec83ed02241fdee5556fc4ef638003cea2b929203d5ed672f4920eb6" && ${sha%% *} != "b9e7abfb0138979afda0832a39a5527779f22ec57c880ad480bf2262c2a7c7a6" && ${sha%% *} != "01cb96260527f7a59c8c3a50a0d571ab3d1f4fe1f68beb3c02b6d3c59005b8d7" && ${sha%% *} != "5245533f5d9f5f1870af1a133fb1dc45d01017a1384611e78379ee23f1efe178" && ${sha%% *} != "${updated%% *}" ]]; then
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
cp -p "$world" "$backup/MysticEgo.WorldAI.dll"
cp -p "$installer" "$backup/MysticEgo.Installer.dll"
cp -p "$web/index.html" "$backup/index.html"
if [[ -f "$web/release-info.json" ]]; then cp -p "$web/release-info.json" "$backup/"; fi
rollback() {
  trap - ERR
  set +e
  systemctl stop "$service"
  cp -p "$backup/Api.Orchestra.dll" "$api"
  cp -p "$backup/MysticEgo.WorldAI.dll" "$world"
  cp -p "$backup/MysticEgo.Installer.dll" "$installer"
  cp -p "$backup/index.html" "$web/index.html"
  if [[ -f "$backup/release-info.json" ]]; then cp -p "$backup/release-info.json" "$web/"; else rm -f -- "$web/release-info.json"; fi
  systemctl start "$service"
  echo "Восстановлена прежняя сборка. Резервная копия: $backup" >&2
  exit 1
}
trap rollback ERR
systemctl stop "$service"
install -m 0644 payload/Api.Orchestra.dll "$api"
install -m 0644 payload/MysticEgo.WorldAI.dll "$world"
install -m 0644 payload/MysticEgo.Installer.dll "$installer"
install -m 0644 payload/web/assets/* "$web/assets/"
install -m 0644 payload/web/favicon.ico "$web/favicon.ico"
install -m 0644 payload/web/mystic-mark.png "$web/mystic-mark.png"
install -m 0644 payload/web/index.html "$web/index.html"
install -m 0644 payload/web/release-info.json "$web/release-info.json"
systemctl start "$service"
for ((attempt=0; attempt<40; attempt++)); do
  if [[ $(health 2>/dev/null) == "$id" ]]; then
    trap - ERR
    tag=$(python3 -c 'import json; print(json.load(open("payload/web/release-info.json"))["installed"])')
    bash install-worker.sh "$tag"
    echo "Orchestra отвечает. Резервная копия: $backup"
    exit 0
  fi
  sleep 2
done
rollback
