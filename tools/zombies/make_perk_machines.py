#!/usr/bin/env python3
"""Writes Props/Zombies/PerkMachines/*.json: a machine of its own shape for every Zombies perk, and the Power Switch Box
(2026-10-27; docs/zombies/ZOMBIES-MODE.md, "Perks" and "Machine props").

Run from the repository root:  python3 -I tools/zombies/make_perk_machines.py

The game finds a perk's machine by the perk's key (ZombiesMap.MachineProp.PerkFor: "jugg" -> "Jugg Perk Machine"); a
perk with no machine here stands as the plain "Perk Machine" (Props/Zombies/PerkMachine.json, hand-made). The script
refuses to write while a perk in Parts/Tables/ZombiesRules.json has no machine (and the other way round).

Every machine shares the same FACE, in the places the game draws on (Source/App/Program.ZombiesPerks.cs): the sign
panel at y 1.62 (0.7 x 0.5, at z -0.36), its corner lamps at y 1.93 (x +-0.33, z -0.37) and the glow strip at y 1.33,
all on a dark sign board whose front is at z -0.35; under it the dispenser (a niche and a tray at y 0.8). So a body
stays behind z -0.335 where the face is (|x| < 0.4, y 0.5 to 2.0).

The BODY round and behind the face is the perk's own silhouette, themed on what it does (original shapes): a riveted
pressure tank (Ironhide Cider), a revolver cylinder standing on end (Swiftload Seltzer), twin barrels with a cartridge belt (Twinfire
Punch), a slim medical column under a big plus sign with paddles at its sides (Mender's Mead), a bellows with a pair of
wings (Second Wind), a raked aerodynamic tower with tail fins (Fleetfoot Fizz), a bullseye on an easel (Marksman Malt),
a gnarled stump on splayed roots (Bloodroot Tonic), a gumball globe on a stepped base with a coin dial (Windfall Soda),
a stack of planks with a hammer on top (Hammerhand Ale), a scrap hopper on legs with a gear (Salvage Stout) and a
heater shield with a helmet on top (Bulwark Brew). The Power Switch Box is a breaker panel with ceramic insulators.

The helpers (boxes from rotation matrices, regular prisms for round parts, the turned bounds) are the slot cabinets'
(tools/zombies/make_slot_cabinets.py); see its notes on prop rotations. Each machine stays inside the plain Perk
Machine's footprint (0.9 x 0.7 m: the Blackmoor layout checks and the nav graph assume it) and under 128 parts.
"""
import importlib.util
import json
import math
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Props", "Zombies", "PerkMachines")
RULES = os.path.join(ROOT, "Parts", "Tables", "ZombiesRules.json")

_spec = importlib.util.spec_from_file_location("slot_cabinets", os.path.join(os.path.dirname(os.path.abspath(__file__)), "make_slot_cabinets.py"))
sc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sc)
b, ring_x, ring_y, ring_z, along, bounds, rx, ry, rz, mul = sc.b, sc.ring_x, sc.ring_y, sc.ring_z, sc.along, sc.bounds, sc.rx, sc.ry, sc.rz, sc.mul

GUN, STEEL, DARK, BRASS, COPPER, WOOD, DWOOD, GLASS, CHROME, BLACK = sc.GUN, sc.STEEL, sc.DARK, sc.BRASS, sc.COPPER, sc.WOOD, sc.DWOOD, sc.GLASS, sc.CHROME, sc.BLACK
WHITE = "#d8dade"


# ---- the shared face ----

def face(colour, board="#1c1d21"):
    return [
        b([0.8, 0.68, 0.05], [0, 1.62, -0.325], board, material="metal"),       # the sign board (front z -0.35)
        b([0.8, 0.03, 0.03], [0, 1.975, -0.335], colour),                        # its trims in the perk's colour
        b([0.8, 0.03, 0.03], [0, 1.265, -0.335], colour),
        b([0.03, 0.68, 0.03], [-0.415, 1.62, -0.335], colour),
        b([0.03, 0.68, 0.03], [0.415, 1.62, -0.335], colour),
        b([0.1, 0.1, 0.03], [-0.33, 1.93, -0.34], CHROME),                       # lamp bezels (the lamps are drawn on them)
        b([0.1, 0.1, 0.03], [0.33, 1.93, -0.34], CHROME),
        b([0.36, 0.52, 0.05], [0, 0.8, -0.325], board, material="metal"),        # the dispenser
        b([0.3, 0.44, 0.02], [0, 0.82, -0.355], CHROME),
        b([0.24, 0.34, 0.02], [0, 0.84, -0.362], BLACK),                         # its niche
        b([0.3, 0.05, 0.08], [0, 0.6, -0.365], CHROME),                          # the tray
        b([0.36, 0.04, 0.04], [0, 1.08, -0.36], colour),
    ]


