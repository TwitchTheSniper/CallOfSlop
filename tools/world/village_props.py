#!/usr/bin/env python3
"""Writes Props/Countryside/*.json: the English-village props made for the World Tour pack's 07 Village (and anyone else).

Usage: python3 tools/world/village_props.py [repo root]

Dry-stone walls and gate piers, a five-bar field gate, a memorial cross (no names on it), a lychgate, a fingerpost,
a timber bus shelter, a village noticeboard, a yellow grit bin, a beer-garden parasol, a hanging flower basket and the
allotment set (bean canes, a water butt, a cold frame, a scarecrow). Every prop faces +Z and stands on y = 0 (see
Props/README.md). Run from anywhere; it rewrites the folder's files (and nothing else).
"""
import json
import math
import os
import random
import sys

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(REPO, "Props", "Countryside")
os.makedirs(OUT, exist_ok=True)

STONE = ["#b89c66", "#c2a670", "#a98d5a", "#b4975f", "#c8ad78", "#9f8454"]   # Cotswold limestone, weathered honey
STONE_DARK = "#8a744c"
OAK = "#5a4026"
OAK_DARK = "#463220"


def R(v):
    return round(v, 4)


def part(size, offset=None, color="grey", material=None, rotation=None):
    p = {"size": [R(v) for v in size]}
    if offset is not None:
        p["offset"] = [R(v) for v in offset]
    if rotation is not None and any(rotation):
        p["rotation"] = [R(v) for v in rotation]
    p["color"] = color
    if material:
        p["material"] = material
    return p


def box(x0, y0, z0, x1, y1, z1, color, material=None, rotation=None):
    """A part from corner to corner."""
    return part((x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), color, material, rotation)


def col(size, offset=None):
    c = {"size": [R(v) for v in size]}
    if offset is not None:
        c["offset"] = [R(v) for v in offset]
    return c


def write(name, fname, desc, parts, collision="solid", collider=None, scatter=None, breakable=None, fade=None):
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
    assert len(parts) <= 128, (name, len(parts))
    with open(os.path.join(OUT, fname), "w") as f:
        f.write("{\n")
        keys = list(d.keys())
        for i, k in enumerate(keys):
            v = d[k]
            sep = "," if i < len(keys) - 1 else ""
            if k == "parts":
                f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(p) for p in v) + "\n  ]" + sep + "\n")
            else:
                f.write(f"  {json.dumps(k)}: {json.dumps(v)}{sep}\n")
        f.write("}\n")
    return d


# ------------------------------------------------------------------------------------------------ dry-stone walls


def drystone(length, seed):
    """A dry-stone wall `length` metres long (along X), 1.1 m tall: a battered body of coursed stone, a few proud
    stones, and a row of upright coping stones ("cock and hen") along the top."""
    rnd = random.Random(seed)
    L = length
    parts = [
        box(-L / 2, 0.0, -0.29, L / 2, 0.36, 0.29, STONE[0], "brick"),     # footing course, widest
        box(-L / 2, 0.36, -0.25, L / 2, 0.68, 0.25, STONE[1], "brick"),
        box(-L / 2, 0.68, -0.21, L / 2, 0.9, 0.21, STONE[2], "brick"),     # battered: narrower towards the top
        box(-L / 2, 0.9, -0.2, L / 2, 0.93, 0.2, STONE_DARK),              # through-stones under the coping
    ]
    # proud stones on both faces (a little lighter or darker), so the faces aren't flat
    for k in range(int(L * 3.5)):
        x = rnd.uniform(-L / 2 + 0.12, L / 2 - 0.12)
        y = rnd.choice((0.12, 0.25, 0.47, 0.58, 0.79))
        w, h = rnd.uniform(0.18, 0.34), rnd.uniform(0.08, 0.13)
        face = rnd.choice((-1, 1))
        depth = 0.29 if y < 0.36 else 0.25 if y < 0.68 else 0.21
        parts.append(part((w, h, 0.04), (x, y, face * (depth + 0.012)), rnd.choice(STONE)))
    # coping: upright slabs, alternately taller and shorter, leaning a touch
    n = int(round(L / 0.16))
    step = L / n
    for k in range(n):
        x = -L / 2 + step * (k + 0.5)
        h = 0.17 if k % 2 == 0 else 0.13
        h += rnd.uniform(-0.015, 0.015)
        parts.append(part((step * 0.86, h, 0.36), (x, 0.93 + h / 2, 0.0), rnd.choice(STONE), None, (0, 0, rnd.uniform(-6, 6))))
    return parts


write("Drystone Wall 2m", "DrystoneWall2m.json",
      "A 2 m length of Cotswold dry-stone wall, 1.1 m tall, with upright coping stones. Waist-high cover; unbreakable.",
      drystone(2.0, 11), collider=[col((2.0, 1.1, 0.56))],
      scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 1, "scale": [1.0, 1.0]})
