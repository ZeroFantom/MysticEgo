#!/usr/bin/env python3
"""Independent root-owned recovery channel. Only a fixed request is accepted."""
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
import urllib.request
from datetime import datetime, timezone

INBOX = Path("/var/lib/mysticego/update-inbox")
STATE = Path("/var/lib/mysticego-orchestra-updater")
SCRIPT = Path("/opt/mysticego/app/ops/orchestra-updater/recover-maintenance.sh")

def trusted(path):
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

def take_request():
    try:
        directory = os.open(INBOX, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            name = "maintenance-rescue.json"
            try:
                fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
            except OSError:
                # Remove a malformed symlink, never follow it.
                try:
                    if stat.S_ISLNK(os.stat(name,dir_fd=directory,follow_symlinks=False).st_mode):
                        os.unlink(name,dir_fd=directory)
                except OSError:
                    pass
                return False
            try:
                return stat.S_ISREG(os.fstat(fd).st_mode) and os.read(fd,64)==b'{"confirmed":true}'
            finally:
                os.close(fd)
                os.unlink(name,dir_fd=directory)
        finally:
            os.close(directory)
    except OSError:
        return False

def publish(state, reason=""):
    fd, name=tempfile.mkstemp(prefix=".rescue-",dir=STATE)
    try:
        with os.fdopen(fd,"w") as file:
            json.dump(dict(state=state, reason=reason, checkedAt=datetime.now(timezone.utc).isoformat()),file)
        os.chmod(name,0o644)
        os.replace(name,STATE/"rescue.json")
    finally:
        Path(name).unlink(missing_ok=True)

def recover():
    if not take_request():
        return
    publish("repairing")
    try:
        if not trusted(SCRIPT):
            raise ValueError("untrusted-package")
        client=urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with client.open("http://127.0.0.1:5500/health/live",timeout=5) as response:
            health=json.load(response)
        node=health.get("node","")
        if health.get("status")!="alive" or health.get("kind","").lower()!="orchestra" or not isinstance(node,str) or not re.fullmatch(r"[a-fA-F0-9-]{36}",node):
            raise ValueError("invalid-node")
        subprocess.run(("bash",str(SCRIPT),node),timeout=480,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        publish("ready")
    except (OSError,ValueError,subprocess.SubprocessError) as error:
        publish("degraded",type(error).__name__)

if __name__=="__main__":
    recover()
