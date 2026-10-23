#!/usr/bin/env python3
"""Writes the reusable sci-fi industrial prop set: Props/Space/Hollowrock (category "Space"), for the Space pack's asteroid
refinery and terraforming processor maps and any other off-world industrial map: an ore crusher, conveyor belt runs,
docking clamps, ore heaps, slag pots, a rock drill rig, a giant intake fan, pipe valve stacks, an atmosphere sensor
pole, a condenser unit, a warning light mast, a hab container and a reagent sphere. Original designs only (no film or
game ships, names or insignia).

    python3 tools/space/hollowrock_redvane_props.py            (run from the repository root)

Every one of them is animated, so they're all shown on the Dev pack's Animated Props map (a row at z = 37, added by hand
to Maps/Dev/AnimatedProps.json). Uses the box helpers of tools/propviz/gen.py. Health follows the material health guide in Props/README.md: machines and
structures are unbreakable cover; the few small things that break are heavy metal.
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
STEEL2 = "#85888c"
DARK = "#34363a"
RUST = "#7a5238"
OCHRE = "#b0804a"
HULL = "#c9bda6"
HULL2 = "#a89b84"
AMBER = "#ffb24a"
EMBER = "#ff6a1e"
TEAL = "#6fe0e8"
RED = "#ff4a3a"
ROCK = "#5a544e"
ORE = "#4a4642"
BELT = "#2b2b2e"
SAFETY = "#e0b030"

SQ = {"randomYaw": False, "yawStep": 90, "yawJitter": 3}
ANY = {"randomYaw": True, "scale": [0.95, 1.05]}


def prop(name, desc, parts, **kw):
    gen.prop(name, CAT, desc, parts, **kw)  # category "Space" whatever the subfolder
    PROPS[-1]["author"] = AUTHOR


def anim(part, a):
    part = dict(part)
    part["anim"] = a
    return part


def glow(kind="pulse", color=AMBER, period=2.4, glow_=1.3, **kw):
    a = {"type": kind, "color": color, "glow": glow_}
    if kind in ("pulse", "blink", "cycle"):
        a["period"] = period
    a.update(kw)
    return a


def spin(axis, pivot, rpm):
    return {"type": "spin", "axis": axis, "pivot": [round(v, 3) for v in pivot], "rpm": rpm}


# ------------------------------------------------------------------ refinery machines ------------------------------------------------------------------

def crusher():
    """A jaw crusher the size of a van: a hopper mouth on top, a flywheel each side, a shaking body, an ore chute."""
    p = [At([4.0, 0.3, 3.0], 0, color=DARK),                                                   # bed
         anim(At([3.4, 2.6, 2.4], 0.3, color=STEEL, mat="metal"), {"type": "shake", "axis": [0.3, 1, 0.2], "distance": 0.01, "period": 0.09}),
         At([3.6, 0.5, 2.6], 2.9, color=RUST, mat="metal")]                                      # hopper lip
    # Hopper: four leaning plates round the mouth.
    for s in (-1, 1):
        p.append(B([3.6, 1.2, 0.12], [0, 3.95, s * 1.45], color=RUST, mat="metal", pitch=-s * 18))
        p.append(B([0.12, 1.2, 2.9], [s * 1.95, 3.95, 0], color=RUST, mat="metal", roll=s * 18))
    # Flywheels on an axle through the body.
    p += HCyl(0.3, 5.0, 1.9, color=DARK)
    for s in (-1, 1):
        x = s * 2.25
        for a in (0, 45, 90, 135):
            p.append(anim(B([0.22, 1.8, 0.75], [x, 1.9, 0], color=STEEL2, mat="metal", pitch=a), spin("x", [x, 1.9, 0], 40)))
        p.append(anim(B([0.26, 0.5, 0.5], [x, 1.9, 0], color=DARK), spin("x", [x, 1.9, 0], 40)))
    # Chute out of the front, a motor at the back.
    p.append(B([1.6, 0.12, 1.2], [0, 0.9, 1.9], color=STEEL2, mat="metal", pitch=-25))
    p.append(At([1.2, 0.9, 0.9], 0.3, 0, -1.6, color=OCHRE, mat="metal"))
    p.append(anim(At([0.14, 0.14, 0.06], 1.3, 0.5, -2.08, color="#5aff7a"), glow("blink", "#5aff7a", 1.2, duty=0.5, glow_=1.4)))
    prop("Ore Crusher", "A van-sized jaw crusher: a hopper mouth on top, a flywheel each side, a chute out of the front. Shakes while it runs.",
         p, collider=[C([3.6, 3.2, 2.6], [0, 1.6, 0]), C([3.6, 1.4, 3.0], [0, 3.6, 0]), C([5.0, 1.0, 0.8], [0, 1.9, 0])], scatter=SQ)


def conveyor():
    """A 4 m belt run on legs, rollers turning, a few lumps of ore riding it. Long along X; solid, waist high."""
    p = [At([4.0, 0.08, 1.1], 0.95, color=BELT),                                                   # belt
         At([4.0, 0.08, 0.1], 1.0, 0, -0.6, color=STEEL, mat="metal"), At([4.0, 0.08, 0.1], 1.0, 0, 0.6, color=STEEL, mat="metal"),
         At([4.0, 0.15, 1.3], 0.78, color=DARK)]                                                 # frame
    for x in (-1.6, 1.6):
        for z in (-0.5, 0.5):
            p.append(At([0.12, 0.78, 0.12], 0, x, z, color=STEEL))
    for i in range(5):
        x = -1.6 + i * 0.8
        for a in (0, 45, 90, 135):
            p.append(anim(B([0.16, 0.26, 1.2], [x, 1.1, 0], color=STEEL2, mat="metal", roll=a), spin("z", [x, 1.1, 0], 36)))
    for i, (x, z, s) in enumerate([(-1.3, 0.1, 0.3), (-0.5, -0.2, 0.26), (0.3, 0.15, 0.34), (1.2, -0.1, 0.28)]):
        p.append(B([s, s * 0.8, s], [x, 1.16, z], color=ORE if i % 2 else ROCK, yaw=30 * i))
    prop("Conveyor Belt Run", "A 4 m conveyor belt on legs, rollers turning, ore riding it: waist-high cover. Long along X.",
         p, collider=[C([4.0, 1.2, 1.3], [0, 0.6, 0])], scatter={"randomYaw": False, "yawStep": 90})


def docking_clamp():
    """A dock's holding clamp: a squat base, a hydraulic column and two jaws reaching up, a blinking beacon."""
    p = [At([2.2, 0.5, 2.2], 0, color=DARK), At([1.4, 0.3, 1.4], 0.5, color=SAFETY)]
    p += Cyl(0.9, 2.4, 0.8, color=STEEL, mat="metal")
    p += Cyl(0.5, 1.2, 3.2, color=STEEL2, mat="metal")
    for s in (-1, 1):
        p.append(B([0.4, 1.8, 0.5], [s * 0.75, 4.9, 0], color=OCHRE, mat="metal", roll=-s * 12))
        p.append(B([0.5, 0.4, 0.6], [s * 1.0, 5.75, 0], color=DARK))
    p.append(anim(At([0.2, 0.2, 0.2], 4.4, 0, 0, color=AMBER), glow("blink", AMBER, 1.8, duty=0.3, glow_=1.8)))
    for z in (-0.95, 0.95):
        p.append(At([0.6, 0.08, 0.08], 0.5, 0, z, color=SAFETY))
    prop("Docking Clamp", "A docking clamp: a hydraulic column on a hazard-banded base, two jaws open to the sky, a blinking beacon.",
         p, collider=[C([2.2, 0.8, 2.2]), C([0.9, 3.2, 0.9], [0, 2.4, 0])], scatter=SQ)


