# build_dataset.py
# Assembles the LiquidFlow dictation-reformatter fine-tuning dataset.
#
# Every example is a (raw dictation -> structurally formatted text) pair built
# from the same underlying authored content, so the formatted side is correct
# by construction: identical wording, with only structural edits (list
# markers, newlines, removal of stutter duplicates, spoken formatting
# commands, and retracted false starts).
#
# Usage:
#   python build_dataset.py            # writes train.jsonl and val.jsonl
#   python build_dataset.py --preview 8

import argparse
import json
import math
import random
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from banks_core import (LIST_POOLS, LIST_FRAMES, STEP_SEQUENCES, RETRACTIONS,
                        CLAUSE_RESTARTS, BASE_SENTENCES, FILLER_KEEPERS)
from banks_prose import PARAGRAPH_PIECES, LONG_PIECES, MIXED_PIECES
from banks_msgs import EMAILS, MEETINGS, NEWLINE_PIECES, BULLET_CMD_PIECES
from banks_identity import (IDENTITY_SHORT, IDENTITY_RESTRAINT, IDENTITY_MEDIUM,
                            NUMERIC_IDENTITY, CODE_PROMPTS_PROSE, CODE_PROMPTS_LISTY)

SEED = 20260720

INSTRUCTION = ("Reformat this dictation. Keep the exact wording; only fix "
               "structure: lists, paragraphs, remove duplicated words and "
               "retracted false starts. If it's already fine, return it "
               "unchanged.")

# ---------------------------------------------------------------------------
# Text transforms (applied to the source strings BEFORE rendering, so raw and
# formatted output always share identical wording).
# ---------------------------------------------------------------------------

def tf_ident(s):
    return s

def tf_lower(s):
    return s.lower()

_DEPUNCT_MID = re.compile(r"[,.;:!?]+(?=\s)")
_DEPUNCT_END = re.compile(r"[,.;:!?]+$")

def depunct(s):
    """Remove sentence punctuation (keeps intra-token punctuation like 9:30,
    3.5, wd-40, emails, urls, apostrophes)."""
    s = _DEPUNCT_MID.sub("", s)
    s = _DEPUNCT_END.sub("", s)
    return s

def tf_lowdep(s):
    return depunct(s.lower())

def pick_mode(rng, allow_lowdep=True):
    r = rng.random()
    if r < 0.55:
        return tf_ident, "punct"
    if r < 0.75 or not allow_lowdep:
        return tf_lower, "lower"
    return tf_lowdep, "lowdep"

# ---------------------------------------------------------------------------
# Stutter injection (raw side only).
# ---------------------------------------------------------------------------

STUTTER_AVOID = {
    "no", "wait", "actually", "sorry", "mean", "scratch", "new", "line",
    "paragraph", "bullet", "point", "first", "second", "third", "then",
    "finally", "number", "step", "item", "next", "also", "plus", "oh",
}
_WORD_PUNCT = re.compile(r"^([A-Za-z][A-Za-z']+)([,.!?;:]?)$")

def inject_stutters(raw, rng, n=1):
    words = raw.split(" ")
    cands = []
    for i, w in enumerate(words):
        m = _WORD_PUNCT.match(w)
        if m and m.group(1).lower() not in STUTTER_AVOID:
            cands.append(i)
    if not cands:
        return raw
    rng.shuffle(cands)
    chosen = []
    for i in cands:
        if all(abs(i - j) > 2 for j in chosen):
            chosen.append(i)
        if len(chosen) == n:
            break
    for i in sorted(chosen, reverse=True):
        m = _WORD_PUNCT.match(words[i])
        base = m.group(1)
        prev_ok = i > 0 and _WORD_PUNCT.match(words[i - 1]) and \
            _WORD_PUNCT.match(words[i - 1]).group(2) == "" and \
            _WORD_PUNCT.match(words[i - 1]).group(1).lower() not in STUTTER_AVOID
        r = rng.random()
        if r < 0.18 and prev_ok:
            # bigram repeat: "i think i think"
            pbase = _WORD_PUNCT.match(words[i - 1]).group(1)
            words[i - 1:i - 1] = [pbase, base]
        elif r < 0.28:
            # triple: "the the the"
            words[i:i] = [base, base]
        else:
            words[i:i] = [base]
    return " ".join(words)

