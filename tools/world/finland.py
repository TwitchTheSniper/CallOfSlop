#!/usr/bin/env python3
"""Generator for Maps/World/06-Finland.json (World Tour pack, order 506, large, 24 players): "Vuorikoski", a boreal
forest in early autumn with two wooden villages, a church hamlet and a big copper mine in the middle with a tunnel
network under it.

Usage: python3 tools/world/finland.py <repo root> [preview.png]

Layout (north is -Z). The map is mirror-symmetric about x = 0 in its terrain, tunnels, roads and objectives (each
team's half is the other's reflection), and dressed differently on each side:
- Blue holds Honkala in the west, Red holds Kuusela in the east: red and yellow ochre wooden houses along a gravel
  street, a village shop, a garage, a cottage, and a lake below the village with a jetty and a lakeside sauna.
- Kirkonmaki, the church hamlet on a low hill in the north: the wooden church, its bell tower, two houses.
- The Vuorikoski mine in the middle on a levelled pad: the concrete headframe over the shaft (a cage lift runs down
  it), the concentrator with a conveyor gallery up to the headframe, the hoist house, the workshop, offices, a rail
  spur with an ore load-out and hopper wagons, slag heaps, and the terraced open pit in the south.
- Underground (terrain "cave" features, floor at -8 m, "level 120"): the shaft station under the headframe, the main
  drift east-west, a haulage loop with the pump station and the explosives magazine, the crusher chamber under the
  concentrator, a drift out to the pit floor, and adits up to the forest ridges, the villages and the church hill.
- The edge is a wavy noise outline whose ground rises into forested hills (a cliff boundary with mesh pines beyond).

Built with tools/cluttered/townkit.py (occupancy, props, foliage helpers) and terrain.py (the game's height features,
ported). Caves are not in the terrain port, so this file carries a small port of the cave floor (TerrainVoids.cs) for
placing things in the tunnels.
"""
import glob as _glob, json, math, os, random, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "cluttered"))
import numpy as np
import townkit as tk
import terrain as tr
from townkit import R, col, yaw_facing, yaw_along

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/World/06-Finland.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None

# townkit's prop loader reads every Props/*/*.json; the flag designs file isn't a prop
_real_glob = _glob.glob
tk.glob.glob = lambda p, **kw: [f for f in _real_glob(p, **kw) if not f.replace("\\", "/").endswith("Flags/flags.json")]

# ================================================================ outline: a wavy blob, mirror-symmetric about x = 0
RIGHT = [(0, -86), (28, -84), (58, -80), (86, -66), (106, -44), (116, -16), (117, 14), (108, 40), (90, 62), (62, 78), (30, 86), (0, 88)]
CTRL = RIGHT + [(-x, z) for x, z in reversed(RIGHT[1:-1])]
NOISE = {"amplitude": 2.6, "scale": 24, "seed": 506, "octaves": 2}
t = tk.Town(REPO, CTRL, NOISE, pack_props=[os.path.join(REPO, "Maps/World/Props")])
rng = random.Random(506)
HALF_X = max(abs(p[0]) for p in t.outline)
HALF_Z = max(abs(p[1]) for p in t.outline)

# ================================================================ palette
CONCRETE = (0.60, 0.59, 0.56); CONC_D = (0.46, 0.45, 0.43); STEEL = (0.42, 0.44, 0.46); RUST = (0.48, 0.30, 0.20)
TIMBER = (0.42, 0.32, 0.20); DECK = (0.52, 0.40, 0.26); WATER = (0.16, 0.26, 0.30); DARK = (0.10, 0.10, 0.11)
GLASS_D = (0.16, 0.20, 0.24); BLUE_STEEL = (0.26, 0.36, 0.50); GALV = (0.62, 0.64, 0.66); RED_OCHRE = (0.55, 0.21, 0.15)
WHITE = (0.93, 0.92, 0.88)

# ================================================================ levels
PAD = 1.0          # the mine pad
F = -8.0           # tunnel floor ("level 120")
WATER_Y = -1.0
PIT = (-3.0, -6.0, -9.0)


def mirror_feature(f):
    g = json.loads(json.dumps(f))
    if "x" in g:
        g["x"] = -g["x"]
    if "points" in g:
        g["points"] = [[-p[0]] + p[1:] for p in g["points"]]
    if "angle" in g:
        g["angle"] = -g["angle"]
    return g


FEAT = [
    {"type": "noise", "amplitude": 1.1, "scale": 36, "octaves": 4, "seed": 5061},
    {"type": "noise", "amplitude": 0.25, "scale": 8, "octaves": 2, "seed": 5062},
]
HALF = []   # west-half features (mirrored to the east)
# forest hills and ridges
HALF += [
    {"type": "hill", "x": -62, "z": -56, "radius": 20, "height": 7.5},
    {"type": "ridge", "points": [[-68, -60], [-60, -54], [-54, -56]], "width": 6, "height": 1.2, "falloff": "dome", "material": "rock"},   # granite crown
    {"type": "hill", "x": -100, "z": -46, "radius": 14, "height": 5},
    {"type": "hill", "x": -34, "z": -68, "radius": 14, "height": 3.5},
    {"type": "ridge", "points": [[-54, -38], [-55, -22], [-53, -6], [-56, 8]], "width": 18, "height": 4.2},                      # the sight screen between village and mine
    {"type": "hill", "x": -50, "z": 70, "radius": 14, "height": 5},
    {"type": "hill", "x": -62, "z": 10, "radius": 9, "height": 2.5},
    {"type": "hill", "x": -96, "z": 30, "radius": 12, "height": 3},
    {"type": "hill", "x": -24, "z": 76, "radius": 10, "height": 3},
    # slag heap: a dark cone of waste rock
    {"type": "hill", "x": -48, "z": 44, "radius": 10.5, "height": 6.5, "falloff": "linear", "material": "rock"},
    # the lake below the village (a shallow sandy basin)
    {"type": "flatten", "x": -74, "z": 48, "radius": 12, "height": -1.8, "edge": 6, "mode": "cut", "material": "sand"},
    {"type": "flatten", "x": -86, "z": 42, "radius": 6.5, "height": -1.8, "edge": 5, "mode": "cut", "material": "sand"},
]
FEAT += HALF + [mirror_feature(f) for f in HALF]
# the church hill (on the axis)
FEAT += [{"type": "hill", "x": 0, "z": -66, "radius": 30, "height": 4.5},
         {"type": "flatten", "x": 0, "z": -67, "w": 58, "d": 24, "height": 3.75, "edge": 4, "mode": "cut"}]
# the mine pad
FEAT += [{"type": "flatten", "x": 0, "z": -8, "w": 92, "d": 66, "height": PAD, "edge": 10}]
# the open pit: three benches and haul ramps
FEAT += [{"type": "plateau", "x": 0, "z": 56, "w": 50, "d": 36, "height": PIT[0], "edge": 1.2, "mode": "cut", "material": "rock"},
         {"type": "plateau", "x": 0, "z": 56, "w": 36, "d": 26, "height": PIT[1], "edge": 1.2, "mode": "cut", "material": "rock"},
         {"type": "plateau", "x": 0, "z": 56, "w": 22, "d": 16, "height": PIT[2], "edge": 1.2, "mode": "cut", "material": "dirt"}]
PIT_RAMPS = [[[-31, 33, PAD], [-22, 45, PIT[0]]], [[23, 71, PIT[0]], [15, 62, PIT[1]]], [[-16, 45, PIT[1]], [-8, 52, PIT[2]]]]
PIT_RAMPS += [[[-p[0], p[1], p[2]] for p in r] for r in PIT_RAMPS]
for pts in PIT_RAMPS:
    FEAT.append({"type": "ramp", "points": pts, "width": 4.6, "edge": 1.5, "material": "dirt"})
# rail bed (level, straight through the mine) and the load-out yard
RAIL_Z = 18.0
FEAT += [{"type": "ramp", "points": [[-68, RAIL_Z, PAD], [68, RAIL_Z, PAD]], "width": 5, "edge": 3, "material": "dirt"},
         {"type": "flatten", "x": 0, "z": 26, "radius": 8, "height": PAD, "edge": 4}]
# gravel roads (follow the ground)
ROADS_W = [
    ("main road", [[-112, -14], [-90, -14], [-70, -15], [-56, -14], [-44, -12]], 6.0),
    ("village street", [[-90, -56], [-90, -14], [-90, 20], [-80, 28]], 5.5),
    ("church road", [[-20, -24], [-20, -50], [-8, -56]], 5.0),
    ("pit road", [[-30, 24], [-31, 33]], 4.6),
]
ROADS = ROADS_W + [(n + " east", [[-p[0], p[1]] for p in pts], w) for n, pts, w in ROADS_W]
for name, pts, w in ROADS:
    FEAT.append({"type": "ramp", "points": pts, "width": w, "edge": 2.0, "material": "dirt"})

SURF = {"grass": "#5a6a34", "dirt": "#7a6a52", "rock": "#6e6560", "sand": "#a49a78", "dirtSlope": 24, "rockSlope": 37, "variation": 0.14}


def terrain_spec(feats):
    return {"cell": 1, "margin": 32, "surface": SURF, "features": feats}


def q(v, s=0.25):
    return round(v / s) * s


