#!/usr/bin/env python3
"""Generator for Maps/Space/06-RegolithReach.json: "Regolith Reach", the Space pack's lunar mining outpost (medium,
16 players, terrain).

    python3 tools/space/regolith_reach.py [repo root] [plan.png]

A regolith mining outpost of the invented Pallas Ore Concern on the floor of a crater: a central excavation pit with two
core drill rigs (hill 1), ore conveyors climbing out of it north and south to the ore docks (hills 2 and 3), a habitat
base at each end (Blue west, Red east: a control hub, a garage hangar, two sealed domes, a glazed tunnel out to an
airlock), a landing pad and a solar farm in opposite quadrants, berms and small craters between, and the crater wall
all round (a terrain `cliff` edge) under a black starry sky. Grey-white regolith with hazard orange and yellow.
Everything is original: no film or game location, ship, company or insignia.

Built as one half (x < 0 and the north ore dock) turned 180 degrees about the centre for the other half, terrain
features included, so both teams get the same ground, cover and walks.
"""
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "06-RegolithReach.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import spacekit_a as K  # noqa: E402
from spacekit_a import block, collider, mesh, mesh_at, prop, clutter, decal, decal_scatter, waypoint, spawn, building, round_collider  # noqa: E402
import keelspar_decals as sdec  # noqa: E402

BASE_Y = 1.2          # the habitat pads
PIT_FLOOR = -3.5
DOCK_Y = 0.4

PALE = "#d8d6cf"
HULL = "#e4e2dc"
HULL2 = "#c9c7c0"
STEEL = "#6c6f73"
DARK = "#34363a"
ORANGE = "#e8701e"
HAZARD = "#f0c040"
GLASS = "#2e4450"
LAMP = "#fff0c8"

FEATURES = []
FLAGS = []


def feat(**f):
    FEATURES.append(f)


def rot_feature(f):
    g = dict(f)
    if "x" in g:
        g["x"], g["z"] = -g["x"], -g["z"]
    if "points" in g:
        g["points"] = [[-p[0], -p[1]] + list(p[2:]) for p in g["points"]]
    return g


# ================================================================================================ terrain
def terrain_half():
    # Shape first: berms (long low ridges that cut the sightlines between the quadrants) and small craters (a rim and
    # a bowl); then the level pads and ramps, so nothing is built on a bump.
    feat(type="ridge", points=[[-20, 16], [-16, 28], [-18, 42]], width=8, height=2.4)
    feat(type="ridge", points=[[-44, -30], [-36, -22], [-34, -14]], width=7, height=2.2)
    feat(type="ridge", points=[[-10, -48], [4, -46]], width=7, height=2.0)
    feat(type="hill", x=-18, z=8, radius=11, height=1.3)
    feat(type="basin", x=-18, z=8, radius=7.5, depth=2.6)
    feat(type="hill", x=-8, z=50, radius=8, height=1.1)
    feat(type="basin", x=-8, z=50, radius=5, depth=2.0)
    feat(type="hill", x=-46, z=-44, radius=8, height=1.4)
    feat(type="basin", x=-46, z=-44, radius=5, depth=1.6)
    # The habitat pad (west), the tunnel strip, the survey hab pad.
    feat(type="flatten", x=-52, z=0, radius=21, height=BASE_Y, edge=5)
    feat(type="ramp", points=[[-52, -10, BASE_Y], [-26, -10, BASE_Y]], width=7, edge=3)
    feat(type="flatten", x=-21, z=-24, w=20, d=20, height=0.9, edge=2)
    # Solar farm pad (north-west) and the landing pad (south-west).
    feat(type="flatten", x=-26, z=-43, w=32, d=16, height=0.5, edge=3)
    feat(type="flatten", x=-32, z=30, radius=12, height=0.6, edge=3)
    # The north ore dock pad and the conveyor's ramp up out of the pit.
    feat(type="flatten", x=0, z=-33, w=18, d=16, height=DOCK_Y, edge=3)
    feat(type="ramp", points=[[0, -27, DOCK_Y], [0, -8, PIT_FLOOR]], width=5, edge=3, material="dirt")
    # The west vehicle ramp into the pit.
    feat(type="ramp", points=[[-28, 0], [-9, 0, PIT_FLOOR]], width=7, edge=3, material="dirt")
    # Rover tracks (paint only) from the base to the pit and round to the pads.
    feat(type="ramp", points=[[-44, 4], [-30, 2]], width=3, edge=1, material="dirt", mode="fill")
    feat(type="ramp", points=[[-40, 10], [-36, 24], [-34, 30]], width=3, edge=1, material="dirt", mode="fill")


