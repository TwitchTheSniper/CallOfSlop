#!/usr/bin/env python3
"""Generator for Maps/World/01-MainStreet.json (World Tour, small, 12 players): Main Street, Prairie Hollow, a small
American farm town with cornfields all round it.

Usage: python3 tools/world/mainstreet.py <repo root> [preview.png]

First run tools/world/mainstreet_props.py (the Farm, Rail and Town props) and tools/world/mainstreet_decals.py (the
town's sign decals, Maps/World/Decals/MainStreet). Built with tools/cluttered/townkit.py (placement rules and the
occupancy grid) and tools/cluttered/terrain.py (the game's terrain generator, to know the ground height).

Layout (north is -Z): Main Street runs east-west through the middle (two lanes, parallel parking both sides,
sidewalks), crossed by Elm Street at x = 0. North side: Barber Shop and Dot's Diner (west), Prairie Savings Bank and
Hollow Grocery (east). South side: Rosa's Pizza and Main St. Drug (west), the raised town square with the bandstand
and the Starline gas station (east). Alleys with dumpsters, sheds and fences run behind both rows; beyond them the corn
starts and climbs the slope to the wooded rise that closes the map. Blue spawns in the co-op farm yard at the west end
(grain bins, tractor, hay wagon), Red behind a stopped freight train on the rail line at the east end. The ground tilts
gently from the north alley (2.4 m) down to the south one (0 m): the north storefronts sit on cut pads with steps up,
the south ones on built-up pads, Elm Street is the sloping street, the tracks run level through a cutting in the north
and on an embankment in the south, and a creek runs through the south corn. Farm tracks and Main Street poke out of the
outline as dead-end lanes.
"""
import json, math, os, random, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "cluttered"))
import numpy as np
import townkit as tk
import terrain as tr
from townkit import yaw_facing, yaw_along, R

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/World/01-MainStreet.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
rng = random.Random(501)

# ================================================================ outline: a noisy polygon with lanes poking out
POLY = [(-46.5, -7.0), (-48.5, -5.0), (-48.5, 5.0), (-46.5, 7.0),            # west stub (the county road dips out of sight)
        (-45.6, 19.0), (-37.5, 28.0), (-24.0, 30.2), (-4.6, 30.4),
        (-2.9, 38.5), (2.9, 38.5), (4.6, 30.4),                               # south lane (the farm track)
        (24.0, 30.2), (37.5, 28.0), (45.6, 19.0), (46.5, 7.0),
        (48.5, 5.0), (48.5, -5.0), (46.5, -7.0),                               # east stub (the road past the crossing)
        (45.6, -19.0), (37.5, -28.0), (24.0, -30.2), (4.6, -30.4),
        (2.9, -38.5), (-2.9, -38.5), (-4.6, -30.4),                           # north lane (the farm track)
        (-24.0, -30.2), (-37.5, -28.0), (-45.6, -19.0)]
NOISE = {"amplitude": 0.6, "scale": 9, "seed": 17, "octaves": 2}


def poly_outline(pts, noise):
    """The outline the game builds for a polygon boundary: straight edges sampled densely, CCW, then the noise."""
    dense = []
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n = max(1, int(math.ceil(math.hypot(b[0] - a[0], b[1] - a[1]) / 0.25)))
        for k in range(n):
            dense.append((a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n))
    if tk._signed_area(dense) < 0:
        dense.reverse()
    return tk.add_noise(dense, noise["amplitude"], noise["scale"], noise["seed"], noise["octaves"])


class Town(tk.Town):
    def __init__(self, repo, poly, noise, res=0.1):
        self.repo = repo; self.ctrl = poly; self.noise = noise
        self.outline = poly_outline(poly, noise)
        self.P = tk.load_props(repo)
        xs = [p[0] for p in self.outline]; zs = [p[1] for p in self.outline]
        pad = 6.0
        self.x0, self.z0 = math.floor(min(xs) - pad), math.floor(min(zs) - pad)
        self.x1, self.z1 = math.ceil(max(xs) + pad), math.ceil(max(zs) + pad)
        self.res = res
        self.nx = int((self.x1 - self.x0) / res); self.nz = int((self.z1 - self.z0) / res)
        self.occ = np.zeros((self.nz, self.nx), np.uint8); self.walk = np.zeros((self.nz, self.nx), np.uint8)
        self.tall = np.zeros((self.nz, self.nx), np.uint8); self.lane = np.zeros((self.nz, self.nx), np.uint8)
        self._sdf()
        self.ops = []; self.boxes = []; self.props = []; self.meshes = 0
        self.items = []; self.regions = []; self.paths = []; self.exclude = []; self.include = []
        self.keepout = []; self.notes = {}


t = Town(REPO, POLY, NOISE)


def _ij(x0, z0, x1, z1, eps=1e-6):
    j0 = max(0, int(math.floor((x0 - t.x0) / t.res + eps))); j1 = min(t.nx, int(math.ceil((x1 - t.x0) / t.res - eps)))
    i0 = max(0, int(math.floor((z0 - t.z0) / t.res + eps))); i1 = min(t.nz, int(math.ceil((z1 - t.z0) / t.res - eps)))
    return i0, i1, j0, j1


t._ij = _ij
W0 = {"anchor": "world"}
FAILED = []


def P(name, x, z, yaw=0.0, **kw):
    ok = t.prop(name, x, z, yaw, **kw)
    if not ok:
        FAILED.append((name, round(x, 1), round(z, 1)))
    return ok


def occupy(x0, z0, x1, z1, tall=True):
    r = (min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1))
    t.mark(t.occ, r); t.mark(t.walk, r)
    if tall:
        t.mark(t.tall, r)


# ================================================================ ground height (the base grid; pads come later)
def smooth01(e):
    e = min(1.0, max(0.0, e)); return e * e * (3 - 2 * e)


def vnoise(x, z, s, seed):
    """Smooth value noise in -1..1 (for the field undulation in the base grid; any smooth function would do)."""
    gx, gz = x / s, z / s; ix, iz = math.floor(gx), math.floor(gz); fx, fz = gx - ix, gz - iz
    def h(a, b):
        return tk._hash(a, b, seed)
    fx, fz = fx * fx * (3 - 2 * fx), fz * fz * (3 - 2 * fz)
    return (h(ix, iz) * (1 - fx) + h(ix + 1, iz) * fx) * (1 - fz) + (h(ix, iz + 1) * (1 - fx) + h(ix + 1, iz + 1) * fx) * fz


def base_h(x, z):
    zc = min(24.0, max(-24.0, z))
    h = 1.0 - 0.055 * zc                      # the town tilts down to the south: 2.3 m at the north alley, 0 at the south one
    if z < -21.0:
        h += 0.17 * (-21.0 - z)                # the corn climbs to the north rise
    if z > 28.0:
        h += 0.14 * (z - 28.0)                 # and up out of the creek bottom in the south
    if x < -34.0:
        h -= 0.025 * (-34.0 - x)               # the farm yard end sits a little lower
    field = max(smooth01((abs(z) - 20.0) / 4.0), smooth01((abs(x) - 33.0) / 3.0))
    h += field * (0.45 * vnoise(x, z, 11.0, 7) + 0.2 * vnoise(x, z, 5.0, 9))
    return h


MARGIN = 24
HALF_X, HALF_Z = 51.6, 41.6   # the outline's bounding box round the origin (plus the noise), for the terrain grid
GX0, GZ0, GC = -78.0, -66.0, 2.0
GCOLS, GROWS = int(156 / GC) + 1, int(132 / GC) + 1
GRID = [round(base_h(GX0 + c * GC, GZ0 + r * GC), 3) for r in range(GROWS) for c in range(GCOLS)]

FEATURES = [{"type": "grid", "x0": GX0, "z0": GZ0, "cell": GC, "cols": GCOLS, "rows": GROWS, "heights": GRID, "mode": "set"}]
PADS = []   # (x, z, w, d, height): building pads, also used for absolute heights of signs on them


def pad(x, z, w, d, h, edge=1.5, mode="set"):
    FEATURES.append({"type": "flatten", "x": R(x), "z": R(z), "w": R(w), "d": R(d), "height": R(h), "edge": edge, "mode": mode})
    PADS.append((x, z, w, d, h))


