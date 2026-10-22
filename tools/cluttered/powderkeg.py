#!/usr/bin/env python3
"""Generator for Maps/Cluttered/12-PowderKeg.json (large, 16 players): a warehouse full of things that go bang.
Usage: python3 tools/cluttered/powderkeg.py <repo root> [preview.png]

Built with townkit.py (the pack's shared placement rules) plus the props from powderkeg_props.py. Not mirrored:
- West (Blue): the loading yard. Semi trailers backed onto the dock doors, a container stack, a truck park and a
  guard hut: lots of cover and two long flanks (the north yard and the south rail yard) round the hall.
- East (Red): the office annex. A two-storey block inside the hall's east end (break room, dispatch and maintenance
  shop downstairs, offices and an open mezzanine upstairs), two catwalks from the mezzanine over the racking aisles,
  and the staff car park outside. Red gets the high ground; Blue gets the cover and the flanks.
- Middle: the main hall (64 x 52 m, roof at 9.5 m), rows of 4.4 m pallet racking with a staging area in the middle,
  the chemical store and propane tank farm in the north yard, the rail spur in the south yard.
Hundreds of explosive props (red fuel barrels, propane, gas bottles, flammables cabinets) sit in clusters and lines so
chain reactions run along them; none within 15 m of a spawn (bulk tanks 22 m).
The generator places the hills on the line of equal walk (a 0.5 m grid walk from each team's spawns), checks that no
Blue spawn sees a Red one, and prints counts. The self-test measures the walks again on the game's nav graph.
"""
import json, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import townkit as tk
from townkit import yaw_facing, yaw_along

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/12-PowderKeg.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None

CTRL = [(-100, -8), (-97, -33), (-80, -50), (-52, -57), (-26, -64), (4, -70), (34, -67), (58, -57), (80, -44), (93, -20), (95, 10),
        (85, 35), (60, 49), (30, 58), (2, 61), (-28, 57), (-58, 55), (-84, 43), (-98, 20)]
NOISE = {"amplitude": 1.8, "scale": 20, "seed": 12, "octaves": 2}
t = tk.Town(REPO, CTRL, NOISE, pack_props=[os.path.join(REPO, "Maps/Cluttered/Props")])
rng = random.Random(412)

# The game's nav grid (BotNav.cs): nodes at -HalfX + s/2 + i*s. Narrow raised walkways (catwalks, stairs) sit on grid lines
# so the graph finds their tops.
HALF_X = max(abs(min(p[0] for p in t.outline)), abs(max(p[0] for p in t.outline)))
HALF_Z = max(abs(min(p[1] for p in t.outline)), abs(max(p[1] for p in t.outline)))
NAV_S = min(7.0, max(3.0, math.sqrt(4 * HALF_X * HALF_Z / 1000.0)))


def grid_z(z):
    k = round((z + HALF_Z - NAV_S / 2) / NAV_S)
    return -HALF_Z + NAV_S / 2 + k * NAV_S


def grid_x(x):
    k = round((x + HALF_X - NAV_S / 2) / NAV_S)
    return -HALF_X + NAV_S / 2 + k * NAV_S


# ---------------------------------------------------------------- palette
CONC = (0.56, 0.55, 0.52); CONC_D = (0.44, 0.43, 0.41); ASPH = (0.26, 0.26, 0.27); APRON = (0.50, 0.49, 0.46)
CLAD = (0.62, 0.64, 0.66); CLAD_B = (0.30, 0.42, 0.58); BLOCKW = (0.68, 0.66, 0.60); ROOF = (0.36, 0.38, 0.40)
FLOOR = (0.42, 0.42, 0.42); YELLOW = (0.88, 0.72, 0.12); WHITE = (0.86, 0.86, 0.82); DARK = (0.12, 0.13, 0.15)
STEEL = (0.46, 0.48, 0.50); GRATE = (0.40, 0.42, 0.44); OFFICE = (0.80, 0.78, 0.72); RAIL = (0.36, 0.30, 0.26)
CONT = [(0.62, 0.20, 0.16), (0.18, 0.36, 0.56), (0.22, 0.46, 0.30), (0.78, 0.56, 0.16), (0.50, 0.50, 0.52), (0.42, 0.24, 0.40)]

# ---------------------------------------------------------------- the hall frame
HX0, HX1, HZ0, HZ1 = -30.0, 34.0, -28.0, 24.0     # outer faces of the hall walls
WT = 0.4                                            # wall thickness
WH = 9.5                                            # wall height (the roof sits on top)
IX0, IX1, IZ0, IZ1 = HX0 + WT, HX1 - WT, HZ0 + WT, HZ1 - WT
MX = 20.0                                           # the mezzanine's west edge (annex x 20..34)
DECK = 4.8                                          # mezzanine and catwalk floor (top)
CW1 = grid_z(-14.0); CW2 = grid_z(7.0)             # catwalk centre lines (on nav grid lines)
CW_HALF = 0.9
DOCKS = [-21.0, -8.6, 4.4, 17.0]                    # dock door centres on the west wall (z)

# ---------------------------------------------------------------- objectives (decided first: everything keeps clear)
BLUE_SP = [(-91, -17), (-87, -12.5), (-92, -6), (-88, -1), (-92.5, 4), (-87.5, 8.5), (-92, 13.5), (-86, 17), (-84, -5)]
RED_SP = [(85, -18), (81, -13), (86, -8), (81.5, -3), (86.5, 2), (82, 7), (86, 12), (81, 17), (77, 0)]
FFA_SP = [(-60, -36), (-66, 48), (-20, -48), (60, -38), (24, 50), (60, 38), (64, -28), (-46, 0)]
HILLS = [(-4.0, -3.6, 5.0), (-3.0, -35.8, 5.0), (-2.0, 35.0, 5.0)]
FLAGS = [(-94.0, 1.0), (89.0, -2.0)]
SPAWNS_ALL = BLUE_SP + RED_SP + FFA_SP
for x, z in SPAWNS_ALL:
    t.keepout.append((x, z, 2.6))
for x, z, r in HILLS:
    t.keepout.append((x, z, r + 0.6))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))


def far_from_spawns(x, z, d):
    return all(math.hypot(x - sx, z - sz) >= d for sx, sz in SPAWNS_ALL)


EXPLOSIVE = {"Red Fuel Barrel": 5.5, "Propane Tank": 6.5, "Propane Bulk Tank": 11.0, "Gas Bottle": 3.8, "Gas Bottle Cage": 7.0,
             "Flammables Cabinet": 6.0, "Explosive Barrel": 5.5, "Jerry Can": 3.5}
BOOM = []   # (name, x, z, y) of every explosive prop placed


def bang(name, x, z, yaw=0.0, y=None, check=True, tag="boom"):
    """Places an explosive prop, never closer to a spawn than its blast radius + 9.5 m (15 m for a barrel)."""
    if not far_from_spawns(x, z, EXPLOSIVE[name] + 9.5):
        return False
    if t.prop(name, x, z, yaw, y=y, check=check, tag=tag):
        BOOM.append((name, x, z, y or 0.0))
        return True
    return False


# ---------------------------------------------------------------- flat surfaces (meshes; Concrete = intact, plain = busted)
def slab(x0, z0, x1, z1, c, material="Concrete", h=0.04, y=0.0, yaw=0.0):
    kw = {"material": material} if material else {}
    if yaw:
        kw["yaw"] = yaw
    t.mesh("box", (x0 + x1) / 2, (z0 + z1) / 2, c, y=y, width=round(abs(x1 - x0), 2), height=h, depth=round(abs(z1 - z0), 2), **kw)


def lines_x(x0, x1, z, c=YELLOW, w=0.12):
    t.mesh("box", (x0 + x1) / 2, z, c, y=0.0, width=round(x1 - x0, 2), height=0.045, depth=w)


def lines_z(z0, z1, x, c=YELLOW, w=0.12):
    t.mesh("box", x, (z0 + z1) / 2, c, y=0.0, width=w, height=0.045, depth=round(z1 - z0, 2))


# ---------------------------------------------------------------- walls with openings
def wall_x(x0, x1, z0, z1, h, c, holes=(), y0=0.0):
    """A wall along X from x0 to x1 (thickness z0..z1), base y0, height h, with openings (centre x, width, bottom, top)
    measured from y0."""
    pos = x0
    for (cx, w, bot, top) in sorted(holes):
        a, b = cx - w / 2, cx + w / 2
        if a - pos > 0.05:
            t.block(pos, y0, z0, a, y0 + h, z1, c)
        if bot > 0.05:
            t.block(a, y0, z0, b, y0 + bot, z1, c)
        if top < h - 0.05:
            t.block(a, y0 + top, z0, b, y0 + h, z1, c)
        pos = b
    if x1 - pos > 0.05:
        t.block(pos, y0, z0, x1, y0 + h, z1, c)