# ---------------------------------------------------------------------------
# Spoken linearizations of lists.
# ---------------------------------------------------------------------------

def speak_commas_and(items, rng, punct):
    if punct:
        if len(items) == 2:
            return items[0] + " and " + items[1]
        style = rng.random()
        if style < 0.25:
            return ", ".join(items)
        joiner = ", and " if style < 0.75 else " and "
        return ", ".join(items[:-1]) + joiner + items[-1]
    return " and ".join(items)

def speak_sprinkle(items, rng, punct):
    conns = ["also", "oh and", "plus", "and"]
    if punct:
        parts = [items[0]]
        for it in items[1:-1]:
            if rng.random() < 0.35:
                parts.append(rng.choice(conns) + " " + it)
            else:
                parts.append(it)
        parts.append(rng.choice(["and ", "oh and ", "and also "]) + items[-1])
        return ", ".join(parts)
    parts = [items[0]]
    for it in items[1:]:
        parts.append(rng.choice(conns + ["and then"]) + " " + it)
    return " ".join(parts)

def speak_ordinal(items, rng, punct):
    mids = ["then", "after that", "next", "and then"]
    parts = ["first " + items[0]]
    for it in items[1:-1]:
        parts.append(rng.choice(mids) + " " + it)
    if len(items) > 1:
        parts.append(rng.choice(["and finally", "finally", "and last"]) + " " + items[-1])
    return ", ".join(parts) if punct else " ".join(parts)

NUM_WORDS = ["one", "two", "three", "four", "five", "six", "seven", "eight",
             "nine", "ten"]

def speak_numberwords(items, rng, punct):
    parts = ["number %s %s" % (NUM_WORDS[k], it) for k, it in enumerate(items)]
    return ", ".join(parts) if punct else " ".join(parts)

def render_bullets(items):
    return "\n".join("- " + it for it in items)

def render_numbered(items):
    return "\n".join("%d. %s" % (k + 1, it) for k, it in enumerate(items))

INTRO_NO_COMMA_ENDINGS = ("need", "to", "pack", "forget", "grab")

def join_intro(intro, spoken, punct):
    if punct and not intro.rstrip().endswith(INTRO_NO_COMMA_ENDINGS):
        return intro + ", " + spoken
    return intro + " " + spoken

# ---------------------------------------------------------------------------
# Record helper
# ---------------------------------------------------------------------------

def rec(raw, out, cat, group):
    raw = re.sub(r"  +", " ", raw).strip()
    out = "\n".join(ln.rstrip() for ln in out.split("\n")).strip()
    return {"raw": raw, "out": out, "cat": cat, "group": group}

# ---------------------------------------------------------------------------
# Generators
# ---------------------------------------------------------------------------

LIST_COUNTS = {
    "grocery": 84, "work": 66, "home": 48, "school": 26, "packing": 24,
    "pharmacy": 14, "hardware": 14, "party": 14, "fitness": 14,
    "sideproject": 26,
}

def gen_lists(rng):
    out = []
    for domain, count in LIST_COUNTS.items():
        pool = LIST_POOLS[domain]
        frames = LIST_FRAMES[domain]
        for i in range(count):
            tf, mode = pick_mode(rng)
            punct = mode != "lowdep"
            n = rng.randint(3, min(9, len(pool)))
            items = [tf(x) for x in rng.sample(pool, n)]
            intro = tf(rng.choice(frames["intro"]))
            outro = tf(rng.choice(frames["outro"])) if rng.random() < 0.3 else None
            numbered = domain in ("work", "home", "school", "sideproject") and rng.random() < 0.25
            if numbered:
                spoken = speak_ordinal(items, rng, punct)
                body = render_numbered(items)
            else:
                speak = speak_sprinkle if rng.random() < 0.3 else speak_commas_and
                spoken = speak(items, rng, punct)
                body = render_bullets(items)
            raw = join_intro(intro, spoken, punct)
            outp = intro + "\n" + body
            if outro:
                raw += (", " if punct else " ") + outro
                outp += "\n" + outro
            if rng.random() < 0.2:
                raw = inject_stutters(raw, rng, rng.randint(1, 2))
            out.append(rec(raw, outp, "list", "list-%s-%d" % (domain, i)))
    return out

