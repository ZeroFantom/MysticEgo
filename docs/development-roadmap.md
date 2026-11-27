# MysticEgo development status and roadmap

Status snapshot: 2026-11-27. A merged change is not proof of deployment. Draft PRs are not released functionality.

| Area | Status | Next work |
|---|---|---|
| Desktop updates and release highlights | PR #86/#87 merged | Publish next release, verify installed Windows/Linux apps and restarts |
| Orchestra maintenance recovery | PR #85 merged | VPS staging with backups and compatible database schemas |
| Visible navigation MCP | PR #71 merged; 11/11 workflows passed | Interactive and permission verification on actual devices |
| Auth credentials | PR #73 merged, CI passed | Compromised password corpus, MFA/passkeys, key rotation |
| Traffic protection | PR #74 merged, CI passed | Provider WAF/L3-L4, cluster-wide limits, hub message admission |
| Browser session security | PR #75 merged; 11/11 workflows passed | HTTPS cookie deployment, XSS/CSP, session-family replay detection and integration tests |
| Capacity & resilience | PR #75 merged; 11/11 workflows passed | GPU/VRAM, tokens/sec, latency, all-node model pools, durable history, actual cloud billing |
| Timeweb rapid response | PR #75 merged; standby start code | Budget ledger, create VPS, verified readiness and fenced activation |
| Orchestra HA | Design, not implemented | Shared transactional registries, durable queue, storage replication, SignalR backplane, fencing, load balancer and failover drills |
| Independent analyzer | PR #75 merged | Independent primary metrics, durable history, self-cost measurements, separate failure domain |
| Computer Use / MCP | PR #72 experimental; status in PR | Windows/X11 testing, native permission enforcement, independent cursor and focus isolation |
| KDE Plasma / Windows settings | PR #72 experimental; status in PR | Native runtime validation, individual panel settings, safe desktop management |
| VRM handoff / lipsync / generation | PR #72 experimental; current CI in PR | Runtime continuity, animation and generation integration |
| Localization / typography | PR #72 experimental; partial coverage | English default, Chinese/Korean/Russian coverage, licensed bundled fonts, all desktop apps |
| Account permissions / sessions | PR #72 experimental; status in PR | Versioned consent, device-local grants for all native operations, revocation across connections and legacy tokens |
| License / studio / developer pages | Existing main pages and legal docs | Verify current document acceptance and all translations |
| Collective chat / profile | Existing main documentation | Verify voice, transcription, edits, replies, forwarding and profile status across clients |
| Development workspace | PR #77 and #76 merged; category selection and owner reconciliation | Real GitHub staging verification, notifications, per-user identity, distributed locks |

## Engineering standards
API and MCP controllers share application services; domain policies and persistence live behind abstractions. XML summary documents trust boundaries and behavior. Use authenticated identity, least privilege, bounded timeouts, cancellation and audited mutations. Measure performance before optimizing. Never enable Wayland native automation: Windows and Linux X11 are the intended desktop automation targets. No automatic provider spending based solely on model output.

## Release gates
Build .NET and frontend, run smoke/integration tests, verify Windows/X11 desktop workflows, database migration and rollback, security isolation and realistic staging load. Merge independent verified increments, not unfinished draft functionality. See PRs #72–75 and current GitHub Actions for live results.

Current integration: [PR #72](https://github.com/ZeroFantom/MysticEgo/pull/72) combines native automation, account permissions and presentation. Check the PR for final merge/CI status. Presentation changes preserve account-scoped local settings when Auth returns a partial response; visual smoke uses persistent mocked account preferences. Production and physical-device verification remain separate.
