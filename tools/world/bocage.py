#!/usr/bin/env python3
"""Generator for Maps/World/05-Bocage.json (World Tour pack, France; large, 22 players): the Normandy bocage.

Usage: python3 tools/world/bocage.py <repo root> [preview.png]

A maze of small fields boxed in by tall hedgerows on earth banks (the Countryside "Bocage Hedgerow" props: solid, four
metres and more, they stop bullets and can't be climbed), with field gates and gaps between them; lanes lined with
hedges on both sides, sunk between their banks for long stretches (chemins creux); a brook winding through the middle
under a stone bridge, where the hamlet sits on both banks (the chapel and its calvary on the west bank, the mairie with
its flag on the east); a stone farm with a half-timbered barn, a grain silo and a cider orchard at each end (Blue west,
Red east), cottages here and there, hay, carts, tractors and Normande cows in the fields. The edge rises into wooded
hills (a terrain cliff berm with trees on it), and the four lanes run out of the map in little sunken dead ends.

Fairness: the layout (terrain shapes, lanes, hedges, buildings, spawns, objectives) is point-symmetric about the centre
(every (x, z) has its twin at (-x, -z), turned 180 degrees), except the dressing and which building stands on the
village plots (the chapel / the mairie). The generator checks walks on a 0.5 m grid and spawn-to-spawn sight lines.

Built with tools/cluttered/townkit.py (occupancy, props, foliage, walk fields) and tools/cluttered/terrain.py (the
game's terrain generator, ported), and tools/buildings/bldgen.py (the buildings' footprints, doors and boxes).
"""
import heapq, json, math, os, random, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "cluttered"))
sys.path.insert(0, os.path.join(HERE, "..", "buildings"))
import numpy as np
import townkit as tk
import terrain as tr
import bldgen
from townkit import yaw_facing, yaw_along, R, col

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/World/05-Bocage.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
rng = random.Random(505)


def M(p):
    """The point's twin, turned 180 degrees about the centre."""
    return (-p[0], -p[1]) if len(p) == 2 else (-p[0], -p[1]) + tuple(p[2:])


# ================================================================ outline: an oval with four sunken lanes running out
# Half A runs from the north lobe's west side round the west to the south lobe's west side; the other half is its twin.
HALF_A = [(-9, -100), (-17, -100), (-20, -87), (-26, -78), (-48, -78), (-72, -73), (-92, -60), (-104, -40), (-106, -18),
          (-105, 2), (-114, 7), (-123, 11), (-123, 21), (-114, 25), (-104, 30), (-102, 50), (-88, 66), (-64, 76), (-40, 80),
          (-16, 81), (7, 84)]
CTRL = HALF_A + [M(p) for p in HALF_A]
NOISE = {"amplitude": 0.9, "scale": 16, "seed": 505, "octaves": 2}
t = tk.Town(REPO, CTRL, NOISE, pack_props=[os.path.join(REPO, "Maps/World/Props")])
HALF_X = max(abs(p[0]) for p in t.outline)
HALF_Z = max(abs(p[1]) for p in t.outline)

# ================================================================ lanes, the brook, the village
J1, J2 = (-8.0, 0.0), (8.0, 0.0)          # the two village junctions, either side of the bridge
W_LANE = [(-121, 16), (-104, 14), (-86, 10), (-66, 4), (-48, 7), (-30, 3), (-16, 1), J1]
N_LANE = [J1, (-9, -14), (-8, -30), (-12, -46), (-14, -62), (-12, -78), (-13, -98)]
E_LANE = [M(p) for p in W_LANE]
S_LANE = [M(p) for p in N_LANE]
LANES = [("W", W_LANE), ("N", N_LANE), ("E", E_LANE), ("S", S_LANE)]
BRIDGE = (J1, J2)
STREAM = [(40, -88), (34, -60), (22, -36), (8, -16), (0, 0), (-8, 16), (-22, 36), (-34, 60), (-40, 88)]
BED_Y = -1.8          # the brook's bed (level: a slow brook) and its water
WATER_Y = -1.42
SQUARE_Y = 0.3        # the village squares and the bridge deck
VILLAGE_R = 19.0      # no lane hedges this close to a junction (village street instead)


def seg_dist(p, a, b):
    ax, az = a; bx, bz = b; dx, dz = bx - ax, bz - az
    L2 = dx * dx + dz * dz
    s = 0 if L2 == 0 else max(0, min(1, ((p[0] - ax) * dx + (p[1] - az) * dz) / L2))
    return math.hypot(ax + s * dx - p[0], az + s * dz - p[1])


def poly_dist(p, pts):
    return min(seg_dist(p, pts[i], pts[i + 1]) for i in range(len(pts) - 1))


def resample(pts, step):
    """Points every `step` metres along a polyline: (x, z, arc length, unit direction)."""
    L = polyline_len(pts)
    out = []
    k = 0
    while k * step <= L + 1e-6:
        x, z, ux, uz = at_arc(pts, k * step)
        out.append((x, z, k * step, ux, uz))
        k += 1
    return out


def polyline_len(pts):
    return sum(math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1))


def at_arc(pts, s):
    for i in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[i], pts[i + 1]
        L = math.hypot(bx - ax, bz - az)
        if s <= L or i == len(pts) - 2:
            f = max(0.0, min(1.0, s / L))
            return ax + (bx - ax) * f, az + (bz - az) * f, (bx - ax) / L, (bz - az) / L
        s -= L


def arc_of(pts, p):
    best, bs, acc = 1e9, 0.0, 0.0
    for i in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[i], pts[i + 1]
        dx, dz = bx - ax, bz - az
        L = math.hypot(dx, dz)
        f = max(0, min(1, ((p[0] - ax) * dx + (p[1] - az) * dz) / (L * L)))
        d = math.hypot(ax + f * dx - p[0], az + f * dz - p[1])
        if d < best:
            best, bs = d, acc + f * L
        acc += L
    return bs


def offset_line(pts, off):
    """The polyline moved sideways by `off` (positive: to the left walking along it, i.e. the -normal... (-uz, ux))."""
    out = []
    n = len(pts)
    for i in range(n):
        if i == 0:
            dx, dz = pts[1][0] - pts[0][0], pts[1][1] - pts[0][1]
        elif i == n - 1:
            dx, dz = pts[-1][0] - pts[-2][0], pts[-1][1] - pts[-2][1]
        else:
            ax, az = pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]
            bx, bz = pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]
            la, lb = math.hypot(ax, az), math.hypot(bx, bz)
            dx, dz = ax / la + bx / lb, az / la + bz / lb
        L = math.hypot(dx, dz)
        nx, nz = -dz / L, dx / L
        out.append((pts[i][0] + nx * off, pts[i][1] + nz * off))
    return out


# ================================================================ objectives
BLUE_SP = [(-95, -12), (-87, -11), (-80, -14), (-96, -2), (-86, -1), (-78, -4), (-92, -44), (-76, -46), (-97, 21)]
RED_SP = [M(p) for p in BLUE_SP]
FFA_A = [(-30, -60), (-60, 56), (16, -66)]
FFA_SP = FFA_A + [M(p) for p in FFA_A]
HILL_A = (-1.8, -53.0)                       # tuned on the walk grid (see the report); its twin is hill 3
HILLS = [(0.0, 0.0, 5.0), (HILL_A[0], HILL_A[1], 4.0), (-HILL_A[0], -HILL_A[1], 4.0)]
FLAGS = [(-98.0, -7.0), (98.0, 7.0)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.keepout.append((x, z, 2.6))
for x, z, r in HILLS:
    t.keepout.append((x, z, r + 0.8))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))

# ================================================================ buildings (placed in twins; the village plots differ)
BUILDINGS_A = [
    # name, at, yaw, palette (A), palette (twin)
    ("Norman Farmhouse", (-86.0, -22.0), 0, "limestone", "granite"),
    ("Half-Timbered Barn", (-68.0, -10.0), 270, "ochre cob", "cream cob"),
    ("Norman Cottage", (-88.0, 26.0), 180, "pale cob", "ochre cob"),
    ("Norman Cottage", (-26.0, 16.0), 180, "rose cob", "pale cob"),
    ("Norman Cottage", (6.0, -30.0), 270, "ochre cob", "rose cob"),
]
CHAPEL = ("Church", (-28.0, -14.0), 90, "grey stone")
MAIRIE = ("Norman Farmhouse", (24.0, 12.0), 270, "whitewash")
SILO_A = (-65.5, -26.5)
PLACED = []    # (name, x, z, yaw, palette, plan)
blib = bldgen.Library(REPO)


