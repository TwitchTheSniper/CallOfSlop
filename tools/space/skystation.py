#!/usr/bin/env python3
"""Generator for Maps/Space/01-SkyStation.json: "Stratos Station", the Space pack's floating cloud-mining station
(medium-large, 20 players).

    python3 tools/space/skystation.py [repo root] [plan.png]

A gas giant's upper atmosphere at dusk. Sixteen round platforms on spindle stalks float 23-30 m above a sea of clouds,
joined by railed bridges, a covered walkway and narrow catwalks. Fall off and you die in the clouds (the map's
"fallKill" height). The layout follows a node graph (the user's reference): two team clusters at the ends linked
through a central hub ring, with flanking platforms branching off. Mirror-symmetric about z = 0 (Blue holds the north,
z < 0; Red the south):

  - The Hub (0, 0): a ring deck round an open eye, with a core disc in the middle on four spokes (hill 1). East and
    west catwalks lead to the two landing pads, West Pad and East Pad (+-50, 0; hills 2 and 3).
  - The Refinery (0, -+50): the pump house among gas tanks, on the main line between the hub and the home dock; a
    covered walkway with windows runs from it to the hub.
  - North / South Dock (0, -+96): the team's home platform with the Station Control Hub (spawns, the CTF flag).
  - Quarters (-40, -+78) with the Station Crew Quarters, and the Tank Farm (-52, -+40) beyond it: the west flank, with a
    long catwalk from the Tank Farm back to the Refinery.
  - Hangar (42, -+80) with the Station Hangar Bay, and Comms (52, -+42), its antenna pad: the east flank.

How it's built (FPS first): each round deck is a cosmetic cylinder mesh over a handful of hidden collider strips (the
"Collider" op: collision only, never drawn), with a saucer underside, a bronze rim band and a stalk; railings are
instanced mesh panels with hidden collider bands (0.45-1.1 m above the deck: bullets and grenades go under them, people
don't). Bridges are yawed and tilted box meshes over chains of hidden colliders that step up 8 cm at a time. Buildings
are the general Station set (Buildings/Station*), props the Props/Station set, decals the pack's SkyStation set.
Waypoints run along every bridge so the nav graph (whose grid can miss a narrow catwalk) joins every platform.
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "01-SkyStation.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import skystation_decals as sdec  # noqa: E402

FALL_KILL = 15.0
STALK_BOTTOM = -45.0

DECK = "#a39c8e"
DECK_PAD = "#8f949a"
DECK_HUB = "#9a9488"
RIM = "#8d7458"
UNDER = "#7a705f"
UNDER2 = "#5e574c"
HULL = "#cdbfa8"
STEEL = "#6c6f73"
DARK = "#3a3c40"
GLASS = "#2e4450"
LAMP = "#ffd9a0"

OPS = []
# Hidden collider count (for the summary).
STATS = {"colliders": 0, "meshes": 0, "props": 0, "decals": 0, "waypoints": 0}


def r(v, n=3):
    return round(float(v), n)


def op(d):
    OPS.append(d)


def collider(x0, y0, z0, x1, y1, z1):
    if abs(x1 - x0) < 0.02 or abs(y1 - y0) < 0.02 or abs(z1 - z0) < 0.02:
        return
    STATS["colliders"] += 1
    op({"op": "Collider", "x0": r(min(x0, x1)), "y0": r(min(y0, y1)), "z0": r(min(z0, z1)),
        "x1": r(max(x0, x1)), "y1": r(max(y0, y1)), "z1": r(max(z0, z1))})


def mesh(kind, x, z, y, color, **kw):
    STATS["meshes"] += 1
    d = {"op": "Mesh", "mesh": kind, "x": r(x), "z": r(z), "y": r(y), "color": color}
    for k, v in kw.items():
        if v is None:
            continue
        d[k] = r(v) if isinstance(v, float) else v
    op(d)


def prop(name, x, z, y, yaw=0.0, **kw):
    STATS["props"] += 1
    d = {"op": "Prop", "name": name, "x": r(x), "z": r(z), "y": r(y)}
    if yaw:
        d["yaw"] = r(yaw % 360, 1)
    d.update(kw)
    op(d)


def decal(name, x, z, y, onto="floor", **kw):
    STATS["decals"] += 1
    d = {"op": "Decal", "decal": name, "x": r(x), "z": r(z), "y": r(y), "onto": onto}
    d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
    op(d)


def waypoint(x, z, y):
    STATS["waypoints"] += 1
    op({"op": "Waypoint", "x": r(x), "z": r(z), "y": r(y)})


# ================================================================================================ rotation helpers
# System.Numerics row vectors: v' = v * Rz(roll) * Rx(pitch) * Ry(yaw).
def rot_matrix(yaw, pitch, roll):
    a, b, c = math.radians(yaw), math.radians(pitch), math.radians(roll)
    rz = [[math.cos(c), math.sin(c), 0], [-math.sin(c), math.cos(c), 0], [0, 0, 1]]
    rx = [[1, 0, 0], [0, math.cos(b), math.sin(b)], [0, -math.sin(b), math.cos(b)]]
    ry = [[math.cos(a), 0, -math.sin(a)], [0, 1, 0], [math.sin(a), 0, math.cos(a)]]

    def mul(m, n):
        return [[sum(m[i][k] * n[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mul(mul(rz, rx), ry)


def apply(v, m):
    return [sum(v[k] * m[k][j] for k in range(3)) for j in range(3)]


def yaw_along(dx, dz):
    """Mesh yaw (degrees) that turns local +X along (dx, dz)."""
    return math.degrees(math.atan2(-dz, dx))


def prop_yaw_facing(dx, dz):
    """Prop yaw (degrees) that turns a prop's front (+Z) towards (dx, dz)."""
    return math.degrees(math.atan2(dx, dz))


