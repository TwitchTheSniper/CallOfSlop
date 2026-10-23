#!/usr/bin/env python3
"""Generator for Maps/Space/09-Hollowrock.json: "Hollowrock Refinery", the Space pack's asteroid ore refinery
(medium-large, 16 players).

    python3 tools/space/hollowrock.py [repo root] [plan.png]

An irregular lump of rock belonging to the invented Tessera Ore Combine, hollowed by tunnels and open pits under a
black, starry sky with a hard low sun. The playable area is a noisy smooth blob about 150 x 110 m with the rock's crags
rising round it (a terrain "cliff" edge). Point-symmetric about the centre (every x, z has a -x, -z twin), Blue holds
the north-west, Red the south-east:

  - Dock 1 / Dock 2 (-+44, -+25): each team's raised landing pad (5 m up): a control hut, docking clamps, a parked ore
    shuttle, the spawns and the CTF flag. Three ramps lead down: east to the conveyor bridge, south to the crusher
    yard, north-east to the terrace.
  - The Chasms (-+24, -+16): two fall-kill rifts, 19 x 8 m, floor far below the kill height. A conveyor bridge (a railed
    3.2 m walkway with the ore belt alongside) crosses each; the ground runs round both ends (the Neck, under the
    terrace, and the yard's edge).
  - Crusher Yard (-+34, +-9): a 2.5 m terrace of ore crushers, belts, heaps and a dump truck, with the West / East Drift
    running under it.
  - West / East Drift: a 70 m tunnel from the dock's foot, under the yard, through a chamber (with a skylight you can
    drop through) and out south / north of the smelter: the covered flank route.
  - The Terrace (-+10, -+40): a 6 m high plateau with ramps at both ends overlooking the chasm, the bridge and the
    smelter: the exposed surface route.
  - The Smelter (0, 0): the crucible (a glowing 1.4 m pit, hill 1) between two furnace stacks, slag pots, turbines and
    pipe runs. Hills 2 and 3 sit on the pads either side of it, on the line both teams reach in equal time.

Built for the FPS: terrain does the shaping (one heightfield), the big round things are meshes over hidden colliders,
the dressing is instanced props and baked decals. No foliage (an airless rock).
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "09-Hollowrock.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import hollowrock_redvane_decals as sdec  # noqa: E402

FALL_KILL = -4.0
CHASM_FLOOR = -16.0
DOCK = (-44.0, -25.0)
DOCK_R = 14.0
DOCK_Y = 5.0
YARD = (-34.0, 9.0)
YARD_Y = 2.5
TERRACE = (-10.0, -40.0)
TERRACE_Y = 6.0
CHASM = (-24.0, -16.0)
TUNNEL_Y = -3.5

ROCK = "#5a544e"
ROCK2 = "#48433e"
REGOLITH = "#6b5f52"
STEEL = "#6c6f73"
STEEL2 = "#85888c"
DARK = "#3c414e"
RUST = "#7a5238"
HULL = "#c9bda6"
HULL2 = "#a89b84"
AMBER = "#ffb24a"
EMBER = "#ff6a1e"
SAFETY = "#e0b030"
DECK = "#7e8288"
GLASS = "#2e4450"

OPS = []
STATS = {"boxes": 0, "colliders": 0, "meshes": 0, "props": 0, "decals": 0, "waypoints": 0, "spawns": 0}
SPAWNS = {"b": [], "r": []}


def r(v, n=3):
    return round(float(v), n)


def op(d):
    OPS.append(d)


def mir(x, z):
    return -x, -z


# ------------------------------------------------------------------ op helpers (each places the thing and, by default, its point-mirror twin)

def block(x0, y0, z0, x1, y1, z1, color, mirror=True, anchor=None):
    STATS["boxes"] += 2 if mirror else 1
    d = {"op": "Block", "x0": r(min(x0, x1)), "y0": r(y0), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "y1": r(y1), "z1": r(max(z0, z1)), "color": color}
    if anchor:
        d["anchor"] = anchor
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x0": r(-max(x0, x1)), "x1": r(-min(x0, x1)), "z0": r(-max(z0, z1)), "z1": r(-min(z0, z1))})
        op(e)


def collider(x0, y0, z0, x1, y1, z1, mirror=True, anchor=None):
    STATS["colliders"] += 2 if mirror else 1
    d = {"op": "Collider", "x0": r(min(x0, x1)), "y0": r(y0), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "y1": r(y1), "z1": r(max(z0, z1))}
    if anchor:
        d["anchor"] = anchor
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x0": r(-max(x0, x1)), "x1": r(-min(x0, x1)), "z0": r(-max(z0, z1)), "z1": r(-min(z0, z1))})
        op(e)


def mesh(kind, x, z, y, color, mirror=True, anchor=None, **kw):
    STATS["meshes"] += 2 if mirror else 1
    d = {"op": "Mesh", "mesh": kind, "x": r(x), "z": r(z), "y": r(y), "color": color}
    if anchor:
        d["anchor"] = anchor
    for k, v in kw.items():
        if v is not None:
            d[k] = r(v) if isinstance(v, float) else v
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x": r(-x), "z": r(-z), "yaw": r((kw.get("yaw", 0) + 180) % 360)})
        op(e)


def prop(name, x, z, yaw=0.0, y=None, mirror=True, anchor=None, **kw):
    STATS["props"] += 2 if mirror else 1
    d = {"op": "Prop", "name": name, "x": r(x), "z": r(z)}
    if y is not None:
        d["y"] = r(y)
    if yaw:
        d["yaw"] = r(yaw % 360, 1)
    if anchor:
        d["anchor"] = anchor
    d.update(kw)
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x": r(-x), "z": r(-z), "yaw": r((yaw + 180) % 360, 1)})
        op(e)


def decal(name, x, z, y=0.0, onto="floor", mirror=True, anchor=None, **kw):
    STATS["decals"] += 2 if mirror else 1
    d = {"op": "Decal", "decal": name, "x": r(x), "z": r(z), "y": r(y), "onto": onto}
    if anchor:
        d["anchor"] = anchor
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


def waypoint(x, z, y=0.0, mirror=True, anchor=None):
    STATS["waypoints"] += 2 if mirror else 1
    d = {"op": "Waypoint", "x": r(x), "z": r(z), "y": r(y)}
    if anchor:
        d["anchor"] = anchor
    op(d)
    if mirror:
        e = dict(d)
        e.update({"x": r(-x), "z": r(-z)})
        op(e)


def spawn(x, z):
    STATS["spawns"] += 2
    op({"op": "Spawn", "x": r(x), "z": r(z)})
    op({"op": "Spawn", "x": r(-x), "z": r(-z)})
    SPAWNS["b"].append([r(x), r(z)])
    SPAWNS["r"].append([r(-x), r(-z)])


def rocks(x, z, size, seed, mirror=True):
    op({"op": "Rocks", "x": r(x), "z": r(z), "size": r(size), "color": ROCK2, "seed": seed})
    if mirror:
        op({"op": "Rocks", "x": r(-x), "z": r(-z), "size": r(size), "color": ROCK2, "seed": seed + 7})


def sign(key, x, z, facing, y=1.7, mirror=True):
    """A sign board (a thin block) with the set's tiled sign decal on its face. facing: the way the face looks, +z/-z/+x/-x."""
    names, tw, th = sdec.tiles("Hollowrock", key)
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
            xi = x - w / 2 + tw * (i + 0.5)
            decal(nm, xi if facing == "+z" else xi, zz, y + th / 2, onto, mirror=mirror, size=tw, rotation=0, reach=1.5)
    else:
        x0 = x if facing == "+x" else x - t
        block(x0, y - 0.2, z - w / 2 - 0.15, x0 + t, y + th + 0.2, z + w / 2 + 0.15, DARK, mirror=mirror)
        block(x0, 0, z - 0.08, x0 + t, y - 0.2, z + 0.08, STEEL, mirror=mirror)
        onto = "wall-x" if facing == "+x" else "wall+x"
        xx = x0 + t + 0.6 if facing == "+x" else x0 - 0.6
        for i, nm in enumerate(names):
            zi = z + w / 2 - tw * (i + 0.5) if facing == "+x" else z - w / 2 + tw * (i + 0.5)
            decal(nm, xx, zi, y + th / 2, onto, mirror=mirror, size=tw, rotation=0, reach=1.5)


