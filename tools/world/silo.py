#!/usr/bin/env python3
"""Generator for Maps/World/03-Silo.json, the World Tour pack's large American map (24 players): forested hills over a
Cold War missile complex. Usage:

    python3 tools/world/silo.py [repo root] [preview.png]

Mirrored east to west about x = 0 (Blue holds the west, Red the east): the terrain, the underground complex, the
objectives and the hand-placed cover are built for the west half and reflected; the theming of each half differs
(Blue's side rooms are the armoury and the crew quarters, Red's the comms room and the maintenance bay; Blue's base is
the motor pool, Red's the main gate and headquarters).

Underground (all of it terrain caves and holes, see Maps/README.md "Holes and caves", with buildings from the
Buildings folder standing in cave chambers at an absolute height):
  main level y = -4:   the Launch Control Centre (north, on the axis), the blast locks behind each team's portal, the
                       crew quarters / maintenance bay, the junctions under the vent shafts, the silo's top gantry ring;
  y = -8:              the armoury / comms room;
  y = -13:             the power plant (south, on the axis) and the silo's service-arm level;
  y = -22:             the silo floor, under the missile.
The silo shaft (a hole 23 m across, open to the sky on the hilltop, its lids slid open) has a service tower each side
(stairs between all its levels), a gantry ring at the top level, service arms to the missile at the middle one.

Built with tools/cluttered/townkit.py (occupancy, props, foliage) and terrain.py (the game's terrain generator) plus
tools/world/voids.py (the caves and holes), so the generator can check every building's interior is hollow ground,
every chamber keeps its rock roof, every tunnel ramp is walkable.
"""
import glob, json, math, os, random, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "cluttered"))
sys.path.insert(0, HERE)
import numpy as np
import townkit as tk
import terrain as tr
import voids as vd
from townkit import yaw_facing, yaw_along, R, col

_glob = glob.glob
def _props_glob(p, **k):
    """townkit reads Props/*/*.json; also take category subfolders (Props/Military/Vehicles) and skip the flag pool."""
    out = _glob(p, **k)
    if p.endswith(os.path.join("Props", "*", "*.json")):
        out += _glob(p.replace(os.path.join("*", "*.json"), os.path.join("*", "*", "*.json")))
    return [f for f in out if os.sep + "Flags" + os.sep not in f]


tk.glob.glob = _props_glob
FAILED = []


class SiloTown(tk.Town):
    def prop(self, name, x, z, yaw=0.0, y=None, check=True, gap=0.02, lanes=False, margin=0.6, tag=None):
        ok = super().prop(name, x, z, yaw, y, check, gap, lanes, margin, tag)
        if not ok:
            r = self.foot(name, x, z, round(yaw % 360, 1))
            corners = ((r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3]), (x, z))
            why = "outside" if not all(self.inside(a, b, margin) for a, b in corners) else "keep-out" if self.in_keepout(r, 0.3) else "occupied"
            FAILED.append(f"{name} ({x:.1f}, {z:.1f}): {why}")
        return ok

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/World/03-Silo.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
rng = random.Random(503)

# ================================================================ outline (mirrored control points)
HALF_CTRL = [(0, -82), (-34, -87), (-68, -80), (-98, -64), (-117, -36), (-123, -6), (-119, 26), (-102, 58), (-70, 80), (-36, 86), (0, 82)]
# round the loop: the west half from north to south, then the east half back north
CTRL = HALF_CTRL + [(-x, z) for x, z in reversed(HALF_CTRL[1:-1])]
NOISE = {"amplitude": 1.6, "scale": 22, "seed": 503, "octaves": 2}
t = SiloTown(REPO, CTRL, NOISE)
HALF_X = max(abs(p[0]) for p in t.outline)
HALF_Z = max(abs(p[1]) for p in t.outline)

# ================================================================ levels and palette
Y_M, Y_A, Y_P, Y_S = -4.0, -8.0, -13.0, -22.0      # main level, armoury level, power plant level, silo floor
SHAFT_R = 11.5
PAD_Y = 14.0                                       # the hilltop pad round the shaft
STEEL = (0.30, 0.31, 0.32); DSTEEL = (0.22, 0.23, 0.24); CONC = (0.52, 0.52, 0.49); DCONC = (0.38, 0.38, 0.36)
YEL = (0.78, 0.66, 0.13); WHITE = (0.86, 0.86, 0.84); OLIVE = (0.33, 0.36, 0.25)


def mx(p):
    """Mirror a point (x, z[, y]) about x = 0."""
    return (-p[0],) + tuple(p[1:])


# ================================================================ terrain
FEAT = [
    {"type": "noise", "amplitude": 0.8, "scale": 26, "octaves": 4, "seed": 5031},
    {"type": "noise", "amplitude": 0.25, "scale": 7, "octaves": 2, "seed": 5032},
    # the silo hill: a long ridge north to south down the middle, the complex under it
    {"type": "ridge", "points": [[0, -120], [0, -40, 14], [0, 0, 15], [0, 40, 14], [0, 120]], "width": 150, "height": 14},
]
for s in (1, -1):
    FEAT += [
        {"type": "hill", "x": s * 70, "z": -54, "radius": 30, "height": 7},
        {"type": "hill", "x": s * 74, "z": 54, "radius": 27, "height": 6},
        {"type": "hill", "x": s * 40, "z": -70, "radius": 18, "height": 4},
        {"type": "hill", "x": s * 42, "z": 70, "radius": 18, "height": 4},
    ]
# passes over the ridge north and south of the complex (the surface lanes between the bases)
FEAT += [
    {"type": "valley", "points": [[-75, -64], [-30, -66], [0, -67], [30, -66], [75, -64]], "width": 26, "depth": 5},
    {"type": "valley", "points": [[-75, 64], [-30, 66], [0, 67], [30, 66], [75, 64]], "width": 26, "depth": 5},
]
for s in (1, -1):
    FEAT.append({"type": "plateau", "x": s * 100, "z": 0, "w": 34, "d": 58, "height": 1.5, "edge": 10})
FEAT.append({"type": "flatten", "x": 0, "z": 0, "radius": 19, "height": PAD_Y, "edge": 10})

PADS = []


def pad(x, z, w, d, h, edge=4.0, material=None, mirror=True):
    for s in ((1, -1) if mirror else (1,)):
        f = {"type": "flatten", "x": R(s * x), "z": R(z), "w": R(w), "d": R(d), "height": R(h), "edge": edge}
        if material:
            f["material"] = material
        FEAT.append(f)
        PADS.append((s * x, z, w, d, h))


def road(pts, width=5.0, mirror=True):
    for s in ((1, -1) if mirror else (1,)):
        FEAT.append({"type": "path", "points": [[R(s * p[0]), R(p[1])] + ([R(p[2])] if len(p) > 2 else []) for p in pts],
                     "width": width, "edge": 3, "material": "dirt"})


SURF = {"grass": "#4d6a30", "dirt": "#6b5a3e", "rock": "#6e6c66", "sand": "#8a8060", "dirtSlope": 24, "rockSlope": 37, "variation": 0.12}
TERRAIN = {"cell": 1, "margin": 26, "surface": SURF, "features": FEAT}


def heights():
    return tr.generate({**TERRAIN, "features": [f for f in FEAT if f["type"] not in ("hole", "cave")]}, HALF_X, HALF_Z)


T0 = heights()


def g0(x, z):
    return float(T0.sample(x, z))


# ================================================================ buildings (placement records)
def load_bdef(folder):
    return json.load(open(os.path.join(REPO, "Buildings", folder, "building.json")))


def l2w(lx, lz, at, yaw, mirror=False):
    """A building's local (x, z) on the map (the game's turn: yaw 90 takes the front, +z, to the east)."""
    if mirror:
        lx = -lx
    q = int(round(yaw)) % 360
    dx, dz = {0: (lx, lz), 90: (lz, -lx), 180: (-lx, -lz), 270: (-lz, lx)}[q]
    return at[0] + dx, at[1] + dz


def w_rect(r, at, yaw, mirror, grow=0.0):
    a = l2w(r[0], r[1], at, yaw, mirror); b = l2w(r[2], r[3], at, yaw, mirror)
    return (min(a[0], b[0]) - grow, min(a[1], b[1]) - grow, max(a[0], b[0]) + grow, max(a[1], b[1]) + grow)


PLACED = []   # dicts: name, at, y, yaw, mirror, rooms [(floor, level, top, rect)], doors [(floor, level, x, z, front)], footprint


def place_building(folder, at, yaw=0, y=None, mirror=False, palette=None, seed=None, spawns=False, kind="buried", extra=None):
    d = load_bdef(folder)
    th = d.get("wallThickness", 0.25)
    storey = d.get("storeyHeight", 3.0)
    op = {"op": "Building", "name": d["name"], "at": [R(at[0]), R(at[1])] if y is None else [R(at[0]), R(y), R(at[1])]}
    if yaw:
        op["yaw"] = yaw
    if seed is not None:
        op["seed"] = seed
    if spawns:
        op["spawns"] = True
    v = {}
    if palette:
        v["palette"] = palette
    if mirror:
        v["mirror"] = True
    if extra:
        v.update(extra)
    if v:
        op["variant"] = v
    if y is not None:
        op["anchor"] = "world"
    t.ops.append(op)
    floors = []
    for f in d["floors"]:
        n = (v.get("floors") or d.get("floorCount") or len(d["floors"])) - len(d["floors"]) + 1 if f.get("repeat") else 1
        floors += [f] * max(1, n)
    base = y if y is not None else g0(*at)
    lv = [base + 0.1]
    for f in floors:
        lv.append(lv[-1] + (f.get("height") or storey))
    rooms, doors = [], []
    for k, f in enumerate(floors):
        for r in f["rooms"]:
            rects = r.get("rects") or [r["rect"]]
            for rc in rects:
                rooms.append((k, lv[k], lv[k + 1] - 0.2, w_rect(rc, at, yaw, mirror, -th / 2)))
        for o in f.get("openings", []):
            if o["type"] in ("door", "arch"):
                x, z = l2w(o["at"][0], o["at"][1], at, yaw, mirror)
                doors.append((k, lv[k], x, z, bool(o.get("front"))))
    fp = None
    for r in floors[0]["rooms"]:
        for rc in r.get("rects") or [r["rect"]]:
            w = w_rect(rc, at, yaw, mirror, th / 2)
            fp = w if fp is None else (min(fp[0], w[0]), min(fp[1], w[1]), max(fp[2], w[2]), max(fp[3], w[3]))
    rec = dict(name=d["name"], folder=folder, at=at, y=base, yaw=yaw, mirror=mirror, rooms=rooms, doors=doors, fp=fp,
               levels=lv, kind=kind, storey=storey)
    PLACED.append(rec)
    if kind == "surface":
        t.block(fp[0], 0, fp[1], fp[2], 0.05, fp[3], (0.4, 0.4, 0.4))   # (occupancy only: removed below)
        t.ops.pop(); t.boxes.pop()
        t.mark(t.occ, (fp[0] - 0.5, fp[1] - 0.5, fp[2] + 0.5, fp[3] + 0.5)); t.mark(t.walk, fp); t.mark(t.tall, fp)
    return rec


