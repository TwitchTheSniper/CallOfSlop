#!/usr/bin/env python3
"""Writes the countryside props (Props/Countryside/*.json): Normandy bocage hedgerows, field gates, farm carts and a
tractor, hay, an orchard's apple trees, a calvary cross, a well, cows and sheep, a grain silo, drystone walls and the rest
of a French farm. They're general props (any map can use them); the World Tour pack's Bocage map
(tools/world/bocage.py) is the first to.

Usage: python3 tools/world/bocage_props.py <repo root>

Every name here is unique across Props/, the pack Props folders and the buildings' own props (checked when run).
"""
import glob, json, math, os, random, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Props/Countryside")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def octagon(d, h, y, color, x=0.0, z=0.0, **kw):
    """Four boxes turned 0/45/90/135 degrees about Y: an upright octagon d across, h tall, centred at height y."""
    return [part((d, h, d * 0.414), (x, y, z), color, rotation=[0, a, 0], **kw) for a in (0, 45, 90, 135)]


def drum_x(length, d, y, color, x=0.0, z=0.0, **kw):
    """An octagonal drum lying along X (wheels, logs, round bales)."""
    return [part((length, d, d * 0.414), (x, y, z), color, rotation=[a, 0, 0], **kw) for a in (0, 45, 90, 135)]


def drum_z(length, d, y, color, x=0.0, z=0.0, **kw):
    """An octagonal drum lying along Z (wheels on an axle along Z)."""
    return [part((d * 0.414, d, length), (x, y, z), color, rotation=[0, 0, a], **kw) for a in (0, 45, 90, 135)]


PROPS = []


def add(name, desc, parts, collision="solid", collider=None, scatter=None, breakable=None, fade=None):
    d = {"format": "call-of-slop-prop/1", "name": name, "category": "Countryside", "description": desc, "collision": collision}
    if collision == "solid" and collider is not None:
        d["collider"] = collider
    if scatter is not None:
        d["scatter"] = scatter
    if fade is not None:
        d["fadeDistance"] = fade
    if breakable is not None:
        d["breakable"] = breakable
    d["parts"] = parts
    PROPS.append(d)


# ======================================================================================== hedgerows
# A Normandy bocage hedge: an earth bank about a metre high, a dense hedge of hawthorn, hazel and holly on top, the whole
# thing four metres and more: you can't see, shoot or climb through it. The collider is one box (cheap for the physics
# world and the bots' nav graph); the model is lumpy boxes in several greens. Pieces overlap when lined up (6 m pieces
# every 5.6 m), so a run of them reads as one hedge.
BANK = "#6a5534"
BANK2 = "#5e4c30"
TURF = "#56692f"
GREENS = ["#2f4a24", "#35522a", "#3b5a2c", "#2a4321", "#41612f", "#324d26", "#466833"]
HEDGE_H = 4.3       # collider height: above a parkour mantle's reach (3.8 m) on level ground
HEDGE_D = 2.3       # collider depth (the bank's foot)


