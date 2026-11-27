#!/usr/bin/env bash
# Test the actual installer template without downloading Electron or touching the user's profile.
set -euo pipefail
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
mkdir -p "$work/payload/runtime/electron-linux" "$work/payload/tools/MysticEgo.CloudDesk/"{out,dist}
printf '#!/bin/sh\nexit 0\n' > "$work/payload/launch.sh"
cp "$work/payload/launch.sh" "$work/payload/runtime/electron-linux/electron"
chmod 755 "$work/payload/launch.sh" "$work/payload/runtime/electron-linux/electron"
touch "$work/payload/"{LICENSE,runtime/linux-x64.tar.gz,tools/MysticEgo.CloudDesk/out/CloudDesk.dll,tools/MysticEgo.CloudDesk/dist/index.html}
tar -czf "$work/payload.tar.gz" -C "$work/payload" .
digest=$(sha256sum "$work/payload.tar.gz" | cut -d ' ' -f1)
installer="$work/MysticEgo-CloudDesk-linux-x64.run"
sed -e 's/@PRODUCT@/CloudDesk/g' -e 's/@VERSION@/v0.0.1-test/g' -e "s/@DIGEST@/$digest/g" "$here/linux-installer.sh" > "$installer"
cat "$work/payload.tar.gz" >> "$installer"
# Mimic a browser download with no executable permission.
chmod 644 "$installer"
test ! -x "$installer"
bash "$installer" --check
if (( EUID != 0 )); then
  XDG_DATA_HOME="$work/data" bash "$installer" --install-only
  XDG_DATA_HOME="$work/data" bash "$installer"
  test -x "$work/data/mysticego/apps/CloudDesk/v0.0.1-test/launch.sh"
fi
printf 'tampered' >> "$installer"
if bash "$installer" --check; then
  echo 'ERROR: corrupted payload accepted' >&2
  exit 1
fi
echo 'RUN installer checks passed.'
