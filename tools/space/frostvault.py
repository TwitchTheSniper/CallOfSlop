#!/usr/bin/env python3
"""Generator for Maps/Space/11-Frostvault.json: "Frostvault", the Space pack's cryo-vault research facility (medium,
16 players).

    python3 tools/space/frostvault.py [repo root] [plan.png]

A hexagonal cryogenics facility of the invented Borealis Cryogenics, open to a cold night sky. The layout turns 180
degrees about the centre (Blue holds the west intake, Red the east):

  - The Specimen Vault (0, 0): a sunken 13 m pit (hill 1) inside a 1.2 m podium, ringed by the roofed Quarantine
    Corridor (a loop with twelve doors and four inner stairs). Its roof is the Observation Gallery: railed walkways
    over the corridor, reached from the labs' roofs by two bridges and by two corner stairs.
  - Pod Hall A (north-west) and Pod Hall B (south-east): roofless halls with rows of cryo pods as cover.
  - Lab North and Lab South (0, -+42): white walk-in labs with roof access (hills 2 and 3).
  - Server rooms (north-east and south-west), service alleys along the outer wall, and the two intake bays with the
    teams' spawns and flags.

Everything solid is placed in rotated pairs, so both teams get the same cover. Props are the pack-local Frostvault set
(Maps/Space/Props/Frostvault, tools/space/frostvault_props.py) plus Station, Facility and Office props; decals the
pack's Frostvault set (tools/space/frostvault_decals.py).
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "11-Frostvault.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import dkit  # noqa: E402
import frostvault_decals as fdec  # noqa: E402

K = dkit.Kit(REPO, sym="rot")
K.props_lib.update(dkit.load_props(REPO, [os.path.join(REPO, "Maps", "Space", "Props")]))

HEX_R = 64.0
WALL = "#e4e9ee"       # white wall panels
TRIM = "#b9c4cd"       # steel-blue trim and rails
STEEL = "#8a949c"
PODIUM = "#9aa6b0"
GALLERY = "#aeb9c2"
FLOOR = "#c3ccd3"
DARK = "#3a434b"
GLASS = "#b8e4f8"

H_WALL = 3.2           # room walls
H_LOOP = 3.2           # corridor walls (the gallery slab sits on top: 3.2 - 3.5)
GAL_TOP = 3.5
LOOP_OUT = 20.0        # outer corridor wall face
LOOP_IN = 15.0         # inner corridor wall face
T = 0.4
PODIUM_Y = 1.2
PIT = 6.5              # half-width of the pit


def door(c, w=2.2, h=2.4, y0=0.0):
    return (c, w, y0, y0 + h)


def window(c, w=1.6, y0=1.0, y1=2.2):
    return (c, w, y0, y1)


def rail(x0, z0, x1, z1, y, h=0.9, t=0.12, sym=True):
    """A railing block along a line (axis-aligned)."""
    if abs(x1 - x0) < 0.01:
        K.block(x0 - t / 2, y, z0, x0 + t / 2, y + h, z1, TRIM, sym=sym)
    else:
        K.block(x0, y, z0 - t / 2, x1, y + h, z0 + t / 2, TRIM, sym=sym)


def sign(key, cx, z, y, onto, cz=None, along="x"):
    """A tiled wall sign centred at (cx, z) (along x) or at x = cx running along z (along="z")."""
    names, tw, th = fdec.tiles(key)
    n = len(names)
    for i, nm in enumerate(names):
        off = (i - (n - 1) / 2) * tw
        if along == "x":
            x, zz = cx + (off if onto == "wall-z" else -off), z
        else:
            x, zz = cx, z + (off if onto == "wall+x" else -off)
        K.decal(nm, x, zz, y, onto=onto, size=tw, reach=2.0, sym=False)


# ================================================================================================ the vault complex
def vault():
    # Outer corridor walls (3 doors a side: the middle one offset so no straight line runs down the axis).
    o, i = LOOP_OUT, LOOP_IN
    K.wall_x(-o - T, o + T, -o - T, T, 0, H_LOOP, WALL, holes=[door(-13), door(4), door(13)])          # north (twin: south)
    K.wall_z(-o, o, -o - T, T, 0, H_LOOP, WALL, holes=[door(-13), door(-4), door(13)])                 # west (twin: east)
    # Inner walls: one door in the middle of each side, windows either side.
    K.wall_x(-i - T, i + T, -i - T, T, 0, H_LOOP, WALL, holes=[window(-9), door(0, 3.0), window(9)])
    K.wall_z(-i, i, -i - T, T, 0, H_LOOP, WALL, holes=[window(-9), door(0, 3.0), window(9)])
    # The gallery: the corridor's roof, 0.3 thick, with railings.
    K.slab(-o - T, -o - T, o + T, -i, GAL_TOP, 0.3, GALLERY)                                            # north strip (twin: south)
    K.slab(-o - T, -i, -i, i, GAL_TOP, 0.3, GALLERY)                                                    # west strip (twin: east)
    ro, ri = o + T, i
    rail(-ro, -ro, -1.6, -ro, GAL_TOP)                                                                  # outer north, cut for the lab bridge
    rail(1.6, -ro, ro, -ro, GAL_TOP)
    rail(-ro, -ro, -ro, STAIR_LAND_Z0, GAL_TOP)                                                         # outer west, cut for the corner stair landing
    rail(-ro, STAIR_LAND_Z1, -ro, ro, GAL_TOP)
    rail(-ri, -ri, ri, -ri, GAL_TOP)                                                                    # inner north
    rail(-ri, -ri, -ri, ri, GAL_TOP)                                                                    # inner west
    # Podium (top 1.2 m) round the pit, with four axis stairs: corridor -> podium -> pit.
    p = i - 0.2
    K.block(-p, 0, -p, -1.5, PODIUM_Y, -PIT, PODIUM)                                                    # NW quarter of the north strip (twin: SE)
    K.block(1.5, 0, -p, p, PODIUM_Y, -PIT, PODIUM)                                                      # NE (twin: SW)
    K.block(-p, 0, -PIT, -PIT, PODIUM_Y, -1.5, PODIUM)                                                  # west strip north half (twin: east south)
    K.block(-p, 0, 1.5, -PIT, PODIUM_Y, PIT, PODIUM)                                                    # west strip south half
    ps = p - 1.2                                                                                       # the flights start 1.2 m clear of the inner wall
    up_n = K.stairs_z(-1.5, 1.5, -ps, 1, 0, PODIUM_Y, PODIUM)                                           # from the inner north door up onto the podium
    dn_n = K.stairs_z(-1.5, 1.5, -PIT, -1, 0, PODIUM_Y, PODIUM)                                         # from the pit up onto the podium
    K.block(-1.5, 0, up_n, 1.5, PODIUM_Y, dn_n, PODIUM)                                                 # the walkway between the two flights
    up_w = K.stairs_x(-1.5, 1.5, -ps, 1, 0, PODIUM_Y, PODIUM)
    dn_w = K.stairs_x(-1.5, 1.5, -PIT, -1, 0, PODIUM_Y, PODIUM)
    K.block(up_w, 0, -1.5, dn_w, PODIUM_Y, 1.5, PODIUM)
    # Pit dressing: specimen tanks in the corners, the hex ring, floor lights.
    K.decal("Frostvault Vault Ring", 0, 0, 0, size=12.5, rotation=0, sym=False)
    K.prop("Specimen Tank", -4.6, -4.6, 0)
    K.prop("Specimen Tank", 4.6, -4.6, 0)
    for (x, z) in [(-2.6, -5.4), (2.6, -5.4), (-5.4, -2.6), (-5.4, 2.6)]:
        K.prop("Hex Floor Light", x, z, 0)
    K.prop("Cryo Dewar", -5.6, 0.0, 0)
    K.prop("Specimen Crate", -3.2, 3.6, 30)
    # Podium dressing.
    for (x, z, yw) in [(-13.2, -13.2, 135), (13.2, -13.2, 225)]:
        K.prop("Cryo Pod Upright", x, z, yw, PODIUM_Y)
    for (x, z, yw) in [(-8.5, -11.5, 0), (8.5, -11.5, 0)]:
        K.prop("Cryo Pod", x, z, yw, PODIUM_Y)
    for (x, z, yw) in [(-11.5, -4.0, 90), (-11.5, 8.0, 90)]:
        K.prop("Quarantine Barrier", x, z, yw, PODIUM_Y)
    for (x, z) in [(-9, -9), (9, -9), (-4, -13), (-13, -4)]:
        K.prop("Hex Floor Light", x, z, 0, PODIUM_Y)
    K.prop("Specimen Crate", -12.5, 12.0, 15, PODIUM_Y)
    K.prop("Biohazard Bin", -4.5, -13.6, 0, PODIUM_Y)
    K.decal("Frostvault Hex Floor", -10, -10, PODIUM_Y, size=4, rotation=0)
    K.decal("Frostvault Hex Floor", 10, -10, PODIUM_Y, size=4, rotation=0)
    K.decal("Frostvault Hex Floor", -10, 2, PODIUM_Y, size=4, rotation=0)
    K.decal("Frostvault Quarantine Stripe", 0, -p - 1.2, 0, size=3.6, rotation=0)
    K.decal("Frostvault Quarantine Stripe", -p - 1.2, 0, 0, size=3.6, rotation=90)
    # Corridor dressing: lamp strips under the gallery, barriers and dewars as cover, signs on the walls.
    mid = (o + i) / 2
    for k in range(-2, 3):
        K.prop("Lamp Strip", k * 7.5, -mid, 0, GAL_TOP - 0.3 - 0.45)
        K.prop("Lamp Strip", -mid, k * 7.5 + 3.5, 90, GAL_TOP - 0.3 - 0.45)
    for (x, z, yw) in [(-8.5, -mid, 0), (9.5, -mid - 0.9, 0), (-mid - 0.8, -8.0, 90), (-mid + 0.8, 7.0, 90)]:
        K.prop("Quarantine Barrier", x, z, yw)
    for (x, z) in [(-17.8, -17.8), (17.8, -17.8), (-2.5, -17.0), (-17.2, 2.5)]:
        K.prop("Cryo Dewar", x, z, 0)
    K.prop("Hazard Board", -15.9, -11.0, 270)
    K.prop("Hazard Board", 11.0, -15.9, 180)
    K.prop("Frost Vent", -10.0, -o + 0.04, 0, 2.3)
    K.prop("Frost Vent", 7.0, -o + 0.04, 0, 2.3)
    K.decal("Frostvault Biohazard Sign", -i + 0.3, -5.0, 1.9, onto="wall-x", size=0.9)
    K.decal("Frostvault Biohazard Sign", 5.0, -i + 0.3, 1.9, onto="wall-z", size=0.9)
    K.decal("Frostvault Cryo Warning", -i + 0.3, 5.0, 1.9, onto="wall-x", size=1.0)
    K.decal("global:Exit Sign", -mid, -o + 0.5, 2.85, onto="wall-z", size=0.8)
    K.decal("global:Exit Sign", 8.0, -o + 0.5, 2.85, onto="wall-z", size=0.8)
    K.decal("global:Stencil Arrow", -mid, -4.5, 0, size=1.4, rotation=180)
    K.decal("global:Stencil Arrow", -4.5, -mid, 0, size=1.4, rotation=90)
    K.decal_scatter(["Frostvault Frost Bloom Small", "Frostvault Frost Bloom"], -o, -o, o, -i, 7, 11, gap=2.5)
    K.decal_scatter(["Frostvault Frost Bloom Small", "Frostvault Frost Bloom"], -o, -i, -i, i, 6, 12, gap=2.5)
    K.decal_scatter(["Frostvault Frost Bloom Small", "Frostvault Frost Bloom"], i, -i, o, i, 6, 13, gap=2.5)
    K.decal_scatter(["Frostvault Frost Bloom Small", "Frostvault Frost Bloom"], -o, i, o, o, 7, 14, gap=2.5)
    sign("vault", 0, -i + 0.3, 2.95, "wall-z")
    sign("vault", 0, i - 0.3, 2.95, "wall+z")
    sign("quarantine", 4.0, -o - T - 0.3, 3.0, "wall+z")
    sign("quarantine", -4.0, o + T + 0.3, 3.0, "wall-z")
    sign("quarantine", -o - T - 0.3, -4.0, 3.0, "wall+x", along="z")
    sign("quarantine", o + T + 0.3, 4.0, 3.0, "wall-x", along="z")
    # Gallery dressing: consoles at the corners, floor lights, the observation sign on the lab bridges.
    for (x, z, yw) in [(-18.0, -18.0, 135), (18.0, -18.0, 225)]:
        K.prop("Station Console", x, z, yw, GAL_TOP)
    for (x, z) in [(-17.5, -8), (-17.5, 8), (8, -17.5), (-8, -17.5)]:
        K.prop("Hex Floor Light", x, z, 0, GAL_TOP)
    K.prop("Quarantine Barrier", 10.0, -17.5, 0, GAL_TOP)
    K.prop("Specimen Crate", -17.5, -2.0, 20, GAL_TOP)
    K.decal_scatter(["Frostvault Hex Floor"], -o, -o, o, o, 40, 21, y=GAL_TOP, gap=4.5, minSize=4, maxSize=4)
    # Waypoints: the podium, the pit, the corridor and the gallery (the grid gets most of it; these anchor the levels).
    for (x, z) in [(-11, -11), (11, -11), (-11, 0), (-9, 9), (0, -11), (9, 9)]:
        K.waypoint(x, z, PODIUM_Y)
    K.waypoint(0, -3, 0)
    K.waypoint(-3, 3, 0)
    # The axis routes: outside and inside each inner door, the podium walkway, the foot of the pit stairs.
    for (x, z) in [(-16.2, 0), (-11.5, 0), (-5.2, 0), (0, -16.2), (0, -11.5), (0, -5.2)]:
        K.waypoint(x, z, PODIUM_Y if 6 < abs(x) + abs(z) < 14 else 0)
    for (x, z, y) in [(-13.1, 0, 0.6), (-7.2, 0, 0.6), (0, -13.1, 0.6), (0, -7.2, 0.6)]:
        K.waypoint(x, z, y)
    for (x, z) in [(-21.5, -4), (-18.5, -4), (-21.5, -13), (-18.5, -13), (-21.5, 13), (-18.5, 13),
                   (4, -21.5), (4, -18.5), (-13, -21.5), (-13, -18.5), (13, -21.5), (13, -18.5)]:
        K.waypoint(x, z, 0)
    for (x, z) in [(-17.5, -17.5), (17.5, -17.5), (-17.5, -8), (-17.5, 0), (-17.5, 8), (-8, -17.5), (0, -17.5), (8, -17.5)]:
        K.waypoint(x, z, GAL_TOP)


# ================================================================================================ labs, halls, server rooms
LAB_X, LAB_Z0, LAB_Z1 = 13.0, -48.0, -36.0


def lab():
    """Lab North (the twin is Lab South): white walls, windows, a roof at 3.5 with a parapet and a stair up, a bridge
    south to the gallery."""
    x0, x1, z0, z1 = -LAB_X, LAB_X, LAB_Z0, LAB_Z1
    K.wall_x(x0, x1, z0, T, 0, GAL_TOP, WALL, holes=[window(-8), window(0), window(8)])                 # north wall
    K.wall_x(x0, x1, z1 - T, T, 0, GAL_TOP, WALL, holes=[door(-8), window(0), door(8)])                 # south wall: two doors
    K.wall_z(z0 + T, z1 - T, x0, T, 0, GAL_TOP, WALL, holes=[door(-42.5)])                              # west door
    K.wall_z(z0 + T, z1 - T, x1 - T, T, 0, GAL_TOP, WALL, holes=[door(-40.0), window(-45.0)])           # east door
    sx0, sx1 = x1 - T - 1.5, x1 - T - 0.1
    zs = z0 + T + 1.3
    zend = K.stairs_z(sx0, sx1, zs, 1, 0, GAL_TOP, TRIM)
    sxm = (sx0 + sx1) / 2
    K.waypoint(sxm, zs - 0.8, 0)
    K.waypoint(sxm, zs + 1.2, 1.0)
    K.waypoint(sxm, zs + 2.6, 2.3)
    K.waypoint(sxm, zend + 1.0, GAL_TOP)
    K.slab(x0, z0, x1, z1, GAL_TOP, 0.3, GALLERY, hole=(sx0 - 0.2, zs - 0.3, sx1 + 0.1, zend))
    # Parapet (0.5 m: steppable) with a gap where the bridge leaves.
    K.block(x0, GAL_TOP, z0, x1, GAL_TOP + 0.5, z0 + T, WALL)
    K.block(x0, GAL_TOP, z1 - T, -1.6, GAL_TOP + 0.5, z1, WALL)
    K.block(1.6, GAL_TOP, z1 - T, x1, GAL_TOP + 0.5, z1, WALL)
    K.block(x0, GAL_TOP, z0 + T, x0 + T, GAL_TOP + 0.5, z1 - T, WALL)
    K.block(x1 - T, GAL_TOP, z0 + T, x1, GAL_TOP + 0.5, z1 - T, WALL)
    # The bridge to the gallery.
    K.slab(-1.6, z1, 1.6, -LOOP_OUT - T + 0.1, GAL_TOP, 0.3, GALLERY)
    rail(-1.6, z1, -1.6, -LOOP_OUT - T, GAL_TOP)
    rail(1.6, z1, 1.6, -LOOP_OUT - T, GAL_TOP)
    K.block(-0.3, 0, -28.3, 0.3, GAL_TOP - 0.3, -27.7, STEEL)
    K.decal("Frostvault Hex Floor", 0, -30, GAL_TOP, size=4, rotation=0)
    sign("gallery", 0, z1 + 0.3, 2.9, "wall-z")
    sign("lab_n", 0, z1 + 0.3, 1.1, "wall-z")
    sign("lab_s", 0, -z1 - 0.3, 1.1, "wall+z")
    K.decal("Borealis Logo", -8.0, z1 + 0.3, 1.5, onto="wall-z", size=2.2, sym=False)
    K.decal("Borealis Logo", 8.0, -z1 - 0.3, 1.5, onto="wall+z", size=2.2, sym=False)
    # Inside: benches down the middle, lockers along the north wall, tanks in the corners, a decon arch at each door.
    for (x, z, yw) in [(-6.0, -40.0, 0), (6.0, -40.0, 0), (-6.0, -44.5, 0), (6.0, -44.5, 0)]:
        K.prop("Lab Bench", x, z, yw)
    for x in (-9.5, -3.5, 2.5, 8.5):
        K.prop("Cold Storage Locker", x, z0 + T + 0.35, 0)
    K.prop("Specimen Tank", x0 + 1.3, -37.8, 0)
    K.prop("Hazmat Suit Rack", x0 + 1.0, -46.0, 90)
    K.prop("Decon Shower Arch", -8.0, z1 - 1.4, 0)
    K.prop("Decon Shower Arch", 8.0, z1 - 1.4, 0)
    K.prop("Biohazard Bin", -10.5, -38.5, 0)
    K.prop("Specimen Crate", 10.5, -43.2, 20)
    K.prop("Office Chair", -7.0, -38.6, 200)
    K.prop("Whiteboard", 4.0, z0 + 2.2, 180)
    for x in (-7.5, 0.0, 7.5):
        K.prop("Lamp Strip", x, -42.0, 0, GAL_TOP - 0.3 - 0.45)
    K.decal("Frostvault Biohazard Sign", -10.6, z1 + 0.3, 1.9, onto="wall-z", size=0.8)
    K.decal("global:Vent Grille", 11.0, z0 + 0.3 + T, 2.6, onto="wall-z", size=0.8)
    K.decal("Frostvault Cryo Warning", x0 + T + 0.3, -44.0, 1.8, onto="wall-x", size=0.9)
    K.decal("Frostvault Frost Bloom", x0 + 2.0, -39.0, 0, size=2.0)
    K.decal_scatter(["Frostvault Hex Floor"], x0, z0, x1, z1, 14, 31, gap=4.0, minSize=4, maxSize=4)
    K.decal_scatter(["Frostvault Hex Floor"], x0, z0, x1, z1, 14, 32, y=GAL_TOP, gap=4.0, minSize=4, maxSize=4)
    # Roof dressing.
    K.prop("Vent Stack", -9.0, -46.0, 0, GAL_TOP)
    K.prop("Station Supply Case", 9.0, -38.5, 30, GAL_TOP)
    K.prop("Hex Floor Light", 0, -42, 0, GAL_TOP)
    K.waypoint(0, -42, 0)
    K.waypoint(-8, -38.5, 0)
    K.waypoint(0, -42, GAL_TOP)
    K.waypoint(-8, -40, GAL_TOP)
    K.waypoint(0, -34, GAL_TOP)
    K.waypoint(0, -28, GAL_TOP)
    K.waypoint(0, -23, GAL_TOP)


HALL_X0, HALL_X1, HALL_Z0, HALL_Z1 = -36.0, -18.0, -40.0, -24.0


def pod_hall():
    """Pod Hall A (north-west; the twin is Pod Hall B): a roofless hall with two rows of lying cryo pods, upright pods
    along the walls and an aisle between the doors."""
    x0, x1, z0, z1 = HALL_X0, HALL_X1, HALL_Z0, HALL_Z1
    cz = (z0 + z1) / 2
    K.wall_x(x0, x1, z0, T, 0, H_WALL, WALL, holes=[window(-31), window(-23)])                           # north
    K.wall_x(x0, x1, z1 - T, T, 0, H_WALL, WALL, holes=[door(-30), window(-23)])                        # south: door onto the north yard
    K.wall_z(z0 + T, z1 - T, x0, T, 0, H_WALL, WALL, holes=[door(cz), window(-36.5)])                   # west: from the service alley
    K.wall_z(z0 + T, z1 - T, x1 - T, T, 0, H_WALL, WALL, holes=[door(cz), window(-27.5)])               # east: to the lab lane
    for x in (-33.0, -29.0, -25.0, -21.0):
        K.prop("Cryo Pod", x, cz - 4.3, 0)
        K.prop("Cryo Pod", x, cz + 4.3, 0)
    for x in (-33.5, -27.0, -20.5):
        K.prop("Cryo Pod Upright", x, z0 + T + 0.7, 180)
    K.prop("Coolant Manifold", x0 + T + 0.5, z1 - T - 1.2, 90)
    K.prop("Cryo Dewar", -25.5, z1 - T - 0.8, 0)
    K.prop("Cryo Dewar", -26.5, z1 - T - 1.6, 0)
    K.prop("Station Console", -22.0, z1 - T - 0.7, 0)
    K.prop("Deck Light Post", -30.5, cz, 90)
    K.prop("Deck Light Post", -23.5, cz, 270)
    K.prop("Frost Vent", -29.0, z0 + T + 0.04, 0, 2.2)
    K.decal("Frostvault Hex Number 1", -27.0, cz, 0, size=2.4, rotation=0, sym=False)
    K.decal("Frostvault Hex Number 2", 27.0, -cz, 0, size=2.4, rotation=180, sym=False)
    K.decal("Frostvault Cryo Warning", x0 + T + 0.3, cz - 3.0, 1.8, onto="wall-x", size=1.0)
    K.decal("Frostvault Biohazard Sign", x1 - T - 0.3, cz + 3.0, 1.8, onto="wall+x", size=0.9)
    K.decal_scatter(["Frostvault Frost Bloom", "Frostvault Frost Bloom Small", "Frostvault Frost Bloom Small"], x0, z0, x1, z1, 14, 41, gap=1.8)
    K.decal_scatter(["Frostvault Hex Floor"], x0, z0, x1, z1, 14, 42, gap=4.0, minSize=4, maxSize=4)
    sign("hall_a", (x0 + x1) / 2, z1 + 0.3, 2.85, "wall-z")
    sign("hall_b", -(x0 + x1) / 2, -z1 - 0.3, 2.85, "wall+z")
    K.waypoint(-27, cz, 0)
    K.waypoint(-31, cz - 2.2, 0)
    K.waypoint(-23, cz + 2.2, 0)


SRV_X0, SRV_X1, SRV_Z0, SRV_Z1 = 18.0, 34.0, -40.0, -26.0


def server_room():
    """The north-east server room (the twin is south-west): two rows of server stacks facing a centre aisle."""
    x0, x1, z0, z1 = SRV_X0, SRV_X1, SRV_Z0, SRV_Z1
    cz = (z0 + z1) / 2
    K.wall_x(x0, x1, z0, T, 0, H_WALL, WALL, holes=[window(26)])
    K.wall_x(x0, x1, z1 - T, T, 0, H_WALL, WALL, holes=[door(23), door(30)])
    K.wall_z(z0 + T, z1 - T, x0, T, 0, H_WALL, WALL, holes=[door(cz)])
    K.wall_z(z0 + T, z1 - T, x1 - T, T, 0, H_WALL, WALL, holes=[door(cz + 2), window(cz - 4)])
    for x in (21.5, 24.5, 27.5, 30.5):
        K.prop("Vault Server Stack", x, cz - 3.2, 0)
        K.prop("Vault Server Stack", x, cz + 3.2, 180)
    K.prop("Patch Panel Cabinet", x0 + T + 0.5, z0 + T + 2.0, 90)
    K.prop("Station Console", 26.0, z0 + T + 0.6, 0)
    K.prop("Server Rack", x1 - T - 0.6, z0 + T + 1.0, 270)
    K.prop("Wall Cable Tray", 26.0, z0 + T + 0.3, 0, 2.5)
    K.prop("Tube Light", 22.0, cz, 0, H_WALL - 0.5)
    K.prop("Tube Light", 30.0, cz, 0, H_WALL - 0.5)
    K.prop("Cryo Dewar", x1 - T - 0.8, z1 - T - 0.8, 0)
    K.prop("Specimen Crate", x0 + T + 0.8, z1 - T - 1.0, 40)
    K.decal("global:Electrical Panel", 20.0, z0 + T + 0.3, 1.6, onto="wall-z", size=1.0)
    K.decal("global:Hazard Stripes", 26.5, z1 - 1.5, 0, size=2.0, rotation=0)
    K.decal_scatter(["Frostvault Hex Floor"], x0, z0, x1, z1, 10, 51, gap=4.0, minSize=4, maxSize=4)
    sign("servers", (x0 + x1) / 2, z1 + 0.3, 2.85, "wall-z")
    sign("servers", -(x0 + x1) / 2, -z1 - 0.3, 2.85, "wall+z")
    K.waypoint(26, cz, 0)
    K.waypoint(20, cz, 0)
    K.waypoint(32, cz, 0)


# ================================================================================================ yards, alleys, intake
STAIR_Z = -9.8
STAIR_LAND_Z1 = STAIR_Z - 9 * 0.45          # where the flight ends (9 steps of 0.45 m)
STAIR_LAND_Z0 = STAIR_LAND_Z1 - 2.8


def corner_stairs():
    """Stairs from the north-west forecourt up to the gallery's west strip (the twin serves the south-east)."""
    sx0, sx1 = -24.6, -23.1
    zend = K.stairs_z(sx0, sx1, STAIR_Z, -1, 0, GAL_TOP, TRIM)       # climbs north from z = -9.8
    assert abs(zend - STAIR_LAND_Z1) < 0.01
    K.slab(sx0 - 0.1, STAIR_LAND_Z0, -LOOP_OUT - T + 0.1, zend, GAL_TOP, 0.3, GALLERY)
    rail(sx0 - 0.1, STAIR_LAND_Z0, -LOOP_OUT - T, STAIR_LAND_Z0, GAL_TOP)
    rail(sx0 - 0.1, STAIR_LAND_Z0, sx0 - 0.1, zend, GAL_TOP)
    rail(sx0 - 0.1, STAIR_Z, sx0 - 0.1, zend, 0, h=GAL_TOP + 0.9, t=0.12)  # solid cheek walls beside the flight
    rail(sx1 + 0.1, STAIR_Z, sx1 + 0.1, zend, 0, h=GAL_TOP + 0.9, t=0.12)
    K.waypoint(-23.85, STAIR_Z + 0.8, 0)
    K.waypoint(-23.85, STAIR_Z - 1.3, 1.1)
    K.waypoint(-23.85, STAIR_Z - 2.7, 2.3)
    K.waypoint(-23.0, -15.0, GAL_TOP)
    K.waypoint(-19.0, -15.0, GAL_TOP)
    K.decal("Frostvault Hex Floor", -23.0, -15.0, GAL_TOP, size=4, rotation=0)


