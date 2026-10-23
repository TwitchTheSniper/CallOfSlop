#!/usr/bin/env python3
"""Generator for Maps/Space/08-IgnitionHall.json: "Ignition Hall", the containment hall of the Halvard Fusion Works'
tokamak (Space pack, medium-large, 16 players).

    python3 tools/space/reactor.py [repo root] [plan.png]

An octagonal hall round a central containment vessel, with a turbine wing on the west (Blue) and east (Red) sides.
Concentric rings out from the middle: the core plinth (a 0.5 m step, hill 1) with the plasma column rising from it; the
containment torus round it (solid, 0.5-3.5 m: you only get through at its four diagonal injector gaps); the field coil
blocks on the axes outside the torus; four glowing coolant mains running out along the axes (waist-high conduit you can
mantle, with a gap half way) that split the hall into quadrants; pump bays, cryo tanks and junk in the quadrants; the two
control galleries along the north and south walls (two storeys of slabs on pillars with stairs at both ends; hills 2
and 3 on the lower galleries) and the catwalks from their upper decks over the torus to a square walkway round the
column; the turbine wings with the blast door portals at their mouths. Point-symmetric about the centre: everything
Blue has at (x, z) Red has at (-x, -z). Orange and blue palette, hazard dressing and warning decals throughout.
"""
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "08-IgnitionHall.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
from spacekit import Builder, write_map, plan, r  # noqa: E402

R = 53.0                     # the octagon's circumradius; its flat sides are at R cos 22.5 = 48.97
FLAT = R * math.cos(math.radians(22.5))
WING_X = 74.0
WING_Z = 13.0
CHAMFER = 4.5                # the wings' far corners are cut off, so the horizon fade (Map.OuterStartsFor) starts close past the hall's floor
FLOOR = "#3a3d44"
STEEL = "#6c7078"
DARK = "#2c2e34"
SLAB = "#5a5e68"
RAIL = "#ff8a2a"
ORANGE = "#ff8a2a"
BLUE = "#4ac8ff"
PLASMA = "#9ae8ff"
HULL = "#4a4e58"
COPPER = "#b0683a"

B = Builder()


def outline():
    a = [math.radians(22.5 + 45 * k) for k in range(8)]
    oct_pts = [(R * math.cos(t), R * math.sin(t)) for t in a]
    pts = []
    for i, (x, z) in enumerate(oct_pts):
        pts.append((x, z))
        if i == 3:   # after (-FLAT, +19.1): the west wing, going round clockwise (z decreasing on the far side)
            pts += [(-FLAT, WING_Z), (-WING_X + CHAMFER, WING_Z), (-WING_X, WING_Z - CHAMFER), (-WING_X, -WING_Z + CHAMFER), (-WING_X + CHAMFER, -WING_Z), (-FLAT, -WING_Z)]
        if i == 7:   # after (FLAT, -19.1): the east wing
            pts += [(FLAT, -WING_Z), (WING_X - CHAMFER, -WING_Z), (WING_X, -WING_Z + CHAMFER), (WING_X, WING_Z - CHAMFER), (WING_X - CHAMFER, WING_Z), (FLAT, WING_Z)]
    return [[r(x), r(z)] for x, z in pts]


