#!/usr/bin/env python3
"""Writes two general London buildings for the World Tour's London map (and any UK map):

  Buildings/RailwayArches/      "Railway Arches": a 40 m section of a Victorian brick railway viaduct, five 8 m arch bays
                                 (a stair bay up to the track deck, an arch bar, an open through-arch, a garage and a
                                 workshop) under a flat deck with parapets you can fight along.
  Buildings/BayFrontedTerrace/  "Bay Fronted Terrace": three Victorian two-storey terraced houses with ground-floor bay
                                 windows, front garden walls and back yards.

Run from the repository root:  python3 tools/world/london_buildings.py
then check with:               python3 tools/buildings/buildings.py validate "Railway Arches" "Bay Fronted Terrace"
"""
import json
import math
import os


def R(v):
    return round(v, 3)


def block(x0, y0, z0, x1, y1, z1, color):
    return {"type": "block", "min": [R(x0), R(y0), R(z0)], "max": [R(x1), R(y1), R(z1)], "color": color}


def write(folder, d, rules=None):
    os.makedirs(os.path.join("Buildings", folder), exist_ok=True)
    with open(os.path.join("Buildings", folder, "building.json"), "w") as f:
        json.dump(d, f, indent=1)
        f.write("\n")
    for name, rule in (rules or {}).items():
        os.makedirs(os.path.join("Buildings", folder, "Furnishing"), exist_ok=True)
        with open(os.path.join("Buildings", folder, "Furnishing", name + ".json"), "w") as f:
            json.dump(rule, f, indent=1)
            f.write("\n")
    print(folder, "written")


# ============================================================================================ Railway Arches

ARCH_STOREY = 6.5          # ground floor 0.1 + 6.5 = deck (roof top) at 6.6 m above the ground
DECK = 0.1 + ARCH_STOREY
BAYS = [-20, -12, -4, 4, 12, 20]   # bay boundaries (pier centre lines) along local X
HALF_D = 4.5


