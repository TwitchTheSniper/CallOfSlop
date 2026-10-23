#!/usr/bin/env python3
"""Generator for Maps/Space/07-CanopyDeck.json: "Canopy Deck", the arboretum of the derelict generation ship Long
Meridian (Space pack, medium-large, 16 players).

    python3 tools/space/arboretum.py [repo root] [plan.png]

A biodome inside a torn hull, open to the stars where the dome has gone: the ship's ribs still arch over the deck. The
soil has gone wild in the centuries since the crew left: grass, shrubs and trees have taken the terraces, the dry
irrigation canals and the airlock aprons; the hydroponic racks still glow on the terraces; emergency lighting blinks
on the ribs. Point-symmetric about the centre: Blue holds the west airlock bay, Red the east one, and everything Blue
has at (x, z) Red has at (-x, -z).

Terrain (1 m cells): a raised central terrace round the great tree (hill 1), two long upper terraces along the north and
south hull (hills 2 and 3, the hydroponic bays), two dry canals curving north-south either side of the centre (sunken
flank lanes), low aprons under the airlock bays, and ramps between them all. Boxes: the airlock bays (Hangar ops), the
hull ribs (pillars with cosmetic arching beams), two collapsed walkways (a raised bridge from each upper terrace whose
far end has fallen into a slab of rubble steps down to the deck), bulkhead panels for the signs and the great tree's
trunk. The ellipse boundary is the hull wall.
"""
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "07-CanopyDeck.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
from spacekit import Builder, ellipse_points, yaw_towards, write_map, plan, r  # noqa: E402

RX, RZ = 62.0, 48.0          # the hull ellipse
HULL = "#5c6068"
HULL2 = "#4a4e56"
RIB = "#6e7278"
DECK = "#7a7e86"
BRIDGE = "#8a8e96"
TRUNK = "#4a3a2a"
CANOPY = "#2f5a2a"
CANOPY2 = "#3f6e2e"
LAMP = "#ffd9a0"

B = Builder()


# ================================================================================================ terrain
def mirror_feature(f):
    g = dict(f)
    for k in ("x", "z"):
        if k in g:
            g[k] = -g[k]
    if "points" in g:
        g["points"] = [[-p[0], -p[1]] + p[2:] for p in g["points"]]
    return g


def terrain():
    half = [
        {"type": "valley", "points": [[-28, -50], [-26, -24], [-25, 0], [-26, 24], [-28, 50]], "width": 7, "depth": 1.6},
        {"type": "plateau", "x": -52, "z": 0, "radius": 10, "height": 0.6, "edge": 1.6, "mode": "fill"},
        {"type": "flatten", "x": -54, "z": 0, "w": 15, "d": 19, "height": 0.6, "edge": 1.2},
        {"type": "plateau", "x": 0, "z": -37, "w": 44, "d": 12, "height": 2.4, "edge": 2.2, "mode": "fill"},
        {"type": "ramp", "points": [[-33, -27], [-24, -33, 2.4]], "width": 4, "edge": 2},
        {"type": "ramp", "points": [[33, -27], [24, -33, 2.4]], "width": 4, "edge": 2},
        {"type": "ramp", "points": [[0, -25.5], [0, -32, 2.4]], "width": 5, "edge": 2},
        {"type": "ramp", "points": [[-20, -20], [-10.5, -10.5, 1.4]], "width": 4.5, "edge": 2},
        {"type": "ramp", "points": [[-20, 20], [-10.5, 10.5, 1.4]], "width": 4.5, "edge": 2},
        {"type": "flatten", "x": -9, "z": -13, "w": 6, "d": 12, "height": 0, "edge": 2},
    ]
    feats = [{"type": "noise", "amplitude": 0.3, "scale": 11, "seed": 3}]
    for f in half:
        feats.append(f)
        feats.append(mirror_feature(f))
    # The central terrace last among the plateaus, so the ramps' tops meet its rim; it is its own mirror image.
    feats.insert(1, {"type": "plateau", "x": 0, "z": 0, "radius": 14, "height": 1.4, "edge": 1.8, "mode": "fill"})
    return {
        "cell": 1, "margin": 14, "base": 0,
        "surface": {"grass": "#4e6e34", "dirt": "#4a3c30", "rock": "#5e5a54", "sand": "#7a6a4c", "sandBelow": -0.9, "dirtSlope": 22, "variation": 0.1},
        "features": feats,
    }


