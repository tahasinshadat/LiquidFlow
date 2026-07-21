# validate.py
# Programmatic quality gate for train.jsonl / val.jsonl.
#
# Enforced per pair:
#   1. Line parses as JSON with exactly {"messages": [user, assistant]}.
#   2. User content is the fixed instruction + "\n\n" + raw dictation.
#   3. Raw dictation is a single line (no newlines), 3-560 words, ASCII.
#   4. No novel content: every output token (after stripping list markers)
#      appears in the input at least as many times (case-sensitive multiset
#      containment). The model may only delete words and add structure.
#   5. Word retention: unique-word retention (lowercased, ignoring known
#      droppable connective/command/marker vocabulary) must be >= 0.85, or
#      >= 0.70 when the raw text contains a retraction marker phrase
#      (retractions legitimately delete the retracted content words).
#      Identity pairs trivially pass.
#   6. Output hygiene: no adjacent duplicated words, no leading/trailing
#      whitespace on any line, non-empty.
#   7. No duplicate inputs within a file, and none shared between train and
#      val (exact or punctuation/case-normalized).
#   8. Train identity fraction >= 0.15 (restraint cases).
#
# Exit code 0 and "ALL CHECKS PASSED" on success; nonzero otherwise.

import json
import re
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent

INSTRUCTION = ("Reformat this dictation. Keep the exact wording; only fix "
               "structure: lists, paragraphs, remove duplicated words and "
               "retracted false starts. If it's already fine, return it "
               "unchanged.")

TOKEN_RE = re.compile(r"[A-Za-z0-9']+")
MARKER_STRIP = re.compile(r"^(?:-|\*|•|\d{1,2}[.)])\s+")
ALPHA_RE = re.compile(r"^[A-Za-z']+$")

# Words the formatter is allowed to drop: enumeration connectives and
# ordinals, spoken formatting command words, and retraction marker words.
DROPPABLE = {
    "and", "then", "also", "plus", "next", "after", "that", "first", "second",
    "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth",
    "tenth", "finally", "lastly", "last", "one", "two", "three", "four",
    "five", "six", "seven", "eight", "nine", "ten", "number", "step", "new",
    "line", "paragraph", "break", "bullet", "point", "item", "dash", "no",
    "wait", "sorry", "mean", "scratch", "actually", "make", "hold", "on",
    "er", "uh", "um", "oh", "i",
}

RETRACT_HINTS = ["no wait", "wait no", "i mean", "scratch that", "make that",
                 "no sorry", "actually no", "no hold on", "sorry"]

def tokens(text):
    return TOKEN_RE.findall(text)

def out_tokens(out):
    toks = []
    for ln in out.split("\n"):
        toks.extend(tokens(MARKER_STRIP.sub("", ln)))
    return toks

def norm_text(text):
    return " ".join(t.lower() for t in tokens(text))

def check_pair(raw, out, errors, where):
    def err(msg):
        errors.append("%s: %s [raw: %.90r]" % (where, msg, raw))

    if "\n" in raw:
        err("raw dictation contains a newline")
    nwords = len(raw.split())
    if not (3 <= nwords <= 560):
        err("raw word count %d outside 3..560" % nwords)
    if not out.strip():
        err("empty output")
        return
    for ch in raw + out:
        if ord(ch) > 127:
            err("non-ascii character %r" % ch)
            break
    for ln in out.split("\n"):
        if ln != ln.strip() and ln.strip():
            err("output line has leading/trailing whitespace: %r" % ln)

    in_toks = tokens(raw)
    o_toks = out_tokens(out)

    # 4. multiset containment (case-sensitive): output adds no words
    in_counts = Counter(in_toks)
    for tok, n in Counter(o_toks).items():
        if n > in_counts.get(tok, 0):
            err("novel/extra token in output: %r (out %d > in %d)"
                % (tok, n, in_counts.get(tok, 0)))
            break

    # 5. retention
    if raw != out:
        in_set = {t.lower() for t in in_toks} - DROPPABLE
        out_set = {t.lower() for t in o_toks} - DROPPABLE
        if in_set:
            ratio = len(in_set & out_set) / len(in_set)
            raw_l = raw.lower()
            threshold = 0.70 if any(h in raw_l for h in RETRACT_HINTS) else 0.85
            if ratio < threshold:
                dropped = sorted(in_set - out_set)[:8]
                err("retention %.3f < %.2f (dropped: %s)"
                    % (ratio, threshold, dropped))

    # 6. no adjacent duplicate words in output
    for ln in out.split("\n"):
        lt = tokens(MARKER_STRIP.sub("", ln))
        for a, b in zip(lt, lt[1:]):
            if a.lower() == b.lower() and ALPHA_RE.match(a):
                err("adjacent duplicate %r in output line %r" % (a, ln))
                break

