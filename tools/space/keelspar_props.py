#!/usr/bin/env python3
"""Writes the reusable Space prop set: Props/Space/Keelspar and Props/Space/Regolith (category "Space"), for orbital yards and off-world outposts: hull
plate stacks and racks, keel blocks, welding rigs, a welder bot, parts bin racks, rivet crates, work-light masts, hazard
bollards, a yard tug; a lunar lander, a rover, solar array racks, a core drill rig, ore conveyor sections, a regolith
hopper, an oxygen tank bank, a hab airlock module, sample crates, survey lights and lunar boulders. Original designs
only (no film or game ships, names or insignia).

    python3 tools/space/keelspar_props.py            (run from the repository root)

Uses the box helpers of tools/propviz/gen.py. Health follows the material health guide in Props/README.md: big metal
structures are unbreakable cover; small cases and crates are heavy metal; the oxygen bank and the welding rig explode
with a short fuse.
"""
import json
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "propviz"))
import gen  # noqa: E402
from gen import B, At, Cyl, HCyl, C, brk  # noqa: E402

PROPS = gen.PROPS
CAT = "Space"
SUBFOLDERS = {"yard": "Keelspar", "outpost": "Regolith"}  # Props/Space/<subfolder>/: the category is still "Space"
AUTHOR = "Call of Slop"

HULL = "#cdbfa8"
HULL2 = "#b3a48c"
BRONZE = "#8d7458"
STEEL = "#6c6f73"
DARK = "#34363a"
AMBER = "#ffb24a"
TEAL = "#6fe0e8"
RED = "#ff4a3a"
WHITE = "#e6e4de"
PALE = "#c9c7c0"
ORANGE = "#e8701e"
HAZARD = "#f0c040"
PANEL = "#1e2a4a"
GLASS = "#2e4450"

SQ = {"randomYaw": False, "yawStep": 90, "yawJitter": 3}
ANY = {"randomYaw": True, "scale": [0.95, 1.05]}


CURRENT = {"sub": "Keelspar"}


def prop(name, desc, parts, **kw):
    gen.prop(name, CAT, desc, parts, **kw)
    PROPS[-1]["author"] = AUTHOR
    PROPS[-1]["_sub"] = CURRENT["sub"]


def glow(kind="pulse", color=AMBER, period=2.4, glow_=1.3, **kw):
    a = {"type": kind, "color": color, "glow": glow_}
    if kind in ("pulse", "blink", "cycle"):
        a["period"] = period
    a.update(kw)
    return a


def lit(part, anim):
    part = dict(part)
    part["anim"] = anim
    return part


