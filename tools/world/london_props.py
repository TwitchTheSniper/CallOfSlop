#!/usr/bin/env python3
"""Writes the British street and railway props (Props/British/*.json) used by the World Tour's London map and reusable
by any UK map: a black cab, a double-decker bus, a Belisha beacon, a canal narrowboat, a canal lock gate, railway track,
a commuter train carriage, iron railings, a heritage lamp post, a railway signal, a ticket gate and a mooring bollard.

Run from the repository root:  python3 tools/world/london_props.py
Generic designs only: no real liveries, operators or logos.
"""
import json
import math
import os

OUT = os.path.join("Props", "British")


def R(v):
    return round(v, 4)


def part(size, offset, color, material=None, rotation=None, anim=None):
    p = {"size": [R(s) for s in size], "offset": [R(o) for o in offset], "color": color}
    if rotation:
        p["rotation"] = [R(r) for r in rotation]
    if material:
        p["material"] = material
    if anim:
        p["anim"] = anim
    return p


def octagon(d, h, cy, color, x=0.0, z=0.0, material=None, anim=None, axis="y"):
    """Four boxes d x h x 0.414d turned 0/45/90/135: an octagonal prism d across. axis 'y' stands it up; 'x' lays it
    along X (a wheel seen from the side is axis 'z')."""
    out = []
    for a in (0, 45, 90, 135):
        if axis == "y":
            out.append(part((d, h, d * 0.414), (x, cy, z), color, material, (0, a, 0), anim))
        elif axis == "z":  # disc facing +-Z (wheels on a vehicle running along X)
            out.append(part((d, d * 0.414, h), (x, cy, z), color, material, (0, 0, a), anim))
        else:  # axis x
            out.append(part((h, d, d * 0.414), (x, cy, z), color, material, (a, 0, 0), anim))
    return out


def wheel(x, z, r, w, hub="steel"):
    return octagon(2 * r, w, r, "rubber", x, z, axis="z") + [part((r * 1.1, r * 0.46, w + 0.02), (x, r, z), hub)]


def write(d):
    os.makedirs(OUT, exist_ok=True)
    d = {"format": "call-of-slop-prop/1", "category": "British", "author": "Call of Slop", **d}
    assert len(d["parts"]) <= 128, (d["name"], len(d["parts"]))
    fn = d["name"].replace(" ", "").replace("-", "") + ".json"
    with open(os.path.join(OUT, fn), "w") as f:
        json.dump(d, f, indent=1)
        f.write("\n")
    print(f"{fn}: {len(d['parts'])} parts")


# ---------------------------------------------------------------------------------------------------- black cab

