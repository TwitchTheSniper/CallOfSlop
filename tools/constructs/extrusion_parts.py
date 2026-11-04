#!/usr/bin/env python3
"""Writes the Extrusion parts (Constructs/Parts/Extrusion) and the Extruder Line blueprint.

docs/sandbox/CONSTRUCTS.md, "Extrusion" and "The Wheel Former". An Extruder pushes bulk material through a die; a Wheel
Former builds a wheel up on its hub, wider or bigger. Each comes in the four size classes: the small Extruder widens its
die with Die Frames, the bigger ones are their die. The sizes are part data (storage.extrude, storage.wheel), so a new
line of machines is a row in EXTRUDERS or FORMERS.

Run it from the repo root: python3 -I tools/constructs/extrusion_parts.py (it rewrites the folder; --check only compares).
Then python3 -I tools/constructs/assembling.py, for the recipes. Fix a part here, never in its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, block_sockets, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Extrusion")
BLUEPRINT = os.path.join("Constructs", "Blueprints", "ExtruderLine.json")
SECTION = "Extrusion"
BODY, TRIM, DARK, GOLD = "#55606b", "#3f4750", "#23272c", "#c8a13a"

PROFILES = ["Solid bar", "Plate", "Wall", "Hollow box", "I-beam", "C-channel", "L-angle", "T-bar", "Cross", "Custom", "Round", "Pipe"]

# (class, die across, longest piece, m/s, N of push, bin units, W, kg, health, cells x y z)
EXTRUDERS = [
    (MEDIUM, 8, 64, 0.4, 24000, 2000, 3600, 16000, 6000, (9, 9, 6)),
    (LARGE, 16, 128, 0.8, 96000, 8000, 14400, 180000, 40000, (5, 5, 3)),
    (CAPITAL, 32, 192, 1.6, 384000, 32000, 57600, 1600000, 240000, (3, 3, 2)),
]

# (class, widest, biggest across, first width, first across, m/s, N of push, bin units, W, kg, health, cells x y z, the hub's row of cells)
FORMERS = [
    (SMALL, 1.5, 3, 0.1, 0.4, 0.1, 6000, 400, 900, 520, 700, None, None),
    (MEDIUM, 6, 12, 0.5, 2, 0.2, 24000, 2000, 3600, 18000, 6000, (5, 14, 4), 6),
    (LARGE, 12, 24, 1, 4, 0.4, 96000, 8000, 14400, 200000, 40000, (3, 6, 3), 3),
    (CAPITAL, 20, 48, 2, 8, 0.8, 384000, 32000, 57600, 1800000, 240000, (3, 3, 2), 1),
]


def name_of(c, base):
    return f"{c.name} {base}" if c.name else base


def extruder_config(die, section, length):
    """An Extruder's settings. (A new one goes on the end: a saved setting is found by its key.)"""
    return [
        {"key": "profile", "label": "Profile", "group": "Shape", "options": PROFILES, "default": 0,
         "tooltip": "The cross-section. Custom is the cell grid below; clicking a cell starts one from what is shown. Round and Pipe take their diameter from Width."},
        {"key": "width", "label": "Width", "group": "Shape", "unit": "m", "min": 0.1, "max": section, "step": 0.05, "default": 0.25,
         "tooltip": "Across the section (a Round's or a Pipe's diameter). The die must be at least this wide" + (": weld on Die Frames." if die < section else ".")},
        {"key": "height", "label": "Height", "group": "Shape", "unit": "m", "min": 0.1, "max": section, "step": 0.05, "default": 0.25,
         "tooltip": "Up the section. The die must be at least this tall" + (": weld on Die Frames." if die < section else ".")},
        {"key": "maskA", "label": "Cells", "group": "Shape", "tooltip": "The section as an 8 by 8 grid: click cells on or off."},
        {"key": "maskB", "label": "Cells", "group": "Shape"},
        {"key": "maskC", "label": "Cells", "group": "Shape"},
        {"key": "maskD", "label": "Cells", "group": "Shape"},
        {"key": "wall", "label": "Pipe wall", "group": "Shape", "unit": "m", "min": 0.01, "max": 2, "step": 0.01, "default": 0.03,
         "tooltip": "A Pipe's wall thickness. It leaves a bore of at least 5 cm."},
        {"key": "cutLength", "label": "Cut every", "group": "Cutting", "unit": "m", "min": 0, "max": length, "step": 0.05, "default": 0,
         "tooltip": "Cuts the piece off at the die by itself when it is this long. 0: only when a signal comes into Cut."},
    ]


def former_config():
    return [
        {"key": "runMode", "label": "Run mode", "group": "Run", "options": ["Width", "Depth"], "default": 0,
         "tooltip": "What it builds up: Width along the axle, Depth (the diameter) out from the hub. A wire into Mode decides instead: on is Depth."},
    ]


