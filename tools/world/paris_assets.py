#!/usr/bin/env python3
"""Writes the Parisian asset set for the World Tour pack's Paris Quarter map (and anyone else who wants them):

- Props/Parisian/*.json: reusable street furniture and fittings (bistro tables and chairs, cafe parasols, an advertising
  column, a cast-iron drinking fountain, a tiered square fountain, a newspaper kiosk, metro railings and arch, lantern
  street lamps, bollards, flower stall, chimney pots, window flower boxes, a zinc bar counter and a bottle shelf).
- Buildings/Boulangerie/Props, Buildings/CafeBrasserie/Props and their Furnishing rules (the bakery's and the
  brasserie's own fittings).
- Decals/Signs/*.json + .png: shop-front lettering in French (generic trade words, no brands), blue enamel street
  plaques, and two French road markings (bicycle symbol, LIVRAISON bay).

Usage: python3 tools/world/paris_assets.py [repo root]
"""
import json, math, os, sys
from PIL import Image, ImageDraw, ImageFont

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
REPO = os.path.abspath(REPO)

GREEN = "#2b4636"      # Parisian cast-iron green
GREEN_D = "#1f3328"
CREAM = "#ece3cc"
STONE = "#c9bea6"
BRASS = "#b8923a"
ZINC = "#a3a9ad"


def R(v):
    return round(v, 4)


def part(size, off, color, material=None, rot=None, anim=None):
    p = {"size": [R(s) for s in size], "offset": [R(o) for o in off], "color": color}
    if rot:
        p["rotation"] = [R(r) for r in rot]
    if material:
        p["material"] = material
    if anim:
        p["anim"] = anim
    return p


def octo(d, h, y, color, material=None, x=0.0, z=0.0, anim=None):
    """An octagonal prism d across, h tall, bottom at y (four boxes turned 0/45/90/135)."""
    return [part([d, h, 0.414 * d], [x, y + h / 2, z], color, material, [0, a, 0], anim) for a in (0, 45, 90, 135)]