def black_cab():
    body, glass, chrome = "#141416", "#26343d", "chrome"
    P = []
    P.append(part((4.5, 0.6, 1.78), (0, 0.62, 0), body, "metal"))
    P.append(part((4.3, 0.12, 1.7), (0, 0.27, 0), "#1c1c1e"))
    # tall rounded cabin (the London cab's upright roof)
    P.append(part((2.5, 0.62, 1.66), (-0.25, 1.23, 0), glass))
    P.append(part((2.6, 0.08, 1.68), (-0.25, 1.58, 0), body, "metal"))
    P.append(part((2.3, 0.06, 1.5), (-0.25, 1.64, 0), body))
    P.append(part((0.06, 0.62, 0.08), (0.98, 1.23, 0.8), body))
    P.append(part((0.06, 0.62, 0.08), (0.98, 1.23, -0.8), body))
    P.append(part((0.06, 0.62, 0.08), (-1.48, 1.23, 0.8), body))
    P.append(part((0.06, 0.62, 0.08), (-1.48, 1.23, -0.8), body))
    P.append(part((0.08, 0.62, 1.67), (-0.25, 1.23, 0), body))  # B pillar
    P.append(part((0.05, 0.56, 1.58), (1.03, 1.2, 0), glass, rotation=(0, 0, 18)))  # windscreen
    # bonnet slope and rounded wings
    P.append(part((1.0, 0.14, 1.7), (1.65, 0.96, 0), body, "metal", (0, 0, -8)))
    for z in (0.82, -0.82):
        P.append(part((1.1, 0.3, 0.2), (1.6, 0.72, z), body, "metal", (0, 0, -6)))
        P.append(part((0.9, 0.3, 0.2), (-1.75, 0.72, z), body, "metal"))
    # grille, bumpers, lights
    P.append(part((0.04, 0.34, 0.66), (2.26, 0.66, 0), chrome))
    for y in (0.56, 0.64, 0.72):
        P.append(part((0.045, 0.02, 0.6), (2.27, y, 0), "#1c1c1e"))
    P.append(part((0.14, 0.14, 1.8), (2.26, 0.34, 0), chrome))
    P.append(part((0.14, 0.14, 1.8), (-2.26, 0.34, 0), chrome))
    for z in (0.62, -0.62):
        P += octagon(0.2, 0.04, 0.78, "#fff2c0", 2.26, z, axis="x")
        P.append(part((0.03, 0.12, 0.18), (-2.255, 0.74, z), "#b01a14"))
    # amber roof sign (blank) and the door handles
    P.append(part((0.5, 0.13, 0.22), (0.55, 1.73, 0), "#e8a018"))
    P.append(part((0.52, 0.03, 0.24), (0.55, 1.67, 0), "#1c1c1e"))
    for z in (0.9, -0.9):
        P.append(part((0.12, 0.03, 0.02), (-0.05, 0.86, z), chrome))
        P.append(part((0.12, 0.03, 0.02), (-0.5, 0.86, z), chrome))
    P.append(part((0.2, 0.1, 0.08), (0.85, 1.05, 0.92), body))  # mirrors
    P.append(part((0.2, 0.1, 0.08), (0.85, 1.05, -0.92), body))
    P.append(part((0.02, 0.1, 0.36), (-2.33, 0.48, 0), "#e8e4d8"))  # number plates (blank)
    P.append(part((0.02, 0.1, 0.36), (2.34, 0.48, 0), "#e8e4d8"))
    for x in (1.45, -1.45):
        for z in (0.82, -0.82):
            P += wheel(x, z, 0.33, 0.22)
    write({
        "name": "Black Cab",
        "description": "A black London-style hackney cab with an upright roof and an amber roof sign (no livery). Unbreakable cover.",
        "collision": "solid",
        "collider": [{"size": [4.62, 0.9, 1.82]}, {"size": [2.6, 0.75, 1.68], "offset": [-0.25, 1.27, 0]}],
        "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 6, "scale": [1, 1]},
        "parts": P,
    })


# ---------------------------------------------------------------------------------------------------- double decker

