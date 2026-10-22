#!/usr/bin/env python3
"""Generator for Maps/Cluttered/06-OldTown.json (large, 16 players): an overgrown small-town centre.
Usage: python3 tools/cluttered/oldtown.py <repo root> [preview.png]

Built with townkit.py (the pack's shared placement rules). The street skeleton is point-symmetric about the centre (the
west Main Street is offset north, the east one south, so no straight line runs down the town; the shop rows, back lanes,
side roads, the town hall and the church are each other's 180-degree twins), the dressing of each quarter differs:
park and pond (NW), gas station and rail crossing (NE), car park (SE), houses, gardens and a vacant lot (SW). Hills sit
in the square, in the town hall and in the church; the generator checks the walk from both teams' spawns to them on a
0.5 m grid and that no Blue spawn sees a Red one.
"""
import json, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import townkit as tk
from townkit import yaw_facing, yaw_along

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/06-OldTown.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None

CTRL = [(-93, -6), (-87, -33), (-73, -53), (-46, -63), (-20, -59), (4, -64), (33, -61), (60, -57), (83, -41), (94, -13), (92, 15),
        (82, 38), (60, 56), (31, 60), (4, 56), (-21, 59), (-50, 58), (-76, 43), (-90, 21)]
NOISE = {"amplitude": 1.6, "scale": 18, "seed": 6, "octaves": 2}
t = tk.Town(REPO, CTRL, NOISE, pack_props=[os.path.join(REPO, "Maps/Cluttered/Props")])
rng = random.Random(406)

# ---------------------------------------------------------------- palette
ASPHALT = (0.25, 0.25, 0.27); ASPHALT_B = (0.30, 0.29, 0.29); KERB = (0.60, 0.59, 0.56); PAVING = (0.55, 0.52, 0.48)
SQUARE = (0.62, 0.57, 0.50); LANE = (0.34, 0.33, 0.32); GRAVEL = (0.50, 0.46, 0.40)
BRICK_R = (0.58, 0.32, 0.25); BRICK_Y = (0.72, 0.62, 0.45); PLASTER = (0.82, 0.78, 0.68); PLASTER_G = (0.62, 0.70, 0.62)
PLASTER_B = (0.56, 0.64, 0.74); PINK = (0.80, 0.60, 0.58); STONE = (0.66, 0.64, 0.60); ROOF = (0.30, 0.30, 0.32)
TILE_R = (0.55, 0.26, 0.20); WOOD = (0.45, 0.32, 0.20); DARK = (0.12, 0.13, 0.15); WHITE = (0.86, 0.86, 0.82)
LEAF = (0.24, 0.36, 0.16); IVY = (0.22, 0.34, 0.14); WATER = (0.24, 0.38, 0.40); YELLOW = (0.85, 0.72, 0.20)
AWNINGS = [(0.70, 0.18, 0.16), (0.16, 0.40, 0.30), (0.20, 0.32, 0.58), (0.86, 0.62, 0.16), (0.55, 0.20, 0.45), (0.85, 0.85, 0.80)]

WALL = 0.3
STOREY = 4.2


# ---------------------------------------------------------------- objectives (decided first: everything keeps clear)
BLUE_SP = [(-89, -16), (-85, -19), (-81, -16), (-86, -23), (-87, 6), (-84, 10), (-88, 13), (-83.5, 4.5), (-88, -6)]
RED_SP = [(-x, -z) for x, z in BLUE_SP]
FFA_SP = [(-60, -35.5), (60, 46.6), (-22, 34), (22, -34), (-36, 30), (36, -30)]
HILLS = [(0.0, 0.0, 5.5), (0.0, -31.5, 4.5), (0.0, 31.5, 4.5)]
FLAGS = [(-90.0, 0.5), (90.0, -0.5)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.keepout.append((x, z, 2.6))
for x, z, r in HILLS:
    t.keepout.append((x, z, r + 0.6))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))


# ---------------------------------------------------------------- flat paving (meshes; Concrete = intact, Tiles / plain = busted)
def slab(x0, z0, x1, z1, c, material="Concrete", h=0.04, y=0.0):
    t.mesh("box", (x0 + x1) / 2, (z0 + z1) / 2, c, y=y, width=round(abs(x1 - x0), 2), height=h, depth=round(abs(z1 - z0), 2), **({"material": material} if material else {}))


def road_x(x0, x1, z0, z1, busted=False):
    """A road along X: asphalt, white centre dashes, kerbs."""
    slab(x0, z0, x1, z1, ASPHALT_B if busted else ASPHALT, None if busted else "Concrete", h=0.03)
    zc = (z0 + z1) / 2
    x = x0 + 1.5
    while x < x1 - 2:
        t.mesh("box", x + 1.0, zc, WHITE, y=0.0, width=2.0, height=0.04, depth=0.14)
        x += 4.5


def road_z(z0, z1, x0, x1, busted=False):
    slab(x0, z0, x1, z1, ASPHALT_B if busted else ASPHALT, None if busted else "Concrete", h=0.03)
    xc = (x0 + x1) / 2
    z = z0 + 1.5
    while z < z1 - 2:
        t.mesh("box", xc, z + 1.0, WHITE, y=0.0, width=0.14, height=0.04, depth=2.0)
        z += 4.5


def sidewalk(x0, z0, x1, z1):
    slab(x0, z0, x1, z1, PAVING, "Concrete", h=0.1)


def zebra(cx, cz, along_x, length=7.6):
    """A zebra crossing across a road (stripes along the walking direction)."""
    n = int(length / 0.9)
    for k in range(n):
        o = -length / 2 + 0.45 + k * 0.9
        if along_x:   # crossing runs along X across a road that runs along Z
            t.mesh("box", cx, cz + o, WHITE, y=0.0, width=3.0, height=0.045, depth=0.45)
        else:
            t.mesh("box", cx + o, cz, WHITE, y=0.0, width=0.45, height=0.045, depth=3.0)


# Main Street west (z -9..-1) and east (z 1..9), sidewalks 3 m each side
road_x(-100, -16, -9, -1); sidewalk(-100, -12, -16, -9); sidewalk(-100, -1, -44, 2); sidewalk(-30, -1, -16, 2)
road_x(16, 100, 1, 9); sidewalk(16, 9, 100, 12); sidewalk(44, -2, 100, 1); sidewalk(16, -2, 30, 1)
# Mill Road (SW, x -41..-33 going south) and Station Road (NE, x 33..41 going north), busted asphalt
road_z(-1, 70, -41, -33, busted=True); sidewalk(-44, 2, -41, 70); sidewalk(-33, 2, -30, 70)
road_z(-70, 1, 33, 41, busted=True); sidewalk(41, -70, 44, -2); sidewalk(30, -70, 33, -2)
# back lanes behind the shop rows (busted asphalt, plain = 10% weeds)
slab(-86, -30, -16, -24, LANE, None); slab(16, 24, 86, 30, LANE, None)
slab(-82, 14, -44, 20, LANE, None); slab(-30, 14, -12, 20, LANE, None)
slab(44, -20, 82, -14, LANE, None); slab(12, -20, 30, -14, LANE, None)
# the town square (tiled, busted), with a ring of intact paving round the monument
slab(-16, -24, 16, 24, SQUARE, "Tiles", h=0.06)
t.mesh("cylinder", 0, 0, PAVING, y=0.0, width=14.0, height=0.08, depth=14.0, segments=32, material="Concrete")
for zc in (-17.0, 17.0):
    t.mesh("cylinder", 0, zc, PAVING, y=0.0, width=7.0, height=0.08, depth=7.0, segments=24, material="Concrete")
zebra(-37, -5, False); zebra(37, 5, False)            # across Mill Road / Station Road mouths
zebra(-22.5, -5, True); zebra(22.5, 5, True)          # across Main Street at the square
zebra(-50, -5, True); zebra(50, 5, True)


# ---------------------------------------------------------------- building shells
def wall_x(x0, x1, z0, z1, h, c, holes=()):
    """A wall along X with openings (centre, width, bottom, top): solid piers, a sill under windows, a lintel over all."""
    pos = x0
    for (cx, w, bot, top) in sorted(holes):
        a, b = cx - w / 2, cx + w / 2
        if a - pos > 0.05:
            t.block(pos, 0, z0, a, h, z1, c)
        if bot > 0.05:
            t.block(a, 0, z0, b, bot, z1, c)
        if top < h - 0.05:
            t.block(a, top, z0, b, h, z1, c)
        pos = b
    if x1 - pos > 0.05:
        t.block(pos, 0, z0, x1, h, z1, c)


def wall_z(z0, z1, x0, x1, h, c, holes=()):
    pos = z0
    for (cz, w, bot, top) in sorted(holes):
        a, b = cz - w / 2, cz + w / 2
        if a - pos > 0.05:
            t.block(x0, 0, pos, x1, h, a, c)
        if bot > 0.05:
            t.block(x0, 0, a, x1, bot, b, c)
        if top < h - 0.05:
            t.block(x0, top, a, x1, h, b, c)
        pos = b
    if z1 - pos > 0.05:
        t.block(x0, 0, pos, x1, h, z1, c)


DOOR = lambda c, w=1.6: (c, w, 0.0, 2.4)
WIN = lambda c, w=2.6: (c, w, 1.0, 2.5)