def plinth(color="darkgrey", w=0.86, d=0.66, h=0.1, z=0.02):
    return [b([w, h, d], [0, h / 2, z], color, material="metal")]


def ring_at(cx, cz, y0, y1, a, color, sides=12, material=None):
    return ring_y(cx, cz, y0, y1, a, color, sides, material)


def dome(cx, cz, y0, a, steps, color, material="metal", sides=12):
    """A stepped dome on a ring of apothem a at height y0: `steps` rings, each lower and narrower."""
    p = []
    for i in range(steps):
        t0, t1 = i / steps, (i + 1) / steps
        ya, yb = y0 + a * math.sin(t0 * math.pi / 2), y0 + a * math.sin(t1 * math.pi / 2)
        p += ring_y(cx, cz, ya, max(yb, ya + 0.01), a * math.cos(t0 * math.pi / 2) * 0.98, color, sides, material)
    return p


# ---- the bodies ----

def jugg(c):
    """A riveted pressure tank: health and armour in a can."""
    red, dred = "#8a1a1a", "#5e1010"
    p = ring_y(0, 0.04, 0, 0.1, 0.37, "darkgrey", material="metal")
    p += ring_y(0, 0.04, 0.1, 1.9, 0.34, red, material="metal")                      # the tank
    for y in (0.3, 1.05, 1.82):
        p += ring_y(0, 0.04, y - 0.04, y + 0.04, 0.355, STEEL, material="metal")      # bands
    for yaw in (60, 120, 180, 240, 300):                                              # rivet rows (not on the front)
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        for y in (0.45, 0.75, 1.35, 1.65):
            p += [b([0.04, 0.04, 0.03], [0.345 * s, y, 0.04 - 0.345 * co], DARK, ry(-yaw))]
    p += dome(0, 0.04, 1.9, 0.34, 4, dred)                                            # the domed top
    p += ring_y(0, 0.04, 2.2, 2.42, 0.06, STEEL, 8, "metal")                         # the valve stem
    p += ring_y(0, 0.04, 2.42, 2.46, 0.2, c, 12, "metal")                           # the hand wheel
    for a in (0, 60, 120):
        p += [b([0.38, 0.03, 0.04], [0, 2.44, 0.04], DARK, ry(a))]
    p += ring_y(0.26, 0.2, 2.0, 2.15, 0.07, BRASS, 8, "metal")                      # a gauge
    p += [b([0.12, 0.12, 0.02], [0.26, 2.2, 0.13], WHITE, ry(-30))]
    return p, face(c)


def speed(c):
    """A revolver cylinder standing on end, a speed loader on its pin: reload."""
    green = "#1f5e2c"
    p = plinth("#2a2c30", 0.82, 0.62, 0.12, 0.04)
    p += ring_y(0, 0.06, 0.12, 1.9, 0.33, GUN, material="metal")                    # the cylinder
    for i in range(6):                                                               # flutes between the chambers
        yaw = 30 + i * 60
        if abs(((yaw + 180) % 360) - 180) < 40:
            continue
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        p += [b([0.08, 1.2, 0.04], [0.325 * s, 1.0, 0.06 - 0.325 * co], DARK, ry(-yaw))]
    p += ring_y(0, 0.06, 0.12, 0.24, 0.345, green, material="metal")                # the ratchet end
    p += ring_y(0, 0.06, 1.78, 1.9, 0.345, green, material="metal")
    for i in range(6):                                                               # chamber mouths on top
        a = math.radians(i * 60)
        p += ring_y(0.2 * math.sin(a), 0.06 - 0.2 * math.cos(a), 1.9, 1.92, 0.07, BLACK, 8)
        p += ring_y(0.2 * math.sin(a), 0.06 - 0.2 * math.cos(a), 1.92, 2.08, 0.055, BRASS, 8, "metal")  # rounds in the loader
    p += ring_y(0, 0.06, 1.9, 2.3, 0.05, STEEL, 8, "metal")                          # the centre pin
    p += ring_y(0, 0.06, 2.08, 2.14, 0.29, c, 12, "metal")                           # the speed loader's disc
    p += ring_y(0, 0.06, 2.14, 2.3, 0.08, DARK, 8)                                   # its knob
    p += [b([0.08, 0.8, 0.14], [0.4, 1.0, 0.1], STEEL, material="metal")]           # the crane arm, swung out
    p += [b([0.1, 0.1, 0.3], [0.4, 0.58, 0.0], STEEL)]
    return p, face(c)


