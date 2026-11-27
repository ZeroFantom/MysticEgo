# Account security, registration and device permissions

Experimental implementation in PR #72; not deployed or release-tested.

## Three distinct controls

1. Public Audit registration requires configured, current Terms and Privacy document versions and the PolyForm Noncommercial 1.0.0 license. The API records document acceptance and explicit enabled/disabled optional assistant feature choices in the same database transaction as account creation. Public registration does not grant administrative roles. If the terms or privacy document is not configured, signup fails closed.
2. Account feature consent is a versioned list of scopes. It can be changed later in Account Security. Existing accounts are not assigned retroactive consent: they can review documents and choose features there. Disabling a scope revokes associated server-side device grants.
3. Device approval requires local user confirmation on that device. The server grant record alone does not prove local approval: the native client must have the corresponding locally confirmed grant generation and must check current account consent and server grant status. Windows administrator elevation and OS restrictions remain separate. Device grants are not permanent authorization to execute arbitrary commands.

The desktop VRM handoff is the first native operation wired to this model. The Agent/Audit launcher checks the central account and local grant before each handoff, remembers confirmation for the same OS user/account/device/scope, and checks revocation while the companion is running. The other scopes (browser, desktop input, screen capture, record/replay, peripherals, drivers and games) are registered choices and grant API contracts but their native executors are not yet integrated. Existing local executor leases must remain in place until that integration is complete. A grant must not enable an unrelated scope.

## Session management

The Account Security page is accessible through the signed-in account menu in shared web panels (including embedded panels). It shows current and other refresh-backed sessions, client user-agent, last refresh IP, login time, last refresh time and expiry. These are login sessions, not a live device-presence indicator. User-agent and IP may be incomplete or proxied; no raw tokens are displayed. A user can revoke another session or all other sessions while preserving the current one. Normal logout ends the current session.

New JWTs include a stable random mystic_session claim, preserved across refresh rotation. Auth validates active account and session state; distributed node JWT authentication checks central Auth for session-bearing tokens, failing closed if central validation is unavailable. Browser and station clients poll for revocation (15s and 10s nominal intervals respectively, subject to connectivity). Existing pre-upgrade tokens do not have the claim and retain their previous lifetime until refresh or login; individually revoking legacy sessions is unavailable until upgraded. Revoking all others removes their refresh tokens. Existing running operations, standalone executor tokens, service identities and already-open connections require independent shutdown/revocation handling. Club Agent services are not stopped solely by personal logout.

## Database upgrade

Auth startup applies additive schema upgrades for SQLite and PostgreSQL: nullable SessionId/LastSeenUtc in refresh_tokens, unique index for session IDs; account_license_acceptances, account_feature_consents, account_device_grants. Existing users and refresh tokens are preserved. Backup Auth storage before deployment; test old-schema upgrade and PostgreSQL separately. New consent/device tables cascade on user deletion. The previous account appearance store remains file-backed and should be migrated to database-backed storage for clustered Auth deployments.

## API

- GET /api/auth/sessions
- GET /api/auth/sessions/current
- POST /api/auth/sessions/{id}/revoke
- POST /api/auth/sessions/revoke-others
- GET /api/auth/consents
- POST /api/auth/consents
- POST /api/auth/device-grants/approve
- POST /api/auth/device-grants/revoke

Endpoints use authenticated account identity, not a caller-provided user ID. No device-token privilege is granted. The approval endpoint stores a client assertion; the local grant generation is the additional enforcement for VRM. Do not treat the registry as cryptographic attestation of the device. Test cross-account isolation, concurrent token refresh/revoke, local grant revocation, central outage, upgrade compatibility, UI translations and deployment ordering before release.