T0 = tr.generate(terrain_spec(FEAT), HALF_X, HALF_Z)

# ================================================================ buildings: footprints from the files
BDEF = {}
for f in _glob.glob(os.path.join(REPO, "Buildings/*/building.json")):
    try:
        d = json.load(open(f))
    except ValueError:
        continue
    BDEF[d["name"]] = d


def local_rect(name):
    d = BDEF[name]
    xs, zs = [], []
    for fl in d["floors"]:
        for r in fl["rooms"]:
            for rc in ([r["rect"]] if "rect" in r else r["rects"]):
                xs += [rc[0], rc[2]]; zs += [rc[1], rc[3]]
    x0, z0, x1, z1 = min(xs) - 0.5, min(zs) - 0.5, max(xs) + 0.5, max(zs) + 0.5
    for e in d.get("extras", []):
        if "rect" in e:
            x0, z0, x1, z1 = min(x0, e["rect"][0]), min(z0, e["rect"][1]), max(x1, e["rect"][2]), max(z1, e["rect"][3])
        elif e["type"] == "block" and e["min"][1] < 2.0:
            x0, z0, x1, z1 = min(x0, e["min"][0]), min(z0, e["min"][2]), max(x1, e["max"][0]), max(z1, e["max"][2])
        elif e["type"] == "prop" and not e.get("onRoof"):
            a = e["at"]; px, pz = a[0], a[-1]
            if len(a) == 3 and a[1] > 1.5:
                continue
            x0, z0, x1, z1 = min(x0, px - 1.0), min(z0, pz - 1.0), max(x1, px + 1.0), max(z1, pz + 1.0)
    return x0, z0, x1, z1


def to_world(lx, lz, x, z, yaw, mirror=False):
    if mirror:
        lx = -lx
    a = math.radians(yaw); c, s = math.cos(a), math.sin(a)
    return x + lx * c + lz * s, z - lx * s + lz * c


def world_rect(name, x, z, yaw, mirror=False):
    x0, z0, x1, z1 = local_rect(name)
    pts = [to_world(a, b, x, z, yaw, mirror) for a in (x0, x1) for b in (z0, z1)]
    return min(p[0] for p in pts), min(p[1] for p in pts), max(p[0] for p in pts), max(p[1] for p in pts)


BUILDINGS = []   # (name, x, z, yaw, variant, seed, rect)


def building(name, x, z, yaw, variant=None, seed=None, pad=True, height=None):
    r = world_rect(name, x, z, yaw, (variant or {}).get("mirror", False))
    if pad:
        h = q(float(T0.sample(x, z))) if height is None else height
        FEAT.append({"type": "flatten", "x": R((r[0] + r[2]) / 2), "z": R((r[1] + r[3]) / 2), "w": R(r[2] - r[0] + 3), "d": R(r[3] - r[1] + 3),
                     "height": h, "edge": 3})
    BUILDINGS.append((name, x, z, yaw, variant, seed, r))
    return r


# --- Honkala (Blue, west) and Kuusela (Red, east): the street runs north-south at x = -+90
building("Finnish Wooden House", -102, -40, 90, {"palette": "red ochre"}, 11)
building("Village Shop", -102, -22, 90, {"palette": "yellow ochre"}, 12)
building("Finnish Wooden House", -102, 12, 90, {"palette": "yellow ochre", "mirror": True}, 13)
building("Red Barn Cottage", -78, -44, 270, {"palette": "red ochre"}, 14)
building("Finnish Wooden House", -77, -30, 270, {"palette": "pale green"}, 15)
building("Garage", -78, 2, 270, {"palette": "white block"}, 16)
building("Red Barn Cottage", -104, 28, 90, {"palette": "yellow ochre"}, 17)
building("Lakeside Sauna", -74, 30, 0, {"palette": "tarred log"}, 18)

building("Finnish Wooden House", 102, -40, 270, {"palette": "pale green"}, 21)
building("Village Shop", 102, -22, 270, {"palette": "red ochre"}, 22)
building("Finnish Wooden House", 102, 12, 270, {"palette": "red ochre"}, 23)
building("Hunting Lodge", 80, -44, 90, {"palette": "pine log"}, 24)
building("Red Barn Cottage", 77, -30, 90, {"palette": "yellow ochre"}, 25)
building("Garage", 78, 2, 90, {"palette": "sooty brick"}, 26)
building("Red Barn Cottage", 104, 28, 270, {"palette": "weathered grey"}, 27)
building("Lakeside Sauna", 74, 30, 0, {"palette": "red ochre"}, 28)

# --- Kirkonmaki, the church hamlet
building("Wooden Church", 0, -70, 0, {"palette": "red ochre"}, 31, height=3.75)
building("Finnish Wooden House", -24, -68, 90, {"palette": "yellow ochre"}, 32, height=3.75)
building("Finnish Wooden House", 24, -68, 270, {"palette": "red ochre", "mirror": True}, 33, height=3.75)

# --- the mine (on the pad)
building("Recycling Plant", 0, -38, 0, {"palette": "blue steel"}, 41, height=PAD)            # the concentrator
building("Substation Control House", -26, -12, 90, {"palette": "grey concrete"}, 42, height=PAD)   # the hoist house
building("Machine Shop", 28, -12, 270, {"palette": "works green"}, 43, height=PAD)           # the workshop
building("Office Building", -30, -38, 90, {"palette": "beige brick"}, 44, height=PAD)
building("Garage", 30, -38, 270, {"palette": "white block"}, 45, height=PAD)

# portal pads (where the adits break out)
PORTALS_W = [("west adit", -63, -22), ("village adit", -66, 30), ("church adit", -34, -56)]
for name, x, z in PORTALS_W:
    for sx in (1, -1):
        FEAT.append({"type": "flatten", "x": x * sx, "z": z, "radius": 5, "edge": 3})

# ================================================================ the tunnels (terrain caves)
CAVES_W = [
    ("main drift", [[-40, -4, F], [0, -4, F]], 6, 3.8, "none"),
    ("west adit", [[-40, -4, F], [-50, -14, -5.5], [-63, -22]], 5.5, 3.6, "end"),
    ("village drift", [[-40, -4, F], [-48, 10, F], [-58, 22, -5.0], [-66, 30]], 5.5, 3.6, "end"),
    ("haulage loop", [[-40, -4, F], [-22, 14, F], [0, 18, F]], 5.5, 3.6, "none"),
    ("pump station", [[-22, 14, F]], 11, 4.6, "none"),
    ("church adit", [[0, -38, F], [-16, -50, -5.0], [-34, -56]], 5.5, 3.6, "end"),
]
CAVES = []
for n, pts, w, h, op in CAVES_W:
    CAVES.append((n, pts, w, h, op))
    m = [[-p[0]] + p[1:] for p in pts]
    if any(abs(p[0]) > 0.01 for p in pts):
        CAVES.append((n + " east", m, w, h, op))
CAVES = [c for c in CAVES if c[0] != "main drift east"] + [("main drift east", [[0, -4, F], [40, -4, F]], 6, 3.8, "none")]
CAVES = [c for c in CAVES if c[0] != "pump station east"] + [("magazine", [[22, 14, F]], 11, 4.6, "none")]
CAVES += [
    ("shaft station", [[0, -13, F], [0, 3, F]], 16, 5.5, "none"),
    ("north drift", [[0, -13, F], [0, -36, F]], 5.5, 3.6, "none"),
    ("crusher chamber", [[0, -38, F]], 14, 5.0, "none"),
    ("pit drift", [[0, 3, F], [0, 36, F], [0, 46, PIT[2]], [0, 51, PIT[2]]], 5.5, 3.6, "none"),
]
SHAFT = (0.0, -12.0)
CAVE_FEATS = [{"type": "cave", "points": pts, "width": w, "height": h, "open": op} for (n, pts, w, h, op) in CAVES]
for c in CAVE_FEATS:
    if len(c["points"]) == 1:
        del c["open"]
HOLE = {"type": "hole", "shape": "polygon", "points": [[-1.7, -13.7], [1.7, -13.7], [1.7, -10.3], [-1.7, -10.3]]}

TERRAIN = terrain_spec(FEAT + CAVE_FEATS + [HOLE])
T = tr.generate(terrain_spec(FEAT), HALF_X, HALF_Z)       # the surface (caves aside)


def gy(x, z):
    return float(T.sample(x, z))


def cave_floor(x, z):
    """Floor of the cave at (x, z) where it's flat (middle of a tunnel, inside a chamber), else None. A port of
    TerrainVoidGen.CaveOp's floor: the tunnel's centre-line height, rising into the walls past 0.6 of the half width."""
    best = None
    for (n, pts, w, h, op) in CAVES:
        P = []
        for k, p in enumerate(pts):
            end = (k == 0 and op in ("both", "start")) or (k == len(pts) - 1 and op in ("both", "end"))
            P.append((p[0], p[1], p[2] if len(p) > 2 else (gy(p[0], p[1]) if end else gy(p[0], p[1]) - 6)))
        half = w / 2
        if len(P) == 1:
            d = math.hypot(x - P[0][0], z - P[0][1]); f0 = P[0][2]
        else:
            d = 1e9; f0 = None
            for k in range(len(P) - 1):
                ax, az, ay = P[k]; bx, bz, by = P[k + 1]
                L2 = (bx - ax) ** 2 + (bz - az) ** 2
                tt = max(0.0, min(1.0, ((x - ax) * (bx - ax) + (z - az) * (bz - az)) / L2))
                dd = math.hypot(ax + (bx - ax) * tt - x, az + (bz - az) * tt - z)
                if dd < d:
                    d, f0 = dd, ay + (by - ay) * tt
        u = d / half
        if u < 0.6:
            if best is None or f0 < best[0]:
                best = (f0, h)
    return best


