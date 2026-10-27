#!/usr/bin/env python3
"""Writes Maps/Zombies/01-BlackmoorAsylum.json: the first purpose-built Zombies map (milestone 3).

Run from the repository root:  python3 tools/zombies/make_blackmoor.py

Blackmoor Asylum: an abandoned village asylum with a research wing, World at War style. Seven areas (the start room and
six behind doors and debris with rising prices), 18 boarded windows, four ground spawners, five box spots, seven wall
buys, five perk machines, the power switch (it gates three perks and the lab shutter), two workbenches, four salvage
piles, the weapon-slot upgrade and seventeen slot machines (the classic one in the start room and one for every part category round the map, two in the lab on the power; the script refuses to write the map while a Parts/<Category> folder has no machine). Every machine is a solid prop ("machineProps": true). The script also checks its own
layout: props keep clear of doors, windows, machines and each other's routes, and every point sits where it should.

Plan (north is -Z, east is +X):

                 [ outside: graves, the north ring ]
        +--------- Courtyard (open sky) ---------+---------+
        |   graves     fountain        mound     | Generator|
        +--debris--+------- wall -------+--door--+  House   |
        |  West    |    Receiving Hall  |  East  +----------+
        |  Offices |=debris   (start)   door= Ward |
        +--door----+--win--win--win-----+--shutter-+
        |  Chapel  |  ambulance bay     | Research |
        |          |  (outside)         |   Lab    |
        +----------+                    +----------+
"""
import json
import math
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Maps", "Zombies", "01-BlackmoorAsylum.json")

T = 0.3          # wall thickness
H = 3.6          # wall height
ROOF = 3.9       # roof top
WIN = dict(width=1.6, bottom=0.9, top=2.1)
DOOR_TOP = 2.6

ops = []
problems = []


def op(**kw):
    ops.append(kw)


def holes(*hs):
    return [{"center": c, "width": w, "bottom": b, "top": t} for (c, w, b, t) in hs]


def win(c):
    return (c, WIN["width"], WIN["bottom"], WIN["top"])


def door(c, w=3.0):
    return (c, w, 0, DOOR_TOP)


# ---- Colours ---------------------------------------------------------------------------------------------
PLASTER = "#6f675b"
PLASTER_DARK = "#5e574d"
STONE = "#585852"
CHAPEL = "#6a5e52"
LAB = "#5b6266"
GEN = "#55594f"
ROOFC = "#3a3733"

# ---- Areas (rects x0, z0, x1, z1) -------------------------------------------------------------------------
AREAS = {
    "Receiving Hall": (-7, -8, 7, 4),
    "East Ward": (7.3, -8, 23, 4),
    "West Offices": (-23, -8, -7.3, 4),
    "Courtyard": (-16, -28, 16, -8.3),
    "Generator House": (16.3, -26, 26, -8.3),
    "Chapel": (-23, 4.3, -6.3, 16),
    "Research Lab": (6.3, 4.3, 23, 16),
}


def area_of(x, z):
    for n, (x0, z0, x1, z1) in AREAS.items():
        if x0 <= x <= x1 and z0 <= z <= z1:
            return n
    return None


# ---- Walls ------------------------------------------------------------------------------------------------
# The long wall between the main block and the courtyard / generator house (z -8.3 .. -8).
op(op="WallX", x0=-23.3, x1=26.3, z=-8.3, t=T, y0=0, y1=H, color=PLASTER_DARK, holes=holes(door(-12), door(11.5)))
# The long south wall of the main block (z 4 .. 4.3): the chapel door, three start-room windows, the lab shutter.
op(op="WallX", x0=-23.3, x1=23.3, z=4, t=T, y0=0, y1=H, color=PLASTER_DARK,
   holes=holes(door(-15, 2.6), win(-3.5), win(0), win(3.5), door(15)))