# ------------------------------------------------------------------ yard props
def yard():
    # Hull plate stack: curved plates lie flat, each a little offset.
    p = [At([3.0, 0.14, 2.0], 0, color=DARK)]
    for k in range(5):
        p.append(At([2.9 - k * 0.05, 0.12, 1.9], 0.14 + k * 0.15, (k % 2) * 0.08 - 0.04, 0, color=HULL if k % 2 else HULL2, mat="metal"))
    p.append(At([0.12, 0.9, 2.1], 0, -1.5, color=BRONZE))
    p.append(At([0.12, 0.9, 2.1], 0, 1.5, color=BRONZE))
    prop("Hull Plate Stack", "Five cream hull plates stacked on a skid between two end posts: waist-high cover (long along X).", p,
         collider=[C([3.1, 0.95, 2.1])], scatter=SQ)
    # Hull plate rack: an A-frame with plates leaning on both sides.
    p = [At([3.0, 0.12, 1.6], 0, color=DARK)]
    for x in (-1.3, 1.3):
        p.append(B([0.1, 2.4, 0.1], [x, 1.2, 0.3], color=STEEL, pitch=-18))
        p.append(B([0.1, 2.4, 0.1], [x, 1.2, -0.3], color=STEEL, pitch=18))
    p.append(At([2.8, 0.1, 0.1], 2.26, color=STEEL))
    for k, z in enumerate((0.42, 0.56, -0.42, -0.56)):
        p.append(B([2.6, 2.0, 0.06], [0.1 * (k % 2), 1.05, z], color=HULL if k % 2 else HULL2, mat="metal", pitch=-18 if z > 0 else 18))
    prop("Hull Plate Rack", "An A-frame rack with hull plates leaning on both sides: head-high cover (long along X).", p,
         collider=[C([3.0, 2.3, 1.5])], scatter=SQ)
    # Keel block: the cradle block a hull sits on.
    p = [At([2.0, 0.5, 1.6], 0, color=DARK, mat="metal"), At([1.6, 0.5, 1.2], 0.5, color=STEEL, mat="metal"),
         At([1.8, 0.3, 0.6], 1.0, color=HULL2), At([0.3, 0.1, 1.7], 0.5, 0.75, color=HAZARD), At([0.3, 0.1, 1.7], 0.5, -0.75, color=HAZARD)]
    prop("Keel Block", "A stepped steel cradle block with hazard-painted edges: chest-high cover.", p, collider=[C([2.0, 1.3, 1.6])], scatter=SQ)
    # Welding rig: a trolley with two gas bottles, a hose reel and a torch arm whose tip flickers blue-white.
    p = [At([1.2, 0.12, 0.8], 0.1, color=DARK), At([0.5, 0.6, 0.6], 0.22, -0.3, color=STEEL, mat="metal")]
    for z in (-0.18, 0.18):
        p.append(At([0.26, 0.26, 0.26], 0, -0.4, z, color=DARK))
        p.append(At([0.26, 0.26, 0.26], 0, 0.4, z, color=DARK))
    p += Cyl(0.3, 1.3, 0.22, 0.3, -0.2, color="#d86a2a")
    p += Cyl(0.3, 1.3, 0.22, 0.3, 0.2, color="#4a7ab0")
    p.append(At([0.1, 0.1, 0.1], 1.52, 0.3, -0.2, color=STEEL))
    p.append(At([0.1, 0.1, 0.1], 1.52, 0.3, 0.2, color=STEEL))
    p.append(At([0.06, 1.0, 0.06], 0.82, -0.3, 0.1, color=STEEL))
    p.append(B([0.7, 0.05, 0.05], [0.05, 1.82, 0.1], color=STEEL))
    p.append(B([0.06, 0.3, 0.06], [0.4, 1.68, 0.1], color=DARK))
    p.append(lit(At([0.14, 0.14, 0.14], 1.46, 0.4, 0.1, color="#cfe8ff"), glow("flicker", "#cfe8ff", rate=14, duty=0.6, glow_=2.4, off="#405060")))
    prop("Welding Rig", "A welding trolley: two gas bottles, a reel and a torch arm whose tip flickers blue-white. Bursts after a short fuse.", p,
         collider=[C([1.2, 1.6, 0.8])], scatter=ANY,
         breakable=brk(110, "metal", count=12, explode={"radius": 4.0, "damage": 90, "fuse": 0.5, "knockback": 10}))
    # Welder bot: a squat four-legged robot with a swinging arm.
    p = [At([1.0, 0.5, 1.0], 0.3, color=HULL2, mat="metal"), At([0.6, 0.2, 0.6], 0.8, color=BRONZE)]
    for x in (-0.5, 0.5):
        for z in (-0.5, 0.5):
            p.append(At([0.12, 0.4, 0.12], 0, x, z, color=DARK))
            p.append(At([0.3, 0.06, 0.3], 0, x, z, color=STEEL))
    first = len(p) + 1
    p.append(lit(At([0.2, 0.2, 0.2], 1.0, color=DARK), {"type": "swing", "axis": "y", "pivot": [0, 1.1, 0], "angle": 40, "period": 6}))
    p.append(lit(B([0.08, 0.08, 0.9], [0, 1.1, 0.5], color=STEEL), {"type": "follow", "parent": first}))
    p.append(lit(B([0.14, 0.14, 0.14], [0, 1.1, 1.0], color="#cfe8ff"), glow("flicker", "#cfe8ff", rate=11, duty=0.5, glow_=2.2, off="#405060")))
    for z in (-0.4, 0.4):
        p.append(lit(At([0.1, 0.05, 0.03], 0.62, 0.3, z * 1.26, color=TEAL), glow("blink", TEAL, 1.2, duty=0.5, glow_=1.0)))
    prop("Welder Bot", "A squat four-legged yard robot with a sweeping welding arm: knee-high cover.", p,
         collider=[C([1.0, 0.85, 1.0])], scatter=ANY)
    # Parts bin rack: shelving with coloured bins.
    p = [At([2.4, 0.05, 0.8], 0, color=DARK)]
    for x in (-1.15, 1.15):
        p.append(At([0.06, 2.0, 0.8], 0, x, color=STEEL))
    for y in (0.05, 0.7, 1.35):
        p.append(At([2.3, 0.04, 0.78], y, color=STEEL))
    cols = [ORANGE, "#4a7ab0", HAZARD, TEAL, "#8a9aa8"]
    for row, y in enumerate((0.09, 0.74, 1.39)):
        for k in range(4):
            p.append(At([0.5, 0.36, 0.6], y, -0.85 + k * 0.56, 0.05, color=cols[(k + row) % 5]))
    p.append(At([2.4, 0.05, 0.8], 2.0, color=STEEL))
    prop("Parts Bin Rack", "Shelving with three rows of coloured parts bins: head-high cover (long along X).", p,
         collider=[C([2.4, 2.05, 0.8])], scatter=SQ, breakable=brk(260, "metal", count=14, size=(0.08, 0.3)))
    # Rivet crate: a small ribbed metal crate.
    p = [At([0.8, 0.6, 0.6], 0, color=STEEL, mat="metal"), At([0.84, 0.05, 0.64], 0.57, color=BRONZE),
         At([0.84, 0.05, 0.64], 0.02, color=BRONZE), At([0.3, 0.1, 0.03], 0.3, 0, 0.31, color=HAZARD)]
    prop("Rivet Crate", "A small ribbed steel crate with a hazard label: stackable, breaks.", p,
         breakable=brk(180, "metal", count=10, size=(0.06, 0.2)), scatter=ANY)
    # Work light mast: a tripod with a bank of warm lamps.
    p = [At([0.8, 0.08, 0.8], 0, color=DARK)]
    for a in (0, 120, 240):
        p.append(B([0.08, 1.4, 0.08], [0, 0.7, 0.3], color=STEEL, yaw=a, pitch=14))
    p.append(At([0.12, 3.0, 0.12], 0.5, color=STEEL))
    p.append(At([0.9, 0.3, 0.2], 3.4, color=DARK))
    for x in (-0.3, 0, 0.3):
        p.append(lit(At([0.22, 0.22, 0.04], 3.44, x, 0.11, color="#fff0c8"), glow("flicker", "#fff0c8", rate=2, duty=0.97, glow_=1.8, off="#b8a888")))
    prop("Work Light Mast", "A tripod floodlight mast with three warm lamps facing +Z.", p, collider=[C([0.3, 3.7, 0.3])], scatter=ANY)
    # Hazard bollard: a short post with a blinking amber top.
    p = [At([0.3, 0.1, 0.3], 0, color=DARK), At([0.16, 0.8, 0.16], 0.1, color=HAZARD), At([0.16, 0.12, 0.16], 0.45, color=DARK),
         lit(At([0.2, 0.12, 0.2], 0.9, color=AMBER), glow("blink", AMBER, 1.5, duty=0.4, glow_=1.6))]
    prop("Hazard Bollard", "A short yellow bollard with a blinking amber lamp: marks drops and gantry runs.", p,
         collider=[C([0.3, 1.02, 0.3])], scatter=ANY)
    # Yard tug: a parked orbital tug, a boxy cab on a frame with two thruster pods and a grapple.
    p = [At([5.6, 0.5, 2.2], 0.5, color=STEEL, mat="metal"), At([2.2, 1.4, 2.0], 1.0, 1.4, color=HULL, mat="metal"),
         B([1.0, 0.9, 1.6], [2.6, 1.7, 0], color=GLASS, roll=-30), At([2.4, 0.5, 2.0], 2.4, 1.4, color=HULL2),
         At([2.8, 0.8, 1.6], 1.0, -1.2, color=HULL2, mat="metal"), At([5.7, 0.12, 2.3], 0.95, color=BRONZE)]
    for x in (-2.0, 2.0):
        for z in (-0.8, 0.8):
            p.append(At([0.2, 0.5, 0.2], 0, x, z, color=DARK))
    for z in (-1.0, 1.0):
        p += HCyl(0.8, 1.6, 1.4, x=-2.2, z=z * 1.45, color=DARK)
        p.append(lit(B([0.06, 0.5, 0.5], [-3.02, 1.4, z * 1.45], color=AMBER), glow("pulse", AMBER, 3.0, glow_=1.2)))
    p.append(B([0.1, 0.1, 1.6], [3.0, 1.0, 0], color=STEEL))
    p.append(B([0.6, 0.12, 0.12], [3.3, 0.7, 0.6], color=STEEL, roll=40))
    p.append(B([0.6, 0.12, 0.12], [3.3, 0.7, -0.6], color=STEEL, roll=40))
    p.append(lit(At([0.12, 0.12, 0.12], 2.9, 1.4, 0, color=RED), glow("blink", RED, 1.6, duty=0.2, glow_=2.0)))
    prop("Yard Tug", "A parked orbital yard tug (an original design): a boxy cab on a frame with two thruster pods and a grapple. Long along X.", p,
         collider=[C([5.6, 2.9, 2.2], [0, 1.45, 0])], scatter=SQ)
    # Orbital tool chest: a rolling cabinet, low cover.
    p = [At([1.2, 0.9, 0.6], 0.1, color="#b03a2a", mat="metal"), At([1.24, 0.05, 0.64], 1.0, color=DARK)]
    for y in (0.3, 0.55, 0.8):
        p.append(At([1.0, 0.04, 0.03], y, 0, 0.31, color=STEEL))
    for x in (-0.45, 0.45):
        for z in (-0.2, 0.2):
            p.append(At([0.12, 0.1, 0.12], 0, x, z, color=DARK))
    prop("Yard Tool Chest", "A red rolling tool chest: waist-high cover, breaks.", p, breakable=brk(220, "metal", count=10, size=(0.05, 0.2)), scatter=ANY)


