#!/usr/bin/env python3
"""Writes Props/Zombies/SlotMachines/*.json: one Reel Cabinet per weapon-builder part category (Zombies slot machines,
2026-10-26; docs/zombies/ZOMBIES-MODE.md, "Slot machines").

Run from the repository root:  python3 -I tools/zombies/make_slot_cabinets.py

Each cabinet is the classic "Reel Cabinet" (a prefab "use", so the game's reels, heat meter and topper lights line up on
it) with a category-coloured band and side stripes and its own silhouette on top: a barrel for barrels, a scope for
optics, cartridges for ammo... Original shapes only. The colours match Game.SlotCategoryColor.
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Props", "Zombies", "SlotMachines")

TOP = 1.94  # the classic cabinet's topper top


def b(size, offset, color, rotation=None, material=None):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    if rotation:
        p["rotation"] = rotation
    if material:
        p["material"] = material
    return p


GUN = "#3a3d42"     # gunmetal
STEEL = "#8a9099"
DARK = "#16181b"
BRASS = "#c8961c"
COPPER = "#b06a36"
WOOD = "#6b4524"
GLASS = "#5a9ad8"


def y(v):
    return TOP + v


# name (the category's display name), enum key, colour, what the topper shows, its boxes.
CABINETS = [
    ("Receiver", "#d94d40", "a rifle receiver block with its ejection port, rail and charging handle", [
        b([0.52, 0.22, 0.26], [0, y(0.11), 0], GUN, material="metal"),
        b([0.16, 0.07, 0.01], [0.06, y(0.13), -0.135], DARK),
        b([0.48, 0.03, 0.09], [0, y(0.235), 0], STEEL),
        b([0.05, 0.04, 0.14], [-0.2, y(0.19), -0.17], STEEL),
        b([0.11, 0.1, 0.13], [0.12, y(0.27), 0], "#d94d40"),
    ]),
    ("Barrel", "#8c99ad", "a long gun barrel with a muzzle ring and a cooling shroud, lying across the top", [
        b([0.98, 0.12, 0.12], [0, y(0.1), 0], GUN, material="metal"),
        b([0.98, 0.12, 0.12], [0, y(0.1), 0], GUN, rotation=[45, 0, 0], material="metal"),
        b([0.07, 0.2, 0.2], [0.5, y(0.1), 0], "#8c99ad"),
        b([0.4, 0.17, 0.17], [-0.2, y(0.1), 0], STEEL),
        b([0.05, 0.05, 0.01], [-0.32, y(0.1), -0.09], DARK),
        b([0.05, 0.05, 0.01], [-0.2, y(0.1), -0.09], DARK),
        b([0.05, 0.05, 0.01], [-0.08, y(0.1), -0.09], DARK),
        b([0.03, 0.08, 0.03], [0.42, y(0.2), 0], STEEL),
        b([0.08, 0.06, 0.08], [-0.3, y(0.02), 0], GUN),
        b([0.08, 0.06, 0.08], [0.3, y(0.02), 0], GUN),
    ]),
    ("Splitter", "#4dccbf", "a manifold block splitting into three tubes that fan out", [
        b([0.24, 0.2, 0.24], [0, y(0.1), 0], GUN, material="metal"),
        b([0.09, 0.3, 0.09], [0, y(0.34), 0], "#4dccbf"),
        b([0.09, 0.32, 0.09], [-0.15, y(0.3), 0], "#4dccbf", rotation=[0, 0, 35]),
        b([0.09, 0.32, 0.09], [0.15, y(0.3), 0], "#4dccbf", rotation=[0, 0, -35]),
        b([0.12, 0.04, 0.12], [0, y(0.5), 0], STEEL),
        b([0.12, 0.04, 0.12], [-0.25, y(0.44), 0], STEEL, rotation=[0, 0, 35]),
        b([0.12, 0.04, 0.12], [0.25, y(0.44), 0], STEEL, rotation=[0, 0, -35]),
    ]),
    ("Feed", "#d9a633", "a big curved box magazine standing on its base plate", [
        b([0.2, 0.04, 0.34], [0, y(0.02), 0], STEEL),
        b([0.15, 0.17, 0.3], [0, y(0.12), 0], GUN, material="metal"),
        b([0.15, 0.17, 0.3], [0.035, y(0.27), 0], GUN, rotation=[0, 0, -10], material="metal"),
        b([0.15, 0.17, 0.3], [0.1, y(0.42), 0], GUN, rotation=[0, 0, -20], material="metal"),
        b([0.11, 0.04, 0.22], [0.14, y(0.52), 0], "#d9a633", rotation=[0, 0, -20]),
        b([0.02, 0.32, 0.01], [-0.02, y(0.22), -0.155], "#d9a633"),
    ]),
    ("Ammo", "#f2bf4d", "three oversized cartridges standing up", [
        b([0.13, 0.3, 0.13], [-0.22, y(0.15), 0], BRASS, material="metal"),
        b([0.1, 0.12, 0.1], [-0.22, y(0.36), 0], COPPER),
        b([0.15, 0.03, 0.15], [-0.22, y(0.015), 0], "#a07818"),
        b([0.13, 0.36, 0.13], [0, y(0.18), 0], BRASS, material="metal"),
        b([0.1, 0.14, 0.1], [0, y(0.43), 0], COPPER),
        b([0.15, 0.03, 0.15], [0, y(0.015), 0], "#a07818"),
        b([0.13, 0.3, 0.13], [0.22, y(0.15), 0], BRASS, material="metal"),
        b([0.1, 0.12, 0.1], [0.22, y(0.36), 0], COPPER),
        b([0.15, 0.03, 0.15], [0.22, y(0.015), 0], "#a07818"),
    ]),
    ("Stock", "#9a6b40", "a skeleton rifle stock with its butt plate and cheek rest", [
        b([0.07, 0.34, 0.15], [0.36, y(0.17), 0], DARK),
        b([0.62, 0.05, 0.07], [0.04, y(0.3), 0], GUN, rotation=[0, 0, -6]),
        b([0.62, 0.05, 0.07], [0.04, y(0.05), 0], GUN, rotation=[0, 0, 6]),
        b([0.05, 0.25, 0.07], [-0.26, y(0.17), 0], GUN),
        b([0.26, 0.06, 0.12], [0.12, y(0.35), 0], "#9a6b40"),
    ]),
    ("Grip", "#73bf59", "a raked pistol grip with its trigger and guard", [
        b([0.44, 0.08, 0.13], [0, y(0.4), 0], GUN, material="metal"),
        b([0.15, 0.36, 0.13], [0.1, y(0.2), 0], "#73bf59", rotation=[0, 0, -15]),
        b([0.22, 0.03, 0.06], [-0.07, y(0.22), 0], GUN),
        b([0.03, 0.12, 0.06], [-0.17, y(0.29), 0], GUN),
        b([0.03, 0.08, 0.03], [-0.07, y(0.3), 0], STEEL, rotation=[0, 0, 15]),
    ]),
    ("Optic", "#59a6ff", "a rifle scope with its bells, turrets and rings", [
        b([0.07, 0.1, 0.08], [-0.16, y(0.05), 0], GUN),
        b([0.07, 0.1, 0.08], [0.16, y(0.05), 0], GUN),
        b([0.7, 0.1, 0.1], [0, y(0.16), 0], GUN, material="metal"),
        b([0.7, 0.1, 0.1], [0, y(0.16), 0], GUN, rotation=[45, 0, 0], material="metal"),
        b([0.12, 0.19, 0.19], [-0.36, y(0.16), 0], GUN),
        b([0.1, 0.16, 0.16], [0.35, y(0.16), 0], GUN),
        b([0.01, 0.14, 0.14], [-0.425, y(0.16), 0], GLASS),
        b([0.01, 0.11, 0.11], [0.405, y(0.16), 0], GLASS),
        b([0.07, 0.09, 0.07], [0, y(0.25), 0], "#59a6ff"),
        b([0.07, 0.07, 0.09], [0, y(0.16), -0.08], "#59a6ff"),
    ]),
    ("Muzzle", "#ff8033", "a ported muzzle brake on a short barrel stub", [
        b([0.3, 0.09, 0.09], [-0.3, y(0.12), 0], GUN),
        b([0.36, 0.2, 0.2], [0.06, y(0.12), 0], "#ff8033", material="metal"),
        b([0.05, 0.14, 0.01], [-0.04, y(0.12), -0.105], DARK),
        b([0.05, 0.14, 0.01], [0.06, y(0.12), -0.105], DARK),
        b([0.05, 0.14, 0.01], [0.16, y(0.12), -0.105], DARK),
        b([0.05, 0.01, 0.14], [0.06, y(0.225), 0], DARK),
        b([0.04, 0.22, 0.22], [0.26, y(0.12), 0], GUN),
        b([0.1, 0.03, 0.1], [0.06, y(0.01), 0], GUN),
    ]),
    ("Utility", "#ff4040", "a laser module on a side rail, its red beam pointing out", [
        b([0.62, 0.03, 0.09], [0, y(0.015), 0], STEEL),
        b([0.28, 0.15, 0.17], [-0.05, y(0.105), 0], GUN, material="metal"),
        b([0.03, 0.06, 0.06], [0.105, y(0.11), 0], "#ff4040"),
        b([0.24, 0.012, 0.012], [0.24, y(0.11), 0], "#ff4040"),
        b([0.06, 0.05, 0.05], [-0.12, y(0.2), 0], DARK),
        b([0.03, 0.12, 0.08], [0.24, y(0.07), 0], GUN),
    ]),
    ("Receiver Upgrade", "#bf80ff", "a big cog standing on a post", [
        b([0.07, 0.12, 0.07], [0, y(0.06), 0], GUN),
        b([0.32, 0.32, 0.06], [0, y(0.28), 0], "#bf80ff", material="metal"),
        b([0.32, 0.32, 0.06], [0, y(0.28), 0], "#bf80ff", rotation=[0, 0, 45], material="metal"),
        b([0.4, 0.07, 0.065], [0, y(0.28), 0], "#a066e0"),
        b([0.07, 0.4, 0.065], [0, y(0.28), 0], "#a066e0"),
        b([0.11, 0.11, 0.08], [0, y(0.28), 0], STEEL),
    ]),
    ("Damage Multiplier", "#ffd940", "a big cross mark between two power cells", [
        b([0.44, 0.08, 0.07], [0, y(0.24), 0], "#ffd940", rotation=[0, 0, 45]),
        b([0.44, 0.08, 0.07], [0, y(0.24), 0], "#ffd940", rotation=[0, 0, -45]),
        b([0.06, 0.1, 0.06], [0, y(0.05), 0], GUN),
        b([0.1, 0.22, 0.1], [-0.33, y(0.11), 0], GUN, material="metal"),
        b([0.05, 0.04, 0.05], [-0.33, y(0.24), 0], STEEL),
        b([0.1, 0.22, 0.1], [0.33, y(0.11), 0], GUN, material="metal"),
        b([0.05, 0.04, 0.05], [0.33, y(0.24), 0], STEEL),
        b([0.02, 0.12, 0.01], [-0.33, y(0.11), -0.055], "#ffd940"),
        b([0.02, 0.12, 0.01], [0.33, y(0.11), -0.055], "#ffd940"),
    ]),
    ("Hilt", "#b3804d", "an upright sword hilt: pommel, wrapped grip, cross guard and a blade stub", [
        b([0.1, 0.1, 0.1], [0, y(0.05), 0], BRASS),
        b([0.07, 0.24, 0.07], [0, y(0.22), 0], "#b3804d"),
        b([0.4, 0.05, 0.08], [0, y(0.365), 0], BRASS),
        b([0.09, 0.22, 0.025], [0, y(0.5), 0], STEEL),
    ]),
    ("Head", "#ccd9e6", "an axe head on a short slanted haft", [
        b([0.05, 0.56, 0.05], [0, y(0.26), 0], WOOD, rotation=[0, 0, 12]),
        b([0.24, 0.17, 0.045], [-0.12, y(0.44), 0], GUN, rotation=[0, 0, 12], material="metal"),
        b([0.04, 0.2, 0.05], [-0.25, y(0.42), 0], "#ccd9e6", rotation=[0, 0, 12]),
        b([0.08, 0.04, 0.08], [0.02, y(0.02), 0], STEEL),
    ]),
    ("Melee Mod", "#99e666", "a nail-studded club with a bright grip wrap", [
        b([0.11, 0.56, 0.11], [0, y(0.28), 0], WOOD, rotation=[0, 0, -18]),
        b([0.12, 0.14, 0.12], [-0.035, y(0.11), 0], "#99e666", rotation=[0, 0, -18]),
        b([0.12, 0.015, 0.015], [0.1, y(0.4), 0], STEEL, rotation=[0, 0, -18]),
        b([0.12, 0.015, 0.015], [0.05, y(0.47), -0.02], STEEL, rotation=[0, 30, -18]),
        b([0.015, 0.015, 0.14], [0.08, y(0.36), 0], STEEL),
        b([0.015, 0.015, 0.14], [0.12, y(0.46), 0], STEEL),
        b([0.12, 0.015, 0.015], [0.18, y(0.52), 0.02], STEEL, rotation=[0, -30, -18]),
    ]),
    ("Action", "#f27399", "a rifle bolt with its handle, a hammer and a coil spring", [
        b([0.5, 0.1, 0.1], [0, y(0.12), 0], GUN, material="metal"),
        b([0.5, 0.1, 0.1], [0, y(0.12), 0], GUN, rotation=[45, 0, 0], material="metal"),
        b([0.04, 0.04, 0.2], [0.12, y(0.12), -0.14], STEEL),
        b([0.08, 0.08, 0.08], [0.12, y(0.12), -0.25], "#f27399"),
        b([0.06, 0.16, 0.05], [-0.3, y(0.12), 0], "#f27399", rotation=[0, 0, 20]),
        b([0.02, 0.12, 0.12], [-0.38, y(0.3), 0], STEEL),
        b([0.02, 0.12, 0.12], [-0.3, y(0.3), 0], STEEL),
        b([0.02, 0.12, 0.12], [-0.22, y(0.3), 0], STEEL),
        b([0.02, 0.12, 0.12], [-0.14, y(0.3), 0], STEEL),
        b([0.26, 0.03, 0.03], [-0.26, y(0.3), 0], GUN),
        b([0.04, 0.12, 0.04], [-0.26, y(0.21), 0], GUN),
    ]),
]


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, colour, what, parts in CABINETS:
        top = max(p["offset"][1] + p["size"][1] / 2 for p in parts)
        prop = {
            "format": "call-of-slop-prop/1",
            "name": f"{name} Reel Cabinet",
            "category": "Zombies",
            "description": f"A Zombies slot machine that only pays {name.lower()} parts: the Reel Cabinet with a {name.lower()}-coloured band and, on top, {what}. The front faces -Z; the game draws the reels, the heat meter and the topper lights on it.",
            "collision": "solid",
            "collider": [{"size": [0.9, round(min(top, 2.6), 2), 0.7]}],
            "scatter": {"randomYaw": False, "yawStep": 90},
            "parts": [
                {"use": "Reel Cabinet", "pos": [0, 0, 0]},
                b([0.76, 0.07, 0.02], [0, 1.6, -0.272], colour),
                b([0.035, 0.9, 0.06], [-0.435, 1.05, -0.24], colour),
                b([0.035, 0.9, 0.06], [0.435, 1.05, -0.24], colour),
            ] + parts,
        }
        fn = name.replace(" ", "") + "ReelCabinet.json"
        with open(os.path.join(OUT, fn), "w") as f:
            json.dump(prop, f, indent=1)
            f.write("\n")
    print(f"wrote {len(CABINETS)} cabinets to {OUT}")


main()
