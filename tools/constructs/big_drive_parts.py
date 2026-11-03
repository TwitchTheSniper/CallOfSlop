#!/usr/bin/env python3
"""Writes what moves and powers big constructs (Constructs/Parts/BigDrive): docs/sandbox/CONSTRUCTS.md, "Big drive".

The size classes come from scale.py (Medium x4, Large x16, Capital x64 by linear size). The drive's numbers don't scale by
one rule: mass, power and force grow differently, so each family has its table here, by class, and THE tables at the top
are where the balance lives. A traction city (100,000 t, 300 m, g 18 m/s2: 1.8 GN of weight) is what Capital is sized
for: four Capital Bogies on hub motors, a Boiler or two, smoke out of Chimneys. Every block has odd cell counts, so its
origin is the centre of its middle cell and every socket stays on the class's lattice.

Run it from the repo root: python3 -I tools/constructs/big_drive_parts.py (--check only compares). Data first: the JSON
files are what the game loads; fix a family here, never in its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "BigDrive")
BIG = (MEDIUM, LARGE, CAPITAL)

# --- The tables (class -> numbers) -------------------------------------------------------------------------------------

# name: cells (x, y, z) per class, mass t, health, armour, torque N m, idle, peak, max rpm, curve, burn l/s, starter, spool s, noise
ENGINES = {
    "Diesel": ("Diesel Engine", "A heavy marine-style diesel: huge torque at low revs. Needs a battery for its starter.", "#3d4a5c", "diesel", True, 0.9, {
        MEDIUM: ((3, 3, 5), 9, 9000, 30, 9000, 500, 1200, 2000, 0.6, 0, 50),
        LARGE: ((3, 3, 5), 400, 60000, 60, 300000, 200, 500, 800, 8, 0, 2000),
        CAPITAL: ((1, 1, 3), 2400, 250000, 120, 8000000, 30, 102, 120, 45, 0, 60000),
    }),
    "Turbine": ("Gas Turbine", "A light gas turbine: thirsty, slow to spool, all its power high up. Needs a battery for its starter.", "#6e6a5e", "turbine", True, 0.7, {
        MEDIUM: ((3, 3, 7), 3, 5000, 20, 1800, 3000, 9000, 12000, 1.7, 4, 50),
        LARGE: ((3, 3, 7), 120, 30000, 40, 30000, 1500, 5000, 6500, 16, 10, 2000),
        CAPITAL: ((1, 1, 3), 600, 120000, 80, 400000, 900, 3600, 4500, 150, 20, 60000),
    }),
    "Steam": ("Steam Engine", "Full torque from a standstill, and slow to raise steam. It burns fuel and smokes like a town.", "#5a4436", "steam", False, 0.8, {
        MEDIUM: ((3, 3, 5), 14, 12000, 30, 20000, 20, 240, 500, 2.4, 15, 0),
        LARGE: ((3, 3, 5), 800, 90000, 60, 1200000, 10, 90, 200, 16, 30, 0),
        CAPITAL: ((1, 1, 3), 5000, 400000, 120, 30000000, 5, 40, 90, 150, 45, 0),
    }),
    "Electric": ("Electric Motor", "A shaft motor: full torque from standstill, on the circuit's power. Needs a working battery or plant.", "#2f4a58", "electric", False, 0.2, {
        MEDIUM: ((3, 3, 3), 6, 7000, 25, 12000, 0, 1000, 2400, 0, 0, 0),
        LARGE: ((3, 3, 3), 200, 40000, 50, 400000, 0, 300, 900, 0, 0, 0),
        CAPITAL: ((1, 1, 1), 1500, 200000, 100, 12000000, 0, 120, 240, 0, 0, 0),
    }),
}
# Gearboxes: size cells, mass t, gears (reductions), most torque out (N m)
GEARBOXES = {MEDIUM: (3, 8, [6.0, 4.0, 2.8, 1.9, 1.3], 2e5), LARGE: (3, 300, [8.0, 5.0, 3.0, 2.0], 2e7), CAPITAL: (1, 1500, [12.0, 8.0, 5.0], 4e8)}
RATED = {MEDIUM: 2e5, LARGE: 2e7, CAPITAL: 2e9}  # the torque a drive part of the class carries (N m): past it, it slips
FINAL = {MEDIUM: 4.0, LARGE: 5.0, CAPITAL: 8.0}  # a differential's final drive
# Wheels: radius in half cells via its cells (it is cells tall), width cells, mass t, health, armour
WHEELS = {MEDIUM: (3, 1, 1.2, 4000), LARGE: (3, 1, 60, 30000), CAPITAL: (3, 1, 1500, 200000)}
# Tracks: (wide, tall, long) cells for the unit and the bogie, mass t each, contact rays each
TRACKS = {
    MEDIUM: (((3, 3, 15), 14, 4), ((3, 3, 7), 6, 2)),
    LARGE: (((3, 3, 15), 700, 4), ((3, 3, 7), 280, 2)),
    CAPITAL: (((3, 3, 9), 4800, 6), ((1, 3, 5), 2000, 3)),
}
# Suspension: travel m, Hz, most load a contact (kg), how fast the contact follows the ground (m/s)
SPRINGS = {SMALL: (0.3, 2.2, 1000, 0), MEDIUM: (1.0, 1.4, 6e4, 2.0), LARGE: (3.0, 1.1, 3e6, 3.0), CAPITAL: (8.0, 0.9, 6e7, 4.0)}
RIDE = 0.33  # big struts are gas struts: they ride half way down their travel whatever they carry (up to its most)
# Hub motors: N m at the wheel, kW, m/s it fades out at
HUBS = {SMALL: (1600, 60, 25), MEDIUM: (1.2e5, 1500, 18), LARGE: (6e6, 15000, 12), CAPITAL: (3e8, 60000, 7)}
# Actuators: piston N, stroke m, knee N m, steering N m, jack stroke m
LEGS = {MEDIUM: (4e5, 4, 2e6, 2e5, 2), LARGE: (4e7, 16, 2e8, 2e7, 6), CAPITAL: (6e8, 48, 1.9e9, 1.5e9, 16)}
# Plants: MW by class, burn l/s a MW, spool s by class
PLANTS = {
    "Boiler": ("Boiler", "A coal-black boiler: slow to raise steam, steady, and it feeds the circuit it is on. Smokes through its stack or a chimney on it.", "#4a3a30", 0.6,
               {MEDIUM: (3, 25), LARGE: (40, 60), CAPITAL: (300, 90)}, True),
    "Furnace": ("Furnace", "A roaring furnace generator: up to power fast, burns two and a half boilers' fuel for it. Smokes hard.", "#6a3d24", 1.5,
                {MEDIUM: (2, 5), LARGE: (30, 8), CAPITAL: (250, 12)}, True),
    "Genset": ("Generator Set", "A diesel generator: frugal, ready in seconds, for what a boiler is too slow for.", "#3f5560", 0.35,
               {MEDIUM: (1, 3), LARGE: (20, 5), CAPITAL: (120, 8)}, True),
    "Reactor": ("Reactor", "A sealed reactor: no fuel, no smoke, follows what the circuit draws. Very heavy.", "#3b6a5e", 0,
                {MEDIUM: (10, 8), LARGE: (200, 15), CAPITAL: (2000, 30)}, False),
}
PLANT_MASS = {MEDIUM: 24, LARGE: 900, CAPITAL: 4800}  # t, a Boiler; a Reactor weighs half as much again, a Genset half
TANKS = {MEDIUM: (3, 2.5, 20000), LARGE: (3, 90, 1000000), CAPITAL: (1, 280, 4000000)}  # cells, mass t, litres
BATTERIES = {MEDIUM: (3, 8, 1e6), LARGE: (3, 300, 2e8), CAPITAL: (1, 1800, 1e10)}  # cells, mass t, kJ
ALTERNATORS = {MEDIUM: (0.6, 100), LARGE: (30, 5000), CAPITAL: (300, 50000)}  # mass t, kW
BALLAST = {MEDIUM: 8, LARGE: 400, CAPITAL: 5000}  # t a block
# Thrusters: (name, cls, cells long, force N, seconds of spool)
THRUSTERS = [("Light", SMALL, 3, 8e3, 0.3), ("Small", SMALL, 7, 5e4, 0.5), ("Medium", MEDIUM, 3, 8e5, 1.0), ("Large", LARGE, 3, 12.5e6, 1.6), ("Capital", CAPITAL, 1, 2e8, 2.4)]
JET_BURN = 3.0  # l/s a MN at full thrust
FAN_KW = 5.0  # kW a kN at full thrust


def health(mass_t):
    """A drive part's health: tough for its size."""
    return min(10_000_000, int(600 * mass_t ** 0.85 + 200))


