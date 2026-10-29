#!/usr/bin/env python3
"""Writes the starter set of buildings (Buildings/<Name>/building.json plus their own props and rules) and the Dev
pack's Building Yard map. Run from the repository root: python3 tools/buildings/gen_starter.py

The JSON it writes is the real content: edit the files by hand afterwards if you like (this script is just how they were
first laid out, and a worked example of building files in code)."""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def write(path, obj):
    full = os.path.join(ROOT, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w") as f:
        f.write(dumps(obj) + "\n")


def dumps(obj, ind=0):
    """JSON with short lists and small objects on one line, like the hand-written files."""
    sp = "  " * ind
    if isinstance(obj, dict):
        one = json.dumps(obj)
        if len(one) <= 150 and not any(isinstance(v, (dict, list)) and len(json.dumps(v)) > 60 for v in obj.values()):
            return one.replace('{"', '{ "').replace('"}', '" }').replace('}', ' }').replace('  }', ' }') if False else one
        items = [f'{sp}  {json.dumps(k)}: {dumps(v, ind + 1)}' for k, v in obj.items()]
        return "{\n" + ",\n".join(items) + "\n" + sp + "}"
    if isinstance(obj, list):
        one = json.dumps(obj)
        if len(one) <= 150 and all(not isinstance(v, (dict, list)) or len(json.dumps(v)) <= 150 for v in obj) and (len(obj) < 2 or not isinstance(obj[0], dict) or len(one) <= 150):
            return one
        items = [f"{sp}  {dumps(v, ind + 1)}" for v in obj]
        return "[\n" + ",\n".join(items) + "\n" + sp + "]"
    return json.dumps(obj)


def prop(name, desc, parts, collision="solid", collider=None, breakable=None, category=None):
    d = {"format": "call-of-slop-prop/1", "name": name}
    if category:
        d["category"] = category
    d["description"] = desc
    d["collision"] = collision
    if collider:
        d["collider"] = collider
    d["scatter"] = {"randomYaw": False, "yawStep": 90}
    if breakable:
        d["breakable"] = breakable
    d["parts"] = parts
    return d


def P(size, offset, color, material=None, **k):
    p = {"size": size, "offset": offset, "color": color}
    if material:
        p["material"] = material
    p.update(k)
    return p


# ============================================================================== building-local props

def local_props():
    booth = prop("Diner Booth", "A 1950s diner booth: two red vinyl benches with high backs facing over a chrome-edged table.", [
        P([1.2, 0.45, 0.5], [0, 0.225, -0.72], "#b3262b"),
        P([1.2, 0.7, 0.12], [0, 0.8, -0.93], "#b3262b"),
        P([1.24, 0.06, 0.16], [0, 1.18, -0.93], "chrome"),
        P([1.2, 0.45, 0.5], [0, 0.225, 0.72], "#b3262b"),
        P([1.2, 0.7, 0.12], [0, 0.8, 0.93], "#b3262b"),
        P([1.24, 0.06, 0.16], [0, 1.18, 0.93], "chrome"),
        P([1.1, 0.05, 0.7], [0, 0.75, 0], "#e8e2cf"),
        P([1.14, 0.03, 0.74], [0, 0.72, 0], "chrome"),
        P([0.12, 0.7, 0.12], [0, 0.36, 0], "chrome"),
        P([0.5, 0.03, 0.5], [0, 0.015, 0], "chrome"),
        P([0.08, 0.14, 0.08], [0.4, 0.82, 0.2], "#c8102e"),
        P([0.07, 0.12, 0.07], [0.3, 0.81, 0.24], "#f2c230"),
    ], collider=[{"size": [1.2, 1.1, 2.0]}], breakable={"health": 120, "breakBy": "all", "sound": "soft", "debris": {"count": 14, "size": [0.06, 0.25], "colors": ["#b3262b", "chrome", "#e8e2cf"]}})
    juke = prop("Jukebox", "A glowing chrome and amber jukebox.", [
        P([0.8, 1.1, 0.55], [0, 0.55, 0], "#5a2a1a", "wood"),
        P([0.84, 0.35, 0.58], [0, 1.25, 0], "chrome"),
        P([0.62, 0.3, 0.02], [0, 1.25, 0.29], "#ffb347", anim={"type": "pulse", "period": 2.4, "color": "#ffb347", "off": "#7a4a20", "glow": 0.9}),
        P([0.6, 0.5, 0.02], [0, 0.6, 0.28], "#3a1f12"),
        P([0.08, 1.0, 0.04], [-0.34, 0.6, 0.29], "#e2433c", anim={"type": "blink", "period": 1.3, "duty": 0.6, "color": "#ff5040", "glow": 0.7}),
        P([0.08, 1.0, 0.04], [0.34, 0.6, 0.29], "#3cc2e2", anim={"type": "blink", "period": 1.3, "duty": 0.6, "phase": 0.5, "color": "#40d0ff", "glow": 0.7}),
    ], collider=[{"size": [0.84, 1.42, 0.58]}], breakable={"health": 150, "breakBy": "all", "sound": "glass", "debris": {"count": 12, "size": [0.05, 0.2]}})
    pump = prop("Fuel Pump", "A 1980s petrol pump: white and red cabinet, a price display and a hose on its holster.", [
        P([0.7, 0.18, 0.5], [0, 0.09, 0], "concrete"),
        P([0.6, 1.5, 0.42], [0, 0.93, 0], "white", "metal"),
        P([0.62, 0.25, 0.44], [0, 1.8, 0], "#c8102e"),
        P([0.4, 0.22, 0.02], [0, 1.35, 0.22], "#1a1a1a"),
        P([0.3, 0.08, 0.025], [0, 1.4, 0.225], "#7cff6a", anim={"type": "flicker", "rate": 3, "duty": 0.95, "color": "#7cff6a", "glow": 0.5}),
        P([0.12, 0.25, 0.1], [0.36, 1.0, 0.1], "darkgrey"),
        P([0.04, 0.9, 0.04], [0.4, 0.55, 0.18], "rubber"),
    ], collider=[{"size": [0.7, 1.95, 0.5]}])
    rack = prop("Pallet Rack", "Heavy warehouse racking, 2.7 m wide: orange beams on blue uprights, two levels of loaded pallets.", [
        P([0.08, 3.0, 1.0], [-1.32, 1.5, 0], "#2a4f9e", "metal"),
        P([0.08, 3.0, 1.0], [1.32, 1.5, 0], "#2a4f9e", "metal"),
        P([2.6, 0.12, 0.08], [0, 1.4, -0.46], "orange"), P([2.6, 0.12, 0.08], [0, 1.4, 0.46], "orange"),
        P([2.6, 0.12, 0.08], [0, 2.8, -0.46], "orange"), P([2.6, 0.12, 0.08], [0, 2.8, 0.46], "orange"),
        P([1.2, 0.14, 0.95], [-0.62, 0.07, 0], "pallet", "wood"), P([1.1, 0.8, 0.85], [-0.62, 0.55, 0], "cardboard"),
        P([1.2, 0.14, 0.95], [0.62, 0.07, 0], "pallet", "wood"), P([1.0, 1.0, 0.8], [0.62, 0.64, 0], "#3d6b4a"),
        P([1.2, 0.14, 0.95], [-0.62, 1.53, 0], "pallet", "wood"), P([1.1, 0.7, 0.85], [-0.62, 1.95, 0], "cardboard"),
        P([1.2, 0.14, 0.95], [0.62, 1.53, 0], "pallet", "wood"), P([1.05, 0.55, 0.8], [0.62, 1.88, 0], "tape"),
        P([1.2, 0.14, 0.95], [0.62, 2.93, 0], "pallet", "wood"), P([1.0, 0.5, 0.8], [0.62, 3.25, 0], "cardboard"),
    ], collider=[{"size": [2.72, 3.5, 1.0]}])
    lift = prop("Car Lift", "A two-post car lift: red posts, swing arms down at the floor.", [
        P([0.32, 2.9, 0.32], [-1.6, 1.45, 0], "#c0262d", "metal"),
        P([0.32, 2.9, 0.32], [1.6, 1.45, 0], "#c0262d", "metal"),
        P([0.6, 0.04, 0.6], [-1.6, 0.02, 0], "steel"), P([0.6, 0.04, 0.6], [1.6, 0.02, 0], "steel"),
        P([3.5, 0.12, 0.2], [0, 2.95, 0], "#c0262d"),
        P([0.9, 0.08, 0.12], [-1.15, 0.2, 0.45], "steel", yaw=25), P([0.9, 0.08, 0.12], [-1.15, 0.2, -0.45], "steel", yaw=-25),
        P([0.9, 0.08, 0.12], [1.15, 0.2, 0.45], "steel", yaw=-25), P([0.9, 0.08, 0.12], [1.15, 0.2, -0.45], "steel", yaw=25),
        P([0.2, 0.4, 0.14], [-1.6, 1.2, 0.2], "#333333"),
    ], collider=[{"size": [0.32, 3.0, 0.32], "offset": [-1.6, 1.5, 0]}, {"size": [0.32, 3.0, 0.32], "offset": [1.6, 1.5, 0]}])
    bench = prop("Workbench", "A heavy workbench with a vise, a pegboard of tools behind it.", [
        P([2.0, 0.08, 0.7], [0, 0.88, 0], "#6b4b2a", "wood"),
        P([0.08, 0.84, 0.08], [-0.94, 0.42, -0.3], "darkgrey"), P([0.08, 0.84, 0.08], [0.94, 0.42, -0.3], "darkgrey"),
        P([0.08, 0.84, 0.08], [-0.94, 0.42, 0.3], "darkgrey"), P([0.08, 0.84, 0.08], [0.94, 0.42, 0.3], "darkgrey"),
        P([1.9, 0.04, 0.6], [0, 0.2, 0], "#6b4b2a", "wood"),
        P([2.0, 0.9, 0.03], [0, 1.4, -0.34], "#b59a6a"),
        P([0.18, 0.12, 0.2], [0.75, 0.98, 0.25], "#2e5a8a"),
        P([0.3, 0.03, 0.03], [-0.5, 1.5, -0.31], "steel"), P([0.03, 0.3, 0.03], [-0.2, 1.4, -0.31], "steel"),
        P([0.25, 0.05, 0.03], [0.3, 1.6, -0.31], "red"), P([0.05, 0.22, 0.03], [0.6, 1.35, -0.31], "yellow"),
    ], collider=[{"size": [2.0, 0.92, 0.7], "offset": [0, 0.46, 0]}, {"size": [2.0, 0.9, 0.05], "offset": [0, 1.4, -0.34]}],
        breakable={"health": 160, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.06, 0.35]}})
    tool_chest = prop("Tool Chest", "A red rolling tool chest, drawers full.", [
        P([0.7, 1.0, 0.45], [0, 0.55, 0], "#c0262d", "metal"),
        P([0.72, 0.04, 0.47], [0, 1.07, 0], "steel"),
        P([0.6, 0.02, 0.02], [0, 0.8, 0.23], "steel"), P([0.6, 0.02, 0.02], [0, 0.6, 0.23], "steel"), P([0.6, 0.02, 0.02], [0, 0.4, 0.23], "steel"),
        P([0.1, 0.1, 0.1], [-0.28, 0.05, -0.15], "rubber"), P([0.1, 0.1, 0.1], [0.28, 0.05, -0.15], "rubber"),
        P([0.1, 0.1, 0.1], [-0.28, 0.05, 0.15], "rubber"), P([0.1, 0.1, 0.1], [0.28, 0.05, 0.15], "rubber"),
    ], breakable={"health": 220, "breakBy": "all", "sound": "metal", "debris": {"count": 10, "size": [0.05, 0.2]}})
    write("Buildings/Diner/Props/DinerBooth.json", booth)
    write("Buildings/Diner/Props/Jukebox.json", juke)
    write("Buildings/GasStation/Props/FuelPump.json", pump)
    write("Buildings/Warehouse/Props/PalletRack.json", rack)
    write("Buildings/Garage/Props/CarLift.json", lift)
    write("Buildings/Garage/Props/Workbench.json", bench)
    write("Buildings/Garage/Props/ToolChest.json", tool_chest)