def hedge_parts(length, seed, ends=False):
    rng = random.Random(seed)
    ps = [part((length + 0.1, 0.9, 2.4), (0, 0.45, 0), BANK, material="dirt", texScale=2),
          part((length + 0.05, 0.35, 2.1), (0, 0.98, 0), BANK2, material="dirt", texScale=2),
          part((length, 0.08, 2.18), (0, 1.12, 0), TURF, material="dirt"),
          part((length - 0.1, 2.6, 1.7), (0, 2.35, 0), GREENS[0], material="dirt", texScale=1.5)]
    # lumps of foliage along it: overlapping boxes, a little turned, sticking out of the sides and the top
    n = max(2, int(round(length / 1.15)))
    for k in range(n):
        x = -length / 2 + (k + 0.5) * length / n + rng.uniform(-0.25, 0.25)
        w = rng.uniform(1.2, 1.9); h = rng.uniform(1.4, 2.2); d = rng.uniform(1.65, 2.05)
        top = rng.uniform(3.75, 4.4)
        ps.append(part((min(w, length * 0.9), h, d), (x, top - h / 2, rng.uniform(-0.12, 0.12)), rng.choice(GREENS[1:]),
                       rotation=[rng.uniform(-6, 6), rng.uniform(-14, 14), rng.uniform(-6, 6)], material="dirt", texScale=1.5))
    for k in range(max(1, n // 2)):     # low skirts of growth spilling over the bank
        x = -length / 2 + (k + 0.5) * length / max(1, n // 2) + rng.uniform(-0.4, 0.4)
        side = rng.choice((-1, 1))
        ps.append(part((rng.uniform(0.9, 1.6), rng.uniform(0.5, 0.8), 0.5), (x, 1.35, side * rng.uniform(0.85, 1.0)), rng.choice(GREENS),
                       rotation=[side * rng.uniform(8, 18), rng.uniform(-10, 10), 0], material="dirt"))
    for k in range(max(1, n // 3)):     # a few paler sprigs (hawthorn blossom, hazel) on top
        x = -length / 2 + rng.uniform(0.4, length - 0.4)
        ps.append(part((rng.uniform(0.5, 0.9), 0.35, rng.uniform(0.5, 0.9)), (x, rng.uniform(4.05, 4.45), rng.uniform(-0.5, 0.5)),
                       rng.choice(["#5f7d3a", "#6b8a40", "#8a9a5a"]), rotation=[0, rng.uniform(0, 90), 0], material="dirt"))
    if ends:   # rounded ends, for the side of a gap or a gateway
        for sx in (-1, 1):
            ps.append(part((0.9, 3.0, 1.5), (sx * (length / 2 - 0.2), 2.4, 0), rng.choice(GREENS[1:]), rotation=[0, 45, 0], material="dirt"))
            ps.append(part((0.6, 0.9, 1.9), (sx * (length / 2 - 0.05), 0.45, 0), BANK, material="dirt"))
    return ps


HEDGE_COLLIDER = lambda L: [{"size": [L, HEDGE_H, HEDGE_D], "offset": [0, HEDGE_H / 2, 0]}]
add("Bocage Hedgerow", "A 6 m run of Normandy hedgerow: an earth bank under a dense, head-high-and-more hedge. Stops bullets and can't be climbed.",
    hedge_parts(6.0, 11), collider=HEDGE_COLLIDER(6.0), scatter={"randomYaw": False, "yawStep": 90, "scale": 1})
add("Bocage Hedgerow B", "Another 6 m run of bocage hedgerow (different growth), so long hedges don't repeat.",
    hedge_parts(6.0, 23), collider=HEDGE_COLLIDER(6.0), scatter={"randomYaw": False, "yawStep": 90, "scale": 1})
add("Bocage Hedgerow Short", "A 3 m run of bocage hedgerow, for the odd lengths at corners.",
    hedge_parts(3.0, 37), collider=HEDGE_COLLIDER(3.0), scatter={"randomYaw": False, "yawStep": 90, "scale": 1})
add("Bocage Hedgerow End", "A 2 m bocage hedgerow end, rounded off where the hedge stops at a gateway or a gap.",
    hedge_parts(2.0, 41, ends=True), collider=HEDGE_COLLIDER(2.0), scatter={"randomYaw": False, "yawStep": 90, "scale": 1})

# ======================================================================================== field furniture
GRANITE = "#8a877e"
GRANITE2 = "#76746c"
WEATHERED = "#8e8064"
OAK = "#6e5536"
OAKD = "#4f3c26"
IRON = "#2e2e2c"

gate = []
for sx in (-1, 1):   # granite gateposts
    gate += [part((0.38, 1.5, 0.38), (sx * 1.95, 0.75, 0), GRANITE, material="concrete"), part((0.44, 0.08, 0.44), (sx * 1.95, 1.54, 0), GRANITE2, material="concrete")]
# the gate, a five-bar timber gate hung on the west post, swung open 95 degrees into the field (towards +z)
gx, gz = -1.78, 0.0
L = 3.3
for k, y in enumerate((0.25, 0.45, 0.65, 0.85, 1.05)):
    gate.append(part((0.06, 0.09, L), (gx, y, gz + L / 2 + 0.05), WEATHERED, material="wood"))
gate += [part((0.08, 1.05, 0.1), (gx, 0.65, gz + 0.12), WEATHERED, material="wood"),
         part((0.08, 1.05, 0.1), (gx, 0.65, gz + L), WEATHERED, material="wood"),
         part((0.06, 0.09, 1.25), (gx, 0.66, gz + 1.0), WEATHERED, material="wood", rotation=[-42, 0, 0]),
         part((0.06, 0.06, 0.16), (gx + 0.02, 1.0, gz + 0.08), IRON), part((0.06, 0.06, 0.16), (gx + 0.02, 0.35, gz + 0.08), IRON),
         part((0.3, 0.02, 0.4), (0.4, 0.01, 0.1), "#5a4a32", material="dirt")]   # a muddy rut between the posts
add("Field Gate Open", "Granite gateposts with a five-bar timber field gate swung open; 3.5 m clear between the posts.",
    gate, collider=[{"size": [0.38, 1.5, 0.38], "offset": [-1.95, 0.75, 0]}, {"size": [0.38, 1.5, 0.38], "offset": [1.95, 0.75, 0]}],
    scatter={"randomYaw": False, "yawStep": 90})

rail = [part((0.12, 1.25, 0.12), (x, 0.62, 0), WEATHERED, material="wood") for x in (-1.45, 0, 1.45)]
rail += [part((3.0, 0.1, 0.06), (0, y, 0.07), WEATHERED, material="wood") for y in (0.45, 0.85, 1.15)]
add("Post and Rail Fence", "A 3 m post-and-rail paddock fence, weathered grey timber. Looks only: you walk through it.",
    rail, collision="cosmetic", scatter={"randomYaw": False, "yawStep": 90}, fade=70)

wall = []
rng = random.Random(5)
wall.append(part((3.0, 0.8, 0.62), (0, 0.4, 0), "#7d776a", material="concrete", texScale=2))
for row, y in enumerate((0.18, 0.45, 0.72)):
    x = -1.5 + (0.2 if row % 2 else 0)
    while x < 1.4:
        w = rng.uniform(0.35, 0.6)
        wall.append(part((min(w, 1.5 - x), 0.24, 0.66), (x + min(w, 1.5 - x) / 2, y, rng.uniform(-0.02, 0.02)), rng.choice(["#857e70", "#746e62", "#8f897a", "#6c665a"]), material="concrete"))
        x += w + 0.02
for k in range(6):
    wall.append(part((0.48, 0.22, 0.55), (-1.25 + k * 0.5, 0.92, 0), rng.choice(["#6e6a5e", "#7a7466"]), rotation=[0, 0, rng.uniform(-25, 25)], material="concrete"))
wall.append(part((2.4, 0.05, 0.4), (0.1, 1.04, 0), "#5c6e34", material="dirt"))   # moss on the coping
add("Drystone Wall", "A 3 m drystone farmyard wall, about a metre high with a coping of stones on edge: cover you can mantle.",
    wall, collider=[{"size": [3.0, 1.05, 0.62], "offset": [0, 0.525, 0]}], scatter={"randomYaw": False, "yawStep": 90})

# ======================================================================================== hay, carts, tractor
HAY = "#c9ad5f"
HAY2 = "#b89c52"
HAY3 = "#d6bd6e"
bale = drum_x(1.25, 1.5, 0.75, HAY, material="dirt", texScale=2) + drum_x(1.27, 1.38, 0.75, HAY2, material="dirt", texScale=2)
bale += [part((1.29, 0.04, 0.04), (0, 0.75 + r * 0.66, 0), "#8a7a4a") for r in (1, -1)]
bale += [part((0.02, 0.9, 0.9), (sx * 0.64, 0.75, 0), HAY3, rotation=[22.5, 0, 0], material="dirt") for sx in (1, -1)]
add("Round Hay Bale", "A big round bale of hay lying on its side: solid cover a metre and a half tall.",
    bale, collider=[{"size": [1.25, 1.45, 1.45], "offset": [0, 0.725, 0]}], scatter={"randomYaw": True, "scale": [0.95, 1.05]})

stack = []
for k, (x, y, z, yaw) in enumerate([(-0.58, 0.25, -0.29, 0), (0.58, 0.25, -0.29, 0), (-0.58, 0.25, 0.29, 0), (0.58, 0.25, 0.29, 0),
                                     (0, 0.75, -0.29, 0), (0, 0.75, 0.29, 0), (-0.3, 1.25, 0.0, 90)]):
    stack.append(part((1.1, 0.5, 0.56), (x, y, z), [HAY, HAY2, HAY3][k % 3], material="dirt", texScale=2, rotation=[0, yaw, 0]))
    stack.append(part((0.02, 0.51, 0.58) if yaw == 0 else (0.58, 0.51, 0.02), (x - 0.3 if yaw == 0 else x, y, z if yaw == 0 else z - 0.3), "#8a6a3a", rotation=[0, 0, 0]))
add("Hay Bale Stack", "Square hay bales stacked three high against a barn wall.",
    stack, collider=[{"size": [2.3, 1.0, 1.15], "offset": [0, 0.5, 0]}, {"size": [1.15, 0.5, 1.15], "offset": [0, 1.25, 0]}],
    scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 6},
    breakable={"health": 140, "breakBy": ["explosions", "streaks"], "sound": "soft", "debris": {"count": 24, "size": [0.06, 0.25], "speed": 3, "colors": [HAY, HAY2]}})

wagon = [part((3.6, 0.12, 1.7), (0, 0.95, 0), OAK, material="wood"),
         part((3.6, 0.5, 0.08), (0, 1.25, 0.82), OAKD, material="wood"), part((3.6, 0.5, 0.08), (0, 1.25, -0.82), OAKD, material="wood"),
         part((0.08, 0.5, 1.7), (1.78, 1.25, 0), OAKD, material="wood"), part((0.08, 0.7, 1.7), (-1.78, 1.35, 0), OAKD, material="wood")]
for sx, d in ((1.25, 0.95), (-1.25, 1.2)):
    for sz in (0.95, -0.95):
        wagon += drum_z(0.1, d, d / 2, "#4a3826", x=sx, z=sz, material="wood")
        wagon.append(part((0.14, 0.14, 0.14), (sx, d / 2, sz), IRON))
wagon += [part((0.08, 0.08, 2.0), (1.25, 0.47, 0), IRON), part((0.08, 0.08, 2.0), (-1.25, 0.6, 0), IRON),
          part((1.6, 0.07, 0.07), (2.55, 0.7, 0.35), OAK, rotation=[0, 8, -14]), part((1.6, 0.07, 0.07), (2.55, 0.7, -0.35), OAK, rotation=[0, -8, -14])]
for k in range(6):   # the load of loose hay, heaped
    wagon.append(part((3.0 - k * 0.32, 0.32, 1.55 - k * 0.12), (-0.05 + (k % 2) * 0.08, 1.2 + k * 0.24, 0), [HAY, HAY2, HAY3][k % 3],
                      material="dirt", texScale=2, rotation=[0, (k % 3 - 1) * 4, 0]))
add("Norman Hay Wagon", "A four-wheeled timber farm wagon heaped with loose hay, its shafts down. Solid cover.",
    wagon, collider=[{"size": [3.6, 2.6, 1.9], "offset": [0, 1.3, 0]}], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 12})

cart = []
tilt = 18.0   # body tipped back, shafts in the air
th = math.radians(tilt)
cart += drum_z(0.1, 1.35, 0.675, "#4a3826", x=0, z=0.85, material="wood") + drum_z(0.1, 1.35, 0.675, "#4a3826", x=0, z=-0.85, material="wood")
cart += [part((0.12, 0.12, 1.9), (0, 0.675, 0), IRON)]
cart += [part((2.2, 0.1, 1.5), (-0.25, 0.85, 0), "#7a5e3c", material="wood", rotation=[0, 0, -tilt]),
         part((2.2, 0.45, 0.07), (-0.25, 1.08, 0.74), "#6a5034", material="wood", rotation=[0, 0, -tilt]),
         part((2.2, 0.45, 0.07), (-0.25, 1.08, -0.74), "#6a5034", material="wood", rotation=[0, 0, -tilt]),
         part((0.07, 0.45, 1.5), (-1.3, 0.55, 0), "#6a5034", material="wood", rotation=[0, 0, -tilt]),
         part((2.4, 0.08, 0.08), (1.75, 1.35, 0.5), OAK, material="wood", rotation=[0, 0, -tilt - 6]),
         part((2.4, 0.08, 0.08), (1.75, 1.35, -0.5), OAK, material="wood", rotation=[0, 0, -tilt - 6]),
         part((0.08, 0.08, 1.0), (2.85, 1.7, 0), OAK, material="wood")]
add("Tipped Farm Cart", "A two-wheeled tumbril farm cart tipped back on its axle, shafts up in the air.",
    cart, collider=[{"size": [2.4, 1.3, 1.9], "offset": [-0.3, 0.65, 0]}], scatter={"randomYaw": True},
    breakable={"health": 150, "breakBy": ["explosions", "streaks"], "sound": "wood", "debris": {"count": 18, "size": [0.08, 0.5], "colors": ["#7a5e3c", OAK]}})

GREYT = "#8f9496"
GREYT2 = "#6e7375"
tr = []
tr += [part((1.7, 0.55, 0.62), (0.45, 0.95, 0), GREYT, material="metal"),          # bonnet
       part((0.3, 0.62, 0.66), (1.32, 0.92, 0), "#3c3e40", material="metal"),       # grille
       part((0.9, 0.6, 0.7), (-0.55, 0.85, 0), GREYT2, material="metal"),           # gearbox / transmission
       part((0.1, 0.55, 0.1), (0.95, 1.45, 0.18), "#2a2a2a"),                        # exhaust stack
       part((0.5, 0.08, 0.45), (-0.85, 1.32, 0), "#2a2a2a"),                         # seat
       part((0.06, 0.4, 0.06), (-0.85, 1.12, 0), "#2a2a2a"),
       part((0.06, 0.5, 0.06), (-0.35, 1.45, 0), "#2a2a2a", rotation=[0, 0, 35]),  # steering column
       part((0.36, 0.04, 0.36), (-0.5, 1.68, 0), "#1e1e1e", rotation=[0, 0, 35]),
       part((0.7, 0.06, 0.32), (-0.85, 1.02, 0.62), GREYT, material="metal"), part((0.7, 0.06, 0.32), (-0.85, 1.02, -0.62), GREYT, material="metal"),
       part((0.4, 0.3, 0.14), (-1.25, 0.5, 0), "#3a3a3a")]                           # hitch
for sz in (0.72, -0.72):
    tr += drum_z(0.34, 1.32, 0.66, "#1e1e1e", x=-0.7, z=sz) + drum_z(0.36, 0.7, 0.66, "#a8a8a8", x=-0.7, z=sz * 1.01)
    tr += drum_z(0.2, 0.68, 0.34, "#1e1e1e", x=1.0, z=sz * 0.78) + drum_z(0.22, 0.34, 0.34, "#a8a8a8", x=1.0, z=sz * 0.79)
tr += [part((0.08, 0.08, 1.25), (1.0, 0.34, 0), "#3a3a3a"), part((0.08, 0.08, 1.6), (-0.7, 0.66, 0), "#3a3a3a")]
add("Vintage Tractor", "A little grey 1950s farm tractor with big rear wheels and a tall exhaust: solid cover.",
    tr, collider=[{"size": [2.6, 1.4, 1.75], "offset": [0.0, 0.7, 0]}], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 20})

plough = [part((1.6, 0.1, 0.1), (0, 0.55, 0), "#4a4a46", rotation=[0, 0, -10])]
for k, z in enumerate((-0.35, 0.0, 0.35)):
    plough += [part((0.06, 0.55, 0.06), (-0.3 + k * 0.3, 0.3, z), "#3a3a36"), part((0.45, 0.06, 0.28), (-0.45 + k * 0.3, 0.06, z), "#5a5650", rotation=[0, 20, 25])]
plough += [part((0.7, 0.06, 0.06), (0.75, 0.75, 0.25), OAK, rotation=[0, 0, 30]), part((0.7, 0.06, 0.06), (0.75, 0.75, -0.25), OAK, rotation=[0, 0, 30])]
plough += drum_z(0.06, 0.5, 0.25, "#3a3a36", x=0.55, z=0)
add("Rusty Plough", "An old three-furrow horse plough left at a field edge, rusting.",
    plough, collision="cosmetic", scatter={"randomYaw": True}, fade=55)

# ======================================================================================== yard and village
trough = [part((2.0, 0.62, 0.72), (0, 0.31, 0), GRANITE, material="concrete"),
          part((1.8, 0.04, 0.52), (0, 0.6, 0), "#3e5a5c"),
          part((2.04, 0.08, 0.1), (0, 0.66, 0.33), GRANITE2, material="concrete"), part((2.04, 0.08, 0.1), (0, 0.66, -0.33), GRANITE2, material="concrete"),
          part((0.1, 0.08, 0.72), (0.98, 0.66, 0), GRANITE2, material="concrete"), part((0.1, 0.08, 0.72), (-0.98, 0.66, 0), GRANITE2, material="concrete"),
          part((0.08, 0.9, 0.08), (1.15, 0.45, 0), IRON), part((0.3, 0.06, 0.06), (1.08, 0.9, 0), IRON), part((0.04, 0.3, 0.04), (1.18, 1.05, 0), IRON)]
add("Stone Water Trough", "A granite cattle trough with a hand pump, full of water.",
    trough, collider=[{"size": [2.0, 0.7, 0.72], "offset": [0, 0.35, 0]}], scatter={"randomYaw": False, "yawStep": 90})

cross = [part((2.4, 0.3, 2.4), (0, 0.15, 0), GRANITE2, material="concrete"),
         part((1.8, 0.3, 1.8), (0, 0.45, 0), GRANITE, material="concrete"),
         part((1.2, 0.3, 1.2), (0, 0.75, 0), GRANITE2, material="concrete"),
         part((0.7, 0.9, 0.7), (0, 1.35, 0), GRANITE, material="concrete"),
         part((0.8, 0.1, 0.8), (0, 1.84, 0), GRANITE2, material="concrete")]
cross += octagon(0.34, 2.6, 3.19, GRANITE, material="concrete")
cross += [part((0.18, 1.2, 0.14), (0, 5.05, 0), "#5a5852", material="concrete"),
          part((0.85, 0.16, 0.14), (0, 5.15, 0), "#5a5852", material="concrete"),
          part((0.5, 0.06, 0.03), (0, 4.45, 0.08), "#3a3a36"),
          part((0.12, 0.4, 0.06), (0, 4.95, 0.1), "#c8c4b8", material="concrete"),           # the figure on the cross
          part((0.42, 0.08, 0.05), (0, 5.12, 0.1), "#c8c4b8", material="concrete"),
          part((0.07, 0.07, 0.07), (0, 5.26, 0.1), "#c8c4b8", material="concrete"),
          part((0.3, 0.32, 0.3), (0.55, 1.06, 0.55), "#9a3a3a"), part((0.22, 0.2, 0.22), (0.55, 1.32, 0.55), "#c84a3a"),  # flowers left at the foot
          part((0.26, 0.3, 0.26), (-0.55, 1.05, 0.58), "#8a7a62"), part((0.2, 0.18, 0.2), (-0.55, 1.29, 0.58), "#e0d070")]
add("Calvary Cross", "A Breton-Norman calvary: a granite cross on a stepped plinth at a village crossroads, flowers at its foot.",
    cross, collider=[{"size": [2.4, 0.9, 2.4], "offset": [0, 0.45, 0]}, {"size": [0.7, 4.5, 0.7], "offset": [0, 3.15, 0]}],
    scatter={"randomYaw": False, "yawStep": 90})

well = octagon(1.6, 0.85, 0.425, GRANITE, material="concrete") + octagon(1.72, 0.12, 0.9, GRANITE2, material="concrete")
well += octagon(1.25, 0.05, 0.84, "#1a2224")
well += [part((0.14, 1.6, 0.14), (sx * 0.72, 1.6, 0), OAKD, material="wood") for sx in (1, -1)]
well += [part((1.6, 0.1, 0.1), (0, 2.25, 0), OAKD, material="wood"), part((0.06, 0.06, 0.4), (0.82, 2.25, 0.2), IRON),
         part((1.9, 0.06, 0.95), (0, 2.6, 0.38), "#5a4636", rotation=[30, 0, 0], material="wood"),
         part((1.9, 0.06, 0.95), (0, 2.6, -0.38), "#5a4636", rotation=[-30, 0, 0], material="wood"),
         part((0.03, 0.8, 0.03), (0, 1.8, 0), "#a09070"), part((0.26, 0.26, 0.26), (0, 1.3, 0), "#6a5236", material="wood")]
add("Stone Well", "A round granite village well with a timber windlass, a little roof and the bucket hanging.",
    well, collider=[{"size": [1.55, 0.95, 1.55], "offset": [0, 0.475, 0]}, {"size": [1.6, 2.2, 0.16], "offset": [0, 1.4, 0]}],
    scatter={"randomYaw": False, "yawStep": 90})

barrel = octagon(0.66, 0.95, 0.475, "#7a5432", material="wood") + octagon(0.72, 0.6, 0.475, "#86603a", material="wood")
barrel += octagon(0.74, 0.05, 0.12, IRON) + octagon(0.74, 0.05, 0.83, IRON) + octagon(0.6, 0.02, 0.96, "#5a3e24", material="wood")
add("Cider Barrel", "An oak cider barrel standing on end, iron hoops: Normandy's cidre, not wine.",
    barrel, collider=[{"size": [0.62, 0.97, 0.62], "offset": [0, 0.485, 0]}], scatter={"randomYaw": True, "scale": [0.95, 1.05]},
    breakable={"health": 70, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.06, 0.3], "colors": ["#7a5432", "#86603a", IRON]}})

