#!/usr/bin/env bash
set -euo pipefail
umask 077
[[ $EUID == 0 ]]
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
awk '/MemTotal:/{exit !($2>=3145728)}' /proc/meminfo
[[ $(df -Pk /opt | awk 'NR==2{print $4}') -ge 3145728 ]]
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends python3-venv ca-certificates
id mysticego-voice >/dev/null 2>&1 || useradd --system --user-group --create-home --home-dir /var/lib/mysticego-voice --shell /usr/sbin/nologin mysticego-voice
install -d -o root -g root -m 0755 /opt/mysticego-voice
install -d -o mysticego-voice -g mysticego-voice -m 0700 /var/lib/mysticego-voice
if [[ -f /etc/systemd/system/mysticego-voice.service ]]; then
  systemctl stop mysticego-voice.service
fi
python3 -m venv /opt/mysticego-voice/venv
/opt/mysticego-voice/venv/bin/pip install --disable-pip-version-check --no-input 'faster-whisper==1.1.1' 'requests>=2.32.4,<3'
/opt/mysticego-voice/venv/bin/python -m pip check
chmod -R a+rX /opt/mysticego-voice/venv
runuser -u mysticego-voice -- env HOME=/var/lib/mysticego-voice /opt/mysticego-voice/venv/bin/python -c 'import requests; from faster_whisper import WhisperModel'
install -m 0644 "$here/voice-server.py" /opt/mysticego-voice/server.py
cat > /etc/systemd/system/mysticego-voice.service <<'UNIT'
[Unit]
Description=MysticEgo local CPU speech transcription
After=network-online.target
Wants=network-online.target
StartLimitIntervalSec=900
StartLimitBurst=3
[Service]
User=mysticego-voice
Group=mysticego-voice
Environment=HOME=/var/lib/mysticego-voice
Environment=HF_HOME=/var/lib/mysticego-voice/cache
ExecStart=/opt/mysticego-voice/venv/bin/python /opt/mysticego-voice/server.py
Restart=on-failure
RestartSec=30
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/var/lib/mysticego-voice
MemoryMax=1500M
CPUQuota=150%
UMask=0077
[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl reset-failed mysticego-voice.service || true
systemctl enable --now mysticego-voice.service
