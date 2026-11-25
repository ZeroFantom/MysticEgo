#!/usr/bin/env bash
set -euo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 ]] || { echo 'Установщик требует root.'; exit 1; }
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 ]] || { echo 'Поддерживается Ubuntu 24.04 LTS.'; exit 1; }
exec 9>/run/lock/mysticego-infrastructure.lock
flock -n 9 || { echo 'На этом сервере уже выполняется установка.'; exit 1; }
[[ -r request.json ]] || { echo 'Отсутствует request.json.'; exit 1; }
exec python3 install.py
