#!/usr/bin/env python3
"""Generator for Maps/Space/10-Redvane.json: "Redvane Processor", the Space pack's terraforming atmosphere processor
(medium, 16 players).

    python3 tools/space/redvane.py [repo root] [plan.png]

A plant of the invented Meridian Terraforming Works on a dusty red plain, thickening a thin atmosphere under a hazy
ochre sky. The playable area is an ellipse 136 x 108 m (with a little noise) inside a steel fence. Flat ground, so
everything stands on boxes and meshes; point-symmetric about the centre (every x, z has a -x, -z twin), Blue holds
the west, Red the east:

  - Bunker 1 / Bunker 2 (-+54, +-2): each team's control bunker (a blocky building with roof access), the spawns round
    it and the CTF flag at its door.
  - Intake Fans (-+38, -+17) and (-+38, +-17): four 10 m intake fans (solid, blades turning) standing across each
    team's front, with a 25 m gap between them: the main lane, dressed with containers and machines.
  - The Main Stack (0, 0): a 12 m thick, 34 m tall stack with two square gantries round it: the Gallery (4 m up,
    hill 1) reached by a stepped ramp from each team's side, and the Crown (8 m up) above it by a narrower ramp from
    the Gallery; railed, so you fall only by jumping.
  - Cooling Towers (-+4, -+34): a walk-in square plinth with a doorway on every side (hills 2 and 3 inside) under a
    20 m cooling cylinder, with condensation pools (knee-high rims, water inside) beside each.
  - Pipe Fields (-+19, +-24): a maze of pipe runs at waist height (mantle over them) and head-high pipe racks (walls),
    with lanes marked on the ground: each team's covered flank.

Built for the FPS: pipes are meshes over hidden colliders, the stack and towers are meshes with box cores, dressing
is instanced props and baked decals. No foliage (a dust plain).
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "10-Redvane.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import hollowrock_redvane_decals as sdec  # noqa: E402

RX, RZ = 68.0, 54.0
BUNKER = (-54.0, 0.0)
TOWER = (0.0, -34.0)
RING1_Y, RING1_R, RING1_IN = 4.0, 10.5, 5.6
RING2_Y, RING2_R = 8.0, 8.7
STACK_R = 6.0

GROUND = "#8a5a3c"
DUST = "#9a6a48"
STEEL = "#6c6f73"
STEEL2 = "#85888c"
DARK = "#34363a"
RUST = "#7a5238"
OCHRE = "#b0804a"
HULL = "#c9bda6"
HULL2 = "#a89b84"
CONCRETE = "#8c8275"
AMBER = "#ffb24a"
TEAL = "#6fe0e8"
WATER = "#3f7f8a"
SAFETY = "#e0b030"
DECK = "#7a7268"

OPS = []
STATS = {"boxes": 0, "colliders": 0, "meshes": 0, "props": 0, "decals": 0, "waypoints": 0, "spawns": 0}
SPAWNS = {"b": [], "r": []}


def r(v, n=3):
    return round(float(v), n)


def op(d):
    OPS.append(d)


# ------------------------------------------------------------------ op helpers (each places the thing and, by default, its point-mirror twin)

def block(x0, y0, z0, x1, y1, z1, color, mirror=True):
    STATS["boxes"] += 2 if mirror else 1
    d = {"op": "Block", "x0": r(min(x0, x1)), "y0": r(y0), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "y1": r(y1), "z1": r(max(z0, z1)), "color": color}
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x0": r(-max(x0, x1)), "x1": r(-min(x0, x1)), "z0": r(-max(z0, z1)), "z1": r(-min(z0, z1))})
        op(e)


def collider(x0, y0, z0, x1, y1, z1, mirror=True):
    STATS["colliders"] += 2 if mirror else 1
    d = {"op": "Collider", "x0": r(min(x0, x1)), "y0": r(y0), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "y1": r(y1), "z1": r(max(z0, z1))}
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x0": r(-max(x0, x1)), "x1": r(-min(x0, x1)), "z0": r(-max(z0, z1)), "z1": r(-min(z0, z1))})
        op(e)


def mesh(kind, x, z, y, color, mirror=True, **kw):
    STATS["meshes"] += 2 if mirror else 1
    d = {"op": "Mesh", "mesh": kind, "x": r(x), "z": r(z), "y": r(y), "color": color}
    for k, v in kw.items():
        if v is not None:
            d[k] = r(v) if isinstance(v, float) else v
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x": r(-x), "z": r(-z), "yaw": r((kw.get("yaw", 0) + 180) % 360)})
        op(e)


def prop(name, x, z, yaw=0.0, y=None, mirror=True, **kw):
    STATS["props"] += 2 if mirror else 1
    d = {"op": "Prop", "name": name, "x": r(x), "z": r(z)}
    if y is not None:
        d["y"] = r(y)
    if yaw:
        d["yaw"] = r(yaw % 360, 1)
    d.update(kw)
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x": r(-x), "z": r(-z), "yaw": r((yaw + 180) % 360, 1)})
        op(e)


def decal(name, x, z, y=0.0, onto="floor", mirror=True, **kw):
    STATS["decals"] += 2 if mirror else 1
    d = {"op": "Decal", "decal": name, "x": r(x), "z": r(z), "y": r(y), "onto": onto}
    d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
    op(d)
    if mirror:
        e = dict(d)
        flip = {"wall+x": "wall-x", "wall-x": "wall+x", "wall+z": "wall-z", "wall-z": "wall+z"}
        e.update({"x": r(-x), "z": r(-z), "onto": flip.get(onto, onto)})
        if "rotation" in kw and kw["rotation"] >= 0:
            e["rotation"] = r((kw["rotation"] + 180) % 360)
        op(e)


def scatter(decals, x0, z0, x1, z1, count, seed, mirror=True, **kw):
    STATS["decals"] += count * (2 if mirror else 1)
    d = {"op": "DecalScatter", "decals": decals, "x0": r(min(x0, x1)), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "z1": r(max(z0, z1)),
         "count": count, "seed": seed}
    d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x0": r(-max(x0, x1)), "x1": r(-min(x0, x1)), "z0": r(-max(z0, z1)), "z1": r(-min(z0, z1)), "seed": seed + 1})
        op(e)


def clutter(props, x0, z0, x1, z1, count, seed, **kw):
    d = {"op": "Clutter", "props": props, "x0": r(min(x0, x1)), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "z1": r(max(z0, z1)),
         "count": count, "seed": seed, "mirror": True}
    d.update(kw)
    op(d)


def waypoint(x, z, y=0.0, mirror=True):
    STATS["waypoints"] += 2 if mirror else 1
    op({"op": "Waypoint", "x": r(x), "z": r(z), "y": r(y)})
    if mirror:
        op({"op": "Waypoint", "x": r(-x), "z": r(-z), "y": r(y)})


def spawn(x, z):
    STATS["spawns"] += 2
    op({"op": "Spawn", "x": r(x), "z": r(z)})
    op({"op": "Spawn", "x": r(-x), "z": r(-z)})
    SPAWNS["b"].append([r(x), r(z)])
    SPAWNS["r"].append([r(-x), r(-z)])


def wallx(x0, x1, z, t, y0, y1, color, holes=()):
    """A WallX and its mirror (hole centres are absolute x)."""
    STATS["boxes"] += 2 * (1 + 2 * len(holes))
    op({"op": "WallX", "x0": r(x0), "x1": r(x1), "z": r(z), "t": r(t), "y0": r(y0), "y1": r(y1), "color": color, "holes": list(holes)})
    op({"op": "WallX", "x0": r(-x1), "x1": r(-x0), "z": r(-z - t), "t": r(t), "y0": r(y0), "y1": r(y1), "color": color,
        "holes": [dict(h, center=r(-h["center"])) for h in holes]})


def wallz(z0, z1, x, t, y0, y1, color, holes=()):
    STATS["boxes"] += 2 * (1 + 2 * len(holes))
    op({"op": "WallZ", "z0": r(z0), "z1": r(z1), "x": r(x), "t": r(t), "y0": r(y0), "y1": r(y1), "color": color, "holes": list(holes)})
    op({"op": "WallZ", "z0": r(-z1), "z1": r(-z0), "x": r(-x - t), "t": r(t), "y0": r(y0), "y1": r(y1), "color": color,
        "holes": [dict(h, center=r(-h["center"])) for h in holes]})


def ramp(x, z, width, height, along_x, direction, color, y0=0.0):
    """A stepped Ramp op and its mirror (steps run from (x, z) in `direction`)."""
    STATS["boxes"] += 2 * int(math.ceil(height / 0.5))
    op({"op": "Ramp", "x": r(x), "z": r(z), "width": r(width), "height": r(height), "alongX": along_x, "dir": direction, "color": color, "y0": r(y0)})
    op({"op": "Ramp", "x": r(-x), "z": r(-z), "width": r(width), "height": r(height), "alongX": along_x, "dir": -direction, "color": color, "y0": r(y0)})


def sign(key, x, z, facing, y=1.7, mirror=True):
    """A sign board (a thin block) with the set's tiled sign decal on its face. facing: the way the face looks."""
    names, tw, th = sdec.tiles("Redvane", key)
    n = len(names)
    w = tw * n
    t = 0.14
    if facing in ("+z", "-z"):
        z0 = z if facing == "+z" else z - t
        block(x - w / 2 - 0.15, y - 0.2, z0, x + w / 2 + 0.15, y + th + 0.2, z0 + t, DARK, mirror=mirror)
        block(x - 0.08, 0, z0, x + 0.08, y - 0.2, z0 + t, STEEL, mirror=mirror)
        onto = "wall-z" if facing == "+z" else "wall+z"
        zz = z0 + t + 0.6 if facing == "+z" else z0 - 0.6
        for i, nm in enumerate(names):
            decal(nm, x - w / 2 + tw * (i + 0.5), zz, y + th / 2, onto, mirror=mirror, size=tw, rotation=0, reach=1.5)
    else:
        x0 = x if facing == "+x" else x - t
        block(x0, y - 0.2, z - w / 2 - 0.15, x0 + t, y + th + 0.2, z + w / 2 + 0.15, DARK, mirror=mirror)
        block(x0, 0, z - 0.08, x0 + t, y - 0.2, z + 0.08, STEEL, mirror=mirror)
        onto = "wall-x" if facing == "+x" else "wall+x"
        xx = x0 + t + 0.6 if facing == "+x" else x0 - 0.6
        for i, nm in enumerate(names):
            zi = z + w / 2 - tw * (i + 0.5) if facing == "+x" else z - w / 2 + tw * (i + 0.5)
            decal(nm, xx, zi, y + th / 2, onto, mirror=mirror, size=tw, rotation=0, reach=1.5)


