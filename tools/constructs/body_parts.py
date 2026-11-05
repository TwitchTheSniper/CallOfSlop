#!/usr/bin/env python3
"""Writes vehicle body parts: Constructs/Parts/Body (docs/vehiclekit/VEHICLEKIT.md, "Body parts").

What gets a vehicle out of a box: hoods, cabs and roll cages, fenders and bed sides, sloped sheets, a chassis tub, tube frames in straights and bends,
bolt decks a floor can take furniture on, and the furniture. Each hood bolts on by one socket on top of an engine, each cab by the rails' spacing, so the
kit fits one part where it used to tile a dozen plates.

A slope is a "wedge" shape with its front end sheared (shift), which the game turns into a real convex collider; hidden boxes in steps under it are what
the build rules and a player's feet see. A big shell is hollow to the build rules: only its skin is solid, so an engine or a seat fits inside it.
Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/body_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Body")
GROUP = "Vehicle Body"
STEEL, DARK, TUBE, MESH, WOOD, CLOTH = "#7a7f86", "#34383c", "#8a8f94", "#55595c", "#6e5238", "#6b6448"
HOODS, CABS, SKINS, TUBES, DECKS, FURN = "Hoods and noses", "Cabs and cages", "Fenders and skirts", "Tube frames", "Decks and tubs", "Furniture"


def part(name, section, desc, color=STEEL, role="panel", **k):
    return Part(name, SMALL, section, desc, role=role, color=color, group=GROUP, **k)


def slab(p, mn, mx, color=None):
    """A drawn solid box."""
    p.add(box(mn, mx, color))


def steps(p, x0, x1, z_front, z_back, y_front, y_back, thick, n):
    """Hidden solid boxes along a slope, front to back: what the build rules and feet use."""
    for k in range(n):
        a, b = k / n, (k + 1) / n
        y = y_front + (y_back - y_front) * (a + b) / 2
        p.add(box((x0, y - thick / 2, z_front + (z_back - z_front) * a), (x1, y + thick / 2, z_front + (z_back - z_front) * b), hidden=True))


def slope(p, x0, x1, z_front, z_back, y_front, y_back, thick, color=None):
    """A sheet from (z_front, y_front) up to (z_back, y_back): a sheared wedge, its back end the box and its front end moved."""
    h = thick / 2
    p.add(box((x0, y_back - h, z_front), (x1, y_back + h, z_back), color, detail=True, shape="wedge", taper=[1, 1], shift=[0, round((y_front - y_back) / thick, 4)]))


def bar(p, a, b, r=0.05, color=TUBE, solid=True):
    """A straight tube along an axis from a to b, drawn round on a hidden square (solid False: drawn only)."""
    mn = [min(a[k], b[k]) for k in range(3)]
    mx = [max(a[k], b[k]) for k in range(3)]
    axis = max(range(3), key=lambda k: mx[k] - mn[k])
    for k in range(3):
        if k != axis:
            mn[k] -= r
            mx[k] += r
    if solid:
        p.add(box(tuple(mn), tuple(mx), hidden=True))
    front = {0: "+x", 1: "+y", 2: None}[axis]
    kw = {"front": front} if front else {}
    p.add(box(tuple(mn), tuple(mx), color, detail=True, shape="cylinder", segments=10, **kw))


def rake(p, a, b, r=0.05, color=TUBE, n=2):
    """A tube leaning in the y-z plane, from a (low, front) to b (high, back): a sheared wedge, four-sided, on hidden steps."""
    x = a[0]
    steps(p, x - r, x + r, a[2], b[2], a[1], b[1], r * 2 + 0.03, n)
    t = r * 2
    p.add(box((x - r, b[1] - r, a[2]), (x + r, b[1] + r, b[2]), color, detail=True, shape="wedge", taper=[1, 1], shift=[0, round((a[1] - b[1]) / t, 4)]))


def bend(p, x0, x1, c_y, c_z, radius, dir_, color=TUBE, inner=0.82, angle=90):
    """A curved strip across x, a quarter of a ring about (c_y, c_z)."""
    p.add(box((x0, c_y - radius, c_z - radius), (x1, c_y + radius, c_z + radius), color, detail=True, shape="arc", inner=inner, angle=angle, dir=dir_, segments=8))


def pad(p, name, at, d, size=0.03):
    """A socket on a skin: the game wants a socket on a solid face, so a small hidden pad stands behind it."""
    v = {"+x": (1, 0, 0), "-x": (-1, 0, 0), "+y": (0, 1, 0), "-y": (0, -1, 0), "+z": (0, 0, 1), "-z": (0, 0, -1)}[d]
    mn = [at[k] - size - (0 if v[k] == 0 else size) * v[k] for k in range(3)]
    mx = [at[k] + size - (0 if v[k] == 0 else size) * v[k] for k in range(3)]
    for k in range(3):
        if v[k] > 0:
            mn[k], mx[k] = at[k] - 2 * size, at[k]
        elif v[k] < 0:
            mn[k], mx[k] = at[k], at[k] + 2 * size
    p.add(box(tuple(mn), tuple(mx), hidden=True))
    p.sock(name, at, d)


def rail_feet(p, zs):
    """Sockets for a piece standing across the rails: x +-0.25 at each z, on its underside (y 0)."""
    for k, z in enumerate(zs):
        p.sock(f"footL{k}", (-0.25, 0, z), "-y")
        p.sock(f"footR{k}", (0.25, 0, z), "-y")


def side_sockets(p, x, y, zs):
    for k, z in enumerate(zs):
        pad(p, f"-x{k}", (-x, y, z), "-x")
        pad(p, f"+x{k}", (x, y, z), "+x")


# ---- Hoods: on an engine's top socket, its origin there. Hollow: top and side skins are solid. --------------------------------------------------------


def hood(name, desc, w, zf, zb, y_top_f, y_top_b, y_low, mass, health, armour, color=STEEL, taper=0.86):
    p = part(name, HOODS, desc, color)
    h = (y_top_b - y_low)
    p.add(box((-w, y_low, zf), (w, y_top_b, zb), color, detail=True, shape="wedge", taper=[taper, (y_top_f - y_low) / h], shift=[0, round((y_top_f + y_low - y_top_b - y_low) / 2 / h, 4)]))
    steps(p, -w * 0.9, w * 0.9, zf + 0.02, zb, y_top_f - 0.05, y_top_b - 0.05, 0.05, 5)
    for s in (-1, 1):
        lo, hi = sorted((s * w * 0.9, s * w * 0.84))
        p.add(box((lo, y_low, zf + 0.25), (hi, y_top_f - 0.08, zb), hidden=True))
    p.add(box((-0.04, y_low + 0.04, zf - 0.02), (0.04, y_top_f - 0.03, zf + 0.03), DARK, detail=True))
    pad(p, "engine", (0, 0, 0), "-y")
    pad(p, "front", (0, (y_low + y_top_f) / 2, zf), "-z")
    side_sockets(p, w * 0.9, -0.27, [-0.25, 0.0, 0.25])
    pad(p, "top", (0, y_top_b - (y_top_b - y_top_f) * zb / (zb - zf), 0), "+y")
    p.stats(mass, health, armour, 2.0)
    return "", p


def bull_hood():
    p = part("Bull Hood", HOODS, "A rounded hood with a raised spine. Old truck, bad intentions.", STEEL)
    p.add(box((-0.56, -0.56, -0.95), (0.56, 0.16, 0.6), STEEL, detail=True, shape="rounded", radius=0.22, edges="long", segments=4))
    p.add(box((-0.06, 0.1, -0.9), (0.06, 0.2, 0.58), DARK, detail=True))
    p.add(box((-0.5, 0.05, -0.92), (0.5, 0.1, 0.58), hidden=True))
    for s in (-1, 1):
        lo, hi = sorted((s * 0.5, s * 0.46))
        p.add(box((lo, -0.56, -0.7), (hi, 0.05, 0.58), hidden=True))
    pad(p, "engine", (0, 0, 0), "-y")
    pad(p, "front", (0, -0.2, -0.95), "-z")
    side_sockets(p, 0.5, -0.27, [-0.25, 0.0, 0.25])
    pad(p, "top", (0, 0.1, 0), "+y")
    p.stats(38, 180, 6, 2.0)
    return "", p


def grille(name, desc, n, color):
    p = part(name, HOODS, desc, color)
    w = 0.5
    slab(p, (-w, -0.3, -0.04), (w, -0.26, 0), color)
    slab(p, (-w, 0.26, -0.04), (w, 0.3, 0), color)
    for k in range(n):
        x = -w + (2 * w) * (k + 0.5) / n
        p.add(box((x - 0.018, -0.26, -0.035), (x + 0.018, 0.26, -0.005), color, detail=True))
    pad(p, "back", (0, 0, 0), "+z")
    p.stats(8, 90, 4, 1.0)
    return "", p


def plow():
    p = part("Ram Plow", HOODS, "A wedge of plate on the bumper. Everything else moves first.", DARK)
    p.add(box((-0.6, -0.3, -0.42), (0.6, 0.3, 0), DARK, detail=True, shape="wedge", taper=[0.3, 0.55], shift=[0, -0.1]))
    steps(p, -0.45, 0.45, -0.3, 0, -0.1, 0.0, 0.5, 2)
    pad(p, "back", (0, 0, 0), "+z")
    p.stats(35, 400, 12, 2.0)
    return "", p


# ---- Cabs: stand across the rails (feet at y 0), z 0 the seat row. Bars in tube; skins hollow. ---------------------------------------------------------


def hoop(p, z, w, top, feet=True):
    """A roll hoop across x at z: a foot bar, posts, bent corners, a top bar."""
    r = 0.14
    bar(p, (-w - 0.05, 0.05, z), (w + 0.05, 0.05, z))
    for s in (-1, 1):
        bar(p, (s * w, 0.1, z), (s * w, top - r, z))
        lo, hi = sorted((s * (w - r), s * (w + 0.05)))
        p.add(box((lo, top - r, z - 0.05), (hi, top + 0.05, z + 0.05), TUBE, detail=True, shape="rounded", radius=0.05, edges="all", segments=3))
    bar(p, (-w + r, top, z), (w - r, top, z))


def roll_bar(name, desc, w, top, n_stays):
    p = part(name, CABS, desc, TUBE)
    z = 0.38
    hoop(p, z, w, top)
    p.sock("footL", (-0.25, 0, z), "-y")
    p.sock("footR", (0.25, 0, z), "-y")
    pad(p, "top", (0, top + 0.05, z), "+y")
    p.stats(16, 220, 6, 2.0)
    return "", p


def cage(name, desc, w, top, zf, zb, roof=None, mesh=False, slit=False, color=TUBE):
    """A hoop behind the seats, a low hoop at the dash, leaning A-pillars and top rails between them; a roof sheet, a mesh or armour by the arguments."""
    p = part(name, CABS, desc, color)
    hoop(p, zb, w, top)
    bar(p, (-w - 0.05, 0.05, zf), (w + 0.05, 0.05, zf))
    dash = top - 0.55
    hz = zf + 0.05
    for s in (-1, 1):
        x = s * w
        bar(p, (x, 0.1, zf), (x, dash, zf))
        rake(p, (x, dash, zf), (x, top, zf + 0.42))
        bar(p, (x, top, zf + 0.45), (x, top, zb - 0.1), solid=False)
    bar(p, (-w + 0.05, top, zf + 0.42), (w - 0.05, top, zf + 0.42), solid=False)
    bar(p, (-w + 0.05, dash, zf), (w - 0.05, dash, zf), 0.04, solid=False)
    if roof:
        slope(p, -w - 0.08, w + 0.08, zf + 0.42, zb + 0.15, top + 0.09, top + 0.2, 0.03, roof)
        p.add(box((-w, top + 0.08, zf + 0.5), (w, top + 0.12, zb), hidden=True))
    if mesh:
        for k in range(5):
            x = -w + 2 * w * (k + 0.5) / 5
            p.add(box((x - 0.012, dash - 0.45, zf - 0.02), (x + 0.012, dash, zf + 0.0), MESH, detail=True))
    p.sock("footL", (-0.25, 0, zb), "-y")
    p.sock("footR", (0.25, 0, zb), "-y")
    pad(p, "top", (0, top + 0.12 if roof else top + 0.05, (zf + zb) / 2), "+y")
    p.stats(32 if not roof else 44, 280, 6, 3.0)
    return "", p


def slit_cab():
    """Armoured shell: sloped front with a vision slit, tumbled-in sides, roof; the left side open to climb in."""
    p = part("Slit Cab", CABS, "An armoured cab with a slit to see through. Hot, dark, and a lot safer.", STEEL)
    w, top, zf, zb = 0.92, 1.42, -0.95, 0.42
    slope(p, -w, w, zf, zf + 0.42, 0.55, 1.0, 0.05, STEEL)  # the lower front
    slope(p, -w, w, zf + 0.5, zf + 0.72, 1.12, top, 0.05, STEEL)  # the upper front, over the slit
    steps(p, -w, w, zf, zf + 0.42, 0.55, 1.0, 0.05, 3)
    steps(p, -w, w, zf + 0.5, zf + 0.72, 1.12, top, 0.05, 2)
    slab(p, (-w, top - 0.03, zf + 0.7), (w, top + 0.03, zb))
    slab(p, (-w, 0.03, zb - 0.05), (w, top - 0.03, zb))
    slab(p, (w - 0.05, 0.05, zf + 0.1), (w, top - 0.03, zb - 0.05))
    bar(p, (-w, 0.05, zb - 0.02), (w, 0.05, zb - 0.02))
    bar(p, (-w + 0.02, 0.1, zf + 0.08), (-w + 0.02, 1.0, zf + 0.08))
    p.sock("footL", (-0.25, 0, 0.375), "-y")
    p.sock("footR", (0.25, 0, 0.375), "-y")
    pad(p, "top", (0, top + 0.03, 0), "+y")
    p.stats(70, 500, 16, 3.0)
    return "", p


def screen_frame():
    p = part("Windscreen Frame", CABS, "A raked frame where the glass used to be.", TUBE)
    w, h = 0.8, 0.6
    for s in (-1, 1):
        rake(p, (s * w, 0, -0.25), (s * w, h, 0.05), 0.04)
    bar(p, (-w, 0, -0.25), (w, 0, -0.25), 0.04)
    bar(p, (-w, h, 0.05), (w, h, 0.05), 0.04)
    pad(p, "footL", (-0.25, -0.04, -0.25), "-y")
    pad(p, "footR", (0.25, -0.04, -0.25), "-y")
    p.stats(10, 120, 4, 1.5)
    return "", p


# ---- Fenders, bed sides, slopes, tubs ----------------------------------------------------------------------------------------------------------------


def fender(name, desc, w, r, color=STEEL):
    """An arch over a wheel, from a socket on its inner lip: x 0 to w, the wheel's centre r - 0.03 below its top."""
    p = part(name, SKINS, desc, color)
    cy, cz = -r + 0.03, 0
    p.add(box((0, cy - r, cz - r), (w, cy + r, cz + r), color, detail=True, shape="arc", inner=0.92, angle=180, dir=0, segments=10))
    p.add(box((0, -0.04, -r * 0.55), (w, 0.03, r * 0.55), hidden=True))
    for s in (-1, 1):
        lo, hi = sorted((s * r * 0.6, s * r))
        p.add(box((0, cy + r * 0.2, lo), (w, cy + r * 0.75, hi), hidden=True))
    pad(p, "mount", (0, 0, 0), "-x")
    p.stats(9, 90, 2, 1.0)
    return "", p


