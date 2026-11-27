# Orchestra standby and load balancing checklist

This is a deployment gate, not a live configuration. Do not route write traffic to a replica of the current file-backed Orchestra.

1. Establish the stable public hostname and TLS termination. Configure provider-managed L3/L4 DDoS mitigation and WAF. Restrict origin ports.
2. Deploy a separate standby in another failure domain. Use identical release artifact/config schema. Keep it isolated from production writes, scheduled tasks and model job processing until shared persistence and fencing are implemented.
3. Move mutable registries, WorldAI jobs, locks and file metadata into shared transactional storage; move assets to replicated object storage; configure database backups and tested replication/failover. Use a durable queue with atomic leases/idempotency and a SignalR backplane.
4. Prove readiness with authentication, data replication, schema and dependency checks, not just TCP reachability or /health. Test node admission, sessions, refresh rotation and mutations during primary loss.
5. Configure Timeweb load balancer backend and health checks. Only admit a replica after step 4 and fencing; do not retry non-idempotent mutations across backends. Keep rollback and restore runbooks. Never use local files copied to two writers as shared state.
6. Set alert thresholds from measured traffic, inference queue/service time, GPU/RAM and budget. Use independent Api.Capacity on a separate failure domain. It must remain advisory unless a fenced, audited actuation service is explicitly enabled.

See https://timeweb.cloud/docs/load-balancer and docs/capacity-resilience.md. No actual load balancer has been created by this change.
