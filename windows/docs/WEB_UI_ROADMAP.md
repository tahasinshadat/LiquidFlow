# LiquidFlow → OpenWhispr Parity Roadmap

**Target:** Full OpenWhispr feature parity on a native **C#/.NET core** (`FluidVoice.*`) with a **WebView2-hosted React UI**, Windows-first.

**How to read this doc**
- **Difficulty** (native-core lens): **Easy** = React 1:1 + thin C# shim (CRUD / localStorage / OS setting); **Medium** = a real C# subsystem (audio, process spawn, HTTP stream, sidecar); **Hard** = native/interop (LL hooks, WASAPI loopback, ONNX/GGML runtimes, OAuth, sync).
- **Status** column: `Done` (exists in LiquidFlow core today), `Partial` (core seam exists, UI/bridge missing), `Todo`, `Dropped` (Windows-only clone — no parity loss).
- Sections: **(1)** Parity checklist by area + priority · **(2)** Bridge API spec · **(3)** GPU/NPU plan · **(4)** Phased build order.

---

## 1. Parity Checklist

Priority bands run **Foundation → Core Dictation → Settings → Big Features**. Within each area, rows are roughly build-order.

### 1.0 Foundation (must exist before anything ships)

| Item | What it is | Difficulty | Status |
|---|---|---|---|
| **WebView2 bridge surface** | `postMessage` RPC + push events replacing Electron's ~200-channel `preload.js`. One marshaller, UI-thread hop. (See §2.) | Hard | Todo |
| **Settings store + persistence** | Flat `Settings.Current` singleton, `Save()`, `Changed` event; JSON blob + shallow patch over the bridge. | Medium | **Done** (core) / bridge Todo |
| **Secure secret storage** | API keys in Windows Credential Manager (DPAPI). Write-only across bridge; expose `hasKey`/`isConfigured` only. | Medium | **Done** (`CredentialStore`) / bridge Todo |
| **SQLite + FTS5** | transcriptions / notes / folders / conversations tables + full-text index (`Microsoft.Data.Sqlite`). | Medium | Partial (history store exists; notes/convos Todo) |
| **Sidecar lifecycle mgmt** | Spawn / health / reap child processes (llama-server, Qdrant, sherpa, AEC), PID files, orphan reaper. | Hard | Partial (`LocalAiServer`) |
| **Event marshaller** | Forward `Settings.Changed`, `HistoryChanged`, coordinator events, `ShortcutCaptured` to WebView on UI thread. | Medium | Todo |
| **Debug logging** | File-based leveled logs. | Easy | Partial |
| **Theme / appearance / UI scale / accent / font** | Light/Dark/System, `UiScale`, `AccentColor`, `AppFont`. | Easy | **Done** (settings) / UI Todo |
| **UI component library** | ~60 shadcn/Radix components port as-is into WebView2 React. | Easy | Todo |
| **i18n runtime** | react-i18next + main-process i18n, 10 locales. | Medium | Todo |
| **Error boundary** | Renderer crash fallback. | Easy | Todo |
| **Auto-updater** | Velopack/Squirrel feed, progress, release notes, beta channel. | Hard | Partial (`AutoUpdateCheckEnabled`/`BetaReleasesEnabled` flags exist) |
| **App shell / window manager** | Control Panel window, tray (NotifyIcon), window controls, launch-at-startup (Run key). | Medium | Partial |

### 1.1 Core Dictation (the product's beating heart)

