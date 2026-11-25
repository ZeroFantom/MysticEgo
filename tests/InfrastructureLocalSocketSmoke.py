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
    import pwd
    pwd.getpwnam = lambda _: SimpleNamespace(pw_uid=os.getuid())
    runner.main()


def smoke():
    with tempfile.TemporaryDirectory() as tmp:
        directory = Path(tmp)
        (directory / "install.sh").write_text('cd -- "$(dirname -- "$0")"; cat request.json')
        (directory / "install.py").touch()
        path = str(directory / "test.sock")
        with socket.socket(socket.AF_UNIX) as listener:
            listener.bind(path)
            listener.listen()
            for line, expected in [
                (b"docker\n", {"component": "docker"}),
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
                        if expected:
                            assert json.loads(reply["message"]) == expected
                    finally:
                        assert child.wait(timeout=6) == 0
    print("Local installer socket accepted its allowlist and rejected other commands.")


if __name__ == "__main__":
    if len(sys.argv) == 2:
        serve(sys.argv[1])
    else:
        smoke()
