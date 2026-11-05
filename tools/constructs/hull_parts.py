#!/usr/bin/env python3
"""Writes faceted hull parts: Constructs/Parts/Hull (docs/vehiclekit/VEHICLEKIT.md, "Silhouette").

A walk-in body is a run of 1 m bays, each a slice between two cross-sections, so a roofline steps, a tail narrows and a nose rakes without a wall of plates. A section (PROFILES) is
a floor edge, a waist, a shoulder and a roof edge: the side breaks twice on the way up, so no side is one flat sheet. Bay names give their sections, front to back: Hull Bay T-L
goes from tall to low. Brows close a body's front, Tails its back, the Door Bay holds a side door.

Facets are "hull" shapes (drawn only, any angle); hidden boxes stand for them to the build rules, the physics and a player's feet. The inside is hollow to all of them.
Recipes come from tools/constructs/assembling.py; run it after this one.

Run it from the repo root: python3 -I tools/constructs/hull_parts.py (--check only compares).
Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Hull")
GROUP = "Vehicle Body"
HULLS, GUNS = "Hull bays", "Sponsons and panniers"
STEEL, DARK, RUST, ROOF = "#7a7f86", "#34383c", "#6e5a48", "#5c5f62"
T = 0.04  # a facet's thickness

# Half width and height: floor edge, waist, shoulder, roof edge.
PROFILES = {
    "T": ((1.02, 0.0), (1.1, 0.85), (1.0, 1.95), (0.6, 2.3)),
    "L": ((1.02, 0.0), (1.1, 0.7), (1.0, 1.65), (0.55, 2.02)),
    "N": ((0.82, 0.0), (0.88, 0.85), (0.8, 1.75), (0.45, 2.12)),
}
# What a section shrinks to where a body ends: a brow's front, a tail's back.
SMALLER = {
    "T": ((0.95, 0.0), (0.98, 0.8), (0.82, 1.45), (0.5, 1.72)),
    "L": ((0.95, 0.0), (0.98, 0.65), (0.82, 1.3), (0.48, 1.55)),
    "N": ((0.74, 0.0), (0.78, 0.8), (0.66, 1.4), (0.38, 1.62)),
}


def part(name, section, desc, color=STEEL, role="panel", **k):
    return Part(name, SMALL, section, desc, role=role, color=color, group=GROUP, **k)


def r(v):
    return round(v, 4)


def facet(p, points, normal, color, t=T):
    """A flat panel through the points, t thick along normal (a unit vector): the hull of the points moved each way."""
    both = [[r(a + s * n * t / 2) for a, n in zip(q, normal)] for q in points for s in (-1, 1)]
    mn = tuple(min(q[k] for q in both) for k in range(3))
    mx = tuple(max(q[k] for q in both) for k in range(3))
    p.add(box(mn, mx, color, detail=True, shape="hull", points=both))


def hide(p, mn, mx):
    p.add(box(mn, mx, hidden=True))


def pad(p, name, at, d, size=0.03):
    v = {"+x": (1, 0, 0), "-x": (-1, 0, 0), "+y": (0, 1, 0), "-y": (0, -1, 0), "+z": (0, 0, 1), "-z": (0, 0, -1)}[d]
    mn = [at[k] - (0 if v[k] else size) - (2 * size if v[k] > 0 else 0) for k in range(3)]
    mx = [at[k] + (0 if v[k] else size) + (2 * size if v[k] < 0 else 0) for k in range(3)]
    hide(p, mn, mx)
    p.sock(name, at, d)


def sides(p, a, b, z0, z1, skip=False, plate=None):
    """Both sides and the roof between sections a at z0 and b at z1. skip: the +x side is left to the caller."""
    colours = (RUST, STEEL, ROOF)
    for s in (-1, 1) if not skip else (-1,):
        for k in range(3):
            (ax0, ay0), (ax1, ay1) = a[k], a[k + 1]
            (bx0, by0), (bx1, by1) = b[k], b[k + 1]
            q = [(s * ax0, ay0, z0), (s * ax1, ay1, z0), (s * bx1, by1, z1), (s * bx0, by0, z1)]
            dy, dx = ay1 - ay0, ax1 - ax0
            n = (dy * s, -dx, 0)
            ln = (n[0] ** 2 + n[1] ** 2) ** 0.5
            facet(p, q, (n[0] / ln, n[1] / ln, 0), colours[k])
        # What stands for them: a wall up to the shoulder, a step for the cant.
        x = (a[1][0] + b[1][0] + a[2][0] + b[2][0]) / 4
        y = min(a[2][1], b[2][1])
        lo, hi = sorted((s * (x - 0.03), s * (x + 0.03)))
        hide(p, (lo, 0.0, z0), (hi, y, z1))
        x2 = (a[3][0] + b[3][0] + a[2][0] + b[2][0]) / 4
        lo, hi = sorted((s * (x2 - 0.03), s * (x2 + 0.03)))
        hide(p, (lo, y, z0), (hi, min(a[3][1], b[3][1]), z1))
    facet(p, [(-a[3][0], a[3][1], z0), (a[3][0], a[3][1], z0), (b[3][0], b[3][1], z1), (-b[3][0], b[3][1], z1)], (0, 1, 0), ROOF)
    w, y = min(a[3][0], b[3][0]), min(a[3][1], b[3][1])
    hide(p, (-w, y - 0.03, z0), (w, y + 0.03, z1))


def fittings(p, a, b, length, door=False):
    """Feet on a Bolt Deck's grid, a turret's socket on the roof, a sponson's on each waist."""
    for k, z in enumerate((0.125, length - 0.125)):
        p.sock(f"footL{k}", (-0.75, 0, z), "-y")
        p.sock(f"footR{k}", (0.75, 0, z), "-y")
    w = (a[1][0] + b[1][0]) / 2
    y = (a[1][1] + b[1][1]) / 2
    pad(p, "-x0", (-w, y, length / 2), "-x")
    if not door:
        pad(p, "+x0", (w, y, length / 2), "+x")
    pad(p, "top", (0, min(a[3][1], b[3][1]) + 0.03, length / 2), "+y")