# ================================================================================================ pieces
def hull_ribs():
    """Pillars 3.5 m inside the hull with two cosmetic beams arching up and inward; none in the airlock bays."""
    n = 18
    for k in range(n):
        t = 2 * math.pi * k / n + math.pi / n
        if abs(math.cos(t)) > 0.86:
            continue  # the airlock bays east and west
        x, z = (RX - 3.5) * math.cos(t), (RZ - 3.5) * math.sin(t)
        B.block(x - 0.6, 0, z - 0.6, x + 0.6, 7.5, z + 0.6, RIB)
        yaw = yaw_towards(-x, -z)
        px, pz, py = x, z, 7.2
        for (L, pitch) in ((8.0, 48.0), (9.0, 76.0)):
            B.mesh("box", px, pz, py, RIB, width=1.0, height=L, depth=0.7, yaw=yaw, pitch=pitch, material="Metal", anchor="world")
            a, p = math.radians(yaw), math.radians(pitch)
            px, pz, py = px + math.sin(a) * L * math.sin(p), pz + math.cos(a) * L * math.sin(p), py + L * math.cos(p)
        L = math.hypot(x, z)
        ux, uz = -x / L, -z / L
        B.prop("Emergency Strip Lamp", x + 0.62 * ux, z + 0.62 * uz, yaw=yaw, y=2.3)
        B.prop("Caged Bulkhead Lamp", x + 0.62 * ux, z + 0.62 * uz, yaw=yaw, y=4.0)
        # Pipe runs along the hull between the ribs.
        t2 = t + math.pi / n
        x2, z2 = (RX - 1.6) * math.cos(t2), (RZ - 1.6) * math.sin(t2)
        if abs(math.cos(t2)) <= 0.86:
            B.prop("Wall Pipe Run" if k % 2 else "Wall Cable Tray", x2, z2, yaw=yaw_towards(-x2, -z2) + 90, y=2.6 if k % 2 else 1.4)


def great_tree():
    B.block(-1.1, 0, -1.1, 1.1, 9.0, 1.1, TRUNK)
    B.mesh("cylinder", 0, 0, 0, TRUNK, width=2.6, height=8.5, depth=2.6, segments=12, material="Wood")
    B.mesh("cone", 0, 0, 0, TRUNK, width=4.6, height=1.2, depth=4.6, segments=10, material="Wood")  # root flare
    for (dx, dz, w, h, y) in ((0, 0, 14, 5, 8.2), (4, 2, 9, 4, 9.5), (-4, -3, 9, 4, 10.5), (2, -5, 7, 3.5, 11.2), (-3, 4, 7, 3.5, 11.8)):
        B.mesh("icosphere", dx, dz, y, CANOPY if (dx + dz) % 2 else CANOPY2, width=w, height=h, depth=w, detail=2, anchor="world")
    for yaw in (20, 110, 200, 290):
        B.mesh("box", 0, 0, 7.6, TRUNK, width=0.6, height=5.0, depth=0.6, yaw=yaw, pitch=62, material="Wood", anchor="world")


