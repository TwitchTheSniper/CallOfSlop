#!/usr/bin/env python3
"""Writes the lift parts: Constructs/Parts/Air and the demo airships. docs/sandbox/CONSTRUCTS.md, "Air and lift".

Lift cells are Tank Volumes whose box is a solid envelope, soft or armoured, in four sizes, and two rigid Vacuum Cells.
Burners heat the cell up their flame. Run it from the repo root: python3 -I tools/constructs/air_parts.py (--check only
compares), then python3 -I tools/constructs/assembling.py for their recipes. The JSON files are what the game loads: fix
this, not them.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from big_drive_parts import bands, block, faces, half, health  # noqa: E402
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Air")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")
SECTION = "Lift cells"
BURNERS = "Burners"
SOFT = "#c9c2ae"
ARMOURED = "#6b7178"
RIGID = "#8c9299"
BAND = "#4a4f55"
FLAME = "#d9682e"
# (cls, name prefix, cells a side of its block, the longest side of its box in metres)
SIZES = [(SMALL, "", 2, 40), (MEDIUM, "Medium ", 1, 80), (LARGE, "Large ", 1, 160), (CAPITAL, "Capital ", 1, 320)]
SOFT_SKIN = 0.25  # kg/m2
ARMOUR_SKIN = 1.6
VACUUM_SKIN = 0.9
SOFT_ARMOUR = 2
HARD_ARMOUR = 40  # stops anything up to this much damage a hit
# Burners: (cls, name, cells a side, mass kg, m3 of cell it keeps at full heat, l/s)
BURNER_SIZES = [
    (SMALL, "Balloon Burner", 2, 18, 8000, 0.5),
    (MEDIUM, "Medium Burner", 1, 240, 60000, 5),
    (LARGE, "Large Burner", 1, 2400, 800000, 50),
    (CAPITAL, "Capital Burner", 1, 24000, 12000000, 500),
]


def config(side, y):
    def setting(key, label, group, unit, lo, hi, step, default, tip):
        return {"key": key, "label": label, "group": group, "unit": unit, "min": lo, "max": hi, "step": step, "default": default, "tooltip": tip}
    return [
        setting("sizeX", "Width", "Box", "m", 0.2, side, 0.1, 6, "The cell's size across the block (its x)."),
        setting("sizeY", "Height", "Box", "m", 0.2, side, 0.1, 6, "The cell's size up from the block (its y)."),
        setting("sizeZ", "Length", "Box", "m", 0.2, side, 0.1, 6, "The cell's size along the block (its z)."),
        setting("atX", "Centre x", "Box", "m", -side, side, 0.05, 0, "Where the cell's centre is from the block (x)."),
        setting("atY", "Centre y", "Box", "m", -side, side, 0.05, y, "Where the cell's centre is from the block (y; above it by default)."),
        setting("atZ", "Centre z", "Box", "m", -side, side, 0.05, 0, "Where the cell's centre is from the block (z)."),
        {"key": "fluid", "label": "Gas", "group": "Contents", "creative": True, "tooltip": "What the cell holds. Empty, a pipe's gas or a burner's heat fills it."},
        {"key": "fill", "label": "Fill", "group": "Contents", "unit": "%", "min": 0, "max": 100, "default": 0, "creative": True, "tooltip": "How full, or for hot air how hot."},
        {"key": "hole", "hidden": True, "creative": True, "unit": "m2", "min": 0, "max": 10000, "default": 0},
    ]


def cell(c, prefix, n, side, kind):
    colour, skin, armour, what = {
        "soft": (SOFT, SOFT_SKIN, SOFT_ARMOUR, "A soft bag you size. Hydrogen, helium or a burner's hot air. Any hit makes a hole."),
        "armoured": (ARMOURED, ARMOUR_SKIN, HARD_ARMOUR, "A plated bag you size: small rounds stop at it, and it weighs six times a soft one."),
        "vacuum": (RIGID, VACUUM_SKIN, 8, "A rigid shell its pump empties on power. The best lift of all, and the first hole collapses it."),
    }[kind]
    name = {"soft": f"{prefix}Lift Cell", "armoured": f"Armoured {prefix}Lift Cell", "vacuum": f"{prefix}Vacuum Cell"}[kind]
    p = Part(name, c, SECTION, what, role="fluidtank", tool="wrench", color=colour, material="cloth" if kind == "soft" else "steel", group="Small" if c is SMALL else None)
    block(p, n, n, n, BAND)
    x = half(n, c)
    p.add(box((x, -0.25 * x, -0.25 * x), (x * 1.2, 0.25 * x, 0.25 * x), "#c8863a", detail=True))
    p.add(box((-x * 1.2, -0.25 * x, -0.25 * x), (-x, 0.25 * x, 0.25 * x), "#4f9f5a", detail=True))
    faces(p, n, n, n)
    mass = {SMALL.key: 20, MEDIUM.key: 120, LARGE.key: 900, CAPITAL.key: 7000}[c.key]
    p.stats(mass, health(mass / 1000) * (2 if kind == "armoured" else 1) + 300, armour, 2.0)
    env = {"skin": skin, "side": side, "patch": 0.5 * c.x}
    if kind == "vacuum":
        env["vacuum"] = True
        p.d["power"] = 40.0 * c.x * c.x
    p.set(fluidTank={"volume": True, "flow": 200 * c.x * c.x, "envelope": env}, config=config(side, round(3.0 + 2 * x, 2)), ports=[
        {"name": "Fluid out", "type": "pipe", "dir": "out", "at": [x, 0, 0], "face": "+x"},
        {"name": "Fluid in", "type": "pipe", "dir": "in", "at": [-x, 0, 0], "face": "-x"},
    ])
    return ("", p)


def burner(c, name, n, mass, heat, rate):
    p = Part(name, c, BURNERS, f"Keeps a cell of {heat:,} m3 at full heat, up its flame, on {rate:g} l/s of fuel. Wire its throttle or a height.", role="burner", tool="wrench", color="#5c6066", group="Small" if c is SMALL else None)
    block(p, n, n, n)
    x = half(n, c)
    p.add(box((-0.5 * x, x, -0.5 * x), (0.5 * x, 1.5 * x, 0.5 * x), FLAME, detail=True))
    bands(p, n, n, n, BAND, 1)
    faces(p, n, n, n, skip=("+y",))
    p.stats(mass, health(mass / 1000), c.armour // 2, 2.0)
    p.set(burner={"heat": heat, "burnRate": rate, "at": [0, round(1.5 * x, 3), 0], "reach": 12 * c.x}, ports=[
        {"name": "Fuel in", "type": "pipe", "dir": "in", "fluid": "fuel", "at": [-x, 0, 0], "face": "-x"},
    ])
    return ("", p)


def compressor():
    """Between a bin of bulk gas and the pipes: a bin whose hydrogen or helium leaves by its pipe as gas."""
    p = Part("Gas Compressor", SMALL, "Gas", "Takes hydrogen or helium by chute and sends it down its pipe as gas: a unit is 50 l, 5 m3 of cell.", role="storage", tool="wrench", color="#5d6b4a", group="Small")
    p.add(box((-0.4, 0, -0.4), (0.4, 0.8, 0.4)))
    p.add(box((0.4, 0.3, -0.08), (0.46, 0.46, 0.08), "#c8863a", detail=True))
    p.add(box((-0.25, 0.8, -0.25), (0.25, 0.86, 0.25), BAND, detail=True))
    for d, at in (("-y", (0, 0, 0)), ("+x", (0.4, 0.4, 0)), ("-x", (-0.4, 0.4, 0)), ("+z", (0, 0.4, 0.4)), ("-z", (0, 0.4, -0.4))):
        p.sock(d, at, d)
    p.stats(60, 300, 5, 2.0)
    p.set(storage={"bulk": {"material": "any", "capacity": 200, "min": [-0.3, 0.05, -0.3], "max": [0.3, 0.7, 0.3]}, "gasLitres": 50},
          fluidOut=None, ports=[{"name": "Gas out", "type": "pipe", "dir": "out", "at": [0.4, 0.38, 0], "face": "+x"}])
    p.d.pop("fluidOut")
    return ("", p)


def parts():
    out = [compressor()]
    for c, prefix, n, side in SIZES:
        out.append(cell(c, prefix, n, side, "soft"))
        out.append(cell(c, prefix, n, side, "armoured"))
        if c in (SMALL, MEDIUM):
            out.append(cell(c, prefix, n, side, "vacuum"))
    out += [burner(*b) for b in BURNER_SIZES]
    return out


MAST = "-y7"


def airship(name, about, cell, size, gas=None, fill=0, burner_part=None, jets="Light Jet", masts=1, ballast=None):
    """The demo: a tank with a seat on it, a cell's block on its side and the box above, a jet each side behind."""
    x, y, z = size
    off = 0.7 if ballast else 0  # (the ballast hangs from its corner: the cell's lift goes over it)
    cfg = {"sizeX": x, "sizeY": y, "sizeZ": z, "atX": 0.75 + off, "atY": round(y / 2 + 3, 2), "atZ": off}
    if gas:
        cfg.update(fluid=gas, fill=fill)
    steps = [
        {"part": "Fuel Tank"},
        {"part": "Seat", "on": 0, "socket": "+y", "by": "bottom"},
        {"part": cell, "on": 0, "socket": "-x", "by": "+x", "config": cfg},
        {"part": "Fuel Tank", "on": 0, "socket": "+z", "by": "-z"},
        {"part": "Fuel Tank", "on": 3, "socket": "-x", "by": "+x"},
        {"part": "Fuel Tank", "on": 3, "socket": "+x", "by": "-x"},
        # Masts: the push acts level with the centre of mass, which the cell's skin raises, or it would tip her.
        {"part": "Square Beam 2 m", "on": 4, "socket": "+y", "by": "-z"},
        {"part": "Square Beam 2 m", "on": 5, "socket": "+y", "by": "-z"},
    ]
    for k in range(1, masts):
        steps.append({"part": "Square Beam 2 m", "on": 4 + 2 * k, "socket": "+z", "by": "-z"})
        steps.append({"part": "Square Beam 2 m", "on": 5 + 2 * k, "socket": "+z", "by": "-z"})
    first = len(steps)
    steps.append({"part": jets, "on": 4 + 2 * masts, "socket": MAST, "by": "front"})
    steps.append({"part": jets, "on": 5 + 2 * masts, "socket": MAST, "by": "front"})
    if burner_part:
        steps.append({"part": burner_part, "on": 0, "socket": "+x", "by": "-x"})
    if ballast:
        steps.append({"part": ballast, "on": 0, "socket": "-y", "by": "t0_0"})
    links = [{"type": "pipe", "from": 0, "to": first + k, "fromPort": "Fuel out", "toPort": "Fuel in"} for k in (0, 1)]
    return {"format": "call-of-slop-construct-blueprint/1", "name": name, "description": about, "steps": steps, "links": links}


