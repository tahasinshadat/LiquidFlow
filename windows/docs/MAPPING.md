# macOS → Windows subsystem mapping

Every macOS system integration in FluidVoice is re-implemented with a working Windows equivalent.
File references point into the Swift sources (the spec) and the C# port.

| Capability | macOS (Swift) | Windows (this port) | Where |
| --- | --- | --- | --- |
| **Global hotkey** | CGEvent tap (`GlobalHotkeyManager.swift`) | `WH_KEYBOARD_LL` + `WH_MOUSE_LL` low‑level hooks on a dedicated message‑loop thread | `Input/KeyboardHook.cs`, `Input/HotkeyManager.cs` |
| **Type into any app (Smart Typing)** | AX API / CGEvent unicode / clipboard paste (`TypingService.swift`) | `SendInput` `KEYEVENTF_UNICODE` (200‑unit surrogate‑safe chunks) → clipboard `Ctrl+V` → char‑by‑char | `Typing/NativeInput.cs`, `Typing/TypingService.cs` |
| **Read selection (Write/Edit)** | AX `kAXSelectedText` / `Cmd+C` (`TextSelectionService.swift`) | UI Automation `TextPattern.GetSelection()` → `Ctrl+C` with clipboard save/restore | `Typing/SelectionReader.cs` |
| **Clipboard save/restore** | `NSPasteboard` snapshot (`ClipboardService.swift`) | Win32 clipboard, all‑format snapshot, restore after 5 s unless changed externally | `Typing/ClipboardService.cs` |
| **Foreground app + focus restore** | `NSWorkspace` + AX raise (`ActiveAppMonitor.swift`) | `GetForegroundWindow` + `AttachThreadInput` + `SetForegroundWindow` (40 ms + 3×50 ms retries) | `Typing/FocusTracker.cs` |
| **Per‑app identity** | bundle id | lowercase process name (e.g. `notepad`) | `Typing/FocusTracker.cs` |
| **Menu bar item** | `NSStatusItem` (`MenuBarManager.swift`) | tray `NotifyIcon` (runtime‑drawn "F" glyph, red while recording) | `Ui/TrayIcon.cs` |
| **Live overlay** | notch‑aware `NSPanel` (`BottomOverlayView.swift`) | topmost, click‑through‑style, non‑activating (`WS_EX_NOACTIVATE|TOOLWINDOW`) WPF window; notch logic dropped, pill/small/medium/large kept | `Ui/OverlayWindow.cs` |
| **Mic capture** | Core Audio (`DirectCoreAudioInput.swift`) | WASAPI via NAudio, resampled to 16 kHz mono f32 | `Audio/AudioRecorder.cs` |
| **On‑device STT** | CoreML/Metal models + whisper.cpp | **whisper.cpp via Whisper.net (ARM64‑native)** | `Stt/WhisperEngine.cs` |
| **Secure key storage** | Keychain (`KeychainService.swift`) | Windows Credential Manager (`CredWrite`/`CredRead`) | `Core/CredentialStore.cs` |
| **Settings store** | `UserDefaults` (`SettingsStore.swift`) | JSON in `%LOCALAPPDATA%` | `Core/Settings.cs` |
| **Media pause/resume** | MediaRemote (`MediaPlaybackService.swift`) | `GlobalSystemMediaTransportControlsSessionManager` (WinRT) | `App/MediaPauseService.cs` |
| **Command‑mode shell** | `/bin/zsh` + `osascript` (`TerminalService.swift`) | `powershell.exe -NoProfile -NonInteractive` | `Modes/TerminalService.cs` |
| **Launch at login** | `SMAppService` (`SettingsStore+LaunchAtStartup.swift`) | `HKCU\...\Run` registry value | `App/StartupManager.cs` |
| **Auto‑update** | Sparkle (`SimpleUpdater.swift`) | GitHub releases API + installer download, stable/beta channels | `App/Updater.cs` |
| **Notifications** | `UNUserNotificationCenter` (`NotificationService.swift`) | tray balloon tips | `App/Notifications.cs` |
| **Permissions** | mic + Accessibility prompts | Windows mic privacy consent (Settings → Privacy → Microphone); global hooks need **no** elevation for non‑elevated foreground apps | `app.manifest` (`asInvoker`) |
| **Cloud AI providers** | `LLMClient.swift` | OpenAI‑compatible (stream + tools) + Anthropic messages API | `Ai/LlmClient.cs` |

## Behavior preserved exactly

These were ported to match the mac constants/logic, not just the shape:

- **Activation state machine** — Toggle / Hold / Automatic with the 400 ms tap‑vs‑hold threshold,
  bare‑modifier clean‑press detection, and the deferred‑stop (60×50 ms) when a hold is released
  before recording actually started. `Input/HotkeyManager.cs`
- **Audio math** — 16 kHz target, RMS noise gate 0.002, dB normalize `(dB+55)/55`, smoothing
  `0.7·new + 0.3·avg(2)`, min‑1 s padding before decode. `Audio/AudioRecorder.cs`
- **Formatting pipeline** — filler removal → custom dictionary → the full **115‑rule spoken
  punctuation** table (all spacing classes, quote toggles, dot/slash/@ context rules, comma‑noise
  cleanup) → GAAV post‑LLM cleanup, in the exact mac order. `Text/SpokenPunctuation.cs`,
  `Text/TranscriptFormatter.cs`
- **Prompts** — the dictation and edit system prompts + default bodies are **verbatim** from
  `SettingsStore.swift`, with the same `combineBasePrompt` / `${transcript}` / `{context}`
  rendering and per‑app routing resolution. `Ai/PromptStore.cs`
- **Command mode** — same agentic workflow system prompt (shell/app sections adapted to
  PowerShell/Windows), single `execute_terminal_command` tool, temp 0.1, 20‑turn cap,
  destructive‑command confirmation gate, 30‑chat history. `Modes/CommandModeService.cs`
- **Write/Edit mode** — selection‑vs‑no‑selection branch, the verbatim instruction templates,
  non‑streaming, temp 0.7, replace‑selection by typing. `Modes/RewriteModeService.cs`

## Fluid Intelligence substitution

"Fluid Intelligence" is a **proprietary, closed** local runtime that is **not in the GPLv3 repo**,
so it cannot be ported. To match its *behavior*, this port implements the enhancement layer with an
**open local LLM**:

- **`Ai/LocalAiServer.cs`** manages a [llama.cpp](https://github.com/ggml-org/llama.cpp)
  `llama-server` child process (pinned **win‑arm64 CPU** build) serving a small instruct GGUF
  (Qwen2.5‑1.5B‑Instruct by default; 0.5B and 3B also offered) over a localhost OpenAI‑compatible API.
- It is wired in as a provider (`fluid-local`) in `Ai/ProviderCatalog.cs` and goes through the
  **same** prompt templates, gating, thinking‑tag stripping, and GAAV post‑processing as every other
  provider — so the smart‑formatting / context‑aware‑capitalization / punctuation‑cleanup behavior
  matches, fully on‑device.
- It is clearly labeled **"Fluid Local AI (open substitute)"** in the UI. No proprietary Fluid
  Intelligence artifacts are downloaded or bundled.
