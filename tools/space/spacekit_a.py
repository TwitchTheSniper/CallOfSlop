#!/usr/bin/env python3
"""Shared helpers for the Space pack's generated maps (tools/space/keelspar_drydock.py, tools/space/regolith_reach.py; the "_a" suffix keeps the file apart from the other Space generators):
an op list with counters, hidden colliders, instanced meshes placed by their centre (whatever their turn), sloped
slabs, railings (hidden collider bands plus drawn panels), stairs (one hidden collider and one instanced mesh box per
step), props, decals, waypoints and spawns, and the 180-degree rotation used to build a fair map from one half.

Everything that blocks sight or movement is a Block, a Collider or a solid prop: Mesh ops are looks only (bullets, bots
and players pass through them), so every big mesh here gets a hidden collider core.
"""
import json
import math

OPS = []
STATS = {"blocks": 0, "colliders": 0, "meshes": 0, "props": 0, "decals": 0, "waypoints": 0, "spawns": 0, "buildings": 0}


def reset():
    OPS.clear()
    for k in STATS:
        STATS[k] = 0


def r(v, n=3):
    return round(float(v), n)


def op(d):
    OPS.append(d)


# ------------------------------------------------------------------ solids
def block(x0, y0, z0, x1, y1, z1, color, material=None):
    """A drawn solid box (one draw call each: keep them for big flat decks and walls). The game picks its texture from
    its colour and shape (`material` is ignored: it's here so calls read like the mesh ones)."""
    if abs(x1 - x0) < 0.01 or abs(y1 - y0) < 0.01 or abs(z1 - z0) < 0.01:
        return
    STATS["blocks"] += 1
    d = {"op": "Block", "x0": r(min(x0, x1)), "y0": r(min(y0, y1)), "z0": r(min(z0, z1)),
         "x1": r(max(x0, x1)), "y1": r(max(y0, y1)), "z1": r(max(z0, z1)), "color": color}
    op(d)


def collider(x0, y0, z0, x1, y1, z1):
    """A hidden solid box: collision, bullets and bot sight, never drawn."""
    if abs(x1 - x0) < 0.02 or abs(y1 - y0) < 0.02 or abs(z1 - z0) < 0.02:
        return
    STATS["colliders"] += 1
    op({"op": "Collider", "x0": r(min(x0, x1)), "y0": r(min(y0, y1)), "z0": r(min(z0, z1)),
        "x1": r(max(x0, x1)), "y1": r(max(y0, y1)), "z1": r(max(z0, z1))})


def round_collider(x, z, rad, y0, y1):
    """Three hidden boxes approximating an upright cylinder."""
    a, b, c = rad * 0.96, rad * 0.41, rad * 0.70
    collider(x - a, y0, z - b, x + a, y1, z + b)
    collider(x - b, y0, z - a, x + b, y1, z + a)
    collider(x - c, y0, z - c, x + c, y1, z + c)


# ------------------------------------------------------------------ meshes
def mesh(kind, x, z, y, color, **kw):
    """A cosmetic mesh standing on its lowest point at (x, y, z) (the game's own placement rule)."""
    STATS["meshes"] += 1
    d = {"op": "Mesh", "mesh": kind, "x": r(x), "z": r(z), "y": r(y), "color": color}
    for k, v in kw.items():
        if v is None or (k in ("yaw", "pitch", "roll") and abs(v) < 1e-9):
            continue
        d[k] = r(v) if isinstance(v, float) else v
    op(d)