# ------------------------------------------------------------------ outline and terrain

def boundary_points():
    half = [(-66, -6), (-62, -28), (-50, -46), (-30, -56), (-6, -54), (14, -48), (34, -41), (54, -31)]
    return [list(p) for p in half] + [[-x, -z] for x, z in half]


def chasm_points(cx, cz):
    """An irregular rift 19 x 8 m, long along Z, with a slight kink."""
    pts = [(-3.6, -9.5), (-1.0, -9.8), (2.6, -8.4), (4.2, -5.0), (3.4, -1.0), (4.6, 3.0), (3.8, 7.2), (1.2, 9.6), (-2.2, 9.4), (-4.4, 6.0),
           (-3.0, 2.0), (-4.6, -2.0), (-4.0, -6.0)]
    return [[r(cx + x), r(cz + z)] for x, z in pts]


def terrain():
    def both(f):
        g = dict(f)
        for k in ("x", "z"):
            if k in g:
                g[k] = -g[k]
        if "points" in g:
            g["points"] = [[-p[0], -p[1]] + p[2:] for p in g["points"]]
        return [f, g]

    feats = []
    feats.append({"type": "noise", "amplitude": 0.8, "scale": 16, "octaves": 3, "seed": 19})
    # A gentle dome so the middle sits a little higher than the rim (water would run off, if there were any).
    feats.append({"type": "hill", "x": 0, "z": 0, "radius": 70, "height": 1.2})
    # Docks: a flat pad 5 m up with steep sides.
    feats += both({"type": "plateau", "x": DOCK[0], "z": DOCK[1], "radius": DOCK_R, "height": DOCK_Y, "edge": 3.5, "material": "rock"})
    # Crusher yards: a 2.5 m terrace.
    feats += both({"type": "flatten", "x": YARD[0], "z": YARD[1], "w": 22, "d": 16, "height": YARD_Y, "edge": 4, "material": "dirt"})
    # The terrace: 6 m up, cliff sides.
    feats += both({"type": "plateau", "x": TERRACE[0], "z": TERRACE[1], "w": 26, "d": 12, "height": TERRACE_Y, "edge": 3, "material": "rock"})
    # A spoil ridge along each drift, so the tunnel keeps a rock roof, and mounds over the chambers.
    feats += both({"type": "ridge", "points": [[-52, -4], [-47, 0], [-38, 8], [-28, 14], [-20, 22], [-14, 30]], "width": 14, "height": 1.6})
    feats += both({"type": "hill", "x": -27, "z": 16, "radius": 9, "height": 2.2, "falloff": "dome"})
    # Smelter pad and the hill pads.
    feats.append({"type": "flatten", "x": 0, "z": 0, "radius": 14, "height": 0.6, "edge": 4, "material": "dirt"})
    feats += both({"type": "flatten", "x": -8.5, "z": 16.5, "radius": 5.5, "height": 0.6, "edge": 3, "material": "dirt"})
    # Bridge landings (level, so the deck meets the ground).
    feats += both({"type": "flatten", "x": CHASM[0] - 9, "z": CHASM[1], "w": 6, "d": 7, "height": 0.4, "edge": 2.5, "material": "dirt"})
    feats += both({"type": "flatten", "x": CHASM[0] + 9, "z": CHASM[1], "w": 6, "d": 7, "height": 0.4, "edge": 2.5, "material": "dirt"})
    # Ramps (absolute heights at both ends).
    ramps = [
        [[-36, -24, DOCK_Y], [-32.5, -16.5, 0.4]],          # A: dock -> bridge
        [[-40, -15, DOCK_Y], [-37, -6, 0.4]],              # B: dock -> yard
        [[-37, -5, 0.4], [-35, 2, YARD_Y]],                # yard's north bank
        [[-36, -32, DOCK_Y], [-32, -39, 0.4]],             # C: dock -> terrace ramp
        [[-31, -38, 0.4], [-21, -40, TERRACE_Y]],          # terrace west ramp
        [[4, -36, TERRACE_Y], [12, -28, 0.4]],             # terrace east ramp
        [[-23, 8, YARD_Y], [-17, 6, 0.6]],                 # yard -> smelter
        [[-25, -4, YARD_Y], [-21, -7, 0.4]],               # yard -> chasm's south end
    ]
    for pts in ramps:
        feats += both({"type": "ramp", "points": pts, "width": 4.5, "edge": 2.5, "material": "dirt"})
    # Tunnels (run after every height feature): the drifts, their chambers, the skylights, the chasms, the crucible and ore pits.
    drift = [[-56, -8], [-47, 0, TUNNEL_Y], [-38, 8, TUNNEL_Y], [-28, 14, TUNNEL_Y], [-20, 22, TUNNEL_Y], [-14, 30, TUNNEL_Y + 0.5], [-8, 36]]
    feats += both({"type": "cave", "points": drift, "width": 5.5, "height": 3.2})
    feats += both({"type": "cave", "points": [[-27, 14, TUNNEL_Y]], "width": 12, "height": 5})
    feats += both({"type": "hole", "shape": "circle", "x": -25, "z": 17, "radius": 1.5})
    feats += both({"type": "hole", "shape": "polygon", "points": chasm_points(*CHASM), "floor": CHASM_FLOOR})
    feats.append({"type": "hole", "shape": "circle", "x": 0, "z": 0, "radius": 5.5, "depth": 1.4})
    feats += both({"type": "hole", "shape": "ellipse", "x": -8, "z": -19, "radius": 2.6, "radiusZ": 1.8, "angle": 25, "depth": 1.2})
    return {
        "cell": 1,
        "margin": 24,
        "surface": {"grass": "#55504a", "dirt": REGOLITH, "rock": ROCK, "sand": "#8a7a66", "dirtSlope": 20, "rockSlope": 36, "variation": 0.1},
        "features": feats,
    }


