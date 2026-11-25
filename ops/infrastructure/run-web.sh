#!/usr/bin/env bash
set -euo pipefail
umask 077
root=/opt/mysticego-infrastructure/runner
stage=$(mktemp -d)
trap 'rm -rf -- "$stage"' EXIT
python3 -c 'import sys; from pathlib import Path; data=sys.stdin.buffer.read(4097); assert 0<len(data)<=4096; Path(sys.argv[1]).write_bytes(data)' "$stage/request.json"
ln -s "$root/install.sh" "$root/install.py" "$root/monitoring" "$stage/"
bash "$stage/install.sh"