# ================================================================================================ the core
def core():
    # The plinth: a 0.5 m step, hidden collider strips under a cosmetic cylinder.
    Rp = 6.0
    edges = [0.0, 1.2, 2.4, 3.6, 4.6, 5.4, 5.9, Rp]
    for (a, b) in zip(edges[:-1], edges[1:]):
        half = math.sqrt(max(0.0, Rp * Rp - b * b))
        if half < 0.3:
            continue
        B.collider(-half, 0, a, half, 0.5, b)
        B.collider(-half, 0, -b, half, 0.5, -a)
    B.mesh("cylinder", 0, 0, 0, SLAB, width=12.1, height=0.52, depth=12.1, segments=32, material="Tiles")
    B.mesh("tube", 0, 0, 0.52, ORANGE, radius=6.05, inner=5.7, height=0.08, segments=32)
    # The plasma column: a solid pillar, bright core, rings up it.
    B.collider(-0.8, 0, -0.8, 0.8, 10.5, 0.8)
    B.mesh("cylinder", 0, 0, 0.5, PLASMA, width=1.6, height=10.0, depth=1.6, segments=16)
    B.mesh("cylinder", 0, 0, 0.5, DARK, width=2.4, height=0.6, depth=2.4, segments=16, material="Metal")
    for y in (2.4, 4.6, 6.8, 9.0):
        B.mesh("torus", 0, 0, y, COPPER, radius=1.15, inner=0.22, segments=24, detail=10)
    B.mesh("cylinder", 0, 0, 10.5, DARK, width=2.6, height=0.8, depth=2.6, segments=16, material="Metal")
    # The containment torus: a cosmetic ring over 24 hidden collider segments, with gaps at the four diagonals.
    Rt, tube = 8.5, 1.5
    B.mesh("torus", 0, 0, 0.5, STEEL, radius=Rt, inner=tube, segments=48, detail=16, material="Metal")
    B.mesh("torus", 0, 0, 1.9, BLUE, radius=Rt + tube * 0.92, inner=0.12, segments=48, detail=8)
    n = 24
    for k in range(n):
        t = 2 * math.pi * (k + 0.5) / n
        deg = math.degrees(t) % 90
        if abs(deg - 45) < 9.5:
            continue  # the injector gaps on the diagonals
        cx, cz = Rt * math.cos(t), Rt * math.sin(t)
        hw = Rt * math.pi / n * 1.05
        # An axis-aligned box round the segment's chord: the extents along x and z of a chord of half-length hw across the ring.
        ex = abs(hw * math.sin(t)) + tube * abs(math.cos(t)) * 0.92
        ez = abs(hw * math.cos(t)) + tube * abs(math.sin(t)) * 0.92
        B.collider(cx - ex, 0.4, cz - ez, cx + ex, 3.5, cz + ez)
    for k in range(4):
        t = math.radians(45 + 90 * k)
        x, z = 8.5 * math.cos(t), 8.5 * math.sin(t)
        B.prop("Warning Beacon Post", x * 1.3, z * 1.3 + (1.9 if k % 2 == 0 else -1.9), yaw=0)
        B.decal("Plasma Hazard Glyph", x, z, 0.0, size=1.2, rotation=0)
    B.decal("Ignition Core Ring", 0, 0, 0.5, size=16, rotation=0)
    B.decal("Halvard Logo", 0, 3.2, 0.5, size=2.2, rotation=0)
    for k in range(4):
        t = math.radians(90 * k)
        B.prop("Magnet Coil Block", 12.3 * math.cos(t), 12.3 * math.sin(t), yaw=90 * k + 90)
        B.decal("Ignition Heat Scorch", 12.3 * math.cos(t), 12.3 * math.sin(t), 0, size=5)
    for k in range(8):
        t = math.radians(22.5 + 45 * k)
        B.prop("Caged Bulkhead Lamp", 6.8 * math.cos(t), 6.8 * math.sin(t), yaw=math.degrees(t) + 90, y=4.2)


def mains(s):
    """The west coolant main (its twin is the east one) and the north one (its twin the south one): chains of conduit
    along the axes with a gap half way and a manifold beside it."""
    for x in (-15, -19, -31, -35):
        B.prop("Plasma Conduit", x, 0, yaw=0, s=s)
    B.prop("Coolant Manifold", -25, 2.6, yaw=0, s=s)
    B.prop("Hazard Chain", -25, -1.4, yaw=0, s=s)
    B.decal("global:Hazard Stripes", -25, 0, 0, s=s, size=3.2, rotation=0)
    for z in (-15, -19, -31, -35):
        B.prop("Plasma Conduit", 0, z, yaw=90, s=s)
    B.prop("Coolant Manifold", 2.6, -25, yaw=90, s=s)
    B.prop("Hazard Chain", -1.4, -25, yaw=90, s=s)
    B.decal("global:Hazard Stripes", 0, -25, 0, s=s, size=3.2, rotation=90)
    B.decal("Ignition Guide Line", -27, 1.6, 0, s=s, size=6, rotation=0)
    B.decal("Ignition Guide Line", 1.6, -27, 0, s=s, size=6, rotation=90)


