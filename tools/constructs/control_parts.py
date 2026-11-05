#!/usr/bin/env python3
"""Writes the hand controls' part files and the control modules': Constructs/Parts/Controls.

docs/sandbox/CONSTRUCTS.md, "Logic" and "Control modules". An Analog Lever is a housing with a slot along its top and a handle that tilts in
the slot with the part's value (a "lever" rig, the drawing's moving-box path), a block at each end of the slot for its
end stops. The handle is a detail, so a hidden box stands where it swings: the crosshair finds the part there.
Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/control_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import math
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


# ---- Hand controls, displays, logic blocks, sensors, bundling (docs/sandbox/CONSTRUCTS.md, "Logic blocks") ----------

GREEN, RED, AMBER, CAP, SCREEN, BLOCK, PIN = "#5ac060", "#d0352c", "#e0b030", "#c9ced4", "#101416", "#2f3a44", "#b08a4a"
LATCH = [
    {"key": "latched", "label": "Latched", "group": "General", "default": True, "tooltip": "On: each press turns it on or off. Off: it springs back after its hold time."},
    {"key": "hold", "label": "Stays on for", "group": "General", "unit": "s", "min": 0.05, "max": 30, "step": 0.05, "default": 0.5, "tooltip": "How long a press keeps it on (when not latched)."},
]
BUTTON = [dict(LATCH[0], default=False), LATCH[1]]
UNITS = ["none", "m", "m/s", "km/h", "%", "rpm", "kW", "kg", "L", "deg", "s", "N"]
INK = {"key": "ink", "type": "colour", "label": "Colour", "group": "Display", "default": "#000000", "tooltip": "The colour of what it shows. Black: its own colours."}
RAMP = {"key": "ramp", "label": "Colour mode", "group": "Display", "options": ["Display colour", "Green yellow red", "Gradient", "Heat"], "default": 0, "tooltip": "How the colour follows the value."}
DECIMALS = {"key": "decimals", "label": "Decimals", "group": "General", "min": 0, "max": 4, "step": 1, "default": 1, "tooltip": "Places after the point."}
UNIT = {"key": "unit", "label": "Unit", "group": "General", "options": UNITS, "default": 0, "tooltip": "Shown after the number."}
LOW = {"key": "low", "label": "Min", "group": "Scale", "min": -10000, "max": 10000, "step": 0.1, "default": 0, "tooltip": "The bottom of the scale."}
HIGH = {"key": "high", "label": "Max", "group": "Scale", "min": -10000, "max": 10000, "step": 0.1, "default": 100, "tooltip": "The top of the scale."}
FIVE = [("+x", "+x"), ("-x", "-x"), ("+z", "+z"), ("-z", "-z"), ("-y", "-y")]


def body(name, section, desc, h, t, cls=SMALL, tool="wrench", mass=3, health=40, armour=1, kind=None, color=HOUSING, config=None, **logic):
    """A box 2h wide and long, 2t high, with sockets on its sides and under it; `kind` its logic kind."""
    p = Part(name, cls, section, desc, role="logic", tool=tool, color=color, group="Logic" if cls is SMALL else None)
    p.add(box((-h, -t, -h), (h, t, h)))
    for name_, d in FIVE:
        at = {"+x": (h, 0, 0), "-x": (-h, 0, 0), "+z": (0, 0, h), "-z": (0, 0, -h), "-y": (0, -t, 0)}[d]
        p.sock(name_, at, d)
    p.stats(mass, health, armour, 1.0 if cls is SMALL else None)
    p.set(logic=dict({"kind": kind}, **logic))
    if config:
        p.set(config=config)
    return p


def analog(name, section, rig, s=1, snap=0.0, tool="wrench", mass=3, cls=SMALL, health=40, armour=1):
    """A rotary knob with stops, or a slider: an Analog Lever by another shape (AnalogLever.cs)."""
    desc = "A knob with stops, 0 to 1." if rig == "knob" else "A slider, 0 to 1."
    cfg = [dict(c) for c in CONFIG]
    cfg[1]["default"] = snap
    h, t = 0.1 * s, 0.05 * s
    p = body(name, section, desc, h, t, cls, tool, mass, health, armour, "analoglever", config=cfg)
    p.add(box((-h, t + 0.001, -h), (h, t + 0.1 * s, h), hidden=True))
    if rig == "knob":
        p.add(box((-0.05 * s, t - 0.002 * s, -0.05 * s), (0.05 * s, t + 0.06 * s, 0.05 * s), SHAFT, detail=True, rig="knob", pivot=[0, t, 0]))
        p.add(box((-0.012 * s, t + 0.056 * s, 0), (0.012 * s, t + 0.075 * s, 0.05 * s), AMBER, detail=True, rig="knob", pivot=[0, t, 0]))
        for k in range(-5, 6):
            a = math.radians(k * 27)
            x, z = math.sin(a) * 0.085 * s, math.cos(a) * 0.085 * s
            p.add(box((x - 0.007 * s, t - 0.002 * s, z - 0.007 * s), (x + 0.007 * s, t + 0.012 * s, z + 0.007 * s), STOP, detail=True))
    else:
        p.add(box((-0.012 * s, t - 0.004 * s, -0.085 * s), (0.012 * s, t + 0.006 * s, 0.085 * s), SLOT, detail=True))
        p.add(box((-0.04 * s, t - 0.002 * s, -0.02 * s), (0.04 * s, t + 0.05 * s, 0.02 * s), KNOB, detail=True, rig="slide", travel=0.065 * s))
    return "", p


def toggle(name, kind, shape, s=1, tool="wrench", mass=2, cls=SMALL, health=40, armour=1, config=LATCH):
    """A switch or button with a part of it that moves: a toggle's bat, a rocker's plate, a mushroom's cap."""
    h, t = 0.1 * s, 0.05 * s
    desc = {"toggle": "A flick switch.", "rocker": "A rocker switch.", "mushroom": "A big red button."}[shape]
    p = body(name, "Controls", desc, h, t, cls, tool, mass, health, armour, kind, config=config)
    if shape == "toggle":
        p.add(box((-0.02 * s, t, -0.02 * s), (0.02 * s, t + 0.09 * s, 0.02 * s), CAP, detail=True, use=True, rig="flip", pivot=[0, t, 0]))
    elif shape == "rocker":
        p.add(box((-0.05 * s, t - 0.002 * s, -0.075 * s), (0.05 * s, t + 0.03 * s, 0.075 * s), GREEN, detail=True, use=True, rig="flip", pivot=[0, t, 0]))
    else:
        p.add(box((-0.03 * s, t - 0.002 * s, -0.03 * s), (0.03 * s, t + 0.04 * s, 0.03 * s), CAP, detail=True))
        p.add(box((-0.08 * s, t + 0.04 * s, -0.08 * s), (0.08 * s, t + 0.075 * s, 0.08 * s), RED, detail=True, use=True, rig="press", travel=0.03 * s))
    return "", p


