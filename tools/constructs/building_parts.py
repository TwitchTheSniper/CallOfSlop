#!/usr/bin/env python3
"""Writes the Sandbox's building pieces (Constructs/Parts/Building) and the example buildings (Constructs/Blueprints).

Board item dev-sandbox-building-pieces-bp01: large prefabricated structure parts for anchored builds (bases, bunkers,
towers) on a common building module, at the agreed realistic scale. docs/sandbox/CONSTRUCTS.md, "Building pieces", says
how they fit together. The parts keep the structural parts' 0.25 m cell rules (tools/constructs/structure_parts.py), so
they line up with each other and with the beams and panels:

- A part is made of 0.25 m cells. Its origin is the centre of its first cell (the bottom, left, back one), so cell
  centres are whole multiples of 0.25 m and cell faces are 12.5 cm off them.
- Every socket sits on a cell face (a face of one of the part's solid boxes, facing out of it: a door's jamb inside an
  opening counts), along it at a cell centre.

The module: walls are 0.25 m thick (one cell), 2.75 m tall (11 cells); a floor slab is 0.25 m thick, so a storey is
3 m from floor to floor. Lengths are 2 m and 4 m (8 and 16 cells). A room's corners are L-shaped corner pieces with 2 m
legs (each leg 8 cells, sharing the corner cell), so straight 2 m and 4 m walls fill the module between them and four
corners close a 4 by 4 m room. Floors and foundations carry sockets at the module points (cells 0, 7, 8 and 15 of each
side), where walls stand.

Run it from the repo root: python3 -I tools/constructs/building_parts.py (it rewrites the folder and the four example
blueprints; --check only compares). Data first: the JSON files are what the game loads; this script only keeps the
families consistent and works out the blueprints' socket steps (it mirrors the game's Rot24 turns and fitting order).
"""

import json
import os
import sys

C = 0.25  # one cell
H = C / 2  # a cell face from its centre
OUT = os.path.join("Constructs", "Parts", "Building")
BP_OUT = os.path.join("Constructs", "Blueprints")
PARTS_ROOT = os.path.join("Constructs", "Parts")
GROUP = "Building"
WALL_H = 11  # cells: 2.75 m
STOREY = 12  # cells: 3 m floor to floor


def r(v):
    v = round(v + 0.0, 4)
    return 0.0 if v == 0 else v


def vec(*v):
    return [r(x) for x in v]


def fmt(v):
    return f"{v:.2f}".rstrip("0").rstrip(".")


class Part:
    def __init__(self, name, section, desc, role="frame", tool="welder", material="steel", color="#7c838c"):
        self.d = {
            "format": "call-of-slop-construct-part/1",
            "name": name,
            "description": desc,
            "group": GROUP,
            "section": section,
            "role": role,
            "tool": tool,
        }
        self.material = material
        self.color = color
        self.boxes = []
        self.sockets = []
        self.names = set()
        self.extra = {}

    # Boxes in "face metres": from the part's min corner (cell 0's min corner is at -H on each axis).
    def fbox(self, x0, y0, z0, x1, y1, z1, color=None, detail=False, hidden=False, glass=False, **shape):
        mn = (x0 - H, y0 - H, z0 - H)
        mx = (x1 - H, y1 - H, z1 - H)
        return self.mbox(mn, mx, color, detail, hidden, glass, **shape)

    def cbox(self, x0, y0, z0, x1, y1, z1, color=None, **kw):
        """A box in cells (x0..x1 exclusive)."""
        return self.fbox(x0 * C, y0 * C, z0 * C, x1 * C, y1 * C, z1 * C, color, **kw)

    def mbox(self, mn, mx, color=None, detail=False, hidden=False, glass=False, **shape):
        if min(mx[a] - mn[a] for a in range(3)) < 0.0099:
            return self
        b = {"min": vec(*mn), "max": vec(*mx)}
        if color:
            b["color"] = color
        if detail:
            b["detail"] = True
        if hidden:
            b["hidden"] = True
        if glass:
            b["glass"] = True
        b.update(shape)
        self.boxes.append(b)
        return self

    def solids(self):
        return [b for b in self.boxes if not b.get("detail")]

    def on_solid(self, at, d):
        """Is a socket at `at` facing `d` on a face of a solid box (and not covered by another solid box)?"""
        axis = "xyz".index(d[1])
        sign = 1 if d[0] == "+" else -1
        hit = False
        for b in self.solids():
            face = b["max"][axis] if sign > 0 else b["min"][axis]
            if abs(at[axis] - face) > 1e-4:
                continue
            if all(b["min"][a] - 1e-4 <= at[a] <= b["max"][a] + 1e-4 for a in range(3) if a != axis):
                hit = True
                break
        if not hit:
            return False
        probe = list(at)
        probe[axis] += sign * 0.002
        for b in self.solids():
            if all(b["min"][a] + 1e-4 < probe[a] < b["max"][a] - 1e-4 for a in range(3)):
                return False  # (an inside face: another box of the part is against it)
        return True

    def sock(self, name, at, d, kind=None, must=True):
        at = vec(*at)
        if not self.on_solid(at, d):
            assert not must, (self.d["name"], name, at, d)
            return False
        assert name not in self.names, (self.d["name"], name)
        self.names.add(name)
        s = {"name": name, "at": at, "dir": d}
        if kind:
            s["kind"] = kind
        self.sockets.append(s)
        return True

    def csock(self, name, cell, d, must=False, kind=None):
        """A socket on cell `cell`'s face `d` (only where that face is a solid face of the part)."""
        axis = "xyz".index(d[1])
        at = [cell[0] * C, cell[1] * C, cell[2] * C]
        at[axis] += H if d[0] == "+" else -H
        return self.sock(name, at, d, kind, must)

    def stats(self, mass, health, armour, seconds, scrap=None):
        self.d["mass"] = r(max(0.5, round(mass, 1)))
        self.d["health"] = int(round(health))
        self.d["armour"] = armour
        self.d["material"] = self.material
        self.d["color"] = self.color
        self.d["attachSeconds"] = seconds
        if scrap is not None:
            self.d["scrap"] = scrap
        return self

    def json(self):
        n = self.d["name"]
        assert 0 < len(self.boxes) <= 16, (n, len(self.boxes))
        assert 0 < len(self.sockets) <= 40, (n, len(self.sockets))
        assert len(n) <= 32, n
        for b in self.boxes:
            for a in range(3):
                assert max(abs(b["min"][a]), abs(b["max"][a])) <= 4.0, (n, b)
        d = dict(self.d)
        d.update(self.extra)
        d["boxes"] = self.boxes
        d["sockets"] = self.sockets
        return d


def mods(n):
    """The module points along n cells: both ends and each side of every 2 m line."""
    s = {0, n - 1}
    for k in range(8, n, 8):
        s.add(k - 1)
        s.add(k)
    return sorted(s)


