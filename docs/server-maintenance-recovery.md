# Server-side maintenance and recovery

MysticEgo's native Ubuntu Orchestra installation deploys two independent root-owned systemd timers and the local setup socket. This is server functionality: no browser, CloudDesk client, or desktop login is required for periodic repair.

- `mysticego-orchestra-updater.timer` checks releases, verifies published asset digest and size, updates the app with node identity checks and existing rollback protection. It repairs the installed maintenance worker and rescue channel from the trusted, root-owned local release before contacting GitHub. A failed release is not retried indefinitely.
- `mysticego-infrastructure-reconcile.timer` probes and repairs managed dependencies using a fixed allowlist. Failed actions are rechecked after a 900-second cooldown; healthy components are not reinstalled.
- The updater takes the deployment lock, then the infrastructure lock, without blocking; the local component installer/reconciler uses the infrastructure lock. If busy, the updater leaves pending requests untouched. It starts the reconciler after releasing its locks. Local maintenance remains available with GitHub offline, although missing packages/model files may need network access.

## Recovery policy

| Observation | Automatic action |
| --- | --- |
| Maintenance unit, socket, timer, or installed scripts missing/outdated | Reinstall fixed local maintenance files from the verified root-owned application tree. |
| Maintenance report missing, malformed or older than 120 seconds | Schedule a reconciliation cycle; check again, with bounded retry. |
| LM Studio service account exists but its home was deleted | Recreate only the expected home with the account UID/GID; reject a symlink, unexpected owner or path. |
| libatomic1 / libgomp1 missing | Install required Ubuntu packages only. |
| LM service/unit/profile survives but CLI or cached model is missing | Provision missing artifacts for the existing profile; preserve valid cache, no role switch. |
| Managed LM files intact, expected loaded alias absent | Reset the failed unit state and restart the managed worker. Native model API must confirm the expected loaded instance. |
| Model provisioning lacks capacity | Defer, report ResourceDeferred, retry after cooldown. Initial conservative guard: 2 GiB available RAM, 3 GiB free disk for panel-data or 5 GiB for world. These are provisioning floors, not a runtime memory guarantee. |
| Model endpoint occupied during provisioning | Refuse takeover; report ModelPortOccupied; do not kill another process. |
| Voice service has missing/broken Python dependencies | Existing voice installer restores its dependencies, then probes readiness. |
| SQLite or external database not healthy | Diagnose only; do not delete, replace, switch providers, or rotate credentials. |

Status includes bounded reason codes, lastAttemptAt and nextRetryAt for recovery; repeated identical reports refresh checkedAt without consuming transition history. The owner infrastructure screen shows a retry time when reported. No arbitrary shell command, token, raw exception details or external URL can be supplied through the recovery path.

## Acceptance

Use an isolated test VPS for destructive/partial-deletion scenarios. Test missing home with retained account, missing CLI, missing model cache with retained profile, missing libgomp1, unrelated loaded model, occupied port, insufficient memory, stale/invalid report, concurrent local repair/update, network outage, reboot, and a successful inference. Verify no account/database/identity changes on ordinary updates or repairs. Do not delete live state to test this.

Factory reset remains a separate, explicitly approved CloudDesk operation; it now waits up to six minutes for both standard SQLite files and verifies identity again. This does not guarantee initialization under every workload. No automatic second reset, migration, swap creation, or modification of external model configuration is permitted.

Runtime evidence on 2026-11-28: manual restoration of the missing LM home and libgomp1 allowed the standard reconciler to install llmster and Qwen3-1.7B Q4_K_M. Six services were active, native API showed the loaded alias mysticego-panel-data, one chat completion took 2.627 seconds, available RAM 1.3 GiB before/after; no swap and no matching OOM events in the supplied log. This proves the recovery remedy, not that the new release has been deployed or that sustained concurrent load is safe.
