#!/usr/bin/env bash
set -euo pipefail
umask 077
archive=${1:?archive}; domain=${2:?domain}
checksum=${4:-2f410003f5c1ad68cc702f8a5d21dc0143cd1d9b92d3709be75ac08fffaf9f28}
method=${5:-native}
database=${6:-sqlite}
[[ $database == sqlite || $database == postgres-native || $database == postgres-docker || $database == postgres-prepared ]]
[[ $method == native || $method == docker ]]
[[ $checksum =~ ^[a-f0-9]{64}$ ]]
[[ $(id -u) == 0 && $(uname -m) == x86_64 ]]
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 ]] || { echo 'Required: Ubuntu 24.04 x86-64'; exit 1; }
[[ $domain =~ ^[A-Za-z0-9][A-Za-z0-9.-]*\.[A-Za-z]{2,63}$ && $domain != *..* ]]
if [[ -e /opt/mysticego/app || -e /etc/systemd/system/mysticego-orchestra.service ]]; then echo 'Existing installation; refusing to overwrite'; exit 1; fi
if [[ -e /etc/caddy/Caddyfile ]]; then echo 'Existing Caddy config; use a fresh server'; exit 1; fi
if [[ $database == sqlite && -d /etc/mysticego/database-staging ]]; then
  echo 'Prepared database configuration exists. Select prepared PostgreSQL or review it manually.' >&2; exit 1
fi
if [[ $database == postgres-prepared ]]; then
  test -s /etc/mysticego/database-staging/auth-postgres.connection
  test -s /etc/mysticego/database-staging/orchestra-postgres.connection
fi
echo 'CLOUDDESK_STEP:checksum'
printf '%s  %s\n' "$checksum" "$archive" | sha256sum -c -
export DEBIAN_FRONTEND=noninteractive
if [[ ${MYSTICEGO_INSTALL_PREREQS_READY:-} == 1 ]]; then
  # Factory reset has already installed all prerequisites before deleting state.
  for package in ca-certificates caddy curl libicu74 libssl3t64 unzip gh libatomic1 libgomp1; do
    [[ $(dpkg-query -W -f='${Status}' "$package") == 'install ok installed' ]]
  done
else
  echo 'CLOUDDESK_STEP:apt-update'
  apt-get update
  echo 'CLOUDDESK_STEP:apt-install'
  apt-get install -y ca-certificates caddy curl libicu74 libssl3t64 unzip gh libatomic1 libgomp1
fi
echo 'CLOUDDESK_STEP:unpack'
install -d -m 0755 /opt/mysticego /etc/mysticego
stage=$(mktemp -d /opt/mysticego/.stage.XXXXXXXX)
trap 'rm -rf -- "$stage"' EXIT
unzip -q "$archive" -d "$stage"
app="$stage/MysticEgo"
test -x "$app/mysticego"
install -d "$app/runtime/linux-x64"
tar -xzf "$app/runtime/linux-x64.tar.gz" -C "$app/runtime/linux-x64"
test -x "$app/runtime/linux-x64/dotnet"
chmod -R a+rX,go-w "$app"
mv -T "$app" /opt/mysticego/app
echo 'CLOUDDESK_STEP:configure'
id -u mysticego >/dev/null 2>&1 || useradd --system --user-group --no-create-home --home-dir /var/lib/mysticego --shell /usr/sbin/nologin mysticego
if [[ $database == postgres-native || $database == postgres-docker ]]; then
  dbstage=$(mktemp -d)
  component=postgres
  [[ $database != postgres-native ]] || component=postgres-native
  cp /opt/mysticego/app/ops/infrastructure/{install.sh,install.py} "$dbstage/"
  printf '{"component":"%s","bind":"127.0.0.1","cidr":"127.0.0.1/32"}\n' "$component" > "$dbstage/request.json"
  bash "$dbstage/install.sh"
  for dbrole in auth orchestra; do
    printf '{"component":"prepare-postgres","role":"%s","bind":"127.0.0.1"}\n' "$dbrole" > "$dbstage/request.json"
    bash "$dbstage/install.sh"
  done
  rm -rf -- "$dbstage"
fi
if [[ $database != sqlite ]]; then
  install -d -o mysticego -g mysticego -m 0700 /var/lib/mysticego
  for dbrole in auth orchestra; do
    install -m 0600 -o mysticego -g mysticego "/etc/mysticego/database-staging/$dbrole-postgres.connection" "/var/lib/mysticego/$dbrole-postgres.connection"
  done
fi
printf 'MYSTICEGO_CENTER_AUTH_URL=https://%s\nMYSTICEGO_CENTER_ORCHESTRA_URL=https://%s\n' "$domain" "$domain" > /etc/mysticego/orchestra.env
cat > /etc/systemd/system/mysticego-orchestra.service <<'UNIT'
[Unit]
Description=MysticEgo R3 Orchestra and Auth
Wants=network-online.target
After=network-online.target
[Service]
Type=simple
User=mysticego
Group=mysticego
WorkingDirectory=/opt/mysticego/app
StateDirectory=mysticego
StateDirectoryMode=0700
UMask=0077
EnvironmentFile=/etc/mysticego/orchestra.env
ExecStart=/opt/mysticego/app/mysticego --headless --no-lm --output /var/lib/mysticego
Restart=on-failure
RestartSec=5s
NoNewPrivileges=true
PrivateTmp=true
[Install]
WantedBy=multi-user.target
UNIT
chmod 0644 /etc/systemd/system/mysticego-orchestra.service
if [[ $method == docker ]]; then
  if ! command -v docker >/dev/null; then apt-get install -y docker.io; fi
  systemctl enable --now docker.service
  docker build --pull --tag mysticego-orchestra-runtime:ubuntu24.04 - <<'DOCKERFILE'
FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates libicu74 libssl3t64 openssh-client util-linux && rm -rf /var/lib/apt/lists/*
DOCKERFILE
  install -d -o mysticego -g mysticego -m 0700 /var/lib/mysticego
  install -d -m 0755 /etc/systemd/system/mysticego-orchestra.service.d
  uid=$(id -u mysticego)
  gid=$(id -g mysticego)
  cat > /etc/systemd/system/mysticego-orchestra.service.d/docker.conf <<UNIT
[Unit]
Requires=docker.service
After=docker.service
[Service]
User=root
Group=root
ExecStart=
ExecStart=/usr/bin/docker run --rm --name mysticego-orchestra --network host --user $uid:$gid --cap-drop ALL --security-opt no-new-privileges --env-file /etc/mysticego/orchestra.env --mount type=bind,src=/opt/mysticego/app,dst=/opt/mysticego/app,readonly --mount type=bind,src=/var/lib/mysticego,dst=/var/lib/mysticego --workdir /opt/mysticego/app mysticego-orchestra-runtime:ubuntu24.04 /opt/mysticego/app/mysticego --headless --no-lm --output /var/lib/mysticego
ExecStop=/usr/bin/docker stop -t 20 mysticego-orchestra
UNIT
fi
cat > /etc/caddy/Caddyfile <<CADDY
$domain {
  @token {
    method POST
    path /api/identities/token
  }
  handle @token {
    reverse_proxy 127.0.0.1:5400
  }
  handle {
    reverse_proxy 127.0.0.1:5500
  }
}
CADDY
chmod 0644 /etc/caddy/Caddyfile
caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
systemctl daemon-reload
echo 'CLOUDDESK_STEP:start'
systemctl enable --now mysticego-orchestra.service
echo 'CLOUDDESK_STEP:local-health'
health() {
  curl -fs --max-time 5 "$1" | python3 -c 'import json,sys,uuid; h=json.load(sys.stdin); assert h["status"]=="alive" and h["kind"].lower()=="orchestra"; n=uuid.UUID(h["node"]); assert n.int; print(n)'
}
node=
for ((i=0;i<60;i++)); do
  if node=$(health http://127.0.0.1:5500/health/live 2>/dev/null); then break; fi
  sleep 2
done
if [[ -z $node ]]; then
  journalctl -u mysticego-orchestra -n 30 --no-pager >&2
  echo 'Local Orchestra identity not ready; do not repeat installation.' >&2
  exit 1
fi
echo 'CLOUDDESK_STEP:https'
systemctl enable caddy.service
systemctl restart caddy.service
public=
for ((i=0;i<20;i++)); do
  if public=$(health "https://$domain/health/live" 2>/dev/null) && [[ $public == "$node" ]]; then break; fi
  sleep 5
done
[[ $public == "$node" ]] || { echo 'HTTPS Orchestra identity does not match local node.' >&2; exit 1; }
echo 'CLOUDDESK_STEP:verify-storage'
if [[ $database == sqlite ]]; then
  for ((i=0;i<180;i++)); do
    [[ -s /var/lib/mysticego/auth.db && -s /var/lib/mysticego/orchestra/node.db ]] && break
    sleep 2
  done
  [[ -s /var/lib/mysticego/auth.db && -s /var/lib/mysticego/orchestra/node.db ]] || { echo 'SQLite storage is not ready; preserve the installed node and diagnose.' >&2; exit 1; }
fi
echo 'CLOUDDESK_STEP:maintenance'
recovery=/opt/mysticego/app/ops/orchestra-updater/recover-maintenance.sh
maintenance_ready=false
if [[ -f $recovery ]]; then
  recovery_args=("$node")
  if [[ "${MYSTICEGO_INSTALL_LOCK_FD:-}" == 9 ]]; then
    if grep -Fq -- '--inherited-deploy-lock' "$recovery"; then
      recovery_args+=(--inherited-deploy-lock)
    else
      # Compatibility with historical published archives, which take their own lock.
      flock -u 9
    fi
  fi
  # Only retry the non-destructive maintenance setup, never the installation.
  for attempt in 1 2; do
    [[ $(health http://127.0.0.1:5500/health/live) == "$node" ]] || break
    if bash "$recovery" "${recovery_args[@]}"; then maintenance_ready=true; break; fi
    [[ $attempt == 2 ]] || sleep 5
  done
else
  local_setup=/opt/mysticego/app/ops/infrastructure/install-local.sh
  if [[ -f $local_setup ]] && bash "$local_setup" &&
     systemctl is-active --quiet mysticego-orchestra-updater.timer &&
     systemctl is-active --quiet mysticego-infrastructure-reconcile.timer &&
     systemctl is-active --quiet mysticego-local-setup.socket; then
    maintenance_ready=true
  fi
fi
[[ $(health http://127.0.0.1:5500/health/live) == "$node" && $(health "https://$domain/health/live") == "$node" ]] || { echo 'Orchestra identity changed during maintenance setup.' >&2; exit 1; }
if [[ $maintenance_ready == true ]]; then
  echo CLOUDDESK_MAINTENANCE_SCHEDULED
else
  echo CLOUDDESK_MAINTENANCE_PENDING
  echo 'Orchestra is installed; maintenance setup is incomplete. Use non-destructive recovery, not reset.'
fi
echo 'CLOUDDESK_STEP:complete'
echo "Orchestra ready: https://$domain/ (Audit: /audit-portal/)"
echo "ORCHESTRA_INSTALL_OK $node"
