# MysticEgo appearance system

Two independent preferences: dark/light/system color mode and immersive/simplified interface style. Simplified dark is the default; existing stored themes and custom palettes are retained. Four artwork combinations use generated design-board crops, cinematic frames and a clean abstract SVG. All graphical clients use the clean SVG for simplified dark instead of the WebP crop containing stray lettering. All 17 unique original PNG boards and 6 original GIF animations are archived in docs/design/generated, outside runtime bundles. No externally hosted images. Motion: normal, low, off; flow, fade, slide, scale. System reduced-motion always wins. No fake telemetry or new actions.

## Coverage
- React shared UI: Orchestra, Global, Local, Agent, Audit, public share, studio, developer and download surfaces.
- Electron central control, LocalDesktop, AgentDesktop/AuditDesktop: shared control/effects.js and CSS; existing palette controls retained.
- Avatar overlay: independent local appearance settings; transparent canvas and avatar animation remain unchanged.
- CloudDesk: local settings, recolored existing screens and SVG topology; portable updater includes all new assets.
- CLI recorder, key generator, launchers and geometry helper have no graphical theme and are intentionally unchanged.

Settings are stored locally, per web account scope or desktop origin. They are not synchronized across devices or different domains. Public entry settings are local. The simplified style uses the generated abstract mesh or wave art, never a landscape. Immersive art is concentrated in headers, sidebar and entry screens; repeated data cards and dense forms/tables retain readable surfaces. Artwork previews use a stable 16:9 viewport and contain rather than stretching or cropping.

## Validation
Run frontend npm ci / npm run build; desktop npm ci / npm run build (see package scripts); CloudDesk npm install / npm run build. Run node --check on desktop view/effects JS. Review four combinations, settings persistence, system color changes, reduced motion, keyboard, login, menus, desktop station and topology. Full native and browser visual checks still required before release. Three short WebM cinematics are encoded from the original animated GIFs and available on the public studio page on demand for immersive dark/light and simplified light. No autoplay or looping. Static poster remains available for reduced motion and low/off motion; playback is paused when the tab is hidden or the video leaves the viewport. Simplified dark retains static mesh art and CSS transitions.

The frontend build runs `test:appearance` source-contract checks. They check asset presence and selector/control coverage, not visual rendering. The CloudDesk updater creates asset directories before copying.

Social cards use 1200×630 PNG composites of the generated dark landscape with role-specific labels. The original boards are visual references, never substituted for working controls or real infrastructure data.

## Shared workspace polish
- `workspace-polish.css` provides shared radius, touch sizing, typography, token-based quick actions and narrow-screen layout refinements for all five React panels.
- The mobile navigation opens at up to 850px, uses a dedicated close control, traps keyboard focus, closes on Escape/navigation or desktop resize, restores focus and body scrolling, and makes the background inert while open. Motion preferences disable the drawer transition.
- The session recovery screen uses a centered single-column layout instead of inheriting an empty artwork column.
- Source checks protect these contracts, but are not screenshot comparisons. Browser QA should cover 390/768/1440px, light/dark and both styles, keyboard navigation, closing and resizing the drawer, long translated labels, user covers, and reduced motion.

## Desktop and CloudDesk polish
- Central control, LocalDesktop and AgentDesktop/AuditDesktop share artwork preview buttons with stable 16:9 containers, explicit pressed state, validated settings and a reset that does not depend on select order.
- CloudDesk keeps appearance controls accessible in narrow windows and scrolls its sidebar independently; topology keeps a readable minimum canvas width. Its portable updater copies the clean SVG and the existing AuditPortal component.
- Avatar overlay controls use rounded, theme-aware focus/hover states; the transparent avatar canvas and model behavior remain unchanged.
- Native QA: resize windows, switch four combinations, change system theme, edit custom palettes, reset, reopen and verify persistence, check keyboard focus, motion-off/reduced-motion, long logs and topology, and test portable update/rollback on a disposable installation. Source-contract tests and builds do not replace Electron visual checks.

## Shared motion and cinematics
- `motion.css` consolidates presentation timing and action stagger, using the existing flow/fade/slide/scale preference. Page enter animations use backwards fill instead of permanently transforming the route container.
- Account and notification disclosures remain mounted but inert and aria-hidden when closed; supported browsers transition display discretely with opacity/translation/scale. Older engines can close immediately. Notifications dismiss on outside pointer and Escape, with focus returned on keyboard dismissal or explicit close.
- Dynamic chat controls and action buttons use short entry effects. Reduced motion and the off setting disable these effects, including the decorative navigation trace.
- Cinema uses a stable 16:9 static frame, loads video metadata only on explicit opening, never autoplays or loops, and pauses on visibility loss. No full-screen animated backgrounds or new scene engine.
- Browser QA: keyboard and pointer open/close/reopen; reduced motion and all transitions; changing theme during playback; page navigation and fixed dialogs; verify no unnecessary re-animation on polling. Older browser fallback and real rendering remain unverified by source tests.

## Dashboard visual audit
The original 17 PNG boards and the Chromium screenshots from the 233-check green run were reviewed together. Login/settings broadly follow the intended light/dark visual language. Overview pages now use a single artwork hero, compact metrics and responsive real topology/telemetry sections rather than decorative graphs or repeated imagery. The Orchestra overview reuses the measured CPU chart with a one-minute interval, explicit missing-data bridges and freshness labels. Global/Local show the network report states; Agent/Audit keep their existing role-specific functions. New screenshots cover all overview variants. Concept artwork is directional, not a pixel-identical specification; native window and live-server review remain separate.