def place_building(name, at, yaw, palette, seed):
    d = blib.find(name)
    plan = bldgen.generate(blib, d, {"palette": palette}, seed, (at[0], 0, at[1]), int(round(yaw / 90)) % 4)
    PLACED.append((name, at[0], at[1], yaw, palette, plan, seed))
    return plan


for k, (name, at, yaw, pa, pb) in enumerate(BUILDINGS_A):
    place_building(name, at, yaw, pa, 10 + k)
    place_building(name, M(at), (yaw + 180) % 360, pb, 30 + k)
place_building(CHAPEL[0], CHAPEL[1], CHAPEL[2], CHAPEL[3], 7)
place_building(MAIRIE[0], MAIRIE[1], MAIRIE[2], MAIRIE[3], 8)

# ================================================================ terrain
# Shapes (in twins), then the pads, then the lanes' cuttings and the brook. Shapes first, so pads and roads level them.
FEAT = [{"type": "noise", "amplitude": 0.45, "scale": 34, "octaves": 3, "seed": 5050}]
SHAPES_A = [  # x, z, radius, height (the twin gets the same)
    (-84, -16, 34, 2.6),     # the farm on its rise
    (-82, -52, 22, 1.6),     # the orchard slope
    (-30, -58, 24, 2.4),     # the north fields' knoll
    (-60, 46, 26, 2.2),      # the south-west pasture
    (-36, -22, 16, 1.0),     # the chapel's knoll
    (-6, -64, 16, 1.4),      # by the north lane
    (-50, 20, 14, -0.9),     # a wet hollow
]
for (x, z, r, h) in SHAPES_A:
    for (px, pz) in ((x, z), M((x, z))):
        FEAT.append({"type": "hill", "x": px, "z": pz, "radius": r, "height": h})
SURF = {"grass": "#5b7a36", "dirt": "#6b5638", "rock": "#7a766c", "sand": "#6e6650", "dirtSlope": 26, "rockSlope": 40, "variation": 0.1}
TERRAIN = {"cell": 1, "margin": 30, "surface": SURF, "features": FEAT}


def gen(features):
    return tr.generate({"cell": 1, "margin": 30, "surface": SURF, "features": features}, HALF_X, HALF_Z)


T0 = gen(list(FEAT))


def g0(x, z):
    return float(T0.sample(x, z))


def q(v, step=0.25):
    return round(v / step) * step


PADS = []
for (name, x, z, yaw, pal, plan, seed) in PLACED:
    # the pad covers the shell and everything the building stands outside it (porches, chimneys, its own props):
    # the game stands a building on the highest ground under all of it
    xs = [r[0] for r in plan.footprint] + [r[2] for r in plan.footprint] + [b[0] for b in plan.shell] + [b[3] for b in plan.shell]
    zs = [r[1] for r in plan.footprint] + [r[3] for r in plan.footprint] + [b[2] for b in plan.shell] + [b[5] for b in plan.shell]
    for p_ in plan.props:
        if p_["room"] == "exterior":
            xs += [p_["pos"][0] - 1.0, p_["pos"][0] + 1.0]; zs += [p_["pos"][2] - 1.0, p_["pos"][2] + 1.0]
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    h = q((g0(cx, cz) + g0(-cx, -cz)) / 2) if name != "Church" and not (name == "Norman Farmhouse" and pal == "whitewash") else q(g0(cx, cz))
    PADS.append((cx, cz, x1 - x0 + 4, z1 - z0 + 4, h))
# buildings whose pads overlap (a farmhouse and its barn) share one level
groups = list(range(len(PADS)))
def _root(i):
    while groups[i] != i:
        i = groups[i]
    return i
for i in range(len(PADS)):
    for j in range(i + 1, len(PADS)):
        a, b = PADS[i], PADS[j]
        if abs(a[0] - b[0]) < (a[2] + b[2]) / 2 + 4 and abs(a[1] - b[1]) < (a[3] + b[3]) / 2 + 4:
            groups[_root(j)] = _root(i)
for i in range(len(PADS)):
    members = [k for k in range(len(PADS)) if _root(k) == _root(i)]
    h = q(sum(PADS[k][4] for k in members) / len(members))
    PADS[i] = PADS[i][:4] + (h,)
for (x, z) in (J1, J2):
    PADS.append((x * 1.6, z, 22, 16, SQUARE_Y))
for (x, z, r) in HILLS[1:]:
    PADS.append((x, z, 2 * r + 3, 2 * r + 3, q(g0(x, z))))
for (cx, cz, w, d, h) in PADS:
    FEAT.append({"type": "flatten", "x": R(cx), "z": R(cz), "w": R(w), "d": R(d), "height": h, "edge": 4})
T1 = gen(list(FEAT))


def g1(x, z):
    return float(T1.sample(x, z))


# --- the lanes: painted dirt, and sunk between banks (a "cut" ramp 1.3 m below the ground) on long stretches
SUNKEN = {"W": [(-74, -34)], "N": [(-34, -100)]}     # x ranges for W, z ranges for N; the twins get the same


def sink_depth(name, x, z):
    key = name if name in SUNKEN else {"E": "W", "S": "N"}[name]
    sgn = 1 if name in SUNKEN else -1
    v = (x if key == "W" else z) * sgn
    d = 0.0
    for (a, b) in SUNKEN[key]:
        lo, hi = min(a, b), max(a, b)
        if lo <= v <= hi:
            d = max(d, 1.3 * min(1.0, (v - lo) / 8.0, (hi - v) / 8.0))
    return d


for name, pts in LANES:
    FEAT.append({"type": "ridge", "points": [[R(x), R(z)] for x, z in pts], "width": 4.4, "height": 0.01, "material": "dirt"})
    samples = resample(pts, 3.0)
    run = []
    for (x, z, s, ux, uz) in samples:
        d = sink_depth(name, x, z)
        if d > 0.02 and t.inside(x, z, -6):
            run.append([R(x), R(z), R(g1(x, z) - d)])
        elif run:
            if len(run) > 2:
                FEAT.append({"type": "ramp", "points": run, "width": 4.2, "edge": 1.6, "mode": "cut", "material": "dirt"})
            run = []
    if len(run) > 2:
        FEAT.append({"type": "ramp", "points": run, "width": 4.2, "edge": 1.6, "mode": "cut", "material": "dirt"})

# --- the brook: a level bed cut along it (deeper under the bridge), sandy gravel
FEAT.append({"type": "ramp", "points": [[x, z, BED_Y] for x, z in STREAM], "width": 3.2, "edge": 2.8, "mode": "cut", "material": "sand"})
FEAT.append({"type": "ramp", "points": [[6, -12, -2.6], [0, 0, -2.6], [-6, 12, -2.6]], "width": 3.0, "edge": 1.2, "mode": "cut", "material": "sand"})
T = gen(list(FEAT))
TERRAIN["features"] = FEAT


def gy(x, z):
    return float(T.sample(x, z))


# ================================================================ occupancy from the buildings
for (name, x, z, yaw, pal, plan, seed) in PLACED:
    for b in plan.shell:
        x0, y0, z0, x1, y1, z1 = b[:6]
        if y0 < 1.8 and y1 > 0.3:
            r = (min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1))
            t.mark(t.occ, r); t.mark(t.walk, r)
            if y1 > 1.7:
                t.mark(t.tall, r)
    for p in plan.props:
        pd = p["def"]
    for r in plan.footprint:   # nothing else inside (furniture is the building's business)
        t.mark(t.occ, (r[0] - 0.6, r[1] - 0.6, r[2] + 0.6, r[3] + 0.6))
    for dd in plan.doors:
        if dd["floor"] == 0:
            for pt in (dd["other"], dd["swing"]):
                px, _, pz = pt
                inside = any(r[0] < px < r[2] and r[1] < pz < r[3] for r in plan.footprint)
                if not inside:
                    t.keepout.append((px, pz, 2.2))
                    t.mark(t.occ, (px - 1.4, pz - 1.4, px + 1.4, pz + 1.4))


def foot_rects():
    out = []
    for (name, x, z, yaw, pal, plan, seed) in PLACED:
        out += plan.footprint
    return out


FOOTS = foot_rects()


def in_building(x, z, pad=0.0):
    return any(r[0] - pad < x < r[2] + pad and r[1] - pad < z < r[3] + pad for r in FOOTS)