def bed_side(name, desc, color=STEEL):
    """A pickup's side, 2 m long with an arch in the middle for the wheel, standing on a deck's edge: x 0 inside, its socket on that edge at z 0, 0.25..."""
    p = part(name, SKINS, desc, color)
    t, low, top, arch = 0.05, -0.06, 0.42, 0.6
    slab(p, (0, low, -1.0), (t, top, -arch))
    slab(p, (0, low, arch), (t, top, 1.0))
    slab(p, (0, 0.34, -arch), (t, top, arch))
    p.add(box((0, -0.27, -arch - 0.04), (0.34, 0.93 - 0.27 - 0.33, arch + 0.04), color, detail=True, shape="arc", inner=0.9, angle=180, dir=0, segments=10))
    p.add(box((0.03, 0.38, -0.5), (0.3, 0.42, 0.5), hidden=True))
    bar(p, (0.03, top + 0.03, -1.0), (0.03, top + 0.03, 1.0), 0.03, TUBE)
    for k, z in enumerate((-0.75, -0.5, 0.5, 0.75)):
        pad(p, f"edge{k}", (0, 0, z), "-x")
    p.stats(22, 150, 4, 2.0)
    return "", p


def sheet(name, run, rise, length, color=STEEL):
    """A flat sheet leaning: length along x, run along z from its low edge (front) to its high edge (back at z 0, y 0)."""
    p = part(name, SKINS, "A slanted body sheet. Bolts on by its top or bottom edge.", color)
    t = 0.05
    slope(p, 0, length, -run, 0, -rise, 0, t, color)
    steps(p, 0, length, -run, 0, -rise, 0, t, 3)
    n = max(1, int(length / 0.25))
    for k in range(0, n, 2):
        pad(p, f"top{k}", (0.125 + k * 0.25, 0, 0), "+z")
        pad(p, f"low{k}", (0.125 + k * 0.25, -rise, -run), "-z")
    p.stats(8 * length, 80 * length, 4, 1.0)
    return "", p


