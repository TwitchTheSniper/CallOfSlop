#!/usr/bin/env python3
"""Writes the pack-local decals of the Space pack's two industrial maps, all original artwork, 128 x 128 px each:

  Maps/Space/Decals/Hollowrock/   09 Hollowrock Refinery: the crucible's glow and hazard ring, dock marks, ore grit,
                                  the invented Tessera Ore Combine logo and the place-name signs
  Maps/Space/Decals/Redvane/      10 Redvane Processor: intake rings, pipe-lane markings, dust rings, condensation
                                  tide marks, the invented Meridian Terraforming Works logo and the signs

Wide signs are cut into square tiles the map lines up side by side (SIGNS, used by hollowrock.py and redvane.py, which
import this module for the tile names).

    python3 tools/space/hollowrock_redvane_decals.py          (run from the repository root)
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
COND = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"

# set -> key -> (decal base name, width m, height m, background, text colour, text, border colour)
SIGNS = {
    "Hollowrock": {
        "dock1": ("Hollowrock Sign Dock 1", 3.6, 0.8, "#1c3d4a", "#e8f4f2", "DOCK 1", "#6fe0e8"),
        "dock2": ("Hollowrock Sign Dock 2", 3.6, 0.8, "#4a1f1c", "#f4ece8", "DOCK 2", "#ff7a5a"),
        "yard": ("Hollowrock Sign Crusher Yard", 4.6, 0.7, "#2a2a2e", "#ffc070", "CRUSHER YARD", None),
        "west": ("Hollowrock Sign West Drift", 3.8, 0.7, "#2a2a2e", "#f0f0ea", "WEST DRIFT", None),
        "east": ("Hollowrock Sign East Drift", 3.8, 0.7, "#2a2a2e", "#f0f0ea", "EAST DRIFT", None),
        "smelter": ("Hollowrock Sign Smelter", 3.4, 0.7, "#2a2a2e", "#ffc070", "SMELTER", None),
        "chasm": ("Hollowrock Sign Mind The Chasm", 4.2, 0.6, "#e0b030", "#1c1c1c", "MIND THE CHASM", None),
        "terrace": ("Hollowrock Sign Terrace", 3.4, 0.7, "#2a2a2e", "#f0f0ea", "TERRACE", None),
    },
    "Redvane": {
        "bunker1": ("Redvane Sign Bunker 1", 3.6, 0.8, "#1c3d4a", "#e8f4f2", "BUNKER 1", "#6fe0e8"),
        "bunker2": ("Redvane Sign Bunker 2", 3.6, 0.8, "#4a1f1c", "#f4ece8", "BUNKER 2", "#ff7a5a"),
        "intake": ("Redvane Sign Intake", 3.4, 0.7, "#2a2a2e", "#ffc070", "INTAKE", None),
        "stack": ("Redvane Sign Main Stack", 4.0, 0.7, "#2a2a2e", "#ffc070", "MAIN STACK", None),
        "tower": ("Redvane Sign Cooling Tower", 4.8, 0.7, "#2a2a2e", "#f0f0ea", "COOLING TOWER", None),
        "pipes": ("Redvane Sign Pipe Field", 3.8, 0.7, "#2a2a2e", "#f0f0ea", "PIPE FIELD", None),
        "pools": ("Redvane Sign Pools", 2.8, 0.7, "#2a2a2e", "#f0f0ea", "POOLS", None),
        "dust": ("Redvane Sign Dust Hazard", 4.2, 0.6, "#e0b030", "#1c1c1c", "DUST HAZARD", None),
    },
}

_set = None
_out = None


def use(set_name):
    global _set, _out
    _set = set_name
    _out = os.path.join("Maps", "Space", "Decals", set_name)


def tiles(set_name, key):
    name, w, h = SIGNS[set_name][key][:3]
    n = max(1, int(math.ceil(w / h / 1.15)))
    return [f"{name} {i + 1}" for i in range(n)], w / n, h


def save(name, img, size, surfaces, desc, blend="alpha", rotation=0, fade=None, **extra):
    os.makedirs(_out, exist_ok=True)
    fn = name.replace(" ", "_").lower()
    img.save(os.path.join(_out, fn + ".png"))
    d = {"format": "call-of-slop-decal/1", "name": name, "category": _set, "description": desc, "author": "Call of Slop",
         "size": size, "rotation": rotation, "blend": blend, "surfaces": surfaces, "image": fn + ".png"}
    if fade:
        d["fadeDistance"] = fade
    d.update(extra)
    with open(os.path.join(_out, fn + ".json"), "w") as f:
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


def big():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img), S


def small(img):
    return img.resize((128, 128), Image.LANCZOS)


def sign(key):
    name, w, h, bg, fg, text, border = SIGNS[_set][key]
    names, tw, th = tiles(_set, key)
    n = len(names)
    W, H = 128 * n, 128
    img = Image.new("RGBA", (W, H), bg)
    d = ImageDraw.Draw(img)
    if border:
        d.rectangle([4, 4, W - 5, H - 5], outline=border, width=7)
    if bg == "#e0b030":
        for k in range(-2, n * 6 + 2):
            x = k * 26
            d.polygon([(x, 0), (x + 12, 0), (x - 8, 20), (x - 20, 20)], fill="#1c1c1c")
            d.polygon([(x, 108), (x + 12, 108), (x - 8, 128), (x - 20, 128)], fill="#1c1c1c")
    f = fit_font(d, text, BOLD, W - 44, H - 52)
    centred(d, text, f, W, H, fg)
    for i, nm in enumerate(names):
        save(nm, img.crop((i * 128, 0, (i + 1) * 128, 128)), [round(tw, 3), round(th, 3)], "wall",
             f"Tile {i + 1} of {n} of the '{text}' sign (an invented place name).", fade=80)


# ------------------------------------------------------------------ Hollowrock ------------------------------------------------------------------

def hollowrock():
    use("Hollowrock")
    for k in SIGNS["Hollowrock"]:
        sign(k)
    # The crucible's glow: a soft orange-white disc that fades out, for the pit floor.
    img, d, S = big()
    c = S / 2
    for k in range(40, 0, -1):
        r = 250 * k / 40
        t = k / 40
        col = (255, int(120 + 120 * (1 - t)), int(30 + 150 * (1 - t) ** 2), int(255 * (1 - t) ** 0.7))
        d.ellipse([c - r, c - r, c + r, c + r], fill=col)
    save("Hollowrock Glow Floor", small(img), 10, "floor", "The crucible's molten glow: a soft orange-white disc for the pit floor.", fade=150)
    # Hazard ring round the pit rim: a dashed black and amber ring.
    img, d, S = big()
    for k in range(48):
        a0 = 360 * k / 48
        d.pieslice([12, 12, S - 12, S - 12], a0, a0 + 7.5, fill="#e0b030" if k % 2 else "#1c1c1c")
    d.ellipse([52, 52, S - 52, S - 52], fill=(0, 0, 0, 0))
    save("Hollowrock Hazard Ring", small(img), 14, "floor", "A dashed amber and black ring round the crucible's rim.", fade=150)
    # Dock marks: a big ring, a cross and the dock number.
    for n, col in ((1, "#6fe0e8"), (2, "#ff7a5a")):
        img, d, S = big()
        c = S / 2
        d.ellipse([14, 14, S - 14, S - 14], outline=col, width=18)
        d.ellipse([110, 110, S - 110, S - 110], outline="#f0ece0", width=8)
        for a in (0, 90, 180, 270):
            x, y = c + 160 * math.cos(math.radians(a)), c + 160 * math.sin(math.radians(a))
            d.line([(c + 95 * math.cos(math.radians(a)), c + 95 * math.sin(math.radians(a))), (x, y)], fill=col, width=16)
        f = fit_font(d, str(n), BOLD, 170, 170)
        centred(d, str(n), f, S, S, "#f0ece0")
        save(f"Hollowrock Dock Mark {n}", small(img), 16, "floor", f"Dock {n}'s landing mark: a big ring, four ticks and the number.", fade=150)
    # Ore grit: dark speckles that darken the ground (multiply).
    img = Image.new("RGBA", (128, 128), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    import random
    rnd = random.Random(9)
    for _ in range(140):
        x, y = rnd.uniform(10, 118), rnd.uniform(10, 118)
        r = rnd.uniform(1.5, 5)
        g = rnd.randint(60, 130)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(g, g - 10, g - 20, 255))
    save("Hollowrock Ore Grit", img, 2.2, "floor", "Dark ore grit spilled over the ground (darkens it).", blend="multiply", rotation="random",
         fade=60, sizeVariation=0.3)
    # Ember streak: a glowing orange spill where slag ran.
    img, d, S = big()
    for k in range(18, 0, -1):
        w = 60 * k / 18
        t = k / 18
        col = (255, int(90 + 120 * (1 - t)), int(20 + 80 * (1 - t)), int(230 * (1 - t) ** 0.6))
        d.line([(80, 300), (260, 200), (440, 240)], fill=col, width=int(w), joint="curve")
    save("Hollowrock Slag Spill", small(img), 3.0, "floor", "A run of still-glowing slag spilt across the floor.", rotation="random", fade=90)
    # Logo: a hexagonal nut round a split rock.
    img, d, S = big()
    c = S / 2
    pts = [(c + 200 * math.cos(math.radians(60 * k)), 200 + 200 * math.sin(math.radians(60 * k))) for k in range(6)]
    d.polygon(pts, outline="#ffb24a", width=22)
    d.polygon([(c - 70, 300), (c - 20, 120), (c + 30, 200), (c + 80, 110), (c + 90, 300)], fill="#c9bda6")
    d.line([(c - 20, 120), (c + 10, 300)], fill="#34363a", width=12)
    f = fit_font(d, "TESSERA", BOLD, S - 70, 90)
    centred(d, "TESSERA", f, S, 100, "#f0ece0", dy=380)
    f2 = fit_font(d, "ORE COMBINE", COND, S - 150, 50)
    centred(d, "ORE COMBINE", f2, S, 50, "#ffb24a", dy=460)
    save("Tessera Logo", small(img), 2.4, ["wall", "floor"], "The invented Tessera Ore Combine logo: a split rock in a hexagonal nut.", fade=110)
    # Conveyor lane arrows (amber chevrons along a belt line).
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for k in range(3):
        y = 22 + k * 34
        d.polygon([(64, y), (108, y + 30), (94, y + 30), (64, y + 10), (34, y + 30), (20, y + 30)], fill="#ffb24a")
    save("Hollowrock Lane Arrow", img, 1.6, "floor", "Three amber chevrons pointing north along a haul lane (turn with rotation).", fade=70)
    print("wrote", len(os.listdir(_out)) // 2, "decals to", _out)


# ------------------------------------------------------------------ Redvane ------------------------------------------------------------------

def redvane():
    use("Redvane")
    for k in SIGNS["Redvane"]:
        sign(k)
    # Intake ring: a wide white ring with a numbered keep-clear zone round each fan.
    for n in (1, 2, 3, 4):
        img, d, S = big()
        c = S / 2
        d.ellipse([14, 14, S - 14, S - 14], outline="#f0ece0", width=16)
        for k in range(36):
            a = 2 * math.pi * k / 36
            d.line([(c + 215 * math.cos(a), c + 215 * math.sin(a)), (c + 238 * math.cos(a), c + 238 * math.sin(a))], fill="#e0b030", width=10)
        f = fit_font(d, f"IN-{n}", BOLD, 300, 150)
        centred(d, f"IN-{n}", f, S, S, "#f0ece0")
        save(f"Redvane Intake Ring {n}", small(img), 12, "floor", f"The keep-clear ring in front of intake fan {n}, numbered.", fade=150)
    # Pipe lane: two white edge lines with a dashed centre, for the pipe field's walkways.
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([6, 0, 14, 127], fill="#e8e4d8")
    d.rectangle([113, 0, 121, 127], fill="#e8e4d8")
    for k in range(4):
        d.rectangle([60, k * 32 + 4, 68, k * 32 + 22], fill="#e0b030")
    save("Redvane Pipe Lane", img, [2.2, 2.2], "floor", "A walkway's two white edge lines with an amber dashed centre (runs north-south; turn it 90).",
         fade=60)
    # Dust ring: a pale ochre ring of blown dust, darkens a little (multiply).
    img = Image.new("RGBA", (128, 128), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for k in range(14, 0, -1):
        g = 255 - int(60 * (1 - k / 14))
        d.ellipse([k * 2, k * 2, 127 - k * 2, 127 - k * 2], fill=(g, g - 8, g - 20, 255))
    d.ellipse([40, 40, 88, 88], fill=(255, 255, 255, 255))
    save("Redvane Dust Ring", img, 4, "floor", "A ring of blown ochre dust where the air swirls (darkens the ground).", blend="multiply", rotation="random",
         fade=70, sizeVariation=0.35)
    # Tide mark: the mineral rim a condensation pool leaves (white crust, multiply-free alpha).
    img, d, S = big()
    c = S / 2
    for k in range(10):
        r = 230 - k * 9
        a = int(140 - k * 12)
        d.ellipse([c - r, c - r, c + r, c + r], outline=(240, 236, 220, a), width=6)
    save("Redvane Tide Mark", small(img), 6, "floor", "Pale mineral tide marks round a condensation pool's edge.", rotation="random", fade=100)
    # Logo: a globe with two vanes.
    img, d, S = big()
    c = S / 2
    d.ellipse([c - 160, 40, c + 160, 360], outline="#e8e4d8", width=20)
    d.ellipse([c - 160, 40, c + 160, 360], fill=(190, 90, 50, 160))
    d.polygon([(c - 20, 200), (c - 230, 120), (c - 230, 170)], fill="#ffb24a")
    d.polygon([(c + 20, 200), (c + 230, 280), (c + 230, 230)], fill="#ffb24a")
    f = fit_font(d, "MERIDIAN", BOLD, S - 70, 90)
    centred(d, "MERIDIAN", f, S, 100, "#f0ece0", dy=380)
    f2 = fit_font(d, "TERRAFORMING WORKS", COND, S - 90, 50)
    centred(d, "TERRAFORMING WORKS", f2, S, 50, "#ffb24a", dy=460)
    save("Meridian Logo", small(img), 2.4, ["wall", "floor"], "The invented Meridian Terraforming Works logo: a red globe with two vanes.", fade=110)
    # Stack ring: a big black and white ring round the main stack's base.
    img, d, S = big()
    for k in range(32):
        a0 = 360 * k / 32
        d.pieslice([8, 8, S - 8, S - 8], a0, a0 + 11.25, fill="#f0ece0" if k % 2 else "#2a2a2e")
    d.ellipse([60, 60, S - 60, S - 60], fill=(0, 0, 0, 0))
    save("Redvane Stack Ring", small(img), 16, "floor", "A chequered black and white ring round the main stack's base.", fade=150)
    # Warning triangle (floor) for the pipe field: a yellow triangle with a drop.
    img, d, S = big()
    d.polygon([(S / 2, 30), (S - 30, S - 40), (30, S - 40)], fill="#e0b030")
    d.polygon([(S / 2, 90), (S - 80, S - 70), (80, S - 70)], fill="#1c1c1c")
    d.polygon([(S / 2, 150), (S / 2 + 60, 300), (S / 2, 360), (S / 2 - 60, 300)], fill="#6fe0e8")
    save("Redvane Vapour Warning", small(img), 1.8, ["floor", "wall"], "A yellow warning triangle with a vapour drop, for pipes and pools.", fade=80)
    print("wrote", len(os.listdir(_out)) // 2, "decals to", _out)


if __name__ == "__main__":
    hollowrock()
    redvane()
