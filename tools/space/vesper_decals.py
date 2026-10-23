#!/usr/bin/env python3
"""Writes the Space pack's Vesper Strip decals (Maps/Space/Decals/Vesper/): glowing neon shop signs (invented names,
cut into square tiles the map lines up), landing pad rings and numbers, market floor tiles, the plaza mosaic, neon
scrawls, posters and the invented Port Vesper authority emblem. All original artwork; every picture is 128 x 128 px.

    python3 tools/space/vesper_decals.py          (run from the repository root)
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.join("Maps", "Space", "Decals", "Vesper")
BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
COND = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"
CAT = "Vesper"

# key: (decal base name, width m, height m, neon colour, text)
NEON = {
    "cantina": ("Vesper Neon Cantina", 4.2, 0.9, "#ff5fb4", "KESTREL CANTINA"),
    "noodles": ("Vesper Neon Noodles", 3.6, 0.9, "#b4ff50", "ZERO-G NOODLES"),
    "repairs": ("Vesper Neon Repairs", 4.4, 0.9, "#4fe3ff", "TORQUE & SPROCKET"),
    "parts": ("Vesper Neon Parts", 3.0, 0.9, "#ffa040", "USED PARTS"),
    "customs": ("Vesper Neon Customs", 3.6, 0.9, "#ffa040", "CUSTOMS"),
    "dutyfree": ("Vesper Neon Duty Free", 3.6, 0.9, "#b070ff", "DUTY FREE"),
    "arcade": ("Vesper Neon Arcade", 3.6, 0.9, "#ff5fb4", "NOVA ARCADE"),
    "pawn": ("Vesper Neon Pawn", 2.4, 0.9, "#4fe3ff", "PAWN"),
    "lodging": ("Vesper Neon Lodging", 3.6, 0.9, "#b4ff50", "CAPSULE INN"),
    "fuel": ("Vesper Neon Fuel", 2.4, 0.9, "#ff4a3a", "FUEL"),
    "departures": ("Vesper Neon Departures", 4.4, 0.9, "#4fe3ff", "DEPARTURES"),
    "bazaar": ("Vesper Neon Bazaar", 5.2, 1.0, "#ff5fb4", "VESPER BAZAAR"),
    "west": ("Vesper Neon West Gate", 3.6, 0.9, "#4fe3ff", "WEST GATE"),
    "east": ("Vesper Neon East Gate", 3.6, 0.9, "#ffa040", "EAST GATE"),
    "open": ("Vesper Neon Open", 1.2, 0.6, "#ff4a3a", "OPEN"),
}


def tiles(key):
    name, w, h = NEON[key][:3]
    n = max(1, int(math.ceil(w / h / 1.15)))
    return [f"{name} {i + 1}" for i in range(n)], w / n, h


def save(name, img, size, surfaces, desc, blend="alpha", rotation=0, fade=None, **extra):
    os.makedirs(OUT, exist_ok=True)
    fn = name.replace(" ", "_").replace("&", "and").lower()
    img.save(os.path.join(OUT, fn + ".png"))
    d = {"format": "call-of-slop-decal/1", "name": name, "category": CAT, "description": desc, "author": "Call of Slop",
         "size": size, "rotation": rotation, "blend": blend, "surfaces": surfaces, "image": fn + ".png"}
    if fade:
        d["fadeDistance"] = fade
    d.update(extra)
    with open(os.path.join(OUT, fn + ".json"), "w") as f:
        json.dump(d, f, indent=1)
        f.write("\n")


def fit_font(d, text, path, wmax, hmax, start=120):
    size = start
    while size > 8:
        f = ImageFont.truetype(path, size)
        bb = d.textbbox((0, 0), text, font=f)
        if bb[2] - bb[0] <= wmax and bb[3] - bb[1] <= hmax:
            return f
        size -= 2
    return ImageFont.truetype(path, 8)


def centred(d, text, f, W, H, fill, dy=0, stroke=0):
    bb = d.textbbox((0, 0), text, font=f)
    d.text(((W - (bb[2] - bb[0])) / 2 - bb[0], (H - (bb[3] - bb[1])) / 2 - bb[1] + dy), text, font=f, fill=fill, stroke_width=stroke, stroke_fill=fill)


def neon(key):
    """Neon lettering: a soft glow halo under bright tubes, on a dark backing board."""
    name, w, h, col, text = NEON[key]
    names, tw, th = tiles(key)
    n = len(names)
    S = 2
    W, H = 128 * n * S, 128 * S
    board = Image.new("RGBA", (W, H), (20, 18, 28, 235))
    d = ImageDraw.Draw(board)
    d.rectangle([3, 3, W - 4, H - 4], outline=(60, 56, 70, 255), width=4)
    f = fit_font(d, text, BOLD, W - 60, H - 90, start=200)
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    centred(gd, text, f, W, H, col)
    glow = glow.filter(ImageFilter.GaussianBlur(14))
    board.alpha_composite(glow)
    board.alpha_composite(glow)
    centred(d, text, f, W, H, col, stroke=5)
    # Bright core: the same text in near-white inside the coloured tube.
    centred(d, text, f, W, H, (255, 250, 245, 235))
    img = board.resize((128 * n, 128), Image.LANCZOS)
    for i, nm in enumerate(names):
        save(nm, img.crop((i * 128, 0, (i + 1) * 128, 128)), [round(tw, 3), round(th, 3)], "wall",
             f"Tile {i + 1} of {n} of the neon sign '{text}' (an invented shop name).", fade=90)


def pad_ring():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse([14, 14, S - 14, S - 14], outline="#f0ece0", width=20)
    d.ellipse([90, 90, S - 90, S - 90], outline="#4fe3ff", width=12)
    for k in range(16):
        a = 2 * math.pi * k / 16
        r0, r1 = S / 2 - 70, S / 2 - 36
        d.line([(c + r0 * math.cos(a), c + r0 * math.sin(a)), (c + r1 * math.cos(a), c + r1 * math.sin(a))], fill="#f0ece0", width=9)
    d.rectangle([c - 16, c - 120, c + 16, c + 120], fill="#ffa040")
    d.rectangle([c - 120, c - 16, c + 120, c + 16], fill="#ffa040")
    save("Vesper Pad Ring", img.resize((128, 128), Image.LANCZOS), 14, "floor", "A landing pad's markings: a white ring with ticks, a cyan inner ring and an amber cross.", fade=150)


def pad_numbers():
    for n in range(1, 4):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.rounded_rectangle([6, 6, 121, 121], radius=18, outline="#ffa040", width=8)
        f = fit_font(d, f"{n}", BOLD, 90, 90)
        centred(d, f"{n}", f, 128, 128, "#f0ece0")
        save(f"Vesper Pad Number {n}", img, 2.6, "floor", f"A big white pad number {n} in an amber frame.", fade=120)


def market_tiles():
    S = 512
    img = Image.new("RGBA", (S, S), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for k in range(0, S + 1, 128):
        d.line([(k, 0), (k, S)], fill=(150, 140, 135, 255), width=5)
        d.line([(0, k), (S, k)], fill=(150, 140, 135, 255), width=5)
    for k in range(64, S, 128):
        d.line([(k, 0), (k, S)], fill=(200, 194, 190, 255), width=3)
        d.line([(0, k), (S, k)], fill=(200, 194, 190, 255), width=3)
    save("Vesper Market Tiles", img.resize((128, 128), Image.LANCZOS), 4, "floor", "Market paving joints on a 4 m square (darkens the floor).", blend="multiply", fade=60)


def mosaic():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    cols = ["#ff5fb4", "#4fe3ff", "#ffa040", "#b070ff", "#b4ff50"]
    for ring in range(5):
        r0, r1 = 60 + ring * 40, 95 + ring * 40
        n = 8 + ring * 4
        for k in range(n):
            a0, a1 = 360 * k / n + 2, 360 * (k + 1) / n - 2
            d.pieslice([c - r1, c - r1, c + r1, c + r1], a0, a1, fill=cols[(k + ring) % 5])
        d.ellipse([c - r0, c - r0, c + r0, c + r0], fill=(0, 0, 0, 0) if ring == 0 else None)
        if ring > 0:
            pass
    # Punch the inner disc and the rings' gaps: redraw inner discs transparent.
    for ring in range(5):
        r0 = 60 + ring * 40
        mask = Image.new("L", (S, S), 0)
        ImageDraw.Draw(mask).ellipse([c - r0, c - r0, c + r0, c + r0], fill=255)
        if ring == 0:
            img.paste((0, 0, 0, 0), mask=mask)
    d.ellipse([c - 60, c - 60, c + 60, c + 60], fill="#2a2430")
    d.ellipse([c - 44, c - 44, c + 44, c + 44], fill="#4fe3ff")
    d.regular_polygon((c, c, 30), 8, fill="#2a2430")
    img = img.filter(ImageFilter.SMOOTH)
    save("Vesper Plaza Mosaic", img.resize((128, 128), Image.LANCZOS), 12, "floor", "The fountain plaza's mosaic: rings of coloured tiles round a cyan disc.", fade=150, opacity=0.85)


def scrawl(name, col, seed, desc):
    import random
    rng = random.Random(seed)
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = [(rng.uniform(20, 236), rng.uniform(60, 196)) for _ in range(9)]
    pts.sort()
    for k in range(len(pts) - 1):
        d.line([pts[k], pts[k + 1]], fill=col, width=14)
    for (x, y) in pts[::3]:
        d.ellipse([x - 12, y - 12, x + 12, y + 12], fill=col)
    img = img.filter(ImageFilter.GaussianBlur(1.2))
    save(name, img.resize((128, 128), Image.LANCZOS), [2.0, 1.2], "wall", desc, fade=60, opacity=0.9)


def poster(name, bg, fg, seed, desc):
    import random
    rng = random.Random(seed)
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([30, 8, 226, 248], fill=bg)
    d.rectangle([44, 22, 212, 150], fill=fg)
    for k in range(4):
        y = 170 + k * 18
        d.rectangle([48, y, 48 + rng.uniform(80, 160), y + 8], fill=(240, 236, 230, 255))
    d.ellipse([90, 50, 166, 126], fill=bg)
    d.polygon([(128, 60), (150, 110), (106, 110)], fill=fg)
    save(name, img.resize((128, 128), Image.LANCZOS), [0.8, 1.0], "wall", desc, fade=50)


def emblem():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse([30, 30, S - 30, S - 30], outline="#ffa040", width=24)
    d.ellipse([70, 70, S - 70, S - 70], fill="#1d1a26")
    # A crescent (the strip) over a landing cross.
    d.ellipse([c - 150, c - 150, c + 150, c + 150], fill="#4fe3ff")
    d.ellipse([c - 120, c - 190, c + 180, c + 110], fill="#1d1a26")
    d.rectangle([c - 14, c - 20, c + 14, c + 150], fill="#ffa040")
    d.rectangle([c - 90, c + 60, c + 90, c + 88], fill="#ffa040")
    f = fit_font(d, "PORT VESPER", COND, S - 120, 70)
    centred(d, "PORT VESPER", f, S, 70, "#f0ece0", dy=400)
    save("Port Vesper Emblem", img.resize((128, 128), Image.LANCZOS), 2.6, ["wall", "floor"], "The invented Port Vesper authority emblem: a crescent over a landing cross.", fade=110)


def main():
    for k in NEON:
        neon(k)
    pad_ring()
    pad_numbers()
    market_tiles()
    mosaic()
    scrawl("Vesper Scrawl Pink", "#ff5fb4", 5, "A pink spray-paint scrawl (abstract, no letters).")
    scrawl("Vesper Scrawl Cyan", "#4fe3ff", 9, "A cyan spray-paint scrawl (abstract, no letters).")
    poster("Vesper Poster Violet", "#4a2a80", "#b070ff", 3, "An abstract gig poster in violet.")
    poster("Vesper Poster Orange", "#8a3a10", "#ffa040", 4, "An abstract travel poster in orange.")
    emblem()
    print("wrote", len(os.listdir(OUT)) // 2, "decals to", OUT)


if __name__ == "__main__":
    main()
