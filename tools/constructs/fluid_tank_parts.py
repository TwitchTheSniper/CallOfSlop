#!/usr/bin/env python3
"""Writes the Fluid Tanks: Constructs/Parts/FluidTanks. docs/sandbox/CONSTRUCTS.md, "Fluid tanks".

One part kind in the fuel tanks' six sizes, with the same bodies and capacities: a tank that holds any one fluid, a pipe's
input and output. And the Tank Volume, one block with a box the configure tool sizes. Run it from the repo root: python3 -I tools/constructs/fluid_tank_parts.py (--check only compares), then
python3 -I tools/constructs/assembling.py for their recipes. The JSON files are what the game loads: fix this, not them.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from big_drive_parts import TANKS, bands, block, faces, half, health  # noqa: E402
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "FluidTanks")
SECTION = "Fluid tanks"
BODY = "#3f6f86"
BAND = "#2c5062"
STEEL = "#8a9099"
ABOUT = "A pipe's input and output for any one fluid: empty, it takes what arrives first."
CONFIG = [
    {"key": "fluid", "label": "Fluid", "group": "Contents", "creative": True,
     "tooltip": "What the tank holds. An empty tank takes whatever a pipe brings first."},
    {"key": "fill", "label": "Fill", "group": "Contents", "unit": "%", "min": 0, "max": 100, "default": 0, "creative": True,
     "tooltip": "How full it is. Pipes and burners move this as they run."},
]
# Small sizes: (name, width, height, length, litres, mass kg, health, armour, litres a second through a pipe), the fuel tanks' bodies.
TANK_SIZES = [
    ("Small Fluid Tank", 0.4, 0.3, 0.4, 15, 14, 60, 1, 5),
    ("Fluid Tank", 0.5, 0.5, 0.5, 40, 30, 90, 2, 10),
    ("Large Fluid Tank", 0.5, 0.5, 1.0, 150, 70, 180, 4, 25),
]
FLOW = {MEDIUM: 500, LARGE: 5000, CAPITAL: 20000}
SIDE = 40  # a box's longest side, metres (ConstructStoreVolume.MaxSide)
BOX = [
    {"key": "sizeX", "label": "Width", "group": "Box", "unit": "m", "min": 0.2, "max": SIDE, "step": 0.1, "default": 2, "tooltip": "The box's size across the block (its x)."},
    {"key": "sizeY", "label": "Height", "group": "Box", "unit": "m", "min": 0.2, "max": SIDE, "step": 0.1, "default": 2, "tooltip": "The box's size up from the block (its y). The fluid rises along it."},
    {"key": "sizeZ", "label": "Length", "group": "Box", "unit": "m", "min": 0.2, "max": SIDE, "step": 0.1, "default": 2, "tooltip": "The box's size along the block (its z)."},
    {"key": "atX", "label": "Centre x", "group": "Box", "unit": "m", "min": -SIDE, "max": SIDE, "step": 0.05, "default": 0, "tooltip": "Where the box's centre is from the block (x)."},
    {"key": "atY", "label": "Centre y", "group": "Box", "unit": "m", "min": -SIDE, "max": SIDE, "step": 0.05, "default": 1.2, "tooltip": "Where the box's centre is from the block (y; above it by default)."},
    {"key": "atZ", "label": "Centre z", "group": "Box", "unit": "m", "min": -SIDE, "max": SIDE, "step": 0.05, "default": 0, "tooltip": "Where the box's centre is from the block (z)."},
]


def stubs(p, x, y, z, unit):
    """Where the two pipes leave: a stub on the side (out) and one on the top (in), and a filler cap."""
    s = 0.05 * unit
    t = 0.05 * unit
    p.add(box((x, -s, -s), (x + t, s, s), STEEL, detail=True))
    p.add(box((-s, y, -s), (s, y + t, s), STEEL, detail=True))
    p.add(box((0.4 * x - s, y, 0.6 * z - s), (0.4 * x + s, y + 0.8 * t, 0.6 * z + s), "#d8d8d8", detail=True))


def ports(x, y):
    """A port for any fluid names none."""
    return [
        {"name": "Fluid out", "type": "pipe", "dir": "out", "at": [x, 0, 0], "face": "+x"},
        {"name": "Fluid in", "type": "pipe", "dir": "in", "at": [0, y, 0], "face": "+y"},
    ]


def tank_volume():
    """One block with the box's settings: it holds what the box holds, 1,000 l a cubic metre, and draws the fluid in the box."""
    p = Part("Tank Volume", SMALL, SECTION, "A box you size: one fluid, inside your walls. This block is its pipes' way in and out.", role="fluidtank", tool="wrench", color=BODY, group="Small")
    h = 0.2
    p.add(box((-h, -h, -h), (h, h, h)))
    p.add(box((-h - 0.01, -h - 0.01, -0.03), (h + 0.01, h + 0.01, 0.03), BAND, detail=True))
    p.add(box((h, -0.07, -0.07), (h + 0.05, 0.07, 0.07), "#c8863a", detail=True))
    p.add(box((-h - 0.05, -0.07, -0.07), (-h, 0.07, 0.07), "#4f9f5a", detail=True))
    for d, at in (("+x", (h, 0, 0)), ("-x", (-h, 0, 0)), ("+y", (0, h, 0)), ("-y", (0, -h, 0)), ("+z", (0, 0, h)), ("-z", (0, 0, -h))):
        p.sock(d, at, d)
    p.stats(14, 400, 5, 2.0)
    p.set(fluidTank={"volume": True, "flow": 5000}, config=BOX + CONFIG, ports=[
        {"name": "Fluid out", "type": "pipe", "dir": "out", "at": [h, 0, 0], "face": "+x"},
        {"name": "Fluid in", "type": "pipe", "dir": "in", "at": [-h, 0, 0], "face": "-x"},
    ])
    return ("", p)


