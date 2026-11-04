#!/usr/bin/env python3
"""Writes the suspension families (Constructs/Parts/Suspensions): docs/sandbox/CONSTRUCTS.md, "Suspension types".

Five kinds, each in a ladder of sizes from a kart to a capital body: the plain bracket (the ladder's missing sizes), the Pogo
Strut (a telescoping strut hung from its top end, the wheel straight under it), the Swing Arm (hinged on the chassis side, the
wheel leans with it), the Double Wishbone (two arms, the wheel stays upright) and the Trailing Arm (hinged ahead of the wheel;
roll it 180 degrees on its socket to lead instead, or for the other side of the car). Sizes go by wheel: the description says
which wheels a size takes and what it carries a wheel. The tables at the top are the balance.

Boxes that move carry a "rig" (hub, arm, leg) and the suspension a "path": the game draws them from where its wheel is. Every
part is a hidden solid (what collides, what the sockets sit on, at rest) with the moving boxes as details over it.

Run it from the repo root: python3 -I tools/constructs/suspension_parts.py (--check only compares). Data first: fix a family
here, never in its output. It also writes the demo buggies from the Buggy blueprint.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, file_name, write_parts  # noqa: E402
from big_drive_parts import RIDE, SPRINGS, drive_in, health, mount  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Suspensions")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")

# The Buggy's steps that mount a left wheel.
LEFT_STEPS = (6, 10)

# Where these parts sit in the part menu: the one place to change it.
MENU = {"group": "Drivetrain", "section": "Suspension"}


def tonnes(kg):
    return f"{kg:g} kg" if kg < 1000 else f"{kg / 1000:g} t"


# name, class, wheels it takes, kg a wheel, travel m, Hz, damping, slew m/s, kg, strut cells, arm cells
SIZES = [
    ("Kart", SMALL, "0.2-0.3 m", 150, 0.12, 3.2, 0.75, 0, 5, 3, 2),
    ("Light", SMALL, "0.3-0.45 m", 400, 0.2, 2.6, 0.75, 0, 9, 4, 3),
    ("", SMALL, "0.4-0.55 m", 1000, 0.3, 2.2, 0.75, 0, 16, 5, 3),
    ("Heavy", SMALL, "0.55-0.8 m", 2500, 0.45, 1.8, 0.8, 0, 35, 6, 4),
    ("Truck", SMALL, "1.2 m", 8000, 0.8, 1.4, 0.8, 0, 130, 8, 6),
    ("Medium", MEDIUM, "1.5 m", SPRINGS[MEDIUM][2], SPRINGS[MEDIUM][0], SPRINGS[MEDIUM][1], 0.8, SPRINGS[MEDIUM][3], 1500, 3, 3),
    ("Large", LARGE, "6 m", SPRINGS[LARGE][2], SPRINGS[LARGE][0], SPRINGS[LARGE][1], 0.8, SPRINGS[LARGE][3], 90000, 3, 3),
    ("Capital", CAPITAL, "24 m", SPRINGS[CAPITAL][2], SPRINGS[CAPITAL][0], SPRINGS[CAPITAL][1], 0.8, SPRINGS[CAPITAL][3], 900000, 3, 3),
]
# Each kind's words and how much it weighs against the plain bracket.
KINDS = {
    "Pogo Strut": ("Drops straight down", 1.1, "#3f5f7a"),
    "Swing Arm": ("Arm swings, wheel leans", 1.3, "#7a4f33"),
    "Double Wishbone": ("Two arms, wheel stays upright", 1.6, "#55704a"),
    "Trailing Arm": ("Arm runs back to the wheel", 1.2, "#6a5f7c"),
}
STEEL = "#8f959c"  # rods, shafts
DARK = "#34383d"  # what they bolt to
COIL = "#d0b050"  # springs, as on the plain suspension
UNDER_ARM = -0.66  # an arm's Drive in, in half cells below the hub's middle: the half shaft runs under the arm and can pass under the mount


def config():
    return [
        {"key": "stiffness", "label": "Stiffness", "group": "Spring", "unit": "Hz", "min": 0.5, "max": 6, "step": 0.05, "tooltip": "The spring's bounce frequency: higher is stiffer."},
        {"key": "damping", "label": "Damping", "group": "Spring", "min": 0, "max": 2, "step": 0.01, "tooltip": "How quickly bouncing dies away (1: no overshoot)."},
    ]


def spring(load, travel, hz, damp, slew, path=None):
    """A gas strut: it rides half way down its travel whatever it carries, so its arm sits level (up to its load)."""
    s = {"travel": travel, "frequency": hz, "damping": damp, "maxLoad": load, "ride": RIDE}
    if slew:
        s["slew"] = slew
    if path:
        s["path"] = path
    return s


def name_of(size, kind):
    return f"{size} {kind}" if size else kind


def start(size, kind):
    word, cls, wheels, load, travel, hz, damp, slew, kg, strut, arm = size
    verb, m, color = KINDS[kind]
    p = Part(name_of(word, kind), cls, MENU["section"], f"{verb}. {wheels} wheels, {tonnes(load)}.", role="suspension", tool="wrench", color=color, group=MENU["group"])
    mass = kg * m
    p.stats(mass, max(60, health(mass / 1000) * (0.5 if cls is SMALL else 1)), 3 if cls is SMALL else cls.armour, 1.5 if cls is SMALL else None)
    p.set(config=config())
    return p, cls, cls.half, cls.cell, travel


def hub_cell(p, h, x, z):
    """The cell the wheel bolts to: hidden and solid (the knuckle over it moves)."""
    p.add(box((x - h, -h, z - h), (x + h, h, z + h), hidden=True))


def port_stub(p, h, x0, x1, y, z):
    """Where a half shaft goes in: a stub on the hub, moving with it, from the hidden cell's face (the port) to the hub drawn."""
    r = 0.3 * h
    b = box((x0, y - r, z - r), (x1, y + r, z + r), STEEL, detail=True, shape="cylinder", front="+x")
    b["rig"] = "hub"
    p.add(b)


