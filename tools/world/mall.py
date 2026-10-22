#!/usr/bin/env python3
"""Generator for Maps/World/02-Mall.json ("Slopview Mall", World Tour pack, order 502, medium, 16 players, US).

Usage: python3 tools/world/mall.py <repo root>

An American shopping mall on a terraced suburban lot. The mall itself is the Shopping Mall building
(Buildings/ShoppingMall, written by tools/world/mall_building.py) at the origin, front to the south; this script dresses
it and builds the site round it:

- the atrium: a sunken fountain court cut into the terrain between the two bridges (steps down from the walkways), the
  coffee kiosk, carts, benches, palms, directory boards, hanging lights and sale banners under the skylight;
- every shop unit's invented brand: a lit fascia sign over its front, a logo plaque on its back wall, a brand poster
  inside, security gates, carrier bags; the department store, food court (stall headers), cinema (film posters) and the
  mall's own name over the main entrance and on the road pylon;
- outside: the car park sloping away down to the south (bays, islands with trees, lamps, cars, carts, corrals), the
  entrance road jutting out to the south-east, the multi-storey car park on the west with an alley jutting past it,
  the service yard and truck well at the loading dock on the north with the service road climbing out through the
  wooded embankment, the east service drive, and wooded embankments rising all round into the outline's cliff edge.

Not mirrored: Blue holds the car park (south), Red the service yard (north). Hills: the sunken court, the food court and
the department store's upper floor.
"""
import json, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mall_building as MB

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(REPO, "Maps/World/02-Mall.json")
rng = random.Random(502)
ops = []
L1 = MB.L1           # upper floor level (4.7)
ROOF = MB.ROOF       # 8.9


def R(v):
    return round(v, 3)


def op(name, **kw):
    d = {"op": name}
    d.update({k: (R(v) if isinstance(v, float) else v) for k, v in kw.items()})
    ops.append(d)
    return d


def prop(name, x, z, yaw=0, y=None, **kw):
    d = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
    if yaw:
        d["yaw"] = R(yaw % 360)
    if y is not None:
        d["y"] = R(y)
    d.update(kw)
    ops.append(d)


def decal(name, x, z, y=0.0, onto="floor", **kw):
    d = {"op": "Decal", "decal": name, "x": R(x), "z": R(z)}
    if y:
        d["y"] = R(y)
    if onto != "floor":
        d["onto"] = onto
    d.update({k: (R(v) if isinstance(v, float) else v) for k, v in kw.items()})
    ops.append(d)


# ================================================================ outline: a noisy lot with three lanes jutting out
POLY = [(-60, 54), (-30, 57), (0, 56), (30, 57), (37, 56.2),
        (39.2, 58.2), (39.5, 70), (48.5, 70), (48.8, 58.2), (51, 55.8),   # the entrance road, south-east (corners chamfered)
        (60, 52), (64, 36), (66, 10), (65, -15), (64, -36), (58, -50),
        (30, -54), (5.6, -53.2),
        (3.7, -55.4), (3.5, -67), (-6.5, -67), (-6.7, -55.4), (-8.6, -53.2),   # the service road, north
        (-30, -54), (-58, -50), (-66, -36), (-67, -12), (-66, 21.8),
        (-68.2, 23.5), (-77, 23.5), (-77, 33.5), (-68.2, 33.6), (-66, 35.4),  # the alley past the multi-storey, west
        (-64, 46)]
NOISE = {"amplitude": 0.45, "scale": 14, "seed": 52, "octaves": 2}

# ================================================================ terrain
F = []
F.append({"type": "noise", "amplitude": 0.6, "scale": 22, "seed": 502})
# wooded embankments rising to the edge all round
F.append({"type": "ridge", "points": [[-70, -52, 7], [-30, -56, 6], [0, -56, 5], [30, -56, 6], [64, -50, 7]], "width": 20, "height": 6})
F.append({"type": "ridge", "points": [[66, -44, 6], [69, -10, 5], [68, 20, 5], [64, 52, 4]], "width": 18, "height": 5})
F.append({"type": "ridge", "points": [[-70, -44, 6], [-71, -12, 5], [-70, 20, 4], [-67, 50, 3]], "width": 14, "height": 4})
F.append({"type": "hill", "x": 56, "z": -40, "radius": 12, "height": 3})
F.append({"type": "hill", "x": -56, "z": -40, "radius": 11, "height": 3})
# the pad the mall and its walks stand on (asphalt = the terrain's "sand", painted)
F.append({"type": "flatten", "x": 0, "z": -2, "w": 100, "d": 66, "height": 0, "edge": 5, "material": "sand"})
# the service yard and the truck well at the loading dock
F.append({"type": "flatten", "x": -2, "z": -40, "w": 84, "d": 14, "height": 0, "edge": 4, "material": "sand"})
# the multi-storey car park's pad and the west alley
F.append({"type": "flatten", "x": -54, "z": 4, "w": 20, "d": 28, "height": 0, "edge": 4, "material": "sand"})
# the car park: a plane falling 3.6 m to the south, painted asphalt
F.append({"type": "ramp", "points": [[-4, 30, 0], [-4, 58, -3.6]], "width": 136, "edge": 4, "material": "sand"})
# grassy islands between the bay rows (a kerb's height), and the berm along the south edge
for (x0, x1) in ((-58, -7), (7, 36)):
    F.append({"type": "ridge", "points": [[x0, 40.4], [x1, 40.4]], "width": 2.6, "height": 0.25, "falloff": "linear", "material": "grass"})
