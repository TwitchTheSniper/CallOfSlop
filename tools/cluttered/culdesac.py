#!/usr/bin/env python3
"""Generator for Maps/Cluttered/04-CulDeSac.json: the Cluttered pack's 04 Cul-de-sac (small, keyhole, 10 players).

Usage: python3 tools/cluttered/culdesac.py [repo root] [preview.png]

A suburban cul-de-sac on trash day: the turning circle at the end of a residential street, a ring of houses round it
(two walk-in houses with a kitchen, a living room and a garage with a car, a walk-in duplex on the axis, four closed
houses), front lawns, driveways with parked cars, bins out at the kerb with bags and litter round them, streetlights,
a hydrant, backyards with fences and hedges, a pool and a duck pond, a trampoline and swings, barbecues and laundry
lines, AC units and meters beside the houses and vents and dishes on the roofs; road works close the street's mouth.

The map is mirrored about z = 0 (the street's axis): Blue holds the north half, Red the south. Everything solid is made
as a pair (x, z, yaw) and (x, -z, 180 - yaw) by placement.Site(mirror="z"); the duplex, the island and the road works
sit on the axis and are built once, symmetric. The placement rules (placement.py) put every prop where it belongs.
"""
import json
import math
import os
import random
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import placement as pl  # noqa: E402

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Cluttered", "04-CulDeSac.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None

# ------------------------------------------------------------------------------------------------ the keyhole outline
C = (-14.0, 0.0)      # centre of the turning circle (and of the round part of the keyhole)
RAD = 35.0            # radius of the round part
SW = 10.0             # half width of the street stub
XEND = 40.0           # the street's mouth (closed by road works)
JA = math.degrees(math.asin(SW / RAD))          # where the stub meets the circle
OUTLINE = pl.arc(C[0], C[1], RAD, JA, 360 - JA, step=6) + [(XEND, -SW), (XEND, SW)]
OUTLINE = [(round(x, 2), round(z, 2)) for x, z in OUTLINE]
NOISE = {"amplitude": 0.5, "scale": 9, "seed": 4}

lib = pl.PropLib(REPO, os.path.join(REPO, "Maps", "Cluttered"))
site = pl.Site(lib, outline=OUTLINE, mirror="z", margin=1.3, cosmetic_margin=0.9)
fol = pl.FoliageBuilder(site, seed=12)
S = site

# ------------------------------------------------------------------------------------------------ palette
ASPHALT = "#3c3d40"; PAVE = "#a9a69e"; DRIVE = "#8f8b83"; KERB = "#c2bfb6"
TRIM = "#ecebe4"; DOOR_RED = "#8a2b26"; DOOR_BLUE = "#2f4a6a"; DOOR_GREEN = "#2f5a3a"; GLASS = "#9db4c4"
SIDING = ["#d8cfb8", "#b9c7cf", "#c4c9ad", "#e2d6a6", "#cfc0b0", "#d9d4cc"]
ROOFS = ["#4a4c52", "#5e4436", "#3f4a55", "#6a5444"]
WOOD = "#7a5a3c"; GRASS_COL = "#5d8a3c"; WATER = "#3f7f9a"; HEDGE = "#2f5a26"


def ring_pt(r, ang):
    """A point at radius r from the circle's centre, angle ang (degrees, 0 = +X east, 90 = +Z south)."""
    return C[0] + r * math.cos(math.radians(ang)), C[1] + r * math.sin(math.radians(ang))


# ------------------------------------------------------------------------------------------------ objectives
BLUE_SP = [(-18.8, -30.6), (-6.0, -30.4), (-41.5, -12.8), (3.0, -24.6), (-32.0, -22.4)]
RED_SP = [(x, -z) for x, z in BLUE_SP]
FFA_SP = [(-46.5, 0.0), (34.8, 0.0)]
HILLS = [(C[0], 0.0, 3.5), (25.0, 0.0, 3.5)]
FLAGS = [(-11.5, -32.2), (-11.5, 32.2)]
for x, z in BLUE_SP:
    S.keepout(x, z, 2.4)
for x, z in FFA_SP:
    S.keepout(x, z, 2.4, mirror=False)
for x, z, r in HILLS:
    S.keepout(x, z, r + 0.6, mirror=False)
S.keepout(*FLAGS[0], 2.6)

# ------------------------------------------------------------------------------------------------ lanes (bot routes)
LANES = [
    (pl.arc(C[0], C[1], 8.0, 0, 360, step=15), 2.6),                       # round the island on the road
    ([(-6.0, 0.0), (38.0, 0.0)], 3.0),                                    # the street
    ([(C[0] + 8, -0.5), (-6.0, 0.0)], 2.4),
    ([(-17.0, -18.2), (-17.0, -13.4)], 1.6),                              # N house front path
    ([(-6.25, -19.0), (-6.25, -11.0)], 2.2),                              # N house drive / garage door
    ([(-11.5, -28.6), (-11.5, -33.0)], 1.6),                              # N house back door
    ([(-23.0, -12.0), (-23.0, -24.0), (-21.5, -30.5)], 2.2),              # N/NW side yard to the back
    ([(-21.5, -30.5), (-11.5, -32.4), (-1.0, -31.0), (5.0, -26.5)], 1.8),  # N backyards, along the back fence
    ([(5.0, -26.5), (9.3, -23.6), (9.6, -19.0), (9.6, -11.0), (9.6, -6.6)], 1.8),  # behind NE, through the pocket park
    ([(-1.8, -12.5), (-1.8, -21.0), (-1.0, -26.0)], 1.8),                 # garage / NE gap to the back
    ([(-21.5, -30.5), (-30.5, -25.2), (-37.8, -21.8), (-38.0, -13.5), (-43.5, -9.6), (-45.8, -5.0), (-46.5, -1.0)], 1.8),  # NW yard to the duplex back
    ([(-30.5, -11.0), (-37.0, -10.0), (-43.0, -10.5)], 1.8),              # NW / duplex gap
    ([(-31.5, -4.4), (-28.0, -3.0)], 1.6),                                # duplex front door
    ([(-41.8, -2.4), (-44.5, -2.4)], 1.6),                                # duplex back door
    ([(12.0, -6.4), (30.0, -6.4)], 1.6),                                  # north pavement of the street
]
for pts, w in LANES:
    S.lane(pts, w)

# ------------------------------------------------------------------------------------------------ building helpers


def wall(x0, z0, x1, z1, h, color, holes=(), y0=0.0):
    """A straight wall (a thin axis-aligned box) with openings: holes = [(centre along the wall, width, bottom, top)]."""
    along_x = (x1 - x0) >= (z1 - z0)
    a0, a1 = (x0, x1) if along_x else (z0, z1)

    def B(s0, s1, b, t):
        if s1 - s0 < 0.02 or t - b < 0.02:
            return
        if along_x:
            S.block(s0, y0 + b, z0, s1, y0 + t, z1, color)
        else:
            S.block(x0, y0 + b, s0, x1, y0 + t, s1, color)
    cur = a0
    for c, w, b, t in sorted(holes):
        B(cur, c - w / 2, 0, h)
        B(c - w / 2, c + w / 2, 0, b)
        B(c - w / 2, c + w / 2, t, h)
        cur = c + w / 2
    B(cur, a1, 0, h)


