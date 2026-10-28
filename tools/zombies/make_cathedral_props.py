#!/usr/bin/env python3
"""Writes Maps/Zombies/Props/Cathedral/*.json: the Zombies pack's gothic props for Ashgrave Cathedral (2026-10-27; the map
is written by the C# map kit, Source/MapKit/Generators/AshgraveCathedral.cs; docs/zombies/ZOMBIES-MODE.md, "Ashgrave
Cathedral").

Run from the repository root:  python3 -I tools/zombies/make_cathedral_props.py

Pack props (Maps/README.md "Where assets go"): only the Zombies pack's maps use them, so they need no Dev gallery. All
original designs built from boxes: pews, an altar, candelabra and votive stands, a chandelier, a lectern, a font, a
confessional, a pulpit, the organ's pipe wall and console, choir stalls, a saint, gargoyles, sarcophagi and coffins, the
ossuary's niche walls and skull heaps, cages and cell bars, a rack and a pillory, braziers and wall torches, stained
glass (lancets and a rose window), headstones and a mourning angel, the great bell in its headstock, a scriptorium
desk, a mason's bench, horse stalls, churchyard railings, a dead tree and a tapestry. Candles, coals, torches and
glass glow (flicker / pulse lights: the prop shader, no CPU cost). Things for a wall have their back at -Z (face +Z).
The flames also carry baked lights (a "lights" list, LIGHTS below; Props/README.md "Lights"): on a lit map (Ashgrave
Cathedral, docs/render/LIGHTING.md section 8) every torch, brazier, candle stand and chandelier placed lights the room.
Round shapes are polygon prisms (the helpers in make_slot_cabinets.py).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_slot_cabinets import b, bounds, ring_y, ring_x, ring_z, along  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Maps", "Zombies", "Props", "Cathedral")

STONE = "#8f8a80"
DSTONE = "#6a665e"
LSTONE = "#b0aa9c"
OAK = "#5b3a20"
DOAK = "#3b2614"
IRON = "#2c2c30"
RIRON = "#4a3a30"
GOLD = "#c89a2a"
CLOTH = "#e6dfcc"
RED = "#6e1418"
BONE = "#d8cfb4"
WAX = "#efe6cc"
BRONZE = "#9a6a2a"


def flame(x, y, z, s=0.05, rate=8, glow=1.6):
    return {"size": [s, round(s * 1.7, 3), s], "offset": [round(x, 3), round(y, 3), round(z, 3)], "color": "#ffb848",
            "anim": {"type": "flicker", "rate": rate, "duty": 0.86, "glow": glow, "color": "#ffd070", "off": "#a05a18"}}


def candle(x, y, z, h=0.18, w=0.05):
    return [b([w, h, w], [x, y + h / 2, z], WAX), flame(x, y + h + 0.04, z, w * 0.9)]


def glass(size, offset, color, glow=0.9, period=6.0):
    p = b(size, offset, color)
    p["anim"] = {"type": "pulse", "period": period, "glow": glow, "color": color, "off": color}
    return p


# ---- the props ------------------------------------------------------------------------------------------------

def pew():
    p = [b([3.0, 0.07, 0.46], [0, 0.45, 0.05], OAK, material="wood"),
         b([3.0, 0.55, 0.06], [0, 0.78, -0.2], OAK, material="wood"),
         b([3.0, 0.06, 0.1], [0, 1.08, -0.2], DOAK),
         b([3.0, 0.2, 0.05], [0, 0.6, 0.3], DOAK)]  # the kneeler rail of the next row
    for x in (-1.46, 0, 1.46):
        p.append(b([0.08, 0.98, 0.56], [x, 0.49, 0.0], DOAK, material="wood"))
    for x in (-1.46, 1.46):
        p.append(b([0.1, 0.18, 0.3], [x, 1.05, -0.12], DOAK))
    # One box over the whole pew: too tall to step onto, so nobody (and no zombie) walks along the seats.
    return p, [{"size": [3.0, 1.12, 0.6], "offset": [0, 0.56, 0.02]}]


def altar():
    p = [b([2.6, 0.95, 1.0], [0, 0.475, 0], LSTONE, material="concrete"),
         b([2.8, 0.1, 1.15], [0, 1.0, 0], STONE),
         b([2.82, 0.02, 1.17], [0, 1.06, 0], CLOTH),
         b([2.0, 0.7, 0.03], [0, 0.6, 0.52], RED),
         b([1.6, 0.06, 0.035], [0, 0.88, 0.53], GOLD),
         b([0.06, 0.5, 0.035], [0, 0.6, 0.535], GOLD),
         b([0.36, 0.06, 0.035], [0, 0.72, 0.535], GOLD),
         b([0.08, 0.7, 0.08], [0, 1.42, -0.3], GOLD),
         b([0.4, 0.08, 0.08], [0, 1.6, -0.3], GOLD),
         b([0.3, 0.05, 0.3], [0, 1.09, -0.3], GOLD)]
    for x in (-1.0, -0.6, 0.6, 1.0):
        p += [b([0.12, 0.05, 0.12], [x, 1.09, -0.25], GOLD), b([0.04, 0.28, 0.04], [x, 1.25, -0.25], GOLD)]
        p += candle(x, 1.39, -0.25, 0.22, 0.05)
    return p, None


def candelabrum():
    p = ring_y(0, 0, 0, 0.08, 0.22, IRON, 8) + [b([0.06, 1.55, 0.06], [0, 0.85, 0], IRON), b([0.9, 0.05, 0.05], [0, 1.55, 0], IRON),
                                                 b([0.05, 0.05, 0.5], [0, 1.45, 0], IRON)]
    for x, z, y in ((-0.43, 0, 1.58), (0.43, 0, 1.58), (0, -0.23, 1.48), (0, 0.23, 1.48), (0, 0, 1.62)):
        p += [b([0.1, 0.03, 0.1], [x, y, z], IRON)] + candle(x, y + 0.015, z, 0.24, 0.05)
    return p, [{"size": [0.3, 1.7, 0.3]}]


def votive_stand():
    p = [b([1.3, 0.05, 0.5], [0, 0.85, 0], IRON), b([1.3, 0.05, 0.35], [0, 1.0, -0.08], IRON), b([1.3, 0.05, 0.2], [0, 1.15, -0.16], IRON),
         b([0.05, 1.15, 0.05], [-0.62, 0.575, 0.2], IRON), b([0.05, 1.15, 0.05], [0.62, 0.575, 0.2], IRON),
         b([0.05, 1.2, 0.05], [-0.62, 0.6, -0.22], IRON), b([0.05, 1.2, 0.05], [0.62, 0.6, -0.22], IRON),
         b([0.3, 0.25, 0.2], [0.45, 0.6, 0.2], DOAK)]  # the offering box
    for row, (y, z) in enumerate(((0.875, 0.15), (1.025, -0.04), (1.175, -0.18))):
        n = 6 - row
        for i in range(n):
            x = -0.5 + i * (1.0 / max(1, n - 1))
            p.append(b([0.05, 0.06, 0.05], [x, y + 0.03, z], RED))
            p.append(flame(x, y + 0.09, z, 0.035, rate=10, glow=1.3))
    return p, [{"size": [1.3, 1.2, 0.5]}]


def chandelier():
    p = [b([0.04, 2.6, 0.04], [0, 1.3 + 0.9, 0], IRON)]
    for k in range(8):
        a = math.radians(k * 45)
        x, z = math.sin(a) * 0.95, math.cos(a) * 0.95
        p.append(b([0.76, 0.05, 0.05], [x * 0.5, 0.9, z * 0.5], IRON, [0, -k * 45 + 90, 0]))
        p.append(b([0.75, 0.06, 0.06], [x, 0.9, z], IRON, [0, -k * 45, 0]))
        p += candle(x, 0.93, z, 0.2, 0.05)
        p.append(b([0.04, 0.9, 0.04], [x * 0.5, 1.35, z * 0.5], IRON, [math.degrees(math.atan2(0.48, 0.9)) * 0, 0, 0]))
    p.append(b([0.2, 0.3, 0.2], [0, 0.85, 0], IRON))
    return p, None


def lectern():
    p = ring_y(0, 0, 0, 0.1, 0.25, BRONZE, 8) + ring_y(0, 0, 0.1, 1.05, 0.06, BRONZE, 8)
    p += [b([0.6, 0.06, 0.45], [0, 1.15, 0.02], BRONZE, [-20, 0, 0]),
          b([0.5, 0.04, 0.36], [0, 1.2, 0.04], CLOTH, [-20, 0, 0]),
          b([0.03, 0.05, 0.34], [0, 1.22, 0.04], RED, [-20, 0, 0]),
          b([0.18, 0.18, 0.22], [0, 1.0, -0.12], GOLD),
          b([0.5, 0.06, 0.14], [0, 1.02, -0.18], GOLD, [0, 0, 8])]
    return p, [{"size": [0.6, 1.2, 0.5]}]


def font():
    p = ring_y(0, 0, 0, 0.2, 0.55, DSTONE, 8) + ring_y(0, 0, 0.2, 0.75, 0.26, STONE, 8) + ring_y(0, 0, 0.75, 1.05, 0.6, LSTONE, 8)
    p += ring_y(0, 0, 1.0, 1.06, 0.5, "#2a4a5a", 8)  # the water
    p += [b([0.6, 0.05, 0.6], [0, 1.08, 0], DOAK, [0, 22.5, 0]), b([0.08, 0.5, 0.08], [0, 1.35, 0], DOAK), b([0.3, 0.06, 0.06], [0, 1.5, 0], DOAK)]
    return p, [{"size": [1.2, 1.1, 1.2]}]


def confessional():
    p = [b([2.6, 0.1, 1.3], [0, 0.05, 0], DOAK), b([2.7, 0.15, 1.35], [0, 2.45, 0], DOAK), b([2.8, 0.25, 1.4], [0, 2.65, 0], OAK)]
    for x in (-1.3, -0.45, 0.45, 1.3):
        p.append(b([0.08, 2.4, 1.3], [x, 1.25, 0], OAK, material="wood"))
    p.append(b([2.6, 2.4, 0.06], [0, 1.25, -0.62], OAK, material="wood"))
    p.append(b([0.86, 2.0, 0.06], [0, 1.15, 0.62], OAK, material="wood"))  # the priest's door
    p.append(b([0.3, 0.3, 0.05], [0, 1.6, 0.66], IRON))
    for x in (-0.87, 0.87):
        p.append(b([0.78, 1.7, 0.03], [x, 1.3, 0.6], RED))  # curtains
        p.append(b([0.78, 0.04, 0.04], [x, 2.18, 0.6], GOLD))
    for x in (-1.0, -0.33, 0.33, 1.0):
        p.append(b([0.1, 0.35, 0.1], [x, 2.95, 0.4], OAK))
    return p, [{"size": [2.8, 2.8, 1.4]}]


def pulpit():
    p = ring_y(0, 0, 0, 1.3, 0.25, STONE, 8) + ring_y(0, 0, 1.3, 2.4, 0.75, OAK, 8) + ring_y(0, 0, 2.4, 2.5, 0.8, DOAK, 8)
    p += [b([0.7, 0.4, 0.04], [0, 1.85, 0.76], RED), b([0.5, 0.06, 0.4], [0, 2.55, 0.5], DOAK, [-15, 0, 0])]
    return p, [{"size": [1.6, 2.5, 1.6]}]


def organ():
    p = [b([7.0, 1.6, 1.0], [0, 0.8, 0], DOAK, material="wood"), b([7.2, 0.2, 1.1], [0, 1.7, 0], OAK),
         b([7.0, 4.6, 0.3], [0, 4.1, -0.35], DOAK, material="wood"), b([7.4, 0.35, 0.6], [0, 6.55, -0.2], OAK)]
    for tower, (cx, n, h0) in enumerate(((-2.7, 5, 3.8), (-1.1, 6, 4.4), (0.0, 7, 4.8), (1.1, 6, 4.4), (2.7, 5, 3.8))):
        for i in range(n):
            x = cx + (i - (n - 1) / 2) * 0.17
            h = h0 - abs(i - (n - 1) / 2) * 0.35
            p.append(b([0.13, h, 0.13], [x, 1.8 + h / 2, 0.15 - tower % 2 * 0.1], "#c8c4b8", material="metal"))
            p.append(b([0.14, 0.05, 0.02], [x, 2.15, 0.22 - tower % 2 * 0.1], IRON))
    for x in (-3.45, 3.45):
        p.append(b([0.12, 6.6, 1.1], [x, 3.3, -0.05], OAK))
    return p, [{"size": [7.2, 1.8, 1.1]}, {"size": [7.2, 6.8, 0.4], "offset": [0, 3.4, -0.35]}]


def organ_console():
    p = [b([1.8, 0.8, 0.9], [0, 0.4, -0.1], DOAK, material="wood"), b([1.8, 0.9, 0.12], [0, 1.25, -0.5], OAK),
         b([1.4, 0.04, 0.22], [0, 0.82, 0.24], "#f0ecdc"), b([1.4, 0.04, 0.2], [0, 0.92, 0.14], "#f0ecdc"),
         b([1.4, 0.03, 0.08], [0, 0.85, 0.32], BONE), b([1.6, 0.12, 0.5], [0, 0.06, 0.55], DOAK),
         b([1.2, 0.08, 0.35], [0, 0.48, 0.55], OAK)]  # the bench
    for i in range(10):
        x = -0.75 + i * (1.5 / 9)
        p.append(b([0.07, 0.07, 0.03], [x, 1.35, -0.43], (RED if i % 3 == 0 else CLOTH)))
    p += candle(-0.8, 1.7, -0.5, 0.18, 0.05) + candle(0.8, 1.7, -0.5, 0.18, 0.05)
    return p, [{"size": [1.8, 1.7, 1.0], "offset": [0, 0.85, -0.1]}]


def choir_stalls():
    p = [b([4.0, 0.45, 0.6], [0, 0.225, 0.05], DOAK, material="wood"), b([4.0, 2.6, 0.12], [0, 1.3, -0.3], OAK, material="wood"),
         b([4.2, 0.18, 0.6], [0, 2.7, -0.06], DOAK), b([4.0, 0.9, 0.08], [0, 0.45, 0.6], OAK)]
    for i in range(6):
        x = -1.65 + i * 0.66
        p.append(b([0.06, 1.0, 0.55], [x, 0.5, 0.05], DOAK))
        p.append(b([0.36, 0.5, 0.03], [x + 0.33, 1.8, -0.23], RED))
        p.append(b([0.12, 0.35, 0.12], [x + 0.33, 2.95, -0.06], OAK))
    return p, [{"size": [4.0, 1.0, 1.0], "offset": [0, 0.5, 0.15]}, {"size": [4.2, 2.8, 0.2], "offset": [0, 1.4, -0.3]}]


def saint():
    p = [b([0.9, 0.7, 0.9], [0, 0.35, 0], DSTONE, material="concrete"), b([1.0, 0.1, 1.0], [0, 0.75, 0], STONE)]
    p += ring_y(0, 0, 0.8, 2.1, 0.3, LSTONE, 8)  # the robe
    p += [b([0.5, 0.35, 0.36], [0, 2.25, 0], LSTONE), b([0.22, 0.28, 0.24], [0, 2.56, 0.02], LSTONE),
          b([0.3, 0.06, 0.3], [0, 2.73, 0.02], GOLD), b([0.42, 0.42, 0.02], [0, 2.62, -0.12], GOLD),  # halo
          b([0.12, 0.5, 0.12], [-0.25, 2.0, 0.2], LSTONE, [-40, 0, 0]), b([0.12, 0.5, 0.12], [0.25, 2.0, 0.2], LSTONE, [-40, 0, 0]),
          b([0.14, 0.2, 0.06], [0, 2.2, 0.38], LSTONE)]
    return p, [{"size": [1.0, 2.8, 1.0]}]


def gargoyle():
    p = [b([0.5, 0.25, 0.7], [0, 0.125, 0], DSTONE), b([0.42, 0.45, 0.5], [0, 0.45, 0.05], DSTONE, [-15, 0, 0]),
         b([0.3, 0.28, 0.34], [0, 0.72, 0.35], DSTONE), b([0.2, 0.12, 0.2], [0, 0.66, 0.55], DSTONE),
         b([0.06, 0.18, 0.06], [-0.1, 0.92, 0.3], DSTONE, [-25, 0, 0]), b([0.06, 0.18, 0.06], [0.1, 0.92, 0.3], DSTONE, [-25, 0, 0]),
         b([0.5, 0.3, 0.06], [-0.32, 0.55, -0.1], DSTONE, [0, 30, 40]), b([0.5, 0.3, 0.06], [0.32, 0.55, -0.1], DSTONE, [0, -30, -40]),
         b([0.04, 0.04, 0.02], [-0.07, 0.78, 0.52], "#ff4020"), b([0.04, 0.04, 0.02], [0.07, 0.78, 0.52], "#ff4020")]
    return p, None


def sarcophagus():
    p = [b([2.3, 0.75, 1.0], [0, 0.375, 0], STONE, material="concrete"), b([2.45, 0.12, 1.12], [0, 0.81, 0], LSTONE),
         b([2.0, 0.08, 0.75], [0, 0.07, 0], DSTONE)]
    p += [b([1.5, 0.18, 0.42], [0.05, 0.96, 0], LSTONE), b([0.28, 0.2, 0.26], [-0.88, 0.97, 0], LSTONE),
          b([0.2, 0.22, 0.12], [-0.4, 1.12, 0], LSTONE), b([0.6, 0.08, 0.36], [0.6, 1.06, 0], LSTONE)]
    for x in (-0.7, 0, 0.7):
        p.append(b([0.4, 0.4, 0.02], [x, 0.4, 0.51], DSTONE))
    return p, [{"size": [2.45, 1.0, 1.12]}]


def coffin():
    p = [b([1.95, 0.42, 0.62], [0, 0.21, 0], DOAK, material="wood"), b([2.0, 0.06, 0.66], [0, 0.45, 0], OAK),
         b([0.04, 0.02, 0.4], [0.3, 0.49, 0], GOLD), b([0.16, 0.02, 0.04], [0.3, 0.49, 0], GOLD)]
    return p, [{"size": [2.0, 0.5, 0.66]}]


def ossuary_wall():
    p = [b([3.0, 2.6, 0.12], [0, 1.3, -0.24], DSTONE, material="concrete")]
    for row in range(4):
        y = 0.25 + row * 0.62
        p.append(b([3.0, 0.08, 0.5], [0, y, 0], STONE))
        for i in range(9):
            x = -1.33 + i * 0.333
            if (row + i) % 3 == 1:
                p.append(b([0.3, 0.08, 0.1], [x, y + 0.08, 0.05], BONE, [0, 20 * (i % 2 * 2 - 1), 0]))
                p.append(b([0.28, 0.07, 0.09], [x, y + 0.15, -0.02], BONE, [0, -15, 0]))
            else:
                p.append(b([0.2, 0.2, 0.22], [x, y + 0.14, 0.04], BONE))
                p.append(b([0.05, 0.05, 0.02], [x - 0.05, y + 0.17, 0.155], "#1a1610"))
                p.append(b([0.05, 0.05, 0.02], [x + 0.05, y + 0.17, 0.155], "#1a1610"))
    return p, [{"size": [3.0, 2.6, 0.55], "offset": [0, 1.3, -0.03]}]


def skull_heap():
    p = []
    k = 0
    for layer, (n, r, y) in enumerate(((7, 0.42, 0.1), (5, 0.28, 0.28), (1, 0.0, 0.45))):
        for i in range(n):
            a = (i / n + layer * 0.21) * math.tau
            x, z = math.sin(a) * r, math.cos(a) * r
            p.append(b([0.2, 0.19, 0.22], [x, y, z], BONE, [0, i * 37 % 90, 0]))
            k += 1
    for i in range(5):
        a = i * 1.3
        p.append(b([0.5, 0.06, 0.07], [math.sin(a) * 0.5, 0.04, math.cos(a) * 0.5], BONE, [0, i * 47, 0]))
    return p, None


def cage():
    p = [b([1.3, 0.08, 1.3], [0, 0.04, 0], RIRON), b([1.3, 0.08, 1.3], [0, 2.2, 0], RIRON), b([0.6, 0.06, 0.06], [0, 2.3, 0], RIRON)]
    for i in range(6):
        t = -0.6 + i * 0.24
        for (x, z) in ((t, -0.62), (t, 0.62), (-0.62, t), (0.62, t)):
            p.append(b([0.035, 2.12, 0.035], [x, 1.12, z], IRON))
    p.append(b([1.3, 0.05, 0.05], [0, 1.1, 0.62], RIRON))
    return p, [{"size": [1.3, 2.3, 1.3]}]


def cell_bars():
    p = [b([3.0, 0.12, 0.14], [0, 2.94, 0], RIRON), b([3.0, 0.1, 0.12], [0, 0.05, 0], RIRON), b([3.0, 0.06, 0.06], [0, 1.6, 0], RIRON)]
    for i in range(16):
        x = -1.41 + i * 0.188
        p.append(b([0.035, 2.9, 0.035], [x, 1.45, 0], IRON))
    p += [b([0.08, 0.2, 0.12], [0.55, 1.15, 0.05], RIRON), b([0.14, 0.14, 0.04], [0.55, 1.3, 0.08], "#6a5a30")]
    return p, [{"size": [3.0, 3.0, 0.14]}]


def rack():
    p = [b([2.2, 0.1, 0.12], [0, 0.85, -0.42], OAK), b([2.2, 0.1, 0.12], [0, 0.85, 0.42], OAK),
         b([1.9, 0.08, 0.7], [0, 0.82, 0], DOAK, material="wood")]
    for x in (-1.0, 1.0):
        for z in (-0.42, 0.42):
            p.append(b([0.12, 0.85, 0.12], [x, 0.425, z], OAK))
        p += ring_z(x, 0.98, -0.5, 0.5, 0.07, DOAK, 8)
        p.append(b([0.6, 0.06, 0.06], [x, 0.98, 0.56], RIRON, [0, 0, 55]))
    for z in (-0.18, 0.18):
        p.append(b([0.8, 0.025, 0.025], [-0.6, 0.94, z], "#b8a070"))
        p.append(b([0.8, 0.025, 0.025], [0.6, 0.94, z], "#b8a070"))
    return p, [{"size": [2.3, 1.1, 1.0]}]


def pillory():
    p = [b([0.16, 2.0, 0.16], [0, 1.0, 0], OAK), b([1.4, 0.18, 0.12], [0, 1.6, 0.04], OAK), b([1.4, 0.18, 0.12], [0, 1.4, 0.04], DOAK),
         b([0.9, 0.12, 0.9], [0, 0.06, 0], DSTONE)]
    for x in (-0.45, 0, 0.45):
        p.append(b([0.12 if x == 0 else 0.08, 0.06, 0.02], [x, 1.5, 0.11], "#0e0c0a"))
    return p, [{"size": [0.3, 2.0, 0.3]}, {"size": [1.4, 0.4, 0.2], "offset": [0, 1.5, 0.04]}]


def brazier():
    p = [b([0.06, 0.8, 0.06], [-0.22, 0.4, -0.13], IRON, [0, 0, 12]), b([0.06, 0.8, 0.06], [0.22, 0.4, -0.13], IRON, [0, 0, -12]),
         b([0.06, 0.8, 0.06], [0, 0.4, 0.25], IRON, [12, 0, 0])]
    p += ring_y(0, 0, 0.78, 0.98, 0.32, IRON, 8)
    coal = b([0.5, 0.08, 0.5], [0, 1.0, 0], "#ff6a20")
    coal["anim"] = {"type": "flicker", "rate": 4, "duty": 0.9, "glow": 1.8, "color": "#ff8a30", "off": "#8a2a08"}
    p.append(coal)
    for i in range(3):
        f = flame(math.sin(i * 2.1) * 0.12, 1.12, math.cos(i * 2.1) * 0.12, 0.12, rate=9, glow=2.0)
        p.append(f)
    return p, [{"size": [0.7, 1.0, 0.7]}]


def wall_torch():
    p = [b([0.12, 0.22, 0.04], [0, 0.0 + 0.11, 0.02], IRON), b([0.04, 0.04, 0.28], [0, 0.12, 0.16], IRON, [30, 0, 0]),
         b([0.06, 0.32, 0.06], [0, 0.32, 0.27], DOAK, [20, 0, 0]), b([0.09, 0.08, 0.09], [0, 0.48, 0.32], "#3a2412"),
         flame(0, 0.6, 0.34, 0.11, rate=9, glow=2.2)]
    return p, None


def lancet(colors):
    p = [b([1.5, 4.2, 0.08], [0, 2.1, -0.04], DSTONE)]
    rows = 7
    for r in range(rows):
        for c in range(3):
            col = colors[(r + c) % len(colors)]
            p.append(glass([0.4, 0.5, 0.04], [-0.43 + c * 0.43, 0.35 + r * 0.52, 0.02], col, 0.8, 7.0 + r * 0.3))
    for (x, y, w) in ((0, 3.95, 0.9), (0, 4.15, 0.45)):
        p.append(glass([w, 0.2, 0.04], [x, y, 0.02], colors[0], 1.0))
    for x in (-0.22, 0.22):
        p.append(b([0.05, 3.7, 0.06], [x, 1.95, 0.05], IRON))
    return p, None


def rose_window():
    cols = ["#3a5ac8", "#c83a3a", "#d8b03a", "#3a9a5a", "#7a3ab0"]
    p = ring_z(0, 2.6, -0.1, 0.0, 2.55, DSTONE, 16)
    p += ring_z(0, 2.6, 0.0, 0.05, 0.45, "#d8b03a", 8)
    for k in range(12):
        a = math.radians(k * 30)
        x, y = math.sin(a), math.cos(a)
        p.append(glass([0.5, 1.8, 0.04], [x * 1.4, 2.6 + y * 1.4, 0.03], cols[k % 5], 0.9, 8.0))
        p[-1]["rotation"] = [0, 0, round(-k * 30.0, 2)]
        p.append(b([0.06, 2.0, 0.07], [x * 1.4, 2.6 + y * 1.4, 0.06], IRON, [0, 0, -k * 30 + 15]))
    return p, None


def headstone():
    p = [b([0.62, 0.95, 0.14], [0, 0.475, 0], STONE, material="concrete"), b([0.44, 0.2, 0.14], [0, 1.02, 0], STONE),
         b([0.2, 0.14, 0.14], [0, 1.18, 0], STONE), b([0.75, 0.08, 0.3], [0, 0.04, 0], DSTONE),
         b([0.06, 0.32, 0.02], [0, 0.82, 0.075], DSTONE), b([0.22, 0.06, 0.02], [0, 0.88, 0.075], DSTONE)]
    return p, [{"size": [0.75, 1.25, 0.3]}]


def angel():
    p = [b([1.0, 1.0, 1.0], [0, 0.5, 0], DSTONE, material="concrete"), b([1.1, 0.12, 1.1], [0, 1.06, 0], STONE)]
    p += ring_y(0, 0, 1.12, 2.3, 0.26, LSTONE, 8)
    p += [b([0.4, 0.3, 0.3], [0, 2.4, 0.04], LSTONE), b([0.2, 0.24, 0.22], [0, 2.6, 0.14], LSTONE, [30, 0, 0]),
          b([0.1, 0.45, 0.1], [-0.12, 2.38, 0.24], LSTONE, [-60, 0, 10]), b([0.1, 0.45, 0.1], [0.12, 2.38, 0.24], LSTONE, [-60, 0, -10]),
          b([0.08, 1.6, 0.5], [-0.3, 2.3, -0.25], LSTONE, [20, 0, 18]), b([0.08, 1.6, 0.5], [0.3, 2.3, -0.25], LSTONE, [20, 0, -18])]
    return p, [{"size": [1.1, 3.0, 1.1]}]


def great_bell():
    sw = {"type": "swing", "axis": "x", "pivot": [0.0, 2.55, 0.0], "angle": 8, "period": 3.4, "jitter": 0.1}
    p = [b([0.25, 3.0, 0.25], [-1.25, 1.5, 0], DOAK, material="wood"), b([0.25, 3.0, 0.25], [1.25, 1.5, 0], DOAK, material="wood"),
         b([3.0, 0.2, 0.6], [0, 0.1, 0], DOAK), b([0.25, 0.25, 1.4], [-1.25, 0.12, 0], DOAK), b([0.25, 0.25, 1.4], [1.25, 0.12, 0], DOAK),
         b([2.75, 0.22, 0.3], [0, 2.9, 0], DOAK)]
    bell = [b([1.0, 0.36, 0.25], [0, 2.4, 0], OAK)]  # the headstock
    for y, a, h in ((2.05, 0.36, 0.4), (1.7, 0.5, 0.36), (1.38, 0.64, 0.32), (1.12, 0.78, 0.22)):
        bell += ring_y(0, 0, y - h / 2, y + h / 2, a, BRONZE, 8)
    bell += ring_y(0, 0, 0.98, 1.04, 0.82, "#b88a3a", 8)
    bell.append(b([0.1, 0.8, 0.1], [0, 1.45, 0], IRON))
    bell.append(b([0.22, 0.22, 0.22], [0, 1.05, 0], IRON))
    for q in bell:
        q["anim"] = dict(sw)
    p += bell
    p.append(b([0.04, 2.2, 0.04], [0.7, 1.2, 0.35], "#c8b07a"))  # the rope
    return p, [{"size": [0.3, 3.0, 0.3], "offset": [-1.25, 1.5, 0]}, {"size": [0.3, 3.0, 0.3], "offset": [1.25, 1.5, 0]},
               {"size": [1.7, 1.2, 1.7], "offset": [0, 1.6, 0]}]


def scriptorium_desk():
    p = [b([1.3, 0.06, 0.7], [0, 1.0, 0], OAK, [-18, 0, 0]), b([1.3, 0.8, 0.06], [0, 0.4, -0.3], DOAK), b([1.3, 0.06, 0.25], [0, 1.14, -0.3], OAK)]
    for x in (-0.6, 0.6):
        p.append(b([0.07, 0.95, 0.65], [x, 0.48, 0], DOAK))
    p += [b([0.7, 0.04, 0.48], [0, 1.06, 0.05], CLOTH, [-18, 0, 0]), b([0.02, 0.05, 0.46], [0, 1.08, 0.05], DOAK, [-18, 0, 0]),
          b([0.06, 0.08, 0.06], [0.5, 1.2, -0.3], IRON), b([0.01, 0.16, 0.01], [0.5, 1.3, -0.28], "#e8e0d0")]
    p += candle(-0.5, 1.17, -0.3, 0.14, 0.05)
    p.append(b([0.5, 0.45, 0.5], [0, 0.225, 0.55], DOAK))  # the stool
    return p, [{"size": [1.3, 1.2, 0.8]}]


def mason_bench():
    p = [b([2.0, 0.1, 0.8], [0, 0.85, 0], OAK, material="wood"), b([0.7, 0.45, 0.5], [-0.4, 1.12, 0], LSTONE, material="concrete"),
         b([0.4, 0.3, 0.4], [0.55, 1.05, 0.1], STONE), b([0.25, 0.08, 0.1], [0.3, 0.95, -0.25], OAK), b([0.1, 0.12, 0.14], [0.45, 0.99, -0.25], DOAK)]
    for x in (-0.9, 0.9):
        for z in (-0.32, 0.32):
            p.append(b([0.1, 0.8, 0.1], [x, 0.4, z], DOAK))
    for i in range(3):
        p.append(b([0.3, 0.025, 0.025], [0.1 + i * 0.12, 0.92, 0.25], "#9aa0a8", [0, 25, 0]))
    return p, [{"size": [2.0, 1.35, 0.8]}]


def horse_stall():
    p = [b([3.0, 1.4, 0.08], [0, 0.7, 0], OAK, material="wood"), b([3.0, 0.1, 0.14], [0, 1.45, 0], DOAK),
         b([0.14, 2.2, 0.14], [1.45, 1.1, 0], DOAK), b([0.14, 2.2, 0.14], [-1.45, 1.1, 0], DOAK)]
    for i in range(6):
        p.append(b([0.03, 0.7, 0.03], [-1.2 + i * 0.48, 1.85, 0], IRON))
    p.append(b([3.0, 0.06, 0.06], [0, 2.2, 0], IRON))
    return p, [{"size": [3.0, 2.2, 0.15]}]


def railings():
    p = [b([3.0, 0.06, 0.05], [0, 0.25, 0], IRON), b([3.0, 0.06, 0.05], [0, 1.15, 0], IRON)]
    for i in range(13):
        x = -1.44 + i * 0.24
        p.append(b([0.03, 1.3, 0.03], [x, 0.65, 0], IRON))
        p.append(b([0.07, 0.1, 0.07], [x, 1.33, 0], IRON, [0, 45, 0]))
    for x in (-1.5, 1.5):
        p.append(b([0.22, 1.6, 0.22], [x, 0.8, 0], DSTONE))
    return p, [{"size": [3.2, 1.4, 0.12]}]


def dead_tree():
    p = ring_y(0, 0, 0, 2.6, 0.22, "#3a3028", 8)
    for (a, tilt, l, y) in ((20, 35, 2.0, 2.2), (140, 40, 1.8, 2.5), (250, 30, 2.2, 2.0), (310, 45, 1.4, 3.0), (80, 25, 1.6, 2.8)):
        r = math.radians(a)
        dx, dz = math.sin(r), math.cos(r)
        t = math.radians(tilt)
        top = [dx * math.sin(t) * l, y + math.cos(t) * l, dz * math.sin(t) * l]
        p += along([0, y, 0], top, 0.07, "#3a3028", 6)
        tip = [top[0] + dx * 0.6, top[1] + 0.3, top[2] + dz * 0.6]
        p += along(top, tip, 0.035, "#3a3028", 4)
    p += ring_y(0, 0, 2.5, 3.4, 0.14, "#3a3028", 6)
    return p, [{"size": [0.5, 3.0, 0.5]}]


def tapestry():
    p = [b([0.06, 0.06, 2.0], [0, 3.0, 0.08], DOAK, [0, 90, 0]), b([1.8, 2.8, 0.03], [0, 1.6, 0.06], RED),
         b([1.4, 2.3, 0.032], [0, 1.65, 0.07], "#2a1c4a"), b([0.12, 1.4, 0.035], [0, 1.75, 0.075], GOLD),
         b([0.8, 0.12, 0.035], [0, 2.05, 0.075], GOLD), b([1.8, 0.1, 0.035], [0, 0.25, 0.075], GOLD)]
    for i in range(7):
        p.append(b([0.08, 0.18, 0.03], [-0.75 + i * 0.25, 0.1, 0.06], GOLD))
    return p, None


def gibbet():
    p = [b([0.04, 1.6, 0.04], [0, 2.6, 0], IRON), b([0.8, 0.06, 0.8], [0, 1.8, 0], RIRON), b([0.8, 0.06, 0.8], [0, 0.0 + 0.03, 0], RIRON)]
    for i in range(4):
        t = -0.36 + i * 0.24
        for (x, z) in ((t, -0.38), (t, 0.38), (-0.38, t), (0.38, t)):
            p.append(b([0.025, 1.78, 0.025], [x, 0.92, z], IRON))
    p.append(b([0.2, 0.18, 0.2], [0.1, 0.15, 0.05], BONE))
    return p, None


PROPS = [
    ("Cathedral Pew", "A 3 m oak pew with a high back and a kneeler rail behind; long along X, sitting faces +Z.", pew),
    ("Gothic High Altar", "A stone altar under a white cloth and a red frontal, a gold cross and four lit candles.", altar),
    ("Iron Candelabrum", "A tall iron candle stand with five lit candles.", candelabrum),
    ("Votive Candle Stand", "A three-tiered iron rack of red votive candles, lit, with an offering box.", votive_stand),
    ("Iron Chandelier", "A ring of eight lit candles on a chain (it hangs: place it with y, the ring is 0.9 m up).", chandelier),
    ("Brass Lectern", "A brass reading stand with an open book.", lectern),
    ("Baptismal Font", "An octagonal stone font with a wooden lid.", font),
    ("Confessional Booth", "A carved oak confessional: the priest's door in the middle, a curtained bay either side.", confessional),
    ("Stone Pulpit", "A raised oak pulpit on a stone stem.", pulpit),
    ("Organ Pipe Wall", "The organ: a case 7 m wide with five towers of pipes (back at -Z, the keyboard side +Z).", organ),
    ("Organ Console", "The organ's keyboards, stops and bench, two candles on top.", organ_console),
    ("Choir Stalls", "A 4 m row of six carved choir seats under a tall back.", choir_stalls),
    ("Saint Statue", "A robed stone saint with a gold halo on a plinth.", saint),
    ("Stone Gargoyle", "A crouching winged gargoyle with red eyes (looks only: perch it on a parapet).", gargoyle),
    ("Stone Sarcophagus", "A stone coffin with a carved figure lying on its lid.", sarcophagus),
    ("Plain Coffin", "A dark wooden coffin with a brass cross.", coffin),
    ("Ossuary Niche Wall", "A wall of stone shelves stacked with skulls and bones (back at -Z).", ossuary_wall),
    ("Skull Heap", "A heap of skulls and long bones (looks only).", skull_heap),
    ("Iron Cage", "A rusty iron cage on the floor.", cage),
    ("Cell Bars", "A 3 m run of dungeon bars with a lock (solid: a cell front).", cell_bars),
    ("Stretching Rack", "An old wooden rack with rollers and ropes (no one on it).", rack),
    ("Pillory", "Wooden stocks on a post.", pillory),
    ("Iron Brazier", "An iron tripod brazier of glowing coals.", brazier),
    ("Wall Torch", "A burning torch in an iron wall bracket (back at -Z; looks only).", wall_torch),
    ("Lancet Window Blue", "A tall pointed window of blue and gold stained glass, softly glowing (back at -Z; looks only).", lambda: lancet(["#3050b8", "#c8a030", "#2a3a90", "#b83030"])),
    ("Lancet Window Red", "A tall pointed window of red and green stained glass, softly glowing (back at -Z; looks only).", lambda: lancet(["#b02828", "#2a8a4a", "#d0a030", "#6a2a9a"])),
    ("Rose Window", "A round stained-glass rose window 5 m across (back at -Z; looks only).", rose_window),
    ("Gothic Headstone", "A pointed stone headstone with a carved cross.", headstone),
    ("Mourning Angel Statue", "A stone angel with folded wings, head bowed, on a plinth.", angel),
    ("Great Bell", "The cathedral's great bronze bell in its oak headstock, rocking a little, its rope hanging.", great_bell),
    ("Scriptorium Desk", "A sloped writing desk with an open book, an inkwell, a candle and a stool.", scriptorium_desk),
    ("Mason's Bench", "A stonemason's bench with a block half carved, a mallet and chisels.", mason_bench),
    ("Horse Stall", "A 3 m stable partition with iron bars along its top.", horse_stall),
    ("Churchyard Railings", "A 3 m run of iron railings between stone posts.", railings),
    ("Gnarled Dead Tree", "A bare, twisted dead tree.", dead_tree),
    ("Hanging Tapestry", "A red and violet tapestry with a gold cross on a rod (back at -Z; looks only).", tapestry),
    ("Gibbet Cage", "An iron cage hanging on a chain with a skull inside (place it with y; looks only).", gibbet),
]

# Baked lights (prop frame; flames flicker). Warm candlelight for the candles, a hotter orange for torches and coals.
CANDLE = "#ffc878"
LIGHTS = {
    "Wall Torch": [{"pos": [0, 0.64, 0.46], "color": "#ffa850", "intensity": 1.5, "range": 8, "flicker": True}],
    "Iron Brazier": [{"pos": [0, 1.3, 0], "color": "#ff8a3a", "intensity": 2.4, "range": 10, "flicker": True}],
    "Iron Candelabrum": [{"pos": [0, 1.95, 0], "color": CANDLE, "intensity": 1.1, "range": 7, "flicker": True}],
    "Iron Chandelier": [{"pos": [0, 1.1, 0], "color": CANDLE, "intensity": 2.2, "range": 16, "flicker": True}],
    "Votive Candle Stand": [{"pos": [0, 1.4, 0.15], "color": "#ffa860", "intensity": 0.9, "range": 6, "flicker": True}],
    "Gothic High Altar": [{"pos": [0, 1.85, -0.25], "color": CANDLE, "intensity": 0.9, "range": 6, "flicker": True}],
    "Organ Console": [{"pos": [0, 2.0, -0.5], "color": CANDLE, "intensity": 0.6, "range": 5, "flicker": True}],
    "Scriptorium Desk": [{"pos": [-0.5, 1.45, -0.3], "color": CANDLE, "intensity": 0.4, "range": 4, "flicker": True}],
}

COSMETIC = {"Iron Chandelier", "Stone Gargoyle", "Skull Heap", "Wall Torch", "Lancet Window Blue", "Lancet Window Red", "Rose Window",
            "Hanging Tapestry", "Gibbet Cage"}


def main():
    os.makedirs(OUT, exist_ok=True)
    names = set()
    for name, desc, build in PROPS:
        parts, collider = build()
        assert len(parts) <= 128, f"{name}: {len(parts)} parts"
        assert name not in names
        names.add(name)
        lo, hi = bounds(parts)
        assert all(hi[i] - lo[i] <= 24 for i in range(3)), name
        prop = {"format": "call-of-slop-prop/1", "name": name, "category": "Cathedral", "description": desc,
                "collision": "cosmetic" if name in COSMETIC else "solid"}
        if collider and name not in COSMETIC:
            prop["collider"] = collider
        if name in COSMETIC:
            prop["fadeDistance"] = 90
        prop["scatter"] = {"randomYaw": False, "yawStep": 90}
        fn = "".join(ch for ch in name if ch.isalnum()) + ".json"
        parts_json = ",\n".join("    " + json.dumps(q) for q in parts)
        head = "{\n" + ",\n".join(f"  {json.dumps(k)}: {json.dumps(v)}" for k, v in prop.items())
        tail = "\n  ]"
        if name in LIGHTS:
            tail += ',\n  "lights": [\n' + ",\n".join("    " + json.dumps(q) for q in LIGHTS[name]) + "\n  ]"
        with open(os.path.join(OUT, fn), "w") as f:
            f.write(head + ',\n  "parts": [\n' + parts_json + tail + "\n}\n")
    assert set(LIGHTS) <= names, set(LIGHTS) - names
    print(f"wrote {len(PROPS)} props to {OUT}")


if __name__ == "__main__":
    main()