F.append({"type": "ridge", "points": [[-62, 55], [-20, 57], [20, 57], [37, 56]], "width": 8, "height": 1.6, "material": "grass"})
F.append({"type": "ridge", "points": [[51, 55], [60, 50]], "width": 8, "height": 1.6, "material": "grass"})
# the entrance road (south-east), the service road (north, climbing out through the embankment) and the east drive
F.append({"type": "ramp", "points": [[44, 48], [44, 56, -3.3], [44, 74, -3.0]], "width": 8, "edge": 3, "material": "sand"})
F.append({"type": "ramp", "points": [[-1.5, -44, 0], [-1.5, -52, 0.8], [-1.5, -72, 3.2]], "width": 8, "edge": 3, "material": "sand"})
F.append({"type": "ramp", "points": [[51, -42, 0], [51, 0, 0], [51, 30, -1.2], [48, 48]], "width": 9, "edge": 3, "material": "sand"})
# the west alley past the multi-storey, and the truck well at dock 1 (after the roads, which run on past their ends)
F.append({"type": "ramp", "points": [[-60, 29, 0], [-80, 29, 1.6]], "width": 7, "edge": 3, "material": "dirt"})
F.append({"type": "flatten", "x": 3, "z": -39.5, "w": 15, "d": 7, "height": -1.15, "edge": 3.5, "mode": "cut", "material": "sand"})
# the sunken fountain court in the atrium (between the bridges)
F.append({"type": "flatten", "x": 0, "z": 0, "w": 16, "d": 5, "height": -0.9, "edge": 2.6, "mode": "cut", "material": "sand"})
TERRAIN = {"cell": 0.5, "margin": 16,
           "surface": {"grass": "#5f7d3c", "dirt": "#7a6446", "rock": "#7d7a72", "sand": "#4a4a4c", "variation": 0.05},
           "features": F}

# ================================================================ objectives
BLUE_SP = [(-48, 48.6), (-36, 48.6), (-24, 48.6), (-12, 48.6), (12, 48.6), (24, 48.6), (30.5, 31.4), (-30.5, 31.4)]
RED_SP = [(-36, -43.5), (-26, -45), (-16, -43.5), (-9, -46), (16, -43.5), (26, -45), (36, -43.5), (-1.5, -57)]
FFA_SP = [(-22.6, 0, 0), (22.6, 0, 0), (-8, -6.5, 0), (8, 6.5, 0), (-17, -6.4, L1), (17, 6.4, L1), (-33, -12, 0), (31, -17, 0),
          (28, 9, L1), (-36, 14, L1), (-54, 10, 0), (52, -20, 0)]
ESC_TOP = []   # spawns at the escalator tops (on the bridges): nav nodes that join each escalator to the walkway below
for (foot_x, top_x, zc) in MB.ESC:
    d = 1 if foot_x > top_x else -1
    ESC_TOP.append((top_x - d * 0.9, zc, L1))
    ESC_TOP.append((foot_x + d * 0.9, zc, 0.1))         # the foot, on the walkway slab (a Waypoint would sit on the terrain under it)
# the bridges' ends and the balcony just past them, so the bridges join the balcony ring in the nav graph
for bx in (-12.0, 12.0):
    for sz in (-1, 1):
        ESC_TOP.append((bx, sz * 4.2, L1))
        ESC_TOP.append((bx, sz * 6.4, L1))
