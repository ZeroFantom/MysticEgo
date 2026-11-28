# Messenger experiences — Зеркало души и Духовные круги

Design extension to [Epic #118](https://github.com/ZeroFantom/MysticEgo/issues/118). User requirements: Telegram Mini App, VK keyboards, interesting group/community experiences. This document records proposed mechanics and implementation order, not deployed functionality. Names and individual mechanics below are design proposals.

## Shared rules

All channels use existing Mystic Ego account, active embodiment, permissions, canonical game events and world state. OAuth login and bot link remain independent. Private spiritual contact requires same in-game area; confirmed player friends may communicate across areas within the same world. Chat membership, Mini App context, real-world location, guild membership or moderator role never grant a bypass. NPCs perceive spiritual communication from an in-world identity, not a platform. Unknown world state fails closed. Do not add separate economy, inventory, NPC memory, character creation, quests or game state per platform.

## A. Telegram Mini App: Зеркало души

Start with a responsive web route inside the shared frontend, reusing accessible UI components and read-only world views. Launch from configured bot menu/profile or official direct link. Text bot remains a first-class fallback.
- MVP: account link status, active embodiment, current area, permitted contacts, friends, private spiritual dialogue, canonical history and unread state.
- Subsequent: read-only soul progression, inventory, quests, public chronicles, guild membership and event participation. Mutations use existing domain services and explicit confirmation; never add simulated local state that is treated as authoritative.
- Use theme, viewport, safe-area, back button and reduced motion; load lightweight assets, no AR/camera permission by default. Mobile app handoff remains presentation only.

Authentication: send raw Telegram WebApp initData over HTTPS POST to a dedicated endpoint. Validate official HMAC algorithm using the configured bot token on the server (reject duplicate/malformed fields, constant-time hash, bounded size and auth_date, bot-specific namespace). Never trust initDataUnsafe, query parameters, username, or the client-supplied user/peer/world. Resolve verified private Telegram user in bot realm; unlinked users see linking instructions only. A group-launched Mini App has no access to the group's conversation history and must not treat chat context as membership authorization. Replay-protect initial exchange; issue a short-lived restricted Mini App session, recheck active account/bot binding and server permissions per request, do not issue privileged Auth JWT or refresh tokens from initData. Don't embed OAuth secrets or link codes in URLs. Full account linking/management stays in trusted browser flow with recent password. Revocation denies subsequent calls. No persistent storage for initData or tokens. Verify actual clients, platform signature fixtures and isolated HTTPS staging first.
Official: https://core.telegram.org/bots/webapps (launch methods, validating data).

## B. VK keyboard interface

Keep text commands as fallback and use a shared action/view model rather than a second game engine.
- Persistent menu: Моя душа | Духовная связь; Друзья | Задания; Инвентарь | Хроники. First increment only show actions actually supported by backend.
- Contextual inline keyboards: permitted nearby entities, friends, back/next page, select a contact, start/leave a dialogue, accept/decline event invite. Free text goes to selected recipient only after fresh permission/world check; selection expires or invalidates on movement.
- Use VK keyboard JSON with callback actions; process message_event under verified Callback API secret and exact community. Acknowledge via messages.sendMessageEventAnswer where appropriate. Bind opaque action handle to provider/bot, actual sender, peer, purpose, scope and expiry; atomic consumption for mutations; dedup by event and action, check current account/role/world state. Never trust raw payload as game command or accept a private keyboard action from group context. Handle outdated controls with a refresh message. Group keyboards have a separate action namespace.
- Keep keyboard labels short, pages bounded, support old clients through explicit text fallback, and escape display names/control characters.
Official schema: https://github.com/VKCOM/vk-api-schema and https://github.com/VKCOM/api-schema-typescript/blob/master/src/methods/messages.ts.

## C. Духовные круги — shared community surfaces

Separate group/community adapter, not an extension of private message ingestion. Proposed experiences:
1. Общий костёр: an opt-in public gathering board, recruiting parties, sharing explicitly public rumors, cooperative travel preparation and event announcements. Group conversation itself stays out of NPC memory.
2. Представительство гильдии: link a verified chat/community to a real game guild with consent of both platform admin and authorized game guild officer; show public guild board, join requests, approved recruitment, shared goals. No automatic guild join from membership.
3. Эхо мира: server-published public world events, anomalies, wars, discoveries, faction notices, opt-in digests. Publish only explicitly approved public event projection, no hidden locations, private character state, quests or account IDs.
4. Collective encounters: e.g. a forest rift, rescue expedition, puzzle, resource drive, town defense. One canonical event across Telegram, VK and client; groups see public cards with progress, join/details buttons and outcome. Each participant explicitly joins using linked account; capacity, eligibility, time and action checked by game server. Remote player may register interest where allowed, but local participation requires current authoritative placement. No travel, NPC access, rewards or votes solely from external chat presence.
5. In-world public NPC appearances: only game-authored event projection or expressly permitted public scene; NPC addresses the eligible participants as in-world audience. Private NPC dialogue stays private; public scenes do not bypass area and consent rules. Nonplayers can see public lore/rules and a link to join Mystic Ego, never act as another player.
6. Community-specific reputation/chronicle is a projection of canonical verified achievements, not an independent XP source. Optional group votes affect only explicitly defined cooperative event choices, one eligible character/account per choice, server deadlines and deterministic resolution; no reward farming via repeated messages or inviting bots.

Group onboarding and safety:
- Default disabled. Verify bot membership and administrator authority via provider API, explicit owner approval for binding, per-chat capability flags (announcements, recruiting, events, votes, approved public scenes), guild/area mapping from trusted world data only. Record audit, allow pause/remove, revoke on lost authority/bot removal. Never need broad group history permissions; process explicit commands, mentions and action callbacks only. No automatic ingestion of ordinary chat messages or adding to model prompts.
- Group /link or private account command only directs to private chat; do not stage codes, expose account links or read private inventory/history in shared chat. Private answers via authenticated private channel, no fallback to group. No linking or password collection in group.
- Separate public event DTOs and private game DTOs; filter before enqueue and again before delivery. No cross-posting private conversations, social user IDs, tokens, account IDs, physical locations, hidden NPC or unapproved profile. Explicit opt-in for shared game name, otherwise no public player identity. Block/mute, reporting, admin moderation and per-chat/user/bot quotas. Quiet hours/digests optional. No unsolicited DMs, mass mentions or repeated announcements by default.
- Distinguish Telegram chat/thread and VK peer/community; verified membership/roles, event identity, callback scope and current game participation on each action. Group membership does not equal an in-world location or entitlement. Provider group updates dedup separately from private receipts; no changes to existing private-only parser until dedicated adapter is implemented.
- One world event ID and idempotent participant/action keys across all channels. Durable public outbox, per-destination delivery state, bounded retries and explicit uncertainty for non-idempotent provider sends; no duplicate rewards or duplicate state transitions. No rewards from raw vote counts.
- Example: forest rift is published to subscribed groups. Players click join, get a private eligible session and select their own character. World records reservation/attendance separately; only players actually present and eligible can perform the local event actions. Server computes the result once and publishes a public outcome. All three clients see the same canonical outcome.

## Implementation sequence and release gates

1. Finish existing authenticated Auth–Orchestra handoff, durable ingress/outbox and authoritative world publisher/revalidation. Keep private bot features gated.
2. Add shared presentation/action model with explicit action IDs, views, scope, permission, expiry and version. Add Telegram initData verification and restricted session tests; add VK keyboard serializer, verified callback parser and one-use action tests. Never trust platform UI as authorization.
3. Build Telegram Mini App MVP and VK menus over the same private contact/history services. Test link/unlink, stale state, client rendering, duplicate callbacks and no role escalation.
4. Add opt-in community binding and public-only world event subscriptions, admin control, dedicated group ingress/egress, redaction and quotas. Start with one canonical cooperative event type (expedition) and cross-channel join; add guild representation and other mechanics afterwards.
5. Validate actual bots and devices, rate limits, HTTPS reverse proxy, secret/log protection, PostgreSQL concurrency, revocation, privacy, retention and rollback. No automatic enabling, merging or production rollout.

Acceptance: same account and active embodiment across channels; Mini App raw initData forgery/replay denied; VK callback cannot be replayed or borrowed from another user/chat; no inaccessible NPC listed or contacted; unlinked, blocked, stale or ineligible users cannot act; group membership never changes placement; private message and link code never appear in group output; a cross-platform event joins/rewards once; stale admin permissions disable group publishing. Explicit privacy tests should verify that ordinary group text is not collected for NPC memory.
