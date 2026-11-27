#!/usr/bin/env python3
"""Root-owned local reconciliation; no provider switch or credential rotation."""
import fcntl
import json
from pathlib import Path
import subprocess
import time
from datetime import datetime, timezone
import install

STATUS = Path("/var/lib/mysticego-infrastructure-status")
ENV = Path("/etc/mysticego/orchestra.env")
rows = {}
attempts = {}
history = []

def checked(*args):
    try:
        return subprocess.run(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10).returncode == 0
    except (OSError, subprocess.TimeoutExpired):
        return False

def active(unit):
    return checked("systemctl", "is-active", "--quiet", unit)

def probe(url):
    try:
        return json.loads(install.request(url))
    except (OSError, ValueError):
        return None

def configured(*keys):
    try:
        return any(line.strip().startswith(keys) and line.partition("=")[2].strip().strip('"')
                   for line in ENV.read_text().splitlines())
    except OSError:
        return False

def publish(name, state, message, reason=None):
    rows[name] = dict(name=name, state=state, message=message,
                      checkedAt=datetime.now(timezone.utc).isoformat())
    if reason is not None:
        rows[name]["reason"] = reason
    history.append(rows[name])
    del history[:-120]
    install.atomic(STATUS / "status.json", json.dumps({"components": list(rows.values()), "history": history}), 0o644)
    install.atomic(STATUS / "attempts.json", json.dumps(attempts))

def repair(name, test, action, message):
    if test():
        attempts.pop(name, None)
        publish(name, "available", message)
        return
    if time.time() - attempts.get(name, 0) < 900:
        publish(name, "degraded", "Повторная попытка после паузы; проверьте журнал службы.")
        return
    attempts[name] = time.time()
    publish(name, "repairing", "Автоматическое восстановление выполняется.")
    try:
        action()
        if not test():
            raise RuntimeError("health probe failed")
        attempts.pop(name, None)
        publish(name, "available", message)
    except Exception as error:
        print(name + ": " + type(error).__name__, flush=True)
        reason = type(error).__name__
        if reason not in ("CalledProcessError", "TimeoutExpired", "OSError", "PermissionError", "FileNotFoundError", "ValueError", "RuntimeError"):
            reason = "OtherError"
        publish(name, "degraded", "Восстановление не завершилось. Проверьте журнал службы.", reason)

def lm_ready():
    # A compatibility catalog can list downloaded but unloaded models.
    native = probe("http://127.0.0.1:1234/api/v1/models")
    if isinstance(native, dict) and isinstance(native.get("models"), list):
        return any(model.get("type") == "llm" and model.get("loaded_instances")
                   for model in native["models"] if isinstance(model, dict))
    legacy = probe("http://127.0.0.1:1234/api/v0/models")
    if isinstance(legacy, dict) and isinstance(legacy.get("data"), list):
        return any(model.get("state") == "loaded" and model.get("type") in ("llm", "vlm")
                   for model in legacy["data"] if isinstance(model, dict))
    return False

def lm_repair():
    profile = install.ROOT / "lmstudio/role"
    if profile.is_file() and Path("/etc/systemd/system/mysticego-lmstudio.service").is_file():
        install.run("systemctl", "restart", "mysticego-lmstudio.service", timeout=320)
    else:
        install.lmstudio({"role": profile.read_text().strip() if profile.is_file() else "panel-data"})

def postgres():
    method = install.ROOT / "postgres/method"
    if not method.is_file():
        publish("postgres-service", "external", "Сохранена текущая база приложения. Провайдер не меняется.")
        return
    value = method.read_text().strip()
    if value == "native":
        test = lambda: checked("pg_isready", "-p", "55432")
        action = lambda: install.run("pg_ctlcluster", "16", "mysticego", "start", timeout=60)
    elif value == "docker":
        compose = ("docker", "compose", "-f", str(install.ROOT / "postgres/compose.json"))
        test = lambda: checked(*compose, "exec", "-T", "--user", "postgres", "db", "pg_isready", "-p", "55432")
        action = lambda: install.run(*compose, "start", "db", timeout=90)
    else:
        publish("postgres-service", "degraded", "Неизвестный способ установки PostgreSQL.")
        return
    repair("postgres-service", test, action, "Существующая служба PostgreSQL отвечает.")

def voice_ready():
    return probe("http://127.0.0.1:1237/health") == {"status": "ready"}

def voice_repair():
    if Path("/etc/systemd/system/mysticego-voice.service").is_file():
        install.run("systemctl", "restart", "mysticego-voice.service", timeout=120)
    else:
        install.run("/bin/bash", str(Path(__file__).with_name("install-voice.sh")), timeout=900)
    for _ in range(45):
        if voice_ready():
            return
        time.sleep(2)

def cycle():
    global attempts, rows, history
    STATUS.mkdir(mode=0o755, parents=True, exist_ok=True)
    STATUS.chmod(0o755) # systemd UMask=0077 must not hide the public status directory
    try:
        attempts = json.loads((STATUS / "attempts.json").read_text())
    except (OSError, ValueError):
        attempts = {}
    try:
        previous = json.loads((STATUS / "status.json").read_text())
        rows = {item["name"]: item for item in previous["components"]}
        history = previous.get("history", [])[-120:]
    except (OSError, ValueError, KeyError, TypeError):
        rows = {}
        history = []
    publish("orchestra", "available" if active("mysticego-orchestra.service") else "degraded", "Этот VPS Orchestra.")
    if configured("MYSTICEGO_AUTO_DOCKER=off"):
        publish("docker", "disabled", "Автоматическая установка отключена.")
    else:
        repair("docker", lambda: checked("docker", "info") and checked("docker", "compose", "version"),
               install.docker, "Docker и Compose готовы.")
    postgres()
    if configured("MYSTICEGO_DEFAULT_MODEL=off", "PanelAssistant__Model=", "WorldAI__RemoteWorkers__"):
        publish("lmstudio", "external", "Сохранена заданная конфигурация модели.")
    elif active("mysticego-default-model.service"):
        publish("lmstudio", "repairing", "Первоначальная установка модели выполняется.")
    else:
        repair("lmstudio", lm_ready, lm_repair, "LM Studio сообщает о загруженной модели.")
    if configured("MYSTICEGO_AUTO_VOICE=off", "VoiceTranscription__Endpoint="):
        publish("voice", "external", "Сохранена заданная конфигурация распознавания речи.")
    else:
        repair("voice", voice_ready, voice_repair, "Распознавание речи готово.")

if __name__ == "__main__":
    with open("/run/lock/mysticego-infrastructure.lock", "a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise SystemExit(0)
        cycle()