def shell(x0, z0, x1, z1, front, c, holes, h=STOREY, roof=ROOF):
    """A walk-in single-storey building: four walls with openings ({'N','S','E','W': [holes]}, centres in world
    coordinates along the wall), a roof slab, a floor. Returns the Room (furnishing frame) for it."""
    w = WALL
    # keep every doorway clear (1.1 m each side of the wall) of anything placed later
    for side, hs in holes.items():
        for (cc, ww, bot, top) in hs:
            if bot > 0.05:
                continue
            if side in "NS":
                zz = z0 if side == "N" else z1
                t.mark(t.occ, (cc - ww / 2, zz - 1.1, cc + ww / 2, zz + 1.1), 3)
            else:
                xx = x0 if side == "W" else x1
                t.mark(t.occ, (xx - 1.1, cc - ww / 2, xx + 1.1, cc + ww / 2), 3)
    wall_x(x0, x1, z0, z0 + w, h, c, holes.get("N", ()))
    wall_x(x0, x1, z1 - w, z1, h, c, holes.get("S", ()))
    wall_z(z0 + w, z1 - w, x0, x0 + w, h, c, holes.get("W", ()))
    wall_z(z0 + w, z1 - w, x1 - w, x1, h, c, holes.get("E", ()))
    t.block(x0 - 0.15, h, z0 - 0.15, x1 + 0.15, h + 0.3, z1 + 0.15, roof)
    slab(x0 + w, z0 + w, x1 - w, z1 - w, (0.48, 0.45, 0.42), "Tiles", h=0.05)
    # parapet trim (cosmetic) so the roofline reads
    for (cx, cz, ww, dd) in ((x0 + x1) / 2, z0 - 0.1, x1 - x0 + 0.5, 0.25), ((x0 + x1) / 2, z1 + 0.1, x1 - x0 + 0.5, 0.25), (x0 - 0.1, (z0 + z1) / 2, 0.25, z1 - z0 + 0.5), (x1 + 0.1, (z0 + z1) / 2, 0.25, z1 - z0 + 0.5):
        t.mesh("box", cx, cz, c, y=h + 0.3, width=round(ww, 2), height=0.45, depth=round(dd, 2), material="Brick")
    return tk.Room(t, x0, z0, x1, z1, front)


