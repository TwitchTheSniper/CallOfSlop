#!/usr/bin/env python3
"""Writes the pack-local decals for the Space pack's Keelspar Drydock (Maps/Space/Decals/KeelsparDrydock/) and Regolith
Reach (Maps/Space/Decals/RegolithReach/): wall signs cut into square tiles (SIGNS: tiles() gives the tile names and
size the map generators line up), yard and mining-concern logos, hull numbers, bay stencils, weld scorches, deck plates,
survey marks, pad markings and a few floor stencils. All original artwork; every picture is 128 x 128 px (the atlas size).

    python3 tools/space/keelspar_decals.py          (run from the repository root)
"""
import json
import math
import os
import random

from PIL import Image, ImageDraw, ImageFont

BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
COND = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"

# key: (decal base name, width m, height m, background, text colour, text, border colour, map folder)
SIGNS = {
    # Keelspar Drydock (an invented yard of the invented Meridian Hullworks)
    "north_quay": ("Keelspar Sign North Quay", 4.8, 0.8, "#1c3d4a", "#e8f4f2", "NORTH QUAY", "#6fe0e8", "KeelsparDrydock"),
    "south_quay": ("Keelspar Sign South Quay", 4.8, 0.8, "#4a1f1c", "#f4ece8", "SOUTH QUAY", "#ff7a5a", "KeelsparDrydock"),
    "bow_dock": ("Keelspar Sign Bow Dock", 4.0, 0.8, "#1c3d4a", "#e8f4f2", "BOW DOCK", "#6fe0e8", "KeelsparDrydock"),
    "stern_dock": ("Keelspar Sign Stern Dock", 4.4, 0.8, "#4a1f1c", "#f4ece8", "STERN DOCK", "#ff7a5a", "KeelsparDrydock"),
    "fab_shop": ("Keelspar Sign Fab Shop", 3.4, 0.7, "#2a2a2e", "#ffc070", "FAB SHOP", None, "KeelsparDrydock"),
    "dock_office": ("Keelspar Sign Dock Office", 4.2, 0.7, "#2a2a2e", "#f0f0ea", "DOCK OFFICE", None, "KeelsparDrydock"),
    "parts_store": ("Keelspar Sign Parts Store", 4.2, 0.7, "#2a2a2e", "#f0f0ea", "PARTS STORE", None, "KeelsparDrydock"),
    "weld_bay": ("Keelspar Sign Weld Bay", 3.4, 0.7, "#2a2a2e", "#ffc070", "WELD BAY", None, "KeelsparDrydock"),
    "mind_gap": ("Keelspar Sign Mind The Gap", 3.6, 0.6, "#e0b030", "#1c1c1c", "MIND THE GAP", None, "KeelsparDrydock"),
    "no_step": ("Keelspar Sign No Step", 2.4, 0.6, "#e0b030", "#1c1c1c", "NO STEP", None, "KeelsparDrydock"),
    # Regolith Reach (an invented outpost of the invented Pallas Ore Concern)
    "hab_west": ("Regolith Sign Hab West", 3.6, 0.7, "#1c3d4a", "#e8f4f2", "HAB WEST", "#6fe0e8", "RegolithReach"),
    "hab_east": ("Regolith Sign Hab East", 3.6, 0.7, "#4a1f1c", "#f4ece8", "HAB EAST", "#ff7a5a", "RegolithReach"),
    "drill_site": ("Regolith Sign Drill Site", 4.0, 0.7, "#2a2a2e", "#ffc070", "DRILL SITE", None, "RegolithReach"),
    "ore_dock": ("Regolith Sign Ore Dock", 3.4, 0.7, "#2a2a2e", "#f0f0ea", "ORE DOCK", None, "RegolithReach"),
    "solar_farm": ("Regolith Sign Solar Farm", 4.2, 0.7, "#2a2a2e", "#f0f0ea", "SOLAR FARM", None, "RegolithReach"),
    "airlock": ("Regolith Sign Airlock", 3.0, 0.7, "#2a2a2e", "#f0f0ea", "AIRLOCK", None, "RegolithReach"),
    "garage": ("Regolith Sign Garage", 3.0, 0.7, "#2a2a2e", "#f0f0ea", "GARAGE", None, "RegolithReach"),
    "dust_hazard": ("Regolith Sign Dust Hazard", 4.2, 0.6, "#e0b030", "#1c1c1c", "DUST HAZARD", None, "RegolithReach"),
    "blast_zone": ("Regolith Sign Blast Zone", 3.8, 0.6, "#c83a2a", "#fff0e0", "BLAST ZONE", None, "RegolithReach"),
}


