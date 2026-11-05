#!/usr/bin/env python3
"""Writes the Sandbox's hydraulics: Constructs/Parts/Hydraulics (pumps, Lever Valves, Digital Valve Blocks, cylinder heads, trays).

docs/sandbox/CONSTRUCTS.md, "Hydraulics"; Source/Constructs/ConstructHydraulics.cs. A pump, turned by a shaft or run on power, charges a
circuit: pipe links that carry oil, through a valve, to a cylinder's head. A valve is a logic part whose signal, -1 to 1, is how far
open it is and which way: a Lever Valve is the Analog Lever's handle, sprung to the middle, and a Digital Valve Block takes a data
signal. Stack them side by side for a bank. A head is the part a cylinder's tool leaves at its first point ("toolOnly": it is never
in the inventory): it carries the ports, and the attach tool's Cylinder mode names it by its size.

Sizes: four classes of pump and valve, four of head. Force is the pump's pressure over the head's area, speed the pump's flow over
the area, so a bigger pump pushes harder and many cylinders on one pump go slower.

Run it from the repo root: python3 -I tools/constructs/hydraulics_parts.py (--check only compares), then
tools/constructs/assembling.py for the recipes. Boxes go through zfight.resolve. Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, block_sockets, box, write_parts  # noqa: E402
from control_parts import CONFIG, lever  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Hydraulics")
SECTION = "Hydraulics"
BODY, DARK, OIL, BRASS, STEEL, SCREEN = "#3f4a55", "#23272c", "#b5531c", "#c8a13a", "#a9b0b8", "#10181c"
OIL_PORT = {"type": "pipe", "fluid": "oil"}

# (class, bar, litres a second at rated rpm, rated rpm, power W with no shaft, kg, health)
PUMPS = [
    (SMALL, 120, 1.5, 1800, 4000, 60, 300),
    (MEDIUM, 180, 12, 1800, 40000, 700, 1500),
    (LARGE, 250, 120, 1500, 400000, 9000, 9000),
    (CAPITAL, 350, 1400, 1200, 1000000, 120000, 60000),
]
# (class, kg, health)
VALVES = [(SMALL, 8, 60), (MEDIUM, 90, 300), (LARGE, 900, 1800), (CAPITAL, 9000, 12000)]
# (size name, half width of the head, kg, health, bore cm2, top speed m/s, electro force N, electro speed m/s, electro W)
HEADS = [
    ("S", 0.07, 6, 120, 25, 1.0, 5_000, 0.2, 300),
    ("M", 0.13, 30, 300, 150, 1.0, 40_000, 0.15, 2_000),
    ("L", 0.24, 200, 1200, 1400, 0.8, 400_000, 0.12, 20_000),
    ("XL", 0.42, 1500, 6000, 14000, 0.6, 4_000_000, 0.1, 200_000),
]
# Lanes: 14 cm apart at the small class, wide enough for a hose (10 cm) and the router's clearance.
PITCH = 0.14
TRAY_SECTION = "Hoses and leads"
# (class, length m, lanes, kg, health)
TRAYS = [
    (SMALL, 1, 3, 4, 200), (SMALL, 2, 3, 8, 400), (SMALL, 1, 6, 8, 300), (SMALL, 2, 6, 16, 600),
    (MEDIUM, 3, 3, 60, 1200), (MEDIUM, 6, 3, 120, 2400),
    (LARGE, 12, 3, 800, 8000),
    (CAPITAL, 24, 3, 8000, 40000),
]
# (class, lanes, kg, health)
CLAMPS = [(SMALL, 1, 0.5, 60), (SMALL, 2, 1, 80), (SMALL, 3, 1.5, 100), (MEDIUM, 3, 10, 400), (LARGE, 3, 80, 2500), (CAPITAL, 3, 800, 15000)]


def port(name, direction, at, face):
    return dict(OIL_PORT, name=name, dir=direction, at=list(at), face=face)


def pump(cls, bar, flow, rpm, watts, mass, health):
    name = f"{cls.name} Hydraulic Pump".strip()
    p = Part(name, cls, SECTION, f"Turned by a shaft, or run on power with none linked. {bar} bar, {flow:g} L/s. Its oil goes through valves to cylinders.", color=BODY, group="Joints and Motion")
    c = cls.cell
    h = c  # (2 cells a side)
    p.add(box((-h, 0, -h), (h, 2 * h, h)))
    p.add(box((-0.7 * h, 2 * h, -0.7 * h), (0.7 * h, 2.3 * h, 0.7 * h), DARK, detail=True))
    p.add(box((h, 0.7 * h, -0.3 * h), (1.12 * h, 1.3 * h, 0.3 * h), OIL, detail=True))
    p.add(box((-0.4 * h, 0.6 * h, h), (0.4 * h, 1.4 * h, 1.12 * h), BRASS, detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("drive", (0, h, -h), "-z")
    p.sock("+x", (h, h, 0), "+x")
    p.sock("-x", (-h, h, 0), "-x")
    p.stats(mass, health, 6)
    p.set(power=watts, ports=[
        port("Oil out", "out", (h, h, 0), "+x"),
        {"name": "Drive in", "type": "shaft", "dir": "in", "socket": "drive", "at": [0, h, -h], "face": "-z"},
    ], hydraulics={"kind": "pump", "bar": bar, "flow": flow, "ratedRpm": rpm})
    return "", p


def valve_ports(h):
    return [port("Oil in", "in", (0, 0, -h), "-z"), port("Oil out", "out", (0, 0, h), "+z")]


def lever_valve(cls, mass, health):
    s = cls.cell / SMALL.cell
    name = f"{cls.name} Lever Valve".strip()
    _, p = lever(name, cls, s, mass, health, 2, "wrench" if cls is SMALL else "welder")
    p.d["description"] = "A spool on a handle: push it, and how far is how fast. Oil in from a pump, out to a cylinder. Stack them for a lever bank."
    p.d["section"] = SECTION
    p.d["group"] = "Joints and Motion"
    config = [dict(e) for e in CONFIG]
    for e in config:
        e["default"] = {"value": 0.5, "low": -1, "high": 1, "spring": True, "rest": 0.5}.get(e["key"], e["default"])
    p.set(config=config, ports=valve_ports(0.1 * s), hydraulics={"kind": "valve"})
    return "", p


def digital_valve(cls, mass, health):
    s = cls.cell / SMALL.cell
    h = 0.1 * s
    name = f"{cls.name} Digital Valve Block".strip()
    p = Part(name, cls, SECTION, "A spool on a solenoid: a signal of -1 to 1 opens it, each way. Oil in from a pump, out to a cylinder.", role="logic",
             tool="wrench" if cls is SMALL else "welder", color=BODY, group="Joints and Motion")
    p.add(box((-h, -h, -h), (h, h, h)))
    p.add(box((-0.6 * h, h - 0.01 * s, -0.6 * h), (0.6 * h, h + 0.03 * s, 0.6 * h), SCREEN, detail=True))
    p.add(box((-0.3 * h, h + 0.03 * s - 0.004 * s, -0.3 * h), (0.3 * h, h + 0.045 * s, 0.3 * h), OIL, detail=True))
    for name_, at in (("+x", (h, 0, 0)), ("-x", (-h, 0, 0)), ("-y", (0, -h, 0))):
        p.sock(name_, at, name_)
    p.stats(mass, health, 2, 1.0 if cls is SMALL else None)
    p.set(logic={"kind": "scale", "settings": [1, -1, 0.5], "labels": ["x 1", "x -1", "x 0.5"], "setting": 0},
          config=[
              {"key": "factor", "label": "Multiply by", "group": "Valve", "min": -10, "max": 10, "step": 0.01, "tooltip": "The signal is multiplied by this: -1 turns the cylinder's way round."},
              {"key": "offset", "label": "Then add", "group": "Valve", "min": -1, "max": 1, "step": 0.01, "default": 0, "tooltip": "Added after the multiply."},
              {"key": "low", "label": "Most in", "group": "Valve", "min": -1, "max": 1, "step": 0.01, "default": -1, "tooltip": "How far it opens one way."},
              {"key": "high", "label": "Most out", "group": "Valve", "min": -1, "max": 1, "step": 0.01, "default": 1, "tooltip": "How far it opens the other way."},
          ], ports=valve_ports(h), hydraulics={"kind": "valve"})
    return "", p


def head(size, h, mass, health, kind, keys, name, power=None):
    p = Part(name, SMALL, "Heads", "What a cylinder pivots on. The attach tool's Cylinder mode leaves it at the first point.", role="logic" if kind == "electro" else "frame",
             tool="wrench", color=DARK, group="Joints and Motion")
    p.add(box((-h, -h, -h), (h, h, h)))
    p.add(box((-0.5 * h, h, -0.5 * h), (0.5 * h, 1.3 * h, 0.5 * h), OIL if kind != "electro" else BRASS, detail=True))
    p.sock("+y", (0, h, 0), "+y")
    p.stats(mass, health, 2, 1.0)
    p.set(toolOnly=True, hydraulics=dict(keys, kind=kind), collider="boxes")
    if kind == "electro":
        p.set(power=power, logic={"kind": "scale", "settings": [1], "labels": ["x 1"], "setting": 0},
              config=[{"key": "factor", "label": "Set", "group": "Signal", "min": -10, "max": 10, "step": 0.01, "tooltip": "The Set signal, 0 to 1 of its stroke, is multiplied by this."}])
    else:
        p.set(ports=[port("Oil in", "in", (h, 0, 0), "+x")])
    return "", p


def tray(cls, length, lanes, mass, health):
    """A Cable Tray: a plate and two low walls, open at the ends and the top. Links snap into its lanes, and it guards them."""
    size = {"small": "", "medium": "Medium ", "large": "Large ", "capital": "Capital "}[cls.key]
    name = f"{size}Cable Tray {length:g} m x{lanes}"
    p = Part(name, cls, TRAY_SECTION, f"A node set on it snaps to one of its {lanes} lanes, the auto-router prefers them, and a link in it takes a fifth of the damage.", color=DARK, group="Storage and Logistics")
    return tray_body(p, length, lanes, mass, health, cls.cell / 0.25, name)


def tray_body(p, length, lanes, mass, health, s, name):
    pitch = PITCH * s
    half = pitch * lanes / 2
    end = length / 2
    plate, wall, lane = 0.02 * s, 0.09 * s, 0.09 * s
    p.add(box((-half - 0.02 * s, 0, -end), (half + 0.02 * s, plate, end)))
    for x in (-1, 1):
        lo, hi = sorted((x * half, x * (half + 0.02 * s + 0.004 * s)))
        p.add(box((lo, 0.002 * s, -end + 0.004 * s), (hi, wall, end - 0.004 * s), STEEL, detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("+z", (0, 0.01 * s, end), "+z")
    p.sock("-z", (0, 0.01 * s, -end), "-z")
    p.stats(mass, health, 2, 1.0 if s <= 1 else None)
    p.set(collider="boxes", tray={"at": [0, lane, 0], "axis": [0, 0, 1], "across": [1, 0, 0], "lanes": lanes, "pitch": pitch, "length": length, "guard": length})
    return "", p


def clamp(cls, lanes, mass, health):
    """A Cable Clamp: a saddle and a strap over one, two or three lanes. Short, but it guards a stretch of the run either side."""
    size = {"small": "", "medium": "Medium ", "large": "Large ", "capital": "Capital "}[cls.key]
    name = f"{size}Cable Clamp x{lanes}"
    s = cls.cell / 0.25
    p = Part(name, cls, TRAY_SECTION, "A link set on it snaps to its lane and is held there: the metre of run through it takes a fifth of the damage.", color=STEEL, group="Storage and Logistics")
    pitch = PITCH * s
    half = pitch * lanes / 2
    depth = 0.05 * s
    lane = 0.09 * s
    p.add(box((-half - 0.02 * s, 0, -depth), (half + 0.02 * s, 0.02 * s, depth), DARK))
    for x in (-1, 1):
        lo, hi = sorted((x * half, x * (half + 0.018 * s)))
        p.add(box((lo, 0.002 * s, -depth + 0.004 * s), (hi, 0.17 * s, depth - 0.004 * s), STEEL, detail=True))
    p.add(box((-half - 0.024 * s, 0.17 * s, -depth + 0.008 * s), (half + 0.024 * s, 0.19 * s, depth - 0.008 * s), STEEL, detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.stats(mass, health, 2, 1.0 if s <= 1 else None)
    p.set(collider="boxes", tray={"at": [0, lane, 0], "axis": [0, 0, 1], "across": [1, 0, 0], "lanes": lanes, "pitch": pitch, "length": 2 * depth, "guard": 1.0 * s})
    return "", p


def main():
    parts = []
    for cls, length, lanes, mass, health in TRAYS:
        parts.append(tray(cls, length, lanes, mass, health))
    for cls, lanes, mass, health in CLAMPS:
        parts.append(clamp(cls, lanes, mass, health))
    for row in PUMPS:
        parts.append(pump(*row))
    for cls, mass, health in VALVES:
        parts.append(lever_valve(cls, mass, health))
        parts.append(digital_valve(cls, mass, health))
    for size, h, mass, health, bore, speed, force, slow, watts in HEADS:
        parts.append(head(size, h, mass, health, "cylinder", {"area": bore, "speed": speed}, f"Hydraulic Cylinder Head {size}"))
        parts.append(head(size, h, mass, health, "electro", {"force": force, "speed": slow}, f"Electric Cylinder Head {size}", watts))
    return write_parts(OUT, parts)


if __name__ == "__main__":
    sys.exit(main())