def wall_z(z0, z1, x0, x1, h, c, holes=(), y0=0.0):
    pos = z0
    for (cz, w, bot, top) in sorted(holes):
        a, b = cz - w / 2, cz + w / 2
        if a - pos > 0.05:
            t.block(x0, y0, pos, x1, y0 + h, a, c)
        if bot > 0.05:
            t.block(x0, y0, a, x1, y0 + bot, b, c)
        if top < h - 0.05:
            t.block(x0, y0 + top, a, x1, y0 + h, b, c)
        pos = b
    if z1 - pos > 0.05:
        t.block(x0, y0, pos, x1, y0 + h, z1, c)


def keep_door(x, z, along_x, w, depth=1.3):
    """Keeps the floor either side of a doorway clear of anything placed later."""
    if along_x:
        t.mark(t.occ, (x - w / 2, z - depth, x + w / 2, z + depth), 3)
    else:
        t.mark(t.occ, (x - depth, z - w / 2, x + depth, z + w / 2), 3)


DOOR = lambda c, w=1.6: (c, w, 0.0, 2.4)
ROLLER = lambda c, w=4.4: (c, w, 0.0, 4.2)
WIN = lambda c, w=2.4: (c, w, 1.1, 2.3)


def stairs_x(x_top, z0, z1, dir_down, y0, height, c=STEEL):
    """A solid staircase along X: its top step (at y0 + height) ends at x_top and it descends towards dir_down (+1/-1).
    Steps 0.45 m long, at most 0.4 m high. Returns the x where the bottom step starts."""
    n = math.ceil(height / 0.4); rise = height / n
    for k in range(n):
        xa = x_top + dir_down * k * 0.45; xb = xa + dir_down * 0.45
        t.block(xa, y0, z0, xb, y0 + height - k * rise, z1, c)
    return x_top + dir_down * n * 0.45


def stairs_z(z_top, x0, x1, dir_down, y0, height, c=STEEL):
    n = math.ceil(height / 0.4); rise = height / n
    for k in range(n):
        za = z_top + dir_down * k * 0.45; zb = za + dir_down * 0.45
        t.block(x0, y0, za, x1, y0 + height - k * rise, zb, c)
    return z_top + dir_down * n * 0.45


# ======================================================================================== ground surfaces
# yard apron (busted concrete), roads (asphalt, intact), the car park, the hall floor
slab(-62, -26, HX0, 26, APRON, None)                                    # west yard apron in front of the docks
slab(-96, -30, -62, 30, (0.47, 0.46, 0.43), None)                       # truck park / spawn yard
slab(HX0 - 6, -40, HX1 + 10, HZ0, ASPH, "Concrete", h=0.03)               # north service road
slab(HX0 - 6, HZ1, HX1 + 10, HZ1 + 7, ASPH, "Concrete", h=0.03)           # south service road
slab(HX1, -40, 76, 30, ASPH, "Concrete", h=0.03)                        # east road and car park
slab(IX0, IZ0, IX1, IZ1, FLOOR, "Concrete", h=0.03)                       # hall floor
for z in DOCKS:                                                          # dock door aprons with yellow edge lines
    lines_z(z - 2.6, z + 2.6, HX0 - 0.15, YELLOW, 0.25)
    t.mesh("box", HX0 - 1.0, z, (0.30, 0.31, 0.32), y=0.0, width=2.0, height=0.05, depth=3.6, material="Metal")   # dock leveller plate
# painted walkway lines in the hall: along the staging band and the cross aisle
lines_x(-21, 17, -8.9, YELLOW); lines_x(-21, 17, 4.9, YELLOW)
lines_z(IZ0 + 3, IZ1 - 3, -4.6, YELLOW); lines_z(IZ0 + 3, IZ1 - 3, 0.2, YELLOW)
# road markings
lines_x(HX0 - 4, HX1 + 8, -34.0, WHITE, 0.14); lines_x(HX0 - 4, HX1 + 8, HZ1 + 3.5, WHITE, 0.14)

# ======================================================================================== the hall
# West wall: four dock doors and a personnel door; north wall: a roller door and a personnel door; south likewise;
# east wall: the annex doors (break room, dispatch, shop roller door) and the upstairs door off the outside stair.
for z in DOCKS:
    keep_door(HX0 + WT / 2, z, False, 4.4)
wall_z(HZ0, HZ1, HX0, HX0 + WT, WH, CLAD, [ROLLER(z) for z in DOCKS] + [DOOR(-1.8)])
keep_door(HX0, -1.8, False, 1.6)
wall_x(HX0 + WT, HX1 - WT, HZ0, HZ0 + WT, WH, CLAD, [ROLLER(-14.0), DOOR(8.0), WIN(-24.0, 3.0), WIN(26.0, 2.4)])
keep_door(-14.0, HZ0, True, 4.4); keep_door(8.0, HZ0, True, 1.6)
wall_x(HX0 + WT, HX1 - WT, HZ1 - WT, HZ1, WH, CLAD, [ROLLER(-12.0), DOOR(8.0), WIN(26.0, 2.4)])
keep_door(-12.0, HZ1, True, 4.4); keep_door(8.0, HZ1, True, 1.6)
EAST_UP_DOOR = -12.6
wall_z(HZ0, HZ1, HX1 - WT, HX1, WH, CLAD_B, [DOOR(-22.0), (-2.0, 2.4, 0.0, 2.4), ROLLER(18.5, 4.0), (EAST_UP_DOOR, 1.6, DECK, DECK + 2.3), WIN(-6.0, 2.0)])
keep_door(HX1, -22.0, False, 1.6); keep_door(HX1, -2.0, False, 2.4); keep_door(HX1, 18.5, False, 4.0)
# the roof (one slab), a raised ridge strip of skylights and gutters (cosmetic)
t.block(HX0 - 0.3, WH, HZ0 - 0.3, HX1 + 0.3, WH + 0.35, HZ1 + 0.3, ROOF)
for zc in (-16.0, -2.0, 12.0):
    t.mesh("box", (HX0 + HX1) / 2, zc, (0.62, 0.70, 0.74), y=WH + 0.35, width=HX1 - HX0 - 6, height=0.25, depth=2.2)
for (x0, x1, zc) in ((HX0, HX1, HZ0 - 0.3), (HX0, HX1, HZ1 + 0.3)):
    t.mesh("box", (x0 + x1) / 2, zc, (0.30, 0.31, 0.33), y=WH - 0.2, width=x1 - x0 + 0.6, height=0.3, depth=0.3)
# cladding ribs on the long walls (cosmetic, breaks up the flat walls) and the company sign
for x in range(int(HX0) + 4, int(HX1) - 2, 6):
    for zc, d in ((HZ0 - 0.06, -1), (HZ1 + 0.06, 1)):
        t.mesh("box", x, zc, (0.54, 0.56, 0.58), y=0.0, width=0.18, height=WH, depth=0.1)
t.mesh("box", HX0 - 0.08, -1.8, (0.10, 0.16, 0.30), y=6.2, width=0.1, height=1.6, depth=14.0)
t.mesh("box", HX0 - 0.12, -1.8, (0.92, 0.80, 0.20), y=6.6, width=0.06, height=0.8, depth=12.0)

# interior columns along the roof spans (solid, part of the cover in the staging band)
COLUMNS = [(-20.5, -2.0), (-4.4, -9.4), (-4.4, 5.4), (17.0, -2.0)]
for (cx, cz) in COLUMNS:
    t.block(cx - 0.25, 0, cz - 0.25, cx + 0.25, WH, cz + 0.25, STEEL)

# ---------------------------------------------------------------- the annex (x 20..34): two storeys inside the hall's east end
# downstairs: break room (north), dispatch (middle, open to the hall), maintenance shop (south)
BR = (MX + 0.6, IZ0, IX1, -17.0)     # break room (x, z) rectangle
SH = (MX + 0.6, 13.0, IX1, IZ1)      # shop
DH = 4.6                             # downstairs ceiling (the deck slab is 4.6..4.8)
wall_z(IZ0, -17.0, MX + 0.6, MX + 0.9, DH, BLOCKW, [DOOR(-21.0), WIN(-25.0, 2.0)])
keep_door(MX + 0.75, -21.0, False, 1.6)
wall_x(MX + 0.6, IX1, -17.3, -17.0, DH, BLOCKW, [DOOR(29.5), WIN(24.0, 3.0)])
keep_door(29.5, -17.15, True, 1.6)
wall_z(13.0, IZ1, MX + 0.6, MX + 0.9, DH, BLOCKW, [ROLLER(18.5, 3.2)])
keep_door(MX + 0.75, 18.5, False, 3.2)
wall_x(MX + 0.6, IX1, 13.0, 13.3, DH, BLOCKW, [DOOR(29.0), WIN(24.5, 3.0)])
keep_door(29.0, 13.15, True, 1.6)
# a screen wall inside the dispatch door (you can't look straight out of the door down the hall)
t.block(30.6, 0, -4.8, 30.9, 2.6, 0.8, BLOCKW)
t.mark(t.occ, (30.0, -5.4, 33.6, 1.4), 3)
# the inside stair: from the dispatch floor (bottom at x 31.9) up west to the deck (top step ends at x 26.5)
ST_Z0 = grid_z(6.5) - 0.8; ST_Z1 = ST_Z0 + 1.6
ST_TOP = 26.5
ST_BOTTOM = stairs_x(ST_TOP, ST_Z0, ST_Z1, 1, 0.0, DECK)
keep_door(ST_BOTTOM + 0.8, (ST_Z0 + ST_Z1) / 2, False, 1.6, 1.0)
t.mark(t.occ, (ST_TOP - 1.2, ST_Z0 - 0.3, ST_BOTTOM + 1.2, ST_Z1 + 0.3), 3)
# the deck slab (y 4.6..4.8) over x 20..34, with a hole over the stair
DS0 = DECK - 0.2
t.block(MX, DS0, IZ0, IX1, DECK, ST_Z0 - 0.1, (0.40, 0.41, 0.42))
t.block(MX, DS0, ST_Z1 + 0.1, IX1, DECK, IZ1, (0.40, 0.41, 0.42))
t.block(MX, DS0, ST_Z0 - 0.1, ST_TOP, DECK, ST_Z1 + 0.1, (0.40, 0.41, 0.42))
t.block(ST_BOTTOM + 0.05, DS0, ST_Z0 - 0.1, IX1, DECK, ST_Z1 + 0.1, (0.40, 0.41, 0.42))
# downstairs ceiling lights (cosmetic panels under the deck)
for (x, z) in ((27, -23), (27, -20), (24, -10), (30, -10), (24, 0), (30, 0), (27, 17), (27, 21)):
    t.mesh("box", x, z, (0.95, 0.93, 0.85), y=DS0 - 0.06, width=1.2, height=0.05, depth=0.5)

