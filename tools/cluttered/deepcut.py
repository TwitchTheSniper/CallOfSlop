#!/usr/bin/env python3
"""Generator for Maps/Cluttered/11-DeepcutMine.json (large, 16 players): a working mine on a dusty afternoon.
Usage: python3 tools/cluttered/deepcut.py <repo root> [preview.png]

The map is NOT mirrored. The west half is low and open (the workers' camp, the processing plant, the conveyor yard),
the east half climbs a terraced massif (bench 1 at 4.5 m, bench 2 at 9 m, the top at 13.5 m with the headframe over
the shaft), and an open pit sits between them with two spoil dumps either side. Tunnels pop in and out of the
hillsides: the North Drift under the north dump (with a side adit into the conveyor yard), the South Adit from the pit
floor under the south dump, and the Main Haulage from bench 1 into the shaft station (a junction room under the
headframe) with branches out to bench 2 north and bench 1 south, and the cage running up the shaft to the top.

Tunnels are cut-and-covered: a terrain `ramp` in "cut" mode carves the trench (floor and rock walls) and a box roof
per stretch closes it at the hillside's own height, so the surface carries on over the top. The generator walks the
map on a 0.5 m grid with the tunnels as a second layer (slope rules, boxes, roofs) to place the hills on equal walks
for both teams, and checks the spawn-to-spawn sight lines over the terrain and the tall boxes.
"""
import heapq, json, math, os, random, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import townkit as tk
import terrain as tr
from townkit import yaw_facing, yaw_along

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/11-DeepcutMine.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
R = tk.R
col = tk.col

# ================================================================ outline
CTRL = [(-88, -4), (-82, -30), (-62, -50), (-32, -60), (0, -58), (30, -62), (60, -54), (82, -34), (90, -6), (86, 22),
        (70, 46), (42, 58), (10, 56), (-20, 60), (-50, 56), (-74, 40), (-86, 18)]
NOISE = {"amplitude": 1.5, "scale": 18, "seed": 11, "octaves": 2}
t = tk.Town(REPO, CTRL, NOISE, pack_props=[os.path.join(REPO, "Maps/Cluttered/Props")])
rng = random.Random(411)
HALF_X = max(abs(min(p[0] for p in t.outline)), abs(max(p[0] for p in t.outline)))
HALF_Z = max(abs(min(p[1] for p in t.outline)), abs(max(p[1] for p in t.outline)))

# ================================================================ levels and terrain
L0, PIT, B1, B2, TOP = 0.0, -4.0, 4.5, 9.0, 13.5
NDUMP, SDUMP = 8.5, 8.0
TW, TEDGE = 4.4, 0.5          # tunnel floor width, wall blend
CEIL = 3.2                    # tunnel headroom (floor to the underside of the roof)

FEATURES = [
    {"type": "noise", "amplitude": 0.45, "scale": 22, "seed": 41},
    # the east massif: three benches with rock faces
    {"type": "plateau", "x": 62, "z": 0, "w": 88, "d": 106, "height": B1, "edge": 1.0, "mode": "fill"},
    {"type": "plateau", "x": 70, "z": 0, "w": 60, "d": 70, "height": B2, "edge": 1.0, "mode": "fill"},
    {"type": "plateau", "x": 78, "z": 0, "w": 40, "d": 44, "height": TOP, "edge": 1.0, "mode": "fill"},
    # the spoil dumps north and south of the pit (flat tops, steep tipped faces)
    {"type": "plateau", "x": -14, "z": -46, "w": 62, "d": 30, "height": NDUMP, "edge": 1.4, "mode": "fill"},
    {"type": "plateau", "x": -12, "z": 48, "w": 56, "d": 24, "height": SDUMP, "edge": 1.4, "mode": "fill"},
    # the open pit
    {"type": "plateau", "x": -4, "z": 11, "w": 30, "d": 34, "height": PIT, "edge": 1.0, "mode": "cut", "material": "sand"},
]
# a low safety berm along the top's west rim (the haul road cuts through it)
FEATURES.append({"type": "ridge", "points": [[59.4, -21.5], [59.4, 21.5]], "width": 2.6, "height": 1.7, "falloff": "dome"})
# level pads for the camps and the plant (the low ground has a little noise)
PADS = [(-68, 0, 36, 46, 4), (-38, -4, 30, 30, 4), (-14, -19, 44, 14, 2), (-30, 24, 24, 10, 3)]
for (px, pz, pw, pd, pe) in PADS:
    FEATURES.append({"type": "flatten", "x": px, "z": pz, "w": pw, "d": pd, "height": L0, "edge": pe})
# haul roads (dirt): ramps between the levels, ~17 degrees
ROADS = [
    ("pit west", [[-31, 18, L0], [-17.5, 18, PIT], [-14, 18, PIT]], 5.0),
    ("pit south", [[-10, 25, PIT], [-10, 26.5, PIT], [-10, 36.2, L0]], 4.6),
    ("pit north-east", [[6, -2, PIT], [6, -4, PIT], [11.5, -12.5, L0]], 4.6),
    ("bench 1 north", [[24, -16, B1], [19.5, -16, B1], [14, -26, L0]], 4.6),
    ("bench 1 south", [[25, 24, B1], [19.5, 24, B1], [14.5, 14.5, L0]], 4.6),
    ("north dump west", [[-46, -27, L0], [-23, -27, NDUMP - 1.2], [-19, -33.5, NDUMP]], 4.4),
    ("north dump east", [[32, -26, B1], [21, -26, NDUMP - 0.6], [15, -33.5, NDUMP]], 4.4),
    ("pit east", [[24, -6, B1], [19.5, -6, B1], [6.5, 15.5, PIT], [5, 17, PIT]], 4.6),   # the main haul road carries on down into the pit
    ("south dump east", [[32, 32.5, B1], [21, 32.5, SDUMP - 0.6], [14.6, 39.5, SDUMP]], 4.4),
    ("south dump west", [[-44, 31.5, L0], [-20, 31.5, SDUMP - 1.0], [-16.5, 38.5, SDUMP]], 4.4),
    ("bench 2 north", [[42, -40, B1], [60, -40, B2], [64, -33, B2]], 4.6),
    ("bench 2 south", [[44, 40.5, B1], [62, 40.5, B2], [66, 33, B2]], 4.6),
    ("main haul road", [[63, -6, TOP], [58.5, -6, TOP], [45, -6, B2], [42, -6, B2], [27, -6, B1]], 4.8),
    ("top south", [[44, 27.5, B2], [60, 27.5, TOP], [62, 20, TOP]], 4.4),
]
for name, pts, w in ROADS:
    FEATURES.append({"type": "ramp", "points": pts, "width": w, "edge": 2.0, "material": "dirt"})

# tunnels: (name, centre line [[x, z, floor y], ...] running from portal to portal, kind)
TUNNELS = [
    ("North Drift", [[-49, -40, L0], [-43, -40, L0], [-6, -40, 2.6], [14, -40, B1], [20, -40, B1]]),
    ("North Drift adit", [[-6, -40, 2.6], [-6, -34, 1.4], [-6, -28, L0]]),
    ("South Adit", [[-2, 25, PIT], [-2, 36, PIT], [-2, 50, 0.4], [8, 52.5, 2.6], [21, 52.5, B1]]),
    ("Main Haulage", [[37, 6, B1], [62, 6, B1]]),
    ("Shaft North", [[67, 1, B1], [67, -15, B2], [67, -27, B2]]),
    ("Shaft South", [[68, 11, B1], [71, 30, B1], [71, 39, B1]]),
]
JROOM = (62.0, 0.5, 72.0, 11.5, B1)      # shaft station (x0, z0, x1, z1, floor)
SHAFT = (67.0, 6.0)                      # the shaft and the headframe over it
for name, pts in TUNNELS:
    FEATURES.append({"type": "ramp", "points": pts, "width": TW, "edge": TEDGE, "mode": "cut", "material": "dirt"})
FEATURES.append({"type": "plateau", "x": (JROOM[0] + JROOM[2]) / 2, "z": (JROOM[1] + JROOM[3]) / 2, "w": JROOM[2] - JROOM[0], "d": JROOM[3] - JROOM[1],
                 "height": JROOM[4], "edge": TEDGE, "mode": "cut", "material": "dirt"})

SURFACE = {"grass": "#8a8a5c", "dirt": "#7c6448", "rock": "#7d7266", "sand": "#8f877a", "sandBelow": -3.2, "dirtSlope": 20, "rockSlope": 34, "variation": 0.12}
TERRAIN = {"cell": 0.5, "margin": 14, "surface": SURFACE, "features": FEATURES}

terr = tr.generate(TERRAIN, HALF_X, HALF_Z)                      # the game's ground (tunnels cut)
surf_spec = dict(TERRAIN, features=[f for f in FEATURES if f.get("mode") != "cut" or f["type"] == "plateau" and f["height"] == PIT])
uncut = tr.generate(surf_spec, HALF_X, HALF_Z)                   # the hillsides as they'd be without the tunnels
ground = lambda x, z: float(terr.sample(x, z))
hill = lambda x, z: float(uncut.sample(x, z))


def max_over(fn, x0, z0, x1, z1, step=0.5):
    return max(fn(x0 + (x1 - x0) * i / max(1, int((x1 - x0) / step)), z0 + (z1 - z0) * j / max(1, int((z1 - z0) / step)))
               for i in range(max(1, int((x1 - x0) / step)) + 1) for j in range(max(1, int((z1 - z0) / step)) + 1))


def min_over(fn, x0, z0, x1, z1, step=0.5):
    return min(fn(x0 + (x1 - x0) * i / max(1, int((x1 - x0) / step)), z0 + (z1 - z0) * j / max(1, int((z1 - z0) / step)))
               for i in range(max(1, int((x1 - x0) / step)) + 1) for j in range(max(1, int((z1 - z0) / step)) + 1))


