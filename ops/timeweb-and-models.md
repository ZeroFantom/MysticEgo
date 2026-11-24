# Timeweb и внешние модели LM Studio

## Привязка Global к Timeweb

Добавьте настройки в защищённый файл окружения службы Orchestra.
Для каждого Global используйте UUID из панели узлов.

```text
Timeweb__Token=<токен с доступом к VPS>
Timeweb__OrchestraServerId=<ID VPS самой Orchestra>
Timeweb__Globals__<UUID Global>__ServerId=<ID VPS Global>
Timeweb__Globals__<UUID Global>__Ipv4=<публичный IPv4 VPS Global>
```

Перезапустите Orchestra, чтобы загрузить настройки. В **Узлы сети → Global → VPS Timeweb**
главный разработчик и утверждённые участники команды увидят состояние VPS.
Доступны запуск, штатное выключение и перезагрузка с подтверждением.
Для каждой операции сервер сверяет роль узла, ID VPS и IPv4 с Timeweb и DNS.
Для VPS самой Orchestra операции исключены.
Контракт API описан в [официальном SDK Timeweb](https://github.com/timeweb-cloud/sdk-go/blob/main/docs/ServersAPI.md).

## Отдельный сервер LM Studio

Запустите сервер LM Studio на удалённой машине и загрузите нужную модель.
Используйте частную сеть или VPN. Разрешите доступ к порту LM Studio с VPS Orchestra
и укажите точный ID модели из списка `/v1/models`.

```text
WorldAI__RemoteWorkers__WorldA__Url=http://10.0.0.10:1234
WorldAI__RemoteWorkers__WorldA__Model=<ID мировой модели>
WorldAI__RemoteWorkers__WorldA__Role=world
WorldAI__RemoteWorkers__WorldA__Slots=2
WorldAI__RemoteWorkers__PanelA__Url=http://10.0.0.11:1234
WorldAI__RemoteWorkers__PanelA__Model=<ID модели для панелей>
WorldAI__RemoteWorkers__PanelA__Role=panel-data
WorldAI__RemoteWorkers__PanelA__Slots=2
```

Если LM Studio требует ключ, задайте отдельный `Token` в секции соответствующей модели.
Добавляйте узлы через новые имена секций. Модели для вычислений можно разнести
по разным машинам или совместить на одном сервере. Для настройки сервера
используйте права администратора, а имена и ключи храните вне репозитория.

В **Модели LM Studio** главный разработчик видит занятость пула и может
выполнить ручной запрос. Роль `panel-data` обрабатывает переданные запросом данные
без автоматического чтения источников и без добавления знаний мира к системному
сообщению. Этот запрос не меняет записи в панелях. Существующее API очереди
принимает ту же роль.

Загрузка LM Studio, моделей и конфигурация VPN выполняются на выделенных серверах.