def slab(cx, cz, ytop, length, width, thick, dx, dz, rise, color, material="Metal"):
    """A box mesh `length` along (dx, dz), `width` across, whose top face's centre is at (cx, ytop, cz), tilted so its
    top rises by `rise` over its length."""
    yaw = yaw_along(dx, dz)
    roll = math.degrees(math.atan2(rise, length))
    m = rot_matrix(yaw, 0, roll)
    top = apply([0, thick, 0], m)
    mesh("box", cx - top[0], cz - top[2], ytop - top[1], color, width=length / math.cos(math.radians(roll)) + 0.02,
         height=thick, depth=width, yaw=yaw, roll=roll, material=material)


# ================================================================================================ layout
class Plat:
    def __init__(self, key, name, x, z, y, R, kind="disc", inner=0.0):
        self.key, self.name, self.x, self.z, self.y, self.R, self.kind, self.inner = key, name, x, z, y, R, kind, inner
        self.gaps = []  # (angle radians, half-width metres) where bridges meet the rim


# Blue half (z < 0); the south half is the mirror image.
BLUE = [
    Plat("home", "North Dock", 0, -96, 30.0, 22),
    Plat("mid", "Refinery", 0, -50, 28.0, 14),
    Plat("w1", "Quarters", -40, -78, 29.0, 12),
    Plat("w2", "Tank Farm", -52, -40, 27.5, 10),
    Plat("e1", "Hangar", 42, -82, 29.5, 15),
    Plat("e2", "Comms", 52, -42, 27.5, 9),
]
CENTRE = [
    Plat("hub", "The Hub", 0, 0, 26.0, 20, "ring", inner=11.5),
    Plat("core", "Hub Core", 0, 0, 26.0, 7),
    Plat("padw", "West Pad", -50, 0, 25.0, 12),
    Plat("pade", "East Pad", 50, 0, 25.0, 12),
]


def mirror_plat(p):
    q = Plat(p.key.replace("", "") + "_s", p.name, p.x, -p.z, p.y, p.R, p.kind, p.inner)
    return q


PLATS = {p.key: p for p in CENTRE}
for p in BLUE:
    PLATS[p.key + "_n"] = p
    p.key = p.key + "_n"
    q = Plat(p.key[:-2] + "_s", p.name, p.x, -p.z, p.y, p.R, p.kind, p.inner)
    PLATS[q.key] = q

# (from, to, kind, width): kinds open (railed), covered (walls with windows and a roof), catwalk (narrow, railed)
BRIDGES_BLUE = [
    ("home", "mid", "open", 4.0),
    ("mid", "hub", "covered", 4.0),
    ("home", "w1", "open", 3.2),
    ("w1", "w2", "catwalk", 2.0),
    ("w2", "padw", "open", 3.0),
    ("w2", "mid", "catwalk", 1.8),
    ("home", "e1", "open", 3.2),
    ("e1", "e2", "open", 3.0),
    ("e2", "pade", "catwalk", 2.0),
]
BRIDGES_CENTRE = [
    ("hub", "padw", "catwalk", 2.2),
    ("hub", "pade", "open", 3.0),
]
SPOKES = [(0, -1), (0, 1), (1, 0), (-1, 0)]  # core to hub ring


def side_key(k, s):
    return k if k in ("hub", "core", "padw", "pade") else f"{k}_{s}"


BRIDGES = [(PLATS[side_key(a, s)], PLATS[side_key(b, s)], kind, w) for s in ("n", "s") for (a, b, kind, w) in BRIDGES_BLUE]
BRIDGES += [(PLATS[a], PLATS[b], kind, w) for (a, b, kind, w) in BRIDGES_CENTRE]


def bridge_ends(a, b):
    dx, dz = b.x - a.x, b.z - a.z
    L = math.hypot(dx, dz)
    ux, uz = dx / L, dz / L
    ra = a.R if a.kind != "ring" or True else a.R
    pa = (a.x + ux * ra, a.z + uz * ra)
    pb = (b.x - ux * b.R, b.z - uz * b.R)
    return pa, pb, ux, uz


# ================================================================================================ decks
def deck_strips(p):
    """Hidden collider strips under a disc (or ring) deck."""
    # Strips along X whose ends reach the rim band at their far edge (radius R + 0.12, hidden in the band), narrow enough
    # that their near edge still reaches the railing (R - 0.22): w (2|z| + w) <= 0.6 R. A ring's eye gets the same rule
    # against its inner radius.
    ref = min(p.R, p.inner) if p.kind == "ring" else p.R
    edges = [0.0]
    while edges[-1] < p.R + 0.1:
        z = edges[-1]
        w = 1.5
        while w > 0.12 and w * (2 * z + w) > 0.6 * ref:
            w *= 0.85
        edges.append(min(z + w, p.R + 0.12))
    pos = edges
    bands = [(-b1, -b0) for (b0, b1) in zip(pos[:-1], pos[1:])][::-1] + list(zip(pos[:-1], pos[1:]))
    Rc = p.R + 0.12
    y0, y1 = p.y - 1.0, p.y
    for (z0, z1) in bands:
        far = max(abs(z0), abs(z1))
        if far >= Rc:
            far = Rc - 0.01
        half = math.sqrt(max(0.0, Rc * Rc - far * far))
        if half < 0.3:
            continue
        if p.kind == "ring":
            ri = p.inner - 0.06
            if far < ri:
                hole = math.sqrt(ri * ri - far * far)
                collider(p.x - half, y0, p.z + z0, p.x - hole, y1, p.z + z1)
                collider(p.x + hole, y0, p.z + z0, p.x + half, y1, p.z + z1)
                continue
        collider(p.x - half, y0, p.z + z0, p.x + half, y1, p.z + z1)


