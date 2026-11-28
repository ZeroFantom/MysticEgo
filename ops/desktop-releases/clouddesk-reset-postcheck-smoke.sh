#!/usr/bin/env bash
# Run ONLY the extracted read-only verifier with fixture data and fake services.
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
fixture=$(mktemp -d)
trap 'rm -rf -- "$fixture"' EXIT
script="$root/tools/MysticEgo.CloudDesk/profiles/orchestra-reset.sh"
sed -n '/^verify_postinstall(){/,/^}$/p' "$script" > "$fixture/verifier.sh"
bash -n "$fixture/verifier.sh"
source "$fixture/verifier.sh"
domain=example.invalid
expected=11111111-1111-4111-8111-111111111111
fresh=22222222-2222-4222-8222-222222222222
tag=v1.0.0-beta.7-r3.2.42
state="$fixture/state"
app="$fixture/app"
mkdir -p "$state/orchestra" "$app/runtime/app/web/orchestra"
printf '%s\n' "$fresh" > "$state/center-id.txt"
printf 'fixture' > "$state/auth.db"
printf 'fixture' > "$state/orchestra/node.db"
printf '{"installed":"%s"}\n' "$tag" > "$app/runtime/app/web/orchestra/release-info.json"
die(){ echo "$*" >&2; exit 1; }
health(){ if [[ $1 == http://127.0.0.1:* ]]; then echo "${local_id:-$fresh}"; else echo "${remote_id:-$fresh}"; fi; }
systemctl(){ [[ $* == 'is-active --quiet '* ]] && [[ ${failed_unit:-} != "$3" ]]; }
sleep(){ :; }
output=$(verify_postinstall "$tag" "$state" "$app")
grep -Fxq "FACTORY_RESET_OK $fresh" <<< "$output"
reject(){
 local reason=$1
 shift
 local log
 if log=$("$@" 2>&1); then echo "Unexpected success: $reason" >&2; exit 1; fi
 grep -Fq "$reason" <<< "$log"
}
# Each negative test runs in its own subshell; no real systemctl or network.
check_old(){ local local_id=$expected remote_id=$expected; verify_postinstall "$tag" "$state" "$app"; }
check_mismatch(){ local remote_id=$expected; verify_postinstall "$tag" "$state" "$app"; }
check_service(){ local failed_unit=caddy.service; verify_postinstall "$tag" "$state" "$app"; }
check_pending(){ local failed_unit=mysticego-local-setup.socket; verify_postinstall "$tag" "$state" "$app"; }
check_persisted(){ printf '%s\n' "$expected" > "$state/center-id.txt"; verify_postinstall "$tag" "$state" "$app"; }
reject 'old node identity still returned' check_old
reject 'HTTPS and local node identities differ' check_mismatch
reject 'installed release differs from approved release' verify_postinstall v1.0.0-beta.7-r3.2.43 "$state" "$app"
reject 'Caddy service is not active' check_service
pending=$(check_pending)
grep -Fxq "FACTORY_RESET_OK $fresh" <<< "$pending"
grep -Fxq CLOUDDESK_RESET_MAINTENANCE_PENDING <<< "$pending"
reject 'persisted center identity differs' check_persisted
printf '%s\n' "$fresh" > "$state/center-id.txt"
rm "$state/auth.db"
reject 'Auth SQLite store missing or empty' verify_postinstall "$tag" "$state" "$app"
echo 'PASS factory reset read-only postconditions: healthy, old identity, HTTPS mismatch, release mismatch, inactive service, missing SQLite'
