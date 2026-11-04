#!/usr/bin/env python3
"""Writes the Press family: Constructs/Parts/Press (presses, Die Lathes, dies, Die Racks, housings), Constructs/Pressing.json and the Press Line blueprint.

docs/sandbox/CONSTRUCTS.md, "The Press". A press stamps parts out of extruded pieces. It has 2 or 3 bays and a die slot; the
die sets the family, the material the tier, a piece's size across or its length the size or the count. Presses, lathes and dies
come in the four size classes, and a die fits a press of its own class. All sizes are in cells of the class, so a new class or
a new die family is a row here.

The table is flat on purpose: one row per family, material and size band, so the coverage of the Assembler's list can be counted.

Run it from the repo root: python3 -I tools/constructs/press_parts.py (it rewrites the files; --check only compares).
Then python3 -I tools/constructs/assembling.py, for the machines' recipes. Fix a part here, never in its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, block_sockets, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Press")
TABLE = os.path.join("Constructs", "Pressing.json")
BLUEPRINT = os.path.join("Constructs", "Blueprints", "PressLine.json")
FORMAT = "call-of-slop-pressing/1"
ABOUT = ("The stamping table (docs/sandbox/CONSTRUCTS.md, 'The Press'). Written by tools/constructs/press_parts.py: fix it, not this file. "
         "'rows' are a press's: with a die of family 'die', the first row whose bays hold what it asks runs. Each bay gives its 'sections' (Extrusion presets, round, pipe), 'materials', "
         "'minAcross' and 'maxAcross' (m, the piece's smaller side) and what a stroke takes: 'use' (m) or 'useAcross' (times its size across). 'lathe' rows are a Die Lathe's, chosen as a setting.")
SECTION = "Presses"
DIES = "Dies"
BODY, TRIM, DARK, GOLD, BED = "#4d5a66", "#39424b", "#23272c", "#c8a13a", "#2f363d"
SCREEN = "#10181c"

# (class, bays, speed, W, kg, health)
PRESSES = [
    (SMALL, 2, 1, 900, 520, 800),
    (MEDIUM, 3, 2, 3600, 18000, 6000),
    (LARGE, 3, 4, 14400, 200000, 40000),
    (CAPITAL, 3, 8, 57600, 1800000, 240000),
]
# Dies: family, classes it is made in, what it is cut from, die tier (1: a starter), colour
FAMILIES = [
    ("Button", (SMALL,), "metal", 1, "#c06040"),
    ("Bracket", (SMALL,), "metal", 1, "#7f8a94"),
    ("Gear", (SMALL,), "metal", 1, "#a8763a"),
    ("Board", (SMALL,), "steel", 2, "#3f9b5c"),
    ("Piston", (SMALL,), "steel", 2, "#6f7d8c"),
    ("Housing", (SMALL, MEDIUM, LARGE, CAPITAL), "steel", 2, "#55708a"),
    ("Cylinder", (SMALL, MEDIUM, LARGE, CAPITAL), "steel", 3, "#8a5570"),
]
MAX_ACROSS = {SMALL: 0.45, MEDIUM: 1.8, LARGE: 7.2, CAPITAL: 28.8}  # what a class's bays take: 1.8 cells
HOUSINGS = [(0.25, SMALL, 1), (0.5, SMALL, 2), (1, MEDIUM, 1), (2, MEDIUM, 2), (4, LARGE, 1), (8, LARGE, 2), (16, CAPITAL, 1)]
# A housing's size follows the bar: this band of bar makes that side of housing.
HOUSING_BAND = {0.25: (0.1, 0.2), 0.5: (0.25, 0.45), 1: (0.5, 0.95), 2: (1, 1.8), 4: (2, 3.95), 8: (4, 7.2), 16: (8, 28.8)}
# Tiers of a family are materials: (material, name prefix, mass x, health x, armour x, colour)
MATERIAL_TIERS = [("metal", "", 1, 1, 1, "#7c838c"), ("steel", "Steel ", 1.25, 1.8, 1.5, "#6f7d8c"), ("bronze", "Bronze ", 1.4, 1.4, 1.2, "#a8763a")]


def name_of(c, base):
    return f"{c.name} {base}" if c.name else base


def press_body(p, nx, ny, nz, bays, die):
    """The frame every press and lathe shares, in cells. Returns x0, x1, y0, y1, z0, z1 and the bays' centres."""
    c = p.cls
    k = c.cell
    x0, x1, y0, y1, z0, z1 = c.lo(), c.hi(nx), c.lo(), c.hi(ny), c.lo(), c.hi(nz)
    t = k * 0.12
    base = y0 + k * 1.2
    crown = y1 - k * 2
    p.add(box((x0, y0, z0), (x1, base, z1), BED))
    for x in (x0, x1 - k * 0.5):
        for z in (z0, z1 - k * 0.5):
            p.add(box((x, base, z), (x + k * 0.5, crown, z + k * 0.5)))
    p.add(box((x0, crown, z0), (x1, y1, z1)))
    wall = crown - k * 1.7
    p.add(box((x0 + k * 0.5, wall, z0), (x1 - k * 0.5, crown, z0 + t)))  # the back wall, under the crown: the bays' panels stand on it
    if die:
        mid = (x0 + x1) / 2
        p.add(box((mid - k * 0.8, crown - k * 0.9, (z0 + z1) / 2 - k * 0.8), (mid + k * 0.8, crown, (z0 + z1) / 2 + k * 0.8), DARK, detail=True, shape="cylinder", segments=12))
    span = x1 - x0 - k
    centres = [round(x0 + k * 0.5 + span * (i + 0.5) / bays, 3) for i in range(bays)]
    y = (base + wall) / 2
    for bx in centres:
        h = k * 0.95
        p.add(box((bx - h, wall - t, z0 - t * 0.2), (bx + h, wall, z0 + t), GOLD, detail=True))
        p.add(box((bx - h, base, z0 - t * 0.2), (bx + h, base + t, z0 + t), GOLD, detail=True))
    return x0, x1, y0, y1, z0, z1, base, crown, wall, y, centres