write("Drystone Wall 1m", "DrystoneWall1m.json",
      "A 1 m length of dry-stone wall (to finish a run or fill a gap), 1.1 m tall. Unbreakable.",
      drystone(1.0, 12), collider=[col((1.0, 1.1, 0.56))],
      scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 1, "scale": [1.0, 1.0]})

# ------------------------------------------------------------------------------------------------ gate pier and gate

pier = [
    box(-0.32, 0.0, -0.32, 0.32, 1.25, 0.32, STONE[1], "brick"),
    box(-0.36, 1.25, -0.36, 0.36, 1.36, 0.36, STONE[4]),                  # cap stone
    box(-0.26, 1.36, -0.26, 0.26, 1.42, 0.26, STONE[0]),
    part((0.24, 0.22, 0.24), (0, 1.53, 0), STONE[4]),                     # ball finial (an octagon of boxes)
    part((0.24, 0.22, 0.24), (0, 1.53, 0), STONE[4], None, (0, 45, 0)),
    part((0.3, 0.12, 0.3), (0, 1.53, 0), STONE[4], None, (0, 22.5, 0)),
    box(-0.33, 0.0, -0.33, 0.33, 0.12, 0.33, STONE_DARK),                  # plinth course
    box(-0.322, 0.62, -0.322, 0.322, 0.66, 0.322, STONE_DARK),             # a darker bed joint
]
write("Stone Gate Pier", "StoneGatePier.json",
      "A square limestone gate pier with a cap and a ball finial, for drive and field gateways. Unbreakable.",
      pier, collider=[col((0.66, 1.42, 0.66))], scatter={"randomYaw": False, "yawStep": 90})

gate = []
GW = 3.0
for x in (-GW / 2 + 0.06, GW / 2 - 0.06):
    gate.append(box(x - 0.06, 0.0, -0.06, x + 0.06, 1.2, 0.06, "#8a7a62", "wood"))       # hanging and shutting stiles
for k in range(5):
    y = 0.22 + k * 0.215
    gate.append(box(-GW / 2 + 0.1, y - 0.04, -0.03, GW / 2 - 0.1, y + 0.04, 0.03, "#9a8a70", "wood"))   # five bars
L = math.hypot(GW - 0.3, 0.86)
ang = math.degrees(math.atan2(0.86, GW - 0.3))
gate.append(part((L, 0.07, 0.05), (0.0, 0.65, 0.045), "#8a7a62", "wood", (0, 0, ang)))   # the diagonal brace
gate.append(part((0.06, 0.95, 0.05), (0.0, 0.65, 0.045), "#8a7a62", "wood"))               # middle upright
gate.append(box(-GW / 2 + 0.02, 0.98, 0.06, -GW / 2 + 0.12, 1.08, 0.1, "#2a2a2a"))         # hinge straps and latch
gate.append(box(-GW / 2 + 0.02, 0.28, 0.06, -GW / 2 + 0.12, 0.38, 0.1, "#2a2a2a"))
gate.append(box(GW / 2 - 0.16, 0.9, 0.06, GW / 2 - 0.04, 0.96, 0.12, "#2a2a2a"))
write("Five Bar Gate", "FiveBarGate.json",
      "A 3 m wooden five-bar field gate with a diagonal brace. You can climb over it; it splinters if shot to bits.",
      gate, collider=[col((GW, 1.2, 0.14))], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 2},
      breakable={"health": 110, "breakBy": "all", "sound": "wood", "debris": {"count": 18, "size": [0.05, 0.6], "speed": 3.5}})

# ------------------------------------------------------------------------------------------------ memorial cross

MEM = "#c9bb98"      # pale ashlar
MEM_D = "#b3a47f"
BRONZE = "#4a4a36"
mem = [
    box(-1.35, 0.0, -1.35, 1.35, 0.28, 1.35, MEM_D, "concrete"),          # three steps
    box(-1.05, 0.28, -1.05, 1.05, 0.56, 1.05, MEM, "concrete"),
    box(-0.75, 0.56, -0.75, 0.75, 0.84, 0.75, MEM_D, "concrete"),
    box(-0.5, 0.84, -0.5, 0.5, 2.2, 0.5, MEM, "concrete"),                # the plinth (die)
    box(-0.56, 2.2, -0.56, 0.56, 2.34, 0.56, MEM_D),                       # cornice
    box(-0.44, 2.34, -0.44, 0.44, 2.5, 0.44, MEM),
    box(-0.16, 2.5, -0.16, 0.16, 5.3, 0.16, MEM),                          # the shaft
    box(-0.5, 4.55, -0.13, 0.5, 4.85, 0.13, MEM),                          # cross arms
    box(-0.12, 5.3, -0.12, 0.12, 5.4, 0.12, MEM_D),
    box(-0.2, 2.5, -0.2, 0.2, 2.75, 0.2, MEM_D),                           # shaft base block
]
for sx, sz, w, d in ((0, 1, 0.66, 0.02), (0, -1, 0.66, 0.02), (1, 0, 0.02, 0.66), (-1, 0, 0.02, 0.66)):
    # bronze plaques (blank: no names), one on each face of the die
    mem.append(part((w, 0.8, d), (sx * 0.51, 1.55, sz * 0.51), BRONZE, "metal"))
    mem.append(part((w * 0.82 if w > 0.1 else 0.024, 0.05, d * 0.82 if d > 0.1 else 0.024), (sx * 0.52, 1.75, sz * 0.52), "#8a8460"))