# upstairs: north offices (z -27.6..-17), south offices (13..23.6), the open mezzanine between them
UH = 3.0     # upstairs wall height (ceiling slab on top)
wall_z(IZ0, -17.0, MX + 0.6, MX + 0.85, UH, OFFICE, [WIN(-25.5, 2.6), WIN(-20.0, 2.6)], y0=DECK)
wall_x(MX + 0.6, IX1, -17.25, -17.0, UH, OFFICE, [DOOR(23.0), WIN(28.0, 2.6)], y0=DECK)
wall_x(MX + 0.85, IX1, -22.5, -22.35, UH, OFFICE, [DOOR(31.5, 1.2)], y0=DECK)     # partition: manager's office | open plan
t.block(MX + 0.5, DECK + UH, IZ0, IX1, DECK + UH + 0.2, -17.0, (0.74, 0.73, 0.70))
wall_z(13.0, IZ1, MX + 0.6, MX + 0.85, UH, OFFICE, [WIN(16.0, 2.6), WIN(21.0, 2.6)], y0=DECK)
wall_x(MX + 0.6, IX1, 13.0, 13.25, UH, OFFICE, [DOOR(23.0), WIN(28.0, 2.6)], y0=DECK)
t.block(MX + 0.5, DECK + UH, 13.0, IX1, DECK + UH + 0.2, IZ1, (0.74, 0.73, 0.70))
# the outside stair on the east wall up to the upstairs door: a landing at deck height, steps down south
LAND = (HX1, EAST_UP_DOOR - 1.6, HX1 + 1.8, EAST_UP_DOOR + 1.0)
t.block(LAND[0], DS0, LAND[1], LAND[2], DECK, LAND[3], STEEL)
EXT_X0 = HX1 + 0.1; EXT_X1 = HX1 + 1.7
ext_bottom = stairs_z(LAND[3], EXT_X0, EXT_X1, 1, 0.0, DECK)
t.mark(t.occ, (EXT_X0 - 0.4, LAND[1] - 0.4, EXT_X1 + 0.6, ext_bottom + 1.2), 3)
for z in (LAND[1], LAND[3]):
    t.mesh("box", HX1 + 1.75, (LAND[1] + LAND[3]) / 2, YELLOW, y=DECK, width=0.06, height=1.05, depth=LAND[3] - LAND[1])

# ---------------------------------------------------------------- catwalks over the racking aisles
CW_X0 = -22.6     # west end (the top of the stairs down)
RAIL_Y = DECK
for cz in (CW1, CW2):
    t.block(CW_X0, DS0, cz - CW_HALF, MX, DECK, cz + CW_HALF, GRATE)
    t.mesh("box", (CW_X0 + MX) / 2, cz, (0.34, 0.36, 0.38), y=DECK, width=MX - CW_X0, height=0.02, depth=CW_HALF * 2 - 0.1, material="Grid")
    bottom = stairs_x(CW_X0, cz - CW_HALF, cz + CW_HALF, -1, 0.0, DECK)
    t.mark(t.occ, (bottom - 1.4, cz - CW_HALF - 0.4, CW_X0 + 0.4, cz + CW_HALF + 0.4), 3)
    # railings both sides (3 m props, only the top rail collides) and hanging supports
    x = CW_X0 + 1.5
    while x <= MX - 1.4:
        for side in (-1, 1):
            t.prop("Safety Railing", x, cz + side * (CW_HALF - 0.05), 0, y=RAIL_Y, check=False, tag="catwalk")
        x += 3.0
    for x in range(int(CW_X0) + 3, int(MX), 8):
        for side in (-1, 1):
            t.mesh("box", x, cz + side * (CW_HALF + 0.05), STEEL, y=DECK, width=0.06, height=WH - DECK, depth=0.06)
# the mezzanine edge (x = 20) railing, open where the catwalks join and at the inside stair's head
z = -17.0 + 1.5
while z <= 13.0 - 1.5:
    if all(abs(z - cz) > CW_HALF + 1.5 for cz in (CW1, CW2)):
        t.prop("Safety Railing", MX + 0.06, z, 90, y=RAIL_Y, check=False, tag="catwalk")
    z += 3.0
# stair railings (cosmetic)
for cz in (CW1, CW2):
    for side in (-1, 1):
        t.mesh("box", CW_X0 - 2.7, cz + side * (CW_HALF + 0.03), YELLOW, y=1.0, width=5.6, height=0.05, depth=0.05, pitch=0, roll=-41.6)

# ---------------------------------------------------------------- pallet racking
RACK_LOADED = ["Pallet Racking Bay", "Pallet Racking Bay", "Pallet Racking Bay Wrapped"]
OPEN_BAYS = []      # (x, z) of open bays (barrels go on their shelves)


def rack_row(z_min, x0, bays, seed):
    """A back-to-back row of racking from x0 eastwards: two 1.1 m bays deep (z_min..z_min + 2.2)."""
    r = random.Random(seed)
    rect = (x0, z_min, x0 + bays * 2.8 + 0.02, z_min + 2.2)
    if not t.free(rect, -0.1) or t.in_keepout(rect, 0.3):
        print("WARN rack row blocked", rect)
        return
    for k in range(bays):
        x = x0 + 1.4 + k * 2.8
        for zc in (z_min + 0.55, z_min + 1.65):
            name = "Pallet Racking Bay Open" if r.random() < 0.28 else r.choice(RACK_LOADED)
            t.prop(name, x, zc, 0, check=False, tag="rack")
            if name.endswith("Open"):
                OPEN_BAYS.append((x, zc))
    t.mark(t.occ, rect); t.mark(t.walk, rect); t.mark(t.tall, rect)


ROWS = []
ROWS_ISL = []
# rows hugging the two catwalk aisles, then more rows outwards (3.6 m aisles) while a 3 m wall aisle is left
for cz in (CW1, CW2):
    ROWS.append(cz - 2.3 - 2.2)
    ROWS.append(cz + 2.3)
zr = CW1 - 2.3 - 2.2
while zr - 3.6 - 2.2 - IZ0 >= 3.0:
    zr -= 3.6 + 2.2
    ROWS.append(zr)
zr = CW2 + 2.3
while IZ1 - (zr + 2.2 + 3.6 + 2.2) >= 3.0:
    zr += 2.2 + 3.6
    ROWS.append(zr)
RACK_WEST = (-21.0, 6)      # x0, bays: x -21.0..-4.2
RACK_EAST = (-0.2, 6)       # x -0.2..16.6
for k, zm in enumerate(sorted(ROWS)):
    rack_row(zm, RACK_WEST[0], RACK_WEST[1], 100 + k)
    rack_row(zm, RACK_EAST[0], RACK_EAST[1], 200 + k)
# islands of racking in the staging band (staggered, so no line runs down the band from dock to dispatch)
for (x0, zm, bays) in ((-19.6, -8.6 + 0.0, 2), (-18.0, -1.0, 2), (6.0, -6.0, 2), (9.0, -1.6, 2)):
    rack_row(zm, x0, bays, int(300 + x0 * 7))
    ROWS_ISL.append((x0, zm, bays))
# rack end guards and labels (cosmetic)
for zm in ROWS:
    for xe in (RACK_WEST[0] - 0.15, -4.2 + 0.15, RACK_EAST[0] - 0.15, 16.6 + 0.15):
        t.mesh("box", xe, zm + 1.1, YELLOW, y=0.0, width=0.2, height=0.6, depth=2.2)

