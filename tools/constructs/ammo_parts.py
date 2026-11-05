#!/usr/bin/env python3
"""Writes the ammo parts: Constructs/Parts/Ammo. docs/sandbox/CONSTRUCTS.md, "Ammo feeds".

The Ammo Container: a Storage Volume's box that holds bulk "ammo", with an ammo type and a feed out. It stores what a Storage
Volume stores and follows its rules, sealing included (ammo is a solid, and it burns). The Ammo Switches: 2, 4 and 8 feeds in,
one out, a data input picking which. Run it from the repo root; fix it, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Ammo")
SECTION = "Ammo boxes"
BODY, BAND, BRASS, STEEL = "#4d5a3a", "#c9a227", "#b08d3c", "#7a7f88"


def box_config():
    """The Storage Volume's box settings and its creative contents, word for word, then the ammo type."""
    with open(os.path.join("Constructs", "Parts", "Storage", "StorageVolume.json"), encoding="utf-8") as f:
        volume = json.load(f)
    return volume["config"] + [AMMO_TYPE]


AMMO_TYPE = {"key": "ammoType", "label": "Ammo type", "type": "choice", "group": "Contents", "tooltip": "What this holds: any gun takes any type, and the type is what a gun fed from here fires."}


def container():
    p = Part("Ammo Container", SMALL, SECTION, "A box you size, for rounds. Inside your walls and armour: a bunker. Feeds run from here to the guns. It blows up by what is in it.", role="storage", tool="wrench", color=BODY, group="Weapons")
    h = 0.2
    p.add(box((-h, -h, -h), (h, h, h), BODY))
    p.add(box((-h - 0.01, -h - 0.01, -0.03), (h + 0.01, h + 0.01, 0.03), BAND, detail=True))
    p.add(box((-0.06, h, -0.06), (0.06, h + 0.04, 0.06), BRASS, detail=True))
    p.add(box((-0.07, -0.07, h), (0.07, 0.07, h + 0.03), STEEL, detail=True))
    for d, at in (("+x", (h, 0, 0)), ("-x", (-h, 0, 0)), ("+y", (0, h, 0)), ("-y", (0, -h, 0)), ("+z", (0, 0, h)), ("-z", (0, 0, -h))):
        p.sock(d, at, d)
    p.stats(20, 500, 8, 2.0)
    p.set(storage={"ammoStore": True, "bulk": {"material": "any", "volume": True, "chuteLoad": 160}})
    p.set(ports=[
        {"name": "Ammo out", "type": "feed", "dir": "out", "at": [0, h, 0], "face": "+y"},
        {"name": "Chute in", "type": "chute", "dir": "in", "at": [0, 0, h], "face": "+z"},
    ])
    p.set(config=box_config())
    return ("", p)


def switch(n):
    """n feeds in along its back, one out at the front, the data input (the part's own) on top."""
    w = 0.1 + 0.07 * n
    h = 0.11
    d = 0.16
    p = Part(f"Ammo Switch {n}", SMALL, SECTION, f"{n} ammo feeds in, one out. Its data input picks which goes on: 0 the first, 1 the next. It also stops a fire running along a feed.", role="ammoswitch", tool="wrench", color=BODY, group="Weapons")
    p.add(box((-w, -h, -d), (w, h, d), BODY))
    p.add(box((-w - 0.01, h - 0.03, -0.03), (w + 0.01, h + 0.01, 0.03), BAND, detail=True))
    p.add(box((-0.05, -0.05, -d - 0.03), (0.05, 0.05, -d), STEEL, detail=True))
    ports = [{"name": "Ammo out", "type": "feed", "dir": "out", "at": [0, 0, -d], "face": "-z"}]
    for k in range(n):
        x = round(-w + (2 * k + 1) * w / n, 4)
        p.add(box((x - 0.035, -0.04, d), (x + 0.035, 0.04, d + 0.025), BRASS, detail=True))
        ports.append({"name": f"Ammo in {k + 1}", "type": "feed", "dir": "in", "at": [x, 0, d], "face": "+z"})
    p.sock("bottom", (0, -h, 0), "-y")
    p.sock("top", (0, h, 0), "+y")
    p.stats(6 + 2 * n, 120, 6, 1.5)
    p.set(ports=ports)
    return ("", p)