def doubletap(c):
    """Twin barrels bound side by side, a cartridge belt down one side: double the fire."""
    p = plinth("#2a2c30", 0.86, 0.62, 0.1, 0.06)
    for x in (-0.2, 0.2):
        p += ring_y(x, 0.12, 0.1, 2.3, 0.17, GUN, 12, "metal")                       # the barrels
        p += ring_y(x, 0.12, 2.3, 2.38, 0.18, c, 12, "metal")                        # muzzle crowns
        p += ring_y(x, 0.12, 2.38, 2.385, 0.1, BLACK, 8)                             # bores
    for y in (0.45, 1.25, 2.05):                                                     # the bands binding them
        p += [b([0.78, 0.08, 0.4], [0, y, 0.12], STEEL, material="metal")]
    p += [b([0.08, 1.9, 0.08], [0, 1.25, 0.32], DARK)]                               # the rib between them
    p += [b([0.06, 0.14, 0.06], [0, 2.4, 0.0], STEEL)]                               # a bead sight
    for i in range(9):                                                               # the cartridge belt on the right side
        y = 0.35 + i * 0.2
        z = 0.02 + 0.12 * math.sin(i * 0.7)
        p += [b([0.05, 0.05, 0.1], [0.4, y, z], "#3a2a18")]
        p += [b([0.07, 0.04, 0.04], [0.42, y, z], BRASS, rz(-15), "metal")]
    return p, face(c)


def revive(c):
    """A slim medical column under a big plus sign, a paddle hung at each side: getting people up."""
    blue = "#26457a"
    p = plinth(WHITE, 0.7, 0.56, 0.08, 0.06)
    for x in (-0.28, 0.28):                                                          # castors
        for z in (-0.16, 0.3):
            p += ring_x(0.05, z, x - 0.02, x + 0.02, 0.05, BLACK, 8)
    p += [b([0.7, 0.06, 0.56], [0, 0.12, 0.06], WHITE)]
    p += ring_y(0, 0.12, 0.15, 2.0, 0.2, WHITE, 8, "metal")                          # the column
    for y in (0.5, 1.2, 1.9):
        p += ring_y(0, 0.12, y - 0.03, y + 0.03, 0.215, c, 8)
    p += [b([0.18, 0.62, 0.16], [0, 2.32, 0.12], WHITE), b([0.62, 0.18, 0.16], [0, 2.32, 0.12], WHITE)]   # the plus
    p += [b([0.12, 0.56, 0.17], [0, 2.32, 0.12], c), b([0.56, 0.12, 0.17], [0, 2.32, 0.12], c)]
    for sx in (-1, 1):                                                               # paddles in their cradles
        p += [b([0.08, 0.3, 0.2], [sx * 0.38, 1.2, 0.1], blue, material="metal")]
        p += [b([0.04, 0.22, 0.16], [sx * 0.43, 1.2, 0.1], STEEL)]
        p += [b([0.03, 0.1, 0.03], [sx * 0.44, 1.4, 0.1], BLACK)]
        p += along([sx * 0.38, 1.05, 0.15], [sx * 0.15, 0.5, 0.2], 0.015, BLACK, 4)  # their leads
    return p, face(c)