def airlock_bay(s):
    """The team's base: a hangar-like airlock bay on the apron, the flag and the spawns inside and behind it."""
    B.op({"op": "Hangar", "cx": r(-54 * s), "cz": 0, "w": 12, "d": 16, "h": 6, "wall": HULL, "roof": HULL2, "doorsOnX": True})
    B.stats["boxes"] += 15
    # Deck plates: cosmetic box meshes whose tops sit 2 cm above the apron (no collision, so spawns stand on them).
    B.mesh("box", -54, 0, -0.1, DECK, s=s, width=11.8, height=0.12, depth=15.8, material="Metal")
    B.mesh("box", -44, 0, -0.1, DECK, s=s, width=7.8, height=0.12, depth=19, material="Metal")
    # Signs and crest.
    B.sign("airlock_w" if s > 0 else "airlock_e", -47.3 * s, 0, 5.3, "wall-x" if s > 0 else "wall+x", along="z")
    B.sign("hull", -54 * s, -8.6 * s, 3.4, "wall+z" if s > 0 else "wall-z")
    B.sign("oxygen", -54 * s, 8.6 * s, 3.4, "wall-z" if s > 0 else "wall+z")
    B.decal("Ark Crest", -44, 0, 0.1, s=s, size=4.0, rotation=90 if s > 0 else 270)
    B.decal("Canopy Hull Seams", -54, 0, 0.1, s=s, size=4)
    B.scatter(["Canopy Hull Seams"], -60, -8, -40, 9, 14, 21 if s > 0 else 22, s=s, y=0.1, gap=3.6)
    B.scatter(["global:Rust Stain", "global:Oil Stain", "global:Floor Cracks"], -60, -8, -41, 8, 8, 31 if s > 0 else 32, s=s, y=0.1)
    B.decal("Canopy Section 1" if s > 0 else "Canopy Section 3", -44, -6, 0.1, s=s, size=2.2, rotation=90 if s > 0 else 270)
    # Inside: cargo along the side walls, cases, a console. Outside: the drone wreck, barriers, a sled.
    B.props([("Station Cargo Pod", -57, -6.2, 0), ("Station Cargo Pod", -53, 6.2, 0), ("Supply Case Stack", -58.5, 5.5, 90),
             ("Station Supply Case", -50, -6.5, 20), ("Station Console", -57.5, 0, 90), ("Seed Vault Cabinet", -51, 6.6, 180),
             ("Hover Cargo Sled", -44, 7, 0), ("Maintenance Drone Wreck", -43, -7.5, 35), ("Station Deck Barrier", -46.5, -3.5, 0),
             ("Station Deck Barrier", -46.5, 3.5, 0), ("Deck Light Post", -47.5, -9.2, 90), ("Deck Light Post", -47.5, 9.2, 90),
             ("Station Pipe Riser", -59, -7.2, 0), ("Tool Cart", -55, -5.5, 70)], s=s)
    B.prop("Emergency Strip Lamp", -48.4, -5, yaw=90, s=s, y=2.8)
    B.prop("Emergency Strip Lamp", -48.4, 5, yaw=90, s=s, y=2.8)
    for (x, z) in ((-57, -3), (-57, 3), (-53, -2.5), (-53, 2.5), (-50, 0), (-45, -12), (-45, 12), (-40, -18), (-40, 18), (-50, -15), (-50, 15)):
        B.spawn(x, z, "blue", s=s)


def orchard(s):
    """Between the apron and the canal: an overgrown orchard with planters, heaps and junk; the west flank."""
    B.props([("Planter Trough", -38, -14, 90), ("Planter Trough", -34, 8, 0), ("Planter Trough", -42, 22, 30),
             ("Soil Heap", -36, -4, 0), ("Soil Heap", -31, 17, 0), ("Sprinkler Mast", -38, 2, 0), ("Sprinkler Mast", -36, -22, 0),
             ("Wheelbarrow", -39.5, -9, 160), ("Compost Bags", -41, 12, 20), ("Raised Bed", -33, -18, 0), ("Garden Tools", -32.5, -16.5, 0),
             ("Supply Case Stack", -31, -2, 90), ("Station Supply Case", -30, 10, 45), ("Cable Spool", -44, -19, 0),
             ("Gas Cylinders", -46.5, 18, 0), ("Rubble Pile", -30, 24, 0), ("Log", -40, 27, 50), ("Tree Stump", -46, 24, 0),
             ("Dead Vine Curtain", -36, 12.5, 90), ("Grow Light Pole", -33, 2, 0), ("Watering Can", -37.8, 1.2, 0)], s=s)
    B.scatter(["global:Leaf Litter", "global:Moss Patch", "global:Dirt Patch", "global:Mud Patch"], -47, -28, -29, 28, 26, 41 if s > 0 else 42, s=s)


