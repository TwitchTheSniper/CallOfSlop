#!/usr/bin/env python3
"""Generator for Maps/Cluttered/07-Supermarket.json (medium, 12 players): a run-down supermarket, SLOP MART.
Usage: python3 tools/cluttered/supermarket.py <repo root> [preview.png]

Built with townkit.py (the pack's shared placement rules) and the pack props from supermarket_props.py. Most of the map
is inside the store: grocery aisles, produce, bakery, dairy, a deli under a staff mezzanine (office and break room up a
flight of stairs, overlooking the floor), a frozen section, checkout lanes and the front end; behind it the stock room
(pallet racks, a walk-in cooler, a baler) and the loading dock. Outside: the car park in front, a service alley down the
west side and the dock apron at the back, all cut off by an irregular, noisy polygon outline.

The map is NOT mirrored: Blue spawns in the car park and comes in through the front (doors, smashed windows, the side
alley), Red spawns on the dock apron and comes through the stock room (and owns the quick back stair to the mezzanine,
the high ground). The generator checks fairness by measurement instead: the walk on a 0.5 m grid from each team's spawns
to every hill and to the enemy flag (within 10%), no line of sight between Blue and Red spawns, and the solid cover along
each team's routes to the hills.
"""
import json, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import townkit as tk
from townkit import yaw_facing, yaw_along

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/07-Supermarket.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None

# ---------------------------------------------------------------- outline: a polygon (corners straight into the noise, as the game does)
POLY = [(-50, 22), (-53, 34), (-47, 46.5), (-31, 49), (-13, 45.5), (3, 50), (21, 47), (37, 49.5), (47.5, 41), (46, 27), (37.4, 21.5),
        (37.3, -31), (37.0, -35.2), (33, -37.5), (31.5, -46), (14, -49.5), (-5, -46.5), (-23, -49.5), (-40, -46), (-44.6, -36),
        (-43.6, -12), (-44.8, 10), (-44, 18.5)]
NOISE = {"amplitude": 0.8, "scale": 9, "seed": 7, "octaves": 2}


def poly_outline(points, noise=None):
    """MapBoundary for a polygon: its corners, counter-clockwise, then the noise (see townkit.build_outline for smooth)."""
    pts = [(float(x), float(z)) for x, z in points]
    if tk._signed_area(pts) < 0:
        pts.reverse()
    if noise and noise.get("amplitude", 0) > 0:
        pts = tk.add_noise(pts, noise["amplitude"], noise.get("scale", 12), noise.get("seed", 1), noise.get("octaves", 2))
    return pts


tk.build_outline = poly_outline
t = tk.Town(REPO, POLY, NOISE, pack_props=[os.path.join(REPO, "Maps/Cluttered/Props")])
rng = random.Random(407)

# The walk check has to see what the game's bots see: low things (window sills, counters, freezer islands, promo stacks)
# can be mantled (up to 1.7 m, World.MantleReach; the nav graph adds 2 m of cost per mantle). So keep the height of the
# tallest thing standing on the floor in every cell, next to townkit's occupancy grids.
HGT = tk.np.zeros((t.nz, t.nx), tk.np.float32)
_block, _prop = tk.Town.block, tk.Town.prop


def _block_h(self, x0, y0, z0, x1, y1, z1, c):
    _block(self, x0, y0, z0, x1, y1, z1, c)
    if min(y0, y1) < 0.55:
        i0, i1, j0, j1 = self._ij(min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1))
        HGT[i0:i1, j0:j1] = tk.np.maximum(HGT[i0:i1, j0:j1], max(y0, y1))


def _prop_h(self, name, x, z, yaw=0.0, y=None, check=True, gap=0.02, lanes=False, margin=0.6, tag=None):
    ok = _prop(self, name, x, z, yaw, y, check, gap, lanes, margin, tag)
    if ok and self.P[name]["solid"] and (y is None or y < 0.6):
        i0, i1, j0, j1 = self._ij(*self.foot(name, x, z, round(yaw % 360, 1)))
        HGT[i0:i1, j0:j1] = tk.np.maximum(HGT[i0:i1, j0:j1], self.P[name]["h"] if name not in MANTLE_H else MANTLE_H[name])
    return ok


MANTLE_H = {"Checkout Counter": 0.92}   # its lane pole is thin: the counter top is what gets climbed
tk.Town.block, tk.Town.prop = _block_h, _prop_h


def walk_cost(sources, cell=0.5):
    """Walk cost (metres, plus 2 m per mantle onto anything 0.55-1.7 m tall) from the source points on a 0.5 m grid, like
    the game's nav graph costs a route. Returns (cost array, cell)."""
    import heapq
    np = tk.np
    k = int(cell / t.res)
    nz, nx = t.nz // k, t.nx // k
    h = HGT[:nz * k, :nx * k].reshape(nz, k, nx, k).max(axis=(1, 3))
    gx = t.x0 + (np.arange(nx) + 0.5) * cell; gz = t.z0 + (np.arange(nz) + 0.5) * cell
    blk = h > 1.7
    low = (h > 0.55) & ~blk
    for i in range(nz):
        for j in range(nx):
            if not blk[i, j] and t.edge(gx[j], gz[i]) < 0.4:
                blk[i, j] = True
    dist = np.full((nz, nx), np.inf); hp = []
    for (x, z) in sources:
        j = int((x - t.x0) / cell); i = int((z - t.z0) / cell)
        dist[i, j] = 0; heapq.heappush(hp, (0.0, i, j))
    nb = [(-1, 0, 1), (1, 0, 1), (0, -1, 1), (0, 1, 1), (-1, -1, 1.4142), (-1, 1, 1.4142), (1, -1, 1.4142), (1, 1, 1.4142)]
    while hp:
        d, i, j = heapq.heappop(hp)
        if d > dist[i, j]:
            continue
        for di, dj, w in nb:
            a, b = i + di, j + dj
            if 0 <= a < nz and 0 <= b < nx and not blk[a, b]:
                if di and dj and (blk[i, b] or blk[a, j]):
                    continue
                nd = d + w * cell + (2.0 if low[a, b] and not low[i, j] else 0.0)
                if nd < dist[a, b]:
                    dist[a, b] = nd; heapq.heappush(hp, (nd, a, b))
    return dist, cell

# ---------------------------------------------------------------- palette
TILE = (0.84, 0.82, 0.76); STOCK = (0.52, 0.52, 0.51); WALL_IN = (0.90, 0.89, 0.85); PART = (0.80, 0.80, 0.77)
FACADE = (0.78, 0.72, 0.62); FACADE_D = (0.62, 0.56, 0.48); RED = (0.77, 0.09, 0.11); ROOF = (0.33, 0.34, 0.36)
ASPH = (0.26, 0.26, 0.27); ASPH_B = (0.31, 0.30, 0.30); PAVING = (0.58, 0.56, 0.52); KERB = (0.66, 0.65, 0.62)
WHITE = (0.88, 0.88, 0.84); YELLOW = (0.88, 0.74, 0.16); DARK = (0.12, 0.13, 0.15); COLUMN = (0.86, 0.86, 0.83)
COOL = (0.82, 0.88, 0.92); SOIL = (0.34, 0.27, 0.19); STEEL = (0.62, 0.65, 0.68); CARPET = (0.36, 0.40, 0.48)