def in_cave_zone(x, z, m=0.0):
    """Within (half width + m) of any cave centre line: the surface there sits over a tunnel (or a portal trench)."""
    for (n, pts, w, h, op) in CAVES:
        if len(pts) == 1:
            if math.hypot(x - pts[0][0], z - pts[0][1]) < w / 2 * 1.5 + m:
                return True
            continue
        for k in range(len(pts) - 1):
            ax, az = pts[k][0], pts[k][1]; bx, bz = pts[k + 1][0], pts[k + 1][1]
            L2 = (bx - ax) ** 2 + (bz - az) ** 2
            tt = max(0.0, min(1.0, ((x - ax) * (bx - ax) + (z - az) * (bz - az)) / L2))
            if math.hypot(ax + (bx - ax) * tt - x, az + (bz - az) * tt - z) < w / 2 * 1.5 + m:
                return True
    return False


def portal_zone(x, z, m=0.0):
    """Near a portal trench (an open cave end, where the surface is cut away) or the pit drift's cut."""
    for (n, pts, w, h, op) in CAVES:
        if op == "end":
            ex, ez = pts[-1][0], pts[-1][1]; px, pz = pts[-2][0], pts[-2][1]
            for f in np.linspace(0, 1, 9):
                if math.hypot(x - (ex + (px - ex) * f * 0.7), z - (ez + (pz - ez) * f * 0.7)) < w / 2 * 1.6 + m:
                    return True
    if abs(x) < 4.5 + m and 38 < z < 52:
        return True
    return False


# ================================================================ objectives (decided before the dressing)
BLUE_SP = [(-91, -48), (-87, -36), (-91, -26), (-89, -8), (-91, -2), (-87, 18), (-97, -31), (-109, -4), (-107, 0),
           (-97, -54), (-82, -12), (-84, 16)]
RED_SP = [(-x, z) for x, z in BLUE_SP]
FFA_SP = [(-40, -28), (40, -28), (-16, 32), (16, 32), (-46, -50), (46, -50), (-62, -36), (62, -36)]
HILLS = [(0.0, -1.0, F, 5.0), (0.0, 56.0, None, 6.0), (0.0, 26.0, None, 6.0)]
FLAGS = [(-101.0, -3.0), (101.0, -3.0)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.keepout.append((x, z, 2.6))
for x, z, y, r in HILLS:
    if y is None:
        t.keepout.append((x, z, r + 0.8))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))

# ================================================================ op helpers (world-anchored boxes, meshes, props)
SOLID_BOXES = 0


def wblock(x0, y0, z0, x1, y1, z1, c, mark=True):
    global SOLID_BOXES
    x0, x1 = min(x0, x1), max(x0, x1); z0, z1 = min(z0, z1), max(z0, z1)
    t.ops.append({"op": "Block", "x0": R(x0), "y0": R(y0), "z0": R(z0), "x1": R(x1), "y1": R(y1), "z1": R(z1), "color": col(c), "anchor": "world"})
    SOLID_BOXES += 1
    if mark:
        g = gy((x0 + x1) / 2, (z0 + z1) / 2)
        r = (x0, z0, x1, z1)
        if y0 < g + 1.8:
            t.mark(t.occ, r)
        if y0 < g + 1.7 and y1 > g + 0.55:
            t.mark(t.walk, r)
        if y0 < g + 1.6 and y1 > g + 1.6:
            t.mark(t.tall, r)
    t.boxes.append((x0, z0, x1, z1, y0 - gy((x0 + x1) / 2, (z0 + z1) / 2), y1 - gy((x0 + x1) / 2, (z0 + z1) / 2), c))


def wmesh(kind, x, z, c, y, **kw):
    op = {"op": "Mesh", "mesh": kind, "x": R(x), "z": R(z), "y": R(y)}
    for k, v in kw.items():
        op[k] = R(v) if isinstance(v, float) else v
    op["color"] = col(c)
    op["anchor"] = "world"
    t.ops.append(op); t.meshes += 1


def wprop(name, x, z, yaw, y, tag="world", mark=False, **extra):
    if name not in t.P:
        raise KeyError(name)
    op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
    if yaw % 360:
        op["yaw"] = R(yaw % 360)
    op["y"] = R(y); op["anchor"] = "world"
    op.update(extra)
    t.ops.append(op); t.props.append((name, x, z, yaw, y, tag))
    if mark and t.P[name]["solid"]:
        r = t.foot(name, x, z, yaw); t.mark(t.occ, r)
        if t.P[name]["h"] > 0.55:
            t.mark(t.walk, r)
    t.notes[tag] = t.notes.get(tag, 0) + 1


def gprop(name, x, z, yaw=0.0, tag="dress", check=True, **extra):
    """A prop on the ground (checked against the occupancy grid)."""
    ok = t.prop(name, x, z, yaw, check=check, tag=tag)
    if ok and extra:
        t.ops[-1].update(extra)
    return ok


def decal(name, x, z, y=0.0, onto="floor", world=False, **kw):
    op = {"op": "Decal", "decal": name, "x": R(x), "z": R(z)}
    if y:
        op["y"] = R(y)
    if onto != "floor":
        op["onto"] = onto
    op.update(kw)
    if world:
        op["anchor"] = "world"
    t.ops.append(op)


def scatter(names, x0, z0, x1, z1, count, seed, **kw):
    op = {"op": "DecalScatter", "decals": names, "x0": R(x0), "z0": R(z0), "x1": R(x1), "z1": R(z1), "count": count, "seed": seed}
    op.update(kw)
    t.ops.append(op)


# ================================================================ buildings (ops) and their occupancy
for (name, x, z, yaw, variant, seed, r) in BUILDINGS:
    op = {"op": "Building", "name": name, "at": [x, z]}
    if yaw:
        op["yaw"] = yaw
    if seed is not None:
        op["seed"] = seed
    if variant:
        op["variant"] = variant
    t.ops.append(op)
    t.mark(t.occ, r); t.mark(t.walk, (r[0] + 0.6, r[1] + 0.6, r[2] - 0.6, r[3] - 0.6)); t.mark(t.tall, (r[0] + 0.6, r[1] + 0.6, r[2] - 0.6, r[3] - 0.6))
    t.exclude_rect(r[0] - 0.5, r[1] - 0.5, r[2] + 0.5, r[3] + 0.5)


def bldg(name, x, z):
    for b in BUILDINGS:
        if b[0] == name and b[1] == x and b[2] == z:
            return b
    raise KeyError(name)


# ================================================================ the headframe (concrete tower over the shaft)
SX, SZ = SHAFT
HF = (SX - 6, SZ - 6, SX + 6, SZ + 6)          # the hoist hall, 12 x 12 m
WT, HALL_H, DOOR_W, DOOR_H = 0.5, 6.0, 3.2, 4.2


def wall_x(x0, x1, z0, z1, y0, y1, c, gap=None):
    """A wall along X with an optional doorway (centre, width, head)."""
    if gap is None:
        wblock(x0, y0, z0, x1, y1, z1, c); return
    gc, gw, gh = gap
    wblock(x0, y0, z0, gc - gw / 2, y1, z1, c)
    wblock(gc + gw / 2, y0, z0, x1, y1, z1, c)
    wblock(gc - gw / 2, y0 + gh, z0, gc + gw / 2, y1, z1, c)


def wall_z(x0, x1, z0, z1, y0, y1, c, gap=None):
    if gap is None:
        wblock(x0, y0, z0, x1, y1, z1, c); return
    gc, gw, gh = gap
    wblock(x0, y0, z0, x1, y1, gc - gw / 2, c)
    wblock(x0, y0, gc + gw / 2, x1, y1, z1, c)
    wblock(x0, y0 + gh, gc - gw / 2, x1, y1, gc + gw / 2, c)


