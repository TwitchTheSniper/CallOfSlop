#!/usr/bin/env python3
"""Generator for Maps/Cluttered/09-Beachhead.json (large, 16 players): a beach that leads up into a military base.
Usage: python3 tools/cluttered/beachhead.py <repo root> [preview.png]

Not mirrored. Blue lands on the beach (south, by the waterline, among wrecked landing craft and tank traps), Red
holds the base on the plateau (north, 12 m up, behind fences, sandbags and concrete). Between them the bluff: a steep
face with three pillboxes dug into its top, crossed by a switchback road (the centre), a rocky ravine (west), a long
dune slope (east), two bluff foot paths and a drainage tunnel that runs under the bluff from the seawall into the base.
The base has the height and the hard cover; the beach gets more soft cover and more ways up, and the objectives sit
where the walk is the same for both teams (the generator measures it on a 0.5 m grid over the terrain; the self-test
measures it again on the game's nav graph and with a scripted walker).

Pack props (Maps/Cluttered/Props/Beachhead) come from tools/cluttered/beachhead_props.py.
"""
import json, math, os, random, sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import townkit as tk  # noqa: E402
from townkit import yaw_facing, yaw_along  # noqa: E402
import beachterrain as bt  # noqa: E402

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/09-Beachhead.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
PY = bt.PLATEAU_Y
WATER_Y = bt.WATER_Y

# ============================================================================ outline
CTRL = [(-84, 50), (-60, 58), (-32, 62), (-4, 60), (24, 63), (52, 59), (76, 52),
        (90, 30), (88, 6), (74, -14), (70, -34), (52, -50), (24, -58), (-6, -55), (-34, -60), (-58, -48), (-74, -28), (-86, 0), (-90, 26)]
NOISE = {"amplitude": 1.4, "scale": 18, "seed": 9, "octaves": 2}
t = tk.Town(REPO, CTRL, NOISE, pack_props=[os.path.join(REPO, "Maps/Cluttered/Props")])
rng = random.Random(409)
OUT = t.outline
HALF_X = max(abs(min(p[0] for p in OUT)), abs(max(p[0] for p in OUT)))
HALF_Z = max(abs(min(p[1] for p in OUT)), abs(max(p[1] for p in OUT)))
MARGIN = 30
T = bt.Terrain(HALF_X, HALF_Z, MARGIN)

# the shoreline: the outline's south edge (the ground is SHORE_Y there and runs down under the water outside it)
_sx = np.arange(-120, 121, 1.0)
_south = []
for x in _sx:
    xc = min(max(x, -83.0), 75.0)
    best = None
    for k in range(len(OUT)):
        a = OUT[k]; b = OUT[(k + 1) % len(OUT)]
        if (a[0] - xc) * (b[0] - xc) <= 0 and a[0] != b[0] and a[1] > 25 and b[1] > 25:
            z = a[1] + (b[1] - a[1]) * (xc - a[0]) / (b[0] - a[0])
            best = z if best is None else max(best, z)
    _south.append(best)
SHORE = np.array(_south)
SHORE_Y = 0.42


def shore_z(x):
    return np.interp(x, _sx, SHORE)


# ============================================================================ the base heightfield (one grid feature)
# bluff top edge (z where the flat plateau starts) and face width, west to east
TOP = [(-130, 22), (-88, 18), (-70, 15), (-50, 12.5), (-30, 9.5), (-12, 7.5), (6, 6.5), (24, 8.0), (42, 9.5), (52, 9.0), (60, 3.0), (68, -3.0), (80, -6.0), (130, -6.0)]
FACE = [(-130, 4.0), (48, 4.0), (56, 9.0), (64, 26.0), (72, 31.0), (130, 31.0)]
DUNE_Y = 3.6        # the ground at the foot of the bluff


def base_height(X, Z):
    dz = shore_z(X) - Z                                         # metres inland from the waterline
    beach = np.where(dz < 0, np.maximum(SHORE_Y + 0.28 * dz, -3.0), SHORE_Y + np.minimum(dz, 24.0) * 0.072)
    # dunes: a band of bumps from 16 m inland to the bluff foot, rising towards DUNE_Y
    dune_w = smoothstep_np((dz - 14.0) / 10.0)
    bumps = 1.1 * bt.value_noise(X / 9.0, Z / 7.0, 31) + 0.5 * bt.value_noise(X / 4.0, Z / 4.0, 32)
    dunes = beach + dune_w * (DUNE_Y - beach + np.maximum(bumps, -0.6))
    top = bt.interp(TOP, X); face = bt.interp(FACE, X)
    u = (Z - top) / face                                        # 0 at the plateau edge, 1 at the foot of the face
    bluff = PY + (dunes - PY) * smoothstep_np(u)
    h = np.where(Z <= top, PY, bluff)
    # outside the outline the land rises into hills (not on the sea side): the base's hinterland, the headland
    dout = -SD(X, Z)
    seaward = smoothstep_np((dz - 6.0) / 10.0)                  # 0 at the shore, 1 inland
    west = smoothstep_np((-X - 84.0) / 8.0)
    k = np.maximum(seaward, west) * smoothstep_np(dout / 18.0)
    hills = 7.0 + 2.5 * bt.value_noise(X / 23.0, Z / 19.0, 41)
    h = h + k * hills
    return h


def smoothstep_np(e):
    return bt.smoothstep(e)


def SD(X, Z):
    """Signed distance to the outline (positive inside), for numpy arrays (decimated outline)."""
    pts = np.array(OUT[::3], float)
    d = np.full(X.shape, 1e9); inside = np.zeros(X.shape, bool)
    a = pts; b = np.roll(pts, -1, axis=0)
    for (ax, az), (bx, bz) in zip(a, b):
        dx, dz = bx - ax, bz - az
        L = dx * dx + dz * dz
        tt = np.clip(((X - ax) * dx + (Z - az) * dz) / (L if L > 0 else 1), 0, 1)
        d = np.minimum(d, np.hypot(ax + tt * dx - X, az + tt * dz - Z))
        cond = ((az > Z) != (bz > Z)) & (X < ax + (Z - az) * (bx - ax) / ((bz - az) if bz != az else 1e-9))
        inside ^= cond
    return np.where(inside, d, -d)


GX0 = -math.ceil(T.ext_x / bt.GRID_CELL) * bt.GRID_CELL
GZ0 = -math.ceil(T.ext_z / bt.GRID_CELL) * bt.GRID_CELL
GCOLS = int(round(-2 * GX0 / bt.GRID_CELL)) + 1
GROWS = int(round(-2 * GZ0 / bt.GRID_CELL)) + 1
_gx = GX0 + np.arange(GCOLS) * bt.GRID_CELL
_gz = GZ0 + np.arange(GROWS) * bt.GRID_CELL
_GX, _GZ = np.meshgrid(_gx, _gz)
BASE = np.round(base_height(_GX, _GZ), 2)
T.grid(round(GX0, 2), round(GZ0, 2), bt.GRID_CELL, GCOLS, GROWS, [float(v) for v in BASE.ravel()])


def ground(x, z):
    return T.sample(x, z)


# ============================================================================ sharp terrain features (after the grid)
# The ravine (west flank): a rocky gully cut from the beach up into the plateau.
RAVINE = [(-71.0, 36.0, 1.9), (-67.0, 23.0, 3.9), (-62.0, 9.0, 8.1), (-58.5, -3.0, PY), (-57.5, -7.0, PY)]
T.ramp([list(p) for p in RAVINE], width=7.0, edge=2.5, material="dirt")
# The switchback road (centre): leg 1 along the foot of the bluff to the hairpin, leg 2 back up across the face.
ROAD1 = [(15.0, 38.5, 2.3), (3.0, 30.5, 3.9), (-16.0, 17.5, 7.0)]
HAIRPIN = (-20.0, 15.0, 7.0)
ROAD2 = [(-17.0, 11.0, 7.25), (6.0, 4.0, 11.0), (14.0, 0.0, PY), (16.0, -4.0, PY)]
T.ramp([list(p) for p in ROAD1], width=6.5, edge=2.2, material="dirt")
T.flatten(HAIRPIN[0], HAIRPIN[1], height=HAIRPIN[2], radius=6.5, edge=2.5, material="dirt")
T.ramp([list(p) for p in ROAD2], width=6.5, edge=2.2, material="dirt")
# the overlook: a gravel lay-by on the beach side of leg 2, level with the road, out over the face (hill 2)
OVERLOOK = (1.0, 9.0, 10.0)
T.flatten(OVERLOOK[0], OVERLOOK[1], height=OVERLOOK[2], radius=5.4, edge=2.0, material="dirt")
# the dune observation post: a level hollow half way up the long dune slope in the east (hill 3)
DUNE_OP = (70.0, 15.0)
T.plateau(DUNE_OP[0], DUNE_OP[1], height=round(T.sample(*DUNE_OP), 2), radius=4.2, edge=5.0, mode="fill", material="sand")
# The west bluff path: a zigzag foot path up the face between the ravine and the west pillbox.
WPATH = [(-39.0, 17.5, 3.7), (-53.0, 14.0, 8.0), (-46.0, 9.5, PY), (-45.0, 7.0, PY)]
T.ramp([list(p) for p in WPATH], width=2.8, edge=1.4, material="dirt")
# Pillboxes dug into the top of the face: a level pad PB_FLOOR high, the bunker on it, a trench from the plateau
# down to its back door.
PB_FLOOR = PY - 2.4
PILLBOXES = []  # (x0, z0, x1, z1) outer footprint (walls included), door x