def double_decker():
    red, glass, cream, dark = "#b81c18", "#1e2a30", "#e8e0c8", "#1c1c1e"
    L, W = 11.0, 2.5
    P = []
    P.append(part((L, 0.9, W), (0, 0.75, 0), red, "metal"))          # lower panels
    P.append(part((L - 0.2, 0.95, W - 0.04), (0, 1.67, 0), glass))    # lower windows
    P.append(part((L, 0.5, W), (0, 2.38, 0), red, "metal"))           # between decks
    P.append(part((L - 0.2, 0.95, W - 0.04), (0, 3.1, 0), glass))     # upper windows
    P.append(part((L, 0.32, W), (0, 3.73, 0), red, "metal"))          # roof band
    P.append(part((L - 0.3, 0.08, W - 0.2), (0, 3.93, 0), "#d0ccc4"))  # roof
    P.append(part((L + 0.02, 0.06, W + 0.02), (0, 2.22, 0), cream))   # cream band
    P.append(part((L, 0.2, W - 0.1), (0, 0.22, 0), dark))             # skirt
    # window pillars, both decks
    for x in (-4.4, -3.0, -1.6, -0.2, 1.2, 2.6, 3.9):
        P.append(part((0.1, 0.95, W + 0.004), (x, 3.1, 0), red))
    for x in (-4.0, -2.4, -0.8, 0.8, 2.6):
        P.append(part((0.1, 0.95, W + 0.004), (x, 1.67, 0), red))
    # front: destination blind (blank amber), windscreen, lights, grille
    P.append(part((0.05, 0.3, 1.9), (L / 2 + 0.01, 3.55, 0), "#1c1c1e"))
    P.append(part((0.04, 0.14, 1.4), (L / 2 + 0.035, 3.55, 0), "#e89a18"))
    P.append(part((0.05, 1.2, 2.3), (L / 2 + 0.01, 1.72, 0), glass))
    P.append(part((0.06, 0.24, 1.6), (L / 2 + 0.02, 0.62, 0), "#3a3a3c"))
    for z in (0.95, -0.95):
        P.append(part((0.04, 0.14, 0.26), (L / 2 + 0.03, 0.72, z), "#fff2c0"))
        P.append(part((0.04, 0.2, 0.18), (-L / 2 - 0.02, 0.8, z), "#b01a14"))
    P.append(part((0.16, 0.26, W + 0.04), (L / 2 + 0.04, 0.38, 0), dark))
    P.append(part((0.16, 0.26, W + 0.04), (-L / 2 - 0.04, 0.38, 0), dark))
    P.append(part((0.04, 0.8, 1.9), (-L / 2 - 0.01, 3.1, 0), glass))  # rear upper window
    P.append(part((0.04, 0.5, 1.4), (-L / 2 - 0.01, 1.6, 0), "#8a1410"))  # engine cover
    # doors on the kerb side (+Z: buses run along +X, kerb on the left in the UK = +Z)
    P.append(part((1.1, 2.0, 0.03), (4.4, 1.2, W / 2 + 0.01), glass))
    P.append(part((0.03, 2.0, 0.035), (4.4, 1.2, W / 2 + 0.015), "lightgrey"))
    P.append(part((1.1, 2.0, 0.03), (-0.2, 1.2, W / 2 + 0.01), glass))
    P.append(part((0.03, 2.0, 0.035), (-0.2, 1.2, W / 2 + 0.015), "lightgrey"))
    P.append(part((0.2, 0.12, 0.08), (5.45, 2.0, 1.3), dark))  # mirrors
    P.append(part((0.2, 0.12, 0.08), (5.45, 2.0, -1.3), dark))
    for x in (3.6, -2.9):
        for z in (1.08, -1.08):
            P += wheel(x, z, 0.5, 0.32)
    write({
        "name": "Double Decker Bus",
        "description": "A red double-decker city bus, 11 m long and 4 m tall, with a blank destination blind. Unbreakable cover.",
        "collision": "solid",
        "collider": [{"size": [11.1, 3.98, 2.54]}],
        "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 4, "scale": [1, 1]},
        "parts": P,
    })


# ---------------------------------------------------------------------------------------------------- belisha beacon

def belisha():
    P = [part((0.3, 0.12, 0.3), (0, 0.06, 0), "#1c1c1e")]
    for i in range(10):  # black and white bands up the pole
        P.append(part((0.09, 0.25, 0.09), (0, 0.12 + 0.125 + i * 0.25, 0), "#f0f0ec" if i % 2 else "#141414"))
    P.append(part((0.12, 0.05, 0.12), (0, 2.65, 0), "#141414"))
    glow = {"type": "pulse", "period": 1.0, "color": "#ffb21e", "off": "#7a4810", "glow": 1.4, "randomPhase": False}
    P += octagon(0.34, 0.26, 2.86, "#e89a18", anim=glow)
    P += octagon(0.24, 0.42, 2.86, "#e89a18", anim=glow)
    P.append(part((0.08, 0.04, 0.08), (0, 3.08, 0), "#141414"))
    write({
        "name": "Belisha Beacon",
        "description": "A zebra crossing beacon: a black and white striped pole with a pulsing amber globe.",
        "collision": "solid",
        "collider": [{"size": [0.3, 0.12, 0.3]}, {"size": [0.1, 2.7, 0.1]}],
        "scatter": {"randomYaw": False, "scale": [1, 1]},
        "parts": P,
    })