if __name__ == "__main__" and len(sys.argv) > 3 and sys.argv[3] == "terrain":
    from PIL import Image
    c = terr.colors()
    h = terr.h
    sh = np.clip(0.55 + (h - h.min()) / (h.max() - h.min()) * 0.6, 0, 1.2)
    img = (np.clip(c * sh[..., None], 0, 1) * 255).astype(np.uint8)
    im = Image.fromarray(img).resize((terr.nx * 2, terr.nz * 2))
    from PIL import ImageDraw
    d = ImageDraw.Draw(im)
    P = lambda x, z: ((x - terr.ox) / terr.cell * 2, (z - terr.oz) / terr.cell * 2)
    d.line([P(*p) for p in t.outline[::4]] + [P(*t.outline[0])], fill=(255, 255, 255), width=2)
    for name, pts in TUNNELS:
        d.line([P(p[0], p[1]) for p in pts], fill=(255, 40, 40), width=3)
    for gx in range(-80, 91, 10):
        d.text(P(gx, -HALF_Z - 8), str(gx), fill=(255, 255, 0))
    for gz in range(-60, 61, 10):
        d.text(P(-HALF_X - 10, gz), str(gz), fill=(255, 255, 0))
    im.save(OUT_PNG)
    print("area", round(t.area()), "half", HALF_X, HALF_Z, "terrain", terr.nx, terr.nz, terr.min, terr.max)
    sys.exit(0)


# ================================================================ objectives (decided first: everything keeps clear of them)
BLUE_SP = [(-78, -6), (-78, 0), (-78, 6), (-74, -9), (-74, -3), (-74, 3), (-69, -1), (-74, 8), (-66, -9)]
RED_SP = [(73, -6), (76, -10), (80, -9.5), (84, -6), (87, 0), (86, 9), (61.5, -15), (61.5, -19), (62, 17)]   # none over a tunnel roof
FFA_SP = [(-40, -26), (-20, 26), (-16, -22.5), (30, -10), (50, 0), (36, 45)]
HILLS = [(7.0, 1.0, 4.0), (6.0, -19.0, 5.5), (6.0, 41.5, 4.0)]
FLAGS = [(-84.0, 2.0), (86.5, 3.0)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.keepout.append((x, z, 2.6))
for x, z, r in HILLS:
    t.keepout.append((x, z, r + 0.6))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))


# ================================================================ terrain-aware building helpers
EARTH = (0.49, 0.40, 0.30)       # roof boxes over the tunnels: the colour of the spoil and the dirt roads
LAGGING = (0.17, 0.14, 0.11)     # dark timber lagging on tunnel walls and ceilings
TIMBER = (0.43, 0.33, 0.21)
STEEL = (0.45, 0.47, 0.49)
RUST = (0.48, 0.29, 0.18)
CABIN = (0.84, 0.82, 0.74)
CABIN_B = (0.32, 0.46, 0.62)
CABIN_G = (0.42, 0.52, 0.36)
CONCRETE = (0.62, 0.61, 0.58)
SHED = (0.56, 0.58, 0.6)
ROOFC = (0.38, 0.39, 0.4)
GRAVEL = (0.55, 0.52, 0.47)
ORE = (0.30, 0.28, 0.26)
DARK = (0.12, 0.12, 0.13)
ROOFS = []        # (x0, z0, x1, z1, y0, y1): walkable tops over the tunnels (surface layer for the walk analysis)
WALLS = []        # solid boxes that block walking: (x0, z0, x1, z1, y0, y1) in world heights


def blockw(x0, y0, z0, x1, y1, z1, c, roof=False):
    """A Block at absolute heights ("anchor": "world"). Roofs over tunnels are surfaces, everything else blocks."""
    x0, x1 = min(x0, x1), max(x0, x1); z0, z1 = min(z0, z1), max(z0, z1)
    t.ops.append({"op": "Block", "x0": R(x0), "y0": R(y0), "z0": R(z0), "x1": R(x1), "y1": R(y1), "z1": R(z1), "color": col(c), "anchor": "world"})
    t.boxes.append((x0, z0, x1, z1, 9.0, 9.5, c) if roof else (x0, z0, x1, z1, 0.0, 3.0, c))
    if roof:
        ROOFS.append((x0, z0, x1, z1, y0, y1))
        return
    WALLS.append((x0, z0, x1, z1, y0, y1))
    g = min_over(ground, x0, z0, x1, z1, 1.0)
    r = (x0, z0, x1, z1)
    if y0 < g + 1.8:
        t.mark(t.occ, r)
    if y0 < g + 1.7 and y1 > g + 0.55:
        t.mark(t.walk, r)
    if y0 < g + 1.6 and y1 > g + 1.6:
        t.mark(t.tall, r)


def block(x0, y0, z0, x1, y1, z1, c):
    """A Block standing on a level pad (heights above the pad, which is at `pad_y`)."""
    blockw(x0, pad_y + y0, z0, x1, pad_y + y1, z1, c)


pad_y = L0


def meshw(kind, x, z, c, y, **kw):
    """A cosmetic Mesh at an absolute height."""
    t.mesh(kind, x, z, c, y=round(float(y), 2), anchor="world", **kw)


def propw(name, x, z, yaw, y, tag=None):
    """A prop at an absolute height (hung from a ceiling, on a box top): no occupancy check."""
    op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
    if yaw:
        op["yaw"] = R(yaw % 360)
    op["y"] = R(y)
    op["anchor"] = "world"
    t.ops.append(op); t.props.append((name, x, z, yaw % 360, 9.0, tag))
    t.notes[tag or "other"] = t.notes.get(tag or "other", 0) + 1


FAILED = []


def P(name, x, z, yaw=0.0, tag=None, **kw):
    """A prop through the town's placement rules. One standing over a tunnel roof is put on the roof ("anchor": "world"),
    because the game would stand it on the lowest ground under it: the tunnel floor."""
    ok = t.prop(name, x, z, yaw, tag=tag, **kw)
    if not ok:
        FAILED.append((name, x, z))
        return ok
    r = t.foot(name, x, z, yaw % 360)
    over = [q for q in ROOFS if q[0] < r[2] and r[0] < q[2] and q[1] < r[3] and r[1] < q[3]]
    if over:
        op = t.ops[-1]
        under = [q[5] for q in over if q[0] <= x <= q[2] and q[1] <= z <= q[3]]
        top = max(under) if under else min(q[5] for q in over)
        op["y"] = R(top + float(kw.get("y") or 0.0))
        op["anchor"] = "world"
        ROOF_PROPS.append(name)
    return ok


ROOF_PROPS = []


def wp(name, room, side, along, gap=0.22, tag=None):
    """A prop with its back against one inner wall of a room (side N/S/W/E), facing into the room, at `along` (world x
    for N/S walls, world z for W/E walls)."""
    ix0, iz0, ix1, iz1 = room.rect
    d = t.P[name]["d"]; cz_ = t.P[name]["cz"]
    if side == "N":
        return P(name, along, iz0 + d / 2 + gap - cz_, 0, tag=tag)
    if side == "S":
        return P(name, along, iz1 - d / 2 - gap + cz_, 180, tag=tag)
    if side == "W":
        return P(name, ix0 + d / 2 + gap - cz_, along, 90, tag=tag)
    return P(name, ix1 - d / 2 - gap + cz_, along, 270, tag=tag)


def table_set(x, z, table, chair, tag=None, along_x=True):
    """A table with chairs on its two long sides, square to it."""
    P(table, x, z, 0 if along_x else 90, tag=tag)
    tw, td = t.P[table]["w"], t.P[table]["d"]
    cd = t.P[chair]["d"]
    off = td / 2 + cd / 2 + 0.12
    for k in (-1, 1):
        for s_ in ((-tw / 4, tw / 4) if tw > 1.5 else (0.0,)):
            if along_x:
                P(chair, x + s_, z + k * off, 180 if k > 0 else 0, tag=tag)
            else:
                P(chair, x + k * off, z + s_, 270 if k > 0 else 90, tag=tag)


# ================================================================ tunnels: roofs, linings, timber, lamps, portals
def polyline_samples(pts, step=0.5):
    out = []
    for k in range(len(pts) - 1):
        (ax, az, ay), (bx, bz, by) = pts[k], pts[k + 1]
        L = math.hypot(bx - ax, bz - az); n = max(1, int(round(L / step)))
        dx, dz = (bx - ax) / L, (bz - az) / L
        for i in range(n + (1 if k == len(pts) - 2 else 0)):
            f = i / n
            out.append((ax + (bx - ax) * f, az + (bz - az) * f, ay + (by - ay) * f, dx, dz, k))
    return out


def in_jroom(x, z, pad=0.6):
    return JROOM[0] - pad <= x <= JROOM[2] + pad and JROOM[1] - pad <= z <= JROOM[3] + pad


