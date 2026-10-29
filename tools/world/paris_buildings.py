#!/usr/bin/env python3
"""Writes the three Parisian buildings (Buildings/HaussmannApartment, Buildings/CafeBrasserie, Buildings/Boulangerie).
Usage: python3 tools/world/paris_buildings.py [repo root]; then python3 tools/buildings/buildings.py validate "Haussmann Apartment" ...

Stairwells keep landings of at least 1.15 m between every flight's ends and the walls (bots need a nav node at both
ends; a flight end within ~0.45 m of a wall drops out of the bots' routes).
"""
import json, os, sys

REPO = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def write(rel, obj):
    path = os.path.join(REPO, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        f.write(json.dumps(obj, indent=2) + "\n")


def room(id, type, rect, **kw):
    r = {"id": id, "type": type, "rect": rect}
    r.update(kw)
    return r


def door(at, w=0.9, **kw):
    o = {"type": "door", "at": at, "width": w}
    o.update(kw)
    return o


def win(at, w=1.2, sill=0.9, head=2.4, **kw):
    o = {"type": "window", "at": at, "width": w, "sill": sill, "head": head}
    o.update(kw)
    return o


HALL_FLOOR = {"color": "#d6d0c4", "material": "tiles"}
PARQUET = {"color": "$floor", "material": "wood"}
STEP = {"color": "#b8ae9a", "material": "concrete"}
WOOD_STEP = {"color": "#5a3a22", "material": "wood"}

# ===================================================================== Haussmann Apartment
# 12 x 10 m, front (+Z) on the street. Ground floor: a shop and its stock room, the entrance hall with the stairwell
# (x 1.5..6) running up all six floors; floors 1-4: a flat each (salon at the front, bedroom and kitchen behind);
# floor 2 has the long wrought-iron balcony; floor 5 is the mansard attic (maids' rooms), set back 1.6 m from the
# facade so the top of floor 4 is a parapeted terrace all along the front; a zinc hip roof with chimney stacks on top.
H_LEVELS = [0.1, 4.3, 7.5, 10.7, 13.9, 17.1, 20.1]
cols = {"W": [2.0, -3.6, 3.1, 0.2], "E": [4.4, -3.6, 5.5, 0.2]}
h_stairs = []
for f in range(5):
    c = "W" if f % 2 == 0 else "E"
    h_stairs.append({"floor": f, "rect": cols[c], "dir": "-z" if c == "W" else "+z", "color": STEP})

floors = []
floors.append({
    "height": 4.2,
    "rooms": [
        room("shop", "shop", [-6, -0.5, 1.5, 5], floor={"color": "#b8a888", "material": "tiles"}),
        room("stock", "storage", [-6, -5, 1.5, -0.5], floor={"color": "#8a8478", "material": "none"}),
        room("hall", "corridor", [1.5, -5, 6, 5], floor=HALL_FLOOR, wall="#e4dac4"),
    ],
    "openings": [
        door([3.75, 5], 1.4, front=True, color="$door"),
        door([3.75, -5], 1.1, color="$door"),
        door([-1.6, 5], 1.1, material="glass", health=40, hinge="right"),
        win([-4.4, 5], 2.6, 0.5, 3.2, health=12),
        door([1.5, 2.6], 0.9),
        door([-4.4, -0.5], 0.9),
        door([-1.5, -5], 1.0, material="metal"),
        win([-4.4, -5], 1.0, 1.5, 2.6),
    ],
})
for f in range(1, 5):
    front = [win([-4.4, 5], 1.2, 0.4, 2.6), win([-1.4, 5], 1.2, 0.4, 2.6), win([3.75, 5], 1.2, 0.4, 2.6)]
    if f == 2:  # the balcony floor: French doors onto it
        front = [door([-4.4, 5], 1.1, material="glass", health=30), door([-1.4, 5], 1.1, material="glass", health=30, hinge="right"),
                 door([3.75, 5], 1.0, material="glass", health=30)]
    floors.append({
        "height": 3.2,
        "rooms": [
            room("landing", "corridor", [1.5, -5, 6, 5], floor=HALL_FLOOR, wall="#e4dac4"),
            room("salon", "living", [-6, 0.5, 1.5, 5], floor=PARQUET, seed=f),
            room("chambre", "bedroom", [-6, -5, -2, 0.5], floor=PARQUET, seed=f),
            room("cuisine", "kitchen", [-2, -5, 1.5, 0.5], floor={"color": "#c8b8a0", "material": "tiles"}, seed=f),
        ],
        "openings": front + [
            door([1.5, 2.6], 0.9, color="$door"),
            door([-4.2, 0.5], 0.9),
            door([-0.3, 0.5], 0.9),
            win([-4.0, -5], 1.1, 0.9, 2.4),
            win([-0.25, -5], 1.0, 1.0, 2.4),
            win([3.75, -5], 1.0, 1.0, 2.4),
        ],
    })
floors.append({
    "height": 3.0,
    "rooms": [
        room("attic landing", "corridor", [1.5, -5, 6, 3.4], floor={"color": "#8a6a4a", "material": "wood"}),
        room("maid front", "bedroom", [-6, -0.8, 1.5, 3.4], floor={"color": "#8a6a4a", "material": "wood"}, wall="#e8dcc0"),
        room("maid back", "bedroom", [-6, -5, 1.5, -0.8], floor={"color": "#8a6a4a", "material": "wood"}, wall="#dcd4c0", seed=9),
    ],
    "openings": [
        door([3.75, 3.4], 1.0, color="$door"),
        door([-1.6, 3.4], 1.0, material="glass", health=30),
        win([-4.4, 3.4], 1.1, 0.9, 2.2),
        door([1.5, 1.3], 0.9),
        door([1.5, -4.3], 0.9),
        win([-3.5, -5], 1.0, 1.0, 2.2),
        win([3.75, -5], 0.9, 1.0, 2.2),
    ],
})
h_extras = [
    {"type": "balcony", "floor": 2, "rect": [-6, 5.15, 6, 6.05], "railing": 1.0, "color": {"color": "#2a2a2a", "material": "metal"}},
    {"type": "block", "min": [-6.15, 4.15, 5.15], "max": [6.15, 4.45, 5.36], "color": "$trim"},              # first-floor cornice
    {"type": "block", "min": [-6.1, 3.35, 5.15], "max": [1.35, 4.15, 5.3], "color": "$front"},               # shop fascia
    {"type": "block", "min": [-6.15, 16.85, 5.15], "max": [6.15, 17.15, 5.42], "color": "$trim"},            # main cornice under the terrace
    {"type": "block", "min": [-6.0, 20.1, -3.2], "max": [-5.2, 24.4, -1.4], "color": {"color": "$wall", "material": "concrete"}},
    {"type": "block", "min": [5.2, 20.1, -3.2], "max": [6.0, 24.4, -1.4], "color": {"color": "$wall", "material": "concrete"}},
    {"type": "prop", "name": "Chimney Pot Stack", "at": [-5.6, 24.4, -2.3], "yaw": 90, "scale": 0.95},
    {"type": "prop", "name": "Chimney Pot Stack", "at": [5.6, 24.4, -2.3], "yaw": 90, "scale": 0.95},
    {"type": "prop", "name": "Wheelie Bin Grey", "at": [5.0, -5.8]},
]
for y in (H_LEVELS[1] + 0.55, H_LEVELS[3] + 0.55, H_LEVELS[4] + 0.55):
    for x in (-4.4, -1.4):
        h_extras.append({"type": "prop", "name": "Window Flower Box", "at": [x, y - 0.25, 5.3]})
write("Buildings/HaussmannApartment/building.json", {
    "format": "call-of-slop-building/1",
    "name": "Haussmann Apartment",
    "theme": "A Paris apartment block of the 1860s: cream limestone, wrought-iron balconies, a zinc mansard with chimney stacks; parquet, mouldings and marble halls inside",
    "category": "residential",
    "description": "Six storeys round a stone stairwell: a shop and its stock room beside the entrance hall, four floors of flats (salon, bedroom, kitchen), a balcony along the second floor, the attic maids' rooms behind a parapeted terrace, a zinc roof.",
    "author": "Call of Slop",
    "seed": 504,
    "wallThickness": 0.3,
    "storeyHeight": 3.2,
    "exterior": {"color": "$wall", "material": "concrete"},
    "interior": {"color": "$interior", "material": "none"},
    "floor": {"color": "$floor", "material": "wood"},
    "ceiling": "$ceiling",
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "cream limestone": {"wall": "#d8ccb0", "trim": "#ebe2cc", "front": "#24412f", "door": "#3a2a1e", "roof": "#6e7681", "floor": "#7a5534", "interior": "#ebe4d2", "ceiling": "#f4f0e6"},
        "grey limestone": {"wall": "#c4beb2", "trim": "#dcd8cc", "front": "#5e1c22", "door": "#1f2a3a", "roof": "#646c76", "floor": "#6a4a2e", "interior": "#e4e0d6", "ceiling": "#f2f0ea"},
        "honey stone": {"wall": "#d4b98c", "trim": "#e8d8b4", "front": "#1f3550", "door": "#4a2a1a", "roof": "#727a84", "floor": "#84603a", "interior": "#efe4cc", "ceiling": "#f6f0e2"},
    },
    "floors": floors,
    "stairs": h_stairs,
    "roof": {"type": "hip", "ridge": "x", "pitch": 40, "overhang": 0.35, "color": {"color": "$roof", "material": "metal"}},
    "extras": h_extras,
    "anchors": [
        {"type": "spawn", "name": "entrance hall", "at": [3.75, 3.2]},
        {"type": "spawn", "name": "second-floor salon", "floor": 2, "at": [-3.0, 2.6]},
        {"type": "spawn", "name": "attic", "floor": 5, "at": [-3.0, 1.4]},
        {"type": "point", "name": "shop", "at": [-3.0, 2.5]},
    ],
})

