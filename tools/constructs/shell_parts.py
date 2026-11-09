#!/usr/bin/env python3
"""Writes the robot shell parts (Constructs/Parts/Shells) and the Depot Lander blueprint.

docs/sandbox/CONSTRUCTS.md, "Robot shells". A Robot Shell is a body a player wakes in from a Shell Control Chair on its
channel, within the chair's range: they play as the shell while their own body waits in the chair, and a shell that is
destroyed only sends them back. The Depot Lander is the Orbiter without its Driving Module, the ship the story's Orbital
Depot gives a player to make fly and bring down (docs/story/DEPOT.md).

Run it from the repo root: python3 -I tools/constructs/shell_parts.py (--check only compares).
Then python3 -I tools/constructs/assembling.py, for the recipes. Fix a part here, never in its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Shells")
SECTION = "Shells"
STEEL, DARK, JOINT, VISOR, LAMP, SEAT = "#8b949c", "#30363d", "#56606a", "#38d0e8", "#f2a93b", "#3b4652"
CHANNEL = {"key": "channel", "label": "Channel", "group": "General", "min": 1, "max": 99, "step": 1, "default": 1,
           "tooltip": "A chair wakes you in the nearest free shell on its channel."}
RANGE = 3000  # m: the chair's reach (the story's depot hangs 1.5 km over its chairs)


def shell():
    """A standing robot about a person's size, its feet on its origin and its visor towards -z (a part's front)."""
    p = Part("Robot Shell", SMALL, SECTION, f"A robot body. Sit in a Shell Control Chair on its channel, within {RANGE / 1000:g} km, to wake in it: if it is destroyed you only wake in the chair again.", tool="wrench", group="Logic", color=STEEL)
    for s in (-1, 1):
        x0, x1 = sorted((s * 0.045, s * 0.175))
        p.add(box((x0 - 0.01, 0.0, -0.15), (x1 + 0.01, 0.07, 0.09), DARK))  # feet
        p.add(box((x0, 0.06, -0.07), (x1, 0.47, 0.07), JOINT))  # shins
        p.add(box((x0 - 0.005, 0.45, -0.085), (x1 + 0.005, 0.53, 0.085), DARK, detail=True))  # knees
        p.add(box((x0 + 0.005, 0.52, -0.075), (x1 - 0.005, 0.88, 0.075)))  # thighs
        a0, a1 = sorted((s * 0.26, s * 0.37))
        p.add(box((a0 - 0.02, 1.36, -0.1), (a1 + 0.02, 1.53, 0.1), JOINT))  # shoulders
        p.add(box((a0, 1.0, -0.06), (a1, 1.37, 0.06)))  # upper arms
        p.add(box((a0 + 0.005, 0.74, -0.055), (a1 - 0.005, 1.01, 0.055), JOINT))  # forearms
        p.add(box((a0 + 0.01, 0.64, -0.05), (a1 - 0.01, 0.75, 0.05), DARK, detail=True))  # hands
    p.add(box((-0.2, 0.86, -0.11), (0.2, 1.02, 0.1), DARK))  # pelvis
    p.add(box((-0.245, 1.0, -0.135), (0.245, 1.5, 0.125)))  # torso
    p.add(box((-0.17, 1.13, -0.16), (0.17, 1.42, -0.12), JOINT, detail=True))  # chest plate
    p.add(box((-0.05, 1.22, -0.172), (0.05, 1.27, -0.15), LAMP, detail=True))  # status lamp
    p.add(box((-0.16, 1.07, 0.11), (0.16, 1.44, 0.2), DARK))  # power pack on the back
    p.add(box((-0.06, 1.48, -0.06), (0.06, 1.58, 0.06), DARK))  # neck
    p.add(box((-0.135, 1.56, -0.14), (0.135, 1.83, 0.125)))  # head
    p.add(box((-0.11, 1.65, -0.158), (0.11, 1.73, -0.12), VISOR))  # visor
    p.add(box((0.07, 1.82, -0.015), (0.09, 1.96, 0.015), DARK, detail=True))  # aerial
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("back", (0, 1.25, 0.2), "+z")
    p.stats(38, 160, 3, 3)
    p.set(shell={"body": True}, config=[CHANNEL], centreSockets=False)
    return p


def chair():
    """A reclining seat with a headset frame over it: sit in it to wake in a shell."""
    p = Part("Shell Control Chair", SMALL, SECTION, f"Sit in it to wake in the nearest free Robot Shell on its channel, up to {RANGE / 1000:g} km away. Your body waits here.", role="seat", tool="wrench", group="Logic", color=SEAT)
    p.add(box((-0.28, 0, -0.3), (0.28, 0.42, 0.3)))  # base and cushion
    p.add(box((-0.28, 0.4, 0.22), (0.28, 1.15, 0.34)))  # back
    p.add(box((-0.36, 0.3, -0.25), (-0.29, 0.62, 0.2), DARK))  # arms
    p.add(box((0.29, 0.3, -0.25), (0.36, 0.62, 0.2), DARK))
    p.add(box((-0.04, 1.14, 0.24), (0.04, 1.42, 0.32), DARK, detail=True))  # headset mast
    p.add(box((-0.2, 1.36, 0.0), (0.2, 1.46, 0.3), DARK, detail=True))  # headset arch
    p.add(box((-0.16, 1.32, -0.02), (0.16, 1.38, 0.04), VISOR, detail=True))  # its lens bar
    p.add(box((0.2, 0.62, -0.22), (0.34, 0.66, -0.08), LAMP, detail=True))  # the link lamp on the right arm
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("back", (0, 0.6, 0.34), "+z")
    p.sock("+x", (0.36, 0.2, 0), "+x")
    p.sock("-x", (-0.36, 0.2, 0), "-x")
    p.stats(60, 200, 3, 3)
    p.set(seat={"at": [0, -0.5, 0]}, shell={"range": RANGE}, config=[CHANNEL])
    return p


def lander():
    """The Orbiter without its Driving Module or wires: the depot's challenge is to fit and link one."""
    with open(os.path.join("Constructs", "Blueprints", "Orbiter.json"), encoding="utf-8") as fh:
        orbiter = json.load(fh)
    steps = [s for s in orbiter["steps"] if s["part"] != "Driving Module"]
    assert len(steps) == len(orbiter["steps"]) - 1 and all(s.get("on", 0) < len(steps) for s in steps)
    return {
        "format": "call-of-slop-construct-blueprint/1",
        "name": "Depot Lander",
        "description": "An Orbiter from the Ministry's Orbital Depot with no Driving Module: fit one on the pilot's seat and link seat, module and jets to fly it.",
        "steps": steps,
    }


def main():
    code = write_parts(OUT, [("", shell()), ("", chair())])
    path = os.path.join("Constructs", "Blueprints", "DepotLander.json")
    text = json.dumps(lander(), indent=2) + "\n"
    same = os.path.exists(path) and open(path, encoding="utf-8").read() == text
    if "--check" in sys.argv:
        code |= 0 if same else 1
    elif not same:
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
    return code


if __name__ == "__main__":
    sys.exit(main())