# ======================================================================================== explosives in the hall
def barrel_pallet(x, z, yaw=0.0, n=4, name="Red Fuel Barrel"):
    """Four (or fewer) barrels on a pallet: the pallet on the floor, the barrels standing on it."""
    if not far_from_spawns(x, z, 15.5):
        return 0
    if not t.prop("Pallet", x, z, yaw, tag="pallet"):
        return 0
    a = math.radians(yaw); c, s = math.cos(a), math.sin(a)
    placed = 0
    for (lx, lz) in ((-0.3, -0.2), (0.3, -0.2), (-0.3, 0.2), (0.3, 0.2))[:n]:
        bx = x + lx * c + lz * s; bz = z - lx * s + lz * c
        if bang(name, bx, bz, rng.uniform(0, 360), y=0.144, check=False):
            placed += 1
    return placed


def barrel_line(a, b, spacing=2.4, jitter=0.25, name="Red Fuel Barrel"):
    """A line of barrels from a to b: a fuse line, each within the next one's blast."""
    L = math.hypot(b[0] - a[0], b[1] - a[1]); n = int(L / spacing) + 1
    placed = 0
    for k in range(n):
        f = k / max(1, n - 1)
        x = a[0] + (b[0] - a[0]) * f + rng.uniform(-jitter, jitter); z = a[1] + (b[1] - a[1]) * f + rng.uniform(-jitter, jitter)
        if bang(name, x, z, rng.uniform(0, 360)):
            placed += 1
    return placed


def barrel_cluster(cx, cz, n, r=1.4, name="Red Fuel Barrel"):
    placed = 0
    for k in range(n * 6):
        if placed >= n:
            break
        a = rng.uniform(0, math.tau); d = r * math.sqrt(rng.random())
        if bang(name, cx + math.cos(a) * d, cz + math.sin(a) * d, rng.uniform(0, 360), check=True):
            placed += 1
    return placed


# dock staging (between the dock doors and the racks): barrel pallets, forklifts, pallet stacks, wrapped loads
for (x, z) in ((-26.5, -25.5), (-26.0, -16.5), (-24.0, -5.5), (-26.0, 1.0), (-24.5, 13.0), (-26.0, 21.0), (-23.0, -25.0), (-23.5, 21.5)):
    barrel_pallet(x, z, rng.choice([0, 90]))
# barrels on the open bays' lower shelves (and some on the upper shelf)
for (x, z) in OPEN_BAYS:
    for lx in (-0.95, -0.35, 0.3, 0.9):
        if rng.random() < 0.75:
            bang("Red Fuel Barrel", x + lx, z, rng.uniform(0, 360), y=1.5, check=False)
    if rng.random() < 0.5:
        bang("Red Fuel Barrel", x - 0.68, z, rng.uniform(0, 360), y=3.0, check=False)
# fuse lines along the staging band (lead from the docks' side to the east and back) and down the cross aisle
barrel_line((-13.0, -8.85), (-9.5, -8.85), 1.75, jitter=0.1)
barrel_line((1.5, 1.65), (8.0, 1.65), 2.1, jitter=0.1)
barrel_line((-2.3, -23.8), (-2.3, -18.0), 2.3)
barrel_line((-2.3, 13.5), (-2.3, 21.0), 2.3)
barrel_cluster(2.5, -8.4, 5); barrel_cluster(-16.0, -3.8, 4); barrel_cluster(14.5, -4.0, 4)
# the mezzanine: two barrel pallets by the rail (Red's own risk) and one in each upstairs office corridor
for (x, z) in ((22.0, -8.0), (22.0, 1.0)):
    t.prop("Pallet", x, z, 0, y=DECK, check=False, tag="mezz")
    for (lx, lz) in ((-0.3, -0.2), (0.3, -0.2), (-0.3, 0.2), (0.3, 0.2)):
        bang("Red Fuel Barrel", x + lx, z + lz, rng.uniform(0, 360), y=DECK + 0.144, check=False)
# propane and gas by the forklift charging bay (south-east corner of the racks) and the shop door
for k, (x, z) in enumerate(((18.2, 21.6), (18.8, 22.4), (17.6, 22.4), (18.2, 20.6))):
    bang("Propane Tank", x, z, 0)
bang("Gas Bottle Cage", 18.5, -26.2, 0)
bang("Flammables Cabinet", -9.0, IZ0 + 0.6, 0)
bang("Flammables Cabinet", 4.5, IZ1 - 0.6, 180)

# forklifts, pallet jacks, pallets, wrapped loads and cartons on the hall floor
for (x, z, yaw) in ((-22.5, -2.5, 90), (3.5, -0.5, 0), (-25.5, 11.5, 180), (12.0, -26.0, 0), (-17.5, 20.5, 180), (-14.0, 19.5, 0)):
    t.prop("Forklift", x, z, yaw, tag="hall")
for (x, z) in ((-13.0, -6.5), (12.5, 2.5), (-27.0, -12.0), (-1.0, 17.0), (15.0, -20.0), (-8.0, 24.5 - 2.0)):
    t.prop("Pallet Jack", x, z, rng.uniform(0, 360), tag="hall")
for (x, z) in ((-14.5, 1.5), (-10.5, 3.3), (2.0, -7.6), (6.0, -7.0), (12.0, 3.0), (-18.5, -6.5), (15.5, -4.0), (-27.0, -21.0), (-27.0, 5.0), (-23.0, 17.0),
               (-1.0, -26.0), (1.5, 22.0), (-8.0, -26.0)):
    t.prop(rng.choice(["Pallet Stack", "Cardboard Stack", "Wooden Crate", "Pallet Stack", "IBC Tote", "Cardboard Box Large", "Chemical Drum"]), x, z,
           rng.choice([0, 90]) + rng.uniform(-6, 6), tag="hall")
# a conveyor line at the dispatch side and the packing benches
t.prop("Conveyor Rollers", 18.8, -10.5, 90, tag="hall"); t.prop("Conveyor Rollers", 18.8, -7.5, 90, tag="hall")
for (x, z) in ((24.0, -12.0), (24.0, 8.5)):
    t.prop("Table", x, z, 0, tag="hall")
t.prop("Cardboard Stack", 22.5, -14.5, 0, tag="hall"); t.prop("Cardboard Box Large", 25.8, -14.8, 15, tag="hall")
t.prop("Metal Shelving", 32.6, -14.0, 270, tag="hall"); t.prop("Metal Shelving", 32.6, -9.5, 270, tag="hall")
t.prop("Water Cooler", 33.0, -6.0, 270, tag="hall"); t.prop("Vending Machine", 33.0, 3.6, 270, tag="hall")
t.prop("Archive Boxes", 24.5, 2.0, 0, tag="hall")
# hall lighting: high-bay lamps under the roof (a few flicker), alarm beacons by the roller doors
for x in range(-24, 32, 10):
    for z in (-21.0, -8.0, 3.0, 17.0):
        t.prop("High Bay Light Flickering" if rng.random() < 0.15 else "High Bay Light", x, z, 0, y=WH - 1.45, check=False, tag="light")
for (x, z, yaw) in ((-14.0 - 2.8, HZ0 + 0.35, 0), (-12.0 + 2.8, HZ1 - 0.35, 180), (HX0 + 0.35, DOCKS[1] + 2.8, 90)):
    t.prop("Hazard Placard", x, z, yaw, y=3.0, check=False, tag="sign")
for z in DOCKS:
    for side in (-1, 1):
        t.prop("Dock Bumper", HX0 - 0.02, z + side * 2.5, 270, y=0.6, check=False, tag="dock")

# ---------------------------------------------------------------- the annex rooms
br = tk.Room(t, MX + 0.6, HZ0 + 0.1, HX1 + 0.1, -17.0, (1, 0), wall=0.5)
tk.furnish(t, br, "kitchen", random.Random(51), door_u=-22.0 - br.rect[1], back_door_u=-21.0 - br.rect[1])
for (x, z) in ((26.0, -24.5), (26.0, -20.5)):
    t.prop("Table", x, z, 0, tag="interior")
    for dz in (-0.75, 0.75):
        t.prop("Plastic Chair", x, z + dz, 0 if dz < 0 else 180, tag="interior")
t.prop("Vending Machine", 21.6, -18.1, 90, tag="interior"); t.prop("Glowing Vending Machine", 21.6, -19.1, 90, tag="interior")
# maintenance shop: workbenches along the walls, a running generator, gas bottles by the welding bay, tyres, an engine
for (x, z, yaw, name) in ((33.0, 15.0, 270, "Metal Shelving"), (33.0, 22.0, 270, "Metal Lockers"), (24.0, 23.0, 180, "Metal Shelving"),
                          (27.5, 23.0, 180, "Kitchen Counter"), (22.0, 15.5, 90, "Diesel Generator"), (30.0, 16.2, 0, "Engine Block"),
                          (26.0, 16.0, 0, "Tyre Stack"), (24.0, 19.5, 0, "Running Generator"), (31.0, 20.5, 0, "Golf Cart")):
    t.prop(name, x, z, yaw, tag="shop")
