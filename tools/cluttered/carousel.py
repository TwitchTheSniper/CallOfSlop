"""Usage: python3 tools/cluttered/carousel.py [out.json] (needs nothing but Python 3).

Generates Maps/Cluttered/01-Carousel.json: an overgrown, abandoned fairground in a round clearing.

Built as Blue's half (x < 0, plus pieces centred on x = 0 with z < 0 ... or listed explicitly) and turned 180 degrees
about the centre for Red: every op in HALF gets a twin at (-x, -z). CENTER ops are emitted once (self-symmetric).
"""
import json, math, sys

OUT = sys.argv[1] if len(sys.argv) > 1 else __import__("os").path.join(__import__("os").path.dirname(__import__("os").path.abspath(__file__)), "..", "..", "Maps", "Cluttered", "01-Carousel.json")
R = 34.0

# ---- palette -------------------------------------------------------------------------------------------------------
WOOD = "#8a6a48"; DARKWOOD = "#5e4630"; PAINT_RED = "#b8433a"; PAINT_CREAM = "#e8dcc0"; PAINT_BLUE = "#3f6f9e"
PAINT_GREEN = "#4f8a5b"; PAINT_YELLOW = "#d9a93a"; PAINT_TEAL = "#3c8c8a"; PAINT_PINK = "#c46a8e"; RUST = "#8a5a3c"
BRASS = "#d4af37"; STEEL = "#8c9096"; CANVAS = "#d8cfb4"; HAY = "#c9a85a"; CONCRETE = "#9a968e"

HALF = []    # Blue's half: mirrored
CENTER = []  # emitted once


def r2(v):
    return round(v, 3)


def mirror(op):
    o = dict(op)
    t = o["op"]
    if t == "Block":
        o["x0"], o["x1"] = r2(-op["x1"]), r2(-op["x0"])
        o["z0"], o["z1"] = r2(-op["z1"]), r2(-op["z0"])
    elif t in ("Prop", "Container", "Mesh", "Spawn", "Waypoint", "Model", "Car", "Tank"):
        o["x"], o["z"] = r2(-op["x"]), r2(-op["z"])
        if t in ("Prop", "Mesh", "Model") and ("name" in op or t != "Prop"):
            o["yaw"] = r2((op.get("yaw", 0) + 180) % 360)
    elif t == "StairsX":
        o["z0"], o["z1"] = r2(-op["z1"]), r2(-op["z0"])
        o["x"] = r2(-op["x"]); o["dir"] = -op["dir"]
    elif t == "StairsZ":
        o["x0"], o["x1"] = r2(-op["x1"]), r2(-op["x0"])
        o["z"] = r2(-op["z"]); o["dir"] = -op["dir"]
    elif t == "Ramp":
        o["x"], o["z"] = r2(-op["x"]), r2(-op["z"]); o["dir"] = -op["dir"]
    elif t == "Watchtower":
        o["x"], o["z"] = r2(-op["x"]), r2(-op["z"]); o["stairDir"] = -op.get("stairDir", 1)
    elif t == "CoverLine":
        for a in ("x0", "z0", "x1", "z1"):
            o[a] = r2(-op[a])
    else:
        raise ValueError(t)
    return o


def block(x0, y0, z0, x1, y1, z1, color, to=HALF):
    to.append({"op": "Block", "x0": r2(min(x0, x1)), "y0": r2(y0), "z0": r2(min(z0, z1)),
               "x1": r2(max(x0, x1)), "y1": r2(y1), "z1": r2(max(z0, z1)), "color": color})


def prop(name, x, z, yaw=0, to=HALF, **kw):
    d = {"op": "Prop", "name": name, "x": r2(x), "z": r2(z)}
    if yaw:
        d["yaw"] = r2(yaw % 360)
    d.update(kw)
    to.append(d)


def mesh(kind, x, z, color, to=HALF, **kw):
    d = {"op": "Mesh", "mesh": kind, "x": r2(x), "z": r2(z), "color": color}
    d.update({k: (r2(v) if isinstance(v, float) else v) for k, v in kw.items()})
    to.append(d)


def balloons(x, z, y, n, seed, to=HALF):
    cols = [PAINT_RED, PAINT_YELLOW, PAINT_BLUE, PAINT_PINK, PAINT_GREEN, "#e07a2f"]
    for i in range(n):
        a = (seed * 1.7 + i * 2.39996)
        rr = 0.25 + 0.18 * ((i * 7 + seed) % 3)
        mesh("sphere", x + math.cos(a) * rr, z + math.sin(a) * rr, cols[(i + seed) % len(cols)], to,
             width=0.42, height=0.5, depth=0.42, y=y + 0.12 * ((i * 5 + seed) % 4), segments=10, detail=6)