def B(name, folder, **k):
    d = {"format": "call-of-slop-building/1", "name": name}
    d.update(k)
    write(f"Buildings/{folder}/building.json", d)


def room(id, type, rect=None, rects=None, **k):
    r = {"id": id, "type": type}
    if rect:
        r["rect"] = rect
    if rects:
        r["rects"] = rects
    r.update(k)
    return r


def door(x, z, w=0.9, **k):
    d = {"type": "door", "at": [x, z], "width": w}
    d.update(k)
    return d


def win(x, z, w=1.2, sill=0.9, head=2.2, **k):
    d = {"type": "window", "at": [x, z], "width": w, "sill": sill, "head": head}
    d.update(k)
    return d


def arch(x, z, w=1.0, **k):
    d = {"type": "arch", "at": [x, z], "width": w}
    d.update(k)
    return d


TILE = "tiles"


def two_storey():
    B("Two-Storey House", "TwoStoreyHouse",
      theme="1970s suburban family home: white render, teal trim and door, brown hip roof, orange carpet, wood-panel and avocado interiors",
      category="residential",
      description="A two-storey family home: hall with stairs, living room, dining room, kitchen, utility and WC downstairs; three bedrooms, a bathroom and a front balcony upstairs.",
      author="Call of Slop", seed=21, wallThickness=0.25, storeyHeight=2.9,
      exterior={"color": "$wall", "material": "concrete"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": "none"}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "white and teal": {"wall": "#e6e2d6", "interior": "#e8d9b8", "floor": "#b0683a", "ceiling": "#f2eee4", "trim": "#2e8a86", "door": "#2e8a86", "roof": "#5a3e2e", "panel": "#8a6236"},
          "cream and brown": {"wall": "#d9c9a0", "interior": "#dccfae", "floor": "#7a5a3a", "ceiling": "#f1ede2", "trim": "#5c3d24", "door": "#5c3d24", "roof": "#3e3a36", "panel": "#9a7a4a"},
          "pale blue": {"wall": "#b9c9d4", "interior": "#e4e0d0", "floor": "#6a7f4a", "ceiling": "#f0efe8", "trim": "#f4f4f4", "door": "#b03a2e", "roof": "#454a52", "panel": "#7a6a52"},
      },
      floors=[
          {"autoWindows": {"spacing": 2.6, "width": 1.2, "sill": 0.9, "head": 2.2},
           "rooms": [
               room("hall", "hall", [-1.5, -1, 1.5, 4.5], floor={"color": "$panel", "material": "wood"}),
               room("living", "living", [-5, -1, -1.5, 4.5]),
               room("dining", "dining", [1.5, 0.5, 5, 4.5], floor={"color": "$panel", "material": "wood"}),
               room("wc", "wc", [1.5, -1, 5, 0.5], floor={"color": "#9db08a", "material": TILE}, windows=False),
               room("kitchen", "kitchen", [-1.5, -4.5, 5, -1], floor={"color": "#c9c08a", "material": TILE}, wall="#d8e0a8"),
               room("utility", "laundry", [-5, -4.5, -1.5, -1], floor={"color": "#9a9a92", "material": TILE}),
           ],
           "openings": [door(0.6, 4.5, 1.0, front=True, hinge="right"), door(-1.5, 3.75), door(1.5, 2.5), door(1.5, -0.25, 0.9),
                        door(0.0, -1), door(-1.5, -2.75), door(3.5, -4.5, 0.9, hinge="right"), win(-3.25, 4.5, 1.8)]},
          {"height": 2.8, "autoWindows": {"spacing": 2.6, "width": 1.2, "sill": 0.85, "head": 2.2},
           "rooms": [
               room("landing", "corridor", rects=[[-1.5, -1, 1.5, 4.5], [1.5, -1, 5, 0.5]], floor={"color": "$floor", "material": "none"}),
               room("master", "bedroom", [-5, -1, -1.5, 4.5]),
               room("bedroom2", "bedroom", [1.5, 0.5, 5, 4.5]),
               room("bedroom3", "kidsroom", [-5, -4.5, 1.5, -1]),
               room("bathroom", "bathroom", [1.5, -4.5, 5, -1], floor={"color": "#7fb3b0", "material": TILE}, wall="#cfe4e2",
                    autoWindows={"spacing": 2, "width": 0.7, "sill": 1.3, "head": 2.0}),
           ],
           "openings": [door(-1.5, -0.3), door(1.5, 2.6), door(-0.4, -1), door(3.25, -1, 0.9), door(-3.25, 4.5, 0.9, hinge="right")]},
      ],
      stairs=[{"floor": 0, "rect": [-1.33, 0.35, -0.33, 3.3], "dir": "-z", "color": {"color": "$panel", "material": "wood"}}],
      roof={"type": "hip", "pitch": 28, "overhang": 0.45, "color": {"color": "$roof", "material": "tiles"}},
      extras=[
          {"type": "balcony", "floor": 1, "rect": [-4.6, 4.63, -1.9, 6.0], "railing": 1.0, "color": "$trim"},
          {"type": "awning", "rect": [-0.4, 4.63, 1.6, 5.6], "height": 2.5, "color": "$trim"},
          {"type": "prop", "name": "Wheelie Bin", "at": [5.9, -3.2], "yaw": 270},
          {"type": "prop", "name": "Kettle BBQ", "at": [1.0, -5.8], "yaw": 180},
          {"type": "prop", "name": "Bicycle", "at": [2.4, 5.4], "yaw": 90},
          {"type": "prop", "name": "Garden Gnome", "at": [-2.2, 6.3], "yaw": 20},
          {"type": "prop", "name": "Electric Meter", "at": [5.15, 0.0, 2.5], "yaw": 90},
      ],
      anchors=[{"type": "spawn", "name": "master bedroom", "floor": 1, "at": [-3.2, 1.5]}, {"type": "spawn", "name": "kitchen", "at": [1.5, -2.8]},
               {"type": "point", "name": "balcony", "floor": 1, "at": [-3.25, 5.3]}])


