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


# ---- Room furniture (INTERIORS.md "Phase 8") ------------------------------------------------------------------------
# Small-class pieces the builder stands on a floor slab's top socket by their "bottom" socket (the part's origin, at its
# floor face). Front is +z. Containers are ordinary storage (slots), the boards read the circuit's charge as the Charge Gauge
# does, the medical station is a seat that heals its sitter. Every drawn box keeps its faces off its neighbours' planes
# (zfight.resolve, through Part.json).

STEEL, DARKS, WHITE, RED, WOOD, BRASS, GLOW = "#6a737b", "#3a3f45", "#d8dcd8", "#b8352c", "#7a5a38", "#a58a4e", "#9fd18c"


def furniture(name, desc, role, w, h, d, color, tool="wrench", mass=40, health=200, armour=4, seconds=2.0):
    p = Part(name, SMALL, "Furniture", desc, role=role, tool=tool, color=color, group="Interior")
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("top", (0, h, 0), "+y")
    p.sock("back", (0, h / 2, -d / 2), "-z")
    p.sock("+x", (w / 2, h / 2, 0), "+x")
    p.sock("-x", (-w / 2, h / 2, 0), "-x")
    p.stats(mass, health, armour, seconds)
    return p


def slot(mn, mx, label=None, stack=None):
    s = {"min": [round(v, 3) for v in mn], "max": [round(v, 3) for v in mx]}
    if label:
        s["label"] = label
    if stack:
        s["stack"] = stack
    return s


def cabinet(p, w, h, d, color, shelves, door=None, t=0.03):
    """A steel cabinet open at the front: back, sides, top, bottom, the shelves between; door: a detail panel colour drawn
    on the lower front (a locker's door stays shut: its slots are reached as any store's)."""
    hw, hd = w / 2, d / 2
    p.add(box((-hw, 0, -hd), (hw, t, hd), color))
    p.add(box((-hw, h - t, -hd), (hw, h, hd), color))
    p.add(box((-hw, t, -hd), (hw, h - t, -hd + t), color))
    for sx in (-1, 1):
        x0, x1 = sorted((sx * hw, sx * (hw - t)))
        p.add(box((x0, t, -hd + t), (x1, h - t, hd), color))
    for y in shelves:
        p.add(box((-hw + t, y - t / 2, -hd + t), (hw - t, y + t / 2, hd), color))
    if door:
        p.add(box((-hw + t, t, hd - 0.004), (hw - t, h - t, hd - 0.001), door, detail=True))


def locker():
    w, h, d = 0.6, 1.9, 0.5
    p = furniture("Locker", "A crew locker: two shelves for a person's kit.", "storage", w, h, d, STEEL, mass=45, health=240)
    cabinet(p, w, h, d, STEEL, [0.95])
    p.add(box((-0.2, 1.45, d / 2 - 0.003), (0.2, 1.5, d / 2 + 0.003), DARKS, detail=True))  # name plate
    p.set(storage={"slots": [slot((-0.26, 0.04, -0.21), (0.26, 0.92, 0.24)), slot((-0.26, 0.98, -0.21), (0.26, 1.86, 0.24))]})
    return p


def gun_rack():
    w, h, d = 1.2, 1.6, 0.4
    p = furniture("Gun Rack", "Three long guns or barrels stood upright, and a shelf of small parts.", "storage", w, h, d, DARKS, mass=50, health=260)
    t = 0.04
    p.add(box((-w / 2, 0, -d / 2), (w / 2, 0.08, d / 2), DARKS))  # foot
    p.add(box((-w / 2, 0.08, -d / 2), (w / 2, h, -d / 2 + t), WOOD))  # back board
    for x in (-w / 2, -0.21, 0.19, w / 2 - t):
        p.add(box((x, 0.08, -d / 2 + t), (x + t, h - 0.3, d / 2), DARKS))  # dividers
    p.add(box((-w / 2, h - 0.3, -d / 2 + t), (w / 2, h - 0.3 + t, d / 2), DARKS))  # the top shelf
    p.set(storage={"slots": [slot((-0.555, 0.09, -0.15), (-0.215, 1.29, 0.19), "long gun"),
                             slot((-0.165, 0.09, -0.15), (0.185, 1.29, 0.19), "long gun"),
                             slot((0.235, 0.09, -0.15), (0.555, 1.29, 0.19), "long gun"),
                             slot((-0.55, h - 0.25, -0.15), (0.55, h - 0.02, 0.19), "parts", 6)]})
    return p


