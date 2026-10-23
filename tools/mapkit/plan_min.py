#!/usr/bin/env python3
"""Draws a plan PNG of a map file without the game and without any library beyond Python 3.

    python3 -I tools/mapkit/plan_min.py Maps/Space/04-TwinShard.json plan.png [--scale 6]

This is the minimal fallback CI uses for the "map-plans" artifact when the game's own `--gen-plan` isn't there: it
reads the ops straight from the JSON, so it knows nothing about buildings' insides, terrain heights or meshes beyond a
footprint, and placeholders for ops it can't draw (a hatched square). What it shows: the playable area (rectangle or
boundary outline), boxes shaded by their top height (Block, Slab, Wall, Perimeter, Ramp, Stairs, Bridge, Container,
Tank, Tower, Hangar, Building, Watchtower...), props (small squares), clutter and scatter rectangles (dashed), spawns
(white dots, team spawns in team colours), hills (yellow rings), flags (team-coloured diamonds), explicit waypoints
(grey dots), and the name, size and op count along the bottom. Scale: pixels per metre (default 6, 4-12 sensible).
"""
from __future__ import annotations

import json
import math
import struct
import sys
import zlib

TEAM = {"blue": (70, 120, 240), "red": (235, 70, 60), "0": (70, 120, 240), "1": (235, 70, 60)}


class Canvas:
    def __init__(self, w, h, bg):
        self.w, self.h = w, h
        self.px = bytearray(bytes(bg) * (w * h))

    def put(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            i = (y * self.w + x) * 3
            self.px[i:i + 3] = bytes(c)

    def rect(self, x0, y0, x1, y1, c):
        x0, x1 = sorted((int(math.floor(x0)), int(math.ceil(x1))))
        y0, y1 = sorted((int(math.floor(y0)), int(math.ceil(y1))))
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, self.w), min(y1, self.h)
        if x1 <= x0:
            x1 = x0 + 1
        if y1 <= y0:
            y1 = y0 + 1
        row = bytes(c) * (x1 - x0)
        for y in range(y0, min(y1, self.h)):
            i = (y * self.w + x0) * 3
            self.px[i:i + len(row)] = row

    def outline(self, x0, y0, x1, y1, c, dash=0):
        self.line(x0, y0, x1, y0, c, dash)
        self.line(x1, y0, x1, y1, c, dash)
        self.line(x1, y1, x0, y1, c, dash)
        self.line(x0, y1, x0, y0, c, dash)

    def line(self, x0, y0, x1, y1, c, dash=0):
        n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for i in range(n + 1):
            if dash and (i // dash) % 2:
                continue
            t = i / max(n, 1)
            self.put(int(round(x0 + (x1 - x0) * t)), int(round(y0 + (y1 - y0) * t)), c)

    def circle(self, cx, cy, r, c, fill=False):
        r = max(1, int(r))
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                d = dx * dx + dy * dy
                if (fill and d <= r * r) or (not fill and r * r - 2 * r <= d <= r * r + r):
                    self.put(int(cx) + dx, int(cy) + dy, c)

    def diamond(self, cx, cy, r, c):
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if abs(dx) + abs(dy) <= r:
                    self.put(int(cx) + dx, int(cy) + dy, c)

    def text(self, x, y, s, c):
        for ch in s:
            glyph = FONT.get(ch.upper(), FONT["?"])
            for row, bits in enumerate(glyph):
                for col in range(3):
                    if bits & (4 >> col):
                        self.put(x + col, y + row, c)
            x += 4

    def png(self):
        raw = b"".join(b"\x00" + bytes(self.px[y * self.w * 3:(y + 1) * self.w * 3]) for y in range(self.h))

        def chunk(tag, data):
            return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)

        return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


