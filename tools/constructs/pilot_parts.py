#!/usr/bin/env python3
"""Writes what a pilot wears and sits in: Constructs/Parts/Pilot, and the carrier demo. docs/sandbox/CONSTRUCTS.md, "Carrier work", "Pilot safety".

Run it from the repo root: python3 -I tools/constructs/pilot_parts.py (--check only compares), then python3 -I tools/constructs/assembling.py for
their recipes. Fix this file, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Pilot")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")
FORMAT = "call-of-slop-construct-blueprint/1"
SAVE = "#4f6b3c"
PACK = 0.095  # half a pack: it fits a backpack's small pouch


def worn(name, wear, desc, mass, color, trim):
    p = Part(name, SMALL, "Pilot gear", desc, role="panel", tool="wrench", color=color, group="Flight")
    p.add(box((-PACK, 0, -PACK), (PACK, PACK * 1.6, PACK)))
    p.add(box((-PACK * 0.6, PACK * 1.6, -PACK * 0.6), (PACK * 0.6, PACK * 1.85, PACK * 0.6), trim, detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.stats(mass, 60, 1, 1.0)
    p.set(wear=wear)
    return p


def eject_seat():
    p = Part("Ejection Seat", SMALL, "Seats and helms", "A seat with a handle: the key or a signal throws its rider clear on a parachute.", role="seat", tool="wrench", color="#3a3f46", group="Seats")
    p.add(box((-0.25, 0, -0.3), (0.25, 0.25, 0.3)))
    p.add(box((-0.25, 0.25, 0.15), (0.25, 1.05, 0.3), "#2f343a", use=True))
    p.add(box((-0.1, 1.05, 0.18), (0.1, 1.15, 0.27), "#d8b020", detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.stats(40, 120, 3, 1.5)
    p.set(seat={"at": [0, 0.25, 0], "eject": True})
    return p


def parts():
    return [
        ("Gear", worn("Parachute", "parachute", "In a backpack: jump while falling opens it, jump again cuts it away; it comes off on landing.", 4, SAVE, "#c8c0a8")),
        ("Gear", worn("Backpack Glider", "glider", "In a backpack: jump while falling opens it. W dives, S flares, A and D turn.", 6, "#35608a", "#d8d8d8")),
        ("Gear", worn("Jetpack", "jetpack", "In a backpack: hold jump for four seconds of flight. The ground refills it.", 9, "#5c6066", "#c8642a")),
        ("Seats", eject_seat()),
    ]


def deck():
    """Catapult and arrestor in line, flush, a row of platforms each side: land over the stern, stop, roll on, launch off the bow."""
    s = [{"part": "Catapult Medium 16 m"}]

    def add(part, on, socket, by, **kw):
        s.append(dict(part=part, on=on, socket=socket, by=by, **kw))
        return len(s) - 1

    arrest = add("Arrestor Gear Medium 16 m", 0, "+z", "-z")
    pad = "Lift Platform Medium 5x5"

    def row(first_on, socket, by):
        """Eight platforms from z -15 to 20: the keyed one sits at z 0. Their z -> index."""
        at = {0: add(pad, first_on, socket, by)}
        for z in (-5, -10, -15):
            at[z] = add(pad, at[z + 5], "-z", "+z")
        for z in (5, 10, 15, 20):
            at[z] = add(pad, at[z - 5], "+z", "-z")
        return at

    left = row(0, "-x", "+x")
    right = row(0, "+x", "-x")
    outer = {z: add(pad, right[z], "+x", "-x") for z in (5, 10, 15, 20)}  # (the hangar's floor)
    # Everything that stands on the deck stands at its edges: a plane's wings pass over the rest. Four jets up, four across at the ends for the turn.
    up = [add("Medium Turbojet", left[-15], "-y", "front"), add("Medium Turbojet", left[20], "-y", "front"), add("Medium Turbojet", right[-15], "-y", "front"), add("Medium Turbojet", outer[20], "-y", "front")]
    across = [add("Medium Turbojet", left[-15], "-x", "front"), add("Medium Turbojet", left[20], "-x", "front"), add("Medium Turbojet", right[-10], "+x", "front"), add("Medium Turbojet", outer[15], "+x", "front")]
    tanks = [add("Medium Fuel Bunker", left[-10], "-x", "+x"), add("Medium Fuel Bunker", left[15], "-x", "+x"), add("Medium Fuel Bunker", right[-15], "+x", "-x"), add("Medium Fuel Bunker", outer[20], "+x", "-x")]
    stab = add("Medium Flight Stabilizer", left[-5], "+y.0+", "-y", config={"keys": "Rotorcraft", "maxTilt": 6})
    small = add("Adapter Medium to Small", left[-5], "+y.0-", "back")
    add("Bridge Helm", small, "front0.0", "-y")
    add("Docking Clamp Medium", left[5], "+y.0-", "-y")
    add("Mooring Mast Medium", outer[20], "+y.1+", "-y")
    add("Hangar Bay Medium", outer[5], "+y.0-", "-y", config={"sizeX": 8, "sizeY": 5, "sizeZ": 15, "atX": 0, "atY": 2.5, "atZ": 9.5})
    links = [{"type": "pipe", "from": tanks[k], "to": j, "fromPort": "Fuel out", "toPort": "Fuel in"} for k, j in enumerate(up)]
    links += [{"type": "pipe", "from": tanks[k], "to": j, "fromPort": "Fuel out", "toPort": "Fuel in"} for k, j in enumerate(across)]
    return {"format": FORMAT, "name": "Carrier Deck", "description": "A flying deck. Land over the stern, the arrestor stops you, roll onto the catapult, G hooks, G flings. F arms the clamp; park in the bay.", "steps": s, "links": links}


def deck_plane():
    from wing_parts import light_plane

    b = light_plane()
    b["name"] = "Deck Plane"
    b["description"] = "A Light Plane with an ejection seat, for the Carrier Deck. Shift and Ctrl set the throttle, S lifts the nose, G hooks and launches, L ejects."
    for step in b["steps"]:
        if step["part"] == "Seat":
            step["part"] = "Ejection Seat"
    return b


def blueprints():
    code = 0
    for file, b in (("CarrierDeck", deck()), ("DeckPlane", deck_plane())):
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
