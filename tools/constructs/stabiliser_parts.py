#!/usr/bin/env python3
"""Walker stabilisers (docs/sandbox/GIANTS.md, phase 4): gyro housings that steady a walker's ride.

    python3 tools/constructs/stabiliser_parts.py           writes Constructs/Parts/Stabilisers
    python3 tools/constructs/stabiliser_parts.py --check   compares them

A bare walker thumps, shakes and sways as it steps; each stabiliser on its body steadies `steady` kg of it, so the
footfalls, the sway and the tilt its riders feel, and how far loose parts on it are thrown, shrink with the count and size
of them (Source/Constructs/ConstructWalkFeel.cs). One size a class; the part is a base plate and a gyro drum on it, two
cells across and one high, with sockets under the base and on its sides.

Data first: the JSON files are what the game loads. Change a stabiliser here, never in its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, write_parts  # noqa: E402

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(ROOT, "Constructs", "Parts", "Stabilisers")
SECTION = "Legs"  # (Constructs/Categories.json: Wheels and Suspension, Legs)

# Class, kg of walker each one steadies (a Titan Walker of about 31,000 t is mostly steady on five Capital ones).
SIZES = [
    (SMALL, 600),
    (MEDIUM, 40_000),
    (LARGE, 1_500_000),
    (CAPITAL, 6_000_000),
]


def stabiliser(c, steady):
    h = c.half
    w = c.cell  # (two cells across: -w..w; one high: -h..h)
    name = f"{c.name} Stabiliser".strip()
    p = Part(name, c, SECTION, "A gyro stabiliser: steadies a walker's ride. More and bigger ones take out more of the "
             "footfalls' thump, the shake and the sway its riders feel, and keep loose cargo from being thrown about.",
             color="#56606a", group="Walkers" if c is SMALL else None)
    top = -h + h * 0.6
    p.add(box((-w, -h, -w), (w, top, w)))  # the base plate: solid
    gap = 0.004 * c.x
    p.add(box((-w * 0.62, top + gap, -w * 0.62), (w * 0.62, h, w * 0.62), "#2f3a44", detail=True))  # the gyro drum
    p.add(box((-w * 0.2, h + gap, -w * 0.2), (w * 0.2, h + h * 0.25, w * 0.2), "#c8a040", detail=True))  # its spindle cap
    p.sock("-y", (0, -h, 0), "-y")
    mid = (-h + top) / 2
    for name, at in (("+x", (w, mid, 0)), ("-x", (-w, mid, 0)), ("+z", (0, mid, w)), ("-z", (0, mid, -w))):
        p.sock(name, at, name)
    p.set(stabiliser={"steady": steady})
    mass = steady * 0.04
    return p.stats(mass, 2.5 * c.health_m2 * (2 * w) ** 2, c.armour)


def main():
    return write_parts(OUT, [("", stabiliser(c, s)) for c, s in SIZES])


if __name__ == "__main__":
    sys.exit(main())
