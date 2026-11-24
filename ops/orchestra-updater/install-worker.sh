#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 && ${1:-} =~ ^v[0-9][a-zA-Z0-9._-]{0,80}$ ]]
command -v gh >/dev/null
systemctl is-active --quiet mysticego-orchestra.service
install -d -m 0755 /usr/local/libexec/mysticego /var/lib/mysticego-orchestra-updater
install -d -o mysticego -g mysticego -m 0700 /var/lib/mysticego/update-inbox
install -m 0644 update-orchestra.py /usr/local/libexec/mysticego/orchestra-updater.py
install -m 0644 mysticego-orchestra-updater.{service,timer} /etc/systemd/system/
if [[ ! -e /var/lib/mysticego-orchestra-updater/current.json ]]; then
  printf '{"tag":"%s"}\n' "$1" > /var/lib/mysticego-orchestra-updater/current.json
fi
systemctl daemon-reload
systemctl enable --now mysticego-orchestra-updater.timer
systemctl start --no-block mysticego-orchestra-updater.service
