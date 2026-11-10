#!/usr/bin/env python3
"""Writes the interior system's own parts (Constructs/Parts/Interior): the Freight Lift Car and the hollow capital chassis.

docs/sandbox/INTERIORS.md, "Phase 2". The freight lift is a Linear Actuator 3x3 4 m (Constructs/Parts/Linear, written by
linear_parts.py) stood on end against a shaft wall, with this car hung on its carriage. The car is a small-class part: a
tread-plate floor a push cart or a small stack of crates fits on, a yoke that bolts to the carriage, two side rails and a
socket for the car's call button. Riders and loose things ride it as they ride any deck on a joint (ConstructDeck).

Frame: the yoke's back face is x = 0 with the "mount" socket on it (facing -x, onto the carriage's "top"); the car runs
along +x; up is +y. The floor's top is FLOOR_DROP below the mount, so the builder puts the floor 2 cm above a deck's
floor at the actuator's low stop (12% of 4 m: 0.48 m) and 2 cm above the next deck's floor at the high stop (87%).

The hollow chassis (INTERIORS.md "Phase 4"): the Capital Chassis Deck 128x64 m's outside (footprint, 16 m deep, its
under, top and side sockets by the same names and places, so a walker's legs and hips bolt on as before; its mass and
strength) with a hollow the interior builder can build in: the beams stand on the interior's 4 m grid (the one the builder
lays on the top sockets), the cross beams have a full-height portal where the corridor runs through them, the top plate has
hatches over the planner's stairwells and lift shaft, and each storey of the hollow has capital floor sockets for the
adapter pads in the corridor (one a bay). The armoured hull is the same frame with heavier plate.

The panel walls (INTERIORS.md "Phase 5"): Steel Panel Wall 4, 8, 12 and 16 m, the Steel Wall 4 m's section and plate in long
runs with few links: a bottom and a top socket at each end only (on the floors' and walls' 2 m module points, so they meet a
Steel Floor 4x4 m's sockets wherever a 4 m wall's would) and one end socket each side at mid height. The faces keep sockets
every 2 m for things a player hangs on them; they link to nothing unless something is fitted. A fitted construct holds at
most Construct.MaxLinks links, and a 4 m wall makes about ten.

Run it from the repo root: python3 -I tools/constructs/interior_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, CAPITAL, Part, box, pick, write_parts  # noqa: E402
import building_parts as bp  # noqa: E402 (the Building pieces' wall section, plate and sockets)
import zfight  # noqa: E402

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


# ---- The hollow chassis -------------------------------------------------------------------------------------------

DARK, IRON, ARMOUR = "#3f444a", "#6a5f55", "#4a4f45"
NX, NZ = 8, 4  # capital cells: 128 x 64 m, as the Capital Chassis Deck 128x64 m
GRID = -4.125  # the interior grid's first line (InteriorBuilder.Measure on the deck's top sockets with a one-cell margin)
CELL = 4.0
CLEAR = 0.05  # beams and hatch edges stand this far off a grid line (walls poke 1 cm past their block)
STOREY = 3.0
STOREYS = 4
SPINE = (6, 8)  # the planner's corridor rows (from, to) on a 14-cell-deep grid
BEAM_COLS = (11, 19)  # the grid columns the two inner cross beams fill
# the planner's shafts on this grid (north inner band, 1x3 stairs at a quarter and three quarters, the 2x3 lift in the middle)
HATCHES = ((7, 3, 1, 3), (16, 3, 2, 3), (22, 3, 1, 3))
FLOOR_BLOCKS = (4, 14, 25)  # corridor blocks (row SPINE[0]) holding each storey's floor sockets, one a bay


def g(i):
    """Grid line i (m)."""
    return GRID + i * CELL


def hollow_chassis(armoured=False):
    c = CAPITAL
    w, l = NX * c.cell, NZ * c.cell
    name = f"Armoured Hull {w:g}x{l:g} m" if armoured else f"Hollow Chassis {w:g}x{l:g} m"
    desc = ("Heavy armoured hull, 16 m deep, hollow inside: four storeys of interior under the deck." if armoured else
            "Heavy grillage, 16 m deep, hollow inside: four storeys of interior under the deck.")
    p = Part(f"{c.name} {name}", c, "Chassis", desc, role="frame", color=ARMOUR if armoured else DARK)
    p.d["centreSockets"] = False  # (a big part's sockets are its lattice, nothing else)
    t = c.plate * 2
    x0, x1, y0, y1, z0, z1 = c.lo(), c.hi(NX), c.lo(), c.hi(1), c.lo(), c.hi(NZ)
    lo, hi = y0 + t, y1 - t  # the hollow's floor and roof
    p.add(box((x0, y0, z0), (x1, lo, z1)))
    # the top plate round its hatches: a strip north and south of them, the band between cut by the hatches
    hz0, hz1 = g(HATCHES[0][1]) - CLEAR, g(HATCHES[0][1] + HATCHES[0][3]) + CLEAR
    p.add(box((x0, hi, z0), (x1, y1, hz0)))
    p.add(box((x0, hi, hz1), (x1, y1, z1)))
    at = x0
    for hx, _, hw, _ in HATCHES:
        p.add(box((at, hi, hz0), (g(hx) - CLEAR, y1, hz1)))
        at = g(hx + hw) + CLEAR
    p.add(box((at, hi, hz0), (x1, y1, hz1)))
    beam = IRON
    # end beams and edge beams round the hollow, just outside the grid
    ex0, ex1, ez0, ez1 = g(0) - CLEAR, g(30) + CLEAR, g(0) - CLEAR, g(14) + CLEAR
    p.add(box((x0, lo, z0), (ex0, hi, z1), beam))
    p.add(box((ex1, lo, z0), (x1, hi, z1), beam))
    p.add(box((ex0, lo, z0), (ex1, hi, ez0), beam))
    p.add(box((ex0, lo, ez1), (ex1, hi, z1), beam))
    # the inner cross beams, each filling a grid column, with a full-height portal where the corridor runs through
    for col in BEAM_COLS:
        bx0, bx1 = g(col) + CLEAR, g(col + 1) - CLEAR
        p.add(box((bx0, lo, ez0), (bx1, hi, g(SPINE[0]) - CLEAR), beam))
        p.add(box((bx0, lo, g(SPINE[1]) + CLEAR), (bx1, hi, ez1), beam))
    if armoured:
        # belt armour on the long sides (drawn proud of the edge beams, so it reads as armour) and riveted strips along the top
        a = 0.6
        for zf, zb in ((z0 - a, z0), (z1, z1 + a)):
            p.add(box((x0 + 2, y0 + 2, zf), (x1 - 2, y1 - 2, zb), ARMOUR, detail=True))
    # the deck's own sockets, by the same names and in the same places (chassis_deck in capital_parts.py)
    for i in pick(NX, 4):
        for k in pick(NZ, 4):
            p.sock(f"under{i}.{k}", (i * c.cell, y0, k * c.cell), "-y")
            p.sock(f"top{i}.{k}", (i * c.cell, y1, k * c.cell), "+y")
    for i in pick(NZ, 3):
        p.sock(f"-x{i}", (x0, 0, i * c.cell), "-x")
        p.sock(f"+x{i}", (x1, 0, i * c.cell), "+x")
    for i in pick(NX, 3):
        p.sock(f"-z{i}", (i * c.cell, 0, z0), "-z")
        p.sock(f"+z{i}", (i * c.cell, 0, z1), "+z")
    # each storey's floor sockets (an adapter pad's back), in the middle of a corridor block in each bay; above the bottom plate each
    # sits on a bracket (a socket stands on a solid face), just under the slab, so the corridor below keeps its headroom
    for k in range(STOREYS):
        for b, col in enumerate(FLOOR_BLOCKS):
            x, y, z = g(col) + 2.125, lo + k * STOREY, g(SPINE[0]) + 2.125
            if k > 0:
                p.add(box((x - 0.25, y - 0.15, z - 0.25), (x + 0.25, y, z + 0.25), beam))
            p.sock(f"floor{k}.{b}", (x, y, z), "+y")
    area = w * l * 2 + 2 * (w + l) * c.cell
    if armoured:
        return p.stats(area * c.kg_m2 * 3.5, min(16_000_000, 40 + area * c.health_m2 * 3.5), c.armour * 4)
    return p.stats(area * c.kg_m2 * 2.5, min(16_000_000, 40 + area * c.health_m2 * 2.5), c.armour * 2)


class Panel(bp.Part):
    """A Building piece longer than the Building generator's 4 m limit (its json() asserts it): the same output otherwise."""

    def json(self):
        assert 0 < len(self.boxes) <= 16 and 0 < len(self.sockets) <= 255 and len(self.d["name"]) <= 32, self.d["name"]
        zfight.resolve(self.boxes, self.sockets)
        d = dict(self.d)
        d.update(self.extra)
        d["boxes"] = self.boxes
        d["sockets"] = self.sockets
        return d