# a poppy wreath leaning on the front step (a ring of red boxes, black centres)
for k in range(10):
    a = math.radians(k * 36)
    mem.append(part((0.13, 0.13, 0.05), (math.cos(a) * 0.24, 1.08 + math.sin(a) * 0.24, 0.79), "#b0161a", None, (0, 0, k * 36)))
mem.append(part((0.12, 0.12, 0.03), (0.0, 0.84 + 0.2, 0.83), "#2a3a1c"))
mem.append(part((0.1, 0.42, 0.03), (0.0, 0.84 + 0.0, 0.79), "#2a4a22"))
write("Memorial Cross", "MemorialCross.json",
      "A village memorial: a stone cross on a stepped plinth with blank bronze plaques and a poppy wreath. Unbreakable cover.",
      mem, collider=[col((2.7, 0.28, 2.7)), col((2.1, 0.56, 2.1)), col((1.5, 0.84, 1.5)), col((1.0, 2.5, 1.0)), col((0.32, 5.4, 0.32))],
      scatter={"randomYaw": False, "yawStep": 90})

# ------------------------------------------------------------------------------------------------ lychgate

ly = []
LW, LD = 3.2, 2.6          # across (X) and through (Z)
OPEN = 1.8                 # the clear walk-through width
side_x = OPEN / 2 + 0.27   # centre of each side (a dwarf wall with two posts on it)
for sx in (-1, 1):
    x = sx * side_x
    ly.append(box(x - 0.27, 0.0, -LD / 2 + 0.15, x + 0.27, 0.55, LD / 2 - 0.15, STONE[0], "brick"))   # dwarf wall
    ly.append(box(x - 0.29, 0.55, -LD / 2 + 0.13, x + 0.29, 0.62, LD / 2 - 0.13, STONE[4]))           # its coping
    for z in (-LD / 2 + 0.3, LD / 2 - 0.3):
        ly.append(box(x - 0.09, 0.62, z - 0.09, x + 0.09, 2.45, z + 0.09, OAK, "wood"))              # oak posts
    ly.append(box(x - 0.1, 2.45, -LD / 2 + 0.1, x + 0.1, 2.62, LD / 2 - 0.1, OAK_DARK, "wood"))       # wall plate
    ly.append(box(x - 0.04, 0.62, -0.5, x + 0.04, 2.45, 0.5, OAK, "wood"))                           # middle post
    ly.append(box(x - 0.2, 0.62, -0.62, x + 0.2, 0.9, 0.62, OAK_DARK, "wood"))                       # bench seat on the wall
# tie beams across the opening and a king post under the ridge
for z in (-LD / 2 + 0.3, LD / 2 - 0.3):
    ly.append(box(-side_x - 0.1, 2.45, z - 0.07, side_x + 0.1, 2.6, z + 0.07, OAK_DARK, "wood"))
    ly.append(box(-0.06, 2.6, z - 0.06, 0.06, 3.45, z + 0.06, OAK, "wood"))
    for sx in (-1, 1):   # arched braces (two short tipped pieces)
        ly.append(part((0.6, 0.08, 0.08), (sx * (side_x - 0.36), 2.3, z), OAK, "wood", (0, 0, sx * -38)))
# ridge beam and the pitched roof (ridge along Z), clay tiles in steps
ly.append(box(-0.07, 3.38, -LD / 2 - 0.25, 0.07, 3.52, LD / 2 + 0.25, OAK_DARK, "wood"))
half = LW / 2 + 0.25
pitch = math.degrees(math.atan2(3.5 - 2.55, half))
slope = math.hypot(half, 0.95)
for sx in (-1, 1):
    ly.append(part((slope, 0.1, LD + 0.6), (sx * half / 2, 3.03, 0.0), "#7a4a36", "tiles", (0, 0, -sx * pitch)))
    ly.append(part((slope, 0.04, LD + 0.62), (sx * half / 2, 2.97, 0.0), OAK_DARK, None, (0, 0, -sx * pitch)))   # boarding under
