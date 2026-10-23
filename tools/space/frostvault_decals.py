#!/usr/bin/env python3
"""Writes the Space pack's Frostvault decals (Maps/Space/Decals/Frostvault/): hex floor tiling, frost blooms, a
biohazard trefoil, a cryo warning plate, quarantine stripes, the vault's floor ring, hall numbers, wall signs for every
area and the invented Borealis Cryogenics logo. All original artwork; every picture is 128 x 128 px (the atlas size);
wide signs are cut into square tiles the map lines up side by side (SIGNS, used by tools/space/frostvault.py).

    python3 tools/space/frostvault_decals.py          (run from the repository root)
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join("Maps", "Space", "Decals", "Frostvault")
BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
COND = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"
CAT = "Frostvault"
NAVY = "#17283a"
ICE = "#9fdcff"
WHITE = "#f2f7fa"

# key: (decal base name, width m, height m, background, text colour, text, border colour)
SIGNS = {
    "intake_w": ("Frostvault Sign Intake West", 4.2, 0.8, NAVY, ICE, "INTAKE WEST", "#5fb8ea"),
    "intake_e": ("Frostvault Sign Intake East", 4.2, 0.8, "#3a1f1f", "#ffb0a0", "INTAKE EAST", "#ff7a5a"),
    "hall_a": ("Frostvault Sign Pod Hall A", 3.6, 0.7, NAVY, WHITE, "POD HALL A", None),
    "hall_b": ("Frostvault Sign Pod Hall B", 3.6, 0.7, NAVY, WHITE, "POD HALL B", None),
    "vault": ("Frostvault Sign Specimen Vault", 4.4, 0.7, NAVY, ICE, "SPECIMEN VAULT", None),
    "servers": ("Frostvault Sign Server Room", 3.6, 0.7, NAVY, WHITE, "SERVER ROOM", None),
    "lab_n": ("Frostvault Sign Lab North", 3.4, 0.7, NAVY, WHITE, "LAB NORTH", None),
    "lab_s": ("Frostvault Sign Lab South", 3.4, 0.7, NAVY, WHITE, "LAB SOUTH", None),
    "quarantine": ("Frostvault Sign Quarantine", 4.0, 0.6, "#e0b030", "#1c1c1c", "QUARANTINE", None),
    "gallery": ("Frostvault Sign Gallery", 3.4, 0.6, NAVY, WHITE, "OBSERVATION", None),
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
    if key == "quarantine":
        for k in range(-2, n * 6 + 2):
            x = k * 26
            d.polygon([(x, 0), (x + 12, 0), (x - 8, 18), (x - 20, 18)], fill="#1c1c1c")
            d.polygon([(x, 110), (x + 12, 110), (x - 8, 128), (x - 20, 128)], fill="#1c1c1c")
    f = fit_font(d, text, BOLD, W - 44, H - 52)
    centred(d, text, f, W, H, fg)
    for i, nm in enumerate(names):
        save(nm, img.crop((i * 128, 0, (i + 1) * 128, 128)), [round(tw, 3), round(th, 3)], "wall",
             f"Tile {i + 1} of {n} of the cryo vault's '{text}' sign (an invented place name).", fade=80)


def hexagon(d, cx, cy, rr, **kw):
    d.polygon([(cx + rr * math.cos(math.radians(60 * k)), cy + rr * math.sin(math.radians(60 * k))) for k in range(6)], **kw)


def hex_floor():
    # Hex tile joints on a 4 m square (multiply: darkens the joints).
    S = 512
    img = Image.new("RGBA", (S, S), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    rr = 64
    dx, dy = 1.5 * rr, math.sqrt(3) * rr
    for row in range(-1, 8):
        for col in range(-1, 7):
            cx = col * dx
            cy = row * dy + (dy / 2 if col % 2 else 0)
            hexagon(d, cx, cy, rr - 2, outline=(150, 160, 170, 255), width=5)
    save("Frostvault Hex Floor", img.resize((128, 128), Image.LANCZOS), 4, "floor", "Hexagonal floor tile joints on a 4 m square (darkens the floor).",
         blend="multiply", fade=60)


def frost(name, size, seed, desc):
    import random
    rng = random.Random(seed)
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    for k in range(60):
        a = rng.uniform(0, 2 * math.pi)
        rad = rng.uniform(10, 100)
        x, y = c + rad * math.cos(a), c + rad * math.sin(a)
        rr = rng.uniform(6, 30) * (1.1 - rad / 110)
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=(240, 250, 255, int(rng.uniform(40, 120))))
    for k in range(12):
        a = rng.uniform(0, 2 * math.pi)
        ln = rng.uniform(40, 110)
        d.line([(c, c), (c + ln * math.cos(a), c + ln * math.sin(a))], fill=(250, 253, 255, 150), width=3)
        for j in range(3):
            t = rng.uniform(0.3, 0.9)
            bx, by = c + ln * t * math.cos(a), c + ln * t * math.sin(a)
            b = a + rng.choice((-1, 1)) * 0.9
            bl = rng.uniform(8, 25)
            d.line([(bx, by), (bx + bl * math.cos(b), by + bl * math.sin(b))], fill=(250, 253, 255, 130), width=2)
    save(name, img.resize((128, 128), Image.LANCZOS), size, ["floor", "wall"], desc, rotation="random", fade=50, sizeVariation=0.3)


def biohazard():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.rounded_rectangle([20, 20, S - 20, S - 20], radius=40, fill="#1c1c1c")
    d.rounded_rectangle([36, 36, S - 36, S - 36], radius=32, outline="#f08a20", width=10)
    # Trefoil: three arcs round a ring.
    for k in range(3):
        a = math.radians(90 + 120 * k)
        x, y = c + 70 * math.cos(a), c - 70 * math.sin(a)
        d.ellipse([x - 95, y - 95, x + 95, y + 95], outline="#f08a20", width=34)
    d.ellipse([c - 52, c - 52, c + 52, c + 52], fill="#1c1c1c")
    d.ellipse([c - 40, c - 40, c + 40, c + 40], outline="#f08a20", width=16)
    d.ellipse([c - 14, c - 14, c + 14, c + 14], fill="#1c1c1c")
    save("Frostvault Biohazard Sign", img.resize((128, 128), Image.LANCZOS), 0.9, "wall", "An orange biohazard trefoil on a black plate.", fade=60)


def cryo_warning():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([16, 60, S - 16, S - 60], radius=30, fill=NAVY)
    d.rounded_rectangle([30, 74, S - 30, S - 74], radius=24, outline=ICE, width=8)
    c, cy = S / 2, 190
    for k in range(6):
        a = math.radians(60 * k)
        d.line([(c, cy), (c + 90 * math.cos(a), cy + 90 * math.sin(a))], fill=ICE, width=14)
        for t in (0.55, 0.8):
            bx, by = c + 90 * t * math.cos(a), cy + 90 * t * math.sin(a)
            for s in (-1, 1):
                b = a + s * 0.7
                d.line([(bx, by), (bx + 26 * math.cos(b), by + 26 * math.sin(b))], fill=ICE, width=10)
    f = fit_font(d, "CRYO -190C", BOLD, S - 90, 90)
    centred(d, "CRYO -190C", f, S, 100, WHITE, dy=330)
    save("Frostvault Cryo Warning", img.resize((128, 128), Image.LANCZOS), [1.0, 1.0], "wall", "A navy plate with a snowflake and 'CRYO -190C'.", fade=60)


def stripe():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 200, S, 312], fill=(255, 255, 255, 230))
    for k in range(-2, 14):
        x = k * 44
        d.polygon([(x, 200), (x + 22, 200), (x - 20, 312), (x - 42, 312)], fill=(95, 184, 234, 240))
    save("Frostvault Quarantine Stripe", img.resize((128, 128), Image.LANCZOS), [4, 0.9], "floor", "A blue and white diagonal quarantine line (runs east-west; turn it 90).",
         fade=70)


def vault_ring():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    hexagon(d, c, c, 240, outline=ICE, width=18)
    hexagon(d, c, c, 190, outline=WHITE, width=6)
    for k in range(6):
        a = math.radians(60 * k + 30)
        d.line([(c + 150 * math.cos(a), c + 150 * math.sin(a)), (c + 185 * math.cos(a), c + 185 * math.sin(a))], fill=ICE, width=12)
    d.ellipse([c - 40, c - 40, c + 40, c + 40], outline=ICE, width=10)
    save("Frostvault Vault Ring", img.resize((128, 128), Image.LANCZOS), 13, "floor", "The specimen vault's floor marking: an ice-blue hexagon with ticks.", fade=150)


def numbers():
    for n in range(1, 5):
        S = 256
        img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        hexagon(d, S / 2, S / 2, 120, outline=WHITE, width=12)
        f = fit_font(d, f"{n}", BOLD, 150, 150)
        centred(d, f"{n}", f, S, S, ICE)
        save(f"Frostvault Hex Number {n}", img.resize((128, 128), Image.LANCZOS), 2.4, ["floor", "wall"], f"A white hexagon with the number {n} in it.", fade=100)


def logo():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    hexagon(d, c, 190, 150, outline=ICE, width=22)
    for k in range(6):
        a = math.radians(60 * k)
        d.line([(c, 190), (c + 100 * math.cos(a), 190 + 100 * math.sin(a))], fill=WHITE, width=16)
    d.ellipse([c - 28, 190 - 28, c + 28, 190 + 28], fill=NAVY, outline=WHITE, width=10)
    f = fit_font(d, "BOREALIS", BOLD, S - 70, 100)
    centred(d, "BOREALIS", f, S, 110, WHITE, dy=330)
    f2 = fit_font(d, "CRYOGENICS", COND, S - 150, 60)
    centred(d, "CRYOGENICS", f2, S, 60, ICE, dy=440)
    save("Borealis Logo", img.resize((128, 128), Image.LANCZOS), 2.6, ["wall", "floor"], "The invented Borealis Cryogenics logo: a snowflake in a hexagon.", fade=110)


def main():
    for k in SIGNS:
        sign(k)
    hex_floor()
    frost("Frostvault Frost Bloom", 1.8, 3, "A bloom of frost crystals spreading over a cold floor or wall.")
    frost("Frostvault Frost Bloom Small", 0.9, 11, "A small patch of frost round a cold pipe or vent.")
    biohazard()
    cryo_warning()
    stripe()
    vault_ring()
    numbers()
    logo()
    print("wrote", len(os.listdir(OUT)) // 2, "decals to", OUT)


if __name__ == "__main__":
    main()
