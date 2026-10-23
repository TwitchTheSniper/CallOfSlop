#!/usr/bin/env python3
"""Writes the Space pack's sky-station decals (Maps/Space/Decals/SkyStation/): landing pad rings and glyphs, pad
numbers, deck panel seams, walkway arrows, the station's wall signs (platform names) and the invented Halcyon Gas
Combine logo. All original artwork. Every picture is 128 x 128 px (the atlas size); wide signs are cut into square tiles
the map lines up side by side (SIGNS, used by tools/space/skystation.py).

    python3 tools/space/skystation_decals.py          (run from the repository root)
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join("Maps", "Space", "Decals", "SkyStation")
BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
COND = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"
CAT = "SkyStation"

# key: (decal base name, width m, height m, background, text colour, text, border colour)
SIGNS = {
    "north": ("Stratos Sign North Dock", 4.8, 0.8, "#1c3d4a", "#e8f4f2", "NORTH DOCK", "#6fe0e8"),
    "south": ("Stratos Sign South Dock", 4.8, 0.8, "#4a1f1c", "#f4ece8", "SOUTH DOCK", "#ff7a5a"),
    "refinery": ("Stratos Sign Refinery", 4.0, 0.7, "#2a2a2e", "#ffc070", "REFINERY", None),
    "hub": ("Stratos Sign Hub", 2.8, 0.7, "#2a2a2e", "#f0f0ea", "THE HUB", None),
    "hangar": ("Stratos Sign Hangar", 3.4, 0.7, "#2a2a2e", "#f0f0ea", "HANGAR 2", None),
    "quarters": ("Stratos Sign Quarters", 3.4, 0.7, "#2a2a2e", "#f0f0ea", "QUARTERS", None),
    "tanks": ("Stratos Sign Tank Farm", 3.6, 0.7, "#2a2a2e", "#ffc070", "TANK FARM", None),
    "comms": ("Stratos Sign Comms", 2.8, 0.7, "#2a2a2e", "#f0f0ea", "COMMS", None),
    "pad_e": ("Stratos Sign East Pad", 3.4, 0.7, "#2a2a2e", "#f0f0ea", "EAST PAD", None),
    "pad_w": ("Stratos Sign West Pad", 3.4, 0.7, "#2a2a2e", "#f0f0ea", "WEST PAD", None),
    "danger": ("Stratos Sign Mind The Drop", 3.6, 0.6, "#e0b030", "#1c1c1c", "MIND THE DROP", None),
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
         "size": size, "rotation": rotation, "blend": blend, "surfaces": surfaces, "image": fn + ".png"}
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
    if key == "danger":
        for k in range(-2, n * 6 + 2):
            x = k * 26
            d.polygon([(x, 0), (x + 12, 0), (x - 8, 20), (x - 20, 20)], fill="#1c1c1c")
            d.polygon([(x, 108), (x + 12, 108), (x - 8, 128), (x - 20, 128)], fill="#1c1c1c")
    f = fit_font(d, text, BOLD, W - 44, H - 52)
    centred(d, text, f, W, H, fg)
    for i, nm in enumerate(names):
        save(nm, img.crop((i * 128, 0, (i + 1) * 128, 128)), [round(tw, 3), round(th, 3)], "wall",
             f"Tile {i + 1} of {n} of the sky station's '{text}' sign (an invented place name).", fade=80)


def pad_ring():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse([16, 16, S - 16, S - 16], outline="#f0ece0", width=22)
    d.ellipse([70, 70, S - 70, S - 70], outline="#e8a030", width=10)
    for k in range(24):
        a = 2 * math.pi * k / 24
        r0, r1 = (S / 2 - 48, S / 2 - 30) if k % 2 else (S / 2 - 58, S / 2 - 30)
        d.line([(c + r0 * math.cos(a), c + r0 * math.sin(a)), (c + r1 * math.cos(a), c + r1 * math.sin(a))], fill="#f0ece0", width=8)
    img = img.resize((128, 128), Image.LANCZOS)
    save("Stratos Pad Ring", img, 14, "floor", "A landing pad's ring marking: a white outer ring with ticks and an amber inner ring.", fade=150)


def pad_glyph():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    red = "#c83a2a"
    c = S / 2
    d.ellipse([c - 120, c - 120, c + 120, c + 120], outline=red, width=26)
    d.line([(c - 200, c), (c + 200, c)], fill=red, width=20)
    d.line([(c, c - 70), (c, c + 70)], fill=red, width=20)
    d.polygon([(c + 230, c), (c + 180, c - 34), (c + 180, c + 34)], fill=red)
    d.ellipse([c - 230, c - 24, c - 182, c + 24], fill=red)
    d.rectangle([c - 40, c - 200, c + 40, c - 172], fill=red)
    d.rectangle([c - 40, c + 172, c + 40, c + 200], fill=red)
    img = img.resize((128, 128), Image.LANCZOS)
    save("Stratos Pad Glyph", img, 7, "floor", "A red landing-guidance glyph for the middle of a pad: a ring on a crossed line with an arrow (original).",
         fade=150, rotation=0)


def pad_numbers():
    for n in range(1, 5):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        f = fit_font(d, f"P{n}", BOLD, 110, 90)
        centred(d, f"P{n}", f, 128, 128, "#f0ece0")
        save(f"Stratos Pad Number {n}", img, 2.6, "floor", f"A big white stencilled pad number, P{n}.", fade=120)


def panels():
    img = Image.new("RGBA", (128, 128), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for k in (0, 64):
        d.line([(k, 0), (k, 127)], fill=(120, 116, 110, 255), width=3)
        d.line([(0, k), (127, k)], fill=(120, 116, 110, 255), width=3)
    for (x, y) in ((8, 8), (56, 8), (8, 56), (56, 56), (72, 8), (120, 8), (72, 56), (120, 56),
                   (8, 72), (56, 72), (8, 120), (56, 120), (72, 72), (120, 72), (72, 120), (120, 120)):
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(140, 136, 128, 255))
    save("Stratos Deck Panels", img, 4, "floor", "Deck plate seams and rivets on a 2 m grid (darkens the deck).", blend="multiply", fade=60)


def arrow():
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for k in range(3):
        y = 22 + k * 34
        d.polygon([(64, y), (108, y + 30), (94, y + 30), (64, y + 10), (34, y + 30), (20, y + 30)], fill="#f0c040")
    save("Stratos Walkway Arrow", img, 1.6, "floor", "Three amber chevrons pointing north along a walkway (turn with rotation).", fade=70)


def logo():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    for k, col in enumerate(("#ffb24a", "#e08a3a", "#c06a30")):
        r = 150 - k * 38
        d.arc([c - r, 210 - r, c + r, 210 + r], 180, 360, fill=col, width=26)
    d.rectangle([60, 214, S - 60, 236], fill="#f0ece0")
    f = fit_font(d, "HALCYON", BOLD, S - 70, 110)
    centred(d, "HALCYON", f, S, 140, "#f0ece0", dy=270)
    f2 = fit_font(d, "GAS COMBINE", COND, S - 150, 60)
    centred(d, "GAS COMBINE", f2, S, 60, "#e8a040", dy=440)
    img = img.resize((128, 128), Image.LANCZOS)
    save("Halcyon Logo", img, 2.4, ["wall", "floor"], "The invented Halcyon Gas Combine logo: a setting sun of three arcs over a horizon line.", fade=110)


def team_mark(name, col, desc):
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse([20, 20, S - 20, S - 20], outline=col, width=30)
    d.polygon([(c, 90), (c + 150, c + 110), (c, c + 40), (c - 150, c + 110)], fill=col)
    img = img.resize((128, 128), Image.LANCZOS)
    save(name, img, 5, "floor", desc, fade=140)


def main():
    for k in SIGNS:
        sign(k)
    pad_ring()
    pad_glyph()
    pad_numbers()
    panels()
    arrow()
    logo()
    team_mark("Stratos North Mark", "#5fd0d8", "The north dock's deck mark: a cyan ring round an arrowhead (an invented emblem).")
    team_mark("Stratos South Mark", "#ff6a4a", "The south dock's deck mark: a red ring round an arrowhead (an invented emblem).")
    print("wrote", len(os.listdir(OUT)) // 2, "decals to", OUT)


if __name__ == "__main__":
    main()
