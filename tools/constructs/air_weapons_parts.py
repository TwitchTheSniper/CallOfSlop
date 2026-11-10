#!/usr/bin/env python3
"""Writes the air weapons: Constructs/Parts/AirWeapons, and the demo craft. docs/sandbox/CONSTRUCTS.md, "Air weapons".

Run it from the repo root: python3 -I tools/constructs/air_weapons_parts.py (--check only compares), then python3 -I tools/constructs/assembling.py
for their recipes. The blueprints are built on Constructs/Blueprints/LightPlane.json and SmallHelicopter.json, so run it again if those change. Fix this file, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "AirWeapons")
BLUEPRINTS = os.path.join("Constructs", "Blueprints")
OLIVE, DARK, BRASS, GLASS = "#4a5530", "#2a2c26", "#c8a838", "#20303a"
GROUP = "Weapons"


def bomb(name, radius_m, length, mass, blast, damage, fuse, crater, desc):
    p = Part(name, SMALL, "Bombs", desc, tool="welder", color=OLIVE, group=GROUP)
    r, z = radius_m, length / 2
    p.add(box((-r, -r, -z), (r, r, z), hidden=True))
    p.add(box((-0.04, r - 0.02, -0.06), (0.04, r + 0.05, 0.06), hidden=True))
    p.add(box((-r * 0.97, -r * 0.97, -z * 0.55), (r * 0.97, r * 0.97, z * 0.55), OLIVE, detail=True, shape="cylinder", segments=16))
    p.add(box((-r * 0.94, -r * 0.94, -z * 0.97), (r * 0.94, r * 0.94, -z * 0.54), OLIVE, detail=True, shape="cylinder", segments=16, taper=[0.3, 0.3]))
    p.add(box((-r, -r, -z * 0.4), (r, r, -z * 0.32), BRASS, detail=True, shape="cylinder", segments=16))
    p.add(box((-r * 0.5, -r * 0.5, z * 0.54), (r * 0.5, r * 0.5, z * 0.92), DARK, detail=True, shape="cylinder", segments=12))
    p.add(box((-0.012, -r * 1.3, z * 0.62), (0.012, r * 1.3, z), DARK, detail=True))
    p.add(box((-r * 1.3, -0.012, z * 0.63), (r * 1.3, 0.012, z * 0.99), DARK, detail=True))
    p.add(box((-0.04, r * 0.96, -0.06), (0.04, r + 0.05, 0.06), DARK, detail=True))
    p.sock("lug", (0, r + 0.05, 0), "+y")
    p.stats(mass, 40, 1, 1.0)
    p.set(explosive={"radius": blast, "damage": damage, "fuse": fuse, "knockback": 12, "crater": crater})
    return "Bombs", p


def rack(name, stations, desc, load=300):
    """Lugs below a hanger: each -y socket takes a bomb's lug. An old fixed mount: the Ordnance Rack does its job, so it loads and works but isn't offered."""
    x = 0.125 * stations
    p = Part(name, SMALL, "Bomb racks", desc + f" Rated for {load * stations:g} kg.", role="logic", tool="welder", color=DARK, group=GROUP)
    p.add(box((-x, -0.04, -0.2), (x, 0.08, 0.2)))
    for k in range(stations):
        at = -x + 0.25 * k + 0.125
        p.add(box((at - 0.05, -0.08, -0.07), (at + 0.05, -0.04, 0.07), BRASS, detail=True))
        p.sock(f"lug{k}", (at, -0.04, 0), "-y")
    p.sock("top", (0, 0.08, 0), "+y")
    p.sock("back", (0, 0, 0.2), "+z")
    p.sock("front", (0, 0, -0.2), "-z")
    p.stats(6 * stations, 80, 3, 1.5)
    p.set(logic={"kind": "bombrack", "load": load * stations}, legacy=True)
    return "Bombs", p


class ModularPreset:
    """An old preset of the Modular Bomb Rack: only its shape. It still loads (saves and blueprints name it) as the Ordnance Rack with the same
    stations in the same places, but isn't offered (Source/Constructs/ModularRack.cs, FromLegacy)."""

    def __init__(self, name, stations, layout, size, desc):
        self.name, self.stations, self.layout, self.size, self.desc = name, stations, layout, size, desc

    def json(self):
        return {"format": "call-of-slop-construct-part/1", "name": self.name, "description": self.desc, "legacy": True,
                "modularRack": {"stations": self.stations, "layout": self.layout, "size": self.size}}


