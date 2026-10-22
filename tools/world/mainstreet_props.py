#!/usr/bin/env python3
"""Writes the general-purpose props the World Tour's Main Street map (Maps/World/01-MainStreet.json) needed and no
prop covered, into the shared Props folder (they're reusable: any farm, rail or town map can use them):

- Props/Farm:  Corn Patch A / B (3 x 3 m of tall field corn: cosmetic, "concealment": bots can't see through it), Trampled
               Corn, Field Scarecrow, Row Crop Tractor, Hay Wagon, Big Round Bale, Grain Bin, Split Rail Fence, Wheel Line
               Irrigator, Roadside Farm Stand, Stock Tank, Pumpkin Pile, Farm Fuel Tank.
- Props/Rail:  Railroad Track 6m, Railroad Crossbuck, Freight Boxcar, Freight Boxcar Open (doors open: climb in and
               through), Covered Hopper Car.
- Props/Town:  Bandstand Gazebo, Memorial Obelisk.

Usage: python3 tools/world/mainstreet_props.py <repo root>

Conventions as in Props/README.md: the origin is on the ground at the centre, long things run along +X, things that face
a way face +Z. Vehicles and rail cars are solid, unbreakable cover. Names were checked against every prop in Props/ and
the pack Props folders (prop names are global).
"""
import json, math, os, random, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."


def R(v):
    return round(float(v), 3)


def part(size, offset, color, **kw):
    p = {"size": [R(v) for v in size], "offset": [R(v) for v in offset], "color": color}
    p.update(kw)
    return p


def box(size, offset):
    return {"size": [R(v) for v in size], "offset": [R(v) for v in offset]}


def disc(d, w, x, y, z, color, axis="z", n=2, **kw):
    """A round thing (wheel, drum end): n boxes turned about the axis (2 = eight-point star, 4 = sixteen)."""
    ps = []
    for k in range(n):
        a = 90.0 / n * k
        if axis == "z":
            ps.append(part((d * 0.92, d * 0.92, w), (x, y, z), color, rotation=[0, 0, R(a)], **kw))
        else:
            ps.append(part((w, d * 0.92, d * 0.92), (x, y, z), color, rotation=[R(a), 0, 0], **kw))
    return ps


def upright(d, h, x, y0, z, color, n=4, **kw):
    """An upright octagonal (n=4) column: four boxes d x h x 0.414d turned 0/45/90/135."""
    return [part((d, h, d * 0.414), (x, y0 + h / 2, z), color, rotation=[0, R(45 * k), 0], **kw) for k in range(n)]


OUT = []


def add(folder, name, desc, parts, collision="solid", collider=None, breakable=None, scatter=None, fade=None, concealment=False):
    d = {"format": "call-of-slop-prop/1", "name": name, "category": folder, "description": desc, "author": "Call of Slop", "collision": collision}
    if collider is not None:
        d["collider"] = collider
    if concealment:
        d["concealment"] = True
    if fade is not None:
        d["fadeDistance"] = fade
    d["scatter"] = scatter or {"randomYaw": False, "yawStep": 90, "scale": [1, 1]}
    if breakable:
        d["breakable"] = breakable
    assert 1 <= len(parts) <= 128, (name, len(parts))
    d["parts"] = parts
    OUT.append((folder, d))


# ===================================================================================== Farm

STALK = ["#5f7d2e", "#668531", "#58752b"]
LEAF = ["#6b8f35", "#77983b", "#5f832e", "#83973f"]
DRY = "#9a9a52"
TASSEL = "#c4ad66"
HUSK = "#a3b05c"


def corn_patch(seed):
    """3 x 3 m of field corn: four rows along X (0.75 m apart), six stalks a row, two leaves a stalk, a tassel and the odd
    ear. Each row sways gently as one (its first stalk swings, the rest follow), the rows out of step."""
    rng = random.Random(seed)
    ps = []
    for ri, rz in enumerate((-1.125, -0.375, 0.375, 1.125)):
        first = None
        for k in range(6):
            x = -1.25 + k * 0.5 + rng.uniform(-0.09, 0.09)
            z = rz + rng.uniform(-0.06, 0.06)
            h = rng.uniform(2.15, 2.6)
            group = []
            group.append(part((0.045, h, 0.045), (x, h / 2, z), rng.choice(STALK)))
            for li, (fy, roll) in enumerate(((0.38, -14), (0.64, 12))):
                yaw = rng.uniform(0, 180) if li == 0 else (group[-1]["rotation"][1] + rng.uniform(70, 110)) % 180
                col = DRY if (li == 0 and rng.random() < 0.3) else rng.choice(LEAF)
                group.append(part((rng.uniform(0.8, 1.0), 0.018, 0.1), (x, h * fy, z), col, rotation=[0, R(yaw), roll]))
            group.append(part((0.06, 0.3, 0.06), (x, h + 0.13, z), TASSEL))
            if rng.random() < 0.33:
                group.append(part((0.07, 0.22, 0.07), (x + 0.05, h * 0.5, z), HUSK, rotation=[0, 0, -18]))
            for p in group:
                if first is None:
                    first = len(ps) + 1
                    p["anim"] = {"type": "swing", "axis": "x", "pivot": [R(x), 0, R(rz)], "angle": 1.4, "period": R(rng.uniform(3.2, 4.2)),
                                 "wind": 1, "jitter": 0.2, "phase": R(ri * 0.27)}
                else:
                    p["anim"] = {"type": "follow", "parent": first}
                ps.append(p)
    return ps


