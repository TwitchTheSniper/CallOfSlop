#!/usr/bin/env python3
"""Generator for Maps/Cluttered/05-Dockside.json (medium crescent harbour, 12 players).
Usage: python3 tools/cluttered/dockside.py <repo root> [preview.png]

A harbour front curving round a bay that opens to the north. The playable outline is a polygon: the land side is a
smooth curve (Catmull-Rom with a generator-side wobble, so it isn't regular), the bay side follows the quay edges, the
two piers and the lighthouse mole exactly. Everything is mirrored about x = 0: Blue holds the west horn, Red the east.

Water: the engine has no water surfaces, so the sea is terrain: a `grid` terrain feature drops the ground 3 m just
outside the bay-side outline (the quay wall), and a big flat cosmetic box mesh at y = -0.9 is the water surface. The sea
is outside the outline, so nobody can walk on it or fall in; the invisible barrier is kept low (barrierHeight 0.1) so
shots and sight lines cross the bay. Boats, buoys, ducks and a fishing bobber float on it (pack props placed with
"anchor": "world" at the water level), the only props outside the outline.

Needs tools/cluttered/harbour_props.py to have written the pack props (Maps/Cluttered/Props/Harbour).
Placement rules come from tools/cluttered/placekit.py.
"""
import json, math, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import placekit as pk  # noqa: E402

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/05-Dockside.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None

WATER_Y = -0.9      # water surface (world height)
SEA_BED = -3.0
LEDGE = 0.8         # the ground stays level this far outside the bay-side outline (terrain cells are 0.5 m)

# ============================================================== outline (Blue half, then mirrored)


def arc(cx, cz, r, a0, a1, step=1.0):
    n = max(2, int(abs(math.radians(a1 - a0)) * r / step))
    return [[cx + r * math.cos(math.radians(a0 + (a1 - a0) * k / n)), cz + r * math.sin(math.radians(a0 + (a1 - a0) * k / n))] for k in range(n + 1)]


LAND_CTRL = [[0, 34.0], [-18, 33.2], [-36, 29.5], [-52, 21.5], [-63, 9], [-69, -6], [-70, -20], [-68, -33], [-63, -42]]
land = pk.catmull(LAND_CTRL, step=2.5)
# generator-side wobble along the normal, zero at both ends so the mirror join and the horn corner stay put
L = pk.polyline_length(land)
wob = []
acc = 0.0
for i, p in enumerate(land):
    if i:
        acc += math.hypot(p[0] - land[i - 1][0], p[1] - land[i - 1][1])
    a = land[max(0, i - 1)]
    b = land[min(len(land) - 1, i + 1)]
    tx, tz = b[0] - a[0], b[1] - a[1]
    tl = math.hypot(tx, tz) or 1
    nx, nz = -tz / tl, tx / tl
    fade = min(1.0, acc / 8.0, (L - acc) / 8.0)
    w = 1.0 * fade * (0.6 * math.sin(acc / 6.1 + 0.7) + 0.4 * math.sin(acc / 3.4 + 2.1))
    wob.append([round(p[0] + nx * w, 3), round(p[1] + nz * w, 3)])
land = wob
land[0] = [0.0, 34.0]

# (points, tag) pieces along the bay side, from the horn's north-west corner to the mole tip
pieces = [
    ([[-59, -45], [-30, -45]], "shore"),                            # the horn's north edge (the open sea)
    ([[-30, -45], [-30, -34]], "shore"),
    ([[-30, -34], [-17, -34], [-17, -29], [-30, -29]], "pier"),     # P1: the horn pier
    ([[-30, -29], [-30, -14]], "shore"),
    (arc(-21, -14, 9, 180, 90), "shore"),                            # the inner corner of the bay
    ([[-21, -5], [-15, -5]], "shore"),
    ([[-15, -5], [-15, -19], [-11, -19], [-11, -5]], "pier"),       # P2: the fish quay pier
    ([[-11, -5], [-4, -5]], "shore"),
    ([[-4, -5], [-4, -30]], "mole"),
    (arc(0, -30, 4, 180, 270), "mole"),                             # the mole's round tip
]
# Blue half: points and the tag of the segment that starts at each one (the land curve's last segment, down to the
# horn's north-west corner, is still land: the wall runs to the water there)
blue_pts = [[round(p[0], 3), round(p[1], 3)] for p in land]
blue_tag = ["land"] * len(blue_pts)
for pts, tag in pieces:
    for k, p in enumerate(pts):
        q = [round(p[0], 3), round(p[1], 3)]
        if abs(blue_pts[-1][0] - q[0]) < 1e-6 and abs(blue_pts[-1][1] - q[1]) < 1e-6:
            if k == 0:
                blue_tag[-1] = blue_tag[-1] if blue_tag[-1] == "land" else tag
            continue
        blue_pts.append(q)
        blue_tag.append(tag)
# the first piece's start is the land curve's end + one segment: make that join a land segment
n = len(blue_pts)
seg_blue = [blue_tag[i] for i in range(n - 1)]  # segment i: blue_pts[i] -> blue_pts[i + 1]
OUT = blue_pts + [[-p[0], p[1]] for p in reversed(blue_pts[1:-1])]
TAGS = seg_blue + [seg_blue[k - 1] for k in range(n - 1, 0, -1)]
assert len(TAGS) == len(OUT)
OUTLINE = OUT


def inside(x, z):
    return pk.poly_inside(OUTLINE, x, z)


def edge_dist(x, z):
    return pk.poly_edge_dist(OUTLINE, x, z)


def inset_ok(x, z, m):
    return inside(x, z) and edge_dist(x, z) >= m




SEGS = [(OUTLINE[i], OUTLINE[(i + 1) % len(OUTLINE)], TAGS[i]) for i in range(len(OUTLINE))]
BLUE_SEGS = [(a, b, t) for a, b, t in SEGS if a[0] <= 0.001 and b[0] <= 0.001]

# ============================================================== terrain: the sea is a 3 m drop just outside the bay side
# Base 0 (land). `plateau` features in "cut" mode carve the sea 1.3 m out from the bay-side outline and blend back up to
# the quay level over 0.5 m, so the ground stays level for LEDGE (0.8 m) outside the outline (the barrier holds players
# 0.4 m inside it, and every terrain cell under a player is level); "fill" plateaus then put the piers and the mole back.
CUT = 1.3
EDGE = CUT - LEDGE


def plateau_rect(x0, z0, x1, z1, h, mode, edge=EDGE, mirror=True):
    f = {"type": "plateau", "x": round((x0 + x1) / 2, 3), "z": round((z0 + z1) / 2, 3), "w": round(x1 - x0, 3), "d": round(z1 - z0, 3), "height": h, "edge": edge, "mode": mode}
    out = [f]
    if mirror and abs(x0 + x1) > 0.01:
        out.append(dict(f, x=-f["x"]))
    return out


def plateau_circle(x, z, r, h, mode, edge=EDGE, mirror=True):
    f = {"type": "plateau", "x": x, "z": z, "radius": r, "height": h, "edge": edge, "mode": mode}
    return [f] + ([dict(f, x=-x)] if mirror and abs(x) > 0.01 else [])


TERRAIN_FEATURES = (
    plateau_rect(-130, -130, 130, -45 - CUT, SEA_BED, "cut", mirror=False)          # the open sea north of the horns
    + plateau_rect(-30 + CUT, -130, -4 - CUT, -5 - CUT, SEA_BED, "cut")              # each half of the bay
    + plateau_rect(-30 + CUT - 0.8, -14, -21, -5 - CUT + 0.8, 0.0, "fill", edge=0)   # the bay's inner corner is land again (over the cut's blend)...
    + plateau_circle(-21, -14, 9 - CUT, SEA_BED, "cut")                             # ...except a quarter circle of water
    + plateau_rect(-32, -34 - LEDGE, -17 + LEDGE, -29 + LEDGE, 0.0, "fill")          # pier P1
    + plateau_rect(-15 - LEDGE, -19 - LEDGE, -11 + LEDGE, -3, 0.0, "fill")           # pier P2
    + plateau_rect(-4 - LEDGE, -30, 4 + LEDGE, -3, 0.0, "fill", mirror=False)        # the mole
    + plateau_circle(0, -30, 4 + LEDGE, 0.0, "fill", mirror=False)                   # its round tip
)


def ground_at(x, z):
    """The terrain height from TERRAIN_FEATURES (for checks), mirroring the game's plateau maths."""
    h = 0.0
    for f in TERRAIN_FEATURES:
        if "radius" in f:
            out = max(0.0, math.hypot(x - f["x"], z - f["z"]) - f["radius"])
        else:
            ax = max(abs(x - f["x"]) - f["w"] / 2, 0)
            az = max(abs(z - f["z"]) - f["d"] / 2, 0)
            out = math.hypot(ax, az)
        e = f["edge"]
        mask = (1 - (lambda t: t * t * (3 - 2 * t))(min(1.0, out / e))) if e > 0 else (1.0 if out <= 0 else 0.0)
        if mask <= 0:
            continue
        v = h + (f["height"] - h) * mask
        h = min(h, v) if f["mode"] == "cut" else max(h, v) if f["mode"] == "fill" else v
    return h


