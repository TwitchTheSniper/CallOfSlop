#!/usr/bin/env python3
"""Generator for Maps/Cluttered/10-Timberline.json (large, 16 players): a forest with little cabins all over and two bunkers
hidden underground. Usage: python3 tools/cluttered/timberline.py <repo root> [preview.png]

Asymmetric by design: Blue holds the low west side (the ranger station, the lake with its dock, canoes and ducks), Red the
logging camp up the hill in the east (log landing, sawmill shed, bunkhouse, a truck on the logging road). Red has the high
ground; Blue gets more cover and the covered flanks (the creek bed and denser woods). Fairness is measured, not mirrored:
the generator walks a 0.5 m grid over the terrain (slopes too steep to walk are walls; the bunkers' tunnels are links)
from each team's spawns to every hill and the enemy flag, and checks that no Blue spawn sees a Red one over the terrain.

Bunkers: the engine's terrain is a heightfield (no holes), so a bunker is a pit cut into the ground (a "cut" flatten) with
concrete rooms built inside it from boxes and a roof slab at the surface (a dirt-coloured box, grass growing on it), the
bunker floor a box slab (the bots' nav graph samples box tops under a roof). Ways in: stairs down through a hatch in a
cabin's floor, and a sunken trench (a "cut" ramp) to a door, one under brush on a hillside (Blue's), one a culvert in the
creek bank (Red's). Cabins are built from the pack's log-wall kit props (instanced: no draw call per wall).

Built with townkit.py (occupancy, props, foliage helpers, the walk field) and terrain.py (the game's terrain generator,
ported formula for formula, for the ground heights the world-anchored bunker parts need).
"""
import heapq, json, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import townkit as tk
import terrain as tr
from townkit import yaw_facing, yaw_along, R, col

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT_JSON = os.path.join(REPO, "Maps/Cluttered/10-Timberline.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None

CTRL = [(-94, -2), (-88, -30), (-70, -52), (-44, -64), (-14, -60), (16, -68), (46, -62), (72, -48), (90, -22), (95, 6), (86, 32), (64, 52),
        (36, 62), (6, 60), (-22, 66), (-52, 62), (-78, 44), (-92, 22)]
NOISE = {"amplitude": 1.8, "scale": 20, "seed": 10, "octaves": 2}
t = tk.Town(REPO, CTRL, NOISE, pack_props=[os.path.join(REPO, "Maps/Cluttered/Props")])
rng = random.Random(410)
HALF_X = max(abs(p[0]) for p in t.outline)
HALF_Z = max(abs(p[1]) for p in t.outline)

# ---------------------------------------------------------------- palette
CONCRETE = (0.40, 0.39, 0.36); BUNKER = (0.33, 0.33, 0.31); ROOFDIRT = (0.40, 0.33, 0.21); PLANK = (0.50, 0.38, 0.24)
DECK = (0.46, 0.35, 0.22); WATER = (0.20, 0.34, 0.36); DIRT = (0.42, 0.34, 0.24); STEEL = (0.36, 0.37, 0.38)
WALL_H = 2.7

# ================================================================ objectives (decided first: everything keeps clear)
BLUE_SP = [(-84, -14), (-80, -18), (-86, -6), (-81, -9), (-85, 4), (-81, 8), (-86, 12), (-85, 18), (-82, -1)]
RED_SP = [(82, -12), (86, -6), (79, -18), (84, 2), (80, 8), (86, 12), (77, 14), (88, -16), (81, -3)]
FFA_SP = [(-40, -52), (34, -56), (-46, 54), (42, 52), (-10, -20), (22, 16)]
HILLS = [(0.5, 2.0, 6.0), (0.0, -44.0, 6.0), (2.5, 47.0, 6.0)]
FLAGS = [(-89.0, 3.0), (90.0, -2.0)]
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.keepout.append((x, z, 2.6))
for x, z, r in HILLS:
    t.keepout.append((x, z, r + 0.8))
for x, z in FLAGS:
    t.keepout.append((x, z, 3.0))

# ================================================================ terrain
# Shape: rolling noise, the long rise to the logging camp in the east, a knoll for the fire lookout, a low ridge in the
# north-west woods, the creek down from the north-east hills to the lake in the south-west.
CREEK = [(74, -60), (54, -38), (36, -18), (22, 2), (12, 17), (6, 30), (-8, 36), (-26, 34), (-42, 33)]
FEAT = [
    {"type": "noise", "amplitude": 0.9, "scale": 30, "octaves": 4, "seed": 1010},
    {"type": "noise", "amplitude": 0.22, "scale": 7, "octaves": 2, "seed": 1011},
    {"type": "hill", "x": 118, "z": 0, "radius": 128, "radiusZ": 170, "height": 10},
    {"type": "hill", "x": 4, "z": -46, "radius": 20, "height": 2.6},
    {"type": "ridge", "points": [[-72, -38], [-56, -46], [-36, -46]], "width": 16, "height": 2.2},
    {"type": "hill", "x": 52, "z": 40, "radius": 16, "height": 1.8},
    {"type": "hill", "x": -30, "z": 54, "radius": 14, "height": 1.6},
    # spawn screens: low wooded berms in front of each team's spawns, staggered so no straight line runs past them
    {"type": "ridge", "points": [[-76, -28], [-71, -18], [-72, -8], [-77, -3]], "width": 13, "height": 3.0},
    {"type": "ridge", "points": [[-77, 2], [-72, 8], [-72, 16], [-76, 24]], "width": 13, "height": 3.0},
    {"type": "ridge", "points": [[73, -32], [69, -22], [70, -10], [75, -5]], "width": 13, "height": 3.0},
    {"type": "ridge", "points": [[75, 1], [70, 8], [70, 18], [74, 26]], "width": 13, "height": 3.0},
    {"type": "valley", "points": [list(p) for p in CREEK], "width": 9, "depth": 1.7, "material": "sand"},
    {"type": "flatten", "x": -58, "z": 37, "radius": 10, "height": -1.1, "edge": 6, "mode": "cut", "material": "sand"},
    {"type": "flatten", "x": -46, "z": 42, "radius": 6, "height": -1.1, "edge": 5, "mode": "cut", "material": "sand"},
]
WATER_Y = -0.5
SURF = {"grass": "#4f6e32", "dirt": "#6e5a3c", "rock": "#77746c", "sand": "#8f8460", "dirtSlope": 22, "rockSlope": 36, "variation": 0.12}
TERRAIN = {"cell": 1, "margin": 26, "surface": SURF, "features": FEAT}


def base_terrain():
    return tr.generate(TERRAIN, HALF_X, HALF_Z)


T0 = base_terrain()


def g0(x, z):
    return float(T0.sample(x, z))


def q(v, step=0.25):
    return round(v / step) * step


PADS = []      # (x, z, w, d, height) building pads: flattened before the cuts, so ops stand level


def pad(x, z, w, d, h=None, edge=3.0, material=None):
    h = q(g0(x, z)) if h is None else h
    f = {"type": "flatten", "x": R(x), "z": R(z), "w": R(w), "d": R(d), "height": h, "edge": edge}
    if material:
        f["material"] = material
    FEAT.append(f)
    PADS.append((x, z, w, d, h))
    return h


# ================================================================ world-anchored boxes, props and meshes
BUNKER_BOXES = []     # underground boxes (not marked on the surface occupancy grid)


def wblock(x0, y0, z0, x1, y1, z1, c, surface=False):
    x0, x1 = min(x0, x1), max(x0, x1); z0, z1 = min(z0, z1), max(z0, z1)
    t.ops.append({"op": "Block", "x0": R(x0), "y0": R(y0), "z0": R(z0), "x1": R(x1), "y1": R(y1), "z1": R(z1), "color": col(c), "anchor": "world"})
    if surface:
        t.boxes.append((x0, z0, x1, z1, 0, y1 - y0, c))
    else:
        BUNKER_BOXES.append((x0, z0, x1, z1, y0, y1, c))


def wmesh(kind, x, z, c, y, **kw):
    op = {"op": "Mesh", "mesh": kind, "x": R(x), "z": R(z), "y": R(y)}
    for k, v in kw.items():
        op[k] = R(v) if isinstance(v, float) else v
    op["color"] = col(c)
    op["anchor"] = "world"
    t.ops.append(op); t.meshes += 1


def wprop(name, x, z, yaw, y, tag="world", mark=False):
    """A prop at an absolute height (on a bunker roof, a cabin floor over a bunker, afloat)."""
    if name not in t.P:
        raise KeyError(name)
    op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
    if yaw:
        op["yaw"] = R(yaw % 360)
    op["y"] = R(y); op["anchor"] = "world"
    t.ops.append(op); t.props.append((name, x, z, yaw, 9.0, tag))
    if mark and t.P[name]["solid"]:
        r = t.foot(name, x, z, yaw); t.mark(t.occ, r)
        if t.P[name]["h"] > 0.55:
            t.mark(t.walk, r)
        if t.P[name]["h"] > 1.6:
            t.mark(t.tall, r)
    t.notes[tag] = t.notes.get(tag, 0) + 1


def kit(name, x, z, yaw, world_y=None, tag="cabin"):
    """A cabin-kit prop (walls overlap at the corners, so no occupancy check); marks what it blocks."""
    if world_y is not None:
        wprop(name, x, z, yaw, world_y, tag=tag)
    else:
        op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
        if yaw % 360:
            op["yaw"] = R(yaw % 360)
        t.ops.append(op); t.props.append((name, x, z, yaw, 0.0, tag)); t.notes[tag] = t.notes.get(tag, 0) + 1


# ================================================================ cabins (log-wall kit)
CABINS = []   # dicts: name, rect, room, doors (world points), world_y


def modules(L):
    return {9.0: [3.0, 3.0, 3.0], 6.0: [3.0, 3.0], 4.5: [3.0, 1.5]}[L]


def cabin(name, cx, cz, w, d, doors, windows=(), world_y=None, porch=None, chimney=None, front=(0, 1)):
    """A log cabin w x d centred at (cx, cz), ridge along X. doors / windows: (side, module index) with side N, S, E, W.
    Returns the Room for furnishing. With world_y the cabin stands at that absolute height (on a bunker roof)."""
    x0, x1, z0, z1 = cx - w / 2, cx + w / 2, cz - d / 2, cz + d / 2
    door_pts = []
    for side, L, along_x in (("N", w, True), ("S", w, True), ("W", d, False), ("E", d, False)):
        pos = -L / 2
        for i, m in enumerate(modules(L)):
            c = pos + m / 2
            kind = "door" if (side, i) in doors else "window" if (side, i) in windows else None
            nm = f"Log Wall {m:g}m" + ({"door": " Door", "window": " Window"}[kind] if kind and m == 3.0 else "")
            if along_x:
                x, z = cx + c, (z0 + 0.15 if side == "N" else z1 - 0.15)
                yaw = 180 if side == "N" else 0
            else:
                x, z = (x0 + 0.15 if side == "W" else x1 - 0.15), cz + c
                yaw = 270 if side == "W" else 90
            kit(nm, x, z, yaw, world_y)
            if kind == "door":
                fx, fz = {"N": (0, -1), "S": (0, 1), "W": (-1, 0), "E": (1, 0)}[side]
                door_pts.append((x, z, fx, fz))
            pos += m
    kit(f"Cabin Roof {w:g}x{d:g}", cx, cz, 0, world_y)
    # occupancy: the wall strips; keep the doorways clear (inside and out) and walkable for solid props
    if True:
        for r in ((x0, z0, x1, z0 + 0.3), (x0, z1 - 0.3, x1, z1), (x0, z0, x0 + 0.3, z1), (x1 - 0.3, z0, x1, z1)):
            t.mark(t.occ, r); t.mark(t.walk, r); t.mark(t.tall, r)
        for (x, z, fx, fz) in door_pts:
            for wv in (t.walk, t.tall):
                if fx == 0:
                    t.mark(wv, (x - 0.55, z - 0.2, x + 0.55, z + 0.2), 0)
                else:
                    t.mark(wv, (x - 0.2, z - 0.55, x + 0.2, z + 0.55), 0)
            ax, az = abs(fx), abs(fz)
            t.mark(t.occ, (x - 0.8 - ax * 1.4, z - 0.8 - az * 1.4, x + 0.8 + ax * 1.4, z + 0.8 + az * 1.4), 3)   # the doorway, both sides
            for wv in (t.walk, t.tall):
                t.mark(wv, (x - 0.55 * az - 0.15 * ax, z - 0.55 * ax - 0.15 * az, x + 0.55 * az + 0.15 * ax, z + 0.55 * ax + 0.15 * az), 0)
            t.add_lane([(x - fx * 1.6, z - fz * 1.6), (x + fx * 3.0, z + fz * 3.0)], 1.4)
    if world_y is None:   # interior floor boards (Wood: nothing grows; the roof keeps the rain off anyway)
        t.mesh("box", cx, cz, PLANK, y=0.0, width=w - 0.4, height=0.03, depth=d - 0.4, material="Wood")
    else:
        wmesh("box", cx, cz, PLANK, world_y + 0.005, width=w - 0.4, height=0.03, depth=d - 0.4, material="Wood")
    porch_at = None
    if porch:
        side = porch
        fx, fz = {"N": (0, -1), "S": (0, 1), "W": (-1, 0), "E": (1, 0)}[side]
        px = cx + fx * (w / 2 + 0.9); pz = cz + fz * (d / 2 + 0.9)
        kit("Cabin Porch", px, pz, yaw_facing(fx, fz), world_y)
        porch_at = (px, pz, fx, fz)
        if world_y is None:
            r = (px - 1.6, pz - 1.6, px + 1.6, pz + 1.6)
            t.mark(t.occ, r, 3)
    if chimney:
        fx = -1 if chimney == "W" else 1
        kit("Stone Chimney", cx + fx * (w / 2 + 0.45), cz, 0, world_y)
        if world_y is None:
            t.mark(t.occ, (cx + fx * (w / 2 + 0.45) - 0.5, cz - 0.5, cx + fx * (w / 2 + 0.45) + 0.5, cz + 0.5))
    room = tk.Room(t, x0, z0, x1, z1, front)
    CABINS.append({"name": name, "rect": (x0, z0, x1, z1), "room": room, "doors": door_pts, "world_y": world_y, "porch": porch_at})
    return room


# ================================================================ bunkers (pits cut into the ground, rooms of boxes, a roof at the surface)
# Bunker A (Blue, north-west) is described in its own frame; Bunker B (Red, south-east) is the same plan turned 180 degrees
# about the map centre (x, z) -> (-x, -z): same rooms, same walks, so neither side gets a better tunnel. Only the surface
# heights (B is higher up the hill) and the dressing differ: A's trench door is under brush on a slope, B's opens as a
# concrete culvert in the creek bank.
B_DEPTH = 3.5         # surface to floor (S is a multiple of 0.5, so the floor height is exact in the heightfield)
B_RISE_N = 14         # 0.25 m risers: the nav graph samples every 0.6 m and steps up at most 0.55 m
# The plan (bunker A; x grows east, z south). The stairs climb west from the room to x = -51 (a heightfield sample line:
# the pit is cut no further, so the ground under the landing beyond stays at the surface); a landing box covers the
# last metre of the pit's slope, and the landing strip itself (x -53.7..-52, open in the roof) is the ground, so a
# waypoint there lands on it: the bots' way down. The rest of the cabin's floor is the roof.
BUNKER_PLAN = {
    "cabin": (-49.5, -29.5, 9.0, 6.0),
    "shaft": (-51.0, -31.3, -45.5, -29.5),        # the stair run (top at x = -51)
    "landing": (-52.0, -51.0),                    # the landing box over the end of the pit
    "hole": (-53.7, -31.3, -45.5, -29.5),         # the opening in the roof (the cabin's floor): landing strip + stairs
    "room": (-45.5, -34.5, -32.5, -24.5),
    "corr": (-32.5, -30.0, -21.0, -27.6),
    "door_x": -21.0, "trench_end": (-8.0, -28.8),
}
BUNKERS = []


def T(p, s):
    return (p[0] * s, p[1] * s)


def TR(r, s):
    a, b = T((r[0], r[1]), s), T((r[2], r[3]), s)
    return (min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1]))