for nm, sd in (("Corn Patch A", 11), ("Corn Patch B", 23)):
    add("Farm", nm, "3 x 3 m of tall field corn in four rows (along X), higher than your head. Walk and shoot through it, but it hides you, from bots too (concealment). Tile them at 3 m.",
        corn_patch(sd), collision="cosmetic", fade=300, concealment=True, scatter={"randomYaw": False, "yawStep": 180, "scale": [0.95, 1.06]})

# Trampled corn: stalks snapped and lying about, a few still standing at knee height.
rng = random.Random(5)
ps = []
for k in range(26):
    x, z = rng.uniform(-1.3, 1.3), rng.uniform(-1.3, 1.3)
    L = rng.uniform(1.2, 2.0)
    ps.append(part((L, 0.04, 0.04), (x, 0.04, z), rng.choice(["#8a8a48", "#7b8a3c", DRY]), rotation=[0, R(rng.uniform(0, 180)), R(rng.uniform(-6, 6))]))
    if k % 2 == 0:
        ps.append(part((0.7, 0.015, 0.09), (x + rng.uniform(-0.3, 0.3), 0.07, z + rng.uniform(-0.3, 0.3)), rng.choice([DRY, "#a39a5a", "#6b8f35"]), rotation=[0, R(rng.uniform(0, 180)), 0]))
for k in range(9):
    x, z = rng.uniform(-1.3, 1.3), rng.uniform(-1.3, 1.3); h = rng.uniform(0.35, 0.8)
    ps.append(part((0.045, h, 0.045), (x, h / 2, z), rng.choice(STALK), rotation=[R(rng.uniform(-20, 20)), 0, R(rng.uniform(-20, 20))]))
add("Farm", "Trampled Corn", "3 x 3 m of corn trodden flat: snapped stalks and leaves on the ground, a few stubs standing. For paths through a field.",
    ps, collision="cosmetic", fade=120, scatter={"randomYaw": True, "scale": [0.9, 1.1]})

# Scarecrow: post, cross arm, flannel shirt, straw hat, pumpkin-ish sack head.
ps = [part((0.1, 2.3, 0.1), (0, 1.15, 0), "darkwood", material="wood"),
      part((1.5, 0.08, 0.08), (0, 1.62, 0), "darkwood", material="wood"),
      part((0.5, 0.62, 0.26), (0, 1.38, 0), "#a33a2c"),            # shirt body
      part((0.52, 0.04, 0.27), (0, 1.5, 0.002), "#2e3a5c"),          # plaid band
      part((0.52, 0.04, 0.27), (0, 1.3, 0.002), "#2e3a5c"),
      part((0.62, 0.16, 0.18), (-0.47, 1.6, 0), "#a33a2c"),          # sleeves
      part((0.62, 0.16, 0.18), (0.47, 1.6, 0), "#a33a2c"),
      part((0.1, 0.22, 0.1), (-0.82, 1.53, 0), "#d8c070", rotation=[0, 0, 15]),   # straw hands
      part((0.1, 0.22, 0.1), (0.82, 1.53, 0), "#d8c070", rotation=[0, 0, -15]),
      part((0.28, 0.3, 0.26), (0, 1.86, 0), "#c9b48a"),             # sack head
      part((0.05, 0.05, 0.02), (-0.06, 1.9, 0.13), "black"), part((0.05, 0.05, 0.02), (0.06, 1.9, 0.13), "black"),
      part((0.14, 0.02, 0.02), (0, 1.8, 0.13), "black"),
      part((0.62, 0.03, 0.62), (0, 2.02, 0), "#d2b866"),             # hat brim
      part((0.3, 0.16, 0.3), (0, 2.11, 0), "#c8ac5c"),
      part((0.32, 0.03, 0.32), (0, 2.05, 0), "#7a2a22"),            # hat band
      part((0.36, 0.5, 0.22), (0, 0.85, 0), "#3c4f74"),             # dungarees
      part((0.12, 0.5, 0.12), (-0.1, 0.6, 0), "#3c4f74", rotation=[0, 0, 8]),
      part((0.12, 0.5, 0.12), (0.1, 0.6, 0), "#3c4f74", rotation=[0, 0, -8]),
      part((0.18, 0.12, 0.12), (-0.14, 0.33, 0), "#d8c070"), part((0.18, 0.12, 0.12), (0.14, 0.33, 0), "#d8c070")]