# ================================================================================================ galleries
def gallery(s):
    """The north control gallery (its twin is the south one): a ground-floor control room under a 3.5 m deck on pillars,
    a smaller 6.7 m observation deck above, stairs at both ends, parapets, and the catwalk out over the torus."""
    z0, z1 = -46.5, -34.0
    for x in (-14, -7, 0, 7, 14):
        B.block(x - 0.4, 0, z1 - 1.2, x + 0.4, 3.2, z1 - 0.4, STEEL, s=s)
    B.block(-16, 3.2, z0, 16, 3.5, z1, SLAB, s=s)                                          # level 1 deck
    B.block(-16, 3.5, z1 - 0.2, 16, 4.5, z1, DARK, s=s)                                    # its parapet
    B.block(-10, 6.4, -46.5, 10, 6.7, -40, SLAB, s=s)                                      # level 2 deck
    for x0, x1 in ((-10, -1), (1, 10)):
        B.block(x0, 6.7, -40.2, x1, 7.7, -40, DARK, s=s)
    B.block(-16, 0, -47.4, 16, 9.5, -47.0, HULL, s=s)                                     # the back wall
    # Stairs: floor to level 1 at both ends (climbing in from outside the deck), level 1 to level 2 at the west end.
    for sx in (-1, 1):
        B.op({"op": "StairsX", "z0": r(min(-43.0 * s, -40.5 * s)), "z1": r(max(-43.0 * s, -40.5 * s)), "x": r(sx * 20.1 * s), "dir": -sx * s, "y0": 0, "height": 3.5, "color": SLAB})
        B.stats["boxes"] += 9
    B.op({"op": "StairsX", "z0": r(min(-46.0 * s, -43.5 * s)), "z1": r(max(-46.0 * s, -43.5 * s)), "x": r(-13.6 * s), "dir": 1 * s, "y0": 3.5, "height": 3.1, "color": SLAB})
    B.stats["boxes"] += 8
    # Rails on the open ends of the level 1 deck beyond the stairs, so nobody wanders off the side.
    B.block(-16.2, 3.5, z0, -16.0, 4.5, -43.2, DARK, s=s)
    B.block(16.0, 3.5, z0, 16.2, 4.5, -43.2, DARK, s=s)
    B.block(-16.2, 3.5, -40.3, -16.0, 4.5, z1, DARK, s=s)
    B.block(16.0, 3.5, -40.3, 16.2, 4.5, z1, DARK, s=s)
    # The catwalk from level 2 south over the torus to the walkway round the column.
    # Built from blocks (the Bridge op would stand a pillar in the middle of the gallery's hill): a deck, two rails, and
    # two thin supports that stand on the north coolant main's conduits.
    B.block(-1.0, 6.2, -40.0, 1.0, 6.6, -5.0, SLAB, s=s)
    B.block(-1.0, 6.6, -40.0, -0.85, 7.6, -5.0, RAIL, s=s)
    B.block(0.85, 6.6, -40.0, 1.0, 7.6, -5.0, RAIL, s=s)
    for z in (-31.0, -19.0):
        B.block(-0.2, 0, z - 0.2, 0.2, 6.2, z + 0.2, STEEL, s=s)
    for z in (-37, -33, -29, -25, -21, -17, -13, -9, -6):
        B.waypoint(0, z, 6.6, s=s)
    B.waypoint(0, -42, 6.7, s=s)
    B.waypoint(-12, -43, 6.7, s=s)
    B.waypoint(12, -38, 3.5, s=s)
    B.waypoint(-12, -38, 3.5, s=s)
    # Signs on the back wall, lamps, tube lights under the decks.
    face = "wall-z" if s > 0 else "wall+z"
    B.sign("containment", 0, -46.3 * s, 8.3, face, s=1)
    B.sign("gallery", 0, -46.3 * s, 5.4, face, s=1)
    B.sign("field", -10 * s, -46.3 * s, 2.0, face, s=1)
    B.sign("clear", 10 * s, -46.3 * s, 2.0, face, s=1)
    B.decal("Halvard Logo", 0, -46.3, 1.6, onto="wall-z", s=s, size=2.0)
    B.decal("Plasma Hazard Glyph", -14.5, -46.3, 2.2, onto="wall-z", s=s, size=1.0)
    B.decal("Plasma Hazard Glyph", 14.5, -46.3, 2.2, onto="wall-z", s=s, size=1.0)
    for x in (-12, -4, 4, 12):
        B.prop("Tube Light", x, -39, yaw=0, s=s, y=2.9)
        B.prop("Emergency Strip Lamp", x, -46.9, yaw=0, s=s, y=4.1)
    # Ground floor: the control room.
    B.props([("Console Bank", -8, -42.5, 0), ("Console Bank", 8, -42.5, 0), ("Reactor Control Desk", 0, -42.6, 0),
             ("Tape Mainframe", -14.5, -45.8, 0), ("Tape Mainframe", 14.5, -45.8, 0), ("Patch Panel Cabinet", -12, -46.4, 0),
             ("Station Server Cabinet", 12, -46.4, 0), ("Office Chair", -8, -41.2, 180), ("Office Chair", 7, -41.2, 170),
             ("Tool Cart", 3.5, -36.5, 30), ("Supply Case Stack", -4, -36.3, 0), ("Status Board", -6, -46.6, 0, 1.3),
             ("Status Board", 6, -46.6, 0, 1.3)], s=s)
    # Level 1: consoles behind the parapet, the hill in the middle.
    B.props([("Console Bank", -9, -35.5, 180, 3.5), ("Console Bank", 9, -35.5, 180, 3.5), ("Station Console", -14, -42, 0, 3.5),
             ("Station Console", 14, -42, 0, 3.5), ("Big Wall Display", 0, -46.6, 0, 4.3), ("Hazmat Suit Rack", -7, -46.4, 0, 3.5),
             ("Steel Rifle Rack", 7, -46.4, 0, 3.5), ("Office Chair", -8, -37.2, 0, 3.5), ("Cryo Dewar", 12, -45.2, 0, 3.5)], s=s)
    # Level 2: the observation deck.
    B.props([("Launch Control Console", 0, -41.6, 180, 6.7), ("Station Deck Barrier", -6, -41.2, 0, 6.7), ("Station Deck Barrier", 6, -41.2, 0, 6.7),
             ("Sensor Dish", -7.5, -44.5, 0, 6.7), ("Station Supply Case", 7, -44.5, 20, 6.7)], s=s)
    B.decal("Ignition Floor Grating", 0, -39, 3.5, s=s, size=3)
    B.scatter(["Ignition Floor Grating"], -15, -44, 15, -34, 12, 101 if s > 0 else 102, s=s, y=3.5, gap=2.8)
    B.scatter(["global:Oil Stain", "global:Rust Stain", "global:Scorch Mark", "global:Floor Cracks"], -15, -46, 15, -35, 10, 103 if s > 0 else 104, s=s)
    B.decal("Ignition Sector A" if s > 0 else "Ignition Sector C", 0, -31.5, 0, s=s, size=2.2, rotation=0 if s > 0 else 180)
    B.decal("Ignition Guide Line", 18, -38, 0, s=s, size=6, rotation=90)
    B.decal("Ignition Guide Line", -18, -38, 0, s=s, size=6, rotation=90)
    for (x, z) in ((-19, -30), (19, -30)):
        B.spawn(x, z, None, s=s)


