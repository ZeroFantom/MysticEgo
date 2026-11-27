# MysticEgo UI Automation (experimental)

Локальный исполнитель UI для Orchestra, LM Studio и MCP-клиентов. Не заменяет существующий API помощника. Отдельный процесс работает **в графическом сеансе пользователя**, а не как systemd-служба на VPS или Windows Session 0. Node.js выбран для общего Playwright-драйвера Chromium и экспериментального Electron API. C# API Agent/Audit проксирует команды локальному исполнителю; Orchestra пока нет.

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

Установите MYSTICEGO_UI_CONFIG и MYSTICEGO_UI_TOKEN (случайный секрет длиной от 32 символов), запустите `npm start` в графическом сеансе и вручную введите ALLOW. Разрешение действует максимум 30 минут; Ctrl+C останавливает исполнитель. MCP: `http://127.0.0.1:1247/mcp` (Streamable HTTP); REST: `POST /api/v1/ui/{operation}`. Заголовок Authorization: Bearer <secret> обязателен для обоих. Сервис не слушает внешнюю сеть и не разрешает browser Origin. Не публикуйте его через reverse proxy. HTTP и stdio — альтернативные режимы запуска.

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

Next integration: authenticated Orchestra relay paired by local user, in-panel assistant tool dispatch, local stop/control UI, explicit approval for individual mutating flows, traces and Linux native adapters. Do not route the public web panel directly to the loopback REST endpoint.

## Agent / Audit (C#)

On the local Agent or Audit node, set `UiAutomation__Enabled=true`, `UiAutomation__Owner` to the exact owner identity resolved by the existing assistant policy, and `UiAutomation__LocalToken` to the same local secret as the executor. Keep the secret outside source control. Launch the HTTP executor interactively as described above. Existing Audit permissions remain in force. Clients need an authenticated owner account and a loopback connection; device tokens, forwarded requests and browser Origins are denied. Existing `POST /mcp` includes UI tools when the local executor is reachable; dedicated endpoints are `POST /api/v1/managed/ui-automation/mcp` and `POST /api/v1/managed/ui-automation/actions/{operation}`. Do not publish them externally. On a shared machine, only a locally authorized owner should start the executor; do not run it in Session 0, as root or as administrator.

## Native Windows windows, browser and permitted games

Build `dotnet publish native/UiAutomation.Native.csproj -c Release -r win-x64 --self-contained false`. Configure the absolute path to the resulting trusted EXE and exact allowed process names (without .exe) locally:
```json
{
  "origins": ["https://orchestra.isdarkline.ru"],
  "apps": {},
  "native": {
    "executablePath": "C:/local/UiAutomation.Native.exe",
    "processes": ["chrome", "firefox", "notepad"],
    "allowPointerSpeed": false,
    "vendorApps": {}
  }
}
```
Use HTTP mode; native operations are disabled in stdio. Select the window manually, then call native_attach, native_snapshot, native_click or native_key. Focus changes abort subsequent operations. Native screenshot is a capture of the visible client rectangle and may include overlays; never use it for sensitive windows. It is not a protected-window capture mechanism. No hidden or elevated input, lock screen/UAC access, anti-cheat bypass, or game memory access. Use games only where automation is permitted, e.g. an offline test environment. Exclusive fullscreen/raw-input titles may reject simulated input. Windows pointer speed (1–20) is separate from mouse hardware DPI and in-game sensitivity; native_pointer_get/set requires explicit local config permission.

The existing AutomationRecorder records user clicks (not keystrokes) into a client-coordinate scenario. Native replay accepts that JSON via native_replay_clicks, with an attached window and a 20-click/30-second limit in this prototype. The existing recorder and automation.play also remain available. ui_record_start/stop/replay record assistant click, scroll and assertion steps in memory in the same Playwright session; no typed text is retained and the recording is cleared on close. Use ui_scenario_export to obtain versioned JSON and save it manually in a local scenario file; use ui_scenario_import in a new session opened with the same initial URL or configured Electron app ID. Import validates the exact target, schema, action types, timing and maximum of 20 steps; it does not execute the scenario. Review scenario data before saving or replay: accessible names and initial URLs can contain private data. Use only test targets and URLs without credentials or secret query parameters. ui_replay_cancel interrupts a running replay before the next action (an already-running Playwright operation can complete); cancellation is not rollback. Persistent scenario library UI and user-driven browser DOM recording are not yet implemented.

## Manufacturer applications

Configure exact installed executable paths in native.vendorApps using keys nvidia, logitech and msicenter; then native_vendor_launch opens the corresponding app. Add its actual UI process name to native.processes. Use the manufacturer's normal update screen, with explicit local confirmation before install/reboot; the tool does not implement automatic driver installation, version detection or device DPI configuration. Existing Agent VendorInstallationService remains unchanged. The new tool does not execute arbitrary installer commands or bypass elevation.

## Verification

Windows build and Linux Chromium smoke are defined in GitHub Actions; actual Windows interactive input, OEM application updates, local authentication and LM Studio interoperability must be verified on a test machine before release. Native pointer speed changes are global for the current user and can be restored via the same tool.
