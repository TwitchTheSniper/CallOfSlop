#!/usr/bin/env python3
"""Writes the build stations' part files: Constructs/Parts/Stations.

docs/sandbox/CONSTRUCTS.md, "Build stations". A station is an Inventory Beacon with an aid (storage.beacon names it): it
is welded on, wants power and a chute to a store, and the last box of each model is the lamp that lights while it works.
Numbers (mass, health, power) are here; ranges are in Constructs/Beacons.json; recipes in tools/constructs/assembling.py.

Run it from the repo root: python3 -I tools/constructs/station_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zfight  # noqa: E402

FOLDER = os.path.join("Constructs", "Parts", "Stations")
STEEL, DARK, TRIM, SCREEN, LAMP = "#46525e", "#2a3036", "#9aa4ae", "#14323a", "#39465a"


def b(mn, mx, color=None, detail=False, **more):
    d = {"min": list(mn), "max": list(mx)}
    if color:
        d["color"] = color
    if detail:
        d["detail"] = True
    d.update(more)
    return d


def bench(w, d, h):
    """A cabinet on a foot plate: every station stands on one, half-widths w and d, h high."""
    return [b((-w - 0.04, 0, -d - 0.04), (w + 0.04, 0.07, d + 0.04), DARK), b((-w, 0.06, -d), (w, h, d))]


def sockets(w, d, y=0.4):
    return [
        {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
        {"name": "+x", "at": [w, y, 0], "dir": "+x"},
        {"name": "-x", "at": [-w, y, 0], "dir": "-x"},
        {"name": "back", "at": [0, y, -d], "dir": "-z"},
    ]


def mirror_table():
    w, d = 0.6, 0.4
    m = bench(w, d, 0.72)
    m += [b((-w - 0.03, 0.72, -d - 0.03), (w + 0.03, 0.78, d + 0.03), TRIM)]
    # The mirror: a bright pane across the table, a frame round it, and a part on each side, the same.
    m += [b((-0.02, 0.78, -0.36), (0.02, 1.5, 0.36), "#5d6a76"), b((-0.03, 0.82, -0.32), (0.03, 1.46, 0.32), "#cfe6f2", detail=True)]
    m += [b((0.2, 0.78, -0.12), (0.44, 0.94, 0.12), "#c7923a", detail=True), b((-0.44, 0.78, -0.12), (-0.2, 0.94, 0.12), "#c7923a", detail=True)]
    m += [b((-0.2, 0.3, d - 0.006), (0.2, 0.56, d + 0.006), SCREEN, detail=True)]
    return m, sockets(w, d)


def line_layer():
    w, d = 0.42, 0.75
    m = bench(w, d, 0.62)
    # A bed of rails, a row of beams on it and the carriage that lays them.
    m += [b((-0.3, 0.62, -0.72), (-0.24, 0.72, 0.72), TRIM), b((0.24, 0.62, -0.72), (0.3, 0.72, 0.72), TRIM)]
    for k in range(4):
        z = -0.6 + k * 0.28
        m.append(b((-0.2, 0.62, z), (0.2, 0.74, z + 0.2), "#b0763a", detail=True))
    m += [b((-0.36, 0.72, 0.42), (0.36, 1.2, 0.56), "#c9a21c"), b((-0.1, 0.84, 0.3), (0.1, 1.02, 0.42), DARK, detail=True)]
    m += [b((-0.15, 0.2, d - 0.006), (0.15, 0.46, d + 0.006), SCREEN, detail=True)]
    return m, sockets(w, d)


def replacer():
    w, d = 0.5, 0.5
    m = bench(w, d, 0.7)
    # A round bed and an arm with a claw above it: one part out, the next in.
    m += [b((-0.36, 0.7, -0.36), (0.36, 0.78, 0.36), TRIM), b((-0.12, 0.78, -0.12), (0.12, 0.98, 0.12), "#4e8f6a", detail=True)]
    m += [b((-0.46, 0.7, -0.46), (-0.32, 1.7, -0.32), STEEL), b((-0.46, 1.62, -0.46), (0.1, 1.74, -0.32), STEEL)]
    m += [b((-0.04, 1.3, -0.46), (0.06, 1.62, -0.32), DARK), b((-0.16, 1.2, -0.42), (0.16, 1.3, -0.36), "#c9a21c", detail=True)]
    m += [b((-0.18, 0.28, d - 0.006), (0.18, 0.54, d + 0.006), SCREEN, detail=True)]
    return m, sockets(w, d)


def gantry():
    w, d = 0.9, 0.5
    m = [b((-w - 0.04, 0, -d - 0.04), (w + 0.04, 0.07, d + 0.04), DARK), b((-w, 0.06, -d), (-w + 0.2, 2.9, -d + 0.2)), b((w - 0.2, 0.06, -d), (w, 2.9, -d + 0.2))]
    m += [b((-w, 0.06, d - 0.2), (-w + 0.2, 2.9, d)), b((w - 0.2, 0.06, d - 0.2), (w, 2.9, d))]
    # The portal's top beams, a trolley and a camera on it, and a low desk between the legs.
    m += [b((-w - 0.03, 2.86, -d - 0.03), (w + 0.03, 3.02, -d + 0.21), "#c9a21c"), b((-w - 0.03, 2.87, d - 0.21), (w + 0.03, 3.03, d + 0.03), "#c9a21c")]
    m += [b((-0.3, 3.02, -0.5), (0.3, 3.12, 0.5), STEEL), b((-0.1, 2.72, -0.1), (0.1, 3.02, 0.1), DARK, detail=True)]
    m += [b((-0.34, 0.06, -0.26), (0.34, 0.82, 0.22), STEEL), b((-0.26, 0.5, 0.216), (0.26, 0.78, 0.228), SCREEN, detail=True)]
    return m, [
        {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
        {"name": "+x", "at": [w, 0.4, -d + 0.1], "dir": "+x"},
        {"name": "-x", "at": [-w, 0.4, -d + 0.1], "dir": "-x"},
        {"name": "back", "at": [0, 0.4, -0.26], "dir": "-z"},
    ]


def selector():
    w, d = 0.45, 0.45
    m = bench(w, d, 0.82)
    # A console under a wire cube: twelve thin edges, the shape of a box select.
    c, h = 0.3, 0.3
    y0, y1 = 1.0, 1.6
    edges = [((-c, y0, -h), (c, y0 + 0.03, -h + 0.03)), ((-c, y1, -h), (c, y1 + 0.03, -h + 0.03)), ((-c, y0, h - 0.03), (c, y0 + 0.03, h)), ((-c, y1, h - 0.03), (c, y1 + 0.03, h))]
    edges += [((-c, y0, -h), (-c + 0.03, y0 + 0.03, h)), ((-c, y1, -h), (-c + 0.03, y1 + 0.03, h)), ((c - 0.03, y0, -h), (c, y0 + 0.03, h)), ((c - 0.03, y1, -h), (c, y1 + 0.03, h))]
    edges += [((-c, y0, -h), (-c + 0.03, y1, -h + 0.03)), ((c - 0.03, y0, -h), (c, y1, -h + 0.03)), ((-c, y0, h - 0.03), (-c + 0.03, y1, h)), ((c - 0.03, y0, h - 0.03), (c, y1, h))]
    m += [b(a, e, "#54d0e0", detail=True) for a, e in edges]
    m += [b((-0.04, 0.82, -0.04), (0.04, 1.0, 0.04), TRIM), b((-0.24, 0.36, d - 0.006), (0.24, 0.66, d + 0.006), SCREEN, detail=True)]
    return m, sockets(w, d)


def blueprint_desk():
    w, d = 0.7, 0.45
    m = bench(w, d, 0.74)
    # A raised drawing board on a shelf, a blue sheet on it, and rolled plans at the side.
    m += [b((-0.62, 0.74, -0.42), (0.62, 1.08, -0.3), STEEL), b((-0.62, 0.74, -0.3), (0.62, 0.82, 0.4), TRIM)]
    m += [b((-0.54, 0.82, -0.26), (0.54, 0.84, 0.36), "#2c5fa8", detail=True), b((-0.3, 0.84, -0.1), (0.3, 0.846, 0.2), "#dbe8f6", detail=True)]
    m += [b((0.62, 0.74, -0.3), (0.68, 1.3, -0.24), "#d8c690", detail=True), b((0.62, 0.74, -0.18), (0.68, 1.2, -0.12), "#d8c690", detail=True)]
    return m, sockets(w, d)


MIRROR_CONFIG = [
    {"key": "mirrorAxis", "label": "Plane", "group": "Mirror", "options": ["off", "side to side", "front to back", "up and down"], "default": 0,
     "tooltip": "Which way parts are mirrored, from the seat of the construct you build on (or its first part)."},
    {"key": "mirrorOffset", "label": "Offset", "group": "Mirror", "unit": "m", "min": -64, "max": 64, "step": 0.125, "default": 0,
     "tooltip": "How far the plane sits from the construct's centre."},
]

# (name, file, aid, mass, power W, description, model, config)
STATIONS = [
    ("Mirror Table", "MirrorTable", "mirror", 160, 150, "Mirrors what you build across a plane, in range. Needs power and a chute.", mirror_table, MIRROR_CONFIG),
    ("Line Layer", "LineLayer", "line", 180, 150, "Lays a row or a plate field in one drag, in range. Needs power and a chute.", line_layer, None),
    ("Replacer", "Replacer", "replace", 170, 150, "Swaps a part for another that fits, in range. Needs power and a chute.", replacer, None),
    ("Build Gantry", "BuildGantry", "gantry", 420, 300, "A camera that flies round your build, in range. Needs power and a chute.", gantry, None),
    ("Selector", "Selector", "select", 140, 120, "Select a box of parts to move, copy, delete, paint or weld. Needs power and a chute.", selector, None),
    ("Blueprint Desk", "BlueprintDesk", "blueprint", 150, 120, "Saves a selection as a blueprint to stamp elsewhere. Needs power and a chute.", blueprint_desk, None),
]


def part(s):
    name, _, aid, mass, power, desc, model, config = s
    boxes, socks = model()
    h = max(x["max"][1] for x in boxes)
    boxes.append(b((-0.05, h - 0.002, -0.05), (0.05, h + 0.09, 0.05), LAMP, detail=True))  # the lamp: the last box lights while it works
    zfight.resolve(boxes, socks)
    p = {
        "format": "call-of-slop-construct-part/1",
        "name": name,
        "description": desc,
        "group": "Build stations",
        "role": "storage",
        "tool": "welder",
        "attachSeconds": 4.0,
        "mass": mass,
        "health": 240,
        "armour": 3,
        "material": "steel",
        "power": power,
        "onBreak": "disable",
        "color": STEEL,
        "collider": "boxes",
        "boxes": boxes,
        "sockets": socks,
        "storage": {"beacon": aid},
    }
    if config:
        p["config"] = config
    return p


def main():
    check = "--check" in sys.argv
    bad = 0
    os.makedirs(FOLDER, exist_ok=True)
    for s in STATIONS:
        path = os.path.join(FOLDER, s[1] + ".json")
        text = json.dumps(part(s), indent=2) + "\n"
        old = open(path, encoding="utf-8").read() if os.path.exists(path) else ""
        if old != text:
            if check:
                print(f"out of date: {path}")
                bad += 1
            else:
                with open(path, "w", encoding="utf-8", newline="\n") as f:
                    f.write(text)
    print(f"stations: {len(STATIONS)} parts, {bad} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