def panels(p, x0, x1, z0, z1, crown, wall, y1, centres, bays_width):
    """The bays' cards, over their mouths on the back wall; the machine's own on the front of the crown."""
    c = p.cls
    k = c.cell
    proud = 0.004 * max(1, c.x ** 0.5)
    h = k * 1.25
    out = []
    for i, bx in enumerate(centres):
        out.append({"source": "press.bay", "slot": i, "at": [bx, round((wall + crown) / 2, 3), round(z0 - proud, 4)], "size": [round(bays_width, 3), round(h, 3)], "face": "-z", "up": "+y", "rows": 5})
    out.append({"source": "press.die", "at": [round((x0 + x1) / 2, 3), round((crown + y1) / 2, 3), round(z1 + proud, 4)], "size": [round(k * 5, 3), round(k * 1.5, 3)], "face": "+z", "up": "+y", "rows": 4})
    return out


def tray(x0, x1, y, z0, z1):
    gap = (x1 - x0) * 0.01
    mid = (x0 + x1) / 2
    return [
        {"min": [round(x0 + gap, 3), y, z0], "max": [round(mid - gap, 3), round(y + (z1 - z0), 3), z1], "stack": 12, "label": "Tray"},
        {"min": [round(mid + gap, 3), y, z0], "max": [round(x1 - gap, 3), round(y + (z1 - z0), 3), z1], "stack": 12, "label": "Tray"},
    ]