def secondwind(c):
    """A pair of bellows folds under two swept wings: the breath that gets you up."""
    leather, board = "#6a3a1a", "#4a2a14"
    p = plinth("#2e2a26", 0.86, 0.64, 0.1, 0.04)
    for i in range(8):                                                               # the folds, wide and narrow in turn
        y = 0.15 + i * 0.2
        w = 0.82 if i % 2 == 0 else 0.66
        d = 0.6 if i % 2 == 0 else 0.48
        p += [b([w, 0.2, d], [0, y + 0.1, 0.06], leather if i % 2 else "#7c4520")]
    p += [b([0.86, 0.08, 0.64], [0, 1.79, 0.06], board), b([0.86, 0.06, 0.64], [0, 0.13, 0.06], board)]   # the boards
    p += along([0, 1.83, 0.2], [0, 2.1, 0.3], 0.06, BRASS, 8, "metal")              # the nozzle
    for sx in (-1, 1):                                                               # the wings, swept up and out
        for k, (want, tilt) in enumerate(((0.75, 22), (0.6, 40), (0.45, 58))):
            t = math.radians(tilt)
            root = 0.08
            ln = min(want, (0.4 - root) / math.sin(t))                                # (inside the footprint)
            cx, cy = sx * (root + ln / 2 * math.sin(t)), 1.86 + ln / 2 * math.cos(t)
            p += [b([0.1, ln, 0.05], [cx, cy, 0.24 - k * 0.06], c if k == 0 else "#e8e0d0", rz(-sx * tilt))]
    return p, face(c)


def fleetfoot(c):
    """A raked aerodynamic tower with tail fins and speed stripes: moving fast."""
    teal = "#1e5a5e"
    p = plinth("#2a2c30", 0.84, 0.66, 0.08, 0.02)
    for i in range(9):                                                               # the body, raked back as it rises
        y = 0.08 + i * 0.25
        z = 0.0 + i * 0.022
        w = 0.8 - i * 0.04
        d = 0.6 - i * 0.035
        p += [b([w, 0.26, d], [0, y + 0.13, z], teal, material="metal")]
    p += [b([0.46, 0.06, 0.28], [0, 2.38, 0.18], c)]                                  # the cap
    for sx in (-1, 1):                                                               # speed stripes down the sides
        for k in range(3):
            p += [b([0.012, 0.06, 0.5], [sx * (0.39 - 0.12 * k / 3), 0.6 + k * 0.5, 0.0 + k * 0.03], c, rx(-25))]
        p += [b([0.04, 0.5, 0.28], [sx * 0.33, 1.95, 0.14], c, mul(rz(-sx * 12), rx(-20)))]  # tail fins
    p += [b([0.04, 0.36, 0.26], [0, 2.47, 0.2], c, rx(-25))]                          # the dorsal fin
    return p, face(c)


def marksman(c):
    """A bullseye on an easel with crosshairs over it: headshots."""
    p = plinth("#2a2a30", 0.8, 0.62, 0.08, 0.04)
    p += [b([0.6, 1.8, 0.3], [0, 0.98, 0.04], "#2b2440", material="metal")]          # the cabinet behind the face
    for sx in (-1, 1):                                                               # the easel's legs
        p += along([sx * 0.38, 0.08, 0.3], [sx * 0.2, 2.2, 0.16], 0.03, WOOD, 8)
    p += along([0, 0.08, 0.38], [0, 2.1, 0.2], 0.03, WOOD, 8)
    y, z = 2.22, 0.09
    for k, (a, col) in enumerate(((0.4, WHITE), (0.32, c), (0.24, WHITE), (0.16, c), (0.08, "#f2c040"))):
        p += ring_z(0, y, z - 0.04 - k * 0.004, z + 0.04, a, col, 16 if k == 0 else 12)
    p += [b([0.86, 0.02, 0.02], [0, y, z - 0.07], DARK), b([0.02, 0.86, 0.02], [0, y, z - 0.07], DARK)]   # the crosshair
    p += [b([0.04, 0.04, 0.12], [0.1, y + 0.05, z - 0.06], COPPER, rx(10))]          # a dart in the gold
    return p, face(c)