def shell(rect, h, color, holes, t=0.25):
    """Four walls round rect (outer faces on the rectangle); holes per side "N", "S", "W", "E"."""
    x0, z0, x1, z1 = rect
    wall(x0, z0, x1, z0 + t, h, color, holes.get("N", ()))
    wall(x0, z1 - t, x1, z1, h, color, holes.get("S", ()))
    wall(x0, z0 + t, x0 + t, z1 - t, h, color, holes.get("W", ()))
    wall(x1 - t, z0 + t, x1, z1 - t, h, color, holes.get("E", ()))


def gable(rect, base, rise, color, ridge_x=True, over=0.35, mirror=None):
    """A pitched roof (two cosmetic wedges) over rect, its ridge along X or Z, eaves `over` metres out."""
    x0, z0, x1, z1 = rect[0] - over, rect[1] - over, rect[2] + over, rect[3] + over
    if ridge_x:
        d = (z1 - z0) / 2
        S.mesh("wedge", (x0 + x1) / 2, z0 + d / 2, color, y=base, width=x1 - x0, height=rise, depth=d, material="Tiles", mirror=mirror)
        S.mesh("wedge", (x0 + x1) / 2, z1 - d / 2, color, y=base, width=x1 - x0, height=rise, depth=d, yaw=180.0, material="Tiles", mirror=mirror)
    else:
        d = (x1 - x0) / 2
        S.mesh("wedge", x0 + d / 2, (z0 + z1) / 2, color, y=base, width=z1 - z0, height=rise, depth=d, yaw=90.0, material="Tiles", mirror=mirror)
        S.mesh("wedge", x1 - d / 2, (z0 + z1) / 2, color, y=base, width=z1 - z0, height=rise, depth=d, yaw=270.0, material="Tiles", mirror=mirror)


def roof_y(rect, base, rise, x, z, ridge_x=True, over=0.35):
    """Height of a gable() roof's surface at (x, z)."""
    if ridge_x:
        half = (rect[3] - rect[1]) / 2 + over
        d = abs(z - (rect[1] + rect[3]) / 2)
    else:
        half = (rect[2] - rect[0]) / 2 + over
        d = abs(x - (rect[0] + rect[2]) / 2)
    return base + rise * max(0.0, 1 - d / half)


def slab(rect, y0, y1, color):
    S.block(rect[0], y0, rect[1], rect[2], y1, rect[3], color)


def flat(kind_color, x0, z0, x1, z1, h=0.03, material="Concrete", y=0.0, mirror=None):
    """A flat paved surface (cosmetic mesh lying on the ground): road, pavement, drive, patio."""
    S.mesh("box", (x0 + x1) / 2, (z0 + z1) / 2, kind_color, y=y, width=abs(x1 - x0), height=h, depth=abs(z1 - z0), material=material, mirror=mirror)


def strip_mesh(color, ax, az, bx, bz, width, h=0.03, material="Tiles", y=0.0):
    """A paved strip from (ax, az) to (bx, bz) (a turned box mesh): a diagonal drive or path."""
    L = math.hypot(bx - ax, bz - az)
    S.mesh("box", (ax + bx) / 2, (az + bz) / 2, color, y=y, width=L, height=h, depth=width, yaw=round(pl.along_yaw(bx - ax, bz - az), 2), material=material)


def window_glass(x, z, along_x, w, b, t, y0=0.0):
    """A thin pane in a window hole (cosmetic, so players can still mantle through)."""
    S.mesh("box", x, z, GLASS, y=y0 + b, width=w if along_x else 0.04, height=t - b, depth=0.04 if along_x else w)


def door_panel(x, z, along_x, color, w=0.95, open_side=1):
    """A door left ajar in a doorway (cosmetic): a thin panel swung 70 degrees, hinged at one jamb."""
    if along_x:
        hx, hz = x - open_side * 0.65, z
        S.mesh("box", hx + 0.16, hz + 0.43 * open_side, color, width=0.05, height=2.1, depth=w, yaw=20.0 * open_side)
    else:
        hx, hz = x, z - open_side * 0.65
        S.mesh("box", hx + 0.43 * open_side, hz + 0.16, color, width=w, height=2.1, depth=0.05, yaw=20.0 * open_side)


# ------------------------------------------------------------------------------------------------ ground and roads
S.raw({"op": "Ground", "color": "#5a6b3e"})
# the turning circle and the street (asphalt), the island kerb, pavements round the circle and along the street
S.mesh("cylinder", C[0], C[1], ASPHALT, width=23.0, height=0.02, depth=23.0, segments=48, material="Concrete", mirror=False)
flat(ASPHALT, -6.0, -4.0, XEND + 0.5, 4.0, h=0.02, mirror=False)
S.mesh("tube", C[0], C[1], KERB, radius=4.6, inner=4.3, height=0.16, segments=40, material="Concrete", mirror=False)
# ring pavement: arc pieces from 24 to 336 degrees (the street comes in at 0)
for k in range(26):
    a = 24 + (336 - 24) * (k + 0.5) / 26
    x, z = ring_pt(12.5, a)
    if z < 0 or abs(z) < 0.01:
        S.mesh("box", x, z, PAVE, width=2.0, height=0.06, depth=3.1, yaw=round(pl.along_yaw(math.cos(math.radians(a)), math.sin(math.radians(a))), 2), material="Tiles")
for side in (-1,):
    flat(PAVE, -2.5, side * 5.9, XEND + 0.5, side * 4.0, h=0.06, material="Tiles")   # street pavement (and its twin)
    flat(KERB, -2.5, side * 4.12, XEND + 0.5, side * 4.0, h=0.12, material="Concrete")  # kerb edge
# storm drains in the gutters and manholes in the road (cosmetic, flat)
S.rule("street")
for x in (4.0, 18.0, 31.0):
    S.prop("Storm Drain", x, -3.55, 0, check=False)
for x in (9.0, 26.0):
    S.prop("Manhole Cover", x, 0.0, 0, mirror=False, check=False)
S.prop("Manhole Cover", C[0] - 7.5, -3.0, 30, check=False)

# ------------------------------------------------------------------------------------------------ the island (hill 1)
S.rule("island")
S.mesh("cylinder", C[0], C[1], "#4d6a33", y=0.0, width=8.6, height=0.12, depth=8.6, segments=40, material="Dirt", mirror=False)
for k, a in enumerate(range(0, 360, 45)):
    x, z = ring_pt(3.7, a + 22.5)
    if z < 0:
        S.prop("Flower Pots" if k % 2 else "Garden Gnome", x, z, pl.face_yaw(x - C[0], z - C[1]), check=False)
fol.tree(C[0] - 3.95, 0.0, 7.5)          # two trees at the island's ends, on the axis (each is its own twin)
fol.tree(C[0] + 3.95, 0.0, 6.5)
fol.region("shrub", 1.6, (C[0], 0.0, 4.2), mirror=False, color="#567a30", scale=[0.6, 1.0])
fol.region("grass", 7.0, (C[0], 0.0, 4.3), mirror=False, color="#6a9a40")

# ------------------------------------------------------------------------------------------------ the houses
HOUSES = {}   # name -> dict(rect, roof, ...) for the dressing passes