# ================================================================ the street plan
ROAD = 5.5          # Main Street: two 3.1 m lanes and 2.4 m parking each side (z = -5.5 .. 5.5)
SIDE = 8.6          # sidewalks out to here
ELM = 3.0           # Elm Street's road half width
ELM_SW = 5.5        # Elm's sidewalks out to here
N_ALLEY = (-21.6, -17.8)
S_ALLEY = (17.8, 21.8)

# Buildings: name, origin x, origin z, yaw, footprint (local x0, z0, x1, z1), variant, seed
BUILDINGS = [
    ("Barber Shop", -27.6, -12.2, 0, (-4.1, -3.6, 4.1, 3.6), {"palette": "red brick"}, 3),
    ("Diner", -14.0, -12.75, 0, (-7.12, -4.12, 7.12, 4.12), {"palette": "mint and chrome"}, 5),
    ("Bank", 14.6, -14.4, 0, (-8.2, -6.2, 8.2, 6.2), {"palette": "limestone"}, 7),
    ("Corner Shop", 28.4, -12.75, 0, (-4.62, -4.12, 4.62, 4.12), {"palette": "yellow brick"}, 9),
    ("Pizzeria", -26.4, 13.25, 180, (-6.12, -4.62, 6.12, 4.62), {"palette": "cream and green"}, 11),
    ("Pharmacy", -12.4, 13.25, 180, (-6.12, -4.62, 6.12, 4.62), {"palette": "white and green"}, 13),
    ("Gas Station", 25.6, 20.4, 180, (-4.12, -3.12, 4.12, 3.12), {"palette": "red and white"}, 15),
]


def to_world(b, lx, lz):
    _, ox, oz, yaw, *_ = b
    return (ox + lx, oz + lz) if yaw == 0 else (ox - lx, oz - lz)


def world_rect(b):
    fx0, fz0, fx1, fz1 = b[4]
    a = to_world(b, fx0, fz0); c = to_world(b, fx1, fz1)
    return (min(a[0], c[0]), min(a[1], c[1]), max(a[0], c[0]), max(a[1], c[1]))


B = {b[0]: b for b in BUILDINGS}
for b in BUILDINGS:
    x0, z0, x1, z1 = world_rect(b)
    if b[0] == "Gas Station":
        front = 7.6
        pad(25.6, 15.2, 15.0, 17.0, round(base_h(25.6, front), 2), edge=1.6)
    else:
        front = z1 if b[3] == 0 else z0
        pad((x0 + x1) / 2, (z0 + z1) / 2, x1 - x0 + 1.2, z1 - z0 + 1.2, round(base_h((x0 + x1) / 2, front), 2))
SQUARE = (6.4, 9.2, 17.4, 21.0)
SQUARE_H = 0.62
pad((SQUARE[0] + SQUARE[2]) / 2, (SQUARE[1] + SQUARE[3]) / 2, SQUARE[2] - SQUARE[0], SQUARE[3] - SQUARE[1], SQUARE_H, edge=1.3, mode="fill")
YARD_H = round(base_h(-39.0, 0.0), 2)
pad(-39.5, 0.0, 10.0, 26.0, YARD_H, edge=3.0)

# the rail line: level at 2.2 m, so it runs through a cutting in the north and on an embankment in the south
RAIL_X = 37.0
RAIL_Y = 2.2
FEATURES.append({"type": "ramp", "points": [[RAIL_X, -70, RAIL_Y], [RAIL_X, 70, RAIL_Y]], "width": 5.0, "edge": 6.5, "mode": "set"})
# the crossing: Main Street ramps up over the tracks
FEATURES.append({"type": "ramp", "points": [[30.0, 0.0], [RAIL_X, 0.0, RAIL_Y], [47.0, 0.0]], "width": 11.0, "edge": 2.0, "mode": "set"})
# the creek in the south corn, stopping at the embankment's toe (a culvert pipe there)
FEATURES.append({"type": "valley", "points": [[-75, 30.5], [-40, 30.0], [-10, 30.6], [12, 30.2], [30.5, 30.6]], "width": 7.0, "depth": 1.1, "material": "dirt"})

# paint: dirt fields and alleys first, then the sidewalks (sand = pale concrete), then the roads (rock = asphalt)
def paint_strip(points, core_half, material):
    FEATURES.append({"type": "ridge", "points": [[R(x), R(z)] for x, z in points], "width": R(core_half * 4), "height": 0.01, "material": material})


def paint_rect(x0, z0, x1, z1, material):
    if (x1 - x0) >= (z1 - z0):
        h = (z1 - z0) / 2; paint_strip([(x0 + h, (z0 + z1) / 2), (x1 - h, (z0 + z1) / 2)], h, material)
    else:
        h = (x1 - x0) / 2; paint_strip([((x0 + x1) / 2, z0 + h), ((x0 + x1) / 2, z1 - h)], h, material)


for z0, z1 in ((-34, -21.6), (21.8, 34)):
    paint_rect(-58, z0, 58, z1, "dirt")
paint_rect(-58, -21.6, -33.5, 21.8, "dirt")
paint_rect(33.5, -21.6, 58, 21.8, "dirt")
paint_rect(-34, N_ALLEY[0], 34, N_ALLEY[1], "dirt")
paint_rect(-34, S_ALLEY[0], 34, S_ALLEY[1], "dirt")
paint_rect(-33, -SIDE, 33.5, -ROAD, "sand"); paint_rect(-33, ROAD, 33.5, SIDE, "sand")
for sx in (-1, 1):
    for z0, z1 in ((-17.8, -SIDE), (SIDE, 17.8)):
        paint_rect(min(sx * ELM, sx * ELM_SW), z0, max(sx * ELM, sx * ELM_SW), z1, "sand")
paint_rect(SQUARE[0], SQUARE[1], SQUARE[2], SQUARE[3], "grass")
paint_rect(19.0, 7.0, 32.2, 17.4, "sand")                 # the gas station forecourt
paint_strip([(-60, 0), (60, 0)], ROAD, "rock")             # Main Street (and the county road out of town)
paint_strip([(0, -21.6), (0, 21.8)], ELM, "rock")          # Elm Street
paint_strip([(0, -45), (0, -21.6)], 1.6, "dirt")           # the farm tracks north and south
paint_strip([(0, 21.8), (0, 45)], 1.6, "dirt")
paint_strip([(RAIL_X, -60), (RAIL_X, 60)], 2.2, "dirt")    # the track bed (ballast props on it)

TERRAIN = {"cell": 0.5, "margin": MARGIN,
           "surface": {"grass": "#6f8a3e", "dirt": "#7a6244", "rock": "#3e4043", "sand": "#a8a59c", "dirtSlope": 34, "rockSlope": 70, "variation": 0.07},
           "features": FEATURES}
TER = tr.generate(TERRAIN, HALF_X, HALF_Z)


def gh(x, z):
    return float(TER.sample(x, z))


def pad_h(name):
    b = B[name]; x0, z0, x1, z1 = world_rect(b)
    for (px, pz, w, d, h) in PADS:
        if abs(px - (x0 + x1) / 2) < 0.01 and abs(pz - (z0 + z1) / 2) < 0.01:
            return h
    return PADS[[p[0] for p in BUILDINGS].index(name)][4]


# ================================================================ objectives (decided first so everything keeps clear)
BLUE_SP = [(-43.0, -4.5), (-42.5, 4.5), (-46.5, 2.0), (-47.0, -2.5), (-40.0, -9.0), (-40.0, 9.0)]
RED_SP = [(41.5, -4.5), (41.5, 5.0), (46.0, -2.0), (46.5, 2.5), (41.0, -12.5), (41.0, 12.5)]
HILLS = [(0.0, 0.0, 3.5), (0.0, -19.7, 3.5)]        # the crossroads, Elm Street in the north alley
FLAGS = [(-45.0, 0.0), (44.8, 0.0)]
for x, z in BLUE_SP + RED_SP + FLAGS:
    t.keepout.append((x, z, 2.2))
for x, z, r in HILLS:
    t.keepout.append((x, z, r + 0.6))

