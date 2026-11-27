#!/usr/bin/env python3
"""Run fixed Orchestra installation actions for the local service user."""
import json
import re
import socket
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

DIRECTORY = Path(__file__).resolve().parent
REPORT = Path('/var/lib/mysticego-infrastructure-status/status.json')


UNITS = ("mysticego-orchestra.service", "mysticego-orchestra-updater.service",
         "mysticego-orchestra-updater.timer", "mysticego-infrastructure-reconcile.service",
         "mysticego-infrastructure-reconcile.timer", "mysticego-local-setup.socket",
         "mysticego-default-model.service", "mysticego-lmstudio.service",
         "mysticego-voice.service", "docker.service", "postgresql.service",
         "postgresql@16-mysticego.service", "caddy.service")
PROPERTIES = ("Id", "LoadState", "ActiveState", "SubState", "Result", "ExecMainStatus", "NRestarts")

def host_units():
    # No journal text, command line, credentials, paths or arbitrary unit names.
    try:
        result = subprocess.run(("systemctl", "show", "--no-pager", "--property=" + ",".join(PROPERTIES), *UNITS),
                                capture_output=True, text=True, timeout=4)
        units = []
        for block in result.stdout.split("\n\n"):
            data = dict(line.split("=", 1) for line in block.splitlines() if "=" in line)
            if data.get("Id") not in UNITS:
                continue
            units.append({key: data[key] for key in PROPERTIES if key in data and re.fullmatch(r"[a-zA-Z0-9@_.-]{1,80}", data[key])})
        return units
    except (OSError, subprocess.TimeoutExpired):
        return []

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
        # Read-only host report also works for Orchestra inside a container:
        # its existing Unix socket is mounted, but the host report directory is not.
        if request == b"diagnostic-host\n":
            connection.sendall((json.dumps({"ok": True, "units": host_units()}) + "\n").encode())
            return
        if request == b"maintenance-status\n":
            try:
                if REPORT.stat().st_size > 131072:
                    raise ValueError("report too large")
                report = json.loads(REPORT.read_text())
                if not isinstance(report.get("components"), list):
                    raise ValueError("invalid report")
                reply = {"ok": True, "report": report}
            except (OSError, ValueError, AttributeError):
                reply = {"ok": False, "message": "Отчёт на хосте ещё не создан."}
            connection.sendall((json.dumps(reply, ensure_ascii=False) + "\n").encode())
            return
        jobs = {
            b"monitoring\n": {"component": "monitoring"},
            b"docker\n": {"component": "docker"},
            b"postgres\n": {"component": "postgres"},
            b"postgres-native\n": {"component": "postgres-native"},
            b"lmstudio panel-data\n": {"component": "lmstudio", "role": "panel-data"},
            b"lmstudio world\n": {"component": "lmstudio", "role": "world"},
        }
        data = jobs.get(request)
        if re.fullmatch(rb'metrics-source [a-f0-9]{64}\n', request):
            data = {"component": "metrics-source", "secret": request[15:-1].decode('ascii')}
        if data is None:
            connection.sendall(b'{"ok":false,"message":"Invalid installation request"}\n')
            return
        payload = {"ok": False, "message": "Установка не завершилась. Проверьте журнал mysticego-local-setup."}
        with tempfile.TemporaryDirectory(prefix="mysticego-local-setup-") as temporary, tempfile.TemporaryFile() as output:
            directory = Path(temporary)
            (directory / "request.json").write_text(json.dumps(data))
            for name in ("install.sh", "install.py"):
                (directory / name).symlink_to(DIRECTORY / name)
            if (DIRECTORY / "monitoring").is_dir():
                (directory / "monitoring").symlink_to(DIRECTORY / "monitoring", target_is_directory=True)
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