H = 6.5            # ceiling (the roof slab's underside)
MEZ = 3.6          # mezzanine floor top
X0, X1, Z0, Z1 = -36.0, 36.0, -34.0, 20.0   # store outer walls
W = 0.4            # outer wall thickness
ZP = -14.0         # the sales floor's back wall (stock room partition: z -14.4..-14.0)

# ---------------------------------------------------------------- objectives first (everything keeps clear of them)
BLUE_SP = [(-27.0, 44.4), (-17.5, 44.0), (-8.0, 44.6), (1.5, 44.8), (11.0, 44.6), (20.5, 44.2)]
RED_SP = [(-31.0, -38.4), (-23.0, -41.0), (-16.5, -38.8), (-6.5, -40.6), (4.5, -38.8), (13.0, -41.0)]
FFA_SP = [(-33.5, 12.4), (-6.0, -8.0), (15.45, -3.0), (-17.5, -33.0), (17.0, -25.5), (-40.0, -16.0), (27.0, 16.6)]
HILLS = [(2.5, 1.85, 3.3), (-27.0, 2.2, 3.5), (30.0, 0.2, 3.5)]
FLAGS = [(-4.0, 44.6), (-4.5, -40.4)]
# two keep-out lists: loose cover keeps well clear of spawns, hills and flags; fixtures (shelves, counters) only keep out of
# what the self-test checks (1 m round a spawn, a hill's radius, 2 m round a flag), so the aisles can frame a hill
SOFT = [(x, z, 2.6) for x, z in BLUE_SP + RED_SP + FFA_SP] + [(x, z, r + 0.3) for x, z, r in HILLS] + [(x, z, 2.6) for x, z in FLAGS]
HARD = [(x, z, 0.8) for x, z in BLUE_SP + RED_SP + FFA_SP] + [(x, z, r - 0.25) for x, z, r in HILLS] + [(x, z, 1.75) for x, z in FLAGS]
t.keepout = SOFT


def slab(x0, z0, x1, z1, c, material="Concrete", h=0.04, y=0.0):
    kw = {"material": material} if material else {}
    t.mesh("box", (x0 + x1) / 2, (z0 + z1) / 2, c, y=y, width=round(abs(x1 - x0), 2), height=h, depth=round(abs(z1 - z0), 2), **kw)


def clear(x0, z0, x1, z1):
    """Keeps a rectangle free of anything placed later (doorways, stair feet)."""
    t.mark(t.occ, (min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1)), 3)


def wall(axis, a0, a1, c0, c1, h, color, holes=(), y0=0.0):
    """A wall along X (axis 'x': from x = a0 to a1, z c0..c1) or Z, h high from y0, with openings (centre, width, bottom,
    top) measured along the wall and up from y0. Ground doorways are kept clear 1.1 m each side."""
    def B(p0, ya, p1, yb):
        if axis == "x":
            t.block(p0, ya, c0, p1, yb, c1, color)
        else:
            t.block(c0, ya, p0, c1, yb, p1, color)
    pos = a0
    for (cc, w, bot, top) in sorted(holes):
        lo, hi = cc - w / 2, cc + w / 2
        if lo - pos > 0.05:
            B(pos, y0, lo, y0 + h)
        if bot > 0.05:
            B(lo, y0, hi, y0 + bot)
        if top < h - 0.05:
            B(lo, y0 + top, hi, y0 + h)
        if bot <= 0.05 and y0 < 0.5:
            if axis == "x":
                clear(lo, c0 - 1.1, hi, c1 + 1.1)
            else:
                clear(c0 - 1.1, lo, c1 + 1.1, hi)
        pos = hi
    if a1 - pos > 0.05:
        B(pos, y0, a1, y0 + h)


def P(name, x, z, yaw=0.0, y=None, check=True, lanes=False, tag=None, margin=0.6, gap=None):
    """A prop: fixtures (lanes=False) may touch walls and each other (their footprints are checked 26 cm inside, a cell of the occupancy grid), loose
    cover (lanes=True) keeps 10 cm from everything and off the walking lanes."""
    t.keepout = SOFT if lanes else HARD
    ok = t.prop(name, x, z, yaw, y=y, check=check, lanes=lanes, tag=tag, margin=margin, gap=(0.1 if lanes else -0.26) if gap is None else gap)
    t.keepout = SOFT
    return ok


def column(x, z, top=H, c=COLUMN):
    t.block(x - 0.3, 0, z - 0.3, x + 0.3, top, z + 0.3, c)
    t.mesh("box", x, z, (0.3, 0.32, 0.36), y=0.0, width=0.66, height=0.25, depth=0.66)   # kick guard


# ================================================================ ground surfaces
# sales floor (tiles) and stock room (concrete); nothing grows under the roof anyway
slab(X0 + W, ZP, X1 - W, Z1 - W, TILE, "Tiles", h=0.02)
slab(X0 + W, Z0 + W, X1 - W, ZP - 0.4, STOCK, "Concrete", h=0.02)
# outside: the pavement along the store front (intact, a kerb), the car park and the service yards (busted: weeds at 10%)
slab(-36.0, Z1, 36.0, 23.6, PAVING, "Concrete", h=0.12)
slab(-46.0, 23.6, 42.0, 44.4, ASPH_B, None, h=0.03)
slab(-44.5, -34.0, -36.0, 23.6, ASPH, None, h=0.03)          # west service alley
slab(-44.5, -44.0, 37.0, -34.0, ASPH, None, h=0.03)          # dock apron
# painted fire lane along the kerb, and the dock's yellow lines
for k in range(int(70 / 1.6)):
    x = -35.0 + k * 1.6
    if not (-17.5 < x < -12.5 or 10.5 < x < 15.5):
        t.mesh("box", x, 24.4, YELLOW, y=0.0, width=0.18, height=0.04, depth=1.4, yaw=35.0)
for dx in (-20.0, -6.0, 8.0, 22.0):
    for s in (-1, 1):
        t.mesh("box", dx + s * 1.9, -39.0, YELLOW, y=0.0, width=0.15, height=0.04, depth=9.6)