# ============================================================== palette
QUAY = (0.58, 0.57, 0.54); QUAY_D = (0.42, 0.41, 0.39); ASPH = (0.19, 0.19, 0.21); KERB = (0.72, 0.71, 0.68)
SIDEWALK = (0.64, 0.61, 0.56); TILE = (0.70, 0.68, 0.62); DECK = (0.46, 0.35, 0.23); DECK_D = (0.30, 0.22, 0.15)
BRICK = (0.56, 0.31, 0.25); WAREHOUSE = (0.36, 0.46, 0.54); WAREHOUSE_TRIM = (0.80, 0.78, 0.72); ROOF = (0.30, 0.31, 0.33)
HM_WALL = (0.86, 0.82, 0.72); HM_ROOF = (0.42, 0.24, 0.20); PART = (0.78, 0.76, 0.70); WATER = (0.16, 0.31, 0.36)
STONE = (0.62, 0.60, 0.56); CONTAINERS = [(0.70, 0.22, 0.16), (0.18, 0.36, 0.60), (0.20, 0.50, 0.32), (0.85, 0.55, 0.12), (0.55, 0.55, 0.58), (0.45, 0.20, 0.40)]
CRANE = (0.90, 0.62, 0.10); AWNING = [(0.18, 0.42, 0.70), (0.90, 0.90, 0.88), (0.80, 0.22, 0.18), (0.20, 0.55, 0.45)]

lib = pk.PropLib(REPO, pack="Cluttered")
lay = pk.Layout(lib, terrain=True)
ops = lay.ops
W = {"anchor": "world"}  # meshes over the drop keep their absolute height