y0 = PAD
wall_x(HF[0], HF[2], HF[1], HF[1] + WT, y0, y0 + HALL_H, CONCRETE, (SX, DOOR_W, DOOR_H))
wall_x(HF[0], HF[2], HF[3] - WT, HF[3], y0, y0 + HALL_H, CONCRETE, (SX, DOOR_W, DOOR_H))
wall_z(HF[0], HF[0] + WT, HF[1] + WT, HF[3] - WT, y0, y0 + HALL_H, CONCRETE, (SZ, DOOR_W, DOOR_H))
wall_z(HF[2] - WT, HF[2], HF[1] + WT, HF[3] - WT, y0, y0 + HALL_H, CONCRETE, (SZ, DOOR_W, DOOR_H))
wblock(HF[0] - 0.3, y0 + HALL_H, HF[1] - 0.3, HF[2] + 0.3, y0 + HALL_H + 0.5, HF[3] + 0.3, CONC_D, mark=False)       # hall roof
TOWER = (SX - 4.5, SZ - 4.5, SX + 4.5, SZ + 4.5)
TOP = y0 + 30.0
wblock(TOWER[0], y0 + HALL_H + 0.5, TOWER[1], TOWER[2], TOP, TOWER[3], CONCRETE, mark=False)                          # the tower shaft
wblock(TOWER[0] - 1.2, TOP, TOWER[1] - 1.5, TOWER[2] + 1.2, TOP + 5.5, TOWER[3] + 1.5, CONC_D, mark=False)           # the winder house on top
wblock(TOWER[0] - 1.4, TOP + 5.5, TOWER[1] - 1.7, TOWER[2] + 1.4, TOP + 5.9, TOWER[3] + 1.7, DARK, mark=False)
# shaft railings round the hole (open on the south side: the cage gate)
wblock(SX - 2.0, y0, SZ - 2.0, SX + 2.0, y0 + 1.25, SZ - 1.9, (0.85, 0.7, 0.12))
wblock(SX - 2.0, y0, SZ - 1.9, SX - 1.9, y0 + 1.25, SZ + 1.9, (0.85, 0.7, 0.12))
wblock(SX + 1.9, y0, SZ - 1.9, SX + 2.0, y0 + 1.25, SZ + 1.9, (0.85, 0.7, 0.12))
# cosmetic detail: window bands, the big sheave wheels on the winder house, pipes, ribs
for side in (-1, 1):
    for k in range(4):
        wmesh("box", SX, SZ + side * 4.53, GLASS_D, y0 + 10 + k * 5.0, width=4.0, height=1.4, depth=0.06)
        wmesh("box", SX + side * 4.53, SZ, GLASS_D, y0 + 10 + k * 5.0, width=0.06, height=1.4, depth=4.0)
    wmesh("box", SX + side * 5.75, SZ, GLASS_D, TOP + 2.0, width=0.06, height=1.8, depth=7.0)
    for zz in (SZ - 3.0, SZ + 3.0):
        wmesh("box", SX + side * 4.6, zz, CONC_D, y0 + HALL_H + 0.5, width=0.4, height=TOP - y0 - HALL_H - 0.5, depth=0.5)
for xx in (SX - 2.2, SX + 2.2):
    wmesh("torus", xx, SZ - 6.3, STEEL, TOP + 6.4, radius=1.9, inner=0.16, pitch=90, segments=20)
    wmesh("box", xx, SZ - 6.3, STEEL, TOP + 5.9, width=0.3, height=2.4, depth=0.3)
# the shaft collar (a steel frame round the hole, flat) and cage guides down the shaft
wmesh("box", SX, SZ, DARK, y0 + 0.01, width=4.2, height=0.04, depth=4.2)
for xx in (SX - 1.75, SX + 1.75):
    wmesh("box", xx, SZ, RUST, F, width=0.12, height=y0 - F + 5.0, depth=0.2)

# the cage (an elevator mover): from the shaft station floor up to the hall
MOVERS = [{"type": "elevator", "name": "Shaft cage", "at": [SX, R(F + 0.3), SZ], "rise": R(PAD + 0.05 - (F + 0.3)), "speed": 1.6, "dwell": 3.0,
           "boxes": [{"size": [3.1, 0.3, 3.1], "color": col((0.55, 0.45, 0.12))},
                     {"size": [3.1, 0.4, 0.08], "offset": [0, 0.2, -1.51], "color": col((0.85, 0.7, 0.12))}]}]

# ================================================================ the concentrator conveyor gallery and the load-out
plant = bldg("Recycling Plant", 0, -38)
pr = plant[6]
gal_z0, gal_z1 = TOWER[1] - 1.5, pr[3] - 1.0        # from the winder house to the plant's south wall
gal_y0, gal_y1 = TOP - 2.0, PAD + 9.5
L = gal_z0 - gal_z1
ang = math.degrees(math.atan2(gal_y0 - gal_y1, L))
wmesh("box", SX, (gal_z0 + gal_z1) / 2, (0.36, 0.42, 0.50), (gal_y0 + gal_y1) / 2 - 1.2, width=2.6, height=2.4, depth=math.hypot(L, gal_y0 - gal_y1) + 0.5,
      pitch=-ang, material="Metal")
for f_ in (0.35, 0.7):
    zz = gal_z0 - L * f_; yy = gal_y0 + (gal_y1 - gal_y0) * f_ - 1.6
    wblock(SX - 1.3, PAD, zz - 0.3, SX - 0.9, yy, zz + 0.3, STEEL)
    wblock(SX + 0.9, PAD, zz - 0.3, SX + 1.3, yy, zz + 0.3, STEEL)
    wmesh("box", SX, zz, STEEL, yy - 0.3, width=2.8, height=0.3, depth=0.6)
# load-out: an ore bin straddling the track, a belt up to it from the headframe hall
for xx in (-2.3, 2.3):
    wprop("Ore Bin Hopper", xx, RAIL_Z - 3.6, 0, PAD, tag="loadout", mark=True)
gprop("Belt Conveyor Incline", -9.0, RAIL_Z - 8.0, 0, tag="loadout", check=False)
gprop("Belt Conveyor Incline", 9.0, RAIL_Z - 8.0, 180, tag="loadout", check=False)

# ================================================================ the rail spur: track, wagons, buffers
for k in range(34):
    x = -66 + 2 + k * 4.0
    wprop("Railway Track Section", x, RAIL_Z, 0, PAD, tag="rail")
for sx in (-1, 1):
    wprop("Rail Buffer Stop", sx * 67.0, RAIL_Z, 0 if sx > 0 else 180, PAD, tag="rail", mark=True)
    for k, x in enumerate((11.5, 23.0, 34.5, 50.0)):
        if k == 3 and sx > 0:
            continue
        wprop("Ore Hopper Wagon", sx * x, RAIL_Z, 0, PAD + 0.32, tag="wagon", mark=True)
    if sx < 0:
        wprop("Ore Hopper Wagon", -50.0, RAIL_Z, 0, PAD + 0.32, tag="wagon", mark=True)
for sx in (-1, 1):
    gprop("Railway Crossing Lights", sx * 4.5 + 0.0, RAIL_Z + 3.2, 180 if sx < 0 else 0, tag="rail")
t.add_lane([(-66, RAIL_Z), (66, RAIL_Z)], 4.0)

# ================================================================ the mine yard: plant, vehicles, tanks, fences, lights
YARD = [
    ("Mine Dump Truck", -14, 6, 90), ("Mine Dump Truck", 15, 7, 270), ("Tracked Excavator", -6, 60, 30), ("Mine Dump Truck", 6, 56, 200),
    ("Diesel Storage Tank", 38, -24, 90), ("Diesel Storage Tank", 41, -24, 90), ("Forklift", -14, -24, 30), ("Pickup Truck", -40, -46, 90),
    ("Van", 38, -46, 270), ("Pickup Truck", 16, -26, 0), ("Box Truck", -40, -26, 0), ("Utility Trailer", 40, 2, 0),
    ("Jaw Crusher Unit", -16, -50, 0), ("Mine Timber Stack", -10, 10, 90), ("Mine Timber Stack", 10, 10, 90), ("Spruce Log Pile", 42, 8, 90),
    ("Cable Spool", -6, -22, 0), ("Pallet Stack", 8, -22, 0), ("Gas Bottle Cage", 21, -20, 270), ("Running Generator", -20, 4, 0),
    ("Mine Floodlight Mast", -20, -22, 180), ("Mine Floodlight Mast", 20, -22, 180), ("Mine Floodlight Mast", -22, 22, 0), ("Mine Floodlight Mast", 22, 22, 0),
    ("Mine Floodlight Mast", 0, 36, 0), ("Concrete Block", -8, 30, 0), ("Concrete Block", 8, 30, 0), ("Water Barrier", -28, 30, 90), ("Water Barrier", 28, 30, 90),
    ("Pallet", -12, -26, 20), ("IBC Tote", 34, 6, 0), ("Oil Drum", 33, 4, 0), ("Rusty Drum", 35, 3.5, 0), ("Tyre Stack", -36, 4, 0),
    ("Builders Skip", -36, 8, 90), ("Portable Toilet", -42, -8, 90), ("Portable Toilet", 42, -8, 270),
]
for name, x, z, yaw in YARD:
    gprop(name, x, z, yaw, tag="yard")
# chain-link fence round the north yard (gaps for the roads)
for k in range(9):
    for sx in (-1, 1):
        x = sx * (44.0)
        z = -48 + k * 3.1
        if -16 < z < -10:
            continue
        gprop("Chain Link Fence", x, z, 90, tag="fence")
# the open pit: an excavator and a truck on the floor, timber and barriers on the benches, warning signs at the rim
for sx in (-1, 1):
    gprop("Blasting Warning Sign", sx * 27.0, 35.0, 0, tag="pit")
    gprop("Concrete Block", sx * 12.0, 40.4, 0, tag="pit")
    gprop("Water Barrier", sx * 20.0, 41.6, 0, tag="pit")
    gprop("Mine Timber Stack", sx * 21.0, 66.0, 90, tag="pit")
    gprop("Granite Boulder", sx * 6.0, 62.0, rng.uniform(0, 360), tag="pit")
    gprop("Granite Outcrop", sx * 20.5, 52.0, 90, tag="pit")
    gprop("Ore Mine Cart", sx * 3.0, 50.5, 0, tag="pit")