def leg(p, h, x, y0, y1, pivot):
    """A damper standing on the arm: a rod with a sleeve round its top half."""
    for r, color, span in ((0.12, STEEL, [0, 1]), (0.26, COIL, [0, 0.6])):
        a = r * h
        b = box((x - a, y1, -a), (x + a, y0, a), color, detail=True, shape="cylinder", front="+y")
        b.update({"rig": "leg", "from": [x, y0, 0], "to": [x, y1, 0], "span": span, "pivot": pivot})
        p.add(b)


def pogo(size):
    p, c, h, cell, travel = start(size, "Pogo Strut")
    word, _, _, load, _, hz, damp, slew, _, n, _ = size
    top = c.hi(n)
    cap = top - cell  # the cap's underside
    knuckle = 0.7 * h
    tube = knuckle + travel / 2 + 0.05 * h  # (the tube's bottom clears the knuckle at the top of its travel)
    assert cap - tube > 0.04, word
    rod = min(cap, tube + travel / 2 + 0.2 * h)
    hub_cell(p, h, 0, 0)
    p.add(box((-0.2 * h, h, -0.2 * h), (0.2 * h, cap, 0.2 * h), hidden=True))
    p.add(box((-h, cap, -0.8 * h), (h, top, 0.8 * h), DARK))
    p.add(box((-0.42 * h, tube, -0.42 * h), (0.42 * h, cap + 0.02 * h, 0.42 * h), None, detail=True, shape="cylinder", front="+y"))
    b = box((-0.2 * h, 0, -0.2 * h), (0.2 * h, rod, 0.2 * h), STEEL, detail=True, shape="cylinder", front="+y")
    b["rig"] = "hub"
    p.add(b)
    b = box((-0.65 * h, -knuckle, -0.6 * h), (h, knuckle, 0.6 * h), DARK, detail=True)
    b["rig"] = "hub"
    p.add(b)
    # The coil: rings that close up as the strut does.
    rings = 4
    for k in range(rings):
        s = 0.08 + 0.84 * k / rings
        a = 0.66 * h
        ring = box((-a, -0.1 * h, -a), (a, 0.1 * h, a), COIL, detail=True, shape="cylinder", front="+y")
        ring.update({"rig": "leg", "from": [0, cap, 0], "to": [0, knuckle, 0], "span": [round(s, 3), round(s + 0.07, 3)]})
        p.add(ring)
    p.sock("mount", (0, top, 0), "+y")
    p.sock("side", (-h, top - cell / 2, 0), "-x")
    p.sock("inner", (-h, 0, 0), "-x")  # (onto a differential's end or a steering pivot's leaf)
    p.sock("axle", (h, 0, 0), "+x", kind="axle")
    port_stub(p, h, -h, -0.6 * h, 0, 0)
    drive_in(p, (-h, 0, 0))
    p.set(suspension=spring(load, travel, hz, damp, slew, {"kind": "line"}))
    return p


