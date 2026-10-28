#!/usr/bin/env python3
"""Writes Props/Gothic/*.json and two lit fittings in Props/Lighting: reusable gothic architectural detail
(developer-approved board item dev-cathedral-detail-cd01, 2026-10-28; first used by Ashgrave Cathedral,
Source/MapKit/Generators/AshgraveCathedral.Detail.cs).

Run from the repository root:  python3 -I tools/props/gothic_props.py   (then python3 -I tools/props/gallery.py)

All original designs built from boxes, all cosmetic (looks only: they never change a map's nav or its baked light's
shadows) except the wall tomb, whose chest is solid. Things for a wall have their back at -Z on the wall face (place
them on the face, facing into the room). Stacking pieces for the bay rhythm: a Gothic Pilaster (4 m) on the floor,
Gothic Wall Shafts (2 m) on it, a Gothic Vault Springer (1.2 m) on top where the vault's ribs start; the Gothic
Respond and Gothic Tall Respond prefabs are the usual stacks (5.2 m and 9.2 m). Niches come empty or as prefabs with a
saint, candles (lit) or a reliquary on the shelf. The Corona Chandelier and the Candle Cluster carry baked lights
(Props/README.md "Lights").
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "zombies"))
from make_slot_cabinets import b, bounds, ring_y  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Props", "Gothic")
OUT_LIGHT = os.path.join(ROOT, "Props", "Lighting")

STONE = "#9a9385"     # dressed stone, a shade lighter than the walls so the mouldings read
STONE2 = "#857f72"
DSTONE = "#55514a"
SHADOW = "#2e2c29"    # the inside of a niche or an arch
MARBLE = "#1f1e1d"
OAK = "#5b3a20"
DOAK = "#3b2614"
IRON = "#2c2c30"
GOLD = "#c89a2a"
BRASS = "#a8823a"
WAX = "#efe6cc"
ROBE = "#b0aa9c"


def flame(x, y, z, s=0.04, rate=8, glow=1.6):
    return {"size": [s, round(s * 1.7, 3), s], "offset": [round(x, 3), round(y, 3), round(z, 3)], "color": "#ffb848",
            "anim": {"type": "flicker", "rate": rate, "duty": 0.86, "glow": glow, "color": "#ffd070", "off": "#a05a18"}}


def candle(x, y, z, h=0.16, w=0.045):
    return [b([w, h, w], [x, y + h / 2, z], WAX), flame(x, y + h + 0.035, z, w * 0.85)]


def arch(cx, ys, w, h, thick, depth, z, color, n=4):
    """A pointed arch in the wall plane (x, y) at depth z: springing at (cx -+ w/2, ys), apex h higher at cx; n straight
    pieces a side, each a box thick (across the arch) by depth (out of the wall)."""
    half = w / 2
    # A low arch is a taller pointed one squashed (so it stays pointed rather than dipping at the apex).
    hp = max(h, 1.3 * half)
    squash = h / hp
    h = hp
    r = (half * half + h * h) / w if w > 0 else h
    parts = []
    for side in (-1, 1):
        # The arc's centre is on the springing line, r in from this side's springing point.
        ccx = cx + side * (half - r)
        a0 = 0.0
        a1 = math.atan2(h, r - half)
        pts = []
        for i in range(n + 1):
            a = a0 + (a1 - a0) * i / n
            pts.append((ccx + side * r * math.cos(a), ys + r * math.sin(a) * squash))
        for i in range(n):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            L = math.hypot(x1 - x0, y1 - y0) + thick * 0.5
            ang = math.degrees(math.atan2(y1 - y0, x1 - x0))
            parts.append(b([L, thick, depth], [(x0 + x1) / 2, (y0 + y1) / 2, z], color, [0, 0, ang]))
    return parts


# ---- stacking pieces -----------------------------------------------------------------------------------------------

def pilaster():
    """An engaged pier: base, a shaft with rolls, a moulded capital; 4 m."""
    p = [b([0.8, 0.45, 0.38], [0, 0.225, 0.17], STONE2, material="concrete"),
         b([0.68, 0.12, 0.32], [0, 0.51, 0.14], STONE),
         b([0.46, 3.0, 0.2], [0, 2.07, 0.08], STONE, material="concrete"),
         b([0.2, 3.0, 0.2], [0, 2.07, 0.2], STONE, [0, 45, 0]),
         b([0.1, 3.0, 0.1], [-0.25, 2.07, 0.12], STONE2),
         b([0.1, 3.0, 0.1], [0.25, 2.07, 0.12], STONE2),
         b([0.5, 0.08, 0.32], [0, 3.61, 0.14], STONE2),
         b([0.6, 0.24, 0.36], [0, 3.77, 0.16], STONE),
         b([0.74, 0.12, 0.42], [0, 3.95, 0.19], STONE2)]
    return p, None


def wall_shaft():
    """A plain shaft with a ring halfway, to stack on a pilaster: 2 m."""
    p = [b([0.46, 2.0, 0.2], [0, 1.0, 0.08], STONE, material="concrete"),
         b([0.2, 2.0, 0.2], [0, 1.0, 0.2], STONE, [0, 45, 0]),
         b([0.1, 2.0, 0.1], [-0.25, 1.0, 0.12], STONE2),
         b([0.1, 2.0, 0.1], [0.25, 1.0, 0.12], STONE2),
         b([0.56, 0.1, 0.36], [0, 1.0, 0.16], STONE2)]
    return p, None


def springer():
    """A capital with three rib stubs (one out from the wall, two diagonal) where a vault starts: 1.2 m."""
    p = [b([0.5, 0.1, 0.32], [0, 0.05, 0.14], STONE2),
         b([0.62, 0.3, 0.4], [0, 0.25, 0.18], STONE),
         b([0.76, 0.12, 0.46], [0, 0.46, 0.21], STONE2)]
    for yaw in (-45, 0, 45):
        a = math.radians(yaw)
        t = math.radians(40)
        L = 0.85
        cy = 0.52 + L / 2 * math.cos(t)
        out = 0.18 + L / 2 * math.sin(t)
        p.append(b([0.2, L, 0.24], [math.sin(a) * out, cy, math.cos(a) * out], STONE, [40, yaw, 0]))
    return p, None


# ---- wall panels ---------------------------------------------------------------------------------------------------

def blind_arcade():
    """Three pointed blind arches on colonnettes over a bench-like sill: 3 m wide, 2.8 m tall."""
    p = [b([3.0, 2.0, 0.04], [0, 1.15, 0.02], SHADOW),
         b([3.1, 0.18, 0.2], [0, 0.09, 0.1], STONE2, material="concrete"),
         b([3.1, 0.1, 0.18], [0, 2.75, 0.09], STONE)]
    for x in (-1.45, -0.5, 0.5, 1.45):
        p.append(b([0.1, 1.7, 0.1], [x, 1.03, 0.1], STONE))
        p.append(b([0.18, 0.1, 0.16], [x, 1.93, 0.1], STONE2))
    for cx in (-0.975, 0.0, 0.975):
        p += arch(cx, 1.98, 0.9, 0.55, 0.09, 0.12, 0.08, STONE, 3)
    # The spandrels between the arches: plain stone.
    for x in (-0.49, 0.49):
        p.append(b([0.16, 0.5, 0.06], [x, 2.45, 0.05], STONE2))
    return p, None


def niche():
    """A recessed-looking niche (a dark back between jambs) with a shelf, a pointed hood and a finial: 1.1 x 3.2 m."""
    p = [b([0.8, 1.95, 0.04], [0, 1.47, 0.02], SHADOW),
         b([0.5, 0.42, 0.04], [0, 2.62, 0.02], SHADOW),
         b([1.0, 0.1, 0.4], [0, 0.5, 0.2], STONE2),
         b([0.6, 0.3, 0.3], [0, 0.3, 0.15], STONE),
         b([0.3, 0.16, 0.2], [0, 0.07, 0.1], STONE2)]
    for x in (-0.48, 0.48):
        p.append(b([0.14, 1.95, 0.3], [x, 1.47, 0.15], STONE, material="concrete"))
        p.append(b([0.07, 1.8, 0.07], [x, 1.5, 0.32], STONE2))
    p += arch(0, 2.44, 1.1, 0.62, 0.12, 0.32, 0.16, STONE, 3)
    p.append(b([0.12, 0.36, 0.12], [0, 3.22, 0.18], STONE2))
    p.append(b([0.2, 0.06, 0.2], [0, 3.04, 0.18], STONE))
    return p, None


def saint_figure():
    """A small robed saint for a niche shelf: 1.4 m."""
    p = [b([0.36, 0.08, 0.3], [0, 0.04, 0], STONE2)]
    for yaw in (0, 45, 90, 135):
        p.append(b([0.32, 0.9, 0.14], [0, 0.53, 0], ROBE, [0, yaw, 0]))
    p += [b([0.3, 0.24, 0.22], [0, 1.08, 0], ROBE),
          b([0.15, 0.19, 0.16], [0, 1.29, 0.01], ROBE),
          b([0.26, 0.26, 0.015], [0, 1.31, -0.09], GOLD),
          b([0.08, 0.32, 0.08], [-0.12, 0.92, 0.12], ROBE, [-40, 0, 0]),
          b([0.08, 0.32, 0.08], [0.12, 0.92, 0.12], ROBE, [-40, 0, 0]),
          b([0.1, 0.14, 0.05], [0, 1.0, 0.22], ROBE)]
    return p, None


def reliquary():
    """A gilded house-shaped casket on little feet, set with stones: 0.55 m long."""
    p = [b([0.5, 0.26, 0.26], [0, 0.17, 0], GOLD),
         b([0.54, 0.03, 0.3], [0, 0.04, 0], BRASS),
         b([0.56, 0.05, 0.19], [0, 0.34, -0.065], BRASS, [35, 0, 0]),
         b([0.56, 0.05, 0.19], [0, 0.34, 0.065], BRASS, [-35, 0, 0]),
         b([0.08, 0.1, 0.08], [0, 0.45, 0], GOLD)]
    for x, c in ((-0.15, "#8a1a1a"), (0.0, "#1a3a8a"), (0.15, "#1a6a3a")):
        p.append(b([0.06, 0.06, 0.02], [x, 0.18, 0.135], c))
    for x in (-0.21, 0.21):
        for z in (-0.1, 0.1):
            p.append(b([0.04, 0.04, 0.04], [x, 0.02, z], BRASS))
    return p, None


def window_surround():
    """A moulded frame and pointed hood for a tall lancet (a 1.5 x 4.25 m window at its centre): 2 x 5 m."""
    p = [b([1.9, 0.14, 0.26], [0, -0.07, 0.11], STONE2)]
    for x in (-0.86, 0.86):
        p.append(b([0.2, 3.55, 0.16], [x, 1.78, 0.08], STONE))
        p.append(b([0.07, 3.4, 0.07], [x * 0.94, 1.8, 0.19], STONE2))
        p.append(b([0.26, 0.12, 0.22], [x, 3.6, 0.11], STONE2))
    p += arch(0, 3.62, 1.92, 1.25, 0.16, 0.2, 0.1, STONE, 4)
    p.append(b([0.6, 0.65, 0.03], [0, 4.4, 0.0], SHADOW))
    for x in (-1.08, 1.08):
        p.append(b([0.18, 0.2, 0.2], [x, 3.55, 0.1], STONE2))
    return p, None


def window_frame():
    """Jambs, a sill and a traceried head round a low window (an opening 1.6 m wide, 0.9-2.3 m up, left clear): 2.1 x 3.5 m."""
    p = [b([2.0, 0.12, 0.24], [0, 0.82, 0.12], STONE2)]
    for x in (-0.92, 0.92):
        p.append(b([0.22, 1.62, 0.16], [x, 1.61, 0.08], STONE))
        p.append(b([0.07, 1.5, 0.07], [x * 0.93, 1.61, 0.19], STONE2))
    p.append(b([1.84, 0.12, 0.18], [0, 2.38, 0.09], STONE))
    p.append(b([1.6, 0.8, 0.03], [0, 2.84, 0.015], SHADOW))
    p.append(b([0.08, 0.8, 0.08], [0, 2.84, 0.06], STONE2))
    for x in (-0.4, 0.4):
        p += arch(x, 2.5, 0.68, 0.42, 0.06, 0.07, 0.06, STONE2, 2)
    for a in (0, 45):
        p.append(b([0.22, 0.22, 0.06], [0, 3.12, 0.05], STONE2, [0, 0, a]))
    p += arch(0, 2.44, 2.06, 1.05, 0.14, 0.2, 0.1, STONE, 4)
    return p, None


def wall_tomb():
    """A canopied wall tomb: a chest with a recumbent effigy under a pointed arch (the chest is solid): 2.6 x 3.9 m."""
    p = [b([2.2, 0.8, 0.8], [0, 0.4, 0.42], STONE2, material="concrete"),
         b([2.3, 0.1, 0.9], [0, 0.85, 0.45], STONE),
         b([2.24, 0.12, 0.84], [0, 0.06, 0.42], DSTONE)]
    for x in (-0.75, -0.25, 0.25, 0.75):
        p.append(b([0.36, 0.48, 0.02], [x, 0.43, 0.83], SHADOW))
        p += arch(x, 0.55, 0.36, 0.14, 0.04, 0.03, 0.84, STONE, 1)
    # The effigy: a figure lying along the chest, head to the left on a cushion, hands together.
    p += [b([0.3, 0.1, 0.36], [-0.82, 0.95, 0.45], "#6e1418"),
          b([1.3, 0.2, 0.42], [-0.05, 1.0, 0.45], ROBE),
          b([0.22, 0.2, 0.24], [-0.82, 1.08, 0.45], ROBE),
          b([0.1, 0.18, 0.1], [-0.2, 1.17, 0.45], ROBE),
          b([0.3, 0.16, 0.26], [0.72, 1.0, 0.45], ROBE),
          b([0.26, 0.04, 0.12], [0.2, 1.11, 0.45], GOLD)]
    # The canopy on the wall behind.
    p.append(b([2.3, 2.1, 0.04], [0, 1.95, 0.02], SHADOW))
    for x in (-1.24, 1.24):
        p.append(b([0.18, 2.75, 0.24], [x, 1.37, 0.12], STONE))
        p.append(b([0.14, 0.5, 0.14], [x, 3.0, 0.12], STONE2))
    p += arch(0, 2.75, 2.48, 0.95, 0.16, 0.28, 0.14, STONE, 4)
    p.append(b([0.14, 0.4, 0.14], [0, 3.88, 0.14], STONE2))
    return p, [{"size": [2.3, 1.1, 0.9], "offset": [0, 0.55, 0.45]}]


def plaque():
    """A dark memorial tablet in a stone frame with a pediment and an urn (hang it with y): 0.9 x 1.4 m."""
    p = [b([0.84, 1.0, 0.06], [0, 0.6, 0.03], STONE),
         b([0.7, 0.86, 0.03], [0, 0.6, 0.075], MARBLE),
         b([0.94, 0.08, 0.1], [0, 0.06, 0.05], STONE2),
         b([0.94, 0.08, 0.1], [0, 1.14, 0.05], STONE2),
         b([0.56, 0.06, 0.06], [-0.24, 1.26, 0.04], STONE2, [0, 0, 22]),
         b([0.56, 0.06, 0.06], [0.24, 1.26, 0.04], STONE2, [0, 0, -22]),
         b([0.1, 0.12, 0.08], [0, 1.4, 0.05], STONE2),
         b([0.14, 0.12, 0.06], [0, 0.0, 0.04], STONE2)]
    for i in range(5):
        w = 0.5 if i % 2 == 0 else 0.38
        p.append(b([w, 0.035, 0.005], [0, 0.95 - i * 0.12, 0.093], GOLD))
    return p, None


def banner(cloth, trim, emblem):
    """A long swallow-tailed banner on an iron rod from a wall bracket (hang it with y: the rod is 3.2 m up): 1.1 x 3.3 m."""
    p = [b([1.24, 0.05, 0.05], [0, 3.2, 0.3], IRON),
         b([0.05, 0.05, 0.32], [-0.5, 3.2, 0.16], IRON),
         b([0.05, 0.05, 0.32], [0.5, 3.2, 0.16], IRON),
         b([0.08, 0.08, 0.08], [-0.64, 3.2, 0.3], GOLD),
         b([0.08, 0.08, 0.08], [0.64, 3.2, 0.3], GOLD),
         b([1.0, 2.3, 0.02], [0, 2.03, 0.3], cloth),
         b([1.0, 0.06, 0.03], [0, 3.13, 0.3], trim),
         b([0.05, 2.3, 0.025], [-0.48, 2.03, 0.3], trim),
         b([0.05, 2.3, 0.025], [0.48, 2.03, 0.3], trim)]
    # The swallow tail: two tongues hanging below the field.
    for side in (-1, 1):
        p.append(b([0.5, 0.75, 0.02], [side * 0.26, 0.6, 0.3], cloth, [0, 0, side * 8]))
    # The device: a chevron and a rosette.
    p.append(b([0.55, 0.1, 0.025], [-0.17, 2.0, 0.305], emblem, [0, 0, 35]))
    p.append(b([0.55, 0.1, 0.025], [0.17, 2.0, 0.305], emblem, [0, 0, -35]))
    for a in (0, 45):
        p.append(b([0.26, 0.26, 0.025], [0, 2.6, 0.305], emblem, [0, 0, a]))
    p.append(b([0.1, 0.1, 0.03], [0, 2.6, 0.31], cloth))
    return p, None


def boss():
    """A carved rosette where vault ribs meet (hang it with y: its top is 0.3 m up)."""
    p = [b([0.42, 0.26, 0.42], [0, 0.17, 0], STONE)]
    for a in (0, 45, 90, 135):
        p.append(b([0.66, 0.1, 0.16], [0, 0.08, 0], STONE2, [0, a, 0]))
    p.append(b([0.16, 0.08, 0.16], [0, -0.02, 0], GOLD))
    return p, None


def ledger():
    """A worn grave slab let into the floor, with a brass rosette and lines of lettering: 1 x 2 m."""
    p = [b([1.0, 0.025, 2.0], [0, 0.0125, 0], "#3a3833"),
         b([0.9, 0.006, 0.04], [0, 0.028, 0.93], "#5a564e"),
         b([0.9, 0.006, 0.04], [0, 0.028, -0.93], "#5a564e"),
         b([0.04, 0.006, 1.86], [0.43, 0.028, 0], "#5a564e"),
         b([0.04, 0.006, 1.86], [-0.43, 0.028, 0], "#5a564e")]
    for a in (0, 45):
        p.append(b([0.22, 0.008, 0.22], [0, 0.029, -0.45], BRASS, [0, a, 0]))
    for i in range(5):
        w = 0.6 if i % 2 == 0 else 0.44
        p.append(b([w, 0.006, 0.035], [0, 0.028, 0.0 + i * 0.13], "#24221f"))
    return p, None


def floor_band():
    """A band of chequered stone let into the floor, between borders: 3 x 1 m (lay them end to end)."""
    p = [b([3.0, 0.012, 0.08], [0, 0.006, 0.46], "#2c2a27"),
         b([3.0, 0.012, 0.08], [0, 0.006, -0.46], "#2c2a27")]
    for i in range(6):
        for j in range(2):
            col = "#8e887c" if (i + j) % 2 == 0 else "#46433d"
            p.append(b([0.5, 0.01, 0.42], [-1.25 + i * 0.5, 0.005, -0.21 + j * 0.42], col))
    return p, None


def panelling():
    """Oak wainscot with six traceried panels and a cresting: 3 m wide, 2.3 m tall."""
    p = [b([3.0, 2.0, 0.05], [0, 1.0, 0.025], OAK, material="wood"),
         b([3.04, 0.12, 0.1], [0, 0.06, 0.05], DOAK),
         b([3.04, 0.1, 0.12], [0, 2.05, 0.06], DOAK),
         b([3.0, 0.08, 0.06], [0, 0.95, 0.06], DOAK)]
    for i in range(7):
        p.append(b([0.06, 1.9, 0.07], [-1.5 + i * 0.5, 1.0, 0.06], DOAK))
    for i in range(6):
        cx = -1.25 + i * 0.5
        p.append(b([0.34, 0.6, 0.02], [cx, 1.45, 0.055], "#2a1a0e"))
        p += arch(cx, 1.75, 0.38, 0.2, 0.04, 0.04, 0.07, DOAK, 1)
    for i in range(12):
        p.append(b([0.08, 0.14, 0.06], [-1.38 + i * 0.25, 2.17, 0.06], DOAK, [0, 0, 45]))
    return p, None


# ---- lit fittings (Props/Lighting) ---------------------------------------------------------------------------------

def corona():
    """Two iron hoops of candles, a big one under a small one, on three chains (hang it with y: the big hoop is at y)."""
    p = []
    for (r, y, n) in ((1.2, 0.0, 12), (0.6, 0.55, 6)):
        seg = 2 * r * math.sin(math.pi / n) + 0.04
        for i in range(n):
            a = 2 * math.pi * (i + 0.5) / n
            p.append(b([seg, 0.07, 0.06], [r * math.sin(a), y, r * math.cos(a)], IRON, [0, (math.degrees(a) + 90) % 360 - 180, 0]))
        for i in range(n):
            a = 2 * math.pi * i / n
            x, z = r * math.sin(a), r * math.cos(a)
            p.append(b([0.09, 0.03, 0.09], [x, y + 0.05, z], IRON))
            p += candle(x, y + 0.065, z)
    for k in range(3):
        a = 2 * math.pi * k / 3
        x0, z0 = 1.2 * math.sin(a), 1.2 * math.cos(a)
        # A chain from the big hoop up to the hook over the middle.
        L = math.hypot(1.2, 2.6)
        tilt = math.degrees(math.atan2(1.2, 2.6))
        p.append(b([0.03, L, 0.03], [x0 / 2, 1.3, z0 / 2], IRON, [-tilt, (math.degrees(a) + 180) % 360 - 180, 0]))
    p += [b([0.04, 0.5, 0.04], [0, 2.85, 0], IRON),
          b([0.2, 0.24, 0.2], [0, 0.55, 0], IRON),
          b([0.12, 0.3, 0.12], [0, 0.25, 0], IRON),
          b([0.18, 0.1, 0.18], [0, 2.6, 0], IRON)]
    return p, None


def candle_cluster():
    """Seven candles on a little three-step iron stand (for a shelf, a niche or an altar): 0.5 m."""
    p = [b([0.5, 0.04, 0.28], [0, 0.02, 0], IRON),
         b([0.36, 0.1, 0.2], [0, 0.09, 0], IRON),
         b([0.2, 0.1, 0.14], [0, 0.19, 0], IRON)]
    for x, y, h in ((-0.2, 0.04, 0.12), (-0.11, 0.04, 0.16), (0.2, 0.04, 0.1), (0.11, 0.04, 0.14),
                    (-0.06, 0.14, 0.2), (0.07, 0.14, 0.18), (0.0, 0.24, 0.24)):
        p += candle(x, y, 0.0 + (0.04 if abs(x) > 0.15 else 0.0), h)
    return p, None


# name, description, build, collision ("cosmetic" or "solid"), folder
PROPS = [
    ("Gothic Pilaster", "An engaged stone pier with rolls, a moulded base and a capital, 4 m tall (back at -Z; stack shafts on it).", pilaster),
    ("Gothic Wall Shaft", "A 2 m wall shaft with a ring, to stack on a Gothic Pilaster (back at -Z).", wall_shaft),
    ("Gothic Vault Springer", "A capital with three rib stubs where a vault starts, 1.2 m (back at -Z; on top of the shafts).", springer),
    ("Gothic Blind Arcade", "Three pointed blind arches on colonnettes over a stone sill, 3 m wide (back at -Z).", blind_arcade),
    ("Gothic Wall Niche", "An empty pointed niche with a shelf 0.5 m up and a finial, 3.3 m tall (back at -Z).", niche),
    ("Niche Saint Figure", "A small robed stone saint with a gold halo, 1.4 m, for a niche shelf.", saint_figure),
    ("Reliquary Casket", "A small gilded house-shaped casket set with coloured stones.", reliquary),
    ("Gothic Window Surround", "A moulded frame and pointed hood round a 1.5 x 4.25 m lancet window at its centre (back at -Z).", window_surround),
    ("Gothic Window Frame", "Jambs, a sill and a traceried head round a low window 1.6 m wide, 0.9-2.3 m up, left clear (back at -Z).", window_frame),
    ("Gothic Wall Tomb", "A canopied wall tomb: a stone chest with an effigy under a pointed arch, 2.6 m wide (back at -Z; the chest is solid).", wall_tomb),
    ("Memorial Plaque", "A dark memorial tablet with gilt lettering in a stone frame (back at -Z; hang it with y).", plaque),
    ("Gothic Banner Crimson", "A long crimson swallow-tailed banner with a gold chevron and rosette on a wall rod 3.2 m up (back at -Z).",
     lambda: banner("#6e1418", GOLD, GOLD)),
    ("Gothic Banner Azure", "A long blue swallow-tailed banner with a silver chevron and rosette on a wall rod 3.2 m up (back at -Z).",
     lambda: banner("#1c3466", "#c8c8cc", "#d8d8dc")),
    ("Gothic Banner Verdant", "A long green swallow-tailed banner with a gold chevron and rosette on a wall rod 3.2 m up (back at -Z).",
     lambda: banner("#1e4a2a", GOLD, GOLD)),
    ("Vault Boss", "A carved stone rosette with a gilt centre where vault ribs meet (its top 0.3 m up: hang it with y).", boss),
    ("Ledger Stone", "A worn grave slab let into the floor with a brass rosette and lettering, 1 x 2 m.", ledger),
    ("Chequered Floor Band", "A 3 m band of light and dark stone squares between borders, let into the floor (lay them end to end).", floor_band),
    ("Gothic Panelling", "Oak wainscot with six traceried panels and a cresting, 3 m wide, 2.3 m tall (back at -Z).", panelling),
]

LIGHT_PROPS = [
    ("Corona Chandelier", "Two iron hoops of eighteen candles on chains (hang it with y: the big hoop at y, the hook 3.1 m above). It lights lit maps.", corona),
    ("Candle Cluster", "Seven candles on a stepped iron stand, for a shelf, niche or altar. It lights lit maps.", candle_cluster),
]

LIGHTS = {
    "Corona Chandelier": [{"pos": [0, 0.3, 0], "color": "#ffc878", "intensity": 2.0, "range": 14, "flicker": True}],
    "Candle Cluster": [{"pos": [0, 0.45, 0.15], "color": "#ffc878", "intensity": 0.7, "range": 5, "flicker": True}],
}

SOLID = {"Gothic Wall Tomb"}

# Prefabs: the usual stacks and the furnished niches (Props/README.md "Prefabs").
PREFABS = [
    ("Gothic Respond", "A Gothic Pilaster with a Gothic Vault Springer on it: an aisle's vault respond, 5.2 m (back at -Z).",
     [{"use": "Gothic Pilaster"}, {"use": "Gothic Vault Springer", "pos": [0, 4.0, 0]}]),
    ("Gothic Tall Respond", "A Gothic Pilaster, two Gothic Wall Shafts and a Gothic Vault Springer: a tall vault respond, 9.2 m (back at -Z).",
     [{"use": "Gothic Pilaster"}, {"use": "Gothic Wall Shaft", "pos": [0, 4.0, 0]}, {"use": "Gothic Wall Shaft", "pos": [0, 6.0, 0]},
      {"use": "Gothic Vault Springer", "pos": [0, 8.0, 0]}]),
    ("Gothic Niche Saint", "A Gothic Wall Niche with a Niche Saint Figure on its shelf (back at -Z).",
     [{"use": "Gothic Wall Niche"}, {"use": "Niche Saint Figure", "pos": [0, 0.55, 0.2]}]),
    ("Gothic Niche Candles", "A Gothic Wall Niche with a lit Candle Cluster on its shelf (back at -Z). It lights lit maps.",
     [{"use": "Gothic Wall Niche"}, {"use": "Candle Cluster", "pos": [0, 0.55, 0.16]}]),
    ("Gothic Niche Reliquary", "A Gothic Wall Niche with a Reliquary Casket on its shelf (back at -Z).",
     [{"use": "Gothic Wall Niche"}, {"use": "Reliquary Casket", "pos": [0, 0.55, 0.2]}]),
]


def fname(name):
    return "".join(ch for ch in name if ch.isalnum()) + ".json"


def write(folder, prop, parts, lights=None):
    os.makedirs(folder, exist_ok=True)
    parts_json = ",\n".join("    " + json.dumps(q) for q in parts)
    head = "{\n" + ",\n".join(f"  {json.dumps(k)}: {json.dumps(v)}" for k, v in prop.items())
    tail = "\n  ]"
    if lights:
        tail += ',\n  "lights": [\n' + ",\n".join("    " + json.dumps(q) for q in lights) + "\n  ]"
    with open(os.path.join(folder, fname(prop["name"])), "w") as f:
        f.write(head + ',\n  "parts": [\n' + parts_json + tail + "\n}\n")


def main():
    count = {}
    names = set()
    for group, folder, category in ((PROPS, OUT, "Gothic"), (LIGHT_PROPS, OUT_LIGHT, "Lighting")):
        for name, desc, build in group:
            parts, collider = build()
            assert len(parts) <= 128, f"{name}: {len(parts)} parts"
            assert name not in names, name
            names.add(name)
            lo, hi = bounds(parts)
            assert all(hi[i] - lo[i] <= 24 for i in range(3)), name
            solid = name in SOLID
            prop = {"format": "call-of-slop-prop/1", "name": name, "category": category, "description": desc,
                    "collision": "solid" if solid else "cosmetic"}
            if solid and collider:
                prop["collider"] = collider
            if not solid:
                prop["fadeDistance"] = 90
            prop["scatter"] = {"randomYaw": False, "yawStep": 90}
            write(folder, prop, parts, LIGHTS.get(name))
            count[folder] = count.get(folder, 0) + 1
    for name, desc, uses in PREFABS:
        assert name not in names, name
        names.add(name)
        prop = {"format": "call-of-slop-prop/1", "name": name, "category": "Gothic", "description": desc, "collision": "cosmetic",
                "fadeDistance": 90, "scatter": {"randomYaw": False, "yawStep": 90}}
        write(os.path.join(OUT, "Prefabs"), prop, uses)
        count["prefabs"] = count.get("prefabs", 0) + 1
    print("wrote " + ", ".join(f"{v} to {os.path.relpath(k, ROOT) if os.path.isabs(k) else k}" for k, v in count.items()))


if __name__ == "__main__":
    main()
