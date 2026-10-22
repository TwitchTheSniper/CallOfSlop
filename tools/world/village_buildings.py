#!/usr/bin/env python3
"""Writes the English-village buildings made for the World Tour pack's 07 Village (general buildings, any map can use them):

    Buildings/StoneCottage     "Stone Cottage": a Cotswold two-up two-down in honey limestone under a slate roof
    Buildings/ThatchedCottage  "Thatched Cottage": the same plan under a steep straw thatch, with a gabled porch
    Buildings/VillageShop      "Village Shop": the village stores and post office, with the shopkeeper's flat upstairs,
                               its own stocked shelves, post office counter, till, card and newspaper racks

Usage: python3 tools/world/village_buildings.py [repo root]
Then: python3 tools/buildings/buildings.py validate "Stone Cottage" "Thatched Cottage" "Village Shop"
"""
import json
import os
import random
import sys

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
B = os.path.join(REPO, "Buildings")


def dump(path, d):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        f.write(json.dumps(d, indent=2) + "\n")


def compact(path, d):
    """A prop file: one line per part, like the rest of the library."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    keys = list(d.keys())
    with open(path, "w") as f:
        f.write("{\n")
        for i, k in enumerate(keys):
            sep = "," if i < len(keys) - 1 else ""
            if k == "parts":
                f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(p) for p in d[k]) + "\n  ]" + sep + "\n")
            else:
                f.write(f"  {json.dumps(k)}: {json.dumps(d[k])}{sep}\n")
        f.write("}\n")


def R(v):
    return round(v, 4)


def bx(x0, y0, z0, x1, y1, z1, color, material=None):
    p = {"size": [R(x1 - x0), R(y1 - y0), R(z1 - z0)], "offset": [R((x0 + x1) / 2), R((y0 + y1) / 2), R((z0 + z1) / 2)], "color": color}
    if material:
        p["material"] = material
    return p


# ------------------------------------------------------------------------------------------------ the cottages
# Plan (9 x 7 m, front to +Z): a through hall with the stairs, the living room to the west and the kitchen to the east;
# upstairs the main bedroom (west), the landing, a small bedroom and the bathroom (east). Low ceilings, small windows.


def cottage(name, folder, theme, desc, palettes, roof, porch_roof, extras_more, seed, stack=9.6):
    walls = {"color": "$wall", "material": "brick"}
    d = {
        "format": "call-of-slop-building/1",
        "name": name,
        "theme": theme,
        "category": "residential",
        "description": desc,
        "author": "Call of Slop",
        "seed": seed,
        "wallThickness": 0.25,
        "storeyHeight": 2.7,
        "exterior": walls,
        "interior": {"color": "$interior", "material": "none"},
        "floor": {"color": "$floor", "material": "wood"},
        "ceiling": "$ceiling",
        "trim": "$trim",
        "door": "$door",
        "palettes": palettes,
        "floors": [
            {
                "autoWindows": {"spacing": 2.4, "width": 1.0, "sill": 0.9, "head": 2.0},
                "rooms": [
                    {"id": "hall", "type": "hall", "rect": [-1.2, -3.5, 1.2, 3.5], "floor": {"color": "#8a4a36", "material": "tiles"}},
                    {"id": "living", "type": "living", "rect": [-4.5, -3.5, -1.2, 3.5]},
                    {"id": "kitchen", "type": "kitchen", "rect": [1.2, -3.5, 4.5, 3.5], "floor": {"color": "#a8806a", "material": "tiles"}},
                ],
                "openings": [
                    {"type": "door", "at": [-0.4, 3.5], "width": 1.0, "front": True, "material": "wood"},
                    {"type": "door", "at": [-0.4, -3.5], "width": 0.9, "material": "wood"},
                    {"type": "door", "at": [-1.2, 1.8], "width": 0.9},
                    {"type": "door", "at": [1.2, 2.0], "width": 0.9, "hinge": "right"},
                    {"type": "door", "at": [4.5, -2.2], "width": 0.9, "material": "wood"},
                ],
            },
            {
                "height": 2.5,
                "autoWindows": {"spacing": 2.4, "width": 0.9, "sill": 0.7, "head": 1.8},
                "rooms": [
                    {"id": "bedroom", "type": "bedroom", "rect": [-4.5, -3.5, -1.2, 3.5]},
                    {"id": "landing", "type": "hall", "rect": [-1.2, -3.5, 1.2, 3.5], "windows": False},
                    {"id": "bathroom", "type": "bathroom", "rect": [1.2, -3.5, 4.5, -0.3],
                     "floor": {"color": "#c8d0cc", "material": "tiles"}, "wall": "#e4ece6",
                     "autoWindows": {"spacing": 3, "width": 0.7, "sill": 1.2, "head": 1.8}},
                    {"id": "bedroom2", "type": "kidsroom", "rect": [1.2, -0.3, 4.5, 3.5]},
                ],
                "openings": [
                    {"type": "door", "at": [-1.2, 1.6], "width": 0.9},
                    {"type": "door", "at": [1.2, -2.3], "width": 0.9, "hinge": "right"},
                    {"type": "door", "at": [1.2, 1.9], "width": 0.9},
                    {"type": "window", "at": [0.0, 3.5], "width": 0.8, "sill": 0.7, "head": 1.7},
                ],
            },
        ],
        "stairs": [{"floor": 0, "rect": [0.15, -1.75, 1.05, 0.5], "dir": "-z", "color": {"color": "#5a3e26", "material": "wood"}}],
        "roof": roof,
        "extras": [
            # stone chimney stacks on both gable ends, a stone step at the front door, a porch hood
            {"type": "block", "min": [-4.95, 0.0, -0.45], "max": [-4.38, stack, 0.45], "color": {"color": "$wall", "material": "brick"}},
            {"type": "block", "min": [-5.0, stack, -0.5], "max": [-4.33, stack + 0.15, 0.5], "color": "$trim"},
            {"type": "block", "min": [4.38, 0.0, -0.45], "max": [4.95, stack, 0.45], "color": {"color": "$wall", "material": "brick"}},
            {"type": "block", "min": [4.33, stack, -0.5], "max": [5.0, stack + 0.15, 0.5], "color": "$trim"},
            {"type": "block", "min": [-1.15, 0.0, 3.63], "max": [0.35, 0.14, 4.2], "color": {"color": "#a08a62", "material": "concrete"}},
            porch_roof,
            {"type": "block", "min": [-4.62, 0.0, 3.62], "max": [4.62, 0.3, 3.7], "color": {"color": "#8a744c", "material": "brick"}},   # plinth course
            {"type": "prop", "name": "Hanging Flower Basket", "at": [0.65, 1.95, 3.63]},
            {"type": "prop", "name": "Flower Pots", "at": [-2.6, 4.1], "yaw": 0},
            {"type": "prop", "name": "Water Butt", "at": [5.2, 1.6]},
            {"type": "prop", "name": "Wheelie Bin", "at": [2.0, -4.3], "yaw": 180},
        ] + extras_more,
        "anchors": [
            {"type": "spawn", "name": "living room", "floor": 0, "at": [-2.8, 0.0]},
            {"type": "spawn", "name": "kitchen", "floor": 0, "at": [2.8, -1.0]},
            {"type": "point", "name": "front bedroom window", "floor": 1, "at": [2.8, 2.6]},
        ],
    }
    dump(os.path.join(B, folder, "building.json"), d)


STONE_PAL = {
    "honey stone": {"wall": "#b89c66", "interior": "#ece2c8", "floor": "#6a4a30", "ceiling": "#f2ecdc", "trim": "#e8e2d0", "door": "#5a7a5e", "roof": "#3c4046"},
    "pale limestone": {"wall": "#c9bc98", "interior": "#e6e2d2", "floor": "#7a5a3a", "ceiling": "#f0ece0", "trim": "#ece8dc", "door": "#2a3a5a", "roof": "#45474c"},
    "weathered stone": {"wall": "#a48c60", "interior": "#e8dcc0", "floor": "#5a3e28", "ceiling": "#eee6d2", "trim": "#d8d0bc", "door": "#7a2a24", "roof": "#36383a"},
}
THATCH_PAL = {
    "honey stone": {"wall": "#bca06a", "interior": "#efe4c8", "floor": "#6a4a30", "ceiling": "#f2ecdc", "trim": "#5a4026", "door": "#2f4a3a", "roof": "#a88a52"},
    "old thatch": {"wall": "#a99060", "interior": "#e8ddc2", "floor": "#5a3e28", "ceiling": "#ece4d0", "trim": "#4a3420", "door": "#6a2a24", "roof": "#857048"},
    "limewash": {"wall": "#e4dcc8", "interior": "#f0e8d6", "floor": "#7a5a3a", "ceiling": "#f4efe4", "trim": "#3a2a1c", "door": "#3a4a6a", "roof": "#9c8250"},
}

cottage(
    "Stone Cottage", "StoneCottage",
    "Cotswold two-up two-down: honey limestone, a grey slate roof, stone chimney stacks, a sage front door, low beamed rooms",
    "A two-storey village cottage: a through hall with the stairs, the living room and kitchen; two bedrooms and a bathroom upstairs. First-floor windows overlook the lane.",
    STONE_PAL,
    {"type": "gable", "ridge": "x", "pitch": 42, "overhang": 0.25, "color": {"color": "$roof", "material": "tiles"}},
    {"type": "awning", "rect": [-1.35, 3.63, 0.55, 4.35], "height": 2.3, "color": "$trim"},
    [{"type": "prop", "name": "Bench", "at": [2.6, 4.25], "yaw": 0}],
    seed=27,
)
cottage(
    "Thatched Cottage", "ThatchedCottage",
    "Chocolate-box thatched cottage: stone walls under a deep straw thatch, a gabled oak porch, roses and a picket gate",
    "A two-storey thatched cottage: a through hall with the stairs, the living room and kitchen; two bedrooms and a bathroom under the thatch. First-floor windows peep out under the eaves.",
    THATCH_PAL,
    {"type": "gable", "ridge": "x", "pitch": 47, "overhang": 0.6, "color": {"color": "$roof", "material": "dirt"}},
    {"type": "porch", "rect": [-1.45, 3.63, 0.65, 4.85], "height": 2.3, "color": "$trim"},
    [{"type": "prop", "name": "Picket Fence", "at": [3.0, 6.2], "yaw": 0}, {"type": "prop", "name": "Garden Gnome", "at": [-3.4, 4.6], "yaw": 160}],
    seed=31, stack=10.6,
)

# ------------------------------------------------------------------------------------------------ the village shop props

GOODS = ["#c8282a", "#e8c43a", "#2a5aa8", "#3a8a3a", "#e87a2a", "#f0ece0", "#8a3a8a", "#2a2a2a", "#c8a070"]


def shop_prop(fname, name, desc, parts, collider, breakable, scatter=None):
    d = {"format": "call-of-slop-prop/1", "name": name, "description": desc, "collision": "solid", "collider": collider,
         "scatter": scatter or {"randomYaw": False, "yawStep": 90}, "breakable": breakable, "parts": parts}
    compact(os.path.join(B, "VillageShop", "Props", fname), d)


rnd = random.Random(14)
shelf = [bx(-0.9, 0.0, -0.22, 0.9, 1.9, -0.18, "#6a4a2e", "wood"),
         bx(-0.9, 0.0, -0.22, -0.86, 1.9, 0.22, "#6a4a2e", "wood"), bx(0.86, 0.0, -0.22, 0.9, 1.9, 0.22, "#6a4a2e", "wood"),
         bx(-0.9, 0.0, -0.18, 0.9, 0.12, 0.22, "#4a3420", "wood"), bx(-0.92, 1.88, -0.24, 0.92, 1.94, 0.24, "#4a3420", "wood")]
for k, y in enumerate((0.12, 0.5, 0.88, 1.26)):
    shelf.append(bx(-0.86, y - 0.025, -0.18, 0.86, y + 0.0, 0.22, "#7a5a3a", "wood"))
    x = -0.82
    while x < 0.78:                       # goods along each shelf: tins, packets, jars in rows
        w = rnd.choice((0.09, 0.11, 0.14, 0.18))
        h = rnd.choice((0.12, 0.16, 0.22, 0.28)) if k else rnd.choice((0.2, 0.26, 0.3))
        if x + w > 0.84:
            break
        c = rnd.choice(GOODS)
        shelf.append(bx(x, y, -0.12, x + w, y + h, 0.16, c))
        if rnd.random() < 0.4:            # a label band
            shelf.append(bx(x + 0.005, y + h * 0.4, 0.16, x + w - 0.005, y + h * 0.65, 0.165, rnd.choice(("#f0ece0", "#2a2a2a", "#e8c43a"))))
        x += w + rnd.uniform(0.01, 0.03)
shop_prop("VillageShopGroceryShelf.json", "Village Shop Grocery Shelf",
          "A wooden wall shelf stacked with tins, packets and jars.",
          shelf, [{"size": [1.84, 1.94, 0.48]}], {"health": 120, "breakBy": "all", "sound": "wood", "debris": {"count": 18, "size": [0.05, 0.3], "speed": 3.5}})

post = [bx(-1.2, 0.0, -0.3, 1.2, 1.0, 0.35, "#7a5432", "wood"), bx(-1.23, 1.0, -0.33, 1.23, 1.05, 0.38, "#4a2e1c"),
        bx(-1.2, 1.05, -0.06, 1.2, 2.1, -0.02, "#a8c4cc"),                                    # the glass screen
        bx(-1.22, 2.1, -0.08, 1.22, 2.18, 0.0, "#2a3a2a"), bx(-1.22, 1.05, -0.08, -1.16, 2.1, 0.0, "#2a3a2a"),
        bx(1.16, 1.05, -0.08, 1.22, 2.1, 0.0, "#2a3a2a"), bx(-0.02, 1.05, -0.08, 0.02, 2.1, 0.0, "#2a3a2a"),
        bx(-0.8, 1.05, -0.07, -0.3, 1.13, 0.2, "#2a2a2a"), bx(0.3, 1.05, -0.07, 0.8, 1.13, 0.2, "#2a2a2a"),   # the pass-through trays
        bx(-1.0, 1.05, -0.3, -0.6, 1.2, -0.1, "#c8c8c8", "metal"),                                # letter scales
        bx(0.5, 1.05, -0.28, 0.9, 1.1, -0.1, "#e8e0c8"),                                           # forms
        bx(-1.21, 0.3, 0.36, 1.21, 0.75, 0.37, "#c8282a"),                                         # a red front panel
        bx(-0.9, 2.2, -0.06, 0.9, 2.5, -0.02, "#c8282a"), bx(-0.6, 2.3, -0.02, 0.6, 2.4, -0.01, "#f0ece0")]   # header board (no words)
shop_prop("VillageShopPostCounter.json", "Village Shop Post Counter",
          "The post office counter: a wooden counter behind a glazed screen, with trays, scales and forms, under a red header board.",
          post, [{"size": [2.46, 1.05, 0.71], "offset": [0, 0.525, 0.025]}],
          {"health": 160, "breakBy": "all", "sound": "wood", "debris": {"count": 16, "size": [0.05, 0.3], "speed": 3}})

till = [bx(-0.8, 0.0, -0.3, 0.8, 0.95, 0.3, "#7a5432", "wood"), bx(-0.83, 0.95, -0.33, 0.83, 1.0, 0.33, "#4a2e1c"),
        bx(-0.55, 1.0, -0.15, -0.15, 1.22, 0.15, "#3a3a3a"), bx(-0.5, 1.22, -0.1, -0.2, 1.3, 0.05, "#5a8a6a"),
        bx(0.05, 1.0, -0.2, 0.25, 1.24, 0.0, "#d8e8e8"), bx(0.3, 1.0, -0.2, 0.5, 1.2, 0.0, "#d8e8e8"),      # sweet jars
        bx(0.07, 1.02, -0.18, 0.23, 1.16, -0.02, "#e84a7a"), bx(0.32, 1.02, -0.18, 0.48, 1.12, -0.02, "#f0c43a"),
        bx(0.55, 1.0, 0.0, 0.75, 1.05, 0.25, "#c8282a"), bx(-0.78, 0.2, 0.3, 0.78, 0.8, 0.31, "#5a3a22")]
shop_prop("VillageShopTillCounter.json", "Village Shop Till Counter",
          "The shop counter: a till, jars of sweets and a stack of newspapers on a wooden counter.",
          till, [{"size": [1.66, 1.0, 0.66]}], {"health": 100, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.05, 0.25]}})

cards = [bx(-0.2, 0.0, -0.2, 0.2, 0.05, 0.2, "#2a2a2a"), bx(-0.02, 0.05, -0.02, 0.02, 1.65, 0.02, "#8a8a8a", "metal")]
for k in range(5):
    y = 0.45 + k * 0.25
    for a, (dx, dz) in enumerate(((0.12, 0), (-0.12, 0), (0, 0.12), (0, -0.12))):
        w = (0.02, 0.17) if dx else (0.17, 0.02)
        cards.append({"size": [w[0] if dx else 0.17, 0.2, w[1] if dx else 0.02], "offset": [dx * 1.05, y, dz * 1.05],
                      "color": rnd.choice(["#e87ab0", "#7ab0e8", "#f0ece0", "#f0d070", "#a8d8a0", "#e87a5a"])})
shop_prop("VillageShopCardRack.json", "Village Shop Card Rack",
          "A revolving wire rack of greetings cards.",
          cards, [{"size": [0.36, 1.65, 0.36]}], {"health": 30, "breakBy": "all", "sound": "soft", "debris": {"count": 14, "size": [0.04, 0.15]}},
          scatter={"randomYaw": True})

news = [bx(-0.5, 0.0, -0.2, 0.5, 0.06, 0.2, "#5a3a22", "wood"), bx(-0.5, 0.0, -0.2, -0.46, 0.9, 0.2, "#5a3a22", "wood"),
        bx(0.46, 0.0, -0.2, 0.5, 0.9, 0.2, "#5a3a22", "wood"), bx(-0.46, 0.0, -0.2, 0.46, 0.9, -0.16, "#5a3a22", "wood")]
for k, y in enumerate((0.3, 0.55, 0.8)):
    news.append({"size": [0.9, 0.03, 0.32], "offset": [0, y - 0.12, 0.0], "color": "#6a4a2e", "rotation": [-25, 0, 0]})
    for j in range(3):
        news.append({"size": [0.27, 0.025, 0.3], "offset": [-0.3 + j * 0.3, y - 0.09, 0.01], "color": rnd.choice(["#e8e4d8", "#dcd8cc", "#f0ece0"]), "rotation": [-25, 0, 0]})
        news.append({"size": [0.2, 0.026, 0.05], "offset": [-0.3 + j * 0.3, y - 0.04, -0.08], "color": rnd.choice(["#c8282a", "#2a2a2a", "#2a5aa8"]), "rotation": [-25, 0, 0]})
shop_prop("VillageShopNewspaperRack.json", "Village Shop Newspaper Rack",
          "A low wooden rack of the day's newspapers (mastheads in block colour, no names).",
          news, [{"size": [1.0, 0.9, 0.4]}], {"health": 50, "breakBy": "all", "sound": "wood", "debris": {"count": 14, "size": [0.04, 0.3]}})

dump(os.path.join(B, "VillageShop", "Furnishing", "shop.json"), {
    "format": "call-of-slop-furnishing/1",
    "type": "shop",
    "description": "The Village Shop's floor: the till by the door, shelves of groceries round the walls, a fridge, fruit and veg crates, cards and papers.",
    "items": [
        {"id": "till", "props": ["Village Shop Till Counter"], "place": "wall", "prefer": "near"},
        {"props": ["Village Shop Grocery Shelf"], "place": "wall", "count": [3, 5], "prefer": "far"},
        {"props": ["Fridge"], "place": "wall"},
        {"props": ["Produce Crates", "Apple Crate", "Cabbage Crate", "Potato Sacks"], "place": "wall", "count": [1, 3]},
        {"props": ["Village Shop Newspaper Rack"], "place": "wall"},
        {"props": ["Village Shop Card Rack"], "place": "floor", "gap": 0.6},
        {"props": ["Cardboard Box", "Newspaper Stack"], "place": "floor", "count": [0, 2]},
    ],
})

shop = {
    "format": "call-of-slop-building/1",
    "name": "Village Shop",
    "theme": "The village stores and post office: honey stone, a dark green fascia and bay of shop windows, a striped awning, oak shelves of groceries, a glazed post counter, the shopkeeper's flat above",
    "category": "commercial",
    "description": "A two-storey village shop and post office: the shop floor with the post counter, a store room and a sorting office behind; the shopkeeper's flat upstairs (living room, bedroom, kitchen, bathroom).",
    "author": "Call of Slop",
    "seed": 52,
    "wallThickness": 0.25,
    "storeyHeight": 3.0,
    "exterior": {"color": "$wall", "material": "brick"},
    "interior": {"color": "$interior", "material": "none"},
    "floor": {"color": "$floor", "material": "wood"},
    "ceiling": "$ceiling",
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "honey stone": {"wall": "#bfa36e", "interior": "#efe6cc", "floor": "#7a5a3a", "ceiling": "#f4f0e4", "trim": "#1f4a34", "door": "#1f4a34", "roof": "#3c4046", "flat": "#e4dcc8"},
        "painted render": {"wall": "#e2d8c0", "interior": "#f0e8d4", "floor": "#6a4a30", "ceiling": "#f6f2e8", "trim": "#6a1e22", "door": "#6a1e22", "roof": "#40403e", "flat": "#dcd8c8"},
    },
    "floors": [
        {
            "rooms": [
                {"id": "shop", "type": "shop", "rect": [-5.5, -0.5, 5.5, 5],
                 "props": [{"name": "Village Shop Post Counter", "wall": "E", "along": 0.2, "yaw": 0}]},
                {"id": "store", "type": "storage", "rect": [-5.5, -5, -1, -0.5], "floor": {"color": "#8a8478", "material": "concrete"},
                 "autoWindows": {"spacing": 3, "width": 0.9, "sill": 1.4, "head": 2.3}},
                {"id": "stairs", "type": "corridor", "rect": [-1, -5, 2, -0.5], "windows": False},
                {"id": "sorting", "type": "office", "rect": [2, -5, 5.5, -0.5], "autoWindows": {"spacing": 3, "width": 1.0, "sill": 1.0, "head": 2.2}},
            ],
            "openings": [
                {"type": "door", "at": [-0.6, 5], "width": 1.1, "front": True, "material": "glass", "health": 40},
                {"type": "window", "at": [-3.4, 5], "width": 3.0, "sill": 0.6, "head": 2.4, "health": 20},
                {"type": "window", "at": [2.6, 5], "width": 3.2, "sill": 0.6, "head": 2.4, "health": 20},
                {"type": "window", "at": [-5.5, 2.4], "width": 1.4, "sill": 0.9, "head": 2.3, "health": 3},
                {"type": "window", "at": [5.5, 3.4], "width": 1.0, "sill": 1.0, "head": 2.3, "health": 3},
                {"type": "door", "at": [-0.25, -0.5], "width": 0.9},
                {"type": "door", "at": [-1, -2.6], "width": 0.9},
                {"type": "door", "at": [2, -1.2], "width": 0.9},
                {"type": "door", "at": [-4, -5], "width": 1.0, "material": "metal"},
            ],
        },
        {
            "autoWindows": {"spacing": 2.6, "width": 1.0, "sill": 0.8, "head": 2.1},
            "rooms": [
                {"id": "living", "type": "living", "rect": [-5.5, -0.5, 1, 5], "wall": "$flat"},
                {"id": "bedroom", "type": "bedroom", "rect": [1, -0.5, 5.5, 5], "wall": "$flat"},
                {"id": "landing", "type": "hall", "rect": [-1, -5, 2, -0.5], "windows": False, "wall": "$flat"},
                {"id": "kitchen", "type": "kitchen", "rect": [-5.5, -5, -1, -0.5], "floor": {"color": "#b8b0a0", "material": "tiles"}},
                {"id": "bathroom", "type": "bathroom", "rect": [2, -5, 5.5, -0.5], "floor": {"color": "#c8d4d8", "material": "tiles"}, "wall": "#e8eef0",
                 "autoWindows": {"spacing": 3, "width": 0.8, "sill": 1.3, "head": 2.1}},
            ],
            "openings": [
                {"type": "door", "at": [-0.2, -0.5], "width": 0.9},
                {"type": "door", "at": [1, 2.5], "width": 0.9},
                {"type": "door", "at": [2, -1.3], "width": 0.9},
                {"type": "door", "at": [-1, -2.6], "width": 0.9},
            ],
        },
    ],
    "stairs": [{"floor": 0, "rect": [0.6, -3.9, 1.7, -1.85], "dir": "+z", "color": {"color": "#5a3e26", "material": "wood"}}],
    "roof": {"type": "gable", "ridge": "x", "pitch": 40, "overhang": 0.35, "color": {"color": "$roof", "material": "tiles"}},
    "extras": [
        {"type": "block", "min": [-5.65, 2.45, 5.12], "max": [5.65, 3.0, 5.26], "color": "$trim"},                   # the fascia
        {"type": "block", "min": [-5.65, 0.0, 5.12], "max": [5.65, 0.55, 5.2], "color": "$trim"},                    # stall risers
        {"type": "awning", "rect": [-5.3, 5.13, 4.4, 6.3], "height": 2.4, "color": "$trim"},
        {"type": "prop", "name": "Hanging Shop Sign", "at": [5.0, 0, 5.75]},
        {"type": "prop", "name": "Post Box", "at": [6.3, 5.8], "yaw": 0},
        {"type": "prop", "name": "Sandwich Board", "at": [1.2, 6.9], "yaw": -10},
        {"type": "prop", "name": "Hanging Flower Basket", "at": [-5.3, 2.0, 5.26]},
        {"type": "prop", "name": "Produce Crates", "at": [-4.4, 5.6], "yaw": 0},
        {"type": "prop", "name": "Wheelie Bin", "at": [-2.4, -5.8], "yaw": 180},
        {"type": "prop", "name": "Wheelie Bin Grey", "at": [-1.5, -5.8], "yaw": 180},
        {"type": "block", "min": [3.6, 6.0, -0.4], "max": [4.4, 11.6, 0.4], "color": {"color": "$wall", "material": "brick"}},   # chimney
    ],
    "anchors": [
        {"type": "spawn", "name": "store room", "floor": 0, "at": [-3.2, -2.8]},
        {"type": "spawn", "name": "flat", "floor": 1, "at": [-3.0, 2.0]},
        {"type": "flag", "name": "post counter", "floor": 0, "at": [3.6, 2.0]},
        {"type": "point", "name": "flat window over the street", "floor": 1, "at": [-2.0, 4.0]},
    ],
}
dump(os.path.join(B, "VillageShop", "building.json"), shop)
print("wrote Stone Cottage, Thatched Cottage, Village Shop (with", len(os.listdir(os.path.join(B, "VillageShop", "Props"))), "shop props)")
