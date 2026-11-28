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