def pipe(x0, z0, x1, z1, radius=0.45, y=0.0, color=STEEL, solid=True, supports=True):
    """A straight pipe run between two points on an axis (along X or Z), lying `y` above the ground, over a hidden collider."""
    along_x = abs(x1 - x0) > abs(z1 - z0)
    length = abs(x1 - x0) if along_x else abs(z1 - z0)
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    cy = y + radius
    if along_x:
        mesh("cylinder", cx, cz, cy, color, width=2 * radius, depth=2 * radius, height=length, segments=12, roll=90, material="Metal")
        if solid:
            collider(cx - length / 2, y, cz - radius, cx + length / 2, y + 2 * radius, cz + radius)
        if supports:
            for k in range(int(length / 4) + 1):
                sx = cx - length / 2 + 1.5 + k * 4
                if sx < cx + length / 2 - 1:
                    block(sx - 0.15, 0, cz - 0.15, sx + 0.15, max(0.05, y + radius * 0.6), cz + 0.15, DARK)
    else:
        mesh("cylinder", cx, cz, cy, color, width=2 * radius, depth=2 * radius, height=length, segments=12, pitch=90, material="Metal")
        if solid:
            collider(cx - radius, y, cz - length / 2, cx + radius, y + 2 * radius, cz + length / 2)
        if supports:
            for k in range(int(length / 4) + 1):
                sz = cz - length / 2 + 1.5 + k * 4
                if sz < cz + length / 2 - 1:
                    block(cx - 0.15, 0, sz - 0.15, cx + 0.15, max(0.05, y + radius * 0.6), sz + 0.15, DARK)