def medicine_cabinet():
    w, h, d = 0.8, 1.7, 0.35
    p = furniture("Medicine Cabinet", "Shelves of medical stores behind glass.", "storage", w, h, d, WHITE, mass=35, health=160, armour=2)
    cabinet(p, w, h, d, WHITE, [0.6, 1.15])
    p.add(box((-0.05, h - 0.2, d / 2 - 0.003), (0.05, h - 0.06, d / 2 + 0.003), RED, detail=True))  # the cross
    p.add(box((-0.12, h - 0.15, d / 2 - 0.0035), (0.12, h - 0.11, d / 2 + 0.0025), RED, detail=True))
    p.set(storage={"slots": [slot((-0.37, 0.04, -0.14), (0.37, 0.58, 0.16), "medical"), slot((-0.37, 0.62, -0.14), (0.37, 1.13, 0.16), "medical"),
                             slot((-0.37, 1.17, -0.14), (0.37, 1.66, 0.16), "medical")]})
    return p


def tool_rack():
    w, h, d = 1.4, 1.8, 0.4
    p = furniture("Tool Rack", "A pegboard and bins for tools and spares.", "storage", w, h, d, STEEL, mass=55, health=240)
    t = 0.04
    p.add(box((-w / 2, 0, -d / 2), (w / 2, 0.85, d / 2), STEEL))  # the bench cabinet
    p.add(box((-w / 2, 0.85, -d / 2), (w / 2, h, -d / 2 + t), "#8a6a40"))  # the pegboard
    p.add(box((-w / 2, 1.25, -d / 2 + t), (w / 2, 1.25 + t, d / 2 - 0.1), STEEL))  # the bin shelf
    p.set(storage={"slots": [slot((-0.66, 0.86, -0.15), (-0.01, 1.24, 0.19), "tools"), slot((0.01, 0.86, -0.15), (0.66, 1.24, 0.19), "tools"),
                             slot((-0.66, 1.3, -0.15), (-0.23, 1.75, 0.09), "parts bin", 6), slot((-0.21, 1.3, -0.15), (0.21, 1.75, 0.09), "parts bin", 6),
                             slot((0.23, 1.3, -0.15), (0.66, 1.75, 0.09), "parts bin", 6)]})
    return p


def table(name, desc, w, h, d, top, legs, slots, mass):
    p = furniture(name, desc, "storage", w, h, d, top, mass=mass, health=180, armour=2)
    t, leg = 0.05, 0.06
    p.add(box((-w / 2, h - t, -d / 2), (w / 2, h, d / 2), top))
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * (w / 2 - 0.05), sz * (d / 2 - 0.05)
            p.add(box((min(x, x - sx * leg), 0, min(z, z - sz * leg)), (max(x, x - sx * leg), h - t, max(z, z - sz * leg)), legs))
    p.add(box((-w / 2 + 0.11, 0.12, -d / 2 + 0.11), (w / 2 - 0.11, 0.15, d / 2 - 0.11), legs))  # the shelf under the top
    p.set(storage={"slots": slots})
    return p


def mess_table():
    w, h, d = 1.8, 0.75, 0.9
    return table("Mess Table", "A crew table: what is set on it stays on it.", w, h, d, "#8a8f94", DARKS,
                 [slot((-0.78, 0.16, -0.33), (-0.01, h - 0.06, 0.33), "shelf"), slot((0.01, 0.16, -0.33), (0.78, h - 0.06, 0.33), "shelf")], 60)