def armour_slope(name, h, length):
    """A thick slope to bolt outside a wall: solid wedge, its back the wall."""
    p = part(name, SKINS, "A slab of sloped armour. Rounds skate off it.", STEEL)
    p.add(box((0, 0, -0.3), (length, h, 0), DARK, detail=True, shape="wedge", taper=[1, 0.15], shift=[0, -0.42]))
    steps(p, 0, length, -0.28, 0, 0.05, h * 0.5, h * 0.6, 3)
    for k in range(0, int(length / 0.25), 2):
        pad(p, f"back{k}", (0.125 + k * 0.25, h / 2, 0), "+z")
    p.stats(60 * length * h, 300 * length, 14, 2.0)
    return "", p


def tub(name, n):
    """A solid under-body: rails' width and floor height. The stilts gone: floors bolt on top, sides close the gap to the rails."""
    L = n * 0.25
    p = part(name, DECKS, "A closed steel tub over the rails: a floor stands on it with no stilts showing.", DARK)
    w, top = 0.56, 0.5
    slab(p, (-w, 0, -0.125), (w, top, L - 0.125), DARK)
    for i in range(0, n, 1):
        z = i * 0.25
        p.sock(f"footL{i}", (-0.25, 0, z), "-y") if i % 2 == 0 else None
        p.sock(f"footR{i}", (0.25, 0, z), "-y") if i % 2 == 0 else None
        for j, x in enumerate((-0.25, 0, 0.25)):
            p.sock(f"top{j}{i}", (x, top, z), "+y") if i % 2 == 0 else None
    p.sock("-z", (0, 0.25, -0.125), "-z")
    p.sock("+z", (0, 0.25, L - 0.125), "+z")
    p.stats(9 * L, 220 * L, 8, 2.0)
    return "", p