def pillbox_pad(cx, top_z, w=7.0, d=5.4):
    """The pad for a pillbox centred at x = cx whose back (north) wall stands 1.2 m behind the plateau edge."""
    z0 = top_z - 1.2
    z1 = z0 + d
    T.flatten(cx, (z0 + z1) / 2, height=PB_FLOOR, w=w, d=d, edge=0.4)
    T.ramp([[cx, z0 - 8.5, PY], [cx, z0 + 0.3, PB_FLOOR]], width=1.9, edge=0.3, mode="cut")
    PILLBOXES.append((cx - w / 2, z0, cx + w / 2, z1, cx))


for cx in (-38.0, 24.0, 52.0):
    pillbox_pad(cx, float(bt.interp(TOP, cx)))
# The drainage tunnel (east of centre): an open concrete channel through the dunes from the seawall, then a culvert
# under the bluff, then an open ramp up into the base. Straight along z at x = TUNNEL_X so the cut is clean.
TUNNEL_X = 40.0
TUNNEL = [(TUNNEL_X, 36.5, 2.1), (TUNNEL_X, 15.0, 3.4), (TUNNEL_X, -11.0, PY - 0.02), (TUNNEL_X, -13.0, PY)]
TUNNEL_HALF = 1.75
T.ramp([list(p) for p in TUNNEL], width=TUNNEL_HALF * 2, edge=0.0, mode="cut")
# The seawall: a concrete retaining wall 22 m in from the waterline (1.3-1.6 m high on the beach side, the dunes level
# with its top behind), in 4 m pieces along x; sand ramps through its gaps.
SEAWALL_X = (-46.0, 58.0)
SEAWALL_TOP = 3.45
SEAWALL_GAPS = [(-31.0, -26.0), (9.0, 21.0), (26.5, 31.5), (37.4, 42.6)]
SEAWALL = []   # (x0, x1, z of the beach face)
_runs = []
_x = SEAWALL_X[0]
for g0, g1 in sorted(SEAWALL_GAPS) + [(SEAWALL_X[1], SEAWALL_X[1])]:
    if g0 > _x:
        _runs.append((_x, g0))
    _x = max(_x, g1)
for r0, r1 in _runs:
    n = max(1, math.ceil((r1 - r0) / 4.0))
    for k in range(n):
        a0 = r0 + (r1 - r0) * k / n; a1 = r0 + (r1 - r0) * (k + 1) / n
        SEAWALL.append((round(a0, 2), round(a1, 2), round(float(shore_z((a0 + a1) / 2)) - 22.0, 2)))
for g0, g1 in SEAWALL_GAPS:
    gx = (g0 + g1) / 2
    if abs(gx - TUNNEL_X) < 3 or abs(gx - 15.0) < 7:
        continue   # the tunnel channel and the road have their own ways through
    zw = float(shore_z(gx)) - 22.0
    T.ramp([[gx, zw + 5.0], [gx, zw - 4.5]], width=min(4.0, g1 - g0 - 0.6), edge=1.5, material="sand")
T.finish()

# ============================================================================ site: world-anchored blocks and meshes
W_ = {"anchor": "world"}


def wblock(x0, y0, z0, x1, y1, z1, c, occupy=True):
    """A Block at absolute heights (anchor world); the occupancy grids get it where it stands at body height."""
    x0, x1 = min(x0, x1), max(x0, x1); z0, z1 = min(z0, z1), max(z0, z1)
    t.ops.append({"op": "Block", "x0": tk.R(x0), "y0": tk.R(y0), "z0": tk.R(z0), "x1": tk.R(x1), "y1": tk.R(y1), "z1": tk.R(z1), "color": tk.col(c), **W_})
    g = ground((x0 + x1) / 2, (z0 + z1) / 2)
    t.boxes.append((x0, z0, x1, z1, y0 - g, y1 - g, c))
    r0, r1 = y0 - g, y1 - g
    if not occupy:
        return
    r = (x0, z0, x1, z1)
    if r0 < 1.8 and r1 > 0.05:
        t.mark(t.occ, r)
    if r0 < 1.7 and r1 > 0.55:
        t.mark(t.walk, r)
    if r0 < 1.6 and r1 > 1.6:
        t.mark(t.tall, r)


def wmesh(kind, x, z, c, y, **kw):
    """A Mesh at absolute height y (its base)."""
    t.mesh(kind, x, z, c, y=round(y, 3), **kw, **W_)


def gmesh(kind, x, z, c, **kw):
    """A Mesh standing on the lowest ground under it (the default anchor)."""
    t.mesh(kind, x, z, c, **kw)


def wall(x0, z0, x1, z1, y0, h, th, c, doors=(), windows=()):
    """A wall along X (z0 == z1) or Z (x0 == x1), th thick on the + side, from y0 up h, with openings:
    doors [(centre along the wall, width, top)], windows [(centre, width, bottom, top)]."""
    alongx = abs(x1 - x0) >= abs(z1 - z0)
    a, b = (min(x0, x1), max(x0, x1)) if alongx else (min(z0, z1), max(z0, z1))
    holes = [(c0, w, 0.0, top) for c0, w, top in doors] + list(windows)
    holes.sort()
    pos = a
    pieces = []
    for c0, w, bot, top in holes:
        if c0 - w / 2 > pos:
            pieces.append((pos, c0 - w / 2, 0.0, h))
        if bot > 0.01:
            pieces.append((c0 - w / 2, c0 + w / 2, 0.0, bot))
        if top < h - 0.01:
            pieces.append((c0 - w / 2, c0 + w / 2, top, h))
        pos = c0 + w / 2
    if pos < b:
        pieces.append((pos, b, 0.0, h))
    for p0, p1, ya, yb in pieces:
        if alongx:
            wblock(p0, y0 + ya, z0, p1, y0 + yb, z0 + th, c)
        else:
            wblock(x0, y0 + ya, p0, x0 + th, y0 + yb, p1, c)


def place(name, x, z, yaw=0.0, y=None, check=True, lanes=False, tag=None, gap=0.02, margin=0.6):
    return t.prop(name, x, z, yaw, y=y, check=check, lanes=lanes, tag=tag, gap=gap, margin=margin)


# ============================================================================ palette
SAND = (0.80, 0.72, 0.52); CONC = (0.62, 0.61, 0.57); CONC_D = (0.45, 0.45, 0.43); ASPH = (0.24, 0.24, 0.25)
OLIVE = (0.29, 0.33, 0.15); OLIVE_W = (0.42, 0.45, 0.30); KHAKI = (0.62, 0.58, 0.44); ROOF_G = (0.26, 0.30, 0.20)
BUNKER = (0.55, 0.54, 0.50); BUNKER_D = (0.40, 0.40, 0.38); WOOD = (0.45, 0.33, 0.21); RUSTC = (0.42, 0.28, 0.18)
WHITE = (0.88, 0.88, 0.84); YELLOW = (0.86, 0.72, 0.18); WATER = (0.16, 0.36, 0.44); DARK = (0.14, 0.15, 0.16)
CONTAINERS = [(0.33, 0.38, 0.22), (0.45, 0.30, 0.18), (0.30, 0.34, 0.38), (0.52, 0.48, 0.34)]

# ============================================================================ objectives (decided first; everything keeps clear)
BLUE_SP = [(-28.0, 58.0), (-20.0, 58.5), (-6.0, 56.0), (13.5, 59.5), (22.0, 59.5), (31.0, 57.5), (-44.0, 56.0), (-56.0, 54.5), (-36.0, 58.5)]
RED_SP = [(-24.0, -51.0), (-16.0, -52.5), (-34.0, -51.5), (16.0, -53.0), (24.0, -51.5), (32.0, -50.5), (-8.0, -52.0), (6.0, -51.5), (40.0, -48.0)]
FFA_SP = [(-66.0, 26.0), (64.0, 26.0), (-24.0, 27.0), (-58.0, -22.0), (62.0, -32.0), (-2.0, -26.0)]
HILLS = [(-46.0, 2.0, 5.0), (OVERLOOK[0], OVERLOOK[1], 4.5), (DUNE_OP[0], DUNE_OP[1], 4.5)]
FLAGS = [(-2.0, 55.0), (2.0, -49.0)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.keepout.append((x, z, 2.6))
for x, z, r in HILLS:
    t.keepout.append((x, z, r + 0.6))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))

# ============================================================================ lanes (solid props keep off; bots walk them)
LANES = [
    ([(p[0], p[1]) for p in ROAD1], 4.5), ([(p[0], p[1]) for p in ROAD2], 4.5), ([(p[0], p[1]) for p in RAVINE], 3.6),
    ([(p[0], p[1]) for p in WPATH], 2.0), ([(TUNNEL_X, 37.0), (TUNNEL_X, -13.0)], 2.4),
    ([(x, float(shore_z(x)) - 5.0) for x in range(-62, 64, 8)], 3.0),             # along the beach behind the landing craft
    ([(x, float(shore_z(x)) - 17.5) for x in range(-58, 70, 8)], 3.0),            # along the top of the beach below the seawall
    ([(-60, 26), (-30, 22), (-10, 26), (20, 24), (50, 22), (72, 18)], 2.6),       # through the dunes
    ([(60, 40), (70, 20), (68, 4), (64, -4)], 4.0),                               # the dune slope (east)
    ([(16.0, -4.0), (16.0, -48.0)], 4.0), ([(-34.0, -10.0), (16.0, -10.0)], 4.0), ([(16.0, -24.0), (62.0, -24.0)], 4.0),
    ([(-56, -4), (-48, 2), (-20, 3), (4, 2), (30, 2), (52, 2), (64, -2)], 2.6),   # behind the front line along the plateau edge
    ([(-38.0, 1.0), (-38.0, 9.0)], 1.6), ([(24.0, -1.0), (24.0, 7.0)], 1.6), ([(52.0, 0.0), (52.0, 8.0)], 1.6),   # bunker trenches
    ([(-40, -20), (-40, -48), (-14, -50), (36, -50)], 3.0),                       # behind the barracks and the HQ
]
for pts, w in LANES:
    t.add_lane(pts, w)