def bunker_terrain(s, name):
    """The pad at the surface height (before the pits are cut). Returns the surface height S."""
    P = BUNKER_PLAN
    rects = [TR(P[k], s) for k in ("shaft", "room", "corr")]
    cab = P["cabin"]; cabr = TR((cab[0] - cab[2] / 2, cab[1] - cab[3] / 2, cab[0] + cab[2] / 2, cab[1] + cab[3] / 2), s)
    bx0 = min(r[0] for r in rects + [cabr]) - 2.7; bz0 = min(r[1] for r in rects + [cabr]) - 2.7
    bx1 = max(r[2] for r in rects + [cabr]) + 2.7; bz1 = max(r[3] for r in rects + [cabr]) + 2.7
    cx, cz = (bx0 + bx1) / 2, (bz0 + bz1) / 2
    S = q(np.mean([g0(x, z) for x in np.linspace(bx0, bx1, 9) for z in np.linspace(bz0, bz1, 5)]), 0.5)
    F = S - B_DEPTH
    FEAT.append({"type": "flatten", "x": R(cx), "z": R(cz), "w": R(bx1 - bx0 + 2), "d": R(bz1 - bz0 + 2), "height": S, "edge": 5})
    BUNKERS.append({"name": name, "s": s, "S": S, "F": F, "rects": rects, "cabin": cabr, "hole": TR(P["hole"], s),
                    "roof": (bx0 + 1.2, bz0 + 1.2, bx1 - 1.2, bz1 - 1.2)})
    return S


def bunker_cuts(b):
    """The pit (cut flattens 1 m wider than the rooms, the shaft cut exactly to its top end) and the trench (a cut ramp)."""
    s, F = b["s"], b["F"]
    P = BUNKER_PLAN
    sx0, sz0, sx1, sz1 = P["shaft"]
    plan_cuts = [(sx0, sz0 - 0.7, sx1 + 0.7, sz1 + 0.7)] + [(r[0] - 1.0, r[1] - 1.0, r[2] + 1.0, r[3] + 1.0) for r in (P["room"], P["corr"])]
    for r in plan_cuts:
        r = TR(r, s)
        FEAT.append({"type": "flatten", "x": R((r[0] + r[2]) / 2), "z": R((r[1] + r[3]) / 2), "w": R(r[2] - r[0]), "d": R(r[3] - r[1]),
                     "height": F, "edge": 0.6, "mode": "cut"})
    dx, dz = T((P["door_x"], (P["corr"][1] + P["corr"][3]) / 2), s)
    ex, ez = T(P["trench_end"], s)
    FEAT.append({"type": "ramp", "points": [[R(dx), R(dz), F], [R(ex), R(ez)]], "width": 2.6, "edge": 1.0, "mode": "cut", "material": "dirt"})
    b["door"] = (dx, dz); b["trench_end"] = (ex, ez)


