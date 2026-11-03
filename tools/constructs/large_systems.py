#!/usr/bin/env python3
"""Writes the Sandbox's big systems (Constructs/Parts/Systems): what makes a mothership work.

docs/sandbox/CONSTRUCTS.md, "Large systems". The hulls, adapters and limits are the size classes' (tools/constructs/
scale.py); engines and propulsion are their own family. Here: ramps, gates, lifts, catapults, arrestors, docks and
magnetic locks; rated pistons, hinges and turntables; winches, booms and conveyors; big guns, magazines and hoists; the
bridge, fire director, gauges and sensors; lights; walkways, ladders, scaffold, doors.

A traction city (a tracked land mothership) launches down ramps and lifts first, catapults second, so every class has
a ramp deck, a lift platform and an arched gate.

The numbers are data, in the tables below. Run it from the repo root: python3 -I tools/constructs/large_systems.py
(it rewrites the folder; --check only compares). Fix a family here, never in its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import MEDIUM, LARGE, CAPITAL, SMALL, Part, box, write_parts, fmt, file_name  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Systems")
GROUP_SMALL = "Ship systems"  # small-class parts for big constructs
BIG = (MEDIUM, LARGE, CAPITAL)

STEEL = "#6d747c"
DARK = "#3d4248"
GOLD = "#c7a23a"
HAZARD = "#b8902e"
RUST = "#8a5a3a"
WOOD = "#6f5436"
LANTERN = "#d9642a"
BRIGHT = "#fff0c0"


def plate_mass(c, area):
    return area * c.kg_m2


def plate_health(c, area):
    return area * c.health_m2


def num(key, label, group, unit, lo, hi, step, tip, default=None):
    e = {"key": key, "label": label, "group": group, "unit": unit, "min": lo, "max": hi, "step": step, "tooltip": tip}
    if default is not None:
        e["default"] = default
    return e


def edges(p, x0, x1, z0, z1, y=0.0):
    """Sockets on a deck's four edges, at the middle of the cell nearest each edge's middle."""
    c = p.cls
    mx = round((x0 + x1) / 2 / c.cell) * c.cell
    mz = round((z0 + z1) / 2 / c.cell) * c.cell
    p.sock("+x", (x1, y, mz), "+x")
    p.sock("-x", (x0, y, mz), "-x")
    p.sock("+z", (mx, y, z1), "+z")
    p.sock("-z", (mx, y, z0), "-z")


def deck(name, c, section, desc, nx, nz, thick, role="panel", color=STEEL, pedestal=True, stripes=HAZARD, group=None, tool="welder"):
    """A flat deck nx by nz cells, its top plate on the middle of its cells' height (so a drawbridge's tip is thin), its
    origin in the middle cell along z and in cell 0 along x; with a pedestal under the middle for what carries it."""
    p = Part(name, c, section, desc, role=role, color=color, group=group, tool=tool)
    x0, x1 = c.lo(), c.hi(nx)
    hz = nz * c.cell / 2
    t = thick / 2
    p.add(box((x0, -t, -hz), (x1, t, hz)))
    if pedestal:
        mx = round((x0 + x1) / 2 / c.cell) * c.cell
        p.add(box((mx - c.half * 0.7, c.lo(), -c.half * 0.7), (mx + c.half * 0.7, -t, c.half * 0.7), DARK))
        p.sock("-y", (mx, c.lo(), 0), "-y")
    # Hazard stripes along the long edges (a little proud of the deck).
    p.add(box((x0 + 0.02, t, hz - 0.06 * c.x), (x1 - 0.02, t + 0.004 * c.x, hz - 0.02 * c.x), stripes, detail=True))
    p.add(box((x0 + 0.02, t, -hz + 0.02 * c.x), (x1 - 0.02, t + 0.004 * c.x, -hz + 0.06 * c.x), stripes, detail=True))
    edges(p, x0, x1, -hz, hz)
    # Four sockets on the top, at its corner cells: what stands on a deck (a gate's pillars, a crane's foot).
    zc = (nz - 1) // 2 * c.cell
    for name, x, z in (("+y.0-", 0, -zc), ("+y.0+", 0, zc), ("+y.1-", (nx - 1) * c.cell, -zc), ("+y.1+", (nx - 1) * c.cell, zc)):
        p.sock(name, (x, t, z), "+y")
    area = (x1 - x0) * 2 * hz
    p.stats(plate_mass(c, area) * 1.4, plate_health(c, area), c.armour)
    p.set(walk="floor", centreSockets=False)
    return p


# ---- Launching and landing --------------------------------------------------------------------------------------

RAMPS = {MEDIUM: (8, 5), LARGE: (5, 5), CAPITAL: (3, 3)}  # cells long, cells wide
LIFTS = {MEDIUM: (5, 5), LARGE: (5, 5), CAPITAL: (3, 3)}
DOORS = {MEDIUM: (5, 3), LARGE: (5, 3), CAPITAL: (3, 2)}  # cells wide, cells tall
GATES = {MEDIUM: (5, 5), LARGE: (5, 5), CAPITAL: (3, 3)}  # cells wide, cells tall


def ramp(c):
    n, w = RAMPS[c]
    size = f"{fmt(n * c.cell)}x{fmt(w * c.cell)}"
    p = deck(f"Ramp Deck {c.name} {size}", c, "Ramps and gates", "A drawbridge or ramp deck: hang it on a motor hinge by its -x edge and lower it to the ground, and vehicles drive off down it. Thin at its tip.", n, w, 0.2 * c.cell, pedestal=False)
    return p


def lift(c):
    n, w = LIFTS[c]
    size = f"{fmt(n * c.cell)}x{fmt(w * c.cell)}"
    return deck(f"Lift Platform {c.name} {size}", c, "Lifts", "A deck lift's platform: bolt its pedestal on a piston's top. What stands on it rides it, and its weight counts on the piston's rating.", n, w, 0.2 * c.cell)


def door_leaf(c):
    wide, tall = DOORS[c]
    p = Part(f"Hangar Door {c.name} {fmt(wide * c.cell)}x{fmt(tall * c.cell)}", c, "Ramps and gates", "A sliding hangar door or hatch: put a piston's top on its edge socket and wire the piston, or hang it on a motor hinge.", role="panel", color=RUST)
    x0, x1, y0, y1 = c.lo(), c.hi(wide), c.lo(), c.hi(tall)
    t = 0.1 * c.cell
    p.add(box((x0, y0, -t), (x1, y1, t)))
    for k in range(1, 4):
        at = x0 + (x1 - x0) * k / 4
        p.add(box((at - 0.02 * c.x, y0 + 0.01, -t - 0.004 * c.x), (at + 0.02 * c.x, y1 - 0.01, t + 0.004 * c.x), DARK, detail=True))
    p.sock("-x", (x0, 0, 0), "-x")
    p.sock("+x", (x1, 0, 0), "+x")
    p.sock("-y", (0, y0, 0), "-y")
    p.sock("+y", (0, y1, 0), "+y")
    area = (x1 - x0) * (y1 - y0)
    p.stats(plate_mass(c, area), plate_health(c, area), c.armour + 8)
    p.set(centreSockets=False)
    return p


def gate(c):
    wide, tall = GATES[c]
    p = Part(f"Arched Gate {c.name} {fmt(wide * c.cell)}x{fmt(tall * c.cell)}", c, "Ramps and gates", "A great arched gateway: pillars, a lintel and an arch. Vehicles drive through it; hangar doors or gate leaves close it.", role="frame", color=RUST)
    x0, x1, y0, y1 = c.lo(), c.hi(wide), c.lo(), c.hi(tall)
    z = c.half
    p.add(box((x0, y0, -z), (x0 + c.cell, y1, z)))
    p.add(box((x1 - c.cell, y0, -z), (x1, y1, z)))
    p.add(box((x0 + c.cell, y1 - c.cell, -z), (x1 - c.cell, y1, z)))
    # The arch's stepped shoulders, drawn only.
    inner0, inner1 = x0 + c.cell, x1 - c.cell
    top = y1 - c.cell
    for k, inset in enumerate((0.18, 0.36, 0.54)):
        span = (inner1 - inner0) * inset / 2
        drop = c.cell * 0.28 * (3 - k)
        p.add(box((inner0, top - drop, -z * 0.9), (inner0 + span, top, z * 0.9), "#a0683f", detail=True))
        p.add(box((inner1 - span, top - drop, -z * 0.9), (inner1, top, z * 0.9), "#a0683f", detail=True))
    p.sock("-x", (x0, 0, 0), "-x")
    p.sock("+x", (x1, 0, 0), "+x")
    p.sock("jamb-", (x0 + c.cell, 0, 0), "+x")
    p.sock("jamb+", (x1 - c.cell, 0, 0), "-x")
    p.sock("-y-", (0, y0, 0), "-y")
    p.sock("-y+", ((wide - 1) * c.cell, y0, 0), "-y")
    p.sock("+y", (((wide - 1) // 2) * c.cell, y1, 0), "+y")
    p.sock("+z", (((wide - 1) // 2) * c.cell, (tall - 1) * c.cell, z), "+z")
    p.sock("-z", (((wide - 1) // 2) * c.cell, (tall - 1) * c.cell, -z), "-z")
    area = (wide * tall - (wide - 2) * (tall - 1)) * c.cell * c.cell
    p.stats(plate_mass(c, area) * 4, plate_health(c, area) * 3, c.armour + 10)
    p.set(centreSockets=False)
    return p


CATAPULTS = {  # cells long, accel m/s2, kg, zone width, zone height, W
    MEDIUM: (16, 25, 20000, 6, 6, 500),
    LARGE: (8, 25, 200000, 16, 16, 5000),
    CAPITAL: (4, 25, 2000000, 40, 32, 50000),
}
ARRESTORS = {  # cells long, decel m/s2, kg, zone width, zone height, W
    MEDIUM: (16, 12, 20000, 6, 4, 200),
    LARGE: (8, 12, 200000, 16, 8, 2000),
    CAPITAL: (4, 12, 2000000, 40, 16, 20000),
}


def rail(c, name, desc, kind, n, accel, mass, width, height, power):
    p = Part(name, c, "Launch and landing", desc, role="logic", color=DARK)
    x = c.half
    z0, z1 = -c.hi(n - 1) - c.cell, c.half  # forward (-z) from its cell 0
    t = 0.1 * c.cell
    p.add(box((-x, -t, z0), (x, t, z1)))
    p.add(box((-c.half * 0.7, c.lo(), -c.half * 0.7), (c.half * 0.7, -t, c.half * 0.7), STEEL))
    p.add(box((-0.03 * c.x, t, z0 + 0.02), (0.03 * c.x, t + 0.012 * c.x, z1 - 0.02), GOLD, detail=True))
    p.sock("-y", (0, c.lo(), 0), "-y")
    p.sock("+z", (0, 0, z1), "+z")
    p.sock("-z", (0, 0, z0), "-z")
    p.sock("+x", (x, 0, 0), "+x")
    p.sock("-x", (-x, 0, 0), "-x")
    area = (z1 - z0) * 2 * x
    p.stats(plate_mass(c, area) * 2, plate_health(c, area), c.armour)
    p.set(logic={"kind": kind}, launch={"width": width, "height": height, "accel": accel, "maxMass": mass}, walk="floor", power=power, centreSockets=False)
    return p


def catapult(c):
    n, a, m, w, h, pw = CATAPULTS[c]
    return rail(c, f"Catapult {c.name} {fmt(n * c.cell)} m", f"A launch rail: on a rising signal or F it throws what stands on it (up to {m // 1000} t) forward along the rail at {a} m/s2, drawing the energy from the batteries. It lets go of a docked vehicle first.", "launch", n, a, m, w, h, pw)


def arrestor(c):
    n, a, m, w, h, pw = ARRESTORS[c]
    return rail(c, f"Arrestor Gear {c.name} {fmt(n * c.cell)} m", f"Landing gear for a deck: while on (unwired: always) it slows what moves through its zone, up to {m // 1000} t at {a} m/s2, until it moves with the ship.", "arrest", n, a, m, w, h, pw)


DOCKS = {  # box (w, h, l), longest side, kg, settle m/s, kW, l/s
    MEDIUM: ((8, 4, 8), 32, 30000, 2.0, 50, 2),
    LARGE: ((24, 12, 24), 96, 300000, 3.0, 500, 10),
    CAPITAL: ((64, 32, 64), 128, 3000000, 4.0, 5000, 50),
}


def box_config(side, size, y):
    return [
        {"key": "startOn", "label": "Starts holding", "group": "Dock", "default": False, "tooltip": "A newly built dock starts holding what comes into its box."},
        num("sizeX", "Width", "Box", "m", 0.5, side, 0.5, "The box's size across the part.", size[0]),
        num("sizeY", "Height", "Box", "m", 0.5, side, 0.5, "The box's size up from the part.", size[1]),
        num("sizeZ", "Length", "Box", "m", 0.5, side, 0.5, "The box's size along the part.", size[2]),
        num("atX", "Centre x", "Box", "m", -side, side, 0.25, "Where the box's centre is from the part (x).", 0),
        num("atY", "Centre y", "Box", "m", -side, side, 0.25, "Where the box's centre is from the part (y).", y),
        num("atZ", "Centre z", "Box", "m", -side, side, 0.25, "Where the box's centre is from the part (z).", 0),
    ]


def dock(c):
    size, side, mass, settle, kw, fuel = DOCKS[c]
    p = Part(f"Docking Clamp {c.name}", c, "Launch and landing", f"Holds a vehicle fast to this construct, crew aboard: on a signal or F it catches the body up to {mass // 1000} t with its centre in the box, moving under {settle:g} m/s against it, and lets go on the next. While docked it charges and refuels it, and its bins unload into yours.", role="logic", color=DARK)
    h = c.half
    p.add(box((-h, -h, -h), (h, h * 0.2, h)))
    p.add(box((-h * 0.8, h * 0.2, -h * 0.8), (-h * 0.3, h, h * 0.8), HAZARD, detail=True))
    p.add(box((h * 0.3, h * 0.2, -h * 0.8), (h * 0.8, h, h * 0.8), HAZARD, detail=True))
    p.sock("-y", (0, -h, 0), "-y")
    p.sock("+x", (h, 0, 0), "+x")
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("+z", (0, 0, h), "+z")
    p.sock("-z", (0, 0, -h), "-z")
    p.stats(c.cell * c.cell * c.kg_m2 * 6, c.cell * c.cell * c.health_m2 * 3, c.armour)
    p.set(logic={"kind": "cargo"}, dock={"side": side, "settle": settle, "maxMass": mass, "charge": kw, "refuel": fuel, "chute": True}, power=kw * 20,
          config=box_config(side, size, size[1] / 2 + h * 0.2))
    return p


MAG_LOCKS = {SMALL: (0.75, (3, 2.5, 3), 8, 6000, 1.5, 300), MEDIUM: (1.0, (8, 5, 8), 16, 60000, 2.0, 3000)}


def mag_lock(c):
    pad, size, side, mass, settle, power = MAG_LOCKS[c]
    name = "Magnetic Lock" if c is SMALL else "Magnetic Lock Medium"
    p = Part(name, c, "Launch and landing", f"An electromagnet: on a signal or F it holds a body up to {mass // 1000} t with its centre in the box fast to this construct, and a signal or a dead battery lets it go.", role="logic", color="#4a5560",
             group=GROUP_SMALL if c is SMALL else None, tool="wrench" if c is SMALL else "welder")
    n = 3 if c is SMALL else 1
    x = n * c.cell / 2
    y = c.half
    p.add(box((-x, -y, -x), (x, y * 0.4, x)))
    p.add(box((-x * 0.8, y * 0.4, -x * 0.8), (x * 0.8, y * 0.7, x * 0.8), "#c23a2a", detail=True))
    p.sock("-y", (0, -y, 0), "-y")
    p.sock("+x", (x, 0, 0), "+x")
    p.sock("-x", (-x, 0, 0), "-x")
    p.sock("+z", (0, 0, x), "+z")
    p.sock("-z", (0, 0, -x), "-z")
    p.stats(12 if c is SMALL else 300, 90 if c is SMALL else 800, c.armour)
    p.set(logic={"kind": "cargo"}, dock={"side": side, "settle": settle, "maxMass": mass}, power=power, config=box_config(side, size, size[1] / 2 + y * 0.4))
    return p


# ---- Moving things: rated actuators -------------------------------------------------------------------------------

PISTONS = {  # strokes (m), N, side N, top m/s, length in cells
    MEDIUM: ((4, 8), 300_000, 100_000, 1.0, 2),
    LARGE: ((16, 32), 5_000_000, 2_000_000, 2.0, 2),
    CAPITAL: ((32, 64), 200_000_000, 80_000_000, 4.0, 1),
}
HINGES = {MEDIUM: (500_000, 30), LARGE: (50_000_000, 15), CAPITAL: (1_000_000_000, 8)}  # N m, deg/s
TURNTABLES = {MEDIUM: (500_000, 45), LARGE: (50_000_000, 25), CAPITAL: (1_500_000_000, 12)}  # N m, deg/s


def piston(c, stroke):
    _, force, side, top, n = PISTONS[c]
    p = Part(f"Piston {c.name} {fmt(stroke)} m", c, "Pistons", f"A rated hydraulic ram: a signal of 1 pushes what sits on its top out {fmt(stroke)} m, 0 pulls it in. Holds {force // 1000} kN rigidly ({side // 1000} kN across it); more and it gives way.", role="joint", color=STEEL)
    h = c.half
    y1 = c.hi(n)
    p.add(box((-h, -h, -h), (h, y1, h), side="base"))
    p.add(box((-h * 0.6, y1 - 0.02 * c.x, -h * 0.6), (h * 0.6, y1 + 0.03 * c.x, h * 0.6), GOLD, detail=True, side="leaf"))
    p.sock("base", (0, -h, 0), "-y")
    p.sock("top", (0, y1, 0), "+y")
    p.sock("+x", (h, 0, 0), "+x")
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("+z", (0, 0, h), "+z")
    p.sock("-z", (0, 0, -h), "-z")
    area = 4 * c.cell * (y1 + h)
    p.stats(plate_mass(c, area), plate_health(c, area), c.armour + 8, c.seconds * 1.5)
    p.set(power=force * top / 1000, joint={"type": "piston", "axis": "+y", "min": 0, "max": stroke, "leaf": "top", "speed": top * 0.5, "strength": force, "give": 0.25 * stroke, "sideLoad": side, "overloadDamage": 0},
          config=[
              num("min", "Pulled in to", "Travel", "m", 0, stroke, 0.05, "How far out it rests when pulled in."),
              num("max", "Pushed out to", "Travel", "m", 0, stroke, 0.05, "How far out it goes when pushed out (a signal of 1)."),
              num("speed", "Speed", "Motor", "m/s", 0.05, top, 0.05, f"How fast it slides (up to {top:g} m/s)."),
              num("strength", "Force", "Motor", "N", force / 10, force, force / 100, f"Its rating: what it holds rigidly (up to {force} N)."),
          ], centreSockets=False)
    return p


def motor_hinge(c):
    torque, top = HINGES[c]
    p = Part(f"Motor Hinge {c.name}", c, "Motor hinges", f"A rated hinge for ramps, drawbridges, cranes and gun cradles: wire a signal into it (1 opens to max, -1 to min, 0 shuts). Holds {torque // 1000} kN m rigidly; more and it gives way.", role="joint", color="#4f6a7a")
    h = c.half
    p.add(box((-h, -h, -h), (h, h, h), side="base"))
    p.add(box((h - 0.02 * c.x, -h * 0.5, -h * 0.5), (h + 0.02 * c.x, h * 0.5, h * 0.5), GOLD, detail=True, side="leaf"))
    p.sock("base", (-h, 0, 0), "-x")
    p.sock("leaf", (h, 0, 0), "+x")
    area = 6 * c.cell * c.cell
    p.stats(plate_mass(c, area), plate_health(c, area), c.armour + 8, c.seconds * 1.5)
    p.set(power=torque * top * 3.14159 / 180 / 1000, joint={"type": "motor", "axis": "+y", "pivot": [h, 0, 0], "min": -100, "max": 100, "leaf": "leaf", "speed": top, "strength": torque, "give": 20, "overloadDamage": 0},
          config=[
              num("min", "Lowest angle", "Limits", "deg", -100, 100, 1, "The far end a signal of -1 turns it to."),
              num("max", "Highest angle", "Limits", "deg", -100, 100, 1, "The far end a signal of 1 (or opening it) turns it to."),
              num("speed", "Speed", "Motor", "deg/s", 1, top, 1, f"How fast it turns (up to {top} deg/s)."),
              num("strength", "Torque", "Motor", "N m", torque / 10, torque, torque / 100, f"Its rating: what it holds rigidly (up to {torque} N m)."),
          ], centreSockets=False)
    return p


def turntable(c):
    torque, top = TURNTABLES[c]
    p = Part(f"Turntable {c.name}", c, "Turntables", f"A rated bearing for turrets and cranes: a signal of 1 turns it one way at full speed, -1 the other, 0 holds it. Holds {torque // 1000} kN m rigidly; more and it gives way.", role="joint", color="#5b6168")
    h = c.half
    y = h * 0.6
    p.add(box((-h, -h, -h), (h, y, h), side="base"))
    p.add(box((-h * 0.8, y, -h * 0.8), (h * 0.8, h, h * 0.8), GOLD, side="leaf"))
    p.sock("base", (0, -h, 0), "-y")
    p.sock("top", (0, h, 0), "+y")
    p.sock("+x", (h, 0, 0), "+x")
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("+z", (0, 0, h), "+z")
    p.sock("-z", (0, 0, -h), "-z")
    area = 6 * c.cell * c.cell
    p.stats(plate_mass(c, area) * 1.5, plate_health(c, area) * 1.5, c.armour + 10, c.seconds * 1.5)
    p.set(power=torque * top * 3.14159 / 180 / 1000, joint={"type": "turntable", "axis": "+y", "pivot": [0, h, 0], "leaf": "top", "speed": top, "strength": torque, "give": 20, "overloadDamage": 0},
          config=[
              num("speed", "Speed", "Motor", "deg/s", 1, top, 1, f"How fast it turns at a full signal (up to {top} deg/s)."),
              num("strength", "Torque", "Motor", "N m", torque / 10, torque, torque / 100, f"Its rating: what it holds rigidly (up to {torque} N m)."),
          ], centreSockets=False)
    return p


GATE_HINGES = {MEDIUM: (2, 3), LARGE: (2, 3), CAPITAL: (2, 2)}  # the leaf's cells wide, tall


def gate_hinge(c):
    p = Part(f"Gate Hinge {c.name}", c, "Ramps and gates", "A heavy passive hinge for a gate leaf: F swings the leaf, a signal locks it.", role="hinge", color=DARK)
    h = c.half * 0.5
    p.add(box((-h, -c.half, -h), (0, c.half, h), side="base"))
    p.add(box((0.01 * c.x, -c.half * 0.8, -h * 0.8), (h, c.half * 0.8, h * 0.8), GOLD, side="leaf"))
    p.sock("base", (-h, 0, 0), "-x")
    p.sock("leaf", (h, 0, 0), "+x", kind="leaf")
    p.stats(c.cell * c.cell * c.kg_m2, c.cell * c.cell * c.health_m2, c.armour)
    p.set(hinge={"axis": "+y", "min": -110, "max": 110, "leaf": "leaf"})
    return p


def gate_leaf(c):
    wide, tall = GATE_HINGES[c]
    p = Part(f"Gate Leaf {c.name} {fmt(wide * c.cell)}x{fmt(tall * c.cell)}", c, "Ramps and gates", "One leaf of a great gate: hang it on a gate hinge.", role="door", color=RUST)
    x0, x1, y0, y1 = 0.0, wide * c.cell, -c.half, c.hi(tall)
    t = 0.08 * c.cell
    p.add(box((x0, y0, -t), (x1, y1, t)))
    for k in (0.25, 0.5, 0.75):
        y = y0 + (y1 - y0) * k
        p.add(box((x0 + 0.01, y - 0.03 * c.x, -t - 0.004 * c.x), (x1 - 0.01, y + 0.03 * c.x, t + 0.004 * c.x), DARK, detail=True))
    p.sock("hang", (x0, 0, 0), "-x", kind="leaf")
    area = (x1 - x0) * (y1 - y0)
    p.stats(plate_mass(c, area), plate_health(c, area), c.armour + 6)
    return p


WINCHES = {  # chain m, min m, m/s, N, W
    MEDIUM: (16, 1, 2, 300_000, 20_000),
    LARGE: (32, 2, 3, 3_000_000, 200_000),
    CAPITAL: (64, 4, 4, 30_000_000, 900_000),
}


def winch(c):
    chain, lo, speed, rating, power = WINCHES[c]
    p = Part(f"Winch {c.name} {chain} m", c, "Cranes and winches", f"A powered drum with {chain} m of chain and a hook: F with the wrench hooks a lifting eye in reach, then a signal of 1 reels in and -1 pays out, lifting up to {rating // 10000} t.", role="hitch", color=DARK)
    h = c.half
    p.add(box((-h, -h, -h), (h, h, h)))
    p.add(box((-h * 0.25, -h * 1.3, -h * 0.25), (h * 0.25, -h, h * 0.25), GOLD, detail=True))
    p.sock("+y", (0, h, 0), "+y")
    p.sock("+x", (h, 0, 0), "+x")
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("+z", (0, 0, h), "+z")
    p.sock("-z", (0, 0, -h), "-z")
    p.stats(c.cell * c.cell * c.kg_m2 * 4, c.cell * c.cell * c.health_m2 * 3, c.armour)
    p.set(hitch={"kind": "hook", "at": [0, -h * 1.3, 0], "chain": chain, "winch": {"min": lo, "speed": speed, "rating": rating}}, power=power)
    return p


def lifting_eye(c):
    p = Part(f"Lifting Eye {c.name}", c, "Cranes and winches", "A ring on top of a load: a winch's hook takes it.", role="hitch", color=GOLD)
    h = c.half * 0.5
    p.add(box((-h, 0, -h), (h, h, h)))
    p.sock("-y", (0, 0, 0), "-y")
    p.stats(c.cell * c.cell * c.kg_m2 * 0.2, c.cell * c.cell * c.health_m2, c.armour)
    p.set(hitch={"kind": "eye", "at": [0, h, 0]})
    return p


BOOMS = {MEDIUM: 8, LARGE: 8, CAPITAL: 4}  # cells


def boom(c):
    n = BOOMS[c]
    p = Part(f"Crane Boom {c.name} {fmt(n * c.cell)} m", c, "Cranes and winches", "A lattice crane jib or gantry beam: four chords and bracing. Goes on a motor hinge or a turntable.", role="frame", color=HAZARD)
    h = c.half
    x0, x1 = c.lo(), c.hi(n)
    t = 0.14 * c.cell
    for sy in (-1, 1):
        for sz in (-1, 1):
            y, z = sy * (h - t), sz * (h - t)
            p.add(box((x0, min(y, y + sy * t), min(z, z + sz * t)), (x1, max(y, y + sy * t), max(z, z + sz * t))))
    ties = 10
    for k in range(ties):
        x = x0 + (x1 - x0) * (k + 1) / (ties + 1)
        p.add(box((x - t / 2, -h + 0.02, -h * 0.82), (x + t / 2, h - 0.02, -h * 0.74), DARK, detail=True))
        p.add(box((x - t / 2, -h + 0.02, h * 0.74), (x + t / 2, h - 0.02, h * 0.82), DARK, detail=True))
    p.add(box((x0, -h, -h), (x0 + 0.1 * c.cell, h, h), DARK))
    p.add(box((x1 - 0.1 * c.cell, -h, -h), (x1, h, h), DARK))
    p.sock("-x", (x0, 0, 0), "-x")
    p.sock("+x", (x1, 0, 0), "+x")
    tip = (n - 1) * c.cell
    p.sock("tip+y", (tip, h, 0), "+y")
    p.sock("tip-y", (tip, -h, 0), "-y")
    p.sock("root+y", (0, h, 0), "+y")
    p.sock("root-y", (0, -h, 0), "-y")
    p.stats(n * c.cell * c.cell * c.kg_m2 * 1.2, n * c.cell * c.cell * c.health_m2, c.armour)
    p.set(centreSockets=False)
    return p


CONVEYORS = {MEDIUM: (4, 2, 2.0, 2000), LARGE: (4, 2, 4.0, 40000), CAPITAL: (2, 1, 8.0, 500000)}  # cells long, cells wide, m/s, W


def conveyor(c):
    n, w, speed, power = CONVEYORS[c]
    p = Part(f"Conveyor {c.name} {fmt(n * c.cell)}x{fmt(w * c.cell)}", c, "Cranes and winches", f"A powered belt: a signal sets its speed (1 forward, -1 back; up to {speed:g} m/s) or, unwired, F runs and stops it. It carries crates, parts and vehicles along its top.", role="logic", color="#2f3338")
    h = c.half
    x0, x1 = -h, c.hi(w)
    z0, z1 = -c.hi(n - 1) - c.cell, h
    p.add(box((x0, -h, z0), (x1, h * 0.2, z1)))
    for k in range(1, 8):
        z = z0 + (z1 - z0) * k / 8
        p.add(box((x0 + 0.02, h * 0.2, z - 0.01 * c.x), (x1 - 0.02, h * 0.2 + 0.006 * c.x, z + 0.01 * c.x), "#7c838c", detail=True))
    p.sock("-y", (0, -h, 0), "-y")
    p.sock("+z", (0, 0, z1), "+z")
    p.sock("-z", (0, 0, z0), "-z")
    p.sock("+x", (x1, 0, 0), "+x")
    p.sock("-x", (x0, 0, 0), "-x")
    area = (x1 - x0) * (z1 - z0)
    p.stats(plate_mass(c, area) * 2, plate_health(c, area), c.armour)
    p.set(logic={"kind": "conveyor"}, conveyor={"speed": speed, "dir": "-z"}, power=power, walk="floor")
    return p


# ---- Big weapons --------------------------------------------------------------------------------------------------

GUNS = [  # name, class, barrel cells, barrels, gun block, boxes' mass (kg)
    ("Flak Cannon", MEDIUM, 3, 2, {"damage": 90, "rpm": 180, "spread": 0.6, "range": 500, "maxRange": 800, "mag": 40, "reload": 4, "flags": ["explosive"], "blast": 5, "blastDamage": 120, "kick": 300, "fx": "cannon", "speed": 500, "soundOf": "Leviathan .50"}, 3000),
    ("Naval Gun 20 cm", LARGE, 2, 1, {"damage": 1400, "rpm": 8, "spread": 0.15, "range": 800, "maxRange": 1000, "mag": 1, "reload": 6, "flags": ["explosive"], "blast": 12, "blastDamage": 1600, "kick": 8000, "fx": "cannon", "speed": 600, "soundOf": "Leviathan .50"}, 60000),
    ("Naval Gun 40 cm", CAPITAL, 2, 1, {"damage": 6000, "rpm": 3, "spread": 0.1, "range": 1000, "maxRange": 1000, "mag": 1, "reload": 15, "flags": ["explosive"], "blast": 30, "blastDamage": 7000, "kick": 60000, "fx": "cannon", "speed": 700, "soundOf": "Leviathan .50"}, 800000),
    ("Rocket Battery", LARGE, 1, 1, {"damage": 500, "rpm": 60, "spread": 2, "range": 600, "maxRange": 900, "mag": 12, "reload": 20, "flags": ["explosive"], "blast": 10, "blastDamage": 700, "kick": 300, "fx": "rocket", "speed": 150, "soundOf": "Leviathan .50"}, 30000),
]


def gun(name, c, n, barrels, g, mass):
    p = Part(name, c, "Heavy guns", f"A {c.name.lower()} gun for a turret: mount it by its bottom on a motor hinge or turntable. Fed by a magazine on its back or sides, through ammo hoists. It fires from a gunner's seat, a fire director or a wired signal.", role="gun", color="#4a4f45")
    h = c.half
    t = 0.12 * c.cell
    z1 = -h - n * c.cell
    p.add(box((-h, -h, -h), (h, h * 0.7, h)))
    gap = c.cell / (barrels + 1)
    for b in range(barrels):
        x = -h + gap * (b + 1)
        p.add(box((x - t / 2, -t / 2, z1), (x + t / 2, t / 2, -h), "#2a2b2d"))
    p.sock("bottom", (0, -h, 0), "-y")
    p.sock("top", (0, h * 0.7, 0), "+y")
    p.sock("left", (-h, 0, 0), "-x")
    p.sock("right", (h, 0, 0), "+x")
    p.sock("back", (0, 0, h), "+z")
    p.stats(mass, c.cell * c.cell * c.health_m2 * 4, c.armour + 12)
    g = dict(g)
    g["muzzle"] = [0, 0, z1]
    p.set(gun=g, centreSockets=False)
    return p


MAGAZINES = {MEDIUM: (2000, 12, 700), LARGE: (400, 20, 4000), CAPITAL: (200, 20, 20000)}  # rounds, cook-off radius, damage
HOISTS = {MEDIUM: 8, LARGE: 2, CAPITAL: 1}  # cells


def magazine(c):
    rounds, radius, dmg = MAGAZINES[c]
    p = Part(f"Shell Magazine {c.name}", c, "Heavy guns", f"{rounds} rounds for the heavy guns its hoists reach. Holed, it cooks off.", role="ammobox", color="#5a6048")
    h = c.half
    p.add(box((-h, -h, -h), (h, h, h)))
    p.add(box((-h * 0.8, h, -h * 0.8), (h * 0.8, h + 0.01 * c.x, h * 0.8), HAZARD, detail=True))
    for n, at, d in (("+x", (h, 0, 0), "+x"), ("-x", (-h, 0, 0), "-x"), ("+y", (0, h, 0), "+y"), ("-y", (0, -h, 0), "-y"), ("+z", (0, 0, h), "+z"), ("-z", (0, 0, -h), "-z")):
        p.sock(n, at, d)
    p.stats(c.cell * c.cell * c.kg_m2 * 5, c.cell * c.cell * c.health_m2, c.armour)
    p.set(ammoBox={"rounds": rounds, "cookOff": {"radius": radius, "damage": dmg}})
    return p


def hoist(c):
    n = HOISTS[c]
    p = Part(f"Ammo Hoist {c.name} {fmt(n * c.cell)} m", c, "Heavy guns", "A shell hoist: carries rounds from a magazine up to a gun, like a feed chute.", role="feedchute", color="#6a7a4a")
    h = c.half * 0.5
    x0, x1 = c.lo(), c.hi(n)
    p.add(box((x0, -h, -h), (x1, h, h)))
    p.sock("-x", (x0, 0, 0), "-x")
    p.sock("+x", (x1, 0, 0), "+x")
    p.stats(n * c.cell * c.cell * c.kg_m2 * 0.4, n * c.cell * c.cell * c.health_m2, c.armour)
    return p


# ---- Control ------------------------------------------------------------------------------------------------------


def helm():
    p = Part("Bridge Helm", SMALL, "Bridge", "The ship's wheel and throttle: the helm drives its construct, wherever the crew's seats come. Sit with F.", role="seat", color="#3a3f46", group=GROUP_SMALL, tool="wrench")
    p.add(box((-0.5, 0, -0.5), (0.5, 0.25, 0.5)))
    p.add(box((-0.3, 0.25, 0.15), (0.3, 1.0, 0.3), "#2f343a"))
    p.add(box((-0.5, 0.25, -0.5), (0.5, 1.15, -0.25), DARK))
    p.add(box((-0.42, 0.9, -0.26), (0.42, 1.1, -0.24), "#2fae6a", detail=True))
    p.add(box((-0.1, 1.15, -0.4), (0.1, 1.3, -0.3), GOLD, detail=True))
    p.sock("-y", (0, 0, 0), "-y")
    p.sock("+x", (0.5, 0.125, 0), "+x")
    p.sock("-x", (-0.5, 0.125, 0), "-x")
    p.stats(160, 300, 8, 3)
    p.set(seat={"at": [0, 0.25, 0.0], "helm": True})
    return p


def crew_station():
    p = Part("Crew Station", SMALL, "Bridge", "A crew seat at a console: wire it into a fire director, an input reader or anything else that reads a seat.", role="seat", color="#3a3f46", group=GROUP_SMALL, tool="wrench")
    p.add(box((-0.4, 0, -0.4), (0.4, 0.25, 0.4)))
    p.add(box((-0.25, 0.25, 0.2), (0.25, 0.95, 0.32), "#2f343a"))
    p.add(box((-0.4, 0.25, -0.4), (0.4, 1.0, -0.2), DARK))
    p.add(box((-0.32, 0.8, -0.21), (0.32, 0.95, -0.19), "#2f8fae", detail=True))
    p.sock("-y", (0, 0, 0), "-y")
    p.stats(90, 200, 5, 2)
    p.set(seat={"at": [0, 0.25, 0.05]})
    return p


def director():
    p = Part("Fire Director", SMALL, "Bridge", "One gunner fights every turret: wire a seat into it and it into any number of guns. They aim where that gunner looks, hold fire until they bear, and fire on the gunner's fire key.", role="logic", color="#3a4a3f", group=GROUP_SMALL, tool="wrench")
    p.add(box((-0.25, -0.125, -0.25), (0.25, 0.25, 0.25)))
    p.add(box((-0.2, 0.25, -0.05), (0.2, 0.33, 0.05), "#2a2b2d", detail=True))
    p.sock("-y", (0, -0.125, 0), "-y")
    p.sock("+x", (0.25, 0, 0), "+x")
    p.sock("-x", (-0.25, 0, 0), "-x")
    p.stats(35, 150, 4, 2)
    p.set(logic={"kind": "director"}, power=200)
    return p


def dial(name, kind, desc, c, maximum, extra=None):
    p = Part(name, c, "Bridge", desc, role="logic", color="#2f343a")
    h = c.half
    p.add(box((-h, -h, -h * 0.3), (h, h, h * 0.3)))
    p.add(box((-h * 0.8, -h * 0.8, -h * 0.3 - 0.004 * c.x), (h * 0.8, h * 0.8, -h * 0.28), "#d8d4c0", detail=True))
    p.sock("back", (0, 0, h * 0.3), "+z")
    p.sock("-y", (0, -h, 0), "-y")
    p.sock("+y", (0, h, 0), "+y")
    p.stats(c.cell * c.cell * c.kg_m2, c.cell * c.cell * c.health_m2, 4)
    p.set(logic={"kind": kind}, dial={"at": [0, 0, -h * 0.3], "face": "-z", "max": maximum})
    if extra:
        p.set(**extra)
    return p


def sensor():
    p = Part("Long Range Sensor", SMALL, "Bridge", "A mast sensor: on while a player is in range (set it up to 300 m). Wired into a gun with a turntable it is a sentry at that range.", role="logic", color="#4f6a7a", group=GROUP_SMALL, tool="wrench")
    p.add(box((-0.125, -0.125, -0.125), (0.125, 0.5, 0.125)))
    p.add(box((-0.3, 0.5, -0.05), (0.3, 0.6, 0.05), "#d0d0d0", detail=True))
    p.sock("-y", (0, -0.125, 0), "-y")
    p.stats(20, 80, 2, 2)
    p.set(logic={"kind": "proximity", "settings": [60], "labels": ["60 m"]},
          config=[num("radius", "Range", "Sensor", "m", 5, 300, 5, "How far it reaches.", 120)])
    return p


# ---- Lights -------------------------------------------------------------------------------------------------------


def lamp_config(colour):
    return [
        {"key": "colour", "type": "colour", "label": "Colour", "group": "Light", "default": colour, "tooltip": "The colour it glows when lit."},
        num("brightness", "Brightness", "Light", "%", 10, 200, 1, "How bright it glows.", 100),
        num("blink", "Blink", "Light", "Hz", 0, 10, 0.1, "Flashes this many times a second while lit (0: steady).", 0),
    ]


def floodlight(c):
    name = "Floodlight" if c is SMALL else f"Floodlight {c.name}"
    p = Part(name, c, "Lights", "A big lamp for decks and gates: lights while its input is on.", role="logic", color=DARK, group=GROUP_SMALL if c is SMALL else None, tool="wrench" if c is SMALL else "welder")
    n = 2 if c is SMALL else 1
    h = n * c.cell / 2
    y0 = -c.half
    p.add(box((-h, y0, -h), (h, y0 + 2 * h, h)))
    p.add(box((-h * 0.8, y0 + h * 0.2, -h - 0.004 * c.x), (h * 0.8, y0 + h * 1.8, -h), BRIGHT, detail=True, paintable=False))
    p.sock("-y", (0, y0, 0), "-y")
    p.sock("+z", (0, 0, h), "+z")
    p.stats(8 * c.x * c.x / 4, 40 * c.x, 2)
    p.set(logic={"kind": "lamp"}, power=250 * c.x * c.x / 4, config=lamp_config("#fff4d0"), faces="forward")
    return p


def lantern(c):
    name = "Hanging Lantern" if c is SMALL else f"Hanging Lantern {c.name}"
    p = Part(name, c, "Lights", "A great paper lantern on a chain: hang it under a walkway or a gate. Lights while its input is on.", role="logic", color=LANTERN, group=GROUP_SMALL if c is SMALL else None, tool="wrench" if c is SMALL else "welder")
    # Hung from its +y socket: a chain, a cap and the body.
    s = c.cell
    top = c.half
    p.add(box((-s * 0.5, top - s * 2.2, -s * 0.5), (s * 0.5, top - s * 0.7, s * 0.5)))
    p.add(box((-s * 0.3, top - s * 0.7, -s * 0.3), (s * 0.3, top - s * 0.55, s * 0.3), DARK))
    p.add(box((-s * 0.05, top - s * 0.55, -s * 0.05), (s * 0.05, top, s * 0.05), DARK))
    p.add(box((-s * 0.54, top - s * 1.6, -s * 0.54), (s * 0.54, top - s * 1.3, s * 0.54), "#ffb060", detail=True, paintable=False))
    p.sock("+y", (0, top, 0), "+y")
    p.stats(6 * c.x, 30 * c.x, 1)
    p.set(logic={"kind": "lamp"}, power=30 * c.x, config=lamp_config("#ff9040"), centreSockets=False)
    return p


# ---- Walking about ------------------------------------------------------------------------------------------------


def catwalk(n):
    c = MEDIUM
    p = Part(f"Catwalk {n} m", c, "Walkways", "A grated walkway with rails, a metre wide: for gantries, scaffolds and the outside of a hull.", role="panel", color=STEEL)
    h = c.half
    y = c.cell * 0.05
    z0, z1 = -h, c.hi(n)
    p.add(box((-h, -y, z0), (h, y, z1)))
    for s in (-1, 1):
        p.add(box((s * h - 0.02, y, z0 + 0.02), (s * h + 0.02, 1.0, z0 + 0.06), DARK, detail=True))
        p.add(box((s * h - 0.02, y, z1 - 0.06), (s * h + 0.02, 1.0, z1 - 0.02), DARK, detail=True))
        p.add(box((s * h - 0.02, 0.96, z0 + 0.02), (s * h + 0.02, 1.0, z1 - 0.02), DARK, detail=True))
    p.sock("-z", (0, 0, z0), "-z")
    p.sock("+z", (0, 0, z1), "+z")
    p.sock("+x", (h, 0, 0), "+x")
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("far+x", (h, 0, (n - 1) * c.cell), "+x")
    p.sock("far-x", (-h, 0, (n - 1) * c.cell), "-x")
    area = n * c.cell
    p.stats(area * 40, area * 60, 6, 3)
    p.set(walk="floor", centreSockets=False)
    return p


def ladder(n):
    c = MEDIUM
    rises = int(n / 0.375)
    p = Part(f"Ladder {n} m", c, "Walkways", f"A steep ship ladder {n} m tall, a metre wide: walk up it (0.375 m steps). It goes up the outside of a hull, a gantry or a scaffold.", role="frame", color=DARK)
    h = c.half
    y0 = c.lo()
    for k in range(min(rises, 28)):
        y = y0 + (k + 1) * n / min(rises, 28)
        z = h - (k + 1) * 0.25
        p.add(box((-h, y - 0.05, z - 0.13), (h, y, z + 0.13)))
    z_end = h - min(rises, 28) * 0.25 - 0.13
    p.add(box((-h - 0.02, y0, z_end), (-h + 0.04, y0 + n, h), DARK, detail=True))
    p.add(box((h - 0.04, y0, z_end), (h + 0.02, y0 + n, h), DARK, detail=True))
    p.sock("-y", (0, y0 + n / min(rises, 28) - 0.05, h - 0.25), "-y")
    p.sock("+y", (0, y0 + n, h - min(rises, 28) * 0.25), "+y")
    p.stats(n * 30, n * 70, 6, 3)
    p.set(walk="ladder", centreSockets=False)
    return p


def scaffold():
    c = MEDIUM
    n = 4
    p = Part("Scaffold Tower 4 m", c, "Walkways", "A 4 by 4 by 4 m scaffold section with a grated floor on top: stack them up the outside of a town, and add ladders and catwalks.", role="frame", color=WOOD)
    h = c.half
    x1, y1 = c.hi(n), c.hi(n)
    t = 0.1
    for x in (-h, x1 - 2 * t):
        for z in (-h, x1 - 2 * t):
            p.add(box((x, -h, z), (x + 2 * t, y1 - 0.1, z + 2 * t)))
    p.add(box((-h, y1 - 0.1, -h), (x1, y1, x1)))
    for z in (-h + 0.04, x1 - 2 * t + 0.04):
        p.add(box((-h + 0.2, h + 1.0, z), (x1 - 0.2, h + 1.12, z + 0.12), "#8a7a5a", detail=True))
    for x in (-h + 0.04, x1 - 2 * t + 0.04):
        p.add(box((x, h + 1.0, -h + 0.2), (x + 0.12, h + 1.12, x1 - 0.2), "#8a7a5a", detail=True))
    mid = c.cell
    p.sock("-y", (0, -h, 0), "-y")
    p.sock("-y+", ((n - 1) * c.cell, -h, (n - 1) * c.cell), "-y")
    p.sock("+y", (0, y1, 0), "+y")
    p.sock("+y+", ((n - 1) * c.cell, y1, (n - 1) * c.cell), "+y")
    p.sock("+x", (x1, y1 - 0.05, mid), "+x")
    p.sock("-x", (-h, y1 - 0.05, mid), "-x")
    p.sock("+z", (mid, y1 - 0.05, x1), "+z")
    p.sock("-z", (mid, y1 - 0.05, -h), "-z")
    p.stats(1500, 1200, 10, 5)
    p.set(walk="floor", centreSockets=False)
    return p


def bulkhead_frame():
    c = MEDIUM
    p = Part("Bulkhead Door Frame 4x3", c, "Walkways", "A bulkhead 4 m wide and 3 m tall with a 2 m doorway: a sliding door fills it. Two make an airlock.", role="frame", color=STEEL)
    h = c.half
    t = 0.12
    x1, y1 = c.hi(4), c.hi(3)
    p.add(box((-h, -h, -t), (0.5, y1, t)))
    p.add(box((2.5 - 0.0, -h, -t), (x1, y1, t)))
    p.add(box((0.5, 1.5, -t), (2.5, y1, t)))
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("+x", (x1, 0, 0), "+x")
    p.sock("jamb", (0.5, 0, 0), "+x")
    p.sock("-y", (0, -h, 0), "-y")
    p.sock("-y+", (3, -h, 0), "-y")
    p.sock("+y", (1, y1, 0), "+y")
    p.stats(1800, 2400, 28, 5)
    p.set(centreSockets=False)
    return p


def sliding_door():
    c = MEDIUM
    p = Part("Sliding Door 2x2", c, "Walkways", "A sliding door leaf 2 m wide and 2 m tall: put a piston's top on its edge and wire the piston.", role="panel", color=RUST)
    h = c.half
    t = 0.08
    p.add(box((-h, -h, -t), (c.hi(2), c.hi(2), t)))
    p.add(box((0.4, 0.8, -t - 0.01), (0.6, 1.0, t + 0.01), GOLD, detail=True))
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("+x", (c.hi(2), 0, 0), "+x")
    p.stats(320, 700, 24, 3)
    p.set(centreSockets=False)
    return p


def everything():
    out = []
    for c in BIG:
        out.append(("Launch", ramp(c)))
        out.append(("Launch", lift(c)))
        out.append(("Launch", door_leaf(c)))
        out.append(("Launch", gate(c)))
        out.append(("Launch", gate_hinge(c)))
        out.append(("Launch", gate_leaf(c)))
        out.append(("Launch", catapult(c)))
        out.append(("Launch", arrestor(c)))
        out.append(("Launch", dock(c)))
        for stroke in PISTONS[c][0]:
            out.append(("Actuators", piston(c, stroke)))
        out.append(("Actuators", motor_hinge(c)))
        out.append(("Actuators", turntable(c)))
        out.append(("Moving", winch(c)))
        out.append(("Moving", lifting_eye(c)))
        out.append(("Moving", boom(c)))
        out.append(("Moving", conveyor(c)))
        out.append(("Weapons", magazine(c)))
        out.append(("Weapons", hoist(c)))
    for c in (SMALL, MEDIUM):
        out.append(("Launch", mag_lock(c)))
        out.append(("Lights", lantern(c)))
    for c in (SMALL, MEDIUM, LARGE):
        out.append(("Lights", floodlight(c)))
    for g in GUNS:
        out.append(("Weapons", gun(*g)))
    out += [("Control", x) for x in (helm(), crew_station(), director(), sensor(),
                                     dial("Deck Speed Dial", "speed", "A big dial: its construct's speed, for the bridge.", MEDIUM, 40),
                                     dial("Deck Fuel Dial", "fuelgauge", "A big dial: what the tanks hold.", MEDIUM, 1),
                                     dial("Deck Charge Dial", "chargegauge", "A big dial: what the batteries hold.", MEDIUM, 1))]
    out += [("Interior", x) for x in (catwalk(4), catwalk(8), ladder(4), ladder(8), scaffold(), bulkhead_frame(), sliding_door())]
    return out


# ---- Blueprints: what the parts add up to -------------------------------------------------------------------------


def step(part, on=None, socket=None, by=None, spin=None):
    d = {"part": part}
    if on is not None:
        d.update({"on": on, "socket": socket, "by": by})
    if spin is not None:
        d["spin"] = spin
    return d


BLUEPRINTS = {
    "Deck Lift": [
        step("Piston Medium 4 m"),
        step("Lift Platform Medium 5x5", 0, "top", "-y"),
    ],
    "Naval Turret": [
        step("Turntable Large"),
        step("Naval Gun 20 cm", 0, "top", "bottom"),
        step("Shell Magazine Large", 1, "back", "-z"),
    ],
    "Harbour Crane": [
        step("Turntable Large"),
        step("Crane Boom Large 32 m", 0, "top", "root-y"),
        step("Winch Large 32 m", 1, "tip-y", "+y"),
    ],
    "Gate Ramp": [
        step("Lift Platform Medium 5x5"),
        step("Motor Hinge Medium", 0, "+x", "base", 1),
        step("Ramp Deck Medium 8x5", 1, "leaf", "-x", 0),
        step("Arched Gate Medium 5x5", 0, "+y.1-", "-y-", 2),
    ],
}


def blueprints():
    for name, steps in BLUEPRINTS.items():
        path = os.path.join("Constructs", "Blueprints", file_name(name))
        want = json.dumps({"format": "call-of-slop-construct-blueprint/1", "name": name, "steps": steps}, indent=2) + "\n"
        have = open(path, encoding="utf-8").read() if os.path.exists(path) else ""
        if want == have:
            continue
        if "--check" in sys.argv:
            print(f"out of date: {path}")
            return 1
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(want)
    return 0


if __name__ == "__main__":
    sys.exit(write_parts(OUT, everything()) or blueprints())
