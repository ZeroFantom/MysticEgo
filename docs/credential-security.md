# Credential security

## Implemented contract

- New login: 4–40 ASCII letters/digits/dot/underscore/hyphen, starts and ends with alphanumeric. Existing login spelling/case is preserved.
- New passwords: 15–128 Unicode code points by default, spaces allowed, no control characters. A small initial blocklist and repeated-character/login checks reject obvious weak passwords. Explicitly configured stricter composition rules remain supported. A comprehensive compromised-password blocklist is still needed.
- Shared CredentialPolicy is used for public registration, administrator creation, password changes and first bootstrap account. Existing accounts are not renamed or invalidated by the policy.
- IPasswordHasher resolves to VersionedPasswordHasher. New password hashes use PBKDF2-HMAC-SHA256, 600,000 iterations, random 16-byte salt, 32-byte output, versioned storage and fixed-time verification. Legacy BCrypt hashes remain verifiable, but are not automatically migrated; password change writes the new format. Existing BCrypt length limitations remain until a password change.
- Do not roll back to an Auth binary that cannot verify the new format after new passwords are written. Back up the database and plan a compatible rollback.
- Initial bootstrap credentials must meet the policy, including existing scripted provisioning of fresh environments. Do not place passwords or keys in source control or logs.

GET /api/auth/credential-policy returns public metadata only. Node gateways forward it to the configured Auth. Diagnostic MCP auth.credential_policy reads the same central endpoint; no credentials, tokens or hashes are accepted or returned.

## Architecture conventions

Controllers (HTTP/MCP) authenticate, validate transport and map errors; application services implement business operations; domain policy is shared; persistence and cryptography stay behind abstractions. New public C# types and methods should have XML summary documentation for behavior, trust boundaries and failure cases. MCP tools should call the same application services as HTTP rather than duplicate authorization or persistence. Do not expose credential changes or permission elevation directly to a model.

For future session synchronization: Auth is authoritative; use versioned events and bounded reconciliation, indexed queries, cancellation and bounded deadlines. Do not use stale allow decisions for privileged operations. Measure p50/p95/p99 and throughput before optimizing; no latency claims until measured. Existing large experimental PR #72 is separately reviewed, not a prerequisite for this credential change.

## Verification

Auth security workflow builds Auth and the distributed MCP/gateway project and runs AuthSmoke. Tests cover format, Unicode, salted hashes, invalid inputs, 72-byte suffixes and legacy verification. Integration tests for signup/password change, rate limits, old/new deployment compatibility and real MCP HTTP calls remain required before broad rollout.

References: https://pages.nist.gov/800-63-4/sp800-63b.html and https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html. These are design references, not a claim of formal compliance.
