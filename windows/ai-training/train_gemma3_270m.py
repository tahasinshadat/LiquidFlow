#!/usr/bin/env python
# train_gemma3_270m.py
#
# Full fine-tune of google/gemma-3-270m-it as the LiquidFlow dictation
# reformatter, followed by (optional) GGUF Q8_0 export for llama-server.
#
# Designed for a free Colab T4 (or any CUDA box). Uses Unsloth when it is
# installed (faster, less memory), and falls back to plain Hugging Face
# TRL SFTTrainer otherwise. Either way this is a FULL fine-tune, not LoRA:
# at 270M parameters full tuning fits easily in 16 GB and gives the best
# quality for a narrow structural-editing task.
#
# Quick start (Colab, T4 GPU runtime):
#   pip install -U "trl>=0.13" transformers datasets accelerate
#   pip install -U unsloth          # optional but recommended
#   python train_gemma3_270m.py --gguf
#
# Outputs:
#   ./gemma3-270m-liquidflow/                  (HF checkpoint + tokenizer)
#   ./gemma3-270m-liquidflow-q8_0.gguf         (with --gguf)

import argparse
import dataclasses
import json
import os
import subprocess
import sys

# Unsloth must be imported before transformers/trl to apply its patches.
USE_UNSLOTH = False
if os.environ.get("LIQUIDFLOW_NO_UNSLOTH", "") != "1" and "--no-unsloth" not in sys.argv:
    try:
        from unsloth import FastModel  # noqa: F401
        USE_UNSLOTH = True
        print("[setup] Unsloth found: using FastModel fast path.")
    except Exception as e:  # ImportError, NotImplementedError on unsupported GPUs, ...
        print("[setup] Unsloth not usable (%s); falling back to plain HF + TRL." % type(e).__name__)

import torch
from datasets import load_dataset
from transformers import AutoModelForCausalLM, AutoTokenizer, set_seed
from trl import SFTConfig, SFTTrainer

DEFAULT_MODEL = "google/gemma-3-270m-it"
# Public mirror with identical weights, used automatically if the Google repo
# is gated for this HF account (no license click-through needed).
FALLBACK_MODEL = "unsloth/gemma-3-270m-it"

def parse_args():
    ap = argparse.ArgumentParser(description="Fine-tune Gemma 3 270M as the LiquidFlow reformatter")
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--train", default="dataset/train.jsonl")
    ap.add_argument("--val", default="dataset/val.jsonl")
    ap.add_argument("--out", default="./gemma3-270m-liquidflow")
    ap.add_argument("--epochs", type=float, default=3.0)
    ap.add_argument("--lr", type=float, default=2e-5)
    ap.add_argument("--batch-size", type=int, default=4,
                    help="per-device micro batch; effective batch = batch * grad-accum")
    ap.add_argument("--grad-accum", type=int, default=4)
    ap.add_argument("--max-len", type=int, default=2048)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--no-unsloth", action="store_true",
                    help="force the plain HF + TRL path even if unsloth is installed")
    ap.add_argument("--gguf", action="store_true",
                    help="after training, clone llama.cpp and export a Q8_0 GGUF")
    return ap.parse_args()

# ---------------------------------------------------------------------------
# Model + tokenizer loading
# ---------------------------------------------------------------------------

def load_model_and_tokenizer(args):
    candidates = [args.model]
    if args.model == DEFAULT_MODEL:
        candidates.append(FALLBACK_MODEL)
    last_err = None
    for name in candidates:
        try:
            if USE_UNSLOTH:
                from unsloth import FastModel
                model, tokenizer = FastModel.from_pretrained(
                    model_name=name,
                    max_seq_length=args.max_len,
                    full_finetuning=True,   # full fine-tune, not LoRA
                    load_in_4bit=False,
                    load_in_8bit=False,
                )
            else:
                bf16_ok = torch.cuda.is_available() and torch.cuda.is_bf16_supported()
                # On pre-Ampere GPUs (e.g. Colab T4) train with fp32 master
                # weights + fp16 autocast; on Ampere+ load bf16 directly.
                dtype = torch.bfloat16 if bf16_ok else torch.float32
                tokenizer = AutoTokenizer.from_pretrained(name)
                model = AutoModelForCausalLM.from_pretrained(
                    name,
                    torch_dtype=dtype,
                    attn_implementation="eager",  # recommended for Gemma training
                )
            print("[setup] loaded %s" % name)
            return model, tokenizer
        except Exception as e:
            last_err = e
            msg = str(e).lower()
            if any(k in msg for k in ("gated", "401", "403", "authoriz", "access")):
                print("[setup] %s appears gated for this account; trying mirror..." % name)
                continue
            raise
    raise last_err