# Main block: west and east outer walls (windows), the start room's side walls (a debris pile and a door).
op(op="WallZ", z0=-8.3, z1=4.3, x=-23.3, t=T, y0=0, y1=H, color=PLASTER, holes=holes(win(-5), win(1)))
op(op="WallZ", z0=-8.3, z1=4.3, x=23, t=T, y0=0, y1=H, color=PLASTER, holes=holes(win(-5), win(1)))
op(op="WallZ", z0=-8, z1=4, x=-7.3, t=T, y0=0, y1=H, color=PLASTER, holes=holes(door(-2, 2.6)))
op(op="WallZ", z0=-8, z1=4, x=7, t=T, y0=0, y1=H, color=PLASTER, holes=holes(door(-2, 2.6)))
# Courtyard: a stone garden wall (lower, no roof) with two gates and a west gap, the generator door on the east.
op(op="WallX", x0=-16.3, x1=16.3, z=-28.3, t=T, y0=0, y1=3.0, color=STONE, holes=holes(win(-8), win(8)))
op(op="WallZ", z0=-28.3, z1=-8.3, x=-16.3, t=T, y0=0, y1=3.0, color=STONE, holes=holes(win(-18)))
op(op="WallZ", z0=-28.3, z1=-8.3, x=16, t=T, y0=0, y1=H, color=GEN, holes=holes(door(-18)))
# Generator house.
op(op="WallX", x0=16, x1=26.3, z=-26.3, t=T, y0=0, y1=H, color=GEN, holes=holes(win(21)))
op(op="WallZ", z0=-26.3, z1=-8.3, x=26, t=T, y0=0, y1=H, color=GEN, holes=holes(win(-17)))
# Chapel.
op(op="WallZ", z0=4, z1=16.3, x=-23.3, t=T, y0=0, y1=H, color=CHAPEL)
op(op="WallZ", z0=4.3, z1=16.3, x=-6.3, t=T, y0=0, y1=H, color=CHAPEL, holes=holes(win(10)))
op(op="WallX", x0=-23.3, x1=-6, z=16, t=T, y0=0, y1=H, color=CHAPEL, holes=holes(win(-14)))
# Research lab.
op(op="WallZ", z0=4.3, z1=16.3, x=6, t=T, y0=0, y1=H, color=LAB, holes=holes(win(10)))
op(op="WallZ", z0=4, z1=16.3, x=23, t=T, y0=0, y1=H, color=LAB, holes=holes(win(10)))
op(op="WallX", x0=6, x1=23.3, z=16, t=T, y0=0, y1=H, color=LAB, holes=holes(win(14)))
# Roofs (the courtyard is open to the night sky).
op(op="Block", x0=-23.3, y0=H, z0=-8.3, x1=23.3, y1=ROOF, z1=4.3, color=ROOFC)
op(op="Block", x0=23.3, y0=H, z0=-8.3, x1=26.3, y1=ROOF, z1=-8.0, color=ROOFC)
op(op="Block", x0=16, y0=H, z0=-26.3, x1=26.3, y1=ROOF, z1=-8.3, color=ROOFC)
op(op="Block", x0=-23.3, y0=H, z0=4.3, x1=-6, y1=ROOF, z1=16.3, color=ROOFC)
op(op="Block", x0=6, y0=H, z0=4.3, x1=23.3, y1=ROOF, z1=16.3, color=ROOFC)
# The fountain's rim in the courtyard (solid), and the chapel altar.
op(op="Block", x0=-1.6, y0=0, z0=-19.6, x1=1.6, y1=0.55, z1=-16.4, color="#6c6a62")
op(op="Mesh", mesh="cylinder", x=0, z=-18, color="#6c6a62", width=3.6, height=0.6, depth=3.6, segments=20, material="Concrete")
op(op="Mesh", mesh="cylinder", x=0, z=-18, y=0.55, color="#3b4a44", width=3.0, height=0.04, depth=3.0, segments=20)
op(op="Block", x0=-0.25, y0=0.55, z0=-18.25, x1=0.25, y1=1.7, z1=-17.75, color="#6c6a62")
op(op="Mesh", mesh="sphere", x=0, z=-18, y=1.6, color="#6c6a62", width=0.9, height=0.5, depth=0.9, material="Concrete")
op(op="Block", x0=-21.9, y0=0, z0=8.8, x1=-20.7, y1=1.0, z1=11.2, color="#4a3a2c")

# ---- Zombies section ---------------------------------------------------------------------------------------
DOORS = [
    # name, kind, cost, power, box, areas
    ("the Ward Door", "door", 750, False, [7, 0, -3.3, 7.3, DOOR_TOP, -0.7], ["Receiving Hall", "East Ward"]),
    ("the Records Debris", "debris", 750, False, [-7.3, 0, -3.3, -7, DOOR_TOP, -0.7], ["Receiving Hall", "West Offices"]),
    ("the Courtyard Gate", "door", 1000, False, [10, 0, -8.3, 13, DOOR_TOP, -8], ["East Ward", "Courtyard"]),
    ("the Garden Debris", "debris", 1250, False, [-13.5, 0, -8.3, -10.5, DOOR_TOP, -8], ["West Offices", "Courtyard"]),
    ("the Generator Door", "door", 1250, False, [16, 0, -19.5, 16.3, DOOR_TOP, -16.5], ["Courtyard", "Generator House"]),
    ("the Chapel Door", "door", 1500, False, [-16.3, 0, 4, -13.7, DOOR_TOP, 4.3], ["West Offices", "Chapel"]),
    ("the Lab Shutter", "door", 750, True, [13.5, 0, 4, 16.5, DOOR_TOP, 4.3], ["East Ward", "Research Lab"]),
]


def door_sides(box):
    x0, _, z0, x1, _, z1 = box
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    if x1 - x0 <= z1 - z0:
        off = (x1 - x0) / 2 + 0.75
        return [(cx + off, cz), (cx - off, cz)]
    off = (z1 - z0) / 2 + 0.75
    return [(cx, cz + off), (cx, cz - off)]