t.prop("Toolbox", 27.5, 23.0, 0, y=0.92, check=False, tag="shop"); t.prop("Paint Cans", 28.3, 23.1, 0, y=0.92, check=False, tag="shop")
bang("Gas Bottle", 21.4, 22.6, 0); bang("Gas Bottle", 21.75, 22.9, 0); bang("Gas Bottle", 22.1, 22.6, 0)
bang("Red Fuel Barrel", 33.0, 17.5, 0)

# upstairs offices (placed by hand at deck height)
Y = DECK
for (x, z) in ((23.5, -25.5), (27.0, -25.5), (30.5, -25.5), (23.5, -20.2), (27.0, -20.2)):
    t.prop("Office Desk", x, z, 0, y=Y, check=False, tag="office"); t.prop("Office Chair", x, z + 0.8, 180, y=Y, check=False, tag="office")
    t.prop("Computer Monitor", x, z - 0.15, 0, y=Y + 0.75, check=False, tag="office")
t.prop("Office Desk", 31.8, -19.8, 270, y=Y, check=False, tag="office"); t.prop("Office Chair", 32.7, -19.8, 90, y=Y, check=False, tag="office")
t.prop("Laptop", 31.8, -19.8, 270, y=Y + 0.75, check=False, tag="office"); t.prop("Safe", 33.0, -18.0, 270, y=Y, check=False, tag="office")
t.prop("Filing Cabinet", 21.5, -27.0, 0, y=Y, check=False, tag="office"); t.prop("Filing Cabinet", 22.3, -27.0, 0, y=Y, check=False, tag="office")
t.prop("Photocopier", 33.0, -26.5, 270, y=Y, check=False, tag="office"); t.prop("Water Cooler", 21.4, -17.8, 90, y=Y, check=False, tag="office")
t.prop("Whiteboard", 29.0, -22.7, 0, y=Y, check=False, tag="office"); t.prop("Office Plant", 33.1, -23.5, 0, y=Y, check=False, tag="office")
t.prop("Conference Table", 27.5, 18.0, 90, y=Y, check=False, tag="office")
for dz in (-1.2, 0.0, 1.2):
    for dx in (-0.85, 0.85):
        t.prop("Office Chair", 27.5 + dx, 18.0 + dz, 90 if dx < 0 else 270, y=Y, check=False, tag="office")
t.prop("Archive Boxes", 22.0, 22.6, 0, y=Y, check=False, tag="office"); t.prop("Metal Shelving", 33.1, 22.0, 270, y=Y, check=False, tag="office")
t.prop("Metal Shelving", 33.1, 16.0, 270, y=Y, check=False, tag="office"); t.prop("Server Rack", 21.6, 15.0, 90, y=Y, check=False, tag="office")
t.prop("Whiteboard", 24.0, 23.2, 180, y=Y, check=False, tag="office")
# the open mezzanine: crates and a few pallets to fight round, a desk at the rail (the foreman's spot)
for (x, z, name, yaw) in ((24.5, -10.5, "Wooden Crate", 10), (29.0, -7.0, "Pallet Stack", 0), (23.0, -3.0, "Cardboard Stack", 90), (31.5, 0.5, "Wooden Crate", 0),
                          (24.0, 5.0, "Pallet Stack", 90), (30.5, 10.5, "Cardboard Box Large", 30), (22.0, -15.0, "Office Desk", 90), (32.6, -3.5, "Metal Lockers", 270)):
    t.prop(name, x, z, yaw, y=Y, check=False, tag="mezz")
t.prop("Office Chair", 22.9, -15.0, 90, y=Y, check=False, tag="mezz")

# ======================================================================================== the west loading yard (Blue)
# semi trailers backed onto docks 1 and 3 (a gap behind them), truck cabs hitched; docks 2 and 4 open
for z in (DOCKS[0], DOCKS[2]):
    t.prop("Semi Trailer", HX0 - 1.3 - 5.9, z, 0, tag="yard")
    t.prop("Truck Cab", HX0 - 1.3 - 11.8 - 0.6, z, 180, check=False, tag="yard")
t.prop("Box Truck", -46.0, DOCKS[1] + 0.5, 0, tag="yard")
# a container stack (south-west): rows of containers, two high in places, with gaps to run through
CONTAINERS = []


def container(x, z, along_x, color, y=0.0):
    L, W, H = 6.06, 2.44, 2.59
    hx, hz = (L / 2, W / 2) if along_x else (W / 2, L / 2)
    t.block(x - hx, y, z - hz, x + hx, y + H, z + hz, color)
    # corrugation ribs and door end (cosmetic)
    for k in range(-2, 3):
        o = k * 1.1
        if along_x:
            t.mesh("box", x + o, z, tuple(c * 0.85 for c in color), y=y + 0.05, width=0.08, height=H - 0.1, depth=W + 0.04)
        else:
            t.mesh("box", x, z + o, tuple(c * 0.85 for c in color), y=y + 0.05, width=W + 0.04, height=H - 0.1, depth=0.08)
    CONTAINERS.append((x, z, along_x, y))


for k, (x, z, ax, two) in enumerate(((-76.0, 26.0, True, True), (-69.5, 26.0, True, False), (-76.0, 32.5, True, False), (-66.0, 33.0, False, True),
                                     (-58.0, 34.0, True, True), (-74.0, 39.5, True, True), (-61.0, 41.0, True, False), (-52.0, 41.5, False, False),
                                     (-82.0, -34.0, False, True), (-86.0, -28.0, True, False))):
    col = CONT[k % len(CONT)]
    container(x, z, ax, col)
    if two:
        container(x + (0.2 if ax else 0), z + (0 if ax else 0.2), ax, CONT[(k + 2) % len(CONT)], y=2.59)
# truck park (north-west): trailers and box trucks nose to tail, a guard hut at the gate
t.prop("Semi Trailer", -56.0, -44.0, 0, tag="yard"); t.prop("Truck Cab", -62.5, -44.0, 180, check=False, tag="yard")
t.prop("Semi Trailer", -46.0, -48.5, 8, tag="yard")
t.prop("Box Truck", -70.0, -22.0, 90, tag="yard"); t.prop("Utility Trailer", -64.0, -30.0, 0, tag="yard")
t.prop("Pickup Truck", -52.0, -28.0, 180, tag="yard"); t.prop("Van", -78.0, 20.0, 90, tag="yard")
# parked trucks screening the spawn yard from the docks (gaps between them to run through)
for (x, z, name) in ((-74.0, -14.5, "Box Truck"), (-75.0, 9.5, "Box Truck"), (-73.0, -1.5, "Semi Trailer")):
    t.prop(name, x, z, 90, tag="yard")
for (x, z) in ((-80.0, -22.0), (-80.5, 22.0), (-79.0, 4.0), (-79.0, -8.0)):
    t.prop(rng.choice(["Concrete Block", "Water Barrier", "Pallet Stack", "Cable Spool"]), x, z, 90, tag="yard")
GH = (-74.0, -46.0, -69.0, -41.5)    # guard hut
wall_x(GH[0], GH[2], GH[1], GH[1] + 0.25, 2.8, BLOCKW, [WIN(-71.5, 2.4)])
wall_x(GH[0], GH[2], GH[3] - 0.25, GH[3], 2.8, BLOCKW, [DOOR(-70.3, 1.2), WIN(-72.8, 1.4)])
wall_z(GH[1] + 0.25, GH[3] - 0.25, GH[0], GH[0] + 0.25, 2.8, BLOCKW, [WIN(-43.75, 1.6)])
wall_z(GH[1] + 0.25, GH[3] - 0.25, GH[2] - 0.25, GH[2], 2.8, BLOCKW, [WIN(-43.75, 1.6)])
t.block(GH[0] - 0.3, 2.8, GH[1] - 0.3, GH[2] + 0.3, 3.05, GH[3] + 0.3, ROOF)
keep_door(-70.3, GH[3], True, 1.2)
t.prop("Office Desk", -72.5, -44.8, 0, check=False, tag="hut"); t.prop("Office Chair", -72.5, -44.0, 180, check=False, tag="hut")
t.prop("Desk Phone", -72.2, -44.9, 0, y=0.75, check=False, tag="hut"); t.prop("Monitor Screen Saver", -72.9, -44.9, 0, y=0.75, check=False, tag="hut")
t.prop("Boom Barrier", -78.5, -38.0, 90, tag="yard")
# yard dressing: pallet stacks, tyres, skips, cones, floodlight towers, a fuel depot corner with drums and cans
for (x, z) in ((-40.0, -24.0), (-38.0, 22.5), (-50.0, 18.0), (-54.0, -14.0), (-44.0, 11.0), (-36.0, -3.0), (-48.0, -2.0), (-60.0, 8.0), (-56.0, 24.0),
               (-66.0, -10.0), (-68.0, 12.0), (-42.0, -36.0), (-35.0, -45.0), (-52.0, -36.0)):
    t.prop(rng.choice(["Pallet Stack", "Pallet Stack", "Wooden Crate", "Tyre Stack", "Cable Spool", "Concrete Block", "Water Barrier", "Sandbag Wall", "Pipe Stack"]),
           x + rng.uniform(-1, 1), z + rng.uniform(-1, 1), rng.choice([0, 90]) + rng.uniform(-10, 10), tag="yard")