def bloodroot(c):
    """A gnarled stump on splayed roots, sprouting dark leaves and red berries: life drawn from a fight."""
    bark, dbark, leaf = "#4a3020", "#33200f", "#2a4a20"
    p = ring_y(0, 0.08, 0.0, 1.8, 0.27, bark)                                        # the trunk
    for yaw in (50, 110, 180, 250, 310):                                             # bark ridges
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        p += [b([0.05, 1.5, 0.04], [0.27 * s, 0.85, 0.08 - 0.27 * co], dbark, ry(-yaw))]
    for yaw in (40, 100, 160, 200, 260, 320):                                        # the roots
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        ex, ez = max(-0.36, min(0.36, 0.4 * s)), max(-0.3, min(0.3, 0.08 - 0.34 * co))
        p += along([0.2 * s, 0.4, 0.08 - 0.2 * co], [ex, 0.05, ez], 0.06, dbark, 6)
    p += ring_y(0, 0.08, 1.8, 1.86, 0.28, "#9a6a40")                                 # the cut top
    p += ring_y(0, 0.08, 1.5, 1.56, 0.275, c)                                        # a band of sap
    for (tx, ty, tz) in ((-0.3, 2.42, 0.18), (0.28, 2.46, 0.12), (0.0, 2.55, 0.24)):   # branches
        p += along([tx * 0.3, 1.85, 0.12], [tx, ty, tz], 0.04, bark, 6)
        p += [b([0.2, 0.1, 0.16], [tx, ty + 0.04, tz], leaf, ry(30))]
        p += [b([0.07, 0.07, 0.07], [tx + 0.06, ty - 0.05, tz - 0.05], c)]           # berries
        p += [b([0.06, 0.06, 0.06], [tx - 0.06, ty - 0.02, tz + 0.05], c)]
    return p, face(c, "#24160c")


def windfall(c):
    """A gumball globe on a stepped base with a coin dial: luck falls out."""
    red = "#a8262a"
    p = ring_y(0, 0.04, 0, 0.12, 0.37, red, material="metal")
    p += ring_y(0, 0.04, 0.12, 0.6, 0.33, red, material="metal")                     # the stepped base
    p += ring_y(0, 0.04, 0.6, 0.66, 0.35, c, material="metal")
    p += ring_y(0, 0.04, 0.66, 1.86, 0.2, red, material="metal")                     # the neck (behind the sign)
    p += ring_y(0, 0.04, 1.86, 1.92, 0.3, c, material="metal")                       # the globe's collar
    gy, ga = 2.24, 0.34
    for i in range(5):                                                               # the glass globe
        t0, t1 = -1 + i * 0.4, -1 + (i + 1) * 0.4
        mid = (t0 + t1) / 2
        p += ring_y(0, 0.04, gy + t0 * ga * 0.95, gy + t1 * ga * 0.95, ga * math.sqrt(max(0.05, 1 - mid * mid)), GLASS, 12)
    balls = ["#e03030", "#30a0e0", "#f0d030", "#40c060", "#e070c0", "#ff9030"]
    for k in range(10):                                                              # gumballs against the glass
        yaw = k * 37 + 15
        if abs(((yaw + 180) % 360) - 180) < 30 or abs((yaw % 360) - 180) < 50:
            continue  # (none over the sign, none at the back)
        y = gy - 0.15 + (k % 3) * 0.12
        r = ga * math.sqrt(max(0.05, 1 - ((y - gy) / ga) ** 2)) + 0.005
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        p += [b([0.08, 0.08, 0.08], [r * s, y, 0.04 - r * co], balls[k % len(balls)], ry(-yaw))]
    p += ring_y(0, 0.04, gy + ga * 0.95, gy + ga * 0.95 + 0.06, 0.12, c, 12, "metal")  # the cap
    p += ring_y(0, 0.04, gy + ga * 0.95 + 0.06, gy + ga * 0.95 + 0.14, 0.04, CHROME, 8)
    p += ring_z(0, 0.35, -0.38, -0.3, 0.1, CHROME)                                   # the coin dial
    p += [b([0.14, 0.03, 0.03], [0, 0.35, -0.39], DARK)]
    return p, face(c)


