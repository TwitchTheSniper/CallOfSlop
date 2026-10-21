"""Placement rules for the Cluttered pack's map generators (reusable; dockside.py is the first user).

A generator builds a map in Python and writes the JSON. This module holds the parts that aren't specific to one map:

- PropLib: every global prop (Props/<Category>/*.json) and a pack's own props (Maps/<Pack>/Props/**), with footprints
  in the game's yaw convention, solid/cosmetic, breakable, height, and the height of a table-like top to put things on.
- Layout: the op list plus an occupancy model (axis-aligned rectangles of everything solid), keep-out circles (spawns,
  hills, flags) and lanes (walking lines solid props stay off). Every block, mesh and prop can be mirrored about x = 0
  (x, z, yaw) -> (-x, z, -yaw) so a map is fair by construction.
- Rules that put things where they belong, each a function taking the Layout:
    kerb_row       street furniture along a road edge at a spacing, turned to face the road (lamps, signs, hydrants,
                   meters, benches, bins), skipping spots that are taken
    park_row       vehicles parked along a kerb line, aligned with the road (nose either way), bumper gaps
    dumpster_spot  a dumpster against a wall with trash bags and litter round it, optionally a wheelie bin beside
    trash_ring     cosmetic litter in a ring round a point (bins, benches, doors)
    roof_kit       AC units, vents, turbines, antennas and dishes on a flat roof, clear of its edge and of each other
    wall_kit       wall-mounted units (window AC, vent grilles, meters) on an outside wall, facing out
    room           furniture against the walls of a room and in its middle, clear of its doors; desk-top things on
                   the desks and counters (office, break room, kitchen, living room, storeroom)
    edge_row       things at a spacing along a polyline edge (bollards on a quay, posts on a pier)
    afloat         cosmetic floaters (boats, buoys, ducks, bobbers) on a water surface outside the playable outline
- Geometry helpers: polyline walking, point-in-polygon, distance to a polygon edge, Catmull-Rom sampling.

The game's yaw convention (Source/Props/MapProps.cs): a prop's local (lx, lz) goes to world
(x + lx cos(yaw) + lz sin(yaw), z - lx sin(yaw) + lz cos(yaw)). So the prop's +X points along (cos, -sin) and its
+Z (its front, for things that face a way) along (sin, cos). yaw_facing(dx, dz) and yaw_along(dx, dz) give the yaw.
"""
import glob, json, math, os, random

# ---------------------------------------------------------------- geometry


def yaw_facing(dx, dz):
    """Yaw (degrees) that turns a prop's front (+Z) towards the direction (dx, dz)."""
    return math.degrees(math.atan2(dx, dz)) % 360


def yaw_along(dx, dz):
    """Yaw (degrees) that turns a prop's long axis (+X) along the direction (dx, dz)."""
    return math.degrees(math.atan2(-dz, dx)) % 360


def seg_dist(px, pz, ax, az, bx, bz):
    dx, dz = bx - ax, bz - az
    L = dx * dx + dz * dz
    t = 0 if L == 0 else max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / L))
    return math.hypot(ax + t * dx - px, az + t * dz - pz)


def poly_inside(poly, x, z):
    c = False
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        if (az > z) != (bz > z) and x < ax + (z - az) * (bx - ax) / (bz - az):
            c = not c
    return c


def poly_edge_dist(poly, x, z):
    n = len(poly)
    return min(seg_dist(x, z, *poly[i], *poly[(i + 1) % n]) for i in range(n))


def poly_area(poly):
    a = 0
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        a += ax * bz - bx * az
    return abs(a) / 2


