#!/usr/bin/env python3
"""Generator for Maps/World/07-Village.json: the World Tour pack's 07 Village (small, England, 12 players).

Usage: python3 tools/world/village.py [preview.png]      (run from anywhere; writes the map file in the repository)

A Cotswold village on a south-facing hillside, honey limestone under slate and thatch. The parish church stands on its
raised churchyard at the top of the hill with the allotments beside it (Blue's end); Church Lane runs below the
churchyard wall; the sloping village green in the middle has the memorial cross, the oaks, a bus shelter and the red
phone box; West Lane and East Lane run down either side of the green past a stone cottage and a terrace of stone
houses; the High Street at the bottom has the village shop and post office, the pub and its beer garden, and a
thatched cottage (Red's end); a stream comes in from the west, runs through the cottage gardens, under the High
Street's stone bridge and away across the meadow. Lanes jut out of the hedged outline where they leave the village
(the High Street both ways, Church Lane east, a footpath north from the churchyard and one south from the High Street),
and the land beyond rises into wooded hills.

Not mirrored: the two ends differ (churchyard and allotments uphill against the pub, shop and meadow downhill), so the
generator checks the walks instead (see check_walks: equal within about 10% for both teams to each hill and the enemy
flag, on a 0.5 m grid over the open ground and the building doors).
"""
import json
import math
import os
import random
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "cluttered"))
sys.path.insert(0, os.path.join(REPO, "tools", "buildings"))
import placement as pl  # noqa: E402
import terrain as tr  # noqa: E402
import bldgen  # noqa: E402

OUT_JSON = os.path.join(REPO, "Maps", "World", "07-Village.json")
OUT_PNG = sys.argv[1] if len(sys.argv) > 1 else None

# ================================================================================================ the outline
# A hedged village outline (not a rectangle): bevelled corners, and lanes jutting out where they leave the village.
OUTLINE = [
    (-38.0, -28.0), (-32.5, -34.5), (2.0, -35.0),
    (2.0, -41.0), (7.0, -41.0), (7.0, -35.0),                       # the footpath north out of the churchyard
    (22.0, -35.0), (34.5, -32.0), (39.0, -25.5), (39.0, -16.0),
    (46.0, -16.0), (46.0, -8.5), (39.0, -8.5),                      # Church Lane east, towards the next village
    (39.5, 8.5),
    (46.0, 8.5), (46.0, 15.5), (39.5, 15.5),                        # the High Street east
    (39.0, 26.5), (33.0, 33.0), (-3.0, 33.5),
    (-3.0, 40.0), (-8.0, 40.0), (-8.0, 33.5),                       # the footpath south over the fields
    (-21.0, 33.5), (-32.5, 32.0), (-39.0, 25.5), (-39.5, 15.5),
    (-46.0, 15.5), (-46.0, 8.5), (-39.5, 8.5),                      # the High Street west, over the bridge
    (-39.5, -6.0), (-40.0, -20.0),
]
NOISE = {"amplitude": 0.35, "scale": 9, "seed": 7}
HALF_X = max(abs(p[0]) for p in OUTLINE)
HALF_Z = max(abs(p[1]) for p in OUTLINE)

def prop_library():
    """placement.PropLib, minus the files in Props that aren't props (the flag designs in Props/Flags)."""
    import glob
    out = pl.PropLib.__new__(pl.PropLib)
    out.props = {}
    for f in sorted(glob.glob(os.path.join(REPO, "Props", "**", "*.json"), recursive=True)) + \
            sorted(glob.glob(os.path.join(REPO, "Maps", "World", "Props", "**", "*.json"), recursive=True)):
        d = json.load(open(f))
        if isinstance(d, dict) and "name" in d and "parts" in d:
            out.props[d["name"]] = pl.PropInfo(d, f)
    return out


lib = prop_library()
S = pl.Site(lib, outline=OUTLINE, mirror=None, margin=1.3, cosmetic_margin=0.7)
fol = pl.FoliageBuilder(S, seed=7)
blib = bldgen.Library(REPO)

# ================================================================================================ palette
GRASS = "#5f8a3a"
ASPHALT = "#45464a"
PAVE = "#9a968c"
ROCKC = "#6e6656"
STONE = "#b89c66"
STONE_L = "#c8ae78"
STONE_D = "#9a8256"
KERB = "#b4b0a4"
HEDGE = "#2f5426"
WATER = "#4f6e6a"
WHITE = "#e8e8e2"

# ================================================================================================ terrain
# The hill falls south at 9% (5 degrees): about 6.1 m at the churchyard's north wall, 3 m on the green, 1 m on the
# High Street's south side. The churchyard is a raised plateau behind a retaining wall; pads level the buildings;
# the lanes are terrain ramps painted "sand" (the asphalt colour); pavements and paths are painted "dirt" (paving grey).


def H0(x, z):
    """The hillside before anything is cut or built (the grid feature reproduces this)."""
    return 3.0 - 0.09 * z


CHURCHYARD = (-35.0, -35.0, 12.0, -16.3)   # x0, z0, x1, z1 of the plateau's flat top
YARD_H = 6.3

STREAM = [(-50, -4, 1.2), (-41, -3, 1.3), (-36, 1, 1.4), (-33, 6, 1.8), (-31.6, 10, 2.5), (-31.2, 12, 2.75),
          (-31.0, 14, 2.5), (-30.6, 18, 1.7), (-29.2, 25, 1.4), (-27.4, 32, 1.3), (-26.5, 44, 1.2)]
STREAM_W = 7.5
BRIDGE = (-35.8, -26.2)                    # deck from x0 to x1 over the stream on the High Street (z 9..15)

ROADS = {   # name: (points, width) ramps painted asphalt; heights follow the hillside
    "high_w": ([(-52, 12.0), (-41, 12.0), (-35.6, 12.0)], 6.0),
    "high_e": ([(-26.4, 12.0), (-17, 12.0), (0, 12.3), (17, 12.0), (30, 11.6), (41, 12.0), (52, 12.0)], 6.0),
    "church": ([(-17, -12.0), (0, -12.2), (17, -12.0), (30, -12.6), (40.5, -12.3), (52, -12.3)], 5.0),
    "west": ([(-17, -12.0), (-17, 12.0)], 5.0),
    "east": ([(17, -12.0), (17, 12.0)], 5.0),
}
PATHS = {   # pavements and footpaths, painted paving grey
    "pave_high_n": ([(-26.4, 8.1), (40.5, 8.1)], 1.8),
    "pave_high_s": ([(-24.0, 15.9), (40.5, 15.9)], 1.8),
    "pave_church_s": ([(-14.5, -8.9), (14.5, -8.9)], 1.2),
    "pave_west_e": ([(-13.8, -8.9), (-13.8, 8.1)], 1.4),
    "pave_east_w": ([(13.8, -8.9), (13.8, 8.1)], 1.4),
    "path_north": ([(4.5, -35.0), (4.5, -45.0)], 2.0),
    "path_south": ([(-5.5, 16.5), (-5.5, 26.0), (-5.5, 34.0), (-5.5, 44.0)], 2.0),
    "path_green": ([(-10.0, -8.5), (-3.0, -4.6), (3.0, -4.6), (10.0, 7.5)], 1.6),
    "path_meadow": ([(-24.0, 16.5), (-27.0, 22.0), (-36.0, 27.0)], 1.4),
}
PADS = []   # (x, z, w, d, height) filled in by the building placements

