#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
role=$1
shift
runtime="$root/runtime/linux-x64"
if [ ! -x "$runtime/dotnet" ]; then
    stage=$(mktemp -d "$root/runtime/.dotnet.XXXXXX")
    trap 'rm -rf "$stage"' EXIT HUP INT TERM
    tar -xzf "$root/runtime/linux-x64.tar.gz" -C "$stage"
    if ! mv -T "$stage" "$runtime" 2>/dev/null && [ ! -x "$runtime/dotnet" ]; then
        echo "Не удалось распаковать среду .NET в $runtime." >&2
        exit 2
    fi
    rm -rf "$stage"
    trap - EXIT HUP INT TERM
fi
export MYSTICEGO_DOTNET="$runtime/dotnet"
if [ "${1:-}" = "--headless" ]; then
    shift
    if [ "$role" = control ]; then
        exec "$MYSTICEGO_DOTNET" "$root/runtime/app/bin/MysticEgo.Installer.dll" --lab --no-browser --root "$root" "$@"
    fi
    profile=$1
    shift
    exec "$MYSTICEGO_DOTNET" "$root/runtime/app/bin/MysticEgo.Installer.dll" --join "$profile" --root "$root" "$@"
fi
electron="$root/runtime/electron-linux/electron"
if [ "$role" = station ]; then
    helper="$root/runtime/electron-linux/chrome-sandbox"
    if [ "$(stat -c '%u:%a' -- "$helper" 2>/dev/null || true)" != "0:4755" ]; then
        exec "$electron" --disable-setuid-sandbox "$root/desktop/$role" "$@"
    fi
fi
exec "$electron" "$root/desktop/$role" "$@"
