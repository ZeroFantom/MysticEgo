#!/usr/bin/env bash
# Factory reset is intentionally separate from update/repair. No generic paths or commands.
set -Eeuo pipefail
umask 077
mode=$1
domain=$2
expected=$3
[[ $mode == check || $mode == execute || $mode == verify ]]
[[ $domain =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*\.[a-zA-Z]{2,63}$ && $domain != *..* ]]
[[ $expected =~ ^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$ ]]
[[ $(id -u) == 0 && $(uname -m) == x86_64 ]]
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 ]]
paths=(/opt/mysticego /etc/mysticego /var/lib/mysticego /var/lib/mysticego-orchestra-updater /opt/mysticego-infrastructure /var/lib/mysticego-infrastructure-status /opt/mysticego-voice /var/lib/mysticego-voice /var/lib/mysticego-lm /usr/local/libexec/mysticego)
units=(mysticego-orchestra-updater.timer mysticego-maintenance-rescue.path mysticego-infrastructure-reconcile.timer mysticego-local-setup.socket mysticego-orchestra-updater.service mysticego-maintenance-rescue.service mysticego-infrastructure-reconcile.service mysticego-default-model.service mysticego-voice.service mysticego-lmstudio.service mysticego-orchestra.service mysticego-local-setup@.service)
die(){ echo "Factory reset refused: $*" >&2; exit 1; }
health(){ curl -fsS --max-time 5 "$1" | python3 -c 'import json,sys; h=json.load(sys.stdin); assert h["status"]=="alive" and h["kind"].lower()=="orchestra"; print(h["node"])'; }
check_paths(){
 python3 - "${paths[@]}" /etc/caddy/Caddyfile <<'PY'
import pathlib,sys
paths=[pathlib.Path(p) for p in sys.argv[1:]]
mounts=[line.split()[4] for line in pathlib.Path('/proc/self/mountinfo').read_text().splitlines()]
for p in paths:
    if p.resolve()!=p or p.is_symlink(): raise SystemExit("Unexpected symbolic link: "+str(p))
    if any(m==str(p) or m.startswith(str(p)+"/") for m in mounts): raise SystemExit("Mounted storage must be handled manually: "+str(p))
PY
}
preflight(){
 [[ $(health http://127.0.0.1:5500/health/live) == "$expected" ]] || die 'wrong node'
 [[ $(health "https://$domain/health/live") == "$expected" ]] || die 'wrong HTTPS node'
 [[ -d /opt/mysticego/app && -f /var/lib/mysticego/auth.db && -f /var/lib/mysticego/orchestra/node.db ]] || die 'standard local SQLite data not found'
 # Refuse anything except the native single-site profile. Never drop external DBs.
 [[ ! -e /etc/mysticego/database-staging && ! -e /opt/mysticego-infrastructure/postgres && ! -e /etc/postgresql/16/mysticego ]] || die 'PostgreSQL configuration present'
 local connections
 connections=$(find /var/lib/mysticego -maxdepth 2 -name '*.connection' -print -quit)
 [[ -z $connections ]] || die 'external database connection present'
 [[ ! -e /opt/mysticego-infrastructure/monitoring ]] || die 'managed monitoring requires separate cleanup'
 cmp -s /etc/mysticego/orchestra.env <(printf 'MYSTICEGO_CENTER_AUTH_URL=https://%s\nMYSTICEGO_CENTER_ORCHESTRA_URL=https://%s\n' "$domain" "$domain") || die 'custom environment'
 [[ $(systemctl show mysticego-orchestra.service -p FragmentPath --value) == /etc/systemd/system/mysticego-orchestra.service ]] || die 'unknown service path'
 [[ -z $(systemctl show mysticego-orchestra.service -p DropInPaths --value) ]] || die 'custom or Docker installation'
 grep -Fxq 'ExecStart=/opt/mysticego/app/mysticego --headless --no-lm --output /var/lib/mysticego' /etc/systemd/system/mysticego-orchestra.service || die 'unknown service command'
 cmp -s /etc/caddy/Caddyfile <(cat <<CADDY
$domain {
  @token {
    method POST
    path /api/identities/token
  }
  handle @token {
    reverse_proxy 127.0.0.1:5400
  }
  handle {
    reverse_proxy 127.0.0.1:5500
  }
}
CADDY
 ) || die 'custom/shared Caddy configuration'
 check_paths
 local u allowed known installed_units active_setups
 installed_units=$(systemctl list-unit-files 'mysticego-*' --no-legend --plain)
 while read -r u _; do
   [[ -z $u ]] && continue
   known=false
   for allowed in "${units[@]}"; do [[ $u == "$allowed" ]] && known=true; done
   [[ $known == true ]] || die "unknown MysticEgo unit: $u"
 done <<< "$installed_units"
 # No pending privileged setup instances.
 active_setups=$(systemctl list-units 'mysticego-local-setup@*.service' --state=active,activating,deactivating --no-legend --plain)
 [[ -z $active_setups ]] || die 'local setup busy'
}
# Read-only post-install verification. No cleanup or second destructive attempt.
verify_postinstall(){
 local wanted=$1 state_root=$2 app_root=$3 new='' remote='' installed='' i
 echo CLOUDDESK_RESET_STEP:verify
 for ((i=0;i<12;i++)); do
   new=$(health http://127.0.0.1:5500/health/live 2>/dev/null) || new=''
   remote=$(health "https://$domain/health/live" 2>/dev/null) || remote=''
   if [[ $new =~ ^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$ && $new != "$expected" && $remote == "$new" ]]; then break; fi
   sleep 2
 done
 [[ $new =~ ^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$ ]] || die 'post-install: local Orchestra health or node ID unavailable'
 [[ $new != 00000000-0000-0000-0000-000000000000 ]] || die 'post-install: empty node ID'
 [[ $new != "$expected" ]] || die 'post-install: old node identity still returned'
 [[ $remote == "$new" ]] || die 'post-install: HTTPS and local node identities differ'
 [[ -s "$state_root/center-id.txt" ]] || die 'post-install: center-id.txt missing'
 [[ $(cat "$state_root/center-id.txt") == "$new" ]] || die 'post-install: persisted center identity differs'
 [[ -s "$state_root/auth.db" ]] || die 'post-install: Auth SQLite file missing or empty'
 [[ -s "$state_root/orchestra/node.db" ]] || die 'post-install: Orchestra SQLite file missing or empty'
 installed=$(python3 - "$app_root/runtime/app/web/orchestra/release-info.json" <<'PY'
import json,sys
print(json.load(open(sys.argv[1]))["installed"])
PY
 ) || die 'post-install: installed release metadata unavailable'
 [[ $installed == "$wanted" ]] || die 'post-install: installed release differs from approved release'
 systemctl is-active --quiet mysticego-orchestra.service || die 'post-install: Orchestra service is not active'
 systemctl is-active --quiet caddy.service || die 'post-install: Caddy service is not active'
 systemctl is-active --quiet mysticego-orchestra-updater.timer || die 'post-install: updater timer is not active'
 systemctl is-active --quiet mysticego-local-setup.socket || die 'post-install: local setup socket is not active'
 echo "FACTORY_RESET_OK $new"
}
if [[ $mode == verify ]]; then
 [[ ${4:-} =~ ^v1\.0\.0-beta\.7-r[0-9]+\.[0-9]+\.[0-9]+$ ]] || die 'invalid verification release'
 verify_postinstall "$4" /var/lib/mysticego /opt/mysticego/app
 exit 0
fi
if [[ $mode == check ]]; then
 preflight
 echo FACTORY_RESET_PREFLIGHT_OK
 exit 0
fi
tag=$4
digest=$5
[[ $tag =~ ^v1\.0\.0-beta\.7-r[0-9]+\.[0-9]+\.[0-9]+$ && $digest =~ ^[a-f0-9]{64}$ ]]
test -f r3.zip && test -f install.sh
printf '%s  r3.zip\n' "$digest" | sha256sum -c -
bash -n install.sh
unzip -tq r3.zip >/dev/null
python3 - "$tag" <<'PY'
import json,sys,zipfile
with zipfile.ZipFile("r3.zip") as z:
    for name in ("MysticEgo/mysticego","MysticEgo/runtime/linux-x64.tar.gz","MysticEgo/runtime/app/web/orchestra/release-info.json"):
        z.getinfo(name)
    assert json.loads(z.read("MysticEgo/runtime/app/web/orchestra/release-info.json"))["installed"]==sys.argv[1], "Release mismatch"
PY
# Verify package prerequisites before deletion; a network failure must not wipe a working node.
exec 9>/run/lock/mysticego-clouddesk-deploy.lock
flock -n 9 || die 'deployment busy'
preflight
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates caddy curl libicu74 libssl3t64 unzip gh
preflight
echo CLOUDDESK_RESET_STEP:stop
for u in "${units[@]}"; do
 if [[ $u == *@.service ]]; then continue; fi
 if [[ $(systemctl show "$u" -p LoadState --value) != not-found ]]; then
   systemctl stop "$u"
   enabled_state=$(systemctl is-enabled "$u" 2>/dev/null || true)
   case "$enabled_state" in enabled|enabled-runtime|linked|linked-runtime) systemctl disable "$u";; esac
 fi
done
active_setups=$(systemctl list-units 'mysticego-local-setup@*.service' --state=active,activating,deactivating --no-legend --plain)
[[ -z $active_setups ]] || die 'local setup busy after stopping socket'
systemctl stop caddy.service
check_paths
# Only the fixed, confirmed application-owned directories and unit definitions are removed.
echo CLOUDDESK_RESET_STEP:delete
for u in "${units[@]}"; do rm -f -- "/etc/systemd/system/$u"; done
rm -f -- /etc/caddy/Caddyfile
rm -rf --one-file-system -- "${paths[@]}"
systemctl daemon-reload
echo CLOUDDESK_RESET_STEP:install
# The standard installer now holds the inherited lock until it reaches maintenance recovery.
MYSTICEGO_INSTALL_LOCK_FD=9 MYSTICEGO_INSTALL_PREREQS_READY=1 bash install.sh "$PWD/r3.zip" "$domain" '' "$digest" native sqlite || die 'post-install: installer returned a failure; verify existing state before any retry'
verify_postinstall "$tag" /var/lib/mysticego /opt/mysticego/app