def door_near(rec, x, z):
    best = min(rec["doors"], key=lambda dd: math.hypot(dd[2] - x, dd[3] - z))
    return best


# ================================================================ voids: holes that win, then caves, then skylights
HOLES_FIRST, CAVES, HOLES_LAST = [], [], []
TUNNELS = []          # (points [(x, z, y)], width, height, dressed)


def hole(lst, **kw):
    f = {"type": "hole"}
    f.update({k: (R(v) if isinstance(v, float) else v) for k, v in kw.items()})
    lst.append(f)


def cave(points, width=5.0, height=4.0, open_=None, dressed=True, nav_only=False):
    f = {"type": "cave", "points": [[R(p[0]), R(p[1])] + ([R(p[2])] if len(p) > 2 and p[2] is not None else []) for p in points],
         "width": width, "height": height}
    f["open"] = open_ or "none"
    if len(points) == 1:
        del f["open"]
    CAVES.append(f)
    if not nav_only and len(points) > 1:
        TUNNELS.append(([(p[0], p[1], p[2] if len(p) > 2 and p[2] is not None else None) for p in points], width, height, dressed))
    return f


def chamber(fp, y, height, grow=0.6):
    """Overlapping parallel caves whose flat floors cover the rect fp (grown): a level hall for a building."""
    x0, z0, x1, z1 = fp[0] - grow, fp[1] - grow, fp[2] + grow, fp[3] + grow
    along_x = (x1 - x0) >= (z1 - z0)
    a0, a1, b0, b1 = (x0, x1, z0, z1) if along_x else (z0, z1, x0, x1)
    n = max(2, int(math.ceil((b1 - b0 - 3.0) / 3.0)) + 1)
    for k in range(n):
        b = b0 + 1.5 + (b1 - b0 - 3.0) * k / (n - 1)
        pts = [(a0 + 0.2, b, y), (a1 - 0.2, b, y)] if along_x else [(b, a0 + 0.2, y), (b, a1 - 0.2, y)]
        cave(pts, width=6.0, height=height, dressed=False, nav_only=True)
    return (x0, z0, x1, z1)


# ---------------------------------------------------------------- the silo shaft (before every cave: the pit wins)
hole(HOLES_FIRST, shape="circle", x=0.0, z=0.0, radius=SHAFT_R, floor=Y_S)

# ---------------------------------------------------------------- the buildings underground
LCC = place_building("LaunchControlCentre", (0, -46), 0, Y_M, seed=11, spawns=True)
PLANT = place_building("SiloPowerPlant", (0, 46), 180, Y_P, seed=12, spawns=True)
SIDE = {}
for s, mir in ((-1, False), (1, True)):
    side = "blue" if s < 0 else "red"
    SIDE[side] = dict(
        lock=place_building("SiloBlastLock", (s * 44, 0), 0, Y_M, mirror=mir, palette="green paint" if s > 0 else None, seed=20 + s),
        south=place_building("SiloArmoury" if s < 0 else "SiloCommsRoom", (s * 32, 26), 0, Y_A, seed=30 + s, spawns=True),
        north=place_building("SiloCrewQuarters" if s < 0 else "SiloMaintenanceBay", (s * 32, -26), 0, Y_M, seed=40 + s, spawns=True),
        tower=place_building("SiloServiceTower", (s * 8, 0), 0, Y_S, mirror=mir, seed=50 + s, kind="shaft"),
    )
for rec in [LCC, PLANT] + [SIDE[k][n] for k in SIDE for n in ("lock", "south", "north")]:
    chamber(rec["fp"], rec["y"], rec["storey"] + 1.4)

# ---------------------------------------------------------------- access bunkers (pits at the surface)
ACCESS = {"blue": [], "red": []}
for (x, z) in ((-60.5, -26.5), (-66.5, 30.5)):
    gy = round(g0(x, z) * 4) / 4
    pad(x, z, 15, 15, gy, edge=5)
    ACCESS["blue"].append((x, z, gy)); ACCESS["red"].append((-x, z, gy))
# ================================================================ terrain pads and roads for the surface
pad(-106, -14, 20, 28, 1.5, edge=3)        # motor pool shed / fire station
pad(-106, 16, 16, 16, 1.5, edge=3)         # garage / headquarters
pad(-84, -50, 24, 30, 2.5, edge=6)         # the forward bunker under the north-west hill
for (x, z) in ((-97, -42), (-97, 44)):
    pad(x, z, 8, 8, round(g0(x, z) * 4) / 4, edge=3)
pad(6.4, -25.0, 8, 8, PAD_Y, edge=4)
pad(0, -75, 8, 8, round(g0(0, -75) * 4) / 4, edge=3, mirror=False)
pad(0, 75, 8, 8, round(g0(0, 75) * 4) / 4, edge=3, mirror=False)
ROADS = [
    [(-84, 0), (-76, 0)],                                                      # base to the portal trench
    [(-86, 9), (-66, 12), (-46, 13), (-30, 9), (-18, 4)],                      # service road up to the silo pad
    [(-98, -30), (-86, -36), (-70, -66), (-40, -72), (-14, -68), (0, -68)],    # north track through the pass
    [(-98, 30), (-86, 36), (-70, 66), (-40, 72), (-14, 68), (0, 68)],          # south track
    [(-4, -18), (0, -22), (0, -30)],                                           # pad to the lift heads
]
for pts in ROADS:
    road(pts, width=4.5)
T0 = heights()      # again, with the pads


def access_bunker(x, z, gy, s):
    y = gy - 4.1
    yaw = 270 if s < 0 else 90
    rec = place_building("SiloAccessBunker", (x, z), yaw, y, mirror=(s > 0), palette="green paint" if s > 0 else None, seed=60 + int(x), kind="pit")
    fp = rec["fp"]
    hole(HOLES_FIRST, shape="polygon", points=[[R(fp[0]), R(fp[1])], [R(fp[2]), R(fp[1])], [R(fp[2]), R(fp[3])], [R(fp[0]), R(fp[3])]], floor=R(y))
    return rec


for side, s in (("blue", -1), ("red", 1)):
    ACCESS[side] = [access_bunker(x, z, gy, s) for (x, z, gy) in ACCESS[side]]


# ---------------------------------------------------------------- tunnels (west half; mirrored)
def lower_door(rec):
    """The door on the building's lowest floor nearest its middle (for access bunkers: the blast lock door)."""
    ds = [d for d in rec["doors"] if d[0] == 0]
    return ds[0]


def both(points, **kw):
    cave(points, **kw)
    cave([mx(p) for p in points], **kw)


def out_from(rec, door, dist):
    """A point dist metres out from a door, away from the building's middle."""
    fp = rec["fp"]
    cx, cz = (fp[0] + fp[2]) / 2, (fp[1] + fp[3]) / 2
    x, z = door[2], door[3]
    if abs(x - fp[0]) < 0.3 or abs(x - fp[2]) < 0.3:
        return (x + dist * (1 if x > cx else -1), z)
    return (x, z + dist * (1 if z > cz else -1))


B = SIDE["blue"]
lock_w = door_near(B["lock"], -50, 0); lock_e = door_near(B["lock"], -38, 0)
crew_s = door_near(B["north"], -28, -21); crew_e = door_near(B["north"], -24, -28); crew_w = door_near(B["north"], -40, -28)
arm_n = door_near(B["south"], -34.5, 21); arm_e = door_near(B["south"], -25, 28.5); arm_w = door_near(B["south"], -39, 26)
lcc_w = door_near(LCC, -11, -40.75); lcc_s = door_near(LCC, 0, -39)
plant_w = door_near(PLANT, -12, 40.25); plant_n = door_near(PLANT, 0, 38)
JW = (-26.0, 0.0)

