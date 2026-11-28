# MysticEgo Mobile · Android

Experimental first-party Android shell for existing responsive MysticEgo panels. Android 8+ (min API 26), compile/target API 36, Java 17, Android Gradle Plugin 8.13.2 / Gradle 8.13. No separate backend or account. The launcher opens the personal Audit portal; the explicit browser links can open Orchestra or a supported personal page.

## Build

Install JDK 17, Gradle 8.13 and Android SDK 36 / build-tools 36.0.0. Run from this directory:

```sh
gradle --no-daemon :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleRelease
```

CI publishes a short-lived **debug** APK artifact only. It uses the package ru.isdarkline.mysticego.debug, not the final package; debug can be installed and launched manually, but the production intent intentionally targets ru.isdarkline.mysticego. Release APK is unsigned until the signing process is configured. Do not distribute debug keys, request personal credentials in an unreviewed build, or present an unsigned/debug artifact as the official download.

## Mobile handoff

The shared browser panel offers a dismissible Android hint only on https://orchestra.isdarkline.ru, using local UA/platform detection (no new analytics). It never automatically launches the app or detects whether it is installed. The explicit Chrome intent names ru.isdarkline.mysticego, passes only a fixed route key, and falls back to the existing /desktop-downloads#android section if unsupported/not installed. Other browsers may handle intents differently; the installation link and normal browser remain available. For a debug-only test of route handling, install a trusted local build and invoke its package explicitly with adb:
```sh
adb shell am start -a android.intent.action.VIEW -d mysticego://open/audit-assistant ru.isdarkline.mysticego.debug
```

Private query strings, fragments, account/session tokens and arbitrary host URLs are not transferred. Unsupported routes go to the corresponding root. App sessions are independent of external browser sessions. The WebView does not expose a JavaScript bridge, native device actions, microphone/camera access from web content, file access or arbitrary external navigation. HTTPS and exact primary origin are required; SSL errors are cancelled. The server remains responsible for authorization. CAMERA is requested only for the explicitly opened native QR scanner; WebView camera and microphone permissions remain denied.

## Acceptance and distribution gates

- [ ] CI builds, lint and route tests pass; verify the debug build on actual Android hardware or emulator with updated WebView, using a dedicated test account.
- [ ] Test login/logout, session revocation, personal Audit and Orchestra access, back/reload, links when installed/not installed, Firefox/Chrome fallback, rotation and offline retry.
- [ ] Review trusted WebView content, resource origins and existing browser auth policy. Microphone, media upload, notifications, native/offline features and non-primary server support are intentionally deferred.
- [ ] Configure a dedicated protected signing identity and verified HTTPS Android App Links (Digital Asset Links) after the final certificate is available; custom scheme is not a verified universal link.
- [ ] Publish a verified signed APK and checksum via the official release channel, then update the installation card to use the verified artifact. No automatic publication or production deployment in this PR.
- [ ] Validate edge-to-edge, keyboard, screen reader and localization on real devices.

References: https://developer.android.com/build/releases/agp-8-13-0-release-notes and https://developer.chrome.com/docs/android/intents .

## QR sign-in and mobile appearance

The receiving screen generates a local verifier and a two-minute QR request. An existing signed-in account opens Account Security, scans the link using the native QR action or a supported browser scanner, or pastes the request link, checks the six-digit comparison code and explicitly approves. The receiver redeems exactly once through the same-origin browser cookie gateway. The existing Desktop grant machinery verifies that the approving refresh session remains active; a rotated source refresh token may require restarting the request. No credentials are encoded in the QR. Requests disappear after an Auth restart. Test expiry, source revocation, cancellation, replay, cross-account attempts and native camera permission on an isolated test deployment before release.

Mobile design uses a crescent/orbit launcher mark, portal-inspired hero, rounded cards and bottom navigation. Generated raster artwork is a separate asset-import step; existing shared MysticEgo art is the fallback. No mock login methods or invented data are displayed.

## First initialization and friend QR

Before WebView creation, show the local hardware/system report, explicit notice acknowledgement, and a camera choice. Declining camera is a supported choice; the ordinary account remains accessible. No broad storage, microphone, background location, accessibility, device admin or SMS access is requested. Only a limited model and OS/client descriptor travel with the normal Auth User-Agent. Native scanner recognizes exactly the canonical login link or a canonical friend link with a GUID; it opens the existing contact search, never sends a request automatically. Before production, publish and configure new Terms/Privacy versions as documented in docs/legal/mobile-rollout.md. Existing desktop grant policy version is unchanged.

The receiving device alone displays the six-digit comparison code; the approving client must enter it manually. Five incorrect attempts invalidate the request. A pending browser exchange cannot be cancelled through the UI mid-request; leaving the page during a network exchange may leave an independently revocable session until expiry. Confirm this behavior and cross-tab session changes during isolated acceptance testing.


