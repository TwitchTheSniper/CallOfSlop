#!/usr/bin/env python3
"""Writes the modular shelves and the Smart Terminal: Constructs/Parts/Shelves, and the Smart Depot blueprint.

docs/sandbox/CONSTRUCTS.md, "Modular shelves" and "The Smart Terminal". A Shelf Bay is a cabinet (two sides, a back, a top
board) with one or two tiers of boards, open at the front, on its class's lattice, so bays snap on each other up, along
and back to back. Its room is "rack" slots: a slot takes as many of one part as really fit, in rows and layers, and
holds them in place on anything that moves.

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


def bay(name, cls, nx, ny, nz, slots, desc, tiers=1):
    """One bay: nx x ny x nz cells of its class, a cabinet of two sides, a back and a top, with tiers boards inside.

    Each tier has slots split evenly across it; the bottom tier's slots come first, so a save from before the tiers keeps
    its stacks where they were."""
    p = Part(name, cls, "Shelves", desc, role="storage", tool="welder", color=POST, group="Storage")
    x0, x1, y0, y1, z0, z1 = cls.lo(), cls.hi(nx), cls.lo(), cls.hi(ny), cls.lo(), cls.hi(nz)
    small = cls is SMALL
    board = 0.03 if small else 0.05
    side = 0.03 if small else 0.05  # the side panels
    back = 0.015 if small else 0.025  # the back panel
    inset = 0.01 if small else 0.02  # boards stand this far behind the sides' front edge, so the sides frame them
    # The carcass: two sides the full height and depth, a back between them, open only at the front.
    p.add(box((x0, y0, z0), (x0 + side, y1, z1)))
    p.add(box((x1 - side, y0, z0), (x1, y1, z1)))
    p.add(box((x0 + side, y0, z0), (x1 - side, y1, z0 + back), BRACE))
    bx0, bx1, bz0, bz1 = x0 + side, x1 - side, z0 + back, z1 - inset
    # Boards: one at the bottom, one under the top, and one between each pair of tiers.
    levels = [y0 + (y1 - y0) * k / tiers for k in range(tiers + 1)]
    floors = []  # (top of the board under a tier, bottom of the board over it)
    for k, y in enumerate(levels):
        lo = y if k == 0 else (y - board if k == tiers else y - board / 2)
        hi = lo + board
        p.add(box((bx0, lo, bz0), (bx1, hi, bz1), BOARD))
        if k < tiers:
            # A lip along the board's front that a stack sits behind.
            p.add(box((bx0, hi, bz1 - 0.02), (bx1, hi + 0.03, bz1 - 0.005), LIP, detail=True))
        floors.append((lo, hi))
    block_sockets(p, nx, ny, nz, skip=("+z",), limit={"-y": (4, 2), "+y": (4, 2), "-x": (2, 2), "+x": (2, 2), "-z": (4, 2)})
    lo, hi = bx0 + GAP, bx1 - GAP
    each = (hi - lo) / slots
    room = []
    for t in range(tiers):
        floor, roof = floors[t][1], floors[t + 1][0]
        for k in range(slots):
            room.append({
                "min": vec(lo + k * each + (GAP if k else 0), floor + GAP, bz0 + GAP),
                "max": vec(lo + (k + 1) * each - (GAP if k < slots - 1 else 0), roof - GAP, bz1 - 0.025),
                "rack": True,
                "label": "shelf",
            })
    wide, deep, high = nx * cls.cell, nz * cls.cell, ny * cls.cell
    mass = (wide * deep * (8 if small else 16)) * (tiers + 1) / 2 + high * (deep + wide) * (6 if small else 14)
    p.stats(mass, 80 + 60 * wide * deep * (1 if small else 0.5), 3)
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
    p.add(box((-0.4, 0.82, 0.094), (0.4, 1.26, 0.106), SCREEN, detail=True))  # what its panel stands on
    p.add(box((-0.34, 0.31, 0.32), (0.34, 0.34, 0.345), "#c9a21c", detail=True))
    p.add(box((-0.05, 1.3, -0.1), (0.05, 1.38, 0.0), LAMP, detail=True))  # the lamp: the last box, lit while it works
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("back", (0, 0.6, -d), "-z")
    p.sock("+x", (w, 0.6, -0.15), "+x")
    p.sock("-x", (-w, 0.6, -0.15), "-x")
    p.stats(70, 200, 3, seconds=4.0)
    p.set(power=60, onBreak="disable")
    p.set(panels=[{"source": "terminal", "at": [0, 1.04, 0.112], "size": [0.78, 0.42], "face": "+z", "up": "+y", "rows": 6, "range": 16}])
    p.set(ports=[
        {"name": "Network", "type": "chute", "dir": "both", "at": [-0.25, 1.3, -0.15], "face": "+y"},
        {"name": "Chute out", "type": "chute", "dir": "out", "at": [w, 0.5, 0.15], "face": "+x"},
    ])
    p.set(storage={"slots": [{"min": [-0.41, 0.315, 0.0], "max": [0.41, 0.745, 0.315], "rack": True, "label": "tray"}], "terminal": True})
    return p


def everything():
    small = [
        ("Shelf Bay 0.5", 2, 2, 2, 1, 1),
        ("Shelf Bay 1", 4, 2, 2, 2, 1),
        ("Tall Shelf Bay 1", 4, 4, 2, 2, 2),
    ]
    medium = [
        ("Medium Shelf Bay 1", 1, 1, 1, 1, 1),
        ("Medium Shelf Bay 2", 2, 1, 1, 2, 1),
        ("Medium Shelf Bay 4", 4, 2, 1, 4, 2),
    ]

    def say(slots, tiers):
        n = slots * tiers
        return f"{n} shelf slot{'s' if n > 1 else ''}" + (f" on {tiers} tiers" if tiers > 1 else "")

    parts = []
    for name, nx, ny, nz, slots, tiers in small:
        parts.append(("Small", bay(name, SMALL, nx, ny, nz, slots, f"Snaps to more bays. {say(slots, tiers)}: parts stack as they fit.", tiers)))
    for name, nx, ny, nz, slots, tiers in medium:
        parts.append(("Medium", bay(name, MEDIUM, nx, ny, nz, slots, f"Rack for a store room. {say(slots, tiers)}: parts stack as they fit.", tiers)))
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
            {"part": "Smart Terminal", "on": 0, "socket": "top03", "by": "bottom"},  # at the front edge: nothing stands or runs before its screen
            {"part": "Battery", "on": 0, "socket": "top30", "by": "-y"},
            {"part": "Shelf Bay 1", "on": 0, "socket": "top00", "by": "-y.0.0"},
            {"part": "Shelf Bay 1", "on": 3, "socket": "+y.0.0", "by": "-y.0.0"},
            {"part": "Materials Bin", "on": 0, "socket": "top31", "by": "bottom"},
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