# Windows: (area, wall axis, wall coordinate (its low side), centre along the wall, which side the area is on (+1/-1)).
WINDOWS = [
    ("Receiving Hall", "x", 4, -3.5, -1),
    ("Receiving Hall", "x", 4, 0, -1),
    ("Receiving Hall", "x", 4, 3.5, -1),
    ("East Ward", "z", 23, -5, -1),
    ("East Ward", "z", 23, 1, -1),
    ("West Offices", "z", -23.3, -5, 1),
    ("West Offices", "z", -23.3, 1, 1),
    ("Courtyard", "x", -28.3, -8, 1),
    ("Courtyard", "x", -28.3, 8, 1),
    ("Courtyard", "z", -16.3, -18, 1),
    ("Generator House", "x", -26.3, 21, 1),
    ("Generator House", "z", 26, -17, -1),
    ("Chapel", "z", -6.3, 10, -1),
    ("Chapel", "x", 16, -14, -1),
    ("Research Lab", "z", 6, 10, 1),
    ("Research Lab", "z", 23, 10, -1),
    ("Research Lab", "x", 16, 14, -1),
]

windows = []
spawners = []
for i, (area, axis, w0, c, side) in enumerate(WINDOWS):
    w1 = w0 + T
    inner = w1 if side > 0 else w0      # the face on the area's side
    outer = w0 if side > 0 else w1
    ins = inner + side * 0.9
    out = outer - side * 1.1
    spawn = outer - side * 4.2
    if axis == "x":   # a WallX: the wall runs along x at z = w0
        box = [c - 0.8, 0.9, w0, c + 0.8, 2.1, w1]
        inside, outside, at = [c, ins], [c, out], [c, spawn]
    else:
        box = [w0, 0.9, c - 0.8, w1, 2.1, c + 0.8]
        inside, outside, at = [ins, c], [out, c], [spawn, c]
    if area_of(*inside) != area:
        problems.append(f"window {i}: inside point {inside} is not in {area}")
    if area_of(*outside) is not None:
        problems.append(f"window {i}: outside point {outside} is inside {area_of(*outside)}")
    if area_of(*at) is not None:
        problems.append(f"window {i}: spawner {at} is inside {area_of(*at)}")
    windows.append({"area": area, "box": [round(v, 3) for v in box], "outside": outside, "inside": inside, "boards": 6})
    spawners.append({"window": i, "at": [round(v, 2) for v in at]})

# Windows whose spawner would sit in the ambulance bay notch: spread them a little so they don't share a spot.
GROUND = [("Courtyard", [-10, -23.5]), ("Courtyard", [9, -13]), ("Research Lab", [16, 10]), ("Chapel", [-10, 8.2])]
for area, at in GROUND:
    if area_of(*at) != area:
        problems.append(f"ground spawner {at} not in {area}")
    spawners.append({"area": area, "at": at})

WALL_BUYS = [
    {"build": "Wall Rifle", "at": [-4, -7.97], "yaw": 180, "cost": 500, "ammoCost": 250},     # start room, north wall
    {"build": "Wall SMG", "at": [10, 3.97], "yaw": 0, "cost": 1000},                            # ward, south wall
    {"build": "Wall Pistol", "at": [-19, -7.97], "yaw": 180, "cost": 300, "ammoCost": 150},   # offices, north wall
    {"build": "Wall Pump", "at": [0, -8.33], "yaw": 0, "cost": 900},                            # courtyard, the hall's back wall
    {"build": "Burst Marksman", "at": [25.97, -12], "yaw": -90, "cost": 1200},                  # generator house, east wall
    {"build": "Wall SMG", "at": [-22.97, 6.5], "yaw": 90, "cost": 1000},                        # chapel, west wall
    {"build": "Wall Pump", "at": [6.33, 13], "yaw": 90, "cost": 900},                           # lab, west wall
]

