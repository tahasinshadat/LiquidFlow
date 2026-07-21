# LiquidFlow Reformatter — Gemma 3 270M Fine-Tuning Package

Everything needed to train LiquidFlow's on-device dictation reformatter: a
small model that takes a raw speech-to-text transcript and fixes its
**structure only** — spoken enumerations become bullet/numbered lists, run-ons
get paragraph breaks, stutter duplicates ("the the") and retracted false
starts ("send it monday no wait tuesday") are removed, and literal spoken
commands ("new paragraph", "bullet point") are applied. Wording is never
rewritten, nothing is summarized, nothing is added, and text that is already
fine comes back unchanged.

Base model: [`google/gemma-3-270m-it`](https://huggingface.co/google/gemma-3-270m-it)
(full fine-tune, not LoRA). Final artifact: a Q8_0 GGUF served by the app's
bundled `llama-server`.

## Contents

```
ai-training/
├── README.md                  this file
├── train_gemma3_270m.py       training + GGUF export script (Colab-ready)
└── dataset/
    ├── train.jsonl            1,219 chat-format training pairs
    ├── val.jsonl              91 held-out validation pairs (no overlap)
    ├── build_dataset.py       deterministic generator (seeded), rebuilds both files
    ├── banks_*.py             authored content banks the generator composes from
    └── validate.py            quality gate — run after any dataset change
```

## The fixed instruction (do not change it)

Every training example uses this exact user-message prefix, and **the app must
send the same string at inference time**:

```
Reformat this dictation. Keep the exact wording; only fix structure: lists, paragraphs, remove duplicated words and retracted false starts. If it's already fine, return it unchanged.
```

The user message is that instruction, a blank line, then the raw transcript.
The assistant message is the reformatted text. A one-sentence system prompt is
unnecessary — the instruction is baked into every example.

## Dataset

1,310 examples total (1,219 train / 91 val), generated deterministically from
hand-authored content banks so that every output is correct by construction:
output wording is a subset of input wording (minus duplicates, retractions,
and spoken command words), with only list markers and newlines added.

Coverage: shopping/todo/packing lists spoken linearly; ordered step
instructions ("first... then... after that"); meeting-note dumps with
decision/action-item lists; dictated emails (with and without spoken "new
paragraph" commands); "new line" and "bullet point" command pieces;
code-adjacent prompts where paths, flags, and backticked terms survive
verbatim; run-on multi-paragraph rambles (including 300–500 word pieces);
heavy stutter/duplication; false starts and retractions; mixed prose+list;
questions; number/date/email/URL preservation; lowercase transcripts that stay
lowercase. About 15% of training pairs are exact identity (input == output) so
the model learns restraint, and many more are near-identity (a single stutter
fixed, everything else untouched).

`validate.py` enforces, per pair: JSON/chat structure, the fixed instruction,
multiset containment (no novel words in the output beyond list markers),
word-retention thresholds, no adjacent duplicates in outputs, ASCII-only,
single-line raw inputs, and zero exact-or-normalized overlap between train and
val. Run it after any change:

```
cd dataset
python build_dataset.py     # optional: regenerate (seeded, reproducible)
python validate.py          # must print ALL CHECKS PASSED
```

## Training on Colab (free T4) — step by step

1. Open <https://colab.research.google.com> and create a new notebook.
2. Runtime → Change runtime type → **T4 GPU** → Save.
3. Get this folder into the runtime, either way works:
   - **Upload:** in the Files pane, upload `train_gemma3_270m.py`, then create
     a `dataset` folder and upload `train.jsonl` + `val.jsonl` into it
     (only those three files are needed to train), or
   - **Git:** `!git clone <your repo url>` and `%cd <repo>/windows/ai-training`.
4. Install dependencies (Unsloth optional but faster):

   ```
   !pip install -U "trl>=0.13" transformers datasets accelerate
   !pip install -U unsloth
   ```

5. Run training + GGUF export in one shot:

   ```
   !python train_gemma3_270m.py --gguf
   ```

   Notes:
   - `google/gemma-3-270m-it` is license-gated on Hugging Face. Either accept
     the license on the model page and `huggingface-cli login`, or do nothing:
     the script automatically falls back to the ungated identical-weights
     mirror `unsloth/gemma-3-270m-it`.
   - The script auto-selects bf16 (Ampere+) or fp16-with-fp32-master (T4),
     evaluates on `val.jsonl` each epoch, prints a final eval loss, runs a
     3-sample generation smoke test, and saves to `./gemma3-270m-liquidflow`.
6. Download `gemma3-270m-liquidflow-q8_0.gguf` from the Files pane
   (~300 MB). Done.

**Expected time: well under an hour.** On a free T4 the fine-tune itself is
roughly 10–20 minutes (~230 optimizer steps at effective batch 16), plus a few
minutes for installs, model download, and GGUF conversion.

If you skipped `--gguf`, the exact export commands are:

```
git clone --depth 1 https://github.com/ggml-org/llama.cpp
pip install -r llama.cpp/requirements/requirements-convert_hf_to_gguf.txt
python llama.cpp/convert_hf_to_gguf.py ./gemma3-270m-liquidflow --outfile gemma3-270m-liquidflow-q8_0.gguf --outtype q8_0
```

## LiquidFlow integration

Drop the file at:

```
%LOCALAPPDATA%\LiquidFlow\LocalAI\Models\gemma3-270m-liquidflow-q8_0.gguf
```

The app's llama-server wrapper (`Ai/LocalAiServer.cs`) serves whatever GGUF the
configured model entry points to, so the remaining work is code-side (developer
handles it): add a `LocalAiModel` entry with id `gemma3-270m-liquidflow-q8_0`
and filename `gemma3-270m-liquidflow-q8_0.gguf` to the local model catalog
(`LocalAiServer.Models`) so it can be selected via `Settings.LocalAiModelId`.

At inference, send the fixed instruction + blank line + transcript as the user
message via the OpenAI-compatible chat endpoint. Recommended sampling for this
task: `temperature` 0.1–0.2, `top_p` 0.9, and **`repeat_penalty` 1.0** — a
repeat penalty above 1.0 actively fights a model whose job is to echo the
input verbatim.

## Why these hyperparameters

Three epochs at lr 2e-5 with cosine decay and ~5% warmup is the conservative
sweet spot for full fine-tuning a small instruction-tuned model on a ~1.2k
example task: one epoch underfits the rarer patterns (retractions, spoken
commands), while going much past three at higher learning rates starts to
erode the base model's general robustness and encourages over-eager editing —
the exact failure mode the identity examples exist to prevent. Cosine-to-zero
gives the final epoch small, polishing updates, and weight decay 0.01 with
grad-norm clipping at 1.0 keeps the short run stable in fp16 on a T4.

Effective batch 16 (micro-batch 4 × gradient accumulation 4) is large enough
for stable gradients on heterogeneous example lengths without exceeding T4
memory at `max_len` 2048 — the length needed so the 300–500-word dictations fit
untruncated with both prompt and completion. Loss is masked to the assistant
turn where the installed TRL/Unsloth supports it, so capacity is spent
learning the transformation rather than re-learning to predict transcripts;
the script degrades gracefully to full-sequence loss elsewhere, which is an
acceptable fallback for an echo-heavy task.
