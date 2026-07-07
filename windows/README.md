# FluidVoice for Windows

A faithful Windows port of [FluidVoice](https://github.com/altic-dev/FluidVoice), the local‑first
voice dictation app for macOS. Same core loop, same flows, same formatting and prompt logic —
rebuilt natively for **Windows on ARM (ARM64)** with x64 also supported.

> Global hotkey → capture mic → on‑device speech‑to‑text → type (or rewrite) into whatever app is
> focused, with optional AI enhancement, plus **Command** and **Write/Edit** modes. It lives in the
> system tray, shows a live transcription overlay, keeps local history + usage stats, supports
> per‑app prompt config, and lets you bring your own cloud AI keys. **Nothing leaves your machine
> unless you opt into a cloud provider.**

The macOS Swift codebase is the behavioral specification for this port. See
[`docs/SPEC.md`](docs/SPEC.md) for the exact behavior extracted from it,
[`docs/MAPPING.md`](docs/MAPPING.md) for how every macOS subsystem maps to Windows, and
[`docs/PARITY.md`](docs/PARITY.md) for the feature‑by‑feature status.

---

## Stack

- **C# / .NET 9 + WPF + WinForms interop** — native, no web runtime, fast startup, ARM64‑native.
- **[Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp)** — on‑device STT, ships a **win‑arm64 native** build. This is the parity baseline.
- **[NAudio](https://github.com/naudio/NAudio) / WASAPI** — microphone capture.
- **[llama.cpp](https://github.com/ggml-org/llama.cpp) `llama-server` (ARM64 CPU build)** — the open, local substitute for the proprietary *Fluid Intelligence* enhancement runtime.
- **Windows Credential Manager** — API‑key storage (Keychain equivalent).
- **Inno Setup** — installer.

Everything is ARM64‑native (not x64‑emulated) for low latency.

---

## Quick start (users)

1. Download **`FluidVoice-Setup-<version>-arm64.exe`** (or `-x64.exe`) from Releases and run it.
2. On first launch the dashboard opens. Grant microphone access when Windows asks.
3. Go to **Speech Models** and download a Whisper model (Base is the default, ~140 MB).
4. Press **Right Alt** anywhere to start dictation; press it again to stop. The text types into the
   focused app.
5. Optional: **AI Enhancement** tab to add a cloud provider key or set up local **Fluid Local AI**.

Default hotkeys: dictation = **Right Alt**, Edit/Write = **Alt+R**, Command = *(unset)*, cancel =
**Esc**. All configurable under **General → Hotkeys**, including Toggle / Hold / Automatic activation.

---

## Build from source

Prerequisites: **.NET 9 SDK** (win‑arm64), and for installers **[Inno Setup 6](https://jrsoftware.org/isdl.php)**.

```powershell
cd windows

# run in place (Debug)
dotnet run --project src/FluidVoice.App

# self-contained publish (ARM64 primary)
dotnet publish src/FluidVoice.App -c Release -r win-arm64 --self-contained true -o publish/arm64
# x64 too (optional)
dotnet publish src/FluidVoice.App -c Release -r win-x64  --self-contained true -o publish/x64

# build installers + portable zips for one or both arches
pwsh installer/build.ps1 -Arches arm64,x64
# → windows/dist/FluidVoice-Setup-<ver>-arm64.exe, FluidVoice-portable-<ver>-arm64.zip, ...
```

### Headless self-tests

No mic or GUI needed — these exercise the real pipeline:

```powershell
# STT + formatting: transcribe a WAV, run the on-device formatting pipeline
dotnet run --project src/FluidVoice.App -- --selftest-stt path\to\audio.wav whisper-base

# LLM client: list/non-stream/stream/tool-call against an OpenAI-compatible endpoint
dotnet run --project src/FluidVoice.App -- --selftest-llm http://127.0.0.1:8899/v1
```

There is also a test seam for driving the full hotkey→type loop deterministically: set
`FLUIDVOICE_TEST_AUDIO=<wav>` and the recorder streams that file through the real capture buffer
instead of the microphone. `FLUIDVOICE_ALLOW_INJECTED=1` lets the global hook accept synthetic
(SendInput) keystrokes so the flow can be automated.

---

## Models & where things live

| What | Location |
| --- | --- |
| Settings (JSON) | `%LOCALAPPDATA%\FluidVoice\settings.json` |
| Transcription history | `%LOCALAPPDATA%\FluidVoice\history.json` |
| Command‑mode chats | `%LOCALAPPDATA%\FluidVoice\command-chats.json` |
| Whisper models | `%LOCALAPPDATA%\FluidVoice\Models\Whisper\` |
| Local AI (llama.cpp + GGUF) | `%LOCALAPPDATA%\FluidVoice\LocalAI\` |
| Audio history (opt‑in) | `%LOCALAPPDATA%\FluidVoice\DictationAudioHistory\` |
| Logs | `%LOCALAPPDATA%\FluidVoice\Logs\` |
| API keys | Windows Credential Manager → `FluidVoice/ProviderAPIKeys` |

**Speech models** (all Whisper, 99 languages, on‑device, downloaded on demand from
`huggingface.co/ggerganov/whisper.cpp`):

| Model | Size | Speed | Accuracy |
| --- | --- | --- | --- |
| Whisper Tiny | ~74 MB | ★★★★½ | ★★ |
| **Whisper Base** (default) | ~141 MB | ★★★★ | ★★★ |
| Whisper Small | ~465 MB | ★★★ | ★★★½ |
| Whisper Medium | ~1.4 GB | ★★ | ★★★★ |
| Whisper Large Turbo | ~1.5 GB | ★★★ | ★★★★★ |
| Whisper Large | ~2.9 GB | ★ | ★★★★★ |

The Apple‑Silicon‑only engines from the mac app (Parakeet, Nemotron, Cohere CoreML, Apple Speech)
cannot run on Windows on ARM; see [`docs/PARITY.md`](docs/PARITY.md).

**Fluid Local AI** (open substitute for the proprietary Fluid Intelligence): download it from
**AI Enhancement → Fluid Local AI → Download & set up**. It fetches an ARM64 `llama.cpp` build and a
small instruct GGUF (Qwen2.5‑1.5B by default, ~1.1 GB) and runs a local OpenAI‑compatible server —
same enhancement prompts as the cloud path, but fully offline.

---

## License

GPLv3, same as upstream FluidVoice (from 2026‑02‑23 onward). This fork keeps the license and
attribution. The proprietary *Fluid Intelligence* artifacts are **not** bundled; the local
enhancement layer here is an independent open implementation. See [`../LICENSE`](../LICENSE) and
[`docs/MAPPING.md`](docs/MAPPING.md#fluid-intelligence-substitution).