def reel(name, s, length, mass, health):
    """A drum on a cradle: its "Ammo in" takes a feed (or a box bolts to it), its belt pays out from the front to an Ammo Socket."""
    p = Part(name, SMALL, "Hoses and leads", f"A {length} m ammo belt on a drum. Feed it, take the plug to an Ammo Socket on what moves: a turret, or another construct. It stays whole however the turret turns.", role="hose", tool="wrench", color="#3c4046", group="Hoses")
    x, y, z = 0.3 * s, 0.2 * s, 0.2 * s
    p.add(box((-x, -0.25 * s, -z), (x, -0.17 * s, z)))
    p.add(box((-x, -0.17 * s, -0.04 * s), (-0.24 * s, y, 0.04 * s)))
    p.add(box((0.24 * s, -0.17 * s, -0.04 * s), (x, y, 0.04 * s)))
    drum = box((-0.24 * s, -0.12 * s, -0.16 * s), (0.24 * s, y, 0.16 * s), BAND)
    drum["use"] = True
    p.add(drum)
    p.add(box((-0.05 * s, -0.01 * s, z), (0.05 * s, 0.09 * s, 0.25 * s), "#202226", detail=True))
    p.sock("-y", (0, -0.25 * s, 0), "-y")
    p.sock("-z", (0, 0, -z), "-z")
    p.sock("+x", (x, 0, 0), "+x")
    p.sock("-x", (-x, 0, 0), "-x")
    p.stats(mass, health, 2, 1.5)
    p.set(hose={"end": "outlet", "carries": "ammo", "length": length, "at": [0, round(0.04 * s, 4), round(0.25 * s, 4)]})
    p.set(ports=[{"name": "Ammo in", "type": "feed", "dir": "in", "at": [0, -0.25 * s, round(0.15 * s, 4)], "face": "-y"}])
    return ("", p)


def socket():
    """Bolted to a gun it feeds it; otherwise an "Ammo out" link goes on from it."""
    p = Part("Ammo Socket", SMALL, "Hoses and leads", "Takes an ammo reel's plug. Bolted to a gun it feeds it; elsewhere a feed goes on from it.", role="hose", tool="wrench", color="#55595f", group="Hoses")
    p.add(box((-0.12, -0.12, -0.05), (0.12, 0.12, 0.03)))
    plug = box((-0.06, -0.06, 0.03), (0.06, 0.06, 0.1), BAND)
    plug["use"] = True
    p.add(plug)
    p.sock("-z", (0, 0, -0.05), "-z")
    p.sock("-y", (0, -0.12, 0), "-y")
    p.sock("left", (-0.12, 0, 0), "-x")
    p.sock("right", (0.12, 0, 0), "+x")
    p.sock("top", (0, 0.12, 0), "+y")
    p.sock("bottom", (0, -0.12, -0.001), "-y")
    p.sock("back", (0, 0, 0.1), "+z")
    p.sock("front", (0, 0, -0.051), "-z")
    p.stats(3, 80, 2, 1.0)
    p.set(hose={"end": "inlet", "carries": "ammo", "at": [0, 0, 0.12]})
    p.set(ports=[{"name": "Ammo out", "type": "feed", "dir": "out", "at": [0.12, 0, 0], "face": "+x"}])
    return ("", p)


def parts():
    return [container()] + [switch(n) for n in (2, 4, 8)] + [reel("Ammo Reel", 1, 8, 18, 120), reel("Large Ammo Reel", 1.4, 16, 32, 180), socket()]


def main():
    return write_parts(OUT, parts())


if __name__ == "__main__":
    sys.exit(main())