class OrdnanceRack:
    """A preset of the Ordnance Rack: only its shape. The game makes the whole part from it (Source/Constructs/ModularRack.cs), the same way it
    makes a shape the configure tool asks for, so a preset and the rack it is set to are one part. a, b: rows and columns (a drum's layers and
    segments); length and diameter: the largest ordnance a station takes, m."""

    def __init__(self, name, layout, a, b, length, diameter, desc):
        self.name, self.layout, self.a, self.b, self.length, self.diameter, self.desc = name, layout, a, b, length, diameter, desc

    def json(self):
        ka, kb = ("layers", "segments") if self.layout == "drum" else ("rows", "columns")
        return {"format": "call-of-slop-construct-part/1", "name": self.name, "description": self.desc,
                "modularRack": {"layout": self.layout, ka: self.a, kb: self.b, "length": self.length, "diameter": self.diameter}}


# The Ordnance Rack's presets (docs/sandbox/CONSTRUCTS.md, "Ordnance racks"): every shape differs, and the configure tool turns any into any.
# (name, layout, rows or layers, columns or segments, longest ordnance m, widest m, description)
ORDNANCE_RACKS = [
    ("Ordnance Rack", "grid", 1, 1, 2.5, 0.25, "One station for a bomb or a missile, set by the configure tool: a grid or a drum, its stations, and the largest ordnance it takes."),
    ("Twin Ordnance Rack", "grid", 1, 2, 1.0, 0.25, "Two small bombs or missiles side by side."),
    ("Triple Ordnance Rack", "grid", 1, 3, 2.5, 0.25, "Three side by side."),
    ("Ordnance Rack 2x2", "grid", 2, 2, 2.5, 0.25, "Four: two rows of two."),
    ("Ordnance Rack 2x4", "grid", 2, 4, 2.5, 0.25, "Eight: two rows of four."),
    ("Ordnance Rack 4x4", "grid", 4, 4, 1.0, 0.25, "Sixteen small bombs: a bay's worth."),
    ("Heavy Ordnance Rack", "grid", 1, 1, 2.5, 0.625, "One big station, 5 t: what a small rack won't take."),
    ("Giant Ordnance Rack", "grid", 1, 1, 8.0, 2.0, "For the biggest things a designer dares: 2000 t."),
    ("Launch Rack", "grid", 1, 1, 3.0, 0.25, "A long single station, for a missile under a wing."),
    ("Twin Launch Rack", "grid", 1, 2, 3.0, 0.25, "Two long stations side by side: a vehicle's missile launcher."),
    ("Launch Rack 2x2", "grid", 2, 2, 3.0, 0.25, "Four long stations, two rows of two: a missile battery."),
    ("Launch Rack 1x6", "grid", 1, 6, 3.0, 0.25, "Six long stations side by side: a heavy missile battery."),
    ("Missile Pod 2x2", "grid", 2, 2, 1.5, 0.2, "Four short missiles, two by two."),
    ("Light Ordnance Drum", "drum", 1, 6, 2.5, 0.25, "A drum for a bomb bay, six round it, mounted by its yoke or its ends."),
    ("Heavy Ordnance Drum", "drum", 1, 8, 2.5, 0.625, "A heavy drum for a bomb bay, eight round it, mounted by its yoke or its ends."),
    ("Double Ordnance Drum", "drum", 2, 6, 2.5, 0.25, "Two layers of six round one drum."),
]

# The Modular Bomb Rack's old presets: saves and blueprints still name them.
MODULAR = [
    ("Modular Bomb Rack", 1, "row", "light", "A bomb rack set by the configure tool: 1 to 16 stations, in a row, in pairs or on a drum, light, heavy or giant."),
    ("Triple Bomb Rack", 3, "row", "light", "Three bombs side by side."),
    ("Quad Bomb Rack", 4, "row", "light", "Four bombs side by side."),
    ("5-Station Bomb Rack", 5, "row", "light", "Five bombs side by side."),
    ("6-Station Bomb Rack", 6, "pairs", "light", "Six bombs, three pairs."),
    ("7-Station Bomb Rack", 7, "row", "light", "Seven bombs side by side."),
    ("8-Station Bomb Rack", 8, "pairs", "light", "Eight bombs, four pairs."),
    ("Sixteen Bomb Rack", 16, "pairs", "light", "Sixteen bombs, eight pairs: a bay's worth."),
    ("Light Rotary Drum", 6, "drum", "light", "A small drum for a bomb bay, mounted by its spine or its ends."),
    ("Rotary Bomb Drum", 8, "drum", "heavy", "A heavy drum for a bomb bay, mounted by its spine or its ends."),
]