def write(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        f.write(json.dumps(obj, indent=2) + "\n")


def prop(folder, fname, name, desc, parts, collision="solid", collider=None, scatter=None, breakable=None, category=None, fade=None):
    o = {"format": "call-of-slop-prop/1", "name": name}
    if category:
        o["category"] = category
    o["description"] = desc
    o["collision"] = collision
    if collision == "solid":
        o["collider"] = collider or "bounds"
    if scatter:
        o["scatter"] = scatter
    if fade:
        o["fadeDistance"] = fade
    if breakable:
        o["breakable"] = breakable
    assert 1 <= len(parts) <= 128, (name, len(parts))
    o["parts"] = parts
    write(os.path.join(REPO, folder, fname), o)


P = "Props/Parisian"
SQUARE = {"randomYaw": False, "yawStep": 90}

# ------------------------------------------------------------------ bistro table and chair
p = []
p += [part([0.46, 0.03, 0.07], [0, 0.015, 0], GREEN_D), part([0.07, 0.03, 0.46], [0, 0.015, 0], GREEN_D)]
p += octo(0.07, 0.68, 0.03, GREEN_D, "metal")
p += octo(0.62, 0.035, 0.71, "#e6e1d6")
p += octo(0.64, 0.012, 0.703, BRASS)
p.append(part([0.12, 0.09, 0.12], [0.08, 0.79, 0.05], "#f2efe6"))           # a cup and saucer
p.append(part([0.05, 0.16, 0.05], [-0.12, 0.825, -0.08], "#5a7a3a"))         # a little bottle
prop(P, "BistroTable.json", "Bistro Table", "A round cafe table: a white marble top on a green cast-iron pedestal, a cup and a bottle on it.", p,
     collider=[{"size": [0.62, 0.75, 0.62]}], scatter={"randomYaw": True, "scale": [0.97, 1.03]},
     breakable={"health": 70, "breakBy": "all", "sound": "metal", "debris": {"count": 8, "size": [0.05, 0.18], "colors": ["#e6e1d6", GREEN_D]}})

p = []
for sx in (-0.18, 0.18):
    for sz in (-0.17, 0.17):
        p.append(part([0.03, 0.45, 0.03], [sx, 0.225, sz], "#7a5230", "wood"))
p.append(part([0.44, 0.045, 0.42], [0, 0.47, 0], "#c89a5a", "wood"))
for k in range(5):                                                              # woven seat stripes
    p.append(part([0.42, 0.006, 0.04], [0, 0.496, -0.16 + k * 0.08], "#b8242a" if k % 2 == 0 else "#efe6d2"))
p.append(part([0.03, 0.5, 0.03], [-0.19, 0.74, -0.19], "#7a5230", "wood"))
p.append(part([0.03, 0.5, 0.03], [0.19, 0.74, -0.19], "#7a5230", "wood"))
p.append(part([0.42, 0.2, 0.03], [0, 0.86, -0.2], "#c89a5a", "wood"))
p.append(part([0.42, 0.04, 0.035], [0, 0.98, -0.2], "#7a5230", "wood"))
prop(P, "BistroChair.json", "Bistro Chair", "A rattan cafe chair with a red and cream woven seat (looks only: you walk through it, a shot breaks it).", p,
     collision="cosmetic", scatter={"randomYaw": True},
     breakable={"health": 25, "breakBy": "all", "sound": "wood", "debris": {"count": 6, "size": [0.04, 0.16]}})

# ------------------------------------------------------------------ cafe parasol
p = []
p += octo(0.36, 0.08, 0.0, "#3a3a3a")
p.append(part([0.05, 2.35, 0.05], [0, 1.2, 0], "#d8d2c4"))
p += octo(2.6, 0.08, 2.18, "#a8242a")
p += octo(2.3, 0.12, 2.26, CREAM)
p += octo(1.5, 0.1, 2.38, "#a8242a")
p += octo(0.7, 0.08, 2.48, CREAM)
p.append(part([0.06, 0.12, 0.06], [0, 2.6, 0], "#d8d2c4"))
prop(P, "CafeParasol.json", "Cafe Parasol", "A big cream and red cafe parasol for a terrace (looks only).", p, collision="cosmetic",
     scatter={"randomYaw": True, "scale": [0.95, 1.05]}, fade=110)

# ------------------------------------------------------------------ advertising column (Morris-style)
p = []
p += octo(1.36, 0.36, 0.0, GREEN, "metal")
p += octo(1.16, 2.4, 0.36, "#ddd3b8")
posters = ["#c23a2a", "#2f5a8a", "#e0b040", "#3a7a4a", "#8a3a6a", "#f0ece0", "#d06a2a", "#203040"]
for k in range(8):
    a = math.radians(k * 45)
    r = 0.59
    p.append(part([0.4, 0.9 if k % 2 else 1.2, 0.02], [r * math.sin(a), 1.55 if k % 2 else 1.4, r * math.cos(a)], posters[k], rot=[0, k * 45, 0]))
    p.append(part([0.3, 0.3, 0.02], [r * math.sin(a), 2.32, r * math.cos(a)], posters[(k + 3) % 8], rot=[0, k * 45, 0]))
p += octo(1.32, 0.16, 2.76, GREEN, "metal")
p += octo(1.0, 0.28, 2.92, GREEN, "metal")
p += octo(0.6, 0.24, 3.2, GREEN, "metal")
p.append(part([0.1, 0.3, 0.1], [0, 3.58, 0], GREEN))
prop(P, "AdvertisingColumn.json", "Advertising Column", "A green cast-iron advertising column with a domed cap, pasted all round with theatre and concert posters.", p,
     collider=[{"size": [1.2, 3.5, 1.2]}], scatter=SQUARE)

# ------------------------------------------------------------------ cast-iron drinking fountain
p = []
p += octo(0.95, 0.5, 0.0, GREEN, "metal")
p += octo(0.7, 0.08, 0.5, GREEN_D)
for sx, sz in ((0.2, 0.2), (-0.2, 0.2), (0.2, -0.2), (-0.2, -0.2)):
    p.append(part([0.12, 1.0, 0.12], [sx, 1.08, sz], GREEN, "metal"))
    p.append(part([0.16, 0.14, 0.16], [sx, 1.48, sz], GREEN_D))
p.append(part([0.03, 0.9, 0.03], [0, 1.03, 0], "#a8d0f0", anim={"type": "bob", "distance": 0.04, "period": 0.7}))
p += octo(0.86, 0.1, 1.58, GREEN, "metal")
p += octo(0.64, 0.26, 1.68, GREEN, "metal")
p += octo(0.3, 0.2, 1.94, GREEN, "metal")
p.append(part([0.06, 0.16, 0.06], [0, 2.2, 0], GREEN))
prop(P, "CastIronDrinkingFountain.json", "Cast Iron Drinking Fountain", "A green cast-iron drinking fountain: four columns holding up a little dome, water trickling in the middle.", p,
     collider=[{"size": [0.9, 2.2, 0.9]}], scatter=SQUARE)

# ------------------------------------------------------------------ tiered square fountain
p = []
p += octo(5.2, 0.6, 0.0, STONE, "concrete")
p += octo(4.8, 0.03, 0.56, "#3d6a80", anim=None)
p += octo(0.9, 1.2, 0.56, STONE, "concrete")
p += octo(2.4, 0.26, 1.4, STONE, "concrete")
p += octo(2.1, 0.03, 1.64, "#4a7890")
p += octo(0.45, 0.7, 1.66, STONE, "concrete")
p += octo(1.2, 0.18, 2.3, STONE, "concrete")
p.append(part([0.1, 0.6, 0.1], [0, 2.7, 0], "#b8d8f0", anim={"type": "bob", "distance": 0.15, "period": 1.6, "jitter": 0.2}))
p.append(part([0.2, 0.1, 0.2], [0, 3.05, 0], "#d8ecff", anim={"type": "bob", "distance": 0.15, "period": 1.6, "jitter": 0.2}))
for k in range(8):                                                              # falls from the middle bowl
    a = math.radians(k * 45)
    p.append(part([0.06, 0.8, 0.06], [1.12 * math.sin(a), 1.0, 1.12 * math.cos(a)], "#a8d0f0",
                  anim={"type": "bob", "distance": 0.05, "period": 0.8, "phase": k / 8.0}))
for k in range(8):                                                              # sparkles on the pool
    a = math.radians(k * 45 + 22.5)
    p.append(part([0.08, 0.02, 0.08], [1.9 * math.sin(a), 0.6, 1.9 * math.cos(a)], "white",
                  anim={"type": "pulse", "period": 1.3, "color": "white", "off": "#5a8aa8", "glow": 0.2}))
prop(P, "TieredFountain.json", "Tiered Square Fountain", "A town-square fountain: a wide stone basin, a middle bowl spilling into it and a jet on top.", p,
     collider=[{"size": [5.0, 0.6, 5.0]}, {"size": [0.9, 2.5, 0.9]}], scatter=SQUARE)

# ------------------------------------------------------------------ newspaper kiosk
p = []
p += octo(2.4, 0.25, 0.0, GREEN_D, "metal")
p += octo(2.3, 2.1, 0.25, GREEN, "metal")
for k, c in enumerate(["#c23a2a", "#2f5a8a", "#e0b040", "#f0ece0", "#3a7a4a", "#d06a2a"]):   # magazines on the front counter
    p.append(part([0.28, 0.36, 0.03], [-0.75 + k * 0.3, 1.25, 1.17], c))
p.append(part([1.9, 0.06, 0.35], [0, 0.95, 1.25], "#7a5230", "wood"))              # counter flap
for k in range(4):
    a = math.radians([90, 135, -90, -135][k])
    p.append(part([0.7, 1.0, 0.02], [1.16 * math.sin(a), 1.6, 1.16 * math.cos(a)], posters[k * 2], rot=[0, math.degrees(a), 0]))
p += octo(3.0, 0.12, 2.35, GREEN_D, "metal")
p += octo(2.2, 0.36, 2.47, GREEN, "metal")
p += octo(1.3, 0.3, 2.83, GREEN, "metal")
p.append(part([0.12, 0.35, 0.12], [0, 3.28, 0], BRASS))
p.append(part([1.4, 0.28, 0.04], [0, 2.1, 1.16], "#e8d8a0"))                       # name board
prop(P, "NewsKiosk.json", "News Kiosk", "A green octagonal newspaper kiosk with a domed roof, magazines on the counter and posters round the sides.", p,
     collider=[{"size": [2.3, 2.9, 2.3]}], scatter=SQUARE)

# ------------------------------------------------------------------ metro railing and arch
p = []
p.append(part([3.0, 0.07, 0.07], [0, 1.0, 0], GREEN, "metal"))
p.append(part([3.0, 0.05, 0.05], [0, 0.18, 0], GREEN))
for k in range(13):
    x = -1.44 + k * 0.24
    p.append(part([0.03, 0.82, 0.03], [x, 0.59, 0], GREEN_D))
    if k % 3 == 1:
        p.append(part([0.12, 0.12, 0.02], [x + 0.12, 0.62, 0], GREEN, rot=[0, 0, 45]))   # scrolls
p.append(part([0.1, 1.12, 0.1], [-1.5, 0.56, 0], GREEN, "metal"))
p.append(part([0.1, 1.12, 0.1], [1.5, 0.56, 0], GREEN, "metal"))
p.append(part([0.16, 0.08, 0.16], [-1.5, 1.16, 0], GREEN_D))
p.append(part([0.16, 0.08, 0.16], [1.5, 1.16, 0], GREEN_D))
prop(P, "MetroRailing.json", "Metro Railing", "A 3 m stretch of green cast-iron railing with scrolls, for metro stairs and embankments.", p,
     collider=[{"size": [3.1, 1.08, 0.14]}], scatter={"randomYaw": False, "yawStep": 90})

p = []
for sx in (-1.55, 1.55):
    p += octo(0.26, 0.4, 0.0, GREEN_D, "metal", x=sx)
    p.append(part([0.11, 2.6, 0.11], [sx, 1.7, 0], GREEN, "metal"))
    p.append(part([0.09, 0.6, 0.09], [sx * 0.94, 3.15, 0], GREEN, "metal", rot=[0, 0, -12 if sx > 0 else 12]))
    p.append(part([0.26, 0.34, 0.26], [sx * 0.88, 3.55, 0], "#ff9a30",
                  anim={"type": "flicker", "rate": 2, "duty": 0.97, "color": "#ffb050", "off": "#a05a20", "glow": 1.2}))
p.append(part([3.2, 0.09, 0.09], [0, 2.95, 0], GREEN, "metal"))
p.append(part([2.6, 0.06, 0.06], [0, 2.4, 0], GREEN))
for k in range(9):
    p.append(part([0.04, 0.5, 0.04], [-1.2 + k * 0.3, 2.67, 0], GREEN_D))
prop(P, "MetroArch.json", "Metro Entrance Arch", "The cast-iron arch over a metro staircase: two green stems with amber lamps and a frieze between them (hang your own sign on it).", p,
     collider=[{"size": [0.26, 3.0, 0.26], "offset": [-1.55, 1.5, 0]}, {"size": [0.26, 3.0, 0.26], "offset": [1.55, 1.5, 0]}], scatter=SQUARE)

# ------------------------------------------------------------------ lantern lamp, bollard
p = []
p += octo(0.4, 0.55, 0.0, GREEN_D, "metal")
p += octo(0.26, 0.12, 0.55, GREEN)
p += octo(0.13, 2.9, 0.67, GREEN_D, "metal")
p += octo(0.22, 0.1, 3.55, GREEN)
p.append(part([0.36, 0.5, 0.36], [0, 3.9, 0], "#ffd890", anim={"type": "flicker", "rate": 3, "duty": 0.96, "color": "#ffe0a0", "off": "#806030", "glow": 1.0}))
for sx, sz in ((0.18, 0.18), (-0.18, 0.18), (0.18, -0.18), (-0.18, -0.18)):
    p.append(part([0.03, 0.52, 0.03], [sx, 3.9, sz], GREEN_D))
p.append(part([0.46, 0.06, 0.46], [0, 4.18, 0], GREEN_D))
p.append(part([0.3, 0.1, 0.3], [0, 4.26, 0], GREEN_D))
p.append(part([0.08, 0.14, 0.08], [0, 4.38, 0], GREEN_D))
prop(P, "LanternStreetLamp.json", "Lantern Street Lamp", "A cast-iron street lamp in old-town green with a four-sided glass lantern on top.", p,
     collider=[{"size": [0.4, 0.6, 0.4]}, {"size": [0.16, 4.4, 0.16], "offset": [0, 2.2, 0]}], scatter=SQUARE)

p = []
p += octo(0.2, 0.06, 0.0, GREEN_D)
p += octo(0.15, 0.62, 0.06, GREEN, "metal")
p += octo(0.19, 0.06, 0.5, GREEN_D)
p += octo(0.12, 0.1, 0.68, GREEN)
prop(P, "CastIronBollard.json", "Cast Iron Bollard", "A short green cast-iron bollard with a ring and a rounded top, for pavement edges.", p,
     collider=[{"size": [0.18, 0.78, 0.18]}], scatter={"randomYaw": False})

# ------------------------------------------------------------------ flower stall
p = []
p.append(part([1.9, 0.5, 1.2], [0, 0.25, 0], "#5a3a22", "wood"))
p.append(part([1.9, 0.4, 0.8], [0, 0.7, -0.2], "#6a4628", "wood"))
p.append(part([1.9, 0.4, 0.4], [0, 1.1, -0.4], "#5a3a22", "wood"))
blooms = ["#d02a3a", "#f0c020", "#e870a0", "#f4f0f0", "#8a3ab0", "#f07a20"]
k = 0
for (y, z) in ((0.5, 0.38), (0.9, -0.05), (1.3, -0.4)):
    for i in range(5):
        x = -0.76 + i * 0.38
        p.append(part([0.26, 0.24, 0.26], [x, y + 0.12, z], "#3a4a5a", "metal"))
        p.append(part([0.3, 0.2, 0.3], [x, y + 0.34, z], "#3a6a2a"))
        p.append(part([0.26, 0.12, 0.26], [x, y + 0.48, z], blooms[k % 6]))
        k += 1
p.append(part([0.05, 2.2, 0.05], [-0.9, 1.1, 0.55], "#5a3a22"))
p.append(part([0.05, 2.2, 0.05], [0.9, 1.1, 0.55], "#5a3a22"))
p.append(part([2.1, 0.06, 1.5], [0, 2.22, -0.05], "#2f5a3a"))
prop(P, "FlowerStall.json", "Flower Stall", "A florist's tiered wooden stand of zinc buckets full of flowers, under a green canopy.", p,
     collider=[{"size": [1.9, 1.5, 1.2]}], scatter=SQUARE,
     breakable={"health": 110, "breakBy": "all", "sound": "wood", "debris": {"count": 14, "size": [0.06, 0.3], "colors": ["#5a3a22", "#d02a3a", "#f0c020", "#3a6a2a"]}})

# ------------------------------------------------------------------ chimney stack, window box
p = []
p.append(part([1.7, 1.1, 0.6], [0, 0.55, 0], "#c8b8a0", "concrete"))
p.append(part([1.8, 0.12, 0.7], [0, 1.16, 0], "#8a5a40", "brick"))
for i in range(6):
    x = -0.7 + i * 0.28
    p += octo(0.18, 0.36 + (i % 2) * 0.08, 1.22, "#b0603a", x=x)
prop(P, "ChimneyPotStack.json", "Chimney Pot Stack", "A rendered rooftop chimney stack with a brick cap and a row of terracotta chimney pots.", p,
     collider=[{"size": [1.7, 1.2, 0.6]}], scatter=SQUARE)

p = []
p.append(part([1.0, 0.2, 0.24], [0, 0.1, 0], "#a85a38"))
for i in range(6):
    p.append(part([0.16, 0.14, 0.16], [-0.4 + i * 0.16, 0.26, (i % 2) * 0.04 - 0.02], "#3a6a2a"))
    p.append(part([0.1, 0.08, 0.1], [-0.4 + i * 0.16, 0.36, (i % 2) * 0.04 - 0.02], "#d02a3a" if i % 3 else "#f4f0f0"))
prop(P, "WindowFlowerBox.json", "Window Flower Box", "A terracotta window box of red and white geraniums, for sills and balcony rails (looks only).", p, collision="cosmetic",
     scatter={"randomYaw": False}, fade=60)

# ------------------------------------------------------------------ bar counter and bottle shelf
p = []
p.append(part([3.0, 1.0, 0.62], [0, 0.5, 0.0], "#4a2a18", "wood"))
for i in range(5):
    p.append(part([0.5, 0.6, 0.02], [-1.2 + i * 0.6, 0.55, 0.315], "#5e3820", "wood"))
p.append(part([3.1, 0.05, 0.74], [0, 1.03, 0.02], ZINC, "metal"))
p.append(part([3.0, 0.04, 0.04], [0, 0.18, 0.38], BRASS))
for i in range(3):
    p.append(part([0.05, 0.36, 0.05], [-0.6 + i * 0.14, 1.23, -0.12], BRASS))
    p.append(part([0.04, 0.06, 0.1], [-0.6 + i * 0.14, 1.38, -0.06], "#202020"))
p.append(part([0.5, 0.42, 0.38], [0.9, 1.26, -0.1], "#b02020"))
p.append(part([0.42, 0.06, 0.32], [0.9, 1.5, -0.1], "chrome"))
p.append(part([0.1, 0.12, 0.1], [0.2, 1.11, 0.1], "#f0ece0"))
p.append(part([0.1, 0.12, 0.1], [0.32, 1.11, 0.12], "#f0ece0"))
prop(P, "ZincBarCounter.json", "Zinc Bar Counter", "A bistro bar: dark panelled wood with a zinc top and a brass foot rail, beer taps, an espresso machine and cups.", p,
     collider=[{"size": [3.0, 1.06, 0.7]}], scatter=SQUARE,
     breakable={"health": 160, "breakBy": "all", "sound": "wood", "debris": {"count": 10, "size": [0.06, 0.25], "colors": ["#4a2a18", ZINC]}})

p = []
p.append(part([2.4, 2.1, 0.36], [0, 1.05, 0], "#4a2a18", "wood"))
p.append(part([2.2, 0.8, 0.02], [0, 1.6, 0.18], "#9ab0b8"))               # mirror
for row, y in enumerate((0.45, 0.95, 2.15)):
    if row < 2:
        p.append(part([2.3, 0.04, 0.3], [0, y, 0.05], "#5e3820", "wood"))
    for i in range(10):
        c = ["#3a5a2a", "#6a1a1a", "#c8a040", "#2a2a2a", "#e8e0c8"][(i + row) % 5]
        p.append(part([0.08, 0.3, 0.08], [-1.0 + i * 0.22, y + 0.17 if row < 2 else 2.25, 0.08], c))
prop(P, "BarBottleShelf.json", "Bar Bottle Shelf", "The back of a bar: dark wood shelves of bottles round a long mirror (stand it against a wall).", p,
     collider=[{"size": [2.4, 2.1, 0.36]}], scatter=SQUARE,
     breakable={"health": 120, "breakBy": "all", "sound": "glass", "debris": {"count": 16, "size": [0.04, 0.16], "colors": ["#3a5a2a", "#6a1a1a", "#9ab0b8"]}})

# ------------------------------------------------------------------ building-own props: bakery
B = "Buildings/Boulangerie/Props"
p = []
p.append(part([2.2, 0.85, 0.7], [0, 0.425, 0], "#e8dcc0", "wood"))
p.append(part([2.24, 0.04, 0.74], [0, 0.87, 0], "#d8d0c0"))
p.append(part([2.1, 0.42, 0.62], [0, 1.1, 0.02], "glass"))
pastry = ["#c8843a", "#b06a2a", "#e0b070", "#8a4a2a", "#f0d8a0", "#d89a50"]
for i in range(7):
    p.append(part([0.22, 0.08, 0.16], [-0.84 + i * 0.28, 0.93, 0.12], pastry[i % 6]))
    p.append(part([0.2, 0.07, 0.14], [-0.84 + i * 0.28, 0.93, -0.14], pastry[(i + 2) % 6]))
p.append(part([2.24, 0.03, 0.66], [0, 1.33, 0.02], "#d8d0c0"))
p.append(part([0.36, 0.24, 0.3], [0.8, 1.46, -0.1], "#3a3a3a"))           # till
prop(B, "BreadDisplayCounter.json", "Bread Display Counter", "The bakery counter: a glass case of croissants, tarts and eclairs, a till on top.", p,
     collider=[{"size": [2.2, 1.35, 0.7]}], scatter=SQUARE,
     breakable={"health": 90, "breakBy": "all", "sound": "glass", "debris": {"count": 12, "size": [0.04, 0.16], "colors": ["glass", "#c8843a", "#e8dcc0"]}})
p = []
p.append(part([2.0, 2.0, 0.4], [0, 1.0, -0.02], "#6a4628", "wood"))
for row in range(4):
    y = 0.35 + row * 0.45
    p.append(part([1.9, 0.04, 0.36], [0, y, 0.02], "#8a6038", "wood"))
    for i in range(7 if row % 2 == 0 else 5):
        if row % 2 == 0:
            p.append(part([0.07, 0.4, 0.07], [-0.78 + i * 0.26, y + 0.22, 0.08], "#c8843a", rot=[12, 0, 0]))      # baguettes standing
        else:
            p.append(part([0.3, 0.14, 0.24], [-0.72 + i * 0.36, y + 0.09, 0.06], "#b06a2a"))                      # round loaves
prop(B, "BreadWallRack.json", "Bread Wall Rack", "A wooden wall rack of baguettes standing in rows and round country loaves.", p,
     collider=[{"size": [2.0, 2.0, 0.4]}], scatter=SQUARE,
     breakable={"health": 100, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.05, 0.3], "colors": ["#6a4628", "#c8843a"]}})
