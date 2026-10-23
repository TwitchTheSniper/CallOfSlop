#!/usr/bin/env python3
"""Generator for Maps/Space/03-KeelhaulYard.json: "Keelhaul Yard", the Space pack's gravity showcase (medium, 16 players).

    python3 tools/space/keelhaul.py [repo root] [plan.png]

An orbital breaker's yard of the invented Verrin Salvage company: the bow section of a derelict hauler (the Keel) is
moored to the yard's deck, with a captured asteroid (the Rock) hanging over the middle on two mooring masts. The yard's
gravity plating is failing in patches, so one map holds four kinds of gravity (Maps/README.md, "Gravity zones"):

  - The Yard (the middle deck, y = 0): plain gravity. Hill 1 under the Rock. Cover: cargo pods, plate stacks, cutter rigs.
  - The Hull (east, x = 30): the hauler's hull plating still pulls along itself, so the whole 72 x 18 m wall is a floor
    you fight on sideways (gravity +X). You get on by the Bowl, a quarter pipe that turns gravity round in three
    30-degree steps, or by jumping anywhere within 10 m of the hull (it grabs you), and get off by walking down past
    its bottom edge, which drops you half a metre onto the deck. Hill 3 is on the hull.
  - The Hold (west, sunk 3 m): a cargo hold at a third of normal gravity: a sprint jump covers 25 m and clears the
    18 m void hatch in its floor (or take the catwalk, hill 2). Ramps down from the yard at both ends.
  - The Rock (over the middle): a 12 m asteroid with a pull of its own, reached by a pier from each team's mooring mast
    (switchback stairs); walk all the way round and under it, and drop off onto a mast or the deck.

Mirror-symmetric about z = 0 (Blue holds the north dock, z < 0; Red the south). Everything hangs over a void
(fallKill): the outline is a smooth boundary with no edge. Props come from Props/Station, Industrial, Facility and the
new Props/Space set (tools/space/gravity_props.py); decals from the pack's Gravity set (tools/space/gravity_decals.py).
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "03-KeelhaulYard.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import gravity_decals as gdec  # noqa: E402

FALL_KILL = -4.0

DECK = "#9a9488"
DECK2 = "#8a857a"
HOLD = "#6e6a62"
HULL = "#7d7568"
HULL2 = "#6a6358"
STEEL = "#6c6f73"
DARK = "#3a3c40"
BRASS = "#8d7458"
WARN = "#e0b030"
ROCK = [0.46, 0.41, 0.36]
ROCK2 = "#5c534c"

# The Bowl (quarter pipe) from the yard deck up onto the hull: centre (PIPE_X, PIPE_Y), radius PIPE_R, z in +-PIPE_HALF.
PIPE_X, PIPE_Y, PIPE_R, PIPE_HALF = 22.0, 8.0, 8.0, 12.0
WALL_X = 30.0  # the hull's face
WALL_TOP = 26.0
WALL_HALF = 36.0
# The Rock.
ROCK_C = (0.0, 9.0, 0.0)
ROCK_R = 6.0
ROCK_ZONE_R = 8.0
# The Hold.
HOLD_Y = -3.0
HOLD_X0, HOLD_X1, HOLD_HALF = -46.0, -16.0, 24.0
HATCH_X0, HATCH_X1, HATCH_HALF = -40.0, -22.0, 7.0
HOLD_G = 7.0

OPS = []
STATS = {"blocks": 0, "meshes": 0, "props": 0, "decals": 0, "waypoints": 0, "spawns": 0}
SPAWNS = {"n": [], "s": []}


def r(v, n=3):
    return round(float(v), n)


def op(d):
    OPS.append(d)


def block(x0, y0, z0, x1, y1, z1, color, material=None):
    STATS["blocks"] += 1
    d = {"op": "Block", "x0": r(min(x0, x1)), "y0": r(min(y0, y1)), "z0": r(min(z0, z1)),
         "x1": r(max(x0, x1)), "y1": r(max(y0, y1)), "z1": r(max(z0, z1)), "color": color}
    op(d)


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


def spawn(side, x, z, y=0.0):
    STATS["spawns"] += 1
    op({"op": "Spawn", "x": r(x), "z": r(z), "y": r(y)})
    SPAWNS[side].append([r(x), r(y), r(z)])


def sign(key, x, z, y, onto, along):
    """A wall sign cut into tiles laid side by side along `along` (a unit x/z direction), centred on (x, z)."""
    names, tw, th = gdec.tiles(key)
    n = len(names)
    for i, nm in enumerate(names):
        t = (i - (n - 1) / 2) * tw
        decal(nm, x + along[0] * t, z + along[1] * t, y, onto, size=tw, rotation=0)


# ================================================================================================ the yard
def yard():
    # The middle deck plate (0.6 m thick), the hull foot strip under the Bowl and beside it.
    block(-16, -0.6, -30, 30, 0, 30, DECK)
    # Deck plating seams everywhere on the yard.
    op({"op": "DecalScatter", "decals": ["Gravity Plating Grid"], "x0": -15, "z0": -29, "x1": 21, "z1": 29, "count": 140, "seed": 31, "gap": 3.2})
    op({"op": "DecalScatter", "decals": ["Oil Stain", "Rust Stain", "Scorch Mark", "Oil Drips"], "x0": -15, "z0": -29, "x1": 21, "z1": 29, "count": 26, "seed": 32, "gap": 4})
    decal("Verrin Salvage Mark", 0, 0, 0, size=7)
    for s in (-1, 1):
        # Cover in each half: plate stacks, cutter rigs, pods and plating nodes, in a loose ring round the middle.
        prop("Hull Plate Stack", -9, 9 * s, yaw=90)
        prop("Hull Plate Stack", 12, 9 * s, yaw=90)
        prop("Salvage Cutter Rig", 6, 20 * s, yaw=180 if s > 0 else 0)
        prop("Station Cargo Pod", -6, 21 * s, yaw=0)
        prop("Station Cargo Pod", -4, 22.6 * s, yaw=0)
        prop("Gravity Plating Node", 16, 22 * s)
        prop("Gravity Plating Node", -12, 2 * s)
        prop("Station Deck Barrier", 4, 6 * s, yaw=0)
        prop("Station Deck Barrier", -4, 6 * s, yaw=0)
        prop("Supply Case Stack", 10, 26 * s, yaw=30)
        prop("Oil Drum", 11.2, 27.4 * s)
        prop("Gravity Warning Beacon", 19, 13.5 * s)
        prop("Gravity Warning Beacon", 19, 29 * s)
        prop("Deck Light Post", -14, 28 * s, yaw=90)
        prop("Deck Light Post", 14, 12 * s, yaw=270)
        prop("Station Pipe Run", -14, 20 * s, yaw=90)
        prop("Station Pipe Riser", -14, 24 * s)
        prop("Salvage Scrap", 8, 13 * s)
        prop("Salvage Scrap", -8, 16 * s)
        # Arrows to the Bowl and the hold, and signs on the deck.
        for k in range(3):
            decal("Gravity Arrow Amber", 14 + k * 2.4, 4 * s, 0, rotation=90, size=1.8)
            decal("Gravity Arrow Cyan", -9 - k * 2.4, 16 * s, 0, rotation=270, size=1.8)
        waypoint(-10, 10 * s)
        waypoint(8, 24 * s)
        waypoint(18, 20 * s)
    waypoint(0, 6)
    waypoint(0, -6)
    waypoint(19, 0)
    decal("Hazard Stripes", 20, 0, 0, size=3, rotation=90)
    decal("Hazard Stripes", 20, 10, 0, size=3, rotation=90)
    decal("Hazard Stripes", 20, -10, 0, size=3, rotation=90)
    # Hill 1 ring on the deck under the Rock.
    decal("Stratos Pad Ring", 0, 0, 0, size=10)


# ================================================================================================ the hull and the bowl
def hull():
    # The hull plate (the wall you fight on), its top lip and end lips (1 m steps in the wall's frame: you can't walk off).
    block(WALL_X, 0, -WALL_HALF - 1, WALL_X + 4, WALL_TOP, WALL_HALF + 1, HULL)
    block(WALL_X - 1, WALL_TOP, -WALL_HALF - 1, WALL_X + 4, WALL_TOP + 1, WALL_HALF + 1, HULL2)
    for s in (-1, 1):
        block(WALL_X - 1, 0, s * WALL_HALF, WALL_X + 4, WALL_TOP + 1, s * (WALL_HALF + 1), HULL2)
    # The hauler's hull behind the plate: big tubes and a bow cone, cosmetic.
    mesh("cylinder", WALL_X + 12, 0, 2, HULL2, width=20, depth=78, height=20, roll=90, segments=28, material="Metal")
    mesh("cylinder", WALL_X + 12, 0, 12, HULL, width=16, depth=74, height=16, roll=90, segments=24, material="Metal")
    mesh("cone", WALL_X + 12, -WALL_HALF - 2, 10, HULL2, width=18, depth=18, height=20, pitch=-90, segments=24, material="Metal")
    for k in range(-3, 4):
        mesh("box", WALL_X + 5, k * 10, 0, DARK, width=2, height=WALL_TOP + 1, depth=1.2, material="Metal")
    # Ribs on the face: cover in the wall's frame (0.8 m proud: a block, not a step). Horizontal pairs and a vertical one per half.
    for s in (-1, 1):
        block(WALL_X - 0.8, 11.5, 8 * s, WALL_X, 12.7, 20 * s, STEEL)
        block(WALL_X - 0.8, 18.0, 8 * s, WALL_X, 19.2, 20 * s, STEEL)
        block(WALL_X - 0.8, 6, 27.4 * s, WALL_X, 22, 28.6 * s, STEEL)
        block(WALL_X - 0.6, 14.5, 23 * s, WALL_X, 15.5, 25 * s, BRASS)
    # Hatches, grilles, scorch and the signs, projected onto the face.
    for (y, z, name) in ((9, -4, "Vent Grille"), (9, 4, "Vent Grille"), (22, -14, "Electrical Panel"), (22, 14, "Electrical Panel"),
                         (15, -30, "Scorch Mark"), (15, 30, "Scorch Mark"), (4, -24, "Rust Bleed"), (4, 24, "Rust Bleed"),
                         (20, 0, "Scorch Mark"), (23, -32, "Grime Streaks"), (23, 32, "Grime Streaks")):
        decal(name, WALL_X - 2, z, y, "wall+x", size=2.2)
    sign("hull", WALL_X - 2, 0, 24.2, "wall+x", (0, 1))
    decal("Verrin Salvage Mark", WALL_X - 2, 0, 15.5, "wall+x", size=5)
    for s in (-1, 1):
        decal("Gravity Arrow Amber", WALL_X - 2, 3 * s, 3, "wall+x", size=1.6, rotation=0)
    # Hill 3 ring on the hull.
    decal("Stratos Pad Ring", WALL_X - 2, 0, 16, "wall+x", size=9)

    # The Bowl: nine solid slabs 10 degrees apart, each tangent to the circle, rolled to its angle.
    thick = 0.5
    chord = 2 * PIPE_R * math.sin(math.radians(5)) + 0.06
    for i in range(9):
        deg = 5 + 10 * i
        sn, cs = math.sin(math.radians(deg)), math.cos(math.radians(deg))
        px = PIPE_X + PIPE_R * sn
        py = PIPE_Y - PIPE_R * cs
        mesh("box", px + thick * sn, 0, py - thick * cs, DECK2 if i % 2 else DECK, width=chord, height=thick, depth=2 * PIPE_HALF,
             roll=deg, material="Metal", solid=True)
    # Cheeks closing the bowl's sides, and the void under the slabs filled so nothing stands in there.
    for s in (-1, 1):
        block(PIPE_X, 0, s * PIPE_HALF, WALL_X, PIPE_Y, s * (PIPE_HALF + 1), HULL2)
    block(PIPE_X + 6, 0, -PIPE_HALF, WALL_X, 2.3, PIPE_HALF, HULL2)
    block(PIPE_X + 7.3, 0, -PIPE_HALF, WALL_X, 4.6, PIPE_HALF, HULL2)
    # Warning signs either side of the bowl's foot.
    for s in (-1, 1):
        sign("fault", PIPE_X - 0.6, s * (PIPE_HALF + 0.5), 1.4, "wall+z" if s < 0 else "wall-z", (1, 0))
    for s in (-1, 1):
        waypoint(WALL_X - 0.05, 14 * s, 15)
        waypoint(WALL_X - 0.05, 30 * s, 8)
        # Half a metre from the hull's foot: the step up onto the wall (its zone starts 0.5 m up) is walkable from here.
        for z in (14, 18, 22, 26):
            waypoint(WALL_X - 0.5, z * s, 0)
    waypoint(WALL_X - 0.05, 0, 22)


# ================================================================================================ the hold
def hold():
    y0 = HOLD_Y
    # Floor round the void hatch: four plates.
    block(HOLD_X0, y0 - 0.6, -HOLD_HALF, HATCH_X0, y0, HOLD_HALF, HOLD)
    block(HATCH_X1, y0 - 0.6, -HOLD_HALF, HOLD_X1, y0, HOLD_HALF, HOLD)
    block(HATCH_X0, y0 - 0.6, -HOLD_HALF, HATCH_X1, y0, -HATCH_HALF, HOLD)
    block(HATCH_X0, y0 - 0.6, HATCH_HALF, HATCH_X1, y0, HOLD_HALF, HOLD)
    # The catwalk over the hatch (hill 2), with railings (a solid band 0.45-1.1 m up).
    block(HATCH_X0, y0 - 0.3, -1.2, HATCH_X1, y0, 1.2, STEEL)
    for s in (-1, 1):
        block(HATCH_X0, y0 + 0.45, s * 1.2, HATCH_X1, y0 + 1.1, s * 1.4, BRASS)
    # Walls: west and the two ends, 13 m tall, with a lip of hull above.
    block(HOLD_X0 - 1, y0 - 0.6, -HOLD_HALF - 1, HOLD_X0, 9, HOLD_HALF + 1, HULL)
    for s in (-1, 1):
        block(HOLD_X0, y0 - 0.6, s * HOLD_HALF, HOLD_X1, 9, s * (HOLD_HALF + 1), HULL)
    # The yard's edge above the hold: a lip so you see the drop, open elsewhere (jump down 3 m, come back up the ramps).
    for s in (-1, 1):
        block(HOLD_X1, 0, s * 4, HOLD_X1 + 0.4, 1.0, s * 13.5, BRASS)
    # Ramps down from the yard at both ends.
    for s in (-1, 1):
        op({"op": "Ramp", "x": r(HOLD_X1 - 6), "z": r(18 * s), "width": 4, "height": 3, "alongX": True, "dir": 1, "color": HOLD, "y0": r(y0)})
    # Container stacks you can jump onto only in low gravity (2.1 m), and pods, cases and rigs.
    for s in (-1, 1):
        block(-36, y0, 12 * s, -31, y0 + 2.1, 15 * s, "#7a6d5c")
        block(-27, y0, 18 * s, -22, y0 + 2.1, 21 * s, "#6b7a7a")
        prop("Station Cargo Pod", -43, 10 * s, y0, yaw=90)
        prop("Station Cargo Pod", -43, 13 * s, y0, yaw=90)
        prop("Supply Case Stack", -20, 10 * s, y0, yaw=20)
        prop("Hover Cargo Sled", -34, 20 * s, y0, yaw=0)
        prop("Gravity Plating Node", -44, 20 * s, y0)
        prop("Salvage Cutter Rig", -19, 22 * s, y0, yaw=90)
        prop("Oil Drum", -30, 10 * s, y0)
        prop("Oil Drum", -30.7, 10.4 * s, y0)
        prop("Gravity Warning Beacon", -20, 8.5 * s, y0)
        prop("Deck Light Post", -44, 2 * s, y0, yaw=90)
        prop("Salvage Scrap", -38, 18 * s, y0)
        prop("Station Supply Case", -24, 2.5 * s, y0, yaw=40)
        waypoint(-43, 16 * s, y0)
        waypoint(-19, 13 * s, y0)
        waypoint(-33, 8 * s, y0)
    waypoint(-31, 0, y0)
    waypoint(-43, 0, y0)
    waypoint(-19, 0, y0)
    # Hazard stripes round the hatch, panels, signs on the walls.
    for x in range(int(HATCH_X0) + 1, int(HATCH_X1), 3):
        decal("Hazard Stripes", x + 0.5, -HATCH_HALF - 1, y0, size=2.4, rotation=0)
        decal("Hazard Stripes", x + 0.5, HATCH_HALF + 1, y0, size=2.4, rotation=0)
    for z in range(-6, 7, 3):
        decal("Hazard Stripes", HATCH_X0 - 1, z, y0, size=2.4, rotation=90)
        decal("Hazard Stripes", HATCH_X1 + 1, z, y0, size=2.4, rotation=90)
    op({"op": "DecalScatter", "decals": ["Gravity Plating Grid"], "x0": HOLD_X0 + 1, "z0": -HOLD_HALF + 1, "x1": HOLD_X1 - 1, "z1": HOLD_HALF - 1,
        "count": 90, "seed": 41, "gap": 3.2, "y": r(y0)})
    op({"op": "DecalScatter", "decals": ["Oil Stain", "Rust Stain", "Skid Marks"], "x0": HOLD_X0 + 1, "z0": -HOLD_HALF + 1, "x1": HOLD_X1 - 1, "z1": HOLD_HALF - 1,
        "count": 18, "seed": 42, "gap": 4, "y": r(y0)})
    sign("lowg", HOLD_X0 + 2, 0, y0 + 3.2, "wall-x", (0, 1))
    sign("hold", HOLD_X0 + 2, 16, y0 + 3.2, "wall-x", (0, 1))
    sign("hold", HOLD_X0 + 2, -16, y0 + 3.2, "wall-x", (0, 1))
    for s in (-1, 1):
        sign("fault", -31, s * (HOLD_HALF - 2), y0 + 2.4, "wall+z" if s > 0 else "wall-z", (1, 0))
        decal("Electrical Panel", HOLD_X0 + 2, 8 * s, y0 + 1.6, "wall-x", size=1.8)
        decal("Grime Streaks", HOLD_X0 + 2, 20 * s, y0 + 5, "wall-x", size=3)
    # Hill 2 ring.
    decal("Stratos Pad Ring", -31, 0, y0, size=5)


# ================================================================================================ the rock and the masts
def rock():
    cx, cy, cz = ROCK_C
    op({"op": "Planet", "radius": ROCK_R, "x": cx, "y": cy, "z": cz, "color": ROCK, "detail": 4, "material": "Dirt"})
    # Dressing by latitude and longitude (the Surface ops stand things on the rock whatever the map's gravity).
    op({"op": "Crater", "lat": -90, "lon": 0, "radius": 2.2, "color": ROCK2, "rim": 0.7, "count": 8, "seed": 3})
    op({"op": "SurfaceScatter", "mesh": "icosphere", "count": 26, "color": ROCK2, "minSize": 0.5, "maxSize": 1.1, "seed": 7, "latMin": -70, "latMax": 60, "clear": 2})
    for lon in (90, 270):
        op({"op": "SurfaceBlock", "lat": 0, "lon": lon, "w": 1.6, "h": 1.0, "d": 0.5, "color": BRASS, "heading": 90, "material": "Metal"})
    op({"op": "SurfaceMesh", "mesh": "cylinder", "lat": -90, "lon": 0, "color": WARN, "width": 0.3, "depth": 0.3, "height": 1.6, "solid": False})
    for lon in (0, 180):
        op({"op": "SurfaceWaypoint", "lat": 0, "lon": lon})
    op({"op": "SurfaceWaypoint", "lat": -60, "lon": 90})
    op({"op": "SurfaceWaypoint", "lat": -60, "lon": 270})
    op({"op": "SurfaceWaypoint", "lat": 60, "lon": 90})
    op({"op": "SurfaceWaypoint", "lat": 60, "lon": 270})


def stairs(xa, xb, z0, z1, y0, height, color):
    """A solid flight from x = xa to x = xb (either way) between z0 and z1: 0.5 m rises on 0.9 m treads."""
    n = int(round(height / 0.5))
    run = (xb - xa) / n
    for k in range(1, n + 1):
        block(xa + (k - 1) * run, y0, z0, xa + k * run, y0 + k * height / n, z1, color)


def mast(s):
    """One mooring mast (s = +1 south, -1 north): a column at z = 13..16, switchback stairs beside it, a top walkway and
    the pier onto the Rock's top. The stairs are in plain gravity (the rock's zone ends 8 m from its centre)."""
    def Z(z):
        return s * z
    top = 15.0
    # Column and its base.
    block(-1.5, 0, Z(13), 1.5, top, Z(16), STEEL)
    block(-2.0, 0, Z(12.6), 2.0, 0.5, Z(16.4), DARK)
    # Flight A: along +X beside the column, up to a landing on a pillar; flight B back along -X beside it. Wide treads
    # (0.9 m runs, 0.5 m rises) so the bots' graph gets nodes on every flight.
    rise = 7.5
    x0, x1 = -14.0, -0.5
    stairs(x0, x1, Z(16), Z(17.6), 0, rise, HULL)
    block(x1, 0, Z(16), 2.2, rise, Z(19.2), HULL2)
    stairs(x1, x0, Z(17.6), Z(19.2), rise, rise, HULL)
    # Top walkway: along the stairs' far side, then across to the column.
    block(x0 - 1.6, top - 0.3, Z(19.2), 2.2, top, Z(20.8), STEEL)
    block(1.5, top - 0.3, Z(13), 3.1, top, Z(20.8), STEEL)
    block(x0 - 1.6, top - 0.3, Z(17.6), x0, top, Z(19.2), STEEL)
    # Rails on the walkway's outer edges.
    block(x0 - 1.6, top, Z(20.8), 3.1, top + 1.0, Z(21.0), BRASS)
    block(x0 - 1.6, top, Z(17.6), x0 - 1.4, top + 1.0, Z(21.0), BRASS)
    block(x0 - 1.6, top, Z(17.4), x0, top + 1.0, Z(17.6), BRASS)
    block(3.1, top, Z(13), 3.3, top + 1.0, Z(21.0), BRASS)
    # The pier to the Rock's top (its end 1.6 m short of the pole: a 0.4 m step down onto the rock).
    block(-1.5, top - 0.3, Z(1.6), 1.5, top, Z(13), STEEL)
    for z in (3, 6, 9, 12):
        block(-1.7, top, Z(z), -1.5, top + 0.9, Z(z + 0.2), BRASS)
        block(1.5, top, Z(z), 1.7, top + 0.9, Z(z + 0.2), BRASS)
    # Mast dressing and signs.
    prop("Mooring Clamp", 4.5, Z(14.5), 0, yaw=90)
    prop("Station Console", -4, Z(13.5), 0, yaw=180 if s > 0 else 0)
    prop("Station Pipe Riser", -3, Z(13.2), 0)
    sign("mast", 0, Z(12.9), 2.2, "wall+z" if s > 0 else "wall-z", (1, 0))
    decal("Gravity Arrow Amber", 0, Z(11.5), top, "floor", size=1.6, rotation=180 if s > 0 else 0)
    decal("Gravity Arrow Amber", 0, Z(5), top, "floor", size=1.6, rotation=180 if s > 0 else 0)
    sign("rock", 0, Z(12.9), top - 1.2, "wall+z" if s > 0 else "wall-z", (1, 0))
    # Waypoints along the stairs and the pier (the grid misses narrow walkways).
    for k in (2, 6, 10, 14):
        waypoint(x0 + (k - 0.5) * 0.9, Z(16.8), k * 0.5)
        waypoint(x1 - (k - 0.5) * 0.9, Z(18.4), rise + k * 0.5)
    waypoint(0.9, Z(17.6), rise)
    waypoint(-14.8, Z(18.4), top)
    waypoint(-9.5, Z(20), top)
    waypoint(-3, Z(20), top)
    waypoint(2.3, Z(17), top)
    waypoint(2.3, Z(14), top)
    waypoint(0, Z(14.5), top)
    waypoint(0, Z(9), top)
    waypoint(0, Z(4), top)


# ================================================================================================ the docks
def dock(s):
    def Z(z):
        return s * z
    sd = "s" if s > 0 else "n"
    block(-16, -0.6, Z(30), 16, 0, Z(46), DECK2)
    # The dock office: a U of walls with two door gaps at the front, a roof slab.
    block(-10, 0, Z(45), 10, 4, Z(46), HULL)
    block(-10, 0, Z(38), -9, 4, Z(46), HULL)
    block(9, 0, Z(38), 10, 4, Z(46), HULL)
    for (x0, x1) in ((-10, -6), (-2, 2), (6, 10)):
        block(x0, 0, Z(38), x1, 4, Z(39), HULL)
    block(-10, 3.6, Z(38), 10, 4, Z(46), HULL2)
    # Inside: consoles and cabinets; outside: tanks, pods, barriers, lights.
    prop("Station Console", -7, Z(44), 0, yaw=0 if s < 0 else 180)
    prop("Station Console", 7, Z(44), 0, yaw=0 if s < 0 else 180)
    prop("Station Server Cabinet", 0, Z(45.2), 0, yaw=0 if s < 0 else 180)
    prop("Station Supply Case", 4, Z(41), 0, yaw=30)
    prop("Twin Gas Tanks", -13, Z(33), 0, yaw=90)
    prop("Squat Pressure Tank", 13, Z(34), 0)
    prop("Station Cargo Pod", 13.5, Z(40), 0, yaw=90)
    prop("Station Cargo Pod", 13.5, Z(43), 0, yaw=90)
    prop("Station Deck Barrier", -5, Z(34), 0)
    prop("Station Deck Barrier", 5, Z(34), 0)
    prop("Deck Light Post", -15, Z(45), 0, yaw=90)
    prop("Deck Light Post", 15, Z(45), 0, yaw=270)
    prop("Landing Beacon", -15, Z(31), 0)
    prop("Landing Beacon", 15, Z(31), 0)
    prop("Hull Plate Stack", -12, Z(39), 0, yaw=0)
    prop("Gravity Plating Node", 0, Z(33), 0)
    prop("Salvage Scrap", -8, Z(31.5), 0)
    # Spawns: four inside the office, four outside.
    for (x, z) in ((-7, 42), (-3, 43), (3, 43), (7, 42), (-11, 36), (11, 36), (-8, 31.5), (8, 31.5)):
        spawn(sd, x, Z(z))
    # Decals: the yard mark at the door, plating seams, a "hull plating live" reminder and the team colour band.
    decal("Verrin Salvage Mark", 0, Z(36), 0, size=4)
    op({"op": "DecalScatter", "decals": ["Gravity Plating Grid"], "x0": -15, "z0": r(Z(31)), "x1": 15, "z1": r(Z(37)), "count": 30, "seed": 51 + s, "gap": 3.2})
    op({"op": "DecalScatter", "decals": ["Oil Stain", "Rust Stain", "Paper Litter"], "x0": -15, "z0": r(Z(31)), "x1": 15, "z1": r(Z(45)), "count": 10, "seed": 53 + s, "gap": 4})
    sign("fault", 0, Z(37.95), 3.0, "wall+z" if s > 0 else "wall-z", (1, 0))
    decal("Stencil Arrow", -4, Z(37.95), 1.6, "wall+z" if s > 0 else "wall-z", size=1.4)
    decal("Stencil Arrow", 4, Z(37.95), 1.6, "wall+z" if s > 0 else "wall-z", size=1.4)
    decal("Electrical Panel", -8.95, Z(42), 1.5, "wall-x", size=1.6)
    waypoint(0, Z(42))
    waypoint(-12, Z(36))
    waypoint(12, Z(36))
    waypoint(0, Z(35.5))


# ================================================================================================ zones and the map
def zones():
    xa = PIPE_X + PIPE_R * math.sin(math.radians(15))
    xb = PIPE_X + PIPE_R * math.sin(math.radians(45))
    xc = PIPE_X + PIPE_R * math.sin(math.radians(75))
    z = []
    # The hull's pull above the bowl and beside it (from half a metre up: the bottom edge drops you onto the deck).
    z.append({"shape": "box", "name": "hull", "min": [20, r(PIPE_Y + 0.3), -PIPE_HALF], "max": [WALL_X + 0.5, WALL_TOP + 0.5, PIPE_HALF],
              "field": {"type": "uniform", "direction": [1, 0, 0]}})
    for s in (-1, 1):
        z.append({"shape": "box", "name": "hull side", "min": [20, 0.5, min(s * PIPE_HALF, s * WALL_HALF)], "max": [WALL_X + 0.5, WALL_TOP + 0.5, max(s * PIPE_HALF, s * WALL_HALF)],
                  "field": {"type": "uniform", "direction": [1, 0, 0]}})
    # The bowl: three 30-degree steps.
    z.append({"shape": "box", "name": "bowl 30", "min": [r(xa), -1, -PIPE_HALF], "max": [r(xb), r(PIPE_Y + 0.3), PIPE_HALF],
              "field": {"type": "uniform", "direction": [0.5, -0.8660254, 0]}})
    z.append({"shape": "box", "name": "bowl 60", "min": [r(xb), -1, -PIPE_HALF], "max": [r(xc), r(PIPE_Y + 0.3), PIPE_HALF],
              "field": {"type": "uniform", "direction": [0.8660254, -0.5, 0]}})
    z.append({"shape": "box", "name": "bowl foot", "min": [r(xc), -1, -PIPE_HALF], "max": [WALL_X + 0.5, r(PIPE_Y + 0.3), PIPE_HALF],
              "field": {"type": "uniform", "direction": [1, 0, 0]}})
    # The piers stay level up to the step onto the rock.
    for s in (-1, 1):
        z.append({"shape": "box", "name": "pier", "min": [-3, 12, min(s * 1.6, s * 9)], "max": [3, 19, max(s * 1.6, s * 9)],
                  "field": {"type": "uniform"}})
    # The rock.
    z.append({"shape": "sphere", "name": "rock", "center": list(ROCK_C), "radius": ROCK_ZONE_R,
              "field": {"type": "point", "center": list(ROCK_C), "strength": 18}})
    # The hold: a third of normal gravity.
    z.append({"shape": "box", "name": "hold", "min": [HOLD_X0 - 2, HOLD_Y - 2, -HOLD_HALF - 2], "max": [HOLD_X1, 30, HOLD_HALF + 2],
              "field": {"type": "uniform", "strength": HOLD_G}})
    return {"type": "zones", "default": {"type": "uniform"}, "zones": z}


def boundary_points():
    half = [(-52, 0), (-52, -28), (-36, -34), (-20, -40), (-20, -50), (0, -52), (20, -50), (20, -34), (34, -42), (42, -36), (42, 0)]
    pts = list(half)
    pts += [(x, -z) for (x, z) in reversed(half[1:-1])]
    return [[x, z] for (x, z) in pts]


def build():
    yard()
    hull()
    hold()
    rock()
    mast(-1)
    mast(1)
    dock(-1)
    dock(1)


def main():
    build()
    m = {
        "format": "call-of-slop-map/1",
        "name": "Keelhaul Yard",
        "description": "A salvage yard with failing gravity plating: fight on a hull wall, long-jump a low-g hold, walk round a captured rock.",
        "author": "Call of Slop",
        "order": 603,
        "destructible": False,
        "fallKill": FALL_KILL,
        "maxPlayers": 16,
        "skybox": {"preset": "night", "zenith": "#05060c", "horizon": "#07080f", "ground": "#05060c",
                   "sun": {"direction": [-0.7, 0.35, 0.6], "intensity": 1.1, "size": 2.0},
                   "moon": {"direction": [0.5, 0.3, -0.8], "size": 24, "color": "#b8a07c", "intensity": 0.4},
                   "stars": 1.8, "clouds": False, "ambient": 1.9},
        "boundary": {"shape": "smooth", "points": boundary_points(), "edge": "none", "barrierHeight": 1.0},
        "gravity": zones(),
        "teamSpawns": [SPAWNS["n"], SPAWNS["s"]],
        "hills": [{"x": 0, "z": 0, "y": 0, "radius": 4.5},
                  {"x": -31, "z": 0, "y": HOLD_Y, "radius": 3.5},
                  {"x": WALL_X - 0.05, "z": 0, "y": 16, "radius": 4.5}],
        "flags": [[0, 0, -42], [0, 0, 42]],
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
    S = 5
    W, H = 110 * S, 110 * S
    img = Image.new("RGB", (W, H), (30, 30, 44))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * S, H / 2 + z * S)
    d.polygon([P(x, z) for x, z in boundary_points()], outline=(120, 120, 160))
    for o in OPS:
        if o["op"] == "Block":
            c = int(90 + min(o["y1"], 26) * 5)
            d.rectangle([P(o["x0"], o["z0"]), P(o["x1"], o["z1"])], fill=(c, c, c - 20))
    for o in OPS:
        if o["op"] == "Waypoint":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 2, z - 2, x + 2, z + 2], fill=(80, 200, 255))
        if o["op"] == "Spawn":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 3, z - 3, x + 3, z + 3], fill=(80, 255, 80) if o["z"] < 0 else (255, 80, 80))
        if o["op"] == "Prop":
            x, z = P(o["x"], o["z"])
            d.rectangle([x - 2, z - 2, x + 2, z + 2], fill=(255, 200, 60))
    x, z = P(ROCK_C[0], ROCK_C[2])
    d.ellipse([x - ROCK_R * S, z - ROCK_R * S, x + ROCK_R * S, z + ROCK_R * S], outline=(200, 160, 120), width=2)
    img.save(path)
    print("plan:", path)


if __name__ == "__main__":
    main()
