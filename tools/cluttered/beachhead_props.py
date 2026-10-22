#!/usr/bin/env python3
"""Writes the Cluttered pack's beach-landing and army-base props (Maps/Cluttered/Props/Beachhead/*.json), the things no
global prop covers: beach obstacles (czech hedgehogs, dragon's teeth, stake ramps, concertina wire), wrecks (a landing
craft, a beached fishing boat), army vehicles and kit (jeep, truck, fuel bowser, ammo crates, radio sets, a map table,
rifle racks, sandbag nests, camo nets) and a few animated things (a wind sock, a sweeping searchlight, helipad lights).
They're pack-local (Props/README.md, "Pack props"): only the Cluttered pack's maps see them. 09 Beachhead
(tools/cluttered/beachhead.py) uses them.

Usage: python3 tools/cluttered/beachhead_props.py <repo root>

Concertina wire and the barbed wire fence are cosmetic (bots and players push through: wire you can't cross would
make the beach a maze for bots); everything big is solid and unbreakable, crates and kit break.
"""
import json, math, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/Cluttered/Props/Beachhead")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def octagon(d, h, y, color, x=0.0, z=0.0, axis="y", **kw):
    """An octagon d across and h long, centred at (x, y, z): standing (axis y), or lying along x or z (a wheel, a tank)."""
    out = []
    for a in (0, 45, 90, 135):
        if axis == "y":
            out.append(part((d, h, d * 0.414), (x, y, z), color, rotation=[0, a, 0], **kw))
        elif axis == "x":
            out.append(part((h, d, d * 0.414), (x, y, z), color, rotation=[a, 0, 0], **kw))
        else:
            out.append(part((d * 0.414, d, h), (x, y, z), color, rotation=[0, 0, a], **kw))
    return out


def wheel(x, z, d=0.8, w=0.3):
    return octagon(d, w, d / 2, "rubber", x=x, z=z, axis="z") + [part((d * 0.45, d * 0.45, w + 0.02), (x, d / 2, z), "#3a3d34")]


OLIVE = "#4b5320"; OLIVE_D = "#3b4219"; CANVAS = "#6b6a45"; RUST = "#5a3d2b"; STEEL_D = "#3c3f44"; HULL = "#5d6268"
PROPS = []

# ---------------------------------------------------------------- beach obstacles
hh = []
for yaw in (0, 60, 120):
    hh.append(part((1.75, 0.14, 0.14), (0, 0.62, 0), RUST, rotation=[0, yaw, 38], material="metal"))
    hh.append(part((1.75, 0.04, 0.2), (0, 0.62, 0), "#6b4a34", rotation=[0, yaw, 38], material="metal"))
hh.append(part((0.3, 0.3, 0.3), (0, 0.62, 0), "#4a3426", material="metal"))
PROPS.append({"name": "Czech Hedgehog", "category": "Beachhead", "description": "A rusty steel anti-tank hedgehog of three crossed girders. Unbreakable cover.",
              "collision": "solid", "collider": [{"size": [1.1, 1.2, 1.1]}], "scatter": {"randomYaw": True, "scale": [0.9, 1.1]}, "parts": hh})

dt = [part((1.0, 0.4, 1.0), (0, 0.2, 0), "concrete", material="concrete"), part((0.76, 0.4, 0.76), (0, 0.6, 0), "concrete", material="concrete"),
      part((0.5, 0.36, 0.5), (0, 0.98, 0), "concrete", material="concrete"), part((0.3, 0.06, 0.3), (0, 1.19, 0), "#8a8a84"),
      part((1.02, 0.08, 1.02), (0, 0.05, 0), "#6a6a62")]
