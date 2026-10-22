#!/usr/bin/env python3
"""Writes the London map's pack decals (Maps/World/Decals/London/): shop fascias, the station name board, street name
plates, road text (LOOK RIGHT, BUS STOP) and two posters, all with invented names. Wide signs are cut into square-pixel
tiles (one decal each, 128 x 128 px in the atlas) that the map lines up side by side, so the lettering stays sharp.

Run from the repository root:  python3 tools/world/london_decals.py
`SIGNS` is imported by tools/world/london.py to place the tiles.
"""
import json
import math
import os

OUT = os.path.join("Maps", "World", "Decals", "London")
FONTS = {
    "sans": "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "cond": "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf",
    "serif": "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf",
    "poppins": "/usr/share/fonts/truetype/google-fonts/Poppins-Bold.ttf",
}

# key: (decal base name, width m, height m, background, text colour, lines, font, surfaces, border colour)
SIGNS = {
    "station": ("London Station Board", 6.6, 0.9, "#1a2a52", "#f4f2ea", ["WHITMORE ROAD"], "sans", "wall", "#f4f2ea"),
    "barber": ("London Barber Fascia", 3.6, 0.55, "#14243a", "#f0d878", ["SHARP LADS"], "serif", "wall", None),
    "shop": ("London Corner Shop Fascia", 8.0, 0.62, "#1e5a32", "#f8f4e8", ["PATEL & SON  FOOD - WINE - NEWS"], "cond", "wall", "#e8c040"),
    "pub": ("London Pub Fascia", 6.0, 0.6, "#173a2a", "#e8c870", ["THE LAMPLIGHTER"], "serif", "wall", None),
    "bar": ("London Arch Bar Sign", 6.6, 0.58, "#1e2a3a", "#f0a040", ["ARCH SEVEN TAPROOM"], "poppins", "wall", None),
    "garage": ("London Garage Sign", 7.0, 0.48, "#e8e4da", "#a01818", ["VIADUCT MOTORS  MOT - TYRES"], "cond", "wall", "#a01818"),
    "workshop": ("London Workshop Sign", 3.2, 0.5, "#2a2a2a", "#e8e8e0", ["BOW JOINERY"], "cond", "wall", None),
    "plate_lane": ("London Street Plate Arches", 1.5, 0.36, "#f4f4f0", "#141414", ["ARCHES LANE"], "sans", "wall", "#141414"),
    "plate_canal": ("London Street Plate Canal", 1.5, 0.36, "#f4f4f0", "#141414", ["CANAL STREET"], "sans", "wall", "#141414"),
    "plate_high": ("London Street Plate High", 1.7, 0.36, "#f4f4f0", "#141414", ["WHITMORE ROAD"], "sans", "wall", "#141414"),
    "towpath": ("London Towpath Sign", 1.6, 0.42, "#1e3a6a", "#f4f2ea", ["WHITMORE CUT"], "sans", "wall", "#f4f2ea"),
    "look": ("London Look Right", 1.8, 0.42, None, "#f4f4f0", ["LOOK RIGHT"], "sans", "floor", None),
    "busstop": ("London Bus Stop Marking", 3.4, 0.7, None, "#e8c020", ["BUS STOP"], "sans", "floor", None),
}
POSTERS = [
    ("London Poster Tea", "BRITE", "TEA", "#c83a2a", "#f8f0d8"),
    ("London Poster Gig", "LIVE", "FRI", "#1e1e3a", "#f0c040"),
]


def tiles(key):
    name, w, h = SIGNS[key][:3]
    n = max(1, int(math.ceil(w / h / 1.15)))
    return [f"{name} {i + 1}" for i in range(n)], w / n, h


def render(key):
    from PIL import Image, ImageDraw, ImageFont
    name, w, h, bg, fg, lines, font, surf, border = SIGNS[key]
    names, tw, th = tiles(key)
    n = len(names)
    W, H = 128 * n, 128
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0) if bg is None else bg)
    d = ImageDraw.Draw(img)
    if border:
        d.rectangle([3, 3, W - 4, H - 4], outline=border, width=6)
    text = lines[0]
    size = 100
    while size > 10:
        f = ImageFont.truetype(FONTS[font], size)
        bb = d.textbbox((0, 0), text, font=f)
        if bb[2] - bb[0] <= W - 40 and bb[3] - bb[1] <= H - 40:
            break
        size -= 2
    bb = d.textbbox((0, 0), text, font=f)
    x = (W - (bb[2] - bb[0])) / 2 - bb[0]
    y = (H - (bb[3] - bb[1])) / 2 - bb[1]
    d.text((x, y), text, font=f, fill=fg)
    os.makedirs(OUT, exist_ok=True)
    for i, nm in enumerate(names):
        tile = img.crop((i * 128, 0, (i + 1) * 128, 128))
        fn = nm.replace(" ", "_").lower()
        tile.save(os.path.join(OUT, fn + ".png"))
        dec = {
            "format": "call-of-slop-decal/1", "name": nm, "category": "London",
            "description": f"Tile {i + 1} of {n} of the London map's {SIGNS[key][0].replace('London ', '').lower()} ({' '.join(lines)}; an invented name).",
            "author": "Call of Slop", "size": [round(tw, 3), round(th, 3)], "rotation": 0, "blend": "alpha",
            "surfaces": "floor" if surf == "floor" else "wall", "fadeDistance": 70 if surf == "wall" else 45, "image": fn + ".png",
        }
        with open(os.path.join(OUT, fn + ".json"), "w") as fo:
            json.dump(dec, fo, indent=1)
            fo.write("\n")


def posters():
    from PIL import Image, ImageDraw, ImageFont
    for nm, a, b, bg, fg in POSTERS:
        img = Image.new("RGBA", (128, 128), bg)
        d = ImageDraw.Draw(img)
        d.rectangle([2, 2, 125, 125], outline="#f4f0e4", width=4)
        f = ImageFont.truetype(FONTS["poppins"], 34)
        for t, yy in ((a, 22), (b, 64)):
            bb = d.textbbox((0, 0), t, font=f)
            d.text(((128 - (bb[2] - bb[0])) / 2 - bb[0], yy), t, font=f, fill=fg)
        d.ellipse([88, 96, 116, 124], fill=fg)
        fn = nm.replace(" ", "_").lower()
        img.save(os.path.join(OUT, fn + ".png"))
        dec = {"format": "call-of-slop-decal/1", "name": nm, "category": "London", "description": f"An invented advertising poster ({a} {b}).",
               "author": "Call of Slop", "size": [0.8, 1.15], "rotation": 0, "blend": "alpha", "surfaces": "wall", "fadeDistance": 50, "image": fn + ".png"}
        with open(os.path.join(OUT, fn + ".json"), "w") as fo:
            json.dump(dec, fo, indent=1)
            fo.write("\n")


if __name__ == "__main__":
    for k in SIGNS:
        render(k)
    posters()
    print(len(os.listdir(OUT)) // 2, "decals in", OUT)