def bay(front, back):
    a, b = PROFILES[front], PROFILES[back]
    name = f"Hull Bay {front}" if front == back else f"Hull Bay {front}-{back}"
    p = part(name, HULLS, "A metre of walk-in hull. Bays stand on a bolt deck, one after another.")
    sides(p, a, b, 0.0, 1.0)
    fittings(p, a, b, 1.0)
    p.stats(120, 500, 8, 2.0)
    return "", p


def cap(p, section, z, normal, color=STEEL):
    """Closes a body's end: the section as a flat plate at z."""
    half = [(x, y, z) for x, y in section]
    facet(p, half + [(-x, y, z) for x, y in section], (0, 0, 1), color, 0.05)
    hide(p, (-section[1][0] + 0.05, 0, z - 0.03), (section[1][0] - 0.05, section[3][1] * 0.9, z + 0.03))


def brow(name):
    """A body's front: the sides close in and the top rakes back from the front plate."""
    a, b = SMALLER[name], PROFILES[name]
    p = part(f"Hull Brow {name}", HULLS, "A body's raked front: bolts on as the first bay.")
    sides(p, a, b, 0.0, 1.0)
    cap(p, a, 0.0, -1)
    fittings(p, a, b, 1.0)
    p.stats(150, 600, 10, 2.0)
    return "", p


def tail(name):
    """A body's back: narrower and lower to a flat stern."""
    a, b = PROFILES[name], SMALLER[name]
    p = part(f"Hull Tail {name}", HULLS, "A body's tapering back: bolts on as the last bay.")
    sides(p, a, b, 0.0, 1.0)
    cap(p, b, 1.0, 1)
    fittings(p, a, b, 1.0)
    p.stats(150, 600, 10, 2.0)
    return "", p


def door_bay():
    """1.5 m of tall hull with a 1.25 x 2 m doorway in its +x side, the door's plane at x = PLANE."""
    PLANE = 1.0
    a = PROFILES["T"]
    p = part("Hull Door Bay T", HULLS, "A bay with a doorway in one side. Takes a 1.25x2 m flush door on a Door Hinge.")
    L = 1.5
    sides(p, a, a, 0.0, L, skip=True)
    top = 2.0
    # The flat frame round the doorway, then the cant over it.
    for z0, z1 in ((0.0, 0.125), (1.375, L)):
        facet(p, [(PLANE, 0.0, z0), (PLANE, top, z0), (PLANE, top, z1), (PLANE, 0.0, z1)], (1, 0, 0), RUST)
        hide(p, (PLANE - 0.03, 0.0, z0), (PLANE + 0.03, top, z1))
    facet(p, [(PLANE, top, 0.0), (a[3][0], a[3][1], 0.0), (a[3][0], a[3][1], L), (PLANE, top, L)], (0.66, 0.75, 0), STEEL)
    hide(p, (PLANE - 0.03, top, 0.0), (PLANE + 0.03, top + 0.1, L))
    hide(p, (0.65, 2.15, 0.0), (0.71, a[3][1], L))
    fittings(p, a, a, L, door=True)
    p.stats(170, 500, 8, 2.0)
    return "", p


