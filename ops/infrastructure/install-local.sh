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