ly.append(box(-0.14, 3.48, -LD / 2 - 0.3, 0.14, 3.58, LD / 2 + 0.3, "#6a3e2e", "tiles"))                       # ridge tiles
write("Lychgate", "Lychgate.json",
      "A churchyard lychgate: oak posts on two low stone walls with seats, under a steep tiled roof. Walk through it along Z (1.8 m clear). Unbreakable.",
      ly, collider=[col((0.58, 2.62, LD - 0.26), (-side_x, 1.31, 0)), col((0.58, 2.62, LD - 0.26), (side_x, 1.31, 0)),
                    col((LW + 0.4, 0.5, LD + 0.4), (0, 2.95, 0))],
      scatter={"randomYaw": False, "yawStep": 90})

# ------------------------------------------------------------------------------------------------ fingerpost

WHITE = "#ecebe4"
BLACK = "#1e1e1e"
fp = [
    box(-0.07, 0.0, -0.07, 0.07, 2.75, 0.07, WHITE, "metal"),
    box(-0.1, 0.0, -0.1, 0.1, 0.25, 0.1, WHITE),                    # base collar
    box(-0.09, 2.75, -0.09, 0.09, 2.82, 0.09, BLACK),                # cap and finial ball
    part((0.12, 0.12, 0.12), (0, 2.9, 0), WHITE),
    part((0.12, 0.12, 0.12), (0, 2.9, 0), WHITE, None, (0, 45, 0)),
]
for (y, yaw, L) in ((2.45, 0, 1.0), (2.2, 120, 0.85), (1.95, 235, 0.95)):
    # an arm: a white board with a black border strip and black "lettering" bars (no words), a pointed end
    a = math.radians(yaw)
    dx, dz = math.cos(a), -math.sin(a)                # +X of a prop turned by yaw
    cx, cz = dx * (L / 2 + 0.07), dz * (L / 2 + 0.07)
    fp.append(part((L, 0.18, 0.035), (cx, y, cz), WHITE, None, (0, yaw, 0)))
    fp.append(part((L - 0.04, 0.02, 0.04), (cx, y + 0.07, cz), BLACK, None, (0, yaw, 0)))
    fp.append(part((L - 0.04, 0.02, 0.04), (cx, y - 0.07, cz), BLACK, None, (0, yaw, 0)))
    for k in range(3):
        o = 0.18 + k * (L - 0.45) / 3
        lx, lz = dx * (0.07 + o), dz * (0.07 + o)
        fp.append(part((0.16 + 0.04 * (k % 2), 0.045, 0.042), (lx + dx * 0.08, y, lz + dz * 0.08), BLACK, None, (0, yaw, 0)))
    tx, tz = dx * (L + 0.07), dz * (L + 0.07)
    fp.append(part((0.13, 0.13, 0.034), (tx, y, tz), WHITE, None, (yaw and 0, yaw, 45)))   # the pointed tip (a turned square)
write("Fingerpost Sign", "FingerpostSign.json",
      "A white cast-iron fingerpost at a lane junction: three pointing arms with black borders and blank lettering bars.",
      fp, collider=[col((0.2, 2.9, 0.2))], scatter={"randomYaw": True},
      breakable={"health": 150, "breakBy": ["bullets", "explosions", "streaks"], "sound": "metal", "debris": {"count": 10, "size": [0.04, 0.4], "speed": 3}})

# ------------------------------------------------------------------------------------------------ timber bus shelter

bs = []
BW, BD, BH = 3.2, 1.7, 2.2
TIM = "#6e5234"
TIM_L = "#82623e"
# low stone plinth all round the back and the sides, then boarded walls on a frame
bs.append(box(-BW / 2, 0.0, -BD / 2, BW / 2, 0.45, -BD / 2 + 0.22, STONE[1], "brick"))
for sx in (-1, 1):
    bs.append(box(sx * BW / 2 - 0.11, 0.0, -BD / 2, sx * BW / 2 + 0.11, 0.45, BD / 2 - 0.25, STONE[1], "brick"))
for k in range(9):        # vertical boards on the back wall
    x = -BW / 2 + 0.18 + k * (BW - 0.36) / 8
    bs.append(box(x - 0.17, 0.45, -BD / 2 + 0.04, x + 0.17, BH, -BD / 2 + 0.12, TIM if k % 2 else TIM_L, "wood"))
for sx in (-1, 1):        # side walls (boarded to 2 m, with an open window band at the front end)
    bs.append(box(sx * BW / 2 - 0.05, 0.45, -BD / 2 + 0.04, sx * BW / 2 + 0.05, BH, BD / 2 - 0.65, TIM, "wood"))
    bs.append(box(sx * BW / 2 - 0.07, 0.0, BD / 2 - 0.3, sx * BW / 2 + 0.07, BH, BD / 2 - 0.16, OAK, "wood"))    # front corner post
    bs.append(box(sx * BW / 2 - 0.06, 0.45, BD / 2 - 0.65, sx * BW / 2 + 0.06, 1.05, BD / 2 - 0.3, TIM_L, "wood"))
