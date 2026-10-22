#!/usr/bin/env python3
"""Writes the general boreal-forest, mining and Nordic village props used by the World Tour's Finland map
(Maps/World/06-Finland.json, tools/world/finland.py), into general reusable folders (Props/README.md, the asset rule):

- Props/Forest: granite boulders and outcrops, fallen spruce logs, autumn bracken, birch leaf litter, mossy stones, fly
  agaric toadstools, dead branches, lingonberry heath, an anthill, a birch firewood stack and a spruce log pile.
- Props/Mining: an ore mine cart and narrow-gauge mine rail, a tunnel timber frame and cage lamp, a mine timber stack,
  blasting explosives, an ore bin hopper, a belt conveyor incline, a jaw crusher, a dump truck, a tracked excavator, a
  floodlight mast, a diesel tank, a blasting warning sign, a standard-gauge railway track section, a rail buffer stop
  and an ore hopper wagon.
- Props/Nordic: a roadside mailbox rack, a kicksled, a moose warning sign, a wooden rowing boat, a lake jetty ladder,
  a red-and-white lifebuoy post, a birch-bark bench and a sauna water bucket.

Some are adapted from the Cluttered pack's pack-local Timberline and Deepcut Mine props (read from
Maps/Cluttered/Props, renamed and recoloured: those stay pack-local), the rest are new. Every name is unique across
Props/ and every pack's Props folder (checked here).

Usage: python3 tools/world/finland_props.py <repo root>
"""
import copy, glob, json, math, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
SRC = os.path.join(REPO, "Maps/Cluttered/Props")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def octagon(d, h, y, color, x=0.0, z=0.0, **kw):
    """Four boxes turned 0/45/90/135 about Y: an upright octagon d across, h tall, centred at height y."""
    return [part((d, h, d * 0.414), (x, y, z), color, rotation=[0, a, 0], **kw) for a in (0, 45, 90, 135)]


def drum_x(length, d, x, y, z, color, **kw):
    """A cylinder lying along X."""
    return [part((length, d * 0.414, d), (x, y, z), color, rotation=[a, 0, 0], **kw) for a in (0, 45, 90, 135)]


def wheel_z(d, w, x, y, z, color, hub="darkgrey"):
    ps = [part((d, d * 0.414, w), (x, y, z), color, rotation=[0, 0, a]) for a in (0, 45, 90, 135)]
    ps.append(part((d * 0.45, d * 0.45, w + 0.04), (x, y, z), hub))
    return ps


OUT = {}   # folder -> list of prop dicts


def add(folder, d):
    d = dict(d)
    d.setdefault("format", "call-of-slop-prop/1")
    d["category"] = folder
    d.setdefault("author", "Call of Slop")
    OUT.setdefault(folder, []).append(d)


def adapt(src, folder, name, description, recolor=None, **over):
    """A copy of a Cluttered pack prop under a new (global) name, with colours swapped by `recolor` {old: new}."""
    d = json.load(open(os.path.join(SRC, src)))
    d = copy.deepcopy(d)
    d["name"] = name
    d["description"] = description
    for p in d["parts"]:
        c = p.get("color")
        if recolor and isinstance(c, str) and c.lower() in recolor:
            p["color"] = recolor[c.lower()]
    d.update(over)
    d.pop("category", None)
    add(folder, d)
    return d


# ===================================================================================== Forest
GRANITE = {"#7d7a72": "#8a7f78", "#6a6862": "#776c66", "#85827a": "#9a8e86", "#4f6a2c": "#5a7034"}
adapt("Timberline/MossyBoulder.json", "Forest", "Granite Boulder",
      "A rounded pinkish-grey granite boulder left by the ice, moss and lichen on top. About chest high.", GRANITE)
adapt("Timberline/MossyFallenLog.json", "Forest", "Fallen Spruce Log",
      "A big fallen spruce furred with moss, its bark peeling. Cover you can vault.",
      {"#4e3a26": "#4a3624", "#9a7a50": "#a8875a", "#4f6a2c": "#56702e", "#5a7a34": "#647e36", "#5d4630": "#56412c"})