def extruder_small():
    p = Part("Extruder", SMALL, SECTION, "Pushes bulk material through a die as one long piece. Cut it with a signal or by length. Needs power.", role="storage", group="Production", color=BODY)
    p.add(box((-0.5, 0, -0.75), (0.5, 0.85, 0.75)))
    p.add(box((-0.5, 0.85, -0.05), (0.5, 1, 0.75)))
    p.add(box((-0.5, 0.85, -0.75), (-0.46, 1, -0.05), TRIM))
    p.add(box((0.46, 0.85, -0.75), (0.5, 1, -0.05), TRIM))
    p.add(box((-0.46, 0.85, -0.75), (0.46, 1, -0.71), TRIM))
    p.add(box((-0.27, 0.23, 0.74), (0.27, 0.77, 0.76), DARK, detail=True))
    p.add(box((-0.51, 0.1, -0.5), (-0.49, 0.7, 0.5), GOLD, detail=True))
    p.add(box((0.49, 0.1, -0.5), (0.51, 0.7, 0.5), GOLD, detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("+x", (0.5, 0.5, 0.35), "+x")
    p.sock("-x", (-0.5, 0.5, 0.35), "-x")
    p.sock("top", (0, 1, 0.35), "+y")
    p.sock("back", (0, 0.5, -0.75), "-z")
    p.sock("die", (0, 0.5, 0.75), "+z", kind="die")
    p.stats(380, 600, 6, 4)
    p.set(power=900, storage={
        "bulk": {"material": "any", "capacity": 400, "min": [-0.455, 0.855, -0.705], "max": [0.455, 0.995, -0.055]},
        "extrude": {"speed": 0.2, "die": [0.5, 0.5], "at": [0, 0.5, 0.75], "maxSection": [6, 6], "maxLength": 48, "pushForce": 6000},
    }, config=extruder_config(0.5, 6, 48))
    return p


def head(p, cells, at_y, hopper_cells=1):
    """A big machine's block: origin the centre of cell 0, 0, 0, a hopper let into the back of its top. Returns x0, x1, y1, z0, z1."""
    c = p.cls
    nx, ny, nz = cells
    x0, x1, y0, y1, z0, z1 = c.lo(), c.hi(nx), c.lo(), c.hi(ny), c.lo(), c.hi(nz)
    t = c.cell * 0.1
    z = z0 + hopper_cells * c.cell
    y = y1 - c.cell * 0.5
    p.add(box((x0, y0, z0), (x1, y, z1)))
    p.add(box((x0, y, z), (x1, y1, z1)))
    p.add(box((x0, y, z0), (x0 + t, y1, z), TRIM))
    p.add(box((x1 - t, y, z0), (x1, y1, z), TRIM))
    p.add(box((x0 + t, y, z0), (x1 - t, y1, z0 + t), TRIM))
    p.add(box((x0 - t * 0.2, y0 + t, z0 + t), (x0, y - t, z1 - t), GOLD, detail=True))
    p.add(box((x1, y0 + t, z0 + t), (x1 + t * 0.2, y - t, z1 - t), GOLD, detail=True))
    return x0, x1, y, y1, z0, z, z1, t


def bin_of(units, x0, x1, y, y1, z0, z, t):
    m = 0.05 * t + t
    return {"material": "any", "capacity": units, "min": [round(x0 + m, 3), round(y + 0.05 * t, 3), round(z0 + m, 3)], "max": [round(x1 - m, 3), round(y1 - 0.05 * t, 3), round(z - 0.05 * t, 3)]}


def extruder(row):
    c, die, length, speed, push, units, power, mass, health, cells = row
    p = Part(name_of(c, "Extruder"), c, SECTION, f"Pushes bulk material through a {die} x {die} m die as one long piece, up to {length} m. Cut it with a signal or by length. Needs power.", role="storage", group="Production", color=BODY)
    nx, ny, nz = cells
    x0, x1, y, y1, z0, z, z1, t = head(p, cells, 0)
    cx, cy = (nx - 1) / 2 * c.cell, (ny - 1) / 2 * c.cell
    p.add(box((cx - die / 2, cy - die / 2, z1 - t * 0.1), (cx + die / 2, cy + die / 2, z1 + t * 0.1), DARK, detail=True))
    block_sockets(p, nx, ny, nz, limit={d: (3, 3) for d in ("+x", "-x", "+y", "-y", "-z")}, skip=("+z",))
    p.sock("die", (cx, cy, z1), "+z", kind="die")
    p.stats(mass, health, c.armour)
    p.set(power=power, storage={
        "bulk": bin_of(units, x0, x1, y, y1, z0, z, t),
        "extrude": {"speed": speed, "die": [die, die], "at": [cx, cy, z1], "maxSection": [die, die], "maxLength": length, "pushForce": push},
    }, config=extruder_config(die, die, length))
    return p


def former(row):
    c, width, across, w0, d0, speed, push, units, power, mass, health, cells, hub_row = row
    desc = f"Builds a wheel up on its hub: wider, or bigger across, up to {width:g} m wide and {across:g} m across. A Cut signal parts it off. Needs power."
    p = Part(name_of(c, "Wheel Former"), c, SECTION, desc, role="storage", group="Production", color=BODY)
    if c is SMALL:
        p.add(box((-0.6, 0, -0.75), (0.6, 2.05, 0.75)))
        p.add(box((-0.6, 2.05, -0.05), (0.6, 2.2, 0.75)))
        p.add(box((-0.6, 2.05, -0.75), (-0.56, 2.2, -0.05), TRIM))
        p.add(box((0.56, 2.05, -0.75), (0.6, 2.2, -0.05), TRIM))
        p.add(box((-0.56, 2.05, -0.75), (0.56, 2.2, -0.71), TRIM))
        p.add(box((-0.61, 0.2, -0.5), (-0.59, 1.9, 0.5), GOLD, detail=True))
        p.add(box((0.59, 0.2, -0.5), (0.61, 1.9, 0.5), GOLD, detail=True))
        hub = (0, 1.6, 0.75)
        bulk = {"material": "any", "capacity": units, "min": [-0.555, 2.055, -0.705], "max": [0.555, 2.195, -0.055]}
        p.add(box((-0.2, 1.4, 0.74), (0.2, 1.8, 0.77), DARK, detail=True, shape="cylinder", segments=16))
        p.sock("bottom", (0, 0, 0), "-y")
        p.sock("+x", (0.6, 0.75, 0), "+x")
        p.sock("-x", (-0.6, 0.75, 0), "-x")
        p.sock("top", (0, 2.2, 0.35), "+y")
        p.sock("back", (0, 0.75, -0.75), "-z")
        p.sock("die", hub, "+z", kind="die")
        p.stats(mass, health, 6, 4)
    else:
        nx, ny, nz = cells
        x0, x1, y, y1, z0, z, z1, t = head(p, cells, 0)
        hub = ((nx - 1) / 2 * c.cell, hub_row * c.cell, z1)
        bulk = bin_of(units, x0, x1, y, y1, z0, z, t)
        r = c.cell * 0.4
        p.add(box((hub[0] - r, hub[1] - r, z1 - t * 0.1), (hub[0] + r, hub[1] + r, z1 + t * 0.2), DARK, detail=True, shape="cylinder", segments=16))
        block_sockets(p, nx, ny, nz, limit={d: (3, 3) for d in ("+x", "-x", "+y", "-y", "-z")}, skip=("+z",))
        p.sock("die", hub, "+z", kind="die")
        p.stats(mass, health, c.armour)
    p.set(power=power, storage={
        "bulk": bulk,
        "wheel": {"speed": speed, "at": list(hub), "maxWidth": width, "maxDiameter": across, "startWidth": w0, "startDiameter": d0, "pushForce": push},
    }, config=former_config())
    return p


def die_frame(name, half_x, half_y, desc, mass, health, armour, seconds, gold):
    p = Part(name, SMALL, SECTION, desc, group="Production", color="#4a535c")
    p.add(box((-half_x, -half_y, -0.4), (half_x, half_y, 0.4)))
    p.add(box((-half_x - 0.01, -gold, -0.2), (half_x + 0.01, gold, 0.2), GOLD, detail=True))
    p.sock("-x", (-half_x, 0, 0), "-x")
    p.sock("+x", (half_x, 0, 0), "+x")
    p.sock("-y", (0, -half_y, 0), "-y")
    p.sock("+y", (0, half_y, 0), "+y")
    p.stats(mass, health, armour, seconds)
    p.set(dieFrame=True, centreSockets=False)
    return p


def line():
    """The Extruder Line: a Timer wired to the Extruder's Cut, so a 2 m piece comes off every 10 s."""
    return {
        "format": "call-of-slop-construct-blueprint/1",
        "name": "Extruder Line",
        "description": "An Extruder with a bin, a battery and a timer that cuts: tip in material.",
        "steps": [
            {"part": "Foundation"},
            {"part": "Extruder", "on": 0, "socket": "top21", "by": "bottom"},
            {"part": "Materials Bin", "on": 0, "socket": "top01", "by": "bottom"},
            {"part": "Battery", "on": 0, "socket": "top02", "by": "-y"},
            {"part": "Timer", "on": 0, "socket": "top03", "by": "-y", "config": {"period": 10, "duty": 10}},
        ],
        "links": [{"type": "chute", "from": 2, "to": 1}, {"type": "data", "from": 4, "to": 1, "toPort": "Cut"}],
    }


def main():
    parts = [("", extruder_small())]
    parts += [("", extruder(r)) for r in EXTRUDERS]
    parts += [("", former(r)) for r in FORMERS]
    parts.append(("", die_frame("Die Frame", 0.25, 0.5, "Attach it to an Extruder to widen the die, up to 6 x 6 m.", 90, 300, 6, 3, 0.3)))
    parts.append(("", die_frame("Large Die Frame", 0.5, 1, "Attach it to an Extruder: 1 x 2 m more die.", 280, 700, 8, 5, 0.6)))
    code = write_parts(OUT, parts)
    text = json.dumps(line(), indent=2) + "\n"
    with open(BLUEPRINT, encoding="utf-8") as fh:
        same = fh.read() == text
    if "--check" in sys.argv:
        if not same:
            print(f"out of date: {BLUEPRINT}")
            return 1
        return code
    if not same:
        with open(BLUEPRINT, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
    return code


if __name__ == "__main__":
    sys.exit(main())