bs.append(box(-BW / 2, BH - 0.12, BD / 2 - 0.3, BW / 2, BH, BD / 2 - 0.14, OAK_DARK, "wood"))                  # front beam
# the bench along the back
bs.append(box(-BW / 2 + 0.25, 0.45, -BD / 2 + 0.14, BW / 2 - 0.25, 0.5, -BD / 2 + 0.55, TIM_L, "wood"))
for x in (-1.0, 0.0, 1.0):
    bs.append(box(x - 0.05, 0.0, -BD / 2 + 0.3, x + 0.05, 0.45, -BD / 2 + 0.4, OAK_DARK, "wood"))
# a timetable case on the inside of the east wall (blank sheets)
bs.append(box(BW / 2 - 0.12, 1.2, -0.25, BW / 2 - 0.06, 1.75, 0.2, "#2a2a2a"))
bs.append(box(BW / 2 - 0.13, 1.26, -0.2, BW / 2 - 0.12, 1.69, 0.15, "#f0ece0"))
# a mono-pitch tiled roof (falling to the back)
rw = BW + 0.5
rl = BD + 0.6
pitch = 14
bs.append(part((rw, 0.09, rl), (0.0, BH + 0.18, 0.0), "#5a3e30", "tiles", (pitch, 0, 0)))
bs.append(part((rw + 0.02, 0.05, rl + 0.02), (0.0, BH + 0.11, 0.0), OAK_DARK, None, (pitch, 0, 0)))
bs.append(box(-rw / 2, BH - 0.04, BD / 2 + 0.05, rw / 2, BH + 0.32, BD / 2 + 0.12, OAK_DARK, "wood"))           # fascia
write("Timber Bus Shelter", "TimberBusShelter.json",
      "A village bus shelter: boarded timber walls on a stone plinth, a bench along the back and a tiled roof (open side faces +Z). Unbreakable.",
      bs, collider=[col((BW, BH, 0.16), (0, BH / 2, -BD / 2 + 0.08)), col((0.22, BH, BD - 0.16), (-BW / 2, BH / 2, 0.08)),
                    col((0.22, BH, BD - 0.16), (BW / 2, BH / 2, 0.08)), col((BW + 0.4, 0.3, BD + 0.4), (0, BH + 0.17, 0)),
                    col((BW - 0.5, 0.5, 0.42), (0, 0.25, -BD / 2 + 0.35))],
      scatter={"randomYaw": False, "yawStep": 90})

# ------------------------------------------------------------------------------------------------ noticeboard

nb = [
    box(-0.75, 0.0, -0.05, -0.65, 2.05, 0.05, OAK, "wood"),
    box(0.65, 0.0, -0.05, 0.75, 2.05, 0.05, OAK, "wood"),
    box(-0.68, 0.95, -0.04, 0.68, 1.85, 0.04, OAK_DARK, "wood"),            # the board
    box(-0.62, 1.0, 0.04, 0.62, 1.8, 0.05, "#5a6a3a"),                       # green baize
    part((1.75, 0.06, 0.4), (0.0, 2.1, 0.02), "#5a3e30", "tiles", (14, 0, 0)),   # little roof
]
rnd = random.Random(5)
for k, (x, y, w, h) in enumerate(((-0.4, 1.55, 0.3, 0.38), (-0.02, 1.6, 0.28, 0.22), (0.36, 1.5, 0.3, 0.42), (-0.35, 1.15, 0.34, 0.24),
                                  (0.08, 1.24, 0.22, 0.3), (0.4, 1.12, 0.22, 0.16))):
    nb.append(part((w, h, 0.006), (x, y, 0.054), rnd.choice(["#f2eee2", "#f2d870", "#e8b0b0", "#b8d0e8", "#f2eee2"])))
    nb.append(part((w * 0.7, 0.02, 0.004), (x, y + h * 0.3, 0.059), "#3a3a3a"))
write("Village Noticeboard", "VillageNoticeboard.json",
      "A parish noticeboard: green baize under a little tiled roof, pinned with notices (no readable text).",
      nb, collider=[col((1.5, 2.15, 0.12))], scatter={"randomYaw": False, "yawStep": 90},
      breakable={"health": 70, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.04, 0.35], "speed": 3}})

# ------------------------------------------------------------------------------------------------ grit bin