def tanks():
    out = [tank_volume()]
    for name, w, h, l, litres, mass, hp, armour, flow in TANK_SIZES:
        p = Part(name, SMALL, SECTION, f"Holds {litres} l. {ABOUT}", role="fluidtank", tool="wrench", color=BODY, group="Small")
        x, y, z = w / 2, h / 2, l / 2
        p.add(box((-x, -y, -z), (x, y, z)))
        p.add(box((-x - 0.01, -y - 0.01, -0.03), (x + 0.01, y + 0.01, 0.03), BAND, detail=True))
        stubs(p, x, y, z, 1)
        for d, at in (("+x", (x, 0, 0)), ("-x", (-x, 0, 0)), ("+y", (0, y, 0)), ("-y", (0, -y, 0)), ("+z", (0, 0, z)), ("-z", (0, 0, -z))):
            p.sock(d, at, d)
        p.stats(mass, hp, armour, 2.0)
        p.set(fluidTank={"capacity": litres, "flow": flow}, ports=ports(x, y), config=CONFIG)
        out.append(("", p))
    for c in (MEDIUM, LARGE, CAPITAL):
        n, t, litres = TANKS[c]
        p = Part(f"{c.name} Fluid Bunker", c, SECTION, f"Holds {litres / 1000:g} kl. {ABOUT}", role="fluidtank", tool="wrench", color=BODY)
        block(p, n, n, n)
        bands(p, n, n, n, BAND, 2)
        x = half(n, c)
        stubs(p, x, x, x, c.x)
        faces(p, n, n, n)
        p.stats(t * 1000, health(t), c.armour // 2)
        p.set(fluidTank={"capacity": litres, "flow": FLOW[c]}, ports=ports(x, x), config=CONFIG)
        out.append(("", p))
    return out


def main():
    return write_parts(OUT, tanks())


if __name__ == "__main__":
    sys.exit(main())
