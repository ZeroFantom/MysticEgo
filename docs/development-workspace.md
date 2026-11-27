# Development Workspace

Orchestra route: /development-workspace. Team-only API: /api/v1/managed/development-workspace.

## Current implementation
Internal issues and discussion topics are stored through NodeState.Data (existing SQLite/PostgreSQL abstraction). Approved developers and owner can create items; local issues can be taken, released and completed. Explicit publication by creator or owner uses REST Issues or GraphQL Discussions. Polling imports up to 500 issues and 250 discussions every two minutes; pull requests are excluded. The remote title, body, status and issue assignees are authoritative after publication. Published issue actions require an explicitly configured GitHub account mapping. GitHub changes from other clients may race with updates; distributed concurrency is not guaranteed. A correlation marker helps reconcile uncertain creation results. Do not blindly retry publishing records.

## Configuration
Set DevelopmentGitHub__Token through server secret configuration. Use a fine-grained token restricted to ZeroFantom/MysticEgo, Issues and Discussions read access, write only when necessary. Enable DevelopmentGitHub__AllowWrites explicitly. Map approved MysticEgo account GUIDs to verified GitHub logins via DevelopmentGitHub__Members__<guid>. Discussion publishing currently requires category General. The token never goes to the frontend. GitHub operations are performed under the integration identity, not individual OAuth identities; local audit logs record the initiating account.

## Roadmap
Threaded comments and replies, bidirectional comment synchronization, categories, labels, priorities, milestones, deadlines, Kanban, sprint planning, notifications, individual OAuth/GitHub App identity, webhook signature verification and delivery deduplication, durable sync cursor/outbox, reconciliation UI, deletion handling, pagination beyond bounded import, distributed locking, MCP read-only workspace access and end-to-end tests. GitHub outage should leave local work accessible. No live GitHub write is performed by tests.