# A 3x5 font for the caption (digits, capitals and a little punctuation).
FONT = {
    "0": [7, 5, 5, 5, 7], "1": [2, 6, 2, 2, 7], "2": [7, 1, 7, 4, 7], "3": [7, 1, 7, 1, 7], "4": [5, 5, 7, 1, 1],
    "5": [7, 4, 7, 1, 7], "6": [7, 4, 7, 5, 7], "7": [7, 1, 1, 1, 1], "8": [7, 5, 7, 5, 7], "9": [7, 5, 7, 1, 7],
    "A": [7, 5, 7, 5, 5], "B": [6, 5, 6, 5, 6], "C": [7, 4, 4, 4, 7], "D": [6, 5, 5, 5, 6], "E": [7, 4, 7, 4, 7],
    "F": [7, 4, 7, 4, 4], "G": [7, 4, 5, 5, 7], "H": [5, 5, 7, 5, 5], "I": [7, 2, 2, 2, 7], "J": [1, 1, 1, 5, 7],
    "K": [5, 5, 6, 5, 5], "L": [4, 4, 4, 4, 7], "M": [5, 7, 7, 5, 5], "N": [6, 5, 5, 5, 5], "O": [7, 5, 5, 5, 7],
    "P": [7, 5, 7, 4, 4], "Q": [7, 5, 5, 7, 1], "R": [7, 5, 6, 5, 5], "S": [7, 4, 7, 1, 7], "T": [7, 2, 2, 2, 2],
    "U": [5, 5, 5, 5, 7], "V": [5, 5, 5, 5, 2], "W": [5, 5, 7, 7, 5], "X": [5, 5, 2, 5, 5], "Y": [5, 5, 2, 2, 2],
    "Z": [7, 1, 2, 4, 7], " ": [0, 0, 0, 0, 0], "-": [0, 0, 7, 0, 0], ".": [0, 0, 0, 0, 2], ",": [0, 0, 0, 2, 4],
    ":": [0, 2, 0, 2, 0], "(": [1, 2, 2, 2, 1], ")": [4, 2, 2, 2, 4], "/": [1, 1, 2, 4, 4], "?": [7, 1, 3, 0, 2],
    "X": [5, 5, 2, 5, 5], "_": [0, 0, 0, 0, 7], "'": [2, 2, 0, 0, 0], "+": [0, 2, 7, 2, 0], "=": [0, 7, 0, 7, 0],
}


def shade(h, hmax):
    """Box fill by top height: low boxes dark grey, tall ones light."""
    t = 0 if hmax <= 0 else max(0.0, min(1.0, h / hmax))
    v = int(90 + 140 * t)
    return (v, v, min(255, v + 10))


def boxes_of(op):
    """(x0, z0, x1, z1, top) for the ops with a footprint we can read straight off their numbers."""
    k = op.get("op")
    g = op.get
    try:
        if k in ("Block", "Collider", "DestructibleBox"):
            return [(g("x0"), g("z0"), g("x1"), g("z1"), g("y1", 1))]
        if k == "Slab":
            return [(g("x0"), g("z0"), g("x1"), g("z1"), g("yTop", 1))]
        if k == "Bridge":
            return [(g("x0"), g("z0"), g("x1"), g("z1"), g("h", 3))]
        if k == "WallX":
            t = g("t", 0.5) / 2
            return [(g("x0"), g("z") - t, g("x1"), g("z") + t, g("y1", 3))]
        if k == "WallZ":
            t = g("t", 0.5) / 2
            return [(g("x") - t, g("z0"), g("x") + t, g("z1"), g("y1", 3))]
        if k in ("Building", "Tower", "Hangar", "Hill"):
            w, d = g("w"), g("d")
            top = g("h", g("height", g("stories", 1) * 3))
            return [(g("cx") - w / 2, g("cz") - d / 2, g("cx") + w / 2, g("cz") + d / 2, top)]
        if k == "Tank":
            r = g("r")
            return [(g("x") - r, g("z") - r, g("x") + r, g("z") + r, g("h", 3))]
        if k == "Watchtower":
            return [(g("x") - 1.5, g("z") - 1.5, g("x") + 1.5, g("z") + 1.5, g("height", 6))]
        if k in ("Container", "TrainCar", "Car", "Plane"):
            L, W, H = {"Container": (6, 2.4, 2.6), "TrainCar": (12, 3, 3.5), "Car": (4.4, 1.9, 1.5), "Plane": (12, 12, 4)}[k]
            if not g("alongX", True):
                L, W = W, L
            return [(g("x") - L / 2, g("z") - W / 2, g("x") + L / 2, g("z") + W / 2, g("y", 0) + H)]
        if k == "Ramp":
            w = g("width", 2)
            L = g("height", 2) * 2.5
            if g("alongX", True):
                return [(g("x") - L / 2, g("z") - w / 2, g("x") + L / 2, g("z") + w / 2, g("y0", 0) + g("height", 2))]
            return [(g("x") - w / 2, g("z") - L / 2, g("x") + w / 2, g("z") + L / 2, g("y0", 0) + g("height", 2))]
        if k == "StairsX":
            L = g("height", 2) / 0.4 * 0.45
            return [(g("x") - L / 2, g("z0"), g("x") + L / 2, g("z1"), g("y0", 0) + g("height", 2))]
        if k == "StairsZ":
            L = g("height", 2) / 0.4 * 0.45
            return [(g("x0"), g("z") - L / 2, g("x1"), g("z") + L / 2, g("y0", 0) + g("height", 2))]
        if k == "CoverLine":
            t = g("thick", 0.4) / 2
            return [(min(g("x0"), g("x1")) - t, min(g("z0"), g("z1")) - t, max(g("x0"), g("x1")) + t, max(g("z0"), g("z1")) + t, g("height", 1))]
        if k == "RuinWall":
            return [(min(g("x0"), g("x1")) - 0.3, min(g("z0"), g("z1")) - 0.3, max(g("x0"), g("x1")) + 0.3, max(g("z0"), g("z1")) + 0.3, g("maxH", 2))]
        if k == "Prop" and "w" in op:
            return [(g("x") - g("w") / 2, g("z") - g("d") / 2, g("x") + g("w") / 2, g("z") + g("d") / 2, g("y", 0) + g("h", 1))]
        if k == "Mesh" and g("solid", False):
            r = g("radius", 0) or max(g("width", 1), g("depth", 1)) * g("scale", 1) / 2
            return [(g("x") - r, g("z") - r, g("x") + r, g("z") + r, g("y", 0) + g("height", 1) * g("scale", 1))]
    except (TypeError, KeyError):
        return []
    return []


