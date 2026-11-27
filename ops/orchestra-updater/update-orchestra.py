#!/usr/bin/env python3
"""Root-owned Orchestra update service. Only exact published candidates can be approved."""
import fcntl
import hashlib
import json
from datetime import datetime
import os
import pwd
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import tempfile
import time
import urllib.request
import zipfile

ROOT = Path("/opt/mysticego")
APP = ROOT / "app"
STATE = Path("/var/lib/mysticego-orchestra-updater")
INBOX = Path("/var/lib/mysticego/update-inbox")
REPO = "ZeroFantom/MysticEgo"
SERVICE = "mysticego-orchestra.service"
LIMIT = 2_000_000_000
PANEL_TOKEN = INBOX.parent / "orchestra" / "github-read-token"


class GitHubUnavailable(RuntimeError):
    pass


def panel_token():
    try:
        uid = pwd.getpwnam("mysticego").pw_uid
        path = PANEL_TOKEN
        directory = os.open(path.anchor, os.O_RDONLY | os.O_DIRECTORY)
        try:
            for part in path.parent.parts[1:]:
                child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=directory)
                os.close(directory)
                directory = child
            fd = os.open(path.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
            with os.fdopen(fd, "rb") as token_file:
                info = os.fstat(token_file.fileno())
                if stat.S_ISREG(info.st_mode) and info.st_uid == uid and info.st_nlink == 1 and not info.st_mode & 0o077:
                    token = token_file.read(301).strip()
                    if re.fullmatch(rb"[A-Za-z0-9_]{20,300}", token):
                        return token.decode("ascii")
        finally:
            os.close(directory)
    except (OSError, KeyError):
        pass
    return None


def github(*args, timeout=60):
    env = os.environ.copy()
    env["GH_HOST"] = "github.com"
    if not env.get("GH_TOKEN") and not env.get("GITHUB_TOKEN"):
        token = panel_token()
        if token:
            env["GH_TOKEN"] = token
    try:
        return subprocess.check_output(("gh", *args), timeout=timeout, stderr=subprocess.PIPE, env=env)
    except (OSError, subprocess.SubprocessError) as error:
        detail = getattr(error, "stderr", b"") or b""
        if any(hint in detail.lower() for hint in
               (b"http 401", b"http 403", b"http 404", b"auth login", b"bad credentials")):
            raise GitHubUnavailable("Проверьте токен GitHub с правом Contents: Read для ZeroFantom/MysticEgo.") from None
        raise GitHubUnavailable("Служба обновления не смогла обратиться к GitHub. Проверьте сеть VPS и журнал службы.") from None


def command(*args, timeout=60):
    return subprocess.check_output(args, text=True, timeout=timeout, stderr=subprocess.PIPE)


def write(name, data):
    temporary = STATE / (name + ".tmp")
    temporary.write_text(json.dumps(data), encoding="utf-8")
    temporary.replace(STATE / name)


WEB = Path("runtime/app/web")
AUDIT = APP / WEB / "audit"
PREVIOUS = {"orchestra": ROOT / ".orchestra-previous", "audit": ROOT / ".audit-previous"}
TAG = re.compile(r"v[0-9][A-Za-z0-9._-]{0,80}")
PROJECT_TAG = re.compile(r"v(\d+)\.(\d+)\.(\d+)-beta\.(\d+)-r(\d+(?:\.\d+)*)")


def panel_version(directory):
    try:
        value = json.loads((directory / "release-info.json").read_text()).get("installed", "")
        return value if TAG.fullmatch(value) else None
    except (OSError, ValueError, TypeError):
        return None


def versions():
    return panel_version(APP / WEB / "orchestra"), panel_version(AUDIT)


def status(phase, installed, candidate=None, targets=(), message=None, last_target=None):
    write("status.json", dict(phase=phase, current=installed[0], currentAudit=installed[1],
                             candidate=candidate, protocol=2, targets=list(targets),
                             message=message, lastTarget=last_target))


MAINTENANCE_FILES = ("install.sh", "install.py", "local-runner.py", "reconcile.py", "install-voice.sh", "voice-server.py", "record-oneshot.py")
RESCUE_FILES = ("install-worker.sh","update-orchestra.py","mysticego-orchestra-updater.service","mysticego-orchestra-updater.timer",
                "recover-maintenance.sh","rescue-request.py","mysticego-maintenance-rescue.path","mysticego-maintenance-rescue.service")
MAINTENANCE_HOME = Path("/opt/mysticego-infrastructure/local")
MAINTENANCE_SOCKET = INBOX.parent / "local-setup.sock"
MAINTENANCE_STATUS = Path("/var/lib/mysticego-infrastructure-status/status.json")


def trusted_file(path):
    # Never execute scripts from a user-writable application tree or symlink.
    try:
        for part in (path, *path.parents):
            info = part.lstat()
            if info.st_uid != 0 or info.st_mode & 0o022 or stat.S_ISLNK(info.st_mode):
                return False
            if part == path and not stat.S_ISREG(info.st_mode):
                return False
        return True
    except OSError:
        return False


def system_ok(*args):
    try:
        command("systemctl", *args, timeout=5)
        return True
    except (OSError, subprocess.SubprocessError):
        return False


def maintenance_ready(source):
    try:
        if not stat.S_ISSOCK(MAINTENANCE_SOCKET.lstat().st_mode):
            return False
        for name in MAINTENANCE_FILES:
            if not trusted_file(MAINTENANCE_HOME / name) or (source / name).read_bytes() != (MAINTENANCE_HOME / name).read_bytes():
                return False
        return all(system_ok(operation, "--quiet", unit)
                   for operation in ("is-active", "is-enabled")
                   for unit in ("mysticego-local-setup.socket", "mysticego-infrastructure-reconcile.timer"))
    except OSError:
        return False


def maintenance_state():
    try:
        return json.loads((STATE / "maintenance.json").read_text())
    except (OSError, ValueError):
        return {}


def maintenance_record(state, release, reason="", retry_after=0):
    write("maintenance.json", dict(state=state, release=release, reason=reason,
                                   checkedAt=datetime.now().astimezone().isoformat(), retryAfter=retry_after))


def ensure_rescue(release):
    source=APP/"ops/orchestra-updater"
    # A legacy package may not contain this channel. Do not execute partial trees.
    if not all(trusted_file(source/name) for name in RESCUE_FILES):
        return
    try:
        installed=Path("/usr/local/libexec/mysticego/maintenance-rescue.py")
        if (trusted_file(installed) and installed.read_bytes()==(source/"rescue-request.py").read_bytes()
                and system_ok("is-active","--quiet","mysticego-maintenance-rescue.path")
                and system_ok("is-enabled","--quiet","mysticego-maintenance-rescue.path")):
            return
        command("bash",str(source/"install-worker.sh"),release,timeout=90)
    except (OSError, subprocess.SubprocessError):
        print("Independent maintenance recovery channel is not ready.",flush=True)


def ensure_maintenance(release, force=False):
    source = APP / "ops/infrastructure"
    script = source / "install-local.sh"
    if not release or not all(trusted_file(source / name) for name in (*MAINTENANCE_FILES, "install-local.sh")):
        maintenance_record("unavailable", release, "trusted-package-incomplete")
        return False
    if maintenance_ready(source) and not force:
        # A running timer alone does not prove the reconciler is publishing.
        try:
            fresh = time.time() - MAINTENANCE_STATUS.stat().st_mtime < 3300
        except OSError:
            fresh = False
        if fresh:
            maintenance_record("ready", release)
            return True
        if system_ok("is-active", "--quiet", "mysticego-infrastructure-reconcile.service"):
            maintenance_record("waiting", release)
            return True
        # Kick a timer which has not yet published, without reinstalling software.
        prior = maintenance_state()
        if prior.get("release") == release and prior.get("retryAfter", 0) > time.time():
            return True
        started = system_ok("start", "--no-block", "mysticego-infrastructure-reconcile.service")
        maintenance_record("waiting" if started else "degraded", release,
                           "" if started else "service-start-failed", time.time()+120)
        return started
    prior = maintenance_state()
    if not force and prior.get("release") == release and prior.get("retryAfter", 0) > time.time():
        return False
    maintenance_record("repairing", release, retry_after=time.time()+120)
    try:
        # Fixed path, root-owned verified release only. Long installs run separately.
        command("bash", str(script), timeout=90)
        if not maintenance_ready(source):
            raise RuntimeError("maintenance service validation failed")
        maintenance_record("ready" if MAINTENANCE_STATUS.is_file() else "waiting", release)
        return True
    except (OSError, subprocess.SubprocessError, RuntimeError) as error:
        print("Orchestra Maintenance Service setup: " + type(error).__name__, flush=True)
        maintenance_record("degraded", release, type(error).__name__, time.time()+120)
        return False


def take_maintenance_request():
    # Distinct fixed request, no user-supplied command, path or arguments.
    try:
        directory = os.open(INBOX, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            fd = os.open("maintenance-repair.json", os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
            try:
                return stat.S_ISREG(os.fstat(fd).st_mode) and os.read(fd, 64) == b'{"confirmed":true}'
            finally:
                os.close(fd)
                os.unlink("maintenance-repair.json", dir_fd=directory)
        finally:
            os.close(directory)
    except OSError:
        return False


def project_version(tag):
    match = PROJECT_TAG.fullmatch(tag or "")
    if not match:
        return None
    release = tuple(map(int, match.group(5).split(".")))
    return (*(int(match.group(i)) for i in range(1, 5)), *(release + (0,) * 8)[:8])


def newer(tag, installed, publications):
    if not installed or tag == installed:
        return False
    right = project_version(installed)
    left = project_version(tag)
    if left is not None and right is not None:
        return left > right
    return installed in publications and publications[tag] > publications[installed]


def newest(installed):
    releases = json.loads(github("api", f"repos/{REPO}/releases?per_page=30"))
    publications = {r["tag_name"]: datetime.fromisoformat(r["published_at"])
                    for r in releases if not r["draft"] and r.get("published_at") and TAG.fullmatch(r["tag_name"])}
    for release in sorted(releases, key=lambda item: item.get("published_at") or "", reverse=True):
        tag = release["tag_name"]
        if release["draft"] or tag not in publications:
            continue
        targets = [target for target, version in zip(("orchestra", "audit"), installed)
                   if newer(tag, version, publications) or
                   (target == "audit" and version is None and (AUDIT / "index.html").is_file())]
        if not targets:
            continue
        for asset in release["assets"]:
            digest = asset.get("digest") or ""
            if (asset["name"] == "MysticEgo-Orchestra-linux-x64.zip" and asset["state"] == "uploaded"
                    and 0 < asset["size"] <= LIMIT and re.fullmatch(r"sha256:[a-f0-9]{64}", digest)):
                if len(targets) == 2:
                    targets.append("both")
                candidate = dict(tag=tag, digest=digest, asset=asset["name"], id=asset["id"], size=asset["size"])
                return candidate, targets
    return None, []


def take_request():
    directory = os.open(INBOX, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        try:
            fd = os.open("pending.json", os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
        except FileNotFoundError:
            return None
        try:
            if not stat.S_ISREG(os.fstat(fd).st_mode):
                return None
            data = os.read(fd, 4097)
            try:
                return json.loads(data) if len(data) <= 4096 else None
            except (UnicodeError, ValueError):
                return None
        finally:
            os.close(fd)
            os.unlink("pending.json", dir_fd=directory)
    finally:
        os.close(directory)


def unpack(package, destination):
    with zipfile.ZipFile(package) as archive:
        entries = archive.infolist()
        if len(entries) > 30000 or sum(item.file_size for item in entries) > 5 * LIMIT:
            raise ValueError("bundle limits exceeded")
        paths = set()
        for item in entries:
            name = PurePosixPath(item.filename)
            mode = item.external_attr >> 16
            if (not name.parts or name.parts[0] != "MysticEgo" or ".." in name.parts
                    or chr(92) in item.filename or stat.S_IFMT(mode) not in (0, stat.S_IFREG, stat.S_IFDIR)
                    or name in paths):
                raise ValueError("invalid bundle entry")
            paths.add(name)
            out = destination.joinpath(*name.parts)
            if item.is_dir():
                out.mkdir(parents=True, exist_ok=True)
            else:
                out.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(item) as source, out.open("xb") as target:
                    shutil.copyfileobj(source, target)
                out.chmod(0o755 if mode & 0o111 else 0o644)
    app = destination / "MysticEgo"
    if not os.access(app / "mysticego", os.X_OK) or not (app / "runtime/app/bin/Api.Orchestra.dll").is_file():
        raise ValueError("incomplete Orchestra bundle")
    # The launcher runs as mysticego and cannot extract into a root-owned app.
    runtime = app / "runtime/linux-x64/dotnet"
    if not os.access(runtime, os.X_OK):
        raise ValueError("bundle must include unpacked Linux .NET runtime")
    return app


def health():
    client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with client.open("http://127.0.0.1:5500/health/live", timeout=3) as response:
        payload = json.load(response)
        if payload.get("status") != "alive" or payload.get("kind", "").lower() != "orchestra":
            raise ValueError("unexpected health")
        return payload["node"]


def restore(component):
    previous = PREVIOUS[component]
    if not previous.exists():
        return
    live = APP if component == "orchestra" else AUDIT
    if component == "orchestra":
        command("systemctl", "stop", SERVICE)
    if live.exists():
        with tempfile.TemporaryDirectory(prefix=".mysticego-rejected-", dir=ROOT) as folder:
            live.rename(Path(folder) / "rejected")
            previous.rename(live)
    else:
        previous.rename(live)
    if component == "orchestra":
        command("systemctl", "start", SERVICE)


def web_assets(audit, orchestra):
    for source in (audit / "assets").iterdir():
        if not source.is_file():
            continue
        target = orchestra / "assets" / source.name
        if target.exists():
            if source.read_bytes() != target.read_bytes():
                raise ValueError("Audit asset conflicts with installed Orchestra")
        else:
            fd, name = tempfile.mkstemp(prefix=".audit-asset-", dir=target.parent)
            os.close(fd)
            temp = Path(name)
            try:
                shutil.copyfile(source, temp)
                temp.chmod(0o644)
                temp.replace(target)
            finally:
                temp.unlink(missing_ok=True)


def audit_health(expected):
    client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with client.open("http://127.0.0.1:5500/audit-portal/", timeout=3) as response:
        page = response.read(256 * 1024)
    if page != expected.read_bytes() or b'data-role="audit"' not in page:
        raise ValueError("Audit portal did not serve the expected build")


def apply(candidate, installed, targets, target):
    status("downloading", installed, candidate, last_target=target)
    expected_node = health()
    with tempfile.TemporaryDirectory(prefix=".mysticego-update-", dir=ROOT) as folder:
        stage = Path(folder)
        package = stage / candidate["asset"]
        github("release", "download", candidate["tag"], "--repo", REPO,
                "--pattern", candidate["asset"], "--dir", folder, timeout=600)
        with package.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if package.stat().st_size != candidate["size"] or "sha256:" + digest != candidate["digest"]:
            raise ValueError("digest or size mismatch")
        if versions() != installed or newest(installed) != (candidate, targets):
            raise ValueError("release or installed build changed")
        app = unpack(package, stage)
        orchestra = app / WEB / "orchestra"
        audit = app / WEB / "audit"
        if panel_version(orchestra) != candidate["tag"] or panel_version(audit) != candidate["tag"]:
            raise ValueError("release tag does not match packaged panels")
        if target == "orchestra" and AUDIT.is_dir():
            shutil.rmtree(audit)
            shutil.copytree(AUDIT, audit)
        web_assets(audit, APP / WEB / "orchestra" if target == "audit" else orchestra)
        status("switching", installed, candidate, last_target=target)
        if target == "audit":
            AUDIT.rename(PREVIOUS["audit"])
            audit.rename(AUDIT)
            audit_health(AUDIT / "index.html")
            if health() != expected_node:
                raise ValueError("unexpected Orchestra ID")
        else:
            command("systemctl", "stop", SERVICE)
            try:
                APP.rename(PREVIOUS["orchestra"])
            except OSError:
                command("systemctl", "start", SERVICE)
                raise
            app.rename(APP)
            command("systemctl", "start", SERVICE)
            for _ in range(30):
                try:
                    if health() == expected_node:
                        audit_health(AUDIT / "index.html")
                        break
                except (OSError, ValueError, KeyError):
                    pass
                time.sleep(2)
            else:
                raise RuntimeError("new app did not pass health checks")
        # Commit the verified app independently: infrastructure failure must not
        # roll back a healthy app or touch its existing database.
        if target != "audit":
            maintenance_record("repairing", candidate["tag"], retry_after=time.time()+120)
        status("current", versions(), last_target=target)
        if target != "audit":
            ensure_maintenance(versions()[0], force=True)
            ensure_rescue(versions()[0])
        previous = PREVIOUS["audit" if target == "audit" else "orchestra"]
        shutil.rmtree(previous)


def main():
    # CloudDesk's SSH deployment and this timer share the local deployment lock.
    with open("/run/lock/mysticego-clouddesk-deploy.lock", "a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return
        run_locked()


def automatic_enabled():
    """Always update by default. Legacy application-owned toggle files are ignored."""
    return True


def automatic_target(candidate, targets, last):
    if not candidate or "orchestra" not in targets or not automatic_enabled():
        return None
    if last.get("candidate") == candidate and last.get("phase") in ("downloading", "switching", "failed"):
        return None
    # Persistent root-owned guard survives temporary GitHub outages and process restarts.
    try:
        prior = json.loads((STATE / "automatic-attempt.json").read_text())
        if prior.get("tag") == candidate["tag"]:
            return None
    except (OSError, ValueError, AttributeError):
        pass
    # No unattended retries of a failed release. Do not race active dependency provisioning.
    if system_ok("is-active", "--quiet", "mysticego-infrastructure-reconcile.service"):
        return None
    return "orchestra"


def run_locked():
    file = STATE / "status.json"
    last = json.loads(file.read_text()) if file.exists() else {}
    candidate, targets, target = None, [], None
    try:
        for component, previous in PREVIOUS.items():
            if previous.exists():
                if last.get("phase") == "current":
                    shutil.rmtree(previous)
                else:
                    restore(component)
        installed = versions()
        # Retry locally before contacting GitHub, even with no newer release or no network.
        ensure_maintenance(installed[0], force=take_maintenance_request())
        ensure_rescue(installed[0])
        candidate, targets = newest(installed)
        request = take_request()
        if not candidate:
            status("current", installed)
        elif isinstance(request, dict) and request.get("candidate") == candidate and request.get("target") in targets:
            target = request["target"]
            apply(candidate, installed, targets, target)
        elif last.get("phase") in ("downloading", "switching", "failed") and last.get("candidate") == candidate:
            status("failed", installed, candidate, targets, "Обновление остановилось. Проверьте журнал службы на VPS.", last.get("lastTarget"))
        elif (automatic := automatic_target(candidate, targets, last)):
            target = automatic
            write("automatic-attempt.json", {"tag": candidate["tag"]})
            apply(candidate, installed, targets, target)
        else:
            status("ready", installed, candidate, targets)
    except Exception as error:
        print(f"MysticEgo update failed: {type(error).__name__}: {error}", flush=True)
        committed = target is not None and file.exists() and json.loads(file.read_text()).get("phase") == "current"
        if not committed:
            try:
                for component in PREVIOUS:
                    restore(component)
            except Exception as rollback_error:
                print(f"Rollback failed: {type(rollback_error).__name__}: {rollback_error}", flush=True)
            message = str(error) if isinstance(error, GitHubUnavailable) else "Проверьте журнал службы обновления на VPS."
            status("unavailable" if isinstance(error, GitHubUnavailable) else "failed",
                   versions(), candidate, targets, message, target)
        raise SystemExit(1)


if __name__ == "__main__":
    main()


