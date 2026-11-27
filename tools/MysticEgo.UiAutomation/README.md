# MysticEgo UI Automation (experimental)

Локальный исполнитель UI для Orchestra, LM Studio и MCP-клиентов. Не заменяет существующий API помощника. Отдельный процесс работает **в графическом сеансе пользователя**, а не как systemd-служба на VPS или Windows Session 0. Node.js выбран для общего Playwright-драйвера Chromium и экспериментального Electron API. Серверный C# API Orchestra пока не проксирует эти команды.

## Запуск

1. В этом каталоге установите зависимости: `npm install`, затем `npx playwright install chromium` (Linux может требовать системные зависимости).
2. Скопируйте config.example.json в личный конфигурационный файл вне репозитория. Разрешайте только тестовые origin и принадлежащие вам Electron-приложения. Для desktop укажите, например, `"apps": {"audit": {"executablePath": "/absolute/path/to/electron", "args": ["/absolute/path/to/app"]}}`. Реальный путь и параметры зависят от сборки.
3. В LM Studio откройте mcp.json, добавьте локальный сервер (абсолютные пути):

```json
{
  "mcpServers": {
    "mysticego-ui": {
      "command": "node",
      "args": ["/absolute/path/to/tools/MysticEgo.UiAutomation/server.mjs", "--stdio"],
      "env": {"MYSTICEGO_UI_CONFIG": "/absolute/path/to/ui-config.json"}
    }
  }
}
```

В режиме stdio отдельный токен UI не нужен: процесс запускает сам MCP-клиент. Не отключайте авторизацию API LM Studio. Используйте модель с поддержкой вызова инструментов.

## HTTP / REST

Установите MYSTICEGO_UI_CONFIG и MYSTICEGO_UI_TOKEN (случайный секрет длиной от 32 символов), запустите `npm start` в графическом сеансе. MCP: `http://127.0.0.1:1247/mcp` (Streamable HTTP); REST: `POST /api/v1/ui/{operation}`. Заголовок Authorization: Bearer <secret> обязателен для обоих. Сервис не слушает внешнюю сеть и не разрешает browser Origin. Не публикуйте его через reverse proxy. HTTP и stdio — альтернативные режимы запуска.

| Tool | Purpose |
| --- | --- |
| ui_targets | List permitted panels and apps |
| ui_open | Open visible Chromium or configured Electron window |
| ui_snapshot | Read accessibility tree and URL |
| ui_screenshot | Capture current page/window |
| ui_click / ui_fill | Interact with exact accessible role and name |
| ui_scroll | Scroll window |
| ui_navigate | Navigate browser to allowed origin |
| ui_assert_visible | Wait for element and verify visibility |
| ui_close | End controlled session |

All operations except targets/open require the returned sessionId. One active window session per executor, serial actions. Example: ask the assistant to open Orchestra, inspect the UI, click Overview using its actual accessible name and verify the heading. Authentication is done manually in the new, isolated browser context. Do not send passwords or production secrets to the model. UI text is untrusted, not instructions for the model. Launch only while supervising; click and fill can change application state. Stop the local process to end automation. The origin allowlist is not a network sandbox. Use test accounts and staging instances, not admin production sessions.

## Tests and current limits

Run `npm test` for mocked session and allowlist unit tests. Actual Chromium/Electron launches, MCP interoperability with LM Studio, installed desktop packaging and cross-platform behaviour require interactive validation. Electron driver controls renderer content; native OS dialogs and other non-Electron desktop apps are not covered. No arbitrary JavaScript, shell commands or remote workstation access are exposed.

Next integration: authenticated Orchestra relay paired by local user, in-panel assistant tool dispatch, local stop/control UI, explicit approval for mutating flows, screenshots/traces and desktop/native adapters. Do not route the public web panel directly to the loopback REST endpoint.