def armour(c):
    return c.armour


def half(n, c):
    return n * c.cell / 2


def block(p, nx, ny, nz, color=None, **kw):
    """The solid block, its origin in the middle cell."""
    c = p.cls
    return p.add(box((-half(nx, c), -half(ny, c), -half(nz, c)), (half(nx, c), half(ny, c), half(nz, c)), color, **kw))


def faces(p, nx, ny, nz, skip=()):
    """A socket on each face's middle cell, the socket named after its face, and on the far cells of a long face."""
    c = p.cls
    h = (half(nx, c), half(ny, c), half(nz, c))
    for axis, a in enumerate("xyz"):
        for sign, s in ((1, "+"), (-1, "-")):
            d = s + a
            if d in skip:
                continue
            at = [0.0, 0.0, 0.0]
            at[axis] = sign * h[axis]
            p.sock(d, at, d)
    # The long sides' end cells, for bolting it down.
    if nz > 1 and nz >= nx:
        for sign, s in ((1, "+"), (-1, "-")):
            z = (nz - 1) / 2 * c.cell * sign
            if "-y" not in skip:
                p.sock(f"-y{s}", (0, -h[1], z), "-y")
            if "+y" not in skip:
                p.sock(f"+y{s}", (0, h[1], z), "+y")


def bands(p, nx, ny, nz, color, n=2):
    """A couple of bands round a block, proud of it: what stops a long box looking like a brick."""
    c = p.cls
    x, y, z = half(nx, c), half(ny, c), half(nz, c)
    e = 0.04 * c.x
    for k in range(n):
        at = z * (2 * (k + 1) / (n + 1) - 1)
        w = 0.05 * z + 0.02 * c.x
        p.add(box((-x - e, -y - e, at - w), (x + e, y + e, at + w), color, detail=True))


