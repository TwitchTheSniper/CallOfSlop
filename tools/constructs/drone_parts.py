#!/usr/bin/env python3
"""Writes the wireless seat link's and the cameras' parts (Constructs/Parts/Remote), and the Scout Drone and Control Post blueprints.

docs/sandbox/CONSTRUCTS.md, "Drones" and "Cameras and screens". A Drone Receiver has the seat role with nobody in it, so a drone
needs no seat. A Remote Console is a seat that sends; a Remote Transmitter lets any seat send. A Camera and a Camera Screen meet
on a channel.

Run it from the repo root: python3 -I tools/constructs/drone_parts.py (--check only compares).
Then python3 -I tools/constructs/assembling.py, for the recipes. Fix a part here, never in its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, Part, block_sockets, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Remote")
SECTION = "Remote"
CASE, DARK, GLASS, LAMP, MAST = "#3f4c5a", "#23272c", "#1b2a33", "#39c46a", "#8a939c"
CHANNEL = {"key": "channel", "label": "Channel", "group": "General", "min": 1, "max": 99, "step": 1, "default": 1,
           "tooltip": "Parts on the same channel talk: a sender and a receiver, a camera and a screen."}


def cube(p, half, h):
    p.add(box((-half, 0, -half), (half, h, half)))
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("+x", (half, h / 2, 0), "+x")
    p.sock("-x", (-half, h / 2, 0), "-x")
    p.sock("back", (0, h / 2, half), "+z")


def receiver():
    p = Part("Drone Receiver", SMALL, SECTION, "The seat of a thing with no seat. Link it into a Driving or Turret Module: a console or transmitter on its channel then drives it.", role="seat", tool="wrench", group="Logic", color=CASE)
    cube(p, 0.12, 0.1)
    p.add(box((-0.01, 0.1, -0.01), (0.01, 0.3, 0.01), MAST, detail=True))
    p.add(box((-0.05, 0.1, -0.1), (0.05, 0.12, -0.08), LAMP, detail=True))
    p.stats(3, 60, 2, 1)
    p.set(power=30, seat={"at": [0, 0, 0]}, remote={"receiver": True}, config=[CHANNEL])
    return p


def transmitter(name, cls, rng, power, mass, half, h, mast):
    p = Part(name, cls, SECTION, f"Wire a seat into it: the seat's controls go out on its channel, up to {rng / 1000:g} km.", role="logic", tool="wrench" if mass <= 40 else "welder", group="Logic", color=CASE)
    cube(p, half, h)
    p.add(box((-0.02, h, -0.02), (0.02, h + mast, 0.02), MAST, detail=True))
    p.stats(mass, 80, 2, 1.5)
    p.set(power=power, logic={"kind": "remotelink"}, remote={"range": rng}, config=[CHANNEL])
    return p


def console():
    p = Part("Remote Console", SMALL, SECTION, "A seat with a screen and a transmitter. Sit in it to fly what is on its channel, up to 800 m, seeing through its camera.", role="seat", tool="wrench", group="Logic", color=CASE)
    p.add(box((-0.25, 0, -0.25), (0.25, 0.45, 0.3)))
    p.add(box((-0.25, 0.45, 0.2), (0.25, 1.05, 0.3)))
    p.add(box((-0.4, 0, -0.75), (0.4, 0.7, -0.5), DARK))
    p.add(box((-0.42, 0.7, -0.78), (0.42, 1.2, -0.72), DARK))
    p.sock("bottom", (0, 0, 0), "-y")
    p.sock("back", (0, 0.5, 0.3), "+z")
    p.sock("+x", (0.25, 0.15, 0), "+x")
    p.sock("-x", (-0.25, 0.15, 0), "-x")
    p.stats(70, 200, 3, 3)
    p.set(power=120, seat={"at": [0, -0.55, 0]}, remote={"range": 800}, config=[CHANNEL],
          panels=[{"source": "camera", "at": [0, 0.95, -0.716], "size": [0.74, 0.42], "face": "+z", "up": "+y", "rows": 3, "range": 8}])
    return p


def camera(name, cls, half, deep, fov, desc):
    p = Part(name, cls, SECTION, desc, tool="wrench" if cls is SMALL else "welder", group="Logic", color=DARK, role="panel")
    p.add(box((-half, -half, -deep), (half, half, deep)))
    p.add(box((-half * 0.6, -half * 0.6, -deep - half * 0.5), (half * 0.6, half * 0.6, -deep), GLASS, detail=True))
    p.sock("back", (0, 0, deep), "+z")
    p.sock("-y", (0, -half, 0), "-y")
    p.sock("+y", (0, half, 0), "+y")
    p.sock("+x", (half, 0, 0), "+x")
    p.sock("-x", (-half, 0, 0), "-x")
    p.stats(1.5 if cls is SMALL else 30, 40 if cls is SMALL else 200, 1, 1)
    p.set(camera={"at": [0, 0, round(-deep - half * 0.5, 3)], "fov": fov}, config=[CHANNEL], centreSockets=False)
    return p


def screen(name, cls, w, h, rng, power, mass):
    p = Part(name, cls, SECTION, f"Shows the camera on its channel, while someone stands within {rng:g} m and looks at it.", role="panel", tool="wrench" if mass <= 40 else "welder", group="Logic", color=DARK)
    t = 0.04 if cls is SMALL else 0.12
    p.add(box((-w / 2, -h / 2, 0), (w / 2, h / 2, t)))
    p.sock("back", (0, 0, t), "+z")
    p.sock("-y", (0, -h / 2, t / 2), "-y")
    p.sock("+y", (0, h / 2, t / 2), "+y")
    p.stats(mass, 50, 1, 1)
    p.set(power=power, config=[CHANNEL], centreSockets=False,
          panels=[{"source": "camera", "at": [0, 0, round(-0.004 * (1 if cls is SMALL else 2), 3)], "size": [round(w * 0.97, 3), round(h * 0.95, 3)], "face": "-z", "up": "+y", "rows": 3, "range": rng}])
    return p


def rotor(name, force, power, mass, r):
    p = Part(name, SMALL, SECTION, f"A small electric rotor: {force:g} N, pushing away from its nozzle. Light enough for a drone's frame.", role="thruster", tool="wrench", group="Logic", color=CASE)
    p.add(box((-r, -r, -0.06), (r, r, 0.06)))
    p.add(box((-r * 1.3, -r * 1.3, 0.03), (r * 1.3, r * 1.3, 0.08), DARK, detail=True))
    p.sock("front", (0, 0, -0.06), "-z")
    p.sock("+x", (r, 0, 0), "+x")
    p.sock("-x", (-r, 0, 0), "-x")
    p.stats(mass, 40, 1, 1)
    p.set(thruster={"force": force, "spool": 0.15, "exhaust": "+z", "at": [0, 0, 0.06], "fuel": "electric", "power": power})
    return p


def frame():
    """One piece: a cross a metre across, rotor sockets under its arms and two at the back, and the rest on top."""
    p = Part("Drone Frame", SMALL, SECTION, "A light cross for a drone: rotors under its arms, two pushers at its back, the rest on top.", tool="wrench", group="Logic", color=CASE)
    t, h = 0.05, 0.05
    p.add(box((-0.5, -h, -t), (0.5, h, t)))
    p.add(box((-t, -h, -0.5), (t, h, -t), CASE))
    p.add(box((-t, -h, t), (t, h, 0.5), CASE))
    p.add(box((-0.1, -h, -0.1), (0.1, 0.06, 0.1), DARK, detail=True))
    for name, at in (("r0", (-0.45, 0, 0)), ("r1", (0.45, 0, 0)), ("r2", (0, 0, -0.45)), ("r3", (0, 0, 0.45))):
        p.sock(name, (at[0], -h, at[2]), "-y")
    p.sock("nose", (0, 0, -0.5), "-z")
    p.sock("pushL", (-0.45, 0, t), "+z")
    p.sock("pushR", (0.45, 0, t), "+z")
    p.sock("top", (0, h, 0), "+y")
    p.sock("topF", (0, h, -0.3), "+y")
    p.sock("topB", (0, h, 0.3), "+y")
    p.stats(8, 120, 1, 1.5)
    p.set(centreSockets=False)
    return p


def scout():
    """Four rotors to lift it, two pushers at the back to drive and turn it. W, A, S, D and the hover hold work them by how they are mounted."""
    steps = [{"part": "Drone Frame"}]

    def add(part, socket, by):
        steps.append({"part": part, "on": 0, "socket": socket, "by": by})
        return len(steps) - 1

    fans = [add("Drone Rotor", s, "front") for s in ("r0", "r1", "r2", "r3")]
    fans += [add("Drone Pusher", s, "front") for s in ("pushL", "pushR")]
    add("Drone Battery", "top", "-y")
    rx = add("Drone Receiver", "topF", "bottom")
    module = add("Driving Module", "topB", "-y")
    add("Camera", "nose", "back")
    links = [{"type": "data", "from": rx, "to": module}]
    links += [{"type": "data", "from": module, "to": fan, "toPort": "drive"} for fan in fans]
    return {
        "format": "call-of-slop-construct-blueprint/1",
        "name": "Scout Drone",
        "description": "Rotors, a battery, a receiver and a camera, no seat. Fly it from a Remote Console on channel 1: Sprint lifts it into its hover hold.",
        "steps": steps,
        "links": links,
    }


def battery():
    p = Part("Drone Battery", SMALL, SECTION, "A small battery, a few minutes' flight.", role="battery", tool="wrench", group="Logic", color="#2f3a33")
    p.add(box((-0.1, 0, -0.08), (0.1, 0.08, 0.08)))
    p.sock("-y", (0, 0, 0), "-y")
    p.sock("+y", (0, 0.08, 0), "+y")
    p.stats(5, 60, 1, 1)
    p.set(battery={"capacity": 400})
    return p


def control_post():
    return {
        "format": "call-of-slop-construct-blueprint/1",
        "name": "Control Post",
        "description": "A Remote Console on a slab with a battery: sit in it to fly the drone on channel 1. A Camera Screen beside it shows its camera to anyone near.",
        "steps": [
            {"part": "Foundation"},
            {"part": "Remote Console", "on": 0, "socket": "top11", "by": "bottom"},
            {"part": "Battery", "on": 0, "socket": "top33", "by": "-y"},
        ],
    }


def main():
    parts = [("", receiver()), ("", console())]
    parts.append(("", transmitter("Remote Transmitter", SMALL, 800, 60, 6, 0.12, 0.2, 0.5)))
    parts.append(("", transmitter("Medium Remote Transmitter", MEDIUM, 3000, 300, 120, 0.5, 1, 2)))
    parts.append(("", frame()))
    parts.append(("", rotor("Drone Rotor", 200, 0.6, 2.5, 0.08)))
    parts.append(("", rotor("Drone Pusher", 60, 0.2, 1.2, 0.06)))
    parts.append(("", battery()))
    parts.append(("", camera("Camera", SMALL, 0.06, 0.08, 70, "A small camera. It films along its lens for a screen on its channel, and does nothing until a screen near a player asks.")))
    parts.append(("", camera("Medium Camera", MEDIUM, 0.5, 0.5, 60, "A big camera for a big construct.")))
    parts.append(("", screen("Camera Screen", SMALL, 0.5, 0.3, 8, 20, 3)))
    parts.append(("", screen("Large Camera Screen", SMALL, 1, 0.6, 10, 40, 8)))
    parts.append(("", screen("Medium Camera Screen", MEDIUM, 4, 2, 25, 200, 90)))
    code = write_parts(OUT, parts)
    for name, data in (("ScoutDrone.json", scout()), ("ControlPost.json", control_post())):
        path = os.path.join("Constructs", "Blueprints", name)
        text = json.dumps(data, indent=2) + "\n"
        same = os.path.exists(path) and open(path, encoding="utf-8").read() == text
        if "--check" in sys.argv:
            code |= 0 if same else 1
        elif not same:
            with open(path, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)
    return code


if __name__ == "__main__":
    sys.exit(main())
