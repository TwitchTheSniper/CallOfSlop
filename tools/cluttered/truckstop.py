#!/usr/bin/env python3
"""Generator for Maps/Cluttered/08-TruckStop.json (medium, 12 players): a desert highway truck stop at dusk.
Usage: python3 tools/cluttered/truckstop.py <repo root> [preview.png]

Built with townkit.py (the pack's shared placement rules) and the pack props from truckstop_props.py. The map is NOT
mirrored: Blue holds the motel in the west (a two-storey L with balconies over a courtyard and a drained pool: the high
ground), Red holds the truck lot in the east (rows of semis and trailers to weave between and climb, the tire shop with
a roof you can reach: cover and flanks). The diner, the water tower, the convenience store and the gas canopy are the
contested middle, the highway runs along the south edge. Fairness is measured instead of mirrored: the generator walks
a 0.5 m grid from each team's spawns to every hill and the enemy flag (and the self-test does the same on the game's
nav graph), checks that no Blue spawn sees a Red one, and counts the cover along each team's routes.
"""
import json, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import townkit as tk
from townkit import yaw_facing, yaw_along

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/08-TruckStop.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None

CTRL = [(-60, -8), (-58, -27), (-53, -35.5), (-40, -37), (-26, -37), (-12, -40.5), (8, -40.5), (24, -41), (41, -39.5), (50, -34), (58, -22), (60, -4), (59.5, 12),
        (55.5, 22), (46, 30.5), (34, 38.6), (10, 38.6), (-20, 38.8), (-36, 38.2), (-48, 32.5), (-56, 24), (-60, 10)]
NOISE = {"amplitude": 1.2, "scale": 16, "seed": 8, "octaves": 2}
t = tk.Town(REPO, CTRL, NOISE, pack_props=[os.path.join(REPO, "Maps/Cluttered/Props")], res=0.1)   # 10 cm grid: props can stand close to walls
rng = random.Random(408)


def _ij(x0, z0, x1, z1, eps=1e-6):
    """Town._ij with a little tolerance, so a wall face on a grid line doesn't spill into the next cell (float error)."""
    j0 = max(0, int(math.floor((x0 - t.x0) / t.res + eps))); j1 = min(t.nx, int(math.ceil((x1 - t.x0) / t.res - eps)))
    i0 = max(0, int(math.floor((z0 - t.z0) / t.res + eps))); i1 = min(t.nz, int(math.ceil((z1 - t.z0) / t.res - eps)))
    return i0, i1, j0, j1


t._ij = _ij
W0 = {"anchor": "world"}

# ---------------------------------------------------------------- palette
ASPHALT = (0.22, 0.22, 0.24); ASPHALT_B = (0.30, 0.29, 0.28); KERB = (0.62, 0.60, 0.56); PAVING = (0.58, 0.55, 0.50)
LOT = (0.33, 0.32, 0.31); GRAVEL = (0.55, 0.48, 0.38); WHITE = (0.88, 0.88, 0.84); YELLOW = (0.86, 0.70, 0.18); DARK = (0.10, 0.11, 0.13)
STUCCO = (0.86, 0.74, 0.58); STUCCO_T = (0.72, 0.42, 0.30); TEAL = (0.22, 0.50, 0.50); DOOR_C = (0.30, 0.45, 0.55)
DINER = (0.80, 0.82, 0.80); DINER_R = (0.75, 0.16, 0.14); STORE = (0.90, 0.88, 0.80); RED = (0.78, 0.16, 0.12)
GARAGE = (0.62, 0.62, 0.60); BLUE_G = (0.20, 0.36, 0.60); ROOF = (0.34, 0.32, 0.31); WOOD = (0.45, 0.32, 0.20)
GLASS = (0.30, 0.42, 0.48); CURTAIN = (0.62, 0.42, 0.30); POOLTILE = (0.45, 0.70, 0.78); MURK = (0.22, 0.34, 0.20)

WALL = 0.3
G_H = 3.2       # ground-floor walls
SLAB = 3.4      # top of the first-floor slab (balcony level)
UP_H = 6.5      # top of the upper walls
ROOF_T = 6.8    # motel roof


# ---------------------------------------------------------------- objectives (decided first: everything keeps clear)
BLUE_SP = [(-56.5, -22.0), (-55.5, -14.0), (-57.0, -6.5), (-55.5, 1.5), (-56.5, 9.0), (-54.5, 15.5), (-57.5, -17.5), (-55.0, -10.0)]
RED_SP = [(55.0, -15.5), (54.5, -7.0), (57.0, 0.5), (55.0, 8.0), (55.5, 14.0), (57.5, -10.0), (57.5, 4.0), (53.5, -19.0)]
FFA_SP = [(-32.0, 3.5), (-3.0, -38.5), (40.7, -2.0), (12.5, -4.0), (-12.0, 24.0), (2.0, 31.5)]
HILLS = [(2.0, 17.5, 4.5), (-0.5, -15.0, 4.5), (-8.0, -26.5, 3.0)]     # under the gas canopy, the diner lot, inside the diner
FLAGS = [(-57.5, 0.0), (57.5, -3.5)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.keepout.append((x, z, 2.6))
for x, z, r in HILLS:
    t.keepout.append((x, z, r + 0.6))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))


FAILED = []


def P(name, x, z, yaw=0.0, **kw):
    """t.prop that remembers hand-placed props that didn't fit (printed at the end)."""
    ok = t.prop(name, x, z, yaw, **kw)
    if not ok:
        r = t.foot(name, x, z, round(yaw % 360, 1))
        why = "outside" if not all(t.inside(cx, cz, kw.get("margin", 0.6)) for cx, cz in ((r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3]))) else \
            "keepout" if t.in_keepout(r, 0.3) else "occupied"
        FAILED.append((name, round(x, 1), round(z, 1), why))
    return ok


def against(name, axis, wall, s, inward, gap=0.12, **kw):
    """A prop with its back to a wall: axis "z" for a wall along X at z = wall (the prop at x = s), "x" for a wall along Z
    at x = wall (the prop at z = s); inward is +1 or -1, the side of the wall the prop stands on (it faces that way)."""
    d = t.P[name]["d"] / 2 + gap
    if axis == "z":
        return P(name, s, wall + inward * d, yaw_facing(0, inward), **kw)
    return P(name, wall + inward * d, s, yaw_facing(inward, 0), **kw)


# ---------------------------------------------------------------- flat things on the ground (meshes)
def slab(x0, z0, x1, z1, c, material="Concrete", h=0.04, y=0.0, **kw):
    t.mesh("box", (x0 + x1) / 2, (z0 + z1) / 2, c, y=y, width=round(abs(x1 - x0), 2), height=h, depth=round(abs(z1 - z0), 2), **({"material": material} if material else {}), **kw)


def dashes_x(x0, x1, z, c=WHITE, w=0.14, dash=3.0, gap=6.0):
    x = x0 + gap / 2
    while x + dash < x1:
        t.mesh("box", x + dash / 2, z, c, y=0.0, width=dash, height=0.045, depth=w)
        x += dash + gap


# the highway (two lanes, closed at both ends by road works), the north kerb and sidewalk, the far shoulder
HW = (29.5, 37.5)       # the highway's north and south edges; the sidewalk runs along z 27.1..29.5
slab(-75, HW[0], 75, HW[1], ASPHALT, "Concrete", h=0.03)
t.mesh("box", 0, 33.42, YELLOW, y=0.0, width=150.0, height=0.045, depth=0.12); t.mesh("box", 0, 33.58, YELLOW, y=0.0, width=150.0, height=0.045, depth=0.12)
t.mesh("box", 0, 30.0, WHITE, y=0.0, width=150.0, height=0.045, depth=0.12); t.mesh("box", 0, 37.0, WHITE, y=0.0, width=150.0, height=0.045, depth=0.12)
slab(-75, 27.1, 75, HW[0], PAVING, "Concrete", h=0.12)
slab(-75, HW[1], 75, 41.0, GRAVEL, "Dirt", h=0.03)
# lots: the motel car park, the diner lot, the forecourt (intact), the truck lot (busted asphalt: weeds in the cracks)
slab(-62, 13, -18, 27.1, LOT, None)
slab(-18, -21.5, 12, -7.0, LOT, None)
slab(-18, -7.0, 16, 27.1, (0.46, 0.45, 0.43), "Concrete", h=0.05)
slab(16, -24.5, 62, 27.1, (0.31, 0.30, 0.29), None)
slab(18, -26.0, 46, -24.5, (0.44, 0.43, 0.41), "Concrete", h=0.05)      # garage apron
# truck lot bay lines (north-south)
for x in (18.0, 29.2, 40.7, 52.0):
    for z0, z1 in ((-23.0, -4.0), (0.5, 19.5)):
        t.mesh("box", x, (z0 + z1) / 2, YELLOW, y=0.0, width=0.14, height=0.035, depth=z1 - z0)


# ---------------------------------------------------------------- walls with openings (any height band)
def wall_x(x0, x1, z0, z1, h, c, holes=(), y0=0.0):
    """A wall along X from y0 to y0 + h, with openings (centre, width, bottom, top) measured from y0."""
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


DOOR = lambda c, w=1.4: (c, w, 0.0, 2.3)
WIN = lambda c, w=1.6: (c, w, 1.0, 2.3)