def press(row):
    c, bays, speed, power, mass, health = row
    k = c.cell
    nx, ny, nz = 7, 8, 6
    p = Part(name_of(c, "Press"), c, SECTION, f"Stamps parts out of extruded pieces. {bays} bays and a die slot: the die sets what it makes. Set pieces in the bays with the physics gun or by chute. Needs power.", role="storage", group="Production", color=BODY)
    x0, x1, y0, y1, z0, z1, base, crown, wall, y, centres = press_body(p, nx, ny, nz, bays, True)
    block_sockets(p, nx, ny, nz, faces="xy", limit={d: (3, 3) for d in ("+x", "-x", "+y", "-y")})
    for i, bx in enumerate(centres):
        p.sock(f"bay{i + 1}", (bx, y, z0), "-z", kind="die")
    mid = (x0 + x1) / 2
    zm = (z0 + z1) / 2
    p.sock("dieslot", (mid, base, zm), "+y", kind="pressdie")
    p.stats(mass, health, c.armour, None if c is not SMALL else 4)
    bay_list = [{"socket": f"bay{i + 1}", "min": [round(bx - k * 0.95, 3), round(base, 3), round(z0 - k * 1.5, 3)], "max": [round(bx + k * 0.95, 3), round(wall, 3), round(z0 + k * 1.5, 3)]} for i, bx in enumerate(centres)]
    width = min(k * 3.2, (x1 - x0 - k) / bays - k * 0.1)
    tz0 = round(zm + k * 1.3, 3)
    tz1 = round(z1 - k * 0.55, 3)
    p.set(power=power, storage={
        "slots": tray(x0 + k * 0.55, x1 - k * 0.55, round(base + k * 0.01, 3), tz0, tz1),
        "press": {"bays": bay_list,
                  "die": {"socket": "dieslot", "min": [round(mid - k * 2, 3), round(base, 3), round(zm, 3)], "max": [round(mid + k * 2, 3), round(wall, 3), round(z1 + k * 2, 3)]},
                  "maxAcross": MAX_ACROSS[c], "speed": speed, "outAt": [round(mid, 3), round(y0, 3), round(z1 + k * 0.2, 3)], "outDir": "+z"},
    }, panels=panels(p, x0, x1, z0, z1, crown, wall, y1, centres, width))
    return p


def lathe(row):
    c, _, speed, power, mass, health = row
    k = c.cell
    nx, ny, nz = 7, 6, 5
    p = Part(name_of(c, "Die Lathe"), c, SECTION, "Cuts press dies from a solid block. Choose the die with the configure tool, and set the block in its bay. Needs power.", role="storage", group="Production", color=TRIM)
    x0, x1, y0, y1, z0, z1, base, crown, wall, y, centres = press_body(p, nx, ny, nz, 1, False)
    block_sockets(p, nx, ny, nz, faces="xy", limit={d: (3, 3) for d in ("+x", "-x", "+y", "-y")})
    p.sock("bay1", (centres[0], y, z0), "-z", kind="die")
    p.add(box((centres[0] - k * 0.7, y - k * 0.7, z0 + k), (centres[0] + k * 0.7, y + k * 0.7, z0 + k * 2), DARK, detail=True))
    p.stats(mass * 0.6, health * 0.7, c.armour, None if c is not SMALL else 4)
    width = min(k * 3.2, (x1 - x0 - k) - k * 0.1)
    p.set(power=power, config=[{"key": "recipe", "label": "Cuts", "group": "Work", "tooltip": "The die it cuts from the block in its bay. A lathe cuts dies of its own size."}], storage={
        "slots": tray(x0 + k * 0.55, x1 - k * 0.55, round(base + k * 0.01, 3), round(z0 + k * 2.2, 3), round(z1 - k * 0.55, 3)),
        "press": {"lathe": True, "bays": [{"socket": "bay1", "min": [round(centres[0] - k * 0.95, 3), round(base, 3), round(z0 - k * 1.5, 3)], "max": [round(centres[0] + k * 0.95, 3), round(wall, 3), round(z0 + k * 1.5, 3)]}],
                  "maxAcross": MAX_ACROSS[c], "speed": speed, "outAt": [round((x0 + x1) / 2, 3), round(y0, 3), round(z1 + k * 0.2, 3)], "outDir": "+z"},
    }, panels=panels(p, x0, x1, z0, z1, crown, wall, y1, centres, width))
    return p


def die(family, c, colour):
    k = c.cell
    s = k * 1.2
    h = k * 0.45
    nm = name_of(c, f"{family} Die")
    mass = {SMALL: 12, MEDIUM: 36}.get(c, 2000 if c is LARGE else 60000)
    p = Part(nm, c, DIES, f"Fits a {c.name.lower() + ' ' if c.name else ''}press and makes it stamp {family.lower()} parts. Every material a {family.lower()} part comes in uses this die.", role="panel", tool="wrench", group="Production", color=BED)
    p.add(box((-s, 0, -s), (s, h, s)))
    p.add(box((-s * 0.55, h, -s * 0.55), (s * 0.55, h * 1.5, s * 0.55), colour))
    p.sock("mount", (0, 0, 0), "-y", kind="pressdie")
    p.stats(mass, 300 * c.x, c.armour, 2 if c is SMALL else None)
    p.set(pressDie=family, centreSockets=False)
    return p