for (x, z, yaw) in ((-38.0, 30.0, 0), (-64.0, -52.0, 10)):
    t.prop("Builders Skip", x, z, yaw, tag="yard")
for (x, z, yaw) in ((-36.0, -27.5, 135), (-36.0, 27.0, 45), (-60.0, -20.0, 90), (-60.0, 15.0, 90)):
    t.prop("Floodlight Tower", x, z, yaw, tag="yard")
for (x, z) in ((-33.0, DOCKS[1] - 3.5), (-33.0, DOCKS[1] + 3.5), (-33.0, DOCKS[3] - 3.5), (-33.0, DOCKS[3] + 3.5), (-44.0, -1.5), (-43.0, 1.5)):
    t.prop("Traffic Cone", x, z, 0, tag="yard")
# the fuel depot (north-west of the docks): drums in rows and on pallets, a bowser, jerry cans
barrel_pallet(-44.0, -20.5, 0); barrel_pallet(-41.5, -20.5, 0); barrel_pallet(-44.0, -17.5, 90, 3)
barrel_line((-48.5, -24.0), (-48.5, -16.0), 2.0)
for (x, z) in ((-46.5, -15.5), (-45.5, -15.2), (-41.0, -16.5)):
    bang("Jerry Can", x, z, rng.uniform(0, 360))
# by the trailers: barrels waiting to load (lines to light towards the docks)
barrel_line((-38.0, DOCKS[0] + 3.0), (-38.0, DOCKS[0] + 7.0), 2.0)
barrel_line((-37.0, DOCKS[2] - 4.0), (-37.0, DOCKS[2] - 7.0), 1.6)
barrel_pallet(-34.0, 9.5, 0); barrel_pallet(-34.0, -14.5, 90)
barrel_cluster(-56.0, 30.0, 6)      # by the container stack

# ======================================================================================== the north yard: chemical store, tank farm
CS = (-10.0, -54.0, 12.0, -42.0)     # chemical store (walk-in): doors S (two) and W, E
wall_x(CS[0], CS[2], CS[1], CS[1] + 0.3, 4.2, BLOCKW, [WIN(-4.0, 2.0), WIN(6.0, 2.0)])
wall_x(CS[0], CS[2], CS[3] - 0.3, CS[3], 4.2, BLOCKW, [DOOR(-5.0, 2.2), DOOR(6.0, 2.2), WIN(1.0, 3.0)])
wall_z(CS[1] + 0.3, CS[3] - 0.3, CS[0], CS[0] + 0.3, 4.2, BLOCKW, [DOOR(-48.0, 2.0)])
wall_z(CS[1] + 0.3, CS[3] - 0.3, CS[2] - 0.3, CS[2], 4.2, BLOCKW, [DOOR(-48.0, 2.0)])
t.block(CS[0] - 0.4, 4.2, CS[1] - 0.4, CS[2] + 0.4, 4.5, CS[3] + 0.4, ROOF)
slab(CS[0] + 0.3, CS[1] + 0.3, CS[2] - 0.3, CS[3] - 0.3, (0.48, 0.50, 0.46), "Tiles", h=0.04)
for (x, z, al) in ((-5.0, CS[3], True), (6.0, CS[3], True), (CS[0], -48.0, False), (CS[2], -48.0, False)):
    keep_door(x, z, al, 2.2)
t.prop("Hazard Placard", -0.5, CS[3] + 0.02, 0, y=2.6, check=False, tag="sign"); t.prop("Hazard Placard", 2.5, CS[3] + 0.02, 0, y=2.6, check=False, tag="sign")
# inside: drum racks along the north wall, flammables cabinets, gas cages, chemical drums, barrels on pallets in the middle
for k, x in enumerate((-8.4, -7.1, -5.8, -2.0, -0.7, 0.6, 3.6, 4.9, 6.2, 9.0, 10.3)):
    if k % 3 == 2:
        bang("Red Fuel Barrel", x, CS[1] + 0.95, 0)
    else:
        t.prop("Chemical Drum", x, CS[1] + 0.95, rng.uniform(0, 360), tag="chem")
for (x, z, yaw) in ((CS[0] + 0.9, -51.5, 90), (CS[0] + 0.9, -45.0, 90), (CS[2] - 0.9, -51.5, 270), (CS[2] - 0.9, -45.0, 270)):
    bang("Flammables Cabinet", x, z, yaw)
bang("Gas Bottle Cage", 1.0, CS[1] + 1.0, 0)
barrel_pallet(-4.5, -49.0, 0); barrel_pallet(4.5, -49.0, 90); barrel_pallet(0.0, -47.0, 0, 3)
t.prop("IBC Tote", -7.5, -46.5, 0, tag="chem"); t.prop("IBC Tote", 8.8, -46.5, 0, tag="chem")
# outside the chemical store: a bund of chemical drums and IBCs on the east side, a spill kit, a forklift
for (x, z) in ((14.0, -52.5), (14.0, -50.8), (15.4, -52.5)):
    t.prop("IBC Tote", x, z, 0, tag="chem")
for (x, z) in ((-12.5, -52.0), (-12.5, -50.8), (-13.6, -51.4)):
    t.prop("Chemical Drum", x, z, rng.uniform(0, 360), tag="chem")
t.prop("Forklift", -16.0, -45.0, 90, tag="chem")
# the tank farm (north-east): three bulk propane tanks in a low concrete bund, propane bottle cages, a valve
for (x, z) in ((26.0, -57.0), (26.0, -52.5), (32.0, -57.0)):
    bang("Propane Bulk Tank", x, z, 0)
for (x0, z0, x1, z1) in ((21.0, -60.0, 37.0, -59.6), (21.0, -49.6, 30.0, -49.2), (20.6, -60.0, 21.0, -49.2), (37.0, -60.0, 37.4, -54.0)):
    t.block(x0, 0, z0, x1, 0.9, z1, CONC_D)          # the bund wall (vaultable)
t.prop("Gas Valve", 34.5, -51.5, 0, tag="tank"); t.prop("Chain Link Fence", 39.0, -52.0, 90, tag="tank")
for (x, z) in ((40.0, -46.0), (41.0, -46.0), (40.5, -45.2)):
    bang("Propane Tank", x, z, 0)
bang("Gas Bottle Cage", 15.0, -44.0, 0)
# north-west of the hall: pallet yard and a skip
for (x, z) in ((-24.0, -44.0), (-26.0, -40.0), (-30.0, -50.0), (-36.0, -38.0), (-20.0, -56.0), (16.0, -38.0), (22.0, -41.0), (46.0, -32.0)):
    t.prop(rng.choice(["Pallet Stack", "Pallet Stack", "Wooden Crate", "Cable Spool", "Pipe Stack", "Tyre Stack"]), x, z, rng.choice([0, 90]), tag="north")
t.prop("Dumpster", 3.5, HZ0 - 0.8 - 6.5, 0, tag="north")

# ======================================================================================== the south yard: rail spur, canopy, stock
RAIL_Z = 44.0
for dz in (-0.75, 0.75):
    t.mesh("box", -10.0, RAIL_Z + dz, (0.42, 0.40, 0.38), y=0.0, width=96.0, height=0.12, depth=0.1, material="Metal")
for x in range(-56, 38, 2):
    t.mesh("box", x, RAIL_Z, RAIL, y=0.0, width=0.25, height=0.08, depth=2.4, material="Wood")
# a boxcar on the spur (solid block, cosmetic details) and a flat wagon of crates
t.block(-40.0, 0.0, RAIL_Z - 1.45, -26.0, 3.8, RAIL_Z + 1.45, (0.46, 0.22, 0.16))
t.mesh("box", -33.0, RAIL_Z, (0.36, 0.17, 0.12), y=3.8, width=14.2, height=0.12, depth=3.0)
t.mesh("box", -33.0, RAIL_Z + 1.46, (0.30, 0.15, 0.10), y=0.9, width=3.0, height=2.6, depth=0.05)
t.block(6.0, 0.0, RAIL_Z - 1.3, 18.0, 1.1, RAIL_Z + 1.3, (0.30, 0.30, 0.32))
for x in (7.5, 10.5, 13.5, 16.5):
    t.prop("Wooden Crate", x, RAIL_Z, rng.choice([0, 15]), y=1.1, check=False, tag="rail")
# loading canopy over the south roller door
for (px, pz) in ((-17.0, 31.0), (-7.0, 31.0)):
    t.block(px - 0.2, 0, pz - 0.2, px + 0.2, 4.8, pz + 0.2, STEEL)