def stall(cx, cz, w, d, facing, roof_col, seed):
    """A fairground game stall: back wall, counter at the front (open side), roof on four posts, striped awning.
    facing: 'S' (opens to +z), 'N', 'E' (+x), 'W'. w is along the front, d is the depth."""
    hx, hz = (w / 2, d / 2) if facing in "NS" else (d / 2, w / 2)
    x0, x1, z0, z1 = cx - hx, cx + hx, cz - hz, cz + hz
    H = 2.6
    # posts
    for px, pz in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
        block(px - 0.08 if px == x0 else px - 0.16, 0, pz - 0.08 if pz == z0 else pz - 0.16,
              px + 0.16 if px == x0 else px + 0.08, H, pz + 0.16 if pz == z0 else pz + 0.08, DARKWOOD)
    t = 0.2
    if facing == "S":
        block(x0, 0, z0, x1, H, z0 + t, WOOD)                       # back wall
        block(x0 + 0.2, 0, z1 - 0.55, x1 - 0.2, 1.1, z1, PAINT_CREAM)  # counter
        block(x0, 0, z0 + t, x0 + t, 1.1, z1 - 0.55, WOOD)           # low side walls
        block(x1 - t, 0, z0 + t, x1, 1.1, z1 - 0.55, WOOD)
        aw = dict(width=w + 0.4, height=0.5, depth=1.2, yaw=0.0)
        ax, az = cx, z1 + 0.35
    elif facing == "N":
        block(x0, 0, z1 - t, x1, H, z1, WOOD)
        block(x0 + 0.2, 0, z0, x1 - 0.2, 1.1, z0 + 0.55, PAINT_CREAM)
        block(x0, 0, z0 + 0.55, x0 + t, 1.1, z1 - t, WOOD)
        block(x1 - t, 0, z0 + 0.55, x1, 1.1, z1 - t, WOOD)
        aw = dict(width=w + 0.4, height=0.5, depth=1.2, yaw=180.0)
        ax, az = cx, z0 - 0.35
    elif facing == "E":
        block(x0, 0, z0, x0 + t, H, z1, WOOD)
        block(x1 - 0.55, 0, z0 + 0.2, x1, 1.1, z1 - 0.2, PAINT_CREAM)
        block(x0 + t, 0, z0, x1 - 0.55, 1.1, z0 + t, WOOD)
        block(x0 + t, 0, z1 - t, x1 - 0.55, 1.1, z1, WOOD)
        aw = dict(width=1.2, height=0.5, depth=w + 0.4, yaw=0.0)
        ax, az = x1 + 0.35, cz
    else:  # W
        block(x1 - t, 0, z0, x1, H, z1, WOOD)
        block(x0, 0, z0 + 0.2, x0 + 0.55, 1.1, z1 - 0.2, PAINT_CREAM)
        block(x0 + 0.55, 0, z0, x1 - t, 1.1, z0 + t, WOOD)
        block(x0 + 0.55, 0, z1 - t, x1 - t, 1.1, z1, WOOD)
        aw = dict(width=1.2, height=0.5, depth=w + 0.4, yaw=0.0)
        ax, az = x0 - 0.35, cz
    block(x0 - 0.1, H, z0 - 0.1, x1 + 0.1, H + 0.15, z1 + 0.1, roof_col)  # roof
    # striped awning (cosmetic): two colours, slightly different heights so they don't flicker
    mesh("roundedbox", ax, az, PAINT_CREAM, y=H - 0.45, radius=0.08, **aw)
    mesh("roundedbox", ax, az, roof_col, y=H - 0.43, radius=0.08,
         **{**aw, "width": aw["width"] * (0.55 if facing in "NS" else 1.0), "depth": aw["depth"] * (1.0 if facing in "NS" else 0.55)})
    # prize shelf and prizes (cosmetic) against the back wall
    balloons(cx, cz, H + 0.6, 5, seed)


def ticket_booth(cx, cz, col):
    block(cx - 1, 0, cz - 1, cx + 1, 2.5, cz + 1, col)
    block(cx - 1.3, 2.5, cz - 1.3, cx + 1.3, 2.7, cz + 1.3, PAINT_CREAM)
    mesh("cone", cx, cz, PAINT_RED, y=2.7, width=2.4, height=1.0, depth=2.4, segments=4, yaw=45.0)


