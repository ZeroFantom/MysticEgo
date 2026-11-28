#!/usr/bin/env bash
# Factory reset is intentionally separate from update/repair. No generic paths or commands.
set -Eeuo pipefail
umask 077
mode=$1
domain=$2
expected=$3
[[ $mode == check || $mode == execute ]]
[[ $domain =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*\.[a-zA-Z]{2,63}$ && $domain != *..* ]]
[[ $expected =~ ^[a-f0-9-]{36}$ ]]
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
# Refuse a busy local provisioning job before any package/service mutation.
exec 8>/run/lock/mysticego-infrastructure.lock
flock -n 8 || die 'infrastructure setup busy; no deletion performed'
apt-get update
apt-get install -y ca-certificates caddy curl libicu74 libssl3t64 unzip gh libatomic1 libgomp1
preflight
echo CLOUDDESK_RESET_STEP:stop
for u in "${units[@]}"; do
 if [[ $u == *@.service ]]; then continue; fi
 if [[ $(systemctl show "$u" -p LoadState --value) != not-found ]]; then
   systemctl stop "$u"
   # is-enabled returns zero for some static/indirect units too. Only remove
   # installation symlinks for explicitly enabled units; activation helpers are stopped above.
   case "$(systemctl is-enabled "$u" 2>/dev/null || true)" in
     enabled) systemctl disable "$u" ;;
     enabled-runtime) systemctl disable --runtime "$u" ;;
   esac
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
flock -u 8
echo CLOUDDESK_RESET_STEP:install
# The standard installer borrows the deployment lock; compatible recovery keeps it held.
MYSTICEGO_INSTALL_LOCK_FD=9 MYSTICEGO_INSTALL_PREREQS_READY=1 bash install.sh "$PWD/r3.zip" "$domain" '' "$digest" native sqlite
# Installer readiness is not factory-reset completion. Never repeat deletion here.
echo CLOUDDESK_RESET_STEP:verify-identity
new=$(health http://127.0.0.1:5500/health/live) || die 'post-install local health check failed; do not repeat reset'
[[ $new =~ ^[a-f0-9-]{36}$ && $new != "$expected" ]] || die 'new local identity not verified; do not repeat reset'
public=$(health "https://$domain/health/live") || die 'post-install HTTPS health check failed; do not repeat reset'
[[ $public == "$new" ]] || die 'local and HTTPS identities differ; do not repeat reset'
echo CLOUDDESK_RESET_STEP:verify-storage
# Health may precede initialization of the local SQLite stores. Allow a bounded
# wait, then name the missing invariant instead of returning a silent exit 1.
# A fresh VPS report showed node.db appearing almost five minutes after auth.db.
# Wait up to six minutes, without requiring a user login or repeating deletion.
for ((i=0;i<180;i++)); do
 [[ -f /var/lib/mysticego/auth.db && -f /var/lib/mysticego/orchestra/node.db ]] && break
 sleep 2
done
[[ -f /var/lib/mysticego/auth.db ]] || die 'post-install Auth SQLite store missing; do not repeat reset'
[[ -f /var/lib/mysticego/orchestra/node.db ]] || die 'post-install Orchestra SQLite store missing; do not repeat reset'
[[ $(health http://127.0.0.1:5500/health/live) == "$new" && $(health "https://$domain/health/live") == "$new" ]] || die 'identity changed during storage verification; do not repeat reset'
echo CLOUDDESK_RESET_STEP:complete
echo "FACTORY_RESET_OK $new"