# portal trench from the motor pool's side of the hill into the blast lock
PORTAL_START = (-75.0, 0.0)
both([PORTAL_START, (-58.0, 0.0, Y_M), (lock_w[2] + 0.2, 0.0, Y_M)], open_="start")
# cableway: blast lock - junction - silo shaft (into the pit: the pit wins)
both([(lock_e[2] - 0.2, 0.0, Y_M), (JW[0], 0.0, Y_M), (-6.0, 0.0, Y_M)])
cave([JW + (Y_M,)], width=11.0, height=5.0, dressed=False, nav_only=True)
cave([mx(JW + (Y_M,))], width=11.0, height=5.0, dressed=False, nav_only=True)
# junction - crew quarters / maintenance bay (south door)
both([(JW[0], -3.0, Y_M), (-27.4, -12.0, Y_M), out_from(B["north"], crew_s, -0.2) + (Y_M,)], width=4.6)
# crew quarters east door - Launch Control Centre west door
p = out_from(B["north"], crew_e, -0.2)
both([p + (Y_M,), (-18.5, crew_e[3], Y_M), (-17.0, lcc_w[3], Y_M), out_from(LCC, lcc_w, -0.2) + (Y_M,)], width=4.6)
# junction - armoury / comms (north door), down to y = -8
both([(JW[0], 3.0, Y_M), (-27.6, 8.0, Y_M), (-33.4, 16.0, -6.7), out_from(B["south"], arm_n, -0.2) + (Y_A,)], width=4.6)
# armoury east door - power plant west door, down to y = -13
both([out_from(B["south"], arm_e, -0.2) + (Y_A,), (-20.0, arm_e[3] + 1.0, Y_A), (-17.0, plant_w[3], -12.6), out_from(PLANT, plant_w, -0.2) + (Y_P,)], width=4.6)
# access bunkers
nw, sw = ACCESS["blue"]
d = lower_door(nw); a = out_from(nw, d, -0.2)
both([a + (nw["y"],), (a[0] + 6.0, a[1], nw["y"] - 0.4), (crew_w[2] - 7.0, crew_w[3], Y_M), out_from(B["north"], crew_w, -0.2) + (Y_M,)], width=4.6)
d = lower_door(sw); a = out_from(sw, d, -0.2)
both([a + (sw["y"],), (a[0] + 6.0, a[1], sw["y"] - 0.4), (arm_w[2] - 8.0, arm_w[3], Y_A), out_from(B["south"], arm_w, -0.2) + (Y_A,)], width=4.6)
# central: LCC - silo top level, the lift alcove; power plant - silo service-arm level
cave([out_from(LCC, lcc_s, -0.2) + (Y_M,), (0.0, -6.0, Y_M)])
LIFT = (6.4, -25.0)
both([(0.0, LIFT[1], Y_M), (-(LIFT[0] - 0.4), LIFT[1], Y_M)], width=5.0, dressed=False)
cave([out_from(PLANT, plant_n, -0.2) + (Y_P,), (0.0, 3.0, Y_P)])

# ---------------------------------------------------------------- nav-only lines inside the shaft (gantries, floor)
ring = [(10.0 * math.cos(a), 10.0 * math.sin(a), Y_M + 0.1) for a in np.linspace(0, 2 * math.pi, 25)]
cave(ring, width=1.0, height=0.5, nav_only=True)
cave([(-5.0, -2.4, Y_P + 0.1), (0.0, 0.0, Y_P + 0.1), (5.0, -2.4, Y_P + 0.1)], width=1.0, height=0.5, nav_only=True)
cave([(7.2 * math.cos(a), 7.2 * math.sin(a), Y_S) for a in np.linspace(0, 2 * math.pi, 19)], width=1.0, height=0.5, nav_only=True)

# ---------------------------------------------------------------- skylights: vent shafts over the junctions, the lift shaft
for s in (-1, 1):
    hole(HOLES_LAST, shape="circle", x=s * JW[0], z=0.0, radius=1.8)
for s in (-1, 1):
    hole(HOLES_LAST, shape="circle", x=s * LIFT[0], z=LIFT[1], radius=2.3)

FEAT_VOIDS = HOLES_FIRST + CAVES + HOLES_LAST
V = vd.build(FEAT_VOIDS, T0)


# ================================================================ checks on the underground (python port of the voids)
PROBLEMS = []


def check_underground():
    for rec in PLACED:
        if rec["kind"] == "surface":
            continue
        bad = []
        for (k, lv, top, r) in rec["rooms"]:
            if rec["kind"] == "pit" and k > 0:
                continue
            for x in np.arange(r[0] + 0.25, r[2] - 0.2, 0.5):
                for z in np.arange(r[1] + 0.25, r[3] - 0.2, 0.5):
                    kd = V.kind(x, z)
                    if kd == vd.SOLID:
                        bad.append(f"solid at ({x:.1f},{z:.1f})"); continue
                    fl = V.floor(x, z)
                    if fl > lv + 0.02:
                        bad.append(f"floor {fl:.2f} above {lv:.2f} at ({x:.1f},{z:.1f})")
                    if kd == vd.CAVE and V.ceiling(x, z) < top:
                        bad.append(f"ceiling {V.ceiling(x, z):.2f} under {top:.2f} at ({x:.1f},{z:.1f})")
        if bad:
            PROBLEMS.append(f"{rec['name']} at {rec['at']}: {len(bad)} bad interior samples, e.g. {bad[:3]}")
    for pts, w, h, dressed in TUNNELS:
        ys = []
        for p in pts:
            ys.append(p[2] if p[2] is not None else g0(p[0], p[1]))
        for a, b, ya, yb in zip(pts, pts[1:], ys, ys[1:]):
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            if L > 0.01 and math.degrees(math.atan2(abs(yb - ya), L)) > 29.5:
                PROBLEMS.append(f"tunnel {pts[0][:2]}..{pts[-1][:2]}: segment {a[:2]}->{b[:2]} climbs {math.degrees(math.atan2(abs(yb - ya), L)):.0f} deg")
            # the roof: the ceiling along the centre line where the tunnel is under ground
            for s in np.linspace(0, 1, max(2, int(L / 1.0))):
                x, z = a[0] + (b[0] - a[0]) * s, a[1] + (b[1] - a[1]) * s
                y = ya + (yb - ya) * s
                if math.hypot(x, z) < SHAFT_R + 0.5:
                    continue
                kd = V.kind(x, z)
                if kd == vd.SOLID:
                    PROBLEMS.append(f"tunnel {pts[0][:2]}..{pts[-1][:2]}: solid at ({x:.1f},{z:.1f})"); break
                if kd == vd.CAVE and V.ceiling(x, z) < y + 2.4:
                    PROBLEMS.append(f"tunnel {pts[0][:2]}..{pts[-1][:2]}: low roof {V.ceiling(x, z) - y:.2f} m at ({x:.1f},{z:.1f})"); break


check_underground()


# ================================================================ world-anchored boxes, meshes and props
UNDER_BOXES = []


def wblock(x0, y0, z0, x1, y1, z1, c):
    x0, x1 = min(x0, x1), max(x0, x1); z0, z1 = min(z0, z1), max(z0, z1)
    t.ops.append({"op": "Block", "x0": R(x0), "y0": R(y0), "z0": R(z0), "x1": R(x1), "y1": R(y1), "z1": R(z1), "color": col(c), "anchor": "world"})
    UNDER_BOXES.append((x0, z0, x1, z1, y0, y1, c))


MESHES = 0


def wmesh(kind, x, z, y, c, **kw):
    global MESHES
    op = {"op": "Mesh", "mesh": kind, "x": R(x), "z": R(z), "y": R(y)}
    for k, v in kw.items():
        op[k] = R(v) if isinstance(v, float) else v
    op["color"] = col(c)
    op["anchor"] = "world"
    t.ops.append(op)
    MESHES += 1


WPROPS = []


def wprop(name, x, z, y, yaw=0.0):
    if name not in t.P:
        raise KeyError(name)
    op = {"op": "Prop", "name": name, "x": R(x), "z": R(z), "y": R(y), "anchor": "world"}
    if yaw:
        op["yaw"] = R(yaw % 360)
    t.ops.append(op)
    WPROPS.append((name, x, z, y, yaw))


def both_block(x0, y0, z0, x1, y1, z1, c):
    wblock(x0, y0, z0, x1, y1, z1, c)
    wblock(-x1, y0, z0, -x0, y1, z1, c)


def both_prop(name, x, z, y, yaw=0.0):
    wprop(name, x, z, y, yaw)
    wprop(name, -x, z, y, -yaw)


# ---------------------------------------------------------------- the silo: gantry ring at the top level, arms at the middle
TOP = Y_M + 0.1        # deck tops level with the tunnel floors (and the service towers' roofs: -22 + 0.1 + 4 x 4.5)
MID = Y_P + 0.1
DECK = 0.35
GANT = (0.36, 0.37, 0.36)
wblock(-7.5, TOP - DECK, -13.0, 7.5, TOP, -8.6, GANT)          # north deck (the LCC tunnel comes in here)
wblock(-7.5, TOP - DECK, 8.6, 7.5, TOP, 12.0, GANT)            # south deck
both_block(-11.6, TOP - DECK, -8.6, -5.3, TOP, -3.6, GANT)     # north-west / north-east
both_block(-11.6, TOP - DECK, 3.6, -5.3, TOP, 8.6, GANT)       # south-west / south-east
both_block(-14.0, TOP - DECK, -3.4, -10.7, TOP, 3.4, GANT)     # bridge from the cableway onto the tower roof
RAIL = 1.05
wblock(-5.3, TOP, -8.75, 5.3, TOP + RAIL, -8.6, YEL)
wblock(-5.3, TOP, 8.6, 5.3, TOP + RAIL, 8.75, YEL)
both_block(-5.45, TOP, -8.6, -5.3, TOP + RAIL, -3.6, YEL)
both_block(-5.45, TOP, 3.6, -5.3, TOP + RAIL, 8.6, YEL)
# middle level: the power plant tunnel's landing, the arm to the missile, the missile platform, arms to the towers
wblock(-3.0, MID - DECK, 9.4, 3.0, MID, 13.2, GANT)
wblock(-1.0, MID - DECK, 2.8, 1.0, MID, 9.4, GANT)
wblock(-2.8, MID - DECK, -2.8, 2.8, MID, 2.8, GANT)
both_block(-5.3, MID - DECK, -3.3, -2.8, MID, -1.5, GANT)
# the missile: a solid core, the body and nose as meshes
MISSILE_TOP = 5.0
wblock(-1.15, Y_S, -1.15, 1.15, MISSILE_TOP - 3.0, 1.15, (0.82, 0.82, 0.8))
for y0, h, c in ((Y_S + 1.0, 9.0, WHITE), (Y_S + 10.0, 0.6, (0.12, 0.12, 0.12)), (Y_S + 10.6, 9.4, WHITE),
                 (Y_S + 20.0, 0.5, (0.12, 0.12, 0.12)), (Y_S + 20.5, 4.5, WHITE)):
    wmesh("cylinder", 0, 0, y0, c, width=3.0, depth=3.0, height=h, segments=24, material="Metal")
