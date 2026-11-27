# Development Workspace

Orchestra route: /development-workspace. Team-only API: /api/v1/managed/development-workspace.

## Current implementation
Internal issues and discussion topics are stored through NodeState.Data (existing SQLite/PostgreSQL abstraction). Approved developers and owner can create items; local issues can be taken, released and completed. Explicit publication by creator or owner uses REST Issues or GraphQL Discussions. Polling imports up to 500 issues and 250 discussions every two minutes; pull requests are excluded. The remote title, body, status and issue assignees are authoritative after publication. Published issue actions require an explicitly configured GitHub account mapping. GitHub changes from other clients may race with updates; distributed concurrency is not guaranteed. A correlation marker helps a human reconcile uncertain creation results, but is not trusted as automatic identity proof. Records with uncertain publication remain blocked; do not blindly retry.

## Configuration
Set DevelopmentGitHub__Token through server secret configuration. Use a fine-grained token restricted to ZeroFantom/MysticEgo, Issues and Discussions read access, write only when necessary. Enable DevelopmentGitHub__AllowWrites explicitly. Map approved MysticEgo account GUIDs to verified GitHub logins via DevelopmentGitHub__Members__<guid>. Discussion publishing currently requires category General. The token never goes to the frontend. GitHub operations are performed under the integration identity, not individual OAuth identities; local audit logs record the initiating account.

## Roadmap
Categories, labels, priorities, milestones, deadlines, sprint planning, notifications, individual OAuth/GitHub App identity, durable webhook delivery deduplication, durable sync cursor/outbox, reconciliation UI, deletion handling, pagination beyond bounded import, distributed locking and end-to-end tests. GitHub outage should leave local work accessible. No live GitHub write is performed by tests.


## Boards, threads and MCP
The three-column task board (open, in-progress, closed) uses the same task actions as list view. No drag-and-drop or GitHub Projects v2 mapping yet. Local comments and replies use a separate per-item entity. Threads fetch GitHub comments on demand at most once per minute after a successful read, with cached content retained during errors. Up to 500 comments per thread, including local comments; issue import reads up to 300 comments, discussion import reads 50 top-level comments and 20 replies each. Further history remains on GitHub. Remote deletions are not reconciled yet. Explicit comment publication is restricted to its author or owner, on an already-linked topic. GitHub Issues have flat comments; only top-level comments in GitHub Discussions can receive native published replies. Internal replies remain local unless explicitly published. Uncertain comment creation is blocked pending manual reconciliation.

GET /api/v1/managed/development-workspace/{id}/comments
POST /api/v1/managed/development-workspace/{id}/comments
POST /api/v1/managed/development-workspace/{id}/comments/{commentId}/publish

POST /api/v1/managed/development-workspace/mcp uses the shared MCP dispatcher and application service. Tool workspace.tasks returns up to 100 recent item summaries, with truncation indicator. It has no mutation, comment body or secret access. Clients must treat repository titles as untrusted content. Authentication excludes device credentials.

## Webhook
Configure a GitHub repository webhook with JSON payload at https://<orchestra-host>/api/v1/public/development-webhook. Set DevelopmentGitHub__WebhookSecret to a cryptographically random secret of at least 32 characters on both ends. Subscribe to Issues, Issue comments, Discussions and Discussion comments. HMAC-SHA256 authenticates the raw request body in constant time; only the configured repository is accepted. The endpoint requests a coalesced background read at 15-second ticks; two-minute fallback polling remains. Webhooks do not apply payload contents or authorize writes. Thread comments refresh when opened, not fleet-wide on every event. Durable delivery-ID tracking remains future work. Replayed valid payloads can request additional bounded reads.

## Tests
DevelopmentWorkspaceSmoke verifies HMAC validation, modified payload rejection, coalescing, fixed GitHub routes and disabled writes with a fake HTTP handler. Dedicated CI compiles Orchestra, runs smoke and builds frontend. No real GitHub mutation in tests. Storage, member isolation, concurrent updates, webhook delivery and REST/GraphQL end-to-end integration still require staging tests before release.