def deck_visual(p, color):
    seg = 64 if p.R > 12 else 48
    top = p.y + 0.02
    if p.kind == "ring":
        mesh("tube", p.x, p.z, top - 1.2, color, radius=p.R, inner=p.inner, height=1.2, segments=seg, material="Metal")
        mesh("tube", p.x, p.z, top - 1.35, RIM, radius=p.inner + 0.12, inner=p.inner - 0.1, height=1.4, segments=seg)
    else:
        mesh("cylinder", p.x, p.z, top - 1.2, color, width=2 * p.R, height=1.2, depth=2 * p.R, segments=seg, material="Metal")
    # Bronze rim band, a little proud of the deck.
    mesh("tube", p.x, p.z, top - 1.5, RIM, radius=p.R + 0.18, inner=p.R - 0.04, height=1.55, segments=seg, material="Metal")
    # Saucer underside: a wide shallow cone pointing down, a darker collar and a stalk into the clouds.
    depth = 0.42 * p.R if p.kind != "ring" else 0.25 * p.R
    base = top - 1.5
    if p.kind == "ring":
        mesh("tube", p.x, p.z, base - 1.6, UNDER, radius=p.R * 0.96, inner=p.inner + 0.5, height=1.6, segments=seg)
        mesh("cone", p.x, p.z, base, UNDER, width=2 * p.R * 0.94, height=depth, depth=2 * p.R * 0.94, segments=seg, pitch=180.0)
    else:
        mesh("cone", p.x, p.z, base, UNDER, width=2 * p.R * 0.96, height=depth, depth=2 * p.R * 0.96, segments=seg, pitch=180.0)
    mesh("cylinder", p.x, p.z, base - depth * 0.55, UNDER2, width=p.R * 0.55, height=depth * 0.5, depth=p.R * 0.55, segments=24)
    if p.key != "core":
        tip = base - depth
        sw = 0.9 if p.R >= 14 else 0.6
        mesh("cylinder", p.x, p.z, STALK_BOTTOM, STEEL, width=sw, height=tip - STALK_BOTTOM + 0.5, depth=sw, segments=12)
        mesh("cone", p.x, p.z, STALK_BOTTOM, STEEL, width=sw * 2.6, height=5.0, depth=sw * 2.6, segments=12, pitch=180.0)
        mesh("sphere", p.x, p.z, STALK_BOTTOM - 6.2, "#ffb24a", width=1.2, height=1.2, depth=1.2, segments=12, detail=6)
    # Window lights round the underside collar (small glowing-looking boxes).
    if p.R >= 12:
        n = int(p.R * 1.2)
        for k in range(n):
            a = 2 * math.pi * k / n
            rr = p.R * 0.9
            mesh("box", p.x + rr * math.cos(a), p.z + rr * math.sin(a), base - depth * 0.12 - 0.35, LAMP,
                 width=0.9, height=0.22, depth=0.3, yaw=math.degrees(-a) + 90)


# ================================================================================================ railings
RAIL_LO, RAIL_HI = 0.45, 1.1


def rail_collider_line(x0, z0, ya, x1, z1, yb, piece=0.7, t=0.12):
    """Hidden collider band along a line (a railing), cut into short pieces so each AABB stays thin."""
    L = math.hypot(x1 - x0, z1 - z0)
    n = max(1, int(math.ceil(L / piece)))
    if abs(x1 - x0) < 0.01 or abs(z1 - z0) < 0.01:
        # Axis-aligned: still cut where the height changes.
        n = max(1, int(math.ceil(abs(yb - ya) / 0.15)))
    for k in range(n):
        f0, f1 = k / n, (k + 1) / n
        ax, az = x0 + (x1 - x0) * f0, z0 + (z1 - z0) * f0
        bx, bz = x0 + (x1 - x0) * f1, z0 + (z1 - z0) * f1
        y = ya + (yb - ya) * (f0 + f1) / 2
        collider(min(ax, bx) - t / 2, y + RAIL_LO, min(az, bz) - t / 2, max(ax, bx) + t / 2, y + RAIL_HI, max(az, bz) + t / 2)


def rail_visual_line(x0, z0, ya, x1, z1, yb, posts=True, post_every=2.5):
    L = math.hypot(x1 - x0, z1 - z0)
    if L < 0.2:
        return
    dx, dz = (x1 - x0) / L, (z1 - z0) / L
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    ym = (ya + yb) / 2
    slab(cx, cz, ym + 1.0, L, 0.05, 0.55, dx, dz, yb - ya, HULL2_PANEL, material="")
    slab(cx, cz, ym + 1.12, L, 0.14, 0.1, dx, dz, yb - ya, RIM, material="Metal")
    if posts:
        n = max(1, int(round(L / post_every)))
        for k in range(n + 1):
            f = k / n
            mesh("cylinder", x0 + (x1 - x0) * f, z0 + (z1 - z0) * f, ya + (yb - ya) * f, STEEL, width=0.1, height=1.12, depth=0.1, segments=8)


HULL2_PANEL = "#b8ad99"


def kept_arcs(a0, a1, gaps, rr):
    """The parts of the arc a0..a1 (radians) outside every gap (angle, half-width in metres at radius rr)."""
    arcs = [(a0, a1)]
    for (ga, hw) in gaps:
        h = hw / rr
        out = []
        for (b0, b1) in arcs:
            # The gap's angle brought next to this arc.
            g = ga + 2 * math.pi * round(((b0 + b1) / 2 - ga) / (2 * math.pi))
            g0, g1 = g - h, g + h
            if g1 <= b0 or g0 >= b1:
                out.append((b0, b1))
                continue
            if g0 > b0:
                out.append((b0, g0))
            if g1 < b1:
                out.append((g1, b1))
        arcs = out
    return [(b0, b1) for (b0, b1) in arcs if (b1 - b0) * rr > 0.08]


def ring_railing(cx, cz, y, rr, gaps):
    n = max(12, int(round(2 * math.pi * rr / 2.0)))
    step = 2 * math.pi / n
    for k in range(n):
        for (a0, a1) in kept_arcs(k * step, (k + 1) * step, gaps, rr):
            x0, z0 = cx + rr * math.cos(a0), cz + rr * math.sin(a0)
            x1, z1 = cx + rr * math.cos(a1), cz + rr * math.sin(a1)
            rail_visual_line(x0, z0, y, x1, z1, y, post_every=10)
            rail_collider_line(x0, z0, y, x1, z1, y)


def rim_railing(p):
    """Railing round a deck's rim, cut exactly where bridges and spokes meet it."""
    ring_railing(p.x, p.z, p.y, p.R - 0.22, p.gaps)
    if p.kind == "ring":
        ring_railing(p.x, p.z, p.y, p.inner + 0.22, p.inner_gaps)


