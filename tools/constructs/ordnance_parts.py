#!/usr/bin/env python3
"""Writes the sections of modular bombs and missiles, and the Ordnance Bench: Constructs/Parts/Ordnance. docs/sandbox/CONSTRUCTS.md, "Modular ordnance".

A section is a real part (made in survival, stored, carried) whose "ordnance" block is what it adds to a design. Its boxes run along Z, nose to -z, at the reference
diameter of 25 cm; the bench scales them. Run it from the repo root: python3 -I tools/constructs/ordnance_parts.py (--check only compares), then python3 -I tools/constructs/assembling.py
for their recipes. Fix this file, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Ordnance")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")
R = 0.125  # m: the reference radius
GROUP = "Weapons"
OLIVE, DARK, STEEL, BRASS, RED, WHITE, GREY = "#4a5530", "#2a2c26", "#7c838c", "#c8a838", "#a83428", "#d8d8d0", "#565a5e"


def section(name, slot, length, mass, color, desc, band=None, fins=False, nose=False, wings=0.0, canards=False, nozzle=False, pack=False, glass=False, ring=False,
            lengths=None, **ordnance):
    """A round section, with a coloured band, fins, wings, canards, a nozzle, a chute pack, a glass eye, a ring tail or a tapered nose.
    lengths: (shortest, longest) as shares of its own length, when the bench may set its length (sizes(), below)."""
    p = Part(name, SMALL, "Ordnance sections", desc, tool="wrench", color=color, group=GROUP)
    z = length / 2
    p.add(box((-R, -R, -z), (R, R, z), hidden=True))
    if nose:
        p.add(box((-R, -R, -z), (R, R, z), color, detail=True, shape="cylinder", segments=16, taper=[0.2, 0.2]))
    else:
        p.add(box((-R, -R, -z), (R, R, z), color, detail=True, shape="cylinder", segments=16))
    if band:
        p.add(box((-R * 1.01, -R * 1.01, -z * 0.5), (R * 1.01, R * 1.01, -z * 0.3), band, detail=True, shape="cylinder", segments=16))
    if fins:
        p.add(box((-0.012, -R * 1.9, -z * 0.8), (0.012, R * 1.9, z * 0.8), DARK, detail=True))
        p.add(box((-R * 1.9, -0.012, -z * 0.79), (R * 1.9, 0.012, z * 0.79), DARK, detail=True))
    if canards:
        p.add(box((-0.01, -R * 1.5, -z * 0.5), (0.01, R * 1.5, z * 0.2), DARK, detail=True))
        p.add(box((-R * 1.5, -0.01, -z * 0.49), (R * 1.5, 0.01, z * 0.19), DARK, detail=True))
    if wings:
        p.add(box((-R - wings, -0.015, -z * 0.7), (R + wings, 0.015, z * 0.7), color if color != DARK else GREY, detail=True))
    if nozzle:
        p.add(box((-R * 0.85, -R * 0.85, z * 0.6), (R * 0.85, R * 0.85, z * 1.02), DARK, detail=True, shape="cylinder", segments=12, taper=[0.6, 0.6]))
    if pack:
        p.add(box((-R * 0.8, -R * 0.8, z * 0.4), (R * 0.8, R * 0.8, z * 1.02), "#c0b080", detail=True, shape="cylinder", segments=12, taper=[0.6, 0.6]))
    if glass:
        p.add(box((-R * 0.6, -R * 0.6, -z * 1.02), (R * 0.6, R * 0.6, -z * 0.7), "#20303a", detail=True, glass=True, shape="cylinder", segments=12, taper=[0.4, 0.4]))
    if ring:
        p.add(box((-R * 1.6, -R * 1.6, -z * 0.2), (R * 1.6, R * 1.6, z * 0.8), DARK, detail=True, shape="cylinder", segments=16))
    p.sock("-z", (0, 0, -z), "-z")
    p.sock("+z", (0, 0, z), "+z")
    p.stats(mass, 40, 1, 1.0)
    group = group_of(name, slot, ordnance.get("kind"))
    block = {"slot": slot, "length": length, "group": group, "groupOrder": GROUPS.index(group) + 1}
    if lengths:
        block["lengths"] = sizes(length, *lengths)
    numbers = {k: v for k, v in ordnance.items() if k not in TOP}
    block.update({k: v for k, v in ordnance.items() if k in TOP})
    if numbers:
        block["numbers"] = numbers
    p.set(ordnance=block)
    return "Sections", p


# The Ordnance Bench's picker, nose to tail in the order a weapon is built (a design's sections are stacked the same way). Every section goes in every
# design: a design with a motor that lights as it leaves flies as a missile, one without falls as a bomb (OrdnanceDesign.Powered).
GROUPS = ["Fuses", "Seekers", "Forward control", "Warheads", "Sub-payloads", "Body and fuel", "Wings", "Propulsion", "Tail control", "Stabilizers and brakes"]
FORWARD = ("Canards", "Reaction Jets")
SUBS = ("cluster", "incendiary", "mines", "mirv", "rods")


def group_of(name, slot, kind):
    """A section's group in the bench's picker, from its slot."""
    if slot == "control":
        return "Forward control" if name in FORWARD else "Tail control"
    if slot == "payload":
        return "Sub-payloads" if kind in SUBS else "Warheads"
    return {"fuse": "Fuses", "seeker": "Seekers", "spacer": "Warheads", "body": "Body and fuel", "fuel": "Body and fuel", "wings": "Wings",
            "motor": "Propulsion", "stage": "Propulsion", "stabilizer": "Stabilizers and brakes"}[slot]


TOP = ("payload", "blast", "damage", "crater", "fuseDelay", "settings", "kind", "massFixed")  # (the rest are the flight's numbers)
STEP = 0.05  # m at 25 cm: a length's step on the bench


def sizes(length, lo, hi, presets=None):
    """A lengthable section's lengths (m at 25 cm): its shortest and longest as shares of its own length, and the bench picker's presets
    (its own length, then twice and three times it, unless given). Its mass, filling, built-in fuel, propellant and the per-length settings
    go with the length; a section's own length is still the one its numbers are written at."""
    snap = lambda x: round(round(x / STEP) * STEP, 3)  # noqa: E731
    lo, hi = max(STEP, snap(length * lo)), snap(length * hi)
    presets = presets or [length, length * 2, length * 3]
    return {"min": lo, "max": hi, "step": STEP, "presets": [round(p, 3) for p in presets if lo <= p <= hi]}