# ------------------------------------------------------------------ the dock

def dock():
    cx, cz = DOCK
    y = 0.0  # ops stand on the pad (terrain), so y is above the pad
    # Control hut: a one-storey blocky building with roof access, doors facing the map.
    op({"op": "Building", "cx": -48, "cz": -29, "w": 10, "d": 10, "stories": 1, "wall": HULL2, "floor": DECK, "doors": "E", "roofAccess": True})
    op({"op": "Building", "cx": 48, "cz": 29, "w": 10, "d": 10, "stories": 1, "wall": HULL2, "floor": DECK, "doors": "W", "roofAccess": True})
    STATS["boxes"] += 40
    prop("Sensor Dish", -50, -31, 30, y=3.3)
    prop("Antenna Mast", -46, -32, 0, y=3.3)
    prop("Station Console", -46, -27, 90, y=3.3)
    # Landing mark and the shuttle.
    decal("Hollowrock Dock Mark 1", -38, -20, mirror=False, rotation=0)
    decal("Hollowrock Dock Mark 2", 38, 20, mirror=False, rotation=0)
    prop("Parked Cloud Skimmer", -38, -20, 160)
    for x, z, yaw in [(-33.5, -14.5, 0), (-42.5, -13.5, 0), (-32, -26, 0), (-44, -36, 0)]:
        prop("Docking Clamp", x, z, yaw)
    for x, z in [(-35, -32), (-31, -22), (-50, -16), (-56, -22)]:
        prop("Landing Beacon", x, z)
    for x, z, yaw in [(-53, -19, 0), (-55, -27, 90), (-40, -36, 0)]:
        prop("Deck Light Post", x, z, yaw)
    # Cargo set down by the hut.
    prop("Station Cargo Pod", -52, -14, 10)
    prop("Supply Case Stack", -49, -14.5, 0)
    prop("Hover Cargo Sled", -41, -32.5, 95)
    prop("Hab Container", -52, -36.5, 0)
    prop("Fuel Coupling", -40, -27, 0)
    prop("Squat Pressure Tank", -56, -32, 0)
    prop("Station Deck Barrier", -36.5, -29.5, 90)
    prop("Station Deck Barrier", -36.5, -31.8, 90)
    sign("dock1", -42.4, -24, "+x", y=2.2, mirror=False)
    sign("dock2", 42.4, 24, "-x", y=2.2, mirror=False)
    decal("Tessera Logo", -42.9, -31.5, 2.0, "wall+x", size=1.8, rotation=0)
    decal("Hazard Stripes", -36, -24, 0, rotation=90, size=3)
    decal("Hazard Stripes", -40, -15, 0, rotation=0, size=3)
    scatter(["Oil Stain", "Rust Stain", "Floor Cracks", "Oil Drips", "Footprints"], -56, -36, -32, -14, 16, 31, gap=2)
    clutter(["Station Supply Case", "Steel Ammo Can", "Toolbox", "Cable Spool", "Gas Cylinders"], -56, -37, -31, -13, 12, 41)
    # Spawns: a ring on the pad clear of the hut and the ramps.
    for x, z in [(-38, -34), (-33, -28), (-33.5, -20.5), (-39, -14.5), (-47, -14), (-55, -19), (-57, -26), (-45, -37)]:
        spawn(x, z)
    spawn(-27, -33)
    spawn(-44, 2)
    spawn(-43, 14)
    spawn(-29, 14.5)