def yards():
    """The open ground between the rooms: the north yard (between the labs, the halls and the loop), the lab lanes,
    the service alleys along the outer wall and the forecourts in front of the loop."""
    # North yard (x -18..18, z -36..-20): cover between the lab and the loop.
    for (x, z, yw) in [(-12.0, -28.0, 0), (12.0, -31.0, 90), (-5.0, -24.5, 0), (6.5, -27.0, 45)]:
        K.prop("Station Cargo Pod", x, z, yw)
    K.prop("Supply Case Stack", -7.0, -32.0, 0)
    K.prop("Quarantine Barrier", 4.0, -23.5, 0)
    K.prop("Hazard Board", -3.0, -33.0, 0)
    K.prop("Coolant Manifold", 15.0, -24.5, 0)
    K.prop("Cryo Dewar", 14.0, -27.0, 0)
    K.prop("Deck Light Post", -16.5, -22.5, 45)
    K.prop("Deck Light Post", 16.5, -34.0, 225)
    K.decal("Frostvault Quarantine Stripe", 0, -22.2, 0, size=4.0, rotation=0)
    K.decal_scatter(["Frostvault Hex Floor"], -18, -36, 18, -20, 24, 61, gap=4.0, minSize=4, maxSize=4)
    K.decal_scatter(["Frostvault Frost Bloom Small", "global:Floor Cracks", "Frostvault Frost Bloom"], -18, -36, 18, -20, 8, 62, gap=3.0)
    # Lab lanes (between the halls / server rooms and the labs).
    K.prop("Station Pipe Riser", -15.5, -45.0, 0)
    K.prop("Specimen Crate", -16.0, -41.0, 10)
    K.prop("Biohazard Bin", -14.5, -37.5, 0)
    K.prop("Station Pipe Riser", 15.5, -44.0, 0)
    K.prop("Cold Storage Locker", 16.2, -40.0, 90)
    # North service passage (between the lab and the outer wall).
    for x in (-10.0, -2.0, 6.0):
        K.prop("Station Pipe Run", x, -52.0, 0)
    K.prop("Vent Stack", -26.0, -50.0, 0)
    K.prop("Ventilation Fan Unit", 14.0, -52.0, 180)
    K.prop("Gas Cylinders", 2.0, -50.3, 0)
    K.prop("Specimen Crate", -4.0, -50.5, 0)
    K.prop("Deck Light Post", -22.0, -46.0, 0)
    K.prop("Deck Light Post", 22.0, -47.0, 0)
    K.decal_scatter(["Frostvault Frost Bloom", "Frostvault Frost Bloom Small", "global:Rust Stain"], -30, -54, 30, -48, 10, 71, gap=3.0)
    # West service alley (between Pod Hall A and the outer wall) and the south-west alley past the server room.
    K.prop("Station Pipe Run", -40.5, -29.0, 90)
    K.prop("Cryo Dewar", -39.0, -36.0, 0)
    K.prop("Coolant Manifold", -42.5, -20.0, 90)
    K.prop("Specimen Crate", -39.5, -24.0, 20)
    K.prop("Station Pipe Riser", -37.8, -43.0, 0)
    # Forecourt west of the loop (x -38..-21): the approach from the intake.
    for (x, z, yw) in [(-27.0, -12.0, 90), (-27.5, 10.0, 0), (-33.0, 0.0, 0)]:
        K.prop("Station Cargo Pod", x, z, yw)
    K.prop("Supply Case Stack", -30.5, -6.0, 90)
    K.prop("Quarantine Barrier", -24.0, 4.0, 90)
    K.prop("Quarantine Barrier", -24.0, -4.5, 90)
    K.prop("Hover Cargo Sled", -31.0, 15.0, 170)
    K.prop("Hazard Board", -26.0, -19.0, 90)
    K.prop("Deck Light Post", -29.0, -16.5, 0)
    K.prop("Deck Light Post", -29.0, 17.5, 0)
    K.prop("Biohazard Bin", -22.5, -15.5, 0)
    K.decal_scatter(["Frostvault Hex Floor"], -38, -20, -20.5, 20, 22, 81, gap=4.0, minSize=4, maxSize=4)
    K.decal_scatter(["Frostvault Frost Bloom Small", "Frostvault Frost Bloom", "global:Oil Stain"], -38, -20, -21, 20, 8, 82, gap=3.0)
    # Mirrored litter: breakable crates, bins and dewars in the yards (solid, so Clutter keeps them off the lanes).
    K.clutter(["Specimen Crate", "Specimen Crate", "Biohazard Bin", "Cryo Dewar", "Station Supply Case"], -44, -54, 44, -20, 22, 3)
    K.clutter(["Specimen Crate", "Biohazard Bin", "Station Supply Case"], -44, -20, -20.5, 20, 8, 4)
    K.clutter(["global:Paint Cans", "global:Toolbox", "global:Bucket", "global:Mop and Bucket"], -44, -54, 44, 54, 30, 5)
    # Waypoints in the alleys the grid may miss.
    for (x, z) in [(-40, -30), (-39, -40), (-44, -20), (-16, -45), (-15.5, -39), (16, -46), (-20, -51), (0, -52), (22, -51), (-22, -14)]:
        K.waypoint(x, z, 0)