def swing(size):
    p, c, h, cell, travel = start(size, "Swing Arm")
    word, _, _, load, _, hz, damp, slew, _, _, n = size
    x = (n - 1) * cell
    high = cell
    top = c.hi(2)
    at = 0.65 * x
    hub_cell(p, h, x, 0)
    p.add(box((0.4 * h, -0.3 * h, -0.3 * h), (x - h + 0.01 * h, 0.3 * h, 0.3 * h), hidden=True))
    p.add(box((-h, -h, -h), (0.4 * h, top, h), DARK))
    arm = box((0, -0.28 * h, -0.4 * h), (x, 0.28 * h, 0.4 * h), None, detail=True)
    arm.update({"rig": "arm", "pivot": [0, 0, 0]})
    p.add(arm)
    b = box((x - 0.55 * h, -0.7 * h, -0.6 * h), (x + h, 0.7 * h, 0.6 * h), DARK, detail=True)
    b["rig"] = "hub"
    p.add(b)
    p.add(box((0.4 * h, high - 0.3 * h, -0.3 * h), (at + 0.32 * h, high + 0.3 * h, 0.3 * h), DARK, detail=True))
    leg(p, h, at, high - 0.3 * h, 0.25 * h, [0, 0, 0])
    p.sock("mount", (-h, 0, 0), "-x")
    p.sock("mount-high", (-h, cell, 0), "-x")
    p.sock("top", (0, top, 0), "+y")
    p.sock("axle", (x + h, 0, 0), "+x", kind="axle")
    port_stub(p, h, x - h, x - 0.5 * h, UNDER_ARM * h, 0)
    drive_in(p, (x - h, UNDER_ARM * h, 0))
    p.set(suspension=spring(load, travel, hz, damp, slew, {"kind": "arc", "pivot": [0, 0, 0], "axis": "+z", "tilt": 1}))
    return p


def wishbone(size):
    p, c, h, cell, travel = start(size, "Double Wishbone")
    word, _, _, load, _, hz, damp, slew, _, _, n = size
    x = (n - 1) * cell
    top = c.hi(3)
    high = 2 * cell
    at = 0.65 * x
    hub_cell(p, h, x, 0)
    p.add(box((0.4 * h, -0.2 * h, -0.2 * h), (x - h + 0.01 * h, 0.2 * h, 0.2 * h), hidden=True))
    p.add(box((-h, -h, -h), (0.4 * h, top, h), DARK))
    for y in (0, cell):
        for z in (-0.6 * h, 0.6 * h):
            tine = box((0, y - 0.16 * h, z - 0.13 * h), (x, y + 0.16 * h, z + 0.13 * h), None, detail=True)
            tine.update({"rig": "arm", "pivot": [0, y, 0]})
            p.add(tine)
    b = box((x - 0.3 * h, -0.7 * h, -0.55 * h), (x + 0.3 * h, cell + 0.4 * h, 0.55 * h), DARK, detail=True)
    b["rig"] = "hub"
    p.add(b)
    b = box((x + 0.28 * h, -0.5 * h, -0.45 * h), (x + h, 0.5 * h, 0.45 * h), STEEL, detail=True)
    b["rig"] = "hub"
    p.add(b)
    p.add(box((0.4 * h, high - 0.3 * h, -0.3 * h), (at + 0.32 * h, high + 0.3 * h, 0.3 * h), DARK, detail=True))
    leg(p, h, at, high - 0.3 * h, 0.2 * h, [0, 0, 0])
    p.sock("mount", (-h, 0, 0), "-x")
    p.sock("mount-mid", (-h, cell, 0), "-x")
    p.sock("mount-high", (-h, 2 * cell, 0), "-x")
    p.sock("top", (0, top, 0), "+y")
    p.sock("axle", (x + h, 0, 0), "+x", kind="axle")
    port_stub(p, h, x - h, x - 0.25 * h, UNDER_ARM * h, 0)
    drive_in(p, (x - h, UNDER_ARM * h, 0))
    p.set(suspension=spring(load, travel, hz, damp, slew, {"kind": "arc", "pivot": [0, 0, 0], "axis": "+z", "length": x, "tilt": 0}))
    return p