add("Farm", "Field Scarecrow", "A scarecrow on a post: plaid shirt, dungarees, sack head and a straw hat, arms out along X.",
    ps, collider=[box((0.14, 2.2, 0.14), (0, 1.1, 0))],
    breakable={"health": 45, "breakBy": "all", "sound": "soft", "debris": {"count": 14, "size": [0.05, 0.2], "speed": 3, "colors": ["#a33a2c", "#d8c070", "#3c4f74"]}},
    scatter={"randomYaw": True, "scale": [0.95, 1.05]})

# Row crop tractor (front at +X): big rear wheels, small fronts, hood, cab-less seat with a ROPS frame.
TR = "#b8462e"; TRIM = "#d8d2c4"
ps = [part((1.9, 0.75, 0.75), (0.75, 1.15, 0), TR, material="metal"),          # hood
      part((0.06, 0.6, 0.6), (1.72, 1.1, 0), "darkgrey"),                    # grille
      part((1.92, 0.08, 0.77), (0.75, 1.52, 0), TRIM),                       # hood stripe
      part((0.12, 0.7, 0.12), (1.2, 1.85, 0.2), "black"),                    # exhaust stack
      part((0.9, 0.85, 0.9), (-0.55, 1.0, 0), TR, material="metal"),         # transmission housing
      part((1.2, 0.35, 0.5), (0.9, 0.55, 0), "darkgrey", material="metal"),  # front axle frame
      part((0.5, 0.12, 0.5), (-0.6, 1.5, 0), "black"),                       # seat base
      part((0.42, 0.42, 0.08), (-0.82, 1.75, 0), "black"),                   # seat back
      part((0.08, 0.5, 0.08), (-0.2, 1.75, 0), "black", rotation=[0, 0, 30]),   # steering column
      part((0.04, 0.34, 0.34), (-0.08, 2.0, 0), "black", rotation=[0, 0, 30]),  # wheel
      part((0.08, 1.6, 0.08), (-1.05, 2.05, -0.62), "darkgrey"), part((0.08, 1.6, 0.08), (-1.05, 2.05, 0.62), "darkgrey"),
      part((0.08, 0.08, 1.32), (-1.05, 2.85, 0), "darkgrey"),                # roll bar
      part((1.0, 0.06, 0.4), (-0.55, 1.45, -0.98), TR), part((1.0, 0.06, 0.4), (-0.55, 1.45, 0.98), TR),   # fenders
      part((0.3, 0.3, 0.6), (-1.2, 0.85, 0), "darkgrey", material="metal")]  # drawbar / hitch
for z in (-0.95, 0.95):
    ps += disc(1.65, 0.45, -0.55, 0.82, z, "rubber", n=4)
    ps += disc(0.9, 0.47, -0.55, 0.82, z, TR, n=2)
    ps += disc(0.82, 0.25, 1.25, 0.41, z * 0.78, "rubber", n=2)
    ps += disc(0.4, 0.27, 1.25, 0.41, z * 0.78, TRIM, n=1)
add("Vehicles" if False else "Farm", "Row Crop Tractor", "A faded red row crop tractor with big rear wheels and an open seat under a roll bar. Front at +X. Solid cover.",
    ps, collider=[box((2.9, 1.1, 1.0), (0.2, 1.0, 0)), box((1.7, 1.65, 0.45), (-0.55, 0.82, -0.95)), box((1.7, 1.65, 0.45), (-0.55, 0.82, 0.95)),
                  box((0.9, 0.82, 0.25), (1.25, 0.41, -0.74)), box((0.9, 0.82, 0.25), (1.25, 0.41, 0.74))])

# Hay wagon: flat bed on four wheels, a stack of square bales, a tongue at +X.
ps = [part((4.6, 0.12, 2.2), (0, 1.0, 0), "#8a6a44", material="wood"),
      part((4.4, 0.25, 0.2), (0, 0.8, -0.8), "darkgrey", material="metal"), part((4.4, 0.25, 0.2), (0, 0.8, 0.8), "darkgrey", material="metal"),
      part((1.6, 0.1, 0.12), (3.0, 0.62, 0), "darkgrey", rotation=[0, 0, -8]),
      part((0.1, 0.9, 2.2), (-2.25, 1.5, 0), "#7a5a38", material="wood")]
for x in (-1.6, 1.6):
    for z in (-0.95, 0.95):
        ps += disc(0.75, 0.22, x, 0.38, z, "rubber", n=2)