adapt("Timberline/FernClump.json", "Forest", "Autumn Bracken",
      "A clump of bracken turning rust and gold in the autumn.", {"#4a7a30": "#a0662a", "#3e6a2a": "#7a5a26"})
adapt("Timberline/LeafLitter.json", "Forest", "Birch Leaf Litter",
      "Fallen yellow birch leaves and brown pine needles.", {"#6a5a2a": "#8a6a2a", "#7a4a20": "#c09a2a", "#8a6a2a": "#d0aa38", "#5a3a1a": "#5a3a1a"})
adapt("Timberline/ForestStones.json", "Forest", "Mossy Stones",
      "A few granite stones half sunk in moss.", GRANITE)
adapt("Timberline/Toadstools.json", "Forest", "Fly Agaric Toadstools",
      "Red fly agaric toadstools with white spots and a brown bolete.", {"#c83020": "#d02818"})
adapt("Timberline/FallenBranch.json", "Forest", "Dead Pine Branch",
      "A dry pine branch on the forest floor, a few needles still on it.", {"#3e5a26": "#5a5a26"})

# A low granite outcrop: a whaleback of bedrock breaking through the moss, two to three metres long.
outcrop = [
    part((3.2, 0.9, 2.2), (0, 0.32, 0), "#8a7f78", material="concrete", rotation=[4, 12, 6]),
    part((2.0, 1.2, 1.6), (0.7, 0.45, 0.2), "#776c66", material="concrete", rotation=[-8, 40, 10]),
    part((1.4, 0.8, 1.3), (-1.1, 0.35, -0.3), "#9a8e86", material="concrete", rotation=[10, -25, -6]),
    part((1.0, 0.5, 0.9), (1.5, 0.22, -0.7), "#83786f", material="concrete", rotation=[0, 70, 4]),
    part((1.8, 0.06, 1.2), (-0.2, 0.79, 0.0), "#5a7034", rotation=[4, 12, 6]),
    part((0.8, 0.05, 0.6), (0.9, 1.06, 0.25), "#9aa46a", rotation=[-8, 40, 10]),    # pale lichen
    part((0.5, 0.05, 0.4), (-1.2, 0.76, -0.2), "#c8b85a", rotation=[10, -25, -6]),  # yellow lichen
]
add("Forest", {"name": "Granite Outcrop", "description": "A low whaleback of bare granite bedrock through the moss, about 3.5 m long. Waist to chest high cover.",
               "collision": "solid", "collider": [{"size": [3.4, 1.0, 2.3]}, {"size": [1.8, 1.3, 1.4], "offset": [0.7, 0.65, 0.2]}],
               "scatter": {"randomYaw": True, "scale": [0.85, 1.3]}, "parts": outcrop})

# Lingonberry and blueberry heath: a low dwarf-shrub patch with red and blue berries.
heath = []
for k, (x, z, s, c) in enumerate(((0, 0, 0.7, "#4a5e2a"), (0.35, 0.2, 0.5, "#6a4a26"), (-0.3, 0.25, 0.45, "#56682c"), (0.1, -0.35, 0.5, "#7a3a22"))):
    heath.append(part((s, 0.16, s * 0.8), (x, 0.08, z), c, rotation=[0, k * 37, 0]))
for (x, z, c) in ((0.1, 0.1, "#c41e24"), (-0.2, 0.15, "#c41e24"), (0.3, -0.1, "#2a2a6a"), (-0.05, -0.25, "#c41e24"), (0.25, 0.3, "#2a2a6a")):
    heath.append(part((0.05, 0.05, 0.05), (x, 0.18, z), c))
add("Forest", {"name": "Lingonberry Heath", "description": "A low patch of lingonberry and blueberry shrubs, leaves reddening, berries on them.",
               "collision": "cosmetic", "fadeDistance": 30, "scatter": {"randomYaw": True, "scale": [0.8, 1.8]}, "parts": heath})

# A wood-ant hill: a mound of pine needles at the foot of a spruce.
ant = octagon(1.3, 0.35, 0.175, "#5a4026") + octagon(0.9, 0.3, 0.45, "#634628") + octagon(0.5, 0.2, 0.65, "#6e5030")
add("Forest", {"name": "Anthill", "description": "A wood-ant mound of pine needles, knee high.", "collision": "cosmetic", "fadeDistance": 40,
               "scatter": {"randomYaw": True, "scale": [0.8, 1.3]}, "parts": ant})