# ================================================================ hedgerows
# Field hedges for half A (the twins are made by turning them). Gaps: (x, z, kind) near the line; "gate" = 4.6 m with a
# timber field gate between granite posts, "gap" = 3.4 m worn through.
FIELD_A = [
    ([(-106, -35), (-92, -35), (-80, -36), (-62, -35)], [(-90, -35, "gate"), (-72, -35.5, "gap")]),
    ([(-61, -80), (-60, -56), (-61, -36), (-60, -24), (-58, -12), (-59, 0)], [(-60, -62, "gate"), (-60.5, -30, "gap"), (-58.5, -7, "gate")]),
    ([(-60, -40), (-46, -38), (-30, -42), (-16, -40), (-8, -40)], [(-52, -38.8, "gate"), (-24, -41.2, "gap")]),
    ([(-38, -82), (-36, -60), (-38, -42)], [(-36.5, -64, "gate")]),
    ([(-46, -40), (-47, -26), (-46, -10), (-48, 6)], [(-47, -32, "gap"), (-46.5, -3, "gate")]),
    ([(-108, 40), (-88, 42), (-72, 37), (-56, 41), (-44, 38), (-30, 46), (-22, 44)], [(-81, 40.5, "gate"), (-50, 39.5, "gap"), (-37, 42, "gate")]),
    ([(-76, 8), (-74, 24), (-76, 39)], [(-74.5, 17, "gate"), (-75, 32, "gap")]),
    ([(-50, 8), (-52, 24), (-50, 39)], [(-51.5, 14, "gap"), (-51, 31, "gate")]),
    ([(-66, 40), (-64, 58), (-66, 84)], [(-64.5, 52, "gate")]),
    ([(-108, 60), (-90, 58), (-66, 60)], [(-84, 58.5, "gap")]),
    ([(-38, 8), (-36, 24), (-30, 36)], [(-36.8, 19, "gate")]),
    ([(-8, -46), (6, -44), (18, -48), (34, -48)], [(1, -44.6, "gate"), (14, -46.8, "gap")]),
    ([(4, -46), (4, -62), (2, -86)], [(4, -56, "gap"), (3, -74, "gate")]),
]
HEDGES = []      # (pts, gaps, kind)
for pts, gaps in FIELD_A:
    HEDGES.append((pts, gaps, "field"))
    HEDGES.append(([M(p) for p in pts], [(-x, -z, k) for (x, z, k) in gaps], "field"))

