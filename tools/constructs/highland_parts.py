#!/usr/bin/env python3
"""Writes the Clan MacSlop's parts: Constructs/Parts/Highland (docs/vehiclekit/VEHICLEKIT.md, "The Highland style").

The Pipe Box first: a Music Player for a Highland vehicle, a box with a small screen and a set of bagpipes on its lid, that strikes up the
pipes by itself while someone drives (docs/sandbox/MEDIA.md, "Pipe Boxes"; the "pipes" flag of its "media" key). Then what the clan bolts on
where bandits hang skulls: a Saltire on a pole for the roofs, thistles, a haggis on a post and a Highland coo's head as trophies, wee cabers
down the flanks as spikes, a wee keg on the beds, and whisky casks in the rooms (loot inside). Dressing stands on one "base" socket and points
up its own +y, so the kit turns it outward, as the bandit parts do (Source/PartKit/Generators/BanditParts.cs); a cask stands on feet on the floor's
quarter grid, as tools/constructs/office_parts.py's Server Rack does. Faces that meet are kept a few millimetres apart (CLAUDE.md,
"No coplanar faces"). Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/highland_parts.py (--check only compares).
Boxes go through zfight.resolve. Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Highland")
SECTION = "Body panels"
GROUP = "Lights and Details"
WOOD, WOOD_DARK, BEZEL = "#6b4527", "#4a2f1a", "#14181c"
TARTAN, TARTAN_GREEN, EBONY, SILVER = "#9b1c22", "#1f4a2e", "#1b1714", "#c9cbc8"
SALTIRE, WHITE = "#005eb8", "#f2f0e8"
STEM, LEAF, THISTLE = "#3f6b34", "#2f5a2a", "#7a3a8c"
GINGER, GINGER_DARK, HORN, NOSE = "#b5632a", "#8a4a1e", "#e3d6b4", "#2a201c"
HAGGIS, PLATE = "#6e4a2e", "#d9d4c8"
IRON = "#3a3532"

VOLUME = {"key": "volume", "label": "Volume", "group": "General", "min": 0, "max": 1, "step": 0.05, "default": 0.8,
          "tooltip": "How loud it plays, before distance."}
LOOP = {"key": "loop", "label": "Loop the list", "group": "General", "type": "toggle", "default": True,
        "tooltip": "After the last tune it starts again from the top."}


def base_socket(p):
    p.sock("base", (0, 0, 0), "-y")


def post(p, top, colour=IRON):
    """A square post from the base up to top, under whatever it holds."""
    p.add(box((-0.02, 0, -0.02), (0.02, top, 0.02), colour))


def pipe_box():
    desc = ("The Clan MacSlop's pipes, in a box. It strikes up by itself when someone drives what it's on, so you hear them coming, "
            "and plays the game's tracks and links like a Music Player. Heard to 150 m. Run a data cable to a speaker to carry it further.")
    p = Part("Pipe Box", SMALL, "Media", desc, role="panel", tool="wrench", group="Logic", color=WOOD)
    w, h, t = 0.32, 0.32, 0.14
    rim = 0.008
    f = -t / 2  # (the case stands centred on its base socket, the screen's face at -z)
    p.add(box((-w / 2, 0, f), (w / 2, h, t / 2)))  # (the case)
    p.add(box((-w / 2 + rim, rim, f - 0.004), (w / 2 - rim, h - rim, f + 0.002), BEZEL, detail=True))  # (the screen's bezel, as a Music Player's)
    # The pipes on its lid: the bag, three drones up out of it and the blowpipe.
    top = h + 0.004
    p.add(box((-0.11, top, -0.06), (0.11, top + 0.12, 0.06), TARTAN, detail=True, shape="sphere", segments=10, paintable=False))
    for x, tall, z in ((-0.06, 0.32, 0.02), (0.0, 0.27, 0.03), (0.06, 0.27, 0.02)):
        p.add(box((x - 0.013, top + 0.09, z - 0.013), (x + 0.013, top + 0.09 + tall, z + 0.013), EBONY, detail=True, shape="cylinder", segments=8, front="+y", paintable=False))
        p.add(box((x - 0.02, top + 0.09 + tall - 0.03, z - 0.02), (x + 0.02, top + 0.09 + tall, z + 0.02), SILVER, detail=True, shape="cylinder", segments=8, front="+y", paintable=False))
    p.add(box((0.09, top + 0.08, -0.01), (0.15, top + 0.095, 0.004), TARTAN_GREEN, detail=True, paintable=False))  # (the blowpipe)
    base_socket(p)
    p.sock("back", (0, h / 2, t / 2), "+z")
    p.stats(8, 60, 1, 1)
    pw = round(w - 2 * rim, 3)
    bottom = 0.44  # (as the Music Player: YouTube's player stays in view above the controls)
    p.set(config=[VOLUME, LOOP], centreSockets=False, media={"hear": 150, "near": 10, "width": 356, "height": 200, "pipes": True},
          panels=[{"source": "media", "at": [0, h / 2, round(f - 0.007, 3)], "size": [pw, pw], "face": "-z", "up": "+y", "rows": 8, "range": 12,
                   "picture": [0, bottom, 1, 1],
                   "touch": [{"action": "media.previous", "at": [0.02, 0.03, 0.32, 0.24], "label": "Prev"},
                             {"action": "media.toggle", "at": [0.35, 0.03, 0.65, 0.24], "label": "Play"},
                             {"action": "media.next", "at": [0.68, 0.03, 0.98, 0.24], "label": "Next"}]}])
    return "", p


def saltire():
    p = Part("Saltire Flag", SMALL, SECTION, "A Saltire on a pole, white on blue. It flies the right way up whichever way you park.", color=SALTIRE, group=GROUP)
    r, tall = 0.016, 1.0
    p.add(box((-0.05, 0, -0.04), (0.05, 0.04, 0.04), IRON))  # (its foot)
    p.add(box((-r, 0.04, -r), (r, tall, r), hidden=True))
    p.add(box((-r, 0.04, -r), (r, tall, r), WOOD_DARK, detail=True, shape="cylinder", segments=8, front="+y"))
    x0, x1, y0, y1 = r + 0.004, 0.52, tall - 0.36, tall - 0.02
    p.add(box((x0, y0, -0.006), (x1, y1, 0.006), SALTIRE, detail=True))  # (the flag)
    steps = 7
    sw, sh = (x1 - x0) / steps, (y1 - y0) / steps
    for k in range(steps):  # (the white cross, in steps: each square through the cloth, 3 mm proud of both faces)
        x = x0 + k * sw
        for y in (y0 + k * sh, y1 - (k + 1) * sh):
            p.add(box((x, y, -0.009), (min(x1, x + sw * 1.3), min(y1, y + sh * 1.3), 0.009), WHITE, detail=True, paintable=False))
    base_socket(p)
    p.stats(2, 25, 0, 0.5)
    return "", p


def thistle():
    p = Part("Thistle", SMALL, SECTION, "A thistle on a stalk, bigger than they grow. Don't pat it.", color=STEM, group=GROUP)
    top = 0.24
    p.add(box((-0.018, 0, -0.018), (0.018, top, 0.018), STEM))
    for s in (-1, 1):  # (two spiky leaves)
        lo, hi = sorted((s * 0.02, s * 0.12))
        p.add(box((lo, 0.08, -0.012), (hi, 0.11, 0.012), LEAF, detail=True, paintable=False))
    p.add(box((-0.07, top, -0.07), (0.07, top + 0.12, 0.07), STEM, detail=True, shape="sphere", segments=10, paintable=False))  # (the bulb)
    p.add(box((-0.06, top + 0.1, -0.06), (0.06, top + 0.22, 0.06), THISTLE, detail=True, shape="cylinder", segments=10, taper=[0.7, 0.7], front="-y", paintable=False))  # (the purple tuft, flaring up)
    base_socket(p)
    p.stats(1, 15, 0, 0.5)
    return "", p


def caber():
    p = Part("Wee Caber", SMALL, SECTION, "A caber, cut down to fit. Still a log. Still thrown, now and then.", color=WOOD, group=GROUP)
    r, tall = 0.05, 0.7
    p.add(box((-r * 1.5, 0.002, -r * 1.5), (r * 1.5, 0.024, r * 1.5), IRON, detail=True, shape="cylinder", segments=8, front="+y"))
    p.add(box((-r, 0, -r), (r, tall, r), hidden=True))
    p.add(box((-r, 0, -r), (r, tall, r), WOOD, detail=True, shape="cylinder", segments=8, taper=[0.8, 0.8], front="+y", paintable=False))
    p.add(box((-r * 0.8 - 0.004, tall - 0.003, -r * 0.8 - 0.004), (r * 0.8 + 0.004, tall + 0.008, r * 0.8 + 0.004), "#c8a77a", detail=True, shape="cylinder", segments=8, front="+y", paintable=False))  # (its cut end)
    base_socket(p)
    p.stats(6, 50, 2, 0.5)
    return "", p


def coo():
    p = Part("Coo Heid", SMALL, SECTION, "A Highland coo's head, ginger fringe and all. Ceramic, the clan insists.", color=GINGER, group=GROUP)
    top = 0.2
    post(p, top)
    p.add(box((-0.06, top, -0.05), (0.06, top + 0.14, 0.06), hidden=True))
    p.add(box((-0.08, top, -0.08), (0.08, top + 0.17, 0.08), GINGER, detail=True, shape="sphere", segments=10, paintable=False))  # (the shaggy head)
    p.add(box((-0.07, top + 0.09, -0.094), (0.07, top + 0.16, -0.05), GINGER_DARK, detail=True, paintable=False))  # (the fringe, over its eyes)
    p.add(box((-0.045, top + 0.005, -0.112), (0.045, top + 0.06, -0.06), NOSE, detail=True, paintable=False))  # (the nose)
    for s in (-1, 1):
        lo, hi = sorted((s * 0.07, s * 0.25))
        p.add(box((lo, top + 0.12, -0.02), (hi, top + 0.155, 0.015), HORN, detail=True, paintable=False))
        lo2, hi2 = sorted((s * 0.22, s * 0.255))
        p.add(box((lo2, top + 0.15, -0.02), (hi2, top + 0.27, 0.015), HORN, detail=True, paintable=False))
    base_socket(p)
    p.stats(3, 25, 0, 0.5)
    return "", p


def haggis():
    p = Part("Haggis on a Post", SMALL, SECTION, "A haggis on a plate on a post. A wild one: they run faster one way round the hill.", color=HAGGIS, group=GROUP)
    top = 0.22
    post(p, top)
    p.add(box((-0.13, top, -0.13), (0.13, top + 0.02, 0.13), PLATE, detail=True, shape="cylinder", segments=12, front="+y", paintable=False))
    p.add(box((-0.1, top + 0.024, -0.075), (0.1, top + 0.15, 0.075), HAGGIS, detail=True, shape="sphere", segments=10, paintable=False))
    p.add(box((-0.016, top + 0.03, -0.08), (0.016, top + 0.15, 0.08), TARTAN, detail=True, paintable=False))  # (its tartan ribbon)
    base_socket(p)
    p.stats(2, 15, 0, 0.5)
    return "", p


def keg():
    p = Part("Wee Keg", SMALL, SECTION, "A wee keg on a stand, strapped down. Strictly for the radiator.", color=WOOD, group=GROUP)
    r, tall = 0.15, 0.36
    p.add(box((-0.1, 0, -0.1), (0.1, 0.03, 0.1), IRON))  # (the stand)
    p.add(box((-r, 0.032, -r), (r, 0.032 + tall, r), hidden=True))
    p.add(box((-r, 0.032, -r), (r, 0.032 + tall, r), WOOD, detail=True, shape="cylinder", segments=12, taper=[0.86, 0.86], front="+y"))
    for y in (0.07, 0.032 + tall - 0.06):  # (the hoops, 4 mm proud of the staves)
        p.add(box((-r - 0.004, y, -r - 0.004), (r + 0.004, y + 0.022, r + 0.004), IRON, detail=True, shape="cylinder", segments=12, front="+y", paintable=False))
    base_socket(p)
    p.stats(9, 40, 1, 0.5)
    return "", p


def feet(p, xs, zs):
    for i, x in enumerate(xs):
        for k, z in enumerate(zs):
            p.sock(f"foot{i}{k}", (x, 0, z), "-y")


def cask():
    p = Part("Whisky Cask", SMALL, "Furniture", "A cask stood on end, ageing. What's inside is loot, and older than you.", role="storage", tool="wrench", color=WOOD, group="Vehicle Body")
    r, tall, wall = 0.26, 0.95, 0.04
    p.add(box((-r, 0, -r), (r, tall, r), WOOD, detail=True, shape="cylinder", segments=12, taper=[0.9, 0.9], front="+y"))  # (the staves)
    for y in (0.08, tall / 2 - 0.012, tall - 0.11):  # (hoops, 4 mm proud)
        p.add(box((-r - 0.004, y, -r - 0.004), (r + 0.004, y + 0.025, r + 0.004), IRON, detail=True, shape="cylinder", segments=12, front="+y", paintable=False))
    p.add(box((-0.03, tall - 0.002, -0.02), (0.03, tall + 0.014, 0.04), WOOD_DARK, detail=True, paintable=False))  # (the bung)
    # Solid: its foot, its lid and four walls round the loot (a slot can't run into a solid box).
    i = 0.9 * r
    p.add(box((-i, 0, -i), (i, 0.05, i), hidden=True))
    p.add(box((-i, tall - 0.04, -i), (i, tall, i), hidden=True))
    for s in (-1, 1):
        lo, hi = sorted((s * (i - wall), s * i))
        p.add(box((lo, 0.052, -i), (hi, tall - 0.042, i), hidden=True))
        p.add(box((-i + wall + 0.002, 0.052, lo), (i - wall - 0.002, tall - 0.042, hi), hidden=True))
    feet(p, (-0.125, 0.125), (0.0,))
    p.stats(30, 160, 2, 2.0)
    k = i - wall - 0.006
    p.set(storage={"slots": [{"min": [-k, 0.056, -k], "max": [k, 0.48, k], "label": "cask"},
                             {"min": [-k, 0.49, -k], "max": [k, tall - 0.046, k], "label": "cask"}]})
    return "", p


def main():
    return write_parts(OUT, [
        pipe_box(),
        saltire(),
        thistle(),
        caber(),
        coo(),
        haggis(),
        keg(),
        cask(),
    ])


if __name__ == "__main__":
    sys.exit(main())