def out_dir(folder):
    return os.path.join("Maps", "Space", "Decals", folder)


def tiles(key):
    name, w, h = SIGNS[key][:3]
    n = max(1, int(math.ceil(w / h / 1.15)))
    return [f"{name} {i + 1}" for i in range(n)], w / n, h


def save(folder, name, img, size, surfaces, desc, blend="alpha", rotation=0, fade=None, **extra):
    out = out_dir(folder)
    os.makedirs(out, exist_ok=True)
    fn = name.replace(" ", "_").lower()
    img.save(os.path.join(out, fn + ".png"))
    d = {"format": "call-of-slop-decal/1", "name": name, "category": folder, "description": desc, "author": "Call of Slop",
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
    name, w, h, bg, fg, text, border, folder = SIGNS[key]
    names, tw, th = tiles(key)
    n = len(names)
    W, H = 128 * n, 128
    img = Image.new("RGBA", (W, H), bg)
    d = ImageDraw.Draw(img)
    if border:
        d.rectangle([4, 4, W - 5, H - 5], outline=border, width=7)
    if bg in ("#e0b030", "#c83a2a"):
        dark = "#1c1c1c" if bg == "#e0b030" else "#fff0e0"
        for k in range(-2, n * 6 + 2):
            x = k * 26
            d.polygon([(x, 0), (x + 12, 0), (x - 8, 20), (x - 20, 20)], fill=dark)
            d.polygon([(x, 108), (x + 12, 108), (x - 8, 128), (x - 20, 128)], fill=dark)
    f = fit_font(d, text, BOLD, W - 44, H - 52)
    centred(d, text, f, W, H, fg)
    for i, nm in enumerate(names):
        save(folder, nm, img.crop((i * 128, 0, (i + 1) * 128, 128)), [round(tw, 3), round(th, 3)], "wall",
             f"Tile {i + 1} of {n} of the '{text}' sign (an invented place name).", fade=80)


def stencil(folder, name, text, size, desc, color="#f0ece0", surfaces="floor", fade=120, font=BOLD):
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    f = fit_font(d, text, font, 112, 96)
    centred(d, text, f, 128, 128, color)
    save(folder, name, img, size, surfaces, desc, fade=fade)


# ------------------------------------------------------------------ Keelspar Drydock
def keelspar_logo():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse([30, 30, S - 30, S - 30], outline="#e8a030", width=22)
    # A keel line with four rib arcs over it.
    d.line([(90, 300), (S - 90, 300)], fill="#f0ece0", width=22)
    for k in range(4):
        x = 130 + k * 84
        d.arc([x - 44, 150, x + 44, 300 + 44], 180, 360, fill="#6fe0e8", width=18)
    f = fit_font(d, "MERIDIAN", BOLD, S - 150, 70)
    centred(d, "MERIDIAN", f, S, 70, "#f0ece0", dy=340)
    f2 = fit_font(d, "HULLWORKS", COND, S - 190, 46)
    centred(d, "HULLWORKS", f2, S, 46, "#e8a030", dy=405)
    img = img.resize((128, 128), Image.LANCZOS)
    save("KeelsparDrydock", "Meridian Hullworks Logo", img, 2.6, ["wall", "floor"],
         "The invented Meridian Hullworks roundel: a keel line under four rib arcs.", fade=110)


def hull_number():
    for i, tile in enumerate(("MH", "07")):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        f = fit_font(d, tile, BOLD, 116, 100)
        centred(d, tile, f, 128, 128, "#2a2a2e")
        save("KeelsparDrydock", f"Keelspar Hull Number {i + 1}", img, 2.6, ["wall", "floor"],
             f"Tile {i + 1} of 2 of the hull number 'MH-07' painted on the half-built hull (dark on cream plating).", fade=120)


def bay_numbers():
    for n in range(1, 5):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.rectangle([6, 6, 121, 121], outline="#f0c040", width=8)
        f = fit_font(d, f"B{n}", BOLD, 96, 84)
        centred(d, f"B{n}", f, 128, 128, "#f0c040")
        save("KeelsparDrydock", f"Keelspar Bay Number {n}", img, 2.4, "floor", f"A boxed amber stencil, bay B{n}.", fade=110)


def weld_scorch():
    S = 256
    rng = random.Random(11)
    img = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    for k in range(60):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0, 100) ** 1.0
        x, y = c + rr * math.cos(a), c + rr * math.sin(a)
        s = rng.uniform(4, 22) * (1 - rr / 140)
        g = int(40 + rr * 0.9)
        d.ellipse([x - s, y - s, x + s, y + s], fill=(g, g, g, int(200 - rr)))
    for k in range(40):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(80, 120)
        x, y = c + rr * math.cos(a), c + rr * math.sin(a)
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(70, 60, 50, 160))
    img = img.resize((128, 128), Image.LANCZOS)
    save("KeelsparDrydock", "Keelspar Weld Scorch", img, 1.4, ["floor", "wall"],
         "A sooty weld scorch with spatter round it (darkens the surface).", blend="multiply", fade=60, sizeVariation=0.4)


