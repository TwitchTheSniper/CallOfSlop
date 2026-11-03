#!/usr/bin/env python3
"""Writes the big structural parts (Constructs/Parts/Capital): the same kit at the Medium, Large and Capital size classes.

What it is for (docs/sandbox/CONSTRUCTS.md, "Size classes"): a mothership on the scale of a carrier (300 m, 100,000 t)
or a traction city: a heavy chassis on track units, overhanging storeys with terraces and balconies, building walls with
windows and arches, riveted armour, domes, spires, stacks and trusses; and a ship's hull, bows and keel. A small vehicle's
parts bolt on through the adapters.

Run it from the repo root: python3 -I tools/constructs/capital_parts.py (it rewrites the folder; --check only compares).
"""

import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, MEDIUM, LARGE, CAPITAL, Part, box, block_sockets, fmt, pick, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Capital")
BIG = (MEDIUM, LARGE, CAPITAL)

STEEL, DARK, RUST, RUST2, IRON = "#5f666e", "#3f444a", "#7a4a32", "#8c5b3c", "#6a5f55"
BRICK, PLASTER, COPPER, SOOT, BRASS = "#8a5a44", "#b79b7a", "#9c5a38", "#2f2b28", "#a58a4e"

FRAMES, PLATES, ARMOUR, HULL, CHASSIS = "Frames", "Plates", "Armour", "Hull", "Chassis"
WALLS, DECKS, ROOFS, STACKS, STAIRS, SHAPES, ADAPTERS = "Walls", "Decks", "Roofs", "Stacks", "Stairs", "Shapes", "Adapters"


def part(name, c, section, desc, role="frame", color=STEEL, material="steel"):
    """A part of class c; the name carries the class, so every name is unique across the classes."""
    p = Part(f"{c.name} {name}", c, section, desc, role=role, color=color, material=material)
    p.d["centreSockets"] = False  # (a big part's sockets are its class's lattice, nothing else)
    return p


def steel(p, area, k=1.0, armour=None, seconds=None):
    """Mass and health from the plate area in it (m2), k times its class's plate."""
    c = p.cls
    return p.stats(area * c.kg_m2 * k, min(16_000_000, 40 + area * c.health_m2 * k), c.armour if armour is None else armour, seconds)


def sides(p, nx, ny, nz, limit=3, skip=()):
    """The block's sockets, at most limit x limit a face (the 64 a part can have go quickly)."""
    lim = {d: (limit, limit) for d in ("+x", "-x", "+y", "-y", "+z", "-z")}
    block_sockets(p, nx, ny, nz, limit=lim, skip=skip)


def rivets(p, x0, x1, z0, z1, y, colour, n=6):
    """Rows of raised rivet heads along a plate's edges, as strips (detail): a rough riveted look for a few boxes."""
    s = (z1 - z0) / 40
    h = 0.012 * p.cls.x ** 0.5
    for z in (z0 + s * 2, z1 - s * 2):
        p.add(box((x0 + s, y, z - s / 2), (x1 - s, y + h, z + s / 2), colour, detail=True))
    s = (x1 - x0) / 40
    for x in (x0 + s * 2, x1 - s * 2):
        p.add(box((x - s / 2, y, z0 + s * 3), (x + s / 2, y + h, z1 - s * 3), colour, detail=True))


# ---- Frames -------------------------------------------------------------------------------------------------------


def node(c):
    H = c.half
    p = part("Frame Node", c, FRAMES, f"A {fmt(c.cell)} m welded box: the frame's node, sockets on all six faces.", color=DARK)
    p.add(box((-H, -H, -H), (H, H, H)))
    sides(p, 1, 1, 1)
    return steel(p, 6 * c.cell ** 2 * 0.5)


def girder(c, n):
    H, L = c.half, n * c.cell
    p = part(f"Girder {fmt(L)} m", c, FRAMES, f"A riveted I-girder, {fmt(c.cell)} m deep and {fmt(L)} m long, with sockets every {fmt(c.cell)} m.", color=STEEL)
    z0, z1 = c.lo(), c.hi(n)
    f, w = c.cell * 0.12, c.cell * 0.05
    p.add(box((-H, H - f, z0), (H, H, z1)))
    p.add(box((-H, -H, z0), (H, -H + f, z1)))
    p.add(box((-w, -H + f, z0), (w, H - f, z1), DARK))
    sides(p, 1, 1, n)
    return steel(p, L * c.cell * 2.5 * 0.5)


def box_girder(c, n):
    H, L = c.half, n * c.cell
    p = part(f"Box Frame {fmt(L)} m", c, FRAMES, f"A hollow box section, {fmt(c.cell)} m square and {fmt(L)} m long: you can walk through it.", color=IRON)
    z0, z1, t = c.lo(), c.hi(n), max(c.plate, c.cell * 0.05)
    p.add(box((-H, -H, z0), (H, -H + t, z1)))
    p.add(box((-H, H - t, z0), (H, H, z1)))
    p.add(box((-H, -H + t, z0), (-H + t, H - t, z1)))
    p.add(box((H - t, -H + t, z0), (H, H - t, z1)))
    sides(p, 1, 1, n)
    return steel(p, L * c.cell * 4 * 0.6)