def canal(s):
    """The west dry canal: sluice gates across it, algae, puddles, junk in the bed and vines on the banks."""
    for z in (-14, 14):
        B.prop("Sluice Gate", -26.2 + (0.4 if z < 0 else -0.4), z, yaw=0, s=s)
        B.prop("Dead Vine Curtain", -26.2 + (0.4 if z < 0 else -0.4), z + 0.5, yaw=0, s=s, y=-1.5)
    B.props([("Log", -25.5, -32, 15), ("Rubble Pile", -26.5, 30, 0), ("Station Pipe Run", -25, 4, 90), ("Soil Heap", -25.5, -3, 0),
             ("Brick Pile", -27, 22, 0), ("Plank Pile", -25, -24, 85)], s=s)
    B.scatter(["Algae Bloom", "Algae Bloom", "global:Puddle", "global:Water Stain", "global:Rubble Dust"], -29, -46, -23, 46, 30, 51 if s > 0 else 52, s=s)
    # The irrigation pump station on the canal's inner bank, with its sign on a panel wall.
    B.block(-19.5, 0, -24.2, -14.5, 3.0, -23.8, HULL, s=s)
    B.sign("irrigation", -17 * s, -23.7 * s, 2.3, "wall-z" if s > 0 else "wall+z")
    B.props([("Coolant Pump", -17, -22.2, 0), ("Station Pipe Riser", -20.2, -22.8, 0), ("Station Pipe Run", -22.5, -21, 90),
             ("Gas Valve", -14.2, -22.8, 0), ("Junction Box", -19.2, -23.3, 0)], s=s)
    B.decal("global:Oil Stain", -17, -21, 0, s=s, size=1.8)


def terrace(s):
    """The north upper terrace (z = -43..-31, 2.4 m up): hydroponic bays either side of hill 2, a bulkhead panel behind."""
    B.block(-10, 0, -45.4, 10, 5.5, -44.8, HULL, s=s)
    B.sign("arboretum", 0, -44.7 * s, 3.4, "wall-z" if s > 0 else "wall+z")
    B.sign("hydro", -6.5 * s, -44.7 * s, 1.5, "wall-z" if s > 0 else "wall+z")
    B.sign("vault", 6.5 * s, -44.7 * s, 1.5, "wall-z" if s > 0 else "wall+z")
    for x in (-8.5, 8.5):
        B.prop("Emergency Strip Lamp", x, -44.75, yaw=0, s=s, y=4.6)
    B.decal("Canopy Section 2" if s > 0 else "Canopy Section 4", 0, -41.5, 0, s=s, size=2.2, rotation=0 if s > 0 else 180)
    racks = [(-9, -40.5, "Hydroponic Rack Lit"), (-15, -40.5, "Hydroponic Rack"), (-20, -40.5, "Hydroponic Rack Lit"),
             (9, -40.5, "Hydroponic Rack"), (15, -40.5, "Hydroponic Rack Lit"), (20, -40.5, "Hydroponic Rack"),
             (-12, -33.8, "Hydroponic Rack"), (-19, -33.8, "Hydroponic Rack Lit"), (12, -33.8, "Hydroponic Rack Lit"), (19, -33.8, "Hydroponic Rack")]
    for (x, z, n) in racks:
        B.prop(n, x, z, yaw=0, s=s)
    B.props([("Seed Vault Cabinet", -4.5, -42.6, 0), ("Seed Vault Cabinet", 4.5, -42.6, 0), ("Station Console", -7, -36, 180),
             ("Station Server Cabinet", 7, -36, 180), ("Grow Light Pole", -12, -37.2, 0), ("Grow Light Pole", 12, -37.2, 0),
             ("Sprinkler Mast", -21.5, -37, 0), ("Sprinkler Mast", 21.5, -37, 0), ("Soil Heap", -16, -37.5, 0),
             ("Supply Case Stack", 16, -37.3, 0), ("Watering Can", -10, -38.5, 0), ("Flower Pots", 9, -38.5, 0), ("Tool Cart", -22, -41.5, 10),
             ("Ventilation Fan Unit", 15, -43.6, 0), ("Ventilation Fan Unit", -15, -43.6, 0)], s=s)
    B.prop("Dead Vine Curtain", -17, -31.4, yaw=0, s=s)
    B.prop("Dead Vine Curtain", 17, -31.4, yaw=0, s=s)
    B.scatter(["Root Cracks", "global:Moss Patch", "global:Leaf Litter", "global:Rust Stain"], -21, -43, 21, -32, 22, 61 if s > 0 else 62, s=s, gap=2.0)
    for x in (-18, 18):
        B.decal("Canopy Deck Arrow", x * 1.3, -30, 0, s=s, size=1.6, rotation=(0 if x < 0 else 0))
    for (x, z) in ((-14, -37), (14, -37)):
        B.spawn(x, z, None, s=s)


