#!/usr/bin/env python3
"""Provision only the selected MysticEgo component. Request JSON never enters command arguments."""
import base64
import ipaddress
import json
import os
from pathlib import Path
import pwd
import re
import secrets
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path("/opt/mysticego-infrastructure")
STATE = Path("/var/lib/mysticego")
LM_HOME = Path("/var/lib/mysticego-lm")
HEX = re.compile(r"[a-f0-9]{64}")
SERVICES = ("auth", "orchestra", "audit", "global", "local", "agent", "world_ai")
MODELS = {"world": ("qwen/qwen3-4b", "mysticego-world", 4096),
          "panel-data": ("qwen/qwen3-1.7b", "mysticego-panel-data", 4096)}
RFC1918 = tuple(map(ipaddress.ip_network, ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16")))


def say(text):
    print(text, flush=True)


def run(*args, **options):
    subprocess.run(args, check=True, **options)


def atomic(path, value, mode=0o600, owner=None):
    fd, name = tempfile.mkstemp(dir=path.parent, prefix="." + path.name + ".")
    try:
        with os.fdopen(fd, "w") as output:
            output.write(value)
        os.chmod(name, mode)
        if owner is not None:
            os.chown(name, owner, -1)
        os.replace(name, path)
    finally:
        Path(name).unlink(missing_ok=True)


def account(user, home):
    try:
        return pwd.getpwnam(user).pw_uid
    except KeyError:
        run("useradd", "--system", "--user-group", "--create-home", "--home-dir", str(home),
            "--shell", "/usr/sbin/nologin", user)
        return pwd.getpwnam(user).pw_uid


def ensure_lm_home():
    # Factory reset removes LM_HOME but intentionally keeps the system account.
    uid = account("mysticego-lm", LM_HOME)
    entry = pwd.getpwnam("mysticego-lm")
    if entry.pw_uid != uid or Path(entry.pw_dir) != LM_HOME:
        raise ValueError("Unexpected LM Studio service account home.")
    if LM_HOME.is_symlink():
        raise ValueError("LM Studio home must not be a symbolic link.")
    try:
        LM_HOME.mkdir(mode=0o700)
    except FileExistsError:
        if not LM_HOME.is_dir() or LM_HOME.stat().st_uid != uid:
            raise ValueError("Unexpected LM Studio home ownership or type.")
    else:
        os.chown(LM_HOME, uid, entry.pw_gid)
    return uid


def system_packages(*packages):
    run("apt-get", "update")
    run("apt-get", "install", "-y", "--no-install-recommends", *packages,
        env={**os.environ, "DEBIAN_FRONTEND": "noninteractive"})


def docker():
    if not shutil.which("docker"):
        say("Устанавливаем Docker из репозитория Ubuntu")
        system_packages("docker.io", "docker-compose-v2")
    elif subprocess.run(("docker", "compose", "version"), stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL).returncode:
        say("Устанавливаем Docker Compose v2")
        system_packages("docker-compose-v2")
    run("systemctl", "enable", "--now", "docker.service")
    run("docker", "compose", "version")
    run("docker", "info", stdout=subprocess.DEVNULL)


class DirectRequest(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, file, code, message, headers, new_url):
        return None


def request(url, token=None):
    headers = {"Authorization": "Bearer " + token} if token else {}
    client = urllib.request.build_opener(urllib.request.ProxyHandler({}), DirectRequest())
    with client.open(urllib.request.Request(url, headers=headers), timeout=8) as response:
        return response.read(1024 * 1024)


def wait(url):
    for _ in range(25):
        try:
            request(url)
            return
        except (OSError, ValueError):
            time.sleep(2)
    raise ValueError("Служба не ответила по " + url)


def secret(data, name):
    value = data.get(name) or ""
    if not HEX.fullmatch(value):
        raise ValueError("Нужен ключ из 64 строчных шестнадцатеричных символов.")
    return value


def metrics_source(data):
    uid = pwd.getpwnam("mysticego").pw_uid
    directory = STATE / "orchestra"
    if not directory.is_dir() or directory.is_symlink() or directory.stat().st_uid != uid:
        raise ValueError("На этом сервере не найден стандартный каталог Orchestra.")
    atomic(directory / "monitoring-token", secret(data, "secret") + "\n", owner=uid)
    say("Ключ метрик сохранён. Для удалённого Prometheus укажите этот же ключ.")


def monitoring(data):
    url = (data.get("url") or "").strip()
    scrape = {"job_name": "mysticego", "static_configs": [{"targets": ["127.0.0.1:5500"]}]}
    token = ""
    if url:
        origin = urllib.parse.urlsplit(url)
        if (origin.scheme != "https" or not origin.hostname or origin.username or origin.password or
                origin.path not in ("", "/") or origin.query or origin.fragment):
            raise ValueError("Введите HTTPS-адрес Orchestra без пути.")
        token = secret(data, "secret")
        url = f"https://{origin.netloc}/api/v1/public/metrics-scrape"
        scrape.update(scheme="https", metrics_path="/api/v1/public/metrics-scrape",
                      authorization={"credentials_file": "/etc/prometheus/metrics-token"},
                      static_configs=[{"targets": [origin.netloc]}])
    try:
        sample = request(url or "http://127.0.0.1:5500/metrics", token)
    except OSError:
        raise ValueError("Prometheus не смог прочитать метрики. Проверьте URL и ключ источника.") from None
    if b"mysticego_" not in sample:
        raise ValueError("Источник не вернул метрики MysticEgo.")
    docker()
    directory = ROOT / "monitoring"
    directory.mkdir(parents=True, exist_ok=True)
    shutil.copytree("monitoring", directory, dirs_exist_ok=True)
    for folder in ("provisioning", "dashboards"):
        assets = directory / folder
        assets.chmod(0o755)
        for path in assets.rglob("*"):
            path.chmod(0o755 if path.is_dir() else 0o644)
    atomic(directory / "prometheus.json", json.dumps({"global": {"scrape_interval": "15s"},
           "scrape_configs": [scrape]}), 0o644)
    atomic(directory / "metrics-token", token + "\n", 0o400, owner=65534)
    password = directory / "grafana-password"
    rotate_grafana = not password.exists()
    if rotate_grafana:
        atomic(password, secrets.token_hex(24) + "\n", 0o400, owner=472)
    # Keep the original project volumes when upgrading a prior local Compose stack.
    run("docker", "compose", "--project-directory", str(directory), "-f", str(directory / "compose.yml"),
        "up", "-d", "--force-recreate")
    wait("http://127.0.0.1:9090/-/ready")
    for _ in range(20):
        targets = json.loads(request("http://127.0.0.1:9090/api/v1/targets")).get("data", {})
        if any(row.get("health") == "up" and row.get("labels", {}).get("job") == "mysticego"
               for row in targets.get("activeTargets", [])):
            break
        time.sleep(2)
    else:
        raise ValueError("Prometheus запущен, но цель MysticEgo не в состоянии UP. Откройте страницу /targets.")
    wait("http://127.0.0.1:3000/api/health")
    if rotate_grafana:
        # Grafana on an existing data volume ignores the first-run password setting.
        try:
            encoded = base64.b64encode(("admin:" + password.read_text().strip()).encode()).decode()
            client = urllib.request.build_opener(urllib.request.ProxyHandler({}), DirectRequest())
            with client.open(urllib.request.Request("http://127.0.0.1:3000/api/user",
                    headers={"Authorization": "Basic " + encoded}), timeout=8):
                pass
        except OSError:
            say("Grafana использует пароль из существующего тома. Начальный файл действует для новой установки.")
    say("Prometheus готов на 127.0.0.1:9090, Grafana на 127.0.0.1:3000.")
    say("Grafana: admin. Начальный пароль находится в /opt/mysticego-infrastructure/monitoring/grafana-password.")
    say("Для доступа с ноутбука используйте SSH-перенаправление портов.")


def private_ip(value):
    ip = ipaddress.IPv4Address(value)
    if not ip.is_loopback and not any(ip in net for net in RFC1918):
        raise ValueError("Укажите 127.0.0.1 или IPv4 частной сети RFC1918.")
    return ip


def postgres(data):
    bind = private_ip(data.get("bind") or "127.0.0.1")
    network = ipaddress.IPv4Network(data.get("cidr") or "127.0.0.1/32", strict=True)
    if bind.is_loopback and network != ipaddress.IPv4Network("127.0.0.1/32"):
        raise ValueError("Для localhost допустим CIDR 127.0.0.1/32.")
    if not bind.is_loopback and (network.prefixlen < 24 or not any(network.subnet_of(net) for net in RFC1918)):
        raise ValueError("Для VPN или частной сети укажите CIDR клиентов /24–/32.")
    method = "native" if data.get("component") == "postgres-native" else "docker"
    directory = ROOT / "postgres"
    directory.mkdir(parents=True, exist_ok=True)
    config = directory / "network.json"
    network_config = [str(bind), str(network)]
    mode_file = directory / "method"
    previous = mode_file.read_text().strip() if mode_file.exists() else ("docker" if (directory / "compose.json").exists() else method)
    if previous != method:
        raise ValueError("Смена способа установки PostgreSQL требует отдельного переноса данных.")
    if config.exists() and json.loads(config.read_text()) != network_config:
        raise ValueError("PostgreSQL уже установлен с другой сетевой конфигурацией.")
    atomic(mode_file, method + "\n")
    passwords = directory / "credentials.json"
    if passwords.exists():
        values = json.loads(passwords.read_text())
        if data.get("secret") and data["secret"] != values["auth"]:
            raise ValueError("Для существующей базы укажите прежний ключ Auth или оставьте поле пустым.")
    else:
        values = {role: secrets.token_hex(32) for role in (*SERVICES, "admin")}
        if data.get("secret"):
            values["auth"] = secret(data, "secret")
        atomic(passwords, json.dumps(values))
    atomic(config, json.dumps(network_config))
    if method == "docker":
        docker()
        atomic(directory / "admin", values["admin"], 0o400, owner=999)
    atomic(directory / "pg_hba.conf", f"local all postgres peer\nlocal all all reject\nhost all all {network} scram-sha-256\n", 0o644)
    if method == "docker":
        compose = {"name": "mysticego-postgres", "services": {"db": {
            "image": "postgres:17-bookworm", "network_mode": "host", "restart": "unless-stopped",
            "environment": {"POSTGRES_PASSWORD_FILE": "/run/mysticego-password"},
            "command": ["postgres", "-p", "55432", "-c", f"listen_addresses={bind}", "-c",
                        "hba_file=/etc/mysticego-pg_hba.conf"],
            "volumes": ["data:/var/lib/postgresql/data", "./admin:/run/mysticego-password:ro",
                        "./pg_hba.conf:/etc/mysticego-pg_hba.conf:ro"]}}, "volumes": {"data": {}}}
        atomic(directory / "compose.json", json.dumps(compose))
        command = ("docker", "compose", "-f", str(directory / "compose.json"))
        run(*command, "up", "-d")
        for _ in range(30):
            if subprocess.run((*command, "exec", "-T", "--user", "postgres", "db", "pg_isready",
                               "-h", str(bind), "-p", "55432"), stdout=subprocess.DEVNULL).returncode == 0:
                break
            time.sleep(2)
        else:
            raise ValueError("PostgreSQL не запустился. Проверьте журнал контейнера.")
        sql_command = (*command, "exec", "-T", "--user", "postgres", "db", "psql", "-p", "55432", "-d", "postgres", "-v", "ON_ERROR_STOP=1")
    else:
        system_packages("postgresql-16", "postgresql-client-16")
        cluster = Path("/etc/postgresql/16/mysticego")
        if not cluster.exists():
            run("pg_createcluster", "16", "mysticego", "--port=55432", "--start-conf=auto")
        atomic(cluster / "pg_hba.conf", (directory / "pg_hba.conf").read_text(), 0o640, owner=pwd.getpwnam("postgres").pw_uid)
        atomic(cluster / "conf.d/mysticego.conf", "port = 55432\nlisten_addresses = '" + str(bind) + "'\npassword_encryption = 'scram-sha-256'\n", 0o644)
        run("systemctl", "enable", "postgresql.service")
        run("pg_ctlcluster", "16", "mysticego", "restart")
        sql_command = ("runuser", "-u", "postgres", "--", "psql", "-h", "/var/run/postgresql", "-p", "55432", "-d", "postgres", "-v", "ON_ERROR_STOP=1")
    setup = []
    for role in SERVICES:
        name = "mysticego_" + role
        password = values[role]
        if not HEX.fullmatch(password):
            raise ValueError("Файл учётных данных PostgreSQL повреждён.")
        setup.extend((f"""SELECT 'CREATE ROLE "{name}" LOGIN PASSWORD ''{password}''' WHERE NOT EXISTS
          (SELECT FROM pg_roles WHERE rolname='{name}') \\gexec""",
            f"""SELECT 'CREATE DATABASE "{name}" OWNER "{name}"' WHERE NOT EXISTS
          (SELECT FROM pg_database WHERE datname='{name}') \\gexec""",
            f'REVOKE CONNECT ON DATABASE "{name}" FROM PUBLIC;'))
    # SQL may contain passwords. Only report whether provisioning succeeded.
    if subprocess.run(sql_command, input="\n".join(setup) + "\n",
                      text=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
        raise ValueError("Не удалось подготовить роли и базы PostgreSQL.")
    say(f"PostgreSQL ({method}) готов на {bind}:55432. Созданы отдельные базы и роли: " + ", ".join(SERVICES) + ".")
    say("Учётные данные: /opt/mysticego-infrastructure/postgres/credentials.json (root).")


def auth_postgres(data):
    if any((STATE / file).exists() for file in ("auth.db", "auth.db-wal", "center-id.txt", "orchestra-identity.json")):
        raise ValueError("Обнаружена действующая установка. Перенос Auth требует миграции данных.")
    host = private_ip(data.get("bind") or "127.0.0.1")
    password = secret(data, "secret")
    uid = account("mysticego", STATE)
    connection = f"Host={host};Port=55432;Database=mysticego_auth;Username=mysticego_auth;Password={password};Timeout=5"
    atomic(STATE / "auth-postgres.connection", connection + "\n", owner=uid)
    say("PostgreSQL настроен для первого запуска Auth. SQLite на существующих узлах сохранён.")


def prepare_postgres(data):
    role = data.get("role")
    if role not in SERVICES or role == "world_ai":
        raise ValueError("Выберите роль панели или Auth.")
    if (STATE / "center-id.txt").exists() or (STATE / "auth.db").exists() or (STATE / "auth.db-wal").exists() or (STATE / "orchestra-identity.json").exists() or (STATE / role / "node.db").exists() or (STATE / role / "node.db.audit.db").exists():
        raise ValueError("Обнаружены данные действующего узла. Нужна отдельная миграция.")
    host = private_ip(data.get("bind") or "127.0.0.1")
    supplied = data.get("secret")
    if supplied:
        password = secret(data, "secret")
    elif host.is_loopback:
        credentials = ROOT / "postgres/credentials.json"
        if not credentials.is_file():
            raise ValueError("Укажите пароль роли или сначала установите PostgreSQL на этом VPS.")
        password = json.loads(credentials.read_text())[role]
        if not HEX.fullmatch(password):
            raise ValueError("Некорректный файл учётных данных.")
    else:
        raise ValueError("Для удалённой базы требуется пароль её отдельной роли.")
    if not host.is_loopback and data.get("url") != "private-vpn":
        raise ValueError("Удалённое подключение разрешено только по подтверждённой защищённой частной сети/VPN. Для SSH-туннеля используйте loopback.")
    name = "mysticego_" + role
    if not shutil.which("psql"):
        system_packages("postgresql-client-16")
    result = subprocess.run(("psql", "-X", "-h", str(host), "-p", "55432", "-U", name, "-d", name, "-At", "-v", "ON_ERROR_STOP=1",
                             "-c", "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'"),
                            env={**os.environ, "PGPASSWORD": password, "PGCONNECT_TIMEOUT": "5", "PGSSLMODE": "disable"},
                            capture_output=True, text=True, timeout=15)
    if result.returncode:
        raise ValueError("Не удалось подключиться к PostgreSQL. Проверьте сеть, CIDR и пароль роли.")
    if result.stdout.strip() != "0":
        raise ValueError("В базе уже есть таблицы. Для нового узла используйте отдельную пустую базу; повторное подключение требует явного восстановления.")
    directory = Path("/etc/mysticego/database-staging")
    directory.mkdir(parents=True, exist_ok=True, mode=0o700)
    os.chmod(directory, 0o700)
    connection = f"Host={host};Port=55432;Database={name};Username={name};Password={password};Timeout=5;SSL Mode=Disable"
    target = directory / (role + "-postgres.connection")
    if target.exists() and target.read_text().strip() != connection:
        raise ValueError("Роль уже подготовлена с другими параметрами; измените настройки вручную после проверки.")
    atomic(target, connection + "\n")
    say(f"Подготовлено подключение {role} к PostgreSQL {host}:55432. Пароль сохранён только на целевом VPS.")


def lmstudio_dependencies():
    """Install the required llmster runtime dependency before running its installer."""
    missing = []
    for package in ("libatomic1", "libgomp1"):
        result = subprocess.run(("dpkg-query", "-W", "-f=${Status}", package),
                                capture_output=True, text=True, timeout=10)
        if result.returncode != 0 or result.stdout.strip() != "install ok installed":
            missing.append(package)
    if not shutil.which("curl"):
        missing.extend(("curl", "ca-certificates"))
    if missing:
        say("Устанавливаем зависимости LM Studio: " + ", ".join(missing))
        system_packages(*missing)


def lm_cache_present(model):
    family = model.split("/", 1)[1]
    model_folder = LM_HOME / ".lmstudio/models"
    return any(path.is_file() and path.stat().st_size > 0 and family in path.parent.name.lower()
               and "q4_k_m" in path.name.lower() for path in model_folder.glob("*/*/*.gguf"))


def lmstudio(data):
    role = data.get("role") or "panel-data"
    if role not in MODELS:
        raise ValueError("Выберите профиль модели: world или panel-data.")
    model, alias, context = MODELS[role]
    uid = ensure_lm_home()
    state = ROOT / "lmstudio"
    state.mkdir(parents=True, exist_ok=True)
    profile = state / "role"
    if profile.exists() and profile.read_text().strip() != role:
        raise ValueError("Этот сервер уже использует другой профиль LM Studio.")
    command = ("runuser", "-u", "mysticego-lm", "--", "env", f"HOME={LM_HOME}")
    cli = LM_HOME / ".lmstudio/bin/lms"
    lmstudio_dependencies()
    if not cli.is_file():
        say("Устанавливаем LM Studio llmster под отдельным пользователем")
        with tempfile.NamedTemporaryFile() as install:
            run("curl", "-fsSL", "--proto", "=https", "--max-time", "120",
                "-o", install.name, "https://lmstudio.ai/install.sh")
            os.chown(install.name, uid, -1)
            run(*command, "bash", install.name, stdin=subprocess.DEVNULL)
    cached = lm_cache_present(model)
    if not profile.exists() or not cached:
        # A retained role file is not proof the model survived a partial deletion.
        # Never take over an unrelated server listening on the model endpoint.
        try:
            request("http://127.0.0.1:1234/v1/models")
        except OSError:
            pass
        else:
            raise ValueError("Порт 1234 уже занят сервером моделей.")
        run(*command, str(cli), "daemon", "up")
        try:
            if not cached:
                say(f"Скачиваем модель {model}")
                run(*command, str(cli), "get", model + "@q4_k_m", "--gguf", "--yes", stdin=subprocess.DEVNULL)
        finally:
            run(*command, str(cli), "daemon", "down")
        atomic(profile, role + "\n")
    unit = f"""[Unit]
Description=MysticEgo LM Studio model worker
After=network-online.target
Wants=network-online.target
[Service]
Type=oneshot
RemainAfterExit=yes
User=mysticego-lm
Environment=HOME={LM_HOME}
ExecStartPre={cli} daemon up
ExecStartPre={cli} load {model} --identifier {alias} --context-length {context} --yes
ExecStart={cli} server start --port 1234 --bind 127.0.0.1
ExecStop={cli} daemon down
TimeoutStartSec=300
[Install]
WantedBy=multi-user.target
"""
    atomic(Path("/etc/systemd/system/mysticego-lmstudio.service"), unit, 0o644)
    run("systemctl", "daemon-reload")
    run("systemctl", "enable", "--now", "mysticego-lmstudio.service")
    wait("http://127.0.0.1:1234/v1/models")
    payload = json.loads(request("http://127.0.0.1:1234/v1/models"))
    if alias not in {row.get("id") for row in payload.get("data", [])}:
        raise ValueError("API LM Studio не вернул ID выбранной модели.")
    say(f"LM Studio готов на 127.0.0.1:1234, модель {alias}.")
    say("Файлы GGUF можно перенести и импортировать через lms import.")


def model_connect(data):
    role = data.get("role") or "panel-data"
    if role not in MODELS:
        raise ValueError("Выберите профиль модели.")
    url = urllib.parse.urlsplit(data.get("url") or "http://127.0.0.1:1234")
    if url.scheme != "http" or url.hostname != "127.0.0.1" or not url.port or url.path not in ("", "/") or (
            url.username or url.password or url.query or url.fragment):
        raise ValueError("Укажите http://127.0.0.1:PORT. Для отдельного сервера используйте SSH-туннель.")
    address = f"http://127.0.0.1:{url.port}"
    alias = MODELS[role][1]
    try:
        models = json.loads(request(address + "/v1/models")).get("data", [])
        if not any(item.get("id") == alias for item in models):
            raise ValueError("LM Studio не загрузил модель " + alias)
        original_node = json.loads(request("http://127.0.0.1:5500/health/live"))["node"]
    except OSError:
        raise ValueError("Не удалось подтвердить доступ к Orchestra и серверу модели.") from None
    config = Path("/etc/mysticego/orchestra.env")
    original = config.read_text()
    key = "WorldAI__RemoteWorkers__" + ("World" if role == "world" else "Panels") + "__"
    lines = [line for line in original.splitlines() if not line.startswith(key)]
    lines.extend((key + "Url=" + address, key + "Model=" + alias, key + "Role=" + role, key + "Slots=1"))
    updated = "\n".join(lines) + "\n"
    if updated == original:
        say(f"Модель {alias} уже подключена.")
        return
    atomic(config, updated)
    try:
        run("systemctl", "restart", "mysticego-orchestra.service")
        wait("http://127.0.0.1:5500/health/live")
        if json.loads(request("http://127.0.0.1:5500/health/live"))["node"] != original_node:
            raise ValueError("После перезапуска изменился ID Orchestra.")
    except (OSError, ValueError, KeyError, subprocess.SubprocessError):
        atomic(config, original)
        run("systemctl", "restart", "mysticego-orchestra.service")
        raise ValueError("Orchestra не прошла проверку. Прежние настройки восстановлены.") from None
    say(f"Модель {alias} подключена к Orchestra. Роль: {role}.")


def install_runner(_):
    for name in ("install.sh", "install.py", "run-web.sh"):
        if not Path(name).is_file() or Path(name).is_symlink():
            raise ValueError("Обновите веб-установщик из CloudDesk.")
    target = ROOT / "runner"
    target.mkdir(parents=True, exist_ok=True)
    for name in ("install.sh", "install.py", "run-web.sh"):
        atomic(target / name, Path(name).read_text(), 0o700)
    shutil.copytree("monitoring", target / "monitoring", dirs_exist_ok=True)
    say("Веб-установщик подготовлен. Добавьте ограниченный SSH-ключ и имя сервера в Orchestra.")


def main():
    data = json.loads(Path("request.json").read_text())
    actions = {"docker": lambda _: docker(), "metrics-source": metrics_source, "monitoring": monitoring,
               "postgres": postgres, "postgres-native": postgres, "prepare-postgres": prepare_postgres, "auth-postgres": auth_postgres, "lmstudio": lmstudio, "model-connect": model_connect, "web-runner": install_runner}
    action = actions.get(data.get("component"))
    if not action:
        raise ValueError("Неизвестный компонент.")
    action(data)


if __name__ == "__main__":
    try:
        main()
    except ValueError as error:
        say(str(error))
        raise SystemExit(1)
    except (OSError, subprocess.SubprocessError):
        say("Шаг установки завершился ошибкой. Проверьте журнал выбранной службы на VPS.")
        raise SystemExit(1)
