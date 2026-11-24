#!/usr/bin/env python3
"""Produce the Orchestra Linux release asset from a complete, built MysticEgo ZIP."""
import argparse
import shutil
import tarfile
import tempfile
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("bundle", type=Path)
args = parser.parse_args()
output = Path("MysticEgo-Orchestra-linux-x64.zip")
with tempfile.TemporaryDirectory() as tmp, ZipFile(args.bundle) as source:
    runtime = Path(tmp)
    with source.open("MysticEgo/runtime/linux-x64.tar.gz") as stream:
        with tarfile.open(fileobj=stream, mode="r|gz") as archive:
            archive.extractall(runtime, filter="data")
    if not (runtime / "dotnet").is_file():
        raise SystemExit("Linux runtime is missing.")
    with ZipFile(output, "x", compression=ZIP_DEFLATED) as target:
        for item in source.infolist():
            name = item.filename
            if not name.startswith("MysticEgo/") or name.startswith(("MysticEgo/runtime/electron", "MysticEgo/runtime/win-x64/", "MysticEgo/runtime/linux-x64/")):
                continue
            if name == "MysticEgo/runtime/linux-x64.tar.gz":
                continue
            with source.open(item) as reader, target.open(item, "w", force_zip64=True) as writer:
                shutil.copyfileobj(reader, writer)
        for file in sorted(runtime.rglob("*")):
            if file.is_file() and not file.is_symlink():
                target.write(file, "MysticEgo/runtime/linux-x64/" + file.relative_to(runtime).as_posix())
