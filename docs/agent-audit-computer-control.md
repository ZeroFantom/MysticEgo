# Agent / Audit interactive computer control

Status: architecture contract and experimental implementation in [PR #72](https://github.com/ZeroFantom/MysticEgo/pull/72); not a deployed capability. See the [implemented tool inventory and setup](../tools/MysticEgo.UiAutomation/README.md).

Agent and Audit share the local UI Automation runtime. Windows services must never inject input from Session 0; launch the interactive executor from the signed-in user's desktop bootstrap. Audit must keep its existing remote-command opt-in disabled by default. Agent organization roles do not substitute for local desktop consent.

## Session contract

- Explicit local approval of the authenticated requesting principal, device, target windows and capabilities; expiry, revocation and visible stop control.
- Broad interactive capability may be granted for a bounded session, not a permanent unrestricted remote endpoint.
- Separate scopes: observe, browser, desktop, game-test, record, replay, peripherals, vendor-update.
- No credential capture, hidden recording, arbitrary remote shell, bypass of secure desktop or anti-cheat. Treat UI content as untrusted.
- Installed browser: use user-approved dedicated automation profile or supported explicit browser attachment; do not silently attach to personal profiles or export cookies.
- Native actions require a local interactive adapter; Windows UI Automation/SendInput, Linux X11 accessibility, X11 MPX and KDE Plasma APIs. Wayland/XWayland are not supported. Games: supported input in owned/test environments; no process injection or anti-cheat bypass.
- Preserve local user input and emergency stop. Reject focus changes during replay. Privileged updates require independent local confirmation and OS elevation.

## Planned MCP surface

| Tool | Scope | Contract |
|---|---|---|
| computer_session_request | local consent | authenticated principal, device, capabilities, expiry |
| computer_session_status | session | granted scopes and revocation |
| computer_session_stop | session | cancel and release input |
| computer_windows | observe | permitted windows |
| computer_focus / computer_click / computer_key / computer_scroll | desktop | target window, bounded action |
| browser_attach | browser | explicit approved browser target |
| recording_start / recording_stop | record | visible, scoped recording; no secrets |
| recording_replay | replay | local review, target binding, cancellation |
| vendor_inventory / vendor_check_updates | observe | installed versions and official update availability |
| vendor_update | vendor-update | approved vendor package, independent elevation |
| mouse_settings_get / mouse_settings_set | peripherals | OS pointer speed or supported vendor device DPI |

These names are proposed contracts, not yet registered tools. Existing ui_*, native_* and desktop_* tools are implemented separately. A [local VRM handoff](local-desktop-handoff.md) is also prototyped, but is not a general native action router.

## Vendor integration

Reuse IVendorInstallationService and InstallationJobs in Api.Agent. Audit should expose the same shared provider interface with owner authorization, never forward an arbitrary download URL or installer arguments from MCP. NVIDIA: official signed packages appropriate to detected GPU and OS; G HUB and MSI Center: official supported update flows. Verify signatures, versions, exit status and restart requirements. Do not promise unattended installation if vendor does not provide a supported interface. OS pointer speed is distinct from hardware DPI and game sensitivity; use supported vendor/device interfaces for DPI. Report unsupported operations rather than simulate success.

## Recording

Reuse the existing Windows AutomationRecorder for explicit foreground-window mouse recording. The experimental executor has portable versioned semantic UI scenarios and a local library CLI; native replay accepts scoped recorded clicks. General user-input recording and graphical library integration remain incomplete. Do not record arbitrary keyboard streams. Text entry in a scenario must be explicitly authored with nonsecret placeholders. Replay must validate target, focus and bounds, support stop, and never run automatically after upload.

## Integration gates

1. Local-only executor and scenario validation tests.
2. Interactive bootstrap wiring for AgentDesktop and AuditDesktop, consent/stop UI, signed local IPC or paired outbound authenticated relay.
3. Native window adapter and safe game-test mode.
4. Vendor provider integration and physical-device test matrix.
5. Security review and interactive tests before enabling remote control.