def bolt_deck(name, nx, nz):
    """A floor plate with a socket every 0.25 m on its top, so furniture stands where it's wanted."""
    p = part(name, DECKS, "Floor plate with bolt holes all over: shelves, seats and crates stand on it.", "#6f6a60")
    C = 0.25
    x1, z1 = -0.125 + nx * C, -0.125 + nz * C
    slab(p, (-0.125, -0.125, -0.125), (x1, -0.075, z1))
    for i in range(nx):
        for k in range(nz):
            if nz < 8 or k % 2 == 0:
                p.sock(f"back{i}{k}", (i * C, -0.125, k * C), "-y")
            p.sock(f"front{i}{k}", (i * C, -0.075, k * C), "+y")
    for k in range(0, nz, 2 if nz >= 8 else 1):
        p.sock(f"-x{k}", (-0.125, -0.12, k * C), "-x")
        p.sock(f"+x{k}", (x1, -0.12, k * C), "+x")
    for i in range(nx):
        p.sock(f"-z{i}", (i * C, -0.12, -0.125), "-z")
        p.sock(f"+z{i}", (i * C, -0.12, z1), "+z")
    p.stats(20 * nx * nz * C * C, 160, 12, 1.0)
    return "", p


# ---- Tubes ------------------------------------------------------------------------------------------------------------------------------------------


