#!/usr/bin/env python3
"""Writes the Shopping Mall building (Buildings/ShoppingMall/building.json and its furnishing rules).

Usage: python3 tools/world/mall_building.py <repo root>

A two-storey American shopping mall round an open atrium (the courtyard in the middle of the plan, x -24..24, z -8..8,
which is not a room: it is open to the sky under a skylight frame). Building coordinates: +Z is the front (south, the
car park), metres, origin in the middle of the atrium.

- West: the department store anchor, two floors joined by its own stair, an arch onto the atrium on both floors.
- North and south rows: four shop units a floor (12, 9, 9, 12 m wide on the north side, 12, 8, 8, 12 on the south),
  each with an arched shop front and display windows onto the atrium (ground floor) or its balcony (upper floor), and a
  back door onto the service corridor behind.
- The north hall (between the north shops) and the entrance hall (between the south shops, the main doors from the car
  park) each hold a stair up to the upper floor.
- East: the food court on the ground floor (an arch 12 m wide onto the atrium), the cinema upstairs (lobby onto the
  east balcony, two auditoriums, exit corridors).
- Back of house along the north side (single storey): stock rooms, restrooms and the loading dock with two roll-up
  openings; its roof is a terrace reached by doors from the upper service corridor.
- Extras: the atrium's tiled walkways, the upper balconies round the void (a ring, with two bridges), their glass-topped
  railings, four escalators (solid steps from the walkway up to the bridges, with cosmetic balustrades), the skylight
  frame, an entrance canopy and a facade band. Everything is building shell (instanced, one draw call or so).

The plan is checked by tools/buildings/buildings.py validate "Shopping Mall".
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Buildings/ShoppingMall")
F0H, F1H = 4.6, 4.2                    # floor-to-floor heights
L1 = 0.1 + F0H                        # upper floor level above the ground
ROOF = L1 + F1H
AX0, AX1, AZ0, AZ1 = -24.0, 24.0, -8.0, 8.0   # atrium courtyard
BAL = 3.0                              # balcony depth
VZ = AZ1 - BAL                         # the void runs z -VZ..VZ
VX = AX1 - BAL
BRIDGES = [(-13.5, -10.5), (10.5, 13.5)]
ESC_RUN, ESC_STEPS = 5.85, 18          # 0.256 m risers: a 0.6 m nav sample never spans more than 0.55 m of rise
FOOD_STALLS = [-10.5, 0.0, 10.5]     # the food court's three stalls along its east wall (z)
ESC_LANES = [(-3.2, -1.5), (1.5, 3.2)]  # z bands of the two escalators on each side (1.7 m wide)
ESC_GAP = 0.2                            # the railing gap at the top is this much wider on each side

# north row (z -20..-8) and south row (z 8..20) shop units, x ranges; (id, type) per floor
NORTH = [(-24, -12), (-12, -3), (3, 12), (12, 24)]
SOUTH = [(-24, -12), (-12, -4), (4, 12), (12, 24)]
NORTH_G = [("n1", "electronics"), ("n2", "books"), ("n3", "shoes"), ("n4", "sports")]
NORTH_U = [("n1u", "arcade"), ("n2u", "beauty"), ("n3u", "vacant"), ("n4u", "pets")]
SOUTH_G = [("s1", "clothing"), ("s2", "jewellery"), ("s3", "candy"), ("s4", "toys")]
SOUTH_U = [("s1u", "homewares"), ("s2u", "phones"), ("s3u", "services"), ("s4u", "clothing")]


def shop_front(x0, x1, zc):
    """An arched shop front with a display window either side, centred on the unit's wall at z = zc."""
    w = x1 - x0
    cx = (x0 + x1) / 2
    arch = 3.6 if w >= 11 else 3.2 if w >= 9 else 3.0
    win = (w - arch - 1.4) / 2 - 0.35
    ops = [{"type": "arch", "at": [cx, zc], "width": arch, "head": 3.4}]
    off = arch / 2 + 0.3 + win / 2
    for s in (-1, 1):
        ops.append({"type": "window", "at": [round(cx + s * off, 3), zc], "width": round(win, 2), "sill": 0.5, "head": 3.2, "health": 20})
    return ops


def back_door(x0, x1, zc, rel=0.0):
    return {"type": "door", "at": [round((x0 + x1) / 2 + rel, 2), zc], "width": 1.0, "material": "metal"}


def room(id_, type_, rect, **kw):
    r = {"id": id_, "type": type_, "rect": [float(v) for v in rect], "windows": False}
    r.update(kw)
    return r


