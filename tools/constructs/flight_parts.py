#!/usr/bin/env python3
"""Writes the flight control parts: Constructs/Parts/Flight, and the demo craft. docs/sandbox/CONSTRUCTS.md, "Flight control".

Flight Stabilizers in three sizes (a seat's axes in, mixed channels out), Flight Panels (telemetry and autopilot buttons), and
the Quadcopter and Stabilised Airship blueprints. Run it from the repo root: python3 -I tools/constructs/flight_parts.py
(--check only compares), then python3 -I tools/constructs/assembling.py for their recipes. Fix this file, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from air_parts import airship  # noqa: E402
from scale import CAPITAL, LARGE, MEDIUM, SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Flight")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")
SECTION = "Flight control"
HOUSING, DARK, ARROW, PIN, SCREEN = "#39414a", "#20252b", "#e0b030", "#b08a4a", "#101416"
KEYS = ["Auto", "Rotorcraft", "Plane", "Rocket"]
MODES = ["Hold altitude", "Hold heading", "Hover", "Waypoint", "Return home"]
FILL = 22  # (a little short of her weight: the lift jet keeps her up and lets her down, and she needn't vent to come down)
FIVE = (("+x", "+x"), ("-x", "-x"), ("+z", "+z"), ("-z", "-z"), ("-y", "-y"))


def number(key, label, group, unit, lo, hi, step, default, tip, **more):
    return dict({"key": key, "label": label, "group": group, "unit": unit, "min": lo, "max": hi, "step": step, "default": default, "tooltip": tip}, **more)


def toggle(key, label, group, default, tip):
    return {"key": key, "type": "toggle", "label": label, "group": group, "default": default, "tooltip": tip}


def choice(key, label, group, options, default, tip):
    return {"key": key, "type": "choice", "label": label, "group": group, "options": options, "default": default, "tooltip": tip}


def adv(*a, **k):
    return number(*a, advanced=True, **k)


def axis(name, label, rate):
    out = [adv(f"{name}Rate", "Rate limit", label, "deg/s", 0, 720, 5, 0, "The fastest it turns this way. 0: Max turn.")] if rate else []
    return out + [
        adv(f"{name}Response", "Response (P)", label, "%", 0, 400, 5, 0, "How hard it goes for what is asked. Up: quicker; too much rocks. 0: auto."),
        adv(f"{name}Damping", "Damping (D)", label, "%", 0, 400, 5, 0, "How hard it stops. Up: less overshoot; too much shakes. 0: auto."),
        adv(f"{name}Hold", "Hold (I)", label, "%", 0, 400, 5, 0, "How it works off a steady push. Up: it holds tighter; too much wanders. 0: auto."),
        adv(f"{name}Expo", "Expo", label, "%", 0, 100, 5, 0, "Soft around the middle of a wired stick. A key is all or nothing."),
    ]


CONFIG = [
    choice("keys", "Keys", "Control", KEYS, 0, "What the keys mean. Auto: wings and a forward push make it a plane, nozzles a rocket, anything else a rotorcraft. It finds what it can use by itself."),
    choice("flightPreset", "Preset", "Control", ["Normal", "Docile", "Sport"], 0, "What auto means below: gentle, as it comes, or lively. Anything you set yourself stays."),
    number("strength", "Strength", "Control", "%", 10, 300, 5, 100, "How hard it corrects. Lower it if the craft shakes."),
    toggle("level", "Stabilise level", "Control", True, "Off: the keys ask for turning, and it stays at whatever angle you leave it."),
    toggle("mouseAim", "Mouse aim", "Control", False, "It flies where the seat looks."),
    toggle("invertPitch", "Invert pitch", "Control", False, "W lifts the nose and S drops it."),
    number("maxTilt", "Max lean", "Limits", "deg", 0, 80, 1, 0, "The most it leans. 0: the pattern's own."),
    number("maxTurn", "Max turn", "Limits", "deg/s", 0, 360, 5, 0, "The fastest it turns. 0: the pattern's own."),
    number("maxClimb", "Max climb", "Limits", "m/s", 0, 200, 0.5, 0, "The fastest it climbs. 0: the pattern's own."),
    number("maxSpeed", "Max speed", "Limits", "m/s", 0, 1000, 1, 0, "The speed the keys ask for. 0: the pattern's own."),
    number("maxDescent", "Max descent", "Limits", "m/s", 0, 200, 0.5, 0, "The fastest it comes down. 0: as Max climb."),
    number("trimPitch", "Trim, nose up", "Trim", "deg", -30, 30, 0.5, 0, "What it counts as level, nose up."),
    number("trimRoll", "Trim, right wing down", "Trim", "deg", -30, 30, 0.5, 0, "What it counts as level, right wing down."),
    toggle("autopilot", "Autopilot", "Autopilot", False, "On: it holds what the mode says. Its input and the Flight Panel set this too."),
    choice("apMode", "Mode", "Autopilot", MODES, 2, "What it holds."),
    number("targetAlt", "Altitude", "Autopilot", "m", 0, 20000, 1, 0, "The height held. 0: where it was when engaged."),
    number("targetHeading", "Heading", "Autopilot", "deg", -1, 360, 1, -1, "The heading held, clockwise from north. -1: where it pointed when engaged."),
    number("waypointX", "Waypoint x", "Autopilot", "m", -100000, 100000, 1, 0, "The point it flies to, world x."),
    number("waypointZ", "Waypoint z", "Autopilot", "m", -100000, 100000, 1, 0, "The point it flies to, world z."),
    *axis("pitch", "Pitch", True),
    *axis("roll", "Roll", True),
    *axis("yaw", "Yaw", True),
    *axis("climb", "Climb", False),
    adv("throttleCurve", "Throttle curve", "Feel", "%", -100, 100, 5, 0, "Above 0: finer low down. Below 0: finer high up."),
    adv("maxBank", "Max bank", "Feel", "deg", 0, 85, 1, 0, "The most it banks. 0: Max lean."),
    adv("maxPitchAngle", "Max pitch", "Feel", "deg", 0, 85, 1, 0, "The most it pitches. 0: Max lean."),
    adv("turnCoord", "Turn coordination", "Feel", "%", 0, 200, 5, 0, "How much the rudder follows a bank. 0: auto, all of it."),
    adv("autoLevel", "Auto-level", "Feel", "%", 0, 400, 5, 0, "How hard it comes level when let go. 0: auto."),
    adv("windHold", "Wind hold", "Feel", "%", 0, 400, 5, 0, "How fast it leans into a steady drift. 0: auto."),
    adv("landingAssist", "Landing assist", "Feel", "m", 0, 100, 1, 0, "Below this height it comes down gently. 0: off."),
    adv("collectiveRange", "Collective range", "Feel", "%", 0, 100, 5, 0, "The most of its rotors' push it uses. 0: all of it."),
    adv("thrustLimit", "Thrust limit", "Feel", "%", 0, 100, 5, 0, "The most of its thrusters' push it uses. 0: all of it."),
    {"key": "homeX", "hidden": True, "unit": "m", "min": -100000, "max": 100000, "default": 0},
    {"key": "homeZ", "hidden": True, "unit": "m", "min": -100000, "max": 100000, "default": 0},
]


def stabilizer(name, cls, s, mass, hp, armour, gyro, tool):
    """A flat box, its arrow showing forward: mount it level, facing the way the craft flies."""
    p = Part(name, cls, SECTION, "Takes a seat's controls and flies the craft: link its outputs to thrusters, flaps and gimbals.", role="logic", tool=tool, color=HOUSING, group="Flight control")
    h, t = 0.15 * s, 0.06 * s
    p.add(box((-h, -t, -h), (h, t, h)))
    p.add(box((-0.02 * s, t - 0.003 * s, -0.11 * s), (0.02 * s, t + 0.012 * s, 0.05 * s), ARROW, detail=True))
    p.add(box((-0.06 * s, t - 0.002 * s, -0.09 * s), (0.06 * s, t + 0.01 * s, -0.05 * s), ARROW, detail=True))
    for k in range(4):
        x = (-0.09 + 0.06 * k) * s
        p.add(box((x - 0.012 * s, t - 0.003 * s, 0.08 * s), (x + 0.012 * s, t + 0.016 * s, 0.12 * s), PIN, detail=True))
    for n, d in FIVE:
        p.sock(n, {"+x": (h, 0, 0), "-x": (-h, 0, 0), "+z": (0, 0, h), "-z": (0, 0, -h), "-y": (0, -t, 0)}[d], d)
    p.stats(mass, hp, armour, 2.0 if cls is SMALL else None)
    p.set(logic={"kind": "flightstabilizer", "gyro": gyro}, config=CONFIG, faces="forward", faceAxis="-z")
    return "", p


def box_panel(name, cls, s, mass, hp, armour, tool, desc, source, rows, touch=None):
    w, h, t = 0.7 * s, 0.5 * s, 0.03 * s
    p = Part(name, cls, SECTION, desc, role="logic", tool=tool, color=HOUSING, group="Flight control")
    p.add(box((-w / 2, -h / 2, -t), (w / 2, h / 2, t)))
    for n, at, d in (("back", (0, 0, -t), "-z"), ("+x", (w / 2, 0, 0), "+x"), ("-x", (-w / 2, 0, 0), "-x"), ("+y", (0, h / 2, 0), "+y"), ("-y", (0, -h / 2, 0), "-y")):
        p.sock(n, at, d)
    p.stats(mass, hp, armour, 1.0 if cls is SMALL else None)
    surface = {"source": source, "at": [0, 0, round(t + 0.003, 4)], "size": [round(w - 0.016, 3), round(h - 0.016, 3)], "face": "+z", "up": "+y", "rows": rows}
    if touch:
        surface["touch"] = touch
    p.set(logic={"kind": "instrument"}, panels=[surface])
    return "", p


def row(y0, y1, *buttons):
    n = len(buttons)
    return [{"action": a, "at": [round(0.02 + k * 0.96 / n, 3), y0, round(0.02 + (k + 1) * 0.96 / n - 0.01, 3), y1], "label": label} for k, (a, label) in enumerate(buttons)]


TOUCH = row(0.14, 0.26, ("flight.on", "AP on"), ("flight.off", "AP off"), ("flight.mode", "Mode"), ("flight.home", "Set home")) + row(0.02, 0.13, ("flight.level", "Level"), ("flight.strength", "Strength"), ("flight.altitude", "Hold alt"), ("flight.heading", "Hold hdg"))


def control(name, cls, s, mass, hp, armour, tool):
    """Telemetry over the autopilot's and the stabilizer's buttons (ConstructFlight.FillPanel; PanelTouch.cs)."""
    return box_panel(name, cls, s, mass, hp, armour, tool, "Attitude, height, speed, the autopilot and how it is set, with buttons. For the first stabilizer on the construct. No wiring.", "flight.panel", 8, TOUCH)


def diagnostic(name, cls, s, mass, hp, armour, tool):
    """The stabilizer's view: what it found, from above and from the side (ConstructFlight.FillDiagnostic; PanelRender.FlightPlot)."""
    return box_panel(name, cls, s, mass, hp, armour, tool, "A plot of the craft from above and from the side: an arrow for each thing the stabilizer found, by what it is told; red ones are dead. Names any axis it can't control.", "flight.diagnostic", 6)