def ore_heap():
    """A heap of mined ore with glowing veins: chest-high rocky cover."""
    p = []
    rocks = [(0, 0, 2.6, 1.3, 2.2, 0), (-0.9, 0.4, 1.4, 0.9, 1.2, 35), (0.9, -0.3, 1.3, 0.8, 1.1, 60), (0.2, 0.8, 1.0, 0.6, 0.9, 15),
             (-0.3, -0.9, 1.1, 0.5, 0.8, 80), (1.1, 0.7, 0.8, 0.45, 0.7, 50), (-1.3, -0.3, 0.7, 0.4, 0.6, 20)]
    for i, (x, z, w, h, d, yaw) in enumerate(rocks):
        p.append(At([w, h, d], 0, x, z, color=ROCK if i % 2 == 0 else ORE, yaw=yaw))
    for x, z, y in [(-0.4, 0.3, 1.0), (0.6, -0.5, 0.8), (0.1, -0.1, 1.3)]:
        p.append(anim(B([0.5, 0.08, 0.14], [x, y, z], color=EMBER, yaw=40), glow("pulse", EMBER, 3.0, glow_=1.1)))
    prop("Ore Heap", "A heap of mined ore, veins of it glowing faintly: chest-high rocky cover.", p,
         collider=[C([2.6, 1.3, 2.2]), C([1.4, 0.9, 1.2], [-0.9, 0.45, 0.4])], scatter={"randomYaw": True, "scale": [0.8, 1.2]})