bale_y = 1.06
for layer, (nx, nz) in enumerate(((4, 4), (4, 4), (3, 4))):
    for i in range(nx):
        for j in range(nz):
            if layer == 2 and (i + j) % 3 == 0:
                continue
            x = -1.65 + i * 1.1 + layer * 0.4; z = -0.81 + j * 0.54
            c = "#c8ac5c" if (i + j + layer) % 2 else "#d2b866"
            ps.append(part((1.08, 0.48, 0.52), (x, bale_y + 0.24 + layer * 0.48, z), c, material="dirt", texScale=2))
add("Farm", "Hay Wagon", "A flatbed hay wagon stacked three bales high, tongue at +X. The bed is a mantle (1.06 m), the bales solid cover.",
    ps, collider=[box((4.6, 0.7, 2.2), (0, 0.71, 0)), box((4.4, 0.96, 2.16), (0, 1.54, 0)), box((3.3, 0.48, 2.16), (0.15, 2.26, 0))])

# Big round bale (lying, axis along X).
ps = disc(1.55, 1.4, 0, 0.78, 0, "#c3a659", axis="x", n=4, material="dirt", texScale=2)
ps += disc(1.2, 1.42, 0, 0.78, 0, "#cdb266", axis="x", n=2, material="dirt", texScale=2)
ps += [part((0.03, 1.58, 1.58), (x, 0.78, 0), "#d9d6c6", rotation=[R(a), 0, 0]) for x in (-0.35, 0.35) for a in (0, 45)][:2]
add("Farm", "Big Round Bale", "A big round hay bale lying on its side (axis along X), net-wrapped. Solid, chest-high cover.",
    ps, collider=[box((1.4, 1.2, 1.5), (0, 0.6, 0)), box((1.4, 0.36, 1.1), (0, 1.38, 0))], scatter={"randomYaw": True, "scale": [0.95, 1.05]})

# Grain bin: corrugated steel cylinder with a cone roof and a ladder.
GAL = "galvanized"
ps = upright(5.6, 5.4, 0, 0, 0, GAL, material="metal")
ps += upright(5.66, 0.1, 0, 1.5, 0, "#8d9293") + upright(5.66, 0.1, 0, 3.0, 0, "#8d9293") + upright(5.66, 0.1, 0, 4.5, 0, "#8d9293")
for k, (d, h) in enumerate(((5.7, 0.55), (4.6, 0.5), (3.4, 0.5), (2.2, 0.45), (1.0, 0.4))):
    ps += upright(d, h, 0, 5.4 + k * 0.5, 0, "#a9adae" if k % 2 else "#b6babb", material="metal")
ps += upright(0.7, 0.35, 0, 7.9, 0, "#8d9293")
ps += [part((0.06, 5.8, 0.06), (x, 2.9, 2.86), "darkgrey") for x in (-0.25, 0.25)]
ps += [part((0.5, 0.04, 0.05), (0, 0.4 + 0.45 * k, 2.86), "darkgrey") for k in range(12)]
ps += [part((1.0, 0.9, 0.06), (1.4, 1.0, 2.55), "#7d8283", material="metal", rotation=[0, -27, 0]),
       part((0.9, 0.5, 0.9), (0, 0.25, 3.2), "#6f7475", material="metal")]   # door, auger hopper
add("Farm", "Grain Bin", "A corrugated steel grain bin 5.6 m across and 8 m tall with a cone roof, a ladder and an auger hopper at +Z.",
    ps, collider=[box((5.4, 6.6, 2.3), (0, 3.3, 0)), box((2.3, 6.6, 5.4), (0, 3.3, 0)), box((3.96, 6.6, 3.96), (0, 3.3, 0)), box((0.9, 0.5, 0.9), (0, 0.25, 3.2))])

# Split rail fence, 3 m along X.
ps = [part((0.16, 1.2, 0.16), (x, 0.6, 0), "#7a6046", material="wood") for x in (-1.45, 1.45)]
ps += [part((3.1, 0.11, 0.09), (0, y, 0), c, material="wood", rotation=[0, 0, r]) for y, c, r in ((0.35, "#8a6e50", 1.5), (0.7, "#7f6447", -1), (1.02, "#8a6e50", 0.5))]
add("Farm", "Split Rail Fence", "A 3 m section of weathered split rail fence (along X), 1.2 m tall: vault it or crouch behind it.",
    ps, collider=[box((3.1, 1.1, 0.16), (0, 0.55, 0))],
    breakable={"health": 90, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.08, 0.6], "speed": 3, "colors": ["#8a6e50", "#7a6046"]}},
    scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3})

# Wheel line irrigator: a 12 m aluminium pipe through three big wheels.
ALU = "#b9bec2"
ps = [part((11.8, 0.13, 0.13), (0, 0.95, 0), ALU, material="metal")]
for x in (-5.4, 0, 5.4):
    for a in (0, 30, 60, 90, 120, 150):
        ps.append(part((0.05, 1.85, 0.05), (x, 0.95, 0), ALU, rotation=[R(a), 0, 0]))
    ps += [part((0.06, 0.06, 0.06), (x, 0.95, 0), "darkgrey")]
    for a in range(0, 180, 30):
        ps.append(part((0.06, 0.5, 0.06), (x, 0.95 + 0.92 * math.cos(math.radians(a)), 0.92 * math.sin(math.radians(a))), ALU, rotation=[R(-a), 0, 0]))
        ps.append(part((0.06, 0.5, 0.06), (x, 0.95 - 0.92 * math.cos(math.radians(a)), -0.92 * math.sin(math.radians(a))), ALU, rotation=[R(-a), 0, 0]))