def apartments():
    def flat(prefix, sx):
        """One flat's rooms; sx = 1 west flat, -1 east (mirrored)."""
        def R(x0, z0, x1, z1):
            a, b = sorted([x0 * sx, x1 * sx])
            return [a, z0, b, z1]
        return [
            room(f"{prefix}living", "living", R(-8, 1, -2, 5), openTo=[f"{prefix}kitchen"]),
            room(f"{prefix}kitchen", "kitchen", R(-8, -2, -4.5, 1), floor={"color": "#a8b8a0", "material": TILE}),
            room(f"{prefix}bath", "bathroom", R(-4.5, -2, -2, 1), floor={"color": "#9ab0c0", "material": TILE}, wall="#cdd9de",
                 autoWindows={"spacing": 2, "width": 0.6, "sill": 1.4, "head": 2.0}),
            room(f"{prefix}bed", "bedroom", R(-8, -5, -2, -2)),
        ]

    def flat_doors(sx):
        return [door(-2 * sx, 3.0), door(-3.25 * sx, 1, 0.9), door(-6.2 * sx, -2)]
    hall = room("stairs", "corridor", [-2, -5, 2, 5], floor={"color": "#8a8a84", "material": "concrete"}, wall="#b7c2b0", windows=False)
    common = {"autoWindows": {"spacing": 2.5, "width": 1.3, "sill": 0.9, "head": 2.2}}
    ground = dict(common, rooms=[hall] + flat("w_", 1) + flat("e_", -1),
                  openings=[door(0, 5, 1.2, front=True, material="metal", color="$door"), door(0, -5, 1.0, material="metal", color="$door")]
                  + flat_doors(1) + flat_doors(-1) + [win(0, 5 if False else -5, 1.0) if False else win(-1.2, 5, 0.6, sill=1.0, head=2.2)])
    upper = dict(common, repeat=True, rooms=[dict(hall)] + flat("w_", 1) + flat("e_", -1),
                 openings=flat_doors(1) + flat_doors(-1) + [win(0, 5, 1.6), win(0, -5, 1.6), door(-5, 5, 0.9), door(5, 5, 0.9, hinge="right")])
    B("Apartment Block", "ApartmentBlock",
      theme="1960s concrete council flats: grey precast concrete, blue steel doors, a bare stairwell, lino floors and worn second-hand furniture",
      category="residential",
      description="Three storeys of flats round a concrete stairwell (more with the \"floors\" variant): two flats a floor, each with a living room and kitchen, a bedroom and a bathroom; balconies, roof access.",
      author="Call of Slop", seed=31, wallThickness=0.3, storeyHeight=3.0, floorCount=3,
      exterior={"color": "$wall", "material": "concrete"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": "none"}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "grey concrete": {"wall": "#8f8f8a", "interior": "#d9d3c0", "floor": "#8a6a52", "ceiling": "#e6e3da", "trim": "#5a6a7a", "door": "#2f5a9a", "roof": "#5c5c58"},
          "brown brick": {"wall": "#7a5040", "interior": "#e1d8c2", "floor": "#6a5a4a", "ceiling": "#e9e6dc", "trim": "#d8d0c0", "door": "#3e6a3a", "roof": "#4a4a48"},
      },
      floors=[ground, upper],
      stairs=[{"floor": 0, "rect": [-1.85, -3.5, -0.75, 0.5], "dir": "-z", "color": {"color": "#9a9a94", "material": "concrete"}},
              {"floor": 1, "rect": [0.75, -3.5, 1.85, 0.5], "dir": "+z", "alt": {"rect": [-1.85, -3.5, -0.75, 0.5], "dir": "-z"}, "color": {"color": "#9a9a94", "material": "concrete"}}],
      roof={"type": "flat", "parapet": 1.0, "color": {"color": "$roof", "material": "concrete"}},
      extras=[
          {"type": "balcony", "floor": 1, "rect": [-6.2, 5.15, -3.8, 6.4], "railing": 1.05, "color": {"color": "#9a9a94", "material": "concrete"}},
          {"type": "balcony", "floor": 1, "rect": [3.8, 5.15, 6.2, 6.4], "railing": 1.05, "color": {"color": "#9a9a94", "material": "concrete"}},
          {"type": "awning", "rect": [-1.4, 5.15, 1.4, 6.3], "height": 2.6, "color": {"color": "#9a9a94", "material": "concrete"}},
          {"type": "prop", "name": "Plastic Water Tank", "at": [5.0, 0, -2.5], "onRoof": True},
          {"type": "prop", "name": "TV Antenna", "at": [-4.0, 0, -3.0], "onRoof": True},
          {"type": "prop", "name": "Satellite Dish", "at": [-6.5, 0, 3.5], "yaw": 180, "onRoof": True},
          {"type": "prop", "name": "Dumpster", "at": [-6.0, -6.4], "yaw": 0},
          {"type": "prop", "name": "Wheelie Bin Grey", "at": [-3.8, -6.2], "yaw": 0},
          {"type": "prop", "name": "Post Box", "at": [2.0, 6.0], "yaw": 0},
      ],
      anchors=[{"type": "spawn", "name": "stairwell", "at": [0, 3.5]}, {"type": "spawn", "name": "west flat", "floor": 1, "at": [-5, 3]},
               {"type": "spawn", "name": "east flat", "floor": 1, "at": [5, 3]}, {"type": "hill", "name": "roof", "floor": 0, "at": [0, 0]}])


