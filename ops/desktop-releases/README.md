# Отдельные установщики MysticEgo

Каждый продукт поставляется одним файлом: MysticEgo-<Product>-win-x64.exe (NSIS) или MysticEgo-<Product>-linux-x64.run (самораспаковывающийся Bash-установщик). Product: AuditDesktop, AgentDesktop, LocalDesktop, CloudDesk. Аватар-ассистент остаётся частью приложения. Старые ZIP и режим разработки сохранены.

## Windows
Запустите EXE, примите лицензию, дождитесь установки, откройте приложение через меню «Пуск». Версии устанавливаются раздельно в LocalAppData. Установщики не подписаны: SmartScreen может предупреждать. Удаление через список приложений не удаляет пользовательские профили.

## Linux x86_64
Из папки загрузки (замените название на нужный продукт):
```bash
cd ~/Downloads
bash ./MysticEgo-CloudDesk-linux-x64.run --check
bash ./MysticEgo-CloudDesk-linux-x64.run
```
Браузеры обычно сохраняют файлы без executable-bit. Прямой запуск `./MysticEgo-CloudDesk-linux-x64.run` требует `chmod u+x` и файловой системы без `noexec`. `sudo` не исправляет отсутствие права выполнения; установщик запрещает запуск от root. Предпочтительный способ — `bash ./...run`. Если даже после распаковки приложение не запускается, проверьте `findmnt -T "${XDG_DATA_HOME:-$HOME/.local/share}" -o TARGET,FSTYPE,OPTIONS` и `ls -l` установленного Electron. Не отключайте sandbox или ограничения ОС. Проверяйте SHA256SUMS-desktop.txt из доверенного релиза; `--check` проверяет встроенный payload, но не удостоверяет автора установщика.
Повторный запуск того же файла открывает установленное приложение. --install-only устанавливает без запуска; --check проверяет SHA-256 вложенного архива и извлекает его во временный каталог без запуска. Установка: ~/.local/share/mysticego/apps/<Product>/<Version> (или XDG_DATA_HOME). В установленном каталоге есть launch.sh. Это RUN, не AppImage; FUSE не нужен. Нужны системные библиотеки Electron и графическая сессия. Песочница не отключается; Ubuntu может потребовать настройку AppArmor. Не запускайте от root.

.NET и Electron входят в комплект. Windows 10/11 x64: CloudDesk использует системный OpenSSH, если присутствуют все клиентские утилиты, иначе комплектный официальный portable OpenSSH (preview); GitHub CLI также входит в Windows-комплект. WSL не нужен. В Linux нужны GitHub CLI, OpenSSH, secret-tool и терминал Konsole, GNOME Terminal или x-terminal-emulator. Сам установщик не меняет hosts, SSH, DNS или серверы; приложение может запросить отдельную настройку при запуске. Функциональность на реальных VPS и физических Windows 10/11 требует отдельной проверки.

## Release CI
Сначала собираются серверный архив и резервные ZIP, затем отдельные установщики. Linux payload проверяется при сборке; EXE собирается NSIS и проверяется сигнатура MZ. SHA256SUMS-desktop.txt публикуется вместе с релизом. Старые клиенты получают ZIP по ключам windows/linux, новые — индивидуальные ключи audit-windows и т. п.

## Единый CloudDesk

CloudDesk включает локальный центр Auth/Orchestra. Отдельный ControlDesktop больше не собирается. Старые GUI launchers полного комплекта перенаправляют в CloudDesk; серверный `mysticego --headless` остаётся совместимым. Локальный запуск не требует GitHub или Timeweb. Данные старого центра остаются в прежнем каталоге. Исторические релизы не удалены.

## Anonymous in-app updates

For new versioned installs of CloudDesk, AgentDesktop, AuditDesktop and LocalDesktop on Linux/Windows x64, the main Electron process queries public GitHub release metadata after startup and every 30 minutes. No MysticEgo session, GitHub login or token is used. The exact platform/product installer in the same beta channel is downloaded while the application runs, with a bounded size and SHA-256 verification against the GitHub asset digest; only GitHub release redirect hosts are accepted. The cached verified installer is reused. The user confirms installation and restart, so active work is not interrupted automatically. Installation is side by side, user profiles remain external, and an orderly application exit precedes launching the new build. No administrator elevation or disabling of the sandbox. Linux can still require normal AppArmor approval for a new versioned Electron path. Older installers and ZIP/source deployments do not contain the updater and require a one-time installation of an updated package. No Authenticode or independent release signature is introduced; trust remains the official public GitHub repository and HTTPS. Versioned legacy shortcuts still exist; use the new installation after switching.