def gen_steps(rng):
    out = []
    for si, seq in enumerate(STEP_SEQUENCES):
        variants = []
        variants.append((tf_ident, True, "ordinal"))
        variants.append((tf_lowdep, False, "ordinal"))
        if si % 2 == 0:
            variants.append((tf_lower, True, "number"))
        if si % 3 == 0:
            variants.append((tf_lower, True, "ordinal"))
        for vi, (tf, punct, style) in enumerate(variants):
            steps = [tf(s) for s in seq["steps"]]
            intro_choice = rng.choice(seq["intros"])
            intro = tf(intro_choice) if intro_choice else None
            speak = speak_ordinal if style == "ordinal" else speak_numberwords
            spoken = speak(steps, rng, punct)
            if intro:
                raw = join_intro(intro, spoken, punct)
                outp = intro + "\n" + render_numbered(steps)
            else:
                raw = spoken
                outp = render_numbered(steps)
            if rng.random() < 0.25:
                raw = inject_stutters(raw, rng, 1)
            out.append(rec(raw, outp, "steps", "steps-%d" % si))
    return out

RETRACT_MARKERS = ["no wait", "wait no", "sorry", "no sorry", "actually no",
                   "i mean", "no i mean", "make that", "actually make that",
                   "scratch that", "er i mean", "uh i mean", "no hold on"]
RESTART_MARKERS = ["actually scratch that", "no scratch that", "wait no",
                   "hold on i mean", "no wait"]

def gen_retractions(rng):
    out = []
    for ri, u in enumerate(RETRACTIONS):
        n_var = 4 if ri % 2 == 0 else 3
        markers = rng.sample(RETRACT_MARKERS, n_var)
        for vi in range(n_var):
            tf, mode = pick_mode(rng)
            punct = mode != "lowdep"
            pre, wrong, right, post = (tf(u["pre"]), tf(u["wrong"]),
                                       tf(u["right"]), tf(u["post"]))
            marker = markers[vi]
            if punct:
                raw = "%s %s, %s, %s %s" % (pre, wrong, marker, right, post)
            else:
                raw = "%s %s %s %s %s" % (pre, wrong, marker, right, post)
            outp = "%s %s %s" % (pre, right, post)
            if rng.random() < 0.2:
                raw = inject_stutters(raw, rng, 1)
            out.append(rec(raw, outp, "retraction", "retr-%d" % ri))
    for ci, u in enumerate(CLAUSE_RESTARTS):
        for vi in range(2):
            tf, mode = pick_mode(rng)
            punct = mode != "lowdep"
            pre, fs, corr, post = (tf(u["pre"]), tf(u["false_start"]),
                                   tf(u["correct"]), tf(u["post"]))
            marker = rng.choice(RESTART_MARKERS)
            if punct:
                raw = "%s %s, %s, %s %s" % (pre, fs, marker, corr, post)
            else:
                raw = "%s %s %s %s %s" % (pre, fs, marker, corr, post)
            outp = "%s %s %s" % (pre, corr, post)
            out.append(rec(raw, outp, "retraction", "restart-%d" % ci))
    return out

def gen_stutter_prose(rng):
    out = []
    for bi, base in enumerate(BASE_SENTENCES):
        group = "base-%d" % bi
        r = rng.random()
        if r < 0.3:  # pure identity
            out.append(rec(base, base, "identity", group))
            if r < 0.12:  # plus one stutter variant on the same base
                raw = inject_stutters(base, rng, 1)
                out.append(rec(raw, base, "stutter", group))
        else:
            n_var = 2 if rng.random() < 0.8 else 1
            for _ in range(n_var):
                tf, mode = pick_mode(rng)
                clean = tf(base)
                raw = inject_stutters(clean, rng, rng.randint(1, 3))
                if raw == clean:
                    continue
                out.append(rec(raw, clean, "stutter", group))
    for fi, s in enumerate(FILLER_KEEPERS):
        group = "filler-%d" % fi
        out.append(rec(s, s, "identity", group))
        raw = inject_stutters(s, rng, 1)
        if raw != s:
            out.append(rec(raw, s, "stutter", group))
    return out