# ------------------------------------------------------------------ the bunkers

def bunkers():
    bx, bz = BUNKER
    op({"op": "Building", "cx": r(bx), "cz": r(bz), "w": 14, "d": 12, "stories": 1, "wall": CONCRETE, "floor": DECK, "doors": "EN", "roofAccess": True})
    op({"op": "Building", "cx": r(-bx), "cz": r(-bz), "w": 14, "d": 12, "stories": 1, "wall": CONCRETE, "floor": DECK, "doors": "WS", "roofAccess": True})
    STATS["boxes"] += 50
    # Roof: a searchlight, sandbags, an antenna, a sensor pole.
    prop("Field Searchlight", bx + 4, bz - 3, 90, y=3.3)
    prop("Sandbag Wall Short", bx + 5.5, bz + 1, 90, y=3.3)
    prop("Antenna Mast", bx - 4, bz + 3, 0, y=3.3)
    prop("Atmos Sensor Pole", bx - 4, bz - 3, 0, y=3.3)
    # Round it: generators, tanks, a hab container row, supply cases, the sign and the logo.
    sign("bunker1", bx + 7.2, bz - 4.2, "+x", y=2.4, mirror=False)
    sign("bunker2", -bx - 7.2, -bz + 4.2, "-x", y=2.4, mirror=False)
    decal("Meridian Logo", bx + 7.1, bz + 4, 2.0, "wall-x", size=1.8, rotation=0)
    decal("Redvane Vapour Warning", bx + 7.1, bz - 1, 1.3, "wall-x", size=0.9, rotation=0)
    prop("Diesel Generator", bx - 2, bz - 10, 0)
    prop("Reagent Sphere", bx + 4, bz + 10.5, 0)
    prop("Hab Container", bx - 10, bz - 9, 90)
    prop("Hab Container", bx - 10, bz + 12, 90)
    prop("Condenser Unit", bx + 11, bz - 8, 0)
    prop("Pipe Valve Stack", bx + 10, bz + 7, 0)
    prop("Supply Case Stack", bx + 9, bz + 2.5, 20)
    prop("Warning Light Mast", bx + 12, bz + 12, 0)
    prop("Station Deck Barrier", bx + 12, bz - 2, 90)
    prop("Station Deck Barrier", bx + 12, bz + 1, 90)
    prop("Sandbag Nest Ring", bx + 15, bz - 11, 0)
    prop("Fuel Drum Rack", bx - 3, bz + 9.5, 0)
    scatter(["Tyre Tracks", "Oil Stain", "Redvane Dust Ring", "Sand Drift", "Footprints", "Rust Stain"], bx - 12, bz - 14, bx + 16, bz + 14, 22, 11, gap=2)
    clutter(["Station Supply Case", "Steel Ammo Can", "Oil Drum", "Gas Cylinders", "Toolbox", "Cable Spool"], bx - 12, bz - 14, bx + 16, bz + 14, 12, 21)
    for x, z in [(-64, -5), (-64, 5), (-59, 11), (-50, 13), (-59, -11), (-50, -13), (-44, -7), (-44, 7), (-46, 20), (-46, -20)]:
        spawn(x, z)


# ------------------------------------------------------------------ the intake fans and the main lane between them

def intakes():
    for n, (x, z) in enumerate([(-38, -17), (-38, 17)]):
        prop("Intake Fan", x, z, 0)
        k = n + 1
        decal(f"Redvane Intake Ring {k}", x, z + (-9 if z < 0 else 9), 0, mirror=False, rotation=0)
        decal(f"Redvane Intake Ring {k + 2}", -x, -z + (9 if z < 0 else -9), 0, mirror=False, rotation=0)
        prop("Warning Light Mast", x - 6.5, z, 0)
        prop("Condenser Unit", x + 1, z + (-3.5 if z < 0 else 3.5), 0)
        prop("Pipe Valve Stack", x + 4.5, z + (-3 if z < 0 else 3), 0)
        scatter(["Redvane Dust Ring", "Redvane Dust Ring", "Sand Drift"], x - 6, z - 8, x + 6, z + 8, 8, 31 + n, gap=2.5)
    sign("intake", -38, -22.5, "+z", y=2.4)
    sign("dust", -38, 22.5, "-z", y=1.5)
    # The main lane between the fans: containers, spheres, a generator, sandbags, barriers.
    prop("Hab Container", -30, -6, 0)
    prop("Hab Container", -24, 7, 90)
    prop("Reagent Sphere", -33, 4, 0)
    prop("Diesel Generator", -20, -8, 0)
    prop("Sandbag Wall Long", -27, 0, 0)
    prop("Sandbag Corner", -16, 2, 0)
    prop("Condenser Unit", -16, -3.5, 90)
    prop("Station Cargo Pod", -21, 11, 30)
    prop("Concrete Tank Trap", -25, -11, 20)
    prop("Concrete Tank Trap", -22, -13, 50)
    decal("Hazard Stripes", -30, -9.5, 0, rotation=0, size=3)
    scatter(["Tyre Tracks", "Redvane Dust Ring", "Sand Drift", "Oil Stain", "Dirt Patch", "Footprints"], -36, -12, -12, 12, 18, 41, gap=2.5)
    clutter(["Oil Drum", "Rusty Drum", "Station Supply Case", "Pallet", "Small Crate", "Cinder Blocks"], -36, -12, -12, 12, 10, 51)


