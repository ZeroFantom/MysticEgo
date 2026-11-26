# MysticEgo appearance system

Two independent preferences: dark/light/system color mode and immersive/simplified interface style. Simplified dark is the default; existing stored themes and custom palettes are retained. Four artwork combinations now use actual generated design-board crops and cinematic frames, optimized as WebP. All 17 unique original PNG boards and 6 original GIF animations are archived in docs/design/generated, outside runtime bundles. No externally hosted images. Motion: normal, low, off; flow, fade, slide, scale. System reduced-motion always wins. No fake telemetry or new actions.

## Coverage
- React shared UI: Orchestra, Global, Local, Agent, Audit, public share, studio, developer and download surfaces.
- Electron central control, LocalDesktop, AgentDesktop/AuditDesktop: shared control/effects.js and CSS; existing palette controls retained.
- Avatar overlay: independent local appearance settings; transparent canvas and avatar animation remain unchanged.
- CloudDesk: local settings, recolored existing screens and SVG topology; portable updater includes all new assets.
- CLI recorder, key generator, launchers and geometry helper have no graphical theme and are intentionally unchanged.

Settings are stored locally, per web account scope or desktop origin. They are not synchronized across devices or different domains. Public entry settings are local. The simplified style uses the generated abstract mesh or wave art, never a landscape. Immersive art is limited to headers, entry screens and selected cards; dense forms/tables retain opaque readable surfaces.

## Validation
Run frontend npm ci / npm run build; desktop npm ci / npm run build (see package scripts); CloudDesk npm install / npm run build. Run node --check on desktop view/effects JS. Review four combinations, settings persistence, system color changes, reduced motion, keyboard, login, menus, desktop station and topology. Full native and browser visual checks still required before release. Three short WebM cinematics are encoded from the original animated GIFs and available on the public studio page on demand for immersive dark/light and simplified light. No autoplay or looping, disabled for reduced motion, low/off motion. Simplified dark retains static mesh art and CSS transitions.

The frontend build runs `test:appearance` source-contract checks. They check asset presence and selector/control coverage, not visual rendering. The CloudDesk updater creates asset directories before copying.

Social cards use 1200×630 PNG composites of the generated dark landscape with role-specific labels. The original boards are visual references, never substituted for working controls or real infrastructure data.