# ================================================================ the store shell
# front: entrance and exit doors, three smashed shop windows (sills to mantle over)
FRONT_HOLES = [(-15.0, 4.0, 0.0, 2.9), (13.0, 4.0, 0.0, 2.9), (-27.0, 8.0, 1.0, 3.0), (-1.0, 9.0, 1.0, 3.0), (26.0, 8.0, 1.0, 3.0)]
wall("x", X0, X1, Z1 - W, Z1, H, FACADE, FRONT_HOLES)
# back (dock) wall: three open dock doors and a closed one with a truck backed up to it
DOCK_DOORS = [-20.0, -6.0, 8.0]
wall("x", X0, X1, Z0, Z0 + W, H, FACADE_D, [(d, 3.4, 0.0, 3.8) for d in DOCK_DOORS])
# west wall (alley side): a fire exit from produce, the stock room's side door; east wall: blank
wall("z", Z0 + W, Z1 - W, X0, X0 + W, H, FACADE, [(6.0, 1.6, 0.0, 2.4), (-24.0, 1.6, 0.0, 2.4)])
wall("z", Z0 + W, Z1 - W, X1 - W, X1, H, FACADE)
# partition between the shop floor and the stock room: bakery door, the main double door, the deli's back door, and the
# mezzanine break room's back door up at MEZ (reached by the stock room stair)
wall("x", X0 + W, X1 - W, ZP - 0.4, ZP, H, PART, [(-27.0, 1.8, 0.0, 2.4), (-3.0, 3.0, 0.0, 2.8), (18.0, 1.4, 0.0, 2.3), (31.7, 1.2, MEZ, MEZ + 2.2)])
# the roof (one slab) and the front parapet carrying the sign
t.block(X0 - 0.2, H, Z0 - 0.2, X1 + 0.2, H + 0.3, Z1 + 0.2, ROOF)
t.mesh("box", 0.0, Z1 + 0.15, FACADE, y=H + 0.3, width=72.6, height=2.5, depth=0.3, material="Brick")
t.mesh("box", 0.0, Z1 + 0.32, RED, y=H - 0.6, width=72.6, height=0.45, depth=0.06)
P("Slop Mart Sign", -1.0, Z1 + 0.42, 0, y=H + 0.45, check=False, tag="sign")
for xx in (-15.0, 13.0):
    # entrance canopies on posts, automatic door frames pushed open
    t.mesh("box", xx, 21.9, RED, y=3.35, width=7.0, height=0.35, depth=3.6)
    t.mesh("box", xx, 21.9, WHITE, y=3.3, width=6.8, height=0.06, depth=3.4)
    for px in (xx - 3.2, xx + 3.2):
        t.block(px - 0.12, 0.12, 23.3, px + 0.12, 3.3, 23.54, STEEL)
    for s in (-1, 1):
        t.mesh("box", xx + s * 2.6, Z1 - 0.2, (0.55, 0.62, 0.66), y=0.0, width=1.2, height=2.85, depth=0.08)
    t.mesh("box", xx, Z1 - 0.2, (0.3, 0.32, 0.34), y=2.9, width=4.4, height=0.3, depth=0.3)
