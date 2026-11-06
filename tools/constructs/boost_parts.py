#!/usr/bin/env python3
"""Writes jets, rockets, boosters, decouplers, repulsors and Anti-Grav: Constructs/Parts/Boost, and the demo craft. docs/sandbox/CONSTRUCTS.md, "Jets, rockets and repulsors".

Run it from the repo root: python3 -I tools/constructs/boost_parts.py (--check only compares), then python3 -I tools/constructs/assembling.py for
their recipes. Fix this file, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from big_drive_parts import health, pipe_port, power_port  # noqa: E402
from scale import CAPITAL, LARGE, MEDIUM, SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Boost")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")
BODY, DARK, GLOW, RING, FIELD = "#5c6066", "#2a2c2f", "#c8642a", "#b7bcc2", "#58c8e0"
JETS = [("Light", SMALL, 3, 12e3, 0.5), ("Small", SMALL, 7, 75e3, 0.8), ("Medium", MEDIUM, 3, 1.2e6, 1.4), ("Large", LARGE, 3, 18e6, 2.2), ("Capital", CAPITAL, 1, 3e8, 3.2)]
ROCKETS = [("Light", SMALL, 3, 30e3, 0.2), ("Small", SMALL, 7, 200e3, 0.3), ("Medium", MEDIUM, 3, 3e6, 0.5), ("Large", LARGE, 3, 40e6, 0.8), ("Capital", CAPITAL, 1, 5e8, 1.2)]
BOOSTERS = [("Small", SMALL, 7, 120e3, 8), ("Medium", MEDIUM, 3, 2e6, 20), ("Large", LARGE, 3, 30e6, 30)]
REPULSORS = [("", SMALL, 2, 30e3, 3, 40), ("Medium ", MEDIUM, 1, 800e3, 5, 800), ("Large ", LARGE, 1, 20e6, 8, 16000)]
ANTIGRAV = [("", SMALL, 2, 6, 80, 400), ("Medium ", MEDIUM, 1, 30, 1500, 8000), ("Large ", LARGE, 1, 120, 40000, 200000), ("Capital ", CAPITAL, 1, 500, 1000000, 5000000)]
DECOUPLERS = [("", SMALL, 2), ("Medium ", MEDIUM, 1), ("Large ", LARGE, 1), ("Capital ", CAPITAL, 1)]


def nozzle(name, c, cells, section, desc, mass):
    """As the thrusters of big_drive_parts: a body along z, a nozzle at +z."""
    wide = 1 if name.startswith("Light") else 3 if c is SMALL else 1
    h = wide * c.cell / 2
    z = cells * c.cell / 2
    p = Part(name, c, section, desc, role="thruster", tool="wrench", color=BODY, group="Thrusters")
    p.add(box((-h, -h, -z), (h, h, z)))
    p.add(box((-h * 1.08, -h * 1.08, z * 0.5), (h * 1.08, h * 1.08, z * 1.03), DARK, detail=True))
    p.add(box((-h * 0.7, -h * 0.7, z * 1.01), (h * 0.7, h * 0.7, z * 1.06), GLOW, detail=True, glow=True))
    p.sock("front", (0, 0, -z), "-z")
    p.sock("+x", (h, 0, 0), "+x")
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("+y", (0, h, 0), "+y")
    p.sock("-y", (0, -h, 0), "-y")
    p.stats(mass, health(mass / 1000), max(2, c.armour // 3), c.seconds)
    return p, h, z


def jets():
    out = []
    for word, c, cells, force, spool in JETS:
        p, h, z = nozzle(f"{word} Turbojet", c, cells, "Jets", f"Breathes air: {force / 1000:g} kN at sea level, less as the air thins, none in vacuum. Afterburner: its input on, at the top of its throttle, for 2.5 times the thrust and six times the fuel.", force / 18 / 11)
        rate = round(force / 1e6 * 2, 4)
        p.set(thruster={"kind": "air", "force": force, "spool": spool, "exhaust": "+z", "at": [0, 0, z], "burnRate": rate, "afterburner": {"force": round(force * 1.5), "burn": round(rate * 5, 4), "band": 0.9}},
              ports=[pipe_port("Fuel in", "in", (-h, 0, 0), "-x", "fuel")])
        out.append(("Jets", p))
    return out


def rockets():
    out = []
    for word, c, cells, force, spool in ROCKETS:
        p, h, z = nozzle(f"{word} Rocket Engine", c, cells, "Rockets", f"{force / 1000:g} kN anywhere. Fuel and oxidiser by pipe from Fluid Tanks. Its nozzle gimbals 8 degrees on its pitch and yaw inputs.", force / 18 / 40)
        rate = round(force / 1e6 * 6, 4)
        p.set(thruster={"kind": "rocket", "force": force, "spool": spool, "exhaust": "+z", "at": [0, 0, z], "burnRate": rate, "oxidiser": round(rate * 2, 4), "gimbal": 8, "gimbalRate": 60},
              ports=[pipe_port("Fuel in", "in", (-h, 0, 0), "-x", "fuel"), pipe_port("Oxidiser in", "in", (h, 0, 0), "+x", "oxidiser")])
        out.append(("Rockets", p))
    for word, c, cells, force, burn in BOOSTERS:
        p, h, z = nozzle(f"{word} Solid Booster", c, cells, "Rockets", f"{force / 1000:g} kN for {burn} s, once. Its input lights it, or Shift unwired. It can't be throttled or stopped.", force / 18 / 25)
        p.set(thruster={"kind": "solid", "force": force, "spool": 0.05, "exhaust": "+z", "at": [0, 0, z], "burnTime": burn}, ports=[pipe_port("Fuel in", "in", (-h, 0, 0), "-x", "fuel")],
              config=[{"key": "grain", "hidden": True, "unit": "s", "min": 0, "max": 1000, "default": 0}])
        out.append(("Rockets", p))
    return out


def plate(p, x, t, colour=None):
    p.add(box((-x, -t, -x), (x, t, x), colour))
    for n, at, d in (("+y", (0, t, 0), "+y"), ("-y", (0, -t, 0), "-y"), ("+x", (x, 0, 0), "+x"), ("-x", (-x, 0, 0), "-x"), ("+z", (0, 0, x), "+z"), ("-z", (0, 0, -x), "-z")):
        p.sock(n, at, d)


def decouplers():
    out = []
    for prefix, c, n in DECOUPLERS:
        x = n * c.cell / 2
        t = 0.2 * c.cell
        p = Part(f"{prefix}Decoupler", c, "Rockets", "A rising input and it goes: each side flies on as its own craft, with its own tanks. What is linked across it is cut.", role="logic", tool="wrench", color=RING, group="Rockets" if c is SMALL else None)
        plate(p, x, t)
        p.add(box((-x * 1.03, -t * 0.3, -x * 1.03), (x * 1.03, t * 0.3, x * 1.03), "#d0352c", detail=True))
        p.stats(10 * c.x * c.x, health(0.01 * c.x * c.x), 2, c.seconds)
        p.set(logic={"kind": "decoupler", "kick": 3})
        out.append(("Rockets", p))
    return out


def repulsors():
    out = []
    for prefix, c, n, force, reach, kw in REPULSORS:
        x = n * c.cell / 2
        p = Part(f"{prefix}Repulsor", c, "Repulsors", f"Pushes off what is under it, {force / 1000:g} kN at the ground, a tenth of that by {reach / 2:g} m, none at {reach:g} m. On power; its throttle wire sets it.", role="thruster", tool="wrench", color=BODY, group="Thrusters")
        t = x * 0.5
        p.add(box((-x, -x, -t), (x, x, t)))
        p.add(box((-x * 0.8, -x * 0.8, t * 0.96), (x * 0.8, x * 0.8, t * 1.14), FIELD, detail=True, glow=True))
        p.sock("front", (0, 0, -t), "-z")
        p.sock("+x", (x, 0, 0), "+x")
        p.sock("-x", (-x, 0, 0), "-x")
        p.sock("+y", (0, x, 0), "+y")
        p.sock("-y", (0, -x, 0), "-y")
        mass = force / 18 / 30
        p.stats(mass, health(mass / 1000), max(2, c.armour // 3), c.seconds)
        p.set(thruster={"kind": "repulsor", "force": force, "fuel": "electric", "power": kw, "spool": 0.1, "exhaust": "+z", "at": [0, 0, t], "range": reach}, ports=[power_port("Power in", "in", (-x, 0, 0), "-x")])
        out.append(("Repulsors", p))
    return out


def antigrav():
    out = []
    for prefix, c, n, radius, mass, kw in ANTIGRAV:
        x = n * c.cell / 2
        p = Part(f"{prefix}Anti-Grav Generator", c, "Repulsors", f"Cancels half the weight of what is within {radius} m, more on a big ship. Draws {kw / 1000:g} MW. Power gone, it stops at once. Its input switches it.", role="logic", tool="wrench", color=BODY, group="Thrusters" if c is SMALL else None)
        plate(p, x, x)
        p.add(box((-x * 0.6, x * 0.99, -x * 0.6), (x * 0.6, x * 1.15, x * 0.6), FIELD, detail=True, glow=True))
        p.stats(mass, health(mass / 1000), max(2, c.armour // 3), c.seconds)
        p.set(logic={"kind": "antigrav", "share": 0.5, "radius": radius}, power=float(kw), ports=[power_port("Power in", "in", (-x, 0, 0), "-x")])
        out.append(("Repulsors", p))
    return out


def parts():
    return jets() + rockets() + decouplers() + repulsors() + antigrav()


def data(a, b, out=None, to=None):
    link = {"type": "data", "from": a, "to": b}
    if out:
        link["fromPort"] = out
    if to:
        link["toPort"] = to
    return link


def pipe(a, b, into="Fuel in", out="Fuel out"):
    return {"type": "pipe", "from": a, "to": b, "fromPort": out, "toPort": into}


FORMAT = "call-of-slop-construct-blueprint/1"


def cross(block):
    """Five blocks in a plus and the corners: 0 the middle, 1 the front, 2 the back, 3 front left, 4 front right, 5 rear left, 6 rear right."""
    return [
        {"part": block},
        {"part": block, "on": 0, "socket": "-z", "by": "+z"},
        {"part": block, "on": 0, "socket": "+z", "by": "-z"},
        {"part": block, "on": 1, "socket": "-x", "by": "+x"},
        {"part": block, "on": 1, "socket": "+x", "by": "-x"},
        {"part": block, "on": 2, "socket": "-x", "by": "+x"},
        {"part": block, "on": 2, "socket": "+x", "by": "-x"},
    ]


CORNERS = ("front left", "front right", "rear left", "rear right")


def jet_platform():
    steps = cross("Fuel Tank")
    steps += [{"part": "Light Turbojet", "on": 3 + k, "socket": "-y", "by": "front"} for k in range(4)]  # 7..10
    steps.append({"part": "Seat", "on": 0, "socket": "+y", "by": "bottom"})  # 11
    steps.append({"part": "Flight Stabilizer", "on": 2, "socket": "+y", "by": "-y", "config": {"pattern": "Hover platform"}})  # 12
    steps.append({"part": "Light Turbojet", "on": 2, "socket": "+z", "by": "front"})  # 13: the push on
    steps.append({"part": "Light Turbojet", "on": 1, "socket": "-z", "by": "front"})  # 14: the push back
    steps.append({"part": "Input Reader", "on": 0, "socket": "-x", "by": "+x", "config": {"setting": "jump"}})  # 15
    steps.append({"part": "Flight Panel", "on": 1, "socket": "+y", "by": "-y"})
    links = [data(11, 12), data(12, 13), data(12, 14, "reverse"), data(11, 15), data(15, 13, None, "afterburner")]
    links += [data(12, 7 + k, port) for k, port in enumerate(CORNERS)]
    return {"format": FORMAT, "name": "Jet Hover Platform", "description": "Four turbojets hold it up and level. W and S push it on, Space lights the pusher's afterburner, Shift and Ctrl climb and sink, A and D turn.", "steps": steps, "links": links}


def fill(fluid):
    return {"fluid": fluid, "fill": 100}


def two_stage():
    steps = [
        {"part": "Fluid Tank", "config": fill("fuel")},  # 0: first stage fuel
        {"part": "Fluid Tank", "on": 0, "socket": "+y", "by": "-y", "config": fill("oxidiser")},  # 1: first stage oxidiser
        {"part": "Light Rocket Engine", "on": 0, "socket": "-y", "by": "front"},  # 2
        {"part": "Decoupler", "on": 1, "socket": "+y", "by": "-y"},  # 3
        {"part": "Fluid Tank", "on": 3, "socket": "+y", "by": "-y", "config": fill("fuel")},  # 4: second stage fuel
        {"part": "Fluid Tank", "on": 4, "socket": "+y", "by": "-y", "config": fill("oxidiser")},  # 5
        {"part": "Light Rocket Engine", "on": 4, "socket": "+x", "by": "+y"},  # 6
        {"part": "Light Rocket Engine", "on": 4, "socket": "-x", "by": "-y"},  # 7
        {"part": "Seat", "on": 5, "socket": "+y", "by": "bottom"},  # 8
        {"part": "Flight Stabilizer", "on": 5, "socket": "+z", "by": "-y", "config": {"pattern": "Gimballed rocket"}},  # 9
        {"part": "Input Reader", "on": 5, "socket": "-z", "by": "+x", "config": {"setting": "jump"}},  # 10
        {"part": "Toggle", "on": 5, "socket": "+x", "by": "-x"},  # 11
        {"part": "Math Block", "on": 5, "socket": "-x", "by": "+x", "config": {"operator": "multiply"}},  # 12
    ]
    links = [pipe(0, 2), pipe(1, 2, "Oxidiser in", "Fluid out"), pipe(4, 6), pipe(4, 7), pipe(5, 6, "Oxidiser in", "Fluid out"), pipe(5, 7, "Oxidiser in", "Fluid out"),
             data(8, 9), data(8, 10), data(10, 3), data(10, 11), data(9, 2), data(9, 12), data(11, 12, None, "b"), data(12, 6), data(12, 7),
             data(9, 2, "pitch", "gimbal pitch"), data(9, 2, "yaw", "gimbal yaw"), data(9, 6, "pitch", "gimbal pitch"), data(9, 6, "yaw", "gimbal yaw"), data(9, 7, "pitch", "gimbal pitch"), data(9, 7, "yaw", "gimbal yaw")]
    return {"format": FORMAT, "name": "Two-Stage Rocket", "description": "Shift opens the throttle, W, S, A and D lean it, Space drops the first stage and lights the second.", "steps": steps, "links": links}


def hover_car():
    steps = cross("Frame Block")
    steps += [{"part": "Repulsor", "on": 3 + k, "socket": "-y", "by": "front"} for k in range(4)]  # 7..10
    steps.append({"part": "Seat", "on": 0, "socket": "+y", "by": "bottom"})  # 11
    steps.append({"part": "Flight Stabilizer", "on": 2, "socket": "+y", "by": "-y", "config": {"pattern": "Hover platform"}})  # 12
    steps.append({"part": "Battery", "on": 1, "socket": "+y", "by": "-y"})  # 13
    steps.append({"part": "Light Lift Fan", "on": 2, "socket": "+z", "by": "front"})  # 14
    steps.append({"part": "Light Lift Fan", "on": 1, "socket": "-z", "by": "front"})  # 15
    links = [data(11, 12), data(12, 14), data(12, 15, "reverse")]
    links += [data(12, 7 + k, port) for k, port in enumerate(CORNERS)]
    links += [{"type": "power", "from": 13, "to": k, "fromPort": "Power out", "toPort": "Power in"} for k in (7, 8, 9, 10, 14, 15)]
    return {"format": FORMAT, "name": "Hover Car", "description": "Four repulsors and a battery. Shift lifts it off, W and S drive it, A and D turn it, Ctrl sets it down. It follows the ground.", "steps": steps, "links": links}


def barge():
    steps = cross("Concrete Floor 2x2 m") if False else cross("Fuel Tank")
    steps += [{"part": "Light Lift Fan", "on": 3 + k, "socket": "-y", "by": "front"} for k in range(4)]  # 7..10
    steps.append({"part": "Seat", "on": 0, "socket": "+y", "by": "bottom"})  # 11
    steps.append({"part": "Flight Stabilizer", "on": 2, "socket": "+y", "by": "-y", "config": {"pattern": "Hover platform"}})  # 12
    steps.append({"part": "Anti-Grav Generator", "on": 3, "socket": "+y", "by": "-y"})  # 13
    steps.append({"part": "Anti-Grav Generator", "on": 4, "socket": "+y", "by": "-y"})  # 14
    steps.append({"part": "Light Lift Fan", "on": 2, "socket": "+z", "by": "front"})  # 15
    steps.append({"part": "Battery", "on": 1, "socket": "+y", "by": "-y"})  # 16
    steps.append({"part": "Battery", "on": 6, "socket": "+y", "by": "-y"})  # 17
    steps.append({"part": "Generator", "on": 5, "socket": "+y", "by": "-y"})  # 18
    links = [data(11, 12), data(12, 15)]
    links += [data(12, 7 + k, port) for k, port in enumerate(CORNERS)]
    links += [{"type": "power", "from": 16, "to": k, "fromPort": "Power out", "toPort": "Power in"} for k in (7, 8, 9, 10, 13, 14, 15)]
    return {"format": FORMAT, "name": "Anti-Grav Barge", "description": "Two generators carry her whole weight; small fans steer her up and down. Lose the power and she falls.", "steps": steps, "links": links}


def blueprints():
    code = 0
    for file, b in (("JetHoverPlatform", jet_platform()), ("TwoStageRocket", two_stage()), ("HoverCar", hover_car()), ("AntiGravBarge", barge())):
        path = os.path.join(BLUEPRINTS, file + ".json")
        text = json.dumps(b, indent=1) + "\n"
        if "--check" in sys.argv:
            with open(path, encoding="utf-8") as h:
                code |= h.read() != text
        else:
            with open(path, "w", encoding="utf-8", newline="\n") as h:
                h.write(text)
    return code


def main():
    return write_parts(OUT, parts()) | blueprints()


if __name__ == "__main__":
    sys.exit(main())
