# Traffic protection and DDoS resilience

This is defense in depth, not a guarantee against volumetric DDoS. Application admission limits are per process. A flooded network link, TLS handshake flood or distributed attack requires mitigation upstream of the host.

## Application layer

Auth and distributed node APIs use the shared `TrafficProtection` extension before authentication. Existing signup and Auth endpoint policies remain active. Request rejection is immediate (no queued backlog), with HTTP 429, Retry-After, no-store and a stable JSON code. Expensive MCP/AI/avatar operations, password verification and long-lived hub requests use separate concurrency capacity. The global token bucket still applies to all these categories; it does not guarantee priority under saturation. Static assets served before UseNode and messages inside already established WebSocket connections are not covered by these HTTP limits.

Default starting values, not production sizing:
| Setting | Default |
| --- | ---: |
| GlobalPerSecond | 500 |
| ClientPerSecond | 40 |
| SensitivePerMinute | 30 |
| ExpensivePerSecond | 60 |
| ExpensiveClientPerSecond | 8 |
| ConcurrentSensitiveRequests | 12 |
| ConcurrentRequests | 128 |
| ConcurrentExpensiveRequests | 24 |
| ConcurrentHubRequests | 1024 |

Override through the TrafficProtection configuration section or e.g. TrafficProtection__ClientPerSecond. Per-client keys use the observed peer IP (IPv4-mapped IPv6 normalized, IPv6 /64 grouped). No attacker-supplied user ID/path keys. Built-in partition eviction is not a hard upper bound on active IP keys; upstream filtering is necessary for large distributed floods. NAT and reverse proxy clients can share limits.

TrustedProxies is empty by default. For distributed nodes behind a trusted immediate proxy, configure only exact peer addresses, and only if that proxy overwrites X-Forwarded-For with one validated client IP. Multiple addresses and malformed values are ignored. This changes only the admission key, not authorization identity. Auth already uses ASP.NET forwarded-header middleware: do not enable duplicate forwarding trust there. Do not trust all headers or a whole unverified network. Review existing proxy topology before deployment.

The .NET Meter MysticEgo.TrafficProtection records rejection counts by bounded category (no IP, account or request body labels). It requires an appropriate metrics exporter/collector; existing Prometheus endpoint does not automatically export this new Meter.

## Edge and network

1. Use hosting-provider L3/L4 DDoS mitigation and a reverse proxy/CDN/WAF for public hostnames (including direct Auth hostnames). Confirm WebSocket support and preserve authenticated API behavior. Do not challenge machine-to-machine endpoints with interactive CAPTCHA.
2. Restrict origin ingress to approved proxy/provider networks or private tunnels, except necessary separately secured device channels. Do not expose Kestrel, databases, LM Studio or local native MCP to the Internet. Do not publish the origin IP or permit direct access around the edge. Keep SSH restricted to management networks. Verify access before changing firewall rules to avoid lockout.
3. If the deployment uses Nginx, optional sample includes are in ../ops/security/nginx: traffic-zones.conf in http {}, traffic-server.conf in public server {}. They limit requests and concurrent connections and set slow-client timeouts. Adapt to existing reverse proxy, trusted IP configuration, TLS and WebSocket routes. Do not install Nginx over another existing proxy. Run nginx -t, stage and monitor before reload. For a different edge provider use equivalent native rules.
4. Apply separate WAF rate rules for login, registration, MCP, AI/transcription and uploads; measure normal peak traffic, shared NAT, reconnect storms and file transfers. Cache public immutable assets at the edge only; never cache personalized/authenticated responses. Use provider-level protection for network saturation and bots.
5. Alert on sustained 429s, connection saturation, CPU/memory, p95 latency, origin egress and upstream 5xx. Restrict operational metrics and configuration to administrators. Do not expose an MCP tool that changes security limits without authorization.

## Verification

TrafficProtectionSmoke uses a localhost ephemeral Kestrel server, not production. It verifies proxy-header spoofing, IPv6 grouping, sensitive request limits, 429/Retry-After and separation of expensive concurrency from ordinary endpoints. CI builds Auth and distributed APIs. Realistic load benchmarks, upstream protection configuration, static edge limits, per-hub-message limits and cluster-wide admission are follow-up work. No external network load tests or production changes are made by this PR.
