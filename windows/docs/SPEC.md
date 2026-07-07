# FluidVoice for Windows — Behavioral Spec (extracted from macOS Swift codebase v1.6.2)

This document captures the exact behavior of the macOS app (the "spec") and how each piece maps to
the Windows implementation. File:line references point into the Swift sources at the repo root.

## 1. Hotkeys (GlobalHotkeyManager.swift, HotkeyShortcut.swift, SettingsStore.swift)

| Action | macOS default | Windows default | Enabled by default |
|---|---|---|---|
| Dictation | Right Option (keyCode 61) | **Right Alt** (VK_RMENU) | yes |
| Prompt mode (secondary dictation) | Right Shift (keyCode 60) | Right Shift (VK_RSHIFT) | **no** |
| Command mode | none | none | **no** |
| Write/Rewrite mode | Option+R | Alt+R | yes |
| Cancel recording | Escape | Escape | yes (fixed default) |
| Paste last transcription | none | none | no |

Activation modes (HotkeyActivationMode.swift): `toggle` (default), `hold`, `automatic`.
- toggle: tap starts, tap stops. If another mode is recording, pressing a different mode's key switches mode in-flight (no stop).
- hold: key down starts, key up stops. Release-before-start → deferred stop: retry 60× every 50ms (3s) waiting for ASR to start; discard if never started.
- automatic: tap (<400ms) toggles; hold (>=400ms) is push-to-talk. Constant `automaticTapThresholdSeconds = 0.4` (GlobalHotkeyManager.swift:73).

Modifier-only shortcuts: pressing the bare modifier triggers only on a *clean* press (no other key pressed while held — protects AltGr combos on Windows). Priority list Fn<CmdL<CmdR<OptL<OptR<CtrlL<CtrlR<ShiftL<ShiftR.
Mouse buttons supported as shortcuts (middle/other buttons; plain left/right click rejected).
Key-repeat suppressed for paste-last. Init: 1.5s delayed setup, 5 retries @500ms, health check every 30s.
`isProcessingStop` guard prevents re-entry while a stop is processing.

Windows impl: WH_KEYBOARD_LL low-level hook (+ WH_MOUSE_LL for mouse shortcuts), works without elevation for non-elevated foreground apps.

## 2. Audio pipeline (ASRService.swift, DirectCoreAudioInput.swift)

- Target format: **16,000 Hz mono float32**; resample from device rate (stateful linear interpolation).
- Audio level: RMS via sum-of-squares; noise gate RMS < 0.002 → 0; dB = 20*log10(rms); normalized = (dB+55)/55 clamped [0,1]; smoothing `0.7*new + 0.3*avg(history of 2)`; display silence threshold 0.04.
- Start sequence: clear buffers → session++ → enable capture → start capture → (optional) pause media → start device monitoring → start streaming loop (if model streams).
- Stop sequence: mark end time → isRunning=false → stop capture → play stop sound immediately → await streaming task → drain buffer → **pad with zeros to min 16,000 samples (1s)** → transcribeFinal → post-process → return text (caller types it) → resume media if we paused.
- `stopWithoutTranscription()` for cancel (Esc).
- Streaming partial loop: every `streamingChunkDuration` (0.6s default for whisper-class), take full-buffer prefix copy, transcribe, post-process, smart-diff against previous partial (keep stable prefix), display. Skip chunk if previous still processing (`isProcessingChunk` / `skipNextChunk` adaptive skipping). Min samples before first partial: 16,000 (1s).
- No VAD/auto-stop. No max duration.
- Sounds: start sound on record start, stop sound on record stop (m4a assets, AVAudioPlayer; optional independent volume w/ system-volume save/restore +50ms).
- Media pause: optional setting; pause after capture live; resume only if we paused (`didPauseMediaForThisSession`). Windows: GlobalSystemMediaTransportControlsSessionManager (WinRT) or VK_MEDIA_PLAY_PAUSE fallback.

