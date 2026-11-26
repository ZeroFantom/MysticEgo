#!/usr/bin/env bash
set -euo pipefail
target=$(realpath "$1")
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
download(){
  curl --fail --location --retry 3 --proto '=https' --tlsv1.2 "$1" -o "$2"
  printf '%s  %s\n' "$3" "$2" | sha256sum -c -
}
download 'https://github.com/PowerShell/Win32-OpenSSH/releases/download/10.4.0.0p1-Preview/OpenSSH-Win64.zip' "$work/ssh.zip" b67ff8becd203502190ec42ac7787c24c2cd3325ed7700485fe7a1c83d2a7957
download 'https://github.com/cli/cli/releases/download/v2.106.0/gh_2.106.0_windows_amd64.zip' "$work/gh.zip" d406f442ca0baa3d3e9c695e9cded1aa48e67dad3294b8947e5ab84f980b4844
unzip -q "$work/ssh.zip" -d "$work/ssh"
unzip -q "$work/gh.zip" -d "$work/gh"
ssh=$(find "$work/ssh" -iname ssh.exe -print -quit)
gh=$(find "$work/gh" -iname gh.exe -print -quit)
test -n "$ssh" && test -n "$gh"
mkdir -p "$target/openssh" "$target/github"
# Only client binaries, libraries and upstream legal notices; no service registration.
for name in ssh.exe scp.exe ssh-keygen.exe ssh-keyscan.exe sftp.exe; do cp "$(dirname "$ssh")/$name" "$target/openssh/"; done
find "$(dirname "$ssh")" -maxdepth 1 -type f \( -iname '*.dll' -o -iname '*license*' -o -iname '*notice*' \) -exec cp {} "$target/openssh/" \;
cp "$gh" "$target/github/"
find "$work/gh" -type f \( -iname '*license*' -o -iname '*notice*' \) -exec cp {} "$target/github/" \;
# Keep upstream license documents even when the archive does not include them.
curl --fail --location --proto '=https' https://raw.githubusercontent.com/PowerShell/openssh-portable/v10.4.0.0/LICENCE -o "$target/openssh/UPSTREAM-LICENCE.txt"
curl --fail --location --proto '=https' https://raw.githubusercontent.com/cli/cli/v2.106.0/LICENSE -o "$target/github/UPSTREAM-LICENSE.txt"