WARHEAD, MOTOR, BODY = (0.5, 6), (0.5, 4), (0.2, 8)  # a section's shortest and longest, as shares of its own length
DELAY = {"key": "delay", "label": "Delay", "unit": "s", "min": 0.05, "max": 30, "step": 0.05, "default": 1}


def setting(key, label, unit, lo, hi, step, default, per_length=False):
    """A setting; per_length: its least, most and default are for the section's own length and go with its length (a tank's fuel, a motor's burn)."""
    s = {"key": key, "label": label, "unit": unit, "min": lo, "max": hi, "step": step, "default": default}
    if per_length:
        s["perLength"] = True
    return s


CLASS = setting("class", "Thrust class", "", 1, 5, 1, 1)
CHANNEL = setting("channel", "Channel", "", 1, 99, 1, 1)
WHITE2, ORANGE, BLUE = "#c8ccc8", "#b86a28", "#3a4c68"


def stage1():
    return [
        section("Impact Fuse", "fuse", 0.2, 3, GREY, "Goes off the moment it hits.", nose=True, fuseDelay=0.05),
        section("Delay Fuse", "fuse", 0.25, 4, GREY, "Goes off a set time after it hits: through a deck first.", band=BRASS, nose=True, settings=[DELAY]),
        section("Warhead HE", "payload", 0.5, 25, OLIVE, "25 kg of high explosive for its 0.5 m. Set its length for a bigger charge.", band=BRASS, payload=20, lengths=WARHEAD),
        section("Warhead Blast", "payload", 0.6, 28, "#5a4a30", "Thin-cased: a wider blast for its weight, and less damage.", band=RED, payload=22, blast=1.5, damage=0.6, lengths=WARHEAD),
        section("Warhead Bunker", "payload", 0.6, 45, "#3a3d40", "A thick steel case: a deep crater.", band=WHITE, payload=20, damage=1.2, crater=2.2, lengths=WARHEAD),
        section("Bomb Tail Fins", "stabilizer", 0.4, 6, DARK, "Keeps a bomb nose down.", fins=True, drag=0.2),
    ]