def key_switch():
    p = body("Key Switch", "Controls", "Turns for its owner, or with a Key in your hands.", 0.1, 0.05, kind="keyswitch")
    p.add(box((-0.04, 0.048, -0.04), (0.04, 0.066, 0.04), PIN, detail=True))
    p.add(box((-0.008, 0.064, -0.03), (0.008, 0.078, 0.03), SLOT, detail=True, use=True, rig="flip", pivot=[0, 0.05, 0]))
    return "", p


def key_item():
    p = Part("Key", SMALL, "Controls", "Hold it to turn any Key Switch.", role="frame", tool="wrench", color=PIN, group="Logic")
    p.add(box((-0.03, 0, -0.08), (0.03, 0.012, -0.02)))
    p.add(box((-0.01, 0, -0.02), (0.01, 0.012, 0.09), detail=True))
    p.sock("-y", (0, 0, -0.05), "-y")
    p.stats(0.2, 10, 0, 0.5)
    return "", p


def keypad(name, kind, desc, config, s=1):
    """Twelve real keys under a screen on the part's front (+z): `"use"` boxes with `"key"`."""
    hw, hh, t = 0.11 * s, 0.15 * s, 0.04 * s
    p = Part(name, SMALL, "Controls", desc, role="logic", tool="wrench", color=HOUSING, group="Logic")
    p.add(box((-hw, -hh, -t), (hw, hh, t)))
    for name_, at, d in (("back", (0, 0, -t), "-z"), ("+x", (hw, 0, 0), "+x"), ("-x", (-hw, 0, 0), "-x"), ("+y", (0, hh, 0), "+y"), ("-y", (0, -hh, 0), "-y")):
        p.sock(name_, at, d)
    rows = ["123", "456", "789", "C0E"]
    colour = {"C": RED, "E": GREEN}
    for r, row in enumerate(rows):
        for k, ch in enumerate(row):
            x = (k - 1) * 0.062 * s
            y = 0.012 * s - r * 0.044 * s
            p.add(box((x - 0.026 * s, y - 0.018 * s, t - 0.002), (x + 0.026 * s, y + 0.018 * s, t + 0.012 * s), colour.get(ch, CAP), detail=True, use=True, key=ch))
    p.stats(3, 40, 1, 1.0)
    p.set(logic={"kind": kind}, config=config + [INK], panels=[{"source": "logic.keypad", "at": [0, 0.09 * s, t + 0.004], "size": [0.18 * s, 0.09 * s], "face": "+z", "up": "+y", "rows": 1}])
    return "", p