# walking lanes solid props keep off
t.add_lane([(-52, 0), (52, 0)], 2.0)
t.add_lane([(0, -40), (0, 40)], 2.4)
t.add_lane([(-34, -19.7), (34, -19.7)], 1.6)
t.add_lane([(-34, 19.8), (34, 19.8)], 1.6)
t.add_lane([(-33, -7.1), (33.5, -7.1)], 1.2)
t.add_lane([(-33, 7.1), (33.5, 7.1)], 1.2)
for x in (-22.3, -19.5):
    t.add_lane([(x, -8.6 if x < -21 else 8.6), (x, -19.7 if x < -21 else 19.8)], 1.3)    # the passages between buildings

# ================================================================ buildings
for name, ox, oz, yaw, fp, variant, seed in BUILDINGS:
    op = {"op": "Building", "name": name, "at": [R(ox), R(oz)], "seed": seed}
    if yaw:
        op["yaw"] = yaw
    op["variant"] = variant
    t.ops.append(op)
    x0, z0, x1, z1 = world_rect(B[name])
    occupy(x0 - 0.3, z0 - 0.3, x1 + 0.3, z1 + 0.3)
# the buildings' own extras outside their walls (dumpsters and bins out back, the bank's porch, the gas canopy posts)
occupy(9.8, -8.2, 19.4, -5.8, tall=False)                  # bank porch and bollards
occupy(-17.0, -18.6, -10.0, -16.6, tall=False)             # diner dumpster
occupy(-30.6, 17.6, -22.0, 19.6, tall=False)               # pizzeria dumpster, flour sacks
occupy(-14.4, 17.6, -10.6, 19.4, tall=False)               # pharmacy bins
for (px, pz) in ((-6.5, 5.5), (6.5, 5.5), (-6.5, 13.0), (6.5, 13.0)):
    wx, wz = to_world(B["Gas Station"], px, pz); occupy(wx - 0.3, wz - 0.3, wx + 0.3, wz + 0.3)
for (px, pz) in ((-2.5, 9.3), (2.5, 9.3)):
    wx, wz = to_world(B["Gas Station"], px, pz); occupy(wx - 1.0, wz - 0.8, wx + 1.0, wz + 0.8, tall=False)    # pump islands
wx, wz = to_world(B["Gas Station"], 7.8, 4.0); occupy(wx - 0.8, wz - 0.8, wx + 0.8, wz + 0.8)                  # pylon sign
wx, wz = to_world(B["Gas Station"], -5.0, 9.3); occupy(wx - 2.4, wz - 1.1, wx + 2.4, wz + 1.1)                 # the sedan at the pump

# ---------------------------------------------------------------- shop signs (invented brands): boards over the
# buildings' plain sign boxes, the decal pieces on their faces
SIGN_N = {"Dots Diner": 2, "Barber Shop": 2, "Main St Drug": 2, "Prairie Savings": 3, "Rosas Pizza": 2, "Hollow Grocery": 2, "Starline": 2, "Welcome Sign": 2,
          "Water Tower Lettering": 3, "Co-op Lettering": 4}
SIGN_SIZE = {"Dots Diner": (3.2, 0.8), "Barber Shop": (2.6, 0.62), "Main St Drug": (3.0, 0.7), "Prairie Savings": (5.4, 0.55), "Rosas Pizza": (3.0, 0.72),
             "Hollow Grocery": (3.2, 0.7), "Starline": (3.6, 0.8), "Welcome Sign": (3.6, 1.8), "Water Tower Lettering": (6.6, 1.5), "Co-op Lettering": (7.5, 1.6)}
DECALS = []


def sign_pieces(sign, cx, cy, cz, facing, scale=1.0, y_world=True):
    """The pieces of a sign decal side by side, centred at (cx, cy, cz) on a wall that faces `facing` ('+z', '-z',
    '+x', '-x'); cy is an absolute height."""
    n = SIGN_N[sign]; w, h = SIGN_SIZE[sign]; pw = w / n * scale
    fx, fz = {"+z": (0, 1), "-z": (0, -1), "+x": (1, 0), "-x": (-1, 0)}[facing]
    ux, uz = -fz, fx              # left to right as you face the wall: (-fz, fx) for a wall facing +z is +x... see below
    ux, uz = (1, 0) if facing == "+z" else (-1, 0) if facing == "-z" else (0, -1) if facing == "+x" else (0, 1)
    onto = {"+z": "wall-z", "-z": "wall+z", "+x": "wall-x", "-x": "wall+x"}[facing]
    for k in range(n):
        s = (k - (n - 1) / 2) * pw
        x, z = cx + ux * s + fx * 0.35, cz + uz * s + fz * 0.35
        nm = f"MS {sign}" + (f" {k + 1}" if n > 1 else "")
        op = {"op": "Decal", "decal": nm, "x": R(x), "z": R(z), "y": R(cy), "onto": onto, "reach": 1.2}
        if scale != 1.0:
            op["size"] = R(max(pw, h * scale))
        op.update(W0)
        DECALS.append(op)


def board(cx, cy, cz, w, h, facing, color=(0.16, 0.16, 0.17), depth=0.08, out=0.22):
    """A cosmetic sign board (a mesh, so decals land on it) standing out from a wall that faces `facing`."""
    fx, fz = {"+z": (0, 1), "-z": (0, -1), "+x": (1, 0), "-x": (-1, 0)}[facing]
    yaw = 0 if facing in ("+z", "-z") else 90
    t.mesh("box", cx + fx * out, cz + fz * out, color, y=R(cy - h / 2), width=w, height=h, depth=depth, yaw=yaw, **W0)


def bsign(bname, sign, lx, ly, lz, w, h, board_on=True):
    b = B[bname]; yaw = b[3]; ph = pad_h(bname)
    cx, cz = to_world(b, lx, lz)
    facing = "+z" if yaw == 0 else "-z"
    if board_on:
        board(cx, ph + ly, cz, w, h, facing)
        fx = 0; fz = 1 if yaw == 0 else -1
        sign_pieces(sign, cx + fx * 0.26, ph + ly, cz + fz * 0.26, facing)
    else:
        sign_pieces(sign, cx, ph + ly, cz, facing)


bsign("Barber Shop", "Barber Shop", -1.2, 3.3, 3.72, 2.75, 0.74)
bsign("Diner", "Dots Diner", -2.8, 2.85, 4.12, 3.35, 0.92)
bsign("Corner Shop", "Hollow Grocery", 0.0, 3.25, 4.13, 3.35, 0.82)
bsign("Pizzeria", "Rosas Pizza", -1.5, 3.15, 4.72, 3.15, 0.84)
bsign("Pharmacy", "Main St Drug", 0.6, 2.94, 4.74, 3.15, 0.82)
bsign("Bank", "Prairie Savings", 0.0, 4.95, 7.7, 0, 0, board_on=False)   # carved into the entablature over the columns
gx, gz = to_world(B["Gas Station"], 0.0, 13.1)
sign_pieces("Starline", gx - 3.5, pad_h("Gas Station") + 4.24, gz, "-z", scale=0.58)
sign_pieces("Starline", gx + 3.5, pad_h("Gas Station") + 4.24, gz, "-z", scale=0.58)

# ================================================================ Main Street and Elm Street
GREY = (0.6, 0.59, 0.56)
for zk in (-ROAD, ROAD):                       # kerbstones (cosmetic meshes) along Main Street, broken at Elm
    for xa, xb in ((-33.0, -ELM - 0.2), (ELM + 0.2, 33.5)):
        s = xa
        while s < xb - 0.1:
            e = min(xb, s + 6.0)
            t.mesh("box", (s + e) / 2, zk, GREY, width=R(e - s), height=0.16, depth=0.2, material="Concrete")
            s = e
for xk in (-ELM, ELM):
    for za, zb in ((-17.8, -SIDE - 0.2), (SIDE + 0.2, 17.8)):
        s = za
        while s < zb - 0.1:
            e = min(zb, s + 3.0)
            t.mesh("box", xk, (s + e) / 2, GREY, width=0.2, height=0.16, depth=R(e - s), material="Concrete")
            s = e

# road markings: double yellow centre line, white parking ticks, stop lines, STOP, crosswalks, the RR crossing
for x in np.arange(-50.0, 31.0, 4.0):
    if abs(x) > ELM + 2.2:
        DECALS.append({"op": "Decal", "decal": "Double Yellow Lines", "x": R(x), "z": 0.0, "rotation": 0})