def slag_pot():
    """A crucible of molten slag on a cradle, its surface glowing."""
    p = [At([1.9, 0.25, 1.9], 0, color=DARK)]
    p += Cyl(1.5, 1.2, 0.25, color=STEEL, mat="metal")
    p += Cyl(1.65, 0.14, 1.3, color=RUST)
    p.append(anim(At([1.1, 0.06, 1.1], 1.38, color=EMBER, yaw=22.5), glow("flicker", EMBER, glow_=1.8, rate=6, duty=0.9)))
    p.append(anim(At([1.1, 0.06, 1.1], 1.38, color=EMBER), glow("flicker", EMBER, glow_=1.8, rate=6, duty=0.9)))
    for s in (-1, 1):
        p.append(At([0.2, 0.9, 0.2], 0.25, s * 0.95, 0, color=DARK))
    prop("Slag Pot", "A crucible of molten slag on a cradle, its surface glowing and flickering. Chest-high cover; don't touch.",
         p, collider=[C([1.7, 1.45, 1.7])], scatter=ANY)


def drill_rig():
    """A tracked rock drill: a mast leaning over a boring drill rod that hammers up and down."""
    p = []
    for z in (-0.95, 0.95):
        p.append(At([2.8, 0.5, 0.6], 0, 0, z, color=DARK, mat="metal"))
    p.append(At([2.4, 1.0, 1.9], 0.5, color=OCHRE, mat="metal"))
    p.append(At([1.0, 0.9, 1.5], 1.5, -0.5, color=STEEL, mat="metal"))                      # cab
    p.append(At([0.9, 0.5, 1.3], 1.6, -0.5, color="#2e4450"))                                  # cab glass
    p.append(B([0.4, 3.4, 0.4], [1.1, 2.5, 0], color=STEEL2, mat="metal", roll=-15))         # mast
    p.append(anim(B([0.16, 2.2, 0.16], [1.65, 1.9, 0], color=DARK, roll=-15), {"type": "bob", "axis": [0.26, 0.97, 0], "distance": 0.2, "period": 0.5}))
    p.append(anim(At([0.14, 0.14, 0.14], 1.6, -0.5, 1.0, color=AMBER), glow("blink", AMBER, 1.0, duty=0.5, glow_=1.5)))
    prop("Rock Drill Rig", "A tracked rock drill: a cab, a leaning mast and a drill rod hammering at the ground.",
         p, collider=[C([2.8, 1.5, 2.5], [0, 0.75, 0]), C([1.0, 1.0, 1.5], [-0.5, 2.0, 0])], scatter=SQ)


# ------------------------------------------------------------------ processor machines ------------------------------------------------------------------

def intake_fan():
    """A giant atmosphere intake: a 10 x 10 m housing with four turning blades behind a grille. Face on +-Z."""
    S = 10.0
    T = 2.4
    p = [At([S, 0.6, T], 0, color=DARK),                                                         # sill
         At([S, 0.6, T], S - 0.6, color=DARK),                                                   # lintel
         At([0.6, S, T], 0, -S / 2 + 0.3, color=DARK), At([0.6, S, T], 0, S / 2 - 0.3, color=DARK)]
    # Back plate and the housing sides (rust streaked).
    p.append(At([S - 1.2, S - 1.2, 0.3], 0.6, 0, 0, color=STEEL, mat="metal"))
    for s in (-1, 1):
        p.append(At([S - 1.2, 0.3, T - 0.6], 0.6 + (S - 1.5) if s > 0 else 0.6, 0, 0, color=STEEL2, mat="metal"))
    # Hub and blades.
    cy = S / 2
    p += HCyl(1.6, 1.2, cy, 0, 0, color=OCHRE, mat="metal", alongZ=True)
    for a in (0, 45, 90, 135):
        p.append(anim(B([0.9, S - 2.2, 0.12], [0, cy, 0.5], color=STEEL2, mat="metal", roll=a + 10), spin("z", [0, cy, 0.5], 18)))
    # Grille bars on both faces and a hazard band.
    for i in range(7):
        x = -S / 2 + 1.2 + i * (S - 2.4) / 6
        for z in (-T / 2 + 0.05, T / 2 - 0.05):
            p.append(At([0.08, S - 1.2, 0.08], 0.6, x, z, color=DARK))
    for z in (-T / 2 - 0.02, T / 2 + 0.02):
        p.append(At([S, 0.3, 0.04], 0.6, 0, z, color=SAFETY))
    p.append(anim(At([0.3, 0.3, 0.3], S - 0.3, 0, 0, color=RED), glow("blink", RED, 2.4, duty=0.15, glow_=2.0)))
    prop("Intake Fan", "A giant atmosphere intake: a 10 m square housing with four slow blades behind a grille, a red beacon on top. Wall-sized cover, face on Z.",
         p, collider=[C([S, S, T], [0, S / 2, 0])], scatter={"randomYaw": False, "yawStep": 90})


