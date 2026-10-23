#!/usr/bin/env python3
"""Generator for Maps/Space/12-VesperStrip.json: "Vesper Strip", the Space pack's spaceport bazaar (medium, 16 players).

    python3 tools/space/vesperstrip.py [repo root] [plan.png]

A bean-shaped night-time spaceport strip of the invented Port Vesper authority: three landing pads along the long
north arc (docked shuttles on the outer two, the middle one open: hill 2), a departures concourse, the fountain plaza
(hill 1), the customs hall (hill 3) in the bean's dent, and a crescent of neon-lit shops along the south side: a repair
shop, a cantina (an arcade on the east side) and a kiosk each side, with market stalls in the lane in front of them and
a back alley behind. The shop roofs are joined by railed bridges into one rooftop lane from the west repair shop to
the customs roof and on to the east. Each team's base is a lobe behind a gate wall with a cargo yard.

Mirror-symmetric about x = 0 (Blue west, Red east): every solid thing has a twin at (-x, z) with its yaw negated; the
two halves differ only in neon signs and litter. Props are the pack-local Vesper set (Maps/Space/Props/Vesper,
tools/space/vesper_props.py) plus Retail, Street, Urban, Station, Animated and Industrial props; decals the pack's
Vesper set (tools/space/vesper_decals.py).
"""
import json
import math
import os
import random
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "12-VesperStrip.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import dkit  # noqa: E402
import vesper_decals as vdec  # noqa: E402

K = dkit.Kit(REPO, sym="mirx")
K.props_lib.update(dkit.load_props(REPO, [os.path.join(REPO, "Maps", "Space", "Props")]))

CTRL = [[-74, 0], [-64, -30], [-36, -46], [0, -50], [36, -46], [64, -30], [74, 0], [56, 24], [32, 34], [0, 24], [-32, 34], [-56, 24]]
OUTLINE = dkit.catmull(CTRL)

GROUND = "#3a3844"
WALL = "#6a6270"        # shop walls: dark plaster
WALL2 = "#55505e"
ROOF = "#4a4650"
TRIM = "#8d8798"
STEEL = "#6c6f73"
DARK = "#2a2c30"
CONC = "#5e5a66"

WALL_H = 3.4
ROOF_TOP = 3.7          # every shop roof and bridge
T = 0.4


def door(c, w=2.2, h=2.6, y0=0.0):
    return (c, w, y0, y0 + h)


def window(c, w=1.6, y0=1.0, y1=2.2):
    return (c, w, y0, y1)


def rail(x0, z0, x1, z1, y, h=0.9, t=0.12, sym=True):
    if abs(x1 - x0) < 0.01:
        K.block(x0 - t / 2, y, z0, x0 + t / 2, y + h, z1, TRIM, sym=sym)
    else:
        K.block(x0, y, z0 - t / 2, x1, y + h, z0 + t / 2, TRIM, sym=sym)


def neon(key, cx, z, y, onto, along="x"):
    names, tw, th = vdec.tiles(key)
    n = len(names)
    for i, nm in enumerate(names):
        off = (i - (n - 1) / 2) * tw
        if along == "x":
            x, zz = cx + (off if onto == "wall-z" else -off), z
        else:
            x, zz = cx, z + (off if onto == "wall+x" else -off)
        K.decal(nm, x, zz, y, onto=onto, size=tw, reach=2.0, sym=False)


def parapet(x0, z0, x1, z1, gaps=()):
    """A 0.5 m parapet round a roof; gaps: ("N"|"S"|"W"|"E", a, b) leaves a..b open on that side."""
    y = ROOF_TOP
    def seg(side, a0, a1):
        cuts = sorted([(a, b) for (s, a, b) in gaps if s == side])
        cur = a0
        for (a, b) in cuts:
            if a > cur:
                yield cur, a
            cur = max(cur, b)
        if cur < a1:
            yield cur, a1
    for (a, b) in seg("N", x0, x1):
        K.block(a, y, z0, b, y + 0.5, z0 + T, WALL)
    for (a, b) in seg("S", x0, x1):
        K.block(a, y, z1 - T, b, y + 0.5, z1, WALL)
    for (a, b) in seg("W", z0 + T, z1 - T):
        K.block(x0, y, a, x0 + T, y + 0.5, b, WALL)
    for (a, b) in seg("E", z0 + T, z1 - T):
        K.block(x1 - T, y, a, x1, y + 0.5, b, WALL)


# ================================================================================================ the crescent of shops
REP = (-58.0, -46.0, 10.0, 20.0)      # repair shop x0, x1, z0, z1
CAN = (-40.0, -26.0, 18.0, 26.0)      # cantina (east: the arcade)
KIO = (-22.0, -12.0, 18.0, 24.0)      # kiosk (duty free; east: pawn)
CUS = (-7.0, 7.0, 14.0, 22.0)         # customs hall