for x in np.arange(-31.0, 32.0, 6.0):
    if abs(x) > ELM + 3.0:
        for zk in (-3.1, 3.1):
            DECALS.append({"op": "Decal", "decal": "Lane Dash", "x": R(x), "z": R(zk - 0.9 * (1 if zk < 0 else -1) * 0), "rotation": 0, "size": 1.0})
for z in np.arange(-19.0, 20.0, 4.0):
    if abs(z) > ROAD + 2.4:
        DECALS.append({"op": "Decal", "decal": "Double Yellow Lines", "x": 0.0, "z": R(z), "rotation": 90})
for sx in (-1, 1):
    for zc in (-3.6, 0.0, 3.6):           # across Main Street (an east-west road: bars turned to run east-west)
        DECALS.append({"op": "Decal", "decal": "Crosswalk Bars", "x": R(sx * (ELM + 2.2)), "z": zc, "rotation": 90, "size": 3.6})
    for xc in (-1.5, 1.5):                 # across Elm Street (north-south road)
        DECALS.append({"op": "Decal", "decal": "Crosswalk Bars", "x": xc, "z": R(sx * (ROAD + 1.6)), "rotation": 0, "size": 3.0})
    DECALS.append({"op": "Decal", "decal": "Stop Line", "x": R(sx * 1.5), "z": R(sx * (ROAD + 3.6)), "rotation": 0, "size": 2.8})
    DECALS.append({"op": "Decal", "decal": "MS Stop Marking", "x": R(sx * 1.5), "z": R(sx * (ROAD + 5.4)), "rotation": 0 if sx > 0 else 180})
for x, z, rot in ((29.0, 2.4, 90), (29.0, -2.4, 90), (45.5, 2.4, 270), (45.5, -2.4, 270)):
    pass
DECALS.append({"op": "Decal", "decal": "MS Railroad Crossing Marking", "x": 28.0, "z": 1.6, "rotation": 270, "size": 4.6})
DECALS.append({"op": "Decal", "decal": "MS Railroad Crossing Marking", "x": 46.0, "z": -1.6, "rotation": 90, "size": 4.6})
DECALS.append({"op": "DecalScatter", "decals": ["Asphalt Patch", "Oil Stain", "Oil Drips", "Floor Cracks", "Oil Stain", "Manhole Cover"], "x0": -32, "z0": -5.2, "x1": 32, "z1": 5.2, "count": 30, "seed": 3, "gap": 2.0})
DECALS.append({"op": "DecalScatter", "decals": ["Floor Cracks", "Water Stain", "Cigarette Butts", "Coffee Stain", "Dirt Patch"], "x0": -33, "z0": -8.5, "x1": 33, "z1": -5.6, "count": 22, "seed": 4, "gap": 1.5})
DECALS.append({"op": "DecalScatter", "decals": ["Floor Cracks", "Water Stain", "Cigarette Butts", "Coffee Stain", "Dirt Patch"], "x0": -33, "z0": 5.6, "x1": 33, "z1": 8.5, "count": 22, "seed": 5, "gap": 1.5})
DECALS.append({"op": "DecalScatter", "decals": ["Tyre Tracks", "Mud Patch", "Puddle", "Footprints", "Gravel Scatter"], "x0": -1.6, "z0": -40, "x1": 1.6, "z1": -22, "count": 8, "seed": 6, "gap": 2.0})
DECALS.append({"op": "DecalScatter", "decals": ["Tyre Tracks", "Mud Patch", "Puddle", "Footprints", "Gravel Scatter"], "x0": -1.6, "z0": 22, "x1": 1.6, "z1": 40, "count": 8, "seed": 7, "gap": 2.0})
DECALS.append({"op": "DecalScatter", "decals": ["Tyre Tracks", "Mud Patch", "Oil Stain", "Gravel Scatter", "Dry Grass"], "x0": -44, "z0": -10, "x1": -34, "z1": 10, "count": 14, "seed": 8, "gap": 2.0})
DECALS.append({"op": "DecalScatter", "decals": ["Oil Stain", "Oil Drips", "Oil Stain", "Floor Cracks"], "x0": 19.5, "z0": 7.5, "x1": 31.5, "z1": 16.5, "count": 12, "seed": 9, "gap": 1.5})

# parked cars along both kerbs (gaps at Elm, the bank and the hydrants)
CARS = ("Sedan", "Hatchback", "SUV", "Pickup Truck", "Van", "Sedan", "Pickup Truck", "Police Car")
for zk, side in ((-ROAD, (0, 1)), (ROAD, (0, -1))):
    for xa, xb in ((-32.0, -ELM - 1.5), (ELM + 1.5, 31.5)):
        tk.kerb_parking(t, (xa, zk), (xb, zk), side, rng, cars=CARS, lane_off=1.2, fill=0.6, meters=False)
# tall blockers in the lanes: a stopped school-style bus and a delivery truck double-parked (break the long street view)
P("Bus", -12.5, -1.6, 180)
P("Box Truck", 15.5, 1.6, 0)
P("Pickup Truck", -1.3, -11.6, 90)                         # parked on Elm north
P("Van", 1.4, 14.0, 270)                                   # and on Elm south

# street furniture along the four sidewalk runs
SKIP_N = [(-27.6 - 0.8, -27.6 + 0.8), (-14.0 - 0.8, -14.0 + 0.8), (14.6 - 2.0, 14.6 + 2.0), (28.4 + 1.6 - 0.8, 28.4 + 1.6 + 0.8)]
for (xa, xb) in ((-33.0, -ELM_SW - 0.6), (ELM_SW + 0.6, 33.5)):
    tk.street_furniture(t, (xa, -SIDE + 0.3), (xb, -SIDE + 0.3), (0, 1), rng, lamp_every=13.0, lamp_kinds=("Street Lamp",), trees=False,
                        kerb_off=2.4, back_off=1.8, extras=[("Bench", "Trash Can"), ("Fire Hydrant",), ("Newspaper Box",), ("Mailbox",), ("Parking Meter",)],
                        skip=[(s0 - xa, s1 - xa) for s0, s1 in SKIP_N])
    tk.street_furniture(t, (xb, SIDE - 0.3), (xa, SIDE - 0.3), (0, -1), rng, lamp_every=13.0, lamp_kinds=("Street Lamp",), trees=False,
                        kerb_off=2.4, back_off=1.8, extras=[("Bench", "Trash Can"), ("Fire Hydrant",), ("Newspaper Box",), ("Parking Meter",)],
                        skip=[(xb - 26.4 - 1.0, xb - 26.4 + 1.0), (xb + 12.4 - 1.5, xb + 12.4 + 1.5)] if xb < 0 else [(xb - 21.0, xb - 6.0)])
# the crossroads: a four-way stop with a flashing signal hung over it, stop signs, street name signs
for sx, sz in ((-1, -1), (1, 1)):
    P("Stop Sign", sx * (ELM + 0.6), sz * (ROAD + 3.0), yaw_facing(0, -sz * 1.0) if True else 0)
for sx, sz in ((1, -1), (-1, 1)):
    P("Stop Sign", sx * (ELM + 0.6), sz * (ROAD + 3.0), yaw_facing(0, sz * 1.0))
P("Street Name Sign", -(ELM + 0.6), -(SIDE - 0.4), 45)
P("Street Name Sign", (ELM + 0.6), (SIDE - 0.4), 225)
P("Traffic Light Arm", -(ELM + 0.9), ROAD + 0.9, 90)
P("Speed Limit Sign", -31.5, -SIDE + 0.6, 90)
P("Speed Limit Sign", 32.0, SIDE - 0.6, 270)
P("Pedestrian Crossing Sign", 4.9, -9.4, 0)
# utility poles along the alleys (wires implied), flags, hanging banners
for x in (-30.5, 4.5, 30.5):
    P("Utility Pole", x, N_ALLEY[0] + 0.3, 0)
    P("Utility Pole", x + 2.0, S_ALLEY[1] - 0.5, 0)
P("Pole Transformer", -10.5, N_ALLEY[0] + 0.3, 0)
P("Flag on a Pole", 22.6, -SIDE + 1.2, 0, flag="us") if False else None
for x, z in ((23.4, -6.9), (7.2, 15.1), (32.6, 8.2), (-6.6, 7.4)):
    t.ops.append({"op": "Prop", "name": "Flag on a Pole", "x": x, "z": z, "flag": "us"})
    occupy(x - 0.3, z - 0.3, x + 0.3, z + 0.3)
