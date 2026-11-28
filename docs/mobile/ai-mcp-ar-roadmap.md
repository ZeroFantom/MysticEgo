# MysticEgo Mobile: AI, device requests and augmented reality

This slice builds on feature/android-mobile. The primary server and released desktop clients are unchanged until review.

## Runtime and routing
1. Server inference (existing Auth and assistant/workbench): recommended default, phone is a UI and VRM renderer. Local inference can instead use the native loopback adapter. External provider is opt-in only; no implicit fallback.
2. PocketPal AI supports GGUF and a local OpenAI-compatible API (the store build may require Plus). A separately installed llama.cpp server is an alternative. Keep bind address 127.0.0.1 and authentication enabled. Suggested initial models: compact 1–2B Q4 instruct, context 2–4K, validate RAM, performance and battery. Integrated runtime candidate LiteRT-LM; investigate a separate Kotlin module, SDK version, model license, import checksums, memory and thermal limits before adding native dependency. No model downloads by default.
3. For speech, reuse configured OpenAI-compatible transcription provider on Orchestra. User recording is foreground-only, explicitly uploaded, bounded; add local speech option only after hardware tests. Cloud model key stays on server. Use approved provider disclosure and quotas before enabling external API.

## Cross-device MCP
New endpoint on Auth for own active account sessions: device.request. Fixed action enum, one-time acceptance at target, bounded queue, no background daemon or remote code. Existing PC MCP remains operator/owner scoped and unchanged. Next phase is genuine device registration and device-scoped credential, explicit per-operation lease, current online state, target-native handler, structured execution result, revocation, audit and negative tests. Do not expose local desktop MCP port to WAN or relay arbitrary shell/ADB/accessibility inputs. Start with approved app aliases and read-only diagnostics on enrolled devices.

## AR phases
A: camera preview with current three-vrm pipeline, screen placement, manual motion selection; no world anchor.
B: ARCore optional native session (supported devices), camera pose + hit test on tracked planes, anchor per character, shared animation mapping and VRM renderer, tracking-loss status, resource lifecycle. Evaluate native graphics bridge vs WebXR compatible external browser; Android WebView cannot be assumed to implement immersive-ar.
C: spatial walk/sit/wave with safe floor targets, occlusion, light estimation, optional depth; local camera frames, no scene upload by default. Add action suggestions from model only as validated animation enum. AR closes on app background; no remote camera enable.

References:
- https://developers.google.com/edge/litert-lm/overview
- https://github.com/a-ghorbani/pocketpal-ai
- https://developers.google.com/ar/develop/fundamentals
- https://developers.google.com/ar/develop/webxr/arcore-comparison

## Release gate
Build/lint/negative tests; real phone test; signed APK; provider disclosure/legal review; no production/registry publication before complete.

External providers additionally require the human-readable MobileCloud:ProviderName and VoiceTranscription:ProviderName. Missing disclosure disables their respective new endpoints. Publish provider-specific privacy notices before production.

## Spatial WebXR prototype
The AR preview now explicitly offers a fixed Chrome URL, with independent browser sign-in. Android browser Assistant → Spatial AR loads the existing VRM, checks immersive-ar, starts a user-initiated WebXR session with hit-test, allows horizontal surface placement with a reticle and local-space pose, and supports idle/wave/nod. No persistent XRAnchor, depth occlusion, room mapping upload, or native ARCore renderer yet. The optional DOM overlay supplies motion controls; without it, system exit remains available. Verify with an ARCore-supported phone; CI typecheck cannot prove physical tracking. Reference: https://developers.google.com/ar/develop/webxr/hello-webxr .

MCP discovery now adds device.list, device.status and device.cancel. A 35-second opt-in foreground receive lease distinguishes an active refresh session from a session ready to accept requests. The UI stops receiving when hidden, and sender cancellation is limited to pending requests created by that session. Approval does not assert that navigation or OS execution succeeded. This remains a session-mediated app navigation slice, not device enrollment or a native OS agent.


### Native Android pilot

A separate Mobile Agent enrollment and dedicated MCP namespace (POST /api/auth/mobile-agent/mcp) now exist in the Android branch. Account authorization creates a short-lived single-use registration ticket; the installation receives a scoped credential protected by Keystore. Tools mobile.list, mobile.request and mobile.status operate on registered Android agents; the first action set is battery-status, vibrate, open-assistant. This is not an infrastructure NodeId or hardware attestation. Existing session-to-session device MCP remains separate. Durable execution audit, desktop native enrollment, pairing proof beyond possession of the ticket, Orchestra infrastructure integration, ARCore anchors and embedded inference remain later stages. Keep external release disabled until CI and two-device tests pass.