def railway_arches():
    T = 0.6
    face = HALF_D + T / 2          # outer face of the long walls
    brick = {"color": "$wall", "material": "brick"}
    arch = {"color": "$arch", "material": "brick"}
    rooms = [
        {"id": "stairs", "type": "stairhall", "rect": [-20, -HALF_D, -12, HALF_D], "floor": {"color": "#6a6a66", "material": "concrete"}},
        {"id": "bar", "type": "archbar", "rect": [-12, -HALF_D, -4, HALF_D], "floor": {"color": "#5a3e2a", "material": "wood"}},
        {"id": "passage", "type": "passage", "rect": [-4, -HALF_D, 4, HALF_D], "floor": {"color": "#5e5c58", "material": "concrete"}},
        {"id": "garage", "type": "garage", "rect": [4, -HALF_D, 12, HALF_D], "floor": {"color": "#605e5a", "material": "concrete"}},
        {"id": "workshop", "type": "industrial-workshop", "rect": [12, -HALF_D, 20, HALF_D], "floor": {"color": "#625f5a", "material": "concrete"}},
    ]
    openings = [
        # stair bay: an arch from the end (a station hall or a yard) and a wide doorway to the front lane
        {"type": "arch", "at": [-20, 0.6], "width": 2.6, "head": 3.4},
        {"type": "arch", "at": [-15.6, HALF_D], "width": 2.0, "head": 3.2, "front": True},
        {"type": "door", "at": [-12, 2.2], "width": 1.0, "material": "wood", "color": "$door"},
        # arch bar: a glazed shopfront and a back door
        {"type": "door", "at": [-9.6, HALF_D], "width": 1.2, "material": "glass", "health": 60},
        {"type": "window", "at": [-6.2, HALF_D], "width": 2.6, "sill": 0.6, "head": 3.0, "health": 20},
        {"type": "door", "at": [-6.0, -HALF_D], "width": 1.0, "material": "metal", "color": "$door"},
        {"type": "window", "at": [-9.4, -HALF_D], "width": 1.6, "sill": 1.5, "head": 2.6},
        # the open through-arch, with side doors into the bar and the garage
        {"type": "door", "at": [-4, -2.6], "width": 1.0, "material": "wood"},
        {"type": "door", "at": [4, -2.6], "width": 1.0, "material": "metal", "color": "$door"},
        {"type": "arch", "at": [0, HALF_D], "width": 6.0, "head": 4.6},
        {"type": "arch", "at": [0, -HALF_D], "width": 6.0, "head": 4.6},
        # garage: a wide steel door, a back door, a door through to the workshop
        {"type": "door", "at": [8.2, HALF_D], "width": 3.0, "material": "metal", "color": "$shutter", "open": True},
        {"type": "door", "at": [10.0, -HALF_D], "width": 1.0, "material": "metal", "color": "$door"},
        {"type": "window", "at": [6.2, -HALF_D], "width": 1.4, "sill": 1.6, "head": 2.6},
        {"type": "door", "at": [12, -1.6], "width": 1.0, "material": "wood"},
        # workshop: a door and a window to the front, a back door
        {"type": "door", "at": [14.4, HALF_D], "width": 1.2, "material": "metal", "color": "$door"},
        {"type": "window", "at": [17.6, HALF_D], "width": 2.0, "sill": 1.0, "head": 2.8, "health": 10},
        {"type": "door", "at": [16.0, -HALF_D], "width": 1.0, "material": "metal", "color": "$door"},
    ]
    stairs = [{"floor": 0, "rect": [-18.7, -4.05, -13.6, -2.85], "dir": "+x", "color": {"color": "#5c5a56", "material": "concrete"}}]

    extras = []
    # deck parapets along both long sides (the ends stay open onto the next span or the embankment), with a coping
    for s in (1, -1):
        z0, z1 = sorted((s * (HALF_D - T / 2), s * face))
        extras.append(block(-20.3, DECK, z0, 20.3, DECK + 1.1, z1, brick))
        extras.append(block(-20.3, DECK + 1.1, min(z0, z1) - 0.05, 20.3, DECK + 1.2, max(z0, z1) + 0.05, "$coping"))
        # cornice (string course) under the deck, standing out of the face
        zc0, zc1 = sorted((s * face, s * (face + 0.22)))
        extras.append(block(-20.3, DECK - 0.55, zc0, 20.3, DECK - 0.05, zc1, "$coping"))
    # piers (pilasters) at each bay line and the arch spandrels: stepped blocks that stand out of the face, so the
    # rectangular openings read as round-headed arches
    for s in (1, -1):
        z0, z1 = sorted((s * face, s * (face + 0.16)))
        for x in BAYS:
            extras.append(block(x - 0.55, 0, z0, x + 0.55, DECK - 0.55, z1, brick))
        zs0, zs1 = sorted((s * face, s * (face + 0.1)))
        for i in range(len(BAYS) - 1):
            cx = (BAYS[i] + BAYS[i + 1]) / 2
            r, spring = 3.45, 2.5
            y = spring + 0.6
            while y < spring + r - 0.05:
                y1 = min(y + 0.45, DECK - 0.55)
                w = math.sqrt(max(0.0, r * r - ((y + y1) / 2 - spring) ** 2))
                if 3.45 - w > 0.08:
                    extras.append(block(cx - 3.45, y, zs0, cx - w, y1, zs1, arch))
                    extras.append(block(cx + w, y, zs0, cx + 3.45, y1, zs1, arch))
                y = y1
            extras.append(block(cx - 3.45, spring + r - 0.05, zs0, cx + 3.45, DECK - 0.55, zs1, arch))
            # a keystone
            zk0, zk1 = sorted((s * face, s * (face + 0.2)))
            extras.append(block(cx - 0.3, spring + r - 0.45, zk0, cx + 0.3, spring + r + 0.15, zk1, "$coping"))
    # outside dressing: bar sign board and lamps, garage oil drums, workshop sign
    extras += [
        block(-11.4, 3.3, face, -4.6, 3.9, face + 0.12, "$sign"),
        block(4.6, 3.6, face, 11.8, 4.1, face + 0.12, "$sign"),
        {"type": "prop", "name": "Hanging Lantern", "at": [-10.8, 3.1, face + 0.3]},
        {"type": "prop", "name": "Hanging Lantern", "at": [-5.0, 3.1, face + 0.3]},
        {"type": "prop", "name": "Tyre Stack", "at": [5.4, face + 0.7]},
        {"type": "prop", "name": "Oil Drum", "at": [11.0, face + 0.55]},
        {"type": "prop", "name": "Electric Meter", "at": [19.0, 0.9, face]},
        {"type": "prop", "name": "Sandwich Board", "at": [-11.4, face + 0.8], "yaw": 20},
        {"type": "prop", "name": "Wheelie Bin", "at": [-7.8, -face - 0.5], "yaw": 180},
        {"type": "prop", "name": "Wheelie Bin Grey", "at": [-8.6, -face - 0.5], "yaw": 180},
    ]
    d = {
        "format": "call-of-slop-building/1",
        "name": "Railway Arches",
        "theme": "A Victorian London brick railway viaduct: sooty stock brick piers and arches, stone keystones, the arches let as a bar, a garage and workshops, ballast and rails on the deck",
        "category": "special",
        "description": "A 40 m viaduct section of five 8 m arch bays: a stair bay up to the track deck, an arch bar, an open through-arch, a garage and a workshop. The deck has parapets along both sides and open ends, so sections and bridges join up.",
        "author": "Call of Slop",
        "seed": 508,
        "wallThickness": T,
        "storeyHeight": ARCH_STOREY,
        "doorHeight": 2.3,
        "exterior": brick,
        "interior": {"color": "$inner", "material": "brick"},
        "floor": {"color": "#5e5c58", "material": "concrete"},
        "ceiling": "$vault",
        "trim": "$coping",
        "door": "$door",
        "palettes": {
            "london stock": {"wall": "#857256", "arch": "#9a8566", "inner": "#7a6a52", "vault": "#4a443c", "coping": "#b8b0a0",
                             "door": "#2a4a3a", "shutter": "#5a6a70", "sign": "#1e2a3a", "deck": "#4a4844"},
            "engineering red": {"wall": "#6a2e26", "arch": "#7e3a2e", "inner": "#6a3a30", "vault": "#3a2e2a", "coping": "#a8a49a",
                                "door": "#1e2a44", "shutter": "#6a6a66", "sign": "#3a1a1a", "deck": "#454240"},
        },
        "floors": [{"rooms": rooms, "openings": openings}],
        "stairs": stairs,
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "$deck", "material": "dirt"}},
        "extras": extras,
        "anchors": [
            {"type": "point", "name": "deck centre", "floor": 0, "at": [0, 0]},
            {"type": "spawn", "name": "workshop", "at": [16, -1]},
            {"type": "spawn", "name": "bar", "at": [-8, -2.2]},
        ],
    }
    archbar = {
        "format": "call-of-slop-furnishing/1",
        "type": "archbar",
        "description": "A craft-beer bar in a railway arch: a long counter with stools, tables and chairs, a fridge, kegs and bottles.",
        "items": [
            {"id": "bar", "props": ["Kitchen Counter"], "place": "wall", "prefer": "far", "count": [2, 3]},
            {"props": ["Bottles", "Coffee Maker"], "place": "on", "of": "bar", "y": 0.9},
            {"props": ["Bar Stool"], "place": "front", "of": "bar", "distance": 0.3},
            {"props": ["Fridge"], "place": "wall", "chance": 0.9},
            {"id": "t", "props": ["Table"], "place": "floor", "count": [2, 3], "gap": 0.9},
            {"props": ["Wooden Chair", "Plastic Chair"], "place": "around", "of": "t", "count": [2, 3]},
            {"props": ["Oil Drum", "Pallet"], "place": "corner", "count": [1, 2]},
            {"props": ["Ceiling Fan"], "place": "ceiling", "chance": 0.5},
            {"props": ["Bottles", "Newspaper", "Cardboard Box Small"], "place": "floor", "count": [1, 3]},
        ],
    }
    write("RailwayArches", d, {"archbar": archbar})


