#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
if [[ $EUID == 0 ]]; then echo 'Запускайте от обычного пользователя, не root'; exit 1; fi
if command -v dotnet >/dev/null; then export CLOUDDESK_DOTNET=$(command -v dotnet); elif [[ -x $HOME/.dotnet/dotnet ]]; then export CLOUDDESK_DOTNET=$HOME/.dotnet/dotnet; else echo 'Установите .NET SDK 10'; exit 1; fi
for cmd in node npm gh ssh scp ssh-keygen ssh-keyscan konsole; do command -v "$cmd" >/dev/null || { echo "Не найден $cmd"; exit 1; }; done
"$CLOUDDESK_DOTNET" publish backend/CloudDesk.csproj -c Release -o out
if [[ ! -d node_modules ]]; then npm install --foreground-scripts; fi
if [[ ! -x node_modules/electron/dist/electron || ! -s node_modules/electron/path.txt ]]; then
  echo "Electron не готов. Используйте clouddesk-repair-v2.sh и, при необходимости, v3." >&2; exit 1
fi
npm run build
exec npm start
