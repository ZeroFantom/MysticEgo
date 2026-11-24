#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
read -r ip host < orchestra-host.txt
if [[ ! $ip =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ || ! $host =~ ^[a-z0-9-]+(\.[a-z0-9-]+)+$ ]]; then
  echo 'Некорректная запись Orchestra в orchestra-host.txt.' >&2
  exit 1
fi
inspect() {
  awk -v host="$host" -v ip="$ip" '
    { sub(/#.*/, ""); for (i=2; i<=NF; i++) if (tolower($i)==host) {
        if ($1==ip) found=1; else conflict=1
      }
    }
    END { print conflict ? "conflict" : (found ? "ok" : "missing") }
  ' /etc/hosts
}
case "$(inspect)" in
  ok) exit 0 ;;
  conflict) echo "В /etc/hosts есть другой IP для $host. Сверьте его с IP VPS и исправьте запись." >&2; exit 1 ;;
esac
echo "Добавляем $host → $ip в /etc/hosts. Для первого запуска нужен sudo."
printf '\n%s %s # MysticEgo CloudDesk\n' "$ip" "$host" | sudo tee -a /etc/hosts >/dev/null
[[ $(inspect) == ok ]]
