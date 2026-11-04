#!/usr/bin/env python3
"""Writes the hand controls' part files and the control modules': Constructs/Parts/Controls.

docs/sandbox/CONSTRUCTS.md, "Logic" and "Control modules". An Analog Lever is a housing with a slot along its top and a handle that tilts in
the slot with the part's value (a "lever" rig, the drawing's moving-box path), a block at each end of the slot for its
end stops. The handle is a detail, so a hidden box stands where it swings: the crosshair finds the part there.
Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/control_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import LARGE, MEDIUM, SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Controls")
HOUSING, SLOT, STOP, SHAFT, KNOB = "#3a3f46", "#16181b", "#8f98a2", "#c9ced4", "#e0b030"

CONFIG = [
    {"key": "value", "label": "Value", "group": "General", "min": 0, "max": 1, "step": 0.01, "default": 0, "tooltip": "Where the handle stands, 0 to 1."},
    {"key": "snap", "label": "Snap step", "group": "General", "min": 0, "max": 0.5, "step": 0.01, "default": 0, "tooltip": "The steps the handle clicks through. 0: smooth."},
    {"key": "low", "label": "Output at 0", "group": "Output", "min": -100, "max": 100, "step": 0.01, "default": 0, "tooltip": "What it puts out with the handle at 0."},
    {"key": "high", "label": "Output at 1", "group": "Output", "min": -100, "max": 100, "step": 0.01, "default": 1, "tooltip": "What it puts out with the handle at 1."},
    {"key": "spring", "type": "toggle", "label": "Spring return", "group": "Spring", "default": False, "tooltip": "Goes back to its rest value when let go."},
    {"key": "rest", "label": "Rest value", "group": "Spring", "min": 0, "max": 1, "step": 0.01, "default": 0, "tooltip": "Where the spring takes it."},
]


def lever(name, cls, s, mass, health, armour, tool):
    """s: the scale against the small one, whose housing is a 0.2 m cube round the origin."""
    h = 0.1 * s
    p = Part(name, cls, "Controls", "A handle you drag from 0 to 1, for throttles and speeds.", role="logic", tool=tool, color=HOUSING, group="Logic" if cls is SMALL else None)
    p.add(box((-h, -h, -h), (h, h, h)))
    p.add(box((-h, h + 0.001, -h), (h, h + 0.25 * s, h), hidden=True))  # (the crosshair's way to the handle)
    p.add(box((-0.03 * s, h - 0.006 * s, -0.08 * s), (0.03 * s, h + 0.008 * s, 0.08 * s), SLOT, detail=True))
    for z in (-1, 1):
        lo, hi = sorted((z * 0.08 * s, z * 0.1 * s))
        p.add(box((-0.035 * s, h - 0.002 * s, lo), (0.035 * s, h + 0.022 * s, hi), STOP, detail=True))
    pivot = [0, h, 0]
    p.add(box((-0.014 * s, h, -0.014 * s), (0.014 * s, h + 0.2 * s, 0.014 * s), SHAFT, detail=True, rig="lever", pivot=pivot))
    p.add(box((-0.032 * s, h + 0.19 * s, -0.032 * s), (0.032 * s, h + 0.25 * s, 0.032 * s), KNOB, detail=True, rig="lever", pivot=pivot))
    for name_, at in (("+x", (h, 0, 0)), ("-x", (-h, 0, 0)), ("+z", (0, 0, h)), ("-z", (0, 0, -h)), ("-y", (0, -h, 0))):
        p.sock(name_, at, name_)
    p.stats(mass, health, armour, 1.0 if cls is SMALL else None)
    p.set(logic={"kind": "analoglever"}, config=CONFIG)
    return "", p


def module(name, kind, desc, accent):
    """A control module (ConstructModules.cs): a flat box with a row of terminals along its top, each a control it breaks out."""
    p = Part(name, SMALL, "Controls", desc, role="logic", tool="wrench", color=HOUSING, group="Logic")
    h, t = 0.15, 0.08
    p.add(box((-h, -t, -h), (h, t, h)))
    p.add(box((-0.11, t - 0.004, -0.11), (0.11, t + 0.012, -0.03), accent, detail=True))
    for k in range(4):
        x = -0.09 + 0.06 * k
        p.add(box((x - 0.012, t - 0.004, 0.03), (x + 0.012, t + 0.02, 0.09), STOP, detail=True))
    for name_, at in (("+x", (h, 0, 0)), ("-x", (-h, 0, 0)), ("+z", (0, 0, h)), ("-z", (0, 0, -h)), ("-y", (0, -t, 0))):
        p.sock(name_, at, name_)
    p.stats(4, 60, 2, 1.0)
    p.set(logic={"kind": kind})
    return "", p


def main():
    return write_parts(OUT, [
        module("Driving Module", "drivemodule", "Link a seat in and its drive output to the drivetrain: that seat drives. The controls are outputs too.", "#5ac060"),
        module("Turret Module", "turretmodule", "Link a seat in and its guns output to a gun: that seat aims and fires it.", "#e06040"),
        lever("Analog Lever", SMALL, 1, 3, 40, 1, "wrench"),
        lever("Medium Analog Lever", MEDIUM, 5, 60, 180, 4, "welder"),
        lever("Large Analog Lever", LARGE, 20, 600, 600, 8, "welder"),
    ])


if __name__ == "__main__":
    sys.exit(main())