def screen(name, source, desc, w, h, rows, cls=SMALL, tool="wrench", mass=3, health=30, armour=0, kind="display", config=None, section="Displays", **logic):
    """A flat box with one panel on its front (+z), and sockets behind and round it."""
    t = max(0.03, min(w, h) * 0.08)
    p = Part(name, cls, section, desc, role="logic", tool=tool, color=HOUSING, group="Logic" if cls is SMALL else None)
    p.add(box((-w / 2, -h / 2, -t), (w / 2, h / 2, t)))
    for name_, at, d in (("back", (0, 0, -t), "-z"), ("+x", (w / 2, 0, 0), "+x"), ("-x", (-w / 2, 0, 0), "-x"), ("+y", (0, h / 2, 0), "+y"), ("-y", (0, -h / 2, 0), "-y")):
        p.sock(name_, at, d)
    p.stats(mass, health, armour, 1.0 if cls is SMALL else None)
    p.set(logic=dict({"kind": kind}, **logic), panels=[{"source": source, "at": [0, 0, t + 0.003], "size": [round(w - 0.016, 3), round(h - 0.016, 3)], "face": "+z", "up": "+y", "rows": rows}])
    p.set(config=(config or []) + [INK])
    return "", p


def displays():
    out = []
    readout = [DECIMALS, UNIT]
    scale = [DECIMALS, UNIT, LOW, HIGH]
    plot = [LOW, HIGH, RAMP]  # (no number on these: the whole screen is the bar or the line)
    sizes = [("", SMALL, 1, "wrench", 3, 30, 0), ("Medium ", MEDIUM, 4, "welder", 30, 120, 2), ("Large ", LARGE, 16, "welder", 300, 400, 4)]
    for pre, cls, s, tool, mass, hp, ar in sizes:
        out.append(screen(pre + "Number Readout", "logic.readout", "Shows the number wired into it.", 0.5 * s, 0.25 * s, 1, cls, tool, mass, hp, ar, config=readout))
        out.append(screen(pre + "Bar Gauge", "logic.bar", "A bar across the whole screen, from its min to its max.", 0.5 * s, 0.25 * s, 1, cls, tool, mass, hp, ar, config=plot))
        out.append(screen(pre + "Tall Bar Gauge", "logic.barv", "A bar up the whole screen, from its min to its max.", 0.25 * s, 0.5 * s, 2, cls, tool, mass, hp, ar, config=plot))
        out.append(screen(pre + "Dial Gauge", "logic.dial", "A needle, from its min to its max.", 0.5 * s, 0.5 * s, 4, cls, tool, mass, hp, ar, config=scale))
        out.append(screen(pre + "Graph Screen", "logic.graph", "The number wired into it over the last 30 s. Min and max the same: it fits the line.", 1.0 * s, 0.5 * s, 4, cls, tool, mass, hp, ar, config=plot))
        out.append(screen(pre + "Text Sign", "logic.sign", "Says what you type. A wire on it switches to its second message.", 1.0 * s, 0.5 * s, 3, cls, tool, mass, hp, ar, text=2))
        out.append(screen(pre + "Compass", "logic.compass", "The way you face looking at it. No wiring.", 0.5 * s, 0.25 * s, 2, cls, tool, mass, hp, ar, kind="instrument", section="Gauges"))
        out.append(screen(pre + "Altimeter", "logic.altimeter", "How high it is. No wiring.", 0.5 * s, 0.25 * s, 1, cls, tool, mass, hp, ar, kind="instrument", section="Gauges", config=[dict(DECIMALS, default=0), dict(UNIT, default=1)]))
        out.append(screen(pre + "Speedometer", "logic.speedometer", "How fast its construct goes. No wiring.", 0.5 * s, 0.5 * s, 4, cls, tool, mass, hp, ar, kind="instrument", section="Gauges",
                          config=[{"key": "units", "label": "Units", "group": "General", "options": ["m/s", "km/h", "mph"], "default": 1, "tooltip": "What it reads in."}, dict(LOW, default=0), dict(HIGH, default=120)]))
        for n in (4, 8):
            out.append(screen(f"{pre}Light Bank {n}", "logic.lamps", "A labelled lamp for each of its inputs. Type the labels, comma-separated.", 0.5 * s, 0.09 * n * s, n, cls, tool, mass, hp, ar, ports=n, text=1))
    return out