# the department store hill is just inside its upper arch (the doorway strip there is always kept free of furniture)
HILLS = [{"x": -6.5, "z": 0, "radius": 3.6}, {"x": 33, "z": 0, "y": 0.1, "radius": 5}, {"x": -25.0, "z": 0, "y": L1, "radius": 4.5}]
FLAGS = [[0, 50.5], [-20, -40.5]]

# ================================================================ the buildings
op("Ground", color=[0.36, 0.42, 0.3])
# "world": the pad is level at y = 0, and the sunken court under the atrium must not get a foundation slab filling it
ops.append({"op": "Building", "name": "Shopping Mall", "at": [0, 0, 0], "anchor": "world", "seed": 502, "variant": {"palette": "sandstone", "doors": "random"}})
ops.append({"op": "Building", "name": "Multi-Storey Car Park", "at": [-54, 4], "yaw": 90, "seed": 52, "variant": {"floors": 2}})

# sidewalks round the mall (kerbs: a step up from the asphalt)
PAVE = [0.62, 0.6, 0.56]
for (x0, z0, x1, z1) in ((-46, 23.15, -4.15, 30), (4.15, 23.15, 46, 30), (-4.15, 26.15, 4.15, 30), (-45.7, -23, -42.15, 23.15),
                         (42.15, -30, 46, 23.15), (-26, -32, -4.6, -29.15), (10.6, -32, 26, -29.15)):
    op("Block", x0=x0, y0=0, z0=z0, x1=x1, y1=0.12, z1=z1, color=PAVE)

# ================================================================ shop fronts: brand signs, logos, posters, gates
UNITS = {  # unit id -> (brand key, x0, x1, row, floor)
    "n1": ("Voltwave", -24, -12, "n", 0), "n2": ("Dog Eared Books", -12, -3, "n", 0), "n3": ("Sole Purpose", 3, 12, "n", 0), "n4": ("Full Tilt Sports", 12, 24, "n", 0),
    "s1": ("Loom Linen", -24, -12, "s", 0), "s2": ("Gilt Trip", -12, -4, "s", 0), "s3": ("Gumdrop Galaxy", 4, 12, "s", 0), "s4": ("Topsy Toys", 12, 24, "s", 0),
    "n1u": ("Token Town", -24, -12, "n", 1), "n2u": ("Gloss Lab", -12, -3, "n", 1), "n3u": (None, 3, 12, "n", 1), "n4u": ("Wagtail Pets", 12, 24, "n", 1),
    "s1u": ("Nest Nook", -24, -12, "s", 1), "s2u": ("Cellsius", -12, -4, "s", 1), "s3u": ("Customer Service", 4, 12, "s", 1), "s4u": ("Hemline", 12, 24, "s", 1),
}
SIGN_Y = (3.5, L1 + 3.45)
LOGO_Y = (3.0, L1 + 2.7)
SALE = ["Poster Sale", "Poster Clearance", "Poster Half Price", "Poster Buy One"]
for uid, (brand, x0, x1, row, fl) in UNITS.items():
    cx = (x0 + x1) / 2
    s = -1 if row == "n" else 1           # the front wall is at z = 8 * s, the back at 20 * s
    yb = 0.0 if fl == 0 else L1
    face = s * 8 - s * 0.15               # the front wall's atrium face
    yaw = 0 if row == "n" else 180
    if brand is None:
        prop("Store Hoarding", cx, face - s * 0.06, yaw, y=yb)
        continue
    prop(f"{brand} Sign", cx, face, yaw, y=SIGN_Y[fl])
    back = s * 20 - s * 0.22
    prop(f"{brand} Logo", cx - s * 2.2, back, yaw + 180, y=LOGO_Y[fl])
    prop("Security Gate Pair", cx, s * 8 + s * 0.7, yaw, y=yb)
    # a brand poster high on a side wall, a sale poster on the other
    inner = s * 14
    if brand != "Customer Service":
        decal(f"{brand} Poster", x0 + 0.8, inner, y=yb + 2.85, onto="wall-x", reach=1.5)
    decal(rng.choice(SALE), x1 - 0.8, inner + s * 2.5, y=yb + 2.85, onto="wall+x", reach=1.5)
    decal("Ceiling Tile Stain", cx + rng.uniform(-3, 3), s * 14 + rng.uniform(-3, 3), y=yb + (MB.F0H if fl == 0 else MB.F1H) - 0.3, onto="ceiling")

