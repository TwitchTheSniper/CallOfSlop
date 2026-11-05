#!/usr/bin/env python3
"""Writes the IT Department's parts: Constructs/Parts/Office (docs/vehiclekit/VEHICLEKIT.md, "The IT style").

What the IT Department bolts on where bandits hang skulls and spikes: a beige monitor on a stalk, a potted plant, a desk fan and an
Employee of the Month plaque as trophies, and router antennas down the flanks as spikes. Each stands on one "base" socket and points
up its own +y, so the kit turns it outward, as the bandit parts do (tools/constructs/bandit_parts.py). Leaves and soil aren't
paintable; the rest takes the style's paint. Faces that meet are kept a few millimetres apart (CLAUDE.md, "No coplanar faces").
Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/office_parts.py (--check only compares).
Boxes go through zfight.resolve. Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Office")
BEIGE, BEIGE_DARK, SCREEN, GREY, DARK = "#d8d0b8", "#b8ae94", "#1d2a33", "#8e9296", "#2c2e31"
POT, SOIL, LEAF, LEAF_DARK = "#b85c38", "#3a2a1e", "#3f8a3a", "#2e6b2c"
GOLD, BLUE = "#c8a018", "#1f4e8c"
SECTION = "Body panels"
GROUP = "Lights and Details"


def base_socket(p):
    p.sock("base", (0, 0, 0), "-y")


def stalk(p, top, colour=GREY):
    """A square post from the base up to top, under whatever it holds."""
    p.add(box((-0.02, 0, -0.02), (0.02, top, 0.02), colour))


def monitor():
    p = Part("CRT Monitor", SMALL, SECTION, "A beige monitor on a pole. Still logged in to something important.", color=BEIGE, group=GROUP)
    top = 0.18
    stalk(p, top)
    p.add(box((-0.07, top, -0.06), (0.07, top + 0.02, 0.06), BEIGE_DARK))  # (the foot)
    p.add(box((-0.17, top + 0.02, -0.12), (0.17, top + 0.3, 0.14), BEIGE))  # (the case: deep, as they were)
    p.add(box((-0.14, top + 0.05, -0.124), (0.14, top + 0.27, -0.1), SCREEN, detail=True, paintable=False))  # (the glass, 4 mm proud of the case's face)
    p.add(box((0.1, top + 0.03, -0.126), (0.13, top + 0.045, -0.11), "#3fd04a", detail=True, paintable=False))  # (the power light)
    base_socket(p)
    p.stats(9, 30, 1, 0.5)
    return "", p


def plant():
    p = Part("Office Plant", SMALL, SECTION, "A potted plant nobody waters. It has outlived three reorganisations.", color=POT, group=GROUP)
    p.add(box((-0.11, 0, -0.11), (0.11, 0.22, 0.11), hidden=True))
    p.add(box((-0.11, 0, -0.11), (0.11, 0.22, 0.11), POT, detail=True, shape="cylinder", segments=12, taper=[0.8, 0.8], front="+y"))
    p.add(box((-0.095, 0.2, -0.095), (0.095, 0.225, 0.095), SOIL, detail=True, shape="cylinder", segments=12, front="+y", paintable=False))
    p.add(box((-0.16, 0.24, -0.16), (0.16, 0.52, 0.16), LEAF, detail=True, shape="sphere", segments=10, paintable=False))
    p.add(box((-0.1, 0.44, -0.12), (0.12, 0.64, 0.1), LEAF_DARK, detail=True, shape="sphere", segments=8, paintable=False))
    base_socket(p)
    p.stats(5, 20, 0, 0.5)
    return "", p


def fan():
    p = Part("Desk Fan", SMALL, SECTION, "A desk fan on a stalk. Moves the air, not the deadline.", color=BEIGE, group=GROUP)
    top = 0.22
    stalk(p, top)
    p.add(box((-0.05, top, -0.05), (0.05, top + 0.06, 0.06), BEIGE_DARK))  # (the motor)
    p.add(box((-0.15, top - 0.09, -0.08), (0.15, top + 0.21, -0.05), GREY, detail=True, shape="cylinder", segments=12, front="-z"))  # (the guard: 12 sides, so it gets a hull; 16 keep their boxes)
    p.add(box((-0.03, top + 0.03, -0.085), (0.03, top + 0.09, -0.078), DARK, detail=True, shape="cylinder", segments=8, front="-z"))  # (the hub, in front of the guard)
    base_socket(p)
    p.stats(3, 20, 0, 0.5)
    return "", p


def plaque():
    p = Part("Employee of the Month", SMALL, SECTION, "A gold plaque on a post. Everyone has had it twice.", color=GOLD, group=GROUP)
    top = 0.25
    stalk(p, top, DARK)
    p.add(box((-0.16, top, -0.03), (0.16, top + 0.22, 0.0), "#5a3a22"))  # (the board)
    p.add(box((-0.12, top + 0.03, -0.034), (0.12, top + 0.19, -0.03), GOLD, detail=True))  # (the plate, 4 mm proud)
    p.add(box((-0.08, top + 0.13, -0.038), (0.08, top + 0.16, -0.034), BLUE, detail=True, paintable=False))  # (the name, in the house blue)
    base_socket(p)
    p.stats(3, 25, 1, 0.5)
    return "", p


def antenna(name, length, desc, mass):
    p = Part(name, SMALL, SECTION, desc, color=DARK, group=GROUP)
    r = 0.016
    p.add(box((-0.05, 0, -0.035), (0.05, 0.04, 0.035), DARK))  # (its foot)
    p.add(box((-r, 0.04, -r), (r, length, r), hidden=True))
    p.add(box((-r, 0.04, -r), (r, length, r), DARK, detail=True, shape="cylinder", segments=8, taper=[0.7, 0.7], front="+y"))
    p.add(box((-0.012, 0.012, -0.039), (0.012, 0.024, -0.035), "#3fd04a", detail=True, paintable=False))  # (the link light, on the foot's face)
    base_socket(p)
    p.stats(mass, 25, 1, 0.5)
    return "", p


def main():
    return write_parts(OUT, [
        monitor(),
        plant(),
        fan(),
        plaque(),
        antenna("Router Antenna", 0.3, "A router's antenna, bolted on. Full bars, no internet.", 0.5),
        antenna("Wi-Fi Mast", 0.55, "A tall Wi-Fi antenna. The password is on a sticky note somewhere.", 1),
    ])


if __name__ == "__main__":
    sys.exit(main())