def keep_door(side, cc, ww, xx_or_zz):
    """Keeps a doorway clear (1.1 m each side of the wall) of anything placed later."""
    if side in "NS":
        t.mark(t.occ, (cc - ww / 2, xx_or_zz - 1.1, cc + ww / 2, xx_or_zz + 1.1), 3)
    else:
        t.mark(t.occ, (xx_or_zz - 1.1, cc - ww / 2, xx_or_zz + 1.1, cc + ww / 2), 3)


def shell(x0, z0, x1, z1, front, c, holes, h=3.6, roof=ROOF, floor=(0.48, 0.45, 0.42), floor_mat="Tiles"):
    """A walk-in single-storey building: four walls with openings ({'N','S','E','W': [holes]}, centres in world
    coordinates along the wall), a roof slab and a floor. Returns the Room (furnishing frame)."""
    w = WALL
    for side, hs in holes.items():
        for (cc, ww, bot, top) in hs:
            if bot > 0.05:
                continue
            keep_door(side, cc, ww, z0 if side == "N" else z1 if side == "S" else x0 if side == "W" else x1)
    wall_x(x0, x1, z0, z0 + w, h, c, holes.get("N", ()))
    wall_x(x0, x1, z1 - w, z1, h, c, holes.get("S", ()))
    wall_z(z0 + w, z1 - w, x0, x0 + w, h, c, holes.get("W", ()))
    wall_z(z0 + w, z1 - w, x1 - w, x1, h, c, holes.get("E", ()))
    t.block(x0 - 0.15, h, z0 - 0.15, x1 + 0.15, h + 0.3, z1 + 0.15, roof)
    slab(x0 + w, z0 + w, x1 - w, z1 - w, floor, floor_mat, h=0.05)
    return tk.Room(t, x0, z0, x1, z1, front)


def glass_panels(holes, side, coord, y0=0.0):
    """Dark glass in window openings (cosmetic, so you can shoot and climb through)."""
    for (cc, ww, bot, top) in holes:
        if bot < 0.05:
            continue
        if side in "NS":
            t.mesh("box", cc, coord, GLASS, y=y0 + bot, width=ww, height=top - bot, depth=0.04)
        else:
            t.mesh("box", coord, cc, GLASS, y=y0 + bot, width=0.04, height=top - bot, depth=ww)


ROOMS = []          # (room, kind, door_u, back_door_u) furnished by townkit
WAYPOINTS = []      # extra nav points in tight rooms

# ================================================================ the motel (Blue, west): a two-storey L round a courtyard
# West wing: rooms x -53..-46 along z -24..12 (8 rooms of 4.5 m), doors onto a 2 m walkway (x -46..-44).
# North wing: rooms z -33..-26 along x -46..-22 (5 rooms of 4.8 m), walkway z -26..-24.
# The lobby fills the corner (x -53..-46, z -33..-24). Upstairs, the same rooms open onto balconies over the walkways.
MX0, MXF, MWALK = -53.0, -46.0, -44.0          # west wing: back wall x, front wall x, walkway edge
NZ0, NZF, NWALK = -33.0, -26.0, -24.0          # north wing
WZ0, WZ1, WROOM = -24.0, 12.0, 4.5
NX0, NX1, NROOM = -46.0, -22.0, 4.8
OPEN = {("W", 0): {0, 2, 3, 5, 7}, ("W", 1): {1, 3, 6}, ("N", 0): {0, 2, 4}, ("N", 1): {1, 3}}
BREEZEWAY = 4        # west wing, ground floor: room 4 is a breezeway right through the wing (back lot <-> walkway)
BZ = (WZ0 + BREEZEWAY * WROOM, WZ0 + (BREEZEWAY + 1) * WROOM)
MOTEL_ROOMS = []     # (wing, floor, k, Room, y)


def motel_wing(wing, floor):
    """One floor of one wing: front wall with a door and a window per open room (locked rooms get a painted door and
    curtained window), partitions round the open rooms, a bathroom partition in each open room."""
    y0 = 0.0 if floor == 0 else SLAB
    h = G_H if floor == 0 else UP_H - SLAB
    opened = OPEN[(wing, floor)]
    if wing == "W":
        n, a0, rw = 8, WZ0, WROOM
        holes = []
        walled = set(opened) | ({BREEZEWAY} if floor == 0 else set())
        for k in range(n):
            z0 = a0 + k * rw
            dz, wz = z0 + 1.2, z0 + 3.2
            if floor == 0 and k == BREEZEWAY:
                holes.append(((BZ[0] + BZ[1]) / 2, rw - 0.6, 0.0, 2.6))
                keep_door("E", (BZ[0] + BZ[1]) / 2, rw - 0.6, MXF)
                continue
            if k in opened:
                holes += [DOOR(dz, 1.0), WIN(wz, 1.4)]
                if floor == 0:
                    keep_door("E", dz, 1.0, MXF)
            else:
                t.mesh("box", MXF + 0.02, dz, DOOR_C, y=y0, width=0.06, height=2.2, depth=1.0)
                t.mesh("box", MXF + 0.02, wz, CURTAIN, y=y0 + 1.0, width=0.06, height=1.3, depth=1.4)
            t.mesh("box", MXF + 0.03, dz, (0.85, 0.75, 0.30), y=y0 + 2.35, width=0.05, height=0.14, depth=0.3)   # room number plate
        wall_z(a0, WZ1, MXF - WALL, MXF, h, STUCCO, holes, y0=y0)
        glass_panels(holes, "E", MXF - 0.15, y0)
        for k in range(n):
            z0 = a0 + k * rw
            # partition on the room's north side if this room or the one before is open (the lobby wall for room 0)
            if k > 0 and (k in walled or (k - 1) in walled):
                t.block(MX0 + WALL, y0, z0 - 0.1, MXF - WALL, y0 + h, z0 + 0.1, STUCCO)
            if k in opened:
                room = tk.Room(t, MX0, z0 + (0.0 if k == 0 else -0.2), MXF, z0 + rw + (0.0 if k == n - 1 else 0.2), (1, 0))
                # bathroom partition 3.9 m in from the front wall, door gap at the room's u = 0 side
                bx = MXF - WALL - 3.9
                t.block(bx - 0.12, y0, z0 + 0.3 + 1.4, bx, y0 + h, z0 + rw - 0.1, (0.92, 0.90, 0.84))
                MOTEL_ROOMS.append((wing, floor, k, room, y0))
        if floor == 0:
            t.block(MX0 + WALL, 0, WZ0 - 0.1, MXF - WALL, h, WZ0 + 0.1, STUCCO)      # lobby / room 0 wall
        else:
            t.block(MX0 + WALL, y0, WZ0 - 0.1, MXF - WALL, y0 + h, WZ0 + 0.1, STUCCO)
    else:
        n, a1, rw = 5, NX1, NROOM
        holes = []
        for k in range(n):
            x1 = a1 - k * rw
            dx, wx = x1 - 1.2, x1 - 3.2
            if k in opened:
                holes += [DOOR(dx, 1.0), WIN(wx, 1.4)]
                if floor == 0:
                    keep_door("S", dx, 1.0, NZF)
            else:
                t.mesh("box", dx, NZF + 0.02, DOOR_C, y=y0, width=1.0, height=2.2, depth=0.06)
                t.mesh("box", wx, NZF + 0.02, CURTAIN, y=y0 + 1.0, width=1.4, height=1.3, depth=0.06)
            t.mesh("box", dx, NZF + 0.03, (0.85, 0.75, 0.30), y=y0 + 2.35, width=0.3, height=0.14, depth=0.05)
        wall_x(NX0, NX1, NZF - WALL, NZF, h, STUCCO, holes, y0=y0)
        glass_panels(holes, "S", NZF - 0.15, y0)
        for k in range(n):
            x1 = a1 - k * rw
            if k > 0 and (k in opened or (k - 1) in opened):
                t.block(x1 - 0.1, y0, NZ0 + WALL, x1 + 0.1, y0 + h, NZF - WALL, STUCCO)
            if k in opened:
                room = tk.Room(t, x1 - rw - (0.2 if k < n - 1 else 0.0), NZ0, x1 + (0.0 if k == 0 else 0.2), NZF, (0, 1))
                bz = NZF - WALL - 3.9
                t.block(x1 - rw + 0.1, y0, bz - 0.12, x1 - 0.3 - 1.4, y0 + h, bz, (0.92, 0.90, 0.84))
                MOTEL_ROOMS.append((wing, floor, k, room, y0))


# outer walls: the long back walls (both floors in one block), the wing ends
# west back wall, both floors: a back door into the lobby (z -29) and the breezeway's opening
LOBBY_BACK = -29.0
for (z0, z1) in ((NZ0, LOBBY_BACK - 0.65), (LOBBY_BACK + 0.65, BZ[0] + 0.3), (BZ[1] - 0.3, WZ1)):
    t.block(MX0, 0, z0, MX0 + WALL, UP_H, z1, STUCCO_T)
