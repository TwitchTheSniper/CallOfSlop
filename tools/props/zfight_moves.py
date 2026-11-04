#!/usr/bin/env python3
"""How far the boxes of a model moved between two git versions (CLAUDE.md, "No coplanar faces").

    python3 -I tools/props/zfight_moves.py OLD_REV [NEW_REV] [--paths A B ...] [--worst N] [--csv FILE]

A z-fighting correction is 2 to 5 mm, 10 mm on big or far-seen pieces (tools/constructs/zfight.py). This compares every
prop, construct part and weapon part changed between OLD_REV and NEW_REV (the working tree when NEW_REV is left out)
box by box, face by face in the box's own axes, and prints the distribution, boxes added or dropped, and the worst moves.
Files whose box count changed are matched by best fit, so a real redesign shows as a big move: read those by hand.
"""
import argparse
import json
import math
import os
import subprocess
import sys

BINS = [0, 0.5, 2, 5, 10, 25, 50, 100, 1e9]
LABELS = ["0", "<=0.5", "<=2", "<=5", "<=10", "<=25", "<=50", "<=100", ">100"]


def git(*a):
    return subprocess.run(["git", *a], capture_output=True, text=True, check=False)


def load(rev, f):
    if rev is None:
        try:
            with open(f, encoding="utf-8") as fh:
                return json.load(fh)
        except (OSError, ValueError):
            return None
    r = git("show", f"{rev}:{f}")
    if r.returncode:
        return None
    try:
        return json.loads(r.stdout)
    except ValueError:
        return None


def box_of(b):
    """(centre, size, rotation, the box's other keys) of a prop part, construct box or weapon part box, or None."""
    if "use" in b:
        return None
    if "min" in b:
        lo, hi = b["min"], b["max"]
        c = [(lo[k] + hi[k]) / 2 for k in range(3)]
        s = [hi[k] - lo[k] for k in range(3)]
    elif "size" in b:
        s = b["size"]
        c = b.get("offset", b.get("at", b.get("pos", [0, 0, 0])))
    else:
        return None
    if len(s) != 3 or len(c) != 3:
        return None
    rot = b.get("rotation", b.get("rot", [0, 0, 0]))
    return c, s, rot


def boxes_of(d):
    if not isinstance(d, dict):
        return None
    fmt = d.get("format", "")
    if fmt.startswith("call-of-slop-prop"):
        lst = d.get("parts")
    elif fmt.startswith("call-of-slop-construct-part") or fmt.startswith("call-of-slop-part"):
        lst = d.get("boxes") or d.get("model")
    else:
        return None
    if not isinstance(lst, list):
        return None
    return [(i, box_of(b), b) for i, b in enumerate(lst) if isinstance(b, dict)]


def rot_matrix(rot):
    """Rows of yaw * pitch * roll (PropPart.Rotation), so row k is world axis k in terms of the box's axes."""
    p, y, r = [math.radians(v) for v in rot]
    cp, sp, cy, sy, cr, sr = math.cos(p), math.sin(p), math.cos(y), math.sin(y), math.cos(r), math.sin(r)
    ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    rx = [[1, 0, 0], [0, cp, -sp], [0, sp, cp]]
    rz = [[cr, -sr, 0], [sr, cr, 0], [0, 0, 1]]
    def mul(a, b):
        return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mul(mul(ry, rx), rz)


def face_moves(a, b):
    """Each of the six faces' move (mm, signed outward), in the old box's axes, and the worst share of a size."""
    (ca, sa, ra), (cb, sb, rb) = a, b
    m = rot_matrix(ra)
    d = [cb[k] - ca[k] for k in range(3)]
    moves = []
    share = 0.0
    for k in range(3):
        axis = [m[0][k], m[1][k], m[2][k]]
        along = sum(axis[i] * d[i] for i in range(3))
        ds = (sb[k] - sa[k]) / 2
        moves += [(along + ds) * 1000, (-along + ds) * 1000]
        share = max(share, abs(sb[k] - sa[k]) / max(abs(sa[k]), 1e-6))
    return moves, share


