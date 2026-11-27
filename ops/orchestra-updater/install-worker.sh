#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 && ${1:-} =~ ^v[0-9][a-zA-Z0-9._-]{0,80}$ ]]
command -v gh >/dev/null
systemctl is-active --quiet mysticego-orchestra.service
install -d -m 0755 /usr/local/libexec/mysticego /var/lib/mysticego-orchestra-updater
install -d -o mysticego -g mysticego -m 0700 /var/lib/mysticego/update-inbox
stage=$(mktemp /usr/local/libexec/mysticego/.updater.XXXXXXXX)
install -o root -g root -m 0644 update-orchestra.py "$stage"
mv -T "$stage" /usr/local/libexec/mysticego/orchestra-updater.py
install -m 0644 mysticego-orchestra-updater.{service,timer} /etc/systemd/system/
if [[ ! -e /var/lib/mysticego-orchestra-updater/current.json ]]; then
  printf '{"tag":"%s"}\n' "$1" > /var/lib/mysticego-orchestra-updater/current.json
fi
# Fixed independent recovery service, no arbitrary command or root SSH key.
if [[ -f rescue-request.py && -f mysticego-maintenance-rescue.path && -f mysticego-maintenance-rescue.service ]]; then
  stage=$(mktemp /usr/local/libexec/mysticego/.rescue.XXXXXXXX)
  install -o root -g root -m 0644 rescue-request.py "$stage"
  mv -T "$stage" /usr/local/libexec/mysticego/maintenance-rescue.py
  install -o root -g root -m 0644 mysticego-maintenance-rescue.{path,service} /etc/systemd/system/
  systemctl daemon-reload
  systemctl enable --now mysticego-maintenance-rescue.path
  systemctl is-active --quiet mysticego-maintenance-rescue.path
  if [[ ! -f /var/lib/mysticego-orchestra-updater/rescue.json ]]; then
    stage=$(mktemp /var/lib/mysticego-orchestra-updater/.rescue.XXXXXXXX)
    printf '{"state":"ready","reason":"","checkedAt":"%s"}\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$stage"
    chmod 0644 "$stage"
    mv -T "$stage" /var/lib/mysticego-orchestra-updater/rescue.json
  fi
fi
systemctl daemon-reload
systemctl enable --now mysticego-orchestra-updater.timer
systemctl start --no-block mysticego-orchestra-updater.service