# ------------------------------------------------------------------ the main stack and its gantries

def stack():
    # The stack: a 12 m octagonal drum, a waist band, a flared crown; a box core (a cross and a square) for collision.
    mesh("cylinder", 0, 0, 0, HULL2, width=12, depth=12, height=34, segments=8, yaw=22.5, material="Metal", mirror=False)
    mesh("cylinder", 0, 0, 34, DARK, width=13.5, depth=13.5, height=2.5, segments=8, yaw=22.5, mirror=False)
    mesh("torus", 0, 0, 3.5, RUST, radius=6.1, inner=0.25, mirror=False)
    mesh("torus", 0, 0, 7.5, RUST, radius=6.1, inner=0.25, mirror=False)
    mesh("torus", 0, 0, 20, AMBER, radius=6.15, inner=0.2, mirror=False)
    s = STACK_R * math.cos(math.radians(45))
    collider(-s, 0, -s, s, 36, s, mirror=False)
    collider(-5.55, 0, -2.3, 5.55, 36, 2.3, mirror=False)
    collider(-2.3, 0, -5.55, 2.3, 36, 5.55, mirror=False)
    decal("Redvane Stack Ring", 0, 0, 0, mirror=False, rotation=0, size=16)
    # The Gallery (ring 1): four deck strips round the stack, railed on the outside, a stepped ramp from each side.
    y1 = RING1_Y
    R, I = RING1_R, RING1_IN
    block(-R, y1 - 0.3, -R, R, y1, -I, DECK, mirror=False)
    block(-R, y1 - 0.3, I, R, y1, R, DECK, mirror=False)
    block(-R, y1 - 0.3, -I, -I, y1, I, DECK, mirror=False)
    block(I, y1 - 0.3, -I, R, y1, I, DECK, mirror=False)
    # The Crown (ring 2).
    y2 = RING2_Y
    R2 = RING2_R
    block(-R2, y2 - 0.3, -R2, R2, y2, -I, DECK, mirror=False)
    block(-R2, y2 - 0.3, I, R2, y2, R2, DECK, mirror=False)
    block(-R2, y2 - 0.3, -I, -I, y2, I, DECK, mirror=False)
    block(I, y2 - 0.3, -I, R2, y2, I, DECK, mirror=False)
    # Legs under the gallery's corners and the crown's.
    for (x, z) in [(-R + 0.4, -R + 0.4), (R - 0.4, -R + 0.4), (-R + 0.4, R - 0.4), (R - 0.4, R - 0.4)]:
        block(x - 0.3, 0, z - 0.3, x + 0.3, y1 - 0.3, z + 0.3, DARK, mirror=False)
    for (x, z) in [(-R2 + 0.4, -R2 + 0.4), (R2 - 0.4, -R2 + 0.4), (-R2 + 0.4, R2 - 0.4), (R2 - 0.4, R2 - 0.4)]:
        block(x - 0.25, y1, z - 0.25, x + 0.25, y2 - 0.3, z + 0.25, DARK, mirror=False)

    def rails(Rr, y, gaps):
        """A railing round a square deck of half size Rr at deck height y: posts and rails (meshes), a hidden band."""
        for side in ("n", "s", "w", "e"):
            # Each side runs -Rr..Rr along its axis; gaps are (side, a0, a1) ranges along that axis to leave open.
            segs = [(-Rr, Rr)]
            for gs, a0, a1 in gaps:
                if gs != side:
                    continue
                out = []
                for s0, s1 in segs:
                    if a1 <= s0 or a0 >= s1:
                        out.append((s0, s1))
                    else:
                        if a0 > s0:
                            out.append((s0, a0))
                        if a1 < s1:
                            out.append((a1, s1))
                segs = out
            for s0, s1 in segs:
                L = s1 - s0
                c = (s0 + s1) / 2
                if side in ("n", "s"):
                    z = -Rr + 0.15 if side == "n" else Rr - 0.15
                    collider(s0, y + 0.45, z - 0.1, s1, y + 1.1, z + 0.1, mirror=False)
                    mesh("box", c, z, y + 1.02, STEEL2, width=L, height=0.08, depth=0.08, mirror=False)
                    mesh("box", c, z, y + 0.55, STEEL2, width=L, height=0.05, depth=0.05, mirror=False)
                    for k in range(int(L / 2.2) + 1):
                        px = s0 + 0.2 + k * 2.2
                        if px <= s1 - 0.1:
                            mesh("box", px, z, y, STEEL2, width=0.08, height=1.1, depth=0.08, mirror=False)
                else:
                    x = -Rr + 0.15 if side == "w" else Rr - 0.15
                    collider(x - 0.1, y + 0.45, s0, x + 0.1, y + 1.1, s1, mirror=False)
                    mesh("box", x, c, y + 1.02, STEEL2, width=0.08, height=0.08, depth=L, mirror=False)
                    mesh("box", x, c, y + 0.55, STEEL2, width=0.05, height=0.05, depth=L, mirror=False)
                    for k in range(int(L / 2.2) + 1):
                        pz = s0 + 0.2 + k * 2.2
                        if pz <= s1 - 0.1:
                            mesh("box", x, pz, y, STEEL2, width=0.08, height=1.1, depth=0.08, mirror=False)

    # Ground -> Gallery: 8 m stepped ramps on the west (z 6.5..9.5, from x -18.5) and east (mirror).
    ramp(-18.5, 8, 3, y1, True, 1, DECK)
    # Gallery -> Crown: a narrow ramp along the gallery's west edge (x -10.5..-8.7, z 10.5 -> 2.5) and its mirror.
    ramp(-9.6, 10.5, 1.8, y2 - y1, False, -1, DECK, y0=y1)
    rails(R, y1, [("w", 6.5, 9.5), ("e", -9.5, -6.5), ("w", 2.5, 10.5), ("e", -10.5, -2.5)])
    rails(R2, y2, [("w", 2.5, 4.5), ("e", -4.5, -2.5)])
    # Dressing on the gantries: consoles, cases, lamps, grates; signs on the stack.
    prop("Station Console", 0, -8.5, 180, y=y1, mirror=False)
    prop("Station Console", 0, 8.5, 0, y=y1, mirror=False)
    prop("Supply Case Stack", 8, -8, 0, y=y1)
    prop("Station Supply Case", -8, 2, 20, y=y1)
    prop("Pipe Valve Stack", 7.5, 7, 0, y=y1)
    prop("Caged Bulkhead Lamp", 0, -5.8, 0, y=y1 + 2.4, mirror=False)
    prop("Caged Bulkhead Lamp", 0, 5.8, 0, y=y1 + 2.4, mirror=False)
    prop("Caged Bulkhead Lamp", -5.8, 0, 0, y=y2 + 2.4)
    prop("Station Supply Case", 7, 0, 0, y=y2)
    sign("stack", 0, -6.3, "-z", y=y1 + 1.4, mirror=False)
    sign("stack", 0, 6.3, "+z", y=y1 + 1.4, mirror=False)
    decal("Meridian Logo", -6.4, 0, 2.2, "wall+x", size=2.2, rotation=0)
    decal("Hazard Stripes", -16, 8, 0, rotation=0, size=3)
    for k in range(4):
        decal("Trench Grate", -8 + k * 5.3, -8, y1, rotation=0, mirror=False)
        decal("Trench Grate", -8 + k * 5.3, 8, y1, rotation=0, mirror=False)
    scatter(["Rust Stain", "Oil Drips", "Footprints", "Scorch Mark"], -R, -R, R, R, 10, 61, y=y1, mirror=False, gap=2)
    # Waypoints: the ramps (each step), the gallery's and the crown's strips.
    waypoint(-20, 8)
    for k in range(1, 9):
        waypoint(-18.5 + k - 0.5, 8, k * 0.5)
    for k in range(1, 9):
        waypoint(-9.6, 10.5 - k + 0.5, y1 + k * 0.5)
    for a in (-8, -4, 0, 4, 8):
        waypoint(a, -8, y1, mirror=False)
        waypoint(a, 8, y1, mirror=False)
        waypoint(-8, a, y1, mirror=False)
        waypoint(8, a, y1, mirror=False)
    for a in (-7, -3.5, 0, 3.5, 7):
        waypoint(a, -7.2, y2, mirror=False)
        waypoint(a, 7.2, y2, mirror=False)
        waypoint(-7.2, a, y2, mirror=False)
        waypoint(7.2, a, y2, mirror=False)