def terrain():
    feats = [{"type": "noise", "amplitude": 0.55, "scale": 16, "seed": 12}]
    # The excavation: a bowl with a level floor (hill 1 and the drill rigs), self-symmetric.
    feats.append({"type": "basin", "x": 0, "z": 0, "radius": 20, "depth": -PIT_FLOOR, "falloff": "smooth"})
    feats.append({"type": "flatten", "x": 0, "z": 0, "radius": 9, "height": PIT_FLOOR, "edge": 4})
    terrain_half()
    for f in list(FEATURES):
        feats.append(f)
    for f in list(FEATURES):
        feats.append(rot_feature(f))
    return {
        "cell": 1,
        "margin": 26,
        "surface": {"grass": "#c4c2ba", "dirt": "#aba89f", "rock": "#8f8c86", "sand": "#d6d4cc", "dirtSlope": 22, "rockSlope": 36, "variation": 0.08},
        "features": feats,
    }


# ================================================================================================ the half
def tunnel(x0, x1, z, w=3.0):
    """A glazed pressurised tunnel along X: knee-to-waist wall, open window band, a lintel band and a roof, mullions
    every 2.2 m; cover you can shoot through the windows of, and mantle into."""
    for s in (-1, 1):
        zz = z + s * (w / 2 + 0.15)
        block(x0, 0, zz - 0.15, x1, 1.1, zz + 0.15, HULL)
        block(x0, 2.2, zz - 0.15, x1, 2.9, zz + 0.15, HULL)
        n = max(1, int(round((x1 - x0) / 2.2)))
        for k in range(n + 1):
            x = x0 + (x1 - x0) * k / n
            mesh("box", x, zz, 1.05, STEEL, width=0.2, height=1.2, depth=0.34)
    block(x0 - 0.2, 2.9, z - w / 2 - 0.5, x1 + 0.2, 3.25, z + w / 2 + 0.5, HULL2)
    mesh_at("tube", (x0 + x1) / 2, 3.4, z, HULL, roll=90, radius=w / 2 + 0.55, inner=w / 2 + 0.2, height=x1 - x0 + 0.4, segments=24, material="Metal")
    for k in range(int((x1 - x0) / 4)):
        mesh("box", x0 + 2 + k * 4, z, 2.8, LAMP, width=0.6, height=0.06, depth=0.5)
    for x in range(int(x0) + 2, int(x1), 4):
        waypoint(x, z)


def dome(x, z, rad, color=HULL):
    """A sealed habitat dome (nobody inside): a hemisphere over a solid round core, a door ring and a lamp."""
    mesh("sphere", x, z, -rad * 0.98, color, width=2 * rad, height=2 * rad, depth=2 * rad, segments=32, detail=12, material="Metal")
    mesh("tube", x, z, 0, HULL2, radius=rad + 0.3, inner=rad - 0.2, height=0.5, segments=32)
    mesh("cylinder", x, z, rad - 0.4, STEEL, width=1.2, height=0.9, depth=1.2, segments=12)
    round_collider(x, z, rad * 0.92, 0, rad - 0.2)


