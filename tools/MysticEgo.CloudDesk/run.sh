#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

if (( EUID == 0 )); then
  echo 'Запускайте CloudDesk от обычного пользователя.' >&2
  exit 1
fi

runtime="${CLOUDDESK_RUNTIME_DIR:-$(cd ../.. && pwd -P)/runtime}"
if [[ ! -x $runtime/electron-linux/electron && -z ${CLOUDDESK_RUNTIME_DIR:-} && -d ../MysticEgo/runtime ]]; then
  runtime="$(cd ../MysticEgo/runtime && pwd -P)"
fi
electron="$runtime/electron-linux/electron"
if [[ $(uname -m) != x86_64 || ! -x $electron ]]; then
  echo "Для CloudDesk нужен комплектный Linux x64 Electron: $electron" >&2
  exit 1
fi

scratch=''
stage=''
cleanup() {
  if [[ -n $scratch ]]; then rm -f -- "$scratch"; fi
  if [[ -n $stage ]]; then rm -rf -- "$stage"; fi
}
trap cleanup EXIT

has_dotnet() {
  [[ -x $1 ]] && "$1" --list-runtimes | grep -E '^Microsoft\.NETCore\.App 10\.' >/dev/null
}

if has_dotnet "$runtime/linux-x64/dotnet"; then
  export CLOUDDESK_DOTNET="$runtime/linux-x64/dotnet"
elif has_dotnet "$(command -v dotnet || true)"; then
  export CLOUDDESK_DOTNET=$(command -v dotnet)
elif has_dotnet "$HOME/.dotnet/dotnet"; then
  export CLOUDDESK_DOTNET="$HOME/.dotnet/dotnet"
else
  archive="$runtime/linux-x64.tar.gz"
  if [[ ! -f $archive ]]; then
    echo 'Не найден .NET 10. Распакуйте полный архив MysticEgo R3.1.' >&2
    exit 1
  fi
  release=$(sha256sum < "$archive")
  cache="${XDG_CACHE_HOME:-$HOME/.cache}/mysticego/dotnet"
  target="$cache/${release%% *}"
  if ! has_dotnet "$target/dotnet"; then
    mkdir -p -- "$cache"
    stage=$(mktemp -d "$cache/.extract.XXXXXXXX")
    tar -xzf "$archive" -C "$stage"
    has_dotnet "$stage/dotnet"
    if ! mv -T -- "$stage" "$target" && ! has_dotnet "$target/dotnet"; then exit 1; fi
  fi
  export CLOUDDESK_DOTNET="$target/dotnet"
fi

if [[ ${1:-} == --check ]]; then
  "$CLOUDDESK_DOTNET" out/CloudDesk.dll </dev/null
  "$electron" --disable-setuid-sandbox --version
  exit
fi

install_apparmor_profile() {
  case "$electron" in
    *[\*\?\[\]\{\}\\\"]*|*$'\n'*|*$'\r'*)
      echo 'Путь к Electron содержит специальные символы. Переместите MysticEgo в каталог с обычным именем.' >&2
      return 1 ;;
  esac
  local answer digest name profile_path
  if ! command -v sudo >/dev/null; then
    echo 'Для настройки AppArmor нужен sudo.' >&2
    return 1
  fi
  printf 'Разрешить AppArmor запуск песочницы для %s? Требуется sudo один раз [y/N] ' "$electron" >&2
  read -r answer </dev/tty || return 1
  case "$answer" in y|Y|д|Д) ;; *) return 1 ;; esac

  digest=$(printf '%s' "$electron" | sha256sum)
  name="mysticego-clouddesk-${digest:0:20}"
  profile_path="/etc/apparmor.d/$name"
  printf 'abi <abi/4.0>,\ninclude <tunables/global>\n\nprofile %s "%s" flags=(unconfined) {\n  userns,\n}\n' "$name" "$electron" |
    sudo tee "$profile_path" >/dev/null &&
    sudo apparmor_parser -r "$profile_path"
}

ulimit -c 0 || true
scratch=$(mktemp)
if "$electron" --disable-setuid-sandbox . 2>"$scratch"; then
  cat "$scratch" >&2
  exit 0
else
  status=$?
fi
cat "$scratch" >&2
if [[ $(cat /proc/sys/kernel/apparmor_restrict_unprivileged_userns 2>/dev/null || true) == 1 ]] &&
    grep -Eq 'No usable sandbox|Failed to move to new namespace' "$scratch"; then
  if install_apparmor_profile; then
    "$electron" --disable-setuid-sandbox .
    exit
  fi
fi
exit "$status"
