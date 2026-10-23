#!/usr/bin/env python3
"""Writes three Normandy country buildings into Buildings/ (general buildings: any map can place them):

- Norman Farmhouse (Buildings/NormanFarmhouse): a long two-storey stone farmhouse under a steep slate roof, the big
  farm kitchen ("la salle") with its hearth, a hall and stairs, a parlour; three bedrooms upstairs. Stone chimneys on
  both gables.
- Half-Timbered Barn (Buildings/HalfTimberedBarn): a colombage barn on a stone plinth, the threshing floor with big
  double doors front and back, a byre for the cows, and a hayloft under the roof with loft doors in both gables and over
  the front doors: the long sight lines over the hedges.
- Norman Cottage (Buildings/NormanCottage): a half-timbered thatched cottage, kitchen-living room and a back room
  downstairs, two attic bedrooms up a steep stair.

The half-timbering is the building's own `block` extras: dark oak studs, rails and plates a few centimetres proud of
the plaster, worked out here so they never cross a door or a window. They are building boxes, so they're drawn in the
buildings' instanced pass with everything else.

Usage: python3 tools/world/bocage_buildings.py <repo root>
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."


def R(v):
    return round(float(v), 3)


def timber_frame(rects, floors, openings_by_floor, t, color, stud=0.85, plinth=0.0, skip_floors=()):
    """Half-timber extras for the outside walls of the bounding rect of `rects` (one rect per floor, x0 z0 x1 z1).
    floors: list of (floor_y, height) above the ground. openings_by_floor: per floor, a list of (side, centre, width,
    sill, head) with side N/S/E/W and centre along the wall (x for N/S, z for E/W)."""
    ex = []
    proud = 0.05
    for fi, ((fy, fh), rect, ops) in enumerate(zip(floors, rects, openings_by_floor)):
        if fi in skip_floors:
            continue
        x0, z0, x1, z1 = rect
        sides = {"S": (x0, x1, z1 + t / 2, True, 1), "N": (x0, x1, z0 - t / 2, True, -1),
                 "W": (z0, z1, x0 - t / 2, False, -1), "E": (z0, z1, x1 + t / 2, False, 1)}
        for side, (a0, a1, face, along_x, out) in sides.items():
            gaps = [(c - w / 2, c + w / 2, s, h) for (sd, c, w, s, h) in ops if sd == side]
            y_lo = max(fy, plinth) if fi == 0 else fy
            y_hi = fy + fh

            def box(u0, u1, v0, v1):
                if u1 - u0 < 0.02 or v1 - v0 < 0.02:
                    return
                f0, f1 = (face, face + proud) if out > 0 else (face - proud, face)
                if along_x:
                    ex.append({"type": "block", "min": [R(u0), R(v0), R(f0)], "max": [R(u1), R(v1), R(f1)], "color": color})
                else:
                    ex.append({"type": "block", "min": [R(f0), R(v0), R(u0)], "max": [R(f1), R(v1), R(u1)], "color": color})

            # studs, clear of the openings (each opening gets a post either side)
            posts = []
            n = max(2, int(round((a1 - a0) / stud)))
            for k in range(n + 1):
                posts.append(a0 + (a1 - a0) * k / n)
            for (g0, g1, s, h) in gaps:
                posts += [g0 - 0.08, g1 + 0.08]
            posts = sorted(posts)
            kept = []
            for p in posts:
                if any(g0 - 0.12 < p < g1 + 0.12 for (g0, g1, s, h) in gaps) and not any(abs(p - (g0 - 0.08)) < 1e-6 or abs(p - (g1 + 0.08)) < 1e-6 for (g0, g1, s, h) in gaps):
                    # a stud inside an opening: keep the bit under the sill and over the head only
                    for (g0, g1, s, h) in gaps:
                        if g0 - 0.12 < p < g1 + 0.12:
                            if s > 0.3:
                                box(p - 0.07, p + 0.07, y_lo, fy + s)
                            if fy + h < y_hi - 0.3:
                                box(p - 0.07, p + 0.07, fy + h, y_hi)
                    continue
                if kept and p - kept[-1] < 0.3:
                    continue
                kept.append(p)
            for p in kept:
                p = min(max(p, a0 - t / 2 + 0.07), a1 + t / 2 - 0.07)
                box(p - 0.08, p + 0.08, y_lo, y_hi)
            # rails: the sole plate, a mid rail at 1.0 m and the top plate, broken at the openings
            for ry, rh in ((y_lo, 0.16), (fy + 1.0, 0.12), (y_hi - 0.18, 0.18)):
                u = a0 - t / 2
                for (g0, g1, s, h) in sorted(gaps):
                    if fy + s < ry + rh and fy + h > ry:
                        box(u, g0, ry, ry + rh)
                        u = g1
                box(u, a1 + t / 2, ry, ry + rh)
    return ex


def write_building(folder, data, furnishing=None):
    d = os.path.join(REPO, "Buildings", folder)
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, "building.json"), "w") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    for name, rule in (furnishing or {}).items():
        os.makedirs(os.path.join(d, "Furnishing"), exist_ok=True)
        with open(os.path.join(d, "Furnishing", name + ".json"), "w") as f:
            json.dump(rule, f, indent=2)
            f.write("\n")


def win(at, w=0.9, sill=0.9, head=2.0):
    return {"type": "window", "at": at, "width": w, "sill": sill, "head": head}


def openings_on_walls(rect, ops):
    """(side, centre, width, sill, head) for the openings that sit on rect's outside walls."""
    x0, z0, x1, z1 = rect
    out = []
    for o in ops:
        x, z = o["at"]
        w = o.get("width", 1.0)
        s = o.get("sill", 0.0 if o["type"] in ("door", "arch") else 0.9)
        h = o.get("head", 2.1 if o["type"] in ("door", "arch") else 2.2)
        if abs(z - z1) < 1e-6:
            out.append(("S", x, w, s, h))
        elif abs(z - z0) < 1e-6:
            out.append(("N", x, w, s, h))
        elif abs(x - x0) < 1e-6:
            out.append(("W", z, w, s, h))
        elif abs(x - x1) < 1e-6:
            out.append(("E", z, w, s, h))
    return out