def west_base():
    cx, cz = -52.0, 0.0
    building("Station Control Hub", -58, 0, -10, 90, 661, variant={"palette": "white hull"})
    building("Station Hangar Bay", -56, 0, 16, 90, 662, variant={"palette": "white hull"})
    names, tw, th = sdec.tiles("hab_west")
    sign_row(names, tw, -51.3, -10, 3.1, "wall-x", along_z=True)
    names, tw, th = sdec.tiles("garage")
    sign_row(names, tw, -48.3, 16, 6.15, "wall-x", along_z=True)
    dome(-44, -26, 5.0)
    dome(-62, 30, 4.2)
    tunnel(-49, -29, -10)
    FLAGS.append([-58.0, BASE_Y, 2.0])
    decal("Pallas Ore Logo", -58, 2, size=4.0, rotation=90)
    for (x, z) in [(-66, -6), (-66, 8), (-50, -22), (-58, -22), (-44, -14), (-46, -4), (-46, 6), (-44, 18), (-52, 30), (-58, 30), (-66, 24), (-64, -20)]:
        spawn(0, x, z)
    for (n, x, z, yw) in [("Hab Airlock Module", -38, -24, 90), ("Hab Airlock Module", -27, -13.5, 180), ("Hab Airlock Module", -58, 26, 0),
                          ("Lunar Rover", -44, 10, 20), ("Lunar Rover", -46, -30, 100), ("Propellant Tank", -66, 16, 90), ("Oxygen Tank Bank", -34, -20, 0),
                          ("Station Cargo Pod", -48, 24, 0), ("Station Cargo Pod", -40, 0, 90), ("Supply Case Stack", -42, -8, 0), ("Sample Crate", -41, 4, 30),
                          ("Sample Crate", -40, -6, 0), ("Antenna Mast", -68, -14, 0), ("Sensor Dish", -66, 0, 90), ("Station Console", -46, 20, 270),
                          ("Deck Light Post", -50, -24, 0), ("Deck Light Post", -50, 28, 0), ("Deck Light Post", -40, 14, 0), ("Survey Light", -33, -6, 0),
                          ("Survey Light", -33, -14, 0), ("Work Light Mast", -45, -20, 90), ("Station Pipe Run", -54, -24, 0), ("Station Pipe Run", -36, 8, 90),
                          ("Station Deck Barrier", -42, -12, 0), ("Station Deck Barrier", -44, 0, 90), ("Regolith Hopper", -36, 16, 0), ("Lunar Boulder", -38, 26, 0),
                          ("Ventilation Fan Unit", -52, -20, 0), ("Propellant Tank", -66, -24, 0), ("Caged Bulkhead Lamp", -29.3, -8.2, 0)]:
        prop(n, x, z, 0, yw)
    names, tw, th = sdec.tiles("airlock")
    sign_row(names, tw, -27, -12.1, 2.3, "wall+z")
    decal("Regolith Hab Number 1", -62, -1.5, 1.4, onto="wall-z", size=1.4, reach=2.0)
    decal("global:Hazard Stripes", -46.5, -10, size=2.8, rotation=90)
    decal("global:Hazard Stripes", -31.5, -10, size=2.8, rotation=90)
    decal_scatter(["Regolith Scuff", "Regolith Scuff", "global:Footprints", "Regolith Dust Spray"], -70, -30, -34, 32, 40, seed=21, gap=1.5)
    decal_scatter(["global:Tyre Tracks"], -48, -10, -30, 30, 8, seed=22, gap=4.0)
    for (x, z) in ((-50, -4), (-50, 6), (-46, 14), (-46, -18), (-66, 14), (-66, -2), (-62, -22), (-60, 32), (-40, -10), (-30, -6)):
        waypoint(x, z)


def sign_row(names, tw, cx, zc, y, onto, along_z=False):
    n = len(names)
    for i, nm in enumerate(names):
        off = (i - (n - 1) / 2) * tw
        if along_z:
            # A sign on a wall facing +X (onto wall-x from a point east of it): the viewer looks west, right is -Z.
            decal(nm, cx, zc - off, y, onto=onto, size=tw, reach=2.0)
        else:
            x = cx + off if onto == "wall-z" else cx - off
            decal(nm, x, zc, y, onto=onto, size=tw, reach=2.0)


def survey_hab():
    building("Station Crew Quarters", -20, 0, -24, 0, 631, variant={"palette": "white hull"})
    names, tw, th = sdec.tiles("drill_site")
    sign_row(names, tw, -20, -18.4, 2.75, "wall-z")
    decal("Regolith Hab Number 2", -16.5, -18.4, 1.5, onto="wall-z", size=1.3, reach=2.5)
    for (n, x, z, yw) in [("Hab Airlock Module", -28, -20, 90), ("Oxygen Tank Bank", -12, -31, 0), ("Lunar Rover", -8, -22, 80),
                          ("Station Server Cabinet", -26, -30, 0), ("Supply Case Stack", -14, -30.5, 0), ("Sample Crate", -10, -18, 0),
                          ("Deck Light Post", -28, -31, 0), ("Survey Light", -9, -27, 0), ("Station Deck Barrier", -12, -16, 0)]:
        prop(n, x, z, 0, yw)
    decal_scatter(["Regolith Scuff", "global:Footprints", "Regolith Survey Mark"], -28, -32, -10, -16, 14, seed=23, gap=1.5)
    for (x, z) in ((-26, -17), (-14, -17), (-26, -31), (-12, -29)):
        waypoint(x, z)