# ------------------------------------------------------------------ chasm and the conveyor bridge

def bridge():
    cx, cz = CHASM
    x0, x1 = cx - 8.0, cx + 8.0
    top = 0.75
    # Deck: a dark steel plate with a lighter tread strip, world-anchored.
    block(x0, top - 0.3, cz - 1.6, x1, top, cz + 1.6, DECK, anchor="world")
    block(x0, top, cz - 0.5, x1, top + 0.02, cz + 0.5, STEEL2, anchor="world")
    # Girders under it, into the rift.
    for s in (-1, 1):
        mesh("box", cx, cz + s * 1.5, top - 1.0, STEEL, width=16.4, height=0.7, depth=0.3, material="Metal", anchor="world")
    for x in (cx - 4, cx + 4):
        mesh("box", x, cz, top - 3.5, DARK, width=0.5, height=5.0, depth=3.2, anchor="world")
    # Railings: posts and a top rail (meshes), a hidden band that holds people in. South side only; the belt is the north rail.
    for x in [x0 + 0.3 + i * 2.2 for i in range(8)]:
        mesh("box", x, cz + 1.5, top, STEEL2, width=0.08, height=1.1, depth=0.08, anchor="world")
    mesh("box", cx, cz + 1.5, top + 1.02, STEEL2, width=16.0, height=0.08, depth=0.08, anchor="world")
    mesh("box", cx, cz + 1.5, top + 0.55, STEEL2, width=16.0, height=0.05, depth=0.05, anchor="world")
    collider(x0, top + 0.45, cz + 1.4, x1, top + 1.1, cz + 1.6, anchor="world")
    # The ore belt alongside (solid, waist high): a rail of its own.
    for x in (cx - 6, cx - 2, cx + 2, cx + 6):
        prop("Conveyor Belt Run", x, cz - 2.4, 0, y=top, anchor="world")
    block(x0, top - 0.3, cz - 3.1, x1, top, cz - 1.6, DARK, anchor="world")
    # The belt carries on over the ground to the smelter side and to the yard side.
    for x in (cx + 10, cx + 14):
        prop("Conveyor Belt Run", x, cz - 2.4, 0)
    prop("Ore Bin Hopper", cx + 18, cz - 2.4, 90)
    prop("Conveyor Belt Run", cx - 10, cz - 2.4, 0)
    # Lane arrows and grit on the deck; warning signs and barriers at the rift's rims.
    for x in (cx - 5, cx, cx + 5):
        decal("Hollowrock Lane Arrow", x, cz, top, rotation=90, anchor="world")
    scatter(["Hollowrock Ore Grit", "Rust Stain", "Scorch Mark"], x0, cz - 1.5, x1, cz + 1.5, 6, 51, y=top, onto="floor", anchor="world", gap=1.5)
    sign("chasm", cx - 6, cz - 6.5, "-x", y=1.5)
    sign("chasm", cx + 6.2, cz + 7.5, "+x", y=1.5)
    for x, z, yaw in [(cx - 6.2, cz - 4.5, 90), (cx - 6.2, cz + 4.5, 90), (cx + 6.2, cz - 5.5, 90), (cx + 6.2, cz + 5.5, 90)]:
        prop("Station Deck Barrier", x, z, yaw)
    prop("Warning Light Mast", cx - 7, cz + 9, 0)
    prop("Warning Light Mast", cx + 7, cz - 11, 0)
    # Waypoints along the deck and both landings (the grid can miss a 3 m walkway).
    for x in [x0 - 2, x0 + 1, cx - 3, cx, cx + 3, x1 - 1, x1 + 2]:
        waypoint(x, cz, top + 0.02 if x0 <= x <= x1 else 0, anchor="world" if x0 <= x <= x1 else None)