YEL = "#e0b81e"
gb = [
    box(-0.45, 0.0, -0.32, 0.45, 0.62, 0.32, YEL, None),
    box(-0.43, 0.04, -0.34, 0.43, 0.6, -0.32, "#c9a418"),
    part((0.96, 0.1, 0.72), (0.0, 0.69, 0.02), YEL, None, (8, 0, 0)),     # sloping lid
    box(-0.3, 0.6, 0.33, 0.3, 0.66, 0.37, "#c9a418"),                       # handle lip
    box(-0.47, 0.0, -0.34, 0.47, 0.06, 0.34, "#a88a14"),
    part((0.42, 0.12, 0.004), (0.0, 0.42, 0.322), "#2a2a2a"),               # a dark label panel (no text)
]
write("Grit Bin", "GritBin.json",
      "A yellow plastic road-grit bin with a sloping lid, as found at the top of every icy lane.",
      gb, collider=[col((0.94, 0.74, 0.72))], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 6},
      breakable={"health": 45, "breakBy": "all", "sound": "soft", "debris": {"count": 12, "size": [0.04, 0.3], "speed": 3, "colors": [YEL, "#8a8478", "#a09a8c"]}})

# ------------------------------------------------------------------------------------------------ beer-garden parasol

par = [
    box(-0.03, 0.0, -0.03, 0.03, 2.35, 0.03, "#d8d4c8", "metal"),
    box(-0.25, 0.0, -0.25, 0.25, 0.06, 0.25, "#3a3a3a"),
]
for k in range(8):   # eight canopy panels sloping down from the crown, alternately green and cream
    a = k * 45 + 22.5
    rr = 1.05
    x, z = math.cos(math.radians(a)) * rr * 0.5, -math.sin(math.radians(a)) * rr * 0.5
    par.append(part((rr * 1.03, 0.035, 0.86), (x, 2.22, z), "#2f5a3a" if k % 2 else "#e8e2cc", None, (0, a, -14)))
par.append(part((0.14, 0.12, 0.14), (0, 2.42, 0), "#2f5a3a"))
for k in range(8):   # the valance round the rim
    a = k * 45
    x, z = math.cos(math.radians(a)) * 1.0, -math.sin(math.radians(a)) * 1.0
    par.append(part((0.8, 0.16, 0.02), (x, 1.97, z), "#e8e2cc" if k % 2 else "#2f5a3a", None, (0, a + 90, 0)))
write("Beer Garden Parasol", "BeerGardenParasol.json",
      "A big green-and-cream pub garden parasol on a pole (stands through a picnic table). Looks only.",
      par, collision="cosmetic", scatter={"randomYaw": True}, fade=90)

# ------------------------------------------------------------------------------------------------ hanging flower basket

FLOWERS = ["#c8287a", "#e84a3a", "#f0d040", "#ffffff", "#8a4ac8", "#e87ab0"]
hb = [
    box(-0.03, 0.0, -0.02, 0.03, 0.35, 0.02, "#1e1e1e"),                    # wall plate
    box(-0.02, 0.3, 0.0, 0.02, 0.34, 0.5, "#1e1e1e"),                       # bracket arm
    part((0.02, 0.36, 0.02), (0.0, 0.17, 0.25), "#1e1e1e", None, (-55, 0, 0)),   # stay
    box(-0.005, 0.12, 0.43, 0.005, 0.3, 0.45, "#1e1e1e"),                    # chain
    part((0.42, 0.2, 0.42), (0.0, 0.03, 0.44), "#3a4a22"),                    # the moss basket
    part((0.42, 0.2, 0.42), (0.0, 0.03, 0.44), "#3a4a22", None, (0, 45, 0)),
    part((0.5, 0.16, 0.5), (0.0, 0.13, 0.44), "#3f6a2a"),                     # trailing foliage
    part((0.5, 0.16, 0.5), (0.0, 0.13, 0.44), "#4a7a30", None, (0, 45, 0)),
]
rnd = random.Random(9)
for k in range(14):
    a = rnd.uniform(0, math.tau)
    r = rnd.uniform(0.05, 0.27)
    hb.append(part((0.07, 0.06, 0.07), (math.cos(a) * r, rnd.uniform(0.0, 0.24), 0.44 + math.sin(a) * r), rnd.choice(FLOWERS)))
for k in range(5):   # trailing stems
    a = k * 72 + 20
    x, z = math.cos(math.radians(a)) * 0.24, 0.44 + math.sin(math.radians(a)) * 0.24
    hb.append(part((0.05, 0.22, 0.05), (x, -0.08, z), "#3f6a2a"))
# the prop stands on y = 0 at its lowest point: lift everything so the trailing stems end at y = 0
low = min(p["offset"][1] - p["size"][1] / 2 for p in hb)
for p in hb:
    p["offset"][1] = R(p["offset"][1] - low)
write("Hanging Flower Basket", "HangingFlowerBasket.json",
      "A hanging basket of trailing flowers on a black wall bracket. Wall-mounted: back at -Z, lift it with the Prop op's y (about 1.9 m).",
      hb, collision="cosmetic", scatter={"randomYaw": False, "yawStep": 90}, fade=55)

# ------------------------------------------------------------------------------------------------ allotment set