def parts():
    return [
        stabilizer("Flight Stabilizer", SMALL, 1, 4, 60, 2, 1500, "wrench"),
        stabilizer("Medium Flight Stabilizer", MEDIUM, 4, 240, 400, 6, 400000, "welder"),
        stabilizer("Large Flight Stabilizer", LARGE, 16, 12000, 2000, 12, 3e7, "welder"),
        stabilizer("Capital Flight Stabilizer", CAPITAL, 64, 500000, 12000, 30, 2e9, "welder"),
        control("Flight Control Panel", SMALL, 1, 3, 30, 0, "wrench"),
        control("Medium Flight Control Panel", MEDIUM, 4, 30, 120, 2, "welder"),
        control("Large Flight Control Panel", LARGE, 16, 300, 400, 4, "welder"),
        control("Capital Flight Control Panel", CAPITAL, 64, 2000, 1600, 8, "welder"),
        diagnostic("Flight Diagnostic Panel", SMALL, 1, 3, 30, 0, "wrench"),
        diagnostic("Medium Flight Diagnostic Panel", MEDIUM, 4, 30, 120, 2, "welder"),
        diagnostic("Large Flight Diagnostic Panel", LARGE, 16, 300, 400, 4, "welder"),
    ]


def data(a, b, out=None, to=None):
    link = {"type": "data", "from": a, "to": b}
    if out:
        link["fromPort"] = out
    if to:
        link["toPort"] = to
    return link