| Item | What it is | Difficulty | Status |
|---|---|---|---|
| **Record → transcribe → paste loop** | Audio capture (NAudio/WASAPI) → STT → insert. | Medium | **Done** (`DictationCoordinator`) |
| **Dictation overlay window** | Always-on-top mic pill; idle/hover/recording/processing states, drag, position, auto-hide, size presets. | Medium | Partial (`OverlayWindow`; React overlay Todo) |
| **Activation modes** | Toggle / Hold (PTT) / Automatic — PTT needs `WH_KEYBOARD_LL` hook. | Hard | **Done** (`HotkeyMode`, `HotkeyManager`) |
| **Cancel recording / cancel processing** | X button + cancel hotkey mid-flight. | Medium | **Done** (`RequestCancel`, `CancelRecordingShortcut`) |
| **Auto-paste + keep-in-clipboard** | SendInput / clipboard paste; `TextInsertionMode`, `CopyTranscriptionToClipboard`. | Medium | **Done** |
| **Paste-last-transcription** | Re-insert last result via hotkey. | Easy | **Done** (`RequestPasteLast`) |
| **Mic device selection** | Pick input device, prefer built-in, stale-mic recovery. | Medium | Partial (`ListInputDevices`, `PreferredInputDeviceId`) |
| **Audio cues** | Start/stop sounds + volume. | Easy | **Done** (`EnableTranscriptionSounds`) |
| **Pause media on dictation** | SMTC pause of Spotify/etc. | Medium | **Done** (`PauseMediaDuringTranscription`) |
| **VAD auto-stop** | Silero VAD, silence-seconds threshold; model download. | Hard | **Done** (`VadAutoStop`) |
| **Recording validation** | Reject too-short/silent clips; save-discarded option. | Medium | Partial |
| **Streaming transcription preview** | Live partial text into overlay (cloud + Parakeet live). | Medium | Partial (`EnableStreamingPreview`; needs `PreviewTextChanged` event) |
| **Voice-agent hotkey route** | Dedicated hotkey → transcript straight to agent, bypass cleanup. | Medium | Todo |

### 1.2 Settings surfaces (mostly React ports over an existing core)

| Item | What it is | Difficulty | Status |
|---|---|---|---|
| **Hotkeys panel** | 4+ named slots, capture flow, conflict/reserved validation, auto-fallback. | Medium | **Done** (core) / UI Todo |
| **Speech-to-Text panel** | Model list, select, download/cancel/delete, progress bars; per-context (dictation/note/upload) sub-tabs. | Medium | Partial (`SpeechModels`, `ModelDownloader` — single-context) |
| **Language selector** | 58 STT languages + auto-detect; UI language. | Easy/Medium | **Done** (`WhisperLanguage`) / UI-lang Todo |
| **LLM providers panel** | Provider list, key entry, verify (list-models), model select, custom providers. | Medium | **Done** (`ProviderCatalog`, `LlmClient.ListModelsAsync`) |
| **Local LLM (llama.cpp / GGUF)** | Download/run local reasoning model, Vulkan accel, context tokens. | Hard | Partial (`LocalAiServer`, `LocalAiModelId`/`LocalAiContextTokens`) |
| **Prompt Studio** | Edit dictate/edit base+body prompts, reset to default, override detection. | Easy | **Done** (`PromptStore`) |
| **Prompt profiles + per-app routing** | Named profiles, app bindings, routing scope (all/selected). | Medium | **Done** (settings) / UI Todo |
| **Command mode + Rewrite mode config** | Linked-to-global toggle, own provider/model, confirm-before-execute. | Medium | **Done** (settings) / mode services exist |
| **Dictionary** | Custom entries (triggers→replacement) fed to STT as prompt. | Easy | **Done** (`CustomDictionaryEntries`) / UI Todo |
| **Auto-learn corrections** | Detect user edits in target app, promote to dictionary; threshold, dismiss. | Hard | **Done** (`CorrectionLearner`, `LearnedCorrections`) / edit-monitor is the hard part |
| **Formatting toggles** | Filler removal, punctuation convert, spacing, capitalization, casing. | Easy/Medium | **Done** (settings) |
| **Snippets / text expansion** | Trigger → expansion. | Easy/Medium | Todo |
| **History + stats** | Chronological list, original vs processed, copy/delete/clear, reveal audio, retry, stats (words today, streak, top apps, time saved, daily). | Easy/Medium | **Done** (`HistoryStore`) / UI Todo |
| **General / Privacy / System** | Startup, floating-icon, notifications, audio retention, storage usage, telemetry, data reset, model-cache mgmt, permissions cards (mic + system-audio). | Easy–Medium | Partial |
| **Onboarding wizard** | welcome → usecase → setup → permissions → hotkey → (agent/meeting) → finish; live mic/hotkey test. | Medium | Partial (`OnboardingCompleted`, `SetupTested`) |
| **Command palette (Ctrl-K)** | Global search over notes + transcripts. | Easy | Todo |

### 1.3 Big Features (net-new subsystems)

**Meetings + system audio**

