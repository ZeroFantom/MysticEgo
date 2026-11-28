#!/usr/bin/env bash
# Only the existing, trusted Orchestra installation is used. No app replacement.
set -Eeuo pipefail
stage='preflight'
trap 'printf "Maintenance recovery failed: stage=%s line=%s exit=%s\\n" "$stage" "$LINENO" "$?" >&2' ERR
umask 077
[[ $(id -u) == 0 ]]
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 ]]
root=/opt/mysticego/app
expected=${1:?expected Orchestra node ID}
[[ $expected =~ ^[a-fA-F0-9-]{36}$ ]]
health() {
  curl -fsS --max-time 5 http://127.0.0.1:5500/health/live |
    python3 -c 'import json,sys; h=json.load(sys.stdin); assert h["status"]=="alive" and h["kind"].lower()=="orchestra"; print(h["node"])'
}
[[ $(health) == "$expected" ]] || { echo 'Wrong Orchestra node.' >&2; exit 1; }
# Share the deployment lock with the privileged updater.
stage='deployment-lock'
inherited=false
if [[ ${2:-} == --inherited-deploy-lock ]]; then
  [[ $(readlink "/proc/$/fd/9") == /run/lock/mysticego-clouddesk-deploy.lock ]]
  flock -n 9
  inherited=true
else
  exec 9>/run/lock/mysticego-clouddesk-deploy.lock
  flock -w 120 9
fi
[[ $(health) == "$expected" ]]
systemctl is-active --quiet mysticego-orchestra.service
stage='trusted-release-validation'
tag=$(python3 - "$root" <<'PY'
import json, pathlib, stat, sys, re
root=pathlib.Path(sys.argv[1])
paths=["runtime/app/web/orchestra/release-info.json", "ops/orchestra-updater/install-worker.sh",
       "ops/orchestra-updater/update-orchestra.py", "ops/orchestra-updater/mysticego-orchestra-updater.service",
       "ops/orchestra-updater/mysticego-orchestra-updater.timer"]
paths += ["ops/infrastructure/"+name for name in ("install-local.sh","install.sh","install.py","local-runner.py","reconcile.py","install-voice.sh","voice-server.py")]
# R3.2.37 is recoverable with the CloudDesk script; newer releases also
# include a separate fixed-command recovery service.
extra=["ops/orchestra-updater/"+name for name in ("recover-maintenance.sh","rescue-request.py","mysticego-maintenance-rescue.path","mysticego-maintenance-rescue.service")]
if any((root/name).exists() for name in extra):
    paths += extra
for name in paths:
    path=root/name
    for part in (path,*path.parents):
        info=part.lstat()
        if info.st_uid != 0 or info.st_mode & 0o022 or stat.S_ISLNK(info.st_mode):
            raise SystemExit("Untrusted installation path")
        if part==path and not stat.S_ISREG(info.st_mode):
            raise SystemExit("Missing regular release file")
tag=json.loads((root/paths[0]).read_text())["installed"]
match=re.fullmatch(r"v1\.0\.0-beta\.7-r(\d+)\.(\d+)\.(\d+)",tag)
if not match or tuple(map(int,match.groups())) < (3,2,37):
    raise SystemExit("Install Orchestra R3.2.37 or later first")
print(tag)
PY
)
stage='updater-service'
echo 'Installing privileged update service from the existing release.'
command -v gh >/dev/null || { apt-get update; apt-get install -y gh; }
# install-worker schedules the service; it may see the held deployment lock.
bash "$root/ops/orchestra-updater/install-worker.sh" "$tag"
stage='maintenance-service'
echo 'Installing Orchestra Maintenance Service.'
bash "$root/ops/infrastructure/install-local.sh"
stage='service-verification'
systemctl is-enabled --quiet mysticego-orchestra-updater.timer
systemctl is-active --quiet mysticego-orchestra-updater.timer
systemctl is-enabled --quiet mysticego-infrastructure-reconcile.timer
systemctl is-active --quiet mysticego-infrastructure-reconcile.timer
systemctl is-active --quiet mysticego-local-setup.socket
[[ -S /var/lib/mysticego/local-setup.sock ]]
stage='health-verification'
[[ $(health) == "$expected" ]]
# Release lock before triggering immediate status publication; the timer also retries.
if [[ $inherited == false ]]; then
  flock -u 9
  systemctl start --no-block mysticego-orchestra-updater.service
fi
echo 'MAINTENANCE_RECOVERY_OK'