def walkway(s):
    """The collapsed walkway: a railed bridge from the north terrace's east part towards the centre at 4.2 m, its far end
    fallen into a slab of rubble steps down to the deck (a Ramp op under a tilted cosmetic slab)."""
    x0, x1 = -10.5, -7.5
    if s < 0:
        x0, x1 = -x1, -x0
    B.op({"op": "StairsZ", "x0": r(x0), "x1": r(x1), "z": r(-33.25 * s), "dir": 1 * s, "y0": 2.4, "height": 1.8, "color": BRIDGE, "anchor": "world"})
    B.stats["boxes"] += 5
    B.op({"op": "Bridge", "x0": r(x0), "z0": r(-31 * s), "x1": r(x1), "z1": r(-17 * s), "h": 4.2, "color": BRIDGE, "anchor": "world"})
    B.stats["boxes"] += 5
    B.op({"op": "Ramp", "x": r(-9 * s), "z": r(-9 * s), "width": 3, "height": 4.2, "alongX": False, "dir": -1 * s, "color": "#6a6e76", "anchor": "world"})
    B.stats["boxes"] += 9
    B.mesh("box", -9, -13, 2.1, BRIDGE, s=s, width=3.3, height=0.35, depth=9.6, pitch=-25 * s, material="Metal", anchor="world")
    for z in (-30, -27, -24, -21, -18):
        B.waypoint(-9, z, 4.2, s=s)
    for z in (-14, -11):
        B.waypoint(-9, z, 0, s=s)
    B.waypoint(-9, -34.5, 0, s=s)
    B.prop("Station Deck Barrier", -9, -19.5, yaw=0, s=s, y=4.2)
    B.decal("Canopy Deck Arrow", -9, -26, 4.2, s=s, size=1.4, rotation=180 if s > 0 else 0)
    B.scatter(["global:Rubble Dust", "global:Rust Stain", "global:Floor Cracks"], -12, -18, -6, -8, 6, 71 if s > 0 else 72, s=s)
    B.props([("Rubble Pile", -6.2, -11, 0), ("Loose Bricks", -11.5, -9.5, 0), ("Cinder Blocks", -6.5, -16, 20)], s=s)


def centre():
    great_tree()
    B.decal("Root Cracks", 2.5, 2.5, 1.4, size=3.2)
    B.decal("Root Cracks", -2.5, -2.5, 1.4, size=3.2)
    for s in (1, -1):
        B.props([("Planter Trough", -9.5, -6.5, 35), ("Planter Trough", -6.5, 9.5, 125), ("Station Cargo Pod", -11.5, 1.5, 90),
                 ("Soil Heap", -7.5, -9.5, 0), ("Grow Light Pole", -12.5, -5, 0), ("Hydroponic Rack Lit", -2.5, -11.2, 0),
                 ("Dead Vine Curtain", -8, 0, 90), ("Flower Pots", -7.2, 6.8, 0), ("Station Supply Case", -11, 5.5, 30)], s=s)
        B.scatter(["global:Leaf Litter", "global:Moss Patch", "Root Cracks"], -13, -13, 0, 13, 10, 81 if s > 0 else 82, s=s, y=1.4, gap=2.2)
        for (x, z) in ((-17, -17), (-17, 17)):
            B.decal("Canopy Deck Arrow", x, z, 0, s=s, size=1.6, rotation=(45 if z < 0 else 135) + (180 if s < 0 else 0))
        B.spawn(-24, -8, None, s=s)
    # Scaffold towers (tall cover) between the canals and the terraces, and a few more flank pieces.
    for s in (1, -1):
        B.props([("Scaffold Tower", -12, 24, 0), ("Station Deck Barrier", -16, 27, 90), ("Supply Case Stack", -8, 27, 20),
                 ("Twin Gas Tanks", -20, -31, 0), ("Station Pipe Run", -3, -27, 0), ("Oil Drum", -5, -28.5, 0),
                 ("Station Cargo Pod", 4, 24, 0), ("Vent Stack", 8, 28, 0), ("Deck Light Post", -14, 29.5, 0)], s=s)