p = []
p.append(part([1.6, 1.9, 1.1], [0, 0.95, 0], "#9aa0a4", "metal"))
for i in range(3):
    p.append(part([1.4, 0.32, 0.03], [0, 0.45 + i * 0.5, 0.56], "#2a2a2a"))
    p.append(part([1.2, 0.04, 0.05], [0, 0.66 + i * 0.5, 0.58], "chrome"))
p.append(part([0.3, 0.4, 0.04], [0.55, 1.75, 0.56], "#d06a2a", anim={"type": "pulse", "period": 3.0, "color": "#ff9a40", "off": "#7a3a1a", "glow": 0.6}))
prop(B, "BakeryDeckOven.json", "Bakery Deck Oven", "A stainless steel three-deck bread oven with glowing peepholes.", p,
     collider=[{"size": [1.6, 1.9, 1.1]}], scatter=SQUARE)
p = []
for sx in (-0.85, 0.85):
    for sz in (-0.4, 0.4):
        p.append(part([0.07, 0.86, 0.07], [sx, 0.43, sz], "#6a4628", "wood"))
p.append(part([1.9, 0.07, 0.95], [0, 0.89, 0], "#c8a878", "wood"))
p.append(part([0.5, 0.08, 0.4], [-0.3, 0.97, 0], "#f0e8d8"))
p.append(part([0.12, 0.08, 0.6], [0.5, 0.97, 0.1], "#e8d8b8"))
p.append(part([0.12, 0.08, 0.6], [0.7, 0.97, 0.1], "#e8d8b8"))
p.append(part([1.8, 0.05, 0.85], [0, 0.18, 0], "#8a6038", "wood"))
p.append(part([0.5, 0.3, 0.35], [-0.5, 0.36, 0], "#e8e0d0"))
prop(B, "BakersTable.json", "Bakers Table", "A floury wooden work table with dough and shaped loaves, a sack of flour underneath.", p,
     collider=[{"size": [1.9, 0.95, 0.95]}], scatter=SQUARE,
     breakable={"health": 90, "breakBy": "all", "sound": "wood", "debris": {"count": 10, "size": [0.05, 0.3]}})