t.block(MX0, 2.3, LOBBY_BACK - 0.65, MX0 + WALL, UP_H, LOBBY_BACK + 0.65, STUCCO_T)
t.block(MX0, 2.6, BZ[0] + 0.3, MX0 + WALL, UP_H, BZ[1] - 0.3, STUCCO_T)
keep_door("W", LOBBY_BACK, 1.3, MX0); keep_door("W", (BZ[0] + BZ[1]) / 2, WROOM - 0.6, MX0)
slab(MX0 + WALL, BZ[0] + 0.1, MXF - WALL, BZ[1] - 0.1, PAVING, "Concrete", h=0.08)
t.block(MX0 + WALL, 0, NZ0, NX1, UP_H, NZ0 + WALL, STUCCO_T)             # north back wall
t.block(MX0 + WALL, 0, WZ1 - WALL, MXF, UP_H, WZ1, STUCCO_T)             # west wing's south end
t.block(NX1 - WALL, 0, NZ0 + WALL, NX1, UP_H, NZF, STUCCO_T)             # north wing's east end
# lobby: east wall (z -33..-24, door onto the walkway corner at z -25), back door to the back lot at z -29
LOBBY_E = [DOOR(-25.0, 1.3), WIN(-29.5, 2.4)]
wall_z(NZ0 + WALL, WZ0 - 0.1, MXF - WALL, MXF, G_H, STUCCO, LOBBY_E)
glass_panels(LOBBY_E, "E", MXF - 0.15)
keep_door("E", -25.0, 1.3, MXF)
for wing in ("W", "N"):
    for fl in (0, 1):
        motel_wing(wing, fl)
# upstairs: the corner above the lobby is a closed linen room (its east wall)
t.block(MXF - WALL, SLAB, NZ0 + WALL, MXF, UP_H, WZ0 - 0.1, STUCCO)
# first-floor slabs (the balconies over the walkways are part of them), the roof, rails, posts and two staircases
t.block(MX0, G_H, NZ0, MWALK, SLAB, WZ1, (0.60, 0.58, 0.54))
t.block(MWALK, G_H, NZ0, NX1, SLAB, NWALK, (0.60, 0.58, 0.54))
t.block(MX0 - 0.2, UP_H, NZ0 - 0.2, MXF + 0.4, ROOF_T, WZ1 + 0.2, ROOF)
t.block(MXF + 0.4, UP_H, NZ0 - 0.2, NX1 + 0.2, ROOF_T, NZF + 0.4, ROOF)
t.block(MWALK - 0.1, SLAB, WZ0 - 0.1, MWALK, SLAB + 1.0, WZ1, (0.30, 0.30, 0.32))           # west balcony rail
t.block(MWALK, SLAB, NWALK - 0.1, NX1, SLAB + 1.0, NWALK, (0.30, 0.30, 0.32))              # north balcony rail
for z in [WZ0 + k * WROOM for k in range(9)]:
    t.mesh("cylinder", MWALK - 0.06, z, WHITE, y=0.0, width=0.14, height=G_H, depth=0.14, segments=8)
for x in [NX0 + 2.0 + k * NROOM for k in range(5)] + [NX1]:
    t.mesh("cylinder", x, NWALK - 0.06, WHITE, y=0.0, width=0.14, height=G_H, depth=0.14, segments=8)
# a turquoise fascia along the balcony edges and the roofline (cosmetic)
t.mesh("box", MWALK - 0.02, (WZ0 + WZ1) / 2, TEAL, y=G_H - 0.1, width=0.12, height=0.4, depth=WZ1 - WZ0)
t.mesh("box", (MWALK + NX1) / 2, NWALK - 0.02, TEAL, y=G_H - 0.1, width=NX1 - MWALK, height=0.4, depth=0.12)
t.mesh("box", MXF + 0.42, (NZ0 + WZ1) / 2, TEAL, y=UP_H, width=0.1, height=0.45, depth=WZ1 - NZ0 + 0.4)
t.mesh("box", (MXF + NX1) / 2, NZF + 0.42, TEAL, y=UP_H, width=NX1 - MXF, height=0.45, depth=0.1)
STAIRS = []
t.ops.append({"op": "StairsZ", "x0": -45.9, "x1": -44.5, "z": 16.1, "dir": -1, "y0": 0, "height": SLAB, "color": [0.55, 0.54, 0.5]})
STAIRS.append((-45.9, 12.0, -44.5, 16.1))
t.ops.append({"op": "StairsX", "z0": -25.9, "z1": -24.5, "x": -17.9, "dir": -1, "y0": 0, "height": SLAB, "color": [0.55, 0.54, 0.5]})
STAIRS.append((-22.0, -25.9, -17.9, -24.5))
for r in STAIRS:
    t.mark(t.occ, r); t.mark(t.walk, (r[0], r[1], r[2], r[3])); t.mark(t.tall, r)
    t.boxes.append((r[0], r[1], r[2], r[3], 0, SLAB, (0.55, 0.54, 0.5)))
# cosmetic windows on the back walls (curtains)
for z in [WZ0 + 2.25 + k * WROOM for k in range(8)]:
    for y in ((4.6,) if abs(z - (BZ[0] + BZ[1]) / 2) < 1 else (1.2, 4.6)):
        t.mesh("box", MX0 - 0.03, z, CURTAIN, y=y, width=0.06, height=0.9, depth=0.8)
for x in [NX1 - 2.4 - k * NROOM for k in range(5)]:
    for y in (1.2, 4.6):
        t.mesh("box", x, NZ0 - 0.03, CURTAIN, y=y, width=0.8, height=0.9, depth=0.06)
slab(MX0 + WALL, NZ0 + WALL, MXF - WALL, WZ0 - 0.1, (0.55, 0.42, 0.32), "Tiles", h=0.05)        # lobby floor
slab(MXF, NZ0 + WALL, MWALK, WZ1, PAVING, "Concrete", h=0.08)                                   # walkways
slab(MWALK, NZF, NX1, NWALK, PAVING, "Concrete", h=0.08)
LOBBY = tk.Room(t, MX0, NZ0, MXF, WZ0, (1, 0))

# ---------------------------------------------------------------- the courtyard: a drained pool, deck, loungers, lawn
POOL = (-38.0, -14.0, -27.0, -8.0)         # pit: floor at -1.0, deep end (x > -31) at -1.8
DECK = (-41.5, -17.0, -24.0, -5.0)
slab(DECK[0], DECK[1], DECK[2], POOL[1], PAVING, "Concrete", h=0.06, **W0)
slab(DECK[0], POOL[3], DECK[2], DECK[3], PAVING, "Concrete", h=0.06, **W0)
slab(DECK[0], POOL[1], POOL[0], POOL[3], PAVING, "Concrete", h=0.06, **W0)
slab(POOL[2], POOL[1], DECK[2], POOL[3], PAVING, "Concrete", h=0.06, **W0)
for (x0, z0, x1, z1) in ((POOL[0] - 0.3, POOL[1] - 0.3, POOL[2] + 0.3, POOL[1]), (POOL[0] - 0.3, POOL[3], POOL[2] + 0.3, POOL[3] + 0.3),
                         (POOL[0] - 0.3, POOL[1], POOL[0], POOL[3]), (POOL[2], POOL[1], POOL[2] + 0.3, POOL[3])):
    slab(x0, z0, x1, z1, WHITE, "Concrete", h=0.1, **W0)                                          # coping
# tiled walls and floor inside the pit (cosmetic), a green puddle in the deep end
for (cx, cz, w, d) in (((POOL[0] + POOL[2]) / 2, POOL[1] + 0.02, POOL[2] - POOL[0], 0.04), ((POOL[0] + POOL[2]) / 2, POOL[3] - 0.02, POOL[2] - POOL[0], 0.04),
                       (POOL[0] + 0.02, (POOL[1] + POOL[3]) / 2, 0.04, POOL[3] - POOL[1]), (POOL[2] - 0.02, (POOL[1] + POOL[3]) / 2, 0.04, POOL[3] - POOL[1])):
    t.mesh("box", cx, cz, POOLTILE, y=-1.8, width=w, height=1.8, depth=d, material="Tiles", **W0)
slab(POOL[0], POOL[1], -31.0, POOL[3], POOLTILE, "Tiles", h=0.03, y=-1.0, **W0)
slab(-31.0, POOL[1], POOL[2], POOL[3], POOLTILE, "Tiles", h=0.03, y=-1.8, **W0)
slab(-30.2, -13.4, -27.4, -9.0, MURK, None, h=0.05, y=-1.79, **W0)
# steps: one along the shallow end wall, one at the deep end's drop (blocks inside the pit, world heights)
t.ops.append({"op": "Block", "x0": POOL[0], "y0": -1.0, "z0": -13.9, "x1": POOL[0] + 0.8, "y1": -0.5, "z1": -11.6, "color": [0.80, 0.82, 0.80], "anchor": "world"})
t.ops.append({"op": "Block", "x0": -31.8, "y0": -1.8, "z0": -13.9, "x1": -31.0, "y1": -1.4, "z1": -8.1, "color": [0.80, 0.82, 0.80], "anchor": "world"})
t.mark(t.occ, (POOL[0] - 0.5, POOL[1] - 0.5, POOL[2] + 0.5, POOL[3] + 0.5))
for (lx, lz, ly, lyaw) in ((POOL[2] - 0.2, -12.5, -1.75, 90), (POOL[0] + 0.2, -9.0, -0.95, 270)):
    t.ops.append({"op": "Prop", "name": "Pool Ladder", "x": lx, "z": lz, "yaw": lyaw, "y": ly, "anchor": "world"})
    t.props.append(("Pool Ladder", lx, lz, lyaw, ly, "pool"))
t.ops.append({"op": "Prop", "name": "Rubber Duck", "x": -28.6, "z": -11.4, "yaw": 40, "y": -1.74, "anchor": "world"})
t.ops.append({"op": "Prop", "name": "Sports Balls", "x": -30.0, "z": -10.0, "y": -1.79, "anchor": "world"})
t.props.append(("Rubber Duck", -28.6, -11.4, 40, -1.74, "pool")); t.props.append(("Sports Balls", -30.0, -10.0, 0, -1.79, "pool"))
for (x, z, yy) in ((-36.5, -16.0, 180), (-34.0, -16.0, 180), (-31.5, -16.0, 180), (-28.5, -16.2, 170), (-36.0, -6.0, 0), (-33.2, -6.0, 0), (-25.4, -11.0, 270), (-25.4, -8.4, 270)):
    P("Sun Lounger", x, z, yy + rng.uniform(-6, 6), tag="pool")