def food_truck(cx, cz, along_x, col, hatch_side):
    L, W = 5.6, 2.3
    hx, hz = (L / 2, W / 2) if along_x else (W / 2, L / 2)
    block(cx - hx, 0.35, cz - hz, cx + hx, 2.9, cz + hz, col)               # body
    block(cx - hx + 0.3, 0, cz - hz + 0.2, cx + hx - 0.3, 0.35, cz + hz - 0.2, "#26262a")  # wheels/chassis
    # cab at one end
    if along_x:
        block(cx + hx, 0.35, cz - hz + 0.1, cx + hx + 1.5, 2.1, cz + hz - 0.1, PAINT_CREAM)
    else:
        block(cx - hx + 0.1, 0.35, cz - hz - 1.5, cx + hx - 0.1, 2.1, cz - hz, PAINT_CREAM)
    # serving hatch awning (cosmetic) and a sign on the roof
    if along_x:
        az = cz + (hz + 0.5) * hatch_side
        mesh("roundedbox", cx, az, PAINT_YELLOW, y=2.2, width=L * 0.7, height=0.12, depth=1.1, radius=0.05)
        mesh("roundedbox", cx, cz, PAINT_RED, y=2.9, width=2.4, height=0.7, depth=0.15, radius=0.05)
    else:
        ax = cx + (hx + 0.5) * hatch_side
        mesh("roundedbox", ax, cz, PAINT_YELLOW, y=2.2, width=1.1, height=0.12, depth=L * 0.7, radius=0.05)
        mesh("roundedbox", cx, cz, PAINT_RED, y=2.9, width=0.15, height=0.7, depth=2.4, radius=0.05)


def umbrella_table(x, z, col, chairs=4, seed=0):
    prop("Table", x, z, yaw=(seed * 37) % 180)
    mesh("cylinder", x, z, STEEL, width=0.06, depth=0.06, height=2.3, segments=6)
    mesh("cone", x, z, col, y=2.0, width=2.4, depth=2.4, height=0.55, segments=8)
    for k in range(chairs):
        a = math.radians(seed * 23 + k * 360 / chairs)
        prop("Plastic Chair", x + math.cos(a) * 1.05, z + math.sin(a) * 1.05, yaw=-math.degrees(a) + 90)


def lamp(x, z, yaw):
    prop("Street Lamp", x, z, yaw=yaw)


# ====================================================================================================================
# CENTRE: the carousel (static core + cosmetic canopy; the rotating deck is a mover)
# ====================================================================================================================
block(-0.9, 0, -0.9, 0.9, 4.2, 0.9, PAINT_RED, CENTER)
mesh("cylinder", 0, 0, PAINT_RED, CENTER, width=2.8, depth=2.8, height=3.3, segments=16, material="Wood")
mesh("cylinder", 0, 0, BRASS, CENTER, width=3.0, depth=3.0, height=0.25, y=3.3, segments=16)
mesh("cylinder", 0, 0, PAINT_CREAM, CENTER, width=10.6, depth=10.6, height=0.45, y=3.55, segments=24)
mesh("cone", 0, 0, PAINT_RED, CENTER, width=11.0, depth=11.0, height=2.2, y=4.0, segments=12)
mesh("cone", 0, 0, PAINT_CREAM, CENTER, width=11.06, depth=11.06, height=2.21, y=4.0, segments=12, yaw=15.0)
mesh("sphere", 0, 0, BRASS, CENTER, width=0.6, height=0.6, depth=0.6, y=6.1)
mesh("cylinder", 0, 0, BRASS, CENTER, width=0.08, depth=0.08, height=1.4, y=6.6, segments=6)
mesh("torus", 0, 0, PAINT_YELLOW, CENTER, radius=5.3, inner=0.09, y=3.55, segments=32, detail=6)
# round stone kerb round the deck (cosmetic) so the ride reads from far away
mesh("tube", 0, 0, CONCRETE, CENTER, radius=5.25, inner=4.95, height=0.12, segments=32, material="Concrete")

carousel = {"type": "rotate", "name": "Carousel", "at": [0, 0.42, 0], "speed": 14, "color": WOOD, "boxes": []}
for k in range(8):
    a = math.radians(45 * k)
    carousel["boxes"].append({"size": [3.0, 0.42, 3.0], "offset": [r2(3.0 * math.cos(a)), -0.21, r2(3.0 * math.sin(a))],
                              "yaw": 45 * k, "color": "#9c6b45"})
seat_cols = [PAINT_CREAM, PAINT_BLUE, PAINT_PINK, PAINT_GREEN]
for k in range(4):
    a = math.radians(22.5 + 90 * k)
    px, pz = r2(3.6 * math.cos(a)), r2(3.6 * math.sin(a))
    carousel["boxes"].append({"size": [0.12, 2.6, 0.12], "offset": [px, 1.3, pz], "color": BRASS})
    carousel["boxes"].append({"size": [0.55, 0.45, 0.55], "offset": [px, 1.05, pz], "yaw": 22.5 + 90 * k, "color": seat_cols[k]})

# ====================================================================================================================
# BLUE'S HALF (west). Red's half is this turned 180 degrees.
# ====================================================================================================================

# ---- Base: the west entrance gate --------------------------------------------------------------------------------
block(-24.8, 0, -3.6, -24.2, 3.4, 3.6, PAINT_BLUE)                    # gate screen (blocks the mid line from spawn)
for zz in (-6.2, 6.2):
    block(-24.9, 0, zz - 0.35, -24.1, 3.4, zz + 0.35, PAINT_CREAM)      # gate pillars