BOX = [  # the box spots: a solid crate stands at each (machine props)
    {"at": [0, -7.6], "yaw": 180},       # start room, against the north wall
    {"at": [-10, 3.6], "yaw": 0},        # offices, against the south wall
    {"at": [-6, -22], "yaw": 90},        # courtyard, by the graves
    {"at": [-19.5, 15.6], "yaw": 0},     # chapel, against the south wall
    {"at": [9.5, 15.6], "yaw": 0},       # lab, against the south wall
]
BENCHES = [{"at": [-20, 3.55], "yaw": 0}, {"at": [20.5, -8.75], "yaw": 0}]
PERKS = [
    {"perk": "revive", "at": [-6.6, -6], "yaw": 90},
    {"perk": "secondwind", "at": [6.6, 1.6], "yaw": -90},
    {"perk": "jugg", "at": [22.6, -2], "yaw": -90, "power": True},
    {"perk": "speed", "at": [-22.6, -2], "yaw": 90, "power": True},
    {"perk": "doubletap", "at": [-6.7, 13.5], "yaw": -90, "power": True},
]
SLOT = {"at": [22.65, 6.5], "yaw": -90, "cost": 4000}
POWER = {"at": [18, -25.8], "yaw": 180}
SLOTS = [  # slot machines: the classic one (any part) in the start room; per-category machines (2026-10-26) spread round the map
    {"at": [6.6, -6.2], "yaw": -90},
    {"at": [22.6, 8.4], "yaw": -90, "power": True, "category": "Receiver"},   # lab, behind the power
    {"at": [11.8, 3.6], "yaw": 0, "category": "Barrel"},                       # ward, south wall
    {"at": [-14.6, -7.6], "yaw": 180, "category": "Grip"},                     # offices, north wall
    {"at": [-7.7, 2.7], "yaw": -90, "category": "Stock"},                      # offices, by the start room wall
    {"at": [5.0, -8.7], "yaw": 0, "category": "Ammo"},                         # courtyard, the hall's back wall
    {"at": [-5.0, -8.7], "yaw": 0, "category": "Feed"},                        # courtyard, the hall's back wall
    {"at": [25.6, -14.2], "yaw": -90, "category": "Action"},                   # generator house, east wall
    {"at": [-6.7, 7.0], "yaw": -90, "category": "Hilt"},                       # chapel, east wall
    {"at": [-11.6, 15.6], "yaw": 0, "category": "Head"},                       # chapel, south wall
    {"at": [6.7, 5.2], "yaw": 90, "power": True, "category": "Optic"},         # lab, west wall, behind the power
    {"at": [13.5, -7.6], "yaw": 180, "category": "Splitter"},                  # ward, north wall, by the courtyard gate
    {"at": [17.0, 3.6], "yaw": 0, "category": "Utility"},                      # ward, south wall, by the lab shutter
    {"at": [16.8, -20.5], "yaw": 90, "category": "Upgrade"},                   # generator house, west wall
    {"at": [15.6, -13.8], "yaw": -90, "category": "Multiplier"},               # courtyard, the generator house wall
    {"at": [-22.6, 5.2], "yaw": 90, "category": "MeleeMod"},                   # chapel, west wall
    {"at": [-15.6, -12.2], "yaw": 90, "category": "Muzzle"},                   # courtyard, the west garden wall
]
# Every part category (a Parts/<Category> folder: the PartCategory names) gets a machine; a new category must be placed here.
PART_CATEGORIES = sorted(d for d in os.listdir(os.path.join(ROOT, "Parts")) if not d.startswith("_") and d not in ("Builds", "Tables")
                         and os.path.isdir(os.path.join(ROOT, "Parts", d)))
_placed_cats = [s["category"] for s in SLOTS if "category" in s]
for c in PART_CATEGORIES:
    if c not in _placed_cats:
        problems.append(f"the part category {c} has no slot machine: add one to SLOTS")
for c in _placed_cats:
    if c not in PART_CATEGORIES or _placed_cats.count(c) > 1:
        problems.append(f"slot machine category {c}: not a Parts folder, or placed twice")
SALVAGE = [{"at": [12.2, 1.2]}, {"at": [6, -25.5]}, {"at": [23.5, -22]}, {"at": [13.2, 12.6]}]

zombies = {
    "start": "Receiving Hall",
    "machineProps": True,
    "areas": [{"name": n, "rect": list(r)} for n, r in AREAS.items()],
    "doors": [{"name": n, "kind": k, "cost": c, **({"power": True} if p else {}), "box": b, "areas": a} for (n, k, c, p, b, a) in DOORS],
    "windows": windows,
    "spawners": spawners,
    "wallBuys": WALL_BUYS,
    "box": {"cost": 950, "start": 0, "spots": BOX},
    "workbenches": BENCHES,
    "perks": PERKS,
    "slotUpgrade": SLOT,
    "power": POWER,
    "salvage": SALVAGE,
    "slots": SLOTS,
}

# ---- Keep-clear zones: where players and zombies must be able to stand and walk -----------------------------
KEEP = []  # (x, z, radius, what)
for (n, _, _, _, b, _) in DOORS:
    for s in door_sides(b):
        KEEP.append((s[0], s[1], 1.1, f"{n} side"))
    KEEP.append(((b[0] + b[3]) / 2, (b[2] + b[5]) / 2, 1.2, n))
for i, w in enumerate(windows):
    KEEP.append((w["inside"][0], w["inside"][1], 1.0, f"window {i} inside"))
    KEEP.append((w["outside"][0], w["outside"][1], 1.1, f"window {i} outside"))
for s in spawners:
    KEEP.append((s["at"][0], s["at"][1], 1.0, "spawner"))


def fwd(yaw_deg):
    a = math.radians(yaw_deg)
    return math.sin(a), -math.cos(a)