for (x, z) in ((-40.0, -16.0), (-26.0, -16.0), (-40.2, -6.2)):
    P("Patio Table", x, z, rng.uniform(0, 90), tag="pool")
for (x, z) in ((-41.0, -14.0), (-41.0, -9.0), (-27.0, -6.3)):
    P("Patio Chair", x, z, rng.uniform(0, 360), tag="pool")
P("Lawn Sprinkler", -37.0, 4.0, 0, tag="pool"); P("Gas BBQ", -42.6, -2.0, 90, tag="pool")
P("Planter", -42.8, 6.5, 0, tag="pool"); P("Planter", -23.5, -21.5, 0, tag="pool"); P("Planter", -42.8, -20.5, 0, tag="pool")
P("Large Flower Pot", -24.2, -4.0, 0, tag="pool"); P("Large Flower Pot", -41.8, -17.6, 0, tag="pool")
P("Hammock", -27.0, 2.0, 90, tag="pool"); P("Laundry Line", -33.5, -21.0, 0, tag="pool")


# ================================================================ the diner (north middle)
DN = (-14.0, -36.0, 6.0, -22.0)
DN_S = [DOOR(-4.0, 1.8), WIN(-10.4, 4.6), WIN(1.8, 4.6)]
r = shell(*DN, (0, 1), DINER, {"S": DN_S, "N": [DOOR(-8.0, 1.2)], "E": [DOOR(-27.5, 1.4), WIN(-32.5, 2.0)], "W": [WIN(-27.0, 2.6)]}, h=3.8, roof=DINER_R,
          floor=(0.85, 0.85, 0.82), floor_mat="Tiles")
glass_panels(DN_S, "S", DN[3] - 0.15); glass_panels([WIN(-32.5, 2.0)], "E", DN[2] - 0.15); glass_panels([WIN(-27.0, 2.6)], "W", DN[0] + 0.15)
DINER_ROOM = r
t.mesh("box", (DN[0] + DN[2]) / 2, DN[3] + 0.02, DINER_R, y=0.55, width=DN[2] - DN[0], height=0.3, depth=0.06)   # red band
t.mesh("box", (DN[0] + DN[2]) / 2, DN[3] + 0.6, (0.85, 0.85, 0.85), y=3.2, width=DN[2] - DN[0] + 0.6, height=0.18, depth=1.4)  # canopy over the windows
P("Neon Open Sign", -6.5, -29.0, 0, y=4.1, check=False, tag="sign")
P("Rotating Diner Sign", -15.0, -21.0, 0, tag="sign")
P("Neon Sign", -10.4, -21.95, 0, y=2.7, check=False, tag="sign"); P("Neon Sign", 1.8, -21.95, 0, y=2.7, check=False, tag="sign")
# booths along the front windows, a counter with stools, the kitchen at the back
for k in range(10):
    bx = -12.6 + k * 1.85
    if abs(bx - (-4.0)) < 1.9 or -11.0 < bx < -5.0:     # the front door, and the hill zone by the counter
        continue
    P("Diner Booth", bx, -23.65, 0, tag="interior")
    P("Table Lamp", bx, -23.65, 0, y=0.78, check=False, tag="interior")
for k in range(10):
    cx = -12.6 + k * 1.4
    if -11.0 < cx < -5.2:          # a gap in the counter (the way into the kitchen) by the hill
        continue
    P("Kitchen Counter", cx, -30.2, 0, tag="interior")
    P("Bar Stool", cx, -29.1, 0, tag="interior")
    if k % 2 == 0:
        P(rng.choice(["Stacked Plates", "Coffee Maker", "Toaster", "Knife Block"]), cx, -30.3, 180, y=0.92, check=False, tag="interior")
KIT = DN[1] + WALL
for nm, x in (("Fridge", -12.9), ("Cooker", -11.9), ("Cooker", -11.1), ("Kitchen Sink", -10.1), ("Dishwasher", -6.0), ("Chest Freezer", -4.3),
              ("Fridge", -2.9), ("Metal Shelving", -1.4), ("Metal Shelving", 0.0), ("Fridge", 4.9)):
    against(nm, "z", KIT, x, 1, tag="interior")
P("Cooking Pot", -11.9, KIT + 0.45, 0, y=1.0, check=False, tag="interior"); P("Frying Pan", -11.1, KIT + 0.45, 0, y=1.0, check=False, tag="interior")
against("Flickering TV", "x", DN[2] - WALL, -24.3, -1, tag="interior"); against("Glowing Vending Machine", "x", DN[2] - WALL, -33.8, -1, tag="interior")
against("Grandfather Clock", "x", DN[0] + WALL, -32.6, 1, tag="interior"); against("Arcade Cabinet", "x", DN[0] + WALL, -24.6, 1, tag="interior")
against("Arcade Cabinet", "x", DN[0] + WALL, -25.8, 1, tag="interior")
for x in (-9.0, -1.0, 3.5):
    P("Ceiling Fan", x, -26.5, 0, y=3.0, check=False, tag="interior")
WAYPOINTS += [(-4.0, -27.0), (2.0, -27.0), (-10.0, -27.0), (-5.0, -33.0)]
# dumpsters and bins behind the diner, by its back door
tk.bins_by_door(t, (-8.0, -36.0), (1, 0), (0, -1), rng, side=-1, seed=210)
tk.bins_by_door(t, (-8.0, -36.0), (1, 0), (0, -1), rng, side=1, dumpster=True, seed=211)
for (x, nm) in ((1.5, "Gas Cylinders"), (3.0, "Pallet Stack"), (-13.0, "Cooling Unit"), (4.8, "Oil Drum")):
    against(nm, "z", DN[1], x, -1, tag="lane")

# ================================================================ the convenience store and the gas canopy (south middle)
ST = (-9.0, -4.0, 7.0, 6.0)
ST_S = [DOOR(-1.0, 1.8), WIN(-5.8, 4.4), WIN(3.6, 4.4)]
r = shell(*ST, (0, 1), STORE, {"S": ST_S, "N": [DOOR(4.0, 1.2)], "W": [DOOR(1.0, 1.3)], "E": [WIN(1.0, 2.6)]}, h=3.8, roof=RED)
glass_panels(ST_S, "S", ST[3] - 0.15); glass_panels([WIN(1.0, 2.6)], "E", ST[2] - 0.15)
ROOMS.append((r, "store", (ST[2] - WALL) - (-1.0), (ST[2] - WALL) - 4.0))
t.mesh("box", (ST[0] + ST[2]) / 2, ST[3] + 0.02, RED, y=3.0, width=ST[2] - ST[0], height=0.8, depth=0.08)
P("Shop Sign", -1.0, 6.1, 0, y=3.05, check=False, tag="sign"); P("Neon Sign", 3.6, 5.95, 0, y=2.6, check=False, tag="sign")
P("Glowing Vending Machine", 7.6, 4.8, 90, tag="gas"); P("Ice Machine", 7.6, 3.4, 90, tag="gas")
P("Payphone", 7.4, -2.5, 90, tag="gas"); P("Newspaper Box", -9.5, 5.2, 270, tag="gas")
tk.bins_by_door(t, (4.0, -4.0), (1, 0), (0, -1), rng, side=-1, seed=220)
# canopy over two pump islands, the hill in the middle between them
CAN = (-9.5, 12.5, 15.5, 22.5)
CX = (CAN[0] + CAN[2]) / 2
for px in (CX - 8.5, CX + 8.5):
    t.block(px - 0.3, 0, 17.2, px + 0.3, 5.0, 17.8, WHITE)
t.mesh("box", CX, 17.5, WHITE, y=5.0, width=CAN[2] - CAN[0], height=0.6, depth=CAN[3] - CAN[1])
t.mesh("box", CX, 17.5, RED, y=5.2, width=CAN[2] - CAN[0] + 0.2, height=0.3, depth=CAN[3] - CAN[1] + 0.2)
for iz in (15.0, 20.0):
    for x0, x1 in ((CX - 11.5, CX - 5.5), (CX + 5.5, CX + 11.5)):
        t.mesh("box", (x0 + x1) / 2, iz, KERB, y=0.0, width=x1 - x0, height=0.18, depth=1.3, material="Concrete")
    for px in (CX - 10.0, CX - 7.0, CX + 7.0, CX + 10.0):
        P("Old Town Fuel Pump", px, iz, 0, tag="gas")
for xx, zz in ((CX - 8.5, 12.9), (CX + 8.5, 22.2)):
    P(rng.choice(["Sedan", "Hatchback", "Pickup Truck", "SUV"]), xx, zz, 0, tag="gas")
P("Pylon Sign", 14.5, 28.3, 180, tag="gas")
P("Trash Can", -10.6, 15.0, 0, tag="gas"); P("Trash Can", 15.4, 20.0, 0, tag="gas")
P("Gas Cylinders", -11.5, 5.0, 0, tag="gas"); P("Gas Cylinders", -11.5, 3.8, 0, tag="gas")