def repair_shop():
    x0, x1, z0, z1 = REP
    K.wall_x(x0, x1, z0, T, 0, WALL_H, WALL, holes=[(x0 + 6.0, 5.0, 0, 3.0), window(x1 - 2.5)])     # north: the bay door
    K.wall_x(x0, x1, z1 - T, T, 0, WALL_H, WALL, holes=[window(x0 + 4.0), door(x1 - 3.0)])          # south: back door to the alley
    K.wall_z(z0 + T, z1 - T, x0, T, 0, WALL_H, WALL, holes=[window(15.0)])
    K.wall_z(z0 + T, z1 - T, x1 - T, T, 0, WALL_H, WALL, holes=[door(17.0, 2.6, 2.8)])             # east: into the market lane
    K.slab(x0, z0, x1, z1, ROOF_TOP, 0.3, ROOF)
    parapet(x0, z0, x1, z1, gaps=[("E", 18.0, 20.0), ("W", 12.0, 15.1)])
    # Stairs up the west side (from the yard, climbing south), onto a landing that joins the roof.
    sx0, sx1 = x0 - 2.4, x0 - 1.0
    zend = K.stairs_z(sx0, sx1, 8.0, 1, 0, ROOF_TOP, TRIM)
    K.slab(sx0 - 0.1, zend, x0 + 0.1, zend + 3.0, ROOF_TOP, 0.3, ROOF)
    rail(sx0 - 0.1, zend, sx0 - 0.1, zend + 3.0, ROOF_TOP)
    rail(sx0 - 0.1, zend + 3.0, x0, zend + 3.0, ROOF_TOP)
    rail(sx0 - 0.1, 8.0, sx0 - 0.1, zend, 0, h=ROOF_TOP + 0.9)
    for (z, y) in [(7.2, 0), (9.5, 1.1), (10.9, 2.3), (zend + 1.5, ROOF_TOP)]:
        K.waypoint((sx0 + sx1) / 2, z, y)
    # Inside: an engine on its cradle, scrap, shelving, a generator; the bowser outside the bay.
    K.prop("Shuttle Engine Pod", -51.5, 15.5, 10)
    K.prop("Scrap Pile", -55.5, 17.5, 40)
    K.prop("Metal Shelving", x0 + T + 0.3, 12.5, 90)
    K.prop("Running Generator", x1 - T - 1.2, 12.0, 0)
    K.prop("Cable Spool", -49.0, 18.5, 0)
    K.prop("Gas Cylinders", -56.5, 12.5, 0)
    K.prop("Toolbox", -53.0, 13.0, 30)
    K.prop("Pallet", -56.0, 15.0, 0)
    K.prop("Caged Bulkhead Lamp", -52.0, z0 + T + 0.02, 0, 3.0)
    K.prop("Fuel Bowser", -52.0, 7.5, 90)
    K.prop("Porter Bot", -47.0, 8.0, 150)
    K.prop("Neon Awning", -52.0, z0 - 0.02, 180, 0)
    K.decal("global:Oil Stain", -51.0, 14.0, 0, size=2.4)
    K.decal("global:Oil Drips", -52.0, 8.5, 0, size=1.8)
    K.decal("global:Hazard Stripes", -52.0, z0 + 1.0, 0, size=2.4, rotation=0)
    K.decal("Vesper Scrawl Cyan", x1 - T - 0.3, 13.0, 1.6, onto="wall+x", size=2.0)
    K.decal("global:Grime Streaks", x0 + T + 0.3, 18.0, 1.8, onto="wall-x", size=1.8)
    neon("repairs", -52.0, z0 - 0.3, 3.75, "wall+z")
    neon("fuel", 52.0, z0 - 0.3, 3.75, "wall+z")
    K.prop("Pad Edge Light", -52.0, 9.0, 0)
    K.waypoint(-52.0, 15.0, 0)
    K.waypoint(-52.0, 15.0, ROOF_TOP)
    K.waypoint(-48.0, 19.0, ROOF_TOP)