write(os.path.join(REPO, "Buildings/Boulangerie/Furnishing/boulangerie.json"), {
    "format": "call-of-slop-furnishing/1", "type": "boulangerie",
    "description": "The bakery shop: bread racks on the walls, a basket of baguettes, a chalkboard; the counter is placed by the building.",
    "items": [
        {"props": ["Bread Wall Rack"], "place": "wall", "count": [1, 2], "prefer": "far"},
        {"props": ["Bistro Table"], "place": "corner", "chance": 0.6},
        {"props": ["Sandwich Board", "Large Flower Pot"], "place": "corner", "chance": 0.7},
        {"props": ["Cardboard Box"], "place": "floor", "count": [0, 1]}]})
write(os.path.join(REPO, "Buildings/Boulangerie/Furnishing/bakehouse.json"), {
    "format": "call-of-slop-furnishing/1", "type": "bakehouse",
    "description": "The bakehouse behind the shop: deck ovens on the walls, the bakers' table in the middle, flour and racks.",
    "items": [
        {"props": ["Bakery Deck Oven"], "place": "wall", "count": [1, 2], "prefer": "far"},
        {"props": ["Bakers Table"], "place": "centre"},
        {"props": ["Metal Shelving"], "place": "wall", "chance": 0.8},
        {"props": ["Potato Sacks", "Cardboard Stack"], "place": "corner", "count": [1, 2]},
        {"props": ["Mop and Bucket", "Broom", "Bucket"], "place": "floor", "count": [0, 2]}]})