def tube_bend(name, r):
    p = part(name, TUBES, "A 90 degree bend in steel tube.", TUBE, role="frame")
    p.add(box((-0.05, 0, 0), (0.05, r * 2, r * 2), TUBE, detail=True, shape="arc", inner=0.76, angle=90, dir=135, segments=8))
    p.add(box((-0.05, r * 1.4, r * 0.6), (0.05, r * 2, r * 1.2), hidden=True))
    p.add(box((-0.05, r * 0.6, r * 1.4), (0.05, r * 1.2, r * 2), hidden=True))
    pad(p, "a", (0, r * 2, r), "+y")
    pad(p, "b", (0, r, r * 2), "+z")
    p.stats(3, 80, 5, 1.0)
    return "", p


def bed_rail(name, L):
    p = part(name, TUBES, "Tube rail on posts round a bed: rope it down or hang on.", TUBE, role="frame")
    bar(p, (0, 0.45, -L / 2), (0, 0.45, L / 2), 0.04)
    for z in (-L / 2 + 0.08, 0, L / 2 - 0.08):
        bar(p, (0, 0, z), (0, 0.42, z), 0.03)
    for k, z in enumerate((-0.25, 0.25)):
        pad(p, f"edge{k}", (-0.03, 0.05, z), "-x")
    p.stats(5 * L, 100, 4, 1.0)
    return "", p


