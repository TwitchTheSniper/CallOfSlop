#!/usr/bin/env python3
"""Writes the Sandbox's linear joints (Constructs/Parts/Linear): rail sections with a carriage, free, electric and shaft driven.

docs/sandbox/CONSTRUCTS.md, "Linear joints"; Source/Constructs/ConstructLinear.cs. A section's axis is +z: its middle is where
the carriage rests (the leaf socket "top"), its ends the sockets "end-" and "end+" that a stack joins by. The travel of a
section is its length, so three 2 m sections are a 6 m rail. The carriage's boxes are "side": "leaf" details: the game
moves them with what hangs on it, and shows one carriage on a stack.

Kinds: Free Slider (a "slider" joint: no motor), Linear Actuator (a piston's motor and strength model, on the circuit),
Shaft Linear Drive (the same motor turned by a drive shaft through a lead screw).
Sizes: three gauges of small (1x1, 2x2, 3x3 cells of 0.25 m), and one of Medium, Large and Capital, a few travels each.

Run it from the repo root: python3 -I tools/constructs/linear_parts.py (--check only compares), then
tools/constructs/assembling.py for the recipes. make_joint_sides.py leaves these alone: they draw their own sides.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, fmt, r, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Linear")
BASE, GUIDE, COLLAR, SCREW, HOUSING = "#3b4046", "#a9b0b8", "#f08a12", "#c7a23a", "#2c3238"

# (class, gauge in cells, travels in m, rated force N, top speed m/s, shaft lead m/turn)
SIZES = [
    (SMALL, 1, (0.5, 1, 2), 6_000, 2.0, 0.05),
    (SMALL, 2, (1, 2, 4), 24_000, 2.0, 0.05),
    (SMALL, 3, (2, 4), 60_000, 2.0, 0.05),
    (MEDIUM, 1, (4, 8), 300_000, 2.0, 0.2),
    (LARGE, 1, (16, 32), 5_000_000, 3.0, 0.8),
    (CAPITAL, 1, (32, 64), 200_000_000, 4.0, 3.2),
]
SIDE_LOAD = 5  # N across a driven rail, times its rating: a rail is made to carry what rides on it


def num(key, label, group, unit, lo, hi, step, tip, default=None):
    e = {"key": key, "label": label, "group": group, "unit": unit, "min": lo, "max": hi, "step": step, "tooltip": tip}
    if default is not None:
        e["default"] = default
    return e


def stops():
    return [
        num("stopLow", "Low stop", "Stops", "%", 0, 100, 1, "Where the low end stop sits along the whole rail, a stack too.", 0),
        num("stopHigh", "High stop", "Stops", "%", 0, 100, 1, "Where the high end stop sits along the whole rail, a stack too.", 100),
    ]


def word(c, g):
    return f"{g}x{g}" if c is SMALL else c.name


def section(kind, c, g, travel, force, top, lead):
    w = g * c.cell
    h = w if c is SMALL and g == 1 else w * 0.5  # the rail's height (a 1x1 is square: a small part welds to its side)
    plate = 0.1 * w  # the carriage's plate
    long = min(travel * 0.45, 2 * w)  # the carriage's length
    lip = 0.008 * c.x
    y = h / 2
    head = {"free": "Free Slider", "electric": "Linear Actuator", "shaft": "Shaft Linear Drive"}[kind]
    name = f"{head} {word(c, g)} {fmt(travel)} m"
    kn = f"{force / 1000:g} kN"
    p = Part(name, c, "Linear", {
        "free": f"{fmt(travel)} m of free rail. Stacks end to end. Lock signal holds it.",
        "electric": f"{fmt(travel)} m. Set 0..1 of travel; stacks end to end. Rated {kn}.",
        "shaft": f"{fmt(travel)} m. Shaft turns it: {fmt(lead)} m a turn. Holds with the engine off. Rated {kn}.",
    }[kind], role="joint", tool="wrench", color=BASE, group="Joints")

    # The rail, and its two guides (long details just proud of it, so one run of sections reads as one rail).
    z = travel / 2
    p.add(box((-w / 2, -y, -z), (w / 2, y, z), BASE, side="base"))
    top_y = y + plate + lip * 2.5
    p.add(box((-w * 0.3, y - lip, -z), (w * 0.3, top_y, z), hidden=True))  # (a socket stands on a solid face: the carriage's travel, never drawn)
    p.add(box((-w / 2 - lip, -y * 0.2, -z + lip / 2), (w / 2 + lip, y * 0.45, z - lip / 2), GUIDE, detail=True, side="base", paintable=False))
    if kind == "shaft":
        p.add(box((-w * 0.08, y, -z + lip / 2), (w * 0.08, y + lip * 2, z - lip / 2), SCREW, detail=True, side="base", paintable=False))
        p.add(box((w * 0.3, -y * 0.8, -w * 0.3), (w / 2 + lip * 1.5, y * 0.8, w * 0.3), HOUSING, detail=True, side="base", paintable=False))  # the gear case at Drive in
    if kind == "electric":
        p.add(box((-w * 0.08, y, -z + lip / 2), (w * 0.08, y + lip * 2, z - lip / 2), GUIDE, detail=True, side="base", paintable=False))
        p.add(box((-w / 2 - lip * 1.5, -y * 0.8, -w * 0.3), (-w * 0.3, y * 0.8, w * 0.3), HOUSING, detail=True, side="base", paintable=False))  # the motor

    # The carriage (its side is the leaf: it travels), wrapped over the rail.
    p.add(box((-w / 2 - lip * 2.5, y + lip * 2.5, -long / 2), (w / 2 + lip * 2.5, top_y, long / 2), COLLAR, detail=True, side="leaf", paintable=False))
    for sx in (-1, 1):
        a, b = sx * (w / 2 + lip * 1.5), sx * (w / 2 + lip * 2.5)
        p.add(box((min(a, b), -y * 0.5, -long / 2 + lip), (max(a, b), y + lip * 2.5 + 0.002 * c.x, long / 2 - lip), COLLAR, detail=True, side="leaf", paintable=False))
    # A cue on its top: a bar along the axis, for which way it goes.
    p.add(box((-w * 0.05, top_y, -long * 0.42), (w * 0.05, top_y + lip * 0.8, long * 0.42), "#1c1c1c", detail=True, side="leaf", paintable=False))

    p.sock("-x", (-w / 2, 0, 0), "-x")
    drive_socket = "+x"
    p.sock("+x", (w / 2, 0, 0), "+x")
    p.sock("end-", (0, 0, -z), "-z")
    p.sock("end+", (0, 0, z), "+z")
    p.sock("top", (0, top_y, 0), "+y")
    # Underneath, on the cell centres (the ends stay on the frame's lattice), the middle ones first; and the sides.
    cells = max(1, int(round(travel / c.cell)))
    ats = [(k + 0.5) * c.cell - z for k in range(cells)] if cells % 2 == 0 else [k * c.cell - (cells // 2) * c.cell for k in range(cells)]
    ats.sort(key=abs)
    for n, at in enumerate(ats[:6]):
        p.sock("base" if n == 0 else f"base.{n}", (0, -y, at), "-y")
    k = g * g * travel / c.cell
    if c is SMALL:
        mass = {"free": 1.0, "electric": 1.4, "shaft": 1.3}[kind] * 0.3 * k
        health = 100 + 8 * k
        armour = 4 + g
        seconds = 1.5 + 0.5 * g
    else:
        area = 4 * w * travel
        mass = area * c.kg_m2
        health = area * c.health_m2
        armour = c.armour + 8
        seconds = c.seconds * 1.5
    p.stats(mass, health, armour, seconds)

    joint = {"type": "slider" if kind == "free" else "piston", "axis": "+z", "min": r(-z), "max": r(z), "leaf": "top"}
    speed = top * 0.5
    if kind != "free":
        joint.update(speed=speed, strength=force, give=r(0.25 * travel), sideLoad=SIDE_LOAD * force, overloadDamage=0)
    joint["rail"] = {"gauge": g, "drive": kind}
    if kind == "shaft":
        joint["speed"] = top
        joint["rail"]["lead"] = lead
    config = []
    if kind == "electric":
        config += [
            {"key": "setMode", "type": "choice", "label": "Set signal is", "group": "Motor", "options": ["Position", "Speed"], "default": "Position",
             "tooltip": "Position: 0 is one end, 1 the other. Speed: -1 to 1 of its speed, 0 holds."},
            num("rest", "Rest position", "Motor", "%", 0, 100, 1, "Where it sits with nothing wired into Set: % along the rail.", 50),
            num("speed", "Speed", "Motor", "m/s", 0.05, top, 0.05, f"How fast it slides, up to {top:g} m/s."),
            num("strength", "Force", "Motor", "N", force / 10, force, max(100, force / 100), f"What it holds, up to {force} N."),
        ]
    if kind == "shaft":
        config += [
            num("lead", "Lead", "Screw", "m/turn", r(lead / 5), r(lead * 4), r(lead / 10), "How far a turn of the shaft moves it. Longer: faster, less force.", lead),
            num("strength", "Force", "Screw", "N", force / 10, force, max(100, force / 100), f"What it holds, up to {force} N."),
        ]
    config += stops()
    p.set(joint=joint, config=config, centreSockets=False)
    if kind == "electric":
        p.set(power=r(10 * k / 16) if c is SMALL else r(force * top / 20000), ports=[{"name": "Power in", "type": "power", "dir": "in", "at": [r(-w / 2), 0.0, 0.0], "face": "-x"}])
    elif kind == "shaft":
        p.set(power=0, ports=[{"name": "Drive in", "type": "shaft", "dir": "in", "socket": drive_socket}])
    else:
        p.set(power=0)
    return p


def everything():
    out = []
    for c, g, travels, force, top, lead in SIZES:
        for travel in travels:
            for kind, folder in (("free", "Free"), ("electric", "Electric"), ("shaft", "Shaft")):
                out.append((folder, section(kind, c, g, travel, force, top, lead)))
    return out


if __name__ == "__main__":
    sys.exit(write_parts(OUT, everything()))