t.mesh("box", -12.0, 27.6, (0.30, 0.42, 0.58), y=4.8, width=12.0, height=0.25, depth=7.6)
# stock in the yard: pallet stacks, IBCs, cable spools, pipe stacks; barrels by the boxcar; propane cages by the canopy
for (x, z) in ((-24.0, 36.0), (-28.5, 34.0), (-14.0, 39.0), (6.0, 33.5), (12.0, 37.0), (20.0, 33.0), (26.0, 38.5), (-46.0, 34.0), (-50.0, 39.0),
               (30.0, 31.5), (-34.0, 51.0), (0.0, 51.0), (14.0, 51.0), (-20.0, 50.0)):
    t.prop(rng.choice(["Pallet Stack", "IBC Tote", "Cable Spool", "Pipe Stack", "Wooden Crate", "Pallet Stack", "Concrete Pipe"]), x, z,
           rng.choice([0, 90]) + rng.uniform(-8, 8), tag="south")
barrel_line((-42.0, 40.0), (-42.0, 34.0), 2.0)
barrel_line((-24.5, 40.5), (-16.0, 40.5), 2.2)
barrel_pallet(-20.0, 30.0, 0); barrel_pallet(-4.0, 30.0, 0); barrel_pallet(19.0, 41.0, 90)
barrel_cluster(8.0, 30.0, 4)
barrel_cluster(-30.0, 40.5, 6); barrel_cluster(-12.0, 47.5, 5); barrel_line((24.0, 33.0), (30.0, 37.0), 2.0)
barrel_cluster(-30.0, -46.0, 5); barrel_line((-40.0, -40.0), (-34.0, -40.0), 2.0)
bang("Gas Bottle Cage", -9.0, 28.5, 0); bang("Gas Bottle Cage", 22.0, 46.5, 90)
for (x, z) in ((-1.5, 41.5), (-0.7, 41.5)):
    bang("Propane Tank", x, z, 0)
t.prop("Dumpster", 36.5, 30.5, 90, tag="south"); t.prop("Dumpster", 36.5, 33.0, 90, tag="south")

# ======================================================================================== the east side (Red): car park, office entrance
tk.car_park(t, 46, -26, 66, -6, random.Random(77), along_x=False, fill=0.55)
tk.car_park(t, 46, 6, 66, 26, random.Random(78), along_x=False, fill=0.5)
t.mesh("box", HX1 + 2.2, -2.0, (0.30, 0.42, 0.58), y=3.0, width=4.2, height=0.2, depth=5.0)   # entrance canopy over the dispatch door
for (x, z) in ((HX1 + 4.0, -4.3), (HX1 + 4.0, 0.3)):
    t.block(x - 0.12, 0, z - 0.12, x + 0.12, 3.0, z + 0.12, STEEL)
for (x, z, yaw) in ((37.0, -24.0, 90), (37.0, 22.0, 90)):
    t.prop("Bench", x, z, yaw, tag="east")
t.prop("Trash Can", 37.0, -26.0, 0, tag="east"); t.prop("Cigarette Bin", 37.0, 2.8, 0, tag="east")
t.prop("Dumpster", 38.0, 26.5, 90, tag="east"); t.prop("Wheelie Bin", 38.0, 29.0, 90, tag="east")
for (x, z) in ((40.0, -34.0), (54.0, -34.0), (68.0, -34.0), (40.0, 28.0), (54.0, 30.0), (70.0, 30.0)):
    t.prop("Street Lamp", x, z, 0, tag="east")
for (x, z) in ((72.0, -20.0), (72.0, -6.0), (72.0, 8.0), (72.0, 20.0), (68.0, -1.0)):
    t.prop(rng.choice(["Concrete Block", "Water Barrier", "Planter", "Concrete Block"]), x, z, 90, tag="east")
for (x, z) in ((42.0, -11.0), (42.0, 9.0), (41.0, -1.0)):
    t.prop("Bollard", x, z, 0, tag="east")
bang("Red Fuel Barrel", 36.4, 15.5, 0); bang("Red Fuel Barrel", 36.4, 16.3, 0); bang("Red Fuel Barrel", 37.1, 15.9, 0)   # by the shop roller door

# ======================================================================================== foliage and the edge
BX0, BZ0, BX1, BZ1 = t.x0, t.z0, t.x1, t.z1
t.region("grass", 3.0, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.5, scale=[0.8, 1.3], color=[0.42, 0.48, 0.26])
t.region("shrub", 0.35, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.6)
t.region("bush", 0.05, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.8, scale=[0.8, 1.3])
# rough ground along the edge: long grass, shrubs and bushes in a band inside the outline
ring = [(x, z) for (x, z) in t.outline[::12]]
for k in range(len(ring)):
    a = ring[k]; b = ring[(k + 1) % len(ring)]
    t.region("grass", 5.5, polygon=[[tk.R(a[0]), tk.R(a[1])], [tk.R(b[0]), tk.R(b[1])], [tk.R(b[0] * 0.93), tk.R(b[1] * 0.92)], [tk.R(a[0] * 0.93), tk.R(a[1] * 0.92)]],
             patchiness=0.3, scale=[1.0, 1.7], color=[0.40, 0.50, 0.24])
    t.region("shrub", 0.8, polygon=[[tk.R(a[0]), tk.R(a[1])], [tk.R(b[0]), tk.R(b[1])], [tk.R(b[0] * 0.95), tk.R(b[1] * 0.94)], [tk.R(a[0] * 0.95), tk.R(a[1] * 0.94)]], patchiness=0.4)
    t.region("bush", 0.25, polygon=[[tk.R(a[0]), tk.R(a[1])], [tk.R(b[0]), tk.R(b[1])], [tk.R(b[0] * 0.96), tk.R(b[1] * 0.95)], [tk.R(a[0] * 0.96), tk.R(a[1] * 0.95)]],
             patchiness=0.5, scale=[0.9, 1.5])
# weeds along the foot of the hall walls
for (a, b) in (((HX0 - 0.8, HZ0 - 0.8), (HX1 + 0.8, HZ0 - 0.8)), ((HX0 - 0.8, HZ1 + 0.8), (HX1 + 0.8, HZ1 + 0.8)), ((HX0 - 0.8, HZ0), (HX0 - 0.8, HZ1)),
               ((CS[0] - 0.7, CS[1] - 0.7), (CS[2] + 0.7, CS[1] - 0.7))):
    tk.weeds_along(t, a, b, width=1.0, density=5.0)
# trees round the edge (behind the fence line) and by the car park
for (x, z) in ((-94.0, -24.0), (-90.0, -36.0), (-70.0, -52.0), (-40.0, -58.0), (-12.0, -64.0), (14.0, -66.0), (46.0, -60.0), (66.0, -50.0), (84.0, -34.0),
               (90.0, 22.0), (76.0, 40.0), (48.0, 52.0), (16.0, 57.0), (-14.0, 57.0), (-44.0, 53.0), (-72.0, 48.0), (-92.0, 28.0), (78.0, -12.0), (78.0, 14.0),
               (52.0, -1.0), (60.0, -1.0), (-96.0, 14.0), (-80.0, 34.0), (88.0, -26.0), (70.0, 42.0)):
    t.tree(x + rng.uniform(-1.2, 1.2), z + rng.uniform(-1.2, 1.2), rng.uniform(7.0, 11.0), "pine" if rng.random() < 0.35 else "tree")
t.exclude_rect(46, -27, 66, -5, 0.1); t.exclude_rect(46, 5, 66, 27, 0.1)


def arc_of(x, z):
    pts = t.outline; best = (1e9, 0); s = 0.0
    for i in range(len(pts)):
        d = (pts[i][0] - x) ** 2 + (pts[i][1] - z) ** 2
        if d < best[0]:
            best = (d, s)
        s += math.hypot(pts[(i + 1) % len(pts)][0] - pts[i][0], pts[(i + 1) % len(pts)][1] - pts[i][1])
    return best[1]


def fence_run(s0, s1, name="Chain Link Fence", inset=0.9, spacing=3.0):
    """Chain-link panels along the outline from arc length s0 to s1, just inside it, end to end (unchecked: they only
    meet each other; anything solid already there wins)."""
    pts = t.outline; n = len(pts)
    arc = [0.0]
    for i in range(1, n + 1):
        arc.append(arc[-1] + math.hypot(pts[i % n][0] - pts[i - 1][0], pts[i % n][1] - pts[i - 1][1]))
    total = arc[-1]
    if s1 < s0:
        s1 += total
    def at(s):
        s %= total
        lo, hi = 0, n
        while hi - lo > 1:
            m = (lo + hi) // 2
            if arc[m] <= s:
                lo = m
            else:
                hi = m
        a = pts[lo]; b = pts[(lo + 1) % n]; f = (s - arc[lo]) / max(1e-6, arc[lo + 1] - arc[lo])
        return a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f
    count = 0; s = s0
    while s + spacing <= s1:
        (xa, za), (xb, zb) = at(s), at(s + spacing)
        dx, dz = xb - xa, zb - za; L = math.hypot(dx, dz) or 1.0
        ix, iz = -dz / L, dx / L
        x = (xa + xb) / 2 + ix * inset; z = (za + zb) / 2 + iz * inset
        r = t.foot(name, x, z, yaw_along(dx, dz))
        if t.inside(x, z, 0.3) and not t.in_keepout(r, 0.3) and t.free((x - 0.3, z - 0.3, x + 0.3, z + 0.3)):
            t.prop(name, x, z, yaw_along(dx, dz), check=False, margin=-2.5, tag="edge")
            count += 1
        s += spacing
    return count


