#!/usr/bin/env python3
"""Generator for Maps/Cluttered/02-CrescentBazaar.json (medium bean map).
Usage: python3 tools/cluttered/crescent_bazaar.py <repo root> [preview.png]

Mirror symmetry about x = 0 (west half = Blue, east half = Red). Everything solid is placed in pairs (x, z, yaw) and
(-x, z, -yaw). Cosmetic litter is left to the game's Clutter op (unmirrored, it has no gameplay effect).
Writes the JSON and a top-down PNG preview.
"""
import json, math, random, sys, glob, os

REPO = sys.argv[1]
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/02-CrescentBazaar.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else "/tmp/preview.png"

# ---------------------------------------------------------------- outline (centripetal Catmull-Rom, closed)
CTRL = [[-68.5, 0], [-55.5, -30], [-28, -38.5], [0, -22.5], [28, -38.5], [55.5, -30], [68.5, 0], [51.5, 32], [19, 43], [-19, 43], [-51.5, 32]]

def catmull(pts, step=0.25):
    n = len(pts); out = []
    for i in range(n):
        p0, p1, p2, p3 = pts[(i - 1) % n], pts[i], pts[(i + 1) % n], pts[(i + 2) % n]
        def tj(ti, a, b):
            return ti + max(1e-4, math.hypot(b[0] - a[0], b[1] - a[1])) ** 0.5
        t0 = 0; t1 = tj(t0, p0, p1); t2 = tj(t1, p1, p2); t3 = tj(t2, p2, p3)
        seg = math.hypot(p2[0] - p1[0], p2[1] - p1[1]); k = max(2, int(seg / step))
        for s in range(k):
            t = t1 + (t2 - t1) * s / k
            def lerp(a, b, ta, tb):
                return [(tb - t) / (tb - ta) * a[0] + (t - ta) / (tb - ta) * b[0], (tb - t) / (tb - ta) * a[1] + (t - ta) / (tb - ta) * b[1]]
            A1 = lerp(p0, p1, t0, t1); A2 = lerp(p1, p2, t1, t2); A3 = lerp(p2, p3, t2, t3)
            B1 = lerp(A1, A2, t0, t2); B2 = lerp(A2, A3, t1, t3)
            out.append(lerp(B1, B2, t1, t2))
    return out

OUTLINE = catmull(CTRL)

def inside(x, z):
    c = False; n = len(OUTLINE)
    for i in range(n):
        ax, az = OUTLINE[i]; bx, bz = OUTLINE[(i + 1) % n]
        if (az > z) != (bz > z) and x < ax + (z - az) * (bx - ax) / (bz - az):
            c = not c
    return c

def edge_dist(x, z):
    best = 1e9; n = len(OUTLINE)
    for i in range(0, n):
        ax, az = OUTLINE[i]; bx, bz = OUTLINE[(i + 1) % n]
        dx, dz = bx - ax, bz - az; L = dx * dx + dz * dz
        t = 0 if L == 0 else max(0, min(1, ((x - ax) * dx + (z - az) * dz) / L))
        best = min(best, math.hypot(ax + t * dx - x, az + t * dz - z))
    return best

def inset_ok(x, z, m):
    return inside(x, z) and edge_dist(x, z) >= m

def area():
    a = 0; n = len(OUTLINE)
    for i in range(n):
        ax, az = OUTLINE[i]; bx, bz = OUTLINE[(i + 1) % n]; a += ax * bz - bx * az
    return abs(a) / 2

# ---------------------------------------------------------------- props library footprints
PROPS = {}
for f in glob.glob(os.path.join(REPO, "Props/*/*.json")):
    d = json.load(open(f))
    xs, zs, ys = [], [], []
    for p in d["parts"]:
        s = p["size"]; o = p.get("offset", [0, s[1] / 2, 0])
        r = max(s[0], s[2]) if ("rotation" in p or "yaw" in p) else None
        hx = (r if r else s[0]) / 2; hz = (r if r else s[2]) / 2
        xs += [o[0] - hx, o[0] + hx]; zs += [o[2] - hz, o[2] + hz]; ys.append(o[1] + s[1] / 2)
    PROPS[d["name"]] = dict(solid=d.get("collision", "solid") == "solid", brk="breakable" in d,
                            w=max(xs) - min(xs), d=max(zs) - min(zs), h=max(ys), cx=(max(xs) + min(xs)) / 2, cz=(max(zs) + min(zs)) / 2)

# ---------------------------------------------------------------- map state
ops = []          # ops in order
boxes = []        # (x0, z0, x1, z1, h, color) solid boxes for preview/occupancy/sightlines
solids = []       # occupancy footprints (x0, z0, x1, z1) for props + boxes
meshes = 0
props_hand = []   # (name, x, z, yaw)
keepout = []      # (x, z, r) circles solid props avoid (spawns, hills, flags)
lanes = []        # polylines (pts, width) kept free of solid scatter

def R(v):
    return round(v, 2)

def col(c):
    return [round(v, 3) for v in c]

def block(x0, y0, z0, x1, y1, z1, c, mirror=True):
    x0, x1 = min(x0, x1), max(x0, x1); z0, z1 = min(z0, z1), max(z0, z1)
    ops.append({"op": "Block", "x0": R(x0), "y0": R(y0), "z0": R(z0), "x1": R(x1), "y1": R(y1), "z1": R(z1), "color": col(c)})
    boxes.append((x0, z0, x1, z1, y1, y0, c)); solids.append((x0, z0, x1, z1))
    if mirror and abs(x0 + x1) > 0.01:
        block(-x1, y0, z0, -x0, y1, z1, c, mirror=False)

