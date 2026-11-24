# Разработка MysticEgo

Стабильные сборки выпускаем из `main`. Изменения начинаем от `develop` в ветках `feature/<задача>` и `fix/<задача>`. Готовый набор изменений переносим в `main` через pull request.

## Проверка изменений

- Для .NET запустите `dotnet build MysticEgo.Distributed.slnx`.
- Для веб-интерфейса запустите `npm ci --prefix frontend` и `npm run build --prefix frontend`.
- В pull request укажите результат проверки и образы Windows или Linux, на которых запускали сборку.
