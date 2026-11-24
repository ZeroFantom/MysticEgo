# Запуск CloudDesk R3.1

Распакуйте полный архив MysticEgo в локальную папку. Готовая сборка рассчитана на Windows и Linux x64.

- Windows: запустите `tools\MysticEgo.CloudDesk\run-windows.cmd`.
- Linux: из корня проекта выполните `bash tools/MysticEgo.CloudDesk/run.sh`.
  Если AppArmor блокирует песочницу Electron, сценарий предложит единожды
  настроить профиль для пути к этому Electron с помощью sudo и повторит запуск.
- Добавьте `--check` к команде для проверки запуска .NET и версии Electron.

Для работы с исходниками соберите CloudDesk в его каталоге:

```sh
npm install --ignore-scripts --include=dev --include=optional
npm run build
dotnet publish backend/CloudDesk.csproj -c Release -o out
```

Для сборки нужен .NET SDK 10. Для запуска из Git нужны также комплектные
Electron и .NET из каталога runtime полного архива на той же платформе.
Встроенные действия, которые открывают терминал, используют Konsole.