# ============================================================== objectives (hand-placed), keep-outs and lanes
BLUE_SP = [(-60.5, -39.0), (-60.5, -33.0), (-60.5, -25.0), (-60.5, -19.0), (-51.8, -36.0), (-51.8, -21.0)]
RED_SP = [(-x, z) for x, z in BLUE_SP]
FFA_SP = [(-36.0, 23.5), (-20.0, 28.5), (36.0, 23.5), (20.0, 28.5)]
HILLS = [(0.0, -25.5, 3.5), (0.0, 3.5, 4.5), (0.0, 17.5, 4.0)]
FLAGS = [(-61.5, -29.0), (61.5, -29.0)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    lay.keepout.append((x, z, 2.5))
for x, z, r in HILLS:
    lay.keepout.append((x, z, r + 0.5))
for x, z in FLAGS:
    lay.keepout.append((x, z, 2.5))

ROAD_W = 7.0
ROAD = [[0, 4.0], [-24, 4.0]] + [[-24 + 17 * math.cos(math.radians(a)), -13 + 17 * math.sin(math.radians(a))] for a in range(85, 179, 5)] + [[-41, -13.0], [-41, -43.0]]
LANES = [
    (ROAD, 3.0),                                                      # the waterfront road
    ([[-33.5, -43], [-33.5, -12]], 2.6),                              # horn quay walk (between the crane legs)
    ([[-51.8, -43], [-51.8, -11]], 2.4),                              # yard lane between the container rows
    ([[-64, -29], [-41, -29]], 2.4),                                  # yard cross lane (the gap in the rows)
    ([[-64, -10.5], [-41, -10.5]], 2.4),                              # yard's south lane
    ([[-27, -2.2], [0, -2.2]], 2.4),                                  # bay-bottom quay walk
    ([[-30, -31.5], [-18, -31.5]], 2.0),                              # pier P1
    ([[-13, -5], [-13, -18]], 1.8),                                   # pier P2
    ([[-2.2, -6], [-2.2, -28]], 1.8),                                 # mole, west of the sea wall
    ([[-30, 28.6], [0, 28.8]], 2.4),                                  # back alley
    ([[-29.0, 8], [-29.0, 27]], 2.2),                                 # side lane between the warehouse and the harbour office
    ([[0, 10], [0, 26]], 3.0),                                        # fish market aisle
    ([[-30, 9.0], [0, 9.0]], 2.0),                                    # sidewalk
    ([[-40, -4], [-46, 8], [-44, 24]], 2.0),                          # path across the depot lot
]
lay.lanes.extend(LANES)

# ============================================================== water, quay walls, coping, paving


def seg_box(a, b, inner, outer, y, h, c, material=None, mirror=True, extend=0.0):
    """A box mesh along an outline segment a->b spanning `inner` metres inside to `outer` outside (the water is on the
    segment's left), from height y up h (anchor world)."""
    dx, dz = b[0] - a[0], b[1] - a[1]
    Ls = math.hypot(dx, dz)
    tx, tz = dx / Ls, dz / Ls
    lx, lz = tz, -tx  # left = water side
    mid = (outer - inner) / 2
    cx = (a[0] + b[0]) / 2 + lx * mid
    cz = (a[1] + b[1]) / 2 + lz * mid
    kw = dict(y=y, width=Ls + extend, height=h, depth=inner + outer, yaw=round(pk.yaw_along(tx, tz), 2), **W)
    if material:
        kw["material"] = material
    lay.mesh("box", cx, cz, c, mirror=mirror, **kw)


# the sea: one big flat box (its top is the water surface)
lay.mesh("box", 0.0, -170.0, WATER, mirror=False, y=WATER_Y - 0.3, width=500.0, height=0.3, depth=400.0, **W)
for a, b, t in BLUE_SEGS:
    if t == "land":
        continue
    # quay wall face down into the water and the coping stone on top of the edge
    seg_box(a, b, -LEDGE + 0.05, LEDGE + 0.25, -2.0, 2.0, QUAY_D if t != "pier" else DECK_D, material="Concrete" if t != "pier" else "Wood", extend=0.3)
    seg_box(a, b, 0.25, LEDGE + 0.3, 0.0, 0.12 if t != "pier" else 0.08, KERB if t != "pier" else DECK, material="Concrete" if t != "pier" else "Wood", extend=0.3)
    if t == "shore":
        seg_box(a, b, 6.5, 0.0, 0.0, 0.03, QUAY, material="Concrete", extend=0.4)
# the piers: timber decks on posts
for (x0, z0, x1, z1) in [(-30.2, -34.0, -17.0, -29.0), (-15.0, -19.0, -11.0, -5.0)]:
    lay.mesh("box", (x0 + x1) / 2, (z0 + z1) / 2, DECK, y=0.0, width=x1 - x0, height=0.05, depth=z1 - z0, material="Wood", **W)
    for k in range(int((x1 - x0) / 2.4) + 1 if x1 - x0 > z1 - z0 else int((z1 - z0) / 2.4) + 1):
        for side in (0, 1):
            if x1 - x0 > z1 - z0:
                px, pz = x0 + 0.6 + k * 2.4, (z0 - LEDGE - 0.2) if side == 0 else (z1 + LEDGE + 0.2)
            else:
                px, pz = (x0 - LEDGE - 0.2) if side == 0 else (x1 + LEDGE + 0.2), z0 + 0.6 + k * 2.4
            if (x1 - x0 > z1 - z0 and px > x1) or (x1 - x0 <= z1 - z0 and pz > z1 - 0.5):
                continue
            lay.mesh("cylinder", px, pz, DECK_D, y=-2.0, width=0.32, height=2.25, depth=0.32, segments=8, **W)
# the mole: concrete with a round tip
lay.mesh("box", 0.0, -17.5, QUAY, mirror=False, y=0.0, width=8.0, height=0.03, depth=25.0, material="Concrete", **W)
lay.mesh("cylinder", 0.0, -30.0, QUAY, mirror=False, y=0.0, width=8.0, height=0.03, depth=8.0, segments=24, material="Concrete", **W)

# land: busted concrete over the horn yards and the middle, asphalt road, kerbs, sidewalk, market tiles
lay.mesh("box", -52.0, -26.0, QUAY, y=0.0, width=40.0, height=0.02, depth=39.5, material="Concrete")      # x -72..-32, z -45.75..-6.25
lay.mesh("box", -32.0, -23.5, QUAY, y=0.0, width=4.0, height=0.02, depth=35.0, material="Concrete")       # x -34..-30, z -41..-6
lay.mesh("box", 0.0, 18.0, QUAY, mirror=False, y=0.0, width=60.0, height=0.02, depth=35.0, material="Concrete")  # x -30..30, z 0.5..35.5
for i in range(len(ROAD) - 1):
    a, b = ROAD[i], ROAD[i + 1]
    dx, dz = b[0] - a[0], b[1] - a[1]
    Ls = math.hypot(dx, dz)
    yaw = round(pk.yaw_along(dx / Ls, dz / Ls), 2)
    lay.mesh("box", (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, ASPH, y=0.0, width=Ls + 0.6, height=0.05, depth=ROAD_W, yaw=yaw)
    for off in (ROAD_W / 2, -ROAD_W / 2):
        nx, nz = dz / Ls, -dx / Ls
        lay.mesh("box", (a[0] + b[0]) / 2 + nx * off, (a[1] + b[1]) / 2 + nz * off, KERB, y=0.0, width=Ls + 0.3, height=0.13, depth=0.3, yaw=yaw)
# the middle road's two halves meet on x = 0: the box from (0,4) to (-24,4) is mirrored, so it covers -24..24
lay.mesh("box", -15.0, 8.95, SIDEWALK, y=0.0, width=30.0, height=0.08, depth=2.9, material="Tiles")       # south sidewalk, x -30..0
lay.mesh("box", 0.0, 17.7, TILE, mirror=False, y=0.0, width=18.0, height=0.06, depth=14.6, material="Tiles")  # fish market street
# road markings: dashed centre line
for k in range(9):
    lay.mesh("box", -1.5 - k * 2.8, 4.0, (0.85, 0.82, 0.70), y=0.05, width=1.4, height=0.01, depth=0.14)
# a zebra crossing in front of the market
for k in range(5):
    lay.mesh("box", -2.4 + k * 1.2, 4.0, (0.88, 0.88, 0.86), mirror=False, y=0.05, width=0.6, height=0.01, depth=5.6)

# ============================================================== land wall and the town beyond it
for a, b, t in BLUE_SEGS:
    if t != "land":
        continue
    dx, dz = b[0] - a[0], b[1] - a[1]
    Ls = math.hypot(dx, dz)
    tx, tz = dx / Ls, dz / Ls
    rx, rz = -tz, tx  # right = outside for the land side (the polygon runs clockwise on screen here)
    cx, cz = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
    # which side is outside? test a point
    if inside(cx + rx * 1.0, cz + rz * 1.0):
        rx, rz = -rx, -rz
    lay.mesh("box", cx + rx * 0.3, cz + rz * 0.3, BRICK, y=0.0, width=Ls + 0.35, height=3.4, depth=0.5, yaw=round(pk.yaw_along(tx, tz), 2), material="Brick")
    lay.mesh("box", cx + rx * 0.3, cz + rz * 0.3, (0.66, 0.62, 0.56), y=3.4, width=Ls + 0.45, height=0.14, depth=0.62, yaw=round(pk.yaw_along(tx, tz), 2), material="Concrete")
# town rooftops beyond the wall (scenery)
rnd = random.Random(31)
for k in range(15):
    t = (k + 0.5) / 15
    i = int(t * (len(land) - 3)) + 1
    p, q = land[i - 1], land[i + 1]
    tx, tz = q[0] - p[0], q[1] - p[1]
    Ls = math.hypot(tx, tz)
    nx, nz = -tz / Ls, tx / Ls
    if inside(land[i][0] + nx, land[i][1] + nz):
        nx, nz = -nx, -nz
    d = rnd.uniform(9, 16)
    bx, bz = land[i][0] + nx * d, land[i][1] + nz * d
    hgt = rnd.uniform(6, 13)
    w, dd = rnd.uniform(7, 12), rnd.uniform(7, 11)
    yaw = round(pk.yaw_along(tx / Ls, tz / Ls), 1)
    c = rnd.choice([(0.62, 0.55, 0.48), (0.72, 0.68, 0.60), (0.50, 0.42, 0.38), (0.58, 0.60, 0.62)])
    lay.mesh("box", bx, bz, c, width=w, height=hgt, depth=dd, yaw=yaw, material="Brick" if rnd.random() < 0.5 else "Concrete")
    lay.mesh("wedge", bx, bz, (0.36, 0.22, 0.18), y=hgt, width=w + 0.4, height=2.2, depth=dd / 2 + 0.2, yaw=yaw)
    lay.mesh("wedge", bx, bz, (0.36, 0.22, 0.18), y=hgt, width=w + 0.4, height=2.2, depth=dd / 2 + 0.2, yaw=round((yaw + 180) % 360, 1))
    if rnd.random() < 0.5:
        lay.mesh("box", bx + rnd.uniform(-2, 2), bz + rnd.uniform(-2, 2), (0.45, 0.30, 0.26), y=hgt + 1.0, width=0.8, height=2.6, depth=0.8, material="Brick")
# a breakwater arm across the far side of the bay mouth with a beacon (scenery out on the water)
for k in range(7):
    lay.mesh("box", -40.0 - k * 9.0, -72.0 - k * 1.6, STONE, y=SEA_BED, width=9.6, height=3.6, depth=5.0, yaw=10.0, material="Concrete", **W)
lay.mesh("cylinder", -40.0, -72.0, (0.85, 0.20, 0.15), y=0.6, width=1.0, height=4.0, depth=1.0, segments=12, **W)

# ============================================================== buildings
WALL_T = 0.3


def warehouse():
    """W1: a walk-in warehouse on the road (x -27..-9, z 10.5..24.5): open store floor, an office and a break room
    at the back, doors to the road, the market street, the side lane and the alley."""
    x0, x1, z0, z1, h = -27.0, -9.0, 10.5, 24.5, 4.6
    lay.wall(x0, z0, x1, z0, h, WALL_T, WAREHOUSE, doors=[(-21.0, 3.6, 3.4), (-13.0, 1.6, 2.4)])            # north: loading door + door
    lay.wall(x0, z1 - WALL_T, x1, z1 - WALL_T, h, WALL_T, WAREHOUSE, doors=[(-11.5, 1.6, 2.4)])           # south: back door
    lay.wall(x0, z0 + WALL_T, x0, z1 - WALL_T, h, WALL_T, WAREHOUSE, doors=[(14.2, 1.8, 2.4)])            # west
    lay.wall(x1 - WALL_T, z0 + WALL_T, x1 - WALL_T, z1 - WALL_T, h, WALL_T, WAREHOUSE, doors=[(16.5, 1.8, 2.4)])  # east, to the market
    lay.block(x0 - 0.2, h, z0 - 0.2, x1 + 0.2, h + 0.25, z1 + 0.2, ROOF)
    # office (south-west) and break room (south, east of it): partitions 3 m high
    lay.wall(x0 + WALL_T, 18.3, -20.3, 18.3, 3.0, WALL_T, PART, doors=[(-23.0, 1.2, 2.2)])
    lay.wall(-20.6, 18.6, -20.6, z1 - WALL_T, 3.0, WALL_T, PART)
    lay.wall(-20.3, 19.3, -14.3, 19.3, 3.0, WALL_T, PART, doors=[(-17.5, 1.2, 2.2)])
    lay.wall(-14.6, 19.6, -14.6, z1 - WALL_T, 3.0, WALL_T, PART)
    # trim band and roof edge (cosmetic)
    lay.mesh("box", (x0 + x1) / 2, z0 - 0.02, WAREHOUSE_TRIM, y=4.1, width=x1 - x0 + 0.1, height=0.3, depth=0.06)
    lay.mesh("box", (x0 + x1) / 2, z0 - 0.04, (0.12, 0.22, 0.34), y=3.55, width=6.0, height=0.5, depth=0.04)  # name board
    lay.mesh("box", -21.0, z0 - 0.05, (0.75, 0.62, 0.15), y=3.4, width=3.8, height=0.12, depth=0.06)   # loading door frame
    lay.mesh("box", -21.0, z0 - 0.3, (0.38, 0.40, 0.42), y=3.45, width=3.8, height=0.25, depth=0.6)    # roller door housing
    return (x0, z0, x1, z1, h + 0.25)


def harbour_office():
    """HM: the harbour master's office (x -41..-31, z 11..20): reception at the front, an office at the back."""
    x0, x1, z0, z1, h = -41.0, -31.0, 11.0, 20.0, 3.4
    lay.wall(x0, z0, x1, z0, h, WALL_T, HM_WALL, doors=[(-36.0, 1.6, 2.4)])
    lay.wall(x0, z1 - WALL_T, x1, z1 - WALL_T, h, WALL_T, HM_WALL, doors=[(-38.0, 1.4, 2.4)])
    lay.wall(x0, z0 + WALL_T, x0, z1 - WALL_T, h, WALL_T, HM_WALL, doors=[])
    lay.wall(x1 - WALL_T, z0 + WALL_T, x1 - WALL_T, z1 - WALL_T, h, WALL_T, HM_WALL, doors=[(17.6, 1.6, 2.4)])
    lay.wall(x0 + WALL_T, 15.4, x1 - WALL_T, 15.4, 3.0, WALL_T, PART, doors=[(-34.6, 1.2, 2.2)])
    lay.block(x0 - 0.3, h, z0 - 0.3, x1 + 0.3, h + 0.25, z1 + 0.3, HM_ROOF)
    for k in range(3):  # windows (cosmetic panes on the front)
        xx = -39.5 + k * 1.6 if k < 2 else -32.8
        lay.mesh("box", xx, z0 - 0.03, (0.55, 0.70, 0.80), y=1.0, width=1.1, height=1.2, depth=0.04)
        lay.mesh("box", xx, z0 - 0.06, (0.95, 0.95, 0.92), y=0.94, width=1.3, height=0.08, depth=0.08)
    lay.mesh("box", -36.0, z0 - 0.04, (0.12, 0.22, 0.34), y=2.7, width=3.2, height=0.45, depth=0.04)  # HARBOUR OFFICE board
    return (x0, z0, x1, z1, h + 0.25)


W1 = warehouse()
HM = harbour_office()


def market():
    """The fish market street between the two warehouses (x -9..9): counters facing the aisle under awnings."""
    for z in (12.6, 15.4, 20.0, 22.8):
        lay.prop("Fish Counter", -6.0, z, 90)                     # +Z (the customer side) faces the aisle (+X)
    for z0, z1 in ((11.0, 17.0), (18.6, 24.2)):
        lay.mesh("wedge", -6.6, (z0 + z1) / 2, AWNING[0 if z0 < 15 else 2], y=2.6, width=z1 - z0, height=0.5, depth=2.6, yaw=-90.0)
        for z in (z0, z1):
            lay.mesh("cylinder", -5.35, z, (0.30, 0.30, 0.32), width=0.1, height=2.65, depth=0.1, segments=6)
    # stall at the road end of each side, a hot dog cart at the alley end
    # strings of bulbs across the street
    for k, z in enumerate((12.0, 16.0, 20.0, 24.0)):
        lay.mesh("box", 0.0, z, (0.20, 0.20, 0.20), mirror=False, y=4.0, width=17.6, height=0.03, depth=0.03)
        for j in range(8):
            lay.mesh("sphere", -7.7 + j * 2.2, z, (1.0, 0.85, 0.5), mirror=False, y=3.8, width=0.16, height=0.2, depth=0.16, segments=6, detail=4)


market()

# containers: the horn yard (closed ones are 1 box, open ones 3: a tunnel)
CONT = []


def container(x, z, alongx, c, y=0.0, open_=False):
    lay.ops.append({"op": "Container", "x": x, "z": z, "alongX": alongx, "color": pk.col(c), **({"y": y} if y else {}), **({"open": True} if open_ else {})})
    lay.ops.append({"op": "Container", "x": -x, "z": z, "alongX": alongx, "color": pk.col(c), **({"y": y} if y else {}), **({"open": True} if open_ else {})})
    hx, hz = (3.0, 1.25) if alongx else (1.25, 3.0)
    for sx in (1, -1):
        lay.boxes.append((sx * x - hx, z - hz, sx * x + hx, z + hz, y, y + 2.6, c))
        if y < 1:
            lay.solids.append((sx * x - hx, z - hz, sx * x + hx, z + hz))
    CONT.append((x, z, alongx, y))


container(-48.0, -40.5, False, CONTAINERS[0]); container(-48.0, -40.5, False, CONTAINERS[1], y=2.6)
container(-48.0, -34.0, False, CONTAINERS[2])
container(-48.0, -23.5, False, CONTAINERS[3], open_=True)
container(-48.0, -17.0, False, CONTAINERS[4]); container(-48.0, -17.0, False, CONTAINERS[0], y=2.6)
container(-55.5, -37.0, False, CONTAINERS[5]); container(-55.5, -37.0, False, CONTAINERS[3], y=2.6)
container(-55.5, -21.5, False, CONTAINERS[1])
container(-57.0, -14.0, True, CONTAINERS[2])
container(-56.0, -43.1, True, CONTAINERS[4])


def gantry_crane():
    """A ship-to-shore crane straddling the horn quay: four solid legs, girders, a boom out over the water, the cab
    and a hook block hanging over the basin."""
    legs = [(-37.0, -24.5), (-31.0, -24.5), (-37.0, -16.5), (-31.0, -16.5)]
    for x, z in legs:
        lay.block(x - 0.3, 0, z - 0.3, x + 0.3, 9.0, z + 0.3, CRANE)
    for z in (-24.5, -16.5):
        lay.mesh("box", -34.0, z, CRANE, y=9.0, width=6.8, height=0.7, depth=0.7, material="Metal")
        lay.mesh("box", -34.0, z, CRANE, y=1.0, width=6.0, height=0.25, depth=0.35)
    for x in (-37.0, -31.0):
        lay.mesh("box", x, -20.5, CRANE, y=9.0, width=0.7, height=0.7, depth=8.6, material="Metal")
        lay.mesh("box", x, -20.5, (0.25, 0.25, 0.26), y=0.0, width=0.6, height=0.6, depth=9.4)          # bogies on the rail
    lay.mesh("box", -24.0, -20.5, CRANE, y=10.0, width=30.0, height=0.9, depth=1.6, material="Metal")   # boom
    lay.mesh("box", -40.5, -20.5, (0.30, 0.30, 0.32), y=9.7, width=4.0, height=2.2, depth=3.0)           # machinery house + counterweight
    lay.mesh("box", -33.5, -18.6, (0.92, 0.92, 0.90), y=7.6, width=2.0, height=1.6, depth=1.6)           # cab
    lay.mesh("box", -32.5, -18.6, (0.55, 0.70, 0.80), y=7.9, width=0.05, height=0.9, depth=1.4)
    lay.mesh("box", -14.5, -20.5, (0.25, 0.25, 0.26), y=10.9, width=1.2, height=0.6, depth=1.2)
    lay.mesh("cylinder", -19.0, -20.5, (0.15, 0.15, 0.15), y=4.6, width=0.06, height=5.4, depth=0.06, segments=4, **W)
    lay.mesh("box", -19.0, -20.5, CRANE, y=3.8, width=1.4, height=0.8, depth=0.9, **W)                  # hook block over the basin
    for z in (-24.5, -16.5):                                                                            # diagonal braces
        lay.mesh("box", -34.0, z, CRANE, y=4.5, width=7.6, height=0.25, depth=0.25, roll=48.0)


gantry_crane()

# the mole: a sea wall down its spine (blocks the long line across the bay), the lighthouse on the tip
lay.block(-0.35, 0, -21.0, 0.35, 2.3, -7.0, STONE)
lay.mesh("box", 0.0, -14.0, (0.70, 0.68, 0.64), mirror=False, y=2.3, width=1.0, height=0.15, depth=14.4, material="Concrete")
lay.prop("Lighthouse", 0.0, -31.4, 0, scale=1.35)

# ============================================================== props by rule
# ---- quay edges: bollards every 6 m, life rings every few (on the land side of every bay-side edge)
SHORE_RUNS = []
run = []
for a, b, t in BLUE_SEGS:
    if t == "shore":
        if not run:
            run = [a]
        run.append(b)
    else:
        if len(run) > 1:
            SHORE_RUNS.append(run)
        run = []
if len(run) > 1:
    SHORE_RUNS.append(run)


def clear_edge(x, z):
    return inset_ok(x, z, 0.4)


nb = 0
for k, rpts in enumerate(SHORE_RUNS):
    nb += pk.edge_row(lay, rpts, 6.0, None, inset=0.75, start=2.0, end=1.5, seed=40 + k,
                      every=["Mooring Bollard", "Mooring Bollard", "Life Ring Post", "Mooring Bollard", "Mooring Bollard", None])
# piers and the mole: bollards along both long sides
P1N = [[-30, -34], [-17, -34]]; P1S = [[-17, -29], [-30, -29]]; P2W = [[-15, -5], [-15, -19]]; P2E = [[-11, -19], [-11, -5]]
MOLE_W = [[-4, -6], [-4, -21]]
for k, e in enumerate((P1N, P1S, P2W, P2E, MOLE_W)):
    pk.edge_row(lay, e, 4.5, None, inset=0.6, start=1.6, end=1.4, seed=60 + k,
                every=["Mooring Bollard", "Flag on a Pole", "Mooring Bollard", "Pennant Pole"] if k < 4 else ["Mooring Bollard", "Life Ring Post", "Mooring Bollard", "Flickering Street Lamp"])
lay.prop("Gantry Hook", -18.8, -31.5, 90)          # the hoist over the pier tip, for landing catches from the boats

# ---- the road: lamps on both sides, hydrants and meters on the sidewalk, parked vans and trucks along the kerb
MID = [[-1.0, 4.0], [-24.0, 4.0]]
HORN = [[-41.0, -14.0], [-41.0, -43.0]]
CURVE = ROAD[1:-1]


def road_clear(x, z):
    return inset_ok(x, z, 0.8)


pk.kerb_row(lay, MID, 4.45, 12.0, ["Street Lamp", "Street Lamp", "Flickering Street Lamp"], seed=70, start=5.5, face="arm", clear=road_clear)
pk.kerb_row(lay, MID, -4.3, 12.0, ["Street Lamp"], seed=71, start=11.5, face="arm", clear=road_clear)
pk.kerb_row(lay, MID, 4.2, 12.0, ["Fire Hydrant", "Parking Meter", "Post Litter Bin", "Parking Meter"], seed=72, start=9.0, face="road", clear=road_clear)
pk.kerb_row(lay, CURVE, -4.4, 9.0, ["Street Lamp", "Bench", "Street Lamp", "Post Litter Bin"], seed=73, start=2.0, face="arm", clear=road_clear)
pk.kerb_row(lay, CURVE, 4.3, 10.0, ["Street Lamp", "Bus Stop Sign", "Street Lamp", "Fire Hydrant"], seed=74, start=3.0, face="arm", clear=road_clear)
pk.kerb_row(lay, HORN, -4.3, 10.0, ["Street Lamp", "Flickering Street Lamp"], seed=75, start=2.0, face="arm", clear=road_clear)
pk.kerb_row(lay, HORN, 4.4, 14.0, ["Street Lamp"], seed=76, start=6.0, face="arm", clear=road_clear)
lay.prop("Pedestrian Crossing Sign", -3.6, 8.0, 0)
lay.prop("Speed Limit Sign", -27.0, 8.4, 0)
lay.prop("Stop Sign", -38.6, -12.0, 270)


def not_loading(x, z):
    return not (-24.0 < x < -18.0) and road_clear(x, z)


VEH_MID = ["Van", "Pickup Truck", "Box Truck", "Hatchback", "Van"]
VEH_HORN = ["Van", "Pickup Truck", "Hatchback", "Van"]
pk.park_row(lay, [[-9.5, 4.0], [-26.0, 4.0]], 2.3, VEH_MID, seed=80, gap=1.6, empty=0.1, clear=not_loading)
pk.park_row(lay, [[-41.0, -14.5], [-41.0, -27.0]], 2.35, VEH_HORN, seed=81, gap=1.0, empty=0.0, clear=road_clear)
pk.park_row(lay, [[-41.0, -31.0], [-41.0, -43.5]], 2.35, VEH_HORN, seed=82, gap=1.0, empty=0.0, clear=road_clear)

# ---- behind the buildings: dumpsters with their trash, bins, wall units; roofs dressed
pk.dumpster_spot(lay, -16.5, W1[3], (0, 1), seed=90, bins=2, inside=lambda x, z: inset_ok(x, z, 0.5))
pk.dumpster_spot(lay, -24.5, W1[3], (0, 1), seed=91, bins=1, inside=lambda x, z: inset_ok(x, z, 0.5))
pk.dumpster_spot(lay, -34.5, HM[3], (0, 1), seed=92, bins=1, inside=lambda x, z: inset_ok(x, z, 0.5))
pk.wall_kit(lay, (-27.0, W1[3]), (-9.0, W1[3]), (0, 1), [("Electric Meter", 0.0), ("Vent Grille", 3.0), ("Window AC Unit", 2.6), ("Gas Meter", 0.0), ("Vent Grille", 3.2)], seed=94,
            avoid=[(15.5, 1.0), (10.5, 1.8), (2.5, 1.8)])
pk.wall_kit(lay, (-27.0, W1[1]), (-9.0, W1[1]), (0, -1), [("Vent Grille", 3.6), ("Vent Grille", 3.6)], seed=95, avoid=[(6.0, 2.4), (14.0, 1.2)])
pk.wall_kit(lay, (-41.0, HM[3]), (-31.0, HM[3]), (0, 1), [("Window AC Unit", 2.2), ("Electric Meter", 0.0), ("Vent Grille", 2.6)], seed=96, avoid=[(3.0, 1.0)])
pk.wall_kit(lay, (-9.0, 11.0), (-9.0, 24.0), (1, 0), [("Neon Sign", 3.1)], seed=97)
pk.roof_kit(lay, W1[0], W1[1], W1[2], W1[3], W1[4], seed=100,
            items=[("Rooftop HVAC Unit", 1), ("Rooftop Condenser", 2), ("Roof Turbine Vent", 3), ("Turbine Vent", 2), ("Mushroom Vent", 3),
                   ("Exhaust Fan", 1), ("Skylight", 3), ("Air Duct", 1), ("TV Antenna", 1), ("Satellite Dish", 1)])
pk.roof_kit(lay, HM[0], HM[1], HM[2], HM[3], HM[4], seed=101,
            items=[("Split AC Unit", 2), ("Satellite Dish", 1), ("TV Antenna", 1), ("Anemometer", 1), ("Mushroom Vent", 2)])
lay.prop("Weather Vane", -39.6, 12.4, 0, y=HM[4])
lay.prop("Radio Mast", -33.4, 18.0, 0, y=HM[4])

# ---- inside: the warehouse store floor, its office and break room, the harbour office's two rooms
pk.room(lay, -26.7, 18.6, -20.6, 24.2, "office", doors=[(-23.0, 18.3)], seed=110, middle=2)
pk.room(lay, -20.3, 19.6, -14.6, 24.2, "break", doors=[(-17.5, 19.3)], seed=111, middle=1)
pk.room(lay, -40.7, 11.3, -31.3, 15.4, "office", doors=[(-36.0, 11.0), (-34.6, 15.4), (-31.0, 17.6)], seed=112, middle=0,
        wall_items=["Reception Desk", "Office Plant", "Water Cooler", "Filing Cabinet", "Filing Cabinet", "Plastic Chair", "Plastic Chair"])
pk.room(lay, -40.7, 15.7, -31.3, 19.7, "office", doors=[(-34.6, 15.4), (-38.0, 20.0), (-31.0, 17.6)], seed=113, middle=2,
        wall_items=["Filing Cabinet", "Filing Cabinet", "Metal Shelving", "Safe", "Whiteboard", "Office Plant"])
pk.room(lay, -26.7, 10.8, -9.3, 18.3, "store", doors=[(-21.0, 10.5), (-13.0, 10.5), (-27.0, 14.2), (-9.0, 16.5)], seed=114, middle=0,
        wall_items=["Metal Shelving", "Metal Shelving", "Metal Shelving", "Pallet Stack", "Archive Boxes", "Metal Shelving", "Cardboard Stack", "Metal Lockers"])
lay.prop("Forklift", -20.5, 15.4, 90)
lay.prop("Pallet Stack", -16.4, 13.6, 0); lay.prop("Wooden Crate", -16.0, 15.6, 0); lay.prop("Small Crate", -16.0, 15.6, 0, y=0.9)
lay.prop("Freight Hoist", -10.4, 22.8, 270)
lay.prop("Ceiling Fan", -23.6, 21.4, 0, y=2.3); lay.prop("Ceiling Fan", -17.4, 21.9, 0, y=2.3)

# ---- the fish market: ice chests, boxes and crates behind the counters, signs
for z in (12.0, 16.0, 19.4, 23.4):
    lay.prop("Fish Box Stack", -7.9, z, 90)
lay.prop("Chest Freezer", -7.9, 14.1, 90); lay.prop("Chest Freezer", -7.9, 21.6, 90)
for k, z in enumerate((12.6, 15.4, 20.0, 22.8)):
    lay.prop("Fish Box", -4.6 if k % 2 else -4.9, z + 0.9, 30.0 * k)
lay.prop("Swinging Shop Sign", -8.2, 10.9, 90)
lay.prop("Fish Box Stack", -7.7, 25.6, 0)
lay.prop("Hot Dog Cart", -6.3, 26.6, 0)

# ---- the yard: forklifts, pallets, drums, cable spools, a floodlight tower, a portable toilet
for name, x, z, yaw in [("Forklift", -44.0, -10.4, 180), ("Pallet Stack", -58.5, -11.0, 0), ("Pallet Stack", -57.0, -9.4, 90), ("Pallet", -60.5, -9.0, 15),
                        ("Wooden Crate", -63.0, -16.5, 0), ("Wooden Crate", -63.0, -15.4, 0), ("Oil Drum", -63.4, -36.0, 0), ("Rusty Drum", -62.8, -35.5, 0),
                        ("Oil Drum", -63.2, -34.9, 0), ("Cable Spool", -52.0, -42.6, 0), ("Pipe Stack", -58.0, -43.2, 0), ("Floodlight Tower", -60.0, -42.6, 0),
                        ("Portable Toilet", -65.5, -24.0, 90), ("Gas Cylinders", -64.8, -21.0, 90), ("Concrete Block", -45.6, -29.0, 90),
                        ("Pallet Stack", -51.8, -13.8, 0), ("Running Generator", -58.8, -27.8, 90), ("Warning Beacon", -46.2, -44.0, 0)]:
    lay.prop(name, x, z, yaw)
lay.prop("Small Crate", -63.0, -15.95, 0, y=0.9)

# ---- the horn quay: fish boxes, nets, pots, pallets under the crane
for name, x, z, yaw in [("Pallet Stack", -34.2, -22.6, 0), ("Wooden Crate", -33.0, -18.4, 0), ("Lobster Pot Stack", -32.0, -42.6, 90),
                        ("Fish Box Stack", -35.8, -38.6, 90), ("Upturned Dinghy", -35.6, -9.2, 300), ("Lobster Pot Stack", -31.5, -11.5, 0),
                        ("Bench", -26.0, -3.0, 0), ("Life Ring Post", -28.3, -5.4, 210), ("Oil Drum", -35.8, -33.0, 0), ("Rusty Drum", -36.4, -32.4, 0)]:
    lay.prop(name, x, z, yaw)
# ---- bay-bottom quay, pier P2 and the mole
for name, x, z, yaw in [("Lobster Pot Stack", -19.4, -3.2, 0), ("Fish Box Stack", -8.6, -3.2, 0), ("Pallet Stack", -17.0, -0.6, 0),
                        ("Bench", -2.6, -10.0, 90), ("Bench", -2.6, -18.0, 90), ("Fish Box Stack", -2.9, -21.6, 90),
                        ("Upturned Dinghy", -6.5, -0.8, 0)]:
    lay.prop(name, x, z, yaw)

# ---- depot lot (west of the harbour office): trucks and a trailer lined up, a generator, a skip, toilets
for name, x, z, yaw in [("Box Truck", -55.5, -2.5, 90), ("Truck Cab", -60.5, -1.5, 90), ("Utility Trailer", -51.0, -3.5, 90),
                        ("Diesel Generator", -60.5, 9.0, 0), ("Builders Skip", -52.0, 14.5, 20), ("Portable Toilet", -46.2, 18.0, 0),
                        ("Pad Transformer", -64.0, 2.5, 90), ("Tyre Stack", -48.5, 11.0, 0), ("Concrete Pipe", -55.0, 18.5, 90),
                        ("Park Picnic Table", -44.0, 23.8, 10), ("Utility Pole", -45.5, -5.5, 90)]:
    lay.prop(name, x, z, yaw)

# ---- litter where people stand: round the benches, the hot dog cart, the bus stop
for k, (x, z) in enumerate([(-26.0, -3.0), (-2.6, -10.0), (-6.3, 26.6), (-44.0, 23.8), (-52.0, 14.5)]):
    pk.trash_ring(lay, x, z, 0.9, 2.4, 3, seed=120 + k, inside=lambda x, z: inset_ok(x, z, 0.5))
# ---- quay life: ducks by the water's edge, pigeons
for name, x, z, yaw in [("Mallard Duck", -28.6, -6.2, 40), ("Hen Duck", -29.4, -8.2, 80), ("Duck Family", -19.5, -4.2, 0),
                        ("Pigeons", -12.0, -0.2, 0), ("Pigeons", -33.0, -27.0, 30), ("Mallard Duck", -3.2, -24.0, 270), ("Rope Coil", -29.2, -31.0, 0),
                        ("Fishing Net Pile", -21.5, -31.6, 10), ("Rope Coil", -13.0, -17.6, 0), ("Fish Box", -12.4, -11.0, 0), ("Fishing Net Pile", -10.8, -2.4, 80)]:
    lay.prop(name, x, z, yaw)

# ---- on the water: moored boats, buoys, a bobber, ducks
FLOAT = [("Fishing Trawler", -23.5, -36.6, 0.0), ("Harbour Launch", -24.0, -26.6, 180.0), ("Rowing Boat", -17.6, -10.0, 90.0),
         ("Rowing Boat", -8.6, -12.0, 275.0), ("Harbour Launch", -6.5, -20.5, 90.0), ("Fishing Trawler", -27.6, -45.0 - 2.2, 180.0),
         ("Buoy", -12.0, -40.0, None), ("Buoy", -36.0, -60.0, None), ("Buoy", -8.0, -52.0, None),
         ("Fishing Bobber", -24.5, -11.5, None), ("Duck on a Pond", -20.0, -10.5, None), ("Duck on a Pond", -9.0, -27.0, None)]
for k, (name, x, z, yaw) in enumerate(FLOAT):
    assert not inside(x, z) and edge_dist(x, z) > 1.0, (name, x, z)
    pk.afloat(lay, name, x, z, WATER_Y, yaw=yaw, seed=130 + k)

# ============================================================== scattered solid cover (mirrored, off the lanes and keep-outs)


def scatter(names, region, count, seed, gap=1.15, yaws=(0, 90, 180, 270), jitter=0.0, margin=1.6):
    rnd = random.Random(seed)
    placed = 0
    tries = 0
    while placed < count and tries < count * 80:
        tries += 1
        name = rnd.choice(names)
        x = rnd.uniform(region[0], region[2])
        z = rnd.uniform(region[1], region[3])
        if x > -1.5:
            continue
        yaw = (rnd.choice(yaws) + (rnd.uniform(-jitter, jitter) if jitter else 0)) % 360
        r = lib.foot(name, x, z, yaw)
        if not all(inset_ok(cx, cz, margin) for cx, cz in ((r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3]))):
            continue
        if not lay.free(r, gap) or lay.on_lane(r) or lay.in_keepout(r, 0.6):
            continue
        if lay.prop(name, round(x, 2), round(z, 2), yaw, gap=gap, lanes=True):
            placed += 1
    return placed


YARD = ["Pallet Stack", "Wooden Crate", "Oil Drum", "Rusty Drum", "Cable Spool", "Tyre Stack", "Gas Cylinders", "Pallet", "Concrete Block", "Small Crate", "Plank Pile"]
QUAYSIDE = ["Lobster Pot Stack", "Fish Box Stack", "Wooden Crate", "Pallet Stack", "Oil Drum", "Cable Spool", "Mooring Bollard", "Small Crate"]
ALLEY = ["Wheelie Bin", "Wheelie Bin Grey", "Recycling Bin", "Cardboard Stack", "Pallet", "Wooden Crate", "Trash Can", "Oil Drum"]
LOT = ["Tyre Stack", "Concrete Block", "Brick Pile", "Rubble Pile", "Log", "Tree Stump", "Wooden Crate", "Oil Drum", "Pallet Stack", "Car On Bricks", "Burnt Out Car"]
STORE = ["Pallet Stack", "Wooden Crate", "Cardboard Stack", "Cardboard Box Large", "Archive Boxes", "Small Crate"]
SC = 0
SC += scatter(YARD, (-64, -44, -45, -10), 22, 201)
SC += scatter(QUAYSIDE, (-37.5, -44.5, -30.5, -8), 10, 202, gap=1.0)
SC += scatter(QUAYSIDE, (-29, -5, -2, 0.4), 8, 203, gap=1.0)
SC += scatter(ALLEY, (-30, 25.0, -2, 33), 10, 204, gap=1.0)
SC += scatter(LOT, (-66, -8, -42, 24), 18, 205, yaws=(0, 30, 60, 90, 120, 150), jitter=6)
SC += scatter(STORE, (-26.5, 11, -9.5, 18.2), 8, 206, gap=1.1)
SC += scatter(["Bench", "Planter", "Trash Can", "Lobster Pot Stack", "Wooden Crate"], (-31, -15, -21, -5), 6, 207, gap=1.0)

# ============================================================== spawns and waypoints
for x, z in BLUE_SP + RED_SP + FFA_SP:
    ops.append({"op": "Spawn", "x": x, "z": z})
ops.append({"op": "AutoWaypoints", "spacing": 6})
WPS = [(-27.0, -31.5), (-21.5, -31.5), (-13.0, -8.0), (-13.0, -15.5), (-2.2, -9.0), (-2.2, -16.0), (-2.2, -23.0), (0.0, -27.0),
       (-23.6, 21.4), (-17.4, 22.0), (-36.0, 13.2), (-36.0, 17.8), (-29.0, 18.0), (-33.5, -20.5), (-51.8, -29.0), (-12.0, 22.5)]
for x, z in WPS:
    ops.append({"op": "Waypoint", "x": x, "z": z})
    if abs(x) > 0.01:
        ops.append({"op": "Waypoint", "x": -x, "z": z})

# ============================================================== cosmetic clutter (the game's Clutter op; one per side, unmirrored)
LITTER = ["Litter", "Litter", "Newspaper", "Soda Cans", "Bottles", "Bottle", "Plastic Bag", "Flattened Cardboard", "Cardboard Box Small", "Trash Bag"]
QUAY_BITS = ["Rope Coil", "Fish Box", "Fishing Net Pile", "Litter", "Plank", "Bottle", "Soda Cans", "Rope Coil", "Flattened Cardboard", "Pigeons"]
YARD_BITS = ["Tyre", "Cinder Blocks", "Plank", "Flattened Cardboard", "Loose Bricks", "Litter", "Car Wheel", "Rubble", "Jerry Can", "Soda Cans"]
LOT_BITS = ["Tyre", "Cinder Blocks", "Loose Bricks", "Rubble", "Car Door", "Car Wheel", "Plank", "Litter", "Bottles", "Newspaper"]
STORE_BITS = ["Cardboard Box Small", "Flattened Cardboard", "Open Cardboard Box", "Plank", "Paint Cans", "Toolbox", "Bucket", "Broom", "Litter"]
for s in (1, -1):
    def X(v):
        return v * s
    ops.append({"op": "Clutter", "props": LITTER, "x0": X(-30), "z0": 28.7, "x1": X(-1), "z1": 28.7, "width": 5.0, "count": 120, "seed": 301 + (s < 0)})
    ops.append({"op": "Clutter", "props": QUAY_BITS, "x0": X(-27), "z0": -2.2, "x1": X(-4.5), "z1": -2.2, "width": 5.0, "count": 70, "seed": 303 + (s < 0)})
    ops.append({"op": "Clutter", "props": QUAY_BITS, "x0": X(-33.8), "z0": -44.5, "x1": X(-33.8), "z1": -8.0, "width": 6.4, "count": 90, "seed": 305 + (s < 0)})
    ops.append({"op": "Clutter", "props": QUAY_BITS, "x0": X(-29.5), "z0": -31.5, "x1": X(-17.5), "z1": -31.5, "width": 4.4, "count": 30, "seed": 307 + (s < 0)})
    ops.append({"op": "Clutter", "props": QUAY_BITS, "x0": X(-13), "z0": -5.5, "x1": X(-13), "z1": -18.5, "width": 3.4, "count": 25, "seed": 309 + (s < 0)})
    ops.append({"op": "Clutter", "props": YARD_BITS, "x0": X(-64), "z0": -44.5, "x1": X(-44.6), "z1": -9.0, "count": 260, "seed": 311 + (s < 0)})
    ops.append({"op": "Clutter", "props": LOT_BITS, "x0": X(-67), "z0": -8.0, "x1": X(-42), "z1": 25.0, "count": 170, "seed": 313 + (s < 0)})
    ops.append({"op": "Clutter", "props": ["Newspaper", "Litter", "Soda Cans", "Plastic Bag", "Bottle", "Storm Drain"], "x0": X(-1), "z0": 4.0, "x1": X(-24), "z1": 4.0,
                "width": 6.8, "count": 50, "seed": 315 + (s < 0)})
    ops.append({"op": "Clutter", "props": LITTER, "x0": X(-38), "z0": -12.0, "x1": X(-24), "z1": 0.0, "count": 50, "seed": 317 + (s < 0)})
    ops.append({"op": "Clutter", "props": STORE_BITS, "x0": X(-26.5), "z0": 10.9, "x1": X(-9.4), "z1": 18.2, "count": 45, "seed": 319 + (s < 0)})
    ops.append({"op": "Clutter", "props": ["Fish Box", "Fish Box", "Litter", "Newspaper", "Cardboard Box Small", "Pigeons"], "x0": X(-4.8), "z0": 10.6, "x1": X(-0.5), "z1": 25.0,
                "count": 30, "seed": 321 + (s < 0)})
    ops.append({"op": "Clutter", "props": LITTER, "x0": X(-41), "z0": 20.4, "x1": X(-27), "z1": 28.0, "count": 40, "seed": 323 + (s < 0)})
    ops.append({"op": "Clutter", "props": ["Litter", "Bottle", "Rope Coil", "Fish Box", "Soda Cans"], "x0": X(-3.8), "z0": -6.0, "x1": X(-0.6), "z1": -29.0, "count": 25, "seed": 325 + (s < 0)})

# ============================================================== foliage
INNER = [[round(p[0], 2), round(p[1], 2)] for p in OUTLINE]


def strip(pts, w):
    """A polygon strip w wide on the inside of a land polyline."""
    left = pk.offset_line(pts, w)
    right = pk.offset_line(pts, -w)
    # keep the side that's inside
    mid = pts[len(pts) // 2]
    li = pk.offset_line([pts[len(pts) // 2 - 1], mid, pts[len(pts) // 2 + 1]], 1.0)[1]
    inner = left if inside(*li) else right
    return [[round(p[0], 2), round(p[1], 2)] for p in pts] + [[round(p[0], 2), round(p[1], 2)] for p in reversed(inner)]


land_blue = [p for p in land]
land_red = [[-p[0], p[1]] for p in land]
WALLFOOT = [strip(land_blue[2:-2], 1.5), strip(land_red[2:-2], 1.5)]
LOT_BLUE = [[-66, -8], [-44.5, -8], [-44.5, -6.5], [-42.5, 8], [-42.0, 26], [-48, 22], [-56, 19], [-63, 9]]
LOT_RED = [[-x, z] for x, z in reversed(LOT_BLUE)]
CORNER_BLUE = [[-36.5, -12.0], [-31.0, -12.0], [-30.5, -5.5], [-28.0, -2.5], [-31.5, -1.0], [-34.5, -5.0]]
foliage = {
    "seed": 5,
    "surfaces": {"Concrete": 0.09},
    "paths": [{"points": [list(map(lambda v: round(v, 2), p)) for p in pts], "width": w} for pts, w in LANES if pts is not ROAD],
    "include": [{"shape": "polygon", "points": poly} for poly in WALLFOOT + [CORNER_BLUE, [[-x, z] for x, z in reversed(CORNER_BLUE)]]],
    "regions": [
        {"type": "grass", "density": 4.5, "polygon": INNER, "patchiness": 0.5, "color": "#7f8a4a", "scale": [0.6, 1.05]},
        {"type": "shrub", "density": 0.25, "polygon": INNER, "patchiness": 0.7, "color": "#6b7a3c"},
        {"type": "grass", "density": 9.0, "polygon": LOT_BLUE, "patchiness": 0.45, "color": "#768a3e"},
        {"type": "grass", "density": 9.0, "polygon": LOT_RED, "patchiness": 0.45, "color": "#768a3e"},
        {"type": "shrub", "density": 1.2, "polygon": LOT_BLUE}, {"type": "shrub", "density": 1.2, "polygon": LOT_RED},
        {"type": "bush", "density": 0.35, "polygon": LOT_BLUE, "scale": [0.9, 1.6]}, {"type": "bush", "density": 0.35, "polygon": LOT_RED, "scale": [0.9, 1.6]},
        {"type": "grass", "density": 12.0, "polygon": WALLFOOT[0], "color": "#6a7d3a"}, {"type": "grass", "density": 12.0, "polygon": WALLFOOT[1], "color": "#6a7d3a"},
        {"type": "bush", "density": 0.9, "polygon": WALLFOOT[0], "patchiness": 0.6, "scale": [0.7, 1.4]},
        {"type": "bush", "density": 0.9, "polygon": WALLFOOT[1], "patchiness": 0.6, "scale": [0.7, 1.4]},
        {"type": "shrub", "density": 1.6, "polygon": WALLFOOT[0]}, {"type": "shrub", "density": 1.6, "polygon": WALLFOOT[1]},
        {"type": "grass", "density": 6.0, "polygon": CORNER_BLUE, "color": "#7a8c44"},
        {"type": "grass", "density": 6.0, "polygon": [[-x, z] for x, z in reversed(CORNER_BLUE)], "color": "#7a8c44"},
    ],
    "items": [],
}
TREES = [("tree", -57.5, 13.0, 7.0), ("tree", -64.5, -4.0, 6.5), ("tree", -49.5, 20.0, 6.0), ("tree", -43.5, 5.0, 5.5), ("pine", -62.5, 4.0, 8.5),
         ("tree", -34.0, -6.8, 6.0), ("tree", -31.0, 25.0, 5.5), ("tree", -45.0, -14.6, 6.0), ("tree", -27.5, 8.9, 5.5), ("tree", -3.8, 9.2, 5.0),
         ("tree", -15.5, 9.4, 5.5)]
for t, x, z, h in TREES:
    assert inset_ok(x, z, 2.0), ("tree too near the edge", x, z)
    for sx in (1, -1):
        foliage["items"].append({"type": t, "x": x * sx, "z": z, "height": h})
        lay.solids.append((x * sx - 0.4, z - 0.4, x * sx + 0.4, z + 0.4))
        lay.boxes.append((x * sx - 0.3, z - 0.3, x * sx + 0.3, z + 0.3, 0, h, (0.3, 0.2, 0.1)))

# ============================================================== the map
mp = {
    "format": "call-of-slop-map/1",
    "name": "Dockside",
    "description": "A working harbour curving round a bay: piers, a container yard under a crane, warehouses you can fight through, a fish market and a lighthouse on the mole.",
    "author": "Call of Slop",
    "order": 405,
    "maxPlayers": 12,
    "sky": "#9aa3a8",
    "skybox": {"preset": "overcast", "clouds": {"coverage": 0.8, "speed": 14, "heading": 70}, "haze": {"color": "#b8c0c4", "strength": 0.25}},
    "boundary": {"shape": "polygon", "points": [[round(p[0], 3), round(p[1], 3)] for p in OUTLINE], "edge": "none", "barrierHeight": 0.1, "groundMargin": 14},
    "terrain": {
        "cell": 0.5, "margin": 30,
        "surface": {"grass": "#6d7a48", "dirt": "#6a5d48", "rock": "#5c5a55", "sand": "#7d7560", "sandBelow": -1.0, "variation": 0.08},
        "features": TERRAIN_FEATURES,
    },
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": ops,
    "foliage": foliage,
}

for x, z in BLUE_SP + RED_SP + FFA_SP + [(h[0], h[1]) for h in HILLS] + FLAGS:
    assert inset_ok(x, z, 1.5), (x, z)


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

# ============================================================== report
props = lay.props
nsolid = sum(1 for p in props if lib[p[0]]["solid"])
nbrk = sum(1 for p in props if lib[p[0]]["brk"])
nblocks = sum(1 for o in ops if o["op"] == "Block")
ncont = sum(1 for o in ops if o["op"] == "Container")
boxes_est = nblocks + sum(3 if o.get("open") else 1 for o in ops if o["op"] == "Container")
by_cat = {}
for p in props:
    c = lib[p[0]]["cat"]
    k = by_cat.setdefault(c, [0, 0])
    k[0 if lib[p[0]]["solid"] else 1] += 1
print(f"area {pk.poly_area(OUTLINE):.0f} m2, outline {len(OUTLINE)} points; blocks {nblocks}, containers {ncont}, boxes ~{boxes_est} (+{len(foliage['items'])} trunks); "
      f"meshes {lay.meshes}; hand props {len(props)} (solid {nsolid}, breakable {nbrk}, scattered {SC * 2}); file {os.path.getsize(OUT_JSON) // 1024} KB")
print("by category (solid, cosmetic):", ", ".join(f"{c} {v[0]}/{v[1]}" for c, v in sorted(by_cat.items())))

# the quay edge: level ground from the outline out to the ledge, water beyond (checked on every bay-side segment)
dry, wet = 0, 0
for a, b, t in SEGS:
    if t == "land":
        continue
    dx, dz = b[0] - a[0], b[1] - a[1]
    Ls = math.hypot(dx, dz)
    lx, lz = dz / Ls, -dx / Ls
    if inside((a[0] + b[0]) / 2 + lx * 0.3, (a[1] + b[1]) / 2 + lz * 0.3):
        lx, lz = -lx, -lz
    for k in range(1, int(Ls / 0.5)):
        x, z = a[0] + dx * k / int(Ls / 0.5), a[1] + dz * k / int(Ls / 0.5)
        if edge_dist(x, z) < 0.3 and min(math.hypot(x - a[0], z - a[1]), math.hypot(x - b[0], z - b[1])) > 0.6:
            for o in (-0.5, 0.0, 0.6):
                if abs(ground_at(x + lx * o, z + lz * o)) > 0.01:
                    dry += 1
            if ground_at(x + lx * 2.0, z + lz * 2.0) > SEA_BED + 0.5 and not inside(x + lx * 2.0, z + lz * 2.0) and edge_dist(x + lx * 2.0, z + lz * 2.0) > 1.6:
                wet += 1
                print("  dry water at", round(x + lx * 2.0, 1), round(z + lz * 2.0, 1), t)
print(f"quay edge: {dry} samples not level within the ledge, {wet} with no water 2 m out")
low = 0
for gx in range(-140, 141):
    for gz in range(-90, 70):
        x, z = gx * 0.5, gz * 0.5
        if inside(x, z) and abs(ground_at(x, z)) > 0.001:
            low += 1
            if low < 5:
                print("  low ground inside at", x, z, round(ground_at(x, z), 2))
print(f"inside the outline: {low} samples (every 0.5 m) off the quay level")

# solid props over spawns/hills/flags?
bad = []
for name, x, z, yaw, y in props:
    if lib[name]["solid"] and y < 0.5:
        r = lib.foot(name, x, z, yaw)
        for (kx, kz, kr) in [(x_, z_, 1.0) for x_, z_ in BLUE_SP + RED_SP + FFA_SP] + [(h[0], h[1], h[2]) for h in HILLS] + [(fx, fz, 2.0) for fx, fz in FLAGS]:
            dx = max(r[0] - kx, 0, kx - r[2]); dz = max(r[1] - kz, 0, kz - r[3])
            if math.hypot(dx, dz) < kr:
                bad.append((name, round(x, 1), round(z, 1)))
for b in bad:
    print("WARN solid prop on an objective:", b)
outside = [(n, round(x, 1), round(z, 1)) for n, x, z, yaw, y in props if not inside(x, z) and y > -0.3]
for o in outside:
    print("WARN prop outside the outline:", o)

if OUT_PNG:
    from PIL import Image, ImageDraw
    S = 7
    W_, H_ = int(150 * S), int(90 * S)

    def P(x, z):
        return ((x + 75) * S, (z + 50) * S)
    img = Image.new("RGB", (W_, H_), (40, 70, 85))
    d = ImageDraw.Draw(img)
    d.polygon([P(*p) for p in OUTLINE], fill=(150, 148, 140))
    for a, b, t in SEGS:
        d.line([P(*a), P(*b)], fill={"land": (150, 60, 50), "shore": (230, 230, 230), "pier": (160, 110, 60), "mole": (200, 200, 120)}[t], width=3)
    for pts, w in LANES:
        d.line([P(*p) for p in pts], fill=(175, 172, 160), width=max(1, int(w * S)))
    for x0, z0, x1, z1, y0, y1, c in lay.boxes:
        shade = tuple(int(v * 255 * (0.6 if y1 < 1.3 else 1.0)) for v in c)
        if y0 < 1.7:
            d.rectangle([P(x0, z0), P(x1, z1)], fill=shade, outline=(20, 20, 20))
    for name, x, z, yaw, y in props:
        r = lib.foot(name, x, z, yaw)
        if lib[name]["solid"]:
            col_ = (150, 90, 40) if lib[name]["brk"] else (80, 80, 95)
            if y > 2:
                col_ = (90, 160, 90)
            if y < -0.3:
                col_ = (220, 220, 255)
            d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=col_)
        else:
            d.rectangle([P(r[0], r[1]), P(r[2], r[3])], outline=(230, 200, 60) if y < -0.3 else (120, 200, 230))
    for x, z in BLUE_SP:
        d.ellipse([P(x - 0.7, z - 0.7), P(x + 0.7, z + 0.7)], fill=(40, 80, 230))
    for x, z in RED_SP:
        d.ellipse([P(x - 0.7, z - 0.7), P(x + 0.7, z + 0.7)], fill=(230, 40, 40))
    for x, z in FFA_SP:
        d.ellipse([P(x - 0.7, z - 0.7), P(x + 0.7, z + 0.7)], fill=(240, 240, 240))
    for x, z, r in HILLS:
        d.ellipse([P(x - r, z - r), P(x + r, z + r)], outline=(255, 230, 0), width=3)
    for x, z in FLAGS:
        d.rectangle([P(x - 0.6, z - 0.6), P(x + 0.6, z + 0.6)], fill=(255, 255, 255))
    for gx in range(-70, 71, 10):
        d.text(P(gx, -49), str(gx), fill=(255, 255, 255))
    for gz in range(-40, 41, 10):
        d.text(P(-74, gz), str(gz), fill=(255, 255, 255))
    # sight lines between the two teams' spawns at eye height: blocked by tall boxes and solid props taller than 1.6 m
    tall = [(b[0], b[1], b[2], b[3]) for b in lay.boxes if b[5] > 1.6 and b[4] < 1.6]
    tall += [lib.foot(n, x, z, yw) for n, x, z, yw, y in props if lib[n]["solid"] and lib[n]["h"] > 1.6 and -0.3 < y < 0.5]

    def blocked(a, b):
        Lb = math.hypot(b[0] - a[0], b[1] - a[1])
        k = int(Lb / 0.25)
        for i in range(1, k):
            x = a[0] + (b[0] - a[0]) * i / k
            z = a[1] + (b[1] - a[1]) * i / k
            for r in tall:
                if r[0] <= x <= r[2] and r[1] <= z <= r[3]:
                    return True
        return False
    vis = 0
    for a in BLUE_SP:
        for b in RED_SP:
            if not blocked(a, b):
                vis += 1
                d.line([P(*a), P(*b)], fill=(255, 0, 255), width=1)
    print("blue-red spawn lines of sight:", vis)
    img.save(OUT_PNG)