# ---------------------------------------------------------------------------
# Dataset -> chat-templated text
# ---------------------------------------------------------------------------

def build_text_dataset(path, tokenizer):
    ds = load_dataset("json", data_files=path, split="train")
    bos = tokenizer.bos_token or ""

    def to_text(ex):
        text = tokenizer.apply_chat_template(
            ex["messages"], tokenize=False, add_generation_prompt=False)
        # Gemma's chat template already prepends <bos>; the trainer's own
        # tokenization adds it again. Strip it here so exactly one survives.
        if bos and text.startswith(bos):
            text = text[len(bos):]
        return {"text": text}

    return ds.map(to_text, remove_columns=[c for c in ds.column_names if c != "text"])

# ---------------------------------------------------------------------------
# Version-tolerant SFTConfig construction (TRL renamed several fields over
# time: max_seq_length -> max_length, evaluation_strategy -> eval_strategy).
# ---------------------------------------------------------------------------

def make_sft_config(args, bf16_ok):
    fields = {f.name for f in dataclasses.fields(SFTConfig)}
    cfg = dict(
        output_dir=args.out + "-checkpoints",
        num_train_epochs=args.epochs,
        learning_rate=args.lr,
        lr_scheduler_type="cosine",
        warmup_ratio=0.05,
        weight_decay=0.01,
        max_grad_norm=1.0,
        per_device_train_batch_size=args.batch_size,
        per_device_eval_batch_size=args.batch_size,
        gradient_accumulation_steps=args.grad_accum,
        logging_steps=10,
        save_strategy="no",           # we save once at the end
        bf16=bf16_ok,
        fp16=(torch.cuda.is_available() and not bf16_ok),
        seed=args.seed,
        report_to="none",
        optim="adamw_torch",
        dataset_text_field="text",
        packing=False,
    )
    # renamed / optional fields
    for cand in ("max_seq_length", "max_length"):
        if cand in fields:
            cfg[cand] = args.max_len
            break
    for cand in ("eval_strategy", "evaluation_strategy"):
        if cand in fields:
            cfg[cand] = "epoch"
            break
    if "dataset_num_proc" in fields:
        cfg["dataset_num_proc"] = 2
    cfg = {k: v for k, v in cfg.items() if k in fields}
    return SFTConfig(**cfg)

def make_trainer(model, tokenizer, cfg, train_ds, val_ds):
    # Try to mask the prompt so loss is computed on the model turn only.
    # If this collator is unavailable in the installed TRL, fall back to
    # standard full-sequence LM loss (fine for this echo-heavy task).
    collator = None
    if not USE_UNSLOTH:
        try:
            from trl import DataCollatorForCompletionOnlyLM
            resp_ids = tokenizer.encode("<start_of_turn>model\n",
                                        add_special_tokens=False)
            collator = DataCollatorForCompletionOnlyLM(
                response_template=resp_ids, tokenizer=tokenizer)
            print("[setup] completion-only loss enabled.")
        except Exception as e:
            print("[setup] completion-only collator unavailable (%s); "
                  "training with full-sequence loss." % type(e).__name__)
    kwargs = dict(model=model, args=cfg, train_dataset=train_ds,
                  eval_dataset=val_ds)
    if collator is not None:
        kwargs["data_collator"] = collator
    try:
        trainer = SFTTrainer(processing_class=tokenizer, **kwargs)
    except TypeError:
        trainer = SFTTrainer(tokenizer=tokenizer, **kwargs)
    if USE_UNSLOTH:
        try:
            from unsloth.chat_templates import train_on_responses_only
            trainer = train_on_responses_only(
                trainer,
                instruction_part="<start_of_turn>user\n",
                response_part="<start_of_turn>model\n",
            )
            print("[setup] unsloth train_on_responses_only enabled.")
        except Exception as e:
            print("[setup] train_on_responses_only unavailable (%s); "
                  "full-sequence loss." % type(e).__name__)
    return trainer

