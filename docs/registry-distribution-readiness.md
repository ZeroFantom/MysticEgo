# Registry and package distribution readiness

Status: proposal; no registry entry or package is published by this document.

## Decision

- GitHub Releases remains the distribution channel for desktop installers and checksums.
- GitHub Container Registry (GHCR) is a candidate for reproducible service images after an isolated Docker build and runtime smoke test. Existing Dockerfiles include `services/Api.Orchestra/Dockerfile`, Auth, Agent, AuditOrchestra, Global and Local. Do not change active deployment or repository ownership as part of this proposal.
- The npm workspaces `@mysticego/sdk` and `@mysticego/ui` are private source workspaces (0.2.0); do not publish them just to populate a registry. Revisit when there is a real external consumer, stable exports, emitted types, license review and versioning.
- NuGet is useful only for a deliberately supported independent .NET client or contract library with tested API compatibility; not for every internal assembly.

## GHCR pilot

1. Build Orchestra from the root context using its existing Dockerfile; review .dockerignore and build context for sensitive files.
2. Run the service in an isolated environment with synthetic configuration, verify health, auth and shutdown; no production credentials or access.
3. After passing, add a separately reviewed opt-in publish workflow; use least-privilege `GITHUB_TOKEN` with `contents: read` and `packages: write` only in the publishing job. Tag immutable versions and commit identifiers, attach source metadata and record digest. Keep package visibility private until an explicit publication decision.
4. Test pull/install and rollback. Do not redirect current release automation to the new registry until verified.

## MCP

The repository already implements JSON-RPC MCP endpoints and desktop MCP interfaces. The presence of `/mcp` is not evidence that it is a public, compatible, safe remote MCP service.

Before external registration:
- inventory tools, transports and authentication; ensure authorization is enforced at the actual hosting/routing boundaries and at execution;
- validate supported protocol versions, initialization, tool discovery, request limits, session lifecycle and cancellation with external clients;
- enforce least privilege, explicit device-local approval, audited mutations, and per-user/tenant isolation; never expose native automation or infrastructure control anonymously;
- define a stable distribution artifact or a deliberately public-facing authenticated server endpoint, documented setup, permissions and limitations;
- after review, prepare and validate a versioned `server.json` and publish through the official MCP publisher under a verified namespace. Do not advertise private hosts or management endpoints.

An organization-managed registry/allowlist is a separate governance feature, not a requirement for using local MCP. Defer it until multiple users/clients need centrally managed discovery.

References:
- https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry
- https://github.com/modelcontextprotocol/registry
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-mcp-usage/configure-mcp-registry