block(-24.9, 3.4, -6.6, -24.1, 4.0, 6.6, PAINT_RED)                   # lintel
mesh("roundedbox", -24.0, 0, PAINT_YELLOW, y=4.0, width=0.2, height=1.1, depth=7.5, radius=0.1)  # sign board
mesh("roundedbox", -23.9, 0, PAINT_RED, y=4.25, width=0.1, height=0.6, depth=6.2, radius=0.05)
balloons(-24.5, -6.2, 4.3, 6, 1)
balloons(-24.5, 6.2, 4.3, 6, 2)
ticket_booth(-20.5, -8.2, PAINT_BLUE)
ticket_booth(-20.5, 8.2, PAINT_BLUE)
# turnstile barriers between booths and gate
prop("Sawhorse Barricade", -22.6, -4.6, yaw=90)
prop("Sawhorse Barricade", -22.6, 4.6, yaw=90)
prop("Traffic Cone", -22.4, -11.0)
prop("Traffic Cone", -22.0, 11.2)
prop("Bench", -31.3, -4.8, yaw=90)
prop("Bench", -31.3, 4.8, yaw=90)
prop("Trash Can", -26.2, -5.2)
prop("Trash Can", -26.2, 5.2)
lamp(-26.0, -10.5, 90)
lamp(-26.0, 10.5, 90)

SPAWNS_BLUE = [(-29.0, -6.0), (-29.0, -2.5), (-29.0, 2.5), (-29.0, 6.0), (-26.8, -8.8), (-26.8, 8.8)]
SPAWNS_FFA = [(-4.0, -29.0), (-21.0, -23.0)]
for x, z in SPAWNS_BLUE + SPAWNS_FFA:
    HALF.append({"op": "Spawn", "x": x, "z": z})

# ---- North-west: the midway (game stalls), the caravan, the stage --------------------------------------------------
stall(-17.0, -24.6, 4.2, 2.2, "S", PAINT_RED, 3)
stall(-10.0, -27.6, 4.2, 2.2, "S", PAINT_BLUE, 4)
stall(-22.4, -17.4, 3.8, 2.2, "E", PAINT_GREEN, 5)
prop("Wooden Crate", -15.6, -26.7)
prop("Small Crate", -16.7, -26.9)
prop("Cardboard Stack", -13.4, -29.6, yaw=10)
prop("Gas Cylinders", -7.2, -29.9)

# caravan with crate steps up to its roof (Blue's north flank high ground)
HALF.append({"op": "Container", "x": -24.0, "z": -12.6, "alongX": False, "color": "#d9cdb0"})
mesh("roundedbox", -22.68, -12.6, PAINT_TEAL, y=1.0, width=0.06, height=0.5, depth=5.6, radius=0.02)  # paint stripe
for i, h in enumerate((0.5, 1.0, 1.55, 2.1)):
    zc = -10.6 - i * 1.0
    block(-22.75, 0, zc - 0.5, -21.75, h, zc + 0.5, WOOD if i % 2 == 0 else "#7c5e3e")

# stage (centred on x = 0, north): deck, stairs W/E/S, backdrop, canopy
block(-4.5, 0, -25.0, 4.5, 1.6, -20.5, DARKWOOD)
HALF.append({"op": "StairsX", "z0": -24.4, "z1": -22.8, "x": -6.3, "dir": 1, "y0": 0, "height": 1.6, "color": WOOD})
HALF.append({"op": "StairsX", "z0": -24.4, "z1": -22.8, "x": 6.3, "dir": -1, "y0": 0, "height": 1.6, "color": WOOD})
HALF.append({"op": "StairsZ", "x0": -1.0, "x1": 1.0, "z": -18.7, "dir": -1, "y0": 0, "height": 1.6, "color": WOOD})
block(-4.5, 1.6, -25.35, 4.5, 4.8, -25.0, PAINT_RED)                   # backdrop
block(-4.5, 1.6, -21.0, -3.2, 2.1, -20.5, PAINT_CREAM)                # footlight lips (low cover on the deck)
block(3.2, 1.6, -21.0, 4.5, 2.1, -20.5, PAINT_CREAM)
for xx in (-4.4, 4.4):
    block(xx - 0.12, 1.6, -20.62, xx + 0.12, 4.8, -20.38, BRASS)       # front posts
mesh("roundedbox", 0, -22.9, PAINT_RED, y=4.8, width=9.6, height=0.25, depth=5.0, radius=0.1)  # canopy (cosmetic)
mesh("roundedbox", 0, -20.4, PAINT_CREAM, y=4.4, width=9.4, height=0.4, depth=0.12, radius=0.05)
for i in range(7):
    mesh("sphere", -3.6 + i * 1.2, -20.35, [PAINT_YELLOW, PAINT_RED, PAINT_BLUE][i % 3], y=4.15, width=0.3, height=0.3, depth=0.3, segments=8, detail=5)