# ============================================================================================ Norman Farmhouse
def farmhouse():
    t = 0.5
    H0, H1 = 2.9, 2.8
    rect = [-7, -3.5, 7, 3.5]
    g_ops = [
        {"type": "door", "at": [-0.2, 3.5], "width": 1.0, "front": True, "material": "wood"},
        {"type": "door", "at": [-4.0, -3.5], "width": 0.95, "material": "wood"},
        {"type": "door", "at": [-1, 1.9], "width": 0.9},
        {"type": "door", "at": [2, 2.3], "width": 0.9, "hinge": "right"},
        win([-5.6, 3.5]), win([-2.6, 3.5]), win([4.0, 3.5]), win([5.9, 3.5]),
        win([-6.0, -3.5], 0.7, 1.1, 1.9), win([4.5, -3.5], 0.8),
        win([-7, 2.0], 0.8), win([7, -1.0], 0.8),
    ]
    u_ops = [
        {"type": "door", "at": [-1, -1.0], "width": 0.9},
        {"type": "door", "at": [2, -2.4], "width": 0.9},
        {"type": "door", "at": [2, 2.2], "width": 0.9, "hinge": "right"},
        win([-5.0, 3.5]), win([-2.6, 3.5]), win([0.3, 3.5], 0.7), win([4.5, 3.5]),
        win([-4.0, -3.5], 0.8), win([0.0, -3.5], 0.6, 1.2, 1.9), win([4.6, -3.5], 0.8),
        win([-7, 1.2], 0.7), win([7, 1.5], 0.7), win([7, -1.8], 0.7),
    ]
    data = {
        "format": "call-of-slop-building/1",
        "name": "Norman Farmhouse",
        "theme": "A Normandy stone longere: thick limestone walls, small deep-set windows, a steep slate roof, stone chimneys; a farm kitchen with a hearth, cider and oak furniture",
        "category": "residential",
        "description": "A long two-storey stone farmhouse: the farm kitchen with its hearth, the hall and stairs and a parlour downstairs; three bedrooms upstairs; firewood, milk churns and a bench outside.",
        "author": "Call of Slop",
        "seed": 505,
        "wallThickness": t,
        "storeyHeight": H0,
        "exterior": {"color": "$wall", "material": "brick"},
        "interior": {"color": "$interior", "material": "none"},
        "floor": {"color": "$floor", "material": "wood"},
        "ceiling": {"color": "$ceiling", "material": "wood"},
        "trim": "$trim",
        "door": "$door",
        "palettes": {
            "limestone": {"wall": "#b4a98e", "interior": "#e8dfc8", "floor": "#6e5236", "ceiling": "#5e4630", "trim": "#4a4038", "door": "#3e5a6e", "roof": "#3a3e46"},
            "granite": {"wall": "#8c887e", "interior": "#e4dccb", "floor": "#6a4e34", "ceiling": "#58422e", "trim": "#3e3a34", "door": "#7a2e26", "roof": "#33363c"},
            "whitewash": {"wall": "#ddd6c4", "interior": "#efe8d6", "floor": "#74563a", "ceiling": "#62492f", "trim": "#4e5a3a", "door": "#2f5a3e", "roof": "#3c4048"},
        },
        "floors": [
            {
                "rooms": [
                    {"id": "salle", "type": "kitchen", "rules": "salle", "rect": [-7, -3.5, -1, 3.5], "floor": {"color": "#8a7e6a", "material": "tiles"}},
                    {"id": "hall", "type": "hall", "rect": [-1, -3.5, 2, 3.5], "floor": {"color": "#8a7e6a", "material": "tiles"}},
                    {"id": "parlour", "type": "living", "rect": [2, -3.5, 7, 3.5]},
                ],
                "openings": g_ops,
            },
            {
                "height": H1,
                "rooms": [
                    {"id": "landing", "type": "corridor", "rect": [-1, -3.5, 2, 3.5]},
                    {"id": "bed1", "type": "bedroom", "rect": [-7, -3.5, -1, 3.5]},
                    {"id": "bed2", "type": "bedroom", "rect": [2, -3.5, 7, 0]},
                    {"id": "bed3", "type": "kidsroom", "rect": [2, 0, 7, 3.5]},
                ],
                "openings": u_ops,
            },
        ],
        "stairs": [{"floor": 0, "rect": [0.65, -1.95, 1.75, 1.05], "dir": "-z", "color": {"color": "#5a4430", "material": "wood"}}],
        "roof": {"type": "gable", "ridge": "x", "pitch": 50, "overhang": 0.35, "color": {"color": "$roof", "material": "tiles"}},
        "extras": [
            {"type": "block", "min": [-7.95, 0, -0.6], "max": [-7.25, 11.8, 0.6], "color": {"color": "$wall", "material": "brick"}},
            {"type": "block", "min": [-8.05, 11.8, -0.7], "max": [-7.15, 12.0, 0.7], "color": "$trim"},
            {"type": "block", "min": [7.25, 5.0, -0.6], "max": [7.95, 11.8, 0.6], "color": {"color": "$wall", "material": "brick"}},
            {"type": "block", "min": [7.15, 11.8, -0.7], "max": [8.05, 12.0, 0.7], "color": "$trim"},
            {"type": "block", "min": [-0.95, 0, 3.75], "max": [0.55, 0.12, 4.5], "color": {"color": "#8a857a", "material": "concrete"}},
            {"type": "prop", "name": "Firewood Stack", "at": [-5.2, 4.1], "yaw": 0},
            {"type": "prop", "name": "Bench", "at": [2.9, 4.25], "yaw": 0},
            {"type": "prop", "name": "Milk Churns", "at": [-8.0, 2.2], "yaw": 90},
            {"type": "prop", "name": "Flower Pots", "at": [1.2, 4.0]},
            {"type": "prop", "name": "Leaning Farm Tools", "at": [-2.6, -4.05], "yaw": 180},
            {"type": "prop", "name": "Cider Barrel", "at": [-6.2, -4.2]},
            {"type": "prop", "name": "Cider Barrel", "at": [-5.4, -4.25]},
            {"type": "prop", "name": "Wheelbarrow", "at": [7.9, -2.0], "yaw": 80},
        ],
        "anchors": [
            {"type": "spawn", "name": "farm kitchen", "at": [-4.0, 0.5]},
            {"type": "spawn", "name": "parlour", "at": [4.5, 0.0]},
            {"type": "spawn", "name": "big bedroom", "floor": 1, "at": [-4.0, 0.0]},
            {"type": "point", "name": "front door", "at": [-0.2, 5.0]},
        ],
    }
    salle = {
        "format": "call-of-slop-furnishing/1",
        "type": "salle",
        "description": "A Normandy farm kitchen: the hearth, a long oak table with chairs, a dresser, the range and the sink, cider and apples.",
        "items": [
            {"props": ["Fireplace"], "place": "wall", "prefer": "far"},
            {"id": "table", "props": ["Dining Table"], "place": "centre"},
            {"props": ["Wooden Chair", "Dining Chair"], "place": "around", "of": "table", "count": [3, 5]},
            {"props": ["Stacked Plates", "Apple Crate", "Cooking Pot"], "place": "on", "of": "table", "y": 0.77, "count": [1, 2]},
            {"id": "counter", "props": ["Kitchen Counter"], "place": "wall", "count": [1, 2]},
            {"props": ["Kitchen Sink"], "place": "wall"},
            {"props": ["Kettle", "Knife Block", "Frying Pan"], "place": "on", "of": "counter", "y": 0.9, "count": [1, 2]},
            {"props": ["Bookshelf", "Chest of Drawers"], "place": "wall", "chance": 0.8},
            {"props": ["Cider Barrel", "Apple Crate", "Potato Sacks"], "place": "corner", "count": [1, 2]},
            {"props": ["Cat", "Broom", "Bucket"], "place": "floor", "count": [1, 2]},
        ],
    }
    # stone farmhouse: no half-timbering; quoins and a plinth course in darker stone
    ex = data["extras"]
    for (x, z) in ((-7, -3.5), (7, -3.5), (-7, 3.5), (7, 3.5)):
        sx = 1 if x > 0 else -1
        sz = 1 if z > 0 else -1
        for k in range(9):
            y = 0.3 + k * 0.62
            lx = 0.55 if k % 2 == 0 else 0.3
            lz = 0.3 if k % 2 == 0 else 0.55
            cx, cz = x + sx * t / 2, z + sz * t / 2
            ex.append({"type": "block", "min": [R(min(cx, cx - sx * lx)), R(y), R(min(cz, cz + sz * 0.04))],
                       "max": [R(max(cx, cx - sx * lx)), R(y + 0.32), R(max(cz, cz + sz * 0.04))], "color": "$trim"})
            ex.append({"type": "block", "min": [R(min(cx, cx + sx * 0.04)), R(y), R(min(cz, cz - sz * lz))],
                       "max": [R(max(cx, cx + sx * 0.04)), R(y + 0.32), R(max(cz, cz - sz * lz))], "color": "$trim"})
    write_building("NormanFarmhouse", data, {"salle": salle})


