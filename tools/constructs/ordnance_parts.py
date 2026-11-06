#!/usr/bin/env python3
"""Writes the sections of modular bombs and missiles, and the Ordnance Bench: Constructs/Parts/Ordnance. docs/sandbox/CONSTRUCTS.md, "Modular ordnance".

A section is a real part (made in survival, stored, carried) whose "ordnance" block is what it adds to a design. Its boxes run along Z, nose to -z, at the reference
diameter of 25 cm; the bench scales them. Run it from the repo root: python3 -I tools/constructs/ordnance_parts.py (--check only compares), then python3 -I tools/constructs/assembling.py
for their recipes. Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Ordnance")
R = 0.125  # m: the reference radius
GROUP = "Weapons"
OLIVE, DARK, STEEL, BRASS, RED, WHITE, GREY = "#4a5530", "#2a2c26", "#7c838c", "#c8a838", "#a83428", "#d8d8d0", "#565a5e"


def section(name, slot, length, mass, color, desc, band=None, who=None, fins=False, nose=False, **ordnance):
    """A round section, with a coloured band, fins or a tapered nose."""
    p = Part(name, SMALL, "Ordnance sections", desc, tool="wrench", color=color, group=GROUP)
    z = length / 2
    p.add(box((-R, -R, -z), (R, R, z), hidden=True))
    if nose:
        p.add(box((-R, -R, -z), (R, R, z), color, detail=True, shape="cylinder", segments=16, taper=[0.2, 0.2]))
    else:
        p.add(box((-R, -R, -z), (R, R, z), color, detail=True, shape="cylinder", segments=16))
    if band:
        p.add(box((-R * 1.01, -R * 1.01, -z * 0.5), (R * 1.01, R * 1.01, -z * 0.3), band, detail=True, shape="cylinder", segments=16))
    if fins:
        p.add(box((-0.012, -R * 1.9, -z * 0.8), (0.012, R * 1.9, z * 0.8), DARK, detail=True))
        p.add(box((-R * 1.9, -0.012, -z * 0.79), (R * 1.9, 0.012, z * 0.79), DARK, detail=True))
    p.sock("-z", (0, 0, -z), "-z")
    p.sock("+z", (0, 0, z), "+z")
    p.stats(mass, 40, 1, 1.0)
    block = {"slot": slot, "length": length}
    if who:
        block["for"] = who
    numbers = {k: v for k, v in ordnance.items() if k not in TOP}
    block.update({k: v for k, v in ordnance.items() if k in TOP})
    if numbers:
        block["numbers"] = numbers
    p.set(ordnance=block)
    return "Sections", p


TOP = ("payload", "blast", "damage", "crater", "fuseDelay", "settings", "kind")  # (the rest are the flight's numbers)
DELAY = {"key": "delay", "label": "Delay", "unit": "s", "min": 0.05, "max": 30, "step": 0.05, "default": 1}


def stage1():
    return [
        section("Impact Fuse", "fuse", 0.2, 3, GREY, "Goes off the moment it hits.", nose=True, fuseDelay=0.05),
        section("Delay Fuse", "fuse", 0.25, 4, GREY, "Goes off a set time after it hits: through a deck first.", band=BRASS, nose=True, settings=[DELAY]),
        section("Warhead HE", "payload", 0.5, 25, OLIVE, "25 kg of high explosive. Stack as many as the airframe can lift.", band=BRASS, payload=20),
        section("Warhead Blast", "payload", 0.6, 28, "#5a4a30", "Thin-cased: a wider blast for its weight, and less damage.", band=RED, payload=22, blast=1.5, damage=0.6),
        section("Warhead Bunker", "payload", 0.6, 45, "#3a3d40", "A thick steel case: a deep crater.", band=WHITE, payload=20, damage=1.2, crater=2.2),
        section("Bomb Tail Fins", "stabilizer", 0.4, 6, DARK, "Keeps a bomb nose down.", who="bomb", fins=True, drag=0.2),
    ]


def bench():
    p = Part("Ordnance Bench", SMALL, "Workbenches", "F opens it: design a bomb or a missile from sections, and build it here.", role="storage", tool="welder", color="#4a4f45", group="Weapons")
    p.add(box((-1, 0, -0.5), (1, 0.9, 0.5)))
    p.add(box((-0.95, 0.9, -0.45), (0.95, 0.96, 0.45), OLIVE, detail=True))
    p.add(box((-1, 0.9, 0.44), (1, 1.6, 0.5), "#3a3f38"))
    for k in range(3):
        x = -0.6 + 0.6 * k
        p.add(box((x - 0.1, 0.96, -0.3), (x + 0.1, 1.02, 0.3), BRASS, detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.stats(180, 900, 3, 4.0)
    slots = [{"min": [-0.9 + 0.6 * k, 1.0, -0.4], "max": [-0.4 + 0.6 * k, 1.5, 0.4], "stack": 12, "label": f"Bay {k + 1}"} for k in range(3)]
    p.set(storage={"ordnanceBench": True, "slots": slots})
    return "", p


def parts():
    return [bench()] + stage1()


def main():
    return write_parts(OUT, parts())


if __name__ == "__main__":
    sys.exit(main())