def cantina():
    x0, x1, z0, z1 = CAN
    cx = (x0 + x1) / 2
    K.wall_x(x0, x1, z0, T, 0, WALL_H, WALL, holes=[(cx, 4.0, 0, 3.0), window(x0 + 2.5), window(x1 - 2.5)])
    K.wall_x(x0, x1, z1 - T, T, 0, WALL_H, WALL, holes=[door(x0 + 3.0), window(x1 - 3.5)])
    K.wall_z(z0 + T, z1 - T, x0, T, 0, WALL_H, WALL, holes=[window(22.0)])
    K.wall_z(z0 + T, z1 - T, x1 - T, T, 0, WALL_H, WALL, holes=[door(21.0)])
    K.slab(x0, z0, x1, z1, ROOF_TOP, 0.3, ROOF)
    parapet(x0, z0, x1, z1, gaps=[("W", 18.0, 20.0), ("E", 20.0, 22.5)])
    # Inside: a bar along the back, tables, stools, an arcade cabinet and a slot machine, lanterns.
    K.prop("Food Stall Counter", -32.0, z1 - T - 1.0, 0)
    K.prop("Bar Stool", -34.0, 23.6, 0)
    K.prop("Bar Stool", -32.0, 23.6, 0)
    K.prop("Bar Stool", -30.0, 23.6, 0)
    K.prop("Food Court Table Set Teal", -37.0, 21.5, 0)
    K.prop("Food Court Table Set", -28.5, 21.0, 90)
    K.prop("Arcade Cabinet", x0 + T + 0.5, 19.5, 90)
    K.prop("Slot Machine", x1 - T - 0.45, 24.5, 270)
    K.prop("Glowing Vending Machine", x1 - T - 0.5, 19.2, 270)
    K.prop("Hanging Lantern", -36.0, 23.5, 0, 0.6)
    K.prop("Hanging Lantern", -29.0, 23.5, 0, 0.6)
    K.prop("Neon Awning", cx, z0 - 0.02, 180, 0)
    K.prop("Sandwich Board", cx - 3.0, z0 - 1.2, 20)
    K.decal("global:Round Rug", -33.0, 21.0, 0, size=2.4)
    K.decal("Vesper Poster Violet", x0 + T + 0.3, 24.5, 1.6, onto="wall-x", size=1.0)
    K.decal("Vesper Poster Orange", x1 - T - 0.3, 22.0, 1.6, onto="wall+x", size=1.0)
    K.decal("global:Coffee Stain", -31.0, 22.0, 0, size=0.6)
    neon("cantina", cx, z0 - 0.3, 3.75, "wall+z")
    neon("arcade", -cx, z0 - 0.3, 3.75, "wall+z")
    neon("open", cx + 4.5, z0 - 0.3, 2.75, "wall+z")
    neon("open", -cx - 4.5, z0 - 0.3, 2.75, "wall+z")
    # Roof: a condenser, crates, a lantern string.
    K.prop("Rooftop Condenser", -38.0, 24.0, 0, ROOF_TOP)
    K.prop("Cargo Crate Small", -28.5, 24.5, 20, ROOF_TOP)
    K.prop("Lantern String", cx, 22.0, 0, ROOF_TOP)
    K.waypoint(cx, 22.0, 0)
    K.waypoint(cx, 22.0, ROOF_TOP)
    K.waypoint(-39.0, 19.0, ROOF_TOP)
    K.waypoint(-27.0, 21.0, ROOF_TOP)


def kiosk():
    x0, x1, z0, z1 = KIO
    cx = (x0 + x1) / 2
    K.wall_x(x0, x1, z0, T, 0, WALL_H, WALL2, holes=[(cx, 3.0, 0, 2.8), window(x0 + 2.0, 1.4)])
    K.wall_x(x0, x1, z1 - T, T, 0, WALL_H, WALL2, holes=[door(x1 - 2.5)])
    K.wall_z(z0 + T, z1 - T, x0, T, 0, WALL_H, WALL2, holes=[window(21.0)])
    K.wall_z(z0 + T, z1 - T, x1 - T, T, 0, WALL_H, WALL2, holes=[door(21.5)])
    K.slab(x0, z0, x1, z1, ROOF_TOP, 0.3, ROOF)
    parapet(x0, z0, x1, z1, gaps=[("W", 20.0, 22.5), ("E", 18.0, 20.5)])
    K.prop("Jewellery Display Case", -17.0, 22.5, 0)
    K.prop("Retail Gondola", x0 + T + 0.8, 21.0, 90)
    K.prop("Display Plinth", -14.5, 20.0, 0)
    K.prop("Perfume Display Table", -19.5, 20.0, 0)
    K.prop("Carrier Bag", -16.0, 19.5, 40)
    K.prop("Neon Awning", cx, z0 - 0.02, 180, 0)
    K.decal("Vesper Scrawl Pink", x0 + T + 0.3, 19.5, 1.5, onto="wall-x", size=1.6)
    neon("dutyfree", cx, z0 - 0.3, 3.75, "wall+z")
    neon("pawn", -cx, z0 - 0.3, 3.75, "wall+z")
    K.prop("Satellite Dish", -20.0, 22.5, 200, ROOF_TOP)
    K.waypoint(cx, 21.0, 0)
    K.waypoint(cx, 21.0, ROOF_TOP)


