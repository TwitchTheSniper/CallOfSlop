#!/usr/bin/env python3
"""Generator for Maps/Space/04-TwinShard.json: "Twin Shard", a binary asteroid with two gravity wells (medium, 16 players).

    python3 tools/space/twinshard.py [repo root] [plan.png]

Two 36 m shards of one broken asteroid, each with a pull of its own (a point field 26 m out from its centre), mined by
the invented Oresong Collective: Blue's camp on the west shard, Red's on the east. Two tethers join them
(Maps/README.md, "Gravity zones"):

  - The Line (upper tether, 18 m over the shards' centres): a railed catwalk between the shards' top poles at a third
    of normal gravity, with the Knot in the middle (hill 1, round an open eye: the Drop) and a side platform off it
    (hill 3). Sprint jumps fly 25 m here.
  - The Keel Line (lower tether, 18 m under the centres): its underside is the floor, gravity pulls up: you walk it upside down from
    one shard's bottom pole to the other's (hill 2 under the middle). Its top face is an ordinary floor in plain gravity.
  - The Pebble: a small moonlet between the shards with its own pull (reaching just 0.8 m over its surface). Drop
    onto it through the Knot's eye; jump off its underside and you fall onto the Keel Line's top. Players only (bots
    don't drop).
  - Between the wells is plain gravity down into the void (fallKill 28 m under the shards' centres), so a fall off the Line's middle that
    misses the Pebble and the Keel Line is fatal.

Walk round a shard to its underside and onto the Keel Line, along it upside down, up the far shard to its top and back
over the Line: one loop, three kinds of gravity. Mirror-symmetric about x = 0. Shards are solid icosphere meshes; rocks,
pods, masts and pads are solid meshes stood on the surface by their own rotations (there's one Planet op per map, so
the Surface ops can't build two). Props stand only near the poles (they're drawn upright).
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "04-TwinShard.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import gravity_decals as gdec  # noqa: E402

FALL_KILL = -4.0  # (the lowest a map may put it: everything sits 24 m up so the Keel Line's walkers stay above it)
Y0 = 24.0  # the shards' centre height
SHARD_X = 34.0
SHARD_R = 18.0
SHARD_ZONE = 26.0
TETHER_Y = Y0 + 18.2  # top of the upper tether's deck
KEEL_Y = Y0 - 18.2  # underside of the lower tether's deck (its floor)
PEBBLE_R = 4.0
PEBBLE_ZONE = 4.8  # just over its surface: a jump leaves its pull
LINE_G = 6.0

ROCK = [0.44, 0.40, 0.36]
ROCK_B = [0.40, 0.37, 0.35]
ROCK2 = "#5a524b"
ORE = "#7a6a4a"
STEEL = "#6c6f73"
DARK = "#3a3c40"
BRASS = "#8d7458"
HAB = "#b8b0a0"
PAD = "#8f949a"
WARN = "#e0b030"
CYAN = "#5fc8d0"

OPS = []
STATS = {"blocks": 0, "meshes": 0, "props": 0, "decals": 0, "waypoints": 0, "spawns": 0}
SPAWNS = {"w": [], "e": []}


def r(v, n=3):
    return round(float(v), n)


def op(d):
    OPS.append(d)


def block(x0, y0, z0, x1, y1, z1, color):
    STATS["blocks"] += 1
    op({"op": "Block", "x0": r(min(x0, x1)), "y0": r(min(y0, y1)), "z0": r(min(z0, z1)),
        "x1": r(max(x0, x1)), "y1": r(max(y0, y1)), "z1": r(max(z0, z1)), "color": color})


def mesh(kind, x, z, y, color, **kw):
    STATS["meshes"] += 1
    d = {"op": "Mesh", "mesh": kind, "x": r(x), "z": r(z), "y": r(y), "color": color}
    for k, v in kw.items():
        if v is None:
            continue
        d[k] = r(v) if isinstance(v, float) else v
    op(d)


def prop(name, x, z, y=0.0, yaw=0.0, **kw):
    STATS["props"] += 1
    d = {"op": "Prop", "name": name, "x": r(x), "z": r(z)}
    if y:
        d["y"] = r(y)
    if yaw:
        d["yaw"] = r(yaw % 360, 1)
    d.update(kw)
    op(d)


def decal(name, x, z, y=0.0, onto="floor", **kw):
    STATS["decals"] += 1
    d = {"op": "Decal", "decal": name, "x": r(x), "z": r(z), "y": r(y), "onto": onto}
    d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
    op(d)


def waypoint(x, z, y=0.0):
    STATS["waypoints"] += 1
    op({"op": "Waypoint", "x": r(x), "z": r(z), "y": r(y)})


def spawn(side, x, y, z):
    STATS["spawns"] += 1
    op({"op": "Spawn", "x": r(x), "z": r(z), "y": r(y)})
    SPAWNS[side].append([r(x), r(y), r(z)])


def sign(key, x, z, y, onto, along):
    names, tw, th = gdec.tiles(key)
    n = len(names)
    for i, nm in enumerate(names):
        t = (i - (n - 1) / 2) * tw
        decal(nm, x + along[0] * t, z + along[1] * t, y, onto, size=tw, rotation=0)


# ================================================================================================ standing things on a sphere
def surface_dir(lat, lon):
    la, lo = math.radians(lat), math.radians(lon)
    return (math.cos(la) * math.sin(lo), math.sin(la), math.cos(la) * math.cos(lo))


def stand(kind, cx, cy, cz, R, lat, lon, color, sink=0.3, above=0.0, **kw):
    """A solid mesh standing on the sphere (cx, cy, cz, R) at latitude/longitude, sunk `sink` m, its +Y along the
    surface normal (yaw = atan2(dx, dz), pitch = acos(dy): the game's roll-pitch-yaw order)."""
    d = surface_dir(lat, lon)
    p = (cx + d[0] * (R - sink + above), cy + d[1] * (R - sink + above), cz + d[2] * (R - sink + above))
    yaw = math.degrees(math.atan2(d[0], d[2]))
    pitch = math.degrees(math.acos(max(-1.0, min(1.0, d[1]))))
    mesh(kind, p[0], p[2], p[1], color, yaw=yaw, pitch=pitch, solid=True, **kw)
    return p


def shard(sx, side):
    """One shard at (sx, 0, 0): the ball, rocks, the camp near its top pole, a drill mast on its outer flank, a pad on
    its outer pole, spawns, waypoints."""
    cx, cy, cz = sx, Y0, 0.0
    R = SHARD_R
    out = 1 if sx > 0 else -1  # the outer direction (away from the other shard)
    lon_out = 90 if out > 0 else 270  # longitude facing +X / -X
    lon_in = 270 if out > 0 else 90
    mesh("icosphere", cx, cz, cy - R, ROCK if out < 0 else ROCK_B, width=2 * R, height=2 * R, depth=2 * R, detail=4, material="Dirt", solid=True)
    # Rocks: a seeded scatter clear of the camp (top), the pad (outer pole) and the keel landing (bottom).
    seed = 11 if out < 0 else 23
    rng = _Rng(seed)
    placed = 0
    while placed < 44:
        lat = math.degrees(math.asin(rng.uniform(-0.75, 0.75)))
        lon = rng.uniform(0, 360)
        if lat > 50 or lat < -55:
            continue
        dlon = (lon - lon_out + 180) % 360 - 180
        if abs(dlon) < 30 and abs(lat) < 25:
            continue  # the mast's flank stays clear
        size = rng.uniform(0.9, 2.6)
        stand("icosphere", cx, cy, cz, R, lat, lon, ROCK2 if placed % 3 else ORE, sink=size * 0.3, width=size, height=size * rng.uniform(0.5, 0.9),
              depth=size * rng.uniform(0.7, 1.2), detail=1, material="Dirt" if placed % 3 else "Metal")
        placed += 1
    # The camp round the top pole (lat 70-85): habitat pods, a console, cargo, lights; two spawns inside the ring.
    for k, lon in enumerate((lon_out, lon_out + 60, lon_out - 60)):
        stand("roundedbox", cx, cy, cz, R, 72, lon, HAB, sink=0.4, width=4.0, height=2.6, depth=3.0, radius=0.3, material="Metal")
    stand("cylinder", cx, cy, cz, R, 76, 180, STEEL, sink=0.4, width=1.2, depth=1.2, height=6.0, material="Metal")  # (the camp mast, off the Line)
    stand("box", cx, cy, cz, R, 64, lon_out + 30, BRASS, sink=0.3, width=2.4, height=1.0, depth=0.6, material="Metal")
    stand("box", cx, cy, cz, R, 64, lon_out - 30, BRASS, sink=0.3, width=2.4, height=1.0, depth=0.6, material="Metal")
    # The drill mast on the outer flank at the equator, with ore crates and a cover ring.
    stand("cylinder", cx, cy, cz, R, 0, lon_out, DARK, sink=0.5, width=2.2, depth=2.2, height=12.0, material="Metal")
    stand("box", cx, cy, cz, R, 0, lon_out, STEEL, sink=0.3, above=0.0, width=5.0, height=0.9, depth=5.0, material="Metal")
    for lat, lon in ((12, lon_out + 14), (-12, lon_out - 14), (12, lon_out - 14), (-12, lon_out + 14)):
        stand("box", cx, cy, cz, R, lat, lon, ORE, sink=0.3, width=1.6, height=1.1, depth=1.4, material="Metal")
    # Keel landing: a brass ring round the bottom pole marks where the Keel Line meets the shard.
    for lon in range(0, 360, 60):
        stand("box", cx, cy, cz, R, -70, lon, BRASS, sink=0.2, width=1.6, height=0.4, depth=0.6, material="Metal")
    # Props near the top pole (drawn upright: keep them within 15 degrees of it).
    top = (cx, cy + R, cz)
    # (Clear of the Line's landing over the pole, z within +-2.5.)
    prop("Station Console", cx + out * 1.0, cz + 4.0, cy + R - 0.5, yaw=0 if out < 0 else 180)
    prop("Station Cargo Pod", cx - out * 1.0, cz - 4.2, cy + R - 0.55, yaw=0)
    prop("Gravity Warning Beacon", cx + out * 4.0, cz + 3.0, cy + R - 0.7)
    prop("Deck Light Post", cx - out * 4.0, cz - 3.0, cy + R - 0.7, yaw=180)
    prop("Station Supply Case", cx + out * 4.5, cz - 3.2, cy + R - 0.85, yaw=35)
    prop("Salvage Scrap", cx - out * 3.0, cz + 4.5, cy + R - 0.85)
    # Spawns: eight per shard, on the upper half within 35 degrees of the top (clear of the camp pods and rocks).
    sd = "w" if out < 0 else "e"
    for lat, lon in ((60, lon_in + 30), (60, lon_in - 30), (58, lon_out + 100), (58, lon_out - 100),
                     (48, lon_in + 15), (48, lon_in - 15), (46, lon_out + 140), (46, lon_out - 140)):
        d = surface_dir(lat, lon)
        spawn(sd, cx + d[0] * (R + 0.1), cy + d[1] * (R + 0.1), cz + d[2] * (R + 0.1))
    # Waypoints spread over the shard (the zone pass probes its own grid; these anchor the poles and the flanks).
    for lat, lon in ((85, 0), (-85, 0), (0, lon_in), (30, 0), (30, 180), (-30, 0), (-30, 180), (0, lon_out + 45), (0, lon_out - 45)):
        d = surface_dir(lat, lon)
        waypoint(cx + d[0] * (R + 0.05), cz + d[2] * (R + 0.05), cy + d[1] * (R + 0.05))
    # Decals on the top: the collective's mark, plating seams, stains, a sign on a pod.
    decal("Verrin Salvage Mark", cx, cz, cy + R + 0.2, size=5)
    op({"op": "DecalScatter", "decals": ["Gravity Plating Grid", "Oil Stain", "Rust Stain", "Scorch Mark"], "x0": r(cx - 7), "z0": -7, "x1": r(cx + 7), "z1": 7,
        "count": 24, "seed": seed, "gap": 2.2, "y": r(cy + R - 0.6)})
    op({"op": "DecalScatter", "decals": ["Oil Stain", "Scorch Mark", "Rust Stain", "Skid Marks"], "x0": r(cx - 15), "z0": -15, "x1": r(cx + 15), "z1": 15,
        "count": 30, "seed": seed + 1, "gap": 3.0, "y": r(cy + R - 6)})
    for lon in (lon_out, lon_out + 60, lon_out - 60):
        d = surface_dir(72, lon)
        px, pz = cx + d[0] * (R + 1.0), cz + d[2] * (R + 1.0)
        decal("Vent Grille", px + (0.5 if out > 0 else -0.5), pz, cy + d[1] * (R + 1.0) + 1.0, "wall", size=1.2)


class _Rng:
    """A tiny deterministic generator (so the file never depends on Python's version)."""

    def __init__(self, seed):
        self.s = (seed * 2654435761 + 12345) & 0xFFFFFFFF

    def next(self):
        self.s = (self.s * 1664525 + 1013904223) & 0xFFFFFFFF
        return self.s / 4294967296.0

    def uniform(self, a, b):
        return a + (b - a) * self.next()


# ================================================================================================ the tethers and the pebble
def tethers():
    x0, x1 = -SHARD_X - 1, SHARD_X + 1  # the decks run into the rock past the poles (a landing 0.2 m over each pole)
    # The Line (upper): a 3 m deck, railings (solid bands 0.45-1.1 m up, bullets go under), the Knot and its side platform.
    block(x0, TETHER_Y - 0.4, -1.5, -2, TETHER_Y, 1.5, STEEL)
    block(2, TETHER_Y - 0.4, -1.5, x1, TETHER_Y, 1.5, STEEL)
    for s in (-1, 1):
        for xa in (x0, 6):
            xb = -6 if xa == x0 else x1
            block(xa, TETHER_Y + 0.45, s * 1.5, xb, TETHER_Y + 1.1, s * 1.7, BRASS)
    # The Knot: a 12 m pad with a 4 m eye in its middle (the Drop: fall through it onto the Pebble).
    block(-6, TETHER_Y - 0.4, -6, -2, TETHER_Y, 6, PAD)
    block(2, TETHER_Y - 0.4, -6, 6, TETHER_Y, 6, PAD)
    block(-2, TETHER_Y - 0.4, -6, 2, TETHER_Y, -2, PAD)
    block(-2, TETHER_Y - 0.4, 2, 2, TETHER_Y, 6, PAD)
    block(-3, TETHER_Y - 0.4, 6, 3, TETHER_Y, 16, STEEL)
    block(-6, TETHER_Y - 0.4, 14, 6, TETHER_Y, 20, PAD)
    block(-6, TETHER_Y + 0.45, -6.2, 6, TETHER_Y + 1.1, -6, BRASS)
    block(-6, TETHER_Y + 0.45, 20, 6, TETHER_Y + 1.1, 20.2, BRASS)
    block(-6.2, TETHER_Y + 0.45, 14, -6, TETHER_Y + 1.1, 20, BRASS)
    block(6, TETHER_Y + 0.45, 14, 6.2, TETHER_Y + 1.1, 20, BRASS)
    block(-3.2, TETHER_Y + 0.45, 6, -3, TETHER_Y + 1.1, 14, BRASS)
    block(3, TETHER_Y + 0.45, 6, 3.2, TETHER_Y + 1.1, 14, BRASS)
    for s in (-1, 1):
        block(s * 6, TETHER_Y + 0.45, -6, s * 6.2, TETHER_Y + 1.1, -1.7, BRASS)
        block(s * 6, TETHER_Y + 0.45, 1.7, s * 6.2, TETHER_Y + 1.1, 6, BRASS)
    # Pylons under the Line, cosmetic, and the cable under the Keel Line.
    for x in range(-24, 25, 12):
        mesh("cylinder", x, 0, TETHER_Y - 3.4, DARK, width=0.8, depth=0.8, height=3.0, material="Metal")
    mesh("cylinder", 0, 0, KEEL_Y - 1.0, DARK, width=0.5, depth=0.5, height=2 * SHARD_X - 4, roll=90, segments=12, material="Metal")
    # The Keel Line (lower): its underside is the floor in the inverted zone; its top is a plain floor you can fall onto.
    block(x0, KEEL_Y, -1.5, x1, KEEL_Y + 0.4, 1.5, STEEL)
    block(-6, KEEL_Y, -5, 6, KEEL_Y + 0.4, 5, PAD)
    # Rails on the underside (hanging "down" in world terms: below the floor's level in that frame means y below KEEL_Y).
    for s in (-1, 1):
        block(x0, KEEL_Y - 1.1, s * 1.5, -6, KEEL_Y - 0.45, s * 1.7, BRASS)
        block(6, KEEL_Y - 1.1, s * 1.5, x1, KEEL_Y - 0.45, s * 1.7, BRASS)
        block(-6, KEEL_Y - 1.1, s * 5, 6, KEEL_Y - 0.45, s * 5.2, BRASS)
        block(s * 5, KEEL_Y - 1.1, -5, s * 5.2, KEEL_Y - 0.45, -1.7, BRASS)
        block(s * 5, KEEL_Y - 1.1, 1.7, s * 5.2, KEEL_Y - 0.45, 5, BRASS)
    # The Pebble.
    mesh("icosphere", 0, 0, Y0 - PEBBLE_R, ORE, width=2 * PEBBLE_R, height=2 * PEBBLE_R, depth=2 * PEBBLE_R, detail=3, material="Dirt", solid=True)
    for lat, lon, size in ((40, 30, 1.4), (-20, 150, 1.1), (10, 250, 1.6), (-50, 320, 1.0)):
        stand("icosphere", 0, Y0, 0, PEBBLE_R, lat, lon, ROCK2, sink=size * 0.3, width=size, height=size * 0.6, depth=size * 0.9, detail=1, material="Dirt")
    # Props on the Knot and the side platform; signs at the Line's ends and on the Knot's rails.
    prop("Gravity Plating Node", 0, 4.5, TETHER_Y)
    prop("Station Deck Barrier", -4, 2.5, TETHER_Y, yaw=90)
    prop("Station Deck Barrier", 4, 2.5, TETHER_Y, yaw=90)
    prop("Mooring Clamp", 0, 17.5, TETHER_Y)
    prop("Gravity Warning Beacon", -4.5, 15, TETHER_Y)
    prop("Gravity Warning Beacon", 4.5, 15, TETHER_Y)
    prop("Station Supply Case", 3.5, -4, TETHER_Y, yaw=20)
    prop("Salvage Scrap", -3, -4.5, TETHER_Y)
    for (x, z, rot) in ((0, -2.6, 0), (0, 2.6, 0), (-2.6, 0, 90), (2.6, 0, 90)):
        decal("Hazard Stripes", x, z, TETHER_Y, size=4, rotation=rot)
    decal("Stratos Pad Ring", 0, 17, TETHER_Y, size=5)
    decal("Hazard Stripes", 0, 7, TETHER_Y, size=2.4, rotation=0)
    decal("Hazard Stripes", 0, 13, TETHER_Y, size=2.4, rotation=0)
    for x in (-26, -16, 16, 26):
        decal("Gravity Arrow Cyan", x, 0, TETHER_Y, size=1.6, rotation=90 if x < 0 else 270)
    for x in (-20, 20):
        decal("Gravity Arrow Amber", x, 0, KEEL_Y - 0.1, "ceiling", size=1.6, rotation=90 if x < 0 else 270)
    decal("Stratos Pad Ring", 0, 0, KEEL_Y - 0.1, "ceiling", size=7)
    op({"op": "DecalScatter", "decals": ["Gravity Plating Grid", "Rust Stain"], "x0": r(x0), "z0": -1.4, "x1": r(x1), "z1": 1.4, "count": 30, "seed": 5, "gap": 2.5, "y": r(TETHER_Y)})
    # Waypoints along both tethers (the grid can miss a 3 m catwalk).
    for x in range(-35, 36, 5):
        if x != 0:
            waypoint(x, 0, TETHER_Y)  # (none in the Knot's eye)
    for k in range(-14, 15):
        waypoint(k * 2.5, 0, KEEL_Y - 0.05)  # (zone nodes link up to 4.8 m apart: closer than the Line's)
    for s in (-1, 1):
        waypoint(s * 4, 4, TETHER_Y)
        waypoint(s * 4, -4, TETHER_Y)
    waypoint(0, 10, TETHER_Y)
    waypoint(0, 17, TETHER_Y)
    waypoint(0, 3, KEEL_Y - 0.05)
    waypoint(0, -3, KEEL_Y - 0.05)


# ================================================================================================ zones and the map
def zones():
    z = []
    # (The Line's zone is only as wide as its deck: the shards' tops either side of it keep their own pull.)
    z.append({"shape": "box", "name": "the line", "min": [-SHARD_X + 3, Y0 + 15, -2.5], "max": [SHARD_X - 3, Y0 + 26, 2.5],
              "field": {"type": "uniform", "strength": LINE_G}})
    z.append({"shape": "box", "name": "the knot", "min": [-7, Y0 + 15, -7], "max": [7, Y0 + 26, 21],
              "field": {"type": "uniform", "strength": LINE_G}})
    z.append({"shape": "box", "name": "the keel line", "min": [-SHARD_X + 3, KEEL_Y - 6, -6], "max": [SHARD_X - 3, KEEL_Y, 6],
              "field": {"type": "uniform", "direction": [0, 1, 0]}})
    z.append({"shape": "sphere", "name": "the pebble", "center": [0, Y0, 0], "radius": PEBBLE_ZONE,
              "field": {"type": "point", "center": [0, Y0, 0], "strength": 14}})
    for sx in (-SHARD_X, SHARD_X):
        z.append({"shape": "sphere", "name": "west shard" if sx < 0 else "east shard", "center": [sx, Y0, 0], "radius": SHARD_ZONE,
                  "field": {"type": "point", "center": [sx, Y0, 0], "strength": 18}})
    return {"type": "zones", "default": {"type": "uniform"}, "zones": z}


def boundary_points():
    half = [(0, -30), (14, -28), (30, -32), (50, -30), (60, -16), (62, 0)]
    pts = list(half) + [(x, -z) for (x, z) in reversed(half[:-1])]
    pts = pts + [(-x, z) for (x, z) in reversed(pts[1:-1])]
    return [[x, z] for (x, z) in pts]


def build():
    shard(-SHARD_X, "w")
    shard(SHARD_X, "e")
    tethers()


def main():
    build()
    m = {
        "format": "call-of-slop-map/1",
        "name": "Twin Shard",
        "description": "A binary asteroid: two gravity wells, a low-g tether over the top, an upside-down one under, and a moonlet between.",
        "author": "Call of Slop",
        "order": 604,
        "destructible": False,
        "fallKill": FALL_KILL,
        "maxPlayers": 16,
        "skybox": {"preset": "night", "zenith": "#05060c", "horizon": "#06070e", "ground": "#05060c",
                   "sun": {"direction": [0.2, 0.55, -0.8], "intensity": 1.15, "size": 2.2},
                   "moon": {"direction": [-0.6, -0.2, 0.75], "size": 20, "color": "#c9a27a", "intensity": 0.5},
                   "stars": 1.9, "clouds": False, "ambient": 2.0},
        "boundary": {"shape": "smooth", "points": boundary_points(), "edge": "none", "barrierHeight": 1.0},
        "gravity": zones(),
        "teamSpawns": [SPAWNS["w"], SPAWNS["e"]],
        "hills": [{"x": 0, "z": -4, "y": TETHER_Y, "radius": 3.5},
                  {"x": 0, "z": 0, "y": KEEL_Y, "radius": 4.0},
                  {"x": 0, "z": 17, "y": TETHER_Y, "radius": 3.5}],
        "flags": [[-SHARD_X, TETHER_Y, 0], [SHARD_X, TETHER_Y, 0]],  # (on the Line's landing over each top pole)
        "ops": OPS,
    }
    with open(OUT_JSON, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print(f"wrote {OUT_JSON}: {len(OPS)} ops, {STATS}")
    if OUT_PNG:
        plan(OUT_PNG)


def plan(path):
    from PIL import Image, ImageDraw
    S = 4
    W, H = 130 * S, 70 * S
    img = Image.new("RGB", (W, H), (30, 30, 44))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * S, H / 2 + z * S)
    d.polygon([P(x, z) for x, z in boundary_points()], outline=(120, 120, 160))
    for sx in (-SHARD_X, SHARD_X):
        x, z = P(sx, 0)
        d.ellipse([x - SHARD_R * S, z - SHARD_R * S, x + SHARD_R * S, z + SHARD_R * S], outline=(200, 160, 120), width=2)
    for o in OPS:
        if o["op"] == "Block":
            c = int(120 + min(max(o["y1"], -20), 20) * 3)
            d.rectangle([P(o["x0"], o["z0"]), P(o["x1"], o["z1"])], fill=(c, c, c - 20))
        if o["op"] == "Spawn":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 3, z - 3, x + 3, z + 3], fill=(80, 255, 80) if o["x"] < 0 else (255, 80, 80))
    img.save(path)
    print("plan:", path)


if __name__ == "__main__":
    main()