# ============================================================================ the sea and the coast beyond the outline (cosmetic)
wmesh("box", 0.0, 190.0, WATER, WATER_Y - 0.3, width=500.0, height=0.3, depth=300.0)
# a grounded landing craft half under water, a wrecked one further out, buoys, and a warship on the horizon
for (x, z, yaw, tilt) in [(-38.0, 74.0, 76.0, 9.0), (34.0, 82.0, 101.0, -14.0)]:
    wmesh("box", x, z, (0.36, 0.38, 0.40), -1.2, width=10.0, height=2.2, depth=3.6, yaw=yaw, roll=tilt, material="Metal")
    wmesh("box", x, z, (0.30, 0.22, 0.16), 0.6, width=9.0, height=0.25, depth=3.7, yaw=yaw, roll=tilt)
for (x, z) in [(-70.0, 80.0), (8.0, 96.0), (62.0, 78.0), (-20.0, 110.0)]:
    wmesh("sphere", x, z, (0.85, 0.30, 0.12), -0.35, width=0.9, height=0.9, depth=0.9, segments=10, detail=6)
    wmesh("cylinder", x, z, (0.85, 0.30, 0.12), 0.3, width=0.12, height=1.1, depth=0.12, segments=6)
SHIP = (0.42, 0.44, 0.47)
wmesh("box", 40.0, 215.0, SHIP, -1.0, width=58.0, height=5.0, depth=8.0, yaw=8.0)
wmesh("box", 44.0, 215.0, SHIP, 4.0, width=16.0, height=5.5, depth=5.5, yaw=8.0)
wmesh("box", 46.0, 215.0, (0.36, 0.38, 0.41), 9.5, width=4.0, height=7.0, depth=1.2, yaw=8.0)
wmesh("cylinder", 30.0, 216.5, SHIP, 4.0, width=2.6, height=6.0, depth=2.6, segments=10)
wmesh("box", 18.0, 218.0, (0.30, 0.31, 0.33), 4.0, width=5.0, height=2.0, depth=3.0, yaw=8.0)
wmesh("box", 13.0, 218.6, (0.25, 0.26, 0.28), 4.8, width=6.0, height=0.5, depth=0.5, yaw=8.0)

# ============================================================================ seawall
for x0, x1, zw in SEAWALL:
    lo = min(ground(x0, zw + 0.2), ground(x1, zw + 0.2)) - 0.6
    wblock(x0, lo, zw - 0.8, x1, SEAWALL_TOP, zw, CONC)
    wmesh("box", (x0 + x1) / 2, zw - 0.4, (0.70, 0.69, 0.65), SEAWALL_TOP, width=x1 - x0 + 0.02, height=0.12, depth=1.0, material="Concrete")
# the outfall: the tunnel's channel meets the beach through a gap with concrete wing walls
for sx in (-1, 1):
    wmesh("box", TUNNEL_X + sx * (TUNNEL_HALF + 0.3), 33.0, CONC_D, 1.2, width=0.5, height=2.6, depth=9.0, material="Concrete")

# ============================================================================ pillboxes dug into the top of the face
PB_H = PY - PB_FLOOR       # 2.4: the roof's underside is level with the plateau
for k, (x0, z0, x1, z1, cx) in enumerate(PILLBOXES):
    th = 0.5
    wall(x0, z0, x1, z0, PB_FLOOR, PB_H, th, BUNKER, doors=[(cx, 1.3, 2.1)])                                  # back, door to the trench
    wall(x0, z1 - th, x1, z1 - th, PB_FLOOR, PB_H, th, BUNKER, windows=[(cx - 2.0, 1.2, 1.2, 1.55), (cx, 1.4, 1.2, 1.55), (cx + 2.0, 1.2, 1.2, 1.55)])
    wall(x0, z0 + th, x0, z1 - th, PB_FLOOR, PB_H, th, BUNKER, windows=[((z0 + z1) / 2 + 0.6, 0.9, 1.2, 1.55)])
    wall(x1 - th, z0 + th, x1 - th, z1 - th, PB_FLOOR, PB_H, th, BUNKER, windows=[((z0 + z1) / 2 + 0.6, 0.9, 1.2, 1.55)])
    wblock(x0 - 0.25, PY, z0 - 0.25, x1 + 0.25, PY + 0.35, z1 + 0.45, BUNKER_D)                               # roof slab
    wmesh("box", (x0 + x1) / 2, z1 + 0.3, BUNKER_D, PB_FLOOR + 1.62, width=x1 - x0 + 0.4, height=0.12, depth=0.5)   # slit hood
    wmesh("box", (x0 + x1) / 2, (z0 + z1) / 2, (0.30, 0.30, 0.29), PB_FLOOR, width=x1 - x0 - 1.0, height=0.03, depth=z1 - z0 - 1.0, material="Concrete")
    # inside: ammo, a radio on a crate, a bench along the back wall; outside: sandbags along the trench lips
    ix0, iz0, ix1, iz1 = x0 + th, z0 + th, x1 - th, z1 - th
    place("Ammo Crate Stack", ix0 + 0.75, iz0 + 0.45, 0, tag="bunker")
    place("Ammo Crate Stack", ix1 - 0.75, iz0 + 0.45, 0, tag="bunker")
    place("Wooden Crate", ix1 - 0.55, iz1 - 1.6, 0, tag="bunker")
    place("Radio Set", ix1 - 0.55, iz1 - 1.6, 200, y=0.9, check=False, tag="bunker")
    place("Bench", cx - 1.6, iz0 + 0.35, 0, tag="bunker") if k != 1 else place("Plastic Chair", cx + 1.4, iz1 - 1.2, 160, tag="bunker")
    for j in range(3):
        place("Ammo Box", ix0 + 0.5 + j * 0.35, iz1 - 0.5, 15 * j, check=False, tag="bunker")
    place("Helipad Light", cx, iz0 + 0.1, 0, y=2.0, check=False, tag="bunker")
    for side in (-1, 1):
        for j in range(3):
            place("Sandbag Wall", cx + side * 1.3, z0 - 1.4 - j * 2.1, 90, tag="trench")
    for gx, gz in ((cx, z0 - 1.0), (cx, (z0 + z1) / 2), (cx, z0 - 6.0)):
        t.ops.append({"op": "Waypoint", "x": tk.R(gx), "z": tk.R(gz)})

# ============================================================================ the drainage tunnel
def tunnel_floor(z):
    pts = TUNNEL
    for (xa, za, ya), (xb, zb, yb) in zip(pts, pts[1:]):
        if (za - z) * (zb - z) <= 0:
            return ya + (yb - ya) * (z - za) / (zb - za)
    return pts[0][2] if z > pts[0][1] else pts[-1][2]


TUNNEL_ROOF = []
z = 14.0
while z > -6.0:
    za, zb = z, z - 2.0
    floor_hi = max(tunnel_floor(za), tunnel_floor(zb))
    surf = max(min(ground(TUNNEL_X - 2.6, zz), ground(TUNNEL_X + 2.6, zz)) for zz in (za, (za + zb) / 2, zb))
    y0 = floor_hi + 2.45
    if surf - y0 >= 0.25:
        wblock(TUNNEL_X - TUNNEL_HALF - 0.6, y0, zb, TUNNEL_X + TUNNEL_HALF + 0.6, surf, za, (0.42, 0.38, 0.30))
        TUNNEL_ROOF.append((zb, za, y0, surf))
    z = zb
# the culvert's concrete lining (cosmetic), stepped every metre, and amber lamps along the walls
for k in range(int(36.5 - (-13.0))):
    zc = 36.0 - k
    fy = tunnel_floor(zc)
    wmesh("box", TUNNEL_X, zc, (0.36, 0.36, 0.35), fy - 0.02, width=TUNNEL_HALF * 2 + 0.1, height=0.05, depth=1.02, material="Concrete")
    roofed = any(zb <= zc <= za for zb, za, _, _ in TUNNEL_ROOF)
    hgt = 2.45 if roofed else max(0.8, min(2.45, ground(TUNNEL_X + 2.6, zc) - fy))
    for sx in (-1, 1):
        wmesh("box", TUNNEL_X + sx * (TUNNEL_HALF - 0.06), zc, (0.48, 0.47, 0.44), fy, width=0.12, height=hgt, depth=1.02, material="Concrete")
    if roofed and k % 5 == 2:
        place("Helipad Light", TUNNEL_X - TUNNEL_HALF + 0.12, zc, 90, y=2.0, check=False, tag="tunnel")
for zc in range(30, -14, -4):
    t.ops.append({"op": "Waypoint", "x": TUNNEL_X, "z": float(zc)})
# the grate and railings where the tunnel comes up in the base
for sx in (-1, 1):
    wmesh("box", TUNNEL_X + sx * (TUNNEL_HALF + 0.15), -7.0, (0.30, 0.30, 0.32), PY, width=0.06, height=1.0, depth=12.0)

# ============================================================================ the base: roads and pads (meshes on the plateau)
def slab(x0, z0, x1, z1, c, material="Concrete", h=0.04, y=PY):
    wmesh("box", (x0 + x1) / 2, (z0 + z1) / 2, c, y, width=round(abs(x1 - x0), 2), height=h, depth=round(abs(z1 - z0), 2), **({"material": material} if material else {}))


slab(13.0, -48.0, 19.0, -2.0, ASPH)                     # the main road from the gate to the HQ
slab(-34.0, -13.0, 13.0, -7.0, ASPH)                    # west to the motor pool
slab(19.0, -27.0, 62.0, -21.0, ASPH)                    # east to the fuel depot and the helipad
for zc in range(-46, -2, 5):
    slab(15.85, zc, 16.15, zc + 2.2, WHITE, None, h=0.05)
for xc in range(-32, 12, 5):
    slab(xc, -10.15, xc + 2.2, -9.85, WHITE, None, h=0.05)