churns = []
for k, (x, z) in enumerate(((-0.32, 0.0), (0.05, 0.05), (0.38, -0.05))):
    churns += octagon(0.32, 0.5, 0.25, "#b8bcbe", x=x, z=z, material="metal") + octagon(0.22, 0.14, 0.57, "#b0b4b6", x=x, z=z, material="metal")
    churns += [part((0.26, 0.06, 0.26), (x, 0.67, z), "#8a8e90", material="metal"), part((0.3, 0.03, 0.04), (x, 0.42, z), "#8a8e90")]
add("Milk Churns", "Three steel milk churns waiting by the gate for the creamery lorry.",
    churns, collider=[{"size": [1.05, 0.7, 0.4], "offset": [0.03, 0.35, 0]}], scatter={"randomYaw": True},
    breakable={"health": 90, "breakBy": "all", "sound": "metal", "debris": {"count": 10, "size": [0.05, 0.2], "colors": ["#b8bcbe", "#8a8e90"]}})

tools_ = [part((0.03, 1.5, 0.03), (-0.3, 0.75, 0.05), "#8a6a44", rotation=[-12, 0, 6]), part((0.2, 0.25, 0.02), (-0.33, 0.12, 0.2), "#5a5a58", rotation=[-12, 0, 6]),
          part((0.03, 1.6, 0.03), (0.0, 0.8, 0.06), "#8a6a44", rotation=[-10, 0, 0]), part((0.38, 0.03, 0.05), (0.0, 0.05, 0.2), "#5a5a58"),
          part((0.03, 1.7, 0.03), (0.32, 0.85, 0.05), "#8a6a44", rotation=[-10, 0, -8]), part((0.55, 0.04, 0.02), (0.55, 1.62, 0.2), "#7a7a78", rotation=[0, 20, -30]),
          part((0.45, 0.35, 0.3), (0.75, 0.18, 0.15), "#5a4a3a", material="wood")]
