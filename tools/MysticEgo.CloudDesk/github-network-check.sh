#!/usr/bin/env bash
# Read-only diagnostic. No authentication header, token output, or configuration changes.
set -u
umask 077
report="$PWD/github-network-report-$(date +%Y%m%d-%H%M%S).txt"
{
  echo 'CloudDesk GitHub network diagnostic (no credentials)'
  date -Is
  command -v gh >/dev/null && gh --version | head -n1
  for key in HTTPS_PROXY HTTP_PROXY ALL_PROXY NO_PROXY https_proxy http_proxy all_proxy no_proxy; do
    [[ -v $key ]] && printf '%s=set (value hidden)\n' "$key"
  done
  if command -v getent >/dev/null; then
    echo 'DNS:'; timeout 8 getent ahosts api.github.com | awk '!seen[$1]++{print $1}'
  fi
  if command -v curl >/dev/null; then
    for family in default ipv4 ipv6; do
      flags=(); [[ $family == ipv4 ]] && flags=(-4); [[ $family == ipv6 ]] && flags=(-6)
      echo "--- $family ---"
      # -q prevents local curlrc from introducing credentials or disabling TLS checks.
      curl -q "${flags[@]}" --silent --show-error --connect-timeout 7 --max-time 12 --output /dev/null --write-out 'HTTP=%{http_code} IP=%{remote_ip} DNS=%{time_namelookup}s TCP=%{time_connect}s TLS=%{time_appconnect}s TOTAL=%{time_total}s\n' https://api.github.com/user 2>&1
      echo "curl exit=$? (HTTP 401 without credentials is expected)"
    done
  else echo 'curl not installed'; fi
} > "$report" 2>&1
cat "$report"
printf '\nОтчёт: %s\n' "$report"