def rack(c, nx, ny, nz):
    k = c.cell
    x0, x1, y0, y1, z0, z1 = c.lo(), c.hi(nx), c.lo(), c.hi(ny), c.lo(), c.hi(nz)
    p = Part(name_of(c, "Die Rack"), c, SECTION, "Holds press dies on a shelf. A chute from it into a press brings a die to an empty slot.", role="storage", group="Production", color=TRIM)
    board = k * 0.12
    post = k * 0.14
    p.add(box((x0, y0, z0), (x1, y0 + board, z1), BED))
    for x in (x0, x1 - post):
        for z in (z0, z1 - post):
            p.add(box((x, y0 + board, z), (x + post, y1, z + post)))
    block_sockets(p, nx, ny, nz, faces="xy", limit={d: (2, 2) for d in ("+x", "-x", "+y", "-y")})
    p.stats(60 * c.x, 200 * c.x, c.armour, 3 if c is SMALL else None)
    gap = 0.015 * c.x
    p.set(storage={"slots": [{"min": [round(x0 + post + gap, 3), round(y0 + board + gap, 3), round(z0 + gap, 3)], "max": [round(x1 - post - gap, 3), round(y1 - gap, 3), round(z1 - gap, 3)], "rack": True, "label": "Dies"}]})
    return p


def housing(side, c, n, tier):
    material, prefix, mx, hx, ax, colour = tier
    k = c.cell
    x0, x1 = c.lo(), c.hi(n)
    nm = f"{prefix}Housing {side:g} m"
    p = Part(nm, c, "Housings", f"A stamped {material} box with open ends, to close over a mechanism. Its size follows the bar that made it; its material, its strength.", material="steel", color=colour, group="Structure")
    t = max(0.02, side * 0.08)
    p.add(box((x0, x0, x0), (x1, x0 + t, x1)))
    p.add(box((x0, x1 - t, x0), (x1, x1, x1)))
    p.add(box((x0, x0 + t, x0), (x0 + t, x1 - t, x1)))
    p.add(box((x1 - t, x0 + t, x0), (x1, x1 - t, x1)))
    block_sockets(p, n, n, n, limit={d: (2, 2) for d in ("+x", "-x", "+y", "-y", "+z", "-z")})
    p.stats(round(4 * side * side * c.kg_m2 * mx, 1), round(4 * side * side * c.health_m2 * hx), round(c.armour * ax, 1), None if c is not SMALL else 2)
    return p


def bay_row(label, sections, materials, lo, hi, use=None, across=None):
    b = {"label": label, "sections": list(sections), "materials": list(materials), "minAcross": lo, "maxAcross": hi}
    if use is not None:
        b["use"] = use
    else:
        b["useAcross"] = across
    return b