def stage3():
    """Flight: motors and their fuels, tanks, separators, steering and seekers (docs/sandbox/CONSTRUCTS.md, "Modular ordnance: flight")."""
    return [
        # Motors: thrust in N at 25 cm (with the cube of the scale), fuel use kg/s, built-in fuel kg, "air": breathes air.
        section("Solid Motor", "motor", 0.8, 20, WHITE2, "A solid rocket: strong and simple. Set its burn; it can't be stopped.", nozzle=True, kind="solid",
                settings=[setting("burn", "Burn time", "s", 0.5, 12, 0.5, 3, True), CLASS], thrust=9000, mass_burn=4, mass_class=6, lengths=MOTOR),
        section("Boost Motor", "motor", 0.7, 24, ORANGE, "A short, hard kick off the rail: put it behind a Stage Separator.", nozzle=True, kind="solid",
                settings=[setting("burn", "Burn time", "s", 0.3, 4, 0.1, 1.2, True), CLASS], thrust=22000, mass_burn=6, mass_class=8, lengths=MOTOR),
        section("Liquid Motor", "motor", 0.6, 18, STEEL, "Burns what its stage's tanks hold, and gimbals while it burns.", nozzle=True, kind="liquid",
                settings=[CLASS], thrust=6000, use=1.0, gimbal=30, mass_class=4),
        section("Hybrid Motor", "motor", 0.7, 22, GREY, "A solid grain and a liquid oxidiser: six seconds on its own, longer with a tank.", nozzle=True, kind="hybrid",
                settings=[CLASS], thrust=7000, use=1.2, fuel=7, mass_class=5, lengths=MOTOR),
        section("Cold Gas Thruster", "motor", 0.4, 8, STEEL, "A bottle of gas: a gentle push that works anywhere, air or none, and no flame.", nozzle=True, kind="cold gas",
                thrust=1500, use=0.5, fuel=3, lengths=MOTOR),
        section("Ramjet", "motor", 1.0, 25, GREY, "Breathes air, and only works at 150 m/s or more: boost it first. Nothing in thin air.", band=ORANGE, nozzle=True, kind="air",
                settings=[CLASS], thrust=12000, use=1.2, air=1, minSpeed=150, mass_class=5),
        section("Turbojet", "motor", 0.9, 30, DARK, "A small jet: weak, but it sips fuel. Breathes air: nothing in thin air. With wings, a cruise missile.", band=STEEL, nozzle=True, kind="air",
                settings=[CLASS], thrust=2500, use=0.2, air=1, mass_class=6),
        # One tank of any length (the Long Missile Fuel Tank was this at 1 m: OrdnanceDesign.Folded). 3 kg of it are its ends, the same at any length.
        section("Missile Fuel Tank", "fuel", 0.5, 6, STEEL, "Fuel for the liquid, hybrid and air motors of its stage. Set its length for more.", band=BLUE,
                settings=[setting("fuel", "Fuel load", "kg", 2, 75, 1, 15, True)], mass_fuel=1, massFixed=3, lengths=(0.2, 8, [0.5, 1.0, 2.0])),
        section("Stage Separator", "stage", 0.12, 4, RED, "Splits a missile into stages: what is behind it burns first and drops when it burns out.", band=DARK),
        section("Body Tube", "body", 0.5, 5, WHITE2, "An empty length of airframe: room, and weight.", lengths=BODY),
        # Steering: degrees a second of turn. Fins and canards need air and speed; a gimbal and vanes need a burning motor; reaction jets work anywhere.
        section("Tail Fins", "control", 0.4, 6, DARK, "Steer in the air at speed. Nothing in thin air.", fins=True, turnAir=60, drag=0.03),
        section("Canards", "control", 0.3, 5, DARK, "Small fins near the nose: a sharp turn in the air, and some drag.", canards=True, turnAir=90, drag=0.05),
        section("Gimbal Mount", "control", 0.2, 7, STEEL, "Swings the motor: turns while it burns, in air or out of it.", band=BRASS, turnBurn=80),
        section("Jet Vanes", "control", 0.15, 3, GREY, "Vanes in the exhaust: a little turn while it burns.", turnBurn=45),
        section("Reaction Jets", "control", 0.25, 10, GREY, "Puffs of gas round the body: a slow turn at any speed, anywhere.", band=RED, turnAny=35),
        # Seekers: how it finds what it steers for (the game's own pieces: flares, Cameras, Wireless Senders, Flight Stabilizers, beacons).
        section("Heat Seeker", "seeker", 0.35, 6, WHITE2, "Locks the moving thing nearest straight ahead as it goes. Flares draw it.", nose=True, glass=True, kind="heat", reach=1200, cone=25),
        section("Radar Seeker", "seeker", 0.4, 9, GREY, "Locks the vehicle nearest straight ahead, moving or not. Flares don't fool it.", nose=True, kind="radar", reach=2000, cone=15),
        section("Command Link", "seeker", 0.3, 5, GREY, "Goes for what the gunner looks at as it leaves: a vehicle, a base or a spot on the ground.", nose=True, band=BRASS, kind="command", reach=2500, cone=10),
        section("Laser Seeker", "seeker", 0.35, 7, DARK, "Rides the spot the gunner keeps looking at, or the spot a Camera on its channel looks at (a drone's).", nose=True, glass=True,
                kind="laser", reach=3000, cone=30, settings=[CHANNEL]),
        section("GPS Unit", "seeker", 0.3, 5, OLIVE, "Flies to its Flight Stabilizer's waypoint, read as it flies: wire the waypoint from logic.", nose=True, band=BLUE, kind="gps", reach=50000, cone=180),
        section("Radio Homer", "seeker", 0.3, 5, OLIVE, "Homes on the Wireless Sender on its channel.", nose=True, band=RED, kind="radio", reach=3000, cone=180, settings=[CHANNEL]),
        section("Beacon Homer", "seeker", 0.3, 5, OLIVE, "Homes on the beacon nearest straight ahead as it goes.", nose=True, band=WHITE, kind="beacon", reach=2000, cone=30),
    ]