add("Leaning Farm Tools", "A pitchfork, a rake and a scythe leaning on a wall beside a trug of kindling.",
    tools_, collision="cosmetic", scatter={"randomYaw": False, "yawStep": 90}, fade=45)

wood = [part((1.9, 1.0, 0.55), (0, 0.5, 0), "#6a5032", material="wood")]
rng = random.Random(9)
for row in range(4):
    for k in range(7):
        x = -0.82 + k * 0.27 + (0.13 if row % 2 else 0)
        if x > 0.85:
            continue
        wood += drum_z(0.57, 0.24, 0.13 + row * 0.24, rng.choice(["#a07a4c", "#8e6a40", "#b0885a"]), x=x, z=0.0)
wood += [part((2.1, 0.06, 0.8), (0, 1.12, 0.05), "#4a4a46", rotation=[8, 0, 0])]
add("Firewood Stack", "A stack of split logs under a scrap of corrugated tin, against a farmhouse wall.",
    wood, collider=[{"size": [1.95, 1.05, 0.6], "offset": [0, 0.525, 0]}], scatter={"randomYaw": False, "yawStep": 90},
    breakable={"health": 160, "breakBy": ["explosions", "streaks"], "sound": "wood", "debris": {"count": 18, "size": [0.08, 0.35], "colors": ["#a07a4c", "#8e6a40"]}})