# ------------------------------------------------------------------ crusher yard

def yard():
    cx, cz = YARD
    sign("yard", cx, cz - 7.2, "-z", y=2.4)
    # Two crushers feeding a belt line to a hopper, heaps either side.
    prop("Ore Crusher", cx - 6, cz + 2, 180)
    prop("Ore Crusher", cx + 3, cz + 2, 180)
    for x in (cx - 5.5, cx - 1.5, cx + 2.5, cx + 6.5):
        prop("Conveyor Belt Run", x, cz + 5.2, 0)
    prop("Ore Bin Hopper", cx + 10, cz + 5.2, 90)
    prop("Mine Dump Truck", cx - 4, cz - 3.5, 100)
    prop("Rock Drill Rig", cx + 7, cz - 4, 220)
    for x, z, yaw in [(cx - 9, cz + 6, 20), (cx + 1, cz - 1.5, 70), (cx - 10, cz - 4, 130), (cx + 8, cz + 1, 0)]:
        prop("Ore Heap", x, z, yaw)
    prop("Mine Floodlight Mast", cx - 9.5, cz - 6.5, 0)
    prop("Diesel Storage Tank", cx + 8, cz - 7, 0)
    prop("Hab Container", cx - 2, cz + 7.5, 0)
    prop("Blasting Warning Sign", cx + 10, cz - 2, 270)
    prop("Slag Pot", cx + 4.5, cz - 1.5, 0)
    clutter(["Oil Drum", "Rusty Drum", "Mine Timber Stack", "Pallet", "Small Crate", "Cable Spool", "Tyre"], cx - 11, cz - 8, cx + 11, cz + 8, 14, 61)
    scatter(["Tyre Tracks", "Oil Stain", "Hollowrock Ore Grit", "Hollowrock Ore Grit", "Rust Stain", "Footprints", "Gravel Scatter"],
            cx - 11, cz - 8, cx + 11, cz + 8, 22, 71, gap=1.5)
    decal("Hollowrock Slag Spill", cx + 5.5, cz - 3, 0)
    # The drift's skylight: barriers round it and a sign.
    for x, z, yaw in [(-25, 15.1, 0), (-25, 18.9, 0)]:
        prop("Station Deck Barrier", x, z, yaw)
    decal("Hollowrock Hazard Ring", -25, 17, 0, size=4.6)


# ------------------------------------------------------------------ the terrace