| Item | What it is | Difficulty | Status |
|---|---|---|---|
| **System-audio loopback capture** | WASAPI process-loopback, self-excluding, into a live meeting note. | Hard | Todo |
| **Acoustic echo cancellation** | AEC helper + echo-leak detection for meeting capture. | Hard | Todo |
| **Meeting detection engine** | Coalesce process + mic-activity + calendar signals; cooldown, suppress-during-record. | Hard | Todo |
| **Meeting notification overlay** | "Record this meeting?" prompt window. | Medium | Todo |
| **Live meeting transcription banner** | Stream transcript into note; side-panel snap layout. | Hard | Todo |
| **Google Calendar sync** | OAuth loopback, multi-account, upcoming events. | Hard | Todo |
| **Meeting hotkey + layout mode** | Start meeting record; narrow vs full-width. | Medium | Todo |

**Diarization**

| Item | What it is | Difficulty | Status |
|---|---|---|---|
| **Speaker diarization** | Speaker embeddings, live speaker ID, assignment policy, lock speaker. | Hard | Todo |
| **ONNX worker process** | Out-of-process ONNX host (embeddings, speaker embeddings, fbank), crash-isolation + respawn. | Hard | Todo |
| **Participant / speaker labels** | Show/edit speakers, parse transcript segments. | Medium | Todo |

**Notes**

| Item | What it is | Difficulty | Status |
|---|---|---|---|
| **Notes CRUD + rich editor** | Markdown/rich text, note store. | Medium | Todo |
| **Folders + drag-and-drop** | Create/rename/assign; default + Meetings folder; reorder. | Easy/Medium | Todo |
| **Note actions (LLM)** | Summarize/etc., processing overlay + background toasts. | Medium | Todo |
| **Auto-generate title** | LLM titles the note. | Medium | Todo |
| **Embedded chat in note** | Chat scoped to one note. | Medium | Todo |
| **Upload audio → note** | Drop file, FFmpeg → WAV 16k, transcribe into note; own STT config. | Medium | Todo |
| **Save notes as files** | Mirror notes to disk folder, rebuild. | Medium | Todo |
| **Note sharing** | Cloud share + visibility. | Medium | Todo (cloud-gated) |

**Chat agent + vector search**

| Item | What it is | Difficulty | Status |
|---|---|---|---|
| **Chat view + conversations** | Multi-conversation, date grouping, persistence. | Medium | Todo |
| **Streaming + tool-call rendering** | SSE stream, tool-loop, tool icons. | Hard | Partial (`LlmClient` streaming exists for modes) |
| **Vector search (`search_notes`)** | Hybrid FTS5 + Qdrant semantic, RRF merge, cloud→local→FTS fallback; MiniLM ONNX embeddings. | Hard | Todo |
| **CRUD tools** | get/create/update note, list folders. | Medium | Todo |
| **Clipboard / web-search / calendar tools** | Agent-callable tools, gated by sign-in/gcal. | Easy–Medium | Todo |
| **Agent overlay window** | Hotkey-summoned floating chat. | Medium | Todo |

**Accounts / cloud (optional; can ship local-only first)**

| Item | Difficulty | Status |
|---|---|---|
| Cloud STT + BYOK streaming providers (Deepgram/AssemblyAI/OpenAI-Realtime/Corti) | Hard | Todo |
| Auth (email/pw, verify, forgot, re-auth gate) | Medium | Todo |
| Plans/billing (Stripe), usage metering, referral | Hard | Todo |
| Cloud backup / sync / migration | Hard | Todo |
| Workspaces / Teams (feature-flagged — deferrable with no parity loss) | Hard | Todo |
| Integrations: REST API keys, MCP, CLI loopback bridge (Kestrel 8200–8219) | Medium/Hard | Todo |

**Dropped (Windows-only clone — no parity loss):** macOS Globe/Fn key & AppleScript paste & dock indicator & post-migration bundle-ID onboarding; all Linux Wayland hotkey backends (GNOME/Hyprland/KDE D-Bus), pactl mic detection, ydotool/NixOS paste diagnostics.

---

## 2. Bridge API Spec

**Transport.** Single `postMessage` channel over WebView2.
- **Request** `{ id, method, params }` → **Response** `{ id, ok:true, result }` or `{ id, ok:false, error:{ code, message } }`.
- **Push event** (no `id`) `{ event, payload }`.
- All C# handlers async: `CoreWebView2.WebMessageReceived` → dispatch → `PostWebMessageAsJson`. Long-running ops (downloads, verify, installs) return a `jobId` immediately and stream `*.progress` events; each has a matching `cancel*` backed by a per-job `CancellationTokenSource`.
- **Method naming:** `domain.verb`.