muck = [part((2.6, 0.5, 2.0), (0, 0.25, 0), "#4a3a26", material="dirt"), part((2.0, 0.4, 1.5), (0.1, 0.65, 0), "#56442c", material="dirt", rotation=[0, 10, 0]),
        part((1.2, 0.3, 0.9), (-0.1, 0.98, 0.05), "#5e4a30", material="dirt", rotation=[0, -15, 0]), part((0.7, 0.12, 0.5), (0.4, 1.15, 0), HAY2, material="dirt"),
        part((0.03, 1.3, 0.03), (0.2, 1.4, -0.2), "#8a6a44", rotation=[20, 0, 15])]
add("Muck Heap", "A steaming farmyard muck heap with a fork stuck in it. Waist-high: you can mantle it, if you must.",
    muck, collider=[{"size": [2.4, 1.1, 1.8], "offset": [0, 0.55, 0]}], scatter={"randomYaw": True})

sign = [part((0.1, 2.6, 0.1), (0, 1.3, 0), "#e8e6e0"), part((0.14, 0.1, 0.14), (0, 2.65, 0), "#2a2a2a"),
        part((1.1, 0.24, 0.04), (0.5, 2.3, 0), "#f2f0ea"), part((1.06, 0.03, 0.045), (0.5, 2.39, 0), "#c8302a"),
        part((0.7, 0.06, 0.05), (0.42, 2.28, 0), "#2a2a2a"),
        part((0.16, 0.16, 0.04), (1.1, 2.3, 0), "#f2f0ea", rotation=[0, 0, 45]),
        part((1.0, 0.24, 0.04), (-0.45, 1.95, 0), "#f2f0ea", rotation=[0, 70, 0]), part((0.62, 0.06, 0.05), (-0.42, 1.93, 0), "#2a2a2a", rotation=[0, 70, 0]),
        part((0.6, 0.22, 0.04), (0, 1.55, 0.06), "#3a62a8")]
