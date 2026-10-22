#!/usr/bin/env python3
"""Writes the general Finnish buildings (Buildings/<Name>/, see Buildings/README.md) used by the World Tour's Finland map
and shown on the Dev pack's "Finnish Buildings" showcase map (Maps/Dev/BuildingsFinnish.json, also written here):

- Finnish Wooden House: a 1920s two-storey board-clad house, red ochre or yellow ochre with white trim and corner
  boards, a steep tin gable roof; hall, living room, kitchen, bedroom, bathroom and stair hall downstairs, three
  rooms and a landing upstairs.
- Red Barn Cottage: a one-storey red ochre summer cottage (mokki) with a porch: living room and kitchen open plan, a
  bedroom and a little washroom with its own sauna stove.
- Village Shop: a wooden village general store (kylakauppa): the shop floor with big windows, a store room, an
  office and a WC, a sign board over the door.
- Wooden Church: a small Lutheran timber church, a tall nave of pews under a steep roof, a chancel with the altar, a
  porch, a little belfry turret on the gable.
- Lakeside Sauna: a log sauna by the water: a changing room and the steam room with its stove and benches.

Usage: python3 tools/world/finland_buildings.py <repo root>
Then: python3 tools/buildings/buildings.py validate "Finnish Wooden House" ...
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def write_json(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        f.write(json.dumps(obj, indent=2) + "\n")


def write_prop(folder, d):
    d = dict(d)
    order = {"format": "call-of-slop-prop/1"}
    order.update(d)
    order.setdefault("author", "Call of Slop")
    fn = os.path.join(REPO, "Buildings", folder, "Props", "".join(w for w in d["name"].title().split() if w.isalnum()) + ".json")
    os.makedirs(os.path.dirname(fn), exist_ok=True)
    with open(fn, "w") as f:
        f.write("{\n")
        for k, v in order.items():
            if k != "parts":
                f.write(f'  "{k}": {json.dumps(v)},\n')
        f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(q) for q in order["parts"]) + "\n  ]\n}\n")


# ============================================================================================ palettes
WOOD_PAL = {
    "red ochre":    {"wall": "#8c3626", "interior": "#e8e0cc", "floor": "#8a6440", "ceiling": "#f0ece0", "trim": "#f2efe6", "door": "#f2efe6", "roof": "#38393c"},
    "yellow ochre": {"wall": "#cfa448", "interior": "#e6e8d8", "floor": "#7a5a3a", "ceiling": "#f2efe4", "trim": "#f6f4ee", "door": "#4a6a8a", "roof": "#7a2e22"},
    "pale green":   {"wall": "#9aae8a", "interior": "#ece4d0", "floor": "#8a6a48", "ceiling": "#f0ede2", "trim": "#f4f2ea", "door": "#7a3a2a", "roof": "#3a3e42"},
}


def corner_boards(x0, z0, x1, z1, h, t=0.18):
    """White corner boards up each outside corner (blocks just outside the walls)."""
    out = []
    for x in (x0, x1):
        for z in (z0, z1):
            sx = -1 if x == x0 else 1
            sz = -1 if z == z0 else 1
            out.append({"type": "block", "min": [round(min(x, x + sx * t), 3), 0.0, round(min(z, z + sz * t), 3)],
                        "max": [round(max(x, x + sx * t), 3), round(h, 3), round(max(z, z + sz * t), 3)], "color": "$trim"})
    return out


# ============================================================================================ Finnish Wooden House
house = {
    "format": "call-of-slop-building/1",
    "name": "Finnish Wooden House",
    "theme": "A 1920s Finnish board-clad house: red or yellow ochre boards, white frames and corner boards, a steep tin roof; birch floors, rag rugs, pine furniture",
    "category": "residential",
    "description": "A two-storey wooden house: hall, living room, kitchen, bedroom, bathroom and stair hall downstairs; two bedrooms, a small study and a landing upstairs. Front porch, back door to the yard.",
    "author": "Call of Slop",
    "seed": 601,
    "wallThickness": 0.25,
    "storeyHeight": 2.9,
    "exterior": {"color": "$wall", "material": "wood"},
    "interior": {"color": "$interior", "material": "none"},
    "floor": {"color": "$floor", "material": "wood"},
    "ceiling": "$ceiling",
    "trim": "$trim",
    "door": "$door",
    "palettes": WOOD_PAL,
    "floors": [
        {
            "autoWindows": {"spacing": 2.4, "width": 1.0, "sill": 0.9, "head": 2.1},
            "rooms": [
                {"id": "hall", "type": "hall", "rect": [-1.5, 0.5, 1.5, 4]},
                {"id": "living", "type": "living", "rect": [-5, -1, -1.5, 4]},
                {"id": "kitchen", "type": "kitchen", "rect": [1.5, -1, 5, 4], "floor": {"color": "#c8bfa8", "material": "tiles"}},
                {"id": "bedroom", "type": "bedroom", "rect": [-5, -4, -1.5, -1]},
                {"id": "stairs", "type": "stairhall", "rect": [-1.5, -4, 1.5, 0.5], "windows": False},
                {"id": "bath", "type": "bathroom", "rect": [1.5, -4, 5, -1], "floor": {"color": "#a8b8b8", "material": "tiles"}, "wall": "#dfe8e6",
                 "autoWindows": {"spacing": 2, "width": 0.6, "sill": 1.4, "head": 2.0}},
            ],
            "openings": [
                {"type": "door", "at": [0, 4], "width": 1.0, "front": True},
                {"type": "arch", "at": [0.5, 0.5], "width": 1.2},
                {"type": "door", "at": [-1.5, 2.2], "width": 0.9},
                {"type": "door", "at": [1.5, 2.2], "width": 0.9, "hinge": "right"},
                {"type": "door", "at": [-3.25, -1], "width": 0.9},
                {"type": "door", "at": [3.25, -1], "width": 0.9},
                {"type": "door", "at": [5, 1.5], "width": 0.9, "color": "$door"},
            ],
        },
        {
            "height": 2.7,
            "autoWindows": {"spacing": 2.4, "width": 0.9, "sill": 0.9, "head": 2.0},
            "rooms": [
                {"id": "landing", "type": "corridor", "rect": [-1.5, -4, 1.5, 0.5], "windows": False},
                {"id": "bed1", "type": "bedroom", "rect": [-5, -4, -1.5, 4]},
                {"id": "bed2", "type": "kidsroom", "rect": [1.5, -4, 5, 4]},
                {"id": "study", "type": "office", "rect": [-1.5, 0.5, 1.5, 4]},
            ],
            "openings": [
                {"type": "door", "at": [-1.5, -3.3], "width": 0.9},
                {"type": "door", "at": [1.5, -3.3], "width": 0.9},
                {"type": "door", "at": [0.8, 0.5], "width": 0.9},
            ],
        },
    ],
    "stairs": [{"floor": 0, "rect": [-1.25, -2.9, -0.15, -0.6], "dir": "-z", "color": {"color": "#8a6440", "material": "wood"}}],
    "roof": {"type": "gable", "ridge": "x", "pitch": 42, "overhang": 0.5, "color": {"color": "$roof", "material": "metal"}},
    "extras": corner_boards(-5.125, -4.125, 5.125, 4.125, 5.6) + [
        {"type": "porch", "rect": [-1.5, 4.13, 1.5, 5.7], "height": 2.6, "color": "$trim"},
        {"type": "block", "min": [-0.4, 4.0, -1.0], "max": [0.4, 8.6, -0.2], "color": {"color": "#8a5a44", "material": "brick"}},
        {"type": "prop", "name": "Birch Firewood Stack", "at": [-6.3, 0.0], "yaw": 90},
        {"type": "prop", "name": "Wheelie Bin Grey", "at": [6.3, -2.6], "yaw": 270},
        {"type": "prop", "name": "Kicksled", "at": [2.4, 5.0], "yaw": 20},
        {"type": "prop", "name": "Flower Pot", "at": [-2.2, 4.8], "yaw": 0},
    ],
    "anchors": [
        {"type": "spawn", "name": "living room", "at": [-3.3, 1.5]},
        {"type": "spawn", "name": "upstairs bedroom", "floor": 1, "at": [-3.3, 1.0]},
        {"type": "point", "name": "front door", "at": [0, 6.4]},
    ],
}
write_json(os.path.join(REPO, "Buildings/FinnishWoodenHouse/building.json"), house)

# ============================================================================================ Red Barn Cottage
cottage = {
    "format": "call-of-slop-building/1",
    "name": "Red Barn Cottage",
    "theme": "A Finnish summer cottage (mokki): red ochre boards, white trim, a low tin roof, a lakeside porch; rag rugs, a wood stove and a washroom with a sauna stove",
    "category": "residential",
    "description": "A one-storey cottage: an open living room and kitchen, a bedroom and a washroom with a sauna stove, a porch and a back door.",
    "author": "Call of Slop",
    "seed": 611,
    "wallThickness": 0.25,
    "storeyHeight": 2.7,
    "exterior": {"color": "$wall", "material": "wood"},
    "interior": {"color": "$interior", "material": "wood"},
    "floor": {"color": "$floor", "material": "wood"},
    "ceiling": "$ceiling",
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "red ochre":    {"wall": "#8a3424", "interior": "#c8a47a", "floor": "#8a6440", "ceiling": "#c09a70", "trim": "#f2efe6", "door": "#f2efe6", "roof": "#3a3b3e"},
        "yellow ochre": {"wall": "#c99c44", "interior": "#cfac80", "floor": "#7a5a3a", "ceiling": "#c4a074", "trim": "#f6f4ee", "door": "#3a5a7a", "roof": "#6e2a20"},
        "weathered grey": {"wall": "#7a7670", "interior": "#c2a078", "floor": "#7a5e40", "ceiling": "#b89670", "trim": "#e8e4da", "door": "#8a3424", "roof": "#2e3034"},
    },
    "floors": [
        {
            "autoWindows": {"spacing": 2.4, "width": 1.0, "sill": 0.9, "head": 2.0},
            "rooms": [
                {"id": "living", "type": "living", "rect": [-4, -0.5, 1, 3], "openTo": ["kitchen"]},
                {"id": "kitchen", "type": "kitchen", "rect": [1, -0.5, 4, 3]},
                {"id": "bedroom", "type": "bedroom", "rect": [-4, -3, 0.5, -0.5]},
                {"id": "wash", "type": "sauna", "rect": [0.5, -3, 4, -0.5], "furnish": "explicit", "floor": {"color": "#7a6a5a", "material": "tiles"},
                 "autoWindows": {"spacing": 2, "width": 0.6, "sill": 1.4, "head": 2.0},
                 "props": [{"name": "Sauna Stove", "wall": "N", "along": 1.2}, {"name": "Sauna Bench", "wall": "W", "along": 0},
                           {"name": "Sauna Bucket", "at": [0.4, -0.6]}]},
            ],
            "openings": [
                {"type": "door", "at": [-1.5, 3], "width": 1.0, "front": True},
                {"type": "door", "at": [-1.75, -0.5], "width": 0.9},
                {"type": "door", "at": [3.3, -0.5], "width": 0.9},
                {"type": "door", "at": [4, 1.3], "width": 0.9, "color": "$door"},
            ],
        }
    ],
    "roof": {"type": "gable", "ridge": "x", "pitch": 30, "overhang": 0.5, "color": {"color": "$roof", "material": "metal"}},
    "extras": corner_boards(-4.125, -3.125, 4.125, 3.125, 2.7) + [
        {"type": "porch", "rect": [-4.0, 3.13, 1.0, 5.0], "height": 2.5, "color": "$trim"},
        {"type": "block", "min": [2.6, 2.4, -2.2], "max": [3.2, 5.6, -1.6], "color": {"color": "#2a2a2a", "material": "metal"}},
        {"type": "prop", "name": "Birch Log Bench", "at": [-2.6, 4.2], "yaw": 0},
        {"type": "prop", "name": "Birch Firewood Stack", "at": [5.2, -1.4], "yaw": 90},
        {"type": "prop", "name": "Rocking Chair", "at": [0.0, 4.3], "yaw": 160},
    ],
    "anchors": [{"type": "spawn", "name": "living room", "at": [-1.5, 1.2]}, {"type": "point", "name": "porch", "at": [-1.5, 4.3]}],
}
write_json(os.path.join(REPO, "Buildings/RedBarnCottage/building.json"), cottage)

# ============================================================================================ Village Shop
shop = {
    "format": "call-of-slop-building/1",
    "name": "Village Shop",
    "theme": "A 1950s Finnish wooden village store (kylakauppa): painted boards, white trim, a sign board over the door; shelves and freezers, a store room of crates",
    "category": "commercial",
    "description": "A wooden village shop: the shop floor with big front windows, a store room with a loading door, a back office and a WC, a sign board and a bench out front.",
    "author": "Call of Slop",
    "seed": 621,
    "wallThickness": 0.25,
    "storeyHeight": 3.2,
    "exterior": {"color": "$wall", "material": "wood"},
    "interior": {"color": "$interior", "material": "none"},
    "floor": {"color": "$floor", "material": "tiles"},
    "ceiling": "$ceiling",
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "yellow ochre": {"wall": "#d0a44a", "interior": "#eeeadc", "floor": "#d8d2c2", "ceiling": "#f4f2ea", "trim": "#f6f4ee", "door": "#2a4a7a", "roof": "#3a3b3e", "sign": "#2a4a8a"},
        "red ochre":    {"wall": "#8a3424", "interior": "#ece6d6", "floor": "#d4ccba", "ceiling": "#f2f0e8", "trim": "#f2efe6", "door": "#2e5a3a", "roof": "#38393c", "sign": "#e8e4d8"},
    },
    "floors": [
        {
            "rooms": [
                {"id": "shop", "type": "shop", "rect": [-6, -1, 6, 4.5], "autoWindows": {"spacing": 3, "width": 1.6, "sill": 0.6, "head": 2.4, "health": 20}},
                {"id": "store", "type": "storage", "rect": [-6, -4.5, 1, -1], "floor": {"color": "#9a968c", "material": "concrete"}, "windows": False},
                {"id": "office", "type": "office", "rect": [1, -4.5, 4, -1], "autoWindows": {"spacing": 2, "width": 0.9, "sill": 1.0, "head": 2.2}},
                {"id": "wc", "type": "wc", "rect": [4, -4.5, 6, -1], "windows": False},
            ],
            "openings": [
                {"type": "door", "at": [0, 4.5], "width": 1.2, "front": True, "material": "glass", "health": 40},
                {"type": "window", "at": [-3.4, 4.5], "width": 3.0, "sill": 0.5, "head": 2.5, "health": 25},
                {"type": "window", "at": [3.4, 4.5], "width": 3.0, "sill": 0.5, "head": 2.5, "health": 25},
                {"type": "door", "at": [-2.5, -1], "width": 1.0},
                {"type": "door", "at": [2.5, -1], "width": 0.9},
                {"type": "door", "at": [5.0, -1], "width": 0.9},
                {"type": "door", "at": [-4.0, -4.5], "width": 1.4, "material": "metal"},
            ],
        }
    ],
    "roof": {"type": "gable", "ridge": "x", "pitch": 26, "overhang": 0.5, "color": {"color": "$roof", "material": "metal"}},
    "extras": corner_boards(-6.125, -4.625, 6.125, 4.625, 3.3) + [
        {"type": "block", "min": [-3.2, 2.75, 4.63], "max": [3.2, 3.6, 4.8], "color": "$sign"},
        {"type": "block", "min": [-3.0, 2.85, 4.8], "max": [3.0, 3.5, 4.83], "color": "$trim"},
        {"type": "awning", "rect": [-1.4, 4.63, 1.4, 5.8], "height": 2.65, "color": "$trim"},
        {"type": "prop", "name": "Birch Log Bench", "at": [-3.4, 5.6], "yaw": 0},
        {"type": "prop", "name": "Bike Rack", "at": [3.6, 5.9], "yaw": 0},
        {"type": "prop", "name": "Kicksled", "at": [2.0, 5.4], "yaw": 340},
        {"type": "prop", "name": "Post Box", "at": [-5.6, 5.4], "yaw": 0},
        {"type": "prop", "name": "Pallet Stack", "at": [-2.2, -5.6], "yaw": 0},
        {"type": "prop", "name": "Wheelie Bin Grey", "at": [0.6, -5.4], "yaw": 180},
    ],
    "anchors": [{"type": "spawn", "name": "store room", "at": [-4.5, -2.8]}, {"type": "flag", "name": "shop counter", "at": [0, 1.5]}],
}
write_json(os.path.join(REPO, "Buildings/VillageShop/building.json"), shop)

# ============================================================================================ Wooden Church
pews = []
for k in range(6):
    z = -4.2 + k * 1.25
    pews += [{"name": "Wooden Pew", "at": [-2.2, z], "yaw": 180}, {"name": "Wooden Pew", "at": [2.2, z], "yaw": 180}]
church = {
    "format": "call-of-slop-building/1",
    "name": "Wooden Church",
    "theme": "A small Finnish Lutheran timber church: ochre or white boards, a steep shingle roof, tall windows, a belfry turret; pale-blue pews and a wooden altar",
    "category": "special",
    "description": "A small wooden church: a porch, a lofty nave of pews, a chancel with the altar through an arch, and a vestry with a side door; a belfry turret on the front gable.",
    "author": "Call of Slop",
    "seed": 631,
    "wallThickness": 0.3,
    "storeyHeight": 5.5,
    "doorHeight": 2.5,
    "exterior": {"color": "$wall", "material": "wood"},
    "interior": {"color": "$interior", "material": "wood"},
    "floor": {"color": "$floor", "material": "wood"},
    "ceiling": {"color": "$ceiling", "material": "wood"},
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "red ochre": {"wall": "#8a3424", "interior": "#e8e2d0", "floor": "#9a7a54", "ceiling": "#a8c0cc", "trim": "#f2efe6", "door": "#f2efe6", "roof": "#2a2a2c"},
        "white boards": {"wall": "#ecebe4", "interior": "#eee8d6", "floor": "#9a7a54", "ceiling": "#9ab4c4", "trim": "#3a4a5a", "door": "#7a2e22", "roof": "#2c2e32"},
    },
    "floors": [
        {
            "rooms": [
                {"id": "porch", "type": "narthex", "rect": [-2, 6.5, 2, 9.5], "windows": False, "furnish": "none"},
                {"id": "nave", "type": "nave", "rect": [-4.5, -6.5, 4.5, 6.5], "furnish": "explicit",
                 "autoWindows": {"spacing": 2.6, "width": 1.0, "sill": 1.6, "head": 4.4}, "props": pews},
                {"id": "chancel", "type": "chancel", "rect": [-3, -10, 3, -6.5], "furnish": "explicit",
                 "autoWindows": {"spacing": 2.2, "width": 0.9, "sill": 1.8, "head": 4.2},
                 "props": [{"name": "Church Altar Table", "wall": "N", "along": 0}]},
                {"id": "vestry", "type": "storage", "rect": [3, -10, 4.5, -6.5], "windows": False, "furnish": "none"},
            ],
            "openings": [
                {"type": "door", "at": [0, 9.5], "width": 1.4, "front": True, "material": "wood"},
                {"type": "arch", "at": [0, 6.5], "width": 1.6, "head": 3.0},
                {"type": "arch", "at": [0, -6.5], "width": 2.6, "head": 3.6},
                {"type": "door", "at": [3.75, -6.5], "width": 0.9},
                {"type": "door", "at": [4.5, -8.3], "width": 0.9, "material": "wood"},
                {"type": "door", "at": [-4.5, 4.6], "width": 1.0, "material": "wood"},
                {"type": "window", "at": [0, -10], "width": 1.2, "sill": 2.0, "head": 4.4},
            ],
        }
    ],
    "roof": {"type": "gable", "ridge": "z", "pitch": 52, "overhang": 0.4, "color": {"color": "$roof", "material": "wood"}},
    "extras": corner_boards(-4.65, -6.65, 4.65, 6.65, 5.6) + [
        # the belfry turret on the front gable, a lantern and a spire
        {"type": "block", "min": [-1.3, 9.0, 4.6], "max": [1.3, 12.0, 7.2], "color": "$wall"},
        {"type": "block", "min": [-1.45, 12.0, 4.45], "max": [1.45, 12.25, 7.35], "color": "$trim"},
        {"type": "block", "min": [-1.0, 12.25, 4.9], "max": [1.0, 13.8, 6.9], "color": "$trim"},
        {"type": "block", "min": [-0.55, 13.8, 5.35], "max": [0.55, 16.0, 6.45], "color": "$roof"},
        {"type": "block", "min": [-0.08, 16.0, 5.82], "max": [0.08, 17.4, 5.98], "color": "#c8a040"},
        {"type": "block", "min": [-0.4, 16.7, 5.82], "max": [0.4, 16.86, 5.98], "color": "#c8a040"},
        {"type": "porch", "rect": [-1.6, 9.63, 1.6, 11.2], "height": 2.8, "color": "$trim"},
        {"type": "prop", "name": "Church Grave Marker", "at": [-6.5, -2.0], "yaw": 90},
        {"type": "prop", "name": "Church Grave Marker", "at": [-6.5, 0.5], "yaw": 90},
        {"type": "prop", "name": "Church Grave Marker", "at": [-6.6, 3.0], "yaw": 90},
        {"type": "prop", "name": "Church Grave Marker", "at": [6.5, 1.0], "yaw": 270},
        {"type": "prop", "name": "Church Grave Marker", "at": [6.6, 3.5], "yaw": 270},
        {"type": "prop", "name": "Church Grave Marker", "at": [6.5, 6.0], "yaw": 270},
    ],
    "anchors": [{"type": "spawn", "name": "nave", "at": [0, 0]}, {"type": "hill", "name": "nave", "at": [0, 0]}, {"type": "point", "name": "door", "at": [0, 12.0]}],
}
write_json(os.path.join(REPO, "Buildings/WoodenChurch/building.json"), church)
write_prop("WoodenChurch", {"name": "Wooden Pew", "description": "A plain pale-blue painted wooden pew, 3 m long along X, facing +Z.",
                            "collision": "solid", "collider": [{"size": [3.0, 0.95, 0.62]}], "scatter": {"randomYaw": False, "yawStep": 90},
                            "breakable": {"health": 120, "breakBy": "all", "sound": "wood", "debris": {"count": 14, "size": [0.06, 0.5]}},
                            "parts": [part((3.0, 0.06, 0.42), (0, 0.45, 0.06), "#9ab4c4", material="wood"),
                                      part((3.0, 0.5, 0.05), (0, 0.72, -0.18), "#9ab4c4", material="wood"),
                                      part((0.06, 0.95, 0.6), (-1.47, 0.475, -0.01), "#7a96a8"), part((0.06, 0.95, 0.6), (1.47, 0.475, -0.01), "#7a96a8"),
                                      part((3.0, 0.08, 0.06), (0, 0.97, -0.18), "#6a8494")]})
write_prop("WoodenChurch", {"name": "Church Altar Table", "description": "A wooden altar with a white cloth, two candlesticks and a cross, a curved altar rail in front. Faces +Z.",
                            "collision": "solid", "collider": [{"size": [2.0, 1.0, 0.8], "offset": [0, 0.5, -0.4]}, {"size": [3.2, 0.8, 0.12], "offset": [0, 0.4, 0.9]}],
                            "scatter": {"randomYaw": False, "yawStep": 90},
                            "parts": [part((1.9, 0.95, 0.75), (0, 0.475, -0.4), "#8a6a44", material="wood"),
                                      part((2.0, 0.04, 0.8), (0, 0.97, -0.4), "#f2f0ea"), part((0.5, 0.5, 0.02), (0, 0.7, -0.01), "#c8a040"),
                                      part((0.05, 0.3, 0.05), (-0.7, 1.14, -0.5), "#c8a040"), part((0.05, 0.3, 0.05), (0.7, 1.14, -0.5), "#c8a040"),
                                      part((0.06, 0.8, 0.04), (0, 1.39, -0.7), "#c8a040"), part((0.4, 0.06, 0.04), (0, 1.55, -0.7), "#c8a040"),
                                      part((3.2, 0.06, 0.16), (0, 0.8, 0.9), "#9ab4c4", material="wood"),
                                      part((3.2, 0.2, 0.4), (0, 0.1, 0.95), "#7a5a3a", material="wood")]
                                     + [part((0.05, 0.7, 0.05), (x, 0.45, 0.9), "#7a96a8") for x in (-1.5, -0.75, 0.0, 0.75, 1.5)]})
write_prop("WoodenChurch", {"name": "Church Grave Marker", "description": "A weathered granite headstone with a little iron cross on top, in the churchyard. Faces +Z.",
                            "collision": "solid", "collider": [{"size": [0.7, 0.9, 0.22]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 4},
                            "parts": [part((0.7, 0.75, 0.18), (0, 0.375, 0), "#7d7670", material="concrete"),
                                      part((0.76, 0.08, 0.24), (0, 0.04, 0), "#6a645e"),
                                      part((0.04, 0.3, 0.03), (0, 0.9, 0), "#2a2a2a"), part((0.18, 0.04, 0.03), (0, 0.96, 0), "#2a2a2a"),
                                      part((0.4, 0.1, 0.02), (0, 0.55, 0.095), "#5a544e"),
                                      part((0.6, 0.05, 0.5), (0, 0.025, 0.4), "#56702e")]})

# ============================================================================================ Lakeside Sauna
sauna = {
    "format": "call-of-slop-building/1",
    "name": "Lakeside Sauna",
    "theme": "A Finnish log sauna by a lake: tarred logs, a green tin roof, a porch to cool off on; a changing room and the steam room with a wood stove and pine benches",
    "category": "residential",
    "description": "A small log sauna: a changing room behind the porch and the steam room with a wood stove and benches, a chimney pipe through the roof.",
    "author": "Call of Slop",
    "seed": 641,
    "wallThickness": 0.3,
    "storeyHeight": 2.5,
    "exterior": {"color": "$wall", "material": "wood"},
    "interior": {"color": "$interior", "material": "wood"},
    "floor": {"color": "$floor", "material": "wood"},
    "ceiling": "$interior",
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "tarred log": {"wall": "#4a3424", "interior": "#c49a62", "floor": "#8a6a44", "trim": "#2e2218", "door": "#6a4a2e", "roof": "#3e5a3a"},
        "red ochre":  {"wall": "#7e3022", "interior": "#c8a06a", "floor": "#8a6a44", "trim": "#ece8de", "door": "#ece8de", "roof": "#2e3034"},
    },
    "floors": [
        {
            "rooms": [
                {"id": "changing", "type": "changing", "rect": [-2.5, 0, 2.5, 2], "furnish": "explicit",
                 "autoWindows": {"spacing": 2, "width": 0.7, "sill": 1.1, "head": 1.9},
                 "props": [{"name": "Birch Log Bench", "wall": "N", "along": -1.2}, {"name": "Coat Stand", "at": [2.0, -0.5]}]},
                {"id": "steam", "type": "sauna", "rect": [-2.5, -2.5, 2.5, 0], "furnish": "explicit",
                 "autoWindows": {"spacing": 3, "width": 0.6, "sill": 1.3, "head": 1.8},
                 "props": [{"name": "Sauna Stove", "wall": "N", "along": 1.6}, {"name": "Sauna Bench", "wall": "N", "along": -0.9},
                           {"name": "Sauna Bucket", "at": [0.6, 0.4]}]},
            ],
            "openings": [
                {"type": "door", "at": [-1.3, 2], "width": 0.9, "front": True},
                {"type": "door", "at": [1.6, 0], "width": 0.9},
            ],
        }
    ],
    "roof": {"type": "gable", "ridge": "x", "pitch": 28, "overhang": 0.5, "color": {"color": "$roof", "material": "metal"}},
    "extras": [
        {"type": "porch", "rect": [-2.5, 2.15, 2.5, 3.8], "height": 2.3, "color": "$trim"},
        {"type": "block", "min": [1.7, 2.2, -1.6], "max": [2.0, 4.6, -1.3], "color": {"color": "#2a2a2a", "material": "metal"}},
        {"type": "prop", "name": "Birch Firewood Stack", "at": [-3.9, -0.4], "yaw": 90},
        {"type": "prop", "name": "Sauna Bucket", "at": [1.6, 3.2], "yaw": 0},
    ],
    "anchors": [{"type": "point", "name": "porch", "at": [0, 3.0]}],
}
write_json(os.path.join(REPO, "Buildings/LakesideSauna/building.json"), sauna)
SAUNA_PROPS = [
    {"name": "Sauna Stove", "description": "A wood-fired sauna stove (kiuas): a black steel firebox heaped with stones, the fire glowing, a flue pipe. Its back goes to a wall.",
     "collision": "solid", "collider": [{"size": [0.6, 0.85, 0.6]}], "scatter": {"randomYaw": False, "yawStep": 90},
     "parts": [part((0.56, 0.6, 0.56), (0, 0.3, 0), "#1e1e20", material="metal"),
               part((0.5, 0.22, 0.5), (0, 0.71, 0), "#6a6660"), part((0.2, 0.12, 0.2), (0.1, 0.86, 0.08), "#4a4644"),
               part((0.18, 0.1, 0.2), (-0.12, 0.85, -0.06), "#7a7670"),
               part((0.3, 0.16, 0.02), (0, 0.22, 0.285), "#ff8a2a", anim={"type": "flicker", "rate": 8, "duty": 0.85, "color": "#ff8a2a", "off": "#7a3010", "glow": 1.6}),
               part((0.12, 1.4, 0.12), (0.12, 1.5, -0.16), "#2a2a2a", material="metal")]},
    {"name": "Sauna Bench", "description": "Two-tier pine sauna benches (lauteet) on a frame, 2 m long along X, facing +Z. You can step up onto them.",
     "collision": "solid", "collider": [{"size": [2.0, 0.45, 1.2], "offset": [0, 0.225, 0]}, {"size": [2.0, 0.95, 0.6], "offset": [0, 0.475, -0.3]}],
     "scatter": {"randomYaw": False, "yawStep": 90},
     "breakable": {"health": 150, "breakBy": "all", "sound": "wood", "debris": {"count": 14, "size": [0.06, 0.4]}},
     "parts": [part((2.0, 0.06, 0.6), (0, 0.45, 0.3), "#d8b47a", material="wood"), part((2.0, 0.06, 0.6), (0, 0.95, -0.3), "#d8b47a", material="wood"),
               part((2.0, 0.42, 0.04), (0, 0.21, 0.58), "#c49a62"), part((2.0, 0.5, 0.04), (0, 0.7, 0.0), "#c49a62"),
               part((0.06, 0.95, 1.2), (-0.97, 0.475, 0), "#a07a48"), part((0.06, 0.95, 1.2), (0.97, 0.475, 0), "#a07a48")]},
]
for folder in ("LakesideSauna", "RedBarnCottage"):
    for p in SAUNA_PROPS:
        write_prop(folder, p)

# ============================================================================================ the showcase map
show = {
    "format": "call-of-slop-map/1",
    "name": "Finnish Buildings",
    "description": "The Finnish buildings in a row along a gravel road: wooden houses in their palettes, the cottage, the village shop, the wooden church and the lakeside sauna.",
    "author": "Call of Slop",
    "order": 963,
    "halfX": 58,
    "halfZ": 30,
    "sky": [0.62, 0.72, 0.82],
    "flagPool": ["finland"],
    "ops": [
        {"op": "Ground", "color": [0.36, 0.42, 0.28]},
        {"op": "Perimeter", "height": 4, "color": [0.5, 0.48, 0.44]},
        {"op": "Block", "x0": -58, "y0": 0, "z0": 2, "x1": 58, "y1": 0.03, "z1": 7, "color": [0.5, 0.47, 0.42]},
        {"op": "Building", "name": "Finnish Wooden House", "at": [-46, -8], "yaw": 0, "spawns": True},
        {"op": "Building", "name": "Finnish Wooden House", "at": [-32, -8], "yaw": 0, "spawns": True, "variant": {"palette": "yellow ochre", "mirror": True}},
        {"op": "Building", "name": "Finnish Wooden House", "at": [-46, 18], "yaw": 180, "spawns": True, "variant": {"palette": "pale green"}},
        {"op": "Building", "name": "Red Barn Cottage", "at": [-18, -8], "yaw": 0, "spawns": True},
        {"op": "Building", "name": "Red Barn Cottage", "at": [-30, 18], "yaw": 180, "spawns": True, "variant": {"palette": "yellow ochre"}},
        {"op": "Building", "name": "Village Shop", "at": [-2, -8.5], "yaw": 0, "spawns": True},
        {"op": "Building", "name": "Village Shop", "at": [-12, 18.5], "yaw": 180, "spawns": True, "variant": {"palette": "red ochre"}},
        {"op": "Building", "name": "Wooden Church", "at": [22, -14], "yaw": 0, "spawns": True},
        {"op": "Building", "name": "Wooden Church", "at": [8, 18], "yaw": 180, "variant": {"palette": "white boards"}},
        {"op": "Building", "name": "Lakeside Sauna", "at": [40, -8], "yaw": 0},
        {"op": "Building", "name": "Lakeside Sauna", "at": [40, 16], "yaw": 180, "variant": {"palette": "red ochre"}},
        {"op": "Prop", "name": "Flag on a Pole", "x": 52, "z": 0, "flag": "finland"},
        {"op": "Prop", "name": "Mailbox Rack", "x": -24, "z": 1.0},
        {"op": "Prop", "name": "Moose Warning Sign", "x": 30, "z": 8.0, "yaw": 180},
        {"op": "RingSpawns", "count": 10, "frac": 0.85},
        {"op": "AutoWaypoints", "spacing": 6},
    ],
    "foliage": {"seed": 5, "regions": [{"type": "grass", "density": 2, "rect": [-57, -29, 57, 29]},
                                       {"type": "pine", "density": 0.01, "rect": [-57, -29, 57, -24]},
                                       {"type": "tree", "density": 0.01, "rect": [-57, 25, 57, 29], "color": [0.78, 0.66, 0.2]}]},
}
with open(os.path.join(REPO, "Maps/Dev/BuildingsFinnish.json"), "w") as f:
    f.write("{\n" + ",\n".join(f'  "{k}": ' + (("[\n" + ",\n".join("    " + json.dumps(o) for o in v) + "\n  ]") if k == "ops" else json.dumps(v)) for k, v in show.items()) + "\n}\n")
print("buildings written")