def panel_wall(metres):
    """A steel storey wall `metres` long (a multiple of 4) with links only where they hold it: see the module docstring."""
    n = int(metres / bp.C)
    h = bp.WALL_H
    L = n * bp.C
    p = Panel(f"Steel Panel Wall {metres} m", bp.SECTION_WALLS, "Long storey wall with few links (interiors). " + bp.MAT_WORDS["steel"],
              role="frame", tool=bp.MATS["steel"][7], material=bp.MATS["steel"][4], color=bp.MATS["steel"][5])
    p.fbox(0, 0, 0, L, h * bp.C, bp.C)
    bp.wall_details(p, "steel", n, h, [(0, 0, L, h * bp.C)])
    for x in (0, n - 1):
        p.csock(f"b{x}", (x, 0, 0), "-y")
        p.csock(f"t{x}", (x, h - 1, 0), "+y")
    p.csock(f"l{h // 2}", (0, h // 2, 0), "-x")
    p.csock(f"r{h // 2}", (n - 1, h // 2, 0), "+x")
    for x in sorted(set(range(0, n, 8)) | {n - 1}):
        for y in bp.thirds(h):
            p.csock(f"f{x}_{y}", (x, y, 0), "-z")
            p.csock(f"k{x}_{y}", (x, y, 0), "+z")
    area = L * h * bp.C
    return bp.mat_stats(p, "steel", area, seconds_scale=0.6 + 0.4 * n / 16)


def main():
    panels = [("Walls", panel_wall(m)) for m in (4, 8, 12, 16)]
    return write_parts(OUT, [("", car()), ("Capital", hollow_chassis()), ("Capital", hollow_chassis(armoured=True))] + panels)


if __name__ == "__main__":
    sys.exit(main())