# ------------------------------------------------------------------ cooling towers and pools

def towers():
    tx, tz = TOWER
    h = 4.0
    door = {"width": 2.4, "bottom": 0, "top": 2.7}
    wallx(tx - 6, tx + 6, tz - 6, 0.5, 0, h, CONCRETE, [dict(door, center=tx)])
    wallx(tx - 6, tx + 6, tz + 5.5, 0.5, 0, h, CONCRETE, [dict(door, center=tx)])
    wallz(tz - 6, tz + 6, tx - 6, 0.5, 0, h, CONCRETE, [dict(door, center=tz)])
    wallz(tz - 6, tz + 6, tx + 5.5, 0.5, 0, h, CONCRETE, [dict(door, center=tz)])
    op({"op": "Slab", "x0": r(tx - 6.2), "z0": r(tz - 6.2), "x1": r(tx + 6.2), "z1": r(tz + 6.2), "yTop": h + 0.3, "thick": 0.3, "color": CONCRETE})
    op({"op": "Slab", "x0": r(-tx - 6.2), "z0": r(-tz - 6.2), "x1": r(-tx + 6.2), "z1": r(-tz + 6.2), "yTop": h + 0.3, "thick": 0.3, "color": CONCRETE})
    STATS["boxes"] += 2
    # The cooling cylinder: a waisted stack of drums and a wide lip, with a collider so grenades and shots stop.
    mesh("cylinder", tx, tz, h + 0.3, HULL, width=12.5, depth=12.5, height=5, segments=16, material="Concrete")
    mesh("cylinder", tx, tz, h + 5.3, HULL2, width=10.5, depth=10.5, height=9, segments=16, material="Concrete")
    mesh("cylinder", tx, tz, h + 14.3, HULL, width=12, depth=12, height=5, segments=16, material="Concrete")
    mesh("torus", tx, tz, h + 19.3, DARK, radius=6.3, inner=0.5)
    collider(tx - 4.4, h + 0.3, tz - 4.4, tx + 4.4, h + 20, tz + 4.4)
    # Inside: grates, lamps, a valve stack in a corner, the hill in the middle.
    for (dx, dz) in [(-3.5, -3.5), (3.5, -3.5), (-3.5, 3.5), (3.5, 3.5)]:
        prop("Caged Bulkhead Lamp", tx + dx, tz + dz, 0, y=3.2)
    prop("Pipe Valve Stack", tx + 4.2, tz + 4.2, 45)
    prop("Station Supply Case", tx - 4.4, tz - 4.4, 30)
    decal("Round Sewer Grate", tx, tz, 0, size=2.6, rotation=0)
    decal("Redvane Tide Mark", tx, tz, 0, size=8, rotation=0)
    scatter(["Water Stain", "Rust Stain", "Puddle"], tx - 5, tz - 5, tx + 5, tz + 5, 6, 71, gap=1.5)
    decal("Hazard Chevrons", tx, tz - 7.3, 0, rotation=0)
    decal("Hazard Chevrons", tx, tz + 7.3, 0, rotation=180)
    sign("tower", tx - 7.6, tz - 3, "-x", y=2.5)
    for (dx, dz) in [(-7.5, 0), (7.5, 0), (0, -7.5), (0, 7.5), (0, 0)]:
        waypoint(tx + dx, tz + dz)
    # Condensation pools: knee-high rims, water inside, pipes feeding them, tide marks round.
    for (px, pz, w, d) in [(tx - 13, tz + 9, 10, 6), (tx - 11, tz - 9, 8, 6)]:
        block(px - w / 2, 0, pz - d / 2, px + w / 2, 0.55, pz - d / 2 + 0.4, CONCRETE)
        block(px - w / 2, 0, pz + d / 2 - 0.4, px + w / 2, 0.55, pz + d / 2, CONCRETE)
        block(px - w / 2, 0, pz - d / 2, px - w / 2 + 0.4, 0.55, pz + d / 2, CONCRETE)
        block(px + w / 2 - 0.4, 0, pz - d / 2, px + w / 2, 0.55, pz + d / 2, CONCRETE)
        mesh("box", px, pz, 0.3, WATER, width=w - 0.8, height=0.05, depth=d - 0.8)
        decal("Redvane Tide Mark", px, pz, 0, size=max(w, d) + 3, rotation=0)
        decal("Redvane Vapour Warning", px - w / 2 + 1, pz - d / 2 - 0.8, 0, size=1.2, rotation=0)
        prop("Condenser Unit", px, pz + d / 2 + 1.6, 0)
    pipe(tx - 18, tz + 9, tx - 7, tz + 9, radius=0.35, y=0.8, supports=True)
    pipe(tx - 15, tz - 9, tx - 7, tz - 9, radius=0.35, y=0.8, supports=True)
    sign("pools", tx - 19.5, tz, "+x", y=2.2)
    prop("Reagent Sphere", tx + 10, tz - 4, 0)
    prop("Hab Container", tx + 10.5, tz + 5, 0)
    prop("Warning Light Mast", tx + 8, tz + 10, 0)
    prop("Atmos Sensor Pole", tx - 20, tz + 14, 0)
    scatter(["Redvane Dust Ring", "Sand Drift", "Tyre Tracks", "Footprints", "Dirt Patch"], tx - 20, tz - 14, tx + 16, tz + 14, 16, 81, gap=2.5)
    clutter(["Oil Drum", "Station Supply Case", "IBC Tote", "Chemical Drum", "Cinder Blocks"], tx - 20, tz - 14, tx + 16, tz + 14, 10, 91)