# department store, food court, cinema, the mall itself
prop("Holloway Finch Sign", -23.85, 0, 90, y=3.65)
prop("Holloway Finch Sign", -23.85, 0, 90, y=L1 + 3.5)
prop("Holloway Finch Sign", -33, 23.15, 0, y=5.2)
prop("Holloway Finch Logo", -41.85, 12, 90, y=2.8)
prop("Holloway Finch Logo", -24.15, -16, 270, y=L1 + 2.6)
prop("Food Court Sign", 23.85, 0, 270, y=3.62)
prop("Food Court Sign", 42.15, 0, 90, y=4.0)
prop("Stardust Cinemas Sign", 23.85, 0, 270, y=L1 + 3.5)
prop("Stardust Cinemas Sign", 42.15, 0, 90, y=L1 + 1.6)
prop("Stardust Cinemas Logo", 31.85, -6.0, 270, y=L1 + 2.6)
prop("Slopview Mall Sign", 0, 26.15, 0, y=4.05)
prop("Slopview Mall Pylon", 44, 47.5, 180)
for k, (z, brand) in enumerate(zip(MB.FOOD_STALLS, ("Wokstar", "Bun Deal", "Slice of Life"))):
    prop(f"{brand} Header", 41.82, z, 270, y=2.75)
for k, (x, film) in enumerate(((25.6, "Space Llamas"), (29.0, "The Last Donut"), (25.6, "Mall Of The Dead"), (29.0, "Pixel Hearts"))):
    zf = -13.85 if k < 2 else 13.85
    decal(f"Film Poster {film}", x, zf + (1.0 if k < 2 else -1.0), y=L1 + 1.7, onto="wall-z" if k < 2 else "wall+z", reach=1.5)
decal("Stardust Cinemas Poster", 23.6, -6.4, y=1.6, onto="wall+x", reach=1.5)
decal("Film Poster Space Llamas", 23.6, 6.4, y=1.6, onto="wall+x", reach=1.5)
decal("Poster Combo Deal", 28, -21.8, y=2.4, onto="wall-z", reach=1.5)
decal("Poster Free Refills", 37, 21.8, y=2.4, onto="wall+z", reach=1.5)
decal("Holloway Finch Poster", -24.4, -10, y=2.8, onto="wall+x", reach=1.5)
decal("Holloway Finch Poster", -24.4, 10, y=L1 + 2.6, onto="wall+x", reach=1.5)

# wayfinding
decal("Sign Food Court Right", -3.6, 17.5, y=2.7, onto="wall-x", reach=1.0)
decal("Sign Department Store Left", 3.6, 19.0, y=2.7, onto="wall+x", reach=1.0)
decal("Sign Cinema Upstairs", -3.6, 12.5, y=2.7, onto="wall-x", reach=1.0)
decal("Sign Restrooms Right", -2.2, -19.4, y=2.9, onto="wall-z", reach=1.2)
decal("Sign Staff Only", -1.5, -20.6, y=2.75, onto="wall+z", reach=1.0)
decal("Sign Mall Entrance", -6.5, 23.6, y=2.9, onto="wall-z", reach=1.2)
decal("Sign Mall Entrance", 6.5, 23.6, y=2.9, onto="wall-z", reach=1.2)
decal("Sign Dock 1", -1.0, -29.6, y=3.95, onto="wall+z", reach=1.2)
decal("Sign Dock 2", 6.0, -29.6, y=3.95, onto="wall+z", reach=1.2)
decal("Sign Staff Only", -17, -29.6, y=2.75, onto="wall+z", reach=1.2)
decal("Sign Staff Only", 17, -29.6, y=2.75, onto="wall+z", reach=1.2)
decal("Sign Deliveries", 42.6, -20, y=2.6, onto="wall-x", reach=1.2)
decal("Sign Fire Lane", -14, 23.6, y=2.75, onto="wall-z", reach=1.0)
decal("Sign Fire Lane", 14, 23.6, y=2.75, onto="wall-z", reach=1.0)
decal("Sign Food Court Left", 23.0, -7.0, y=2.6, onto="wall+x", reach=1.5)
decal("Sign Department Store Left", -23.0, 7.0, y=2.6, onto="wall-x", reach=1.5)
decal("Slopview Floor Medallion", 0, 20, y=0.1)

# ================================================================ the atrium
prop("Mall Fountain", 0, 0)
prop("Drip Drop Coffee Kiosk", 6.6, 0)
for (x, z, yaw) in ((-3.6, -3.4, 0), (3.6, 3.4, 180), (-10.0, 4.0, 180), (10.0, -4.0, 0)):
    prop("Mall Bench", x, z, yaw)
for (x, z) in ((-10.6, -4.2), (10.6, 4.2), (-22.6, -6.6), (22.6, 6.6), (-22.6, 6.6), (22.6, -6.6)):
    prop("Mall Planter Palm", x, z)
