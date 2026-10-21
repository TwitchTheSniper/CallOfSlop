#!/usr/bin/env python3
"""Writes the Cluttered pack's harbour props (Maps/Cluttered/Props/Harbour/*.json): boats, mooring bollards, fish
market and quayside bits that no global prop covers. They're pack-local (Props/README.md, "Pack props"): only the
Cluttered pack's maps see them. Dockside (tools/cluttered/dockside.py) uses them.

Usage: python3 tools/cluttered/harbour_props.py <repo root>

Boats float: their origin is on the waterline (place them with the Prop op's "anchor": "world" and "y" = the water
level), the hull reaches 0.45 m below it, and the whole boat bobs and rocks on the GPU (a tiny keel part bobs, a second
one rolls on it and every visible part follows the roll). They're cosmetic: they sit on the water outside the playable
outline, where nobody can reach them, and fade out 260 m away.
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/Cluttered/Props/Harbour")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def octagon(d, h, y, color, x=0.0, z=0.0, **kw):
    """Four boxes turned 0/45/90/135 degrees: an octagon d across, h tall, centred at height y."""
    return [part((d, h, d * 0.414), (x, y, z), color, rotation=[0, a, 0], **kw) for a in (0, 45, 90, 135)]


def floating(parts, bob=0.07, period=3.6, roll=3.0, roll_period=4.7, axis="x"):
    """Makes a list of parts float: part 1 bobs, part 2 rolls on it (about the boat's long axis), the rest follow part 2."""
    head = [
        part((0.3, 0.05, 0.3), (0, -0.4, 0), "darkgrey", anim={"type": "bob", "distance": bob, "period": period, "jitter": 0.2, "freezeDistance": 120}),
        part((0.2, 0.05, 0.2), (0, -0.35, 0), "darkgrey", anim={"type": "swing", "axis": axis, "pivot": [0, 0, 0], "angle": roll, "period": roll_period,
                                                             "wind": 0.6, "jitter": 0.25, "parent": 1}),
    ]
    for p in parts:
        p.setdefault("anim", {"type": "follow", "parent": 2})
    return head + parts


def hull(length, width, depth, color, stripe, deck="lightwood", bow=True, free=0.45):
    """A boat hull along +X (bow at +X): a box, a diamond bow, a gunwale stripe and a deck. Waterline at y = 0."""
    hb = depth - free  # height above the waterline
    body = length - (width * 0.5 if bow else 0)
    cx = -length / 2 + body / 2
    ps = [part((body, depth, width), (cx, -free + depth / 2, 0), color, material="wood")]
    if bow:
        s = width / 1.414
        ps.append(part((s, depth * 0.92, s), (cx + body / 2, -free + depth * 0.46 + 0.02, 0), color, rotation=[0, 45, 0], material="wood"))
    ps.append(part((body + 0.02, 0.1, width + 0.04), (cx, hb - 0.08, 0), stripe))
    ps.append(part((body - 0.3, 0.04, width - 0.2), (cx, hb - 0.1, 0), deck, material="wood"))
    ps.append(part((body * 0.98, 0.06, width + 0.03), (cx, 0.05, 0), "#c8c8c0"))  # boot-top line at the waterline
    return ps


PROPS = []

# ---------------------------------------------------------------- boats (cosmetic, floating)
fb = hull(8.6, 2.9, 1.65, "#2a4a6e", "white")
fb += [
    part((2.2, 1.7, 2.1), (-1.4, 2.05, 0), "white"),                      # wheelhouse
    part((2.4, 0.1, 2.3), (-1.4, 2.95, 0), "#2a4a6e"),
    part((0.04, 0.55, 1.7), (-0.29, 2.3, 0), "glass"),                    # front windows
    part((1.6, 0.55, 0.04), (-1.4, 2.3, 1.06), "glass"),
    part((1.6, 0.55, 0.04), (-1.4, 2.3, -1.06), "glass"),
    part((0.12, 4.2, 0.12), (1.3, 3.1, 0), "white"),                      # mast
    part((0.08, 0.08, 2.6), (1.3, 4.6, 0), "white"),                      # cross-tree
    part((3.2, 0.08, 0.08), (2.5, 2.6, 0), "darkgrey", rotation=[0, 0, 28]),  # derrick boom
    part((1.6, 0.35, 1.6), (2.4, 1.38, 0), "#2f5a34"),                    # net heap
    part((0.6, 0.3, 0.4), (0.4, 1.35, 0.8), "orange"),                     # fish boxes
    part((0.6, 0.3, 0.4), (0.4, 1.35, -0.8), "#3a7ad0"),
    part((0.6, 0.3, 0.4), (-3.2, 1.35, 0.6), "orange"),
    part((0.5, 0.5, 0.06), (-2.55, 2.0, 1.07), "orange"),                  # life ring on the wheelhouse
    part((0.3, 0.6, 0.3), (-2.0, 3.3, 0.6), "darkgrey"),                  # exhaust stack
    part((0.14, 0.14, 0.14), (1.3, 5.27, 0), "white", anim={"type": "blink", "period": 3, "duty": 0.2, "color": "#fff8d0", "off": "#808070", "glow": 1.4, "parent": 2}),
    part((0.4, 0.06, 0.06), (-4.3, 1.4, 0), "darkgrey"),                  # stern rail
]
PROPS.append({"name": "Fishing Trawler", "description": "A blue fishing boat with a wheelhouse, mast and nets, bobbing at its mooring.",
              "parts": floating(fb, bob=0.06, period=4.2, roll=2.5, roll_period=5.3)})

ml = hull(5.6, 2.2, 1.2, "white", "#c03028", deck="lightwood")
ml += [
    part((1.6, 1.0, 1.7), (-0.3, 1.25, 0), "white"),
    part((1.7, 0.08, 1.8), (-0.3, 1.79, 0), "#c03028"),
    part((0.04, 0.45, 1.4), (0.52, 1.4, 0), "glass"),
    part((1.2, 0.4, 0.04), (-0.3, 1.4, 0.86), "glass"),
    part((1.2, 0.4, 0.04), (-0.3, 1.4, -0.86), "glass"),
    part((0.08, 1.4, 0.08), (-0.6, 2.5, 0), "darkgrey"),                  # aerial
    part((0.9, 0.3, 0.9), (-2.2, 0.9, 0), "darkgrey"),                    # outboard housing
    part((0.5, 0.25, 0.35), (1.6, 0.9, 0), "orange"),
]
PROPS.append({"name": "Harbour Launch", "description": "A white motor launch with a red stripe and a little cabin, bobbing at its mooring.",
              "parts": floating(ml, bob=0.08, period=3.4, roll=3.5, roll_period=4.1)})

rb = hull(3.3, 1.25, 0.62, "#a8442c", "white", deck="wood", free=0.3)
rb += [
    part((0.22, 0.05, 1.1), (0.5, 0.26, 0), "lightwood"),                 # thwarts
    part((0.22, 0.05, 1.1), (-0.7, 0.26, 0), "lightwood"),
    part((2.2, 0.04, 0.06), (-0.1, 0.36, 0.46), "lightwood", rotation=[0, 12, 0]),  # oars
    part((2.2, 0.04, 0.06), (-0.1, 0.36, -0.46), "lightwood", rotation=[0, -12, 0]),
    part((0.4, 0.2, 0.06), (-1.6, 0.25, 0), "darkwood"),                  # transom
]
PROPS.append({"name": "Rowing Boat", "description": "A small wooden rowing boat with its oars shipped, rocking on the water.",
              "parts": floating(rb, bob=0.05, period=2.8, roll=5, roll_period=3.3)})

# ---------------------------------------------------------------- quayside (solid)
mb = octagon(0.42, 0.12, 0.06, "#2b2b2e", material="metal") + octagon(0.3, 0.32, 0.28, "#2b2b2e", material="metal") \
    + octagon(0.46, 0.1, 0.49, "#2b2b2e", material="metal") + [part((0.06, 0.03, 0.06), (0, 0.555, 0), "#4a4a4e")]
PROPS.append({"name": "Mooring Bollard", "description": "A squat cast-iron mooring bollard on the quay edge. You can step onto it.",
              "collision": "solid", "collider": [{"size": [0.44, 0.55, 0.44]}], "scatter": {"randomYaw": False, "yawStep": 45},
              "parts": mb})

lr = [part((0.1, 1.5, 0.1), (0, 0.75, 0), "#c8b040"), part((0.5, 0.06, 0.14), (0, 1.45, 0), "#c8b040"),
      part((0.6, 0.12, 0.06), (0, 1.15, 0.08), "orange"), part((0.6, 0.12, 0.06), (0, 0.67, 0.08), "orange"),
      part((0.12, 0.6, 0.06), (-0.24, 0.91, 0.08), "orange"), part((0.12, 0.6, 0.06), (0.24, 0.91, 0.08), "orange"),
      part((0.1, 0.1, 0.07), (0, 1.15, 0.09), "white"), part((0.1, 0.1, 0.07), (0, 0.67, 0.09), "white"),
      part((0.34, 0.06, 0.34), (0, 0.03, 0), "darkgrey")]
PROPS.append({"name": "Life Ring Post", "description": "A yellow post with an orange life ring on the quay (the ring faces +Z, towards the water).",
              "collision": "solid", "collider": [{"size": [0.14, 1.5, 0.14]}], "scatter": {"randomYaw": False, "yawStep": 90},
              "parts": lr})

ud = [part((2.8, 0.42, 1.15), (0, 0.72, 0), "#3c7a5a", material="wood"), part((0.8, 0.38, 0.8), (1.4, 0.71, 0), "#3c7a5a", rotation=[0, 45, 0], material="wood"),
      part((2.8, 0.06, 1.2), (0, 0.5, 0), "white"), part((2.6, 0.05, 0.12), (0, 0.95, 0), "#2a5a40"),
      part((0.12, 0.5, 1.3), (-0.8, 0.25, 0), "darkwood"), part((0.12, 0.5, 1.3), (0.8, 0.25, 0), "darkwood"),
      part((1.9, 0.12, 0.12), (0, 0.4, 0.55), "darkwood"), part((1.9, 0.12, 0.12), (0, 0.4, -0.55), "darkwood")]
PROPS.append({"name": "Upturned Dinghy", "description": "A green dinghy lying upside down on two trestles for painting. Waist-high cover.",
              "collision": "solid", "collider": [{"size": [3.4, 0.95, 1.3]}], "scatter": {"randomYaw": True},
              "breakable": {"health": 120, "breakBy": "all", "sound": "wood", "debris": {"count": 14, "size": [0.08, 0.4]}},
              "parts": ud})

lp = []
for i, (x, y, z) in enumerate([(-0.4, 0.22, 0), (0.4, 0.22, 0), (0.0, 0.66, 0)]):
    lp += [part((0.72, 0.42, 0.52), (x, y, z), "#6a5a3a", material="wood"), part((0.74, 0.05, 0.54), (x, y + 0.2, z), "#3a3020"),
           part((0.6, 0.3, 0.02), (x, y, z + 0.27), "#2f3a30")]
lp += [part((0.18, 0.18, 0.18), (0.4, 0.52, 0.18), "orange"), part((0.14, 0.14, 0.14), (-0.45, 0.5, -0.2), "red")]
PROPS.append({"name": "Lobster Pot Stack", "description": "Three wooden lobster pots stacked on the quay with their marker floats.",
              "collision": "solid", "collider": [{"size": [1.55, 0.88, 0.55]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 8},
              "breakable": {"health": 45, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.05, 0.3]}},
              "parts": lp})

