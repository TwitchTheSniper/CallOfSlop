#!/usr/bin/env python3
"""Writes the IT Department's parts: Constructs/Parts/Office (docs/vehiclekit/VEHICLEKIT.md, "The IT style").

What the IT Department bolts on where bandits hang skulls and spikes: a beige monitor on a stalk, a potted plant, a desk fan, an
Employee of the Month plaque, a satellite dish, a network switch and a cable reel as trophies, and router antennas down the flanks as
spikes. In its rooms: server racks (loot on their shelves), and the Computer and the Fax Machine, the only line to the IT Department
(docs/vehiclekit/VEHICLEKIT.md, "The IT style"; docs/story/STORY.md, "The IT Department's fax"). Furniture stands on feet on the
floor's quarter grid and faces its own +z, into the room, as tools/constructs/body_parts.py's does. Each stands on one "base" socket and points
up its own +y, so the kit turns it outward, as the bandit parts do (tools/constructs/bandit_parts.py). Leaves and soil aren't
paintable; the rest takes the style's paint. Faces that meet are kept a few millimetres apart (CLAUDE.md, "No coplanar faces").
Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/office_parts.py (--check only compares).
Boxes go through zfight.resolve. Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Office")
BEIGE, BEIGE_DARK, SCREEN, GREY, DARK = "#d8d0b8", "#b8ae94", "#1d2a33", "#8e9296", "#2c2e31"
POT, SOIL, LEAF, LEAF_DARK = "#b85c38", "#3a2a1e", "#3f8a3a", "#2e6b2c"
GOLD, BLUE = "#c8a018", "#1f4e8c"
SECTION = "Body panels"
GROUP = "Lights and Details"
OFFICE = "Office"
LED_G, LED_B, LED_A, PAPER, CABLE = "#3fd04a", "#3aa0ff", "#f0a020", "#f2efe6", "#e86a1a"


def base_socket(p):
    p.sock("base", (0, 0, 0), "-y")


def stalk(p, top, colour=GREY):
    """A square post from the base up to top, under whatever it holds."""
    p.add(box((-0.02, 0, -0.02), (0.02, top, 0.02), colour))


def monitor():
    p = Part("CRT Monitor", SMALL, SECTION, "A beige monitor on a pole. Still logged in to something important.", color=BEIGE, group=GROUP)
    top = 0.18
    stalk(p, top)
    p.add(box((-0.07, top, -0.06), (0.07, top + 0.02, 0.06), BEIGE_DARK))  # (the foot)
    p.add(box((-0.17, top + 0.02, -0.12), (0.17, top + 0.3, 0.14), BEIGE))  # (the case: deep, as they were)
    p.add(box((-0.14, top + 0.05, -0.124), (0.14, top + 0.27, -0.1), SCREEN, detail=True, paintable=False))  # (the glass, 4 mm proud of the case's face)
    p.add(box((0.1, top + 0.03, -0.126), (0.13, top + 0.045, -0.11), "#3fd04a", detail=True, paintable=False))  # (the power light)
    base_socket(p)
    p.stats(9, 30, 1, 0.5)
    return "", p


def plant():
    p = Part("Office Plant", SMALL, SECTION, "A potted plant nobody waters. It has outlived three reorganisations.", color=POT, group=GROUP)
    p.add(box((-0.11, 0, -0.11), (0.11, 0.22, 0.11), hidden=True))
    p.add(box((-0.11, 0, -0.11), (0.11, 0.22, 0.11), POT, detail=True, shape="cylinder", segments=12, taper=[0.8, 0.8], front="+y"))
    p.add(box((-0.095, 0.2, -0.095), (0.095, 0.225, 0.095), SOIL, detail=True, shape="cylinder", segments=12, front="+y", paintable=False))
    p.add(box((-0.16, 0.24, -0.16), (0.16, 0.52, 0.16), LEAF, detail=True, shape="sphere", segments=10, paintable=False))
    p.add(box((-0.1, 0.44, -0.12), (0.12, 0.64, 0.1), LEAF_DARK, detail=True, shape="sphere", segments=8, paintable=False))
    base_socket(p)
    p.stats(5, 20, 0, 0.5)
    return "", p


def fan():
    p = Part("Desk Fan", SMALL, SECTION, "A desk fan on a stalk. Moves the air, not the deadline.", color=BEIGE, group=GROUP)
    top = 0.22
    stalk(p, top)
    p.add(box((-0.05, top, -0.05), (0.05, top + 0.06, 0.06), BEIGE_DARK))  # (the motor)
    p.add(box((-0.15, top - 0.09, -0.08), (0.15, top + 0.21, -0.05), GREY, detail=True, shape="cylinder", segments=12, front="-z"))  # (the guard: 12 sides, so it gets a hull; 16 keep their boxes)
    p.add(box((-0.03, top + 0.03, -0.085), (0.03, top + 0.09, -0.078), DARK, detail=True, shape="cylinder", segments=8, front="-z"))  # (the hub, in front of the guard)
    base_socket(p)
    p.stats(3, 20, 0, 0.5)
    return "", p


def plaque():
    p = Part("Employee of the Month", SMALL, SECTION, "A gold plaque on a post. Everyone has had it twice.", color=GOLD, group=GROUP)
    top = 0.25
    stalk(p, top, DARK)
    p.add(box((-0.16, top, -0.03), (0.16, top + 0.22, 0.0), "#5a3a22"))  # (the board)
    p.add(box((-0.12, top + 0.03, -0.034), (0.12, top + 0.19, -0.03), GOLD, detail=True))  # (the plate, 4 mm proud)
    p.add(box((-0.08, top + 0.13, -0.038), (0.08, top + 0.16, -0.034), BLUE, detail=True, paintable=False))  # (the name, in the house blue)
    base_socket(p)
    p.stats(3, 25, 1, 0.5)
    return "", p


def antenna(name, length, desc, mass):
    p = Part(name, SMALL, SECTION, desc, color=DARK, group=GROUP)
    r = 0.016
    p.add(box((-0.05, 0, -0.035), (0.05, 0.04, 0.035), DARK))  # (its foot)
    p.add(box((-r, 0.04, -r), (r, length, r), hidden=True))
    p.add(box((-r, 0.04, -r), (r, length, r), DARK, detail=True, shape="cylinder", segments=8, taper=[0.7, 0.7], front="+y"))
    p.add(box((-0.012, 0.012, -0.039), (0.012, 0.024, -0.035), "#3fd04a", detail=True, paintable=False))  # (the link light, on the foot's face)
    base_socket(p)
    p.stats(mass, 25, 1, 0.5)
    return "", p


def feet(p, xs, zs):
    for i, x in enumerate(xs):
        for k, z in enumerate(zs):
            p.sock(f"foot{i}{k}", (x, 0, z), "-y")


def rack_box(mn, mx):
    return {"min": list(mn), "max": list(mx), "rack": True, "label": "shelf"}


def lit(p, mn, mx, colour):
    """A small lamp on a face: unlit, bright, not paint."""
    p.add(box(mn, mx, colour, detail=True, paintable=False, glow=True))


def server_rack():
    p = Part("Server Rack", SMALL, "Furniture", "A rack of humming servers. Blinking means it works. Probably. Spare parts on the shelves.", role="storage", tool="wrench", color=DARK, group="Vehicle Body")
    w, d, h = 0.3, 0.275, 1.5
    p.add(box((-w, 0, -d), (-w + 0.03, h, d)))  # (the sides)
    p.add(box((w - 0.03, 0, -d), (w, h, d)))
    p.add(box((-w + 0.032, h - 0.03, -d), (w - 0.032, h, d)))  # (the top)
    p.add(box((-w + 0.032, 0, -d), (w - 0.032, 0.08, d)))  # (the plinth)
    bands = [(0.08, 0.5), (0.53, 0.95), (0.98, 1.4)]
    for k, (y0, y1) in enumerate(bands):
        p.add(box((-w + 0.034, y0 + 0.002, -d + 0.004), (w - 0.034, y1, -0.02), "#202225"))  # (the servers, at the back)
        for j in range(3):  # (a row of lights on each server's face)
            yy = y0 + 0.06 + j * (y1 - y0 - 0.08) / 3
            lit(p, (-0.22, yy, -0.02), (-0.12, yy + 0.015, -0.006), LED_G if (j + k) % 2 == 0 else LED_B)
            lit(p, (0.14, yy, -0.02), (0.2, yy + 0.015, -0.006), LED_A if j == 1 and k == 1 else LED_G)
        if k < 2:
            p.add(box((-w + 0.034, y1, -0.018), (w - 0.034, y1 + 0.03, d - 0.004), GREY))  # (a shelf in front)
    p.add(box((-w + 0.034, 1.4, -d + 0.004), (w - 0.034, h - 0.032, d - 0.004), BLUE, detail=True))  # (the badge band)
    feet(p, (-0.125, 0.125), (-0.125, 0.125))
    p.stats(38, 180, 3, 2.0)
    p.set(storage={"slots": [rack_box((-0.26, 0.08, 0.0), (0.26, 0.5, 0.26)), rack_box((-0.26, 0.56, 0.0), (0.26, 0.95, 0.26)), rack_box((-0.26, 1.01, 0.0), (0.26, 1.4, 0.26))]})
    return "", p


# The adverts on a computer's screen (OfficeParts.FillComputer picks their words; Story/Office/Scammers.json): two banners under the text.
ADVERTS = [{"action": "office.advert", "at": [0.02, 0.2, 0.98, 0.36], "label": "AD"},
           {"action": "office.advert2", "at": [0.02, 0.03, 0.98, 0.19], "label": "AD"}]


def computer():
    p = Part("Computer", SMALL, OFFICE, "A beige computer on a desk. Plug it into a battery's circuit, stand at it and type in chat: it emails the IT Department. Don't click the adverts.", role="panel", tool="wrench", color=BEIGE, group="Logic")
    top = 0.74
    p.add(box((-0.4, top, -0.3), (0.4, top + 0.03, 0.3), "#6e6a60"))  # (the desk top)
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * 0.36, sz * 0.26
            p.add(box((x - 0.025, 0, z - 0.025), (x + 0.025, top - 0.002, z + 0.025), DARK))
    t = top + 0.03
    p.add(box((-0.07, t, -0.2), (0.07, t + 0.02, -0.08), BEIGE_DARK))  # (the monitor's foot)
    p.add(box((-0.2, t + 0.02, -0.28), (0.2, t + 0.34, 0.04), BEIGE))  # (the monitor: deep, as they were)
    p.add(box((-0.17, t + 0.05, 0.04), (0.17, t + 0.31, 0.052), SCREEN, detail=True, paintable=False))  # (the glass)
    lit(p, (0.175, t + 0.025, 0.04), (0.19, t + 0.04, 0.054), LED_G)  # (its power light)
    p.add(box((-0.21, t, 0.1), (0.21, t + 0.025, 0.25), BEIGE_DARK))  # (the keyboard)
    p.add(box((-0.19, t + 0.025, 0.115), (0.19, t + 0.04, 0.235), BEIGE, detail=True))  # (its keys)
    p.add(box((0.25, t, 0.14), (0.31, t + 0.025, 0.22), BEIGE_DARK, detail=True))  # (the mouse)
    p.add(box((0.14, 0.03, -0.22), (0.3, 0.5, 0.2), BEIGE))  # (the tower, under the desk)
    p.add(box((0.16, 0.36, 0.2), (0.28, 0.4, 0.212), DARK, detail=True))  # (its floppy drive)
    lit(p, (0.25, 0.44, 0.2), (0.27, 0.46, 0.212), LED_G)
    p.add(box((0.2, 0.5, -0.14), (0.24, top - 0.002, -0.1), DARK, detail=True))  # (the cable up to the desk)
    feet(p, (-0.25, 0.25), (0.0,))
    p.stats(22, 60, 1, 2.0)
    p.set(power=60, office="computer", centreSockets=False,
          panels=[{"source": "office.computer", "at": [0, round(t + 0.18, 3), 0.056], "size": [0.33, 0.25], "face": "+z", "up": "+y", "rows": 8, "range": 10, "touch": ADVERTS}])
    return "", p


def fax():
    p = Part("Fax Machine", SMALL, OFFICE, "A fax on a cabinet. The only number the IT Department answers. Plug it into a battery's circuit and wait.", role="panel", tool="wrench", color=BEIGE, group="Logic")
    w, d, h = 0.3, 0.225, 0.7
    p.add(box((-w, 0, -d), (w, h, d), GREY))  # (the cabinet)
    p.add(box((-0.26, 0.06, d), (0.26, 0.66, d + 0.012), PAPER, detail=True, paintable=False))  # (the last page, taped to its front)
    p.add(box((-0.22, h + 0.002, -0.18), (0.22, h + 0.14, 0.17), BEIGE))  # (the fax)
    p.add(box((-0.22, h + 0.14, -0.18), (-0.1, h + 0.18, 0.12), DARK))  # (the handset)
    p.add(box((-0.06, h + 0.14, -0.17), (0.18, h + 0.26, -0.15), PAPER, detail=True, paintable=False))  # (paper in the feeder)
    p.add(box((-0.08, h + 0.03, 0.17), (0.18, h + 0.045, 0.3), PAPER, detail=True, paintable=False))  # (a page coming out)
    p.add(box((0.0, h + 0.07, 0.17), (0.16, h + 0.11, 0.182), SCREEN, detail=True, paintable=False))  # (its little screen)
    lit(p, (0.18, h + 0.08, 0.17), (0.2, h + 0.1, 0.182), LED_G)
    feet(p, (-0.125, 0.125), (0.0,))
    p.stats(14, 50, 1, 2.0)
    p.set(power=40, office="fax", centreSockets=False,
          panels=[{"source": "office.fax", "at": [0, 0.36, round(d + 0.016, 3)], "size": [0.5, 0.56], "face": "+z", "up": "+y", "rows": 6, "range": 8}])
    return "", p


def terminal():
    """The terminal in a sealed data centre (docs/story/FRAGMENTS.md; Game/GameStoryFragments.cs): a logic part, its kind "cipher".
    It needs power (a battery on its construct's circuit), then puts out a carrier number and waits for the answer its screen asks
    for on its key input, which a player builds out of logic blocks. Heavy and welder-fitted, so it isn't carried off."""
    p = Part("Data Centre Terminal", SMALL, OFFICE, "A sealed terminal older than anyone's records. Power it, then wire it the answer its screen asks for.", role="logic", tool="welder", color=DARK, group="Logic")
    w, d, h = 0.45, 0.3, 1.3
    p.add(box((-w, 0, -d), (w, h, d), DARK))  # (the cabinet)
    p.add(box((-0.4, 0.66, d + 0.004), (0.4, 1.16, d + 0.02), SCREEN, detail=True, paintable=False))  # (the glass, 4 mm proud)
    p.add(box((-0.4, 0.2, d + 0.004), (0.4, 0.5, d + 0.016), GREY, detail=True))  # (the key-in panel)
    for k in range(4):  # (dead lamps over the screen)
        x = -0.3 + k * 0.2
        lit(p, (x - 0.03, 1.2, d + 0.004), (x + 0.03, 1.24, d + 0.016), LED_A if k == 0 else LED_G)
    p.add(box((-0.06, 0.3, d + 0.018), (0.06, 0.4, d + 0.034), "#c0392b", detail=True, paintable=False))  # (the key socket)
    p.add(box((-w + 0.04, h + 0.002, -d + 0.04), (w - 0.04, h + 0.06, d - 0.04), GREY))  # (the vent on top)
    feet(p, (-0.25, 0.25), (0.0,))
    p.stats(160, 600, 4, 4.0)
    p.set(power=120, logic={"kind": "cipher"}, centreSockets=False,
          panels=[{"source": "story.terminal", "at": [0, 0.91, round(d + 0.024, 3)], "size": [0.78, 0.48], "face": "+z", "up": "+y", "rows": 6, "range": 14}])
    return "", p


def switch():
    p = Part("Network Switch", SMALL, SECTION, "A network switch on a post, cables hanging out of it. Do not unplug the yellow one.", color=DARK, group=GROUP)
    top = 0.14
    stalk(p, top)
    p.add(box((-0.2, top, -0.08), (0.2, top + 0.06, 0.08), DARK))
    for k in range(6):
        x = -0.17 + k * 0.06
        lit(p, (x, top + 0.03, -0.092), (x + 0.025, top + 0.045, -0.08), LED_G if k % 3 else LED_A)
    for k, c in enumerate(("#1f6fd8", "#f2c400", "#c0392b", "#e6e2d6")):  # (patch cables, down from its face)
        x = -0.15 + k * 0.09
        p.add(box((x + 0.03, top - 0.16, -0.094), (x + 0.045, top + 0.02, -0.08), c, detail=True, paintable=False))
    base_socket(p)
    p.stats(3, 30, 1, 0.5)
    return "", p


def dish():
    p = Part("Satellite Dish", SMALL, SECTION, "A satellite dish on a post. It picks up the IT Department's hold music.", color="#c9cbc8", group=GROUP)
    top = 0.22
    stalk(p, top, DARK)
    p.add(box((-0.06, top, -0.06), (0.06, top + 0.04, 0.06), DARK))  # (its mount)
    p.add(box((-0.25, top + 0.04, -0.25), (0.25, top + 0.12, 0.25), hidden=True))
    p.add(box((-0.25, top + 0.04, -0.25), (0.25, top + 0.12, 0.25), "#c9cbc8", detail=True, shape="cylinder", segments=12, taper=[0.35, 0.35], front="-y"))  # (the bowl: narrow at its foot)
    p.add(box((-0.012, top + 0.12, 0.17), (0.012, top + 0.3, 0.194), DARK, detail=True))  # (the arm)
    p.add(box((-0.04, top + 0.3, 0.13), (0.04, top + 0.36, 0.21), DARK, detail=True))  # (the receiver on it)
    base_socket(p)
    p.stats(4, 30, 1, 0.5)
    return "", p


def reel():
    p = Part("Cable Reel", SMALL, SECTION, "A drum of network cable, about a kilometre of it. All of it tangled.", color=CABLE, group=GROUP)
    top = 0.06
    p.add(box((-0.08, 0, -0.06), (0.08, top, 0.06), DARK))  # (its stand)
    r = 0.17
    for x in (-0.14, 0.11):
        p.add(box((x, top, -r), (x + 0.03, top + 2 * r, r), "#7a5e40", detail=True, shape="cylinder", segments=12, front="+x"))  # (the flanges)
    p.add(box((-0.11, top + 0.04, -r + 0.04), (0.11, top + 2 * r - 0.04, r - 0.04), hidden=True))
    p.add(box((-0.11, top + 0.04, -r + 0.04), (0.11, top + 2 * r - 0.04, r - 0.04), CABLE, detail=True, shape="cylinder", segments=12, front="+x", paintable=False))  # (the cable)
    base_socket(p)
    p.stats(6, 30, 0, 0.5)
    return "", p


def main():
    return write_parts(OUT, [
        monitor(),
        plant(),
        fan(),
        plaque(),
        antenna("Router Antenna", 0.3, "A router's antenna, bolted on. Full bars, no internet.", 0.5),
        antenna("Wi-Fi Mast", 0.55, "A tall Wi-Fi antenna. The password is on a sticky note somewhere.", 1),
        switch(),
        dish(),
        reel(),
        server_rack(),
        computer(),
        fax(),
        terminal(),
    ])


if __name__ == "__main__":
    sys.exit(main())