def customs():
    """The customs hall in the dent (hill 3): a thick roof on four pillars, a solid back wall with two doors, scanner
    gates and booths under it; the rooftop lane crosses its roof."""
    x0, x1, z0, z1 = CUS
    K.slab(x0, z0, x1, z1, ROOF_TOP, 1.0, CONC, sym=False)
    for (x, z) in [(x0 + 0.5, z0 + 0.5), (x0 + 0.5, z1 - 0.5)]:
        K.block(x - 0.35, 0, z - 0.35, x + 0.35, ROOF_TOP - 1.0, z + 0.35, CONC)
    K.wall_x(x0, x1, z1 - T, T, 0, ROOF_TOP - 1.0, WALL2, holes=[door(-4.0, 2.2, 2.6), door(4.0, 2.2, 2.6)], sym=False)   # back wall, doors at +-4
    K.prop("Customs Scanner Gate", -3.5, 15.5, 0)
    K.prop("Guard Booth", x0 + 1.4, 18.0, 90)
    K.prop("Queue Rope Barrier", -3.0, 13.0, 0)
    K.prop("Security Gate Pair", 0.0, 17.5, 0, sym=False)
    K.prop("Suitcase", -5.5, 15.0, 30)
    K.prop("Duffel Bag", 2.0, 19.5, 0, sym=False)
    K.prop("Tube Light", -3.5, 18.0, 0, ROOF_TOP - 1.4)
    K.decal("Port Vesper Emblem", 0, 18.0, 0, size=3.0, rotation=0, sym=False)
    K.decal("global:Stop Line", 0, 13.2, 0, size=3.5, rotation=0, sym=False)
    K.decal("global:Stencil Arrow", -3.5, 12.0, 0, size=1.2, rotation=0)
    neon("customs", 0, z0 - 0.3, ROOF_TOP - 0.5, "wall+z")
    neon("bazaar", 0, z1 + 0.3, ROOF_TOP - 0.5, "wall-z")
    # Rails along the roof's north and south edges, with gaps where the bridges and stairs join.
    rail(x0, z0, x1, z0, ROOF_TOP, sym=False)
    rail(x0, z1, x1, z1, ROOF_TOP, sym=False)
    rail(x0, z0, x0, 15.0, ROOF_TOP)                                                                # west edge: cut for the stair landing and bridge
    rail(x0, 20.5, x0, z1, ROOF_TOP)
    # Stairs from the plaza up to the roof, beside the kiosk bridge.
    sx0, sx1 = -11.4, -10.0
    zend = K.stairs_z(sx0, sx1, 11.0, 1, 0, ROOF_TOP, TRIM)
    K.slab(sx0 - 0.1, zend, x0 + 0.1, 18.0, ROOF_TOP, 0.3, ROOF)
    rail(sx0 - 0.1, zend, sx0 - 0.1, 18.0, ROOF_TOP)
    rail(sx0 - 0.1, 11.0, sx0 - 0.1, zend, 0, h=ROOF_TOP + 0.9)
    rail(sx1 + 0.1, 11.0, sx1 + 0.1, zend, 0, h=ROOF_TOP + 0.9)
    for (z, y) in [(10.2, 0), (12.5, 1.1), (14.0, 2.3), (zend + 1.4, ROOF_TOP)]:
        K.waypoint((sx0 + sx1) / 2, z, y)
    for (x, z) in [(-4.0, 18.0), (4.0, 18.0), (0.0, 15.0), (-5.0, 12.5)]:
        K.waypoint(x, z, 0, sym=False)
    for (x, z) in [(-5.0, 18.0), (0.0, 18.0), (5.0, 18.0), (-5.0, 15.5)]:
        K.waypoint(x, z, ROOF_TOP, sym=False)
    K.prop("Cargo Crate Small", 0.0, 20.5, 0, ROOF_TOP, sym=False)
    K.prop("Pad Edge Light", -5.0, 16.0, 0, ROOF_TOP)


def bridges():
    """The rooftop lane: repair shop -> cantina -> kiosk -> customs roof, as railed slabs at ROOF_TOP."""
    def bridge(x0, z0, x1, z1):
        K.slab(x0, z0, x1, z1, ROOF_TOP, 0.3, ROOF)
        rail(x0, z0, x1, z0, ROOF_TOP)
        rail(x0, z1, x1, z1, ROOF_TOP)
        K.block((x0 + x1) / 2 - 0.3, 0, (z0 + z1) / 2 - 0.3, (x0 + x1) / 2 + 0.3, ROOF_TOP - 0.3, (z0 + z1) / 2 + 0.3, STEEL)
        K.waypoint((x0 + x1) / 2, (z0 + z1) / 2, ROOF_TOP)
        K.decal("global:Hazard Stripes", (x0 + x1) / 2, (z0 + z1) / 2, ROOF_TOP, size=2.0, rotation=0)
    bridge(REP[1], 18.0, CAN[0], 20.0)
    bridge(CAN[1], 20.0, KIO[0], 22.5)
    bridge(KIO[1], 18.0, CUS[0], 20.5)