def bunker_build(b, culvert):
    """The rooms: walls, the stairs and landing in the shaft, the roof (less the opening), floors and ceilings (cosmetic),
    lamps, furniture, waypoints and the trench door's dressing. The floor is the pit's own ground (a box floor would hide
    the waypoints the bots need), with a concrete mesh on it."""
    s, S, F = b["s"], b["S"], b["F"]
    P = BUNKER_PLAN
    top = S + 0.06            # roof top / cabin floor
    ceil = S - 0.4            # underside of the roof
    sh, rm, co = b["rects"]

    def wall(x0, z0, x1, z1, y0=F, y1=ceil):
        a = TR((x0, z0, x1, z1), s)
        wblock(a[0], y0, a[1], a[2], y1, a[3], BUNKER)
    rx0, rz0, rx1, rz1 = P["room"]; sx0, sz0, sx1, sz1 = P["shaft"]; cx0, cz0, cx1, cz1 = P["corr"]
    lx0, lx1 = P["landing"]
    wall(rx0 - 0.3, rz0 - 0.3, rx1 + 0.3, rz0)                 # room north
    wall(rx0 - 0.3, rz1, rx1 + 0.3, rz1 + 0.3)                 # room south
    wall(rx0 - 0.3, rz0, rx0, sz0 - 0.3)                       # room west, either side of the shaft
    wall(rx0 - 0.3, sz1 + 0.3, rx0, rz1)
    wall(rx1, rz0, rx1 + 0.3, cz0 - 0.3)                       # room east, either side of the corridor
    wall(rx1, cz1 + 0.3, rx1 + 0.3, rz1)
    wall(rx1 + 0.3, cz0 - 0.3, cx1, cz0)                       # corridor sides
    wall(rx1 + 0.3, cz1, cx1, cz1 + 0.3)
    dz0, dz1 = (cz0 + cz1) / 2 - 0.7, (cz0 + cz1) / 2 + 0.7   # the door at the corridor's end
    wall(cx1, cz0 - 0.3, cx1 + 0.3, dz0)
    wall(cx1, dz1, cx1 + 0.3, cz1 + 0.3)
    wall(cx1, dz0, cx1 + 0.3, dz1, y0=F + 2.3)
    wall(lx0, sz0 - 0.3, rx0 - 0.3, sz0, y1=top)               # shaft sides, up to the cabin floor
    wall(lx0, sz1, rx0 - 0.3, sz1 + 0.3, y1=top)
    # stairs: B_RISE_N steps from the floor up to the surface, climbing west; the landing box at the top
    rise = (S - F) / B_RISE_N; run = (sx1 - sx0) / B_RISE_N
    for k in range(1, B_RISE_N + 1):
        xa, xb = sx1 - (k - 1) * run, sx1 - k * run
        a = TR((xb, sz0, xa, sz1), s)
        wblock(a[0], F, a[1], a[2], F + k * rise, a[3], (0.38, 0.37, 0.35))
    a = TR((lx0, sz0, lx1, sz1), s)
    wblock(a[0], F, a[1], a[2], S, a[3], (0.38, 0.37, 0.35))
    # roof: the bounding box less the opening (4 slabs), dirt-coloured
    X0, Z0, X1, Z1 = b["roof"]
    hx0, hz0, hx1, hz1 = b["hole"]
    for r in ((X0, Z0, hx0, Z1), (hx1, Z0, X1, Z1), (hx0, Z0, hx1, hz0), (hx0, hz1, hx1, Z1)):
        if r[2] - r[0] > 0.05 and r[3] - r[1] > 0.05:
            wblock(r[0], ceil, r[1], r[2], top, r[3], ROOFDIRT, surface=True)
    # floors (on the pit's ground) and ceilings: cosmetic concrete; planks on the landing strip
    for r in (rm, co):
        cxr, czr, wr, dr = (r[0] + r[2]) / 2, (r[1] + r[3]) / 2, r[2] - r[0], r[3] - r[1]
        wmesh("box", cxr, czr, (0.30, 0.30, 0.29), ceil - 0.02, width=wr, height=0.02, depth=dr, material="Concrete")
        wmesh("box", cxr, czr, (0.36, 0.35, 0.33), F, width=wr, height=0.015, depth=dr, material="Concrete")
    a = TR((P["hole"][0], sz0, lx0, sz1), s)
    wmesh("box", (a[0] + a[2]) / 2, (a[1] + a[3]) / 2, PLANK, S, width=a[2] - a[0], height=0.03, depth=a[3] - a[1], material="Wood")
    # lamps (caged bulbs on the generator, flickering), every few metres
    lamp_y = ceil - 0.7 - F
    for (px, pz) in ((-42.5, -31.5), (-36.0, -31.5), (-42.5, -27.5), (-36.0, -27.5), (-28.0, -28.8), (-23.5, -28.8), (-44.4, -30.4)):
        x, z = T((px, pz), s)
        t.ops.append({"op": "Prop", "name": "Bunker Cage Lamp", "x": R(x), "z": R(z), "y": R(lamp_y)}); t.props.append(("Bunker Cage Lamp", x, z, 0, lamp_y, "bunker"))
    # furniture, in plan coordinates (props stand on the pit's floor)
    def put(name, px, pz, yaw):
        x, z = T((px, pz), s)
        y = (yaw + (180 if s < 0 else 0)) % 360
        t.ops.append({"op": "Prop", "name": name, "x": R(x), "z": R(z), "yaw": R(y)}); t.props.append((name, x, z, y, 0.0, "bunker"))
    for px in (-44.4, -41.6, -38.8):
        put("Bunk Bed", px, -33.4, 0)                        # bunks along the north wall
    put("Metal Lockers", -36.4, -33.9, 0)
    put("Map Table", -39.0, -29.2, 0)                         # the plotting table in the middle
    put("Office Chair", -36.6, -26.7, 180)
    put("Office Desk", -35.6, -25.4, 180)
    put("Running Generator", -33.6, -33.2, 270)              # the generator in the corner, the lamps flicker on it
    put("Gas Cylinders", -33.6, -31.6, 270)
    for px in (-44.2, -41.8):
        put("Metal Shelving", px, -25.0, 180)                 # supply shelves along the south wall
    put("Supply Crate", -39.4, -25.1, 0); put("Supply Crate", -39.4, -26.0, 10)
    put("Filing Cabinet", -33.2, -25.1, 180)
    def top_put(name, px, pz, h):
        x, z = T((px, pz), s)
        t.ops.append({"op": "Prop", "name": name, "x": R(x), "z": R(z), "y": R(h), "yaw": R(rng.uniform(0, 360))}); t.props.append((name, x, z, 0, h, "bunker"))
    top_put("Field Radio", -35.9, -25.5, 0.76)
    top_put("Camp Lantern", -38.4, -29.0, 0.88)
    top_put("Ammo Can", -44.2, -25.0, 0.95); top_put("Ammo Can", -41.8, -25.0, 1.45); top_put("Ammo Can", -39.4, -25.1, 0.6)
    for k in range(10):
        top_put(rng.choice(["Ammo Can", "Newspaper", "Litter", "Soda Cans", "Bottle", "Backpack", "Duffel Bag", "Book Pile", "Bucket"]),
                rng.uniform(-44.5, -33.5), rng.uniform(-32.0, -26.8), 0.0)
    # waypoints: the rooms, the corridor, the bottom of the stairs and the landing strip at the top (on the ground)
    for (px, pz) in ((-43.5, -30.4), (-40.0, -31.8), (-41.0, -27.2), (-35.0, -29.6), (-30.0, -28.8), (-25.0, -28.8), (-22.0, -28.8),
                     (-44.9, -30.4), (-52.9, -30.4)):
        x, z = T((px, pz), s)
        t.ops.append({"op": "Waypoint", "x": R(x), "z": R(z)})
    # the trench door's dressing
    dx, dz = b["door"]; ex, ez = b["trench_end"]
    L = math.hypot(ex - dx, ez - dz); ux, uz = (ex - dx) / L, (ez - dz) / L
    if culvert:
        # a big concrete culvert pipe round the doorway, lying along the trench, and stones down the gully
        wmesh("tube", dx + ux * 1.5, dz + uz * 1.5, (0.55, 0.55, 0.52), F - 0.25, radius=1.75, inner=1.5, height=2.6, pitch=90.0,
              yaw=yaw_facing(ux, uz), segments=20, material="Concrete")
        for k in range(4):
            t.prop("Forest Stones", dx + ux * (4.5 + k * 2.2) + rng.uniform(-0.5, 0.5), dz + uz * (4.5 + k * 2.2) + rng.uniform(-0.5, 0.5),
                   rng.uniform(0, 360), check=False, tag="bunker")
    else:
        # a hillside door: a timber frame round the doorway and a rusty steel door hanging open
        nx, nz = -uz, ux
        for sgn in (1, -1):
            wmesh("box", dx + ux * 0.35 + nx * sgn * 0.85, dz + uz * 0.35 + nz * sgn * 0.85, (0.30, 0.24, 0.16), F, width=0.25, height=2.6, depth=0.25, material="Wood")
        wmesh("box", dx + ux * 0.35, dz + uz * 0.35, (0.30, 0.24, 0.16), F + 2.35, width=2.0 if abs(nx) > 0.5 else 0.3, height=0.3, depth=0.3 if abs(nx) > 0.5 else 2.0, material="Wood")
        wmesh("box", dx + ux * 0.9 + nx * 1.1, dz + uz * 0.9 + nz * 1.1, (0.42, 0.28, 0.20), F, width=1.3, height=2.2, depth=0.08,
              yaw=yaw_along(ux, uz) + 15, material="Metal")
    b["entrances"] = [T((-52.9, -30.4), s), b["trench_end"]]


# ================================================================ the layout (terrain features that depend on it come first)
# --- bunker pads, cabin pads, hill pads (in that order; the bunker cuts go last)
SA = bunker_terrain(1, "Ranger bunker")
SB = bunker_terrain(-1, "Logging bunker")

CAB = {}   # name -> (cx, cz, w, d, pad height)
for name, cx, cz, w, d in (("Ranger Station", -68.0, -2.0, 9.0, 6.0), ("Generator Shed", -76.0, 22.0, 4.5, 4.5), ("Lake Cabin", -79.0, 33.0, 6.0, 4.5),
                           ("Fishing Shack", -66.0, 20.0, 4.5, 4.5), ("Old Cabin", -24.0, -52.0, 6.0, 4.5), ("Hermit Cabin", 40.0, -48.0, 6.0, 4.5),
                           ("Bunkhouse", 70.0, -24.0, 9.0, 6.0), ("Tool Shed", 66.0, 22.0, 6.0, 4.5), ("South Cabin", 26.0, 52.0, 6.0, 6.0),
                           ("Woodcutter Cabin", -30.0, 12.0, 6.0, 4.5), ("Trail Hut", 48.0, -12.0, 4.5, 4.5)):
    CAB[name] = (cx, cz, w, d, pad(cx, cz, w + 5, d + 5, edge=4))
for (x, z, r) in HILLS:
    pad(x, z, r * 2 + 3, r * 2 + 3, edge=6)
SAWMILL = (60.0, 2.0, 12.0, 8.0)
pad(SAWMILL[0], SAWMILL[1], SAWMILL[2] + 6, SAWMILL[3] + 6, edge=5, material="dirt")
LANDING = (72.0, 34.0)       # the log landing by the tool shed
pad(LANDING[0], LANDING[1] - 6, 16, 10, edge=5, material="dirt")
LOOKOUT = (16.0, -48.0)
pad(LOOKOUT[0], LOOKOUT[1], 8, 8, edge=4)

