#!/usr/bin/env python3
"""Writes the bandit vehicles' parts: Constructs/Parts/Bandit (docs/vehiclekit/VEHICLEKIT.md).

The fuel drum a technical carries on its bed, and what bandits bolt on for show: spikes, skulls and a hubcap. A spike and a trophy stand on one
"base" socket and point up their own +y, so the kit turns them outward. A drum and a spike are round: a hidden solid box for the collider
and the build rules, a cylinder drawn over it, as Steel Tube does. Bone isn't paintable; the rest takes war paint.
Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/bandit_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Bandit")
IRON, DARK, BONE, BONE_DARK, HORN, DRUM, HOOP = "#5a5048", "#2c2a28", "#d8cdb0", "#7a6f58", "#3a3026", "#6e3f26", "#4e3a2a"
SECTION = "Body panels"
GROUP = "Lights and Details"


def base_socket(p):
    p.sock("base", (0, 0, 0), "-y")


def spike(name, length, radius, mass):
    p = Part(name, SMALL, SECTION, "A sharpened steel stake. It looks worse than it hits.", color=IRON, group=GROUP)
    p.add(box((-radius, 0, -radius), (radius, length, radius), hidden=True))
    p.add(box((-radius, 0, -radius), (radius, length, radius), IRON, detail=True, shape="cylinder", segments=8, taper=[0.96, 0.96], front="+y"))
    p.add(box((-radius * 1.5, 0.002, -radius * 1.5), (radius * 1.5, 0.022, radius * 1.5), DARK, detail=True, shape="cylinder", segments=8, front="+y"))
    base_socket(p)
    p.stats(mass, 45, 4, 0.5)
    return "", p


def skull_at(p, y):
    """A skull's dome, jaw and sockets, its face towards -z, sitting at height y."""
    p.add(box((-0.075, y + 0.03, -0.09), (0.075, y + 0.17, 0.09), BONE, detail=True, shape="sphere", segments=10, paintable=False))
    p.add(box((-0.05, y, -0.085), (0.05, y + 0.06, -0.01), BONE, detail=True, paintable=False))
    for x in (-0.034, 0.034):
        p.add(box((x - 0.018, y + 0.082, -0.096), (x + 0.018, y + 0.112, -0.05), DARK, detail=True, paintable=False))
    p.add(box((-0.01, y + 0.056, -0.094), (0.01, y + 0.076, -0.06), DARK, detail=True, paintable=False))


def skull(name, desc, horns, mass):
    p = Part(name, SMALL, SECTION, desc, color=BONE, group=GROUP)
    top = 0.2
    p.add(box((-0.02, 0, -0.02), (0.02, top, 0.02), IRON))
    p.add(box((-0.05, top, -0.04), (0.05, top + 0.14, 0.05), hidden=True))
    skull_at(p, top)
    if horns:
        for s in (-1, 1):
            lo, hi = sorted((s * 0.07, s * 0.2))
            p.add(box((lo, top + 0.09, -0.025), (hi, top + 0.135, 0.02), HORN, detail=True, paintable=False))
            lo2, hi2 = sorted((s * 0.17, s * 0.215))
            p.add(box((lo2, top + 0.13, -0.025), (hi2, top + 0.27, 0.02), HORN, detail=True, paintable=False))
    base_socket(p)
    p.stats(mass, 30, 1, 0.5)
    return "", p


def hubcap():
    p = Part("Hubcap Plate", SMALL, SECTION, "A stolen hubcap. Keeps the rain off nothing.", color="#9aa0a6", group=GROUP)
    r = 0.2
    p.add(box((-r, 0, -r), (r, 0.03, r), hidden=True))
    p.add(box((-r, 0, -r), (r, 0.03, r), "#9aa0a6", detail=True, shape="cylinder", segments=16, front="+y"))
    p.add(box((-0.08, 0.03, -0.08), (0.08, 0.05, 0.08), DARK, detail=True, shape="cylinder", segments=12, front="+y"))
    base_socket(p)
    p.stats(2, 25, 1, 0.5)
    return "", p


def ladder():
    """A short ladder for a floor over small wheels: a Boarding Ladder's rig, 0.8 m long so it clears the ground."""
    p = Part("Scrap Ladder", SMALL, "Boarding", "A short ladder of welded rebar. Climb aboard from outside.", role="panel", tool="wrench", color="#6a5a48", group="Panels")
    for s in (-1, 1):
        lo, hi = sorted((s * 0.21, s * 0.25))
        p.add(box((lo, -0.8, -0.08), (hi, 0, -0.04)))
    for y in (-0.15, -0.45, -0.72):
        p.add(box((-0.21, y - 0.015, -0.075), (0.21, y + 0.015, -0.045)))
    for s in (-1, 1):
        p.add(box((s * 0.23 - 0.02, -0.04, -0.04), (s * 0.23 + 0.02, 0, 0)))  # (its brackets: the socket's on their face)
    p.sock("back", (0, 0, 0), "+z")
    p.stats(6, 150, 3, 1.5)
    p.set(boarding=True)
    return "", p


def drum():
    p = Part("Fuel Drum", SMALL, "Fuel tanks", "Holds 60 l of fuel in thin steel, out in the open. Feeds an engine through a pipe. Burns well.",
             role="fueltank", tool="wrench", color=DRUM, group="Power and Fuel")
    r, h = 0.29, 0.44
    p.add(box((-r, -h, -r), (r, h, r), hidden=True))
    p.add(box((-r, -h, -r), (r, h, r), DRUM, detail=True, shape="cylinder", segments=14, front="+y"))  # (14 sides: 16 are too many corners for a hull, and a drum collides as its round shape)
    for y in (-0.16, 0.16):
        p.add(box((-r - 0.008, y - 0.015, -r - 0.008), (r + 0.008, y + 0.015, r + 0.008), HOOP, detail=True, shape="cylinder", segments=16, front="+y"))
    p.add(box((0.1, h, -0.04), (0.18, h + 0.03, 0.04), DARK, detail=True))
    p.add(box((-0.04, -0.34, -r - 0.03), (0.04, -0.26, -r + 0.01), DARK, detail=True))
    for name, at in (("+x", (r, 0, 0)), ("-x", (-r, 0, 0)), ("+y", (0, h, 0)), ("-y", (0, -h, 0)), ("+z", (0, 0, r)), ("-z", (0, 0, -r))):
        p.sock(name, at, name)
    p.stats(38, 55, 1, 2.0)
    p.set(ports=[
        {"name": "Fuel out", "type": "pipe", "dir": "out", "fluid": "fuel", "at": [0.0, -0.3, -r], "face": "-z"},
        {"name": "Fill", "type": "pipe", "dir": "in", "fluid": "fuel", "at": [0.14, h, 0.0], "face": "+y"},
    ], fuelTank={"capacity": 60})
    return "", p


def main():
    return write_parts(OUT, [
        drum(),
        ladder(),
        spike("Iron Spike", 0.35, 0.045, 1.5),
        spike("Long Iron Spike", 0.6, 0.05, 2.5),
        skull("Skull Trophy", "A skull on a stake. A warning, or a boast.", False, 2),
        skull("Horned Skull", "A horned skull on a stake. Somebody's bull, probably.", True, 3),
        hubcap(),
    ])


if __name__ == "__main__":
    sys.exit(main())
