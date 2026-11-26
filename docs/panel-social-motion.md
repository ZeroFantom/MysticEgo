# MysticEgo — social previews and cinematic UI

All five panel roles share the frontend build and motion styles. The current direction is a restrained cinematic tech/fantasy identity: layered landscape, moonlight, subtle constellation, role-specific colors, dark/light themes retained. The original procedural PNG and SVG artwork is intentionally a placeholder for future commissioned or generated high-detail anime/MMORPG art; it does not use third-party game artwork.

## Public previews

- The root and /share of each panel carry role-only Open Graph and Twitter metadata in the initial HTML, before JavaScript.
- Orchestra's /studio, /developer and /desktop-downloads have their own titles and descriptions. Embedded Audit uses /audit-portal/ and /audit-portal/share.
- C# resolves absolute URLs using the trusted Node:PublicUrl configuration, not request Host or forwarded headers. Set this to the actual HTTPS public origin when deploying; local-only nodes remain private and won't be previewable by external crawlers.
- The five 1200x630 PNG files are generated at build time, copied to each role's /og/ directory, and served without login. No account, live status, node ID, IP, token, telemetry or organization data appears in the metadata.
- Share pages link to the role's normal login/workspace. They do not change permissions or expose any protected API. Do not open private routes or a LAN-only Agent to the internet just to enable preview.
- On standalone static hosting, render the marker block with the real HTTPS origin (and Audit prefix when applicable). The static fallback uses root-relative paths; external crawlers require absolute URLs.

## Verification

Run npm ci and npm run build inside frontend; npm run test:social independently verifies generated role HTML and PNG dimensions/data. After deploy, check the actual HTML (not DevTools modified DOM) with curl, verify og:image returns 200 image/png without a cookie, and paste the link into Telegram and VK. Platforms choose their own crops/layout and cache previews. Telegram's @WebpageBot may refresh a cached preview.

## Motion

Use CSS compositing (opacity, translate, scale) for entrances, 150 ms micro-interactions, 240 ms overlays and 340 ms page transitions. No new animation dependency, WebGL scene, network image or continuous layout animation. Prefer the shared classes and keep keyboard focus visible. The reduced-motion preference removes the added entrance and hover motion. No animation on each telemetry poll. Test light/dark, narrow screens, account and chat menus, registration, notifications, and normal keyboard operation.
