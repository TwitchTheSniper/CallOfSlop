#!/usr/bin/env python3
"""Tags and draws the two sides of every joint part (board item dev-joint-side-indicators-js01).

For each Constructs/Parts file whose role is hinge or joint, the "boxes" list is rebuilt:
  - the part's own solid box: "side": "base", dark fixed-looking colour;
  - a base mounting flange (dark, at the base socket's end) with four bolts: "side": "base";
  - a moving-side collar (safety orange, over the leaf half) and a direction cue on it (a stepped arrow: one head with a
    tail tick for a rotating joint, a head at each end for a piston): "side": "leaf".
Collar, flange, bolts and cue are detail boxes (no bounds, mass or collision) and not paintable, so painting a joint never
hides which side is which. Faces never share a plane (the no-z-fighting rule): the collar and flange stand 8 and 5 mm proud on
the sides and 4 mm inside the end faces, the cue steps differ by 2.5 mm, and the steps overlap by 2 mm.
Idempotent: boxes with a "side" key and the old gold leaf strips (#c7a23a) are dropped before rebuilding; the other detail
boxes stay. Run from the repo root: python3 -I tools/constructs/make_joint_sides.py   (--check: change nothing, exit 1 if a file would change)
"""
import glob, json, sys

BASE_BODY, FLANGE, BOLT, COLLAR, CUE = "#3b4046", "#202327", "#8d939a", "#f08a12", "#1c1c1c"
AXES = {"x": 0, "y": 1, "z": 2}


def dirof(s):  # "+x" -> (axis index, sign)
    return AXES[s[1]], (1 if s[0] == "+" else -1)


def mk(lo, hi, colour, side):
    return {"size": [round(hi[i] - lo[i], 4) for i in range(3)],
            "at": [round((hi[i] + lo[i]) / 2, 4) for i in range(3)],
            "color": colour, "detail": True, "side": side, "paintable": False}


def build(d):
    jb = d.get("joint") or d["hinge"]
    keep = [b for b in d["boxes"] if not (b.get("detail") and "side" in b) and b.get("color") != "#c7a23a"]
    main = next(b for b in keep if not b.get("detail"))
    others = [b for b in keep if b is not main]
    c = main.get("at", [0, 0, 0])
    h = [main["size"][i] / 2 for i in range(3)]
    socks = {s["name"]: s for s in d["sockets"]}
    a, sa = dirof(socks[jb["leaf"]]["dir"])
    b_, sb = dirof(socks["base"]["dir"])
    out = [dict(main, color=BASE_BODY, side="base")]
    out += others

    def box(ranges):  # ranges: {axis: (lo, hi)} -> lo/hi vectors
        lo = [0, 0, 0]
        hi = [0, 0, 0]
        for ax, (l, hh) in ranges.items():
            lo[ax], hi[ax] = min(l, hh), max(l, hh)
        return lo, hi

    ina = min(0.008, h[a] * 0.25)  # (the collar and flange end this far inside their faces)
    inb = min(0.008, h[b_] * 0.25)
    # Moving-side collar over the leaf half.
    cr = {}
    for ax in range(3):
        if ax == a:
            cr[ax] = (c[a] + sa * 0.1 * h[a], c[a] + sa * (h[a] - ina))
        else:
            cr[ax] = (c[ax] - h[ax] - 0.008, c[ax] + h[ax] + 0.008)
    out.append(mk(*box(cr), COLLAR, "leaf"))
    # Base flange and its bolts.
    tb = min(0.04, h[b_] * 0.8)
    fr = {}
    for ax in range(3):
        if ax == b_:
            fr[ax] = (c[b_] + sb * (h[b_] - inb - tb), c[b_] + sb * (h[b_] - inb))
        else:
            fr[ax] = (c[ax] - h[ax] - 0.005, c[ax] + h[ax] + 0.005)
    out.append(mk(*box(fr), FLANGE, "base"))
    mid = c[b_] + sb * (h[b_] - inb - tb / 2)
    p1, p2 = [ax for ax in range(3) if ax != b_]
    for s1 in (-1, 1):
        for s2 in (-1, 1):
            ctr = [0, 0, 0]
            ctr[b_] = mid
            ctr[p1] = c[p1] + s1 * (h[p1] + 0.005)
            ctr[p2] = c[p2] + s2 * (h[p2] + 0.005)
            out.append(mk(*box({ax: (ctr[ax] - 0.01, ctr[ax] + 0.01) for ax in range(3)}), BOLT, "base"))
    # Direction cue on the collar's +n face.
    axis_a = AXES[jb["axis"][1]] if "axis" in jb else a
    sliding = jb.get("type") == "piston"
    n = next(ax for ax in range(3) if ax != axis_a and ax != a) if axis_a != a else (0 if a != 0 else 1)
    t = axis_a if sliding else 3 - n - axis_a
    w = 3 - n - t
    ext_t = abs(cr[t][1] - cr[t][0])
    ext_w = abs(cr[w][1] - cr[w][0])
    lg = min(ext_t * 0.8, 0.6)
    if lg >= 0.05:
        u = max(0.011, min(min(ext_w, ext_t) / 9, 0.03))
        while 5 * u > ext_w * 0.9 and u > 0.011:
            u -= 0.002
        if 5 * u <= ext_w * 0.95:
            f = cr[n][1]
            wc = (cr[w][0] + cr[w][1]) / 2
            tc = (cr[t][0] + cr[t][1]) / 2
            T = lg / 2

            def glyph(t0, t1, across, up):
                lo = [0, 0, 0]
                hi = [0, 0, 0]
                lo[t], hi[t] = tc + min(t0, t1), tc + max(t0, t1)
                lo[w], hi[w] = wc - across / 2, wc + across / 2
                lo[n], hi[n] = f - 0.0045, f + up
                return mk(lo, hi, CUE, "leaf")

            ends = (1, -1) if sliding else (1,)
            out.append(glyph(-T + (0.5 * u if not sliding else -(T - 1.5 * u)), T - 1.5 * u, u, 0.0065))
            for e in ends:
                out.append(glyph(e * (T - 2 * u), e * (T - u), 5 * u, 0.009))
                out.append(glyph(e * (T - u - 0.002), e * T, 3 * u, 0.0115))
            if not sliding:
                out.append(glyph(-T, -T + u, 3 * u, 0.009))
    return out


def fmt(boxes):
    lines = []
    for b in boxes:
        lines.append("    " + json.dumps(b, separators=(", ", ": ")))
    return "[\n" + ",\n".join(lines) + "\n  ]"


def main():
    check = "--check" in sys.argv
    changed = 0
    for f in sorted(glob.glob("Constructs/Parts/**/*.json", recursive=True)):
        txt = open(f).read()
        d = json.loads(txt)
        if d.get("role") not in ("joint", "hinge"):
            continue
        boxes = build(d)
        i = txt.index('"boxes"')
        j = txt.index("[", i)
        depth, k = 0, j
        while True:
            depth += txt[k] == "["
            depth -= txt[k] == "]"
            if depth == 0:
                break
            k += 1
        new = txt[:j] + fmt(boxes) + txt[k + 1:]
        if new != txt:
            changed += 1
            if not check:
                open(f, "w").write(new)
    print(f"{changed} file(s) {'would change' if check else 'written'}")
    sys.exit(1 if check and changed else 0)


main()
