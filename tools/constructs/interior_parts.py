#!/usr/bin/env python3
"""Writes the interior system's own parts (Constructs/Parts/Interior): the Freight Lift Car and the hollow capital chassis.

docs/sandbox/INTERIORS.md, "Phase 2". The freight lift is a Linear Actuator 3x3 4 m (Constructs/Parts/Linear, written by
the part kit's linear_parts) stood on end against a shaft wall, with this car hung on its carriage. The car is a small-class part: a
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
    p.add(box((0, y0, -hw), (LENGTH, y1, hw), PLATE, finish="deck plate"))
    p.add(box((0, y1, -0.6), (YOKE_T, YOKE_TOP, 0.6), FRAME))  # the yoke, bolted to the carriage
    # side rails: a corner post at the far end, a top bar and a mid bar each side (the near end is the yoke's)
    for s in (-1, 1):
        zo, zi = s * hw, s * (hw - BAR)
        zlo, zhi = min(zo, zi), max(zo, zi)
        p.add(box((YOKE_T, y1, zlo), (YOKE_T + BAR, y1 + RAIL_H, zhi), FRAME))
        p.add(box((LENGTH - BAR, y1, zlo), (LENGTH, y1 + RAIL_H, zhi), FRAME))
        p.add(box((YOKE_T + BAR, y1 + RAIL_H - BAR, zlo), (LENGTH - BAR, y1 + RAIL_H, zhi), RAIL, finish="hazard paint"))
        p.add(box((YOKE_T + BAR, y1 + RAIL_H * 0.5 - BAR / 2, zlo), (LENGTH - BAR, y1 + RAIL_H * 0.5 + BAR / 2, zhi), FRAME))
    # a hazard stripe along the open edge (detail: drawn just proud of the floor)
    p.add(box((LENGTH - 0.15, y1, -hw + BAR), (LENGTH - 0.005, y1 + 0.006, hw - BAR), STRIPE, detail=True, paintable=False, finish="hazard paint"))
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
    p.d["finish"] = "wall panelling"  # (phase 12: a wall reads as a wall; the builder gives each its faction's own by a step's finish)
    area = L * h * bp.C
    return bp.mat_stats(p, "steel", area, seconds_scale=0.6 + 0.4 * n / 16)


# ---- Room furniture (INTERIORS.md "Phase 8") ------------------------------------------------------------------------
# Small-class pieces the builder stands on a floor slab's top socket by their "bottom" socket (the part's origin, at its
# floor face). Front is +z. Containers are ordinary storage (slots), the boards read the circuit's charge as the Charge Gauge
# does, the medical station is a seat that heals its sitter. Every drawn box keeps its faces off its neighbours' planes
# (zfight.resolve, through Part.json).

STEEL, DARKS, WHITE, RED, WOOD, BRASS, GLOW = "#6a737b", "#3a3f45", "#d8dcd8", "#b8352c", "#7a5a38", "#a58a4e", "#9fd18c"


def furniture(name, desc, role, w, h, d, color, tool="wrench", mass=40, health=200, armour=4, seconds=2.0, finish="painted steel"):
    p = Part(name, SMALL, "Furniture", desc, role=role, tool=tool, color=color, group="Interior")
    p.set(finish=finish)  # (phase 12: the part's material; a box naming its own keeps it)
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
    p.add(box((-w / 2, 0.08, -d / 2), (w / 2, h, -d / 2 + t), WOOD, finish="wood"))  # back board
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
    p = furniture("Medicine Cabinet", "Shelves of medical stores behind glass.", "storage", w, h, d, WHITE, mass=35, health=160, armour=2, finish="gloss paint")
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
    p.add(box((-w / 2, 0.85, -d / 2), (w / 2, h, -d / 2 + t), "#8a6a40", finish="wood"))  # the pegboard
    p.add(box((-w / 2, 1.25, -d / 2 + t), (w / 2, 1.25 + t, d / 2 - 0.1), STEEL))  # the bin shelf
    p.set(storage={"slots": [slot((-0.66, 0.86, -0.15), (-0.01, 1.24, 0.19), "tools"), slot((0.01, 0.86, -0.15), (0.66, 1.24, 0.19), "tools"),
                             slot((-0.66, 1.3, -0.15), (-0.23, 1.75, 0.09), "parts bin", 6), slot((-0.21, 1.3, -0.15), (0.21, 1.75, 0.09), "parts bin", 6),
                             slot((0.23, 1.3, -0.15), (0.66, 1.75, 0.09), "parts bin", 6)]})
    return p


def table(name, desc, w, h, d, top, legs, slots, mass):
    p = furniture(name, desc, "storage", w, h, d, top, mass=mass, health=180, armour=2)
    t, leg = 0.05, 0.06
    p.add(box((-w / 2, h - t, -d / 2), (w / 2, h, d / 2), top, finish="plastic"))
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
    p = furniture("Chart Table", "The navigator's table, with a chart drawer.", "storage", w, h, d, WOOD, mass=70, health=200, armour=2, finish="wood")
    t = 0.04
    p.add(box((-w / 2, 0, -d / 2), (w / 2, h - 0.25, d / 2), WOOD))  # the cabinet
    p.add(box((-w / 2, h - 0.25, -d / 2), (w / 2, h - 0.25 + t, d / 2), WOOD))  # the drawer floor
    for sx in (-1, 1):
        x0, x1 = sorted((sx * w / 2, sx * (w / 2 - t)))
        p.add(box((x0, h - 0.25 + t, -d / 2), (x1, h - t, d / 2), WOOD))
    p.add(box((-w / 2 + t, h - 0.25 + t, -d / 2), (w / 2 - t, h - t, -d / 2 + t), WOOD))
    p.add(box((-w / 2, h - t, -d / 2), (w / 2, h, d / 2), "#c9c2a8", finish="matte paint"))  # the chart's face
    p.set(storage={"slots": [slot((-0.65, h - 0.2, -0.45), (0.65, h - 0.045, 0.49), "charts")]})
    return p


def galley_stove():
    w, h, d = 1.0, 0.9, 0.7
    p = furniture("Galley Stove", "A galley range: hobs over an oven (cooking comes with food).", "storage", w, h, d, STEEL, mass=90, health=260, finish="steel")
    cabinet(p, w, h, d, STEEL, [])
    for x in (-0.25, 0.25):
        for z in (-0.15, 0.15):
            p.add(box((x - 0.09, h, z - 0.09), (x + 0.09, h + 0.02, z + 0.09), DARKS, detail=True))  # hobs
    p.add(box((-0.3, h - 0.15, d / 2 + 0.001), (0.3, h - 0.11, d / 2 + 0.03), DARKS, detail=True))  # the oven's handle
    p.set(storage={"slots": [slot((-0.46, 0.04, -0.31), (0.46, h - 0.04, 0.34), "oven")]})
    return p


def board(name, desc, w, h, d, desk):
    """A panel that reads the circuit's charge (the Charge Gauge's logic): a cabinet, or a console with its dial on a desk."""
    p = furniture(name, desc, "logic", w, h, d, DARKS, mass=60, health=180, armour=3, finish="matte paint")
    if desk:
        p.add(box((-w / 2, 0, -d / 2), (w / 2, 0.8, d / 2), DARKS))
        p.add(box((-w / 2, 0.8, -d / 2), (w / 2, h, -d / 2 + 0.2), DARKS))
        p.add(box((-0.35, 0.95, -d / 2 + 0.2), (0.35, 1.1, -d / 2 + 0.203), GLOW, detail=True))  # the screen
        p.d["dial"] = {"at": [0, 0.82, 0.0], "face": "+y", "max": 1}
    else:
        p.add(box((-w / 2, 0, -d / 2), (w / 2, h, d / 2), DARKS))
        for i in range(4):
            y = 0.4 + i * 0.3
            p.add(box((-0.45, y, d / 2), (0.45, y + 0.04, d / 2 + 0.004), BRASS, detail=True, finish="brass"))  # breaker rows
        p.d["dial"] = {"at": [0, h - 0.25, d / 2 + 0.01], "face": "+z", "max": 1}
    p.d["logic"] = {"kind": "chargegauge"}
    return p


def medical_station():
    w, h, d = 0.8, 1.6, 2.0
    p = furniture("Medical Station", "A treatment couch with its monitor: whoever sits in it heals, 4 health a second.", "seat", w, h, d, WHITE,
                  mass=120, health=240, armour=3, seconds=3.0, finish="gloss paint")
    p.add(box((-w / 2, 0, -d / 2), (w / 2, 0.5, d / 2), WHITE))  # the couch
    p.add(box((-w / 2 + 0.05, 0.5, -d / 2 + 0.05), (w / 2 - 0.05, 0.58, d / 2 - 0.05), "#5a8fa0", finish="plastic"))  # its pad
    p.add(box((-0.06, 0.58, -d / 2 + 0.05), (0.06, h, -d / 2 + 0.17), STEEL))  # the monitor's post
    p.add(box((-0.25, h - 0.35, -d / 2 + 0.17), (0.25, h, -d / 2 + 0.22), DARKS))  # the monitor
    p.add(box((-0.2, h - 0.3, -d / 2 + 0.22), (0.2, h - 0.05, -d / 2 + 0.223), GLOW, detail=True))
    p.d["seat"] = {"at": [0, 0.15, 0.2], "heal": 4}
    return p


def repair_station():
    w, h, d = 1.6, 1.5, 0.8
    p = furniture("Repair Station", "A welding bench with stock for repairs at hand.", "storage", w, h, d, "#5d6670", mass=150, health=320, armour=6, finish="dirty steel")
    p.add(box((-w / 2, 0, -d / 2), (w / 2, 0.85, d / 2), "#5d6670"))  # the bench
    p.add(box((-w / 2, 0.85, -d / 2), (w / 2, h, -d / 2 + 0.05), STEEL))  # the back
    p.add(box((0.45, 0.85, -0.2), (0.7, 1.25, 0.05), "#c9a21c", finish="matte paint"))  # the welding set
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
    p.d["finish"] = "deck plate"
    p.extra["walk"] = "floor"
    area = metres * metres
    return p.stats(kgm2 * area, 60 + hpm2 * area, arm, round(min(40.0, max(2.0, secs * area / 16 * 0.5)), 1))


# ---- Dressing (INTERIORS.md "Phase 11") -----------------------------------------------------------------------------
# Things only looked at, hung on a wall's face socket by their "back" socket (the part's origin, facing -z; the part stands
# in +z, into the room) or stood on a floor socket by their "bottom". Up is +y. A wall's face sockets are 0.125, 1.375 and
# 2.625 m over the floor and the storey's ceiling is 2.75 m up, so a top-band piece keeps under y = +0.11 and hangs below its
# socket. One link each, no joints, no wires: the mains lamps light from the power circuit (logic "mains": the game's
# ConstructLamps.Lit), so a room's lights need no switch.

GLOW_PALE, YELLOW, HAZARD, RUST, PIPE_GREEN, GALV, CABLE, ORANGE = "#fff3b0", "#d9b23a", "#262626", "#7d6b4f", "#5f7d6a", "#a4abb0", "#2b2d30", "#c8692c"
LAGGED = "#c4bda8"
LAMP_CONFIG = [
    {"key": "colour", "type": "colour", "label": "Colour", "group": "Light", "default": "#ffed99", "tooltip": "The colour it glows when lit."},
    {"key": "brightness", "label": "Brightness", "group": "Light", "unit": "%", "min": 10, "max": 200, "step": 1, "default": 100, "tooltip": "How bright it glows."},
    {"key": "blink", "label": "Blink", "group": "Light", "unit": "Hz", "min": 0, "max": 10, "step": 0.1, "default": 0, "tooltip": "Flashes this many times a second while lit (0: steady)."}]


def hung(name, desc, role="frame", color=STEEL, mass=10, health=80, armour=2, seconds=1.0, tool="wrench", finish=None):
    p = Part(name, SMALL, "Dressing", desc, role=role, tool=tool, color=color, group="Interior")
    if finish:
        p.set(finish=finish)  # (phase 12: the part's material; a box naming its own keeps it, a lamp's glow names none)
    p.sock("back", (0, 0, 0), "-z")
    p.stats(mass, health, armour, seconds)
    return p


def glow(mn, mx):
    """A lamp's glowing face: lit while the lamp is (the game draws a lit lamp's unpaintable detail bright)."""
    return box(mn, mx, GLOW_PALE, detail=True, paintable=False)


def mains_lamp(p, watts):
    p.set(logic={"kind": "lamp", "mains": True}, power=watts, config=LAMP_CONFIG)
    return p


def wall_light():
    p = hung("Wall Light", "A lamp for a room's wall. Lights while the power circuit reaches it and has charge (no wire needed); a wire into it switches it.",
             role="logic", color=DARKS, mass=4, health=40, armour=1)
    p.add(box((-0.3, -0.07, 0.0), (0.3, 0.05, 0.12), DARKS, finish="matte paint"))
    p.add(glow((-0.27, -0.09, 0.02), (0.27, -0.07, 0.105)))
    return mains_lamp(p, 20)


def hall_floodlight():
    p = hung("Hall Floodlight", "A big lamp on an arm for a hall's wall. Lights while the power circuit reaches it and has charge; a wire into it switches it.",
             role="logic", color=DARKS, mass=18, health=80, armour=2)
    p.add(box((-0.15, -0.3, 0.0), (0.15, 0.08, 0.04), DARKS, finish="matte paint"))  # wall plate
    p.add(box((-0.05, -0.12, 0.04), (0.05, 0.0, 0.42), STEEL, finish="steel"))  # arm
    p.add(box((-0.45, -0.3, 0.3), (0.45, -0.1, 0.62), DARKS, finish="matte paint"))  # head
    p.add(glow((-0.42, -0.32, 0.33), (0.42, -0.3, 0.59)))
    return mains_lamp(p, 120)


def pipe_bank():
    p = hung("Pipe Bank", "Three pipes, 3.6 m, along a wall under the ceiling, on brackets. Dressing.", color=RUST, mass=60, health=200, armour=3, finish="steel")
    for x in (-1.4, 0.0, 1.4):
        p.add(box((x - 0.04, -0.52, 0.0), (x + 0.04, 0.06, 0.05), DARKS))  # bracket plate
        p.add(box((x - 0.025, -0.5, 0.05), (x + 0.025, -0.47, 0.26), DARKS))  # bracket arm
    # (phase 12) the top pipe lagged, the middle one painted, the bottom one bare; the lagged pipe's flanges are bare steel bands
    for k, (y, col, fin) in enumerate(((-0.12, LAGGED, "lagging"), (-0.27, PIPE_GREEN, "painted steel"), (-0.42, STEEL, "steel"))):
        p.add(box((-1.78, y - 0.06, 0.08), (1.78, y + 0.06, 0.2), col, finish=fin))
        for x in (-1.0, 1.0):
            p.add(box((x - 0.03, y - 0.08, 0.06), (x + 0.03, y + 0.08, 0.22), STEEL if fin == "lagging" else col, finish="steel" if fin == "lagging" else fin))  # flange
    return p


def duct_run():
    p = hung("Duct Run", "A square air duct, 3.6 m, along a wall under the ceiling. Dressing.", color=GALV, mass=45, health=150, armour=2, finish="aluminium")
    p.add(box((-1.78, -0.5, 0.06), (1.78, -0.05, 0.56), GALV))
    for x in (-1.2, -0.4, 0.4, 1.2):
        p.add(box((x - 0.03, -0.52, 0.045), (x + 0.03, -0.03, 0.575), "#8e959a"))  # seams
    for x in (-1.0, 1.0):
        p.add(box((x - 0.03, -0.53, 0.0), (x + 0.03, 0.06, 0.04), DARKS))  # hanger plates
    return p


def cable_tray():
    p = hung("Cable Tray Run", "A ladder tray of cables, 3.6 m, along a wall under the ceiling. Dressing.", color=STEEL, mass=25, health=120, armour=2, finish="steel")
    for z0 in (0.03, 0.38):
        p.add(box((-1.78, -0.16, z0), (1.78, -0.08, z0 + 0.02), STEEL))  # side rails
    for i in range(8):
        x = -1.575 + i * 0.45
        p.add(box((x - 0.015, -0.15, 0.05), (x + 0.015, -0.13, 0.38), STEEL))  # rungs
    for x in (-1.1, 1.1):
        p.add(box((x - 0.03, -0.2, 0.0), (x + 0.03, 0.06, 0.03), DARKS))  # brackets
    for z0, z1, col in ((0.07, 0.15, CABLE), (0.17, 0.24, ORANGE), (0.26, 0.35, CABLE)):
        p.add(box((-1.77, -0.13, z0), (1.77, -0.07, z1), col, finish="rubber"))
    return p


def wall_rib():
    p = hung("Wall Rib", "A frame of the hull's structure standing along a wall, floor to ceiling. Dressing.", color=DARKS, mass=40, health=200, armour=4, finish="dirty steel")
    p.add(box((-0.15, -1.355, 0.0), (0.15, 1.355, 0.03), DARKS))
    p.add(box((-0.03, -1.35, 0.03), (0.03, 1.35, 0.2), DARKS))
    p.add(box((-0.11, -1.345, 0.2), (0.11, 1.345, 0.23), DARKS))
    p.add(box((-0.02, 0.9, 0.23), (0.02, 1.33, 0.45), DARKS))  # knee under the ceiling
    return p


def wall_sign():
    p = hung("Deck Sign", "A stencilled section board with a hazard band. Dressing.", color="#2f4f6f", mass=3, health=30, armour=1, finish="painted steel")
    p.add(box((-0.45, -0.18, 0.0), (0.45, 0.18, 0.03), "#2f4f6f"))
    p.add(box((-0.4, 0.04, 0.03), (0.4, 0.13, 0.036), "#e8e8e0", detail=True))  # the stencil band
    for i in range(6):
        x = -0.4 + i * 0.14
        p.add(box((x, -0.13, 0.03), (x + 0.07, -0.03, 0.036), YELLOW if i % 2 == 0 else HAZARD, detail=True, finish="hazard paint"))
    return p


def tool_board():
    p = hung("Tool Board", "A pegboard of hand tools. Dressing.", color="#8c7a5a", mass=12, health=60, armour=1)
    p.add(box((-0.6, -0.4, 0.0), (0.6, 0.4, 0.03), "#8c7a5a", finish="wood"))
    for x, y0, y1, w, col in ((-0.45, -0.25, 0.2, 0.04, DARKS), (-0.3, -0.3, 0.25, 0.03, "#b8352c"), (-0.12, -0.2, 0.28, 0.05, DARKS),
                              (0.08, -0.32, 0.1, 0.06, "#c9a21c"), (0.3, -0.25, 0.25, 0.04, DARKS), (0.46, -0.15, 0.3, 0.03, STEEL)):
        p.add(box((x - w / 2, y0, 0.03), (x + w / 2, y1, 0.07), col, detail=True))
    return p


def fire_point():
    p = hung("Fire Point", "A red cabinet with an extinguisher and a hose reel. Dressing.", color=RED, mass=20, health=60, armour=1, finish="gloss paint")
    p.add(box((-0.22, -0.4, 0.0), (0.22, 0.4, 0.2), RED))
    p.add(box((-0.18, -0.36, 0.2), (0.18, 0.1, 0.206), "#e8e8e0", detail=True))  # the window
    p.add(box((-0.06, -0.3, 0.206), (0.06, 0.05, 0.26), "#9a1f1a", detail=True))  # the extinguisher behind it
    return p


def jib_crane():
    p = hung("Jib Crane", "A wall jib with a trolley and a chain hoist, for a hall's upper wall. Dressing.", color=YELLOW, mass=420, health=900, armour=6, seconds=3.0, finish="hazard paint")
    p.add(box((-0.25, -1.0, 0.0), (0.25, 0.08, 0.08), DARKS))  # wall plate
    p.add(box((-0.06, -0.25, 0.08), (0.06, -0.02, 3.6), YELLOW))  # the jib's web
    p.add(box((-0.14, -0.29, 0.1), (0.14, -0.25, 3.58), YELLOW))  # its bottom flange
    p.add(box((-0.14, -0.02, 0.1), (0.14, 0.02, 3.58), YELLOW))  # its top flange
    p.add(box((-0.05, -0.95, 0.08), (0.05, -0.29, 0.75), YELLOW))  # the knee brace
    p.add(box((-0.18, -0.42, 2.3), (0.18, -0.3, 2.7), DARKS))  # trolley
    p.add(box((-0.12, -0.75, 2.38), (0.12, -0.42, 2.62), "#c9a21c"))  # hoist
    p.add(box((-0.015, -1.4, 2.485), (0.015, -0.75, 2.515), CABLE, finish="steel"))  # chain
    p.add(box((-0.07, -1.55, 2.45), (0.07, -1.4, 2.55), DARKS))  # hook block
    for x in (-0.2, 0.2):
        p.add(box((x - 0.02, -0.4, 3.5), (x + 0.02, -0.29, 3.6), HAZARD))  # end stops
    return p


def bay_marking():
    p = Part("Bay Marking", SMALL, "Dressing", "A painted floor bay: four yellow lines round a 3.4 m square. Dressing.", role="frame", tool="wrench", color=YELLOW, group="Interior")
    p.set(finish="hazard paint")
    p.sock("bottom", (0, 0, 0), "-y")
    h0, h1, a, w = 0.004, 0.015, 1.7, 0.1
    p.add(box((-a, h0, -a), (a, h1, -a + w), YELLOW))
    p.add(box((-a, h0, a - w), (a, h1, a), YELLOW))
    p.add(box((-a, h0, -a + w + 0.005), (-a + w, h1, a - w - 0.005), YELLOW))
    p.add(box((a - w, h0, -a + w + 0.005), (a, h1, a - w - 0.005), YELLOW))
    p.stats(4, 40, 1, 1.0)
    return p


# ---- Trim (INTERIORS.md "Phase 12") ----------------------------------------------------------------------------------
# Cheap pieces that make a room read as floor, wall and trim: one link each, no joints, no wires. The builder paints them from the
# faction's palette (docs/sandbox/INTERIORS.md, Palettes.json) and gives them its wear's material.


def kick_plate():
    """Skirting: a 3.6 m strip of deck plate along a wall's foot, on the wall's low face socket (0.125 m over the floor)."""
    p = hung("Kick Plate", "A strip of plate along a wall's foot, where boots and carts scuff it. Trim.", color="#4a4f55", mass=8, health=60, armour=2, finish="deck plate")
    p.add(box((-1.78, -0.113, 0.0), (1.78, 0.17, 0.012), "#4a4f55"))  # (1.2 cm over the floor's top: no shared plane)
    p.add(box((-1.78, 0.17, 0.0), (1.78, 0.19, 0.02), "#3a3f45", finish="steel"))  # the top lip
    return p


def lane_marking():
    """A walkway 3.6 m long between two painted edge lines, for a hall's floor: the middle worn where feet go."""
    p = Part("Lane Marking", SMALL, "Dressing", "Painted walkway lines 3.6 m long, with the scuffed lane between them. Trim.", role="frame", tool="wrench", color=YELLOW, group="Interior")
    p.set(finish="hazard paint")
    p.sock("bottom", (0, 0, 0), "-y")
    a, w, half = 1.8, 0.08, 0.6
    for s in (-1, 1):
        z0, z1 = sorted((s * half, s * (half + w)))
        p.add(box((-a, 0.004, z0), (a, 0.014, z1), YELLOW))
    p.add(box((-a + 0.01, 0.002, -half + 0.006), (a - 0.01, 0.0125, half - 0.006), "#6a6e72", finish="worn paint"))  # the scuffed lane
    p.stats(3, 30, 1, 1.0)
    return p


def dressing_parts():
    return [wall_light(), hall_floodlight(), pipe_bank(), duct_run(), cable_tray(), wall_rib(), wall_sign(), tool_board(), fire_point(), jib_crane(), bay_marking(),
            kick_plate(), lane_marking()]


def main():
    panels = [("Walls", panel_wall(m)) for m in (4, 8, 12, 16)]
    furn = [("Furniture", f) for f in furniture_parts()]
    floors = [("Floors", floor_panel(m)) for m in (8, 16)]
    dress = [("Dressing", d) for d in dressing_parts()]
    return write_parts(OUT, [("", car()), ("Capital", hollow_chassis()), ("Capital", hollow_chassis(armoured=True))] + panels + furn + floors + dress)


if __name__ == "__main__":
    sys.exit(main())
