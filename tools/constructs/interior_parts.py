#!/usr/bin/env python3
"""Writes the interior system's own parts (Constructs/Parts/Interior): the Freight Lift Car.

docs/sandbox/INTERIORS.md, "Phase 2". The freight lift is a Linear Actuator 3x3 4 m (Constructs/Parts/Linear, written by
linear_parts.py) stood on end against a shaft wall, with this car hung on its carriage. The car is a small-class part: a
tread-plate floor a push cart or a small stack of crates fits on, a yoke that bolts to the carriage, two side rails and a
socket for the car's call button. Riders and loose things ride it as they ride any deck on a joint (ConstructDeck).

Frame: the yoke's back face is x = 0 with the "mount" socket on it (facing -x, onto the carriage's "top"); the car runs
along +x; up is +y. The floor's top is FLOOR_DROP below the mount, so the builder puts the floor 2 cm above a deck's
floor at the actuator's low stop (12% of 4 m: 0.48 m) and 2 cm above the next deck's floor at the high stop (87%).

Run it from the repo root: python3 -I tools/constructs/interior_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Interior")
PLATE, FRAME, RAIL, STRIPE = "#5d6670", "#3b4046", "#c9a21c", "#f08a12"

LENGTH = 3.2  # along +x, away from the rail: the 4 m shaft block less the rail, the carriage and clearance
WIDTH = 3.5  # across (z): the block less a quarter metre each side
FLOOR_DROP = 0.46  # the floor's top below the mount: low stop 0.48 m less 2 cm
PLATE_T = 0.1
YOKE_T = 0.1
YOKE_TOP = 0.6
RAIL_H = 1.0  # side rails' top over the floor
BAR = 0.06


def car():
    p = Part("Freight Lift Car", SMALL, "Lifts",
             "Hangs on a vertical Linear Actuator's carriage. Freight-sized: a push cart or a stack of crates.",
             role="frame", tool="welder", color=PLATE, group="Joints")
    y0 = -FLOOR_DROP - PLATE_T
    y1 = -FLOOR_DROP
    hw = WIDTH / 2
    p.add(box((0, y0, -hw), (LENGTH, y1, hw), PLATE))
    p.add(box((0, y1, -0.6), (YOKE_T, YOKE_TOP, 0.6), FRAME))  # the yoke, bolted to the carriage
    # side rails: a corner post at the far end, a top bar and a mid bar each side (the near end is the yoke's)
    for s in (-1, 1):
        zo, zi = s * hw, s * (hw - BAR)
        zlo, zhi = min(zo, zi), max(zo, zi)
        p.add(box((YOKE_T, y1, zlo), (YOKE_T + BAR, y1 + RAIL_H, zhi), FRAME))
        p.add(box((LENGTH - BAR, y1, zlo), (LENGTH, y1 + RAIL_H, zhi), FRAME))
        p.add(box((YOKE_T + BAR, y1 + RAIL_H - BAR, zlo), (LENGTH - BAR, y1 + RAIL_H, zhi), RAIL))
        p.add(box((YOKE_T + BAR, y1 + RAIL_H * 0.5 - BAR / 2, zlo), (LENGTH - BAR, y1 + RAIL_H * 0.5 + BAR / 2, zhi), FRAME))
    # a hazard stripe along the open edge (detail: drawn just proud of the floor)
    p.add(box((LENGTH - 0.15, y1, -hw + BAR), (LENGTH - 0.005, y1 + 0.006, hw - BAR), STRIPE, detail=True, paintable=False))
    p.sock("mount", (0, 0, 0), "-x")
    p.sock("button", (YOKE_T, 0.3, 0.4), "+x")  # the car's send button, on the yoke's face at hand height
    for i, x in enumerate((0.8, 1.8, 2.8)):
        for j, z in enumerate((-1.0, 0.0, 1.0)):
            p.sock(f"t{i}_{j}", (x, y1, z), "+y")  # floor sockets for cargo or a cart's clamp
    area = LENGTH * WIDTH
    p.stats(mass=area * 40 + 120, health=area * 120, armour=12, seconds=6)
    p.set(walk="floor")
    return p


def main():
    return write_parts(OUT, [("", car())])


if __name__ == "__main__":
    sys.exit(main())