# ================================================================ the water tower (between the diner and the tire shop)
WT = (13.5, -34.5)
for dx in (-2.2, 2.2):
    for dz in (-2.2, 2.2):
        t.block(WT[0] + dx - 0.2, 0, WT[1] + dz - 0.2, WT[0] + dx + 0.2, 10.0, WT[1] + dz + 0.2, (0.45, 0.45, 0.46))
for y in (3.0, 6.5):
    for (cx, cz, w, d) in ((WT[0], WT[1] - 2.2, 4.4, 0.1), (WT[0], WT[1] + 2.2, 4.4, 0.1), (WT[0] - 2.2, WT[1], 0.1, 4.4), (WT[0] + 2.2, WT[1], 0.1, 4.4)):
        t.mesh("box", cx, cz, (0.42, 0.42, 0.44), y=y, width=w, height=0.12, depth=d, material="Metal")
t.mesh("cylinder", WT[0], WT[1], (0.78, 0.80, 0.80), y=10.0, width=6.4, height=4.6, depth=6.4, segments=24, material="Metal")
t.mesh("cone", WT[0], WT[1], (0.70, 0.72, 0.72), y=14.6, width=6.8, height=1.6, depth=6.8, segments=24, material="Metal")
t.mesh("torus", WT[0], WT[1], (0.35, 0.35, 0.36), y=10.1, radius=3.4, inner=0.06, segments=32, detail=6)
t.mesh("box", WT[0], WT[1] + 3.22, (0.80, 0.30, 0.20), y=11.6, width=4.2, height=1.4, depth=0.05)        # painted band
t.mesh("box", WT[0] + 2.35, WT[1], (0.35, 0.35, 0.36), y=0.0, width=0.08, height=10.0, depth=0.5)        # ladder
P("Pad Transformer", 10.0, -31.0, 0, tag="yard"); P("Junction Box", 17.0, -31.2, 0, tag="yard")
P("Portable Generator", 9.5, -36.6, 90, tag="yard")

# ================================================================ the tire shop (Red side, north-east): three bays and an office
GR = (20.0, -38.0, 44.0, -26.0)
GR_H = 4.2
GR_S = [(27.5, 5.0, 0.0, 3.8), (33.5, 5.0, 0.0, 3.8), (39.5, 5.0, 0.0, 3.8), DOOR(22.4, 1.2), WIN(24.0, 1.4)]
r = shell(*GR, (0, 1), GARAGE, {"S": GR_S, "E": [DOOR(-36.0, 1.4)], "N": [DOOR(38.5, 1.2)]}, h=GR_H, roof=ROOF, floor=(0.46, 0.46, 0.46), floor_mat="Concrete")
glass_panels([WIN(24.0, 1.4)], "S", GR[3] - 0.15)
wall_z(GR[1] + WALL, GR[3] - WALL, 24.8, 25.0, GR_H, GARAGE, [DOOR(-29.0, 1.2)])                       # office partition
for x in (27.5, 33.5, 39.5):
    t.mesh("box", x, GR[3] + 0.02, (0.55, 0.55, 0.56), y=3.8, width=5.0, height=0.7, depth=0.12, material="Metal")   # rolled-up doors
    t.mesh("box", x, GR[3] + 0.08, BLUE_G, y=GR_H - 0.55, width=5.4, height=0.5, depth=0.08)
t.mesh("box", 33.5, GR[3] + 0.06, WHITE, y=GR_H + 0.3, width=12.0, height=1.2, depth=0.1)               # TIRES sign
t.mesh("box", 33.5, GR[3] + 0.1, (0.15, 0.25, 0.55), y=GR_H + 0.5, width=10.4, height=0.8, depth=0.05)
P("Car Lift Raised", 27.5, -31.8, 90, tag="garage"); P("Car Lift", 33.5, -31.8, 90, tag="garage")
P("Tire Changer", 39.0, -35.8, 0, tag="garage"); P("Air Compressor", 42.6, -29.5, 90, tag="garage")
GN = GR[1] + WALL
for nm, x in (("Tool Chest", 26.2), ("Portable Generator", 30.0), ("Tire Rack", 32.6), ("Metal Shelving", 35.0), ("Tool Chest", 36.8), ("Tire Rack", 40.6)):
    against(nm, "z", GN, x, 1, tag="garage")
P("Engine Block", 41.6, -33.6, 30, tag="garage"); against("Tyre Stack", "x", GR[2] - WALL, -34.4, -1, tag="garage"); against("Tyre Stack", "x", GR[2] - WALL, -30.4, -1, tag="garage")
P("Explosive Barrel", 39.0, -29.2, 0, tag="garage"); P("Explosive Barrel", 42.6, -37.3, 0, tag="garage"); P("Oil Drum", 40.0, -29.0, 0, tag="garage")
P("Pedestal Fan", 26.4, -27.4, 180, tag="garage")
for x in (28.0, 38.0):
    P("Ceiling Fan", x, -32.0, 0, y=3.8, check=False, tag="interior")
OFFICE = tk.Room(t, GR[0], GR[1], 25.0, GR[3], (0, 1))
for nm, u, v, f in (("Office Desk", 2.2, 2.4, "front"), ("Office Chair", 2.2, 3.2, "front"), ("Filing Cabinet", 4.1, 5.0, "left"),
                    ("Water Cooler", 0.4, 6.0, "right"), ("Metal Lockers", 0.4, 9.0, "right"), ("Archive Boxes", 3.6, 10.8, "back"), ("Safe", 4.0, 8.0, "left")):
    OFFICE.put(nm, u, v, f, rng=rng, tag="interior")
for nm, u, v in (("Computer Monitor", 2.0, 2.4), ("Desk Phone", 2.6, 2.3)):
    OFFICE.put(nm, u, v, "front", y=0.76, rng=rng, tag="interior")
WAYPOINTS += [(22.5, -29.0), (22.5, -35.0), (30.5, -34.0), (36.5, -34.0), (42.0, -32.0)]
# fire escape up the east wall to the roof (high ground over the truck lot), a landing at 3 m, a 1.5 m mantle up
P("Fire Escape Stairs", 45.0, -32.2, 270, check=False, tag="lane"); P("Fire Escape Landing", 44.9, -28.6, 270, check=False, tag="lane")
for rr in ((44.0, -34.1, 46.0, -27.3),):
    t.mark(t.occ, rr); t.mark(t.walk, rr)
tk.bins_by_door(t, (44.0, -36.0), (0, 1), (1, 0), rng, side=-1, dumpster=False, seed=230)
tk.bins_by_door(t, (38.5, -38.0), (1, 0), (0, -1), rng, side=-1, seed=231)


# ================================================================ the truck lot (Red, east): semis and trailers in bays
def semi(x, zc, front, trailer, tractor=None):
    """A trailer standing north-south centred on (x, zc), its kingpin end towards front (-1 north, +1 south), and
    optionally its tractor coupled in front of it."""
    yaw = 90 if front < 0 else 270
    ok = P(trailer, x, zc, yaw, gap=0.1, tag="trucks")
    if ok and tractor:
        P(tractor, x, zc + front * 7.9, yaw, check=False, tag="trucks")
    return ok


TRACTORS = ["Semi Tractor Red", "Semi Tractor Blue", "Semi Tractor White"]
LOTSPEC = [  # (x, trailer centre z, front, trailer, coupled?): three double rows with aisles at x 29.2, 40.7 and 52
    (21.5, -16.5, 1, "Box Trailer", True), (25.9, -14.0, 1, "Reefer Trailer", False),
    (32.5, -17.0, 1, "Flatbed Lumber", True), (36.9, -15.5, 1, "Box Trailer Rusty", False),
    (44.0, -16.0, 1, "Box Trailer", True), (48.4, -13.0, 1, "Flatbed Machinery", False),
    (21.5, 12.5, -1, "Flatbed Pipes", False), (25.9, 13.5, -1, "Box Trailer", True),
    (32.5, 11.0, -1, "Reefer Trailer", True), (36.9, 13.4, -1, "Flatbed Lumber", False),
    (44.0, 13.4, -1, "Box Trailer Rusty", True), (48.4, 10.5, -1, "Box Trailer", False),
]
for k, (x, zc, front, trailer, coupled) in enumerate(LOTSPEC):
    if not semi(x, zc, front, trailer, TRACTORS[k % 3] if coupled else None):
        print("WARN trailer not placed", trailer, x, zc)
# crate steps up onto two box trailers' roofs (3.76 m): 1.2 m, then 2.5 m, then a 1.3 m mantle
CRATE_STEPS = [((18.2, -15.0, 19.2, -13.6), (19.2, -15.0, 20.2, -13.6)), ((50.7, 8.6, 51.7, 10.0), (49.7, 8.6, 50.7, 10.0))]
for (lo, hi) in CRATE_STEPS:
    t.block(lo[0], 0, lo[1], lo[2], 1.2, lo[3], (0.62, 0.48, 0.30))
    t.block(hi[0], 0, hi[1], hi[2], 2.5, hi[3], (0.58, 0.44, 0.28))
