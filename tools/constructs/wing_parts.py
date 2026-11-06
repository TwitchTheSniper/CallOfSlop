#!/usr/bin/env python3
"""Writes the flying parts: Constructs/Parts/Wings, Constructs/Parts/Rotors and the demo aircraft. docs/sandbox/CONSTRUCTS.md,
"Wings" and "Propellers and rotors".

A wing is a frame with a "surface" block, a control surface one with a "control" inside it, a rotor a part of role "rotor":
what they do in the air is in their numbers, so a new one is a row here. Each family comes in the size classes. Run it from
the repo root: python3 -I tools/constructs/wing_parts.py (--check only compares), then python3 -I tools/constructs/assembling.py
for their recipes. The JSON files are what the game loads: fix this, not them.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, CLASSES, Part, box, write_parts  # noqa: E402

WINGS = os.path.join("Constructs", "Parts", "Wings")
ROTORS = os.path.join("Constructs", "Parts", "Rotors")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")
SKIN, RIB, CLOTH, DARK, TRIM = "#b8bec6", "#7c838c", "#d9cfae", "#30343a", "#c8a13a"
# (name, chord m, span m, kg/m2 at small, cl0, aspect of the wing it makes, stall deg)
SEGMENTS = [
    ("Wing Segment", 1.5, 2, 9, 0.25, 7, 15),
    ("Short Wing Segment", 1.5, 1, 9, 0.25, 7, 15),
    ("Narrow Wing Segment", 1, 2, 8, 0.25, 8, 15),
    ("Tail Plane", 1, 2, 8, 0.0, 4, 14),
]


def name_of(c, base):
    return f"{c.name} {base}" if c.name else base


def group_of(c):
    return "Small" if c is SMALL else None


def wing_body(p, x, z, thick, colour=None):
    """A wing's section along x: a solid slab, a rounded nose and a thin trailing edge on it (drawn, proud of nothing)."""
    p.add(box((-x, -thick / 2, -z), (x, thick / 2, z), hidden=True))
    p.add(box((-x, -thick * 0.46, -z * 0.6), (x, thick * 0.46, z * 0.5), colour, detail=True))
    p.add(box((-x + 0.01, -thick * 0.38, -z), (x - 0.01, thick * 0.38, -z * 0.55), colour, detail=True, shape="rounded", radius=0.5, edges="long"))
    p.add(box((-x + 0.01, -thick * 0.3, z * 0.45), (x - 0.01, thick * 0.3, z), colour or RIB, detail=True, shape="wedge", taper=[1, 0.1], front="+z"))


