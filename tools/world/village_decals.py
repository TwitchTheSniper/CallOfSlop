#!/usr/bin/env python3
"""Writes the World Tour pack's decals for 07 Village: Maps/World/Decals/Village/*.json plus their PNG pictures.

Usage: python3 tools/world/village_decals.py      (needs Pillow; uses the DejaVu Serif Bold font from the system)

Painted signs and road text for one invented village (no real names or brands): the village shop's fascia
("VILLAGE" / "STORES"), its post office board, the pub's name board ("THE PLOUGH"), the allotments' sign, and the
yellow BUS STOP and white SLOW road markings. Every picture is drawn big and bold, because a decal gets 128 x 128
pixels whatever its size (see Decals/README.md, "Pack decals").
"""
import json
import os

from PIL import Image, ImageDraw, ImageFont

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(REPO, "Maps", "World", "Decals", "Village")
os.makedirs(OUT, exist_ok=True)
SERIF = "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"
SANS = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def font(path, size):
    try:
        return ImageFont.truetype(path, size)
    except OSError:
        return ImageFont.load_default()


def sign(fname, text_lines, size_m, bg, fg, border=None, fontpath=SERIF, px=256, road=False):
    """A PNG (px across, height in the sign's proportion, transparent outside the board) with centred text."""
    w = px
    h = max(16, int(round(px * size_m[1] / size_m[0])))
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0) if road else bg)
    d = ImageDraw.Draw(img)
    if border and not road:
        b = max(2, h // 12)
        d.rectangle([b, b, w - 1 - b, h - 1 - b], outline=border, width=max(1, b // 2))
    n = len(text_lines)
    # the biggest font that fits each line
    for i, line in enumerate(text_lines):
        box_h = (h * (0.78 if not road else 0.95)) / n
        top = h * (0.11 if not road else 0.025) + i * box_h
        fs = int(box_h * 1.1)
        while fs > 6:
            f = font(fontpath, fs)
            l, t, r, bt = d.textbbox((0, 0), line, font=f)
            if r - l <= w * (0.88 if not road else 0.98) and bt - t <= box_h * 0.9:
                break
            fs -= 1
        f = font(fontpath, fs)
        l, t, r, bt = d.textbbox((0, 0), line, font=f)
        d.text(((w - (r - l)) / 2 - l, top + (box_h - (bt - t)) / 2 - t), line, font=f, fill=fg)
    img.save(os.path.join(OUT, fname))


def decal(fname, name, desc, size, image, surfaces, fade=60):
    d = {"format": "call-of-slop-decal/1", "name": name, "description": desc, "author": "Call of Slop",
         "size": size, "rotation": 0, "blend": "alpha", "surfaces": surfaces, "fadeDistance": fade, "image": image}
    with open(os.path.join(OUT, fname), "w") as f:
        f.write(json.dumps(d, indent=2) + "\n")


GREEN = (31, 74, 52, 255)
GOLD = (222, 190, 108, 255)
CREAM = (238, 228, 200, 255)
BLACK = (24, 24, 22, 255)
RED = (176, 24, 28, 255)
WHITE = (240, 240, 234, 255)
YELLOW = (232, 190, 40, 255)

sign("village_stores_left.png", ["VILLAGE"], (2.6, 0.46), GREEN, GOLD, border=GOLD)
decal("VillageStoresSignLeft.json", "Village Stores Sign Left", "The village shop's fascia, first half: VILLAGE in gold on bottle green.",
      [2.6, 0.46], "village_stores_left.png", ["wall"])
sign("village_stores_right.png", ["STORES"], (2.6, 0.46), GREEN, GOLD, border=GOLD)
decal("VillageStoresSignRight.json", "Village Stores Sign Right", "The village shop's fascia, second half: STORES in gold on bottle green.",
      [2.6, 0.46], "village_stores_right.png", ["wall"])
sign("post_office.png", ["POST", "OFFICE"], (1.0, 0.62), RED, WHITE, border=WHITE, fontpath=SANS)
decal("PostOfficeSign.json", "Post Office Sign", "A red post office board in white lettering (no crown, no logo).",
      [1.0, 0.62], "post_office.png", ["wall"])
sign("the_plough.png", ["THE PLOUGH"], (3.6, 0.5), BLACK, CREAM, border=CREAM)
decal("PubNameBoard.json", "Pub Name Board", "The pub's name board over its door: THE PLOUGH, cream on black.",
      [3.6, 0.5], "the_plough.png", ["wall"])
sign("allotments.png", ["ALLOTMENTS"], (1.6, 0.36), (40, 84, 48, 255), WHITE, border=WHITE, fontpath=SANS)
decal("AllotmentsSign.json", "Allotments Sign", "A small green ALLOTMENTS sign for the gate.", [1.6, 0.36], "allotments.png", ["wall"])
sign("bus_stop.png", ["BUS STOP"], (5.0, 1.0), None, YELLOW, fontpath=SANS, road=True)
decal("BusStopRoadMarking.json", "Bus Stop Road Marking", "BUS STOP in yellow letters on the road at a bus stop (reads from the south side; runs east-west).",
      [5.0, 1.0], "bus_stop.png", ["floor"], fade=80)
sign("slow.png", ["SLOW"], (3.0, 1.1), None, WHITE, fontpath=SANS, road=True)
decal("SlowRoadMarking.json", "Slow Road Marking", "SLOW in white letters across a lane (reads from the south side; runs east-west).",
      [3.0, 1.1], "slow.png", ["floor"], fade=80)
print("wrote", len([f for f in os.listdir(OUT) if f.endswith(".json")]), "decals to", OUT)