HW = TW / 2 + TEDGE + 0.45      # roof half-width: over the floor, the wall blend and a little into the rock
TUNNEL_INFO = []                # per tunnel: (name, samples, covered flags, chunks)
WAYPOINTS = []
INCLUDE_TOPS = []               # foliage include zones (onTop) over the roofs
LAMPS = 0
for name, pts in TUNNELS:
    S = polyline_samples(pts)
    cov = [hill(x, z) >= y + CEIL + 0.45 and not in_jroom(x, z) for (x, z, y, dx, dz, k) in S]
    # chunks: runs of covered samples, cut at bends, every 2 m on slopes, and where the hill over it changes height
    chunks = []
    i = 0
    while i < len(S):
        if not cov[i]:
            i += 1
            continue
        j = i
        top_i = max_over(hill, S[i][0] - HW, S[i][1] - HW, S[i][0] + HW, S[i][1] + HW, 1.0)
        while j + 1 < len(S) and cov[j + 1] and S[j + 1][5] == S[i][5]:
            nx_, nz_, ny_ = S[j + 1][0], S[j + 1][1], S[j + 1][2]
            L = math.hypot(nx_ - S[i][0], nz_ - S[i][1])
            sloped = abs(ny_ - S[i][2]) > 0.01
            top_j = max_over(hill, nx_ - HW, nz_ - HW, nx_ + HW, nz_ + HW, 1.0)
            if L > (2.0 if sloped else 14.0) or abs(top_j - top_i) > 0.25:
                break
            j += 1
        chunks.append((i, j))
        i = j + 1
    TUNNEL_INFO.append((name, S, cov, chunks))
    for (i, j) in chunks:
        a, b = S[i], S[j]
        axis_z = abs(a[4]) < 1e-6          # dz = 0: running along x
        axis_x = abs(a[3]) < 1e-6          # dx = 0: running along z
        xs = [S[k][0] for k in range(i, j + 1)]; zs = [S[k][1] for k in range(i, j + 1)]
        ext = 0.26                          # half a sample each end, so chunks meet
        if axis_z:
            x0, x1 = min(xs) - ext, max(xs) + ext; z0, z1 = a[1] - HW, a[1] + HW
        elif axis_x:
            x0, x1 = a[0] - HW, a[0] + HW; z0, z1 = min(zs) - ext, max(zs) + ext
        else:
            x0, x1 = min(xs) - HW, max(xs) + HW; z0, z1 = min(zs) - HW, max(zs) + HW
        floor_max = max(S[k][2] for k in range(i, j + 1)); floor_min = min(S[k][2] for k in range(i, j + 1))
        y0 = floor_max + CEIL
        y1 = max(max_over(hill, x0, z0, x1, z1, 0.5) + 0.02, y0 + 0.4)
        blockw(x0, y0, z0, x1, y1, z1, EARTH, roof=True)
        INCLUDE_TOPS.append((x0, z0, x1, z1))
        # dark lagging: the ceiling and both walls (cosmetic meshes)
        L = math.hypot(b[0] - a[0], b[1] - a[1]) + 0.5
        cx, cz = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        yaw = yaw_along(a[3], a[4])
        meshw("box", cx, cz, LAGGING, y0 - 0.06, width=round(L, 2), height=0.05, depth=TW + 0.2, yaw=yaw, material="Wood")
        nx_, nz_ = -a[4], a[3]
        for side in (1, -1):
            meshw("box", cx + nx_ * side * (TW / 2 - 0.02), cz + nz_ * side * (TW / 2 - 0.02), LAGGING, floor_min - 0.3,
                  width=round(L, 2), height=round(y0 - floor_min + 0.28, 2), depth=0.06, yaw=yaw, material="Wood")
    # timber sets every 2.5 m and lamps every ~9 m under the roofs, waypoints every 3 m all along
    last_set = -9.0; last_lamp = -4.0; last_wp = -9.0; s_acc = 0.0
    for k, (x, z, y, dx, dz, seg) in enumerate(S):
        if k:
            s_acc += math.hypot(x - S[k - 1][0], z - S[k - 1][1])
        if s_acc - last_wp >= 3.0 and not in_jroom(x, z, -0.5):
            WAYPOINTS.append((x, z)); last_wp = s_acc
        if not cov[k]:
            continue
        ceil_here = max(S[q][2] for q in range(max(0, k - 4), min(len(S), k + 5))) + CEIL
        if s_acc - last_set >= 2.5:
            t.prop("Timber Set", x, z, yaw_facing(dx, dz), check=False, tag="tunnel"); last_set = s_acc
        if s_acc - last_lamp >= 9.0:
            propw("Mine Lamp", x + dz * 0.0, z, rng.uniform(0, 360), ceil_here - 0.66, tag="tunnel"); last_lamp = s_acc; LAMPS += 1
    # portal frames at both ends of every covered run
    for k in range(len(S)):
        if cov[k] and (k == 0 or not cov[k - 1] or k == len(S) - 1 or not cov[k + 1]):
            if not (k == 0 or not cov[k - 1]) and not (k == len(S) - 1 or not cov[k + 1]):
                continue
            x, z, y, dx, dz, seg = S[k]
            if in_jroom(x, z, 1.5):
                continue
            yaw = yaw_along(dx, dz)
            nx_, nz_ = -dz, dx
            ceil_y = y + CEIL
            meshw("box", x, z, TIMBER, ceil_y - 0.5, width=0.5, height=0.5, depth=TW + 0.8, yaw=yaw, material="Wood")
            for side in (1, -1):
                meshw("box", x + nx_ * side * (TW / 2 - 0.15), z + nz_ * side * (TW / 2 - 0.15), TIMBER, y - 0.2, width=0.4, height=CEIL - 0.3, depth=0.4, yaw=yaw, material="Wood")

# the shaft station (junction room) under the headframe: a roof in four pieces round the shaft
jx0, jz0, jx1, jz1, jy = JROOM
JCEIL = jy + 3.8
sx, sz = SHAFT
HOLE = 1.7
for (x0, z0, x1, z1) in ((jx0 - 0.4, jz0 - 0.4, sx - HOLE, jz1 + 0.4), (sx + HOLE, jz0 - 0.4, jx1 + 0.4, jz1 + 0.4),
                         (sx - HOLE, jz0 - 0.4, sx + HOLE, sz - HOLE), (sx - HOLE, sz + HOLE, sx + HOLE, jz1 + 0.4)):
    blockw(x0, JCEIL, z0, x1, max_over(hill, x0, z0, x1, z1) + 0.02, z1, EARTH, roof=True)
    INCLUDE_TOPS.append((x0, z0, x1, z1))
meshw("box", (jx0 + jx1) / 2, (jz0 + jz1) / 2, LAGGING, JCEIL - 0.06, width=jx1 - jx0, height=0.05, depth=jz1 - jz0)
for (a, b, n) in (((jx0, jz0), (jx1, jz0), (0, 1)), ((jx0, jz1), (jx1, jz1), (0, -1)), ((jx0, jz0), (jx0, jz1), (1, 0)), ((jx1, jz0), (jx1, jz1), (-1, 0))):
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    meshw("box", (a[0] + b[0]) / 2 + n[0] * 0.03, (a[1] + b[1]) / 2 + n[1] * 0.03, LAGGING, jy - 0.3, width=round(L, 2), height=round(JCEIL - jy + 0.28, 2), depth=0.06,
          yaw=yaw_along(b[0] - a[0], b[1] - a[1]), material="Wood")
for (wx, wz) in ((63.0, 2.0), (71.0, 2.0), (63.0, 10.0), (71.0, 10.0), (64.0, 6.0), (70.0, 6.0), (67.0, 2.0), (67.0, 10.0)):
    WAYPOINTS.append((wx, wz))
for (lx, lz) in ((64.0, 3.0), (70.0, 9.0), (64.0, 9.5), (70.5, 2.5)):
    propw("Mine Lamp", lx, lz, rng.uniform(0, 360), JCEIL - 0.66, tag="tunnel"); LAMPS += 1


# ================================================================ walk-in portable cabins and sheds
def wall_x(x0, x1, z0, z1, h, c, holes=()):
    pos = x0
    for (cx, w, bot, top) in sorted(holes):
        a, b = cx - w / 2, cx + w / 2
        if a - pos > 0.05:
            block(pos, 0, z0, a, h, z1, c)
        if bot > 0.05:
            block(a, 0, z0, b, bot, z1, c)
        if top < h - 0.05:
            block(a, top, z0, b, h, z1, c)
        pos = b
    if x1 - pos > 0.05:
        block(pos, 0, z0, x1, h, z1, c)


def wall_z(z0, z1, x0, x1, h, c, holes=()):
    pos = z0
    for (cz, w, bot, top) in sorted(holes):
        a, b = cz - w / 2, cz + w / 2
        if a - pos > 0.05:
            block(x0, 0, pos, x1, h, a, c)
        if bot > 0.05:
            block(x0, 0, a, x1, bot, b, c)
        if top < h - 0.05:
            block(x0, top, a, x1, h, b, c)
        pos = b
    if z1 - pos > 0.05:
        block(x0, 0, pos, x1, h, z1, c)


DOOR = lambda c, w=1.3: (c, w, 0.0, 2.2)
WIN = lambda c, w=1.6: (c, w, 1.0, 2.0)
ROOMS = []


def shell(x0, z0, x1, z1, c, holes, h=2.8, roof=ROOFC, wall=0.2, trim=None):
    """A walk-in cabin or shed on the pad: walls with openings, a roof slab, skids and a floor plate."""
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
    wall_x(x0, x1, z0, z0 + wall, h, c, holes.get("N", ()))
    wall_x(x0, x1, z1 - wall, z1, h, c, holes.get("S", ()))
    wall_z(z0 + wall, z1 - wall, x0, x0 + wall, h, c, holes.get("W", ()))
    wall_z(z0 + wall, z1 - wall, x1 - wall, x1, h, c, holes.get("E", ()))
    block(x0 - 0.1, h, z0 - 0.1, x1 + 0.1, h + 0.22, z1 + 0.1, roof)
    meshw("box", (x0 + x1) / 2, (z0 + z1) / 2, (0.40, 0.38, 0.36), pad_y, width=x1 - x0 - 2 * wall, height=0.04, depth=z1 - z0 - 2 * wall, material="Tiles")
    if trim:
        for zz in (z0 - 0.03, z1 + 0.03):
            meshw("box", (x0 + x1) / 2, zz, trim, pad_y + 0.1, width=x1 - x0, height=0.25, depth=0.05)
    return tk.Room(t, x0, z0, x1, z1, (0, 1), wall=wall)