def solar_farm():
    for row, z in enumerate((-49, -45, -41, -37)):
        for k in range(5):
            x = -38 + k * 5.2 + (2.6 if row % 2 else 0)
            prop("Solar Array Rack", x, z, 0, 0)
    for (n, x, z, yw) in [("Pad Transformer", -41, -35, 0), ("Street Cabinet", -41, -38, 90), ("Station Server Cabinet", -10, -37, 180),
                          ("Lunar Boulder", -41, -47, 0), ("Survey Light", -11, -44, 0), ("Survey Light", -41, -40, 0), ("Sample Crate", -24, -31, 20)]:
        prop(n, x, z, 0, yw)
    names, tw, th = sdec.tiles("solar_farm")
    sign_board(names, tw, -24, -33.5, 1.6, "wall+z")
    decal_scatter(["Regolith Scuff", "Regolith Dust Spray", "global:Footprints"], -40, -48, -10, -31, 16, seed=24, gap=1.5)
    for x in (-36, -30, -24, -18, -12):
        for z in (-47, -43, -39):
            waypoint(x + 1.3 - (1.3 if z == -43 else 0), z)


def sign_board(names, tw, cx, z, y, onto):
    """A free-standing sign board (a dark panel on two posts) with the tiles on its face."""
    n = len(names)
    w = tw * n
    mesh("box", cx, z, y - 0.45, DARK, width=w + 0.2, height=0.9, depth=0.1)
    for s in (-1, 1):
        mesh("cylinder", cx + s * (w / 2 + 0.2), z, 0, STEEL, width=0.1, height=y + 0.5, depth=0.1, segments=8)
    for i, nm in enumerate(names):
        off = (i - (n - 1) / 2) * tw
        x = cx + off if onto == "wall-z" else cx - off
        decal(nm, x, z - 0.6 if onto == "wall+z" else z + 0.6, y, onto=onto, size=tw, reach=1.5)


def landing_pad():
    cx, cz = -32, 30
    decal("Regolith Landing Ring", cx, cz, 0, size=16, rotation=0)
    prop("Lunar Lander", cx, cz, 0, 0)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        prop("Deck Floor Light", cx + 9.2 * math.cos(a), cz + 9.2 * math.sin(a), 0, 0)
    for (n, x, z, yw) in [("Fuel Coupling", -22, 36, 270), ("Landing Beacon", -22, 24, 0), ("Landing Beacon", -42, 36, 0), ("Station Cargo Pod", -20, 30, 90),
                          ("Supply Case Stack", -42, 24, 0), ("Hover Cargo Sled", -24, 42, 90), ("Lunar Boulder", -44, 42, 0), ("Hazard Bollard", -26, 20, 0),
                          ("Hazard Bollard", -38, 20, 0)]:
        prop(n, x, z, 0, yw)
    names, tw, th = sdec.tiles("dust_hazard")
    sign_board(names, tw, -32, 19.0, 1.6, "wall+z")
    decal_scatter(["Regolith Dust Spray", "Regolith Dust Spray", "Regolith Scuff"], -42, 20, -22, 40, 18, seed=25, gap=1.2)
    for (x, z) in ((-38, 24), (-26, 24), (-38, 36), (-26, 36), (-20, 30)):
        waypoint(x, z)