wmesh("cone", 0, 0, Y_S + 25.0, (0.2, 0.2, 0.22), width=3.0, depth=3.0, height=4.0, segments=24, material="Metal")
for k in range(4):
    a = math.radians(45 + 90 * k)
    wmesh("box", 1.9 * math.sin(a), 1.9 * math.cos(a), Y_S + 1.0, (0.2, 0.2, 0.22), width=0.12, height=2.6, depth=1.4, yaw=45 + 90 * k, material="Metal")
for k in range(4):
    a = math.radians(90 * k)
    wmesh("cone", 0.75 * math.sin(a), 0.75 * math.cos(a), Y_S + 1.0, (0.25, 0.25, 0.26), width=1.1, depth=1.1, height=1.0, segments=12, pitch=180, material="Metal")
wmesh("cylinder", 0, 0, Y_S, DCONC, width=8.0, depth=8.0, height=0.3, segments=32, material="Concrete")   # the launch ring
# lettering-ish bands on the body: a black roll pattern
for k in range(4):
    a = math.radians(90 * k)
    wmesh("box", 1.48 * math.sin(a), 1.48 * math.cos(a), Y_S + 14.0, (0.1, 0.1, 0.1), width=0.6, height=3.0, depth=0.06, yaw=90 * k)
# umbilical arms from the shaft wall at the middle level, cable trays down the wall
for s in (-1, 1):
    wmesh("box", s * 3.6, 6.5, MID + 1.9, (0.7, 0.6, 0.15), width=0.25, height=0.25, depth=6.0, yaw=s * 30)

# ---------------------------------------------------------------- the silo floor: cover round the missile
for (x, z, yaw, name) in ((-4.8, 5.6, 0, "Concrete Block"), (-6.0, -6.4, 90, "Propellant Tank"), (-3.2, 8.2, 20, "Wooden Crate"),
                          (-8.4, 6.6, 0, "Cable Spool"), (-4.6, -9.0, 0, "Gas Cylinders"), (-2.6, -4.2, 0, "Sandbag Wall"),
                          (-9.6, -5.4, 0, "Oil Drum")):
    both_prop(name, x, z, Y_S, yaw)
wprop("Concrete Block", 0.0, 6.2, Y_S, 90)
wprop("Concrete Block", 0.0, -6.2, Y_S, 90)
for a in range(0, 360, 45):
    r = math.radians(a + 22.5)
    wprop("Caged Bulkhead Lamp", 11.45 * math.sin(r), 11.45 * math.cos(r), Y_S + 3.0, yaw=(a + 22.5 + 180))
    wprop("Caged Bulkhead Lamp", 11.45 * math.sin(r), 11.45 * math.cos(r), Y_M + 2.6, yaw=(a + 22.5 + 180))
wprop("Radiation Sign", -2.0, 11.45, MID + 1.4, 180)
wprop("Radiation Sign", 2.0, 11.45, MID + 1.4, 180)
both_prop("Warning Beacon", -7.0, -12.4, TOP, 0)


# ---------------------------------------------------------------- tunnel dressing: steel arch sets, lights, a walkway
RIB = (0.27, 0.27, 0.26)


def floor_y(pts, k, s):
    a, b = pts[k], pts[k + 1]
    ya = a[2] if a[2] is not None else g0(a[0], a[1])
    yb = b[2] if b[2] is not None else g0(b[0], b[1])
    return ya + (yb - ya) * s


def dress_tunnels():
    n_ribs = 0
    for pts, w, h, dressed in TUNNELS:
        if not dressed:
            continue
        span = w * 0.74
        post_h = h * 0.75
        run = 0.0
        for k in range(len(pts) - 1):
            a, b = pts[k], pts[k + 1]
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            if L < 0.5:
                continue
            dx, dz = (b[0] - a[0]) / L, (b[1] - a[1]) / L
            nxp, nzp = -dz, dx
            yaw = math.degrees(math.atan2(dx, dz))
            ya, yb = floor_y(pts, k, 0), floor_y(pts, k, 1)
            pitch = -math.degrees(math.atan2(yb - ya, L))
            st = 2.0 - run % 4.0 if run > 0 else 2.0
            d = st
            while d < L - 0.5:
                x, z = a[0] + dx * d, a[1] + dz * d
                if math.hypot(x, z) < SHAFT_R + 1.0 or V.kind(x, z) != vd.CAVE:
                    d += 4.0
                    continue
                y = ya + (yb - ya) * d / L
                for sd in (-1, 1):
                    wmesh("box", x + nxp * sd * span / 2, z + nzp * sd * span / 2, y - 0.3, RIB, width=0.22, height=post_h + 0.3, depth=0.22, yaw=yaw, material="Metal")
                wmesh("box", x, z, y + post_h - 0.05, RIB, width=span + 0.2, height=0.24, depth=0.22, yaw=yaw, material="Metal")
                n_ribs += 1
                if n_ribs % 2 == 0:
                    wprop("Tube Light", x, z, y + post_h - 0.53, yaw + 90)
                d += 4.0
            run += L
            # walkway: a concrete strip down the middle of each straight stretch (cosmetic, so bots' floor nodes stay)
            if L > 2.0:
                cx, cz = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
                if V.kind(cx, cz) == vd.CAVE and math.hypot(cx, cz) > SHAFT_R + 2:
                    wmesh("box", cx, cz, (ya + yb) / 2 + 0.005, (0.42, 0.42, 0.4), width=w * 0.5, height=0.03, depth=math.hypot(L, yb - ya),
                          yaw=yaw, pitch=pitch, material="Concrete")
    return n_ribs


N_RIBS = dress_tunnels()

# ---------------------------------------------------------------- the portals: where each trench goes under the hill
def portal_x():
    """First roofed cell along the portal trench (west half)."""
    x = PORTAL_START[0]
    while x < -50 and V.kind(x, 0.0) != vd.CAVE:
        x += 0.25
    return x


PX = portal_x() + 0.6
PY = max(V.floor(PX, 0.0), Y_M)
for s in (-1, 1):
    wprop("Blast Door Portal", s * PX, 0.0, PY, 90)
    wprop("Blast Door Leaf", s * (PX + 0.9), -1.55, PY, 180 if s < 0 else 0)
    wprop("Restricted Area Sign", s * (PX - 6.0), 3.6, g0(s * (PX - 6.0), 3.6), 90 * -s)
    wprop("Caged Bulkhead Lamp", s * (PX + 4.0), 1.9, PY + 2.4, 270 if s < 0 else 90)

# ---------------------------------------------------------------- rooms in the rock: the junctions and the lift lobby
for s in (-1, 1):
    x = s * JW[0]
    for (dx, dz, yaw, name) in ((-3.6, -3.2, 45, "Ventilation Fan Unit"), (3.4, 3.4, 225, "Ventilation Fan Unit"), (2.6, -3.8, 0, "Pad Transformer"),
                                (-3.0, 3.6, 0, "Cable Spool"), (3.8, -1.0, 90, "Junction Box"), (-4.2, 0.9, 0, "Oil Drum")):
        wprop(name, x + s * dx, dz, Y_M, yaw if s < 0 else -yaw)
    for (dx, dz) in ((0.0, -4.4), (0.0, 4.4)):
        wprop("Caged Bulkhead Lamp", x + dx, dz, Y_M + 2.4, 0 if dz < 0 else 180)
both_prop("Restricted Area Sign", LIFT[0] - 0.5, LIFT[1] - 2.3, Y_M, 0)
for (x, z, yaw, name) in ((-1.9, -18.0, 90, "Patch Panel Cabinet"), (1.9, -31.5, 270, "Junction Box"), (-1.9, -32.0, 90, "Gas Cylinders"),
                          (1.9, 18.0, 270, "Junction Box"), (-1.9, 26.0, 90, "Cable Spool"), (1.9, 30.5, 270, "Gas Cylinders")):
    wprop(name, x, z, Y_M if z < 0 else Y_P, yaw)

# ---------------------------------------------------------------- the lift: an elevator from the lobby up the shaft to the hilltop
LIFT_TOP = PAD_Y
MOVERS = [{"type": "elevator", "name": f"Silo Lift {side}", "at": [R(s * LIFT[0]), R(Y_M + 0.3), R(LIFT[1])], "rise": R(LIFT_TOP - Y_M),
           "speed": 3.0, "dwell": 3.0, "color": [0.6, 0.55, 0.2],
           "boxes": [{"size": [3.0, 0.3, 3.0]}, {"size": [3.0, 1.0, 0.12], "offset": [0, 0.5, -1.44], "color": [0.85, 0.7, 0.15]},
                     {"size": [3.0, 1.0, 0.12], "offset": [0, 0.5, 1.44], "color": [0.85, 0.7, 0.15]}], "phase": 0.0}
          for side, s in (("west", -1), ("east", 1))]