**Cross-cutting rules**
1. **One event marshaller** hops every C# event to the `Dispatcher` before `PostWebMessageAsJson` (events fire on arbitrary threads).
2. **Secrets never cross the bridge** — keys are write-only; expose `hasApiKey`/`isConfigured` only. Do not replicate the WPF PasswordBox key-readback.
3. **Enums are strings on the wire** (`JsonStringEnumConverter` already configured): `"Dictate"`, `"ReliablePaste"`, `"Bottom"`, etc.
4. **Patch echo:** `settings.patch` → `Save` → `Settings.Changed` → `settings.changed` push. React should ignore the echo of its own patch (client token / `hint` compare).
5. **Two new coordinator events required:** `PreviewTextChanged` (raise in `EmitPartial`, `DictationCoordinator.cs:228`) and `DictationCompleted` (raise after `AddEntry`, `:406`). Everything else maps to existing public members.

### 2.1 Settings
```
settings.getAll()                          -> Settings           // whole Settings.Current, enums as strings
settings.patch({ changes })                -> { applied: string[] }   // shallow-merge + Save("bridge"); keys excluded
settings.reset({ keys? })                  -> Settings
event settings.changed { hint }
```
Collections React edits directly (sent as replacement arrays via `settings.patch`): `CustomDictionaryEntries`, `PromptProfiles`, `AppPromptBindings`, `CustomProviders`, `FillerWords`, `LearnedCorrections` flags, prompt-routing scope, selected-prompt ids/off toggles. Nested JSON shapes: `CustomDictionaryEntry{Id,Triggers[],Replacement}`, `PromptProfile{Id,Name,Mode,Prompt}`, `AppPromptBinding{Id,AppId,AppDisplayName?,Mode,PromptId?}`, `CustomProvider{Id,Name,BaseUrl}`, `HotkeyShortcut{Kind,VirtualKey,Modifiers[],MouseButton,IsModifierOnly}` (pass-through; produced by `hotkey.captured`).

### 2.2 Speech models
```
models.list()                              -> { selectedId, models: SpeechModel[] }
models.select({ id })                      -> { selectedId }
models.download({ id })                    -> { jobId }
models.cancelDownload({ jobId })           -> {}
models.delete({ id })                      -> { isDownloaded:false }
event models.downloadProgress { jobId, id, phase, fraction, error? }
```
`SpeechModel = { id, displayName, tagline, description, engine, languageSupport, sizeDisplay, expectedBytes, ramEstimate, badge, speedPercent, accuracyPercent, supportsLivePreview, isDownloaded }`. `phase ∈ PreparingDownload|Downloading|Loading|Ready|Failed`.

### 2.3 History + stats
```
history.list({ query?, limit?, offset? })  -> { total, entries: HistoryEntry[] }
history.delete({ ids })                     -> {}
history.clear()                             -> {}
history.stats({ days? })                    -> { wordsToday, transcriptionsToday, totalWords,
                                                 currentStreakDays, aiEnhancementRate,
                                                 timeSavedTodayMinutes, timeSavedTodayLabel,
                                                 topApps:[{app,count}], daily:[{date,words}] }
event history.changed {}
```
`HistoryEntry = { id, timestamp, rawText, processedText, appName, windowTitle, wasAIProcessed, wasCancelled, processingModel, aiProcessingError, characterCount, wordCount, audio:{fileName,durationMilliseconds,sampleRate,channels,model}|null }`.

### 2.4 Providers + keys + verify
```
providers.list()                           -> { selectedProviderId, providers: Provider[] }
providers.setKey({ id, apiKey|null })      -> { hasApiKey }          // write-only
providers.selectModel({ id, model })       -> {}
providers.select({ id })                    -> {}                     // "" = AI off
providers.verify({ id })                    -> { verified, models, error? }   // ListModelsAsync -> save -> MarkVerified -> Save
providers.addCustom({ name, baseUrl })      -> Provider
providers.removeCustom({ id })              -> {}
providers.getModeRouting()                  -> { command:{linked,providerId,model,effectiveProviderId,effectiveModel,confirmBeforeExecute},
                                                 rewrite:{linked,providerId,model,effectiveProviderId,effectiveModel} }
providers.setModeRouting({ mode, patch })   -> {}
providers.localAi.status()                  -> { runtimeInstalled, selectedModelId, models:[{id,displayName,description,installed,installedBytes,expectedBytes}] }
providers.localAi.install()                 -> { jobId }
providers.localAi.selectModel({ id })       -> {}
providers.localAi.delete({ id })            -> {}
event providers.localAi.progress { jobId, phase, fraction, error? }
```
`Provider = { id, name, baseUrl, group, isLocal, needsApiKey, hasApiKey, isConfigured, curatedModels, availableModels, selectedModel }`.

