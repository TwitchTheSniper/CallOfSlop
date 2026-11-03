#!/usr/bin/env python3
"""Writes the Sandbox's machine tiers (Constructs/Parts/Gathering and Constructs/Parts/Refining).

docs/sandbox/CONSTRUCTS.md, "Machine tiers". Four lines of machine (Drill Module, Saw Module, Sawmill, Smelter), five tiers
each, following the threat ladder: 1 Scrap, 2 Workshop, 3 Industrial, 4 Military, 5 Plasma. Tier 1 keeps the name, the
file and the numbers of the part that was there before, except the Drill Module, a compact rig with a small buffer.

Higher tiers are physically larger: every box, bin and work box grows by the tier's SIZE from the part's origin (the
centre of its bottom socket). Only what hangs below the floor keeps tier 1's length: a bit reaches the same 1.2 m under
the part at every tier, so a bigger drill still fits the ground clearance of a vehicle a small one fits (its bit is
thicker, not longer). Sockets stay on the 0.25 m frame grid: scaled, then snapped. Tier 2's 1.25 m body doesn't reach a
grid line, so its side and back sockets stand on small solid mounting pads at 0.75 m.

The curves are the CURVE table below: higher tiers work faster, hold more, are tougher, draw more power and weigh more,
and the power a unit costs falls. Bins hold what their volume holds (SIZE cubed, rounded); mass and health go by SIZE
squared, like a shell. What a tier adds to the look on top is a drawn detail.

Run it from the repo root: python3 -I tools/constructs/machine_tiers.py (it rewrites the files; --check only compares).
Data first: the JSON files are what the game loads; this script only keeps the lines consistent. Fix it, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zfight  # noqa: E402

FORMAT = "call-of-slop-construct-part/1"
GROUP = "Production"

TIERS = [None, "Scrap", "Workshop", "Industrial", "Military", "Plasma"]
PREFIX = [None, "", "Workshop ", "Industrial ", "Military ", "Plasma "]

GRID = 0.25  # the frame step: sockets stay on it
SIZE = {1: 1.0, 2: 1.25, 3: 1.5, 4: 2.0, 5: 2.5}  # linear size by tier

# tier: multipliers on tier 1's numbers.
CURVE = {
    #    rate  power cap   mass  health armour attach
    1: (1,  1.0, 1,   1.0,  1.0,  1.0, 1.0),
    2: (2,  1.8, 2,   1.5,  1.5,  1.5, 1.25),
    3: (4,  3.2, 3.5, 2.25, 2.25, 2.0, 1.5),
    4: (8,  5.6, 8,   4.0,  4.0,  3.0, 1.75),
    5: (16, 9.6, 16,  6.25, 6.25, 4.0, 2.0),
}

# A tier's colours by role. Tier 1's come from each line's own table below.
PALETTE = {
    2: dict(body="#7d6b45", trim="#5e4426", frame="#9a8a66", accent="#c7a23a", edge="#d0d4da", dark="#3a332a", core="#8a4a30", heat="#ff8a2a", motor="#6a7a45", light="#c7a23a"),
    3: dict(body="#34566f", trim="#2a4254", frame="#8d99a6", accent="#e8b81e", edge="#dfe4ea", dark="#22272d", core="#5a6470", heat="#ffa030", motor="#2f7a6a", light="#40e070"),
    4: dict(body="#4b5436", trim="#3a4230", frame="#5c6448", accent="#1e211c", edge="#a8aeb4", dark="#1e211c", core="#3e4438", heat="#ff4a20", motor="#3a4230", light="#ff3020"),
    5: dict(body="#1c2130", trim="#d9e2ec", frame="#2c3448", accent="#35e0ff", edge="#6ff0ff", dark="#10131c", core="#2a3044", heat="#b050ff", motor="#d9e2ec", light="#35e0ff"),
}
GLOWS = {3: ("light",), 4: ("light", "heat"), 5: ("light", "heat", "edge", "accent")}  # the roles that glow at a tier


def box(mn, mx, role="body"):
    return {"min": list(mn), "max": list(mx), "role": role}


def wall_bin(height, role="body"):
    """The gathering modules' bin: a floor and four walls."""
    return [
        box((-0.5, 0, -0.5), (0.5, 0.05, 0.5), role),
        box((-0.5, 0.05, 0.44), (0.5, height, 0.5), role),
        box((-0.5, 0.05, -0.5), (0.5, height, -0.44), role),
        box((-0.5, 0.05, -0.44), (-0.44, height, 0.44), role),
        box((0.44, 0.05, -0.44), (0.5, height, 0.44), role),
    ]