for x in (-2.7, 2.7):
    ps += [part((0.04, 0.4, 0.04), (x, 1.2, 0), "darkgrey"), part((0.12, 0.08, 0.12), (x, 1.42, 0), "#5c6064")]   # sprinkler risers
add("Farm", "Wheel Line Irrigator", "A 12 m span of wheel line irrigation pipe on three 1.9 m spoked wheels, sprinkler risers on top. Looks only: walk under and through it.",
    ps, collision="cosmetic", fade=200)

# Roadside farm stand: a wooden counter with baskets of corn, pumpkins and a shade roof on posts.
ps = [part((2.4, 0.9, 0.9), (0, 0.45, 0), "#9a7a52", material="wood"),
      part((2.5, 0.06, 1.0), (0, 0.93, 0), "#7a5a38", material="wood")]
ps += [part((0.1, 2.3, 0.1), (x, 1.15, z), "#6e5236", material="wood") for x in (-1.2, 1.2) for z in (-0.45, 0.45)]
ps += [part((2.8, 0.05, 1.4), (0, 2.32, 0), "#3f6a4a", rotation=[12, 0, 0]),
       part((2.8, 0.05, 0.05), (0, 2.2, 0.68), "#e6dcc4")]
for k, x in enumerate((-0.85, -0.3, 0.3, 0.85)):
    ps.append(part((0.45, 0.22, 0.45), (x, 1.07, 0.15), "#8a6a3a", material="wood"))
    if k % 2 == 0:
        ps += [part((0.06, 0.06, 0.32), (x + dx, 1.22, 0.15), "#e0c35a", rotation=[0, R(dx * 200), 0]) for dx in (-0.12, 0, 0.12)]
    else:
        ps += [part((0.18, 0.15, 0.18), (x + dx, 1.25, 0.15 + dz), "#d9752a") for dx, dz in ((-0.1, -0.08), (0.1, 0.07), (0, 0.02))]
ps += [part((0.9, 0.6, 0.04), (0, 0.55, 0.47), "#e6dcc4"), part((0.7, 0.08, 0.02), (0, 0.65, 0.49), "#a33a2c"), part((0.5, 0.06, 0.02), (0, 0.48, 0.49), "#3f6a4a")]
add("Farm", "Roadside Farm Stand", "A roadside produce stand: a wooden counter with baskets of sweet corn and pumpkins under a green shade roof. Front at +Z.",
    ps, collider=[box((2.4, 0.95, 0.9), (0, 0.475, 0))] + [box((0.1, 2.3, 0.1), (x, 1.15, z)) for x in (-1.2, 1.2) for z in (-0.45, 0.45)],
    breakable={"health": 120, "breakBy": "all", "sound": "wood", "debris": {"count": 18, "size": [0.06, 0.4], "speed": 3, "colors": ["#9a7a52", "#d9752a", "#e0c35a"]}})

# Stock tank: a round galvanized water trough.
ps = upright(2.0, 0.62, 0, 0, 0, GAL, material="metal") + upright(2.04, 0.06, 0, 0.6, 0, "#8d9293") + upright(1.86, 0.02, 0, 0.5, 0, "#40606a")
add("Farm", "Stock Tank", "A round galvanized stock tank 2 m across, full of water. Knee-high cover.",
    ps, collider=[box((1.9, 0.66, 0.9), (0, 0.33, 0)), box((0.9, 0.66, 1.9), (0, 0.33, 0)), box((1.45, 0.66, 1.45), (0, 0.33, 0))],
    breakable={"health": 160, "breakBy": "all", "sound": "metal", "debris": {"count": 10, "size": [0.06, 0.25], "speed": 3}})

# Pumpkin pile.
rng = random.Random(9); ps = []
for k in range(11):
    s = rng.uniform(0.26, 0.42); x, z = rng.uniform(-0.6, 0.6), rng.uniform(-0.45, 0.45); y = s * 0.45 + (0.22 if k > 7 else 0)
    c = rng.choice(["#d9752a", "#e0832e", "#c96a22"])
    ps += [part((s, s * 0.85, s * 0.8), (x, y, z), c, rotation=[0, R(a), 0]) for a in (0, 60, 120)]
    ps.append(part((0.04, 0.08, 0.04), (x, y + s * 0.45, z), "#5a6a2a"))