# ---------------------------------------------------------------------------------------------------- narrowboat

def narrowboat():
    L = 18.0
    hull, cabin, trim, roof = "#1c1c1e", "#1f4a34", "#a8241c", "#3a3a36"
    P = []
    P.append(part((L - 1.6, 0.7, 2.06), (-0.4, 0.35, 0), hull, "metal"))
    P.append(part((1.2, 0.7, 1.4), (L / 2 - 1.0, 0.35, 0), hull, "metal"))         # bow taper
    P.append(part((0.7, 0.7, 0.7), (L / 2 - 0.15, 0.35, 0), hull, "metal"))
    P.append(part((L - 1.5, 0.06, 2.1), (-0.4, 0.72, 0), trim))                     # gunwale
    P.append(part((12.6, 1.0, 1.82), (-0.9, 1.25, 0), cabin, "metal"))              # cabin
    P.append(part((12.8, 0.06, 1.9), (-0.9, 1.78, 0), roof))
    P.append(part((12.62, 0.12, 1.84), (-0.9, 1.62, 0), trim))                      # coach line
    for x in [-6.2 + i * 1.5 for i in range(9)]:
        if abs(x + 0.9) < 0.6:
            continue
        P.append(part((0.5, 0.36, 1.84), (x, 1.3, 0), "#c8d8d8"))                  # portholes / windows
    P.append(part((0.9, 0.9, 1.86), (-6.0, 1.2, 0), "#e8c860"))                    # roses-and-castles panel
    P.append(part((0.5, 0.5, 1.88), (-6.0, 1.2, 0), trim))
    P.append(part((0.8, 0.5, 0.04), (5.5, 1.1, 0.0), trim))                         # front well bulkhead
    P.append(part((1.6, 0.35, 1.7), (-8.0, 0.88, 0), hull))                         # stern deck
    P.append(part((0.05, 0.9, 0.05), (-8.6, 1.3, 0), "#c8a040"))  # tiller post
    P.append(part((1.1, 0.05, 0.05), (-8.1, 1.75, 0), "#c8a040"))
    P.append(part((0.12, 0.7, 0.12), (-2.5, 2.15, 0), "#1c1c1e"))                   # chimney
    P.append(part((0.2, 0.06, 0.2), (-2.5, 2.52, 0), "#c8a040"))
    P.append(part((1.2, 0.18, 0.5), (1.5, 1.9, 0.4), "#7a5a3a"))                     # roof clutter: logs, planters
    P.append(part((0.8, 0.2, 0.3), (3.5, 1.9, -0.5), "#4a6a2a"))
    P.append(part((0.6, 0.16, 0.16), (-4.0, 1.89, 0.6), "#8a6a44", rotation=(0, 20, 0)))
    for x in (-5.0, 0.0, 5.0):  # fenders
        P.append(part((0.3, 0.3, 0.12), (x, 0.55, 1.08), "#6a5a44"))
        P.append(part((0.3, 0.3, 0.12), (x, 0.55, -1.08), "#6a5a44"))
    write({
        "name": "Canal Narrowboat",
        "description": "An 18 m canal narrowboat: black hull, green cabin with a red coach line, a painted panel, roof clutter and a tiller.",
        "collision": "solid",
        "collider": [{"size": [17.6, 0.75, 2.1], "offset": [0, 0.375, 0]}, {"size": [12.8, 1.1, 1.9], "offset": [-0.9, 1.3, 0]}],
        "scatter": {"randomYaw": False, "yawStep": 180, "scale": [1, 1]},
        "parts": P,
    })


# ---------------------------------------------------------------------------------------------------- lock gate

