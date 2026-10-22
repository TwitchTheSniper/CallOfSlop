#!/usr/bin/env python3
"""Writes the Main Street map's own decals (Maps/World/Decals/MainStreet/): the invented shop signs of Prairie Hollow
(Dot's Diner, the barber's, Main St. Drug, Prairie Savings, Rosa's Pizza, Hollow Grocery, Starline gas), the welcome
sign, the water tower and grain co-op lettering, a few posters and flyers, and the US pavement markings (STOP, the RR
crossing). Every name, word and logo is made up for this town. Pictures are PNGs drawn with Pillow.

Usage: python3 tools/world/mainstreet_decals.py <repo root>

The game resamples a decal's image to 128 x 128 and stretches it over the decal's size, so a sign is drawn at its true
shape (40 px per 10 cm... whatever fits 2048 px), then squashed into square tiles: a wide sign is cut into side-by-side
pieces ("<Name> 1", "<Name> 2", ...), each about as wide as it is tall, so each piece keeps its 128 px across. The map
generator (mainstreet.py) places the pieces next to each other; SIGNS below gives their sizes.
"""
import json, os, sys
from PIL import Image, ImageDraw, ImageFont

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/World/Decals/MainStreet")
FONTS = "/usr/share/fonts/truetype"
BOLD = f"{FONTS}/google-fonts/Poppins-Bold.ttf"
SANS = f"{FONTS}/dejavu/DejaVuSans-Bold.ttf"
COND = f"{FONTS}/dejavu/DejaVuSansCondensed-Bold.ttf"
SERIF = f"{FONTS}/dejavu/DejaVuSerif-Bold.ttf"


def font(path, px):
    return ImageFont.truetype(path, int(px))


def text_c(d, cx, cy, s, f, fill, stroke=0, stroke_fill=None):
    b = d.textbbox((0, 0), s, font=f, stroke_width=stroke)
    w, h = b[2] - b[0], b[3] - b[1]
    d.text((cx - w / 2 - b[0], cy - h / 2 - b[1]), s, font=f, fill=fill, stroke_width=stroke, stroke_fill=stroke_fill)


def fit(d, s, path, max_w, max_h):
    px = max_h
    while px > 8:
        f = font(path, px)
        b = d.textbbox((0, 0), s, font=f)
        if b[2] - b[0] <= max_w and b[3] - b[1] <= max_h:
            return f
        px -= 2
    return font(path, 8)


def board(W, H, bg, border=None, bw=0.06, radius=0.15):
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    r = int(min(W, H) * radius)
    if border:
        d.rounded_rectangle([0, 0, W - 1, H - 1], r, fill=border)
        m = int(min(W, H) * bw)
        d.rounded_rectangle([m, m, W - 1 - m, H - 1 - m], max(1, r - m), fill=bg)
    else:
        d.rounded_rectangle([0, 0, W - 1, H - 1], r, fill=bg)
    return im, d


PPM = 400   # pixels per metre while drawing