def chart_table():
    w, h, d = 1.4, 0.95, 1.0
    p = furniture("Chart Table", "The navigator's table, with a chart drawer.", "storage", w, h, d, WOOD, mass=70, health=200, armour=2)
    t = 0.04
    p.add(box((-w / 2, 0, -d / 2), (w / 2, h - 0.25, d / 2), WOOD))  # the cabinet
    p.add(box((-w / 2, h - 0.25, -d / 2), (w / 2, h - 0.25 + t, d / 2), WOOD))  # the drawer floor
    for sx in (-1, 1):
        x0, x1 = sorted((sx * w / 2, sx * (w / 2 - t)))
        p.add(box((x0, h - 0.25 + t, -d / 2), (x1, h - t, d / 2), WOOD))
    p.add(box((-w / 2 + t, h - 0.25 + t, -d / 2), (w / 2 - t, h - t, -d / 2 + t), WOOD))
    p.add(box((-w / 2, h - t, -d / 2), (w / 2, h, d / 2), "#c9c2a8"))  # the chart's face
    p.set(storage={"slots": [slot((-0.65, h - 0.2, -0.45), (0.65, h - 0.045, 0.49), "charts")]})
    return p


def galley_stove():
    w, h, d = 1.0, 0.9, 0.7
    p = furniture("Galley Stove", "A galley range: hobs over an oven (cooking comes with food).", "storage", w, h, d, STEEL, mass=90, health=260)
    cabinet(p, w, h, d, STEEL, [])
    for x in (-0.25, 0.25):
        for z in (-0.15, 0.15):
            p.add(box((x - 0.09, h, z - 0.09), (x + 0.09, h + 0.02, z + 0.09), DARKS, detail=True))  # hobs
    p.add(box((-0.3, h - 0.15, d / 2 + 0.001), (0.3, h - 0.11, d / 2 + 0.03), DARKS, detail=True))  # the oven's handle
    p.set(storage={"slots": [slot((-0.46, 0.04, -0.31), (0.46, h - 0.04, 0.34), "oven")]})
    return p


def board(name, desc, w, h, d, desk):
    """A panel that reads the circuit's charge (the Charge Gauge's logic): a cabinet, or a console with its dial on a desk."""
    p = furniture(name, desc, "logic", w, h, d, DARKS, mass=60, health=180, armour=3)
    if desk:
        p.add(box((-w / 2, 0, -d / 2), (w / 2, 0.8, d / 2), DARKS))
        p.add(box((-w / 2, 0.8, -d / 2), (w / 2, h, -d / 2 + 0.2), DARKS))
        p.add(box((-0.35, 0.95, -d / 2 + 0.2), (0.35, 1.1, -d / 2 + 0.203), GLOW, detail=True))  # the screen
        p.d["dial"] = {"at": [0, 0.82, 0.0], "face": "+y", "max": 1}
    else:
        p.add(box((-w / 2, 0, -d / 2), (w / 2, h, d / 2), DARKS))
        for i in range(4):
            y = 0.4 + i * 0.3
            p.add(box((-0.45, y, d / 2), (0.45, y + 0.04, d / 2 + 0.004), BRASS, detail=True))  # breaker rows
        p.d["dial"] = {"at": [0, h - 0.25, d / 2 + 0.01], "face": "+z", "max": 1}
    p.d["logic"] = {"kind": "chargegauge"}
    return p


def medical_station():
    w, h, d = 0.8, 1.6, 2.0
    p = furniture("Medical Station", "A treatment couch with its monitor: whoever sits in it heals, 4 health a second.", "seat", w, h, d, WHITE,
                  mass=120, health=240, armour=3, seconds=3.0)
    p.add(box((-w / 2, 0, -d / 2), (w / 2, 0.5, d / 2), WHITE))  # the couch
    p.add(box((-w / 2 + 0.05, 0.5, -d / 2 + 0.05), (w / 2 - 0.05, 0.58, d / 2 - 0.05), "#5a8fa0"))  # its pad
    p.add(box((-0.06, 0.58, -d / 2 + 0.05), (0.06, h, -d / 2 + 0.17), STEEL))  # the monitor's post
    p.add(box((-0.25, h - 0.35, -d / 2 + 0.17), (0.25, h, -d / 2 + 0.22), DARKS))  # the monitor
    p.add(box((-0.2, h - 0.3, -d / 2 + 0.22), (0.2, h - 0.05, -d / 2 + 0.223), GLOW, detail=True))
    p.d["seat"] = {"at": [0, 0.15, 0.2], "heal": 4}
    return p


