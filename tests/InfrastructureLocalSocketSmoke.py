"""Exercise the Unix socket protocol without installing host packages."""
import importlib.util
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[1]
RUNNER = ROOT / "ops/infrastructure/local-runner.py"


def serve(directory):
    spec = importlib.util.spec_from_file_location("local_runner", RUNNER)
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    runner.DIRECTORY = Path(directory)
    runner.REPORT = Path(directory) / "report.json"
    runner.host_units = lambda: [{"Id": "docker.service", "ActiveState": "active"}]
    import pwd
    pwd.getpwnam = lambda _: SimpleNamespace(pw_uid=os.getuid())
    runner.main()


def smoke():
    with tempfile.TemporaryDirectory() as tmp:
        directory = Path(tmp)
        (directory / "install.sh").write_text('cd -- "$(dirname -- "$0")"; cat request.json')
        (directory / "install.py").touch()
        report = {"components": [{"name": "docker", "state": "available", "message": "ready", "checkedAt": "2026-01-01T00:00:00Z"}]}
        (directory / "report.json").write_text(json.dumps(report))
        path = str(directory / "test.sock")
        with socket.socket(socket.AF_UNIX) as listener:
            listener.bind(path)
            listener.listen()
            for line, expected in [
                (b"maintenance-status\n", report),
                (b"diagnostic-host\n", [{"Id": "docker.service", "ActiveState": "active"}]),
                (b"metrics-source "+b"a"*64+b"\n", {"component":"metrics-source","secret":"a"*64}),
                (b"metrics-source "+b"!"*64+b"\n", None),
                (b"docker\n", {"component": "docker"}),
                (b"postgres\n", {"component": "postgres"}),
                (b"postgres-native\n", {"component": "postgres-native"}),
                (b"lmstudio world\n", {"component": "lmstudio", "role": "world"}),
                (b"exec other-command\n", None),
            ]:
                with socket.socket(socket.AF_UNIX) as client:
                    client.settimeout(5)
                    client.connect(path)
                    connection, _ = listener.accept()
                    try:
                        child = subprocess.Popen([sys.executable, __file__, tmp], stdin=connection)
                    finally:
                        connection.close()
                    try:
                        client.sendall(line)
                        with client.makefile() as source:
                            reply = json.loads(source.readline())
                        assert reply["ok"] is (expected is not None)
                        if line == b"maintenance-status\n":
                            assert reply["report"] == expected
                        elif line == b"diagnostic-host\n":
                            assert reply["units"] == expected
                        elif expected:
                            assert json.loads(reply["message"]) == expected
                    finally:
                        assert child.wait(timeout=6) == 0
    print("Local installer socket accepted its allowlist and rejected other commands.")


if __name__ == "__main__":
    if len(sys.argv) == 2:
        serve(sys.argv[1])
    else:
        smoke()