# audience: hay bale rows in front of the stage (west half of both stages; their mirrors fill the east halves)
for zz in (-17.6, -18.8):
    prop("Hay Bale", -3.6, zz)
    prop("Hay Bale", -2.2, zz)
for zz in (17.6, 18.8):
    prop("Hay Bale", -3.6, zz)
    prop("Hay Bale", -2.2, zz)
prop("Cable Spool", -5.6, -26.4)
prop("Wooden Crate", -3.0, -26.6)
prop("Cable Spool", -3.4, 26.6)       # behind the south stage (west side)
prop("Wooden Crate", -5.6, 26.4)

# operator tower between the midway and the plaza
HALF.append({"op": "Watchtower", "x": -7.6, "z": -14.6, "height": 2.4, "color": "#7a6248", "stairDir": 1})
balloons(-7.6, -14.6, 5.3, 4, 6)

# low cover along the inside of the midway lane
HALF.append({"op": "CoverLine", "x0": -21.0, "z0": -9.8, "x1": -12.5, "z1": -11.6, "height": 1.1, "thick": 0.7,
             "color": PAINT_CREAM, "segment": 2.6, "gap": 1.8})
prop("Trash Can", -15.0, -20.4)
prop("Wheelie Bin", -12.6, -21.2, yaw=20)
prop("Bench", -19.4, -20.8, yaw=45)
lamp(-13.8, -18.0, 0)

# ---- South-west: the food court (food trucks, picnic tables) -------------------------------------------------------
food_truck(-14.6, 22.4, True, PAINT_TEAL, -1)
food_truck(-21.6, 15.4, False, PAINT_PINK, 1)
umbrella_table(-9.2, 17.6, PAINT_RED, 4, 1)
umbrella_table(-12.8, 14.0, PAINT_BLUE, 3, 2)
umbrella_table(-8.0, 21.4, PAINT_YELLOW, 4, 3)  # (clear of the foot of the stage stairs; the map file was edited to match)
prop("Vending Machine", -18.4, 25.2, yaw=180)
prop("Vending Machine", -17.3, 25.2, yaw=180)
prop("Explosive Barrel", -11.2, 25.0)          # the trucks' generator fuel: one per side, far from spawns
prop("Oil Drum", -10.4, 25.6)
prop("Wheelie Bin Grey", -24.6, 11.8, yaw=80)
prop("Recycling Bin", -18.0, 19.0)
prop("Trash Can", -9.6, 20.4)
lamp(-15.6, 17.2, 0)
prop("Pallet Stack", -25.0, 19.4, yaw=90)
# hay bale wall on the inside of the food court lane
for i, (x, z) in enumerate([(-20.4, 10.6), (-18.6, 11.0), (-15.4, 11.4), (-13.6, 11.8)]):
    prop("Hay Bale", x, z, yaw=8 * (i % 2))
    if i % 2 == 0:
        prop("Hay Bale", x + 0.1, z, y=0.52, yaw=-6)

# ---- Mid-west: the plaza round the carousel --------------------------------------------------------------------
# ticket kiosk (tall) and candy cart (low) either side of the mid lane
block(-11.8, 0, -5.3, -10.2, 2.4, -3.7, PAINT_YELLOW)
mesh("cone", -11.0, -4.5, PAINT_RED, y=2.4, width=2.2, height=0.9, depth=2.2, segments=8)
block(-11.9, 0.25, 3.5, -10.1, 1.15, 4.5, PAINT_PINK)
block(-11.8, 0, 3.6, -10.2, 0.25, 4.4, "#26262a")
mesh("cylinder", -11.0, 4.0, STEEL, width=0.06, depth=0.06, height=2.4, segments=6)
mesh("cone", -11.0, 4.0, PAINT_CREAM, y=2.1, width=2.0, depth=2.0, height=0.5, segments=8)
balloons(-10.4, 4.0, 2.7, 6, 7)
prop("Planter", -8.2, 0.0)
prop("Bench", -6.4, -5.6, yaw=45)
prop("Bench", -6.4, 5.6, yaw=135)
lamp(-8.2, -7.4, 45)
lamp(-8.4, 7.2, 135)
prop("Trash Can", -5.4, -7.4)
prop("Planter", -2.4, -9.0)
prop("Planter", -2.4, 9.0)
prop("Fire Hydrant", -16.6, -2.4)
prop("Mailbox", -16.6, 2.2, yaw=90)