add("Village Signpost", "A white French country fingerpost: two arrow boards to the next hamlets and a blue route plate.",
    sign, collider=[{"size": [0.12, 2.6, 0.12], "offset": [0, 1.3, 0]}], scatter={"randomYaw": True})

ladder = [part((0.06, 3.2, 0.06), (-0.28, 1.55, 0.15), OAK, rotation=[-14, 0, 4]), part((0.06, 3.2, 0.06), (0.28, 1.55, 0.15), OAK, rotation=[-14, 0, -4]),
          part((0.06, 3.0, 0.06), (0.0, 1.45, -0.55), OAK, rotation=[22, 0, 0])]
for k in range(8):
    y = 0.3 + k * 0.36
    w = 0.6 - k * 0.04
    ladder.append(part((w, 0.04, 0.04), (0, y, 0.15 - (y - 1.55) * math.tan(math.radians(14))), OAK))
add("Orchard Ladder", "A tall three-legged orchard ladder for picking apples. Looks only.",
    ladder, collision="cosmetic", scatter={"randomYaw": True}, fade=50)

apples = []
rng = random.Random(14)
for k in range(16):
    apples.append(part((0.08, 0.07, 0.08), (rng.uniform(-0.9, 0.9), 0.035, rng.uniform(-0.9, 0.9)), rng.choice(["#b8301e", "#c84a26", "#9a2a1a", "#a8a030", "#c8a03a"]),
                       rotation=[0, rng.uniform(0, 90), 0]))
