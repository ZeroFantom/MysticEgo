#!/usr/bin/env python3
"""Overlay freshly built portable .NET hosts and their dependency closures.

Keep existing runtime configuration templates and native launchers. Reject two
different assemblies with the same relative name rather than silently choosing
one host's dependency version over another.
"""
import hashlib
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HOSTS = [
    ROOT / "core/Api.Auth",
    *(ROOT / "services" / f"Api.{role}" for role in
      ("Orchestra", "Global", "Local", "Agent", "AuditOrchestra", "Capacity")),
    ROOT / "tools/MysticEgo.Installer",
]


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def refresh(destination):
    files = {}
    for project in HOSTS:
        output = project / "bin/Release/net10.0"
        for suffix in (".dll", ".deps.json", ".runtimeconfig.json"):
            if not (output / (project.name + suffix)).is_file():
                raise SystemExit(f"Missing build output: {project.name}{suffix}")
        for source in sorted(output.rglob("*")):
            if not source.is_file():
                continue
            relative = source.relative_to(output)
            if not (source.suffix == ".dll" or source.name.endswith((".deps.json", ".runtimeconfig.json")) or relative.parts[0] == "runtimes"):
                continue
            if relative in files and digest(files[relative]) != digest(source):
                raise SystemExit(f"Conflicting dependency: {relative} from {files[relative]} and {source}")
            files[relative] = source
    for relative, source in files.items():
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    print(f"Refreshed {len(files)} runtime files for {len(HOSTS)} hosts")


if __name__ == "__main__":
    refresh(Path(sys.argv[1]))