def catmull(pts, step=0.5, closed=False):
    """Centripetal Catmull-Rom through pts (open: from the first to the last point)."""
    n = len(pts)
    out = []
    rng = range(n) if closed else range(n - 1)
    for i in rng:
        p0 = pts[(i - 1) % n] if closed or i > 0 else pts[0]
        p1, p2 = pts[i], pts[(i + 1) % n]
        p3 = pts[(i + 2) % n] if closed or i + 2 < n else pts[-1]

        def tj(ti, a, b):
            return ti + max(1e-4, math.hypot(b[0] - a[0], b[1] - a[1])) ** 0.5
        t0 = 0
        t1 = tj(t0, p0, p1)
        t2 = tj(t1, p1, p2)
        t3 = tj(t2, p2, p3)
        seg = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
        k = max(2, int(seg / step))
        for s in range(k):
            t = t1 + (t2 - t1) * s / k

            def lerp(a, b, ta, tb):
                if tb - ta < 1e-9:
                    return list(a)
                return [(tb - t) / (tb - ta) * a[0] + (t - ta) / (tb - ta) * b[0], (tb - t) / (tb - ta) * a[1] + (t - ta) / (tb - ta) * b[1]]
            A1 = lerp(p0, p1, t0, t1)
            A2 = lerp(p1, p2, t1, t2)
            A3 = lerp(p2, p3, t2, t3)
            B1 = lerp(A1, A2, t0, t2)
            B2 = lerp(A2, A3, t1, t3)
            out.append(lerp(B1, B2, t1, t2))
    if not closed:
        out.append(list(pts[-1]))
    return out


def walk(pts, spacing, start=0.0, end=0.0):
    """Points every `spacing` metres along a polyline: (x, z, tx, tz) with the unit tangent, from `start` metres in to
    `end` metres before its end."""
    segs = []
    total = 0
    for i in range(len(pts) - 1):
        L = math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1])
        segs.append((total, L, pts[i], pts[i + 1]))
        total += L
    d = start
    out = []
    while d <= total - end + 1e-6:
        for s0, L, a, b in segs:
            if d <= s0 + L + 1e-9 and L > 0:
                t = (d - s0) / L
                out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, (b[0] - a[0]) / L, (b[1] - a[1]) / L))
                break
        d += spacing
    return out


def polyline_length(pts):
    return sum(math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1))