BUILDINGS = [
    # name, at, yaw, seed, variant
    ("Church", (-2.0, -25.0), 270, 3, {"palette": "honey limestone"}),
    ("Stone Cottage", (-26.0, -1.0), 90, 5, {"palette": "honey stone"}),
    ("Row Houses", (29.0, 0.0), 270, None, {"palette": "yellow stone"}),
    ("Post Office Stores", (-16.0, 21.0), 180, 7, {"palette": "honey stone"}),
    ("Pub", (10.0, 21.0), 180, 8, {"palette": "cotswold stone"}),
    ("Thatched Cottage", (28.0, 21.0), 180, 9, {"palette": "honey stone"}),
]
PLANS = {}
for name, (bx, bz), yaw, seed, var in BUILDINGS:
    d = blib.find(name)
    plan = bldgen.generate(blib, d, var, seed or 0, (bx, 0, bz), yaw // 90)
    PLANS[name] = plan
    xs = [r[0] for r in plan.footprint] + [r[2] for r in plan.footprint]
    zs = [r[1] for r in plan.footprint] + [r[3] for r in plan.footprint]
    fx0, fx1, fz0, fz1 = min(xs), max(xs), min(zs), max(zs)
    if name != "Church":     # the church stands on the churchyard plateau
        PADS.append(((fx0 + fx1) / 2, (fz0 + fz1) / 2, fx1 - fx0 + 2.4, fz1 - fz0 + 2.4, round(H0(bx, bz), 2)))

HILLS = [(0.0, -0.5, 4.0), (-16.4, -1.6, 3.0), (16.4, -0.6, 3.0)]   # x, z, radius (KotH)

features = [
    # the hillside: a grid of heights (8 m cells) adding the 9% fall, plus the wooded hills that rise beyond the hedge
    None,
    {"type": "noise", "amplitude": 0.25, "scale": 14, "seed": 11},
    {"type": "hill", "x": -10, "z": -70, "radius": 34, "radiusZ": 22, "height": 9},
    {"type": "hill", "x": 36, "z": -58, "radius": 22, "height": 6},
    {"type": "hill", "x": -62, "z": -30, "radius": 22, "radiusZ": 30, "height": 7},
    {"type": "hill", "x": 66, "z": 0, "radius": 20, "radiusZ": 26, "height": 5},
    {"type": "hill", "x": -60, "z": 40, "radius": 24, "height": 4},
    {"type": "hill", "x": 30, "z": 64, "radius": 26, "radiusZ": 16, "height": 3.5},
    {"type": "valley", "points": [list(p) for p in STREAM], "width": STREAM_W, "material": "rock"},
    {"type": "plateau", "w": CHURCHYARD[2] - CHURCHYARD[0], "d": CHURCHYARD[3] - CHURCHYARD[1],
     "x": (CHURCHYARD[0] + CHURCHYARD[2]) / 2, "z": (CHURCHYARD[1] + CHURCHYARD[3]) / 2, "height": YARD_H, "edge": 1.0, "mode": "fill"},
]


def grid_feature():
    x0, z0, cell = -(HALF_X + 24), -(HALF_Z + 24), 8.0
    cols = int((2 * (HALF_X + 24)) / cell) + 2
    rows = int((2 * (HALF_Z + 24)) / cell) + 2
    hs = []
    for r in range(rows):
        for c in range(cols):
            hs.append(round(H0(x0 + c * cell, z0 + r * cell), 3))
    return {"type": "grid", "x0": x0, "z0": z0, "cell": cell, "cols": cols, "rows": rows, "heights": hs, "mode": "add"}


features[0] = grid_feature()
for x, z, w, d, h in PADS:
    features.append({"type": "flatten", "x": round(x, 2), "z": round(z, 2), "w": round(w, 2), "d": round(d, 2), "height": h, "edge": 1.6})
for x, z, r in HILLS:
    features.append({"type": "flatten", "x": x, "z": z, "radius": r + 0.5, "edge": 3})
for key, (pts, w) in PATHS.items():
    features.append({"type": "ramp", "points": [list(p) for p in pts], "width": w, "edge": 0.6, "material": "dirt"})
for key, (pts, w) in ROADS.items():
    features.append({"type": "ramp", "points": [list(p) for p in pts], "width": w, "edge": 0.8, "material": "sand"})
# the churchyard's ways in: steps up through the lychgate, a path up from the cottage's orchard, two gaps from the allotments
STEPS = [
    ([(-7.5, -11.5), (-7.5, -15.2), (-7.5, -18.6, YARD_H)], 2.6),
    ([(-30.0, -7.5), (-30.0, -15.0), (-30.0, -18.6, YARD_H)], 2.2),
    ([(6.0, -11.0), (6.0, -15.2), (6.0, -17.8, YARD_H)], 2.0),
    ([(16.0, -27.0), (12.8, -27.0), (10.6, -27.0, YARD_H)], 2.2),
    ([(16.0, -20.0), (12.8, -20.0), (10.6, -20.0, YARD_H)], 2.2),
]
for pts, w in STEPS:
    features.append({"type": "ramp", "points": [list(p) for p in pts], "width": w, "edge": 0.8, "material": "dirt"})

TERRAIN = {
    "cell": 0.5,
    "margin": 22,
    "surface": {"grass": GRASS, "dirt": PAVE, "rock": ROCKC, "sand": ASPHALT, "dirtSlope": 70, "rockSlope": 33, "variation": 0.07},
    "features": features,
}
T = tr.generate(TERRAIN, HALF_X, HALF_Z)


def G(x, z):
    """The finished ground height at (x, z) (the game's terrain, from the Python port)."""
    return float(T.sample(x, z))


# ================================================================================================ ops: buildings
S.raw({"op": "Ground", "color": GRASS})
for name, (bx, bz), yaw, seed, var in BUILDINGS:
    op = {"op": "Building", "name": name, "at": [bx, bz], "yaw": yaw, "seed": seed, "variant": var}
    if seed is None:
        del op["seed"]
    S.raw(op)
    plan = PLANS[name]
    for b in plan.shell:          # building walls are solid for the placement rules (ground floor, below head height)
        x0, y0, z0, x1, y1, z1 = b[:6]
        if y0 < 2.0 and y1 > 0.3:
            S.occupy((x0, z0, x1, z1), 0.0, 3.0, kind="box")
    for r in plan.footprint:
        S.occupy((r[0] - 0.05, r[1] - 0.05, r[2] + 0.05, r[3] + 0.05), 0.0, 3.0, kind="building")
    for dd in plan.doors:          # keep the doorways' outsides clear
        if dd["floor"] == 0:
            ox, _, oz = dd["other"]
            S.keepout(ox, oz, 1.1)

# ================================================================================================ objectives, spawns
BLUE_SP = [(-30.0, -27.0), (-24.0, -31.0), (-33.0, -20.5), (26.0, -30.8), (26.0, -22.0), (35.0, -27.6)]
RED_SP = [(-34.5, 23.0), (-30.5, 29.5), (-17.0, 29.5), (8.0, 29.5), (27.0, 29.5), (35.6, 21.5)]
FFA_SP = [(0.0, -32.8), (0.0, 30.0)]
FLAGS = [(-25.5, -24.0), (25.5, 29.5)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    S.keepout(x, z, 2.4)
    S.spawn(x, z)
for x, z, r in HILLS:
    S.keepout(x, z, r + 0.4)
for x, z in FLAGS:
    S.keepout(x, z, 2.4)

# ================================================================================================ ops: helpers


def gy(x, z):
    return round(G(x, z), 3)


def block_world(x0, y0, z0, x1, y1, z1, color, solid=True):
    """A Block at absolute heights (anchor world)."""
    x0, x1 = min(x0, x1), max(x0, x1)
    z0, z1 = min(z0, z1), max(z0, z1)
    S.raw({"op": "Block", "x0": round(x0, 3), "y0": round(y0, 3), "z0": round(z0, 3), "x1": round(x1, 3), "y1": round(y1, 3), "z1": round(z1, 3),
           "color": color, "anchor": "world"})
    S.counts["block"] += 1
    if solid:
        S.occ.append(((x0, z0, x1, z1), 0.0, 3.0, "box"))


def mesh_world(kind, x, z, y, color, **kw):
    op = {"op": "Mesh", "mesh": kind, "x": round(x, 3), "z": round(z, 3), "y": round(y, 3)}
    for k, v in kw.items():
        if v is not None:
            op[k] = round(v, 3) if isinstance(v, float) else v
    op["color"] = color
    op["anchor"] = "world"
    S.raw(op)
    S.counts["mesh"] += 1


def strip_world(ax, az, bx, bz, width, height, color, material=None, lift=0.0, piece=3.0):
    """A cosmetic strip (kerb, coping, water) from a to b in pieces that follow the ground, each tipped to the slope."""
    L = math.hypot(bx - ax, bz - az)
    n = max(1, int(math.ceil(L / piece)))
    yaw = pl.along_yaw(bx - ax, bz - az)
    for k in range(n):
        t0, t1 = k / n, (k + 1) / n
        x0, z0 = ax + (bx - ax) * t0, az + (bz - az) * t0
        x1, z1 = ax + (bx - ax) * t1, az + (bz - az) * t1
        h0, h1 = G(x0, z0), G(x1, z1)
        seg = L / n
        roll = math.degrees(math.atan2(h1 - h0, seg))
        mesh_world("box", (x0 + x1) / 2, (z0 + z1) / 2, min(h0, h1) - 0.02 + lift, color, width=seg + 0.04, height=height, depth=width,
                   yaw=round(yaw, 2) or None, roll=round(roll, 2) or None, material=material)


def wall_props(ax, az, bx, bz, gaps=(), face="along", check=True):
    """A run of dry-stone wall (2 m and 1 m pieces) from a to b, leaving `gaps` (distances along the run, width)."""
    L = math.hypot(bx - ax, bz - az)
    tx, tz = (bx - ax) / L, (bz - az) / L
    yaw = pl.along_yaw(tx, tz)
    out = []
    d = 0.0
    while d < L - 0.45:
        piece = 2.0 if d + 2.0 <= L + 0.05 else 1.0
        if any(g0 - 0.01 < d + piece and d < g0 + gw for g0, gw in gaps):
            d += 1.0
            continue
        c = d + piece / 2
        p = S.prop("Drystone Wall 2m" if piece == 2.0 else "Drystone Wall 1m", ax + tx * c, az + tz * c, yaw, check=check, lanes=False, gap=-0.05)
        if p:
            out.append(p)
        d += piece
    return out


def flat_y(name, x, z, yaw=0.0, **kw):
    return S.prop(name, x, z, yaw, **kw)


# ================================================================================================ the stream and the bridge
S.rule("stream")
deck_y = gy(BRIDGE[0] - 0.5, 12.0)
block_world(BRIDGE[0], deck_y - 0.55, 9.0, BRIDGE[1], deck_y + 0.02, 15.0, STONE_D)                    # the deck
for z0, z1 in ((8.55, 9.0), (15.0, 15.45)):
    block_world(BRIDGE[0] - 0.4, deck_y, z0, BRIDGE[1] + 0.4, deck_y + 1.0, z1, STONE)                 # parapets
    mesh_world("box", (BRIDGE[0] + BRIDGE[1]) / 2, (z0 + z1) / 2, deck_y + 1.0, STONE_L, width=BRIDGE[1] - BRIDGE[0] + 0.9, height=0.12, depth=0.5)
for x0, x1 in ((BRIDGE[0], BRIDGE[0] + 1.3), (BRIDGE[1] - 1.3, BRIDGE[1])):
    block_world(x0, deck_y - 3.2, 8.55, x1, deck_y - 0.5, 15.45, STONE)                                # abutments
for zf in (8.5, 15.5):   # the arch ring on both faces (cosmetic), darker voussoirs over the stream
    mesh_world("box", (BRIDGE[0] + BRIDGE[1]) / 2, zf, deck_y - 0.75, STONE_D, width=BRIDGE[1] - BRIDGE[0] - 2.4, height=0.3, depth=0.12)
# water along the bed (cosmetic), a little above the lowest ground of each piece
pts = [(p[0], p[1]) for p in STREAM]
dense = pl.resample(pts, 2.5, start=0)
for (x0, z0, tx, tz) in dense:
    if not (-46 < z0 < 37):
        continue
    bed = min(G(x0 + dx, z0 + dz) for dx, dz in ((0, 0), (0.4, 0), (-0.4, 0), (0, 0.4), (0, -0.4)))
    mesh_world("box", x0, z0, bed + 0.08, WATER, width=2.9, height=0.03, depth=1.5, yaw=round(pl.along_yaw(tx, tz), 1), material="Tiles")
S.occupy((BRIDGE[0] - 0.4, 8.5, BRIDGE[1] + 0.4, 15.5), 0.0, 3.0, kind="box")

# ================================================================================================ the churchyard wall
S.rule("walls")
yard_wall_z = (-15.95, -15.35)
gaps_x = [(-9.0, -6.0), (-31.2, -28.8), (4.8, 7.2)]
segments = []
cur = CHURCHYARD[0]
for g0, g1 in sorted(gaps_x):
    segments.append((cur, g0))
    cur = g1
segments.append((cur, CHURCHYARD[2] + 0.6))
for a, b in segments:
    if b - a < 0.3:
        continue
    k = a
    while k < b - 0.01:       # 4 m blocks, each from the lane's ground to 0.7 m above the churchyard
        e = min(b, k + 4.0)
        low = min(G(k, yard_wall_z[1] + 0.3), G(e, yard_wall_z[1] + 0.3)) - 0.4
        block_world(k, low, yard_wall_z[0], e, YARD_H + 0.7, yard_wall_z[1], STONE)
        mesh_world("box", (k + e) / 2, sum(yard_wall_z) / 2, YARD_H + 0.7, STONE_L, width=e - k + 0.02, height=0.1, depth=0.75)
        k = e
# the east side of the churchyard (to the allotments), with the two gaps where the steps come up
for z0, z1 in ((-36.0, -28.2), (-25.8, -21.2), (-18.8, -15.95)):
    low = min(G(12.9, z0), G(12.9, z1)) - 0.3
    block_world(12.0, low, z0, 12.6, YARD_H + 0.7, z1, STONE)
    mesh_world("box", 12.3, (z0 + z1) / 2, YARD_H + 0.7, STONE_L, width=0.75, height=0.1, depth=z1 - z0 + 0.02)
S.prop("Lychgate", -7.5, -17.75, 0, check=False)
for x in (-9.3, -5.7):
    S.prop("Stone Gate Pier", x, -15.65, 0, check=False)
for x in (-31.6, -28.4):
    S.prop("Stone Gate Pier", x, -15.65, 0, check=False)

# ================================================================================================ lanes: walls, gates, kerbs
# Church Lane, north side east of the churchyard: the allotments' wall, with the field gate standing open
wall_props(13.2, -15.65, 40.0, -15.65, gaps=[(12.6, 3.4)])
S.prop("Five Bar Gate", 27.4, -17.4, 90, lanes=False)       # swung open against the wall, inside
for x in (25.6, 29.4):
    S.prop("Stone Gate Pier", x, -15.65, 0, check=False)
# West Lane, west side: the cottage's front garden wall with its gate
wall_props(-19.9, -9.4, -19.9, 9.4, gaps=[(8.1, 1.8)])
# East Lane, east side: the terrace's front garden walls, a gap at each front door
wall_props(19.9, -9.2, 19.9, 9.0, gaps=[(5.0, 1.6), (10.0, 1.6), (15.0, 1.6)])
# the High Street, south side: the meadow wall west of the shop, the cottage's front wall east of the pub
wall_props(-40.0, 17.1, -25.0, 17.1, gaps=[(14.0, 1.0)])
wall_props(23.0, 17.0, 39.0, 17.0, gaps=[(3.6, 1.4)])
# the High Street, north side west of West Lane: the cottage orchard's wall along the bridge approach
wall_props(-25.8, 8.6, -19.9, 8.6)
wall_props(-40.5, 8.6, -36.4, 8.6)

# kerbs (cosmetic, following the slope)
S.rule("kerbs")
KERBS = [
    ((-26.4, 9.05), (40.5, 9.05)), ((-24.0, 14.95), (40.5, 14.95)), ((-52, 9.05), (-36.0, 9.05)), ((-52, 14.95), (-36.0, 14.95)),
    ((-14.5, -9.5), (14.5, -9.5)), ((-14.55, -9.5), (-14.55, 9.0)), ((14.55, -9.5), (14.55, 9.0)),
]
for a, b in KERBS:
    strip_world(*a, *b, 0.22, 0.14, KERB, "Concrete")

# ================================================================================================ the green
S.rule("green")
MEM = (0.0, -6.45)
S.prop("Memorial Cross", *MEM, 180)
for k, (x, z, yaw) in enumerate(((-3.4, -6.45, 90), (3.4, -6.45, 270), (-7.5, 4.8, 120), (8.2, -4.4, 300))):
    S.prop("Bench", x, z, yaw, lanes=False)
fol.tree(-8.0, 1.5, 10.5)            # the oaks
fol.tree(8.5, 3.5, 9.5)
fol.tree(-10.5, -6.0, 7.5)
S.prop("Timber Bus Shelter", -6.0, 7.0, 0, lanes=False, check=False)
S.prop("Bus Stop Sign", -2.6, 8.6, 180, check=False)
S.prop("Phone Booth", 11.4, 6.8, 180, lanes=False, check=False)
S.prop("Village Noticeboard", -12.0, -7.3, 0, lanes=False, check=False)
S.prop("Post Litter Bin", -3.8, 7.9, 180, check=False)
S.prop("Post Litter Bin", 5.0, -7.9, 0, check=False)
S.prop("Flag on a Pole", 11.6, -7.4, 0, check=False)
S.prop("Planter", -1.6, -6.6, 0, check=False)
S.prop("Planter", 1.6, -6.6, 0, check=False)
S.prop("Drinking Fountain", 10.6, 0.0, 270, check=False)
# the village fete on the green: two stalls, bales to sit on, a tea table
S.prop("Market Stall", -8.5, -3.2, 60)
S.prop("Market Stall", 4.8, 5.0, 200)
for x, z, yaw in ((-6.0, -0.4, 20), (5.0, 1.8, 200), (-10.6, 2.0, 80), (10.4, -2.8, 100)):
    S.prop("Hay Bale", x, z, yaw)
S.prop("Park Picnic Table", -2.6, 5.2, 0)
S.prop("Park Picnic Table", 6.4, -5.8, 90)

# ================================================================================================ the high street
S.rule("street")
S.prop("Bus", -9.0, 10.4, 0, lanes=False, check=False)           # waiting at the stop, eastbound (driving on the left)
S.prop("Van", -18.6, 13.9, 180, lanes=False, check=False)        # a delivery outside the shop, westbound
S.prop("Hatchback", 6.0, 13.8, 180, lanes=False, check=False)    # parked outside the pub
S.prop("Sedan", 14.8, 13.8, 180, lanes=False, check=False)
S.prop("Hatchback", 33.0, 10.2, 0, lanes=False, check=False)
S.prop("Pickup Truck", 34.0, -13.6, 0, lanes=False, check=False)  # by the allotment gate on Church Lane
S.prop("Hatchback", 2.0, -10.6, 180, lanes=False, check=False)
S.prop("Bicycle", -10.4, 16.6, 0, check=False)
S.prop("Fingerpost Sign", -15.0, -9.6, 20, check=False)
S.prop("Fingerpost Sign", 15.2, 9.6, 200, check=False)
S.prop("Fingerpost Sign", -37.6, 8.0, 0, check=False)
S.prop("Grit Bin", -19.2, -10.2, 90, check=False)
S.prop("Grit Bin", 19.4, -10.0, 270, check=False)
S.prop("Speed Limit Sign", 44.0, 15.6, 270, check=False)
S.prop("Speed Limit Sign", -45.0, 8.4, 90, check=False)
for x, z, yaw in ((-14.0, 9.1, 0), (14.0, -9.6, 180), (25.0, 15.7, 180), (-22.0, 9.2, 0)):
    S.prop("Street Lamp", x, z, yaw, check=False)
S.prop("Utility Pole", -24.6, 15.8, 0, check=False)
S.prop("Utility Pole", 21.0, -15.0, 0, check=False)
S.prop("Pole Transformer", 21.0, -14.8, 0, check=False)
S.prop("Manhole Cover", 3.0, 12.2, 0, check=False)
S.prop("Manhole Cover", -17.0, 2.0, 0, check=False)
S.prop("Storm Drain", 10.0, 9.4, 0, check=False)
S.prop("Storm Drain", -2.0, 14.6, 0, check=False)

# ================================================================================================ the pub garden and the back yards
S.rule("pub")
for x, z in ((-1.4, 18.6), (-1.4, 22.4), (-1.4, 26.2)):
    S.prop("Park Picnic Table", x, z, 90, lanes=False)
    S.prop("Beer Garden Parasol", x, z, 0, check=False)
S.prop("Beer Garden Parasol", 0.6, 19.0, 0, check=False)
S.prop("Beer Garden Parasol", 0.6, 23.0, 0, check=False)
S.prop("Sandwich Board", 1.8, 16.4, 20, check=False)
for x, z in ((4.0, 27.2), (5.2, 27.3)):
    S.prop("Wheelie Bin", x, z, 0, check=False)
S.prop("Pallet Stack", 13.5, 28.0, 10, lanes=False)
S.prop("Small Crate", 15.4, 27.6, 30, lanes=False)
S.prop("Builders Skip", 20.0, 29.5, 90, lanes=False)

S.rule("shop")
S.prop("Pallet", -12.0, 27.6, 10, lanes=False)
S.prop("Cardboard Stack", -13.4, 27.4, 0, lanes=False)
S.prop("Recycling Bin", -19.6, 27.0, 180, lanes=False)
S.prop("Garden Shed", -20.5, 30.5, 90)
S.prop("Water Butt", -18.8, 31.6, 0)
S.prop("Oil Drum", -10.4, 28.8, 0)
S.prop("Pallet", -10.0, 30.4, 80, lanes=False)

# ================================================================================================ the allotments
S.rule("allotments")
PLOTS = []
for row, z in enumerate((-31.6, -27.4, -23.2, -19.4)):
    for col_, x in enumerate((16.5, 21.5, 30.5, 35.5)):
        if (row, col_) in ((0, 3),):
            continue
        PLOTS.append((x, z))
rnd = random.Random(17)
for i, (x, z) in enumerate(PLOTS):
    kind = rnd.choice(("beans", "beds", "frame", "beds", "shed"))
    if kind == "beans":
        S.prop("Bean Canes", x, z, 0, check=False)
        S.prop("Bean Canes", x, z + 1.3, 0, check=False)
    elif kind == "beds":
        S.prop("Raised Bed", x - 1.0, z, 0)
        S.prop("Raised Bed", x + 1.3, z, 0)
    elif kind == "frame":
        S.prop("Cold Frame", x - 0.8, z, 0)
        S.prop("Bean Canes", x + 0.6, z + 1.2, 0, check=False)
    else:
        S.prop("Garden Shed", x, z, rnd.choice((0, 180)))
        S.prop("Water Butt", x + 1.6, z + 0.6, 0)
for name, x, z, yaw in (("Allotment Scarecrow", 26.5, -26.0, 30), ("Wheelbarrow", 20.2, -22.0, 60), ("Compost Bin", 36.4, -27.0, 0),
                        ("Compost Bin", 36.4, -25.6, 0), ("Water Butt", 33.0, -22.6, 0), ("Garden Tools", 22.0, -30.8, 0),
                        ("Watering Can", 29.0, -31.0, 40), ("Compost Bags", 34.6, -19.0, 0), ("Hose Reel", 15.0, -24.0, 90)):
    S.prop(name, x, z, yaw, check=name in ("Compost Bin", "Water Butt", "Wheelbarrow"))

# ================================================================================================ the churchyard
S.rule("churchyard")
GRAVES = []
for gx in range(-33, -17, 3):
    for gz in range(-32, -18, 4):
        if abs(gx + 30) < 2 and gz > -22:
            continue
        GRAVES.append((gx + rnd.uniform(-0.4, 0.4), gz + rnd.uniform(-0.5, 0.5)))
for gx in range(-12, 12, 3):
    for gz in (-33.0,):
        GRAVES.append((gx + rnd.uniform(-0.3, 0.3), gz))
for k, (x, z) in enumerate(GRAVES):
    S.prop("Churchyard Chest Tomb" if k % 7 == 3 else "Churchyard Headstone", x, z, 180 + rnd.uniform(-6, 6))
S.prop("Bench", -18.6, -32.0, 90, check=False)
S.prop("Bench", 9.6, -33.6, 0, check=False)
S.prop("Wheelbarrow", -19.0, -20.0, 30)

# ================================================================================================ the cottage garden (west) and the terrace's back yards (east)
S.rule("gardens")
S.prop("Garden Shed", -33.0, -12.0, 90)
S.prop("Raised Bed", -36.0, -9.0, 90)
S.prop("Raised Bed", -36.0, -12.4, 90)
S.prop("Compost Bin", -37.4, -14.2, 0)
S.prop("Wheelbarrow", -34.5, -6.6, 120)
S.prop("Laundry Line", -31.8, 3.5, 90, lanes=False)
S.prop("Bench", -21.2, -3.4, 90)
S.prop("Kettle BBQ", -31.0, -4.5, 0)
for x, z, yaw in ((37.2, -5.2, 90), (37.2, 0.4, 90), (37.2, 5.4, 90)):
    S.prop("Wheelie Bin", x, z, yaw)
S.prop("Bicycle", 37.2, 2.8, 90)

# meadow: a bench by the stream, the field gate on the footpath out
S.rule("meadow")
S.prop("Bench", -24.8, 28.0, 250)
S.prop("Five Bar Gate", -5.5, 38.4, 0, check=False)
S.prop("Five Bar Gate", 4.5, -39.4, 0, check=False)
S.prop("Log", -36.5, 19.5, 30)
S.prop("Hay Bale", -34.0, 27.0, 20)

S.raw({"op": "AutoWaypoints", "spacing": 6})
for x, z in ((-31.2, 12.0), (-31.6, 8.0), (-31.0, 16.0), (-33.5, 5.0), (-30.2, 20.0), (-28.8, 26.0), (-36.5, 0.5),   # the stream bed, under the bridge
             (-7.5, -13.5), (-7.5, -17.0), (6.0, -14.0), (6.0, -17.2), (-30.0, -12.0), (-30.0, -17.4), (14.2, -27.0), (11.0, -27.0),
             (14.2, -20.0), (11.0, -20.0), (-5.5, 36.5), (4.5, -38.0), (-43.5, 12.0), (43.5, 12.0), (43.0, -12.3)):
    S.waypoint(x, z)

# ================================================================================================ decals
S.rule("decals")


def decal(name, x, z, **kw):
    op = {"op": "Decal", "decal": name, "x": round(x, 2), "z": round(z, 2)}
    op.update({k: (round(v, 3) if isinstance(v, float) else v) for k, v in kw.items()})
    S.raw(op)


# the High Street: white centre dashes (driving on the left), the zebra crossing by the pub with its zig-zags, double
# yellow lines along the green and the shop, the bus stop box, give-way lines and triangles where the lanes meet it
for x in range(-44, 46, 5):
    if abs(x - 9) < 6 or abs(x + 17) < 4 or abs(x - 17) < 4 or BRIDGE[0] - 1 < x < BRIDGE[1] + 1:
        continue
    zc = 12.0 if x < 0 else 12.3 - (x / 17) * 0.3 if x < 17 else 12.0 - (x - 17) / 13 * 0.4 if x < 30 else 11.8
    decal("Lane Dash", x, zc, rotation=90, size=2.0)
decal("Crosswalk Bars", 9.0, 12.15, rotation=90, size=6.0)
for x in (3.6, 14.4):
    decal("Road Zigzag Lines", x, 9.35, size=4.6)
    decal("Road Zigzag Lines", x, 14.7, size=4.6)
for x in (-26.0, -22.0, -13.0, 21.5):
    decal("Double Yellow Lines", x, 14.6)
for x in (-14.0, 0.0, 4.0, 22.0, 26.0):
    decal("Double Yellow Lines", x, 9.4)
decal("Bus Stop Road Marking", -9.0, 10.3, size=4.6)
for x in (-12.6, -5.4):
    decal("Loading Bay Box", x, 9.9, size=1.0, tint="#e8be28") if False else None
for lane_x, sgn in ((-17.0, 1), (17.0, 1)):
    decal("Give Way Lines", lane_x + 1.25, 8.55, size=2.5)                 # southbound half (on the left: east)
    decal("Give Way Triangle", lane_x + 1.25, 4.2, rotation=180)
    decal("Give Way Lines", lane_x - 1.25, -9.45, size=2.5)                # northbound half (west)
    decal("Give Way Triangle", lane_x - 1.25, -5.3, rotation=0)
decal("Slow Road Marking", 33.0, -11.4, rotation=270, size=2.6)            # Church Lane, westbound into the village
decal("Slow Road Marking", -42.5, 10.6, rotation=90, size=2.6) if False else None
decal("Stop Line", 9.0, 12.15, size=0.1, rotation=0) if False else None
# road wear: patches, cracks, oil under the parked cars, puddles in the dips, drains and covers
S.raw({"op": "DecalScatter", "decals": ["Asphalt Patch", "Floor Cracks", "Floor Cracks", "Puddle", "Oil Stain"], "x0": -40, "z0": 9.4, "x1": 40, "z1": 14.6,
       "count": 26, "seed": 51, "gap": 2.5, "minSize": 0.9, "maxSize": 1.9})
S.raw({"op": "DecalScatter", "decals": ["Asphalt Patch", "Floor Cracks", "Puddle", "Leaf Litter"], "x0": -16, "z0": -14.2, "x1": 40, "z1": -9.8,
       "count": 14, "seed": 52, "gap": 2.5, "minSize": 0.8, "maxSize": 1.6})
for x, z in ((-17.0, -6.0), (-17.0, 6.0), (17.0, -6.0), (17.0, 6.0)):
    decal("Floor Cracks", x + 0.6, z, size=1.4)
for x, z, yaw in ((-18.6, 13.9, 0), (6.0 + 16.0, 13.8, 0), (27.0, 13.8, 0), (33.0, 10.2, 0), (2.0, -10.6, 0), (34.0, -13.6, 0)):
    decal("Oil Drips", x, z, rotation=90)
# pavements and paths: grime, gum, leaves, gravel
S.raw({"op": "DecalScatter", "decals": ["Dirt Patch", "Water Stain", "Cigarette Butts", "Leaf Litter"], "x0": -24, "z0": 15.1, "x1": 40, "z1": 16.8,
       "count": 14, "seed": 53, "gap": 2, "minSize": 0.5, "maxSize": 1.2})
S.raw({"op": "DecalScatter", "decals": ["Gravel Scatter", "Footprints", "Dirt Patch"], "x0": -6.5, "z0": 17, "x1": -4.5, "z1": 33, "count": 8, "seed": 54, "gap": 1.5})
S.raw({"op": "DecalScatter", "decals": ["Gravel Scatter", "Footprints", "Moss Patch"], "x0": -30, "z0": -35, "x1": -16, "z1": -17, "count": 14, "seed": 55, "gap": 2})
S.raw({"op": "DecalScatter", "decals": ["Mud Patch", "Footprints", "Tyre Tracks", "Dirt Patch"], "x0": 14, "z0": -33, "x1": 37, "z1": -17, "count": 18, "seed": 56, "gap": 2.5})
S.raw({"op": "DecalScatter", "decals": ["Mud Patch", "Footprints", "Dry Grass"], "x0": -39, "z0": 16, "x1": -24, "z1": 32, "count": 10, "seed": 57, "gap": 2.5})
for (x, z, r) in ((-8.0, 1.5, 4.5), (8.5, 3.5, 4.0), (-10.5, -6.0, 3.0)):
    S.raw({"op": "DecalScatter", "decals": ["Leaf Litter", "Leaf Pile"], "x0": x - r, "z0": z - r, "x1": x + r, "z1": z + r, "count": 4, "seed": int(x * 7 + z), "gap": 1.2})
decal("Leaf Pile", -6.0, 6.0)
# doormats and fag ends at the doors, a chalk mark on the pub's step
for x, z, rot in ((-26.0 + 3.65 + 0.45, -1.4, 90), (-15.4, 16.4, 0), (7.8, 16.4, 0), (27.6, 17.9, 0)):
    decal("Doormat", x, z, rotation=rot)
decal("Cigarette Butts", 9.4, 16.6)
decal("Cigarette Butts", 3.4, 16.4)
# walls: moss and grime low on the churchyard wall, weathering on the bridge, the signs on the shop and the pub
for x in range(-34, 12, 4):
    decal("Moss Patch", x + 0.7, -14.9, y=0.4, onto="wall-z", size=1.2)
    if x % 8 == 2:
        decal("Grime Streaks", x + 2.2, -14.9, y=1.0, onto="wall-z", size=1.6)
for zf, onto in ((8.4, "wall+z"), (15.6, "wall-z")):
    decal("Moss Patch", -31.0, zf - (0.5 if onto == "wall+z" else -0.5), y=0.7, onto=onto, size=1.4, anchor="world") if False else None
shop_y = PADS[[b[0] for b in BUILDINGS].index("Post Office Stores") - 1][4]
decal("Village Stores Sign Left", -19.45, 15.0, y=shop_y + 2.73, onto="wall+z", anchor="world", reach=1.5)
decal("Village Stores Sign Right", -16.8, 15.0, y=shop_y + 2.73, onto="wall+z", anchor="world", reach=1.5)
decal("Post Office Sign", -11.5, 15.0, y=shop_y + 2.73, onto="wall+z", anchor="world", reach=1.5, size=0.88)
pub_y = PADS[[b[0] for b in BUILDINGS].index("Pub") - 1][4]
decal("Pub Name Board", 6.2, 15.0, y=pub_y + 2.77, onto="wall+z", anchor="world", reach=1.5, size=3.2)
decal("Allotments Sign", 27.5, -14.6, y=0.7, onto="wall-z", reach=1.5, size=1.3)
S.raw({"op": "DecalScatter", "decals": ["Grime Streaks", "Water Damage", "Moss Patch", "Peeling Paint"], "x0": -30, "z0": 16, "x1": 33, "z1": 27,
       "count": 18, "seed": 58, "onto": "wall", "gap": 3})
S.raw({"op": "DecalScatter", "decals": ["Grime Streaks", "Moss Patch", "Water Damage"], "x0": 22, "z0": -9, "x1": 38, "z1": 9,
       "count": 10, "seed": 59, "onto": "wall", "gap": 3})

# ================================================================================================ more dressing
S.rule("dressing")
# hanging baskets on the pub and the shop, flags out for the village (the UK pool)
for x in (3.6, 11.2, 16.2):
    S.prop("Hanging Flower Basket", x, 15.7, 180, y=1.95, check=False)
S.prop("Flag on a Pole", 2.6, 16.6, 0, check=False, flag="uk") if False else None
S.raw({"op": "Prop", "name": "Flag on a Pole", "x": 2.3, "z": 16.9, "flag": "uk"})
S.raw({"op": "Prop", "name": "Flag on a Pole", "x": -6.4, "z": -17.2, "flag": "uk"})
# the pub's front: benches and planters; the shop's front: crates of veg, a bike
S.prop("Bench", 13.4, 16.6, 0, check=False)
S.prop("Large Flower Pot", 5.0, 16.4, 0, check=False)
S.prop("Large Flower Pot", 10.6, 16.4, 0, check=False)
S.prop("Cabbage Crate", -21.0, 16.4, 0, check=False)
S.prop("Apple Crate", -20.2, 16.4, 10, check=False)
# cottage front gardens
S.prop("Flower Pots", -21.0, 2.6, 90, check=False)
S.prop("Garden Gnome", -21.4, -5.0, 60, check=False)
S.prop("Bird Bath", 22.2, 0.0, 0)
S.prop("Garden Gnome", 22.0, -5.0, 250, check=False)
S.prop("Flower Pots", 22.4, 4.8, 270, check=False)
S.prop("Rose Arch" if "Rose Arch" in lib else "Flower Pot", 31.0, 18.2, 0, check=False)
# the meadow: a stile-side bench, a picnic by the willow
S.prop("Park Picnic Table", -25.6, 21.6, 30)
# church: a bench by the porch, the notice board's flower pots are part of the church
S.prop("Water Butt", -14.8, -21.0, 0)

# ================================================================================================ scenery beyond the hedge (cosmetic)
S.rule("scenery")
rs = random.Random(91)
TREE_GREENS = ["#3d6a2e", "#466f30", "#355f2a", "#4f7a34"]


def scenery_tree(x, z, h):
    g = G(x, z)
    mesh_world("cylinder", x, z, g - 0.2, "#4e3c2a", width=0.55, height=h * 0.45, depth=0.55, segments=8)
    w = h * rs.uniform(0.55, 0.75)
    mesh_world("icosphere", x, z, g + h * 0.32, rs.choice(TREE_GREENS), width=w, height=h * 0.72, depth=w, detail=1)


placed = []
for k in range(900):
    x, z = rs.uniform(-66, 66), rs.uniform(-62, 62)
    if pl.point_in_poly(x, z, OUTLINE) or pl.poly_edge_dist(x, z, OUTLINE) < 4.0:
        continue
    if abs(z - 12.0) < 5 and abs(x) > 40:       # keep the High Street clear where it leaves
        continue
    if abs(z + 12.3) < 4.5 and x > 39:
        continue
    if abs(x - 4.5) < 3 and z < -34 or abs(x + 5.5) < 3 and z > 33:
        continue
    if abs(x - (-26.0 - (z - 32) * 0.05)) < 5 and z > 32:    # the stream's way out
        continue
    if any((x - a) ** 2 + (z - b) ** 2 < 30 for a, b in placed):
        continue
    placed.append((x, z))
    if len(placed) >= 120:
        break
for x, z in placed:
    scenery_tree(x, z, rs.uniform(7, 12))
# the next cottages along the High Street, beyond the village edge (boxes and roofs only)
for x, z, w, d in ((54.0, 20.5, 9.0, 7.0), (-55.0, 3.5, 9.0, 7.0), (57.0, -20.0, 8.0, 6.5)):
    g = G(x, z)
    mesh_world("box", x, z, g - 0.3, STONE, width=w, height=5.6, depth=d, material="Brick")
    for sgn in (-1, 1):
        mesh_world("wedge", x, z + sgn * d / 4, g + 5.3, "#3c4046", width=w + 0.5, height=2.6, depth=d / 2 + 0.3, yaw=0.0 if sgn < 0 else 180.0, material="Tiles")

# ================================================================================================ foliage
fol.surfaces(Sand=0.0, Dirt=0.04, Rock=0.5, Concrete=0.0, Tiles=0.0, Wood=0.0, Metal=0.0, Plain=0.0, Brick=0.0)
fol.region("grass", 4.0, (-48, -43, 48, 42), color="#5f8f3a", scale=[0.6, 1.0], patchiness=0.2)
fol.region("grass", 3.0, [(-41, -6), (-31, 0), (-29, 34), (-40, 26)], color="#7a9a44", scale=[0.9, 1.5], patchiness=0.5)   # long meadow grass
fol.region("shrub", 0.6, [(-41, -6), (-31, 0), (-29, 34), (-40, 26)], patchiness=0.6)
fol.strip("bush", 1.4, [(p[0], p[1]) for p in STREAM], 7.0, color="#5a7a34", scale=[0.7, 1.2])                   # reeds along the stream
fol.region("grass", 1.5, (12.8, -35, 39, -16.4), color="#6a8a3a", patchiness=0.6)
for x, z in ((-36.5, -26.0), (-36.0, -17.5), (-20.0, -34.0), (10.6, -17.2), (-14.5, -33.5)):
    fol.tree(x, z, 7.0, type="pine")                                                                                    # churchyard yews
for x, z, h in ((-37.5, -10.5, 6.0), (-34.0, -14.5, 5.5), (-23.0, -13.5, 5.5)):
    fol.tree(x, z, h)                                                                                                   # the orchard
for x, z, h in ((-36.0, 18.0, 9.0), (-26.5, 31.5, 8.0), (-38.0, 4.5, 8.5)):
    fol.tree(x, z, h)                                                                                                   # willows by the stream
for x, z, h in ((38.0, 30.0, 7.0), (21.5, 31.0, 6.5), (-9.0, 31.5, 7.0)):
    fol.tree(x, z, h)
# hedges inside the outline along the lanes and gardens (concealment)
fol.strip("bush", 1.8, [(-40.5, -15.0), (-19.8, -15.0)], 1.4, color=HEDGE, scale=[1.0, 1.6])
fol.strip("bush", 1.6, [(-2.4, 16.8), (-2.4, 33.5)], 1.2, color=HEDGE, scale=[1.0, 1.5])
fol.strip("bush", 1.6, [(-8.6, 17.0), (-8.6, 33.5)], 1.2, color=HEDGE, scale=[1.0, 1.5])
fol.strip("shrub", 2.4, [(-29.0, -5.4), (-23.0, -5.4)], 0.8, color="#b05a7a", scale=[0.6, 0.9])                  # flower beds
fol.strip("shrub", 2.4, [(24.0, 18.4), (32.0, 18.4)], 0.8, color="#c07ab0", scale=[0.6, 0.9])
fol.strip("shrub", 2.0, [(24.0, -6.0), (24.0, 6.0)], 0.8, color="#d0a040", scale=[0.6, 0.9])
fol.strip("shrub", 2.0, [(-12.2, 15.4), (-20.0, 15.4)], 0.6, color="#a04a40", scale=[0.5, 0.8])
fol.path([(-17.0, -14.0), (-17.0, 14.0)], 5.0)
fol.path([(-5.5, 16.0), (-5.5, 41.0)], 1.8)

# ================================================================================================ cosmetic clutter (the game's Clutter op)
LITTER = ["Litter", "Newspaper", "Soda Cans", "Plastic Bag", "Bottle"]
S.raw({"op": "Clutter", "props": LITTER, "x0": -40, "z0": -15, "x1": 40, "z1": 16, "count": 90, "seed": 31})
S.raw({"op": "Clutter", "props": ["Flower Pot", "Garden Gnome", "Watering Can", "Bucket", "Garden Tools", "Cat"], "x0": -40, "z0": -10, "x1": -21, "z1": 8, "count": 18, "seed": 32})
S.raw({"op": "Clutter", "props": ["Watering Can", "Bucket", "Garden Tools", "Flower Pot", "Compost Bags", "Pigeons"], "x0": 13, "z0": -35, "x1": 39, "z1": -17, "count": 40, "seed": 33})
S.raw({"op": "Clutter", "props": ["Bottle", "Bottles", "Soda Cans", "Newspaper"], "x0": -4, "z0": 16, "x1": 3, "z1": 28, "count": 25, "seed": 34})

# ================================================================================================ fairness


DEBUG_WALKS = os.environ.get("VILLAGE_DEBUG_WALKS")


def check_walks(res=0.5):
    """Average walk (metres) from each team's spawns to each hill and to the enemy flag, on a grid over the open
    ground (buildings and solid things blocked; walking up the hill counts 1.3x)."""
    import heapq
    import numpy as np
    x0, z0, x1, z1 = -48.0, -44.0, 48.0, 42.0
    nx, nz = int((x1 - x0) / res), int((z1 - z0) / res)
    xs = x0 + (np.arange(nx) + 0.5) * res
    zs = z0 + (np.arange(nz) + 0.5) * res
    X, Z = np.meshgrid(xs, zs)
    free = np.zeros((nz, nx), bool)
    for j in range(nz):
        for i in range(nx):
            free[j, i] = pl.point_in_poly(xs[i], zs[j], OUTLINE)
    hh = T.sample(X, Z)
    for r, y0, y1, kind in S.occ:
        if y1 - y0 < 0.6 and kind == "prop":
            continue
        i0, i1 = int((r[0] - x0) / res), int(math.ceil((r[2] - x0) / res))
        j0, j1 = int((r[1] - z0) / res), int(math.ceil((r[3] - z0) / res))
        free[max(0, j0):max(0, j1), max(0, i0):max(0, i1)] = False
    # building doors: open the doorway cells again (so routes may cut through buildings)
    for plan in PLANS.values():
        for dd in plan.doors:
            if dd["floor"] != 0:
                continue
            b = dd["box"]
            i0, i1 = int((b[0] - 0.3 - x0) / res), int(math.ceil((b[3] + 0.3 - x0) / res))
            j0, j1 = int((b[2] - 0.3 - z0) / res), int(math.ceil((b[5] + 0.3 - z0) / res))
            free[j0:j1, i0:i1] = True
    gy_, gx_ = np.gradient(hh, res)
    free &= np.hypot(gx_, gy_) < math.tan(math.radians(40))

    def cell(x, z):
        return int((z - z0) / res), int((x - x0) / res)

    def dijkstra(src):
        dist = np.full((nz, nx), np.inf)
        pq = []
        for x, z in src:
            j, i = cell(x, z)
            dist[j, i] = 0.0
            heapq.heappush(pq, (0.0, j, i))
        steps = [(1, 0, 1.0), (-1, 0, 1.0), (0, 1, 1.0), (0, -1, 1.0), (1, 1, 1.414), (1, -1, 1.414), (-1, 1, 1.414), (-1, -1, 1.414)]
        while pq:
            d, j, i = heapq.heappop(pq)
            if d > dist[j, i]:
                continue
            for dj, di, c in steps:
                a, b = j + dj, i + di
                if 0 <= a < nz and 0 <= b < nx and free[a, b]:
                    up = max(0.0, hh[j, i] - hh[a, b])  # the real walk goes from (a, b) to (j, i)
                    nd = d + c * res + up * 0.3 / res * res
                    if nd < dist[a, b]:
                        dist[a, b] = nd
                        heapq.heappush(pq, (nd, a, b))
        return dist

    def team_avg(spawns, target):
        dist = dijkstra([target])
        vals = [dist[cell(*s)] for s in spawns]
        if DEBUG_WALKS:
            print("    ", target, [round(v) if v < 1e9 else -1 for v in vals])
            import numpy as np
            from PIL import Image
            img = np.zeros((nz, nx, 3), np.uint8)
            img[free] = (90, 140, 80)
            fin = np.isfinite(dist)
            img[fin] = (200, 200, 120)
            Image.fromarray(img).resize((nx * 2, nz * 2)).save(DEBUG_WALKS)
        return sum(vals) / len(vals)
    out = []
    for k, (x, z, r) in enumerate(HILLS):
        out.append((f"hill {k + 1}", team_avg(BLUE_SP, (x, z)), team_avg(RED_SP, (x, z))))
    out.append(("enemy flag", team_avg(BLUE_SP, FLAGS[1]), team_avg(RED_SP, FLAGS[0])))
    for what, b, r in out:
        flag = "  <-- over 10%" if abs(b - r) > 0.1 * max(b, r) else ""
        print(f"  walk {what}: blue {b:.0f} m, red {r:.0f} m{flag}")


# ================================================================================================ output


def preview(png, bounds=(-50, -45, 50, 44), scale=9):
    """Top-down: the terrain's colours with hill shading and 1 m contours, the buildings (shell boxes, shaded by height),
    solid props (brown), spawns, hills (yellow) and flags (white), and the outline."""
    import numpy as np
    from PIL import Image, ImageDraw
    bx0, bz0, bx1, bz1 = bounds
    W, Hh = int((bx1 - bx0) * scale), int((bz1 - bz0) * scale)
    xs = bx0 + (np.arange(W) + 0.5) / scale
    zs = bz0 + (np.arange(Hh) + 0.5) / scale
    X, Z = np.meshgrid(xs, zs)
    hh = T.sample(X, Z)
    cols = T.colors()
    ci = np.clip(((X - T.xs[0]) / T.cell).round().astype(int), 0, len(T.xs) - 1)
    cj = np.clip(((Z - T.zs[0]) / T.cell).round().astype(int), 0, len(T.zs) - 1)
    rgb = cols[cj, ci]
    gz_, gx_ = np.gradient(hh, 1 / scale)
    shade = np.clip(1.0 - 0.9 * (gx_ * 0.5 + gz_ * -0.7), 0.55, 1.35)
    rgb = np.clip(rgb * shade[..., None], 0, 1)
    contour = (np.floor(hh) != np.floor(np.roll(hh, 1, axis=0))) | (np.floor(hh) != np.floor(np.roll(hh, 1, axis=1)))
    rgb[contour] = rgb[contour] * 0.7
    img = Image.fromarray((rgb * 255).astype(np.uint8))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return ((x - bx0) * scale, (z - bz0) * scale)
    for op in S.ops:
        if op["op"] == "Mesh" and op["mesh"] == "box" and op.get("color") == WATER:
            d.ellipse([P(op["x"] - 1.2, op["z"] - 1.2), P(op["x"] + 1.2, op["z"] + 1.2)], fill=(70, 110, 120))
    for name, plan in PLANS.items():
        for b in sorted(plan.shell, key=lambda b: b[4]):
            x0, y0, z0, x1, y1, z1, col_ = b[:7]
            k = 0.55 + 0.45 * min(1.0, y1 / 12)
            d.rectangle([P(x0, z0), P(x1, z1)], fill=tuple(int(c * 255 * k) for c in col_))
    for e in S.occ:
        r, y0, y1, kind = e
        if kind == "prop":
            d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=(150, 90, 40) if y1 - y0 < 1.3 else (110, 60, 140))
        elif kind == "box":
            pass
        elif kind == "trunk":
            d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=(60, 40, 20))
    for op in S.ops:
        if op["op"] == "Block":
            d.rectangle([P(op["x0"], op["z0"]), P(op["x1"], op["z1"])], outline=(60, 50, 40), fill=(170, 150, 110))
    d.line([P(*p) for p in OUTLINE + OUTLINE[:1]], fill=(20, 60, 20), width=3)
    for sp, c in [(p, (40, 90, 230)) for p in BLUE_SP] + [(p, (230, 50, 50)) for p in RED_SP] + [(p, (240, 240, 240)) for p in FFA_SP]:
        d.ellipse([P(sp[0] - 0.7, sp[1] - 0.7), P(sp[0] + 0.7, sp[1] + 0.7)], fill=c, outline=(0, 0, 0))
    for x, z, r in HILLS:
        d.ellipse([P(x - r, z - r), P(x + r, z + r)], outline=(255, 230, 0), width=3)
    for x, z in FLAGS:
        d.rectangle([P(x - 0.6, z - 0.6), P(x + 0.6, z + 0.6)], fill=(255, 255, 255), outline=(0, 0, 0))
    for gx in range(-50, 51, 10):
        d.text(P(gx + 0.3, bz0 + 0.5), str(gx), fill=(255, 255, 255))
    for gz in range(-40, 45, 10):
        d.text(P(bx0 + 0.3, gz), str(gz), fill=(255, 255, 255))
    img.save(png)