def quadcopter():
    """Tanks in a cross, a jet under each corner tank, a seat on the middle one. W leans it on, Q and E sideways, A and D turn it, Shift and Ctrl climb and sink."""
    steps = [
        {"part": "Fuel Tank"},  # 0: the middle
        {"part": "Fuel Tank", "on": 0, "socket": "-z", "by": "+z"},  # 1: the front
        {"part": "Fuel Tank", "on": 0, "socket": "+z", "by": "-z"},  # 2: the back
        {"part": "Fuel Tank", "on": 1, "socket": "-x", "by": "+x"},  # 3: front left
        {"part": "Fuel Tank", "on": 1, "socket": "+x", "by": "-x"},  # 4: front right
        {"part": "Fuel Tank", "on": 2, "socket": "-x", "by": "+x"},  # 5: rear left
        {"part": "Fuel Tank", "on": 2, "socket": "+x", "by": "-x"},  # 6: rear right
        {"part": "Light Jet", "on": 3, "socket": "-y", "by": "front"},
        {"part": "Light Jet", "on": 4, "socket": "-y", "by": "front"},
        {"part": "Light Jet", "on": 5, "socket": "-y", "by": "front"},
        {"part": "Light Jet", "on": 6, "socket": "-y", "by": "front"},
        {"part": "Seat", "on": 0, "socket": "+y", "by": "bottom"},  # 11
        {"part": "Flight Stabilizer", "on": 2, "socket": "+y", "by": "-y"},  # 12
        {"part": "Drone Battery", "on": 6, "socket": "+y", "by": "-y"},  # 13
        {"part": "Flight Control Panel", "on": 1, "socket": "+y", "by": "-y"},  # 14
    ]
    links = []
    return {"format": "call-of-slop-construct-blueprint/1", "name": "Quadcopter",
            "description": "Four jets and a Flight Stabilizer. Shift lifts off and climbs, Ctrl sinks and lands, W and S lean it on, Q and E sideways, A and D turn. The panel's AP buttons hold what you leave it at.",
            "steps": steps, "links": links}