### 2.5 Prompts
```
prompts.get({ mode })                       -> { basePrompt, builtInBody, effectiveBody, hasOverride }
prompts.set({ mode, body|null })            -> { hasOverride, effectiveBody }   // blank or ==builtin clears
```
(`mode ∈ Dictate|Edit`. Profiles/bindings/selected-ids/off flags via `settings.patch`.)

### 2.6 Dictation control + events
```
dictation.status()                          -> { activeMode, isProcessingStop, aiConfiguredForFocusedApp }
dictation.start({ mode })                    -> {}
dictation.stop()                             -> {}
dictation.cancel()                           -> {}
dictation.pasteLast()                        -> {}
event dictation.recordingStateChanged { recording, mode }
event dictation.statusChanged { status }              // "Transcribing"/"Refining..."/"Ready"/"Error"
event dictation.preview { text }                      // NEW (PreviewTextChanged)
event dictation.completed { entryId }                 // NEW (DictationCompleted)
```

### 2.7 Hotkey capture
```
hotkey.beginCapture()                        -> {}   // HotkeyManager.CaptureMode = true
hotkey.endCapture()                          -> {}
event hotkey.captured { shortcut }           // React assigns to field, then settings.patch
```

### 2.8 VAD
```
vad.status()                                 -> { enabled, silenceSeconds, modelInstalled, modelBytes }
vad.downloadModel()                          -> { jobId }
event vad.downloadProgress { jobId, phase, fraction, error? }
```

### 2.9 Dictionary + learned corrections
```
dictionary.list()                            -> { entries }
dictionary.upsert({ entry })                 -> { entries }
dictionary.remove({ id })                    -> { entries }
corrections.list()                           -> { corrections:[{from,to,count,promoted,dismissed}] }
corrections.promote({ from, to })            -> { entries, corrections }   // CorrectionLearner.Promote
corrections.dismiss({ from, to })            -> { corrections }
corrections.setEnabled({ enabled, threshold? }) -> {}
```

### 2.10 Audio devices
```
audio.listInputDevices()                     -> { devices:[{id,name,isDefault}], preferredId }
audio.selectInputDevice({ id|null })         -> {}
```

### 2.11 Command / Rewrite mode (only if React hosts those screens)
```
command.state() | command.send({text}) | command.confirmPending() | command.cancelPending()
                | command.newChat() | command.openChat({id}) | command.deleteChat()
  events: command.stateChanged, command.streamingText {delta}, command.confirmationNeeded {command}
rewrite.state() | rewrite.apply({instruction}) | rewrite.accept() | rewrite.tryAgain()
  event: rewrite.stateChanged
```

**Deferred (big features), same conventions:** `notes.*`, `folders.*`, `chat.*` (+ `chat.streaming`/`chat.toolCall` events), `search.notes`, `meeting.*` (+ `meeting.detected`/`meeting.transcript` events), `calendar.*`, `upload.*`, `account.*`/`billing.*`/`usage.*`.

---

## 3. GPU / NPU Acceleration Plan

**Hardware:** Snapdragon X Elite (Adreno GPU + Hexagon NPU), Windows-on-ARM.

### The hard truth
The sherpa-onnx managed API **exposes** `ModelConfig.Provider` and the app already sets it (`ParakeetEngine.cs:57,91`, currently `"cpu"`), **but the shipped win-arm64 native stack is CPU-only.** The bundled `onnxruntime.dll` (v1.24.4) has **no DirectML EP and no QNN EP** linked, and `sherpa-onnx-c-api.dll` was built **without** `SHERPA_ONNX_ENABLE_QNN` / `SHERPA_ONNX_ENABLE_GPU`. Flipping `Provider="qnn"` or `"directml"` today just logs *"Fallback to cpu"*. **Real acceleration = swapping the native stack + model conversion, not a config change.** No quick win.

