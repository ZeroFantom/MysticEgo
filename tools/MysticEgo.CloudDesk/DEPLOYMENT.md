# CloudDesk 0.4.7 и развёртывание Оркестра

На вкладке «Развёртывание» кнопка «Проверить релиз GitHub» показывает тег, имя ZIP и SHA‑256. Она обращается только к GitHub.

CloudDesk ожидает опубликованный предварительный релиз `v1.0.0-beta.7-r3.1` приватного репозитория `ZeroFantom/MysticEgo` и вложение `MysticEgo_BETA7_R3.1_CloudDesk.zip`. GitHub CLI скачивает ZIP на рабочем компьютере. CloudDesk сверяет SHA‑256 по метаданным вложения GitHub, затем отправляет ZIP и установщик на VPS через SCP. Установщик проверяет SHA‑256 повторно.

Для существующего VPS выберите его в разделе «Инфраструктура». Требуются Ubuntu Server 24.04 x86‑64, доступ root по SSH с проверенным ключом сервера, публичный IP, DNS имени Оркестра и доступные порты 80/443 для Caddy. Установщик рассчитан на чистый VPS. При частичной установке сохраните данные и изучите журнал перед повтором.

Проверка ядра из корня решения

```bash
dotnet test core/TestMagicCommands/TestMagicCommands.csproj -c Release
dotnet build tools/MysticEgo.CloudDesk/backend/CloudDesk.csproj -c Release
```