def corner_shop():
    B("Corner Shop", "CornerShop",
      theme="Family-run 1970s corner grocery: yellow brick, a red striped awning, white lino, crammed shelves, a cluttered back office",
      category="commercial",
      description="A corner grocery: the shop floor with big front windows, a store room with a back door, a back office and a WC.",
      author="Call of Slop", seed=41, wallThickness=0.25, storeyHeight=3.2,
      exterior={"color": "$wall", "material": "brick"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": TILE}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "yellow brick": {"wall": "#c9a45c", "interior": "#efe8d2", "floor": "#e4e0d4", "ceiling": "#f4f2ea", "trim": "#c8102e", "door": "#3a3a3a", "roof": "#4c4c4a"},
          "green grocer": {"wall": "#8a5a3e", "interior": "#e4ecd8", "floor": "#d8d2c0", "ceiling": "#f2f2ea", "trim": "#2e7d32", "door": "#2e7d32", "roof": "#454545"},
      },
      floors=[{
          "rooms": [
              room("shop", "shop", [-4.5, -0.5, 4.5, 4], autoWindows={"spacing": 3, "width": 1.6, "sill": 0.6, "head": 2.4, "health": 20}),
              room("store", "storage", [-4.5, -4, 0.5, -0.5], floor={"color": "#9a968c", "material": "concrete"}, windows=False),
              room("office", "office", [0.5, -4, 3, -0.5], autoWindows={"spacing": 2, "width": 0.9, "sill": 1.0, "head": 2.2}),
              room("wc", "wc", [3, -4, 4.5, -0.5], windows=False),
          ],
          "openings": [door(1.6, 4, 1.1, front=True, material="glass", health=40), win(-2.4, 4, 3.0, sill=0.5, head=2.5, health=25),
                       win(3.5, 4, 1.2, sill=0.5, head=2.5, health=25),
                       door(-1.8, -0.5), door(1.75, -0.5, 0.9), door(3.75, -0.5, 0.9), door(-3.0, -4, 1.0, material="metal")]}],
      roof={"type": "flat", "parapet": 0.8, "color": {"color": "$roof", "material": "concrete"}},
      extras=[
          {"type": "awning", "rect": [-4.65, 4.13, 4.65, 5.5], "height": 2.75, "color": "$trim"},
          {"type": "prop", "name": "Shop Sign", "at": [0, 2.95, 4.13]},
          {"type": "prop", "name": "Neon Open Sign", "at": [3.5, 0.95, 3.6], "yaw": 180},
          {"type": "prop", "name": "Newspaper Box", "at": [-3.6, 6.0], "yaw": 0},
          {"type": "prop", "name": "Sandwich Board", "at": [3.4, 6.1], "yaw": 15},
          {"type": "prop", "name": "Wheelie Bin", "at": [-1.4, -5.0], "yaw": 180},
          {"type": "prop", "name": "Rooftop Condenser", "at": [2.5, 0, -2.0], "onRoof": True},
          {"type": "prop", "name": "Window AC Unit", "at": [1.2, 2.2, -4.13], "yaw": 180},
      ],
      anchors=[{"type": "spawn", "name": "store room", "at": [-2.5, -2.5]}, {"type": "flag", "name": "shop counter", "at": [0, 1.5]}])