# ================================================================ inside the tunnels
TUN_TAG = "tunnel"


def tunnel_dress(pts, every_rail=3.0, every_frame=6.5, every_lamp=8.0, width=5.5, height=3.6, skip=()):
    """Rails down the middle, timber frames and cage lamps along a drift ([x, z, floor] points)."""
    for k in range(len(pts) - 1):
        ax, az, ay = pts[k]; bx, bz, by = pts[k + 1]
        L = math.hypot(bx - ax, bz - az); ux, uz = (bx - ax) / L, (bz - az) / L
        yaw_r = yaw_along(ux, uz); yaw_f = yaw_facing(ux, uz)
        s = 1.5
        while s < L - 1.0:
            x, z, y = ax + ux * s, az + uz * s, ay + (by - ay) * s / L
            if not any(math.hypot(x - a, z - b) < r for a, b, r in skip):
                wprop("Mine Rail Section", x, z, yaw_r, y + 0.01, tag=TUN_TAG)
            s += every_rail
        s = 3.0
        while s < L - 2.0:
            x, z, y = ax + ux * s, az + uz * s, ay + (by - ay) * s / L
            if not any(math.hypot(x - a, z - b) < r for a, b, r in skip):
                wprop("Tunnel Timber Frame", x, z, yaw_f, y, tag=TUN_TAG)
            s += every_frame
        s = 5.0
        while s < L - 2.0:
            x, z, y = ax + ux * s, az + uz * s, ay + (by - ay) * s / L
            if not any(math.hypot(x - a, z - b) < r for a, b, r in skip):
                wprop("Tunnel Cage Lamp", x, z, 0, y + height - 1.0, tag=TUN_TAG)
            s += every_lamp


STATIONS = [(0, -5, 8.0), (-22, 14, 5.5), (22, 14, 5.5), (0, -38, 7.0), (0, 18, 3.0)]
for (n, pts, w, h, op) in CAVES:
    if len(pts) < 2 or n == "shaft station":
        continue
    P = []
    for k, p in enumerate(pts):
        if len(p) > 2:
            P.append(tuple(p))
        else:
            P.append((p[0], p[1], gy(p[0], p[1])))
    if op == "end":
        P = P[:-1] + [(P[-2][0] + (P[-1][0] - P[-2][0]) * 0.75, P[-2][1] + (P[-1][1] - P[-2][1]) * 0.75, P[-2][2] + (P[-1][2] - P[-2][2]) * 0.75)]
    if n == "pit drift":
        P = P[:2]
    tunnel_dress(P, width=w, height=h, skip=STATIONS)
# the shaft station: lockers, a tool bench, carts at the cage, signs, lamps
ST = [("Ore Mine Cart", -2.8, -7.5, 0), ("Ore Mine Cart", 2.8, -7.5, 0), ("Metal Lockers", -5.6, -10.0, 90), ("Metal Lockers", -5.6, -8.0, 90),
      ("Workbench" if "Workbench" in t.P else "Table", 5.4, -9.0, 270), ("Toolbox", 5.4, -9.0, 270), ("Mine Timber Stack", -5.0, 6.0, 0), ("Wooden Crate", 4.6, 5.5, 0),
      ("Wooden Crate", 5.4, 6.4, 30), ("Oil Drum", -5.6, 1.0, 0), ("Fire Extinguisher" if "Fire Extinguisher" in t.P else "Gas Cylinders", 5.6, 1.5, 0)]
for name, x, z, yaw in ST:
    if name in t.P:
        wprop(name, x, z, yaw, F + (0.85 if name == "Toolbox" else 0.0), tag="station", mark=True)
for (x, z) in ((-3, -3), (3, -3), (-3, 4), (3, 4), (0, -9)):
    wprop("Tunnel Cage Lamp", x, z, 0, F + 4.3, tag="station")
# the pump station (west) and the magazine (east)
for name, x, z, yaw in (("Diesel Generator", -24.5, 12.0, 0), ("Running Generator", -19.5, 16.5, 90), ("Concrete Pipe", -22.0, 17.5, 0),
                        ("Gas Cylinders", -25.5, 16.0, 0), ("Oil Drum", -18.6, 11.0, 0), ("Rusty Drum", -18.0, 12.2, 0)):
    wprop(name, x, z, yaw, F, tag="pump", mark=True)
for name, x, z, yaw in (("Blasting Explosives Crate", 24.6, 11.4, 0), ("Blasting Explosives Crate", 25.0, 12.4, 90), ("Blasting Explosives Crate", 24.4, 16.6, 0),
                        ("Wooden Crate", 19.4, 16.4, 0), ("Wooden Crate", 19.6, 11.6, 20), ("Mine Timber Stack", 22.0, 17.6, 0), ("Supply Crate" if "Supply Crate" in t.P else "Small Crate", 19.4, 14.0, 90)):
    wprop(name, x, z, yaw, F, tag="magazine", mark=True)
for (x, z) in ((-22, 14), (22, 14), (0, -38)):
    wprop("Tunnel Cage Lamp", x, z, 0, F + 3.4, tag="chamber")
# the crusher chamber: the crusher under the ore pass, a hopper, carts
for name, x, z, yaw in (("Jaw Crusher Unit", -2.5, -41.5, 0), ("Ore Bin Hopper", 3.2, -41.0, 0), ("Ore Mine Cart", -3.5, -35.0, 90), ("Mine Timber Stack", 4.0, -35.0, 90)):
    wprop(name, x, z, yaw, F, tag="crusher", mark=True)
# carts parked in the drifts (cover)
for (x, z, yaw) in ((-30.0, -5.5, 0), (30.0, -2.5, 0), (-12.0, 17.6, 10), (12.0, 17.6, 350), (-46.0, 6.0, 70), (46.0, 6.0, 290), (1.5, 28.0, 90), (-1.5, -26.0, 90)):
    fl = cave_floor(x, z)
    if fl:
        wprop("Ore Mine Cart", x, z, yaw, fl[0], tag="cart", mark=True)
# portals: concrete adit frames at each tunnel mouth (look back down the tunnel)
for (n, pts, w, h, op) in CAVES:
    if op != "end":
        continue
    ex, ez = pts[-1][0], pts[-1][1]; px, pz = pts[-2][0], pts[-2][1]
    Lp = math.hypot(ex - px, ez - pz); ux, uz = (px - ex) / Lp, (pz - ez) / Lp
    # where the roof starts: about where the floor is 4.6 m under the surface
    for s in np.linspace(2, Lp, 60):
        x, z = ex + ux * s, ez + uz * s
        fy = gy(ex, ez) + (pts[-2][2] - gy(ex, ez)) * s / Lp
        if gy(x, z) - fy > h + 1.05:
            break
    yaw = yaw_facing(-ux, -uz)
    wmesh("box", x, z, CONC_D, fy + h - 0.2, width=w * 0.95, height=0.9, depth=0.6, yaw=yaw, material="Concrete")
    for side in (-1, 1):
        nx_, nz_ = -uz * side, ux * side
        wmesh("box", x + nx_ * w * 0.42, z + nz_ * w * 0.42, CONC_D, fy, width=0.6, height=h, depth=0.6, yaw=yaw, material="Concrete")
    t.notes["portal"] = t.notes.get("portal", 0) + 1
    decal("Ulos Sign", x + ux * 1.5, z + uz * 1.5, y=fy + 2.2, onto="wall", world=True, reach=4)

