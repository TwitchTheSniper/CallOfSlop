#!/usr/bin/env python3
"""Writes the Sandbox's free rotation parts (Constructs/Parts/Free): joints that turn with no motor, spring or power.

docs/sandbox/CONSTRUCTS.md, "Free rotation"; Source/Constructs/ConstructLinear.cs. A Hinge is a door's: it springs to its latch and
its leaf socket takes doors. These do neither: what hangs on them turns freely, for as long as it likes, and a physics gun
turns them by the moving side. Two shapes: a Free Swivel turns about the line through its leaf socket (a lazy Susan), a Free
Pivot about an axis across it (a pendulum). A Lock signal holds it; limits are a setting, off to begin with.

Run it from the repo root: python3 -I tools/constructs/free_parts.py (--check only compares), then tools/constructs/assembling.py
for the recipes. make_joint_sides.py leaves these alone: they draw their own sides.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Free")
BASE, COLLAR, CUE = "#3b4046", "#2f9e8f", "#1c1c1c"

SIZES = [(SMALL, 1), (SMALL, 2), (SMALL, 3), (MEDIUM, 1), (LARGE, 1), (CAPITAL, 1)]


def num(key, label, lo, hi, step, tip, default):
    return {"key": key, "label": label, "group": "Limits", "unit": "deg", "min": lo, "max": hi, "step": step, "tooltip": tip, "default": default}


def word(c, g):
    return f"{g}x{g}" if c is SMALL else c.name


def part(kind, c, g):
    w = g * c.cell
    pivot = kind == "pivot"
    h = w if pivot or c is SMALL and g == 1 else w * 0.5  # (a 1x1 is square: a small part welds to its side)
    y = h / 2
    lip = 0.008 * c.x
    head = "Free Pivot" if pivot else "Free Swivel"
    p = Part(f"{head} {word(c, g)}", c, "Free rotation", {
        "swivel": "Turns freely about its leaf, no motor. Lock signal holds it. Limits in its settings.",
        "pivot": "Swings freely across its leaf, no motor. Lock signal holds it. Limits in its settings.",
    }[kind], role="joint", tool="wrench", color=BASE, group="Joints")
    p.add(box((-w / 2, -y, -w / 2), (w / 2, y, w / 2), BASE, side="base"))
    # The moving side: a plate over the top and a bar across it, for how far it has turned.
    p.add(box((-w * 0.42, y, -w * 0.42), (w * 0.42, y + lip * 3, w * 0.42), COLLAR, detail=True, side="leaf", paintable=False))
    p.add(box((-w * 0.06, y + lip * 3, -w * 0.4), (w * 0.06, y + lip * 4, w * 0.4), CUE, detail=True, side="leaf", paintable=False))
    if pivot:
        for sx in (-1, 1):  # the pin's ends, on the axis
            a, b = sx * (w / 2), sx * (w / 2 + lip * 2)
            p.add(box((min(a, b), -w * 0.12, -w * 0.12), (max(a, b), w * 0.12, w * 0.12), "#4a4f55", detail=True, side="base", paintable=False))  # (base side: not the moving side's colour)
    p.sock("base", (0, -y, 0), "-y")
    p.sock("top", (0, y, 0), "+y")
    p.sock("+z", (0, 0, w / 2), "+z")
    p.sock("-z", (0, 0, -w / 2), "-z")
    p.sock("+x", (w / 2, 0, 0), "+x")
    p.sock("-x", (-w / 2, 0, 0), "-x")
    if c is SMALL:
        k = g * g * 2
        p.stats(1.5 * k * (1.3 if pivot else 1), 80 + 15 * k, 3 + g, 1.0 + 0.5 * g)
    else:
        area = 6 * w * w
        p.stats(area * c.kg_m2, area * c.health_m2, c.armour + 8, c.seconds * 1.5)
    p.set(power=0, joint={"type": "swivel", "axis": "+x" if pivot else "+y", "pivot": [0.0, 0.0, 0.0] if pivot else [0.0, y, 0.0], "leaf": "top"}, config=[
        {"key": "limited", "type": "toggle", "label": "Use limits", "group": "Limits", "default": False, "tooltip": "Off: it turns forever."},
        num("min", "Back to", -180, 180, 1, "How far it turns one way, with limits on.", -90),
        num("max", "Forward to", -180, 180, 1, "How far it turns the other way, with limits on.", 90),
    ], centreSockets=False)
    return p


def everything():
    return [(kind.capitalize(), part(kind, c, g)) for kind in ("swivel", "pivot") for c, g in SIZES]


if __name__ == "__main__":
    sys.exit(write_parts(OUT, everything()))