# ---- Trees (trunks are cover): hand-placed pairs -----------------------------------------------------------------
TREES = [(-14.4, -15.6, 7.5), (-12.4, 8.6, 6.5), (-2.5, 30.0, 8.0), (-28.2, -16.6, 7.0), (-21.5, 23.6, 7.5), (-6.0, -31.0, 6.5)]

# ---- Waypoints in cluttered pockets (stage top is a box top; auto waypoints cover the floor) ------------------------
for x, z in [(-12.0, -25.0), (-24.0, -20.0), (-20.0, 22.0), (-9.0, 20.5), (-26.5, 15.0)]:
    HALF.append({"op": "Waypoint", "x": x, "z": z})

# ====================================================================================================================
# Assemble
# ====================================================================================================================
ops = [{"op": "Ground", "color": [0.4, 0.36, 0.27]}]
ops += CENTER
for op in HALF:
    ops.append(op)
for op in HALF:
    ops.append(mirror(op))

# ---- Hand-placed solid clusters (the game's Clutter keeps solid props off every bot route, which on a map this full
# leaves almost no room): small piles of crates, bales and bins in the pockets between the lanes, found by a greedy
# search for spots well clear of boxes, props, paths, spawns and objectives, and mirrored like everything else.
import random
sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
import carousel_boxes as mapboxes
_tmp = {"ops": ops}
_boxes, _props, _, _spawns, _ = mapboxes.extract(_tmp)
HILLS = [(0, -13.0), (0, 13.0)]
FLAGS = [(-31.0, 0), (31.0, 0)]
def _seg_dist(px, pz, ax, az, bx, bz):
    dx, dz = bx - ax, bz - az
    t = max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / (dx * dx + dz * dz + 1e-9)))
    return math.hypot(px - ax - t * dx, pz - az - t * dz)
_paths_all = []
for pl in (north_lane_pts := [[-26, -5], [-21, -14], [-13, -19.5], [-6, -17], [0, -15.5], [6, -17], [13, -19.5], [21, -14], [26, -5]],
           [[-25, 0], [-6.5, 0]], [[-12, -12], [-14, -6], [-12, 0], [-14, 6], [-12, 12]]):
    for q in (pl, [[-x, -z] for x, z in pl]):
        _paths_all += [(q[i][0], q[i][1], q[i + 1][0], q[i + 1][1]) for i in range(len(q) - 1)]
def _box_dist(px, pz, b):
    dx = max(b[0] - px, 0, px - b[3]); dz = max(b[2] - pz, 0, pz - b[5])
    return math.hypot(dx, dz)
def _ok(x, z, placed):
    r = math.hypot(x, z)
    if r < 8.0 or r > 30.8:
        return False
    if any(_box_dist(x, z, b) < 2.4 for b in _boxes if b[4] > 0.3):
        return False
    if any(math.hypot(x - o["x"], z - o["z"]) < 2.2 for o in _props):
        return False
    if any(math.hypot(x - sx, z - sz) < 4.0 for sx, sz in _spawns):
        return False
    if any(math.hypot(x - hx, z - hz) < 5.2 for hx, hz in HILLS) or any(math.hypot(x - fx, z - fz) < 3.5 for fx, fz in FLAGS):
        return False
    if any(_seg_dist(x, z, *sg) < 2.8 for sg in _paths_all):
        return False
    if any(math.hypot(x - tx, z - tz) < 2.0 or math.hypot(x + tx, z + tz) < 2.0 for tx, tz, _h in TREES):
        return False
    if any(math.hypot(x - qx, z - qz) < 3.2 or math.hypot(x + qx, z + qz) < 3.2 for qx, qz in placed):
        return False
    return True
_rng = random.Random(7)
_cands = [(x * 0.5, z * 0.5) for x in range(-62, -1) for z in range(-62, 63)]
_rng.shuffle(_cands)
_placed = []
for x, z in _cands:
    if len(_placed) >= 34:
        break
    if _ok(x, z, _placed) and _ok(-x, -z, _placed):
        _placed.append((x, z))
PILES = {
    "midway": [["Wooden Crate", "Small Crate", "Cardboard Box"], ["Cardboard Stack", "Cardboard Box Large"], ["Wooden Crate", "Wooden Crate", "Small Crate"]],
    "food": [["Pallet Stack", "Wheelie Bin"], ["Recycling Bin", "Wheelie Bin Grey", "Cardboard Box"], ["Hay Bale", "Pallet", "Plastic Chair"]],
    "mid": [["Hay Bale", "Hay Bale", "Traffic Cone"], ["Small Crate", "Trash Can"], ["Tyre Stack", "Oil Drum", "Cable Spool"]],
}
_offs = [(-0.55, -0.35), (0.6, 0.25), (-0.1, 0.75)]
CLUSTER_PROPS = []
for i, (x, z) in enumerate(_placed):
    kind = "midway" if z < -9 else "food" if z > 9 else "mid"
    pile = PILES[kind][i % 3]
    rot = _rng.uniform(0, math.tau)
    for k, name in enumerate(pile):
        ox, oz = _offs[k]
        c, s_ = math.cos(rot), math.sin(rot)
        op = {"op": "Prop", "name": name, "x": r2(x + ox * c - oz * s_), "z": r2(z + ox * s_ + oz * c), "yaw": r2(_rng.uniform(0, 360))}
        CLUSTER_PROPS.append(op)
