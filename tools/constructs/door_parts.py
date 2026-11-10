#!/usr/bin/env python3
"""Writes the flush doors, the door hinges and the Door Handle: Constructs/Parts/Doors (docs/sandbox/CONSTRUCTS.md, "Doors").

A door here hangs inside its opening, in the plane of the wall, with the same small gap all round. A Door Hinge does that: it fits in the gap
itself (GAP wide), its base socket on one jamb and its leaf socket facing the door, and its pivot sits on the door's outer face, so the door swings
out past the jamb. Its min is 0: it opens one way, towards its own -z, and the hinge's knuckles are all that shows outside. Turn it end over to swing
the other way.

A door is named for its opening, not its slab: a Flush Door 1.5x2.25 m is GAP smaller at each edge, with the hinge in the gap on one side. Sizes are
the openings the game has: 1.5x2.25 m a Door Wall, Door Corner or bunker; 0.7x1.8 m a Door Frame 1x2 m (its jamb is off the socket grid: weld the hinge on); 1.25x2.35 m a vehicle kit room; 1x2 m and
1.25x2 m for what players cut themselves. Its hang socket is at the middle of its edge, and a door's height is measured from the same middle, so a hinge
at the opening's mid-height centres it.

Recipes come from tools/constructs/assembling.py; run it after this one.
Run it from the repo root: python3 -I tools/constructs/door_parts.py (--check only compares).
Boxes go through zfight.resolve (CLAUDE.md, "No coplanar faces"). Fix this file, not its output.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scale import SMALL, Part, box, write_parts  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "Doors")
SECTION = "Doors and gates"
GROUP = "Building"
GAP = 0.02  # the gap round a door, and a Door Hinge's width
THICK = 0.04  # a flush door
PIVOT = -THICK / 2  # a Door Hinge's axis: on the door's outer face, so its inner corner swings away from the jamb
KNUCKLE = 0.01  # its barrel's radius: all that stands out of the wall
STEEL, DARK, BARREL, PIN, GRIP = "#3b4046", "#2a2d31", "#55595e", "#8d939a", "#c8c8c8"
OPENINGS = [(0.7, 1.8), (1.0, 2.0), (1.25, 2.0), (1.25, 2.35), (1.5, 2.25)]
#          name, colour, trim colour, material, kg/m2, health/m2, armour
KINDS = [("Steel", "#5a6168", "#4c5359", "steel", 43, 215, 30), ("Wood", "#6b5a3e", "#5c4d35", "wood", 12.5, 57, 2)]


def hinge(name, length, knuckles, mass, health):
    p = Part(name, SMALL, "Hinges", "Fits in a door's gap, on the jamb: what hangs on it swings in the wall's plane and out past it. Any panel can hang on it.",
             role="hinge", tool="wrench", color=STEEL, group="Joints and Motion")
    h = length / 2
    p.add(box((-GAP / 2, -h, -THICK / 2), (GAP / 2, h, THICK / 2), DARK, side="base"))
    pitch = length / knuckles
    for k in range(knuckles):
        y = -h + (k + 0.5) * pitch
        b = pitch * 0.4
        p.add(box((-KNUCKLE, y - b, PIVOT - KNUCKLE), (KNUCKLE, y + b, PIVOT + KNUCKLE), PIN if k % 2 else BARREL, detail=True, shape="cylinder", segments=8, front="+y",
                  side="leaf" if k % 2 else "base", paintable=False))
    p.sock("base", (-GAP / 2, 0, 0), "-x")
    p.sock("leaf", (GAP / 2, 0, 0), "+x", kind="leaf")
    p.set(collider="boxes")  # (the knuckles are looks, and half of them swing with the leaf: a hull here would stand still)
    p.set(hinge={"axis": "+y", "pivot": [0, 0, PIVOT], "min": 0, "max": 100, "leaf": "leaf"}, config=[
        {"key": "max", "label": "Swings open to", "group": "Limits", "unit": "deg", "min": 0, "max": 100, "step": 1, "tooltip": "How far it swings open."},
        {"key": "pushable", "label": "Opens when pushed", "group": "Door", "default": True, "tooltip": "On: a player walking into it swings it. Off: only F opens it. A wire into its Push port overrides this while connected: high on, low off."}])
    p.stats(mass, health, 5, 1.5)
    return p


def door(kind, w, h):
    word, colour, trim, material, kg, hp, armour = kind
    name = f"{word} Flush Door {w:g}x{h:g} m"
    dw, dh = w - 2 * GAP, h - 2 * GAP
    p = Part(name, SMALL, SECTION, f"Fills a {w:g} by {h:g} m opening, on a Door Hinge in its gap.", role="door", tool="wrench", material=material, color=colour, group=GROUP)
    t = THICK / 2
    p.add(box((0, -dh / 2, -t), (dw, dh / 2, t)))
    # Two raised panels, on both faces (one box through the door: a box under 1 cm is skipped), and the handle through it.
    m = 0.1
    mid = dh * 0.1
    p.add(box((m, mid + 0.05, -t - 0.003), (dw - m, dh / 2 - m, t + 0.003), trim, detail=True))
    p.add(box((m, -dh / 2 + m, -t - 0.003), (dw - m, mid - 0.05, t + 0.003), trim, detail=True))
    p.add(box((dw - 0.15, -0.05, -t - 0.04), (dw - 0.1, 0.05, t + 0.04), GRIP, detail=True, use=True, paintable=False))
    p.sock("hang", (0, 0, 0), "-x", kind="leaf")
    p.sock("front", (dw / 2, 0, t), "+z")
    p.sock("back", (dw / 2, 0, -t), "-z")
    area = dw * dh
    p.stats(kg * area, hp * area, armour, 2.0)
    if kg * area <= 40:
        p.set(physicsOnly=True)  # (light but bulky: never an item, as the Wood Door 1.25 m)
    return p


# Event doors (docs/sandbox/CONSTRUCTS.md, "Event doors"): no hinge part and no joint. The door part goes straight on the
# opening's jamb (its "hang" socket is a plain one, GAP off its edge, so the gap is kept), and its "door" block says how it
# moves: a swing door turns a quarter turn about its edge on its outer face (towards its own -z, out past the jamb), a
# bulkhead slides into the wall beside the opening. Its state is the game's (shut, open, moving), its F line is a door's,
# and it has Open, Toggle and Lock ports. A Left and a Right leaf hung on either jamb of one opening meet in the middle
# ("meet" sockets): linked there, F on either works both.
EVENT_SECONDS = 0.8
BULKHEAD_THICK = 0.06
EVENT_OPENINGS = [(1.0, 2.0), (1.25, 2.0), (1.25, 2.35), (1.5, 2.25)]


def slab(p, kind, x0, x1, dh, t):
    word, colour, trim, material, kg, hp, armour = kind
    p.add(box((x0, -dh / 2, -t), (x1, dh / 2, t)))
    m = 0.1
    mid = dh * 0.1
    p.add(box((x0 + m, mid + 0.05, -t - 0.003), (x1 - m, dh / 2 - m, t + 0.003), trim, detail=True))
    p.add(box((x0 + m, -dh / 2 + m, -t - 0.003), (x1 - m, mid - 0.05, t + 0.003), trim, detail=True))


def hang_strip(p, s=1):
    """A hidden solid strip in the gap on the hang side: what the plain "hang" socket sits on (a socket is on a face)."""
    x0, x1 = (-GAP, 0) if s > 0 else (0, GAP)
    p.add(box((x0, -0.2, -0.01), (x1, 0.2, 0.01), hidden=True))


def event_stats(p, kind, area):
    word, colour, trim, material, kg, hp, armour = kind
    p.stats(kg * area, hp * area, armour, 2.0)
    if kg * area <= 40:
        p.set(physicsOnly=True)


def swing_door(kind, w, h):
    word, colour, trim, material = kind[:4]
    name = f"{word} Swing Door {w:g}x{h:g} m"
    dw, dh = w - 2 * GAP, h - 2 * GAP
    p = Part(name, SMALL, SECTION, f"Fills a {w:g} by {h:g} m opening: goes straight on its jamb, no hinge. F opens it; it swings out past the jamb.",
             role="door", tool="wrench", material=material, color=colour, group=GROUP)
    t = THICK / 2
    slab(p, kind, 0, dw, dh, t)
    p.add(box((dw - 0.15, -0.05, -t - 0.04), (dw - 0.1, 0.05, t + 0.04), GRIP, detail=True, use=True, paintable=False))
    hang_strip(p)
    p.sock("hang", (-GAP, 0, 0), "-x")
    p.sock("front", (dw / 2, 0, t), "+z")
    p.sock("back", (dw / 2, 0, -t), "-z")
    p.set(door={"kind": "swing", "axis": "+y", "pivot": [0, 0, -t], "angle": 90, "seconds": EVENT_SECONDS, "hang": "hang"})
    event_stats(p, kind, dw * dh)
    return p


def double_leaf(kind, w, h, side):
    """Half of a double door for a w-wide opening: Left hangs on the left jamb, Right on the right; they meet in the middle."""
    word, colour, trim, material = kind[:4]
    name = f"{word} Double Door {side[0]} {w:g}x{h:g} m"  # (L or R: a name is at most 32 characters)
    dw, dh = w / 2 - 1.5 * GAP, h - 2 * GAP
    p = Part(name, SMALL, SECTION, f"One leaf of a double door for a {w:g} by {h:g} m opening, on the {side.lower()} jamb; the other leaf meets it. F on either opens both.",
             role="door", tool="wrench", material=material, color=colour, group=GROUP)
    t = THICK / 2
    s = 1 if side == "Left" else -1  # (the Right leaf is the Left one mirrored: it hangs on its +x edge)
    x0, x1 = (0, dw) if s > 0 else (-dw, 0)
    slab(p, kind, x0, x1, dh, t)
    gx = dw - 0.12 if s > 0 else -dw + 0.07
    p.add(box((gx, -0.05, -t - 0.04), (gx + 0.05, 0.05, t + 0.04), GRIP, detail=True, use=True, paintable=False))
    hang_strip(p, s)
    p.sock("hang", (-s * GAP, 0, 0), "-x" if s > 0 else "+x")
    m0, m1 = sorted((s * (dw - GAP), s * (dw + GAP / 2)))
    p.add(box((m0, -0.2, -0.01), (m1, 0.2, 0.01), hidden=True))  # (the meeting edge's socket sits on it, half the gap out)
    p.sock("meet", (s * (dw + GAP / 2), 0, 0), "+x" if s > 0 else "-x")
    p.sock("front", (s * dw / 2, 0, t), "+z")
    p.sock("back", (s * dw / 2, 0, -t), "-z")
    p.set(door={"kind": "swing", "axis": "+y", "pivot": [0, 0, -t], "angle": 90 * s, "seconds": EVENT_SECONDS, "hang": "hang"})
    event_stats(p, kind, dw * dh)
    return p


def bulkhead(w, h):
    kind = KINDS[0]
    name = f"Bulkhead Door {w:g}x{h:g} m"
    dw, dh = w - 2 * GAP, h - 2 * GAP
    p = Part(name, SMALL, SECTION, f"A sliding steel door for a {w:g} by {h:g} m opening: goes on its jamb and slides into the wall on that side (keep {dw:g} m of wall there).",
             role="door", tool="wrench", material="steel", color="#6a6f58", group=GROUP)
    t = BULKHEAD_THICK / 2
    p.add(box((0, -dh / 2, -t), (dw, dh / 2, t)))
    for y in (-dh / 2 + 0.25, dh / 2 - 0.25):  # (two ribs across, both faces)
        p.add(box((0.06, y - 0.04, -t - 0.012), (dw - 0.06, y + 0.04, t + 0.012), "#4f5442", detail=True))
    p.add(box((dw - 0.12, -0.15, -t - 0.03), (dw - 0.06, 0.15, t + 0.03), GRIP, detail=True, use=True, paintable=False))
    hang_strip(p)
    p.sock("hang", (-GAP, 0, 0), "-x")
    p.sock("front", (dw / 2, 0, t), "+z")
    p.sock("back", (dw / 2, 0, -t), "-z")
    p.set(door={"kind": "slide", "axis": "-x", "travel": round(dw + GAP, 2), "seconds": 1.2, "hang": "hang"})
    p.stats(kind[4] * 1.5 * dw * dh, kind[5] * 1.5 * dw * dh, kind[6] + 10, 2.5)
    return p


def handle():
    p = Part("Door Handle", SMALL, SECTION, "On anything hung on a hinge: what F takes hold of. A panel works without one; this shows where.", tool="wrench", color=GRIP, group=GROUP)
    p.add(box((-0.03, 0, -0.08), (0.03, 0.012, 0.08), STEEL))
    p.add(box((-0.015, 0.012, -0.07), (0.015, 0.04, -0.04), GRIP, detail=True))
    p.add(box((-0.015, 0.012, 0.04), (0.015, 0.04, 0.07), GRIP, detail=True))
    p.add(box((-0.018, 0.04, -0.07), (0.018, 0.065, 0.07), GRIP, detail=True, use=True, paintable=False))
    p.sock("base", (0, 0, 0), "-y")
    p.stats(1.5, 40, 3, 0.5)
    return p


def main():
    parts = [("", hinge("Door Hinge", 0.5, 3, 3, 120)), ("", hinge("Long Door Hinge", 2.0, 7, 9, 240)), ("", handle())]
    for kind in KINDS:
        for w, h in OPENINGS:
            parts.append((kind[0], door(kind, w, h)))
        for w, h in EVENT_OPENINGS:
            parts.append(("Event" + kind[0], swing_door(kind, w, h)))
        for side in ("Left", "Right"):
            parts.append(("Event" + kind[0], double_leaf(kind, 1.5, 2.25, side)))
    for w, h in [(1.25, 2.0), (1.5, 2.25)]:
        parts.append(("EventSteel", bulkhead(w, h)))
    return write_parts(OUT, parts)


if __name__ == "__main__":
    sys.exit(main())
