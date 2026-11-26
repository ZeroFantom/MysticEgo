#!/usr/bin/env bash
set -euo pipefail
root=$(realpath "$1")
output=$(realpath "$2")
cd "$(dirname "$root")"
folder=$(basename "$root")

package() {
    local platform=$1
    local electron dotnet audit agent local_launcher other_electron
    case "$platform" in
        linux-x64)
            electron=runtime/electron-linux
            other_electron=runtime/electron
            dotnet=runtime/linux-x64.tar.gz
            audit=audit-desktop
            agent=agent-desktop
            local_launcher=local-desktop
            test -x "$folder/$electron/electron"
            test -x "$folder/$audit" && test -x "$folder/$agent" && test -x "$folder/$local_launcher"
            ;;
        win-x64)
            electron=runtime/electron
            other_electron=runtime/electron-linux
            dotnet=runtime/win-x64
            audit=AuditDesktop.exe
            agent=AgentDesktop.cmd
            local_launcher=LocalDesktop.cmd
            test -f "$folder/$electron/electron.exe"
            test -f "$folder/$dotnet/dotnet.exe"
            ;;
    esac
    local public="$output/MysticEgo-Desktop-$platform.zip"
    local developer="$output/MysticEgo-CloudDesk-$platform.zip"
    local entries=(LICENSE docs/legal/commercial.md docs/legal/LICENSE-APACHE-2.0.txt "$audit" "$agent" "$local_launcher" desktop runtime/app runtime/avatars "$electron" "$dotnet")
    for optional in runtime/vision runtime/THIRD_PARTY.md; do
        if [[ -e "$folder/$optional" ]]; then entries+=("$optional"); fi
    done
    if [[ $platform == win-x64 ]]; then
        entries+=(AuditDesktop.dll AuditDesktop.deps.json AuditDesktop.runtimeconfig.json)
    fi
    for entry in "${entries[@]}"; do test -e "$folder/$entry"; done
    rm -f -- "$public" "$developer"
    zip -qr -1 "$public" "${entries[@]/#/$folder/}"
    # Preserve both .NET runtimes in the developer kit for remote deployment.
    zip -qr -1 "$developer" "$folder" -x "$folder/$other_electron/*"
    unzip -tq "$public"
    unzip -tq "$developer"
    unzip -Z1 "$public" > "$public.entries"
    ! grep -q '/tools/MysticEgo.CloudDesk/' "$public.entries"
    rm -f -- "$public.entries"
}
package linux-x64
package win-x64