def bull_bar():
    p = part("Bull Bar", TUBES, "Bent tube in front of the grille. Ploughs through fences.", TUBE)
    w, h = 0.55, 0.5
    bar(p, (-w, 0, -0.14), (w, 0, -0.14), 0.045)
    for s in (-1, 1):
        rake(p, (s * 0.3, 0.0, -0.14), (s * 0.3, h, -0.02), 0.04)
    bar(p, (-0.34, h, -0.02), (0.34, h, -0.02), 0.04)
    p.add(box((-0.05, 0.15, -0.14), (0.05, 0.25, 0.04), hidden=True))
    p.sock("back", (0, 0.2, 0.04), "+z")
    p.stats(14, 300, 8, 1.5)
    return "", p


def barrel(name, L, cap):
    """A long round tank lying along z on cradles."""
    p = part(name, SKINS, f"Holds {cap} l in a round tank on cradles. A big target.", "#6e5a42", role="fueltank", tool="wrench")
    r = 0.62
    p.add(box((-r, 0.1, -L / 2), (r, 0.1 + 2 * r, L / 2), hidden=True))
    p.add(box((-r, 0.1, -L / 2), (r, 0.1 + 2 * r, L / 2), "#6e5a42", detail=True, shape="cylinder", segments=16))
    for z in (-L / 2 + 0.3, L / 2 - 0.3):
        slab(p, (-0.56, 0, z - 0.06), (0.56, 0.12, z + 0.06), DARK)
        p.add(box((-r - 0.012, 0.09, z - 0.03), (r + 0.012, 0.11 + 2 * r, z + 0.03), "#4e3a2a", detail=True, shape="cylinder", segments=16))
    p.add(box((-0.12, 0.1 + 2 * r - 0.02, -0.12), (0.12, 0.1 + 2 * r + 0.07, 0.12), DARK, detail=True, shape="cylinder", segments=10, front="+y"))
    for k, z in enumerate((-1.0, 0.0, 1.0)[: 3 if L > 2.5 else 2]):
        p.sock(f"footL{k}", (-0.25, 0, z), "-y")
        p.sock(f"footR{k}", (0.25, 0, z), "-y")
    pad(p, "top", (0, 0.1 + 2 * r, 0), "+y")
    p.stats(70 + 30 * L, 180, 3, 3.0)
    p.set(ports=[{"name": "Fuel out", "type": "pipe", "dir": "out", "fluid": "fuel", "at": [0.0, 0.1 + r, -L / 2], "face": "-z"},
                 {"name": "Fill", "type": "pipe", "dir": "in", "fluid": "fuel", "at": [0.0, 0.1 + 2 * r, 0.0], "face": "+y"}], fuelTank={"capacity": cap})
    return "", p