# ============================================================================================ Bay Fronted Terrace

def bay_terrace():
    S1 = 3.0      # ground floor storey
    S2 = 2.9
    rooms0, rooms1, open0 = [], [], []
    doors = ["$door", "#1e3a6a", "#2f5d3a"]
    stairs = []
    extras = []
    for i, cx in enumerate((-5.0, 0.0, 5.0)):
        n = i + 1
        rooms0.append({"id": f"front{n}", "type": "living", "rects": [[cx - 2.5, 0, cx + 2.5, 5], [cx - 1.8, 5, cx + 0.8, 5.9]]})
        rooms0.append({"id": f"kitchen{n}", "type": "kitchen", "rect": [cx - 2.5, -5, cx + 2.5, 0], "floor": {"color": "#a89a88", "material": "tiles"},
                       "wall": "#e4dfd0"})
        rooms1.append({"id": f"bedroom{n}", "type": "bedroom", "rect": [cx - 2.5, -5, cx + 2.5, 5], "wall": "$paper"})
        open0 += [
            {"type": "door", "at": [cx + 1.75, 5], "width": 0.95, "color": doors[i], **({"front": True} if n == 2 else {})},
            {"type": "window", "at": [cx - 0.5, 5.9], "width": 1.8, "sill": 0.6, "head": 2.45},
            {"type": "window", "at": [cx - 1.8, 5.45], "width": 0.5, "sill": 0.6, "head": 2.45},
            {"type": "window", "at": [cx + 0.8, 5.45], "width": 0.5, "sill": 0.6, "head": 2.45},
            {"type": "door", "at": [cx + 1.85, 0], "width": 0.9},
            {"type": "door", "at": [cx + 1.85, -5], "width": 0.9, "color": doors[i]},
            {"type": "window", "at": [cx - 0.9, -5], "width": 1.2, "sill": 1.0, "head": 2.2},
        ]
        if n < 3:  # the kitchens were knocked through when the terrace became one let: a door in each party wall
            open0.append({"type": "door", "at": [cx + 2.5, -3.0], "width": 0.9})
        stairs.append({"floor": 0, "rect": [cx - 1.45, -1.15, cx + 0.95, -0.15], "dir": "-x", "color": {"color": "$floor", "material": "wood"}})
        # bay trim (stone sill and lintel bands), front garden wall with a gate gap, bins, yard walls
        trim = "$trim"
        for y0, y1 in ((0.5, 0.62), (2.45, 2.62)):
            extras.append(block(cx - 1.95, y0, 5.9, cx + 0.95, y1, 6.05, trim))
            extras.append(block(cx - 1.95, y0, 5.0, cx - 1.8 - 0.125, y1, 6.05, trim))
            extras.append(block(cx + 0.8 + 0.125, y0, 5.0, cx + 0.95, y1, 6.05, trim))
        extras.append(block(cx - 2.5, 0, 7.3, cx + 1.15, 0.7, 7.5, {"color": "$wall", "material": "brick"}))
        extras.append(block(cx + 2.35, 0, 7.3, cx + 2.5, 0.7, 7.5, {"color": "$wall", "material": "brick"}))
        extras.append(block(cx - 2.5, 0.7, 7.27, cx + 1.15, 0.78, 7.53, trim))
        extras.append({"type": "prop", "name": "Wheelie Bin" if i % 2 else "Wheelie Bin Grey", "at": [cx - 0.6, 6.65], "yaw": 0})
        extras.append({"type": "prop", "name": "Flower Pot", "at": [cx - 2.1, 6.9]})
        # back yard: party fences and a back wall with a gate gap
        extras.append(block(cx + 2.4, 0, -10.0, cx + 2.6, 1.6, -5.13, {"color": "$wall", "material": "brick"}))
        extras.append(block(cx - 2.5, 0, -10.1, cx + 1.3, 1.6, -9.9, {"color": "$wall", "material": "brick"}))
        extras.append({"type": "prop", "name": ["Laundry Line", "Garden Shed", "Compost Bin"][i], "at": [cx - 1.2, -8.2] if i != 1 else [cx - 1.4, -8.5],
                       **({"yaw": 0} if i != 0 else {"yaw": 90})})
        # chimney stacks on the party walls
    extras.append(block(-7.6, 0, -10.0, -7.4, 1.6, -5.13, {"color": "$wall", "material": "brick"}))
    for x in (-7.5, -2.5, 2.5, 7.5):
        extras.append(block(x - 0.45, 8.2, -0.5, x + 0.45, 10.9, 0.5, {"color": "$wall", "material": "brick"}))
        extras.append(block(x - 0.5, 10.9, -0.55, x + 0.5, 11.05, 0.55, trim))
    extras.append({"type": "prop", "name": "TV Antenna", "at": [0.0, 0, 0.6], "onRoof": True})
    d = {
        "format": "call-of-slop-building/1",
        "name": "Bay Fronted Terrace",
        "theme": "A late-Victorian London terrace: yellow stock brick, stone-trimmed ground-floor bay windows, front garden walls with bins, slate roofs and chimney stacks, back yards with washing lines",
        "category": "residential",
        "description": "Three two-storey terraced houses, each with a bay-windowed front room, a kitchen with the stairs and a back door to its yard, and one big bedroom upstairs.",
        "author": "Call of Slop",
        "seed": 1890,
        "wallThickness": 0.25,
        "storeyHeight": S1,
        "exterior": {"color": "$wall", "material": "brick"},
        "interior": {"color": "$interior", "material": "none"},
        "floor": {"color": "$floor", "material": "wood"},
        "ceiling": "#efe8d8",
        "trim": "$trim",
        "door": "$door",
        "palettes": {
            "london stock": {"wall": "#a08a5e", "interior": "#dcd2b8", "paper": "#7a8a6a", "floor": "#5a3a24", "trim": "#e8e0cc", "door": "#7a1e1e", "roof": "#3a3e46"},
            "red brick": {"wall": "#8a3c2c", "interior": "#e0d4bc", "paper": "#8a6a4a", "floor": "#5a3420", "trim": "#e4dcc8", "door": "#141414", "roof": "#3a3a40"},
            "white stucco": {"wall": "#ddd6c6", "interior": "#e8e0cc", "paper": "#6a7a8a", "floor": "#5a3a24", "trim": "#f4f0e4", "door": "#1e3a5a", "roof": "#40444a"},
        },
        "floors": [
            {"autoWindows": {"spacing": 2.4, "width": 1.0, "sill": 0.9, "head": 2.3}, "rooms": rooms0, "openings": open0},
            {"height": S2, "autoWindows": {"spacing": 2.3, "width": 1.0, "sill": 1.1, "head": 2.45}, "rooms": rooms1, "openings": []},
        ],
        "stairs": stairs,
        "roof": {"type": "gable", "ridge": "x", "pitch": 36, "overhang": 0.3, "color": {"color": "$roof", "material": "tiles"}},
        "extras": extras,
        "anchors": [
            {"type": "spawn", "name": "No. 1 kitchen", "at": [-5.5, -3.4]},
            {"type": "spawn", "name": "No. 3 kitchen", "at": [4.5, -3.4]},
            {"type": "spawn", "name": "No. 2 bedroom", "floor": 1, "at": [0.0, 2.5]},
        ],
    }
    write("BayFrontedTerrace", d)


if __name__ == "__main__":
    railway_arches()
    bay_terrace()