def stage4():
    """Bombs and payloads: fuses, sub-payloads, series charges, glide and cruise kits, rocket assist, stabilizers."""
    count = lambda lo, hi, d: setting("count", "Count", "", lo, hi, 1, d, True)  # noqa: E731 (a longer bay holds more)
    return [
        # Fuses: any together; impact always works.
        section("Proximity Fuse", "fuse", 0.2, 4, GREY, "Goes off when something flying comes within its range.", band=RED, nose=True, kind="prox",
                settings=[setting("range", "Range", "m", 1, 30, 1, 6)]),
        section("Altitude Fuse", "fuse", 0.2, 4, GREY, "Goes off coming down to its height over the ground: for bomblets and air bursts.", band=BLUE, nose=True, kind="altitude",
                settings=[setting("height", "Height", "m", 2, 500, 1, 30)]),
        section("Timer Fuse", "fuse", 0.2, 3, GREY, "Goes off a set time after it leaves its rack.", band=BRASS, nose=True, kind="timer",
                settings=[setting("time", "Time", "s", 0.5, 300, 0.5, 10)]),
        section("Airburst Fuse", "fuse", 0.2, 4, GREY, "Goes off a set distance short of what it steers for.", band=WHITE, nose=True, kind="airburst",
                settings=[setting("distance", "Distance", "m", 2, 200, 1, 15)]),
        section("Remote Fuse", "fuse", 0.2, 4, GREY, "Goes off when a Wireless Sender on its channel turns on, at any range.", band=OLIVE, nose=True, kind="remote",
                settings=[CHANNEL]),
        # Sub-payloads: the charge's filling shared among what it lets go, on the game's own shells and blasts.
        section("Cluster Dispenser", "payload", 0.8, 30, OLIVE, "Opens and scatters bomblets that go off where they land. Give it an Altitude Fuse.", band=BRASS,
                kind="cluster", payload=18, blast=1.1, damage=0.9, settings=[count(4, 400, 24)], lengths=WARHEAD),
        section("Fire Bomblets", "payload", 0.8, 28, "#6a3a20", "Scatters bomblets that set fire to what is near where they land.", band=RED,
                kind="incendiary", payload=15, settings=[count(4, 200, 12)], lengths=WARHEAD),
        section("Mine Dispenser", "payload", 0.9, 34, OLIVE, "Sows mines that lie until something moves near them, or their life runs out.", band=WHITE,
                kind="mines", payload=16, prox=4, settings=[count(2, 120, 8), setting("life", "Mine life", "s", 10, 900, 10, 180)], lengths=WARHEAD),
        section("MIRV Bus", "payload", 0.9, 40, WHITE2, "Splits into small guided warheads that fan out round what it steers for.", band=RED,
                kind="mirv", payload=24, settings=[count(2, 24, 4)], lengths=WARHEAD),
        section("Rod Bundle", "payload", 1.0, 60, STEEL, "Steel rods thrown down fast: what they hit takes their speed. Drop them from high up.",
                kind="rods", payload=25, blast=0.4, damage=2.5, crater=1.5, settings=[count(2, 60, 8)], lengths=WARHEAD),
        section("Kinetic Penetrator", "payload", 0.8, 70, "#3a3d40", "A dense rod with a little filling: the faster it hits, the harder.", band=STEEL, nose=True,
                kind="kinetic", payload=12, blast=0.5, damage=3, crater=2.5, lengths=WARHEAD),
        section("Tandem Spacer", "spacer", 0.15, 3, DARK, "Splits the payload ahead of it from the one behind: they go off one after the other, the later one deeper.", band=BRASS),
        # Glide, cruise and rocket assist (bombs).
        section("Glide Wings", "wings", 0.8, 30, OLIVE, "Wings: it glides a long way from high up. Add a Glide Guidance Kit and a seeker to steer it.", wings=0.9, lift=1.0, drag=0.04),
        section("Pop-out Wings", "wings", 0.6, 20, OLIVE, "Folded wings that spring out a moment after it drops: a shorter glide.", wings=0.15, lift=0.8, drag=0.03, deploy=0.8),
        section("Cruise Wings", "wings", 0.9, 26, STEEL, "Long thin wings that hold it up at speed: with a Turbojet, a cruise missile.", wings=1.3, lift=1.3, drag=0.025),
        section("Glide Guidance Kit", "control", 0.35, 8, DARK, "Moving tail fins and a computer: it steers for what its seeker finds.", fins=True, turnAir=40, drag=0.02),
        section("Rocket Assist", "motor", 0.6, 22, ORANGE, "A rocket that lights a second after the drop and drives the bomb on: further, and harder into what it hits.", nozzle=True, kind="solid",
                settings=[setting("burn", "Burn time", "s", 0.5, 8, 0.5, 2, True)], thrust=7000, wait=1, mass_burn=4, lengths=MOTOR),
        # Stabilizers.
        section("Retard Chute", "stabilizer", 0.3, 6, DARK, "A chute that opens off the rack: it falls slow and steep, and the plane is long gone.", pack=True, drag=1.4, deploy=0.3),
        section("Ballute", "stabilizer", 0.3, 5, DARK, "A balloon brake: slower and steeper than fins alone.", pack=True, drag=0.5, deploy=0.2),
        section("Ring Tail", "stabilizer", 0.35, 7, DARK, "A ring round the tail: steady, with a little steering in the air.", ring=True, drag=0.05, turnAir=8),
    ]