def gun(name, desc, length, half, mass, block, color=DARK, legacy=False):
    """A heavy gun on the shell pool: a breech block and a tube or a box of tubes."""
    p = Part(name, SMALL, "Guns", desc, role="gun", tool="welder", color=color, group=GROUP)
    p.add(box((-half, -half, -half), (half, half, half)))
    p.add(box((-half * 0.45, -half * 0.45, -length), (half * 0.45, half * 0.45, -half), "#3a3d40"))
    p.sock("bottom", (0, -half, 0), "-y")
    p.sock("top", (0, half, 0), "+y")
    p.sock("left", (-half, 0, 0), "-x")
    p.sock("right", (half, 0, 0), "+x")
    p.sock("back", (0, 0, half), "+z")
    p.stats(mass, 240, 6, 3.0)
    g = dict(block)
    g["muzzle"] = [0, 0, -length]
    p.set(gun=g, centreSockets=False)
    if legacy:
        p.set(legacy=True)  # (an old mount: the Ordnance Rack launches designed missiles; it loads and works but isn't offered)
    return "Guns", p


def sight(name, kind, reach, cone, desc, config=None):
    p = Part(name, SMALL, "Sights", desc, tool="wrench", color=DARK, group=GROUP)
    p.add(box((-0.08, 0, -0.14), (0.08, 0.12, 0.14)))
    p.add(box((-0.05, 0.03, -0.155), (0.05, 0.09, -0.135), GLASS, detail=True))
    p.sock("bottom", (0, 0, 0), "-y")
    p.stats(5, 50, 1, 1.0)
    p.set(sight={"kind": kind, "reach": reach, "cone": cone})
    if config:
        p.set(config=config)
    return "Sights", p


def parts():
    fuse = {"key": "fuseRange", "label": "Fuse range", "group": "Gun", "unit": "m", "min": 0, "max": 1500, "step": 25, "default": 0, "tooltip": "With nothing locked, shells burst this far out. 0: only near what flies, or on what they hit."}
    return [
        bomb("Light Bomb", 0.11, 1.0, 40, 7, 120, 0.5, 2.5, "40 kg: 7 m blast half a second after a hit. Hang it on a Bomb Rack."),
        rack("Bomb Rack", 1, "Holds a bomb on its lug. While its input is on it lets go, three times a second, if its own construct leaves the way down clear: a closed bay door stops it."),
        rack("Twin Bomb Rack", 2, "Two bombs side by side. While its input is on it lets them go in turn, if its own construct leaves the way down clear."),
        rack("Heavy Bomb Rack", 1, "A strong single hook for what a small rack won't take. While its input is on it lets go, if its own construct leaves the way down clear.", load=5000),
        rack("Giant Bomb Rack", 1, "For the biggest things a designer dares. While its input is on it lets go, if its own construct leaves the way down clear.", load=2000000),
        gun("AA Gun", "Flak: its shells burst near what flies. An AA Sight on it leads what the gunner looks at.", 1.3, 0.22, 90, {"damage": 80, "rpm": 90, "spread": 0.4, "range": 900, "maxRange": 1000, "mag": 8, "reload": 3, "kick": 200, "fx": "cannon", "soundOf": "Leviathan .50", "shell": "flak", "speed": 340, "prox": 4, "blast": 12, "blastDamage": 170}),
        gun("Rocket Pod Mk2", "Seven rockets that push themselves: 1.5 s of burn, straight at what the gun points at.", 0.5, 0.2, 45, {"damage": 90, "rpm": 180, "spread": 1.2, "range": 500, "maxRange": 1000, "mag": 7, "reload": 8, "kick": 30, "fx": "rocket", "soundOf": "Leviathan .50", "shell": "rocket", "speed": 40, "thrust": 220, "burn": 1.5, "blast": 5, "blastDamage": 160, "crater": 1.2}),
        gun("Missile Rail", "A guided missile: it locks what the gunner looks at, or what a Seeker Head sees, and turns after it. Flares draw it.", 0.7, 0.14, 60, {"damage": 200, "rpm": 30, "spread": 0, "range": 900, "maxRange": 1000, "mag": 2, "reload": 10, "kick": 20, "fx": "rocket", "soundOf": "Leviathan .50", "shell": "missile", "speed": 30, "thrust": 160, "burn": 3, "turn": 70, "prox": 5, "blast": 9, "blastDamage": 320}, legacy=True),
        gun("Flare Dispenser", "Throws flares that burn five seconds, sinking. Missiles coming in may go for one instead.", 0.3, 0.12, 15, {"damage": 1, "rpm": 180, "spread": 8, "range": 30, "maxRange": 60, "mag": 12, "reload": 6, "kick": 2, "fx": "cannon", "soundOf": "Leviathan .50", "shell": "flare", "speed": 30, "burn": 5}),
        sight("AA Sight", "lead", 1200, 8, "On a gun: the gun leads the body its gunner looks at, and its shells burst on the way there.", [fuse]),
        sight("Seeker Head", "seeker", 1000, 20, "On a Missile Rail: the missile locks the flying body nearest straight ahead, with or without a gunner."),
    ] + [("Bombs", ModularPreset(*m)) for m in MODULAR] + [("Racks", OrdnanceRack(*m)) for m in ORDNANCE_RACKS]


