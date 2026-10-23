#!/usr/bin/env python3
"""Writes the sci-fi prop set Props/Space/CanopyIgnition (category "Space"): the pieces the Space pack's derelict arboretum
(07 Canopy Deck) and fusion reactor hall (08 Ignition Hall) need beyond the Facility, Industrial, Station and garden
sets. Original designs only (no film or game hardware, names or insignia).

    python3 tools/space/canopy_ignition_props.py            (run from the repository root)

Arboretum: hydroponic racks (dead and lit), a planter trough, a seed vault cabinet, a sprinkler mast, a canal sluice
gate, a grow light pole, an emergency strip lamp, a dead vine curtain (concealment), a soil heap and a wrecked
maintenance drone. Reactor: a magnet coil block, a plasma conduit (chain them end to end for a glowing coolant main), a
coolant manifold, a cryo dewar, a console bank, a warning beacon post, a blast shutter, a tool cart and a hazard chain.

Uses the box helpers of tools/propviz/gen.py. Health follows the material guide in Props/README.md: structures and
machines are unbreakable cover; cabinets, consoles and carts are heavy metal.
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
AUTHOR = "Call of Slop"

STEEL = "#6c6f73"
DARK = "#34363a"
HULL = "#9a9ea6"
HULL2 = "#7d8189"
RUST = "#7a4a30"
SOIL = "#3e3228"
MOSS = "#4f6b2e"
DEADLEAF = "#7a6a3a"
LEAF = "#4a8a3a"
PINK = "#ff5ad0"
BLUE = "#4ac8ff"
ORANGE = "#ff8a2a"
AMBER = "#ffb24a"
RED = "#ff4a3a"
COPPER = "#b0683a"
GLASS = "#2e4450"
WHITE = "#e8ecf0"

SQ = {"randomYaw": False, "yawStep": 90, "yawJitter": 3}
ANY = {"randomYaw": True, "scale": [0.95, 1.05]}


def prop(name, desc, parts, **kw):
    gen.prop(name, CAT, desc, parts, **kw)
    PROPS[-1]["author"] = AUTHOR


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


# ------------------------------------------------------------------ arboretum ------------------------------------------------------------------

def hydroponic_rack(name, desc, alive):
    p = []
    for x in (-1.42, 1.42):
        for z in (-0.42, 0.42):
            p.append(At([0.08, 2.2, 0.08], 0, x, z, color=STEEL))
    tube = PINK if alive else "#5a3a50"
    for k, y in enumerate((0.3, 0.95, 1.6)):
        p.append(At([2.9, 0.05, 0.9], y, 0, 0, color=HULL2, mat="metal"))                       # shelf
        p.append(At([2.7, 0.18, 0.7], y + 0.05, 0, 0, color=DARK))                               # tray
        p.append(At([2.6, 0.04, 0.6], y + 0.22, 0, 0, color=SOIL if alive else "#2a2420"))       # growing medium
        for i in range(5):
            x = -1.1 + i * 0.55
            if alive:
                p.append(At([0.22, 0.28, 0.2], y + 0.24, x, 0.0, color=LEAF, yaw=20 * i))
                p.append(At([0.3, 0.12, 0.28], y + 0.38, x, 0.0, color="#62a84a", yaw=35 * i))
            else:
                p.append(At([0.06, 0.26, 0.06], y + 0.24, x, 0.05 * (i % 2), color=DEADLEAF, roll=12 * (i % 3 - 1)))
                p.append(At([0.26, 0.03, 0.18], y + 0.24, x, 0.0, color="#5a4a2a", yaw=20 * i))
        lamp = At([2.6, 0.05, 0.1], y + 0.56 if k < 2 else 2.08, 0, 0, color=tube)
        p.append(lit(lamp, glow("flicker", PINK, rate=6, duty=0.9, glow_=1.6)) if alive else lamp)
    p.append(At([3.0, 0.08, 1.0], 2.2, 0, 0, color=HULL, mat="metal"))                             # cap rail
    p.append(At([0.5, 0.4, 0.06], 1.25, -1.0, 0.47, color=DARK))                                     # controller box
    p.append(lit(At([0.1, 0.1, 0.02], 1.4, -0.9, 0.5, color=BLUE if alive else RED), glow("blink", BLUE if alive else RED, 2.0, duty=0.3)))
    prop(name, desc, p, collider=[C([3.0, 2.2, 1.0])], scatter=SQ)


def planter_trough():
    p = [At([3.0, 0.9, 1.2], 0, color=HULL2, mat="metal"), At([3.06, 0.08, 1.26], 0.86, color=STEEL),
         At([2.8, 0.1, 1.0], 0.86, color=SOIL)]
    for i in range(4):
        x = -1.1 + i * 0.73
        p.append(At([0.5, 0.5, 0.5], 0.92, x, 0.1 * (i % 2 - 0.5), color=MOSS if i % 2 else "#5e8a3a", yaw=25 * i))
        p.append(At([0.7, 0.2, 0.6], 1.3, x, 0, color=LEAF, yaw=40 * i))
    p.append(At([3.1, 0.04, 0.3], 0.3, 0, 0.62, color=RUST))                                        # rust streak band
    p.append(At([3.0, 0.06, 0.6], 0.02, 0, -0.9, color=MOSS))                                       # moss spilling over
    prop("Planter Trough", "A long metal planter trough overgrown with shrubs: waist-high cover you can mantle.", p,
         collider=[C([3.0, 0.95, 1.2])], scatter=SQ)


def seed_vault():
    p = [At([1.2, 2.1, 0.7], 0, color=HULL, mat="metal"), At([1.26, 0.06, 0.76], 2.06, color=STEEL),
         At([0.9, 1.3, 0.04], 0.5, 0, 0.35, color="#a8c8d8"), At([1.0, 0.08, 0.06], 1.85, 0, 0.35, color=DARK),
         At([0.1, 0.5, 0.05], 0.9, 0.5, 0.36, color=DARK)]
    for k in range(4):
        p.append(At([0.8, 0.02, 0.02], 0.7 + k * 0.3, 0, 0.37, color="#d8e8f0"))
    p.append(lit(At([0.14, 0.14, 0.03], 1.9, -0.4, 0.36, color=BLUE), glow("pulse", BLUE, 3.2, glow_=1.4)))
    prop("Seed Vault Cabinet", "A frosted cryo seed cabinet, still humming, with a pulsing blue status lamp.", p,
         breakable=brk(320, "metal", count=12, size=(0.08, 0.25)), scatter=SQ)


def sprinkler_mast():
    p = [At([0.5, 0.12, 0.5], 0, color=DARK), At([0.14, 3.0, 0.14], 0.12, color=STEEL)]
    head = At([0.3, 0.2, 0.3], 3.1, color=HULL2)
    p.append(lit(head, {"type": "spin", "axis": "y", "rpm": 6, "jitter": 0.3}))
    for yaw in (0, 90):
        p.append(lit(At([1.8, 0.05, 0.05], 3.2, color=STEEL, yaw=yaw), {"type": "follow", "parent": 3}))
    p.append(At([0.4, 0.06, 0.06], 3.2, 0, 0, color=RUST))
    prop("Sprinkler Mast", "A tall irrigation mast with a slowly turning cross head, long dry.", p,
         collider=[C([0.5, 3.1, 0.5])], scatter=ANY)


def sluice_gate():
    p = []
    for x in (-1.6, 1.6):
        p.append(At([0.3, 2.4, 0.6], 0, x, 0, color="#8a8478", mat="concrete"))
    p.append(At([3.5, 0.3, 0.6], 2.4, 0, 0, color="#8a8478", mat="concrete"))
    p.append(At([2.9, 1.3, 0.12], 0.9, 0, 0, color=RUST, mat="metal"))                              # the gate, half raised
    p.append(At([2.9, 0.06, 0.14], 2.2, 0, 0, color=STEEL))
    for x in (-0.9, 0.0, 0.9):
        p.append(At([0.06, 1.3, 0.14], 0.9, x, 0, color=STEEL))
    p.append(At([0.12, 0.9, 0.12], 2.7, 0, 0, color=STEEL))                                        # spindle
    p += Cyl(0.9, 0.08, 3.4, 0, 0, color=RUST)                                                      # wheel
    p.append(At([0.9, 0.08, 0.08], 3.4, 0, 0, color=RUST))
    p.append(At([0.08, 0.08, 0.9], 3.4, 0, 0, color=RUST))
    prop("Sluice Gate", "A concrete sluice frame over a dry canal with a rusted gate half raised and a hand wheel on top.", p,
         collider=[C([0.3, 2.4, 0.6], [-1.6, 1.2, 0]), C([0.3, 2.4, 0.6], [1.6, 1.2, 0]), C([3.5, 0.5, 0.6], [0, 2.45, 0])], scatter=SQ)


def grow_light_pole():
    p = [At([0.4, 0.1, 0.4], 0, color=DARK), At([0.12, 3.4, 0.12], 0.1, color=STEEL),
         At([0.9, 0.08, 0.5], 3.5, 0.3, 0, color=HULL2)]
    p.append(lit(At([0.8, 0.03, 0.4], 3.46, 0.3, 0, color="#ffd0f0"), glow("flicker", "#ffb0f0", rate=4, duty=0.92, glow_=1.5)))
    p.append(At([0.5, 0.06, 0.06], 3.3, 0.15, 0, color=STEEL))
    prop("Grow Light Pole", "A slim pole with a pink-white grow light panel, still on.", p, collider=[C([0.4, 3.5, 0.4])], scatter=ANY)


def emergency_strip():
    p = [At([1.2, 0.25, 0.12], 0, 0, 0, color=DARK)]
    p.append(lit(At([1.0, 0.12, 0.03], 0.06, 0, 0.06, color=RED), glow("blink", RED, 1.4, duty=0.5, glow_=1.8)))
    prop("Emergency Strip Lamp", "A red emergency strip lamp for a bulkhead (its back at -Z; lift it with y).", p,
         collision="cosmetic", scatter={"randomYaw": False, "yawStep": 90}, fade=70)


def vine_curtain():
    p = [At([3.0, 0.14, 0.14], 3.4, color=STEEL)]
    import random
    rng = random.Random(11)
    for i in range(14):
        x = -1.4 + i * 0.215
        h = rng.uniform(1.6, 3.2)
        p.append(At([0.12, h, 0.12], 3.4 - h, x, rng.uniform(-0.2, 0.2), color=DEADLEAF if i % 3 else MOSS, roll=rng.uniform(-6, 6)))
        if i % 2 == 0:
            p.append(At([0.34, 0.2, 0.3], 3.4 - h * rng.uniform(0.3, 0.9), x, rng.uniform(-0.25, 0.25), color=LEAF if i % 4 else MOSS, yaw=rng.uniform(0, 90)))
    prop("Dead Vine Curtain", "Vines hanging from a rail, mostly dead: walk and shoot through, hides you from bots.", p,
         collision="cosmetic", scatter={"randomYaw": False, "yawStep": 90}, fade=90)
    PROPS[-1]["concealment"] = True


def soil_heap():
    p = Cyl(2.0, 0.5, 0, color=SOIL, mat="dirt") + Cyl(1.3, 0.3, 0.5, color=SOIL, mat="dirt") + Cyl(0.6, 0.15, 0.8, color="#4a3c30")
    p.append(At([0.5, 0.15, 0.5], 0.5, 0.5, 0.3, color=MOSS))
    p.append(At([0.06, 0.6, 0.06], 0.8, -0.3, -0.2, color=DEADLEAF, roll=15))
    prop("Soil Heap", "A heap of dark soil spilled from a torn planter: low cover you can step onto.", p,
         collider=[C([1.8, 0.75, 1.8])], scatter=ANY)


def drone_wreck():
    p = [At([1.0, 0.5, 0.8], 0.25, 0, 0, color=HULL2, mat="metal", roll=8), At([0.9, 0.2, 0.7], 0.72, 0, 0, color=DARK, roll=8)]
    for (x, z, a) in ((0.9, 0.6, 20), (-0.9, 0.6, -15), (0.9, -0.6, -30), (-0.75, -0.65, 40)):
        p.append(B([1.1, 0.08, 0.1], [x * 0.7, 0.5, z * 0.7], color=STEEL, yaw=a, roll=10 if x > 0 else -20))
        p.append(B([0.5, 0.04, 0.12], [x, 0.6, z], color=DARK, yaw=a + 45))
    p.append(At([0.3, 0.3, 0.3], 0.35, 0.55, 0.0, color=GLASS))
    p.append(lit(At([0.1, 0.1, 0.1], 0.82, -0.2, 0.2, color=AMBER), glow("blink", AMBER, 3.0, duty=0.1, glow_=1.6)))
    p.append(At([0.6, 0.06, 0.4], 0.0, -0.4, 0.5, color=RUST))
    prop("Maintenance Drone Wreck", "A crashed four-rotor maintenance drone, arms bent, one lamp still blinking.", p,
         collider=[C([2.2, 0.9, 1.6])], scatter=ANY)


# ------------------------------------------------------------------ reactor ------------------------------------------------------------------

def magnet_coil():
    p = [At([3.0, 0.3, 1.4], 0, color=DARK, mat="metal"), At([2.6, 2.6, 1.1], 0.3, color=COPPER, mat="metal"),
         At([3.0, 0.3, 1.4], 2.9, color=DARK, mat="metal")]
    for k in range(6):
        p.append(At([2.7, 0.08, 1.16], 0.5 + k * 0.42, color="#8a4a28"))
    for x in (-1.3, 1.3):
        p.append(At([0.3, 3.2, 1.4], 0, x, 0, color=HULL2, mat="metal"))
    p.append(lit(At([2.4, 0.1, 0.04], 1.5, 0, 0.58, color=BLUE), glow("pulse", BLUE, 2.2, glow_=1.5)))
    p.append(lit(At([2.4, 0.1, 0.04], 1.5, 0, -0.58, color=BLUE), glow("pulse", BLUE, 2.2, glow_=1.5)))
    p.append(At([0.6, 0.5, 0.2], 2.2, 0.9, 0.62, color=DARK))
    prop("Magnet Coil Block", "A copper field-coil block in a steel frame with pulsing blue field lines: tall solid cover.", p,
         collider=[C([3.0, 3.2, 1.4])], scatter=SQ)


def plasma_conduit():
    p = HCyl(1.0, 4.0, 0.7, color=HULL, mat="metal")
    for x in (-1.9, 1.9):
        p += HCyl(1.2, 0.2, 0.7, x=x, color=STEEL)
    for x in (-1.0, 0.0, 1.0):
        p.append(lit(B([0.1, 1.08, 1.08], [x, 0.7, 0], color=BLUE), glow("pulse", BLUE, 1.6, glow_=1.4, phase=0.33 * (x + 1))))
    for x in (-1.2, 1.2):
        p.append(At([0.2, 0.2, 1.0], 0.0, x, 0, color=DARK))
    p.append(At([1.6, 0.04, 0.3], 1.21, 0, 0, color=ORANGE))
    prop("Plasma Conduit", "A 4 m run of coolant conduit with pulsing blue field rings (chain them end to end along X): you can mantle it.", p,
         collider=[C([4.0, 1.22, 1.1], [0, 0.61, 0])], scatter=SQ)


def coolant_manifold():
    p = [At([2.4, 1.6, 1.4], 0, color=HULL2, mat="metal"), At([2.5, 0.1, 1.5], 1.6, color=DARK)]
    for x in (-0.8, 0.0, 0.8):
        p += Cyl(0.4, 0.5, 1.7, x, 0, color=ORANGE)
        p += Cyl(0.6, 0.06, 1.75, x, 0.3, color=STEEL)
        p.append(At([0.6, 0.06, 0.06], 1.75, x, 0.3, color=STEEL))
    p += HCyl(0.5, 2.6, 1.0, z=0.85, color=ORANGE)
    p += HCyl(0.5, 2.6, 0.5, z=-0.85, color=ORANGE)
    for x in (-0.7, 0.7):
        p.append(At([0.3, 0.3, 0.06], 0.9, x, 0.71, color=WHITE))
        p.append(At([0.02, 0.14, 0.02], 0.95, x, 0.75, color=RED, roll=30))
    p.append(lit(At([0.1, 0.1, 0.03], 1.4, 0.0, 0.72, color=AMBER), glow("blink", AMBER, 1.0, duty=0.5, glow_=1.5)))
    prop("Coolant Manifold", "A valve manifold: three orange valve stacks with hand wheels, gauges and a warning lamp on a steel block.", p,
         collider=[C([2.4, 2.0, 1.6], [0, 1.0, 0])], scatter=SQ)


def cryo_dewar():
    p = Cyl(1.5, 2.2, 0.2, color="#c8ccd4", mat="metal") + Cyl(1.6, 0.2, 0, color=DARK) + Cyl(1.1, 0.3, 2.4, color="#c8ccd4")
    for y in (0.8, 1.6):
        p += Cyl(1.56, 0.1, y, color="#dbe8f4")                                                     # frost bands
    p.append(At([0.14, 0.6, 0.14], 2.7, 0.25, 0, color=STEEL))
    p.append(At([0.5, 0.08, 0.08], 3.1, 0.25, 0, color=STEEL))
    p.append(lit(At([0.1, 0.1, 0.03], 1.2, 0, 0.78, color=BLUE), glow("pulse", BLUE, 2.8, glow_=1.3)))
    p.append(At([0.5, 0.3, 0.03], 1.7, 0, 0.78, color=WHITE))
    prop("Cryo Dewar", "A tall silver cryogenic dewar with frost bands, a vent stack and a blue status lamp.", p,
         collider=[C([1.5, 2.6, 1.5])], scatter=ANY)


def console_bank():
    p = [At([3.0, 0.9, 0.9], 0, color=HULL2, mat="metal"), At([3.0, 0.4, 0.5], 0.9, 0, -0.2, color=HULL2, mat="metal"),
         At([3.1, 0.06, 0.96], 0.9, 0, 0, color=DARK)]
    for i, x in enumerate((-0.95, 0.0, 0.95)):
        screen = At([0.8, 0.3, 0.03], 0.96, x, 0.06, color=BLUE if i != 1 else ORANGE)
        p.append(lit(screen, glow("flicker", BLUE if i != 1 else ORANGE, rate=8, duty=0.88, glow_=1.1, phase=0.3 * i)))
        for k in range(6):
            p.append(At([0.08, 0.03, 0.08], 0.93, x - 0.3 + k * 0.12, 0.3, color=["red", "green", AMBER][k % 3]))
    p.append(At([2.8, 0.06, 0.06], 0.6, 0, 0.47, color=STEEL))
    prop("Console Bank", "Three control consoles in a row with flickering orange and blue screens and rows of switches.", p,
         breakable=brk(300, "metal", by=["explosions", "streaks", "bullets"], count=14, size=(0.08, 0.3)), scatter=SQ)


def warning_beacon():
    p = [At([0.5, 0.1, 0.5], 0, color=DARK), At([0.14, 2.0, 0.14], 0.1, color=STEEL)]
    for k in range(5):
        p.append(At([0.16, 0.2, 0.16], 0.2 + k * 0.4, color=ORANGE if k % 2 else DARK))
    p.append(Cyl(0.3, 0.12, 2.1, color=DARK)[0])
    p.append(lit(At([0.26, 0.26, 0.26], 2.22, color=ORANGE), {"type": "spin", "axis": "y", "rpm": 60, "glow": 1.6}))
    p.append(lit(At([0.3, 0.1, 0.3], 2.48, color=DARK), {"type": "follow", "parent": 9}))
    prop("Warning Beacon Post", "A hazard-striped post with a turning orange beacon on top.", p, collider=[C([0.5, 2.5, 0.5])], scatter=ANY)


def blast_shutter():
    p = [At([4.0, 3.6, 0.4], 0, color=HULL2, mat="metal")]
    for k in range(5):
        p.append(At([3.6, 0.08, 0.04], 0.5 + k * 0.65, 0, 0.21, color=DARK))
    for i in range(6):
        p.append(B([0.5, 0.3, 0.03], [-1.6 + i * 0.64, 3.1, 0.22], color=ORANGE if i % 2 == 0 else DARK, yaw=0))
    p.append(At([4.2, 0.2, 0.5], 3.6, color=DARK))
    p.append(At([0.6, 0.6, 0.06], 1.5, 1.3, 0.22, color=WHITE))
    p.append(lit(At([0.12, 0.12, 0.04], 2.6, -1.5, 0.22, color=RED), glow("blink", RED, 2.4, duty=0.2, glow_=1.4)))
    prop("Blast Shutter", "A closed steel blast shutter with an orange hazard band: a 4 m wall section (front at +Z).", p,
         collider=[C([4.0, 3.8, 0.5])], scatter=SQ)


def tool_cart():
    p = [At([1.2, 0.8, 0.7], 0.15, color=RED, mat="metal"), At([1.24, 0.05, 0.74], 0.95, color=DARK)]
    for k in range(3):
        p.append(At([1.0, 0.02, 0.02], 0.3 + k * 0.22, 0, 0.36, color=STEEL))
    for x in (-0.45, 0.45):
        for z in (-0.25, 0.25):
            p.append(At([0.12, 0.15, 0.12], 0, x, z, color="rubber"))
    p.append(At([0.4, 0.1, 0.2], 1.0, -0.2, 0, color=STEEL))
    p.append(At([0.3, 0.06, 0.3], 1.0, 0.3, 0.1, color=AMBER))
    prop("Tool Cart", "A red wheeled tool cart with its drawers shut and tools on top.", p,
         breakable=brk(150, "metal", count=10, size=(0.06, 0.2)), scatter=ANY)


def hazard_chain():
    p = []
    for x in (-1.1, 1.1):
        p.append(At([0.14, 0.9, 0.14], 0, x, 0, color=ORANGE))
        p.append(At([0.3, 0.05, 0.3], 0, x, 0, color=DARK))
    for i in range(9):
        x = -0.9 + i * 0.225
        sag = 0.22 * (1 - (x / 1.0) ** 2)
        p.append(At([0.2, 0.05, 0.05], 0.82 - sag, x, 0, color=DARK if i % 2 else ORANGE))
    prop("Hazard Chain", "Two orange bollards with a sagging chain between them (looks only).", p, collision="cosmetic",
         scatter={"randomYaw": False, "yawStep": 90}, fade=60)


def write(out):
    folder = os.path.join(out, CAT, "CanopyIgnition")
    os.makedirs(folder, exist_ok=True)
    for p in PROPS:
        fn = "".join(ch for ch in p["name"] if ch.isalnum()) + ".json"
        with open(os.path.join(folder, fn), "w") as f:
            json.dump(p, f, indent=1)
            f.write("\n")
    print(f"wrote {len(PROPS)} props to {folder}")


if __name__ == "__main__":
    hydroponic_rack("Hydroponic Rack", "A three-shelf hydroponic rack: dead stalks in dry trays, lamps out, a red fault lamp blinking.", alive=False)
    hydroponic_rack("Hydroponic Rack Lit", "A three-shelf hydroponic rack still growing under flickering pink grow lamps.", alive=True)
    planter_trough()
    seed_vault()
    sprinkler_mast()
    sluice_gate()
    grow_light_pole()
    emergency_strip()
    vine_curtain()
    soil_heap()
    drone_wreck()
    magnet_coil()
    plasma_conduit()
    coolant_manifold()
    cryo_dewar()
    console_bank()
    warning_beacon()
    blast_shutter()
    tool_cart()
    hazard_chain()
    write(os.path.join(REPO, "Props"))
