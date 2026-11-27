# Localization and account presentation

Experimental PR #72, not a completed four-language release.

## Locales and fonts

Default selection: English (en). Supported catalog selections: English, Simplified Chinese (zh-CN), Korean (ko), Russian (ru). Existing source strings are Russian migration keys; not-yet-migrated strings still appear in Russian. Do not label the whole product fully translated until every screen and dialog has been reviewed. User content, command identifiers, code, paths, names, logs and canonical legal text are not automatically translated.

Typography presets:
- Modern: Inter, DM Sans, Noto Sans, Segoe UI.
- Editorial: Noto Serif, Source Serif 4, Georgia.
- System: native OS UI fonts.
- Mono: JetBrains Mono, Cascadia Mono, Consolas.
- Chinese: Noto Sans/Serif CJK SC, Microsoft YaHei, PingFang SC, SimSun.
- Korean: Noto Sans/Serif CJK KR, Malgun Gothic, Apple SD Gothic Neo, Batang.

CSS uses installed fonts, no external font requests. Fonts are not bundled yet; exact appearance depends on installed fonts and OS fallback. Bundle reviewed, licensed font files and include their notices in a future release.

## Account preferences

Auth owns GET/POST /api/auth/presentation. Authenticated user ID from validated JWT is the storage key, not a supplied user ID. POST applies validated field patches. The shared node gateway forwards these calls with bearer auth. Data includes language, font, theme, palette, colors, interface style, animation/transition preferences, density, text size and effects. Device permissions, processes, hardware settings and credentials are excluded. Guest preferences stay local. Web hydrates on account change, not every token refresh. AgentDesktop/AuditDesktop hydrate on sign-in; both use the same Auth record. No cross-account queued writes. Updates from another device are picked up on the next sign-in, not via real-time push. Unsynchronized changes may be lost on sign-out; UI reports failures. Existing browser-local preferences are not automatically migrated to the server. Some desktop visual effects have different render capabilities.

For single Auth instance: configure Presentation__Directory to an absolute private persistent directory on the Auth volume, include it in backup/restore, restrict filesystem permissions. Default is data/presentation under Auth content root. Store uses atomic file replacement and a process-local lock, not a distributed database. Multi-replica Auth needs a shared database implementation before deployment.

## Coverage

- Shared web navigation for Orchestra, Global, Local, Agent, Audit; login/register, appearance settings, common headings.
- Studio/developer/license navigation and principal headings; long-form copy still needs translation.
- AgentDesktop/AuditDesktop launcher static labels and main dynamic states; native dialogs and error messages still need migration.
- Remaining: detailed pages, all backend message codes, all public copy, VRM Studio/companion dialogue controls, LocalDesktop, CloudDesk, installers, date/number formatting, accessibility and visual regression in all locales.

Canonical license text remains unchanged. Translation completeness and CJK glyph coverage must be verified on Windows and KDE X11; Wayland remains unsupported for interactive desktop mode.