# --- trails (dirt paths) and the logging road; they keep to the ground's own height along their points
TRAILS = [
    ("blue_mid", [(-64, -2), (-48, -6), (-30, -4), (-14, 0), (-4, 2)], 2.6),
    ("red_mid", [(56, 2), (42, -6), (34.5, -8), (25.5, -8), (14, -2), (8, 2)], 2.6),
    ("blue_north", [(-66, -10), (-61, -22), (-62, -36), (-48, -45), (-30, -46), (-14, -45), (-4, -45)], 2.4),
    ("red_road", [(72, -12), (62, -30), (46, -40), (28, -44), (9, -45)], 4.5),
    ("blue_south", [(-64, 8), (-54, 14), (-40, 22), (-30, 29), (-30, 38), (-22, 42), (-12, 45)], 2.4),
    ("red_south", [(68, 10), (62, 22), (62, 38), (48, 45), (30, 44), (16, 46), (0, 46)], 2.4),
    ("mid_north", [(2, -4), (4, -22), (2, -37)], 2.2),
    ("mid_south", [(0, 8), (0, 22), (-1, 28.5), (-1, 37.5), (-5, 40)], 2.2),
    ("lake_path", [(-66, 8), (-62, 14), (-57, 20)], 2.0),
    ("camp_link", [(70, -18), (68, -6), (64, 2)], 3.5),
]
BRIDGES = [((25.5, -8.0), (34.5, -8.0)), ((-1.0, 28.5), (-1.0, 37.5)), ((-30.0, 29.0), (-30.0, 38.0))]
BRIDGE_ENDS = {a for a, b in BRIDGES} | {b for a, b in BRIDGES}
for name, pts, w in TRAILS:
    # the terrain path stops at a bridge (a path across the creek would fill it in under the deck)
    runs, cur = [], [pts[0]]
    for i in range(1, len(pts)):
        if pts[i - 1] in BRIDGE_ENDS and pts[i] in BRIDGE_ENDS:
            runs.append(cur); cur = [pts[i]]
        else:
            cur.append(pts[i])
    runs.append(cur)
    for run in runs:
        if len(run) >= 2:
            FEAT.append({"type": "path", "points": [[R(x), R(z)] for x, z in run], "width": R(w), "edge": 2.0, "material": "dirt"})
    t.add_lane(pts, w + 0.6)
    t.path(pts, w, density=0.12)

# --- the bunker pits and trenches last, so nothing fills them back in
for b in BUNKERS:
    bunker_cuts(b)

TF = base_terrain()   # the final terrain (the game builds the same heightfield)


def gy(x, z):
    return float(TF.sample(x, z))


# ================================================================ structures
# --- bunkers and their hatch cabins (standing on the roofs)
bunker_build(BUNKERS[0], culvert=False)
bunker_build(BUNKERS[1], culvert=True)
for b, nm in ((BUNKERS[0], "Hunting Cabin"), (BUNKERS[1], "Trapper Cabin")):
    s = b["s"]; cxp, czp, w, d = BUNKER_PLAN["cabin"]
    cx, cz = T((cxp, czp), s)
    top = b["S"] + 0.06
    # plan: doors on the south side (module 1) and the west end (towards the team's base); turned with the plan
    doors = (("S", 1), ("W", 1)) if s > 0 else (("N", 1), ("E", 0))
    wins = (("N", 0), ("N", 2), ("E", 1)) if s > 0 else (("S", 0), ("S", 2), ("W", 0))
    room = cabin(nm, cx, cz, w, d, doors, wins, world_y=top, chimney=("W" if s > 0 else "E"))
    # the hatch: a frame and a propped-open lid at the top of the stairs; a rug pulled back; a few things
    hx, hz = T((-52.9, -32.0), s)
    wprop("Bunker Hatch Lid", hx, hz, 0 if s > 0 else 180, top + 0.01, tag="bunker")
    for (px, pz, nm2, yaw) in ((-47.4, -27.6, "Single Bed", 90), (-53.0, -27.6, "Wooden Chair", 45), (-50.4, -27.6, "Table", 0),
                               (-46.6, -31.75, "Firewood Rack", 0), (-50.0, -31.9, "Coat Stand", 0)):
        x, z = T((px, pz), s)
        wprop(nm2, x, z, yaw + (180 if s < 0 else 0), top, tag="cabin")
    x, z = T((-50.4, -27.6), s)
    wprop("Camp Lantern", x, z, 0, top + 0.78, tag="cabin")
    # porch-side bits outside the door, on the roof
    for (px, pz, nm2) in ((-47.5, -25.6, "Axe Stump"), (-44.0, -27.0, "Firewood Rack")):
        x, z = T((px, pz), s)
        wprop(nm2, x, z, rng.uniform(0, 360) if nm2 == "Axe Stump" else (90 if s > 0 else 270), top, tag="cabin", mark=True)