# ---------------------------------------------------------------------------
# Post-training smoke test
# ---------------------------------------------------------------------------

def smoke_test(model, tokenizer, val_path, n=3):
    print("\n[smoke test] generating on %d validation dictations:" % n)
    try:
        rows = []
        with open(val_path, encoding="utf-8") as f:
            for line in f:
                rows.append(json.loads(line))
                if len(rows) == n:
                    break
        model.eval()
        device = next(model.parameters()).device
        for row in rows:
            user_msg = [m for m in row["messages"] if m["role"] == "user"][0]
            inputs = tokenizer.apply_chat_template(
                [user_msg], tokenize=True, add_generation_prompt=True,
                return_tensors="pt").to(device)
            with torch.no_grad():
                out = model.generate(inputs, max_new_tokens=700,
                                     do_sample=False,
                                     pad_token_id=tokenizer.eos_token_id)
            text = tokenizer.decode(out[0][inputs.shape[1]:],
                                    skip_special_tokens=True)
            print("-" * 60)
            print("IN : %s" % user_msg["content"].split("\n\n", 1)[1][:220])
            print("OUT: %s" % text[:400])
    except Exception as e:
        print("[smoke test] skipped (%s: %s)" % (type(e).__name__, e))

# ---------------------------------------------------------------------------
# GGUF export
# ---------------------------------------------------------------------------

GGUF_NAME = "gemma3-270m-liquidflow-q8_0.gguf"

def gguf_commands(out_dir):
    return [
        ["git", "clone", "--depth", "1",
         "https://github.com/ggml-org/llama.cpp"],
        [sys.executable, "-m", "pip", "install", "-r",
         "llama.cpp/requirements/requirements-convert_hf_to_gguf.txt"],
        [sys.executable, "llama.cpp/convert_hf_to_gguf.py", out_dir,
         "--outfile", GGUF_NAME, "--outtype", "q8_0"],
    ]

def export_gguf(out_dir):
    print("\n[gguf] exporting %s -> %s" % (out_dir, GGUF_NAME))
    for cmd in gguf_commands(out_dir):
        if cmd[0] == "git" and os.path.isdir("llama.cpp"):
            print("[gguf] llama.cpp already cloned, skipping clone")
            continue
        print("[gguf] $ " + " ".join(cmd))
        subprocess.run(cmd, check=True)
    size = os.path.getsize(GGUF_NAME) / 1e6
    print("[gguf] wrote %s (%.0f MB)" % (GGUF_NAME, size))

# ---------------------------------------------------------------------------

def main():
    args = parse_args()
    set_seed(args.seed)
    if not torch.cuda.is_available():
        print("[warn] no CUDA GPU detected; training will be very slow. "
              "On Colab: Runtime -> Change runtime type -> T4 GPU.")

    model, tokenizer = load_model_and_tokenizer(args)
    train_ds = build_text_dataset(args.train, tokenizer)
    val_ds = build_text_dataset(args.val, tokenizer)
    print("[data] train=%d val=%d" % (len(train_ds), len(val_ds)))
    print("[data] sample (truncated):\n%s\n" % train_ds[0]["text"][:400])

    bf16_ok = (torch.cuda.is_available() and torch.cuda.is_bf16_supported())
    cfg = make_sft_config(args, bf16_ok)
    trainer = make_trainer(model, tokenizer, cfg, train_ds, val_ds)

    trainer.train()
    metrics = trainer.evaluate()
    print("[eval] final: %s" % {k: round(v, 4) for k, v in metrics.items()
                                if isinstance(v, (int, float))})

    print("[save] writing %s" % args.out)
    trainer.save_model(args.out)
    tokenizer.save_pretrained(args.out)

    smoke_test(model, tokenizer, args.val)

    print("\n" + "=" * 70)
    print("GGUF EXPORT (llama.cpp -> Q8_0)")
    print("=" * 70)
    print("Run these exact commands (or re-run this script with --gguf):\n")
    for cmd in gguf_commands(args.out):
        print("  $ " + " ".join(cmd))
    print("\nThen copy %s to the LiquidFlow model folder:" % GGUF_NAME)
    print("  %LOCALAPPDATA%\\LiquidFlow\\LocalAI\\Models\\" + GGUF_NAME)
    if args.gguf:
        export_gguf(args.out)

if __name__ == "__main__":
    main()
