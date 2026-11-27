#!/usr/bin/env python3
"""Best-effort fixed-unit accounting while the systemd cgroup still exists.
No subprocess input, arbitrary paths or application secrets are collected.
"""
import datetime
import json
import os
import pathlib
import sys
import tempfile
import subprocess

UNITS = {"mysticego-orchestra-updater.service", "mysticego-infrastructure-reconcile.service"}
OUTPUT = pathlib.Path("/var/lib/mysticego-infrastructure-status/oneshot")


def number(path):
    try:
        value = int(path.read_text().strip())
        return value if 0 <= value < 2**63 else None
    except (OSError, ValueError):
        return None


def cpu_ms(group):
    try:
        values = dict(line.split() for line in (group / "cpu.stat").read_text().splitlines())
        return int(values["usage_usec"]) // 1000
    except (OSError, ValueError, KeyError):
        return None


def duration_ms(unit):
    try:
        result = subprocess.run(
            ["/usr/bin/systemctl", "show", "--property=ExecMainStartTimestampMonotonic,ExecMainExitTimestampMonotonic", unit],
            capture_output=True, text=True, timeout=2, check=True,
        )
        values = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
        start, end = int(values["ExecMainStartTimestampMonotonic"]), int(values["ExecMainExitTimestampMonotonic"])
        return (end - start) // 1000 if 0 < start <= end else None
    except (OSError, ValueError, KeyError, subprocess.SubprocessError):
        return None


def collect(unit):
    if unit not in UNITS:
        return
    # ExecStopPost runs in the same fixed unit cgroup before systemd removes it.
    expected = "/system.slice/" + unit
    groups = pathlib.Path("/proc/self/cgroup").read_text().splitlines()
    if "0::" + expected not in groups:
        return
    group = pathlib.Path("/sys/fs/cgroup") / expected.lstrip("/")
    result = os.environ.get("SERVICE_RESULT", "unknown")
    if result not in {"success", "resources", "timeout", "exit-code", "signal", "core-dump", "watchdog", "start-limit-hit", "oom-kill", "protocol"}:
        result = "unknown"
    record = {
        "unit": unit,
        "finishedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "result": result,
        "cpuMilliseconds": cpu_ms(group),
        "peakBytes": number(group / "memory.peak"),
        "durationMilliseconds": duration_ms(unit),
    }
    OUTPUT.mkdir(mode=0o755, parents=True, exist_ok=True)
    fd, temp = tempfile.mkstemp(dir=OUTPUT, prefix=".run-")
    try:
        with os.fdopen(fd, "w") as file:
            json.dump(record, file, separators=(",", ":"))
            os.fchmod(file.fileno(), 0o644)
        os.replace(temp, OUTPUT / (unit + ".json"))
    finally:
        if os.path.exists(temp):
            os.unlink(temp)


if __name__ == "__main__":
    try:
        if len(sys.argv) == 2:
            collect(sys.argv[1])
    except (OSError, ValueError):
        # Accounting cannot turn a successful update into a failed update.
        pass