# ------------------------------------------------------------------ outpost props
def outpost():
    # Lunar lander: four legs with pads, a tank ball, a bell, a crew can on top.
    p = []
    for a in (45, 135, 225, 315):
        p.append(B([0.14, 3.0, 0.14], [0, 1.5, 2.4], color=STEEL, yaw=a, pitch=-32))
        p.append(B([0.9, 0.12, 0.9], [0, 0.06, 3.1], color=STEEL, yaw=a))
    p.append(At([3.2, 0.4, 3.2], 1.9, color=STEEL, mat="metal"))
    p.append(At([3.2, 0.4, 3.2], 1.9, color=STEEL, mat="metal", yaw=45))
    p += Cyl(1.4, 1.2, 0.9, color=BRONZE)                                  # the engine bell throat
    p += Cyl(2.0, 0.5, 0.4, color=DARK)                                    # the bell mouth
    p.append(At([2.6, 1.2, 2.6], 2.3, color=HULL, mat="metal"))
    p.append(At([2.6, 1.2, 2.6], 2.3, color=HULL, mat="metal", yaw=45))
    for a in (0, 90, 180, 270):
        p.append(B([1.1, 1.1, 1.1], [0, 2.9, 1.9], color=WHITE, yaw=a))
    p += Cyl(2.2, 1.6, 3.5, color=WHITE, mat="metal")
    p.append(At([2.4, 0.3, 2.4], 5.1, color=BRONZE))
    p.append(B([0.6, 0.6, 0.05], [0, 4.4, 1.12], color=GLASS))
    p.append(At([0.08, 1.6, 0.08], 5.4, 0.8, 0.8, color=STEEL))
    p.append(lit(At([0.2, 0.2, 0.2], 7.0, 0.8, 0.8, color=RED), glow("blink", RED, 1.4, duty=0.25, glow_=2.0)))
    p.append(B([0.9, 0.9, 0.06], [0, 5.9, -0.9], color=PANEL, pitch=30))
    prop("Lunar Lander", "A parked lunar lander (an original design): four splayed legs, a tank ring, an engine bell and a white crew can on top.", p,
         collider=[C([3.2, 5.4, 3.2], [0, 2.7, 0]), C([5.0, 0.3, 5.0], [0, 0.15, 0])], scatter=SQ)
    # Lunar rover: six wheels, an open chassis, a seat, a dish.
    p = [At([3.4, 0.3, 1.6], 0.5, color=STEEL, mat="metal"), At([3.5, 0.08, 1.7], 0.8, color=HAZARD)]
    for x in (-1.2, 0, 1.2):
        for z in (-0.95, 0.95):
            p += HCyl(0.7, 0.3, 0.35, x=x, z=z, color=DARK, alongZ=True)
            p.append(At([0.1, 0.1, 0.5], 0.3, x, z * 0.75, color=STEEL))
    p.append(At([0.8, 0.5, 1.2], 0.88, -0.9, color=WHITE))
    p.append(At([0.8, 0.9, 0.08], 0.88, -1.3, color=WHITE))
    p.append(At([0.6, 0.3, 1.2], 0.88, 0.2, color=DARK))
    p.append(At([1.0, 0.4, 1.0], 0.88, 1.0, color=PALE, mat="metal"))
    p.append(B([0.1, 0.9, 0.1], [1.3, 1.6, -0.5], color=STEEL))
    p.append(lit(B([0.7, 0.7, 0.06], [1.3, 2.1, -0.5], color=WHITE, pitch=-40), {"type": "swing", "axis": "y", "pivot": [1.3, 2.05, -0.5], "angle": 50, "period": 12}))
    p.append(lit(At([0.14, 0.14, 0.14], 1.35, -1.6, 0.6, color=AMBER), glow("blink", AMBER, 1.6, duty=0.3, glow_=1.4)))
    prop("Lunar Rover", "A parked six-wheeled lunar rover with an open chassis, a seat and a swivelling dish. Long along X.", p,
         collider=[C([3.4, 1.4, 1.9], [0, 0.7, 0])], scatter=SQ)
    # Solar array rack: a tilted row of dark blue panels on a frame.
    p = []
    for x in (-1.6, 0, 1.6):
        p.append(At([0.1, 0.9, 0.1], 0, x, 0.5, color=STEEL))
        p.append(At([0.1, 1.7, 0.1], 0, x, -0.5, color=STEEL))
    p.append(B([4.0, 0.06, 1.7], [0, 1.3, 0], color=PANEL, pitch=-36))
    for x in (-1.0, 1.0):
        p.append(B([0.03, 0.08, 1.7], [x, 1.33, 0], color=PALE, pitch=-36))
    p.append(B([4.0, 0.08, 0.03], [0, 1.33, 0], color=PALE, pitch=-36))
    p.append(At([0.4, 0.4, 0.3], 0, 1.95, 0.4, color=DARK))
    p.append(lit(At([0.08, 0.05, 0.03], 0.3, 1.95, 0.56, color="#5aff7a"), glow("blink", "#5aff7a", 2.0, duty=0.5, glow_=1.0)))
    prop("Solar Array Rack", "A 4 m row of dark blue solar panels tilted on a frame: chest-high cover (long along X).", p,
         collider=[C([4.0, 1.9, 1.7], [0, 0.95, 0])], scatter=SQ)
    # Core drill rig: a lattice derrick with a spinning drill head and a hazard base.
    p = [At([3.0, 0.4, 3.0], 0, color=HAZARD, mat="metal"), At([2.2, 0.6, 2.2], 0.4, color=DARK)]
    for x in (-1.0, 1.0):
        for z in (-1.0, 1.0):
            p.append(B([0.12, 6.4, 0.12], [x * 0.85, 3.6, z * 0.85], color=ORANGE, pitch=-5 * z, roll=5 * x))
    for k in range(5):
        y = 1.6 + k * 1.2
        s = 1.7 - k * 0.14
        p.append(At([s, 0.06, 0.06], y, 0, -s / 2, color=ORANGE))
        p.append(At([s, 0.06, 0.06], y, 0, s / 2, color=ORANGE))
        p.append(At([0.06, 0.06, s], y, -s / 2, 0, color=ORANGE))
        p.append(At([0.06, 0.06, s], y, s / 2, 0, color=ORANGE))
    p.append(At([1.4, 0.4, 1.4], 6.7, color=STEEL))
    p.append(lit(At([0.3, 2.0, 0.3], 0.9, color=STEEL), {"type": "spin", "axis": "y", "rpm": 60}))
    p.append(lit(At([0.6, 0.5, 0.6], 2.9, color=DARK), {"type": "bob", "distance": 0.25, "period": 2.6}))
    p.append(lit(At([0.2, 0.2, 0.2], 7.1, color=RED), glow("blink", RED, 1.2, duty=0.3, glow_=2.0)))
    p.append(At([0.6, 0.9, 0.5], 0.4, 1.6, 1.2, color=PALE, mat="metal"))
    p.append(lit(At([0.4, 0.3, 0.02], 0.9, 1.6, 1.46, color=TEAL), glow("flicker", TEAL, rate=3, duty=0.92, glow_=1.0)))
    prop("Core Drill Rig", "A 7 m orange lattice derrick on a hazard-striped base with a spinning core drill and a bobbing head.", p,
         collider=[C([3.0, 1.0, 3.0]), C([1.9, 6.6, 1.9], [0, 3.3, 0])], scatter=SQ)
    # Ore conveyor section: a 4 m belt on legs with turning rollers.
    p = []
    for x in (-1.7, 1.7):
        for z in (-0.5, 0.5):
            p.append(At([0.1, 1.0, 0.1], 0, x, z, color=STEEL))
    p.append(At([4.0, 0.12, 1.2], 1.0, color=DARK))
    p.append(At([4.0, 0.06, 1.0], 1.12, color="#2a2a2e"))
    for x in (-1.6, -0.8, 0, 0.8, 1.6):
        p.append(lit(B([0.14, 0.14, 1.1], [x, 1.19, 0], color=STEEL, pitch=45), {"type": "spin", "axis": "x", "rpm": 40}))
    p.append(At([4.0, 0.04, 0.08], 1.3, 0, -0.56, color=HAZARD))
    p.append(At([4.0, 0.04, 0.08], 1.3, 0, 0.56, color=HAZARD))
    for x in (-1.2, 0, 1.2):
        p.append(At([0.5, 0.2, 0.5], 1.18, x, 0, color="#9a9088"))
    prop("Ore Conveyor Section", "A 4 m ore conveyor on legs with turning rollers and a few lumps of ore: hip-high cover (long along X).", p,
         collider=[C([4.0, 1.35, 1.2], [0, 0.68, 0])], scatter=SQ)
    # Regolith hopper: a square funnel on legs with a chute.
    p = []
    for x in (-1.0, 1.0):
        for z in (-1.0, 1.0):
            p.append(At([0.14, 1.6, 0.14], 0, x, z, color=STEEL))
    p.append(At([2.4, 1.4, 2.4], 1.6, color=PALE, mat="metal"))
    p.append(At([2.5, 0.1, 2.5], 3.0, color=HAZARD))
    for a in (0, 90, 180, 270):
        p.append(B([2.4, 1.2, 0.06], [0, 1.0, 0.95], color=PALE, yaw=a, pitch=-30))
    p.append(At([0.6, 0.3, 0.6], 0.3, color=DARK))
    p.append(B([0.5, 0.1, 1.6], [0.8, 0.5, 0.9], color=STEEL, pitch=-25))
    p.append(At([0.12, 0.8, 0.12], 3.1, 1.0, 1.0, color=STEEL))
    p.append(lit(At([0.2, 0.2, 0.2], 3.9, 1.0, 1.0, color=AMBER), glow("blink", AMBER, 2.2, duty=0.3, glow_=1.6)))
    prop("Regolith Hopper", "A pale square ore hopper on four legs with a funnel and a chute: tall cover.", p,
         collider=[C([2.4, 3.1, 2.4], [0, 1.55, 0])], scatter=SQ)
    # Oxygen tank bank: four white tanks on a frame, explosive.
    p = [At([3.0, 0.12, 1.0], 0, color=DARK)]
    for k in range(4):
        x = -1.05 + k * 0.7
        p += Cyl(0.5, 1.4, 0.12, x, 0, color=WHITE, mat="metal")
        p.append(At([0.12, 0.12, 0.12], 1.52, x, 0, color="#4a7ab0"))
        p.append(At([0.44, 0.1, 0.02], 0.9, x, 0.26, color="#4a7ab0"))
    p.append(At([3.0, 0.06, 0.06], 1.1, 0, 0.5, color=STEEL))
    p.append(At([3.0, 0.06, 0.06], 1.1, 0, -0.5, color=STEEL))
    prop("Oxygen Tank Bank", "Four white oxygen tanks strapped on a frame. Shoot it and it bursts after a short fuse.", p,
         collider=[C([3.0, 1.6, 1.0])], scatter=SQ,
         breakable=brk(90, "metal", count=12, explode={"radius": 5.0, "damage": 100, "fuse": 0.6, "knockback": 12}))
    # Hab airlock module: a short white cylinder-ish module with a round door and a lamp.
    p = Cyl(2.4, 2.6, 0, color=WHITE, mat="metal") + [At([2.5, 0.12, 2.5], 0, color=DARK), At([2.5, 0.12, 2.5], 2.5, color=BRONZE)]
    p.append(At([1.0, 1.6, 0.1], 0.5, 0, 1.2, color=STEEL))
    p.append(At([0.4, 0.4, 0.04], 1.5, 0, 1.27, color=GLASS))
    p.append(lit(At([0.3, 0.1, 0.1], 2.2, 0, 1.25, color="#5aff7a"), glow("cycle", "#5aff7a", 6.0, steps=2, on=[0], off="#c83a2a", glow_=1.4)))
    prop("Hab Airlock Module", "A white airlock drum with a round-windowed door facing +Z and a green-red cycling lamp: tall cover.", p,
         collider=[C([2.4, 2.6, 2.4]), C([1.0, 2.6, 2.4]), C([2.4, 2.6, 1.0])], scatter=SQ)
    # Sample crate: a pale sealed case with orange corners.
    p = [At([0.9, 0.5, 0.6], 0, color=PALE, mat="metal"), At([0.94, 0.05, 0.64], 0.47, color=ORANGE), At([0.94, 0.05, 0.64], 0.0, color=ORANGE),
         At([0.3, 0.12, 0.02], 0.25, 0, 0.31, color=DARK)]
    prop("Sample Crate", "A pale sealed sample case with orange edges: breaks.", p, breakable=brk(160, "metal", count=10, size=(0.06, 0.2)), scatter=ANY)
    # Survey light: a slim pole with a blinking lamp.
    p = [At([0.3, 0.3, 0.3], 0, color=DARK), At([0.08, 2.2, 0.08], 0.3, color=ORANGE),
         lit(At([0.22, 0.22, 0.22], 2.5, color=TEAL), glow("blink", TEAL, 2.4, duty=0.2, glow_=1.8))]
    prop("Survey Light", "A slim orange survey pole with a blinking cyan lamp.", p, collider=[C([0.3, 2.7, 0.3])], scatter=ANY)
    # Lunar boulders: solid big, cosmetic small, and a cosmetic rubble scatter.
    p = [B([1.4, 1.1, 1.2], [0, 0.5, 0], color=PALE, yaw=20, roll=8), B([1.1, 0.9, 1.3], [0.2, 0.42, -0.1], color="#b9b7b0", yaw=-30, pitch=10),
         B([0.8, 0.7, 0.8], [-0.3, 0.7, 0.2], color=WHITE, yaw=50)]
    prop("Lunar Boulder", "A pale grey boulder of fused regolith: waist-high cover.", p, collider=[C([1.4, 1.2, 1.3])], scatter={"randomYaw": True, "scale": [0.8, 1.6]})
    p = [B([0.5, 0.36, 0.44], [0, 0.17, 0], color=PALE, yaw=25, roll=6), B([0.36, 0.3, 0.42], [0.1, 0.14, -0.06], color="#b9b7b0", yaw=-20)]
    prop("Lunar Boulder Small", "A small pale rock: looks only.", p, collision="cosmetic", fade=50, scatter={"randomYaw": True, "scale": [0.6, 1.5], "weight": 3})
    p = []
    for k, (x, z, s) in enumerate([(-0.5, 0.3, 0.16), (0.4, -0.3, 0.12), (0.1, 0.5, 0.1), (-0.2, -0.5, 0.14), (0.6, 0.4, 0.08), (-0.6, -0.1, 0.1)]):
        p.append(B([s, s * 0.7, s * 0.9], [x, s * 0.3, z], color=PALE if k % 2 else "#b9b7b0", yaw=k * 50))
    prop("Lunar Rubble", "A scatter of small pale stones: looks only.", p, collision="cosmetic", fade=40, scatter={"randomYaw": True, "scale": [0.8, 1.4], "weight": 4})
    # Regolith bag: a cosmetic sack of fines.
    p = [B([0.7, 0.4, 0.5], [0, 0.2, 0], color="#cfc9bc", yaw=10), B([0.5, 0.2, 0.3], [0.05, 0.45, 0], color="#c2bcb0", yaw=-15)]
    prop("Regolith Bag", "A sagging bag of regolith fines: looks only.", p, collision="cosmetic", fade=50, scatter={"randomYaw": True, "weight": 2})


def write(out):
    for p in PROPS:
        folder = os.path.join(out, CAT, p.pop("_sub"))
        os.makedirs(folder, exist_ok=True)
        fn = "".join(ch for ch in p["name"] if ch.isalnum()) + ".json"
        with open(os.path.join(folder, fn), "w") as f:
            json.dump(p, f, indent=1)
            f.write("\n")
    print(f"wrote {len(PROPS)} props to {os.path.join(out, CAT)}/Keelspar and Regolith")


if __name__ == "__main__":
    CURRENT["sub"] = "Keelspar"
    yard()
    CURRENT["sub"] = "Regolith"
    outpost()
    write(os.path.join(REPO, "Props"))
