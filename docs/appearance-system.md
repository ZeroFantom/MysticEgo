# MysticEgo appearance system

Two independent preferences: dark/light/system color mode and immersive/simplified interface style. Simplified dark is the default; existing stored themes and custom palettes are retained. Four artwork combinations are original lightweight vector interpretations of the approved moonlit shrine and network mesh concepts, not the generated high-resolution mockups. No third-party source images or video dependencies. Motion: normal, low, off; flow, fade, slide, scale. System reduced-motion always wins. No fake telemetry or new actions.

## Coverage
- React shared UI: Orchestra, Global, Local, Agent, Audit, public share, studio, developer and download surfaces.
- Electron central control, LocalDesktop, AgentDesktop/AuditDesktop: shared control/effects.js and CSS; existing palette controls retained.
- Avatar overlay: independent local appearance settings; transparent canvas and avatar animation remain unchanged.
- CloudDesk: local settings, recolored existing screens and SVG topology; portable updater includes all new assets.
- CLI recorder, key generator, launchers and geometry helper have no graphical theme and are intentionally unchanged.

Settings are stored locally, per web account scope or desktop origin. They are not synchronized across devices or different domains. Public entry settings are local. The simplified style does not load a landscape as its background. Immersive art is limited to headers, entry screens and selected cards; dense forms/tables retain opaque readable surfaces.

## Validation
Run frontend npm ci / npm run build; desktop npm ci / npm run build (see package scripts); CloudDesk npm install / npm run build. Run node --check on desktop view/effects JS. Review four combinations, settings persistence, system color changes, reduced motion, keyboard, login, menus, desktop station and topology. Full native and browser visual checks still required before release. No MP4/WebM cinematics are included: lightweight CSS transitions are used instead.

The frontend build runs `test:appearance` source-contract checks. They check asset presence and selector/control coverage, not visual rendering. The CloudDesk updater creates asset directories before copying.