def boundary_points(bd):
    shape = bd.get("shape", "rect")
    cx, cz = bd.get("center", [0, 0])
    if shape == "circle":
        r = bd.get("radius", 30)
        return [(cx + r * math.cos(a), cz + r * math.sin(a)) for a in [i * math.tau / 72 for i in range(72)]]
    if shape == "ellipse":
        rx, rz = bd.get("radiusX", bd.get("radius", 30)), bd.get("radiusZ", bd.get("radius", 20))
        return [(cx + rx * math.cos(a), cz + rz * math.sin(a)) for a in [i * math.tau / 72 for i in range(72)]]
    if shape in ("polygon", "smooth") and bd.get("points"):
        pts = [(float(p[0]), float(p[1])) for p in bd["points"]]
        if shape == "smooth" and len(pts) >= 3:  # a Catmull-Rom pass so the plan hints at the curve
            out = []
            n = len(pts)
            for i in range(n):
                p0, p1, p2, p3 = pts[i - 1], pts[i], pts[(i + 1) % n], pts[(i + 2) % n]
                for s in range(6):
                    t = s / 6
                    t2, t3 = t * t, t * t * t
                    out.append((0.5 * (2 * p1[0] + (-p0[0] + p2[0]) * t + (2 * p0[0] - 5 * p1[0] + 4 * p2[0] - p3[0]) * t2 + (-p0[0] + 3 * p1[0] - 3 * p2[0] + p3[0]) * t3),
                                0.5 * (2 * p1[1] + (-p0[1] + p2[1]) * t + (2 * p0[1] - 5 * p1[1] + 4 * p2[1] - p3[1]) * t2 + (-p0[1] + 3 * p1[1] - 3 * p2[1] + p3[1]) * t3)))
            return out
        return pts
    hx, hz = bd.get("halfX", 30), bd.get("halfZ", 30)
    return [(cx - hx, cz - hz), (cx + hx, cz - hz), (cx + hx, cz + hz), (cx - hx, cz + hz)]


