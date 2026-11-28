# MCP exposure review — first local hardening slice

Reviewed against MysticEgo @ f75e7e54cfc45bc9bdeebe871b3f32ae63d23b0b. Not a penetration test or public registry certification.

## Existing boundaries

- Diagnostic controller: operator policy, account resolution, Orchestra developer check and Global/Local command policy; local desktop calls also require owner, loopback and no forwarding/browser-origin headers. UI bridge requires matching active owner consent. Check these boundaries on each actual host and reverse proxy; generic MapMcpEndpoints in the shared library does not itself declare an authorization policy. Do not mount it as a public server.
- Capacity controller: owner-only, explicit read-only tools. Workspace controller: account and owner/member check, explicit read-only workspace tool. Review cross-tenant results and failure responses.
- Desktop and CloudDesk: temporary loopback bearer token, origin restriction, request size bound and visible window. Mutating update actions require separate native confirmation.

## This change

Recheck the session generation after asynchronous renderer reads, before focus/input and after the final target lookup. If the window lost focus, no native input is sent. Reject malformed requests and tool calls without a valid request ID before execution. Preserve both identical desktop bridge copies. Add mock-based regression tests; real Electron and OS focus behaviour still requires acceptance testing.

## Registry decision

Do not publish this administrative or device-control MCP endpoint. Before public distribution: inventory effective route mappings and middleware on each host, test unauthenticated/device/foreign-account requests, validate session and protocol negotiation, check cross-target access and revoke/cancel races, review network/origin limits and error redaction. A separate narrow read-only public-facing adapter is preferable if external discovery is genuinely needed. See PR #111.
