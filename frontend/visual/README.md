# Browser visual smoke checks

Run from the repository root (Node 24):
```sh
npm --prefix frontend ci --ignore-scripts
npm --prefix frontend run build
npm --prefix frontend/visual install --ignore-scripts --no-package-lock
npm --prefix frontend/visual exec -- playwright install --with-deps chromium
npm --prefix frontend/visual test
```

The independent, pinned Playwright dependency does not change the production frontend lockfile. The test starts ephemeral loopback servers for each built role; replaces runtime configuration with loopback URLs, intercepts all API traffic with synthetic responses and blocks external resources. Credentials, profile, nodes and telemetry are fixtures, not live data. No real server or cloud writes are made.

Coverage: Orchestra, Global, Local, Agent, Audit; 390/768/1440px; dark/light × simplified/immersive sign-in and settings, circular style control, aspect ratios, horizontal document overflow, appearance persistence, drawer and account controls, profile dialog, topology, telemetry curves and gaps, and chat composer. The smoke checks use reduced motion for stable screenshots; a separate check toggles motion off.

GitHub Actions uploads PNG screenshots and results.json even on failures. Locally see artifacts/ or set VISUAL_OUTPUT. This is a smoke suite, not pixel-diff baselines or an assertion that the original design boards are faithfully reproduced. Review actual screenshots before approving new baselines. Real API integration and native Electron rendering remain separate tests.

The notification regression checks verify the header bell position, drawer bounds and Escape behavior across all roles and viewports, plus access to the password visibility control. The workflow also publishes the original PNG design boards as a separate short-lived review artifact; these references are not deployed or treated as pixel-identical baselines.
