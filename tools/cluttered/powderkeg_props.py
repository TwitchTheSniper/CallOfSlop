#!/usr/bin/env python3
"""Writes the props Powder Keg (tools/cluttered/powderkeg.py) needs that no prop covered yet.

Usage: python3 tools/cluttered/powderkeg_props.py <repo root>

Two groups (see Props/README.md, "Pack props" and "Where map assets live"):
- Props/Volatile (global, reusable by any map): things that go off. Red Fuel Barrel, Propane Tank, Propane Bulk Tank,
  Gas Bottle, Gas Bottle Cage and Flammables Cabinet are explosive with a short "fuse": a hit that breaks one lights it
  (sparks and a hiss) and it goes off a moment later, so a line of them ripples instead of popping all at once. Chemical
  Drum and IBC Tote break but don't explode.
- Maps/Cluttered/Props/PowderKeg (pack-local, category "Warehouse"): pallet racking bays (loaded, wrapped and open), a
  catwalk safety railing, a semi trailer, a pallet jack, a dock bumper, a hazard placard and a high-bay light.

Explosive values: a frag grenade is 120 damage in a 7.5 m radius; the old Explosive Barrel 110 in 5.5 m with no fuse.
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
GLOBAL = os.path.join(REPO, "Props/Volatile")
PACK = os.path.join(REPO, "Maps/Cluttered/Props/PowderKeg")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def octagon(d, h, y, color, x=0.0, z=0.0, **kw):
    """Four boxes turned 0/45/90/135 degrees about Y: an upright octagonal cylinder d across, h tall, centred at height y."""
    return [part((d, h, d * 0.414), (x, y, z), color, rotation=[0, a, 0], **kw) for a in (0, 45, 90, 135)]


def octagon_x(d, length, y, color, x=0.0, z=0.0, **kw):
    """An octagonal cylinder lying along X (four boxes turned about X)."""
    return [part((length, d, d * 0.414), (x, y, z), color, rotation=[a, 0, 0], **kw) for a in (0, 45, 90, 135)]


def write(folder, fname, d):
    os.makedirs(folder, exist_ok=True)
    d = {"format": "call-of-slop-prop/1", **d}
    if "parts" in d:
        assert len(d["parts"]) <= 128, (fname, len(d["parts"]))
    lines = ["{"]
    keys = list(d.keys())
    for k in keys:
        v = d[k]
        if k == "parts":
            lines.append('  "parts": [')
            lines.append(",\n".join("    " + json.dumps(p) for p in v))
            lines.append("  ]" + ("," if k != keys[-1] else ""))
        else:
            lines.append(f'  "{k}": {json.dumps(v)}' + ("," if k != keys[-1] else ""))
    lines.append("}")
    with open(os.path.join(folder, fname), "w") as f:
        f.write("\n".join(lines) + "\n")


# ===================================================================================== Props/Volatile (global)
BARREL_RED = "#c8161d"
p = []
p += octagon(0.58, 0.86, 0.45, BARREL_RED, material="metal")
for y in (0.015, 0.885):
    p += octagon(0.62, 0.03, y, "darkgrey")
for y in (0.31, 0.6):
    p += octagon(0.625, 0.035, y, "#9e1015")
p += octagon(0.56, 0.012, 0.9, "#a51217")
p.append(part((0.07, 0.03, 0.07), (0.15, 0.915, 0.1), "black"))
p.append(part((0.05, 0.025, 0.05), (-0.14, 0.912, -0.12), "black"))
# white band with a flame diamond on two sides
p.append(part((0.36, 0.2, 0.012), (0, 0.46, 0.296), "white"))
p.append(part((0.13, 0.13, 0.014), (0, 0.46, 0.298), "orange", rotation=[0, 0, 45]))
p.append(part((0.04, 0.07, 0.016), (0, 0.45, 0.302), "black"))
p.append(part((0.012, 0.2, 0.36), (0.296, 0.46, 0), "white"))
p.append(part((0.014, 0.13, 0.13), (0.298, 0.46, 0), "orange", rotation=[45, 0, 0]))
p.append(part((0.016, 0.07, 0.04), (0.302, 0.45, 0), "black"))
write(GLOBAL, "RedFuelBarrel.json", {
    "name": "Red Fuel Barrel",
    "category": "Volatile",
    "description": "A bright red 200-litre fuel drum with flame placards. A hit that breaks it lights it: sparks, a hiss, and 0.6 s later it goes off. Barrels caught in the blast light too, so a line of them goes up one after another.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [0.6, 0.92, 0.6]}],
    "scatter": {"randomYaw": True, "scale": [0.97, 1.03]},
    "breakable": {"health": 30, "breakBy": "all", "sound": "metal",
                  "debris": {"count": 12, "size": [0.08, 0.24], "speed": 7, "colors": ["red", "darkgrey", "black", "orange"]},
                  "explode": {"radius": 5.5, "damage": 120, "fuse": 0.6}},
    "parts": p,
})

# Propane tank: an upright cylinder like a forklift / heater bottle
p = []
p += octagon(0.36, 1.12, 0.62, "white", material="metal")
p += octagon(0.3, 0.12, 1.22, "white")
p += octagon(0.38, 0.06, 0.04, "darkgrey")
p.append(part((0.06, 0.14, 0.06), (0, 1.34, 0), "galvanized"))
p.append(part((0.16, 0.05, 0.05), (0, 1.39, 0), "#c8161d"))
p.append(part((0.26, 0.16, 0.26), (0, 1.33, 0), "#c8161d"))
p.append(part((0.2, 0.08, 0.012), (0, 0.8, 0.186), "#c8161d"))
p.append(part((0.012, 0.08, 0.2), (0.186, 0.8, 0), "#c8161d"))
write(GLOBAL, "PropaneTank.json", {
    "name": "Propane Tank",
    "category": "Volatile",
    "description": "A white propane bottle with a red valve guard. Shoot it and it hisses for a second, then goes off hard.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [0.38, 1.42, 0.38]}],
    "scatter": {"randomYaw": True, "scale": [0.96, 1.04]},
    "breakable": {"health": 70, "breakBy": "all", "sound": "metal",
                  "debris": {"count": 10, "size": [0.06, 0.2], "speed": 8, "colors": ["white", "red", "darkgrey"]},
                  "explode": {"radius": 6.5, "damage": 140, "fuse": 1.0}},
    "parts": p,
})

# Propane bulk tank: a horizontal bullet tank on two concrete saddles
p = []
p += octagon_x(1.5, 3.4, 1.15, "white", material="metal")
for sx in (-1.0, 1.0):
    p += octagon_x(1.1, 0.25, 1.15, "white", x=sx * 1.82)
    p += octagon_x(0.6, 0.12, 1.15, "lightgrey", x=sx * 2.0)
for sx in (-1.1, 1.1):
    p.append(part((0.4, 0.55, 1.2), (sx, 0.275, 0), "concrete", material="concrete"))
p.append(part((3.0, 0.16, 0.012), (0, 1.15, 0.761), "#c8161d"))
p.append(part((3.0, 0.16, 0.012), (0, 1.15, -0.761), "#c8161d"))
p.append(part((0.5, 0.18, 0.5), (0.3, 1.97, 0), "galvanized"))
p.append(part((0.08, 0.3, 0.08), (0.3, 2.15, 0.12), "#c8161d"))
p.append(part((0.24, 0.06, 0.06), (0.3, 2.28, 0.12), "#c8161d"))
p.append(part((0.06, 0.25, 0.06), (-0.4, 2.0, 0), "galvanized"))
write(GLOBAL, "PropaneBulkTank.json", {
    "name": "Propane Bulk Tank",
    "category": "Volatile",
    "description": "A 4 m bulk propane tank on concrete saddles (long side along X). It takes a lot to rupture it, then burns for 1.5 s and leaves a crater-sized blast.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [4.1, 1.95, 1.52]}],
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "breakable": {"health": 300, "breakBy": "all", "sound": "metal",
                  "debris": {"count": 24, "size": [0.1, 0.4], "speed": 9, "colors": ["white", "red", "concrete", "darkgrey"]},
                  "explode": {"radius": 11, "damage": 200, "fuse": 1.5}},
    "parts": p,
})

# Gas bottle: a single welding cylinder
p = []
p += octagon(0.24, 1.0, 0.52, "#6e1a1a", material="metal")
p += octagon(0.2, 0.1, 1.06, "#6e1a1a")
p.append(part((0.05, 0.12, 0.05), (0, 1.16, 0), "galvanized"))
p.append(part((0.12, 0.04, 0.04), (0, 1.2, 0), "black"))
p.append(part((0.16, 0.06, 0.012), (0, 0.85, 0.123), "white"))
write(GLOBAL, "GasBottle.json", {
    "name": "Gas Bottle",
    "category": "Volatile",
    "description": "A maroon welding gas cylinder. Small blast, short fuse.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [0.26, 1.2, 0.26]}],
    "scatter": {"randomYaw": True, "scale": [0.95, 1.05]},
    "breakable": {"health": 25, "breakBy": "all", "sound": "metal",
                  "debris": {"count": 6, "size": [0.05, 0.14], "speed": 7, "colors": ["#6e1a1a", "galvanized"]},
                  "explode": {"radius": 3.8, "damage": 90, "fuse": 0.4}},
    "parts": p,
})

# Gas bottle cage: eight bottles in a galvanized frame
p = []
for (x, z) in ((-0.45, -0.18), (-0.15, -0.18), (0.15, -0.18), (0.45, -0.18), (-0.45, 0.18), (-0.15, 0.18), (0.15, 0.18), (0.45, 0.18)):
    col = "#6e1a1a" if (x + z) % 0.6 < 0.3 else "#1f4f2c"
    p += octagon(0.24, 1.1, 0.62, col, x=x, z=z)
    p.append(part((0.05, 0.1, 0.05), (x, 1.22, z), "galvanized"))
for (x, z) in ((-0.64, -0.36), (0.64, -0.36), (-0.64, 0.36), (0.64, 0.36)):
    p.append(part((0.05, 1.5, 0.05), (x, 0.75, z), "galvanized"))
for y in (0.05, 0.55, 1.05, 1.48):
    p.append(part((1.33, 0.04, 0.04), (0, y, -0.36), "galvanized"))
    p.append(part((1.33, 0.04, 0.04), (0, y, 0.36), "galvanized"))
    p.append(part((0.04, 0.04, 0.76), (-0.64, y, 0), "galvanized"))
    p.append(part((0.04, 0.04, 0.76), (0.64, y, 0), "galvanized"))
p.append(part((1.3, 0.04, 0.74), (0, 0.03, 0), "galvanized"))
p.append(part((0.3, 0.3, 0.014), (0, 1.25, 0.372), "yellow", rotation=[0, 0, 45]))
write(GLOBAL, "GasBottleCage.json", {
    "name": "Gas Bottle Cage",
    "category": "Volatile",
    "description": "A galvanized cage of eight gas cylinders. One hit too many and the whole cage goes up after a second.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [1.34, 1.5, 0.78]}],
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "breakable": {"health": 150, "breakBy": "all", "sound": "metal",
                  "debris": {"count": 16, "size": [0.06, 0.3], "speed": 8, "colors": ["#6e1a1a", "#1f4f2c", "galvanized"]},
                  "explode": {"radius": 7, "damage": 150, "fuse": 1.0}},
    "parts": p,
})

# Flammables cabinet: a yellow safety cabinet
p = [part((1.1, 1.72, 0.55), (0, 0.88, 0), "yellow", material="metal"),
     part((1.12, 0.04, 0.57), (0, 1.76, 0), "#c9a312"),
     part((1.0, 0.06, 0.5), (0, 0.03, 0), "darkgrey"),
     part((0.008, 1.5, 0.01), (0, 0.9, 0.28), "#8a7010"),
     part((0.4, 0.14, 0.01), (0, 1.45, 0.281), "#c8161d"),
     part((0.32, 0.05, 0.012), (0, 1.45, 0.283), "white"),
     part((0.18, 0.18, 0.012), (-0.27, 1.12, 0.281), "#c8161d", rotation=[0, 0, 45]),
     part((0.18, 0.18, 0.012), (0.27, 1.12, 0.281), "#c8161d", rotation=[0, 0, 45]),
     part((0.03, 0.18, 0.03), (-0.06, 0.95, 0.29), "black"),
     part((0.03, 0.18, 0.03), (0.06, 0.95, 0.29), "black"),
     part((0.08, 0.06, 0.08), (0.4, 1.8, -0.15), "darkgrey")]
write(GLOBAL, "FlammablesCabinet.json", {
    "name": "Flammables Cabinet",
    "category": "Volatile",
    "description": "A yellow FLAMMABLE safety cabinet full of solvent tins (front faces +Z). Takes a beating, then burns for 1.2 s and blows its doors off.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [1.1, 1.78, 0.56]}],
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "breakable": {"health": 220, "breakBy": "all", "sound": "metal",
                  "debris": {"count": 14, "size": [0.06, 0.3], "speed": 7, "colors": ["yellow", "red", "darkgrey"]},
                  "explode": {"radius": 6, "damage": 130, "fuse": 1.2}},
    "parts": p,
})

# Chemical drum: blue plastic, breaks, doesn't explode
p = []
p += octagon(0.56, 0.9, 0.46, "#1f5fa8")
for y in (0.3, 0.62):
    p += octagon(0.585, 0.04, y, "#1a5296")
p += octagon(0.5, 0.02, 0.92, "#1a5296")
p.append(part((0.09, 0.04, 0.09), (0.14, 0.94, 0.08), "white"))
p.append(part((0.07, 0.04, 0.07), (-0.13, 0.94, -0.1), "white"))
p.append(part((0.24, 0.24, 0.012), (0, 0.48, 0.282), "white"))
p.append(part((0.14, 0.14, 0.014), (0, 0.48, 0.285), "black", rotation=[0, 0, 45]))
write(GLOBAL, "ChemicalDrum.json", {
    "name": "Chemical Drum",
    "category": "Volatile",
    "description": "A blue plastic chemical drum with a corrosive label. It splits when shot but doesn't burn.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [0.58, 0.94, 0.58]}],
    "scatter": {"randomYaw": True, "scale": [0.97, 1.03]},
    "breakable": {"health": 55, "breakBy": "all", "sound": "soft",
                  "debris": {"count": 10, "size": [0.06, 0.2], "speed": 5, "colors": ["#1f5fa8", "white"]}},
    "parts": p,
})

# IBC tote: a white tank in a galvanized cage on a pallet
p = [part((1.2, 0.15, 1.0), (0, 0.075, 0), "darkgrey"),
     part((1.1, 0.92, 0.92), (0, 0.62, 0), "#d8d8cc"),
     part((0.3, 0.06, 0.3), (0, 1.11, 0), "white"),
     part((0.14, 0.14, 0.12), (0, 0.28, 0.5), "darkgrey")]
for x in (-0.58, -0.29, 0.0, 0.29, 0.58):
    p.append(part((0.03, 1.0, 0.03), (x, 0.65, 0.48), "galvanized"))
    p.append(part((0.03, 1.0, 0.03), (x, 0.65, -0.48), "galvanized"))
for z in (-0.24, 0.0, 0.24):
    p.append(part((0.03, 1.0, 0.03), (0.58, 0.65, z), "galvanized"))
    p.append(part((0.03, 1.0, 0.03), (-0.58, 0.65, z), "galvanized"))
for y in (0.4, 0.75, 1.13):
    p.append(part((1.19, 0.03, 0.03), (0, y, 0.48), "galvanized"))
    p.append(part((1.19, 0.03, 0.03), (0, y, -0.48), "galvanized"))
    p.append(part((0.03, 0.03, 0.99), (0.58, y, 0), "galvanized"))
    p.append(part((0.03, 0.03, 0.99), (-0.58, y, 0), "galvanized"))
write(GLOBAL, "IbcTote.json", {
    "name": "IBC Tote",
    "category": "Volatile",
    "description": "A 1,000-litre intermediate bulk container: a white tank in a galvanized cage on a steel pallet. Bullets punch it out; it doesn't burn.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [1.2, 1.15, 1.0]}],
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "breakable": {"health": 180, "breakBy": "all", "sound": "metal",
                  "debris": {"count": 14, "size": [0.06, 0.3], "speed": 5, "colors": ["#d8d8cc", "galvanized"]}},
    "parts": p,
})

# ===================================================================================== Maps/Cluttered/Props/PowderKeg (pack-local)
UPRIGHT = "#2a4f8f"
BEAM = "#e06a12"
LEVELS = (1.5, 3.0)        # beam tops of the two upper levels (the floor level stands on the ground)
TOP = 4.4


def rack_frame():
    ps = []
    for x in (-1.36, 1.36):
        for z in (-0.5, 0.5):
            ps.append(part((0.09, TOP, 0.08), (x, TOP / 2, z), UPRIGHT, material="metal"))
        ps.append(part((0.04, 0.04, 1.0), (x, 0.6, 0), UPRIGHT))
        ps.append(part((0.04, 0.04, 1.0), (x, 2.4, 0), UPRIGHT))
        ps.append(part((0.04, 0.04, 1.0), (x, 3.9, 0), UPRIGHT))
    for y in LEVELS + (TOP - 0.04,):
        ps.append(part((2.8, 0.12, 0.06), (0, y - 0.06, 0.52), BEAM, material="metal"))
        ps.append(part((2.8, 0.12, 0.06), (0, y - 0.06, -0.52), BEAM, material="metal"))
    for y in LEVELS:
        ps.append(part((2.64, 0.025, 1.0), (0, y - 0.01, 0), "galvanized"))
    return ps


def cartons(x, y, w, h, d, color, rows=2):
    """A pallet of cartons: the pallet, a stack of boxes with tape seams."""
    ps = [part((w, 0.14, d), (x, y + 0.07, 0), "pallet", material="wood"),
          part((w - 0.04, h, d - 0.04), (x, y + 0.14 + h / 2, 0), color)]
    for k in range(1, rows):
        ps.append(part((w - 0.02, 0.012, d - 0.02), (x, y + 0.14 + h * k / rows, 0), "tape"))
    ps.append(part((0.012, h, d - 0.03), (x, y + 0.14 + h / 2, 0), "tape"))
    return ps


def wrapped(x, y, w, h, d, color="#cfd6d8"):
    return [part((w, 0.14, d), (x, y + 0.07, 0), "pallet", material="wood"),
            part((w - 0.02, h, d - 0.02), (x, y + 0.14 + h / 2, 0), color),
            part((w, 0.1, d), (x, y + 0.14 + h * 0.75, 0), "#e6eaec")]


rack = rack_frame()
rack += cartons(-0.68, 0.0, 1.2, 1.05, 0.9, "cardboard", 3) + cartons(0.68, 0.0, 1.2, 0.9, 0.9, "#b08a5a", 2)
rack += cartons(-0.68, LEVELS[0], 1.2, 1.0, 0.9, "#9c7a4e", 2) + wrapped(0.68, LEVELS[0], 1.2, 1.05, 0.9)
rack += cartons(-0.68, LEVELS[1], 1.2, 0.95, 0.9, "cardboard", 2) + cartons(0.68, LEVELS[1], 1.2, 1.1, 0.9, "#a88456", 3)
write(PACK, "PalletRackingBay.json", {
    "name": "Pallet Racking Bay",
    "category": "Warehouse",
    "description": "One 2.8 m bay of blue-and-orange pallet racking, 4.4 m tall, three levels loaded with cartons (long side along X). Solid cover: bullets stop in the stock.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [2.82, 4.4, 1.1]}],
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "parts": rack,
})

rack = rack_frame()
rack += wrapped(-0.68, 0.0, 1.2, 1.2, 0.9, "#c4ccd0") + wrapped(0.68, 0.0, 1.2, 1.0, 0.9, "#d9b38a")
rack += wrapped(-0.68, LEVELS[0], 1.2, 1.1, 0.9, "#d6d9c8") + cartons(0.68, LEVELS[0], 1.2, 1.0, 0.9, "cardboard", 2)
rack += wrapped(-0.68, LEVELS[1], 1.2, 1.0, 0.9) + wrapped(0.68, LEVELS[1], 1.2, 1.1, 0.9, "#c9b8a0")
write(PACK, "PalletRackingBayWrapped.json", {
    "name": "Pallet Racking Bay Wrapped",
    "category": "Warehouse",
    "description": "A pallet racking bay loaded with shrink-wrapped pallets (long side along X). Solid cover.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [2.82, 4.4, 1.1]}],
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "parts": rack,
})

rack = rack_frame()
rack += cartons(0.68, LEVELS[1], 1.2, 0.8, 0.9, "cardboard", 2)
rack += [part((1.2, 0.14, 0.9), (-0.68, 0.07, 0), "pallet", material="wood")]
cols = [{"size": [0.1, TOP, 0.09], "offset": [x, TOP / 2, z]} for x in (-1.36, 1.36) for z in (-0.5, 0.5)]
cols += [{"size": [2.82, 0.12, 1.1], "offset": [0, y - 0.06, 0]} for y in LEVELS]
cols += [{"size": [1.2, 0.94, 0.9], "offset": [0.68, LEVELS[1] + 0.47, 0]}]
write(PACK, "PalletRackingBayOpen.json", {
    "name": "Pallet Racking Bay Open",
    "category": "Warehouse",
    "description": "A mostly empty pallet racking bay (long side along X): you can see and shoot through it. Things put on its shelves stand at y = 1.5 and 3.0.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": cols,
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "parts": rack,
})

# Catwalk safety railing: 3 m along X, top rail at 1.05 m, a mid rail and a yellow kick plate; only the top rail collides,
# so it keeps people (and bots) on the catwalk without being a wall bullets stop at.
p = []
for x in (-1.45, 0.0, 1.45):
    p.append(part((0.05, 1.08, 0.05), (x, 0.54, 0), "yellow", material="metal"))
p.append(part((3.0, 0.05, 0.05), (0, 1.06, 0), "yellow", material="metal"))
p.append(part((3.0, 0.04, 0.04), (0, 0.55, 0), "yellow"))
p.append(part((3.0, 0.12, 0.012), (0, 0.06, 0), "#c9a312"))
write(PACK, "SafetyRailing.json", {
    "name": "Safety Railing",
    "category": "Warehouse",
    "description": "A 3 m yellow catwalk railing along X (top rail at 1.05 m). Only the top rail is solid: it keeps you on the catwalk but bullets fly through.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [3.0, 0.12, 0.08], "offset": [0, 1.0, 0]}],
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "parts": p,
})

# Semi trailer: an 11.8 m box trailer along X, the rear doors at -X, landing legs at the front
L = 11.8
p = [part((L, 2.75, 2.5), (0, 1.15 + 1.375, 0), "#d9dadc", material="metal"),
     part((L + 0.02, 0.1, 2.52), (0, 3.92, 0), "lightgrey"),
     part((L, 0.18, 2.4), (0, 1.06, 0), "darkgrey"),
     part((7.0, 0.7, 0.03), (0.6, 0.62, 1.22), "lightgrey"),      # side skirts
     part((7.0, 0.7, 0.03), (0.6, 0.62, -1.22), "lightgrey"),
     part((0.03, 2.6, 1.22), (-L / 2 - 0.01, 2.5, 0.61), "#c8c9cb"),
     part((0.03, 2.6, 1.22), (-L / 2 - 0.01, 2.5, -0.61), "#c8c9cb"),
     part((0.05, 2.6, 0.05), (-L / 2 - 0.03, 2.5, 0), "darkgrey"),
     part((0.2, 0.25, 2.5), (-L / 2 + 0.1, 0.95, 0), "black"),
     part((0.04, 0.12, 0.4), (-L / 2 - 0.02, 1.1, 0.95), "red"),
     part((0.04, 0.12, 0.4), (-L / 2 - 0.02, 1.1, -0.95), "red"),
     part((6.0, 0.5, 0.012), (0.5, 3.1, 1.256), "#1f5fa8"),
     part((6.0, 0.5, 0.012), (0.5, 3.1, -1.256), "#1f5fa8")]
for x in (-L / 2 + 1.2, -L / 2 + 2.4):
    for z in (-0.85, 0.85):
        p.append(part((1.0, 1.0, 0.55), (x, 0.5, z), "rubber"))
        p.append(part((0.5, 0.5, 0.57), (x, 0.5, z), "galvanized"))
for z in (-0.8, 0.8):
    p.append(part((0.12, 1.0, 0.12), (L / 2 - 2.0, 0.5, z), "darkgrey"))
    p.append(part((0.3, 0.06, 0.3), (L / 2 - 2.0, 0.03, z), "darkgrey"))
write(PACK, "SemiTrailer.json", {
    "name": "Semi Trailer",
    "category": "Warehouse",
    "description": "An 11.8 m box trailer along X, rear doors at -X (back it onto a dock door), landing legs at the front. Unbreakable cover.",
    "author": "Call of Slop",
    "collision": "solid",
    "collider": [{"size": [L, 3.97, 2.52]}],
    "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
    "parts": p,
})

# Pallet jack
p = [part((0.18, 0.08, 1.15), (-0.2, 0.06, 0.1), "darkgrey"),
     part((0.18, 0.08, 1.15), (0.2, 0.06, 0.1), "darkgrey"),
     part((0.6, 0.35, 0.22), (0, 0.2, -0.55), "#c8161d", material="metal"),
     part((0.05, 1.0, 0.05), (0, 0.75, -0.7), "#c8161d", rotation=[-15, 0, 0]),
     part((0.36, 0.05, 0.05), (0, 1.23, -0.82), "black"),
     part((0.12, 0.12, 0.12), (0, 0.06, -0.55), "rubber")]
write(PACK, "PalletJack.json", {
    "name": "Pallet Jack",
    "category": "Warehouse",
    "description": "A red hand pallet jack, forks along +Z.",
    "author": "Call of Slop",
    "collision": "cosmetic",
    "scatter": {"randomYaw": True, "scale": [1, 1]},
    "fadeDistance": 60,
    "parts": p,
})

write(PACK, "DockBumper.json", {
    "name": "Dock Bumper",
    "category": "Warehouse",
    "description": "A black rubber dock bumper for the wall beside a dock door (back at -Z).",
    "author": "Call of Slop",
    "collision": "cosmetic",
    "fadeDistance": 60,
    "parts": [part((0.25, 0.5, 0.12), (0, 0.25, 0.06), "rubber"), part((0.29, 0.04, 0.02), (0, 0.48, 0.01), "darkgrey")],
})

write(PACK, "HazardPlacard.json", {
    "name": "Hazard Placard",
    "category": "Warehouse",
    "description": "A red FLAMMABLE diamond placard for a wall (back at -Z, lift it with y).",
    "author": "Call of Slop",
    "collision": "cosmetic",
    "fadeDistance": 50,
    "parts": [part((0.5, 0.5, 0.012), (0, 0.36, 0.006), "white", rotation=[0, 0, 45]),
              part((0.44, 0.44, 0.014), (0, 0.36, 0.012), "#c8161d", rotation=[0, 0, 45]),
              part((0.08, 0.16, 0.016), (0, 0.38, 0.02), "white"),
              part((0.24, 0.035, 0.016), (0, 0.2, 0.02), "white")],
})

# High-bay light: hangs from the roof on two wires (place it with y so the lamp is under the roof), steady glow,
# one in a few flickers (the "Flickering" variant).
def high_bay(flicker):
    lamp = part((0.6, 0.06, 0.6), (0, 0.04, 0), "#fff4d8",
                anim={"type": "flicker", "rate": 6, "duty": 0.82, "color": "#fff4d8", "off": "#4a463c", "glow": 1.2} if flicker
                else {"type": "pulse", "period": 9.0, "color": "#fff6e0", "off": "#efe3c4", "glow": 1.1})
    return [part((0.7, 0.18, 0.7), (0, 0.16, 0), "darkgrey", material="metal"),
            part((0.3, 0.2, 0.3), (0, 0.34, 0), "darkgrey"),
            part((0.02, 1.0, 0.02), (0, 0.94, 0.0), "black"),
            lamp]


write(PACK, "HighBayLight.json", {
    "name": "High Bay Light",
    "category": "Warehouse",
    "description": "A warehouse high-bay lamp on a wire (the lamp is at its foot: give it y = roof height - 1.4). Glows warm white.",
    "author": "Call of Slop",
    "collision": "cosmetic",
    "fadeDistance": 120,
    "parts": high_bay(False),
})
write(PACK, "HighBayLightFlickering.json", {
    "name": "High Bay Light Flickering",
    "category": "Warehouse",
    "description": "A high-bay lamp with a dying tube: mostly on, with random dips.",
    "author": "Call of Slop",
    "collision": "cosmetic",
    "fadeDistance": 120,
    "parts": high_bay(True),
})
print("wrote", GLOBAL, "and", PACK)