# ================================================================================================ bridges
def bridge(a, b, kind, w, ya=None, yb=None, ends=None):
    """A bridge from platform a's rim to b's rim (or between explicit end points)."""
    if ends is None:
        (ax, az), (bx, bz), ux, uz = bridge_ends(a, b)
    else:
        (ax, az), (bx, bz) = ends
        L0 = math.hypot(bx - ax, bz - az)
        ux, uz = (bx - ax) / L0, (bz - az) / L0
    ya = a.y if ya is None else ya
    yb = b.y if yb is None else yb
    L = math.hypot(bx - ax, bz - az)
    # Record the rim gaps (half-width plus a little).
    if ends is None:
        a.gaps.append((math.atan2(uz, ux), w / 2 + 0.12))
        b.gaps.append((math.atan2(-uz, -ux), w / 2 + 0.12))
    px, pz = -uz, ux  # across
    # Overlap 0.7 m into each deck (at the deck's height) so the joint is seamless.
    ox0, oz0 = ax - ux * 0.7, az - uz * 0.7
    ox1, oz1 = bx + ux * 0.7, bz + uz * 0.7
    collider_bridge(ox0, oz0, ya, ax, az, ya, w)
    collider_bridge(bx, bz, yb, ox1, oz1, yb, w)
    collider_bridge(ax, az, ya, bx, bz, yb, w)
    cx, cz = (ax + bx) / 2, (az + bz) / 2
    ym = (ya + yb) / 2
    color = DECK if kind != "catwalk" else "#8c8a86"
    thick = 0.45 if kind != "catwalk" else 0.25
    slab(cx, cz, ym + 0.02, L + 0.6, w, thick, ux, uz, yb - ya, color, material="Grid" if kind == "catwalk" else "Metal")
    # Underside: a spine girder and side stringers.
    slab(cx, cz, ym - thick + 0.02, L + 0.4, w * 0.35, 0.7 if kind != "catwalk" else 0.4, ux, uz, yb - ya, UNDER2, material="Metal")
    for s in (-1, 1):
        sx, sz = px * s * (w / 2 - 0.05), pz * s * (w / 2 - 0.05)
        slab(cx + sx, cz + sz, ym + 0.02 - thick * 0.3, L + 0.6, 0.12, thick + 0.2, ux, uz, yb - ya, RIM, material="Metal")
    edge = w / 2 - 0.1
    for s in (-1, 1):
        x0, z0 = ax + px * s * edge, az + pz * s * edge
        x1, z1 = bx + px * s * edge, bz + pz * s * edge
        if kind == "covered":
            covered_wall(x0, z0, ya, x1, z1, yb, s, ux, uz)
        else:
            rail_visual_line(x0, z0, ya, x1, z1, yb, post_every=2.5 if kind == "open" else 3.0)
            rail_collider_line(x0, z0, ya, x1, z1, yb)
    if kind == "covered":
        covered_roof(ax, az, ya, bx, bz, yb, w, ux, uz)
    # Waypoints along the middle every ~4 m (the nav grid can miss a narrow bridge).
    n = max(1, int(math.ceil(L / 4.0)))
    for k in range(n + 1):
        f = k / n
        waypoint(ax + (bx - ax) * f, az + (bz - az) * f, ya + (yb - ya) * f)
    # Hazard stripes at both mouths.
    for (mx, mz, my, sgn) in ((ax, az, ya, 1), (bx, bz, yb, -1)):
        decal("global:Hazard Stripes", mx + ux * sgn * 0.6, mz + uz * sgn * 0.6, my, size=min(w * 0.9, 3.0),
              rotation=r((yaw_along(ux, uz) + 90) % 180, 1))


def collider_bridge(x0, z0, y0, x1, z1, y1, w):
    """Hidden deck colliders from (x0, z0) at height y0 to (x1, z1) at y1, `w` wide: pieces that step up at most 8 cm."""
    L = math.hypot(x1 - x0, z1 - z0)
    if L < 0.05:
        return
    ux, uz = (x1 - x0) / L, (z1 - z0) / L
    axis = abs(ux) < 0.01 or abs(uz) < 0.01
    rise = abs(y1 - y0)
    if axis:
        n = max(1, int(math.ceil(rise / 0.08)))
        for k in range(n):
            f0, f1 = k / n, (k + 1) / n
            ax, az = x0 + (x1 - x0) * f0, z0 + (z1 - z0) * f0
            bx, bz = x0 + (x1 - x0) * f1, z0 + (z1 - z0) * f1
            y = min(y0 + (y1 - y0) * f0, y0 + (y1 - y0) * f1)  # the lower end: the drawn slope stays on top
            hx = w / 2 * abs(uz) - 0.04 if abs(uz) > 0.5 else 0
            hz = w / 2 * abs(ux) - 0.04 if abs(ux) > 0.5 else 0
            collider(min(ax, bx) - hx, y - 0.5, min(az, bz) - hz, max(ax, bx) + hx, y, max(az, bz) + hz)
        return
    # Diagonal: thin slices across the dominant axis, each covering the part of the strip inside it (inscribed), its
    # top the lowest height of the drawn deck over the slice (so feet sink a few cm at most on these gentle slopes).
    alongx = abs(ux) >= abs(uz)
    t = 0.45
    half = w / 2 - 0.04
    if alongx:
        lo_, hi_ = min(x0, x1), max(x0, x1)
    else:
        lo_, hi_ = min(z0, z1), max(z0, z1)
    n = max(1, int(math.ceil((hi_ - lo_) / t)))
    for k in range(n):
        a_, b_ = lo_ + (hi_ - lo_) * k / n, lo_ + (hi_ - lo_) * (k + 1) / n
        # Centre line coordinate (the other axis) and along-bridge distance at both slice edges.
        def cross_at(v):
            f = (v - (x0 if alongx else z0)) / ((x1 - x0) if alongx else (z1 - z0))
            return (z0 + (z1 - z0) * f) if alongx else (x0 + (x1 - x0) * f), f
        ca, fa = cross_at(a_)
        cb, fb = cross_at(b_)
        hw = half / (abs(ux) if alongx else abs(uz))  # half-width measured along the other axis
        c0 = max(ca, cb) - hw
        c1 = min(ca, cb) + hw
        if c1 - c0 < 0.3:
            continue
        # Lowest drawn height over the slice: at one of its corners.
        hs = []
        for v in (a_, b_):
            for c in (c0, c1):
                px, pz = (v, c) if alongx else (c, v)
                f = ((px - x0) * ux + (pz - z0) * uz) / L
                hs.append(y0 + (y1 - y0) * min(1.0, max(0.0, f)))
        y = min(hs) - 0.01
        if alongx:
            collider(a_, y - 0.5, c0, b_, y, c1)
        else:
            collider(c0, y - 0.5, a_, c1, y, b_)


