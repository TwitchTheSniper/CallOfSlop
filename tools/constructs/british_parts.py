#!/usr/bin/env python3
"""Writes the Ministry of Slop's parts: Constructs/Parts/British (docs/vehiclekit/VEHICLEKIT.md, "The British style").

What a very British faction bolts on where bandits hang skulls, mostly understated: a Union Flag on a pole for the roofs, a Belisha
beacon and a rain gauge beside it, a teapot in its cosy, a traffic cone nobody will move and a garden gnome as trophies, furled
brollies down the flanks as spikes (it's that sort of day), queue posts on the beds (a queue forms behind them), and in the rooms a
pillar box and a red telephone box (loot in each) round a tea urn. Dressing stands on one "base" socket and points up its own +y, so
the kit turns it outward, as the bandit parts do (tools/constructs/bandit_parts.py); furniture stands on feet on the floor's quarter
grid, as tools/constructs/office_parts.py's Server Rack does. Faces that meet are kept a few millimetres apart (CLAUDE.md, "No
coplanar faces"). Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/british_parts.py (--check only compares).
Boxes go through zfight.resolve. Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "British")
SECTION = "Body panels"
GROUP = "Lights and Details"
RED, RED_DARK, BLUE, WHITE = "#c8102e", "#9a0c22", "#012169", "#f2f0e8"
BLACK, IRON, CHROME, GOLD = "#17181a", "#3a3532", "#c9cbc8", "#c8a24a"
AMBER, CONE, CREAM, KNIT = "#f39c12", "#f26a1b", "#e8dfc8", "#3c6e47"
GNOME_BLUE, SKIN, BEARD, GLASS_BLUE = "#2f5aa8", "#e2b48c", "#eeeae0", "#7fb2d9"
WOOD = "#6b4527"


def base_socket(p):
    p.sock("base", (0, 0, 0), "-y")


def post(p, top, colour=IRON):
    """A square post from the base up to top, under whatever it holds."""
    p.add(box((-0.02, 0, -0.02), (0.02, top, 0.02), colour))


def lit(p, mn, mx, colour, **shape):
    """A lamp: unlit, bright, not paint."""
    p.add(box(mn, mx, colour, detail=True, paintable=False, glow=True, **shape))


def feet(p, xs, zs):
    for i, x in enumerate(xs):
        for k, z in enumerate(zs):
            p.sock(f"foot{i}{k}", (x, 0, z), "-y")


def union_flag():
    p = Part("Union Flag", SMALL, SECTION, "The Union Flag on a pole. It's been up there so long that nobody remembers which way up it goes.", color=BLUE, group=GROUP)
    r, tall = 0.016, 1.0
    p.add(box((-0.05, 0, -0.04), (0.05, 0.04, 0.04), IRON))  # (its foot)
    p.add(box((-r, 0.04, -r), (r, tall, r), hidden=True))
    p.add(box((-r, 0.04, -r), (r, tall, r), WOOD, detail=True, shape="cylinder", segments=8, front="+y"))
    x0, x1, y0, y1 = r + 0.004, 0.58, tall - 0.30, tall - 0.02
    p.add(box((x0, y0, -0.006), (x1, y1, 0.006), BLUE, detail=True))  # (the field)
    steps = 6
    sw, sh = (x1 - x0) / steps, (y1 - y0) / steps
    for k in range(steps):  # (the diagonals, in steps: white 3 mm proud of the field, a thinner red 3 mm proud of the white)
        x = x0 + k * sw
        for y in (y0 + k * sh, y1 - (k + 1) * sh):
            p.add(box((x, y, -0.009), (min(x1, x + sw * 1.1), min(y1, y + sh * 1.1), 0.009), WHITE, detail=True, paintable=False))
            p.add(box((x + sw * 0.3, y + sh * 0.3, -0.012), (x + sw * 0.8, y + sh * 0.8, 0.012), RED, detail=True, paintable=False))
    xm, ym = (x0 + x1) / 2, (y0 + y1) / 2
    p.add(box((x0, ym - 0.047, -0.015), (x1, ym + 0.047, 0.015), WHITE, detail=True, paintable=False))  # (the white cross)
    p.add(box((xm - 0.047, y0, -0.015), (xm + 0.047, y1, 0.015), WHITE, detail=True, paintable=False))
    p.add(box((x0, ym - 0.028, -0.018), (x1, ym + 0.028, 0.018), RED, detail=True, paintable=False))  # (the red cross on it)
    p.add(box((xm - 0.028, y0, -0.018), (xm + 0.028, y1, 0.018), RED, detail=True, paintable=False))
    base_socket(p)
    p.stats(2, 25, 0, 0.5)
    return "", p


def teapot():
    p = Part("Teapot", SMALL, SECTION, "A teapot in a knitted cosy, on a post. Still warm. Somehow it is always still warm.", color=KNIT, group=GROUP)
    top = 0.18
    post(p, top)
    p.add(box((-0.1, top, -0.1), (0.1, top + 0.012, 0.1), WHITE, detail=True, shape="cylinder", segments=12, front="+y", paintable=False))  # (its saucer)
    p.add(box((-0.09, top + 0.014, -0.09), (0.09, top + 0.17, 0.09), hidden=True))
    p.add(box((-0.1, top + 0.014, -0.1), (0.1, top + 0.18, 0.1), KNIT, detail=True, shape="sphere", segments=12))  # (the cosy)
    p.add(box((-0.05, top + 0.165, -0.05), (0.05, top + 0.2, 0.05), CREAM, detail=True, shape="cylinder", segments=10, front="+y", paintable=False))  # (the lid)
    p.add(box((-0.018, top + 0.2, -0.018), (0.018, top + 0.228, 0.018), RED, detail=True, shape="sphere", segments=8, paintable=False))  # (the pompom)
    p.add(box((-0.2, top + 0.06, -0.016), (-0.09, top + 0.09, 0.016), CREAM, detail=True, paintable=False))  # (the spout, then its upturn)
    p.add(box((-0.215, top + 0.07, -0.016), (-0.18, top + 0.15, 0.016), CREAM, detail=True, paintable=False))
    p.add(box((0.09, top + 0.05, -0.012), (0.17, top + 0.075, 0.012), CREAM, detail=True, paintable=False))  # (the handle: a loop of three)
    p.add(box((0.145, top + 0.077, -0.012), (0.17, top + 0.13, 0.012), CREAM, detail=True, paintable=False))
    p.add(box((0.09, top + 0.132, -0.012), (0.17, top + 0.155, 0.012), CREAM, detail=True, paintable=False))
    base_socket(p)
    p.stats(2, 15, 0, 0.5)
    return "", p


def cone():
    p = Part("Traffic Cone", SMALL, SECTION, "A traffic cone. Nobody remembers putting it there, and nobody will ever move it.", color=CONE, group=GROUP)
    p.add(box((-0.13, 0, -0.13), (0.13, 0.037, 0.13), BLACK))  # (its square foot)
    p.add(box((-0.1, 0.039, -0.1), (0.1, 0.5, 0.1), CONE, detail=True, shape="cylinder", segments=12, taper=[0.15, 0.15], front="-y"))
    p.add(box((-0.072, 0.2, -0.072), (0.072, 0.29, 0.072), WHITE, detail=True, shape="cylinder", segments=12, taper=[0.78, 0.78], front="-y", paintable=False))  # (the reflective band, a hair proud)
    p.add(box((-0.05, 0.039, -0.05), (0.05, 0.45, 0.05), hidden=True))
    base_socket(p)
    p.stats(1, 15, 0, 0.5)
    return "", p


def gnome():
    p = Part("Garden Gnome", SMALL, SECTION, "A garden gnome with a fishing rod. It has seen things. It will not be drawn on them.", color=GNOME_BLUE, group=GROUP)
    p.add(box((-0.08, 0, -0.08), (0.08, 0.037, 0.08), "#6f6a5e"))  # (its plinth)
    p.add(box((-0.07, 0.039, -0.06), (0.07, 0.17, 0.06), GNOME_BLUE, detail=True, shape="cylinder", segments=10, taper=[0.8, 0.8], front="+y"))  # (coat)
    p.add(box((-0.072, 0.13, -0.062), (0.072, 0.15, 0.062), BLACK, detail=True, shape="cylinder", segments=10, front="+y", paintable=False))  # (belt)
    p.add(box((-0.05, 0.17, -0.05), (0.05, 0.25, 0.05), SKIN, detail=True, shape="sphere", segments=10, paintable=False))  # (head)
    p.add(box((-0.045, 0.15, -0.068), (0.045, 0.215, -0.03), BEARD, detail=True, paintable=False))  # (beard)
    p.add(box((-0.054, 0.232, -0.054), (0.054, 0.38, 0.054), RED, detail=True, shape="cylinder", segments=10, taper=[0.05, 0.05], front="-y", paintable=False))  # (hat)
    p.add(box((0.07, 0.09, -0.012), (0.09, 0.11, 0.012), SKIN, detail=True, paintable=False))  # (hand, rod up out of it)
    p.add(box((0.074, 0.112, -0.006), (0.086, 0.42, 0.006), WOOD, detail=True, paintable=False))
    p.add(box((-0.05, 0.037, -0.05), (0.05, 0.36, 0.05), hidden=True))
    base_socket(p)
    p.stats(2, 15, 0, 0.5)
    return "", p


def rain_gauge():
    p = Part("Rain Gauge", SMALL, SECTION, "A rain gauge on a post. Never empty. Worth mentioning to everyone you meet.", color=IRON, group=GROUP)
    top = 0.5
    post(p, top)
    p.add(box((-0.06, top, -0.06), (0.06, top + 0.02, 0.06), IRON))  # (its bracket)
    p.add(box((-0.045, top + 0.022, -0.045), (0.045, top + 0.15, 0.045), GLASS_BLUE, detail=True, shape="cylinder", segments=10, front="+y", paintable=False))  # (the rain: always some)
    p.add(box((-0.05, top + 0.022, -0.05), (0.05, top + 0.26, 0.05), CHROME, detail=True, shape="cylinder", segments=10, front="+y", paintable=False, glass=True))  # (the tube)
    p.add(box((-0.08, top + 0.262, -0.08), (0.08, top + 0.3, 0.08), CHROME, detail=True, shape="cylinder", segments=10, taper=[0.6, 0.6], front="+y", paintable=False))  # (the funnel)
    base_socket(p)
    p.stats(1, 15, 0, 0.5)
    return "", p


def brolly():
    p = Part("Furled Brolly", SMALL, SECTION, "A furled umbrella, held out like a lance. You'll want it later: it's that sort of day.", color=BLACK, group=GROUP)
    p.add(box((-0.04, 0, -0.04), (0.04, 0.037, 0.04), IRON))  # (its clamp)
    p.add(box((-0.03, 0.039, -0.03), (0.03, 0.12, 0.03), WOOD, detail=True, paintable=False))  # (the crook's shank, then the hook)
    p.add(box((-0.03, 0.0, 0.03), (0.03, 0.03, 0.11), WOOD, detail=True, paintable=False))
    p.add(box((-0.045, 0.12, -0.045), (0.045, 0.62, 0.045), BLACK, detail=True, shape="cylinder", segments=8, taper=[0.3, 0.3], front="-y"))  # (the furled canopy)
    p.add(box((-0.047, 0.4, -0.047), (0.047, 0.43, 0.047), CHROME, detail=True, shape="cylinder", segments=8, front="+y", paintable=False))  # (its strap)
    p.add(box((-0.006, 0.62, -0.006), (0.006, 0.72, 0.006), CHROME, detail=True, paintable=False))  # (the ferrule)
    p.add(box((-0.03, 0.12, -0.03), (0.03, 0.6, 0.03), hidden=True))
    base_socket(p)
    p.stats(1, 20, 0, 0.5)
    return "", p


def belisha():
    p = Part("Belisha Beacon", SMALL, SECTION, "A Belisha beacon, blinking amber. Everybody stops for it. Everybody.", color=AMBER, group=GROUP)
    tall = 0.8
    p.add(box((-0.05, 0, -0.05), (0.05, 0.037, 0.05), IRON))
    for k in range(6):  # (the pole, in black and white bands)
        y0 = 0.037 + k * (tall - 0.037) / 6
        p.add(box((-0.022, y0, -0.022), (0.022, y0 + (tall - 0.037) / 6, 0.022), BLACK if k % 2 == 0 else WHITE, detail=k > 0, paintable=False))
    lit(p, (-0.08, tall, -0.08), (0.08, tall + 0.16, 0.08), AMBER, shape="sphere", segments=10)
    base_socket(p)
    p.stats(2, 20, 0, 0.5)
    return "", p


def queue_post():
    p = Part("Queue Post", SMALL, SECTION, "A queue post and a length of its belt. Wherever it stands, a queue forms behind it, politely.", color=CHROME, group=GROUP)
    p.add(box((-0.12, 0, -0.12), (0.12, 0.037, 0.12), BLACK, detail=True, shape="cylinder", segments=12, front="+y"))  # (its weighted base)
    p.add(box((-0.09, 0, -0.09), (0.09, 0.037, 0.09), hidden=True))
    p.add(box((-0.025, 0.037, -0.025), (0.025, 0.9, 0.025), CHROME))
    p.add(box((-0.04, 0.9, -0.04), (0.04, 0.95, 0.04), CHROME, detail=True))  # (the head the belt reels from)
    p.add(box((0.042, 0.86, -0.004), (0.36, 0.92, 0.004), "#2a3f8f", detail=True, paintable=False))  # (the belt, pulled out)
    p.add(box((0.36, 0.84, -0.012), (0.39, 0.94, 0.012), CHROME, detail=True, paintable=False))  # (its clip)
    base_socket(p)
    p.stats(3, 20, 0, 0.5)
    return "", p


def pillar_box():
    p = Part("Pillar Box", SMALL, "Furniture", "A pillar box. Last collection was some time ago. What's inside is loot, and the odd postcard.",
             role="storage", tool="wrench", color=RED, group="Vehicle Body")
    r, tall, wall = 0.26, 1.1, 0.04
    p.add(box((-r - 0.01, 0, -r - 0.01), (r + 0.01, 0.08, r + 0.01), BLACK, detail=True, shape="cylinder", segments=12, front="+y"))  # (the black foot)
    p.add(box((-r, 0.082, -r), (r, tall - 0.12, r), RED, detail=True, shape="cylinder", segments=12, front="+y"))  # (the pillar)
    p.add(box((-r - 0.012, tall - 0.12, -r - 0.012), (r + 0.012, tall - 0.08, r + 0.012), RED_DARK, detail=True, shape="cylinder", segments=12, front="+y", paintable=False))  # (the cap's rim)
    p.add(box((-r + 0.01, tall - 0.08, -r + 0.01), (r - 0.01, tall, r - 0.01), RED, detail=True, shape="sphere", segments=12))  # (the dome)
    p.add(box((-0.11, tall - 0.27, -r - 0.012), (0.11, tall - 0.25, -r + 0.03), BLACK, detail=True, paintable=False))  # (the slot)
    p.add(box((-0.07, tall - 0.42, -r - 0.008), (0.07, tall - 0.33, -r + 0.03), WHITE, detail=True, paintable=False))  # (the collection plate)
    p.add(box((-0.03, 0.55, -r - 0.008), (0.03, 0.62, -r + 0.03), GOLD, detail=True, paintable=False))  # (the door's lock)
    # Solid: its foot, its lid and four walls round the loot (a slot can't run into a solid box).
    i = 0.9 * r
    p.add(box((-i, 0, -i), (i, 0.08, i), hidden=True))
    p.add(box((-i, tall - 0.12, -i), (i, tall - 0.08, i), hidden=True))
    for s in (-1, 1):
        lo, hi = sorted((s * (i - wall), s * i))
        p.add(box((lo, 0.082, -i), (hi, tall - 0.122, i), hidden=True))
        p.add(box((-i + wall + 0.002, 0.082, lo), (i - wall - 0.002, tall - 0.122, hi), hidden=True))
    feet(p, (-0.125, 0.125), (0.0,))
    p.stats(60, 220, 3, 2.0)
    k = i - wall - 0.006
    p.set(storage={"slots": [{"min": [-k, 0.088, -k], "max": [k, 0.5, k], "label": "post"},
                             {"min": [-k, 0.51, -k], "max": [k, tall - 0.128, k], "label": "post"}]})
    return "", p


def phone_box():
    p = Part("Phone Box", SMALL, "Furniture", "A red telephone box, cut down to fit. The phone's been out of order for years; the shelf under it hasn't.",
             role="storage", tool="wrench", color=RED, group="Vehicle Body")
    w, tall, post_w = 0.3, 1.6, 0.05
    p.add(box((-w - 0.01, 0, -w - 0.01), (w + 0.01, 0.06, w + 0.01), RED_DARK))  # (the plinth)
    for x in (-1, 1):  # (the four corner posts)
        for z in (-1, 1):
            p.add(box((x * w - post_w, 0.062, z * w - post_w), (x * w + post_w, tall - 0.16, z * w + post_w), RED))
    # The glazing, each face a pane between the posts with its glazing bars; the back face is solid (it stands against a wall).
    for face in ((0, -1), (1, 0), (-1, 0)):
        fx, fz = face
        if fx == 0:
            p.add(box((-w + post_w + 0.002, 0.3, fz * w - 0.006), (w - post_w - 0.002, tall - 0.2, fz * w + 0.006), GLASS_BLUE, detail=True, paintable=False, glass=True))
            p.add(box((-0.012, 0.3, fz * w - 0.01), (0.012, tall - 0.2, fz * w + 0.01), RED, detail=True, paintable=False))
        else:
            p.add(box((fx * w - 0.006, 0.3, -w + post_w + 0.002), (fx * w + 0.006, tall - 0.2, w - post_w - 0.002), GLASS_BLUE, detail=True, paintable=False, glass=True))
            p.add(box((fx * w - 0.01, 0.3, -0.012), (fx * w + 0.01, tall - 0.2, 0.012), RED, detail=True, paintable=False))
    p.add(box((-w + post_w + 0.002, 0.062, w - 0.03), (w - post_w - 0.002, tall - 0.162, w + 0.002), RED))  # (the back)
    for fx, fz in ((0, -1), (1, 0), (-1, 0)):  # (the kick panels under the glass)
        if fx == 0:
            p.add(box((-w + post_w + 0.002, 0.062, fz * w - 0.01), (w - post_w - 0.002, 0.298, fz * w + 0.01), RED))
        else:
            p.add(box((fx * w - 0.01, 0.062, -w + post_w + 0.002), (fx * w + 0.01, 0.298, w - post_w - 0.002), RED))
    p.add(box((-w - 0.02, tall - 0.16, -w - 0.02), (w + 0.02, tall - 0.06, w + 0.02), RED))  # (the roof's band, where the sign goes)
    p.add(box((-0.14, tall - 0.145, -w - 0.024), (0.14, tall - 0.075, -w - 0.018), WHITE, detail=True, paintable=False))  # (the sign, lit)
    p.add(box((-w + 0.02, tall - 0.058, -w + 0.02), (w - 0.02, tall, w - 0.02), RED, detail=True, shape="sphere", segments=12))  # (the domed roof)
    p.add(box((-0.08, 0.95, w - 0.1), (0.08, 1.15, w - 0.032), BLACK, detail=True, paintable=False))  # (the phone, on the back wall)
    p.add(box((-0.2, 0.62, w - 0.2), (0.2, 0.65, w - 0.032), WOOD, detail=True, paintable=False))  # (the shelf)
    p.add(box((-w, 0.0, -w), (w, 0.06, w), hidden=True))
    feet(p, (-0.125, 0.125), (0.0,))
    p.stats(80, 260, 3, 2.0)
    k = w - post_w - 0.01
    p.set(storage={"slots": [{"min": [-k, 0.07, -k], "max": [k, 0.6, round(w - 0.04, 3)], "label": "under the shelf"}]})
    return "", p


def tea_urn():
    p = Part("Tea Urn", SMALL, "Furniture", "A tea urn on a trolley. When it runs dry, everything stops.", tool="wrench", color=CHROME, group="Vehicle Body")
    p.add(box((-0.25, 0, -0.2), (0.25, 0.04, 0.2), IRON))  # (the trolley's bottom shelf)
    for x in (-0.23, 0.23):
        p.add(box((x - 0.015, 0.042, -0.18), (x + 0.015, 0.7, -0.15), IRON))
        p.add(box((x - 0.015, 0.042, 0.15), (x + 0.015, 0.7, 0.18), IRON))
    p.add(box((-0.25, 0.702, -0.2), (0.25, 0.74, 0.2), WOOD))  # (its top)
    p.add(box((-0.13, 0.742, -0.12), (0.13, 1.18, 0.12), CHROME, detail=True, shape="cylinder", segments=12, front="+y"))  # (the urn)
    p.add(box((-0.1, 1.182, -0.09), (0.1, 1.22, 0.09), BLACK, detail=True, shape="cylinder", segments=12, front="+y", paintable=False))  # (its lid)
    p.add(box((-0.02, 0.84, -0.15), (0.02, 0.88, -0.11), BLACK, detail=True, paintable=False))  # (the tap)
    lit(p, (-0.01, 1.0, -0.126), (0.01, 1.02, -0.118), RED)  # (the "on" light: always on)
    for k, x in enumerate((-0.2, -0.14, 0.15)):  # (cups, waiting)
        p.add(box((x - 0.025, 0.742, 0.1 - k * 0.04), (x + 0.025, 0.8, 0.15 - k * 0.04), WHITE, detail=True, shape="cylinder", segments=8, front="+y", paintable=False))
    p.add(box((-0.25, 0.04, -0.2), (0.25, 0.74, 0.2), hidden=True))
    feet(p, (-0.125, 0.125), (0.0,))
    p.stats(25, 50, 0, 1.0)
    return "", p


def main():
    return write_parts(OUT, [
        union_flag(),
        teapot(),
        cone(),
        gnome(),
        rain_gauge(),
        brolly(),
        belisha(),
        queue_post(),
        pillar_box(),
        phone_box(),
        tea_urn(),
    ])


if __name__ == "__main__":
    sys.exit(main())