def gen_paragraphs(rng):
    out = []
    for pi, piece in enumerate(PARAGRAPH_PIECES):
        group = "para-%d" % pi
        native_lower = piece.get("native_lower", False)
        variants = [tf_ident]
        if not native_lower:
            variants.append(tf_lower if rng.random() < 0.6 else tf_lowdep)
            if pi % 4 == 0:
                variants.append(tf_lowdep if variants[-1] is tf_lower else tf_lower)
        for tf in variants:
            paras = [tf(p) for p in piece["paras"]]
            raw = " ".join(paras)
            outp = "\n\n".join(paras)
            if rng.random() < 0.25:
                raw = inject_stutters(raw, rng, rng.randint(1, 2))
            out.append(rec(raw, outp, "paragraphs", group))
    for li, piece in enumerate(LONG_PIECES):
        group = "long-%d" % li
        variants = [tf_ident, tf_lower]
        for tf in variants:
            paras = [tf(p) for p in piece["paras"]]
            raw = " ".join(paras)
            outp = "\n\n".join(paras)
            if rng.random() < 0.3:
                raw = inject_stutters(raw, rng, 2)
            out.append(rec(raw, outp, "paragraphs", group))
    return out

PARA_CMDS = ["new paragraph", "next paragraph", "paragraph break"]

def gen_emails(rng):
    out = []
    for ei, em in enumerate(EMAILS):
        group = "email-%d" % ei
        blocks = [em["greeting"]] + em["paras"] + [em["closing"]]
        sig = em.get("sig")
        # variant 1: plain run-on dictation
        raw = " ".join(blocks) + (" " + sig if sig else "")
        outp = em["greeting"] + "\n\n" + "\n\n".join(em["paras"]) + \
            "\n\n" + em["closing"] + ("\n" + sig if sig else "")
        if rng.random() < 0.2:
            raw = inject_stutters(raw, rng, 1)
        out.append(rec(raw, outp, "email", group))
        # extra variant: fully lowercased plain dictation
        if ei % 2 == 0:
            low_blocks = [b.lower() for b in blocks]
            low_sig = sig.lower() if sig else None
            raw_lo = " ".join(low_blocks) + (" " + low_sig if low_sig else "")
            out_lo = (em["greeting"].lower() + "\n\n" +
                      "\n\n".join(p.lower() for p in em["paras"]) + "\n\n" +
                      em["closing"].lower() + ("\n" + low_sig if low_sig else ""))
            out.append(rec(raw_lo, out_lo, "email", group))
        # variant 2: dictated with spoken commands
        cmd = rng.choice(PARA_CMDS)
        parts = [em["greeting"]]
        for p in em["paras"]:
            parts.append(cmd)
            parts.append(p)
        parts.append(cmd)
        parts.append(em["closing"])
        if sig:
            parts.append("new line")
            parts.append(sig)
        raw2 = " ".join(parts)
        out.append(rec(raw2, outp, "email_cmd", group))
    return out

def gen_meetings(rng):
    out = []
    for mi, m in enumerate(MEETINGS):
        group = "meet-%d" % mi
        variants = [(tf_ident, True)]
        if rng.random() < 0.85:
            variants.append((tf_lower, True))
        for tf, punct in variants:
            intro = tf(m["intro"])
            pre = tf(m["pre"]) if m.get("pre") else None
            post = tf(m["post"]) if m.get("post") else None
            raw_parts = [intro + ("." if punct else "")]
            out_parts = [intro]
            if pre:
                raw_parts.append(pre)
                out_parts.append(pre)
            for sec in m["sections"]:
                label = tf(sec["label"])
                items = [tf(it) for it in sec["items"]]
                raw_parts.append(label + (", " if punct else " ") +
                                 speak_commas_and(items, rng, punct) +
                                 ("." if punct else ""))
                out_parts.append(label + "\n" + render_bullets(items))
            if post:
                raw_parts.append(post)
                out_parts.append(post)
            raw = " ".join(raw_parts)
            if not punct:
                raw = depunct(raw)
            if rng.random() < 0.2:
                raw = inject_stutters(raw, rng, 1)
            out.append(rec(raw, "\n\n".join(out_parts), "meeting", group))
    return out