# ================================================================ preview
def preview(path, scale=4):
    from PIL import Image, ImageDraw
    x0, z0 = -HALF_X - 6, -HALF_Z - 6
    W, H = int((2 * HALF_X + 12) * scale), int((2 * HALF_Z + 12) * scale) * 2 + 20
    img = Image.new("RGB", (W, H), (30, 30, 30)); d = ImageDraw.Draw(img)
    hh = int((2 * HALF_Z + 12) * scale)

    def P(x, z, under=False):
        return ((x - x0) * scale, (z - z0) * scale + (hh + 20 if under else 0))
    # terrain shading (top) and voids (bottom)
    hs = T0.h
    for j in range(0, hs.shape[0] - 1):
        for i in range(0, hs.shape[1] - 1):
            x = float(T0.ox) + i; z = float(T0.oz) + j
            if x < x0 or z < z0 or x > -x0 or z > -z0:
                continue
            v = hs[j, i]
            g = int(60 + min(max(v, -2), 16) * 9)
            inside = t.inside(x + 0.5, z + 0.5)
            col_ = (g // 2, g, g // 2) if inside else (g // 3, g // 3, g // 3)
            k = V.kinds[j, i]
            if k == vd.OPEN:
                col_ = (40, 40, 160)
            d.rectangle([P(x, z), P(x + 1, z + 1)], fill=col_)
            if k != vd.SOLID:
                fl = V.F[j, i]
                c2 = int(80 + (fl + 22) * 6)
                d.rectangle([P(x, z, True), P(x + 1, z + 1, True)], fill=(c2, c2 // 2, 40) if k == vd.CAVE else (60, 60, 200))
    d.polygon([P(*p) for p in t.outline[::4]], outline=(255, 255, 255))
    for rec in PLACED:
        fp = rec["fp"]
        under = rec["kind"] != "surface"
        d.rectangle([P(fp[0], fp[1], under), P(fp[2], fp[3], under)], outline=(255, 255, 0) if under else (255, 120, 0), width=2)
        for dd in rec["doors"]:
            d.ellipse([P(dd[2] - 0.5, dd[3] - 0.5, under), P(dd[2] + 0.5, dd[3] + 0.5, under)], fill=(255, 0, 0))
    for x0b, z0b, x1b, z1b, y0, y1, c in UNDER_BOXES:
        d.rectangle([P(x0b, z0b, True), P(x1b, z1b, True)], outline=(200, 200, 200))
    for x0b, z0b, x1b, z1b, y0, y1, c in t.boxes:
        d.rectangle([P(x0b, z0b), P(x1b, z1b)], fill=tuple(int(v * 255) for v in c))
    for name, x, z, yaw, y, tag in t.props:
        r = t.foot(name, x, z, yaw)
        d.rectangle([P(r[0], r[1]), P(r[2], r[3])], outline=(220, 140, 60) if t.P[name]["solid"] else (230, 230, 120))
    for it in t.items:
        d.ellipse([P(it["x"] - 1, it["z"] - 1), P(it["x"] + 1, it["z"] + 1)], outline=(30, 110, 30))
    for kind, a, b, r, c in MARKS:
        if kind == "circle":
            d.ellipse([P(a - r, b - r, r < 0), P(a + r, b + r)], outline=c, width=3)
        elif kind == "dot":
            d.ellipse([P(a - r, b - r), P(a + r, b + r)], fill=c)
    for gx in range(-120, 121, 20):
        d.text(P(gx, z0 + 1), str(gx), fill=(255, 255, 255)); d.text(P(gx, z0 + 1, True), str(gx), fill=(255, 255, 255))
    for gz in range(-80, 81, 20):
        d.text(P(x0 + 1, gz), str(gz), fill=(255, 255, 255)); d.text(P(x0 + 1, gz, True), str(gz), fill=(255, 255, 255))
    img.save(path)


MARKS = []


# ================================================================ objectives
BLUE_SP = [(-90.0, z) for z in (-24, -16, -8, 8, 16, 24)] + [(-86.0, z) for z in (-20, -12, -4, 4, 12, 20)]
RED_SP = [mx(p) for p in BLUE_SP]
FFA_SP = [(-46, -62), (46, -62), (-44, 60), (44, 60), (-30, -44), (30, -44), (-28, 44), (28, 44)]
FLAGS = [(-104.0, 2.0), (104.0, 2.0)]
HILLS = [dict(x=0.0, z=-44.6, y=Y_M + 0.1, radius=5.0, name="launch control"),
         dict(x=0.0, z=4.6, y=Y_S, radius=6.5, name="silo floor"),
         dict(x=0.0, z=44.8, y=Y_P + 0.1, radius=4.5, name="reactor floor")]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.keepout.append((x, z, 2.6))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))

for pts in ROADS:
    for s in (1, -1):
        t.add_lane([(s * x, z) for x, z in pts], 4.5)
        t.path([(s * x, z) for x, z in pts], 4.0, density=0.08)

# ================================================================ surface buildings
SURF = []
for s in (-1, 1):
    blue = s < 0
    yaw = 90 if blue else 270                      # fronts face the middle of the map
    if blue:
        SURF.append(place_building("Warehouse", (-106, -14), yaw, None, palette="green metal", seed=71, kind="surface", spawns=False))
        SURF.append(place_building("Garage", (-106, 16), yaw, None, palette="white block", seed=72, kind="surface"))
    else:
        SURF.append(place_building("FireStation", (106, -14), yaw, None, seed=73, kind="surface"))
        SURF.append(place_building("OfficeBuilding", (106, 16), yaw, None, palette="beige brick", seed=74, kind="surface"))
    bk = place_building("Bunker", (s * 84, -50), 270 if blue else 90, None, mirror=not blue, palette=None if blue else "olive paint", seed=75, kind="surface")
    t.mark(t.occ, (s * 84 - 11, -64, s * 84 + 11, -36)); t.mark(t.walk, (s * 84 - 11, -64, s * 84 + 11, -36))
    for (x, z) in ((97, -42), (97, 44)):
        place_building("ConcreteGuardTower", (s * x, z), 90 if blue else 270, None, palette="olive" if not blue else None, seed=76, kind="surface")
place_building("ConcreteGuardTower", (0, -75), 180, None, seed=77, kind="surface")
place_building("ConcreteGuardTower", (0, 75), 0, None, seed=78, kind="surface")
for rec in ACCESS["blue"] + ACCESS["red"]:
    fp = rec["fp"]
    t.mark(t.occ, (fp[0] - 1, fp[1] - 1, fp[2] + 1, fp[3] + 1)); t.mark(t.walk, fp); t.mark(t.tall, fp)
# keep the surface clear over every hole and trench (the shaft, vent shafts, lift shafts, trenches)
for j in range(V.kinds.shape[0]):
    for i in range(V.kinds.shape[1]):
        if V.kinds[j, i] == vd.OPEN:
            x = float(T0.ox) + i; z = float(T0.oz) + j
            t.mark(t.occ, (x - 0.6, z - 0.6, x + 1.6, z + 1.6))


# ================================================================ surface: the silo pad on the hilltop
for s in (-1, 1):
    # the shaft's two sliding lids, run back on their rails (solid slabs: cover on the hilltop)
    wblock(s * 13.2, PAD_Y - 0.2, -12.5, s * 25.4, PAD_Y + 1.25, 12.5, (0.46, 0.46, 0.43))
    for rz in (-9.0, 9.0):
        wmesh("box", s * 21.0, rz, PAD_Y, (0.25, 0.24, 0.22), width=20.0, height=0.12, depth=0.35, material="Metal")
    wmesh("box", s * 19.3, 0.0, PAD_Y + 1.25, YEL, width=12.0, height=0.03, depth=0.5)
    t.mark(t.occ, (min(s * 13.2, s * 25.4), -12.5, max(s * 13.2, s * 25.4), 12.5)); t.mark(t.walk, (min(s * 13.2, s * 25.4), -12.5, max(s * 13.2, s * 25.4), 12.5))
# a broken safety fence round the shaft (gaps to jump through), hazard posts, floodlights, flags
for a in range(0, 360, 15):
    if a % 90 in (0, 15, 75) or (a // 15) % 4 == 3:
        continue
    r = math.radians(a)
    x, z = 13.4 * math.cos(r), 13.4 * math.sin(r)
    if abs(x) > 12.8 and abs(z) < 13:
        continue
    t.prop("Construction Fence Panel", x, z, yaw_along(-math.sin(r), math.cos(r)), check=False)
for s in (-1, 1):
    t.prop("Floodlight Tower", s * 17.0, -16.0, 225 if s < 0 else 135)
    t.prop("Floodlight Tower", s * 17.0, 16.0, 315 if s < 0 else 45)
    t.prop("Flag on a Pole", s * 15.5, -17.5, 0, check=False)
t.prop("Restricted Area Sign", 0.0, -17.0, 180)
t.prop("Restricted Area Sign", 0.0, 17.0, 0)

# the passes over the ridge: an old wreck in each, the silo pad's searchlights
t.prop("Wrecked Tank", 0.0, -63.0, 90)
t.prop("Wrecked Tank", 0.0, 63.0, 270)
for s in (-1, 1):
    t.prop("Field Searchlight", s * 20.0, 15.5, 0)

# lift heads: a hazard railing round each shaft, a control box, a light
for s in (-1, 1):
    lx, lz = s * LIFT[0], LIFT[1]
    for (dx, dz, yaw) in ((0, -2.9, 0), (2.9 * s, 0, 90)):
        t.prop("Road Barrier", lx + dx, lz + dz, yaw, check=False)
    t.prop("Junction Box", lx - 2.8 * s, lz - 2.6, 90 * -s, check=False)
    t.prop("Warning Beacon", lx + 2.6 * s, lz + 2.6, 0, check=False)
# vent shafts over the junctions: fan housings round a grille-less shaft
for s in (-1, 1):
    vx = s * JW[0]
    t.prop("Ventilation Fan Unit", vx + 4.6 * s, -1.2, 270 if s < 0 else 90)
    t.prop("Mushroom Vent", vx - 3.8 * s, 3.4, 0)
    t.prop("Road Barrier", vx, -3.0, 0, check=False)
    t.prop("Road Barrier", vx, 3.0, 0, check=False)
# antennas over the Launch Control Centre and the power plant
t.prop("Sweeping Satellite Dish", -5.0, -50.0, 180)
t.prop("Radio Mast", 5.0, -52.0, 0)
t.prop("Radar Dish", 0.0, -56.0, 0)
t.prop("Mushroom Vent", -3.0, -42.0, 0); t.prop("Mushroom Vent", 3.0, -42.0, 0)
t.prop("Mushroom Vent", -4.0, 44.0, 0); t.prop("Mushroom Vent", 4.0, 44.0, 0)
t.prop("Exhaust Fan", 0.0, 50.0, 0)
for s in (-1, 1):
    t.prop("Diesel Generator", s * 8.0, -55.0, 0)
    t.prop("Pad Transformer", s * 9.0, 52.0, 0)

# ================================================================ surface: the bases
# Blue: the motor pool (vehicle shed, workshop, trucks, the fuel point); Red: the main gate and headquarters
# (fire station, HQ office, the gatehouse, staff cars). Same footprints, mirrored spawns and cover.
def base_props(s):
    blue = s < 0
    X = lambda x: s * abs(x)
    face = 90 if blue else 270
    for z in (-9.0, 11.0):
        t.prop("Flag on a Pole", X(100.5), z, 0, check=False)
    if blue:
        trucks = [("Covered Cargo Truck", -95.0, -24.0, 90), ("Covered Cargo Truck", -95.0, -15.6, 90), ("Light Utility Jeep", -95.0, -7.5, 90),
                  ("Fuel Tanker Truck", -95.0, 23.5, 90), ("Forklift", -99.5, 3.5, 90), ("Light Utility Jeep", -95.0, 12.5, 90)]
        for name, x, z, yaw in trucks:
            t.prop(name, x, z, yaw)
        t.prop("Fuel Drum Rack", -100.0, 5.6, 0)
        t.prop("Jerrycan Rack", -100.2, 7.2, 0)
        t.prop("Military Pallet", -98.0, 7.4, 0); t.prop("Pallet of Ammo Crates", -112.0, 0.5, 0)
        t.prop("Propellant Tank", -110.0, 4.5, 0)
        t.prop("Diesel Generator", -112.0, 26.0, 90)
        t.prop("Tyre Stack", -100.0, 10.5, 0); t.prop("Tyre Stack", -99.0, 10.5, 0)
        t.prop("Pallet Stack", -99.0, -2.5, 0); t.prop("Jerry Can", -97.5, 4.8, 30)
        t.prop("Red Fuel Barrel", -101.5, -3.5, 0)
    else:
        cars = [("Sedan", 95.0, -24.5, 90), ("SUV", 95.0, -17.5, 90), ("Armoured Car", 95.0, -9.5, 90), ("Covered Cargo Truck", 95.0, 23.5, 90),
                ("Ambulance", 99.5, 3.5, 90), ("Light Utility Jeep", 95.0, 12.5, 90)]
        for name, x, z, yaw in cars:
            t.prop(name, x, z, yaw)
        t.prop("Guard Booth", 81.5, 3.6, 0)
        t.prop("Boom Gate", 81.0, -1.6, 270)
        t.prop("Concrete Tank Trap", 79.0, -5.0, 0); t.prop("Concrete Tank Trap", 79.0, 6.0, 0)
        t.prop("Concrete Block", 100.5, 6.0, 0); t.prop("Field Searchlight", 100.5, 8.2, 270)
        t.prop("Bench", 101.0, 10.5, 0); t.prop("Trash Can", 99.5, 10.5, 0)
        t.prop("Mailbox", 99.0, -1.0, 0); t.prop("Vending Machine", 110.0, 4.5, 180)
        t.prop("Diesel Generator", 112.0, 26.0, 270)
        t.prop("Red Fuel Barrel", 101.5, -3.5, 0)
    for (x, z) in ((-99.5, -29.5), (-99.5, 29.5)):
        t.prop("Floodlight Tower", X(x), z, face)
    for z in (-30.0, -14.0, 14.0, 30.0):
        t.prop("Street Lamp", X(83.5), z, face)
    t.prop("Restricted Area Sign", X(82.5), 7.0, face)


for s in (-1, 1):
    base_props(s)

# ================================================================ surface: sandbag positions scattered through the woods
FORTS = [(-66, -12, 90), (-46, -44, 90), (-44, 48, 90), (-26, -60, 90), (-24, 60, 90), (-38, -14, 70), (-38, 21, 110),
         (-14, -32, 45), (-14, 32, 135), (-56, -57, 60), (-56, 57, 120), (-60, 45, 90)]


def fort(x, z, facing, s, kind):
    """A small sandbag position facing `facing` (degrees, 90 = east), with its kit behind it. Three kinds: a machine-gun
    nest (a ring of sandbags), a U of long walls with corners and an ammunition stack, a low wall under a camo net."""
    if s > 0:
        x, facing = -x, 180 - facing
    f = math.radians(facing)
    fx, fz = math.cos(f), math.sin(f)
    put = 0
    along = yaw_along(-fz, fx)            # a wall's long axis across the facing direction
    P = t.prop

    def put_(name, px, pz, yaw):
        return P(name, px, pz, yaw, check=False)
    if kind == 0:
        if put_("Sandbag Nest Ring", x, z, along):
            put += 1
            put_("Mounted Machine Gun", x + fx * 0.6, z + fz * 0.6, yaw_facing(fx, fz) - 90)
            put_("Steel Ammo Can", x - fx * 0.5, z - fz * 0.5, rng.uniform(0, 360))
        put_("Ammo Crate Wooden", x - fx * 2.4, z - fz * 2.4, along)
        put_("Sandbag Pile", x - fx * 2.0 + fz * 2.0, z - fz * 2.0 - fx * 2.0, along)
    elif kind == 1:
        for off in (-1, 0, 1):
            if put_("Sandbag Wall Long", x + fx * 1.6 + fz * off * 3.0, z + fz * 1.6 - fx * off * 3.0, along):
                put += 1
        for sd in (-1, 1):
            if put_("Sandbag Corner", x + fx * 0.9 + fz * sd * 5.5, z + fz * 0.9 - fx * sd * 5.5, along + (0 if sd < 0 else 90)):
                put += 1
        put_("Stacked Ammo Crates", x - fx * 1.2 + fz * 2.2, z - fz * 1.2 - fx * 2.2, along)
        put_("Field Radio Pack", x - fx * 0.6 - fz * 1.0, z - fz * 0.6 + fx * 1.0, yaw_facing(fx, fz))
        put_("Military Fuel Drum", x - fx * 1.6 - fz * 2.6, z - fz * 1.6 + fx * 2.6, 0)
    else:
        for off in (-0.8, 0.8):
            if put_("Sandbag Wall Short", x + fx * 1.4 + fz * off, z + fz * 1.4 - fx * off, along):
                put += 1
        put_("Camo Net Canopy", x - fx * 1.4, z - fz * 1.4, along)
        put_("Pallet of Ammo Crates", x - fx * 2.6 + fz * 1.8, z - fz * 2.6 - fx * 1.8, along)
        put_("Jerry Can", x - fx * 0.6, z - fz * 0.6, rng.uniform(0, 360))
    t.mark(t.occ, (x - 4.5, z - 4.5, x + 4.5, z + 4.5))
    return put


def fort_site(x, z):
    site = (x - 5.8, z - 5.8, x + 5.8, z + 5.8)
    return t.free((x - 3.5, z - 3.5, x + 3.5, z + 3.5)) and not t.in_keepout(site, 0.0) and not t.on_lane((x - 4, z - 4, x + 4, z + 4)) \
        and all(V.kind(x + dx, z + dz) != vd.OPEN for dx in (-4, 0, 4) for dz in (-4, 0, 4))


FORT_COUNT = 0
for i, (x, z, f) in enumerate(FORTS):
    if fort_site(x, z) and fort_site(-x, z):
        FORT_COUNT += fort(x, z, f, -1, i % 3) + fort(x, z, f, 1, i % 3)
    else:
        FAILED.append(f"fort at (+-{abs(x)}, {z}): site not clear")

# ================================================================ surface: the perimeter fence (just inside the outline)
FENCE = 0
ol = t.outline
acc = 0.0
pts = []
for i in range(len(ol)):
    a, b = ol[i], ol[(i + 1) % len(ol)]
    acc += math.hypot(b[0] - a[0], b[1] - a[1])
    if acc >= 3.1:
        pts.append(a)
        acc = 0.0
for i in range(len(pts)):
    a, b = pts[i], pts[(i + 1) % len(pts)]
    mxp, mzp = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    if L < 1:
        continue
    nx_, nz_ = -(b[1] - a[1]) / L, (b[0] - a[0]) / L
    # inward normal
    if nx_ * (0 - mxp) + nz_ * (0 - mzp) < 0:
        nx_, nz_ = -nx_, -nz_
    if (i * 7) % 23 == 0:
        continue      # a gap where the fence has come down
    if t.prop("Chain Link Fence", mxp + nx_ * 4.0, mzp + nz_ * 4.0, yaw_along(b[0] - a[0], b[1] - a[1]), margin=1.5, check=False):
        FENCE += 1
        if i % 2 == 0:
            t.prop("Barbed Wire Coil", mxp + nx_ * 2.6, mzp + nz_ * 2.6, yaw_along(b[0] - a[0], b[1] - a[1]), margin=1.2)
        if i % 17 == 5:
            t.prop("Mine Warning Sign", mxp + nx_ * 4.6, mzp + nz_ * 4.6, yaw_facing(nx_, nz_), margin=2.0)


# ================================================================ surface: the forest floor
def free_spot(x, z, r):
    return t.free((x - r, z - r, x + r, z + r), 0.3) and not t.in_keepout((x - r, z - r, x + r, z + r), 1.0) \
        and not t.on_lane((x - r, z - r, x + r, z + r)) and t.inside(x, z, 3.0) and V.kind(x, z) == vd.SOLID and V.kind(x + r, z + r) == vd.SOLID \
        and not (abs(x) > 80 and abs(z) < 32) and math.hypot(x, z) > 24


BOULDERS = 0
r2 = random.Random(5030)
tries = 0
while BOULDERS < 44 and tries < 6000:
    tries += 1
    xb = -r2.uniform(14, 110); z = r2.uniform(-78, 78)
    big = BOULDERS < 18
    size = r2.uniform(2.2, 3.0) if big else r2.uniform(0.9, 1.6)
    yaw0 = r2.uniform(0, 360); extra = r2.random() < 0.5
    if not (free_spot(xb, z, size * 0.6) and free_spot(-xb, z, size * 0.6)):
        continue
    for s in (-1, 1):       # mirrored pairs
        x = s * abs(xb); yaw = yaw0 * (1 if s < 0 else -1)
        if big:
            t.prop("Granite Outcrop" if (BOULDERS // 2) % 3 == 0 else "Granite Boulder", x, z, yaw, check=False)
        else:
            t.mesh("icosphere", x, z, (0.47, 0.46, 0.43), width=size, height=size * 0.72, depth=size * 0.85, detail=1, yaw=yaw, y=-0.25, material="Concrete")
            if extra:
                t.mesh("icosphere", x + s * -size * 0.5, z + size * 0.3, (0.43, 0.43, 0.4), width=size * 0.55, height=size * 0.4, depth=size * 0.5, detail=1, yaw=yaw + 40, y=-0.1, material="Concrete")
            t.mark(t.occ, (x - size * 0.4, z - size * 0.4, x + size * 0.4, z + size * 0.4))
        BOULDERS += 1
t.clutter(["Fallen Spruce Log", "Log", "Tree Stump", "Tree Stump", "Spruce Log Pile"], -112, -80, 112, 80, 80, 5033)
t.clutter(["Dead Pine Branch", "Dead Pine Branch", "Autumn Bracken", "Autumn Bracken", "Mossy Stones", "Fly Agaric Toadstools", "Birch Leaf Litter"],
          -114, -82, 114, 82, 700, 5034)

# ================================================================ scenery: the forest carrying on over the hills past the edge
SCENERY = 0
ol = t.outline
cxs = [p[0] for p in ol]; czs = [p[1] for p in ol]
for off, step, seed in ((3.5, 5.0, 1), (9.0, 6.0, 2), (15.0, 6.5, 3), (21.0, 7.5, 4)):
    rr = random.Random(5040 + seed)
    acc = 0.0
    for i in range(len(ol)):
        a, b = ol[i], ol[(i + 1) % len(ol)]
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        acc += L
        if acc < step or L < 1e-6:
            continue
        acc = 0.0
        nx_, nz_ = (b[1] - a[1]) / L, -(b[0] - a[0]) / L
        if nx_ * a[0] + nz_ * a[1] < 0:
            nx_, nz_ = -nx_, -nz_
        o = off + rr.uniform(-1.5, 1.5)
        x, z = a[0] + nx_ * o + rr.uniform(-1, 1), a[1] + nz_ * o + rr.uniform(-1, 1)
        h = rr.uniform(8, 13)
        green = (0.16 + rr.uniform(-0.02, 0.02), 0.27 + rr.uniform(-0.03, 0.03), 0.14)
        if rr.random() < 0.75:   # a pine
            t.mesh("cylinder", x, z, (0.30, 0.22, 0.15), width=0.45, depth=0.45, height=h * 0.4, segments=6)
            t.mesh("cone", x, z, green, width=h * 0.42, depth=h * 0.42, height=h * 0.55, y=h * 0.22, segments=8)
            t.mesh("cone", x, z, green, width=h * 0.3, depth=h * 0.3, height=h * 0.45, y=h * 0.52, segments=8)
        else:                    # a broadleaf
            t.mesh("cylinder", x, z, (0.32, 0.24, 0.16), width=0.5, depth=0.5, height=h * 0.5, segments=6)
            t.mesh("icosphere", x, z, (green[0] + 0.04, green[1] + 0.06, green[2]), width=h * 0.55, height=h * 0.5, depth=h * 0.55, y=h * 0.38, detail=1)
        SCENERY += 1

# ================================================================ foliage
REGIONS = [
    {"type": "grass", "density": 1.7, "rect": [-125, -88, 125, 88], "patchiness": 0.45, "seed": 1},
    {"type": "shrub", "density": 0.42, "rect": [-125, -88, 125, 88], "seed": 2, "color": [0.24, 0.34, 0.16]},
    {"type": "bush", "density": 0.11, "rect": [-125, -88, 125, 88], "seed": 3, "scale": [0.9, 1.5]},
    {"type": "pine", "density": 0.0062, "rect": [-125, -88, 125, 88], "seed": 4, "height": [8, 13]},
    {"type": "tree", "density": 0.0036, "rect": [-125, -88, 125, 88], "seed": 5, "height": [6.5, 10], "color": [0.27, 0.40, 0.17]},
]
for s in (-1, 1):   # denser woods on the side hills and down the ridge's flanks
    REGIONS += [
        {"type": "pine", "density": 0.006, "polygon": [[s * 40, -86], [s * 100, -76], [s * 112, -44], [s * 78, -34], [s * 44, -44]], "seed": 6 + (s > 0)},
        {"type": "pine", "density": 0.006, "polygon": [[s * 40, 86], [s * 100, 76], [s * 112, 44], [s * 78, 34], [s * 44, 44]], "seed": 8 + (s > 0)},
        {"type": "bush", "density": 0.12, "polygon": [[s * 24, -40], [s * 60, -40], [s * 60, 40], [s * 24, 40]], "seed": 10 + (s > 0)},
    ]
EXCLUDE = [
    {"shape": "rect", "center": [-100, 0], "halfX": 18, "halfZ": 30, "density": 0.12},
    {"shape": "rect", "center": [100, 0], "halfX": 18, "halfZ": 30, "density": 0.12},
    {"shape": "circle", "center": [0, 0], "radius": 24, "density": 0.18},
    {"shape": "rect", "center": [-84, -50], "halfX": 11, "halfZ": 14, "density": 0.3},
    {"shape": "rect", "center": [84, -50], "halfX": 11, "halfZ": 14, "density": 0.3},
    {"shape": "rect", "center": [0, -52], "halfX": 11, "halfZ": 6, "density": 0.3},
]

# ================================================================ spawns, waypoints
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": R(x), "z": R(z)})
for x, z in BLUE_SP + RED_SP + FFA_SP:
    MARKS.append(("dot", x, z, 1.0, (60, 120, 255) if x < -50 else (255, 60, 60) if x > 50 else (230, 230, 230)))
t.ops.append({"op": "AutoWaypoints", "spacing": 8})


# ================================================================ decals: the map's own (Maps/World/Decals/Silo) and the global set
PACK_DECALS = {
    "SiloFloorTrefoil": {"name": "Silo Floor Trefoil", "description": "A big yellow radiation trefoil painted on a concrete floor.",
                         "size": 3.0, "rotation": 0, "tintVariation": 0.08, "surfaces": "floor", "fadeDistance": 90,
                         "layers": [{"shape": "circle", "radius": 0.47, "color": [0.86, 0.72, 0.12], "noise": 0.25, "noiseScale": 10},
                                    {"shape": "circle", "radius": 0.08, "color": [0.08, 0.08, 0.08]},
                                    {"shape": "rect", "at": [0.5, 0.71], "size": [0.2, 0.22], "color": [0.08, 0.08, 0.08]},
                                    {"shape": "rect", "at": [0.318, 0.395], "size": [0.2, 0.22], "rotation": 120, "color": [0.08, 0.08, 0.08]},
                                    {"shape": "rect", "at": [0.682, 0.395], "size": [0.2, 0.22], "rotation": 240, "color": [0.08, 0.08, 0.08]},
                                    {"shape": "ring", "radius": 0.44, "width": 0.025, "color": [0.08, 0.08, 0.08]}]},
    "SiloNoLoneZone": {"name": "Silo No Lone Zone", "description": "The red two-person zone painted round launch equipment: a red border, red hatching.",
                       "size": [4.0, 3.0], "rotation": 0, "surfaces": "floor", "fadeDistance": 80,
                       "layers": [{"shape": "rect", "size": [0.96, 0.94], "color": [0.7, 0.1, 0.08], "noise": 0.2, "noiseScale": 12},
                                  {"shape": "rect", "size": [0.86, 0.8], "color": "white", "erase": True},
                                  {"shape": "stripes", "size": [0.86, 0.8], "rotation": 45, "period": 0.16, "duty": 0.25, "color": [0.7, 0.1, 0.08], "alpha": 0.8}]},
    "SiloSeepage": {"name": "Silo Seepage", "description": "Lime and rust streaks where water seeps down a concrete or rock wall.",
                    "size": [2.0, 2.4], "rotation": 0, "sizeVariation": 0.3, "blend": "multiply", "surfaces": "wall",
                    "layers": [{"shape": "line", "at": [0.3, 0.92], "to": [0.32, 0.1], "width": 0.05, "color": [0.62, 0.5, 0.36], "softness": 0.05, "noise": 0.4},
                               {"shape": "line", "at": [0.52, 0.95], "to": [0.5, 0.25], "width": 0.08, "color": [0.55, 0.45, 0.32], "softness": 0.06, "noise": 0.4},
                               {"shape": "line", "at": [0.7, 0.9], "to": [0.72, 0.35], "width": 0.04, "color": [0.66, 0.56, 0.42], "softness": 0.05, "noise": 0.5},
                               {"shape": "splat", "at": [0.5, 0.9], "radius": 0.16, "count": 5, "jag": 0.4, "seed": 7, "color": [0.6, 0.52, 0.4], "alpha": 0.7, "softness": 0.08}]},
    "SiloBlastDoorStencil": {"name": "Silo Blast Door Stencil", "description": "A yellow-and-black KEEP CLEAR panel stencilled beside a blast door.",
                             "size": [1.0, 1.0], "rotation": 0, "surfaces": "wall", "fadeDistance": 60,
                             "layers": [{"shape": "rect", "size": [0.9, 0.9], "color": [0.86, 0.72, 0.12], "noise": 0.2, "noiseScale": 9},
                                        {"shape": "stripes", "at": [0.5, 0.2], "size": [0.9, 0.22], "rotation": 45, "period": 0.18, "duty": 0.5, "color": [0.08, 0.08, 0.08]},
                                        {"shape": "rect", "at": [0.5, 0.62], "size": [0.62, 0.07], "color": [0.08, 0.08, 0.08]},
                                        {"shape": "rect", "at": [0.5, 0.5], "size": [0.5, 0.07], "color": [0.08, 0.08, 0.08]},
                                        {"shape": "rect", "at": [0.5, 0.76], "size": [0.4, 0.07], "color": [0.08, 0.08, 0.08]}]},
}
DECALS = 0


def decal(name, x, z, y=None, world=False, **kw):
    global DECALS
    op = {"op": "Decal", "decal": name, "x": R(x), "z": R(z)}
    if y is not None:
        op["y"] = R(y)
    op.update({k: (R(v) if isinstance(v, float) else v) for k, v in kw.items()})
    if world:
        op["anchor"] = "world"
    t.ops.append(op); DECALS += 1


def scatter(names, x0, z0, x1, z1, count, seed, y=None, world=False, **kw):
    global DECALS
    op = {"op": "DecalScatter", "decals": list(names), "x0": R(x0), "z0": R(z0), "x1": R(x1), "z1": R(z1), "count": count, "seed": seed}
    if y is not None:
        op["y"] = R(y)
    op.update(kw)
    if world:
        op["anchor"] = "world"
    t.ops.append(op); DECALS += count


# the forest floor and the roads
scatter(["Leaf Litter", "Leaf Litter", "Moss Patch", "Dry Grass", "Dirt Patch", "Mud Patch"], -118, -82, 118, 82, 420, 1, gap=2.0)
for pts in ROADS:
    for s in (1, -1):
        for a, b in zip(pts, pts[1:]):
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            for k in range(int(L / 7)):
                f = (k + 0.5) / max(1, int(L / 7))
                decal(rng.choice(["Tyre Tracks", "Mud Patch", "Gravel Scatter", "Puddle"]), s * (a[0] + (b[0] - a[0]) * f), a[1] + (b[1] - a[1]) * f)
for s in (-1, 1):
    scatter(["Oil Stain", "Oil Stain", "Tyre Tracks", "Mud Patch", "Puddle", "Gravel Scatter"], s * 116, -28, s * 84, 28, 40, 2 + (s > 0), gap=1.5)
    for z in (-24, -18, -7, 26):
        decal("Parking Bay", s * 97.0, z, rotation=90)
    decal("Helipad" if s > 0 else "Loading Bay Box", s * 90.0, -38.0, size=7.0 if s > 0 else 5.0)
# underground: building floors and walls, tunnels, the shaft
for rec in PLACED:
    if rec["kind"] in ("surface",):
        continue
    fp = rec["fp"]
    lv = rec["levels"][0]
    k = int((fp[2] - fp[0]) * (fp[3] - fp[1]) / 14) + 3
    scatter(["Floor Cracks", "Oil Stain", "Footprints", "Concrete Patch", "Water Stain", "Rust Stain"], fp[0] + 0.5, fp[1] + 0.5, fp[2] - 0.5, fp[3] - 0.5, k, 10 + len(PLACED), y=lv, world=True, gap=1.0)
    scatter(["Wall Cracks", "Water Damage", "Mould", "Rust Bleed", "Grime Streaks", "Silo Seepage"], fp[0] + 0.5, fp[1] + 0.5, fp[2] - 0.5, fp[3] - 0.5, k // 2 + 2, 11, y=lv, world=True, onto="wall")
    scatter(["Ceiling Water Spot", "Cobweb"], fp[0] + 0.5, fp[1] + 0.5, fp[2] - 0.5, fp[3] - 0.5, k // 3 + 1, 12, y=lv + 1.5, world=True, onto="ceiling")
    for dd in rec["doors"]:
        if rec["kind"] == "buried" and dd[0] == 0:
            pass
decal("Silo No Lone Zone", 0.0, -46.0, Y_M + 0.1, world=True)
decal("Silo Floor Trefoil", 0.0, 44.6, Y_P + 0.1, world=True, size=3.4)
decal("Silo Floor Trefoil", 0.0, 7.6, Y_S, world=True, size=3.0)
decal("Silo Floor Trefoil", 0.0, -7.6, Y_S, world=True, size=3.0)
decal("Stencil Number 1", 0.0, -10.4, TOP, world=True, size=1.4)
decal("Stencil Number 2", 0.0, 11.0, MID, world=True, size=1.4)
decal("Stencil Number 3", 4.2, 7.6, Y_S, world=True, size=1.6)
for s in (-1, 1):
    decal("Hazard Stripes", s * 12.3, 0.0, TOP, world=True, size=3.0, rotation=90)
    decal("Hazard Chevrons", s * 4.1, -2.4, MID, world=True, rotation=0 if s > 0 else 180)
    decal("Silo Blast Door Stencil", s * 50.0, 2.4, Y_M + 1.2, world=True, onto="wall")
    decal("Silo Blast Door Stencil", s * 38.0, -2.4, Y_M + 1.2, world=True, onto="wall")
for a in range(0, 360, 30):
    r = math.radians(a + 10)
    decal("Silo Seepage", 10.8 * math.cos(r), 10.8 * math.sin(r), Y_S + 6.0 + (a % 60) / 10.0, world=True, onto="wall", size=3.6)
decal("Hazard Stripes", -3.0, -8.9, TOP, world=True, size=4.0)
decal("Hazard Stripes", 3.0, -8.9, TOP, world=True, size=4.0)
decal("Hazard Stripes", -3.0, 8.9, TOP, world=True, size=4.0)
decal("Hazard Stripes", 3.0, 8.9, TOP, world=True, size=4.0)
for pts, w, h, dressed in TUNNELS:
    for a, b in zip(pts, pts[1:]):
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        ya = a[2] if a[2] is not None else g0(a[0], a[1])
        yb = b[2] if b[2] is not None else g0(b[0], b[1])
        for k in range(int(L / 5)):
            f = (k + 0.5) / max(1, int(L / 5))
            x, z = a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f
            if V.kind(x, z) == vd.SOLID or math.hypot(x, z) < SHAFT_R + 1:
                continue
            decal(rng.choice(["Water Stain", "Puddle", "Oil Drips", "Floor Cracks", "Footprints", "Mud Patch"]), x, z, ya + (yb - ya) * f, world=True)
# the fronts of the bases
for s in (-1, 1):
    decal("Stop Line", s * 84.0, 0.0, rotation=90, size=4.0)


# ================================================================ write
def write():
    for fn, d in PACK_DECALS.items():
        dd = {"format": "call-of-slop-decal/1", "name": d["name"], "description": d["description"], "author": "Call of Slop"}
        dd.update({k: v for k, v in d.items() if k not in ("name", "description")})
        folder = os.path.join(REPO, "Maps/World/Decals/Silo")
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, fn + ".json"), "w") as f:
            json.dump(dd, f, indent=1); f.write("\n")
    terrain = dict(TERRAIN)
    terrain["features"] = FEAT + FEAT_VOIDS
    m = {
        "format": "call-of-slop-map/1",
        "name": "Silo",
        "description": "Forested hills over a Cold War missile silo: fight down the tunnels for launch control, the reactor and the silo floor.",
        "author": "Call of Slop",
        "order": 503,
        "maxPlayers": 24,
        "flagPool": ["us"],
        "skybox": {"preset": "clear", "sun": {"time": 15.6, "latitude": 38}, "clouds": {"coverage": 0.42, "speed": 8},
                   "haze": {"color": "#e6dcc4", "strength": 0.25}},
        "boundary": {"shape": "smooth", "points": [[R(x), R(z)] for x, z in CTRL], "noise": NOISE,
                     "edge": {"style": "cliff", "height": 7, "width": 6}},
        "terrain": terrain,
        "teamSpawns": [[[R(x), R(z)] for x, z in BLUE_SP], [[R(x), R(z)] for x, z in RED_SP]],
        "hills": [{"x": h["x"], "z": h["z"], "y": h["y"], "radius": h["radius"]} for h in HILLS],
        "flags": [[R(x), R(z)] for x, z in FLAGS],
        "ops": t.ops,
        "foliage": {"seed": 503, "paths": t.paths, "regions": REGIONS, "exclude": EXCLUDE, "items": t.items},
        "movers": MOVERS,
    }
    with open(OUT_JSON, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    n_props = sum(1 for o in t.ops if o["op"] == "Prop")
    n_box = sum(1 for o in t.ops if o["op"] == "Block")
    n_mesh = sum(1 for o in t.ops if o["op"] == "Mesh")
    n_bld = sum(1 for o in t.ops if o["op"] == "Building")
    print(f"wrote {OUT_JSON}: {len(t.ops)} ops ({n_bld} buildings, {n_box} blocks, {n_props} props, {n_mesh} meshes, ~{DECALS} decals), "
          f"{len(FEAT_VOIDS)} hole/cave features, {len(TUNNELS)} tunnels, {N_RIBS} arch sets, {FORT_COUNT} sandbag walls in forts, {FENCE} fence panels, "
          f"{SCENERY} scenery trees; area {t.area():.0f} m2")
    for p in PROBLEMS:
        print("PROBLEM:", p)
    if os.environ.get("SILO_DEBUG"):
        for f in FAILED:
            print("not placed:", f)


if __name__ == "__main__":
    write()
    if OUT_PNG:
        preview(OUT_PNG)