# --- the other cabins (on their pads)
def furnish_cabin(room, kind):
    W, D = room.W, room.D
    if kind == "ranger":
        room.put("Office Desk", W * 0.3, D - 0.45, "front"); room.put("Office Chair", W * 0.3, D - 1.2, "back")
        room.put("Filing Cabinet", 0.35, D - 0.35, "front"); room.put("Metal Lockers", W - 0.4, D - 0.3, "front")
        room.put("Bookshelf", W - 0.3, D * 0.45, "left"); room.put("Table", W * 0.62, D * 0.45, "front")
        room.put("Wooden Chair", W * 0.62, D * 0.45 + 0.8, "front"); room.put("Radiator", 0.2, D * 0.5, "right")
        return [("Field Radio", W * 0.3, D - 0.45, 0.76), ("Desk Phone", W * 0.3 + 0.5, D - 0.45, 0.76), ("Camp Lantern", W * 0.62, D * 0.45, 0.78)]
    if kind == "living":
        room.put("Fireplace" if "Fireplace" in t.P else "Armchair", W * 0.5, D - 0.4, "front")
        room.put("Armchair", W * 0.3, D * 0.5, "right"); room.put("Old Sofa", W * 0.7, D * 0.35, "back")
        room.put("Single Bed", 0.5, D - 1.1, "right"); room.put("Table", W * 0.75, D * 0.7, "front")
        return [("Camp Lantern", W * 0.75, D * 0.7, 0.78), ("Book Pile", W * 0.75 + 0.3, D * 0.7, 0.78)]
    if kind == "shed":
        room.put("Metal Shelving", W - 0.35, D * 0.5, "left"); room.put("Wooden Crate", 0.5, D - 0.5, "front")
        room.put("Portable Generator", W * 0.5, D - 0.5, "front")
        return [("Toolbox", 0.5, D - 0.5, 0.8), ("Paint Cans", W - 0.35, D * 0.5, 1.0)]
    if kind == "bunk":
        for k in range(int(W // 1.6)):
            room.put("Bunk Bed", 0.9 + k * 1.6, D - 0.55, "front")
        room.put("Table", W * 0.5, D * 0.35, "front"); room.put("Wooden Chair", W * 0.5 - 0.7, D * 0.35, "right")
        room.put("Wooden Chair", W * 0.5 + 0.7, D * 0.35, "left"); room.put("Metal Lockers", W - 0.3, D * 0.3, "left")
        return [("Camp Lantern", W * 0.5, D * 0.35, 0.78), ("Soda Cans", W * 0.5 + 0.3, D * 0.35, 0.78)]
    if kind == "fishing":
        room.put("Wooden Crate", 0.5, D - 0.5, "front"); room.put("Table", W * 0.6, D * 0.5, "front")
        return [("Bucket", W * 0.6, D * 0.5, 0.78)]
    return []


def cabin_tops(room, items):
    for name, u, v, h in items:
        x, z = room.xz(u, v)
        t.prop(name, x, z, rng.uniform(0, 360), y=h, check=False, tag="interior")


CABIN_SPECS = [
    # name, doors, windows, porch, chimney, front, furnishing
    ("Ranger Station", (("E", 1), ("W", 0)), (("N", 0), ("N", 2), ("S", 0), ("S", 2)), "E", None, (1, 0), "ranger"),
    ("Generator Shed", (("N", 0),), (), None, None, (0, -1), "shed"),
    ("Lake Cabin", (("E", 0), ("N", 1)), (("S", 0), ("W", 0)), "E", "W", (1, 0), "living"),
    ("Fishing Shack", (("S", 0),), (("E", 0),), None, None, (0, 1), "fishing"),
    ("Old Cabin", (("S", 0), ("E", 0)), (("N", 1), ("W", 0)), "S", "W", (0, 1), "living"),
    ("Hermit Cabin", (("S", 1), ("W", 0)), (("N", 0), ("E", 0)), "S", "E", (0, 1), "living"),
    ("Bunkhouse", (("W", 0), ("E", 1)), (("N", 1), ("S", 0), ("S", 2)), None, "E", (-1, 0), "bunk"),
    ("Tool Shed", (("W", 0), ("N", 0)), (("S", 1),), None, None, (-1, 0), "shed"),
    ("South Cabin", (("N", 0), ("W", 1)), (("S", 1), ("E", 0)), "N", "E", (0, -1), "living"),
    ("Woodcutter Cabin", (("E", 0), ("S", 1)), (("N", 0), ("W", 0)), "E", "W", (1, 0), "living"),
    ("Trail Hut", (("W", 0), ("S", 0)), (("E", 0),), None, None, (-1, 0), "shed"),
]
for name, doors, wins, porch, chim, front, kind in CABIN_SPECS:
    cx, cz, w, d, h = CAB[name]
    room = cabin(name, cx, cz, w, d, doors, wins, porch=porch, chimney=chim, front=front)
    cabin_tops(room, furnish_cabin(room, kind))

# porch dressing: a rocking chair (it rocks), lanterns, firewood by the chimney end
for c in CABINS:
    if c["world_y"] is not None:
        continue
    x0, z0, x1, z1 = c["rect"]
for c in CABINS:
    if c["porch"] is None:
        continue
    px, pz, fx, fz = c["porch"]
    # a rocking chair on one side of the porch (it rocks), a lantern and a bucket on the other
    t.prop("Rocking Chair", px - fz * 0.85, pz + fx * 0.85, yaw_facing(fx, fz) + rng.uniform(-25, 25), y=0.2, check=False, tag="porch")
    t.prop(rng.choice(["Camp Lantern", "Bucket", "Watering Can"]), px + fz * 1.0, pz - fx * 1.0 + 0.0, 0, y=0.2, check=False, tag="porch")

# --- the ranger station yard: flag, fire danger board, the radio mast, a pickup, the generator shed's fuel
t.prop("Flag on a Pole", -61.5, -8.0, 90, tag="ranger")
t.prop("Fire Danger Sign", -61.0, 3.5, 90, tag="ranger")
t.prop("Radio Mast", -73.5, -6.8, 0, tag="ranger")
t.prop("Pickup Truck", -58.5, -13.0, 20, tag="ranger")
t.prop("Diesel Generator", -73.0, 19.0, 0, tag="ranger")
t.prop("Jerry Can", -72.6, 20.6, 30, tag="ranger")
t.prop("Explosive Barrel", -72.5, 25.8, 0, tag="ranger")
t.prop("Explosive Barrel", -71.7, 26.5, 0, tag="ranger")
t.prop("Outhouse", -72.0, 8.5, 270, tag="ranger")
t.prop("Wheelie Bin", -73.4, 3.0, 90, tag="ranger")
t.prop("Trash Can", -62.0, 5.8, 0, tag="ranger")
t.prop("Bird Box Post", -63.5, -10.5, 0, tag="ranger")
t.prop("Trail Sign", -58.0, -3.6, 90, tag="ranger")

# --- the lake: water, a dock off the north shore, the fishing shack, canoes, a rowing boat, ducks, reeds
LAKE = [(-58.0, 37.0, 13.0), (-46.0, 42.0, 8.5)]
for (x, z, r) in LAKE:
    wmesh("cylinder", x, z, WATER, WATER_Y - 0.1, width=r * 2, height=0.1, depth=r * 2, segments=40)
    t.exclude.append({"shape": "circle", "center": [R(x), R(z)], "radius": R(r - 0.6)})
DOCK = (-58.0, 21.0, -56.0, 31.0)    # x0, z0, x1, z1 (it runs south into the lake)
dock_y = WATER_Y + 0.42
wblock(DOCK[0], dock_y - 0.25, DOCK[1], DOCK[2], dock_y, DOCK[3], DECK, surface=True)
for z in np.arange(DOCK[1] + 1.0, DOCK[3], 2.5):
    for x in (DOCK[0] + 0.1, DOCK[2] - 0.1):
        wmesh("cylinder", x, z, (0.30, 0.22, 0.14), WATER_Y - 1.0, width=0.22, height=1.6, depth=0.22, segments=8)
wprop("Canoe", -54.4, 30.0, 92, WATER_Y - 0.12, tag="lake")
wprop("Rowing Boat", -59.8, 31.2, 85, WATER_Y, tag="lake")
wprop("Life Ring Post", -56.2, 32.6, 0, dock_y, tag="lake")
wprop("Mooring Bollard", -57.8, 29.0, 0, dock_y, tag="lake")
wprop("Rope Coil", -56.5, 27.0, 0, dock_y, tag="lake")
wprop("Bucket", -57.4, 25.0, 0, dock_y, tag="lake")
for k in range(9):
    a = rng.uniform(0, math.tau); (lx, lz, lr) = LAKE[k % 2]
    f = rng.uniform(0.25, 0.75)
    wprop("Duck on a Pond", lx + math.cos(a) * lr * f, lz + math.sin(a) * lr * f, rng.uniform(0, 360), WATER_Y, tag="lake")
for (x, z, yaw) in ((-70.5, 26.5, 140), (-66.8, 24.8, 160)):
    t.prop("Canoe", x, z, yaw, tag="lake")
for (x, z) in ((-72.0, 28.0), (-44.5, 28.6), (-36.5, 41.0), (-68.0, 47.0)):
    t.prop(rng.choice(["Mallard Duck", "Hen Duck", "Duck Family"]), x, z, rng.uniform(0, 360), tag="lake")
t.prop("Park Picnic Table", -66.0, 13.5, 20, tag="lake")
t.prop("Park Picnic Table", -40.5, 26.0, 75, tag="lake")
t.prop("Kettle BBQ", -68.5, 15.0, 0, tag="lake")
t.prop("Fishing Net Pile", -61.0, 17.0, 0, tag="lake")
t.prop("Lobster Pot Stack", -65.5, 18.0, 0, tag="lake")
t.prop("Upturned Dinghy", -52.0, 22.0, 40, tag="lake")
# reeds and long grass round the shore
for (x, z, r) in LAKE:
    ring = [(x + math.cos(a) * (r + 3.0), z + math.sin(a) * (r + 3.0)) for a in [i * math.tau / 24 for i in range(24)]]
    t.region("grass", 7.0, polygon=[[R(a), R(b)] for a, b in ring], scale=[1.3, 2.2], color=[0.42, 0.52, 0.26], patchiness=0.5)
    t.region("shrub", 1.4, polygon=[[R(a), R(b)] for a, b in ring], color=[0.34, 0.46, 0.2])

# --- the creek: water strips along the bed (they sit on the lowest ground under them), footbridges where trails cross
CREEK_WATER = (0.22, 0.36, 0.38)
for k in range(len(CREEK) - 1):
    (ax, az), (bx, bz) = CREEK[k], CREEK[k + 1]
    L = math.hypot(bx - ax, bz - az); n = max(1, int(L / 4.0))
    for i in range(n):
        x0, z0 = ax + (bx - ax) * i / n, az + (bz - az) * i / n
        x1, z1 = ax + (bx - ax) * (i + 1) / n, az + (bz - az) * (i + 1) / n
        mx, mz = (x0 + x1) / 2, (z0 + z1) / 2
        if not t.inside(mx, mz, 1.5):
            continue
        bed = min(gy(mx, mz), gy(x0, z0), gy(x1, z1))
        if gy(mx, mz) - min(gy(x0, z0), gy(x1, z1)) > 0.3:      # a road or a pad crosses here: no water on top of it
            continue
        wmesh("box", mx, mz, CREEK_WATER, bed + 0.12, width=L / n + 0.6, height=0.06, depth=2.2, yaw=yaw_along(x1 - x0, z1 - z0))
for (a, b) in BRIDGES:
    y = max(gy(*a), gy(*b)) + 0.1
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    along_x = abs(b[0] - a[0]) > abs(b[1] - a[1])
    mx, mz = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
    hx, hz = (L / 2 + 0.3, 0.9) if along_x else (0.9, L / 2 + 0.3)
    wblock(mx - hx, y - 0.22, mz - hz, mx + hx, y, mz + hz, DECK, surface=True)       # the deck (walkable)
    for sgn in (1, -1):        # rails and posts (cosmetic)
        ox, oz = (0, sgn * 0.85) if along_x else (sgn * 0.85, 0)
        wmesh("box", mx + ox, mz + oz, (0.36, 0.26, 0.16), y + 0.85, width=(L + 0.4) if along_x else 0.1, height=0.08, depth=0.1 if along_x else (L + 0.4))
        for f in (-0.48, -0.16, 0.16, 0.48):
            px, pz = (mx + f * L, mz + oz) if along_x else (mx + ox, mz + f * L)
            wmesh("box", px, pz, (0.36, 0.26, 0.16), y - 0.05, width=0.12, height=1.0, depth=0.12)
        for f in (-0.4, 0.4):
            px, pz = (mx + f * L, mz + oz * 0.9) if along_x else (mx + ox * 0.9, mz + f * L)
            wmesh("cylinder", px, pz, (0.30, 0.22, 0.14), y - 2.2, width=0.25, height=2.0, depth=0.25, segments=8)

# --- the fire lookout on its knoll, by the north hill
lk_h = 6.0
t.ops.append({"op": "Watchtower", "x": LOOKOUT[0], "z": LOOKOUT[1], "height": lk_h, "color": col((0.40, 0.30, 0.20)), "stairDir": -1})
t.mark(t.occ, (LOOKOUT[0] - 2.2, LOOKOUT[1] - 4.5, LOOKOUT[0] + 2.2, LOOKOUT[1] + 2.2))
t.mark(t.walk, (LOOKOUT[0] - 1.8, LOOKOUT[1] - 1.8, LOOKOUT[0] + 1.8, LOOKOUT[1] + 1.8))
t.mesh("cone", LOOKOUT[0], LOOKOUT[1], (0.30, 0.28, 0.24), y=lk_h + 2.3, width=4.0, height=1.4, depth=4.0, segments=4, yaw=45.0, material="Wood")
t.prop("Fire Danger Sign", LOOKOUT[0] - 3.4, LOOKOUT[1] + 3.0, 200, tag="lookout")

# --- the logging camp (Red): sawmill shed, the log landing, the truck on the road, fuel and junk
sx, sz, sw, sd = SAWMILL
y_sm = 3.6
for (px, pz) in ((sx - sw / 2 + 0.3, sz - sd / 2 + 0.3), (sx + sw / 2 - 0.3, sz - sd / 2 + 0.3), (sx - sw / 2 + 0.3, sz + sd / 2 - 0.3), (sx + sw / 2 - 0.3, sz + sd / 2 - 0.3)):
    t.block(px - 0.2, 0, pz - 0.2, px + 0.2, y_sm, pz + 0.2, (0.36, 0.27, 0.18))
t.block(sx - sw / 2 - 0.4, y_sm, sz - sd / 2 - 0.4, sx + sw / 2 + 0.4, y_sm + 0.2, sz + sd / 2 + 0.4, (0.34, 0.33, 0.32))
t.mesh("wedge", sx, sz - sd / 4 - 0.2, (0.38, 0.36, 0.34), y=y_sm + 0.2, width=sw + 0.8, height=0.9, depth=sd / 2 + 0.4, material="Metal")
t.mesh("wedge", sx, sz + sd / 4 + 0.2, (0.38, 0.36, 0.34), y=y_sm + 0.2, width=sw + 0.8, height=0.9, depth=sd / 2 + 0.4, yaw=180.0, material="Metal")
t.prop("Pine Log Pile", sx - 2.5, sz - 1.8, 0, tag="logging")
t.prop("Conveyor Rollers", sx + 2.0, sz + 1.2, 0, tag="logging")
t.prop("Workbench" if "Workbench" in t.P else "Table", sx + 3.5, sz - 2.6, 0, tag="logging")
t.prop("Plank Pile", sx - 3.0, sz + 2.4, 0, tag="logging")
t.prop("Running Generator", sx + 5.0, sz + 2.6, 0, tag="logging")
for (x, z, yaw) in ((66.0, 30.0, 0), (66.5, 36.0, 0), (75.0, 29.0, 90), (79.0, 36.0, 0), (60.0, 34.0, 90)):
    t.prop("Pine Log Pile", x, z, yaw, tag="logging")
t.prop("Truck Cab", 54.5, -31.0, yaw_along(-16, -10) + 180, tag="logging")
t.prop("Utility Trailer", 59.0, -27.8, yaw_along(-16, -10) + 180, tag="logging")
t.prop("Pickup Truck", 77.0, 22.0, 200, tag="logging")
t.prop("Forklift", 70.0, 38.5, 90, tag="logging")
t.prop("Oil Drum", 63.5, 25.5, 0, tag="logging"); t.prop("Rusty Drum", 64.4, 26.2, 0, tag="logging")
t.prop("Explosive Barrel", 61.5, 15.0, 0, tag="logging"); t.prop("Explosive Barrel", 62.3, 15.7, 0, tag="logging")
t.prop("Jerry Can", 63.0, 14.6, 40, tag="logging")
t.prop("Outhouse", 76.0, -32.0, 0, tag="logging")
t.prop("Floodlight Tower", 64.0, -8.0, 90, tag="logging")
t.prop("Axe Stump", 58.5, 9.5, 0, tag="logging"); t.prop("Firewood Rack", 55.5, 8.8, 0, tag="logging")
t.prop("Trail Sign", 54.0, -2.4, 270, tag="logging")
t.prop("Hanging Lantern", 65.0, -19.5, 0, tag="logging")

# --- campsites: the middle clearing (hill 1), by the creek in the south, and in the north-east woods
def campsite(cx, cz, r, n_tents, seed, fire=True, keep_middle=0.0):
    rr = random.Random(seed)
    if fire:
        t.prop("Campfire Ring", cx, cz, rr.uniform(0, 360), check=False, tag="camp")
    placed = 0
    for k in range(n_tents * 3):
        if placed >= n_tents:
            break
        a = rr.uniform(0, math.tau); d = rr.uniform(r * 0.75, r * 1.05)
        x, z = cx + math.cos(a) * d, cz + math.sin(a) * d
        if t.prop(rr.choice(["Ridge Tent", "Dome Tent", "Ridge Tent"]), x, z, yaw_facing(cx - x, cz - z), lanes=True, tag="camp"):
            placed += 1
    for k in range(6):
        a = rr.uniform(0, math.tau); d = rr.uniform(1.6, max(2.0, r * 0.55))
        x, z = cx + math.cos(a) * d, cz + math.sin(a) * d
        t.prop(rr.choice(["Camp Chair", "Camp Chair", "Camp Cooler", "Sleeping Bag", "Camp Lantern", "Backpack", "Duffel Bag"]), x, z, yaw_facing(cx - x, cz - z), tag="camp")
    for k in range(10):
        a = rr.uniform(0, math.tau); d = rr.uniform(1.0, r * 1.2)
        t.prop(rr.choice(["Soda Cans", "Bottle", "Litter", "Plastic Bag", "Newspaper", "Fallen Branch"]), cx + math.cos(a) * d, cz + math.sin(a) * d, rr.uniform(0, 360), tag="camp")
    return placed


campsite(HILLS[0][0], HILLS[0][1], 8.0, 5, 11)
campsite(-14.0, 26.0, 4.5, 3, 12)
campsite(44.0, -28.0, 4.5, 3, 13)
campsite(-40.0, 44.0, 4.0, 2, 14)
t.prop("Outhouse", 12.0, 10.5, 200, tag="camp")
for (x, z, yaw) in ((-9.0, 9.0, 30), (12.5, -5.5, 120), (-3.0, 52.0, 10), (-11.5, 40.5, 80), (4.0, 50.0, 100)):
    t.prop("Park Picnic Table", x, z, yaw, lanes=True, tag="picnic")
for (x, z) in ((-8.5, 6.0), (11.0, -7.0), (-1.0, 53.5)):
    t.prop("Trash Can", x, z, 0, tag="picnic")
for (x, z, yaw) in ((-6.0, -2.0, 300), (9.0, -40.0, 20), (-14.0, 40.0, 70), (24.0, 3.0, 250), (-24.0, -45.0, 120), (46.0, 30.0, 220)):
    t.prop("Trail Sign", x, z, yaw, tag="trail")

# --- the woodcutter's woodshed (open lean-to with firewood and an axe) next to his cabin
wx, wz = -36.5, 14.0
for (px, pz) in ((wx - 1.9, wz - 1.2), (wx + 1.9, wz - 1.2)):
    t.block(px - 0.12, 0, pz - 0.12, px + 0.12, 2.3, pz + 0.12, (0.36, 0.27, 0.18))
t.block(wx - 2.2, 2.3, wz - 1.6, wx + 2.2, 2.42, wz + 1.6, (0.30, 0.29, 0.27))
t.prop("Firewood Rack", wx - 0.9, wz + 0.6, 0, check=False, tag="woodshed")
t.prop("Firewood Rack", wx + 1.1, wz + 0.6, 0, check=False, tag="woodshed")
t.prop("Axe Stump", wx, wz - 2.6, 20, tag="woodshed")
t.prop("Log", wx + 3.2, wz - 1.5, 80, tag="woodshed"); t.prop("Plank Pile", wx - 3.4, wz - 0.6, 90, tag="woodshed")
t.prop("Wheelbarrow", wx + 2.9, wz + 1.6, 200, tag="woodshed")

ERRATICS = [(-66.5, -8.6, 30, 1.6), (-40.0, 30.0, 110, 1.5), (44.0, 12.0, 200, 1.5), (-20.0, -36.0, 60, 1.4), (28.0, -30.0, 300, 1.5)]
# ================================================================ the forest: hand-placed solid cover, trees, foliage
# fallen logs and boulders in the woods (solid cover, off the trails), stumps and junk
BX0, BZ0, BX1, BZ1 = t.x0, t.z0, t.x1, t.z1


def bunker_zone(x, z, m=1.0):
    for b in BUNKERS:
        X0, Z0, X1, Z1 = b["roof"]
        if X0 - m < x < X1 + m and Z0 - m < z < Z1 + m:
            return True
    return False


def in_lake(x, z, m=0.0):
    return any(math.hypot(x - lx, z - lz) < lr + m for lx, lz, lr in LAKE)


def scatter_solid(names, n, seed, x0=None, x1=None, z0=None, z1=None, margin=2.0):
    rr = random.Random(seed); placed = 0
    x0 = BX0 if x0 is None else x0; x1 = BX1 if x1 is None else x1; z0 = BZ0 if z0 is None else z0; z1 = BZ1 if z1 is None else z1
    for k in range(n * 6):
        if placed >= n:
            break
        x, z = rr.uniform(x0, x1), rr.uniform(z0, z1)
        if bunker_zone(x, z, 2.0) or in_lake(x, z, 3.0) or not t.inside(x, z, margin):
            continue
        if t.prop(rr.choice(names), x, z, rr.uniform(0, 360), lanes=True, gap=0.6, tag="forest"):
            placed += 1
    return placed


# Blue's side (west of x = 0) gets more cover than Red's: Red holds the high ground.
nb = scatter_solid(["Mossy Fallen Log", "Mossy Fallen Log", "Mossy Boulder", "Log", "Tree Stump", "Mossy Boulder"], 150, 21, x1=0.0)
nr = scatter_solid(["Mossy Fallen Log", "Mossy Boulder", "Log", "Tree Stump", "Tree Stump"], 110, 22, x0=0.0)
for (x, z, s_) in ((-46.0, -12.0, 3), (-20.0, 22.0, 4), (30.0, 18.0, 5), (20.0, -28.0, 6), (-52.0, 50.0, 7), (50.0, -8.0, 8), (-2.0, -58.0, 9)):
    t.ops.append({"op": "Rocks", "x": R(x), "z": R(z), "size": 2.4, "color": col((0.48, 0.47, 0.44)), "seed": s_})
    t.mark(t.occ, (x - 2.5, z - 2.5, x + 2.5, z + 2.5)); t.mark(t.walk, (x - 2.0, z - 2.0, x + 2.0, z + 2.0)); t.mark(t.tall, (x - 1.2, z - 1.2, x + 1.2, z + 1.2))

# glacial erratics: big boulders (the Mossy Boulder prop scaled up, over head height) that break the long sight lines
def erratic(x, z, yaw, scale):
    t.ops.append({"op": "Prop", "name": "Mossy Boulder", "x": R(x), "z": R(z), "yaw": R(yaw), "scale": R(scale)})
    t.props.append(("Mossy Boulder", x, z, yaw, 0.0, "erratic")); t.notes["erratic"] = t.notes.get("erratic", 0) + 1
    hw = 1.1 * scale
    t.mark(t.occ, (x - hw, z - hw, x + hw, z + hw)); t.mark(t.walk, (x - hw * 0.8, z - hw * 0.8, x + hw * 0.8, z + hw * 0.8))
    t.mark(t.tall, (x - hw * 0.7, z - hw * 0.7, x + hw * 0.7, z + hw * 0.7))


for (x, z, yaw, sc) in ERRATICS:
    erratic(x, z, yaw, sc)

# --- props on the bunker roofs (hand-placed at the roof height: the ground the game sees there is the bunker floor)
for b in BUNKERS:
    X0, Z0, X1, Z1 = b["roof"]
    top = b["S"] + 0.06
    cab = b["cabin"]
    sh = b["rects"][0]
    rr = random.Random(int(b["S"] * 10) + 7)
    for k in range(40):
        x, z = rr.uniform(X0 + 0.5, X1 - 0.5), rr.uniform(Z0 + 0.5, Z1 - 0.5)
        if cab[0] - 1.6 < x < cab[2] + 1.6 and cab[1] - 1.6 < z < cab[3] + 1.6:
            continue
        wprop(rr.choice(["Leaf Litter", "Fallen Branch", "Fern Clump", "Forest Stones", "Toadstools", "Leaf Litter", "Fern Clump"]), x, z, rr.uniform(0, 360), top, tag="roof")
    for k in range(3):
        x, z = rr.uniform(X0 + 2, X1 - 2), rr.uniform(Z0 + 1.5, Z1 - 1.5)
        if cab[0] - 2.5 < x < cab[2] + 2.5 and cab[1] - 2.5 < z < cab[3] + 2.5:
            continue
        wprop(rr.choice(["Mossy Fallen Log", "Tree Stump"]), x, z, rr.uniform(0, 360), top, tag="roof", mark=True)

# --- trees: hand-placed by the cabins and lake, then scattered by regions (the forest)
TREES_BY_HAND = [(-71, -12, "pine"), (-64, 10, "tree"), (-82, 26, "pine"), (-70, 40, "tree"), (-50, 28, "tree"), (-38, 34, "pine"),
                 (-20, -56, "pine"), (-30, -56, "pine"), (36, -52, "pine"), (46, -52, "pine"), (20, 54, "tree"), (32, 54, "tree"),
                 (66, -30, "pine"), (76, -26, "pine"), (60, 28, "pine"), (-34, 8, "tree"), (-25, 16, "tree"), (8, -52, "pine"), (22, -42, "pine")]
for x, z, kind in TREES_BY_HAND:
    t.tree(x + rng.uniform(-0.8, 0.8), z + rng.uniform(-0.8, 0.8), rng.uniform(8, 13) if kind == "pine" else rng.uniform(7, 10), kind)

# forest regions: mixed pines and broadleaf; thinner in the clearings (hills, camps, lake, cabins: no-go zones below)
t.region("pine", 0.016, rect=[BX0, BZ0, BX1, BZ1], height=[9, 15])
t.region("tree", 0.007, rect=[BX0, BZ0, BX1, BZ1], height=[7, 11])
t.region("pine", 0.012, polygon=[[-94, -60], [0, -66], [0, -36], [-94, -24]], height=[10, 16])     # the dark north-west wood (Blue's cover)
t.region("tree", 0.008, polygon=[[-60, 40], [-10, 30], [-10, 66], [-60, 66]], height=[7, 11])
# the understory: grass and shrubs everywhere, ferns (shrubs) and bushes in the woods, lusher by the water
t.region("grass", 2.6, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.55, scale=[0.8, 1.4], color=[0.36, 0.48, 0.22])
t.region("shrub", 0.55, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.5, color=[0.27, 0.42, 0.18])
t.region("bush", 0.12, rect=[BX0, BZ0, BX1, BZ1], patchiness=0.7, scale=[0.9, 1.6], color=[0.22, 0.36, 0.15])
t.region("bush", 0.14, polygon=[[-94, -60], [0, -66], [0, -36], [-94, -24]], patchiness=0.6, scale=[1.0, 1.7], color=[0.20, 0.34, 0.14])
t.region("bush", 0.10, polygon=[[-60, 20], [-6, 20], [-6, 66], [-60, 66]], patchiness=0.6, scale=[1.0, 1.6])
# the creek banks: long grass and bushes along the bed (Blue's covered flank)
for k in range(len(CREEK) - 1):
    (ax, az), (bx, bz) = CREEK[k], CREEK[k + 1]
    L = math.hypot(bx - ax, bz - az); nx, nz = -(bz - az) / L, (bx - ax) / L
    poly = [[R(ax + nx * 6), R(az + nz * 6)], [R(bx + nx * 6), R(bz + nz * 6)], [R(bx - nx * 6), R(bz - nz * 6)], [R(ax - nx * 6), R(az - nz * 6)]]
    t.region("grass", 4.5, polygon=poly, patchiness=0.4, scale=[1.1, 1.9], color=[0.40, 0.50, 0.24])
    t.region("bush", 0.25, polygon=poly, patchiness=0.5, scale=[1.0, 1.6])
# brush over the hillside bunker door (hides it)
for b in BUNKERS:
    ex, ez = b["trench_end"]
    t.region("bush", 0.9, circle=[R(ex), R(ez), 5.0], scale=[1.2, 1.9], patchiness=0.2)

# no-go: hills, spawns, flags, camps (trees only thinned: the hill zones and camp clearings get no trees), bunker pits
for (x, z, r) in HILLS:
    t.exclude.append({"shape": "circle", "center": [R(x), R(z)], "radius": R(r + 1.0), "density": 0.35})
for (x, z) in BLUE_SP + RED_SP + FFA_SP:
    t.exclude.append({"shape": "circle", "center": [R(x), R(z)], "radius": 1.6, "density": 0.3})
for b in BUNKERS:
    X0, Z0, X1, Z1 = b["roof"]
    t.exclude_rect(X0 - 0.3, Z0 - 0.3, X1 + 0.3, Z1 + 0.3)
    # grass on the roof (over the rooms and the corridor, not over the cabin or the shaft opening)
    cab = b["cabin"]
    for r in b["rects"][1:]:
        t.include.append({"shape": "rect", "center": [R((r[0] + r[2]) / 2), R((r[1] + r[3]) / 2)], "halfX": R((r[2] - r[0]) / 2 + 0.6),
                          "halfZ": R((r[3] - r[1]) / 2 + 0.6), "onTop": True})

# ================================================================ the edge: a forested berm all round; pine scenery beyond it
# (cosmetic mesh pines outside the outline, on the berm and past it, so the woods carry on to the horizon)
def outside_pines():
    rr = random.Random(77); n = 0
    pts = t.outline
    total = sum(math.hypot(pts[(i + 1) % len(pts)][0] - pts[i][0], pts[(i + 1) % len(pts)][1] - pts[i][1]) for i in range(len(pts)))
    s = 0.0; i = 0; acc = 0.0
    step = 2.2
    while s < total:
        # walk the outline
        while True:
            a, b = pts[i % len(pts)], pts[(i + 1) % len(pts)]
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            if acc + L >= s:
                break
            acc += L; i += 1
        f = (s - acc) / max(L, 1e-6)
        x, z = a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f
        ux, uz = (b[0] - a[0]) / max(L, 1e-6), (b[1] - a[1]) / max(L, 1e-6)
        ox, oz = uz, -ux       # outward normal (the outline is CCW in townkit: inward is (-uz, ux))
        for k in range(2):
            d = rr.uniform(3.0, 9.0) + k * rr.uniform(6, 12)
            px, pz = x + ox * d + rr.uniform(-1, 1), z + oz * d + rr.uniform(-1, 1)
            if t.inside(px, pz, -2.0):
                continue
            h = rr.uniform(9, 16)
            gyv = gy(px, pz) + 5.0 * min(1.0, max(0.0, -t.edge(px, pz) / 7.0)) - 0.3
            wmesh("cylinder", px, pz, (0.30, 0.22, 0.15), gyv, width=0.45, height=h * 0.35, depth=0.45, segments=6)
            wmesh("cone", px, pz, (0.16, 0.27, 0.15), gyv + h * 0.22, width=h * 0.36, height=h * 0.8, depth=h * 0.36, segments=7, material="Dirt")
            n += 1
        s += step * rr.uniform(0.8, 1.3)
    return n


N_SCENERY = outside_pines()

# ================================================================ spawns, waypoints (after every solid thing)
for x, z in BLUE_SP + RED_SP + FFA_SP:
    t.ops.append({"op": "Spawn", "x": x, "z": z})
t.ops.append({"op": "AutoWaypoints", "spacing": 6})
for c in CABINS:
    if c["world_y"] is not None:
        continue
    x0, z0, x1, z1 = c["rect"]
    t.ops.append({"op": "Waypoint", "x": R((x0 + x1) / 2), "z": R((z0 + z1) / 2)})
    for (x, z, fx, fz) in c["doors"]:
        t.ops.append({"op": "Waypoint", "x": R(x + fx * 1.6), "z": R(z + fz * 1.6)})
        t.ops.append({"op": "Waypoint", "x": R(x - fx * 1.0), "z": R(z - fz * 1.0)})
for b in BUNKERS:
    ex, ez = b["trench_end"]; dx, dz = b["door"]
    for f in (0.25, 0.5, 0.75, 1.0):
        t.ops.append({"op": "Waypoint", "x": R(dx + (ex - dx) * f), "z": R(dz + (ez - dz) * f)})

# ================================================================ cosmetic clutter (the game's Clutter op runs last and keeps out of walls)
FLOOR = ["Leaf Litter", "Leaf Litter", "Leaf Litter", "Fallen Branch", "Fallen Branch", "Forest Stones", "Toadstools", "Fern Clump", "Fern Clump", "Leaf Litter"]
TRASH = ["Litter", "Soda Cans", "Bottle", "Plastic Bag", "Newspaper"]
tiles = 0
step = 24.0
x = BX0
while x < BX1:
    z = BZ0
    while z < BZ1:
        cx_, cz_ = x + step / 2, z + step / 2
        clear = not any(bunker_zone(xx, zz, 1.5) for xx in (x, x + step / 2, x + step) for zz in (z, z + step / 2, z + step))
        if clear and t.inside(cx_, cz_, -10):
            t.clutter(FLOOR, x, z, x + step, z + step, 95, 300 + tiles)
            tiles += 1
        z += step
    x += step
# bunker-tile gaps: cosmetic floor litter on the surface round each bunker (outside its roof)
for k, b in enumerate(BUNKERS):
    X0, Z0, X1, Z1 = b["roof"]
    for j, r in enumerate(((X0 - 8, Z0 - 8, X1 + 8, Z0 - 1.5), (X0 - 8, Z1 + 1.5, X1 + 8, Z1 + 8), (X0 - 8, Z0, X0 - 1.5, Z1), (X1 + 1.5, Z0, X1 + 8, Z1))):
        t.clutter(FLOOR, r[0], r[1], r[2], r[3], 30, 500 + k * 10 + j)
# litter along the trails, round the camps, the yards
for k, (name, pts, w) in enumerate(TRAILS):
    for i in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[i], pts[i + 1]
        if any(bunker_zone(ax + (bx - ax) * f, az + (bz - az) * f, 1.5) for f in (0, 0.5, 1)):
            continue
        t.clutter(TRASH + ["Fallen Branch", "Forest Stones"], ax, az, bx, bz, 8, 600 + k * 20 + i, width=w + 2.0)
t.clutter(["Litter", "Soda Cans", "Bottle", "Plastic Bag", "Newspaper", "Cardboard Box Small", "Tyre", "Plank"], 52, -36, 88, 40, 160, 700)
t.clutter(["Litter", "Soda Cans", "Newspaper", "Plastic Bag", "Bucket"], -88, -16, -58, 16, 70, 701)
t.clutter(["Plank", "Sawdust" if "Sawdust" in t.P else "Plank", "Cardboard Box Small", "Litter"], SAWMILL[0] - 6, SAWMILL[1] - 4, SAWMILL[0] + 6, SAWMILL[1] + 4, 25, 702)

# ================================================================ the map
foliage = {"seed": 410, "surfaces": {"Wood": 0.0, "Plain": 0.1}, "paths": t.paths, "regions": t.regions, "items": t.items,
           "exclude": t.exclude, "include": t.include}
mp = {
    "format": "call-of-slop-map/1",
    "name": "Timberline",
    "description": "A forest of pines and little log cabins: the ranger station and the lake (Blue) against the logging camp up the hill (Red), with a creek between and two bunkers hidden underground.",
    "author": "Call of Slop",
    "order": 410,
    "maxPlayers": 16,
    "sky": "#8fb4d8",
    "skybox": {"preset": "alpine", "sun": {"time": 10.4, "latitude": 47}, "clouds": {"coverage": 0.35, "speed": 6}},
    "boundary": {"shape": "smooth", "points": [list(p) for p in CTRL], "noise": NOISE, "edge": {"style": "cliff", "height": 5, "width": 7},
                 "groundMargin": 10, "barrierHeight": 3},
    "terrain": TERRAIN,
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": [{"x": x, "z": z, "radius": r} for x, z, r in HILLS],
    "flags": [list(FLAGS[0]), list(FLAGS[1])],
    "ops": t.ops,
    "foliage": foliage,
}


def dump(o, ind=0):
    pad_ = "  " * ind
    if isinstance(o, dict) and ind <= 1 and any(isinstance(v, (dict, list)) for v in o.values()):
        return "{\n" + ",\n".join(f'{pad_}  "{k}": {dump(v, ind + 1)}' for k, v in o.items()) + "\n" + pad_ + "}"
    if isinstance(o, list) and ind <= 2 and o and isinstance(o[0], dict):
        return "[\n" + ",\n".join(pad_ + "  " + json.dumps(v) for v in o) + "\n" + pad_ + "]"
    return json.dumps(o)


with open(OUT_JSON, "w") as f:
    f.write(dump(mp) + "\n")


# ================================================================ report and checks
from collections import Counter
cats = Counter(); solid = 0; cosm = 0
for name, x, z, yaw, y, tag in t.props:
    p_ = t.P[name]; cats[p_["cat"]] += 1
    solid += p_["solid"]; cosm += not p_["solid"]
blocks = sum(1 for o in t.ops if o["op"] == "Block")
print(f"area {t.area():.0f} m2 (side {t.area() ** 0.5:.1f} m), half {HALF_X:.1f} x {HALF_Z:.1f}, blocks {blocks}, hand trees {len(t.items)}, meshes {t.meshes} "
      f"(scenery pines {N_SCENERY}), hand props {len(t.props)} (solid {solid}, cosmetic {cosm}), forest cover blue {nb} red {nr}")
print("props by category:", dict(sorted(cats.items())))
print("props by rule:", dict(sorted(t.notes.items())))
print("clutter ops:", sum(1 for o in t.ops if o["op"] == "Clutter"), "asked:", sum(o["count"] for o in t.ops if o["op"] == "Clutter"))
print(f"terrain {TF.nx} x {TF.nz}, heights {TF.min:.2f}..{TF.max:.2f}")
for b in BUNKERS:
    print(f"{b['name']}: surface {b['S']:.2f}, floor {b['F']:.2f}, roof {tuple(round(v, 1) for v in b['roof'])}, door {tuple(round(v, 1) for v in b['door'])}, "
          f"trench end {tuple(round(v, 1) for v in b['trench_end'])} at ground {gy(*b['trench_end']):.2f}; ground at the room centre {gy((b['rects'][1][0] + b['rects'][1][2]) / 2, (b['rects'][1][1] + b['rects'][1][3]) / 2):.2f}")
for (x, z, r) in HILLS:
    c = gy(x, z); d = [gy(x + math.cos(a) * r * 0.8, z + math.sin(a) * r * 0.8) - c for a in np.linspace(0, math.tau, 9)[:-1]]
    print(f"hill ({x},{z}) ground {c:.2f}, edge spread {min(d):+.2f}..{max(d):+.2f}")

# --- walk field over the terrain (0.5 m grid): walls, solid props, trunks, steep ground and the outside block; the bunker
# tunnels are links between their two entrances (their length through the rooms)
CELL = 0.5
k_ = int(CELL / t.res)
NZ, NX = t.nz // k_, t.nx // k_
blk = t.walk[:NZ * k_, :NX * k_].reshape(NZ, k_, NX, k_).max(axis=(1, 3)).astype(bool)
gxs = t.x0 + (np.arange(NX) + 0.5) * CELL; gzs = t.z0 + (np.arange(NZ) + 0.5) * CELL
GX, GZ = np.meshgrid(gxs, gzs)
steep = ~TF.walkable_at(GX, GZ)
blk |= steep
for i in range(NZ):
    for j in range(NX):
        if not blk[i, j] and t.edge(gxs[j], gzs[i]) < 0.4:
            blk[i, j] = True


def cell_of(x, z):
    return int((z - t.z0) / CELL), int((x - t.x0) / CELL)


TUNNEL = 33.0
portals = {}
for b in BUNKERS:
    a_ = cell_of(*b["entrances"][0]); b_ = cell_of(*b["door"])
    for c_ in (a_, b_):
        blk[c_] = False
    portals.setdefault(a_, []).append((b_, TUNNEL)); portals.setdefault(b_, []).append((a_, TUNNEL))


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
        for (c2, cost) in portals.get((i, j), ()):
            if d + cost < dist[c2]:
                dist[c2] = d + cost; heapq.heappush(h, (d + cost, c2[0], c2[1]))
    return dist


def avg_walk(team_sp, goal):
    f = field([goal])
    vals = [f[cell_of(x, z)] for x, z in team_sp]
    return float(np.mean(vals)), max(vals)


report = []
for i, (x, z, r) in enumerate(HILLS):
    b_, _ = avg_walk(BLUE_SP, (x, z)); r_, _ = avg_walk(RED_SP, (x, z))
    report.append((f"hill {i + 1}", b_, r_))
b_, _ = avg_walk(BLUE_SP, FLAGS[1]); r_, _ = avg_walk(RED_SP, FLAGS[0])
report.append(("enemy flag", b_, r_))
for name, b_, r_ in report:
    print(f"walk {name}: blue {b_:.0f} m, red {r_:.0f} m ({(b_ - r_) / max(b_, r_) * 100:+.1f}%)")

# --- spawn-to-spawn sight over the terrain and the tall things (eye height 1.6 m)
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
print("blue-red spawn lines of sight:", len(vis), vis[:8])
for x, z in BLUE_SP + RED_SP + FFA_SP + [(h[0], h[1]) for h in HILLS] + list(FLAGS):
    if not t.inside(x, z, 1.5):
        print("WARN objective near/outside the edge", x, z)
    if not t.free((x - 0.4, z - 0.4, x + 0.4, z + 0.4)):
        print("WARN objective inside something", x, z)
    if bunker_zone(x, z, 1.0):
        print("WARN objective over a bunker", x, z)

if OUT_PNG:
    from PIL import Image, ImageDraw
    sc = 5
    W = int((t.x1 - t.x0) * sc); H = int((t.z1 - t.z0) * sc)
    xs = t.x0 + (np.arange(W) + 0.5) / sc; zs = t.z0 + (np.arange(H) + 0.5) / sc
    XX, ZZ = np.meshgrid(xs, zs)
    hh = TF.sample(XX, ZZ)
    nrm = TF.normal(XX, ZZ)
    shade = np.clip(0.55 + 0.45 * (nrm[..., 0] * -0.5 + nrm[..., 1] * 0.7 + nrm[..., 2] * -0.5), 0, 1)
    base = np.stack([0.30 + 0.02 * hh, 0.42 + 0.015 * hh, 0.24 + 0.0 * hh], -1) * shade[..., None]
    img = Image.fromarray((np.clip(base, 0, 1) * 255).astype(np.uint8)); d = ImageDraw.Draw(img)
    P = lambda x, z: ((x - t.x0) * sc, (z - t.z0) * sc)
    d.line([P(*p_) for p_ in t.outline[::3]] + [P(*t.outline[0])], fill=(250, 250, 250), width=2)
    for (lx, lz, lr) in LAKE:
        d.ellipse([P(lx - lr, lz - lr), P(lx + lr, lz + lr)], fill=(50, 90, 110))
    for b in BUNKERS:
        X0, Z0, X1, Z1 = b["roof"]
        d.rectangle([P(X0, Z0), P(X1, Z1)], outline=(255, 140, 0), width=2)
        for r in b["rects"]:
            d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=(90, 70, 50))
        d.line([P(*b["door"]), P(*b["trench_end"])], fill=(200, 160, 80), width=8)
    for name, pts, w in TRAILS:
        d.line([P(x, z) for x, z in pts], fill=(150, 120, 80), width=int(w * sc * 0.8))
    for x0, z0, x1, z1, y0, y1, c in t.boxes:
        d.rectangle([P(x0, z0), P(x1, z1)], fill=tuple(int(v * 255) for v in c), outline=(20, 20, 20))
    for name, x, z, yaw, y, tag in t.props:
        if y and y > 0.6 and tag != "cabin":
            continue
        r = t.foot(name, x, z, yaw); p_ = t.P[name]
        if p_["cat"] == "Timberline" and name.startswith(("Log Wall", "Cabin Roof")):
            if name.startswith("Log Wall"):
                d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=(110, 75, 45))
            continue
        fill = (200, 120, 50) if p_["solid"] else (230, 230, 120)
        d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=fill if p_["solid"] else None, outline=fill)
    for it in t.items:
        d.ellipse([P(it["x"] - 1.2, it["z"] - 1.2), P(it["x"] + 1.2, it["z"] + 1.2)], outline=(20, 90, 20), width=2)
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
if os.environ.get("TL_DEBUG"):
    for (x, z, r) in HILLS[:1]:
        f = field([(x, z)])
        for sp in BLUE_SP + RED_SP:
            c_ = cell_of(*sp)
            print("spawn", sp, "walk", f[c_], "blocked", blk[c_], "steep", steep[c_], "ground", round(gy(*sp), 2))