def repair_station():
    w, h, d = 1.6, 1.5, 0.8
    p = furniture("Repair Station", "A welding bench with stock for repairs at hand.", "storage", w, h, d, "#5d6670", mass=150, health=320, armour=6)
    p.add(box((-w / 2, 0, -d / 2), (w / 2, 0.85, d / 2), "#5d6670"))  # the bench
    p.add(box((-w / 2, 0.85, -d / 2), (w / 2, h, -d / 2 + 0.05), STEEL))  # the back
    p.add(box((0.45, 0.85, -0.2), (0.7, 1.25, 0.05), "#c9a21c"))  # the welding set
    p.set(storage={"slots": [slot((-0.75, 0.86, -0.34), (0.4, 1.3, 0.38), "stock"), slot((-0.75, 1.32, -0.34), (0.75, h - 0.02, -0.0), "parts bin", 6)]})
    return p


def furniture_parts():
    return [locker(), gun_rack(), medicine_cabinet(), tool_rack(), mess_table(), chart_table(), galley_stove(),
            board("Power Board", "The plant room's breaker board: its dial reads the circuit's charge, 0 to 1.", 1.2, 1.8, 0.3, False),
            board("Engineer's Panel", "The bridge's power board: its dial reads the circuit's charge, 0 to 1.", 1.0, 1.2, 0.6, True),
            medical_station(), repair_station()]


# ---- Floor panels (INTERIORS.md "Phase 8") -------------------------------------------------------------------------
# The Steel Floor 4x4 m's plate and section in big squares, so a long hull's decks take a part per 8 or 16 m square and the
# slab-to-slab links inside it go. Top sockets wherever a wall or a corner may stand (each 4 m block's edge lines, at the 2 m
# module points: walls run along block edges), plus, on the 8 m panel, the block middles for furniture; bottom sockets where a
# wall below may hold it; edge sockets at each block's ends, so a 4x4 m slab beside it links as it would to another slab.
# A part has at most 255 sockets: the 16 m panel keeps its edge lines' tops and only its rim's bottoms.


def floor_panel(metres):
    label, kgm2, hpm2, arm, material, colour, dark, tool, secs = bp.FLOOR_MATS["steel"]
    n = int(metres / bp.C)  # cells a side
    blocks = n // 16
    p = Panel(f"Steel Floor Panel {metres}x{metres} m", bp.SECTION_FLOORS, f"Big floor with few links (interiors). {bp.FLOOR_WORDS['steel']}",
              role="frame", tool=tool, material=material, color=colour)
    p.cbox(0, 0, 0, n, 1, n)
    p.fbox(0.05, bp.C, 0.05, n * bp.C - 0.05, bp.C + 0.008, n * bp.C - 0.05, "#a3a8ac", detail=True)
    mods = bp.mods(n)
    edge = sorted({b * 16 + e for b in range(blocks) for e in (0, 15)})
    full = blocks <= 2
    for x in mods:
        for z in mods:
            if full or x in edge or z in edge:
                p.csock(f"t{x}_{z}", (x, 0, z), "+y")
    for x in mods:
        for z in mods:
            if full or (x in edge and z in (0, n - 1)) or (z in edge and x in (0, n - 1)):
                p.csock(f"b{x}_{z}", (x, 0, z), "-y")
    for e in edge:
        p.csock(f"l{e}", (0, 0, e), "-x")
        p.csock(f"r{e}", (n - 1, 0, e), "+x")
        p.csock(f"f{e}", (e, 0, 0), "-z")
        p.csock(f"k{e}", (e, 0, n - 1), "+z")
    p.extra["walk"] = "floor"
    area = metres * metres
    return p.stats(kgm2 * area, 60 + hpm2 * area, arm, round(min(40.0, max(2.0, secs * area / 16 * 0.5)), 1))


def main():
    panels = [("Walls", panel_wall(m)) for m in (4, 8, 12, 16)]
    furn = [("Furniture", f) for f in furniture_parts()]
    floors = [("Floors", floor_panel(m)) for m in (8, 16)]
    return write_parts(OUT, [("", car()), ("Capital", hollow_chassis()), ("Capital", hollow_chassis(armoured=True))] + panels + furn + floors)


if __name__ == "__main__":
    sys.exit(main())