def walkin_house():
    """The N house (and its S twin): living room and kitchen, an attached garage with a car and a workbench."""
    B = (-20.0, -28.0, -9.0, -19.0)
    G = (-9.0, -27.5, -3.5, -19.5)
    col, roofc = SIDING[1], ROOFS[2]
    h = 2.8
    shell(B, h, col, {
        "S": [(-17.0, 1.5, 0.0, 2.2), (-14.9, 1.4, 0.95, 2.05), (-11.4, 1.4, 0.95, 2.05)],
        "N": [(-11.5, 1.5, 0.0, 2.2), (-17.4, 1.6, 0.95, 2.05)],
        "W": [(-23.5, 1.6, 0.95, 2.05)],
        "E": [(-23.0, 1.4, 0.0, 2.2)],
    })
    wall(-14.5, -27.75, -14.25, -19.25, h, TRIM, [(-21.4, 1.3, 0.0, 2.2), (-25.8, 1.2, 0.0, 2.2)])   # partition
    slab(B, h, h + 0.2, TRIM)
    gable(B, h + 0.2, 1.7, roofc, ridge_x=True)
    # garage
    gh = 2.5
    wall(G[0], G[1], G[2], G[1] + 0.25, gh, col, [(-6.2, 1.2, 1.1, 1.9)])
    wall(G[2] - 0.25, G[1] + 0.25, G[2], G[3] - 0.25, gh, col)
    wall(G[0], G[3] - 0.25, G[2], G[3], gh, col, [(-6.25, 3.7, 0.0, 2.25)])
    slab(G, gh, gh + 0.15, "#8a8a86")
    # glass, doors, trims (cosmetic)
    for x in (-14.9, -11.4):
        window_glass(x, -19.12, True, 1.4, 0.95, 2.05)
    window_glass(-17.4, -27.88, True, 1.6, 0.95, 2.05)
    window_glass(-19.88, -23.5, False, 1.6, 0.95, 2.05)
    window_glass(-6.2, -27.38, True, 1.2, 1.1, 1.9)
    door_panel(-17.0, -19.12, True, DOOR_BLUE)
    door_panel(-11.5, -27.88, True, TRIM, open_side=-1)
    S.mesh("box", -6.25, -19.62, "#d0d0cc", y=2.0, width=3.7, height=0.25, depth=0.3)            # garage door rolled up
    S.mesh("box", -17.0, -18.35, TRIM, y=2.55, width=2.4, height=0.12, depth=1.5)                # porch canopy
    for x in (-18.1, -15.9):
        S.mesh("cylinder", x, -17.75, TRIM, width=0.14, height=2.55, depth=0.14, segments=8)
    S.block(-18.2, 0.0, -19.0, -15.8, 0.16, -18.0, "#b8b2a6")                                    # porch step
    flat(DRIVE, -8.1, -19.5, -4.4, -11.0, material="Tiles")                                      # drive
    flat(PAVE, -17.6, -18.0, -16.4, -13.2, material="Tiles")                                     # front path
    flat("#9a8f80", -14.0, -31.0, -9.2, -28.0, material="Tiles")                                 # back patio
    flat("#6a6a66", -8.75, -27.25, -3.75, -19.75, h=0.02, y=0.0)                                 # garage floor
    flat("#7a6650", -19.75, -27.75, -9.25, -19.25, h=0.02, material="Wood")                      # house floor
    HOUSES["N"] = dict(rect=B, garage=G, roof=(B, h + 0.2, 1.7, True), color=col)
    # garage: car, workbench, shelving, freezer
    S.rule("garage")
    S.prop("Sedan", -5.1, -23.4, 90.0)
    S.block(-8.95, 0.0, -27.25, -6.9, 0.9, -26.6, WOOD)                                            # workbench
    S.mesh("box", -7.9, -26.92, "#5a4a3a", y=0.9, width=2.05, height=0.04, depth=0.62, material="Wood")
    for name, x, z, yaw in (("Toolbox", -8.2, -26.95, 10), ("Paint Cans", -7.4, -26.85, 0)):
        S.prop(name, x, z, yaw, y=0.94, check=False)
    S.prop("Metal Shelving", -8.45, -25.0, 90.0)
    S.prop("Running Washer", -8.4, -20.7, 90.0)
    for name, x, z, yaw in (("Car Wheel", -7.6, -24.8, 30), ("Paint Cans", -6.5, -26.0, 200), ("Bucket", -7.1, -21.3, 0),
                            ("Broom", -7.9, -22.4, 75)):
        S.prop(name, x, z, yaw)
    S.prop("Ceiling Fan", -6.25, -23.5, 0, y=1.75, check=False)
    # living room (west) and kitchen (east) inside
    S.rule("rooms")
    S.room((-19.75, -27.75, -14.5, -19.25), "living", doors=[("S", -17.0, 1.5), ("E", -21.4, 1.3), ("E", -25.8, 1.2)], seed=41)
    S.room((-14.25, -27.75, -9.25, -19.25), "kitchen", doors=[("W", -21.4, 1.3), ("W", -25.8, 1.2), ("N", -11.5, 1.5), ("E", -23.0, 1.4)], seed=42)
    S.prop("Ceiling Fan", -17.1, -23.5, 0, y=2.1, check=False)