# ------------------------------------------------------------------ building-own props: brasserie
B = "Buildings/CafeBrasserie/Props"
p = []
p.append(part([2.0, 0.45, 0.6], [0, 0.225, 0.02], "#5a1a1e"))
p.append(part([2.0, 0.12, 0.56], [0, 0.5, 0.04], "#8a2228"))
p.append(part([2.0, 0.6, 0.16], [0, 0.85, -0.22], "#8a2228"))
p.append(part([2.04, 0.08, 0.2], [0, 1.19, -0.22], "#4a2a18", "wood"))
p.append(part([2.04, 0.06, 0.06], [0, 0.06, 0.3], BRASS))
prop(B, "BrasserieBanquette.json", "Brasserie Banquette", "A red leather bench seat along a brasserie wall, with a wooden top rail.", p,
     collider=[{"size": [2.0, 0.6, 0.6]}], scatter=SQUARE,
     breakable={"health": 110, "breakBy": "all", "sound": "soft", "debris": {"count": 8, "size": [0.06, 0.25], "colors": ["#8a2228", "#4a2a18"]}})
p = []
p.append(part([0.9, 1.6, 0.04], [0, 1.2, 0], "#1a1a1a"))
p.append(part([0.96, 1.66, 0.03], [0, 1.2, -0.02], "#4a2a18", "wood"))
for i in range(6):
    p.append(part([0.6 - (i % 3) * 0.1, 0.05, 0.01], [-0.05, 1.8 - i * 0.2, 0.025], "#f0ece0"))
