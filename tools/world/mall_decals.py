#!/usr/bin/env python3
"""Writes the mall's pack-local decals (Maps/World/Decals/Mall/*.json + *.png): brand posters for every invented shop,
film posters for the cinema, wayfinding and service signs, sale posters, food-court promos and the mall's floor
medallion. Each picture is a 128 x 128 PNG (the decal atlas's layer size) drawn with the 5 x 7 pixel font and the
pixel-art logos of pixelkit.py, so the lettering stays crisp. All brands and films are invented.

Usage: python3 tools/world/mall_decals.py <repo root>
"""
import json, os, sys
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixelkit as pk

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/World/Decals/Mall")
N = 128


def rgb(c):
    c = c.lstrip("#")
    return tuple(int(c[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


class Pic:
    def __init__(self, bg=None):
        self.im = Image.new("RGBA", (N, N), rgb(bg) if bg else (0, 0, 0, 0))
        self.px = self.im.load()

    def rect(self, x0, y0, x1, y1, c):
        c = rgb(c) if isinstance(c, str) else c
        for y in range(max(0, int(y0)), min(N, int(y1))):
            for x in range(max(0, int(x0)), min(N, int(x1))):
                self.px[x, y] = c

    def disc(self, cx, cy, r, c, r_in=0):
        c = rgb(c)
        for y in range(N):
            for x in range(N):
                d = ((x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2) ** 0.5
                if r_in <= d <= r:
                    self.px[x, y] = c

    def text(self, s, cy, c, scale=1, cx=N / 2, spacing=1):
        grid = pk.text_grid(s, spacing)
        w = len(grid[0]) * scale
        x0 = int(round(cx - w / 2))
        y0 = int(round(cy - 7 * scale / 2))
        for r, row in enumerate(grid):
            for k, ch in enumerate(row):
                if ch == "#":
                    self.rect(x0 + k * scale, y0 + r * scale, x0 + (k + 1) * scale, y0 + (r + 1) * scale, c)
        return w

    def fit(self, s, cy, c, maxw=120, maxscale=3):
        for sc in range(maxscale, 0, -1):
            if (len(s) * 6 - 1) * sc <= maxw:
                return self.text(s, cy, c, sc)
        return self.text(s, cy, c, 1, spacing=0)

    def art(self, name, pal, cx, cy, scale):
        art = pk.LOGOS[name]
        w, h = pk.logo_size(name)
        x0, y0 = int(cx - w * scale / 2), int(cy - h * scale / 2)
        for r, row in enumerate(art):
            for k, ch in enumerate(row):
                if ch in pal:
                    self.rect(x0 + k * scale, y0 + r * scale, x0 + (k + 1) * scale, y0 + (r + 1) * scale, pal[ch])

    def border(self, c, t=3):
        self.rect(0, 0, N, t, c); self.rect(0, N - t, N, N, c); self.rect(0, 0, t, N, c); self.rect(N - t, 0, N, N, c)

    def arrow(self, cx, cy, d, c, s=4):
        """A chunky arrow pointing d ('l', 'r', 'u')."""
        for k in range(4 * s):
            h = (4 * s - k) if d != "u" else 0
            if d == "r":
                self.rect(cx + k, cy - (4 * s - k), cx + k + 1, cy + (4 * s - k), c)
            elif d == "l":
                self.rect(cx - k - 1, cy - (4 * s - k), cx - k, cy + (4 * s - k), c)
            else:
                self.rect(cx - (4 * s - k), cy - k - 1, cx + (4 * s - k), cy - k, c)
        if d == "r":
            self.rect(cx - 5 * s, cy - s, cx, cy + s, c)
        elif d == "l":
            self.rect(cx, cy - s, cx + 5 * s, cy + s, c)
        else:
            self.rect(cx - s, cy, cx + s, cy + 5 * s, c)


DECALS = []


def decal(name, pic, size, surfaces, desc, fade=60, opacity=None):
    fn = "".join(ch for ch in name.title() if ch.isalnum())
    pic.im.save(os.path.join(OUT, fn + ".png"))
    d = {"format": "call-of-slop-decal/1", "name": name, "category": "Mall", "description": desc, "author": "Call of Slop",
         "size": size, "rotation": 0, "surfaces": surfaces, "fadeDistance": fade, "image": fn + ".png"}
    if opacity:
        d["opacity"] = opacity
    with open(os.path.join(OUT, fn + ".json"), "w") as f:
        f.write(json.dumps(d, indent=2) + "\n")
    DECALS.append(name)


os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT):
    if f.endswith((".json", ".png")):
        os.remove(os.path.join(OUT, f))

# ---------------------------------------------------------------- brand posters (0.8 x 1.1 m, walls)
BRANDS = [  # (key, name, board, letters, logo, palette, slogan)
    ("Holloway Finch", "HOLLOWAY & FINCH", "#1d2a3a", "#e8d6a0", "monogram", {"a": "#e8d6a0", "b": "#c8a050"}, "SINCE 1962"),
    ("Voltwave", "VOLTWAVE", "#101a2a", "#40d0ff", "bolt", {"a": "#ffe040"}, "POWER UP"),
    ("Dog Eared Books", "DOG-EARED", "#2a3a2a", "#f0e8d0", "book", {"a": "#c84a2a", "b": "#2a6aa0", "c": "#f0e8d0"}, "READ MORE"),
    ("Sole Purpose", "SOLE PURPOSE", "#f0f0f0", "#202020", "shoe", {"a": "#e04020", "b": "#ffffff", "c": "#202020"}, "STEP IT UP"),
    ("Full Tilt Sports", "FULL TILT", "#c81818", "#ffffff", "ball", {"a": "#202020", "b": "#ffffff"}, "GO ALL OUT"),
    ("Loom Linen", "LOOM & LINEN", "#e8e0d4", "#4a3a30", "needle", {"a": "#8a6a50", "b": "#c84a4a"}, "NEW SEASON"),
    ("Gilt Trip", "GILT TRIP", "#141418", "#e8c860", "gem", {"a": "#e8c860", "b": "#fff4c0"}, "TREAT YOURSELF"),
    ("Gumdrop Galaxy", "GUMDROP", "#5a2a8a", "#ffe0f8", "candy", {"a": "#ff60b0", "b": "#ffd020", "c": "#ffffff"}, "PICK N MIX"),
    ("Topsy Toys", "TOPSY TOYS", "#f0d020", "#d02020", "bear", {"a": "#a06030", "b": "#202020", "c": "#202020"}, "PLAY ALL DAY"),
    ("Token Town", "TOKEN TOWN", "#101010", "#ff40c0", "token", {"a": "#ffd020", "b": "#ff8020"}, "PLAY 2 WIN"),
    ("Gloss Lab", "GLOSS LAB", "#f8e0e8", "#a01840", "lips", {"a": "#d0204a", "b": "#ffffff"}, "GLOW UP"),
    ("Wagtail Pets", "WAGTAIL", "#2a7a5a", "#ffffff", "paw", {"a": "#ffffff", "b": "#ffd060"}, "HAPPY PETS"),
    ("Nest Nook", "NEST & NOOK", "#6a8a7a", "#ffffff", "house", {"a": "#e8a050", "b": "#ffffff", "c": "#4a3020"}, "HOME SWEET HOME"),
    ("Cellsius", "CELLSIUS", "#e8f0f8", "#2050c0", "phone", {"a": "#2050c0", "b": "#80d0ff", "c": "#ffffff"}, "STAY COOL"),
    ("Hemline", "HEMLINE", "#f4f0ec", "#202020", "needle", {"a": "#202020", "b": "#c0304a"}, "NEW LOOKS"),
    ("Stardust Cinemas", "STARDUST", "#1a0a2a", "#ffe060", "star", {"a": "#ffe060"}, "NOW SHOWING"),
]
for key, name, board, letters, logo, pal, slogan in BRANDS:
    p = Pic(board)
    p.border(letters, 3)
    w, h = pk.logo_size(logo)
    sc = min(56 // w, 52 // h)
    p.art(logo, pal, N / 2, 40, sc)
    p.fit(name, 86, letters, 116, 2)
    p.rect(20, 98, 108, 100, letters)
    p.fit(slogan, 110, letters, 116, 1)
    decal(f"{key} Poster", p, [0.8, 1.1], "wall", f"A {name.title()} shop poster: the logo, the name and '{slogan.lower()}'. Invented brand.")

# ---------------------------------------------------------------- film posters (0.7 x 1.0 m)
FILMS = [
    ("Space Llamas", "#0a1030", "#ffe060", [("disc", 64, 46, 26, "#c8a0e0"), ("disc", 64, 46, 18, "#0a1030"), ("rect", 50, 30, 78, 64, "#f0e8d8"), ("rect", 54, 22, 60, 32, "#f0e8d8"), ("rect", 68, 22, 74, 32, "#f0e8d8")]),
    ("The Last Donut", "#f0a0c0", "#3a1020", [("disc", 64, 46, 28, "#e080a0"), ("disc", 64, 46, 22, "#f0c070"), ("disc", 64, 46, 9, "#f0a0c0")]),
    ("Mall Of The Dead", "#101810", "#a0ff60", [("rect", 30, 20, 98, 72, "#2a3a2a"), ("rect", 40, 30, 52, 42, "#a0ff60"), ("rect", 76, 30, 88, 42, "#a0ff60"), ("rect", 48, 54, 80, 60, "#a0ff60")]),
    ("Pixel Hearts", "#2a1040", "#ff6080", [("rect", 40, 30, 58, 50, "#ff4060"), ("rect", 70, 30, 88, 50, "#ff4060"), ("rect", 34, 36, 94, 56, "#ff4060"), ("rect", 44, 56, 84, 66, "#ff4060"), ("rect", 56, 66, 72, 74, "#ff4060")]),
]
for title, bg, fg, shapes in FILMS:
    p = Pic(bg)
    for s in shapes:
        if s[0] == "disc":
            p.disc(s[1], s[2], s[3], s[4])
        else:
            p.rect(s[1], s[2], s[3], s[4], s[5])
    words = title.upper().split(" ")
    lines = [" ".join(words[:2]), " ".join(words[2:])] if len(title) > 12 else [title.upper()]
    for i, ln in enumerate([l for l in lines if l]):
        p.fit(ln, 90 + i * 14, fg, 118, 2)
    p.text("COMING SOON" if title == "Pixel Hearts" else "NOW SHOWING", 120, "#ffffff")
    decal(f"Film Poster {title}", p, [0.7, 1.0], "wall", f"A poster for the (invented) film '{title}'.")

# ---------------------------------------------------------------- wayfinding and service signs (walls)
SIGNS = [
    ("Sign Restrooms Right", "RESTROOMS", "r", "#2a4a7a", "#ffffff"),
    ("Sign Food Court Right", "FOOD COURT", "r", "#c83020", "#ffffff"),
    ("Sign Food Court Left", "FOOD COURT", "l", "#c83020", "#ffffff"),
    ("Sign Cinema Upstairs", "CINEMA", "u", "#1a0a2a", "#ffe060"),
    ("Sign Department Store Left", "DEPT STORE", "l", "#1d2a3a", "#e8d6a0"),
    ("Sign Staff Only", "STAFF ONLY", "", "#e8e8e8", "#c02020"),
    ("Sign Dock 1", "DOCK 1", "", "#e8c020", "#202020"),
    ("Sign Dock 2", "DOCK 2", "", "#e8c020", "#202020"),
    ("Sign Fire Lane", "FIRE LANE", "", "#c02020", "#ffffff"),
    ("Sign Deliveries", "DELIVERIES", "r", "#2a5a3a", "#ffffff"),
    ("Sign Mall Entrance", "MALL ENTRANCE", "", "#2a3a4a", "#ffffff"),
    ("Sign Cart Return", "CART RETURN", "", "#2050a0", "#ffffff"),
]
for name, text, arrow, bg, fg in SIGNS:
    p = Pic(bg)
    p.border(fg, 4)
    if arrow == "r":
        p.text(text, 46, fg, 2 if len(text) * 12 <= 116 else 1)
        p.arrow(64, 88, "r", fg, 4)
    elif arrow == "l":
        p.text(text, 46, fg, 2 if len(text) * 12 <= 116 else 1)
        p.arrow(64, 88, "l", fg, 4)
    elif arrow == "u":
        p.text(text, 46, fg, 3)
        p.arrow(64, 82, "u", fg, 4)
        p.text("UPSTAIRS", 108, fg, 1)
    else:
        p.fit(text, 64, fg, 120, 4)
    decal(name, p, [1.0, 0.7], "wall", f"A {text.lower()} sign{' with an arrow' if arrow else ''}.", fade=70)

# ---------------------------------------------------------------- sale posters and food promos (walls)
for name, lines, bg, fg in (("Poster Sale", ["SALE", "NOW ON"], "#d01818", "#ffffff"), ("Poster Clearance", ["CLEARANCE", "UP TO", "70% OFF"], "#ffd020", "#d01818"),
                            ("Poster Half Price", ["50%", "OFF"], "#ffffff", "#d01818"), ("Poster Buy One", ["BUY 1", "GET 1", "FREE"], "#2050c0", "#ffffff"),
                            ("Poster Combo Deal", ["COMBO", "$5", "MEALS"], "#f0a020", "#5a2a10"), ("Poster Free Refills", ["FREE", "REFILLS"], "#2a7a3a", "#ffffff")):
    p = Pic(bg)
    p.border(fg, 4)
    for i, ln in enumerate(lines):
        p.fit(ln, 64 + (i - (len(lines) - 1) / 2) * 30, fg, 112, 4)
    decal(name, p, [0.7, 1.0], "wall", f"A shop poster: {' '.join(lines).lower()}.")

# ---------------------------------------------------------------- floor pieces
p = Pic()
p.disc(64, 64, 63, "#2a3a4a")
p.disc(64, 64, 60, "#d8c890", 56)
p.disc(64, 64, 50, "#e8e4dc")
p.art("star", {"a": "#d8b040"}, 64, 52, 6)
p.text("SLOPVIEW", 92, "#2a3a4a", 2)
decal("Slopview Floor Medallion", p, 3.2, "floor", "The mall's floor medallion: a gold star in navy and cream rings with SLOPVIEW.", fade=90)
p = Pic()
p.rect(0, 40, N, 88, "#e8c020")
p.text("LOADING ZONE", 64, "#202020", 1)
p.rect(0, 40, N, 44, "#202020"); p.rect(0, 84, N, 88, "#202020")
decal("Loading Zone Stencil", p, [4.0, 1.4], "floor", "LOADING ZONE painted across a yellow band, for a dock apron.", fade=80, opacity=0.9)
p = Pic()
p.text("FIRE LANE", 64, "#e02020", 2)
decal("Fire Lane Stencil", p, [4.0, 1.0], "floor", "FIRE LANE stencilled in red on the road.", fade=80, opacity=0.85)
p = Pic()
p.rect(14, 14, 114, 114, "#2050a0")
p.disc(64, 40, 10, "#ffffff")
p.rect(56, 52, 72, 80, "#ffffff"); p.rect(46, 80, 92, 88, "#ffffff"); p.rect(84, 80, 92, 104, "#ffffff")
decal("Accessible Bay Marking", p, 1.6, "floor", "A blue accessible-parking symbol painted in a bay.", fade=60, opacity=0.9)

print(len(DECALS), "decals ->", OUT)