def plan(map_path, out_path, scale=6):
    with open(map_path, encoding="utf-8") as f:
        m = json.load(f)
    ops = m.get("ops", [])
    bd = m.get("boundary")
    if bd:
        pts = boundary_points(bd)
        half_x = max(abs(p[0]) for p in pts)
        half_z = max(abs(p[1]) for p in pts)
    else:
        pts = None
        half_x, half_z = float(m.get("halfX", 40)), float(m.get("halfZ", 40))
    planet = next((o for o in ops if o.get("op") == "Planet"), None)
    if planet:
        half_x = max(half_x, planet.get("radius", 20) + abs(planet.get("x", 0)))
        half_z = max(half_z, planet.get("radius", 20) + abs(planet.get("z", 0)))
    margin = 3
    w = int((half_x + margin) * 2 * scale)
    h = int((half_z + margin) * 2 * scale) + 16
    cv = Canvas(w, h, (28, 30, 34))

    def px(x):
        return (x + half_x + margin) * scale

    def pz(z):
        return (z + half_z + margin) * scale

    # The playable area.
    if pts:
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            cv.line(px(a[0]), pz(a[1]), px(b[0]), pz(b[1]), (120, 160, 120))
    else:
        cv.rect(px(-half_x), pz(-half_z), px(half_x), pz(half_z), (46, 50, 52))
    if planet:
        cv.circle(px(planet.get("x", 0)), pz(planet.get("z", 0)), planet.get("radius", 20) * scale, (110, 95, 70), fill=True)

    boxes = []
    for op in ops:
        boxes.extend(boxes_of(op))
    hmax = max((b[4] for b in boxes), default=1) or 1
    for x0, z0, x1, z1, top in sorted(boxes, key=lambda b: b[4]):
        cv.rect(px(min(x0, x1)), pz(min(z0, z1)), px(max(x0, x1)), pz(max(z0, z1)), shade(top, hmax))
    unknown = 0
    for op in ops:
        k = op.get("op")
        g = op.get
        if k == "Perimeter":
            cv.outline(px(-half_x), pz(-half_z), px(half_x), pz(half_z), (190, 190, 200))
        elif k in ("Clutter", "DecalScatter"):
            cv.outline(px(g("x0", 0)), pz(g("z0", 0)), px(g("x1", 0)), pz(g("z1", 0)), (110, 110, 80), dash=3)
        elif k in ("PlaceProp",) or (k == "Prop" and "name" in op):
            s = max(2, scale // 3)
            cv.rect(px(g("x", 0)) - s, pz(g("z", 0)) - s, px(g("x", 0)) + s, pz(g("z", 0)) + s, (200, 150, 90))
        elif k == "PlaceBuilding":
            at = g("at", [0, 0])
            cv.diamond(px(at[0]), pz(at[-1]), max(3, scale), (170, 120, 200))
        elif k in ("Tree", "TreeIfClear"):
            cv.circle(px(g("x", 0)), pz(g("z", 0)), max(2, scale // 2), (70, 150, 80), fill=True)
        elif k == "Rocks":
            cv.circle(px(g("x", 0)), pz(g("z", 0)), g("size", 2) * scale / 2, (120, 110, 100), fill=True)
        elif k == "Spawn":
            cv.circle(px(g("x", 0)), pz(g("z", 0)), max(2, scale // 2), (245, 245, 245), fill=True)
        elif k == "Waypoint":
            cv.put(int(px(g("x", 0))), int(pz(g("z", 0))), (150, 150, 150))
        elif k == "Decal":
            cv.put(int(px(g("x", 0))), int(pz(g("z", 0))), (150, 130, 60))
        elif k in ("Mesh", "Model"):  # a cosmetic mesh: its footprint as a faint outline
            r = (g("radius", 0) or max(g("width", 1), g("depth", 1)) / 2) * g("scale", 1)
            cv.outline(px(g("x", 0) - r), pz(g("z", 0) - r), px(g("x", 0) + r), pz(g("z", 0) + r), (90, 100, 120))
        elif k in ("Ground", "AutoWaypoints", "RingSpawns", "PlanetWaypoints", "Planet", "Crater") or boxes_of(op):
            pass
        elif k and k.startswith("Surface"):
            pass  # on the planet: no plan view of a sphere here
        else:
            unknown += 1
    # Objectives.
    for hill in m.get("hills", []) or []:
        c = hill if isinstance(hill, list) else [hill.get("x", 0), hill.get("y", 0), hill.get("z", 0)]
        r = hill.get("radius", 5) if isinstance(hill, dict) else 5
        cv.circle(px(c[0]), pz(c[-1]), r * scale, (240, 210, 60))
    flags = m.get("flags") or {}
    for team, pos in (flags.items() if isinstance(flags, dict) else enumerate(flags)):
        if isinstance(pos, (list, tuple)) and len(pos) >= 2:
            cv.diamond(px(pos[0]), pz(pos[-1]), max(3, scale), TEAM.get(str(team).lower(), (200, 200, 200)))
    ts = m.get("teamSpawns") or {}
    for team, lst in (ts.items() if isinstance(ts, dict) else enumerate(ts)):
        for pos in lst or []:
            if isinstance(pos, (list, tuple)) and len(pos) >= 2:
                cv.circle(px(pos[0]), pz(pos[-1]), max(2, scale // 2), TEAM.get(str(team).lower(), (200, 200, 200)), fill=True)
    caption = f"{m.get('name', '?')}  {half_x * 2:.0f} x {half_z * 2:.0f} m  {len(ops)} ops  {len(boxes)} boxes"
    if unknown:
        caption += f"  {unknown} op(s) not drawn"
    cv.text(4, h - 12, caption[: (w - 8) // 4], (220, 220, 220))
    with open(out_path, "wb") as f:
        f.write(cv.png())
    return caption


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    if len(args) < 2:
        print(__doc__)
        return 2
    scale = 6
    if "--scale" in argv:
        scale = int(argv[argv.index("--scale") + 1])
    print(plan(args[0], args[1], scale))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