def deck_plates():
    img = Image.new("RGBA", (128, 128), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for k in (0, 42, 85):
        d.line([(k, 0), (k, 127)], fill=(118, 114, 108, 255), width=3)
    for k in (0, 64):
        d.line([(0, k), (127, k)], fill=(118, 114, 108, 255), width=3)
    for x in (8, 34, 50, 76, 92, 118):
        for y in (8, 56, 72, 120):
            d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(136, 132, 124, 255))
    save("KeelsparDrydock", "Keelspar Deck Plates", img, 4, "floor", "Quay deck plate seams and bolt heads (darkens the deck).", blend="multiply", fade=60)


def crane_mark():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse([40, 40, S - 40, S - 40], outline="#f0c040", width=24)
    d.ellipse([c - 60, c - 60, c + 60, c + 60], outline="#f0c040", width=20)
    for a in range(0, 360, 90):
        t = math.radians(a)
        d.line([(c + 70 * math.cos(t), c + 70 * math.sin(t)), (c + 200 * math.cos(t), c + 200 * math.sin(t))], fill="#f0c040", width=18)
    img = img.resize((128, 128), Image.LANCZOS)
    save("KeelsparDrydock", "Keelspar Crane Mark", img, 5, "floor", "The amber crane-drop target painted under a gantry: a ring with a crosshair.", fade=140)


# ------------------------------------------------------------------ Regolith Reach
def pallas_logo():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.regular_polygon((c, 220, 170), 6, rotation=30, outline="#f0ece0", width=22)
    d.regular_polygon((c, 220, 80), 6, rotation=30, fill="#e8a030")
    f = fit_font(d, "PALLAS ORE", BOLD, S - 120, 70)
    centred(d, "PALLAS ORE", f, S, 70, "#f0ece0", dy=360)
    f2 = fit_font(d, "CONCERN", COND, S - 200, 46)
    centred(d, "CONCERN", f2, S, 46, "#e8a030", dy=425)
    img = img.resize((128, 128), Image.LANCZOS)
    save("RegolithReach", "Pallas Ore Logo", img, 2.6, ["wall", "floor"], "The invented Pallas Ore Concern mark: an amber hexagon in a white one.", fade=110)


