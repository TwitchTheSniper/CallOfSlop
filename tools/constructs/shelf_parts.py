#!/usr/bin/env python3
"""Writes the modular shelves and the Smart Terminal: Constructs/Parts/Shelves, and the Smart Depot blueprint.

docs/sandbox/CONSTRUCTS.md, "Modular shelves" and "The Smart Terminal". A Shelf Bay is one board on four posts, open at
the front and the top, on its class's lattice, so bays snap on each other up, along and back to back. Its room is "rack"
slots: a slot takes as many of one part as really fit, in rows and layers, and holds them in place on anything that moves.

Items are parts a hand can carry (40 kg at most, ConstructParts.HandCarryLimit), so shelves stop at the medium class: a
Large bay (8 m) would stand a person's parts on a floor no one can reach, and physics-only pieces don't go on shelves.

Run it from the repo root: python3 -I tools/constructs/shelf_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import MEDIUM, SMALL, Part, block_sockets, box, vec, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Shelves")
BOARD, POST, BRACE, LIP = "#8a6b45", "#46525e", "#5a6672", "#c9a21c"
CASE, DARK, SCREEN, LAMP = "#3f4c5a", "#2a3036", "#14323a", "#39465a"
GAP = 0.015  # slots stand this clear of the boxes round them


def bay(name, cls, nx, ny, nz, slots, desc):
    """One bay: nx x ny x nz cells of its class, a board at the bottom and slots split evenly across it."""
    p = Part(name, cls, "Shelves", desc, role="storage", tool="welder", color=POST, group="Storage")
    x0, x1, y0, y1, z0, z1 = cls.lo(), cls.hi(nx), cls.lo(), cls.hi(ny), cls.lo(), cls.hi(nz)
    board = 0.04 * (1 if cls is SMALL else 2)
    post = 0.04 * (1 if cls is SMALL else 2)
    p.add(box((x0, y0, z0), (x1, y0 + board, z1), BOARD))
    for x in (x0, x1 - post):
        for z in (z0, z1 - post):
            p.add(box((x, y0 + board, z), (x + post, y1, z + post)))
    # A brace across the back, and a lip along the front that a stack sits behind.
    p.add(box((x0 + post, y1 - post, z0), (x1 - post, y1, z0 + post * 0.5), BRACE, detail=True))
    p.add(box((x0 + post, y0 + board, z1 - 0.02), (x1 - post, y0 + board + 0.03, z1 - 0.005), LIP, detail=True))
    block_sockets(p, nx, ny, nz, skip=("+z",), limit={"-y": (4, 2), "+y": (4, 2), "-x": (2, 2), "+x": (2, 2), "-z": (4, 2)})
    lo, hi = x0 + post + GAP, x1 - post - GAP
    each = (hi - lo) / slots
    room = []
    for k in range(slots):
        room.append({
            "min": vec(lo + k * each + (GAP if k else 0), y0 + board + GAP, z0 + GAP),
            "max": vec(lo + (k + 1) * each - (GAP if k < slots - 1 else 0), y1 - post - GAP, z1 - 0.03),
            "rack": True,
            "label": "shelf",
        })
    wide, deep, high = nx * cls.cell, nz * cls.cell, ny * cls.cell
    mass = (wide * deep * (8 if cls is SMALL else 16)) + high * (6 if cls is SMALL else 14)
    p.stats(mass, 80 + 60 * wide * deep * (1 if cls is SMALL else 0.5), 3)
    p.set(storage={"slots": room})
    return p


def terminal():
    """A cabinet with a screen over a tray: chutes read through Network, and what is pulled lands on the tray."""
    p = Part("Smart Terminal", SMALL, "Beacons and recycling", "Lists its chute network's stores. Pulls an item to its tray, a chute or your hands. Needs power.",
             role="storage", tool="welder", color=CASE, group="Storage")
    w, d = 0.45, 0.35
    p.add(box((-w - 0.04, 0, -d - 0.04), (w + 0.04, 0.07, d + 0.04), DARK))
    p.add(box((-w, 0.06, -d), (w, 0.3, d)))  # the plinth; the tray's floor is its top
    p.add(box((-w, 0.3, -d), (w, 1.3, -0.02)))  # the cabinet
    p.add(box((-w, 0.3, -0.02), (-w + 0.03, 0.76, d)))  # the tray's cheeks
    p.add(box((w - 0.03, 0.3, -0.02), (w, 0.76, d)))
    p.add(box((-w, 0.76, -0.02), (w, 1.3, 0.1)))  # the hood the screen stands in
    p.add(box((-0.34, 0.84, 0.094), (0.34, 1.24, 0.106), SCREEN, detail=True))
    p.add(box((-0.34, 0.31, 0.32), (0.34, 0.34, 0.345), "#c9a21c", detail=True))
    p.add(box((-0.05, 1.3, -0.1), (0.05, 1.38, 0.0), LAMP, detail=True))  # the lamp: the last box, lit while it works
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("back", (0, 0.6, -d), "-z")
    p.sock("+x", (w, 0.6, -0.15), "+x")
    p.sock("-x", (-w, 0.6, -0.15), "-x")
    p.stats(70, 200, 3, seconds=4.0)
    p.set(power=60, onBreak="disable")
    p.set(ports=[
        {"name": "Network", "type": "chute", "dir": "both", "at": [-0.25, 1.3, -0.15], "face": "+y"},
        {"name": "Chute out", "type": "chute", "dir": "out", "at": [w, 0.5, 0.15], "face": "+x"},
    ])
    p.set(storage={"slots": [{"min": [-0.41, 0.315, 0.0], "max": [0.41, 0.745, 0.315], "rack": True, "label": "tray"}], "terminal": True})
    return p


def everything():
    small = [
        ("Shelf Bay 0.5", 2, 2, 2, 1),
        ("Shelf Bay 1", 4, 2, 2, 2),
        ("Tall Shelf Bay 1", 4, 4, 2, 2),
    ]
    medium = [
        ("Medium Shelf Bay 1", 1, 1, 1, 1),
        ("Medium Shelf Bay 2", 2, 1, 1, 2),
        ("Medium Shelf Bay 4", 4, 2, 1, 4),
    ]
    parts = []
    for name, nx, ny, nz, slots in small:
        parts.append(("Small", bay(name, SMALL, nx, ny, nz, slots, f"Snaps to more bays. {slots} shelf slot{'s' if slots > 1 else ''}: parts stack as they fit.")))
    for name, nx, ny, nz, slots in medium:
        parts.append(("Medium", bay(name, MEDIUM, nx, ny, nz, slots, f"Rack for a store room. {slots} shelf slot{'s' if slots > 1 else ''}: parts stack as they fit.")))
    parts.append(("", terminal()))
    return parts


def depot():
    """A terminal reading two shelves and a bin, and a Chest its Chute out feeds."""
    return {
        "format": "call-of-slop-construct-blueprint/1",
        "name": "Smart Depot",
        "description": "A Smart Terminal reading stacked shelves and a bin, feeding a Chest.",
        "steps": [
            {"part": "Foundation"},
            {"part": "Smart Terminal", "on": 0, "socket": "top00", "by": "bottom"},
            {"part": "Battery", "on": 0, "socket": "top30", "by": "-y"},
            {"part": "Shelf Bay 1", "on": 0, "socket": "top03", "by": "-y.0.0"},
            {"part": "Shelf Bay 1", "on": 3, "socket": "+y.0.0", "by": "-y.0.0"},
            {"part": "Materials Bin", "on": 0, "socket": "top21", "by": "bottom"},
            {"part": "Chest", "on": 0, "socket": "top33", "by": "bottom"},
        ],
        "links": [
            {"type": "chute", "from": 1, "to": 3},
            {"type": "chute", "from": 1, "to": 4},
            {"type": "chute", "from": 1, "to": 5},
            {"type": "chute", "from": 1, "fromPort": "Chute out", "to": 6},
        ],
    }


def write_blueprint():
    """Smart Depot's file (--check compares). Returns the exit code."""
    path = os.path.join("Constructs", "Blueprints", "SmartDepot.json")
    text = json.dumps(depot(), indent=2) + "\n"
    if "--check" in sys.argv:
        with open(path, encoding="utf-8") as fh:
            same = fh.read() == text
        print("blueprint " + ("current" if same else "out of date"))
        return 0 if same else 1
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(write_parts(OUT, everything()) | write_blueprint())