def duplex():
    """The W duplex on the axis: two mirrored units (north one built here), a party wall with a kitchen doorway."""
    D = (-41.0, -8.0, -33.0, 8.0)
    col, roofc = SIDING[4], ROOFS[1]
    h = 2.8
    # the north unit's walls (twins make the south unit): east front, west back, north side; party wall on the axis
    wall(-33.25, -7.75, -33.0, -0.125, h, col, [(-4.6, 1.5, 0.0, 2.2), (-1.9, 1.4, 0.95, 2.05)])
    wall(-41.0, -7.75, -40.75, -0.125, h, col, [(-2.4, 1.5, 0.0, 2.2), (-5.6, 1.4, 0.95, 2.05)])
    wall(-41.0, -8.0, -33.0, -7.75, h, col, [(-39.0, 1.4, 0.95, 2.05), (-35.0, 1.4, 0.95, 2.05)])
    wall(-41.0, -0.125, -33.0, 0.125, h, TRIM, [(-39.0, 1.4, 0.0, 2.2)])                       # party wall (on the axis)
    wall(-37.1, -7.75, -36.85, -0.125, h, TRIM, [(-5.9, 1.3, 0.0, 2.2)])                       # front room / kitchen
    slab(D, h, h + 0.2, TRIM)
    gable(D, h + 0.2, 1.8, roofc, ridge_x=False)
    for z in (-1.9,):
        window_glass(-33.12, z, False, 1.4, 0.95, 2.05)
    window_glass(-40.88, -5.6, False, 1.4, 0.95, 2.05)
    window_glass(-39.0, -7.88, True, 1.4, 0.95, 2.05)
    window_glass(-35.0, -7.88, True, 1.4, 0.95, 2.05)
    door_panel(-33.12, -4.6, False, DOOR_RED)
    door_panel(-40.88, -2.4, False, TRIM, open_side=-1)
    S.mesh("box", -32.3, -4.6, TRIM, y=2.5, width=1.5, height=0.12, depth=2.2)                  # porch canopy
    S.block(-33.0, 0.0, -5.6, -32.2, 0.16, -3.6, "#b8b2a6")                                    # step
    flat(PAVE, -32.2, -5.2, -27.4, -4.0, material="Tiles")                                     # path to the pavement
    flat("#7a6650", -40.75, -7.75, -33.25, 7.75, h=0.02, material="Wood", mirror=False)        # floor
    flat("#9a8f80", -44.6, -6.5, -41.0, -0.3, material="Tiles")                                # back patio
    HOUSES["W"] = dict(rect=D, roof=(D, h + 0.2, 1.8, False), color=col)
    S.rule("rooms")
    S.room((-36.85, -7.75, -33.25, -0.125), "living", doors=[("E", -4.6, 1.5), ("W", -5.9, 1.3)], seed=51,
           items=[(["Fabric Sofa"], "wall", 1, None), (["TV Stand"], "wall", 1, None), (["Armchair"], "wall", 1, None),
                  (["Floor Lamp"], "corner", 1, None), (["Coffee Table"], "middle", 1, ["Book Pile", "Laptop"]),
                  (["Rug"], "floor", 1, None), (["Toy Blocks", "Teddy Bear"], "floor", 2, None)])
    S.room((-40.75, -7.75, -37.1, -0.125), "kitchen", doors=[("E", -5.9, 1.3), ("W", -2.4, 1.5), ("S", -39.0, 1.4)], seed=52,
           items=[(["Fridge"], "wall", 1, None), (["Cooker"], "wall", 1, ["Cooking Pot"]), (["Kitchen Sink"], "wall", 1, ["Stacked Plates"]),
                  (["Kitchen Counter"], "wall", 1, ["Microwave", "Toaster", "Kettle"]), (["Pedal Bin"], "corner", 1, None),
                  (["Dining Table"], "middle", 1, ["Stacked Plates"]), (["Dining Chair"], "chairs", 2, None)])
    S.prop("Ceiling Fan", -35.0, -4.0, 0, y=2.1, check=False)


def closed_house(name, rect, col, roofc, ridge_x, door_side, door_at):
    """A house you can't go into: one solid box with a roof, a front door, windows and a porch (cosmetic)."""
    x0, z0, x1, z1 = rect
    h = 2.9
    S.block(x0, 0.0, z0, x1, h, z1, col)
    gable(rect, h, 1.6, roofc, ridge_x=ridge_x)
    # door and windows drawn on the faces
    for side in "NSWE":
        if side in "NS":
            z = z0 - 0.03 if side == "N" else z1 + 0.03
            for k in range(2):
                x = x0 + (x1 - x0) * (0.25 + 0.5 * k)
                if side == door_side and abs(x - door_at) < 1.5:
                    continue
                S.mesh("box", x, z, GLASS, y=1.0, width=1.3, height=1.05, depth=0.04)
                S.mesh("box", x, z, TRIM, y=0.92, width=1.5, height=0.08, depth=0.08)
        else:
            x = x0 - 0.03 if side == "W" else x1 + 0.03
            for k in range(2):
                z = z0 + (z1 - z0) * (0.25 + 0.5 * k)
                if side == door_side and abs(z - door_at) < 1.5:
                    continue
                S.mesh("box", x, z, GLASS, y=1.0, width=0.04, height=1.05, depth=1.3)
                S.mesh("box", x, z, TRIM, y=0.92, width=0.08, height=0.08, depth=1.5)
    if door_side in "NS":
        z = z0 - 0.04 if door_side == "N" else z1 + 0.04
        S.mesh("box", door_at, z, DOOR_GREEN, width=1.0, height=2.1, depth=0.05)
        S.mesh("box", door_at, z + (-0.75 if door_side == "N" else 0.75), TRIM, y=2.45, width=2.0, height=0.1, depth=1.4)
    else:
        x = x0 - 0.04 if door_side == "W" else x1 + 0.04
        S.mesh("box", x, door_at, DOOR_GREEN, width=0.05, height=2.1, depth=1.0)
        S.mesh("box", x + (-0.75 if door_side == "W" else 0.75), door_at, TRIM, y=2.45, width=1.4, height=0.1, depth=2.0)
    HOUSES[name] = dict(rect=rect, roof=(rect, h, 1.6, ridge_x), color=col, door=(door_side, door_at))


walkin_house()
duplex()
closed_house("NW", (-36.3, -20.6, -27.3, -12.6), SIDING[0], ROOFS[0], True, "E", -15.0)
closed_house("NE", (-0.4, -20.4, 7.6, -12.4), SIDING[3], ROOFS[3], False, "S", 2.0)

# drives and paths for the closed houses (diagonal strips to the ring pavement)
ax, az = ring_pt(13.4, 228)
strip_mesh(DRIVE, ax, az, -26.0, -14.2, 3.0)
ax, az = ring_pt(13.4, 312)
strip_mesh(DRIVE, ax, az, 2.9, -12.2, 3.0)
strip_mesh(PAVE, -27.2, -15.0, -24.5, -15.0, 1.1)

flat(DRIVE, 1.6, -12.4, 4.6, -5.9, material="Tiles")       # NE drive straight onto the street
flat(PAVE, 1.4, -12.4, 0.4, -5.9, h=0.04, material="Tiles")


# ------------------------------------------------------------------------------------------------ yards: the big pieces
def trampoline(x, z, d=3.2):
    """A garden trampoline: a 0.85 m solid deck you can jump onto (Block), the mat, frame ring, legs and net posts."""
    S.block(x - d * 0.34, 0.0, z - d * 0.34, x + d * 0.34, 0.85, z + d * 0.34, "#26282c")
    S.mesh("cylinder", x, z, "#1c1c20", y=0.8, width=d - 0.3, height=0.06, depth=d - 0.3, segments=28)
    S.mesh("torus", x, z, "#3a6fb0", y=0.88, radius=d / 2 - 0.1, inner=0.09, segments=32)
    for k in range(6):
        a = math.radians(k * 60 + 15)
        lx, lz = x + math.cos(a) * (d / 2 - 0.15), z + math.sin(a) * (d / 2 - 0.15)
        S.mesh("cylinder", lx, lz, "#8a8f96", width=0.07, height=0.85, depth=0.07, segments=6)
        S.mesh("cylinder", lx, lz, "#3a6fb0", y=0.85, width=0.06, height=1.5, depth=0.06, segments=6)
    S.mesh("tube", x, z, "#202024", y=0.9, radius=d / 2 - 0.12, inner=d / 2 - 0.15, height=1.45, segments=32)


def above_ground_pool(x, z, d=3.8):
    """A round above-ground pool: a solid core you can mantle onto (Block), blue walls, water, a ladder."""
    S.block(x - d * 0.33, 0.0, z - d * 0.33, x + d * 0.33, 1.05, z + d * 0.33, "#5a8fb8")
    S.mesh("cylinder", x, z, "#6a9ec8", width=d, height=1.1, depth=d, segments=32, material="Metal")
    S.mesh("cylinder", x, z, WATER, y=1.0, width=d - 0.12, height=0.03, depth=d - 0.12, segments=32)
    S.mesh("torus", x, z, "#e8e8e4", y=1.1, radius=d / 2, inner=0.06, segments=32)
    S.mesh("box", x + d / 2 + 0.25, z, "#c0c4c8", width=0.5, height=1.35, depth=0.6, material="Metal")