# Birch firewood stack (halkopino): split birch logs, white bark ends, between end posts. Waist-high cover.
fw = [part((2.6, 0.1, 0.9), (0, 0.05, 0), "#3a2a1a")]
for row in range(4):
    for k in range(6):
        x = -1.1 + k * 0.44 + (0.11 if row % 2 else 0)
        fw += [part((0.36, 0.2, 0.8), (x, 0.2 + row * 0.21, 0), "#d8d0bc" if (k + row) % 3 else "#c8bea8", rotation=[0, 0, 45 * ((k + row) % 2)])]
fw += [part((0.08, 1.1, 0.08), (1.32, 0.55, z), "#4a3624") for z in (-0.35, 0.35)]
fw += [part((0.08, 1.1, 0.08), (-1.32, 0.55, z), "#4a3624") for z in (-0.35, 0.35)]
fw += [part((2.9, 0.05, 1.1), (0, 1.13, 0), "#3e3a36", rotation=[5, 0, 0])]
for k in range(6):   # the split faces showing on the front
    fw.append(part((0.3, 0.14, 0.02), (-1.1 + k * 0.44, 0.42, 0.41), "#e0c890"))
add("Forest", {"name": "Birch Firewood Stack", "description": "Split birch firewood stacked between posts under a felt cap, white bark ends out. Waist-high cover.",
               "collision": "solid", "collider": [{"size": [2.7, 1.15, 0.9]}], "scatter": {"randomYaw": False, "yawStep": 90},
               "breakable": {"health": 140, "breakBy": "all", "sound": "wood", "debris": {"count": 18, "size": [0.08, 0.4], "colors": ["#d8d0bc", "#e0c890"]}},
               "parts": fw})

# Spruce log pile at a forest landing: long logs stacked in a pyramid, chocked by posts. Chest-high cover.
logs = []
for row, n in enumerate((5, 4, 3)):
    for k in range(n):
        z = (k - (n - 1) / 2) * 0.46
        y = 0.24 + row * 0.4
        logs += drum_x(5.2, 0.46, 0, y, z, "#6a4a2c" if (k + row) % 2 else "#5a3e26")
        logs.append(part((0.02, 0.36, 0.36), (2.6, y, z), "#c8a46a"))
        logs.append(part((0.02, 0.36, 0.36), (-2.6, y, z), "#c8a46a"))
