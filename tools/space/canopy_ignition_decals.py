#!/usr/bin/env python3
"""Writes the Space pack's decals for 07 Canopy Deck (Maps/Space/Decals/CanopyDeck/) and 08 Ignition Hall
(Maps/Space/Decals/IgnitionHall/): wall signs cut into 128 px square tiles the maps line up side by side (SIGNS, used by
tools/space/arboretum.py and reactor.py), deck section and sector stencils, the two invented crests (the Long Meridian
ark and the Halvard Fusion Works), algae and root grime, hull plate seams, floor guide markings and the reactor's core
ring. All original artwork; the words are invented place names and plain warnings.

    python3 tools/space/canopy_ignition_decals.py          (run from the repository root)
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
COND = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"
if not os.path.exists(COND):
    COND = BOLD

# key: (decal base name, width m, height m, background, text colour, text, border colour, style)
SIGNS = {
    # Canopy Deck (the arboretum): teal and amber on dark hull grey; hazard signs amber with black chevrons.
    "arboretum": ("Canopy Sign Arboretum Deck", 5.2, 0.8, "#1e3a34", "#9fe8d0", "ARBORETUM DECK 4", "#4fc8a8", "plain"),
    "hydro": ("Canopy Sign Hydroponics", 4.0, 0.7, "#23292e", "#9fe8d0", "HYDROPONICS", None, "plain"),
    "irrigation": ("Canopy Sign Irrigation", 3.6, 0.7, "#23292e", "#9fe8d0", "IRRIGATION", None, "plain"),
    "vault": ("Canopy Sign Seed Vault", 3.6, 0.7, "#23292e", "#bfe0ff", "SEED VAULT", None, "plain"),
    "airlock_w": ("Canopy Sign Airlock West", 4.0, 0.8, "#1c3d4a", "#e8f4f2", "AIRLOCK WEST", "#6fe0e8", "plain"),
    "airlock_e": ("Canopy Sign Airlock East", 4.0, 0.8, "#4a1f1c", "#f4ece8", "AIRLOCK EAST", "#ff7a5a", "plain"),
    "oxygen": ("Canopy Sign Low Oxygen", 3.6, 0.6, "#e0b030", "#1c1c1c", "LOW OXYGEN", None, "hazard"),
    "hull": ("Canopy Sign Hull Breach", 3.6, 0.6, "#e0b030", "#1c1c1c", "HULL BREACH", None, "hazard"),
    # Ignition Hall (the reactor): orange and blue on dark steel.
    "containment": ("Ignition Sign Containment", 5.2, 0.8, "#2a2a2e", "#ff9a3a", "CONTAINMENT", "#ff7a2a", "plain"),
    "turbine_a": ("Ignition Sign Turbine Hall A", 4.8, 0.8, "#1c3d4a", "#e8f4f2", "TURBINE HALL A", "#6fe0e8", "plain"),
    "turbine_b": ("Ignition Sign Turbine Hall B", 4.8, 0.8, "#4a1f1c", "#f4ece8", "TURBINE HALL B", "#ff7a5a", "plain"),
    "gallery": ("Ignition Sign Control Gallery", 5.2, 0.7, "#2a2a2e", "#bfe0ff", "CONTROL GALLERY", None, "plain"),
    "coolant": ("Ignition Sign Coolant", 3.2, 0.7, "#2a2a2e", "#bfe0ff", "COOLANT", None, "plain"),
    "field": ("Ignition Sign High Field", 3.6, 0.6, "#ff8a2a", "#1c1c1c", "HIGH FIELD", None, "hazard"),
    "clear": ("Ignition Sign Keep Clear", 3.6, 0.6, "#ff8a2a", "#1c1c1c", "KEEP CLEAR", None, "hazard"),
}

FOLDER = {"Canopy": os.path.join("Maps", "Space", "Decals", "CanopyDeck"), "Ignition": os.path.join("Maps", "Space", "Decals", "IgnitionHall")}


def folder_of(name):
    return FOLDER["Canopy"] if name.startswith("Canopy") or name.startswith("Ark") else FOLDER["Ignition"]


def tiles(key):
    name, w, h = SIGNS[key][:3]
    n = max(1, int(math.ceil(w / h / 1.15)))
    return [f"{name} {i + 1}" for i in range(n)], w / n, h


def save(name, img, size, surfaces, desc, blend="alpha", rotation=0, fade=None, **extra):
    out = folder_of(name)
    os.makedirs(out, exist_ok=True)
    fn = name.replace(" ", "_").lower()
    img.save(os.path.join(out, fn + ".png"))
    d = {"format": "call-of-slop-decal/1", "name": name, "category": os.path.basename(out), "description": desc, "author": "Call of Slop",
         "size": size, "rotation": rotation, "blend": blend, "surfaces": surfaces, "image": fn + ".png"}
    if fade:
        d["fadeDistance"] = fade
    d.update(extra)
    with open(os.path.join(out, fn + ".json"), "w") as f:
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
    name, w, h, bg, fg, text, border, style = SIGNS[key]
    names, tw, th = tiles(key)
    n = len(names)
    W, H = 128 * n, 128
    img = Image.new("RGBA", (W, H), bg)
    d = ImageDraw.Draw(img)
    if border:
        d.rectangle([4, 4, W - 5, H - 5], outline=border, width=7)
    if style == "hazard":
        for k in range(-2, n * 6 + 2):
            x = k * 26
            d.polygon([(x, 0), (x + 12, 0), (x - 8, 20), (x - 20, 20)], fill="#1c1c1c")
            d.polygon([(x, 108), (x + 12, 108), (x - 8, 128), (x - 20, 128)], fill="#1c1c1c")
    f = fit_font(d, text, BOLD, W - 44, H - 52)
    centred(d, text, f, W, H, fg)
    for i, nm in enumerate(names):
        save(nm, img.crop((i * 128, 0, (i + 1) * 128, 128)), [round(tw, 3), round(th, 3)], "wall",
             f"Tile {i + 1} of {n} of the '{text}' sign (an invented place name or a plain warning).", fade=80)


def big(draw_fn, S=512):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    draw_fn(d, S)
    return img.resize((128, 128), Image.LANCZOS)


# ------------------------------------------------------------------ Canopy Deck ------------------------------------------------------------------

def ark_crest():
    def draw(d, S):
        c = S / 2
        d.ellipse([24, 24, S - 24, S - 24], outline="#9fe8d0", width=26)
        # A leaf: two arcs meeting at the tips, a midrib.
        d.chord([c - 150, c - 230, c + 150, c + 70], 20, 160, fill="#4fc8a8")
        d.chord([c - 150, c - 150, c + 150, c + 150], 200, 340, fill="#4fc8a8")
        d.line([(c, c - 220), (c, c + 60)], fill="#1e3a34", width=14)
        for k in range(3):
            y = c + 110 + k * 36
            d.rectangle([c - 120 + k * 20, y, c + 120 - k * 20, y + 16], fill="#e8f4f2")
    save("Ark Crest", big(draw), 2.6, ["wall", "floor"], "The invented crest of the generation ship Long Meridian: a leaf in a ring over three bars.", fade=110)


def section_numbers():
    for n in range(1, 5):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        f = fit_font(d, f"S{n}", BOLD, 110, 90)
        centred(d, f"S{n}", f, 128, 128, "#d8e8e0")
        save(f"Canopy Section {n}", img, 2.4, "floor", f"A big pale stencilled deck section number, S{n}.", fade=120)


def algae():
    def draw(d, S):
        import random
        rng = random.Random(5)
        for i in range(26):
            r = rng.uniform(30, 120)
            x, y = rng.uniform(100, S - 100), rng.uniform(100, S - 100)
            g = rng.randint(90, 150)
            d.ellipse([x - r, y - r * 0.7, x + r, y + r * 0.7], fill=(60, g, 60, rng.randint(90, 170)))
    img = Image.new("RGBA", (512, 512), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    draw(d, 512)
    img = img.resize((128, 128), Image.LANCZOS)
    save("Algae Bloom", img, 2.2, ["floor", "wall"], "A green algae stain spreading over wet decking or a canal wall (darkens the surface).",
         blend="multiply", fade=70, sizeVariation=0.3, rotation="random")


def root_cracks():
    def draw(d, S):
        import random
        rng = random.Random(9)
        c = S / 2
        for i in range(9):
            a = 2 * math.pi * i / 9 + rng.uniform(-0.3, 0.3)
            x, y = c, c
            for k in range(6):
                L = rng.uniform(20, 48)
                nx, ny = x + L * math.cos(a), y + L * math.sin(a)
                d.line([(x, y), (nx, ny)], fill=(70, 60, 50, 230), width=max(2, 14 - k * 2))
                x, y = nx, ny
                a += rng.uniform(-0.5, 0.5)
        for i in range(10):
            x, y = c + rng.uniform(-120, 120), c + rng.uniform(-120, 120)
            d.ellipse([x - 12, y - 8, x + 12, y + 8], fill=(90, 120, 60, 200))
    img = Image.new("RGBA", (512, 512), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    draw(d, 512)
    img = img.resize((128, 128), Image.LANCZOS)
    save("Root Cracks", img, 2.6, "floor", "Deck plating cracked and heaved by roots, with moss in the cracks (darkens the surface).",
         blend="multiply", fade=70, sizeVariation=0.3, rotation="random")


def hull_seams():
    img = Image.new("RGBA", (128, 128), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for k in (0, 64):
        d.line([(k, 0), (k, 127)], fill=(110, 112, 118, 255), width=3)
        d.line([(0, k), (127, k)], fill=(110, 112, 118, 255), width=3)
    for (x, y) in ((8, 8), (56, 8), (8, 56), (56, 56), (72, 8), (120, 8), (72, 56), (120, 56),
                   (8, 72), (56, 72), (8, 120), (56, 120), (72, 72), (120, 72), (72, 120), (120, 120)):
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(130, 132, 136, 255))
    save("Canopy Hull Seams", img, 4, ["floor", "wall"], "Hull plate seams and rivets on a 2 m grid (darkens the plating).", blend="multiply", fade=60)


def canopy_arrow():
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for k in range(3):
        y = 22 + k * 34
        d.polygon([(64, y), (108, y + 30), (94, y + 30), (64, y + 10), (34, y + 30), (20, y + 30)], fill="#4fc8a8")
    save("Canopy Deck Arrow", img, 1.6, "floor", "Three teal chevrons pointing north along a deck path (turn with rotation).", fade=70)


# ------------------------------------------------------------------ Ignition Hall ------------------------------------------------------------------

def fusion_logo():
    def draw(d, S):
        c = S / 2
        d.ellipse([c - 170, 60, c + 170, 400], fill="#ff8a2a")
        d.rectangle([40, 210, S - 40, 250], fill="#4ac8ff")
        d.ellipse([c - 60, 170, c + 60, 290], fill="#2a2a2e")
        f = fit_font(d, "HALVARD", BOLD, S - 70, 100)
        centred(d, "HALVARD", f, S, 120, "#f0f0ea", dy=300)
        f2 = fit_font(d, "FUSION WORKS", COND, S - 150, 56)
        centred(d, "FUSION WORKS", f2, S, 60, "#ff9a3a", dy=440)
    save("Halvard Logo", big(draw), 2.4, ["wall", "floor"], "The invented Halvard Fusion Works logo: an orange sun split by a blue bar.", fade=110)


def core_ring():
    def draw(d, S):
        c = S / 2
        d.ellipse([16, 16, S - 16, S - 16], outline="#ff8a2a", width=20)
        d.ellipse([60, 60, S - 60, S - 60], outline="#f0ece0", width=8)
        for k in range(32):
            a = 2 * math.pi * k / 32
            r0, r1 = (S / 2 - 50, S / 2 - 28) if k % 4 else (S / 2 - 70, S / 2 - 28)
            d.line([(c + r0 * math.cos(a), c + r0 * math.sin(a)), (c + r1 * math.cos(a), c + r1 * math.sin(a))], fill="#f0ece0", width=7)
    save("Ignition Core Ring", big(draw), 16, "floor", "The containment hall's floor ring: an orange outer ring, a white ring and ticks.", fade=150)


def sector_letters():
    for i, L in enumerate("ABCD"):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.rectangle([6, 6, 121, 121], outline="#ff8a2a", width=8)
        f = fit_font(d, L, BOLD, 90, 90)
        centred(d, L, f, 128, 128, "#f0ece0")
        save(f"Ignition Sector {L}", img, 2.2, "floor", f"A boxed white sector letter {L} with an orange frame.", fade=120)


def plasma_glyph():
    def draw(d, S):
        c = S / 2
        d.ellipse([40, 40, S - 40, S - 40], fill="#ff8a2a")
        d.ellipse([70, 70, S - 70, S - 70], fill="#1c1c1c")
        d.ellipse([c - 70, c - 70, c + 70, c + 70], fill="#ff8a2a")
        for k in range(3):
            a = -math.pi / 2 + 2 * math.pi * k / 3
            x, y = c + 130 * math.cos(a), c + 130 * math.sin(a)
            d.ellipse([x - 42, y - 42, x + 42, y + 42], fill="#ff8a2a")
            d.line([(c + 70 * math.cos(a), c + 70 * math.sin(a)), (x, y)], fill="#ff8a2a", width=36)
    save("Plasma Hazard Glyph", big(draw), 1.2, ["wall", "floor"], "An orange plasma hazard glyph: a ring round a core with three lobes (original, not a real symbol).", fade=90)


def guide_line():
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 50, 127, 78], fill="#4ac8ff")
    d.rectangle([0, 58, 127, 70], fill="#9fe0ff")
    save("Ignition Guide Line", img, [6, 0.5], "floor", "A blue floor guide line, 6 m long (runs east-west; turn it 90 for north-south).", fade=80)


def grating():
    img = Image.new("RGBA", (128, 128), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for k in range(0, 128, 16):
        d.line([(k, 0), (k, 127)], fill=(90, 92, 98, 255), width=4)
    for k in range(0, 128, 32):
        d.line([(0, k), (127, k)], fill=(120, 122, 126, 255), width=3)
    save("Ignition Floor Grating", img, 3, "floor", "Steel floor grating bars (darkens the floor into a grid).", blend="multiply", fade=60)


def scorch_ring():
    def draw(d, S):
        c = S / 2
        import random
        rng = random.Random(3)
        for i in range(40):
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(120, 230)
            x, y = c + r * math.cos(a), c + r * math.sin(a)
            s = rng.uniform(20, 60)
            d.ellipse([x - s, y - s, x + s, y + s], fill=(60, 50, 45, rng.randint(120, 200)))
    img = Image.new("RGBA", (512, 512), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    draw(d, 512)
    img = img.resize((128, 128), Image.LANCZOS)
    save("Ignition Heat Scorch", img, 4, ["floor", "wall"], "A ring of heat scorching round a hot machine (darkens the surface).", blend="multiply",
         fade=80, sizeVariation=0.25, rotation="random")


def main():
    for k in SIGNS:
        sign(k)
    ark_crest()
    section_numbers()
    algae()
    root_cracks()
    hull_seams()
    canopy_arrow()
    fusion_logo()
    core_ring()
    sector_letters()
    plasma_glyph()
    guide_line()
    grating()
    scorch_ring()
    for f in FOLDER.values():
        print("wrote", len([x for x in os.listdir(f) if x.endswith(".json")]), "decals to", f)


if __name__ == "__main__":
    main()