def closed_cabin(x0, z0, x1, z1, c, h=2.8, y=0.0):
    """A cabin you can't enter: one block, with a door and windows as dark panels."""
    block(x0, y, z0, x1, y + h, z1, c)
    block(x0 - 0.08, y + h, z0 - 0.08, x1 + 0.08, y + h + 0.18, z1 + 0.08, ROOFC)
    n = max(1, int((x1 - x0) // 3))
    for k in range(n):
        xx = x0 + (x1 - x0) * (k + 0.5) / n
        for zz in (z0 - 0.03, z1 + 0.03):
            meshw("box", xx, zz, DARK, pad_y + y + 1.0, width=1.2 if k else 0.9, height=1.0 if k else 2.0, depth=0.05)


# ---------------------------------------------------------------- Blue: the workers' camp (west, low)
pad_y = L0
r = shell(-75, -17, -64, -12.5, CABIN, {"S": [DOOR(-71.5), WIN(-67.5, 2.2)], "N": [DOOR(-66.0), WIN(-72.5, 2.0)], "W": [WIN(-14.75, 1.4)]}, trim=CABIN_B)
ROOMS.append(("canteen", r))
for (tx, tz) in ((-72.6, -14.8), (-69.0, -14.8)):
    table_set(tx, tz, "Table", "Plastic Chair", tag="canteen")
wp("Glowing Vending Machine", r, "E", -15.9, tag="canteen"); wp("Vending Machine", r, "E", -14.4, tag="canteen")
wp("Kitchen Counter", r, "S", -66.9, tag="canteen"); wp("Fridge", r, "S", -65.4, tag="canteen")
ccz = r.rect[3] - t.P["Kitchen Counter"]["d"] / 2 - 0.1
P("Microwave", -66.7, ccz, 180, y=0.92, check=False, tag="canteen"); P("Kettle", -67.3, ccz, 200, y=0.92, check=False, tag="canteen")
wp("Water Cooler", r, "W", -16.2, tag="canteen"); wp("Pedal Bin", r, "W", -13.4, tag="canteen")
for (tx, tz) in ((-72.6, -14.9), (-69.4, -14.9)):
    P(rng.choice(["Stacked Plates", "Soda Cans", "Desk Phone", "Book Pile"]), tx, tz, rng.uniform(0, 360), y=0.75, check=False, tag="canteen")

r = shell(-75, 9, -66, 13.5, CABIN_G, {"N": [DOOR(-70.0), WIN(-73.2, 1.6)], "S": [WIN(-71.0, 2.4)], "E": [DOOR(11.25)]}, trim=CABIN)
ROOMS.append(("lockers", r))
LW = t.P["Metal Lockers"]["w"] + 0.06
for k in range(int(7.6 // LW)):
    wp("Metal Lockers", r, "S", -74.4 + LW / 2 + k * LW, tag="lockers")
P("Bench", -72.4, 11.2, 0, tag="lockers"); P("Mop and Bucket", -66.6, 9.6, 30, tag="lockers"); P("Duffel Bag", -72.0, 11.0, 70, y=0.45, check=False, tag="lockers")
wp("Laundry Basket", r, "W", 10.0, tag="lockers"); P("Coat Stand", -66.6, 12.6, 0, tag="lockers")

r = shell(-61, -5, -55, -1, CABIN, {"W": [DOOR(-3.0)], "N": [WIN(-58.0, 2.0)], "E": [WIN(-3.0, 1.6)]}, trim=(0.75, 0.55, 0.2))
ROOMS.append(("office", r))
wp("Office Desk", r, "S", -57.4, tag="office"); P("Office Chair", -57.4, -2.9, 180, tag="office")
dz_ = r.rect[3] - t.P["Office Desk"]["d"] / 2 - 0.1
P("Computer Monitor", -57.6, dz_, 0, y=0.76, check=False, tag="office"); P("Desk Phone", -56.7, dz_, 20, y=0.76, check=False, tag="office")
wp("Filing Cabinet", r, "E", -4.2, tag="office"); wp("Whiteboard", r, "N", -58.4, tag="office"); wp("Water Cooler", r, "N", -60.2, tag="office")
wp("Archive Boxes", r, "S", -60.2, tag="office")

closed_cabin(-85, -15, -79, -11, CABIN_B)
closed_cabin(-84, 12, -78, 16, CABIN)
closed_cabin(-63, 13, -57, 17, CABIN_G)
block(-58.5, 0, 2.5, -51.0, 6.5, 11.5, (0.5, 0.45, 0.38))       # core shed: a tall closed store between the camp and the plant
block(-58.7, 6.5, 2.3, -50.8, 6.8, 11.7, ROOFC)
P("Roof Turbine Vent", -55.0, 7.0, 0, y=6.8, check=False, tag="roof")
# camp services: toilets, generator, floodlights, fuel, water, bins by the canteen's back door, picnic tables, trucks
for (x, z, yy) in ((-62.6, 9.5, 270), (-62.6, 8.1, 270), (-62.6, 6.7, 270)):
    P("Portable Toilet", x, z, yy, tag="camp")
P("Running Generator", -78.0, -18.6, 0, tag="camp"); P("Floodlight Mast", -74.0, -20.2, 30, tag="camp")
P("Floodlight Mast", -60.0, 19.5, 210, tag="camp"); P("Diesel Generator", -56.5, 21.0, 0, tag="camp")
P("Plastic Water Tank", -77.0, 17.5, 0, tag="camp")
tk.bins_by_door(t, (-66.0, -17.0), (1, 0), (0, -1), rng, side=1, seed=4111, tag="bins")
for (x, z, yy) in ((-70.0, -6.0, 90), (-70.0, 4.5, 90)):
    P("Park Picnic Table", x, z, yy, tag="camp")
    t.clutter(["Litter", "Soda Cans", "Plastic Bag", "Bottle", "Newspaper"], x - 2.5, z - 2.5, x + 2.5, z + 2.5, 7, int(abs(z) * 10 + 3))
P("Pickup Truck", -60.0, -12.0, 90, tag="vehicles"); P("Pickup Truck", -67.0, 20.5, 0, tag="vehicles")
P("Utility Trailer", -79.0, 21.0, 0, tag="vehicles")
P("Bus Shelter", -63.5, 3.0, 270, tag="camp")
P("Flag on a Pole", -84.5, -2.5, 0, margin=0.4, tag="camp")
P("Pallet Stack", -84.0, -18.5, 0, tag="camp"); P("Gas Cylinders", -82.4, -19.0, 0, tag="camp")

# ---------------------------------------------------------------- the processing plant
r = shell(-48, -15, -30, 0, SHED, {"W": [DOOR(-7.5, 4.0)], "E": [(-10.0, 4.5, 0.0, 4.2), (-3.0, 2.2, 0.0, 2.6)], "N": [DOOR(-39.0, 3.0)], "S": [DOOR(-36.0, 3.0), WIN(-44.0, 4.0)]},
          h=7.0, wall=0.35, roof=(0.42, 0.40, 0.38))
ROOMS.append(("plant", r))
for k in range(3):
    P("Conveyor Rollers", -45.0 + k * 3.2, -12.0, 0, tag="plant")
P("Jaw Crusher", -34.0, -10.8, 0, tag="plant")
wp("Metal Shelving", r, "W", -2.0, tag="plant"); wp("Metal Shelving", r, "W", -4.4, tag="plant")
P("Forklift", -34.0, -4.6, 200, tag="plant"); P("Pallet Stack", -31.2, -1.2, 0, tag="plant"); P("Pallet", -33.5, -2.6, 10, tag="plant")
P("Table", -43.0, -1.1, 0, tag="plant"); P("Toolbox", -43.2, -1.1, 20, y=0.75, check=False, tag="plant")
P("Oil Drum", -47.0, -14.0, 0, tag="plant"); P("Rusty Drum", -46.2, -14.1, 0, tag="plant"); P("Gas Cylinders", -31.0, -13.8, 0, tag="plant")
P("Exhaust Fan", -40.0, -8.0, 0, y=7.2, check=False, tag="roof"); P("Roof Turbine Vent", -44.0, -4.0, 0, y=7.2, check=False, tag="roof")
P("Roof Turbine Vent", -34.0, -12.0, 0, y=7.2, check=False, tag="roof"); P("Rooftop Pipe Rack", -38.0, -3.0, 90, y=7.2, check=False, tag="roof")
# the ore hopper (walk under) on the rail line from the North Drift, the crusher and conveyors by the pit, stockpiles
P("Ore Hopper", -26.0, -9.0, 0, tag="plant")
P("Inclined Conveyor", -12.5, 4.5, 180, check=False, tag="plant")   # from the pit floor up over the west rim
P("Jaw Crusher", -22.6, 4.5, 90, tag="plant")
P("Conveyor Rollers", -26.0, -3.0, 90, tag="plant")
STOCKPILES = [(-50.0, 6.5, 3.6, 3.0, GRAVEL), (-42.5, 7.5, 3.0, 2.6, ORE), (-35.0, 13.5, 3.2, 2.8, (0.62, 0.55, 0.45))]
for (x, z, rr, hh, c) in STOCKPILES:
    meshw("cone", x, z, c, ground(x, z) - 0.2, width=rr * 2, height=hh + 0.2, depth=rr * 2, segments=10, material="Dirt")
    blockw(x - rr * 0.45, ground(x, z) - 0.2, z - rr * 0.45, x + rr * 0.45, ground(x, z) + hh * 0.55, z + rr * 0.45, c)
    t.clutter(["Rubble", "Rubble", "Loose Bricks", "Cinder Blocks"], x - rr - 1.5, z - rr - 1.5, x + rr + 1.5, z + rr + 1.5, 12, int(x * 7 + z) % 997)
P("Floodlight Mast", -52.0, -17.5, 45, tag="plant"); P("Running Generator", -49.0, -19.6, 0, tag="plant")
P("Forklift", -52.0, 0.5, 90, tag="plant")

# ---------------------------------------------------------------- rails from the North Drift down to the hopper
RAIL = [(-49.0, -40.0), (-49.0, -31.0), (-46.0, -24.0), (-33.0, -18.0), (-26.0, -14.5), (-26.0, -9.0)]
cart_spots = []
for k in range(len(RAIL) - 1):
    (ax, az), (bx, bz) = RAIL[k], RAIL[k + 1]
    L = math.hypot(bx - ax, bz - az); n = max(1, int(round(L / 3.0)))
    for i in range(n):
        f = (i + 0.5) / n
        x, z = ax + (bx - ax) * f, az + (bz - az) * f
        t.prop("Rail Track", x, z, yaw_along(bx - ax, bz - az), check=False, tag="rail")
        if k in (2, 3) and i % 3 == 1:
            cart_spots.append((x, z, yaw_along(bx - ax, bz - az)))
for (x, z, yy) in cart_spots[:4]:
    P("Mine Cart", x, z, yy, tag="rail")
t.path([(x, z) for x, z in RAIL], 2.2)
# rails inside the North Drift too
for (name, S, cov, chunks) in TUNNEL_INFO:
    if name != "North Drift":
        continue
    last = -9.0; s_acc = 0.0
    for k, (x, z, y, dx, dz, seg) in enumerate(S):
        if k:
            s_acc += math.hypot(x - S[k - 1][0], z - S[k - 1][1])
        if s_acc - last >= 3.0 and x < 12:
            t.prop("Rail Track", x, z, yaw_along(dx, dz), check=False, tag="rail"); last = s_acc

# ---------------------------------------------------------------- the conveyor yard (between the north dump and the pit)
P("Haul Truck", -9.0, -16.5, 90, tag="vehicles")
P("Timber Stack", -9.5, -25.5, 0, tag="yard"); P("Timber Stack", -2.5, -25.5, 0, tag="yard"); P("Timber Stack", -30.0, -24.5, 90, tag="yard")
P("Cable Spool", -28.0, -14.0, 0, tag="yard"); P("Pipe Stack", -28.5, -19.5, 90, tag="yard"); P("Concrete Pipe", -11.5, -13.5, 0, tag="yard")
P("Floodlight Mast", 7.0, -26.5, 220, tag="yard"); P("Running Generator", 9.5, -25.0, 0, tag="yard")
P("Explosive Barrel", 7.0, -11.0, 0, tag="explosives")
container = (-13.0, -27.8, -6.9, -25.4)   # an open-fronted workshop container against the dump
block(container[0], 0, container[1], container[2], 2.6, container[1] + 0.12, RUST)
block(container[0], 0, container[1], container[0] + 0.12, 2.6, container[3], RUST)
block(container[2] - 0.12, 0, container[1], container[2], 2.6, container[3], RUST)
block(container[0], 2.6, container[1], container[2], 2.75, container[3], RUST)
P("Table", -10.0, -27.0, 0, tag="yard"); P("Toolbox", -10.2, -27.0, 0, y=0.75, check=False, tag="yard"); P("Gas Cylinders", -12.2, -26.9, 0, tag="yard")
P("Inclined Conveyor", -1.5, -9.2, 180, check=False, tag="plant")

# ---------------------------------------------------------------- the pit
P("Excavator", -6.0, 13.5, 200, tag="pit"); P("Haul Truck", -11.0, 22.5, 0, tag="pit")
P("Rubble Pile", -15.5, 9.0, 0, tag="pit"); P("Rubble Pile", 7.5, 9.0, 40, tag="pit"); P("Rubble Pile", -9.0, -3.5, 120, tag="pit")
P("Explosive Barrel", 8.4, 25.0, 0, tag="explosives")
P("Piston Pump", -16.6, 24.5, 90, tag="pit"); P("Floodlight Mast", 9.6, 17.0, 250, tag="pit"); P("Floodlight Mast", -17.0, -4.0, 135, tag="pit")
P("Water Barrier", 2.0, 8.0, 0, tag="pit"); P("Water Barrier", -10.0, 8.0, 90, tag="pit"); P("Concrete Block", 3.5, 15.0, 0, tag="pit")
P("Tyre Stack", -15.8, 18.0, 0, tag="pit"); P("Concrete Block", -9.0, 6.5, 0, tag="pit")

# ---------------------------------------------------------------- the south yard: fuel depot (bunded tanks)
pad_y = L0
P("Fuel Tank", -33.0, 22.5, 0, tag="fuel"); P("Fuel Tank", -33.0, 26.4, 0, tag="fuel")
for (x0, z0, x1, z1) in ((-37.2, 20.6, -28.8, 20.9), (-37.2, 28.3, -28.8, 28.6), (-37.5, 20.6, -37.2, 28.6)):
    block(x0, 0, z0, x1, 0.8, z1, CONCRETE)
P("Utility Pedestal", -27.6, 21.5, 90, tag="fuel"); P("Gas Valve", -27.6, 23.4, 90, tag="fuel")
P("Pickup Truck", -44.0, 25.0, 0, tag="vehicles"); P("Oil Drum", -40.0, 22.0, 0, tag="fuel"); P("Rusty Drum", -40.6, 23.2, 0, tag="fuel")

# ---------------------------------------------------------------- the spoil dumps: rock piles, a dozer track, timber, a lookout
for (x, z) in ((-30.0, -44.0), (-12.0, -50.0), (4.0, -45.0), (-38.0, -52.0)):
    P("Rubble Pile", x, z, rng.uniform(0, 360), tag="dump")
P("Excavator", -24.0, -38.0, 200, tag="dump"); P("Floodlight Mast", 10.0, -34.5, 160, tag="dump")
for (x, z) in ((-28.0, 46.0), (-8.0, 54.0), (-14.0, 41.0)):
    P("Rubble Pile", x, z, rng.uniform(0, 360), tag="dump")
P("Haul Truck", -18.0, 50.0, 0, tag="dump"); P("Floodlight Mast", -7.5, 39.0, 20, tag="dump")

# ---------------------------------------------------------------- bench 1: safety berms of concrete blocks along the rim, plant, the explosives magazine
for (z0, z1) in ((-50.0, -30.5), (-12.0, -10.0), (1.5, 18.0), (36.0, 50.0)):
    z = z0
    while z < z1:
        P("Concrete Block", 19.4, z, 90, tag="berm")
        z += 2.6 if rng.random() < 0.75 else 4.4
P("Haul Truck", 30.0, 14.0, 90, tag="vehicles")
P("Timber Stack", 30.0, 20.0, 0, tag="bench"); P("Timber Stack", 24.5, 22.0, 90, tag="bench"); P("Timber Stack", 34.0, -32.0, 0, tag="bench")
P("Floodlight Mast", 22.0, 2.0, 270, tag="bench"); P("Running Generator", 22.5, 4.5, 0, tag="bench")
P("Cable Spool", 26.0, 24.0, 0, tag="bench"); P("Tyre Stack", 35.0, 24.0, 0, tag="bench"); P("Utility Trailer", 28.5, -20.0, 90, tag="bench")
P("Water Barrier", 33.0, 1.5, 90, tag="bench")
for (x, z, rr, hh, c) in ((28.0, 40.0, 2.6, 2.2, ORE), (34.0, -45.0, 2.4, 2.0, GRAVEL)):
    gy = ground(x, z)
    meshw("cone", x, z, c, gy - 0.2, width=rr * 2, height=hh + 0.2, depth=rr * 2, segments=10, material="Dirt")
    blockw(x - rr * 0.45, gy - 0.2, z - rr * 0.45, x + rr * 0.45, gy + hh * 0.55, z + rr * 0.45, c)
# cover along Red's routes down the benches (the high side has the height, the low side the clutter, but nobody walks open ground)
for (name, x, z, yy) in (("Concrete Block", 32.0, -13.5, 0), ("Timber Stack", 34.0, -2.5, 90), ("Cable Spool", 29.0, 5.5, 0), ("Concrete Block", 36.0, 14.0, 90),
                         ("Water Barrier", 23.0, 10.0, 0), ("Pallet Stack", 30.0, -29.0, 0), ("Concrete Block", 25.0, -31.0, 0), ("Concrete Block", 34.0, 37.0, 0),
                         ("Timber Stack", 27.5, 30.0, 0), ("Sandbag Wall", 36.0, -20.0, 0), ("Sandbag Wall", 26.0, 16.0, 90), ("Concrete Block", 38.0, 45.0, 90)):
    P(name, x, z, yy, tag="cover")
pad_y = B2
for (name, x, z, yy) in (("Sandbag Wall", 47.0, -12.0, 0), ("Concrete Block", 51.0, -0.5, 0), ("Timber Stack", 44.5, 10.5, 90), ("Cable Spool", 55.0, -14.0, 0),
                         ("Concrete Block", 49.0, 20.5, 90), ("Sandbag Wall", 56.0, 13.5, 90)):
    P(name, x, z, yy, tag="cover")
pad_y = B1
# explosives magazine: a squat concrete bunker behind a blast wall, crates inside and on the apron (well away from spawns)
pad_y = B1
MAG = (24.0, -54.0, 30.0, -49.0)
mx0, mz0, mx1, mz1 = MAG
wall_x(mx0, mx1, mz0, mz0 + 0.4, 2.6, CONCRETE)
wall_z(mz0 + 0.4, mz1, mx0, mx0 + 0.4, 2.6, CONCRETE)
wall_z(mz0 + 0.4, mz1, mx1 - 0.4, mx1, 2.6, CONCRETE)
wall_x(mx0, mx1, mz1 - 0.4, mz1, 2.6, CONCRETE, [(27.0, 1.6, 0.0, 2.1)])
block(mx0 - 0.15, 2.6, mz0 - 0.15, mx1 + 0.15, 2.95, mz1 + 0.15, (0.5, 0.5, 0.48))
block(25.0, 0, -46.2, 29.0, 2.0, -45.8, CONCRETE)          # blast wall in front of the door
P("Explosives Crate", 25.0, -53.0, 0, tag="explosives"); P("Explosives Crate", 25.0, -52.0, 0, tag="explosives")
P("Explosives Crate", 29.0, -53.0, 90, tag="explosives")
P("Explosives Crate", 23.0, -47.5, 20, tag="explosives")
P("Danger Sign", 23.2, -48.8, 180, tag="explosives"); P("Danger Sign", 30.8, -47.6, 135, tag="explosives")
P("Alarm Light", 30.6, -49.6, 0, tag="explosives")

# ---------------------------------------------------------------- bench 2: drill pad, ore piles, a parked excavator, the shaft adit heads
pad_y = B2
P("Excavator", 48.0, -24.0, 270, tag="bench"); P("Haul Truck", 50.0, 18.0, 180, tag="vehicles")
P("Timber Stack", 56.0, -30.0, 0, tag="bench"); P("Timber Stack", 74.0, -30.5, 0, tag="bench"); P("Timber Stack", 76.0, 30.5, 0, tag="bench")
P("Explosive Barrel", 44.0, -14.0, 0, tag="explosives")
P("Floodlight Mast", 44.0, 31.0, 300, tag="bench"); P("Floodlight Mast", 58.0, -32.0, 150, tag="bench")
for z in (-30.0, -20.0, 10.0, 20.0):
    P("Concrete Block", 41.6, z, 90, tag="berm")
P("Water Barrier", 62.0, -31.0, 0, tag="bench"); P("Cable Spool", 46.0, 12.0, 0, tag="bench"); P("Pallet Stack", 45.5, -15.0, 0, tag="bench")

# ---------------------------------------------------------------- the top: headframe over the shaft, winder house, Red's camp
pad_y = TOP
P("Headframe Tower", SHAFT[0], SHAFT[1], 0, check=False, tag="headframe")
propw("Headframe Head", SHAFT[0], SHAFT[1], 0, t.ops[-1].get("y", TOP) + 11.9, tag="headframe")
for (x0, z0, x1, z1) in ((sx - 2.6, sz - 2.6, sx + 2.6, sz - 2.3), (sx - 2.6, sz + 2.3, sx + 2.6, sz + 2.6)):
    meshw("box", (x0 + x1) / 2, (z0 + z1) / 2, (0.85, 0.7, 0.1), TOP, width=x1 - x0, height=1.0, depth=z1 - z0)   # collar rails (cosmetic)
r = shell(74, 1, 84, 11, (0.55, 0.36, 0.28), {"W": [(6.0, 2.4, 0.0, 2.6), WIN(2.8, 1.4), WIN(9.2, 1.4)], "S": [DOOR(80.0)], "N": [DOOR(78.0), WIN(81.5, 1.8)]},
          h=4.6, wall=0.3, roof=(0.32, 0.3, 0.3))
ROOMS.append(("winder", r))
meshw("cylinder", 79.5, 4.4, (0.25, 0.25, 0.27), TOP + 1.5, width=2.4, height=3.2, depth=2.4, pitch=90, segments=16, material="Metal")   # hoist drum, lying along z
blockw(78.6, TOP, 4.4, 80.4, TOP + 1.5, 7.6, (0.25, 0.25, 0.27))
wp("Office Desk", r, "S", 76.2, tag="winder"); P("Office Chair", 76.2, 9.0, 180, tag="winder")
P("Computer Monitor", 76.4, r.rect[3] - 0.45, 0, y=0.76, check=False, tag="winder"); wp("Server Rack", r, "E", 9.6, tag="winder")
P("Electric Meter", 83.7, 2.0, 270, y=1.2, check=False, tag="winder")
P("Exhaust Fan", 81.0, 3.0, 0, y=4.8, check=False, tag="roof"); P("Radio Mast", 76.0, 9.5, 0, y=4.8, check=False, tag="roof")
for zr in (5.1, 6.9):   # the winding ropes from the drum up over the sheaves
    dx_, dy_ = SHAFT[0] - 79.0, (TOP + 19.6) - (TOP + 1.9)
    L = math.hypot(dx_, dy_)
    meshw("box", 79.0, zr, (0.6, 0.6, 0.6), TOP + 1.9, width=0.06, height=round(L, 2), depth=0.06,      # turned about its base, at the drum
          roll=round(math.degrees(math.atan2(-dx_, dy_)), 2))
# Red camp: mess cabin and lamp room (walk-in), stores, fuel, toilets, floodlights, trucks
r = shell(74, -19, 84, -14.5, CABIN, {"S": [DOOR(77.0), WIN(81.0, 2.2)], "N": [DOOR(82.0)], "E": [WIN(-16.75, 1.4)]}, trim=(0.7, 0.22, 0.18))
ROOMS.append(("mess", r))
P("Table", 78.4, -16.8, 0, tag="mess"); P("Plastic Chair", 77.8, -17.6, 0, tag="mess"); P("Plastic Chair", 79.0, -16.0, 180, tag="mess")
wp("Glowing Vending Machine", r, "W", -17.4, tag="mess"); wp("Fridge", r, "E", -18.0, tag="mess"); wp("Kitchen Counter", r, "E", -16.6, tag="mess")
P("Kettle", r.rect[2] - 0.35, -16.6, 270, y=0.92, check=False, tag="mess"); P("Water Cooler", 80.4, -18.5, 0, tag="mess")
r = shell(72, 14, 80, 18.5, CABIN_B, {"N": [DOOR(76.0), WIN(73.6, 1.4)], "W": [DOOR(16.25)]}, trim=CABIN)
ROOMS.append(("lamproom", r))
for k in range(int(5.0 // LW)):
    wp("Metal Lockers", r, "S", 74.0 + LW / 2 + k * LW, tag="lamproom")
P("Bench", 76.5, 16.0, 0, tag="lamproom"); wp("Metal Shelving", r, "W", 17.4, tag="lamproom")
closed_cabin(84, 13, 88.5, 16.5, CABIN_G)
P("Fuel Tank", 52.0, -30.5, 0, tag="fuel"); P("Portable Toilet", 82.0, 20.4, 0, tag="camp"); P("Portable Toilet", 83.4, 20.4, 0, tag="camp")
P("Floodlight Mast", 78.5, 21.0, 300, tag="camp")
P("Floodlight Mast", 87.0, -10.0, 270, margin=0.4, tag="camp")
P("Pickup Truck", 86.0, -14.0, 90, margin=0.4, tag="vehicles")
P("Plastic Water Tank", 75.0, 20.6, 0, tag="camp"); P("Flag on a Pole", 88.0, 6.0, 0, margin=0.3, tag="camp")
tk.bins_by_door(t, (82.0, -19.0), (-1, 0), (0, -1), rng, side=1, seed=4112, tag="bins")
# a safety berm (concrete blocks, sandbags) along the top's west rim, where it overlooks bench 2
block(64.6, 0, -8.8, 65.1, 2.2, -3.2, CONCRETE)           # blast wall at the head of the haul road (no line down it; walk round either end)

# ================================================================ edge dressing: the berm is a terrain cliff; scattered junk along its foot
for k in range(0, len(t.outline), 37):
    x, z = t.outline[k]
    cx, cz = x * 0.965, z * 0.965
    if t.inside(cx, cz, 1.6) and rng.random() < 0.55:
        P(rng.choice(["Rubble Pile", "Tyre Stack", "Rusty Drum", "Concrete Pipe", "Rubble Pile", "Tree Stump", "Log"]), cx, cz, rng.uniform(0, 360), lanes=True, tag="edge")

# ================================================================ spawns, waypoints
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
t.ops.append({"op": "AutoWaypoints", "spacing": 7})
for name, room in ROOMS:
    WAYPOINTS.append(((room.rect[0] + room.rect[2]) / 2, (room.rect[1] + room.rect[3]) / 2))
# waypoints every 3 m down the middle of every haul road too (a ramp is narrower than the nav grid's spacing)
for name, pts, w in ROADS:
    acc = 0.0
    for k in range(len(pts) - 1):
        (ax, az, _), (bx, bz, _) = pts[k], pts[k + 1]
        L = math.hypot(bx - ax, bz - az)
        n = max(1, int(L / 3.0))
        for i in range(n + (1 if k == len(pts) - 2 else 0)):
            x, z = ax + (bx - ax) * i / n, az + (bz - az) * i / n
            if t.inside(x, z, 1.0) and t.free((x - 0.3, z - 0.3, x + 0.3, z + 0.3)):
                WAYPOINTS.append((x, z))
for (x, z) in WAYPOINTS:
    t.ops.append({"op": "Waypoint", "x": R(x), "z": R(z)})

# ================================================================ cosmetic clutter (the game's Clutter op runs last)
DEBRIS = ["Rubble", "Rubble", "Rubble", "Loose Bricks", "Cinder Blocks", "Plank", "Plank", "Tyre", "Litter", "Soda Cans", "Plastic Bag", "Bottle", "Car Wheel"]
ROCKS = ["Rubble", "Rubble", "Rubble", "Rubble", "Loose Bricks", "Cinder Blocks"]   # cosmetic only: these ops reach over the tunnels
JUNK = ["Plank", "Plank Pile", "Tyre", "Tyre", "Pallet", "Oil Drum", "Rusty Drum", "Cable Spool", "Wooden Crate", "Small Crate", "Cinder Blocks", "Scaffold Board Stack", "Rubble Pile"]
t.clutter(DEBRIS, -88, -50, -20, 50, 900, 4101)
t.clutter(ROCKS + ["Plank"], -20, -60, 18, -31, 300, 4102)            # north dump top
t.clutter(ROCKS + ["Plank"], -40, 36, 16, 60, 280, 4103)              # south dump top
t.clutter(ROCKS + ["Tyre"], -19, -6, 11, 28, 260, 4104)               # pit floor
t.clutter(DEBRIS, -20, -31, 18, -6, 260, 4105)                        # yard
t.clutter(DEBRIS + ROCKS, 18, -55, 90, 55, 1100, 4106)                # the benches and the top
t.clutter(JUNK, -50, -30, -20, 30, 70, 4107)
t.clutter(JUNK, 22, -36, 36, 50, 50, 4108)
t.clutter(JUNK, 42, -34, 58, 1.5, 22, 4109)
t.clutter(JUNK, 42, 10.5, 58, 34, 22, 4110)
t.clutter(["Rubble Pile", "Rubble Pile", "Plank Pile", "Tyre Stack"], -40, -61, -22, -45, 14, 4111)   # solid junk on the dumps, clear of the tunnels
t.clutter(["Rubble Pile", "Rubble Pile", "Plank Pile"], -38, 38, -10, 58, 14, 4112)
for k, (name, room) in enumerate(ROOMS):
    x0, z0, x1, z1 = room.rect
    t.clutter(["Litter", "Newspaper", "Soda Cans", "Plastic Bag", "Bottles", "Cardboard Box Small", "Mop and Bucket", "Bucket", "Toolbox", "Backpack"],
              x0 + 0.5, z0 + 0.5, x1 - 0.5, z1 - 0.5, 12, 4120 + k)
for k, (name, S, cov, chunks) in enumerate(TUNNEL_INFO):    # tunnel floors: loose rock and the odd plank
    xs = [p[0] for p in S]; zs = [p[1] for p in S]
    t.clutter(["Rubble", "Rubble", "Loose Bricks", "Plank"], min(xs) - 2, min(zs) - 2, max(xs) + 2, max(zs) + 2, 40, 4140 + k)

# ================================================================ foliage: dusty grass on the margins, scrub, pines on the edges
paths = [{"points": [[R(p[0]), R(p[1])] for p in pts], "width": R(w + 1.0)} for name, pts, w in ROADS]
paths += [{"points": [[R(p[0]), R(p[1])] for p in pts], "width": TW + 1.4} for name, pts in TUNNELS]
paths += t.paths
BX0, BZ0, BX1, BZ1 = -92, -64, 92, 62
regions = [
    {"type": "grass", "density": 2.2, "rect": [BX0, BZ0, BX1, BZ1], "patchiness": 0.7, "color": [0.55, 0.55, 0.32], "scale": [0.7, 1.1]},
    {"type": "grass", "density": 4.5, "rect": [-92, -40, -56, 40], "patchiness": 0.5, "color": [0.50, 0.55, 0.30]},      # the camp's verges
    {"type": "grass", "density": 3.5, "rect": [60, -40, 92, 40], "patchiness": 0.55, "color": [0.52, 0.55, 0.31]},       # round Red's camp
    {"type": "shrub", "density": 0.5, "rect": [BX0, BZ0, BX1, BZ1], "patchiness": 0.6, "color": [0.45, 0.47, 0.28]},
    {"type": "bush", "density": 0.12, "rect": [BX0, BZ0, BX1, BZ1], "patchiness": 0.6, "color": [0.36, 0.42, 0.24]},
    {"type": "pine", "density": 0.012, "rect": [-92, -64, -45, 62], "height": [6, 11]},
    {"type": "pine", "density": 0.01, "rect": [60, -60, 92, 60], "height": [7, 12]},
    {"type": "tree", "density": 0.004, "rect": [-92, -64, 92, 62], "height": [5, 8], "color": [0.42, 0.48, 0.26]},
]
for (x0, z0, x1, z1) in [(-50, -61, 17, -31), (-40, 36, 16, 60)]:           # scrub on the old spoil
    regions.append({"type": "shrub", "density": 1.2, "rect": [x0, z0, x1, z1], "patchiness": 0.7, "color": [0.48, 0.46, 0.30]})
    regions.append({"type": "grass", "density": 2.5, "rect": [x0, z0, x1, z1], "patchiness": 0.75, "color": [0.58, 0.55, 0.34]})
# a band of long grass and bushes along the berm all round
edge_poly = [[R(x * 0.94), R(z * 0.94)] for x, z in t.outline[::12]]
exclude = [{"shape": "rect", "center": [R((x0 + x1) / 2), R((z0 + z1) / 2)], "halfX": R((x1 - x0) / 2 + 2.5), "halfZ": R((z1 - z0) / 2 + 2.5)} for (x0, z0, x1, z1) in
           [(-19, -6, 11, 28)]]                                         # the pit floor: bare
for x, z, r in HILLS:
    exclude.append({"shape": "circle", "center": [x, z], "radius": r, "density": 0.15})
include = [{"shape": "polygon", "points": [[R(x0 + 0.3), R(z0 + 0.3)], [R(x1 - 0.3), R(z0 + 0.3)], [R(x1 - 0.3), R(z1 - 0.3)], [R(x0 + 0.3), R(z1 - 0.3)]], "onTop": True}
           for (x0, z0, x1, z1) in INCLUDE_TOPS]   # polygons: a rect zone must be at least 10 m across
items = []
for (x, z, h) in ((-86.0, -16.0, 9.0), (-85.0, 20.0, 8.0), (-62.0, -22.0, 7.0), (-55.0, 22.0, 7.5), (-88.0, 8.0, 10.0), (84.0, 20.0, 9.0),
                  (88.0, -14.0, 10.0), (80.0, -26.0, 8.0), (62.0, -20.0, 7.0)):
    if t.inside(x, z, 1.5) and t.free((x - 0.5, z - 0.5, x + 0.5, z + 0.5), 0.4) and not t.in_keepout((x - 0.5, z - 0.5, x + 0.5, z + 0.5), 1.0):
        items.append({"type": "pine", "x": x, "z": z, "height": h})
foliage = {"seed": 411, "surfaces": {"Tiles": 0.1, "Plain": 0.1, "Dirt": 0.35}, "paths": paths, "regions": regions, "items": items,
           "exclude": exclude, "include": include}

# ================================================================ the shaft cage (an elevator up the shaft from the station to the top)
movers = [{"type": "elevator", "name": "Shaft Cage", "at": [sx, jy + 0.3, sz], "rise": round(TOP - jy, 2), "speed": 1.6, "dwell": 3.5,
           "boxes": [{"size": [2.8, 0.3, 2.8], "color": [0.42, 0.44, 0.46]},
                     {"size": [2.8, 1.1, 0.12], "offset": [0, 0.55, 1.34], "color": [0.85, 0.7, 0.1]},
                     {"size": [2.8, 1.1, 0.12], "offset": [0, 0.55, -1.34], "color": [0.85, 0.7, 0.1]}]}]

# ================================================================ the map
mp = {
    "format": "call-of-slop-map/1",
    "name": "Deepcut Mine",
    "description": "A mine site on a dusty afternoon: the camp and the plant down low, a terraced pit, spoil dumps and the headframe up top, and tunnels that pop in and out of the hills.",
    "author": "Call of Slop",
    "order": 411,
    "maxPlayers": 16,
    "sky": "#c9a77e",
    "skybox": {"preset": "desert", "horizon": "#cfae84", "zenith": "#7f93a6", "sun": {"time": 16.2, "latitude": 30},
               "haze": {"color": "#d8b07a", "strength": 0.65}, "clouds": {"coverage": 0.25, "speed": 6}},
    "boundary": {"shape": "smooth", "points": [list(p) for p in CTRL], "noise": NOISE, "edge": {"style": "cliff", "height": 9, "width": 7},
                 "groundMargin": 8, "barrierHeight": 3},
    "terrain": TERRAIN,
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": t.ops,
    "foliage": foliage,
    "movers": movers,
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

# ================================================================ analysis: walks (surface + tunnel layer) and spawn sight lines
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import dijkstra

CELL = 0.5
gx = np.arange(BX0, BX1, CELL) + CELL / 2
gz = np.arange(BZ0, BZ1, CELL) + CELL / 2
GX, GZ = np.meshgrid(gx, gz)
NZ, NX = GX.shape
SURF = terr.sample(GX, GZ).astype(np.float64)
ROOFED = np.zeros(GX.shape, bool)
for (x0, z0, x1, z1, y0, y1) in ROOFS:
    m = (GX >= x0) & (GX <= x1) & (GZ >= z0) & (GZ <= z1)
    SURF = np.where(m, np.maximum(np.where(ROOFED, SURF, -99), y1), SURF)
    ROOFED |= m
BLOCK = np.zeros(GX.shape, bool)
for (x0, z0, x1, z1, y0, y1) in WALLS:
    m = (GX >= x0 - 0.3) & (GX <= x1 + 0.3) & (GZ >= z0 - 0.3) & (GZ <= z1 + 0.3)
    BLOCK |= m & (y0 < SURF + 1.7) & (y1 > SURF + 0.55)
walk_grid = t.walk
for i in range(NZ):
    for j in range(NX):
        if not BLOCK[i, j]:
            ii = int((gz[i] - t.z0) / t.res); jj = int((gx[j] - t.x0) / t.res)
            if 0 <= ii < t.nz and 0 <= jj < t.nx and walk_grid[ii, jj]:
                BLOCK[i, j] = True
INSIDE = np.vectorize(lambda x, z: t.edge(x, z) >= 0.45)(GX, GZ)
OK = INSIDE & ~BLOCK
idx = -np.ones(GX.shape, np.int64)
idx[OK] = np.arange(OK.sum())
N_SURF = int(OK.sum())
rows, cols, wts = [], [], []
for di, dj in ((0, 1), (1, 0), (1, 1), (1, -1)):
    a = idx[max(0, -di):NZ - max(0, di), max(0, -dj):NX - max(0, dj)]
    b = idx[max(0, di):NZ - max(0, -di) if di <= 0 else NZ, :][:, max(0, dj):NX - max(0, -dj) if dj <= 0 else NX] if False else None
    ia0, ia1 = 0, NZ - di
    ja0, ja1 = max(0, -dj), NX - max(0, dj)
    A = idx[ia0:ia1, ja0:ja1]; B = idx[ia0 + di:ia1 + di, ja0 + dj:ja1 + dj]
    HA = SURF[ia0:ia1, ja0:ja1]; HB = SURF[ia0 + di:ia1 + di, ja0 + dj:ja1 + dj]
    d = CELL * math.hypot(di, dj)
    dh = np.abs(HB - HA)
    ok = (A >= 0) & (B >= 0) & ((dh <= d * 1.0) | (dh <= 0.5))
    rows.append(A[ok]); cols.append(B[ok]); wts.append(np.sqrt(d * d + dh[ok] ** 2))
# the tunnel layer: nodes every 0.5 m along each tunnel, joined to each other, to the J room and to the surface at the mouths
tnodes = []
for (name, S, cov, chunks) in TUNNEL_INFO:
    base = N_SURF + len(tnodes)
    for k, (x, z, y, dx, dz, seg) in enumerate(S):
        tnodes.append((x, z, y))
        if k:
            px, pz, py = S[k - 1][0], S[k - 1][1], S[k - 1][2]
            rows.append(np.array([base + k - 1])); cols.append(np.array([base + k])); wts.append(np.array([math.dist((px, pz, py), (x, z, y))]))
jr = []
for x in np.arange(jx0 + 0.75, jx1, 1.0):
    for z in np.arange(jz0 + 0.75, jz1, 1.0):
        if abs(x - sx) < 1.5 and abs(z - sz) < 1.5:
            continue
        jr.append(len(tnodes)); tnodes.append((float(x), float(z), jy))
T = np.array(tnodes)
from scipy.spatial import cKDTree
kd = cKDTree(T)
for a, b in kd.query_pairs(1.05):
    if abs(T[a, 2] - T[b, 2]) < 0.6:
        rows.append(np.array([N_SURF + a])); cols.append(np.array([N_SURF + b])); wts.append(np.array([float(np.linalg.norm(T[a] - T[b]))]))
for k, (x, z, y) in enumerate(tnodes):
    j = int((x - BX0) / CELL); i = int((z - BZ0) / CELL)
    if 0 <= i < NZ and 0 <= j < NX and idx[i, j] >= 0 and not ROOFED[i, j] and abs(SURF[i, j] - y) < 0.6:
        rows.append(np.array([idx[i, j]])); cols.append(np.array([N_SURF + k])); wts.append(np.array([0.3]))
NN = N_SURF + len(tnodes)
G = coo_matrix((np.concatenate(wts), (np.concatenate(rows), np.concatenate(cols))), shape=(NN, NN)).tocsr()


def node_at(x, z):
    j = int((x - BX0) / CELL); i = int((z - BZ0) / CELL)
    if idx[i, j] >= 0:
        return int(idx[i, j])
    for rr in range(1, 8):
        sub = idx[max(0, i - rr):i + rr + 1, max(0, j - rr):j + rr + 1]
        if (sub >= 0).any():
            return int(sub[sub >= 0][0])
    return -1


def team_field(spawns):
    src = [node_at(x, z) for x, z in spawns]
    D = dijkstra(G, directed=False, indices=src)
    return D.mean(axis=0)


DB = team_field(BLUE_SP); DR = team_field(RED_SP)


def walk(D, x, z):
    n = node_at(x, z)
    return float(D[n]) if n >= 0 else float("inf")


def eye_sees(a, b, eye=1.6):
    """Spawn-to-spawn sight over the surface (terrain + roofs) and tall boxes, sampled every 0.25 m."""
    ya = hill(*a) + eye; yb = hill(*b) + eye
    L = math.hypot(b[0] - a[0], b[1] - a[1]); n = max(1, int(L / 0.25))
    for s in range(1, n):
        f = s / n
        x = a[0] + (b[0] - a[0]) * f; z = a[1] + (b[1] - a[1]) * f; y = ya + (yb - ya) * f
        j = int((x - BX0) / CELL); i = int((z - BZ0) / CELL)
        if SURF[i, j] >= y or not t.inside(x, z, 0.0):
            return False
        for (x0, z0, x1, z1, y0, y1) in WALLS_NEAR.get((int(x // 8), int(z // 8)), ()):
            if x0 <= x <= x1 and z0 <= z <= z1 and y0 <= y <= y1:
                return False
        ii = int((z - t.z0) / t.res); jj = int((x - t.x0) / t.res)
        if t.tall[ii, jj] and hill(x, z) + 1.9 > y:
            return False
    return True


WALLS_NEAR = {}
for w in WALLS:
    for ci in range(int(w[0] // 8), int(w[2] // 8) + 1):
        for cj in range(int(w[1] // 8), int(w[3] // 8) + 1):
            WALLS_NEAR.setdefault((ci, cj), []).append(w)

if __name__ == "__main__":
    from collections import Counter
    for x, z in BLUE_SP + RED_SP + FFA_SP + FLAGS:
        if any(q[0] - 0.5 <= x <= q[2] + 0.5 and q[1] - 0.5 <= z <= q[3] + 0.5 for q in ROOFS):
            print("WARN spawn/flag over a tunnel roof (it would land in the tunnel)", x, z)
    for x, z, rr in HILLS:
        if any(max(q[0] - x, 0, x - q[2]) ** 2 + max(q[1] - z, 0, z - q[3]) ** 2 < (rr + 0.3) ** 2 for q in ROOFS):
            print("WARN hill zone touches a tunnel roof", x, z)
    for x, z in BLUE_SP + RED_SP + FFA_SP + [(h[0], h[1]) for h in HILLS] + FLAGS:
        if not t.inside(x, z, 1.5):
            print("WARN objective near/outside the edge", x, z)
        if not t.free((x - 0.4, z - 0.4, x + 0.4, z + 0.4)):
            print("WARN objective inside something", x, z)
    cats = Counter(); solid = 0; cosm = 0
    for name, x, z, yaw, y, tag in t.props:
        p = t.P[name]; cats[p["cat"]] += 1
        solid += p["solid"]; cosm += not p["solid"]
    blocks = sum(1 for o in t.ops if o["op"] == "Block")
    print(f"area {t.area():.0f} m2, blocks {blocks} ({len(ROOFS)} tunnel roofs), meshes {t.meshes}, hand props {len(t.props)} (solid {solid}, cosmetic {cosm}), lamps {LAMPS}, waypoints {len(WAYPOINTS)}")
    print("props by category:", dict(sorted(cats.items())))
    print("not placed:", FAILED)
    print("props moved onto tunnel roofs:", ROOF_PROPS)
    print("clutter asked:", sum(o["count"] for o in t.ops if o["op"] == "Clutter"))
    for name, S, cov, chunks in TUNNEL_INFO:
        L = sum(math.hypot(S[k][0] - S[k - 1][0], S[k][1] - S[k - 1][1]) for k in range(1, len(S)))
        print(f"  tunnel {name}: {L:.0f} m, {sum(cov) * 0.5:.0f} m under cover, {len(chunks)} roof boxes, floor {S[0][2]:+.1f} .. {S[-1][2]:+.1f}")
    for i, (x, z, r) in enumerate(HILLS):
        b, rd = walk(DB, x, z), walk(DR, x, z)
        print(f"hill {i + 1} ({x},{z}) ground {ground(x, z):.1f}: walk blue {b:.0f} m red {rd:.0f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
    b, rd = walk(DB, *FLAGS[1]), walk(DR, *FLAGS[0])
    print(f"enemy flag: blue {b:.0f} m red {rd:.0f} m ({(b - rd) / max(b, rd) * 100:+.1f}%)")
    vis = [(a, c) for a in BLUE_SP for c in RED_SP if eye_sees(a, c)]
    print("blue-red spawn lines of sight:", len(vis), vis[:4])
    if OUT_PNG:
        from PIL import Image, ImageDraw
        c = terr.colors()
        h = terr.h
        sh = np.clip(0.55 + (h - h.min()) / (h.max() - h.min()) * 0.6, 0, 1.2)
        img = (np.clip(c * sh[..., None], 0, 1) * 255).astype(np.uint8)
        S2 = 3
        im = Image.fromarray(img).resize((terr.nx * S2, terr.nz * S2), Image.NEAREST)
        d = ImageDraw.Draw(im)
        Pp = lambda x, z: ((x - terr.ox) / terr.cell * S2, (z - terr.oz) / terr.cell * S2)
        d.line([Pp(*p) for p in t.outline[::4]] + [Pp(*t.outline[0])], fill=(255, 255, 255), width=2)
        for (x0, z0, x1, z1, y0, y1) in ROOFS:
            d.rectangle([Pp(x0, z0), Pp(x1, z1)], outline=(255, 60, 60))
        for (x0, z0, x1, z1, y0, y1) in WALLS:
            d.rectangle([Pp(x0, z0), Pp(x1, z1)], fill=(60, 60, 70))
        for name, x, z, yaw, y, tag in t.props:
            if y and y > 0.6:
                continue
            r = t.foot(name, x, z, yaw); p = t.P[name]
            fill = (220, 130, 40) if p["solid"] else (240, 240, 140)
            d.rectangle([Pp(r[0], r[1]), Pp(r[2], r[3])], outline=fill)
        for x, z in BLUE_SP:
            d.ellipse([Pp(x - 1, z - 1), Pp(x + 1, z + 1)], fill=(40, 90, 250))
        for x, z in RED_SP:
            d.ellipse([Pp(x - 1, z - 1), Pp(x + 1, z + 1)], fill=(250, 50, 50))
        for x, z in FFA_SP:
            d.ellipse([Pp(x - 1, z - 1), Pp(x + 1, z + 1)], fill=(250, 250, 250))
        for x, z, r in HILLS:
            d.ellipse([Pp(x - r, z - r), Pp(x + r, z + r)], outline=(255, 230, 0), width=3)
        for x, z in FLAGS:
            d.rectangle([Pp(x - 1, z - 1), Pp(x + 1, z + 1)], fill=(255, 255, 255))
        for a, c2 in vis:
            d.line([Pp(*a), Pp(*c2)], fill=(255, 0, 255))
        # equal-walk contour (blue == red) on the surface
        eq = np.zeros(GX.shape, bool)
        V = np.full(GX.shape, np.nan); V[OK] = (DB[:N_SURF] - DR[:N_SURF])
        sgn = np.sign(V)
        eq[:, 1:] |= (sgn[:, 1:] != sgn[:, :-1]) & ~np.isnan(V[:, 1:]) & ~np.isnan(V[:, :-1])
        eq[1:, :] |= (sgn[1:, :] != sgn[:-1, :]) & ~np.isnan(V[1:, :]) & ~np.isnan(V[:-1, :])
        for i, j in zip(*np.nonzero(eq)):
            x, z = gx[j], gz[i]
            d.point(Pp(x, z), fill=(0, 255, 255))
        for gx_ in range(-80, 91, 10):
            d.text(Pp(gx_, -62), str(gx_), fill=(255, 255, 0))
        for gz_ in range(-60, 61, 10):
            d.text(Pp(-91, gz_), str(gz_), fill=(255, 255, 0))
        im.save(OUT_PNG)