for op in CLUSTER_PROPS:
    ops.append(op)
for op in CLUSTER_PROPS:
    ops.append(mirror(op))
ops.append({"op": "AutoWaypoints", "spacing": 6})
print("solid clusters per half:", len(_placed), "props", len(CLUSTER_PROPS))

LITTER = ["Litter", "Litter", "Soda Cans", "Soda Cans", "Newspaper", "Plastic Bag", "Bottle", "Bottles", "Flattened Cardboard",
          "Cardboard Box Small", "Open Cardboard Box", "Trash Bag", "Newspaper Stack", "Plank", "Tyre", "Cinder Blocks"]
SOLID_BREAK = ["Wooden Crate", "Small Crate", "Small Crate", "Cardboard Box", "Cardboard Box", "Cardboard Box Large", "Cardboard Stack",
               "Hay Bale", "Hay Bale", "Trash Can", "Wheelie Bin", "Pallet", "Plastic Chair", "Traffic Cone", "Recycling Bin"]
BACKSTAGE = ["Tyre Stack", "Oil Drum", "Rusty Drum", "Gas Cylinders", "Cable Spool", "Pallet Stack", "Wooden Crate", "Plank Pile"]
clutter = [
    # litter over the whole half (cosmetic)
    {"op": "Clutter", "props": LITTER, "x0": -34, "z0": -34, "x1": 0, "z1": 34, "count": 520, "seed": 11, "mirror": True},
    # popcorn-and-paper drifts in the food court and round the midway
    {"op": "Clutter", "props": ["Litter", "Soda Cans", "Newspaper", "Bottle", "Plastic Bag", "Cardboard Box Small"],
     "x0": -26, "z0": 10, "x1": -4, "z1": 28, "count": 160, "seed": 12, "mirror": True},
    {"op": "Clutter", "props": ["Litter", "Soda Cans", "Bottles", "Newspaper", "Flattened Cardboard"],
     "x0": -26, "z0": -30, "x1": 0, "z1": -12, "count": 140, "seed": 13, "mirror": True},
    # a ring of litter round the carousel kerb
    {"op": "Clutter", "props": ["Litter", "Soda Cans", "Newspaper", "Bottle"], "x0": -12, "z0": -12, "x1": 0, "z1": 12,
     "count": 90, "seed": 14, "mirror": True},
    # breakable boxes, bales, bins and chairs (solid, kept clear of routes by the game)
    {"op": "Clutter", "props": SOLID_BREAK, "x0": -33, "z0": -33, "x1": 0, "z1": 33, "count": 170, "seed": 15, "mirror": True},
    # backstage junk behind the stalls and trucks, along the hedge
    {"op": "Clutter", "props": BACKSTAGE, "x0": -33, "z0": -33, "x1": -4, "z1": -22, "count": 30, "seed": 16, "mirror": True},
    {"op": "Clutter", "props": BACKSTAGE, "x0": -33, "z0": 20, "x1": -4, "z1": 33, "count": 30, "seed": 17, "mirror": True},
    {"op": "Clutter", "props": ["Hay Bale", "Pallet", "Tyre Stack", "Wooden Crate", "Trash Bag"], "x0": -34, "z0": -22, "x1": -27, "z1": 22,
     "count": 26, "seed": 18, "mirror": True},
]
ops += clutter

# ---- objectives ----------------------------------------------------------------------------------------------------
blue = [[x, z] for x, z in SPAWNS_BLUE]
red = [[-x, -z] for x, z in SPAWNS_BLUE]

# ---- foliage -------------------------------------------------------------------------------------------------------
def pts_mirror(pts):
    return [[r2(-x), r2(-z)] for x, z in pts]

