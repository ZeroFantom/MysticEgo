# Development workspace / Корпоративное пространство

Draft implementation in branch feature/developer-workspace. Not deployed.

## Access and workflow

Open Orchestra → Development workspace (/developer-workspace). Only the owner and approved members of DevelopmentTeam may read or mutate workspace data. Device credentials cannot use the workspace. Membership is checked on every HTTP/MCP call.

- Internal workspace: create a task or discussion, take or release a task, comment, complete/reopen. Owner, creator or assignee can complete/reopen local items. Published local records become read-only references.
- GitHub Issues: browse open and closed issues (PRs excluded), filter displayed page, see labels, assignees and comments, take an unassigned open issue using a verified account mapping, comment, owner-only close/reopen.
- GitHub Discussions: browse categories, discussions, comments and post comments; choose a GitHub category when publishing an internal discussion. First 50 top-level comments in UI; full threads on GitHub.
- Search and status filters apply to loaded items only. Backlog/in progress/done are derived from state and assignment, not GitHub Projects or a separate replicated workflow.
- Explicit publish: creator or owner confirms publication of title and description; local comments, assignees and internal identities remain private. A stable marker and pending state are saved before sending. If the response is lost, inspect GitHub; owner can enter the existing number and link it only if the marker matches. Never blindly retry creation. Pending items with no upstream match need an operator recovery procedure.

GitHub is the source of truth for published tasks/discussions: changes made on GitHub appear on the next refresh (shared cache 15s, visible-tab polling 15s). Changes made from Orchestra write to GitHub directly. No duplicate independent editable copies, no two-way overwrite loop, no webhook ingestion yet. Internal items remain available without GitHub. A provider outage is displayed as an error, not as an empty successful result.

## Configuration

Reuse existing GitHubPanel read credential for private repository reading (must also grant Issues and Discussions read). Configure a separate server-side `DevelopmentWorkspace:WriteToken` or environment variable `DevelopmentWorkspace__WriteToken` for writes, limited to ZeroFantom/MysticEgo. Grant only necessary Issues/Discussions read/write and repository metadata access; verify token support for Discussions GraphQL. Do not grant administration, Actions, secrets or contents write merely for the workspace. Enable Discussions in the repository and configure categories.

No token entry in the workspace UI. Store secrets in server secret configuration, not git, command-line arguments or logs. Prefer a short-lived installation token and automated rotation in the next iteration. Currently operations are attributed to the configured GitHub credential, not impersonated as the individual developer; Orchestra audit records the actual MysticEgo actor. Per-developer OAuth/GitHub App linkage is tracked by issue #48. Set `DevelopmentWorkspace:Accounts:<MysticEgo-user-GUID>` to a verified assignable GitHub login; never trust a self-asserted browser mapping. The service account acts on behalf of approved team members; enable write access only if that delegation is appropriate.

## Code and contracts

- Development/GitHubWorkspaceClient: fixed origin/repository, constrained REST routes, application-owned GraphQL documents, 12s timeout, no redirects, bounded response, separate write token.
- Development/GitHubWorkspace: shared service, validation, page snapshots, mutation invalidation and audit.
- Development/InternalWorkspace: existing relational DynamicEntityStore; entity mysticego.orchestra.workspace.item schema v1 (SQLite/PostgreSQL); local write serialization, durable publication mapping. Limit 500 records and 100 comments per item. Back up existing storage. Cross-replica locking and transactional publication outbox remain required for HA.
- Controllers/DeveloperWorkspaceController: team gate, DTOs and stable errors.
- Controllers/DeveloperWorkspaceMcpController: read-only summaries; remote content is untrusted data. No automatic publication or assignment.

HTTP base /api/v1/managed/developer-workspace:
| Method | Route | Purpose |
|---|---|---|
| GET | /connection | Readiness and mapped login |
| GET | /internal | Internal items |
| POST | /internal | Create private item |
| POST | /internal/{id} | Claim/release/comment/close/reopen |
| POST | /internal/{id}/publish | Confirm explicit publication |
| POST | /internal/{id}/reconcile | Owner verifies marker after ambiguous response |
| GET | /github/{kind} | Paged issues/discussions |
| GET | /github/categories | Discussion categories |
| GET | /github/{kind}/{number} | Details/comments |
| POST | /github/{kind}/{number} | Claim/comment/close/reopen as permitted |
| POST | /mcp | workspace.internal_tasks, workspace.github_issues, workspace.github_discussions |

## Verification and next steps

DeveloperWorkspaceSmoke uses a mocked GitHub HTTP transport and isolated local database, verifies PR exclusion, cache, local-only creation, claim conflict, publication marker and route safety. Dedicated CI builds Orchestra and frontend. Real credential testing, GraphQL integration tests, HTTP authorization/role tests, browser e2e, per-user OAuth, webhooks with HMAC-SHA256 signature/replay checks, cursor comments and replies, edit history, assignment release on GitHub, milestones, labels, priorities, kanban/Projects, notifications, full-text search, local-to-GitHub import/link of existing items and HA fencing are not complete. No production account or GitHub token provisioned by this change.

References: https://docs.github.com/en/rest/issues/issues and https://docs.github.com/en/graphql/guides/using-the-graphql-api-for-discussions.