def stack(p, nx, ny, c, tall=4.0, color="#2a2523"):
    """A smoke stack out of a block's top: boxes, and where the smoke leaves."""
    y = half(ny, c)
    w = 0.3 * c.cell
    x = half(nx, c) * 0.45
    h = tall * c.cell * (0.25 if c is CAPITAL else 0.5)
    p.add(box((x - w, y - 0.02, -w), (x + w, y + h, w), color, detail=True))
    p.add(box((x - w * 1.3, y + h * 0.82, -w * 1.3), (x + w * 1.3, y + h + 0.02 * c.x, w * 1.3), "#181513", detail=True))
    p.set(smoke={"at": [round(x, 3), round(y + h + 0.02 * c.x, 3), 0], "size": round(0.7 * c.cell, 3)})


def pipe_port(name, d, at, face, fluid):
    return {"name": name, "type": "pipe", "dir": d, "fluid": fluid, "at": [round(v, 4) for v in at], "face": face}


def power_port(name, d, at, face):
    return {"name": name, "type": "power", "dir": d, "at": [round(v, 4) for v in at], "face": face}


def shaft_port(name, d, socket):
    return {"name": name, "type": "shaft", "dir": d, "socket": socket}


# --- Engines ----------------------------------------------------------------------------------------------------------


def engines():
    out = []
    for key, (word, desc, color, curve, starter, noise, by) in ENGINES.items():
        for c, (cells, t, hp, arm, torque, idle, peak, top, burn, spool, skj) in by.items():
            nx, ny, nz = cells
            name = f"{'Capital' if c is CAPITAL else c.name} {word}"
            mw = torque * peak * 2 * 3.14159 / 60 / 1e6
            p = Part(name, c, "Engines", f"{desc} {torque / 1000:g} kN m at {peak} rpm, about {mw:.0f} MW.", role="engine", tool="wrench", color=color)
            block(p, nx, ny, nz)
            bands(p, nx, ny, nz, "#2b3442")
            faces(p, nx, ny, nz)
            p.stats(t * 1000, hp, arm)
            e = {"torque": torque, "idleRpm": idle, "peakRpm": peak, "maxRpm": top, "curve": curve, "noise": noise}
            ports = [shaft_port("Drive out", "out", "+z")]
            if curve == "electric":
                e["fuel"] = "electric"
                ports.append(power_port("Power in", "in", (-half(nx, c), 0, 0), "-x"))
            else:
                e["burnRate"] = burn
                ports.append(pipe_port("Fuel in", "in", (-half(nx, c), 0, 0), "-x", "fuel"))
                ports.append(pipe_port("Exhaust out", "out", (half(nx, c), 0, 0), "+x", "exhaust"))
                stack(p, nx, ny, c)
            if starter:
                e["starter"] = True
                e["starterKJ"] = skj
            if spool:
                e["spool"] = spool
            p.set(engine=e, ports=ports)
            out.append(("Engines", p))
    return out


# --- The drive line ----------------------------------------------------------------------------------------------------