MACHINE_SIZE = {"box": (1.3, 0.66), "bench": (1.6, 0.8), "perk": (0.9, 0.7), "slot": (1.0, 0.6), "power": (0.7, 0.3), "slots": (0.9, 0.7)}
machines = [("box", s) for s in BOX] + [("bench", s) for s in BENCHES] + [("perk", s) for s in PERKS] + [("slot", SLOT), ("power", POWER)] + [("slots", s) for s in SLOTS]
for kind, s in machines:
    fx, fz = fwd(s["yaw"])
    KEEP.append((s["at"][0] + fx * (MACHINE_SIZE[kind][1] / 2 + 0.6), s["at"][1] + fz * (MACHINE_SIZE[kind][1] / 2 + 0.6), 0.6, f"{kind} front"))
for wb in WALL_BUYS:
    fx, fz = fwd(wb["yaw"])
    KEEP.append((wb["at"][0] + fx * 0.8, wb["at"][1] + fz * 0.8, 0.6, f"{wb['build']} front"))
for s in SALVAGE:
    KEEP.append((s["at"][0], s["at"][1], 0.6, "salvage"))


def rect_for(kind, s):
    w, d = MACHINE_SIZE[kind]
    if s["yaw"] % 180 != 0:
        w, d = d, w
    return (s["at"][0] - w / 2, s["at"][1] - d / 2, s["at"][0] + w / 2, s["at"][1] + d / 2)


def rect_hits_circle(r, x, z, rad):
    cx = min(max(x, r[0]), r[2])
    cz = min(max(z, r[1]), r[3])
    return (cx - x) ** 2 + (cz - z) ** 2 < rad * rad


for kind, s in machines:
    r = rect_for(kind, s)
    a = area_of(*s["at"])
    if a is None:
        problems.append(f"{kind} at {s['at']} is in no area")
    else:
        x0, z0, x1, z1 = AREAS[a]
        if r[0] < x0 - 0.01 or r[2] > x1 + 0.01 or r[1] < z0 - 0.01 or r[3] > z1 + 0.01:
            problems.append(f"{kind} at {s['at']} pokes out of {a}")
    for (x, z, rad, what) in KEEP:
        if what.startswith(kind + " front") and abs(x - s["at"][0]) < 2 and abs(z - s["at"][1]) < 2:
            continue
        if rect_hits_circle(r, x, z, rad - 0.2):
            problems.append(f"{kind} at {s['at']} is in the way of {what} ({x:.2f}, {z:.2f})")


# ---- Props -----------------------------------------------------------------------------------------------
def load_prop(name):
    for folder, _, files in os.walk(os.path.join(ROOT, "Props")):
        for f in files:
            if f.endswith(".json"):
                p = os.path.join(folder, f)
                try:
                    d = json.load(open(p))
                except Exception:
                    continue
                if d.get("name") == name:
                    return d
    raise SystemExit(f"no prop {name}")


_props = {}


def footprint(name, x, z, yaw):
    d = _props.setdefault(name, load_prop(name))
    if d.get("collision", "solid") != "solid":
        return None
    boxes = []
    col = d.get("collider", "bounds")
    if isinstance(col, list):
        for c in col:
            s = c["size"]
            o = c.get("offset", [0, s[1] / 2, 0])
            boxes.append((o[0] - s[0] / 2, o[2] - s[2] / 2, o[0] + s[0] / 2, o[2] + s[2] / 2))
    else:
        xs, zs = [], []
        for p in d["parts"]:
            s = p["size"]
            o = p.get("offset", [0, s[1] / 2, 0])
            r = max(s[0], s[2]) / 2 if "rotation" in p or "yaw" in p else None
            hx, hz = (r, r) if r else (s[0] / 2, s[2] / 2)
            xs += [o[0] - hx, o[0] + hx]
            zs += [o[2] - hz, o[2] + hz]
        boxes.append((min(xs), min(zs), max(xs), max(zs)))
    out = []
    a = math.radians(yaw)
    cs, sn = round(math.cos(a), 6), round(math.sin(a), 6)
    for (x0, z0, x1, z1) in boxes:
        pts = [(px * cs + pz * sn, -px * sn + pz * cs) for px in (x0, x1) for pz in (z0, z1)]
        out.append((x + min(p[0] for p in pts), z + min(p[1] for p in pts), x + max(p[0] for p in pts), z + max(p[1] for p in pts)))
    return out


placed = []


def prop(name, x, z, yaw=0, y=None, check=True, **kw):
    o = {"op": "Prop", "name": name, "x": x, "z": z}
    if yaw:
        o["yaw"] = yaw
    if y is not None:
        o["y"] = y
    o.update(kw)
    ops.append(o)
    fp = footprint(name, x, z, yaw)
    if fp and check:
        mine = []
        for r in fp:
            for (kx, kz, rad, what) in KEEP:
                if rect_hits_circle(r, kx, kz, rad):
                    problems.append(f"prop {name} at ({x}, {z}) is in the way of {what} ({kx:.2f}, {kz:.2f})")
            for kind, s in machines:
                m = rect_for(kind, s)
                if r[0] < m[2] and r[2] > m[0] and r[1] < m[3] and r[3] > m[1]:
                    problems.append(f"prop {name} at ({x}, {z}) overlaps the {kind} at {s['at']}")
            for q, qn in placed:
                if r[0] < q[2] and r[2] > q[0] and r[1] < q[3] and r[3] > q[1]:
                    problems.append(f"prop {name} at ({x}, {z}) overlaps {qn}")
            mine.append((r, name))
        placed.extend(mine)