north_lane = [[-26, -5], [-21, -14], [-13, -19.5], [-6, -17], [0, -15.5], [6, -17], [13, -19.5], [21, -14], [26, -5]]
mid_lane = [[-25, 0], [-6.5, 0]]
ring = [[r2(7.2 * math.cos(math.radians(a))), r2(7.2 * math.sin(math.radians(a)))] for a in range(0, 361, 30)]
connect = [[-12, -12], [-14, -6], [-12, 0], [-14, 6], [-12, 12]]
paths = [
    {"points": north_lane, "width": 3.0}, {"points": pts_mirror(north_lane), "width": 3.0},
    {"points": mid_lane, "width": 3.0}, {"points": pts_mirror(mid_lane), "width": 3.0},
    {"points": ring, "width": 2.6},
    {"points": connect, "width": 2.2}, {"points": pts_mirror(connect), "width": 2.2},
]
regions = [
    {"type": "grass", "density": 3.2, "circle": [0, 0, 34], "patchiness": 0.45, "color": [0.4, 0.55, 0.26]},
    {"type": "grass", "density": 4.0, "circle": [0, 0, 34], "patchiness": 0.6, "scale": [0.9, 1.6], "color": [0.47, 0.56, 0.28], "seed": 41},
]
# overgrown fringe: bushes and shrubs in pockets round the hedge (symmetric pairs of circles)
fringe = []
for a in range(0, 360, 20):
    if a % 180 in (0, 20, 160):   # keep the spawn ends (east and west) clear
        continue
    rad = math.radians(a)
    fringe.append((r2(30.0 * math.cos(rad)), r2(30.0 * math.sin(rad))))
for i, (x, z) in enumerate(fringe):
    regions.append({"type": "bush", "density": 0.55, "circle": [x, z, 3.2], "scale": [0.9, 1.6], "seed": 100 + i})
    regions.append({"type": "shrub", "density": 1.3, "circle": [x, z, 4.2], "seed": 200 + i})
    regions.append({"type": "grass", "density": 6.0, "circle": [x, z, 4.5], "scale": [1.0, 1.8], "seed": 300 + i,
                    "color": [0.38, 0.5, 0.24]})
# weeds round the stalls, trucks and the stage feet, and under the trees
for (x, z, rr) in [(-17, -22.5, 3), (-10, -25.5, 3), (-20, -17.6, 2.5), (-14.6, 19.5, 3), (-24, 15.4, 2.5), (-8, -14.6, 2.5),
                   (-14.4, -15.6, 3), (-12.4, 8.6, 3)]:
    for sx, sz in ((x, z), (-x, -z)):
        regions.append({"type": "shrub", "density": 1.0, "circle": [r2(sx), r2(sz), rr], "seed": int(500 + sx * 7 + sz * 3)})
        regions.append({"type": "bush", "density": 0.35, "circle": [r2(sx), r2(sz), rr], "seed": int(700 + sx * 5 + sz * 11)})
items = []
for x, z, h in TREES:
    items.append({"type": "tree", "x": x, "z": z, "height": h})
    items.append({"type": "tree", "x": -x, "z": -z, "height": h})

m = {
    "format": "call-of-slop-map/1",
    "name": "Carousel",
    "description": "An abandoned fairground in a round clearing: a carousel still turning in the middle, game stalls, food trucks and two stages, all going to seed.",
    "author": "Call of Slop",
    "order": 401,
    "maxPlayers": 10,
    "sky": "#d8b08c",
    "skybox": "dusk",
    "boundary": {
        "shape": "circle", "center": [0, 0], "radius": R,
        "noise": {"amplitude": 0.8, "scale": 11, "seed": 4},
        "edge": {"style": "hedge", "height": 2.8, "thickness": 1.6, "color": [0.2, 0.36, 0.18], "seed": 5},
    },
    "teamSpawns": [blue, red],
    "hills": [{"x": 0, "z": -13.0}, {"x": 0, "z": 13.0}],
    "flags": [[-31.0, 0], [31.0, 0]],
    "ops": ops,
    "foliage": {"seed": 9, "paths": paths, "regions": regions, "items": items},
    "movers": [carousel],
}


def fmt(o, ind=0):
    # one op per line, like the shipped maps
    return json.dumps(o, separators=(", ", ": "))


with open(OUT, "w") as f:
    f.write("{\n")
    keys = list(m.keys())
    for i, k in enumerate(keys):
        v = m[k]
        end = "," if i < len(keys) - 1 else ""
        if k == "ops":
            f.write('  "ops": [\n' + ",\n".join("    " + fmt(o) for o in v) + "\n  ]" + end + "\n")
        elif k == "foliage":
            f.write('  "foliage": {\n    "seed": %d,\n' % v["seed"])
            f.write('    "paths": [\n' + ",\n".join("      " + fmt(p) for p in v["paths"]) + "\n    ],\n")
            f.write('    "regions": [\n' + ",\n".join("      " + fmt(p) for p in v["regions"]) + "\n    ],\n")
            f.write('    "items": [\n' + ",\n".join("      " + fmt(p) for p in v["items"]) + "\n    ]\n  }" + end + "\n")
        elif k == "movers":
            f.write('  "movers": [\n' + ",\n".join("    " + fmt(p) for p in v) + "\n  ]" + end + "\n")
        else:
            f.write("  " + json.dumps(k) + ": " + fmt(v) + end + "\n")
    f.write("}\n")
print("ops", len(ops), "half", len(HALF), "center", len(CENTER), "regions", len(regions))