MODULE_SOCKETS = [
    {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
    {"name": "+x", "at": [0.5, 0.3, 0], "dir": "+x"},
    {"name": "-x", "at": [-0.5, 0.3, 0], "dir": "-x"},
    {"name": "back", "at": [0, 0.3, -0.5], "dir": "-z"},
]
MODULE_BIN = dict(min=[-0.435, 0.055, -0.435], max=[0.435, 0.64, 0.435])
MODULE_COLOURS = dict(body="#5f6b5a", frame="#7a8592", edge="#b9c0c8")

RIM = [(-0.5, 0.7, -0.5), (0.5, 0.735, -0.44)], [(-0.5, 0.7, 0.44), (0.5, 0.735, 0.5)]

# A Drill Module is a compact rig: no bin of its own, a small hopper as its buffer (2 s of its rate at every tier), and a
# named Chute out it must be connected through to keep working. A housing, a boom and a tower carry the bit, at the same
# place under the part as ever. The gearhead on top takes the optional drive shaft: its socket is on the grid, and the
# port moves to the middle of the head. tier: (rated rpm of the shaft, N m it takes off it), the data of "Drive in".
DRILL_SHAFT = {1: (1800, 40), 2: (1600, 80), 3: (1400, 160), 4: (1200, 320), 5: (1000, 640)}
DRILL_SOCKETS = [
    {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
    {"name": "+x", "at": [0.25, 0.25, 0], "dir": "+x"},
    {"name": "-x", "at": [-0.25, 0.25, 0], "dir": "-x"},
    {"name": "back", "at": [0, 0.25, -0.25], "dir": "-z"},
    {"name": "drive", "at": [0, 1, 0.75], "dir": "+y"},
]

# (from tier, min, max, role): what a tier adds, on top of the tiers below it. All drawn details.
DRILL = dict(
    file="Gathering/DrillModule", name="Drill Module", section="Drills", mass=200, health=400, armour=6, power=900, attach=4.0,
    colour="#5f6b5a", tints=dict(MODULE_COLOURS, motor="#55705a"), sockets=DRILL_SOCKETS, bulk=dict(min=[-0.155, 0.465, -0.155], max=[0.155, 0.615, 0.155]), capacity=20,
    description="Taps what lies underground: fit it so its bit reaches the ground over a vein. It holds only a small buffer: link its Chute out to a bin, a Storage Volume or a machine, or it stops when the buffer is full. It runs on electric power, or from a drive shaft into its Drive in: a linked shaft alone runs it, its speed sets the rate and it takes torque from the engine. Look at it to hear the nearest deposit.",
    boxes=[
        box((-0.25, 0, -0.25), (0.25, 0.46, 0.25)),
        box((-0.19, 0.46, -0.19), (0.19, 0.62, -0.16)), box((-0.19, 0.46, 0.16), (0.19, 0.62, 0.19)),
        box((-0.19, 0.46, -0.16), (-0.16, 0.62, 0.16)), box((0.16, 0.46, -0.16), (0.19, 0.62, 0.16)),
        box((-0.12, 0.24, 0.25), (0.12, 0.46, 0.5), "frame"),
        box((-0.16, -0.3, 0.5), (0.16, 0.92, 0.86), "frame"),
        box((-0.13, 0.92, 0.55), (0.13, 1.0, 0.81), "motor"),
        box((-0.07, -1.2, 0.61), (0.07, -0.3, 0.75), "edge"),
    ],
    gather=dict(kind="drill", rate=10, min=[-0.1, -1.2, 0.55], max=[0.1, -0.6, 0.8]),
    ports=[
        {"name": "Chute out", "type": "chute", "dir": "out", "at": [0.19, 0.54, 0], "face": "+x"},
        {"name": "Drive in", "type": "shaft", "dir": "in", "socket": "drive", "at": [0, 1, 0.68], "face": "+y"},
    ],
    extras=[
        (2, (-0.19, 0.84, 0.47), (0.19, 0.9, 0.89), "dark"),
        (2, (0.16, 0.62, 0.6), (0.34, 0.84, 0.78), "motor"),
        (2, (-0.255, 0.38, -0.255), (0.255, 0.42, 0.255), "accent"),
        (3, (-0.11, -1.05, 0.58), (0.11, -1.0, 0.78), "frame"),
        (3, (-0.11, -0.8, 0.58), (0.11, -0.75, 0.78), "frame"),
        (3, (-0.11, -0.55, 0.58), (0.11, -0.5, 0.78), "frame"),
        (3, (-0.24, 0.06, 0.25), (-0.13, 0.36, 0.262), "accent"),
        (3, (0.13, 0.06, 0.25), (0.24, 0.36, 0.262), "accent"),
        (3, (-0.34, 0.62, 0.6), (-0.16, 0.84, 0.78), "motor"),
        (4, (-0.2, 0.06, 0.84), (0.2, 0.46, 0.9), "frame"),
        (4, (-0.26, 0.08, -0.262), (0.26, 0.2, -0.25), "frame"),
        (3, (0.22, 0.84, 0.66), (0.28, 0.88, 0.72), "light"),
        (5, (-0.17, 0.5, 0.49), (0.17, 0.54, 0.87), "light"),
        (5, (-0.17, 0.66, 0.49), (0.17, 0.7, 0.87), "light"),
        (5, (-0.16, 0.62, 0.17), (0.16, 0.635, 0.18), "light"),
    ],
)

SAW = dict(
    file="Gathering/SawModule", name="Saw Module", section="Saws", mass=260, health=400, armour=6, power=900, attach=4.0,
    colour="#5f6b5a", tints=MODULE_COLOURS, sockets=MODULE_SOCKETS, bulk=MODULE_BIN, capacity=400,
    description="Cuts trees: fit it to a vehicle or a base, give it power, and bring its blade to a forest. The wood lands in its bin; F takes a load, and a chute carries it on.",
    boxes=wall_bin(0.7) + [box((-0.09, 0.2, 0.5), (0.09, 0.36, 1.0), "frame"), box((-0.5, 0.24, 1.0), (0.5, 0.32, 1.36), "edge")],
    gather=dict(kind="saw", rate=12, min=[-0.7, 0.1, 0.55], max=[0.7, 1.5, 2.2]),
    extras=[
        (2, (-0.46, 0.32, 1.0), (0.46, 0.355, 1.12), "dark"),
        (2, (-0.12, 0.36, 0.6), (0.12, 0.56, 0.88), "motor"),
        (2, *RIM[0], "accent"),
        (2, *RIM[1], "accent"),
        (3, (-0.3, 0.22, 0.5), (-0.09, 0.34, 0.7), "frame"),
        (3, (0.09, 0.22, 0.5), (0.3, 0.34, 0.7), "frame"),
        (3, (-0.48, 0.26, 1.36), (0.48, 0.3, 1.4), "edge"),
        (3, (-0.44, 0.38, 0.5), (-0.13, 0.62, 0.512), "accent"),
        (3, (0.13, 0.38, 0.5), (0.44, 0.62, 0.512), "accent"),
        (4, (-0.52, 0.355, 1.0), (0.52, 0.39, 1.3), "frame"),
        (4, (-0.14, 0.56, 0.58), (0.14, 0.62, 0.9), "frame"),
        (3, (0.16, 0.42, 0.72), (0.22, 0.46, 0.78), "light"),
        (5, (-0.46, 0.39, 1.1), (0.46, 0.405, 1.26), "light"),
        (5, (-0.1, 0.19, 0.74), (0.1, 0.37, 0.78), "light"),
        (5, (-0.42, 0.735, 0.46), (0.42, 0.76, 0.48), "light"),
    ],
)

SAWMILL = dict(
    file="Refining/Sawmill", name="Sawmill", section="Sawmills", mass=240, health=500, armour=5, power=700, attach=4.0,
    colour="#4f5258", tints=dict(body="#4f5258", trim="#7a5a38", accent="#e0b020", motor="#55705a", dark="#35363a", edge="#c8ccd2"),
    sockets=[
        {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
        {"name": "+x", "at": [0.5, 0.3, -0.3], "dir": "+x"},
        {"name": "-x", "at": [-0.5, 0.3, -0.3], "dir": "-x"},
        {"name": "back", "at": [0, 0.3, -0.5], "dir": "-z"},
    ],
    bulk=dict(min=[-0.435, 0.085, 0.165], max=[0.435, 0.44, 0.435]), capacity=400,
    description="Saws wood into planks. A chute (or F) fills the trough at the back; planks land in the rack at the front, and a chute from the Sawmill (or F) takes them away. Needs power while it works (battery and links); instant and unpowered in creative. A wire into Run holds it off.",
    boxes=[
        box((-0.5, 0, -0.5), (0.5, 0.08, 0.5)),
        box((-0.5, 0.08, -0.5), (0.5, 0.45, -0.44), "trim"), box((-0.5, 0.08, -0.16), (0.5, 0.45, -0.1), "trim"),
        box((-0.5, 0.08, -0.44), (-0.44, 0.45, -0.16), "trim"), box((0.44, 0.08, -0.44), (0.5, 0.45, -0.16), "trim"),
        box((-0.5, 0.08, 0.44), (0.5, 0.45, 0.5), "trim"), box((-0.5, 0.08, 0.1), (0.5, 0.45, 0.16), "trim"),
        box((-0.5, 0.08, 0.16), (-0.44, 0.45, 0.44), "trim"), box((0.44, 0.08, 0.16), (0.5, 0.45, 0.44), "trim"),
        box((-0.18, 0.08, -0.1), (-0.12, 1.05, 0.1)), box((0.12, 0.08, -0.1), (0.18, 1.05, 0.1)),
        box((-0.2, 1.05, -0.16), (0.2, 1.12, 0.16), "accent"),
        box((0.18, 0.6, -0.12), (0.46, 0.88, 0.12), "motor"),
    ],
    details=[
        box((-0.12, 0.72, -0.03), (0.12, 0.78, 0.03), "dark"),
        box((-0.015, 0.52, -0.28), (0.015, 0.98, 0.28), "edge"),
        box((-0.018, 0.46, -0.18), (0.018, 1.03, 0.18), "edge"),
    ],
    refine=dict(machine="sawmill", capacity=200, min=[-0.435, 0.085, -0.435], max=[0.435, 0.44, -0.165]),
    extras=[
        (2, (-0.06, 1.12, -0.24), (0.06, 1.2, 0.24), "dark"),
        (2, (-0.46, 0.46, -0.32), (0.46, 0.5, -0.28), "dark"),
        (2, (0.46, 0.62, -0.08), (0.49, 0.86, 0.08), "frame"),
        (3, (-0.46, 0.6, -0.12), (-0.18, 0.88, 0.12), "motor"),
        (3, (-0.21, 1.05, -0.2), (0.21, 1.09, 0.2), "accent"),
        (3, (-0.46, 0.46, 0.28), (0.46, 0.5, 0.32), "dark"),
        (3, (-0.49, 0.62, -0.08), (-0.46, 0.86, 0.08), "frame"),
        (4, (-0.24, 1.2, -0.3), (0.24, 1.26, 0.3), "frame"),
        (4, (-0.24, 0.5, -0.3), (-0.2, 1.2, -0.26), "frame"),
        (4, (0.2, 0.5, -0.3), (0.24, 1.2, -0.26), "frame"),
        (3, (0.3, 0.88, -0.03), (0.36, 0.92, 0.03), "light"),
        (5, (-0.197, 0.46, -0.1), (-0.185, 1.03, 0.1), "light"),
        (5, (0.185, 0.46, -0.1), (0.197, 1.03, 0.1), "light"),
        (5, (-0.42, 0.45, -0.5), (0.42, 0.47, -0.45), "light"),
        (5, (-0.42, 0.45, 0.45), (0.42, 0.47, 0.5), "light"),
    ],
)

SMELTER = dict(
    file="Refining/Smelter", name="Smelter", section="Smelters", mass=420, health=800, armour=10, power=1200, attach=5.0,
    colour="#4a4f57", tints=dict(body="#4a4f57", core="#7a4432", frame="#7a8592", dark="#35363a", heat="#ff7a1f"),
    sockets=[
        {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
        {"name": "+x", "at": [0.5, 0.3, -0.2], "dir": "+x"},
        {"name": "-x", "at": [-0.5, 0.3, -0.2], "dir": "-x"},
        {"name": "back", "at": [0, 0.3, -0.5], "dir": "-z"},
    ],
    bulk=dict(min=[-0.435, 0.105, 0.105], max=[0.435, 0.38, 0.435]), capacity=400,
    description="Smelts ore to metal, copper and gold, and sand to glass. A chute (or F) fills the hopper on top; what it makes lands in the tray at the front, and a chute from the Smelter (or F) takes it away. The input picks the recipe. Needs power while it works (battery and links); instant and unpowered in creative. A wire into Run holds it off.",
    boxes=[
        box((-0.5, 0, -0.5), (0.5, 0.1, 0.5)),
        box((-0.5, 0.1, -0.5), (0.5, 0.9, 0.1), "core"),
        box((-0.5, 0.1, 0.44), (0.5, 0.4, 0.5)),
        box((-0.5, 0.1, 0.1), (-0.44, 0.4, 0.44)), box((0.44, 0.1, 0.1), (0.5, 0.4, 0.44)),
        box((-0.46, 0.9, -0.44), (0.1, 1.24, -0.38), "frame"), box((-0.46, 0.9, 0.02), (0.1, 1.24, 0.08), "frame"),
        box((-0.46, 0.9, -0.38), (-0.4, 1.24, 0.02), "frame"), box((0.04, 0.9, -0.38), (0.1, 1.24, 0.02), "frame"),
        box((0.18, 0.9, -0.34), (0.42, 1.85, -0.1), "dark"),
        box((0.15, 1.85, -0.37), (0.45, 1.95, -0.07), "body"),
    ],
    details=[box((-0.3, 0.45, 0.1), (0.3, 0.75, 0.13), "heat")],
    refine=dict(machine="smelter", capacity=200, min=[-0.395, 0.905, -0.375], max=[0.035, 1.2, 0.015]),
    extras=[
        (2, (-0.51, 0.72, -0.51), (0.51, 0.78, 0.11), "frame"),
        (2, (0.16, 1.3, -0.36), (0.44, 1.36, -0.08), "frame"),
        (2, (-0.33, 0.42, 0.1), (0.33, 0.45, 0.12), "frame"),
        (3, (-0.51, 0.22, -0.51), (0.51, 0.28, 0.11), "accent"),
        (3, (0.16, 1.6, -0.36), (0.44, 1.66, -0.08), "accent"),
        (3, (-0.33, 0.75, 0.1), (0.33, 0.78, 0.12), "frame"),
        (3, (0.1, 1.0, -0.26), (0.18, 1.08, -0.18), "motor"),
        (4, (-0.5, 0.42, 0.1), (-0.33, 0.8, 0.14), "frame"),
        (4, (0.33, 0.42, 0.1), (0.5, 0.8, 0.14), "frame"),
        (4, (-0.46, 1.24, -0.44), (0.1, 1.28, -0.38), "accent"),
        (3, (0.38, 0.8, 0.1), (0.44, 0.86, 0.14), "light"),
        (5, (-0.511, 0.44, -0.511), (-0.5, 0.48, 0.11), "light"),
        (5, (0.5, 0.44, -0.511), (0.511, 0.48, 0.11), "light"),
        (5, (0.14, 1.95, -0.38), (0.46, 1.98, -0.06), "accent"),
        (5, (0.15, 1.1, -0.35), (0.18, 1.8, -0.31), "light"),
    ],
)

# What a low tier can't work yet (Constructs/Resources.json and Refining.json: "minTier").
DRILL["gated"] = {1: "Copper needs tier 2 and gold tier 3.", 2: "Gold needs tier 3."}
SMELTER["gated"] = {1: "Copper ore needs tier 2 and gold ore tier 3.", 2: "Gold ore needs tier 3."}

LINES = [DRILL, SAW, SAWMILL, SMELTER]


def num(v):
    v = round(v + 0.0, 4)
    return int(v) if float(v).is_integer() else v


def vec(v):
    return [num(c) for c in v]


def scaled_point(v, size):
    """A point at a tier: x and z grow; y grows above the floor, and keeps tier 1's value below it (a bit's reach)."""
    return [v[0] * size, v[1] * size if v[1] > 0 else v[1], v[2] * size]


def on_grid(v):
    return int(v / GRID + (0.5 if v >= 0 else -0.5)) * GRID


def out_to_grid(v):
    """The next grid line out from a face."""
    n = abs(v) / GRID
    whole = int(n)
    return (whole if n - whole < 1e-6 else whole + 1) * GRID * (1 if v >= 0 else -1)


def scaled_sockets(line, tier):
    """The sockets at a tier, on the grid, and the solid pads that hold any the body doesn't reach."""
    if tier == 1:
        return line["sockets"], []
    size = SIZE[tier]
    out, pads = [], []
    for sk in line["sockets"]:
        at = scaled_point(sk["at"], size)
        axis = "xyz".index(sk["dir"][1])
        if sk["name"] == "bottom":
            out.append(dict(sk, at=vec(at)))
            continue
        face = at[axis]
        snapped = [on_grid(c) for c in at]
        snapped[axis] = out_to_grid(face)
        out.append(dict(sk, at=vec(snapped)))
        if abs(snapped[axis] - face) > 1e-6:
            lo, hi = [c - 0.15 for c in snapped], [c + 0.15 for c in snapped]
            if sk["dir"][0] == "+":
                lo[axis] = face - 0.03
                hi[axis] = snapped[axis]
            else:
                lo[axis] = snapped[axis]
                hi[axis] = face + 0.03
            pads.append(box(lo, hi, "frame"))
    return out, pads


def colour_of(line, tier, role):
    if tier == 1:
        return line["tints"].get(role) or line["tints"]["body"]
    return PALETTE[tier][role]


def built_box(line, tier, b, detail=False):
    role = b["role"]
    out = {"min": vec(b["min"]), "max": vec(b["max"])}
    default = line["colour"] if tier == 1 else PALETTE[tier]["body"]
    colour = colour_of(line, tier, role)
    if colour != default:
        out["color"] = colour
    if detail:
        out["detail"] = True
    if role in GLOWS.get(tier, ()):
        out["glow"] = True
        out["paintable"] = False
    return out


def part(line, tier):
    rate, power, cap, mass, health, armour, attach = CURVE[tier]
    size = SIZE[tier]
    sockets, pads = scaled_sockets(line, tier)

    def grown(b):
        return box(scaled_point(b["min"], size), scaled_point(b["max"], size), b["role"])

    boxes = [built_box(line, tier, grown(b)) for b in line["boxes"]]
    boxes += [built_box(line, tier, b) for b in pads]
    boxes += [built_box(line, tier, grown(b), True) for b in line.get("details", [])]
    for t, mn, mx, role in line["extras"]:
        if tier >= t:
            boxes.append(built_box(line, tier, grown(box(mn, mx, role)), True))
    zfight.resolve(boxes, sockets)
    boxes = [{**b, "min": vec(b["min"]), "max": vec(b["max"])} for b in boxes]
    head = f"{TIERS[tier]}, tier {tier} of 5. "
    if "gated" in line and tier < 3:
        head += line["gated"][tier] + " "
    out = {
        "format": FORMAT,
        "name": PREFIX[tier] + line["name"],
        "description": head + line["description"].replace("the Sawmill", "the mill").replace("the Smelter", "the furnace"),
        "group": GROUP,
        "section": line["section"],
        "tier": tier,
        "role": "storage",
        "tool": "welder",
        "attachSeconds": num(line["attach"] * attach),
        "mass": num(line["mass"] * mass),
        "health": num(line["health"] * health),
        "armour": num(line["armour"] * armour),
        "material": "steel",
        "power": num(line["power"] * power),
        "color": line["colour"] if tier == 1 else PALETTE[tier]["body"],
        "boxes": boxes,
        "sockets": sockets,
    }

    def fill(b):
        return dict(min=vec(scaled_point(b["min"], size)), max=vec(scaled_point(b["max"], size)))

    bulk = {"material": "any", "capacity": num(line["capacity"] * cap)}
    if tier > 1:
        bulk["chuteLoad"] = 10 * rate
    storage = {"bulk": {**bulk, **fill(line["bulk"])}}
    if "gather" in line:
        g = line["gather"]
        storage["gather"] = {"kind": g["kind"], "rate": g["rate"] * rate, **fill(g)}
        if line is DRILL:
            storage["gather"]["ratedRpm"], storage["gather"]["shaftTorque"] = DRILL_SHAFT[tier]
    else:
        f = line["refine"]
        storage["refine"] = {"machine": f["machine"], "capacity": num(f["capacity"] * cap)}
        if tier > 1:
            storage["refine"]["speed"] = rate
        storage["refine"].update(fill(f))
    if "ports" in line:
        out["ports"] = [dict(p, at=vec(scaled_point(p["at"], size))) for p in line["ports"]]
    out["storage"] = storage
    return out


def one(v):
    return json.dumps(v, ensure_ascii=True)


def dump(p):
    """One key a line, one box, socket and bin a line."""
    lines = []
    for k, v in p.items():
        if k in ("boxes", "sockets", "ports"):
            lines.append(f'  "{k}": [\n' + ",\n".join("    " + one(i) for i in v) + "\n  ]")
        elif k == "storage":
            lines.append('  "storage": {\n' + ",\n".join(f'    "{n}": {one(b)}' for n, b in v.items()) + "\n  }")
        else:
            lines.append(f'  "{k}": {one(v)}')
    return "{\n" + ",\n".join(lines) + "\n}\n"


def path(line, tier):
    return os.path.join("Constructs", "Parts", line["file"] + (f"T{tier}" if tier > 1 else "") + ".json")


def main():
    check = "--check" in sys.argv
    bad = 0
    for line in LINES:
        for tier in range(1, 6):
            p = part(line, tier)
            left = zfight.hits(p["boxes"])
            if left:
                print(f"z-fight left in {p['name']}: {left}")
                bad += 1
            thin = [b for b in p["boxes"] if min(hi - lo for lo, hi in zip(b["min"], b["max"])) < 0.0105]
            if thin:
                print(f"thinner than 1 cm in {p['name']}: {thin}")
                bad += 1
            if len(p["boxes"]) > 32:
                print(f"too many boxes: {p['name']}")
                bad += 1
            text, file = dump(p), path(line, tier)
            if check:
                if not os.path.exists(file) or open(file, encoding="utf-8").read() != text:
                    print(f"out of date: {file}")
                    bad += 1
            else:
                with open(file, "w", encoding="utf-8", newline="\n") as f:
                    f.write(text)
    print("machine tiers: " + ("checked" if check else "written") + f", {bad} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
