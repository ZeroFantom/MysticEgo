# Отдельные установщики MysticEgo

Каждый продукт поставляется одним файлом: MysticEgo-<Product>-win-x64.exe (NSIS) или MysticEgo-<Product>-linux-x64.run (самораспаковывающийся Bash-установщик). Product: AuditDesktop, AgentDesktop, LocalDesktop, CloudDesk. Аватар-ассистент остаётся частью приложения. Старые ZIP и режим разработки сохранены.

## Windows
Запустите EXE, примите лицензию, дождитесь установки, откройте приложение через меню «Пуск». Версии устанавливаются раздельно в LocalAppData. Установщики не подписаны: SmartScreen может предупреждать. Удаление через список приложений не удаляет пользовательские профили.

## Linux x86_64
```bash
bash MysticEgo-AuditDesktop-linux-x64.run
```
Повторный запуск того же файла открывает установленное приложение. --install-only устанавливает без запуска; --check проверяет SHA-256 вложенного архива и извлекает его во временный каталог без запуска. Установка: ~/.local/share/mysticego/apps/<Product>/<Version> (или XDG_DATA_HOME). В установленном каталоге есть launch.sh. Это RUN, не AppImage; FUSE не нужен. Нужны системные библиотеки Electron и графическая сессия. Песочница не отключается; Ubuntu может потребовать настройку AppArmor. Не запускайте от root.

.NET и Electron входят в комплект. Windows 10/11 x64: CloudDesk использует системный OpenSSH, если присутствуют все клиентские утилиты, иначе комплектный официальный portable OpenSSH (preview); GitHub CLI также входит в Windows-комплект. WSL не нужен. В Linux нужны GitHub CLI, OpenSSH, secret-tool и терминал Konsole, GNOME Terminal или x-terminal-emulator. Сам установщик не меняет hosts, SSH, DNS или серверы; приложение может запросить отдельную настройку при запуске. Функциональность на реальных VPS и физических Windows 10/11 требует отдельной проверки.

## Интерактивная среда AgentDesktop и AuditDesktop

В экспериментальной ветке [PR #72](https://github.com/ZeroFantom/MysticEgo/pull/72) запуск AgentDesktop/AuditDesktop ограничен Windows и полноценной графической сессией Linux X11; Wayland и XWayland не поддерживаются. В KDE выберите Plasma (X11) на экране входа. Это не ограничение headless-сервисов или CloudDesk. Локальный исполнитель MCP пока настраивается отдельно; упаковка в установщики и проверка на физических машинах остаются задачами перед релизом. См. [Computer Use](../../tools/MysticEgo.UiAutomation/README.md) и [VRM handoff](../../docs/local-desktop-handoff.md).

## Release CI
Сначала собираются серверный архив и резервные ZIP, затем отдельные установщики. Linux payload проверяется при сборке; EXE собирается NSIS и проверяется сигнатура MZ. SHA256SUMS-desktop.txt публикуется вместе с релизом. Старые клиенты получают ZIP по ключам windows/linux, новые — индивидуальные ключи audit-windows и т. п.

## Единый CloudDesk

CloudDesk включает локальный центр Auth/Orchestra. Отдельный ControlDesktop больше не собирается. Старые GUI launchers полного комплекта перенаправляют в CloudDesk; серверный `mysticego --headless` остаётся совместимым. Локальный запуск не требует GitHub или Timeweb. Данные старого центра остаются в прежнем каталоге. Исторические релизы не удалены.