def covered_wall(x0, z0, ya, x1, z1, yb, s, ux, uz):
    """One wall of the covered walkway (axis-aligned only): solid to 1.1 m, open windows 1.1-2.2 m, solid to 2.8 m."""
    L = math.hypot(x1 - x0, z1 - z0)
    dx, dz = (x1 - x0) / L, (z1 - z0) / L
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    ym = (ya + yb) / 2
    slab(cx, cz, ym + 1.1, L + 0.4, 0.2, 1.1, dx, dz, yb - ya, HULL, material="Metal")
    slab(cx, cz, ym + 2.9, L + 0.4, 0.2, 0.7, dx, dz, yb - ya, HULL, material="Metal")
    n = max(1, int(round(L / 2.2)))
    for k in range(n + 1):
        f = k / n
        mesh("box", x0 + (x1 - x0) * f, z0 + (z1 - z0) * f, ya + (yb - ya) * f + 1.1, RIM, width=0.22, height=1.1, depth=0.22)
    # Colliders: the lower wall and the upper wall, stepped with the floor.
    m = max(1, int(math.ceil(abs(yb - ya) / 0.15)))
    for k in range(m):
        f0, f1 = k / m, (k + 1) / m
        axx, azz = x0 + (x1 - x0) * f0, z0 + (z1 - z0) * f0
        bxx, bzz = x0 + (x1 - x0) * f1, z0 + (z1 - z0) * f1
        y = ya + (yb - ya) * (f0 + f1) / 2
        collider(min(axx, bxx) - 0.1, y, min(azz, bzz) - 0.1, max(axx, bxx) + 0.1, y + 1.1, max(azz, bzz) + 0.1)
        collider(min(axx, bxx) - 0.1, y + 2.2, min(azz, bzz) - 0.1, max(axx, bxx) + 0.1, y + 2.9, max(azz, bzz) + 0.1)


def covered_roof(ax, az, ya, bx, bz, yb, w, ux, uz):
    L = math.hypot(bx - ax, bz - az)
    cx, cz = (ax + bx) / 2, (az + bz) / 2
    ym = (ya + yb) / 2
    slab(cx, cz, ym + 3.15, L + 0.6, w + 0.6, 0.25, ux, uz, yb - ya, RIM, material="Metal")
    slab(cx, cz, ym + 3.35, L + 0.4, w * 0.6, 0.2, ux, uz, yb - ya, HULL, material="Metal")
    m = max(1, int(math.ceil(abs(yb - ya) / 0.15)))
    for k in range(m):
        f0, f1 = k / m, (k + 1) / m
        axx, azz = ax + (bx - ax) * f0, az + (bz - az) * f0
        bxx, bzz = ax + (bx - ax) * f1, az + (bz - az) * f1
        y = ya + (yb - ya) * (f0 + f1) / 2
        hx = w / 2 + 0.2 if abs(uz) > 0.5 else 0
        hz = w / 2 + 0.2 if abs(ux) > 0.5 else 0
        collider(min(axx, bxx) - hx, y + 2.9, min(azz, bzz) - hz, max(axx, bxx) + hx, y + 3.15, max(azz, bzz) + hz)
    # Lamps along the ceiling.
    n = max(1, int(L / 4))
    for k in range(n):
        f = (k + 0.5) / n
        mesh("box", ax + (bx - ax) * f, az + (bz - az) * f, ya + (yb - ya) * f + 2.82, LAMP, width=0.5, height=0.06, depth=0.5)


# ================================================================================================ round solids
def round_collider(x, z, rad, y0, y1):
    """Three hidden boxes approximating an upright cylinder (an octagon-ish cross plus a square)."""
    a = rad * 0.96
    b = rad * 0.41
    c = rad * 0.70
    collider(x - a, y0, z - b, x + a, y1, z + b)
    collider(x - b, y0, z - a, x + b, y1, z + a)
    collider(x - c, y0, z - c, x + c, y1, z + c)


def dome_tower(x, z, y, rad, drum_h, color=HULL, light=True):
    """A drum with a dome on top (solid drum, cosmetic dome), standing at height y."""
    mesh("cylinder", x, z, y, color, width=2 * rad, height=drum_h, depth=2 * rad, segments=32, material="Metal")
    mesh("tube", x, z, y + drum_h - 0.5, RIM, radius=rad + 0.15, inner=rad - 0.1, height=0.5, segments=32)
    mesh("sphere", x, z, y + drum_h - rad * 0.5, HULL, width=2 * rad * 0.98, height=rad * 1.3, depth=2 * rad * 0.98, segments=32, detail=12)
    mesh("cylinder", x, z, y + drum_h + rad * 0.62, RIM, width=rad * 0.5, height=0.5, depth=rad * 0.5, segments=16)
    if light:
        mesh("sphere", x, z, y + drum_h + rad * 0.62 + 0.5, "#ff5a40", width=0.5, height=0.5, depth=0.5, segments=10, detail=5)
    n = 12
    for k in range(n):
        a = 2 * math.pi * k / n
        mesh("box", x + (rad + 0.02) * math.cos(a), z + (rad + 0.02) * math.sin(a), y + drum_h * 0.55, LAMP,
             width=0.1, height=0.5, depth=0.7, yaw=math.degrees(-a))
    round_collider(x, z, rad, y, y + drum_h)