S.rule("yards")
trampoline(-41.0, -18.6)
above_ground_pool(-24.4, -21.6)
# the duck pond in the pocket park between the NE house and the street
POND = (12.6, -15.2, 2.3)
S.mesh("cylinder", POND[0], POND[1], WATER, y=0.0, width=POND[2] * 2, height=0.04, depth=POND[2] * 1.7, segments=36, mirror=None)
S.mesh("tube", POND[0], POND[1], "#8a8478", radius=POND[2] + 0.1, inner=POND[2] - 0.15, height=0.18, segments=36, scale=1.0, material="Concrete")
S.occupy((POND[0] - POND[2], POND[1] - POND[2] * 0.85, POND[0] + POND[2], POND[1] + POND[2] * 0.85), 0.0, 0.3, kind="water")

# fences between the yards (wooden panels, solid and breakable) with gaps on the routes; a low picket fence in front
S.rule("fences")
S.along(["Fence Panel"], [(-2.6, -27.9), (-2.6, -30.4)], spacing=2.04, start=1.02, face="along", lanes=True, gap=0.0)   # N yard | NE back
S.along(["Fence Panel"], [(-41.4, 0.0), (-44.2, 0.0)], spacing=2.04, start=1.02, face="along", lanes=True, mirror=False, gap=0.0)  # duplex yards
S.along(["Fence Panel"], [(7.9, -21.0), (7.9, -25.2)], spacing=2.04, start=1.02, face="along", lanes=True, gap=0.0)    # NE back | pocket park
S.along(["Picket Fence"], [(-0.9, -12.1), (-0.9, -6.1)], spacing=2.02, start=1.01, face="along", lanes=True, gap=0.0)    # NE front lawn
S.along(["Picket Fence"], [(7.9, -12.1), (7.9, -6.1)], spacing=2.02, start=1.01, face="along", lanes=True, gap=0.0)

# ------------------------------------------------------------------------------------------------ cars
S.rule("cars")
S.prop("SUV", -5.25, -15.4, 90.0, lanes=False)                         # N drive (nose to the garage)
mx, mz = ring_pt(15.5, 228)
S.prop("Hatchback", mx, mz, pl.along_yaw(-math.cos(math.radians(228)), -math.sin(math.radians(228))), lanes=False)  # NW drive
S.prop("Pickup Truck", 3.1, -9.2, 270.0, lanes=False)                  # NE drive, nose to the street
S.prop("Van", 8.0, -2.9, 180.0, lanes=False)                           # parked at the street's kerb, facing the mouth
S.prop("Sedan", 17.6, -2.95, 180.0, lanes=False)
cx_, cz_ = ring_pt(10.4, 204)
S.prop("Hatchback", cx_, cz_, pl.along_yaw(math.sin(math.radians(204)), -math.cos(math.radians(204))), lanes=False)  # at the circle's kerb

# ------------------------------------------------------------------------------------------------ street furniture
S.rule("street")
S.along(["Street Lamp"], [(-1.0, -5.25), (XEND - 1.0, -5.25)], spacing=13.0, start=2.0, face="toward", side=-1, lanes=False)
for a in (214, 258, 292):
    x, z = ring_pt(12.75, a)
    S.prop("Flickering Street Lamp" if a == 258 else "Street Lamp", x, z, pl.along_yaw(C[0] - x, C[1] - z), lanes=False)
S.prop("Fire Hydrant", 4.0, -5.0, pl.face_yaw(0, 1))
x, z = ring_pt(12.4, 236)
S.prop("Fire Hydrant", x, z, pl.face_yaw(C[0] - x, C[1] - z))
S.prop("Street Name Sign", -0.6, -5.3, 0.0)
S.prop("Speed Limit Sign", 33.0, -5.2, 90.0)
S.prop("No Parking Sign", 13.0, -5.2, 0.0)
S.prop("Utility Pole", 27.5, -8.0, 0.0)
S.prop("Pole Transformer", 14.5, -8.0, 0.0)
S.prop("Street Cabinet", 31.5, -8.1, 0.0)
S.prop("Utility Pedestal", -21.6, -13.8, 40.0)
S.prop("Utility Pedestal", -2.9, -12.9, 0.0)
S.prop("Bench", 12.2, -10.6, 180.0)              # in the pocket park, facing the pond
S.prop("Bird Bath", 16.4, -12.4, 0.0)
S.prop("Post Box", 25.0, -5.3, 0.0)
S.prop("Newspaper Box", 26.0, -5.3, 0.0)

# mailboxes at the end of each drive / path, facing the road
for x, z in ((-9.2, -11.0), (-27.6, -6.8), (0.2, -6.5)):
    S.prop("Mailbox", x, z, pl.face_yaw(C[0] - x, C[1] - z) if x < -2 else 0.0)
mx, mz = ring_pt(14.2, 221)
S.prop("Mailbox", mx, mz, pl.face_yaw(C[0] - mx, C[1] - mz))

# trash day: each house's bins out at the kerb, bags and litter round them, a bag on top of an overflowing bin
S.rule("bins")
BIN_SPOTS = [  # (x, z, facing the road (dx, dz), along the kerb (dx, dz)) for N, NW, duplex (north unit), NE
    (*ring_pt(12.2, 292), None), (*ring_pt(12.2, 240), None), (*ring_pt(12.2, 196), None), (5.6, -5.1, (0, 1)),
]
TRASH = ["Trash Bag", "Trash Bag", "Litter", "Soda Cans", "Bottles", "Newspaper", "Plastic Bag", "Flattened Cardboard", "Open Cardboard Box", "Cardboard Box Small"]
bins_placed = []
for i, (x, z, face) in enumerate(BIN_SPOTS):
    fx, fz = face if face else (C[0] - x, C[1] - z)
    L = math.hypot(fx, fz)
    fx, fz = fx / L, fz / L
    tx, tz = -fz, fx
    for k, name in enumerate(("Wheelie Bin", "Wheelie Bin Grey", "Recycling Bin")[: 2 + (i % 2)]):
        o = (k - 0.8) * 0.95
        b = S.prop(name, x + tx * o, z + tz * o, pl.face_yaw(fx, fz), lanes=False, gap=0.03)
        if b:
            bins_placed.append(b)
            if k == 0:
                S.prop("Trash Bag", b.x, b.z, b.yaw + 30, y=1.0, check=False)
for b in bins_placed:
    S.around(b, TRASH, count=6, r1=1.8, seed=int(abs(b.x) * 10 + abs(b.z)))

