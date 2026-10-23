#!/usr/bin/env python3
"""Writes the Space pack's gravity-map decals (Maps/Space/Decals/Gravity/): the invented Verrin Salvage yard mark, the
gravity-fault warning sign, low-gravity and hull signs, a plating-grid floor seam and an arrow glyph. All original
artwork, 128 x 128 px per tile; wide signs are cut into square tiles the maps line up side by side (SIGNS).

    python3 tools/space/gravity_decals.py          (run from the repository root)
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join("Maps", "Space", "Decals", "Gravity")
BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
CAT = "Gravity"

# key: (decal base name, width m, height m, background, text colour, text, border colour)
SIGNS = {
    "fault": ("Gravity Sign Plating Fault", 3.6, 0.6, "#e0b030", "#1c1c1c", "GRAVITY PLATING FAULT", None),
    "lowg": ("Gravity Sign Low Gravity", 3.0, 0.6, "#1c3d4a", "#e8f4f2", "LOW GRAVITY", "#6fe0e8"),
    "hull": ("Gravity Sign Hull Plating", 3.0, 0.6, "#4a1f1c", "#f4ece8", "HULL PLATING LIVE", "#ff7a5a"),
    "rock": ("Gravity Sign The Rock", 2.4, 0.6, "#2a2a2e", "#f0f0ea", "THE ROCK", None),
    "hold": ("Gravity Sign Cargo Hold", 2.4, 0.6, "#2a2a2e", "#ffc070", "CARGO HOLD", None),
    "mast": ("Gravity Sign Mooring Mast", 3.0, 0.6, "#2a2a2e", "#f0f0ea", "MOORING MAST", None),
    "shard": ("Gravity Sign Shard Tether", 3.0, 0.6, "#2a2a2e", "#f0f0ea", "SHARD TETHER", None),
}


def tiles(key):
    name, w, h = SIGNS[key][:3]
    n = max(1, int(math.ceil(w / h / 1.15)))
    return [f"{name} {i + 1}" for i in range(n)], w / n, h


def save(name, img, size, surfaces, desc, blend="alpha", rotation=0, fade=None, **extra):
    os.makedirs(OUT, exist_ok=True)
    fn = name.replace(" ", "_").lower()
    img.save(os.path.join(OUT, fn + ".png"))
    d = {"format": "call-of-slop-decal/1", "name": name, "category": CAT, "description": desc, "author": "Call of Slop",
         "size": size, "rotation": rotation, "blend": blend, "surfaces": surfaces if isinstance(surfaces, list) else [surfaces], "image": fn + ".png"}
    if fade:
        d["fadeDistance"] = fade
    d.update(extra)
    with open(os.path.join(OUT, fn + ".json"), "w") as f:
        json.dump(d, f, indent=1)
        f.write("\n")


def fit_font(d, text, path, wmax, hmax, start=100):
    size = start
    while size > 8:
        f = ImageFont.truetype(path, size)
        bb = d.textbbox((0, 0), text, font=f)
        if bb[2] - bb[0] <= wmax and bb[3] - bb[1] <= hmax:
            return f
        size -= 2
    return ImageFont.truetype(path, 8)


def centred(d, text, f, W, H, fill, dy=0):
    bb = d.textbbox((0, 0), text, font=f)
    d.text(((W - (bb[2] - bb[0])) / 2 - bb[0], (H - (bb[3] - bb[1])) / 2 - bb[1] + dy), text, font=f, fill=fill)


def sign(key):
    name, w, h, bg, fg, text, border = SIGNS[key]
    names, tw, th = tiles(key)
    n = len(names)
    W, H = 128 * n, 128
    img = Image.new("RGBA", (W, H), bg)
    d = ImageDraw.Draw(img)
    if border:
        d.rectangle([4, 4, W - 5, H - 5], outline=border, width=7)
    if key == "fault":
        for k in range(-2, n * 6 + 2):
            x = k * 26
            d.polygon([(x, 0), (x + 12, 0), (x - 8, 20), (x - 20, 20)], fill="#1c1c1c")
            d.polygon([(x, 108), (x + 12, 108), (x - 8, 128), (x - 20, 128)], fill="#1c1c1c")
    f = fit_font(d, text, BOLD, W - 44, H - 52)
    centred(d, text, f, W, H, fg)
    for i, nm in enumerate(names):
        save(nm, img.crop((i * 128, 0, (i + 1) * 128, 128)), [round(tw, 3), round(th, 3)], "wall",
             f"Tile {i + 1} of {n} of the salvage yard's '{text}' sign (an invented place).", fade=80)


def yard_mark():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse([20, 20, S - 20, S - 20], outline="#e8a030", width=18)
    # A hook-and-rock glyph: a ring with a bar through it and a dot below (the invented Verrin Salvage mark).
    d.ellipse([150, 110, S - 150, 322], outline="#f0ece0", width=22)
    d.rectangle([c - 14, 200, c + 14, 400], fill="#f0ece0")
    d.ellipse([c - 44, 380, c + 44, 468], fill="#e8a030")
    for k in range(8):
        a = 2 * math.pi * k / 8
        d.line([(c + 200 * math.cos(a), c + 200 * math.sin(a)), (c + 228 * math.cos(a), c + 228 * math.sin(a))], fill="#f0ece0", width=10)
    img = img.resize((128, 128), Image.LANCZOS)
    save("Verrin Salvage Mark", img, 6, ["floor", "wall"], "The invented Verrin Salvage yard mark: a ring, a bar and a rock, in white and amber.", fade=120)


def plating_grid():
    S = 128
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for k in (0, 64):
        d.line([(k, 0), (k, S)], fill=(20, 20, 24, 150), width=3)
        d.line([(0, k), (S, k)], fill=(20, 20, 24, 150), width=3)
    for x in (8, 56, 72, 120):
        for y in (8, 56, 72, 120):
            d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(30, 30, 34, 170))
    d.rectangle([20, 20, 44, 44], outline=(40, 40, 44, 110), width=2)
    save("Gravity Plating Grid", img, 4, "floor", "Gravity plating seams and bolts on a 2 m grid (darkens the deck).", blend="multiply", fade=60)


def arrow(name, color, desc):
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(128, 16), (232, 120), (176, 120), (176, 240), (80, 240), (80, 120), (24, 120)], fill=color)
    img = img.resize((128, 128), Image.LANCZOS)
    save(name, img, 1.8, ["floor", "wall"], desc, fade=70)


if __name__ == "__main__":
    for k in SIGNS:
        sign(k)
    yard_mark()
    plating_grid()
    arrow("Gravity Arrow Amber", "#e8a030", "An amber arrow: this way to the gravity change (floors and walls; points up before turning).")
    arrow("Gravity Arrow Cyan", "#6fe0e8", "A cyan arrow: this way to the low-gravity hold.")
    print("wrote", OUT)