def segment(c, base, chord, span, kgm2, cl0, aspect, stall):
    x, z = span * c.x / 2, chord * c.x / 2
    area = chord * span * c.x * c.x
    p = Part(name_of(c, base), c, "Wings", f"{area:g} m2 of wing, {span * c.x:g} m of span. Join them along their span; a wing's lift is the sum of its segments, each at its own angle.", tool="welder", color=SKIN, group=group_of(c))
    thick = 0.12 * c.x
    wing_body(p, x, z, thick)
    p.sock("+x", (x, 0, 0), "+x")
    p.sock("-x", (-x, 0, 0), "-x")
    p.sock("+y", (0, thick / 2, 0), "+y")
    p.sock("-y", (0, -thick / 2, 0), "-y")
    p.sock("back", (0, 0, z), "+z")
    p.sock("front", (0, 0, -z), "-z")
    mass = kgm2 * area * c.x ** 0.5
    p.stats(mass, 40 * area ** 0.8 * c.x ** 0.3 + 60, c.armour // 4, 2.0 * c.x ** 0.5)
    p.set(surface={"area": area, "at": [0, 0, round(-z / 2, 4)], "lift": "+y", "forward": "-z", "aspect": aspect, "cl0": cl0, "stall": stall, "cd0": 0.018})
    return ("", p)


def swept(c, side):
    """A swept panel: its tip sits back of its root. Left and right are mirror images, so there are two."""
    sign = 1 if side == "R" else -1
    span, chord, sweep = 2.0 * c.x, 1.5 * c.x, 30
    back = 0.577 * span
    x, z = span / 2, chord / 2
    area = chord * span
    p = Part(name_of(c, f"Swept Wing {side}"), c, "Wings", f"{area:g} m2 of wing swept back {sweep} degrees: less lift a degree, a later stall, less drag fast. A {'right' if side == 'R' else 'left'} wing.", tool="welder", color=SKIN, group=group_of(c))
    n = 4
    for k in range(n):
        x0, x1 = -x + k * span / n, -x + (k + 1) * span / n
        off = back * ((k + 0.5) / n - 0.5) * sign
        p.add(box((x0 + 0.003 * k, -0.06 * c.x, off * sign * sign - z), (x1, 0.06 * c.x, off + z)))
    p.sock("+x", (x, 0, back * 0.375 * sign), "+x")
    p.sock("-x", (-x, 0, -back * 0.375 * sign), "-x")
    p.sock("+y", (0, 0.06 * c.x, 0), "+y")
    p.sock("-y", (0, -0.06 * c.x, 0), "-y")
    p.stats(9 * area * c.x ** 0.5, 40 * area ** 0.8 * c.x ** 0.3 + 60, c.armour // 4, 2.0 * c.x ** 0.5)
    p.set(surface={"area": area, "lift": "+y", "forward": "-z", "aspect": 6, "sweep": sweep, "cl0": 0.2, "stall": 19, "cd0": 0.014})
    return ("", p)


def fabric(c):
    x, z, thick = 1.0 * c.x, 0.75 * c.x, 0.05 * c.x
    area = 4 * x * z
    p = Part(name_of(c, "Fabric Wing Segment"), c, "Wings", f"{area:g} m2 of cloth on a frame: a third of a metal wing's weight, and it lifts less as it tears. Light, and it won't take much.", tool="welder", material="cloth", color=CLOTH, group=group_of(c))
    p.add(box((-x, -thick / 2, -z), (x, thick / 2, z)))
    for at in (-0.7, 0, 0.7):
        w = 0.03 * c.x
        p.add(box((at * x - w, -thick, -z - 0.01), (at * x + w, thick, z + 0.01), RIB, detail=True))
    p.sock("+x", (x, 0, 0), "+x")
    p.sock("-x", (-x, 0, 0), "-x")
    p.sock("+y", (0, thick / 2, 0), "+y")
    p.sock("-y", (0, -thick / 2, 0), "-y")
    p.sock("back", (0, 0, z), "+z")
    p.sock("front", (0, 0, -z), "-z")
    p.stats(3 * area * c.x ** 0.5, 12 * area ** 0.8 + 20, 0, 1.5 * c.x ** 0.5)
    p.set(onBreak="disable", surface={"area": area, "lift": "+y", "forward": "-z", "aspect": 6, "cl0": 0.3, "stall": 14, "cd0": 0.03, "tears": True})
    return ("", p)


def control(c, base, chord, span, about, max_deg, effect, cl0=0.0, vertical=False):
    """A surface with a hinge in the part: its leaf turns by its wire, its first box stays what it collides with."""
    w, ch, t = span * c.x / 2, chord * c.x, 0.06 * c.x
    area = span * chord * c.x * c.x
    p = Part(name_of(c, base), c, "Control surfaces", f"{area:g} m2. {about} Wire its angle, -1 to 1 of {max_deg} degrees.", tool="wrench", color=SKIN, group=group_of(c))
    if vertical:
        p.add(box((-t, -w, 0), (t, w, ch), hidden=True))
        p.add(box((-t * 0.8, -w + 0.01, 0), (t * 0.8, w - 0.01, ch), SKIN, detail=True, side="leaf"))
        p.add(box((-t * 1.2, -w, -0.02 * c.x), (t * 1.2, w, 0.02 * c.x), DARK, detail=True))
        p.sock("hinge", (0, 0, 0), "-z")
        p.sock("+y", (0, w, ch / 2), "+y")
        p.sock("-y", (0, -w, ch / 2), "-y")
        lift, at = "-x", (0, 0, ch / 2)  # (a plus signal swings the nose to the right, as the seat's steering)
    else:
        p.add(box((-w, -t, 0), (w, t, ch), hidden=True))
        p.add(box((-w + 0.01, -t * 0.8, 0), (w - 0.01, t * 0.8, ch), SKIN, detail=True, side="leaf"))
        p.add(box((-w, -t * 1.2, -0.02 * c.x), (w, t * 1.2, 0.02 * c.x), DARK, detail=True))
        p.sock("hinge", (0, 0, 0), "-z")
        p.sock("+x", (w, 0, ch / 2), "+x")
        p.sock("-x", (-w, 0, ch / 2), "-x")
        lift, at = "+y", (0, 0, ch / 2)
    p.stats(8 * area * c.x ** 0.5, 30 * area ** 0.8 + 30, c.armour // 4, 1.5 * c.x ** 0.5)
    p.set(surface={"area": area, "at": list(at), "lift": lift, "forward": "-z", "aspect": 5, "cl0": cl0, "stall": 16, "cd0": 0.02,
                   "control": {"max": max_deg, "rate": 90, "effect": effect, "hinge": [0, 0, 0]}},
          config=[{"key": "reverse", "type": "toggle", "label": "Reverse", "group": "Surface", "tooltip": "It turns the other way for the same signal: a pair of ailerons, one each way."},
                  {"key": "trim", "label": "Trim", "group": "Surface", "unit": "deg", "min": -max_deg, "max": max_deg, "step": 0.5, "default": 0, "tooltip": "Where it sits with no signal."}])
    return ("", p)


def fixed_fin(c):
    w, ch, t = 0.75 * c.x, 0.5 * c.x, 0.05 * c.x
    area = 2 * w * 2 * ch
    p = Part(name_of(c, "Tail Fin"), c, "Wings", f"{area:g} m2 standing up: it keeps a craft pointing the way it flies.", tool="welder", color=SKIN, group=group_of(c))
    p.add(box((-t, -w, -ch), (t, w, ch)))
    p.sock("-y", (0, -w, 0), "-y")
    p.sock("+y", (0, w, 0), "+y")
    p.sock("back", (0, 0, ch), "+z")
    p.sock("front", (0, 0, -ch), "-z")
    p.stats(8 * area * c.x ** 0.5, 30 * area ** 0.8 + 30, c.armour // 4, 2.0)
    p.set(surface={"area": area, "lift": "+x", "forward": "-z", "aspect": 2, "cl0": 0, "stall": 16, "cd0": 0.02})
    return ("", p)


def loose_blade(c):
    r = 1.0 * c.x * (3 if c is LARGE else 1)
    p = Part(name_of(c, "Loose Blade"), c, "Rotors", "A rotor blade, thrown off its hub. Scrap.", tool="wrench", color=DARK, group=group_of(c))
    p.add(box((-r / 2, -0.04 * c.x, -0.02 * c.x), (r / 2, 0.04 * c.x, 0.02 * c.x)))
    p.sock("root", (-r / 2, 0, 0), "-x")
    p.stats(3 * c.x ** 2, 30 * c.x, 0, 1)
    p.set(stage=True)
    return ("", p)


def rotor(name, c, about, radius, kw, rpm, blades, motor=0, shaft=True, heli=False, duct=1, top=95, spool=1.5, health=70, mass=None, colour=DARK):
    """A hub at its origin's end: it mounts by its back and pushes along +z, its blade one box the game repeats round the hub."""
    hub, deep = max(0.08, radius * 0.12), max(0.14, radius * 0.14)
    chord = radius * (0.05 if heli else 0.12)
    mid = (0, 0, deep / 2)
    p = Part(name, c, "Rotors", about, role="rotor", tool="wrench", color=RIB, group=group_of(c))
    p.add(box((-hub, -hub, 0), (hub, hub, deep), hidden=True))
    arm = max(0.02, chord * 0.3)
    p.add(box((-radius, -arm, mid[2] - arm), (radius, arm, mid[2] + arm), hidden=True))
    p.add(box((-arm, -radius, mid[2] - arm), (arm, radius, mid[2] + arm), hidden=True))
    p.add(box((hub, -chord / 2, mid[2] - max(0.012, chord * 0.06)), (radius, chord / 2, mid[2] + max(0.012, chord * 0.06)), colour, detail=True, spin=True))
    p.add(box((-hub, -hub, 0.01), (hub, hub, deep), RIB, detail=True, shape="cylinder", front="+z"))
    if duct > 1:
        p.add(box((-radius - 0.06, -radius - 0.06, -0.02), (radius + 0.06, radius + 0.06, deep + 0.02), "#3f4750", detail=True, shape="tube", inner=round(radius / (radius + 0.06), 3), ends="open", front="+z"))
    p.sock("base", (0, 0, 0), "-z")
    if mass is None:
        mass = 4 * blades * radius ** 2.2 + 8
    p.stats(mass, 80 * max(1, radius) ** 1.5, 2, 2.5)
    b = {"radius": radius, "ratedKw": kw, "ratedRpm": rpm, "blades": blades, "bladeHealth": health, "chord": round(chord, 3), "spool": spool, "topSpeed": top, "duct": duct, "at": list(mid), "axis": "+z", "forward": "+y"}
    if motor:
        b["motorKw"] = motor
    if not shaft:
        b["shaft"] = False
    if heli:
        b["heli"] = True
        b["cyclic"] = 5
        b["hubMoment"] = 0.2
    p.set(rotor=b)
    if shaft:
        p.set(ports=[{"name": "Drive in", "type": "shaft", "dir": "in", "socket": "base"}])
    return ("", p)


def rotors():
    return [
        rotor("Two-Blade Prop", SMALL, "1.8 m across. Bolt it to an engine's Drive out, or link a shaft to it. Its hub is geared to run at its rated speed at the engine's peak.", 0.9, 110, 2400, 2),
        rotor("Three-Blade Prop", SMALL, "2.2 m across, 3 blades, for a stronger engine.", 1.1, 220, 2200, 3, health=80),
        rotor("Electric Prop", SMALL, "1 m across, on its own 24 kW motor: no shaft, it runs off the circuit.", 0.5, 24, 4200, 2, motor=24, shaft=False, health=40, spool=0.6),
        rotor("Ducted Fan", SMALL, "1 m across in a duct, on its own 50 kW motor. Pushes hard for its size. Tilt it on a joint.", 0.5, 50, 6000, 6, motor=50, shaft=False, duct=1.25, top=110, health=30, spool=0.5),
        rotor("Medium Prop", MEDIUM, "7 m across, 4 blades.", 3.5, 2200, 700, 4, health=500, top=110, spool=3),
        rotor("Medium Ducted Fan", MEDIUM, "4 m across in a duct, on its own 1.5 MW motor.", 2, 1500, 1500, 6, motor=1500, shaft=False, duct=1.25, top=110, health=300, spool=1.2),
        rotor("Large Prop", LARGE, "24 m across, 5 blades: a flying fortress's.", 12, 40000, 220, 5, health=6000, top=120, spool=8),
        rotor("Capital Prop", CAPITAL, "76 m across, 6 blades: it carries a city.", 38, 700000, 70, 6, health=80000, top=120, spool=20),
        rotor("Helicopter Rotor", SMALL, "9 m across. Inputs: collective, cyclic pitch, cyclic roll. Its torque turns the craft the other way: a tail rotor has to cancel it.", 4.5, 340, 380, 3, heli=True, spool=4, health=90, top=200),
        rotor("Tail Rotor", SMALL, "2 m across, for a helicopter's tail: link a shaft to it. Collective, either way. Its Torque mix setting cancels the main rotor by itself.", 1, 90, 2000, 3, heli=True, spool=1, health=30, top=200),
        rotor("Medium Helicopter Rotor", MEDIUM, "24 m across. Inputs: collective, cyclic pitch, cyclic roll.", 12, 3500, 140, 5, heli=True, spool=8, health=700, top=200),
        rotor("Medium Tail Rotor", MEDIUM, "4 m across, on its own 700 kW motor, for a big helicopter's tail.", 2, 700, 900, 4, motor=700, shaft=False, heli=True, spool=2, health=200, top=200),
    ]


def wings():
    out = []
    for c in CLASSES:
        for row in SEGMENTS:
            out.append(segment(c, *row))
        out.append(swept(c, "L"))
        out.append(swept(c, "R"))
        out.append(fabric(c))
        out.append(fixed_fin(c))
        out.append(control(c, "Aileron", 0.5, 2, "It rolls the craft: one up, one down (Reverse on one of the pair).", 25, 0.7))
        out.append(control(c, "Elevator", 0.6, 2, "It pitches the craft.", 25, 0.8))
        out.append(control(c, "Rudder", 0.6, 1.5, "It yaws the craft.", 30, 0.8, vertical=True))
        out.append(control(c, "Flap", 0.5, 2, "More lift and more drag: lower it to land.", 40, 0.7, cl0=0.1))
    return out


def blades():
    return [loose_blade(c) for c in CLASSES]


# ---- The demos -----------------------------------------------------------------------------------------------------


def write_blueprint(name, d, check):
    path = os.path.join(BLUEPRINTS, name + ".json")
    text = json.dumps(d, indent=2) + "\n"
    if check:
        return 0 if os.path.exists(path) and open(path).read() == text else (print(f"{path} differs") or 1)
    with open(path, "w") as f:
        f.write(text)
    return 0


def light_plane():
    """A tail-dragger tractor: the engine forward, wings on a block under the seat, a tail on a boom. W and S pitch, A and D yaw, Q and E roll, Shift and Ctrl set the throttle."""
    s = []

    def add(**kw):
        s.append(kw)
        return len(s) - 1

    root = add(part="Frame Block")
    engine = add(part="Engine", on=root, socket="-z", by="-z")  # (its Drive out, +z, faces forward: the prop bolts to it)
    add(part="Two-Blade Prop", on=engine, socket="+z", by="base")
    seat = add(part="Seat", on=root, socket="+y", by="bottom")
    tank = add(part="Fuel Tank", on=root, socket="+z", by="-z")
    boom = add(part="Square Beam 2 m", on=tank, socket="+z", by="-z")
    tail = add(part="Tail Plane", on=boom, socket="+z", by="front")
    fin = add(part="Tail Fin", on=tail, socket="+y", by="-y")
    left = add(part="Wing Segment", on=tank, socket="-x", by="+x")
    right = add(part="Wing Segment", on=tank, socket="+x", by="-x")
    left2 = add(part="Wing Segment", on=left, socket="-x", by="+x")
    right2 = add(part="Wing Segment", on=right, socket="+x", by="-x")
    ail_l = add(part="Aileron", on=left2, socket="back", by="hinge")
    ail_r = add(part="Aileron", on=right2, socket="back", by="hinge", config={"reverse": 1})
    elev = add(part="Elevator", on=tail, socket="back", by="hinge")
    rudd = add(part="Rudder", on=fin, socket="back", by="hinge")
    drive = add(part="Driving Module", on=tank, socket="-y", by="-y")
    leg = add(part="Square Beam 0.5 m", on=root, socket="-y", by="+z")
    axle = add(part="Square Beam 2 m", on=leg, socket="-z", by="+y3", spin=1)
    for end in ("-z", "+z"):
        gear = add(part="Suspension", on=axle, socket=end, by="mount")
        add(part="Wheel", on=gear, socket="axle", by="axle")
    strut = add(part="Square Beam 1 m", on=boom, socket="-y6", by="+z")
    block = add(part="Frame Block", on=strut, socket="-z", by="+y")
    for face in ("-x", "+x"):
        gear = add(part="Suspension", on=block, socket=face, by="mount")
        add(part="Wheel", on=gear, socket="axle", by="axle")
    links = [
        {"type": "data", "from": seat, "to": drive},
        {"type": "data", "from": drive, "to": engine, "toPort": "drive"},
        {"type": "data", "from": drive, "fromPort": "throttle", "to": elev},
        {"type": "data", "from": drive, "fromPort": "roll", "to": ail_l},
        {"type": "data", "from": drive, "fromPort": "roll", "to": ail_r},
        {"type": "data", "from": drive, "fromPort": "steer", "to": rudd},
        {"type": "pipe", "from": tank, "to": engine, "fromPort": "Fuel out", "toPort": "Fuel in"},
    ]
    return {"format": "call-of-slop-construct-blueprint/1", "name": "Light Plane",
            "description": "A single-engine tail-dragger. Shift and Ctrl set the throttle, W and S pitch, A and D the rudder, Q and E roll.", "steps": s, "links": links}


SPIN_T, SPIN_TAIL = 0, 0


def helicopter():
    """An engine straight under its rotor, a tail rotor on a boom held by a bias the driver's keys trim, wheels to stand on."""
    s = []

    def add(**kw):
        s.append(kw)
        return len(s) - 1

    root = add(part="Square Beam 1 m")
    seat = add(part="Seat", on=root, socket="-z", by="back")
    engine = add(part="Turbine", on=root, socket="+y3", by="-z")
    gearbox = add(part="T Gearbox", on=engine, socket="+z", by="-z", spin=SPIN_T)
    main = add(part="Helicopter Rotor", on=gearbox, socket="+z", by="base")
    tank = add(part="Fuel Tank", on=root, socket="-y1", by="+y")
    add(part="Battery", on=tank, socket="+z", by="-z")
    boom = add(part="Square Beam 2 m", on=root, socket="+z", by="-z")
    boom2 = add(part="Square Beam 1 m", on=boom, socket="+z", by="-z")
    add(part="Frame Block", on=seat, socket="+x", by="-x")  # (ballast: the boom's weight, forward of the mast)
    add(part="Frame Block", on=seat, socket="-x", by="+x")
    post = add(part="Square Beam 0.5 m", on=boom2, socket="+y3", by="-z")  # (the tail rotor level with the centre of mass, or its push rolls the craft)
    tailbox = add(part="Right-Angle Gearbox", on=post, socket="+x1", by="+y", spin=SPIN_TAIL)
    tail = add(part="Tail Rotor", on=tailbox, socket="-y", by="base", config={"torqueMix": 1})
    add(part="Tail Fin", on=boom2, socket="+z", by="front")
    drive = add(part="Driving Module", on=tank, socket="-x", by="+x")
    bias = add(part="Scale", on=tank, socket="+x", by="-x", config={"factor": -0.4})
    leg = add(part="Square Beam 0.5 m", on=tank, socket="-y", by="+z")
    axle = add(part="Square Beam 2 m", on=leg, socket="-z", by="+y3", spin=1)
    for end in ("-z", "+z"):
        gear = add(part="Suspension", on=axle, socket=end, by="mount")
        add(part="Wheel", on=gear, socket="axle", by="axle")
    strut = add(part="Square Beam 1 m", on=boom, socket="-y5", by="+z")
    add(part="Suspension", on=strut, socket="-z", by="mount")
    links = [
        {"type": "data", "from": seat, "to": drive},
        {"type": "data", "from": drive, "to": engine, "toPort": "drive"},
        {"type": "data", "from": drive, "fromPort": "throttle", "to": main, "toPort": "cyclic pitch"},
        {"type": "data", "from": drive, "fromPort": "roll", "to": main, "toPort": "cyclic roll"},
        {"type": "data", "from": drive, "fromPort": "steer", "to": bias},
        {"type": "data", "from": bias, "to": tail, "toPort": "collective"},
        {"type": "shaft", "from": gearbox, "fromPort": "Branch shaft", "to": tailbox, "toPort": "Input shaft"},
        {"type": "pipe", "from": tank, "to": engine, "fromPort": "Fuel out", "toPort": "Fuel in"},
    ]
    return {"format": "call-of-slop-construct-blueprint/1", "name": "Small Helicopter",
            "description": "One rotor on an engine, and a tail rotor to hold it straight. Shift and Ctrl set collective, W and S cyclic pitch, Q and E cyclic roll, A and D the tail.", "steps": s, "links": links}


def main():
    check = "--check" in sys.argv
    code = write_parts(WINGS, wings())
    code |= write_parts(ROTORS, rotors() + blades())
    code |= write_blueprint("LightPlane", light_plane(), check)
    code |= write_blueprint("SmallHelicopter", helicopter(), check)
    return code


if __name__ == "__main__":
    sys.exit(main())