def drive_block(name, c, section, desc, role, mass, n, color, ports, block_key, value, skip=()):
    p = Part(name, c, section, desc, role=role, tool="wrench", color=color)
    block(p, n, n, n)
    bands(p, n, n, n, "#2c2f33", 1)
    faces(p, n, n, n, skip)
    p.stats(mass * 1000, health(mass), c.armour // 2)
    p.set(ports=ports, **{block_key: value})
    return p


def drive_line():
    out = []
    for c in BIG:
        n, t, gears, most = GEARBOXES[c]
        cn = c.name
        out.append(("Drive", drive_block(f"{cn} Gearbox", c, "Gearboxes", f"A {len(gears)}-speed box for a {cn.lower()} engine: bolt it to the engine's output. Carries {most / 1000:g} kN m out.",
                                         "transmission", t, n, "#556b4a", [shaft_port("Input shaft", "in", "-z"), shaft_port("Output shaft", "out", "+z")],
                                         "transmission", {"kind": "automatic", "gears": gears, "shift": 1.0, "maxTorque": most, "efficiency": 0.93})))
        rated = RATED[c]
        k = 1 if c is CAPITAL else 3
        m = {MEDIUM: 3, LARGE: 120, CAPITAL: 900}[c]
        kinds = [
            ("Reduction Gearbox", "A 4:1 reduction in line: a quarter of the speed, four times the pull.", {"kind": "gearbox", "ratio": 4.0, "ports": ["-z", "+z"], "efficiency": 0.95}, [shaft_port("Input shaft", "in", "-z"), shaft_port("Output shaft", "out", "+z")], "#5a4f6b"),
            ("Right-Angle Gearbox", "Turns the drive through a right angle.", {"kind": "gearbox", "ports": ["-z", "-y"], "efficiency": 0.96}, [shaft_port("Input shaft", "in", "-z"), shaft_port("Output shaft", "out", "-y")], "#6b5f45"),
            ("T Gearbox", "Splits the drive: straight on and a branch.", {"kind": "gearbox", "ports": ["-z", "+z", "-y"], "efficiency": 0.95}, [shaft_port("Input shaft", "in", "-z"), shaft_port("Output shaft", "out", "+z"), shaft_port("Branch shaft", "out", "-y")], "#5f6b45"),
            ("Clutch", "Passes the drive while engaged: wire a signal to let it out.", {"kind": "clutch", "ports": ["-z", "+z"], "efficiency": 0.99}, [shaft_port("Input shaft", "in", "-z"), shaft_port("Output shaft", "out", "+z")], "#6a5a4a"),
            ("CV Joint", "Carries the drive through a steering pivot, up to 45 degrees.", {"kind": "cvjoint", "maxAngle": 45, "ports": ["+x", "-x"], "efficiency": 0.98}, [shaft_port("Input shaft", "in", "-x"), shaft_port("Output shaft", "out", "+x")], "#6a6f78"),
            ("Wheel Hub", "An independent hub: drives the suspension bolted to it, 6:1.", {"kind": "hub", "ratio": 6.0, "efficiency": 0.97, "ports": ["-z", "+x", "-x"]}, [shaft_port("Drive in", "in", "-z")], "#4d5a3f"),
        ]
        for word, desc, drive, ports, color in kinds:
            drive = dict(drive, maxTorque=rated)
            out.append(("Drive", drive_block(f"{cn} {word}", c, "Drive line", f"{desc} Carries {rated / 1000:g} kN m.", "transfer", m, k, color, ports, "drive", drive)))
        for cells in (3, 7):
            p = Part(f"{cn} Drive Shaft {cells * c.cell:g} m", c, "Drive line", f"A {cells * c.cell:g} m shaft: the drive goes in one end and out of the other. Carries {rated / 1000:g} kN m.", role="transfer", tool="wrench", color="#7a7f86")
            z = half(cells, c)
            w = c.cell * 0.3
            p.add(box((-w, -w, -z), (w, w, z)))
            p.add(box((-w * 1.3, -w * 1.3, -z + 0.02), (w * 1.3, w * 1.3, -z + 0.1 * c.cell), "#4a4f55", detail=True))
            p.add(box((-w * 1.3, -w * 1.3, z - 0.1 * c.cell), (w * 1.3, w * 1.3, z - 0.02), "#4a4f55", detail=True))
            p.sock("end-", (0, 0, -z), "-z")
            p.sock("end+", (0, 0, z), "+z")
            p.sock("bottom", (0, -w, 0), "-y")
            p.stats(cells * m * 200, health(cells * m / 5), c.armour // 3)
            p.set(drive={"kind": "shaft", "ports": ["end-", "end+"], "efficiency": 0.99, "maxTorque": rated},
                  ports=[shaft_port("Input shaft", "both", "end-"), shaft_port("Output shaft", "both", "end+")])
            out.append(("Drive", p))
        # Axles: a wide box, a suspension on each end.
        for word, kind, color, eff in (("Axle", "open", "#3e4636", 0.96), ("Locked Axle", "locked", "#46402f", 0.96)):
            wide = 3 if c is CAPITAL else 7
            p = Part(f"{cn} {word}", c, "Drive line", f"A {wide * c.cell:g} m {kind} axle, {FINAL[c]:g}:1: a suspension on each end, the drive in at the middle. Carries {rated / 1000:g} kN m.", role="differential", tool="wrench", color=color)
            block(p, wide, 1, 1)
            faces(p, wide, 1, 1)
            for sx, s in ((1, "+"), (-1, "-")):
                p.sock(f"top{s}", (sx * (wide - 1) / 2 * c.cell, c.half, 0), "+y")
            p.stats(m * 2500, health(m * 2.5), c.armour // 2)
            p.set(differential={"kind": kind, "ratio": FINAL[c], "efficiency": eff, "maxTorque": rated},
                  ports=[shaft_port("Input shaft", "in", "-z"), shaft_port("Output left", "out", "-x"), shaft_port("Output right", "out", "+x")])
            out.append(("Drive", p))
    return out


# --- Wheels, tracks, suspensions ---------------------------------------------------------------------------------------


def wheel_shape(p, c, r, w, tyre, rim):
    p.add(box((-w, -r, -r * 0.42), (w, r, r * 0.42), tyre))
    p.add(box((-w * 0.98, -r * 0.42, -r), (w * 0.98, r * 0.42, r), tyre))
    p.add(box((-w * 1.0, -r * 0.85, -r * 0.85), (w * 1.0, r * 0.85, r * 0.85), tyre))
    p.add(box((-w * 1.05, -r * 0.3, -r * 0.3), (w * 1.05, r * 0.3, r * 0.3), rim, detail=True))
    p.add(box((-w * 1.03, -r * 0.7, -r * 0.08), (w * 1.03, r * 0.7, r * 0.08), rim, detail=True))
    p.add(box((-w * 1.03, -r * 0.08, -r * 0.7), (w * 1.03, r * 0.08, r * 0.7), rim, detail=True))


def wheels():
    out = []
    for c in BIG:
        tall, wide, t, hp = WHEELS[c]
        r = half(tall, c)
        w = half(wide, c)
        for word, tyre, rim, grip, off, roll, mat, m, desc in (
            ("Tyre", "#1f2124", "#9aa0a6", 1.0, 1.25, 0.004, "rubber", 1.0, "A tyre as tall as a house."),
            ("Drive Wheel", "#3a3633", "#a8763e", 0.85, 1.5, 0.008, "steel", 1.6, "A great spiked iron wheel: bites dirt, can't go flat."),
        ):
            p = Part(f"{c.name} {word}", c, "Wheels", f"{desc} {2 * r:g} m across. Bolt its axle onto a suspension.", role="wheel", tool="wrench", material=mat, color=tyre)
            wheel_shape(p, c, r, w, tyre, rim)
            p.sock("axle", (-w, 0, 0), "-x", kind="axle")
            p.stats(t * m * 1000, hp * m, c.armour, c.seconds)
            block_ = {"radius": r, "grip": grip, "offroad": off, "roll": roll}
            if word == "Tyre":
                block_["flat"] = True
                p.d["onBreak"] = "disable"
            p.set(wheel=block_)
            out.append(("Wheels", p))
    return out


def track(c, word, cells, t, rays, desc):
    nx, ny, nz = cells
    x, r, z = half(nx, c), half(ny, c), half(nz, c)
    p = Part(f"{'Capital' if c is CAPITAL else c.name} {word}", c, "Tracks", desc, role="wheel", tool="wrench", color="#2a2c2f")
    # Solid: a stepped hull, rounded at its ends by two shorter boxes.
    p.add(box((-x, -r, -z + r * 0.9), (x, r, z - r * 0.9)))
    p.add(box((-x * 0.96, -r * 0.6, -z), (x * 0.96, r * 0.6, z)))
    # The tread: pads along the top and the bottom.
    pads = 7
    pitch = (2 * z - 2 * r) / pads
    for k in range(pads):
        at = -z + r + (k + 0.5) * pitch
        for y, flip in ((r, 1), (-r, -1)):
            p.add(box((-x * 1.02, min(y, y + flip * 0.05 * r), at - pitch * 0.38), (x * 1.02, max(y, y + flip * 0.05 * r), at + pitch * 0.38), "#4c4f53", detail=True))
    # The exposed drive wheels at the ends and the road rollers under it: "roll" boxes turn as the track runs.
    side = x * 1.1
    for sz in (1, -1):
        at = sz * (z - r * 0.95)
        s = r * 0.8
        p.add(box((-side, -s, at - s * 0.36), (side, s, at + s * 0.36), "#8a7d4a", detail=True, roll=True))
        p.add(box((-side, -s * 0.36, at - s), (side, s * 0.36, at + s), "#8a7d4a", detail=True, roll=True))
    rollers = 4 if nz > 5 else 3
    gap = (2 * z - 3.8 * r) / max(1, rollers - 1)
    for k in range(rollers):
        at = -z + 1.9 * r + k * gap
        s = r * 0.34
        p.add(box((-side * 1.02, -r * 0.5 - s, at - s), (side * 1.02, -r * 0.5 + s, at + s), "#6b6f64", detail=True, roll=True))
    p.sock("axle", (-x, 0, 0), "-x", kind="axle")
    p.stats(t * 1000, health(t), c.armour + 10, c.seconds)
    p.set(wheel={"radius": r, "grip": 1.05, "offroad": 1.4, "side": 0.5 if nz > 7 else 0.9, "roll": 0.006, "track": {"length": 2 * z, "rays": rays}})
    p.d["friction"] = 1.0
    return p


def tracks():
    out = []
    for c in BIG:
        (unit, ut, ur), (bogie, bt, br) = TRACKS[c]
        ln = lambda cells: cells[2] * c.cell
        out.append(("Tracks", track(c, "Track", unit, ut, ur, f"A {ln(unit):g} m track with its drive wheels and road rollers showing: bolt its middle onto a suspension or a hub motor.")))
        out.append(("Tracks", track(c, "Bogie", bogie, bt, br, f"A short {ln(bogie):g} m track bogie: put several under one body, each on a hub motor, on a steering pivot if it is to turn.")))
    return out


def mount(c, name, section, desc, color, mass, block_):
    p = Part(name, c, section, desc, role="suspension", tool="wrench", color=color)
    block(p, 1, 1, 1)
    h = c.half
    p.add(box((h * 0.3, -h * 1.15, -h * 0.25), (h * 0.7, h * 1.15, h * 0.25), "#d0b050", detail=True))
    p.sock("mount", (-h, 0, 0), "-x")
    p.sock("axle", (h, 0, 0), "+x", kind="axle")
    p.stats(mass * 1000, health(mass), c.armour)
    p.set(suspension=block_)
    return p


def suspensions():
    out = []
    for c in (SMALL,) + BIG:
        travel, hz, load, slew = SPRINGS[c]
        base = {"travel": travel, "frequency": hz, "damping": 0.8, "maxLoad": load}
        if c is not SMALL:
            base["ride"] = RIDE
        if slew:
            base["slew"] = slew
        m = {SMALL: 0.02, MEDIUM: 1.5, LARGE: 90, CAPITAL: 900}[c]
        if c is not SMALL:
            out.append(("Suspension", mount(c, f"{c.name} Suspension", "Suspension", f"A sprung mount: {travel:g} m of travel, carries {load / 1000:g} t a contact. Bolt it to a body, an axle or a steering pivot, a wheel or track on it.", "#b08a2e", m, base)))
        torque, kw, top = HUBS[c]
        hub = dict(base, motor={"torque": torque, "power": kw, "top": top})
        p = mount(c, f"{c.name or 'Small'} Hub Motor", "Suspension", f"A sprung mount with a traction motor in it: {torque / 1000:g} kN m, {kw / 1000:g} MW, on the circuit's power. No shafts: each wheel or track is powered where it stands, on a steering pivot too. Its throttle wire runs it alone.", "#a0602e", m * 1.6, hub)
        p.set(ports=[power_port("Power in", "in", (0, c.half, 0), "+y")])
        if c is SMALL:
            p.d["group"] = "Drivetrain"
        out.append(("Suspension", p))
        if c is SMALL:
            continue
        # A landing strut: a spring with a foot, nothing on its axle: it stands on the ground under the body.
        p = Part(f"{c.name} Landing Strut", c, "Legs", f"A sprung leg with {travel * 2:g} m of travel: bolt it under a body, nothing on it: the body stands on it.", role="suspension", tool="wrench", color="#8f8456")
        h = c.half
        p.add(box((-h, -h, -h), (h, h, h)))
        p.add(box((-h * 1.4, -h * 1.2, -h * 1.4), (h * 1.4, -h * 0.7, h * 1.4), "#4b463a", detail=True))
        p.sock("mount", (0, h, 0), "+y")
        p.sock("axle", (0, -h, 0), "-y", kind="axle")
        p.stats(m * 800, health(m), c.armour)
        p.set(suspension={"travel": travel * 2, "frequency": hz, "damping": 0.9, "maxLoad": load * 2})
        out.append(("Legs", p))
    return out


# --- Actuators ---------------------------------------------------------------------------------------------------------


def num(key, label, group, unit, lo, hi, step, tip):
    return {"key": key, "label": label, "group": group, "unit": unit, "min": lo, "max": hi, "step": step, "tooltip": tip}


def piston(c, word, force, stroke, mass, desc):
    h = c.half
    y = h * 3
    p = Part(f"{c.name} {word}", c, "Legs", desc, role="joint", tool="wrench", color="#6d747c")
    p.add(box((-h, -y, -h), (h, y, h), "#3b4046", side="base"))
    p.add(box((-h * 1.06, y * 0.2, -h * 1.06), (h * 1.06, y * 1.01, h * 1.06), "#f08a12", detail=True, side="leaf", paintable=False))
    p.sock("base", (0, -y, 0), "-y")
    p.sock("top", (0, y, 0), "+y")
    p.sock("+x", (h, 0, 0), "+x")
    p.sock("-x", (-h, 0, 0), "-x")
    p.stats(mass * 1000, health(mass), c.armour)
    speed = max(0.3, min(2.0, stroke / 16))
    p.set(joint={"type": "piston", "axis": "+y", "min": 0, "max": stroke, "leaf": "top", "speed": round(speed, 3), "strength": force, "give": round(stroke / 4, 3), "sideLoad": round(force / 3), "overloadDamage": 0},
          config=[num("speed", "Speed", "Motor", "m/s", round(speed / 10, 3), round(speed, 3), 0.01, "How fast it moves."),
                  num("strength", "Force", "Motor", "N", round(force / 10), force, round(force / 100), "The most it pushes.")], power=round(mass * 40))
    return p


def actuators():
    out = []
    for c in BIG:
        force, stroke, knee, steer, jack = LEGS[c]
        m = {MEDIUM: 3, LARGE: 150, CAPITAL: 2000}[c]
        out.append(("Legs", piston(c, "Leg Piston", force, stroke, m, f"A walker's leg: {stroke:g} m of stroke, {force / 1000:g} kN. Wire a signal to work it.")))
        out.append(("Legs", piston(c, "Jack", force * 0.7, jack, m * 0.7, f"A short heavy jack, {jack:g} m of stroke, {force * 0.7 / 1000:g} kN: lifts a body off its tracks.")))
        h = c.half
        p = Part(f"{c.name} Knee Hinge", c, "Legs", f"A motor hinge for a leg: holds {knee / 1000:g} kN m.", role="joint", tool="wrench", color="#4f6a7a")
        p.add(box((-h, -h, -h), (h, h, h), "#3b4046", side="base"))
        p.add(box((h * 0.2, -h * 1.04, -h * 1.04), (h * 1.04, h * 1.04, h * 1.04), "#f08a12", detail=True, side="leaf", paintable=False))
        p.sock("base", (-h, 0, 0), "-x")
        p.sock("leaf", (h, 0, 0), "+x")
        p.stats(m * 900, health(m), c.armour)
        p.set(joint={"type": "motor", "axis": "+y", "pivot": [h, 0, 0], "min": -100, "max": 100, "leaf": "leaf", "speed": {MEDIUM: 30, LARGE: 20, CAPITAL: 12}[c], "strength": knee, "give": 20, "overloadDamage": 0}, power=m * 50)
        out.append(("Legs", p))
        # A pivot a bogie swings on: its base bolts to a body's side or underside.
        p = Part(f"{c.name} Steering Pivot", c, "Tracks", "A pivot for a wheel or a track: bolt its base to a body's side or under it, a suspension or hub motor on its leaf: it steers with the driver.", role="joint", tool="wrench", color="#8a6d3b")
        p.add(box((-h, -h, -h), (h, h, h), "#3b4046", side="base"))
        p.add(box((h * 0.3, -h * 1.05, -h * 1.05), (h * 1.05, h * 1.05, h * 1.05), "#f08a12", detail=True, side="leaf", paintable=False))
        p.sock("base", (-h, 0, 0), "-x")
        p.sock("top", (0, h, 0), "+y")
        p.sock("leaf", (h, 0, 0), "+x")
        p.stats(m * 500, health(m), c.armour)
        p.set(joint={"type": "steering", "axis": "+y", "pivot": [0, 0, 0], "min": -35, "max": 35, "leaf": "leaf", "speed": {MEDIUM: 60, LARGE: 40, CAPITAL: 25}[c], "strength": steer})
        out.append(("Tracks", p))
    return out


# --- Power -------------------------------------------------------------------------------------------------------------


def plants():
    out = []
    for key, (word, desc, color, burn, by, smokes) in PLANTS.items():
        for c, (mw, spool) in by.items():
            n = 1 if c is CAPITAL else 3
            k = {"Boiler": 1, "Furnace": 0.8, "Genset": 0.5, "Reactor": 1.04}[key]
            t = PLANT_MASS[c] * k
            p = Part(f"{c.name} {word}", c, "Power plants", f"{desc} {mw:g} MW.", role="generator", tool="wrench", color=color)
            block(p, n, n, n)
            bands(p, n, n, n, "#2c2926", 2)
            faces(p, n, n, n)
            p.stats(t * 1000, health(t), c.armour)
            g = {"rate": mw * 1000, "spool": spool}
            ports = [power_port("Power out", "out", (0, 0, half(n, c)), "+z")]
            if burn:
                g["burnRate"] = round(mw * burn, 3)
                ports.append(pipe_port("Fuel in", "in", (-half(n, c), 0, 0), "-x", "fuel"))
                ports.append(pipe_port("Exhaust out", "out", (half(n, c), 0, 0), "+x", "exhaust"))
            else:
                g["fuel"] = "none"
            if smokes:
                stack(p, n, n, c)
            p.set(generator=g, ports=ports)
            out.append(("Plants", p))
    for c in BIG:
        h = c.half
        tall = 5
        for word, cells, w, m in (("Chimney", tall, 0.6, 1.0), ("Stack Section", 3, 0.6, 0.6), ("Wide Funnel", 3, 1.0, 1.4)):
            y = half(cells, c)
            t = {MEDIUM: 1.5, LARGE: 40, CAPITAL: 200}[c] * m
            p = Part(f"{c.name} {word}", c, "Power plants", "Bolt it on a boiler, a furnace or a running engine, or run a pipe to it from their exhaust: it puts out their smoke. Sections stack.", role="frame", tool="wrench", color="#3a3431")
            p.add(box((-h * w, -y, -h * w), (h * w, y, h * w)))
            p.add(box((-h * w * 1.2, y * 0.84, -h * w * 1.2), (h * w * 1.2, y * 1.02, h * w * 1.2), "#1b1816", detail=True))
            p.add(box((-h * w * 1.15, -y * 0.2, -h * w * 1.15), (h * w * 1.15, y * 0.02, h * w * 1.15), "#5a3a26", detail=True))
            p.sock("bottom", (0, -y, 0), "-y")
            p.sock("top", (0, y, 0), "+y")
            p.stats(t * 1000, health(t), c.armour // 2)
            p.set(ports=[pipe_port("Exhaust in", "in", (0, -y * 0.5, h * w), "+z", "exhaust")], smoke={"at": [0, round(y * 1.02, 3), 0], "size": round(0.8 * w * c.cell * (1.6 if word == "Wide Funnel" else 1), 3)})
            out.append(("Plants", p))
    return out


def stores():
    out = []
    for c in BIG:
        n, t, litres = TANKS[c]
        p = Part(f"{c.name} Fuel Bunker", c, "Fuel and charge", f"Holds {litres / 1000:g} kl of fuel.", role="fueltank", tool="wrench", color="#b8352c")
        block(p, n, n, n)
        bands(p, n, n, n, "#7c241d", 2)
        faces(p, n, n, n)
        p.stats(t * 1000, health(t), c.armour // 2)
        p.set(fuelTank={"capacity": litres}, ports=[pipe_port("Fuel out", "out", (half(n, c), 0, 0), "+x", "fuel")])
        out.append(("Power", p))
        n, t, kj = BATTERIES[c]
        p = Part(f"{c.name} Battery Bank", c, "Fuel and charge", f"Holds {kj / 1e6:g} GJ for the circuit it is on.", role="battery", tool="wrench", color="#23272c")
        block(p, n, n, n)
        bands(p, n, n, n, "#d6b13a", 1)
        faces(p, n, n, n)
        p.stats(t * 1000, health(t), c.armour // 2)
        p.set(battery={"capacity": kj}, ports=[power_port("Power out", "out", (half(n, c), 0, 0), "+x")])
        out.append(("Power", p))
        t, kw = ALTERNATORS[c]
        p = Part(f"{c.name} Alternator", c, "Fuel and charge", f"Fit it touching the engine: {kw / 1000:g} MW into the circuit while the engine runs.", role="alternator", tool="wrench", color="#8d9196")
        block(p, 1, 1, 1)
        faces(p, 1, 1, 1)
        p.stats(t * 1000, health(t), c.armour // 3)
        p.set(alternator={"rate": kw}, ports=[power_port("Power out", "out", (c.half, 0, 0), "+x")])
        out.append(("Power", p))
        cells = 7
        z = half(cells, c)
        w = c.cell * 0.2
        p = Part(f"{c.name} Fuel Main {cells * c.cell:g} m", c, "Fuel and charge", "A long fuel main: carries fuel between tanks and whatever burns it, where a link won't reach.", role="fuelline", tool="wrench", color="#e0b030")
        p.add(box((-z, -w, -w), (z, w, w)))
        p.sock("+x", (z, 0, 0), "+x")
        p.sock("-x", (-z, 0, 0), "-x")
        p.sock("bottom", (0, -w, 0), "-y")
        p.stats(t * 300, health(t / 3), 2)
        p.set(legacy=True)
        out.append(("Power", p))
        t = BALLAST[c]
        p = Part(f"{c.name} Ballast Block", c, "Ballast", f"{t:g} t of solid weight: down low, it keeps a tall body on its tracks.", role="frame", color="#50514f")
        block(p, 1, 1, 1)
        faces(p, 1, 1, 1)
        p.stats(t * 1000, health(t), c.armour)
        out.append(("Ballast", p))
    return out


def thrusters():
    out = []
    for word, c, cells, force, spool in THRUSTERS:
        cell = c.cell
        wide = 1 if c is CAPITAL else 3
        if c is SMALL:
            wide = 1 if word == "Light" else 3
        h = wide * cell / 2
        z = cells * cell / 2
        for kind in ("Jet", "Lift Fan"):
            jet = kind == "Jet"
            p = Part(f"{word} {kind}", c, "Thrusters", (f"A jet: {force / 1000:g} kN from fuel." if jet else f"An electric lift fan: {force / 1000:g} kN from the circuit.")
                     + " The push is away from its nozzle. Wire a throttle (0 to 1) or a height; unwired, the driver's W, S, A and D work it by how it is mounted, and Shift holds a height with those that push up.",
                     role="thruster", tool="wrench", color="#5c6066" if jet else "#4a6a78", group="Thrusters")
            p.add(box((-h, -h, -z), (h, h, z)))
            p.add(box((-h * 1.08, -h * 1.08, z * 0.5), (h * 1.08, h * 1.08, z * 1.03), "#2a2c2f", detail=True))
            p.add(box((-h * 0.7, -h * 0.7, z * 1.01), (h * 0.7, h * 0.7, z * 1.06), "#c8642a", detail=True, glow=True))
            p.sock("front", (0, 0, -z), "-z")
            p.sock("+x", (h, 0, 0), "+x")
            p.sock("-x", (-h, 0, 0), "-x")
            p.sock("+y", (0, h, 0), "+y")
            p.sock("-y", (0, -h, 0), "-y")
            mass = force / 18 / (9 if jet else 6)  # (lifts nine times, or six times, its own weight)
            p.stats(mass, health(mass / 1000), max(2, c.armour // 3), c.seconds)
            b = {"force": force, "spool": spool, "exhaust": "+z", "at": [0, 0, z]}
            ports = []
            if jet:
                b["burnRate"] = round(force / 1e6 * JET_BURN, 4)
                ports.append(pipe_port("Fuel in", "in", (-h, 0, 0), "-x", "fuel"))
            else:
                b["fuel"] = "electric"
                b["power"] = round(force / 1000 * FAN_KW, 2)
                ports.append(power_port("Power in", "in", (-h, 0, 0), "-x"))
            p.set(thruster=b, ports=ports)
            out.append(("Thrusters", p))
    return out


def everything():
    return engines() + drive_line() + wheels() + tracks() + suspensions() + actuators() + plants() + stores() + thrusters()


def main():
    return write_parts(OUT, everything())


if __name__ == "__main__":
    sys.exit(main())
