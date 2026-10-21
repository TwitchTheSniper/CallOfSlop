"""Renders animated props at a few moments in time (a Python port of PropAnim.Animate), to check pivots and axes by eye.

Usage: python3 tools/propviz/viz_anim.py Source/Props/PropFile.cs Props/Animated sheet.png [name filter...]
Each prop gets one picture per time in TIMES (lit light parts are drawn in their "on" colour when lit).
"""
import json, math, sys, glob, os
import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import viz  # noqa: E402

TIMES = [0.0, 0.6, 1.3]
TYPES = {"follow": 0, "spin": 1, "rotate": 1, "swing": 2, "oscillate": 2, "bob": 3, "translate": 3, "shake": 4, "vibrate": 4,
         "blink": 5, "pulse": 6, "flicker": 7, "cycle": 8}


def axis_of(a, kind):
    ax = a.get("axis", "x" if kind == 2 else "y")
    if isinstance(ax, str):
        sgn = -1 if ax.startswith("-") else 1
        v = {"x": [1, 0, 0], "y": [0, 1, 0], "z": [0, 0, 1]}[ax[-1]]
        return np.array(v, float) * sgn
    v = np.array(ax, float)
    return v / np.linalg.norm(v)


def turn(v, k, ang):
    c, s = math.cos(ang), math.sin(ang)
    return v * c + np.cross(k, v) * s + k * np.dot(k, v) * (1 - c)


def fract(x):
    return x - math.floor(x)


def animate(parts, i, p, t, seed):
    """Position of point p of part i at time t (no LOD), and the light level of the part's own light (or -1)."""
    light = -1.0
    at = i
    depth = 0
    while at is not None and depth < 4:
        a = parts[at].get("anim")
        if a is None:
            break
        kind = TYPES[a["type"]]
        size = parts[at]["size"]
        pivot = np.array(a.get("pivot", parts[at].get("offset", [0, size[1] / 2, 0])), float)
        rate = a.get("rpm", 0) / 60 if kind == 1 else a.get("rate", 10) if kind == 7 else 1 / a.get("period", 0.08 if kind == 4 else 1)
        jitter = a.get("jitter", 0)
        phase = a.get("phase", 0) + (seed if a.get("randomPhase", True) else 0)
        u = t * rate * (1 + jitter * (2 * fract(seed * 7.13 + 0.37) - 1)) + phase
        ax = axis_of(a, kind)
        if kind == 1:
            p = pivot + turn(p - pivot, ax, 2 * math.pi * u)
        elif kind == 2:
            sv = math.sin(2 * math.pi * u)
            if a.get("oneWay"):
                sv = max(sv, 0)
            p = pivot + turn(p - pivot, ax, math.radians(a["angle"]) * sv)
        elif kind == 3:
            p = p + ax * a["distance"] * math.sin(2 * math.pi * u)
        elif kind == 4:
            p = p + ax * a["distance"] * (math.sin(2 * math.pi * u) * 0.6 + math.sin(2 * math.pi * u * 2.37 + 1.3) * 0.4)
        elif depth == 0 and kind >= 5:
            duty = a.get("duty", 0.5)
            if kind == 5:
                light = 1.0 if fract(u) < duty else 0.0
            elif kind == 6:
                light = 0.5 - 0.5 * math.cos(2 * math.pi * u)
            elif kind == 7:
                n = fract(52.9829189 * fract(math.floor(u) * 0.06711056 + seed * 17.0))
                light = 1.0 if n < duty else 0.15 + 0.3 * n
            else:
                step = math.floor(fract(u) * a["steps"])
                light = 1.0 if step in a["on"] else 0.0
        at = a["parent"] - 1 if "parent" in a else None
        depth += 1
    return p, light


def posed(prop, t, seed=0.37):
    """A copy of the prop with every animated part's box replaced by its corners at time t (as a list of triangles)."""
    out = []
    parts = prop["parts"]
    for i, part in enumerate(parts):
        s = np.array(part["size"]) / 2
        off = np.array(part.get("offset", [0, part["size"][1] / 2, 0]))
        R = viz.rot(*part.get("rotation", [0, 0, 0]))
        corners = np.array([[x, y, z] for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]) * s
        P = (R @ corners.T).T + off
        c = np.array(viz.col(part.get("color", "grey")))
        if "anim" in part:
            moved = [animate(parts, i, q, t, seed) for q in P]
            P = np.array([m[0] for m in moved])
            light = moved[0][1]
            a = part["anim"]
            if light >= 0:
                on = np.array(viz.col(a.get("color", part.get("color", "grey"))))
                off_c = np.array(viz.col(a["off"])) if "off" in a else c * 0.28
                c = off_c + (on - off_c) * light
        centre = P.mean(axis=0)
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        for f in faces:
            a_, b_, cc, d_ = (P[k] for k in f)
            n = np.cross(b_ - a_, d_ - a_)
            ln = np.linalg.norm(n)
            if ln < 1e-12:
                continue
            n = n / ln
            if np.dot(n, (a_ + cc) / 2 - centre) < 0:
                n = -n
            out.append((a_, b_, cc, n, c))
            out.append((a_, cc, d_, n, c))
    return out


def render_tris(T, size, view=(35, 25)):
    orig = viz.tris
    viz.tris = lambda prop: T
    try:
        return viz.render({}, size, view)
    finally:
        viz.tris = orig


if __name__ == "__main__":
    viz.load_colors(sys.argv[1])
    files = sorted(glob.glob(os.path.join(sys.argv[2], "**", "*.json"), recursive=True))
    only = sys.argv[4:] if len(sys.argv) > 4 else None
    props = [json.load(open(f)) for f in files]
    if only:
        props = [p for p in props if any(o.lower() in p["name"].lower() for o in only)]
    S = 170
    cols = 2
    per = len(TIMES)
    rows = (len(props) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * S * per + (cols - 1) * 8, rows * (S + 14)), (30, 30, 30))
    d = ImageDraw.Draw(sheet)
    for i, p in enumerate(props):
        x0, y0 = (i % cols) * (S * per + 8), (i // cols) * (S + 14)
        for k, t in enumerate(TIMES):
            sheet.paste(render_tris(posed(p, t), S), (x0 + k * S, y0))
        d.text((x0 + 4, y0 + S + 1), p["name"], fill=(255, 255, 255))
    sheet.save(sys.argv[3])
    print(len(props), "rendered")