def truss(c, n):
    H, L = c.half, n * c.cell
    p = part(f"Truss {fmt(L)} m", c, FRAMES, f"An open steel truss, {fmt(c.cell)} m square and {fmt(L)} m long: four chords and lacing. It holds storeys up for little weight.", color=STEEL)
    z0, z1, b = c.lo(), c.hi(n), c.cell * 0.1
    for sy in (-1, 1):
        for sx in (-1, 1):
            x0, x1 = (-H, -H + b) if sx < 0 else (H - b, H)
            y0, y1 = (-H, -H + b) if sy < 0 else (H - b, H)
            p.add(box((x0, y0, z0), (x1, y1, z1)))
    # The lacing on the two side faces, a zigzag of a few steps a cell (drawn only: the chords are what collides).
    m = min(4, 24 // (2 * n))
    t = c.cell * 0.035
    for x in (-H + t, H - t):
        for k in range(n):
            zc = k * c.cell - H
            for s in range(m):
                f = (s + 0.5) / m
                y = -H + (c.cell - b) * (f if k % 2 == 0 else 1 - f)
                p.add(box((x - t, y, zc + c.cell * s / m), (x + t, y + b, zc + c.cell * (s + 1) / m), DARK, detail=True))
    sides(p, 1, 1, n)
    return steel(p, L * c.cell * 0.9)


def brace(c, n):
    H, s = c.half, n * c.cell
    p = part(f"Strut {fmt(s)} m", c, FRAMES, f"A 45 degree strut across a {fmt(s)} m square: it props an overhanging storey off the frame below.", color=STEEL)
    m = min(16, 6 + 2 * n)
    y0, z0, d, t = c.lo(), c.lo(), s / m, c.cell * 0.12
    for k in range(m):
        y = y0 + d * k
        p.add(box((-t, max(y0, y - d * 0.4), z0 + d * k), (t, min(c.hi(n), y + d * 1.4), z0 + d * (k + 1))))
    p.sock("bottom", (0, c.lo(), 0), "-y")
    p.sock("front", (0, 0, c.lo()), "-z")
    p.sock("top", (0, c.hi(n), (n - 1) * c.cell), "+y")
    p.sock("back", (0, (n - 1) * c.cell, c.hi(n)), "+z")
    return steel(p, s * c.cell * 0.6)


def buttress(c, n):
    H, s = c.half, n * c.cell
    p = part(f"Buttress {fmt(s)} m", c, FRAMES, f"A stepped buttress, {fmt(s)} m tall and as deep at its foot: it carries a tall wall's load down and out.", color=BRICK, material="stone")
    m = 6
    y0, y1, z0, z1 = c.lo(), c.hi(n), c.lo(), c.hi(n)
    dz = (z1 - z0) / m
    for k in range(m):
        p.add(box((-H, y0, z0 + dz * k), (H, y0 + (y1 - y0) * (k + 1) / m, z0 + dz * (k + 1)), hidden=True))
    p.add(box((-H, y0, z0), (H, y1, z1), detail=True, shape="wedge", taper=[1, 0], shift=[0, -0.5]))
    for k in pick(n, 3):
        p.sock(f"bottom{k}", (0, c.lo(), k * c.cell), "-y")
        p.sock(f"back{k}", (0, k * c.cell, c.hi(n)), "+z")
    return steel(p, s * s * 1.2, seconds=c.seconds * 1.2)


def column(c, n):
    H, L = c.half, n * c.cell
    p = part(f"Column {fmt(L)} m", c, FRAMES, f"A round riveted column, {fmt(c.cell)} m across and {fmt(L)} m tall (it stands along its ends' sockets).", color=IRON)
    z0, z1 = c.lo(), c.hi(n)
    p.add(box((-H, -H, z0), (H, H, z1), hidden=True))
    p.add(box((-H, -H, z0), (H, H, z1), detail=True, shape="cylinder", segments=20))
    for z in (z0, z1 - c.cell * 0.08):
        p.add(box((-H, -H, z), (H, H, z + c.cell * 0.08), DARK, detail=True, shape="cylinder", segments=20))
    sides(p, 1, 1, n)
    return steel(p, math.pi * c.cell * L * 0.6)


# ---- Plates, armour ------------------------------------------------------------------------------------------------

PLATE_KINDS = {
    "hull": ("Hull Plate", PLATES, 1.0, 1.0, STEEL, "steel", "A plain hull plate"),
    "rust": ("Riveted Plate", PLATES, 1.0, 1.1, RUST, "steel", "A rough rusted plate with rows of rivets"),
    "armour": ("Armour Plate", ARMOUR, 1.6, 1.6, IRON, "armour", "A thick riveted armour plate"),
    "belt": ("Armour Belt", ARMOUR, 2.6, 2.6, DARK, "armour", "A heavy armour belt, the thickest plate there is"),
    "brick": ("Brick Wall", WALLS, 1.2, 0.8, BRICK, "stone", "A rendered brick wall"),
    "plaster": ("Plaster Wall", WALLS, 1.0, 0.7, PLASTER, "stone", "A plastered wall"),
}


def plate(kind, c, nx, nz):
    label, section, tk, mk, colour, material, what = PLATE_KINDS[kind]
    H = c.half
    w, l = nx * c.cell, nz * c.cell
    p = part(f"{label} {fmt(w)}x{fmt(l)} m", c, section, f"{what}, {fmt(w)} by {fmt(l)} m ({fmt(c.plate * tk)} m thick), on the {fmt(c.cell)} m lattice.", role="panel", color=colour, material=material)
    t = c.plate * tk
    x0, x1, z0, z1, y0 = c.lo(), c.hi(nx), c.lo(), c.hi(nz), -H
    p.add(box((x0, y0, z0), (x1, y0 + t, z1)))
    if kind in ("rust", "armour", "belt"):
        rivets(p, x0, x1, z0, z1, y0 + t, DARK if kind == "rust" else STEEL)
    for i in pick(nx, 3):
        for k in pick(nz, 3):
            p.sock(f"back{i}.{k}", (i * c.cell, y0, k * c.cell), "-y")
            p.sock(f"front{i}.{k}", (i * c.cell, y0 + t, k * c.cell), "+y")
    ey = y0 + t / 2
    for d, axis, along, pos in (("-x", 0, nz, x0), ("+x", 0, nz, x1), ("-z", 2, nx, z0), ("+z", 2, nx, z1)):
        for j in pick(along, 3):
            at = [0.0, ey, 0.0]
            at[axis] = pos
            at[2 - axis] = j * c.cell
            p.sock(f"{d}{j}", at, d)
    arm = {"hull": c.armour, "rust": c.armour + 4, "armour": c.armour * 2, "belt": c.armour * 3 + 10, "brick": c.armour // 2, "plaster": c.armour // 3}[kind]
    return steel(p, w * l, mk, armour=arm)


def deck(c, nx, nz, kind):
    H = c.half
    w, l = nx * c.cell, nz * c.cell
    label, colour, what = {"deck": ("Deck", IRON, "A steel deck plate"), "timber": ("Timber Deck", "#7d5a3a", "A planked deck on steel"),
                           "grate": ("Grate Deck", DARK, "An open grating")}[kind]
    p = part(f"{label} {fmt(w)}x{fmt(l)} m", c, DECKS, f"{what}, {fmt(w)} by {fmt(l)} m: walked on, driven on, landed on.", role="panel", color=colour, material="wood" if kind == "timber" else "steel")
    t = c.plate * 1.2
    x0, x1, z0, z1, y0 = c.lo(), c.hi(nx), c.lo(), c.hi(nz), -H
    p.add(box((x0, y0, z0), (x1, y0 + t, z1)))
    # Stiffeners under it (drawn only): a deck reads as a deck from below too.
    for i in range(1, nx):
        x = c.lo() + i * c.cell
        p.add(box((x - c.cell * 0.03, y0 - c.cell * 0.06, z0), (x + c.cell * 0.03, y0, z1), DARK, detail=True))
    for i in pick(nx, 3):
        for k in pick(nz, 3):
            p.sock(f"back{i}.{k}", (i * c.cell, y0, k * c.cell), "-y")
            p.sock(f"front{i}.{k}", (i * c.cell, y0 + t, k * c.cell), "+y")
    ey = y0 + t / 2
    for d, axis, along, pos in (("-x", 0, nz, x0), ("+x", 0, nz, x1), ("-z", 2, nx, z0), ("+z", 2, nx, z1)):
        for j in pick(along, 3):
            at = [0.0, ey, 0.0]
            at[axis] = pos
            at[2 - axis] = j * c.cell
            p.sock(f"{d}{j}", at, d)
    p.d["walk"] = "floor"
    return steel(p, w * l, 0.9)


# ---- Walls: windows, arches ----------------------------------------------------------------------------------------


def window_wall(c, nx, ny, colour=BRICK):
    H = c.half
    w, h = nx * c.cell, ny * c.cell
    p = part(f"Window Wall {fmt(w)}x{fmt(h)} m", c, WALLS, f"A building wall {fmt(w)} m long and {fmt(h)} m tall with {nx * ny} real window openings: rounds and light go through them.", role="panel", color=colour, material="stone")
    t = c.plate * 1.2
    z0 = -H
    x0, x1, y0, y1 = c.lo(), c.hi(nx), c.lo(), c.hi(ny)
    op = c.cell * 0.5  # the opening
    pier = (c.cell - op) / 2
    # Lintels and sills right across, piers between the openings.
    for j in range(ny):
        yc = j * c.cell
        p.add(box((x0, yc - H, z0), (x1, yc - op / 2, z0 + t)))
        for i in range(nx + 1):
            xa = x0 if i == 0 else (i - 1) * c.cell + op / 2
            xb = (i * c.cell - op / 2) if i < nx else x1
            p.add(box((xa, yc - op / 2, z0), (xb, yc + op / 2, z0 + t)))
    p.add(box((x0, (ny - 1) * c.cell + op / 2, z0), (x1, y1, z0 + t)))
    for i in pick(nx, 3):
        p.sock(f"bottom{i}", (i * c.cell, y0, z0 + t / 2), "-y")
        p.sock(f"top{i}", (i * c.cell, y1, z0 + t / 2), "+y")
    for j in pick(ny, 3):
        p.sock(f"left{j}", (x0, j * c.cell, z0 + t / 2), "-x")
        p.sock(f"right{j}", (x1, j * c.cell, z0 + t / 2), "+x")
        p.sock(f"back{j}", (0, j * c.cell, z0), "-z")
        p.sock(f"front{j}", (0, j * c.cell, z0 + t), "+z")
    assert pier > 0
    return steel(p, w * h * 0.75, 0.8, armour=c.armour // 2)


def arch_wall(c, n, colour=BRICK):
    """n pointed-ish arches in a row, two cells tall: an arcade. The arc shape turns about X, so the wall's face is Y-Z and it is thick along X."""
    H = c.half
    span, tall = c.cell, 2 * c.cell
    p = part(f"Arcade {fmt(n * span)}x{fmt(tall)} m", c, WALLS, f"{n} arch{'es' if n > 1 else ''} in a row, {fmt(n * span)} m long and {fmt(tall)} m tall: a gateway, a gallery, a grand front.", role="panel", color=colour, material="stone")
    t = c.plate * 2
    x0, x1 = -H, -H + t
    y0, y1 = c.lo(), c.hi(2)
    pier = c.cell * 0.12
    rise = c.cell * 0.5  # the arch's height
    spring = y1 - rise - c.cell * 0.1  # where the curve starts
    z0 = c.lo()
    for k in range(n + 1):
        zc = z0 + k * span
        a = max(z0, zc - pier)
        b = min(c.hi(n), zc + pier)
        p.add(box((x0, y0, a), (x1, spring, b)))
    p.add(box((x0, y1 - c.cell * 0.1, z0), (x1, y1, c.hi(n))))
    m = 6
    for k in range(n):
        za = z0 + k * span + pier
        zb = z0 + (k + 1) * span - pier
        for s in range(m):
            # The spandrel over the opening, in columns that follow the ellipse.
            u = (s + 0.5) / m * 2 - 1
            top = spring + rise * math.sqrt(max(0.0, 1 - u * u))
            p.add(box((x0, top, za + (zb - za) * s / m), (x1, y1 - c.cell * 0.1, za + (zb - za) * (s + 1) / m)))
        ring = c.cell * 0.05
        p.add(box((x0 - 0.01 * c.x, spring - ring, za - ring), (x1 + 0.01 * c.x, spring + rise + ring, zb + ring), DARK, detail=True, shape="arc", inner=0.86, angle=180, dir=180, segments=16))
    for k in range(n):
        p.sock(f"bottom{k}", (x0 + t / 2, y0, k * c.cell), "-y")
        p.sock(f"top{k}", (x0 + t / 2, y1, k * c.cell), "+y")
    p.sock("left", (x0 + t / 2, 0, z0), "-z")
    p.sock("right", (x0 + t / 2, 0, c.hi(n)), "+z")
    return steel(p, n * span * tall * 0.6, 0.8, armour=c.armour // 2)


# ---- Decks that hang out: balconies, terraces, corbels ---------------------------------------------------------------


def railing(p, c, x0, x1, z, y, colour=DARK):
    """A rail along x at z: two bars and posts (drawn: thin things are cheaper not to collide with, a hidden kerb stops you)."""
    h, b = 1.1, 0.05
    p.add(box((x0, y, z - 0.1), (x1, y + 0.3, z + 0.1), colour, hidden=True))
    p.add(box((x0, y + h, z - b), (x1, y + h + 2 * b, z + b), colour, detail=True))
    p.add(box((x0, y + h * 0.55, z - b / 2), (x1, y + h * 0.55 + b, z + b / 2), colour, detail=True))
    n = max(2, int((x1 - x0) / 2) + 1)
    for k in range(min(n, 7)):
        x = x0 + (x1 - x0) * (k + 0.5) / min(n, 7)
        p.add(box((x - b, y, z - b), (x + b, y + h, z + b), colour, detail=True))


def balcony(c, n):
    H = c.half
    w, d = n * c.cell, c.cell
    p = part(f"Balcony {fmt(w)} m", c, DECKS, f"A railed balcony, {fmt(w)} m long and {fmt(d)} m deep, that hangs off a wall on its back sockets.", role="panel", color="#7d5a3a")
    t = c.plate * 1.2
    x0, x1, z0, z1, y0 = c.lo(), c.hi(n), c.lo(), c.hi(1), -H
    p.add(box((x0, y0, z0), (x1, y0 + t, z1)))
    railing(p, c, x0, x1, z0 + 0.06, y0 + t)
    # Brackets under it.
    for k in pick(n, 3):
        x = k * c.cell
        p.add(box((x - c.cell * 0.05, y0 - c.cell * 0.2, z1 - c.cell * 0.5), (x + c.cell * 0.05, y0, z1), DARK, detail=True, shape="wedge", taper=[1, 0], shift=[0, 0.5]))
    for k in pick(n, 3):
        p.sock(f"back{k}", (k * c.cell, y0 + t / 2, z1), "+z")
        p.sock(f"bottom{k}", (k * c.cell, y0, 0), "-y")
    p.sock("left", (x0, y0 + t / 2, 0), "-x")
    p.sock("right", (x1, y0 + t / 2, 0), "+x")
    p.d["walk"] = "floor"
    return steel(p, w * d, 0.7)


def terrace(c, nx, nz):
    H = c.half
    w, l = nx * c.cell, nz * c.cell
    p = part(f"Terrace {fmt(w)}x{fmt(l)} m", c, DECKS, f"A flat roof terrace, {fmt(w)} by {fmt(l)} m, with a stone parapet on three sides: a storey's top and the next one's garden.", role="panel", color=PLASTER, material="stone")
    t, ph, pt = c.plate * 1.2, 1.1, max(0.2, c.plate)
    x0, x1, z0, z1, y0 = c.lo(), c.hi(nx), c.lo(), c.hi(nz), -H
    p.add(box((x0, y0, z0), (x1, y0 + t, z1)))
    p.add(box((x0, y0 + t, z0), (x1, y0 + t + ph, z0 + pt), BRICK))
    p.add(box((x0, y0 + t, z0 + pt), (x0 + pt, y0 + t + ph, z1), BRICK))
    p.add(box((x1 - pt, y0 + t, z0 + pt), (x1, y0 + t + ph, z1), BRICK))
    for i in pick(nx, 3):
        for k in pick(nz, 3):
            p.sock(f"back{i}.{k}", (i * c.cell, y0, k * c.cell), "-y")
    for i in pick(nx, 3):
        p.sock(f"+z{i}", (i * c.cell, y0 + t / 2, z1), "+z")
    p.d["walk"] = "floor"
    return steel(p, w * l, 0.9)


def corbel(c, n):
    H = c.half
    w = n * c.cell
    p = part(f"Corbel {fmt(w)} m", c, FRAMES, f"A stepped bracket, {fmt(w)} m long, that lets a storey overhang the one below by a cell.", color=BRICK, material="stone")
    m = 5
    x0, x1, y0, y1, z0, z1 = c.lo(), c.hi(n), c.lo(), c.hi(1), c.lo(), c.hi(1)
    dy = (y1 - y0) / m
    for k in range(m):
        p.add(box((x0, y0 + dy * k, z0 + (z1 - z0) * (1 - (k + 1) / m)), (x1, y0 + dy * (k + 1), z1), hidden=True))
    p.add(box((x0, y0, z0), (x1, y1, z1), detail=True, shape="wedge", taper=[1, 0], shift=[0, 0.5]))
    for k in pick(n, 3):
        p.sock(f"back{k}", (k * c.cell, 0, z1), "+z")
        p.sock(f"top{k}", (k * c.cell, y1, 0), "+y")
    return steel(p, w * c.cell * 1.2)


# ---- Hull: sections, bows, keels -----------------------------------------------------------------------------------


def hull_section(c, nx, ny, nz):
    w, h, l = nx * c.cell, ny * c.cell, nz * c.cell
    p = part(f"Hull Section {fmt(w)}x{fmt(h)}x{fmt(l)} m", c, HULL, f"A hollow hull block, {fmt(w)} m wide, {fmt(h)} m high and {fmt(l)} m long: a floor, a ceiling and two sides, open at both ends.", color=STEEL)
    t = c.plate * 1.5
    x0, x1, y0, y1, z0, z1 = c.lo(), c.hi(nx), c.lo(), c.hi(ny), c.lo(), c.hi(nz)
    p.add(box((x0, y0, z0), (x1, y0 + t, z1)))
    p.add(box((x0, y1 - t, z0), (x1, y1, z1)))
    p.add(box((x0, y0 + t, z0), (x0 + t, y1 - t, z1)))
    p.add(box((x1 - t, y0 + t, z0), (x1, y1 - t, z1)))
    # Ring frames (drawn): the ribs that show it is a ship from inside.
    r = c.cell * 0.04
    for k in range(1, nz):
        z = c.lo() + k * c.cell
        p.add(box((x0 + t, y0 + t, z - r), (x1 - t, y0 + t + r * 2, z + r), DARK, detail=True))
        p.add(box((x0 + t, y1 - t - r * 2, z - r), (x1 - t, y1 - t, z + r), DARK, detail=True))
    sides(p, nx, ny, nz, limit=2)
    return steel(p, 2 * (w + h) * l, 1.5)


def bow(c, nx, ny, nz):
    w, h, l = nx * c.cell, ny * c.cell, nz * c.cell
    p = part(f"Bow {fmt(w)}x{fmt(h)}x{fmt(l)} m", c, HULL, f"A hull's nose, {fmt(w)} m wide and {fmt(h)} m high, closing to a stem over {fmt(l)} m: fits on a hull section's end.", color=STEEL)
    x0, x1, y0, y1, z0, z1 = c.lo(), c.hi(nx), c.lo(), c.hi(ny), c.lo(), c.hi(nz)
    m = 8
    dz = (z1 - z0) / m
    mid = (x0 + x1) / 2
    for k in range(m):
        half = (x1 - x0) / 2 * math.sqrt((k + 1 - 0.25) / m)
        p.add(box((mid - half, y0, z0 + dz * k), (mid + half, y1, z0 + dz * (k + 1)), hidden=True))
    p.add(box((x0, y0, z0), (x1, y1, z1), detail=True, shape="wedge", taper=[0.08, 1], shift=[0, 0]))
    for i in pick(nx, 3):
        for j in pick(ny, 3):
            p.sock(f"back{i}.{j}", (i * c.cell, j * c.cell, z1), "+z")
    p.sock("top", (mid, y1, (nz - 1) * c.cell), "+y") if nx % 2 else None
    return steel(p, 2 * (w + h) * l * 0.6, 1.5)


def keel(c, n):
    H, L = c.half, n * c.cell
    p = part(f"Keel {fmt(L)} m", c, HULL, f"A deep keel beam, {fmt(L)} m long: the spine under a hull or a chassis.", color=DARK)
    z0, z1 = c.lo(), c.hi(n)
    p.add(box((-H, -H, z0), (H, H, z1), hidden=True))
    p.add(box((-H, -H, z0), (H, H, z1), detail=True, shape="bevel", radius=0.2, edges="long"))
    sides(p, 1, 1, n, limit=4)
    return steel(p, 4 * L * c.cell * 0.7, 1.5)


# ---- The undercarriage -----------------------------------------------------------------------------------------------


def chassis_deck(c, nx, nz):
    w, l = nx * c.cell, nz * c.cell
    p = part(f"Chassis Deck {fmt(w)}x{fmt(l)} m", c, CHASSIS, f"A heavy grillage, {fmt(w)} by {fmt(l)} m and {fmt(c.cell)} m deep: deep beams between two plates, made to carry a town and spread it onto track units.", color=DARK)
    t = c.plate * 2
    x0, x1, y0, y1, z0, z1 = c.lo(), c.hi(nx), c.lo(), c.hi(1), c.lo(), c.hi(nz)
    p.add(box((x0, y0, z0), (x1, y0 + t, z1)))
    p.add(box((x0, y1 - t, z0), (x1, y1, z1)))
    b = c.cell * 0.08
    for i in pick(nx + 1, 4):
        x = c.lo() + i * c.cell
        a, e = max(x0, x - b), min(x1, x + b)
        p.add(box((a, y0 + t, z0), (e, y1 - t, z1), IRON))
    for k in pick(nz + 1, 2):
        z = c.lo() + k * c.cell
        p.add(box((x0 + b * 2, y0 + t, max(z0, z - b)), (x1 - b * 2, y1 - t, min(z1, z + b)), IRON))
    for i in pick(nx, 4):
        for k in pick(nz, 4):
            p.sock(f"under{i}.{k}", (i * c.cell, y0, k * c.cell), "-y")
            p.sock(f"top{i}.{k}", (i * c.cell, y1, k * c.cell), "+y")
    for i in pick(nz, 3):
        p.sock(f"-x{i}", (x0, 0, i * c.cell), "-x")
        p.sock(f"+x{i}", (x1, 0, i * c.cell), "+x")
    for i in pick(nx, 3):
        p.sock(f"-z{i}", (i * c.cell, 0, z0), "-z")
        p.sock(f"+z{i}", (i * c.cell, 0, z1), "+z")
    return steel(p, w * l * 2 + 2 * (w + l) * c.cell, 2.5, armour=c.armour * 2)


def spreader(c, n):
    H, L = c.half, n * c.cell
    p = part(f"Load Spreader {fmt(L)} m", c, CHASSIS, f"A tapered bolster, {fmt(L)} m long: it gathers a column's load and spreads it along a chassis or over several tracks.", color=DARK)
    z0, z1 = c.lo(), c.hi(n)
    p.add(box((-H, -H, z0), (H, -H + c.cell * 0.2, z1)))
    mid = (z0 + z1) / 2
    m = 4
    for k in range(m):
        half = (z1 - z0) / 2 * (1 - k * 0.2)
        y = -H + c.cell * 0.2 + (c.cell * 0.8) * k / m
        p.add(box((-H * 0.8, y, mid - half), (H * 0.8, y + c.cell * 0.8 / m, mid + half), IRON))
    for k in pick(n, 4):
        p.sock(f"under{k}", (0, -H, k * c.cell), "-y")
    if n % 2:
        p.sock("top", (0, H, mid), "+y")
    return steel(p, L * c.cell * 1.5, 2.0)


# ---- Roofs -----------------------------------------------------------------------------------------------------------


def revolve(p, c, n, profile, m, colour, h):
    """A round thing h high on a base n cells across. A shape's taper narrows its front (-Z), so the thing rises towards -Z from
    its base on +Z (cell 0's high face): turned over, the base's sockets face down. m slabs following radius(t), t 0 at the base;
    stepped hidden boxes round them are what collides."""
    cx = (c.lo() + c.hi(n)) / 2
    R = n * c.cell / 2
    z1 = c.half
    dz = h / m
    for k in range(m):
        r0, r1 = R * profile(k / m), R * profile((k + 1) / m)
        z = z1 - dz * (k + 1)
        solid = max(0.02, (r0 + r1) / 2 * 0.92)
        p.add(box((cx - solid, cx - solid, z), (cx + solid, cx + solid, z + dz), hidden=True))
        rd = max(r0, r1)
        if r1 <= r0 and rd > 0.01:
            t = round(max(0.01, r1 / rd), 4)
            p.add(box((cx - rd, cx - rd, z), (cx + rd, cx + rd, z + dz), colour, detail=True, shape="cylinder", segments=20, taper=[t, t]))
        elif rd > 0.01:
            mean = (r0 + r1) / 2
            p.add(box((cx - mean, cx - mean, z), (cx + mean, cx + mean, z + dz), colour, detail=True, shape="cylinder", segments=20))


def base_sockets(p, c, n):
    for i in pick(n, 3):
        for j in pick(n, 3):
            p.sock(f"base{i}.{j}", (i * c.cell, j * c.cell, c.half), "+z")


def dome(c, n):
    s = n * c.cell
    p = part(f"Dome {fmt(s)} m", c, ROOFS, f"A copper dome, {fmt(s)} m across: the town's cathedral, or its sealed reactor.", color=COPPER)
    revolve(p, c, n, lambda t: math.sqrt(max(0.0, 1 - t * t)), 7, COPPER, s / 2)
    base_sockets(p, c, n)
    return steel(p, 2 * math.pi * (s / 2) ** 2, 0.6, armour=c.armour // 2)


def onion(c, n):
    s = n * c.cell
    p = part(f"Onion Dome {fmt(s)} m", c, ROOFS, f"A bulging onion dome, {fmt(s)} m across, drawn to a point.", color=COPPER)
    revolve(p, c, n, lambda t: max(0.02, (0.8 + 0.5 * math.sin(math.pi * min(1.0, t * 1.6))) * (1 - t) ** 0.8 * 0.77), 8, COPPER, s * 0.95)
    base_sockets(p, c, n)
    return steel(p, 2 * math.pi * (s / 2) ** 2, 0.6, armour=c.armour // 2)


def spire(c, n, tall):
    s = n * c.cell
    p = part(f"Spire {fmt(s)}x{fmt(tall * c.cell)} m", c, ROOFS, f"A tall spire, {fmt(s)} m across its foot and {fmt(tall * c.cell)} m high.", color=DARK)
    revolve(p, c, n, lambda t: max(0.015, 1 - t), 6, DARK, tall * c.cell)
    base_sockets(p, c, n)
    return steel(p, math.pi * s * tall * c.cell * 0.5, 0.4, armour=c.armour // 2)


def pyramid_roof(c, n):
    s = n * c.cell
    p = part(f"Hip Roof {fmt(s)} m", c, ROOFS, f"A four-sided roof, {fmt(s)} m square, that rises to a point.", color=RUST)
    h = s / 2
    z1 = c.half
    cx = (c.lo() + c.hi(n)) / 2
    m = 5
    for k in range(m):
        e = s * (1 - (k + 0.3) / m) / 2
        p.add(box((cx - e, cx - e, z1 - h * (k + 1) / m), (cx + e, cx + e, z1 - h * k / m), hidden=True))
    p.add(box((c.lo(), c.lo(), z1 - h), (c.hi(n), c.hi(n), z1), detail=True, shape="wedge", taper=[0, 0], shift=[0, 0]))
    base_sockets(p, c, n)
    return steel(p, s * s * 1.2, 0.6, armour=c.armour // 2)


def gable_roof(c, nx, nz):
    """The ridge runs along X, nx cells long; the span is nz cells of Y. It rises towards -Z from its base on +Z (a wedge's narrow front)."""
    w, l = nz * c.cell, nx * c.cell
    p = part(f"Gable Roof {fmt(w)}x{fmt(l)} m", c, ROOFS, f"A pitched roof, {fmt(w)} m wide and {fmt(l)} m long, with a ridge along its length.", color=RUST)
    x0, x1, y0, y1, z1 = c.lo(), c.hi(nx), c.lo(), c.hi(nz), c.half
    h = w / 2
    mid = (y0 + y1) / 2
    m = 6
    for k in range(m):
        e = w * (1 - (k + 0.3) / m) / 2
        p.add(box((x0, mid - e, z1 - h * (k + 1) / m), (x1, mid + e, z1 - h * k / m), hidden=True))
    p.add(box((x0, y0, z1 - h), (x1, y1, z1), detail=True, shape="wedge", taper=[1, 0], shift=[0, 0]))
    for i in pick(nx, 3):
        for j in pick(nz, 3):
            p.sock(f"base{i}.{j}", (i * c.cell, j * c.cell, z1), "+z")
    return steel(p, w * l * 1.3, 0.6, armour=c.armour // 2)


def barrel_roof(c, nx, nz):
    w, l = nx * c.cell, nz * c.cell
    p = part(f"Barrel Roof {fmt(w)}x{fmt(l)} m", c, ROOFS, f"A curved roof, {fmt(w)} m across and {fmt(l)} m long: a hangar's vault, a station's shed.", color=COPPER)
    x0, x1, y0, z0, z1 = c.lo(), c.hi(nl := nz), c.lo(), c.lo(), c.hi(nx)
    R = w / 2
    t = c.plate
    mid = (z0 + z1) / 2
    m = 10
    # The arc shape turns about X: this part's length runs along X, its span along Z.
    for k in range(m):
        a0, a1 = math.pi * k / m, math.pi * (k + 1) / m
        ys = [y0 + R * math.sin(a) for a in (a0, a1)]
        zs = [mid - R * math.cos(a) for a in (a0, a1)]
        p.add(box((x0, min(ys), min(zs)), (x1, max(ys) + t, max(zs)), hidden=True))
    p.add(box((x0, y0, z0), (x1, y0 + R, z1), detail=True, shape="arc", inner=min(0.98, round(1 - t / R, 4)), angle=180, dir=180, segments=20))
    for i in pick(nl, 3):
        p.sock(f"left{i}", (i * c.cell, y0, 0), "-y")
        p.sock(f"right{i}", (i * c.cell, y0, (nx - 1) * c.cell), "-y")
    return steel(p, math.pi * R * l, 0.8)


def curved_plate(c, n, nx):
    s, w = n * c.cell, nx * c.cell
    p = part(f"Curved Plate {fmt(s)}x{fmt(w)} m", c, SHAPES, f"A quarter-pipe of plate, {fmt(s)} m radius and {fmt(w)} m wide: a wall's top round onto a roof, a hull's bilge.", role="panel", color=STEEL)
    t = c.plate
    y0, z1 = c.lo(), c.hi(n)
    m = min(14, 4 + 2 * n)
    for k in range(m):
        a0, a1 = (math.pi / 2) * k / m, (math.pi / 2) * (k + 1) / m
        ys, zs = [], []
        for a in (a0, a1):
            for rr in (s - t, s):
                ys.append(y0 + rr * math.sin(a))
                zs.append(z1 - rr * math.cos(a))
        p.add(box((c.lo(), max(y0, min(ys)), max(c.lo(), min(zs))), (c.hi(nx), min(c.hi(n), max(ys)), min(z1, max(zs))), hidden=True))
    p.add(box((c.lo(), c.lo(), c.lo()), (c.hi(nx), c.hi(n), c.hi(n)), detail=True, shape="arc", inner=min(0.98, round(1 - t / s, 4)), angle=90, dir=135, segments=max(10, 3 * n)))
    for i in pick(nx, 4):
        p.sock(f"bottom{i}", (i * c.cell, y0, 0), "-y")
        p.sock(f"back{i}", (i * c.cell, (n - 1) * c.cell, z1), "+z")
    return steel(p, w * s * math.pi / 2)


def wedge(c, ny, nz):
    h, l = ny * c.cell, nz * c.cell
    p = part(f"Wedge {fmt(h)}x{fmt(l)} m", c, SHAPES, f"A slope, {fmt(h)} m high over {fmt(l)} m and {fmt(c.cell)} m wide: ramps, prows, roof ends. Hollow.", color=STEEL)
    H = c.half
    y0, y1, z0, z1 = c.lo(), c.hi(ny), c.lo(), c.hi(nz)
    m = min(10, 3 + 2 * max(ny, nz))
    dz = (z1 - z0) / m
    for k in range(m):
        p.add(box((-H, y0, z0 + dz * k), (H, y0 + (y1 - y0) * (k + 0.5) / m, z0 + dz * (k + 1)), hidden=True))
    p.add(box((-H, y0, z0), (H, y1, z1), detail=True, shape="wedge", taper=[1, 0], shift=[0, -0.5]))
    for k in pick(nz, 3):
        p.sock(f"bottom{k}", (0, y0, k * c.cell), "-y")
    for j in pick(ny, 3):
        p.sock(f"back{j}", (0, j * c.cell, z1), "+z")
    return steel(p, (h + l) * c.cell * 2)


# ---- Stacks -----------------------------------------------------------------------------------------------------------


def smokestack(c, n, tall):
    s, h = n * c.cell, tall * c.cell
    p = part(f"Smokestack {fmt(s)}x{fmt(h)} m", c, STACKS, f"A round riveted smokestack, {fmt(s)} m across and {fmt(h)} m tall, banded and hollow. Tall ones need stays or a wide foot.", color=SOOT)
    z0, z1 = c.lo(), c.hi(tall)
    a, b = c.lo(), c.hi(n)
    t = max(c.plate, s * 0.05)
    # A square of walls for collision (a stack's bore is not worth a tree of boxes): rounds find it hollow, players can't climb in.
    p.add(box((a, a, z0), (b, a + t, z1), hidden=True))
    p.add(box((a, b - t, z0), (b, b, z1), hidden=True))
    p.add(box((a, a + t, z0), (a + t, b - t, z1), hidden=True))
    p.add(box((b - t, a + t, z0), (b, b - t, z1), hidden=True))
    p.add(box((a, a, z0), (b, b, z1), detail=True, shape="tube", segments=20, inner=0.86))
    bands = min(4, tall + 1)
    for k in range(bands):
        z = z0 + (z1 - z0) * (k + 0.5) / bands
        p.add(box((a - 0.01 * c.x, a - 0.01 * c.x, z), (b + 0.01 * c.x, b + 0.01 * c.x, z + h * 0.02), IRON, detail=True, shape="tube", segments=20, inner=0.9))
    for i in pick(n, 3):
        for j in pick(n, 3):
            p.sock(f"base{i}.{j}", (i * c.cell, j * c.cell, z0), "-z")
    for k in pick(tall, 3):
        p.sock(f"+x{k}", (b, (a + b) / 2 if n % 2 else 0, k * c.cell), "+x")
        p.sock(f"-x{k}", (a, (a + b) / 2 if n % 2 else 0, k * c.cell), "-x")
    return steel(p, math.pi * s * h, 0.8)


def chimney(c, n, tall):
    s, h = n * c.cell, tall * c.cell
    p = part(f"Chimney {fmt(s)}x{fmt(h)} m", c, STACKS, f"A square brick chimney, {fmt(s)} m across and {fmt(h)} m tall, with a corbelled top.", color=BRICK, material="stone")
    a, b, z0, z1 = c.lo(), c.hi(n), c.lo(), c.hi(tall)
    p.add(box((a, a, z0), (b, b, z1)))
    cap = s * 0.07
    p.add(box((a - cap, a - cap, z1 - s * 0.3), (b + cap, b + cap, z1 + 0.01 * c.x), SOOT, detail=True))
    sides(p, n, n, tall, limit=2)
    return steel(p, 4 * s * h, 0.8)


# ---- Stairs -----------------------------------------------------------------------------------------------------------


def stair(c):
    H = c.half
    h = c.cell
    steps = min(30, math.ceil(h / 0.5))  # (World.StepHeight is 0.55 m: every class's stair is walked)
    rise = h / steps
    runs = {"medium": 1, "large": 1, "capital": 1}[c.key]
    p = part(f"Stair {fmt(h)} m", c, STAIRS, f"A flight of {steps} steps up one {fmt(h)} m cell.", color=IRON)
    y0, z0, z1 = c.lo(), c.lo(), c.hi(runs)
    d = (z1 - z0) / steps
    for k in range(steps):
        p.add(box((-H, y0 + rise * k, z0 + d * k), (H, y0 + rise * (k + 1), z1)))
    p.sock("bottom", (0, y0, 0), "-y")
    p.sock("back", (0, 0, z1), "+z")
    p.sock("left", (-H, 0, 0), "-x")
    p.sock("right", (H, 0, 0), "+x")
    p.sock("top", (0, y0 + h, z1 - d / 2), "+y")
    p.d["walk"] = "stairs"
    return steel(p, h * c.cell * 2, 0.6)


def ladder(c):
    H = c.half
    h = c.cell
    w = min(1.0, c.cell)
    steps = min(30, math.ceil(h / 0.375))
    rise, run = h / steps, 0.25
    p = part(f"Ship Ladder {fmt(h)} m", c, STAIRS, f"A steep ship ladder, {steps} steps up one {fmt(h)} m cell, a metre wide: the quick way between decks.", color=IRON)
    y0, z1 = c.lo(), c.hi(1)
    for k in range(steps):
        p.add(box((-w / 2, y0 + rise * k, z1 - run * (steps - k)), (w / 2, y0 + rise * (k + 1), z1)))
    p.sock("bottom", (0, y0, z1 - run * steps / 2), "-y")
    p.sock("back", (0, 0, z1), "+z")
    p.d["walk"] = "ladder"
    return steel(p, h * 3, 0.3)


# ---- Adapters ---------------------------------------------------------------------------------------------------------


def adapter(a, b, m=4):
    """A pad with one big socket of class a on its back and a grid of class b on its front: what makes small things sit on big ones.
    Its back is on a whole centimetre; a small class's front on a half one, so what bolts on stays on the 1 cm grid."""
    count = m + 1
    step = b.cell * max(1, m // 4)
    size = step * 4 + b.cell
    p = Part(f"Adapter {a.name} to {b.name or 'Small'}{' Wide' if m > 4 else ''}", a, ADAPTERS,
             f"A {fmt(size)} m mounting pad: its back bolts to a {a.key} socket, its front offers {b.key} sockets, {fmt(step)} m apart. This is how a small thing sits on a big one.", color=BRASS)
    p.d["centreSockets"] = False
    back, front = -0.02, 0.025 if b.key == "small" else 0.03
    half = size / 2
    p.add(box((-half, back, -half), (half, front, half)))
    p.sock("back", (0, back, 0), "-y")
    for i in range(-2, 3):
        for j in range(-2, 3):
            p.sock(f"front{i + 2}.{j + 2}", (i * step, front, j * step), "+y", cls=b)
    assert count >= 5
    p.stats(size * size * b.kg_m2, 40 + size * size * b.health_m2, b.armour, b.seconds)
    return p


# ---- The list ---------------------------------------------------------------------------------------------------------


def everything():
    out = []

    def add(c, folder, p):
        out.append((f"{c.key}/{folder}", p))

    for c in BIG:
        add(c, "10-Frames", node(c))
        for n in (1, 2, 3, 4, 6, 8):
            add(c, "10-Frames", girder(c, n))
        for n in (1, 2, 4, 8):
            add(c, "10-Frames", box_girder(c, n))
            add(c, "10-Frames", truss(c, n))
            add(c, "10-Frames", column(c, n))
        for n in (1, 2, 4):
            add(c, "10-Frames", brace(c, n))
            add(c, "10-Frames", buttress(c, n))
            add(c, "10-Frames", corbel(c, n))
        for kind in PLATE_KINDS:
            for nx, nz in ((1, 1), (2, 1), (2, 2), (4, 2), (4, 4)):
                add(c, "20-Plates", plate(kind, c, nx, nz))
        for kind in ("deck", "timber", "grate"):
            for nx, nz in ((1, 1), (2, 1), (2, 2), (4, 2), (4, 4)):
                add(c, "30-Decks", deck(c, nx, nz, kind))
        for nx, ny in ((1, 1), (2, 1), (2, 2), (4, 2), (4, 4)):
            add(c, "40-Walls", window_wall(c, nx, ny))
        for n in (1, 2, 3):
            add(c, "40-Walls", arch_wall(c, n))
            add(c, "30-Decks", balcony(c, n))
        for nx, nz in ((2, 2), (4, 2), (4, 4)):
            add(c, "30-Decks", terrace(c, nx, nz))
        for n in (1, 2, 3, 4):
            add(c, "50-Roofs", dome(c, n))
        for n in (1, 2):
            add(c, "50-Roofs", onion(c, n))
            add(c, "50-Roofs", spire(c, n, 2 * n))
            add(c, "50-Roofs", spire(c, n, 4 * n))
        for n in (1, 2, 4):
            add(c, "50-Roofs", pyramid_roof(c, n))
        for nx, nz in ((2, 1), (2, 2), (2, 4), (4, 4)):
            add(c, "50-Roofs", gable_roof(c, nx, nz))
            add(c, "50-Roofs", barrel_roof(c, nx, nz))
        for n, nx in ((1, 1), (1, 2), (2, 2), (2, 4), (4, 4)):
            add(c, "60-Shapes", curved_plate(c, n, nx))
        for ny, nz in ((1, 1), (1, 2), (2, 1), (2, 2), (2, 4)):
            add(c, "60-Shapes", wedge(c, ny, nz))
        for n, tall in ((1, 2), (1, 4), (1, 8), (2, 4), (2, 8)):
            add(c, "70-Stacks", smokestack(c, n, tall))
            add(c, "70-Stacks", chimney(c, n, tall))
        add(c, "80-Stairs", stair(c))
        add(c, "80-Stairs", ladder(c))
    for c in (LARGE, CAPITAL):
        for nx, ny, nz in ((1, 1, 2), (2, 1, 2), (2, 1, 4), (4, 1, 4), (4, 2, 4)):
            add(c, "90-Hull", hull_section(c, nx, ny, nz))
        for nx, ny, nz in ((2, 1, 2), (2, 1, 4), (4, 1, 4), (4, 2, 4)):
            add(c, "90-Hull", bow(c, nx, ny, nz))
        for n in (2, 4, 8):
            add(c, "90-Hull", keel(c, n))
        for nx, nz in ((1, 1), (2, 1), (2, 2), (4, 2), (4, 4), (8, 4)):
            add(c, "95-Chassis", chassis_deck(c, nx, nz))
        for n in (2, 4, 8):
            add(c, "95-Chassis", spreader(c, n))
    for a, b in ((MEDIUM, SMALL), (LARGE, MEDIUM), (LARGE, SMALL), (CAPITAL, LARGE), (CAPITAL, MEDIUM), (CAPITAL, SMALL)):
        add(a, "99-Adapters", adapter(a, b))
    for a, b in ((LARGE, SMALL), (CAPITAL, SMALL), (CAPITAL, MEDIUM)):
        add(a, "99-Adapters", adapter(a, b, m=8))
    return out


def town_block():
    """A small example of the kit, for the parts menu: a chassis deck, columns, a storey, an arcade, a dome and a stack."""
    deck, floor, col = "Large Chassis Deck 16x16 m", "Large Deck 16x16 m", "Large Column 8 m"
    steps = [{"part": deck}]
    for socket in ("top0.0", "top3.0", "top0.3", "top3.3"):
        steps.append({"part": col, "on": 0, "socket": socket, "by": "-z"})
    steps.append({"part": floor, "on": 1, "socket": "+z", "by": "back0.0"})
    floor_step = len(steps) - 1
    steps.append({"part": "Large Window Wall 8x8 m", "on": 0, "socket": "top1.0", "by": "bottom0"})
    steps.append({"part": "Large Window Wall 8x8 m", "on": 0, "socket": "top1.3", "by": "bottom0"})
    steps.append({"part": "Large Dome 16 m", "on": floor_step, "socket": "front0.0", "by": "base0.0"})
    steps.append({"part": "Large Smokestack 4x32 m", "on": floor_step, "socket": "front3.3", "by": "base0.0"})
    return {
        "format": "call-of-slop-construct-blueprint/1",
        "name": "Town Block",
        "description": "A block of a traction city, built from the Large parts: a chassis deck on columns, a storey, window walls, a copper dome and a smokestack.",
        "steps": steps,
    }


def write_blueprint():
    """Town Block's file (--check compares). Returns the exit code."""
    path = os.path.join("Constructs", "Blueprints", "TownBlock.json")
    text = json.dumps(town_block(), indent=2) + "\n"
    if "--check" in sys.argv:
        with open(path, encoding="utf-8") as fh:
            same = fh.read() == text
        print("blueprint " + ("current" if same else "out of date"))
        return 0 if same else 1
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(write_parts(OUT, everything()) | write_blueprint())
