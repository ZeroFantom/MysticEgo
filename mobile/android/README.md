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

Private query strings, fragments, account/session tokens and arbitrary host URLs are not transferred. Unsupported routes go to the corresponding root. App sessions are independent of external browser sessions. The WebView does not expose a JavaScript bridge, native device actions, microphone/camera, file access or arbitrary external navigation. HTTPS and exact primary origin are required; SSL errors are cancelled. The server remains responsible for authorization. No Android runtime permission beyond INTERNET is requested.

## Acceptance and distribution gates

- [ ] CI builds, lint and route tests pass; verify the debug build on actual Android hardware or emulator with updated WebView, using a dedicated test account.
- [ ] Test login/logout, session revocation, personal Audit and Orchestra access, back/reload, links when installed/not installed, Firefox/Chrome fallback, rotation and offline retry.
- [ ] Review trusted WebView content, resource origins and existing browser auth policy. Microphone, media upload, notifications, native/offline features and non-primary server support are intentionally deferred.
- [ ] Configure a dedicated protected signing identity and verified HTTPS Android App Links (Digital Asset Links) after the final certificate is available; custom scheme is not a verified universal link.
- [ ] Publish a verified signed APK and checksum via the official release channel, then update the installation card to use the verified artifact. No automatic publication or production deployment in this PR.
- [ ] Validate edge-to-edge, keyboard, screen reader and localization on real devices.

References: https://developer.android.com/build/releases/agp-8-13-0-release-notes and https://developer.chrome.com/docs/android/intents .