CANE = "#b89a62"
bc = []
for k in range(6):   # an A-frame row of canes, tied at the top, with runner-bean leaves climbing them
    x = -1.0 + k * 0.4
    for sz in (-1, 1):
        bc.append(part((0.025, 2.05, 0.025), (x, 1.0, sz * 0.22), CANE, None, (sz * 12, 0, 0)))
bc.append(box(-1.15, 1.92, -0.02, 1.15, 1.95, 0.02, CANE))   # the cross cane
rnd = random.Random(21)
for k in range(30):
    x = rnd.uniform(-1.05, 1.05)
    y = rnd.uniform(0.25, 1.85)
    zz = (0.4 - y * 0.18) * rnd.choice((-1, 1))
    bc.append(part((rnd.uniform(0.16, 0.26), rnd.uniform(0.14, 0.22), 0.04), (x, y, zz), rnd.choice(["#3f7a2a", "#4a8a30", "#356a24"]), None, (0, rnd.uniform(-20, 20), rnd.uniform(-25, 25))))
for k in range(6):
    bc.append(part((0.05, 0.05, 0.05), (rnd.uniform(-1.0, 1.0), rnd.uniform(0.6, 1.6), rnd.choice((-0.3, 0.3))), "#d83a2a"))   # scarlet flowers
bc.append(box(-1.25, 0.0, -0.45, 1.25, 0.04, 0.45, "#5a4630"))   # the bed's dug earth
write("Bean Canes", "BeanCanes.json",
      "An allotment row of runner beans climbing an A-frame of bamboo canes, 2.5 m long. Looks only (concealment, not cover).",
      bc, collision="cosmetic", scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, fade=60)

wb = [
    box(-0.32, 0.0, -0.22, -0.12, 0.3, 0.22, "#8a4a36", "brick"),            # brick stand
    box(0.12, 0.0, -0.22, 0.32, 0.3, 0.22, "#8a4a36", "brick"),
]
for a in (0, 45, 90, 135):
    wb.append(part((0.62, 0.92, 0.257), (0.0, 0.76, 0.0), "#2f4a2a", None, (0, a, 0)))
for a in (0, 45, 90, 135):
    wb.append(part((0.66, 0.05, 0.273), (0.0, 1.24, 0.0), "#283e24", None, (0, a, 0)))
wb.append(box(-0.04, 0.42, 0.26, 0.04, 0.5, 0.36, "#202020"))               # tap
wb.append(box(-0.015, 1.0, -0.3, 0.015, 1.9, -0.27, "#3a3a3a"))             # downpipe stub (from a shed gutter)
write("Water Butt", "WaterButt.json",
      "A green plastic water butt on a brick stand, with a tap, for sheds and allotments.",
      wb, collider=[col((0.66, 1.27, 0.66))], scatter={"randomYaw": True},
      breakable={"health": 45, "breakBy": "all", "sound": "soft", "debris": {"count": 12, "size": [0.05, 0.3], "speed": 3}})

cf = [
    box(-0.62, 0.0, -0.42, 0.62, 0.42, -0.36, "#7a5e3e", "wood"),     # back board (taller)
    box(-0.62, 0.0, 0.36, 0.62, 0.24, 0.42, "#7a5e3e", "wood"),       # front board (lower)
    box(-0.62, 0.0, -0.42, -0.56, 0.4, 0.42, "#7a5e3e", "wood"),
    box(0.56, 0.0, -0.42, 0.62, 0.4, 0.42, "#7a5e3e", "wood"),
    part((1.22, 0.02, 0.88), (0.0, 0.34, 0.0), "#a8c8c8", None, (-13, 0, 0)),     # glass lights on the slope
    part((0.03, 0.04, 0.88), (0.0, 0.355, 0.0), "#6a4e30", None, (-13, 0, 0)),
    box(-0.55, 0.0, -0.36, 0.55, 0.1, 0.36, "#4a3a28"),                            # soil
]
for k in range(5):
    cf.append(part((0.14, 0.1, 0.14), (-0.4 + k * 0.2, 0.15, (k % 2) * 0.2 - 0.1), "#4a8a30"))   # seedlings
write("Cold Frame", "ColdFrame.json",
      "An allotment cold frame: a low wooden box of seedlings under a sloping glass lid. Low enough to step onto.",
      cf, collider=[col((1.24, 0.42, 0.84))], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 5},
      breakable={"health": 20, "breakBy": "all", "sound": "glass", "debris": {"count": 14, "size": [0.04, 0.25], "speed": 3}})