add("Farm", "Pumpkin Pile", "A heap of orange pumpkins on the ground, for farm stands, porches and fields. Looks only.",
    ps, collision="cosmetic", breakable={"health": 10, "breakBy": "all", "sound": "soft", "debris": {"count": 16, "size": [0.05, 0.15], "speed": 3, "colors": ["#d9752a", "#e8c070"]}},
    scatter={"randomYaw": True, "scale": [0.85, 1.15]})

# Farm fuel tank: a cylinder tank on a steel stand with a hose nozzle.
ps = disc(1.15, 2.6, 0, 2.1, 0, "#c9c3b0", axis="x", n=4, material="metal")
ps += [part((0.1, 1.55, 0.1), (x, 0.78, z), "darkgrey", rotation=[0, 0, R(-6 if x < 0 else 6)]) for x in (-1.0, 1.0) for z in (-0.4, 0.4)]
ps += [part((0.06, 0.6, 0.06), (1.3, 1.5, 0.3), "black"), part((0.1, 0.18, 0.08), (1.3, 1.15, 0.3), "darkgrey"),
       part((0.2, 0.2, 0.06), (0, 2.1, 0.6), "#a33a2c")]
add("Farm", "Farm Fuel Tank", "A farm diesel tank (2.6 m along X) up on a steel stand, with a hose and nozzle. Crouch under the tank.",
    ps, collider=[box((2.6, 1.05, 1.06), (0, 2.1, 0))] + [box((0.12, 1.55, 0.12), (x, 0.78, z)) for x in (-1.0, 1.0) for z in (-0.4, 0.4)])

# ===================================================================================== Rail

RAILC = "#5d5753"; TIE = "#4b3a2c"; BALLAST = "#7d7770"
ps = [part((6.0, 0.14, 2.9), (0, 0.07, 0), BALLAST, material="dirt", texScale=3)]
for k in range(10):
    ps.append(part((0.22, 0.12, 2.5), (-2.7 + k * 0.6, 0.16, 0), TIE, material="wood"))
for z in (-0.72, 0.72):
    ps += [part((6.0, 0.12, 0.08), (0, 0.27, z), RAILC, material="metal"), part((6.0, 0.03, 0.06), (0, 0.345, z), "#8e8a86")]
add("Rail", "Railroad Track 6m", "6 m of railroad track along X: rails on timber ties in a bed of ballast (standard gauge). Looks only: walk over it. Line them up 6 m apart.",
    ps, collision="cosmetic", fade=250)

ps = [part((0.14, 3.6, 0.14), (0, 1.8, 0), "lightgrey", material="metal")]
for a in (35, -35):
    ps.append(part((1.3, 0.22, 0.03), (0, 3.15, 0.08), "white", rotation=[0, 0, a]))
    ps.append(part((1.32, 0.03, 0.032), (0, 3.15, 0.08), "black", rotation=[0, 0, a]))
ps += [part((0.5, 0.36, 0.03), (0, 2.45, 0.08), "white"), part((0.3, 0.06, 0.033), (0, 2.45, 0.081), "black"),
       part((0.4, 0.06, 0.4), (0, 0.03, 0), "concrete")]
add("Rail", "Railroad Crossbuck", "The white X-shaped railroad crossing sign on a post (faces +Z), with the number-of-tracks plate under it.",
    ps, collider=[box((0.14, 3.6, 0.14), (0, 1.8, 0))],
    breakable={"health": 110, "breakBy": "all", "sound": "metal", "debris": {"count": 10, "size": [0.05, 0.3], "speed": 3}})


def railcar_base(length, body_y):
    ps = [part((length - 0.6, 0.25, 2.3), (0, body_y - 0.2, 0), "#2a2725", material="metal")]   # underframe
    for x in (-(length / 2 - 1.6), length / 2 - 1.6):
        ps.append(part((2.3, 0.45, 2.0), (x, 0.55, 0), "#2f2c2a", material="metal"))               # truck frame
        for dx in (-0.85, 0.85):
            for z in (-0.72, 0.72):
                ps += disc(0.86, 0.12, x + dx, 0.43, z, "#3a3633", n=2)
    for x in (-length / 2 + 0.1, length / 2 - 0.1):
        ps += [part((0.3, 0.25, 0.35), (x, body_y - 0.2, 0), "#3a3633"), part((0.06, 1.6, 0.06), (x, body_y + 0.8, 1.1), "darkgrey")]
    return ps


# Boxcar: 12 m along X, body from 1.2 to 4.3 m.
BOX = "#7a3a26"; L = 12.0
ps = railcar_base(L, 1.25)
ps += [part((L, 3.0, 2.95), (0, 2.75, 0), BOX, material="metal"), part((L + 0.04, 0.1, 3.0), (0, 4.28, 0), "#5e2d1e")]
for x in [(-5.4 + k * 0.9) for k in range(13)]:
    if abs(x) > 1.6:
        for z in (-1.49, 1.49):
            ps.append(part((0.06, 3.0, 0.04), (x, 2.75, z), "#6a3020"))