def core_walkway():
    """The square walkway round the plasma column at 6.6 m, where the two catwalks meet: four bridge pieces touching
    the column, with rails round the outside except where the catwalks join."""
    for (x0, z0, x1, z1) in ((-5, -5, 5, -1), (-5, 1, 5, 5), (-5, -1, -1, 1), (1, -1, 5, 1)):
        B.op({"op": "Bridge", "x0": x0, "z0": z0, "x1": x1, "z1": z1, "h": 6.6, "color": SLAB, "rails": False})
        B.stats["boxes"] += 3
    for (x0, z0, x1, z1) in ((-5, -5.15, -1, -5), (1, -5.15, 5, -5), (-5, 5, -1, 5.15), (1, 5, 5, 5.15), (-5.15, -5, -5, 5), (5, -5, 5.15, 5)):
        B.block(x0, 6.6, z0, x1, 7.6, z1, RAIL)
    for (x, z) in ((-3, -3), (3, -3), (-3, 3), (3, 3), (0, -3), (0, 3), (-3, 0), (3, 0)):
        B.waypoint(x, z, 6.6)
    B.decal("Ignition Floor Grating", -3, -3, 6.6, size=3)
    B.decal("Ignition Floor Grating", 3, 3, 6.6, size=3)


# ================================================================================================ wings
def wing(s):
    """The west turbine wing (Blue's; its twin is Red's east wing): the blast door portal in a wall across its mouth, two
    turbine generators and their pumps, a crane beam overhead, the flag at the far end and the spawns round it."""
    xm = -FLAT                     # the wing mouth, x = -48.97
    B.block(xm - 0.5, 0, -WING_Z, xm + 0.4, 6.5, -2.6, HULL, s=s)
    B.block(xm - 0.5, 0, 2.6, xm + 0.4, 6.5, WING_Z, HULL, s=s)
    B.block(xm - 0.5, 4.3, -2.6, xm + 0.4, 6.5, 2.6, HULL, s=s)
    B.block(xm - 0.5, 6.5, -WING_Z, xm + 0.4, 12, WING_Z, DARK, s=s)
    B.prop("Blast Door Portal", xm, 0, yaw=90, s=s)
    B.prop("Blast Door Leaf", xm - 1.4, -4.0, yaw=90, s=s)
    B.sign("turbine_a" if s > 0 else "turbine_b", (xm + 1.2) * s, 0, 5.4, "wall-x" if s > 0 else "wall+x", s=1, along="z")
    B.sign("coolant", (xm + 1.2) * s, -8 * s, 3.2, "wall-x" if s > 0 else "wall+x", s=1, along="z")
    B.sign("clear", (xm + 1.2) * s, 8 * s, 3.2, "wall-x" if s > 0 else "wall+x", s=1, along="z")
    B.decal("Halvard Logo", xm + 4, 0, 0, s=s, size=3.0, rotation=90 if s > 0 else 270)
    B.decal("Ignition Sector D" if s > 0 else "Ignition Sector B", xm + 7, -5, 0, s=s, size=2.2, rotation=90 if s > 0 else 270)
    # Inside the wing.
    B.props([("Turbine Generator", -60, -6.5, 0), ("Turbine Generator", -68.5, -6.5, 0), ("Coolant Pump", -58, 7, 0), ("Coolant Pump", -66, 7, 0),
             ("Cryo Dewar", -69.5, 9.5, 0), ("Propellant Tank", -62, 10.6, 0), ("Station Pipe Run", -54, 10.5, 0), ("Station Pipe Riser", -52, 11.5, 0),
             ("Ventilation Fan Unit", -72.8, -5.5, 90), ("Coolant Manifold", -53.5, -9, 90), ("Gas Cylinders", -55.5, -11.5, 0),
             ("Supply Case Stack", -64, -11.2, 0), ("Tool Cart", -56, 3, 40), ("Warning Beacon Post", -52, 5.5, 0), ("Warning Beacon Post", -52, -5.5, 0),
             ("Hazard Chain", -64, -2.8, 0), ("Oil Drum", -70, -11, 0), ("Rusty Drum", -70.8, -10.4, 0), ("Cable Spool", -72.5, 2, 0),
             ("Station Console", -73, -2, 90), ("Station Server Cabinet", -73.2, 5, 90)], s=s)
    for x in (-56, -64, -72):
        B.prop("Tube Light", x, 0, yaw=90, s=s, y=5.6)
    for z in (-12.6, 12.6):
        B.prop("Emergency Strip Lamp", -62, z, yaw=0 if z < 0 else 180, s=s, y=3.0)
    # The crane: a beam across the wing on two gantry legs at the far end (cosmetic beam, solid legs), a hook.
    B.mesh("box", -61, 0, 7.6, STEEL, s=s, width=22, height=0.6, depth=0.8, material="Metal")
    B.mesh("box", -60, -12.2, 7.4, STEEL, s=s, width=18, height=0.4, depth=0.5, material="Metal")
    B.mesh("box", -60, 12.2, 7.4, STEEL, s=s, width=18, height=0.4, depth=0.5, material="Metal")
    B.prop("Gantry Hook", -57, 0, yaw=0, s=s, y=4.4)
    B.scatter(["Ignition Floor Grating", "global:Oil Stain", "global:Oil Drips", "global:Rust Stain", "global:Tyre Tracks"], -73, -12, -50, 12, 20, 111 if s > 0 else 112, s=s, gap=2.2)
    B.scatter(["global:Hazard Stripes", "global:Hazard Chevrons"], -72, -12, -50, 12, 6, 113 if s > 0 else 114, s=s, gap=4)
    for (x, z) in ((-70, -3), (-70, 3), (-66, 0), (-62, 3), (-57, -10), (-57, 10), (-72, -5), (-72, 5), (-40, -26), (-40, 26)):
        B.spawn(x, z, "blue", s=s)