def trailing(size):
    """Mirrored top to bottom, so 180 degrees about its socket (either side of the car) gives the same arm the other way along."""
    p, c, h, cell, travel = start(size, "Trailing Arm")
    word, _, _, load, _, hz, damp, slew, _, _, n = size
    z = (n - 1) * cell
    hub_cell(p, h, 0, z)
    p.add(box((-0.3 * h, -0.3 * h, 0.8 * h), (0.3 * h, 0.3 * h, z - h + 0.01 * h), hidden=True))
    p.add(box((-h, -h, -h), (-0.1 * h, h, 0.8 * h), DARK))
    arm = box((-0.1 * h, -0.42 * h, 0), (0.42 * h, 0.42 * h, z), None, detail=True)
    arm.update({"rig": "arm", "pivot": [0, 0, 0]})
    p.add(arm)
    boss = box((-0.12 * h, -0.65 * h, -0.65 * h), (0.5 * h, 0.65 * h, 0.65 * h), STEEL, detail=True, shape="cylinder", front="+x")
    boss.update({"rig": "arm", "pivot": [0, 0, 0]})
    p.add(boss)
    b = box((-0.3 * h, -0.65 * h, z - 0.65 * h), (h, 0.65 * h, z + 0.65 * h), DARK, detail=True)
    b["rig"] = "hub"
    p.add(b)
    p.sock("mount", (-h, 0, 0), "-x")
    p.sock("top", (-0.5 * h, h, 0), "+y")
    p.sock("bottom", (-0.5 * h, -h, 0), "-y")
    p.sock("axle", (h, 0, z), "+x", kind="axle")
    port_stub(p, h, -h, -0.25 * h, 0, z)
    drive_in(p, (-h, 0, z))
    p.set(suspension=spring(load, travel, hz, damp, slew, {"kind": "arc", "pivot": [0, 0, 0], "axis": "+x", "tilt": 0}))
    return p


def plain(size):
    """The plain bracket's missing sizes (Suspension and the Medium, Large and Capital ones are older parts, kept as they are)."""
    word, cls, wheels, load, travel, hz, damp, slew, kg, _, _ = size
    s = {"travel": travel, "frequency": hz, "damping": damp, "maxLoad": load}
    p = mount(cls, f"{word} Suspension", MENU["section"], f"Sprung bracket. {wheels} wheels, {tonnes(load)}.", "#b08a2e", kg / 1000, s)
    p.d["group"] = MENU["group"]
    p.stats(kg, max(60, health(kg / 1000) * 0.5), 3, 1.5)
    p.set(config=config())
    drive_in(p, (-cls.half, 0, 0))
    return p


def everything():
    out = [("PogoStruts", pogo(s)) for s in SIZES]
    out += [("SwingArms", swing(s)) for s in SIZES]
    out += [("DoubleWishbones", wishbone(s)) for s in SIZES]
    out += [("TrailingArms", trailing(s)) for s in SIZES]
    out += [("Plain", plain(s)) for s in SIZES if s[0] in ("Kart", "Light", "Heavy", "Truck")]
    return out


# The demo buggies: the Buggy blueprint on each kind. name, part, the socket it bolts on by, a left one's spin.
DEMOS = [
    ("Pogo Buggy", "Pogo Strut", "inner", 0),
    ("Swing Arm Buggy", "Swing Arm", "mount", 0),
    ("Wishbone Buggy", "Double Wishbone", "mount", 0),
    ("Trailing Arm Buggy", "Trailing Arm", "mount", 3),  # (rolled over, so its hub trails on the left too)
]


def demos():
    """{path: text} of the demo buggies."""
    with open(os.path.join(BLUEPRINTS, "Buggy.json"), encoding="utf-8") as fh:
        base = json.load(fh)
    out = {}
    for name, part, by, left in DEMOS:
        d = json.loads(json.dumps(base))
        d["name"] = name
        d["description"] = f"The Buggy on {part}s."
        steps = d["steps"]
        for i, s in enumerate(steps):
            if s["part"] != "Suspension":
                continue
            s["part"] = part
            s["by"] = by
            if left and i in LEFT_STEPS:
                s["spin"] = left
        out[os.path.join(BLUEPRINTS, file_name(name))] = json.dumps(d, indent=2) + "\n"
    return out


def main():
    code = write_parts(OUT, everything())
    for path, text in demos().items():
        have = None
        if os.path.exists(path):
            with open(path, encoding="utf-8") as fh:
                have = fh.read()
        if have == text:
            continue
        if "--check" in sys.argv:
            print(f"out of date: {path}")
            code = 1
        else:
            with open(path, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)
    return code


if __name__ == "__main__":
    sys.exit(main())