# ---- Furniture: stands on its "-y" sockets on a bolt deck, its back to a wall (+z of the part is out into the room, -z the wall). -----------------------


def feet(p, xs, zs, y=0):
    for i, x in enumerate(xs):
        for k, z in enumerate(zs):
            p.sock(f"foot{i}{k}", (x, y, z), "-y")


def rack_box(mn, mx):
    return {"min": list(mn), "max": list(mx), "rack": True, "label": "shelf"}


def workbench():
    p = part("Scrap Workbench", FURN, "A steel bench with a vice and a shelf under it. Somebody keeps tools here.", WOOD, role="storage", tool="wrench")
    t = 0.04
    slab(p, (-0.5, 0.82, -0.3), (0.5, 0.82 + t, 0.3), WOOD)
    slab(p, (-0.47, 0.3, -0.27), (0.47, 0.33, 0.27), DARK)
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * 0.45, sz * 0.25
            slab(p, (x - 0.025, 0, z - 0.025), (x + 0.025, 0.82, z + 0.025), DARK)
    p.add(box((0.3, 0.86, 0.1), (0.42, 0.96, 0.26), DARK, detail=True))
    slab(p, (-0.5, 0.86, 0.285), (0.5, 1.26, 0.3), DARK)  # (a tool board at its back)
    feet(p, (-0.25, 0.25), (0.0,))
    p.stats(30, 160, 3, 2.0)
    p.set(storage={"slots": [rack_box((-0.41, 0.33, -0.21), (0.41, 0.8, 0.21)), rack_box((-0.48, 0.86, -0.28), (0.25, 1.25, 0.27))]})
    return "", p


def crates():
    p = part("Crate Stack", FURN, "Lashed crates with the lids off. Rummage.", WOOD, role="storage", tool="wrench")
    slab(p, (-0.45, 0, -0.25), (0.0, 0.42, 0.25), WOOD)
    slab(p, (0.03, 0, -0.25), (0.45, 0.42, 0.25), "#5e4a34")
    slab(p, (-0.25, 0.44, -0.22), (0.2, 0.8, 0.22), "#7a5e40")
    slab(p, (-0.47, 0, 0.25), (-0.45, 1.16, 0.27), DARK)  # (a strap post, up past the top crate)
    feet(p, (-0.25, 0.25), (0.0,))
    p.stats(22, 90, 1, 2.0)
    p.material = "wood"
    p.set(storage={"slots": [rack_box((-0.44, 0.42, -0.24), (-0.26, 0.8, 0.24)), rack_box((0.21, 0.42, -0.24), (0.44, 0.8, 0.24)), rack_box((-0.24, 0.8, -0.21), (0.19, 1.15, 0.21))]})
    return "", p


def bunk():
    p = part("Scrap Bunk", FURN, "Two thin mattresses on a tube frame, and a locker under them.", TUBE, tool="wrench", role="storage")
    for y, c in ((0.42, CLOTH), (1.15, "#5c5a42")):
        slab(p, (-0.34, y, -0.95), (0.34, y + 0.09, 0.95), c)
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * 0.36, sz * 0.97
            slab(p, (x - 0.025, 0, z - 0.025), (x + 0.025, 1.45, z + 0.025), TUBE)
    feet(p, (-0.25, 0.25), (-0.75, 0.75))
    p.stats(24, 120, 2, 2.0)
    p.set(storage={"slots": [rack_box((-0.32, 0.02, -0.9), (0.32, 0.41, 0.9))]})
    return "", p