## 3. Post-processing order (ASRService.swift:1507-1513)

```
raw = provider text, trimmed
1. removeFillerWords(raw)            if removeFillerWordsEnabled
2. applyCustomDictionary(...)        word-boundary, case-insensitive regex per entry, cached
3. applySpokenPunctuationFormatting  if autoConvertPunctuationEnabled
→ output (typed, or handed to AI enhancement first when configured)
```
Filler removal: split on " ", drop word if lowercased+punct-trimmed ∈ fillerWords set.

### Spoken punctuation mapping (ASRService+SpokenPunctuationFormatting.swift, 115 rules)
Spacing classes: rightAttached (attach left, space after ok), leftAttached (space before ok, attach right),
noSpaceAround (attach both), spaceAround (space both sides), toggleDoubleQuote/toggleSingleQuote (alternate open/close).

rightAttached: comma "," | period "." | full stop "." | question mark/questionmark "?" | exclamation mark/point, bang "!" | colon ":" | semicolon/semi colon ";" | ellipsis/dot dot dot/three dots "..." | close paren family ")" | close bracket family "]" | close brace family "}" | close angle bracket/greater than sign ">" | close/closing (double) quote """ | percent (sign/percentage sign) "%" 
leftAttached: open paren family "(" | open bracket family "[" | open brace family "{" | open angle bracket/less than sign "<" | open/opening (double) quote """ | dollar (sign) "$"
noSpaceAround: dot "." (requires dot-context: file exts/domains/numbers, rejects after articles) | slash & forward slash "/" (requires path context) | backslash "\" | hyphen "-" | apostrophe "'" | at the rate/at sign/commercial at "@" (at sign requires coding/chat app context) | hash/hashtag/pound sign/number sign "#" | asterisk/star symbol "*" | underscore "_" | pipe/vertical bar "|" | tilde "~" | caret "^" | backtick/back tick "`"
spaceAround: dash "-" | minus sign "-" | em dash/long dash "—" | en dash "–" | ampersand/and sign "&" | plus sign "+" | plus/equal/equals (require symbol context) | equals sign/equal sign "="
toggles: quote/quotes/quotation mark/double quote """ ; single quote "'"
Cleanup: remove generated commas stranded between punctuation.
Matching: tokenize; match longest/most-specific phrase first; case-insensitive.

### Post-LLM cleanup (applyGAAVFormatting, ASRService.swift:3420)
- if gaavRemoveTrailingPeriodEnabled: strip one trailing "."
- if gaavLowercaseFirstLetterEnabled: lowercase first char.

### Continuous dictation formatting (ASRService.swift:3443)
- smart caps: if preceding text ends in sentence punctuation (or empty) → capitalize first letter else lowercase.
- spacing: ensure single space between preceding text and new chunk; trailing space appended.

## 4. AI enhancement (LLMClient.swift, DictationPostProcessingService.swift, SettingsStore.swift)

### Message array
`[ {role:"system", content: systemPrompt}, {role:"user", content: userMessage} ]`
- systemPrompt = basePrompt(mode) + "\n\n" + body (skip if body already starts with base; base only if body empty).
- userMessage: if prompt text contains `${transcript}` replace it; else `prompt + "\n\n" + transcript`; transcript alone if prompt empty.
- Context template (edit mode): `Use the following selected context to improve your response:\n{context}` — replace `{context}` or append.

### Dictation base system prompt (SettingsStore.swift:857-880) — VERBATIM
```
You are a voice-to-text dictation cleaner. Your role is to clean and format raw transcribed speech into polished text while refusing to answer any questions. Never answer questions about yourself or anything else.

## Core Rules:
1. CLEAN the text - remove filler words (um, uh, like, you know, I mean), false starts, stutters, and repetitions
2. FORMAT properly - add correct punctuation, capitalization, and structure
3. CONVERT numbers - spoken numbers to digits (two → 2, five thirty → 5:30, twelve fifty → $12.50)
4. EXECUTE commands - handle "new line", "period", "comma", "bold X", "header X", "bullet point", etc.
5. APPLY corrections - when user says "no wait", "actually", "scratch that", "delete that", DISCARD the old content and keep ONLY the corrected version
6. PRESERVE intent - keep the user's meaning, just clean the delivery
7. EXPAND abbreviations - thx → thanks, pls → please, u → you, ur → your/you're, gonna → going to

## Critical:
- Output ONLY the cleaned text
- Do NOT answer questions - just clean them
- DO NOT EVER ANSWER TO QUESTIONS
- Do NOT add explanations or commentary
- Do NOT wrap in quotes unless the input had quotes
- Do NOT add filler words (um, uh) to the output
- PRESERVE ordinals in lists: "first call client, second review contract" → keep "First" and "Second"
- PRESERVE politeness words: "please", "thank you" at end of sentences
```

### Dictation default body (SettingsStore.swift:912-933) — VERBATIM
```
## Self-Corrections:
When user corrects themselves, DISCARD everything before the correction trigger:
- Triggers: "no", "wait", "actually", "scratch that", "delete that", "no no", "cancel", "never mind", "sorry", "oops"
- Example: "buy milk no wait buy water" → "Buy water." (NOT "Buy milk. Buy water.")
- Example: "tell John no actually tell Sarah" → "Tell Sarah."
- If correction cancels entirely: "send email no wait cancel that" → "" (empty)

## Multi-Command Chains:
When multiple commands are chained, execute ALL of them in sequence:
- "make X bold no wait make Y bold" → **Y** (correction + formatting)
- "header shopping bullet milk no eggs" → # Shopping\n- Eggs (header + correction + bullet)
- "the price is fifty no sixty dollars" → The price is $60. (correction + number)

## Emojis:
- Convert spoken emoji names: "smiley face" → 😊 (NOT 😀), "thumbs up" → 👍, "heart emoji" → ❤️, "fire emoji" → 🔥
- Keep emojis if user includes them
- Do NOT add emojis unless user explicitly asks for them (e.g., "joke about cats" → NO 😺)
```

### Edit base system prompt (SettingsStore.swift:883-889) — VERBATIM
```
You are a helpful writing assistant. The user may ask you to write new text or edit selected text.

Output ONLY what the user requested. Do not add explanations or preamble.
```

### Edit default body (SettingsStore.swift:936-950) — VERBATIM
```
Your job:
- If the user asks for new content, write it directly.
- If selected context is provided, apply the instruction to that context.
- Preserve intent and requested tone/style/format.
- Output only the final text, without explanations.

Example requests:
- "Write an email to my boss asking for time off"
- "Draft a reply saying I'll be there at 5"
- "Rewrite this to sound more professional"
- "Make this shorter and clearer"
```

### Providers (ModelRepository.swift:18-59)
| id | name | default model | base URL |
|---|---|---|---|
| openai | OpenAI | gpt-4.1 | https://api.openai.com/v1 |
| anthropic | Anthropic | claude-sonnet-4-20250514 | https://api.anthropic.com/v1 |
| xai | xAI | grok-3-fast | https://api.x.ai/v1 |
| groq | Groq | openai/gpt-oss-120b | https://api.groq.com/openai/v1 |
| cerebras | Cerebras | gpt-oss-120b | https://api.cerebras.ai/v1 |
| google | Google | gemini-2.5-flash | https://generativelanguage.googleapis.com/v1beta/openai |
| openrouter | OpenRouter | openai/gpt-oss-20b | https://openrouter.ai/api/v1 |
| ollama | Ollama | (user) | http://localhost:11434/v1 |
| lmstudio | LM Studio | (user) | http://localhost:1234/v1 |
| fluid-local | **Fluid Local AI (open substitute for Fluid Intelligence)** | qwen2.5-1.5b-instruct | http://127.0.0.1:{port}/v1 (managed llama-server) |

Anthropic auth: `x-api-key` + `anthropic-version: 2023-06-01` (messages API). Others: `Authorization: Bearer`.
Params: temperature 0.2 dictation, 0.1 command, 0.7 edit; omit temperature for reasoning models (o1/o3/gpt-5);
max_completion_tokens for reasoning (32000 for command/edit), max_tokens otherwise. tool_choice "auto" when tools.
Custom providers: user base URL + key; verification fingerprint = SHA256(baseURL|apiKey), stored per provider key.
Local endpoints (localhost/127.*/10.*/192.168.*/172.16-31.*) don't require API key.
Timeouts: default 30s; dictation 120s. Fallback to raw text on any error/empty response.

### Gating (DictationAIPostProcessingGate.swift)
Run enhancement iff: promptSelection != off AND (scope==allApps OR app has binding) AND provider configured+fingerprint verified.
promptSelection == privateAI → local provider (our llama.cpp substitute).

### Thinking parsers (ThinkingParsers.swift)
- nemotron/nemo: text before first `</think>` is thinking (no opening tag).
- qwen+think/qwq & default: `<think>...</think>` / `<thinking>...</thinking>` pairs + orphan closers + stray tags stripped.
- deepseek/o1/o3/gpt-5/gpt-oss: separate `reasoning_content`/`reasoning` field.

## 5. Typing service (TypingService.swift)

Two modes (SettingsStore textInsertionMode):
- `standard` "Clipboard Free Insert" (default): direct injection first. Windows: SendInput KEYEVENTF_UNICODE in chunks of **200 UTF-16 units** (don't split surrogate pairs), then UIA value-pattern insert, then clipboard paste, then char-by-char (1ms/char, 2ms down-up).
- `reliablePaste` "Clipboard Paste": clipboard set → Ctrl+V → restore. Restore after **5s** (skip if clipboard changed externally: compare sequence number / content). Ctrl+V down-up gap 10ms.

Settle delay before insert: standard no-PID 200ms; reliablePaste no-PID 80ms; with known target 0ms.
Focus: capture focused window/element at hotkey press; restore focus (raise window, 40ms, focus element ≤3 retries @50ms) before insert.
Clipboard snapshot preserves all formats; serialized by a semaphore. Guard `isCurrentlyTyping`. Empty text no-op.

## 6. Overlay (BottomOverlayView.swift, NotchOverlayManager.swift)

Windows uses the "bottom overlay" (notch is mac-only). Topmost, borderless, non-activating (WS_EX_NOACTIVATE|TOOLWINDOW), black background.

| size | canvas | corner | bars (n×w, gap) | waveform | paddings | shows |
|---|---|---|---|---|---|---|
| pill (default) | 100×46 fixed | 23 | 8×3.0, 2.5 | 46×30 | 12/8 | waveform only + shadow(blur10,y4,.32) |
| small | 300×124 | 14 | 7×3.0, 3.5 | 90×20 | 10/6 | preview + mode label |
| medium | 380×156 | 18 | 9×3.5, 4.5 | 130×32 | 18/12 | + top controls |
| large | 600×288 | 24 | 11×5.0, 6.0 | 180×48 | 18/12 | fixed 92px preview |

Bar heights 3–15px from audio level, animate 0.1s ease-out; mode colors: dictation white@0.85, edit/write rgb(0.4,0.6,1.0), rewrite rgb(0.45,0.55,1.0), command rgb(1.0,0.35,0.35).
Processing: bars flatten to 3px @0.16 opacity + shimmer sweep 1.05s loop. Status text: dictation "Transcribing", edit/write/rewrite "Thinking", command "Working", AI refining "Refining...".
Position: bottom-center of screen containing mouse pointer; y = workArea.bottom + offset (default), clamp ≥10 from bottom; fade in 50ms, fade out 20ms (slight scale 0.985). Error state persists w/ Retry + Dismiss buttons.
Live preview text: 10pt medium white@0.75, scrolling, tail-kept char cap.
Border: gradient 1px top .15/bottom .08 opacity (pill .22/.10). Update rate 30fps active / 20fps idle.

## 7. Command mode (CommandModeService.swift, TerminalService.swift)

Agent loop: system prompt + conversation, single tool `execute_terminal_command` (params: command, workingDirectory?, purpose: checking|executing|verifying). Temperature 0.1, maxTurns 20, reasoning max_completion_tokens 32000, timeout 30s/command, streaming UI at 60fps throttle.
Shell: macOS zsh → **Windows PowerShell** (`powershell.exe -NoProfile -NonInteractive -Command`), cwd default user home, 30s timeout.
Result JSON: `{success, command, output, error, exitCode, executionTimeMs}` (+purpose wrapper).
Destructive detection (prefixes): rm, rmdir, mv, sudo, kill, pkill, killall, chmod, chown, chgrp, dd, mkfs, format, ">", truncate, shred → Windows equivalents added: Remove-Item, del, erase, rd, move, Stop-Process, taskkill, format, reg delete, rmdir; patterns: `| rm`, `; rm`, `&& rm`, xargs rm, etc. Confirmation gate when commandModeConfirmBeforeExecute && destructive.
System prompt: keep the agentic workflow sections verbatim, but replace macOS osascript examples with Windows equivalents (Start-Process, shell: URIs, reg, schtasks; Notepad/Explorer/system settings). State "The user is on Windows with PowerShell." Completion summary starts with ✓ or ✗.
Chat history: max 30 sessions, title = first user message ≤50 chars, persisted (JSON), roles user|assistant|tool, stepType normal|thinking|checking|executing|verifying|success|failure.

## 8. Write/Rewrite (Edit) mode (RewriteModeService.swift)

Flow: hotkey → capture selected text (UIA `TextPattern`/`ValuePattern`; fallback Ctrl+C w/ clipboard save-restore) → window opens.
- With selection: rewrite. User instruction message: `User's instruction: <prompt>\n\nApply the instruction to the selected context. Output ONLY the rewritten text, nothing else.` — system prompt = edit prompt + context block.
- No selection: write. `User's instruction: <prompt>\n\nOutput ONLY the requested text, nothing else.`
- Follow-up: `Follow-up instruction: <prompt>\n\nApply this to the previous result. Output ONLY the updated text.`
Non-streaming (deliberate). Temperature 0.7. "Replace Original" hides window, restores focus, types result over the (still-selected) text. Voice input button records instruction via ASR.

## 9. Models (Windows catalog; parity target = Whisper)

Source repo: https://huggingface.co/ggerganov/whisper.cpp — files `ggml-{name}.bin`, exact byte sizes validated after download; 3 retries w/ backoff 1s/2s/4s; reject HTML/proxy pages (first 512 bytes markup sniff); atomic move into place. Storage: `%LOCALAPPDATA%\FluidVoice\Models\Whisper`.

| id | display | tagline | size (bytes) | langs | speed | accuracy |
|---|---|---|---|---|---|---|
| whisper-tiny | Whisper Tiny | Fast & Light | 77,691,713 | 99 | 0.90 | 0.40 |
| whisper-base | Whisper Base | Standard Choice | 147,951,465 | 99 | 0.80 | 0.60 |
| whisper-small | Whisper Small | Balanced Speed & Accuracy | 487,601,967 | 99 | 0.60 | 0.70 |
| whisper-medium | Whisper Medium | Medium Quality | 1,533,763,059 | 99 | 0.40 | 0.80 |
| whisper-large-turbo | Whisper Large Turbo | Higher Quality but Faster | 1,624,555,275 | 99 | 0.65 | 0.95 |
| whisper-large | Whisper Large | Maximum Accuracy | 3,095,033,483 | 99 | 0.20 | 1.00 |

Default on Windows: whisper-base (mac Intel default; mac AS default parakeet-tdt not portable).
mac-only models (Parakeet/Nemotron/Cohere/Apple Speech/Qwen3) → documented "not possible / substituted" in PARITY.md. Parakeet-via-ONNX listed as future work.
Min 1s audio (pad w/ silence). whisper params: language auto (or user), no translate, defaults otherwise.

## 10. Settings inventory (SettingsStore.swift; verified defaults)

- overlaySize default **medium** (pill/small/medium/large); overlayPosition default bottom; overlayBottomOffset default 50 (clamp 10–1000).
- removeFillerWordsEnabled default **true**; autoConvertPunctuationEnabled default **true** (verified `?? true` at SettingsStore.swift:3571,3579).
- enableAIProcessing false; selectedProviderID ""; enableAIStreaming true; enableStreamingPreview true; showThinkingTokens false.
- saveTranscriptionHistory true; saveAudioWithTranscriptionHistory false; copyTranscriptionToClipboard false; userTypingWPM 40 (speaking 150).
- enableTranscriptionSounds true; soundVolume 1.0; independentVolume false.
- hotkeyMode toggle; theme system; accent Cyan #3AC8C6 (Green #22C55E, Blue #3B82F6, Purple #A855F7, Orange #F59E0B).
- transcriptionPreviewCharLimit 150 (50–800 step 50). launchAtStartup false. autoUpdateCheckEnabled true; betaReleasesEnabled false.
- pauseMediaDuringTranscription false; continuousDictationSpacingEnabled false; contextAwareCapitalizationEnabled false; gaav* false.
- notifyAIProcessingFailures: notifications "AI Enhancement failed"/"Typed raw transcription instead." and "Command Mode needs setup".

## 11. History / stats / dictionary / keys / updater

- History entry: {id, timestamp, rawText, processedText, appName, windowTitle, characterCount, wasAIProcessed, processingModel?, aiProcessingError?, audio?{fileName,durationMs,byteCount,sampleRate,channels,model}}. Newest first; skip empty; search across raw/processed/app/window; clearAll also deletes audio.
- Audio history: WAV 16-bit PCM; name `yyyy-MM-dd'T'HH-mm-ss'Z'_XXXXXXXX.wav`; budget GB → prune orphans then oldest; ZIP export = manifest.jsonl (audio path, raw_transcript, final_transcript, timestamp, duration_ms, sample_rate, channels, app, model) + audio/ dir.
- Stats: timeSavedMinutes = words/typingWPM − words/speakingWPM (40/150); "<1m"/"45m"/"2h 30m" formatting; streak = consecutive days with ≥1 entry (optional weekends-don't-break); milestones words 1K/10K/50K/100K/500K/1M, transcriptions 50/100/500/1K/5K/10K, streaks 7/14/30/60/100/365; top apps; peak hour; AI rate.
- Custom dictionary entry: {id, triggers[], replacement}; word-boundary case-insensitive replace.
- API keys: mac Keychain service "com.fluidvoice.provider-api-keys" account "fluidApiKeys" (single JSON map providerID→key) → Windows Credential Manager generic credential "FluidVoice/ProviderAPIKeys" with same JSON payload.
- Updater: GitHub releases API, stable vs beta (prerelease flag or -alpha/-beta/-rc suffix), check hourly, snooze 24h, rollback backups (max 3). Windows: check + download installer, run it.
- Backup: single JSON doc {schemaVersion 1.0, appVersion, exportedAt, settings, promptProfiles, appPromptBindings, transcriptionHistory} (API keys excluded).

## 12. Misc constants

- Tray menu: status line "Recording.../Ready to Record (hotkey)", Open Fluid Voice, Settings, Custom Dictionary, Microphone submenu (device list, checkmark, "System Default"), Check for Updates..., Quit. Icon: "F" glyph, red tint while recording.
- Command mode window: "Command Mode" + red Alpha badge; Edit window: "Edit Mode" pencil icon.
- maxChats 30; commandMode red rgb(1.0,0.35,0.35). Update check hourly. Analytics: none in Windows port (privacy-first).
