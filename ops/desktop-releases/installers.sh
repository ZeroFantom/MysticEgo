#!/usr/bin/env bash
set -euo pipefail
root=$(realpath "$1")
output=$(realpath "$2")
version=$3
[[ $version =~ ^v[0-9][A-Za-z0-9._-]{0,80}$ ]]
here=$(cd -- "$(dirname -- "$0")" && pwd -P)
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
for platform in linux-x64 win-x64; do
  public="$work/public-$platform"
  dev="$work/dev-$platform"
  mkdir -p "$public" "$dev"
  unzip -q "$output/MysticEgo-Desktop-$platform.zip" -d "$public"
  unzip -q "$output/MysticEgo-CloudDesk-$platform.zip" -d "$dev"
  public="$public/$(basename "$root")"
  dev="$dev/$(basename "$root")"
  for product in AuditDesktop AgentDesktop LocalDesktop ControlDesktop CloudDesk; do
    stage=$public
    [[ $product != CloudDesk ]] || stage=$dev
    if [[ $product == ControlDesktop ]]; then cp -a "$root/mysticego" "$public/mysticego"; fi
    case "$product" in
      AuditDesktop) linux='./audit-desktop'; win='runtime\electron\electron.exe desktop\station';;
      AgentDesktop) linux='./agent-desktop'; win='runtime\electron\electron.exe desktop\station --agent-desktop';;
      LocalDesktop) linux='./local-desktop'; win='runtime\electron\electron.exe desktop\local';;
      ControlDesktop) linux='./mysticego'; win='runtime\electron\electron.exe desktop\control';;
      CloudDesk) linux='bash tools/MysticEgo.CloudDesk/run.sh'; win='call tools\MysticEgo.CloudDesk\run-windows.cmd';;
    esac
    name="MysticEgo-$product-$platform"
    if [[ $platform == linux-x64 ]]; then
      printf '#!/bin/sh\nset -eu\ncd -- "$(dirname -- "$0")"\nexec %s\n' "$linux" > "$stage/launch.sh"
      chmod 755 "$stage/launch.sh"
      tar -czf "$work/payload.tar.gz" -C "$stage" .
      hash=$(sha256sum "$work/payload.tar.gz" | cut -d ' ' -f1)
      sed -e "s/@PRODUCT@/$product/g" -e "s/@VERSION@/$version/g" -e "s/@DIGEST@/$hash/g" "$here/linux-installer.sh" > "$output/$name.run"
      cat "$work/payload.tar.gz" >> "$output/$name.run"
      chmod 755 "$output/$name.run"
      bash "$output/$name.run" --check
      rm "$work/payload.tar.gz" "$stage/launch.sh"
    else
      if [[ $product == CloudDesk ]]; then
        printf '@echo off\r\ncd /d "%%~dp0"\r\n%s\r\n' "$win" > "$stage/launch.cmd"
      else
        printf '@echo off\r\ncd /d "%%~dp0"\r\nset "MYSTICEGO_DOTNET=%%~dp0runtime\\win-x64\\dotnet.exe"\r\nstart "" %s\r\n' "$win" > "$stage/launch.cmd"
      fi
      sed -e "s|@PRODUCT@|$product|g" -e "s|@VERSION@|$version|g" -e "s|@SOURCE@|$stage|g" -e "s|@OUTPUT@|$output/$name.exe|g" "$here/windows-installer.nsi" > "$work/installer.nsi"
      makensis -V2 "$work/installer.nsi"
      test "$(head -c2 "$output/$name.exe")" = MZ
      rm "$stage/launch.cmd"
    fi
  done
done