def valve_stack():
    """A cluster of pipe valves and gauges on a manifold."""
    p = [At([1.4, 0.25, 0.8], 0, color=DARK)]
    p += HCyl(0.4, 1.4, 0.7, color=STEEL, mat="metal")
    for i, x in enumerate((-0.45, 0, 0.45)):
        p += Cyl(0.26, 0.9, 0.7, x, 0, color=STEEL2, mat="metal")
        for a in (0, 90):
            p.append(B([0.42, 0.06, 0.06], [x, 1.68, 0], color=RED if i == 1 else SAFETY, yaw=a))
        p.append(At([0.22, 0.22, 0.05], 1.15, x, 0.2, color="#dcdcd0"))
    p.append(anim(At([0.1, 0.1, 0.1], 1.95, 0, 0, color=TEAL), glow("pulse", TEAL, 2.0, glow_=1.2)))
    prop("Pipe Valve Stack", "Three valves on a manifold with gauges and a status light: chest-high cover.",
         p, collider=[C([1.4, 1.75, 0.8], [0, 0.875, 0])], breakable=brk(320, "metal", count=10, size=(0.05, 0.2)), scatter=ANY)


def sensor_pole():
    """An atmosphere sensor pole: an anemometer spinning on top, instrument boxes, a solar panel."""
    p = [At([0.6, 0.2, 0.6], 0, color=DARK), At([0.12, 4.2, 0.12], 0.2, color=STEEL2, mat="metal"),
         At([0.4, 0.3, 0.25], 2.0, 0, 0.1, color=HULL, mat="metal"), At([0.6, 0.04, 0.4], 3.0, 0.2, 0, color="#2e4450", pitch=20)]
    for a in (0, 90):
        p.append(anim(B([0.6, 0.04, 0.04], [0, 4.5, 0], color=STEEL, yaw=a), spin("y", [0, 4.5, 0], 60)))
    for a in (0, 90, 180, 270):
        import math
        p.append(anim(B([0.12, 0.12, 0.12], [0.3 * math.cos(math.radians(a)), 4.5, -0.3 * math.sin(math.radians(a))], color=DARK), spin("y", [0, 4.5, 0], 60)))
    p.append(anim(At([0.08, 0.08, 0.08], 4.3, 0, 0, color=TEAL), glow("blink", TEAL, 3.0, duty=0.2, glow_=1.4)))
    prop("Atmos Sensor Pole", "An atmosphere sensor pole: an anemometer spinning on top, an instrument box and a solar panel.",
         p, collider=[C([0.6, 4.4, 0.6], [0, 2.2, 0])], scatter=ANY)


def condenser():
    """A condenser unit: a finned block with a fan turning in its top, pipes out of one end."""
    p = [At([3.0, 0.2, 1.6], 0, color=DARK), At([2.8, 1.9, 1.4], 0.2, color=STEEL, mat="metal")]
    for i in range(9):
        p.append(At([0.04, 1.7, 1.5], 0.3, -1.2 + i * 0.3, 0, color=STEEL2))
    p.append(At([1.3, 0.2, 1.3], 2.1, 0.5, 0, color=DARK))
    for a in (0, 45, 90, 135):
        p.append(anim(B([1.1, 0.04, 0.2], [0.5, 2.25, 0], color=STEEL2, yaw=a), spin("y", [0.5, 2.25, 0], 150)))
    p += HCyl(0.25, 0.8, 1.0, x=-1.75, color=STEEL, mat="metal")
    p += HCyl(0.25, 0.8, 1.5, x=-1.75, color=STEEL, mat="metal")
    p.append(anim(At([0.1, 0.1, 0.05], 1.6, 1.0, 0.72, color=AMBER), glow("pulse", AMBER, 2.8, glow_=1.0)))
    prop("Condenser Unit", "A finned condenser block with a fan in its top and pipes out of one end: head-high cover. Long along X.",
         p, collider=[C([3.0, 2.3, 1.6], [0, 1.15, 0])], scatter=SQ)