## Device actions, inference and camera preview

- Account Security → Device connection: turn on in both foreground clients, choose the other active session, send a fixed navigation request, approve on receiving device. The dedicated authenticated MCP endpoint is POST /api/auth/device-actions/mcp. tools/list exposes device.request (targetId = active same-account session UUID, action = open-assistant/open-collective/open-profile/open-avatars). No OS command, camera activation, arbitrary URL, or shell tool. Requests expire in two minutes and are consumed once; source/target sessions must still be active at approval. For PC Agent diagnostics/approved commands, keep the existing operator and target permission flow; native cross-device actions require a separate paired device agent and consent lease.
- Native AI button: user starts a trusted OpenAI-compatible server on 127.0.0.1, e.g. PocketPal AI local API or llama.cpp server. Provide port, loaded model ID and local API key. Only current typed text goes to loopback; no remote fallback, no shared history, no key persistence. This is a local API adapter, not a bundled LLM engine.
- Assistant → Models and speech: ordinary chat runs on the configured MysticEgo node (e.g. VPS LM Studio). Optional external text API is disabled unless the operator configures MobileCloud:Endpoint (exact HTTPS /v1/chat/completions URL), MobileCloud:Model and secret MobileCloud:ApiKey. Speech uses the configured VoiceTranscription:Endpoint (HTTPS or HTTP loopback), VoiceTranscription:Model and secret VoiceTranscription:ApiKey. User explicitly approves transfer of current text/audio. Never send device tools, history or account credentials to model providers. Audio up to 30 seconds and 5 MiB; no application audio file persisted by this endpoint. Provider data retention is separate and must be disclosed. No automatic local/cloud fallback.
- Assistant → AR: camera preview with a transparent existing VRM renderer and manual screen placement, size and animation controls. Camera frames remain on the device. This is a 2D camera-aligned 3D overlay, not tracked world AR. Real ARCore support needs hit testing, plane anchors, camera pose, depth/occlusion and a VRM rendering bridge. Do not label it as world-anchored AR until tested on supported hardware. Camera/mic are individually requested and visible; stop on tab change or hidden document. Android 8 remains supported for basic client; local models and future ARCore depend on hardware/runtime.
- Acceptance: test dual-session isolation, revocation and expiry, real phone camera/mic lifecycle and both providers against synthetic data, model API key authentication, local loopback cleartext boundary, slow/cancelled inference, and CPU/GPU/thermal behavior. No production deployment until these pass and operator legal/provider configuration is reviewed.


## Mobile Agent (native enrollment pilot)

In account security on the canonical HTTPS panel, generate a one-time registration code and paste it into the Android MCP screen. A dedicated scoped key is generated by Auth, stored as a SHA-256 hash server-side and AES-GCM-encrypted with Android Keystore locally. The key never enters WebView and cannot call general account APIs. Registration persists in Auth SQL; device requests are in-memory and expire after two minutes. Open the native MCP screen, explicitly enable receiving, and approve individual battery-status, vibrate or open-assistant requests. Leaving the screen stops receiving; the server can show a recent heartbeat for up to 35 seconds. The first two actions invoke Android APIs, open-assistant requests a fixed deep link; results are device-reported, not independently attested. To disconnect permanently, revoke the server key before forgetting the local key. No background service, shell, remote sensor access or native desktop agent changes. Check account isolation, restart/revocation, source expiry, target foreground and actual phone operation before public rollout.

Registration requires a separate explicit acknowledgement in the account panel and native screen. It is not a substitute for publishing and versioning the updated legal documents. The separate agent key survives web logout until revoked; only foreground native requests can use it. Device registration and command history are separate from existing desktop permissions.

Native requests now use a SQL-backed action ledger; accepted actions are never reissued on process restart. Pending requests expire in two minutes; the latest 50 account records are visible in Account Security. On-access 30-day pruning and per-account admission limits are pilot retention controls, not a global scheduled retention policy. Source/target and active session checks are part of the conditional update. No claim of tamper-proof device execution; the result is device-reported.

Phone → PC: Account Security → Computers · MCP diagnostics. The new read-only bridge is disabled unless the Orchestra operator configures MobilePc:OwnerId and MobilePc:AllowedTargets. It requires existing developer + command permissions and a connected target advertising system.health or integrations.status. Existing command journal is used. No application launch or remote OS-control permission is granted by Android enrollment.

Application launch pilot: enable MobilePc:LaunchProposalsEnabled on Orchestra and MYSTICEGO_MOBILE_LAUNCH_ENABLED on an interactive PC with the existing owner and local alias list. The phone submits only an alias. On that PC open the local same-origin account security page and review the proposal, path and confirmation. Status is readable from the phone. No approval MCP tool. Target restart clears proposals; existing node audit records local decision. Test with harmless approved application and isolated account before deployment.