sc = [
    box(-0.04, 0.0, -0.04, 0.04, 1.75, 0.04, "#6a4e30", "wood"),           # post
    box(-0.7, 1.28, -0.035, 0.7, 1.35, 0.035, "#6a4e30", "wood"),           # cross-piece (arms)
    box(-0.24, 0.85, -0.13, 0.24, 1.42, 0.13, "#5a6a8a"),                   # an old coat
    box(-0.62, 1.2, -0.1, -0.24, 1.38, 0.1, "#5a6a8a"),                     # sleeves
    box(0.24, 1.2, -0.1, 0.62, 1.38, 0.1, "#5a6a8a"),
    box(-0.16, 0.5, -0.1, -0.03, 0.88, 0.1, "#6a5a40"),                     # trouser legs
    box(0.03, 0.5, -0.1, 0.16, 0.88, 0.1, "#6a5a40"),
    part((0.26, 0.26, 0.24), (0.0, 1.56, 0.0), "#d8c890"),                  # sacking head
    box(-0.25, 1.68, -0.24, 0.25, 1.71, 0.24, "#6a5a3a"),                   # hat brim
    box(-0.14, 1.71, -0.13, 0.14, 1.86, 0.13, "#6a5a3a"),
    box(-0.7, 1.1, -0.07, -0.6, 1.2, 0.07, "#d8c890"),                      # straw tufts at the cuffs
    box(0.6, 1.1, -0.07, 0.7, 1.2, 0.07, "#d8c890"),
    box(-0.05, 1.5, 0.12, 0.05, 1.56, 0.13, "#2a2a2a"),                     # stitched face
    box(-0.08, 1.6, 0.12, -0.04, 1.64, 0.13, "#2a2a2a"),
    box(0.04, 1.6, 0.12, 0.08, 1.64, 0.13, "#2a2a2a"),
]
write("Allotment Scarecrow", "AllotmentScarecrow.json",
      "A scarecrow in an old blue coat and hat, on a post among the vegetable beds.",
      sc, collider=[col((0.1, 1.86, 0.1))], scatter={"randomYaw": True},
      breakable={"health": 40, "breakBy": "all", "sound": "soft", "debris": {"count": 12, "size": [0.04, 0.3], "speed": 3, "colors": ["#d8c890", "#5a6a8a", "#6a4e30"]}})

# ------------------------------------------------------------------------------------------------ churchyard memorials

GRAVE = "#a8a290"
GRAVE_D = "#8a8676"
hs = [
    box(-0.32, 0.0, -0.07, 0.32, 0.82, 0.07, GRAVE, "concrete"),
    box(-0.24, 0.82, -0.07, 0.24, 0.9, 0.07, GRAVE),                 # rounded top in two steps
    box(-0.14, 0.9, -0.07, 0.14, 0.95, 0.07, GRAVE),
    box(-0.36, 0.0, -0.12, 0.36, 0.08, 0.12, GRAVE_D),               # the base
    box(-0.2, 0.45, 0.07, 0.2, 0.47, 0.072, "#5a584e"),              # worn lettering (lines, no names)
    box(-0.16, 0.38, 0.07, 0.16, 0.4, 0.072, "#5a584e"),
    box(-0.22, 0.6, 0.07, 0.22, 0.63, 0.072, "#5a584e"),
    box(-0.1, 0.7, 0.07, 0.1, 0.78, 0.072, "#6a685c"),               # a carved cross
    box(-0.04, 0.66, 0.07, 0.04, 0.82, 0.072, "#6a685c"),
    part((0.3, 0.06, 0.12), (0.2, 0.03, 0.18), "#4a6a2a"),           # moss at the foot
]
write("Churchyard Headstone", "ChurchyardHeadstone.json",
      "A weathered round-topped limestone headstone with worn lines of lettering (no names). Low cover; unbreakable.",
      hs, collider=[col((0.72, 0.95, 0.24))], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 7})
ct = [
    box(-0.95, 0.0, -0.5, 0.95, 0.12, 0.5, GRAVE_D, "concrete"),
    box(-0.85, 0.12, -0.42, 0.85, 0.74, 0.42, GRAVE, "concrete"),
    box(-0.98, 0.74, -0.53, 0.98, 0.84, 0.53, GRAVE),                 # the lid
    box(-0.6, 0.84, -0.3, 0.6, 0.86, 0.3, GRAVE_D),
]
for sx in (-1, 1):
    ct.append(box(sx * 0.86 - 0.04, 0.12, -0.44, sx * 0.86 + 0.04, 0.74, 0.44, GRAVE_D))      # corner balusters
for sz in (-1, 1):
    ct.append(box(-0.5, 0.3, sz * 0.425 - 0.004, 0.5, 0.56, sz * 0.425 + 0.004, "#8e8a7a"))     # panels
ct.append(part((0.5, 0.05, 0.3), (0.5, 0.875, 0.1), "#5a7a34"))                                   # lichen
write("Churchyard Chest Tomb", "ChurchyardChestTomb.json",
      "A Georgian chest tomb: a stone box with a heavy lid and panelled sides. Waist-high cover; unbreakable.",
      ct, collider=[col((1.96, 0.86, 1.06))], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3})

if __name__ == "__main__":
    print("wrote", len([f for f in os.listdir(OUT) if f.endswith(".json")]), "props to", OUT)