def intake():
    """The west intake bay (Blue; the twin is Red's east bay): the airlock portal at the back, the flag in front of it,
    spawns round the sides and a screen wall with two doors facing the loop."""
    K.prop("Blast Door Portal", -58.0, 0.0, 90)
    K.prop("Blast Door Leaf", -56.8, 0.0, 90)
    K.wall_z(-9.5, 9.5, -38.4, T, 0, H_WALL, WALL, holes=[door(-5.5, 2.4), door(5.5, 2.4), window(0, 2.0, 1.1, 2.0)])
    K.block(-38.6, H_WALL, -9.7, -38.2, H_WALL + 0.4, 9.7, TRIM)
    sign("intake_w", -38.4 - 0.3, 0.0, 2.95, "wall+x", along="z")
    sign("intake_e", 38.4 + 0.3, 0.0, 2.95, "wall-x", along="z")
    K.decal("Borealis Logo", -38.4 - 0.3, -7.0, 2.4, onto="wall+x", size=1.6, sym=False)
    K.decal("Borealis Logo", 38.4 + 0.3, 7.0, 2.4, onto="wall-x", size=1.6, sym=False)
    for (x, z, yw) in [(-50.0, -14.0, 0), (-50.0, 14.0, 0), (-44.5, -5.0, 90), (-44.5, 5.0, 90)]:
        K.prop("Station Cargo Pod", x, z, yw)
    K.prop("Supply Case Stack", -47.0, -10.0, 0)
    K.prop("Supply Case Stack", -47.0, 10.0, 90)
    K.prop("Hover Cargo Sled", -53.0, -8.0, 90)
    K.prop("Cryo Pod", -43.5, -25.0, 60)
    K.prop("Cryo Pod", -43.5, 25.0, 120)
    K.prop("Cryo Pod Upright", -41.0, -14.0, 90)
    K.prop("Cryo Pod Upright", -41.0, 14.0, 90)
    K.prop("Coolant Manifold", -56.0, 9.0, 90)
    K.prop("Deck Light Post", -47.0, -17.0, 0)
    K.prop("Deck Light Post", -47.0, 17.0, 0)
    K.prop("Deck Light Post", -42.0, 0.0, 90)
    K.prop("Station Console", -41.5, -24.0, 60)
    K.prop("Biohazard Bin", -40.5, 7.5, 0)
    K.prop("Hex Floor Light", -52.0, 0.0, 0)
    K.prop("Hex Floor Light", -48.0, -3.0, 0)
    K.prop("Hex Floor Light", -48.0, 3.0, 0)
    K.decal("Frostvault Hex Number 3", -54.0, -6.0, 0, size=2.4, rotation=90, sym=False)
    K.decal("Frostvault Hex Number 4", 54.0, 6.0, 0, size=2.4, rotation=270, sym=False)
    K.decal("Frostvault Quarantine Stripe", -40.0, 0.0, 0, size=4.0, rotation=90)
    K.decal_scatter(["Frostvault Hex Floor"], -58, -24, -39, 24, 30, 91, gap=4.0, minSize=4, maxSize=4)
    K.decal_scatter(["Frostvault Frost Bloom Small", "Frostvault Frost Bloom", "global:Oil Stain"], -58, -24, -39, 24, 10, 92, gap=3.0)
    for (x, z) in [(-55, -7), (-55, 7), (-53, -16), (-53, 16), (-47.5, -20.5), (-47.5, 20.5), (-44, -12), (-44, 12)]:
        K.spawn(x, z, 0)
    K.waypoint(-52, 0, 0)
    K.waypoint(-40, -5.5, 0)
    K.waypoint(-40, 5.5, 0)