TILE = {"color": "$floor", "material": "tiles"}
CARPET_RED = {"color": "#6a2028", "material": "none"}
CONCRETE = {"color": "#8a8478", "material": "concrete"}
SERVICE_WALL = {"color": "#c8c4bc", "material": "concrete"}

# ---------------------------------------------------------------- ground floor
g_rooms = [room("dept", "deptstore", (-42, -23, -24, 23), floor=TILE)]
g_ops = []
for (x0, x1), (rid, typ) in zip(NORTH, NORTH_G):
    g_rooms.append(room(rid, typ, (x0, -20, x1, -8)))
    g_ops += shop_front(x0, x1, -8.0) + [back_door(x0, x1, -20.0, 1.5)]
for (x0, x1), (rid, typ) in zip(SOUTH, SOUTH_G):
    g_rooms.append(room(rid, typ, (x0, 8, x1, 20)))
    g_ops += shop_front(x0, x1, 8.0) + [back_door(x0, x1, 20.0, -1.5)]
g_rooms += [
    room("north hall", "mallhall", (-3, -20, 3, -8), floor=TILE),
    room("entrance hall", "mallhall", (-4, 8, 4, 26), floor=TILE),
    room("north corridor", "corridor", (-24, -23, 24, -20), floor=CONCRETE, wall=SERVICE_WALL),
    room("south corridor west", "corridor", (-24, 20, -4, 23), floor=CONCRETE, wall=SERVICE_WALL),
    room("south corridor east", "corridor", (4, 20, 24, 23), floor=CONCRETE, wall=SERVICE_WALL),
    room("food court", "foodcourt", (24, -23, 42, 23), floor=TILE, props=[
        p for zc in FOOD_STALLS for p in ({"name": "Food Stall Grill Line", "wall": "E", "along": zc},
                                          {"name": "Food Stall Counter", "at": [5.75, zc], "yaw": 270})]),
    room("stock west", "storage", (-24, -29, -10, -23), floor=CONCRETE, wall=SERVICE_WALL),
    room("restrooms", "restroom", (-10, -29, -4, -23), floor={"color": "#c8d4d8", "material": "tiles"}),
    room("loading dock", "warehouse", (-4, -29, 10, -23), floor=CONCRETE, wall=SERVICE_WALL),
    room("stock east", "storage", (10, -29, 24, -23), floor=CONCRETE, wall=SERVICE_WALL),
]
g_ops += [
    # main entrance from the car park (front), and into the atrium
    {"type": "door", "at": [0, 26], "width": 3.0, "front": True, "material": "glass", "health": 40},
    {"type": "door", "at": [-2.6, 26], "width": 1.4, "material": "glass", "health": 40},
    {"type": "door", "at": [2.6, 26], "width": 1.4, "material": "glass", "health": 40},
    {"type": "arch", "at": [0, 8], "width": 6.0, "head": 3.6},
    {"type": "door", "at": [-4, 21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [4, 21.5], "width": 1.2, "material": "metal"},
    # north hall: arch from the atrium, door to the service corridor
    {"type": "arch", "at": [0, -8], "width": 4.0, "head": 3.6},
    {"type": "door", "at": [-1.5, -20], "width": 1.6, "material": "metal"},
    # service corridors: into the department store and the food court, fire exits
    {"type": "door", "at": [-24, -21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [24, -21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [-24, 21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [24, 21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [-14, 23], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [14, 23], "width": 1.2, "material": "metal"},
    # back of house
    {"type": "door", "at": [-17, -23], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [-7, -23], "width": 1.0},
    {"type": "arch", "at": [3, -23], "width": 2.4},
    {"type": "door", "at": [17, -23], "width": 1.2, "material": "metal"},
    {"type": "arch", "at": [-1.0, -29], "width": 3.6, "head": 3.4},
    {"type": "arch", "at": [6.0, -29], "width": 3.6, "head": 3.4},
    {"type": "door", "at": [-17, -29], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [17, -29], "width": 1.2, "material": "metal"},
    # department store: onto the atrium, the car park and the west side
    {"type": "arch", "at": [-24, 0], "width": 7.0, "head": 3.6},
    {"type": "door", "at": [-33, 23], "width": 3.0, "material": "glass", "health": 40},
    {"type": "window", "at": [-38.8, 23], "width": 4.0, "sill": 0.5, "head": 3.2, "health": 20},
    {"type": "window", "at": [-27.4, 23], "width": 4.0, "sill": 0.5, "head": 3.2, "health": 20},
    {"type": "door", "at": [-42, 0], "width": 2.4, "material": "glass", "health": 40},
    # food court: onto the atrium (wide), the east side and the car park
    {"type": "arch", "at": [24, -3.1], "width": 5.6, "head": 3.6},
    {"type": "arch", "at": [24, 3.1], "width": 5.6, "head": 3.6},
    {"type": "door", "at": [42, -6], "width": 2.0, "material": "glass", "health": 40},
    {"type": "door", "at": [42, 6], "width": 2.0, "material": "glass", "health": 40},
    {"type": "window", "at": [42, -15], "width": 4.0, "sill": 0.6, "head": 3.0, "health": 10},
    {"type": "window", "at": [42, 15], "width": 4.0, "sill": 0.6, "head": 3.0, "health": 10},
    {"type": "door", "at": [33, 23], "width": 2.0, "material": "glass", "health": 40},
]

# ---------------------------------------------------------------- upper floor
u_rooms = [room("dept upper", "deptstore", (-42, -23, -24, 23), floor={"color": "#8a7a68", "material": "none"})]
u_ops = []
for (x0, x1), (rid, typ) in zip(NORTH, NORTH_U):
    u_rooms.append(room(rid, typ, (x0, -20, x1, -8)))
    u_ops += shop_front(x0, x1, -8.0) + [back_door(x0, x1, -20.0, 1.5)]
for (x0, x1), (rid, typ) in zip(SOUTH, SOUTH_U):
    u_rooms.append(room(rid, typ, (x0, 8, x1, 20)))
    u_ops += shop_front(x0, x1, 8.0) + [back_door(x0, x1, 20.0, -1.5)]
u_rooms += [
    room("north landing", "mallhall", (-3, -20, 3, -8), floor=TILE),
    room("south landing", "mallhall", (-4, 8, 4, 23), floor=TILE),
    room("upper north corridor", "corridor", (-24, -23, 24, -20), floor=CONCRETE, wall=SERVICE_WALL),
    room("upper south corridor west", "corridor", (-24, 20, -4, 23), floor=CONCRETE, wall=SERVICE_WALL),
    room("upper south corridor east", "corridor", (4, 20, 24, 23), floor=CONCRETE, wall=SERVICE_WALL),
    room("cinema lobby", "cinemalobby", (24, -14, 32, 14), floor=CARPET_RED),
    room("cinema exit north", "corridor", (24, -23, 32, -14), floor=CARPET_RED),
    room("cinema exit south", "corridor", (24, 14, 32, 23), floor=CARPET_RED),
    room("screen 1", "cinemahall", (32, -23, 42, 0), floor=CARPET_RED, wall={"color": "#3a1a24", "material": "none"}),
    room("screen 2", "cinemahall", (32, 0, 42, 23), floor=CARPET_RED, wall={"color": "#1a2a3a", "material": "none"}),
]
u_ops += [
    {"type": "arch", "at": [0, 8], "width": 6.0, "head": 3.4},
    {"type": "door", "at": [-4, 21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [4, 21.5], "width": 1.2, "material": "metal"},
    {"type": "arch", "at": [0, -8], "width": 4.0, "head": 3.4},
    {"type": "door", "at": [-1.5, -20], "width": 1.6, "material": "metal"},
    {"type": "door", "at": [-24, -21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [24, -21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [-24, 21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [24, 21.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [-14, -23], "width": 1.2, "material": "metal"},   # out onto the back-of-house roof
    {"type": "door", "at": [14, -23], "width": 1.2, "material": "metal"},
    {"type": "arch", "at": [-24, 0], "width": 7.0, "head": 3.4},
    {"type": "arch", "at": [24, 0], "width": 7.0, "head": 3.4},
    {"type": "door", "at": [28, -14], "width": 1.6},
    {"type": "door", "at": [28, 14], "width": 1.6},
    {"type": "door", "at": [32, -7], "width": 1.6},
    {"type": "door", "at": [32, 7], "width": 1.6},
    {"type": "door", "at": [32, -18.5], "width": 1.2, "material": "metal"},
    {"type": "door", "at": [32, 18.5], "width": 1.2, "material": "metal"},
    {"type": "window", "at": [-33, 23], "width": 6.0, "sill": 1.0, "head": 3.0, "health": 20},
]

# ---------------------------------------------------------------- stairs (13 steps of 0.354 m, 0.42 m deep)
RUN = 13 * 0.42
stairs = [
    {"floor": 0, "rect": [-41.2, -RUN / 2, -39.6, RUN / 2], "dir": "+z"},           # department store, along its west wall
    {"floor": 0, "rect": [0.6, -10.74 - RUN, 2.4, -10.74], "dir": "-z"},           # north hall
    {"floor": 0, "rect": [1.6, 11.0, 3.4, 11.0 + RUN], "dir": "-z"},               # entrance hall
]

# ---------------------------------------------------------------- extras
X = []


def block(x0, y0, z0, x1, y1, z1, color):
    X.append({"type": "block", "min": [round(x0, 3), round(y0, 3), round(z0, 3)], "max": [round(x1, 3), round(y1, 3), round(z1, 3)], "color": color})


# atrium walkway floors (tiles, flush with the building floors); the middle between the bridges is left to the site
for (x0, z0, x1, z1) in ((AX0, AZ0, AX1, -VZ), (AX0, VZ, AX1, AZ1), (AX0, -VZ, -12.0, VZ), (12.0, -VZ, AX1, VZ)):
    block(x0, 0.0, z0, x1, 0.1, z1, {"color": "$floor", "material": "tiles"})
# balconies round the void and the two bridges (no automatic railings: they'd close the joins)
for r in ((AX0, AZ0, AX1, -VZ), (AX0, VZ, AX1, AZ1), (AX0, -VZ, -VX, VZ), (VX, -VZ, AX1, VZ)) + tuple((b0, -VZ, b1, VZ) for b0, b1 in BRIDGES):
    X.append({"type": "balcony", "rect": [float(v) for v in r], "floor": 1, "railing": 0, "color": {"color": "$floor", "material": "tiles"}})
# railings along the void: 1.05 m, glass-panelled steel (solid cover)
RAIL = "#8aa6b4"
RT = 0.08


def rail_x(x0, x1, z):
    block(x0, L1, z - RT / 2, x1, L1 + 1.05, z + RT / 2, RAIL)


def rail_z(z0, z1, x):
    block(x - RT / 2, L1, z0, x + RT / 2, L1 + 1.05, z1, RAIL)


spans = [(-VX, BRIDGES[0][0]), (BRIDGES[0][1], BRIDGES[1][0]), (BRIDGES[1][1], VX)]
for a, b in spans:
    rail_x(a, b, -VZ + RT / 2)
    rail_x(a, b, VZ - RT / 2)
rail_z(-VZ, VZ, -VX + RT / 2)
rail_z(-VZ, VZ, VX - RT / 2)
for (b0, b1) in BRIDGES:
    inner, outer = (b1, b0) if b0 < 0 else (b0, b1)
    rail_z(-VZ, VZ, inner + (RT / 2 if inner == b1 else -RT / 2))
    # the outer side has gaps where the escalators arrive
    xo = outer - RT / 2 if outer == b1 else outer + RT / 2
    xo = outer + RT / 2 if outer < 0 else outer - RT / 2
    edges = [-VZ] + [v + (ESC_GAP if k % 2 else -ESC_GAP) for lane in ESC_LANES for k, v in enumerate(lane)] + [VZ]
    for k in range(0, len(edges), 2):
        rail_z(edges[k], edges[k + 1], xo)
# escalators: from the walkway up to the bridges' outer sides
ESC = []
for (b0, b1) in BRIDGES:
    top_x = b1 if b0 > 0 else b0          # the bridge's outer edge, where the escalators arrive
    d = 1 if top_x > 0 else -1             # escalators run away from the middle: foot further out
    foot_x = top_x + d * ESC_RUN
    for (z0, z1) in ESC_LANES:
        for k in range(1, ESC_STEPS + 1):
            a = foot_x - d * (k - 1) * ESC_RUN / ESC_STEPS
            b = foot_x - d * k * ESC_RUN / ESC_STEPS
            block(min(a, b), 0.0, z0, max(a, b), 0.1 + k * F0H / ESC_STEPS, z1, {"color": "#8a8a94", "material": "metal"})
        cx = (top_x + foot_x) / 2
        # the balustrade climbs towards its -Z: yaw 270 turns that to +X, yaw 90 to -X
        X.append({"type": "prop", "name": "Escalator Balustrade", "at": [round(cx, 3), 0.1, round((z0 + z1) / 2, 3)], "yaw": 270 if d < 0 else 90})
        ESC.append((foot_x, top_x, (z0 + z1) / 2))
# skylight frame over the atrium: cross beams every 4 m and two long beams
for k in range(13):
    x = AX0 + k * 4.0
    block(x - 0.15, ROOF + 0.3, AZ0 - 0.15, x + 0.15, ROOF + 0.7, AZ1 + 0.15, "#d8dce0")
for z in (-4.0, 0.0, 4.0):
    block(AX0, ROOF + 0.7, z - 0.12, AX1, ROOF + 1.0, z + 0.12, "#d8dce0")
for z in (AZ0, AZ1):
    block(AX0 - 0.15, ROOF, z - 0.15, AX1 + 0.15, ROOF + 0.3, z + 0.15, "#c8ccd0")
# entrance canopy, the facade band, the loading dock's bumper rail
X.append({"type": "awning", "rect": [-6.0, 26.0, 6.0, 29.0], "height": 3.8, "posts": True, "color": "$trim"})
for (x0, x1, z) in ((-42.0, -4.0, 23.0), (4.0, 42.0, 23.0), (-4.0, 4.0, 26.0)):
    block(x0 - 0.15, 7.0, z + 0.12, x1 + 0.15, 7.7, z + 0.3, "$band")
block(-4.0, 0.0, -29.5, 10.0, 0.3, -29.15, "#2a2a2e")
X += [
    {"type": "prop", "name": "Rooftop HVAC Unit", "at": [-30.0, 0, -12.0], "onRoof": True},
    {"type": "prop", "name": "Rooftop HVAC Unit", "at": [-34.0, 0, 10.0], "onRoof": True, "yaw": 90},
    {"type": "prop", "name": "Rooftop HVAC Unit", "at": [34.0, 0, -10.0], "onRoof": True},
    {"type": "prop", "name": "Exhaust Fan", "at": [30.0, 0, 18.0], "onRoof": True},
    {"type": "prop", "name": "Rooftop Condenser", "at": [-16.0, 0, -15.0], "onRoof": True},
    {"type": "prop", "name": "Rooftop Condenser", "at": [16.0, 0, 15.0], "onRoof": True},
]

building = {
    "format": "call-of-slop-building/1",
    "name": "Shopping Mall",
    "theme": "A 1990s American two-storey mall: beige precast panels, a skylit atrium with balconies, glass railings and escalators, shops of every kind, a food court, a cinema, service corridors",
    "category": "commercial",
    "description": "A big two-storey mall round an open skylit atrium: a department store, sixteen shop units, a food court, a two-screen cinema, service corridors, stock rooms and a loading dock, with escalators and bridges up to the balconies.",
    "author": "Call of Slop",
    "seed": 502,
    "wallThickness": 0.3,
    "storeyHeight": F0H,
    "doorHeight": 2.3,
    "exterior": {"color": "$wall", "material": "concrete"},
    "interior": {"color": "$interior", "material": "none"},
    "floor": {"color": "$floor", "material": "none"},
    "ceiling": "$ceiling",
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "sandstone": {"wall": "#c9bca4", "interior": "#ece8e0", "floor": "#d8d2c4", "ceiling": "#f4f2ee", "trim": "#7a8088", "door": "#4a4e56", "roof": "#5a5c60", "band": "#2a5a7a"},
        "grey precast": {"wall": "#a8aaac", "interior": "#eaeaea", "floor": "#cfcfcc", "ceiling": "#f4f4f4", "trim": "#3a4048", "door": "#3a3e44", "roof": "#505256", "band": "#a03a2a"},
    },
    "floors": [
        {"height": F0H, "rooms": g_rooms, "openings": g_ops},
        {"height": F1H, "rooms": u_rooms, "openings": u_ops},
    ],
    "stairs": stairs,
    "roof": {"type": "flat", "parapet": 1.0, "color": {"color": "$roof", "material": "concrete"}},
    "extras": X,
    "anchors": [
        {"type": "hill", "name": "atrium", "floor": 0, "at": [0, 0]},
        {"type": "hill", "name": "food court", "floor": 0, "at": [33, 0]},
        {"type": "hill", "name": "department store", "floor": 1, "at": [-33, 0]},
        {"type": "point", "name": "main entrance", "floor": 0, "at": [0, 27.5]},
        {"type": "point", "name": "loading dock", "floor": 0, "at": [3, -31]},
    ],
}

# ---------------------------------------------------------------- furnishing rules (this building's own room types)
LIGHT = {"props": ["Mall Ceiling Light"], "place": "ceiling", "count": [2, 4], "y": 0.0}
TILL = {"id": "till", "props": ["Shop Checkout Counter"], "place": "wall", "prefer": "near"}
GATE_LITTER = {"props": ["Carrier Bag", "Shoe Box Stack", "Cardboard Box Small"], "place": "floor", "count": [0, 2]}
RULES = {
    "clothing": ("Clothes shop: wall rails, round racks, folding tables, mannequins and changing rooms.", [
        TILL, {"props": ["Changing Room Booth"], "place": "corner", "count": [1, 2], "prefer": "far"},
        {"props": ["Clothes Wall Rail"], "place": "wall", "count": [3, 5]},
        {"props": ["Folded Clothes Table"], "place": "centre"},
        {"props": ["Clothes Rack Round", "Clothes Rack Round", "Clothes Rail Straight"], "place": "floor", "count": [3, 5]},
        {"props": ["Mannequin Casual", "Mannequin Dress", "Mannequin Formal", "Mannequin Sporty"], "place": "floor", "count": [2, 3], "prefer": "near"},
        {"props": ["Sale Sign Stand"], "place": "floor", "chance": 0.6}, GATE_LITTER, LIGHT]),
    "shoes": ("Shoe shop: shoe walls, fitting benches and box stacks.", [
        TILL, {"props": ["Shoe Wall Display"], "place": "wall", "count": [3, 5]},
        {"props": ["Shoe Fitting Bench"], "place": "centre"}, {"props": ["Shoe Fitting Bench", "Display Plinth"], "place": "floor", "count": [1, 2]},
        {"props": ["Shoe Box Stack"], "place": "floor", "count": [2, 4]}, LIGHT]),
    "sports": ("Sports shop: jersey walls, ball cages, treadmills and weights.", [
        TILL, {"props": ["Sports Jersey Wall"], "place": "wall", "count": [2, 4]},
        {"props": ["Treadmill"], "place": "wall", "count": [1, 2], "prefer": "far"},
        {"props": ["Dumbbell Rack"], "place": "wall", "count": [1, 2]},
        {"props": ["Sports Ball Cage"], "place": "floor", "count": [1, 2]},
        {"props": ["Clothes Rail Straight", "Mannequin Sporty"], "place": "floor", "count": [1, 3]},
        {"props": ["Bicycle"], "place": "floor", "chance": 0.6}, LIGHT]),
    "electronics": ("Electronics shop: TV walls, laptop tables, phone islands and speakers.", [
        TILL, {"props": ["TV Wall Display"], "place": "wall", "count": [2, 3], "prefer": "far"},
        {"props": ["Retail Wall Shelving"], "place": "wall", "count": [1, 2]},
        {"props": ["Laptop Display Table"], "place": "centre"},
        {"props": ["Laptop Display Table", "Phone Display Island"], "place": "floor", "count": [1, 3]},
        {"props": ["Speaker Display Stack"], "place": "corner", "count": [1, 2]}, LIGHT]),
    "phones": ("Phone shop: phone islands, a tablet table, accessories walls.", [
        TILL, {"props": ["Retail Wall Shelving"], "place": "wall", "count": [2, 3]},
        {"props": ["Phone Display Island"], "place": "centre"}, {"props": ["Phone Display Island", "Laptop Display Table"], "place": "floor", "count": [1, 2]},
        {"props": ["Office Plant"], "place": "corner"}, LIGHT]),
    "books": ("Bookshop: tall cases round the walls, new-release tables, a spinner.", [
        TILL, {"props": ["Retail Bookcase"], "place": "wall", "count": [4, 7]},
        {"props": ["Book Display Table"], "place": "centre"}, {"props": ["Book Display Table"], "place": "floor", "count": [1, 2]},
        {"props": ["Book Spinner Rack"], "place": "floor", "count": [1, 2]}, {"props": ["Armchair"], "place": "corner", "chance": 0.6}, LIGHT]),
    "jewellery": ("Jeweller: glass counters and lit tower cases.", [
        {"props": ["Jewellery Display Case"], "place": "wall", "count": [2, 3]},
        {"props": ["Jewellery Tower Case"], "place": "wall", "count": [2, 3]},
        {"props": ["Jewellery Display Case"], "place": "centre"}, {"props": ["Office Chair", "Bar Stool"], "place": "floor", "count": [1, 2]},
        {"props": ["Safe"], "place": "corner", "prefer": "far"}, LIGHT]),
    "candy": ("Sweet shop: pick-and-mix walls and candy tables.", [
        TILL, {"props": ["Candy Bin Wall"], "place": "wall", "count": [2, 4]},
        {"props": ["Candy Display Table"], "place": "centre"}, {"props": ["Candy Display Table"], "place": "floor", "count": [1, 2]},
        {"props": ["Glowing Vending Machine"], "place": "wall", "chance": 0.5}, LIGHT]),
    "toys": ("Toy shop: toy shelving, plush bins, a ride-on car and a claw machine.", [
        TILL, {"props": ["Toy Shelf Unit"], "place": "wall", "count": [3, 5]},
        {"props": ["Plush Toy Bin"], "place": "centre"}, {"props": ["Plush Toy Bin", "Retail Gondola"], "place": "floor", "count": [1, 3]},
        {"props": ["Claw Machine"], "place": "corner", "chance": 0.7}, {"props": ["Ride-On Toy Car", "Teddy Bear", "Toy Blocks", "Sports Balls"], "place": "floor", "count": [2, 4]}, LIGHT]),
    "arcade": ("Arcade: rows of cabinets, a claw machine, air hockey and a prize counter.", [
        {"id": "prize", "props": ["Prize Counter"], "place": "wall", "prefer": "far"},
        {"props": ["Arcade Cabinet"], "place": "wall", "count": [5, 8]},
        {"props": ["Air Hockey Table"], "place": "centre"},
        {"props": ["Claw Machine"], "place": "corner", "count": [1, 2]},
        {"props": ["Arcade Cabinet"], "place": "floor", "count": [1, 2]}, LIGHT]),
    "beauty": ("Beauty shop: cosmetics walls, make-up counters and perfume tables.", [
        TILL, {"props": ["Cosmetics Wall Unit"], "place": "wall", "count": [3, 5]},
        {"props": ["Makeup Counter"], "place": "centre"}, {"props": ["Perfume Display Table", "Makeup Counter"], "place": "floor", "count": [1, 2]},
        {"props": ["Bar Stool"], "place": "floor", "count": [0, 2]}, LIGHT]),
    "pets": ("Pet shop: aquariums, food shelves, small-animal cages and a bird cage.", [
        TILL, {"props": ["Aquarium Rack"], "place": "wall", "count": [2, 3], "prefer": "far"},
        {"props": ["Pet Food Shelf"], "place": "wall", "count": [2, 3]},
        {"props": ["Small Animal Cages"], "place": "wall", "count": [1, 2]},
        {"props": ["Retail Gondola"], "place": "centre"}, {"props": ["Bird Cage Stand"], "place": "floor", "count": [1, 2]},
        {"props": ["Dog Kennel", "Cat", "Dog"], "place": "floor", "count": [1, 2]}, LIGHT]),
    "homewares": ("Home store: room sets of sofas, beds and lamps, shelving of homewares.", [
        TILL, {"props": ["Fabric Sofa"], "place": "wall", "count": [1, 2]}, {"props": ["Double Bed"], "place": "wall", "prefer": "far"},
        {"props": ["Retail Wall Shelving", "Bookshelf"], "place": "wall", "count": [1, 3]},
        {"props": ["Rug", "Striped Rug"], "place": "centre"}, {"props": ["Coffee Table", "Armchair", "Floor Lamp", "Dining Table"], "place": "floor", "count": [2, 4]},
        {"props": ["Office Plant", "Table Lamp"], "place": "corner", "count": [1, 2]}, LIGHT]),
    "deptstore": ("Department store floor: clothes, cosmetics and homewares departments with tills.", [
        {"props": ["Shop Checkout Counter"], "place": "wall", "count": [2, 3]},
        {"props": ["Cosmetics Wall Unit", "Clothes Wall Rail", "Shoe Wall Display", "Retail Wall Shelving"], "place": "wall", "count": [8, 12]},
        {"props": ["Makeup Counter"], "place": "centre"},
        {"props": ["Clothes Rack Round", "Clothes Rail Straight", "Folded Clothes Table", "Perfume Display Table", "Retail Gondola"], "place": "floor", "count": [10, 16]},
        {"props": ["Mannequin Casual", "Mannequin Dress", "Mannequin Formal", "Mannequin Sporty"], "place": "floor", "count": [4, 7]},
        {"props": ["Changing Room Booth"], "place": "corner", "count": [1, 2]},
        {"props": ["Sale Sign Stand", "Display Plinth"], "place": "floor", "count": [2, 4]},
        {"props": ["Mall Ceiling Light"], "place": "ceiling", "count": [8, 12], "y": 0.0}]),
    "foodcourt": ("Food court: tables and chairs, tray returns and bins.", [
        {"props": ["Tray Return Station"], "place": "wall", "count": [1, 2]},
        {"props": ["Mall Trash Bin"], "place": "wall", "count": [2, 3]},
        {"props": ["Food Court Table Set", "Food Court Table Set Teal"], "place": "floor", "count": [10, 16]},
        {"props": ["Mall Planter Palm"], "place": "corner", "count": [1, 2]},
        {"props": ["Mall Pendant Light"], "place": "ceiling", "count": [6, 10], "y": 0.0}]),
    "cinemalobby": ("Cinema lobby: the box office, popcorn, a queue rope and poster stands.", [
        {"props": ["Box Office Counter"], "place": "wall", "prefer": "far"},
        {"props": ["Popcorn Machine"], "place": "wall", "count": [1, 2]},
        {"props": ["Glowing Vending Machine", "Arcade Cabinet"], "place": "wall", "count": [1, 2]},
        {"props": ["Queue Rope Barrier"], "place": "floor", "count": [1, 3]},
        {"props": ["Mall Bench"], "place": "wall", "count": [1, 2]},
        {"props": ["Mall Pendant Light"], "place": "ceiling", "count": [3, 5], "y": 0.0}]),
    "cinemahall": ("Cinema auditorium: the big screen and rows of plush seats.", [
        {"props": ["Cinema Big Screen"], "place": "wall", "prefer": "far"},
        {"props": ["Auditorium Seat Row"], "place": "floor", "count": [6, 9]}]),
    "services": ("Customer service desk: a counter, seating and leaflets.", [
        {"props": ["Reception Desk"], "place": "wall", "prefer": "far"}, {"props": ["Mall Bench"], "place": "wall", "count": [1, 2]},
        {"props": ["Filing Cabinet", "Water Cooler", "Office Plant"], "place": "wall", "count": [2, 3]},
        {"props": ["Office Chair"], "place": "floor"}, LIGHT]),
    "vacant": ("An empty unit being fitted out: ladders, paint, sheeting and boxes.", [
        {"props": ["Step Ladder"], "place": "floor", "count": [1, 2]}, {"props": ["Paint Cans", "Toolbox", "Cardboard Box", "Plank"], "place": "floor", "count": [3, 6]},
        {"props": ["Metal Shelving"], "place": "wall", "chance": 0.5}, {"props": ["Scaffold Tower"], "place": "floor", "chance": 0.6}]),
}


def main():
    os.makedirs(os.path.join(OUT, "Furnishing"), exist_ok=True)
    with open(os.path.join(OUT, "building.json"), "w") as f:
        json.dump(building, f, indent=1)
        f.write("\n")
    for t, (desc, items) in RULES.items():
        with open(os.path.join(OUT, "Furnishing", t + ".json"), "w") as f:
            f.write(json.dumps({"format": "call-of-slop-furnishing/1", "type": t, "description": desc, "items": items}, indent=1) + "\n")
    print("wrote", OUT, len(RULES), "rules")


if __name__ == "__main__":
    main()


def showcase():
    """Maps/Dev/BuildingsMall.json: the Shopping Mall on its own, flat, for the Dev pack (the self-test wants every building
    on the Building Yard or a Dev Buildings* showcase map, and walks bots through every room of it)."""
    sp = [[x, 34.0] for x in (-36, -24, -12, 12, 24, 36)] + [[x, -36.0] for x in (-30, -16, 16, 30)] + [[-50.0, 0.0], [50.0, 0.0], [-50.0, 20.0], [50.0, -20.0], [-50.0, -20.0], [50.0, 20.0]]
    mp = {
        "format": "call-of-slop-map/1",
        "name": "Mall Showcase",
        "description": "The Shopping Mall building on its own: walk the atrium, ride the escalators, try every shop, the food court and the cinema.",
        "author": "Call of Slop",
        "order": 959,
        "halfX": 58,
        "halfZ": 44,
        "sky": [0.58, 0.68, 0.8],
        "ops": [{"op": "Ground", "color": [0.3, 0.3, 0.31]}, {"op": "Perimeter", "height": 4, "color": [0.55, 0.5, 0.45]},
                {"op": "Building", "name": "Shopping Mall", "at": [0, 0]}]
               + [{"op": "Spawn", "x": x, "z": z} for x, z in sp] + [{"op": "AutoWaypoints", "spacing": 6}],
    }
    path = os.path.join(REPO, "Maps/Dev/BuildingsMall.json")
    with open(path, "w") as f:
        f.write("{\n" + ",\n".join(f'  "{k}": ' + (("[\n" + ",\n".join("    " + json.dumps(o) for o in v) + "\n  ]") if k == "ops" else json.dumps(v)) for k, v in mp.items()) + "\n}\n")
    print("wrote", path)


if __name__ == "__main__":
    showcase()