def tank_mesh(x, z, y, rad, h):
    """A big cosmetic gas tank (cylinder with a domed top) with a solid core."""
    mesh("cylinder", x, z, y, HULL, width=2 * rad, height=h, depth=2 * rad, segments=24, material="Metal")
    mesh("sphere", x, z, y + h - rad * 0.4, "#bfb19a", width=2 * rad * 0.99, height=rad * 0.8, depth=2 * rad * 0.99, segments=24, detail=10)
    for f in (0.2, 0.62):
        mesh("tube", x, z, y + h * f, RIM, radius=rad + 0.08, inner=rad - 0.05, height=0.3, segments=24)
    round_collider(x, z, rad, y, y + h)


def halo_ring(x, z, y, rad, struts):
    """A cosmetic ring round a building at roof level, held by struts to its walls."""
    mesh("torus", x, z, y, RIM, radius=rad, inner=0.32, segments=48, detail=8)
    for (sx, sz, ex, ez) in struts:
        L = math.hypot(ex - sx, ez - sz)
        mesh("box", (sx + ex) / 2, (sz + ez) / 2, y + 0.05, STEEL, width=L, height=0.25, depth=0.3, yaw=yaw_along(ex - sx, ez - sz))


# ================================================================================================ platform dressing
def mz(z, s):
    return z if s == "n" else -z


def myaw(yaw, s):
    return yaw if s == "n" else 180 - yaw


def place_building(name, x, z, y, yaw, s, seed, spawns=False, palette=None):
    v = {}
    if s == "s":
        yaw = (180 - yaw) % 360  # a reflection in z is a turn of 180 - yaw plus an east-west mirror
        v["mirror"] = True
    if palette:
        v["palette"] = palette
    d = {"op": "Building", "name": name, "at": [r(x), r(y), r(mz(z, s))], "yaw": yaw, "seed": seed}
    if spawns:
        d["spawns"] = True
    if v:
        d["variant"] = v
    op(d)


SPAWNS = {"n": [], "s": []}


def spawn(x, z, y, s):
    SPAWNS[s].append([r(x), r(y), r(mz(z, s))])
    op({"op": "Spawn", "x": r(x), "z": r(mz(z, s)), "y": r(y)})


def dress_home(s):
    p = PLATS["home_" + s]
    y = p.y
    team = "North" if s == "n" else "South"
    # The Station Control Hub (16 x 12), its front towards the Refinery; roof top at y + 7.0.
    bz = -102.0
    place_building("Station Control Hub", 0, bz, y, 0, s, 611 if s == "n" else 612, spawns=True)
    roof = y + 0.1 + 3.4 * 2 + 0.1
    halo_ring(0, mz(bz, s), roof - 0.6, 11.2, [(8.0, mz(bz, s), 10.9, mz(bz, s)), (-8.0, mz(bz, s), -10.9, mz(bz, s)),
                                             (0, mz(bz - 6.0, s), 0, mz(bz - 10.9, s)), (0, mz(bz + 6.0, s), 0, mz(bz + 10.9, s))])
    dome_tower(-3.0, mz(bz, s), roof, 2.6, 2.9)
    prop("Antenna Mast", 5.0, mz(bz + 3.5, s), roof - 0.1, myaw(0, s))
    # Deck dressing: cargo cover, lamps, landing marks.
    for (x, z, n, yw) in [(-12, -88, "Station Cargo Pod", 90), (12, -88, "Station Cargo Pod", 90), (-15, -98, "Supply Case Stack", 0),
                          (15, -98, "Supply Case Stack", 0), (-7, -80, "Station Deck Barrier", 0), (7, -80, "Station Deck Barrier", 0),
                          (-17, -106, "Twin Gas Tanks", 90), (17, -106, "Squat Pressure Tank", 0), (-10, -113, "Fuel Coupling", 180),
                          (10, -113, "Station Supply Case", 30)]:
        prop(n, x, mz(z, s), y, myaw(yw, s))
    pb = PLATS["home_n"]
    for a in range(0, 360, 45):
        rr = pb.R - 1.4
        ax, az = pb.x + rr * math.cos(math.radians(a)), pb.z + rr * math.sin(math.radians(a))
        if abs(ax) < 3:
            continue  # the bridge mouths
        prop("Deck Light Post", ax, mz(az, s), y, myaw(prop_yaw_facing(pb.x - ax, pb.z - az), s))
    decal(f"Stratos {team} Mark", 0, mz(-86, s), y, size=5.0, rotation=0 if s == "n" else 180)
    decal("Halcyon Logo", -9.0, mz(-96, s), y, size=3.0, rotation=0 if s == "n" else 180)
    names, tw, th = sdec.tiles("north" if s == "n" else "south")
    sign_on_wall(names, tw, 0.0, mz(bz + 6.6, s), y + 3.1, "wall-z" if s == "n" else "wall+z", s)
    # Spawns: round the back and sides of the hub building, on the deck (plus its three spawn anchors).
    for (x, z) in [(-14, -94), (14, -94), (-12, -110), (12, -110), (-4, -112), (4, -112), (-17, -100), (17, -100)]:
        spawn(x, z, y, s)


def sign_on_wall(names, tw, cx, z, y, onto, s):
    n = len(names)
    for i, nm in enumerate(names):
        off = (i - (n - 1) / 2) * tw
        x = cx + off if s == "n" else cx - off
        decal(nm, x, z, y, onto=onto, size=tw, reach=2.0)


def dress_mid(s):
    p = PLATS["mid_" + s]
    y = p.y
    # The Refinery Pump House off to the west, tanks round the east, pipe runs between: the deck is cluttered cover.
    place_building("Refinery Pump House", -5.5, -51.0, y, 90 if s == "n" else 90, s, 621)
    for (x, z, rad, h) in [(6.5, -56.5, 2.2, 7.0), (8.5, -47.0, 1.8, 5.5)]:
        tank_mesh(x, mz(z, s), y, rad, h)
    for (x, z, n, yw) in [(3.0, -44.0, "Station Pipe Run", 90), (2.0, -58.0, "Station Pipe Run", 0), (-2.5, -61.0, "Volatile Gas Canister Rack", 0),
                          (11.0, -52.5, "Station Pipe Riser", 90), (-9.5, -43.5, "Gas Refinery Tank", 0), (3.0, -50.5, "Squat Pressure Tank", 0),
                          (-3.0, -40.0, "Station Supply Case", 15)]:
        prop(n, x, mz(z, s), y, myaw(yw, s))
    names, tw, th = sdec.tiles("refinery")
    decal("global:Oil Stain", 4.0, mz(-50.5, s), y, size=2.2)
    decal("global:Rust Stain", -1.0, mz(-57, s), y, size=1.5)