def gen_mixed(rng):
    out = []
    for xi, m in enumerate(MIXED_PIECES):
        group = "mixed-%d" % xi
        variants = [(tf_ident, True)]
        if rng.random() < 0.8:
            variants.append((tf_lowdep, False))
        for tf, punct in variants:
            pre = tf(m["pre"])
            label = tf(m["label"]) if m.get("label") else None
            items = [tf(it) for it in m["items"]]
            post = tf(m["post"]) if m.get("post") else None
            if m["ordered"]:
                spoken = speak_ordinal(items, rng, punct)
                body = render_numbered(items)
            else:
                spoken = speak_commas_and(items, rng, punct)
                body = render_bullets(items)
            raw_parts = [pre]
            if label:
                raw_parts.append(label + (", " if punct else " ") + spoken +
                                 ("." if punct else ""))
            else:
                raw_parts.append(spoken + ("." if punct else ""))
            if post:
                raw_parts.append(post)
            raw = " ".join(raw_parts)
            if not punct:
                raw = depunct(raw)
            out_parts = [pre]
            out_parts.append((label + "\n" + body) if label else body)
            if post:
                out_parts.append(post)
            if rng.random() < 0.2:
                raw = inject_stutters(raw, rng, 1)
            out.append(rec(raw, "\n\n".join(out_parts), "mixed", group))
    return out

def gen_newline_cmds(rng):
    out = []
    for ni, piece in enumerate(NEWLINE_PIECES):
        group = "nl-%d" % ni
        for vi, cmd in enumerate(rng.sample(["new line", "next line"], 2)):
            tf = tf_ident if vi == 0 else tf_lower
            lines = [tf(x) for x in piece["lines"]]
            raw = (" %s " % cmd).join(lines)
            outp = "\n".join(lines)
            out.append(rec(raw, outp, "newline_cmd", group))
    return out

def gen_bullet_cmds(rng):
    out = []
    cmds = ["bullet point", "bullet", "next bullet", "dash"]
    for bi, piece in enumerate(BULLET_CMD_PIECES):
        group = "bp-%d" % bi
        n_var = 2
        for vi in range(n_var):
            tf = tf_ident if vi == 0 else tf_lower
            intro = tf(piece["intro"])
            items = [tf(x) for x in piece["items"]]
            if bi % 5 == 4 and vi == 0:
                spoken = speak_numberwords(items, rng, False)
                body = render_numbered(items)
            else:
                cmd = rng.choice(cmds)
                spoken = " ".join("%s %s" % (cmd, it) for it in items)
                body = render_bullets(items)
            raw = intro + " " + spoken
            outp = intro + "\n" + body
            out.append(rec(raw, outp, "bullet_cmd", group))
    return out

def gen_code(rng):
    out = []
    for ci, s in enumerate(CODE_PROMPTS_PROSE):
        group = "code-%d" % ci
        if ci % 2 == 0:
            out.append(rec(s, s, "identity", group))
        raw = inject_stutters(s, rng, rng.randint(1, 2))
        if raw != s:
            out.append(rec(raw, s, "code_stutter", group))
        if ci % 3 == 0:
            raw2 = inject_stutters(s, rng, 2)
            if raw2 not in (s, raw):
                out.append(rec(raw2, s, "code_stutter", group))
    for li, m in enumerate(CODE_PROMPTS_LISTY):
        group = "codelist-%d" % li
        n_var = 2 if li % 2 == 0 else 1
        for vi in range(n_var):
            punct = True
            items = list(m["items"])
            spoken = speak_commas_and(items, rng, punct) if vi == 0 \
                else speak_sprinkle(items, rng, punct)
            raw = m["pre"] + " " + spoken + "."
            if m.get("post"):
                raw += " " + m["post"]
            out_parts = [m["pre"], render_bullets(items)]
            if m.get("post"):
                out_parts.append(m["post"])
            outp = "\n\n".join(out_parts)
            if rng.random() < 0.25:
                raw = inject_stutters(raw, rng, 1)
            out.append(rec(raw, outp, "code_list", group))
    return out

