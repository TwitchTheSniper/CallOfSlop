#!/usr/bin/env python3
"""Writes the Call Centre's parts: Constructs/Parts/CallCentre (docs/vehiclekit/VEHICLEKIT.md, "The Call Centre style").

The Scammers (docs/story/STORY.md, "The Scammers") run a call centre on wheels. Where bandits hang skulls they bolt on desk phones,
headsets on stands, GIFT CARDS signs and a prize wheel; loudhailers down the flanks as spikes; a phone mast on the roof. In their
rooms: cubicle desks (a drawer of loot in each) and a water cooler. Furniture stands on feet on the floor's quarter grid and faces
its own +z, into the room, as tools/constructs/office_parts.py's does. Each trophy stands on one "base" socket and points up its own
+y, its face to -z, so the kit turns it outward. Faces that meet are kept a few millimetres apart (CLAUDE.md, "No coplanar faces").
Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/callcentre_parts.py (--check only compares).
Boxes go through zfight.resolve. Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "CallCentre")
BLACK, GREY, FABRIC, DESK, WHITE = "#25272a", "#8e9296", "#5f7f8f", "#b9b2a2", "#eeeeea"
TEAL, YELLOW, PINK, GREEN, RED = "#1a8c8c", "#ffd400", "#ff3d8b", "#21c45a", "#d8322a"
WATER = "#7cc4f0"
SECTION = "Body panels"
GROUP = "Lights and Details"


def base_socket(p):
    p.sock("base", (0, 0, 0), "-y")


def stalk(p, top, colour=GREY):
    """A square post from the base up to top, under whatever it holds."""
    p.add(box((-0.02, 0, -0.02), (0.02, top, 0.02), colour))


def lit(p, mn, mx, colour):
    """A small lamp on a face: unlit, bright, not paint."""
    p.add(box(mn, mx, colour, detail=True, paintable=False, glow=True))


def feet(p, xs, zs):
    for i, x in enumerate(xs):
        for k, z in enumerate(zs):
            p.sock(f"foot{i}{k}", (x, 0, z), "-y")


def phone():
    p = Part("Office Phone", SMALL, SECTION, "A desk phone on a post, every line blinking. Your call is important to them.", color=BLACK, group=GROUP)
    top = 0.16
    stalk(p, top)
    p.add(box((-0.13, top, -0.1), (0.13, top + 0.06, 0.1), BLACK))  # (the base)
    p.add(box((-0.13, top + 0.062, 0.0), (0.13, top + 0.1, 0.1), BLACK))  # (the raised back, where the handset sits)
    p.add(box((-0.15, top + 0.102, 0.01), (0.15, top + 0.15, 0.08), "#34373b", detail=True))  # (the handset)
    for k in range(4):  # (the line buttons: all lit, all holding)
        x = -0.11 + k * 0.06
        lit(p, (x, top + 0.062, -0.08), (x + 0.04, top + 0.074, -0.04), RED if k == 1 else GREEN)
    p.add(box((0.155, top + 0.02, 0.03), (0.175, top + 0.12, 0.05), "#34373b", detail=True))  # (the curly cord, down its side)
    base_socket(p)
    p.stats(3, 25, 0, 0.5)
    return "", p


def headset():
    p = Part("Call Headset", SMALL, SECTION, "A headset on a stand. The microphone has heard every script ever written.", color=BLACK, group=GROUP)
    top = 0.2
    stalk(p, top, BLACK)
    p.add(box((-0.12, top, -0.02), (0.12, top + 0.03, 0.02), GREY))  # (the stand's bar)
    p.add(box((-0.12, top + 0.032, -0.025), (0.12, top + 0.25, 0.025), hidden=True))
    p.add(box((-0.12, top + 0.032, -0.025), (0.12, top + 0.25, 0.025), BLACK, detail=True, shape="cylinder", segments=12, front="+z"))  # (the band)
    for x in (-0.15, 0.11):
        p.add(box((x, top + 0.02, -0.06), (x + 0.04, top + 0.12, 0.06), FABRIC, detail=True, paintable=False))  # (the cups, outside the band)
    p.add(box((0.12, top + 0.04, -0.16), (0.135, top + 0.055, -0.06), BLACK, detail=True))  # (the boom)
    lit(p, (0.115, top + 0.03, -0.19), (0.14, top + 0.065, -0.162), RED)  # (the mic: always on)
    base_socket(p)
    p.stats(2, 20, 0, 0.5)
    return "", p


def gift_sign():
    p = Part("Gift Card Sign", SMALL, SECTION, "GIFT CARDS ACCEPTED. Gift cards preferred. Gift cards only, actually.", color=PINK, group=GROUP)
    top = 0.3
    stalk(p, top, BLACK)
    p.add(box((-0.3, top, -0.03), (0.3, top + 0.3, 0.0), PINK))  # (the board)
    for k, c in enumerate((YELLOW, GREEN, TEAL, WHITE)):  # (four cards, fanned across it, 4 mm proud)
        x = -0.26 + k * 0.13
        p.add(box((x, top + 0.06 + (k % 2) * 0.03, -0.034), (x + 0.11, top + 0.2 + (k % 2) * 0.03, -0.03), c, detail=True, paintable=False))
    p.add(box((-0.26, top + 0.235, -0.034), (0.26, top + 0.27, -0.03), YELLOW, detail=True, paintable=False))  # (the headline)
    base_socket(p)
    p.stats(3, 25, 0, 0.5)
    return "", p


def prize_wheel():
    p = Part("Prize Wheel", SMALL, SECTION, "A wheel of prizes on a post. Every slice says WINNER. None of them are.", color=YELLOW, group=GROUP)
    top = 0.22
    stalk(p, top, BLACK)
    r = 0.24
    p.add(box((-r, top, -0.03), (r, top + 2 * r, 0.0), hidden=True))
    p.add(box((-r, top, -0.03), (r, top + 2 * r, 0.0), YELLOW, detail=True, shape="cylinder", segments=12, front="-z"))  # (the wheel: 12 sides, so it gets a hull; 16 keep their boxes)
    cy = top + r
    for k, c in enumerate((PINK, GREEN, TEAL, RED)):  # (the slices: four bars across its face, 4 mm proud)
        y = cy - 0.15 + k * 0.08
        p.add(box((-0.15 + abs(k - 1.5) * 0.03, y, -0.034), (0.15 - abs(k - 1.5) * 0.03, y + 0.06, -0.03), c, detail=True, paintable=False))
    p.add(box((-0.03, cy - 0.03, -0.042), (0.03, cy + 0.03, -0.034), WHITE, detail=True, paintable=False))  # (the hub)
    p.add(box((-0.02, top + 2 * r + 0.004, -0.02), (0.02, top + 2 * r + 0.06, 0.0), RED, detail=True, paintable=False))  # (the pointer)
    base_socket(p)
    p.stats(4, 25, 0, 0.5)
    return "", p


def loudhailer():
    p = Part("Loudhailer", SMALL, SECTION, "A loudhailer on a post, for when nobody picks up: CONGRATULATIONS, YOU HAVE BEEN SELECTED.", color=WHITE, group=GROUP)
    top = 0.12
    p.add(box((-0.05, 0, -0.04), (0.05, top, 0.04), BLACK))  # (its foot)
    p.add(box((-0.06, top, -0.05), (0.06, top + 0.1, 0.07), GREY))  # (the driver)
    p.add(box((-0.13, top - 0.06, -0.3), (0.13, top + 0.16, -0.05), hidden=True))
    p.add(box((-0.13, top - 0.06, -0.3), (0.13, top + 0.16, -0.05), WHITE, detail=True, shape="cylinder", segments=12, taper=[0.35, 0.35], front="+z"))  # (the horn: wide at the mouth)
    base_socket(p)
    p.stats(3, 25, 0, 0.5)
    return "", p


def mast():
    p = Part("Phone Mast", SMALL, SECTION, "A phone mast, bolted to the roof. Four bars of signal, all of it outgoing.", color=GREY, group=GROUP)
    h = 0.9
    p.add(box((-0.06, 0, -0.06), (0.06, 0.04, 0.06), BLACK))  # (its foot)
    p.add(box((-0.025, 0.04, -0.025), (0.025, h, 0.025), GREY))  # (the pole)
    for k, (x, z) in enumerate(((0.0, -0.06), (0.06, 0.0), (0.0, 0.06), (-0.06, 0.0))):  # (the panel antennas round its top, clear of the pole)
        sx, sz = (0.03, 0.012) if x == 0 else (0.012, 0.03)
        p.add(box((x - sx, h - 0.3, z - sz), (x + sx, h - 0.04, z + sz), WHITE, detail=True))
    lit(p, (-0.02, h + 0.002, -0.02), (0.02, h + 0.04, 0.02), RED)  # (the beacon on top)
    base_socket(p)
    p.stats(5, 30, 1, 0.5)
    return "", p


def cubicle():
    p = Part("Cubicle Desk", SMALL, "Furniture", "A cubicle: a desk, a phone, a headset and a script taped to the wall. A drawer of whatever they got today.",
             role="storage", tool="wrench", color=FABRIC, group="Vehicle Body")
    w, d, top = 0.3, 0.25, 0.72  # (as narrow as a rack: a van's room fits a row of them)
    p.add(box((-w, top, -d), (w, top + 0.03, d), DESK))  # (the desk top)
    p.add(box((-w, 0, -d), (-w + 0.03, top - 0.002, d), GREY))  # (the left leg panel)
    # The drawer pedestal, on the right: open at the front, round the drawer's slot.
    px = w - 0.3
    p.add(box((px, 0.0, -d + 0.02), (px + 0.015, top - 0.002, d - 0.03), GREY))
    p.add(box((w - 0.015, 0.0, -d + 0.02), (w - 0.002, top - 0.002, d - 0.03), GREY))
    p.add(box((px + 0.017, 0.0, -d + 0.02), (w - 0.017, 0.05, d - 0.03), GREY))
    p.add(box((px + 0.017, 0.62, -d + 0.02), (w - 0.017, top - 0.002, d - 0.03), GREY))
    p.add(box((px + 0.017, 0.052, -d + 0.02), (w - 0.017, 0.618, -d + 0.035), "#6f7378"))
    p.add(box((-w, top + 0.032, -d), (w, 1.2, -d + 0.04), FABRIC))  # (the partition behind it)
    p.add(box((-w, top + 0.032, -d + 0.042), (-w + 0.03, 1.05, d), FABRIC))  # (the side partition)
    p.add(box((-0.16, top + 0.2, -d + 0.044), (0.04, top + 0.4, -d + 0.048), WHITE, detail=True, paintable=False))  # (the script, taped up)
    p.add(box((-0.25, top + 0.032, -0.16), (-0.1, top + 0.07, -0.04), BLACK, detail=True))  # (the phone)
    lit(p, (-0.23, top + 0.072, -0.1), (-0.19, top + 0.08, -0.06), RED)
    p.add(box((0.0, top + 0.032, -0.2), (0.24, top + 0.22, -0.04), "#d8d0b8", detail=True))  # (a monitor, much like IT's)
    p.add(box((0.02, top + 0.055, -0.036), (0.22, top + 0.2, -0.032), "#1d2a33", detail=True, paintable=False))
    p.add(box((-0.08, top + 0.032, 0.04), (0.18, top + 0.05, 0.16), "#cfc6aa", detail=True))  # (the keyboard)
    p.add(box((-0.26, top + 0.032, 0.06), (-0.18, top + 0.1, 0.14), BLACK, detail=True))  # (the headset, dropped on the desk)
    feet(p, (-0.125, 0.125), (0.0,))
    p.stats(26, 60, 1, 2.0)
    p.set(storage={"slots": [{"min": [round(px + 0.02, 3), 0.06, round(-d + 0.04, 3)], "max": [round(w - 0.02, 3), 0.6, round(d - 0.05, 3)], "rack": True, "label": "drawer"}]})
    return "", p


def cooler():
    p = Part("Water Cooler", SMALL, "Furniture", "A water cooler. Where the team lead tells everyone today's numbers.", tool="wrench", color=WHITE, group="Vehicle Body")
    p.add(box((-0.15, 0, -0.15), (0.15, 0.9, 0.15), WHITE))  # (the cabinet)
    p.add(box((-0.06, 0.62, 0.152), (0.06, 0.7, 0.18), GREY, detail=True))  # (the taps' block, proud of its face)
    lit(p, (-0.045, 0.66, 0.182), (-0.015, 0.68, 0.186), "#3aa0ff")
    lit(p, (0.015, 0.66, 0.182), (0.045, 0.68, 0.186), RED)
    p.add(box((-0.12, 0.902, -0.12), (0.12, 1.32, 0.12), hidden=True))
    p.add(box((-0.12, 0.902, -0.12), (0.12, 1.32, 0.12), WATER, detail=True, shape="cylinder", segments=12, front="+y", paintable=False, glass=True))  # (the bottle)
    feet(p, (0.0,), (0.0,))
    p.stats(20, 40, 0, 1.0)
    return "", p


def main():
    return write_parts(OUT, [
        phone(),
        headset(),
        gift_sign(),
        prize_wheel(),
        loudhailer(),
        mast(),
        cubicle(),
        cooler(),
    ])


if __name__ == "__main__":
    sys.exit(main())