def dump(o, ind=0):
    pad = "  " * ind
    if isinstance(o, dict) and ind <= 1 and any(isinstance(v, (dict, list)) for v in o.values()):
        return "{\n" + ",\n".join(f'{pad}  "{k}": {dump(v, ind + 1)}' for k, v in o.items()) + "\n" + pad + "}"
    if isinstance(o, list) and ind <= 2 and o and isinstance(o[0], dict):
        return "[\n" + ",\n".join(pad + "  " + json.dumps(v) for v in o) + "\n" + pad + "]"
    return json.dumps(o)


def finish():
    return {
        "format": "call-of-slop-map/1",
        "name": "Cotswold Village",
        "description": "A Cotswold village on a hillside: the church on its raised churchyard, the allotments, the green and its memorial, stone cottages, the shop and post office, the pub and a stream under the bridge.",
        "author": "Call of Slop",
        "order": 507,
        "maxPlayers": 12,
        "sky": "#b8c4cc",
        "skybox": {"preset": "clear", "sun": {"time": 15.5, "latitude": 52}, "clouds": {"coverage": 0.55, "speed": 7}, "haze": {"color": "#e4dcc8", "strength": 0.25}},
        "flagPool": ["uk"],
        "boundary": {
            "shape": "polygon",
            "points": [list(p) for p in OUTLINE],
            "noise": NOISE,
            "edge": {"style": "hedge", "height": 2.6, "thickness": 1.4, "seed": 7},
            "groundMargin": 10,
        },
        "terrain": TERRAIN,
        "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
        "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
        "flags": [list(f) for f in FLAGS],
        "ops": S.ops,
        "foliage": fol.data,
    }


if __name__ == "__main__":
    mp = finish()
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    with open(OUT_JSON, "w") as f:
        f.write(dump(mp) + "\n")
    st = S.stats()
    print(f"area {pl.poly_area(OUTLINE):.0f} m2 (side {math.sqrt(pl.poly_area(OUTLINE)):.1f} m), terrain {T.h.shape}, "
          f"height {T.h.min():.1f}..{T.h.max():.1f}, blocks {st['block']}, meshes {st['mesh']}, props {st['prop']} "
          f"(solid {st['solid']}, cosmetic {st['cosmetic']}, breakable {st['breakable']}), skipped {st['skipped']}")
    for r, n, x, z, w in S.skips:
        print("  skipped", r, n, x, z, w)
    check_walks()
    if OUT_PNG:
        preview(OUT_PNG)
        S.preview(OUT_PNG.replace(".png", "_occ.png"), (-50, -45, 50, 44), scale=9, spawns=[(p, (40, 90, 230)) for p in BLUE_SP] + [(p, (230, 50, 50)) for p in RED_SP]
                  + [(p, (240, 240, 240)) for p in FFA_SP], hills=HILLS, flags=FLAGS)