def rot_matrix(yaw, pitch, roll):
    """System.Numerics row vectors: v' = v * Rz(roll) * Rx(pitch) * Ry(yaw)."""
    a, b, c = math.radians(yaw), math.radians(pitch), math.radians(roll)
    rz = [[math.cos(c), math.sin(c), 0], [-math.sin(c), math.cos(c), 0], [0, 0, 1]]
    rx = [[1, 0, 0], [0, math.cos(b), math.sin(b)], [0, -math.sin(b), math.cos(b)]]
    ry = [[math.cos(a), 0, -math.sin(a)], [0, 1, 0], [math.sin(a), 0, math.cos(a)]]

    def mul(m, n):
        return [[sum(m[i][k] * n[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mul(mul(rz, rx), ry)


def apply(v, m):
    return [sum(v[k] * m[k][j] for k in range(3)) for j in range(3)]


def rest_centre(kind, height=1.0, inner=0.0):
    """Where a shape's centre is in its rest pose (bottom at y = 0)."""
    if kind in ("torus", "ring"):
        return [0.0, inner if inner > 0 else 0.1, 0.0]
    return [0.0, height / 2.0, 0.0]


def mesh_at(kind, cx, cy, cz, color, yaw=0.0, pitch=0.0, roll=0.0, **kw):
    """A mesh placed by its CENTRE (cx, cy, cz) whatever its turn: the game turns a shape about its base point, so the
    base is put where the turned centre offset lands on the wanted centre."""
    h = kw.get("height", 1.0)
    inner = kw.get("inner", 0.0)
    if kind in ("torus", "ring") and inner == 0:
        inner = 0.1
    off = apply(rest_centre(kind, h, inner), rot_matrix(yaw, pitch, roll))
    mesh(kind, cx - off[0], cz - off[2], cy - off[1], color, yaw=yaw, pitch=pitch, roll=roll, **kw)


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


# ------------------------------------------------------------------ props, decals, waypoints, spawns
def prop(name, x, z, y=0.0, yaw=0.0, **kw):
    STATS["props"] += 1
    d = {"op": "Prop", "name": name, "x": r(x), "z": r(z)}
    if y:
        d["y"] = r(y)
    if yaw:
        d["yaw"] = r(yaw % 360, 1)
    d.update(kw)
    op(d)


def clutter(props, x0, z0, x1, z1, count, seed=0, **kw):
    d = {"op": "Clutter", "props": props, "x0": r(x0), "z0": r(z0), "x1": r(x1), "z1": r(z1), "count": int(count), "seed": int(seed)}
    d.update(kw)
    op(d)


def decal(name, x, z, y=0.0, onto="floor", **kw):
    STATS["decals"] += 1
    d = {"op": "Decal", "decal": name, "x": r(x), "z": r(z)}
    if y:
        d["y"] = r(y)
    if onto != "floor":
        d["onto"] = onto
    d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
    op(d)


def decal_scatter(names, x0, z0, x1, z1, count, seed=0, y=0.0, **kw):
    d = {"op": "DecalScatter", "decals": names, "x0": r(x0), "z0": r(z0), "x1": r(x1), "z1": r(z1), "count": int(count), "seed": int(seed)}
    if y:
        d["y"] = r(y)
    d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
    op(d)


def waypoint(x, z, y=0.0):
    STATS["waypoints"] += 1
    d = {"op": "Waypoint", "x": r(x), "z": r(z)}
    if y:
        d["y"] = r(y)
    op(d)


def waypoint_grid(x0, z0, x1, z1, y, spacing=6.0, keep=None):
    """Waypoints over a rectangle at height y, `spacing` apart, starting half a step in; `keep(x, z)` filters."""
    nx = max(1, int(round((x1 - x0) / spacing)))
    nz = max(1, int(round((z1 - z0) / spacing)))
    for i in range(nx):
        for j in range(nz):
            x = x0 + (x1 - x0) * (i + 0.5) / nx
            z = z0 + (z1 - z0) * (j + 0.5) / nz
            if keep is None or keep(x, z):
                waypoint(x, z, y)


SPAWNS = [[], []]


def spawn(team, x, z, y=0.0):
    """A spawn point for team 0 (Blue) or 1 (Red), also recorded for teamSpawns."""
    STATS["spawns"] += 1
    SPAWNS[team].append([r(x), r(y), r(z)])
    d = {"op": "Spawn", "x": r(x), "z": r(z)}
    if y:
        d["y"] = r(y)
    op(d)


def building(name, x, y, z, yaw=0, seed=1, spawns=False, variant=None):
    STATS["buildings"] += 1
    d = {"op": "Building", "name": name, "at": [r(x), r(y), r(z)] if y else [r(x), r(z)], "yaw": int(yaw) % 360, "seed": int(seed)}
    if spawns:
        d["spawns"] = True
    if variant:
        d["variant"] = variant
    op(d)


# ------------------------------------------------------------------ railings
RAIL_LO, RAIL_HI = 0.45, 1.1


def rail_collider_line(x0, z0, ya, x1, z1, yb, piece=0.7, t=0.12):
    """Hidden collider band along a line (a railing: bullets, grenades and prone shots go under it), in short pieces."""
    L = math.hypot(x1 - x0, z1 - z0)
    n = max(1, int(math.ceil(L / piece)))
    if abs(x1 - x0) < 0.01 or abs(z1 - z0) < 0.01:
        n = max(1, int(math.ceil(abs(yb - ya) / 0.15)))
    for k in range(n):
        f0, f1 = k / n, (k + 1) / n
        ax, az = x0 + (x1 - x0) * f0, z0 + (z1 - z0) * f0
        bx, bz = x0 + (x1 - x0) * f1, z0 + (z1 - z0) * f1
        y = ya + (yb - ya) * (f0 + f1) / 2
        collider(min(ax, bx) - t / 2, y + RAIL_LO, min(az, bz) - t / 2, max(ax, bx) + t / 2, y + RAIL_HI, max(az, bz) + t / 2)


def rail_visual_line(x0, z0, ya, x1, z1, yb, panel, top, post, post_every=2.5):
    L = math.hypot(x1 - x0, z1 - z0)
    if L < 0.2:
        return
    dx, dz = (x1 - x0) / L, (z1 - z0) / L
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    ym = (ya + yb) / 2
    slab(cx, cz, ym + 1.0, L, 0.05, 0.55, dx, dz, yb - ya, panel, material="")
    slab(cx, cz, ym + 1.12, L, 0.14, 0.1, dx, dz, yb - ya, top, material="Metal")
    n = max(1, int(round(L / post_every)))
    for k in range(n + 1):
        f = k / n
        mesh("cylinder", x0 + (x1 - x0) * f, z0 + (z1 - z0) * f, ya + (yb - ya) * f, post, width=0.1, height=1.12, depth=0.1, segments=8)


def railing(x0, z0, ya, x1, z1, yb=None, panel="#b8ad99", top="#8d7458", post="#6c6f73", post_every=2.5, gaps=()):
    """A railing from (x0, z0) to (x1, z1) (heights ya -> yb), with `gaps` ((distance along, half-width), ...) left open."""
    yb = ya if yb is None else yb
    L = math.hypot(x1 - x0, z1 - z0)
    if L < 0.05:
        return
    cuts = [(0.0, L)]
    for (at, hw) in gaps:
        nxt = []
        for (a, b) in cuts:
            g0, g1 = at - hw, at + hw
            if g1 <= a or g0 >= b:
                nxt.append((a, b))
                continue
            if g0 > a:
                nxt.append((a, g0))
            if g1 < b:
                nxt.append((g1, b))
        cuts = nxt
    for (a, b) in cuts:
        if b - a < 0.15:
            continue
        fa, fb = a / L, b / L
        ax, az, ay = x0 + (x1 - x0) * fa, z0 + (z1 - z0) * fa, ya + (yb - ya) * fa
        bx, bz, by = x0 + (x1 - x0) * fb, z0 + (z1 - z0) * fb, ya + (yb - ya) * fb
        rail_visual_line(ax, az, ay, bx, bz, by, panel, top, post, post_every)
        rail_collider_line(ax, az, ay, bx, bz, by)


# ------------------------------------------------------------------ stairs and sloped gangways
def stairs(x, z, y0, rise, dx, dz, width=1.6, steps=None, riser=0.3, tread=0.32, color="#8c8a86", side="#5e574c",
           waypoints=True):
    """A straight flight climbing `rise` from (x, z, y0) along the unit direction (dx, dz) (axis-aligned), the foot at
    (x, z). One hidden collider and one instanced mesh box per step (same size: one mesh, drawn instanced), a stringer
    each side, and waypoints at the foot, the middle and the top. Returns (x, z, y) at the top landing's start."""
    steps = steps or int(round(rise / riser))
    riser = rise / steps
    px, pz = -dz, dx
    hw = width / 2
    for k in range(steps):
        a = k * tread
        b = (k + 1) * tread
        top = y0 + (k + 1) * riser
        ax, az = x + dx * a, z + dz * a
        bx, bz = x + dx * b, z + dz * b
        x0, x1 = min(ax, bx) - abs(px) * hw, max(ax, bx) + abs(px) * hw
        z0, z1 = min(az, bz) - abs(pz) * hw, max(az, bz) + abs(pz) * hw
        collider(x0, top - riser - 0.02, z0, x1, top, z1)
        mesh_at("box", (ax + bx) / 2, top - riser / 2 - 0.01, (az + bz) / 2, color, width=tread + 0.02 if abs(dx) > 0.5 else width,
                height=riser + 0.02, depth=width if abs(dx) > 0.5 else tread + 0.02, material="Metal")
    L = steps * tread
    for s in (-1, 1):
        slab(x + dx * L / 2 + px * s * (hw + 0.04), z + dz * L / 2 + pz * s * (hw + 0.04), y0 + rise / 2 + 0.2, L + 0.3, 0.08, 0.5,
             dx, dz, rise, side, material="Metal")
    if waypoints:
        waypoint(x - dx * 0.8, z - dz * 0.8, y0)
        waypoint(x + dx * L / 2, z + dz * L / 2, y0 + rise / 2)
        waypoint(x + dx * (L + 0.8), z + dz * (L + 0.8), y0 + rise)
    return x + dx * L, z + dz * L, y0 + rise


def gangway(x0, z0, y0, x1, z1, y1, width, color="#8c8a86", rails=True, material="Grid", waypoints=True, step=0.04):
    """A sloped walkway (axis-aligned) from (x0, y0, z0) to (x1, y1, z1): hidden colliders stepping `step` at a time
    under a drawn sloped slab, railings both sides, waypoints along it."""
    L = math.hypot(x1 - x0, z1 - z0)
    ux, uz = (x1 - x0) / L, (z1 - z0) / L
    px, pz = -uz, ux
    hw = width / 2
    rise = y1 - y0
    n = max(1, int(math.ceil(abs(rise) / step)))
    for k in range(n):
        f0, f1 = k / n, (k + 1) / n
        ax, az = x0 + (x1 - x0) * f0, z0 + (z1 - z0) * f0
        bx, bz = x0 + (x1 - x0) * f1, z0 + (z1 - z0) * f1
        y = min(y0 + rise * f0, y0 + rise * f1)
        collider(min(ax, bx) - abs(px) * hw, y - 0.45, min(az, bz) - abs(pz) * hw, max(ax, bx) + abs(px) * hw, y, max(az, bz) + abs(pz) * hw)
    slab((x0 + x1) / 2, (z0 + z1) / 2, (y0 + y1) / 2 + 0.02, L + 0.4, width, 0.3, ux, uz, rise, color, material=material)
    if rails:
        for s in (-1, 1):
            railing(x0 + px * s * (hw - 0.08), z0 + pz * s * (hw - 0.08), y0, x1 + px * s * (hw - 0.08), z1 + pz * s * (hw - 0.08), y1)
    if waypoints:
        m = max(1, int(math.ceil(L / 4.0)))
        for k in range(m + 1):
            f = k / m
            waypoint(x0 + (x1 - x0) * f, z0 + (z1 - z0) * f, y0 + rise * f)


# ------------------------------------------------------------------ symmetry
def rot180(x, z):
    return -x, -z


def write_map(path, m):
    with open(path, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print(f"wrote {path}: {len(m['ops'])} ops, {STATS}")