# ================================================================ villages: street dressing, flags, yards
def village(sx, name_sign):
    X = lambda v: v * sx
    # the green with the flag pole, benches and the notice board
    gprop("Flag on a Pole", X(-101.0), -3.0 + 4.0, 0, tag="village", flag="finland")
    gprop("Bench", X(-104.5), -6.0, 90 if sx > 0 else 270, tag="village")
    gprop("Mailbox Rack", X(-93.6), -17.6, 90 if sx > 0 else 270, tag="village")
    gprop("Moose Warning Sign", X(-86.0), -54.0, 180, tag="village")
    gprop("Moose Warning Sign", X(-70.0), -18.2, 90 if sx > 0 else 270, tag="village")
    gprop("Bus Stop Sign", X(-93.2), -10.0, 90 if sx > 0 else 270, tag="village")
    for z in (-50, -34, -20, -2, 14, 26):
        gprop("Utility Pole", X(-93.4), z, 0, tag="village")
    for (x, z) in ((-95.5, -46), (-95.5, -28), (-95.5, 6), (-84.5, -36), (-84.5, -20), (-84.5, 10)):
        gprop("Picket Fence", X(x), z, 90, tag="village")
    gprop("Wheelie Bin Grey", X(-95.0), -14.6, 90, tag="village")
    gprop("Recycling Bin", X(-95.2), -13.2, 90, tag="village")
    gprop("Hatchback", X(-94.8), -32.0, 0, tag="village")
    gprop("Pickup Truck", X(-84.0), 6.5, 180, tag="village")
    gprop("Bicycle", X(-95.5), -24.0, 0, tag="village")
    gprop("Birch Firewood Stack", X(-108.5), -36.0, 90, tag="village")
    gprop("Garden Shed", X(-109.0), -46.0, 90 if sx < 0 else 270, tag="village")
    gprop("Laundry Line", X(-108.5), 8.0, 90, tag="village")
    gprop("Compost Bin", X(-107.5), 18.0, 0, tag="village")
    gprop("Wheelbarrow", X(-82.0), -40.0, 30, tag="village")
    gprop("Swing Set", X(-80.5), -52.0, 0, tag="village")
    gprop("Dog Kennel", X(-73.0), -36.5, 270 if sx < 0 else 90, tag="village")
    # the lake: water, jetty, rowing boats, canoe-free Finnish lake, ducks, reeds
    for (lx, lz, lr) in ((-74, 48, 12.5), (-86, 42, 7)):
        wmesh("cylinder", X(lx), lz, WATER, WATER_Y - 0.1, width=lr * 2 + 3, height=0.1, depth=lr * 2 + 3, segments=40)
    jx = X(-74.0)
    dy = WATER_Y + 0.55
    wblock(jx - 0.9, dy - 0.2, 35.2, jx + 0.9, dy, 45.0, DECK)
    wblock(jx - 2.6, dy - 0.2, 45.0, jx + 2.6, dy, 47.0, DECK)
    for zz in (36.0, 39.5, 43.0, 46.5):
        for xo in (-0.8, 0.8):
            wmesh("cylinder", jx + xo, zz, TIMBER, WATER_Y - 1.2, width=0.22, height=1.8, depth=0.22, segments=8)
    wprop("Jetty Ladder", jx + 2.0, 47.0, 0, dy - 1.1, tag="lake")
    wprop("Lifebuoy Post", jx - 1.5, 35.8, 90 if sx < 0 else 270, gy(jx - 1.5, 35.8), tag="lake")
    wprop("Wooden Rowing Boat", jx + 2.3, 41.0, 95, WATER_Y - 0.18, tag="lake")
    wprop("Wooden Rowing Boat", X(-80.5), 36.5, 70, gy(X(-80.5), 36.5), tag="lake")
    for k in range(5):
        a = rng.uniform(0, math.tau); fr = rng.uniform(0.3, 0.8)
        wprop("Duck on a Pond", X(-74) + math.cos(a) * 12 * fr, 48 + math.sin(a) * 12 * fr, rng.uniform(0, 360), WATER_Y, tag="lake")
    gprop("Birch Log Bench", X(-70.0), 34.6, 0, tag="lake")
    gprop("Sauna Bucket", X(-71.5), 34.4, 0, tag="lake")
    # the village name board at the road in
    bx = X(-70.0); bz = -10.6
    for xo in (-1.2, 1.2):
        wmesh("box", bx + xo, bz, GALV, gy(bx, bz) - 0.3, width=0.1, height=2.6, depth=0.1)
    wmesh("box", bx, bz, (0.12, 0.31, 0.6), gy(bx, bz) + 1.5, width=2.8, height=0.8, depth=0.06)
    decal(name_sign, bx, bz + 0.8, y=1.9, onto="wall-z", reach=2)
    decal(name_sign, bx, bz - 0.8, y=1.9, onto="wall+z", reach=2)


village(1, "Honkala Village Sign")
village(-1, "Kuusela Village Sign")
# shop fascias (the sign board faces the street: local +Z of a shop at yaw 90 is +X)
decal("Kauppa Fascia", -97.17 + 0.9, -22.0, y=3.28, onto="wall-x", reach=2)
decal("Kauppa Fascia", 97.17 - 0.9, -22.0, y=3.28, onto="wall+x", reach=2)
decal("Sauna Sign", -74.0, 30 + 2.15 + 0.8, y=2.0, onto="wall-z", reach=2)
decal("Sauna Sign", 74.0, 30 + 2.15 + 0.8, y=2.0, onto="wall-z", reach=2)

# ================================================================ the church hamlet
CH = 3.75
gprop("Flag on a Pole", -6.0, -53.0, 0, tag="hamlet", flag="finland")
for k in range(9):                     # the churchyard's low granite wall (props: instanced)
    for sx in (-1, 1):
        gprop("Granite Boulder", sx * 10.5, -80.0 + k * 2.8, rng.uniform(0, 360), tag="hamlet", scale=0.55)
# the bell tower (kellotapuli): a red ochre tower with a white-trimmed belfry and a shingle cap
bx, bz = 9.0, -55.0
wblock(bx - 1.6, CH, bz - 1.6, bx + 1.6, CH + 6.0, bz + 1.6, RED_OCHRE)
wblock(bx - 1.9, CH + 6.0, bz - 1.9, bx + 1.9, CH + 6.3, bz + 1.9, WHITE, mark=False)
for (ox, oz) in ((-1.5, -1.5), (1.5, -1.5), (-1.5, 1.5), (1.5, 1.5)):
    wmesh("box", bx + ox, bz + oz, WHITE, CH + 6.3, width=0.3, height=2.2, depth=0.3)
wmesh("box", bx, bz, (0.72, 0.55, 0.2), CH + 7.0, width=0.9, height=0.9, depth=0.9)           # the bell
wblock(bx - 2.0, CH + 8.5, bz - 2.0, bx + 2.0, CH + 8.8, bz + 2.0, DARK, mark=False)
wmesh("cone", bx, bz, DARK, CH + 8.8, width=4.2, height=4.5, depth=4.2, segments=4, yaw=45, material="Wood")
decal("Kirkonmaki Village Sign", -6.0, -50.6, y=1.9, onto="wall-z", reach=2)
for xo in (-7.2, -4.8):
    wmesh("box", xo, -51.4, GALV, gy(xo, -51.4) - 0.3, width=0.1, height=2.6, depth=0.1)
wmesh("box", -6.0, -51.4, (0.12, 0.31, 0.6), gy(-6, -51.4) + 1.5, width=3.2, height=0.8, depth=0.06)
gprop("Bench", -4.0, -58.5, 0, tag="hamlet")
gprop("Bench", 4.0, -58.5, 0, tag="hamlet")
gprop("Bike Rack", 12.0, -60.0, 90, tag="hamlet")
gprop("Mailbox Rack", -14.0, -56.5, 0, tag="hamlet")
gprop("Kicksled", 6.0, -59.5, 30, tag="hamlet")

# ================================================================ mine signage
of = bldg("Office Building", -30, -38); ws = bldg("Machine Shop", 28, -12)
decal("Headframe Lettering", SX, TOWER[3] + 1.2, y=TOP - 6.0 - PAD, onto="wall-z", reach=3)
decal("Headframe Lettering", SX, TOWER[1] - 1.2, y=TOP - 6.0 - PAD, onto="wall+z", reach=3)
decal("Rikastamo Sign", 0.0, pr[3] + 1.0, y=6.5, onto="wall-z", reach=3)
decal("Toimisto Sign", of[6][2] + 1.0, -38.0, y=3.0, onto="wall-x", reach=3)
decal("Korjaamo Sign", ws[6][0] - 1.0, -12.0, y=4.0, onto="wall+x", reach=4)
decal("Vaara Sign", SX + 6.8, SZ + 3.0, y=1.8, onto="wall-x", reach=2)
decal("Vaara Sign", SX - 6.8, SZ - 3.0, y=1.8, onto="wall+x", reach=2)
decal("Hissi Sign", SX + 3.0, SZ + 5.0, y=2.4, onto="wall+z", reach=2)
# the mine gate board on the church road and the main roads in
for (gx_, gz_) in ((-44.0, -18.4), (44.0, -18.4)):
    for xo in (-1.4, 1.4):
        wmesh("box", gx_ + xo, gz_, GALV, gy(gx_, gz_) - 0.3, width=0.12, height=3.0, depth=0.12)
    wmesh("box", gx_, gz_, (0.12, 0.31, 0.6), gy(gx_, gz_) + 1.4, width=3.6, height=1.1, depth=0.06)
    decal("Vuorikoski Mine Sign", gx_, gz_ + 0.8, y=2.0, onto="wall-z", reach=2)
# underground signs (on the cave walls, world heights)
decal("Taso Sign", 0.0, -4.0 + 0.0, y=F + 2.2, onto="wall+x", world=True, reach=9)
decal("Taso Sign", 0.0, 2.0, y=F + 2.2, onto="wall-x", world=True, reach=9)
decal("Rajahde Sign", 22.0, 14.0, y=F + 2.0, onto="wall+x", world=True, reach=7)
decal("Hissi Sign", 0.0, -9.0, y=F + 2.2, onto="wall-x", world=True, reach=9)

# ================================================================ the forest: solid cover, trees, understory
def blocked_zone(x, z, m=0.0):
    if in_cave_zone(x, z, m) and portal_zone(x, z, m):
        return True
    if portal_zone(x, z, m):
        return True
    if math.hypot(x - SX, z - SZ) < 9 + m:
        return True
    for (lx, lz, lr) in ((-74, 48, 13), (-86, 42, 8), (74, 48, 13), (86, 42, 8)):
        if math.hypot(x - lx, z - lz) < lr + m:
            return True
    if abs(x) < 26 + m and 37 - m < z < 75 + m:       # the pit
        return True
    return False


