#!/usr/bin/env bash
set -euo pipefail
[[ $EUID == 0 ]]
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 ]] || { echo 'Локальный установщик поддерживает Ubuntu 24.04 LTS.'; exit 0; }
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
target=/opt/mysticego-infrastructure/local
install -d -m 0700 "$target"
install -m 0600 "$here/install.sh" "$here/install.py" "$here/local-runner.py" "$target/"
cat > /etc/systemd/system/mysticego-local-setup.socket <<'UNIT'
[Unit]
Description=MysticEgo local infrastructure installation socket
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
Description=MysticEgo local infrastructure installation
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

# Installed by the existing in-panel updater as well as CloudDesk.
install -m 0600 "$here/reconcile.py" "$here/install-voice.sh" "$here/voice-server.py" "$target/"
install -d -m 0755 /var/lib/mysticego-infrastructure-status
cat > /etc/systemd/system/mysticego-infrastructure-reconcile.service <<'UNIT'
[Unit]
Description=Reconcile local MysticEgo infrastructure
After=network-online.target
[Service]
Type=oneshot
ExecStart=/usr/bin/python3 /opt/mysticego-infrastructure/local/reconcile.py
TimeoutStartSec=50min
UMask=0077
UNIT
cat > /etc/systemd/system/mysticego-infrastructure-reconcile.timer <<'UNIT'
[Unit]
Description=Check local MysticEgo infrastructure
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
