#!/usr/bin/env python3
"""Corrects coplanar faces in prop files, in place, by the smallest amount (tools/constructs/zfight.py).

    python3 -I tools/props/zfight_props.py FILE [FILE ...]

Only the "size" and "offset" of the parts that move are rewritten, so the file's layout stays as it was. A generator
that writes props calls zfight.settle_props on its parts instead (the output is never fixed by hand: CLAUDE.md, Lessons).
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "constructs"))
import zfight  # noqa: E402


def _skip_ws(t, i):
    while t[i] in " \t\r\n":
        i += 1
    return i


def _skip_string(t, i):
    i += 1
    while t[i] != '"':
        i += 2 if t[i] == "\\" else 1
    return i + 1


def _skip_value(t, i):
    i = _skip_ws(t, i)
    if t[i] == '"':
        return _skip_string(t, i)
    if t[i] in "[{":
        depth = 0
        while True:
            if t[i] == '"':
                i = _skip_string(t, i)
                continue
            if t[i] in "[{":
                depth += 1
            elif t[i] in "]}":
                depth -= 1
                if depth == 0:
                    return i + 1
            i += 1
    while t[i] not in ",]} \t\r\n":
        i += 1
    return i


def _object_keys(t, i):
    """The keys of the object at i as {key: (value start, value end)}, and the index after it."""
    keys = {}
    i = _skip_ws(t, i) + 1
    while True:
        i = _skip_ws(t, i)
        if t[i] == "}":
            return keys, i + 1
        if t[i] == ",":
            i += 1
            continue
        e = _skip_string(t, i)
        key = json.loads(t[i:e])
        i = _skip_ws(t, e) + 1  # past the colon
        s = _skip_ws(t, i)
        i = _skip_value(t, s)
        keys[key] = (s, i)


def part_spans(text, list_key="parts"):
    """Where each part's keys sit in the file's text: a list of {key: (start, end)}."""
    top, _ = _object_keys(text, _skip_ws(text, 0))
    s, e = top[list_key]
    out = []
    i = s + 1
    while True:
        i = _skip_ws(text, i)
        if text[i] == "]":
            return out
        if text[i] == ",":
            i += 1
            continue
        keys, i = _object_keys(text, i)
        out.append(keys)


def vec(v):
    return "[" + ", ".join(repr(int(x)) if float(x).is_integer() and abs(x) < 1e6 and isinstance(x, int) else repr(round(x, 5)) for x in v) + "]"


def replace_spans(text, edits):
    """edits: (start, end, new text)."""
    for s, e, new in sorted(edits, reverse=True):
        text = text[:s] + new + text[e:]
    return text


def settle_text(text):
    """The file's text with its coplanar faces corrected, and (parts changed, hits left)."""
    d = json.loads(text)
    parts = d.get("parts")
    if not isinstance(parts, list):
        return text, 0, 0
    drawn = [dict(p) for p in parts]
    before = json.dumps(drawn)
    changed, left = zfight.settle_props(drawn)
    if json.dumps(drawn) == before:
        return text, 0, left
    spans = part_spans(text)
    edits = []
    for old, new, sp in zip(parts, drawn, spans):
        for key in ("size", "offset"):
            if old.get(key) != new.get(key):
                edits.append((sp[key][0], sp[key][1], vec(new[key])))
    return replace_spans(text, edits), changed, left


def main(argv):
    models = boxes = left = 0
    for f in argv:
        with open(f, encoding="utf-8", newline="") as fh:
            text = fh.read()
        new, changed, rest = settle_text(text)
        left += rest
        if rest:
            print(f"{f}: {rest} hit(s) left")
        if new != text:
            models += 1
            boxes += changed
            with open(f, "w", encoding="utf-8", newline="") as fh:
                fh.write(new)
    print(f"{models} file(s), {boxes} part(s) corrected, {left} hit(s) left")
    return 1 if left else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