# ------------------------------------------------------------------------------------------------ houses: walls and roofs
S.rule("walls")
N = HOUSES["N"]["rect"]; G = HOUSES["N"]["garage"]; Wd = HOUSES["W"]["rect"]; NW = HOUSES["NW"]["rect"]; NE = HOUSES["NE"]["rect"]
S.against(N, "W", ["Split AC Unit"], at=[0.75])
S.against(N, "N", ["AC Condenser Fan"], at=[0.45])
S.against(G, "E", ["Gas Meter", "Electric Meter"], at=[0.3, 0.55])
S.against((G[0], G[1], G[2], G[3]), "N", ["Compost Bin"], at=[0.2])
S.against(NW, "N", ["Split AC Unit", "Gas Meter"], at=[0.12, 0.7])
S.against(NW, "W", ["Electric Meter", "Hose Reel"], at=[0.25, 0.75])
S.against(NW, "E", ["Window AC Unit"], at=[0.75], y=1.3, check=False)
S.against(NE, "N", ["AC Condenser Fan", "Gas Meter"], at=[0.25, 0.7])
S.against(NE, "E", ["Electric Meter", "Split AC Unit"], at=[0.3, 0.75])
S.against(NE, "W", ["Window AC Unit"], at=[0.4], y=1.3, check=False)
S.against((Wd[0], Wd[1], Wd[2], -0.15), "N", ["Split AC Unit", "Gas Meter"], at=[0.3, 0.7])
S.against((Wd[0], Wd[1], Wd[2], -0.15), "W", ["Electric Meter"], at=[0.75])
S.against(N, "S", ["Large Flower Pot", "Flower Pots"], at=[0.03, 0.62])

S.rule("roofs")


def roof_props(key, spots):
    """Props on a gable roof: (name, x, z, yaw); y follows the slope (sunk a little so nothing floats)."""
    rect, base, rise, rx = HOUSES[key]["roof"]
    for name, x, z, yaw in spots:
        S.prop(name, x, z, yaw, y=roof_y(rect, base, rise, x, z, rx) - 0.12, check=False)


roof_props("N", [("Brick Chimney", -19.0, -24.4, 0.0), ("Satellite Dish", -12.2, -21.0, 180.0), ("Roof Turbine Vent", -15.5, -24.6, 0.0),
                 ("Mushroom Vent", -10.5, -25.2, 0.0), ("TV Antenna", -16.4, -23.5, 0.0)])
S.prop("Solar Panel", -6.8, -24.7, 180.0, y=2.65, check=False)
S.prop("Solar Panel", -4.95, -24.7, 180.0, y=2.65, check=False)
S.prop("Skylight", -6.2, -21.4, 0.0, y=2.65, check=False)
roof_props("W", [("Brick Chimney", -37.0, -1.2, 0.0), ("Satellite Dish", -35.4, -6.2, 90.0), ("Roof Turbine Vent", -38.8, -4.6, 0.0),
                 ("Mushroom Vent", -36.0, -3.0, 0.0)])
roof_props("NW", [("Satellite Dish", -29.2, -14.6, 120.0), ("Roof Turbine Vent", -33.5, -16.6, 0.0), ("Metal Flue", -31.6, -18.6, 0.0),
                  ("TV Antenna", -31.5, -16.6, 0.0)])
roof_props("NE", [("Weather Vane", 3.6, -16.4, 0.0), ("Satellite Dish", 5.8, -13.6, 160.0), ("Mushroom Vent", 1.2, -18.6, 0.0),
                  ("Roof Turbine Vent", 2.0, -14.6, 0.0)])

# ------------------------------------------------------------------------------------------------ yards: dressing
S.rule("yards")
# N backyard: patio with a barbecue and a table, a laundry line along the back, toys, a shed corner
S.prop("Gas BBQ", -13.2, -28.75, 0.0)
S.prop("Patio Chair", -9.6, -28.8, 200.0)
S.prop("Patio Chair", -8.4, -28.7, 160.0)
S.prop("Laundry Line", -15.0, -33.2, 0.0, lanes=False)
S.prop("Laundry Basket", -13.4, -32.6, 20.0)
# side yard (between the N house and the NW house): the pool, loungers, the giant duck, a shed
S.prop("Giant Rubber Duck", -25.6, -26.4, 20.0)
S.prop("Sun Lounger", -21.4, -17.4, 0.0)
S.prop("Lawn Sprinkler", -24.5, -14.0, 0.0)
# NW yard: the trampoline, swings, a dog kennel
S.prop("Dog Kennel", -28.5, -22.2, 0.0)
S.prop("Dog", -27.6, -23.4, 70.0)
# NE back: a paddling pool, a shed, raised beds
S.prop("Paddling Pool", 4.0, -27.0, 10.0)
S.prop("Raised Bed", 0.6, -22.0, 0.0)
S.prop("Raised Bed", 6.4, -22.0, 0.0)
# duplex backyard: a garden pond with ducks, patio chairs, a rocking chair on the back patio
S.prop("Swing Set", -42.0, -6.4, 90.0)
S.prop("Duck Pond", -46.6, -3.4, 90.0)
S.prop("Patio Chair", -42.0, -4.2, 90.0)
S.prop("Kettle BBQ", -42.0, -1.2, 0.0)
# front porches and lawns
S.prop("Rocking Chair", -19.3, -18.2, 180.0, lanes=False)
S.prop("Wind Chimes", -15.3, -17.5, 0.0, lanes=False)
S.prop("Flag on a Pole", -14.4, -17.7, 180.0)
S.prop("Pinwheel", -11.0, -14.6, 0.0)
S.prop("Garden Gnome", -15.6, -16.2, 160.0)
S.prop("Bicycle", -2.4, -16.4, 30.0)
S.prop("Kick Scooter", -30.4, -9.4, 120.0)
S.prop("Lawnmower", -26.2, -9.2, 45.0)
S.prop("Garden Gnome", -29.4, -11.4, 130.0)
S.prop("Flower Pots", -32.0, -2.6, 0.0)
S.prop("Bench", -26.2, -17.6, 90.0)

# ducks round the park pond and the duplex pond, a duck on the water
S.prop("Duck on a Pond", POND[0] - 0.5, POND[1] + 0.3, 40.0, check=False)
S.prop("Duck on a Pond", POND[0] + 0.8, POND[1] - 0.6, 200.0, check=False)
S.around((POND[0], POND[1]), ["Mallard Duck", "Hen Duck", "Duck Family", "Mallard Duck"], count=5, r0=POND[2] + 0.4, r1=POND[2] + 2.2, seed=7)
S.around((-46.6, -3.4), ["Mallard Duck", "Hen Duck"], count=2, r0=1.2, r1=2.2, seed=8)

# garden scatter: small garden things on the lawns and in the yards (they keep off the routes)
GARDEN = ["Flower Pot", "Flower Pots", "Garden Gnome", "Watering Can", "Garden Tools", "Hose Reel", "Sports Balls", "Toy Blocks",
          "Bucket", "Teddy Bear", "Pinwheel", "Cat", "Pigeons", "Rubber Duck"]
S.scatter(GARDEN, [(-20, -28.5), (-3, -28.5), (-3, -34), (-20, -34.5)], 14, seed=61)
S.scatter(GARDEN, [(-44, -12), (-36.6, -12), (-36.6, -26), (-44, -20)], 10, seed=62)
S.scatter(GARDEN, [(-27.2, -13), (-20.2, -13), (-20.2, -30), (-27.2, -28)], 8, seed=63)
S.scatter(GARDEN, [(0, -21), (7.6, -21), (7.6, -29), (0, -29)], 8, seed=64)
S.scatter(GARDEN, [(-48, -0.5), (-41.2, -0.5), (-41.2, -7.8), (-47, -7.8)], 6, seed=65)
S.scatter(["Flower Pot", "Garden Gnome", "Bicycle", "Kick Scooter", "Sports Balls", "Newspaper", "Watering Can"],
          [(-26, -13), (-8.5, -19.0), (-8.5, -13.8), (-20, -12.6)], 10, seed=66)