def lamp(x, z, facing, y=2.55):
    """A caged bulkhead lamp on a wall, facing into the room (prop yaw: 0 faces +Z)."""
    prop("Caged Bulkhead Lamp", x, z, {"S": 0, "N": 180, "E": 90, "W": -90}[facing], y=y)


# Receiving Hall (the start room): a reception desk, waiting chairs, filing cabinets, lamps.
prop("Reception Desk", -4.6, 0.6, 90)
prop("Office Chair", -5.9, 0.6, 90)
prop("Filing Cabinet", 4.6, -7.55, 180)
prop("Filing Cabinet", 5.4, -7.55, 180)
prop("Wooden Chair", 5.2, 2.9, 0)
prop("Wooden Chair", -1.6, 2.9, 0)
prop("Coat Stand", -6.5, 3.4)
prop("Radiator", 2, -7.8, 180, check=False)
lamp(-3.5, -7.97, "S")
lamp(3.5, -7.97, "S")
lamp(-6.97, -3.5, "E")
lamp(6.97, -5.5, "W")
prop("Tube Light", 0, -2, y=3.1)
prop("Newspaper", 1.2, -1.5, 30)
prop("Cardboard Box", -2.2, -6.6, 15)

# East Ward: beds along both walls, bedside tables, a curtain of shelving.
for i, x in enumerate([15.2, 17.6, 20.0]):
    prop("Single Bed", x, -6.9, 180)
    prop("Bedside Table", x + 1.05, -7.6, 180)
for x in [18.3, 20.7]:
    prop("Single Bed", x, 2.9, 0)
prop("Metal Shelving", 8.3, -6.5, 90)
prop("Wheelie Bin Grey", 8.0, 3.4)
lamp(15.5, -7.97, "S")
lamp(20.5, 3.97, "N")
lamp(22.97, -2.0 + 1.2, "W")
prop("Tube Light", 15, -2, y=3.1)

# West Offices: desks, filing, archives, shelving, a whiteboard.
prop("Office Desk", -19, -3.2)
prop("Office Chair", -19, -2.3, 180)
prop("Office Desk", -15.5, -3.2)
prop("Office Chair", -15.5, -2.3, 180)
prop("Archive Boxes", -22.3, -7.3)
prop("Metal Shelving", -16.5, -7.55, 180)
prop("Filing Cabinet", -9.2, -7.55, 180)
prop("Whiteboard", -11, -0.5, 90)
prop("Paper Shredder", -22.5, 3.4)
lamp(-16.5, 3.97, "N")
lamp(-22.97, -2.5 + 1.0, "E")
prop("Tube Light", -15, 0, y=3.1)

# Courtyard: the graves round the ground spawner, a memorial cross, a well, benches, an old cart.
for (x, z) in [(-13.5, -25.5), (-11.8, -25.8), (-8.2, -25.6), (-13.8, -21.2), (-7.6, -21.5), (-12.6, -19.6), (-8.8, -19.4)]:
    prop("Churchyard Headstone", x, z, 180)
prop("Churchyard Chest Tomb", -14, -15)
prop("Memorial Cross", -3.5, -25.6)
prop("Stone Well", 11.5, -24.5)
prop("Bench", 4.5, -18, -90)
prop("Bench", -4.5, -18, 90)
prop("Tipped Farm Cart", 12.5, -19.5, 90)
prop("Hanging Lantern", 3.2, -10.2)
prop("Hanging Lantern", -3.2, -10.2)
prop("Flickering Street Lamp", 14.6, -27)
prop("Flickering Street Lamp", -14.6, -9.8)
prop("Leaning Farm Tools", 15.4, -11.6, -90)

# Generator House: the generators, fuel and pipes.
prop("Turbine Generator", 21, -16.5, 90)
prop("Running Generator", 24.6, -24.4)
prop("Gas Cylinders", 16.9, -11.2)
prop("Oil Drum", 25.3, -9.0)
prop("Oil Drum", 24.4, -9.0)
prop("Pallet Stack", 17.2, -23.6)
prop("Junction Box", 23.6, -25.85, 0, check=False)
prop("Wall Pipe Run", 18.0, -8.6, 180, check=False)
lamp(25.97, -21.5, "W")
lamp(16.33, -13.0, "E")
prop("Tube Light", 21, -12.5, y=3.1)