def diner():
    write("Buildings/Diner/Furnishing/diner.json", {
        "format": "call-of-slop-furnishing/1", "type": "diner",
        "description": "The Diner's dining room: booths along the window wall, a counter run with stools along the back, a jukebox, a vending machine.",
        "items": [
            {"id": "counter", "props": ["Kitchen Counter"], "place": "wall", "count": [5, 7], "prefer": "far"},
            {"props": ["Bar Stool"], "place": "front", "of": "counter", "count": [5, 7], "distance": 0.25},
            {"props": ["Stacked Plates", "Coffee Maker", "Toaster"], "place": "on", "of": "counter", "y": 0.9, "count": [3, 5]},
            {"props": ["Diner Booth"], "place": "wall", "count": [3, 5], "prefer": "near"},
            {"props": ["Jukebox"], "place": "corner"},
            {"props": ["Glowing Vending Machine"], "place": "wall", "chance": 0.8},
            {"props": ["Ceiling Fan"], "place": "ceiling", "count": 2},
            {"props": ["Pedal Bin", "Office Plant"], "place": "corner", "count": [1, 2]},
        ]})
    B("Diner", "Diner",
      theme="1950s chrome roadside diner: mint-green panels and chrome trim, a black and white checker floor, red vinyl booths, neon",
      category="commercial",
      description="A roadside diner: the dining room with booths, a long counter with stools and a jukebox; a kitchen, restrooms and a store room behind.",
      author="Call of Slop", seed=51, wallThickness=0.25, storeyHeight=3.4,
      exterior={"color": "$wall", "material": "metal"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": TILE}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "mint and chrome": {"wall": "#9fd6c2", "interior": "#f2ead2", "floor": "#e8e8e8", "ceiling": "#fbf7ec", "trim": "#d8dde2", "door": "#c8102e", "roof": "#7a8088", "accent": "#c8102e"},
          "cherry red": {"wall": "#c8323a", "interior": "#f4ecd8", "floor": "#e0e0e0", "ceiling": "#fbf7ec", "trim": "#e6e9ec", "door": "#1f6fb2", "roof": "#6a6e74", "accent": "#1f6fb2"},
      },
      floors=[{
          "rooms": [
              room("dining", "diner", [-7, -0.5, 7, 4], autoWindows={"spacing": 2.3, "width": 1.8, "sill": 0.85, "head": 2.5, "health": 10}),
              room("kitchen", "kitchen", [-7, -4, 2, -0.5], floor={"color": "#b8b4a8", "material": TILE}, wall="#e6e6dc",
                   autoWindows={"spacing": 3, "width": 1.0, "sill": 1.2, "head": 2.3}),
              room("restrooms", "restroom", [2, -4, 4.5, -0.5], floor={"color": "#c8d8e0", "material": TILE}, windows=False),
              room("store", "storage", [4.5, -4, 7, -0.5], floor={"color": "#9a968c", "material": "concrete"}, windows=False),
          ],
          "openings": [door(0, 4, 1.6, front=True, material="glass", health=40), door(-2.2, -0.5, 1.0, swing="out"), door(3.25, -0.5, 0.9),
                       door(5.75, -0.5, 0.9), door(-5.5, -4, 1.0, material="metal"), door(7, 1.75, 1.0, material="glass", health=40)]}],
      roof={"type": "flat", "parapet": 0.5, "color": {"color": "$roof", "material": "metal"}},
      extras=[
          {"type": "block", "min": [-7.2, 3.35, 4.12], "max": [7.2, 3.65, 4.3], "color": "$accent"},
          {"type": "block", "min": [-7.2, 0.0, 4.12], "max": [7.2, 0.35, 4.3], "color": "$trim"},
          {"type": "prop", "name": "Rotating Diner Sign", "at": [-4.5, 0, 1.5], "onRoof": True},
          {"type": "prop", "name": "Rooftop HVAC Unit", "at": [3.0, 0, -2.0], "onRoof": True},
          {"type": "prop", "name": "Neon Sign", "at": [5.0, 2.2, 4.3]},
          {"type": "prop", "name": "Dumpster", "at": [-2.5, -5.6], "yaw": 0},
          {"type": "prop", "name": "Trash Bag", "at": [-4.0, -5.0]},
          {"type": "prop", "name": "Gas Cylinders", "at": [-0.8, -4.6], "yaw": 180},
      ],
      anchors=[{"type": "spawn", "name": "kitchen", "at": [-4.5, -2.2]}, {"type": "hill", "name": "dining room", "at": [0, 1.8]}])


def office():
    stairs_hall = room("stairhall", "corridor", [3, -5, 7, 5], floor={"color": "#8c8c88", "material": "tiles"}, windows=False)
    B("Office Building", "OfficeBuilding",
      theme="1980s accountancy office: beige brick, smoked-glass windows, grey carpet tiles, beige desks, filing cabinets and rubber plants",
      category="commercial",
      description="A two-storey office (more floors with the \"floors\" variant): reception, a meeting room, a break room and restrooms downstairs, an open-plan office and two private offices on each floor above, roof plant.",
      author="Call of Slop", seed=61, wallThickness=0.25, storeyHeight=3.2, floorCount=2,
      exterior={"color": "$wall", "material": "brick"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": "tiles"}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "beige brick": {"wall": "#c2aa86", "interior": "#e8e4d8", "floor": "#7c7f84", "ceiling": "#f2f2ee", "trim": "#3a3a36", "door": "#6a5a48", "roof": "#55555a"},
          "red brick": {"wall": "#8e4a3a", "interior": "#e4e6e0", "floor": "#6a6e74", "ceiling": "#f0f0ec", "trim": "#e0ddd4", "door": "#2d4f6e", "roof": "#4a4a4e"},
      },
      floors=[
          {"autoWindows": {"spacing": 2.4, "width": 1.5, "sill": 0.9, "head": 2.4, "health": 2},
           "rooms": [
               room("reception", "reception", [-3, 0, 3, 5]),
               room("meeting", "meeting", [-7, 0, -3, 5]),
               room("breakroom", "breakroom", [-7, -5, -1, 0], floor={"color": "#b8b0a0", "material": "tiles"}),
               room("restrooms", "restroom", [-1, -5, 3, 0], floor={"color": "#c8d4d8", "material": "tiles"}, windows=False),
               stairs_hall,
           ],
           "openings": [door(0.5, 5, 1.6, front=True, material="glass", health=40), door(3, 3.0, 1.0), door(-3, 2.5), door(-2, 0), door(1, 0, 0.9),
                        door(5, -5, 1.0, material="metal")]},
          {"repeat": True, "autoWindows": {"spacing": 2.4, "width": 1.5, "sill": 0.9, "head": 2.4, "health": 2},
           "rooms": [
               room("open office", "office", [-7, -5, 3, 1]),
               room("office1", "office", [-7, 1, -2, 5]),
               room("office2", "office", [-2, 1, 3, 5]),
               dict(stairs_hall),
           ],
           "openings": [door(3, -4.3, 1.0), door(3, 3.0, 1.0), door(-4.5, 1), door(0.5, 1)]},
      ],
      stairs=[{"floor": 0, "rect": [3.2, -3.5, 4.3, 0.5], "dir": "-z", "color": {"color": "#8c8c88", "material": "tiles"}},
              {"floor": 1, "rect": [5.7, -3.5, 6.8, 0.5], "dir": "+z", "alt": {"rect": [3.2, -3.5, 4.3, 0.5], "dir": "-z"}, "color": {"color": "#8c8c88", "material": "tiles"}}],
      roof={"type": "flat", "parapet": 1.0, "color": {"color": "$roof", "material": "concrete"}},
      extras=[
          {"type": "awning", "rect": [-1.2, 5.13, 2.2, 6.6], "height": 3.0, "color": "$trim", "posts": True},
          {"type": "prop", "name": "Rooftop HVAC Unit", "at": [-4.0, 0, -2.0], "onRoof": True},
          {"type": "prop", "name": "Air Duct", "at": [-1.0, 0, -2.0], "yaw": 90, "onRoof": True},
          {"type": "prop", "name": "Satellite Dish", "at": [1.5, 0, 3.0], "onRoof": True},
          {"type": "prop", "name": "Bike Rack", "at": [-4.5, 6.2]},
          {"type": "prop", "name": "Office Plant", "at": [2.8, 5.6]},
      ],
      anchors=[{"type": "spawn", "name": "reception", "at": [0, 2.5]}, {"type": "spawn", "name": "open office", "floor": 1, "at": [-2, -2]},
               {"type": "hill", "name": "open office", "floor": 1, "at": [-2, -2]}])