def mesh(kind, x, z, c, mirror=True, **kw):
    global meshes
    op = {"op": "Mesh", "mesh": kind, "x": R(x), "z": R(z)}
    for k, v in kw.items():
        op[k] = R(v) if isinstance(v, float) else v
    op["color"] = col(c)
    ops.append(op); meshes += 1
    if mirror and abs(x) > 0.01:
        kw2 = dict(kw)
        if "yaw" in kw2:
            kw2["yaw"] = -kw2["yaw"]
        if "roll" in kw2:
            kw2["roll"] = -kw2["roll"]
        mesh(kind, -x, z, c, mirror=False, **kw2)

def foot(name, x, z, yaw):
    p = PROPS[name]; a = math.radians(yaw); cs, sn = math.cos(a), math.sin(a)
    hx = abs(p["w"] / 2 * cs) + abs(p["d"] / 2 * sn); hz = abs(p["w"] / 2 * sn) + abs(p["d"] / 2 * cs)
    cx = x + p["cx"] * cs + p["cz"] * sn; cz = z - p["cx"] * sn + p["cz"] * cs
    return (cx - hx, cz - hz, cx + hx, cz + hz)

def overlaps(r, gap):
    for s in solids:
        if r[0] - gap < s[2] and s[0] < r[2] + gap and r[1] - gap < s[3] and s[1] < r[3] + gap:
            return True
    return False

def seg_dist(px, pz, ax, az, bx, bz):
    dx, dz = bx - ax, bz - az; L = dx * dx + dz * dz
    t = 0 if L == 0 else max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / L))
    return math.hypot(ax + t * dx - px, az + t * dz - pz)

def on_lane(r):
    cx, cz = (r[0] + r[2]) / 2, (r[1] + r[3]) / 2; rad = math.hypot(r[2] - r[0], r[3] - r[1]) / 2
    for pts, w in lanes:
        for i in range(len(pts) - 1):
            if seg_dist(cx, cz, *pts[i], *pts[i + 1]) < w / 2 + rad:
                return True
    return False

def in_keepout(r, extra=0):
    for (x, z, rad) in keepout:
        dx = max(r[0] - x, 0, x - r[2]); dz = max(r[1] - z, 0, z - r[3])
        if math.hypot(dx, dz) < rad + extra:
            return True
    return False

def prop(name, x, z, yaw=0, mirror=True, y=None, check=True):
    """Hand-placed prop (and its mirror twin)."""
    r = foot(name, x, z, yaw)
    if check and PROPS[name]["solid"] and y is None:
        if overlaps(r, 0.02):
            print(f"WARN skip {name} at ({x},{z}) overlaps"); return
    op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
    if yaw:
        op["yaw"] = R(yaw)
    if y is not None:
        op["y"] = R(y)
    ops.append(op); props_hand.append((name, x, z, yaw))
    if PROPS[name]["solid"] and y is None:
        solids.append(r)
    if mirror and abs(x) > 0.01:
        prop(name, -x, z, (-yaw) % 360 if yaw else 0, mirror=False, y=y, check=check)

def scatter(names, region, count, seed, gap=1.15, yaws=(0, 90, 180, 270), jitter=0, margin=2.0):
    """Mirrored solid scatter over region (x0, z0, x1, z1) in the west half: checks occupancy, lanes, keepouts, edge."""
    rnd = random.Random(seed); placed = 0; tries = 0
    while placed < count and tries < count * 60:
        tries += 1
        name = rnd.choice(names); x = rnd.uniform(region[0], region[2]); z = rnd.uniform(region[1], region[3])
        if x > -1.5:
            continue
        yaw = rnd.choice(yaws) + (rnd.uniform(-jitter, jitter) if jitter else 0)
        yaw = round(yaw % 360, 1)
        r = foot(name, x, z, yaw)
        corners = [(r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3])]
        if not all(inset_ok(cx, cz, margin) for cx, cz in corners):
            continue
        if overlaps(r, gap) or on_lane(r) or in_keepout(r, 0.6):
            continue
        rm = foot(name, -x, z, (-yaw) % 360)
        if overlaps(rm, gap) or on_lane(rm) or in_keepout(rm, 0.6):
            continue
        prop(name, round(x, 2), round(z, 2), yaw)
        placed += 1
    return placed

# ---------------------------------------------------------------- palette
SAND = (0.80, 0.68, 0.50); SAND_D = (0.68, 0.56, 0.40); PLASTER = (0.88, 0.80, 0.66); TERRA = (0.70, 0.40, 0.26)
WOOD = (0.45, 0.31, 0.19); WOOD_L = (0.62, 0.46, 0.28); TILE = (0.20, 0.45, 0.55); STONE = (0.66, 0.63, 0.58)
CLOTHS = [(0.78, 0.18, 0.16), (0.90, 0.62, 0.15), (0.20, 0.38, 0.62), (0.12, 0.50, 0.42), (0.55, 0.20, 0.45), (0.92, 0.85, 0.70)]
HEDGE = (0.22, 0.38, 0.17)