def lock_gate():
    wood, dark = "#3a2c20", "#241a12"
    P = []
    # the gate leaf stands across the water in -Z (down to 0.5 m below the bank: set the water 0.4-0.5 m down); the balance beam lies on the bank (+Z)
    P.append(part((0.4, 1.6, 3.4), (0, -0.16, -1.8), wood, "wood"))
    for y in (-0.3, 0.2):
        P.append(part((0.44, 0.16, 3.42), (0, y, -1.8), dark))
    P.append(part((0.06, 0.06, 3.4), (0.24, 0.64, -1.8), "#e8e4d8"))  # white-painted top rail
    P.append(part((0.06, 1.0, 0.06), (0.24, 1.1, -0.2), "#e8e4d8"))   # walkway handrail post
    P.append(part((0.06, 1.0, 0.06), (0.24, 1.1, -3.4), "#e8e4d8"))
    P.append(part((0.05, 0.05, 3.3), (0.24, 1.58, -1.8), "#e8e4d8"))
    P.append(part((0.36, 0.36, 4.6), (0, 0.95, 2.0), wood, "wood"))      # balance beam
    P.append(part((0.38, 0.06, 0.8), (0, 1.16, 3.9), "#e8e4d8"))          # white beam end
    P.append(part((0.38, 0.06, 0.8), (0, 1.16, 3.0), "#141414"))
    P.append(part((0.3, 0.77, 0.3), (0, 0.385, 2.6), dark))               # beam rest post
    P.append(part((0.5, 0.06, 0.5), (0, 0.03, 2.6), "#6a6a64"))
    P.append(part((0.24, 0.6, 0.24), (0.0, 1.0, -0.15), "#2a2a2c"))       # paddle gear
    P.append(part((0.3, 0.12, 0.12), (0.0, 1.35, -0.15), "#2a2a2c"))
    write({
        "name": "Canal Lock Gate",
        "description": "A timber canal lock gate: the leaf reaches out over the water (-Z, into water 0.45 m down) and the balance beam lies on the bank (+Z).",
        "collision": "solid",
        "collider": [{"size": [0.4, 1.15, 4.6], "offset": [0, 0.575, 2.0]}],
        "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
        "parts": P,
    })


# ---------------------------------------------------------------------------------------------------- railway

def track():
    P = [part((6.0, 0.14, 2.9), (0, 0.07, 0), "#6e6a64", "dirt")]       # ballast bed
    P.append(part((6.0, 0.06, 2.4), (0, 0.16, 0), "#7c7870", "dirt"))
    for i in range(10):
        P.append(part((0.24, 0.12, 2.5), (-2.7 + i * 0.6, 0.22, 0), "#3a3430", "wood"))
    for z in (0.72, -0.72):
        P.append(part((6.0, 0.08, 0.14), (0, 0.3, z), "#5a5048"))       # rail foot (rusty)
        P.append(part((6.0, 0.08, 0.07), (0, 0.38, z), "#a8a8a4", "metal"))  # polished head
    write({
        "name": "Railway Track",
        "description": "Six metres of standard-gauge track on ballast: sleepers and two rails. Runs along X; line pieces up 6 m apart.",
        "collision": "cosmetic",
        "fadeDistance": 120,
        "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
        "parts": P,
    })