def warehouse():
    write("Buildings/Warehouse/Furnishing/warehouse.json", {
        "format": "call-of-slop-furnishing/1", "type": "warehouse",
        "description": "The Warehouse's hall: pallet racks along the walls, rows of stacked pallets and crates, a forklift.",
        "items": [
            {"props": ["Pallet Rack"], "place": "wall", "count": [6, 9]},
            {"props": ["Forklift"], "place": "floor"},
            {"id": "stack", "props": ["Pallet Stack", "Wooden Crate", "Cardboard Stack"], "place": "floor", "count": [8, 14], "gap": 0.6},
            {"props": ["Pallet", "Small Crate", "Oil Drum", "Cardboard Box", "Cardboard Box Large"], "place": "floor", "count": [4, 8], "gap": 0.3},
            {"props": ["Trash Bag", "Flattened Cardboard", "Plank"], "place": "floor", "count": [2, 4]},
        ]})
    B("Warehouse", "Warehouse",
      theme="1990s distribution warehouse: blue corrugated metal walls, a bare concrete floor, yellow safety rails, a mezzanine office over the racks",
      category="industrial",
      description="A tall warehouse hall with pallet racks, a loading bay and a roller-door opening, a mezzanine with stairs, a front office, a break room and a WC.",
      author="Call of Slop", seed=71, wallThickness=0.3, storeyHeight=7.0,
      exterior={"color": "$wall", "material": "metal"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": "concrete"}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "blue metal": {"wall": "#3d5f8a", "interior": "#c9ccc8", "floor": "#8d8c86", "ceiling": "#b9bdc0", "trim": "#f2c230", "door": "#9aa0a6", "roof": "#6e757c"},
          "green metal": {"wall": "#4f6b4a", "interior": "#cdcabe", "floor": "#8a877e", "ceiling": "#bdbfb8", "trim": "#f28c28", "door": "#8a8f86", "roof": "#62665e"},
      },
      floors=[{
          "rooms": [
              room("hall", "warehouse", [-12, -8, 12, 4], autoWindows={"spacing": 5, "width": 3.0, "sill": 4.6, "head": 5.8, "health": 1}),
              room("office", "office", [-12, 4, 0, 8], autoWindows={"spacing": 2.5, "width": 1.4, "sill": 1.0, "head": 2.3}),
              room("breakroom", "breakroom", [0, 4, 8, 8], autoWindows={"spacing": 2.5, "width": 1.4, "sill": 1.0, "head": 2.3}),
              room("wc", "restroom", [8, 4, 12, 8], windows=False),
          ],
          "openings": [door(-6, 8, 1.0, front=True, material="glass", health=40), arch(12, -2.5, 4.0, head=4.6), arch(-5, -8, 4.5, head=4.8),
                       door(-6, 4, 1.0, material="metal"), door(4, 4, 1.0), door(10, 4, 0.9), door(8, 6, 0.9), door(12, 2.2, 1.0, material="metal")]}],
      roof={"type": "gable", "ridge": "x", "pitch": 10, "overhang": 0.3, "color": {"color": "$roof", "material": "metal"}},
      extras=[
          {"type": "platform", "rect": [3.0, -7.85, 11.85, -3.0], "height": 3.2, "railing": 1.05, "color": {"color": "#6e6e6a", "material": "metal"},
           "stairs": {"rect": [0.9, -7.6, 2.9, -6.6], "dir": "+x"}},
          {"type": "prop", "name": "Office Desk", "at": [6.0, 3.31, -7.2], "yaw": 0},
          {"type": "prop", "name": "Office Chair", "at": [6.0, 3.31, -6.3], "yaw": 180},
          {"type": "prop", "name": "Filing Cabinet", "at": [8.0, 3.31, -7.4], "yaw": 0},
          {"type": "prop", "name": "Filing Cabinet", "at": [8.6, 3.31, -7.4], "yaw": 0},
          {"type": "prop", "name": "Archive Boxes", "at": [10.6, 3.31, -6.8], "yaw": 270},
          {"type": "prop", "name": "Water Cooler", "at": [4.0, 3.31, -7.5], "yaw": 0},
          {"type": "prop", "name": "Desk Phone", "at": [6.4, 4.08, -7.2], "yaw": 0},
          {"type": "block", "min": [12.15, 0, -5.0], "max": [14.5, 1.1, 0.0], "color": {"color": "#8d8c86", "material": "concrete"}},
          {"type": "prop", "name": "Box Truck", "at": [17.0, -2.5], "yaw": 90},
          {"type": "prop", "name": "Pallet Stack", "at": [13.4, 1.1, -4.2], "yaw": 0},
          {"type": "prop", "name": "Dumpster", "at": [-10.0, -9.3]},
          {"type": "prop", "name": "Floodlight Tower", "at": [13.5, 5.5], "yaw": 225},
      ],
      anchors=[{"type": "spawn", "name": "office", "at": [-6, 6]}, {"type": "spawn", "name": "hall", "at": [-2, -2]}, {"type": "hill", "name": "hall", "at": [0, -2]},
               {"type": "point", "name": "mezzanine", "at": [7, -5.5]}])


def gas_station():
    B("Gas Station", "GasStation",
      theme="Roadside 1980s gas station: white walls with a red stripe, a big red and white canopy over four pumps, a cramped kiosk shop",
      category="commercial",
      description="A small kiosk shop with a counter, a store room and a WC, and a canopy over two pump islands out front.",
      author="Call of Slop", seed=81, wallThickness=0.25, storeyHeight=3.0,
      exterior={"color": "$wall", "material": "none"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": TILE}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "red and white": {"wall": "#eeeeea", "interior": "#f2efe4", "floor": "#d6d4cc", "ceiling": "#f6f6f2", "trim": "#c8102e", "door": "#4a4a4a", "roof": "#8a8a8a"},
          "blue and yellow": {"wall": "#e8e4d2", "interior": "#eef0f2", "floor": "#c8ccd0", "ceiling": "#f6f6f2", "trim": "#1f4fa0", "door": "#3a3a3a", "roof": "#7a7a7a"},
      },
      floors=[{
          "rooms": [
              room("kiosk", "kiosk", [-4, -0.5, 4, 3], autoWindows={"spacing": 2.4, "width": 1.8, "sill": 0.7, "head": 2.4, "health": 15}),
              room("store", "storage", [-4, -3, 1.5, -0.5], floor={"color": "#9a968c", "material": "concrete"}, windows=False),
              room("wc", "wc", [1.5, -3, 4, -0.5], windows=False),
          ],
          "openings": [door(1.5, 3, 1.1, front=True, material="glass", health=40), door(-1.25, -0.5, 0.9), door(2.75, -0.5, 0.9),
                       door(-4, -1.75, 0.9, material="metal")]}],
      roof={"type": "flat", "parapet": 0.5, "color": {"color": "$roof", "material": "concrete"}},
      extras=[
          {"type": "block", "min": [-4.15, 2.3, 3.12], "max": [4.15, 2.9, 3.25], "color": "$trim"},
          {"type": "awning", "rect": [-6.5, 5.5, 6.5, 13.0], "height": 4.6, "color": "$trim", "posts": True},
          {"type": "block", "min": [-6.6, 4.0, 5.4], "max": [6.6, 4.45, 13.1], "color": "#f2f2ee"},
          {"type": "block", "min": [-3.4, 0, 8.6], "max": [-1.6, 0.18, 10.0], "color": {"color": "#a8a8a2", "material": "concrete"}},
          {"type": "block", "min": [1.6, 0, 8.6], "max": [3.4, 0.18, 10.0], "color": {"color": "#a8a8a2", "material": "concrete"}},
          {"type": "prop", "name": "Fuel Pump", "at": [-2.5, 0.18, 9.0], "yaw": 0},
          {"type": "prop", "name": "Fuel Pump", "at": [-2.5, 0.18, 9.6], "yaw": 180},
          {"type": "prop", "name": "Fuel Pump", "at": [2.5, 0.18, 9.0], "yaw": 0},
          {"type": "prop", "name": "Fuel Pump", "at": [2.5, 0.18, 9.6], "yaw": 180},
          {"type": "prop", "name": "Pylon Sign", "at": [7.8, 4.0], "yaw": 0},
          {"type": "prop", "name": "Ice Chest", "at": [-3.0, 3.6]} if False else {"type": "prop", "name": "Chest Freezer", "at": [-3.0, 3.75], "yaw": 180},
          {"type": "prop", "name": "Jerry Can", "at": [-2.0, 3.6]},
          {"type": "prop", "name": "Air Duct Riser", "at": [2.5, 0, -1.5], "onRoof": True},
          {"type": "prop", "name": "Sedan", "at": [-5.0, 9.3], "yaw": 0},
      ],
      anchors=[{"type": "spawn", "name": "store room", "at": [-1.5, -1.8]}, {"type": "flag", "name": "pumps", "at": [0, 9.3]}])


