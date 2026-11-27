# MysticEgo BETA7 R3.2.39

- CloudDesk: accept public or private project repository, restore maintenance RPC and guarded exact-release updates.
- Linux RUN installer: diagnostics for downloaded executable permissions and noexec filesystems.
- Ubuntu 24.04+: provision missing libatomic1 before LM Studio. Desktop Linux can request PolicyKit approval for the dependency; Windows x64 retains verified vendor bootstrap.
- Unified maintenance plan in Orchestra and CloudDesk, live systemd load, per-service and consolidated XLSX/JSON/TXT diagnostic exports.
- Opt-in unattended Orchestra release updates with checksum verification, existing rollback and no repeated automatic failed tag attempts.
- Owner-only live runtime self-tests with bounded history and report exports; destructive end-to-end tests remain in CI/staging.
- Credential hashing and HTTP admission limits from merged security increments.

Existing Orchestra R3.2.38 uses its normal updater to install this release. Auto-update is off until explicitly enabled in the new panel. The build workflow publishes only after its checks. Independent multi-Orchestra HA, capacity planning and development workspace draft features are not included unless separately merged and verified. See docs/unified-maintenance.md.