def scatter_cover(names, n, seed, x0, x1, z0, z1, mirror=True):
    rr = random.Random(seed); placed = 0
    for k in range(n * 8):
        if placed >= n:
            break
        x, z = rr.uniform(x0, x1), rr.uniform(z0, z1)
        if blocked_zone(x, z, 2.0) or not t.inside(x, z, 3.0) or not t.inside(-x, z, 3.0) or (abs(x) < 46 and -54 < z < 28) \
                or (68 < abs(x) < 112 and -56 < z < 34 and rr.random() < 0.85):
            continue
        nm = rr.choice(names); yaw = rr.uniform(0, 360)
        if t.prop(nm, x, z, yaw, lanes=True, gap=0.8, tag="forest"):
            placed += 1
            if mirror:
                t.prop(nm, -x, z, (360 - yaw) % 360, lanes=True, gap=0.8, check=False, tag="forest")
    return placed


for name, pts, w in ROADS:
    t.add_lane([(p[0], p[1]) for p in pts], w + 1.0)
    t.path([(p[0], p[1]) for p in pts], w - 0.5, density=0.04)
t.path([(-66, RAIL_Z), (66, RAIL_Z)], 4.0)
n_cover = scatter_cover(["Granite Boulder", "Granite Boulder", "Granite Outcrop", "Fallen Spruce Log", "Fallen Spruce Log", "Tree Stump", "Spruce Log Pile"],
                        150, 5071, -114, -1, -86, 88)
# glacial erratics: big boulders over head height that break the long lines through the woods
ERR = [(-58, -30, 1.9), (-40, -62, 1.8), (-80, -60, 1.7), (-96, -20, 1.6), (-66, 0, 1.8), (-56, 26, 1.7), (-34, 60, 1.8), (-98, 40, 1.6), (-20, -60, 1.5)]
for (x, z, s) in ERR:
    for sx in (1, -1):
        X = x * sx
        if t.prop("Granite Boulder", X, z, rng.uniform(0, 360), check=True, gap=0.5, tag="erratic"):
            t.ops[-1]["scale"] = s
            hw = 1.1 * s
            t.mark(t.walk, (X - hw, z - hw, X + hw, z + hw)); t.mark(t.tall, (X - hw * 0.7, z - hw * 0.7, X + hw * 0.7, z + hw * 0.7))

# hand trees round the villages and the church (birches in yards, spruce by the church)
HAND_TREES = [(-96, -36, "tree"), (-108, -14, "tree"), (-96, 20, "tree"), (-84, -50, "pine"), (-70, -38, "tree"), (-84, 26, "tree"),
              (-14, -80, "pine"), (-12, -62, "tree"), (-30, -76, "pine"), (-64, 36, "tree"), (-90, 34, "pine")]
for (x, z, kind) in HAND_TREES:
    for sx in (1, -1):
        t.tree(x * sx + rng.uniform(-0.6, 0.6), z + rng.uniform(-0.6, 0.6), rng.uniform(13, 19) if kind == "pine" else rng.uniform(9, 13), kind)

BX0, BZ0, BX1, BZ1 = t.x0, t.z0, t.x1, t.z1
AUTUMN_BIRCH = [0.82, 0.62, 0.16]
SPRUCE = [0.13, 0.24, 0.14]
# the forest: dark spruce and pine with stands of yellowing birch
t.region("pine", 0.024, rect=[BX0, BZ0, BX1, BZ1], height=[13, 21], color=SPRUCE)
t.region("tree", 0.007, rect=[BX0, BZ0, BX1, BZ1], height=[9, 14], color=AUTUMN_BIRCH)
for sx in (1, -1):
    t.region("tree", 0.012, polygon=[[sx * a, b] for a, b in ((-110, -60), (-70, -76), (-60, -40), (-104, -28))], height=[9, 14], color=[0.86, 0.68, 0.18])
    t.region("tree", 0.010, polygon=[[sx * a, b] for a, b in ((-64, 56), (-30, 64), (-30, 84), (-66, 78))], height=[8, 13], color=[0.78, 0.52, 0.14])
    t.region("pine", 0.012, polygon=[[sx * a, b] for a, b in ((-70, -40), (-46, -40), (-44, 10), (-64, 14))], height=[14, 22], color=SPRUCE)
# the understory: moss and grass, blueberry and lingonberry heath (shrubs, reddening), young spruce (bushes)
t.region("grass", 2.4, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.6, scale=[0.8, 1.3], color=[0.42, 0.46, 0.24])
t.region("shrub", 1.0, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.55, scale=[0.7, 1.1], color=[0.46, 0.30, 0.16])
t.region("shrub", 0.6, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.5, color=[0.30, 0.38, 0.18])
t.region("bush", 0.10, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.7, scale=[0.9, 1.7], color=[0.16, 0.28, 0.15])
# village meadows and lake shores: long grass, a few bushes
for sx in (1, -1):
    for poly in (((-114, -56), (-70, -56), (-68, 34), (-112, 34)), ((-92, 28), (-58, 28), (-58, 62), (-96, 56))):
        t.region("grass", 4.0, polygon=[[sx * a, b] for a, b in poly], patchiness=0.45, scale=[1.0, 1.7], color=[0.50, 0.52, 0.26])
        t.region("bush", 0.08, polygon=[[sx * a, b] for a, b in poly], patchiness=0.5, scale=[1.0, 1.6], color=[0.44, 0.30, 0.14])
# no-go: the pad's built-up yard keeps grass thin, no trees on the mine pad, the pit, roads, hills, spawns
t.exclude.append({"shape": "rect", "center": [0, -8], "halfX": 47, "halfZ": 34, "density": 0.25})
t.exclude.append({"shape": "rect", "center": [0, 56], "halfX": 27, "halfZ": 20, "density": 0.15})
t.exclude.append({"shape": "rect", "center": [0, -67], "halfX": 28, "halfZ": 12, "density": 0.6})
for (x, z, y, r) in HILLS:
    if y is None:
        t.exclude.append({"shape": "circle", "center": [R(x), R(z)], "radius": R(r + 1), "density": 0.3})
for (x, z) in BLUE_SP + RED_SP + FFA_SP:
    t.exclude.append({"shape": "circle", "center": [R(x), R(z)], "radius": 1.6, "density": 0.3})
for (x, z) in FLAGS:
    t.exclude.append({"shape": "circle", "center": [R(x), R(z)], "radius": 3.0})
for (lx, lz, lr) in ((-74, 48, 12.5), (-86, 42, 7), (74, 48, 12.5), (86, 42, 7)):
    t.exclude.append({"shape": "circle", "center": [lx, lz], "radius": lr + 0.5})
t.exclude.append({"shape": "rect", "center": [SX, SZ], "halfX": 7, "halfZ": 7})
for sx in (-1, 1):
    for name, x, z in PORTALS_W:
        t.exclude.append({"shape": "circle", "center": [R(sx * x), R(z)], "radius": 6.0, "density": 0.15})


# ================================================================ scenery beyond the edge: forested hills (mesh spruces and birches)
def outside_trees():
    rr = random.Random(77); n = 0
    pts = t.outline
    segs = [(pts[i], pts[(i + 1) % len(pts)]) for i in range(len(pts))]
    acc = 0.0; total = sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in segs)
    s = 0.0; i = 0
    while s < total:
        while True:
            a, b = segs[i % len(segs)]; L = math.hypot(b[0] - a[0], b[1] - a[1])
            if acc + L >= s:
                break
            acc += L; i += 1
        f = (s - acc) / max(L, 1e-6)
        x, z = a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f
        ux, uz = (b[0] - a[0]) / max(L, 1e-6), (b[1] - a[1]) / max(L, 1e-6)
        ox, oz = uz, -ux
        for k in range(3):
            d = rr.uniform(2.5, 7.0) + k * rr.uniform(6, 11)
            px, pz = x + ox * d + rr.uniform(-1, 1), z + oz * d + rr.uniform(-1, 1)
            if t.inside(px, pz, -2.0):
                continue
            rise = 6.0 * min(1.0, max(0.0, -t.edge(px, pz) / 8.0))
            gyv = gy(px, pz) + rise - 0.3
            if rr.random() < 0.78:
                h = rr.uniform(12, 20)
                wmesh("cylinder", px, pz, (0.30, 0.22, 0.15), gyv, width=0.5, height=h * 0.3, depth=0.5, segments=6)
                wmesh("cone", px, pz, (0.12, 0.22, 0.13), gyv + h * 0.18, width=h * 0.34, height=h * 0.82, depth=h * 0.34, segments=7, material="Dirt")
            else:
                h = rr.uniform(9, 13)
                wmesh("cylinder", px, pz, (0.86, 0.84, 0.78), gyv, width=0.32, height=h * 0.55, depth=0.32, segments=6)
                wmesh("sphere", px, pz, (0.80, 0.60, 0.16) if rr.random() < 0.6 else (0.62, 0.62, 0.20), gyv + h * 0.4, width=h * 0.42, height=h * 0.6, depth=h * 0.42, segments=8, detail=5)
            n += 1
        s += 2.6 * rr.uniform(0.8, 1.3)
    return n


N_SCENERY = outside_trees()

# ================================================================ spawns and waypoints
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
t.ops.append({"op": "AutoWaypoints", "spacing": 7})

# ================================================================ cosmetic clutter and decals (Clutter runs last in the game)
FLOOR = ["Birch Leaf Litter", "Birch Leaf Litter", "Autumn Bracken", "Autumn Bracken", "Mossy Stones", "Lingonberry Heath", "Lingonberry Heath",
         "Fly Agaric Toadstools", "Dead Pine Branch", "Dead Pine Branch", "Anthill"]