EDGE = [((-98, 22), (-97, -33), "fence"), ((-97, -33), (-26, -64), "fence"), ((-26, -64), (58, -57), "hedge"), ((58, -57), (95, 10), "fence"),
        ((95, 10), (60, 49), "hedge"), ((60, 49), (-58, 55), "fence"), ((-58, 55), (-98, 22), "backs")]
edge_counts = {}
for (a, b, style) in EDGE:
    n = fence_run(arc_of(*a), arc_of(*b)) if style == "fence" else tk.edge_run(t, arc_of(*a), arc_of(*b), style, rng)[0]
    edge_counts[style] = edge_counts.get(style, 0) + n

# ======================================================================================== spawns, waypoints
for x, z in SPAWNS_ALL:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
t.ops.append({"op": "AutoWaypoints", "spacing": 6})
WAYPOINTS = [(-27.0, CW1 + 2.4), (-27.0, CW2 - 2.4), (ST_BOTTOM + 1.0, ST_Z1 + 1.0), (EXT_X0 + 0.8, ext_bottom + 1.6),
             (26.0, -22.5), (25.0, -15.5), (26.0, 18.0), (30.0, 10.0), (-3.6, -21.0), (-3.6, 12.2), (-12.5, -21.0), (8.0, -21.0), (-12.5, 12.2), (8.0, 12.2),
             (0.0, -45.0), (-8.0, -48.0), (8.0, -48.0), (-71.5, -43.5), (-18.0, CW1), (-18.0, CW2)]
for (x, z) in WAYPOINTS:
    if t.free((x - 0.35, z - 0.35, x + 0.35, z + 0.35)):
        t.ops.append({"op": "Waypoint", "x": tk.R(x), "z": tk.R(z)})
    else:
        print("WARN waypoint blocked", x, z)

# ======================================================================================== cosmetic clutter (runs last in the game)
LITTER = ["Litter", "Litter", "Flattened Cardboard", "Plastic Bag", "Soda Cans", "Bottle", "Newspaper", "Cardboard Box Small", "Open Cardboard Box",
          "Plank", "Tyre", "Trash Bag", "Rubble", "Cinder Blocks", "Car Wheel"]
t.clutter(LITTER, BX0, BZ0, -30, BZ1, 1100, 41)
t.clutter(LITTER, 34, BZ0, BX1, BZ1, 700, 42)
t.clutter(LITTER, -30, BZ0, 34, HZ0 - 0.5, 600, 43)
t.clutter(LITTER, -30, HZ1 + 0.5, 34, BZ1, 600, 44)
# the hall floor: cardboard, shrink-wrap, pallets bits, cans; the annex rooms; the chemical store
HALL_STUFF = ["Flattened Cardboard", "Open Cardboard Box", "Cardboard Box Small", "Plastic Bag", "Litter", "Plank", "Soda Cans", "Paint Cans", "Broom",
              "Mop and Bucket", "Bucket", "Toolbox", "Step Ladder", "Newspaper"]
t.clutter(HALL_STUFF, IX0 + 0.5, IZ0 + 0.5, MX - 0.5, IZ1 - 0.5, 700, 45)
t.clutter(HALL_STUFF, MX + 1.0, IZ0 + 0.5, IX1 - 0.5, IZ1 - 0.5, 140, 46)
t.clutter(["Paint Cans", "Bucket", "Litter", "Plastic Bag", "Flattened Cardboard", "Toolbox", "Mop and Bucket"], CS[0] + 0.5, CS[1] + 0.5, CS[2] - 0.5, CS[3] - 0.5, 50, 47)
t.clutter(["Pallet", "Plank", "Plank Pile", "Tyre", "Flattened Cardboard", "Cardboard Box Small"], -62, -26, -31, 26, 140, 48)

# ======================================================================================== the map
foliage = {"seed": 412, "surfaces": {"Plain": 0.1, "Tiles": 0.1}, "regions": t.regions, "items": t.items, "exclude": t.exclude}
mp = {
    "format": "call-of-slop-map/1",
    "name": "Powder Keg",
    "description": "A warehouse full of things that go bang: pallet racking, catwalks and offices in the hall, a loading yard with trucks at the docks, a chemical store, a propane tank farm and hundreds of red barrels.",
    "author": "Call of Slop",
    "order": 412,
    "maxPlayers": 16,
    "sky": "#9aa3ab",
    "skybox": {"preset": "overcast", "clouds": {"coverage": 0.85, "speed": 6}},
    "boundary": {"shape": "smooth", "points": [list(p) for p in CTRL], "noise": NOISE, "edge": "none", "groundMargin": 10, "barrierHeight": 3},
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": [{"op": "Ground", "color": [0.40, 0.40, 0.33]}] + t.ops,
    "foliage": foliage,
}


def dump(o, ind=0):
    pad = "  " * ind
    if isinstance(o, dict) and ind <= 1 and any(isinstance(v, (dict, list)) for v in o.values()):
        return "{\n" + ",\n".join(f'{pad}  "{k}": {dump(v, ind + 1)}' for k, v in o.items()) + "\n" + pad + "}"
    if isinstance(o, list) and ind <= 2 and o and isinstance(o[0], dict):
        return "[\n" + ",\n".join(pad + "  " + json.dumps(v) for v in o) + "\n" + pad + "]"
    return json.dumps(o)


with open(OUT_JSON, "w") as f:
    f.write(dump(mp) + "\n")

# ======================================================================================== report and checks
for x, z in SPAWNS_ALL + [(h[0], h[1]) for h in HILLS] + FLAGS:
    if not t.inside(x, z, 1.5):
        print("WARN objective near/outside the edge", x, z)
    if not t.free((x - 0.4, z - 0.4, x + 0.4, z + 0.4)):
        print("WARN objective inside something", x, z)
from collections import Counter
cats = Counter(); solid = 0; cosm = 0
for name, x, z, yaw, y, tag in t.props:
    p = t.P[name]; cats[p["cat"]] += 1
    if p["solid"]:
        solid += 1
    else:
        cosm += 1
blocks = sum(1 for o in t.ops if o["op"] == "Block")
boom = Counter(b[0] for b in BOOM)
print(f"area {t.area():.0f} m2, half {HALF_X:.1f} x {HALF_Z:.1f}, nav spacing {NAV_S:.2f}, catwalks z {CW1:.2f} / {CW2:.2f}, rows {sorted(round(r, 2) for r in ROWS)}")
print(f"blocks {blocks}, trees {len(t.items)}, meshes {t.meshes}, hand props {len(t.props)} (solid {solid}, cosmetic {cosm}), open bays {len(OPEN_BAYS)}")
print("props by category:", dict(sorted(cats.items())))
print("explosives:", sum(boom.values()), dict(boom))
print("edges:", edge_counts, "clutter asked:", sum(o["count"] for o in t.ops if o["op"] == "Clutter"))
near = min((math.hypot(x - sx, z - sz) - EXPLOSIVE[n], n, (round(x, 1), round(z, 1))) for n, x, z, y in BOOM for sx, sz in SPAWNS_ALL)
print("closest explosive to a spawn (minus blast radius):", near)
vis = [(a, b) for a in BLUE_SP for b in RED_SP if t.sees(a, b)]
print("blue-red spawn lines of sight:", len(vis))
fb = t.walk_field(BLUE_SP); fr = t.walk_field(RED_SP)
def avg_walk(field, sps, x, z):
    return t.walk_to(field, x, z)
for i, (x, z, r) in enumerate(HILLS):
    b = min(t.walk_to(fb, x + dx, z + dz) for dx, dz in ((0, 0), (0, 2.5), (0, -2.5), (2.5, 0), (-2.5, 0)))
    rd = min(t.walk_to(fr, x + dx, z + dz) for dx, dz in ((0, 0), (0, 2.5), (0, -2.5), (2.5, 0), (-2.5, 0)))
    print(f"hill {i + 1} ({x},{z}): walk blue {b:.0f} m red {rd:.0f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
b, rd = t.walk_to(fb, *FLAGS[1]), t.walk_to(fr, *FLAGS[0])
print(f"enemy flag: blue {b:.0f} m red {rd:.0f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
if OUT_PNG:
    marks = [("dot", x, z, 0.8, (40, 90, 240)) for x, z in BLUE_SP] + [("dot", x, z, 0.8, (240, 50, 50)) for x, z in RED_SP] + [("dot", x, z, 0.8, (250, 250, 250)) for x, z in FFA_SP]
    marks += [("circle", x, z, r, (255, 230, 0)) for x, z, r in HILLS] + [("dot", x, z, 1.0, (255, 255, 255)) for x, z in FLAGS]
    marks += [("dot", x, z, 0.35, (255, 0, 0)) for n, x, z, y in BOOM]
    marks += [("line", a, b, 0, (255, 0, 255)) for a, b in vis]
    t.preview(OUT_PNG, marks, scale=6)