BLUEPRINT_LIST = [
    ("HotAirBalloon", airship("Hot Air Balloon", "A soft cell over a Balloon Burner. Shift raises the burner, Ctrl lowers it and then vents; wire its throttle or a height instead. Two jets push it.",
                              "Lift Cell", (16, 18, 16), burner_part="Balloon Burner")),
    ("ScoutAirship", airship("Scout Airship", "A soft helium cell and two jets. Any hit holes it: patch it with the attach tool. Creative comes filled; survival pipes gas in.",
                             "Lift Cell", (10, 12, 24), "helium", 90, ballast="Concrete Floor 2x2 m")),
    ("ArmouredAirship", airship("Armoured Airship", "A plated helium cell that stops small rounds, at six times the skin's weight. Creative comes filled; survival pipes gas in.",
                                "Armoured Lift Cell", (16, 16, 40), "helium", 80, masts=3, ballast="Concrete Floor 2x2 m")),
]


def blueprints():
    """Writes the demo airships (--check only compares). Returns the exit code."""
    code = 0
    for file, b in BLUEPRINT_LIST:
        path = os.path.join(BLUEPRINTS, file + ".json")
        text = json.dumps(b, indent=1) + "\n"
        if "--check" in sys.argv:
            with open(path, encoding="utf-8") as h:
                code |= h.read() != text
        else:
            with open(path, "w", encoding="utf-8", newline="\n") as h:
                h.write(text)
    return code


def main():
    return write_parts(OUT, parts()) | blueprints()


if __name__ == "__main__":
    sys.exit(main())