def dress_w1(s):
    p = PLATS["w1_" + s]
    y = p.y
    place_building("Station Crew Quarters", -40.0, -79.5, y, 0, s, 631, palette="white hull")
    for (x, z, n, yw) in [(-48.5, -74, "Station Cargo Pod", 0), (-31.5, -84, "Supply Case Stack", 90), (-32, -71, "Deck Light Post", 0),
                          (-48, -86, "Deck Light Post", 0), (-40, -71.5, "Station Deck Barrier", 0)]:
        prop(n, x, mz(z, s), y, myaw(yw, s))
    names, tw, th = sdec.tiles("quarters")
    sign_on_wall(names, tw, -40.0, mz(-73.9, s), y + 2.75, "wall-z" if s == "n" else "wall+z", s)
    spawn(-46, -71, y, s)
    spawn(-34, -87, y, s)


def dress_w2(s):
    p = PLATS["w2_" + s]
    y = p.y
    for (x, z, rad, h) in [(-55.5, -43.5, 2.0, 6.0), (-48.0, -45.0, 1.6, 4.5), (-56.0, -35.0, 1.5, 4.0)]:
        tank_mesh(x, mz(z, s), y, rad, h)
    for (x, z, n, yw) in [(-46.5, -35.5, "Station Pipe Run", 45), (-57.5, -39.5, "Volatile Gas Canister Rack", 90),
                          (-55.0, -47.5, "Station Pipe Riser", 0)]:
        prop(n, x, mz(z, s), y, myaw(yw, s))
    decal("global:Oil Stain", -51, mz(-41, s), y, size=2.0)


def dress_e1(s):
    p = PLATS["e1_" + s]
    y = p.y
    place_building("Station Hangar Bay", 42.0, -83.0, y, 0, s, 641, palette="bronze hull")
    roof = y + 0.1 + 6.6 + 0.1
    mesh("cylinder", 42.0, mz(-83.0, s), roof, RIM, width=6, height=0.5, depth=6, segments=24)
    mesh("sphere", 42.0, mz(-83.0, s), roof - 1.4, HULL, width=5.6, height=3.6, depth=5.6, segments=24, detail=10)
    for (x, z, n, yw) in [(36.5, -71.5, "Hover Cargo Sled", 0), (31.5, -74.5, "Station Cargo Pod", 90), (54.5, -86, "Fuel Coupling", 270),
                          (31, -90, "Deck Light Post", 0), (53, -90, "Deck Light Post", 0)]:
        prop(n, x, mz(z, s), y, myaw(yw, s))
    names, tw, th = sdec.tiles("hangar")
    sign_on_wall(names, tw, 42.0, mz(-75.4, s), y + 6.15, "wall-z" if s == "n" else "wall+z", s)
    spawn(48, -71.5, y, s)
    spawn(54.5, -80, y, s)


def dress_e2(s):
    p = PLATS["e2_" + s]
    y = p.y
    for (x, z, n, yw) in [(52, -42, "Antenna Mast", 0), (48, -46, "Sensor Dish", 0), (56.5, -39, "Station Server Cabinet", 270),
                          (55.5, -45.5, "Station Console", 225), (47, -38, "Station Supply Case", 0)]:
        prop(n, x, mz(z, s), y, myaw(yw, s))
    dome_tower(55.0, mz(-36.5, s), y, 1.6, 2.4, light=False)


def dress_centre():
    hub, core = PLATS["hub"], PLATS["core"]
    y = hub.y
    # Core: open deck for the hill (a glowing ring painted on it), cover round the edge.
    for (x, z, n, yw) in [(3.6, 3.6, "Station Cargo Pod", 45), (-3.6, -3.6, "Station Cargo Pod", 45),
                          (-3.8, 3.4, "Station Supply Case", 0), (3.8, -3.4, "Station Supply Case", 0)]:
        prop(n, x, z, y, yw)
    decal("Stratos Pad Ring", 0, 0, y, size=12.5)
    # Hub ring: tanks and pods between the spokes, the hub sign, lamps.
    for k in range(4):
        a = math.radians(45 + 90 * k)
        rr = 15.8
        x, z = rr * math.cos(a), rr * math.sin(a)
        tank_mesh(x, z, y, 1.7, 4.5)
        for off in (-0.33, 0.33):
            b = a + off
            prop("Station Deck Barrier", 15.8 * math.cos(b), 15.8 * math.sin(b), y, math.degrees(-b) + 90)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        prop("Deck Light Post", 18.6 * math.cos(a), 18.6 * math.sin(a), y, prop_yaw_facing(-math.cos(a), -math.sin(a)))
    names, tw, th = sdec.tiles("hub")
    # Flank pads: landing pads with markings, a parked skimmer, beacons on the rim (hills 2 and 3).
    for key, sx in (("padw", -1), ("pade", 1)):
        p = PLATS[key]
        decal("Stratos Pad Ring", p.x, p.z, p.y, size=15.5)
        decal("Stratos Pad Glyph", p.x, p.z, p.y, size=7.5, rotation=90 if sx > 0 else 270)
        decal("Stratos Pad Number 1" if sx < 0 else "Stratos Pad Number 2", p.x - sx * 5.5, p.z, p.y, size=2.4, rotation=90 if sx < 0 else 270)
        prop("Parked Cloud Skimmer", p.x + sx * 6.5, p.z, p.y, 90)
        prop("Station Cargo Pod", p.x + sx * 2.0, p.z - 4.8, p.y, 0)
        prop("Station Supply Case", p.x - sx * 2.2, p.z - 5.6, p.y, 20)
        for k in range(8):
            a = math.radians(22.5 + 45 * k)
            prop("Deck Floor Light", p.x + 10.9 * math.cos(a), p.z + 10.9 * math.sin(a), p.y)
        prop("Landing Beacon", p.x + sx * 8.2, p.z - 5.0, p.y)
        prop("Landing Beacon", p.x + sx * 8.2, p.z + 5.0, p.y)