def cab_over():
    """A cab-forward shell over engine and seats: blunt raked nose, tall screen, no bonnet. Stands on the rails like a cage; the back is open to climb in."""
    p = part("Cab Over Shell", HULLS, "A flat-faced cab over the engine, the seats and all. Open at the back.", STEEL)
    w0, w1, zn, zs, zb = 0.8, 0.95, -2.15, -1.25, 0.25
    y0, ybelt, yr = 0.12, 1.02, 1.82
    for s in (-1, 1):
        facet(p, [(s * w1, y0, zs), (s * w1, ybelt, zs), (s * w1, ybelt, zb), (s * w1, y0, zb)], (1, 0, 0), RUST)
        facet(p, [(s * w0, y0, zn), (s * w0, ybelt, zn), (s * w1, ybelt, zs), (s * w1, y0, zs)], (0.95 * s, 0, -0.3), RUST)
        facet(p, [(s * w1, ybelt, zs), (s * w0, ybelt, zn), (s * 0.68, yr, -1.45), (s * 0.78, yr, zb)], (0.9 * s, 0.4, -0.15), STEEL)
        lo, hi = sorted((s * 0.9, s * 0.96))
        hide(p, (lo, 0.3, zs), (hi, ybelt, zb - 0.9))
    facet(p, [(-w0, y0, zn), (w0, y0, zn), (w0, ybelt, zn), (-w0, ybelt, zn)], (0, 0.05, -1), DARK, 0.05)
    facet(p, [(-w0, ybelt, zn), (w0, ybelt, zn), (0.68, yr, -1.45), (-0.68, yr, -1.45)], (0, 0.6, -0.8), "#3f4648")
    facet(p, [(-0.68, yr, -1.45), (0.68, yr, -1.45), (0.78, yr, zb), (-0.78, yr, zb)], (0, 1, 0), ROOF)
    hide(p, (-0.8, y0, zn), (0.8, ybelt - 0.1, zn + 0.05))
    hide(p, (-0.68, yr - 0.03, -1.45), (0.68, yr + 0.03, zb))
    for s in (-1, 1):
        lo, hi = sorted((s * 0.8, s * 0.86))
        hide(p, (lo, 0.3, zb - 0.06), (hi, yr, zb))
    p.sock("footL", (-0.25, 0, 0.25), "-y")
    p.sock("footR", (0.25, 0, 0.25), "-y")
    hide(p, (-0.28, 0, 0.22), (0.28, 0.06, 0.28))
    pad(p, "top", (0, yr + 0.03, -0.3), "+y")
    p.stats(90, 480, 10, 3.0)
    return "", p


def sponson():
    """A gun tub on a hull's side: the turntable sits on its top."""
    p = part("Gun Sponson", GUNS, "A bulged tub on a vehicle's side, with a gun's turntable on top.", STEEL)
    w, h = 0.42, 0.22
    top = [(0.0, h, -0.4), (0.62, h, -0.3), (0.62, h, 0.3), (0.0, h, 0.4)]
    low = [(0.0, -0.3, -0.25), (0.28, -0.3, -0.15), (0.28, -0.3, 0.15), (0.0, -0.3, 0.25)]
    pts = [[r(c) for c in q] for q in top + low]
    p.add(box((0, -0.3, -0.4), (0.62, h, 0.4), RUST, detail=True, shape="hull", points=pts))
    hide(p, (0.02, -0.1, -0.28), (0.5, h, 0.28))
    pad(p, "back", (0, 0, 0), "-x")
    p.sock("top", (0.3, h, 0), "+y")
    p.stats(40, 260, 10, 1.0)
    return "", p