def stabilised_airship():
    """The Scout Airship with a stabilizer: her engines answer the steering, and a jet under her holds the height."""
    b = airship("Stabilised Airship", "The Scout Airship with a Flight Stabilizer: W and S drive her, A and D turn her by her engines, Shift and Ctrl climb and sink on a lift jet. The panel's AP buttons fly her.",
                "Lift Cell", (10, 12, 24), "helium", FILL, masts=2)
    steps = b["steps"]
    steps.append({"part": "Flight Stabilizer", "on": 3, "socket": "+y", "by": "-y"})
    steps.append({"part": "Square Beam 2 m", "on": 3, "socket": "+z", "by": "-z"})
    lift = len(steps)
    steps.append({"part": "Light Jet", "on": lift - 1, "socket": "-y7", "by": "front"})
    steps.append({"part": "Drone Battery", "on": 5, "socket": "+z", "by": "-y"})
    steps.append({"part": "Fuel Tank", "on": 0, "socket": "-z", "by": "+z"})
    steps.append({"part": "Flight Control Panel", "on": len(steps) - 1, "socket": "+y", "by": "-y"})
    b["links"] += [{"type": "pipe", "from": 0, "to": lift, "fromPort": "Fuel out", "toPort": "Fuel in"}]
    return b


def blueprints():
    code = 0
    for file, b in (("Quadcopter", quadcopter()), ("StabilisedAirship", stabilised_airship())):
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
