# Feature parity: macOS FluidVoice → Windows port

Status legend: **✅ Matched** (works like the mac app) · **🔄 Substituted** (open/native
replacement with equivalent behavior) · **⛔ Not possible on Windows ARM** (with reason).

## Core dictation

| Feature | Status | Notes |
| --- | --- | --- |
| Global‑hotkey voice capture from anywhere | ✅ Matched | `WH_KEYBOARD_LL` hook; near‑instant. Right Alt default. |
| Toggle / Hold / Automatic activation | ✅ Matched | 400 ms tap‑vs‑hold threshold; bare‑modifier + deferred‑stop logic ported. |
| Mouse‑button shortcuts | ✅ Matched | Middle / X1 / X2, with modifiers. |
| Cancel recording (Esc) | ✅ Matched | Stops without transcribing. |
| Paste last transcription | ✅ Matched | Opt‑in shortcut. |
| Smart Typing into any focused app | ✅ Matched | **Default is clipboard paste** (Ctrl+V, clipboard snapshot/restored after 5 s): reliable and instant into every app tested. "Clipboard‑free" SendInput‑unicode is offered as an option but modern Windows apps (Win11 Notepad, WinUI/RichEdit) drop bulk unicode bursts, so it isn't the default. mac defaults to clipboard‑free because CGEvent unicode is reliable on macOS — same *reliability* goal, different mechanism. |
| Live transcription overlay | ✅ Matched | Pill / Small / Medium / Large; mode‑colored waveform; live text. Notch variants **dropped** (mac‑only hardware). |
| Start/stop sounds | 🔄 Substituted | Synthesized chirps (mac ships proprietary m4a chimes; not bundled). |
| Media pause/resume while dictating | ✅ Matched | WinRT system media session. |

## Speech models

| Feature | Status | Notes |
| --- | --- | --- |
| Whisper Tiny/Base/Small/Medium/Large(+Turbo) | ✅ Matched | whisper.cpp ARM64‑native; 99 languages; on‑demand download w/ byte‑exact validation, 3× retry, HTML‑page rejection. Parity baseline. |
| Model picker w/ size/speed/accuracy + language | ✅ Matched | Speech Models tab. |
| Parakeet TDT v2/v3, Parakeet Flash | 🔄 Substituted | mac ships Apple‑Silicon CoreML; the port runs the k2‑fsa **ONNX export of `nvidia/parakeet-tdt-0.6b-v2` (int8) on sherpa‑onnx** (win‑arm64‑native NuGet runtime). English, punctuation+casing, near‑instant finals. v3/Flash have no maintained ONNX export yet. Encoder runs on CPU; ONNX Runtime QNN (Hexagon NPU offload) is possible future work — sherpa's bundled ORT is CPU‑only. |
| Nemotron Speech 3.5 (streaming/offline) | ⛔ Not possible | Apple‑Silicon CoreML only. |
| Cohere Transcribe | ⛔ Not possible | Apple‑Silicon CoreML only. |
| Apple Speech / Apple Speech Analyzer | ⛔ Not possible | Apple OS speech APIs; no Windows equivalent shipped. Windows has its own on‑device SR, but it is not wired in — Whisper covers the languages. |
| Live partial transcription | 🔄 Substituted | **Parakeet: true streaming** — a companion streaming transducer (k2 zipformer int8, bundled in the Parakeet download) runs under sherpa‑onnx's online recognizer; new mic samples are fed incrementally so preview latency is constant (~0.2 s) regardless of recording length, and the Parakeet final decode replaces the preview at stop (parakeet‑tdt itself is offline‑only in sherpa‑onnx — true streaming TDT is an open upstream request). Whisper: batch — live partials are simulated by periodically re‑decoding the buffer tail. |

## AI enhancement