# ------------------------------------------------------------------ the pipe fields

def pipe_field():
    cx, cz = -19.0, 24.0   # Blue's flank field (z > 0); Red's is the mirror
    x0, x1 = cx - 12, cx + 12
    sign("pipes", cx, cz - 11.3, "-z", y=2.4)
    # Three long runs along X at waist height with staggered gaps, and two head-high racks (two pipes stacked) as walls.
    rows = [(cz - 8, [(cx - 3, cx)]), (cz, [(cx - 10, cx - 7), (cx + 5, cx + 8)]), (cz + 8, [(cx + 1, cx + 4)])]
    for z, gaps in rows:
        cursor = x0
        for g0, g1 in gaps:
            pipe(cursor, z, g0, z, radius=0.5)
            cursor = g1
        pipe(cursor, z, x1, z, radius=0.5)
    for (xa, za, xb, zb) in [(cx - 6, cz - 7, cx - 6, cz - 1.2), (cx + 7, cz + 1, cx + 7, cz + 7)]:
        pipe(xa, za, xb, zb, radius=0.5)
        pipe(xa, za, xb, zb, radius=0.5, y=1.05, supports=False)
        pipe(xa, za, xb, zb, radius=0.45, y=2.0, supports=False)
    # Cross-links at head height you walk under (cosmetic, no collider), valve stacks at the junctions, a condenser.
    for x in (cx - 9, cx + 2, cx + 10):
        pipe(x, cz - 8.5, x, cz + 8.5, radius=0.3, y=2.4, solid=False, supports=False)
        block(x - 0.12, 0, cz - 8.4, x + 0.12, 2.4, cz - 8.16, DARK)
        block(x - 0.12, 0, cz + 8.16, x + 0.12, 2.4, cz + 8.4, DARK)
    for (x, z, yaw) in [(cx - 3, cz - 4, 0), (cx + 4, cz + 4, 90), (cx - 8, cz + 4, 0)]:
        prop("Pipe Valve Stack", x, z, yaw)
    prop("Condenser Unit", cx + 9, cz - 4, 0)
    prop("Reagent Sphere", cx - 9, cz - 4, 0)
    prop("Hab Container", cx, cz + 12, 0)
    prop("Warning Light Mast", cx + 13, cz + 10, 0)
    prop("Atmos Sensor Pole", cx - 13, cz - 10, 0)
    # Lane markings between the runs, warnings, dust.
    for z in (cz - 4, cz + 4):
        for x in range(int(x0) + 1, int(x1), 2):
            decal("Redvane Pipe Lane", x, z, 0, rotation=90, size=2.2)
    for (x, z) in [(cx - 1.5, cz - 8), (cx + 6.5, cz), (cx + 2.5, cz + 8)]:
        decal("Redvane Vapour Warning", x, z - 1.3, 0, size=1.0, rotation=0)
    scatter(["Rust Stain", "Oil Stain", "Redvane Dust Ring", "Sand Drift", "Water Stain", "Footprints"], x0, cz - 10, x1, cz + 10, 16, 101, gap=2)
    clutter(["Oil Drum", "Station Supply Case", "Gas Cylinders", "Toolbox", "Cable Spool"], x0, cz - 10, x1, cz + 10, 6, 111)
    for z in (cz - 4, cz + 4):
        for x in (cx - 9, cx - 3, cx + 3, cx + 9):
            waypoint(x, z)