# ============================================================================================ Half-Timbered Barn
def barn():
    t = 0.3
    H0, H1 = 3.4, 3.2
    rect = [-9, -5, 9, 5]
    g_ops = [
        {"type": "door", "at": [-4.6, 5], "width": 2.0, "head": 2.9, "material": "wood", "swing": "out", "hinge": "right"},
        {"type": "door", "at": [-2.4, 5], "width": 2.0, "head": 2.9, "material": "wood", "swing": "out", "hinge": "left"},
        {"type": "door", "at": [2.6, -5], "width": 2.0, "head": 2.9, "material": "wood", "swing": "out", "hinge": "left", "open": True},
        {"type": "door", "at": [5, 0.0], "width": 1.2},
        {"type": "door", "at": [9, 2.5], "width": 1.2, "front": True, "material": "wood"},
        win([1.5, 5], 1.0, 1.7, 2.8), win([-7.6, 5], 1.0, 1.7, 2.8), win([-0.6, -5], 1.0, 1.7, 2.8), win([-7.6, -5], 1.0, 1.7, 2.8),
        win([7.0, 5], 1.0, 1.6, 2.6), win([7.0, -5], 1.0, 1.6, 2.6), win([-9, 1.5], 1.0, 1.7, 2.8), win([9, -2.0], 1.0, 1.6, 2.6),
    ]
    u_ops = [
        {"type": "hole", "at": [-9, -0.5], "width": 1.8, "sill": 0.0, "head": 2.4},
        {"type": "hole", "at": [9, 0.5], "width": 1.8, "sill": 0.0, "head": 2.4},
        {"type": "hole", "at": [-3.5, 5], "width": 1.8, "sill": 0.0, "head": 2.3},
        {"type": "hole", "at": [3.5, -5], "width": 1.4, "sill": 0.9, "head": 2.2},
        win([4.5, 5], 1.0, 0.9, 2.1), win([-7.0, -5], 1.0, 0.9, 2.1),
    ]
    data = {
        "format": "call-of-slop-building/1",
        "name": "Half-Timbered Barn",
        "theme": "A Normandy colombage barn: dark oak studding over ochre cob on a stone plinth, a steep brown-tiled roof; hay, carts, cider barrels and cows inside",
        "category": "industrial",
        "description": "A half-timbered barn: the threshing floor with double doors front and back, a byre for the cows with its own door, and a hayloft with loft doors in both gables and over the front: the farm's lookout.",
        "author": "Call of Slop",
        "seed": 515,
        "wallThickness": t,
        "storeyHeight": H0,
        "doorHeight": 2.2,
        "exterior": {"color": "$wall", "material": "none"},
        "interior": {"color": "$interior", "material": "wood"},
        "floor": {"color": "$floor", "material": "wood"},
        "ceiling": {"color": "$ceiling", "material": "wood"},
        "trim": "$trim",
        "door": "$door",
        "palettes": {
            "ochre cob": {"wall": "#c9b07c", "interior": "#7a6046", "floor": "#6e5a40", "ceiling": "#5a4432", "trim": "#3e2c1e", "door": "#5e4026", "roof": "#7a4a34", "plinth": "#8a857a"},
            "cream cob": {"wall": "#ddd0b0", "interior": "#7e6448", "floor": "#6e5a40", "ceiling": "#5a4432", "trim": "#3a2a1c", "door": "#4e3a24", "roof": "#3c3e44", "plinth": "#7c786e"},
        },
        "floors": [
            {
                "rooms": [
                    {"id": "barn", "type": "barn-floor", "rect": [-9, -5, 5, 5], "floor": {"color": "#6e5e44", "material": "dirt"}},
                    {"id": "byre", "type": "byre", "rect": [5, -5, 9, 5], "floor": {"color": "#6a6052", "material": "concrete"}},
                ],
                "openings": g_ops,
            },
            {
                "height": H1,
                "rooms": [{"id": "loft", "type": "hayloft", "rect": [-9, -5, 9, 5]}],
                "openings": u_ops,
            },
        ],
        "stairs": [{"floor": 0, "rect": [-3.4, -4.65, -0.2, -3.65], "dir": "+x", "color": {"color": "#5e4630", "material": "wood"}}],
        "roof": {"type": "gable", "ridge": "x", "pitch": 48, "overhang": 0.5, "color": {"color": "$roof", "material": "tiles"}},
        "extras": [
            {"type": "prop", "name": "Hay Bale Stack", "at": [-11.0, 3.0], "yaw": 90},
            {"type": "prop", "name": "Round Hay Bale", "at": [-11.2, -1.0], "yaw": 90},
            {"type": "prop", "name": "Tipped Farm Cart", "at": [3.0, 7.6], "yaw": 200},
            {"type": "prop", "name": "Muck Heap", "at": [11.6, -2.8], "yaw": 20},
            {"type": "prop", "name": "Milk Churns", "at": [10.0, 4.4], "yaw": 0},
            {"type": "prop", "name": "Chicken", "at": [-1.0, 7.2], "yaw": 40},
            {"type": "prop", "name": "Chicken", "at": [0.0, 8.0], "yaw": 190},
            {"type": "prop", "name": "Loose Straw", "at": [-3.5, 6.6]},
            {"type": "prop", "name": "Loose Straw", "at": [2.6, -6.6]},
        ],
        "anchors": [
            {"type": "spawn", "name": "threshing floor", "at": [-1.0, 2.0]},
            {"type": "spawn", "name": "byre", "at": [7.0, -2.5]},
            {"type": "spawn", "name": "hayloft", "floor": 1, "at": [3.0, 0.0]},
            {"type": "hill", "name": "threshing floor", "at": [-3.5, 0.0]},
            {"type": "point", "name": "hayloft", "floor": 1, "at": [-3.5, 0.0]},
        ],
    }
    # stone plinth round the outside (0.6 m), then the timber frame above it
    ex = data["extras"]
    pl = {"color": "$plinth", "material": "brick"}
    x0, z0, x1, z1 = rect
    o = t / 2
    plinth_h = 0.6
    door_x = [(-5.6, -1.4)]
    for (a, b) in ((x0 - o, -5.6), (-1.4, x1 + o)):
        ex.append({"type": "block", "min": [R(a), 0, R(z1 + o)], "max": [R(b), plinth_h, R(z1 + o + 0.08)], "color": pl})
    for (a, b) in ((x0 - o, 1.6), (3.6, x1 + o)):
        ex.append({"type": "block", "min": [R(a), 0, R(z0 - o - 0.08)], "max": [R(b), plinth_h, R(z0 - o)], "color": pl})
    ex.append({"type": "block", "min": [R(x0 - o - 0.08), 0, R(z0 - o)], "max": [R(x0 - o), plinth_h, R(z1 + o)], "color": pl})
    ex.append({"type": "block", "min": [R(x1 + o), 0, R(z0 - o)], "max": [R(x1 + o + 0.08), plinth_h, 1.9], "color": pl})
    ex.append({"type": "block", "min": [R(x1 + o), 0, 3.1], "max": [R(x1 + o + 0.08), plinth_h, R(z1 + o)], "color": pl})
    frame = timber_frame([rect, rect], [(0.1, H0), (0.1 + H0, H1)], [openings_on_walls(rect, g_ops), openings_on_walls(rect, u_ops)], t + 0.16,
                         "$trim", stud=0.8, plinth=plinth_h)
    ex += frame
    barn_floor = {
        "format": "call-of-slop-furnishing/1",
        "type": "barn-floor",
        "description": "The threshing floor: round bales and stacked hay, a farm cart, the cider barrels, tools and hens.",
        "items": [
            {"props": ["Hay Bale Stack"], "place": "wall", "count": [2, 3]},
            {"props": ["Round Hay Bale"], "place": "wall", "count": [1, 3]},
            {"props": ["Cider Barrel"], "place": "corner", "count": [1, 3]},
            {"props": ["Tipped Farm Cart", "Hay Bale", "Wheelbarrow"], "place": "floor", "count": [1, 2], "gap": 0.8},
            {"props": ["Leaning Farm Tools", "Bucket", "Chicken", "Chicken", "Loose Straw", "Loose Straw"], "place": "floor", "count": [2, 5]},
        ],
    }
    byre = {
        "format": "call-of-slop-furnishing/1",
        "type": "byre",
        "description": "The byre: water troughs along the wall, hay for the cows, milk churns and buckets.",
        "items": [
            {"props": ["Stone Water Trough"], "place": "wall", "count": [1, 2]},
            {"props": ["Hay Bale"], "place": "wall", "count": [2, 4], "gap": 0.02},
            {"props": ["Milk Churns"], "place": "corner"},
            {"props": ["Grazing Cow", "Dairy Cow"], "place": "floor", "count": [1, 2], "gap": 0.6},
            {"props": ["Bucket", "Loose Straw", "Loose Straw"], "place": "floor", "count": [1, 3]},
        ],
    }
    hayloft = {
        "format": "call-of-slop-furnishing/1",
        "type": "hayloft",
        "description": "The hayloft: bales stacked under the roof, loose straw, a cat.",
        "items": [
            {"props": ["Hay Bale"], "place": "wall", "count": [6, 10], "gap": 0.02},
            {"props": ["Hay Bale Stack"], "place": "wall", "count": [1, 2]},
            {"props": ["Hay Bale"], "place": "floor", "count": [2, 4], "gap": 0.6},
            {"props": ["Loose Straw", "Loose Straw", "Cat", "Plank"], "place": "floor", "count": [2, 4]},
        ],
    }
    write_building("HalfTimberedBarn", data, {"barn-floor": barn_floor, "byre": byre, "hayloft": hayloft})