# ------------------------------------------------------------------------------------------------ road works at the mouth
S.rule("roadworks")
S.along(["Construction Fence Panel"], [(38.2, -8.5), (38.2, -0.5)], spacing=4.0, start=2.0, face="along", lanes=False, gap=0.0)
S.prop("Arrow Board", 36.0, -7.6, 270.0, lanes=False)
S.prop("Road Works Sign", 32.4, -3.0, 270.0)
S.prop("Running Generator", 35.6, -4.4, 90.0)
S.prop("Warning Beacon", 37.4, -2.6, 0.0)
S.prop("Water Barrier", 33.8, -7.5, 90.0)
S.along(["Traffic Cone"], [(31.0, -4.2), (36.8, -1.6)], spacing=1.6, face="along", lanes=False)
S.prop("Road Plate", 35.2, 0.0, 90.0, mirror=False, check=False)

flat("#6a5a44", 33.2, -1.6, 36.8, 1.6, h=0.025, material="Dirt", mirror=False)   # the dug-up trench under the road plate
S.prop("Cinder Blocks", 33.6, -2.4, 10.0)
S.prop("Loose Bricks", 36.6, 2.2 * -1, 70.0)
S.prop("Rubble", 34.0, 2.5, 30.0, mirror=False)

# ------------------------------------------------------------------------------------------------ trees and plants
TREES = [(20.5, -8.2, 7.5), (34.5, -8.3, 6.5), (15.7, -15.4, 8.0), (-20.6, -32.9, 7.0), (-26.9, -23.2, 6.0), (-46.2, -8.8, 6.5),
         (-12.0, -15.2, 6.5), (-30.0, -7.4, 6.0), (6.3, -10.4, 6.0)]
for x, z, h in TREES:
    fol.tree(x, z, h)
fol.surfaces(Tiles=0.1, Concrete=0.0, Plain=0.0, Wood=0.0, Metal=0.0)
fol.region("grass", 5.5, (-50.0, -36.0, 41.0, 36.0), mirror=False, color="#5f8f3a", scale=[0.55, 0.95], patchiness=0.15)
BACKYARDS = [[(-20.5, -28.2), (-2.6, -27.8), (-2.6, -33.6), (-12.0, -35.0), (-20.5, -34.4)],
             [(-27.3, -12.6), (-20.2, -13.0), (-20.2, -28.2), (-27.0, -32.0), (-36.3, -25.0), (-36.3, -20.6), (-27.3, -20.6)],
             [(-36.3, -12.0), (-36.3, -25.0), (-41.0, -22.0), (-46.0, -12.0), (-48.5, -8.0), (-41.0, -8.0)],
             [(-48.6, -0.2), (-41.0, -0.2), (-41.0, -8.0), (-48.2, -8.0)],
             [(-0.4, -20.4), (7.6, -20.4), (7.9, -27.6), (2.0, -30.0), (-2.6, -29.0), (-2.6, -21.0)],
             [(7.9, -10.2), (19.5, -10.2), (14.0, -21.0), (7.9, -27.6)]]
for poly in BACKYARDS:
    fol.region("grass", 3.0, poly, color="#6f9a44", scale=[0.8, 1.35], patchiness=0.55)   # longer, uncut grass out back
    fol.region("shrub", 0.35, poly, patchiness=0.6)
    fol.region("bush", 0.12, poly, patchiness=0.7, scale=[0.8, 1.4])
# a hedge row inside the boundary hedge behind the yards, and flower beds along the house fronts
fol.strip("bush", 1.6, pl.arc(C[0], C[1], RAD - 1.6, 196, 300, step=8), 1.8, scale=[1.0, 1.7], color="#355f2a")
fol.strip("shrub", 1.2, pl.arc(C[0], C[1], RAD - 1.4, 196, 300, step=8), 1.2)
fol.strip("shrub", 2.2, [(-20.0, -18.55), (-17.9, -18.55)], 0.8, color="#a35d7a", scale=[0.6, 0.9])
fol.strip("shrub", 2.2, [(-16.1, -18.55), (-9.2, -18.55)], 0.8, color="#c9a33a", scale=[0.6, 0.9])
fol.strip("shrub", 2.0, [(-26.85, -20.4), (-26.85, -16.0)], 0.8, color="#b05a5a", scale=[0.6, 0.9])
fol.strip("shrub", 2.0, [(-0.2, -11.95), (1.0, -11.95)], 0.8, color="#9a6ab0", scale=[0.6, 0.9])
fol.strip("shrub", 2.0, [(5.0, -11.95), (7.6, -11.95)], 0.8, color="#9a6ab0", scale=[0.6, 0.9])
fol.strip("shrub", 2.0, [(-32.55, -7.8), (-32.55, -5.6)], 0.8, color="#c07040", scale=[0.6, 0.9])
fol.strip("bush", 0.8, [(-21.4, -13.6), (-21.4, -18.4)], 1.0, scale=[0.8, 1.2])            # lot line between N and NW
fol.strip("bush", 0.7, [(0.0, -21.0), (7.5, -21.0)], 0.9, scale=[0.8, 1.2])
fol.region("bush", 1.3, (POND[0], POND[1], POND[2] + 1.3), color="#6b7a3a", scale=[0.7, 1.3])  # reeds round the pond
fol.region("shrub", 1.5, (POND[0], POND[1], POND[2] + 1.0), color="#7a8a40")
fol.region("grass", 6.0, (8.0, -21.0, 19.5, -10.2), color="#6a9a40", scale=[0.7, 1.2])      # the pocket park's lawn
fol.zone("exclude", "rect", mirror=False, center=[35.0, 0.0], halfX=1.8, halfZ=1.6, density=0.15)  # weeds in the trench
fol.region("grass", 5.0, (33.2, -1.6, 36.8, 1.6), mirror=False, color="#7a8a40", scale=[0.6, 1.0])

# ------------------------------------------------------------------------------------------------ spawns and waypoints
for x, z in BLUE_SP:
    S.spawn(x, z)
for x, z in FFA_SP:
    S.spawn(x, z, mirror=False)
S.raw({"op": "AutoWaypoints", "spacing": 6})
for x, z in [(-17.1, -23.5), (-11.8, -23.4), (-7.3, -21.5), (-7.3, -25.6), (-17.0, -17.4), (-11.5, -29.4), (-7.3, -18.6),  # N house in and out
             (-35.0, -4.0), (-39.0, -4.0), (-31.6, -4.6), (-42.4, -2.4), (-39.0, -0.9),                                   # duplex
             (-23.0, -18.0), (-23.0, -26.0), (-38.0, -16.0), (-44.0, -9.5), (-45.6, -4.0), (9.6, -17.0), (9.4, -22.5), (4.0, -27.0),
             (-1.8, -17.0), (-1.6, -24.0), (-33.5, -10.4)]:
    S.waypoint(x, z)