logs += [part((0.14, 1.4, 0.14), (x, 0.7, z), "#3a2a1a") for x in (-1.6, 1.6) for z in (-1.3, 1.3)]
add("Forest", {"name": "Spruce Log Pile", "description": "Felled spruce logs stacked at a forest landing, chocked by posts. Chest-high cover you can mantle.",
               "collision": "solid", "collider": [{"size": [5.2, 1.3, 2.4]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 4},
               "parts": logs})

# ===================================================================================== Mining
adapt("DeepcutMine/MineCart.json", "Mining", "Ore Mine Cart",
      "A rusty side-tipping mine cart heaped with ore, on its rail wheels. Waist-high cover.", {"#c8a040": "#d8b030"})
adapt("DeepcutMine/RailTrack.json", "Mining", "Mine Rail Section",
      "A 3 m length of narrow-gauge mine track (rails on timber sleepers), running along X.")
adapt("DeepcutMine/MineLamp.json", "Mining", "Tunnel Cage Lamp",
      "A caged bulb on a short cable in a mine drift, flickering now and then. Its top is 0.6 m up: hang it from a ceiling with the Prop op's y.")
adapt("DeepcutMine/TimberStack.json", "Mining", "Mine Timber Stack",
      "Square mine timbers stacked between two chocks for the tunnel crews. Waist-high cover.")
adapt("DeepcutMine/ExplosivesCrate.json", "Mining", "Blasting Explosives Crate",
      "A wooden crate of blasting explosives with red labels. It goes off when it breaks: keep your distance.")
adapt("DeepcutMine/OreHopper.json", "Mining", "Ore Bin Hopper",
      "An ore bin on four steel legs, its chute 3.4 m up: you can walk under it.")
adapt("DeepcutMine/InclinedConveyor.json", "Mining", "Belt Conveyor Incline",
      "An 11 m belt conveyor climbing 4.5 m along +X on three trestles, its belt running with ore. You can walk under it.")
adapt("DeepcutMine/JawCrusher.json", "Mining", "Jaw Crusher Unit",
      "A jaw crusher on a concrete plinth: flywheels turning, the jaw shaking rock down to gravel.")
adapt("DeepcutMine/HaulTruck.json", "Mining", "Mine Dump Truck",
      "A big mine dump truck loaded with ore, about 9 m long. Solid cover you can't climb.",
      {"#d9a520": "#c8c4b8", "#a87a14": "#8a867c"})      # a pale grey truck: the Finnish mine's own colour
adapt("DeepcutMine/Excavator.json", "Mining", "Tracked Excavator",
      "A tracked excavator with its boom out and the bucket on the ground (+X).", {"#d9a520": "#e07a18", "#a87a14": "#a85a10"})
adapt("DeepcutMine/FloodlightMast.json", "Mining", "Mine Floodlight Mast",
      "A 9 m lighting mast with four lamps facing +Z, burning day and night.")
adapt("DeepcutMine/FuelTank.json", "Mining", "Diesel Storage Tank",
      "A 6 m diesel tank lying on two cradles. Solid, unbreakable cover.")

# Tunnel timber frame, sized for a 6 m wide cave drift (posts 4 m apart, cap at 2.8 m): the drift runs along Z.
add("Mining", {"name": "Tunnel Timber Frame", "description": "A drift timber set: two posts and a cap 4 m across and 2.8 m high (the drift runs along Z).",
               "collision": "cosmetic", "fadeDistance": 90, "scatter": {"randomYaw": False, "yawStep": 90},
               "parts": [part((0.24, 2.8, 0.24), (-2.0, 1.4, 0), "#6e5434", material="wood"),
                         part((0.24, 2.8, 0.24), (2.0, 1.4, 0), "#6e5434", material="wood"),
                         part((4.4, 0.26, 0.28), (0, 2.8, 0), "#5e4628", material="wood"),
                         part((0.5, 0.18, 0.22), (-1.8, 2.6, 0), "#5e4628", material="wood", rotation=[0, 0, 40]),
                         part((0.5, 0.18, 0.22), (1.8, 2.6, 0), "#5e4628", material="wood", rotation=[0, 0, -40]),
                         part((4.0, 0.08, 0.06), (0, 2.6, 0.2), "#2a2a2c")]})            # the cable run along the cap

# Blasting warning sign: a red-bordered yellow board with a black blast mark (no text), on a post.
add("Mining", {"name": "Blasting Warning Sign", "description": "A yellow and red blasting-area warning board with a black burst mark (no text), on a post. Faces +Z.",
               "collision": "cosmetic", "fadeDistance": 80, "scatter": {"randomYaw": False, "yawStep": 90},
               "parts": [part((0.08, 1.8, 0.08), (0, 0.9, -0.03), "#888888"),
                         part((0.9, 0.62, 0.03), (0, 1.55, 0.02), "#c82818"),
                         part((0.78, 0.5, 0.035), (0, 1.55, 0.023), "#f0c818"),
                         part((0.22, 0.22, 0.04), (0, 1.55, 0.03), "#1a1a1a", rotation=[0, 0, 45]),
                         part((0.36, 0.06, 0.04), (0, 1.55, 0.03), "#1a1a1a", rotation=[0, 0, 20]),
                         part((0.36, 0.06, 0.04), (0, 1.55, 0.03), "#1a1a1a", rotation=[0, 0, -40])]})

# Standard-gauge railway track (1.524 m Finnish broad gauge): 4 m along X, steel rails on concrete sleepers in ballast.
rt = [part((4.0, 0.12, 3.0), (0, 0.06, 0), "#6e6a64", material="dirt")]
for k in range(7):
    rt.append(part((0.26, 0.1, 2.5), (-1.71 + k * 0.57, 0.15, 0), "#8a8780", material="concrete"))
for z in (-0.79, 0.79):
    rt.append(part((4.0, 0.14, 0.08), (0, 0.27, z), "#5a5650", material="metal"))
    rt.append(part((4.0, 0.03, 0.16), (0, 0.215, z), "#3e3a36"))
add("Mining", {"name": "Railway Track Section", "description": "A 4 m length of broad-gauge railway track on concrete sleepers and ballast, running along X. Flat: walk over it.",
               "collision": "cosmetic", "fadeDistance": 110, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": rt})

# Rail buffer stop: a steel frame with two red buffers at the end of a siding (faces +X: the buffers point along +X).
bs = [part((0.6, 1.0, 2.4), (-0.3, 0.5, 0), "#3a3a3c", material="metal"),
      part((0.2, 0.3, 2.6), (0.1, 1.05, 0), "#c82818"),
      part((1.6, 0.18, 0.2), (-0.7, 0.55, -0.8), "#3a3a3c", rotation=[0, 0, 30]),
      part((1.6, 0.18, 0.2), (-0.7, 0.55, 0.8), "#3a3a3c", rotation=[0, 0, 30])]
for z in (-0.8, 0.8):
    bs += drum_x(0.5, 0.36, 0.25, 0.95, z, "#2a2a2c")
    bs.append(part((0.06, 0.42, 0.42), (0.52, 0.95, z), "#d8d4c8"))
add("Mining", {"name": "Rail Buffer Stop", "description": "A steel buffer stop at the end of a siding, two buffers facing +X. Waist-high cover.",
               "collision": "solid", "collider": [{"size": [1.2, 1.2, 2.6], "offset": [-0.1, 0.6, 0]}], "scatter": {"randomYaw": False, "yawStep": 90}, "parts": bs})

# Ore hopper wagon: a broad-gauge four-axle wagon with a sloped-side hopper heaped with ore, about 10 m long (+X).
ow = [part((10.0, 0.35, 2.7), (0, 1.1, 0), "#2e2e30", material="metal"),                    # underframe
      part((9.6, 1.5, 2.9), (0, 2.05, 0), "#7a3a24", material="metal"),                      # hopper sides
      part((9.0, 0.5, 2.2), (0, 1.28, 0), "#6a3220", material="metal"),                      # sloped lower hopper
      part((9.8, 0.12, 3.05), (0, 2.85, 0), "#5a2a1a", material="metal"),                    # top rim
      part((3.0, 0.4, 2.4), (-2.6, 2.95, 0), "#4a4440"), part((3.2, 0.5, 2.3), (0.6, 3.0, 0.1), "#55504a"),
      part((2.6, 0.35, 2.2), (3.4, 2.95, -0.1), "#3e3a36"),                                   # ore heaps
      part((0.5, 0.5, 0.6), (-4.9, 1.25, 0), "#2a2a2c"), part((0.5, 0.5, 0.6), (4.9, 1.25, 0), "#2a2a2c")]   # couplers
for x in (-3.6, 3.6):
    ow.append(part((2.6, 0.45, 2.2), (x, 0.65, 0), "#2a2a2c"))                              # bogies
    for dx in (-0.85, 0.85):
        for z in (-0.78, 0.78):
            ow += wheel_z(0.86, 0.12, x + dx, 0.43, z, "#26262a")
for k in range(6):   # side ribs
    for z in (-1.47, 1.47):
        ow.append(part((0.12, 1.5, 0.06), (-4.0 + k * 1.6, 2.05, z), "#5a2a1a"))
ow.append(part((1.2, 0.3, 0.02), (0, 2.4, 1.49), "#e8e4d8"))                                 # number board
add("Mining", {"name": "Ore Hopper Wagon", "description": "A broad-gauge four-axle ore wagon heaped with ore, 10 m long along X. Solid cover you can mantle onto from a platform.",
               "collision": "solid", "collider": [{"size": [10.0, 2.9, 2.9], "offset": [0, 1.45, 0]}],
               "scatter": {"randomYaw": False, "yawStep": 90}, "parts": ow})

# ===================================================================================== Nordic village
# Roadside mailbox rack: a plank on two posts with four mailboxes on it (green, grey, red, blue lids), facing +Z.
mb = [part((0.1, 1.15, 0.1), (x, 0.575, 0), "#4a3624") for x in (-0.85, 0.85)]
mb.append(part((2.0, 0.06, 0.4), (0, 1.15, 0), "#5a4630", material="wood"))
for k, c in enumerate(("#2a6a3a", "#8a8c8e", "#b02a20", "#2a4a8a")):
    x = -0.72 + k * 0.48
    mb.append(part((0.4, 0.32, 0.36), (x, 1.34, 0), c, material="metal"))
    mb.append(part((0.42, 0.06, 0.38), (x, 1.52, 0), c))
    mb.append(part((0.1, 0.04, 0.02), (x, 1.36, 0.19), "#e8e4d8"))
add("Nordic", {"name": "Mailbox Rack", "description": "A row of four rural mailboxes on a plank between two posts at the roadside, facing +Z.",
               "collision": "solid", "collider": [{"size": [1.9, 1.6, 0.42], "offset": [0, 0.8, 0]}], "scatter": {"randomYaw": False, "yawStep": 90},
               "breakable": {"health": 120, "breakBy": "all", "sound": "metal", "debris": {"count": 12, "size": [0.05, 0.25]}},
               "parts": mb})

# Kicksled (potkukelkka): a chair on two long steel runners with handlebars.
ks = [part((1.7, 0.03, 0.04), (0.15, 0.02, z), "#9a9ca0", material="metal") for z in (-0.22, 0.22)]
ks += [part((0.04, 0.95, 0.04), (-0.55, 0.5, z), "#b02a20") for z in (-0.2, 0.2)]
ks += [part((0.04, 0.45, 0.04), (0.0, 0.25, z), "#b02a20") for z in (-0.2, 0.2)]
ks += [part((0.4, 0.04, 0.44), (-0.2, 0.47, 0), "#8a6a40", material="wood"), part((0.04, 0.35, 0.44), (-0.4, 0.66, 0), "#8a6a40", material="wood"),
       part((0.04, 0.04, 0.6), (-0.55, 0.98, 0), "#1a1a1a")]
add("Nordic", {"name": "Kicksled", "description": "A red kicksled: a wooden chair on two long steel runners, for winter shopping trips.",
               "collision": "cosmetic", "fadeDistance": 45, "scatter": {"randomYaw": True}, "parts": ks})

# Moose warning sign: a red-bordered triangle with a black moose shape, on a post (faces +Z).
mo = [part((0.08, 2.0, 0.08), (0, 1.0, -0.03), "#8a8c8e")]
for a, (x, y) in ((0, (0, 1.38)), (60, (-0.23, 1.78)), (-60, (0.23, 1.78))):
    mo.append(part((0.92, 0.12, 0.03), (x, y, 0.02), "#c82818", rotation=[0, 0, a]))
mo.append(part((0.56, 0.42, 0.025), (0, 1.62, 0.012), "#f0c818"))
mo.append(part((0.36, 0.42, 0.025), (0, 1.75, 0.011), "#f0c818"))
mo += [part((0.3, 0.12, 0.03), (0.02, 1.6, 0.03), "#1a1a1a"),                    # body
       part((0.04, 0.12, 0.03), (-0.08, 1.5, 0.03), "#1a1a1a"), part((0.04, 0.12, 0.03), (0.1, 1.5, 0.03), "#1a1a1a"),   # legs
       part((0.1, 0.08, 0.03), (0.2, 1.66, 0.03), "#1a1a1a", rotation=[0, 0, -30]),                                         # head
       part((0.12, 0.03, 0.03), (0.18, 1.72, 0.03), "#1a1a1a", rotation=[0, 0, 20])]                                        # antlers
add("Nordic", {"name": "Moose Warning Sign", "description": "A red-bordered yellow triangle road sign with a black moose on it, on a grey post. Faces +Z.",
               "collision": "solid", "collider": [{"size": [0.12, 2.0, 0.12], "offset": [0, 1.0, -0.03]}], "scatter": {"randomYaw": False, "yawStep": 90},
               "breakable": {"health": 100, "breakBy": "all", "sound": "metal", "debris": {"count": 8, "size": [0.05, 0.2]}},
               "parts": mo})

# Wooden rowing boat (soutuvene), along X, upside down on the shore or afloat with "anchor": "world".
rb = [part((3.6, 0.36, 1.1), (0, 0.3, 0), "#7a5a3a", material="wood"),
      part((3.2, 0.3, 0.8), (0, 0.15, 0), "#6a4a2e", material="wood"),
      part((0.6, 0.36, 0.7), (1.95, 0.34, 0), "#7a5a3a", material="wood", rotation=[0, 45, 0]),
      part((3.7, 0.06, 1.18), (0, 0.5, 0), "#4a3a2a", material="wood"),
      part((0.2, 0.06, 1.0), (0.3, 0.42, 0), "#8a6a44"), part((0.2, 0.06, 1.0), (-0.9, 0.42, 0), "#8a6a44"),
      part((3.62, 0.06, 1.12), (0, 0.18, 0), "#e8e4dc"),
      part((1.6, 0.04, 0.06), (0.1, 0.5, 0.5), "#c8a46a", rotation=[0, 12, 0])]   # an oar
add("Nordic", {"name": "Wooden Rowing Boat", "description": "A clinker-built wooden rowing boat with an oar, about 4 m long along X. Low cover on the shore.",
               "collision": "solid", "collider": [{"size": [3.9, 0.55, 1.2], "offset": [0.1, 0.27, 0]}], "scatter": {"randomYaw": True},
               "breakable": {"health": 150, "breakBy": "all", "sound": "wood", "debris": {"count": 16, "size": [0.08, 0.5]}},
               "parts": rb})

# Lifebuoy post: a red and white ring on a post by the jetty (faces +Z).
lb = [part((0.1, 1.6, 0.1), (0, 0.8, -0.04), "#e8e4dc"), part((0.3, 0.06, 0.12), (0, 1.5, 0.02), "#8a8c8e")]
for k, a in enumerate((0, 45, 90, 135)):
    lb.append(part((0.62, 0.62 * 0.414, 0.1), (0, 1.15, 0.06), "#d02818" if k % 2 == 0 else "#f2f0ea", rotation=[0, 0, a]))
lb.append(part((0.34, 0.34, 0.12), (0, 1.15, 0.065), "#5a4630"))
add("Nordic", {"name": "Lifebuoy Post", "description": "A red and white lifebuoy hanging on a white post at the waterside. Faces +Z.",
               "collision": "cosmetic", "fadeDistance": 70, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": lb})

# Jetty ladder: a steel swim ladder down into the lake (hangs off a deck edge; the deck edge is at -Z, the water at +Z).
jl = []
for x in (-0.25, 0.25):
    jl.append(part((0.05, 1.6, 0.05), (x, 0.3, 0.12), "#c0c2c4", material="metal"))
    jl.append(part((0.05, 0.05, 0.4), (x, 1.1, -0.05), "#c0c2c4", material="metal"))
for k in range(4):
    jl.append(part((0.5, 0.04, 0.06), (0, -0.3 + k * 0.32, 0.12), "#a0a2a4", material="metal"))
add("Nordic", {"name": "Jetty Ladder", "description": "A steel swimming ladder hung off a jetty: its hooks at the deck edge (y 0 to 1.1), the rungs down into the water (+Z).",
               "collision": "cosmetic", "fadeDistance": 50, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": jl})

# A plank bench with birch log legs (outside a sauna, at a jetty).
bb = [part((1.8, 0.07, 0.38), (0, 0.45, 0), "#9a7a4e", material="wood")]
bb += octagon(0.24, 0.42, 0.21, "#d8d0bc", x=-0.65) + octagon(0.24, 0.42, 0.21, "#d8d0bc", x=0.65)
bb += [part((0.04, 0.3, 0.04), (x, 0.24, z), "#2a2a2a") for x in (-0.65, 0.65) for z in (-0.122,)]
add("Nordic", {"name": "Birch Log Bench", "description": "A plank bench on two birch-log legs, for cooling off outside the sauna.",
               "collision": "solid", "collider": [{"size": [1.8, 0.5, 0.4]}], "scatter": {"randomYaw": True},
               "breakable": {"health": 70, "breakBy": "all", "sound": "wood", "debris": {"count": 10, "size": [0.06, 0.3]}},
               "parts": bb})

# Sauna bucket and ladle: a wooden bucket with brass bands and a long-handled ladle.
sb = octagon(0.28, 0.24, 0.12, "#a07a48", material="wood") + octagon(0.29, 0.03, 0.04, "#c8a040") + octagon(0.29, 0.03, 0.2, "#c8a040")
sb += [part((0.03, 0.03, 0.5), (0.05, 0.3, 0.1), "#8a6a40", rotation=[30, 0, 0]), part((0.08, 0.04, 0.08), (0.05, 0.2, -0.08), "#8a6a40")]
add("Nordic", {"name": "Sauna Bucket", "description": "A wooden sauna bucket with brass bands and a long-handled ladle.",
               "collision": "cosmetic", "fadeDistance": 30, "scatter": {"randomYaw": True},
               "breakable": {"health": 10, "breakBy": "all", "sound": "wood", "debris": {"count": 6, "size": [0.03, 0.12]}},
               "parts": sb})


# ===================================================================================== write
def existing_names(skip_dirs):
    names = {}
    files = glob.glob(os.path.join(REPO, "Props/*/*.json")) + glob.glob(os.path.join(REPO, "Maps/*/Props/**/*.json"), recursive=True)
    files += glob.glob(os.path.join(REPO, "Buildings/*/Props/*.json"))
    for f in files:
        if any(os.path.abspath(f).startswith(os.path.abspath(d) + os.sep) for d in skip_dirs) or "/Flags/" in f:
            continue
        try:
            names[json.load(open(f))["name"]] = f
        except (ValueError, KeyError):
            pass
    return names


# Animated props live in the pack (Maps/World/Props/Finland): every animated prop in Props/ must stand on the Dev pack's
# Animated Props gallery, which these map-specific pieces don't belong on.
PACK = {"Tunnel Cage Lamp", "Belt Conveyor Incline", "Jaw Crusher Unit"}
for _p in OUT["Mining"]:
    if _p["name"] == "Mine Floodlight Mast":       # lamps always on: a plain bright colour, no light animation
        for _q in _p["parts"]:
            _q.pop("anim", None)


def write():
    for folder in list(OUT):
        moved = [p for p in OUT[folder] if p["name"] in PACK]
        OUT[folder] = [p for p in OUT[folder] if p["name"] not in PACK]
        for p in moved:
            p["category"] = "Finland"
            OUT.setdefault("@Finland", []).append(p)
    dirs = [os.path.join(REPO, "Props", k) for k in OUT if not k.startswith("@")] + [os.path.join(REPO, "Maps/World/Props/Finland")]
    taken = existing_names(dirs)
    seen = set()
    for folder, props in OUT.items():
        d = os.path.join(REPO, "Maps/World/Props", folder[1:]) if folder.startswith("@") else os.path.join(REPO, "Props", folder)
        os.makedirs(d, exist_ok=True)
        for p in props:
            if p["name"] in taken:
                raise SystemExit(f"prop name {p['name']!r} is already used by {taken[p['name']]}")
            if p["name"] in seen:
                raise SystemExit(f"prop name {p['name']!r} twice")
            seen.add(p["name"])
            order = ["format", "name", "category", "description", "author", "collision", "collider", "scatter", "fadeDistance", "breakable", "flag"]
            fn = os.path.join(d, "".join(w for w in p["name"].replace("-", " ").title().split() if w.isalnum()) + ".json")
            with open(fn, "w") as f:
                f.write("{\n")
                keys = [k for k in order if k in p] + [k for k in p if k not in order and k != "parts"]
                for k in keys:
                    f.write(f'  "{k}": {json.dumps(p[k])},\n')
                f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(q) for q in p["parts"]) + "\n  ]\n}\n")
    print(", ".join(f"{k}: {len(v)}" for k, v in OUT.items()), "props written")


if __name__ == "__main__":
    write()
