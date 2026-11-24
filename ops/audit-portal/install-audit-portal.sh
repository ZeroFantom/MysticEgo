#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
sudo bash install-audit-portal.sh --same-orchestra ORCHESTRA_NODE_ID
sudo bash install-audit-portal.sh --site audit.example.org orchestra.example.org ORCHESTRA_NODE_ID
bash install-audit-portal.sh --check
USAGE
}

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
bundle="$here/web"
verify_bundle() {
  python3 - "$bundle" <<'PY'
from pathlib import Path
import re,sys
root=Path(sys.argv[1])
page=(root/'index.html').read_text()
assert 'data-role="audit"' in page, 'Expected the Audit frontend'
assets=re.findall(r'["\x27](/assets/[^"\x27]+)',page)
assert assets and all((root/path.lstrip('/')).is_file() for path in assets),'Referenced assets missing'
assert (root/'mystic-config.js').is_file(),'Runtime configuration missing'
PY
}
domain_ok() {
  [[ $1 =~ ^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{1,62}$ && ${#1} -le 253 ]]
}

if [[ ${1:-} == --check && $# == 1 ]]; then verify_bundle; echo 'Пакет Audit проверен.'; exit 0; fi
if [[ ${1:-} == --help || $# == 0 ]]; then usage; exit 0; fi
case "$1:$#" in
  --same-orchestra:2|--site:4) ;;
  *) usage >&2; exit 2;;
esac
if (( EUID != 0 )); then echo 'Запустите установку через sudo на нужном VPS.' >&2; exit 1; fi
node=${!#}
if [[ ! $node =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  echo 'Не указан корректный ID Orchestra.' >&2
  exit 2
fi
check_orchestra() {
  curl -fsS --max-time 12 "$1/health/live" |
    python3 -c 'import json,sys; h=json.load(sys.stdin); sys.exit(0 if h.get("status")=="alive" and h.get("kind")=="Orchestra" and h.get("node","").lower()==sys.argv[1].lower() else "Health не подтвердил выбранную Orchestra.")' "$node"
}
verify_bundle
exec 9>/run/lock/mysticego-audit-portal.lock
flock -n 9

if [[ $1 == --same-orchestra ]]; then
  echo 'Проверяем службу и ID локальной Orchestra'
  systemctl is-active --quiet mysticego-orchestra.service
  check_orchestra http://127.0.0.1:5500
  curl -fsS --max-time 5 http://127.0.0.1:5500/audit-portal/ | grep 'data-role="audit"' >/dev/null
  root=/opt/mysticego/app/runtime/app/web
  audit="$root/audit"
  static="$root/orchestra/assets"
  test -f "$audit/index.html" && test -d "$static"
  echo 'Устанавливаем веб-ресурсы Audit'
  # Audit uses absolute /assets URLs served by Orchestra.
  for file in "$bundle"/assets/*; do
    target="$static/${file##*/}"
    if [[ -f $target ]]; then
      cmp -s "$file" "$target" || { echo "Существующий ресурс отличается: $target" >&2; exit 1; }
    else
      asset_stage=$(mktemp "$static/.audit-asset.XXXXXXXX")
      install -m 0644 -- "$file" "$asset_stage"
      mv -f -- "$asset_stage" "$target"
    fi
  done
  backup=$(mktemp -d /opt/mysticego/.audit-portal-backup.XXXXXXXX)
  cp -p -- "$audit/index.html" "$backup/index.html"
  staging=$(mktemp "$audit/.index.XXXXXXXX")
  install -m 0644 -- "$bundle/index.html" "$staging"
  mv -f -- "$staging" "$audit/index.html"
  if ! curl -fsS --max-time 5 http://127.0.0.1:5500/audit-portal/ | cmp -s "$bundle/index.html" -; then
    cp -p -- "$backup/index.html" "$staging"
    mv -f -- "$staging" "$audit/index.html"
    echo 'Проверка портала не прошла. Страница восстановлена.' >&2
    exit 1
  fi
  echo "Портал /audit-portal/ обновлён. Резервная копия: $backup"
  exit 0
fi

site=$2
central=$3
if ! domain_ok "$site" || ! domain_ok "$central" || [[ $site == "$central" ]]; then
  echo 'Укажите два разных имени DNS в нижнем регистре, без https:// и путей.' >&2
  exit 2
fi
echo 'Проверяем доступ VPS к Auth и Orchestra по HTTPS'
check_orchestra "https://$central"
command -v caddy >/dev/null || { echo 'Установите Caddy пакетным менеджером VPS.' >&2; exit 1; }
systemctl is-active --quiet caddy.service || { echo 'Запустите службу caddy.service.' >&2; exit 1; }
caddyfile=/etc/caddy/Caddyfile
fragment="/etc/caddy/mysticego-audit-$site.caddy"
test -f "$caddyfile" && test ! -L "$caddyfile" && test ! -L "$fragment"
root=/var/www/mysticego-audit-portal
install -d -m 0755 "$root"
version=$(mktemp -d "$root/site.XXXXXXXX")
chmod 0755 "$version"
cp -R -- "$bundle/." "$version/"
chmod -R a+rX,go-w "$version"
backup=$(mktemp -d /etc/caddy/.audit-portal-backup.XXXXXXXX)
cp -p -- "$caddyfile" "$backup/Caddyfile"
if [[ -e $fragment ]]; then cp -p -- "$fragment" "$backup/fragment"; fi

restore() {
  local status=$?
  trap - ERR
  cp -p -- "$backup/Caddyfile" "$caddyfile"
  if [[ -f "$backup/fragment" ]]; then cp -p -- "$backup/fragment" "$fragment"; else rm -f -- "$fragment"; fi
  systemctl reload caddy.service || true
  echo "Настройки Caddy восстановлены из $backup" >&2
  exit "$status"
}
trap restore ERR
temp=$(mktemp /etc/caddy/.audit-site.XXXXXXXX)
cat > "$temp" <<CADDY
$site {
    root * $version
    encode zstd gzip

    @auth path /api/auth/audit/register /api/auth/login /api/auth/refresh /api/auth/logout
    handle @auth {
        reverse_proxy https://$central {
            header_up Host $central
        }
    }
    handle /api/v1/* {
        reverse_proxy https://$central {
            header_up Host $central
        }
    }
    handle /api/* {
        respond 404
    }
    handle /assets/* {
        file_server
    }
    handle /audit-portal/* {
        try_files /index.html
        file_server
    }
    handle {
        @root path /
        redir @root /audit-portal/ 302
        file_server
    }
}
CADDY
chmod 0644 "$temp"
mv -f -- "$temp" "$fragment"
line="import $fragment"
if ! grep -Fxq "$line" "$caddyfile"; then printf '\n%s\n' "$line" >> "$caddyfile"; fi
echo 'Проверяем конфигурацию и перечитываем Caddy'
caddy validate --config "$caddyfile" --adapter caddyfile 2>&1
systemctl reload caddy.service
trap - ERR
if curl -fsS --retry 4 --retry-delay 4 --retry-all-errors --max-time 5 "https://$site/audit-portal/" 2>/dev/null |
    cmp -s "$version/index.html" -; then
  echo 'Портал отвечает по HTTPS.'
else
  echo 'Caddy применил настройки. HTTPS ещё не подтверждён. Проверьте DNS, порты 80/443 и журнал Caddy.'
fi
echo "Портал настроен: https://$site/audit-portal/"
echo "Резервная копия настроек: $backup"
