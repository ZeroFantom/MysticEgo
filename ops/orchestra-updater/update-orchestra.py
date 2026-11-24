#!/usr/bin/env python3
"""Root-owned worker. The web process can only approve the exact published candidate."""
import hashlib
import json
import os
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


def command(*args, timeout=60):
    return subprocess.check_output(args, text=True, timeout=timeout, stderr=subprocess.PIPE)


def write(name, data):
    temporary = STATE / (name + ".tmp")
    temporary.write_text(json.dumps(data), encoding="utf-8")
    temporary.replace(STATE / name)


def status(phase, current, candidate=None, message=None):
    write("status.json", dict(phase=phase, current=current, candidate=candidate, message=message))


def newest(current):
    # Releases must provide the dedicated, platform-specific complete bundle.
    releases = json.loads(command("gh", "api", f"repos/{REPO}/releases?per_page=30"))
    baseline = json.loads(command("gh", "api", f"repos/{REPO}/releases/tags/{current}"))["published_at"]
    for release in sorted(releases, key=lambda item: item.get("published_at") or "", reverse=True):
        tag = release["tag_name"]
        if release["draft"] or not release.get("published_at") or release["published_at"] <= baseline or not re.fullmatch(r"v[0-9][A-Za-z0-9._-]{0,80}", tag):
            continue
        for asset in release["assets"]:
            digest = asset.get("digest") or ""
            if (asset["name"] == "MysticEgo-Orchestra-linux-x64.zip" and asset["state"] == "uploaded"
                    and 0 < asset["size"] <= LIMIT and re.fullmatch(r"sha256:[a-f0-9]{64}", digest)):
                return dict(tag=tag, digest=digest, asset=asset["name"], id=asset["id"], size=asset["size"])
    return None


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
            return json.loads(data) if len(data) <= 4096 else None
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


def apply(candidate, current):
    status("downloading", current, candidate)
    expected_node = health()
    previous = ROOT / ".orchestra-previous"
    if previous.exists():
        raise ValueError("previous update requires administrator review")
    with tempfile.TemporaryDirectory(prefix=".orchestra-update-", dir=ROOT) as folder:
        stage = Path(folder)
        package = stage / candidate["asset"]
        command("gh", "release", "download", candidate["tag"], "--repo", REPO,
                "--pattern", candidate["asset"], "--dir", folder, timeout=600)
        with package.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if package.stat().st_size != candidate["size"] or "sha256:" + digest != candidate["digest"]:
            raise ValueError("digest or size mismatch")
        if newest(current) != candidate:
            raise ValueError("release changed")
        app = unpack(package, stage)
        # Keep deployment state external to the application directory.
        status("switching", current, candidate)
        stopped = False
        try:
            command("systemctl", "stop", SERVICE)
            stopped = True
            APP.rename(previous)
            app.rename(APP)
            command("systemctl", "start", SERVICE)
            for _ in range(30):
                try:
                    if health() == expected_node:
                        write("current.json", candidate)
                        break
                except (OSError, ValueError, KeyError):
                    pass
                time.sleep(2)
            else:
                raise RuntimeError("new Orchestra did not pass health check")
        except Exception:
            if previous.exists():
                command("systemctl", "stop", SERVICE)
                if APP.exists():
                    APP.rename(stage / "rejected")
                previous.rename(APP)
            if stopped:
                command("systemctl", "start", SERVICE)
            raise
        shutil.rmtree(previous)
    status("current", candidate["tag"])


def main():
    installed = json.loads((STATE / "current.json").read_text())
    current = installed["tag"]
    try:
        previous = ROOT / ".orchestra-previous"
        if previous.exists():
            last = json.loads((STATE / "status.json").read_text())
            if last.get("candidate") == installed:
                shutil.rmtree(previous)
            else:
                command("systemctl", "stop", SERVICE)
                if APP.exists():
                    with tempfile.TemporaryDirectory(prefix=".orchestra-rejected-", dir=ROOT) as folder:
                        APP.rename(Path(folder) / "app")
                        previous.rename(APP)
                else:
                    previous.rename(APP)
                command("systemctl", "start", SERVICE)
        candidate = newest(current)
        request = take_request()
        if not candidate:
            status("current", current)
            return
        if request == candidate:
            apply(candidate, current)
        else:
            last_file = STATE / "status.json"
            last = json.loads(last_file.read_text()) if last_file.exists() else {}
            if last.get("phase") in ("downloading", "switching", "failed") and last.get("candidate") == candidate:
                status("failed", current, candidate, "Обновление остановилось. Проверьте журнал службы на VPS.")
            else:
                status("ready", current, candidate)
    except Exception as error:
        print(f"Orchestra update failed: {type(error).__name__}: {error}", flush=True)
        status("failed", current, locals().get("candidate"), "Проверьте журнал службы обновления на VPS.")
        raise SystemExit(1)


if __name__ == "__main__":
    main()