def blocks():
    out = []

    def block(name, kind, desc, config=None, accent=GREEN, section="Logic gates", **logic):
        p = body(name, section, desc, 0.1, 0.05, kind=kind, config=config, color=BLOCK, **logic)
        p.add(box((-0.07, 0.048, -0.07), (0.07, 0.056, -0.02), accent, detail=True))
        for k in range(3):
            x = -0.05 + 0.05 * k
            p.add(box((x - 0.012, 0.048, 0.02), (x + 0.012, 0.06, 0.07), PIN, detail=True))
        out.append(("", p))

    value = {"key": "value", "label": "Value", "group": "General", "min": -1000, "max": 1000, "default": 0, "tooltip": "What it holds now."}
    block("XOR Gate", "xor", "On when an odd number of inputs are on.")
    block("NAND Gate", "nand", "Off only when every input is on.")
    block("Math Block", "mathblock", "a and b: add, subtract, multiply, divide, min or max.", [
        {"key": "operator", "label": "Operation", "group": "General", "options": ["add", "subtract", "multiply", "divide", "min", "max"], "default": 0, "tooltip": "What it does with a and b."},
        {"key": "offset", "label": "b if unwired", "group": "General", "min": -1000, "max": 1000, "step": 0.01, "default": 0, "tooltip": "Used when nothing is wired into b."}], AMBER)
    block("Latch", "latch", "Set turns it on, reset turns it off.", accent=AMBER, section="Memory")
    block("Counter", "counter", "Counts up and down on rising inputs, to its limit.", [
        value, {"key": "limit", "label": "Limit", "group": "General", "min": 0, "max": 100000, "step": 1, "default": 0, "tooltip": "The most it counts to. 0: no limit."},
        {"key": "wrap", "label": "Wrap round", "group": "General", "default": False, "tooltip": "Past the limit it starts again."}], AMBER, "Memory")
    block("Memory Cell", "memory", "Stores value when store rises.", [value], AMBER, "Memory")
    block("Filter", "filter", "Follows its input smoothed, clamped and rate limited. Good for hydraulics.", [
        {"key": "seconds", "label": "Smoothing", "group": "General", "unit": "s", "min": 0, "max": 30, "step": 0.05, "default": 0, "tooltip": "How slowly it follows. 0: no smoothing."},
        {"key": "rate", "label": "Rate limit", "group": "General", "unit": "/s", "min": 0, "max": 1000, "step": 0.01, "default": 0, "tooltip": "The most it changes a second. 0: no limit."},
        {"key": "low", "label": "Min", "group": "Clamp", "min": -1000, "max": 1000, "step": 0.01, "default": -1000, "tooltip": "The least it puts out."},
        {"key": "high", "label": "Max", "group": "Clamp", "min": -1000, "max": 1000, "step": 0.01, "default": 1000, "tooltip": "The most it puts out."}])
    block("Multiplexer", "mux", "Puts out the input its select names, 1 to 6.")
    block("Random", "randompulse", "A new random number on each rising input, min to max.", [
        value, {"key": "low", "label": "Min", "group": "General", "min": -1000, "max": 1000, "step": 0.01, "default": 0, "tooltip": "The least it gives."},
        {"key": "high", "label": "Max", "group": "General", "min": -1000, "max": 1000, "step": 0.01, "default": 1, "tooltip": "The most it gives."}])
    return out


def sensors():
    out = []

    def sensor(name, kind, desc, config=None):
        p = body(name, "Sensors", desc, 0.08, 0.04, kind=kind, config=config, mass=1, health=20, color=BLOCK)
        p.add(box((-0.03, 0.038, -0.06), (0.03, 0.05, 0.02), AMBER, detail=True))  # (its front, -z)
        out.append(("", p))

    sensor("Altitude Sensor", "altitudesensor", "How high it is, m.")
    sensor("Tilt Sensor", "tiltsensor", "Its pitch, and its roll on the second output, degrees.")
    sensor("Heading Sensor", "headingsensor", "The way its front faces, degrees from north.")
    sensor("Level Sensor", "levelsensor", "Link its watch output to a tank or store: how full, %.")
    sensor("Damage Sensor", "damagesensor", "Link its watch output to a part: on when that part is hit.", [
        {"key": "hold", "label": "Stays on for", "group": "General", "unit": "s", "min": 0.05, "max": 30, "step": 0.05, "default": 1, "tooltip": "How long a hit keeps it on."}])
    sensor("Occupancy Sensor", "occupancysensor", "On while the seat wired into it, or the drive seat, is taken.")
    return out