slab(-24.0, -32.0, -4.0, -18.0, (0.56, 0.54, 0.50), "Tiles")     # the parade ground (busted)
slab(-52.0, -16.0, -34.0, -4.0, (0.40, 0.40, 0.39), "Concrete")  # the motor pool floor
wmesh("cylinder", 36.0, -38.0, (0.50, 0.50, 0.48), PY, width=13.0, height=0.06, depth=13.0, segments=32, material="Concrete")   # helipad
wmesh("torus", 36.0, -38.0, YELLOW, PY + 0.06, radius=5.2, inner=0.12, segments=32, detail=4, height=0.01)
for (x, z, w, d) in [(34.6, -38.0, 0.5, 4.0), (37.4, -38.0, 0.5, 4.0), (36.0, -38.0, 2.3, 0.5)]:
    wmesh("box", x, z, WHITE, PY + 0.06, width=w, height=0.01, depth=d)
for k in range(8):
    a = k * math.pi / 4
    place("Helipad Light", 36.0 + math.cos(a) * 6.3, -38.0 + math.sin(a) * 6.3, 0, check=False, tag="helipad")
place("Wind Sock", 44.5, -43.5, 30, tag="helipad")

# ============================================================================ buildings
WALL_T = 0.3
BLD = []   # (x0, z0, x1, z1, roof top) for roof dressing and checks


def roofed_box(x0, z0, x1, z1, h, c, roof_c, doors_n=(), doors_s=(), doors_w=(), doors_e=(), win_n=(), win_s=(), win_w=(), win_e=(), pitched=True):
    """A walk-in building on the plateau: walls with doors and windows, a flat roof slab, a cosmetic pitched roof."""
    y0 = PY
    wall(x0, z0, x1, z0, y0, h, WALL_T, c, doors=doors_n, windows=win_n)
    wall(x0, z1 - WALL_T, x1, z1 - WALL_T, y0, h, WALL_T, c, doors=doors_s, windows=win_s)
    wall(x0, z0 + WALL_T, x0, z1 - WALL_T, y0, h, WALL_T, c, doors=doors_w, windows=win_w)
    wall(x1 - WALL_T, z0 + WALL_T, x1 - WALL_T, z1 - WALL_T, y0, h, WALL_T, c, doors=doors_e, windows=win_e)
    wblock(x0 - 0.25, y0 + h, z0 - 0.25, x1 + 0.25, y0 + h + 0.25, z1 + 0.25, roof_c)
    if pitched:
        along_x = (x1 - x0) >= (z1 - z0)
        w, d = (x1 - x0 + 0.6, (z1 - z0) / 2 + 0.3) if along_x else (z1 - z0 + 0.6, (x1 - x0) / 2 + 0.3)
        for sgn in (1, -1):
            if along_x:
                wmesh("wedge", (x0 + x1) / 2, (z0 + z1) / 2 + sgn * d / 2, roof_c, y0 + h + 0.25, width=w, height=1.4, depth=d, yaw=0.0 if sgn < 0 else 180.0)
            else:
                wmesh("wedge", (x0 + x1) / 2 + sgn * d / 2, (z0 + z1) / 2, roof_c, y0 + h + 0.25, width=w, height=1.4, depth=d, yaw=90.0 if sgn < 0 else 270.0)
    BLD.append((x0, z0, x1, z1, h + 0.25))


WIN = lambda c0, w=1.2: (c0, w, 1.0, 2.0)
# barracks (two long huts): doors at both ends and one on the south side, windows down the long walls
for bz0 in (-46.0, -32.0):
    bz1 = bz0 + 8.0
    roofed_box(-48.0, bz0, -30.0, bz1, 3.0, KHAKI, ROOF_G, doors_w=[(bz0 + 4.0, 1.4, 2.2)], doors_e=[(bz0 + 4.0, 1.4, 2.2)], doors_s=[(-39.0, 1.4, 2.2)],
               win_n=[WIN(-44.0), WIN(-39.0), WIN(-34.0)], win_s=[WIN(-44.0), WIN(-34.0)])
    # bunks along both long walls (heads to the wall), a locker between each pair, rifle racks, a stove and a table in the aisle
    for k, bx in enumerate([-46.4, -43.8, -36.0, -33.4]):
        place("Bunk Bed", bx, bz0 + WALL_T + 1.05, 180, tag="barracks")
        place("Bunk Bed", bx, bz1 - WALL_T - 1.05, 0, tag="barracks") if abs(bx + 39.0) > 2.5 else None
        place("Metal Lockers", bx + 1.3, bz0 + WALL_T + 0.3, 0, tag="barracks")
        place("Metal Lockers", bx + 1.3, bz1 - WALL_T - 0.3, 180, tag="barracks")
    place("Rifle Rack", -40.2, bz0 + WALL_T + 0.2, 0, tag="barracks")
    place("Rifle Rack", -37.8, bz0 + WALL_T + 0.2, 0, tag="barracks")
    place("Table", -39.0, bz0 + 4.0, 90, tag="barracks")
    place("Radio Set", -39.0, bz0 + 3.7, 90, y=0.78, check=False, tag="barracks")
    place("Plastic Chair", -39.8, bz0 + 4.6, 200, tag="barracks")
    place("Duffel Bag", -45.0, bz0 + 4.0, 30, tag="barracks"); place("Backpack", -34.5, bz0 + 3.6, 120, tag="barracks")
    place("Ceiling Fan", -43.0, bz0 + 4.0, 0, y=2.3, check=False, tag="barracks"); place("Ceiling Fan", -35.0, bz0 + 4.0, 0, y=2.3, check=False, tag="barracks")
    for wx in (-45.0, -33.0):
        t.ops.append({"op": "Waypoint", "x": wx, "z": bz0 + 4.0})

# the HQ (command post): operations room with the map table and radios, a radio room and the CO's office
HQ = (-6.0, -46.0, 12.0, -34.0)
roofed_box(*HQ, 3.4, (0.66, 0.64, 0.56), (0.30, 0.32, 0.30), doors_s=[(2.0, 1.6, 2.3)], doors_w=[(-40.0, 1.4, 2.3)], doors_e=[(-37.0, 1.4, 2.3)],
           win_s=[WIN(-3.0), WIN(7.5)], win_n=[WIN(-2.0), WIN(4.0)], win_w=[WIN(-36.5)], win_e=[WIN(-43.0)], pitched=False)
wall(5.4, -46.0 + WALL_T, 5.4, -34.0 - WALL_T, PY, 3.0, WALL_T, (0.74, 0.72, 0.66), doors=[(-36.5, 1.2, 2.2), (-43.5, 1.2, 2.2)])   # ops room | east rooms
wall(5.7, -40.0, 12.0 - WALL_T, -40.0, PY, 3.0, WALL_T, (0.74, 0.72, 0.66))                                                          # radio room | office
place("Map Table", -0.3, -41.0, 0, tag="hq")
for k, (x, z, yaw) in enumerate([(-5.2, -43.5, 90), (-5.2, -42.0, 90), (-5.2, -38.0, 90)]):
    place("Office Desk", x + 0.45, z, yaw, tag="hq")
    place("Radio Set", x + 0.45, z, yaw + 180, y=0.76, check=False, tag="hq")
    place("Office Chair", x + 1.35, z, yaw + 180, tag="hq")
place("Filing Cabinet", -1.0, -45.35, 0, tag="hq"); place("Filing Cabinet", -0.4, -45.35, 0, tag="hq"); place("Whiteboard", 2.4, -45.4, 0, tag="hq")
place("Server Rack", 4.6, -45.0, 270, tag="hq"); place("Water Cooler", 4.7, -35.0, 270, tag="hq"); place("Flag on a Pole", -5.2, -35.0, 0, tag="hq")
place("Server Rack", 11.1, -45.2, 90, tag="hq"); place("Server Rack", 11.1, -44.2, 90, tag="hq")
place("Office Desk", 8.6, -43.6, 180, tag="hq"); place("Radio Set", 8.3, -43.6, 0, y=0.76, check=False, tag="hq"); place("Radio Set", 9.0, -43.6, 20, y=0.76, check=False, tag="hq")
place("Office Chair", 8.6, -42.7, 0, tag="hq"); place("Desk Fan", 9.6, -43.4, 0, y=0.76, check=False, tag="hq")
place("Office Desk", 9.0, -36.4, 0, tag="hq"); place("Office Chair", 9.0, -37.3, 180, tag="hq"); place("Laptop", 9.0, -36.4, 180, y=0.76, check=False, tag="hq")
place("Filing Cabinet", 11.3, -38.0, 90, tag="hq"); place("Safe", 6.4, -39.1, 90, tag="hq"); place("Bookshelf", 11.4, -36.0, 90, tag="hq")
place("Ceiling Fan", -0.3, -41.0, 0, y=2.7, check=False, tag="hq")
for wx, wz in [(-1.0, -37.0), (8.6, -38.0), (8.6, -42.0), (2.0, -44.0)]:
    t.ops.append({"op": "Waypoint", "x": wx, "z": wz})

# the comms hut by the radar
COMMS = (48.0, -42.0, 56.0, -35.0)
roofed_box(*COMMS, 3.0, (0.60, 0.62, 0.58), (0.28, 0.30, 0.30), doors_s=[(51.0, 1.4, 2.2)], doors_w=[(-38.5, 1.2, 2.2)], win_e=[WIN(-38.5)], pitched=False)
place("Server Rack", 49.0, -41.2, 0, tag="comms"); place("Server Rack", 50.0, -41.2, 0, tag="comms"); place("Server Rack", 51.0, -41.2, 0, tag="comms")
place("Office Desk", 54.4, -40.8, 0, tag="comms"); place("Radio Set", 54.1, -40.8, 180, y=0.76, check=False, tag="comms"); place("Radio Set", 54.8, -40.8, 160, y=0.76, check=False, tag="comms")
place("Office Chair", 54.4, -39.9, 180, tag="comms"); place("Filing Cabinet", 55.3, -36.4, 270, tag="comms")
t.ops.append({"op": "Waypoint", "x": 52.0, "z": -38.0})

