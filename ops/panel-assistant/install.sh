#!/usr/bin/env bash
set -euo pipefail
if (( EUID != 0 )); then echo 'Запустите через sudo.' >&2; exit 1; fi
if [[ $# != 2 || ! -f $1 || ! -x $1 || ! -f $2 ]]; then
  echo 'Использование: sudo bash install.sh /path/to/llama-server /path/to/model.gguf' >&2
  exit 2
fi
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
install -d -o root -g root -m 0755 /opt/mysticego/panel-model
install -o root -g root -m 0755 -- "$1" /opt/mysticego/panel-model/llama-server
install -o root -g root -m 0644 -- "$2" /opt/mysticego/panel-model/assistant.gguf
install -o root -g root -m 0644 "$here/mysticego-panel-model.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now mysticego-panel-model.service
