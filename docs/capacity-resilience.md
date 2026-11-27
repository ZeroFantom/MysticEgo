# Capacity & resilience

Experimental implementation. Do not treat source code, example limits or model explanations as validated production capacity.

## Shared architecture

- MysticEgo.Capacity: typed samples, bounded latest registry, deterministic capacity and budget estimates. No cloud credentials or model-driven decisions.
- ServerTelemetry: shared resource sampler (5 seconds); enrolled Global/Local/Agent/Audit publish sanitized samples over HTTPS every 10 seconds using existing device JWTs. Orchestra checks the enrolled enabled node and overrides claimed identity/role/cost. Missing and stale data remain unknown. Local LM Studio discovery reports model readiness, not slot capacity.
- CapacityMonitor: primary Orchestra caches assessment every 5 seconds and retains 180 primary history points. Reuses WorldAI scheduler's physical slot grouping and queue backlog. Remote workers registered with Orchestra appear as remote pools; other nodes' actual inference throughput and model concurrency are not yet measured. Network utilization is calculated only with configured link speed. Costs are explicit operator inputs, not live billing data.
- UI: Infrastructure → Capacity & resilience. GET /api/v1/orchestra/capacity. Owner-only. MCP POST /api/v1/orchestra/capacity/mcp (capacity.status, capacity.reserve_plan). No secrets or prompts exposed.
- Api.Capacity: independently deployable collector on 127.0.0.1:5480 by default, requires RSA public key, exact owner subject and configured enrolled device IDs/roles. Publish to it via Capacity:CollectorUrl (HTTPS, explicit trusted destination). Can continue receiving reporting nodes while primary Orchestra is unavailable if Auth/device tokens remain valid. Own local LM Studio model optional; receives aggregate numeric results every two minutes and supplies advisory text only. It does not have cloud credentials. Primary Orchestra currently does not publish its scheduler metrics to the independent service; configure this in a subsequent integration. No durable telemetry storage or automatic promotion.
- CapacityReserve: owner-confirmed start of one pre-provisioned Timeweb standby, using shared provider transport, configured server ID and IPv4 checked against provider; primary ID excluded. Requires fresh positive recommendation and configured within-budget estimate. No blind retry, purchase, deletion, deployment or registration with load balancer. Power-on is not readiness.

## Configuration

Example keys (set via trusted service configuration or environment, not user-supplied request):
- Capacity:Currency=RUB
- Capacity:Costs:<node-id>=monthly cost (primary key orchestra:<NodeId>, other nodes enrolled GUID)
- Capacity:AnalyzerMonthlyCost=incremental monthly overhead including independent host, local model and storage. Set explicit zero only if verified no incremental cost; avoid double-counting co-located services.
- Capacity:ReserveMonthlyCost=estimated additional monthly cost; Capacity:MonthlyLimit=total ceiling; Capacity:MaxAdditionalPools=3
- Capacity:PrimaryLinkMbps=verified provisioned link speed
- Capacity:Reserve:Enabled=false by default; ServerId and Ipv4 required, distinct from Timeweb:OrchestraServerId.
- Timeweb:Token is kept server-side. Use a narrowly scoped provider token when available. No live calls are made by CI.
- Independent service: Authorization:Jwt:PublicKeyPath, issuer/audience, Capacity:OwnerSubject, Capacity:Nodes:<device-guid>=role, Capacity:Costs:<device-guid>, Capacity:LocalEvaluationModel, optional Capacity:LocalEvaluationToken. Put behind TLS reverse proxy and restrict intake to enrolled devices/private network. Revocation currently relies on JWT expiry and explicit allowlist changes; do not issue long-lived device tokens.

Prices must include all relevant costs (compute, GPU, storage, bandwidth, database, backup, CDN, load balancer, analyzer and cloud inference). Current implementation does not retrieve Timeweb tariff/usage, per-token costs, GPU/VRAM, latency distributions or request rates. No spending decision based on missing prices. Pool count is a bounded backlog heuristic, not a calculation of GPU/RAM requirements. A real planner needs measured tokens/sec, arrival rate, token distribution, p95 queue and service time, model/quantization/VRAM/context, quality constraints and cost/throughput curves. User-device inference is opt-in and must not compete with user foreground activity.

## Browser/session security

The existing API uses signed Bearer JWT and opaque SHA-256-hashed refresh credentials. Added algorithm pinning, response headers (frame ancestors, MIME sniffing, base URI and referrer restrictions), and concurrency rejection during refresh rotation. Optional same-origin HTTPS browser gateway: set BrowserSession:Origin to the exact public HTTPS origin, proxy must preserve Host, never enable it for plain HTTP. Cookie __Host-MysticEgoRefresh is Secure, HttpOnly, SameSite=Strict, path /, no Domain. The cookie is only for login/refresh/logout, never authorization of ordinary API/MCP. Refresh credentials are checked against Auth's server-side token store; modified tokens fail. Require exact Origin/Host and custom header on browser mutations. Browser keeps only access JWT in memory and non-secret restoration marker in sessionStorage when this mode is enabled. Existing clients/custom Auth origins use legacy flow. Do not expose BFF to arbitrary origins; use HSTS at HTTPS edge. Test same-origin, cross-origin, refresh rotation, multi-tab login/logout, cookies with reverse proxy. This is not protection against every XSS/session theft; strict script CSP, session-family replay detection, device-bound tokens, full session revocation and additional browser tests remain necessary. Cookie mode is opt-in until deployment verification.

## Orchestra HA / rapid response

Do **not** put a byte-for-byte duplicate of current Orchestra behind an active-active load balancer. Managed node and station registries, WorldAI jobs/memory, some assets and operational locks are local files/in-memory; multiple writers can diverge or repeat deployment jobs. PostgreSQL alone is not sufficient.
Target architecture: Timeweb load balancer with health probes and TLS, separate availability zones, shared replicated Auth/PostgreSQL, replicated object storage, durable job queue with transactional claiming/idempotency, distributed SignalR backplane, leader election with fencing for singleton maintenance/provisioning, stable public URL, and private networks. First deploy a warm standby, replicate and validate state, fence old primary before promoting, verify health and write ownership, then add traffic. Active-active only after all mutable services and migrations are made cluster-safe. Establish RPO/RTO with backup restore and failover drills. Avoid automatic retry of non-idempotent mutations. Provider-managed L3/L4 DDoS mitigation and WAF remain necessary. No real load balancer/standby created or promoted by this PR.

Timeweb official docs: https://timeweb.cloud/docs/load-balancer and https://timeweb.cloud/docs/cloud-servers/manage-servers/create-server. Actual new VPS creation, tariff discovery, budget reservation ledger, provider idempotency/reconciliation, cloud-init verification, health admission and autonomous failover are follow-up work. Independent analyzer must be able to issue proposals without primary, but privileged control should be fenced and explicitly authorized; this implementation keeps actuation in primary.

## Verification
CapacitySmoke tests deduplication, fresh/stale samples, unknown values, budget with analyzer cost, origin and cookie attributes. Dedicated CI builds Auth, Orchestra, independent API, frontend and runs smoke. Production performance, multi-device telemetry, real Timeweb activation, cookie replay, failover and load tests remain release blockers.