# ================================================================================================ the map
def main():
    vault()
    lab()
    pod_hall()
    server_room()
    corner_stairs()
    yards()
    intake()
    K.op({"op": "AutoWaypoints", "spacing": 7})
    outline = dkit.regular_polygon(HEX_R, 6, 0)
    hills = [{"x": 0, "z": 0, "y": 0, "radius": 6},
             {"x": 0, "z": -42, "y": 0, "radius": 4.5},
             {"x": 0, "z": 42, "y": 0, "radius": 4.5}]
    flags = [[-52, 0, 0], [52, 0, 0]]
    ops = [{"op": "Ground", "color": FLOOR}] + K.ops
    m = {
        "format": "call-of-slop-map/1",
        "name": "Frostvault",
        "description": "A hexagonal cryogenics vault: pod halls, white labs, a sunken specimen pit and a roofed quarantine loop with galleries on top.",
        "author": "Call of Slop",
        "order": 611,
        "destructible": False,
        "maxPlayers": 16,
        "flagPool": "none",
        "skybox": {"preset": "night", "zenith": "#070e1c", "horizon": "#2c4a66", "ground": "#141c26", "stars": 1.7,
                   "moon": {"direction": [0.3, 0.55, -0.6], "size": 16, "color": "#d8ecff", "intensity": 0.55},
                   "sun": {"elevation": -12, "azimuth": 250}, "ambient": 1.9, "clouds": False},
        "boundary": {"shape": "polygon", "points": outline, "edge": {"style": "wall", "height": 6, "thickness": 0.8, "color": "#9fb0bd", "material": "Metal"},
                     "groundMargin": 6},
        "teamSpawns": [K.spawns[0], K.spawns[1]],
        "hills": hills,
        "flags": flags,
        "ops": ops,
    }
    with open(OUT_JSON, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print(f"wrote {OUT_JSON}: {len(ops)} ops, {K.stats}, hex area {dkit.area(outline):.0f} m2")
    if OUT_PNG:
        dkit.plan(K, outline, OUT_PNG, (70, 62), hills, flags)


if __name__ == "__main__":
    main()