def warning_mast():
    """A tall warning light mast: a lattice post with an amber strobe and a loud hailer."""
    p = [At([0.9, 0.3, 0.9], 0, color=DARK)]
    for a in (0, 90):
        p.append(At([0.36, 5.0, 0.08], 0.3, color=STEEL2, yaw=a))
    p.append(At([0.5, 0.5, 0.5], 5.3, color=DARK))
    p.append(anim(At([0.36, 0.36, 0.36], 5.8, color=AMBER), glow("blink", AMBER, 1.2, duty=0.25, glow_=2.2)))
    p.append(B([0.5, 0.35, 0.35], [0.5, 4.8, 0], color=STEEL, pitch=15))
    prop("Warning Light Mast", "A lattice mast with an amber strobe and a loud hailer on top.",
         p, collider=[C([0.9, 5.6, 0.9], [0, 2.8, 0])], scatter=SQ)


def hab_container():
    """A sci-fi cargo and habitat container: 6 m long, a hatch with a lit frame on one end."""
    p = [At([6.0, 0.2, 2.6], 0, color=DARK), At([6.0, 2.5, 2.4], 0.2, color=HULL, mat="metal")]
    for x in (-2.9, -1.0, 1.0, 2.9):
        p.append(At([0.14, 2.6, 2.5], 0.15, x, 0, color=HULL2, mat="metal"))
    p.append(At([6.04, 0.1, 2.44], 2.7, color=HULL2))
    p.append(At([0.08, 1.9, 1.0], 0.3, 3.03, 0, color=DARK))                                        # hatch
    p.append(anim(At([0.06, 2.0, 1.1], 0.25, 3.05, 0, color=TEAL), glow("pulse", TEAL, 2.6, glow_=0.9)))
    p.append(At([0.08, 1.9, 1.0], 0.3, 3.08, 0, color=DARK))
    p.append(At([1.6, 0.8, 0.06], 1.2, 0, 1.23, color="#2e4450"))                                   # window
    p.append(At([1.6, 0.8, 0.06], 1.2, 0, -1.23, color="#2e4450"))
    prop("Hab Container", "A 6 m cargo and habitat container with a lit hatch at one end and a window each side. Long along X.",
         p, collider=[C([6.0, 2.8, 2.6], [0, 1.4, 0])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4})


def reagent_sphere():
    """A terraforming reagent sphere on four legs, a pulsing band round its middle."""
    p = [At([2.6, 0.2, 2.6], 0, color=DARK)]
    for x, z in [(-0.9, -0.9), (0.9, -0.9), (-0.9, 0.9), (0.9, 0.9)]:
        p.append(At([0.2, 1.3, 0.2], 0.2, x, z, color=STEEL))
    # The sphere: three octagonal drums of decreasing size, mirrored.
    for d, h, y in [(2.0, 0.4, 1.4), (2.5, 0.5, 1.8), (2.8, 0.7, 2.3), (2.5, 0.5, 3.0), (2.0, 0.4, 3.5), (1.3, 0.3, 3.9)]:
        p += Cyl(d, h, y, color=HULL, mat="metal")
    p.append(anim(At([2.92, 0.16, 2.92], 2.55, color=TEAL, yaw=22.5), glow("pulse", TEAL, 3.2, glow_=1.2)))
    p.append(anim(At([2.92, 0.16, 2.92], 2.55, color=TEAL), glow("pulse", TEAL, 3.2, glow_=1.2)))
    p += Cyl(0.4, 0.8, 4.2, color=STEEL2, mat="metal")
    prop("Reagent Sphere", "A spherical reagent tank on four legs with a pulsing band round its middle: solid cover you can duck under the edge of.",
         p, collider=[C([2.0, 1.3, 2.0], [0, 0.65, 0]), C([2.8, 2.8, 2.8], [0, 2.8, 0])], scatter=ANY)


def write(out):
    folder = os.path.join(out, CAT, "Hollowrock")
    os.makedirs(folder, exist_ok=True)
    for p in PROPS:
        fn = "".join(ch for ch in p["name"] if ch.isalnum()) + ".json"
        with open(os.path.join(folder, fn), "w") as f:
            json.dump(p, f, indent=1)
            f.write("\n")
    print(f"wrote {len(PROPS)} props to {folder}")


if __name__ == "__main__":
    crusher()
    conveyor()
    docking_clamp()
    ore_heap()
    slag_pot()
    drill_rig()
    intake_fan()
    valve_stack()
    sensor_pole()
    condenser()
    warning_mast()
    hab_container()
    reagent_sphere()
    write(os.path.join(REPO, "Props"))