def align(old, new):
    """Pairs of (old entry or None, new entry or None), by index when the counts match, else by nearest box."""
    if len(old) == len(new):
        return list(zip(old, new))
    used = set()
    pairs = []
    for o in old:
        best, score = None, 1e9
        if o[1]:
            for n in new:
                if n[0] in used or not n[1]:
                    continue
                s = sum(abs(o[1][0][k] - n[1][0][k]) + abs(o[1][1][k] - n[1][1][k]) for k in range(3))
                if s < score:
                    best, score = n, s
        if best is not None and score < 0.25:
            used.add(best[0])
            pairs.append((o, best))
        else:
            pairs.append((o, None))
    pairs += [(None, n) for n in new if n[0] not in used]
    return pairs


def bin_of(mm):
    for i, top in enumerate(BINS):
        if mm <= top + 1e-6:
            return i
    return len(BINS) - 1


def changed_files(old, new, paths):
    args = ["diff", "--name-only", old] + ([new] if new else []) + ["--"] + paths
    return [f for f in git(*args).stdout.split("\n") if f.endswith(".json")]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("old")
    ap.add_argument("new", nargs="?")
    ap.add_argument("--paths", nargs="*", default=["Props", "Maps", "Constructs/Parts", "Parts"])
    ap.add_argument("--files", help="read the list of files from this file")
    ap.add_argument("--worst", type=int, default=25)
    ap.add_argument("--csv")
    a = ap.parse_args()
    files = changed_files(a.old, a.new, a.paths)
    if a.files:
        with open(a.files, encoding="utf-8") as fh:
            files = [l.strip() for l in fh if l.strip()]
    hist = {}
    moved = []
    models = dropped = added = rotated = reshaped = same = 0
    shares = [0, 0, 0, 0]
    for f in files:
        ba, bb = boxes_of(load(a.old, f)), boxes_of(load(a.new, f))
        if ba is None or bb is None:
            continue
        kind = "construct" if f.startswith("Constructs") else "part" if f.startswith("Parts") else "prop"
        touched = False
        for o, n in align(ba, bb):
            if o is None:
                added += 1
                touched = True
                continue
            if n is None:
                dropped += 1
                touched = True
                moved.append((1e9, 1, f, o[0], "dropped"))
                continue
            if not o[1] or not n[1]:
                continue
            if o[1] == n[1] or (o[1][0] == n[1][0] and o[1][1] == n[1][1] and o[1][2] == n[1][2]):
                same += 1
                continue
            touched = True
            if any(abs(o[1][2][k] - n[1][2][k]) > 1e-6 for k in range(3)):
                rotated += 1
                moved.append((1e8, 1, f, o[0], f"rotation {o[1][2]} -> {n[1][2]}"))
            moves, share = face_moves(o[1], n[1])
            top = max(abs(m) for m in moves)
            k = (kind, bin_of(top))
            hist[k] = hist.get(k, 0) + 1
            shares[0 if share <= 0.02 else 1 if share <= 0.1 else 2 if share <= 0.25 else 3] += 1
            shortest = min(o[1][1])
            if share > 0.5:
                reshaped += 1
            moved.append((top, share, f, o[0], f"size {o[1][1]} -> {n[1][1]} at {o[1][0]} -> {n[1][0]} rot {o[1][2]}"))
        models += touched
    print(f"{models} models changed, {same} boxes unchanged, {len(moved)} boxes moved, {added} added, {dropped} dropped, {rotated} rotated")
    print("worst face move per box (mm):   " + " ".join(f"{l:>7}" for l in LABELS))
    for kind in ("prop", "construct", "part"):
        print(f"  {kind:<28}  " + " ".join(f"{hist.get((kind, i), 0):>7}" for i in range(len(LABELS))))
    print(f"size change as a share of that dimension: <=2% {shares[0]}, <=10% {shares[1]}, <=25% {shares[2]}, >25% {shares[3]} (>50%: {reshaped})")
    moved.sort(key=lambda m: (-m[0], -m[1]))
    for top, share, f, i, what in moved[:a.worst]:
        print(f"  {top:9.1f} mm {share * 100:5.0f}%  {f} #{i}: {what}")
    if a.csv:
        with open(a.csv, "w", encoding="utf-8") as fh:
            for top, share, f, i, what in moved:
                fh.write(f"{top:.2f}\t{share:.3f}\t{f}\t{i}\t{what}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
