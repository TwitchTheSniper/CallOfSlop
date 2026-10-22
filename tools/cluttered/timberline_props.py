#!/usr/bin/env python3
"""Writes the Cluttered pack's forest props (Maps/Cluttered/Props/Timberline/*.json): camping, logging and bunker bits
that no global prop covers. They're pack-local (Props/README.md, "Pack props"): only the Cluttered pack's maps see them.
10 Timberline (tools/cluttered/timberline.py) uses them.

Usage: python3 tools/cluttered/timberline_props.py <repo root>

Names are chosen so they don't collide with other pack props (a pack's prop names are one namespace).
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/Cluttered/Props/Timberline")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def octagon(d, h, y, color, x=0.0, z=0.0, **kw):
    """Four boxes turned 0/45/90/135 degrees about Y: an upright octagon d across, h tall, centred at height y."""
    return [part((d, h, d * 0.414), (x, y, z), color, rotation=[0, a, 0], **kw) for a in (0, 45, 90, 135)]


def log_x(length, d, y, color, x=0.0, z=0.0, yaw=0, end="#b08a5a", **kw):
    """A log lying along X (four boxes turned about X), with pale cut ends."""
    ps = [part((length, d, d * 0.414), (x, y, z), color, rotation=[a, yaw, 0], **kw) for a in (0, 45, 90, 135)]
    if end and yaw == 0:
        ps += [part((0.02, d * 0.86, d * 0.36), (x + sx * length / 2, y, z), end, rotation=[a, 0, 0]) for sx in (1, -1) for a in (0, 90)]
    return ps


BARK = "#4e3a26"
BARK2 = "#5d4630"
MOSS = "#4f6a2c"
ROCK = "#7d7a72"
ROCK2 = "#6a6862"
CANVAS = "#5f7a3a"
OLIVE = "#55603a"
CONCRETE = "#8a8a86"

PROPS = []

# ---------------------------------------------------------------- camping
tent = [
    part((2.3, 0.04, 2.0), (0, 0.02, 0), "#3a3a30"),                                       # groundsheet
    part((2.2, 0.06, 1.55), (0, 0.6, -0.5), CANVAS, rotation=[-52, 0, 0]),  # north slope
    part((2.2, 0.06, 1.55), (0, 0.6, 0.5), CANVAS, rotation=[52, 0, 0]),                   # south slope
    part((0.05, 1.2, 0.05), (1.08, 0.6, 0), "darkgrey"),                                    # poles
    part((0.05, 1.2, 0.05), (-1.08, 0.6, 0), "darkgrey"),
    part((2.4, 0.05, 0.05), (0, 1.22, 0), "darkgrey"),                                      # ridge
    part((0.04, 1.0, 1.3), (-1.1, 0.5, 0), "#4c6630", rotation=[0, 0, 0]),                 # back wall
    part((0.03, 0.9, 0.5), (1.1, 0.45, 0.3), "#6e8a44", rotation=[0, 25, 0]),               # door flap, open
    part((0.6, 0.12, 0.4), (-0.5, 0.1, 0.2), "#a03020"),                                    # sleeping bag inside
    part((0.02, 0.02, 0.9), (1.25, 0.3, 0.9), "#c8c8b0", rotation=[40, 0, 0]),              # guy lines
    part((0.02, 0.02, 0.9), (1.25, 0.3, -0.9), "#c8c8b0", rotation=[-40, 0, 0]),
]
PROPS.append({"name": "Ridge Tent", "description": "A green two-person ridge tent with its door flap open.",
              "collision": "solid", "collider": [{"size": [2.2, 1.0, 1.7]}], "scatter": {"randomYaw": True},
              "breakable": {"health": 40, "breakBy": "all", "sound": "soft", "debris": {"count": 14, "size": [0.08, 0.4], "colors": [CANVAS, "#4c6630"]}},
              "parts": tent})

dome = [part((2.0, 0.04, 2.0), (0, 0.02, 0), "#3a3a30")]
for k, (w, h, y) in enumerate(((2.0, 0.5, 0.25), (1.75, 0.4, 0.7), (1.3, 0.3, 1.05), (0.7, 0.15, 1.27))):
    dome += octagon(w, h, y, ["#c86a20", "#d27a2c", "#c86a20", "#b85e1a"][k])
dome += [part((0.7, 0.8, 0.05), (0, 0.4, 0.98), "#2a2a26"), part((0.05, 0.05, 2.1), (0, 1.0, 0), "darkgrey", rotation=[0, 45, 0]),
         part((0.05, 0.05, 2.1), (0, 1.0, 0), "darkgrey", rotation=[0, -45, 0])]
PROPS.append({"name": "Dome Tent", "description": "An orange dome tent, door unzipped.",
              "collision": "solid", "collider": [{"size": [1.8, 1.2, 1.8]}], "scatter": {"randomYaw": True},
              "breakable": {"health": 35, "breakBy": "all", "sound": "soft", "debris": {"count": 12, "size": [0.08, 0.35], "colors": ["#c86a20", "#2a2a26"]}},
              "parts": dome})

fire = []
for k in range(8):
    import math
    a = k * math.pi / 4
    fire.append(part((0.26, 0.2, 0.22), (0.55 * math.cos(a), 0.1, 0.55 * math.sin(a)), ROCK if k % 2 else ROCK2, rotation=[0, k * 45 + 10, 0]))
fire += [part((0.8, 0.03, 0.8), (0, 0.015, 0), "#2a2420")]                          # ash bed
fire += [part((0.7, 0.1, 0.1), (0, 0.12, 0), BARK, rotation=[0, a, 18]) for a in (0, 60, 120)]
fire += [
    part((0.3, 0.32, 0.3), (0, 0.24, 0), "#ff9a30", rotation=[0, 45, 0],
         anim={"type": "flicker", "rate": 12, "duty": 0.8, "color": "#ffb040", "off": "#a04010", "glow": 1.8}),
    part((0.18, 0.5, 0.18), (0.02, 0.36, -0.03), "#ffd060", rotation=[0, 20, 0],
         anim={"type": "flicker", "rate": 15, "duty": 0.7, "color": "#ffe080", "off": "#c06010", "glow": 2.2}),
]
PROPS.append({"name": "Campfire Ring", "description": "A ring of stones round a small crackling fire.",
              "collision": "cosmetic", "fadeDistance": 120, "scatter": {"randomYaw": True}, "parts": fire})

bag = [part((0.75, 0.12, 1.9), (0, 0.06, 0), "#2c4a7a"), part((0.7, 0.14, 0.4), (0, 0.08, -0.8), "#3a5a8c"),
       part((0.5, 0.1, 0.3), (0, 0.16, -0.78), "#d8d0b8")]
PROPS.append({"name": "Sleeping Bag", "description": "A blue sleeping bag with a pillow.", "collision": "cosmetic",
              "scatter": {"randomYaw": True}, "parts": bag})

chair = [part((0.55, 0.05, 0.5), (0, 0.42, 0), "#2f5d3a"), part((0.55, 0.5, 0.05), (0, 0.68, -0.26), "#2f5d3a", rotation=[-10, 0, 0])]
for sx in (1, -1):
    chair += [part((0.03, 0.6, 0.03), (sx * 0.25, 0.3, 0), "darkgrey", rotation=[30, 0, 0]),
              part((0.03, 0.6, 0.03), (sx * 0.25, 0.3, 0), "darkgrey", rotation=[-30, 0, 0]),
              part((0.04, 0.04, 0.45), (sx * 0.29, 0.6, 0), "#2f5d3a")]
PROPS.append({"name": "Camp Chair", "description": "A folding green camp chair.", "collision": "cosmetic",
              "scatter": {"randomYaw": True}, "breakable": {"health": 15, "breakBy": "all", "sound": "soft"}, "parts": chair})

cool = [part((0.7, 0.4, 0.42), (0, 0.2, 0), "#c83a30"), part((0.72, 0.07, 0.44), (0, 0.43, 0), "white"),
        part((0.3, 0.04, 0.05), (0, 0.48, 0), "darkgrey")]
PROPS.append({"name": "Camp Cooler", "description": "A red-and-white cool box.", "collision": "cosmetic",
              "scatter": {"randomYaw": True}, "breakable": {"health": 20, "breakBy": "all", "sound": "soft"}, "parts": cool})

lant = [part((0.16, 0.03, 0.16), (0, 0.015, 0), "#2a3a2a"), part((0.11, 0.18, 0.11), (0, 0.12, 0), "amber",
        anim={"type": "flicker", "rate": 8, "duty": 0.9, "color": "#ffd070", "off": "#705020", "glow": 1.3}),
        part((0.15, 0.03, 0.15), (0, 0.225, 0), "#2a3a2a"), part((0.1, 0.06, 0.02), (0, 0.28, 0), "#2a3a2a")]
PROPS.append({"name": "Camp Lantern", "description": "A camping lantern, its mantle glowing.", "collision": "cosmetic",
              "scatter": {"randomYaw": True}, "breakable": {"health": 5, "breakBy": "all", "sound": "glass"}, "parts": lant})

canoe = [part((4.0, 0.36, 0.62), (0, 0.18, 0), "#b8401e", material="wood"),
         part((0.62, 0.34, 0.62), (2.0, 0.19, 0), "#b8401e", rotation=[0, 45, 0]),
         part((0.62, 0.34, 0.62), (-2.0, 0.19, 0), "#b8401e", rotation=[0, 45, 0]),
         part((3.9, 0.04, 0.5), (0, 0.33, 0), "#d8b070", material="wood"),
         part((0.06, 0.05, 0.66), (0.9, 0.36, 0), "#8a6a40"), part((0.06, 0.05, 0.66), (-0.9, 0.36, 0), "#8a6a40"),
         part((1.5, 0.03, 0.08), (0.2, 0.4, 0.12), "#d0b080", rotation=[0, 12, 0]), part((0.3, 0.02, 0.14), (0.95, 0.4, 0.27), "#d0b080", rotation=[0, 12, 0])]
PROPS.append({"name": "Canoe", "description": "A red wooden canoe with a paddle in it. Stand it on the shore, or afloat with \"anchor\": \"world\".",
              "collision": "cosmetic", "fadeDistance": 200, "scatter": {"randomYaw": True}, "parts": canoe})

# ---------------------------------------------------------------- logging and woodcraft
stump = octagon(0.7, 0.5, 0.25, BARK) + [part((0.62, 0.02, 0.26), (0, 0.505, 0), "#b8925e", rotation=[0, a, 0]) for a in (0, 45, 90, 135)]
stump += [part((0.08, 0.1, 0.9), (0.08, 0.62, -0.1), "#6a4a2a", rotation=[-38, 20, 0]),          # axe handle
          part((0.04, 0.16, 0.2), (0.02, 0.52, 0.18), "#a8acb0", rotation=[0, 20, 0])]           # axe head buried in the stump
stump += [part((0.4, 0.14, 0.14), (0.5, 0.07, 0.3), "#c8a070", rotation=[0, 30, 0]), part((0.3, 0.12, 0.12), (-0.45, 0.06, 0.35), "#c8a070", rotation=[0, -50, 0])]
PROPS.append({"name": "Axe Stump", "description": "A chopping block with an axe left in it and split kindling round it.",
              "collision": "solid", "collider": [{"size": [0.7, 0.52, 0.7]}], "scatter": {"randomYaw": True}, "parts": stump})

pile = []
for row, (n, y) in enumerate(((5, 0.25), (4, 0.68), (3, 1.11))):
    for k in range(n):
        z = (k - (n - 1) / 2) * 0.48
        pile += log_x(5.2 - row * 0.15, 0.48, y, BARK if (k + row) % 2 else BARK2, z=z, end="#c09060")
pile += [part((0.12, 1.2, 0.12), (2.2, 0.6, sz * 1.3), "#3a2a1a") for sz in (1, -1)]
PROPS.append({"name": "Pine Log Pile", "description": "Felled pine logs stacked at the landing, chocked with posts. Chest-high cover you can mantle.",
              "collision": "solid", "collider": [{"size": [5.2, 1.35, 2.4]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 6}, "parts": pile})

wood = [part((1.9, 0.08, 0.7), (0, 0.04, 0), "#3a2a1a")]
for row in range(4):
    for k in range(5):
        wood += [part((0.18, 0.18, 0.62), (-0.76 + k * 0.38, 0.17 + row * 0.19, 0), "#8a6a40" if (k + row) % 3 else "#a07a48", rotation=[0, 0, 45 * ((k + row) % 2)])]
wood += [part((0.08, 1.1, 0.08), (sx * 0.98, 0.55, sz * 0.3), "#3a2a1a") for sx in (1, -1) for sz in (1, -1)]
wood += [part((2.1, 0.05, 0.9), (0, 1.12, 0), "#4a4038", rotation=[6, 0, 0])]
PROPS.append({"name": "Firewood Rack", "description": "Split firewood stacked under a little roof. Waist-high cover.",
              "collision": "solid", "collider": [{"size": [2.0, 1.15, 0.75]}], "scatter": {"randomYaw": False, "yawStep": 90},
              "breakable": {"health": 140, "breakBy": "all", "sound": "wood", "debris": {"count": 18, "size": [0.08, 0.4], "colors": ["#8a6a40", "#a07a48"]}},
              "parts": wood})

mlog = log_x(4.6, 0.7, 0.35, BARK, end="#9a7a50")
mlog += [part((1.6, 0.08, 0.5), (-0.6, 0.71, 0.0), MOSS), part((0.9, 0.06, 0.4), (1.2, 0.69, 0.05), "#5a7a34"),
         part((0.14, 0.5, 0.14), (1.6, 0.6, 0.22), BARK, rotation=[0, 0, -25]), part((0.1, 0.45, 0.1), (-1.4, 0.5, -0.3), BARK2, rotation=[30, 0, 10]),
         part((0.5, 0.5, 0.06), (-2.2, 0.3, 0.3), BARK, rotation=[0, 30, 0])]
PROPS.append({"name": "Mossy Fallen Log", "description": "A big fallen trunk furred with moss. Cover you can vault.",
              "collision": "solid", "collider": [{"size": [4.6, 0.7, 0.72]}], "scatter": {"randomYaw": True, "scale": [0.85, 1.15]}, "parts": mlog})

branch = [part((1.8, 0.07, 0.07), (0, 0.04, 0), BARK2, rotation=[0, 0, 0]), part((0.7, 0.05, 0.05), (0.4, 0.04, 0.25), BARK2, rotation=[0, -35, 0]),
          part((0.5, 0.04, 0.04), (-0.5, 0.04, -0.2), BARK2, rotation=[0, 40, 0]), part((0.5, 0.12, 0.3), (0.85, 0.08, 0.35), "#3e5a26")]
PROPS.append({"name": "Fallen Branch", "description": "A dead branch on the forest floor.", "collision": "cosmetic",
              "fadeDistance": 40, "scatter": {"randomYaw": True, "scale": [0.7, 1.4]}, "parts": branch})

# ---------------------------------------------------------------- rocks and forest floor
bould = [part((1.8, 1.2, 1.5), (0, 0.55, 0), ROCK, rotation=[8, 20, 5], material="concrete"),
         part((1.3, 0.9, 1.2), (0.6, 0.6, 0.3), ROCK2, rotation=[-6, 55, 10], material="concrete"),
         part((1.0, 0.7, 1.0), (-0.6, 0.4, -0.3), "#85827a", rotation=[12, -30, -8], material="concrete"),
         part((1.2, 0.08, 0.9), (0.1, 1.15, -0.1), MOSS, rotation=[8, 20, 5])]
PROPS.append({"name": "Mossy Boulder", "description": "A big lichened granite boulder, about chest high.",
              "collision": "solid", "collider": [{"size": [2.2, 1.3, 1.9]}], "scatter": {"randomYaw": True, "scale": [0.8, 1.3]}, "parts": bould})

stones = [part((0.4, 0.22, 0.32), (0, 0.1, 0), ROCK, rotation=[0, 20, 6]), part((0.26, 0.16, 0.22), (0.3, 0.07, 0.15), ROCK2, rotation=[0, -30, 0]),
          part((0.2, 0.12, 0.2), (-0.25, 0.05, 0.18), "#85827a", rotation=[0, 50, 8]), part((0.3, 0.03, 0.2), (0.0, 0.215, 0), MOSS, rotation=[0, 20, 6])]
PROPS.append({"name": "Forest Stones", "description": "A few mossy stones.", "collision": "cosmetic",
              "fadeDistance": 45, "scatter": {"randomYaw": True, "scale": [0.7, 1.6]}, "parts": stones})

mush = []
for k, (x, z, h, c) in enumerate(((0, 0, 0.14, "#c83020"), (0.12, 0.08, 0.1, "#c83020"), (-0.1, 0.1, 0.08, "#e8d8b0"), (0.05, -0.12, 0.11, "#b07040"))):
    mush += [part((0.03, h, 0.03), (x, h / 2, z), "#e8e0c8"), part((0.1, 0.04, 0.1), (x, h, z), c, rotation=[0, 45 * k, 0])]
PROPS.append({"name": "Toadstools", "description": "A cluster of toadstools.", "collision": "cosmetic",
              "fadeDistance": 25, "scatter": {"randomYaw": True, "scale": [0.8, 1.5]}, "parts": mush})

leaves = [part((0.9, 0.02, 0.7), (0, 0.01, 0), "#6a5a2a", rotation=[0, 10, 0]), part((0.6, 0.025, 0.5), (0.3, 0.012, 0.2), "#7a4a20", rotation=[0, 50, 0]),
          part((0.5, 0.022, 0.4), (-0.3, 0.011, -0.15), "#8a6a2a", rotation=[0, -30, 0]), part((0.08, 0.04, 0.06), (0.1, 0.03, -0.1), "#5a3a1a")]
PROPS.append({"name": "Leaf Litter", "description": "A drift of fallen leaves and pine needles.", "collision": "cosmetic",
              "fadeDistance": 30, "scatter": {"randomYaw": True, "scale": [0.8, 1.8]}, "parts": leaves})

fern = [part((0.75, 0.03, 0.18), (0.3 * math.cos(math.radians(a)), 0.2, -0.3 * math.sin(math.radians(a))), "#3e6a2a" if a % 120 else "#4a7a30",
             rotation=[0, a, 32]) for a in range(0, 360, 60)]
fern += [part((0.6, 0.03, 0.14), (0.18 * math.cos(math.radians(a)), 0.3, -0.18 * math.sin(math.radians(a))), "#4a7a30", rotation=[0, a, 55]) for a in range(30, 360, 120)]
PROPS.append({"name": "Fern Clump", "description": "A clump of bracken ferns.", "collision": "cosmetic",
              "fadeDistance": 45, "scatter": {"randomYaw": True, "scale": [0.8, 1.5]}, "parts": fern})

# ---------------------------------------------------------------- trail and ranger things
sign = [part((0.12, 1.6, 0.12), (0, 0.8, 0), "#5a4028"), part((0.9, 0.36, 0.05), (0.25, 1.35, 0.07), "#6a4a2a", material="wood"),
        part((0.7, 0.05, 0.06), (0.25, 1.4, 0.1), "#e8dcb0"), part((0.5, 0.05, 0.06), (0.2, 1.28, 0.1), "#e8dcb0"),
        part((0.2, 0.2, 0.05), (0.62, 1.35, 0.07), "#6a4a2a", rotation=[0, 0, 45])]
PROPS.append({"name": "Trail Sign", "description": "A carved wooden trail marker pointing the way.", "collision": "solid",
              "collider": [{"size": [0.14, 1.6, 0.14]}], "scatter": {"randomYaw": True},
              "breakable": {"health": 50, "breakBy": "all", "sound": "wood"}, "parts": sign})

fds = [part((0.1, 2.0, 0.1), (sx * 0.6, 1.0, 0), "#5a4028") for sx in (1, -1)]
fds += [part((1.5, 0.9, 0.06), (0, 1.5, 0.06), "#6a4a2a", material="wood"), part((1.2, 0.18, 0.04), (0, 1.8, 0.1), "#e8dcb0"),
        part((1.1, 0.4, 0.03), (0, 1.36, 0.1), "#e8dcb0")]
for k, c in enumerate(("#2a8a2a", "#3060c0", "#e0c020", "#e07020", "#c02020")):
    fds.append(part((0.2, 0.36, 0.03), (-0.44 + k * 0.22, 1.36, 0.115), c))
fds.append(part((0.06, 0.32, 0.02), (0.22, 1.36, 0.135), "black", rotation=[0, 0, -60]))
PROPS.append({"name": "Fire Danger Sign", "description": "The ranger's fire danger board, the needle on HIGH.", "collision": "solid",
              "collider": [{"size": [1.4, 2.0, 0.2]}], "scatter": {"randomYaw": False},
              "breakable": {"health": 90, "breakBy": "all", "sound": "wood"}, "parts": fds})

oh = [part((1.3, 0.1, 1.3), (0, 0.05, 0), "#4a3a28"),
      part((1.3, 2.2, 0.06), (0, 1.15, -0.62), "#6a4c30", material="wood"), part((0.06, 2.2, 1.3), (0.62, 1.15, 0), "#6a4c30", material="wood"),
      part((0.06, 2.2, 1.3), (-0.62, 1.15, 0), "#6a4c30", material="wood"), part((1.3, 2.1, 0.06), (0, 1.1, 0.62), "#76563a", material="wood"),
      part((0.18, 0.18, 0.02), (0, 1.8, 0.66), "#20180c", rotation=[0, 0, 0]),                                     # the moon cut-out
      part((0.06, 0.06, 0.03), (0.5, 1.05, 0.66), "#a8a090"), part((1.5, 0.06, 1.5), (0, 2.3, 0), "#3a3a36", rotation=[8, 0, 0]),
      part((0.18, 0.4, 0.18), (0.4, 2.5, -0.4), "#3a3a36")]
PROPS.append({"name": "Outhouse", "description": "A wooden outhouse with a crescent moon on the door. Solid cover.", "collision": "solid",
              "collider": [{"size": [1.3, 2.25, 1.3]}], "scatter": {"randomYaw": False, "yawStep": 90},
              "breakable": {"health": 150, "breakBy": "all", "sound": "wood", "debris": {"count": 18, "size": [0.08, 0.5]}}, "parts": oh})

birdh = [part((0.08, 1.5, 0.08), (0, 0.75, 0), "#5a4028"), part((0.3, 0.26, 0.26), (0, 1.63, 0), "#8a5a30"),
         part((0.36, 0.04, 0.34), (0, 1.79, -0.07), "#3a3a36", rotation=[30, 0, 0]), part((0.36, 0.04, 0.34), (0, 1.79, 0.07), "#3a3a36", rotation=[-30, 0, 0]),
         part((0.07, 0.07, 0.02), (0, 1.65, 0.135), "#1a1208")]
PROPS.append({"name": "Bird Box Post", "description": "A bird box on a post.", "collision": "cosmetic", "scatter": {"randomYaw": True}, "parts": birdh})

# ---------------------------------------------------------------- bunker kit
radio = [part((0.5, 0.3, 0.3), (0, 0.15, 0), OLIVE), part((0.44, 0.2, 0.02), (0, 0.17, 0.155), "#2a2e24"),
         part((0.05, 0.05, 0.02), (-0.14, 0.2, 0.17), "#ffb030", anim={"type": "blink", "period": 1.6, "duty": 0.3, "color": "#ffc040", "off": "#402a08", "glow": 1.3}),
         part((0.05, 0.05, 0.02), (-0.06, 0.2, 0.17), "#40ff60", anim={"type": "flicker", "rate": 6, "duty": 0.7, "color": "#60ff70", "off": "#0a3010", "glow": 1.0}),
         part((0.08, 0.08, 0.03), (0.12, 0.17, 0.17), "#1a1a1a"), part((0.015, 0.7, 0.015), (0.2, 0.62, -0.1), "#202020"),
         part((0.14, 0.1, 0.07), (-0.36, 0.05, 0.05), "#1a1a1a"), part((0.05, 0.02, 0.2), (-0.36, 0.11, 0.05), "#1a1a1a")]
PROPS.append({"name": "Field Radio", "description": "An olive field radio set, its lamps blinking, the handset beside it.", "collision": "cosmetic",
              "scatter": {"randomYaw": True}, "breakable": {"health": 30, "breakBy": "all", "sound": "metal"}, "parts": radio})

mt = [part((2.2, 0.08, 1.3), (0, 0.84, 0), "#5a4a34", material="wood")]
mt += [part((0.1, 0.8, 0.1), (sx * 1.0, 0.4, sz * 0.55), "#3a3026") for sx in (1, -1) for sz in (1, -1)]
mt += [part((1.6, 0.01, 1.0), (0, 0.885, 0), "#d8cca0"), part((0.5, 0.012, 0.36), (-0.3, 0.89, 0.1), "#7a9a6a"),
       part((0.4, 0.012, 0.3), (0.4, 0.89, -0.2), "#7a9a6a"), part((0.6, 0.013, 0.04), (0.1, 0.891, 0.0), "#3060a0", rotation=[0, 30, 0]),
       part((0.05, 0.12, 0.05), (0.3, 0.94, 0.15), "#c02020"), part((0.05, 0.12, 0.05), (-0.4, 0.94, -0.2), "#2050c0"),
       part((0.05, 0.12, 0.05), (0.1, 0.94, -0.3), "#c02020"), part((0.25, 0.04, 0.18), (-0.8, 0.9, 0.35), "#1a1a1a")]
PROPS.append({"name": "Bunker Map Table", "description": "A plotting table with a contour map and coloured pins on it.", "collision": "solid",
              "collider": [{"size": [2.2, 0.88, 1.3]}], "scatter": {"randomYaw": False, "yawStep": 90},
              "breakable": {"health": 110, "breakBy": "all", "sound": "wood"}, "parts": mt})

lamp = [part((0.04, 0.3, 0.04), (0, 0.55, 0), "#202020"), part((0.2, 0.04, 0.2), (0, 0.4, 0), "#303030"),
        part((0.12, 0.16, 0.12), (0, 0.3, 0), "#ffe8a0", anim={"type": "flicker", "rate": 6, "duty": 0.86, "color": "#fff0b0", "off": "#3a3420", "glow": 2.4}),
        part((0.16, 0.02, 0.02), (0, 0.3, 0.07), "#202020"), part((0.02, 0.02, 0.16), (0.07, 0.3, 0), "#202020"),
        part((0.16, 0.02, 0.02), (0, 0.3, -0.07), "#202020"), part((0.02, 0.02, 0.16), (-0.07, 0.3, 0), "#202020")]
PROPS.append({"name": "Bunker Cage Lamp", "description": "A caged bulb hanging from a bunker ceiling, flickering on generator power. Its top is 0.7 m up: place it with y = ceiling - 0.7.",
              "collision": "cosmetic", "fadeDistance": 90, "scatter": {"randomYaw": True}, "parts": lamp})

sc = [part((1.0, 0.6, 0.6), (0, 0.3, 0), OLIVE, material="wood"), part((1.02, 0.06, 0.62), (0, 0.57, 0), "#4a5232"),
      part((0.4, 0.14, 0.02), (0, 0.35, 0.305), "#d8d0a0"), part((0.06, 0.08, 0.64), (0.4, 0.3, 0), "#2a2a22"), part((0.06, 0.08, 0.64), (-0.4, 0.3, 0), "#2a2a22")]
PROPS.append({"name": "Supply Crate", "description": "An olive army supply crate, stencilled. Knee-high cover.", "collision": "solid",
              "collider": [{"size": [1.0, 0.6, 0.62]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 8},
              "breakable": {"health": 60, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.06, 0.3], "colors": [OLIVE, "#4a5232"]}},
              "parts": sc})

ammo = [part((0.32, 0.2, 0.16), (0, 0.1, 0), "#4a5a30"), part((0.06, 0.03, 0.04), (0, 0.215, 0), "#2a2a22"), part((0.2, 0.06, 0.005), (0, 0.12, 0.081), "#e0d090")]
PROPS.append({"name": "Ammo Can", "description": "A green steel ammo can.", "collision": "cosmetic", "scatter": {"randomYaw": True}, "parts": ammo})

hatch = [part((1.2, 0.08, 1.2), (0, 0.04, 0), "#5a5a52", material="metal"), part((1.0, 0.06, 1.0), (0, 0.55, -0.62), "#6a6a5e", rotation=[-75, 0, 0], material="metal"),
         part((0.3, 0.04, 0.04), (0, 0.95, -0.66), "#303030"), part((0.08, 0.08, 0.08), (0.45, 0.12, -0.55), "#303030")]
PROPS.append({"name": "Bunker Hatch Lid", "description": "A steel hatch frame with its lid propped open. Lay it round a stairwell opening.",
              "collision": "cosmetic", "scatter": {"randomYaw": False}, "parts": hatch})

# ---------------------------------------------------------------- cabin kit: log walls, roofs, porch, chimney
# Cabins are built from these instead of map boxes: props are drawn instanced (no draw call per wall) and still collide.
# Walls run along X, centred on the origin, 0.3 m thick (z -0.15..0.15), 2.7 m tall: nine 0.3 m log courses. Corners
# overlap (the logs cross like a notched corner). Openings: a door 1.1 m wide up to 2.1 m, a window 1.0 m wide from
# 0.95 to 2.25 m (mantle through it).
WALL_H, COURSE, THICK = 2.7, 0.3, 0.3
LOGS = ("#6b4a2c", "#5e4126", "#73502f")


def courses(x0, x1, k0, k1, z=0.0):
    """Log courses k0..k1-1 from x0 to x1."""
    return [part((x1 - x0, COURSE * 0.96, THICK), ((x0 + x1) / 2, COURSE * (k + 0.5), z), LOGS[k % 3], material="wood") for k in range(k0, k1)]


def wall(length, opening=None):
    L = length / 2
    ps, col = [], []
    if opening is None:
        ps = courses(-L, L, 0, 9)
        col = [{"size": [length, WALL_H, THICK]}]
    elif opening == "door":
        a, b, top = -0.55, 0.55, 7    # courses 0..6 split (2.1 m)
        ps = courses(-L, a, 0, top) + courses(b, L, 0, top) + courses(-L, L, top, 9)
        ps += [part((0.08, 2.1, 0.34), (x, 1.05, 0), "#3e2a18") for x in (a, b)] + [part((1.26, 0.1, 0.34), (0, 2.1, 0), "#3e2a18")]
        ps += [part((0.05, 2.0, 0.9), (b + 0.05, 1.0, 0.46), "#4a3220", rotation=[0, -70, 0])]    # the door, swung open outwards
        col = [{"size": [L + a, WALL_H, THICK], "offset": [(-L + a) / 2, WALL_H / 2, 0]}, {"size": [L - b, WALL_H, THICK], "offset": [(b + L) / 2, WALL_H / 2, 0]},
               {"size": [b - a, WALL_H - 2.1, THICK], "offset": [0, (2.1 + WALL_H) / 2, 0]}]
    else:   # window
        a, b = -0.5, 0.5
        ps = courses(-L, L, 0, 3) + courses(-L, a, 3, 7) + courses(b, L, 3, 7) + courses(-L, L, 7, 9)
        ps += [part((1.12, 0.06, 0.4), (0, 0.93, 0.02), "#3e2a18"), part((0.06, 1.3, 0.34), (a - 0.03, 1.6, 0), "#3e2a18"),
               part((0.06, 1.3, 0.34), (b + 0.03, 1.6, 0), "#3e2a18"), part((0.5, 1.2, 0.04), (b + 0.3, 1.6, 0.2), "#4a6a3a", rotation=[0, 20, 0])]  # shutter
        col = [{"size": [L + a, WALL_H, THICK], "offset": [(-L + a) / 2, WALL_H / 2, 0]}, {"size": [L - b, WALL_H, THICK], "offset": [(b + L) / 2, WALL_H / 2, 0]},
               {"size": [b - a, 0.9, THICK], "offset": [0, 0.45, 0]}, {"size": [b - a, WALL_H - 2.1, THICK], "offset": [0, (2.1 + WALL_H) / 2, 0]}]
    return ps, col


for length in (3.0, 1.5):
    for opening in ((None, "door", "window") if length == 3.0 else (None,)):
        ps, col = wall(length, opening)
        nm = f"Log Wall {length:g}m" + {None: "", "door": " Door", "window": " Window"}[opening]
        desc = {None: "A section of log-cabin wall", "door": "A log-cabin wall with an open door", "window": "A log-cabin wall with a window you can climb through"}[opening]
        PROPS.append({"name": nm, "description": f"{desc}, {length:g} m long, 2.7 m tall. Cabin kit: runs along X, centred.",
                      "collision": "solid", "collider": col, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": ps})


def roof(w, d, color="#3c3a34"):
    """A pitched shingle roof over a w x d cabin (ridge along X), with a flat ceiling at the wall top as its collider."""
    ov = 0.35
    rise = 1.4 if d >= 6 else 1.1
    half = d / 2 + ov
    ang = math.degrees(math.atan2(rise, half))
    slope = math.hypot(half, rise)
    ps = [part((w - 0.2, 0.14, d - 0.2), (0, WALL_H + 0.07, 0), "#5a4630", material="wood")]   # ceiling boards
    for sz in (1, -1):
        ps.append(part((w + 2 * ov, 0.12, slope), (0, WALL_H + 0.14 + rise / 2, sz * half / 2), color, rotation=[sz * ang, 0, 0], material="wood"))
    ps.append(part((w + 2 * ov + 0.04, 0.16, 0.24), (0, WALL_H + 0.14 + rise, 0), "#2c2a26"))          # ridge cap
    # gable ends: stepped log courses up to the ridge
    n = 4
    for k in range(n):
        gw = (d - 0.3) * (1 - (k + 0.5) / n)
        for sx in (1, -1):
            ps.append(part((THICK, rise / n, gw), (sx * (w / 2 - 0.15), WALL_H + 0.14 + rise * (k + 0.5) / n, 0), LOGS[k % 3], material="wood"))
    return ps, [{"size": [w, 0.16, d], "offset": [0, WALL_H + 0.08, 0]}]


for w, d in ((4.5, 4.5), (6.0, 4.5), (9.0, 6.0), (6.0, 6.0)):
    ps, col = roof(w, d)
    PROPS.append({"name": f"Cabin Roof {w:g}x{d:g}", "description": f"A pitched roof and ceiling for a {w:g} x {d:g} m log cabin (ridge along X). Place it at the cabin's centre.",
                  "collision": "solid", "collider": col, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": ps})

porch = [part((3.0, 0.2, 1.8), (0, 0.1, 0), "#7a5a38", material="wood")]
porch += [part((2.9, 0.02, 0.12), (0, 0.205, z), "#5e4428") for z in (-0.6, -0.2, 0.2, 0.6)]
porch += [part((0.14, 2.5, 0.14), (sx * 1.4, 1.35, 0.8), "#5a4028") for sx in (1, -1)]
porch += [part((3.3, 0.1, 2.0), (0, 2.6, 0.1), "#3c3a34", rotation=[-10, 0, 0], material="wood"), part((0.06, 0.9, 1.8), (1.42, 0.65, 0), "#5a4028"),
          part((0.06, 0.06, 1.7), (1.42, 1.05, 0), "#5a4028"), part((0.06, 0.9, 1.8), (-1.42, 0.65, 0), "#5a4028"), part((0.06, 0.06, 1.7), (-1.42, 1.05, 0), "#5a4028")]
PROPS.append({"name": "Cabin Porch", "description": "A plank porch with a lean-to roof on two posts, 3 x 1.8 m. Its back (-Z) goes against the cabin's front wall.",
              "collision": "solid", "collider": [{"size": [3.0, 0.2, 1.8]}, {"size": [0.14, 2.5, 0.14], "offset": [1.4, 1.45, 0.8]}, {"size": [0.14, 2.5, 0.14], "offset": [-1.4, 1.45, 0.8]}],
              "scatter": {"randomYaw": False, "yawStep": 90}, "parts": porch})

chim = [part((0.9, 1.2, 0.9), (0, 0.6, 0), "#77726a", material="concrete"), part((0.7, 3.2, 0.7), (0, 2.8, 0), "#6e6a62", material="concrete"),
        part((0.8, 0.2, 0.8), (0, 4.45, 0), "#5a5650"), part((0.3, 0.3, 0.3), (0, 4.75, 0), "#9a9a9a",
        anim={"type": "bob", "distance": 0.25, "period": 3.5, "jitter": 0.3}), part((0.22, 0.22, 0.22), (0.05, 5.2, 0.05), "#b0b0b0", anim={"type": "bob", "distance": 0.35, "period": 4.1, "jitter": 0.3})]
PROPS.append({"name": "Stone Chimney", "description": "A fieldstone chimney stack for a cabin's gable end, a wisp of smoke over it.",
              "collision": "solid", "collider": [{"size": [0.9, 1.2, 0.9]}, {"size": [0.7, 3.4, 0.7], "offset": [0, 2.9, 0]}],
              "scatter": {"randomYaw": False, "yawStep": 90}, "parts": chim})

# ---------------------------------------------------------------- write
os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT):
    if f.endswith(".json"):
        os.remove(os.path.join(OUT, f))
names = set()
for p in PROPS:
    assert p["name"] not in names, p["name"]
    names.add(p["name"])
    d = {"format": "call-of-slop-prop/1", "name": p["name"], "category": "Timberline", "description": p["description"], "author": "Call of Slop"}
    for k in ("collision", "collider", "scatter", "fadeDistance", "breakable"):
        if k in p:
            d[k] = p[k]
    d["parts"] = p["parts"]
    assert 1 <= len(d["parts"]) <= 128, (p["name"], len(d["parts"]))
    path = os.path.join(OUT, p["name"].replace(" ", "") + ".json")
    with open(path, "w") as f:
        f.write("{\n")
        for k in d:
            if k == "parts":
                f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(q) for q in d["parts"]) + "\n  ]\n")
            else:
                f.write(f'  "{k}": {json.dumps(d[k])},\n')
        f.write("}\n")
print(f"{len(PROPS)} Timberline props -> {OUT}")