def garage():
    B("Garage", "Garage",
      theme="Backstreet car repair workshop: soot-stained red brick, an oily concrete floor, green steel doors, a car up on the lift, tools everywhere",
      category="industrial",
      description="A car repair workshop: one big bay with a car lift and an open roller-door opening, a parts store and a small office with its own front door.",
      author="Call of Slop", seed=91, wallThickness=0.3, storeyHeight=4.4,
      exterior={"color": "$wall", "material": "brick"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": "concrete"}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "sooty brick": {"wall": "#7a4436", "interior": "#bdb8a8", "floor": "#6e6a62", "ceiling": "#a8a69c", "trim": "#2e5a3a", "door": "#2e5a3a", "roof": "#4a4f52"},
          "white block": {"wall": "#cfcfc8", "interior": "#c8c6bc", "floor": "#74706a", "ceiling": "#b0aea6", "trim": "#1f4f8a", "door": "#1f4f8a", "roof": "#55595c"},
      },
      floors=[{
          "rooms": [
              room("workshop", "garage", [-6, -5, 3, 5], autoWindows={"spacing": 3.5, "width": 1.8, "sill": 2.2, "head": 3.4},
                   props=[{"name": "Car Lift", "at": [-1.6, -1.6]}, {"name": "Sedan", "at": [-1.6, -1.6], "y": 1.2},
                          {"name": "Workbench", "wall": "W", "along": 1.0, "yaw": 0}, {"name": "Tool Chest", "wall": "W", "along": -1.2}]),
              room("office", "office", [3, 0, 6, 5], autoWindows={"spacing": 2.2, "width": 1.1, "sill": 0.95, "head": 2.3}),
              room("parts", "storage", [3, -5, 6, 0], windows=False),
          ],
          "openings": [arch(-1.5, 5, 3.8, head=3.6), door(4.5, 5, 0.9, front=True, material="metal"), door(3, 2.5, 0.9), door(3, -2.5, 0.9),
                       door(-6, -3.5, 1.0, material="metal")]}],
      roof={"type": "shed", "dir": "-z", "pitch": 9, "overhang": 0.35, "color": {"color": "$roof", "material": "metal"}},
      extras=[
          {"type": "block", "min": [-3.65, 3.65, 5.15], "max": [0.65, 4.2, 5.45], "color": "$trim"},
          {"type": "prop", "name": "Car On Bricks", "at": [-3.5, 8.0], "yaw": 90},
          {"type": "prop", "name": "Tyre Stack", "at": [1.6, 6.0]},
          {"type": "prop", "name": "Oil Drum", "at": [2.4, 6.2]},
          {"type": "prop", "name": "Engine Block", "at": [-5.0, 6.3], "yaw": 30},
          {"type": "prop", "name": "Hanging Shop Sign", "at": [3.0, 2.6, 5.15], "yaw": 0},
          {"type": "prop", "name": "Builders Skip", "at": [-8.2, -2.0], "yaw": 90},
      ],
      anchors=[{"type": "spawn", "name": "office", "at": [4.5, 2.5]}, {"type": "hill", "name": "workshop", "at": [-2, 1]}])


def ruin():
    B("Ruined House", "RuinedHouse",
      theme="Bombed-out 1930s townhouse: scorched brick, a collapsed roof and half the upper floor gone, breaches in the walls, rubble and wrecked furniture",
      category="special",
      description="A burnt-out two-storey house: no roof, half the upper floor collapsed, holes blown through the walls, rubble everywhere; the stairs still climb to what's left.",
      author="Call of Slop", seed=101, wallThickness=0.3, storeyHeight=3.0,
      exterior={"color": "$wall", "material": "brick"}, interior={"color": "$interior", "material": "none"},
      floor={"color": "$floor", "material": "wood"}, ceiling="$ceiling", trim="$trim", door="$door",
      palettes={
          "scorched brick": {"wall": "#5e3a30", "interior": "#8a8072", "floor": "#4a3a2e", "ceiling": "#6e665c", "trim": "#3a3530", "door": "#3e2e22"},
          "grey stone": {"wall": "#6e6a64", "interior": "#8c8478", "floor": "#4e4438", "ceiling": "#706a62", "trim": "#3c3a36", "door": "#40342a"},
      },
      floors=[
          {"rooms": [
              room("front room", "ruin", [-4.5, 0, 0.5, 4]),
              room("hall", "ruin", [0.5, -4, 2.5, 4]),
              room("back room", "ruin", [-4.5, -4, 0.5, 0]),
              room("kitchen", "ruin", [2.5, -4, 4.5, 4]),
          ],
           "openings": [arch(1.5, 4, 1.0, front=True), {"type": "hole", "at": [-2.5, 4], "width": 2.2, "sill": 0.4, "head": 2.4},
                        {"type": "hole", "at": [-4.5, -2.0], "width": 1.6, "sill": 0.3, "head": 1.9}, arch(0.5, 3.1, 0.9), arch(0.5, -3.1, 0.9),
                        door(2.5, 3.1, 0.9, open=True), {"type": "hole", "at": [4.5, 2.0], "width": 1.0, "sill": 0.9, "head": 2.1},
                        {"type": "hole", "at": [3.5, -4], "width": 0.9, "sill": 1.0, "head": 2.0}, door(1.5, -4, 0.9, material="wood"),
                        {"type": "hole", "at": [-1.0, -4], "width": 1.2, "sill": 0.9, "head": 2.1}]},
          {"rooms": [
              room("landing", "ruin", [0.5, -4, 2.5, 4]),
              room("bedroom", "ruin", [-4.5, -4, 0.5, 4]),
          ],
           "openings": [arch(0.5, -3.1, 0.9), {"type": "hole", "at": [-2.0, 4], "width": 1.2, "sill": 0.9, "head": 2.1},
                        {"type": "hole", "at": [-4.5, 1.0], "width": 2.4, "sill": 0.2, "head": 2.6}, {"type": "hole", "at": [-1.0, -4], "width": 1.2, "sill": 0.9, "head": 2.1}]},
      ],
      stairs=[{"floor": 0, "rect": [0.7, -2.4, 2.3, 2.0], "dir": "-z", "railing": False}],
      roof={"type": "none"},
      extras=[
          {"type": "prop", "name": "Rubble Pile", "at": [3.6, -1.2]},
          {"type": "prop", "name": "Rubble Pile", "at": [-2.0, 5.3], "yaw": 40},
          {"type": "prop", "name": "Brick Pile", "at": [-5.6, -1.0], "yaw": 80},
          {"type": "prop", "name": "Burnt Out Car", "at": [-1.0, 8.0], "yaw": 15},
          {"type": "prop", "name": "Loose Bricks", "at": [1.2, 5.0]},
          {"type": "block", "min": [2.4, 3.0, -4.15], "max": [4.65, 4.2, -3.85], "color": {"color": "$wall", "material": "brick"}},
          {"type": "block", "min": [4.35, 3.0, -4.0], "max": [4.65, 3.9, 0.5], "color": {"color": "$wall", "material": "brick"}},
      ],
      anchors=[{"type": "spawn", "name": "back room", "at": [-2, -2]}, {"type": "hill", "name": "upstairs", "floor": 1, "at": [-2, 0]}])