# ================================================================================================ quadrants and hull
def quadrant(s):
    """The north-west quadrant's dressing (its twin is the south-east one), plus the north-east one (twin: south-west)."""
    B.props([("Coolant Pump", -30, -22, 45), ("Cryo Dewar", -24, -30, 0), ("Cryo Dewar", -21.5, -31.5, 0), ("Propellant Tank", -34, -12, 0),
             ("Station Pipe Riser", -37, -8, 0), ("Station Pipe Run", -39, -5, 90), ("Ventilation Fan Unit", -40, -24, 45),
             ("Supply Case Stack", -14, -14, 0), ("Station Cargo Pod", -18, -9, 20), ("Warning Beacon Post", -12, -22, 0),
             ("Gas Bottle Cage", -29, -30, 0), ("Tool Cart", -27, -9, 60), ("Hazard Chain", -17, -17, 45), ("Oil Drum", -37, -3.5, 0),
             ("Twin Gas Tanks", -29, -37, 0), ("Station Deck Barrier", -22, -22, 45), ("Deck Light Post", -36, -31, 0)], s=s)
    B.props([("Coolant Pump", 30, -22, -45), ("Cryo Dewar", 24, -30, 0), ("Propellant Tank", 34, -12, 0), ("Ventilation Fan Unit", 40, -24, -45),
             ("Station Cargo Pod", 18, -9, -20), ("Warning Beacon Post", 12, -22, 0), ("Gas Bottle Cage", 29, -30, 0),
             ("Station Pipe Run", 39, -5, 90), ("Station Pipe Riser", 37, -8, 0), ("Hazard Chain", 17, -17, -45), ("Supply Case Stack", 14, -14, 0),
             ("Twin Gas Tanks", 29, -37, 0), ("Station Deck Barrier", 22, -22, -45), ("Deck Light Post", 36, -31, 0), ("Tool Cart", 27, -9, -60)], s=s)
    for q in (-1, 1):
        B.decal("Ignition Heat Scorch", 30 * q, -22, 0, s=s, size=4)
        B.scatter(["Ignition Floor Grating", "global:Oil Stain", "global:Rust Stain", "global:Floor Cracks", "global:Scorch Mark"],
                  min(12 * q, 42 * q), -40, max(12 * q, 42 * q), -3, 16, (121 if q < 0 else 122) + (0 if s > 0 else 2), s=s, gap=2.5)
    B.decal("Ignition Sector B" if s > 0 else "Ignition Sector D", -30, -30, 0, s=s, size=2.2, rotation=0 if s > 0 else 180)