# Chapel: pews facing the altar on the west, a cross, candles.
for z in [6.4, 9.8, 13.2]:
    for x in [-16.8, -13.0]:
        prop("Bench", x, z, -90)
prop("Calvary Cross", -22.5, 13.2, 90)
prop("Hanging Lantern", -21.5, 15.1)
prop("Bookshelf", -9.2, 15.6, 0)
lamp(-6.33, 6.5, "W")
lamp(-12.0, 15.97, "N")
prop("Rubble Pile", -10.5, 5.6)

# Research Lab: consoles, the mainframe, a specimen tank, hazmat suits, signs.
prop("Reactor Control Desk", 12.5, 7.6, 180)
prop("Tape Mainframe", 22.5, 14.6, -90)
prop("Server Rack", 22.5, 12.6, -90)
prop("Propellant Tank", 18.8, 13.6)
prop("Hazmat Suit Rack", 6.8, 7.5, 90)
prop("Status Board", 11.5, 15.7, 180)
prop("Coolant Pump", 19.5, 5.0)
prop("Radiation Sign", 16, 4.33, 0, y=1.6, check=False)
lamp(6.33, 11.8, "E")
lamp(22.97, 8.5, "W")
prop("Tube Light", 15, 10, y=3.1)
prop("Tube Light", 11, 12, y=3.1)

# Outside: the ambulance bay, the graveyard to the west, junk round the ring, lamps on poles.
prop("Ambulance", 0, 19.5, 90)
prop("Rubble Pile", -4.2, 15.5)
prop("Trash Bag", 4.5, 16.4)
for (x, z) in [(-30.5, -14.5), (-30.0, -9.5), (-30.8, -2.0), (-30.4, 15.0), (-28.5, 19.5)]:
    prop("Churchyard Headstone", x, z, 90)
prop("Burnt Out Car", 31.0, -2.0, 0)
prop("Concrete Pipe", 30.5, 15.0, 90)
prop("Tyre Stack", 22.0, 22.0)
prop("Rusty Drum", -22.0, -33.5)
prop("Rusty Drum", 12.5, -34.0)
prop("Wooden Crate", 31.5, -27.0)
prop("Flickering Street Lamp", -29.0, 6.0)
prop("Flickering Street Lamp", 29.5, -12.5)
prop("Flickering Street Lamp", 0, -34.0)
prop("Flickering Street Lamp", 10, 22.5)

# Decals: grime, damp, cracks, blood. (Looks only.)
op(op="DecalScatter", decals=["Floor Cracks", "Water Stain", "Dirt Patch", "Floor Cracks", "Rubble Dust"], x0=-22.5, z0=-7.5, x1=22.5, z1=3.5, count=60, seed=11, gap=1.2)
op(op="DecalScatter", decals=["Gib Smear", "Gib Spatter"], x0=-22, z0=-7.5, x1=22, z1=3.5, count=10, seed=3, gap=3)
op(op="DecalScatter", decals=["Mould", "Water Damage", "Wall Cracks", "Grime Streaks", "Handprints", "Peeling Paint"], x0=-22.8, z0=-7.8, x1=22.8, z1=15.8, count=80, seed=5, onto="wall")
op(op="DecalScatter", decals=["Cracked Tiles", "Water Stain", "Floor Cracks", "Gib Smear"], x0=-22.5, z0=4.8, x1=-6.8, z1=15.5, count=18, seed=7, gap=1.4)
op(op="DecalScatter", decals=["Cracked Tiles", "Oil Stain", "Paint Spill", "Gib Spatter"], x0=6.8, z0=4.8, x1=22.5, z1=15.5, count=18, seed=8, gap=1.4)
op(op="DecalScatter", decals=["Oil Stain", "Oil Drips", "Floor Cracks"], x0=16.8, z0=-25.5, x1=25.5, z1=-8.8, count=14, seed=9, gap=1.4)
op(op="DecalScatter", decals=["Leaf Litter", "Mud Patch", "Moss Patch", "Leaf Pile"], x0=-15.5, z0=-27.5, x1=15.5, z1=-8.8, count=26, seed=4, gap=1.5)
op(op="Decal", decal="Hazard Stripes", x=15, z=5.2, size=2.8)
op(op="Decal", decal="Stencil Arrow", x=13, z=-8.32, y=1.6, onto="wall+z", size=0.9)
op(op="Decal", decal="Graffiti Scribble", x=0, z=-7.98, y=2.2, onto="wall-z", size=2.2)
op(op="Decal", decal="Handprints", x=-6.98, z=1.5, y=1.4, onto="wall-x", size=1.0)

# Spawns: four in the start room (Zombies spawns the team there); more round the map for other modes.
for (x, z) in [(-2.5, -3.5), (2.5, -3.5), (-2.5, 0.5), (2.5, 0.5), (14, -2), (-14, 0), (0, -14), (21, -13), (-12, 9), (12, 11)]:
    op(op="Spawn", x=x, z=z)