def rail(name, load, length, mass, desc):
    """A launch rail: a beam with a slide under it whose lug takes a designed weapon. What it lets go leaves forward along it; a missile lights on it."""
    p = Part(name, SMALL, "Bombs", desc + f" Rated for {load:g} kg.", role="logic", tool="welder", color=DARK, group=GROUP)
    z = length / 2
    p.add(box((-0.06, -0.02, -z), (0.06, 0.08, z)))
    p.add(box((-0.035, -0.06, -z * 0.85), (0.035, -0.02, z * 0.85), BRASS))
    p.add(box((-0.07, 0.0, z * 0.7), (0.07, 0.1, z), GREY, detail=True))
    p.sock("lug0", (0, -0.06, 0), "-y")
    p.sock("top", (0, 0.08, 0), "+y")
    p.sock("back", (0, 0.03, z), "+z")
    p.sock("front", (0, 0.03, -z), "-z")
    p.stats(mass, 120, 3, 2.0)
    p.set(logic={"kind": "bombrack", "load": load, "rail": True}, legacy=True)  # (an old mount: the Ordnance Rack launches missiles; it loads and works but isn't offered)
    return "Rails", p


def rails():
    return [
        rail("Launch Rail", 600, 1.4, 12, "Fires the designed missile on it forward along the rail, under its own thrust: one too heavy for its motor stays put. A bomb on it just drops."),
        rail("Heavy Launch Rail", 8000, 3.0, 60, "A long rail for a big missile. Fires it forward under its own thrust; one too heavy for its motor stays put."),
    ]