def bundling():
    channel = {"key": "channel", "label": "Channel", "group": "General", "min": 1, "max": 99, "step": 1, "default": 1, "tooltip": "A sender and a receiver on the same channel talk."}
    out = []

    def radio(name, kind, desc, config):
        p = body(name, "Wireless and bus", desc, 0.1, 0.05, kind=kind, config=config, color=BLOCK)
        p.add(box((0.05, 0.048, 0.05), (0.07, 0.2, 0.07), SHAFT, detail=True))
        out.append(("", p))

    radio("Wireless Sender", "radiotx", "Sends its input on a channel, short range.", [channel, {"key": "radius", "label": "Range", "group": "General", "unit": "m", "min": 5, "max": 100, "step": 1, "default": 30, "tooltip": "How far it carries."}])
    radio("Wireless Receiver", "radiorx", "Puts out the nearest sender on its channel.", [channel])
    for name, kind, desc in (("Data Bus", "busin", "Eight signals in, one link out to a Bus Breakout."), ("Bus Breakout", "busout", "One link from a Data Bus in, its eight signals out.")):
        p = body(name, "Wireless and bus", desc, 0.15, 0.05, kind=kind, color=BLOCK)
        for k in range(8):
            x = -0.105 + 0.03 * k
            p.add(box((x - 0.009, 0.048, -0.12), (x + 0.009, 0.06, -0.06), PIN, detail=True))
        p.add(box((-0.05, 0.048, 0.05), (0.05, 0.07, 0.11), GREEN if kind == "busin" else AMBER, detail=True))
        out.append(("", p))
    return out


def controls():
    code = [{"key": "code", "label": "Code", "group": "General", "min": 0, "max": 999999, "step": 1, "default": 0, "tooltip": "The number that opens it. Only you can see or change it."},
            {"key": "hold", "label": "Stays open", "group": "General", "unit": "s", "min": 0, "max": 600, "step": 0.5, "default": 0, "tooltip": "0: until Clear."},
            {"key": "invert", "label": "Locked is on", "group": "General", "default": False, "tooltip": "On until opened: wire it straight into a hinge's Lock."}]
    entry = [{"key": "value", "label": "Value", "group": "General", "min": -1000000, "max": 1000000, "default": 0, "tooltip": "What it puts out."},
             dict(DECIMALS, default=0, tooltip="Where the point goes in what you type.")]
    return [
        keypad("Numpad", "numpad", "Type a number, Accept puts it out, Clear resets.", entry),
        keypad("Code Lock", "codelock", "The right code turns it on.", code),
        analog("Rotary Knob", "Controls", "knob", snap=0.1),
        analog("Medium Rotary Knob", "Controls", "knob", s=5, snap=0.1, tool="welder", mass=30, cls=MEDIUM, health=180, armour=4),
        analog("Slider", "Controls", "slide"),
        analog("Medium Slider", "Controls", "slide", s=5, tool="welder", mass=30, cls=MEDIUM, health=180, armour=4),
        toggle("Toggle Switch", "switch", "toggle"),
        toggle("Rocker Switch", "switch", "rocker"),
        toggle("Mushroom Button", "button", "mushroom", config=BUTTON),
        toggle("Medium Mushroom Button", "button", "mushroom", s=5, tool="welder", mass=30, cls=MEDIUM, health=180, armour=4, config=BUTTON),
        key_switch(),
        key_item(),
    ]


def main():
    return write_parts(OUT, [
        module("Driving Module", "drivemodule", "Link a seat in and its drive output to the drivetrain: that seat drives. The controls are outputs too.", "#5ac060"),
        module("Turret Module", "turretmodule", "Link a seat in and its guns output to a gun: that seat aims and fires it.", "#e06040"),
        lever("Analog Lever", SMALL, 1, 3, 40, 1, "wrench"),
        lever("Medium Analog Lever", MEDIUM, 5, 60, 180, 4, "welder"),
        lever("Large Analog Lever", LARGE, 20, 600, 600, 8, "welder"),
        *controls(),
        *displays(),
        *blocks(),
        *sensors(),
        *bundling(),
    ])


if __name__ == "__main__":
    sys.exit(main())