PROPS.append({"name": "Dragons Tooth", "category": "Beachhead", "description": "A stepped concrete anti-tank block (dragon's tooth). Unbreakable cover.",
              "collision": "solid", "collider": [{"size": [1.0, 1.2, 1.0]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 10}, "parts": dt})

sr = [part((3.0, 0.24, 0.24), (0, 0.55, 0), "darkwood", rotation=[0, 0, 20], material="wood"),
      part((0.2, 1.0, 0.2), (1.0, 0.5, 0), "darkwood", material="wood"),
      part((0.2, 0.5, 0.2), (-0.6, 0.25, 0), "darkwood", material="wood"),
      part((0.3, 0.16, 0.3), (1.38, 1.1, 0), "#3b4030", material="metal"),
      part((0.12, 0.08, 0.12), (1.38, 1.22, 0), "#8a7a30")]
PROPS.append({"name": "Beach Stake Ramp", "category": "Beachhead", "description": "A log ramp on posts driven into the sand, a rusty mine on its top end.",
              "collision": "solid", "collider": [{"size": [2.8, 0.75, 0.32]}, {"size": [0.24, 1.15, 0.24], "offset": [1.0, 0.575, 0]}],
              "scatter": {"randomYaw": True}, "parts": sr})

cw = []
for k in range(6):
    x = -1.25 + k * 0.5
    for a in range(8):
        ang = a * 45.0
        cy = 0.42 + 0.38 * math.cos(math.radians(ang)); cz = 0.38 * math.sin(math.radians(ang))
        cw.append(part((0.025, 0.3, 0.025), (x + 0.06 * (a % 2), cy, cz), "#7a7a76", rotation=[ang + 90, 0, 0]))
cw += [part((3.0, 0.02, 0.02), (0, 0.8, 0), "#6a6a66"), part((3.0, 0.02, 0.02), (0, 0.06, 0.3), "#6a6a66"), part((3.0, 0.02, 0.02), (0, 0.06, -0.3), "#6a6a66"),
       part((0.05, 0.95, 0.05), (-1.45, 0.47, 0), "darkgrey"), part((0.05, 0.95, 0.05), (1.45, 0.47, 0), "darkgrey")]
PROPS.append({"name": "Concertina Wire", "category": "Beachhead", "description": "A 3 m roll of razor wire on two pickets (along X). Looks only: you push through it.",
              "collision": "cosmetic", "fadeDistance": 70, "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 12}, "parts": cw})

bw = [part((0.09, 1.35, 0.09), (-1.5, 0.67, 0), "darkwood"), part((0.09, 1.35, 0.09), (1.5, 0.67, 0), "darkwood")]
for y in (0.3, 0.6, 0.9, 1.2):
    bw.append(part((3.0, 0.02, 0.02), (0, y, 0), "#77736c"))
bw += [part((3.2, 0.02, 0.02), (0, 0.75, 0), "#77736c", rotation=[0, 0, 22]), part((3.2, 0.02, 0.02), (0, 0.75, 0), "#77736c", rotation=[0, 0, -22])]
PROPS.append({"name": "Barbed Wire Fence", "category": "Beachhead", "description": "Two wooden posts with strands of barbed wire between them (3 m along X). Looks only.",
              "collision": "cosmetic", "fadeDistance": 80, "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 6}, "parts": bw})

# ---------------------------------------------------------------- wrecks
lc = [part((10.0, 0.3, 3.6), (0, 0.15, 0), HULL, material="metal"),                          # floor
      part((10.0, 1.7, 0.25), (0, 0.85, 1.675), HULL, material="metal"),                     # sides
      part((10.0, 1.7, 0.25), (0, 0.85, -1.675), HULL, material="metal"),
      part((10.04, 0.12, 0.3), (0, 1.72, 1.675), "#4a4e54"), part((10.04, 0.12, 0.3), (0, 1.72, -1.675), "#4a4e54"),
      part((0.35, 2.5, 3.6), (-4.85, 1.25, 0), HULL, material="metal"),                      # stern
      part((1.4, 1.0, 1.6), (-4.3, 3.0, 0.6), "#565b60", material="metal"),                  # coxswain's shelter
      part((0.05, 0.4, 1.2), (-3.58, 3.1, 0.6), "#26292c"),
      part((3.0, 0.14, 3.3), (6.25, 0.6, 0), "#666b70", rotation=[0, 0, -24], material="metal"),  # the bow ramp, down
      part((0.2, 1.8, 0.35), (5.0, 0.9, 1.6), "#4a4e54"), part((0.2, 1.8, 0.35), (5.0, 0.9, -1.6), "#4a4e54"),
      part((4.0, 0.9, 0.04), (-1.5, 1.0, 1.82), RUST), part((2.5, 0.6, 0.04), (2.6, 0.7, -1.82), RUST),   # rust streaks
      part((1.1, 0.8, 0.06), (0.8, 0.75, 1.82), "#26292c"),                                  # shell hole
      part((0.5, 0.5, 0.5), (-2.4, 0.55, -1.0), OLIVE_D), part((0.6, 0.35, 0.4), (-1.6, 0.48, 1.1), OLIVE),  # kit left aboard
      part((0.3, 0.6, 0.3), (-4.4, 2.8, -1.2), "#2b2b2b")]
PROPS.append({"name": "Landing Craft Wreck", "category": "Beachhead", "description": "A grounded landing craft with its bow ramp down: steel sides to hide behind, mantle in over the side.",
              "collision": "solid",
              "collider": [{"size": [10.0, 0.3, 3.6]}, {"size": [10.0, 1.7, 0.3], "offset": [0, 0.85, 1.65]}, {"size": [10.0, 1.7, 0.3], "offset": [0, 0.85, -1.65]},
                           {"size": [0.35, 2.5, 3.6], "offset": [-4.85, 1.25, 0]}, {"size": [1.4, 1.0, 1.6], "offset": [-4.3, 3.0, 0.6]},
                           {"size": [2.4, 0.45, 3.2], "offset": [6.3, 0.225, 0]}],
              "scatter": {"randomYaw": False}, "parts": lc})

fb = [part((6.4, 1.3, 2.3), (0, 0.95, 0), "#2f5872", rotation=[13, 0, 0], material="wood"),
      part((1.6, 1.2, 1.6), (3.25, 0.98, 0), "#2f5872", rotation=[13, 45, 0], material="wood"),
      part((6.5, 0.12, 2.36), (0, 1.62, 0.15), "white", rotation=[13, 0, 0]),
      part((5.8, 0.06, 2.0), (0, 1.55, 0.12), "lightwood", rotation=[13, 0, 0], material="wood"),
      part((1.8, 1.4, 1.6), (-1.4, 2.25, 0.4), "white", rotation=[13, 0, 0]),
      part((1.9, 0.08, 1.7), (-1.4, 2.98, 0.57), "#2f5872", rotation=[13, 0, 0]),
      part((0.03, 0.45, 1.3), (-0.48, 2.45, 0.42), "#26292c", rotation=[13, 0, 0]),
      part((0.1, 3.4, 0.1), (1.2, 3.1, 0.75), "white", rotation=[13, 0, 0]),
      part((2.6, 0.06, 0.06), (2.2, 2.2, 0.55), "darkgrey", rotation=[13, 0, 28]),
      part((1.4, 0.3, 1.2), (2.0, 1.75, 0.2), "#2f5a34", rotation=[13, 0, 0]),
      part((0.9, 0.7, 0.06), (0.6, 0.8, -1.2), "#1e2a33", rotation=[13, 0, 0]),             # stove-in planks
      part((6.0, 0.3, 0.08), (0, 0.3, 1.22), "#5a6a40", rotation=[13, 0, 0])]               # weed line
PROPS.append({"name": "Beached Fishing Boat", "category": "Beachhead", "description": "A blue fishing boat run aground and leaning on its side, holed below the waterline.",
              "collision": "solid", "collider": [{"size": [6.6, 1.7, 2.3], "offset": [0, 0.85, 0.15]}, {"size": [1.7, 1.2, 1.6], "offset": [-1.4, 2.2, 0.45]}],
              "scatter": {"randomYaw": True}, "parts": fb})

# ---------------------------------------------------------------- vehicles
jp = [part((3.5, 0.35, 1.5), (0, 0.6, 0), OLIVE_D, material="metal"),                         # chassis
      part((2.1, 0.6, 1.62), (-0.55, 1.05, 0), OLIVE, material="metal"),                      # tub
      part((1.25, 0.45, 1.5), (1.15, 0.98, 0), OLIVE, material="metal"),                      # bonnet
      part((0.06, 0.55, 1.45), (0.5, 1.6, 0), OLIVE_D), part((0.03, 0.42, 1.3), (0.51, 1.6, 0), "glass"),
      part((0.5, 0.5, 0.04), (1.15, 1.22, 0), "white"), part((0.04, 0.5, 0.5), (1.15, 1.22, 0), "white"),  # white star
      part((0.6, 0.3, 0.5), (-0.3, 1.45, 0.4), "#3a3020"), part((0.6, 0.3, 0.5), (-0.3, 1.45, -0.4), "#3a3020"),  # seats
      part((0.25, 0.4, 0.35), (-1.65, 1.15, 0.5), OLIVE_D),                                   # jerry can on the back
      part((0.12, 1.4, 0.06), (-1.55, 1.9, -0.6), "darkgrey", rotation=[0, 0, -8]),         # whip aerial
      part((0.1, 0.1, 0.12), (1.78, 0.98, 0.55), "amber"), part((0.1, 0.1, 0.12), (1.78, 0.98, -0.55), "amber")]
for x in (1.15, -1.15):
    for z in (0.78, -0.78):
        jp += wheel(x, z, 0.78, 0.28)
jp += octagon(0.7, 0.24, 1.1, "rubber", x=-1.85, z=0, axis="x")
PROPS.append({"name": "Army Jeep", "category": "Beachhead", "description": "An olive army jeep with a white star on the bonnet. Solid cover.",
              "collision": "solid", "collider": [{"size": [3.6, 1.0, 1.7], "offset": [0, 0.5, 0]}, {"size": [2.2, 0.6, 1.6], "offset": [-0.5, 1.3, 0]}],
              "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 10, "scale": [1, 1]}, "parts": jp})

tr = [part((6.6, 0.4, 2.0), (0, 0.85, 0), OLIVE_D, material="metal"),
      part((1.8, 1.25, 2.3), (2.25, 1.65, 0), OLIVE, material="metal"),                      # cab
      part((1.0, 0.85, 2.2), (3.4, 1.45, 0), OLIVE, material="metal"),                       # bonnet
      part((0.04, 0.55, 2.0), (3.16, 2.0, 0), "glass"), part((1.2, 0.5, 0.04), (2.1, 2.0, 1.16), "glass"), part((1.2, 0.5, 0.04), (2.1, 2.0, -1.16), "glass"),
      part((4.3, 0.15, 2.4), (-1.15, 1.13, 0), OLIVE_D, material="wood"),                     # bed
      part((4.3, 0.6, 0.08), (-1.15, 1.5, 1.18), OLIVE, material="wood"), part((4.3, 0.6, 0.08), (-1.15, 1.5, -1.18), OLIVE, material="wood"),
      part((4.4, 1.5, 2.45), (-1.15, 2.55, 0), CANVAS), part((4.45, 0.12, 2.5), (-1.15, 3.33, 0), "#5c5b3a"),  # canvas tilt
      part((0.06, 1.4, 2.3), (-3.36, 2.5, 0), "#4a4930"),
      part((0.2, 0.2, 2.3), (3.95, 0.9, 0), "darkgrey"),                                      # bumper
      part((0.1, 0.16, 0.2), (3.92, 1.25, 0.85), "amber"), part((0.1, 0.16, 0.2), (3.92, 1.25, -0.85), "amber"),
      part((0.6, 0.6, 0.04), (2.25, 1.75, 1.17), "white"), part((0.6, 0.6, 0.04), (2.25, 1.75, -1.17), "white")]
for x in (2.7, -0.8, -2.3):
    for z in (0.95, -0.95):
        tr += wheel(x, z, 1.0, 0.36)
PROPS.append({"name": "Army Truck", "category": "Beachhead", "description": "A covered army cargo truck with a canvas tilt. Solid cover.",
              "collision": "solid", "collider": [{"size": [7.0, 1.3, 2.4], "offset": [0.3, 0.65, 0]}, {"size": [2.9, 1.0, 2.3], "offset": [2.7, 1.8, 0]},
                                                 {"size": [4.4, 2.1, 2.45], "offset": [-1.15, 2.35, 0]}],
              "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 6, "scale": [1, 1]}, "parts": tr})

fbz = octagon(1.5, 3.6, 1.35, "#5a5d3a", x=0, axis="x", material="metal") + [
    part((3.8, 0.25, 1.1), (0, 0.55, 0), OLIVE_D), part((1.6, 0.08, 0.08), (2.6, 0.55, 0), "darkgrey"),
    part((0.4, 0.25, 0.4), (0, 2.18, 0), "darkgrey"), part((0.3, 0.3, 0.3), (-1.85, 1.0, 0.5), "#2b2b2b"),
    part((1.0, 0.2, 0.04), (0, 1.45, 0.77), "#c8b040"), part((0.4, 0.12, 0.04), (0, 1.45, 0.79), "red")]
fbz += wheel(-0.4, 0.62, 0.85, 0.28) + wheel(-0.4, -0.62, 0.85, 0.28)
PROPS.append({"name": "Fuel Bowser", "category": "Beachhead", "description": "A towed army fuel bowser. Solid cover (it doesn't go off).",
              "collision": "solid", "collider": [{"size": [3.7, 2.1, 1.55]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 12}, "parts": fbz})

# ---------------------------------------------------------------- kit
ac = []
for (x, y, z, w, h, d) in [(-0.33, 0.22, 0, 0.6, 0.44, 0.55), (0.33, 0.22, 0, 0.6, 0.44, 0.55), (0.0, 0.66, 0.02, 0.6, 0.44, 0.55)]:
    ac += [part((w, h, d), (x, y, z), "#4f5a2c", material="wood"), part((w + 0.02, 0.05, d + 0.02), (x, y + h / 2 - 0.03, z), "#3a4220"),
           part((0.34, 0.1, 0.01), (x, y, z + d / 2 + 0.003), "#d8c040"), part((0.1, 0.05, 0.05), (x - w / 2 - 0.02, y + 0.08, z), "#2b2b2b"),
           part((0.1, 0.05, 0.05), (x + w / 2 + 0.02, y + 0.08, z), "#2b2b2b")]
PROPS.append({"name": "Ammo Crate Stack", "category": "Beachhead", "description": "Green wooden ammunition crates stacked three high, stencilled in yellow.",
              "collision": "solid", "collider": [{"size": [1.3, 0.88, 0.56]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 10},
              "breakable": {"health": 60, "breakBy": "all", "sound": "wood", "debris": {"count": 14, "size": [0.05, 0.3], "colors": ["#4f5a2c", "#3a4220", "#d8c040"]}},
              "parts": ac})

ab = [part((0.3, 0.19, 0.15), (0, 0.095, 0), "#4f5a2c", material="metal"), part((0.31, 0.03, 0.16), (0, 0.2, 0), "#3a4220"),
      part((0.12, 0.02, 0.03), (0, 0.225, 0), "#2b2b2b"), part((0.18, 0.05, 0.005), (0, 0.1, 0.077), "#d8c040")]
PROPS.append({"name": "Ammo Box", "category": "Beachhead", "description": "A steel ammunition can.", "collision": "cosmetic",
              "breakable": {"health": 15, "breakBy": "all", "sound": "metal", "debris": {"count": 5, "size": [0.03, 0.08]}}, "parts": ab})

rs = [part((0.46, 0.3, 0.3), (0, 0.15, 0), "#4a5230", material="metal"), part((0.42, 0.18, 0.01), (0, 0.17, 0.152), "#2b2b2b"),
      part((0.05, 0.05, 0.02), (-0.12, 0.2, 0.16), "#c8c8c0"), part((0.05, 0.05, 0.02), (0.0, 0.2, 0.16), "#c8c8c0"),
      part((0.08, 0.04, 0.02), (0.13, 0.2, 0.16), "#40d060", anim={"type": "blink", "period": 1.2, "duty": 0.5, "color": "#40ff70", "glow": 1.0}),
      part((0.012, 0.9, 0.012), (0.18, 0.75, -0.08), "#2b2b2b"),
      part((0.08, 0.18, 0.06), (-0.27, 0.12, 0.05), "#1e1e1e")]
PROPS.append({"name": "Radio Set", "category": "Beachhead", "description": "A field radio with a whip aerial and a blinking set light (desk or crate top).",
              "collision": "cosmetic", "breakable": {"health": 25, "breakBy": "all", "sound": "metal", "debris": {"count": 6, "size": [0.03, 0.1]}}, "parts": rs})

mt = [part((2.0, 0.07, 1.2), (0, 0.8, 0), "darkwood", material="wood"),
      part((1.8, 0.005, 1.05), (0, 0.84, 0), "#d8cfa8"),
      part((0.7, 0.006, 0.5), (-0.4, 0.843, -0.1), "#6a9a5a"), part((0.5, 0.006, 0.3), (0.5, 0.843, 0.2), "#7aa0c0"),
      part((0.08, 0.04, 0.08), (0.1, 0.86, 0.1), "red"), part((0.08, 0.04, 0.08), (-0.3, 0.86, -0.25), "blue"), part((0.08, 0.04, 0.08), (0.45, 0.86, -0.3), "red"),
      part((0.3, 0.02, 0.04), (0.65, 0.85, 0.42), "#2b2b2b")]
for x in (-0.92, 0.92):
    for z in (-0.52, 0.52):
        mt.append(part((0.08, 0.77, 0.08), (x, 0.385, z), "darkwood"))
PROPS.append({"name": "Map Table", "category": "Beachhead", "description": "A planning table with a map of the coast and unit markers on it.",
              "collision": "solid", "collider": [{"size": [2.0, 0.84, 1.2]}], "scatter": {"randomYaw": False, "yawStep": 90},
              "breakable": {"health": 90, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.05, 0.4], "colors": ["darkwood", "#d8cfa8"]}},
              "parts": mt})

rr = [part((1.0, 0.08, 0.32), (0, 0.04, 0), "darkwood", material="wood"), part((1.0, 0.08, 0.2), (0, 1.15, -0.05), "darkwood", material="wood"),
      part((0.06, 1.2, 0.06), (-0.47, 0.6, -0.12), "darkwood"), part((0.06, 1.2, 0.06), (0.47, 0.6, -0.12), "darkwood")]
for k in range(5):
    x = -0.36 + k * 0.18
    rr += [part((0.05, 1.05, 0.06), (x, 0.62, 0.0), "#3a2a1c", rotation=[-8, 0, 0]), part((0.03, 0.35, 0.04), (x, 1.05, 0.06), "#2b2b2b", rotation=[-8, 0, 0])]
PROPS.append({"name": "Rifle Rack", "category": "Beachhead", "description": "A wooden rack of five rifles, for the barracks wall.",
              "collision": "solid", "collider": [{"size": [1.0, 1.2, 0.34]}], "scatter": {"randomYaw": False, "yawStep": 90},
              "breakable": {"health": 70, "breakBy": "all", "sound": "wood", "debris": {"count": 10, "size": [0.04, 0.4]}}, "parts": rr})

sn = []
for side, (cx, cz, w, d) in enumerate([(0, 1.0, 2.8, 0.55), (-1.35, -0.05, 0.55, 1.6), (1.35, -0.05, 0.55, 1.6)]):
    for row in range(3):
        y = 0.16 + row * 0.3
        sn.append(part((w - row * 0.05, 0.3, d - row * 0.04), (cx, y, cz), "#8a7a52" if (row + side) % 2 else "#9a8a60", material="dirt"))
sn += [part((0.5, 0.32, 0.3), (0.2, 1.05, 1.0), "#8a7a52", material="dirt"), part((0.6, 0.06, 0.05), (0.25, 1.0, 0.65), "#2b2b2b")]
PROPS.append({"name": "Sandbag Nest", "category": "Beachhead", "description": "A U of sandbags three high, an MG position facing +Z. Unbreakable cover.",
              "collision": "solid", "collider": [{"size": [2.8, 0.92, 0.55], "offset": [0, 0.46, 1.0]}, {"size": [0.55, 0.92, 1.6], "offset": [-1.35, 0.46, -0.05]},
                                                 {"size": [0.55, 0.92, 1.6], "offset": [1.35, 0.46, -0.05]}],
              "scatter": {"randomYaw": False, "yawStep": 90}, "parts": sn})

cn = [part((0.08, 2.5, 0.08), (x, 1.25, z), "darkwood") for x in (-2.4, 2.4) for z in (-1.9, 1.9)]
cn += [part((5.2, 0.04, 4.2), (0, 2.45, 0), "#4d5a2a"), part((2.0, 0.05, 1.6), (-1.2, 2.38, 0.6), "#5f6a34"), part((1.6, 0.05, 1.8), (1.4, 2.36, -0.8), "#3e4722"),
       part((1.4, 0.05, 1.2), (0.6, 2.4, 1.2), "#6b6a45"), part((1.2, 0.05, 1.0), (-1.6, 2.37, -1.3), "#556030")]
PROPS.append({"name": "Camo Net", "category": "Beachhead", "description": "A camouflage net on four poles, 2.5 m up, over a vehicle or a gun pit. Looks only.",
              "collision": "cosmetic", "fadeDistance": 120, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": cn})

# ---------------------------------------------------------------- animated
ws = [part((0.12, 4.0, 0.12), (0, 2.0, 0), "#c8c8c0"), part((0.5, 0.1, 0.5), (0, 0.05, 0), "concrete"),
      part((0.06, 0.06, 0.06), (0, 4.0, 0), "darkgrey", anim={"type": "swing", "axis": "y", "pivot": [0, 4.0, 0], "angle": 35, "period": 7.0, "wind": 1.0, "jitter": 0.3})]
for k, (x, d, c) in enumerate([(0.3, 0.42, "orange"), (0.75, 0.36, "white"), (1.2, 0.3, "orange"), (1.6, 0.24, "white")]):
    ws.append(part((0.45, d, d), (x, 3.85 - k * 0.06, 0), c, anim={"type": "follow", "parent": 3}))
PROPS.append({"name": "Wind Sock", "category": "Beachhead", "description": "An orange and white wind sock on a pole, swinging round with the wind.",
              "collision": "solid", "collider": [{"size": [0.5, 0.1, 0.5]}, {"size": [0.14, 4.0, 0.14]}], "scatter": {"randomYaw": True}, "parts": ws})

sl = [part((0.7, 0.08, 0.7), (0, 0.04, 0), "darkgrey"), part((0.12, 1.3, 0.12), (0, 0.69, 0), "darkgrey"),
      part((0.3, 0.12, 0.3), (0, 1.36, 0), STEEL_D, anim={"type": "swing", "axis": "y", "pivot": [0, 1.4, 0], "angle": 55, "period": 11.0, "jitter": 0.3}),
      part((0.56, 0.56, 0.6), (0, 1.75, 0.05), STEEL_D, material="metal", anim={"type": "follow", "parent": 3}),
      part((0.46, 0.46, 0.03), (0, 1.75, 0.36), "#fff4c8", anim={"type": "follow", "parent": 3, "glow": 2.2}),
      part((0.06, 0.3, 0.06), (0.32, 1.6, 0.05), "darkgrey", anim={"type": "follow", "parent": 3})]
PROPS.append({"name": "Searchlight", "category": "Beachhead", "description": "A searchlight on a post, sweeping slowly from side to side (faces +Z).",
              "collision": "solid", "collider": [{"size": [0.7, 0.08, 0.7]}, {"size": [0.2, 1.5, 0.2], "offset": [0, 0.75, 0]}], "scatter": {"randomYaw": False}, "parts": sl})

hl = [part((0.22, 0.1, 0.22), (0, 0.05, 0), "darkgrey"),
      part((0.14, 0.1, 0.14), (0, 0.15, 0), "#e0a020", anim={"type": "blink", "period": 1.6, "duty": 0.4, "color": "#ffc040", "off": "#5a4010", "glow": 1.6})]
PROPS.append({"name": "Helipad Light", "category": "Beachhead", "description": "A blinking amber edge light for a helipad.", "collision": "cosmetic", "fadeDistance": 90, "parts": hl})


def main():
    os.makedirs(OUT, exist_ok=True)
    for p in PROPS:
        d = {"format": "call-of-slop-prop/1", "name": p["name"], "category": p.get("category", "Beachhead"), "description": p["description"],
             "author": "Call of Slop", "collision": p.get("collision", "solid")}
        for k in ("collider", "scatter", "fadeDistance", "breakable"):
            if k in p:
                d[k] = p[k]
        d["parts"] = p["parts"]
        assert len(d["parts"]) <= 128, (p["name"], len(d["parts"]))
        fn = os.path.join(OUT, p["name"].replace(" ", "") + ".json")
        with open(fn, "w") as f:
            f.write("{\n")
            keys = list(d.keys())
            for i, k in enumerate(keys):
                if k == "parts":
                    f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(q) for q in d[k]) + "\n  ]")
                else:
                    f.write(f'  "{k}": {json.dumps(d[k])}')
                f.write(",\n" if i < len(keys) - 1 else "\n")
            f.write("}\n")
    print(f"wrote {len(PROPS)} props to {OUT}")


if __name__ == "__main__":
    main()