# ===================================================================== Cafe Brasserie (corner)
# 12 x 10 m on a street corner: shopfronts on the front (+Z) and the east side (+X). Hall and stairs on the west
# (x -6..-1.5), climbing to a flat roof with a parapet.
b_cols = {"W": [-5.5, -3.6, -4.4, 0.2], "E": [-3.1, -3.6, -2.0, 0.2]}
b_stairs = [{"floor": 0, "rect": b_cols["E"], "dir": "-z", "color": WOOD_STEP},
            {"floor": 1, "rect": b_cols["W"], "dir": "+z", "color": WOOD_STEP},
            {"floor": 2, "rect": b_cols["E"], "dir": "-z", "color": WOOD_STEP}]
b_floors = [
    {"height": 4.2,
     "rooms": [
         room("salle", "brasserie", [-1.5, -1, 6, 5], floor={"color": "#d8cfc0", "material": "tiles"}, wall="#e8d8b0",
              props=[{"name": "Zinc Bar Counter", "at": [0.4, -1.15]}]),
         room("hall", "corridor", [-6, -5, -1.5, 5], floor=HALL_FLOOR, wall="#e4dac4"),
         room("kitchen", "kitchen", [-1.5, -5, 3.5, -1], floor={"color": "#b8b4a8", "material": "tiles"}, wall="#ece8dc"),
         room("wc", "wc", [3.5, -5, 6, -1], floor={"color": "#c8d4d8", "material": "tiles"}),
     ],
     "openings": [
         door([2.25, 5], 1.2, front=True, material="glass", health=40),
         win([0.1, 5], 2.0, 0.5, 3.2, health=12),
         win([4.6, 5], 2.0, 0.5, 3.2, health=12),
         door([6, 3.6], 1.1, material="glass", health=40, hinge="right"),
         win([6, 1.0], 2.2, 0.5, 3.2, health=12),
         door([-3.75, 5], 1.0, color="$door"),
         door([-3.75, -5], 1.0, color="$door"),
         {"type": "arch", "at": [-1.5, 2.6], "width": 1.4},
         door([1.0, -1], 0.9, swing="out"),
         door([4.75, -1], 0.9),
         door([1.0, -5], 1.0, material="metal"),
         win([6, -3.0], 0.8, 1.5, 2.4),
     ]},
    {"height": 3.2,
     "rooms": [
         room("landing", "corridor", [-6, -5, -1.5, 5], floor={"color": "#6a4a2e", "material": "wood"}),
         room("salle haute", "salle", [-1.5, -1, 6, 5], floor={"color": "#7a5534", "material": "wood"}, wall="#d8c49a"),
         room("office", "office", [-1.5, -5, 6, -1], floor={"color": "#7a5534", "material": "wood"}),
     ],
     "openings": [
         door([-1.5, 2.6], 0.9, color="$door"),
         door([2.0, -1], 0.9),
         win([0.5, 5], 1.2, 0.4, 2.6), win([4.0, 5], 1.2, 0.4, 2.6),
         win([6, 0.8], 1.2, 0.4, 2.6), door([6, 3.4], 1.0, material="glass", health=30),
         win([-3.75, 5], 1.0, 0.9, 2.6),
         win([0.5, -5], 1.1, 0.9, 2.4), win([4.0, -5], 1.1, 0.9, 2.4), win([6, -3.0], 1.1, 0.9, 2.4),
     ]},
    {"height": 3.2,
     "rooms": [
         room("landing", "corridor", [-6, -5, -1.5, 5], floor={"color": "#6a4a2e", "material": "wood"}),
         room("living", "living", [-1.5, -1, 6, 5], floor=PARQUET),
         room("bedroom", "bedroom", [-1.5, -5, 2.5, -1], floor=PARQUET),
         room("bath", "bathroom", [2.5, -5, 6, -1], floor={"color": "#e0e4e0", "material": "tiles"}, wall="#d8e4e0"),
     ],
     "openings": [
         door([-1.5, 2.6], 0.9, color="$door"),
         door([0.5, -1], 0.9),
         door([4.25, -1], 0.9),
         win([0.5, 5], 1.2, 0.4, 2.6), win([4.0, 5], 1.2, 0.4, 2.6), win([6, 2.0], 1.2, 0.4, 2.6),
         win([-3.75, 5], 1.0, 0.9, 2.6),
         win([0.5, -5], 1.1, 0.9, 2.4), win([4.25, -5], 0.8, 1.5, 2.4), win([6, -3.0], 0.8, 1.5, 2.4),
     ]},
]
write("Buildings/CafeBrasserie/building.json", {
    "format": "call-of-slop-building/1",
    "name": "Cafe Brasserie",
    "theme": "A Paris corner brasserie: cream stone over a dark painted shopfront on two streets, red awnings, a zinc bar, red banquettes and marble tables; a dining room and the owners' flat above",
    "category": "commercial",
    "description": "A three-storey corner building: the brasserie with its zinc bar, kitchen and WC; an upstairs dining room and office; a flat on top; stairs up to a parapeted roof terrace.",
    "author": "Call of Slop",
    "seed": 505,
    "wallThickness": 0.3,
    "storeyHeight": 3.2,
    "exterior": {"color": "$wall", "material": "concrete"},
    "interior": {"color": "$interior", "material": "none"},
    "floor": {"color": "$floor", "material": "wood"},
    "ceiling": "$ceiling",
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "oxblood": {"wall": "#d6cab0", "trim": "#e8e0cc", "front": "#4a1418", "awning": "#8a1e22", "door": "#3a1a14", "roof": "#7a746a", "floor": "#7a5534", "interior": "#ece2cc", "ceiling": "#f2ead8"},
        "bottle green": {"wall": "#cfc6b4", "trim": "#e4ded0", "front": "#1f3a2a", "awning": "#2a5a3a", "door": "#1a2a20", "roof": "#727068", "floor": "#6a4a2e", "interior": "#e8e2d2", "ceiling": "#f2eee4"},
    },
    "floors": b_floors,
    "stairs": b_stairs,
    "roof": {"type": "flat", "parapet": 1.0, "color": {"color": "$roof", "material": "concrete"}},
    "extras": [
        {"type": "awning", "rect": [-1.4, 5.15, 6.2, 6.7], "height": 3.0, "color": "$awning"},
        {"type": "awning", "rect": [6.15, -0.9, 7.7, 4.95], "height": 3.0, "color": "$awning"},
        {"type": "balcony", "floor": 1, "rect": [6.15, 2.2, 7.0, 4.6], "railing": 1.0, "color": {"color": "#2a2a2a", "material": "metal"}},
        {"type": "block", "min": [-1.5, 3.35, 5.15], "max": [6.32, 4.15, 5.32], "color": "$front"},
        {"type": "block", "min": [6.15, 3.35, -1.0], "max": [6.32, 4.15, 5.32], "color": "$front"},
        {"type": "block", "min": [-6.15, 4.15, 5.15], "max": [6.4, 4.42, 5.4], "color": "$trim"},
        {"type": "block", "min": [6.15, 4.15, -5.15], "max": [6.4, 4.42, 5.4], "color": "$trim"},
        {"type": "prop", "name": "Chimney Pot Stack", "at": [2.0, 0, -4.3], "onRoof": True},
        {"type": "prop", "name": "Rooftop Condenser", "at": [4.2, 0, 1.5], "onRoof": True, "yaw": 90},
        {"type": "prop", "name": "Bistro Table", "at": [0.6, 7.6]},
        {"type": "prop", "name": "Bistro Table", "at": [3.9, 7.6]},
        {"type": "prop", "name": "Wheelie Bin", "at": [-0.2, -5.8]},
    ],
    "anchors": [
        {"type": "spawn", "name": "brasserie", "at": [3.0, 3.4]},
        {"type": "spawn", "name": "dining room", "floor": 1, "at": [2.5, 2.5]},
        {"type": "spawn", "name": "flat", "floor": 2, "at": [2.5, 2.5]},
        {"type": "point", "name": "bar", "at": [0.6, 1.6]},
    ],
})