# ------------------------------------------------------------------ the rest: the plain between, the fence line, dust

def plain():
    # Between the pipe field, the fans and the stack: scattered cover so no lane is a naked run.
    prop("Hab Container", -8, 16, 20)
    prop("Reagent Sphere", 12, 14, 0)
    prop("Concrete Tank Trap", -2, 15, 30)
    prop("Sandbag Wall Long", 4, 19, 0)
    prop("Station Deck Barrier", -14, -14, 0)
    prop("Station Deck Barrier", -11.5, -14, 0)
    prop("Condenser Unit", -18, -19, 0)
    prop("Hover Cargo Sled", 16, -3, 80)
    prop("Mine Floodlight Mast", -22, 14, 0)
    prop("Mine Floodlight Mast", 10, -24, 0)
    prop("Sandbag Nest Ring", 20, 6, 0)
    prop("Station Cargo Pod", -6, -20, 60)
    # Along the fence: masts, sensor poles, the odd container and a dump of drums.
    for a in (150, 170, 195, 215):
        x, z = RX * 0.9 * math.cos(math.radians(a)), RZ * 0.9 * math.sin(math.radians(a))
        prop("Atmos Sensor Pole" if a % 2 else "Warning Light Mast", x, z, 0)
    prop("Hab Container", -40, -30, 60)
    prop("Hab Container", -34, 36, 110)
    prop("Mine Floodlight Mast", -52, -22, 0)
    prop("Diesel Storage Tank", -40, 34, 20)
    prop("Pipe Stack", -46, 28, 30)
    prop("Pipe Stack", -24, -36, 80)
    clutter(["Oil Drum", "Rusty Drum", "IBC Tote", "Pallet Stack", "Cinder Blocks", "Rubble"], -56, -34, -36, -20, 8, 121)
    clutter(["Oil Drum", "Pallet", "Small Crate", "Rubble", "Cable Spool"], -50, 24, -30, 40, 8, 131)
    # The tank farm (south-west corner): refined-gas tanks in a bund of pipe runs, a pump shed.
    for (x, z) in [(-46, 32), (-41, 36), (-50, 37)]:
        prop("Gas Refinery Tank", x, z, 0)
    prop("Twin Gas Tanks", -44, 26, 0)
    prop("Squat Pressure Tank", -36, 40, 0)
    pipe(-52, 30, -38, 30, radius=0.35, y=0.6)
    prop("Station Pipe Riser", -38, 31.5, 0)
    prop("Pipe Valve Stack", -40, 28, 0)
    op({"op": "Hangar", "cx": -28, "cz": 40, "w": 12, "d": 9, "h": 5, "wall": CONCRETE, "roof": RUST, "doorsOnX": True})
    op({"op": "Hangar", "cx": 28, "cz": -40, "w": 12, "d": 9, "h": 5, "wall": CONCRETE, "roof": RUST, "doorsOnX": True})
    STATS["boxes"] += 12
    prop("Coolant Pump", -28, 40, 0)
    prop("Turbine Generator", -28, 37, 0)
    prop("Caged Bulkhead Lamp", -28, 43, 0, y=4)
    sign("pools", -28, 35.2, "-z", y=3.2)
    # The vehicle yard (north-west corner): a haul truck, an excavator, containers, tyres.
    prop("Mine Dump Truck", -48, -30, 70)
    prop("Tracked Excavator", -38, -38, 200)
    op({"op": "Container", "x": -30, "z": -32, "alongX": True, "color": RUST})
    op({"op": "Container", "x": 30, "z": 32, "alongX": True, "color": RUST})
    op({"op": "Container", "x": -30, "z": -29.2, "alongX": True, "color": OCHRE, "open": True})
    op({"op": "Container", "x": 30, "z": 29.2, "alongX": True, "color": OCHRE, "open": True})
    STATS["boxes"] += 16
    prop("Tyre Stack", -44, -38, 0)
    prop("Tyre Stack", -42.5, -37, 40)
    prop("Fuel Drum Rack", -52, -34, 0)
    prop("Cable Spool", -36, -30, 0)
    prop("Mine Timber Stack", -34, -41, 20)
    # Dust everywhere, tracks on the lanes.
    scatter(["Redvane Dust Ring", "Sand Drift", "Dirt Patch", "Dry Grass"], -60, -48, 0, 48, 40, 141, gap=4)
    scatter(["Tyre Tracks", "Footprints", "Skid Marks"], -50, -20, -10, 20, 10, 151, gap=4)