def terrace():
    cx, cz = TERRACE
    sign("terrace", cx - 12.4, cz, "-x", y=2.2)
    # Low cover along the south rim looking at the chasm and smelter, a searchlight, sandbags, ammo.
    for x in (cx - 9, cx - 5, cx + 1, cx + 5, cx + 9):
        prop("Station Deck Barrier", x, cz + 5.4, 0)
    prop("Sandbag Nest Ring", cx - 1, cz + 3.5, 0)
    prop("Field Searchlight", cx + 7, cz + 3.5, 180)
    prop("Mine Floodlight Mast", cx - 10, cz - 4.5, 0)
    prop("Sensor Dish", cx + 9, cz - 4, 200)
    prop("Hab Container", cx + 3, cz - 4, 0)
    prop("Supply Case Stack", cx - 5, cz - 2, 30)
    prop("Stacked Ammo Crates", cx + 8.5, cz + 0.5, 0)
    prop("Warning Light Mast", cx - 12, cz + 5, 0)
    clutter(["Steel Ammo Can", "Station Supply Case", "Sandbag Pile", "Spent Shell Casings", "Oil Drum"], cx - 12, cz - 5, cx + 12, cz + 5, 8, 81)
    scatter(["Footprints", "Rust Stain", "Hollowrock Ore Grit", "Floor Cracks"], cx - 12, cz - 5, cx + 12, cz + 5, 10, 91, gap=2)
    for x in (cx - 11, cx - 4, cx + 4, cx + 11):
        waypoint(x, cz)
    for pts in ([[-31, -38], [-26, -39], [-21, -40]], [[4, -36], [8, -32], [12, -28]], [[-36, -32], [-34, -35.5], [-32, -39]]):
        for x, z in pts:
            waypoint(x, z)


# ------------------------------------------------------------------ the smelter

def smelter():
    # The crucible: hill 1 in the glowing pit, a hazard ring round the rim.
    decal("Hollowrock Glow Floor", 0, 0, 0, mirror=False, rotation=0, size=10)
    decal("Hollowrock Hazard Ring", 0, 0, 0, mirror=False, rotation=0, size=14)
    # Furnace stacks: a tall cylinder with a glowing throat band over a hidden core.
    for (x, z) in [(7.5, -7.5)]:
        mesh("cylinder", x, z, 0, STEEL, width=5.0, depth=5.0, height=15, segments=20, material="Metal")
        mesh("cylinder", x, z, 15, DARK, width=3.6, depth=3.6, height=3, segments=20)
        mesh("torus", x, z, 2.2, EMBER, radius=2.55, inner=0.18)
        mesh("torus", x, z, 6.0, RUST, radius=2.6, inner=0.14)
        mesh("torus", x, z, 11.0, RUST, radius=2.6, inner=0.14)
        collider(x - 1.9, 0, z - 1.9, x + 1.9, 18, z + 1.9)
        collider(x - 2.4, 0, z - 1.0, x + 2.4, 18, z + 1.0)
        collider(x - 1.0, 0, z - 2.4, x + 1.0, 18, z + 2.4)
        prop("Wall Pipe Run", x - 2.65, z, 90, y=0.6)
        prop("Caged Bulkhead Lamp", x, z - 2.6, 0, y=2.6)
        prop("Station Pipe Riser", x + 2.9, z, 0)
    # A gantry over the pit: two posts and a beam with a hanging slag pot.
    block(9.6, 0, -0.4, 10.4, 9, 0.4, DARK)
    mesh("box", 0, 0, 8.6, STEEL, width=20.8, height=0.5, depth=0.5, material="Metal", mirror=False)
    mesh("box", 2, 0, 6.2, DARK, width=0.08, height=2.4, depth=0.08, mirror=False)
    mesh("cylinder", 2, 0, 5.0, STEEL, width=1.6, depth=1.6, height=1.2, segments=12, mirror=False)
    # Slag pots and machines round the pit.
    for x, z in [(4, 6.5), (6.5, 3.5), (-3, 7.5)]:
        prop("Slag Pot", x, z, 0)
    prop("Turbine Generator", -8, -3, 90)
    prop("Coolant Pump", -9.5, 3, 0)
    prop("Pipe Valve Stack", -6, 8.5, 0)
    prop("Station Pipe Run", -11, -1, 0)
    prop("Reactor Control Desk", 11, 4, 270)
    prop("Volatile Gas Canister Rack", 9, 9, 0)
    prop("Hab Container", -1, -11.5, 0)
    prop("Conveyor Belt Run", -11.5, -8, 0)
    sign("smelter", -8.2, -12.6, "-z", y=2.4)
    scatter(["Hollowrock Slag Spill", "Scorch Mark", "Hollowrock Ore Grit", "Oil Stain", "Rust Stain", "Soot Plume"], -13, -13, 13, 13, 18, 101, gap=1.8, mirror=False)
    decal("Scorch Mark", 7.5, -7.5, 2.2, "wall-x", size=2.5)


# ------------------------------------------------------------------ the drifts