def bench():
    p = part("Crew Bench", FURN, "A plank bench with a locker in its belly.", WOOD, tool="wrench", role="storage")
    slab(p, (-0.24, 0.42, -0.74), (0.24, 0.47, 0.74), WOOD)
    slab(p, (-0.2, 0, -0.7), (0.2, 0.41, 0.7), "#4a3c2c")
    slab(p, (-0.27, 0.47, -0.75), (-0.24, 0.86, 0.75), WOOD)  # (its back)
    feet(p, (0.0,), (-0.5, 0.0, 0.5))
    p.stats(18, 100, 2, 2.0)
    p.set(storage={"slots": [rack_box((-0.23, 0.48, -0.72), (0.23, 0.85, 0.72))]})
    return "", p


def main():
    parts = [
        hood("Long Hood", "A long tapered hood. Bolts to the top of an engine, which it hides.", 0.56, -0.95, 0.6, -0.08, 0.14, -0.56, 34, 170, 5),
        hood("Snub Hood", "A short blunt hood. Bolts to the top of an engine, which it hides.", 0.6, -0.65, 0.6, -0.16, 0.12, -0.56, 30, 170, 5, taper=0.95),
        hood("Slab Nose", "A wide armoured nose sloping to a blade. Bolts to the top of an engine.", 0.85, -1.05, 0.6, -0.26, 0.12, -0.3, 80, 420, 14, DARK, 0.7),
        bull_hood(),
        grille("Bar Grille", "Upright bars across a radiator.", 9, DARK),
        grille("Slat Grille", "Three heavy slats across a radiator.", 3, STEEL),
        plow(),
        roll_bar("Roll Bar", "A tube hoop behind the seats.", 0.8, 1.32, 2),
        cage("Roll Cage", "A full tube cage round the seats: leaning pillars, nothing in the gaps.", 0.82, 1.32, -0.95, 0.38),
        cage("Tin Roof Cab", "A tube cage under a slanted tin roof.", 0.82, 1.32, -0.95, 0.38, roof="#6b6d70"),
        cage("Chop Top Cab", "A low cage with its roof cut down to the seats' tops.", 0.82, 1.24, -0.95, 0.38, roof=DARK),
        cage("Mesh Guard Cage", "A tube cage with mesh across the dash.", 0.82, 1.32, -0.95, 0.38, mesh=True),
        slit_cab(),
        screen_frame(),
        fender("Arch Fender", "A curved mudguard over a wheel.", 0.5, 0.55),
        fender("Wide Arch Fender", "A broad curved mudguard over a wheel.", 0.7, 0.62),
        bed_side("Pickup Bed Side", "A truck bed's side with an arch for the wheel."),
        bed_rail("Bed Rail 1 m", 1.0),
        bed_rail("Bed Rail 2 m", 2.0),
        sheet("Slant Sheet 1 m", 0.25, 0.45, 1.0),
        sheet("Slant Sheet 2 m", 0.25, 0.45, 2.0),
        sheet("Skirt Sheet 1 m", 0.35, 0.5, 1.0, DARK),
        sheet("Skirt Sheet 2 m", 0.35, 0.5, 2.0, DARK),
        armour_slope("Armour Slope 1 m", 0.75, 1.0),
        armour_slope("Armour Slope 2 m", 0.75, 2.0),
        tub("Chassis Tub 1 m", 4),
        tub("Chassis Tub 2 m", 8),
        bolt_deck("Bolt Deck 1 m", 4, 4),
        bolt_deck("Bolt Deck 0.5x1 m", 2, 4),
        bolt_deck("Bolt Deck 0.5 m", 2, 2),
        bolt_deck("Bolt Deck 0.25 m", 1, 1),
        bolt_deck("Bolt Deck 1x2 m", 4, 8),
        tube_bend("Tube Bend 0.25 m", 0.125),
        tube_bend("Tube Bend 0.5 m", 0.25),
        bull_bar(),
        barrel("Tanker Barrel 3 m", 3.0, 600),
        barrel("Tanker Barrel 2 m", 2.0, 400),
        workbench(),
        crates(),
        bunk(),
        bench(),
    ]
    return write_parts(OUT, parts)


if __name__ == "__main__":
    sys.exit(main())