def rows():
    r = []
    def add(d, output, seconds, *bays, count=1):
        e = {"die": d, "output": output, "seconds": seconds, "bays": list(bays)}
        if count > 1:
            e["count"] = count
        r.append(e)
    add("Button", "Button", 1.5, bay_row("Strip", ["plate"], ["laminate"], 0.1, 0.45, use=0.1), bay_row("Rod", ["round"], ["copper"], 0.1, 0.25, use=0.05))
    # Bracket: the section picks the shape, the bar's size which of them. They are one tier, so every metal gives the same part.
    metals = ["metal", "steel", "bronze"]
    add("Bracket", "L Bracket", 1, bay_row("Angle", ["l-angle"], metals, 0.1, 0.2, use=0.15))
    add("Bracket", "Corner Bracket", 1.5, bay_row("Angle", ["l-angle"], metals, 0.25, 0.45, use=0.25))
    add("Bracket", "Gusset 0.25 m", 1, bay_row("Plate", ["plate"], metals, 0.25, 0.35, use=0.25))
    add("Bracket", "Gusset 0.5 m", 1.5, bay_row("Plate", ["plate"], metals, 0.4, 0.45, use=0.5))
    add("Gear", "Gear Set", 3, bay_row("Rod", ["round"], ["steel"], 0.1, 0.25, use=0.15))
    add("Board", "Circuit Board", 3, bay_row("Sheet", ["plate"], ["laminate"], 0.2, 0.45, use=0.2), bay_row("Wire", ["round"], ["copper"], 0.1, 0.15, use=0.05))
    add("Piston", "Piston Set", 5, bay_row("Rod", ["round"], ["steel"], 0.1, 0.25, use=0.2), bay_row("Bar", ["solid"], ["bronze"], 0.1, 0.25, use=0.1))
    for side, c, _ in HOUSINGS:
        lo, hi = HOUSING_BAND[side]
        for material, prefix, *_ in MATERIAL_TIERS:
            add("Housing", f"{prefix}Housing {side:g} m", 2 + side * 0.5, bay_row("Bar", ["hollow", "solid"], [material], lo, hi, across=1))
    for c, lo, hi in ((SMALL, 0.2, 0.45), (MEDIUM, 0.5, 1.8), (LARGE, 2, 7.2), (CAPITAL, 8, 28.8)):
        add("Cylinder", name_of(c, "Cylinder") if c is not SMALL else "Small Cylinder", 6, bay_row("Tube", ["pipe"], ["steel"], lo, hi, across=1), bay_row("Plate", ["plate"], ["steel"], lo, hi, across=0.5))
    return r


def lathe_rows():
    r = []
    for family, classes, block, tier, _ in FAMILIES:
        for c in classes:
            across = {SMALL: 0.25, MEDIUM: 1, LARGE: 4, CAPITAL: 8}[c]
            r.append({"output": name_of(c, f"{family} Die"), "seconds": 15 * tier * (1 + CLASSES_RANK[c]),
                      "bays": [bay_row("Block", ["solid"], ["steel"] if block == "steel" else ["metal", "steel"], across, MAX_ACROSS[c], use=across)]})
    return r


CLASSES_RANK = {SMALL: 0, MEDIUM: 1, LARGE: 2, CAPITAL: 3}


def line():
    """The Press Line: an Extruder cutting a plate strip into a press by chute, with its power; dies and the second bay's rod are the player's."""
    return {
        "format": "call-of-slop-construct-blueprint/1",
        "name": "Press Line",
        "description": "An Extruder chuted into a Press, with a bin and a battery: fit a die and tip in material.",
        "steps": [
            {"part": "Foundation"},
            {"part": "Extruder", "on": 0, "socket": "top21", "by": "bottom", "config": {"profile": 1, "width": 0.3, "height": 0.3, "cutLength": 2}},
            {"part": "Materials Bin", "on": 0, "socket": "top01", "by": "bottom"},
            {"part": "Battery", "on": 0, "socket": "top02", "by": "-y"},
            {"part": "Press", "on": 0, "socket": "top23", "by": "-y.3.2"},
        ],
        "links": [{"type": "chute", "from": 2, "to": 1}, {"type": "chute", "from": 1, "to": 4}],
    }


def write(path, text, check):
    try:
        with open(path, encoding="utf-8") as fh:
            same = fh.read() == text
    except OSError:
        same = False
    if check:
        if not same:
            print(f"out of date: {path}")
        return 0 if same else 1
    if not same:
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
    return 0


def main():
    check = "--check" in sys.argv
    parts = []
    for r in PRESSES:
        parts.append(("", press(r)))
        parts.append(("", lathe(r)))
    for family, classes, _, _, colour in FAMILIES:
        for c in classes:
            parts.append(("Dies", die(family, c, colour)))
    parts.append(("", rack(SMALL, 6, 6, 3)))
    parts.append(("", rack(MEDIUM, 6, 6, 3)))
    for side, c, n in HOUSINGS:
        for t in MATERIAL_TIERS:
            parts.append(("Housings", housing(side, c, n, t)))
    code = write_parts(OUT, parts)
    table = {"format": FORMAT, "about": ABOUT, "rows": rows(), "lathe": lathe_rows()}
    code |= write(TABLE, json.dumps(table, indent=1) + "\n", check)
    code |= write(BLUEPRINT, json.dumps(line(), indent=2) + "\n", check)
    return code


if __name__ == "__main__":
    sys.exit(main())
