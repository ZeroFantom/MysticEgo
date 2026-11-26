#!/usr/bin/env bash
set -euo pipefail
product='@PRODUCT@'
version='@VERSION@'
digest='@DIGEST@'
mode=${1:-}
case "$mode" in
  --help|-h) printf '%s\n' "$product $version (Linux x86_64)" "bash $0 [--install-only | --check | --help]"; exit 0;;
  --check|--install-only|'') ;;
  *) echo 'Unknown option.' >&2; exit 2;;
esac
[[ $(uname -m) == x86_64 ]] || { echo 'Linux x86_64 required.' >&2; exit 1; }
[[ $EUID != 0 || $mode == --check ]] || { echo 'Run as a regular user, not root.' >&2; exit 1; }
self=$(realpath -- "$0")
line=$(awk '/^__MYSTICEGO_PAYLOAD__$/{print NR+1;exit}' "$self")
[[ $line =~ ^[0-9]+$ ]] || exit 1
actual=$(tail -n +"$line" -- "$self" | sha256sum)
[[ $actual == "$digest "* ]] || { echo 'Installer integrity check failed.' >&2; exit 1; }
verify(){
  [[ -x $1/launch.sh && -x $1/runtime/electron-linux/electron && -f $1/runtime/linux-x64.tar.gz && -f $1/LICENSE ]] || return 1
  if [[ $product == CloudDesk ]]; then
    [[ -f $1/tools/MysticEgo.CloudDesk/out/CloudDesk.dll && -f $1/tools/MysticEgo.CloudDesk/dist/index.html ]]
  else
    [[ -f $1/runtime/app/bin/MysticEgo.Installer.dll && -f $1/desktop/station/main.mjs && -f $1/desktop/local/main.mjs && -f $1/desktop/control/main.mjs ]]
  fi
}
umask 077
if [[ $mode == --check ]]; then
  stage=$(mktemp -d)
  trap 'rm -rf -- "$stage"' EXIT
  tail -n +"$line" -- "$self" | tar -xzf - --no-same-owner -C "$stage"
  verify "$stage"
  printf 'Verified %s %s\n' "$product" "$version"
  exit
fi
parent="${XDG_DATA_HOME:-$HOME/.local/share}/mysticego/apps/$product"
mkdir -p -- "$parent"
target="$parent/$version"
[[ ! -L $target ]] || { echo 'Unsafe installation path.' >&2; exit 1; }
if [[ ! -d $target ]]; then
  stage=$(mktemp -d "$parent/.install.XXXXXXXX")
  trap 'rm -rf -- "$stage"' EXIT
  tail -n +"$line" -- "$self" | tar -xzf - --no-same-owner -C "$stage"
  verify "$stage"
  printf '%s\n' "$digest" > "$stage/.installer-sha256"
  if ! mv -T -- "$stage" "$target"; then
    [[ -d $target && ! -L $target ]] || exit 1
    rm -rf -- "$stage"
  fi
fi
verify "$target" && [[ $(cat "$target/.installer-sha256" 2>/dev/null) == "$digest" ]] || { echo "Existing installation does not match: $target" >&2; exit 1; }
printf 'Installed: %s\n' "$target"
[[ $mode == --install-only ]] && exit 0
exec "$target/launch.sh"
__MYSTICEGO_PAYLOAD__
