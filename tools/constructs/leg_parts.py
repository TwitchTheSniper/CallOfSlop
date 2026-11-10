#!/usr/bin/env python3
"""Walker legs (docs/sandbox/GIANTS.md): one all-in-one part a leg, for every kind and size tier, and the test walker.

    python3 tools/constructs/leg_parts.py           writes Constructs/Parts/Walkers and Constructs/Blueprints/TitanWalker.json
    python3 tools/constructs/leg_parts.py --check   compares them

A leg part is its hip housing (one cell of its size class, the "mount" socket on its -x face, the hip's pivot at the
cell's centre) and a "leg" block: the kind, the segments (hip first), their width, the foot, what it carries and how fast
it steps. The segments, knees, ankle and foot are posed by the gait brain (Source/Constructs/ConstructWalk.cs) and drawn
by Source/Render/ConstructRenderLegs.cs: they are not boxes of the file.

Data first: the JSON files are what the game loads. Change a leg here, never in its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, write_parts, r  # noqa: E402

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(ROOT, "Constructs", "Parts", "Walkers")
BLUEPRINT = os.path.join(ROOT, "Constructs", "Blueprints", "TitanWalker.json")
SECTION = "Legs"  # (Constructs/Categories.json: Wheels and Suspension, Legs)

# Size tiers: class, reach (m, hip to foot), the thickest segment (m), kg a leg carries, seconds a step takes in the air.
TIERS = [
    ("Small", SMALL, 1.6, 0.12, 400, 0.35),
    ("Medium", MEDIUM, 6.0, 0.5, 8_000, 0.7),
    ("Large", LARGE, 20.0, 2.0, 600_000, 1.4),
    ("Titan", CAPITAL, 48.0, 4.5, 12_000_000, 2.4),
]

# Kinds: segment shares of the reach (hip first), the hip's standing height and the foot's spread (shares of the reach), colours.
KINDS = [
    ("Mech", "mech", [0.42, 0.42, 0.16], 0.62, 0.3, "#5d6670", "#2a2d31"),
    ("Digitigrade", "digitigrade", [0.36, 0.38, 0.26], 0.6, 0.3, "#6b5f4f", "#2e2a25"),
    ("Insect", "insect", [0.14, 0.34, 0.38, 0.14], 0.45, 0.6, "#4f5d48", "#252a22"),
]

POWER = {"hydraulic": 1.0, "electric": 0.8, "shaft": 1.2}  # strength on each source (phase 2 gates the legs on them)

# The gait profiles on the configure tool (docs/sandbox/GIANTS.md, phase 3). Simple: no settings, it just walks. Basic: the
# walker's stride, step height, body height and gait, read from its lead leg (the one with the highest profile). Advanced:
# each leg's own pace, lift, spread and gait group too.
WHOLE = "The walker follows its leg with the highest profile."
CONFIG = [
    {"key": "walkProfile", "label": "Gait profile", "group": "Gait", "type": "choice", "options": ["Simple", "Basic", "Advanced"], "default": 0,
     "tooltip": "Simple: it just walks. Basic: the gait settings. Advanced: each leg's own tuning too. " + WHOLE},
    {"key": "stride", "label": "Stride", "group": "Gait", "unit": "%", "min": 50, "max": 150, "step": 5, "default": 100,
     "tooltip": "How far each step reaches: longer strides walk faster. Basic and Advanced. " + WHOLE},
    {"key": "stepLift", "label": "Step height", "group": "Gait", "unit": "%", "min": 50, "max": 250, "step": 5, "default": 100,
     "tooltip": "How high the feet lift: higher clears bigger obstacles. Basic and Advanced. " + WHOLE},
    {"key": "bodyHeight", "label": "Body height", "group": "Gait", "unit": "%", "min": 60, "max": 130, "step": 5, "default": 100,
     "tooltip": "How high it stands (still no higher than the legs reach). Basic and Advanced. " + WHOLE},
    {"key": "gait", "label": "Gait", "group": "Gait", "type": "choice", "options": ["Auto", "Tripod", "Wave"], "default": 0,
     "tooltip": "Auto: a tripod with six or more legs in an even count, else a wave. Tripod: half the legs at a time. Wave: one leg at a time. " + WHOLE},
    {"key": "legPace", "label": "Leg pace", "group": "This leg", "unit": "%", "min": 50, "max": 150, "step": 5, "default": 100, "advanced": True,
     "tooltip": "This leg's step speed. Advanced profile only."},
    {"key": "legLift", "label": "Leg lift", "group": "This leg", "unit": "%", "min": 50, "max": 200, "step": 5, "default": 100, "advanced": True,
     "tooltip": "How high this leg lifts its foot, on top of the step height. Advanced profile only."},
    {"key": "legSpread", "label": "Leg spread", "group": "This leg", "unit": "%", "min": 70, "max": 130, "step": 5, "default": 100, "advanced": True,
     "tooltip": "How far out this leg puts its foot. Advanced profile only."},
    {"key": "legGroup", "label": "Gait group", "group": "This leg", "type": "choice", "options": ["Auto", "First", "Second"], "default": 0, "advanced": True,
     "tooltip": "In a tripod, which half this leg steps with. Advanced profile only."},
]


def leg(tier, kind):
    tname, c, reach, width, load, step = tier
    kname, key, shares, stand, spread, colour, joint = kind
    h = c.half
    p = Part(f"{tname} {kname} Leg", c, SECTION, f"A {kname.lower()} walker leg: hip, segments, knees, foot and actuators in one part. "
             "The gait brain walks it.", role="leg", color=colour, group="Walkers" if c is SMALL else None)
    p.add(box((-h, -h, -h), (h, h, h)))  # the hip housing: one cell, solid
    a = h * 0.55
    p.add(box((h + 0.004 * c.x, -a, -a), (h + h * 0.3, a, a), joint, detail=True))  # the hip actuator's cap, proud of the outer face
    p.sock("mount", (-h, 0, 0), "-x")
    p.sock("top", (0, h, 0), "+y")
    p.sock("drive", (0, 0, -h), "-z")  # (a drive shaft's port: the leg walks on the engine)
    p.set(leg={
        "type": key,
        "tier": tname.lower(),
        "segments": [r(reach * s) for s in shares],
        "width": r(width),
        "foot": r(width * 0.9),
        "hip": [0, 0, 0],
        "load": load,
        "stepTime": step,
        "stepHeight": 0.15,
        "stand": stand,
        "spread": spread,
        "power": POWER,
        "jointColor": joint,
    })
    # Power (docs/sandbox/GIANTS.md, phase 2): a hose from a valve or a pump, a drive shaft, or the electric circuit
    # through the leg's mount (no port: power runs through the parts it is fitted to).
    p.set(ports=[
        {"type": "pipe", "fluid": "oil", "name": "Oil in", "dir": "in", "at": [0, r(-h * 0.5), r(h)], "face": "+z"},
        {"name": "Drive in", "type": "shaft", "dir": "in", "socket": "drive"},
    ])
    p.set(config=CONFIG)
    mass = 8 * reach ** 2.6
    return p.stats(mass, 300 * reach ** 1.5, c.armour)


def blueprint():
    """The test walker: a bare 128 x 64 m capital chassis on six titan mech legs, a helm on the first leg's hip, a Driving Module wired to that leg, and a battery that powers the legs."""
    legs = [{"part": "Titan Mech Leg", "on": 0, "socket": f"{side}{i}", "by": "mount"} for side in ("-z", "+z") for i in (0, 4, 7)]  # (the deck's side sockets: cells 0, 4 and 7 along it)
    steps = [{"part": "Capital Chassis Deck 128x64 m"}] + legs + [
        {"part": "Adapter Capital to Small", "on": 1, "socket": "top", "by": "back"},  # (the helm rides the first leg's hip: its wire reaches the leg)
        {"part": "Bridge Helm", "on": 7, "socket": "front2.2", "by": "-y", "spin": 1},
        {"part": "Driving Module", "on": 8, "socket": "+x", "by": "-x"},
        {"part": "Battery", "on": 9, "socket": "+x", "by": "-x"},  # (electric power: it reaches every leg through the hips and the deck)
    ]
    return {
        "format": "call-of-slop-construct-blueprint/1",
        "name": "Titan Walker",
        "group": "Walkers",
        "description": "A bare 128 m walking chassis on six titan legs (docs/sandbox/GIANTS.md). The helm walks it: W/S, A/D turn, Q/E strafe, Sprint/Crouch height.",
        "steps": steps,
        "wires": [[8, 9], [9, 1]],
    }


def main():
    parts = [("", leg(t, k)) for t in TIERS for k in KINDS]
    code = write_parts(OUT, parts)
    text = json.dumps(blueprint(), indent=2) + "\n"
    have = open(BLUEPRINT, encoding="utf-8").read() if os.path.exists(BLUEPRINT) else None
    if "--check" in sys.argv:
        if have != text:
            print(f"out of date: {BLUEPRINT}")
            code = 1
    elif have != text:
        with open(BLUEPRINT, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
        print(f"written {BLUEPRINT}")
    return code


if __name__ == "__main__":
    sys.exit(main())