for x in (-24.0, -8.5, 8.5, 24.0):
    t.ops.append({"op": "Prop", "name": "Hanging Banner", "x": x, "z": -SIDE + 0.9, "y": 3.4, "flag": "us"})

# ---------------------------------------------------------------- the town square (raised, the bandstand in the middle)
sx0, sz0, sx1, sz1 = SQUARE
scx, scz = (sx0 + sx1) / 2, (sz0 + sz1) / 2
P("Bandstand Gazebo", scx, scz + 0.6, 180)
occupy(scx - 2.9, scz - 2.3, scx + 2.9, scz + 3.5)
P("Memorial Obelisk", scx - 3.2, sz0 + 1.4, 180)
for (bx, bz, yaw) in ((sx0 + 0.9, scz - 1.5, 90), (sx0 + 0.9, scz + 2.5, 90), (sx1 - 0.9, scz - 1.5, 270), (sx1 - 0.9, scz + 2.5, 270), (scx + 2.5, sz1 - 0.8, 180)):
    P("Bench", bx, bz, yaw)
for (lx, lz) in ((sx0 + 0.5, sz0 + 0.5), (sx1 - 0.5, sz0 + 0.5), (sx0 + 0.5, sz1 - 0.5), (sx1 - 0.5, sz1 - 0.5)):
    P("Street Lamp", lx, lz, 45)
for (tx_, tz_) in ((sx0 + 2.0, sz1 - 1.9), (sx1 - 1.6, sz0 + 3.8), (sx1 - 2.0, sz1 - 2.2)):
    t.tree(tx_, tz_, rng.uniform(7.5, 9.5))
P("Drinking Fountain", sx1 - 0.7, sz0 + 1.2, 270)
P("Planter", scx + 3.2, sz0 + 1.0, 0)
P("Post Litter Bin", sx0 + 0.8, sz0 + 3.2, 90)
t.region("grass", 6.0, rect=[R(sx0 + 0.3), R(sz0 + 0.3), R(sx1 - 0.3), R(sz1 - 0.3)], scale=[0.6, 0.9], color=[0.36, 0.5, 0.22], patchiness=0.15)
t.region("bush", 0.5, rect=[R(sx0 + 0.3), R(sz1 - 1.3), R(sx1 - 0.3), R(sz1 - 0.3)], scale=[0.8, 1.2], patchiness=0.1)
t.path([(scx, sz0), (scx, scz - 2.4)], 1.6)
t.path([(sx0, scz + 0.5), (sx1, scz + 0.5)], 1.2)
# the square's retaining wall on its low (south and east) sides, with steps up at the path ends
STONE = (0.62, 0.6, 0.55)
t.mesh("box", scx, sz1 + 0.15, STONE, width=sx1 - sx0 + 0.3, height=0.5, depth=0.3, material="Concrete")
DECALS.append({"op": "DecalScatter", "decals": ["Leaf Litter", "Leaf Pile", "Paper Litter"], "x0": sx0, "z0": sz0, "x1": sx1, "z1": sz1, "count": 10, "seed": 11, "gap": 2})

# ---------------------------------------------------------------- the gas station dressing
gsx, gsz = to_world(B["Gas Station"], 0, 0)
P("Air Compressor" if "Air Compressor" in t.P else "Vending Machine", 30.9, 16.8, 270)
P("Propane Tank", 31.4, 18.8, 0)
P("Trash Can", 25.6, 11.1, 0)
P("Phone Booth" if rng.random() < 0.5 else "Payphone", 20.0, 15.6, 90)
P("Tyre Stack", 31.6, 21.4, 0)

# ================================================================ alleys: back doors, dumpsters, sheds, fences, junk
def alley_dress(z_wall, out, xs, seed):
    for k, x in enumerate(xs):
        tk.bins_by_door(t, (x, z_wall), (1, 0), (0, out), rng, side=1 if k % 2 == 0 else -1, dumpster=k % 2 == 0, seed=seed + k)


alley_dress(-17.0, -1, [-31.0, -24.0, -5.8, 8.0, 24.5, 32.6], 100)
alley_dress(18.1, 1, [-32.0, -21.0, -9.0, -6.9], 120)
for x, z, yaw in ((-33.6, -19.6, 90), (33.9, -19.6, 90), (-33.4, 19.8, 90)):
    P("Garden Shed", x, z, yaw)
P("Pickup Truck", -9.0, N_ALLEY[0] + 1.7, 0)
P("Pallet Stack", -18.0, N_ALLEY[1] - 0.6, 0)
P("Builders Skip", 25.5, S_ALLEY[1] - 0.6 + 2.6, 0) if False else None
P("Hot Dog Cart" if False else "Cardboard Stack", 18.6, S_ALLEY[0] + 0.8, 0)
P("Picnic Table" if "Picnic Table" in t.P else "Park Picnic Table", -16.8, S_ALLEY[1] - 1.0, 90)
# fences: picket and chain link sections between the alleys and the corn, broken up with gaps
def fence_run(name, xa, xb, z, gaps):
    L = t.P[name]["w"]; x = xa
    while x + L <= xb:
        cx = x + L / 2
        if all(not (g0 - L / 2 < cx < g1 + L / 2) for g0, g1 in gaps):
            P(name, cx, z, 0, gap=-0.05, margin=0.3)
        x += L


fence_run("Picket Fence", -33.0, 33.0, N_ALLEY[0] - 0.3, [(-27.0, -22.0), (-3.5, 3.5), (9.0, 13.0), (21.5, 26.0)])
fence_run("Chain Link Fence", -33.0, 33.0, S_ALLEY[1] + 0.1, [(-29.0, -23.0), (-3.5, 3.5), (5.0, 9.5), (17.0, 22.0)])
DECALS.append({"op": "DecalScatter", "decals": ["Oil Stain", "Mud Patch", "Puddle", "Gravel Scatter", "Trash Bag" if False else "Paper Litter"], "x0": -33, "z0": N_ALLEY[0], "x1": 33, "z1": N_ALLEY[1], "count": 16, "seed": 13, "gap": 2})
DECALS.append({"op": "DecalScatter", "decals": ["Oil Stain", "Mud Patch", "Puddle", "Gravel Scatter", "Paper Litter"], "x0": -33, "z0": S_ALLEY[0], "x1": 33, "z1": S_ALLEY[1], "count": 16, "seed": 14, "gap": 2})
# wall dressing: posters on the passage walls and the alley backs, graffiti low down in the alleys
for x, z, onto, dec, y in ((-23.5, -10.5, "wall-x", "MS County Fair Poster", 1.5), (-20.8, -11.5, "wall+x", "MS Lost Dog Flyer", 1.4),
                           (-20.2, 11.0, "wall+x", "MS Homecoming Poster", 1.5), (-18.8, 12.5, "wall-x", "MS County Fair Poster", 1.5),
                           (5.0, -10.5, "wall-x", "MS Lost Dog Flyer", 1.4), (-5.5, 12.0, "wall-x", "MS Homecoming Poster", 1.5)):
    DECALS.append({"op": "Decal", "decal": dec, "x": x, "z": z, "y": y, "onto": onto, "reach": 2.5})
DECALS.append({"op": "DecalScatter", "decals": ["Graffiti Tag", "Grime Streaks", "Peeling Paint", "Rust Bleed", "Torn Poster", "Grime Streaks"], "x0": -33, "z0": -19.2, "x1": 33, "z1": -16.0, "count": 14, "seed": 15, "onto": "wall", "gap": 3})
DECALS.append({"op": "DecalScatter", "decals": ["Graffiti Scribble", "Grime Streaks", "Peeling Paint", "Water Damage", "Torn Poster"], "x0": -33, "z0": 16.0, "x1": 33, "z1": 19.5, "count": 12, "seed": 16, "onto": "wall", "gap": 3})

