#!/usr/bin/env python3
"""Writes the Video Screens and the Music Player (Constructs/Parts/Media).

docs/sandbox/MEDIA.md. A screen is a dark case, a bezel standing out of it and the picture's panel on the bezel, a few millimetres apart so
no two faces share a plane. The Music Player is a box with a small screen: YouTube's player stays on view whatever a part is for.

Run it from the repo root: python3 -I tools/constructs/media_parts.py (--check only compares).
Then python3 -I tools/constructs/assembling.py, for the recipes. Fix a part here, never in its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Media")
SECTION = "Media"
CASE, BEZEL, SPEAKER, GRILLE = "#23272c", "#14181c", "#30363d", "#4a525a"
VOLUME = {"key": "volume", "label": "Volume", "group": "General", "min": 0, "max": 1, "step": 0.05, "default": 0.8,
          "tooltip": "How loud it plays, before distance."}
LOOP = {"key": "loop", "label": "Loop the list", "group": "General", "type": "toggle", "default": False,
        "tooltip": "After the last track it starts again from the top."}


def media(hear, near, width=None, height=None):
    m = {"hear": hear, "near": near}
    if width:
        m["width"], m["height"] = width, height
    return m


def video(name, cls, w, h, range_m, hear, near, power, mass):
    desc = f"Plays YouTube links and the game's own tracks: paste a link with the configure tool. Seen from {range_m:g} m, heard to {hear:g} m."
    p = Part(name, cls, SECTION, desc, role="panel", tool="wrench" if mass <= 40 else "welder", group="Logic", color=CASE)
    t = 0.05 if cls is SMALL else 0.12
    rim = 0.012 if cls is SMALL else 0.03
    proud = 0.004 if cls is SMALL else 0.008
    p.add(box((-w / 2, -h / 2, 0), (w / 2, h / 2, t)))
    p.add(box((-w / 2 + rim, -h / 2 + rim, -proud), (w / 2 - rim, h / 2 - rim, 0.002), BEZEL, detail=True))
    p.sock("back", (0, 0, t), "+z")
    p.sock("-y", (0, -h / 2, t / 2), "-y")
    p.sock("+y", (0, h / 2, t / 2), "+y")
    p.sock("-x", (-w / 2, 0, t / 2), "-x")
    p.sock("+x", (w / 2, 0, t / 2), "+x")
    p.stats(mass, 50, 1, 1)
    pw, ph = w - 2 * rim, h - 2 * rim
    pw = min(pw, round(ph * 16 / 9, 3))
    p.set(power=power, config=[VOLUME, LOOP], centreSockets=False, media=media(hear, near),
          panels=[{"source": "media", "at": [0, 0, round(-proud - 0.003, 3)], "size": [round(pw, 3), round(pw * 9 / 16, 3)], "face": "-z", "up": "+y", "rows": 3, "range": range_m}])
    return p


def music():
    desc = "Plays a list of YouTube links and the game's own tracks: build the list with the configure tool. Heard to 50 m. The game's tracks need no browser."
    p = Part("Music Player", SMALL, SECTION, desc, role="panel", tool="wrench", group="Logic", color=SPEAKER)
    w, h, t = 0.5, 0.26, 0.16
    p.add(box((-w / 2, 0, 0), (w / 2, h, t)))
    for x in (-0.17, 0.17):
        p.add(box((x - 0.05, 0.08, -0.004), (x + 0.05, 0.18, 0.002), GRILLE, detail=True))
    p.add(box((-0.1, 0.045, -0.004), (0.1, 0.215, 0.002), BEZEL, detail=True))
    p.sock("bottom", (0, 0, t / 2), "-y")
    p.sock("back", (0, h / 2, t), "+z")
    p.sock("top", (0, h, t / 2), "+y")
    p.stats(6, 50, 1, 1)
    p.set(power=10, config=[VOLUME, LOOP], centreSockets=False, media=media(50, 5, 356, 200),
          panels=[{"source": "media", "at": [0, 0.13, -0.007], "size": [0.19, 0.107], "face": "-z", "up": "+y", "rows": 3, "range": 12}])
    return p


def main():
    parts = [
        ("", music()),
        ("", video("Video Screen", SMALL, 1.0, 0.6, 25, 40, 4, 40, 6)),
        ("", video("Medium Video Screen", MEDIUM, 3, 1.75, 60, 80, 8, 150, 60)),
        ("", video("Large Video Screen", MEDIUM, 8, 4.6, 150, 160, 16, 400, 240)),
    ]
    return write_parts(OUT, parts)


if __name__ == "__main__":
    sys.exit(main())