# ===================================================================== Boulangerie
# 7 x 10 m, two storeys and a roof terrace: the bakery shop at the front, the bakehouse behind, the stairs in the
# back corner; the bakers' flat above with a little balcony.
write("Buildings/Boulangerie/building.json", {
    "format": "call-of-slop-building/1",
    "name": "Boulangerie",
    "theme": "A neighbourhood bakery: a painted wooden shopfront under a striped awning, a glass counter of pastries and racks of baguettes, a hot bakehouse behind, the bakers' flat upstairs",
    "category": "commercial",
    "description": "A narrow two-storey bakery: shop and bakehouse downstairs, the bakers' living room and bedroom upstairs with a front balcony, stairs up to a flat roof terrace.",
    "author": "Call of Slop",
    "seed": 506,
    "wallThickness": 0.25,
    "storeyHeight": 3.2,
    "exterior": {"color": "$wall", "material": "concrete"},
    "interior": {"color": "$interior", "material": "none"},
    "floor": {"color": "$floor", "material": "wood"},
    "ceiling": "$ceiling",
    "trim": "$trim",
    "door": "$door",
    "palettes": {
        "sky blue": {"wall": "#d8cdb4", "trim": "#ece6d8", "front": "#3a6a8a", "awning": "#e8e0d0", "door": "#2a4a62", "roof": "#7a746a", "floor": "#7a5534", "interior": "#f0e6d0", "ceiling": "#f6f0e2"},
        "burgundy": {"wall": "#cfc4ae", "trim": "#e6e0d2", "front": "#6a1e2a", "awning": "#c8a050", "door": "#4a1420", "roof": "#76706a", "floor": "#6a4a2e", "interior": "#efe4cc", "ceiling": "#f6f0e2"},
    },
    "floors": [
        {"height": 3.6,
         "rooms": [
             room("shop", "boulangerie", [-3.5, 0, 3.5, 5], floor={"color": "#c8b898", "material": "tiles"}, wall="#f0e0c0",
                  props=[{"name": "Bread Display Counter", "at": [-1.0, -0.55]}]),
             room("bakehouse", "bakehouse", [-3.5, -5, 0.3, 0], floor={"color": "#a8a49a", "material": "tiles"}, wall="#ece8dc"),
             room("stairs", "corridor", [0.3, -5, 3.5, 0], floor={"color": "#a8a49a", "material": "tiles"}, windows=False),
         ],
         "openings": [
             door([1.9, 5], 1.1, front=True, material="glass", health=40),
             win([-1.5, 5], 3.0, 0.5, 2.9, health=12),
             door([2.6, 0], 0.9),
             door([-1.6, 0], 0.9),
             door([-1.6, -5], 1.0, material="metal"),
             win([2.0, -5], 0.9, 1.5, 2.6),
         ]},
        {"height": 3.2,
         "rooms": [
             room("living", "living", [-3.5, 0, 3.5, 5], floor=PARQUET),
             room("bedroom", "bedroom", [-3.5, -5, 0.3, 0], floor=PARQUET),
             room("landing", "corridor", [0.3, -5, 3.5, 0], floor={"color": "#6a4a2e", "material": "wood"}, windows=False),
         ],
         "openings": [
             door([-1.4, 5], 1.0, material="glass", health=30),
             win([1.8, 5], 1.2, 0.5, 2.6),
             door([0.3, -4.3], 0.9),
             door([-1.6, 0], 0.9),
             win([-1.6, -5], 1.1, 0.9, 2.4),
             win([2.6, -5], 0.8, 1.2, 2.4),
         ]},
    ],
    "stairs": [
        {"floor": 0, "rect": [0.8, -3.7, 1.85, -1.5], "dir": "-z", "color": WOOD_STEP},
        {"floor": 1, "rect": [2.15, -3.7, 3.2, -1.5], "dir": "+z", "color": WOOD_STEP},
    ],
    "roof": {"type": "flat", "parapet": 1.0, "color": {"color": "$roof", "material": "concrete"}},
    "extras": [
        {"type": "awning", "rect": [-3.4, 5.13, 3.4, 6.4], "height": 2.95, "color": "$awning"},
        {"type": "balcony", "floor": 1, "rect": [-3.0, 5.13, 0.4, 5.95], "railing": 1.0, "color": {"color": "#2a2a2a", "material": "metal"}},
        {"type": "block", "min": [-3.62, 3.0, 5.13], "max": [3.62, 3.62, 5.27], "color": "$front"},
        {"type": "block", "min": [-3.62, 0.1, 5.13], "max": [3.62, 0.45, 5.25], "color": "$front"},
        {"type": "prop", "name": "Chimney Pot Stack", "at": [-1.6, 0, -4.3], "onRoof": True, "scale": 0.8},
        {"type": "prop", "name": "Window Flower Box", "at": [1.8, 5.1, 5.27]},
        {"type": "prop", "name": "Bicycle", "at": [-2.4, 6.0]},
    ],
    "anchors": [
        {"type": "spawn", "name": "shop", "at": [1.5, 2.8]},
        {"type": "spawn", "name": "flat", "floor": 1, "at": [0.0, 2.5]},
    ],
})
print("buildings written")