# ================================================================ the co-op farm yard (Blue, west)
YW = -39.5
P("Grain Bin", -40.0, -16.0, 0)
P("Grain Bin", -40.5, 15.5, 180)
P("Row Crop Tractor", -36.4, -4.0, 200)
P("Hay Wagon", -36.3, 11.6, 90)
P("Farm Fuel Tank", -36.0, 17.2, 90)
P("Big Round Bale", -44.0, -12.4, 0); P("Big Round Bale", -44.6, -14.9, 30)
P("Stock Tank", -35.6, -11.2, 0)
P("Hay Bale", -37.0, 4.8, 0)
P("Wheelbarrow", -38.2, -11.0, 40); P("Jerry Can", -37.6, 18.4, 0)
P("Roadside Farm Stand", -33.6, 9.6, 270) if False else None
P("Roadside Farm Stand", -44.0, -8.6, 0)
P("Pumpkin Pile", -42.2, -8.3, 0); P("Pumpkin Pile", -50.5, 6.2, 0) if False else None
P("Field Scarecrow", -43.0, -19.4, 20)
P("Chicken", -42.0, 3.0, 40); P("Chicken", -41.2, 2.0, 200)
t.ops.append({"op": "Decal", "decal": "MS Co-op Lettering 1", "x": 0, "z": 0}) if False else None
# the welcome signs at both ends of town (a board on posts, the decal on its face)
for (wx, wz, facing) in ((-33.6, -9.8, "-x"), (34.0, 9.8, "+x")):
    h0 = gh(wx, wz)
    t.mesh("box", wx, wz, (0.22, 0.26, 0.2), y=R(h0 + 1.0), width=0.12, height=2.0, depth=3.9, yaw=0, **W0)
    for dz in (-1.6, 1.6):
        t.block(wx - 0.08, h0 - 0.3, wz + dz - 0.08, wx + 0.08, h0 + 1.2, wz + dz + 0.08, (0.4, 0.3, 0.2))
        t.ops[-1].update(W0)
    for fsg in (facing,):
        fx = -1 if facing == "-x" else 1
        n = SIGN_N["Welcome Sign"]; pw = SIGN_SIZE["Welcome Sign"][0] / n
        for k in range(n):
            s = (k - 0.5) * pw * (1 if facing == "+x" else -1)
            DECALS.append({"op": "Decal", "decal": f"MS Welcome Sign {k + 1}", "x": R(wx + fx * 0.4), "z": R(wz - s), "y": R(h0 + 2.0),
                           "onto": "wall+x" if facing == "-x" else "wall-x", "reach": 1.0, **W0})
    occupy(wx - 0.3, wz - 2.1, wx + 0.3, wz + 2.1)

# ================================================================ the rail line and the stopped freight (Red, east)
for z in np.arange(-57.0, 58.0, 6.0):
    t.ops.append({"op": "Prop", "name": "Railroad Track 6m", "x": RAIL_X, "z": R(z), "yaw": 90})
CARS_RAIL = [("Freight Boxcar Open", -13.6), ("Covered Hopper Car", 0.0), ("Freight Boxcar", 13.6)]
for name, z in CARS_RAIL:
    P(name, RAIL_X, z, 90, check=False)
    occupy(RAIL_X - 1.5, z - 6.0, RAIL_X + 1.5, z + 6.0)
for (cx, cz, yaw) in ((33.8, -7.6, 180), (40.2, 7.6, 0)):
    P("Railway Crossing Lights" if "Railway Crossing Lights" in t.P else "Warning Beacon", cx, cz, yaw)
    P("Railroad Crossbuck", cx, cz + (1.2 if yaw == 0 else -1.2), yaw)
P("Concrete Pipe", 31.2, 26.0, 90)                         # the culvert where the creek meets the embankment
P("Junction Box", 40.4, -8.8, 0)
P("Pallet Stack", 41.2, -16.4, 0); P("Pallet", 42.4, 17.0, 20); P("Cable Spool", 43.0, -18.6, 0)
P("Hay Bale", 40.0, 16.0, 90); P("Oil Drum", 40.8, 18.6, 0); P("Rusty Drum", 41.4, 19.4, 0)
P("Big Round Bale", 45.0, -9.6, 90); P("Big Round Bale", 44.4, 8.6, 0)
P("Split Rail Fence", 46.2, -4.4, 0); P("Split Rail Fence", 46.2, 4.4, 0)
P("Stock Tank", 43.2, 16.6, 0)
P("Field Scarecrow", 44.0, -16.5, 200)
P("Utility Pole", 44.0, -2.5, 0) if False else None
DECALS.append({"op": "DecalScatter", "decals": ["Gravel Scatter", "Oil Stain", "Rust Stain", "Dry Grass"], "x0": 35.0, "z0": -30, "x1": 39.0, "z1": 30, "count": 18, "seed": 17, "gap": 2})

# ================================================================ the cornfields
FIELD_KEEP = []   # rectangles kept free of corn (tracks, yards, clearings)


def keep(x0, z0, x1, z1):
    FIELD_KEEP.append((x0, z0, x1, z1))


keep(-2.6, -45, 2.6, 45)                 # the farm tracks along Elm
keep(-33.5, N_ALLEY[0] - 0.2, 33.5, S_ALLEY[1] + 0.2)   # the town
keep(-48.0, -10.5, -33.5, 10.5)          # the farm yard
keep(RAIL_X - 4.6, -60, RAIL_X + 2.8, 60)               # the track bed and its slopes
keep(38.8, -11.0, 53.5, 11.0)            # Red's clearing beyond the tracks
keep(-55, -6.0, -44, 6.0)                # the county road out west
# trampled paths (flanks through the corn) and small clearings
TRAMPLE = [((-30.0, -24.0), (-12.0, -27.5)), ((10.0, -26.0), (26.0, -24.0)), ((-34.0, 24.0), (-40.5, 14.0)), ((34.5, -14.0), (31.5, -24.0)),
           ((-38.0, -12.0), (-31.0, -24.5))]
CLEAR = [(-18.0, -27.0, 2.6), (19.0, -27.3, 2.6), (-34.5, 25.6, 2.4)]


def in_keep(x, z, pad=0.0):
    return any(x0 - pad <= x <= x1 + pad and z0 - pad <= z <= z1 + pad for x0, z0, x1, z1 in FIELD_KEEP)


def near_trample(x, z, w=1.6):
    for (a, b) in TRAMPLE:
        d = math.hypot(*b) and tk._along(a, b)
        L, (ux, uz) = tk._along(a, b)
        s = max(0.0, min(L, (x - a[0]) * ux + (z - a[1]) * uz))
        if math.hypot(x - (a[0] + ux * s), z - (a[1] + uz * s)) < w:
            return True
    return False


CORN = 0; TRAMPLED = 0
SPOTS = []
for gi in range(-16, 17):                    # the north and south bands: rows along X, three patches deep
    for k in range(3):
        for sg in (-1, 1):
            SPOTS.append((gi * 3.0, sg * (23.5 + 3.0 * k), False))
for gi in range(4):                           # the west and east strips beside the yard and Red's clearing: rows along Z
    for gj in range(-7, 8):
        for sg in (-1, 1):
            SPOTS.append((sg * (35.0 + 3.0 * gi), gj * 3.0 + 1.5, True))
for x, z, along_z in SPOTS:
    if not t.inside(x, z, 0.4) or in_keep(x, z, 0.0):
        continue
    if any(math.hypot(x - cx, z - cz) < r + 1.5 for cx, cz, r in CLEAR):
        continue
    if any(math.hypot(x - sx, z - sz) < 3.0 for sx, sz in BLUE_SP + RED_SP + FLAGS):
        continue
    if not t.free((x - 1.45, z - 1.45, x + 1.45, z + 1.45), 0.0):
        continue
    yaw = (90 if along_z else 0) + (180 if rng.random() < 0.5 else 0)
    if near_trample(x, z):
        if t.prop("Trampled Corn", x + rng.uniform(-0.2, 0.2), z + rng.uniform(-0.2, 0.2), rng.uniform(0, 360), check=False, margin=-2.0, tag="corn"):
            TRAMPLED += 1
        continue
    nm = "Corn Patch A" if rng.random() < 0.5 else "Corn Patch B"
    if t.prop(nm, x, z, yaw, check=False, margin=-2.0, tag="corn"):
        CORN += 1
        t.ops[-1]["scale"] = R(rng.uniform(0.95, 1.06))
        t.mark(t.tall, (x - 1.5, z - 1.5, x + 1.5, z + 1.5))   # hides (for the spawn sight check)
