#!/usr/bin/env python3
"""Writes the air cargo parts and blueprints: Constructs/Parts/AirCargo, and the Sky Crane, its pods and the crashed aircraft a world
is salvaged for. docs/sandbox/CONSTRUCTS.md, "Flight in survival".

What a craft lifts is any body with a Tow Eye, on a Cargo Winch: the only new part. The rest are blueprints of parts that exist. Run
it from the repo root: python3 -I tools/constructs/air_cargo.py (--check only compares), then python3 -I tools/constructs/assembling.py
for the winch's recipe. The JSON files are what the game loads: fix this, not them.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "AirCargo")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")
FORMAT = "call-of-slop-construct-blueprint/1"
DARK, GOLD = "#30343a", "#c8a13a"
WING_LEFT, WING_RIGHT, FIN = 0, 0, 1  # (the turn about a socket that keeps a piece's lift side up, and a fin's to the side)


def cargo_winch():
    h = 0.2
    p = Part("Cargo Winch 16 m", SMALL, "Air cargo", "A winch a pilot works from the seat: grab takes the nearest eye within its 16 m, or lets go; reel takes it in, pay out lets it down. Lifts 3 t. Needs power.", role="hitch", tool="wrench", color=DARK, group="Vehicles")
    p.add(box((-h, -h, -h), (h, h, h)))
    p.add(box((-0.05, -0.27, -0.05), (0.05, -h, 0.05), GOLD, detail=True))
    p.sock("+y", (0, h, 0), "+y")
    p.sock("+x", (h, 0, 0), "+x")
    p.sock("-x", (-h, 0, 0), "-x")
    p.sock("+z", (0, 0, h), "+z")
    p.sock("-z", (0, 0, -h), "-z")
    p.stats(35, 200, 6, 3)
    p.set(hitch={"kind": "hook", "at": [0, -0.27, 0], "chain": 16, "winch": {"min": 1, "speed": 3, "rating": 54000, "grab": True}}, power=800)
    return p


def blueprint(name, description, steps, links=(), loot=()):
    d = {"format": FORMAT, "name": name, "description": description, "steps": steps, "links": list(links)}
    if loot:
        d["loot"] = list(loot)
    return d


FAN = "Extrusion fan3.straight.6 0x200x0 metal"  # (a 2 m ducted fan off a Fan Former)


def sky_crane():
    """A hub with four arms, every weight paired across it and a Cargo Winch under its middle, where a load hangs through the centre of mass. The stabilizer finds its fans by itself."""
    s = []

    def add(**kw):
        s.append(kw)
        return len(s) - 1

    hub = add(part="Frame Block")
    arms = [add(part="Square Beam 2 m", on=hub, socket=side, by="-z") for side in ("+x", "-x", "+z", "-z")]
    for arm, hand in zip(arms, (1, 1, 0, 0)):
        add(part=FAN, on=arm, socket="+y7", by="base", config={"hand": hand})
        add(part="Square Beam 1 m", on=arm, socket="-y6", by="+z")  # (a leg: it stands on these, with its winch clear of the ground)
    seat = add(part="Seat", on=hub, socket="+y", by="bottom")
    add(part="Battery Pack", on=arms[2], socket="-y2", by="+y")
    pack = add(part="Battery Pack", on=arms[3], socket="-y2", by="+y")
    add(part="Flight Stabilizer", on=arms[0], socket="+y2", by="-y", config={"keys": "Rotorcraft", "apMode": "Hover"})
    add(part="Flight Control Panel", on=pack, socket="+x", by="back")
    winch = add(part="Cargo Winch 16 m", on=hub, socket="-y", by="+y")
    links = []
    for key, sock, port in (("fire", "-y2", "grab"), ("aim", "-y3", "reel"), ("reload", "-y4", "pay out")):
        reader = add(part="Input Reader", on=arms[0], socket=sock, by="+y", config={"setting": key})
        links.append({"type": "data", "from": seat, "to": reader})
        links.append({"type": "data", "from": reader, "to": winch, "toPort": port})
    return blueprint("Sky Crane", "Four ducted fans on a hub, a Cargo Winch under it. Hover over a Tow Eye with the panel's autopilot: fire grabs or lets go, aim reels in, reload lets out. Shift lifts off, Ctrl lands.", s, links)


def cargo_pod():
    """A deck and a frozen cargo volume under a Tow Eye: what stands in the box flies with it."""
    s = [{"part": "Frame Block"}]
    s.append({"part": "Tow Eye", "on": 0, "socket": "+y", "by": "bottom"})
    s.append({"part": "Cargo Volume", "on": 0, "socket": "-y", "by": "+y", "config": {"startOn": 1, "sizeX": 2, "sizeY": 1.5, "sizeZ": 2, "atY": -1.0}})
    for side in ("+x", "-x", "+z", "-z"):
        s.append({"part": "Square Beam 1 m", "on": 0, "socket": side, "by": "-z"})
    return blueprint("Cargo Pod", "A Cargo Volume under a Tow Eye: load it on the ground, freeze it, and a Sky Crane carries what is in it.", s)


def drill_pod():
    """A Drill Rig with a battery and a Tow Eye: lowered on a winch it sits on a deposit and fills its bin."""
    s = [{"part": "Drill Module"},
         {"part": "Battery", "on": 0, "socket": "-x", "by": "+x"},
         {"part": "Materials Bin", "on": 0, "socket": "+x", "by": "-x"},
         {"part": "Tow Eye", "on": 0, "socket": "back", "by": "bottom", "spin": 0}]
    return blueprint("Drill Pod", "A Drill Module with a battery and a bin, on a Tow Eye: a Sky Crane lowers it onto a deposit, waits, and lifts it out full.", s,
                     [{"type": "chute", "from": 0, "to": 2, "fromPort": "Chute out"}])


def crashed_plane():
    """What a light aircraft leaves: wings and a tail still on, its Chest of spares thrown open by nobody."""
    s = [{"part": "Frame Block"},
         {"part": "Wing Segment", "on": 0, "socket": "+x", "by": "-x"},
         {"part": "Fabric Wing Segment", "on": 1, "socket": "+x", "by": "-x"},
         {"part": "Square Beam 2 m", "on": 0, "socket": "+z", "by": "-z"},
         {"part": "Tail Plane", "on": 3, "socket": "+z", "by": "front"},
         {"part": "Tail Fin", "on": 4, "socket": "+y", "by": "-y"},
         {"part": "Rudder", "on": 5, "socket": "back", "by": "hinge"},
         {"part": "Square Beam 1 m", "on": 0, "socket": "-z", "by": "-z"},
         {"part": "Two-Blade Prop", "on": 7, "socket": "+z", "by": "base"},
         {"part": "Chest", "on": 0, "socket": "+y", "by": "bottom"}]
    return blueprint("Crashed Plane", "A light plane that came down: salvage its wings and tail, and see what its Chest holds.", s, loot=[{"step": 9, "table": "aircraft"}])


def crashed_jet():
    """A fast one: a jet on a keel, one swept wing left, a Chest of avionics."""
    s = [{"part": "Frame Block"},
         {"part": "Light Turbojet", "on": 0, "socket": "+z", "by": "front"},
         {"part": "Swept Wing L", "on": 0, "socket": "-x", "by": "+x"},
         {"part": "Aileron", "on": 2, "socket": "+y", "by": "hinge"},
         {"part": "Chest", "on": 0, "socket": "+y", "by": "bottom"}]
    return blueprint("Crashed Jet", "A jet that came down: its turbojet may be worth more than the Chest it carried.", s, loot=[{"step": 4, "table": "aircraft"}])


WING = "Extrusion wing-wing 150x0x400 cloth"


def fabric_flyer():
    """The survival plane: what a Workshop-tier base makes. Cloth off an Airframe Former, a wooden prop off a Fan Former, an Engine; a left piece welds by its base, a right one by its tip."""
    s = []

    def add(**kw):
        s.append(kw)
        return len(s) - 1

    root = add(part="Frame Block")
    engine = add(part="Engine", on=root, socket="-z", by="-z")
    prop = add(part="Extrusion fan0.straight.3 0x150x0 wood", on=engine, socket="+z", by="base")
    seat = add(part="Seat", on=root, socket="+y", by="bottom")
    tank = add(part="Fuel Tank", on=root, socket="+z", by="-z")
    boom = add(part="Square Beam 2 m", on=tank, socket="+z", by="-z")
    end = add(part="Frame Block", on=boom, socket="+z", by="-z")
    left = add(part=WING, on=tank, socket="-x", by="base", spin=WING_LEFT)
    right = add(part=WING, on=tank, socket="+x", by="tip", spin=WING_RIGHT)
    add(part="Battery", on=tank, socket="-y", by="+y")
    ail_l = add(part="Extrusion wing-aileron 50x0x200 cloth", on=left, socket="+x2", by="-x0", spin=0)
    ail_r = add(part="Extrusion wing-aileron 50x0x200 cloth", on=right, socket="+x1", by="-x0", spin=0)
    tail_l = add(part="Extrusion wing-tail 100x0x100 cloth", on=end, socket="-x", by="base", spin=WING_LEFT)
    tail_r = add(part="Extrusion wing-tail 100x0x100 cloth", on=end, socket="+x", by="tip", spin=WING_RIGHT)
    elev_l = add(part="Extrusion wing-elevator 40x0x100 cloth", on=tail_l, socket="+x0", by="-x0", spin=0)
    elev_r = add(part="Extrusion wing-elevator 40x0x100 cloth", on=tail_r, socket="+x0", by="-x0", spin=0)
    fin = add(part="Extrusion wing-fin 100x0x100 cloth", on=end, socket="+y", by="base", spin=FIN)
    rudd = add(part="Extrusion wing-rudder 40x0x100 cloth", on=fin, socket="+x0", by="-x0", spin=0)
    stab = add(part="Flight Stabilizer", on=tank, socket="+y", by="-y", config={"keys": "Plane"})
    leg = add(part="Square Beam 0.5 m", on=root, socket="-y", by="+z")
    axle = add(part="Square Beam 2 m", on=leg, socket="-z", by="+y3", spin=1)
    for side in ("-z", "+z"):
        gear = add(part="Suspension", on=axle, socket=side, by="mount")
        add(part="Wheel", on=gear, socket="axle", by="axle")
    strut = add(part="Square Beam 0.5 m", on=boom, socket="-y3", by="+z")  # (as long as the nose leg: it stands level, so its wheels don't shove it along)
    block = add(part="Frame Block", on=strut, socket="-z", by="+y")
    for face in ("-x", "+x"):
        gear = add(part="Suspension", on=block, socket=face, by="mount")
        add(part="Wheel", on=gear, socket="axle", by="axle")
    links = [
        {"type": "data", "from": seat, "to": stab},
        {"type": "data", "from": stab, "fromPort": "throttle", "to": prop},
        {"type": "data", "from": stab, "fromPort": "pitch", "to": elev_l},
        {"type": "data", "from": stab, "fromPort": "pitch", "to": elev_r},
        {"type": "data", "from": stab, "fromPort": "yaw", "to": rudd},
        {"type": "data", "from": stab, "fromPort": "left aileron", "to": ail_l},
        {"type": "data", "from": stab, "fromPort": "right aileron", "to": ail_r},
        {"type": "pipe", "from": tank, "to": engine, "fromPort": "Fuel out", "toPort": "Fuel in"},
    ]
    return blueprint("Fabric Flyer", "The plane a workshop can make: cloth off an Airframe Former, a wooden prop off a Fan Former, an Engine. Shift and Ctrl set the throttle, S lifts the nose, A and D bank and turn.", s, links)


def write_blueprint(name, d, check):
    path = os.path.join(BLUEPRINTS, name + ".json")
    text = json.dumps(d, indent=2) + "\n"
    if check:
        return 0 if os.path.exists(path) and open(path).read() == text else (print(f"{path} differs") or 1)
    with open(path, "w", newline="\n") as f:
        f.write(text)
    return 0


def main():
    check = "--check" in sys.argv
    code = write_parts(OUT, [("", cargo_winch())])
    for name, d in (("SkyCrane", sky_crane()), ("CargoPod", cargo_pod()), ("DrillPod", drill_pod()), ("CrashedPlane", crashed_plane()), ("CrashedJet", crashed_jet()), ("FabricFlyer", fabric_flyer())):
        code |= write_blueprint(name, d, check)
    return code


if __name__ == "__main__":
    sys.exit(main())