def build():
    op({"op": "Ground", "color": GROUND})
    bunkers()
    intakes()
    stack()
    towers()
    pipe_field()
    plain()
    op({"op": "AutoWaypoints", "spacing": 6})


def main():
    build()
    bx, bz = BUNKER
    m = {
        "format": "call-of-slop-map/1",
        "name": "Redvane Processor",
        "description": "A terraforming atmosphere processor on a red dust plain: giant intake fans, a pipe maze, cooling towers and gantries round the main stack.",
        "author": "Call of Slop",
        "order": 610,
        "destructible": False,
        "maxPlayers": 16,
        "flagPool": "none",
        "skybox": {"preset": "desert", "zenith": "#9a5a3c", "horizon": "#d49a66", "ground": "#8a5a3c",
                   "sun": {"time": 16.2, "latitude": 20, "color": "#ffd0a0", "intensity": 0.95, "size": 2.2, "glow": 0.6},
                   "clouds": {"coverage": 0.55, "density": 0.5, "color": "#e0b090", "shade": "#a0684a", "speed": 9, "altitude": 1400},
                   "haze": {"color": "#d8905a", "strength": 0.85}, "ambient": 1.2, "fog": {"max": 0.9}},
        "boundary": {"shape": "ellipse", "radiusX": RX, "radiusZ": RZ, "noise": {"amplitude": 1.2, "scale": 16, "seed": 5},
                     "edge": {"style": "fence", "height": 1.8, "spacing": 3, "thickness": 0.12, "color": "#4a4440", "material": "Metal"},
                     "groundMargin": 30, "barrierHeight": 2.5},
        "teamSpawns": [SPAWNS["b"], SPAWNS["r"]],
        "hills": [{"x": 0, "z": 0, "y": RING1_Y, "radius": 9}, {"x": TOWER[0], "z": TOWER[1], "radius": 4.5}, {"x": -TOWER[0], "z": -TOWER[1], "radius": 4.5}],
        "flags": [[bx + 9.5, 0, bz], [-bx - 9.5, 0, -bz]],
        "ops": OPS,
    }
    with open(OUT_JSON, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print(f"wrote {OUT_JSON}: {len(OPS)} ops, {STATS}")
    if OUT_PNG:
        plan(m, OUT_PNG)


def plan(m, path):
    from PIL import Image, ImageDraw
    S = 5
    W, H = 150 * S, 120 * S
    img = Image.new("RGB", (W, H), (90, 55, 40))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * S, H / 2 + z * S)
    d.ellipse([P(-RX, -RZ), P(RX, RZ)], outline=(200, 200, 220), width=2)
    for o in OPS:
        if o["op"] in ("Block", "Collider"):
            d.rectangle([P(o["x0"], o["z0"]), P(o["x1"], o["z1"])], fill=(160, 160, 170) if o["op"] == "Block" else (90, 90, 130))
        if o["op"] == "WallX":
            d.rectangle([P(o["x0"], o["z"]), P(o["x1"], o["z"] + o["t"])], fill=(160, 160, 170))
        if o["op"] == "WallZ":
            d.rectangle([P(o["x"], o["z0"]), P(o["x"] + o["t"], o["z1"])], fill=(160, 160, 170))
        if o["op"] == "Ramp":
            n = int(math.ceil(o["height"] / 0.5))
            if o["alongX"]:
                xa, xb = sorted((o["x"], o["x"] + n * o["dir"]))
                d.rectangle([P(xa, o["z"] - o["width"] / 2), P(xb, o["z"] + o["width"] / 2)], fill=(120, 170, 120))
            else:
                za, zb = sorted((o["z"], o["z"] + n * o["dir"]))
                d.rectangle([P(o["x"] - o["width"] / 2, za), P(o["x"] + o["width"] / 2, zb)], fill=(120, 170, 120))
        if o["op"] == "Mesh" and o["mesh"] == "cylinder" and o.get("pitch", 0) == 0 and o.get("roll", 0) == 0:
            x, z, rr = o["x"], o["z"], o["width"] / 2
            d.ellipse([P(x - rr, z - rr), P(x + rr, z + rr)], outline=(220, 200, 160))
        if o["op"] == "Prop":
            x, z = P(o["x"], o["z"])
            d.rectangle([x - 2, z - 2, x + 2, z + 2], fill=(255, 200, 60))
        if o["op"] == "Waypoint":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 1.5, z - 1.5, x + 1.5, z + 1.5], fill=(80, 200, 255))
        if o["op"] == "Spawn":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 3, z - 3, x + 3, z + 3], fill=(80, 255, 80) if o["x"] < 0 else (255, 80, 80))
        if o["op"] == "Building":
            x, z = P(o["cx"], o["cz"])
            d.rectangle([x - o["w"] / 2 * S, z - o["d"] / 2 * S, x + o["w"] / 2 * S, z + o["d"] / 2 * S], outline=(255, 120, 255), width=2)
    for h in m["hills"]:
        x, z = P(h["x"], h["z"])
        d.ellipse([x - h["radius"] * S, z - h["radius"] * S, x + h["radius"] * S, z + h["radius"] * S], outline=(255, 255, 0), width=2)
    for fx, fy, fz in m["flags"]:
        x, z = P(fx, fz)
        d.rectangle([x - 4, z - 4, x + 4, z + 4], outline=(255, 255, 255), width=2)
    img.save(path)
    print("plan:", path)


if __name__ == "__main__":
    main()
