#!/usr/bin/env python3
"""The game's icon: an orange hex nut with a blue core and a crosshair, on dark steel.
Writes Branding/CallOfSlop.ico (the exe's icon, 16 to 256 px) and Branding/CallOfSlop.png (the window's, 256 px)."""
import math
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Branding")
BACK, RIM = (24, 30, 38, 255), (58, 70, 84, 255)
ORANGE, ORANGE_DARK = (240, 148, 32, 255), (176, 96, 16, 255)
BLUE, WHITE = (60, 120, 210, 255), (236, 240, 246, 255)


def hexagon(c, r, turn=30):
    return [(c + r * math.cos(math.radians(turn + 60 * i)), c + r * math.sin(math.radians(turn + 60 * i))) for i in range(6)]


def draw(size):
    s = size * 4
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c = s / 2
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=s * 0.19, fill=RIM)
    d.rounded_rectangle((s * 0.035, s * 0.035, s * 0.965, s * 0.965), radius=s * 0.165, fill=BACK)
    d.polygon(hexagon(c, s * 0.43), fill=ORANGE_DARK)
    d.polygon(hexagon(c, s * 0.39), fill=ORANGE)
    r = s * 0.235
    d.ellipse((c - r, c - r, c + r, c + r), fill=BACK)
    r = s * 0.17
    d.ellipse((c - r, c - r, c + r, c + r), fill=BLUE)
    if size >= 32:  # (below this the cross is mud: the blue core reads by itself)
        w, a, b = s * 0.022, s * 0.05, s * 0.28
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            x0, y0 = c + dx * a - abs(dy) * w, c + dy * a - abs(dx) * w
            x1, y1 = c + dx * b + abs(dy) * w, c + dy * b + abs(dx) * w
            d.rectangle((min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)), fill=WHITE)
    return im.resize((size, size), Image.LANCZOS)


def main():
    sizes = [256, 128, 64, 48, 32, 24, 16]
    images = [draw(n) for n in sizes]
    images[0].save(os.path.join(OUT, "CallOfSlop.ico"), format="ICO", append_images=images[1:], sizes=[(n, n) for n in sizes])
    images[0].save(os.path.join(OUT, "CallOfSlop.png"))


if __name__ == "__main__":
    main()