def foliage():
    outline = ellipse_points(RX - 1.5, RZ - 1.5, 40)
    canal_w = [[-32, -50], [-22, -50], [-20, 0], [-22, 50], [-32, 50], [-30, 0]]
    canal_e = [[-x, -z] for x, z in canal_w]
    items = []
    for (x, z, h) in ((-40, -10, 8), (-36, -17, 7), (-44, -22, 6.5), (-35, 12, 9), (-42, 16, 7), (-30, -8, 6), (-21, -42, 7.5), (-28, -36, 8),
                      (-33, 30, 7), (-18, 36, 6.5), (-6, 38.5, 8.5), (-46, 6, 6), (-15, 20, 7), (-22, 8, 6.5)):
        items.append({"type": "tree", "x": x, "z": z, "height": h})
        items.append({"type": "tree", "x": -x, "z": -z, "height": h})
    return {
        "seed": 7,
        "surfaces": {"Metal": 0, "Concrete": 0, "Tiles": 0},
        "exclude": [{"shape": "circle", "center": [-52, 0], "radius": 11, "density": 0.05}, {"shape": "circle", "center": [52, 0], "radius": 11, "density": 0.05},
                    {"shape": "circle", "center": [0, 0], "radius": 6.5, "density": 0.3}],
        "regions": [
            {"type": "grass", "density": 2.6, "polygon": outline, "color": "#5a7a38", "patchiness": 0.45},
            {"type": "grass", "density": 3.5, "polygon": canal_w, "color": "#6a8a40", "scale": [1.0, 1.5]},
            {"type": "grass", "density": 3.5, "polygon": canal_e, "color": "#6a8a40", "scale": [1.0, 1.5]},
            {"type": "shrub", "density": 0.35, "polygon": canal_w, "seed": 3},
            {"type": "shrub", "density": 0.35, "polygon": canal_e, "seed": 4},
            {"type": "bush", "density": 0.08, "polygon": outline, "seed": 5},
            {"type": "shrub", "density": 0.5, "rect": [-22, -44, 22, -31], "seed": 6},
            {"type": "shrub", "density": 0.5, "rect": [-22, 31, 22, 44], "seed": 7},
            {"type": "bush", "density": 0.25, "rect": [-48, -30, -28, 30], "seed": 8},
            {"type": "bush", "density": 0.25, "rect": [28, -30, 48, 30], "seed": 9},
        ],
        "items": items,
    }


# ================================================================================================ the map
def build():
    B.op({"op": "Ground", "color": "#4a3c30"})
    hull_ribs()
    centre()
    for s in (1, -1):
        airlock_bay(s)
        orchard(s)
        canal(s)
        terrace(s)
        walkway(s)
    # Ground-level litter over the whole deck.
    B.scatter(["global:Leaf Litter", "global:Dry Grass", "global:Dirt Patch", "global:Moss Patch", "global:Rubble Dust"], -58, -44, 58, 44, 60, 91, gap=3.0)
    B.op({"op": "AutoWaypoints", "spacing": 7})
    for (x, z) in ((-24, -37), (24, -37), (-24, 37), (24, 37), (-30, -30), (30, 30), (-26, 0), (26, 0)):
        B.waypoint(x, z)


def main():
    build()
    m = {
        "format": "call-of-slop-map/1",
        "name": "Canopy Deck",
        "description": "The overgrown arboretum of a derelict generation ship: terraces, dry canals and hull ribs under a torn dome.",
        "author": "Call of Slop",
        "order": 607,
        "destructible": False,
        "maxPlayers": 16,
        "skybox": {"preset": "night", "zenith": "#06060f", "horizon": "#141a2a", "ground": "#0a0c14", "stars": 1.8,
                   "sun": {"elevation": 18, "azimuth": 300, "color": "#c8d8ff", "intensity": 0.55},
                   "moon": {"direction": [0.3, 0.55, -0.8], "size": 22, "color": "#d8b090", "intensity": 0.9}, "ambient": 2.2, "clouds": False},
        "boundary": {"shape": "ellipse", "radiusX": RX, "radiusZ": RZ, "noise": {"amplitude": 1.2, "scale": 16, "seed": 7},
                     "edge": {"style": "wall", "height": 10, "thickness": 1.2, "color": HULL2, "material": "Metal"}, "groundMargin": 5},
        "terrain": terrain(),
        "teamSpawns": [B.spawns["blue"], B.spawns["red"]],
        "hills": [{"x": 0, "z": 0, "radius": 6}, {"x": 0, "z": -37, "radius": 5}, {"x": 0, "z": 37, "radius": 5}],
        "flags": [[-55, 0], [55, 0]],
        "foliage": foliage(),
        "ops": B.ops,
    }
    write_map(OUT_JSON, m, B.stats)
    if OUT_PNG:
        plan(OUT_PNG, B.ops, ellipse_points(RX, RZ, 64), size=(140, 110), scale=4)


if __name__ == "__main__":
    main()
