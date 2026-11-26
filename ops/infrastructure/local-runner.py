#!/usr/bin/env python3
"""Run fixed Orchestra installation actions for the local service user."""
import json
import socket
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

DIRECTORY = Path(__file__).resolve().parent


def main():
    with socket.socket(fileno=sys.stdin.fileno()) as connection:
        _, uid, _ = struct.unpack("3i", connection.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12))
        expected = __import__("pwd").getpwnam("mysticego").pw_uid
        if uid != expected:
            return
        connection.settimeout(5)
        try:
            with connection.makefile("rb") as incoming:
                request = incoming.readline(80)
        except OSError:
            return
        jobs = {
            b"docker\n": {"component": "docker"},
            b"postgres\n": {"component": "postgres"},
            b"postgres-native\n": {"component": "postgres-native"},
            b"lmstudio panel-data\n": {"component": "lmstudio", "role": "panel-data"},
            b"lmstudio world\n": {"component": "lmstudio", "role": "world"},
        }
        data = jobs.get(request)
        if data is None:
            connection.sendall(b'{"ok":false,"message":"Invalid installation request"}\n')
            return
        payload = {"ok": False, "message": "Установка не завершилась. Проверьте журнал mysticego-local-setup."}
        with tempfile.TemporaryDirectory(prefix="mysticego-local-setup-") as temporary, tempfile.TemporaryFile() as output:
            directory = Path(temporary)
            (directory / "request.json").write_text(json.dumps(data))
            for name in ("install.sh", "install.py"):
                (directory / name).symlink_to(DIRECTORY / name)
            try:
                result = subprocess.run(("/bin/bash", str(directory / "install.sh")), stdin=subprocess.DEVNULL,
                                        stdout=output, stderr=output, timeout=45 * 60)
                payload["ok"] = result.returncode == 0
            except (OSError, subprocess.TimeoutExpired):
                pass
            output.seek(max(0, output.tell() - 3500))
            tail = output.read().decode("utf-8", errors="replace").strip()
            if tail:
                payload["message"] = tail
        connection.sendall((json.dumps(payload, ensure_ascii=False) + "\n").encode())


if __name__ == "__main__":
    try:
        main()
    except (OSError, KeyError):
        raise SystemExit(1)
