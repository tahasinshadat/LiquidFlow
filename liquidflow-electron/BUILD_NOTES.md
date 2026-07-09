# LiquidFlow (Electron) — build & run

This is the Electron edition (fork of OpenWhispr, MIT — see `FORK_NOTICE.md`).

## Requirements
- **Node.js ≥ 20** (`package.json` engines relaxed from upstream's `>=24`; `.npmrc` sets
  `engine-strict=false`, `.nvmrc` pins `20`). This was the actual install blocker on the
  dev machine (upstream required Node ≥ 24).
- Build tools for native modules (`better-sqlite3`, `onnxruntime-node`): prebuilt binaries
  are fetched during install; Visual Studio Build Tools (C++) only needed if they don't.
- First run downloads several GB of engines/models (whisper.cpp, sherpa/Parakeet,
  llama-server, Qdrant, MiniLM embeddings, VAD) via the `predev`/`prebuild` scripts.

## Speech-to-text engines & architecture (Windows-on-ARM)

The app shell is kept **x64** (accepting Prism emulation on Snapdragon X Elite), but the
STT engines run as **separate child processes**, so they can be native ARM64 regardless of
the shell's arch — a native arm64 `.exe` launches and runs natively even under an x64
parent. The engine binary is chosen by *true machine arch* (`getMachineArch()` in
`src/utils/serverUtils.js`, which reads `PROCESSOR_ARCHITEW6432` to see through x64
emulation), not by `process.arch`.

| Engine | Windows-on-ARM | Why |
|--------|----------------|-----|
| **Parakeet** (sherpa-onnx, default) | **native ARM64** | k2-fsa ships `sherpa-onnx-v1.12.23-win-arm64-shared-MT-Release.tar.bz2` (static-CRT, self-contained). Fast — same engine the native C# app uses. |
| Whisper.cpp (fallback) | x64 (emulated) | The OpenWhispr whisper.cpp fork publishes no Windows ARM64 build; the x64 binary runs under emulation. |

- **No GPU/NPU acceleration** — CPU inference only (Adreno/Hexagon EPs deliberately skipped
  per project scope). This matches the native app's proven-good CPU path.
- `resolveArchBinaryPath()` tries `arm64` then falls back to `x64` on ARM hardware, so
  whisper still works and Parakeet gets native speed.
- Default engine is **local Parakeet** (`localTranscriptionProvider: "nvidia"`,
  `parakeetModel: "parakeet-tdt-0.6b-v3"`, `useLocalWhisper: true`) — LiquidFlow is
  local-first with no cloud accounts, matching the native app.

Downloaders: `scripts/download-sherpa-onnx.js` has a `win32-arm64` entry and always ships
the arm64 build on Windows targets; `scripts/download-whisper-cpp.js` pins Windows to x64.

### GPU/NPU acceleration — investigated, verdict: CPU is the fast path (2026-07)

Exhaustively researched (6 parallel deep-dives + 2 adversarial refutations) **and** benchmarked
on-device. Conclusion: **no GPU or NPU path is both faster and achievable on this machine** for
Parakeet; the only real win is CPU thread tuning.

- **CPU thread sweep (shipped):** measured optimum is **3 threads** (`parakeetWsServer.js`),
  down from 4. Parakeet's INT8 transducer is memory-bandwidth bound on the X Elite's clustered
  cores — it *peaks at 3 and regresses hard past 4* (RTF: 3→0.071, 4→0.079, 6→0.163, 8→0.237;
  a 4s clip went 393ms→299ms at 3 threads). This is the OPPOSITE of "more threads = faster";
  the research had theorized 6–8, the benchmark disproved it. Also lowers power.
- **`--model-type=transducer`** (a suggested cold-start optimization): **do NOT add** — it
  crashes Parakeet TDT (`'vocab_size' does not exist in the metadata`); TDT needs auto-detection.
- **NPU (Hexagon/QNN) for Parakeet:** sherpa-onnx v1.13.4 (2026-07) *did* add mainline QNN for
  Parakeet TDT — but prebuilt QNN context binaries target only phone/auto SoCs, **none for the
  X Elite (HTP v73)**; no QnnHtp.dll/FastRPC user libs or QNN SDK on this box; sherpa's QNN
  loader is POSIX `dlopen`-only (won't load on Windows); no win-arm64 QNN sherpa build ships.
  Real path exists in the abstract, but it's a research project here with unverified payoff. Skip.
- **GPU (Adreno/DirectML) for Parakeet:** the binary literally says "DirectML is for Windows
  only. Fallback to cpu!" — sherpa has no arm64-directml build (x64-only), DirectML strips
  INT8→FP32/FP16 (loses the INT8 win), integrated Adreno is ~50× slower for small ASR models,
  and the Adreno Windows driver crashes DirectML/Vulkan compute. Much slower. Skip.
- **whisper.cpp on Adreno (Vulkan):** the current Adreno driver (31.0.133.1) crashes Vulkan
  compute (`createComputePipeline: ErrorUnknown`); no arm64 vulkan build; CPU beats the iGPU
  here anyway. Skip.
- **The one NPU path that actually runs today:** Windows AI Speech Recognition API (Copilot+
  PCs auto-schedule it on the Hexagon NPU). But it's a Whisper-tiny/base-class English-only
  model — **likely less accurate than Parakeet 0.6B** — and needs the app repackaged as MSIX
  with a WinRT C# sidecar. Offer only as an opt-in "streaming/low-latency" mode if the accuracy
  drop is acceptable; not a drop-in speedup.

### Verified on the dev machine (Snapdragon X Elite, Windows 11 ARM)
- The arm64 sherpa binary is genuinely ARM64 (PE machine `0xAA64`) and runs natively.
- The runtime resolver selects `sherpa-onnx-ws-win32-arm64.exe`.
- **End-to-end inference:** the arm64 websocket server loaded the 652 MB Parakeet model and
  returned well-formed JSON over the fork's WS protocol in a ~156 ms round-trip — real
  onnxruntime-ARM64 inference, no crash. (The native C# app already proves real-speech
  accuracy with the same stack.)

## Run (dev)
```
cd liquidflow-electron
npm install            # Node ≥ 20; installs deps + rebuilds native modules
npm run dev            # compiles native helpers, downloads sidecars, launches Electron
```
Direct launch without the `predev` sidecar downloads: `node scripts/run-electron.js`.

## Build a Windows installer
```
npm run build:win      # electron-builder → dist/ (NSIS + portable)
```

## Auth / accounts
Stripped to local-only: `authClient` is `null` (`src/lib/auth.ts`), onboarding skips the
welcome/sign-in step, and `LOCAL_ONLY` is treated as auth-skipped. Cloud LLMs, if used, are
BYOK-direct (your own API key) — nothing phones home.
