# Historical components: integration contract

Status: design proposal, not implemented or deployed. Historical snapshots are kept separately; this document contains no original private documents, tokens, user data or legacy configurations.

## Target architecture

MysticEgo remains the main application platform. Integrations use existing application services, authenticated identity and versioned contracts instead of parallel accounts, chat history or progression systems.

| Capability | Integration target | Required work |
| --- | --- | --- |
| Dynamic RPG quests and NPC dialogs | services/MysticEgo.Soul; services/MysticEgo.WorldAI | versioned quest proposals, structured validation, context limits |
| Progression and attributes | services/MysticEgo.Soul/GameProgress.cs | compare historical formulas with current progression; preserve compatibility of stored souls |
| Asynchronous inference | WorldAiJobQueue | existing role restrictions, bounded jobs, idempotency, retries and cancellation |
| Social and external bot adapters | Existing authenticated chat/profile application services | account linking with explicit consent, scope isolation, deduplication, revocation |
| Organization, roles, tasks and assets | Existing Orchestra and Auth domain contracts | compare policies; no parallel source of identity or organization ownership |
| Desktop features | Existing desktop and UI automation modules | reuse independent UX ideas; retain device-local permission checks |

## First vertical slice: quest proposal

Current SoulEngine exposes a catalog and records quest attempts; GameProgress calculates experience from passed attempts. WorldAI already has a persistent single-Orchestra queue. Do not replace these with a legacy model calling a chatbot synchronously.

1. Define a versioned QuestProposal DTO containing bounded title, description, difficulty, objective and references to allowed facts. Model output is untrusted data.
2. Queue an authorized request via WorldAI with an idempotency key scoped to user, world and request. Do not include credentials or unrelated private conversation history.
3. Validate schema, bounds, referenced facts and ownership; reject malformed or unknown fields. AI proposes narrative, not authority.
4. Derive rewards and penalties from server policy, never directly from AI response or client-supplied values. No award on timeout, invalid output or provider failure.
5. Persist a quest instance and completion event transactionally with a unique completion key. Retry cannot duplicate experience or currency; preserve existing Soul progress and migration compatibility.
6. Expose the same application contract to web/desktop and optional Telegram/VK adapters. External adapters must not implement their own economy or account database.

## Acceptance gates

- malformed JSON, excessive fields and prompt-injection strings cannot mutate balances;
- cross-user or cross-world identifiers fail authorization;
- concurrent completion and retry award once;
- queue restart and cancellation are deterministic;
- existing static quests, stored souls, allocation and progression tests still pass;
- AI unavailable: existing gameplay works, no new economic effects;
- external accounts can be linked and revoked without exposing provider tokens;
- test using isolated fixtures, never production databases or historical credentials.

## Repository policy

Retain original snapshots and upstream license/attribution. Move only reviewed components with source commit provenance, explicit license compatibility and tests. Do not merge unrelated examples, forks or credentials into the main repository. Keep private author materials outside public GitHub. Archive old repositories only after their unique history and needed code are accounted for.