def sign_diner(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im, d = board(W, H, "#f3ead2", "#b7bcc2", bw=0.09)
    d.rectangle([int(H * 0.12), int(H * 0.72), W - int(H * 0.12), int(H * 0.8)], fill="#c0302a")
    f = fit(d, "DOT'S DINER", BOLD, W * 0.84, H * 0.56)
    text_c(d, W / 2, H * 0.43, "DOT'S DINER", f, "#c0302a", stroke=6, stroke_fill="#7a1a16")
    for x in (H * 0.25, W - H * 0.25):   # coffee cup logos
        d.rounded_rectangle([x - H * 0.1, H * 0.3, x + H * 0.1, H * 0.6], 8, fill="#2a3a5a")
        d.arc([x + H * 0.04, H * 0.36, x + H * 0.18, H * 0.52], 270, 90, fill="#2a3a5a", width=8)
    return im


def sign_barber(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im, d = board(W, H, "#f4f2ec", "#1e2f5a", bw=0.08, radius=0.08)
    for k in range(10):   # barber stripes at both ends
        for x0 in (int(H * 0.12), W - int(H * 0.52)):
            c = "#c0302a" if k % 2 else "#1e2f5a"
            d.polygon([(x0 + k * H * 0.08, H * 0.12), (x0 + k * H * 0.08 + H * 0.08, H * 0.12), (x0 + k * H * 0.08 - H * 0.12, H * 0.88), (x0 + k * H * 0.08 - H * 0.2, H * 0.88)], fill=c)
    d.rectangle([int(H * 0.1), int(H * 0.1), int(H * 0.12), H - int(H * 0.1)], fill="#1e2f5a")
    f = fit(d, "BARBER SHOP", SERIF, W - H * 1.3, H * 0.6)
    text_c(d, W / 2, H / 2, "BARBER SHOP", f, "#1e2f5a")
    return im


def sign_drug(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im, d = board(W, H, "#1f6a46", "#e8e4d8", bw=0.07, radius=0.1)
    cx = H * 0.55
    d.rectangle([cx - H * 0.09, H * 0.2, cx + H * 0.09, H * 0.8], fill="#f4f2ec")
    d.rectangle([cx - H * 0.3, H * 0.41, cx + H * 0.3, H * 0.59], fill="#f4f2ec")
    f = fit(d, "MAIN ST. DRUG", COND, W - H * 1.4, H * 0.58)
    text_c(d, W / 2 + H * 0.4, H / 2, "MAIN ST. DRUG", f, "#f4f2ec")
    return im


def sign_bank(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, H - 1], fill="#cfc8b6")
    d.rectangle([0, 0, W - 1, int(H * 0.08)], fill="#b5ad9a"); d.rectangle([0, H - int(H * 0.08), W - 1, H - 1], fill="#b5ad9a")
    f = fit(d, "PRAIRIE  SAVINGS  BANK", SERIF, W * 0.92, H * 0.6)
    text_c(d, W / 2 + 3, H / 2 + 3, "PRAIRIE  SAVINGS  BANK", f, "#8a8270")
    text_c(d, W / 2, H / 2, "PRAIRIE  SAVINGS  BANK", f, "#3a362e")
    return im


def sign_pizza(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im, d = board(W, H, "#f3ead2", "#2f6a3a", bw=0.08, radius=0.2)
    d.rectangle([int(H * 0.1), int(H * 0.78), W - int(H * 0.1), int(H * 0.84)], fill="#c0302a")
    f = fit(d, "ROSA'S PIZZA", BOLD, W * 0.7, H * 0.58)
    text_c(d, W / 2, H * 0.45, "ROSA'S PIZZA", f, "#c0302a", stroke=5, stroke_fill="#2f6a3a")
    for x in (H * 0.5, W - H * 0.5):    # pizza slice logos
        d.polygon([(x, H * 0.72), (x - H * 0.24, H * 0.22), (x + H * 0.24, H * 0.22)], fill="#e8b44a")
        d.rectangle([x - H * 0.26, H * 0.16, x + H * 0.26, H * 0.24], fill="#b07a3a")
        for px, py in ((-0.08, 0.34), (0.07, 0.42), (0, 0.56)):
            d.ellipse([x + H * px - H * 0.04, H * py - H * 0.04, x + H * px + H * 0.04, H * py + H * 0.04], fill="#b0302a")
    return im


def sign_grocery(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im, d = board(W, H, "#2a4a2a", "#d8c07a", bw=0.07, radius=0.06)
    f = fit(d, "HOLLOW GROCERY", COND, W * 0.78, H * 0.56)
    text_c(d, W / 2, H / 2, "HOLLOW GROCERY", f, "#f0e2b0")
    for x in (H * 0.42, W - H * 0.42):   # an apple each end
        d.ellipse([x - H * 0.2, H * 0.3, x + H * 0.2, H * 0.72], fill="#c0302a")
        d.rectangle([x - 4, H * 0.18, x + 4, H * 0.32], fill="#6a4a2a")
        d.ellipse([x + 2, H * 0.17, x + H * 0.16, H * 0.27], fill="#5a9a3a")
    return im


def sign_starline(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, H - 1], fill="#f4f2ec")
    d.rectangle([0, int(H * 0.72), W - 1, H - 1], fill="#1e4a8a")
    d.rectangle([0, int(H * 0.64), W - 1, int(H * 0.72)], fill="#d83a2a")
    import math
    cx, cy, R1, R2 = H * 0.55, H * 0.36, H * 0.3, H * 0.12
    pts = [(cx + (R1 if k % 2 == 0 else R2) * math.sin(math.pi * k / 5), cy - (R1 if k % 2 == 0 else R2) * math.cos(math.pi * k / 5)) for k in range(10)]
    d.polygon(pts, fill="#d83a2a")
    f = fit(d, "STARLINE", BOLD, W - H * 1.4, H * 0.55)
    text_c(d, W / 2 + H * 0.35, H * 0.36, "STARLINE", f, "#1e4a8a")
    return im


def sign_welcome(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im, d = board(W, H, "#2e5a3a", "#e8e4d8", bw=0.05, radius=0.08)
    text_c(d, W / 2, H * 0.2, "WELCOME TO", fit(d, "WELCOME TO", SANS, W * 0.5, H * 0.13), "#e8e4d8")
    text_c(d, W / 2, H * 0.47, "PRAIRIE HOLLOW", fit(d, "PRAIRIE HOLLOW", SERIF, W * 0.86, H * 0.3), "#f4e6a0")
    text_c(d, W / 2, H * 0.78, "EST. 1889   POP. 1,204", fit(d, "EST. 1889   POP. 1,204", SANS, W * 0.66, H * 0.12), "#e8e4d8")
    for x in (W * 0.1, W * 0.9):   # wheat sheaves
        for k in range(-2, 3):
            d.line([(x, H * 0.92), (x + k * H * 0.05, H * 0.3)], fill="#d8b44a", width=8)
            d.ellipse([x + k * H * 0.05 - H * 0.03, H * 0.22, x + k * H * 0.05 + H * 0.03, H * 0.36], fill="#e8c45a")
    return im


def lettering(text, path, color, outline=None):
    def draw(w, h):
        W, H = int(w * PPM), int(h * PPM)
        im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
        f = fit(d, text, path, W * 0.96, H * 0.9)
        text_c(d, W / 2, H / 2, text, f, color, stroke=6 if outline else 0, stroke_fill=outline)
        return im
    return draw


def poster_fair(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im = Image.new("RGBA", (W, H), "#f0e4c0"); d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, H * 0.28], fill="#c0302a")
    text_c(d, W / 2, H * 0.14, "COUNTY", fit(d, "COUNTY", BOLD, W * 0.86, H * 0.2), "#f0e4c0")
    text_c(d, W / 2, H * 0.38, "FAIR", fit(d, "FAIR", BOLD, W * 0.8, H * 0.18), "#1e2f5a")
    d.ellipse([W * 0.22, H * 0.5, W * 0.78, H * 0.5 + W * 0.56], outline="#1e2f5a", width=10)   # ferris wheel
    for k in range(8):
        import math
        a = math.pi * k / 4; cx, cy, r = W / 2, H * 0.5 + W * 0.28, W * 0.28
        d.line([(cx, cy), (cx + r * math.cos(a), cy + r * math.sin(a))], fill="#1e2f5a", width=6)
    text_c(d, W / 2, H * 0.93, "AUG 14 - 17", fit(d, "AUG 14 - 17", SANS, W * 0.8, H * 0.07), "#c0302a")
    return im


def poster_dog(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im = Image.new("RGBA", (W, H), "#f4f2ec"); d = ImageDraw.Draw(im)
    text_c(d, W / 2, H * 0.12, "LOST DOG", fit(d, "LOST DOG", BOLD, W * 0.9, H * 0.16), "#1a1a1a")
    d.rectangle([W * 0.15, H * 0.25, W * 0.85, H * 0.65], fill="#8a8a86")
    d.ellipse([W * 0.32, H * 0.32, W * 0.68, H * 0.6], fill="#c49a5a")
    for y in (0.72, 0.8, 0.88):
        d.rectangle([W * 0.12, H * y, W * 0.88, H * y + H * 0.035], fill="#5a5a58")
    return im


def poster_homecoming(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im = Image.new("RGBA", (W, H), "#1e2f5a"); d = ImageDraw.Draw(im)
    text_c(d, W / 2, H * 0.16, "GO", fit(d, "GO", BOLD, W * 0.5, H * 0.18), "#f4c430")
    text_c(d, W / 2, H * 0.36, "OWLS!", fit(d, "OWLS!", BOLD, W * 0.9, H * 0.15), "#f4c430")
    d.polygon([(W * 0.5, H * 0.48), (W * 0.82, H * 0.62), (W * 0.5, H * 0.76), (W * 0.18, H * 0.62)], fill="#8a4a2a")   # football
    d.line([(W * 0.4, H * 0.62), (W * 0.6, H * 0.62)], fill="#f4f2ec", width=8)
    text_c(d, W / 2, H * 0.88, "FRIDAY 7 PM", fit(d, "FRIDAY 7 PM", SANS, W * 0.8, H * 0.07), "#f4f2ec")
    return im


def mark_stop(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    text_c(d, W / 2, H / 2, "STOP", fit(d, "STOP", COND, W * 0.94, H * 0.94), (236, 236, 228, 235))
    return im


def mark_rr(w, h):
    W, H = int(w * PPM), int(h * PPM)
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    c = (236, 236, 228, 235); t = int(W * 0.08)
    d.line([(W * 0.08, H * 0.1), (W * 0.92, H * 0.9)], fill=c, width=t)
    d.line([(W * 0.92, H * 0.1), (W * 0.08, H * 0.9)], fill=c, width=t)
    f = fit(d, "R", COND, W * 0.3, H * 0.28)
    text_c(d, W * 0.2, H / 2, "R", f, c); text_c(d, W * 0.8, H / 2, "R", f, c)
    d.rectangle([0, int(H * 0.02), W, int(H * 0.05)], fill=c); d.rectangle([0, int(H * 0.95), W, int(H * 0.98)], fill=c)
    return im


# name, size (w, h) in metres, art, surfaces, pieces (0 = from the aspect)
SIGNS = [
    ("MS Dots Diner", (3.2, 0.8), sign_diner, "wall", 0),
    ("MS Barber Shop", (2.6, 0.62), sign_barber, "wall", 0),
    ("MS Main St Drug", (3.0, 0.7), sign_drug, "wall", 0),
    ("MS Prairie Savings", (5.4, 0.55), sign_bank, "wall", 3),
    ("MS Rosas Pizza", (3.0, 0.72), sign_pizza, "wall", 0),
    ("MS Hollow Grocery", (3.2, 0.7), sign_grocery, "wall", 0),
    ("MS Starline", (3.6, 0.8), sign_starline, "wall", 0),
    ("MS Welcome Sign", (3.6, 1.8), sign_welcome, "wall", 2),
    ("MS Water Tower Lettering", (6.6, 1.5), lettering("PRAIRIE HOLLOW", SERIF, "#1e2f5a"), "wall", 3),
    ("MS Co-op Lettering", (7.5, 1.6), lettering("HOLLOW GRAIN CO-OP", COND, "#f4f2ec", outline="#3a3a36"), "wall", 4),
    ("MS County Fair Poster", (0.7, 1.0), poster_fair, "wall", 1),
    ("MS Lost Dog Flyer", (0.32, 0.45), poster_dog, "wall", 1),
    ("MS Homecoming Poster", (0.62, 0.9), poster_homecoming, "wall", 1),
    ("MS Stop Marking", (2.6, 1.1), mark_stop, "floor", 1),
    ("MS Railroad Crossing Marking", (3.0, 5.6), mark_rr, "floor", 1),
]


def pieces_of(size, n):
    return n if n else max(1, round(size[0] / size[1] / 2.0))


def main():
    os.makedirs(OUT, exist_ok=True)
    names = []
    for name, size, art, surf, n in SIGNS:
        n = pieces_of(size, n)
        im = art(*size)
        W, H = im.size
        for k in range(n):
            tile = im.crop((W * k // n, 0, W * (k + 1) // n, H)).resize((256, 256), Image.LANCZOS)
            nm = name if n == 1 else f"{name} {k + 1}"
            fn = nm.replace(" ", "").replace("-", "")
            tile.save(os.path.join(OUT, fn + ".png"))
            d = {"format": "call-of-slop-decal/1", "name": nm, "category": "MainStreet",
                 "description": f"Main Street (World Tour): {name[3:]}" + (f", piece {k + 1} of {n} (left to right)" if n > 1 else "") + ". Invented for the town of Prairie Hollow.",
                 "author": "Call of Slop", "size": [round(size[0] / n, 3), size[1]], "rotation": 0, "surfaces": surf,
                 "fadeDistance": 150 if size[0] > 2 else 70, "image": fn + ".png"}
            with open(os.path.join(OUT, fn + ".json"), "w") as f:
                json.dump(d, f, indent=2)
            names.append(nm)
    print(f"wrote {len(names)} decals to {OUT}")


if __name__ == "__main__":
    main()