def building_yard():
    """The Dev pack's Building Yard: every starter building on two streets, a few variants (palettes, mirrored, turned, floors)."""
    ops = [
        {"op": "Ground", "color": [0.36, 0.42, 0.3]},
        {"op": "Perimeter", "height": 5, "color": [0.55, 0.5, 0.45]},
        # Streets and pavements (thin slabs: the grass keeps off them).
        {"op": "Block", "x0": -70, "y0": 0, "z0": -4, "x1": 70, "y1": 0.03, "z1": 4, "color": [0.22, 0.22, 0.24]},
        {"op": "Block", "x0": -70, "y0": 0, "z0": 26, "x1": 70, "y1": 0.03, "z1": 31, "color": [0.22, 0.22, 0.24]},
        {"op": "Block", "x0": -2.5, "y0": 0, "z0": -55, "x1": 2.5, "y1": 0.03, "z1": 55, "color": [0.22, 0.22, 0.24]},
    ]

    def bld(name, x, z, yaw=0, seed=0, variant=None, spawns=False):
        o = {"op": "Building", "name": name, "at": [x, z]}
        if yaw:
            o["yaw"] = yaw
        if seed:
            o["seed"] = seed
        if variant:
            o["variant"] = variant
        if spawns:
            o["spawns"] = True
        ops.append(o)
    # North side of the main street, fronts facing it (south, +z).
    bld("Warehouse", -46, -22, spawns=True)
    bld("Apartment Block", -18, -15, spawns=True)
    bld("Office Building", 14, -15)
    bld("Corner Shop", 31, -11, spawns=True)
    bld("Small House", 46, -11, spawns=True)
    bld("Small House", 61, -11, seed=5, variant={"palette": "yellow brick", "mirror": True})
    # South side, turned round to face it.
    bld("Gas Station", -50, 17, yaw=180, spawns=True)
    bld("Diner", -28, 13, yaw=180, spawns=True)
    bld("Two-Storey House", -10, 14, yaw=180, spawns=True)
    bld("Garage", 12, 13, yaw=180, spawns=True)
    bld("Ruined House", 28, 13, yaw=180, spawns=True)
    bld("Two-Storey House", 44, 14, yaw=180, seed=9, variant={"palette": "pale blue", "mirror": True, "doors": "open"})
    bld("Apartment Block", 60, 15, yaw=180, variant={"floors": 4, "palette": "brown brick"})
    # Beyond the back street: turned sideways, other palettes and floor counts.
    bld("Office Building", -44, 44, yaw=90, seed=4, variant={"floors": 3, "palette": "red brick"})
    bld("Diner", -20, 42, yaw=0, variant={"palette": "cherry red"})
    bld("Corner Shop", 12, 41, yaw=0, seed=2, variant={"palette": "green grocer", "mirror": True})
    bld("Garage", 30, 43, yaw=270, variant={"palette": "white block"})
    bld("Small House", 50, 42, yaw=0, seed=8, variant={"palette": "grey render", "roof": "hip"})
    ops += [
        {"op": "Prop", "name": "Street Lamp", "x": -30, "z": -5}, {"op": "Prop", "name": "Street Lamp", "x": 0, "z": -5},
        {"op": "Prop", "name": "Street Lamp", "x": 30, "z": -5}, {"op": "Prop", "name": "Street Lamp", "x": -15, "z": 5, "yaw": 180},
        {"op": "Prop", "name": "Street Lamp", "x": 20, "z": 5, "yaw": 180}, {"op": "Prop", "name": "Bus Shelter", "x": 5, "z": -6.5},
        {"op": "Prop", "name": "Hatchback", "x": -36, "z": 2.0, "yaw": 90}, {"op": "Prop", "name": "Van", "x": 38, "z": -2.0, "yaw": 270},
        {"op": "RingSpawns", "count": 10, "frac": 0.82},
        {"op": "AutoWaypoints", "spacing": 8},
    ]
    m = {
        "format": "call-of-slop-map/1", "name": "Building Yard",
        "description": "Every starter building from the Buildings folder on two streets, with variants: walk in, open doors (F), shoot out windows.",
        "author": "Call of Slop", "order": 956, "halfX": 70, "halfZ": 56,
        "skybox": {"preset": "day"} if False else None,
        "sky": [0.58, 0.68, 0.8],
        "ops": ops,
        "foliage": {"seed": 3, "paths": [{"points": [[-70, 0], [70, 0]], "width": 9}, {"points": [[0, -56], [0, 56]], "width": 6}],
                    "regions": [{"type": "grass", "density": 2.0, "rect": [-69, -55, 69, 55]},
                                {"type": "tree", "density": 0.006, "rect": [-69, -55, 69, 55], "height": [5, 8]}]},
    }
    m.pop("skybox")
    write("Maps/Dev/BuildingYard.json", m)


if __name__ == "__main__":
    local_props()
    two_storey()
    apartments()
    corner_shop()
    diner()
    office()
    warehouse()
    gas_station()
    garage()
    ruin()
    building_yard()
    print("wrote the starter buildings and Maps/Dev/BuildingYard.json")