# ================================================================================================ clouds
def clouds():
    import random
    rng = random.Random(7)
    shapes = [("sphere", 26, 7, 18), ("sphere", 16, 5, 12), ("sphere", 38, 9, 26)]
    cols = ["#cfa7a6", "#b996a8", "#d8b0a0", "#a48aa6"]
    for i in range(110):
        x = rng.uniform(-150, 150)
        z = rng.uniform(-200, 200)
        kind, w, h, d = shapes[i % 3]
        y = rng.uniform(-2, 9)
        mesh(kind, x, z, y, cols[i % 4], width=w, height=h, depth=d, segments=16, detail=8, yaw=rng.uniform(0, 180),
             scale=rng.uniform(0.8, 1.5))


# ================================================================================================ the map
def boundary_points():
    half = [(-68, 0), (-66, -22), (-68, -46), (-60, -70), (-54, -92), (-34, -104), (-20, -122), (0, -126),
            (20, -122), (36, -106), (60, -100), (64, -70), (66, -46), (68, -22), (68, 0)]
    pts = list(half)
    pts += [(x, -z) for (x, z) in reversed(half[1:-1])]
    return [[x, z] for (x, z) in pts]


def build():
    # Spokes from the core to the hub ring (level).
    hub, core = PLATS["hub"], PLATS["core"]
    hub.inner_gaps = []
    for (sx, sz) in SPOKES:
        a = math.atan2(sz, sx)
        core.gaps.append((a, 1.32))
        hub.inner_gaps.append((a, 1.32))
        bridge(core, hub, "open", 2.4, ends=((sx * core.R, sz * core.R), (sx * hub.inner, sz * hub.inner)))
    for (a, b, kind, w) in BRIDGES:
        bridge(a, b, kind, w)
    for p in PLATS.values():
        if p.kind == "ring":
            deck_strips(p)
            deck_visual(p, DECK_HUB)
        else:
            deck_strips(p)
            deck_visual(p, DECK_PAD if p.key in ("padw", "pade") else DECK)
        rim_railing(p)
        # Deck plate seams.
        if p.key != "core":
            op({"op": "DecalScatter", "decals": ["Stratos Deck Panels"], "x0": r(p.x - p.R * 0.7), "z0": r(p.z - p.R * 0.7),
                "x1": r(p.x + p.R * 0.7), "z1": r(p.z + p.R * 0.7), "count": int(p.R * p.R / 14), "seed": int(abs(p.x * 7 + p.z)), "y": p.y,
                "gap": 3.4})
    for s in ("n", "s"):
        dress_home(s)
        dress_mid(s)
        dress_w1(s)
        dress_w2(s)
        dress_e1(s)
        dress_e2(s)
    dress_centre()
    clouds()
    # A few platform waypoints (the grid gets the decks; these anchor the middles).
    for p in PLATS.values():
        if p.kind != "ring":
            waypoint(p.x + (2.5 if p.key == "core" else 0), p.z + (2.5 if p.key == "core" else (p.R * 0.55 if p.z < 0 else -p.R * 0.55)), p.y)


def main():
    build()
    home_n, home_s = PLATS["home_n"], PLATS["home_s"]
    m = {
        "format": "call-of-slop-map/1",
        "name": "Stratos Station",
        "description": "A cloud-mining station of round platforms and bridges above a sea of clouds at dusk. Mind the drop.",
        "author": "Call of Slop",
        "order": 601,
        "destructible": False,
        "fallKill": FALL_KILL,
        "maxPlayers": 20,
        "skybox": {"image": "Skies/stratos_dusk.png", "exposure": 1.0, "sun": {"elevation": 4.5, "azimuth": 262, "color": "#ffc890", "intensity": 1.05},
                   "ambient": 1.15},
        "boundary": {"shape": "smooth", "points": boundary_points(), "edge": "none", "barrierHeight": 1.0},
        "teamSpawns": [SPAWNS["n"], SPAWNS["s"]],
        "hills": [{"x": 0, "z": 0, "y": PLATS["core"].y, "radius": 4.5},
                  {"x": -47.5, "z": 0, "y": PLATS["padw"].y, "radius": 4.5},
                  {"x": 47.5, "z": 0, "y": PLATS["pade"].y, "radius": 4.5}],
        "flags": [[0, home_n.y, -86], [0, home_s.y, 86]],
        "ops": OPS,
    }
    with open(OUT_JSON, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print(f"wrote {OUT_JSON}: {len(OPS)} ops, {STATS}")
    if OUT_PNG:
        plan(OUT_PNG)


def plan(path):
    from PIL import Image, ImageDraw
    S = 4
    W, H = 160 * S, 270 * S
    img = Image.new("RGB", (W, H), (40, 36, 60))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * S, H / 2 + z * S)
    bp = boundary_points()
    d.polygon([P(x, z) for x, z in bp], outline=(120, 120, 160))
    for o in OPS:
        if o["op"] == "Collider":
            a, b = P(o["x0"], o["z0"]), P(o["x1"], o["z1"])
            y = o["y1"]
            c = int(80 + (y - 20) * 12)
            d.rectangle([a, b], fill=(c, c, c - 20))
    for o in OPS:
        if o["op"] == "Waypoint":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 2, z - 2, x + 2, z + 2], fill=(80, 200, 255))
        if o["op"] == "Spawn":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 3, z - 3, x + 3, z + 3], fill=(80, 255, 80) if o["z"] < 0 else (255, 80, 80))
        if o["op"] == "Prop":
            x, z = P(o["x"], o["z"])
            d.rectangle([x - 2, z - 2, x + 2, z + 2], fill=(255, 200, 60))
        if o["op"] == "Building":
            x, z = P(o["at"][0], o["at"][2])
            d.rectangle([x - 6 * S, z - 5 * S, x + 6 * S, z + 5 * S], outline=(255, 120, 255))
    img.save(path)
    print("plan:", path)


if __name__ == "__main__":
    main()