def thirds(h):
    return sorted({0, h // 2, h - 1})


# ---- Materials ---------------------------------------------------------------------------------------------------------

# key: (name, kg per m2 of wall, health per m2, armour, material, colour, dark colour, tool, seconds to fit a 4 m wall)
MATS = {
    "wood": ("Wood", 30, 45, 2, "wood", "#8a6a40", "#6e5332", "wrench", 3.0),
    "tin": ("Corrugated", 18, 55, 4, "steel", "#8d959b", "#6f777d", "wrench", 3.0),
    "concrete": ("Concrete", 300, 260, 25, "concrete", "#9a978f", "#7f7c75", "welder", 5.0),
    "rc": ("Reinforced", 360, 420, 45, "concrete", "#7c827f", "#646a67", "welder", 6.0),
    "steel": ("Steel", 160, 320, 35, "steel", "#5f6870", "#4c545b", "welder", 5.0),
}
MAT_WORDS = {
    "wood": "Timber framing with plank faces: cheap, light, stops little and burns.",
    "tin": "Corrugated sheet metal on a light frame: cheap and quick, stops pistols' weakest rounds only.",
    "concrete": "Precast concrete: heavy and tough, stops pistols and SMGs.",
    "rc": "Reinforced concrete: the heaviest and toughest, stops every small arm short of a marksman rifle.",
    "steel": "Welded steel plate on a frame: tough for its weight, stops pistols, SMGs and buckshot.",
}


def mat_stats(p, key, area, base=60, seconds_scale=1.0):
    name, kgm2, hpm2, arm, material, colour, dark, tool, secs = MATS[key]
    return p.stats(kgm2 * area, base + hpm2 * area, arm, round(max(1.5, secs * seconds_scale), 1))


def mat_part(name, key, section, desc, role="frame"):
    nm, kgm2, hpm2, arm, material, colour, dark, tool, secs = MATS[key]
    return Part(name, section, f"{desc} {MAT_WORDS[key]}", role=role, tool=tool, material=material, color=colour)


def wall_details(p, key, n, h, rects):
    """A little surface detail (drawn only): plank seams, corrugation ribs, a plinth, plate seams."""
    nm, kgm2, hpm2, arm, material, colour, dark, tool, secs = MATS[key]
    L = n * C
    if key == "wood":
        for y in (0.9, 1.8):
            if y < h * C - 0.1 and len(p.boxes) < 15:
                for zf in (-0.006, C):
                    seg = [rc for rc in rects if rc[1] <= y - 0.03 and rc[3] >= y + 0.03]
                    for rc in seg[:1]:
                        p.fbox(rc[0], y - 0.02, min(zf, 0), rc[2], y + 0.02, max(zf + 0.006, 0.006) if zf < 0 else C + 0.006, dark, detail=True)
    elif key == "tin":
        ribs = max(2, int(L / 0.5))
        for k in range(1, ribs):
            if len(p.boxes) >= 15:
                break
            x = L * k / ribs
            col = [rc for rc in rects if rc[0] <= x - 0.02 and rc[2] >= x + 0.02]
            if col:
                rc = max(col, key=lambda q: q[3] - q[1])
                p.fbox(x - 0.02, rc[1], -0.012, x + 0.02, rc[3], 0.0, dark, detail=True)
    elif key in ("concrete", "rc"):
        base = [rc for rc in rects if rc[1] == 0]
        for rc in base[:3]:
            if len(p.boxes) < 16:
                p.fbox(rc[0], 0, -0.015, rc[2], 0.15, C + 0.015, dark, detail=True)
    elif key == "steel":
        for rc in rects[:3]:
            if len(p.boxes) < 16 and rc[3] - rc[1] > 0.5:
                p.fbox(rc[0], rc[3] - 0.08, -0.01, rc[2], rc[3] - 0.04, C + 0.01, dark, detail=True)


def wall_sockets(p, n, h, top=True, ends=True, faces=True):
    for x in mods(n):
        p.csock(f"b{x}", (x, 0, 0), "-y")
        if top:
            p.csock(f"t{x}", (x, h - 1, 0), "+y")
    if ends:
        for y in thirds(h):
            p.csock(f"l{y}", (0, y, 0), "-x")
            p.csock(f"r{y}", (n - 1, y, 0), "+x")
    if faces:
        for x in mods(n):
            for y in thirds(h):
                p.csock(f"f{x}_{y}", (x, y, 0), "-z")
                p.csock(f"k{x}_{y}", (x, y, 0), "+z")


def wall_from(p, key, n, h, rects, area=None):
    """A straight wall of n x h cells from rectangles (face metres along the wall and up), one cell thick."""
    for (x0, y0, x1, y1) in rects:
        p.fbox(x0, y0, 0, x1, y1, C)
    wall_details(p, key, n, h, rects)
    wall_sockets(p, n, h)
    if area is None:
        area = sum((x1 - x0) * (y1 - y0) for (x0, y0, x1, y1) in rects)
    return area


SECTION_WALLS = "Walls"
SECTION_OPEN = "Wall openings"
SECTION_CORNERS = "Corners and ends"
SECTION_STAGES = "Damaged"


def straight_wall(key, n, h=WALL_H, kind="Wall"):
    L, Hm = n * C, h * C
    name = f"{MATS[key][0]} {kind} {fmt(L)} m"
    what = {"Wall": f"A {fmt(L)} m wall, {fmt(Hm)} m tall (a storey under a 0.25 m floor).",
            "Half Wall": f"A {fmt(L)} m half wall, {fmt(Hm)} m tall: cover to crouch behind, a balcony's edge.",
            "Parapet": f"A {fmt(L)} m parapet, {fmt(Hm)} m tall: the edge of a flat roof."}[kind]
    p = mat_part(name, key, SECTION_WALLS, what)
    area = wall_from(p, key, n, h, [(0, 0, L, Hm)])
    return mat_stats(p, key, area, seconds_scale=0.6 + 0.4 * n / 16)


def stage_wall(key, n, stage):
    """A wall's damage stage (it loses chunks before it breaks off): the same sockets, fewer boxes."""
    L, Hm = n * C, WALL_H * C
    whole = f"{MATS[key][0]} Wall {fmt(L)} m"
    name = f"{whole} {'Damaged' if stage == 1 else 'Breached'}"
    p = mat_part(name, key, SECTION_STAGES,
                 f"{whole} after heavy fire: {'a chunk is gone from its top corner' if stage == 1 else 'a chunk gone from its top corner and a hole knocked through at the bottom (crouch through it)'}.")
    if n == 16:
        chunk = (12 * C, 7 * C)  # the top corner from here (x) and from this height (y) is gone
        hole = (4 * C, 8 * C, 6 * C)  # x0, x1, height of the hole at the bottom
    else:
        chunk = (5 * C, 7 * C)
        hole = (1 * C, 5 * C, 6 * C)
    rects = []
    if stage == 1:
        rects = [(0, 0, chunk[0], Hm), (chunk[0], 0, L, chunk[1])]
    else:
        rects = [(0, 0, hole[0], Hm), (hole[0], hole[2], hole[1], Hm), (hole[1], 0, chunk[0], Hm), (chunk[0], 0, L, chunk[1])]
    for (x0, y0, x1, y1) in rects:
        p.fbox(x0, y0, 0, x1, y1, C)
    # Rubble edges at the break (drawn only).
    nm, kgm2, hpm2, arm, material, colour, dark, tool, secs = MATS[key]
    p.fbox(chunk[0] - 0.08, chunk[1] - 0.06, -0.02, chunk[0] + 0.12, chunk[1] + 0.05, C + 0.02, dark, detail=True)
    p.fbox(chunk[0] + 0.2, chunk[1] - 0.04, 0.03, chunk[0] + 0.42, chunk[1] + 0.07, C - 0.03, dark, detail=True)
    if stage == 2:
        p.fbox(hole[0] - 0.06, hole[2] - 0.05, -0.02, hole[1] + 0.06, hole[2] + 0.04, C + 0.02, dark, detail=True)
    wall_sockets(p, n, WALL_H)
    # The whole wall's sockets, exactly (a stage swaps in place: every link stays). Sockets that now hang in the air are kept.
    full = Part("x", SECTION_STAGES, "")
    full.fbox(0, 0, 0, L, Hm, C)
    wall_sockets(full, n, WALL_H)
    p.sockets = [dict(s) for s in full.sockets]
    p.names = {s["name"] for s in p.sockets}
    p.extra["stage"] = True
    area = sum((x1 - x0) * (y1 - y0) for (x0, y0, x1, y1) in rects)
    whole_area = L * Hm
    p.stats(MATS[key][1] * area, 60 + MATS[key][2] * whole_area, MATS[key][3], 1.5)
    return p


def stage_links(p, key, n):
    """The whole wall's "damaged" chain: below 60% of its health it loses a chunk, below 30% it is breached."""
    L = n * C
    whole = f"{MATS[key][0]} Wall {fmt(L)} m"
    if p.d["name"] == whole:
        p.extra["damaged"] = {"below": 0.6, "part": f"{whole} Damaged"}
    elif p.d["name"] == f"{whole} Damaged":
        p.extra["damaged"] = {"below": 0.3, "part": f"{whole} Breached"}


def window_wall(key, n):
    L, Hm = n * C, WALL_H * C
    name = f"{MATS[key][0]} Window Wall {fmt(L)} m"
    wins = [(2 * C, 6 * C)] if n == 8 else [(2 * C, 6 * C), (10 * C, 14 * C)]
    sill, head = 4 * C, 8 * C  # a 1 m window, its sill 1 m up
    p = mat_part(name, key, SECTION_OPEN, f"A {fmt(L)} m wall with {'a 1 m window' if n == 8 else 'two 1 m windows'}, sills 1 m up (a Window Glass fits each).")
    rects = [(0, 0, L, sill), (0, head, L, Hm)]
    xs = [0] + [v for w in wins for v in w] + [L]
    for k in range(0, len(xs), 2):
        rects.append((xs[k], sill, xs[k + 1], head))
    for (x0, y0, x1, y1) in rects:
        p.fbox(x0, y0, 0, x1, y1, C)
    wall_details(p, key, n, WALL_H, rects)
    wall_sockets(p, n, WALL_H)
    for k, (x0, x1) in enumerate(wins):
        p.csock(f"sill{k}" if n > 8 else "sill", (int(round(x0 / C)), 3, 0), "+y", must=True)
    area = L * Hm - len(wins) * 1.0
    return mat_stats(p, key, area, seconds_scale=0.6 + 0.4 * n / 16)


def door_wall(key):
    n, L, Hm = 8, 2.0, WALL_H * C
    name = f"{MATS[key][0]} Door Wall 2 m"
    p = mat_part(name, key, SECTION_OPEN, "A 2 m wall with a 1.5 by 2.25 m doorway: a Hinge on either jamb takes a 1.25 m door.")
    rects = [(0, 0, C, Hm), (7 * C, 0, L, Hm), (C, 9 * C, 7 * C, Hm)]
    for (x0, y0, x1, y1) in rects:
        p.fbox(x0, y0, 0, x1, y1, C)
    wall_details(p, key, n, WALL_H, rects)
    wall_sockets(p, n, WALL_H)
    p.csock("hinge", (0, 4, 0), "+x", must=True)
    p.csock("hinge2", (7, 4, 0), "-x", must=True)
    return mat_stats(p, key, L * Hm - 1.5 * 2.25, seconds_scale=0.8)


def garage_wall(key):
    n, L, Hm = 16, 4.0, WALL_H * C
    name = f"{MATS[key][0]} Garage Wall 4 m"
    p = mat_part(name, key, SECTION_OPEN, "A 4 m wall with a 3 by 2.5 m opening for vehicles: two Garage Door leaves on Hinges close it.")
    rects = [(0, 0, 2 * C, Hm), (14 * C, 0, L, Hm), (2 * C, 10 * C, 14 * C, Hm)]
    for (x0, y0, x1, y1) in rects:
        p.fbox(x0, y0, 0, x1, y1, C)
    wall_details(p, key, n, WALL_H, rects)
    wall_sockets(p, n, WALL_H)
    p.csock("hinge", (1, 4, 0), "+x", must=True)
    p.csock("hinge2", (14, 4, 0), "-x", must=True)
    return mat_stats(p, key, L * Hm - 3.0 * 2.5)


def slit_wall(key):
    n, L, Hm = 8, 2.0, WALL_H * C
    name = f"{MATS[key][0]} Slit Wall 2 m"
    lo, hi = 1.3, 1.65  # eye height 1.6 standing
    p = mat_part(name, key, SECTION_OPEN, "A 2 m wall with two firing slits 0.5 m wide at eye height: shoot out, hard to hit in.")
    slits = [(C, 3 * C), (5 * C, 7 * C)]
    rects = [(0, 0, L, lo), (0, hi, L, Hm), (0, lo, C, hi), (3 * C, lo, 5 * C, hi), (7 * C, lo, L, hi)]
    for (x0, y0, x1, y1) in rects:
        p.fbox(x0, y0, 0, x1, y1, C)
    wall_details(p, key, n, WALL_H, rects)
    wall_sockets(p, n, WALL_H)
    return mat_stats(p, key, L * Hm - 2 * 0.5 * (hi - lo), seconds_scale=0.8)


def corner(key, kind="corner", h=WALL_H, label=None):
    """An L with 2 m legs (8 cells each, sharing the corner cell): leg A along +x, leg B along +z."""
    Hm = h * C
    names = {"corner": "Wall Corner", "door": "Door Corner", "window": "Window Corner", "parapet": "Parapet Corner"}
    name = f"{MATS[key][0]} {names[kind]}"
    desc = {"corner": "A room's corner: an L of two 2 m wall legs. Four close a 4 m room; straight walls fill the module between them.",
            "door": "A room's corner with a 1.5 m doorway in one leg (a Hinge on its jamb takes a 1.25 m door).",
            "window": "A room's corner with a 1 m window in one leg (sill 1 m up; a Window Glass fits it).",
            "parapet": "A flat roof's corner: an L of two 2 m parapet legs, 0.75 m tall."}[kind]
    p = mat_part(name, key, SECTION_CORNERS, desc)
    p.fbox(0, 0, 0, C, Hm, 8 * C)  # leg B (with the corner cell)
    if kind in ("corner", "parapet"):
        rects = [(C, 0, 2.0, Hm)]
        area = (2.0 + 1.75) * Hm
    elif kind == "door":
        rects = [(7 * C, 0, 2.0, Hm), (C, 9 * C, 7 * C, Hm)]
        area = 3.75 * Hm - 1.5 * 2.25
    else:
        rects = [(C, 0, 2.0, 4 * C), (C, 8 * C, 2.0, Hm), (C, 4 * C, 2 * C, 8 * C), (6 * C, 4 * C, 2.0, 8 * C)]
        area = 3.75 * Hm - 1.0
    for (x0, y0, x1, y1) in rects:
        p.fbox(x0, y0, 0, x1, y1, C)
    nm, kgm2, hpm2, arm, material, colour, dark, tool, secs = MATS[key]
    if key in ("concrete", "rc") and kind != "parapet":
        p.fbox(-0.015, 0, -0.015, 2.0, 0.15, 0, dark, detail=True)
        p.fbox(-0.015, 0, 0, 0, 0.15, 2.0, dark, detail=True)
    elif key == "tin":
        for x in (0.5, 1.0, 1.5):
            p.fbox(x - 0.02, 0, -0.012, x + 0.02, Hm, 0, dark, detail=True)
            p.fbox(-0.012, 0, x - 0.02, 0, Hm, x + 0.02, dark, detail=True)
    elif key == "steel":
        p.fbox(-0.01, Hm - 0.08, -0.01, 2.0, Hm - 0.04, 0, dark, detail=True)
    for y in thirds(h):
        for x in (0, 7):
            p.csock(f"fa{x}_{y}", (x, y, 0), "-z")
            p.csock(f"ka{x}_{y}", (x, y, 0), "+z")
            p.csock(f"fb{x}_{y}", (0, y, x), "-x")
            p.csock(f"kb{x}_{y}", (0, y, x), "+x")
        p.csock(f"ra{y}", (7, y, 0), "+x")
        p.csock(f"rb{y}", (0, y, 7), "+z")
    for (x, z) in ((0, 0), (7, 0), (0, 7)):
        p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        p.csock(f"t{x}_{z}", (x, h - 1, z), "+y")
    if kind == "door":
        p.csock("hinge", (7, 4, 0), "-x", must=True)
    if kind == "window":
        p.csock("sill", (2, 3, 0), "+y", must=True)
    # (sockets are kept to 40: the grid above already reaches it on the plain corner)
    p.sockets = p.sockets[:40]
    return mat_stats(p, key, area, seconds_scale=0.9)


def tee(key):
    """A T: a 2 m crossbar (8 cells along +x) with a 1.75 m stem from its last cell (+z)."""
    Hm = WALL_H * C
    name = f"{MATS[key][0]} Wall T"
    p = mat_part(name, key, SECTION_CORNERS, "Where an inside wall meets an outside one: a 2 m crossbar with a 1.75 m stem from its end, so the inside wall runs on the module line.")
    p.fbox(0, 0, 0, 2.0, Hm, C)
    p.fbox(7 * C, 0, C, 2.0, Hm, 2.0)
    for y in thirds(WALL_H):
        p.csock(f"l{y}", (0, y, 0), "-x")
        p.csock(f"r{y}", (7, y, 0), "+x")
        p.csock(f"s{y}", (7, y, 7), "+z")
        for x in (0, 7):
            p.csock(f"f{x}_{y}", (x, y, 0), "-z")
        p.csock(f"k0_{y}", (0, y, 0), "+z")
        p.csock(f"m{y}", (7, y, 7), "-x")
        p.csock(f"n{y}", (7, y, 7), "+x")
    for (x, z) in ((0, 0), (7, 0), (7, 7)):
        p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        p.csock(f"t{x}_{z}", (x, WALL_H - 1, z), "+y")
    return mat_stats(p, key, 3.75 * Hm, seconds_scale=0.9)


def end_cap(key):
    Hm = WALL_H * C
    name = f"{MATS[key][0]} Wall End"
    p = mat_part(name, key, SECTION_CORNERS, "A free wall's finished end: a 0.25 m post the wall's height, with a cap.")
    p.fbox(0, 0, 0, C, Hm, C)
    nm, kgm2, hpm2, arm, material, colour, dark, tool, secs = MATS[key]
    p.fbox(-0.02, Hm - 0.1, -0.02, C + 0.02, Hm, C + 0.02, dark, detail=True)
    for y in thirds(WALL_H):
        for d in ("-x", "+x", "-z", "+z"):
            p.csock(f"{d}{y}".replace("-", "m").replace("+", "p"), (0, y, 0), d)
    p.csock("b", (0, 0, 0), "-y")
    p.csock("t", (0, WALL_H - 1, 0), "+y")
    return mat_stats(p, key, C * Hm * 2, seconds_scale=0.4)


# ---- Floors, foundations --------------------------------------------------------------------------------------------

SECTION_FLOORS = "Floors"
SECTION_FOUND = "Foundations"
FLOOR_MATS = {
    # key: (label, kg per m2, health per m2, armour, material, colour, dark, tool, seconds for 4x4)
    "wood": ("Wood", 35, 50, 2, "wood", "#8a6a40", "#6e5332", "wrench", 4.0),
    "steel": ("Steel", 90, 220, 20, "steel", "#8a9096", "#6c7278", "welder", 5.0),
    "concrete": ("Concrete", 300, 260, 25, "concrete", "#9a978f", "#7f7c75", "welder", 6.0),
    "rc": ("Reinforced", 360, 420, 45, "concrete", "#7c827f", "#646a67", "welder", 7.0),
}
FLOOR_WORDS = {
    "wood": "Planks on joists: light, burns.",
    "steel": "Steel tread plate on a frame.",
    "concrete": "A precast concrete slab.",
    "rc": "A reinforced concrete slab: stops every small arm short of a marksman rifle.",
}


def slab_sockets(p, nx, nz, holes=(), edges=True, top=True, bottom=True, y=0):
    """Module sockets on a slab's top and bottom (where walls stand and hang) and a few on its edges."""
    for x in mods(nx):
        for z in mods(nz):
            if top:
                p.csock(f"t{x}_{z}", (x, y, z), "+y")
            if bottom:
                p.csock(f"b{x}_{z}", (x, 0, z), "-y")
    if edges:
        for z in sorted({0, nz - 1}):
            p.csock(f"l{z}", (0, 0, z), "-x")
            p.csock(f"r{z}", (nx - 1, 0, z), "+x")
        for x in sorted({0, nx - 1}):
            p.csock(f"f{x}", (x, 0, 0), "-z")
            p.csock(f"k{x}", (x, 0, nz - 1), "+z")


def slab(key, nx, nz, kind="plain"):
    label, kgm2, hpm2, arm, material, colour, dark, tool, secs = FLOOR_MATS[key]
    W, D = nx * C, nz * C
    names = {"plain": f"{label} Floor {fmt(W)}x{fmt(D)} m", "hatch": f"{label} Hatch Floor {fmt(W)}x{fmt(D)} m",
             "stair": f"{label} Stair Floor {fmt(W)}x{fmt(D)} m", "well": f"{label} Stairwell Floor {fmt(W)}x{fmt(D)} m"}
    descs = {"plain": f"A {fmt(W)} by {fmt(D)} m floor or ceiling slab, 0.25 m thick, on the walls' tops: the next storey stands on it.",
             "hatch": "A 4 by 4 m floor with a 1 by 2 m hatch in its middle for a Ship Ladder.",
             "stair": "A 4 by 4 m floor with a 1 by 3 m opening along one side (a cell in from the edge) over a Straight Stair; you come up 0.75 m from the wall.",
             "well": "A 4 by 4 m floor with a 2 by 2.5 m stairwell along one side (a cell in from the edge) over a Half-Turn Stair."}
    p = Part(names[kind], SECTION_FLOORS, f"{descs[kind]} {FLOOR_WORDS[key]}", role="frame", tool=tool, material=material, color=colour)
    holes = []
    if kind == "plain":
        rects = [(0, 0, nx, nz)]
    elif kind == "hatch":
        holes = [(6, 6, 10, 14)]
        rects = [(0, 0, 16, 6), (0, 14, 16, 16), (0, 6, 6, 14), (10, 6, 16, 14)]
    elif kind == "stair":
        holes = [(1, 4, 5, 16)]
        rects = [(0, 0, 16, 4), (0, 4, 1, 16), (5, 4, 16, 16)]
    else:
        holes = [(1, 6, 9, 16)]
        rects = [(0, 0, 16, 6), (0, 6, 1, 16), (9, 6, 16, 16)]
    for (x0, z0, x1, z1) in rects:
        p.cbox(x0, 0, z0, x1, 1, z1)
    if key == "wood":
        for k in range(1, 4):
            z = D * k / 4
            for (x0, z0, x1, z1) in rects:
                if z0 * C < z - 0.02 and z1 * C > z + 0.02 and len(p.boxes) < 16:
                    p.fbox(x0 * C, C, z - 0.01, x1 * C, C + 0.006, z + 0.01, dark, detail=True)
                    break
    elif key == "steel":
        for (x0, z0, x1, z1) in rects[:2]:
            p.fbox(x0 * C + 0.05, C, z0 * C + 0.05, x1 * C - 0.05, C + 0.008, z1 * C - 0.05, "#a3a8ac", detail=True)
    slab_sockets(p, nx, nz, holes)
    area = W * D - sum((x1 - x0) * (z1 - z0) * C * C for (x0, z0, x1, z1) in holes)
    p.extra["walk"] = "floor"
    return p.stats(kgm2 * area, 60 + hpm2 * area, arm, round(max(2.0, secs * area / 16), 1))


def foundation(kind, key="concrete"):
    nx = nz = 16
    label = {"concrete": "Concrete", "rc": "Reinforced"}[key]
    hp_m3, kg_m3, arm, colour, dark = {"concrete": (500, 1800, 25, "#8e8a80", "#75716a"), "rc": (800, 2000, 45, "#787d79", "#626763")}[key]
    if kind == "pad2":
        nx = nz = 8
    names = {"pad2": f"{label} Foundation 2x2 m", "pad4": f"{label} Foundation 4x4 m", "step": f"{label} Stepped Foundation", "deep": f"{label} Deep Foundation"}
    descs = {"pad2": "A 2 by 2 m pad, 0.5 m thick, that anchors a build to the ground.",
             "pad4": "A 4 by 4 m pad, 0.5 m thick, that anchors a build to the ground: a room's floor. Module sockets for walls on top.",
             "step": "A 4 by 4 m pad, 0.5 m thick, with its back half 0.5 m deeper: set its deep side downhill on a slope.",
             "deep": "A 4 by 4 m pad on four 0.5 m piers reaching 1 m below it: stands over rough or sloping ground."}
    p = Part(names[kind], SECTION_FOUND, descs[kind], role="foundation", tool="welder", material="concrete", color=colour)
    W, D = nx * C, nz * C
    p.cbox(0, 0, 0, nx, 2, nz)
    vol = W * D * 0.5
    if kind == "step":
        p.cbox(0, -2, 8, nx, 0, nz)
        vol += W * D * 0.25
    elif kind == "deep":
        for (x, z) in ((0, 0), (14, 0), (0, 14), (14, 14)):
            p.cbox(x, -4, z, x + 2, 0, z + 2)
        vol += 4 * 0.5 * 0.5 * 1.0
    p.fbox(-0.01, 2 * C - 0.04, -0.01, W + 0.01, 2 * C, D + 0.01, dark, detail=True)
    for x in mods(nx):
        for z in mods(nz):
            p.csock(f"t{x}_{z}", (x, 1, z), "+y")
    for v in mods(nx):
        p.csock(f"l{v}", (0, 1, v), "-x")
        p.csock(f"r{v}", (nx - 1, 1, v), "+x")
        p.csock(f"f{v}", (v, 1, 0), "-z")
        p.csock(f"k{v}", (v, 1, nz - 1), "+z")
    p.extra["walk"] = "floor"
    return p.stats(kg_m3 * vol, 200 + hp_m3 * vol, arm, 4.0 if nx == 8 else 6.0)


# ---- Roofs ----------------------------------------------------------------------------------------------------------

SECTION_ROOFS = "Roofs"
ROOF_MATS = {"wood": ("Wood", 25, 45, 2, "wood", "#7a5a38", "#5f462c", "wrench"), "tin": ("Corrugated", 15, 55, 4, "steel", "#8d959b", "#6f777d", "wrench"),
             "concrete": ("Concrete", 300, 260, 25, "concrete", "#9a978f", "#7f7c75", "welder")}


def roof_slope(key, nx, nz, ny, kind="slope"):
    """A mono-pitch roof (a solid wedge rising to +z), stepped collision under a smooth top: walkable."""
    label, kgm2, hpm2, arm, material, colour, dark, tool = ROOF_MATS[key]
    W, D, R = nx * C, nz * C, ny * C
    name = f"{label} {'Lean-to Roof' if kind == 'leanto' else 'Roof Slope'} {fmt(W)}x{fmt(D)} m"
    desc = (f"A lean-to roof {fmt(W)} m wide, rising {fmt(R)} m over {fmt(D)} m: a shed's roof against a wall." if kind == "leanto"
            else f"A roof slope {fmt(W)} m wide rising {fmt(R)} m over {fmt(D)} m; two back to back make a ridge (a Gable Wall closes each end).")
    p = Part(name, SECTION_ROOFS, desc, role="frame", tool=tool, material=material, color=colour)
    m = min(12, nz // 2 if nz >= 8 else nz)
    dz = D / m
    for k in range(m):
        h = R * (k + 0.5) / m
        p.fbox(0, 0, dz * k, W, max(0.02, h), dz * (k + 1), hidden=True)
    p.fbox(0, 0, 0, W, R, D, detail=True, shape="wedge", taper=[1, 0], shift=[0, -0.5])
    if key == "tin":
        for k in range(1, int(W / 0.5)):
            if len(p.boxes) < 16:
                x = 0.5 * k
                p.mbox((x - 0.02 - H, -H, -H), (x + 0.02 - H, R - H + 0.02, D - H), dark, detail=True, shape="wedge", taper=[1, 0], shift=[0, -0.5])
    for x in mods(nx):
        for z in mods(nz):
            p.csock(f"b{x}_{z}", (x, 0, z), "-y")
    for x in mods(nx):
        for y in range(ny):
            p.csock(f"k{x}_{y}", (x, y, nz - 1), "+z")
    p.extra["walk"] = "ramp"
    area = W * D
    return p.stats(kgm2 * area, 60 + hpm2 * area, arm, round(max(2.0, 4.0 * area / 16), 1))


def ridge_roof(key, nx=16, ny=16, nz=4):
    """A ridge (gable) roof: a triangular prism. In its own frame the ridge runs along y and the apex points to -z (up
    once fitted): its +z face is the base, which goes on the walls' tops."""
    label, kgm2, hpm2, arm, material, colour, dark, tool = ROOF_MATS[key]
    W, L, R = nx * C, ny * C, nz * C
    name = f"{label} Ridge Roof {fmt(W)}x{fmt(L)} m"
    p = Part(name, SECTION_ROOFS, f"A ridge roof {fmt(W)} m wide and {fmt(L)} m long, its ridge {fmt(R)} m up: two slopes and closed ends in one piece.",
             role="frame", tool=tool, material=material, color=colour)
    # Layers from the base (+z) up to the apex (-z), each narrower: stepped collision.
    for k in range(nz):
        # k = 0 is the apex layer (z cell 0), k = nz - 1 the base layer.
        half = (k + 1) * nx / (2 * nz)
        x0, x1 = nx / 2 - half, nx / 2 + half
        p.cbox(x0, 0, k, x1, ny, k + 1, hidden=True)
    p.fbox(0, 0, 0, W, L, R, detail=True, shape="wedge", taper=[0, 1], shift=[0, 0])
    for x in mods(nx):
        for y in mods(ny):
            p.csock(f"b{x}_{y}", (x, y, nz - 1), "+z")
    for y in (0, ny - 1):
        for x in (nx // 2 - 2, nx // 2 + 1):
            p.csock(f"e{x}_{y}", (x, y, nz - 1), "-y" if y == 0 else "+y")
    p.extra["walk"] = "ramp"
    area = W * L * 1.12
    return p.stats(kgm2 * area, 60 + hpm2 * area, arm, round(max(2.0, 5.0 * area / 16), 1))


def gable(key, nx=16, nz=4):
    """A gable wall: the triangle under a ridge made of two Roof Slopes. Built like the ridge roof (apex to -z, thin in y)."""
    label, kgm2, hpm2, arm, material, colour, dark, tool = ROOF_MATS[key] if key in ROOF_MATS else MATS[key][:8]
    W, R = nx * C, nz * C
    p = Part(f"{label} Gable Wall {fmt(W)} m", SECTION_ROOFS, f"The triangle closing a ridge's end, {fmt(W)} m wide and {fmt(R)} m tall, on top of a wall.",
             role="frame", tool=tool, material=material, color=colour)
    for k in range(nz):
        half = (k + 1) * nx / (2 * nz)
        x0, x1 = nx / 2 - half, nx / 2 + half
        p.cbox(x0, 0, k, x1, 1, k + 1, hidden=True)
    p.fbox(0, 0, 0, W, C, R, detail=True, shape="wedge", taper=[0, 1], shift=[0, 0])
    for x in mods(nx):
        p.csock(f"b{x}", (x, 0, nz - 1), "+z")
    p.csock("l", (0, 0, nz - 1), "-x")
    p.csock("r", (nx - 1, 0, nz - 1), "+x")
    area = W * R / 2
    return p.stats(kgm2 * area * (1 if key in ROOF_MATS else 1), 60 + hpm2 * area, arm, 2.0)


# ---- Stairs and access ----------------------------------------------------------------------------------------------

SECTION_STAIRS = "Stairs and access"
STAIR_MATS = {"wood": ("Wood", "wood", "#8a6a40", "#6e5332", "wrench", 2, 60), "steel": ("Steel", "steel", "#6f767f", "#4f565e", "welder", 20, 120),
              "concrete": ("Concrete", "concrete", "#9a978f", "#7f7c75", "welder", 25, 160)}


def step_boxes(p, key, steps, x0, x1, solid_visible, tread_colour):
    """Steps as solid boxes from the floor: drawn as they are (concrete), or hidden under thin treads (wood, steel)."""
    for (z0, z1, top) in steps:
        if solid_visible:
            p.fbox(x0, 0, z0, x1, top, z1)
        else:
            p.fbox(x0, 0, z0, x1, top, z1, hidden=True)
    if not solid_visible:
        for (z0, z1, top) in steps:
            if len(p.boxes) < 16:
                p.fbox(x0, top - 0.05, z0, x1, top, z1, tread_colour, detail=True)


def straight_stair(key, rise=STOREY, run=12, width=4, label="Straight Stair"):
    nm, material, colour, dark, tool, arm, hp = STAIR_MATS[key]
    n = 8 if rise == STOREY else 4
    stepr = rise * C / n
    stepz = run * C / n
    Rm, Dm, Wm = rise * C, run * C, width * C
    name = f"{nm} {label} {fmt(Rm)} m"
    p = Part(name, SECTION_STAIRS, f"A {fmt(Wm)} m wide flight of {n} steps rising {fmt(Rm)} m over {fmt(Dm)} m (0.375 m each){': a storey, under a Stair Floor' if rise == STOREY else ': half a storey, onto a Landing'}.",
             role="frame", tool=tool, material=material, color=colour)
    steps = [(stepz * k, stepz * (k + 1), stepr * (k + 1)) for k in range(n)]
    step_boxes(p, key, steps, 0, Wm, key == "concrete", dark)
    for x in sorted({0, width - 1}):
        for z in sorted({0, run - 1}):
            p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        p.csock(f"f{x}", (x, 0, 0), "-z")
        for y in sorted({0, rise // 2, rise - 1}):
            p.csock(f"k{x}_{y}", (x, y, run - 1), "+z")
        p.csock(f"t{x}", (x, rise - 1, run - 1), "+y")
    for z in sorted({0, run // 2 - 1, run // 2, run - 1}):
        p.csock(f"l{z}", (0, 0, z), "-x")
        p.csock(f"r{z}", (width - 1, 0, z), "+x")
    p.extra["walk"] = "stairs"
    vol = Wm * Dm * Rm / 2
    kg = {"wood": 120, "steel": 260, "concrete": 900}[key] * Wm * Dm * Rm / 3
    return p.stats(kg, hp + 60 * vol, arm, 3.0 if rise == STOREY else 2.0)


def half_turn(key):
    nm, material, colour, dark, tool, arm, hp = STAIR_MATS[key]
    p = Part(f"{nm} Half-Turn Stair 3 m", SECTION_STAIRS,
             "Two 1 m flights of four steps and a landing between them, 2 by 2.5 m: up one side, turn, up the other, a storey in all (under a Stairwell Floor).",
             role="frame", tool=tool, material=material, color=colour)
    sr, sz = 0.375, 0.375
    # Flight A (x 0..1 m) up along +z, the landing (z 1.5..2.5 m) at 1.5 m, flight B (x 1..2 m) back along -z to 3 m.
    for k in range(4):
        p.fbox(0, 0, sz * k, 1.0, sr * (k + 1), sz * (k + 1), None if key == "concrete" else None)
    p.fbox(0, 0, 1.5, 2.0, 1.5, 2.5)
    for j in range(4):
        z1 = 1.5 - sz * j
        p.fbox(1.0, 0, z1 - sz, 2.0, 1.5 + sr * (j + 1), z1)
    if key == "steel":
        p.fbox(0.98, 0, 0, 1.02, 1.5, 1.5, dark, detail=True)  # the middle stringer
    for (x, z) in ((0, 0), (3, 0), (4, 0), (7, 0), (0, 9), (7, 9)):
        p.csock(f"b{x}_{z}", (x, 0, z), "-y")
    for z in (0, 5, 9):
        p.csock(f"l{z}", (0, 0, z), "-x")
        p.csock(f"r{z}", (7, 0, z), "+x")
    for x in (0, 7):
        p.csock(f"k{x}", (x, 0, 9), "+z")
    p.csock("t", (7, 11, 0), "+y")
    p.extra["walk"] = "stairs"
    kg = {"steel": 260, "concrete": 900}[key] * 2.0 * 2.5 * 3 / 3
    return p.stats(kg, hp * 1.5 + 200, arm, 4.0)


def ship_ladder():
    p = Part("Ship Ladder 3 m", SECTION_STAIRS,
             "A steep steel ladder, 0.75 m wide: eight 0.375 m steps over 2 m, a storey up through a Hatch Floor or onto a tower.",
             role="frame", tool="welder", material="steel", color="#6f767f")
    n = 8
    steps = [(0.25 * k, 0.25 * (k + 1), 0.375 * (k + 1)) for k in range(n)]
    step_boxes(p, "steel", steps, 0, 0.75, False, "#9aa0a6")
    for x in (0, 2):
        for z in (0, 7):
            p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        for y in (0, 5, 11):
            p.csock(f"k{x}_{y}", (x, y, 7), "+z")
        p.csock(f"t{x}", (x, 11, 7), "+y")
    for z in (0, 7):
        p.csock(f"l{z}", (0, 0, z), "-x")
        p.csock(f"r{z}", (2, 0, z), "+x")
    p.extra["walk"] = "ladder"
    return p.stats(90, 260, 20, 2.5)


def landing(key):
    nm, material, colour, dark, tool, arm, hp = STAIR_MATS[key]
    p = Part(f"{nm} Landing 2x2 m", SECTION_STAIRS, "A 2 by 2 m platform 1.5 m up on four posts: half a storey, where two Stair Flights turn.",
             role="frame", tool=tool, material=material, color=colour)
    p.cbox(0, 5, 0, 8, 6, 8)
    for (x, z) in ((0, 0), (7, 0), (0, 7), (7, 7)):
        p.cbox(x, 0, z, x + 1, 5, z + 1)
    for (x, z) in ((0, 0), (7, 0), (0, 7), (7, 7)):
        p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        p.csock(f"t{x}_{z}", (x, 5, z), "+y")
    for v in (0, 3, 4, 7):
        p.csock(f"l{v}", (0, 5, v), "-x")
        p.csock(f"r{v}", (7, 5, v), "+x")
        p.csock(f"f{v}", (v, 5, 0), "-z")
        p.csock(f"k{v}", (v, 5, 7), "+z")
    p.extra["walk"] = "floor"
    return p.stats({"wood": 160, "steel": 320}[key], hp * 2 + 100, arm, 3.0)


def ramp(key, nx, nz, ny):
    label = {"concrete": "Concrete", "steel": "Steel", "wood": "Wood"}[key]
    colour, material, tool, kg, hp, arm = {"concrete": ("#9a978f", "concrete", "welder", 900, 300, 25), "steel": ("#7a8189", "steel", "welder", 200, 200, 20),
                                           "wood": ("#8a6a40", "wood", "wrench", 80, 80, 2)}[key]
    W, D, R = nx * C, nz * C, ny * C
    p = Part(f"{label} Ramp {fmt(W)}x{fmt(D)} m", SECTION_STAIRS,
             f"A ramp {fmt(W)} m wide rising {fmt(R)} m over {fmt(D)} m: walk or drive up it (onto a foundation, a loading dock, a bunker's roof).",
             role="frame", tool=tool, material=material, color=colour)
    m = min(12, int(round(R / 0.125)))
    dz = D / m
    for k in range(m):
        p.fbox(0, 0, dz * k, W, R * (k + 1) / m, dz * (k + 1), hidden=True)
    p.fbox(0, 0, 0, W, R, D, detail=True, shape="wedge", taper=[1, 0], shift=[0, -0.5])
    for x in sorted({0, nx - 1} | set(mods(nx))):
        for z in sorted({0, nz - 1}):
            p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        for y in range(ny):
            p.csock(f"k{x}_{y}", (x, y, nz - 1), "+z")
    p.extra["walk"] = "ramp"
    return p.stats(kg * W * D * R / 2, hp + 60 * W * D, arm, 3.0)


def railing(key, n):
    label, colour, dark, material, tool, kg, hp, arm = {"steel": ("Steel", "#6f767f", "#565c63", "steel", "welder", 8, 60, 6),
                                                        "wood": ("Wood", "#8a6a40", "#6e5332", "wood", "wrench", 6, 40, 1)}[key]
    L = n * C
    p = Part(f"{label} Railing {fmt(L)} m", SECTION_STAIRS, f"A 1 m railing, {fmt(L)} m long: a landing's, balcony's or roof's edge (you can't walk through it; rounds can).",
             role="frame", tool=tool, material=material, color=colour)
    t = 0.03 if key == "steel" else 0.04
    posts = [k * 4 for k in range(n // 4 + 1)]  # every metre
    for k in posts:
        x = k * C
        x0 = x if k < n else x - 2 * t
        p.fbox(x0, 0, H - t, x0 + 2 * t, 1.0, H + t)
    p.fbox(0, 1.0 - 2 * t, H - t, L, 1.0, H + t)
    p.fbox(0, 0.5 - t, H - t * 0.7, L, 0.5 + t, H + t * 0.7)
    # Bottom sockets at the posts' feet (a solid face on a cell centre).
    for k in posts:
        cell = k if k < n else n - 1
        at = (cell * C, -H, 0)
        if p.on_solid(vec(*at), "-y"):
            p.sock(f"b{cell}", at, "-y")
    p.sock("l", (-H, 0, 0), "-x", must=False)
    p.sock("r", (n * C - H, 0, 0), "+x", must=False)
    return p.stats(kg * L, hp + 20 * L, arm, 1.5)


# ---- Doors and gates ------------------------------------------------------------------------------------------------

SECTION_DOORS = "Doors and gates"


def leaf(name, desc, w, h, colour, material, mass, health, armour, bars=0, frame=None):
    """A door or gate leaf (role door): its hang socket on its -x edge, hung on a Hinge's leaf."""
    p = Part(name, SECTION_DOORS, desc, role="door", tool="wrench", material=material, color=colour)
    t = 0.06 if bars == 0 else 0.05
    if bars == 0:
        p.mbox((0, -h / 2, -t / 2), (w, h / 2, t / 2))
        p.mbox((w - 0.15, -0.05, -t / 2 - 0.04), (w - 0.1, 0.05, t / 2 + 0.04), "#c8c8c8", detail=True)
    else:
        f = 0.05
        p.mbox((0, -h / 2, -t / 2), (w, -h / 2 + f, t / 2), frame)
        p.mbox((0, h / 2 - f, -t / 2), (w, h / 2, t / 2), frame)
        p.mbox((0, -h / 2 + f, -t / 2), (f, h / 2 - f, t / 2), frame)
        p.mbox((w - f, -h / 2 + f, -t / 2), (w, h / 2 - f, t / 2), frame)
        for k in range(1, bars + 1):
            x = w * k / (bars + 1)
            p.mbox((x - 0.012, -h / 2 + f, -0.012), (x + 0.012, h / 2 - f, 0.012), frame)
        p.mbox((0, -0.02, -0.02), (w, 0.02, 0.02), frame)
    p.sock("hang", (0, 0, 0), "-x", kind="leaf")
    p.sock("front", (w / 2, 0, t / 2 if bars == 0 else 0.012), "+z", must=bars == 0)
    p.sock("back", (w / 2, 0, -t / 2 if bars == 0 else -0.012), "-z", must=bars == 0)
    return p.stats(mass, health, armour, 2.0)


def window_glass():
    p = Part("Window Glass 1 m", SECTION_OPEN, "A 1 m glass pane that fits a window wall's window (on its sill): see through it; any round breaks it.",
             role="panel", tool="wrench", material="glass", color="#9fc6d6")
    p.mbox((-H, -H, -0.015), (4 * C - H, 4 * C - H, 0.015), glass=True)
    p.mbox((-H, 2 * C - H - 0.015, -0.02), (4 * C - H, 2 * C - H + 0.015, 0.02), "#5a6068", detail=True)
    p.sock("bottom", (0, -H, 0), "-y")
    return p.stats(9, 28, 0, 1.2)


# ---- Defences -------------------------------------------------------------------------------------------------------

SECTION_DEF = "Defences"


def sandbags(n, h, name, desc, l_shape=False):
    p = Part(name, SECTION_DEF, desc, role="frame", tool="wrench", material="sand", color="#a8946a")
    L, Hm = n * C, h * C
    if not l_shape:
        p.cbox(0, 0, 0, n, h, 2)
        rows = h
        for k in range(rows):
            if len(p.boxes) < 16:
                y = (k + 0.5) * C
                p.fbox(-0.02, y - 0.03, -0.02, L + 0.02, y + 0.03, 2 * C + 0.02, "#8f7c55", detail=True, shape="rounded", radius=0.5)
        for x in mods(n):
            for z in (0, 1):
                p.csock(f"b{x}_{z}", (x, 0, z), "-y")
                p.csock(f"t{x}_{z}", (x, h - 1, z), "+y")
            p.csock(f"f{x}", (x, 0, 0), "-z")
            p.csock(f"k{x}", (x, 0, 1), "+z")
        for z in (0, 1):
            p.csock(f"l{z}", (0, 0, z), "-x")
            p.csock(f"r{z}", (n - 1, 0, z), "+x")
        vol = L * Hm * 0.5
    else:
        p.cbox(0, 0, 0, 4, h, 2)
        p.cbox(0, 0, 2, 2, h, 4)
        for (x, z) in ((0, 0), (3, 0), (0, 3), (3, 1), (1, 3)):
            p.csock(f"b{x}_{z}", (x, 0, z), "-y")
            p.csock(f"t{x}_{z}", (x, h - 1, z), "+y")
        for z in (0, 1):
            p.csock(f"r{z}", (3, 0, z), "+x")
        for x in (0, 1):
            p.csock(f"k{x}", (x, 0, 3), "+z")
        p.csock("f0", (0, 0, 0), "-z")
        p.csock("l0", (0, 0, 0), "-x")
        vol = (1.0 * 0.5 + 0.5 * 0.5) * Hm
    return p.stats(1500 * vol, 300 + 900 * vol, 30, 2.5)


def barrier():
    p = Part("Concrete Barrier 2 m", SECTION_DEF, "A 2 m precast road barrier, 0.75 m tall and wide at its foot: stops vehicles, cover to crouch behind.",
             role="frame", tool="welder", material="concrete", color="#a3a097")
    p.cbox(0, 0, 0, 8, 1, 3)
    p.fbox(0, C, 0.2, 2.0, 0.5, 0.55)
    p.fbox(0, 0.5, 0.29, 2.0, 0.75, 0.46)
    p.fbox(-0.005, 0.12, -0.005, 2.005, 0.16, 0.755, "#c2a33a", detail=True)
    for x in (0, 7):
        for z in (0, 2):
            p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        p.csock(f"l{x}", (0, 0, 1), "-x") if x == 0 else p.csock(f"r{x}", (7, 0, 1), "+x")
    for x in (0, 7):
        p.csock(f"f{x}", (x, 0, 0), "-z")
        p.csock(f"k{x}", (x, 0, 2), "+z")
    return p.stats(1200, 1400, 30, 3.0)


def blast_wall():
    p = Part("Blast Wall 2 m", SECTION_DEF, "A 2 m reinforced T-wall, 3 m tall on a 0.75 m foot: soaks up blasts and stops every small arm short of a marksman rifle.",
             role="frame", tool="welder", material="concrete", color="#8a8e88")
    p.cbox(0, 0, 0, 8, 1, 3)
    p.cbox(0, 1, 1, 8, 12, 2)
    for x in mods(8):
        for z in (0, 1, 2):
            p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        p.csock(f"t{x}", (x, 11, 1), "+y")
        for y in (1, 6, 11):
            p.csock(f"f{x}_{y}", (x, y, 1), "-z")
            p.csock(f"k{x}_{y}", (x, y, 1), "+z")
    for y in (0, 6):
        p.csock(f"l{y}", (0, y, 1), "-x")
        p.csock(f"r{y}", (7, y, 1), "+x")
    return p.stats(4200, 4000, 45, 5.0)


def fence(n):
    L = n * C
    p = Part(f"Wire Fence {fmt(L)} m", SECTION_DEF, f"A {fmt(L)} m chain-wire fence, 2 m tall and wire-topped: you see and shoot through it, can't climb or walk through it.",
             role="frame", tool="welder", material="steel", color="#7d848b")
    t = 0.04
    Hm = 2.0
    posts = [0, n] if n == 8 else [0, n // 2, n]
    for k in posts:
        x = k * C
        x0 = x if k == 0 else (x - 2 * t if k == n else x - t)
        p.fbox(x0, 0, H - t, x0 + 2 * t, Hm, H + t)
    p.fbox(0, Hm - 0.05, H - 0.02, L, Hm - 0.01, H + 0.02)
    p.fbox(0, 0.03, H - 0.02, L, 0.07, H + 0.02)
    wires = 7
    for k in range(1, wires + 1):
        x = L * k / (wires + 1)
        p.fbox(x - 0.01, 0.07, H - 0.01, x + 0.01, Hm - 0.05, H + 0.01, "#9aa1a8")
    p.fbox(0, Hm, H - 0.06, L, Hm + 0.12, H + 0.06, "#5e6369", detail=True, shape="cylinder", segments=6)
    p.fbox(0, 1.0 - 0.01, H - 0.008, L, 1.0 + 0.01, H + 0.008, "#9aa1a8", detail=True)
    for k in posts:
        cell = k if k < n else n - 1
        at = (cell * C, -H, 0)
        if p.on_solid(vec(*at), "-y"):
            p.sock(f"b{cell}", at, "-y")
    for y in (0, 4, 7):
        p.sock(f"l{y}", (-H, y * C, 0), "-x", must=False)
        p.sock(f"r{y}", (n * C - H, y * C, 0), "+x", must=False)
    return p.stats(12 * L, 120 + 40 * L, 6, 2.5)


def bunker():
    """A 4 by 4 m reinforced bunker (a foundation: it anchors), 3 m tall: two slits each side and front, a door at the back."""
    p = Part("Bunker Module", SECTION_DEF,
             "A 4 by 4 m reinforced concrete bunker, 3 m tall, that anchors itself: firing slits in the front and sides, a doorway at the back (a Hinge on its jamb takes a door), a roof to build on.",
             role="foundation", tool="welder", material="concrete", color="#7c827f")
    Hm = WALL_H * C
    lo, hi = 1.3, 1.65
    # Front wall (z 0) with two slits.
    p.fbox(0, 0, 0, 4.0, lo, C)
    p.fbox(0, hi, 0, 4.0, Hm, C)
    for (x0, x1) in ((0, 1.0), (1.5, 2.5), (3.0, 4.0)):
        p.fbox(x0, lo, 0, x1, hi, C)
    # Sides (solid) and the back with a 1.5 m doorway.
    p.fbox(0, 0, C, C, Hm, 4.0 - C)
    p.fbox(4.0 - C, 0, C, 4.0, Hm, 4.0 - C)
    p.fbox(0, 0, 4.0 - C, 1.25, Hm, 4.0)
    p.fbox(2.75, 0, 4.0 - C, 4.0, Hm, 4.0)
    p.fbox(1.25, 9 * C, 4.0 - C, 2.75, Hm, 4.0)
    p.cbox(0, WALL_H, 0, 16, WALL_H + 1, 16)
    p.fbox(-0.02, 0, -0.02, 4.02, 0.2, 0.0, "#646a67", detail=True)
    for x in mods(16):
        for z in mods(16):
            p.csock(f"t{x}_{z}", (x, WALL_H, z), "+y")
    for (x, z) in ((0, 0), (15, 0), (0, 15), (15, 15)):
        p.csock(f"b{x}_{z}", (x, 0, z), "-y")
    for v in (0, 15):
        p.csock(f"l{v}", (0, 0, v), "-x")
        p.csock(f"r{v}", (15, 0, v), "+x")
        p.csock(f"f{v}", (v, 0, 0), "-z")
        p.csock(f"k{v}", (v, 0, 15), "+z")
    p.csock("hinge", (4, 4, 15), "+x", must=True)
    p.csock("hinge2", (11, 4, 15), "-x", must=True)
    p.extra["walk"] = "floor"
    area = 4 * 4.0 * Hm + 16
    return p.stats(360 * area * 0.9, 60 + 420 * area * 0.6, 45, 8.0)


def pillbox():
    """A 3 by 3 m reinforced pillbox (a foundation): slits on three sides, an open doorway at the back."""
    p = Part("Pillbox", SECTION_DEF,
             "A 3 by 3 m reinforced concrete pillbox, 2.25 m tall, that anchors itself: a firing slit in the front and each side, a 1.5 m doorway at the back.",
             role="foundation", tool="welder", material="concrete", color="#858a84")
    Hm = 8 * C
    W = 3.0
    lo, hi = 1.3, 1.6
    for (z0, z1) in ((0, C),):
        p.fbox(0, 0, z0, W, lo, z1)
        p.fbox(0, hi, z0, W, Hm, z1)
        p.fbox(0, lo, z0, 0.9, hi, z1)
        p.fbox(2.1, lo, z0, W, hi, z1)
    for (x0, x1) in ((0, C), (W - C, W)):
        p.fbox(x0, 0, C, x1, lo, W - C)
        p.fbox(x0, hi, C, x1, Hm, W - C)
        p.fbox(x0, lo, C, x1, hi, 1.1)
        p.fbox(x0, lo, 1.9, x1, hi, W - C)
    p.fbox(0, 0, W - C, 0.75, Hm, W)
    p.fbox(2.25, 0, W - C, W, Hm, W)
    p.cbox(0, 8, 0, 12, 9, 12)
    for (x, z) in ((0, 0), (11, 0), (0, 11), (11, 11), (5, 5), (6, 6), (0, 5), (11, 6)):
        p.csock(f"t{x}_{z}", (x, 8, z), "+y")
    for (x, z) in ((0, 0), (11, 0), (0, 11), (11, 11)):
        p.csock(f"b{x}_{z}", (x, 0, z), "-y")
    for v in (0, 11):
        p.csock(f"l{v}", (0, 0, v), "-x")
        p.csock(f"r{v}", (11, 0, v), "+x")
        p.csock(f"f{v}", (v, 0, 0), "-z")
        p.csock(f"k{v}", (v, 0, 11), "+z")
    p.extra["walk"] = "floor"
    area = 4 * 3.0 * 2.0 + 9
    return p.stats(360 * area * 0.9, 60 + 420 * area * 0.6, 45, 7.0)


# ---- Watchtower kit -------------------------------------------------------------------------------------------------

SECTION_TOWER = "Watchtower"


def tower_platform():
    p = Part("Tower Platform 2x2 m", SECTION_TOWER,
             "A 2 by 2 m wooden lookout platform with a 1 m parapet on three sides, open at the front for a Ship Ladder: on four Wood Posts.",
             role="frame", tool="wrench", material="wood", color="#7a5a38")
    p.cbox(0, 0, 0, 8, 1, 8)
    p.cbox(0, 1, 7, 8, 5, 8)
    p.cbox(0, 1, 0, 1, 5, 7)
    p.cbox(7, 1, 0, 8, 5, 7)
    p.fbox(0.25, C, 0.25, 1.75, C + 0.006, 1.75, "#6e5332", detail=True)
    for (x, z) in ((0, 0), (7, 0), (0, 7), (7, 7)):
        p.csock(f"b{x}_{z}", (x, 0, z), "-y")
        p.csock(f"t{x}_{z}", (x, 4, z), "+y")
    for z in (0, 7):
        p.csock(f"l{z}", (0, 0, z), "-x")
        p.csock(f"r{z}", (7, 0, z), "+x")
    for x in (0, 7):
        p.csock(f"k{x}", (x, 0, 7), "+z")
        p.csock(f"f{x}", (x, 0, 0), "-z")
    for x in (3, 4):
        p.csock(f"front{x}", (x, 0, 0), "-z")
    p.extra["walk"] = "floor"
    return p.stats(220, 420, 2, 4.0)


def tower_roof():
    p = Part("Tower Roof 2x2 m", SECTION_TOWER, "A corrugated pyramid roof for the tower's 2 by 2 m top, on four Tower Posts.",
             role="frame", tool="wrench", material="steel", color="#8d959b")
    # In its own frame the apex points to -z (up once fitted); its base (+z face) goes on the posts.
    for k in range(3):
        e = (k + 1) / 3 * 8
        c = 4
        p.cbox(c - e / 2, c - e / 2, k, c + e / 2, c + e / 2, k + 1, hidden=True)
    p.fbox(0, 0, 0, 2.0, 2.0, 0.75, detail=True, shape="wedge", taper=[0, 0], shift=[0, 0])
    for (x, y) in ((0, 0), (7, 0), (0, 7), (7, 7)):
        p.csock(f"b{x}_{y}", (x, y, 2), "+z")
    return p.stats(60, 220, 4, 3.0)


def tower_brace():
    p = Part("Tower Brace", SECTION_TOWER, "A timber X brace, 1.5 by 2.75 m, between two tower legs 2 m apart: stiffens the tower's sides.",
             role="frame", tool="wrench", material="wood", color="#7a5a38")
    w, hcells, m = 6, 11, 6
    W, Hm = w * C, hcells * C
    dy = Hm / m
    for up in (True, False):
        for k in range(m):
            t0, t1 = k / m, (k + 1) / m
            xa = W * (t0 if up else 1 - t1)
            xb = W * (t1 if up else 1 - t0)
            p.fbox(max(0, xa - 0.04), dy * k, H - 0.04, min(W, xb + 0.04), dy * (k + 1), H + 0.04)
    for y in (0, 5, 10):
        p.sock(f"l{y}", (-H, y * C, 0), "-x", must=False)
        p.sock(f"r{y}", (W - H, y * C, 0), "+x", must=False)
    return p.stats(40, 160, 1, 2.0)


# ---- Columns and beams ----------------------------------------------------------------------------------------------

SECTION_FRAME = "Columns and beams"


def column(key, h=WALL_H, name=None, section=SECTION_FRAME):
    label, colour, material, tool, kgm, hpm, arm, cells = {
        "concrete": ("Concrete Column", "#9a978f", "concrete", "welder", 450, 600, 25, 2),
        "steel": ("Steel Column", "#5f6670", "steel", "welder", 60, 400, 30, 1),
        "wood": ("Wood Post", "#7a5a38", "wood", "wrench", 25, 120, 2, 1)}[key]
    Hm = h * C
    name = name or f"{label} {fmt(Hm)} m"
    p = Part(name, section, f"A {fmt(cells * C)} m square {label.lower()}, {fmt(Hm)} m tall: holds up a floor or a roof at a module point.",
             role="frame", tool=tool, material=material, color=colour)
    if key == "steel":
        f = 0.03
        p.fbox(0, 0, 0, C, Hm, f)
        p.fbox(0, 0, C - f, C, Hm, C)
        p.fbox(H - 0.015, 0, f, H + 0.015, Hm, C - f, "#545a63")
    else:
        p.cbox(0, 0, 0, cells, h, cells)
    for x in range(cells):
        for z in range(cells):
            p.csock(f"b{x}{z}", (x, 0, z), "-y")
            p.csock(f"t{x}{z}", (x, h - 1, z), "+y")
    for y in sorted({0, h // 2, h - 1}):
        for v in range(cells):
            p.csock(f"l{v}_{y}", (0, y, v), "-x")
            p.csock(f"r{v}_{y}", (cells - 1, y, v), "+x")
            p.csock(f"f{v}_{y}", (v, y, 0), "-z")
            p.csock(f"k{v}_{y}", (v, y, cells - 1), "+z")
    return p.stats(kgm * Hm, hpm * Hm * cells, arm, 3.0)


def beam(key, n):
    label, colour, material, tool, kgm, hpm, arm, depth = {
        "concrete": ("Concrete Beam", "#9a978f", "concrete", "welder", 300, 500, 25, 2),
        "steel": ("Steel Beam", "#5f6670", "steel", "welder", 40, 300, 30, 2),
        "wood": ("Wood Beam", "#7a5a38", "wood", "wrench", 15, 90, 2, 1)}[key]
    L = n * C
    p = Part(f"{label} {fmt(L)} m", SECTION_FRAME, f"A {label.lower()}, {fmt(L)} m long and {fmt(depth * C)} m deep: spans between columns or walls under a floor.",
             role="frame", tool=tool, material=material, color=colour)
    if key == "steel":
        f = 0.03
        p.fbox(0, 0, 0, L, f, C)
        p.fbox(0, 2 * C - f, 0, L, 2 * C, C)
        p.fbox(0, f, H - 0.015, L, 2 * C - f, H + 0.015, "#545a63")
    else:
        p.cbox(0, 0, 0, n, depth, 1)
    for x in mods(n):
        p.csock(f"b{x}", (x, 0, 0), "-y")
        p.csock(f"t{x}", (x, depth - 1, 0), "+y")
        for y in range(depth):
            p.csock(f"f{x}_{y}", (x, y, 0), "-z")
            p.csock(f"k{x}_{y}", (x, y, 0), "+z")
    for y in range(depth):
        p.csock(f"l{y}", (0, y, 0), "-x")
        p.csock(f"r{y}", (n - 1, y, 0), "+x")
    return p.stats(kgm * L, hpm * L, arm, 3.0)


# ---- The list --------------------------------------------------------------------------------------------------------

def everything():
    out = []
    wall_keys = ["wood", "tin", "concrete", "rc", "steel"]
    for k, key in enumerate(wall_keys):
        folder = f"10-Walls/{k + 1}0-{MATS[key][0]}"
        for n in (8, 16):
            w = straight_wall(key, n)
            stage_links(w, key, n)
            out.append((folder, w))
            for stage in (1, 2):
                s = stage_wall(key, n, stage)
                stage_links(s, key, n)
                out.append((f"90-Damaged/{k + 1}0-{MATS[key][0]}", s))
            out.append((folder, straight_wall(key, n, 5, "Half Wall")))
            out.append((folder, window_wall(key, n)))
        out.append((folder, door_wall(key)))
        if key != "wood":
            out.append((folder, garage_wall(key)))
        if key in ("concrete", "rc", "steel"):
            out.append((folder, slit_wall(key)))
        for kind in ("corner", "door", "window"):
            out.append((folder, corner(key, kind)))
        out.append((folder, tee(key)))
        out.append((folder, end_cap(key)))
        if key in ("wood", "concrete"):
            out.append((folder, straight_wall(key, 8, 3, "Parapet")))
            out.append((folder, straight_wall(key, 16, 3, "Parapet")))
            out.append((folder, corner(key, "parapet", 3)))
    out.append(("10-Walls/60-Glass", window_glass()))
    for k, key in enumerate(["wood", "steel", "concrete", "rc"]):
        folder = f"20-Floors/{k + 1}0-{FLOOR_MATS[key][0]}"
        for nx, nz in ((8, 8), (8, 16), (16, 16)):
            out.append((folder, slab(key, nx, nz)))
        for kind in ("hatch", "stair", "well"):
            out.append((folder, slab(key, 16, 16, kind)))
    for kind in ("pad2", "pad4", "step", "deep"):
        out.append(("30-Foundations", foundation(kind)))
    out.append(("30-Foundations", foundation("pad4", "rc")))
    out.append(("40-Roofs", roof_slope("wood", 16, 16, 4)))
    out.append(("40-Roofs", roof_slope("wood", 8, 16, 4)))
    out.append(("40-Roofs", roof_slope("tin", 16, 16, 4)))
    out.append(("40-Roofs", roof_slope("tin", 8, 16, 4)))
    out.append(("40-Roofs", roof_slope("tin", 16, 8, 2, "leanto")))
    out.append(("40-Roofs", roof_slope("tin", 8, 8, 2, "leanto")))
    out.append(("40-Roofs", ridge_roof("wood")))
    out.append(("40-Roofs", ridge_roof("tin")))
    out.append(("40-Roofs", gable("wood")))
    out.append(("40-Roofs", gable("concrete")))
    for key in ("wood", "steel", "concrete"):
        out.append(("50-Stairs", straight_stair(key)))
    for key in ("steel", "concrete"):
        out.append(("50-Stairs", straight_stair(key, rise=6, run=6, label="Stair Flight")))
        out.append(("50-Stairs", half_turn(key)))
    out.append(("50-Stairs", ship_ladder()))
    for key in ("wood", "steel"):
        out.append(("50-Stairs", landing(key)))
    out.append(("50-Stairs", ramp("concrete", 8, 16, 4)))
    out.append(("50-Stairs", ramp("concrete", 16, 16, 4)))
    out.append(("50-Stairs", ramp("steel", 8, 16, 4)))
    out.append(("50-Stairs", ramp("wood", 4, 12, 3)))
    for key in ("steel", "wood"):
        for n in (8, 16):
            out.append(("50-Stairs/Railings", railing(key, n)))
    out.append(("60-Doors", leaf("Wood Door 1.25 m", "A 1.25 by 2.2 m wooden door for a Door Wall or Door Corner (on a Hinge on the jamb).", 1.28, 2.2, "#6b5a3e", "wood", 35, 160, 2)))
    out.append(("60-Doors", leaf("Steel Door 1.25 m", "A 1.25 by 2.2 m steel door for a Door Wall, Door Corner or the Bunker Module (on a Hinge on the jamb).", 1.28, 2.2, "#5a6168", "steel", 120, 600, 30)))
    out.append(("60-Doors", leaf("Garage Door Leaf", "One leaf of a garage's double door, 1.3 by 2.2 m: two on Hinges close a Garage Wall.", 1.28, 2.2, "#7d858b", "steel", 90, 380, 6)))
    out.append(("60-Doors", leaf("Fence Gate", "A 1.8 m wire gate on a Hinge at a Wire Fence's end: fills the 2 m module with the hinge.", 1.78, 1.8, "#7d848b", "steel", 30, 200, 6, bars=5, frame="#6a7178")))
    out.append(("70-Defences", sandbags(8, 4, "Sandbag Wall 2 m", "Sandbags stacked 1 m high and 0.5 m deep, 2 m long: cover that soaks up rounds and blasts.")))
    out.append(("70-Defences", sandbags(4, 4, "Sandbag Wall 1 m", "Sandbags stacked 1 m high and 0.5 m deep, 1 m long.")))
    out.append(("70-Defences", sandbags(8, 2, "Low Sandbag Wall 2 m", "Sandbags stacked 0.5 m high, 2 m long: cover lying down, or a step to stand on.")))
    out.append(("70-Defences", sandbags(4, 4, "Sandbag Corner", "An L of sandbags, 1 m on each side and 1 m high: the corner of a gun nest.", l_shape=True)))
    out.append(("70-Defences", barrier()))
    out.append(("70-Defences", blast_wall()))
    out.append(("70-Defences", fence(8)))
    out.append(("70-Defences", fence(16)))
    out.append(("70-Defences", bunker()))
    out.append(("70-Defences", pillbox()))
    out.append(("80-Tower", tower_platform()))
    out.append(("80-Tower", tower_roof()))
    out.append(("80-Tower", tower_brace()))
    out.append(("80-Tower", column("wood", 5, "Tower Post 1.25 m", SECTION_TOWER)))
    for key in ("concrete", "steel", "wood"):
        out.append(("85-Frame", column(key)))
        for n in (8, 16):
            out.append(("85-Frame", beam(key, n)))
    return out


def file_name(name):
    s = name.replace(".", "p").replace(":", "to").replace("-", "")
    return "".join(ch for ch in s if ch.isalnum()) + ".json"


# ---- Blueprints: a mirror of the game's fitting rules (ConstructBuild.Place, Rot24) ------------------------------------

DIRS = ["+x", "-x", "+y", "-y", "+z", "-z"]


def _cols():
    out = []
    for p in ([0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]):
        for signs in range(8):
            m = [[0] * 3 for _ in range(3)]
            for c in range(3):
                m[p[c]][c] = 1 if (signs >> c & 1) == 0 else -1
            det = (m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
                   + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]))
            if det < 0:
                continue
            out.append([p[c] * 2 + (0 if (signs >> c & 1) == 0 else 1) for c in range(3)])
    return out


COLS = _cols()


def apply(rot, v):
    o = [0.0, 0.0, 0.0]
    for c in range(3):
        d = COLS[rot][c]
        o[d >> 1] += v[c] if (d & 1) == 0 else -v[c]
    return o


def apply_dir(rot, d):
    c = COLS[rot][d >> 1]
    return c if (d & 1) == 0 else c ^ 1


def turn(x="+x", y="+y"):
    """The turn taking the part's +x and +y to these body directions."""
    for rr in range(24):
        if apply_dir(rr, 0) == DIRS.index(x) and apply_dir(rr, 2) == DIRS.index(y):
            return rr
    raise ValueError((x, y))


def upright(rr):
    return apply(rr, [0, 1, 0])[1] * 2 + apply(rr, [0, 0, -1])[2] * -1 + apply(rr, [1, 0, 0])[0] * 0.1


def quant(v):
    return [round(x * 100) / 100 for x in v]


class Placed:
    def __init__(self, d, pos, rot):
        self.d, self.pos, self.rot = d, pos, rot
        self.used = set()

    def sock(self, i):
        s = self.d["sockets"][i]
        a = apply(self.rot, s["at"])
        return [self.pos[k] + a[k] for k in range(3)], apply_dir(self.rot, DIRS.index(s["dir"]))


class Blueprint:
    def __init__(self, name, desc, defs):
        self.name, self.desc, self.defs = name, desc, defs
        self.parts = []
        self.steps = []

    def first(self, part, rot=0):
        self.parts.append(Placed(self.defs[part], [0.0, 0.0, 0.0], rot))
        self.steps.append({"part": part} if rot == 0 else {"part": part, "spin": rot})
        return 0

    def _place(self, j, t, d, ds, spin):
        tgt = self.parts[j]
        at, dr = tgt.sock(t)
        want = dr ^ 1
        turns = [rr for rr in range(24) if apply_dir(rr, DIRS.index(d["sockets"][ds]["dir"])) == want]
        for a in range(len(turns)):
            for b in range(a + 1, len(turns)):
                if upright(turns[b]) > upright(turns[a]):
                    turns[a], turns[b] = turns[b], turns[a]
        rot = turns[spin % len(turns)]
        sa = apply(rot, d["sockets"][ds]["at"])
        pos = quant([at[k] - sa[k] for k in range(3)])
        return pos, rot

    def _kind(self, d, i):
        return d["sockets"][i].get("kind", "struct")

    def _commit(self, part, j, t, ds, pos, rot, step):
        d = self.defs[part]
        new = Placed(d, pos, rot)
        self.parts[j].used.add(t)
        new.used.add(ds)
        self.parts.append(new)
        self.steps.append(step)
        # AutoLink: the new part's free struct sockets that meet a free struct socket of an earlier part (first part found).
        me = len(self.parts) - 1
        for s in range(len(d["sockets"])):
            if s in new.used or self._kind(d, s) != "struct":
                continue
            at, dr = new.sock(s)
            for jj, other in enumerate(self.parts[:-1]):
                done = False
                for tt in range(len(other.d["sockets"])):
                    if tt in other.used or self._kind(other.d, tt) != "struct":
                        continue
                    oat, odr = other.sock(tt)
                    if odr == dr ^ 1 and sum((oat[k] - at[k]) ** 2 for k in range(3)) < 0.0004:
                        other.used.add(tt)
                        new.used.add(s)
                        done = True
                        break
                if done:
                    break
        return me

    def put(self, part, at, rot):
        """Fits `part` with its origin at `at` (metres, body frame) turned `rot`: finds an earlier part's free socket to go on."""
        d = self.defs[part]
        at = quant(at)
        for j, tgt in enumerate(self.parts):
            if tgt.d.get("role") == "door":
                continue
            for t in range(len(tgt.d["sockets"])):
                if t in tgt.used or self._kind(tgt.d, t) != "struct":
                    continue
                for ds in range(len(d["sockets"])):
                    if self._kind(d, ds) != "struct":
                        continue
                    for spin in range(4):
                        pos, rr = self._place(j, t, d, ds, spin)
                        if rr == rot and all(abs(pos[k] - at[k]) < 0.004 for k in range(3)):
                            step = {"part": part, "on": j, "socket": tgt.d["sockets"][t]["name"], "by": d["sockets"][ds]["name"]}
                            if spin:
                                step["spin"] = spin
                            return self._commit(part, j, t, ds, pos, rr, step)
        raise ValueError(f"{self.name}: no socket takes {part} at {at} turned {rot}")

    def hang(self, part, j, sock, by, spin=0):
        """A step by socket names (a hinge on a jamb, a door on a hinge)."""
        tgt = self.parts[j]
        d = self.defs[part]
        t = [s["name"] for s in tgt.d["sockets"]].index(sock)
        ds = [s["name"] for s in d["sockets"]].index(by)
        pos, rr = self._place(j, t, d, ds, spin)
        step = {"part": part, "on": j, "socket": sock, "by": by}
        if spin:
            step["spin"] = spin
        return self._commit(part, j, t, ds, pos, rr, step)

    def json(self):
        assert len(self.steps) <= 96
        return {"format": "call-of-slop-construct-blueprint/1", "name": self.name, "description": self.desc, "steps": self.steps}


def cell(x, y, z):
    """A cell's centre in metres, the body origin being the centre of cell (0, 0, 0)."""
    return [x * C, y * C, z * C]


R0 = 0  # no turn
RY90 = turn("-z", "+y")  # +x to -z: a quarter turn left about the vertical (seen from above, x east and z south)
RY180 = turn("-x", "+y")
RY270 = turn("+z", "+y")


def room_corners(bp, prefix, x0, y, z0, nx, nz, door=None, window=None):
    """Four corners round a room whose min cell is (x0, y, z0), nx by nz cells. door/window: which corner (0..3) gets one."""
    # Corner 0 at (x0, z0) legs +x, +z; 1 at (x0+nx-1, z0) legs +z, -x; 2 at (x0+nx-1, z0+nz-1) legs -x, -z; 3 at (x0, z0+nz-1) legs -z, +x.
    rots = [R0, RY270, RY180, RY90]
    spots = [(x0, z0), (x0 + nx - 1, z0), (x0 + nx - 1, z0 + nz - 1), (x0, z0 + nz - 1)]
    out = []
    for k in range(4):
        kind = "Door Corner" if door == k else "Window Corner" if window == k else "Wall Corner"
        out.append(bp.put(f"{prefix} {kind}", cell(spots[k][0], y, spots[k][1]), rots[k]))
    return out


def blueprints(defs):
    out = []

    # Two-Storey House: two concrete pads, a 4 by 8 m concrete house, a straight stair, a wooden ridge roof, a front door.
    bp = Blueprint("Two-Storey House", "A 4 by 8 m concrete house on two foundations: a front door, windows on both floors, a straight stair up through the floor, a wooden ridge roof.", defs)
    bp.first("Concrete Foundation 4x4 m")
    bp.put("Concrete Foundation 4x4 m", cell(0, 0, 16), R0)
    g = 2  # the ground floor's first cell row (the pads are 2 cells thick)
    # Ground floor: corners (the door corner's doorway on the east side's south end), and the long walls between them.
    room_corners(bp, "Concrete", 0, g, 0, 16, 32, door=1)
    bp.put("Concrete Window Wall 4 m", cell(0, g, 23), RY90)  # west, along +z from z 8 to 23 (turned: its +x along -z)
    bp.put("Concrete Window Wall 4 m", cell(15, g, 8), RY270)  # east
    # The stair: along the west wall, rising to -z (from z 15 up to z 4), under the stair floor's opening.
    stair = bp.put("Concrete Straight Stair 3 m", cell(4, g, 15), RY180)
    f = g + WALL_H  # the first floor slab's row
    bp.put("Concrete Stair Floor 4x4 m", cell(0, f, 0), R0)
    bp.put("Concrete Floor 4x4 m", cell(0, f, 16), R0)
    u = f + 1
    room_corners(bp, "Concrete", 0, u, 0, 16, 32, window=3)
    bp.put("Concrete Window Wall 4 m", cell(0, u, 23), RY90)
    bp.put("Concrete Window Wall 4 m", cell(15, u, 8), RY270)
    rf = u + WALL_H
    # The ridge roofs: base (+z) down, ridge along the house (z): local x to body x, local z to body -y.
    rr = turn("+x", "+z")
    bp.put("Wood Ridge Roof 4x4 m", cell(0, rf + 3, 0), rr)
    bp.put("Wood Ridge Roof 4x4 m", cell(0, rf + 3, 16), rr)
    out.append(bp)
    # The front door: a hinge on the door corner's jamb and a wooden door on it.
    door_corner = 3  # (steps: 0, 1 pads, 2 corner 0, 3 corner 1: the door corner)
    h = bp.hang("Hinge", door_corner, "hinge", "base")
    bp.hang("Wood Door 1.25 m", h, "leaf", "hang")

    # Bunker: a reinforced bunker on a pad, a sandbagged yard behind it, a steel door.
    bp = Blueprint("Bunker", "A reinforced bunker module on a concrete pad, slits facing front, a steel door at the back opening onto a sandbagged yard.", defs)
    bp.first("Concrete Foundation 4x4 m")
    bp.put("Concrete Foundation 4x4 m", cell(0, 0, 16), R0)
    bk = bp.put("Bunker Module", cell(0, 2, 0), R0)
    bp.put("Sandbag Wall 2 m", cell(0, 2, 23), RY90)
    bp.put("Sandbag Wall 2 m", cell(0, 2, 31), RY90)
    bp.put("Sandbag Wall 2 m", cell(15, 2, 16), RY270)
    bp.put("Sandbag Wall 2 m", cell(15, 2, 24), RY270)
    bp.put("Sandbag Wall 2 m", cell(2, 2, 30), R0)
    bp.put("Sandbag Wall 1 m", cell(10, 2, 30), R0)
    hb = bp.hang("Hinge", bk, "hinge", "base")
    bp.hang("Steel Door 1.25 m", hb, "leaf", "hang")
    out.append(bp)

    # Watchtower: a pad, four legs, braces, the platform, a ship ladder, posts and a roof.
    bp = Blueprint("Watchtower", "A wooden watchtower on a concrete pad: a 2 m lookout platform 3 m up with a parapet, reached by a ship ladder, under a corrugated roof.", defs)
    bp.first("Concrete Foundation 4x4 m")
    legs = ((8, 8), (15, 8), (8, 15), (15, 15))  # (module points of the pad)
    for (x, z) in legs:
        bp.put("Wood Post 2.75 m", cell(x, 2, z), R0)
    bp.put("Tower Brace", cell(9, 2, 15), R0)  # back
    bp.put("Tower Brace", cell(8, 2, 14), RY90)  # west side (along -z from 14 to 9)
    bp.put("Tower Brace", cell(15, 2, 9), RY270)  # east side
    bp.put("Tower Platform 2x2 m", cell(8, 2 + WALL_H, 8), R0)
    bp.put("Ship Ladder 3 m", cell(10, 2, 0), R0)
    for (x, z) in legs:
        bp.put("Tower Post 1.25 m", cell(x, 2 + WALL_H + 5, z), R0)
    bp.put("Tower Roof 2x2 m", cell(8, 2 + WALL_H + 5 + 5 + 2, 8), turn("+x", "+z"))
    out.append(bp)

    # Small Outpost: a corrugated hut with a door, a fenced yard with a gate, a sandbag wall at the front.
    bp = Blueprint("Small Outpost", "A corrugated hut with a wooden door on two concrete pads, its yard fenced with a gate on the east side and sandbagged at the front.", defs)
    bp.first("Concrete Foundation 4x4 m")
    bp.put("Concrete Foundation 4x4 m", cell(0, 0, 16), R0)
    hut = room_corners(bp, "Corrugated", 0, 2, 16, 16, 16, door=0)
    bp.put("Corrugated Roof Slope 4x4 m", cell(0, 2 + WALL_H, 16), R0)
    bp.put("Wire Fence 4 m", cell(0, 2, 15), RY90)  # west side of the yard
    fe = bp.put("Wire Fence 2 m", cell(15, 2, 15), RY90)  # east side, from the hut back to z 8
    bp.put("Sandbag Wall 2 m", cell(1, 2, 0), R0)
    bp.put("Sandbag Wall 1 m", cell(9, 2, 0), R0)
    hd = bp.hang("Hinge", hut[0], "hinge", "base")
    bp.hang("Wood Door 1.25 m", hd, "leaf", "hang")
    hg = bp.hang("Hinge", fe, "r4", "base")
    bp.hang("Fence Gate", hg, "leaf", "hang")
    out.append(bp)
    return out


def main():
    check = "--check" in sys.argv
    parts = everything()
    names = set()
    want = {}
    defs = {}
    for folder, p in parts:
        d = p.json()
        assert d["name"] not in names, d["name"]
        names.add(d["name"])
        defs[d["name"]] = d
        path = os.path.join(OUT, folder, file_name(d["name"]))
        assert path not in want, path
        want[path] = json.dumps(d, indent=2) + "\n"
    # Stages: the same sockets as the part they stand for.
    for d in defs.values():
        if "damaged" in d:
            t = defs[d["damaged"]["part"]]
            assert [(s["name"], s["at"], s["dir"]) for s in t["sockets"]] == [(s["name"], s["at"], s["dir"]) for s in d["sockets"]], d["name"]
    # The other parts the blueprints use (the hinge), from their files.
    for root, _, files in os.walk(PARTS_ROOT):
        if root.startswith(OUT):
            continue
        for f in files:
            if f.endswith(".json"):
                with open(os.path.join(root, f), encoding="utf-8") as fh:
                    d = json.load(fh)
                defs.setdefault(d["name"], d)
    bps = {}
    for bp in blueprints(defs):
        bps[os.path.join(BP_OUT, file_name(bp.name.replace("-", " ")))] = json.dumps(bp.json(), indent=2) + "\n"
    have = {}
    for root, _, files in os.walk(OUT):
        for f in files:
            if f.endswith(".json"):
                path = os.path.join(root, f)
                with open(path, encoding="utf-8") as fh:
                    have[path] = fh.read()
    for path in bps:
        if os.path.exists(path):
            with open(path, encoding="utf-8") as fh:
                have[path] = fh.read()
    want.update(bps)
    stale = sorted(p for p in set(have) - set(want) if p.startswith(OUT))
    changed = sorted(p for p in want if have.get(p) != want[p])
    if check:
        for p in stale:
            print(f"stale: {p}")
        for p in changed:
            print(f"out of date: {p}")
        print(f"{len(want) - len(bps)} building parts and {len(bps)} blueprints; {len(changed)} out of date, {len(stale)} stale")
        return 1 if stale or changed else 0
    for p in stale:
        os.remove(p)
    for p in changed:
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(want[p])
    print(f"{len(want) - len(bps)} building parts and {len(bps)} blueprints written ({len(changed)} changed, {len(stale)} removed)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