def train_car():
    L, W = 20.0, 2.8
    body, glass, door, roof, under = "#d8dcdc", "#1e2a30", "#e8a020", "#8a8e90", "#2a2c2e"
    P = []
    for x in (-L / 4, L / 4):
        P.append(part((L / 2, 1.0, W), (x, 1.5, 0), body, "metal"))
        P.append(part((L / 2, 0.9, W - 0.04), (x, 2.45, 0), glass))
        P.append(part((L / 2, 0.45, W), (x, 3.12, 0), body, "metal"))
        P.append(part((L / 2 - 0.1, 0.25, W - 0.4), (x, 3.45, 0), roof))
        P.append(part((L / 2, 0.08, W + 0.01), (x, 1.92, 0), "#1e4a7a"))  # plain blue band (no livery)
        P.append(part((L / 2 - 0.4, 0.5, W - 0.5), (x, 0.75, 0), under))
    for x in (-8.0, -3.0, 3.0, 8.0):  # doors, both sides
        for z in (W / 2 + 0.01, -W / 2 - 0.01):
            P.append(part((1.3, 2.0, 0.03), (x, 2.0, z), door))
            P.append(part((0.5, 0.8, 0.035), (x - 0.32, 2.45, z), glass))
            P.append(part((0.5, 0.8, 0.035), (x + 0.32, 2.45, z), glass))
    for x in (-L / 2 - 0.01, L / 2 + 0.01):  # cab ends
        P.append(part((0.04, 1.0, 2.2), (x, 2.5, 0), glass))
        P.append(part((0.06, 0.9, W), (x, 1.5, 0), "#e8b818"))  # yellow warning panel
        for z in (0.9, -0.9):
            P.append(part((0.04, 0.14, 0.24), (x * 1.001, 1.3, z), "#fff2c0"))
    for x in (-7.0, 7.0):  # bogies
        P.append(part((2.6, 0.45, 2.2), (x, 0.5, 0), "#1c1c1e"))
        for dx in (-0.8, 0.8):
            for z in (0.72, -0.72):
                P += octagon(0.8, 0.12, 0.4, "#3a3a3c", x + dx, z, axis="z")
    P.append(part((0.6, 0.35, 0.6), (-5.0, 3.62, 0), "#5a5e60"))  # roof equipment
    P.append(part((0.6, 0.35, 0.6), (5.0, 3.62, 0), "#5a5e60"))
    write({
        "name": "Commuter Train Car",
        "description": "A 20 m suburban electric train carriage in plain white and blue with yellow doors and cab ends. Stands on Railway Track (rails 0.42 m up).",
        "collision": "solid",
        "collider": [{"size": [10.0, 3.6, 2.8], "offset": [-5.0, 1.8, 0]}, {"size": [10.0, 3.6, 2.8], "offset": [5.0, 1.8, 0]}],
        "scatter": {"randomYaw": False, "yawStep": 180, "scale": [1, 1]},
        "parts": P,
    })


def signal():
    P = [part((0.5, 0.15, 0.5), (0, 0.075, 0), "#6a6a64", "concrete")]
    P.append(part((0.14, 3.6, 0.14), (0, 1.95, 0), "#2a2a2c", "metal"))
    P.append(part((0.36, 0.9, 0.2), (0, 3.6, 0.06), "#141414"))
    P.append(part((0.6, 0.66, 0.03), (0, 3.6, -0.06), "#141414"))       # backing board
    P.append(part((0.16, 0.16, 0.03), (0, 3.85, 0.17), "#ff3020", anim=None))
    P.append(part((0.16, 0.16, 0.03), (0, 3.6, 0.17), "#3a3010"))
    P.append(part((0.16, 0.16, 0.03), (0, 3.35, 0.17), "#103a18"))
    P.append(part((0.3, 0.04, 0.12), (0, 3.92, 0.24), "#141414"))       # hood
    P.append(part((0.4, 0.3, 0.25), (0, 0.4, 0.2), "#4a4e50"))          # trackside box
    P.append(part((0.04, 1.4, 0.04), (0.15, 1.2, -0.1), "#e8b818"))      # ladder rail
    write({
        "name": "Railway Signal",
        "description": "A trackside colour-light railway signal on a post, showing red. Faces +Z.",
        "collision": "solid",
        "collider": [{"size": [0.5, 0.15, 0.5]}, {"size": [0.16, 4.0, 0.16]}],
        "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
        "parts": P,
    })


# ---------------------------------------------------------------------------------------------------- street furniture