def load(name):
    with open(os.path.join(BLUEPRINTS, name), encoding="utf-8") as f:
        return json.load(f)


def data(a, b, port=None):
    d = {"type": "data", "from": a, "to": b}
    if port:
        d["fromPort"] = port
    return d


def light_bomber():
    """The Light Plane with a twin rack under its tail boom's root; the pilot's fire key lets go."""
    b = load("LightPlane.json")
    s, links = b["steps"], b["links"]

    def add(part, on, socket, by, **kw):
        s.append(dict(part=part, on=on, socket=socket, by=by, **kw))
        return len(s) - 1

    boom, stab = 5, 17
    rack_at = add("Twin Ordnance Rack", boom, "-y2", "top")
    add("Light Bomb", rack_at, "lug0", "lug")
    add("Light Bomb", rack_at, "lug1", "lug")
    links.append(data(stab, rack_at, "fire"))
    return {"format": b["format"], "name": "Light Bomber", "description": "The Light Plane with two Light Bombs under its boom. The fire key lets them go; a cross shows where the next lands.", "steps": s, "links": links}


def flak_gun():
    b = load("GunnerTurret.json")
    s = b["steps"]
    s[7] = {"part": "AA Gun", "on": 6, "socket": "leaf", "by": "bottom"}
    s.append({"part": "AA Sight", "on": 7, "socket": "top", "by": "bottom"})
    return {"format": b["format"], "name": "Flak Gun", "description": "A seated AA Gun with an AA Sight. Look at what flies: the gun leads it, and its shells burst beside it.", "steps": s, "wires": b["wires"]}


def gunship():
    """The Small Helicopter with a gunner on its right skid block and a stabilised gun under them. The gunner's Aim key throws flares."""
    b = load("SmallHelicopter.json")
    s, links = b["steps"], b["links"]

    def add(part, on, socket, by, **kw):
        s.append(dict(part=part, on=on, socket=socket, by=by, **kw))
        return len(s) - 1

    block = 9  # the Frame Block on the seat's +x side
    seat = add("Seat", block, "+y", "bottom")
    turret = add("Turret Module", block, "+z", "-x", config={"stabilised": 1})
    table = add("Turntable", block, "-y", "base")
    arm = add("Frame Block", table, "top", "+y")
    hinge = add("Motor Hinge", arm, "-y", "base")
    mg = add("Mounted MG", hinge, "leaf", "top")
    box_at = add("Ammo Box", mg, "back", "front")
    flares = add("Flare Dispenser", 10, "-y", "top")
    flare_box = add("Ammo Box", flares, "back", "front")
    links += [data(seat, turret), data(turret, mg), data(turret, flares, "aim")]
    assert box_at and flare_box
    return {"format": b["format"], "name": "Gunship", "description": "The Small Helicopter with a door gunner on a stabilised mount: the gun and the look stay on their point as she moves. The gunner's Aim key throws flares.", "steps": s, "links": links}


def main():
    code = write_parts(OUT, parts())
    check = "--check" in sys.argv
    for name, make in (("LightBomber.json", light_bomber), ("FlakGun.json", flak_gun), ("Gunship.json", gunship)):
        path = os.path.join(BLUEPRINTS, name)
        text = json.dumps(make(), indent=2) + "\n"
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