def ore_dock_north():
    """The north ore dock (hill 2): hoppers, a conveyor climbing out of the pit, crates and a sign."""
    # One continuous belt from the pit floor up the ramp to the dock: four sloped sections, each pitched to the ramp's
    # straight profile (anchor "world": y is the ground height at the section's centre, so none floats or sinks).
    run = 4.75
    for k in range(4):
        zc = -8 - run * (k + 0.5)
        prop("Ore Conveyor Slope", 1.6, zc, PIT_FLOOR + (-8 - zc) / 19.0 * (DOCK_Y - PIT_FLOOR), 90, anchor="world")
    for (n, x, z, yw) in [("Regolith Hopper", -8, -36, 0), ("Regolith Hopper", 8, -36, 0), ("Ore Bin Hopper", 0, -39, 0), ("Ore Mine Cart", -9, -31, 90),
                          ("Blasting Explosives Crate", 9, -29, 0), ("Blasting Warning Sign", 7, -26.5, 180), ("Mine Floodlight Mast", -10, -29, 0),
                          ("Sample Crate", -7, -27, 0), ("Sample Crate", -8, -27.8, 40), ("Station Deck Barrier", 6, -32, 90), ("Lunar Boulder", 12, -34, 0)]:
        prop(n, x, z, 0, yw)
    names, tw, th = sdec.tiles("ore_dock")
    sign_board(names, tw, 0, -41.5, 1.6, "wall-z")
    names, tw, th = sdec.tiles("blast_zone")
    sign_board(names, tw, 12, -30, 1.4, "wall+z")
    for z in (-12, -18, -24):
        decal("Regolith Ore Arrow", 0, z, 0, size=1.6, rotation=0)
    decal_scatter(["Regolith Scuff", "Regolith Scuff", "global:Tyre Tracks", "Regolith Survey Mark"], -11, -40, 11, -26, 16, seed=26, gap=1.4)
    for (x, z) in ((-6, -33), (6, -33), (0, -29), (0, -37), (-2, -16), (-2, -22), (0, -10)):
        waypoint(x, z)


def pit_half():
    """The west half of the excavation: a drill rig, the vehicle ramp's edge cover, boulders."""
    prop("Core Drill Rig", -6, -6, 0, 0)
    for (n, x, z, yw) in [("Ore Conveyor Section", -12, 4, 0), ("Ore Mine Cart", -14, -3, 0), ("Sample Crate", -10, -1, 20), ("Lunar Boulder", -15, 8, 0),
                          ("Blasting Explosives Crate", -5, 10, 0), ("Oxygen Tank Bank", -2, -12, 0), ("Survey Light", -12, -8, 0),
                          ("Station Supply Case", -8, 7, 0), ("Mine Timber Stack", -17, 0, 90), ("Tracked Excavator", -24, -6, 150)]:
        prop(n, x, z, 0, yw)
    decal_scatter(["Regolith Scuff", "Regolith Survey Mark", "global:Tyre Tracks", "Regolith Dust Spray"], -18, -14, 0, 14, 16, seed=27, gap=1.4)
    for (x, z) in ((-6, 2), (-12, -2), (-20, 0), (-4, -10), (-8, 10)):
        waypoint(x, z)


def midfield():
    """Berm pockets and craters between the base and the pit."""
    for (n, x, z, yw) in [("Lunar Boulder", -26, 8, 0), ("Lunar Boulder", -28, 18, 0), ("Lunar Boulder", -36, -36, 0), ("Lunar Rover", -30, 22, 60),
                          ("Station Cargo Pod", -24, 12, 30), ("Oxygen Tank Bank", -22, 44, 0), ("Sample Crate", -18, 4, 0), ("Survey Light", -26, 10, 0),
                          ("Survey Light", -12, 46, 0), ("Regolith Hopper", -28, -22, 0), ("Station Pipe Run", -30, 10, 0), ("Lunar Boulder", -4, 44, 0),
                          ("Mine Floodlight Mast", -24, 2, 0), ("Hazard Bollard", -10, 30, 0), ("Hazard Bollard", -4, 36, 0)]:
        prop(n, x, z, 0, yw)
    decal_scatter(["Regolith Survey Mark", "Regolith Scuff", "global:Footprints"], -40, 0, -4, 46, 14, seed=28, gap=2.0)
    for (x, z) in ((-18, 10), (-30, 14), (-26, 36), (-12, 44), (-40, 20), (-20, 18)):
        waypoint(x, z)


# ================================================================================================ turning the half
def boundary_points():
    # The west half of the outline, north to south (a little irregular); the east half is it turned.
    half = [(0, -60), (-16, -58), (-32, -57), (-48, -51), (-60, -42), (-68, -30), (-73, -16), (-75, 0), (-73, 14), (-70, 27),
            (-62, 41), (-52, 50), (-38, 55), (-20, 60)]
    full = list(half) + [(-x, -z) for (x, z) in half]  # point-symmetric: the second half is the first turned
    return [[x, z] for (x, z) in full]