# ---------------------------------------------------------------- objectives (hand-placed, inside the outline)
BLUE_SP = [(-53, -3), (-53, 7), (-47, -3), (-47, 7), (-58, 2), (-44, 2), (-50, 14), (-50, -10)]
RED_SP = [(-x, z) for x, z in BLUE_SP]
FFA_SP = [(-30, -27), (30, -27), (-24, 33), (24, 33)]
HILLS = [(0, -9, 4.5), (0, 11, 4.5), (0, 29, 4.5)]
FLAGS = [(-57.5, 2.0), (57.5, 2.0)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    keepout.append((x, z, 2.5))
for x, z, r in HILLS:
    keepout.append((x, z, r + 0.5))
for x, z in FLAGS:
    keepout.append((x, z, 2.5))

# lanes (also foliage paths): the main walks every solid stays off
LANES = [
    ([(-60, 2), (-40, 2), (-28, 4), (-14, 6), (14, 6), (28, 4), (40, 2), (60, 2)], 3.2),       # main east-west thoroughfare (through the hall)
    ([(-48, -8), (-40, -18), (-28, -22), (-14, -16), (-6, -12), (6, -12), (14, -16), (28, -22), (40, -18), (48, -8)], 3.0),  # crescent alley under the inner curve
    ([(-48, 12), (-44, 22), (-30, 30), (-12, 33), (12, 33), (30, 30), (44, 22), (48, 12)], 3.0),   # garden walk on the outer arc
    ([(-34, 4), (-30, -8), (-20, -14)], 2.6), ([(34, 4), (30, -8), (20, -14)], 2.6),             # base to plaza cut
    ([(-30, 6), (-24, 16), (-14, 24), (-6, 30)], 2.6), ([(30, 6), (24, 16), (14, 24), (6, 30)], 2.6),  # base to garden cut
    ([(0, -16), (0, 36)], 2.6),                                                                   # north-south spine
    ([(-9, -6), (-9, 25)], 2.2), ([(9, -6), (9, 25)], 2.2),                                       # hall side aisles
]
lanes.extend(LANES)

# ---------------------------------------------------------------- ground
ops.append({"op": "Ground", "color": col((0.60, 0.52, 0.40))})

# ---------------------------------------------------------------- caravanserai (team base), west lobe
# A courtyard 18 x 16 (x -57..-39, z -6..10) with 3.4 m walls and gates on every side.
def caravanserai():
    x0, x1, z0, z1, h, t = -59.5, -40.0, -7.0, 11.0, 3.4, 0.6
    # north wall (z0), gate 3 m wide centred x=-50
    block(x0, 0, z0, -51.5, h, z0 + t, SAND)
    block(-48.5, 0, z0, x1, h, z0 + t, SAND)
    block(-51.5, 2.6, z0, -48.5, h, z0 + t, SAND)  # lintel
    # south wall (z1)
    block(x0, 0, z1 - t, -51.5, h, z1, SAND)
    block(-48.5, 0, z1 - t, x1, h, z1, SAND)
    block(-51.5, 2.6, z1 - t, -48.5, h, z1, SAND)
    # east wall (x1): two gates at z=-2.5 and z=6.5, solid pier in the middle
    block(x1 - t, 0, z0 + t, x1, h, -4.0, SAND)
    block(x1 - t, 0, -1.0, x1, h, 5.0, SAND)
    block(x1 - t, 0, 8.0, x1, h, z1 - t, SAND)
    block(x1 - t, 2.6, -4.0, x1, h, -1.0, SAND)
    block(x1 - t, 2.6, 5.0, x1, h, 8.0, SAND)
    # west wall (x0) with a postern at z=2 (the flag sits inside it)
    block(x0, 0, z0 + t, x0 + t, h, 0.8, SAND)
    block(x0, 0, 3.2, x0 + t, h, z1 - t, SAND)
    block(x0, 2.4, 0.8, x0 + t, h, 3.2, SAND)
    # corner towers (solid) with domes
    for (cx, cz) in [(x0 + 0.3, z0 + 0.3), (x0 + 0.3, z1 - 0.3), (x1 - 0.3, z0 + 0.3), (x1 - 0.3, z1 - 0.3)]:
        block(cx - 1.3, 0, cz - 1.3, cx + 1.3, 5.2, cz + 1.3, SAND_D)
        mesh("sphere", cx, cz, TILE, y=4.6, width=2.4, height=2.0, depth=2.4, segments=16, detail=8)
    # arcade roof along the inside of the north and south walls (cosmetic shade, on cosmetic posts)
    for zz in (z0 + t + 1.2, z1 - t - 1.2):
        mesh("box", (x0 + x1) / 2, zz, PLASTER, y=2.9, width=x1 - x0 - 1.6, height=0.18, depth=2.4, material="Tiles")
        for k in range(6):
            xx = x0 + 2.2 + k * 3.1
            if abs(xx + 50) > 2.0:
                mesh("cylinder", xx, zz + (1.0 if zz < 0 else -1.0), PLASTER, width=0.3, height=2.9, depth=0.3, segments=10)
    # the well in the courtyard (low, steppable rim block + mesh)
    block(-51.0, 0, 1.2, -49.0, 0.5, 3.2, STONE)
    mesh("cylinder", -50, 2.2, STONE, width=2.6, height=0.8, depth=2.6, segments=20, material="Concrete")
    mesh("torus", -50, 2.2, WOOD, y=2.3, radius=0.9, inner=0.06)
    mesh("box", -50, 1.25, WOOD, width=0.12, height=2.4, depth=0.12)
    mesh("box", -50, 3.15, WOOD, width=0.12, height=2.4, depth=0.12)
    mesh("box", -50, 2.2, WOOD, y=2.3, width=0.12, height=0.12, depth=2.1)
    # banners over the east gates
    for gz in (-2.5, 6.5):
        mesh("box", x1 + 0.05, gz, CLOTHS[2], y=2.0, width=0.06, height=1.4, depth=2.6)

caravanserai()

def screens_and_shophouses():
    # blind walls in front of the east gates, so nobody looks out of a base straight down the thoroughfare
    block(-37.0, 0, -5.0, -36.4, 3.0, -0.6, SAND)
    block(-37.0, 0, 4.6, -36.4, 3.0, 9.0, SAND)
    # two tall shophouses frame the plaza's west and east sides (solid), with awnings and a balcony
    block(-19.5, 0, -11.5, -14.5, 6.5, -3.0, PLASTER)
    block(-19.7, 6.5, -11.7, -14.3, 6.8, -2.8, SAND_D)
    mesh("wedge", -13.8, -7.25, CLOTHS[1], y=2.6, width=8.0, height=0.5, depth=1.4, yaw=90.0)
    mesh("box", -14.2, -7.25, WOOD, y=3.6, width=0.8, height=0.1, depth=4.0)
    for k in range(3):
        mesh("box", -14.45, -9.5 + k * 2.25, (0.15, 0.13, 0.12), y=3.9, width=0.05, height=1.3, depth=0.9)  # shuttered windows
        mesh("box", -14.45, -9.5 + k * 2.25, TILE, y=0.0, width=0.05, height=2.2, depth=1.1)                # shop doors

screens_and_shophouses()

# ---------------------------------------------------------------- the covered souk hall (centre), hill B inside
def souk_hall():
    # 26 x 14 roof on piers: x -13..13, z 4..18, roof at 4.6 m. Half-height walls between the piers on the long
    # sides with gaps, and the short ends offset so no straight line runs through it.
    hx0, hz0, hz1, roof = -13.0, 4.0, 18.0, 4.6
    block(hx0, roof, hz0, -hx0, roof + 0.35, hz1, TERRA, mirror=False)
    # piers
    for (px, pz) in [(-13, 4), (-13, 18), (-6.5, 4), (-6.5, 18)]:
        block(px - 0.45, 0, pz - 0.45, px + 0.45, roof, pz + 0.45, PLASTER)
    for pz in (4, 18):
        block(-0.45, 0, pz - 0.45, 0.45, roof, pz + 0.45, PLASTER, mirror=False)
    # tall wall sections on the ends (x = +-13) with a door each side of a middle pier
    block(-13.3, 0, 6.5, -12.7, roof, 9.0, PLASTER)
    block(-13.3, 0, 13.0, -12.7, roof, 15.5, PLASTER)
    # long sides: waist-high market counters between the piers (cover) and full-height panels near the corners
    block(-11.5, 0, 3.75, -8.5, 1.05, 4.25, WOOD_L)
    block(-11.5, 0, 17.75, -8.5, 1.05, 18.25, WOOD_L)
    block(-4.8, 0, 3.75, -2.2, 1.05, 4.25, WOOD_L)
    block(-4.8, 0, 17.75, -2.2, 1.05, 18.25, WOOD_L)
    # inside: two stall islands either side of the hill zone, tall enough to block sight end to end
    block(-8.6, 0, 7.4, -6.4, 2.1, 9.4, WOOD)
    block(-8.6, 0, 12.6, -6.4, 2.1, 14.6, WOOD)
    # skylight lantern on the roof (cosmetic)
    mesh("box", 0, 11, PLASTER, y=4.95, width=6, height=0.9, depth=4, material="Tiles", mirror=False)
    mesh("sphere", 0, 11, TILE, y=5.4, width=4.2, height=2.6, depth=4.2, segments=20, detail=10, mirror=False)
    # hanging lanterns and cloth under the roof
    for k, lx in enumerate((-10.5, -3.5)):
        for lz in (7.0, 15.0):
            mesh("sphere", lx, lz, (0.95, 0.75, 0.35), y=3.4, width=0.45, height=0.6, depth=0.45, segments=10, detail=6)
            mesh("box", lx, lz, (0.2, 0.15, 0.1), y=3.95, width=0.03, height=0.65, depth=0.03)
    for k, lz in enumerate((6.0, 11.0, 16.0)):
        mesh("box", 0, lz, CLOTHS[k % len(CLOTHS)], y=4.3, width=25, height=0.04, depth=1.6, mirror=False)

souk_hall()

# ---------------------------------------------------------------- crescent plaza (inner curve), hill A on the fountain
def plaza():
    # fountain: a steppable 0.45 m basin block with a cosmetic bowl and spout
    block(-2.2, 0, -11.2, 2.2, 0.45, -6.8, STONE, mirror=False)
    mesh("cylinder", 0, -9, STONE, width=5.4, height=0.55, depth=5.4, segments=28, material="Concrete", mirror=False)
    mesh("cylinder", 0, -9, (0.30, 0.55, 0.68), y=0.5, width=4.6, height=0.04, depth=4.6, segments=28, mirror=False)
    mesh("cylinder", 0, -9, STONE, y=0.5, width=0.5, height=1.4, depth=0.5, segments=12, mirror=False)
    mesh("cylinder", 0, -9, STONE, y=1.9, width=1.6, height=0.25, depth=1.6, segments=16, mirror=False)
    # tiled star around it (cosmetic flat)
    mesh("cylinder", 0, -9, TILE, y=0.0, width=9.0, height=0.03, depth=9.0, segments=8, mirror=False)
    # a ring of market stalls following the inner curve: counter (solid block) + awning (cosmetic wedge) on posts
    stalls = [(-6.5, -15.2, 20), (-12.5, -18.4, 32), (-19.5, -22.6, 22), (-11.0, -6.0, -70)]
    for i, (sx, sz, ang) in enumerate(stalls):
        stall(sx, sz, ang, CLOTHS[i % len(CLOTHS)])
    # stepped seating either side of the plaza (solid, mantle-able cover)
    block(-8.6, 0, -2.2, -4.4, 0.9, -1.0, SAND_D)
    # a big rug market on the floor (cosmetic)
    for k in range(5):
        mesh("box", -5.5 + k * 0.1, -3.4 + (k % 2) * 0.3, CLOTHS[(k + 2) % len(CLOTHS)], y=0.01 + k * 0.012, width=2.0, height=0.02, depth=1.3, yaw=k * 17.0)

def stall(sx, sz, ang, cloth):
    """A market stall centred (sx, sz) facing angle ang (deg): solid counter 2.6 x 0.6 x 1.0, cosmetic awning and posts."""
    a = math.radians(ang); ux, uz = math.cos(a), -math.sin(a)  # counter long axis
    # counter: axis-aligned approximation (blocks can't turn), use the dominant axis
    if abs(ux) >= abs(uz):
        block(sx - 1.3, 0, sz - 0.3, sx + 1.3, 1.0, sz + 0.3, WOOD_L)
    else:
        block(sx - 0.3, 0, sz - 1.3, sx + 0.3, 1.0, sz + 1.3, WOOD_L)
    yaw = 0 if abs(ux) >= abs(uz) else 90
    mesh("wedge", sx, sz, cloth, y=2.25, width=3.0 if yaw == 0 else 2.2, height=0.5, depth=2.2 if yaw == 0 else 3.0, yaw=float(yaw))
    for dx, dz in ((-1.35, -1.0), (1.35, -1.0), (-1.35, 1.0), (1.35, 1.0)):
        px, pz = (sx + dx, sz + dz) if yaw == 0 else (sx + dz, sz + dx)
        mesh("cylinder", px, pz, WOOD, width=0.12, height=2.4, depth=0.12, segments=6)
    # wares on the counter: pots and bundles (cosmetic)
    for k in range(3):
        o = -0.8 + k * 0.8
        px, pz = (sx + o, sz) if yaw == 0 else (sx, sz + o)
        mesh("sphere" if k != 1 else "cylinder", px, pz, (TERRA, (0.85, 0.75, 0.3), (0.55, 0.3, 0.15))[k], y=1.0, width=0.4, height=0.42, depth=0.4, segments=10, detail=6)

plaza()

# ---------------------------------------------------------------- north pockets beside the dent: the spice yards
def spice_yard():
    # a low-walled yard: L of 1.2 m walls (mantle-able cover) with a store shed (solid) against the outer wall side
    block(-36.0, 0, -28.5, -33.4, 2.8, -24.6, PLASTER)                      # store shed
    mesh("wedge", -34.7, -26.55, TERRA, y=2.8, width=3.0, height=0.6, depth=4.4, yaw=90.0)
    block(-28.0, 0, -18.4, -22.0, 1.15, -17.8, SAND_D)                     # low wall
    block(-38.4, 0, -21.6, -37.8, 1.15, -16.0, SAND_D)
    # sack heaps (hay bales read as sacks), crates and pallets
    prop("Hay Bale", -31.0, -30.2, 10); prop("Hay Bale", -29.6, -30.4, 350)
    prop("Wooden Crate", -26.0, -31.0, 0); prop("Small Crate", -26.0, -31.0, 0, y=0.9, check=False)
    prop("Pallet Stack", -38.2, -24.0, 90)
    prop("Cardboard Stack", -24.2, -27.6, 15)
    # spice baskets (cosmetic cylinders, bright colours)
    spice = [(0.80, 0.30, 0.10), (0.90, 0.70, 0.10), (0.55, 0.15, 0.10), (0.40, 0.50, 0.20), (0.75, 0.45, 0.20)]
    for k in range(10):
        bx = -33.0 + (k % 5) * 0.75; bz = -20.6 + (k // 5) * 0.8
        mesh("cylinder", bx, bz, WOOD_L, width=0.6, height=0.35, depth=0.6, segments=10)
        mesh("sphere", bx, bz, spice[k % 5], y=0.22, width=0.55, height=0.3, depth=0.55, segments=10, detail=5)
    stall(-17.0, -28.4, 25, CLOTHS[4])

spice_yard()

# ---------------------------------------------------------------- outer arc: the garden (south)
def garden():
    # pavilion over hill C: four solid columns and a domed roof (the roof is cosmetic, the columns are cover)
    for (px, pz) in [(-3.6, 25.4), (-3.6, 32.6)]:
        block(px - 0.35, 0, pz - 0.35, px + 0.35, 3.4, pz + 0.35, PLASTER)
    mesh("cylinder", 0, 29, PLASTER, y=3.4, width=9.2, height=0.3, depth=9.2, segments=8, mirror=False)
    mesh("sphere", 0, 29, TILE, y=2.6, width=6.4, height=4.0, depth=6.4, segments=24, detail=12, mirror=False)
    # minaret landmark at the south edge: solid core + cosmetic shaft, balcony and cap
    block(-0.9, 0, 36.2, 0.9, 15.0, 38.0, SAND, mirror=False)
    mesh("cylinder", 0, 37.1, SAND, width=2.6, height=15.0, depth=2.6, segments=16, material="Brick", mirror=False)
    mesh("cylinder", 0, 37.1, PLASTER, y=11.0, width=3.4, height=0.4, depth=3.4, segments=16, mirror=False)
    mesh("cylinder", 0, 37.1, SAND, y=15.0, width=2.0, height=2.2, depth=2.0, segments=16, mirror=False)
    mesh("sphere", 0, 37.1, TILE, y=16.6, width=2.2, height=1.8, depth=2.2, segments=16, detail=8, mirror=False)
    mesh("cone", 0, 37.1, (0.85, 0.7, 0.25), y=17.9, width=0.4, height=1.4, depth=0.4, segments=8, mirror=False)
    # hedged beds: low hedges (1.1 m, solid, mantle-able) with gaps
    block(-20.0, 0, 24.0, -14.0, 1.1, 25.0, HEDGE)
    block(-27.0, 0, 33.6, -21.0, 1.1, 34.6, HEDGE)
    block(-38.5, 0, 25.0, -37.5, 1.1, 30.0, HEDGE)
    # raised water channel between beds (0.5 m, steppable)
    block(-14.0, 0, 30.0, -10.5, 0.5, 31.0, STONE)
    mesh("box", -12.25, 30.5, (0.30, 0.55, 0.68), y=0.5, width=3.3, height=0.02, depth=0.7)
    # benches and planters
    prop("Bench", -6.8, 22.0, 0); prop("Bench", -17.0, 36.3, 0)
    prop("Planter", -16.0, 27.4, 0); prop("Planter", -24.0, 26.0, 0); prop("Planter", -32.2, 34.8, 0)

garden()

# ---------------------------------------------------------------- west lanes: cart yard and dyers' row
def cart_yard():
    # a cart (solid blocks) and hay around the south-west of the base
    block(-44.0, 0.45, 21.0, -40.6, 1.2, 22.8, WOOD)               # cart bed (crouch under / mantle)
    block(-44.0, 0, 20.9, -43.6, 0.9, 21.3, WOOD); block(-44.0, 0, 22.5, -43.6, 0.9, 22.9, WOOD)
    mesh("cylinder", -42.0, 20.8, WOOD, y=0.55, width=1.1, height=0.1, depth=1.1, pitch=90.0, segments=12)
    mesh("cylinder", -42.0, 23.0, WOOD, y=0.55, width=1.1, height=0.1, depth=1.1, pitch=90.0, segments=12)
    prop("Hay Bale", -38.6, 18.1, 80); prop("Hay Bale", -38.4, 19.9, 95)
    # dyers' row north of the base: cloth hung on lines between posts (cosmetic), vats (solid drums)
    for k in range(4):
        xx = -46.0 + k * 3.2
        mesh("cylinder", xx, -14.5, WOOD, width=0.14, height=2.8, depth=0.14, segments=6)
        if k < 3:
            mesh("box", xx + 1.6, -14.5, CLOTHS[(k + 1) % len(CLOTHS)], y=1.1, width=2.9, height=1.6, depth=0.03)
    prop("Oil Drum", -45.4, -17.8, 0); prop("Rusty Drum", -44.4, -18.4, 0)
    # a tea house south-east of the base: a small solid room-sized block with an awning, chairs and tables out front
    block(-36.0, 0, 13.0, -31.0, 3.0, 16.4, PLASTER)
    mesh("wedge", -33.5, 12.2, CLOTHS[0], y=2.4, width=5.6, height=0.4, depth=1.8, yaw=180.0)
    prop("Table", -34.6, 10.8, 0); prop("Table", -31.8, 10.6, 90)

cart_yard()

# ---------------------------------------------------------------- spawns, waypoints placed after the structures
for x, z in BLUE_SP + RED_SP + FFA_SP:
    ops.append({"op": "Spawn", "x": R(x), "z": R(z)})

# ---------------------------------------------------------------- hand-placed cover props (mirrored)
def cover():
    # base courtyard: crates by the gates, feed bales
    prop("Wooden Crate", -43.4, -3.6, 0); prop("Wooden Crate", -43.4, 7.6, 0)
    prop("Hay Bale", -56.2, 8.6, 0); prop("Hay Bale", -56.2, -4.4, 0)
    # outside the east gates: sandbag-like sack walls (sandbag wall) and a vending-machine-like water cooler
    # plaza edge: planters and benches facing the fountain
    prop("Planter", -6.6, -9.0, 0); prop("Bench", -3.8, -13.6, 0)
    prop("Street Lamp", -11.6, -10.4, 0)
    # hall surroundings
    prop("Dumpster", -15.6, 20.2, 0)
    prop("Wooden Crate", -15.2, 1.4, 0); prop("Small Crate", -15.2, 1.4, 0, y=0.9, check=False)
    prop("Pallet Stack", -11.0, 1.0, 0)
    # garden-side water barrels
    prop("Oil Drum", -20.6, 19.4, 0); prop("Oil Drum", -21.4, 20.1, 0)
    prop("Wheelie Bin", -26.0, 21.8, 0)
    # tea house drums with one explosive (gas for the samovars) per side, far from spawns
    prop("Explosive Barrel", -24.4, -11.2, 0)
    prop("Gas Cylinders", -36.6, 17.3, 0)

cover()

ops.append({"op": "AutoWaypoints", "spacing": 6})
ops.append({"op": "Waypoint", "x": -50, "z": 2})  # courtyard
ops.append({"op": "Waypoint", "x": 50, "z": 2})
ops.append({"op": "Waypoint", "x": -30, "z": -25})  # spice yards
ops.append({"op": "Waypoint", "x": 30, "z": -25})

# ---------------------------------------------------------------- scattered solid props (mirrored, deterministic)
MARKET = ["Wooden Crate", "Small Crate", "Cardboard Box Large", "Cardboard Stack", "Pallet Stack", "Hay Bale", "Table", "Wooden Chair",
          "Plastic Chair", "Trash Can", "Wheelie Bin", "Recycling Bin", "Oil Drum", "Rusty Drum", "Planter", "Sawhorse Barricade", "Cardboard Box"]
GARDEN = ["Planter", "Bench", "Tree Stump", "Log", "Hay Bale", "Wheelie Bin Grey", "Recycling Bin", "Wooden Chair", "Table"]
YARD = ["Wooden Crate", "Small Crate", "Pallet Stack", "Pallet", "Cable Spool", "Tyre Stack", "Oil Drum", "Rusty Drum", "Hay Bale", "Plank Pile", "Brick Pile", "Cardboard Stack"]
n = 0
n += scatter(MARKET, (-30, -14, -2, 2), 26, 11)                 # around the plaza and in front of the hall
n += scatter(MARKET, (-40, -32, -12, -12), 22, 12)              # spice yards and the crescent alley
n += scatter(YARD, (-67, -30, -40, -8), 16, 13)                 # behind the base, north
n += scatter(YARD, (-67, 12, -38, 33), 18, 14)                  # cart yard, south-west
n += scatter(GARDEN, (-44, 20, -2, 43), 28, 15, yaws=(0, 45, 90, 135, 180), jitter=8)  # the garden
n += scatter(MARKET, (-38, 0, -16, 20), 22, 16)                 # between base and hall
n += scatter(MARKET, (-12.5, 5, -1.5, 17.5), 6, 17, gap=1.2)    # inside the hall
SCATTERED = n

# ---------------------------------------------------------------- cosmetic dressing (meshes): rugs, pots, lanterns
rnd = random.Random(5)
mc = 0
tries = 0
while mc < 70 and tries < 4000:
    tries += 1
    x = rnd.uniform(-60, -1.5); z = rnd.uniform(-34, 39)
    if not inset_ok(x, z, 1.5):
        continue
    r = (x - 0.4, z - 0.4, x + 0.4, z + 0.4)
    if overlaps(r, 0.2):
        continue
    k = rnd.random()
    if k < 0.45:   # clay pot
        h = rnd.uniform(0.5, 0.9)
        mesh("sphere", round(x, 2), round(z, 2), (TERRA, (0.62, 0.36, 0.22), (0.78, 0.55, 0.35))[rnd.randrange(3)], width=h * 0.8, height=h, depth=h * 0.8, segments=12, detail=6)
    elif k < 0.75:  # rug laid on the ground
        mesh("box", round(x, 2), round(z, 2), CLOTHS[rnd.randrange(len(CLOTHS))], y=0.01, width=round(rnd.uniform(1.4, 2.4), 2), height=0.02, depth=round(rnd.uniform(0.9, 1.5), 2), yaw=round(rnd.uniform(0, 180), 1))
    else:           # lantern post
        mesh("cylinder", round(x, 2), round(z, 2), WOOD, width=0.1, height=2.2, depth=0.1, segments=6)
        mesh("sphere", round(x, 2), round(z, 2), (0.95, 0.75, 0.35), y=2.2, width=0.35, height=0.45, depth=0.35, segments=8, detail=5)
    mc += 1

# strings of bunting across the crescent alley (thin cosmetic boxes)
for k in range(6):
    bx = -38 + k * 6.0; bz = -24 + abs(k - 3) * 1.6
    mesh("box", bx, bz, CLOTHS[k % len(CLOTHS)], y=3.6, width=0.05, height=0.25, depth=8.0, yaw=float(20 + k * 8))

# ---------------------------------------------------------------- cosmetic clutter (game-side Clutter op, unmirrored)
LITTER = ["Litter", "Litter", "Newspaper", "Soda Cans", "Bottles", "Bottle", "Plastic Bag", "Flattened Cardboard", "Cardboard Box Small",
          "Open Cardboard Box", "Trash Bag", "Newspaper Stack", "Loose Bricks", "Rubble", "Cinder Blocks", "Plank", "Tyre"]
ops.append({"op": "Clutter", "props": LITTER, "x0": -69, "z0": -39, "x1": 69, "z1": 45, "count": 1000, "seed": 21})
ops.append({"op": "Clutter", "props": ["Bottles", "Newspaper", "Litter", "Soda Cans", "Plastic Bag", "Open Cardboard Box"], "x0": -24, "z0": -20, "x1": 24, "z1": 0, "count": 160, "seed": 22})
ops.append({"op": "Clutter", "props": ["Litter", "Trash Bag", "Flattened Cardboard", "Bottles", "Plank"], "x0": -40, "z0": -24, "x1": 40, "z1": -24, "width": 6, "count": 120, "seed": 23})

# ---------------------------------------------------------------- foliage
paths = [{"points": [list(p) for p in pts], "width": w} for pts, w in LANES]
foliage = {
    "seed": 9,
    "paths": paths,
    "regions": [
        # the garden: lush grass, shrubs and bushes, broadleaf trees, cypresses (pines) lining the walk
        {"type": "grass", "density": 6, "polygon": [[-46, 18], [-30, 26], [-12, 24], [12, 24], [30, 26], [46, 18], [48, 30], [18, 40], [-18, 40], [-48, 30]], "color": "#6f8a3a"},
        {"type": "shrub", "density": 0.9, "polygon": [[-46, 18], [-30, 26], [-12, 24], [12, 24], [30, 26], [46, 18], [48, 30], [18, 40], [-18, 40], [-48, 30]]},
        {"type": "bush", "density": 0.35, "polygon": [[-46, 18], [-30, 26], [-12, 24], [12, 24], [30, 26], [46, 18], [48, 30], [18, 40], [-18, 40], [-48, 30]], "scale": [0.9, 1.5]},
        # dry tufts everywhere else (sparse, patchy)
        {"type": "grass", "density": 2.2, "rect": [-69, -39, 69, 24], "patchiness": 0.8, "color": "#9a9450", "scale": [0.6, 1.0]},
        # weeds along the foot of the city wall
        {"type": "bush", "density": 0.25, "rect": [-69, -39, 69, 45], "patchiness": 0.9, "color": "#5f6e34", "scale": [0.6, 1.1]},
        # the base courtyards' kitchen gardens
        {"type": "shrub", "density": 1.4, "rect": [-58, -5.5, -54, 0.5]}, {"type": "shrub", "density": 1.4, "rect": [54, -5.5, 58, 0.5]},
    ],
    "items": [],
}
# hand-placed trees, mirrored: orchard trees in the garden, cypresses along the walk, shade trees in the plaza
trees = [("tree", -22.0, 29.0, 6.5), ("tree", -30.0, 23.6, 6.0), ("tree", -12.0, 37.0, 7.0), ("tree", -36.0, 31.0, 6.0), ("tree", -18.0, 21.5, 5.5),
         ("pine", -6.0, 36.0, 9.0), ("pine", -42.0, 26.0, 8.5), ("pine", -26.0, 37.4, 8.0),
         ("tree", -16.0, -12.0, 7.0), ("tree", -46.0, -22.0, 6.5), ("tree", -56.0, 16.0, 6.0), ("pine", -60.0, -10.0, 8.0),
         ("tree", -24.0, 10.0, 6.0)]
for t, x, z, h in trees:
    for sx in (1, -1):
        foliage["items"].append({"type": t, "x": x * sx, "z": z, "height": h})
        solids.append((x * sx - 0.4, z - 0.4, x * sx + 0.4, z + 0.4))
        boxes.append((x * sx - 0.3, z - 0.3, x * sx + 0.3, z + 0.3, h, 0, (0.3, 0.2, 0.1)))

# ---------------------------------------------------------------- the map
mp = {
    "format": "call-of-slop-map/1",
    "name": "Crescent Bazaar",
    "description": "A walled market on a bean-shaped plan: the crescent plaza sits in the inner curve, a covered souk in the middle, a palace garden round the outer arc.",
    "author": "Call of Slop",
    "order": 402,
    "maxPlayers": 16,
    "sky": "#d8c3a0",
    "boundary": {
        "shape": "smooth",
        "points": CTRL,
        "edge": {"style": "wall", "height": 5, "thickness": 1.0, "color": col(SAND), "material": "Brick"},
        "groundMargin": 8,
    },
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [({"x": x, "z": z, "y": 0.45, "radius": r} if z == -9 else {"x": x, "z": z, "radius": r}) for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": ops,
    "foliage": foliage,
}

# checks
for x, z in BLUE_SP + RED_SP + FFA_SP + [(h[0], h[1]) for h in HILLS] + FLAGS:
    assert inset_ok(x, z, 1.5), (x, z)

def dump(o, ind=0):
    pad = "  " * ind
    if isinstance(o, dict) and ind <= 1 and any(isinstance(v, (dict, list)) for v in o.values()):
        items = []
        for k, v in o.items():
            items.append(f'{pad}  "{k}": {dump(v, ind + 1)}')
        return "{\n" + ",\n".join(items) + "\n" + pad + "}"
    if isinstance(o, list) and ind <= 2 and o and isinstance(o[0], dict):
        return "[\n" + ",\n".join(pad + "  " + json.dumps(v) for v in o) + "\n" + pad + "]"
    return json.dumps(o)

os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
with open(OUT_JSON, "w") as f:
    f.write(dump(mp) + "\n")

nsolid = sum(1 for p in props_hand if PROPS[p[0]]["solid"])
nbrk = sum(1 for p in props_hand if PROPS[p[0]]["brk"])
print(f"area {area():.0f} m2, bbox x {min(p[0] for p in OUTLINE):.1f}..{max(p[0] for p in OUTLINE):.1f} z {min(p[1] for p in OUTLINE):.1f}..{max(p[1] for p in OUTLINE):.1f}")
print(f"blocks {sum(1 for o in ops if o['op']=='Block')}, meshes {meshes}, hand props {len(props_hand)} (solid {nsolid}, breakable {nbrk}, scattered {SCATTERED * 2}), trees {len(foliage['items'])}")

# ---------------------------------------------------------------- preview PNG + sightline check
from PIL import Image, ImageDraw
S = 8; W = int(140 * S); H = int(90 * S)
def P(x, z):
    return ((x + 70) * S, (z + 42) * S)
img = Image.new("RGB", (W, H), (60, 55, 50)); d = ImageDraw.Draw(img)
d.polygon([P(*p) for p in OUTLINE], fill=(190, 170, 130))
for pts, w in LANES:
    d.line([P(*p) for p in pts], fill=(205, 190, 155), width=int(w * S))
for x0, z0, x1, z1, h, y0, c in boxes:
    shade = tuple(int(v * 255 * (0.6 if h < 1.3 else 1.0)) for v in c)
    d.rectangle([P(x0, z0), P(x1, z1)], fill=shade, outline=(30, 30, 30) if y0 < 1.7 else (120, 120, 120))
for name, x, z, yaw in props_hand:
    r = foot(name, x, z, yaw)
    if PROPS[name]["solid"]:
        d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=(150, 90, 40) if PROPS[name]["brk"] else (90, 90, 100))
for x, z in BLUE_SP:
    d.ellipse([P(x - 0.6, z - 0.6), P(x + 0.6, z + 0.6)], fill=(40, 80, 230))
for x, z in RED_SP:
    d.ellipse([P(x - 0.6, z - 0.6), P(x + 0.6, z + 0.6)], fill=(230, 40, 40))
for x, z in FFA_SP:
    d.ellipse([P(x - 0.6, z - 0.6), P(x + 0.6, z + 0.6)], fill=(240, 240, 240))
for x, z, r in HILLS:
    d.ellipse([P(x - r, z - r), P(x + r, z + r)], outline=(255, 230, 0), width=3)
for x, z in FLAGS:
    d.rectangle([P(x - 0.5, z - 0.5), P(x + 0.5, z + 0.5)], fill=(255, 255, 255))
for gx in range(-60, 61, 10):
    d.text(P(gx, -41), str(gx), fill=(255, 255, 255))
for gz in range(-30, 41, 10):
    d.text(P(-69, gz), str(gz), fill=(255, 255, 255))

# sightlines: blue spawns to red spawns (eye height, blocked by boxes taller than 1.6 m with bottom below 1.6 m, and the outline)
tall = [(b[0], b[1], b[2], b[3]) for b in boxes if b[4] > 1.6 and b[5] < 1.6]
def blocked(a, b):
    L = math.hypot(b[0] - a[0], b[1] - a[1]); k = int(L / 0.25)
    for i in range(1, k):
        x = a[0] + (b[0] - a[0]) * i / k; z = a[1] + (b[1] - a[1]) * i / k
        if not inside(x, z):
            return True
        for r in tall:
            if r[0] <= x <= r[2] and r[1] <= z <= r[3]:
                return True
    return False
vis = 0
for a in BLUE_SP:
    for b in RED_SP:
        if not blocked(a, b):
            vis += 1; d.line([P(*a), P(*b)], fill=(255, 0, 255), width=1)
print("blue-red spawn lines of sight:", vis)
img.save(OUT_PNG)