p.append(part([0.05, 0.4, 0.05], [-0.4, 0.2, 0.15], "#4a2a18"))
p.append(part([0.05, 0.4, 0.05], [0.4, 0.2, 0.15], "#4a2a18"))
prop(B, "MenuChalkboard.json", "Menu Chalkboard", "A tall blackboard of the day's dishes in white chalk, leaning against the wall.", p, collision="cosmetic",
     scatter=SQUARE, breakable={"health": 30, "breakBy": "all", "sound": "wood", "debris": {"count": 6, "size": [0.05, 0.2]}})

write(os.path.join(REPO, "Buildings/CafeBrasserie/Furnishing/brasserie.json"), {
    "format": "call-of-slop-furnishing/1", "type": "brasserie",
    "description": "The brasserie room: red banquettes along the walls, little marble tables with rattan chairs, the menu board, a coat stand.",
    "items": [
        {"id": "ban", "props": ["Brasserie Banquette"], "place": "wall", "count": [1, 3], "prefer": "far"},
        {"props": ["Bistro Table"], "place": "front", "of": "ban", "distance": 0.15},
        {"id": "t", "props": ["Bistro Table"], "place": "floor", "count": [3, 5], "gap": 0.75},
        {"props": ["Bistro Chair"], "place": "around", "of": "t", "count": [1, 2], "distance": 0.05},
        {"props": ["Menu Chalkboard"], "place": "wall", "chance": 0.8},
        {"props": ["Coat Stand", "Large Flower Pot"], "place": "corner", "count": [1, 2]},
        {"props": ["Ceiling Fan"], "place": "ceiling", "chance": 0.7, "area": [20, 1000]}]})