def hammerhand(c):
    """A stack of planks, cross-braced and nailed, a big hammer lying on top: boards up."""
    planks = ["#7a5230", "#6b4524", "#80582f", "#5e3c20"]
    p = []
    for i in range(10):                                                              # the stack
        y = 0.1 + i * 0.19
        jog = (0.02 if i % 2 else -0.02)
        p += [b([0.8, 0.18, 0.58], [jog, y, 0.04 + jog * 0.5], planks[i % len(planks)], ry(1.5 if i % 3 == 0 else -1))]
    for sx in (-1, 1):                                                               # X braces on the sides
        p += [b([0.04, 1.7, 0.08], [sx * 0.42, 0.98, 0.04], DWOOD, rx(14)), b([0.04, 1.7, 0.08], [sx * 0.425, 0.98, 0.04], DWOOD, rx(-14))]
        for y in (0.3, 0.98, 1.66):
            p += [b([0.03, 0.03, 0.03], [sx * 0.445, y, 0.04], STEEL)]               # nail heads
    # The hammer: handle across the top, the head at the right.
    p += [b([0.78, 0.07, 0.07], [-0.02, 1.97, 0.1], WOOD, ry(8))]
    p += [b([0.14, 0.16, 0.36], [0.33, 2.03, 0.12], GUN, ry(8), "metal")]
    p += [b([0.1, 0.1, 0.12], [0.34, 2.03, -0.1], STEEL, ry(8))]                   # its face
    p += [b([0.06, 0.06, 0.12], [0.32, 2.05, 0.31], STEEL, mul(ry(8), rx(25)))]      # its claw
    p += [b([0.12, 0.08, 0.08], [-0.34, 1.97, 0.05], c, ry(8))]                      # the grip wrap
    return p, face(c, "#24180e")


def salvage(c):
    """A scrap hopper on legs, junk sticking out of it, a gear on its side: parts from anything."""
    olive, dolive = "#4a5232", "#363c24"
    p = []
    for x in (-0.36, 0.36):                                                          # legs
        for z in (-0.26, 0.32):
            p += [b([0.06, 1.3, 0.06], [x, 0.65, z], STEEL, material="metal")]
    p += [b([0.7, 1.0, 0.5], [0, 0.72, 0.06], olive, material="metal")]              # the bin
    # The hopper: four plates leaning out, wider at the top.
    p += [b([0.76, 0.9, 0.05], [0, 1.75, 0.27], dolive, rx(-12), "metal"), b([0.76, 0.9, 0.05], [0, 1.75, -0.18], dolive, rx(12), "metal")]
    p += [b([0.05, 0.9, 0.46], [-0.36, 1.75, 0.05], dolive, rz(-6), "metal"), b([0.05, 0.9, 0.46], [0.36, 1.75, 0.05], dolive, rz(6), "metal")]
    p += [b([0.7, 0.05, 0.46], [0, 1.32, 0.05], olive)]
    for (x, y, z, ang, col) in ((-0.15, 2.25, 0.1, [20, 30, 15], STEEL), (0.18, 2.3, 0.0, [-30, 10, 40], COPPER),     # junk
                                (0.0, 2.35, 0.2, [10, 60, -25], GUN), (-0.25, 2.2, 0.25, [45, 0, 10], "#8a2020"),
                                (0.25, 2.18, 0.26, [0, 20, 70], BRASS)):
        p += [b([0.28, 0.06, 0.1], [x, y, z], col, ang)]
    p += ring_x(0.95, 0.12, -0.44, -0.37, 0.18, c, 12, "metal")                     # the gear on the left side
    for i in range(10):
        a = i * 36
        s, co = math.sin(math.radians(a)), math.cos(math.radians(a))
        p += [b([0.07, 0.06, 0.06], [-0.405, 0.95 + 0.2 * co, 0.12 + 0.2 * s], c, rx(-a), "metal")]
    p += ring_x(0.95, 0.12, -0.46, -0.36, 0.05, STEEL, 8)
    return p, face(c)