add("Fallen Apples", "Windfall apples in the orchard grass.", apples, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.8, 1.3]}, fade=26)

straw = []
rng = random.Random(15)
for k in range(10):
    straw.append(part((rng.uniform(0.4, 0.9), 0.02, rng.uniform(0.15, 0.4)), (rng.uniform(-0.8, 0.8), 0.01 + k * 0.001, rng.uniform(-0.6, 0.6)), rng.choice([HAY, HAY2, HAY3]),
                      rotation=[0, rng.uniform(0, 180), 0]))
add("Loose Straw", "Wisps of straw blown across a farmyard.", straw, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.8, 1.4]}, fade=30)

# ======================================================================================== orchard tree
def apple_tree(seed, h=4.2):
    rng = random.Random(seed)
    ps = [part((0.28, 1.5, 0.28), (0, 0.75, 0), "#5a4630", material="wood", rotation=[0, 0, rng.uniform(-4, 4)]),
          part((0.22, 1.0, 0.22), (0.25, 1.7, 0.1), "#5a4630", material="wood", rotation=[0, 20, -28]),
          part((0.2, 1.0, 0.2), (-0.22, 1.75, -0.12), "#5a4630", material="wood", rotation=[0, -30, 30]),
          part((0.16, 0.9, 0.16), (0.05, 1.95, -0.3), "#5a4630", material="wood", rotation=[28, 0, 0])]
    greens = ["#4f6e2c", "#5a7a32", "#476628", "#62823a"]
    blobs = [(0, 2.95, 0, 2.6, 1.3), (0.8, 2.6, 0.3, 1.6, 1.0), (-0.8, 2.65, -0.2, 1.7, 1.0), (0.2, 2.6, -0.85, 1.6, 0.95),
             (-0.2, 2.6, 0.85, 1.6, 0.95), (0.1, 3.55, 0.1, 1.7, 0.7)]
    for (x, y, z, w, hh) in blobs:
        ps.append(part((w, hh, w * rng.uniform(0.85, 1.05)), (x, y * h / 4.2, z), rng.choice(greens), material="dirt",
                       rotation=[rng.uniform(-8, 8), rng.uniform(0, 90), rng.uniform(-8, 8)]))
    for k in range(18):   # apples showing on the outside of the canopy
        a = rng.uniform(0, 2 * math.pi); r = rng.uniform(1.5, 1.68); y = rng.uniform(2.25, 3.0) * h / 4.2
        ps.append(part((0.14, 0.14, 0.14), (r * math.cos(a), y, r * math.sin(a)), rng.choice(["#c0301e", "#d04a24", "#a8281a", "#c8b03a"])))
    return ps


add("Apple Tree", "A cider-apple tree from a Normandy orchard: a low gnarled trunk, a round crown and red apples. The trunk is solid.",
    apple_tree(3), collider=[{"size": [0.34, 2.0, 0.34], "offset": [0, 1.0, 0]}], scatter={"randomYaw": True, "scale": [0.85, 1.15]})
add("Apple Tree B", "Another orchard apple tree, a little taller and lopsided.",
    apple_tree(8, 4.6), collider=[{"size": [0.34, 2.0, 0.34], "offset": [0, 1.0, 0]}], scatter={"randomYaw": True, "scale": [0.85, 1.15]})

# ======================================================================================== silo
silo = []
CON = "#b2aea4"
CON2 = "#a29e94"
for k in range(10):
    silo += octagon(4.0, 1.05, 0.55 + k * 1.05, CON if k % 2 == 0 else CON2, material="concrete")
for k in range(5):
    silo += octagon(4.06, 0.06, 1.1 + k * 2.1, "#5a5a58")
silo += octagon(4.1, 0.25, 10.7, "#7a7e80", material="metal") + octagon(3.2, 0.5, 11.05, "#8a8e90", material="metal") + octagon(1.9, 0.45, 11.5, "#8a8e90", material="metal")
silo += [part((0.6, 0.3, 0.6), (0, 11.7, 0), "#6a6e70", material="metal")]
for k in range(25):   # the ladder up the side, with its cage hoops
    silo.append(part((0.5, 0.04, 0.04), (0, 0.4 + k * 0.42, 2.12), "#4a4a48"))
silo += [part((0.05, 10.6, 0.05), (sx, 5.4, 2.12), "#4a4a48") for sx in (0.25, -0.25)]
silo += [part((0.9, 0.04, 0.6), (0, 3.0 + k * 1.6, 2.35), "#4a4a48") for k in range(5)]
silo += [part((0.4, 0.4, 2.6), (1.6, 10.3, 2.2), "#8a8e90", material="metal", rotation=[-40, 0, 0]),       # filling pipe
         part((0.9, 0.9, 0.9), (1.2, 0.9, 2.1), "#7a7e80", material="metal"), part((0.5, 0.6, 0.5), (1.2, 0.3, 2.6), "#6a5a3a")]
add("Concrete Grain Silo", "A 12 m concrete-stave grain silo with a steel cap, a caged ladder and a filling pipe. Solid.",
    silo, collider=[{"size": [3.4, 11.0, 3.4], "offset": [0, 5.5, 0]}, {"size": [2.6, 11.0, 4.1], "offset": [0, 5.5, 0]}],
    scatter={"randomYaw": False, "yawStep": 90})

# ======================================================================================== animals
NORM = "#ece6da"
NORMB = "#6a4228"