fc = [part((2.4, 0.85, 0.95), (0, 0.425, 0), "steel", material="metal"), part((2.44, 0.06, 1.0), (0, 0.88, 0), "chrome"),
      part((2.3, 0.08, 0.85), (0, 0.95, 0), "#dfe8ee")]  # ice bed
fish_cols = ["#9aa6ae", "#c0c8cc", "#d88a6a", "#8a9aa0", "#b4b0a0"]
for i in range(9):
    x = -1.0 + (i % 5) * 0.5
    z = -0.22 if i < 5 else 0.22
    if i >= 5:
        x += 0.25
    fc.append(part((0.4, 0.06, 0.12), (x, 1.02, z), fish_cols[i % len(fish_cols)], rotation=[0, 15 if i % 2 else -15, 0]))
fc += [part((0.05, 0.9, 0.05), (-1.15, 1.4, 0.45), "steel"), part((0.05, 0.9, 0.05), (1.15, 1.4, 0.45), "steel"),
       part((2.35, 0.3, 0.04), (0, 1.8, 0.45), "white"), part((0.9, 0.12, 0.045), (0, 1.8, 0.45), "#2a5a9a"),
       part((0.3, 0.2, 0.02), (0.7, 1.1, -0.45), "white")]
PROPS.append({"name": "Fish Counter", "description": "A steel fish market counter with an ice bed full of fish and a sign board (the customer side is +Z).",
              "collision": "solid", "collider": [{"size": [2.4, 1.0, 0.95]}], "scatter": {"randomYaw": False, "yawStep": 90},
              "parts": fc})

