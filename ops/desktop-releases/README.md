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

For new versioned installs of CloudDesk, AgentDesktop, AuditDesktop and LocalDesktop on Linux/Windows x64, the main Electron process queries public GitHub release metadata after startup and every 30 minutes. No MysticEgo session, GitHub login or token is used. The exact platform/product installer in the same beta channel is downloaded while the application runs, with a bounded size and SHA-256 verification against the GitHub asset digest; only GitHub release redirect hosts are accepted. The cached verified installer is reused. AgentDesktop and LocalDesktop install and restart without a MysticEgo confirmation dialog, after critical local operations finish; their existing enrolled nodes restart through the normal startup path. AuditDesktop and CloudDesk require explicit confirmation and display the selected GitHub release notes before installing. Installation is side by side, user profiles remain external, and an orderly application exit precedes launching the new build. No administrator elevation or disabling of the sandbox. Linux can still require normal AppArmor approval for a new versioned Electron path. Older installers and ZIP/source deployments do not contain the updater and require a one-time installation of an updated package. No Authenticode or independent release signature is introduced; trust remains the official public GitHub repository and HTTPS. Versioned legacy shortcuts still exist; use the new installation after switching.

## Release highlights

AgentDesktop, LocalDesktop and AuditDesktop show a dismissible card for the installed tag; the updater saves the selected release notes before switching, and individual installers embed the exact release markdown for offline/manual installations. Acknowledgement is stored in the product's external user-data directory. Notes are rendered as text. For all five web panels, the production build embeds the tag and markdown from docs/releases/beta<beta>-r<release>.md matching the five release-info.json files. The card is visible without login; dismissal is remembered per panel in the browser. An already open tab detects a changed served tag and offers a page reload rather than interrupting work. Keep release markdown current and aligned with the release tag. Dev-only builds do not show release announcements. Linux OS sandbox/permission prompts cannot be suppressed safely. This change does not create or publish a new version.