def build():
    west_base()
    survey_hab()
    solar_farm()
    landing_pad()
    ore_dock_north()
    pit_half()
    midfield()
    half = list(K.OPS)
    other = K.rotated(half)
    K.OPS.clear()
    K.OPS.extend(half + other)
    K.SPAWNS[1] = [[K.r(-x), y, K.r(-z)] for (x, y, z) in K.SPAWNS[0]]
    FLAGS.append([K.r(-FLAGS[0][0]), FLAGS[0][1], K.r(-FLAGS[0][2])])
    # Shared: the east-west sign on the pit rim, mirrored clutter (boulders and rubble everywhere, kept off the pads).
    clutter(["Lunar Boulder", "Lunar Boulder Small", "Lunar Boulder Small", "Lunar Rubble", "Lunar Rubble", "Regolith Bag"], -70, -58, 0, 58, 140, seed=31, mirror=True)
    clutter(["Lunar Rubble", "Lunar Boulder Small"], -70, -58, 0, 58, 120, seed=32, mirror=True)
    clutter(["Sample Crate", "Rivet Crate", "Station Supply Case", "Regolith Bag"], -46, -30, -8, 30, 16, seed=33, mirror=True)
    op = {"op": "AutoWaypoints", "spacing": 7}
    K.OPS.append(op)


def main():
    K.reset()
    build()
    m = {
        "format": "call-of-slop-map/1",
        "name": "Regolith Reach",
        "description": "A regolith mining outpost on a crater floor: an excavation pit with drill rigs, ore docks, habitat domes and tunnels, a landing pad and a solar farm under the stars.",
        "author": "Call of Slop",
        "order": 606,
        "destructible": False,
        "maxPlayers": 16,
        "skybox": {"preset": "night", "zenith": "#020308", "horizon": "#06070e", "ground": "#0c0d12", "stars": 2.0, "clouds": False,
                   "sun": {"elevation": 24, "azimuth": 150, "intensity": 1.25, "color": "#fff6e8", "size": 1.0, "glow": 0.2},
                   "moon": {"direction": [-0.6, 0.35, -0.7], "size": 9, "color": "#b9c6d8", "intensity": 0.2},
                   "ambient": 0.8, "fog": {"max": 0.3}},
        "boundary": {"shape": "smooth", "points": boundary_points(), "edge": {"style": "cliff", "height": 10, "width": 8}, "groundMargin": 10},
        "terrain": terrain(),
        "teamSpawns": [K.SPAWNS[0], K.SPAWNS[1]],
        "hills": [{"x": 0, "z": 0, "radius": 5.5}, {"x": 0, "z": -31, "radius": 4.5}, {"x": 0, "z": 31, "radius": 4.5}],
        "flags": [[FLAGS[0][0], FLAGS[0][2]], [FLAGS[1][0], FLAGS[1][2]]],
        "ops": K.OPS,
    }
    K.write_map(OUT_JSON, m)
    if OUT_PNG:
        plan(OUT_PNG)


def plan(path):
    from PIL import Image, ImageDraw
    S = 4
    W, H = 160 * S, 130 * S
    img = Image.new("RGB", (W, H), (20, 20, 30))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * S, H / 2 + z * S)
    d.polygon([P(x, z) for x, z in boundary_points()], outline=(120, 120, 160))
    for f in terrain()["features"]:
        if f["type"] in ("flatten", "hill", "basin") and "radius" in f:
            r = f["radius"]
            d.ellipse([P(f["x"] - r, f["z"] - r), P(f["x"] + r, f["z"] + r)], outline=(90, 90, 120))
        if f["type"] in ("ridge", "ramp"):
            d.line([P(p[0], p[1]) for p in f["points"]], fill=(140, 110, 80), width=3)
    for o in K.OPS:
        if o["op"] == "Block":
            d.rectangle([P(o["x0"], o["z0"]), P(o["x1"], o["z1"])], fill=(170, 170, 160))
        if o["op"] == "Building":
            x, z = P(o["at"][0], o["at"][-1])
            d.rectangle([x - 6 * S, z - 5 * S, x + 6 * S, z + 5 * S], outline=(255, 120, 255))
    for o in K.OPS:
        if o["op"] == "Waypoint":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 2, z - 2, x + 2, z + 2], fill=(80, 200, 255))
        if o["op"] == "Spawn":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 3, z - 3, x + 3, z + 3], fill=(80, 255, 80) if o["x"] < 0 else (255, 80, 80))
        if o["op"] == "Prop":
            x, z = P(o["x"], o["z"])
            d.rectangle([x - 2, z - 2, x + 2, z + 2], fill=(255, 200, 60))
    img.save(path)
    print("plan:", path)


if __name__ == "__main__":
    main()
