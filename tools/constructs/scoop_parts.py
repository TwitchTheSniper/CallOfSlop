#!/usr/bin/env python3
"""Writes the Sandbox's scoops (Constructs/Parts/Scoops): buckets for arms, in four styles and the four size classes.

docs/sandbox/CONSTRUCTS.md, "Scoops"; Source/Constructs/ConstructScoops.cs. A scoop is a bin with a cutting edge. In a part's
frame the floor is at the bottom (-y, fill rises along +y), the back wall at -z and the edge at the front of the floor,
cutting towards +z; the mouth opens up and forwards, its plane running from the edge to the top of the back wall.

Styles (a size's width is the style's cells across, times the class's cell):
  Digging Bucket    the all-rounder: teeth, a deep curved shell.
  Grading Bucket    wide and shallow, a plain lip: shaves the ground in flat, broad bites and holds a lot. Poor in stone.
  Trenching Bucket  narrow and deep, heavy teeth: cuts hard and narrow, holds little.
  Rock Bucket       a skeleton of bars and big teeth: the best in stone, and fines fall through the bars (its "keep").

Capacity is the fill box's volume in the units a cubic metre of ground gives (Constructs/Digging.json), so what a bucket
carries is what it dug. Mass is its plate area at the class's plate weight, and the cut force grows with the edge's
width to the power 1.5, never under 2.5 times the bucket's own weight, so a parked bucket rests on the ground.

Run it from the repo root: python3 -I tools/constructs/scoop_parts.py (--check only compares), then
tools/constructs/assembling.py for the recipes. Data first: fix this, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, r, vec, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Scoops")
UNITS = 10  # units a cubic metre (Constructs/Digging.json)
SHELL, WEAR, TOOTH, BAR = "#b8862a", "#55585c", "#3a3c40", "#6a6256"


class Style:
    def __init__(self, name, tag, cells, long, high, wall, force, bite, bites, flat, spill, seconds, stone, keep, stone_force=1.0):
        self.name, self.tag = name, tag
        self.cells = cells  # width in cells, by class key
        self.long, self.high = long, high  # length and height against the width (rounded up to the class's cells)
        self.wall = wall  # plate, against the width
        self.force = force  # N a metre of edge, per metre of the class's scale
        self.bite, self.bites, self.flat = bite, bites, flat  # a bite's ball against the width, how many, flat or round
        self.spill, self.seconds = spill, seconds  # the angle of the mouth from up at which it pours; seconds to empty
        self.stone, self.keep = stone, keep


DIG = Style("Digging Bucket", "dig", {"small": 5, "medium": 3, "large": 3, "capital": 3}, 1.0, 0.8, 0.05, 2600, 0.3, 2, False, 105, 2.0, 0.4, None)
GRADE = Style("Grading Bucket", "grade", {"small": 7, "medium": 5, "large": 5, "capital": 5}, 0.6, 0.4, 0.03, 1100, 0.2, 3, True, 95, 1.5, 0.12, None)
TRENCH = Style("Trenching Bucket", "trench", {"small": 3, "medium": 1, "large": 1, "capital": 1}, 1.5, 1.2, 0.08, 6000, 0.55, 1, False, 110, 3.0, 0.6, None)
ROCK = Style("Rock Bucket", "rock", {"small": 5, "medium": 3, "large": 3, "capital": 3}, 1.0, 0.8, 0.07, 4200, 0.3, 2, False, 105, 2.0, 1.0, {"dirt": 0.3, "sand": 0.1})
STYLES = (DIG, GRADE, TRENCH, ROCK)


def cells_for(c, metres):
    return max(1, int(round(metres / c.cell + 0.499)))


def teeth(p, n, w, t, z, y0, y1, long, colour=TOOTH):
    """n teeth along the edge, pointing +z: drawn, not solid (the edge is the lip)."""
    for i in range(n):
        x = (i + 0.5) / n * w - w / 2
        hw = w / n * 0.28
        p.add(box((x - hw, y0, z), (x + hw, y1, z + long), colour, detail=True))


def shell(p, s, c, w, xl, xh, y0, y1, z0, z1, t):
    """The body of a style between its corners. Returns the plate area (m2) and the fill box's corners."""
    h = y1 - y0
    long = z1 - z0
    ribs = 3 if c.key == "small" else 5
    if s is ROCK:
        # A skeleton: bars along the floor and up the back on a frame, gaps between them.
        bar = w / (ribs * 2 + 1)
        for i in range(ribs):
            x = xl + bar * (2 * i + 1)
            p.add(box((x, y0, z0 + t), (x + bar, y0 + t, z1)))
            p.add(box((x, y0 + t, z0), (x + bar, y1, z0 + t), BAR))
        p.add(box((xl, y0, z1 - t * 2), (xh, y0 + t, z1), WEAR))  # the lip
        p.add(box((xl, y0, z0), (xh, y0 + t, z0 + t)))  # the heel
        p.add(box((xl, y1 - t, z0 - 0.002 * c.x), (xh, y1, z0 + t)))  # the top bar
        area = 2 * (w * long + w * h) * 0.5
    else:
        p.add(box((xl, y0, z0 + t), (xh, y0 + t, z1)))  # the floor
        p.add(box((xl, y0, z0), (xh, y1, z0 + t)))  # the back
        p.add(box((xl, y0, z1 - t * 3), (xh, y0 + t * 1.4, z1), WEAR))  # the wear lip
        p.add(box((xl + t, y0 + t, z0 + t), (xh - t, y0 + t * 2.5, z0 + t * 3.5), hidden=False, detail=True))  # the curve into the back
        area = w * long + w * h
    # The sides stand tall at the back and fall away to the edge, in steps.
    steps = 3 if s is not GRADE else 2
    for i in range(steps):
        z_a = z0 + long * i / steps
        z_b = z0 + long * (i + 1) / steps
        top = y1 - (h * 0.75) * i / steps
        for x0, x1 in ((xl, xl + t), (xh - t, xh)):
            p.add(box((x0, y0, z_a), (x1, top, z_b)))
    area += 2 * long * h * 0.6
    inner_top = y0 + (h * 0.72)
    return area, (r(xl + t + 0.005), r(y0 + t + 0.005), r(z0 + t + 0.005)), (r(xh - t - 0.005), r(inner_top), r(z0 + long * 0.8))


