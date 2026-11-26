# MysticEgo

![Иллюстрация компьютерной площадки MysticEgo](frontend/packages/ui/src/pages/media/club-editorial.webp)

Платформа с доступным исходным кодом для управления распределёнными рабочими станциями. MysticEgo связывает центральную панель, организации, площадки и компьютеры, чтобы видеть их состояние, обновлять компоненты и выполнять разрешённые операции из одного интерфейса.

[Скачать релиз](https://github.com/ZeroFantom/MysticEgo/releases) · [Сайт студии](https://orchestra.isdarkline.ru/studio) · [Профиль разработчика](https://orchestra.isdarkline.ru/developer) · [Сообщить о проблеме](https://github.com/ZeroFantom/MysticEgo/issues)

## Выбор приложения

| Приложение | Для чего | Windows x64 | Linux x64 |
| --- | --- | --- | --- |
| AuditDesktop | Личный Audit и доступ к своим организациям | `AuditDesktop.exe` | `./audit-desktop` |
| AgentDesktop | Компьютер клуба, подключаемый по приглашению Local | `AgentDesktop.cmd` | `./agent-desktop` |
| LocalDesktop | Запуск Local на площадке с профилем из Global | `LocalDesktop.cmd` | `./local-desktop` |
| CloudDesk | Развёртывание и обновление серверов, диагностика инфраструктуры | `tools\MysticEgo.CloudDesk\run-windows.cmd` | `bash tools/MysticEgo.CloudDesk/run.sh` |

Для первых трёх приложений скачайте **MysticEgo-Desktop** для своей ОС, распакуйте ZIP целиком и запустите нужный файл. На странице входа Orchestra есть такой же каталог приложений.

Площадку создают в Global. LocalDesktop получает её конфигурацию от Global и запускает Local. Администратор выпускает приглашение в панели Local, а AgentDesktop получает профиль станции по сети площадки. Для личного использования можно начать с AuditDesktop.

## Как устроен проект

| Компонент | Ответственность |
| --- | --- |
| Orchestra | Центральный реестр, доступ к узлам, развёртывание и обновления |
| Global | Организация и её площадки |
| Local | Площадка, выпуск приглашений и связь с рабочими станциями |
| Agent | Регистрация станции, диагностика и разрешённые действия на ней |
| Audit | Личный профиль и подключение к организациям |
| Auth | Учётные записи, токены и управление доступом |

Сервисы написаны на C# / .NET 10. Веб-панели используют TypeScript, React и Vite, настольные интерфейсы — Electron. Точки входа находятся в `services/`, `frontend/apps/` и `desktop/`. Общие модели и инфраструктурный код — в `core/`, `shared/` и `services/MysticEgo.Distributed/`.

## Разработка

Понадобятся .NET 10 SDK и Node.js 24. Из корня репозитория:

```bash
dotnet build MysticEgo.Distributed.slnx
npm ci --prefix frontend
npm run build --prefix frontend
```

Для отдельной разработки интерфейса Orchestra используйте `npm run dev:orchestra --prefix frontend`. Vite запускает интерфейс, а для входа потребуется работающий API. Остальные команды перечислены в [frontend/package.json](frontend/package.json).

[Как внести изменения](CONTRIBUTING.md) · [Развёртывание](ops/infrastructure/README.md) · [Обновление Orchestra](ops/orchestra-updater/README.md) · [Ассистент](ops/panel-assistant/README.md) · [История развёртывания](docs/operations-notes.md)

## О студии

MysticEgo развивается как инфраструктурный проект с доступным исходным кодом. Параллельно студия исследует концепцию **Ether And Chaos** — фэнтезийный мир с самостоятельными персонажами и последствиями решений игроков. Текущий этап инфраструктуры и игровой замысел описаны на [странице студии](https://orchestra.isdarkline.ru/studio).

Там же указано, чем можно помочь проекту и как предложить сотрудничество, рекламу или обсудить финансирование разработки.

## Лицензия

Код текущей редакции, принадлежащий DarkLie:Zero, доступен по [PolyForm Noncommercial 1.0.0](LICENSE). Для коммерческого использования, которое не покрывает эта лицензия, требуется [отдельное письменное разрешение](docs/legal/commercial.md). У прежних версий под Apache 2.0 и сторонних компонентов сохраняются их лицензии.

[Задачи и обсуждения](https://github.com/ZeroFantom/MysticEgo/issues) · [Проверка документов регистрации](docs/legal/launch-review.md)