# the guard hut at the main gate
GUARD = (20.0, -7.5, 23.6, -3.5)
roofed_box(*GUARD, 2.6, (0.70, 0.68, 0.60), (0.28, 0.30, 0.28), doors_n=[(21.8, 1.0, 2.1)], win_w=[(-5.5, 1.6, 1.0, 1.9)], win_s=[(21.8, 1.6, 1.0, 1.9)], pitched=False)
place("Table", 21.8, -4.4, 0, tag="gate"); place("Radio Set", 21.6, -4.4, 180, y=0.78, check=False, tag="gate"); place("Plastic Chair", 21.8, -5.3, 180, tag="gate")

# the motor pool: an open shed (Hangar op, doors on its west and east ends)
MP = (-43.0, -10.0, 18.0, 12.0, 5.2)
t.ops.append({"op": "Hangar", "cx": MP[0], "cz": MP[1], "w": MP[2], "d": MP[3], "h": MP[4], "wall": tk.col(OLIVE_W), "roof": tk.col(ROOF_G), "doorsOnX": True})
_mx0, _mx1, _mz0, _mz1 = MP[0] - MP[2] / 2, MP[0] + MP[2] / 2, MP[1] - MP[3] / 2, MP[1] + MP[3] / 2
for r in [(_mx0, _mz0, _mx0 + 0.4, MP[1] - 3.3), (_mx0, MP[1] + 3.3, _mx0 + 0.4, _mz1), (_mx1 - 0.4, _mz0, _mx1, MP[1] - 3.3), (_mx1 - 0.4, MP[1] + 3.3, _mx1, _mz1),
          (_mx0, _mz0, _mx1, _mz0 + 0.4), (_mx0, _mz1 - 0.4, _mx1, _mz1)]:
    t.mark(t.occ, r); t.mark(t.walk, r); t.mark(t.tall, r)
    t.boxes.append((r[0], r[1], r[2], r[3], 0.0, MP[4], OLIVE_W))
BLD.append((_mx0, _mz0, _mx1, _mz1, MP[4] + 0.4))
place("Army Truck", -46.0, -13.6, 0, tag="motorpool"); place("Army Truck", -38.5, -6.4, 180, tag="motorpool")
place("Army Jeep", -49.5, -6.0, 0, tag="motorpool"); place("Fuel Bowser", -37.5, -13.8, 0, tag="motorpool")
place("Table", -51.2, -12.0, 90, tag="motorpool"); place("Toolbox", -51.2, -12.2, 30, y=0.78, check=False, tag="motorpool")
place("Engine Block", -51.0, -14.6, 0, tag="motorpool"); place("Tyre Stack", -43.0, -15.0, 0, tag="motorpool"); place("Oil Drum", -35.2, -5.0, 0, tag="motorpool")
place("Rusty Drum", -35.9, -4.9, 0, tag="motorpool"); place("Metal Shelving", -42.0, -4.8, 180, tag="motorpool")
for wx in (-48.0, -38.0):
    t.ops.append({"op": "Waypoint", "x": wx, "z": -10.0})

# the supply yard at the front: containers (hard cover facing the bluff), pallets and crates
for (x, z, ax, ci, y, op) in [(-21.0, -2.0, True, 0, 0.0, False), (-13.0, -2.5, True, 1, 0.0, True), (-21.0, -2.0, True, 2, 2.6, False),
                              (-5.5, -1.0, False, 3, 0.0, False), (-27.0, -4.0, False, 1, 0.0, False)]:
    t.ops.append({"op": "Container", "x": x, "z": z, "alongX": ax, "color": tk.col(CONTAINERS[ci]), **({"y": y} if y else {}), **({"open": True} if op else {})})
    hx, hz = (3.0, 1.25) if ax else (1.25, 3.0)
    if y < 1:
        r = (x - hx, z - hz, x + hx, z + hz)
        t.mark(t.occ, r); t.mark(t.walk, r); t.mark(t.tall, r)
        t.boxes.append((r[0], r[1], r[2], r[3], 0.0, 2.6, CONTAINERS[ci]))
        if op:
            t.walk[t._ij(x - hx + 0.4, z - hz + 0.3, x + hx - 0.4, z + hz - 0.3)[0]:t._ij(x - hx + 0.4, z - hz + 0.3, x + hx - 0.4, z + hz - 0.3)[1],
                   t._ij(x - hx + 0.4, z - hz + 0.3, x + hx - 0.4, z + hz - 0.3)[2]:t._ij(x - hx + 0.4, z - hz + 0.3, x + hx - 0.4, z + hz - 0.3)[3]] = 0

# the fuel depot: three tanks behind a sandbag revetment, explosive barrels in stacks, a pump, the bowser
for (x, z) in [(48.0, -15.0), (55.0, -15.5)]:
    t.ops.append({"op": "Tank", "x": x, "z": z, "r": 2.3, "h": 4.2, "color": tk.col((0.55, 0.58, 0.50))})
    r = (x - 2.3, z - 2.3, x + 2.3, z + 2.3)
    t.mark(t.occ, r); t.mark(t.walk, r); t.mark(t.tall, r)
    t.boxes.append((r[0], r[1], r[2], r[3], 0.0, 4.2, (0.55, 0.58, 0.50)))