def drifts():
    y = TUNNEL_Y
    # Portal frames at the mouths, lamps and timbering along the tunnel, carts and a heap in the chamber.
    sign("west", -51.5, -11.5, "+z", y=2.4, mirror=False)
    sign("east", 51.5, 11.5, "-z", y=2.4, mirror=False)
    prop("Tunnel Timber Frame", -53.5, -5.5, 48)
    prop("Tunnel Timber Frame", -9.5, 33.5, 45)
    for (x, z, yaw) in [(-45, 1.8, 48), (-40, 6.2, 40), (-33, 11, 30), (-23, 19, 45), (-17, 26, 55)]:
        prop("Tunnel Timber Frame", x, z, yaw, y=y, anchor="world")
    for (x, z) in [(-47, 0), (-38, 8), (-20, 22), (-14, 30)]:
        prop("Caged Bulkhead Lamp", x, z, 0, y=y + 2.6, anchor="world")
    for (x, z, yaw) in [(-49, -3, 48), (-43, 3.5, 48), (-36, 9.5, 36), (-30, 13, 36), (-22, 20, 50), (-17, 26, 55), (-12, 33, 45)]:
        prop("Mine Rail Section", x, z, yaw, y=y, anchor="world")
    prop("Ore Mine Cart", -30, 12.5, 36, y=y, anchor="world")
    prop("Ore Mine Cart", -24, 11.5, 0, y=y, anchor="world")
    prop("Ore Heap", -28, 18.5, 0, y=y, anchor="world")
    prop("Mine Timber Stack", -31, 16.5, 20, y=y, anchor="world")
    prop("Caged Bulkhead Lamp", -27, 14, 0, y=y + 4.2, anchor="world")
    prop("Blasting Explosives Crate", -24, 16.5, 0, y=y, anchor="world")
    for (x, z) in [(-47, 0), (-38, 8), (-27, 14), (-20, 22), (-14, 30)]:
        decal("Hollowrock Ore Grit", x, z, y, anchor="world")


# ------------------------------------------------------------------ open ground: hills, pits, cover, grit

def open_ground():
    # Hill pads 2 and 3: a ring of cover round each.
    hx, hz = -8.5, 16.5
    prop("Hab Container", hx - 7.5, hz + 1, 80)
    prop("Reagent Sphere", hx + 7, hz + 2, 0)
    prop("Ore Heap", hx + 1, hz + 7, 40)
    prop("Station Deck Barrier", hx - 2, hz - 6.5, 0)
    prop("Sandbag Wall Long", hx + 4, hz - 5, 150)
    prop("Warning Light Mast", hx - 6.5, hz - 5, 0)
    # The ore pit (shallow, climbable) with heaps and a drill beside it; rocks and heaps across the open ground.
    prop("Rock Drill Rig", -13, -22.5, 300)
    prop("Ore Heap", -4.5, -22, 10)
    rocks(-16, -31, 2.4, 3)
    rocks(-14, 12, 2.0, 5)
    rocks(14, -30, 1.8, 9)
    rocks(-40, -42, 2.8, 11)
    rocks(-58, -2, 2.6, 13)
    rocks(-20, 26, 2.2, 15)
    rocks(-6, 36, 3.0, 17)
    # Cover between the bridge landing and the smelter, and in the Neck.
    prop("Hab Container", -14, -8, 0)
    prop("Ore Heap", -17, -3, 60)
    prop("Station Cargo Pod", -19, -28, 20)
    prop("Supply Case Stack", -16, -26, 0)
    prop("Pipe Stack", -10, -27, 70)
    prop("Atmos Sensor Pole", -28, -29, 0)
    prop("Atmos Sensor Pole", 2, -24, 0)
    prop("Warning Light Mast", -19.5, -31, 0)
    # By the tunnel exits (where the drift breaks out under the far terrace) and south of the smelter.
    prop("Ore Heap", -3, 38, 20)
    prop("Station Deck Barrier", -12, 40, 90)
    prop("Mine Floodlight Mast", 2, 30, 0)
    prop("Hab Container", 8, 24, 170)
    prop("Ore Heap", -4, 26, 50)
    # By the dock ramps' feet and the yard's edge.
    prop("Ore Heap", -39, -1, 0)
    prop("Hover Cargo Sled", -30, -8, 40)
    prop("Reagent Sphere", -47, 7, 0)
    prop("Supply Case Stack", -41, 12, 0)
    prop("Station Cargo Pod", -44, 17, 100)
    # Grit, tracks and footprints over the open ground (regolith).
    scatter(["Hollowrock Ore Grit", "Tyre Tracks", "Footprints", "Gravel Scatter", "Floor Cracks", "Rubble Dust"], -22, -34, 14, -4, 28, 111, gap=2.5)
    scatter(["Hollowrock Ore Grit", "Footprints", "Gravel Scatter", "Rubble Dust"], -48, -10, -20, 30, 22, 121, gap=2.5)
    clutter(["Rubble Pile", "Rubble", "Loose Bricks", "Cinder Blocks", "Oil Drum", "Station Supply Case"], -22, -34, 14, -4, 14, 131)
    clutter(["Rubble", "Rubble Pile", "Oil Drum", "Pipe Stack", "Cable Spool"], -48, -12, -20, 30, 10, 141)