| Feature | Status | Notes |
| --- | --- | --- |
| Cloud providers (OpenAI, Groq, Anthropic, xAI, Cerebras, Google, OpenRouter, custom) | ✅ Matched | Same base URLs/default models; OpenAI‑compatible + Anthropic messages API; keys in Credential Manager. |
| Ollama / LM Studio local endpoints | ✅ Matched | Localhost, no key. |
| **Fluid Intelligence** (proprietary local runtime) | 🔄 Substituted | **Fluid Local AI**: managed llama.cpp (ARM64) + open instruct GGUF, same prompts/gating/cleanup. Proprietary artifacts not ported (closed source, not in repo). |
| Verbatim dictation + edit system prompts | ✅ Matched | Copied exactly from `SettingsStore.swift`. |
| Per‑app prompt routing | ✅ Matched | By process name; all‑apps vs selected‑apps scope. |
| Streaming + thinking‑tag parsing | ✅ Matched | SSE streaming; nemotron/qwen/separate‑field reasoning handled. |
| Gating + fallback to raw on error | ✅ Matched | Same configured/verified‑fingerprint gate; 120 s dictation timeout; raw fallback + notification. |

## Modes

| Feature | Status | Notes |
| --- | --- | --- |
| Write/Edit mode (rewrite selection or write new) | ✅ Matched | UIA/clipboard selection capture; verbatim templates; temp 0.7, non‑streaming; replace by typing. |
| Command mode (control the OS by voice) | ✅ Matched | Agentic loop, `execute_terminal_command` tool, **PowerShell** shell (mac uses zsh+osascript), destructive‑command confirmation, chat history. |
| Command‑mode chat UI (multi‑turn, recent chats) | ✅ Matched | Max 30 chats, titles, persisted. |

## Data, history, config

| Feature | Status | Notes |
| --- | --- | --- |
| Transcription history (search/copy/paste/delete) | ✅ Matched | JSON store, same record schema. |
| Audio history (opt‑in, budget, ZIP export) | ✅ Matched | WAV + `manifest.jsonl` export; GB‑budget pruning. |
| Today‑usage stats (words, time saved, sessions, streak) | ✅ Matched | Same `words/typingWPM − words/speakingWPM` formula, streaks, top apps, AI rate. |
| Custom dictionary | ✅ Matched | Trigger→replacement, word‑boundary case‑insensitive. |
| Per‑app configuration | ✅ Matched | App→prompt bindings by process name. |
| Settings backup/restore | 🔄 Partial | Settings/history are plain JSON files you can copy; a one‑click backup document isn't wired to UI yet. |

## System integration

| Feature | Status | Notes |
| --- | --- | --- |
| System‑tray icon + menu | ✅ Matched | Status line, Open, Settings, Custom Dictionary, Microphone submenu, Check for Updates, Quit. |
| Adaptive light/dark theming | ✅ Matched | Follows system; manual override; accent color. |
| Auto‑update + beta channel | ✅ Matched | GitHub releases API; installer download. |
| Launch at startup | ✅ Matched | Run registry key. |
| Installer (ARM64 primary, x64) | ✅ Matched | Inno Setup `.exe` + portable ZIP. |
| Local‑first / everything opt‑in | ✅ Matched | No analytics in the port (mac has opt‑in PostHog; dropped for privacy). Nothing leaves the machine unless a cloud provider is enabled. |
| Onboarding flow | 🔄 Simplified | First run opens the dashboard; model download + AI setup are one click in their tabs (mac has a dedicated multi‑step onboarding). |

## Summary

The **entire core loop and behavior** — hotkey, capture, on‑device STT, the full formatting
pipeline, smart typing, overlay, AI enhancement (cloud + local substitute), Write and Command modes,
history, stats, per‑app config, tray, theming, updater, installer — is **matched or substituted with
a working open/native equivalent**. That now includes **Parakeet**, the mac app's flagship engine,
substituted with the ONNX export on sherpa‑onnx including true streaming live partials. The only
**⛔ not‑possible** items left are the remaining Apple‑Silicon‑only CoreML models (Nemotron / Cohere /
Apple Speech) and the closed Fluid Intelligence runtime; Whisper is the documented parity baseline
for multilingual STT and Fluid Local AI is the open substitute for enhancement.