Whisper.net on WoA is **CPU-locked**: its win-arm64 runtime is CPU-only; the Vulkan runtime ships **x64 only** (no win-arm64); there is **no** QNN/Hexagon whisper.cpp backend. Keep Whisper CPU or prefer Parakeet.

### Recommended approach (ranked, most→least realistic)

**Step 0 — ship now, zero risk: `xnnpack` CPU path.**
Set `config.ModelConfig.Provider = "xnnpack"` on both the offline and streaming recognizers. It's honored by the current native lib, it's a genuine CPU speedup over the default, and it's a clean A/B against `"cpu"`. Combine with `NumThreads` tuning (currently `ProcessorCount-2`, cap 8) + warmup. The Oryon cores are strong — for short (<15 s) dictation this is already near-real-time and likely captures most of the practical benefit. **Do this first.**

**Spike 1 — Parakeet encoder on QNN EP (Hexagon NPU): highest ceiling, highest effort.**
- Requires: (a) a **QNN-enabled arm64 ORT** (`Microsoft.ML.OnnxRuntime.QNN` 1.24.4 has arm64 binaries — matches bundled ORT version) swapped in for sherpa's stock CPU `onnxruntime.dll`, **plus** a sherpa build compiled with `-DSHERPA_ONNX_ENABLE_QNN=ON`, **or** bypass sherpa's transducer wrapper and drive ORT directly for the encoder.
- Requires: convert `encoder.int8.onnx` to QNN-friendly **static-shape QDQ** and pre-compile an **EPContext** binary (avoids multi-second per-session compile).
- Constraints that shape the result: HTP runs **quantized only** (QDQ int8/int16), **no dynamic shapes** (must fix/pad the encoder time dim), **no `Loop`/`If`** (the `nemo_transducer` decoder/joiner decode loop stays on **CPU**). So expect **partial offload** — encoder on NPU, decode loop on CPU.
- **Risk: high.** Model surgery + per-op fallback debugging + custom native bundle maintenance. Payoff real (encoder is the compute-heavy part) but not guaranteed to beat well-threaded int8 CPU on short clips. Scope as an experiment.