for z in (-1.5, 1.5):
    ps += [part((3.0, 2.7, 0.06), (0, 2.65, z * 1.01), "#6e3322", material="metal"), part((3.2, 0.1, 0.1), (0, 4.05, z * 1.02), "darkgrey"),
           part((0.9, 0.3, 0.02), (-4.0, 3.6, z * 1.02), "#e6dcc4"), part((1.6, 0.18, 0.02), (3.8, 1.75, z * 1.02), "#e6dcc4")]
ps += [part((0.5, 0.05, 0.6), (5.6, 4.36, 0), "#4a3a2c")]
add("Rail", "Freight Boxcar", "A rust-red 12 m freight boxcar (along X) on two trucks, doors shut. Solid cover 4.3 m tall; the gap under it is too low to crawl.",
    ps, collider=[box((L, 3.1, 3.0), (0, 2.75, 0)), box((L - 0.6, 1.2, 2.3), (0, 0.6, 0))])

# Open boxcar: both doors slid open, so the middle 3 m is a passage through the car (floor 1.25 m: mantle in).
ps = railcar_base(L, 1.25)
for sx in (-1, 1):
    cx = sx * (L / 2 + 1.5) / 2; w = L / 2 - 1.5
    ps.append(part((w, 3.0, 2.95), (cx, 2.75, 0), "#3e5a6e", material="metal"))
    for z in (-1.5, 1.5):
        ps.append(part((3.0, 2.7, 0.06), (sx * 3.05, 2.65, z * 1.03), "#344e60", material="metal"))   # door slid back over the side
ps += [part((3.0, 0.1, 2.95), (0, 1.3, 0), "#4b3a2c", material="wood"),      # floor
       part((3.0, 0.1, 3.0), (0, 4.25, 0), "#3e5a6e", material="metal"),     # roof over the doorway
       part((L + 0.04, 0.1, 3.0), (0, 4.33, 0), "#2e4252")]
ps += [part((0.5, 0.4, 0.4), (0.6, 1.55, -0.6), "cardboard"), part((0.6, 0.3, 0.4), (-0.7, 1.5, 0.8), "pallet", material="wood")]
add("Rail", "Freight Boxcar Open", "A blue-grey 12 m boxcar with both doors slid open: climb in (1.25 m floor) and through to the other side.",
    ps, collider=[box((4.5, 3.1, 3.0), (-3.75, 2.75, 0)), box((4.5, 3.1, 3.0), (3.75, 2.75, 0)), box((3.0, 0.45, 3.0), (0, 4.1, 0)),
                  box((L - 0.6, 1.35, 2.3), (0, 0.675, 0)), box((3.0, 0.1, 3.0), (0, 1.3, 0))])

# Covered hopper: sloped bins under a rounded roof, 12 m.
HOP = "#9a9c94"
ps = railcar_base(L, 1.25)
ps += [part((L - 1.2, 2.2, 3.0), (0, 3.25, 0), HOP, material="metal"), part((L - 1.4, 0.35, 2.7), (0, 4.5, 0), "#8a8c84", material="metal"),
       part((L - 1.6, 0.12, 0.8), (0, 4.72, 0), "#7a7c74")]
for x in (-3.6, 0, 3.6):
    ps += [part((2.2, 1.0, 2.6), (x, 1.75, 0), HOP, material="metal"), part((1.0, 0.4, 0.8), (x, 1.05, 0), "#6f716a")]
for x in (-4.5, -1.8, 1.8, 4.5):
    for z in (-1.51, 1.51):
        ps.append(part((0.1, 2.2, 0.05), (x, 3.25, z), "#8a8c84"))
ps += [part((1.4, 0.3, 0.02), (-3.5, 3.6, z * 1.52), "#2a3c6a") for z in (-1, 1)]
add("Rail", "Covered Hopper Car", "A grey 12 m covered hopper (along X) for grain: three bins under a round roof with roof hatches. Solid, 4.8 m tall.",
    ps, collider=[box((L - 1.2, 2.5, 3.0), (0, 3.4, 0)), box((L - 0.6, 2.15, 2.6), (0, 1.07, 0))])

# ===================================================================================== Town