def load_file(path, errors):
    records = []
    with open(path, encoding="utf-8") as f:
        for i, line in enumerate(f, 1):
            where = "%s:%d" % (path.name, i)
            line = line.rstrip("\n")
            if not line:
                errors.append("%s: blank line" % where)
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError as e:
                errors.append("%s: json parse error: %s" % (where, e))
                continue
            if set(obj.keys()) != {"messages"}:
                errors.append("%s: keys %s != {'messages'}" % (where, set(obj)))
                continue
            msgs = obj["messages"]
            if (not isinstance(msgs, list) or len(msgs) != 2
                    or msgs[0].get("role") != "user"
                    or msgs[1].get("role") != "assistant"
                    or set(msgs[0]) != {"role", "content"}
                    or set(msgs[1]) != {"role", "content"}):
                errors.append("%s: bad messages structure" % where)
                continue
            user = msgs[0]["content"]
            prefix = INSTRUCTION + "\n\n"
            if not user.startswith(prefix):
                errors.append("%s: user content missing fixed instruction" % where)
                continue
            raw = user[len(prefix):]
            out = msgs[1]["content"]
            check_pair(raw, out, errors, where)
            records.append((raw, out))
    return records

def structure_histogram(records):
    hist = Counter()
    for raw, out in records:
        if raw == out:
            hist["identity"] += 1
        elif re.search(r"(?m)^\d{1,2}\. ", out):
            hist["numbered_list"] += 1
        elif "\n- " in out or out.startswith("- "):
            hist["bulleted_list"] += 1
        elif "\n\n" in out:
            hist["paragraph_breaks"] += 1
        elif "\n" in out:
            hist["line_breaks"] += 1
        else:
            hist["inline_cleanup"] += 1
    return hist

def main():
    errors = []
    train_path = HERE / "train.jsonl"
    val_path = HERE / "val.jsonl"
    train = load_file(train_path, errors)
    val = load_file(val_path, errors)

    # 7. duplicates
    for name, recs in (("train", train), ("val", val)):
        raws = [r for r, _ in recs]
        for raw, n in Counter(raws).items():
            if n > 1:
                errors.append("%s: duplicate input x%d: %.80r" % (name, n, raw))
    train_exact = {r for r, _ in train}
    val_exact = {r for r, _ in val}
    for raw in train_exact & val_exact:
        errors.append("train/val exact overlap: %.80r" % raw)
    train_norm = {norm_text(r) for r, _ in train}
    val_norm = {norm_text(r) for r, _ in val}
    for t in train_norm & val_norm:
        errors.append("train/val normalized overlap: %.80r" % t)

    # 8. identity fraction and minimum sizes
    if len(train) < 1200:
        errors.append("train has %d examples, need >= 1200" % len(train))
    if len(val) < 80:
        errors.append("val has %d examples, need >= 80" % len(val))
    if train:
        ident = sum(1 for r, o in train if r == o)
        frac = ident / len(train)
        if frac < 0.15:
            errors.append("train identity fraction %.3f < 0.15" % frac)

    for name, recs in (("train", train), ("val", val)):
        if not recs:
            continue
        ident = sum(1 for r, o in recs if r == o)
        lens = sorted(len(r.split()) for r, _ in recs)
        print("%s: %d examples | identity: %d (%.1f%%) | raw words min/median/max: %d/%d/%d"
              % (name, len(recs), ident, 100.0 * ident / len(recs),
                 lens[0], lens[len(lens) // 2], lens[-1]))
        for k, v in structure_histogram(recs).most_common():
            print("    %-18s %d" % (k, v))

    if errors:
        print("\n%d ERROR(S):" % len(errors))
        for e in errors[:40]:
            print("  " + e)
        if len(errors) > 40:
            print("  ... and %d more" % (len(errors) - 40))
        sys.exit(1)
    print("\nALL CHECKS PASSED")

if __name__ == "__main__":
    main()