def market_lane():
    """Stalls in the lane between the shops and the plaza, and the back alley behind the shops."""
    for (name, x, z, yw) in [("Bazaar Stall Red", -20.0, 13.0, 180), ("Bazaar Stall Blue", -24.5, 13.0, 180), ("Noodle Counter", -30.5, 13.0, 180),
                             ("Bazaar Stall Blue", -36.5, 13.0, 180), ("Mall Kiosk Cart", -42.0, 11.5, 90), ("Bazaar Stall Red", -16.0, 9.5, 0)]:
        K.prop(name, x, z, yw)
    for (x, z, yw) in [(-27.5, 13.5, 0), (-33.5, 13.5, 0)]:
        K.prop("Holo Sign Post", x, z, yw)
    K.prop("Plaza Lamp", -14.0, 15.5, 0)
    K.prop("Plaza Lamp", -44.0, 15.5, 0)
    K.prop("Porter Bot", -22.5, 16.0, 200)
    K.prop("Cargo Crate Small", -41.0, 16.0, 25)
    K.prop("Hot Dog Cart", -46.0, 5.0, 60)
    K.prop("Lantern String", -30.0, 16.0, 0)
    K.prop("Lantern String", -20.0, 16.0, 0)
    K.prop("Trash Can", -38.5, 16.5, 0)
    K.prop("Trash Can", -18.5, 16.5, 0)
    K.decal_scatter(["Vesper Market Tiles"], -46, 6, -8, 18, 36, 11, gap=4.0, minSize=4, maxSize=4)
    K.decal_scatter(["global:Paper Litter", "global:Cigarette Butts", "global:Oil Stain", "global:Puddle"], -46, 6, -8, 18, 10, 12, gap=2.5)
    # Back alley: bins, scrap, scrawls, a lantern string and a lamp.
    K.prop("Dumpster", -44.0, 23.5, 90)
    K.prop("Scrap Pile", -33.0, 28.5, 70)
    K.prop("Wheelie Bin", -24.0, 27.5, 0)
    K.prop("Wheelie Bin Grey", -23.2, 27.6, 20)
    K.prop("Cargo Crate Small", -49.0, 22.5, 10)
    K.prop("Flickering Street Lamp", -43.0, 21.5, 90)
    K.prop("Lantern String", -32.0, 28.0, 0)
    K.decal("Vesper Scrawl Pink", -36.0, CAN[3] + 0.3, 1.6, onto="wall-z", size=2.0)
    K.decal("global:Graffiti Burst", -30.0, CAN[3] + 0.3, 1.5, onto="wall-z", size=1.5)
    K.decal_scatter(["global:Puddle", "global:Oil Stain", "global:Paper Litter", "global:Broken Glass"], -50, 20, -12, 30, 8, 13, gap=2.5)
    for (x, z) in [(-44.0, 21.0), (-33.0, 27.0), (-26.0, 27.5), (-16.0, 25.5), (-10.0, 23.5), (-50.5, 22.5)]:
        K.waypoint(x, z, 0)


# ================================================================================================ plaza, concourse, pads
def plaza():
    """The fountain plaza (hill 1) with the transit kiosk block that stops the gate-to-gate sightline."""
    K.prop("Mall Fountain", 0.0, -16.0, 0, sym=False)
    K.decal("Vesper Plaza Mosaic", 0.0, -8.0, 0, size=12, rotation=0, sym=False)
    K.block(-4.0, 0, 0.5, 4.0, 3.0, 5.5, WALL2, sym=False)                                           # the transit kiosk
    K.block(-4.3, 3.0, 0.2, 4.3, 3.3, 5.8, ROOF, sym=False)
    neon("departures", 0, 0.5 - 0.3, 2.4, "wall+z")
    neon("departures", 0, 5.5 + 0.3, 2.4, "wall-z")
    K.decal("Port Vesper Emblem", -4.0 - 0.3, 3.0, 1.6, onto="wall+x", size=2.0)
    for (x, z, yw) in [(-7.5, -3.0, 90), (-7.5, -13.0, 90), (-3.0, -22.0, 0)]:
        K.prop("Mall Bench", x, z, yw)
    for (x, z) in [(-9.5, -8.0), (-5.0, -20.5)]:
        K.prop("Plaza Lamp", x, z, 0)
    K.prop("Mall Planter Palm", -11.0, -3.0, 0)
    K.prop("Mall Trash Bin", -8.5, -0.5, 0)
    K.prop("Porter Bot", -6.0, -9.0, 120)
    K.prop("Holo Sign Post", -6.5, 3.0, 90)
    K.prop("Mall Directory Board", -9.0, 6.5, 270)
    K.prop("Pigeons", -3.0, -11.5, 0)
    K.decal_scatter(["global:Paper Litter", "global:Cigarette Butts", "global:Puddle", "global:Floor Cracks"], -12, -22, 12, 8, 10, 21, gap=3.0)
    for (x, z) in [(-6.0, -6.0), (6.0, -6.0), (0.0, -3.0), (-6.5, 3.0), (6.5, 3.0), (0.0, 8.0)]:
        K.waypoint(x, z, 0, sym=False)