# farm dressing out in the fields
P("Wheel Line Irrigator", -10.0, -25.2, 0, check=False)
P("Wheel Line Irrigator", 14.0, 31.0, 0, check=False) if False else None
P("Field Scarecrow", -18.0, -27.0, 160)
P("Field Scarecrow", 19.0, -27.3, 200)
P("Row Crop Tractor", -34.5, 25.6, 300)
P("Big Round Bale", 18.0, -25.4, 0)
P("Hay Wagon", 2.0, -26.5, 90) if False else None
P("Hay Wagon", -1.0, -31.5, 90, check=False)                # on the north track: blocks the long view up Elm
P("Split Rail Fence", -2.6, -33.2, 90); P("Split Rail Fence", 2.6, -33.2, 90)
P("Split Rail Fence", -2.6, 33.2, 90); P("Split Rail Fence", 2.6, 33.2, 90)
P("Row Crop Tractor", 1.0, 31.5, 85, check=False)           # the south track
P("Mailbox", -2.2, 37.4, 90)
for x, z in ((-16.0, 26.4), (-2.0, 25.8), (12.0, 25.6)):
    DECALS.append({"op": "DecalScatter", "decals": ["Mud Patch", "Puddle", "Moss Patch"], "x0": x - 4, "z0": z - 1.2, "x1": x + 4, "z1": z + 1.2, "count": 3, "seed": int(x + 50), "gap": 1.5})

# ================================================================ scenery outside the outline (cosmetic meshes)
def tree_mesh(x, z, h, kind):
    g = gh(x, z)
    if kind == "pine":
        t.mesh("cylinder", x, z, (0.3, 0.22, 0.14), y=R(g - 0.2), width=0.35, height=R(h * 0.3), depth=0.35, segments=6, **W0)
        t.mesh("cone", x, z, (0.17, 0.28, 0.15), y=R(g + h * 0.2), width=R(h * 0.42), height=R(h * 0.8), depth=R(h * 0.42), segments=8, **W0)
    else:
        t.mesh("cylinder", x, z, (0.33, 0.24, 0.15), y=R(g - 0.2), width=0.45, height=R(h * 0.5), depth=0.45, segments=6, **W0)
        t.mesh("icosphere", x, z, rng.choice([(0.25, 0.36, 0.16), (0.29, 0.4, 0.18), (0.22, 0.33, 0.15)]), y=R(g + h * 0.35), width=R(h * 0.62), height=R(h * 0.6), depth=R(h * 0.62), detail=1, **W0)


# a shelter belt of trees on the rise all round, just outside the outline
pts = t.outline; n = len(pts); acc = 0.0; nxt = 0.0
for i in range(n):
    a = pts[i]; b = pts[(i + 1) % n]; L = math.hypot(b[0] - a[0], b[1] - a[1]); acc += L
    if acc < nxt:
        continue
    nxt = acc + rng.uniform(3.5, 6.0)
    ux, uz = (b[0] - a[0]) / L, (b[1] - a[1]) / L
    ix, iz = -uz, ux                         # inward normal (CCW outline)
    if (abs(a[0]) < 6 and abs(a[1]) > 30) or (abs(a[1]) < 8 and abs(a[0]) > 44):
        continue                             # keep the lanes' ends open (the road and tracks run on)
    for depth in (rng.uniform(5.0, 8.0), rng.uniform(10.0, 15.0)):
        x, z = a[0] - ix * depth, a[1] - iz * depth
        tree_mesh(x, z, rng.uniform(8.0, 13.0), "tree" if rng.random() < 0.7 else "pine")
# the roads run on past the lanes
for x0, x1 in ((-78, -53), (53, 78)):
    t.mesh("box", (x0 + x1) / 2, 0, (0.24, 0.25, 0.26), y=-0.02, width=abs(x1 - x0), height=0.05, depth=11.0, material="Concrete")
# the water tower on the rise north-west, the grain elevator past the tracks to the east
WTX, WTZ = -22.0, -50.0
g = gh(WTX, WTZ)
for (lx, lz) in ((-3, -3), (3, -3), (-3, 3), (3, 3)):
    t.mesh("cylinder", WTX + lx, WTZ + lz, (0.55, 0.58, 0.62), y=R(g), width=0.4, height=17.0, depth=0.4, segments=8, material="Metal", **W0)
t.mesh("cylinder", WTX, WTZ, (0.55, 0.58, 0.62), y=R(g), width=0.9, height=17.0, depth=0.9, segments=8, material="Metal", **W0)
t.mesh("cylinder", WTX, WTZ, (0.86, 0.87, 0.85), y=R(g + 16.5), width=9.0, height=5.0, depth=9.0, segments=24, material="Metal", **W0)
t.mesh("cone", WTX, WTZ, (0.78, 0.79, 0.78), y=R(g + 21.5), width=9.4, height=2.4, depth=9.4, segments=24, material="Metal", **W0)
t.mesh("torus", WTX, WTZ, (0.5, 0.52, 0.55), y=R(g + 16.6), radius=4.7, inner=0.12, segments=24, **W0)
DECALS.append({"op": "Decal", "decal": "MS Water Tower Lettering 1", "x": WTX - 2.2, "z": WTZ + 6.0, "y": R(g + 19.0), "onto": "wall-z", "reach": 3.0, "size": 2.2, **W0})
DECALS.append({"op": "Decal", "decal": "MS Water Tower Lettering 2", "x": WTX, "z": WTZ + 6.0, "y": R(g + 19.0), "onto": "wall-z", "reach": 3.0, "size": 2.2, **W0})
DECALS.append({"op": "Decal", "decal": "MS Water Tower Lettering 3", "x": WTX + 2.2, "z": WTZ + 6.0, "y": R(g + 19.0), "onto": "wall-z", "reach": 3.0, "size": 2.2, **W0})
EX, EZ = 64.0, -16.0
g = gh(EX, EZ)
for k in range(4):
    t.mesh("cylinder", EX, EZ - 9 + k * 6.0, (0.78, 0.77, 0.72), y=R(g), width=6.0, height=22.0, depth=6.0, segments=20, material="Concrete", **W0)
t.mesh("box", EX - 4.6, EZ + 1.0, (0.7, 0.68, 0.62), y=R(g), width=4.0, height=30.0, depth=5.0, material="Concrete", **W0)     # head house
t.mesh("box", EX - 4.6, EZ + 1.0, (0.55, 0.53, 0.5), y=R(g + 30.0), width=4.6, height=3.0, depth=5.6, material="Metal", **W0)
t.mesh("box", EX, EZ - 0.0, (0.72, 0.7, 0.65), y=R(g + 22.0), width=6.0, height=2.0, depth=24.0, material="Metal", **W0)        # gallery over the silos
for k in range(4):
    DECALS.append({"op": "Decal", "decal": f"MS Co-op Lettering {k + 1}", "x": R(EX - 8.0), "z": R(EZ - 2.8 + k * 1.9), "y": R(g + 24.5), "onto": "wall+x", "reach": 5.0, **W0})
for k in range(3):
    gx_, gz_ = 58.5 - k * 0.0, 8.0 + k * 6.5
    gg = gh(gx_, gz_)
    t.mesh("cylinder", gx_, gz_, (0.7, 0.72, 0.72), y=R(gg), width=5.6, height=6.0, depth=5.6, segments=16, material="Metal", **W0)
    t.mesh("cone", gx_, gz_, (0.66, 0.68, 0.68), y=R(gg + 6.0), width=5.8, height=2.0, depth=5.8, segments=16, material="Metal", **W0)
# corn running on past the edge (looks only: a carpet of corn-green, so the field doesn't stop at the outline)
# (the shelter belt and the rise hide most of it; the lanes look out over it)

# ================================================================ foliage
t.regions += []
FOL_REG = t.regions
FOL_REG.append({"type": "grass", "density": 3.0, "rect": [-56, -44, 56, 44], "scale": [0.7, 1.2], "color": [0.5, 0.55, 0.28], "patchiness": 0.45})
FOL_REG.append({"type": "shrub", "density": 0.25, "rect": [-56, -44, 56, 44], "scale": [0.8, 1.3], "color": [0.44, 0.5, 0.24], "patchiness": 0.5})
FOL_REG.append({"type": "grass", "density": 6.0, "polygon": [[-26, 23.0], [30, 23.0], [30, 28.8], [-26, 28.8]], "scale": [1.2, 1.9], "color": [0.36, 0.5, 0.22], "patchiness": 0.4})
FOL_REG.append({"type": "shrub", "density": 1.0, "polygon": [[-26, 23.0], [30, 23.0], [30, 28.8], [-26, 28.8]], "scale": [1.0, 1.5], "patchiness": 0.4})
FOL_REG.append({"type": "bush", "density": 0.35, "rect": [-46.0, -10.5, -33.5, 10.5], "scale": [0.8, 1.2], "patchiness": 0.6})
for (tx_, tz_, h) in ((-36.8, -14.0, 9.5), (-36.0, 17.0, 10.0), (42.5, -9.5, 8.0), (42.0, 16.5, 8.5), (-14.0, 24.0, 9.0), (8.0, 24.2, 8.5), (24.5, 23.0, 8.0),
                      (-33.0, 24.0, 10.0), (32.5, -23.5, 9.0)):
    t.tree(tx_, tz_, h)
