#!/usr/bin/env python3
"""Writes the Sandbox's structural construct parts (Constructs/Parts/Structure) at the agreed scale.

The developer's scale chart (2026-10-29, "Realistic"): a frame step of 0.25 m, beams and plates 0.25 to 2 m, on the
unchanged 1 cm grid with Rot24 turns. docs/sandbox/CONSTRUCTS.md, "Structural parts", says how they fit together.

The rules every part here keeps (the "Structural parts" self-test checks them in every turn):
- A part is made of 0.25 m cells. Its origin is the centre of one cell, so cell centres are whole centimetres and
  cell faces are 12.5 cm off them.
- Every socket sits on a cell face, at the middle of the cell's face (the panels' edge sockets 1 cm inside the
  plate, see below). Two sockets that mate then always put the new part's origin on a whole centimetre, and the new
  part's cells line up with the old part's: the 0.25 m lattice carries on through every part.
- A thin plate lies on the face of its cells (its back on the cell face), so a plate on a beam sits flush on it.

Run it from the repo root: python3 -I tools/constructs/structure_parts.py (it rewrites the folder; --check only
compares). Data first: the JSON files are what the game loads; this script only keeps a family consistent.
"""

import json
import math
import os
import sys

C = 0.25  # the frame step (one cell)
H = C / 2  # half a cell: a cell face from its centre
OUT = os.path.join("Constructs", "Parts", "Structure")


def r(v):
    """Metres to 4 decimals (5 mm steps stay exact)."""
    v = round(v + 0.0, 4)
    return 0.0 if v == 0 else v


def vec(*v):
    return [r(x) for x in v]


def box(mn, mx, color=None, detail=False, hidden=False, **shape):
    """A box from its corners (None when it would be thinner than 1 cm: Part.add skips it)."""
    if min(mx[a] - mn[a] for a in range(3)) < 0.0099:
        return None
    b = {"min": vec(*mn), "max": vec(*mx)}
    if color:
        b["color"] = color
    if detail:
        b["detail"] = True
    if hidden:
        b["hidden"] = True
    b.update(shape)
    return b