prop("Mall Kiosk Cart", -16.4, 0, 90)
prop("Mall Kiosk Cart", 16.4, 0, 270)
prop("Mall Directory Board", -4.6, 6.6, 0)
prop("Mall Directory Board", 4.0, -6.6, 180)
for (x, z) in ((-8.5, 6.4), (8.5, -6.4), (-20.0, -4.6), (20.0, 4.6)):
    prop("Mall Trash Bin", x, z)
# lights under the balconies, pendants and banners hanging from the skylight beams
for x in range(-21, 22, 6):
    for z in (-6.5, 6.5):
        prop("Mall Ceiling Light", x, z, 0, y=L1 - 0.2 - 0.69)
for x in (-20, -16, -8, -4, 4, 8, 16, 20):
    prop("Mall Pendant Light", x, 0, 0, y=ROOF + 0.3 - 1.5)
for (x, z) in ((-6, -4), (6, 4), (-18, 4), (18, -4)):
    prop("Sale Banner Hanging", x, z, 0 if z < 0 else 180, y=ROOF + 0.7 - 1.9)
# the upper balconies: palms at the corners, benches against the railing, a few bins
for (x, z) in ((-22.4, -6.4), (22.4, 6.4), (-22.4, 6.4), (22.4, -6.4)):
    prop("Mall Planter Palm", x, z, 0, y=L1)
for (x, z, yaw) in ((-6.0, -5.5, 0), (6.0, 5.5, 180), (-18.0, 5.5, 180), (18.0, -5.5, 0)):
    prop("Mall Bench", x, z, yaw, y=L1)
for (x, z) in ((-2.0, 5.5), (2.0, -5.5)):
    prop("Mall Trash Bin", x, z, 0, y=L1)
DS = ["Coffee Stain", "Water Stain", "Footprints", "Paper Litter", "Cigarette Butts"]
op("DecalScatter", decals=DS, x0=-23, z0=-7.6, x1=23, z1=-5.2, count=10, seed=1, y=0.1, gap=2.0)
op("DecalScatter", decals=DS, x0=-23, z0=5.2, x1=23, z1=7.6, count=10, seed=2, y=0.1, gap=2.0)
op("DecalScatter", decals=["Water Stain", "Puddle", "Coffee Stain"], x0=-9, z0=-2.6, x1=9, z1=2.6, count=6, seed=3, y=-0.9, gap=2.0)
op("DecalScatter", decals=DS, x0=-23, z0=-7.6, x1=23, z1=-5.2, count=8, seed=4, y=L1, gap=2.0)
op("DecalScatter", decals=DS, x0=-23, z0=5.2, x1=23, z1=7.6, count=8, seed=5, y=L1, gap=2.0)

# ================================================================ the car park (south): bay rows, islands, lamps, cars, carts
CARS = ["Sedan", "Sedan", "Hatchback", "SUV", "SUV", "Pickup Truck", "Van", "Taxi"]
BAY_W = 2.7
bays = []
for row_z, rot in ((35.6, 0), (45.2, 180)):
    x = -58.0
    while x < 37:
        if not (-8.5 < x < 8.5):
            bays.append((x, row_z, rot))
        x += BAY_W
for (x, z, rot) in bays:
    decal("Parking Bay", x, z, rotation=rot)
    near = abs(x) < 16
    if rng.random() < (0.75 if near else 0.5):
        prop(rng.choice(CARS), x + rng.uniform(-0.15, 0.15), z + rng.uniform(-0.3, 0.3), 90 + rot + rng.uniform(-4, 4))
for x in (-12.4, -9.7, 9.7, 12.4):
    decal("Accessible Bay Marking", x, 35.6)
# the walkway from the car park to the main doors, a crossing over the fire lane
decal("Crosswalk Bars", 0, 31.6, rotation=90, size=5.5)
for x in (-20, 20):
    decal("Fire Lane Stencil", x, 31.4)
for x in (-6.2, -4.2, 4.2, 6.2):
    prop("Bollard", x, 30.4)
# islands: trees, lamps and cart corrals; lamps along the rows
ISLAND_TREES = []
for x0, x1 in ((-58, -7), (7, 36)):
    x = x0 + 3
    k = 0
    while x < x1 - 2:
        if k % 2 == 0:
            ISLAND_TREES.append((x, 40.4))
        else:
            prop("Street Lamp", x, 40.4)
        x += 6.5
        k += 1
for (x, z) in ((-32, 38.6), (20, 42.2), (-46, 42.2)):
    prop("Cart Return Bay", x, z, 0 if z < 40 else 180)