def pannier():
    """A faceted stowage box hung on a side: a bulge that breaks the flank."""
    p = part("Hull Pannier 1 m", GUNS, "A faceted box hung on a hull's side.", RUST)
    pts = [(0.0, 0.3, -0.5), (0.0, 0.3, 0.5), (0.22, 0.26, -0.42), (0.22, 0.26, 0.42), (0.34, 0.0, -0.42), (0.34, 0.0, 0.42), (0.2, -0.28, -0.34), (0.2, -0.28, 0.34), (0.0, -0.3, -0.42), (0.0, -0.3, 0.42)]
    p.add(box((0, -0.3, -0.5), (0.34, 0.3, 0.5), RUST, detail=True, shape="hull", points=[list(q) for q in pts]))
    hide(p, (0.02, -0.2, -0.38), (0.24, 0.2, 0.38))
    pad(p, "back", (0, 0, 0), "-x")
    p.stats(30, 220, 6, 1.0)
    return "", p


def ammo_hutch():
    """A low armoured lid over a crate or two on a deck: where a feed starts."""
    p = part("Ammo Hutch", GUNS, "A low armoured lid over ammunition on a deck. Stands on a bolt deck.", DARK)
    a, b = (0.62, 0.0), (0.46, 0.62)
    zs = ((-0.52, -0.4), (0.52, 0.4))
    for s in (-1, 1):
        facet(p, [(s * 0.62, 0, -0.52), (s * 0.46, 0.62, -0.4), (s * 0.46, 0.62, 0.4), (s * 0.62, 0, 0.52)], (0.97 * s, 0.24, 0), DARK, 0.06)
        lo, hi = sorted((s * 0.55, s * 0.6))
        hide(p, (lo, 0, -0.4), (hi, 0.55, 0.4))
    facet(p, [(-0.46, 0.62, -0.4), (0.46, 0.62, -0.4), (0.46, 0.62, 0.4), (-0.46, 0.62, 0.4)], (0, 1, 0), DARK, 0.06)
    hide(p, (-0.46, 0.59, -0.4), (0.46, 0.65, 0.4))
    facet(p, [(-0.62, 0, -0.52), (0.62, 0, -0.52), (0.46, 0.62, -0.4), (-0.46, 0.62, -0.4)], (0, 0.2, -0.98), RUST, 0.06)
    for i, x in enumerate((-0.5, 0.5)):
        for k, z in enumerate((-0.25, 0.25)):
            p.sock(f"foot{i}{k}", (x, 0, z), "-y")
            hide(p, (x - 0.03, 0, z - 0.03), (x + 0.03, 0.03, z + 0.03))
    pad(p, "top", (0, 0.65, 0), "+y")
    p.stats(60, 500, 16, 2.0)
    return "", p


def dually():
    """Two tyres on one hub, for a heavy rear axle."""
    p = part("Dually Wheel", "Wheels", "Twin 0.9 m tyres on one hub: a heavy axle's rear wheels.", "#1f2124", role="wheel", tool="wrench")
    for x0, x1 in ((-0.34, -0.03), (0.03, 0.34)):
        p.add(box((x0, -0.45, -0.45), (x1, 0.45, 0.45), "#1f2124", detail=True, shape="cylinder", segments=16, front="+x"))
        hide(p, (x0, -0.32, -0.32), (x1, 0.32, 0.32))
    p.add(box((-0.03, -0.2, -0.2), (0.03, 0.2, 0.2), "#55595c", detail=True, shape="cylinder", segments=10, front="+x"))
    for s in (-1, 1):
        p.add(box((s * 0.33 - 0.015, -0.22, -0.22), (s * 0.33 + 0.015, 0.22, 0.22), "#6e7275", detail=True, shape="cylinder", segments=10, front="+x"))
    p.sock("axle", (-0.34, 0, 0), "-x", kind="axle")
    p.stats(68, 300, 3, 1.5)
    p.set(wheel={"radius": 0.45, "grip": 1.0, "offroad": 1.3, "roll": 0.025, "flat": True}, material="rubber", onBreak="disable")
    return "", p


def main():
    names = ["T", "L", "N"]
    parts = [bay(a, a) for a in names] + [bay(a, b) for a in names for b in names if a != b]
    parts += [brow(a) for a in names] + [tail(a) for a in names]
    parts += [door_bay(), cab_over(), sponson(), pannier(), ammo_hutch(), dually()]
    return write_parts(OUT, parts)


if __name__ == "__main__":
    sys.exit(main())