def bench():
    p = Part("Ordnance Bench", SMALL, "Workbenches", "F opens it: design a bomb or a missile from sections, and build it here.", role="storage", tool="welder", color="#4a4f45", group="Weapons")
    p.add(box((-1, 0, -0.5), (1, 0.9, 0.5)))
    p.add(box((-0.95, 0.9, -0.45), (0.95, 0.96, 0.45), OLIVE, detail=True))
    p.add(box((-1, 0.9, 0.44), (1, 1.6, 0.5), "#3a3f38"))
    for k in range(3):
        x = -0.6 + 0.6 * k
        p.add(box((x - 0.1, 0.96, -0.3), (x + 0.1, 1.02, 0.3), BRASS, detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.stats(180, 900, 3, 4.0)
    slots = [{"min": [-0.9 + 0.6 * k, 1.0, -0.4], "max": [-0.4 + 0.6 * k, 1.5, 0.4], "stack": 12, "label": f"Bay {k + 1}"} for k in range(3)]
    p.set(storage={"ordnanceBench": True, "slots": slots})
    return "", p


def parts():
    return [bench()] + stage1() + stage3() + stage4() + rails()


def plane(name, desc, rack, weapon):
    """The Light Plane with a rack or rail under each outer wing, each holding a designed weapon by its key; the pilot's fire key lets go."""
    with open(os.path.join(BLUEPRINTS, "LightPlane.json"), encoding="utf-8") as f:
        b = json.load(f)
    s, links = b["steps"], b["links"]
    wings, stab = (11, 12), 17
    for wing in wings:
        s.append({"part": rack, "on": wing, "socket": "-y", "by": "top"})
        at = len(s) - 1
        s.append({"part": weapon, "on": at, "socket": "lug0", "by": "lug"})
        links.append({"type": "data", "from": stab, "to": at, "fromPort": "fire"})
    return {"format": b["format"], "name": name, "description": desc, "steps": s, "links": links}


def blueprints():
    return [
        ("StrikePlane.json", plane("Strike Plane", "The Light Plane with a heat-seeking missile on a Launch Rack under each wing. Point at what flies and fire: each locks what is nearest straight ahead.",
                                   "Launch Rack", "Ordnance:M25:Heat Seeker+Proximity Fuse+Warhead HE+Tail Fins+Solid Motor")),
        ("GlideBomber.json", plane("Glide Bomber", "The Light Plane with a GPS glide bomb under each wing: set the Flight Stabilizer's waypoint, let go from high up, and they glide there.",
                                   "Launch Rack", "Ordnance:B25:GPS Unit+Impact Fuse+Warhead HE+Pop-out Wings+Glide Guidance Kit")),
    ]


def main():
    code = write_parts(OUT, parts())
    check = "--check" in sys.argv
    for name, bp in blueprints():
        path = os.path.join(BLUEPRINTS, name)
        text = json.dumps(bp, indent=2) + "\n"
        if check:
            with open(path, encoding="utf-8") as f:
                if f.read() != text:
                    print(f"out of date: {path}")
                    code = 1
        else:
            with open(path, "w", encoding="utf-8") as f:
                f.write(text)
    return code


if __name__ == "__main__":
    sys.exit(main())