for _ in range(14):
    x, z = rng.uniform(-56, 36), rng.choice([rng.uniform(31.5, 33.0), rng.uniform(38.4, 39.0), rng.uniform(47.8, 50.5)])
    if abs(x) < 8 or abs(z - 48.6) < 1.5:
        continue
    prop("Shopping Cart", x, z, rng.uniform(0, 360))
for x in (-40, -20, 20, 32):
    prop("Street Lamp", x, 52.6)
op("DecalScatter", decals=["Oil Stain", "Oil Drips", "Oil Drips", "Asphalt Patch", "Floor Cracks", "Skid Marks"], x0=-58, z0=31, x1=40, z1=52, count=70, seed=6, gap=2.5)
op("DecalScatter", decals=["Floor Cracks", "Puddle", "Leaf Litter", "Asphalt Patch"], x0=-58, z0=-22, x1=-44, z1=30, count=14, seed=7, gap=2.5)
# the bus stop and the entrance road
prop("Bus Shelter", 36.5, 50.4, 270)
prop("Bus Stop Sign", 37.0, 47.6, 270)
for z in (58, 62, 66):
    decal("Lane Dash", 44, z)
prop("Boom Barrier", 44, 66.5, 0)
prop("Traffic Cone", 41.5, 67.5)
prop("Traffic Cone", 46.5, 67.6)
prop("Fire Hydrant", 46.6, 30.6)
prop("Fire Hydrant", -46.6, 30.6)

# the east drive: parked vans, a dumpster, the food court's bins
for (x, z, n) in ((53.5, -28, "Box Truck"), (53.5, -14, "Van"), (53.5, 8, "Pickup Truck"), (53.5, 20, "Van")):
    prop(n, x, z, 90)
for (x, z) in ((46.6, -12), (46.6, 12)):
    prop("Dumpster", x, z, 90)
prop("Wheelie Bin", 46.6, -14.4, 90)
prop("Recycling Bin", 46.6, 14.4, 90)
op("DecalScatter", decals=["Oil Stain", "Tyre Tracks", "Floor Cracks", "Puddle"], x0=47, z0=-40, x1=56, z1=30, count=20, seed=8, gap=3)

# ================================================================ the service yard and loading dock (north)
prop("Box Truck", -1.0, -36.6, 90)                 # backed into the truck well at dock 1
prop("Truck Cab", 6.0, -38.0, 270)
decal("Loading Zone Stencil", 18, -38.5)
decal("Loading Zone Stencil", -16, -38.5)
decal("Hazard Stripes", -1.0, -29.6, y=0.15, onto="wall+z", reach=1.2)
decal("Hazard Stripes", 6.0, -29.6, y=0.15, onto="wall+z", reach=1.2)
for (x, z, yaw) in ((-22, -31.2, 0), (-12, -31.2, 0), (13, -31.2, 0), (22.5, -31.2, 0)):
    prop("Dumpster", x, z, yaw)
for (x, z) in ((-19.5, -33.5), (-9.5, -33.4), (16.0, -33.0)):
    prop("Pallet Stack", x, z)
for (x, z) in ((10.5, -35.5), (12.6, -35.0), (-7.0, -35.8)):
    prop("Pallet", x, z, rng.uniform(0, 90))
prop("Forklift", 15.0, -37.5, 30)
prop("Wooden Crate", 11.0, -33.2)
prop("Cardboard Stack", 8.6, -33.0)
for (x, z) in ((-30, -33.5), (30, -33.5)):
    prop("Floodlight Tower", x, z)
prop("Street Cabinet", -26.5, -30.6)
prop("Junction Box", 26.5, -30.6)
for x in (-38, -28, 28, 38):
    prop("Utility Trailer" if x == 38 else "Pallet Stack", x, -48.5, 90 if x == 38 else 0)