def gen_identity(rng):
    out = []
    def add(strings, prefix, lower_frac):
        for i, s in enumerate(strings):
            group = "%s-%d" % (prefix, i)
            out.append(rec(s, s, "identity", group))
            if rng.random() < lower_frac and s != s.lower():
                low = s.lower()
                out.append(rec(low, low, "identity", group))
    add(IDENTITY_SHORT, "idshort", 0.30)
    add(IDENTITY_RESTRAINT, "idrestr", 0.35)
    add(IDENTITY_MEDIUM, "idmed", 0.50)
    add(NUMERIC_IDENTITY, "idnum", 0.40)
    return out

# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

def build():
    rng = random.Random(SEED)
    records = []
    records += gen_lists(rng)
    records += gen_steps(rng)
    records += gen_retractions(rng)
    records += gen_stutter_prose(rng)
    records += gen_paragraphs(rng)
    records += gen_emails(rng)
    records += gen_meetings(rng)
    records += gen_mixed(rng)
    records += gen_newline_cmds(rng)
    records += gen_bullet_cmds(rng)
    records += gen_code(rng)
    records += gen_identity(rng)

    # exact dedup on raw text
    seen = set()
    deduped = []
    for r in records:
        key = r["raw"]
        if key in seen:
            continue
        seen.add(key)
        deduped.append(r)
    return deduped

def split(records, rng, val_target=88):
    by_cat_group = defaultdict(lambda: defaultdict(list))
    for r in records:
        by_cat_group[r["cat"]][r["group"]].append(r)
    total = len(records)
    frac = val_target / total
    train, val = [], []
    for cat in sorted(by_cat_group):
        groups = sorted(by_cat_group[cat])
        rng.shuffle(groups)
        cat_total = sum(len(by_cat_group[cat][g]) for g in groups)
        quota = max(1, int(round(cat_total * frac)))
        got = 0
        for g in groups:
            recs = by_cat_group[cat][g]
            if got < quota:
                val.extend(recs)
                got += len(recs)
            else:
                train.extend(recs)
    rng.shuffle(train)
    rng.shuffle(val)
    return train, val

def to_jsonl_line(r):
    return json.dumps({"messages": [
        {"role": "user", "content": INSTRUCTION + "\n\n" + r["raw"]},
        {"role": "assistant", "content": r["out"]},
    ]}, ensure_ascii=True)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", type=int, default=0,
                    help="print N random samples instead of writing files")
    args = ap.parse_args()

    records = build()
    rng = random.Random(SEED + 1)

    if args.preview:
        samples = rng.sample(records, min(args.preview, len(records)))
        for r in samples:
            print("=" * 72)
            print("[%s / %s]" % (r["cat"], r["group"]))
            print("--- RAW ---")
            print(r["raw"])
            print("--- FORMATTED ---")
            print(r["out"])
        return

    train, val = split(records, rng)
    train_path = HERE / "train.jsonl"
    val_path = HERE / "val.jsonl"
    with open(train_path, "w", encoding="utf-8", newline="\n") as f:
        for r in train:
            f.write(to_jsonl_line(r) + "\n")
    with open(val_path, "w", encoding="utf-8", newline="\n") as f:
        for r in val:
            f.write(to_jsonl_line(r) + "\n")

    def summarize(name, recs):
        cats = Counter(r["cat"] for r in recs)
        ident = sum(1 for r in recs if r["raw"] == r["out"])
        lens = sorted(len(r["raw"].split()) for r in recs)
        print("%s: %d examples | identity: %d (%.1f%%) | words min/med/max: %d/%d/%d"
              % (name, len(recs), ident, 100.0 * ident / len(recs),
                 lens[0], lens[len(lens) // 2], lens[-1]))
        for cat, n in sorted(cats.items(), key=lambda kv: -kv[1]):
            print("    %-12s %d" % (cat, n))

    summarize("train", train)
    summarize("val", val)
    print("wrote %s and %s" % (train_path, val_path))

if __name__ == "__main__":
    main()