op(op="AutoWaypoints", spacing=3)

# ---- The map ----------------------------------------------------------------------------------------------
BOUNDARY = [[-31.5, -36], [-10, -37.5], [18, -35.5], [33.5, -32], [34.5, -6], [31, 8], [32, 23], [10, 26], [-12, 25.5], [-30.5, 23], [-34, 4], [-33, -21]]

for kind, s in machines:
    pass

the_map = {
    "format": "call-of-slop-map/1",
    "name": "Blackmoor Asylum",
    "description": "Zombies: an abandoned village asylum and its research wing at night. Seven areas, eighteen boarded windows, the mystery box in five spots, five perks, a slot machine for every part category, the power in the generator house.",
    "author": "Call of Slop",
    "order": 960,
    "maxPlayers": 8,
    "boundary": {
        "shape": "polygon",
        "points": BOUNDARY,
        "noise": {"amplitude": 0.8, "scale": 9, "seed": 13},
        "edge": {"style": "wall", "height": 3.4, "thickness": 0.9, "color": "#3e3d3a", "material": "Brick"},
    },
    "skybox": {
        "preset": "night",
        "zenith": "#070b12",
        "horizon": "#26302f",
        "ground": "#151a19",
        "moon": {"size": 5, "intensity": 0.55, "color": "#cfe0d8"},
        "stars": 0.8,
        "clouds": {"coverage": 0.55, "density": 0.55, "speed": 5, "color": "#2b3334"},
        "haze": {"color": "#3b4a42", "strength": 0.35},
        "ambient": 1.9,
    },
    "terrain": {
        "cell": 1,
        "margin": 22,
        "surface": {"grass": "#3f4a32", "dirt": "#4d4334", "rock": "#4f4c47", "sand": "#5a5345", "variation": 0.1},
        "features": [
            {"type": "noise", "amplitude": 0.55, "scale": 11, "seed": 21},
            {"type": "hill", "x": -48, "z": -30, "radius": 18, "height": 7},
            {"type": "hill", "x": 50, "z": 10, "radius": 20, "height": 8},
            {"type": "ridge", "points": [[-60, 42], [0, 50], [60, 44]], "width": 26, "height": 6},
            {"type": "hill", "x": 6, "z": -60, "radius": 22, "height": 6},
            {"type": "flatten", "x": 1.5, "z": -6.5, "w": 57, "d": 52, "height": 0, "edge": 5},
            {"type": "flatten", "x": 0, "z": -2, "w": 47, "d": 12.6, "height": 0, "edge": 0.5, "material": "rock"},
            {"type": "flatten", "x": -14.6, "z": 10.2, "w": 17.3, "d": 12.3, "height": 0, "edge": 0.5, "material": "rock"},
            {"type": "flatten", "x": 14.6, "z": 10.2, "w": 17.3, "d": 12.3, "height": 0, "edge": 0.5, "material": "rock"},
            {"type": "flatten", "x": 21.2, "z": -17.3, "w": 10.3, "d": 18, "height": 0, "edge": 0.5, "material": "rock"},
            {"type": "flatten", "x": 0, "z": 15, "w": 12, "d": 18, "height": 0, "edge": 0.5, "material": "dirt"},
            {"type": "hill", "x": 9, "z": -21.5, "radius": 3.2, "height": 0.6, "material": "dirt"},
            {"type": "hill", "x": -29.5, "z": 2, "radius": 6, "height": 0.9},
        ],
    },
    "foliage": {
        "seed": 9,
        "regions": [
            {"type": "grass", "density": 3, "rect": [-15.6, -27.6, 15.6, -8.8], "color": "#4a5536", "patchiness": 0.5},
            {"type": "grass", "density": 2.2, "polygon": BOUNDARY, "color": "#435033", "patchiness": 0.55},
            {"type": "bush", "density": 0.08, "polygon": BOUNDARY, "color": "#34402c", "scale": [0.8, 1.3]},
        ],
        "items": [
            {"type": "pine", "x": -31, "z": -30, "height": 9},
            {"type": "pine", "x": 30, "z": -28.5, "height": 8},
            {"type": "tree", "x": 29.5, "z": 20, "height": 7},
            {"type": "tree", "x": -28, "z": 21.5, "height": 6.5},
            {"type": "tree", "x": 13.5, "z": -10.6, "height": 5.5},
        ],
        "exclude": [{"shape": "rect", "center": [-1.5, 15], "halfX": 5.5, "halfZ": 10}],
    },
    "ops": ops,
    "zombies": zombies,
}

if problems:
    print("PROBLEMS:")
    for p in problems:
        print("  " + p)
    sys.exit(1)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w") as f:
    f.write(json.dumps(the_map, indent=1))
    f.write("\n")
print(f"wrote {OUT}: {len(ops)} ops, {len(windows)} windows, {len(spawners)} spawners, {len(placed)} solid props checked")