tiles = 0
step = 24.0
x = BX0
while x < BX1:
    z = BZ0
    while z < BZ1:
        cx_, cz_ = x + step / 2, z + step / 2
        if t.inside(cx_, cz_, -10) and not (abs(cx_) < 40 and -36 < cz_ < 22):
            t.clutter(FLOOR, x, z, x + step, z + step, 70, 5100 + tiles)
            tiles += 1
        z += step
    x += step
YARD_JUNK = ["Pallet", "Plank", "Plank Pile", "Tyre", "Oil Drum", "Rusty Drum", "Cinder Blocks", "Wooden Crate", "Cable Spool", "Litter", "Soda Cans", "Gas Cylinders"]
t.clutter(YARD_JUNK, -44, -48, 44, 24, 150, 5201)
t.clutter(["Litter", "Plank", "Rubble", "Cinder Blocks", "Tyre"], -24, 38, 24, 74, 40, 5202)
for sx, s_ in ((-1, 5203), (1, 5204)):
    t.clutter(["Flower Pot", "Garden Gnome", "Watering Can", "Bucket", "Litter", "Newspaper", "Bicycle", "Garden Tools", "Plank"],
              min(sx * 114, sx * 66), -56, max(sx * 114, sx * 66), 34, 70, s_)
t.clutter(["Litter", "Soda Cans", "Bottle", "Plastic Bag"], -30, -80, 30, -52, 30, 5205)

scatter(["Tyre Tracks", "Tyre Tracks", "Oil Stain", "Mud Patch", "Gravel Scatter", "Puddle", "Dirt Patch"], -44, -40, 44, 26, 130, 5301, gap=2.0)
scatter(["Mud Patch", "Puddle", "Gravel Scatter", "Tyre Tracks"], -112, -16, 112, -12, 50, 5302, gap=2.5)
scatter(["Moss Patch", "Moss Patch", "Leaf Litter", "Dry Grass", "Dirt Patch"], -114, -86, 114, 88, 420, 5303, gap=2.0)
scatter(["Rust Stain", "Oil Stain", "Gravel Scatter"], -66, 16, 66, 20, 50, 5304, gap=1.5)
for sx in (-1, 1):
    scatter(["Mud Patch", "Puddle", "Leaf Litter", "Gravel Scatter"], min(sx * 92, sx * 88), -56, max(sx * 92, sx * 88), 24, 30, 5310 + sx, gap=2.0)
# grime on the mine buildings and the headframe, wear in the villages
scatter(["Grime Streaks", "Rust Bleed", "Water Damage", "Wall Cracks", "Soot Plume"], -44, -50, 44, 0, 70, 5320, onto="wall")
scatter(["Peeling Paint", "Water Damage", "Mould", "Grime Streaks"], -114, -56, -66, 34, 30, 5321, onto="wall")
scatter(["Peeling Paint", "Water Damage", "Mould", "Grime Streaks"], 66, -56, 114, 34, 30, 5322, onto="wall")
scatter(["Graffiti Tag", "Graffiti Scribble"], -44, -20, 44, 0, 6, 5323, onto="wall")

# underground: wet floors and rust in the drifts (world heights), cobwebs and water spots on the ceilings
for (n, pts, w, h, op) in CAVES:
    if len(pts) == 1:
        x, z = pts[0][0], pts[0][1]
        for k in range(5):
            a = rng.uniform(0, math.tau); rr_ = rng.uniform(0, w * 0.25)
            decal(rng.choice(["Water Stain", "Puddle", "Oil Stain", "Gravel Scatter"]), x + math.cos(a) * rr_, z + math.sin(a) * rr_, y=F + 0.3, world=True)
        continue
    for k in range(len(pts) - 1):
        a, b = pts[k], pts[k + 1]
        if len(a) < 3 or len(b) < 3:
            continue
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        for s in np.arange(4.0, L, 7.0):
            x = a[0] + (b[0] - a[0]) * s / L; z = a[1] + (b[1] - a[1]) * s / L; y = a[2] + (b[2] - a[2]) * s / L
            decal(rng.choice(["Water Stain", "Puddle", "Mud Patch", "Rust Stain", "Gravel Scatter"]), x + rng.uniform(-1, 1), z + rng.uniform(-1, 1), y=y + 0.3, world=True)

# ================================================================ the map
foliage = {"seed": 506, "surfaces": {"Wood": 0.0, "Plain": 0.1}, "paths": t.paths, "regions": t.regions, "items": t.items,
           "exclude": t.exclude, "include": t.include}
mp = {
    "format": "call-of-slop-map/1",
    "name": "Vuorikoski",
    "description": "Finland in early autumn: a big copper mine in a spruce and birch forest, its tunnels under everything, between two wooden lake villages (Blue Honkala, Red Kuusela) and a church on the hill.",
    "author": "Call of Slop",
    "order": 506,
    "maxPlayers": 24,
    "flagPool": ["finland"],
    "sky": "#a9b8c4",
    "skybox": {"preset": "clear", "horizon": "#c4cdd0", "zenith": "#6f8fb4", "sun": {"time": 15.2, "latitude": 63, "day": 262},
               "clouds": {"coverage": 0.55, "speed": 7, "heading": 70}, "haze": {"color": "#e6dcc4", "strength": 0.25}, "fog": {"max": 0.9}},
    "boundary": {"shape": "smooth", "points": [list(p) for p in CTRL], "noise": NOISE, "edge": {"style": "cliff", "height": 6, "width": 8},
                 "groundMargin": 10, "barrierHeight": 3},
    "terrain": TERRAIN,
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [dict({"x": x, "z": z, "radius": r}, **({"y": y} if y is not None else {})) for x, z, y, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": t.ops,
    "foliage": foliage,
    "movers": MOVERS,
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

# ================================================================ report
from collections import Counter
cats = Counter()
for name, x, z, yaw, y, tag in t.props:
    cats[t.P[name]["cat"]] += 1
blocks = sum(1 for o in t.ops if o["op"] == "Block")
print(f"area {t.area():.0f} m2, half {HALF_X:.1f} x {HALF_Z:.1f}, blocks {blocks}, buildings {len(BUILDINGS)}, hand trees {len(t.items)}, meshes {t.meshes} "
      f"(scenery trees {N_SCENERY}), hand props {len(t.props)}, cover {n_cover}x2, decal ops {sum(1 for o in t.ops if o['op'] == 'Decal')}")
print("props by category:", dict(sorted(cats.items())))
print("props by rule:", dict(sorted(t.notes.items())))
print("clutter asked:", sum(o["count"] for o in t.ops if o["op"] == "Clutter"), "in", sum(1 for o in t.ops if o["op"] == "Clutter"), "ops")
print(f"terrain {T.nx} x {T.nz}, heights {T.min:.2f}..{T.max:.2f}")
for (x, z, y, r) in HILLS:
    print(f"hill ({x},{z}) ground {gy(x, z):.2f} cave {cave_floor(x, z)}")
for (n, pts, w, h, op) in CAVES:
    if op == "end":
        print(f"{n}: portal ground {gy(pts[-1][0], pts[-1][1]):.2f}, previous point floor {pts[-2][2]} at ground {gy(pts[-2][0], pts[-2][1]):.2f}")
for x, z in BLUE_SP:
    print(f"spawn ({x},{z}) ground {gy(x, z):.2f}", end="; ")
print()
for b in BUILDINGS:
    print(f"{b[0]} at ({b[1]},{b[2]}) yaw {b[3]}: rect {tuple(round(v, 1) for v in b[6])} ground {gy(b[1], b[2]):.2f}")

if OUT_PNG:
    marks = [("circle", x, z, r, (255, 230, 0)) for x, z, y, r in HILLS]
    marks += [("dot", x, z, 1.2, (60, 120, 255)) for x, z in BLUE_SP] + [("dot", x, z, 1.2, (255, 60, 60)) for x, z in RED_SP]
    marks += [("dot", x, z, 1.0, (240, 240, 240)) for x, z in FFA_SP]
    for b in BUILDINGS:
        x0, z0, x1, z1 = b[6]
        for a_, b_ in (((x0, z0), (x1, z0)), ((x1, z0), (x1, z1)), ((x1, z1), (x0, z1)), ((x0, z1), (x0, z0))):
            marks.append(("line", a_, b_, 0, (40, 40, 220)))
    for (n, pts, w, h, op) in CAVES:
        for k in range(len(pts) - 1):
            marks.append(("line", pts[k][:2], pts[k + 1][:2], 0, (255, 40, 200)))
        if len(pts) == 1:
            marks.append(("circle", pts[0][0], pts[0][1], w / 2, (255, 40, 200)))
    t.preview(OUT_PNG, marks, scale=4)
    # a shaded terrain image too
    from PIL import Image
    c = T.colors(); hh = T.h
    sh = np.clip(0.55 + (hh - hh.min()) / (hh.max() - hh.min()) * 0.6, 0, 1.2)
    img = (np.clip(c * sh[..., None], 0, 1) * 255).astype(np.uint8)
    Image.fromarray(img).resize((T.nx * 3, T.nz * 3)).save(OUT_PNG.replace(".png", "_terrain.png"))