op("DecalScatter", decals=["Oil Stain", "Tyre Tracks", "Tyre Tracks", "Floor Cracks", "Puddle", "Cardboard Flat"], x0=-40, z0=-47, x1=40, z1=-31, count=40, seed=9, gap=2.5)
op("DecalScatter", decals=["Graffiti Tag", "Graffiti Scribble", "Grime Streaks", "Rust Bleed", "Grime Streaks"], x0=-24, z0=-30.2, x1=24, z1=-29.4, count=10, seed=10, onto="wall", gap=3)
op("DecalScatter", decals=["Grime Streaks", "Grime Streaks", "Water Damage"], x0=-42, z0=23.2, x1=42, z1=24.0, count=12, seed=11, onto="wall", gap=4)
op("DecalScatter", decals=["Grime Streaks", "Graffiti Tag", "Grime Streaks"], x0=42.2, z0=-28, x1=43, z1=22, count=8, seed=12, onto="wall", gap=4)
op("DecalScatter", decals=["Grime Streaks", "Water Damage"], x0=-43, z0=-22, x1=-42.2, z1=22, count=6, seed=13, onto="wall", gap=4)
# service corridors: scuffs and stains
for (x0, z0, x1, z1, y) in ((-23.5, -22.6, 23.5, -20.4, 0.1), (-23.5, 20.4, 23.5, 22.6, 0.1), (-23.5, -22.6, 23.5, -20.4, L1), (-23.5, 20.4, 23.5, 22.6, L1)):
    op("DecalScatter", decals=["Water Stain", "Floor Cracks", "Footprints", "Coffee Stain"], x0=x0, z0=z0, x1=x1, z1=z1, count=8, seed=int(x0 * z0) % 97 + int(y), y=y, gap=3)

# ================================================================ spawns and waypoints
for x, z in BLUE_SP + RED_SP:
    op("Spawn", x=float(x), z=float(z))
for x, z, y in FFA_SP + ESC_TOP:
    if y:
        op("Spawn", x=float(x), z=float(z), y=float(y))
    else:
        op("Spawn", x=float(x), z=float(z))
for (x, z) in ((0, -3.6), (0, 3.6), (-9.5, 0), (9.5, 0), (-6.5, 0), (-4.5, -2.0), (4.5, 2.0), (-12.0, -6.5), (12.0, 6.5), (0, 28), (3, -32), (-1.5, -50), (44, 60), (-70, 29), (51, -36), (51, 36)):
    op("Waypoint", x=float(x), z=float(z))
op("AutoWaypoints", spacing=5)

# ================================================================ litter (the Clutter op runs last and keeps clear of everything solid)
INSIDE_LITTER = ["Litter", "Soda Cans", "Newspaper", "Plastic Bag", "Carrier Bag", "Voltwave Bag", "Loom Linen Bag", "Holloway Finch Bag", "Topsy Toys Bag",
                 "Gilt Trip Bag", "Gumdrop Galaxy Bag", "Carrier Bag"]
op("Clutter", props=INSIDE_LITTER, x0=-23.5, z0=-7.8, x1=23.5, z1=7.8, count=60, seed=21)
op("Clutter", props=INSIDE_LITTER + ["Mall Trash Bin"], x0=24.5, z0=-22.5, x1=41.5, z1=22.5, count=30, seed=22)
op("Clutter", props=INSIDE_LITTER, x0=-41.5, z0=-22.5, x1=-24.5, z1=22.5, count=30, seed=23)
op("Clutter", props=["Litter", "Soda Cans", "Plastic Bag", "Newspaper", "Bottle", "Shopping Cart", "Trash Can"], x0=-58, z0=30.5, x1=40, z1=53, count=90, seed=24)
op("Clutter", props=["Litter", "Trash Bag", "Flattened Cardboard", "Cardboard Box", "Pallet", "Plastic Bag", "Bottles", "Tyre", "Cigarette Bin"], x0=-42, z0=-48, x1=42, z1=-30.5, count=110, seed=25)
op("Clutter", props=["Cardboard Box", "Flattened Cardboard", "Litter", "Cardboard Box Small"], x0=-23.5, z0=-22.8, x1=23.5, z1=-20.2, count=24, seed=26)
op("Clutter", props=["Cardboard Box", "Flattened Cardboard", "Litter", "Cardboard Box Small"], x0=-23.5, z0=20.2, x1=23.5, z1=22.8, count=24, seed=27)
op("Clutter", props=["Litter", "Trash Bag", "Plastic Bag", "Soda Cans", "Tyre"], x0=-77, z0=23.8, x1=-60, z1=33.2, count=20, seed=28)