def concourse():
    """Between the pads and the plaza: departures boards, benches, lamps and lantern strings."""
    for (x, z) in [(-14.0, -24.0), (14.0, -24.0)]:
        K.prop("Departures Board", x, z, 0, sym=False)
    for (x, z, yw) in [(-20.0, -22.0, 0), (-26.0, -18.0, 90)]:
        K.prop("Mall Bench", x, z, yw)
    for (x, z) in [(-18.0, -26.5), (-30.0, -14.0)]:
        K.prop("Plaza Lamp", x, z, 0)
    K.prop("Lantern String", -24.0, -20.0, 0)
    K.prop("Mall Kiosk Cart", -30.0, -22.0, 0)
    K.prop("Vending Machine", -8.5, -27.0, 0)
    K.prop("Glowing Vending Machine", -7.5, -27.0, 0)
    K.prop("Holo Sign Post", -34.0, -10.0, 0)
    K.prop("Cargo Crate Large", -22.0, -8.0, 0)
    K.prop("Cargo Crate Small", -20.0, -6.0, 35)
    K.prop("Hover Cargo Sled", -38.0, -4.0, 20)
    K.prop("Porter Bot", -16.0, -14.0, 60)
    K.prop("Mall Planter Box", -30.0, -6.0, 0)
    K.decal_scatter(["global:Lane Dash"], -44, -22, 44, -22, 10, 31, gap=5, minSize=3, maxSize=3)
    K.decal_scatter(["global:Paper Litter", "global:Puddle", "global:Oil Stain", "global:Skid Marks"], -44, -28, 44, -4, 14, 32, gap=3.0)


def pads():
    """Three landing pads along the north arc: docked shuttles on the outer two, the middle one open (hill 2)."""
    # Centre pad.
    K.decal("Vesper Pad Ring", 0, -36, 0, size=14, rotation=0, sym=False)
    K.decal("Vesper Pad Number 2", 0, -43.5, 0, size=2.6, rotation=0, sym=False)
    for k in range(12):
        a = math.radians(30 * k)
        K.prop("Pad Edge Light", 7.6 * math.cos(a), -36 + 7.6 * math.sin(a), 0, sym=False)
    K.prop("Pad Light Pylon", -9.5, -36.0, 0)
    K.prop("Fuel Bowser", -8.0, -29.0, 0)
    K.prop("Fuel Coupling", -4.0, -44.5, 0)
    K.prop("Landing Beacon", -3.0, -27.5, 0)
    # Outer pads with shuttles (nose towards the middle: Blue's shuttle points east, Red's west).
    px, pz = -38.0, -30.0
    K.decal("Vesper Pad Ring", px, pz, 0, size=14, rotation=0)
    K.decal("Vesper Pad Number 1", px, pz - 7.8, 0, size=2.6, rotation=0, sym=False)
    K.decal("Vesper Pad Number 3", -px, pz - 7.8, 0, size=2.6, rotation=0, sym=False)
    for k in range(12):
        a = math.radians(30 * k)
        K.prop("Pad Edge Light", px + 7.6 * math.cos(a), pz + 7.6 * math.sin(a), 0)
    K.prop("Docked Shuttle", px - 0.5, pz, 0)
    K.prop("Pad Light Pylon", px - 9.0, pz - 5.0, 0)
    K.prop("Pad Light Pylon", px + 8.0, pz + 6.5, 0)
    K.prop("Hover Cargo Sled", px + 5.0, pz - 8.5, 60)
    K.prop("Cargo Crate Small", px + 7.5, pz - 7.0, 20)
    K.prop("Fuel Coupling", px - 6.0, pz + 6.0, 0)
    K.prop("Landing Beacon", px - 8.5, pz + 4.0, 0)
    K.prop("Porter Bot", px + 3.0, pz + 8.5, 300)
    K.prop("Scrap Pile", px - 12.0, pz + 3.0, 10)
    K.decal("global:Oil Drips", px + 1.0, pz, 0, size=2.4)
    K.decal_scatter(["global:Skid Marks", "global:Oil Stain", "global:Tyre Tracks"], -48, -44, 48, -26, 8, 41, gap=4.0)
    for (x, z) in [(px, pz + 6.0), (px + 6.5, pz), (px - 6.5, pz), (0.0, -36.0), (-5.0, -40.0)]:
        K.waypoint(x, z, 0, sym=abs(x) > 0.1)


# ================================================================================================ gates, yards, bases
GATE_X = -48.0


