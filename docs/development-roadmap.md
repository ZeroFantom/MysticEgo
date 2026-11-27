# MysticEgo — implementation status and development roadmap

Snapshot: 2026-11-27. Distinguish merged source, unmerged development, and deployed functionality. Green CI does not prove a production rollout. Track work in [Issues](https://github.com/ZeroFantom/MysticEgo/issues), [Discussions](https://github.com/ZeroFantom/MysticEgo/discussions), [PRs](https://github.com/ZeroFantom/MysticEgo/pulls) and [Actions](https://github.com/ZeroFantom/MysticEgo/actions).

## Source and release status

| Area | Status | Reference / remaining gate |
|---|---|---|
| Core Orchestra/Global/Local/Agent/Audit/Auth; CloudDesk | Existing main | README, infrastructure and release docs; pilot/staging validation |
| Chat, voice/transcription, reply/edit/delete/forward, profile | Existing main | [Collective chat](collective-chat.md); test permissions, message privacy, delivery and cross-device consistency |
| Themes, transitions, Open Graph, studio/developer and licensing | Existing main | [Appearance](appearance-system.md), [social previews](panel-social-motion.md), [legal](legal/launch-review.md); verify actual deployed public pages |
| Password policy, versioned PBKDF2, public requirements API/MCP | Merged #73, CI passed | [Credential security](credential-security.md); comprehensive compromised-password list, MFA/passkeys, legacy migration |
| HTTP admission and DDoS defense | Merged #74, CI passed | [Traffic protection](traffic-protection.md); upstream WAF/L3-L4, hub message limits, cluster quotas, load tests |
| Browser automation initial MCP | Open #71, CI passed at snapshot | [PR #71](https://github.com/ZeroFantom/MysticEgo/pull/71); reconcile with overlapping #72 before merge |
| Native automation, VRM handoff, language/fonts, account consent/sessions | Draft #72, avatar-build failed; Chromium cancelled | [PR #72](https://github.com/ZeroFantom/MysticEgo/pull/72); review build failure and finish full integration |
| Secure browser refresh gateway, capacity monitor, independent analyzer, standby activation | Draft #75, CI passed at snapshot | [PR #75](https://github.com/ZeroFantom/MysticEgo/pull/75), [design in branch](https://github.com/ZeroFantom/MysticEgo/blob/feature/capacity-resilience/docs/capacity-resilience.md); production security review and HA gates |
| Developer workspace | New branch feature/developer-workspace | [Developer workspace](developer-workspace.md); CI, real GitHub permissions and browser verification |

## Work packages and acceptance criteria

### P0 — identity, permissions and resilience

- Account registration: versioned terms, privacy notice and license; separately selected optional feature scopes; no implied OS permissions. Migrate SQLite/PostgreSQL without loss. Existing users explicitly review new versions.
- Account device grants: one local approval per user/device/scope/version; revoke from account, enforce before each native action and cancel ongoing work. Device identity keypairs, challenge/signature, replay protection, encrypted local secrets and key rotation.
- Account sessions: stable session identity through refresh, list all sessions, end one/all others; immediate central revocation and disconnect existing connections. Test races, token reuse, account switching and offline behavior. Remove legacy JWT revocation gap.
- Browser: HttpOnly Secure SameSite host-only refresh cookie, explicit Origin/Host checks, reject ambiguous cookies, signing algorithm pinning, strict script CSP, session-family reuse detection, short token TTL, HSTS, XSS/CSRF tests. Bearer-only compatibility for desktop/local clients. Enable protected mode only after HTTPS gateway verification.
- Admission: keep per-process limits, add distributed quotas, message/connection budgets, bot filtering, upstream protection and dashboards; test normal NAT, reconnect storms and overload. Do not expose origin or admin APIs.
- Database and HA: move mutable node/station registries, job queues, assets and locks to transactional shared storage; PostgreSQL replication, backup/restore, fencing, SignalR backplane, idempotent outbox and consumer deduplication. Warm standby before active-active. Readiness must verify data and write ownership, not just /health. Timeweb load balancer, failover drills, explicit RTO/RPO.

### P1 — assistant and computing plane

- Common API/MCP controllers call shared application services and policies. Use XML summary on public C# contracts, explain trust boundaries, dependency injection, interfaces, cancellation, bounded queues and typed errors. Eliminate repeated auth/provider/client logic.
- Visible Playwright/Electron automation with explicit targets, scoped consent, recorder and cancellable replay. Native Windows/X11 only; reject Wayland/XWayland for Agent/Audit desktop control. KDE Plasma X11 D-Bus settings (animations, theme, virtual desktop); Windows native settings and virtual desktop support. Test actual OS and UI. No universal second physical pointer: parallel control uses owned browser/windows and virtual cursors, pauses on conflict; global physical input requires direct consent. No anti-cheat bypass, elevated or hidden input.
- Desktop handoff for VRM: same avatar/profile and animation continuation across web/native boundary; lip sync, VRMA generation, animation state, audio and safe local window overlay. Verify build and runtime on Windows/X11. Paid generation only with explicit budget/confirmation; avatar/animation/sticker export and rights. See issues #37, #38, #42, #43.
- Localization: English default; simplified Chinese, Korean and Russian across web and desktop; semantic keys, local font presets and licensed bundled fonts if needed. Store user settings centrally, restore after login, no cross-account leakage. Complete untranslated pages, errors, dialogs, generated legal translations and accessibility. Verify text expansion/CJK.
- Capacity: shared telemetry for all node roles, actual inference pool capacity without duplicate role aliases, local/remote model availability, CPU/RAM/GPU/VRAM, token throughput, queue/service latency percentiles, traffic, utilization and costs. 15-minute charts are initial; add durable history, alerts and forecasting. Use measured capacity and uncertainty, not invented prices. Separate analyzer with local model advisory, independent failure domain, self-cost and total cost; local user resources opt-in. Model output cannot authorize spending.
- Timeweb: pre-provisioned standby start only after budget/identity confirmation; finish live tariff/usage, quota check, cost reservation, idempotent create, cloud-init/release verification, health admission, rollback/drain and fenced independent control. No purchase or traffic promotion until verified.

### P1 — corporate workspace

- Internal task/discussion + explicit GitHub publication, canonical GitHub view and mutation, assignee mapping, team authorization, per-user OAuth/GitHub App, audit. Never sync confidential internal comments automatically.
- Extend to signed webhook ingestion, durable outbox, optimistic version checks, synchronization reconciliation, notifications, project boards, milestones, labels, priorities, comments/replies and full-text search. Shared service for REST/GraphQL and MCP; model read-only by default. See issue #48 and [workspace guide](developer-workspace.md).

### P2 — operation and product maturity

- CloudDesk DNS/restore, local LM Studio restart/model readiness, update rollback, safe SQLite/PostgreSQL migration and database recovery (#39, #40, #41, #47, #65).
- Pilot clubs and telemetry, real load and cost benchmarks, observability and restoration, public registration legal review, sustainable operating budget (#28, #30, #31, #32).
- Ether And Chaos: separate experimental game/world work; canonical world memory, NPC behavior and world events prototype (#29). Do not represent concepts as shipped game.
- Maintain Studio and developer public descriptions, license page and README as features move from concept to merged to deployed. Preserve legal source text and licensing boundaries.

## Merge and verification policy

Small independently reviewable PRs; test Auth, migrations on SQLite/PostgreSQL, web build, browser UI, Windows/X11 native paths, MCP contracts, role isolation, security abuse cases, concurrency, outage recovery and cost controls. Require current CI and human review for privileged or billing changes. Don't merge overlapping branches blindly. Run staged integration before production deploy; preserve backup, rollback and observed metrics. Update this roadmap whenever an acceptance gate changes. No secrets in docs, GitHub issues, logs or model inputs.