t.path([(-52, 0), (52, 0)], 11.0)
t.path([(0, -41), (0, 41)], 3.4, density=0.15)
t.path([(RAIL_X, -60), (RAIL_X, 60)], 4.4, density=0.05)
for (a, b) in TRAMPLE:
    t.path([a, b], 2.4, density=0.3)
foliage = {"seed": 501, "surfaces": {"Rock": 0.0, "Sand": 0.06, "Concrete": 0.0, "Tiles": 0.08}, "paths": t.paths, "regions": t.regions, "items": t.items, "exclude": t.exclude}

# ================================================================ spawns, waypoints, clutter
for x, z in BLUE_SP + RED_SP:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
# litter: the street, the alleys, the yards
t.clutter(["Litter", "Newspaper", "Soda Cans", "Plastic Bag", "Cigarette Bin" if False else "Litter", "Bottle", "Leaf Litter" if False else "Newspaper"], -33, -8.4, 33, -5.8, 40, 31)
t.clutter(["Litter", "Newspaper", "Soda Cans", "Plastic Bag", "Bottle"], -33, 5.8, 33, 8.4, 40, 32)
t.clutter(["Litter", "Trash Bag", "Plastic Bag", "Soda Cans", "Bottles", "Flattened Cardboard", "Plank", "Cardboard Box Small", "Tyre"], -33, N_ALLEY[0], 33, N_ALLEY[1], 70, 33)
t.clutter(["Litter", "Trash Bag", "Plastic Bag", "Soda Cans", "Bottles", "Flattened Cardboard", "Plank", "Cardboard Box Small", "Tyre"], -33, S_ALLEY[0], 33, S_ALLEY[1], 70, 34)
t.clutter(["Hay Bale", "Pallet", "Tyre", "Plank Pile", "Bucket", "Oil Drum", "Litter", "Plank"], -46, -10, -34, 10, 24, 35)
t.clutter(["Pallet", "Plank", "Tyre", "Litter", "Rusty Drum", "Cinder Blocks"], 39, -11, 52, 11, 18, 36)
t.clutter(["Litter", "Plastic Bag", "Soda Cans", "Newspaper"], -2.5, -40, 2.5, 40, 16, 37)
t.ops.append({"op": "AutoWaypoints", "spacing": 4})

# hand waypoints in the tight spots (passages, behind the train, the square, the yard)
for (x, z) in ((-22.3, -14.0), (-19.5, 14.0), (0, -19.7), (0, 19.8), (scx, scz + 0.6), (scx, sz0 + 0.8), (RAIL_X - 3.5, -6.8), (RAIL_X + 3.0, 6.8),
               (RAIL_X + 3.0, -6.8), (RAIL_X - 3.5, 6.8), (-39.5, 0.0), (44.0, 0.0), (-24.0, -27.0), (24.0, -27.0), (-20.0, 27.0), (20.0, 27.0)):
    t.ops.append({"op": "Waypoint", "x": R(x), "z": R(z)})

# ================================================================ write the map
ops = t.ops + DECALS
mp = {
    "format": "call-of-slop-map/1",
    "name": "Main Street",
    "description": "Prairie Hollow, a small farm town: shops and a diner on Main Street, alleys behind, a stopped freight train, cornfields all round.",
    "author": "Call of Slop",
    "order": 501,
    "maxPlayers": 12,
    "flagPool": ["us"],
    "sky": "#a9c4dc",
    "skybox": {"preset": "clear", "sun": {"time": 17.2, "latitude": 41, "day": 235}, "clouds": {"coverage": 0.35, "speed": 6},
               "haze": {"color": "#f0d8a8", "strength": 0.25}},
    "boundary": {"shape": "polygon", "points": [list(p) for p in POLY], "noise": NOISE,
                 "edge": {"style": "cliff", "height": 4.5, "width": 7}, "groundMargin": 12},
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "terrain": TERRAIN,
    "ops": ops,
    "foliage": foliage,
}


def dump(o, ind=0):
    pad_ = "  " * ind
    if isinstance(o, dict) and ind <= 1 and any(isinstance(v, (dict, list)) for v in o.values()):
        return "{\n" + ",\n".join(f'{pad_}  "{k}": {dump(v, ind + 1)}' for k, v in o.items()) + "\n" + pad_ + "}"
    if isinstance(o, list) and ind <= 2 and o and isinstance(o[0], dict):
        return "[\n" + ",\n".join(pad_ + "  " + json.dumps(v) for v in o) + "\n" + pad_ + "]"
    return json.dumps(o)


def clean(o):
    if isinstance(o, float):
        return round(o, 3)
    if isinstance(o, (np.floating,)):
        return round(float(o), 3)
    if isinstance(o, dict):
        return {k: clean(v) for k, v in o.items()}
    if isinstance(o, list):
        return [clean(v) for v in o]
    return o


os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
with open(OUT_JSON, "w") as f:
    f.write(dump(clean(mp)) + "\n")

# ================================================================ report
for x, z in BLUE_SP + RED_SP + [(h[0], h[1]) for h in HILLS] + FLAGS:
    if not t.inside(x, z, 1.5):
        print("WARN objective near/outside the edge", x, z)
    if not t.free((x - 0.4, z - 0.4, x + 0.4, z + 0.4)):
        print("WARN objective inside something", x, z)
from collections import Counter
cats = Counter(); solid = 0
for name, x, z, yaw, y, tag in t.props:
    cats[t.P[name]["cat"]] += 1; solid += t.P[name]["solid"]
print(f"area {t.area():.0f} m2, blocks {sum(1 for o in t.ops if o['op'] == 'Block')}, trees {len(t.items)}, meshes {t.meshes}, hand props {len(t.props)} (solid {solid}), corn {CORN} + trampled {TRAMPLED}, decal ops {len(DECALS)}")
print("props by category:", dict(sorted(cats.items())))
print("didn't fit:", len(FAILED), FAILED[:40])
print("terrain height range", round(float(TER.h.min()), 2), round(float(TER.h.max()), 2), "grid", TER.nx, "x", TER.nz)
vis = [(a, b) for a in BLUE_SP for b in RED_SP if t.sees(a, b)]
print("blue-red spawn lines of sight:", len(vis), vis[:4])
fb = t.walk_field(BLUE_SP); fr = t.walk_field(RED_SP)
for i, (x, z, r) in enumerate(HILLS):
    b_ = t.walk_to(fb, x, z); r_ = t.walk_to(fr, x, z)
    print(f"hill {i + 1} ({x},{z}): walk blue {b_:.0f} m red {r_:.0f} m ({(b_ - r_) / max(b_, r_) * 100:+.1f}%)")
b_, r_ = t.walk_to(fb, *FLAGS[1]), t.walk_to(fr, *FLAGS[0])
print(f"enemy flag: blue {b_:.0f} m red {r_:.0f} m")
if OUT_PNG:
    marks = [("dot", x, z, 0.8, (40, 90, 240)) for x, z in BLUE_SP] + [("dot", x, z, 0.8, (240, 50, 50)) for x, z in RED_SP]
    marks += [("circle", x, z, r, (255, 230, 0)) for x, z, r in HILLS] + [("dot", x, z, 1.0, (255, 255, 255)) for x, z in FLAGS]
    marks += [("line", a, b, 0, (255, 0, 255)) for a, b in vis]
    for b in BUILDINGS:
        x0, z0, x1, z1 = world_rect(b); t.boxes.append((x0, z0, x1, z1, 0, 4, (0.55, 0.4, 0.35)))
    t.preview(OUT_PNG, marks, scale=8)