write(os.path.join(REPO, "Buildings/CafeBrasserie/Furnishing/salle.json"), {
    "format": "call-of-slop-furnishing/1", "type": "salle",
    "description": "The upstairs dining room: a long table for parties, tables for two, a sideboard of plates, a piano.",
    "items": [
        {"id": "long", "props": ["Dining Table"], "place": "centre"},
        {"props": ["Dining Chair"], "place": "around", "of": "long", "count": [4, 6], "distance": 0.1},
        {"id": "t", "props": ["Bistro Table"], "place": "floor", "count": [2, 3], "gap": 0.7},
        {"props": ["Bistro Chair"], "place": "around", "of": "t", "count": [1, 2], "distance": 0.05},
        {"props": ["Chest of Drawers", "Upright Piano", "Bar Bottle Shelf"], "place": "wall", "count": [1, 2], "prefer": "far"},
        {"props": ["Large Flower Pot", "Floor Lamp"], "place": "corner", "count": [1, 2]}]})


# ------------------------------------------------------------------ sign decals (Decals/Signs)
FONT_SERIF = "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"
FONT_SANS = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"
D = os.path.join(REPO, "Decals/Signs")
os.makedirs(D, exist_ok=True)


def text_image(path, text, aspect, fg, bg=None, border=None, font=FONT_SERIF, sub=None, sub_fg=None, pad=0.08):
    """Draws text into a wide picture aspect:1, then squeezes it into 128 x 128 (the decal stretches it back)."""
    H = 160
    W = int(H * aspect)
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0) if bg is None else bg)
    d = ImageDraw.Draw(im)
    if border:
        bw = max(6, H // 14)
        d.rectangle([bw // 2, bw // 2, W - 1 - bw // 2, H - 1 - bw // 2], outline=border, width=bw)
    main_h = H * (0.56 if sub else 0.72)
    size = int(main_h)
    while size > 10:
        f = ImageFont.truetype(font, size)
        bb = d.textbbox((0, 0), text, font=f)
        if bb[2] - bb[0] <= W * (1 - 2 * pad) and bb[3] - bb[1] <= main_h:
            break
        size -= 2
    bb = d.textbbox((0, 0), text, font=f)
    ty = (H * (0.42 if sub else 0.5)) - (bb[3] + bb[1]) / 2
    d.text(((W - (bb[2] - bb[0])) / 2 - bb[0], ty), text, font=f, fill=fg)
    if sub:
        s2 = int(H * 0.2)
        f2 = ImageFont.truetype(FONT_SANS, s2)
        bb2 = d.textbbox((0, 0), sub, font=f2)
        while bb2[2] - bb2[0] > W * 0.8 and s2 > 8:
            s2 -= 2
            f2 = ImageFont.truetype(FONT_SANS, s2)
            bb2 = d.textbbox((0, 0), sub, font=f2)
        d.text(((W - (bb2[2] - bb2[0])) / 2 - bb2[0], H * 0.8 - (bb2[3] + bb2[1]) / 2), sub, font=f2, fill=sub_fg or fg)
    im = im.resize((128, 128), Image.LANCZOS)
    im.save(path, "PNG")


def decal(fname, name, desc, size, surfaces, image, rotation=0, blend="alpha"):
    write(os.path.join(D, fname), {
        "format": "call-of-slop-decal/1", "name": name, "description": desc, "author": "Call of Slop",
        "size": size, "rotation": rotation, "tintVariation": 0.04, "blend": blend, "surfaces": surfaces, "image": image})


GOLD = (226, 190, 108, 255)
CREAM_T = (240, 232, 210, 255)
TRADES = [("Boulangerie", "BOULANGERIE", "a bakery"), ("Patisserie", "PÂTISSERIE", "a pastry shop"), ("Brasserie", "BRASSERIE", "a brasserie"),
          ("Cafe", "CAFÉ", "a cafe"), ("Tabac", "TABAC", "a tobacconist"), ("Pharmacie", "PHARMACIE", "a chemist"),
          ("Librairie", "LIBRAIRIE", "a bookshop"), ("Fromagerie", "FROMAGERIE", "a cheese shop")]
for key, word, what in TRADES:
    aspect = 6.0 if len(word) > 6 else 4.0
    w = 3.2 if aspect == 6.0 else 2.2
    text_image(os.path.join(D, f"French{key}.png"), word, aspect, GOLD if key not in ("Pharmacie", "Tabac") else CREAM_T)
    decal(f"FrenchSign{key}.json", f"French Sign {key}", f"Gilded shop-front lettering in French for {what} (for a fascia board).",
          [w, round(w / aspect, 3)], ["wall"], f"French{key}.png")
text_image(os.path.join(D, "FrenchMetro.png"), "MÉTRO", 4.0, (236, 200, 120, 255), bg=(32, 56, 44, 255), border=(236, 200, 120, 255))
decal("FrenchSignMetro.json", "French Sign Metro", "A metro sign: amber lettering on a dark green enamel plate with a border.", [1.6, 0.4], ["wall"], "FrenchMetro.png")

# ------------------------------------------------------------------ the Paris Quarter's own decals (Maps/World/Decals/Paris): invented names
PD = os.path.join(REPO, "Maps/World/Decals/Paris")
os.makedirs(PD, exist_ok=True)


def pdecal(fname, name, desc, size, surfaces, image):
    write(os.path.join(PD, fname), {
        "format": "call-of-slop-decal/1", "name": name, "description": desc, "author": "Call of Slop",
        "size": size, "rotation": 0, "tintVariation": 0.03, "blend": "alpha", "surfaces": surfaces, "image": image})


BRANDS = [("ChezMargot", "CHEZ MARGOT", "BRASSERIE", (236, 210, 140, 255), None),
          ("PetitZinc", "LE PETIT ZINC", "CAFÉ - TABAC", (240, 232, 210, 255), None),
          ("MaisonLefevre", "MAISON LEFÈVRE", "BOULANGERIE - PÂTISSERIE", GOLD, None),
          ("BonFromage", "AU BON FROMAGE", "FROMAGERIE - CRÈMERIE", (240, 232, 210, 255), None),
          ("HotelLilas", "HÔTEL DES LILAS", "CHAMBRES - SALON", GOLD, None),
          ("LibrairieMoulin", "LIBRAIRIE DU MOULIN", "LIVRES ANCIENS", (240, 232, 210, 255), None),
          ("PharmacieButte", "PHARMACIE DE LA BUTTE", "OUVERT 7J/7", (240, 250, 240, 255), None),
          ("EpicerieSaintAubin", "ÉPICERIE SAINT-AUBIN", "PRODUITS FINS", GOLD, None)]
for key, line, sub, fg, bg in BRANDS:
    text_image(os.path.join(PD, f"{key}.png"), line, 5.0, fg, bg=bg, sub=sub, pad=0.05)
    pdecal(f"Sign{key}.json", f"Paris Sign {key}", f"Shop-front lettering for an invented Paris Quarter shop: {line.encode('ascii', 'ignore').decode()}.",
           [3.4, 0.68], ["wall"], f"{key}.png")
for key, line, sub in (("RueDesLilas", "RUE DES LILAS", "18e Arr."), ("PlaceDeLaFontaine", "PLACE DE LA FONTAINE", "18e Arr."),
                       ("RueDuMoulin", "RUE DU MOULIN", "18e Arr."), ("PassageDesArtistes", "PASSAGE DES ARTISTES", "18e Arr.")):
    text_image(os.path.join(PD, f"Plaque{key}.png"), line, 3.0, (245, 245, 240, 255), bg=(28, 52, 120, 255), border=(220, 225, 230, 255), font=FONT_SANS, sub=sub,
               pad=0.1)
    pdecal(f"Plaque{key}.json", f"Paris Plaque {key}", f"A blue enamel street-name plaque with a white border: {line}.", [0.75, 0.25], ["wall"], f"Plaque{key}.png")

# road markings: a white bicycle symbol and a yellow LIVRAISON delivery bay word
im = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
W8 = (238, 238, 232, 235)
d.ellipse([10, 64, 50, 104], outline=W8, width=7)
d.ellipse([78, 64, 118, 104], outline=W8, width=7)
d.line([30, 84, 56, 50, 86, 50, 98, 84], fill=W8, width=7)
d.line([56, 50, 64, 84, 98, 84], fill=W8, width=6)
d.line([48, 40, 62, 40], fill=W8, width=7)
d.line([82, 38, 92, 50], fill=W8, width=6)
im = im.rotate(0)
im.save(os.path.join(D, "FrenchBicycle.png"), "PNG")
decal("FrenchBikeSymbol.json", "French Bike Lane Symbol", "A white bicycle symbol painted on a cycle lane (the wheels point east-west; turn it with the lane).",
      1.4, ["floor"], "FrenchBicycle.png")
text_image(os.path.join(D, "FrenchLivraison.png"), "LIVRAISON", 4.5, (236, 196, 40, 240), font=FONT_SANS)
decal("FrenchLivraison.json", "French Livraison Marking", "The yellow word LIVRAISON painted in a delivery bay (reads along x: turn it 90 for a bay along z).",
      [2.7, 0.6], ["floor"], "FrenchLivraison.png")
print("Parisian assets written to", REPO)