def hull():
    """Corner pillars at the octagon's vertices, blast shutters and pipe runs along the diagonal walls, a ceiling."""
    for k in range(8):
        t = math.radians(22.5 + 45 * k)
        x, z = (R - 2.2) * math.cos(t), (R - 2.2) * math.sin(t)
        B.block(x - 0.8, 0, z - 0.8, x + 0.8, 11.5, z + 0.8, STEEL)
        yaw = math.degrees(math.atan2(-x, -z))
        B.prop("Emergency Strip Lamp", x - 0.85 * math.cos(t), z - 0.85 * math.sin(t), yaw=yaw, y=3.0)
        B.prop("Radiation Sign", x - 0.85 * math.cos(t), z - 0.85 * math.sin(t), yaw=yaw, y=1.6)
    for k in range(4):
        t = math.radians(45 + 90 * k)
        x, z = (FLAT - 1.2) * math.cos(t), (FLAT - 1.2) * math.sin(t)
        yaw = math.degrees(math.atan2(-x, -z))
        B.prop("Blast Shutter", x, z, yaw=yaw)
        for off in (-7.5, 7.5):
            px, pz = x + off * math.cos(t + math.pi / 2), z + off * math.sin(t + math.pi / 2)
            B.prop("Wall Pipe Run" if off < 0 else "Wall Cable Tray", px + 0.4 * math.cos(t), pz + 0.4 * math.sin(t), yaw=yaw, y=2.4 if off < 0 else 1.2)
    # A dark ceiling disc with radial beams (looks only): the hall is roofed, the galleries' back walls reach it.
    B.mesh("cylinder", 0, 0, 12.0, DARK, width=2 * R + 6, height=0.6, depth=2 * R + 6, segments=16, material="Metal")
    for k in range(8):
        B.mesh("box", 0, 0, 11.4, STEEL, width=2 * R, height=0.5, depth=0.9, yaw=22.5 * k, material="Metal")
    B.mesh("torus", 0, 0, 11.5, ORANGE, radius=14, inner=0.25, segments=48, detail=8)