def cow(seed, grazing=False):
    rng = random.Random(seed)
    ps = [part((1.55, 0.72, 0.68), (0, 1.02, 0), NORM),
          part((0.5, 0.62, 0.62), (0.62, 1.05, 0), NORM),
          part((0.45, 0.55, 0.6), (-0.6, 1.05, 0), NORM)]
    for k in range(4):   # brown patches (Normande cattle)
        ps.append(part((rng.uniform(0.3, 0.55), rng.uniform(0.3, 0.45), 0.7), (rng.uniform(-0.55, 0.45), rng.uniform(0.95, 1.2), 0.005 * k), NORMB))
    for sx in (0.55, -0.55):
        for sz in (0.22, -0.22):
            ps.append(part((0.15, 0.68, 0.15), (sx, 0.34, sz), NORM if sx > 0 else NORMB))
            ps.append(part((0.16, 0.08, 0.16), (sx, 0.04, sz), "#2a2420"))
    hy = 0.55 if grazing else 1.25
    hx = 1.15 if grazing else 1.08
    ps += [part((0.4, 0.3, 0.32), (0.92, hy + 0.1 if not grazing else 0.8, 0), NORM, rotation=[0, 0, -45 if grazing else -15]),   # neck
           part((0.42, 0.34, 0.3), (hx, hy, 0), NORM, rotation=[0, 0, -60 if grazing else -20]),
           part((0.16, 0.18, 0.31), (hx + 0.16, hy - (0.18 if grazing else 0.08), 0), "#c8a898", rotation=[0, 0, -60 if grazing else -20]),
           part((0.1, 0.12, 0.33), (hx - 0.02, hy + 0.06, 0), NORMB, rotation=[0, 0, -60 if grazing else -20]),     # spectacles round the eyes
           part((0.06, 0.06, 0.5), (hx - 0.12, hy + 0.18, 0), "#d8d0b8"),                                         # horns
           part((0.14, 0.05, 0.08), (hx - 0.14, hy + 0.12, 0.22), NORM), part((0.14, 0.05, 0.08), (hx - 0.14, hy + 0.12, -0.22), NORM),
           part((0.05, 0.55, 0.05), (-0.82, 0.95, 0), NORMB, rotation=[0, 0, -8]), part((0.08, 0.12, 0.08), (-0.86, 0.66, 0), "#2a2420"),
           part((0.24, 0.12, 0.26), (-0.2, 0.62, 0), "#e8b8b0")]
    return ps


add("Dairy Cow", "A Normande dairy cow, white with brown patches and the breed's dark spectacles, looking up.",
    cow(4), collider=[{"size": [1.7, 1.4, 0.7], "offset": [0.05, 0.7, 0]}], scatter={"randomYaw": True, "scale": [0.95, 1.05]})
add("Grazing Cow", "A Normande dairy cow with its head down in the grass.",
    cow(7, grazing=True), collider=[{"size": [1.7, 1.4, 0.7], "offset": [0.05, 0.7, 0]}], scatter={"randomYaw": True, "scale": [0.95, 1.05]})

sheep = [part((0.85, 0.5, 0.52), (0, 0.6, 0), "#e2dcc8"), part((0.6, 0.12, 0.46), (0, 0.9, 0), "#d6d0bc"), part((0.3, 0.42, 0.56), (-0.32, 0.6, 0), "#d8d2be"),
         part((0.22, 0.24, 0.2), (0.5, 0.72, 0), "#2a2622", rotation=[0, 0, -20]), part((0.12, 0.05, 0.1), (0.46, 0.84, 0.13), "#2a2622"),
         part((0.12, 0.05, 0.1), (0.46, 0.84, -0.13), "#2a2622")]
sheep += [part((0.07, 0.38, 0.07), (sx, 0.19, sz), "#2a2622") for sx in (0.28, -0.28) for sz in (0.14, -0.14)]
add("Sheep", "A woolly sheep with a black face. Looks only.", sheep, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.85, 1.1]}, fade=60)


# ======================================================================================== write
def main():
    existing = {}
    for f in glob.glob(os.path.join(REPO, "Props/**/*.json"), recursive=True) + glob.glob(os.path.join(REPO, "Maps/*/Props/**/*.json"), recursive=True) \
            + glob.glob(os.path.join(REPO, "Buildings/*/Props/*.json")):
        if os.path.abspath(os.path.dirname(f)) == os.path.abspath(OUT) or "/Flags/" in f:
            continue
        try:
            existing[json.load(open(f))["name"].lower()] = f
        except Exception:
            pass
    os.makedirs(OUT, exist_ok=True)
    names = set()
    for p in PROPS:
        n = p["name"]
        if n.lower() in existing:
            raise SystemExit(f"prop name clash: {n} (also {existing[n.lower()]})")
        if n.lower() in names:
            raise SystemExit(f"duplicate {n}")
        names.add(n.lower())
        if len(p["parts"]) > 128:
            raise SystemExit(f"{n}: {len(p['parts'])} parts")
        fn = "".join(ch for ch in n.title() if ch.isalnum()) + ".json"
        lines = ["{"]
        keys = [k for k in p if k != "parts"]
        for k in keys:
            lines.append(f'  "{k}": {json.dumps(p[k])},')
        lines.append('  "parts": [')
        lines.append(",\n".join("    " + json.dumps(q) for q in p["parts"]))
        lines.append("  ]")
        lines.append("}")
        open(os.path.join(OUT, fn), "w").write("\n".join(lines) + "\n")
    print(f"{len(PROPS)} props written to {OUT}")


if __name__ == "__main__":
    main()
