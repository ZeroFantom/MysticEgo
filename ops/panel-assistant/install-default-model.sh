#!/usr/bin/env bash
set -euo pipefail
umask 077
source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
infra=${1:-"$source_dir/../infrastructure"}
config=/etc/mysticego/orchestra.env
if [[ -r $config ]] && grep -Eq '^(MYSTICEGO_DEFAULT_MODEL=off|PanelAssistant__Model=.+|WorldAI__RemoteWorkers__[^=]+__Url=.+)$' "$config"; then
  echo 'Сохранена заданная конфигурация моделей MysticEgo.'
  exit 0
fi
if systemctl is-active --quiet mysticego-lmstudio.service ||
   systemctl is-active --quiet mysticego-default-model.service ||
   curl -sS --max-time 3 http://127.0.0.1:1234/v1/models >/dev/null 2>&1; then
  echo 'Сохранена действующая настройка LM Studio.'
  exit 0
fi
destination=/opt/mysticego-infrastructure/default-model
install -d -m 0700 "$destination"
install -m 0600 "$infra/install.py" "$infra/install.sh" "$destination/"
printf '%s\n' '{"component":"lmstudio","role":"panel-data"}' > "$destination/request.json"
systemd-run --quiet --collect --no-block --unit=mysticego-default-model \
  --description='Install MysticEgo default LM Studio model' \
  --property=RuntimeMaxSec=45min /bin/bash "$destination/install.sh"
echo 'LM Studio и Qwen3 1.7B готовятся в фоне.'