def gate_and_yard():
    """The west gate wall (the twin is the east gate): a main gate, a cargo gate and a side gate; a cargo yard behind it."""
    x = GATE_X
    K.wall_z(-34.0, REP[2], x, T, 0, WALL_H, WALL2, holes=[(-20.0, 3.0, 0, WALL_H), (-8.0, 2.4, 0, 2.8), (3.0, 4.0, 0, WALL_H), window(-28.0, 1.4)])
    K.block(x - 0.2, WALL_H, -34.2, x + T + 0.2, WALL_H + 0.3, REP[2], TRIM)
    K.wall_z(REP[3], 27.0, x, T, 0, WALL_H, WALL2)                                                   # behind the repair shop: the alley is closed off from the base
    K.prop("Cargo Crate Large", -59.5, 18.0, 90)
    K.prop("Guard Booth", x + T + 1.2, 7.5, 270)
    K.prop("Crowd Barrier", x + T + 1.6, 0.6, 90)
    K.prop("Crowd Barrier", x - 1.6, 5.4, 90)
    K.prop("Crowd Barrier", x - 1.6, -23.5, 90)
    K.prop("Warning Beacon", x + T + 0.3, -17.5, 0)
    K.prop("Holo Sign Post", x - 2.0, 7.5, 90)
    neon("west", x - 0.3, 3.0, 3.2, "wall+x", along="z")
    neon("east", -x + 0.3, 3.0, 3.2, "wall-x", along="z")
    K.decal("global:Hazard Chevrons", x - 1.5, -20.0, 0, size=2.4, rotation=90)
    K.decal("global:Stencil Arrow", x + 2.0, 3.0, 0, size=1.4, rotation=270)
    K.decal("Vesper Scrawl Cyan", x + T + 0.3, -12.0, 1.5, onto="wall-x", size=1.8)
    K.decal("global:Poster Diagonal", x + T + 0.3, 9.0, 1.6, onto="wall-x", size=1.0)
    # Cargo yard (between the gate wall and the base): containers and crate rows.
    K.op({"op": "Container", "x": -55.0, "z": -27.0, "alongX": True, "color": "#8a4a3a"})
    K.op({"op": "Container", "x": 55.0, "z": -27.0, "alongX": True, "color": "#8a4a3a"})
    K.op({"op": "Container", "x": -55.0, "z": -27.0, "alongX": True, "color": "#3a5a8a", "y": 2.6})
    K.op({"op": "Container", "x": 55.0, "z": -27.0, "alongX": True, "color": "#3a5a8a", "y": 2.6})
    K.op({"op": "Container", "x": -56.0, "z": -16.0, "alongX": False, "color": "#5a6a3a", "open": True})
    K.op({"op": "Container", "x": 56.0, "z": -16.0, "alongX": False, "color": "#5a6a3a", "open": True})
    K.stats["boxes"] += 10
    K.boxes += [(-58, -28.25, -52, -25.75, 0, 5.2), (52, -28.25, 58, -25.75, 0, 5.2), (-57.25, -19, -54.75, -13, 0, 2.6), (54.75, -19, 57.25, -13, 0, 2.6)]
    for (x2, z, yw) in [(-52.0, -12.0, 0), (-52.0, -5.0, 90), (-56.0, -8.5, 0)]:
        K.prop("Cargo Crate Stack" if yw == 0 else "Cargo Crate Large", x2, z, yw)
    K.prop("Cargo Crate Large", -51.0, -22.0, 0)
    K.prop("Cargo Crate Small", -50.5, -30.0, 15)
    K.prop("Fuel Bowser", -51.5, -32.0, 0)
    K.prop("Pad Light Pylon", -50.0, -36.0, 0)
    K.prop("Porter Bot", -53.5, -22.5, 40)
    K.prop("Pallet Stack", -58.0, -22.0, 0)
    K.prop("Oil Drum", -58.5, -11.5, 0)
    K.decal_scatter(["global:Tyre Tracks", "global:Oil Stain", "global:Loading Bay Box"], -60, -34, -49, -4, 7, 51, gap=3.5)
    for (x2, z) in [(-51.5, -20.0), (-45.5, -20.0), (-51.5, 3.0), (-45.5, 3.0), (-51.5, -8.0), (-45.5, -8.0), (-55.0, -22.0), (-53.0, -8.0)]:
        K.waypoint(x2, z, 0)


def base():
    """Blue's west lobe (the twin is Red's): the flag, eight spawns, cover and the dock-side dressing."""
    K.prop("Docked Shuttle", -60.0, -16.0, 90)                                                       # a parked shuttle, nose north
    K.prop("Cargo Crate Stack", -60.0, -3.0, 0)
    K.prop("Cargo Crate Large", -66.0, -2.0, 90)
    K.prop("Cargo Crate Small", -53.5, 7.0, 30)
    K.prop("Hover Cargo Sled", -63.0, -9.0, 100)
    K.prop("Plaza Lamp", -52.0, 12.0, 0)
    K.prop("Pad Light Pylon", -66.0, -12.0, 0)
    K.prop("Holo Sign Post", -54.0, -1.0, 90)
    K.prop("Departures Board", -54.0, -16.0, 90)
    K.prop("Porter Bot", -58.0, -6.0, 90)
    K.prop("Fuel Coupling", -68.0, -4.0, 0)
    K.prop("Landing Beacon", -70.0, 2.0, 0)
    K.prop("Mall Bench", -52.0, 17.0, 0)
    K.prop("Lantern String", -56.0, 16.0, 90)
    K.decal("Port Vesper Emblem", -66.0, 0.0, 0, size=3.6, rotation=90)
    K.decal_scatter(["global:Lane Dash"], -62, 0, -50, 0, 4, 61, gap=3, minSize=3, maxSize=3)
    K.decal_scatter(["global:Oil Stain", "global:Puddle", "global:Paper Litter", "global:Skid Marks"], -70, -16, -49, 20, 8, 62, gap=3.0)
    for (x, z) in [(-66, -7), (-66, 7), (-67, -12), (-58, -25), (-55, -5), (-52, -1), (-63, 13), (-54, 14)]:
        K.spawn(x, z, 0)
    K.waypoint(-62.0, 0.0, 0)
    K.waypoint(-66.0, 8.0, 0)
    K.waypoint(-56.0, 2.0, 0)
    K.waypoint(-58.0, -15.0, 0)