# ================================================================ foliage
inside_lot = [[-46, -34], [46, -34], [46, 31], [-46, 31]]
foliage = {
    "seed": 502,
    "surfaces": {"Sand": 0.04, "Concrete": 0.02},
    "paths": [{"points": [[44, 46], [44, 70]], "width": 8}, {"points": [[-1.5, -44], [-1.5, -70]], "width": 8},
              {"points": [[-60, 29], [-78, 29]], "width": 6}, {"points": [[51, -42], [51, 30], [48, 48]], "width": 9}],
    "exclude": [{"shape": "polygon", "points": inside_lot}, {"shape": "rect", "center": [-54, 4], "halfX": 10, "halfZ": 14}],
    "regions": [
        {"type": "grass", "density": 4.5, "rect": [-80, -70, 70, 72], "patchiness": 0.4, "scale": [1.0, 1.6]},
        {"type": "shrub", "density": 0.6, "polygon": [[-70, -46], [70, -46], [70, -66], [-70, -66]]},
        {"type": "bush", "density": 0.25, "polygon": [[-70, -46], [70, -46], [70, -66], [-70, -66]], "scale": [0.9, 1.5]},
        {"type": "tree", "density": 0.045, "polygon": [[-66, -47], [-8, -48], [-8, -66], [-66, -66]], "height": [6.5, 10]},
        {"type": "tree", "density": 0.045, "polygon": [[5, -48], [66, -46], [66, -66], [5, -66]], "height": [6.5, 10]},
        {"type": "pine", "density": 0.02, "polygon": [[-66, -47], [66, -47], [66, -66], [-66, -66]], "height": [8, 12]},
        {"type": "tree", "density": 0.04, "polygon": [[57, -44], [70, -44], [70, 50], [57, 48]], "height": [6, 9.5]},
        {"type": "shrub", "density": 0.8, "polygon": [[56, -44], [70, -44], [70, 50], [56, 48]]},
        {"type": "tree", "density": 0.04, "polygon": [[-80, -44], [-64.5, -44], [-64.5, 21], [-80, 21]], "height": [6, 9]},
        {"type": "tree", "density": 0.04, "polygon": [[-80, 36], [-63, 36], [-60, 54], [-80, 54]], "height": [6, 9]},
        {"type": "shrub", "density": 0.9, "polygon": [[-62, 53], [38, 54], [38, 60], [-62, 60]]},
        {"type": "bush", "density": 0.3, "polygon": [[-62, 53], [38, 54], [38, 60], [-62, 60]], "scale": [0.9, 1.4]},
        {"type": "tree", "density": 0.02, "polygon": [[-62, 54], [38, 55], [38, 60], [-62, 60]], "height": [5.5, 8]},
        {"type": "shrub", "density": 1.2, "rect": [-58, 39.6, 36, 41.2]},
    ],
    "items": [{"type": "tree", "x": x, "z": z, "height": round(rng.uniform(5.5, 7.5), 1)} for (x, z) in ISLAND_TREES],
}

mp = {
    "format": "call-of-slop-map/1",
    "name": "Slopview Mall",
    "description": "An American shopping mall: a skylit two-storey atrium with escalators, bridges and a sunken fountain court, sixteen branded shops, a food court and cinema, service corridors, the loading dock, and a car park sloping away into wooded banks.",
    "author": "Call of Slop",
    "order": 502,
    "maxPlayers": 16,
    "flagPool": ["us"],
    "skybox": {"preset": "clear", "sun": {"time": 15.5, "latitude": 38}, "clouds": {"coverage": 0.35, "speed": 8}},
    "boundary": {"shape": "polygon", "points": [list(p) for p in POLY], "noise": NOISE, "edge": {"style": "cliff", "height": 7, "width": 6}, "groundMargin": 10},
    "terrain": TERRAIN,
    "teamSpawns": [[list(p) for p in BLUE_SP], [list(p) for p in RED_SP]],
    "hills": HILLS,
    "flags": FLAGS,
    "ops": ops,
    "foliage": foliage,
}
# a flag or two flying the US flag by the main entrance and the pylon
ops.insert(3, {"op": "Prop", "name": "Flag on a Pole", "x": -8.5, "z": 29.0, "flag": "us"})
ops.insert(4, {"op": "Prop", "name": "Flag on a Pole", "x": 8.5, "z": 29.0, "flag": "us"})


def dump(o, ind=0):
    pad = "  " * ind
    if isinstance(o, dict) and ind <= 1 and any(isinstance(v, (dict, list)) for v in o.values()):
        return "{\n" + ",\n".join(f'{pad}  "{k}": {dump(v, ind + 1)}' for k, v in o.items()) + "\n" + pad + "}"
    if isinstance(o, list) and ind <= 2 and o and isinstance(o[0], dict):
        return "[\n" + ",\n".join(pad + "  " + json.dumps(v) for v in o) + "\n" + pad + "]"
    return json.dumps(o)


os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w") as f:
    f.write(dump(mp) + "\n")
n = {}
for o in ops:
    n[o["op"]] = n.get(o["op"], 0) + 1
print(OUT, n)