# ============================================================================================ Norman Cottage
def cottage():
    t = 0.3
    H0, H1 = 2.7, 2.4
    rect = [-5, -3.25, 5, 3.25]
    g_ops = [
        {"type": "door", "at": [-1.4, 3.25], "width": 0.95, "front": True, "material": "wood"},
        {"type": "door", "at": [3.0, -3.25], "width": 0.9, "material": "wood"},
        {"type": "door", "at": [1, 1.8], "width": 0.9},
        win([-3.4, 3.25], 0.9, 0.9, 1.9), win([0.4, 3.25], 0.8, 0.9, 1.9), win([3.2, 3.25], 0.9, 0.9, 1.9),
        win([-2.0, -3.25], 0.8, 1.0, 1.9), win([-5, -0.4], 0.7, 1.0, 1.9), win([5, 0.0], 0.8, 0.9, 1.9),
    ]
    u_ops = [
        {"type": "door", "at": [1, -1.2], "width": 0.9},
        win([-3.0, 3.25], 0.8, 0.8, 1.7), win([3.0, 3.25], 0.8, 0.8, 1.7), win([-5, 1.4], 0.6, 0.8, 1.7), win([5, 1.4], 0.6, 0.8, 1.7),
        win([3.0, -3.25], 0.7, 0.8, 1.7),
    ]
    data = {
        "format": "call-of-slop-building/1",
        "name": "Norman Cottage",
        "theme": "A Normandy chaumiere: half-timbered walls of oak studding and pale cob on a flint plinth, a steep thatched roof, a low beamed kitchen-living room and two attic bedrooms",
        "category": "residential",
        "description": "A thatched half-timbered cottage: a kitchen-living room with the hearth and the stairs, a back room, and two bedrooms in the attic.",
        "author": "Call of Slop",
        "seed": 525,
        "wallThickness": t,
        "storeyHeight": H0,
        "exterior": {"color": "$wall", "material": "none"},
        "interior": {"color": "$interior", "material": "none"},
        "floor": {"color": "$floor", "material": "wood"},
        "ceiling": {"color": "$ceiling", "material": "wood"},
        "trim": "$trim",
        "door": "$door",
        "palettes": {
            "pale cob": {"wall": "#e2d6b8", "interior": "#eee4cc", "floor": "#6e5236", "ceiling": "#5a4230", "trim": "#3a2a1c", "door": "#5a6e3a", "roof": "#8e7a52", "plinth": "#7e7a70"},
            "ochre cob": {"wall": "#cfae78", "interior": "#ece0c4", "floor": "#6a4e34", "ceiling": "#56402e", "trim": "#34261a", "door": "#3a5a7a", "roof": "#857048", "plinth": "#8a857a"},
            "rose cob": {"wall": "#d6b4a2", "interior": "#efe4d0", "floor": "#6e5236", "ceiling": "#5a4230", "trim": "#3a2a1c", "door": "#7a2e26", "roof": "#8a7650", "plinth": "#7a766c"},
        },
        "floors": [
            {
                "rooms": [
                    {"id": "living", "type": "living", "rules": "kitchen", "rect": [-5, -3.25, 1, 3.25], "floor": {"color": "#8a7a66", "material": "tiles"}},
                    {"id": "back", "type": "bedroom", "rect": [1, -3.25, 5, 3.25]},
                ],
                "openings": g_ops,
            },
            {
                "height": H1,
                "rooms": [
                    {"id": "attic1", "type": "bedroom", "rect": [-5, -3.25, 1, 3.25]},
                    {"id": "attic2", "type": "kidsroom", "rect": [1, -3.25, 5, 3.25]},
                ],
                "openings": u_ops,
            },
        ],
        "stairs": [{"floor": 0, "rect": [-4.8, -1.75, -3.7, 0.65], "dir": "-z", "color": {"color": "#5a4430", "material": "wood"}}],
        "roof": {"type": "gable", "ridge": "x", "pitch": 55, "overhang": 0.55, "color": {"color": "$roof", "material": "dirt"}},
        "extras": [
            {"type": "block", "min": [-5.85, 0, -0.5], "max": [-5.17, 11.8, 0.4], "color": {"color": "$plinth", "material": "brick"}},
            {"type": "prop", "name": "Flower Pots", "at": [-0.2, 3.85]},
            {"type": "prop", "name": "Bench", "at": [-3.4, 3.95], "yaw": 0},
            {"type": "prop", "name": "Firewood Stack", "at": [6.0, -1.0], "yaw": 90},
            {"type": "prop", "name": "Watering Can", "at": [1.2, 4.2], "yaw": 30},
        ],
        "anchors": [
            {"type": "spawn", "name": "kitchen-living room", "at": [-1.8, 0.2]},
            {"type": "spawn", "name": "attic", "floor": 1, "at": [3.0, 0.5]},
            {"type": "point", "name": "front door", "at": [-1.4, 4.6]},
        ],
    }
    ex = data["extras"]
    pl = {"color": "$plinth", "material": "brick"}
    x0, z0, x1, z1 = rect
    o = t / 2
    ph = 0.45
    ex.append({"type": "block", "min": [R(x0 - o), 0, R(z1 + o)], "max": [-1.9, ph, R(z1 + o + 0.08)], "color": pl})
    ex.append({"type": "block", "min": [-0.9, 0, R(z1 + o)], "max": [R(x1 + o), ph, R(z1 + o + 0.08)], "color": pl})
    ex.append({"type": "block", "min": [R(x0 - o), 0, R(z0 - o - 0.08)], "max": [2.5, ph, R(z0 - o)], "color": pl})
    ex.append({"type": "block", "min": [3.5, 0, R(z0 - o - 0.08)], "max": [R(x1 + o), ph, R(z0 - o)], "color": pl})
    ex.append({"type": "block", "min": [R(x0 - o - 0.08), 0, R(z0 - o)], "max": [R(x0 - o), ph, R(z1 + o)], "color": pl})
    ex.append({"type": "block", "min": [R(x1 + o), 0, R(z0 - o)], "max": [R(x1 + o + 0.08), ph, R(z1 + o)], "color": pl})
    ex += timber_frame([rect, rect], [(0.1, H0), (0.1 + H0, H1)], [openings_on_walls(rect, g_ops), openings_on_walls(rect, u_ops)], t + 0.16,
                       "$trim", stud=0.7, plinth=ph)
    write_building("NormanCottage", data)


if __name__ == "__main__":
    farmhouse()
    barn()
    cottage()
    print("wrote Norman Farmhouse, Half-Timbered Barn, Norman Cottage")