place("Old Town Fuel Pump", 51.5, -19.4, 0, tag="depot"); place("Fuel Bowser", 44.0, -19.5, 0, tag="depot")
EXPLOSIVE = []
for (cx, cz, n) in [(46.0, -10.6, 4), (58.0, -11.0, 3), (52.0, -11.6, 3), (59.5, -18.8, 3), (30.0, -18.0, 2)]:
    for k in range(n):
        x, z = cx + (k % 2) * 0.72 - 0.36, cz + (k // 2) * 0.72
        if place("Explosive Barrel", x, z, 0, tag="explosive"):
            EXPLOSIVE.append((x, z))
for (x, z, yaw) in [(44.0, -8.4, 0), (48.0, -8.4, 0), (56.0, -8.4, 0), (60.5, -13.0, 90)]:
    place("Sandbag Wall", x, z, yaw, tag="depot")
place("Gas Cylinders", 61.0, -16.0, 90, tag="depot"); place("No Parking Sign", 43.0, -12.0, 90, tag="depot")

# ============================================================================ the base: towers, lights, the front line, the fence
TOWERS = [(-28.0, 2.0, 5.0, -1), (31.0, 2.5, 5.0, -1), (-60.0, -34.0, 5.5, 1)]
for (x, z, h, sd) in TOWERS:
    t.ops.append({"op": "Watchtower", "x": x, "z": z, "height": h, "color": tk.col(WOOD), "stairDir": sd})
    r = (x - 1.6, z - 1.6, x + 1.6, z + 1.6)
    t.mark(t.occ, r)
    for lx in (-1, 1):
        for lz in (-1, 1):
            q = (x + lx * 1.3 - 0.2, z + lz * 1.3 - 0.2, x + lx * 1.3 + 0.2, z + lz * 1.3 + 0.2)
            t.mark(t.walk, q); t.mark(t.tall, q)
    run = (h + 0.25) / 0.4 * 0.45
    sr = (x - 0.6, z + 1.5, x + 0.6, z + 1.5 + run) if sd > 0 else (x - 0.6, z - 1.5 - run, x + 0.6, z - 1.5)
    t.mark(t.occ, sr); t.mark(t.walk, sr)
    t.boxes.append((x - 1.5, z - 1.5, x + 1.5, z + 1.5, h, h + 1.25, WOOD))
    place("Searchlight", x + 0.9, z + 0.9 * (1 if sd < 0 else -1), 0 if sd < 0 else 180, y=h + 0.25, check=False, tag="tower")
    t.ops.append({"op": "Waypoint", "x": x, "z": z + (-2.5 - run if sd < 0 else 2.5 + run)})

for (x, z, yaw) in [(-52.0, -2.0, 30), (8.0, -6.0, 180), (40.0, -30.0, 0), (-22.0, -36.0, 90), (24.0, -44.0, 270)]:
    place("Floodlight Tower", x, z, yaw, tag="lights")
for (x, z, yaw) in [(-54.0, -1.0, 90), (10.0, -8.0, 0), (42.5, -31.0, 0), (58.0, -32.0, 90)]:
    place("Running Generator", x, z, yaw, tag="lights")
place("Diesel Generator", 46.0, -33.5, 0, tag="lights")
place("Radar Dish", 60.0, -26.0, 0, tag="comms"); place("Sweeping Satellite Dish", 46.5, -44.5, 160, tag="comms")
place("Radio Mast", 57.5, -44.0, 0, tag="comms")
place("Radio Mast", 10.5, -45.0, 0, y=3.65, check=False, tag="roof"); place("Satellite Dish", -4.0, -44.5, 160, y=3.65, check=False, tag="roof")
place("Flag on a Pole", -14.0, -25.0, 0, tag="parade")

# the main gate: a boom across the road, sandbag nests either side, a searchlight
place("Boom Gate", 13.4, -2.2, 0, check=False, tag="gate")
place("Sandbag Nest", 9.0, -1.0, 0, tag="gate"); place("Sandbag Nest", 25.5, -1.0, 0, tag="gate")
place("Searchlight", 11.6, -4.6, 160, tag="gate"); place("Direction Sign", 19.8, -9.5, 270, tag="gate")
# the front line along the top of the bluff: sandbag nests and walls facing the sea, barbed wire (looks only) in front
FRONT_NESTS = [(-56.0, None), (-32.0, None), (-17.0, None), (-6.0, None), (35.0, None), (46.0, None), (58.0, None)]
for x, _ in FRONT_NESTS:
    zt = float(bt.interp(TOP, x)) - 3.4
    place("Sandbag Nest", x, zt, 0, tag="front")
for x in range(-54, 58, 3):
    zt = float(bt.interp(TOP, x)) - 1.4
    if any(abs(x - px) < 2.6 for px in (-46, -38, 24, 52, 40)) or 2 < x < 20:
        continue
    place("Barbed Wire Fence", x + 1.5, zt, 0, check=False, tag="wire")
for x in range(-52, 60, 7):
    zt = float(bt.interp(TOP, x)) - 5.2
    place("Sandbag Wall", x, zt, 0, tag="front")

# the perimeter fence round the back of the base (chain link, just inside the outline)
_pts = t.outline
_arc = [0.0]
for i in range(1, len(_pts) + 1):
    _arc.append(_arc[-1] + math.hypot(_pts[i % len(_pts)][0] - _pts[i - 1][0], _pts[i % len(_pts)][1] - _pts[i - 1][1]))
_back = [i for i, p in enumerate(_pts) if p[1] < float(bt.interp(TOP, p[0])) - 7.0]
# the back of the base is one run of the outline: find where it starts (the point after a gap)
_bs = set(_back)
_start = next(i for i in _back if (i - 1) % len(_pts) not in _bs)
_run = []
i = _start
while i in _bs:
    _run.append(i)
    i = (i + 1) % len(_pts)
FENCE = tk.edge_run(t, _arc[_run[0]] + 2.0, _arc[_run[-1]] - 2.0, "fence", rng, inset=1.1)

# the mess tent on the parade ground, latrines, benches
TENT = (-22.0, -31.0, -12.0, -24.0)
for k in range(5):
    x = TENT[0] + k * (TENT[2] - TENT[0]) / 4
    for z in (TENT[1], TENT[3]):
        wmesh("cylinder", x, z, WOOD, PY, width=0.12, height=2.4, depth=0.12, segments=6)
for sgn in (1, -1):
    wmesh("wedge", (TENT[0] + TENT[2]) / 2, (TENT[1] + TENT[3]) / 2 + sgn * 1.85, (0.40, 0.42, 0.28), PY + 2.4, width=10.6, height=1.3, depth=3.7, yaw=0.0 if sgn < 0 else 180.0)
for k in range(3):
    tx = TENT[0] + 2.0 + k * 3.0
    place("Table", tx, -27.5, 0, tag="tent"); place("Bench", tx, -26.7, 0, tag="tent"); place("Bench", tx, -28.3, 180, tag="tent")
    place("Stacked Plates", tx + 0.2, -27.5, 0, y=0.78, check=False, tag="tent")
place("Kitchen Counter", -21.0, -24.6, 180, tag="tent"); place("Cooker", -19.6, -24.6, 180, tag="tent"); place("Fridge", -18.6, -24.6, 180, tag="tent")
for k in range(3):
    place("Portable Toilet", -27.0, -17.0 - k * 1.3, 90, tag="latrine")
place("Bench", -8.0, -19.0, 0, tag="parade"); place("Bench", -8.0, -31.0, 180, tag="parade")

# ============================================================================ the beach
def dz_at(x, z):
    return float(shore_z(x)) - z


def scatter(names, area, count, seed, gap=1.0, yaws=None, ok=None, lanes=True, tag="scatter", tries=60):
    """Solid cover scattered over area (x0, z0, x1, z1), off lanes, keep-outs and everything solid; ok(x, z) filters spots."""
    r_ = random.Random(seed)
    n = 0
    for _ in range(count * tries):
        if n >= count:
            break
        x = r_.uniform(area[0], area[2]); z = r_.uniform(area[1], area[3])
        if ok and not ok(x, z):
            continue
        name = r_.choice(names)
        yaw = r_.choice(yaws) if yaws else r_.uniform(0, 360)
        if place(name, x, z, yaw, lanes=lanes, gap=gap, tag=tag):
            n += 1
    return n


# grounded landing craft (bows to the land, ramps down) and a beached fishing boat: Blue lands behind them
WRECKS = [("Landing Craft Wreck", -24.0, 49.5, 90.0), ("Landing Craft Wreck", 18.0, 51.0, 98.0), ("Landing Craft Wreck", -51.0, 45.5, 76.0),
          ("Beached Fishing Boat", 41.0, 47.5, 20.0)]
for name, x, z, yaw in WRECKS:
    assert place(name, x, z, yaw, tag="wreck"), name
    t.ops.append({"op": "Waypoint", "x": x, "z": z})
# obstacles in bands up the beach: stake ramps at the waterline, hedgehogs on the wet sand, dragon's teeth below the
# seawall, concertina wire (looks only) between them
NB = {}
NB["stakes"] = scatter(["Beach Stake Ramp"], (-80, 44, 80, 64), 16, 501, gap=1.2, yaws=[80, 90, 100, 270, 260], ok=lambda x, z: 1.5 < dz_at(x, z) < 5.5)
NB["hedgehogs"] = scatter(["Czech Hedgehog"], (-80, 36, 80, 62), 62, 502, gap=1.3, ok=lambda x, z: 5.5 < dz_at(x, z) < 15.0)
NB["teeth"] = scatter(["Dragons Tooth"], (-60, 30, 66, 50), 22, 503, gap=1.0, yaws=[0, 90], ok=lambda x, z: 18.0 < dz_at(x, z) < 20.6)
NB["sandbags"] = scatter(["Sandbag Wall", "Sandbag Wall", "Sandbag Nest"], (-70, 36, 70, 60), 22, 504, gap=1.0, yaws=[170, 180, 190], ok=lambda x, z: 8.0 < dz_at(x, z) < 18.0)
NB["flotsam"] = scatter(["Log", "Log", "Wooden Crate", "Upturned Dinghy", "Ammo Crate Stack", "Oil Drum", "Rusty Drum", "Tyre Stack"], (-80, 36, 80, 64), 34, 505,
                        gap=1.0, ok=lambda x, z: 1.0 < dz_at(x, z) < 19.0)
_wire = 0
_rw = random.Random(506)
for x in range(-66, 70, 4):
    if _rw.random() < 0.35:
        continue
    z = float(shore_z(x)) - 16.2 + _rw.uniform(-0.8, 0.8)
    if place("Concertina Wire", x + 2.0, z, _rw.uniform(-8, 8), tag="wire"):
        _wire += 1
place("Life Ring Post", -64.0, 52.5, 180, tag="beach"); place("Direction Sign", 7.5, 41.0, 0, tag="beach")
for k, (x, z) in enumerate([(-36.0, 50.0), (2.0, 47.0), (30.0, 50.0), (-8.0, 43.5), (54.0, 44.0), (-64.0, 42.0)]):
    wmesh("cylinder", x, z, (0.46, 0.40, 0.30), ground(x, z) - 0.02, width=3.4 + k % 3, height=0.06, depth=3.0 + k % 2, segments=12, material="Dirt")   # shell craters

# ============================================================================ the dunes and the bluff
NB["dune_teeth"] = scatter(["Dragons Tooth", "Czech Hedgehog", "Concrete Block"], (-60, 14, 75, 36), 30, 511, gap=1.1, yaws=[0, 90],
                           ok=lambda x, z: dz_at(x, z) > 24.0 and z > float(bt.interp(TOP, x)) + float(bt.interp(FACE, x)) + 1.5)
NB["dune_cover"] = scatter(["Sandbag Wall", "Sandbag Nest", "Log", "Rubble Pile", "Picket Fence", "Burnt Out Car", "Wooden Crate", "Ammo Crate Stack"],
                           (-62, 12, 80, 36), 42, 512, gap=1.1, yaws=[0, 90, 180, 270, 170, 190],
                           ok=lambda x, z: dz_at(x, z) > 23.0 and z > float(bt.interp(TOP, x)) + float(bt.interp(FACE, x)) + 1.0)
NB["dune_slope"] = scatter(["Sandbag Wall", "Sandbag Nest", "Czech Hedgehog", "Log", "Picket Fence", "Concrete Block", "Rubble Pile"], (58, -2, 86, 30), 32, 513,
                           gap=1.1, yaws=[0, 90, 180, 270], ok=lambda x, z: x > 60 and z > float(bt.interp(TOP, x)) + 2.0)
place("Camo Net", -2.0, 22.0, 0, check=False, tag="dunes"); place("Sandbag Nest", -2.0, 22.4, 180, tag="dunes")
place("Camo Net", 58.0, 22.0, 90, check=False, tag="dunes")
# the hairpin: a burnt-out truck, sandbags and a road sign
place("Burnt Out Car", -24.5, 17.0, 60, tag="hairpin"); place("Sandbag Nest", -16.5, 14.0, 200, tag="hairpin")
place("Direction Sign", -14.0, 18.5, 120, tag="hairpin"); place("Ammo Crate Stack", -23.5, 12.5, 90, tag="hairpin")
# sandbag walls along both sides of the road's first leg (the beach side's way up), a gap every few metres
for (xa, za, _), (xb, zb, _) in zip(ROAD1, ROAD1[1:]):
    L = math.hypot(xb - xa, zb - za); ux, uz = (xb - xa) / L, (zb - za) / L
    for k in range(1, int(L / 4.5)):
        for side in (-1, 1):
            if (k + (side > 0)) % 2:
                continue
            px, pz = xa + ux * k * 4.5 - uz * side * 3.6, za + uz * k * 4.5 + ux * side * 3.6
            place("Sandbag Wall", px, pz, yaw_along(ux, uz), tag="road")
# rocks at the foot of the face and in the ravine (real cover: boxes)
for k, (x, z, size) in enumerate([(-68.0, 20.0, 2.2), (-58.0, 18.0, 1.8), (-47.0, 21.0, 1.6), (-6.0, 15.0, 1.7), (30.0, 16.0, 1.8), (46.0, 18.5, 1.5),
                                  (-64.0, 6.0, 1.4), (-60.5, 0.0, 1.2), (66.0, 12.0, 1.6), (76.0, 4.0, 1.8)]):
    if t.free((x - size, z - size, x + size, z + size), 0.3) and not t.on_lane((x - size, z - size, x + size, z + size)) and not t.in_keepout((x - size, z - size, x + size, z + size)):
        t.ops.append({"op": "Rocks", "x": x, "z": z, "size": size, "color": tk.col((0.50, 0.47, 0.42)), "seed": 30 + k})
        r = (x - size * 0.8, z - size * 0.8, x + size * 0.8, z + size * 0.8)
        t.mark(t.occ, r); t.mark(t.walk, r); t.mark(t.tall, r)
        t.boxes.append((r[0], r[1], r[2], r[3], 0.0, size, (0.5, 0.47, 0.42)))
NB["ravine"] = scatter(["Log", "Tree Stump", "Rubble Pile", "Sandbag Wall"], (-75, -8, -52, 34), 18, 514, gap=1.0,
                       ok=lambda x, z: abs(ground(x, z) - min(ground(x + 1, z), ground(x - 1, z))) < 0.6)

# ============================================================================ the base: scattered cover and kit
_onplat = lambda x, z: z < float(bt.interp(TOP, x)) - 2.0 and abs(ground(x, z) - PY) < 0.05
NB["base_yard"] = scatter(["Ammo Crate Stack", "Wooden Crate", "Pallet Stack", "Sandbag Wall", "Oil Drum", "Small Crate", "Cable Spool", "Concrete Block"],
                          (-30, -8, 12, 6), 18, 521, gap=1.0, yaws=[0, 90], ok=_onplat)
NB["base_rear"] = scatter(["Ammo Crate Stack", "Wooden Crate", "Pallet Stack", "Oil Drum", "Sandbag Wall", "Tyre Stack", "Army Jeep", "Camo Net"],
                          (-56, -52, 66, -14), 34, 522, gap=1.2, yaws=[0, 90, 180, 270], ok=_onplat)
NB["base_east"] = scatter(["Sandbag Wall", "Ammo Crate Stack", "Oil Drum", "Wooden Crate", "Concrete Block", "Fuel Bowser"], (24, -8, 66, 6), 14, 523, gap=1.1,
                          yaws=[0, 90], ok=_onplat)
NB["base_west"] = scatter(["Sandbag Wall", "Ammo Crate Stack", "Army Truck", "Tyre Stack", "Oil Drum", "Pallet Stack", "Wooden Crate"], (-74, -40, -50, 6), 14, 524,
                          gap=1.1, yaws=[0, 90, 180, 270], ok=_onplat)
for (x, z) in [(-58.0, -16.0), (-24.0, -14.5), (28.0, -32.0), (62.0, -38.0), (-6.0, -50.5)]:
    place("Portable Generator" if x > 0 else "Diesel Generator", x, z, 0, tag="kit")

# ============================================================================ spawns and waypoints
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
t.ops.append({"op": "AutoWaypoints", "spacing": 7})
EXTRA_WP = [(-63.0, 14.0), (-60.0, 3.0), (-50.0, 13.0), (-44.0, 15.5), (-20.0, 15.0), (-8.0, 7.5), (20.0, 2.0), (2.0, 26.0), (40.0, 28.0),
            (70.0, 14.0), (64.0, 2.0), (-13.0, -2.0), (-21.0, -6.0), (46.0, -12.5), (52.0, -19.0), (-17.0, -27.5), (36.0, -38.0),
            (OVERLOOK[0], OVERLOOK[1]), (DUNE_OP[0], DUNE_OP[1] - 2.0), (-46.0, 2.0), (-62.5, 10.0)]
for x, z in EXTRA_WP:
    t.ops.append({"op": "Waypoint", "x": x, "z": z})

# ============================================================================ cosmetic clutter (the game's Clutter op runs last and keeps out of everything)
BEACH_BITS = ["Plank", "Rope Coil", "Fishing Net Pile", "Bottle", "Litter", "Ammo Box", "Tyre", "Plank", "Plastic Bag", "Car Wheel", "Rubble", "Rope Coil"]
DUNE_BITS = ["Plank", "Ammo Box", "Rubble", "Bottle", "Litter", "Cinder Blocks", "Tyre", "Loose Bricks", "Plank"]
BASE_BITS = ["Litter", "Newspaper", "Soda Cans", "Cardboard Box Small", "Flattened Cardboard", "Ammo Box", "Bottle", "Plank", "Cinder Blocks"]
YARD_BITS = ["Ammo Box", "Ammo Box", "Plank", "Cardboard Box Small", "Car Wheel", "Tyre", "Toolbox", "Bucket", "Paint Cans", "Cinder Blocks", "Rubble"]
for k, x0 in enumerate(range(-84, 84, 24)):
    t.clutter(BEACH_BITS, x0, 38.0, x0 + 24, 64.0, 170, 601 + k)
    t.clutter(DUNE_BITS, x0, 12.0, x0 + 24, 38.0, 120, 611 + k)
for k, (x0, z0, x1, z1, n) in enumerate([(-74, -58, -20, -14, 200), (-20, -58, 30, -14, 220), (30, -50, 72, -6, 200), (-60, -14, 0, 8, 200), (0, -14, 40, 8, 160)]):
    t.clutter(BASE_BITS if k % 2 else YARD_BITS, x0, z0, x1, z1, n, 621 + k)
t.clutter(["Ammo Box", "Plank", "Rubble", "Cinder Blocks", "Loose Bricks"], -75, -10, -52, 36, 70, 631)                 # the ravine
t.clutter(["Litter", "Plank", "Bottle", "Rubble"], TUNNEL_X, 34.0, TUNNEL_X, -12.0, 40, 632, width=2.4)   # the tunnel
t.clutter(["Flattened Cardboard", "Plank", "Ammo Box", "Cardboard Box Small", "Litter"], -47.5, -45.5, -30.5, -24.5, 40, 633)   # barracks floors

# ============================================================================ foliage
INNER = [[tk.R(p[0]), tk.R(p[1])] for p in t.outline[::6]]


def band(z_lo_fn, z_hi_fn, x0, x1, step=4.0):
    """A polygon between two curves z_lo(x) < z_hi(x) for x0..x1 (z grows to the south)."""
    xs = [x0 + k * step for k in range(int((x1 - x0) / step) + 1)]
    return [[tk.R(x), tk.R(z_lo_fn(x))] for x in xs] + [[tk.R(x), tk.R(z_hi_fn(x))] for x in reversed(xs)]


foot = lambda x: float(bt.interp(TOP, x)) + float(bt.interp(FACE, x))
DUNES = band(lambda x: foot(x) - 1.0, lambda x: float(shore_z(x)) - 21.0, -92, 92)
UPPER_BEACH = band(lambda x: float(shore_z(x)) - 21.0, lambda x: float(shore_z(x)) - 12.0, -92, 92)
FACE_BAND = band(lambda x: float(bt.interp(TOP, x)) - 1.0, lambda x: foot(x) + 0.5, -92, 92)
PLATEAU = band(lambda x: -70.0, lambda x: float(bt.interp(TOP, x)) - 0.5, -92, 92)
t.region("grass", 3.6, polygon=PLATEAU, patchiness=0.5, color="#6f8648", scale=[0.7, 1.2])
t.region("shrub", 0.35, polygon=PLATEAU, patchiness=0.6, color="#5d7038")
t.region("grass", 6.5, polygon=DUNES, patchiness=0.55, color="#9aa45e", scale=[0.9, 1.7])
t.region("shrub", 0.9, polygon=DUNES, patchiness=0.6, color="#7f8a4a")
t.region("bush", 0.12, polygon=DUNES, patchiness=0.5, color="#6a7a40", scale=[0.8, 1.3])
t.region("grass", 1.2, polygon=UPPER_BEACH, patchiness=0.75, color="#a6aa68", scale=[0.7, 1.3])
t.region("grass", 4.0, polygon=FACE_BAND, patchiness=0.6, color="#7c8a4c")
t.region("shrub", 1.2, polygon=FACE_BAND, patchiness=0.5, color="#667a3c")
t.region("bush", 0.35, polygon=FACE_BAND, patchiness=0.5, color="#4e6230", scale=[0.9, 1.6])
RAVINE_POLY = [[-76, 36], [-66, 36], [-56, 12], [-52, -10], [-62, -10], [-68, 10]]
t.region("bush", 0.5, polygon=RAVINE_POLY, patchiness=0.4, color="#4a5e2c", scale=[0.9, 1.7])
t.region("shrub", 1.4, polygon=RAVINE_POLY, color="#5c6e36")
for pts, w in LANES[:5]:
    t.path(pts, max(1.6, w - 1.0), density=0.08)
FOL = {"seed": 9, "surfaces": {"Tiles": 0.1, "Plain": 0.1}, "paths": t.paths, "regions": t.regions, "items": t.items}
# trees: pines and broadleaves round the back of the base and on the headland side, a few on the dunes
_rt = random.Random(707)
TREE_ZONES = [((-80, -40, -56, -6), 16, "pine"), ((-58, -58, -20, -48), 8, "pine"), ((26, -60, 50, -50), 6, "pine"), ((56, -40, 76, -26), 8, "pine"),
              ((-66, 4, -50, 12), 4, "tree"), ((66, 6, 88, 28), 8, "tree"), ((-86, 10, -70, 40), 8, "tree"), ((-20, -60, 0, -52), 4, "tree"),
              ((58, -26, 72, -10), 6, "tree"), ((-70, -50, -60, -40), 4, "pine")]
for (x0, z0, x1, z1), n, kind in TREE_ZONES:
    placed = 0
    for _ in range(n * 30):
        if placed >= n:
            break
        x = _rt.uniform(x0, x1); z = _rt.uniform(z0, z1)
        if t.on_lane((x - 0.5, z - 0.5, x + 0.5, z + 0.5)):
            continue
        h = _rt.uniform(7.0, 11.0) if kind == "pine" else _rt.uniform(5.5, 8.5)
        if t.tree(x, z, h, kind):
            placed += 1

# ============================================================================ the map file
for x, z in BLUE_SP + RED_SP + FFA_SP + [(h[0], h[1]) for h in HILLS] + FLAGS:
    assert t.inside(x, z, 1.5), ("objective too near the edge", x, z)
mp = {
    "format": "call-of-slop-map/1",
    "name": "Beachhead",
    "description": "A beach that leads up into a military base: land among the wrecks and tank traps, then take the bluff by the road, the ravine, the dunes or the drainage tunnel.",
    "author": "Call of Slop",
    "order": 409,
    "maxPlayers": 16,
    "sky": "#a9c4dc",
    "skybox": {"preset": "clear", "sun": {"time": 9.3, "latitude": 42}, "clouds": {"coverage": 0.4, "speed": 9, "heading": 70},
               "haze": {"color": "#dfe2d6", "strength": 0.3}},
    "boundary": {"shape": "smooth", "points": [list(p) for p in CTRL], "noise": NOISE, "edge": "none", "groundMargin": 20},
    "terrain": {"cell": bt.CELL, "margin": MARGIN,
                "surface": {"grass": "#6b7f42", "dirt": "#7a6448", "rock": "#7a766c", "sand": "#c8b98c", "sandBelow": 5.2, "variation": 0.08},
                "features": T.features},
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": t.ops,
    "foliage": FOL,
}


def dump(o, ind=0):
    pad = "  " * ind
    if isinstance(o, dict) and ind <= 1 and any(isinstance(v, (dict, list)) for v in o.values()):
        items = [f'{pad}  "{k}": {dump(v, ind + 1)}' for k, v in o.items()]
        return "{\n" + ",\n".join(items) + "\n" + pad + "}"
    if isinstance(o, list) and ind <= 2 and o and isinstance(o[0], dict):
        return "[\n" + ",\n".join(pad + "  " + json.dumps(v, separators=(", ", ": ")) for v in o) + "\n" + pad + "]"
    return json.dumps(o, separators=(",", ":") if ind >= 3 else (", ", ": "))


with open(OUT_JSON, "w") as f:
    f.write(dump(mp) + "\n")

# ============================================================================ report and checks
P = t.P
props = t.props
nsolid = sum(1 for p in props if P[p[0]]["solid"])
nbrk = sum(1 for p in props if P[p[0]]["brk"])
nblock = sum(1 for o in t.ops if o["op"] == "Block")
by_cat = {}
for p in props:
    k = by_cat.setdefault(P[p[0]]["cat"], [0, 0])
    k[0 if P[p[0]]["solid"] else 1] += 1
nclutter = sum(o["count"] for o in t.ops if o["op"] == "Clutter")
print(f"area {t.area():.0f} m2, outline {len(CTRL)} control points; terrain {T.nx}x{T.nz} samples, {len(T.features)} features; "
      f"blocks {nblock}, other box ops {sum(1 for o in t.ops if o['op'] in ('Container', 'Hangar', 'Tank', 'Watchtower', 'Rocks'))}, trees {len(t.items)}; meshes {t.meshes}; "
      f"hand props {len(props)} (solid {nsolid}, breakable {nbrk}); clutter up to {nclutter}; file {os.path.getsize(OUT_JSON) // 1024} KB")
print("by category (solid/cosmetic):", ", ".join(f"{c} {v[0]}/{v[1]}" for c, v in sorted(by_cat.items())))
print("scatter:", NB, "wire", _wire, "explosive barrels", len(EXPLOSIVE), "fence", FENCE)

# walking: the terrain graph with everything that blocks walking at body height
X_, Z_ = np.meshgrid(T.X, T.Z)
ok = SD(X_, Z_) > 0.5
for j in range(T.nz):
    zz = T.oz + j * T.cell
    if zz < t.z0 or zz >= t.z1:
        continue
    i0 = int(math.ceil((t.x0 - T.ox) / T.cell)); i1 = int(math.floor((t.x1 - T.ox) / T.cell))
    gi = int((zz - t.z0) / t.res)
    if gi >= t.nz:
        continue
    for i in range(max(0, i0), min(T.nx, i1)):
        xx = T.ox + i * T.cell
        gj = int((xx - t.x0) / t.res)
        if 0 <= gj < t.nx and t.walk[gi, gj]:
            ok[j, i] = False
WG = bt.WalkGrid(T, ok)


def avg_walk(sources, goal, kind):
    f = WG.fields(sources, kind)
    gi = WG.node(*goal)
    v = f[:, gi]
    return float(np.mean(v))


_fb = WG.fields(BLUE_SP[:1], "len")[0]
print(f"walkable cells {WG.n}, reachable from a Blue spawn {int(np.isfinite(_fb).sum())} ({100 * np.isfinite(_fb).mean():.1f}%)")
print("balance (Blue/Red walk from their team spawns, metres; walking time in brackets):")
worst = 0.0
for name, goal in [(f"hill {i + 1}", (h[0], h[1])) for i, h in enumerate(HILLS)] + [("enemy flag", None)]:
    if goal is None:
        b, r = avg_walk(BLUE_SP, FLAGS[1], "len"), avg_walk(RED_SP, FLAGS[0], "len")
        bt_, rt_ = avg_walk(BLUE_SP, FLAGS[1], "time"), avg_walk(RED_SP, FLAGS[0], "time")
    else:
        b, r = avg_walk(BLUE_SP, goal, "len"), avg_walk(RED_SP, goal, "len")
        bt_, rt_ = avg_walk(BLUE_SP, goal, "time"), avg_walk(RED_SP, goal, "time")
    d = (b - r) / max(b, r)
    worst = max(worst, abs(d))
    print(f"  {name:10s} {b:6.1f} / {r:6.1f} m ({d * 100:+.1f}%)   time {bt_:6.1f} / {rt_:6.1f}")


# sight lines between the team spawns at eye height (terrain and tall boxes and props)
def sees(a, b):
    ya = ground(*a) + 1.6; yb = ground(*b) + 1.6
    L = math.hypot(b[0] - a[0], b[1] - a[1]); n = max(1, int(L / 0.25))
    for s_ in range(1, n):
        f_ = s_ / n
        x = a[0] + (b[0] - a[0]) * f_; z = a[1] + (b[1] - a[1]) * f_; y = ya + (yb - ya) * f_
        g = ground(x, z)
        if g > y:
            return False
        if t.inside(x, z, 0.0):
            i = int((z - t.z0) / t.res); j = int((x - t.x0) / t.res)
            if t.tall[i, j] and y - g < 2.4:
                return False
    return True


vis = [(a, b) for a in BLUE_SP for b in RED_SP if sees(a, b)]
print("blue-red spawn lines of sight:", len(vis), vis[:3])
bad = []
for name, x, z, yaw, y, tag in props:
    if P[name]["solid"] and (y or 0) < 0.6:
        r = t.foot(name, x, z, yaw)
        for (kx, kz, kr) in [(a, b, 1.0) for a, b in BLUE_SP + RED_SP + FFA_SP] + [(h[0], h[1], h[2]) for h in HILLS] + [(fx, fz, 2.0) for fx, fz in FLAGS]:
            dx = max(r[0] - kx, 0, kx - r[2]); dz = max(r[1] - kz, 0, kz - r[3])
            if math.hypot(dx, dz) < kr:
                bad.append((name, round(x, 1), round(z, 1)))
print("solid props on objectives:", bad[:5])
for (ex, ez) in EXPLOSIVE:
    for sx, sz in BLUE_SP + RED_SP + FFA_SP:
        if math.hypot(ex - sx, ez - sz) < 10.5:
            print("WARN explosive near a spawn", ex, ez, sx, sz)

if OUT_PNG:
    marks = [("dot", x, z, 0.8, (40, 80, 230)) for x, z in BLUE_SP] + [("dot", x, z, 0.8, (230, 40, 40)) for x, z in RED_SP]
    marks += [("dot", x, z, 0.8, (240, 240, 240)) for x, z in FFA_SP] + [("circle", x, z, r, (255, 230, 0)) for x, z, r in HILLS]
    marks += [("circle", x, z, 1.2, (255, 255, 255)) for x, z in FLAGS]
    t.preview(OUT_PNG, marks, scale=5)
    # put the terrain under it: height tint, slopes (orange: slow, red: too steep), the sea
    from PIL import Image
    im = np.array(Image.open(OUT_PNG).convert("RGB")).astype(np.int32)
    hh, ww = im.shape[:2]
    xs = t.x0 + (np.arange(ww) + 0.5) / 5; zs = t.z0 + (np.arange(hh) + 0.5) / 5
    ii = np.clip(np.round((xs - T.ox) / T.cell).astype(int), 0, T.nx - 1); jj = np.clip(np.round((zs - T.oz) / T.cell).astype(int), 0, T.nz - 1)
    Hs = T.H[np.ix_(jj, ii)]; Ss = T.slope_deg()[np.ix_(jj, ii)]
    hn = np.clip((Hs + 3) / 18, 0, 1)
    bg = np.stack([70 + 140 * hn, 90 + 120 * hn, 70 + 80 * hn], -1)
    bg[Ss > 25] = (205, 150, 80); bg[Ss > 45] = (150, 60, 55); bg[Hs < WATER_Y] = (40, 80, 130)
    plain = ((im == (120, 130, 100)).all(-1)) | ((im == (40, 44, 40)).all(-1))
    im[plain] = bg[plain].astype(np.int32)
    Image.fromarray(im.astype(np.uint8)).save(OUT_PNG)
