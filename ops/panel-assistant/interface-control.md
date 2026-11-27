# MysticEgo Interface Control — first stage

## Web (Orchestra, Global, Local, Agent, Audit)
Open an authenticated panel in a real browser. Use the MCP control button in the top bar and confirm a ten-minute session. The visible tab polls its own node API. An external MCP client authenticates with the same user identity at `/api/v1/interface-control/mcp` (Streamable HTTP, JSON response). Configure the exact panel origin and an access token through the MCP client's secret store. No bearer token is placed in the URL.

Tools: interface_sessions, interface_inspect, interface_navigate, interface_assert_view. Select a session from the list, inspect available controls and navigate using a path from the observed controls, then inspect or assert the observed route. A queued request is not a completed action. Only marked navigation links and allowlisted assistant tabs are supported. Three-segment infrastructure routes are supported; arbitrary query strings are never exposed. The visible cursor is an in-page visualization, not an OS mouse; the original React link is clicked. Escape, closing or hiding the tab, losing heartbeat, or expiry revokes the session. No arbitrary selector, code, credentials, forms, or direct management commands.

For Orchestra's developer chat, enable the same control and explicitly request «Открой мне страницу обзора». The existing LM Studio integration emits a view and the UI clicks an actual marked link. If there is no permission or link, the normal confirmation link remains. This is separate from an external MCP client's tool calling.

## Desktop
CloudDesk, LocalDesktop and Audit/Agent launcher windows: menu MCP → allow control. The application creates a loopback-only HTTP MCP service with a random short-lived bearer token. Copy the client configuration locally. Use desktop_inspect, desktop_click with a marked target ID. The existing Electron window receives mouse input and provides a cursor indicator. Only safe navigation or disclosure controls are marked; no forms, deployment, shutdown, secrets or system-wide input. Escape, hiding, minimizing, navigation or expiry stops the service. Embedded authenticated web panels can use their own web MCP session.

This does not expose a public desktop server. A remote LM Studio on a VPS cannot reach the desktop's loopback; use a local MCP host/client. Do not expose the port or share the temporary token.

## Tests and limitations
Service smoke covers cross-account isolation, queue limits, invalid routes, observed navigation and revocation; browser smoke uses the real UI with mock server responses. Native mouse input is implemented in Electron but needs a real Windows/Linux interactive test. No full Playwright headed browser runner, OS-wide mouse/keyboard, screen capture, arbitrary forms or automatic destructive testing in this first stage. Add isolated test accounts and explicit allowlisted controls before broader tests.

## R3.2.42 additions

Desktop MCP adds `desktop_update_status`, `desktop_update_check` and `desktop_update_apply`. The last tool always requires a separate native confirmation for the current verified candidate. Its response is a receipt, not an installation result. CloudDesk adds `clouddesk_ssh_status` (stage/result only), and marked diagnostic report download controls for the current selected node. Reports are saved locally, not returned as tool content. Factory reset is deliberately not exposed as a callable destructive MCP action: the domain phrase and native confirmation remain manual. Web navigation includes actual assistant tabs and nested infrastructure graphs; no arbitrary query or secrets. Escape and existing lease rules still apply.