# ================================================================================================ the map
def build():
    B.op({"op": "Ground", "color": FLOOR})
    core()
    core_walkway()
    hull()
    for s in (1, -1):
        mains(s)
        gallery(s)
        wing(s)
        quadrant(s)
    B.scatter(["Ignition Floor Grating", "global:Oil Stain", "global:Floor Cracks", "global:Rust Stain"], -45, -45, 45, 45, 40, 131, gap=3.0)
    B.op({"op": "AutoWaypoints", "spacing": 6})
    for (x, z) in ((-7, -7), (7, 7), (-7, 7), (7, -7), (-25, -3), (25, 3), (3, -25), (-3, 25), (-44, 0), (44, 0)):
        B.waypoint(x, z)
    for (x, z) in ((-18, -42), (18, -42), (-18, 42), (18, 42)):
        B.waypoint(x, z, 0)


def main():
    build()
    m = {
        "format": "call-of-slop-map/1",
        "name": "Ignition Hall",
        "description": "A fusion reactor's containment hall: rings round the core, coolant mains, galleries, catwalks and two turbine wings.",
        "author": "Call of Slop",
        "order": 608,
        "destructible": False,
        "maxPlayers": 16,
        "skybox": {"preset": "night", "zenith": "#0a0b12", "horizon": "#1c2030", "ground": "#101218", "stars": 0.4,
                   "sun": {"elevation": 62, "azimuth": 200, "color": "#ffd8b0", "intensity": 0.75}, "moon": False, "ambient": 2.0, "clouds": False},
        "boundary": {"shape": "polygon", "points": outline(), "edge": {"style": "wall", "height": 12, "thickness": 1.0, "color": HULL, "material": "Metal"},
                     "groundMargin": 4},
        "teamSpawns": [B.spawns["blue"], B.spawns["red"]],
        "hills": [{"x": 0, "z": 3.5, "y": 0.5, "radius": 5}, {"x": 0, "z": -39, "y": 3.5, "radius": 4.5}, {"x": 0, "z": 39, "y": 3.5, "radius": 4.5}],
        "flags": [[-70, 0, 0], [70, 0, 0]],
        "ops": B.ops,
    }
    write_map(OUT_JSON, m, B.stats)
    if OUT_PNG:
        plan(OUT_PNG, B.ops, outline(), size=(160, 120), scale=3.5)


if __name__ == "__main__":
    main()