def offset_line(pts, off):
    """The polyline moved `off` metres to its left (left of the walking direction, with x east and z south: (tz, -tx))."""
    out = []
    n = len(pts)
    for i in range(n):
        a = pts[max(0, i - 1)]
        b = pts[min(n - 1, i + 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]
        L = math.hypot(tx, tz) or 1
        out.append([pts[i][0] + tz / L * off, pts[i][1] - tx / L * off])
    return out


# ---------------------------------------------------------------- props


class PropLib:
    """Prop footprints and facts, global props plus a pack's own (the pack's shadow global ones of the same name)."""

    def __init__(self, repo, pack=None):
        self.p = {}
        files = glob.glob(os.path.join(repo, "Props/*/*.json"))
        if pack:
            files += glob.glob(os.path.join(repo, "Maps", pack, "Props/**/*.json"), recursive=True)
        for f in files:
            d = json.load(open(f))
            xs, zs, ys = [], [], []
            flat = 0.0
            for p in d["parts"]:
                s = p["size"]
                o = p.get("offset", [0, s[1] / 2, 0])
                rot = p.get("rotation")
                yaw = (rot[1] if rot else p.get("yaw", 0)) or 0
                r = max(s[0], s[2]) if yaw else None
                hx = (r if r else s[0]) / 2
                hz = (r if r else s[2]) / 2
                xs += [o[0] - hx, o[0] + hx]
                zs += [o[2] - hz, o[2] + hz]
                top = o[1] + s[1] / 2
                ys.append(top)
                if s[0] * s[2] > 0.25 and 0.5 < top < 1.3 and not p.get("anim"):
                    flat = max(flat, top)  # a table top, a counter, a desk: what other props can stand on
            col_ = d.get("collider")
            if d.get("collision", "solid") == "solid" and isinstance(col_, list) and col_:
                # solid props take up their collider boxes on the ground, not their model (a lamp's arm is up high)
                cxs, czs = [], []
                for c in col_:
                    s = c["size"]
                    o = c.get("offset", [0, s[1] / 2, 0])
                    cxs += [o[0] - s[0] / 2, o[0] + s[0] / 2]
                    czs += [o[2] - s[2] / 2, o[2] + s[2] / 2]
                xs, zs = cxs, czs
            cat = d.get("category") or os.path.basename(os.path.dirname(f))
            self.p[d["name"]] = dict(solid=d.get("collision", "solid") == "solid", brk="breakable" in d, cat=cat,
                                     w=max(xs) - min(xs), d=max(zs) - min(zs), h=max(ys), cx=(max(xs) + min(xs)) / 2,
                                     cz=(max(zs) + min(zs)) / 2, top=flat, pack=f.startswith(os.path.join(repo, "Maps")))

    def __getitem__(self, name):
        return self.p[name]

    def __contains__(self, name):
        return name in self.p

    def foot(self, name, x, z, yaw, scale=1.0):
        """World axis-aligned footprint (x0, z0, x1, z1) of a prop at (x, z) turned yaw degrees."""
        p = self.p[name]
        a = math.radians(yaw)
        cs, sn = math.cos(a), math.sin(a)
        w, d = p["w"] * scale / 2, p["d"] * scale / 2
        hx = abs(w * cs) + abs(d * sn)
        hz = abs(w * sn) + abs(d * cs)
        cx = x + (p["cx"] * cs + p["cz"] * sn) * scale
        cz = z + (-p["cx"] * sn + p["cz"] * cs) * scale
        return (cx - hx, cz - hz, cx + hx, cz + hz)


def R(v):
    return round(v, 2)


def col(c):
    return [round(v, 3) for v in c] if not isinstance(c, str) else c


def rect_hit(a, b, gap=0.0):
    return a[0] - gap < b[2] and b[0] < a[2] + gap and a[1] - gap < b[3] and b[1] < a[3] + gap


class Layout:
    """The op list and occupancy of a map being generated. `mirror` (default on) gives every placement a twin at
    (-x, z, -yaw) unless it sits on the mirror line."""

    def __init__(self, lib, terrain=False):
        self.lib = lib
        self.ops = []
        self.boxes = []          # (x0, z0, x1, z1, y0, y1, color) every Block, for previews and sight checks
        self.solids = []         # occupancy rectangles (blocks below 2 m up, solid props, trunks)
        self.props = []          # (name, x, z, yaw, y) every hand-placed prop
        self.meshes = 0
        self.keepout = []        # (x, z, r)
        self.lanes = []          # (pts, width)
        self.terrain = terrain   # on a terrain map, things near a drop are anchored to the world height
        self.log = []

    # ---- low level
    def block(self, x0, y0, z0, x1, y1, z1, c, mirror=True, occupy=True):
        x0, x1 = min(x0, x1), max(x0, x1)
        z0, z1 = min(z0, z1), max(z0, z1)
        op = {"op": "Block", "x0": R(x0), "y0": R(y0), "z0": R(z0), "x1": R(x1), "y1": R(y1), "z1": R(z1), "color": col(c)}
        self.ops.append(op)
        self.boxes.append((x0, z0, x1, z1, y0, y1, c))
        if occupy and y0 < 1.9:
            self.solids.append((x0, z0, x1, z1))
        if mirror and abs(x0 + x1) > 0.01:
            self.block(-x1, y0, z0, -x0, y1, z1, c, mirror=False, occupy=occupy)

    def mesh(self, kind, x, z, c, mirror=True, **kw):
        op = {"op": "Mesh", "mesh": kind, "x": R(x), "z": R(z)}
        for k, v in kw.items():
            op[k] = R(v) if isinstance(v, float) else v
        op["color"] = col(c)
        self.ops.append(op)
        self.meshes += 1
        if mirror and abs(x) > 0.01:
            kw2 = dict(kw)
            for k in ("yaw", "roll"):
                if k in kw2:
                    kw2[k] = -kw2[k]
            self.mesh(kind, -x, z, c, mirror=False, **kw2)

    def free(self, r, gap=0.0):
        return not any(rect_hit(r, s, gap) for s in self.solids)

    def on_lane(self, r, pad=0.0):
        cx, cz = (r[0] + r[2]) / 2, (r[1] + r[3]) / 2
        rad = math.hypot(r[2] - r[0], r[3] - r[1]) / 2
        for pts, w in self.lanes:
            for i in range(len(pts) - 1):
                if seg_dist(cx, cz, *pts[i], *pts[i + 1]) < w / 2 + rad + pad:
                    return True
        return False

    def in_keepout(self, r, extra=0.0):
        for (x, z, rad) in self.keepout:
            dx = max(r[0] - x, 0, x - r[2])
            dz = max(r[1] - z, 0, z - r[3])
            if math.hypot(dx, dz) < rad + extra:
                return True
        return False

    def prop(self, name, x, z, yaw=0.0, y=None, mirror=True, check=True, scale=None, world=False, gap=0.02, lanes=False):
        """A hand-placed prop (and its twin). With check, a solid one standing on the ground must not overlap anything
        solid (and with lanes, nor a lane or keep-out). Returns False (placing nothing) when it doesn't fit."""
        p = self.lib[name]
        yaw = round(yaw % 360, 1)
        r = self.lib.foot(name, x, z, yaw, scale or 1)
        on_ground = y is None or world
        if check and p["solid"] and on_ground:
            if not self.free(r, gap):
                return False
            if lanes and (self.on_lane(r) or self.in_keepout(r, 0.4)):
                return False
            if mirror and abs(x) > 0.01:
                rm = self.lib.foot(name, -x, z, (-yaw) % 360, scale or 1)
                if rect_hit(r, rm, gap) or not self.free(rm, gap):
                    return False
        self._emit(name, x, z, yaw, y, scale, world)
        if p["solid"] and on_ground:
            self.solids.append(r)
        if mirror and abs(x) > 0.01:
            self._emit(name, -x, z, (-yaw) % 360, y, scale, world)
            if p["solid"] and on_ground:
                self.solids.append(self.lib.foot(name, -x, z, (-yaw) % 360, scale or 1))
        return True

    def _emit(self, name, x, z, yaw, y, scale, world):
        op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
        if yaw:
            op["yaw"] = R(yaw)
        if scale and abs(scale - 1) > 1e-3:
            op["scale"] = R(scale)
        if y is not None:
            op["y"] = R(y)
        if world:
            op["anchor"] = "world"
        self.ops.append(op)
        self.props.append((name, x, z, yaw, y if y is not None else 0.0))

    # ---- walls with openings (axis-aligned blocks)
    def wall(self, x0, z0, x1, z1, h, t, c, doors=(), y0=0.0, lintel=True, mirror=True):
        """A wall from (x0, z0) to (x1, z1) (along X or Z), t thick on the +side, h high, with door openings
        [(centre along the wall, width, top)]. A door's top below h leaves a lintel above it."""
        alongx = abs(x1 - x0) >= abs(z1 - z0)
        a, b = (min(x0, x1), max(x0, x1)) if alongx else (min(z0, z1), max(z0, z1))
        cuts = sorted(doors)
        pos = a
        pieces = []
        for c0, w, top in cuts:
            if c0 - w / 2 > pos:
                pieces.append((pos, c0 - w / 2, y0, y0 + h))
            if lintel and top < h - 0.05:
                pieces.append((c0 - w / 2, c0 + w / 2, y0 + top, y0 + h))
            pos = c0 + w / 2
        if pos < b:
            pieces.append((pos, b, y0, y0 + h))
        for p0, p1, ya, yb in pieces:
            if alongx:
                self.block(p0, ya, z0, p1, yb, z0 + t, c, mirror=mirror)
            else:
                self.block(x0, ya, p0, x0 + t, yb, p1, c, mirror=mirror)


# ---------------------------------------------------------------- rules


def kerb_row(lay, pts, offset, spacing, names, seed=0, start=0.0, end=0.0, face="road", jitter=0.0, mirror=True, y=None, clear=None):
    """Street furniture along a road edge: at every `spacing` metres along pts, `offset` metres to the line's left
    (negative: right), turned so its front faces the road (face="road"), away from it ("away"), along it ("along") or with its
    +X arm reaching over it ("arm": street lamps).
    names is a list cycled in order (a name of None leaves a gap). `clear` is an optional function (x, z) -> bool for
    spots to skip. Returns how many were placed (pairs count once)."""
    rnd = random.Random(seed)
    n = 0
    for k, (x, z, tx, tz) in enumerate(walk(pts, spacing, start, end)):
        name = names[k % len(names)]
        if name is None:
            continue
        nx, nz = tz, -tx  # left normal
        px, pz = x + nx * offset + tx * rnd.uniform(-jitter, jitter), z + nz * offset + tz * rnd.uniform(-jitter, jitter)
        if clear and not clear(px, pz):
            continue
        toward = (-nx * math.copysign(1, offset), -nz * math.copysign(1, offset))
        yaw = (yaw_facing(*toward) if face == "road" else yaw_facing(-toward[0], -toward[1]) if face == "away"
               else yaw_along(*toward) if face == "arm" else yaw_along(tx, tz))  # "arm": the prop's +X (a lamp's arm) reaches over the road
        if lay.prop(name, px, pz, yaw, y=y, mirror=mirror):
            n += 1
    return n


def park_row(lay, pts, offset, names, seed=0, gap=1.4, start=0.0, end=0.0, mirror=True, clear=None, empty=0.2):
    """Vehicles parked nose to tail along a kerb line: each turned along the road (+X along the tangent, a random half
    facing the other way), `offset` metres to the line's left, spaced by their own length plus `gap`. `empty` is the
    chance a bay is left empty. Returns the vehicles placed."""
    rnd = random.Random(seed)
    total = polyline_length(pts)
    d = start
    n = 0
    while d < total - end:
        name = rnd.choice(names)
        L = lay.lib[name]["w"]
        mid = d + L / 2
        if mid + L / 2 > total - end:
            break
        x, z, tx, tz = walk(pts, 1e9, mid)[0]
        if rnd.random() >= empty:
            nx, nz = tz, -tx
            px, pz = x + nx * offset, z + nz * offset
            yaw = yaw_along(tx, tz) + (180 if rnd.random() < 0.5 else 0)
            if (clear is None or clear(px, pz)) and lay.prop(name, px, pz, yaw, mirror=mirror):
                n += 1
        d += L + gap
    return n


LITTER = ["Litter", "Plastic Bag", "Flattened Cardboard", "Soda Cans", "Bottles", "Newspaper", "Bottle", "Cardboard Box Small"]


def trash_ring(lay, cx, cz, r0, r1, count, names=LITTER, seed=0, mirror=True, inside=None):
    """Cosmetic litter in a ring r0..r1 round (cx, cz), random turns, off anything solid."""
    rnd = random.Random(seed)
    n = 0
    for _ in range(count * 6):
        if n >= count:
            break
        a = rnd.uniform(0, math.tau)
        r = rnd.uniform(r0, r1)
        x, z = cx + math.cos(a) * r, cz + math.sin(a) * r
        if inside and not inside(x, z):
            continue
        name = rnd.choice(names)
        f = lay.lib.foot(name, x, z, 0)
        if not lay.free(f, 0.02):
            continue
        lay.prop(name, x, z, rnd.uniform(0, 360), mirror=mirror)
        n += 1
    return n


def dumpster_spot(lay, x, z, wall_normal, seed=0, bins=1, bags=(2, 5), litter=(3, 7), mirror=True, inside=None):
    """A dumpster standing with its back to a wall (wall_normal points from the wall into the alley), trash bags
    piled round it, litter scattered further out, and `bins` wheelie bins beside it. Returns False if the dumpster
    didn't fit."""
    rnd = random.Random(seed)
    nx, nz = wall_normal
    yaw = yaw_facing(nx, nz)
    d = lay.lib["Dumpster"]["d"] / 2 + 0.05
    if not lay.prop("Dumpster", x + nx * d, z + nz * d, yaw, mirror=mirror):
        return False
    tx, tz = -nz, nx  # along the wall
    for b in range(bins):
        s = 1 if b % 2 == 0 else -1
        bx = x + tx * s * (1.6 + 0.85 * (b // 2)) + nx * 0.5
        bz = z + tz * s * (1.6 + 0.85 * (b // 2)) + nz * 0.5
        lay.prop(rnd.choice(["Wheelie Bin", "Wheelie Bin Grey", "Recycling Bin"]), bx, bz, yaw + rnd.uniform(-8, 8), mirror=mirror)
    cx, cz = x + nx * d, z + nz * d
    trash_ring(lay, cx, cz, 1.2, 2.2, rnd.randint(*bags), ["Trash Bag"], seed + 1, mirror, inside)
    trash_ring(lay, cx, cz, 1.2, 2.4, rnd.randint(1, 2), ["Cardboard Box Small", "Open Cardboard Box", "Flattened Cardboard"], seed + 3, mirror, inside)
    trash_ring(lay, cx, cz, 1.4, 3.6, rnd.randint(*litter), LITTER, seed + 2, mirror, inside)
    return True


ROOF_ITEMS = [("Rooftop HVAC Unit", 1), ("Rooftop Condenser", 2), ("Split AC Unit", 2), ("Turbine Vent", 3), ("Roof Turbine Vent", 2),
              ("Mushroom Vent", 3), ("Exhaust Fan", 1), ("Skylight", 2), ("Satellite Dish", 1), ("TV Antenna", 1), ("Air Duct", 1)]


def roof_kit(lay, x0, z0, x1, z1, y, seed=0, items=ROOF_ITEMS, margin=0.7, mirror=True):
    """Rooftop plant on a flat roof (x0..x1, z0..z1) whose top is at height y: each (name, count) placed at a random
    spot clear of the roof edge and of each other, square to the building (90 degree steps)."""
    rnd = random.Random(seed)
    used = []
    n = 0
    for name, count in items:
        for _ in range(count):
            for _try in range(40):
                yaw = rnd.choice((0, 90, 180, 270))
                x = rnd.uniform(x0 + margin, x1 - margin)
                z = rnd.uniform(z0 + margin, z1 - margin)
                f = lay.lib.foot(name, x, z, yaw)
                if f[0] < x0 + margin or f[2] > x1 - margin or f[1] < z0 + margin or f[3] > z1 - margin:
                    continue
                if any(rect_hit(f, u, 0.5) for u in used):
                    continue
                used.append(f)
                lay.prop(name, x, z, yaw, y=y, mirror=mirror)
                n += 1
                break
    return n


def wall_kit(lay, a, b, normal, items, seed=0, mirror=True, avoid=()):
    """Wall-mounted things on an outside wall face from a to b (the face line), `normal` pointing out of the wall:
    items is a list of (name, y) placed at even spacing with a little jitter, front (+Z) facing out, back on the wall.
    `avoid` is a list of (centre along the wall, half width) to keep clear (doors)."""
    rnd = random.Random(seed)
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    tx, tz = (b[0] - a[0]) / L, (b[1] - a[1]) / L
    nx, nz = normal
    yaw = yaw_facing(nx, nz)
    k = len(items)
    for i, (name, y) in enumerate(items):
        s = L * (i + 0.5) / k + rnd.uniform(-0.6, 0.6)
        if any(abs(s - c) < hw + lay.lib[name]["w"] / 2 for c, hw in avoid):
            s += 2.2
        if s < 0.6 or s > L - 0.6:
            continue
        d = lay.lib[name]["d"] / 2 + 0.01
        lay.prop(name, a[0] + tx * s + nx * d, a[1] + tz * s + nz * d, yaw, y=y, mirror=mirror)


# Room kits: (wall items, middle items, things put on tables/counters/desks)
ROOMS = {
    "office": (["Filing Cabinet", "Filing Cabinet", "Metal Shelving", "Water Cooler", "Office Plant", "Photocopier", "Whiteboard", "Archive Boxes", "Filing Cabinet"],
               [("Office Desk", "Office Chair")],
               ["Desk Phone", "Laptop", "Desk Printer", "Book Pile", "Computer Monitor"]),
    "break": (["Fridge", "Glowing Vending Machine", "Kitchen Counter", "Kitchen Sink", "Water Cooler", "Vending Machine", "Pedal Bin", "Metal Lockers"],
              [("Table", "Plastic Chair")],
              ["Microwave", "Kettle", "Coffee Maker", "Stacked Plates", "Soda Cans"]),
    "kitchen": (["Fridge", "Kitchen Counter", "Cooker", "Kitchen Sink", "Dishwasher", "Kitchen Counter", "Pedal Bin"],
                [("Dining Table", "Dining Chair")],
                ["Microwave", "Kettle", "Toaster", "Coffee Maker", "Stacked Plates", "Cooking Pot"]),
    "living": (["Bookshelf", "TV Stand", "Floor Lamp", "Fabric Sofa", "Armchair", "Radiator"],
               [("Coffee Table", None)],
               ["Book Pile", "Table Lamp"]),
    "store": (["Metal Shelving", "Metal Shelving", "Pallet Stack", "Archive Boxes", "Metal Shelving", "Cardboard Stack"],
              [("Wooden Crate", None)],
              []),
}


def room(lay, x0, z0, x1, z1, kind, doors=(), seed=0, mirror=True, middle=1, wall_gap=0.05, tops=True, wall_items=None):
    """Furnishes a room inside walls whose inner faces are x0..x1, z0..z1: the kind's wall items stand with their
    backs to the walls (front facing into the room), clear of `doors` [(x, z) points with a 1.3 m clearance], and
    `middle` furniture sets (a desk or table and its chairs) go in the middle, with desk-top things on them.
    Returns the props placed (pairs count once)."""
    walls_list, mids, top_items = ROOMS[kind]
    if wall_items is not None:
        walls_list = wall_items
    rnd = random.Random(seed)
    n = 0

    def near_door(r, pad=1.3):
        for dx, dz in doors:
            if max(r[0] - dx, 0, dx - r[2]) ** 2 + max(r[1] - dz, 0, dz - r[3]) ** 2 < pad * pad:
                return True
        return False

    sides = [("N", (x0, z0), (x1, z0), (0, 1)), ("S", (x1, z1), (x0, z1), (0, -1)), ("W", (x0, z1), (x0, z0), (1, 0)), ("E", (x1, z0), (x1, z1), (-1, 0))]
    queue = list(walls_list)
    rnd.shuffle(queue)
    for _, a, b, (nx, nz) in sides:
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        tx, tz = (b[0] - a[0]) / L, (b[1] - a[1]) / L
        s = 0.35 + rnd.uniform(0, 0.4)
        while queue and s < L - 0.3:
            name = queue[0]
            w, d = lay.lib[name]["w"], lay.lib[name]["d"]
            if s + w > L - 0.25:
                break
            c = s + w / 2
            px = a[0] + tx * c + nx * (d / 2 + wall_gap)
            pz = a[1] + tz * c + nz * (d / 2 + wall_gap)
            yaw = yaw_facing(nx, nz)
            f = lay.lib.foot(name, px, pz, yaw)
            if near_door(f) or not lay.free(f, 0.02) or f[0] < x0 - 0.01 or f[2] > x1 + 0.01 or f[1] < z0 - 0.01 or f[3] > z1 + 0.01:
                s += 0.6
                continue
            lay.prop(name, px, pz, yaw, mirror=mirror, check=False)
            if not lay.lib[name]["solid"]:
                pass
            else:
                lay.solids.append(f)
            n += 1
            queue.pop(0)
            s += w + rnd.uniform(0.1, 0.5)
    # middle sets
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    for m in range(middle):
        table, chair = mids[m % len(mids)]
        for _try in range(30):
            ox = rnd.uniform(-(x1 - x0) / 2 + 1.6, (x1 - x0) / 2 - 1.6) if x1 - x0 > 3.2 else 0
            oz = rnd.uniform(-(z1 - z0) / 2 + 1.6, (z1 - z0) / 2 - 1.6) if z1 - z0 > 3.2 else 0
            yaw = rnd.choice((0, 90, 180, 270))
            tx, tz = cx + ox, cz + oz
            f = lay.lib.foot(table, tx, tz, yaw)
            if near_door(f, 1.0) or not lay.free(f, 0.9):
                continue
            lay.prop(table, tx, tz, yaw, mirror=mirror, check=False)
            lay.solids.append(f)
            n += 1
            top = lay.lib[table]["top"]
            if tops and top > 0 and top_items:
                for k in range(rnd.randint(1, 2)):
                    it = rnd.choice(top_items)
                    a = math.radians(yaw)
                    lx = rnd.uniform(-lay.lib[table]["w"] / 2 + 0.25, lay.lib[table]["w"] / 2 - 0.25)
                    lz = rnd.uniform(-0.15, 0.15)
                    lay.prop(it, tx + lx * math.cos(a) + lz * math.sin(a), tz - lx * math.sin(a) + lz * math.cos(a), yaw + rnd.uniform(-25, 25), y=top, mirror=mirror)
            if chair:
                a = math.radians(yaw)
                # chairs on the table's front (+Z) side, and one at the back for a table
                fz = lay.lib[table]["d"] / 2 + 0.35
                spots = [(0, fz, 180)] if "Desk" in table else [(-0.35, fz, 180), (0.35, -fz, 0)]
                for lx, lz, cy in spots:
                    px = tx + lx * math.cos(a) + lz * math.sin(a)
                    pz = tz - lx * math.sin(a) + lz * math.cos(a)
                    lay.prop(chair, px, pz, yaw + cy + rnd.uniform(-20, 20), mirror=mirror, check=False)
                    lay.solids.append(lay.lib.foot(chair, px, pz, yaw + cy))
            break
    return n


def edge_row(lay, pts, spacing, name, inset=0.6, start=1.0, end=1.0, face="out", seed=0, mirror=True, every=None):
    """Things along an edge polyline (a quay or pier edge) every `spacing` metres, `inset` metres in from it on the
    line's right (with x east and z south: (-tz, tx)), turned to face out over the edge (face="out"), in, or along it."""
    rnd = random.Random(seed)
    n = 0
    for k, (x, z, tx, tz) in enumerate(walk(pts, spacing, start, end)):
        nm = name if every is None else every[k % len(every)]
        if nm is None:
            continue
        rx, rz = -tz, tx  # right normal (into the land when the water is on the left)
        px, pz = x + rx * inset, z + rz * inset
        yaw = yaw_facing(-rx, -rz) if face == "out" else yaw_facing(rx, rz) if face == "in" else yaw_along(tx, tz)
        if lay.prop(nm, px, pz, yaw + rnd.uniform(-3, 3), mirror=mirror):
            n += 1
    return n


def afloat(lay, name, x, z, water_y, yaw=None, seed=0, mirror=True, scale=None):
    """A floater on the water surface at world height water_y (boats, buoys, ducks, bobbers): placed with
    "anchor": "world" so it isn't dropped to the sea bed."""
    rnd = random.Random(seed)
    lay.prop(name, x, z, rnd.uniform(0, 360) if yaw is None else yaw, y=water_y, mirror=mirror, world=True, check=False, scale=scale)
