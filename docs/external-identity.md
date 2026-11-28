# External Identity System — Telegram / VK

Epic [#118](https://github.com/ZeroFantom/MysticEgo/issues/118). This increment is a persistence foundation, not a functioning login or bot integration.

## Model
- Existing Auth UserId remains the only account identifier. `external_identities` stores Provider, Realm, Subject, UserId and LinkedUtc. The primary key prevents assigning a verified subject to two users. A unique index permits one association per account/provider/realm. Separate namespaces avoid collisions between providers or application-scoped IDs.
- Realm is a server-defined identity namespace, not an arbitrary caller-supplied string. Subject is opaque and must be derived from a verified provider assertion. Telegram bot user ID and login subject, or VK user ID and VK ID subject, must not be presumed interchangeable without official protocol validation.
- Additive compatibility DDL runs through AccountSecuritySchema for SQLite/PostgreSQL; no user/password/role/session changes. SQLite fresh/legacy schema and constraints are covered by AuthSmoke; PostgreSQL deployment still requires staging verification.
- ExternalIdentityStore is not registered as a public endpoint. It only persists already verified/confirmed associations and resolves active accounts. It never creates or merges users or issues tokens. Uniqueness constraints protect against conflicting concurrent inserts; later application service must map a failed write to a generic conflict and discard the failed DbContext.

## Before enabling the feature
1. Verify current official Telegram/VK login protocols and subject namespace mapping; configure trusted issuers, client IDs and redirect allowlists server-side. Implement signature/token validation, state, nonce, PKCE where applicable, expiry and replay checks.
2. Bind short-lived single-use link intents to the authenticated account, current session, provider and purpose. Require recent reauthentication and explicit confirmation. Atomically consume proof and persist the link; a failed transaction must never change account ownership. Bot deep-link tokens must be random, hashed at rest and confirmed on both sides.
3. Implement external login using existing Auth session issuance and account checks. For new accounts explicitly handle registration/consent and a secure recovery path, without synthetic usable passwords. Do not accept provider tokens in lieu of Mystic Ego access tokens.
4. Implement unlink/revocation with audit, recent reauthentication, alternative login/recovery checks, session and bot access revocation. Do not enable unlink or external sessions before these protections exist.
5. Add account UI, rate limiting, CSRF protections and provider sandbox integration tests. Do not include real provider secrets in Git. Only then connect messenger identity resolution to authoritative World Presence.

Tests: `dotnet run --project tests/AuthSmoke/AuthSmoke.csproj -c Release`.