def survey_mark():
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.line([(c - 90, c), (c + 90, c)], fill="#ff6a3a", width=14)
    d.line([(c, c - 90), (c, c + 90)], fill="#ff6a3a", width=14)
    d.ellipse([c - 30, c - 30, c + 30, c + 30], outline="#ff6a3a", width=12)
    img = img.resize((128, 128), Image.LANCZOS)
    save("RegolithReach", "Regolith Survey Mark", img, 1.6, "floor", "An orange survey cross sprayed on the regolith.", fade=90, sizeVariation=0.2)


def landing_ring():
    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = S / 2
    d.ellipse([16, 16, S - 16, S - 16], outline="#f0ece0", width=20)
    d.regular_polygon((c, c, 150), 6, rotation=0, outline="#ff6a3a", width=14)
    for k in range(12):
        a = 2 * math.pi * k / 12
        d.line([(c + 196 * math.cos(a), c + 196 * math.sin(a)), (c + 236 * math.cos(a), c + 236 * math.sin(a))], fill="#f0ece0", width=10)
    img = img.resize((128, 128), Image.LANCZOS)
    save("RegolithReach", "Regolith Landing Ring", img, 16, "floor", "A lander pad's markings: a white ring with ticks and an orange hexagon.", fade=160)


def regolith_scuff():
    S = 256
    rng = random.Random(5)
    img = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    for k in range(80):
        x, y = rng.uniform(30, 226), rng.uniform(30, 226)
        s = rng.uniform(6, 30)
        g = rng.randint(150, 205)
        d.ellipse([x - s, y - s * 0.6, x + s, y + s * 0.6], fill=(g, g, g + 4, rng.randint(60, 140)))
    img = img.resize((128, 128), Image.LANCZOS)
    save("RegolithReach", "Regolith Scuff", img, 2.4, "floor", "Darker churned regolith where boots and wheels have been (darkens the ground).",
         blend="multiply", fade=70, sizeVariation=0.4)


def dust_spray():
    S = 256
    rng = random.Random(9)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for k in range(120):
        x, y = rng.uniform(16, 240), rng.uniform(16, 240)
        s = rng.uniform(2, 9)
        d.ellipse([x - s, y - s, x + s, y + s], fill=(226, 224, 218, rng.randint(90, 200)))
    img = img.resize((128, 128), Image.LANCZOS)
    save("RegolithReach", "Regolith Dust Spray", img, 1.6, ["floor", "wall"], "Pale dust blown over a floor or wall by a lander or a rover.", fade=70, sizeVariation=0.4)


def hab_numbers():
    for n in range(1, 4):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.ellipse([6, 6, 121, 121], outline="#f0ece0", width=8)
        f = fit_font(d, f"H{n}", BOLD, 86, 80)
        centred(d, f"H{n}", f, 128, 128, "#f0ece0")
        save("RegolithReach", f"Regolith Hab Number {n}", img, 1.6, ["wall", "floor"], f"A ringed white stencil, hab module H{n}.", fade=100)


def conveyor_arrow():
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for k in range(3):
        y = 22 + k * 34
        d.polygon([(64, y), (108, y + 30), (94, y + 30), (64, y + 10), (34, y + 30), (20, y + 30)], fill="#ff6a3a")
    save("RegolithReach", "Regolith Ore Arrow", img, 1.6, "floor", "Three orange chevrons pointing north along the ore route (turn with rotation).", fade=70)


def main():
    for k in SIGNS:
        sign(k)
    keelspar_logo()
    hull_number()
    bay_numbers()
    weld_scorch()
    deck_plates()
    crane_mark()
    pallas_logo()
    survey_mark()
    landing_ring()
    regolith_scuff()
    dust_spray()
    hab_numbers()
    conveyor_arrow()
    for folder in ("KeelsparDrydock", "RegolithReach"):
        print("wrote", len(os.listdir(out_dir(folder))) // 2, "decals to", out_dir(folder))


if __name__ == "__main__":
    main()