WH = "#e8e4d8"; ROOF = "#4a5a6e"
ps = []
ps += upright(5.6, 0.45, 0, 0, 0, "#b8b0a0", material="concrete")                 # base
ps += upright(5.3, 0.06, 0, 0.45, 0, "#9a7a52", material="wood")                  # deck
for k in range(8):
    a = math.radians(22.5 + 45 * k); x, z = 2.45 * math.cos(a), 2.45 * math.sin(a)
    ps.append(part((0.16, 2.6, 0.16), (x, 0.5 + 1.3, z), WH))                        # posts
    if k != 2:   # the south-facing gap (k = 2 sits at +Z) is the way in
        a2 = math.radians(45 * k + 45); x2, z2 = 2.38 * math.cos(math.radians(22.5 + 45 * k + 22.5)), 2.38 * math.sin(math.radians(22.5 + 45 * k + 22.5))
        ps.append(part((1.85, 0.07, 0.07), (x2, 1.45, z2), WH, rotation=[0, R(-math.degrees(math.radians(22.5 + 45 * k + 22.5)) + 90), 0]))
        ps.append(part((1.85, 0.07, 0.07), (x2, 0.65, z2), WH, rotation=[0, R(-math.degrees(math.radians(22.5 + 45 * k + 22.5)) + 90), 0]))
        for s in (-0.6, -0.2, 0.2, 0.6):
            ang = math.radians(22.5 + 45 * k + 22.5); tx, tz = -math.sin(ang), math.cos(ang)
            ps.append(part((0.04, 0.8, 0.04), (x2 + tx * s, 1.05, z2 + tz * s), WH))
ps += upright(6.2, 0.18, 0, 3.1, 0, WH)                                            # eave ring
for k, (d, h) in enumerate(((6.0, 0.35), (4.6, 0.35), (3.2, 0.35), (1.8, 0.35), (0.6, 0.5))):
    ps += upright(d, h, 0, 3.28 + k * 0.35, 0, ROOF if k % 2 == 0 else "#56667a")
ps += [part((0.08, 0.7, 0.08), (0, 5.4, 0), "#c8a040")]
assert len(ps) <= 128, len(ps)
add("Town", "Bandstand Gazebo", "A white octagonal town-square bandstand (5.6 m across) with a railing, an open step in at +Z and a slate-blue roof. Step up onto its deck.",
    ps, collider=[box((5.4, 0.5, 2.3), (0, 0.25, 0)), box((2.3, 0.5, 5.4), (0, 0.25, 0)), box((3.9, 0.5, 3.9), (0, 0.25, 0)),
                  box((0.16, 2.6, 0.16), (2.26, 1.8, 0.94)), box((0.16, 2.6, 0.16), (-2.26, 1.8, 0.94)),
                  box((0.16, 2.6, 0.16), (2.26, 1.8, -0.94)), box((0.16, 2.6, 0.16), (-2.26, 1.8, -0.94)), box((5.6, 1.0, 5.6), (0, 3.6, 0))])

GRAN = "#a8a49a"
ps = [part((2.2, 0.3, 2.2), (0, 0.15, 0), "#8e8a82", material="concrete"), part((1.7, 0.3, 1.7), (0, 0.45, 0), GRAN, material="concrete"),
      part((1.1, 0.9, 1.1), (0, 1.05, 0), GRAN, material="concrete"),
      part((0.8, 0.02, 0.5), (0, 1.1, 0.555), "#3a3a36"), part((0.6, 0.04, 0.02), (0, 1.25, 0.566), "#c8b070"), part((0.5, 0.03, 0.02), (0, 1.0, 0.566), "#c8b070")]
for k, (w, h) in enumerate(((0.8, 1.2), (0.7, 1.2), (0.6, 1.2), (0.5, 1.0))):
    ps.append(part((w, h, w), (0, 1.5 + k * 1.15 + h / 2, 0), GRAN, material="concrete"))
ps += [part((0.5, 0.35, 0.5), (0, 6.25, 0), GRAN, rotation=[0, 45, 0]), part((0.3, 0.3, 0.3), (0, 6.5, 0), GRAN, rotation=[0, 45, 45])]
add("Town", "Memorial Obelisk", "A granite memorial obelisk 6.6 m tall on a stepped plinth, with a dark plaque facing +Z. Solid, unbreakable.",
    ps, collider=[box((2.2, 0.3, 2.2), (0, 0.15, 0)), box((1.7, 0.3, 1.7), (0, 0.45, 0)), box((1.1, 0.9, 1.1), (0, 1.05, 0)), box((0.8, 5.0, 0.8), (0, 4.0, 0))])

# ===================================================================================== write

def main():
    for folder, d in OUT:
        path = os.path.join(REPO, "Props", folder)
        os.makedirs(path, exist_ok=True)
        fn = d["name"].replace(" ", "").replace("(", "").replace(")", "") + ".json"
        with open(os.path.join(path, fn), "w") as f:
            f.write("{\n")
            keys = list(d.keys())
            for i, k in enumerate(keys):
                v = d[k]
                end = "," if i < len(keys) - 1 else ""
                if k == "parts":
                    f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(p) for p in v) + "\n  ]" + end + "\n")
                else:
                    f.write(f"  {json.dumps(k)}: {json.dumps(v)}{end}\n")
            f.write("}\n")
    print(f"wrote {len(OUT)} props: " + ", ".join(f"{fo}/{d['name']} ({len(d['parts'])})" for fo, d in OUT))


if __name__ == "__main__":
    main()