def pick(n, m):
    """Up to m of the indices 0..n-1, evenly spread, both ends kept."""
    if n <= m:
        return list(range(n))
    if m == 1:
        return [n // 2]
    return sorted({round(i * (n - 1) / (m - 1)) for i in range(m)})


DIRS = {"+x": (0, 1), "-x": (0, -1), "+y": (1, 1), "-y": (1, -1), "+z": (2, 1), "-z": (2, -1)}


class Part:
    def __init__(self, name, group, section, desc, role="frame", tool="welder", material="steel", color="#7c838c"):
        self.d = {
            "format": "call-of-slop-construct-part/1",
            "name": name,
            "description": desc,
            "group": group,
            "section": section,
            "role": role,
            "tool": tool,
        }
        self.material = material
        self.color = color
        self.boxes = []
        self.sockets = []
        self.names = set()

    def add(self, b):
        if b is not None:
            self.boxes.append(b)
        return self

    def sock(self, name, at, d):
        assert name not in self.names, name
        self.names.add(name)
        self.sockets.append({"name": name, "at": vec(*at), "dir": d})

    def stats(self, mass, health, armour, seconds=None, scrap=None):
        self.d["mass"] = r(max(0.5, round(mass, 1)))
        self.d["health"] = int(round(health))
        self.d["armour"] = armour
        self.d["material"] = self.material
        self.d["color"] = self.color
        if seconds is not None:
            self.d["attachSeconds"] = seconds
        if scrap is not None:
            self.d["scrap"] = scrap
        return self

    def json(self):
        assert 0 < len(self.boxes) <= 16, (self.d["name"], len(self.boxes))
        assert 0 < len(self.sockets) <= 40, (self.d["name"], len(self.sockets))
        assert len(self.d["name"]) <= 32, self.d["name"]
        d = dict(self.d)
        d["boxes"] = self.boxes
        d["sockets"] = self.sockets
        return d


def cells_min(n):
    return -H


def cells_max(n):
    return n * C - H


def faces_of_block(p, nx, ny, nz, faces="xyz", limit=None, skip=()):
    """Sockets on the faces of an nx x ny x nz block of cells (origin: the centre of cell 0,0,0), one per cell face."""
    lim = limit or {}
    dims = (nx, ny, nz)
    for axis, a in enumerate("xyz"):
        if a not in faces:
            continue
        others = [k for k in range(3) if k != axis]
        for sign, sname in ((-1, "-"), (1, "+")):
            d = sname + a
            if d in skip:
                continue
            u, v = others
            iu = pick(dims[u], lim.get(d, (99, 99))[0])
            iv = pick(dims[v], lim.get(d, (99, 99))[1])
            for i in iu:
                for j in iv:
                    at = [0.0, 0.0, 0.0]
                    at[axis] = cells_max(dims[axis]) if sign > 0 else cells_min(dims[axis])
                    at[u] = i * C
                    at[v] = j * C
                    nm = d if dims[u] * dims[v] == 1 else f"{d}{i}{j}" if dims[u] > 1 and dims[v] > 1 else f"{d}{i if dims[u] > 1 else j}"
                    p.sock(nm, at, d)


# ---- Beams -----------------------------------------------------------------------------------------------------------

BEAMS = "Beams"


def seams(p, n, half, color):
    """Darker rings at each 0.25 m along a beam (detail): its length reads at a glance."""
    for k in range(1, n):
        z = k * C - H
        p.add(box((-half - 0.003, -half - 0.003, z - 0.008), (half + 0.003, half + 0.003, z + 0.008), color, detail=True))


def square_beam(n):
    L = n * C
    name = f"Square Beam {fmt(L)} m"
    p = Part(name, BEAMS, "Square beams",
             f"A welded 0.25 m square steel beam, {fmt(L)} m long: frames, chassis rails, posts. Sockets every 0.25 m."
             if n > 1 else "A 0.25 m welded steel cube: the frame's node. Sockets on all six faces.",
             color="#6f767f")
    p.add(box((-H, -H, -H), (H, H, cells_max(n))))
    seams(p, n, H, "#5d636b")
    faces_of_block(p, 1, 1, n)
    return p.stats(2.5 * n, 50 + 100 * L, 8, 1.5 if n <= 2 else 2.5)


def i_beam(n):
    L = n * C
    p = Part(f"I-Beam {fmt(L)} m", BEAMS, "I-beams",
             f"A steel I-beam, 0.25 m deep, {fmt(L)} m long: stiffer and tougher than a square beam for the same weight.",
             color="#5f6670")
    z0, z1 = -H, cells_max(n)
    f = 0.03
    p.add(box((-H, H - f, z0), (H, H, z1)))
    p.add(box((-H, -H, z0), (H, -H + f, z1)))
    p.add(box((-0.015, -H + f, z0), (0.015, H - f, z1), "#545a63"))
    faces_of_block(p, 1, 1, n)
    return p.stats(3.2 * n, 60 + 140 * L, 10, 2.5)


def tube(n, kind):
    L = n * C
    if kind == "tube":
        half, seg, mass, hp, arm, colour = 0.055, 12, 1.0, (30, 60), 5, "#8a9098"
        name, desc = f"Steel Tube {fmt(L)} m", f"An 11 cm steel tube, {fmt(L)} m long: roll cages, rails and light frames."
    else:
        half, seg, mass, hp, arm, colour = 0.025, 8, 0.4, (15, 30), 3, "#9aa0a6"
        name, desc = f"Steel Rod {fmt(L)} m", f"A 5 cm steel rod, {fmt(L)} m long: railings, bars and grilles."
    p = Part(name, BEAMS, "Tubes and rods", desc, color=colour)
    z0, z1 = -H, cells_max(n)
    p.add(box((-half, -half, z0), (half, half, z1), hidden=True))
    p.add(box((-half, -half, z0), (half, half, z1), detail=True, shape="cylinder", segments=seg))
    p.sock("end-", (0, 0, z0), "-z")
    p.sock("end+", (0, 0, z1), "+z")
    for k in range(n):
        for d, at in (("+x", (half, 0, k * C)), ("-x", (-half, 0, k * C)), ("+y", (0, half, k * C)), ("-y", (0, -half, k * C))):
            p.sock(f"{d}{k}" if n > 1 else d, at, d)
    return p.stats(mass * n, hp[0] + hp[1] * L, arm, 1.5)


def angle_iron(n):
    L = n * C
    p = Part(f"Angle Iron {fmt(L)} m", BEAMS, "Angle and channel",
             f"An L-section steel angle, 0.25 m legs, {fmt(L)} m long: edges, ledges and light bracing.", color="#7a7f86")
    z0, z1, t = -H, cells_max(n), 0.03
    p.add(box((-H, -H, z0), (H, -H + t, z1)))
    p.add(box((-H, -H + t, z0), (-H + t, H, z1)))
    faces_of_block(p, 1, 1, n, skip=("+x", "+y"))
    return p.stats(1.0 * n, 30 + 70 * L, 6, 2.0)


def channel(n):
    L = n * C
    p = Part(f"Channel {fmt(L)} m", BEAMS, "Angle and channel",
             f"A U-section steel channel, 0.25 m wide, {fmt(L)} m long: rails, runners and gutters.", color="#727980")
    z0, z1, t = -H, cells_max(n), 0.03
    p.add(box((-H, -H, z0), (H, -H + t, z1)))
    p.add(box((-H, -H + t, z0), (-H + t, H, z1)))
    p.add(box((H - t, -H + t, z0), (H, H, z1)))
    faces_of_block(p, 1, 1, n, skip=("+y",))
    return p.stats(1.5 * n, 40 + 90 * L, 7, 2.0)


def steps_diagonal(p, ny, nz, m, t, colour=None, y_up=True):
    """A bar from the bottom-front corner to the top-back corner of an ny x nz block of cells (Y-Z plane), as m steps."""
    y0, y1 = -H, cells_max(ny)
    z0, z1 = -H, cells_max(nz)
    dy, dz = (y1 - y0) / m, (z1 - z0) / m
    for k in range(m):
        ya = y0 + dy * k if y_up else y1 - dy * (k + 1)
        p.add(box((-t, max(y0, ya - dy * 0.35), z0 + dz * k), (t, min(y1, ya + dy * 1.35), z0 + dz * (k + 1)), colour))


def brace(ny, nz):
    hy, hz = ny * C, nz * C
    length = math.hypot(hy, hz)
    if ny == nz:
        name, sec = f"Brace 45 {fmt(hy)} m", f"A 45 degree steel brace across a {fmt(hy)} m square"
    else:
        name, sec = f"Brace 1:2 {fmt(hy)}x{fmt(hz)} m", f"A shallow steel brace (1 up, 2 along) across {fmt(hy)} by {fmt(hz)} m"
    p = Part(name, BEAMS, "Braces", sec + ": stiffens a frame's corner from one rail to the other.", color="#7f868e")
    m = min(16, max(4, 2 * max(ny, nz)))
    steps_diagonal(p, ny, nz, m, 0.025)
    p.sock("bottom", (0, -H, 0), "-y")
    p.sock("front", (0, 0, -H), "-z")
    p.sock("top", (0, cells_max(ny), (nz - 1) * C), "+y")
    p.sock("back", (0, (ny - 1) * C, cells_max(nz)), "+z")
    return p.stats(3 * length, 30 + 60 * length, 5, 1.8)


def cross_brace(n):
    s = n * C
    p = Part(f"Cross Brace {fmt(s)} m", BEAMS, "Braces",
             f"Two steel braces crossed in a {fmt(s)} m square: an X that keeps a frame panel square.", color="#7f868e")
    m = min(8, 2 * n)
    steps_diagonal(p, n, n, m, 0.025)
    steps_diagonal(p, n, n, m, 0.025, y_up=False)
    top, back = cells_max(n), cells_max(n)
    for d, at in (("bottom0", ((0, -H, 0), "-y")), ("bottom1", ((0, -H, (n - 1) * C), "-y")),
                  ("top0", ((0, top, 0), "+y")), ("top1", ((0, top, (n - 1) * C), "+y")),
                  ("front0", ((0, 0, -H), "-z")), ("front1", ((0, (n - 1) * C, -H), "-z")),
                  ("back0", ((0, 0, back), "+z")), ("back1", ((0, (n - 1) * C, back), "+z"))):
        p.sock(d, at[0], at[1])
    return p.stats(6 * s * 1.414, 40 + 100 * s, 5, 2.2)


def voxel(p, cells, colour=None):
    """Solid boxes for a set of cells (merged along X where they run) and a socket on every cell face on the bounds."""
    cells = set(cells)
    lo = [min(c[a] for c in cells) for a in range(3)]
    hi = [max(c[a] for c in cells) for a in range(3)]
    done = set()
    for c in sorted(cells, key=lambda c: (c[1], c[2], c[0])):
        if c in done:
            continue
        x1 = c[0]
        while (x1 + 1, c[1], c[2]) in cells and (x1 + 1, c[1], c[2]) not in done:
            x1 += 1
        for x in range(c[0], x1 + 1):
            done.add((x, c[1], c[2]))
        p.add(box((c[0] * C - H, c[1] * C - H, c[2] * C - H), (x1 * C + H, c[1] * C + H, c[2] * C + H), colour))
    for c in sorted(cells):
        for d, (axis, sign) in DIRS.items():
            n = list(c)
            n[axis] += sign
            if tuple(n) in cells:
                continue
            if (sign > 0 and c[axis] != hi[axis]) or (sign < 0 and c[axis] != lo[axis]):
                continue  # (an inside face: not on the part's bounds)
            at = [c[0] * C, c[1] * C, c[2] * C]
            at[axis] += sign * H
            p.sock(f"{d}{c[0] - lo[0]}{c[1] - lo[1]}{c[2] - lo[2]}".replace("-", "m").replace("+", "p"), at, d)


def junction(name, cells, desc):
    p = Part(name, BEAMS, "Junctions", desc, color="#6a7078")
    voxel(p, cells)
    p.add(box((-H - 0.006, -H - 0.006, -H - 0.006), (H + 0.006, H + 0.006, H + 0.006), "#596068", detail=True))
    n = len(cells)
    return p.stats(2.5 * n, 60 + 30 * n, 8, 2.0)


def end_cap(kind):
    if kind == "beam":
        p = Part("Beam End Cap", BEAMS, "Caps", "A 4 cm steel cap with a bevelled face for the open end of a beam.", color="#7d848c")
        t = 0.04
        p.add(box((-H, -H, -H), (H, H, -H + t)))
        p.add(box((-H + 0.02, -H + 0.02, -H + t), (H - 0.02, H - 0.02, -H + t + 0.015), "#6a7078", detail=True, shape="bevel", radius=0.4))
        p.sock("back", (0, 0, -H), "-z")
        p.sock("front", (0, 0, -H + t), "+z")
        return p.stats(1.2, 60, 8, 1.2)
    p = Part("Tube End Cap", BEAMS, "Caps", "A domed steel cap for the end of a steel tube.", color="#8a9098")
    t, half = 0.03, 0.055
    p.add(box((-half, -half, -H), (half, half, -H + t), hidden=True))
    p.add(box((-half, -half, -H), (half, half, -H + t), detail=True, shape="cylinder", segments=12))
    p.sock("back", (0, 0, -H), "-z")
    p.sock("front", (0, 0, -H + t), "+z")
    return p.stats(0.5, 30, 5, 1.0)


def bracket(kind):
    t = 0.02
    if kind == "L":
        p = Part("L Bracket", BEAMS, "Brackets and gussets",
                 "A 0.25 m steel angle bracket for an inside corner: weld it into the corner between a floor and a wall.", color="#8c9298")
        p.add(box((-H, -H, -H), (H, -H + t, H)))
        p.add(box((-H, -H + t, -H), (-H + t, H, H)))
        p.sock("-y", (0, -H, 0), "-y")
        p.sock("-x", (-H, 0, 0), "-x")
        p.sock("-z", (0, 0, -H), "-z")
        p.sock("+z", (0, 0, H), "+z")
        return p.stats(0.8, 40, 5, 1.2)
    p = Part("Corner Bracket", BEAMS, "Brackets and gussets",
             "A three-sided steel bracket for the inside corner where a floor meets two walls.", color="#8c9298")
    p.add(box((-H, -H, -H), (H, -H + t, H)))
    p.add(box((-H, -H + t, -H), (-H + t, H, H)))
    p.add(box((-H + t, -H + t, -H), (H, H, -H + t)))
    p.sock("-y", (0, -H, 0), "-y")
    p.sock("-x", (-H, 0, 0), "-x")
    p.sock("-z", (0, 0, -H), "-z")
    return p.stats(1.2, 50, 5, 1.2)


def gusset(n):
    s = n * C
    p = Part(f"Gusset {fmt(s)} m", BEAMS, "Brackets and gussets",
             f"A triangular steel plate, {fmt(s)} m on its two short sides: stands in a corner to stiffen it.", color="#858b92")
    t = 0.015
    m = 3 * n if n == 1 else 2 * n
    y0, top = -H, cells_max(n)
    z0, z1 = -H, cells_max(n)
    dz = (z1 - z0) / m
    for k in range(m):
        # Columns from the front: shortest at the front, full height at the back.
        h = (top - y0) * (k + 1) / m
        p.add(box((-t, y0, z0 + dz * k), (t, y0 + h, z0 + dz * (k + 1))))
    for k in range(n):
        p.sock(f"bottom{k}" if n > 1 else "bottom", (0, -H, k * C), "-y")
        p.sock(f"back{k}" if n > 1 else "back", (0, k * C, z1), "+z")
    return p.stats(4 * s * s + 0.4, 30 + 80 * s, 6, 1.2)


# ---- Panels ----------------------------------------------------------------------------------------------------------

PANELS, ARMOUR = "Panels", "Armour"

# material: (section, group, thickness, kg per m2, health base, health per m2, armour, tool, seconds, colour, material, words)
MATS = {
    "sheet": ("Sheet steel", PANELS, 0.02, 8, 20, 50, 6, "welder", 1.5, "#a3a9ae", "steel",
              "Thin sheet steel (2 cm): light skin and cover that stops shotgun pellets, not rifles."),
    "plate": ("Plate steel", PANELS, 0.05, 20, 40, 120, 20, "welder", 2.5, "#8a9096", "steel",
              "Plate steel (5 cm): stops pistols, SMG rounds up to 20 damage and every shotgun pellet."),
    "wood": ("Wood", PANELS, 0.05, 7, 25, 65, 1, "wrench", 1.2, "#8a6a40", "wood",
             "Wooden planks (5 cm): cheap, light cover that burns."),
    "glass": ("Glass", PANELS, 0.03, 9, 8, 20, 0, "wrench", 1.2, "#9fc6d6", "glass",
              "A glass pane (3 cm) in a thin frame: you can see through it; any bullet breaks it."),
    "mesh": ("Mesh and grate", PANELS, 0.03, 5, 30, 50, 3, "welder", 1.5, "#6e7378", "steel",
             "A steel grate (3 cm): bars you can walk on and see and shoot through."),
    "tread": ("Tread plate", PANELS, 0.03, 12, 40, 100, 10, "welder", 2.0, "#9a9ea2", "steel",
              "Steel tread plate (3 cm) with a raised pattern: floors, decks and steps."),
    "armour": ("Armour plate", ARMOUR, 0.08, 45, 80, 240, 40, "welder", 3.5, "#5d6650", "armour",
               "Armour plate (8 cm): stops every SMG, assault rifle, LMG and shotgun round and pistols up to 40 damage."),
    "heavy": ("Heavy armour", ARMOUR, 0.12, 80, 150, 400, 68, "welder", 5.0, "#4a5240", "armour",
              "Heavy armour (12 cm): stops every pistol, SMG, rifle, LMG, shotgun and marksman round; snipers and heavy guns get through."),
}
MAT_NAME = {"sheet": "Sheet Steel", "plate": "Plate Steel", "wood": "Wood Planks", "glass": "Glass Pane", "mesh": "Mesh Grate",
            "tread": "Tread Plate", "armour": "Armour Plate", "heavy": "Heavy Armour"}


def panel_sockets(p, nx, nz, t, front=True):
    """A plate's sockets: its back (on the cell face) and front at cell centres, its four edges 1 cm into the plate."""
    back_y = -H
    nb = (4, 4)
    for i in pick(nx, nb[0]):
        for k in pick(nz, nb[1]):
            p.sock(f"back{i}{k}" if nx * nz > 1 else "back", (i * C, back_y, k * C), "-y")
    edge_y = -0.12  # (1 cm in from the back: a whole centimetre, inside the thinnest plate)
    for d, axis, n_along, pos in (("-x", 0, nz, -H), ("+x", 0, nz, cells_max(nx)), ("-z", 2, nx, -H), ("+z", 2, nx, cells_max(nz))):
        for j in pick(n_along, 4):
            at = [0.0, edge_y, 0.0]
            at[axis] = pos
            at[2 - axis] = j * C
            p.sock(f"{d}{j}" if n_along > 1 else d, at, d)
    if front:
        left = 40 - len(p.sockets)
        per = max(1, int(math.sqrt(left)))
        for i in pick(nx, min(per, 3)):
            for k in pick(nz, min(per, 3)):
                p.sock(f"front{i}{k}" if nx * nz > 1 else "front", (i * C, back_y + t, k * C), "+y")


def size_name(nx, nz):
    a, b = nx * C, nz * C
    return f"{fmt(a)} m" if nx == nz else f"{fmt(a)}x{fmt(b)} m"


def bolts(p, nx, nz, t, colour):
    x1, z1 = cells_max(nx), cells_max(nz)
    for x in (-H + 0.04, x1 - 0.04):
        for z in (-H + 0.04, z1 - 0.04):
            p.add(box((x - 0.015, -H + t, z - 0.015), (x + 0.015, -H + t + 0.012, z + 0.015), colour, detail=True))


def panel(mat, nx, nz):
    sec, group, t, kgm2, hb, hm, arm, tool, secs, colour, material, words = MATS[mat]
    area = nx * nz * C * C
    name = f"{MAT_NAME[mat]} {size_name(nx, nz)}"
    p = Part(name, group, sec, f"{words} {size_name(nx, nz)}.", role="panel", tool=tool, material=material, color=colour)
    x0, x1, z0, z1, y0, y1 = -H, cells_max(nx), -H, cells_max(nz), -H, -H + t
    if mat == "wood":
        # Planks along Z, 0.25 m wide, in two shades.
        for i in range(nx):
            p.add(box((i * C - H, y0, z0), (i * C + H, y1, z1), "#8a6a40" if i % 2 == 0 else "#7b5e38"))
        if nx * 1 + 2 <= 16:
            for z in (z0 + 0.06, z1 - 0.06):
                p.add(box((x0 + 0.01, y1, z - 0.02), (x1 - 0.01, y1 + 0.01, z + 0.02), "#6a5030", detail=True))
    elif mat == "glass":
        f = 0.03
        p.add(box((x0, y0, z0), (x1, y1, z0 + f), "#5a6068"))
        p.add(box((x0, y0, z1 - f), (x1, y1, z1), "#5a6068"))
        p.add(box((x0, y0, z0 + f), (x0 + f, y1, z1 - f), "#5a6068"))
        p.add(box((x1 - f, y0, z0 + f), (x1, y1, z1 - f), "#5a6068"))
        p.add(box((x0 + f, y0 + 0.01, z0 + f), (x1 - f, y1 - 0.01, z1 - f)))
    elif mat == "mesh":
        f = 0.03
        p.add(box((x0, y0, z0), (x1, y1, z0 + f)))
        p.add(box((x0, y0, z1 - f), (x1, y1, z1)))
        p.add(box((x0, y0, z0 + f), (x0 + f, y1, z1 - f)))
        p.add(box((x1 - f, y0, z0 + f), (x1, y1, z1 - f)))
        # Bars along Z, every 0.125 m (0.25 m on the 2 m grate), and two cross ties drawn over them.
        step = C / 2 if nx <= 4 else C
        bars = int(round((x1 - x0) / step)) - 1
        for b in range(bars):
            x = x0 + step * (b + 1)
            p.add(box((x - 0.008, y0, z0 + f), (x + 0.008, y1, z1 - f), "#7d8288"))
        for z in (z0 + (z1 - z0) / 3, z0 + 2 * (z1 - z0) / 3):
            if len(p.boxes) < 16:
                p.add(box((x0 + f, y1 - 0.012, z - 0.006), (x1 - f, y1 + 0.004, z + 0.006), "#5f6469", detail=True))
    else:
        p.add(box((x0, y0, z0), (x1, y1, z1)))
        if mat == "tread":
            # A raised pattern: short ridges in rows (detail).
            rows = min(6, max(2, nz * 2 // 2))
            for k in range(rows):
                z = z0 + (z1 - z0) * (k + 0.5) / rows
                xa = x0 + 0.03 + (0.04 if k % 2 else 0)
                p.add(box((xa, y1, z - 0.008), (x1 - 0.03, y1 + 0.006, z + 0.008), "#b1b5b9", detail=True))
        elif mat in ("plate", "armour", "heavy"):
            bolts(p, nx, nz, t, "#3f4636" if mat != "plate" else "#6a7076")
            if mat != "plate":
                p.add(box((x0 + 0.03, y1, z0 + 0.03), (x1 - 0.03, y1 + 0.01, z1 - 0.03), "#545c47" if mat == "armour" else "#40472f", detail=True))
    panel_sockets(p, nx, nz, t)
    scrap = int(round(kgm2 * area * 0.35)) if group == ARMOUR else None
    return p.stats(kgm2 * area, hb + hm * area, arm, secs if area >= 0.5 else max(1.0, secs * 0.7), scrap)


def opening(name, mat, nx, nz, hole, desc, section="Openings", group=PANELS):
    """A plate with a hole in it: four solid boxes round the hole (x0, z0, x1, z1 in metres from the plate's corner)."""
    sec, _, t, kgm2, hb, hm, arm, tool, secs, colour, material, words = MATS[mat]
    p = Part(name, group, section, desc, role="panel", tool=tool, material=material, color=colour)
    x0, x1, z0, z1, y0, y1 = -H, cells_max(nx), -H, cells_max(nz), -H, -H + t
    hx0, hz0, hx1, hz1 = (x0 + hole[0], z0 + hole[1], x0 + hole[2], z0 + hole[3])
    p.add(box((x0, y0, z0), (x1, y1, hz0)))
    p.add(box((x0, y0, hz1), (x1, y1, z1)))
    p.add(box((x0, y0, hz0), (hx0, y1, hz1)))
    p.add(box((hx1, y0, hz0), (x1, y1, hz1)))
    lip = "#4a5058" if material == "steel" else "#363c2c"
    for (a, b) in (((hx0 - 0.02, hz0 - 0.02), (hx1 + 0.02, hz0)), ((hx0 - 0.02, hz1), (hx1 + 0.02, hz1 + 0.02)),
                   ((hx0 - 0.02, hz0), (hx0, hz1)), ((hx1, hz0), (hx1 + 0.02, hz1))):
        p.add(box((a[0], y1, a[1]), (b[0], y1 + 0.012, b[1]), lip, detail=True))
    if group == ARMOUR:
        p.add(box((x0 + 0.03, y1, z0 + 0.03), (hx0 - 0.03, y1 + 0.01, z1 - 0.03), "#545c47" if mat == "armour" else "#40472f", detail=True))
    panel_sockets(p, nx, nz, t)
    area = nx * nz * C * C - (hole[2] - hole[0]) * (hole[3] - hole[1])
    scrap = int(round(kgm2 * area * 0.35)) if group == ARMOUR else None
    return p.stats(kgm2 * area, hb + hm * area, arm, secs, scrap)


def vent(name, nx, nz):
    sec, _, t, kgm2, hb, hm, arm, tool, secs, colour, material, words = MATS["plate"]
    p = Part(name, PANELS, "Openings", "A 1 m steel plate with three louvred slots: air in, heat out (and a peek through).",
             role="panel", tool=tool, material=material, color=colour)
    x0, x1, z0, z1, y0, y1 = -H, cells_max(nx), -H, cells_max(nz), -H, -H + t
    slot, bar = 0.06, (z1 - z0 - 0.2 - 3 * 0.06) / 2
    edges = [z0 + 0.1]
    for k in range(3):
        edges.append(edges[-1] + slot)
        if k < 2:
            edges.append(edges[-1] + bar)
    # Bottom and top bands, side posts, and the bars between the slots.
    p.add(box((x0, y0, z0), (x1, y1, edges[0])))
    p.add(box((x0, y0, edges[-1]), (x1, y1, z1)))
    p.add(box((x0, y0, edges[0]), (x0 + 0.15, y1, edges[-1])))
    p.add(box((x1 - 0.15, y0, edges[0]), (x1, y1, edges[-1])))
    for k in range(2):
        a, b = edges[1 + 2 * k], edges[2 + 2 * k]
        p.add(box((x0 + 0.15, y0, a), (x1 - 0.15, y1, b)))
    for k in range(3):
        a = edges[2 * k]
        p.add(box((x0 + 0.15, y1, a - 0.005), (x1 - 0.15, y1 + 0.02, a + 0.012), "#6a7076", detail=True))
    panel_sockets(p, nx, nz, t)
    area = nx * nz * C * C - 3 * slot * (x1 - x0 - 0.3)
    return p.stats(kgm2 * area, hb + hm * area, arm, secs)


def triangle(mat, n):
    sec, group, t, kgm2, hb, hm, arm, tool, secs, colour, material, words = MATS[mat]
    s = n * C
    p = Part(f"Triangle Plate {fmt(s)} m", PANELS, "Triangles",
             f"A right-angled triangle of plate steel, {fmt(s)} m on its two short sides: noses, roofs and odd gaps.",
             role="panel", tool=tool, material=material, color=colour)
    x0, z0, x1, z1, y0, y1 = -H, -H, cells_max(n), cells_max(n), -H, -H + t
    m = min(16, 2 * n if n > 1 else 3)
    dx = (x1 - x0) / m
    for k in range(m):
        # Columns from the -x side: full length there, short at the far corner.
        length = (z1 - z0) * (m - k) / m
        p.add(box((x0 + dx * k, y0, z0), (x0 + dx * (k + 1), y1, z0 + length)))
    for i in range(n):
        for k in range(n - i):
            if i + k < n - 1 or n == 1:
                p.sock(f"back{i}{k}", (i * C, -H, k * C), "-y")
    for j in pick(n, 4):
        p.sock(f"-x{j}", (-H, -0.12, j * C), "-x")
        p.sock(f"-z{j}", (j * C, -0.12, -H), "-z")
    area = s * s / 2
    return p.stats(kgm2 * area, hb + hm * area, arm, secs)


# ---- Shapes ----------------------------------------------------------------------------------------------------------

SHAPES = "Shapes"


def stepped(p, ny, nz, nx, height, m, colour=None, hidden=True):
    """Collision for a profile in the Y-Z plane: m slabs along Z, each as tall as height(t) at its middle (t: 0 front, 1 back)."""
    y0, y1 = -H, cells_max(ny)
    z0, z1 = -H, cells_max(nz)
    dz = (z1 - z0) / m
    for k in range(m):
        h = height((k + 0.5) / m) * (y1 - y0)
        if h >= 0.01:
            p.add(box((-H, y0, z0 + dz * k), (cells_max(nx), y0 + h, z0 + dz * (k + 1)), colour, hidden=hidden))


def shape_sockets(p, nx, ny, nz, sides_row=True):
    for i in pick(nx, 4):
        for k in pick(nz, 4):
            p.sock(f"bottom{i}{k}" if nx * nz > 1 else "bottom", (i * C, -H, k * C), "-y")
    for i in pick(nx, 4):
        for j in pick(ny, 4):
            p.sock(f"back{i}{j}" if nx * ny > 1 else "back", (i * C, j * C, cells_max(nz)), "+z")
    if sides_row:
        for k in pick(nz, 4):
            if k >= nz // 2 or nz == 1:
                p.sock(f"-x{k}" if nz > 1 else "-x", (-H, 0, k * C), "-x")
                p.sock(f"+x{k}" if nz > 1 else "+x", (cells_max(nx), 0, k * C), "+x")


def wedge(ny, nz, nx, label):
    hy, hz = ny * C, nz * C
    name = f"Wedge {label} {fmt(hy)}x{fmt(hz)} m" if ny != nz else f"Wedge {label} {fmt(hy)} m"
    p = Part(name, SHAPES, "Wedges",
             f"A solid steel slope, {fmt(hy)} m high over {fmt(hz)} m ({label}): noses, ramps and roof edges. Smooth on top.",
             color="#80878f")
    m = min(12, max(4, 2 * nz))
    stepped(p, ny, nz, nx, lambda t: t, m)
    p.add(box((-H, -H, -H), (cells_max(nx), cells_max(ny), cells_max(nz)), detail=True, shape="wedge", taper=[1, 0], shift=[0, -0.5]))
    shape_sockets(p, nx, ny, nz)
    vol = nx * ny * nz * 0.5
    return p.stats(2.5 * vol, 40 + 20 * vol, 8, 2.0)


def slope_corner(n):
    s = n * C
    p = Part(f"Slope Corner {fmt(s)} m", SHAPES, "Corners",
             f"An outer corner where two {fmt(s)} m 1:1 slopes meet (a square pyramid on its side): its back face is the base.",
             color="#80878f")
    m = max(3, 2 * n) if n > 1 else 3
    z0, z1 = -H, cells_max(n)
    dz = (z1 - z0) / m
    for k in range(m):
        f = (k + 0.5) / m  # 0 at the front (the point), 1 at the back (the base)
        e = s * f
        p.add(box((-H, -H, z0 + dz * k), (-H + e, -H + e, z0 + dz * (k + 1)), hidden=True))
    p.add(box((-H, -H, -H), (cells_max(n), cells_max(n), cells_max(n)), detail=True, shape="wedge", taper=[0, 0], shift=[-0.5, -0.5]))
    for i in range(n):
        for j in range(n):
            p.sock(f"back{i}{j}" if n > 1 else "back", (i * C, j * C, z1), "+z")
    # The two triangular faces: a socket each at their back row, where they are widest.
    p.sock("bottom", (0, -H, (n - 1) * C), "-y")
    p.sock("-x", (-H, 0, (n - 1) * C), "-x")
    vol = n ** 3 / 3
    return p.stats(2.5 * vol, 40 + 20 * vol, 8, 2.0)


def inner_corner(n):
    s = n * C
    p = Part(f"Inner Corner {fmt(s)} m", SHAPES, "Corners",
             f"An inner corner where two {fmt(s)} m 1:1 slopes meet in a valley: stepped steel, flat bottom, two flat backs.",
             color="#80878f")
    # Height h(x, z) = max of the two slopes: layers of L shapes (two boxes each), from the bottom up.
    m = 4 if n == 2 else 6
    dy = s / m
    x1, z1 = cells_max(n), cells_max(n)
    for k in range(m):
        y = -H + dy * k
        cut = s * (k + 0.5) / m  # the layer covers the back strip and the +x strip
        p.add(box((-H, y, -H + cut), (x1, y + dy, z1)))
        p.add(box((-H + cut, y, -H), (x1, y + dy, -H + cut)))
    for i in range(n):
        for k in range(n):
            p.sock(f"bottom{i}{k}", (i * C, -H, k * C), "-y")
        p.sock(f"back{i}", (i * C, 0, z1), "+z")
        p.sock(f"+x{i}", (x1, 0, i * C), "+x")
    vol = n ** 3 * 2 / 3
    return p.stats(2.5 * vol, 40 + 20 * vol, 8, 2.0)


def pyramid(n):
    s = n * C
    hgt = n // 2
    p = Part(f"Pyramid Cap {fmt(s)} m", SHAPES, "Caps and trims",
             f"A steel pyramid on a {fmt(s)} m square base: caps posts, towers and noses.", color="#7d848c")
    z0, z1 = -H, cells_max(hgt)
    m = 3 if n == 2 else 4
    dz = (z1 - z0) / m
    for k in range(m):
        f = (k + 0.5) / m
        e = s * f
        c = (cells_max(n) + -H) / 2
        p.add(box((c - e / 2, c - e / 2, z0 + dz * k), (c + e / 2, c + e / 2, z0 + dz * (k + 1)), hidden=True))
    p.add(box((-H, -H, z0), (cells_max(n), cells_max(n), z1), detail=True, shape="wedge", taper=[0, 0], shift=[0, 0]))
    for i in range(n):
        for j in range(n):
            p.sock(f"base{i}{j}", (i * C, j * C, z1), "+z")
    vol = n * n * hgt / 3
    return p.stats(2.5 * vol + 0.5, 40 + 20 * vol, 8, 1.5)


def quarter_round(n, nx):
    s = n * C
    name = f"Quarter Round {fmt(s)} m" if nx == n else f"Quarter Round {fmt(s)}x{fmt(nx * C)} m"
    p = Part(name, SHAPES, "Rounded",
             f"A solid steel quarter-round, {fmt(s)} m radius{'' if nx == n else ', ' + fmt(nx * C) + ' m long'}: rounded corners and edges.",
             color="#80878f")
    m = min(12, max(4, 3 * n))
    # Height at distance d from the back: sqrt(R^2 - d^2) (the centre is the bottom back edge).
    stepped(p, n, n, nx, lambda t: math.sqrt(max(0.0, 1 - (1 - t) ** 2)), m)
    p.add(box((-H, -H, -H), (cells_max(nx), cells_max(n), cells_max(n)), detail=True, shape="arc", inner=0, angle=90, dir=135, segments=max(6, 4 * n)))
    shape_sockets(p, nx, n, n)
    vol = nx * n * n * math.pi / 4
    return p.stats(2.5 * vol, 40 + 20 * vol, 8, 2.0)


def curved_panel(n, nx):
    s = n * C
    t = 0.06
    p = Part(f"Curved Plate {fmt(s)} m", SHAPES, "Rounded",
             f"A quarter-pipe of plate steel, {fmt(s)} m radius and {fmt(nx * C)} m wide: from a wall's top round onto a roof.",
             role="panel", color="#8a9096")
    inner = 1 - t / s
    # Collision: thin boxes round the arc (centre at the bottom back edge, from the bottom front to the top back).
    m = min(12, 4 * n // 2 + 4)
    y0, z1 = -H, cells_max(n)
    for k in range(m):
        a0, a1 = (math.pi / 2) * k / m, (math.pi / 2) * (k + 1) / m
        pts = []
        for a in (a0, a1):
            for rr in (s - t, s):
                pts.append((y0 + rr * math.sin(a), z1 - rr * math.cos(a)))
        ys, zs = [q[0] for q in pts], [q[1] for q in pts]
        p.add(box((-H, max(y0, min(ys)), max(-H, min(zs))), (cells_max(nx), min(cells_max(n), max(ys)), min(z1, max(zs))), hidden=True))
    p.add(box((-H, -H, -H), (cells_max(nx), cells_max(n), cells_max(n)), detail=True, shape="arc", inner=r(inner), angle=90, dir=135, segments=max(8, 4 * n)))
    for i in pick(nx, 4):
        p.sock(f"bottom{i}" if nx > 1 else "bottom", (i * C, -H, 0), "-y")
        p.sock(f"back{i}" if nx > 1 else "back", (i * C, (n - 1) * C, z1), "+z")
    area = nx * C * s * math.pi / 2
    return p.stats(20 * area, 40 + 120 * area, 20, 2.5)


def trim(kind, n):
    L = n * C
    if kind == "round":
        p = Part(f"Round Trim {fmt(L)} m", SHAPES, "Caps and trims",
                 f"A 0.25 m quarter-round edge trim, {fmt(L)} m long: softens a box's edge.", color="#858c94")
        stepped_x = True
    else:
        p = Part(f"Chamfer Trim {fmt(L)} m", SHAPES, "Caps and trims",
                 f"A 0.25 m 45 degree edge trim, {fmt(L)} m long: bevels a box's edge.", color="#858c94")
        stepped_x = False
    # Along X (the length), profile in Y-Z.
    m = 4
    f = (lambda t: math.sqrt(max(0.0, 1 - (1 - t) ** 2))) if stepped_x else (lambda t: t)
    stepped(p, 1, 1, n, f, m)
    if stepped_x:
        p.add(box((-H, -H, -H), (cells_max(n), H, H), detail=True, shape="arc", inner=0, angle=90, dir=135, segments=6))
    else:
        p.add(box((-H, -H, -H), (cells_max(n), H, H), detail=True, shape="wedge", taper=[1, 0], shift=[0, -0.5]))
    for i in range(n):
        p.sock(f"bottom{i}" if n > 1 else "bottom", (i * C, -H, 0), "-y")
        p.sock(f"back{i}" if n > 1 else "back", (i * C, 0, H), "+z")
    p.sock("-x", (-H, 0, 0), "-x")
    p.sock("+x", (cells_max(n), 0, 0), "+x")
    vol = n * (0.785 if stepped_x else 0.5)
    return p.stats(2.5 * vol, 30 + 20 * vol, 8, 1.5)


# ---- The list --------------------------------------------------------------------------------------------------------

def fmt(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return s


def everything():
    out = []  # (folder, part)
    for n in (1, 2, 3, 4, 6, 8):
        out.append(("10-Beams/10-Square", square_beam(n)))
    for n in (2, 4, 6, 8):
        out.append(("10-Beams/20-IBeam", i_beam(n)))
    for n in (2, 4, 8):
        out.append(("10-Beams/30-Tube", tube(n, "tube")))
    for n in (2, 4, 8):
        out.append(("10-Beams/30-Tube", tube(n, "rod")))
    for n in (2, 4, 8):
        out.append(("10-Beams/40-Angle", angle_iron(n)))
    for n in (2, 4, 8):
        out.append(("10-Beams/40-Angle", channel(n)))
    for ny, nz in ((2, 2), (4, 4), (8, 8), (2, 4), (4, 8)):
        out.append(("10-Beams/50-Brace", brace(ny, nz)))
    for n in (2, 4):
        out.append(("10-Beams/50-Brace", cross_brace(n)))
    out.append(("10-Beams/60-Junction", junction("Corner Junction", [(0, 0, 0), (1, 0, 0), (0, 0, 1)],
                                                "An L of three steel cells: two beams meet at a right angle in one plane.")))
    out.append(("10-Beams/60-Junction", junction("T Junction", [(-1, 0, 0), (0, 0, 0), (1, 0, 0), (0, 0, 1)],
                                                "A T of four steel cells: a beam meets the middle of another.")))
    out.append(("10-Beams/60-Junction", junction("Cross Junction", [(0, 0, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)],
                                                "A plus of five steel cells: two beams cross in one plane.")))
    out.append(("10-Beams/60-Junction", junction("3-Way Corner", [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
                                                "Three steel arms at right angles: the corner of a box frame.")))
    out.append(("10-Beams/60-Junction", junction("6-Way Hub", [(0, 0, 0), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)],
                                                "A hub with an arm each way: six beams meet in a frame's middle.")))
    out.append(("10-Beams/70-Bracket", bracket("L")))
    out.append(("10-Beams/70-Bracket", bracket("corner")))
    for n in (1, 2):
        out.append(("10-Beams/70-Bracket", gusset(n)))
    out.append(("10-Beams/80-Cap", end_cap("beam")))
    out.append(("10-Beams/80-Cap", end_cap("tube")))

    sizes = {
        "sheet": [(1, 1), (2, 2), (2, 4), (4, 4), (4, 8), (8, 8)],
        "plate": [(1, 1), (2, 2), (2, 4), (4, 4), (4, 8), (8, 8)],
        "wood": [(2, 2), (2, 4), (4, 8), (8, 8)],
        "glass": [(2, 2), (2, 4), (4, 4), (4, 8)],
        "mesh": [(2, 2), (4, 4), (4, 8), (8, 8)],
        "tread": [(2, 2), (4, 4), (4, 8), (8, 8)],
    }
    for k, (mat, ss) in enumerate(sizes.items()):
        for nx, nz in ss:
            out.append((f"20-Panels/{k + 1}0-{MATS[mat][0].replace(' ', '')}", panel(mat, nx, nz)))
    out.append(("20-Panels/70-Openings", opening("Window Frame 1 m", "plate", 4, 4, (0.25, 0.25, 0.75, 0.75),
                                                 "A 1 m steel plate with a 0.5 m square window: fit a glass pane behind it, or leave it open.")))
    out.append(("20-Panels/70-Openings", opening("Window Frame 1x0.5 m", "plate", 4, 2, (0.15, 0.15, 0.85, 0.35),
                                                 "A 1 by 0.5 m steel plate with a wide, low window slot.")))
    out.append(("20-Panels/70-Openings", opening("Gun Port Panel 1 m", "plate", 4, 4, (0.3, 0.45, 0.7, 0.6),
                                                 "A 1 m steel plate with a firing slit 0.4 by 0.15 m: shoot out, hard to shoot in.")))
    out.append(("20-Panels/70-Openings", vent("Vent Panel 1 m", 4, 4)))
    out.append(("20-Panels/70-Openings", opening("Hatch Frame 1 m", "plate", 4, 4, (0.15, 0.15, 0.85, 0.85),
                                                 "A 1 m steel deck plate with a 0.7 m square hole: a way through a floor or a roof.")))
    out.append(("20-Panels/70-Openings", opening("Door Frame 1x2 m", "plate", 4, 8, (0.15, 0.0, 0.85, 1.8),
                                                 "A 1 by 2 m steel wall plate with a 0.7 by 1.8 m doorway in it.")))
    for n in (2, 4, 8):
        out.append(("20-Panels/80-Triangles", triangle("plate", n)))

    for nx, nz in [(2, 2), (2, 4), (4, 4), (4, 8), (8, 8)]:
        out.append(("30-Armour/10-Armour", panel("armour", nx, nz)))
    for nx, nz in [(2, 2), (4, 4), (4, 8), (8, 8)]:
        out.append(("30-Armour/20-Heavy", panel("heavy", nx, nz)))
    out.append(("30-Armour/30-Ports", opening("Armour Gun Port 1 m", "armour", 4, 4, (0.3, 0.45, 0.7, 0.6),
                                              "A 1 m armour plate with a firing slit 0.4 by 0.15 m.", "Gun ports", ARMOUR)))
    out.append(("30-Armour/30-Ports", opening("Heavy Armour Port 1 m", "heavy", 4, 4, (0.35, 0.47, 0.65, 0.58),
                                              "A 1 m heavy armour plate with a narrow firing slit 0.3 by 0.11 m.", "Gun ports", ARMOUR)))

    out.append(("40-Shapes/10-Wedges", wedge(1, 1, 1, "1:1")))
    out.append(("40-Shapes/10-Wedges", wedge(2, 2, 2, "1:1")))
    out.append(("40-Shapes/10-Wedges", wedge(4, 4, 4, "1:1")))
    out.append(("40-Shapes/10-Wedges", wedge(1, 2, 2, "1:2")))
    out.append(("40-Shapes/10-Wedges", wedge(2, 4, 4, "1:2")))
    out.append(("40-Shapes/10-Wedges", wedge(2, 1, 2, "2:1")))
    out.append(("40-Shapes/10-Wedges", wedge(4, 2, 4, "2:1")))
    for n in (2, 4):
        out.append(("40-Shapes/20-Corners", slope_corner(n)))
    for n in (2, 4):
        out.append(("40-Shapes/20-Corners", inner_corner(n)))
    out.append(("40-Shapes/30-Rounded", quarter_round(1, 1)))
    out.append(("40-Shapes/30-Rounded", quarter_round(2, 2)))
    out.append(("40-Shapes/30-Rounded", quarter_round(4, 4)))
    out.append(("40-Shapes/30-Rounded", curved_panel(4, 4)))
    out.append(("40-Shapes/30-Rounded", curved_panel(8, 4)))
    out.append(("40-Shapes/40-Caps", pyramid(2)))
    out.append(("40-Shapes/40-Caps", pyramid(4)))
    for n in (4, 8):
        out.append(("40-Shapes/40-Caps", trim("round", n)))
    for n in (4, 8):
        out.append(("40-Shapes/40-Caps", trim("chamfer", n)))
    return out


def file_name(name):
    s = name.replace(".", "p").replace(":", "to").replace("-", "")
    return "".join(ch for ch in s if ch.isalnum()) + ".json"


def main():
    check = "--check" in sys.argv
    parts = everything()
    names = set()
    want = {}
    for folder, p in parts:
        d = p.json()
        assert d["name"] not in names, d["name"]
        names.add(d["name"])
        path = os.path.join(OUT, folder, file_name(d["name"]))
        assert path not in want, path
        want[path] = json.dumps(d, indent=2) + "\n"
    have = {}
    for root, _, files in os.walk(OUT):
        for f in files:
            if f.endswith(".json"):
                path = os.path.join(root, f)
                with open(path, encoding="utf-8") as fh:
                    have[path] = fh.read()
    stale = sorted(set(have) - set(want))
    changed = sorted(p for p in want if have.get(p) != want[p])
    if check:
        for p in stale:
            print(f"stale: {p}")
        for p in changed:
            print(f"out of date: {p}")
        print(f"{len(want)} structural parts; {len(changed)} out of date, {len(stale)} stale")
        return 1 if stale or changed else 0
    for p in stale:
        os.remove(p)
    for p in changed:
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(want[p])
    print(f"{len(want)} structural parts written ({len(changed)} changed, {len(stale)} removed) in {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