LANE_OFF = 5.2
EXTRA_LANE_GAPS = {("N", 1): [61.0], ("S", 1): [61.0]}   # into the hill fields beside the north and south lanes
FARM_FRONT_A = (-97.0, -74.0)      # the Blue farmyard opens onto the west lane between these x (north side)
for name, pts in LANES:
    for side in (1, -1):
        line = offset_line(pts, side * LANE_OFF)
        L = polyline_len(line)
        gaps = []
        # field gates every ~30 m, staggered between the two sides
        s = 16.0 if side > 0 else 31.0
        while s < L - 6:
            x, z, ux, uz = at_arc(line, s)
            gaps.append((x, z, "gate" if (int(s) // 30) % 2 == 0 else "gap"))
            s += 30.0
        for s in EXTRA_LANE_GAPS.get((name, side), ()):
            x, z, ux, uz = at_arc(line, s)
            gaps.append((x, z, "gap"))
        HEDGES.append((line, gaps, "lane:" + name))


def lane_clear(x, z, own=None):
    """Is (x, z) in a lane's corridor (other than `own`), the brook's, a village square or a farm front?"""
    for name, pts in LANES:
        if name == own:
            continue
        if poly_dist((x, z), pts) < LANE_OFF - 0.6:
            return True
    if poly_dist((x, z), STREAM) < 6.0:
        return True
    if min(math.hypot(x - J1[0], z - J1[1]), math.hypot(x - J2[0], z - J2[1])) < VILLAGE_R:
        return True
    for sgn in (1, -1):
        fx0, fx1 = FARM_FRONT_A
        if fx0 < x * sgn < fx1 and -2 < z * sgn < 8:
            return True
    return False


HEDGE_PIECES = []
GATES = []


def sink(nm, x, z, yaw):
    """How far below the ground at its middle the game will stand this prop (the lowest of 9 points of its bounds)."""
    r = t.foot(nm, x, z, yaw)
    base = min(gy(px, pz) for px in (r[0], (r[0] + r[2]) / 2, r[2]) for pz in (r[1], (r[1] + r[3]) / 2, r[3]))
    return gy(x, z) - base


def hedge_run(pts, gaps, kind, seed):
    """Lays hedge props along a polyline, leaving the gaps. Pieces in a cleared zone are dropped."""
    own = kind.split(":")[1] if kind.startswith("lane:") else None
    L = polyline_len(pts)
    cut = []
    for (gx, gz, k) in gaps:
        s = arc_of(pts, (gx, gz))
        w = 4.6 if k == "gate" else 3.4
        cut.append((s - w / 2, s + w / 2, k, s))
    cut.sort()
    solid = []
    u = 0.0
    for (a, b, k, s) in cut:
        if a > u:
            solid.append((u, a, u > 0, True))
        u = max(u, b)
    if u < L:
        solid.append((u, L, u > 0, False))
    r = random.Random(seed)
    for (a, b, cap0, cap1) in solid:
        ln = b - a
        if ln < 1.5:
            continue
        pieces = []
        s = a
        while s + 6.0 <= b + 0.01:
            pieces.append((s, s + 6.0, r.choice(["Bocage Hedgerow", "Bocage Hedgerow B"])))
            s += 5.6
        rem = b - s
        if rem > 0.4:
            if rem + 0.4 > 3.0 or not pieces:
                nm, pl = ("Bocage Hedgerow Short", 3.0) if rem <= 3.0 else ("Bocage Hedgerow", 6.0)
                pieces.append((max(a, b - pl), max(a, b - pl) + pl, nm))
            else:
                pieces.append((b - 3.0, b, "Bocage Hedgerow Short"))
        if cap0:
            pieces.append((a, a + 2.0, "Bocage Hedgerow End"))
        if cap1:
            pieces.append((b - 2.0, b, "Bocage Hedgerow End"))
        for (s0, s1, nm) in pieces:
            x0, z0, _, _ = at_arc(pts, s0)
            x1, z1, _, _ = at_arc(pts, s1)
            cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
            if lane_clear(cx, cz, own) or in_building(cx, cz, 1.6) or not t.inside(cx, cz, 1.4):
                continue
            if any(kr < 4.0 and math.hypot(cx - kx, cz - kz) < kr + 1.2 for (kx, kz, kr) in t.keepout):   # spawns, flags, doors (hills keep their hedges)
                continue
            yaw = yaw_along(x1 - x0, z1 - z0)
            # The game stands a prop on the lowest ground under its (axis-aligned) bounds; on a bank that sinks a long,
            # turned piece so far its top is in reach from the high side. Such pieces are halved.
            if nm.startswith("Bocage Hedgerow") and "Short" not in nm and "End" not in nm and sink(nm, cx, cz, yaw) > 0.45:
                for (u0, u1) in ((s0, s0 + 3.0), (s1 - 3.0, s1)):
                    xa, za, _, _ = at_arc(pts, u0)
                    xb, zb, _, _ = at_arc(pts, u1)
                    HEDGE_PIECES.append(("Bocage Hedgerow Short", (xa + xb) / 2, (za + zb) / 2, yaw_along(xb - xa, zb - za)))
                continue
            HEDGE_PIECES.append((nm, cx, cz, yaw))
    for (a, b, k, s) in cut:
        x, z, ux, uz = at_arc(pts, s)
        if k == "gate" and not lane_clear(x, z, own) and not in_building(x, z, 2) and t.inside(x, z, 3):
            GATES.append((x, z, yaw_along(ux, uz)))


for i, (pts, gaps, kind) in enumerate(HEDGES):
    hedge_run(pts, gaps, kind, 900 + i)

for (nm, x, z, yaw) in HEDGE_PIECES:
    lift = min(0.4, max(0.0, sink(nm, x, z, yaw) - 0.5))     # the last few on steep banks: lifted a little (the bank still meets the slope)
    op = {"op": "Prop", "name": nm, "x": R(x), "z": R(z), "yaw": R(yaw % 360)}
    if lift > 0.02:
        op["y"] = R(lift)
    t.ops.append(op)
    t.props.append((nm, x, z, yaw, 0.0, "hedge"))
    r = t.foot(nm, x, z, yaw)
    # the rotated outline, marked as a fat line (the bounding box of a turned piece is far too big)
    a = math.radians(yaw); ux, uz = math.cos(a), -math.sin(a)
    half = (6.0 if "Short" not in nm and "End" not in nm else (3.0 if "Short" in nm else 2.0)) / 2
    for k in range(int(half * 8) + 1):
        f = -half + k * 0.25
        px, pz = x + ux * f, z + uz * f
        rr = (px - 1.15, pz - 1.15, px + 1.15, pz + 1.15)
        t.mark(t.occ, rr); t.mark(t.walk, rr); t.mark(t.tall, rr)
for (x, z, yaw) in GATES:
    t.ops.append({"op": "Prop", "name": "Field Gate Open", "x": R(x), "z": R(z), "yaw": R(yaw % 360)})
    t.props.append(("Field Gate Open", x, z, yaw, 0.0, "gate"))
    a = math.radians(yaw); ux, uz = math.cos(a), -math.sin(a)
    for sgn in (1, -1):
        px, pz = x + ux * sgn * 1.95, z + uz * sgn * 1.95
        t.mark(t.walk, (px - 0.2, pz - 0.2, px + 0.2, pz + 0.2)); t.mark(t.occ, (px - 0.3, pz - 0.3, px + 0.3, pz + 0.3))
    t.keepout.append((x, z, 2.4))
print(f"hedge pieces {len(HEDGE_PIECES)}, gates {len(GATES)}")

# lanes are lanes: solid props keep off them; foliage paths along them (a few tufts down the middle)
for name, pts in LANES:
    t.add_lane(pts, 4.0)
    t.path(pts, 4.2, density=0.08)
t.add_lane([J1, J2], 5.0)

# ================================================================ the bridge and the brook
DECK_TOP = SQUARE_Y + 0.05
STONE = (0.55, 0.53, 0.49)
STONE2 = (0.47, 0.45, 0.42)


def wblock(x0, y0, z0, x1, y1, z1, c):
    t.ops.append({"op": "Block", "x0": R(min(x0, x1)), "y0": R(y0), "z0": R(min(z0, z1)), "x1": R(max(x0, x1)), "y1": R(y1), "z1": R(max(z0, z1)),
                  "color": col(c), "anchor": "world"})
    t.boxes.append((min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1), y0, y1, c))


def wmesh(kind, x, z, c, y, **kw):
    op = {"op": "Mesh", "mesh": kind, "x": R(x), "z": R(z), "y": R(y)}
    for k, v in kw.items():
        op[k] = R(v) if isinstance(v, float) else v
    op["color"] = col(c)
    op["anchor"] = "world"
    t.ops.append(op); t.meshes += 1


wblock(-7.4, DECK_TOP - 0.5, -2.4, 7.4, DECK_TOP, 2.4, STONE)                     # the deck
for sgn in (1, -1):                                                                  # parapets (waist high: cover, mantle over)
    wblock(-7.4, DECK_TOP, sgn * 2.4, 7.4, DECK_TOP + 0.95, sgn * 2.85, STONE2)
    for x in (-7.4, 7.4):
        wmesh("box", x, sgn * 2.62, STONE, DECK_TOP + 0.95, width=0.7, height=0.18, depth=0.7, material="Concrete")
    wmesh("box", 0, sgn * 2.62, STONE, DECK_TOP + 0.95, width=14.6, height=0.08, depth=0.55, material="Concrete")
# the arch's face under the deck either side (cosmetic: a ring of stone voussoirs)
for sgn in (1, -1):
    wmesh("tube", 0, sgn * 2.45, STONE2, DECK_TOP - 0.55, radius=3.2, inner=2.7, height=0.3, pitch=90.0, segments=20, material="Concrete")
t.mark(t.occ, (-7.6, -3.0, 7.6, 3.0))
# water: flat strips along the bed
WATER = (0.24, 0.36, 0.38)
for k in range(len(STREAM) - 1):
    (ax, az), (bx, bz) = STREAM[k], STREAM[k + 1]
    L = math.hypot(bx - ax, bz - az); n = max(1, int(L / 5.0))
    for i in range(n):
        x0, z0 = ax + (bx - ax) * i / n, az + (bz - az) * i / n
        x1, z1 = ax + (bx - ax) * (i + 1) / n, az + (bz - az) * (i + 1) / n
        mx, mz = (x0 + x1) / 2, (z0 + z1) / 2
        if not t.inside(mx, mz, -3):
            continue
        wmesh("box", mx, mz, WATER, WATER_Y - 0.04, width=L / n + 0.8, height=0.04, depth=3.6, yaw=yaw_along(x1 - x0, z1 - z0))
# stepping stones at two fords (twins)
for (fx, fz) in ((26, -44), (-26, 44)):
    k = STREAM.index((22, -36)) if fx > 0 else STREAM.index((-22, 36))
    ux, uz = (34 - 22, -60 + 36) if fx > 0 else (-34 + 22, 60 - 36)
    L = math.hypot(ux, uz); ux, uz = ux / L, uz / L
    nx, nz = -uz, ux
    for j in range(-2, 3):
        x, z = fx + nx * j * 0.9, fz + nz * j * 0.9
        wmesh("roundedbox", x, z, (0.5, 0.49, 0.46), WATER_Y - 0.12, width=0.7, height=0.22, depth=0.6, radius=0.08, yaw=float(rng.uniform(0, 90)), material="Concrete")
    t.ops.append({"op": "Waypoint", "x": R(fx), "z": R(fz)})

# ================================================================ farms (twins) and the village
def twin_prop(name, x, z, yaw=0.0, tag="farm", y=None, check=True, both=True):
    ok = t.prop(name, x, z, yaw, y=y, check=check, tag=tag)
    if both:
        ok2 = t.prop(name, -x, -z, yaw + 180, y=y, check=check, tag=tag)
        return ok, ok2
    return ok


# the grain silo by each barn
for (x, z) in (SILO_A, M(SILO_A)):
    t.prop("Concrete Grain Silo", x, z, 0, check=False, tag="farm")
    t.mark(t.occ, (x - 2.3, z - 2.3, x + 2.3, z + 2.3)); t.mark(t.walk, (x - 2.0, z - 2.0, x + 2.0, z + 2.0)); t.mark(t.tall, (x - 2.0, z - 2.0, x + 2.0, z + 2.0))
# the farmyard (between the farmhouse, the barn and the lane): tractor, wagon, well, trough, muck heap, bales
YARD = [("Vintage Tractor", -82.0, -8.0, 30.0), ("Norman Hay Wagon", -91.5, -16.5, 85.0), ("Stone Well", -88.0, -6.0, 0.0),
        ("Stone Water Trough", -76.5, -16.0, 90.0), ("Muck Heap", -100.0, -18.0, 15.0), ("Round Hay Bale", -79.5, 1.5, 10.0),
        ("Round Hay Bale", -77.8, 1.2, 80.0), ("Hay Bale Stack", -101.0, -6.0, 90.0), ("Tipped Farm Cart", -96.5, 3.5, 200.0),
        ("Milk Churns", -84.0, -17.0, 0.0), ("Firewood Stack", -102.0, -26.0, 90.0), ("Cider Barrel", -77.0, -20.5, 0.0),
        ("Cider Barrel", -76.2, -21.2, 0.0), ("Wheelbarrow", -93.0, -3.0, 140.0), ("Leaning Farm Tools", -75.5, -2.5, 90.0)]
for (nm, x, z, yaw) in YARD:
    twin_prop(nm, x, z, yaw, tag="yard")
for (nm, x, z, yaw) in [("Chicken", -86.5, -9.0, 30), ("Chicken", -85.0, -10.2, 200), ("Chicken", -90.5, -1.0, 110), ("Loose Straw", -84.0, -4.0, 0),
                        ("Loose Straw", -92.0, -10.0, 70), ("Bucket", -87.0, -4.8, 0), ("Cat", -81.0, -18.0, 60), ("Dog Kennel", -98.5, -12.5, 90)]:
    twin_prop(nm, x, z, yaw, tag="yard")
# the orchards behind the farmhouses: rows of apple trees, a ladder, crates and windfalls
ORCH_A = (-100.0, -72.0, -66.0, -39.0)
ORCH_TREES = []
for i, x in enumerate(np.arange(ORCH_A[0] + 3, ORCH_A[2] - 2, 6.5)):
    for j, z in enumerate(np.arange(ORCH_A[1] + 4, ORCH_A[3] - 2, 6.5)):
        jx, jz = rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6)
        nm = "Apple Tree" if (i + j) % 2 else "Apple Tree B"
        yaw = rng.uniform(0, 360)
        for (px, pz, py) in ((x + jx, z + jz, yaw), (-(x + jx), -(z + jz), yaw + 180)):
            if t.inside(px, pz, 2.5) and t.prop(nm, px, pz, py, tag="orchard"):
                ORCH_TREES.append((px, pz))
for (nm, x, z, yaw) in [("Orchard Ladder", -84.5, -55.0, 40), ("Apple Crate", -80.0, -48.0, 10), ("Apple Crate", -79.2, -48.4, 60),
                        ("Apple Crate", -90.0, -62.0, 0), ("Cider Barrel", -71.0, -44.0, 0), ("Wheelbarrow", -88.0, -50.0, 70),
                        ("Sheep", -94.0, -58.0, 30), ("Sheep", -92.5, -66.0, 160), ("Sheep", -73.0, -60.0, 260)]:
    twin_prop(nm, x, z, yaw, tag="orchard")

# the village: the chapel's calvary and signposts at the west junction, the well and the mairie's flag at the east
t.prop("Calvary Cross", -15.0, -7.5, 0, tag="village", check=False); t.mark(t.occ, (-16.3, -8.8, -13.7, -6.2)); t.mark(t.walk, (-16.2, -8.7, -13.8, -6.3))
t.prop("Stone Well", 15.0, 7.5, 180, tag="village", check=False); t.mark(t.occ, (14.0, 6.5, 16.0, 8.5)); t.mark(t.walk, (14.2, 6.7, 15.8, 8.3))
for (x, z, yaw) in ((-17.0, 6.0, 30), (17.0, -6.0, 210)):
    t.prop("Village Signpost", x, z, yaw, tag="village")
t.prop("Flag on a Pole", 18.5, 4.8, 270, tag="village", check=False)
for (nm, x, z, yaw) in [("Bench", -12.5, -10.5, 180), ("Bench", 12.5, 10.5, 0), ("Bicycle", -18.5, 9.0, 80), ("Flower Pots", -11.0, 7.0, 0),
                        ("Flower Pots", 11.0, -7.0, 0), ("Cider Barrel", 18.0, -9.0, 0), ("Cider Barrel", 18.8, -9.6, 0),
                        ("Patio Table", 13.0, -10.5, 0), ("Patio Chair", 12.0, -11.3, 180), ("Patio Chair", 14.0, -9.8, 20),
                        ("Post Box", -12.0, 6.5, 180), ("Utility Pole", -20.0, -8.5, 0), ("Utility Pole", 20.0, 8.5, 0)]:
    t.prop(nm, x, z, yaw, tag="village")
# low drystone walls round the village gardens and the churchyard (cover, mantle over)
WALLS_A = [((-40.0, -6.5), (-22.0, -6.5)), ((-14.0, -24.0), (-14.0, -18.5)), ((-34.0, 10.0), (-18.0, 10.0)), ((-20.0, 22.0), (-20.0, 13.0))]
for (a, b) in WALLS_A:
    for (p, q_) in ((a, b), (M(a), M(b))):
        L = math.hypot(q_[0] - p[0], q_[1] - p[1]); n = max(1, int(round(L / 3.0)))
        for k in range(n):
            f = (k + 0.5) / n
            t.prop("Drystone Wall", p[0] + (q_[0] - p[0]) * f, p[1] + (q_[1] - p[1]) * f, yaw_along(q_[0] - p[0], q_[1] - p[1]), tag="wall")

# ================================================================ the fields: hay, cattle, sheep, a plough, telegraph poles
FIELD_ITEMS_A = [
    # hay meadow, north fields: round bales scattered (solid cover across the open grass)
    ("Round Hay Bale", -55, -50, 20), ("Round Hay Bale", -48, -60, 80), ("Round Hay Bale", -44, -50, 140), ("Round Hay Bale", -52, -68, 10),
    ("Round Hay Bale", -26, -52, 60), ("Round Hay Bale", -18, -64, 110), ("Round Hay Bale", -30, -72, 30),
    ("Hay Bale Stack", -42, -70, 0), ("Norman Hay Wagon", -22, -48, 100),
    # pasture, south-west: the herd, a trough, a muck heap
    ("Dairy Cow", -90, 50, 30), ("Grazing Cow", -84, 48, 200), ("Grazing Cow", -78, 54, 120), ("Dairy Cow", -96, 52, 300),
    ("Grazing Cow", -60, 26, 60), ("Dairy Cow", -64, 16, 160), ("Stone Water Trough", -70, 48, 90), ("Stone Water Trough", -62, 30, 0),
    ("Grazing Cow", -40, 26, 230), ("Dairy Cow", -44, 32, 10),
    ("Sheep", -56, 70, 30), ("Sheep", -52, 72, 200), ("Sheep", -58, 74, 110), ("Sheep", -46, 66, 300),
    # the ploughed field by the chapel, the farm's cart track
    ("Rusty Plough", -58, -24, 30), ("Vintage Tractor", -52, -18, 300), ("Tipped Farm Cart", -55, -4, 160),
    ("Round Hay Bale", -86, 32, 0), ("Hay Bale Stack", -96, 34, 90),
    ("Round Hay Bale", 10, -58, 40), ("Round Hay Bale", -2, -70, 90), ("Grazing Cow", 12, -76, 20), ("Dairy Cow", 18, -70, 250),
]
for (nm, x, z, yaw) in FIELD_ITEMS_A:
    twin_prop(nm, x, z, yaw, tag="field")
# telegraph poles along the lanes (every ~24 m, on the bank behind the hedge line's inside)
for name, pts in LANES:
    line = offset_line(pts, LANE_OFF)          # standing in the hedge line, as they do
    L = polyline_len(line)
    s = 10.0
    while s < L - 5:
        x, z, ux, uz = at_arc(line, s)
        in_hedge = any(abs(hx - x) < 2.0 and abs(hz - z) < 2.0 for (_, hx, hz, _) in HEDGE_PIECES)
        if in_hedge and t.inside(x, z, 3) and not any(math.hypot(x - gx, z - gz) < 4.0 for (gx, gz, _) in GATES):
            t.prop("Utility Pole", x, z, yaw_along(ux, uz), tag="poles", check=False)
        s += 24.0
# milk churns and a few things at the lane gates
for k, (x, z, yaw) in enumerate(GATES[::3]):
    a = math.radians(yaw)
    t.prop(rng.choice(["Milk Churns", "Hay Bale", "Round Hay Bale"]), x + math.cos(a) * 3.2 + 1.6, z - math.sin(a) * 3.2 + 1.6, yaw, tag="gate")

# ================================================================ trees: hedgerow oaks, willows by the brook, the woods beyond the edge
oaks = 0
for i, (pts, gaps, kind) in enumerate(HEDGES):
    L = polyline_len(pts)
    s = 7.0 + (i % 3) * 4
    while s < L - 4:
        x, z, ux, uz = at_arc(pts, s)
        near_piece = any(abs(hx - x) < 2.5 and abs(hz - z) < 2.5 for (_, hx, hz, _) in HEDGE_PIECES)
        if near_piece and t.inside(x, z, 3) and not in_building(x, z, 3):
            h = rng.uniform(10.0, 14.0)
            t.items.append({"type": "tree", "x": R(x), "z": R(z), "height": R(h)})
            t.boxes.append((x - 0.25, z - 0.25, x + 0.25, z + 0.25, 0, h, (0.32, 0.22, 0.12)))
            oaks += 1
        s += rng.uniform(16.0, 24.0)
willows = 0
for k in range(0, 9):
    for sgn in (1, -1):
        s = 8 + k * 21
        x, z, ux, uz = at_arc(STREAM, s)
        x, z = x + (-uz) * sgn * 4.6, z + ux * sgn * 4.6
        if t.inside(x, z, 3) and abs(z) > 14 and t.free((x - 0.5, z - 0.5, x + 0.5, z + 0.5)) and not t.in_keepout((x - 0.5, z - 0.5, x + 0.5, z + 0.5), 1):
            t.items.append({"type": "tree", "x": R(x), "z": R(z), "height": R(rng.uniform(7.0, 9.5)), })
            t.boxes.append((x - 0.25, z - 0.25, x + 0.25, z + 0.25, 0, 8, (0.32, 0.22, 0.12)))
            willows += 1
# village lime trees
for (x, z) in ((-20.0, -24.0), (-36.0, -3.0), (20.0, 24.0), (36.0, 3.0), (-12.0, 14.0), (12.0, -14.0)):
    t.tree(x, z, 9.5)
# the woods beyond the edge: mesh trees on the berm (cosmetic; the berm is the terrain's cliff edge)
pts = t.outline
n = len(pts)
arcs = [0.0]
for i in range(1, n + 1):
    arcs.append(arcs[-1] + math.hypot(pts[i % n][0] - pts[i - 1][0], pts[i % n][1] - pts[i - 1][1]))
total = arcs[-1]
area_sign = 1 if tk._signed_area(pts) > 0 else -1
scenery = 0
s = 0.0
j = 0
while s < total:
    while arcs[j + 1] < s:
        j += 1
    a_, b_ = pts[j], pts[(j + 1) % n]
    f = (s - arcs[j]) / max(1e-6, arcs[j + 1] - arcs[j])
    x, z = a_[0] + (b_[0] - a_[0]) * f, a_[1] + (b_[1] - a_[1]) * f
    dx, dz = b_[0] - a_[0], b_[1] - a_[1]; L = math.hypot(dx, dz)
    # outward normal
    ox, oz = (dz / L, -dx / L) if area_sign > 0 else (-dz / L, dx / L)
    for row in range(3):
        d = rng.uniform(3.5, 9.0) + row * 8.0
        px, pz = x + ox * d, z + oz * d
        if t.edge(px, pz) > -3.0:
            continue
        h = rng.uniform(9.0, 15.0)
        c = rng.choice([(0.27, 0.40, 0.19), (0.31, 0.44, 0.21), (0.24, 0.36, 0.17), (0.34, 0.46, 0.22)])
        t.mesh("cylinder", px, pz, (0.30, 0.22, 0.14), width=0.6, height=h * 0.55, depth=0.6, segments=6)
        t.mesh("icosphere", px, pz, c, y=h * 0.42, width=h * 0.55, height=h * 0.5, depth=h * 0.55, detail=1, material="Dirt")
        t.mesh("icosphere", px + rng.uniform(-1, 1), pz + rng.uniform(-1, 1), c, y=h * 0.65, width=h * 0.38, height=h * 0.35, depth=h * 0.38, detail=1, material="Dirt")
        scenery += 1
    s += rng.uniform(5.5, 8.0)

# ================================================================ foliage
t.region("grass", 1.7, polygon=[[R(x), R(z)] for x, z in t.outline[::6]], scale=[0.9, 1.4], color=[0.36, 0.5, 0.22], patchiness=0.3)
for i, (pts, gaps, kind) in enumerate(HEDGES):     # brambles, nettles and cow parsley at the hedge feet; bushes softening the banks
    for k in range(len(pts) - 1):
        a, b = pts[k], pts[k + 1]
        tk.weeds_along(t, a, b, width=3.6, density=3.0)
        tk.hedge_strip(t, a, b, width=3.2, density=0.35, height=(1.0, 1.7), color=(0.21, 0.33, 0.15))
for k in range(len(STREAM) - 1):                    # reeds and long grass along the brook
    a, b = STREAM[k], STREAM[k + 1]
    L, (ux, uz) = tk._along(a, b); nx, nz = -uz, ux
    for sgn in (1, -1):
        p0 = (a[0] + nx * sgn * 3.0, a[1] + nz * sgn * 3.0); p1 = (b[0] + nx * sgn * 3.0, b[1] + nz * sgn * 3.0)
        q0 = (a[0] + nx * sgn * 5.5, a[1] + nz * sgn * 5.5); q1 = (b[0] + nx * sgn * 5.5, b[1] + nz * sgn * 5.5)
        t.region("grass", 5.0, polygon=[[R(p0[0]), R(p0[1])], [R(p1[0]), R(p1[1])], [R(q1[0]), R(q1[1])], [R(q0[0]), R(q0[1])]], scale=[1.3, 2.1],
                 color=[0.42, 0.52, 0.26], patchiness=0.5)
t.path(STREAM, 3.4)
t.path([J1, J2], 5.0)
for (x0, z0, x1, z1) in (ORCH_A, (-ORCH_A[2], -ORCH_A[3], -ORCH_A[0], -ORCH_A[1])):
    t.region("grass", 2.5, rect=[R(x0), R(z0), R(x1), R(z1)], scale=[1.0, 1.6], color=[0.40, 0.55, 0.24], patchiness=0.4)
for sgn in (1, -1):   # the farmyards: trodden, a few tufts
    t.exclude.append({"shape": "rect", "center": [R(-87 * sgn), R(-6 * sgn)], "halfX": 12, "halfZ": 10, "density": 0.12})
    t.exclude.append({"shape": "circle", "center": [R(-12 * sgn), 0], "radius": 9, "density": 0.15})   # the village squares

# ================================================================ waypoints: every gap and gate, both sides; the lanes; the bridge
for (pts, gaps, kind) in HEDGES:
    for (gx, gz, k) in gaps:
        s = arc_of(pts, (gx, gz))
        x, z, ux, uz = at_arc(pts, s)
        if lane_clear(x, z, kind.split(":")[1] if ":" in kind else None) or not t.inside(x, z, 3):
            continue
        nx, nz = -uz, ux
        for d in (-2.6, 0.0, 2.6):
            px, pz = x + nx * d, z + nz * d
            if t.inside(px, pz, 1.5) and not in_building(px, pz, 0.5):
                t.ops.append({"op": "Waypoint", "x": R(px), "z": R(pz)})
for name, pts in LANES:
    for (x, z, s, ux, uz) in resample(pts, 6.0):
        if t.inside(x, z, 2.0) and abs(x) > 7.6:
            t.ops.append({"op": "Waypoint", "x": R(x), "z": R(z)})

# ================================================================ decals
DECALS = []


def decal(name, x, z, **kw):
    d = {"op": "Decal", "decal": name, "x": R(x), "z": R(z)}
    d.update({k: (R(v) if isinstance(v, float) else v) for k, v in kw.items()})
    t.ops.append(d); DECALS.append(name)


def scatter(names, x0, z0, x1, z1, count, seed, **kw):
    d = {"op": "DecalScatter", "decals": names, "x0": R(x0), "z0": R(z0), "x1": R(x1), "z1": R(z1), "count": count, "seed": seed}
    d.update(kw)
    t.ops.append(d)


for name, pts in LANES:   # cart ruts, mud and puddles down the lanes
    for (x, z, s, ux, uz) in resample(pts, 7.0):
        if not t.inside(x, z, 2.0) or abs(x) < 8:
            continue
        r_ = rng.random()
        rot = (math.degrees(math.atan2(ux, -uz)) + 360) % 360
        if r_ < 0.55:
            decal("Bocage Cart Ruts", x, z, rotation=R(rot))
        elif r_ < 0.75:
            decal("Puddle", x + rng.uniform(-1, 1), z + rng.uniform(-1, 1))
        else:
            decal("Mud Patch", x, z)
for (x, z, yaw) in GATES:
    decal("Bocage Hoof Prints", x, z)
    decal("Mud Patch", x + rng.uniform(-0.8, 0.8), z + rng.uniform(-0.8, 0.8), size=2.6)
for sgn in (1, -1):
    scatter(["Bocage Cow Pat"], -100 * sgn, 40 * sgn, -40 * sgn, 74 * sgn, 50, 61 if sgn > 0 else 62)
    scatter(["Bocage Cow Pat"], -70 * sgn, 10 * sgn, -36 * sgn, 38 * sgn, 25, 63 if sgn > 0 else 64)
    scatter(["Bocage Apple Blossom", "Bocage Apple Blossom", "Leaf Litter"], ORCH_A[0] * sgn, ORCH_A[1] * sgn, ORCH_A[2] * sgn, ORCH_A[3] * sgn, 45, 65 if sgn > 0 else 66, gap=1.5)
    scatter(["Mud Patch", "Tyre Tracks", "Dirt Patch", "Puddle"], -100 * sgn, -18 * sgn, -74 * sgn, 4 * sgn, 30, 67 if sgn > 0 else 68, gap=2.0)
    scatter(["Mud Patch", "Puddle"], -62 * sgn, -36 * sgn, -48 * sgn, -2 * sgn, 14, 69 if sgn > 0 else 70)   # the ploughed field
scatter(["Gravel Scatter", "Dirt Patch", "Footprints"], -20, -6, 20, 6, 16, 71)
# walls: lichen, moss and whitewash wear on the stone; posters and the bunting in the village
for (name, x, z, yaw, pal, plan, seed) in PLACED:
    for r in plan.footprint[:1]:
        x0, z0, x1, z1 = r
        for k in range(4):
            px = rng.uniform(x0 + 1, x1 - 1); pz = z1 + 1.2
            decal(rng.choice(["Bocage Lichen", "Moss Patch", "Bocage Whitewash Wear", "Grime Streaks"]), px, pz, y=R(rng.uniform(0.4, 1.6)), onto="wall-z")
            px = rng.uniform(x0 + 1, x1 - 1); pz = z0 - 1.2
            decal(rng.choice(["Bocage Lichen", "Moss Patch", "Bocage Whitewash Wear", "Water Damage"]), px, pz, y=R(rng.uniform(0.4, 1.6)), onto="wall+z")
decal("Bocage Tricolour Bunting", 24.0 - 4.9, 12.0, y=3.0, onto="wall+x", reach=3.0)   # across the mairie's front
decal("Bocage Cider Poster", -26.0 + 2.5, 16.0 - 4.6, y=1.3, onto="wall+z", reach=2.0)
decal("Bocage Cider Poster", 26.0 - 2.0, -16.0 + 4.6, y=1.3, onto="wall-z", reach=2.0)
decal("Bocage Village Sign", -26.0 - 3.0, 16.0 - 4.6, y=1.6, onto="wall+z", reach=2.0)
decal("Bocage Village Sign", 26.0 + 3.0, -16.0 + 4.6, y=1.6, onto="wall-z", reach=2.0)

# ================================================================ cosmetic clutter (the game's Clutter runs last and keeps out of walls)
for sgn in (1, -1):
    t.clutter(["Fallen Apples", "Fallen Apples", "Fallen Apples", "Loose Straw"], ORCH_A[0] * sgn, ORCH_A[1] * sgn, ORCH_A[2] * sgn, ORCH_A[3] * sgn, 90, 80 if sgn > 0 else 81)
    t.clutter(["Loose Straw", "Loose Straw", "Chicken", "Bucket", "Plank"], -100 * sgn, -18 * sgn, -74 * sgn, 4 * sgn, 40, 82 if sgn > 0 else 83)
    t.clutter(["Sheep", "Sheep", "Fallen Apples"], -64 * sgn, 60 * sgn, -36 * sgn, 78 * sgn, 10, 84 if sgn > 0 else 85)
    t.clutter(["Loose Straw", "Plank", "Brick Pile", "Bucket", "Chicken"], -36 * sgn, -6 * sgn, -10 * sgn, 22 * sgn, 18, 86 if sgn > 0 else 87)

# ================================================================ spawns and the map
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": R(x), "z": R(z)})
t.ops.append({"op": "AutoWaypoints", "spacing": 6})

# building ops first (so everything after them sees them), then the rest
bops = []
for (name, x, z, yaw, pal, plan, seed) in PLACED:
    bops.append({"op": "Building", "name": name, "at": [R(x), R(z)], "yaw": yaw, "seed": seed, "variant": {"palette": pal}})
ops = bops + t.ops

foliage = {"seed": 505, "surfaces": {"Wood": 0.0, "Concrete": 0.05}, "paths": t.paths, "regions": t.regions, "items": t.items,
           "exclude": t.exclude, "include": t.include}
mp = {
    "format": "call-of-slop-map/1",
    "name": "Bocage",
    "description": "Normandy hedgerow country: a maze of small fields walled in by tall hedges, sunken lanes, a hamlet on a brook, and a stone farm with a barn and a silo at each end.",
    "author": "Call of Slop",
    "order": 505,
    "maxPlayers": 22,
    "sky": "#a9bed0",
    "skybox": {"preset": "clear", "sun": {"time": 15.6, "latitude": 49}, "horizon": "#c4ccc8",
               "clouds": {"coverage": 0.55, "speed": 7, "heading": 70}, "haze": {"color": "#e6e2cc", "strength": 0.25}},
    "flagPool": ["france"],
    "boundary": {"shape": "smooth", "points": [list(p) for p in CTRL], "noise": NOISE, "edge": {"style": "cliff", "height": 6, "width": 8},
                 "groundMargin": 10, "barrierHeight": 4.5},
    "terrain": TERRAIN,
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": HILLS[0][0], "z": HILLS[0][1], "y": DECK_TOP, "radius": HILLS[0][2]}] + [{"x": x, "z": z, "radius": r} for x, z, r in HILLS[1:]],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
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


os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
with open(OUT_JSON, "w") as f:
    f.write(dump(mp) + "\n")

# ================================================================ report and checks
from collections import Counter
cats = Counter(); solid = 0
for name, x, z, yaw, y, tag in t.props:
    cats[tag] += 1; solid += t.P[name]["solid"]
print(f"area {t.area():.0f} m2, half {HALF_X:.1f} x {HALF_Z:.1f}, blocks {sum(1 for o in ops if o['op'] == 'Block')}, buildings {len(PLACED)}, "
      f"hand props {len(t.props)} (solid {solid}), oaks {oaks}, willows {willows}, scenery trees {scenery}, meshes {t.meshes}, decals {len(DECALS)} + scatters")
print("props by tag:", dict(sorted(cats.items())))
print(f"terrain {T.nx} x {T.nz}, heights {T.min:.2f}..{T.max:.2f}")
for (x, z, r) in HILLS[1:]:
    c = gy(x, z); d = [gy(x + math.cos(a) * r * 0.8, z + math.sin(a) * r * 0.8) - c for a in np.linspace(0, math.tau, 9)[:-1]]
    print(f"hill ({x},{z}) ground {c:.2f}, edge spread {min(d):+.2f}..{max(d):+.2f}")

# --- walk field: walls, hedges, solid props, steep ground and the outside block; the bridge deck is walkable
CELL = 0.5
k_ = int(CELL / t.res)
NZ, NX = t.nz // k_, t.nx // k_
WALK = t.walk.copy()
t.mark(WALK, (-7.4, -2.4, 7.4, 2.4), 0)       # the deck (the stream under it is open too)
blk = WALK[:NZ * k_, :NX * k_].reshape(NZ, k_, NX, k_).max(axis=(1, 3)).astype(bool)
gxs = t.x0 + (np.arange(NX) + 0.5) * CELL; gzs = t.z0 + (np.arange(NZ) + 0.5) * CELL
GX, GZ = np.meshgrid(gxs, gzs)
steep = ~T.walkable_at(GX, GZ)
deck = (np.abs(GX) <= 7.4) & (np.abs(GZ) <= 2.4)
blk |= steep & ~deck
for i in range(NZ):
    for j in range(NX):
        if not blk[i, j] and t.edge(gxs[j], gzs[i]) < 0.4:
            blk[i, j] = True


def cell_of(x, z):
    return int((z - t.z0) / CELL), int((x - t.x0) / CELL)


def field(sources):
    dist = np.full((NZ, NX), np.inf); h = []
    for (x, z) in sources:
        c_ = cell_of(x, z); dist[c_] = 0; heapq.heappush(h, (0.0, c_[0], c_[1]))
    nb_ = [(-1, 0, 1), (1, 0, 1), (0, -1, 1), (0, 1, 1), (-1, -1, 1.4142), (-1, 1, 1.4142), (1, -1, 1.4142), (1, 1, 1.4142)]
    while h:
        d, i, j = heapq.heappop(h)
        if d > dist[i, j]:
            continue
        for di, dj, w in nb_:
            a, b = i + di, j + dj
            if 0 <= a < NZ and 0 <= b < NX and not blk[a, b]:
                if di and dj and (blk[i, b] or blk[a, j]):
                    continue
                nd = d + w * CELL
                if nd < dist[a, b]:
                    dist[a, b] = nd; heapq.heappush(h, (nd, a, b))
    return dist


FB = field(BLUE_SP); FR = field(RED_SP)
report = []
for i, (x, z, r) in enumerate(HILLS):
    fg = field([(x, z)])
    b_ = float(np.mean([fg[cell_of(*p)] for p in BLUE_SP])); r_ = float(np.mean([fg[cell_of(*p)] for p in RED_SP]))
    report.append((f"hill {i + 1}", b_, r_))
fb = field([FLAGS[1]]); fr = field([FLAGS[0]])
report.append(("enemy flag", float(np.mean([fb[cell_of(*p)] for p in BLUE_SP])), float(np.mean([fr[cell_of(*p)] for p in RED_SP]))))
for name, b_, r_ in report:
    print(f"walk {name}: blue {b_:.0f} m, red {r_:.0f} m ({(b_ - r_) / max(b_, r_, 1) * 100:+.1f}%)")
# reachability: open, inside cells that nobody can walk to (closed-off fields)
inside_open = ~blk
reach = np.isfinite(FB) | np.isfinite(FR)
lost = inside_open & ~reach
print(f"walkable cells {int(inside_open.sum())}, unreachable {int(lost.sum())} ({lost.sum() / max(1, inside_open.sum()) * 100:.1f}%)")
for x, z in BLUE_SP + RED_SP + FFA_SP + [(h[0], h[1]) for h in HILLS] + list(FLAGS):
    if not np.isfinite(FB[cell_of(x, z)]):
        print("WARN unreachable objective", x, z)
    if not t.inside(x, z, 1.5):
        print("WARN objective near/outside the edge", x, z)


def sees(a, b):
    ya, yb = gy(*a) + 1.6, gy(*b) + 1.6
    L = math.hypot(b[0] - a[0], b[1] - a[1]); n = max(1, int(L / 0.25))
    for s_ in range(1, n):
        f = s_ / n; x = a[0] + (b[0] - a[0]) * f; z = a[1] + (b[1] - a[1]) * f
        if gy(x, z) > ya + (yb - ya) * f:
            return False
        i = int((z - t.z0) / t.res); j = int((x - t.x0) / t.res)
        if t.tall[i, j]:
            return False
    return True


vis = [(a, b) for a in BLUE_SP for b in RED_SP if sees(a, b)]
print("blue-red spawn lines of sight:", len(vis), vis[:6])
if os.environ.get("BOCAGE_EQUAL"):
    # where Blue and Red walk the same distance (for placing hills 2 and 3)
    for z in range(-80, -20, 5):
        row = [(x, FB[cell_of(x, z)] - FR[cell_of(x, z)]) for x in range(-60, 50, 1) if np.isfinite(FB[cell_of(x, z)]) and np.isfinite(FR[cell_of(x, z)])]
        best = min(row, key=lambda v: abs(v[1])) if row else None
        print("equal walk at z", z, best)

if OUT_PNG:
    from PIL import Image, ImageDraw
    sc = 4
    W = int((t.x1 - t.x0) * sc); H = int((t.z1 - t.z0) * sc)
    xs = t.x0 + (np.arange(W) + 0.5) / sc; zs = t.z0 + (np.arange(H) + 0.5) / sc
    XX, ZZ = np.meshgrid(xs, zs)
    hh = T.sample(XX, ZZ)
    nrm = T.normal(XX, ZZ)
    shade = np.clip(0.55 + 0.45 * (nrm[..., 0] * -0.5 + nrm[..., 1] * 0.7 + nrm[..., 2] * -0.5), 0, 1)
    base = np.stack([0.33 + 0.03 * hh, 0.45 + 0.025 * hh, 0.25 + 0.0 * hh], -1) * shade[..., None]
    img = Image.fromarray((np.clip(base, 0, 1) * 255).astype(np.uint8)); d = ImageDraw.Draw(img)
    P = lambda x, z: ((x - t.x0) * sc, (z - t.z0) * sc)
    d.line([P(*p_) for p_ in t.outline[::3]] + [P(*t.outline[0])], fill=(250, 250, 250), width=2)
    for name, pts in LANES:
        d.line([P(x, z) for x, z in pts], fill=(150, 120, 80), width=int(4 * sc))
    d.line([P(x, z) for x, z in STREAM], fill=(60, 100, 130), width=int(3 * sc))
    for (name, x, z, yaw, pal, plan, seed) in PLACED:
        for r in plan.footprint:
            d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=(150, 110, 80), outline=(20, 20, 20))
    lost_img = np.zeros((H, W), bool)
    for name, x, z, yaw, y, tag in t.props:
        p_ = t.P[name]
        if tag == "hedge":
            a = math.radians(yaw); ux, uz = math.cos(a), -math.sin(a)
            half = p_["w"] / 2
            d.line([P(x - ux * half, z - uz * half), P(x + ux * half, z + uz * half)], fill=(20, 60, 20), width=int(2.3 * sc))
            continue
        r = t.foot(name, x, z, yaw)
        fill = (200, 120, 50) if p_["solid"] else (230, 230, 120)
        d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=fill if p_["solid"] else None, outline=fill)
    for (x, z, yaw) in GATES:
        d.ellipse([P(x - 1, z - 1), P(x + 1, z + 1)], fill=(240, 240, 240))
    for it in t.items:
        d.ellipse([P(it["x"] - 1.5, it["z"] - 1.5), P(it["x"] + 1.5, it["z"] + 1.5)], outline=(10, 70, 10), width=2)
    ii, jj = np.nonzero(lost)
    for i_, j_ in zip(ii[::7], jj[::7]):
        x, z = gxs[j_], gzs[i_]
        d.point(P(x, z), fill=(255, 0, 255))
    for x, z in BLUE_SP:
        d.ellipse([P(x - 1, z - 1), P(x + 1, z + 1)], fill=(40, 90, 240))
    for x, z in RED_SP:
        d.ellipse([P(x - 1, z - 1), P(x + 1, z + 1)], fill=(240, 50, 50))
    for x, z in FFA_SP:
        d.ellipse([P(x - 1, z - 1), P(x + 1, z + 1)], fill=(250, 250, 250))
    for x, z, r in HILLS:
        d.ellipse([P(x - r, z - r), P(x + r, z + r)], outline=(255, 230, 0), width=3)
    for i_, (x, z) in enumerate(FLAGS):
        d.rectangle([P(x - 1.2, z - 1.2), P(x + 1.2, z + 1.2)], fill=(40, 90, 240) if i_ == 0 else (240, 50, 50), outline=(255, 255, 255))
    for a, b in vis:
        d.line([P(*a), P(*b)], fill=(255, 0, 255), width=1)
    for gx in range(int(t.x0 // 10 * 10), int(t.x1), 10):
        d.text(P(gx, t.z0 + 1), str(gx), fill=(255, 255, 255))
    for gz in range(int(t.z0 // 10 * 10), int(t.z1), 10):
        d.text(P(t.x0 + 1, gz), str(gz), fill=(255, 255, 255))
    img.save(OUT_PNG)
if os.environ.get("BOCAGE_PROBE"):
    for p in [(-80, -55), (-50, -55), (-25, -55), (-8, -55), (10, -60), (30, -70), (-50, -20), (-30, -30)]:
        print("probe", p, round(float(FB[cell_of(*p)]), 1), round(float(FR[cell_of(*p)]), 1))
if os.environ.get("BOCAGE_HILLS"):
    from scipy import ndimage
    AB = np.mean([field([p]) for p in BLUE_SP], axis=0); AR = np.mean([field([p]) for p in RED_SP], axis=0)
    clear = ndimage.distance_transform_edt(~blk) * CELL
    diff = np.abs(AB - AR) / np.maximum(AB, AR)
    ok = (clear >= float(os.environ.get("BOCAGE_CLR", "4.5"))) & np.isfinite(diff) & (GZ < -15)
    idx = np.argsort(np.where(ok, diff, 9).ravel())[:400]
    seen = []
    for k in idx:
        i, j = divmod(int(k), NX)
        x, z = gxs[j], gzs[i]
        if any(math.hypot(x - a, z - b) < 8 for a, b in seen):
            continue
        seen.append((x, z))
        print(f"hill candidate ({x:.1f}, {z:.1f}) diff {diff[i, j] * 100:.1f}% walk {AB[i, j]:.0f}/{AR[i, j]:.0f} clear {clear[i, j]:.1f}")
        if len(seen) > 12:
            break
if os.environ.get("BOCAGE_SMOKE"):
    for p in [(-62.0, 4.7), (-40.0, 6.5), (-86.0, -6.0), (0.0, 0.0)]:
        print("ground", p, round(gy(*p), 2))
low = sorted((4.3 - sink(nm, x, z, yaw) + min(0.4, max(0.0, sink(nm, x, z, yaw) - 0.5)), round(x, 1), round(z, 1), nm) for (nm, x, z, yaw) in HEDGE_PIECES)
print("lowest hedge tops over their centre ground:", [(round(v[0], 2),) + v[1:] for v in low[:5]])
if os.environ.get("BOCAGE_PADS"):
    for (name, x, z, yaw, pal, plan, seed) in PLACED:
        hs = [gy(px, pz) for r in plan.footprint for px in np.linspace(r[0], r[2], 8) for pz in np.linspace(r[1], r[3], 8)]
        print(f"pad {name} ({x},{z}) ground {min(hs):.2f}..{max(hs):.2f}")
