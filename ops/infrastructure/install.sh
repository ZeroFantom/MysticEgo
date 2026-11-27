#!/usr/bin/env bash
set -euo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 ]] || { echo 'Установщик требует root.'; exit 1; }
. /etc/os-release
[[ $ID == ubuntu && ${VERSION_ID:-} =~ ^[0-9]+\.[0-9]+$ ]] && dpkg --compare-versions "$VERSION_ID" ge 24.04 || { echo 'Требуется Ubuntu 24.04 или новее.' >&2; exit 1; }
exec 9>/run/lock/mysticego-infrastructure.lock
flock -n 9 || { echo 'На этом сервере уже выполняется установка.'; exit 1; }
[[ -r request.json ]] || { echo 'Отсутствует request.json.'; exit 1; }
exec python3 install.py