def make(s, c):
    n = s.cells[c.key]
    w = n * c.cell
    long_cells = cells_for(c, w * s.long)
    high_cells = cells_for(c, w * s.high)
    z0, z1 = c.lo(), c.hi(long_cells)
    y0, y1 = c.lo(), c.hi(high_cells)
    long, high = z1 - z0, y1 - y0
    xl, xh = -w / 2, w / 2
    t = max(0.02, w * s.wall)
    name = s.name if c is SMALL else f"{s.name} {c.name}"
    desc = {
        DIG: "Pushed edge first into the ground it digs it out and holds what it cuts. Tip it to pour.",
        GRADE: "Wide and shallow: shaves the ground flat in broad bites and holds a lot. Poor in stone.",
        TRENCH: "Narrow and deep: cuts hard, holds little.",
        ROCK: "Bars and big teeth: the best in stone. Dirt and sand mostly fall through.",
    }[s]
    p = Part(name, c, "Scoops", desc, role="storage", material="steel", color=SHELL, group="Gathering" if c is SMALL else None)
    area, fmin, fmax = shell(p, s, c, w, xl, xh, y0, y1, z0, z1, t)
    if s is not GRADE:
        count = {DIG: 5, TRENCH: 3, ROCK: 4}[s] if c is SMALL or n > 1 else 3
        tl = w * (0.14 if s is ROCK else 0.1)
        teeth(p, count, w, t, z1 + 0.002 * c.x, y0, y0 + t * 1.1, tl)
    else:
        p.add(box((xl, y0 - 0.002 * c.x, z1 - t), (xh, y0 + t * 0.5, z1 + w * 0.012), TOOTH, detail=True))  # the blade
    p.sock("back", (0, 0, z0), "-z")
    p.sock("top", (0, y1, 0), "+y")
    p.sock("under", (0, y0, 0), "-y")
    volume = (fmax[0] - fmin[0]) * (fmax[1] - fmin[1]) * (fmax[2] - fmin[2])
    capacity = max(2, int(round(volume * UNITS)))
    edge_y = y0 + 0.01
    mouth = (0, long, high)
    mass = area * c.kg_m2 * (t / c.plate) ** 0.5 * 0.9
    force = max(s.force * w ** 1.5, 2.5 * mass * 9.81)  # (the ground stands more than the bucket's own weight: parked, it rests)
    scoop = {
        "edge": [vec(xl, edge_y, z1), vec(xh, edge_y, z1)],
        "rim": [vec(xl, y1, z0), vec(xh, y1, z0)],
        "cuts": [0, 0, 1],
        "mouth": vec(*mouth),
        "reach": r(high),
        "cutForce": int(round(force, -1)),
        "biteRadius": r(max(0.3, w * s.bite)),
        "spillDegrees": s.spill,
        "pourSeconds": s.seconds,
    }
    if s.bites > 1:
        scoop["bites"] = s.bites
    if s.flat:
        scoop["flat"] = True
    if s.stone < 1:
        scoop["stoneRate"] = s.stone
    if s.keep:
        scoop["keep"] = s.keep
    p.set(storage={"bulk": {"material": "any", "capacity": capacity, "min": list(fmin), "max": list(fmax)}, "scoop": scoop})
    p.stats(mass, area * c.health_m2 * 1.5, c.armour)
    return p


def main():
    parts = []
    for c in (SMALL, MEDIUM, LARGE, CAPITAL):
        for s in STYLES:
            parts.append(("", make(s, c)))
    return write_parts(OUT, parts)


if __name__ == "__main__":
    sys.exit(main())