**Spike 2 — Parakeet encoder on DirectML EP (Adreno GPU): lower effort than QNN, less certain payoff.**
- `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 has arm64 binaries; DML supports **dynamic shapes and float** → **less model surgery** than QNN. Same structural blocker: needs a DML-enabled ORT + GPU-capable sherpa build (or drive ORT directly).
- Adreno-via-DirectML transformer throughput on WoA is modest and driver-sensitive; for short dictations, kernel-launch/transfer overhead can erase gains. **Risk: medium; payoff: uncertain.**

**Not viable here:** Whisper on any accelerator (no arm64 GPU/NPU runtime exists); CUDA/CoreML (wrong vendor).

**Alternative worth watching:** Windows 11 24H2 Arm now ships the Qualcomm QNN EP via Windows Update (KB5096135) and Windows ML can broker EPs — but that targets the OS's ORT, not sherpa's private copy, so it doesn't help the current sherpa integration without the same native-stack swap.

### Decision
1. **Now:** land `xnnpack` + thread/warmup tuning (Step 0). Ship it.
2. **Later, as a boxed spike (not a toggle):** Parakeet-encoder QNN EP (Spike 1) as the acceleration bet with the highest ceiling; fall back to DirectML (Spike 2) if QNN model conversion proves too costly. Gate both behind a settings flag and A/B against the tuned CPU baseline before shipping.

---

## 4. Phased Build Order

Each phase is **independently shippable and testable** — it ends with a working app a user could run.

### Phase 0 — Bridge + Shell (Foundation)
**Goal:** WebView2 app boots, renders React, round-trips settings.
- WebView2 host window + React app + component library port.
- Bridge transport (§2 request/response/event envelope) + one event marshaller (UI-thread hop).
- `settings.getAll` / `patch` / `reset` + `settings.changed`; theme/scale/accent live.
- Debug logging, error boundary, launch-at-startup, tray, window controls.
**Ship test:** change a setting in React → persists → survives restart → tray + theme work.

### Phase 1 — Core Dictation MVP (usable product)
**Goal:** hotkey → speak → text appears in the focused app, with history.
- Wire `DictationCoordinator` over the bridge: `dictation.*` methods + 4 events (incl. the two new `preview`/`completed`).
- React dictation overlay window (states, drag, position, size).
- `models.*` (list/select/download/delete + progress), `audio.*` device picker, `hotkey.*` capture, `vad.*`.
- `history.*` (list/delete/clear/stats) + History view.
- Formatting toggles, audio cues, pause-media, cancel/paste-last.
- **STT perf: land the `xnnpack` Step-0 change here.**
**Ship test:** press hotkey anywhere → dictate → text inserted; overlay animates; history + stats populate; VAD auto-stop works.

### Phase 2 — AI Enhancement + Settings Completeness
**Goal:** dictation gets cleaned up by an LLM; all non-big-feature settings are UI-complete.
- `providers.*` (list/verify/setKey/selectModel/select/custom + local-AI install), `prompts.*`, Prompt Studio.
- Prompt profiles + per-app routing UI; Command mode + Rewrite mode config + services wired.
- `dictionary.*` + `corrections.*` (+ the foreign-app edit-monitor for auto-learn).
- Snippets, command palette (Ctrl-K), full General/Privacy/System panels, permissions cards.
- Onboarding wizard end-to-end.
**Ship test:** configure a provider (verify passes, key stored write-only), dictate with cleanup on; per-app prompt routing fires; onboarding completes cleanly.

### Phase 3 — Notes + Local Reasoning
**Goal:** a notes workspace with LLM actions; upload-to-transcribe.
- SQLite notes/folders schema + FTS5; `notes.*` / `folders.*`; rich editor, drag-and-drop.
- Note actions (LLM), auto-title, embedded per-note chat.
- Upload-audio → note (FFmpeg → WAV 16k) with its own STT config.
- Save-notes-as-files mirror.
- Harden local LLM (llama.cpp/GGUF) via `LocalAiServer` + sidecar supervision.
**Ship test:** create/edit/organize notes; run an LLM action; drop an audio file and get a transcribed note.

### Phase 4 — Chat Agent + Vector Search
**Goal:** conversational agent over your notes with tools.
- ONNX worker process (crash-isolated) + MiniLM embeddings; Qdrant sidecar; RRF hybrid `search.notes` (cloud→local→FTS fallback).
- `chat.*` with streaming + tool-call rendering; CRUD/clipboard tools.
- Agent overlay window (hotkey-summoned).
**Ship test:** ask the agent a question that requires searching notes; it streams, calls `search_notes`, cites, and can create/update a note.

### Phase 5 — Meetings + Diarization
**Goal:** record meetings with system audio, live transcript, speakers.
- WASAPI process-loopback (self-excluding) + AEC + echo-leak.
- Meeting detection engine (process + mic-activity + calendar) + notification overlay + side-panel layout.
- Live meeting transcription banner into a note.
- Diarization (speaker embeddings, live speaker ID) via the ONNX worker; participant labels.
- Google Calendar OAuth + upcoming events; meeting hotkey.
**Ship test:** join a Zoom/Teams call → prompted to record → mic+system captured, live transcript with speaker labels lands in a Meetings-folder note.

### Phase 6 — Cloud / Accounts (optional, feature-flagged)
**Goal:** parity for the cloud tier; ship dark until ready.
- Auth (sign-in/up/verify/forgot/re-auth), cloud STT + streaming BYOK providers, usage metering.
- Billing (Stripe), referral, cloud backup/sync/migration.
- Integrations: REST API keys, MCP, CLI loopback bridge (Kestrel 8200–8219).
- Enterprise LLM providers (Bedrock/Azure/Vertex), Tinfoil. Workspaces/Teams behind the feature flag.
**Ship test:** sign in, transcribe via cloud, hit usage limit toast, back up notes, drive the app from the CLI bridge.

### Acceleration track (parallel, not a phase gate)
Runs alongside Phase 1+. Ship the `xnnpack` baseline immediately; box the QNN/DirectML spike (§3) as an experiment gated behind a settings flag, A/B'd against the tuned CPU baseline before any promotion.

---

### One-glance phase → shippable value

| Phase | Shippable outcome |
|---|---|
| 0 | React shell that persists settings |
| 1 | **Working dictation app** (hotkey → text → history) |
| 2 | Dictation with AI cleanup + all settings complete |
| 3 | Notes workspace + upload-to-transcribe |
| 4 | Chat agent with vector search over notes |
| 5 | Meeting recording with live transcript + diarization |
| 6 | Full cloud/account parity |