def railings():
    iron = "#141414"
    P = [part((2.5, 0.05, 0.05), (0, 0.12, 0), iron), part((2.5, 0.05, 0.05), (0, 1.0, 0), iron)]
    for i in range(12):
        x = -1.14 + i * 0.208
        P.append(part((0.025, 1.08, 0.025), (x, 0.6, 0), iron))
        P.append(part((0.05, 0.08, 0.05), (x, 1.17, 0), "#7a6a2a", rotation=(0, 45, 0)))  # gilded spear tips
    for x in (-1.25, 1.25):
        P.append(part((0.08, 1.24, 0.08), (x, 0.62, 0), iron))
        P.append(part((0.11, 0.06, 0.11), (x, 1.27, 0), iron))
    write({
        "name": "Iron Railings",
        "description": "A 2.5 m panel of black cast-iron park railings with spear tips (runs along X).",
        "collision": "solid",
        "collider": [{"size": [2.5, 1.2, 0.1]}],
        "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
        "parts": P,
    })


def heritage_lamp():
    iron = "#161618"
    P = octagon(0.42, 0.5, 0.25, iron) + octagon(0.26, 0.5, 0.75, iron)
    P.append(part((0.13, 2.8, 0.13), (0, 2.4, 0), iron, "metal"))
    P.append(part((0.5, 0.06, 0.06), (0, 3.6, 0), iron))  # ladder bar
    P.append(part((0.22, 0.12, 0.22), (0, 3.86, 0), iron))
    P.append(part((0.36, 0.5, 0.36), (0, 4.17, 0), "#f4e0a0", anim=None))
    for a in (0.17, -0.17):
        P.append(part((0.03, 0.52, 0.38), (a, 4.17, 0), iron))
        P.append(part((0.38, 0.52, 0.03), (0, 4.17, a), iron))
    P.append(part((0.46, 0.08, 0.46), (0, 4.46, 0), iron))
    P.append(part((0.24, 0.12, 0.24), (0, 4.56, 0), iron))
    P.append(part((0.06, 0.1, 0.06), (0, 4.66, 0), "#7a6a2a"))
    write({
        "name": "Heritage Lamp Post",
        "description": "A black Victorian cast-iron lamp column with a square lantern on top.",
        "collision": "solid",
        "collider": [{"size": [0.42, 1.0, 0.42]}, {"size": [0.14, 4.6, 0.14]}],
        "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
        "parts": P,
    })


def ticket_gate():
    P = []
    for z in (0.0,):
        P.append(part((1.3, 0.95, 0.26), (0, 0.475, z), "#c8ccd0", "metal"))
        P.append(part((1.32, 0.05, 0.3), (0, 0.98, z), "#2a2c2e"))
        P.append(part((0.18, 0.04, 0.16), (0.45, 1.02, z), "#3a6ac8"))  # card reader
        P.append(part((0.06, 0.12, 0.2), (-0.6, 0.9, z), "#30d060"))     # status light
    P.append(part((0.02, 0.6, 0.36), (0.1, 0.75, 0.31), "#9ac0d0"))       # glass paddle
    write({
        "name": "Ticket Gate",
        "description": "A station ticket barrier cabinet with a card reader and a glass paddle on its +Z side. Line several up 1.0 m apart.",
        "collision": "solid",
        "collider": [{"size": [1.3, 1.0, 0.3]}],
        "breakable": {"health": 300, "breakBy": ["explosions", "streaks"], "sound": "metal", "debris": {"count": 10, "size": [0.06, 0.2]}},
        "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
        "parts": P,
    })


def mooring_bollard():
    iron = "#1a1a1c"
    P = octagon(0.3, 0.32, 0.16, iron) + octagon(0.4, 0.08, 0.36, iron)
    P.append(part((0.5, 0.05, 0.5), (0, 0.025, 0), "#7a7670", "concrete"))
    write({
        "name": "Mooring Ring Bollard",
        "description": "A short black cast-iron canal-side mooring bollard on a stone pad. Low enough to step over.",
        "collision": "solid",
        "collider": [{"size": [0.4, 0.4, 0.4]}],
        "scatter": {"randomYaw": False, "yawStep": 90, "scale": [1, 1]},
        "parts": P,
    })


if __name__ == "__main__":
    for f in (black_cab, double_decker, belisha, narrowboat, lock_gate, track, train_car, signal, railings,
              heritage_lamp, ticket_gate, mooring_bollard):
        f()