### Phone to PC read-only pilot

The canonical panel exposes a new MCP endpoint POST /api/v1/mobile-pc/mcp on Orchestra only. It requires an existing developer account, operator command access, an account-session claim and explicit operator configuration MobilePc:OwnerId (account UUID) / MobilePc:AllowedTargets (semicolon-separated connected agent UUIDs, no defaults). No target URLs are accepted. The existing SignalR registry and capability store must report the configured station online and advertising system.health or integrations.status. No arbitrary arguments or mutation actions are forwarded. The response includes only numeric runtime telemetry / boolean integration flags and the existing execution ID; the standard command journal records dispatch outcome. The account panel can explicitly load available stations and request diagnostics. This uses existing account permissions, not the Android agent credential, and is disabled until the operator opts in. It does not prove a locally granted PC consent lease or enable app.launch. For mutating commands, implement a separate target-local approval and scoped device mapping first.

Mobile Agent request states are now durable in Auth SQL. Current-session MCP status and same-account history are separate; pending requests expire, accepted requests without a receipt become result-unknown, and revocation closes pending/accepted requests. History is capped per account and pruned on access; retention scheduling remains a release gate.


### Local approval pilot for application launch

Orchestra MCP adds pc.launch.propose and pc.launch.status, gated by the existing owner/target configuration plus MobilePc:LaunchProposalsEnabled=true. SignalR only invokes proposal and status commands; neither executes the app. The target requires MYSTICEGO_MOBILE_LAUNCH_ENABLED=true, matching MYSTICEGO_AGENT_OWNER_USER, an interactive process and an alias in its existing MYSTICEGO_LAUNCH_ALLOWLIST_JSON. The local owner must visit the same-origin loopback Agent/Audit account-security panel, review the alias and exact path, then explicitly accept or decline. The local endpoint rejects forwarded/non-loopback access and device tokens; claim is one-shot and expires after two minutes. Re-resolve alias before execution and never forward command-line arguments. Target journal records decision; phone receives only proposal ID, alias, state and expiry. A launch-requested receipt is not proof that a program remained running. Queue is in-memory; lost on target restart. A local owner configuration and interactive desktop acceptance test are mandatory; no general remote shell, remote confirmation, or Session 0 launching. Existing legacy app.launch permissions are not expanded or audited by this feature.

### Embedded inference implementation

Pinned LiteRT-LM Android 0.18.0 and Kotlin 2.3.21, CPU-only trial with explicit file selection, independent SHA-256, private no-backup model file, bounded import, resource admission and separate process. Reference: https://developers.google.com/edge/litert-lm/android . No account token, history, toolset or network requests from this Activity. Model format compatibility, binary dependency provenance, ABI and physical device smoke are release gates. Manual choice between four inference modes replaces the former single AI toolbar button. Full hybrid scheduling with compute nodes, automatic load balancing and shared conversation context are separate future work.

### Spatial AR session anchors and walking

Existing Chrome WebXR / ARCore-backed immersive mode now requests optional anchors and attempts XRHitTestResult.createAnchor() for an explicit horizontal surface placement. When unavailable, it retains session-local placement and labels it as such. Track the anchor pose each frame, hide avatar when tracking is unavailable, discard stale asynchronous anchor creations, delete anchors on replacement/end, and do not persist scene data. A separate walk mode moves the avatar up to one metre towards a selected same-height hit point, with approximate procedural steps and reduced-motion handling; it is not a collision-aware path planner or proof of plane identity. Browser-only, no claim of native WebView immersive AR. Requires real-device testing on Chrome with ARCore and anchor support. No camera-frame upload.

### Account-session avatar handoff

Auth device-action MCP exposes avatar.handoff with targetId, motion, mood and phaseSeconds. Same-account receiving session, explicit readiness and target approval, two-minute expiry, bounded visual state. Target opens assistant or supported camera preview / spatial browser AR; one-shot memory slot scoped to preferenceScope. Camera and placement still require local actions. Spatial renderer maps unsupported motions to idle and does not implement mood expressions. Source remains active; approval is not rendering proof. Existing account avatar assets and same-server history are loaded independently, not copied. No native desktop overlay migration, unsent drafts, local-model context, audio or world coordinates. Test actual two-device usage before rollout.