# dressing between the rows: pallets, drums, tires, a forklift, a floodlight tower; explosive barrels kept off the spawns
for (nm, x, z, yy) in (("Forklift", 35.6, -3.0, 0), ("Pallet Stack", 22.0, -2.0, 0), ("Pallet Stack", 22.0, -0.7, 10), ("Wooden Crate", 25.2, -1.0, 20),
                       ("Tyre Stack", 39.2, 4.0, 0), ("Tyre Stack", 39.9, 4.8, 0), ("Oil Drum", 45.6, -2.6, 0), ("Rusty Drum", 46.4, -1.6, 0),
                       ("Explosive Barrel", 23.6, -3.4, 0), ("Explosive Barrel", 29.2, -21.0, 0), ("Cable Spool", 18.8, -4.5, 0), ("Utility Trailer", 50.8, -9.5, 90),
                       ("Floodlight Tower", 40.7, 18.6, 90), ("Portable Toilet", 48.0, -22.4, 0), ("Portable Toilet", 49.4, -22.4, 0),
                       ("Pallet", 50.6, 3.0, 30), ("Concrete Block", 51.0, -1.2, 0), ("Concrete Block", 51.8, -12.6, 90), ("Water Barrier", 51.5, 12.6, 90),
                       ("Tyre Stack", 51.5, -4.6, 0), ("Pickup Truck", 52.0, 20.0, 90), ("Wooden Crate", 52.0, 5.5, 0),
                       ("Gas Cylinders", 18.2, 2.0, 0)):
    if not P(nm, x, z, yy, gap=0.1, tag="trucks"):
        print("WARN lot prop not placed", nm, x, z)
# the truck fuel island under its own canopy, by the highway, a semi filling up
TC = (20.0, 20.4, 46.0, 26.8)
for px in (21.0, 45.0):
    t.block(px - 0.3, 0, 23.3, px + 0.3, 5.4, 23.9, WHITE)
t.mesh("box", 33.0, 23.6, WHITE, y=5.4, width=TC[2] - TC[0], height=0.5, depth=TC[3] - TC[1])
t.mesh("box", 33.0, 23.6, (0.20, 0.50, 0.25), y=5.55, width=TC[2] - TC[0] + 0.2, height=0.25, depth=TC[3] - TC[1] + 0.2)
t.mesh("box", 33.0, 23.6, KERB, y=0.0, width=22.0, height=0.18, depth=1.2, material="Concrete")
for px in (24.5, 30.0, 36.0, 41.5):
    P("Diesel Dispenser", px, 23.6, 0, tag="gas")
P("Box Trailer", 31.0, 21.6, 0, gap=0.05, tag="trucks"); P("Semi Tractor Blue", 38.9, 21.6, 0, check=False, tag="trucks")


# ================================================================ the motel car park, its sign; the highway
tk.car_park(t, -55.0, 16.5, -26.0, 21.5, rng, along_x=True, fill=0.7, bay_d=5.0, aisle=6.0,
            cars=("Sedan", "Hatchback", "SUV", "Van", "Pickup Truck", "Burnt Out Car", "Car On Bricks"), tag="carpark")
tk.car_park(t, -16.0, -21.4, 10.0, -16.6, rng, along_x=True, fill=0.65, bay_d=4.8,
            cars=("Sedan", "Hatchback", "Pickup Truck", "Police Car", "Taxi", "SUV"), tag="carpark")
P("Motel Neon Sign", -36.0, 26.3, 0, tag="sign")
P("Billboard Motel", -23.5, 28.4, 0, tag="sign"); P("Billboard Diner", 21.5, 28.4, 0, tag="sign")
# street lamps along the sidewalk (arms over the road), every third one flickering
for k, x in enumerate((-46, -31, -16, -1, 11, 29, 44)):
    P("Flickering Street Lamp" if k % 3 == 1 else "Street Lamp", x, 27.9, yaw_along(0, 1), tag="lamp")
# the highway is closed at both ends (it leaves the map there); a jack-knifed semi and a crash break the long line down it
for x0, zs in ((-40.0, (30.6, 32.8, 35.0)), (36.5, (30.6, 32.8, 35.0))):
    for dz in zs:
        P("Water Barrier", x0, dz, 90, margin=0.3, tag="edge")
P("Road Works Sign", -37.5, 31.2, 90, tag="edge"); P("Arrow Board", -36.2, 34.0, 90, tag="edge")
P("Road Works Sign", 34.0, 31.2, 270, tag="edge"); P("Arrow Board", 32.8, 34.6, 270, tag="edge")
P("Box Trailer Rusty", 16.5, 34.3, 12, gap=0.1, tag="highway"); P("Semi Tractor Red", 23.2, 34.8, 320, check=False, tag="highway")
P("Overturned Car", -14.0, 35.0, 75, tag="highway"); P("Police Car", -20.5, 31.5, 10, tag="highway"); P("Sedan", -30.0, 35.4, 165, tag="highway")
for (x, z) in ((-17.0, 32.5), (-18.0, 35.1), (-11.0, 31.7), (9.0, 32.0), (7.0, 34.6), (-3.5, 31.1)):
    P("Traffic Cone", x, z, rng.uniform(0, 360), tag="highway")
P("Sawhorse Barricade", -24.0, 34.0, 90, tag="highway"); P("Road Barrier", 28.5, 33.0, 80, tag="highway")
P("Speed Limit Sign", -44.0, 28.4, 180, tag="highway"); P("Direction Sign", 40.5, 28.4, 180, tag="highway")

# ================================================================ the back lot behind the motel (Blue's spawn) and the edges
for (nm, x, z, yy) in (("Pallet Stack", -54.2, -26.0, 90), ("Wheelbarrow", -54.3, 5.3, 20), ("Rusty Drum", -54.3, 12.3, 0), ("Tyre Stack", -57.6, 12.3, 0),
                       ("Running Generator", -54.0, 5.4 + 1.6, 90), ("Plastic Water Tank", -57.0, -25.6, 0)):
    if not P(nm, x, z, yy, gap=0.1, tag="backlot"):
        print("WARN backlot prop not placed", nm, x, z)
tk.bins_by_door(t, (MX0, -29.0), (0, 1), (-1, 0), rng, side=1, seed=240)
for (nm, x, z, yy) in (("Utility Pole", -40.0, -35.3, 0), ("Utility Pole", -16.0, -38.2, 0), ("Utility Pole", 8.0, -38.6, 0), ("Pole Transformer", -16.0, -38.2, 0)):
    P(nm, x, z, yy, check=False, tag="edge")


# ================================================================ lanes (walking routes kept free of solid dressing)
LANES = [
    ([(-57, -26), (-56, -16), (-56, 16), (-54, 24)], 2.4),                       # the back lot
    ([(-45, -25), (-45, 12)], 1.6), ([(-46, -25), (-22, -25)], 1.6),             # motel walkways
    ([(-52, -3.75), (-43, -3.75)], 2.4),                                         # through the breezeway
    ([(-43, 9), (-24, 9), (-12, 9), (12, 9), (18, 9)], 2.4),                     # the middle line, motel to truck lot
    ([(-43, -20), (-20, -20), (-14, -18.5), (12, -18.5), (16, -22)], 2.4),        # courtyard - diner lot - garage apron
    ([(-54, 24.6), (-24, 24.6), (-12, 24.6), (16, 24.6), (19, 19)], 2.4),         # frontage
    ([(-40, 33.5), (36, 33.5)], 2.0),                                            # highway
    ([(-17, -21), (-17, 26)], 2.2), ([(12, -16), (12, 26)], 2.4),                # either side of the store and canopy
    ([(-16, -38.3), (8, -38.3)], 1.8),                                           # the diner's back alley
    ([(18, -24), (18, 20)], 1.8), ([(29.2, -24), (29.2, 20)], 2.2), ([(40.7, -24), (40.7, 20)], 2.2), ([(52, -24), (52, 20)], 2.2),  # truck lot aisles
    ([(16, -2.5), (58, -2.5)], 2.4), ([(16, -24), (56, -24)], 2.4),              # truck lot cross aisles
    ([(54, -20), (55.5, 20)], 2.4),                                              # Red's spawn strip
    ([(8, -33), (18, -33), (18, -25)], 1.8),                                     # past the water tower
    ([(-30, -18), (-30, -21)], 2.0), ([(-22, -10), (-12, -10)], 2.0),
]
LANES += [([(x, -23), (x, 19.5)], 1.4) for x in (23.7, 34.7, 46.2)]          # the gaps between paired trailers stay open
for pts, w in LANES:
    t.add_lane(pts, w)


def scatter_solid(names, rect, n, seed, square=False, gap=0.25):
    """Solid junk scattered over a rectangle, off the lanes, spawns and hills, clear of everything else."""
    r2 = random.Random(seed); placed = 0
    for _ in range(n * 10):
        if placed >= n:
            break
        nm = r2.choice(names)
        yaw = r2.choice([0, 90, 180, 270]) + r2.uniform(-6, 6) if square else r2.uniform(0, 360)
        if t.prop(nm, r2.uniform(rect[0], rect[2]), r2.uniform(rect[1], rect[3]), yaw, gap=gap, lanes=True, tag="junk"):
            placed += 1
    return placed


JUNK = ["Pallet Stack", "Pallet", "Wooden Crate", "Small Crate", "Oil Drum", "Rusty Drum", "Tyre Stack", "Cable Spool", "Cardboard Stack",
        "Cardboard Box Large", "Plank Pile", "Engine Block", "Pipe Stack", "Gas Cylinders"]