def solid_building(x0, z0, x1, z1, h, c, roof=ROOF, windows=None, brick=True, sign=None):
    """A building you can't enter: one solid block, windows and doors as dark cosmetic panels on its street faces."""
    t.block(x0, 0, z0, x1, h, z1, c)
    t.mesh("box", (x0 + x1) / 2, (z0 + z1) / 2, roof, y=h, width=round(x1 - x0 + 0.3, 2), height=0.35, depth=round(z1 - z0 + 0.3, 2), material="Concrete")
    for side in (windows or ()):
        floors = [1.3 + k * 3.2 for k in range(max(1, int((h - 0.8) // 3.2)))]
        if side in "NS":
            z = z0 - 0.04 if side == "N" else z1 + 0.04
            n = int((x1 - x0) // 3.0)
            for k in range(n):
                xx = x0 + (x1 - x0) * (k + 0.5) / n
                for fy in floors:
                    t.mesh("box", xx, z, DARK, y=fy, width=1.3, height=1.5, depth=0.06)
        else:
            x = x0 - 0.04 if side == "W" else x1 + 0.04
            n = int((z1 - z0) // 3.0)
            for k in range(n):
                zz = z0 + (z1 - z0) * (k + 0.5) / n
                for fy in floors:
                    t.mesh("box", x, zz, DARK, y=fy, width=0.06, height=1.5, depth=1.3)


def awning(x, z, front, width, c):
    """A striped shop awning over a door/window (cosmetic wedge leaning out from the facade)."""
    fx, fz = front
    t.mesh("wedge", x + fx * 0.75, z + fz * 0.75, c, y=2.75, width=width, height=0.55, depth=1.5, yaw=yaw_facing(-fx, -fz))


def facade_sign(x, z, front, text_color=YELLOW, board=DARK, width=3.0, y=3.05):
    """The shop's name board over the front (a cosmetic Shop Sign prop on the wall)."""
    fx, fz = front
    t.prop("Shop Sign", x + fx * 0.02, z + fz * 0.02, yaw_facing(fx, fz), y=y, check=False, tag="sign")


def ivy(x, z, front, w, h, y0=0.0):
    """A patch of ivy on a wall (cosmetic leafy slab just proud of the wall)."""
    fx, fz = front
    yaw = yaw_facing(fx, fz)
    t.mesh("box", x + fx * 0.05, z + fz * 0.05, IVY, y=y0, width=w, height=h, depth=0.1, yaw=yaw, material="Dirt")


# ================================================================ north-west row (z -24..-12, fronts face +z onto West Main)
ROOMS = []   # (room, kind, door_u, back_door_u, rect) for furnishing and indoor clutter


def add_room(room, kind, door_xz, back_xz=None):
    du = (door_xz[0] - room.o[0]) * room.u[0] + (door_xz[1] - room.o[1]) * room.u[1]
    bu = None if back_xz is None else (back_xz[0] - room.o[0]) * room.u[0] + (back_xz[1] - room.o[1]) * room.u[1]
    ROOMS.append((room, kind, du, bu))


# Diner (x -76..-62)
r = shell(-76, -24, -62, -12, (0, 1), PLASTER_B, {"S": [DOOR(-66.5, 1.8), WIN(-72.5, 4.2), WIN(-63.8, 1.6)], "N": [DOOR(-73.0)], "W": [WIN(-18.0, 2.2)]})
add_room(r, "diner", (-66.5, -12), (-73.0, -24))
awning(-69.5, -12, (0, 1), 9.0, AWNINGS[0]); facade_sign(-69.0, -12, (0, 1))
t.prop("Neon Open Sign", -69.0, -18.0, 0, y=STOREY + 0.3, check=False, tag="sign")
t.prop("Rotating Diner Sign", -63.0, -10.9, 0, tag="sign")
# Barber (x -62..-54)
r = shell(-62, -24, -54, -12, (0, 1), PINK, {"S": [DOOR(-56.3), WIN(-59.6, 3.0)], "N": [DOOR(-58.0)]})
add_room(r, "barber", (-56.3, -12), (-58.0, -24))
awning(-58.0, -12, (0, 1), 7.0, AWNINGS[5]); facade_sign(-58.0, -12, (0, 1))
t.prop("Barber Pole", -54.9, -11.6, 0, tag="sign")
# Hardware (solid, x -54..-46) and the cut-through passage x -46..-42
solid_building(-54, -24, -46, -12, 5.6, BRICK_Y, windows="S")
awning(-50.0, -12, (0, 1), 6.5, AWNINGS[1]); facade_sign(-50.0, -12, (0, 1))
# Arcade (x -42..-30)
r = shell(-42, -24, -30, -12, (0, 1), (0.30, 0.22, 0.38), {"S": [DOOR(-34.0, 2.0), WIN(-39.0, 3.6)], "N": [DOOR(-40.0)], "W": [DOOR(-17.0)]})
add_room(r, "arcade", (-34.0, -12), (-40.0, -24))
facade_sign(-36.0, -12, (0, 1)); t.prop("Neon Open Sign", -36.0, -16.5, 0, y=STOREY + 0.3, check=False, tag="sign")
for xx in (-40.5, -31.5):
    t.prop("Neon Sign", xx, -11.95, 0, y=2.7, check=False, tag="sign")
# Bank (solid, x -30..-16), two storeys
solid_building(-30, -24, -16, -12, 7.6, STONE, windows="SE")
t.prop("ATM", -27.0, -11.55, 0, tag="street")

# ================================================================ south-east row (z 12..24, fronts face -z onto East Main): the NW row's twin
# Laundromat (x 62..76)
r = shell(62, 12, 76, 24, (0, -1), PLASTER_G, {"N": [DOOR(66.5, 1.8), WIN(72.5, 4.2), WIN(63.8, 1.6)], "S": [DOOR(73.0)], "E": [WIN(18.0, 2.2)]})
add_room(r, "laundromat", (66.5, 12), (73.0, 24))
awning(69.5, 12, (0, -1), 9.0, AWNINGS[2]); facade_sign(69.0, 12, (0, -1))
# Office: estate agent (x 54..62)
r = shell(54, 12, 62, 24, (0, -1), BRICK_R, {"N": [DOOR(56.3), WIN(59.6, 3.0)], "S": [DOOR(58.0)]})
add_room(r, "office", (56.3, 12), (58.0, 24))
facade_sign(58.0, 12, (0, -1)); t.prop("Swinging Shop Sign", 54.8, 11.2, 270, tag="sign")
# Pharmacy (solid) + passage
solid_building(46, 12, 54, 24, 5.6, PLASTER, windows="N")
awning(50.0, 12, (0, -1), 6.5, AWNINGS[1]); facade_sign(50.0, 12, (0, -1))
# Corner store (x 30..42)
r = shell(30, 12, 42, 24, (0, -1), (0.70, 0.55, 0.38), {"N": [DOOR(34.0, 2.0), WIN(39.0, 3.6)], "S": [DOOR(40.0)], "E": [DOOR(17.0)]})
add_room(r, "store", (34.0, 12), (40.0, 24))
awning(36.0, 12, (0, -1), 11.0, AWNINGS[3]); facade_sign(36.0, 12, (0, -1))
t.prop("Neon Sign", 31.5, 11.95, 180, y=2.7, check=False, tag="sign")
# Hotel (solid, x 16..30), two storeys
solid_building(16, 12, 30, 24, 7.6, BRICK_R, windows="NW")

# ================================================================ south-west row (z 2..14, fronts face -z onto West Main)
solid_building(-80, 2, -62, 14, 8.4, (0.62, 0.36, 0.34), windows="N")        # the old cinema
t.mesh("box", -71.0, 1.6, (0.10, 0.10, 0.12), y=3.2, width=12.0, height=1.6, depth=1.2)   # marquee
for k in range(10):
    t.mesh("sphere", -76.4 + k * 1.0, 1.0, (1.0, 0.85, 0.45), y=3.15, width=0.18, height=0.18, depth=0.18, segments=8, detail=4)
t.prop("Neon Sign", -71.0, 2.0, 180, y=5.6, check=False, tag="sign")
# House: ground-floor flat with kitchen and living room (x -62..-44), a partition with a door
r = shell(-62, 2, -44, 14, (0, -1), PLASTER, {"N": [DOOR(-48.0), WIN(-54.0, 2.4), WIN(-59.0, 2.0)], "S": [DOOR(-56.0)], "E": [WIN(8.0, 2.0)]})
wall_z(2.3, 13.7, -53.15, -52.85, STOREY, PLASTER, [DOOR(9.0, 1.4)])
add_room(tk.Room(t, -62, 2, -52.85, 14, (0, -1)), "kitchen", (-59.0, 2), (-56.0, 14))
add_room(tk.Room(t, -53.15, 2, -44, 14, (0, -1)), "living", (-48.0, 2))
awning(-48.0, 2, (0, -1), 3.0, AWNINGS[4])
# Estate agent / office (x -30..-16)
r = shell(-30, 2, -16, 14, (0, -1), PLASTER_B, {"N": [DOOR(-20.5, 1.8), WIN(-26.5, 4.0)], "S": [DOOR(-27.0)], "E": [DOOR(9.0)]})
add_room(r, "office", (-20.5, 2), (-27.0, 14))
facade_sign(-23.0, 2, (0, -1)); awning(-26.5, 2, (0, -1), 5.0, AWNINGS[2])

# ================================================================ north-east row (z -14..-2, fronts face +z onto East Main)
# Apartment flat (x 16..30): living + kitchen (the SW house's twin)
r = shell(16, -14, 30, -2, (0, 1), PINK, {"S": [DOOR(20.5, 1.8), WIN(26.5, 3.2)], "N": [DOOR(27.0)], "W": [DOOR(-9.0)]})
wall_x(16.3, 29.7, -8.15, -7.85, STOREY, PINK, [DOOR(20.5, 1.4), DOOR(27.0, 1.4)])
add_room(tk.Room(t, 16, -8.0, 30, -2, (0, 1)), "living", (20.5, -2))
add_room(tk.Room(t, 16, -14, 30, -8.0, (0, 1)), "kitchen", (20.5, -8), (27.0, -14))
awning(20.5, -2, (0, 1), 3.0, AWNINGS[0])
# Gas station (x 44..80, z -36..-2): forecourt, canopy on four pillars, pump islands, kiosk shop at the back
slab(44, -36, 82, -2, (0.50, 0.49, 0.47), "Tiles", h=0.05)
for (px, pz) in ((51.5, -22.5), (68.5, -22.5), (51.5, -11.5), (68.5, -11.5)):
    t.block(px - 0.3, 0, pz - 0.3, px + 0.3, 4.8, pz + 0.3, WHITE)
t.mesh("box", 60, -17, WHITE, y=4.8, width=21.0, height=0.6, depth=15.0)
t.mesh("box", 60, -17, (0.80, 0.15, 0.12), y=5.0, width=21.2, height=0.25, depth=15.2)
for iz in (-19.5, -14.5):
    t.mesh("box", 60, iz, KERB, y=0.0, width=12.0, height=0.18, depth=1.3, material="Concrete")
    for px in (55.5, 60.0, 64.5):
        t.prop("Old Town Fuel Pump", px, iz, 0, tag="gas")
r = shell(54, -36, 74, -27, (0, 1), WHITE, {"S": [DOOR(60.0, 2.0), WIN(66.5, 5.0), WIN(56.4, 1.8)], "N": [DOOR(71.0)], "E": [DOOR(-31.5)]}, roof=(0.80, 0.15, 0.12))
add_room(r, "store", (60.0, -27), (71.0, -36))
facade_sign(64.0, -27, (0, 1))
t.prop("Pylon Sign", 78.8, -3.6, 0, tag="gas")
t.prop("Glowing Vending Machine", 74.9, -26.4, 0, tag="gas"); t.prop("Vending Machine", 52.8, -26.5, 0, tag="gas")
t.prop("Chest Freezer", 51.0, -26.4, 0, tag="gas")
t.prop("Gas Valve", 77.5, -30.0, 270, tag="gas"); t.prop("Utility Pedestal", 77.5, -27.5, 270, tag="gas")
t.prop("Gas Cylinders", 76.4, -34.0, 0, tag="gas"); t.prop("Tyre Stack", 46.5, -34.0, 0, tag="gas"); t.prop("Tyre Stack", 47.5, -32.6, 0, tag="gas")
t.prop("Oil Drum", 46.0, -30.0, 0, tag="gas"); t.prop("Rusty Drum", 46.8, -29.2, 0, tag="gas")
for xx in (55.6, 64.4):
    t.prop(rng.choice(["Sedan", "Hatchback", "Pickup Truck"]), xx + 0.0, -17.0, 0, tag="gas")
t.prop("Air Duct", 64.0, -31.5, 0, y=STOREY + 0.3, check=False, tag="roof")

# ================================================================ town hall (north of the square) and church (south): twins
TH = (-12, -40, 12, -24)
r = shell(*TH, (0, 1), STONE, {"S": [DOOR(0.0, 2.4), WIN(-7.0, 2.2), WIN(7.0, 2.2)], "N": [DOOR(-6.0), DOOR(6.0)], "W": [DOOR(-32.0, 1.8)], "E": [DOOR(-32.0, 1.8)]}, h=6.0)
ROOMS.append((r, "hall", 12 - 0.3, None))
t.prop("Bell Frame", 0, -36.0, 0, y=6.3, check=False, tag="roof")
t.mesh("box", 0, -24.1, STONE, y=6.0, width=6.0, height=2.2, depth=0.3, material="Brick")            # pediment
t.mesh("cylinder", 0, -24.0, WHITE, y=6.4, width=1.4, height=1.4, depth=0.1, pitch=90.0, segments=20)  # clock face
for xx in (-3.0, 3.0):
    t.mesh("cylinder", xx, -23.4, STONE, width=0.7, height=6.0, depth=0.7, segments=12)               # portico columns
t.mesh("box", 0, -22.7, STONE, y=0.0, width=8.0, height=0.15, depth=1.6, material="Concrete")           # step
for xx in (-10.0, 10.0):
    t.prop("Flag on a Pole", xx, -22.6, 0, tag="square")
CH = (-12, 24, 12, 40)
r = shell(*CH, (0, -1), (0.70, 0.66, 0.58), {"N": [DOOR(0.0, 2.4), WIN(-7.0, 2.2), WIN(7.0, 2.2)], "S": [DOOR(-6.0), DOOR(6.0)], "W": [DOOR(32.0, 1.8)], "E": [DOOR(32.0, 1.8)]}, h=6.0, roof=TILE_R)
ROOMS.append((r, "church", 12 - 0.3, None))
t.block(-2.5, 0, 40, 2.5, 13.0, 44.5, (0.70, 0.66, 0.58))                                          # bell tower
t.mesh("cone", 0, 42.25, TILE_R, y=13.0, width=5.6, height=7.0, depth=5.6, segments=4, yaw=45.0)
t.mesh("box", 0, 39.95, (0.25, 0.3, 0.45), y=2.0, width=1.6, height=3.4, depth=0.1)
t.mesh("wedge", 0, 32, TILE_R, y=6.3, width=24.4, height=2.4, depth=8.2, yaw=180.0)                  # pitched roof, two halves
t.mesh("wedge", 0, 28, TILE_R, y=6.3, width=24.4, height=2.4, depth=8.2, yaw=0.0)
for xx in (-12.15, 12.15):
    for zz in (27.0, 37.0):
        t.mesh("box", xx, zz, (0.25, 0.3, 0.45), y=2.2, width=0.08, height=2.6, depth=1.2)            # stained glass

# ================================================================ the town square: monument, fountain, clock, benches, bus stops
for sx in (-4.0, 4.0):   # two statues either side of the middle (the hill's centre stays open), on plinths you can mantle
    t.block(sx - 1.3, 0, -1.3, sx + 1.3, 0.9, 1.3, STONE)
    t.block(sx - 0.85, 0.9, -0.85, sx + 0.85, 3.8, 0.85, (0.42, 0.48, 0.40))
    t.mesh("capsule", sx, 0, (0.40, 0.50, 0.42), y=3.4, radius=0.42, height=2.2, segments=12)
    t.mesh("sphere", sx, 0, (0.40, 0.50, 0.42), y=5.4, width=0.6, height=0.7, depth=0.6, segments=12, detail=6)
t.prop("Fountain", 0, -17.0, 0, tag="square")
t.prop("Ticking Street Clock", 0, 17.0, 0, tag="square")
for (bx, bz, by) in ((-4.5, -17.0, 90), (4.5, -17.0, 270), (-4.5, 17.0, 90), (4.5, 17.0, 270)):
    t.prop("Bench", bx, bz, by, tag="square")
for (px, pz) in ((-12.5, -19.5), (12.5, -19.5), (-12.5, 19.5), (12.5, 19.5), (-12.5, 10.0), (12.5, -10.0)):
    t.prop("Planter", px, pz, 0, tag="square")
for (px, pz, yy) in ((-9.0, -9.0, 45), (9.0, 9.0, 225), (-9.0, 9.0, 135), (9.0, -9.0, 315)):
    t.prop("Post Litter Bin", px, pz, yy, tag="square")
# bus stops at the square's entries (shelter faces the road), a newsstand, phone booth, cut sight lines down the sidewalks
t.prop("Bus Shelter", -24.0, 0.85, 180, tag="busstop"); t.prop("Bus Stop Sign", -26.6, -0.6, 180, tag="busstop")
t.prop("Bus Shelter", 24.0, -0.85, 0, tag="busstop"); t.prop("Bus Stop Sign", 26.6, 0.6, 0, tag="busstop")
t.prop("Newsstand", -19.6, 0.7, 180, tag="square"); t.prop("Newsstand", 19.6, -0.7, 0, tag="square")
t.prop("Phone Booth", -14.6, -10.8, 90, tag="square"); t.prop("Phone Booth", 14.6, 10.8, 270, tag="square")
t.prop("Hot Dog Cart", -6.0, -21.0, 0, tag="square"); t.prop("Market Stall", 6.5, 21.0, 180, tag="square")
t.prop("Drinking Fountain", 13.6, -16.0, 270, tag="square"); t.prop("Drinking Fountain", -13.6, 16.0, 90, tag="square")
t.prop("Bike Rack", -13.6, -4.0, 90, tag="square"); t.prop("Bike Rack", 13.6, 4.0, 270, tag="square")
t.prop("Bicycle", -13.0, -3.0, 90, tag="square"); t.prop("Kick Scooter", 13.0, 3.4, 270, tag="square")
t.prop("Pigeons", -3.0, 6.0, 30, tag="square"); t.prop("Pigeons", 4.0, -7.0, 200, tag="square"); t.prop("Pigeons", -6.0, -13.0, 80, tag="square")

# ================================================================ junctions: cycling traffic lights, signs, a bus at each terminus
for (x, z, yy) in ((-42.6, -10.6, 0), (-31.4, -10.6, 0), (-42.6, 0.6, 180), (-31.4, 0.6, 180)):
    t.prop("Cycling Traffic Light", x, z, yy, tag="junction")
for (x, z, yy) in ((42.6, 10.6, 180), (31.4, 10.6, 180), (42.6, -0.6, 0), (31.4, -0.6, 0)):
    t.prop("Cycling Traffic Light", x, z, yy, tag="junction")
t.prop("Street Name Sign", -43.0, 3.0, 45, tag="junction"); t.prop("Street Name Sign", 43.0, -3.0, 225, tag="junction")
t.prop("Stop Sign", -31.2, 3.2, 180, tag="junction"); t.prop("Stop Sign", 31.2, -3.2, 0, tag="junction")
t.prop("One Way Sign", -42.9, 12.0, 90, tag="junction"); t.prop("One Way Sign", 42.9, -12.0, 270, tag="junction")
t.prop("Pedestrian Signal", -44.3, -1.5, 90, tag="junction"); t.prop("Pedestrian Signal", 44.3, 1.5, 270, tag="junction")
t.prop("Speed Limit Sign", -60.0, 1.4, 180, tag="junction"); t.prop("Speed Limit Sign", 60.0, -1.4, 0, tag="junction")
t.prop("Yield Sign", -31.3, 22.0, 270, tag="junction"); t.prop("Yield Sign", 31.3, -22.0, 90, tag="junction")
# bus termini at each end of Main Street (spawn cover)
t.prop("Bus", -71.0, -7.4, 0, tag="parked"); t.prop("Bus", 71.0, 7.4, 180, tag="parked")
t.prop("Bus Shelter", -88.0, 0.9, 180, tag="busstop"); t.prop("Bus Shelter", 88.0, -0.9, 0, tag="busstop")

# ================================================================ rail crossing (NE edge) and its twin, the old goods siding (SW edge)
def railway(z, x0, x1):
    slab(x0, z - 2.2, x1, z + 2.2, GRAVEL, "Dirt", h=0.08)
    x = x0 + 0.35
    while x < x1:
        t.mesh("box", x, z, (0.30, 0.22, 0.16), y=0.08, width=0.25, height=0.1, depth=2.6)
        x += 0.75
    for dz in (-0.72, 0.72):
        t.mesh("box", (x0 + x1) / 2, z + dz, (0.42, 0.40, 0.38), y=0.18, width=x1 - x0, height=0.12, depth=0.08, material="Metal")
railway(-48.0, 14.0, 88.0)
slab(33, -51, 41, -45, (0.32, 0.30, 0.30), "Concrete", h=0.1)                  # crossing deck
t.prop("Railway Crossing Lights", 31.6, -43.8, 0, tag="rail"); t.prop("Railway Crossing Lights", 42.4, -52.2, 180, tag="rail")
t.prop("Boom Gate", 31.9, -43.2, 0, tag="rail"); t.prop("Boom Gate", 42.1, -52.8, 180, tag="rail")
t.prop("Warning Beacon", 44.6, -44.0, 0, tag="rail")
t.ops.append({"op": "TrainCar", "x": 64.0, "z": -48.0, "alongX": True, "color": [0.48, 0.24, 0.18]})
for (x0, z0, x1, z1) in ((57.0, -49.6, 71.0, -46.4),):
    t.mark(t.occ, (x0, z0, x1, z1)); t.mark(t.walk, (x0, z0, x1, z1)); t.mark(t.tall, (x0, z0, x1, z1))
    t.boxes.append((x0, z0, x1, z1, 0, 3.6, (0.48, 0.24, 0.18)))
railway(48.0, -88.0, -14.0)
t.prop("Railway Crossing Lights", -31.6, 43.8, 180, tag="rail"); t.prop("Railway Crossing Lights", -42.4, 52.2, 0, tag="rail")
t.prop("Boom Gate", -31.9, 43.2, 180, tag="rail"); t.prop("Boom Gate", -42.1, 52.8, 0, tag="rail")
slab(-41, 45, -33, 51, (0.32, 0.30, 0.30), "Concrete", h=0.1)

# ================================================================ quarters
# --- NW: the park (x -86..-18, z -62..-30): pond, playground, paths, hedges
duck_n = tk.duck_pond(t, -58.0, -45.0, 7.5, 5.0, rng)
t.prop("Bench", -58.0, -38.4, 180, tag="park"); t.prop("Bench", -49.0, -45.0, 270, tag="park"); t.prop("Bench", -67.0, -45.5, 90, tag="park")
t.prop("Post Litter Bin", -56.4, -38.2, 0, tag="park"); t.prop("Bird Bath", -45.0, -52.0, 0, tag="park")
# playground with soft surface (Tiles: busted) and a fence
slab(-40, -52, -26, -38, (0.55, 0.30, 0.25), "Tiles", h=0.05)
t.prop("Swing Set", -33.0, -49.5, 0, tag="playground"); t.prop("Swing Set", -36.5, -41.5, 90, tag="playground")
for (px, pz) in ((-28.0, -40.5), (-28.6, -49.0), (-38.8, -50.8)):
    t.prop("Pinwheel", px, pz, rng.uniform(0, 360), tag="playground")
t.prop("Paddling Pool", -30.0, -44.0, 0, tag="playground"); t.prop("Sports Balls", -31.0, -42.0, 0, tag="playground"); t.prop("Teddy Bear", -29.4, -46.2, 30, tag="playground")
t.prop("Park Picnic Table", -44.0, -38.5, 0, tag="park"); t.prop("Park Picnic Table", -72.0, -38.0, 90, tag="park")
t.prop("Drinking Fountain", -40.6, -36.0, 0, tag="park")
for (x0, x1, z) in ((-40.0, -35.0, -52.0), (-31.0, -26.0, -52.0)):
    pass
def path_furniture(pts, w, seed, extras):
    r2 = random.Random(seed)
    for k in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[k], pts[k + 1]
        L, (ux, uz) = tk._along((ax, az), (bx, bz))
        if L < 8:
            continue
        side = 1 if k % 2 == 0 else -1
        nx, nz = -uz * side, ux * side
        a = (ax + nx * (w / 2 + 0.15) + ux * 2.0, az + nz * (w / 2 + 0.15) + uz * 2.0); b = (bx + nx * (w / 2 + 0.15) - ux * 2.0, bz + nz * (w / 2 + 0.15) - uz * 2.0)
        tk.street_furniture(t, a, b, (-nx, -nz), r2, lamp_every=19.0, lamp_kinds=("Flickering Street Lamp", "Street Lamp"), trees=False,
                            extras=extras, kerb_off=0.4, back_off=0.9, tag="park")


PARK_EXTRAS = [("Bench", "Post Litter Bin"), ("Bench",), ("Post Litter Bin",), ("Park Picnic Table",), ("Bench", "Trash Can")]
t.prop("Hammock", -70.0, -48.6, 90, tag="park"); t.prop("Bird on a Perch", -49.0, -40.0, 30, tag="park")
for (x, z) in ((-53.0, -50.5), (-63.5, -40.6), (-51.6, -42.0)):
    t.prop("Duck Family" if x < -60 else "Mallard Duck", x, z, rng.uniform(0, 360), tag="pond")
# --- SE: the car park (x 30..72, z 33..55) with its kiosk entrance off the back lane
slab(28, 31, 76, 57, (0.33, 0.32, 0.32), None, h=0.04)
car_n = tk.car_park(t, 30.0, 33.0, 72.0, 55.0, rng, along_x=True, fill=0.62)
t.prop("Boom Barrier", 25.5, 31.2, 90, tag="carpark"); t.prop("Ticket Machine", 26.8, 34.0, 90, tag="carpark")
t.prop("Shopping Cart", 74.0, 44.0, 30, tag="carpark"); t.prop("Shopping Cart", 54.0, 44.0, 200, tag="carpark")
# --- SW: houses with back gardens along Mill Road's west side (x -86..-46, z 22..44)
for (hx0, hz0, hx1, hz1, c) in ((-58, 23, -47, 31, PLASTER), (-74, 23, -63, 31, (0.70, 0.56, 0.44))):
    solid_building(hx0, hz0, hx1, hz1, 5.0, c, windows="SE")
    t.mesh("wedge", (hx0 + hx1) / 2, (hz0 + hz1) / 2 - 2.0, TILE_R, y=5.0, width=hx1 - hx0 + 0.6, height=2.2, depth=4.3, yaw=180.0)
    t.mesh("wedge", (hx0 + hx1) / 2, (hz0 + hz1) / 2 + 2.0, TILE_R, y=5.0, width=hx1 - hx0 + 0.6, height=2.2, depth=4.3, yaw=0.0)
    t.prop("Smoking Chimney" if hx0 == -74 else "Brick Chimney", hx0 + 2.0, (hz0 + hz1) / 2, 0, y=5.6, check=False, tag="roof")
    t.prop("TV Antenna", hx1 - 2.0, (hz0 + hz1) / 2 + 1.0, 0, y=6.2, check=False, tag="roof")
    t.prop("Satellite Dish", hx1 - 0.4, hz0 + 2.0, 90, y=3.2, check=False, tag="wall")
    ivy(hx0 + 2.5, hz1, (0, 1), 3.0, 3.4)
garden = [("Garden Shed", -79.0, 39.0, 90), ("Patio Table", -52.5, 35.5, 0), ("Patio Chair", -54.2, 35.0, 90), ("Patio Chair", -50.8, 36.0, 270),
          ("Gas BBQ", -56.5, 38.5, 180), ("Laundry Line", -66.0, 36.5, 0), ("Lawnmower", -70.5, 38.0, 30), ("Wheelbarrow", -61.0, 40.5, 120),
          ("Dog Kennel", -48.5, 40.5, 270), ("Garden Gnome", -63.0, 33.5, 160), ("Garden Gnome", -49.5, 33.2, 200), ("Raised Bed", -72.0, 34.0, 0),
          ("Compost Bin", -77.0, 33.5, 0), ("Kettle BBQ", -69.0, 33.8, 0), ("Sun Lounger", -59.5, 37.8, 90), ("Hose Reel", -47.6, 32.6, 270),
          ("Flower Pots", -57.0, 32.2, 0), ("Watering Can", -71.0, 36.0, 0), ("Bird Bath", -64.0, 41.0, 0), ("Garden Tools", -78.0, 36.0, 90)]
for name, x, z, yy in garden:
    t.prop(name, x, z, yy, tag="garden")
# picket fences along the front gardens on Mill Road, fence panels between the gardens
for z in (22.6, 24.7, 26.8, 31.4, 33.5, 35.6, 37.7, 39.8):
    t.prop("Picket Fence", -44.9, z, 90, tag="garden")
for x in (-61.0,):
    for z in (32.2, 34.2, 36.2, 38.2, 40.2):
        t.prop("Fence Panel", x, z, 90, tag="garden")
# --- SW corner of the square: the vacant lot (x -28..-15, z 21..44), overgrown
for name, x, z, yy in [("Builders Skip", -22.0, 24.6, 0), ("Concrete Pipe", -26.0, 30.0, 90), ("Rubble Pile", -18.4, 30.5, 20),
                       ("Car On Bricks", -22.5, 38.0, 80), ("Tyre Stack", -26.6, 35.0, 0), ("Brick Pile", -17.6, 35.2, 40), ("Shopping Cart", -19.5, 41.2, 130),
                       ("Old Sofa", -25.5, 42.0, 15), ("Cinder Blocks", -20.0, 27.8, 70), ("Pallet", -26.4, 26.0, 10)]:
    t.prop(name, x, z, yy, tag="lot")
# --- NE: yard behind the apartment (x 15..30, z -44..-21), a water tower and a laundry line
for name, x, z, yy in [("Water Tower", 22.0, -38.0, 0), ("Laundry Line", 23.0, -28.0, 0), ("Garden Shed", 17.6, -31.0, 90),
                       ("Wheelie Bin", 18.0, -22.0, 0), ("Running Generator", 27.4, -32.5, 90), ("Portable Toilet", 27.8, -42.5, 270),
                       ("Diesel Generator", 22.0, -44.0, 0), ("Scaffold Board Stack", 17.4, -26.0, 90)]:
    t.prop(name, x, z, yy, tag="yard")
# --- the churchyard (z 40..56) and the town hall garden (z -56..-40): trees, headstones (cosmetic), a war memorial bench
for k in range(18):
    hx = -10.0 + (k % 6) * 4.0 + (1.0 if (k // 6) % 2 else 0); hz = 47.0 + (k // 6) * 3.0
    if abs(hx) > 3.4:
        t.mesh("box", hx, hz, (0.6, 0.6, 0.58), width=0.7, height=rng.uniform(0.7, 1.2), depth=0.18, yaw=round(rng.uniform(-6, 6), 1), material="Concrete")
for (bx, bz, by) in ((-8.0, -45.0, 0), (8.0, -45.0, 0), (-8.0, 45.0, 180), (8.0, 45.0, 180)):
    t.prop("Bench", bx, bz, by, tag="park")


for (x, z, yy) in ((-68.0, -11.0, 0), (-58.6, -11.2, 0), (-38.6, -11.0, 0), (68.0, 11.0, 180), (58.6, 11.2, 180), (38.6, 11.0, 180), (-22.0, 2.9, 180), (24.0, -2.9, 0)):
    t.prop("Sandwich Board", x, z, yy + rng.uniform(-15, 15), tag="street")
t.prop("Scaffold Tower", -23.0, -10.6, 0, tag="street"); t.prop("Builders Skip", 23.0, 10.7, 0, tag="street")
t.prop("Shopping Cart", 33.0, 10.8, 200, tag="street"); t.prop("Shopping Cart", 39.6, 11.0, 160, tag="street")
t.prop("Rocking Chair", -45.6, 27.0, 90, tag="garden"); t.prop("Rocking Chair", -45.6, 36.5, 90, tag="garden")
t.prop("Weather Vane", 23.0, 18.0, 0, y=7.9, check=False, tag="roof"); t.prop("Pennant Pole", -14.2, -21.2, 0, tag="square"); t.prop("Pennant Pole", 14.2, 21.2, 0, tag="square")
t.prop("Hanging Banner", 0.0, -24.25, 0, y=3.3, check=False, tag="sign")

# ================================================================ lanes (walking routes kept free of solid dressing)
LANES = [
    ([(-92, -5), (-16, -5)], 3.4), ([(16, 5), (92, 5)], 3.4),                    # Main Street
    ([(-16, -5), (-6, -5), (-6, 5), (6, 5), (16, 5)], 3.0),                      # through the square
    ([(-37, -5), (-37, 60)], 3.0), ([(37, 5), (37, -60)], 3.0),                  # Mill Road, Station Road
    ([(-86, -27), (-14, -27), (-14, -10)], 2.6), ([(86, 27), (14, 27), (14, 10)], 2.6),   # back lanes N-W and S-E
    ([(-82, 17), (-14, 17), (-14, 30)], 2.4), ([(82, -17), (14, -17), (14, -30)], 2.4),   # back lanes S-W and N-E
    ([(-44, -27), (-44, -12)], 2.2), ([(44, 27), (44, 12)], 2.2),                # passages through the rows
    ([(-14, -27), (-14, -45), (14, -45), (14, -27)], 2.4), ([(14, 27), (14, 45), (-14, 45), (-14, 27)], 2.4),  # round the hall and church
    ([(-6, -22), (-6, -26)], 2.0), ([(6, 22), (6, 26)], 2.0),
    ([(-80, -27), (-80, -40), (-66, -52), (-46, -54), (-26, -54), (-20, -40), (-20, -30)], 2.4),               # park loop
    ([(-62, -27), (-62, -38), (-40, -38), (-20, -38)], 2.2),
    ([(80, 5), (80, -30), (62, -40), (44, -40)], 2.4), ([(44, -24), (76, -24)], 2.4),                           # gas station
    ([(80, 27), (80, 40)], 2.2), ([(30, 44), (74, 44)], 2.4),                                                    # car park aisle
    ([(-80, 17), (-80, 30)], 2.2), ([(-44, 44), (-60, 44)], 2.0), ([(-20, 20), (-20, 50)], 2.0),
]
for pts, w in LANES:
    t.add_lane(pts, w)
for pts, w in LANES[15:17]:
    path_furniture(pts, w, 330, PARK_EXTRAS)

# ================================================================ furnishing the interiors
for room, kind, du, bu in ROOMS:
    if kind == "church":
        # pews (benches) in two blocks either side of the aisle, an altar table, a piano, candles; the middle stays open
        W, D = room.W, room.D
        for row in range(5):
            v = 1.8 + row * 1.25
            for cu in (W / 2 - 8.5, W / 2 - 6.4, W / 2 + 6.4, W / 2 + 8.5):
                room.put("Bench", cu, v, "back", rng=rng, tag="interior")
        room.put("Table", W / 2, D - 0.9, "front", rng=rng, tag="interior"); room.put("Upright Piano", 0.5, D - 1.4, "right", rng=rng, tag="interior")
        room.put("Hanging Lantern", W / 2 - 3.0, D - 0.6, "front", rng=rng, tag="interior"); room.put("Hanging Lantern", W / 2 + 3.0, D - 0.6, "front", rng=rng, tag="interior")
        room.put("Grandfather Clock", W - 0.4, D - 1.0, "left", rng=rng, tag="interior")
        continue
    if kind == "hall":
        W, D = room.W, room.D
        for row in range(4):
            v = 1.6 + row * 1.0
            for k in range(5):
                for side in (-1, 1):
                    cu = W / 2 + side * (5.2 + k * 0.7)
                    room.put("Plastic Chair", cu, v, "back", rng=rng, jitter=8, tag="interior")
        room.put("Conference Table", W / 2, D - 1.0, "front", rng=rng, tag="interior")
        room.put("Grandfather Clock", 0.4, D - 1.0, "right", rng=rng, tag="interior"); room.put("Upright Piano", W - 0.5, D - 1.6, "left", rng=rng, tag="interior")
        room.put("Bookshelf", 0.25, 2.0, "right", rng=rng, tag="interior"); room.put("Bookshelf", W - 0.25, 2.0, "left", rng=rng, tag="interior")
        room.put("Reception Desk", W - 2.2, 1.0, "back", rng=rng, tag="interior")
        room.put("Filing Cabinet", W - 0.4, D - 3.6, "left", rng=rng, tag="interior"); room.put("Filing Cabinet", 0.4, D - 3.6, "right", rng=rng, tag="interior")
        continue
    tk.furnish(t, room, kind, rng, door_u=du, back_door_u=bu)

# ================================================================ back lanes: bins by the back doors, crates and pallets along the walls
BACK_DOORS = [((-73.0, -24.3), (1, 0), (0, -1), -1), ((-58.0, -24.3), (1, 0), (0, -1), 1), ((-40.0, -24.3), (1, 0), (0, -1), 1),
              ((73.0, 24.3), (1, 0), (0, 1), 1), ((58.0, 24.3), (1, 0), (0, 1), -1), ((40.0, 24.3), (1, 0), (0, 1), -1),
              ((-56.0, 14.3), (1, 0), (0, 1), 1), ((-27.0, 14.3), (1, 0), (0, 1), 1), ((56.0, -14.3), (1, 0), (0, -1), -1),
              ((27.0, -14.3), (1, 0), (0, -1), -1), ((71.0, -36.3), (1, 0), (0, -1), -1),
              ((-6.0, -40.3), (1, 0), (0, -1), -1), ((6.0, 40.3), (1, 0), (0, 1), 1)]
for k, (door, wd, od, side) in enumerate(BACK_DOORS):
    tk.bins_by_door(t, door, wd, od, rng, side=side, dumpster=k < 11, seed=100 + k)
# behind the solid buildings: more dumpsters (each building has a back door)
for k, (door, wd, od, side) in enumerate([((-50.0, -24.0), (1, 0), (0, -1), 1), ((-23.0, -24.0), (1, 0), (0, -1), -1), ((50.0, 24.0), (1, 0), (0, 1), -1),
                                          ((23.0, 24.0), (1, 0), (0, 1), 1), ((-71.0, 14.0), (1, 0), (0, 1), 1)]):
    t.mesh("box", door[0], door[1] + od[1] * 0.04, (0.20, 0.18, 0.16), y=0.0, width=1.2, height=2.2, depth=0.06)
    tk.bins_by_door(t, door, wd, od, rng, side=side, seed=200 + k)


def lane_dressing(a, b, out, names, n, seed):
    """Crates, pallets and junk lined up against a back wall (out = unit vector from the wall into the lane)."""
    r2 = random.Random(seed); L, (ux, uz) = tk._along(a, b)
    placed = 0
    for _ in range(n * 6):
        if placed >= n:
            break
        name = r2.choice(names); s = r2.uniform(0.8, L - 0.8)
        dep = t.P[name]["d"]
        x = a[0] + ux * s + out[0] * (dep / 2 + 0.08); z = a[1] + uz * s + out[1] * (dep / 2 + 0.08)
        if t.prop(name, x, z, yaw_facing(*out) + r2.uniform(-6, 6), lanes=True, tag="lane"):
            placed += 1


JUNK = ["Wooden Crate", "Small Crate", "Pallet Stack", "Pallet", "Cardboard Stack", "Cardboard Box Large", "Oil Drum", "Rusty Drum",
        "Plank Pile", "Brick Pile", "Gas Meter", "Junction Box", "Air Duct Riser", "Tyre Stack", "Sawhorse Barricade", "Cable Spool"]
# fire escapes up the back of the two-storey buildings (before the lanes get their junk: none under them or at their foot)
t.prop("Fire Escape Stairs", -26.5, -24.9, 0, check=False, tag="lane"); t.prop("Fire Escape Landing", -23.5, -24.9, 0, check=False, tag="lane")
t.prop("Fire Escape Stairs", 26.5, 24.9, 180, check=False, tag="lane"); t.prop("Fire Escape Landing", 23.5, 24.9, 180, check=False, tag="lane")
for (x0, z0, x1, z1) in ((-29.6, -26.4, -21.0, -24.0), (21.0, 24.0, 29.6, 26.4)):
    t.mark(t.occ, (x0, z0, x1, z1)); t.mark(t.walk, (x0, z0, x1, z1))
lane_dressing((-84, -24.0), (-16, -24.0), (0, -1), JUNK, 16, 1); lane_dressing((16, 24.0), (84, 24.0), (0, 1), JUNK, 16, 2)
lane_dressing((-80, 14.0), (-44, 14.0), (0, 1), JUNK, 6, 3); lane_dressing((-30, 14.0), (-12, 14.0), (0, 1), JUNK, 4, 4)
lane_dressing((44, -14.0), (82, -14.0), (0, -1), JUNK, 4, 5); lane_dressing((12, -14.0), (30, -14.0), (0, -1), JUNK, 4, 6)

# ================================================================ roofs and walls
ROOFS = [(-76, -24, -62, -12, STOREY + 0.3), (-62, -24, -54, -12, STOREY + 0.3), (-54, -24, -46, -12, 5.6), (-42, -24, -30, -12, STOREY + 0.3), (-30, -24, -16, -12, 7.6),
         (62, 12, 76, 24, STOREY + 0.3), (54, 12, 62, 24, STOREY + 0.3), (46, 12, 54, 24, 5.6), (30, 12, 42, 24, STOREY + 0.3), (16, 12, 30, 24, 7.6),
         (-80, 2, -62, 14, 8.4), (-62, 2, -44, 14, STOREY + 0.3), (-30, 2, -16, 14, STOREY + 0.3), (16, -14, 30, -2, STOREY + 0.3), (54, -36, 74, -27, STOREY + 0.3),
         (-12, -40, 12, -24, 6.3)]
for k, (x0, z0, x1, z1, y) in enumerate(ROOFS):
    rr = random.Random(500 + k)
    # keep the diner/arcade neon signs and the bell frame clear
    if (x0, z0) == (-12, -40):
        tk.roof_kit(t, x0, z0, x1, -37.5 - 1.2, y, rr, n=3)
        tk.roof_kit(t, x0, -34.0, x1, z1, y, rr, n=4)
        continue
    tk.roof_kit(t, x0, z0, x1, z1, y, rr)
WALLS = [((-84, -24.05), (-16, -24.05), (0, -1)), ((16, 24.05), (84, 24.05), (0, 1)), ((-80, 14.05), (-44, 14.05), (0, 1)), ((-30, 14.05), (-16, 14.05), (0, 1)),
         ((44, -14.05), (80, -14.05), (0, -1)), ((16, -14.05), (30, -14.05), (0, -1)), ((-80.05, 2), (-80.05, 14), (-1, 0)), ((-76.05, -24), (-76.05, -12), (-1, 0)),
         ((76.05, 12), (76.05, 24), (1, 0)), ((-16, -24), (-16, -12), (1, 0)), ((16, 12), (16, 24), (-1, 0))]
for k, (a, b, out) in enumerate(WALLS):
    tk.wall_kit(t, a, b, out, random.Random(600 + k), skip=[(s - 1.2, s + 1.2) for s in range(0, 80, 15)])
# ivy creeping up walls along the lanes and the park side
for (x, z, f, w, h) in ((-70.0, -24.0, (0, -1), 4.0, 3.6), (-48.0, -24.0, (0, -1), 3.0, 5.0), (-33.0, -24.0, (0, -1), 3.5, 3.2), (-20.0, -24.0, (0, -1), 4.0, 6.5),
                        (70.0, 24.0, (0, 1), 4.0, 3.6), (48.0, 24.0, (0, 1), 3.0, 5.0), (33.0, 24.0, (0, 1), 3.5, 3.2), (20.0, 24.0, (0, 1), 4.0, 6.5),
                        (-76.0, -18.0, (-1, 0), 3.0, 3.0), (76.0, 18.0, (1, 0), 3.0, 3.0), (-74.0, 14.0, (0, 1), 4.0, 6.0), (-12.0, 30.0, (-1, 0), 3.0, 4.5),
                        (12.0, -30.0, (1, 0), 3.0, 4.5), (-60.0, 14.0, (0, 1), 2.5, 2.8), (26.0, -14.0, (0, -1), 2.5, 3.0), (-8.0, 40.0, (0, 1), 4.0, 4.0)):
    ivy(x, z, f, w, h)

# ================================================================ streets: kerb parking, street furniture, street trees
def skip_ranges(a, b, xs):
    """Skip intervals (arc lengths from a) around the given x (or z) positions along an axis-aligned line."""
    L = math.hypot(b[0] - a[0], b[1] - a[1]); horiz = abs(b[0] - a[0]) > abs(b[1] - a[1])
    out = []
    for (c, w) in xs:
        s = abs(c - (a[0] if horiz else a[1]))
        out.append((s - w / 2, s + w / 2))
    return out


DOORS_NW = [(-66.5, 2.5), (-56.3, 2.2), (-44.0, 4.4), (-34.0, 2.5), (-42.0, 2.0), (-16.0, 2.0)]
DOORS_SW = [(-48.0, 2.2), (-37.0, 15.0), (-20.5, 2.5), (-16.0, 2.0), (-24.0, 5.0), (-19.6, 3.4), (-88.0, 5.0), (-71.0, 13.0)]
# West Main north kerb (z -9) and south kerb (z -1); East Main mirrored
tk.kerb_parking(t, (-74, -9), (-17, -9), (0, 1), rng, skip=skip_ranges((-74, -9), (-17, -9), [(-50.0, 3.0), (-37.0, 9.0), (-22.5, 3.5), (-63.0, 2.0)]), tag="parked")
tk.kerb_parking(t, (-74, -1), (-45, -1), (0, -1), rng, skip=skip_ranges((-74, -1), (-45, -1), [(-60.0, 2.0), (-50.0, 3.0)]), tag="parked")
tk.kerb_parking(t, (-29, -1), (-17, -1), (0, -1), rng, skip=skip_ranges((-29, -1), (-17, -1), [(-24.0, 5.0), (-22.5, 3.5)]), tag="parked")
tk.kerb_parking(t, (17, 9), (74, 9), (0, -1), rng, skip=skip_ranges((17, 9), (74, 9), [(50.0, 3.0), (37.0, 9.0), (22.5, 3.5), (63.0, 2.0)]), tag="parked")
tk.kerb_parking(t, (46, 1), (74, 1), (0, 1), rng, skip=skip_ranges((46, 1), (74, 1), [(50.0, 3.0)]), fill=0.35, tag="parked")
tk.kerb_parking(t, (17, 1), (29, 1), (0, 1), rng, skip=skip_ranges((17, 1), (29, 1), [(24.0, 5.0), (22.5, 3.5)]), tag="parked")
tk.kerb_parking(t, (-41, 22), (-41, 56), (1, 0), rng, skip=skip_ranges((-41, 22), (-41, 56), [(43.0, 9.0)]), fill=0.5, tag="parked")
tk.kerb_parking(t, (41, -22), (41, -56), (-1, 0), rng, skip=skip_ranges((41, -22), (41, -56), [(-48.0, 9.0)]), fill=0.5, tag="parked")
SF = [((-76, -9), (-17, -9), (0, 1), DOORS_NW), ((-76, -1), (-44, -1), (0, -1), DOORS_SW), ((-30, -1), (-17, -1), (0, -1), DOORS_SW),
      ((17, 9), (76, 9), (0, -1), [(66.5, 2.5), (56.3, 2.2), (44.0, 4.4), (34.0, 2.5), (24.0, 5.0), (19.6, 3.4), (88.0, 5.0)]),
      ((44, 1), (76, 1), (0, 1), [(60.0, 3.0), (48.0, 2.0)]), ((17, 1), (30, 1), (0, 1), [(20.5, 2.5), (24.0, 5.0), (19.6, 3.4)]),
      ((-41, 3), (-41, 58), (1, 0), [(10.0, 3.0), (43.0, 9.0)]), ((-33, 3), (-33, 58), (-1, 0), [(9.0, 3.0), (32.0, 3.0), (43.0, 9.0)]),
      ((41, -3), (41, -58), (-1, 0), [(-10.0, 3.0), (-48.0, 9.0)]), ((33, -3), (33, -58), (1, 0), [(-9.0, 3.0), (-32.0, 3.0), (-48.0, 9.0)])]
for k, (a, b, road, doors) in enumerate(SF):
    tk.street_furniture(t, a, b, road, random.Random(700 + k), skip=skip_ranges(a, b, doors), back_off=2.25, lamp_every=14.0)

# ================================================================ trees and foliage
# park trees round the pond and along the loop; churchyard and town hall garden; yards; along the edge
park_trees = [(-80, -45), (-74, -52), (-68, -38), (-64, -55), (-52, -56), (-46, -42), (-48, -33), (-70, -33), (-77, -34), (-82, -38),
              (-40, -57), (-30, -57), (-24, -50), (-22, -34), (-34, -34), (-26, -44), (-62, -50), (-54, -36), (-42, -48), (-86, -26)]
for x, z in park_trees:
    t.tree(x + rng.uniform(-1, 1), z + rng.uniform(-1, 1), rng.uniform(6.5, 10.5), "tree")
for x, z in [(-11, 50), (11, 50), (-13, 43), (13, 43), (-4, 54), (5, 54), (-11, -50), (11, -50), (-13, -43), (13, -43), (-4, -54), (5, -54), (0, -47)]:
    t.tree(x + rng.uniform(-0.6, 0.6), z + rng.uniform(-0.6, 0.6), rng.uniform(7, 10), "pine" if abs(x) > 12 else "tree")
for x, z in [(-84, 34), (-66, 44), (-54, 46), (-46, 50), (-28, 52), (-17, 46), (-25, 22.5), (18, -46), (27, -48), (16, -24), (48, -52), (58, -40),
             (72, -40), (80, -32), (84, 22), (82, 30), (76, 52), (62, 52), (48, 57), (34, 57), (22, 50), (20, 38), (-90, 10), (-86, 16), (90, -10)]:
    t.tree(x + rng.uniform(-1, 1), z + rng.uniform(-1, 1), rng.uniform(6, 9.5), "tree")

# hedges: along the park's lane edge (gaps at the paths), round the gardens, along the car park, round the churchyard
for (a, b) in [((-85, -31.2), (-64, -31.2)), ((-60, -31.2), (-46, -31.2)), ((-42, -31.2), (-22, -31.2)),
               ((-80, 21.5), (-62, 21.5)), ((-60, 21.5), (-46, 21.5)), ((-82, 31.5), (-82, 44)), ((-61, 42), (-46, 42)),
               ((29, 32), (29, 56)), ((74, 32), (74, 48)), ((-13.5, 42), (-13.5, 56)), ((13.5, 42), (13.5, 56)), ((-13.5, -42), (-13.5, -56)), ((13.5, -42), (13.5, -56)),
               ((45.2, -44), (45.2, -38)), ((15.2, -46), (15.2, -21)), ((-15.2, 21), (-15.2, 46))]:
    tk.hedge_strip(t, a, b)
# weeds at the foot of the walls on the back lanes and along the kerbs of the side roads
for (a, b) in [((-84, -24.6), (-16, -24.6)), ((16, 24.6), (84, 24.6)), ((-80, 14.6), (-44, 14.6)), ((44, -14.6), (82, -14.6)),
               ((-33.4, 4), (-33.4, 60)), ((33.4, -4), (33.4, -60)), ((-40.6, 4), (-40.6, 60)), ((40.6, -4), (40.6, -60))]:
    tk.weeds_along(t, a, b)

BX0, BZ0, BX1, BZ1 = t.x0, t.z0, t.x1, t.z1
# everywhere: grass and weeds (killed on intact paving and indoors, 10% on busted paving)
t.region("grass", 3.2, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.45, scale=[0.8, 1.3])
t.region("shrub", 0.35, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.6)
t.region("bush", 0.06, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.8, scale=[0.8, 1.3])
# the park: lush meadow, shrubs and bushes
PARK = [[-90, -30], [-16, -30], [-16, -64], [-90, -64]]
t.region("grass", 6.0, polygon=PARK, patchiness=0.35, scale=[1.0, 1.6], color=[0.36, 0.50, 0.22])
t.region("shrub", 0.8, polygon=PARK, patchiness=0.4)
t.region("bush", 0.25, polygon=PARK, patchiness=0.5, scale=[0.9, 1.5])
t.region("tree", 0.02, polygon=PARK, height=[6.5, 10.5])
# gardens, the vacant lot, the churchyard and the yards: long grass
for poly in ([[-86, 21], [-44, 21], [-44, 46], [-86, 46]], [[-29, 20], [-15, 20], [-15, 50], [-29, 50]], [[-14, 40], [14, 40], [14, 58], [-14, 58]],
             [[-14, -40], [14, -40], [14, -58], [-14, -58]], [[14, -46], [30, -46], [30, -20], [14, -20]], [[76, 24], [90, 24], [90, 50], [76, 50]],
             [[44, -46], [86, -46], [86, -36], [44, -36]]):
    t.region("grass", 5.5, polygon=poly, patchiness=0.4, scale=[1.0, 1.8], color=[0.40, 0.50, 0.24])
    t.region("shrub", 0.7, polygon=poly, patchiness=0.4)
    t.region("bush", 0.2, polygon=poly, patchiness=0.6)
    t.region("tree", 0.012, polygon=poly, height=[6.0, 9.5])
# the rail lines: weeds between the sleepers
for (x0, z0, x1, z1) in ((14, -50.2, 88, -45.8), (-88, 45.8, -14, 50.2)):
    t.region("grass", 4.5, rect=[x0, z0, x1, z1], patchiness=0.5, color=[0.46, 0.5, 0.26])
    t.region("shrub", 0.5, rect=[x0, z0, x1, z1], patchiness=0.5)

# foliage paths: the park paths (gravel, the odd tuft) and the walking lanes inside the park
for pts, w in LANES[15:17]:
    t.path(pts, w, density=0.1)
    for k in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[k], pts[k + 1]
        L = math.hypot(bx - ax, bz - az)
        t.mesh("box", (ax + bx) / 2, (az + bz) / 2, GRAVEL, y=0.0, width=round(L + w * 0.5, 2), height=0.03, depth=w, yaw=yaw_along(bx - ax, bz - az))

# ================================================================ the boundary: mixed edge styles along the outline
def arc_of(x, z):
    """Arc length along the game outline of the outline point nearest (x, z)."""
    pts = t.outline; best = (1e9, 0); s = 0.0
    for i in range(len(pts)):
        d = (pts[i][0] - x) ** 2 + (pts[i][1] - z) ** 2
        if d < best[0]:
            best = (d, s)
        s += math.hypot(pts[(i + 1) % len(pts)][0] - pts[i][0], pts[(i + 1) % len(pts)][1] - pts[i][1])
    return best[1]


EDGE = [((-92, -18), (-86, -34), "backs"), ((-86, -34), (-26, -62), "hedge"), ((-26, -62), (14, -64), "hedge"), ((14, -64), (30, -62), "panel"),
        ((48, -59), (80, -44), "fence"), ((80, -44), (93, -18), "backs"), ((93, 18), (86, 34), "backs"), ((86, 34), (32, 60), "fence"),
        ((24, 58), (-14, 58), "hedge"), ((-14, 59), (-30, 60), "panel"), ((-48, 58), (-78, 42), "picket"), ((-78, 42), (-90, 22), "hedge")]
edge_counts = {}
for (a, b, style) in EDGE:
    n, total = tk.edge_run(t, arc_of(*a), arc_of(*b), style, rng)
    edge_counts[style] = edge_counts.get(style, 0) + n
# roads leave the map: road blocks across the far ends
for (x, z, yy) in ((-35.0, 57.5, 0), (-39.0, 57.5, 0), (35.0, -57.0, 0), (39.0, -57.0, 0)):
    t.prop("Water Barrier", x, z, yy, margin=0.3, tag="edge")
for sx in (1, -1):   # road works close Main Street just short of each spawn: staggered fence panels, so no line runs down the street
    for (x, z, yy) in ((-79.4, -7.0, 90), (-77.2, -3.0, 90)):
        t.prop("Construction Fence Panel", x * sx, z * sx, yy, tag="edge")
    t.prop("Road Works Sign", -75.6 * sx, -7.6 * sx, 270 if sx > 0 else 90, tag="edge")
    t.prop("Traffic Cone", -76.0 * sx, -5.2 * sx, 0, tag="edge"); t.prop("Traffic Cone", -75.8 * sx, -1.8 * sx, 0, tag="edge")
    t.prop("Arrow Board", -82.5 * sx, -6.0 * sx, 90 if sx > 0 else 270, tag="edge")

# ================================================================ spawns, waypoints (after every solid thing)
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
t.ops.append({"op": "AutoWaypoints", "spacing": 6})
WAYPOINTS = []
for room, kind, du, bu in ROOMS:
    ix0, iz0, ix1, iz1 = room.rect
    WAYPOINTS.append(((ix0 + ix1) / 2, (iz0 + iz1) / 2))
    if du is not None:
        WAYPOINTS.append(room.xz(du, 1.0))
for (x, z) in WAYPOINTS:
    if t.free((x - 0.35, z - 0.35, x + 0.35, z + 0.35)):
        t.ops.append({"op": "Waypoint", "x": tk.R(x), "z": tk.R(z)})

# ================================================================ cosmetic clutter (the game's Clutter op runs last and keeps out of walls)
LITTER = ["Litter", "Litter", "Litter", "Newspaper", "Soda Cans", "Bottles", "Bottle", "Plastic Bag", "Flattened Cardboard", "Cardboard Box Small",
          "Trash Bag", "Newspaper Stack", "Loose Bricks", "Rubble", "Cinder Blocks", "Plank", "Tyre", "Car Wheel", "Manhole Cover"]
t.clutter(LITTER, BX0, BZ0, 0, BZ1, 1100, 41); t.clutter(LITTER, 0, BZ0, BX1, BZ1, 1100, 42)  # a Clutter op takes up to 2000
# along the kerbs: leaves of newspaper, cans, bottles in the gutters
for k, (a, b) in enumerate([((-90, -8.6), (-16, -8.6)), ((-90, -1.4), (-16, -1.4)), ((16, 1.4), (90, 1.4)), ((16, 8.6), (90, 8.6)),
                            ((-40.6, 2), (-40.6, 60)), ((-33.4, 2), (-33.4, 60)), ((40.6, -2), (40.6, -60)), ((33.4, -2), (33.4, -60))]):
    t.clutter(["Litter", "Newspaper", "Soda Cans", "Bottle", "Plastic Bag", "Storm Drain"], a[0], a[1], b[0], b[1], 70, 50 + k, width=1.2)
# the back lanes: bags, cardboard, planks, bottles
for k, (a, b) in enumerate([((-84, -27), (-16, -27)), ((16, 27), (84, 27)), ((-82, 17), (-12, 17)), ((12, -17), (82, -17))]):
    t.clutter(["Trash Bag", "Flattened Cardboard", "Open Cardboard Box", "Plank", "Bottles", "Litter", "Plastic Bag", "Cardboard Box Small"], a[0], a[1], b[0], b[1], 110, 60 + k, width=5.0)
# indoors: things left behind
for k, (room, kind, du, bu) in enumerate(ROOMS):
    ix0, iz0, ix1, iz1 = room.rect
    t.clutter(["Book Pile", "Paint Cans", "Newspaper", "Litter", "Cardboard Box Small", "Plastic Bag", "Bottles", "Soda Cans", "Backpack", "Broom", "Mop and Bucket", "Bucket", "Toolbox"],
              ix0 + 0.4, iz0 + 0.4, ix1 - 0.4, iz1 - 0.4, 16, 80 + k)
# the car park, the gas station forecourt, the square, the park and the lot
t.clutter(["Litter", "Soda Cans", "Plastic Bag", "Car Wheel", "Tyre", "Car Door", "Road Plate"], 28, 31, 76, 57, 120, 90)
t.clutter(["Litter", "Soda Cans", "Plastic Bag", "Tyre", "Newspaper"], 44, -36, 82, -2, 80, 91)
t.clutter(["Litter", "Newspaper", "Pigeons", "Soda Cans", "Plastic Bag", "Bottle"], -16, -24, 16, 24, 120, 92)
t.clutter(["Litter", "Plastic Bag", "Bottle", "Newspaper", "Sports Balls", "Duck Decoy", "Log", "Tree Stump"], -88, -62, -16, -30, 120, 93)
t.clutter(["Rubble", "Loose Bricks", "Cinder Blocks", "Plank", "Tyre", "Litter", "Bottles", "Rubble Pile", "Brick Pile"], -29, 20, -15, 50, 60, 94)

# ================================================================ the map
foliage = {"seed": 406, "surfaces": {"Tiles": 0.1, "Plain": 0.1}, "paths": t.paths, "regions": t.regions, "items": t.items, "exclude": t.exclude}
mp = {
    "format": "call-of-slop-map/1",
    "name": "Old Town",
    "description": "An overgrown small-town centre at dusk: Main Street's shops, diner and arcade, the square and its fountain, a park with a duck pond, a gas station, a car park and a rail crossing.",
    "author": "Call of Slop",
    "order": 406,
    "maxPlayers": 16,
    "sky": "#c98a62",
    "skybox": {"preset": "dusk", "sun": {"time": 18.6, "latitude": 42}, "clouds": {"coverage": 0.4, "speed": 5}, "stars": 0.6},
    "boundary": {"shape": "smooth", "points": [list(p) for p in CTRL], "noise": NOISE, "edge": "none", "groundMargin": 10, "barrierHeight": 3},
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": [{"op": "Ground", "color": [0.36, 0.37, 0.27]}] + t.ops,
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

# ================================================================ report and checks
for x, z in BLUE_SP + RED_SP + FFA_SP + [(h[0], h[1]) for h in HILLS] + FLAGS:
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
print(f"area {t.area():.0f} m2 (side {t.area() ** 0.5:.1f} m), blocks {blocks}, trees {len(t.items)}, meshes {t.meshes}, hand props {len(t.props)} (solid {solid}, cosmetic {cosm})")
print("props by category:", dict(sorted(cats.items())))
print("props by rule:", dict(sorted(t.notes.items())))
print("edges:", edge_counts, "clutter ops:", sum(1 for o in t.ops if o["op"] == "Clutter"), "clutter asked:", sum(o["count"] for o in t.ops if o["op"] == "Clutter"))
vis = [(a, b) for a in BLUE_SP for b in RED_SP if t.sees(a, b)]
print("blue-red spawn lines of sight:", len(vis))
fb = t.walk_field(BLUE_SP); fr = t.walk_field(RED_SP)
for i, (x, z, r) in enumerate(HILLS):
    b, rd = min(t.walk_to(fb, x + dx, z + dz) for dx, dz in ((0, 0), (0, 2.5), (0, -2.5), (2.5, 0), (-2.5, 0))), min(t.walk_to(fr, x + dx, z + dz) for dx, dz in ((0, 0), (0, 2.5), (0, -2.5), (2.5, 0), (-2.5, 0)))
    print(f"hill {i + 1} ({x},{z}): walk blue {b:.0f} m red {rd:.0f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
b, rd = t.walk_to(fb, *FLAGS[1]), t.walk_to(fr, *FLAGS[0])
print(f"enemy flag: blue {b:.0f} m red {rd:.0f} m")
if OUT_PNG:
    marks = [("dot", x, z, 0.8, (40, 90, 240)) for x, z in BLUE_SP] + [("dot", x, z, 0.8, (240, 50, 50)) for x, z in RED_SP] + [("dot", x, z, 0.8, (250, 250, 250)) for x, z in FFA_SP]
    marks += [("circle", x, z, r, (255, 230, 0)) for x, z, r in HILLS] + [("dot", x, z, 1.0, (255, 255, 255)) for x, z in FLAGS]
    marks += [("line", a, b, 0, (255, 0, 255)) for a, b in vis]
    t.preview(OUT_PNG, marks)
