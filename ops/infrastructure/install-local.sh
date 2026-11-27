#!/usr/bin/env bash
set -euo pipefail
[[ $EUID == 0 ]]
. /etc/os-release
[[ $ID == ubuntu && ${VERSION_ID:-} =~ ^[0-9]+\.[0-9]+$ ]] && dpkg --compare-versions "$VERSION_ID" ge 24.04 || { echo 'Требуется Ubuntu 24.04 или новее.' >&2; exit 1; }
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# Upgrade the existing privileged updater BEFORE any systemd or dependency operation.
# Legacy releases call this hook with a 20-second deadline; the new updater can
# retry every timer cycle even if the rest of this invocation is interrupted.
if [[ "$here" == /opt/mysticego/app/ops/infrastructure && -f /usr/local/libexec/mysticego/orchestra-updater.py ]]; then
  source="$here/../orchestra-updater/update-orchestra.py"
  [[ -f "$source" && ! -L "$source" && $(stat -c %u "$source") == 0 && $(( 8#$(stat -c %a "$source") & 8#022 )) == 0 ]]
  stage=$(mktemp /usr/local/libexec/mysticego/.orchestra-updater.XXXXXXXX)
  install -o root -g root -m 0644 "$source" "$stage"
  mv -T "$stage" /usr/local/libexec/mysticego/orchestra-updater.py
fi
target=/opt/mysticego-infrastructure/local
install -d -m 0700 "$target"
# Copy the complete service before enabling its endpoints.
for script in install.sh install.py local-runner.py reconcile.py install-voice.sh voice-server.py; do
  stage=$(mktemp "$target/.$script.XXXXXXXX")
  install -o root -g root -m 0600 "$here/$script" "$stage"
  mv -T "$stage" "$target/$script"
done
cat > /etc/systemd/system/mysticego-local-setup.socket <<'UNIT'
[Unit]
Description=Orchestra Maintenance Service socket
[Socket]
ListenStream=/var/lib/mysticego/local-setup.sock
SocketUser=mysticego
SocketGroup=mysticego
SocketMode=0600
Accept=yes
MaxConnections=1
RemoveOnStop=yes
[Install]
WantedBy=sockets.target
UNIT
cat > /etc/systemd/system/mysticego-local-setup@.service <<'UNIT'
[Unit]
Description=Orchestra Maintenance Service request
[Service]
Type=exec
ExecStart=/usr/bin/python3 /opt/mysticego-infrastructure/local/local-runner.py
StandardInput=socket
StandardOutput=journal
StandardError=journal
RuntimeMaxSec=46min
UMask=0077
UNIT
chmod 0644 /etc/systemd/system/mysticego-local-setup*
systemctl daemon-reload
systemctl enable --now mysticego-local-setup.socket
# An active unit can retain a socket inode that was removed on disk.
if [[ ! -S /var/lib/mysticego/local-setup.sock ]]; then
  systemctl restart mysticego-local-setup.socket
fi
[[ -S /var/lib/mysticego/local-setup.sock ]]

# Installed by the existing in-panel updater as well as CloudDesk.
install -d -o root -g root -m 0755 /var/lib/mysticego-infrastructure-status
cat > /etc/systemd/system/mysticego-infrastructure-reconcile.service <<'UNIT'
[Unit]
Description=Orchestra Maintenance Service
After=network-online.target
[Service]
Type=oneshot
CPUAccounting=yes
MemoryAccounting=yes
TasksAccounting=yes
ExecStart=/usr/bin/python3 /opt/mysticego-infrastructure/local/reconcile.py
TimeoutStartSec=50min
UMask=0077
UNIT
cat > /etc/systemd/system/mysticego-infrastructure-reconcile.timer <<'UNIT'
[Unit]
Description=Orchestra Maintenance Service timer
[Timer]
OnBootSec=45s
OnUnitInactiveSec=30s
[Install]
WantedBy=timers.target
UNIT
if [[ -d "$here/../monitoring" ]]; then
  cp -a "$here/../monitoring" "$target/"
fi
systemctl daemon-reload
systemctl enable --now mysticego-infrastructure-reconcile.timer
systemctl start --no-block mysticego-infrastructure-reconcile.service
systemctl is-enabled --quiet mysticego-infrastructure-reconcile.timer
systemctl is-active --quiet mysticego-infrastructure-reconcile.timer
systemctl is-active --quiet mysticego-local-setup.socket
echo 'Локальная служба обслуживания установлена; первый цикл запущен.'