JUNK_N = {
    "lot": scatter_solid(JUNK + ["Tyre Stack", "Oil Drum", "Pallet Stack"], (16.5, -23.0, 51.0, 19.0), 45, 901),
    "motel back": scatter_solid(["Gas Meter", "Junction Box", "Air Duct Riser", "Pallet", "Oil Drum", "Wheelie Bin", "Cardboard Stack"], (-52.0, -36.0, -22.0, -33.4), 10, 902, square=True),
    "diner back": scatter_solid(["Pallet Stack", "Pallet", "Wooden Crate", "Cardboard Stack", "Oil Drum", "Produce Crates", "Gas Cylinders"], (-14.0, -40.0, 6.0, -36.3), 8, 903, square=True),
    "garage back": scatter_solid(["Tyre Stack", "Tyre Stack", "Oil Drum", "Rusty Drum", "Engine Block", "Pallet"], (20.0, -40.8, 44.0, -38.3), 8, 904),
    "courtyard": scatter_solid(["Planter", "Patio Chair", "Sun Lounger", "Patio Table", "Kettle BBQ", "Bird Bath"], (-43.0, -23.0, -23.0, 11.0), 8, 905),
    "forecourt": scatter_solid(["Concrete Block", "Water Barrier", "Trash Can", "Gas Cylinders", "Tyre Stack"], (8.0, -6.0, 15.5, 11.0), 5, 906),
    "desert": scatter_solid(["Tyre Stack", "Oil Drum", "Rusty Drum", "Car On Bricks", "Concrete Pipe", "Hay Bale", "Log", "Tree Stump"], (-60.0, -40.0, 60.0, 39.0), 14, 907),
}

# ================================================================ furnishing the rooms
def motel_room(room, y, rr):
    """A motel room (u along the front wall from the door side, v into the room): bed against the side wall, TV opposite,
    bedside table, bathroom behind the partition at v = 3.9 (toilet, sink, shower), a suitcase on the floor."""
    W, D = room.W, room.D
    up = y > 0.5
    def put(nm, u, v, f):
        return room.put(nm, u, v, f, y=(y if up else None), rng=rr, tag="interior")
    put("Double Bed" if rr.random() < 0.7 else "Single Bed", W - 1.2, 2.6, "left")
    put("TV Stand", 0.35, 2.6, "right")
    put("Bedside Table", W - 0.36, 1.3, "left")
    room.put("Table Lamp", W - 0.36, 1.3, "left", y=y + 0.93, rng=rr, tag="interior")
    put("Toilet", 2.3, D - 0.46, "front")
    put("Bathroom Sink", 3.3, D - 0.37, "front")
    put("Shower Cubicle", W - 0.6, 4.65, "left")
    room.put(rr.choice(["Suitcase", "Duffel Bag", "Backpack", "Laundry Basket"]), 1.0, 3.3, "front", y=y if up else None, rng=rr, tag="interior")
    room.put(rr.choice(["Rug", "Striped Rug"]), W - 1.1, 1.0, "front", y=y if up else None, rng=rr, tag="interior")
    if rr.random() < 0.5:
        room.put("Ceiling Fan", W / 2, 2.0, "front", y=y + 2.4, rng=rr, tag="interior")


for wing, fl, k, room, y in MOTEL_ROOMS:
    motel_room(room, y, random.Random(800 + k * 7 + fl * 31 + (0 if wing == "W" else 100)))
    if fl == 0:
        ix0, iz0, ix1, iz1 = room.rect
        WAYPOINTS.append(room.xz(0.9, 1.2))
        WAYPOINTS.append(room.xz(0.8, 4.8))
WAYPOINTS += [(-50.0, -3.75), (-55.0, -3.75)]
# the lobby: reception desk, key rack, a TV, chairs, the ice machine and vending machines
for nm, u, v, f in (("Reception Desk", 3.0, 3.6, "right"), ("Bookshelf", 1.0, 6.1, "front"), ("Armchair", 5.6, 5.8, "front"), ("Armchair", 6.8, 5.8, "front"),
                    ("Coffee Table", 6.2, 4.8, "front"), ("Flickering TV", 0.25, 2.0, "right"), ("Ice Machine", 0.45, 4.4, "right"),
                    ("Glowing Vending Machine", 5.0, 0.5, "back"), ("Water Cooler", 8.1, 6.1, "front"), ("Office Plant", 8.1, 3.0, "left")):
    if not LOBBY.put(nm, u, v, f, rng=rng, tag="interior"):
        print("WARN lobby", nm)
LOBBY.put("Desk Phone", 3.0, 3.6, "right", y=1.0, rng=rng, tag="interior"); LOBBY.put("Ceiling Fan", 4.0, 3.0, "front", y=2.5, rng=rng, tag="interior")
WAYPOINTS += [(-50.0, -28.5), (-48.0, -25.0)]
# walkway and balcony furniture against the front walls (a 1.3 m strip by the rail stays clear)
for (z, nm) in ((-21.0, "Housekeeping Cart"), (-11.3, "Ice Machine"), (1.5, "Plastic Chair"), (9.9, "Plastic Chair")):
    P(nm, MXF + 0.4, z, 90, y=SLAB, check=False, tag="balcony")
for (x, nm) in ((-31.0, "Plastic Chair"), (-36.0, "Housekeeping Cart")):
    P(nm, x, NZF + 0.35, 0, y=SLAB, check=False, tag="balcony")
for (z, nm) in ((-16.0, "Glowing Vending Machine"), (-1.6, "Plastic Chair"), (-18.4, "Housekeeping Cart")):
    P(nm, MXF + 0.45, z, 90, tag="balcony")
for room, kind, du, bu in ROOMS:
    tk.furnish(t, room, kind, rng, door_u=du, back_door_u=bu)
WAYPOINTS += [(-1.0, 3.5), (4.0, -1.5), (-6.0, 0.0)]

# ================================================================ roofs and walls
tk.roof_kit(t, MX0, NZ0, MXF + 0.4, WZ1, ROOF_T, random.Random(501), n=9)
tk.roof_kit(t, MXF + 0.4, NZ0, NX1, NZF + 0.4, ROOF_T, random.Random(502), n=5)
tk.roof_kit(t, DN[0], DN[1], DN[2], DN[3], 3.8 + 0.3, random.Random(503), n=6)
tk.roof_kit(t, ST[0], ST[1], ST[2], ST[3], 3.8 + 0.3, random.Random(504), n=4)
tk.roof_kit(t, GR[0], GR[1], GR[2] - 2.0, GR[3], GR_H + 0.3, random.Random(505), n=5)
for k, (a, b, out) in enumerate([((DN[0], DN[1] - 0.05), (DN[2], DN[1] - 0.05), (0, -1)), ((ST[0], ST[1] - 0.05), (ST[2], ST[1] - 0.05), (0, -1)),
                                 ((GR[0], GR[1] - 0.05), (GR[2], GR[1] - 0.05), (0, -1)), ((MX0 - 0.05, -22.0), (MX0 - 0.05, 11.0), (-1, 0)),
                                 ((DN[0] - 0.05, -35.0), (DN[0] - 0.05, -30.0), (-1, 0)), ((GR[2] + 0.05, -29.5), (GR[2] + 0.05, -26.5), (1, 0))]):
    tk.wall_kit(t, a, b, out, random.Random(600 + k), skip=[(s - 1.2, s + 1.2) for s in (4.0, 6.0)] if k == 0 else ())

# ================================================================ trees and foliage (desert scrub)
for x, z in [(-34.0, 6.5), (-26.5, -2.0), (-40.5, -1.5), (-27.0, -19.0), (-51.5, 22.5), (-44.0, 25.6), (-15.5, -26.5), (8.0, -12.0),
             (-15.0, 12.0), (15.0, -8.0), (16.5, 14.0), (52.5, -14.5), (48.5, -26.0), (55.0, -24.0), (0.5, -38.6), (-20.5, -30.5),
             (48.0, -31.0), (17.5, -19.5), (-48.0, 30.0), (46.0, 27.6), (-58.0, -2.5), (57.5, 18.0)]:
    t.tree(x + rng.uniform(-0.4, 0.4), z + rng.uniform(-0.4, 0.4), rng.uniform(5.0, 7.5), "tree")
BX0, BZ0, BX1, BZ1 = t.x0, t.z0, t.x1, t.z1
DRY = [0.62, 0.55, 0.32]; SAGE = [0.46, 0.50, 0.36]
t.region("grass", 4.0, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.6, scale=[0.7, 1.3], color=DRY)
t.region("shrub", 0.8, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.6, color=SAGE)
t.region("bush", 0.05, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.8, scale=[0.8, 1.4], color=[0.40, 0.44, 0.28])
# the motel courtyard's tired lawn, and the scrubby verges along the highway and the edges
LAWN = [[-43.5, -23.5], [-22.5, -23.5], [-22.5, 11.5], [-43.5, 11.5]]
t.region("grass", 7.0, polygon=LAWN, patchiness=0.4, scale=[0.9, 1.4], color=[0.52, 0.56, 0.28])
t.region("shrub", 0.4, polygon=LAWN, patchiness=0.5, color=[0.40, 0.50, 0.26])
for poly in ([[-62, 37.6], [62, 37.6], [62, 42], [-62, 42]], [[-64, -37.5], [64, -37.5], [64, -43], [-64, -43]], [[-62, -30], [-58.5, -30], [-58.5, 30], [-62, 30]],
             [[58, -30], [62, -30], [62, 30], [58, 30]]):
    t.region("grass", 5.0, polygon=poly, patchiness=0.5, scale=[0.9, 1.6], color=DRY)
    t.region("shrub", 1.2, polygon=poly, patchiness=0.5, color=SAGE)
    t.region("bush", 0.35, polygon=poly, patchiness=0.6, scale=[0.9, 1.6], color=[0.42, 0.46, 0.30])