def build():
    dock()
    bridge()
    yard()
    terrace()
    smelter()
    drifts()
    open_ground()
    # Waypoints on the ramps and the smelter's approaches; the grid does the rest.
    for pts in ([[-36, -24], [-34, -20], [-32.5, -16.5]], [[-40, -15], [-38.5, -10.5], [-37, -6], [-36, -1.5], [-35, 2]],
                [[-23, 8], [-20, 7], [-17, 6]], [[-25, -4], [-23, -5.5], [-21, -7]], [[-56, -8], [-52, -4.5]], [[-8, 36], [-11, 33]]):
        for x, z in pts:
            waypoint(x, z)
    op({"op": "AutoWaypoints", "spacing": 6})


def main():
    build()
    m = {
        "format": "call-of-slop-map/1",
        "name": "Hollowrock Refinery",
        "description": "An asteroid ore refinery: tunnels and open pits, conveyor bridges over fall-kill chasms, a glowing smelter. Mind the drop.",
        "author": "Call of Slop",
        "order": 609,
        "destructible": False,
        "fallKill": FALL_KILL,
        "maxPlayers": 16,
        "flagPool": "none",
        "skybox": {"preset": "night", "zenith": "#04050a", "horizon": "#15121a", "ground": "#0b0a09", "stars": 1.8, "clouds": False,
                   "sun": {"elevation": 14, "azimuth": 295, "color": "#ffd8b0", "intensity": 1.0, "size": 0.7, "glow": 0.2},
                   "moon": {"direction": [0.3, 0.35, -0.9], "size": 18, "color": "#c9a080", "intensity": 0.25},
                   "ambient": 1.5, "fog": {"max": 0.5}},
        "boundary": {"shape": "smooth", "points": boundary_points(), "noise": {"amplitude": 2.5, "scale": 14, "seed": 9},
                     "edge": {"style": "cliff", "height": 14, "width": 7}, "barrierHeight": 30},
        "terrain": terrain(),
        "teamSpawns": [SPAWNS["b"], SPAWNS["r"]],
        "hills": [{"x": 0, "z": 0, "radius": 5}, {"x": 8.5, "z": -16.5, "radius": 4.5}, {"x": -8.5, "z": 16.5, "radius": 4.5}],
        "flags": [[-39.5, -31], [39.5, 31]],
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
    sys.path.insert(0, os.path.join(REPO, "tools", "cluttered"))
    import terrain as tp
    spec = dict(m["terrain"])
    spec["features"] = [f for f in spec["features"] if f["type"] not in ("hole", "cave")]
    t = tp.generate(spec, 70, 60)
    S = 5
    W, H = 150 * S, 125 * S
    img = Image.new("RGB", (W, H), (10, 10, 14))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * S, H / 2 + z * S)
    for i in range(0, W, 2):
        for j in range(0, H, 2):
            x, z = (i - W / 2) / S, (j - H / 2) / S
            h = float(t.sample(x, z))
            c = int(max(0, min(255, 70 + h * 14)))
            d.rectangle([i, j, i + 1, j + 1], fill=(c, c - 8, c - 16))
    d.polygon([P(x, z) for x, z in m["boundary"]["points"]], outline=(120, 160, 200))
    for f in m["terrain"]["features"]:
        if f["type"] == "hole":
            if f["shape"] == "polygon":
                d.polygon([P(x, z) for x, z in f["points"]], fill=(0, 0, 0) if "floor" in f else (40, 30, 30))
            else:
                x, z, rr = f["x"], f["z"], f["radius"]
                d.ellipse([P(x - rr, z - rr), P(x + rr, z + rr)], fill=(40, 30, 30))
        if f["type"] == "cave":
            pts = [P(p[0], p[1]) for p in f["points"]]
            if len(pts) > 1:
                d.line(pts, fill=(200, 120, 60), width=int(f.get("width", 5) * S))
            else:
                x, z, rr = f["points"][0][0], f["points"][0][1], f.get("width", 5) / 2
                d.ellipse([P(x - rr, z - rr), P(x + rr, z + rr)], fill=(200, 120, 60))
    for o in OPS:
        if o["op"] in ("Block", "Collider"):
            d.rectangle([P(o["x0"], o["z0"]), P(o["x1"], o["z1"])], fill=(150, 150, 160) if o["op"] == "Block" else (90, 90, 120))
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
            d.rectangle([x - o["w"] / 2 * S, z - o["d"] / 2 * S, x + o["w"] / 2 * S, z + o["d"] / 2 * S], outline=(255, 120, 255))
    for h in m["hills"]:
        x, z = P(h["x"], h["z"])
        d.ellipse([x - h["radius"] * S, z - h["radius"] * S, x + h["radius"] * S, z + h["radius"] * S], outline=(255, 255, 0), width=2)
    for fx, fz in m["flags"]:
        x, z = P(fx, fz)
        d.rectangle([x - 4, z - 4, x + 4, z + 4], outline=(255, 255, 255), width=2)
    img.save(path)
    print("plan:", path)


if __name__ == "__main__":
    main()