def bulwark(c):
    """A heater shield standing on its point, a helmet on top: an armour plate."""
    slate, rim = "#334a66", STEEL
    p = plinth("#2a2c30", 0.7, 0.5, 0.08, 0.12)
    p += [b([0.12, 1.6, 0.2], [0, 0.9, 0.24], GUN, material="metal")]                # the stand behind it
    # The shield: full width above y 0.9, narrowing to its point at y 0.15.
    p += [b([0.88, 1.32, 0.1], [0, 1.56, -0.2], slate, material="metal")]
    for i in range(5):
        y0 = 0.15 + i * 0.15
        w = 0.15 + 0.73 * ((i + 1) / 5) ** 0.8
        p += [b([w, 0.15, 0.1], [0, y0 + 0.075, -0.2], slate, material="metal")]
    for sx in (-1, 1):                                                               # its rim
        p += [b([0.04, 1.34, 0.12], [sx * 0.44, 1.56, -0.2], rim, material="metal")]
        p += [b([0.04, 0.86, 0.12], [sx * 0.24, 0.53, -0.2], rim, rz(sx * 27), "metal")]
    p += [b([0.9, 0.05, 0.12], [0, 2.22, -0.2], rim, material="metal")]
    p += ring_z(0, 2.08, -0.27, -0.25, 0.08, c)                                      # a stud over the sign
    p += ring_y(0, 0.05, 2.25, 2.3, 0.22, GUN, 12, "metal")                         # the helmet on top
    p += dome(0, 0.05, 2.3, 0.21, 4, slate)
    p += [b([0.04, 0.24, 0.42], [0, 2.4, 0.05], c)]                                  # its crest
    p += [b([0.3, 0.05, 0.04], [0, 2.36, -0.17], DARK)]                              # the eye slit
    return p, face(c)


# key, body builder, a short description of the body.
MACHINES = [
    ("jugg", jugg, "a riveted pressure tank with a domed top, a valve wheel and a gauge"),
    ("speed", speed, "a revolver cylinder standing on end, a speed loader on its pin and the crane arm swung out"),
    ("doubletap", doubletap, "twin barrels bound side by side with a cartridge belt down one side"),
    ("revive", revive, "a slim medical column on castors under a big plus sign, a paddle hung at each side"),
    ("secondwind", secondwind, "a pair of bellows folds under two swept wings, a brass nozzle on top"),
    ("fleetfoot", fleetfoot, "a raked aerodynamic tower with tail fins and speed stripes"),
    ("marksman", marksman, "a bullseye on an easel, crosshairs over it and a dart in the gold"),
    ("bloodroot", bloodroot, "a gnarled stump on splayed roots, sprouting dark leaves and red berries"),
    ("windfall", windfall, "a gumball globe on a stepped base with a coin dial"),
    ("hammerhand", hammerhand, "a stack of planks, cross-braced and nailed, with a big hammer lying on top"),
    ("salvage", salvage, "a scrap hopper on legs with junk sticking out of it and a gear on its side"),
    ("bulwark", bulwark, "a heater shield standing on its point with a helmet on top"),
]


def power_box():
    """The power switch: a breaker panel with ceramic insulators and cables. The game draws the lever (pivot y 1.1,
    z -0.18) and the lamp (y 1.48, z -0.14) on its front plate (front z -0.125), as on the old box."""
    grey = "#4d5258"
    p = [b([0.7, 1.0, 0.25], [0, 1.1, 0], grey, material="metal")]                  # the panel (as before: the lever's place)
    p += [b([0.72, 0.06, 0.26], [0, 1.48, 0], "yellow")]                             # the warning stripe
    for x in (-0.3, -0.18, 0.18, 0.3):
        p += [b([0.04, 0.6, 0.012], [x, 0.98, -0.127], "#3a3e44")]                   # the breaker rows
    p += [b([0.2, 0.12, 0.01], [0.2, 0.88, -0.128], "red")]
    p += [b([0.76, 0.06, 0.28], [0, 1.63, 0], "#3d4248", material="metal")]          # the top rail
    for x in (-0.24, 0, 0.24):                                                       # ceramic insulators and their leads
        for k in range(3):
            p += ring_y(x, 0.0, 1.66 + k * 0.07, 1.71 + k * 0.07, 0.06 - k * 0.008, "#d8d0c0", 8)
        p += ring_y(x, 0.0, 1.87, 1.9, 0.02, COPPER, 8)
        p += along([x, 1.9, 0.0], [x * 0.6, 2.2, 0.08], 0.015, BLACK, 4)
    p += [b([0.08, 0.6, 0.08], [0.22, 0.3, 0.04], "galvanized"), b([0.08, 0.6, 0.08], [-0.22, 0.3, 0.04], "galvanized")]  # conduits
    p += [b([0.6, 0.05, 0.3], [0, 0.025, 0], "darkgrey")]
    p += ring_z(-0.22, 1.32, -0.14, -0.125, 0.07, WHITE)                             # a gauge
    p += [b([0.08, 0.012, 0.005], [-0.2, 1.33, -0.143], BLACK, rz(30))]
    return p