# ------------------------------------------------------------------------------------------------ cosmetic clutter (the game's Clutter op)
WIND = ["Litter", "Litter", "Newspaper", "Soda Cans", "Bottle", "Plastic Bag", "Flattened Cardboard", "Newspaper"]
S.raw({"op": "Clutter", "props": WIND, "x0": -48, "z0": -34, "x1": 39, "z1": 34, "count": 700, "seed": 31})
S.raw({"op": "Clutter", "props": ["Litter", "Newspaper", "Soda Cans", "Plastic Bag", "Bottle"], "x0": -1, "z0": -3.6, "x1": 37, "z1": -3.6, "width": 1.0, "count": 45, "seed": 32})
S.raw({"op": "Clutter", "props": ["Litter", "Newspaper", "Soda Cans", "Plastic Bag", "Bottle"], "x0": -1, "z0": 3.6, "x1": 37, "z1": 3.6, "width": 1.0, "count": 45, "seed": 33})
S.raw({"op": "Clutter", "props": WIND + ["Trash Bag", "Cardboard Box Small"], "x0": C[0] - 12, "z0": -12, "x1": C[0] + 12, "z1": 12, "count": 160, "seed": 34})
S.raw({"op": "Clutter", "props": ["Sports Balls", "Toy Blocks", "Garden Tools", "Watering Can", "Flower Pot", "Bucket", "Rubber Duck", "Pigeons"],
       "x0": -48, "z0": -34, "x1": 18, "z1": 34, "count": 140, "seed": 35})
S.raw({"op": "Clutter", "props": ["Cinder Blocks", "Loose Bricks", "Rubble", "Plank", "Cardboard Box Small"], "x0": 30, "z0": -9, "x1": 38, "z1": 9, "count": 30, "seed": 36})

# ------------------------------------------------------------------------------------------------ scenery outside the hedge (cosmetic)
rnd = random.Random(77)
for k, a in enumerate(range(28, 334, 22)):
    if 150 < a < 210:
        pass
    r = RAD + 9 + rnd.uniform(0, 4)
    x, z = ring_pt(r, a)
    w, d = rnd.uniform(8, 11), rnd.uniform(8, 10)
    col = SIDING[k % len(SIDING)]
    S.mesh("box", x, z, col, width=w, height=3.0, depth=d, mirror=False)
    ridge_x = rnd.random() < 0.5
    rect = (x - w / 2, z - d / 2, x + w / 2, z + d / 2)
    gable(rect, 3.0, 1.6, ROOFS[k % len(ROOFS)], ridge_x=ridge_x, mirror=False)
    tx, tz = ring_pt(RAD + 4.5, a + 9)
    S.mesh("cylinder", tx, tz, "#5a4632", width=0.45, height=3.2, depth=0.45, segments=8, mirror=False)
    S.mesh("icosphere", tx, tz, "#3d6a2e", y=2.6, width=4.6, height=4.2, depth=4.6, detail=1, mirror=False)
# the street carries on past the road works and curves away north-east
px, pz, hd = XEND, 0.0, 0.0
for k in range(7):
    hd += 9.0
    L = 8.0
    nx, nz = px + L * math.cos(math.radians(-hd)), pz + L * math.sin(math.radians(-hd))
    S.mesh("box", (px + nx) / 2, (pz + nz) / 2, ASPHALT, width=L + 0.4, height=0.02, depth=8.0, yaw=round(hd, 2), material="Concrete", mirror=False)
    for sd in (-1, 1):
        ox, oz = math.sin(math.radians(-hd)) * 5.0 * sd, -math.cos(math.radians(-hd)) * 5.0 * sd
        S.mesh("box", (px + nx) / 2 + ox, (pz + nz) / 2 + oz, PAVE, width=L + 0.4, height=0.06, depth=2.0, yaw=round(hd, 2), material="Tiles", mirror=False)
        if k % 2 == 0:
            S.mesh("cylinder", nx + ox * 1.1, nz + oz * 1.1, "#4a4a4e", width=0.16, height=4.4, depth=0.16, segments=8, mirror=False)
    px, pz = nx, nz

# ------------------------------------------------------------------------------------------------ preview / output
SP_ALL = [((x, z), (40, 90, 230)) for x, z in BLUE_SP] + [((x, z), (230, 50, 50)) for x, z in RED_SP] + [((x, z), (240, 240, 240)) for x, z in FFA_SP]


def finish():
    mp = {
        "format": "call-of-slop-map/1",
        "name": "Cul-de-sac",
        "description": "Trash day at the end of a quiet street: a turning circle ringed by houses, lawns, drives, bins and backyards, inside a keyhole of hedge.",
        "author": "Call of Slop",
        "order": 404,
        "maxPlayers": 10,
        "sky": "#a9c3d8",
        "skybox": {"preset": "clear", "sun": {"time": 9.6, "latitude": 42}, "clouds": {"coverage": 0.38, "speed": 6}},
        "boundary": {
            "shape": "polygon",
            "points": [list(p) for p in OUTLINE],
            "noise": NOISE,
            "edge": {"style": "hedge", "height": 2.7, "thickness": 1.5, "seed": 4},
            "groundMargin": 8,
        },
        "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
        "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
        "flags": [list(FLAGS[0]), list(FLAGS[1])],
        "ops": S.ops,
        "foliage": fol.data,
    }
    return mp


def dump(o, ind=0):
    pad = "  " * ind
    if isinstance(o, dict) and ind <= 1 and any(isinstance(v, (dict, list)) for v in o.values()):
        return "{\n" + ",\n".join(f'{pad}  "{k}": {dump(v, ind + 1)}' for k, v in o.items()) + "\n" + pad + "}"
    if isinstance(o, list) and ind <= 2 and o and isinstance(o[0], dict):
        return "[\n" + ",\n".join(pad + "  " + json.dumps(v) for v in o) + "\n" + pad + "]"
    return json.dumps(o)


if __name__ == "__main__":
    mp = finish()
    with open(OUT_JSON, "w") as f:
        f.write(dump(mp) + "\n")
    st = S.stats()
    print(f"area {pl.poly_area(OUTLINE):.0f} m2, blocks {st['block']}, meshes {st['mesh']}, props {st['prop']} "
          f"(solid {st['solid']}, cosmetic {st['cosmetic']}, breakable {st['breakable']}), skipped {st['skipped']}")
    print("by rule:", st["rules"])
    for r, n, x, z, w in S.skips:
        if r in ("street", "cars", "yards", "walls", "roadworks", "fences", "garage", "island") and n not in GARDEN and n not in TRASH:
            print("  skipped", r, n, x, z, w)
    tall = S.solid_rects()
    vis = [(a, b) for a in BLUE_SP for b in RED_SP if not S.sightline(a, b, tall)]
    print("blue-red spawn lines of sight:", vis)
    if OUT_PNG:
        S.preview(OUT_PNG, (-52, -38, 44, 38), scale=10, spawns=SP_ALL, hills=HILLS, flags=FLAGS)
        S.preview(OUT_PNG.replace(".png", "_n.png"), (-50, -37, 42, 2), scale=16, spawns=SP_ALL, hills=HILLS, flags=FLAGS)
