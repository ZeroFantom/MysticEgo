# CloudDesk — Windows 10/11 и Linux x64

Скачайте отдельный установщик из GitHub Releases. Windows: EXE, Linux: bash MysticEgo-CloudDesk-linux-x64.run. ZIP-комплект также поддерживается.

Windows использует комплектный .NET/Electron, GitHub CLI и клиентские OpenSSH-инструменты (системный клиент предпочтителен). Нет установки SSH-сервера, WSL, изменения PATH системы или обязательной записи hosts. При необходимости приватного DNS-override вручную запустите ensure-hosts.ps1 с проверкой адреса. Хранилище токена Timeweb — DPAPI CurrentUser, зашифрованный файл timeweb.dpapi. SSH-ключи и known_hosts защищены ACL текущего пользователя. Новый каталог: LocalAppData/MysticEgo/CloudDesk; существующий ~/.local/share/mysticego-clouddesk сохраняется. GitHub CLI использует системное хранилище учётных данных.

Linux: прежнее secret-tool, chmod 600/700 и каталог ~/.local/share/mysticego-clouddesk; доступны Konsole, GNOME Terminal, x-terminal-emulator. AppArmor при необходимости настраивается существующим способом. Проверка ключа сервера обязательна на обеих ОС. Пароль SSH передаётся непосредственно SSH.NET, не в командной строке или временном файле.

Разработка: npm install --ignore-scripts --include=dev --include=optional; npm run build; dotnet publish backend/CloudDesk.csproj -c Release -o out. Требуется SDK 10. Для запуска нужен комплект runtime. CI проверяет локальные криптографические операции, ACL/chmod, генерацию ключа, SSH-конфигурацию и регрессии IPC на Windows и Linux. Живые платные операции не выполняются в CI.