for (xx, w) in ((-27.0, 8.0), (-1.0, 9.0), (26.0, 8.0)):
    # window frames: mullions every 2 m and a transom (the glass is long gone)
    n = int(w // 2)
    for k in range(n + 1):
        t.mesh("box", xx - w / 2 + k * w / n, Z1 - 0.2, (0.3, 0.32, 0.34), y=1.0, width=0.1, height=2.0, depth=0.12)
    t.mesh("box", xx, Z1 - 0.2, (0.3, 0.32, 0.34), y=2.95, width=w, height=0.1, depth=0.14)
# the closed dock door: a roller door on both faces of the wall, and every dock door's frame, bumpers and seals
for dx in DOCK_DOORS + [22.0]:
    for s in (-1, 1):
        t.mesh("box", dx + s * 1.85, Z0 - 0.08, YELLOW, y=0.0, width=0.3, height=3.9, depth=0.16)
        t.mesh("box", dx + s * 1.45, Z0 - 0.2, DARK, y=0.9, width=0.3, height=0.45, depth=0.25)   # rubber bumpers
    t.mesh("box", dx, Z0 - 0.08, (0.16, 0.16, 0.18), y=3.8, width=4.0, height=0.5, depth=0.2)
    if dx == 22.0:
        for z in (Z0 - 0.04, Z0 + W + 0.04):
            t.mesh("box", dx, z, (0.62, 0.64, 0.66), y=0.0, width=3.4, height=3.8, depth=0.05, material="Metal")
    else:
        t.mesh("box", dx, Z0 + W / 2, (0.36, 0.38, 0.40), y=0.0, width=3.4, height=0.03, depth=1.6, material="Metal")   # dock plate
        t.mesh("box", dx, Z0 + W / 2, (0.6, 0.62, 0.64), y=3.45, width=3.4, height=0.5, depth=0.5, material="Metal")   # rolled-up door

# ================================================================ the mezzanine (staff office and break room over the deli)
MX0 = 10.4
t.block(MX0, MEZ - 0.3, ZP, X1 - W, MEZ, -7.0, (0.70, 0.70, 0.68))
t.block(MX0, MEZ, -7.15, X1 - W, MEZ + 1.05, -7.0, STEEL)                    # parapet along the edge (cover up there)
t.mesh("box", (MX0 + X1 - W) / 2, -7.075, (0.80, 0.82, 0.84), y=MEZ + 1.05, width=X1 - W - MX0, height=0.06, depth=0.2)
t.block(MX0, MEZ, -7.6, MX0 + 0.15, MEZ + 1.05, -7.15, STEEL)
t.block(MX0, MEZ, -9.8, MX0 + 0.15, MEZ + 1.05, -9.4, STEEL)
for px in (12.5, 18.5, 24.5, 30.5):
    t.block(px - 0.2, 0, -7.6, px + 0.2, MEZ - 0.3, -7.2, STEEL)               # posts under the edge
# main stair from the back aisle (9 steps of 0.4 m, 0.45 m deep, like the game's StairsX)
SX0 = MX0 - 9 * 0.45
for k in range(1, 10):
    t.block(SX0 + (k - 1) * 0.45, 0, -9.4, SX0 + k * 0.45, k * 0.4, -7.6, (0.62, 0.62, 0.60))
t.mesh("box", (SX0 + MX0) / 2, -7.55, STEEL, y=0.0, width=4.2, height=0.06, depth=0.06, pitch=0.0)
clear(SX0 - 1.4, -9.4, SX0, -7.6)
# office and break room: walls from MEZ to the ceiling, windows onto the floor
wall("z", ZP, -9.8, MX0, MX0 + 0.25, H - MEZ, WALL_IN, y0=MEZ)
wall("x", MX0 + 0.25, X1 - W, -10.05, -9.8, H - MEZ, WALL_IN, [(13.2, 1.2, 0.0, 2.2), (17.5, 3.0, 1.0, 2.3), (24.2, 1.2, 0.0, 2.2), (29.5, 4.0, 1.0, 2.3), (33.6, 1.6, 1.0, 2.3)], y0=MEZ)
wall("z", ZP, -10.05, 21.0, 21.25, H - MEZ, WALL_IN, y0=MEZ)
t.mesh("box", (MX0 + 21.0) / 2, (ZP - 10.05) / 2, CARPET, y=MEZ, width=21.0 - MX0 - 0.3, height=0.02, depth=ZP * -1 - 10.05 - 0.05)
# stock room stair up to the break room's back door, and its landing
LX0 = 29.05
for k in range(1, 10):
    t.block(LX0 - 9 * 0.45 + (k - 1) * 0.45, 0, -16.4, LX0 - 9 * 0.45 + k * 0.45, k * 0.4, ZP - 0.4, (0.55, 0.56, 0.58))
t.block(LX0, MEZ - 0.3, -16.4, 33.2, MEZ, ZP - 0.4, (0.55, 0.56, 0.58))
t.block(LX0, MEZ, -16.55, 33.35, MEZ + 1.05, -16.4, STEEL)
t.block(33.2, MEZ, -16.4, 33.35, MEZ + 1.05, ZP - 0.4, STEEL)
clear(LX0 - 9 * 0.45 - 1.4, -16.4, LX0 - 9 * 0.45, ZP - 0.4)

# ================================================================ walk-in cooler in the stock room's north-east corner
wall("z", Z0 + W, -24.3, 26.0, 26.3, 3.0, COOL, [(-28.5, 1.4, 0.0, 2.3)])
wall("x", 26.0, X1 - W, -24.3, -24.0, 3.0, COOL)
t.block(26.0, 3.0, Z0 + W, X1 - W, 3.2, -24.0, COOL)
t.mesh("box", 26.0 - 0.05, -28.5, (0.70, 0.82, 0.90), y=0.0, width=0.04, height=2.3, depth=1.4)   # strip curtain

# ================================================================ structural columns
for (x, z) in ((0.65, 1.85), (4.35, 1.85), (-27.0, -2.0), (-27.0, 6.6), (30.0, -2.0), (30.0, 6.6), (-21.0, 16.8), (19.5, 16.8),
               (-12.0, -18.2), (8.5, -18.2), (-12.0, -29.6), (20.0, -21.0)):
    column(x, z)

# ================================================================ walking lanes (solid scatter keeps off them)
LANES = [
    ([(-34.0, 10.6), (34.0, 10.6)], 2.0), ([(-34.0, -7.3), (34.0, -7.3)], 2.0), ([(-34.0, 1.85), (34.0, 1.85)], 2.0),
    ([(-15.0, 22.0), (-15.0, 16.0)], 3.0), ([(13.0, 22.0), (13.0, 16.0)], 3.0), ([(-35.0, 17.2), (35.0, 17.2)], 1.6),
    ([(-20.0, -36.0), (-24.0, -28.0), (-27.0, -14.0)], 2.2), ([(-6.0, -36.0), (-8.6, -25.4), (-3.0, -14.0)], 2.4), ([(8.0, -36.0), (14.0, -24.0), (18.0, -14.0)], 2.2),
    ([(-37.0, -24.0), (-26.0, -24.0), (-6.0, -24.0)], 2.0), ([(14.0, -24.0), (24.0, -15.4)], 1.8), ([(14.0, -28.5), (27.0, -28.5)], 1.6),
    ([(-37.0, 6.0), (-30.0, 6.0)], 1.8), ([(-40.0, 22.0), (-40.0, -36.0)], 2.6), ([(-42.0, -38.0), (34.0, -38.0)], 3.0),
    ([(-44.0, 26.8), (42.0, 26.8)], 4.0), ([(-44.0, 42.6), (42.0, 42.6)], 3.6), ([(-1.0, 27.0), (-1.0, 42.0)], 3.0),
    ([(-45.0, 27.0), (-45.0, 42.0)], 2.4), ([(41.0, 27.0), (41.0, 42.0)], 2.4),
]
for pts, w in LANES:
    t.add_lane(pts, w)

# ================================================================ shop floor: aisles of gondola shelving
ROWS = [-16.0 + 3.7 * k for k in range(11)]
KINDS = ["Grocery Shelf Bottles", "Grocery Shelf Snacks", "Grocery Shelf Cereal", "Grocery Shelf Cans", "Grocery Shelf Household"]
RUN_A = (-5.6, 0.3)
RUN_B = (3.4, 9.3)
shelf_count = 0
for k, x in enumerate(ROWS):
    for ri, (za, zb) in enumerate((RUN_A, RUN_B)):
        mods = [za + 0.55 + 1.2, za + 0.55 + 3.6]
        caps = [(za + 0.275, 180), (zb - 0.275, 0)]
        if k == 5:   # the centre court: the middle of row 5 is taken out round hill 1 (two columns hold the roof there)
            if ri == 0:
                mods = mods[:1]; caps = [caps[0], (mods[0] + 1.2 + 0.275, 0)]
            else:
                mods = mods[1:]; caps = [(mods[0] - 1.2 - 0.275, 180), caps[1]]
        for j, z in enumerate(mods):
            if P(KINDS[(k + ri * 2 + j) % len(KINDS)], x, z, 90 if (k + j) % 2 else 270, tag="shelf"):
                shelf_count += 1
        for z, yaw in caps:
            P("Shelf End Cap", x, z, yaw, tag="shelf")
# hanging aisle signs over both ends of every aisle, a department sign over each department
for k in range(len(ROWS) - 1):
    ax = (ROWS[k] + ROWS[k + 1]) / 2
    for z in (-6.2, 9.9):
        P("Hanging Aisle Sign", ax, z, 0, y=H - 1.75, check=False, tag="sign")
for name, x, z, yaw in (("Department Sign Produce", -27.0, 9.0, 0), ("Department Sign Produce", -27.0, -4.4, 0), ("Department Sign Bakery", -28.0, -10.2, 0),
                        ("Department Sign Dairy", -7.0, -10.2, 0), ("Department Sign Deli", 22.0, -6.2, 0), ("Department Sign Frozen", 30.0, -4.6, 0),
                        ("Department Sign Frozen", 30.0, 9.0, 0), ("Department Sign Checkout", -4.0, 11.4, 0), ("Department Sign Checkout", 10.0, 11.4, 0)):
    P(name, x, z, yaw, y=H - 1.75, check=False, tag="sign")

# ---- produce (west): tilted bins round hill 2, crates and a greens wall along the west wall
PROD = ["Produce Bin Fruit", "Produce Bin Greens", "Produce Bin Melons"]
for j, (x, z) in enumerate(((-31.8, -3.4), (-27.0, -3.6), (-22.2, -3.4), (-31.8, 8.2), (-26.0, 8.6), (-21.5, 8.4), (-32.4, 2.2), (-21.4, 2.2))):
    P(PROD[j % 3], x, z, 90 if abs(x + 27) > 5 and abs(z - 2.2) < 1 else rng.choice((0, 180)), tag="produce")
z = -4.6
while z < 10.4:
    if not (4.0 < z < 8.0):
        P(rng.choice(["Produce Bin Greens", "Produce Bin Fruit"]), X0 + W + 0.52, z, 90, tag="produce")
    z += 1.9
for (x, z, nm) in ((-31.6, -1.4, "Apple Crate"), (-23.0, -1.6, "Orange Crate"), (-31.4, 5.8, "Cabbage Crate"), (-22.6, 5.6, "Potato Sacks")):
    P(nm, x, z, rng.uniform(0, 360), tag="produce")
for (x, z) in ((-19.0, -4.8), (-19.0, 6.2)):
    P("Produce Crates", x, z, 90, tag="produce")

# ---- bakery (back west): racks against the back wall, bread tables in front
for x in (-34.6, -33.0, -31.4, -29.8, -24.2, -22.6, -21.0):
    P("Bakery Rack", x, ZP + 0.32, 0, tag="bakery")
for x in (-32.6, -22.2):
    P("Bread Display", x, -11.2, 0, tag="bakery")
P("Shopping Basket Stack", -30.4, -11.4, 20, tag="bakery")

# ---- dairy (back wall middle): open chillers, two yoghurt islands
for x in (-17.8, -15.4, -13.0, -10.6, -8.2, 1.2, 3.6, 6.0, 8.4):
    P("Dairy Chiller", x, ZP + 0.47, 0, tag="dairy")
for x in (-14.0, 4.0):
    P("Freezer Island", x, -11.0, 0, tag="dairy")

# ---- deli under the mezzanine: counters facing the back aisle, prep kitchen behind them
for x in (14.0, 16.4, 23.4, 25.8, 28.2, 30.6):
    P("Deli Counter", x, -9.9, 0, tag="deli")
for x, nm in ((12.2, "Fridge"), (13.0, "Fridge"), (14.4, "Kitchen Counter"), (15.6, "Kitchen Counter"), (20.6, "Cooker"), (21.4, "Cooker"),
              (22.6, "Kitchen Counter"), (23.8, "Kitchen Sink"), (25.2, "Kitchen Counter"), (26.6, "Dishwasher"), (28.0, "Kitchen Counter"),
              (29.4, "Fridge"), (30.2, "Fridge"), (32.4, "Chest Freezer"), (34.4, "Metal Shelving")):
    P(nm, x, ZP + 0.4, 0, tag="deli")
for (x, nm) in ((14.4, "Microwave"), (15.6, "Coffee Maker"), (22.6, "Knife Block"), (25.2, "Stacked Plates"), (28.0, "Cooking Pot")):
    P(nm, x, ZP + 0.35, rng.uniform(-15, 15), y=0.9, check=False, tag="deli")

# ---- frozen (east): glass-door freezers along the east wall, open islands
for z in (-4.3, -1.9, 0.5, 2.9, 5.3, 7.7, 10.1):
    P("Freezer Case", X1 - W - 0.5, z, 270, tag="frozen")
for x, z in ((27.0, -3.9), (31.4, -4.6), (27.0, 6.4), (31.4, 6.4)):
    P("Freezer Island", x, z, 90, tag="frozen")

# ---- checkouts and the front end
for k in range(10):
    P("Checkout Counter", -8.0 + 2.6 * k, 13.5, 270, tag="checkout")
P("Reception Desk", -29.0, 14.2, 0, tag="frontend")
P("Safe", -30.8, 12.9, 0, tag="frontend"); P("Filing Cabinet", -27.2, 12.9, 0, tag="frontend")
P("Desk Phone", -29.4, 14.1, 10, y=1.0, check=False, tag="frontend")
for (x, z) in ((-20.0, 18.95), (-10.6, 18.95), (8.0, 18.95), (18.6, 18.95)):
    P("Shopping Cart Nest", x, z, 0, tag="frontend")
for (x, z, nm, yaw) in ((-33.8, 18.7, "Glowing Vending Machine", 180), (-32.6, 18.7, "Vending Machine", 180), (32.6, 18.7, "ATM", 180),
                        (34.2, 17.2, "Water Cooler", 270), (-24.0, 18.9, "Gumball Machine", 0), (4.8, 18.9, "Gumball Machine", 0)):
    P(nm, x, z, yaw, tag="frontend")
for (x, z) in ((-17.6, 17.6), (-12.4, 18.6), (16.0, 18.4)):
    P("Shopping Basket Stack", x, z, rng.uniform(0, 360), tag="frontend")
for (x, z) in ((-23.0, 11.6), (24.0, 11.2), (-3.0, -8.4), (8.0, 3.0)):
    P("Wet Floor Sign", x, z, rng.uniform(0, 360), tag="frontend")

# ---- loose cover on the shop floor: promo stacks and pallets of water in the open spots, abandoned carts
for (x, z, nm) in ((-11.0, 10.4, "Promo Stack"), (20.0, 10.2, "Promo Stack"), (-30.5, -7.6, "Pallet of Water"), (33.0, -7.8, "Promo Stack"),
                   (-19.5, 1.8, "Promo Stack"), (23.0, 1.0, "Pallet of Water"), (9.0, -11.6, "Pallet of Goods"), (-24.0, 13.0, "Promo Stack")):
    P(nm, x, z, rng.choice((0, 90, 180, 270)) + rng.uniform(-8, 8), tag="cover")
for _ in range(14):
    x, z = rng.uniform(-34, 34), rng.uniform(-8.5, 11.2)
    P("Shopping Cart", x, z, rng.uniform(0, 360), lanes=False, tag="carts")

# ---- ceiling lights: rows of pendant panels, a few dying ones flickering
LIGHTS = 0
for x in [-30.0, -25.0, -20.0] + [(ROWS[k] + ROWS[k + 1]) / 2 for k in range(len(ROWS) - 1)] + [24.5, 28.0, 32.5]:
    for z in (-11.5, -7.5, -3.5, 1.0, 5.5, 9.8, 13.5, 17.5):
        if x > MX0 - 0.6 and z < -6.4:
            continue          # the mezzanine is there
        P("Flickering Ceiling Light" if rng.random() < 0.12 else "Ceiling Light Panel", x, z, 90, y=H - 1.0, check=False, tag="light")
        LIGHTS += 1
for x in (13.0, 17.0, 21.0, 25.0, 29.0, 33.0):                                   # under the mezzanine (low ceiling)
    P("Ceiling Light Panel", x, -11.0, 0, y=MEZ - 0.3 - 0.9, check=False, tag="light"); LIGHTS += 1
for x in (-30.0, -22.0, -14.0, -6.0, 2.0, 10.0, 18.0):                           # stock room: fewer, more of them dying
    for z in (-30.0, -24.0, -18.0):
        if x > 25 and z < -23:
            continue
        P("Flickering Ceiling Light" if rng.random() < 0.3 else "Ceiling Light Panel", x, z, 0, y=H - 1.0, check=False, tag="light"); LIGHTS += 1
for x in (15.5, 28.0):                                                           # mezzanine rooms
    P("Ceiling Light Panel", x, -12.0, 0, y=H - 1.0, check=False, tag="light"); LIGHTS += 1

# ================================================================ the mezzanine rooms (props stand on the floor at y = MEZ)
MEZ_PROPS = [
    ("Office Desk", 15.2, -12.6, 0), ("Office Chair", 15.2, -11.7, 180), ("Office Desk", 18.6, -12.6, 0), ("Office Chair", 18.6, -11.7, 190),
    ("Filing Cabinet", 11.0, -13.6, 90), ("Filing Cabinet", 11.0, -12.9, 90), ("Safe", 20.5, -13.5, 270), ("Whiteboard", 12.6, -13.7, 0),
    ("Water Cooler", 11.0, -11.0, 90), ("Office Plant", 20.6, -10.6, 0),
    ("Table", 26.5, -12.0, 0), ("Plastic Chair", 26.1, -11.2, 175), ("Plastic Chair", 26.9, -12.8, 5), ("Plastic Chair", 25.5, -12.2, 85),
    ("Fridge", 21.8, -13.6, 0), ("Kitchen Counter", 23.0, -13.65, 0), ("Kitchen Sink", 24.2, -13.65, 0), ("Vending Machine", 34.9, -11.2, 270),
    ("Metal Lockers", 34.9, -12.6, 270), ("Fabric Sofa", 30.0, -13.5, 0), ("Coffee Table", 30.0, -12.3, 0),
]
for nm, x, z, yaw in MEZ_PROPS:
    P(nm, x, z, yaw, y=MEZ, check=False, tag="mezzanine")
for nm, x, z, top in (("Computer Monitor", 15.0, -12.75, 0.76), ("Desk Phone", 15.7, -12.6, 0.76), ("Monitor Screen Saver", 18.4, -12.75, 0.76),
                      ("Laptop", 19.0, -12.5, 0.76), ("Microwave", 23.0, -13.7, 0.9), ("Kettle", 23.3, -13.6, 0.9), ("Soda Cans", 26.6, -12.0, 0.75),
                      ("Book Pile", 30.1, -12.3, 0.45)):
    P(nm, x, z, rng.uniform(-20, 20), y=MEZ + top, check=False, tag="mezzanine")
P("Ceiling Fan", 28.0, -12.0, 0, y=H - 0.7, check=False, tag="mezzanine")
for (x, z, nm) in ((16.0, -8.4, "Litter"), (27.0, -8.6, "Newspaper"), (33.0, -8.2, "Soda Cans"), (22.0, -13.0, "Cardboard Box Small"), (12.5, -12.0, "Archive Boxes")):
    P(nm, x, z, rng.uniform(0, 360), y=MEZ, check=False, tag="mezzanine")

# ================================================================ the stock room
# pallet racks: along the west wall (a gap at the side door), two rows west of the goods path, one row in the east
for z in (-31.6, -28.8, -20.0, -17.2):
    P("Pallet Rack", X0 + W + 0.56, z, 90, tag="stock")
for zr in (-26.2, -21.4):
    for x in (-22.6, -19.8, -17.0, -14.2):
        P("Pallet Rack", x, zr, 0, tag="stock")
for x in (5.6, 8.4, 11.2):
    P("Pallet Rack", x, -21.6, 0, tag="stock")
for x in (-4.5, -1.7):          # a rack island between the middle dock door and the main door: no straight line through
    P("Pallet Rack", x, -25.4, 0, tag="stock")
# baler and bales by the closed door, roll cages and lockers along the partition, a forklift parked up
P("Cardboard Baler", 15.0, Z0 + W + 0.56, 0, tag="stock")
for (x, z) in ((18.4, -33.15), (19.6, -33.15), (20.8, -33.15), (22.0, -33.15), (23.2, -33.15), (19.0, -32.3), (21.5, -32.3)):
    P("Store Cardboard Bale", x, z, rng.choice((0, 180)), tag="stock")
for x in (-20.2, -19.0, -17.8, -16.6, -15.4):
    P("Metal Lockers", x, ZP - 0.4 - 0.28, 180, tag="stock")
P("Table", -10.0, -16.2, 0, tag="stock"); P("Plastic Chair", -10.4, -15.5, 180, tag="stock"); P("Plastic Chair", -9.6, -16.9, 0, tag="stock")
P("Water Cooler", -13.6, ZP - 0.4 - 0.22, 180, tag="stock")
P("Whiteboard", -12.0, ZP - 0.4 - 0.32, 180, y=None, tag="stock")
for x in (1.6, 2.5, 3.4, 9.2, 10.1):
    P("Roll Cage", x, ZP - 0.4 - 0.4, rng.uniform(-10, 10), tag="stock")
P("Forklift", 1.0, -29.4, 90, tag="stock")
for (x, z, nm) in ((-12.0, -32.4, "Pallet of Goods"), (-10.6, -32.4, "Pallet of Water"), (-1.6, -32.2, "Pallet of Goods"), (3.0, -32.3, "Pallet of Goods"),
                   (-14.0, -29.6, "Pallet Stack"), (13.6, -31.0, "Pallet of Water"), (4.0, -26.0, "Pallet of Goods"), (-27.0, -31.6, "Pallet of Goods"),
                   (-30.5, -26.6, "Pallet Stack"), (-9.6, -18.0, "Pallet of Water"), (22.0, -26.0, "Roll Cage"),
                   (-23.4, -17.0, "Pallet of Goods"), (-29.6, -17.2, "Roll Cage"), (0.0, -20.4, "Pallet of Goods")):
    P(nm, x, z, rng.choice((0, 90, 180, 270)) + rng.uniform(-6, 6), lanes=True, tag="stock")
# the walk-in cooler: shelving along its walls, a freezer and pallets
for z in (-32.6, -31.2, -29.8):
    P("Metal Shelving", X1 - W - 0.3, z, 270, tag="cooler")
for x in (28.0, 29.4, 30.8, 32.2, 33.6):
    P("Metal Shelving", x, Z0 + W + 0.3, 0, tag="cooler")
P("Chest Freezer", 34.6, -26.0, 270, tag="cooler"); P("Pallet of Water", 30.0, -27.2, 0, tag="cooler"); P("Produce Crates", 32.6, -25.2, 0, tag="cooler")

# ================================================================ outside: the west service alley
tk.bins_by_door(t, (X0, -24.0), (0, 1), (-1, 0), rng, side=1, seed=71, tag="bins")
tk.bins_by_door(t, (X0, 6.0), (0, 1), (-1, 0), rng, side=-1, dumpster=False, seed=72, tag="bins")
P("Box Truck", -40.2, -4.0, 90, tag="alley")                       # a delivery truck parked down the alley (breaks the long line)
for (x, z, nm, yaw) in ((-36.65, -12.0, "Rooftop Condenser", 90), (-36.65, -14.0, "Rooftop Condenser", 90), (-36.6, 13.0, "AC Condenser Fan", 90),
                        (-36.6, 14.6, "AC Condenser Fan", 90), (-42.6, -28.0, "Pallet Stack", 0), (-42.4, -30.0, "Store Cardboard Bale", 90),
                        (-37.0, -30.4, "Pallet Stack", 0), (-42.8, 16.0, "Builders Skip", 90), (-37.2, -1.0, "Gas Cylinders", 0)):
    P(nm, x, z, yaw, tag="alley")
tk.wall_kit(t, (X0, -33.0), (X0, 19.0), (-1, 0), rng, ys=(2.6, 3.6), every=6.0, skip=[(7.4, 10.6), (37.4, 40.6), (24.0, 34.0)], tag="wall")

# ================================================================ outside: the dock apron
P("Box Truck", 22.0, Z0 - 4.1, 270, tag="dock")                    # backed up to the closed door
P("Truck Cab", 27.6, -41.8, 180, tag="dock")
for dx in (-13.0, 1.0):
    tk.bins_by_door(t, (dx - 2.4, Z0), (1, 0), (0, -1), rng, side=1, seed=int(80 + dx), tag="bins")
tk.bins_by_door(t, (25.6, Z0), (1, 0), (0, -1), rng, side=1, dumpster=True, seed=88, tag="bins")
for (x, z, nm, yaw) in ((-26.8, -35.2, "Store Cardboard Bale", 0), (-28.0, -35.2, "Store Cardboard Bale", 0), (-27.4, -36.2, "Store Cardboard Bale", 90),
                        (-33.0, -35.0, "Pallet Stack", 0), (-33.4, -36.0, "Pallet Stack", 10), (12.4, -35.0, "Pallet of Water", 0), (13.8, -35.2, "Pallet of Goods", 0),
                        (-36.5, -41.0, "Bench", 90), (-36.4, -39.4, "Cigarette Bin", 0), (-36.6, -43.0, "Trash Can", 0), (-3.0, -35.2, "Roll Cage", 0),
                        (-41.0, -35.6, "Diesel Generator", 0)):
    P(nm, x, z, yaw, tag="dock")
for (x, z) in ((-40.5, -43.2), (2.0, -46.2), (30.0, -45.0)):
    P("Flickering Street Lamp", x, z, rng.uniform(0, 360), margin=0.3, tag="lamp")
tk.wall_kit(t, (-35.0, Z0), (35.0, Z0), (0, -1), rng, ys=(4.2, 5.2), every=7.0,
            skip=[(d + 35.0 - 2.3, d + 35.0 + 2.3) for d in DOCK_DOORS + [22.0]], kinds=(("Vent Grille", 3), ("Electric Meter", 1), ("Window AC Unit", 1)), tag="wall")
# the store roof: plant, vents and skylights (seen from the car park)
tk.roof_kit(t, X0 + 1, Z0 + 1, X1 - 1, Z1 - 1.5, H + 0.3, rng, n=36)

# ================================================================ outside: the car park
# kerb furniture along the store front
for (x, nm, yaw) in ((-33.0, "Trash Can", 180), (-31.0, "Newspaper Box", 180), (-29.6, "Payphone", 180), (-21.0, "Bench", 180), (-19.6, "Trash Can", 180),
                     (-9.0, "Glowing Vending Machine", 180), (-7.8, "Vending Machine", 180), (6.0, "Bench", 180), (7.6, "Trash Can", 180),
                     (19.0, "Newspaper Box", 180), (20.0, "Trash Can", 180), (30.0, "Recycling Bin", 180), (31.0, "Recycling Bin", 180), (32.0, "Recycling Bin", 180)):
    P(nm, x, 20.8 if nm not in ("Bench",) else 21.0, yaw, tag="kerb")
for xx in (-15.0, 13.0):
    for k in range(-2, 3):
        P("Bollard", xx + k * 1.4, 23.3, 0, tag="kerb")
# bays: two runs of double rows with a planted island between them and at both ends, lamps in the islands, cart corrals
for (ix0, ix1) in ((-48.0, -44.4), (-3.4, 0.6), (39.6, 42.0)):
    t.mesh("box", (ix0 + ix1) / 2, 34.8, KERB, y=0.0, width=ix1 - ix0, height=0.16, depth=10.4, material="Concrete")
    t.mesh("box", (ix0 + ix1) / 2, 34.8, SOIL, y=0.0, width=ix1 - ix0 - 0.3, height=0.18, depth=10.1, material="Dirt")
    rect = [round(ix0 + 0.2, 2), 29.8, round(ix1 - 0.2, 2), 39.8]
    t.region("grass", 6.0, rect=rect, patchiness=0.4, scale=[1.0, 1.6])
    t.region("shrub", 1.0, rect=rect, patchiness=0.4)
    t.region("bush", 0.25, rect=rect, patchiness=0.5)
    cx = (ix0 + ix1) / 2
    t.tree(cx, 31.6, rng.uniform(6.0, 8.0)); t.tree(cx, 38.0, rng.uniform(6.0, 8.0))
    P("Street Lamp", cx, 34.8, 0, tag="lamp")
for (x, z, yaw) in ((-22.3, 32.3, 90), (10.1, 37.3, 90), (28.3, 32.3, 90)):
    P("Cart Corral", x, z, yaw, tag="corral")
for (x, z) in ((-30.0, 34.8), (15.0, 34.8)):
    P("Flickering Street Lamp", x, z, 0, tag="lamp")
cars = ("Sedan", "Hatchback", "SUV", "Van", "Pickup Truck", "Taxi", "Burnt Out Car", "Car On Bricks", "Sedan", "Hatchback")
tk.car_park(t, -44.2, 29.6, -3.6, 40.0, random.Random(4071), along_x=True, fill=0.62, cars=cars)
tk.car_park(t, 0.8, 29.6, 39.4, 40.0, random.Random(4072), along_x=True, fill=0.58, cars=cars)
# abandoned carts and a bus stop, a price pylon at the corner
for _ in range(16):
    x, z = rng.uniform(-44, 40), rng.uniform(24.5, 43.5)
    P("Shopping Cart", x, z, rng.uniform(0, 360), lanes=False, tag="carts")
P("Bus Shelter", 34.0, 44.2, 180, tag="kerb"); P("Bus Stop Sign", 31.4, 44.6, 180, tag="kerb")
P("Pylon Sign", 43.0, 31.0, 270, tag="kerb")
P("Bench", -38.0, 43.4, 0, margin=0.4, tag="kerb"); P("Post Litter Bin", -36.4, 43.6, 0, margin=0.4, tag="kerb")

# ================================================================ foliage: weeds through the busted asphalt, long grass on the verges
BX0, BZ0, BX1, BZ1 = t.x0, t.z0, t.x1, t.z1
t.region("grass", 3.0, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.5, scale=[0.8, 1.3], color=[0.45, 0.50, 0.26])
t.region("shrub", 0.4, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.6)
# verges between the asphalt and the edge (car park south and sides, the dock's north edge): lush
for poly in ([[-54, 44.4], [48, 44.4], [48, 52], [-54, 52]], [[-54, 20], [-46, 20], [-46, 46], [-54, 46]], [[42, 20], [49, 20], [49, 46], [42, 46]],
             [[-46, -52], [38, -52], [38, -44], [-46, -44]]):
    t.region("grass", 6.5, polygon=poly, patchiness=0.35, scale=[1.0, 1.8], color=[0.38, 0.50, 0.22])
    t.region("shrub", 1.0, polygon=poly, patchiness=0.4)
    t.region("bush", 0.3, polygon=poly, patchiness=0.5, scale=[0.9, 1.5])
    t.region("tree", 0.02, polygon=poly, height=[6.0, 9.5])
tk.tree_row(t, (-44.0, 47.0), (-14.0, 46.0), 7.5, rng, h=(6.5, 9.5))
tk.tree_row(t, (6.0, 47.6), (34.0, 47.4), 7.5, rng, h=(6.5, 9.5))
tk.tree_row(t, (-38.0, -46.0), (28.0, -46.4), 9.0, rng, h=(6.0, 9.0))
# weeds along the foot of the walls in the alley and on the apron, and along the kerb
for (a, b) in (((-36.6, -33.0), (-36.6, 19.0)), ((-35.0, -34.6), (35.0, -34.6)), ((-35.5, 23.8), (35.5, 23.8)), ((-43.4, -33.0), (-43.4, 18.0))):
    tk.weeds_along(t, a, b, width=0.9, density=5.0)

# ================================================================ the edge: dressed per section
def arc_of(x, z):
    pts = t.outline; best = (1e9, 0); s = 0.0
    for i in range(len(pts)):
        d = (pts[i][0] - x) ** 2 + (pts[i][1] - z) ** 2
        if d < best[0]:
            best = (d, s)
        s += math.hypot(pts[(i + 1) % len(pts)][0] - pts[i][0], pts[(i + 1) % len(pts)][1] - pts[i][1])
    return best[1]


EDGE = [((-49, 23), (-47, 46), "hedge"), ((-46, 47), (36, 49), "hedge"), ((38, 49), (46.5, 28), "fence"),
        ((-44.2, 18), (-44.4, -35), "backs"), ((-42, -45), (31, -46), "fence"), ((32, -38), (36.8, -34), "fence")]
edge_counts = {}
for (a, b, style) in EDGE:
    n, total = tk.edge_run(t, arc_of(*a), arc_of(*b), style, rng, inset=1.5 if style == "fence" else 0.9)   # turned fence panels get extra collider boxes: keep them all inside
    edge_counts[style] = edge_counts.get(style, 0) + n

# ================================================================ spawns and waypoints (after every solid thing)
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
t.ops.append({"op": "AutoWaypoints", "spacing": 5})
WAYPOINTS = [(ax, z) for ax in [(ROWS[k] + ROWS[k + 1]) / 2 for k in range(len(ROWS) - 1)] for z in (-2.6, 6.3)]
WAYPOINTS += [(-8.0 + 2.6 * k + 1.3, 13.5) for k in range(9)]                      # checkout lanes
WAYPOINTS += [(30.5, -28.5), (28.0, -31.0), (-35.0, -24.0), (-30.0, -22.8), (-3.0, -15.6), (18.0, -15.6), (18.0, -12.6), (-27.0, -12.6),
              (22.0, -14.8), (24.2, -15.4), (11.6, -8.5), (5.4, -8.5), (16.0, -12.2), (25.5, -12.2), (-39.8, -18.0), (-40.0, 9.0), (-39.6, 0.0)]
for (x, z) in WAYPOINTS:
    if t.free((x - 0.3, z - 0.3, x + 0.3, z + 0.3)) or True:
        t.ops.append({"op": "Waypoint", "x": tk.R(x), "z": tk.R(z)})

# ================================================================ cosmetic clutter (the game's Clutter op runs last and keeps out of walls)
SHOP = ["Dropped Groceries", "Dropped Groceries", "Dropped Groceries", "Litter", "Soda Cans", "Bottle", "Bottles", "Cardboard Box Small",
        "Flattened Cardboard", "Plastic Bag", "Newspaper", "Open Cardboard Box"]
t.clutter(SHOP, X0 + W + 0.3, -9.4, 0.0, 19.2, 340, 41); t.clutter(SHOP, 0.0, -9.4, X1 - W - 0.3, 19.2, 340, 42)
t.clutter(SHOP + ["Wet Floor Sign"], X0 + W + 0.3, ZP + 0.2, X1 - W - 0.3, -9.0, 120, 43)
STOCKROOM = ["Flattened Cardboard", "Flattened Cardboard", "Cardboard Box", "Cardboard Box Small", "Open Cardboard Box", "Cardboard Stack", "Plank", "Litter",
             "Trash Bag", "Store Pallet Jack", "Plastic Bag", "Soda Cans", "Pallet"]
t.clutter(STOCKROOM, X0 + W + 0.3, Z0 + W + 0.3, 25.6, ZP - 0.7, 320, 44)
OUT = ["Litter", "Litter", "Litter", "Soda Cans", "Bottles", "Bottle", "Plastic Bag", "Newspaper", "Flattened Cardboard", "Trash Bag", "Cardboard Box Small",
       "Car Wheel", "Tyre", "Shopping Basket Stack", "Dropped Groceries", "Storm Drain", "Manhole Cover"]
t.clutter(OUT, -50.0, 23.8, 0.0, 48.0, 330, 45); t.clutter(OUT, 0.0, 23.8, 47.0, 48.0, 330, 46)
t.clutter(["Litter", "Trash Bag", "Flattened Cardboard", "Plastic Bag", "Bottles", "Plank", "Cardboard Box Small", "Pallet", "Tyre"], -44.0, -34.0, -36.3, 20.0, 150, 47)
t.clutter(["Litter", "Trash Bag", "Flattened Cardboard", "Plastic Bag", "Bottles", "Plank", "Cardboard Box Small", "Pallet", "Tyre", "Cigarette Bin"],
          -44.0, -48.0, 36.0, -34.3, 260, 48)

# ================================================================ the map
foliage = {"seed": 407, "surfaces": {"Plain": 0.1}, "paths": t.paths, "regions": t.regions, "items": t.items, "exclude": t.exclude}
mp = {
    "format": "call-of-slop-map/1",
    "name": "Supermarket",
    "description": "A run-down supermarket after closing: aisles, produce, deli and frozen, checkouts, a staff mezzanine, the stock room and loading dock, and a weedy car park. Not mirrored: Blue holds the car park, Red the dock.",
    "author": "Call of Slop",
    "order": 407,
    "maxPlayers": 12,
    "sky": "#8e96a0",
    "skybox": {"preset": "overcast", "sun": {"time": 17.6, "latitude": 44}, "clouds": {"coverage": 0.75, "speed": 6}, "ambient": 1.05},
    "boundary": {"shape": "polygon", "points": [list(p) for p in POLY], "noise": NOISE, "edge": "none", "groundMargin": 10, "barrierHeight": 3},
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": [{"op": "Ground", "color": [0.36, 0.37, 0.30]}] + t.ops,
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
    if not t.free((x - 0.4, z - 0.4, x + 0.4, z + 0.4), grid=t.walk):
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
print(f"area {t.area():.0f} m2 (side {t.area() ** 0.5:.1f} m), blocks {blocks}, trees {len(t.items)}, meshes {t.meshes}, hand props {len(t.props)} (solid {solid}, cosmetic {cosm}), shelves {shelf_count}, lights {LIGHTS}")
print("props by category:", dict(sorted(cats.items())))
print("props by rule:", dict(sorted(t.notes.items())))
print("edges:", edge_counts, "clutter asked:", sum(o["count"] for o in t.ops if o["op"] == "Clutter"))
vis = [(a, b) for a in BLUE_SP for b in RED_SP if t.sees(a, b)]
print("blue-red spawn lines of sight:", len(vis), vis)
fields = {s_: walk_cost([s_]) for s_ in BLUE_SP + RED_SP}
def avg_walk(sources, x, z):
    vals = [float(fields[s_][0][int((z - t.z0) / 0.5), int((x - t.x0) / 0.5)]) for s_ in sources]
    return sum(vals) / len(vals)
for i, (x, z, r) in enumerate(HILLS):
    b, rd = avg_walk(BLUE_SP, x, z), avg_walk(RED_SP, x, z)
    print(f"hill {i + 1} ({x},{z}): avg walk blue {b:.1f} m red {rd:.1f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
b, rd = avg_walk(BLUE_SP, *FLAGS[1]), avg_walk(RED_SP, *FLAGS[0])
print(f"enemy flag: avg blue {b:.1f} m red {rd:.1f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
if OUT_PNG:
    marks = [("dot", x, z, 0.8, (40, 90, 240)) for x, z in BLUE_SP] + [("dot", x, z, 0.8, (240, 50, 50)) for x, z in RED_SP] + [("dot", x, z, 0.8, (250, 250, 250)) for x, z in FFA_SP]
    marks += [("circle", x, z, r, (255, 230, 0)) for x, z, r in HILLS] + [("dot", x, z, 1.0, (255, 255, 255)) for x, z in FLAGS]
    marks += [("line", a, b, 0, (255, 0, 255)) for a, b in vis]
    t.preview(OUT_PNG, marks)