def scenery():
    """Dark towers and spires beyond the edge, against the night sky (cosmetic meshes outside the outline)."""
    rng = random.Random(12)
    for i in range(26):
        a = math.radians(i * 360 / 26 + rng.uniform(-5, 5))
        rr = rng.uniform(96, 125)
        x, z = rr * math.cos(a), rr * math.sin(a) * 0.85
        h = rng.uniform(18, 55)
        w = rng.uniform(6, 14)
        K.mesh("box", x, z, 0, "#13131b", width=w, height=h, depth=w * rng.uniform(0.6, 1.4), yaw=rng.uniform(0, 90), sym=False)
        if i % 3 == 0:
            K.mesh("cone", x, z, h, "#1b1b26", width=w * 0.6, height=h * 0.4, depth=w * 0.6, sym=False)
        if i % 2 == 0:
            K.mesh("box", x, z, h * 0.6, ["#ff5fb4", "#4fe3ff", "#ffa040"][i % 3], width=w * 1.02, height=0.5, depth=w * 1.02 * rng.uniform(0.6, 1.4), sym=False)


# ================================================================================================ the map
def check_inside():
    bad = []
    for (x0, z0, x1, z1, y0, y1) in K.boxes:
        for (x, z) in [(x0, z0), (x1, z0), (x0, z1), (x1, z1)]:
            if not dkit.inside(OUTLINE, x, z) or dkit.edge_dist(OUTLINE, x, z) < 2.0:
                bad.append((round(x, 1), round(z, 1)))
    for (x0, z0, x1, z1) in K.foot:
        for (x, z) in [(x0, z0), (x1, z0), (x0, z1), (x1, z1)]:
            if not dkit.inside(OUTLINE, x, z):
                bad.append(("prop", round(x, 1), round(z, 1)))
    if bad:
        print("WARNING: near or outside the outline:", bad[:12])


def main():
    repair_shop()
    cantina()
    kiosk()
    customs()
    bridges()
    market_lane()
    plaza()
    concourse()
    pads()
    gate_and_yard()
    base()
    scenery()
    K.clutter(["Cargo Crate Small", "global:Small Crate", "global:Oil Drum", "global:Traffic Cone"], -70, -44, 70, 30, 24, 7, mirror=False)
    K.clutter(["global:Debris"], -70, -44, 70, 30, 220, 8, mirror=False)
    K.op({"op": "AutoWaypoints", "spacing": 7})
    check_inside()
    hills = [{"x": 0, "z": -6, "y": 0, "radius": 5},
             {"x": 0, "z": -36, "y": 0, "radius": 5},
             {"x": 0, "z": 18, "y": 0, "radius": 4.5}]
    flags = [[-66, 0, 0], [66, 0, 0]]
    ops = [{"op": "Ground", "color": GROUND}] + K.ops
    m = {
        "format": "call-of-slop-map/1",
        "name": "Vesper Strip",
        "description": "A night-time spaceport bazaar: three landing pads, a fountain plaza, a customs hall and a crescent of neon shops with a rooftop lane.",
        "author": "Call of Slop",
        "order": 612,
        "destructible": False,
        "maxPlayers": 16,
        "flagPool": "none",
        "skybox": {"preset": "night", "zenith": "#05070f", "horizon": "#2a1f3a", "ground": "#120e18", "stars": 1.8,
                   "moon": {"direction": [-0.45, 0.4, -0.5], "size": 22, "color": "#e8b090", "intensity": 0.5},
                   "sun": {"elevation": -15, "azimuth": 100}, "haze": {"color": "#ff5fb4", "strength": 0.3}, "ambient": 1.9, "clouds": False},
        "boundary": {"shape": "smooth", "points": CTRL, "edge": {"style": "wall", "height": 5, "thickness": 0.8, "color": "#4a4652", "material": "Metal"},
                     "groundMargin": 8},
        "teamSpawns": [K.spawns[0], K.spawns[1]],
        "hills": hills,
        "flags": flags,
        "ops": ops,
    }
    with open(OUT_JSON, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print(f"wrote {OUT_JSON}: {len(ops)} ops, {K.stats}, bean area {dkit.area(OUTLINE):.0f} m2")
    if OUT_PNG:
        dkit.plan(K, OUTLINE, OUT_PNG, (80, 56), hills, flags)


if __name__ == "__main__":
    main()