def write(fn, prop):
    parts_json = ",\n".join("    " + json.dumps(q) for q in prop.pop("parts"))
    head = "{\n" + ",\n".join(f"  {json.dumps(k)}: {json.dumps(v)}" for k, v in prop.items())
    with open(fn, "w") as f:
        f.write(head + ',\n  "parts": [\n' + parts_json + "\n  ]\n}\n")


def main():
    rules = json.load(open(RULES))
    perks = {p["key"]: p for p in rules["perks"]}
    mine = {k for k, *_ in MACHINES}
    if set(perks) != mine:
        raise SystemExit(f"perks without a machine: {sorted(set(perks) - mine)}; machines without a perk: {sorted(mine - set(perks))}")
    os.makedirs(OUT, exist_ok=True)
    for key, build, what in MACHINES:
        perk = perks[key]
        body, front = build(perk["color"])
        parts = body + front
        assert len(parts) <= 128, f"{key}: {len(parts)} parts (props take 128)"
        lo, hi = bounds(parts)
        # The plain Perk Machine's footprint (0.9 x 0.7, a hair over for a knob): Blackmoor's layout checks and the
        # self-test "Zombies more perks" assume it.
        if not (lo[0] >= -0.47 and hi[0] <= 0.47 and lo[2] >= -0.41 and hi[2] <= 0.41):
            out = [q for q in parts if (lambda l, h: l[0] < -0.47 or h[0] > 0.47 or l[2] < -0.41 or h[2] > 0.41)(*bounds([q]))]
            raise SystemExit(f"{key}: bounds {lo} to {hi}; outside: {out[:4]}")
        # Nothing of the body in front of the face (the sign, lamps and dispenser are drawn there).
        for q in body:
            ql, qh = bounds([q])
            if ql[2] < -0.335 and qh[1] > 0.55 and ql[1] < 2.0 and ql[0] < 0.4 and qh[0] > -0.4:
                raise SystemExit(f"{key}: a body part pokes through the face: {q}")
        top = hi[1]
        name = key[0].upper() + key[1:] + " Perk Machine"
        write(os.path.join(OUT, key[0].upper() + key[1:] + "PerkMachine.json"), {
            "format": "call-of-slop-prop/1",
            "name": name,
            "category": "Zombies",
            "description": f"The Zombies perk machine for {perk['name']}: {what}, with the perk machines' face (sign board, lamp bezels, dispenser). The front faces -Z; the game paints the perk's lit sign and lamps on it.",
            "collision": "solid",
            "collider": [{"size": [0.9, round(min(top, 2.6), 2), 0.7]}],
            "scatter": {"randomYaw": False, "yawStep": 90},
            "parts": parts,
        })
    pw = power_box()
    lo, hi = bounds(pw)
    assert lo[0] >= -0.4 and hi[0] <= 0.4 and lo[2] >= -0.16 and hi[2] <= 0.16, f"power box bounds {lo} to {hi}"
    write(os.path.join(ROOT, "Props", "Zombies", "PowerSwitchBox.json"), {
        "format": "call-of-slop-prop/1",
        "name": "Power Switch Box",
        "category": "Zombies",
        "description": "The Zombies power switch: a grey breaker panel with a warning stripe, breaker rows, a gauge, three ceramic insulators with their leads, and conduit. The game draws the lever and the lamp on its -Z face. (Written by tools/zombies/make_perk_machines.py.)",
        "collision": "solid",
        "collider": [{"size": [0.7, 1.9, 0.3]}],
        "scatter": {"randomYaw": False, "yawStep": 90},
        "parts": pw,
    })
    print(f"wrote {len(MACHINES)} perk machines to {OUT} and the Power Switch Box")


if __name__ == "__main__":
    main()