fbx = [part((0.6, 0.26, 0.4), (0, 0.13, 0), "#3a7ad0"), part((0.54, 0.04, 0.34), (0, 0.25, 0), "#dfe8ee"),
       part((0.35, 0.05, 0.1), (-0.08, 0.28, -0.06), "#9aa6ae", rotation=[0, 10, 0]), part((0.32, 0.05, 0.1), (0.1, 0.28, 0.08), "#c0c8cc", rotation=[0, -12, 0])]
PROPS.append({"name": "Fish Box", "description": "A blue plastic fish box full of ice and fish.",
              "collision": "cosmetic", "scatter": {"randomYaw": True, "scale": [0.9, 1.1]},
              "breakable": {"health": 10, "breakBy": "all", "sound": "soft", "debris": {"count": 8, "size": [0.04, 0.14]}},
              "parts": fbx})

fs = []
cols = ["#3a7ad0", "orange", "#e0d040", "#3a7ad0", "orange"]
for k, (x, z, y) in enumerate([(-0.32, 0, 0.13), (0.32, 0, 0.13), (0, 0, 0.39), (-0.32, 0, 0.65), (0.3, 0.02, 0.39)]):
    fs.append(part((0.6, 0.26, 0.42), (x, y, z), cols[k]))
fs.append(part((0.5, 0.04, 0.34), (-0.32, 0.79, 0), "#dfe8ee"))
PROPS.append({"name": "Fish Box Stack", "description": "Plastic fish boxes stacked on the quay. Knee-high cover that breaks easily.",
              "collision": "solid", "collider": [{"size": [1.25, 0.8, 0.44]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 10},
              "breakable": {"health": 25, "breakBy": "all", "sound": "soft", "debris": {"count": 12, "size": [0.05, 0.25]}},
              "parts": fs})

np_ = [part((1.6, 0.22, 1.1), (0, 0.11, 0), "#2f5a34"), part((1.1, 0.18, 0.8), (0.1, 0.3, 0.05), "#365f3a"),
       part((0.6, 0.12, 0.5), (-0.1, 0.44, -0.05), "#2a5030"), part((0.14, 0.14, 0.14), (0.6, 0.25, 0.4), "orange"),
       part((0.14, 0.14, 0.14), (-0.6, 0.25, -0.35), "orange"), part((0.14, 0.14, 0.14), (0.2, 0.42, -0.3), "#e0d040")]
PROPS.append({"name": "Fishing Net Pile", "description": "A heap of green fishing net with floats in it.",
              "collision": "cosmetic", "scatter": {"randomYaw": True}, "parts": np_})

rc = octagon(0.7, 0.08, 0.04, "#c8a860") + octagon(0.5, 0.08, 0.12, "#b89850") + octagon(0.3, 0.07, 0.19, "#c8a860") \
    + [part((0.9, 0.04, 0.05), (0.6, 0.02, 0.1), "#c8a860", rotation=[0, 20, 0])]
PROPS.append({"name": "Rope Coil", "description": "A coil of mooring rope on the quay.",
              "collision": "cosmetic", "scatter": {"randomYaw": True}, "parts": rc})

# ---------------------------------------------------------------- write
os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT):
    if f.endswith(".json"):
        os.remove(os.path.join(OUT, f))
for p in PROPS:
    d = {"format": "call-of-slop-prop/1", "name": p["name"], "category": "Harbour", "description": p["description"],
         "author": "Call of Slop"}
    if "collision" in p:
        d["collision"] = p["collision"]
    else:
        d["collision"] = "cosmetic"
        d["fadeDistance"] = 260
    for k in ("collider", "scatter", "breakable"):
        if k in p:
            d[k] = p[k]
    d["parts"] = p["parts"]
    assert len(d["parts"]) <= 128, (p["name"], len(d["parts"]))
    path = os.path.join(OUT, p["name"].replace(" ", "") + ".json")
    with open(path, "w") as f:
        f.write("{\n")
        keys = list(d.keys())
        for k in keys:
            if k == "parts":
                f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(q) for q in d["parts"]) + "\n  ]\n")
            else:
                f.write(f'  "{k}": {json.dumps(d[k])},\n')
        f.write("}\n")
print(f"{len(PROPS)} harbour props -> {OUT}")