for (a, b) in [((MX0 - 0.5, -32.0), (MX0 - 0.5, 11.0)), ((DN[0], DN[1] - 0.6), (DN[2], DN[1] - 0.6)), ((GR[0], GR[1] - 0.6), (GR[2], GR[1] - 0.6)),
               ((-62, 26.9), (62, 26.9)), ((ST[0] - 0.5, -3.5), (ST[0] - 0.5, 5.5))]:
    tk.weeds_along(t, a, b, density=5.0)
t.exclude.append({"shape": "rect", "center": [-32.5, -11.0], "halfX": 5.8, "halfZ": 3.3})   # nothing grows in the pool

# ================================================================ spawns, waypoints (after every solid thing)
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
t.ops.append({"op": "AutoWaypoints", "spacing": 6})
for (x, z) in WAYPOINTS:
    if t.free((x - 0.35, z - 0.35, x + 0.35, z + 0.35)):
        t.ops.append({"op": "Waypoint", "x": tk.R(x), "z": tk.R(z)})

# ================================================================ cosmetic clutter (the game's Clutter op runs last and keeps out of walls)
LITTER = ["Litter", "Litter", "Litter", "Newspaper", "Soda Cans", "Bottles", "Bottle", "Plastic Bag", "Flattened Cardboard", "Cardboard Box Small",
          "Trash Bag", "Plank", "Tyre", "Car Wheel", "Rubble", "Loose Bricks", "Cinder Blocks"]
t.clutter(LITTER, BX0, BZ0, 0, BZ1, 650, 41); t.clutter(LITTER, 0, BZ0, BX1, BZ1, 650, 42)
t.clutter(["Tyre", "Tyre", "Car Wheel", "Litter", "Soda Cans", "Plank", "Road Plate", "Manhole Cover", "Plastic Bag", "Flattened Cardboard", "Car Door"],
          16, -24, 60, 20, 320, 43)
t.clutter(["Litter", "Soda Cans", "Plastic Bag", "Newspaper", "Bottle", "Storm Drain"], -55, 30.1, 50, 30.1, 120, 44, width=1.2)
t.clutter(["Litter", "Soda Cans", "Plastic Bag", "Bottle", "Plank", "Tyre", "Rubble"], -45, 33.5, 40, 33.5, 150, 45, width=7.0)
t.clutter(["Litter", "Plastic Bag", "Bottles", "Soda Cans", "Sports Balls", "Newspaper", "Flattened Cardboard"], -44, -24, -22, 12, 120, 46)
t.clutter(["Litter", "Trash Bag", "Flattened Cardboard", "Open Cardboard Box", "Bottles", "Plastic Bag", "Cardboard Box Small"], -16, -40, 8, -36.5, 90, 47)
t.clutter(["Litter", "Soda Cans", "Plastic Bag", "Newspaper", "Cardboard Box Small"], -18, -21, 12, -7, 90, 48)
t.clutter(["Litter", "Soda Cans", "Plastic Bag", "Tyre", "Newspaper", "Bottle"], -16, 6.5, 16, 29, 120, 49)
t.clutter(["Tyre", "Car Wheel", "Toolbox", "Paint Cans", "Bucket", "Plank", "Litter", "Cardboard Box Small"], GR[0] + 0.4, GR[1] + 0.4, GR[2] - 0.4, GR[3] - 0.4, 60, 50)
t.clutter(["Litter", "Newspaper", "Soda Cans", "Plastic Bag", "Bottles", "Cardboard Box Small"], DN[0] + 0.4, DN[1] + 0.4, DN[2] - 0.4, DN[3] - 0.4, 35, 51)
t.clutter(["Litter", "Newspaper", "Soda Cans", "Plastic Bag", "Bottles", "Cardboard Box Small", "Book Pile"], ST[0] + 0.4, ST[1] + 0.4, ST[2] - 0.4, ST[3] - 0.4, 25, 52)
for k, (wing, fl, kk, room, y) in enumerate(MOTEL_ROOMS):
    if fl == 0:
        ix0, iz0, ix1, iz1 = room.rect
        t.clutter(["Litter", "Newspaper", "Soda Cans", "Bottles", "Book Pile", "Plastic Bag", "Teddy Bear", "Backpack"], ix0 + 0.3, iz0 + 0.3, ix1 - 0.3, iz1 - 0.3, 8, 60 + k)
t.clutter(["Litter", "Plastic Bag", "Bottle", "Soda Cans", "Tyre", "Rubble", "Plank", "Cinder Blocks"], -61, -27, -53.5, 24, 100, 70)

# ================================================================ the map
TERRAIN = {
    "cell": 0.5, "margin": 30,
    "surface": {"grass": "#9a8458", "dirt": "#8e6c48", "rock": "#8a5e44", "sand": "#b49a6c", "sandBelow": 0.6, "variation": 0.1},
    "features": [
        {"type": "plateau", "x": -32.5, "z": -11.0, "w": 11.0, "d": 6.0, "height": -1.0, "edge": 0, "mode": "cut"},
        {"type": "plateau", "x": -29.0, "z": -11.0, "w": 4.0, "d": 6.0, "height": -1.8, "edge": 0, "mode": "cut"},
        # the desert beyond the fence: mesas to the north, low hills over the highway, buttes east and west
        {"type": "ridge", "points": [[-110, -66, 9], [-50, -72, 13], [10, -70, 16], [70, -73, 12], [110, -64, 8]], "width": 32, "material": "rock"},
        {"type": "ridge", "points": [[-110, 72, 5], [-40, 74, 7], [30, 73, 6], [110, 70, 5]], "width": 26},
        {"type": "hill", "x": 92, "z": -20, "radius": 16, "height": 11, "falloff": "dome", "material": "rock"},
        {"type": "hill", "x": -92, "z": 20, "radius": 14, "height": 8, "falloff": "dome", "material": "rock"},
    ],
}
foliage = {"seed": 408, "surfaces": {"Plain": 0.1}, "paths": t.paths, "regions": t.regions, "items": t.items, "exclude": t.exclude}
mp = {
    "format": "call-of-slop-map/1",
    "name": "Truck Stop",
    "description": "A desert highway truck stop at dusk: a two-storey motel round a drained pool, a diner, a gas station and store, a tire shop and a lot full of semis.",
    "author": "Call of Slop",
    "order": 408,
    "maxPlayers": 12,
    "sky": "#c98a62",
    "skybox": {"preset": "dusk", "sun": {"elevation": 5.5, "azimuth": 318}, "haze": {"color": "#f0a060", "strength": 0.5},
               "clouds": {"coverage": 0.25, "speed": 4}, "stars": 0.8},
    "boundary": {"shape": "smooth", "points": [list(p) for p in CTRL], "noise": NOISE,
                 "edge": {"style": "fence", "height": 1.3, "spacing": 3.0, "color": [0.45, 0.36, 0.26]}, "groundMargin": 10},
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "terrain": TERRAIN,
    "ops": t.ops,
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
blocks = sum(1 for o in t.ops if o["op"] == "Block") + sum(9 for o in t.ops if o["op"] in ("StairsX", "StairsZ"))
print(f"area {t.area():.0f} m2, blocks {blocks}, trees {len(t.items)}, meshes {t.meshes}, hand props {len(t.props)} (solid {solid}, cosmetic {cosm})")
print("props by category:", dict(sorted(cats.items())))
print("props by rule:", dict(sorted(t.notes.items())))
print("junk scattered:", JUNK_N)
print("hand-placed props that didn't fit:", len(FAILED), FAILED[:40])
print("clutter ops:", sum(1 for o in t.ops if o["op"] == "Clutter"), "clutter asked:", sum(o["count"] for o in t.ops if o["op"] == "Clutter"))
expl = [(x, z) for name, x, z, yaw, y, tag in t.props if name == "Explosive Barrel"]
for (ex, ez) in expl:
    d = min(math.hypot(ex - sx, ez - sz) for sx, sz in BLUE_SP + RED_SP + FFA_SP)
    if d < 12:
        print("WARN explosive barrel near a spawn", ex, ez, round(d, 1))
vis = [(a, b) for a in BLUE_SP for b in RED_SP if t.sees(a, b)]
print("blue-red spawn lines of sight:", len(vis))
fb = t.walk_field(BLUE_SP); fr = t.walk_field(RED_SP)
for i, (x, z, r) in enumerate(HILLS):
    b = min(t.walk_to(fb, x + dx, z + dz) for dx, dz in ((0, 0), (0, 2.5), (0, -2.5), (2.5, 0), (-2.5, 0)))
    rd = min(t.walk_to(fr, x + dx, z + dz) for dx, dz in ((0, 0), (0, 2.5), (0, -2.5), (2.5, 0), (-2.5, 0)))
    print(f"hill {i + 1} ({x},{z}): walk blue {b:.0f} m red {rd:.0f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
b, rd = t.walk_to(fb, *FLAGS[1]), t.walk_to(fr, *FLAGS[0])
print(f"enemy flag: blue {b:.0f} m red {rd:.0f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
if OUT_PNG:
    marks = [("dot", x, z, 0.8, (40, 90, 240)) for x, z in BLUE_SP] + [("dot", x, z, 0.8, (240, 50, 50)) for x, z in RED_SP] + [("dot", x, z, 0.8, (250, 250, 250)) for x, z in FFA_SP]
    marks += [("circle", x, z, r, (255, 230, 0)) for x, z, r in HILLS] + [("dot", x, z, 1.0, (255, 255, 255)) for x, z in FLAGS]
    marks += [("line", a, b, 0, (255, 0, 255)) for a, b in vis]
    t.preview(OUT_PNG, marks)
