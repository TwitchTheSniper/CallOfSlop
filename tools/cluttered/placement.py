#!/usr/bin/env python3
"""Shared placement rules for Cluttered-pack map generators (Python 3, no dependencies; Pillow only for preview()).

A generator builds a map through one `Site`: it emits the JSON ops (Block, Mesh, Prop, Spawn, Waypoint), keeps an
occupancy list of everything solid so props never overlap, keeps solid props off the bot lanes and objectives, and can
mirror everything for a fair map. On top of those primitives are the placement RULES: small functions that put props
where they belong instead of at random:

    rule                  what it places, and where
    --------------------  -----------------------------------------------------------------------------------------
    along(...)            things that follow a road or a path: streetlights, hydrants, signs, parked cars, bins at
                          the kerb, street trees; at a spacing, set back from the line, turned to face it or follow it
    against(...)          things that stand against a wall: AC units, meters, dumpsters behind a building, a bench by
                          a porch, furniture against a room's wall (back to the wall, facing out of it)
    on_roof(...)          vents, turbine vents, dishes, aerials, condensers, solar panels on a flat roof rectangle
    on_top(...)           small cosmetic things on a table, a counter or a desk (y = the support's top)
    around(...)           a scatter round an anchor: litter and bags round bins and dumpsters, ducks round a pond,
                          leaves round a tree; falls off with distance, cosmetic by default
    room(...)             a furnished room from a template (kitchen, living, dining, bedroom, garage, office, utility),
                          furniture against the walls, the floor in front of every door kept clear
    scatter(...)          a deterministic scatter over a rectangle or polygon that obeys every rule above (lanes,
                          keep-outs, outline margin, occupancy); for yards, verges, junk

Every rule is deterministic for its seed, mirror-aware (a placement and its twin are made together, or neither), and
returns what it placed, so a caller can hang more rules off it (e.g. litter `around` each bin it placed `along` a kerb).

Quick start:

    import placement as pl
    lib = pl.PropLib(repo_root)                       # every prop in Props/ (and a pack's own Props/ if given)
    site = pl.Site(lib, outline=points, mirror="z")   # mirror: None, "x" (x -> -x), "z" (z -> -z) or "point"
    site.lane([(0, 0), (30, 0)], 3.0)                 # a bot route: solid props keep off it
    site.keepout(10, 4, 2.5)                          # a spawn / hill / flag
    site.block(-5, 0, -5, 5, 3, 5, "#c8b89a")         # a solid box (and its twin)
    site.prop("Fire Hydrant", 4, 6.2, yaw=pl.face_yaw(0, -1))
    lamps = site.along(["Street Lamp"], [(0, 5), (40, 5)], spacing=14, offset=0.4, face="line", side=1)
    for b in site.along(["Wheelie Bin"], kerb, spacing=6, offset=0.5, face="line"):
        site.around(b, ["Trash Bag", "Litter", "Soda Cans"], count=4, r1=1.6, seed=b.seed)
    ops = site.ops                                    # put them in the map's "ops" list

Conventions (the game's, see Props/README.md and Maps/README.md): metres, Y up, north is -Z. A prop's +X is its "along"
direction and its front faces +Z. At yaw t (degrees) a prop's local (lx, lz) lands at
(x + lx cos t + lz sin t, z - lx sin t + lz cos t), so its front faces (sin t, cos t): face_yaw(dx, dz) gives the yaw
that makes a prop face (dx, dz), along_yaw(dx, dz) the yaw that runs its +X along (dx, dz).
"""
import glob
import json
import math
import os
import random

# ------------------------------------------------------------------------------------------------ small geometry


def face_yaw(dx, dz):
    """Yaw (degrees) that turns a prop's front (+Z) towards the direction (dx, dz)."""
    return math.degrees(math.atan2(dx, dz)) % 360


def along_yaw(dx, dz):
    """Yaw (degrees) that runs a prop's +X axis along the direction (dx, dz)."""
    return math.degrees(math.atan2(-dz, dx)) % 360


def rot(lx, lz, yaw):
    """A prop-frame offset (lx, lz) turned by yaw degrees, as a world offset (dx, dz)."""
    a = math.radians(yaw)
    c, s = math.cos(a), math.sin(a)
    return lx * c + lz * s, -lx * s + lz * c


def seg_dist(px, pz, ax, az, bx, bz):
    """Distance from point (px, pz) to the segment (ax, az)-(bx, bz)."""
    dx, dz = bx - ax, bz - az
    L = dx * dx + dz * dz
    t = 0 if L == 0 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / L))
    return math.hypot(ax + t * dx - px, az + t * dz - pz)


def rect_point_dist(r, x, z):
    """Distance from (x, z) to the rectangle r = (x0, z0, x1, z1) (0 inside)."""
    dx = max(r[0] - x, 0, x - r[2])
    dz = max(r[1] - z, 0, z - r[3])
    return math.hypot(dx, dz)


def rect_seg_dist(r, ax, az, bx, bz):
    """Distance between a rectangle and a segment (0 when they touch)."""
    # the segment crosses the rectangle?
    if rect_point_dist(r, ax, az) == 0 or rect_point_dist(r, bx, bz) == 0:
        return 0.0
    corners = [(r[0], r[1]), (r[2], r[1]), (r[2], r[3]), (r[0], r[3])]
    for i in range(4):
        cx, cz = corners[i]
        ex, ez = corners[(i + 1) % 4]
        if _segs_cross(ax, az, bx, bz, cx, cz, ex, ez):
            return 0.0
    best = min(seg_dist(cx, cz, ax, az, bx, bz) for cx, cz in corners)
    best = min(best, rect_point_dist(r, ax, az), rect_point_dist(r, bx, bz))
    return best


def poly_seg_dist(poly, ax, az, bx, bz):
    """Distance between a convex polygon and a segment (0 when they touch)."""
    if point_in_poly(ax, az, poly) or point_in_poly(bx, bz, poly):
        return 0.0
    n = len(poly)
    best = 1e9
    for i in range(n):
        cx, cz = poly[i]
        ex, ez = poly[(i + 1) % n]
        if _segs_cross(ax, az, bx, bz, cx, cz, ex, ez):
            return 0.0
        best = min(best, seg_dist(cx, cz, ax, az, bx, bz), seg_dist(ax, az, cx, cz, ex, ez), seg_dist(bx, bz, cx, cz, ex, ez))
    return best


def _segs_cross(ax, az, bx, bz, cx, cz, dx, dz):
    def o(px, pz, qx, qz, rx, rz):
        return (qx - px) * (rz - pz) - (qz - pz) * (rx - px)
    d1 = o(cx, cz, dx, dz, ax, az)
    d2 = o(cx, cz, dx, dz, bx, bz)
    d3 = o(ax, az, bx, bz, cx, cz)
    d4 = o(ax, az, bx, bz, dx, dz)
    return (d1 > 0) != (d2 > 0) and (d3 > 0) != (d4 > 0)


def rects_overlap(a, b, gap=0.0):
    return a[0] - gap < b[2] and b[0] < a[2] + gap and a[1] - gap < b[3] and b[1] < a[3] + gap


def point_in_poly(x, z, poly):
    c = False
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        if (az > z) != (bz > z) and x < ax + (z - az) * (bx - ax) / (bz - az):
            c = not c
    return c


def poly_edge_dist(x, z, poly):
    n = len(poly)
    return min(seg_dist(x, z, *poly[i], *poly[(i + 1) % n]) for i in range(n))


def poly_area(poly):
    a = 0.0
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        a += ax * bz - bx * az
    return abs(a) / 2


def resample(pts, spacing, start=None):
    """Points every `spacing` metres along a polyline, each as (x, z, tx, tz) with the unit tangent. `start` is the
    distance of the first point (default half a spacing, so a line's ends aren't crowded)."""
    out = []
    d = spacing / 2 if start is None else start
    run = 0.0
    for i in range(len(pts) - 1):
        ax, az = pts[i]
        bx, bz = pts[i + 1]
        L = math.hypot(bx - ax, bz - az)
        if L < 1e-9:
            continue
        tx, tz = (bx - ax) / L, (bz - az) / L
        while d <= run + L + 1e-9:
            t = d - run
            out.append((ax + tx * t, az + tz * t, tx, tz))
            d += spacing
        run += L
    return out


def arc(cx, cz, r, a0, a1, step=None, n=None):
    """Points on a circular arc from angle a0 to a1 (degrees, 0 = +X, 90 = +Z)."""
    if n is None:
        n = max(2, int(abs(a1 - a0) / (step or 6)) + 1)
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * k / (n - 1))), cz + r * math.sin(math.radians(a0 + (a1 - a0) * k / (n - 1)))) for k in range(n)]


def R(v, nd=2):
    return round(v, nd)


# ------------------------------------------------------------------------------------------------ the prop library


def _turned_half(hx, hy, hz, pitch, yaw, roll):
    """Half extents of the axis-aligned box round a centred box turned by roll (Z), pitch (X), then yaw (Y)."""
    p, y, r = (math.radians(v) for v in (pitch, yaw, roll))
    out = [0.0, 0.0, 0.0]
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                x, yy, z = sx * hx, sy * hy, sz * hz
                x, yy = x * math.cos(r) - yy * math.sin(r), x * math.sin(r) + yy * math.cos(r)
                yy, z = yy * math.cos(p) - z * math.sin(p), yy * math.sin(p) + z * math.cos(p)
                x, z = x * math.cos(y) + z * math.sin(y), -x * math.sin(y) + z * math.cos(y)
                out = [max(out[0], abs(x)), max(out[1], abs(yy)), max(out[2], abs(z))]
    return out


class PropInfo:
    """A prop file reduced to what placement needs. Bounds are in the prop's frame (origin on the ground at its
    centre): x0..x1 along +X, z0..z1 along +Z (front), h = the top. `top` is the height of the surface things can be
    put on (a counter, a table: the solid collider's top, else the biggest flat part's top)."""

    def __init__(self, d, path):
        self.name = d["name"]
        self.path = path
        self.category = d.get("category") or os.path.basename(os.path.dirname(path))
        self.solid = d.get("collision", "solid") == "solid"
        self.breakable = "breakable" in d
        self.explosive = "explode" in d.get("breakable", {})
        self.animated = any("anim" in p for p in d["parts"])
        xs, zs, ys = [], [], []
        lxs, lzs = [], []    # parts that reach below 1.9 m: what stands in the way at the ground
        best = (0.0, 0.0)
        for p in d["parts"]:
            s = p["size"]
            o = p.get("offset", [0, s[1] / 2, 0])
            rotn = p.get("rotation", [p.get("pitch", 0), p.get("yaw", 0), p.get("roll", 0)])
            pitch, yaw, roll = (rotn + [0, 0, 0])[:3]
            if pitch or roll:  # tipped parts: the exact bounds of the turned box (roll, then pitch, then yaw)
                hx, hy, hz = _turned_half(s[0] / 2, s[1] / 2, s[2] / 2, pitch, yaw, roll)
            else:
                a = math.radians(yaw)
                c, sn = abs(math.cos(a)), abs(math.sin(a))
                hx = s[0] / 2 * c + s[2] / 2 * sn
                hz = s[0] / 2 * sn + s[2] / 2 * c
                hy = s[1] / 2
            xs += [o[0] - hx, o[0] + hx]
            zs += [o[2] - hz, o[2] + hz]
            ys.append(o[1] + hy)
            if o[1] - hy < 1.9:
                lxs += [o[0] - hx, o[0] + hx]
                lzs += [o[2] - hz, o[2] + hz]
            area = s[0] * s[2]
            if o[1] + hy > 0.3 and area > best[0]:
                best = (area, o[1] + hy)
        self.x0, self.x1, self.z0, self.z1 = min(xs), max(xs), min(zs), max(zs)
        if not lxs:
            lxs, lzs = xs, zs
        self.low = (min(lxs), max(lxs), min(lzs), max(lzs))  # x0, x1, z0, z1 of the parts near the ground
        self.h = max(ys)
        self.top = best[1]
        col = d.get("collider")
        if self.solid and isinstance(col, list):
            self.top = max(c.get("offset", [0, c["size"][1] / 2, 0])[1] + c["size"][1] / 2 for c in col)
        self.w = self.x1 - self.x0
        self.d = self.z1 - self.z0

    def outline(self, x, z, yaw=0.0, scale=1.0, full=False):
        """The turned footprint as 4 corners (an oriented rectangle), for exact overlap tests."""
        x0, x1, z0, z1 = (self.x0, self.x1, self.z0, self.z1) if full else self.low
        out = []
        for lx, lz in ((x0, z0), (x1, z0), (x1, z1), (x0, z1)):
            dx, dz = rot(lx * scale, lz * scale, yaw)
            out.append((x + dx, z + dz))
        return out

    def footprint(self, x, z, yaw=0.0, scale=1.0, full=False):
        """World rectangle (x0, z0, x1, z1) round the prop placed at (x, z) turned by yaw degrees: the parts near
        the ground (below 1.9 m, so a street lamp's arm or a sign's board can hang over things), or all of it."""
        a = math.radians(yaw)
        c, s = math.cos(a), math.sin(a)
        x0, x1, z0, z1 = (self.x0, self.x1, self.z0, self.z1) if full else self.low
        pts = []
        for lx in (x0 * scale, x1 * scale):
            for lz in (z0 * scale, z1 * scale):
                pts.append((x + lx * c + lz * s, z - lx * s + lz * c))
        return (min(p[0] for p in pts), min(p[1] for p in pts), max(p[0] for p in pts), max(p[1] for p in pts))


def _sep(pa, pb):
    """The largest gap between two convex polygons along their edge normals (negative when they overlap)."""
    best = -1e9
    for poly in (pa, pb):
        n = len(poly)
        for i in range(n):
            ax, az = poly[i]
            bx, bz = poly[(i + 1) % n]
            nx, nz = bz - az, ax - bx
            L = math.hypot(nx, nz)
            if L < 1e-9:
                continue
            nx, nz = nx / L, nz / L
            a = [px * nx + pz * nz for px, pz in pa]
            b = [px * nx + pz * nz for px, pz in pb]
            best = max(best, min(b) - max(a), min(a) - max(b))
    return best


def rect_poly(r):
    return [(r[0], r[1]), (r[2], r[1]), (r[2], r[3]), (r[0], r[3])]


class PropLib:
    """Every prop the map can name: the global Props folder, then (optionally) the pack's own Props folder, which
    shadows global names like the game does. `lib["Wheelie Bin"]` is a PropInfo; `lib.category("Kitchen")` lists names."""

    def __init__(self, repo, pack_dir=None):
        self.props = {}
        for f in sorted(glob.glob(os.path.join(repo, "Props", "**", "*.json"), recursive=True)):
            if os.path.basename(f).lower() == "readme.json":
                continue
            d = json.load(open(f))
            self.props[d["name"]] = PropInfo(d, f)
        if pack_dir:
            for f in sorted(glob.glob(os.path.join(pack_dir, "Props", "**", "*.json"), recursive=True)):
                d = json.load(open(f))
                self.props[d["name"]] = PropInfo(d, f)

    def __getitem__(self, name):
        if name.startswith("global:"):
            name = name[7:]
        if name not in self.props:
            close = sorted(self.props, key=lambda n: _edit(n.lower(), name.lower()))[:3]
            raise KeyError(f'unknown prop "{name}" (did you mean {close}?)')
        return self.props[name]

    def __contains__(self, name):
        return name in self.props

    def category(self, cat):
        return [n for n, p in self.props.items() if p.category.lower() == cat.lower()]


def _edit(a, b):
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ca != cb)))
        prev = cur
    return prev[-1]


# ------------------------------------------------------------------------------------------------ the site


class Placed:
    """One placed prop: name, position, yaw (degrees), y, its footprint, and the seed a follow-up rule can use."""
    __slots__ = ("name", "x", "z", "yaw", "y", "rect", "seed", "twin", "has_twin")

    def __init__(self, name, x, z, yaw, y, rect, seed, twin=False):
        self.name, self.x, self.z, self.yaw, self.y, self.rect, self.seed, self.twin = name, x, z, yaw, y, rect, seed, twin
        self.has_twin = False

    def __repr__(self):
        return f"Placed({self.name!r}, {self.x:.2f}, {self.z:.2f}, yaw {self.yaw:.0f})"


MIRRORS = (None, "x", "z", "point")


class Site:
    """A map under construction: the ops, the occupancy, the lanes and keep-outs, and the placement rules.

    outline   the playable outline as a list of (x, z) points (a polygon; sample a curve densely), or None
    mirror    None, "x" (twin at -x), "z" (twin at -z) or "point" (twin at -x, -z); every primitive makes the twin
              unless called with mirror=False (for things on the mirror line, built once)
    margin    how far solid props keep from the outline (the game's Clutter keeps 1.1 m)
    cosmetic_margin  the same for cosmetic props (more than the outline noise, or one can end up outside)
    """

    def __init__(self, lib, outline=None, mirror=None, margin=1.2, cosmetic_margin=0.4):
        assert mirror in MIRRORS, mirror
        self.lib = lib
        self.outline = outline
        self.mirror = mirror
        self.margin = margin
        self.cosmetic_margin = cosmetic_margin  # keep above the outline's noise amplitude
        self.ops = []
        self.occ = []        # (rect, y0, y1, kind): solid things (boxes, solid props, trunks)
        self.polys = {}      # id(occ entry) -> turned outline, for props not square to the axes
        self.cosm = []       # rects of cosmetic props on the ground (so litter doesn't pile up on itself)
        self.lanes = []      # (points, width, bbox)
        self.keep = []       # (x, z, r)
        self.placed = []     # every Placed prop (twins too)
        self.counts = {"block": 0, "mesh": 0, "prop": 0, "solid": 0, "cosmetic": 0, "breakable": 0}
        self.by_rule = {}
        self._rule = "prop"
        self.skipped = 0
        self.skips = []      # (rule, name, x, z, why) for every placement that didn't fit (see why())

    # ---- mirror transforms ---------------------------------------------------------------------------------

    def mpos(self, x, z):
        m = self.mirror
        return (-x, z) if m == "x" else (x, -z) if m == "z" else (-x, -z) if m == "point" else (x, z)

    def myaw_prop(self, yaw):
        """The twin's yaw for a prop (props are left-right symmetric about their own X = 0 plane)."""
        m = self.mirror
        return (-yaw) % 360 if m == "x" else (180 - yaw) % 360 if m == "z" else (yaw + 180) % 360 if m == "point" else yaw

    def mrect(self, r):
        (ax, az), (bx, bz) = self.mpos(r[0], r[1]), self.mpos(r[2], r[3])
        return (min(ax, bx), min(az, bz), max(ax, bx), max(az, bz))

    def on_axis(self, x, z, tol=0.05):
        """True if (x, z) is its own twin (on the mirror line)."""
        tx, tz = self.mpos(x, z)
        return abs(tx - x) < tol and abs(tz - z) < tol

    def _twin(self, mirror):
        return self.mirror is not None and mirror is not False

    # ---- bookkeeping -----------------------------------------------------------------------------------------

    def lane(self, pts, width, mirror=None):
        """A walking line (bot route, doorway approach) solid props keep clear of (width = the clear strip)."""
        pts = [tuple(p) for p in pts]
        xs = [p[0] for p in pts]
        zs = [p[1] for p in pts]
        self.lanes.append((pts, width, (min(xs) - width, min(zs) - width, max(xs) + width, max(zs) + width)))
        if self._twin(mirror):
            tp = [self.mpos(*p) for p in pts]
            if any(abs(a[0] - b[0]) > 0.05 or abs(a[1] - b[1]) > 0.05 for a, b in zip(pts, tp)):
                self.lane(tp, width, mirror=False)

    def keepout(self, x, z, r, mirror=None):
        """A circle solid props keep out of (spawns, hills, flags)."""
        self.keep.append((x, z, r))
        if self._twin(mirror) and not self.on_axis(x, z):
            self.keep.append((*self.mpos(x, z), r))

    def occupy(self, rect, y0=0.0, y1=3.0, kind="box", mirror=None):
        """Mark a rectangle as taken (e.g. a mesh with a solid core added elsewhere, a pond, a hole)."""
        self.occ.append((rect, y0, y1, kind))
        if self._twin(mirror):
            t = self.mrect(rect)
            if t != rect:
                self.occ.append((t, y0, y1, kind))

    def inside(self, x, z, margin=0.0):
        if self.outline is None:
            return True
        return point_in_poly(x, z, self.outline) and (margin <= 0 or poly_edge_dist(x, z, self.outline) >= margin)

    def rect_inside(self, r, margin=0.0):
        return all(self.inside(cx, cz, margin) for cx, cz in ((r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3])))

    def blocked(self, rect, gap=0.1, y0=0.0, y1=3.0, poly=None):
        """Does the rectangle (over heights y0..y1) hit anything solid already placed? With `poly` (a turned
        outline inside rect) the test is exact against turned things too, so a car parked at 45 degrees can stand
        close to a wall."""
        for e in self.occ:
            r, a, b, _ = e
            if a < y1 and y0 < b and rects_overlap(rect, r, gap):
                q = self.polys.get(id(e))
                if poly is None and q is None:
                    return True
                if _sep(poly or rect_poly(rect), q or rect_poly(r)) < gap:
                    return True
        return False

    def on_lane(self, rect, extra=0.0, poly=None):
        for pts, w, bb in self.lanes:
            if not rects_overlap(rect, bb):
                continue
            for i in range(len(pts) - 1):
                d = poly_seg_dist(poly, *pts[i], *pts[i + 1]) if poly else rect_seg_dist(rect, *pts[i], *pts[i + 1])
                if d < w / 2 + extra:
                    return True
        return False

    def in_keepout(self, rect, extra=0.0):
        return any(rect_point_dist(rect, x, z) < r + extra for x, z, r in self.keep)

    def cosmetic_blocked(self, rect, gap=0.02):
        return any(rects_overlap(rect, r, gap) for r in self.cosm)

    def free(self, name, x, z, yaw=0.0, scale=1.0, y=None, gap=0.1, lanes=True, margin=None):
        """Could `name` go here? Solid props: inside the outline (margin), off everything solid, off lanes and
        keep-outs. Cosmetic props: inside the outline, not inside a solid thing, not on another cosmetic prop."""
        p = self.lib[name]
        r = p.footprint(x, z, yaw, scale)
        m = self.margin if margin is None else margin
        if not self.rect_inside(r, m if p.solid else self.cosmetic_margin):
            return False
        base = 0.0 if y is None else y
        poly = p.outline(x, z, yaw, scale) if abs(yaw % 90) > 0.5 else None
        if p.solid:
            if self.blocked(r, gap, base, base + p.h * scale, poly):
                return False
            if y is None and lanes and (self.on_lane(r, poly=poly) or self.in_keepout(r)):
                return False
            return True
        if self.blocked(r, 0.0, base, base + max(0.05, p.h * scale), poly):
            return False
        return y is not None or not self.cosmetic_blocked(r)

    def why(self, name, x, z, yaw=0.0, scale=1.0, y=None, gap=0.1, lanes=True, margin=None):
        """Why free() says no: "outline", "solid", "lane", "keepout", "cosmetic" or "" (it fits)."""
        p = self.lib[name]
        r = p.footprint(x, z, yaw, scale)
        m = self.margin if margin is None else margin
        if not self.rect_inside(r, m if p.solid else self.cosmetic_margin):
            return "outline"
        base = 0.0 if y is None else y
        poly = p.outline(x, z, yaw, scale) if abs(yaw % 90) > 0.5 else None
        if self.blocked(r, gap if p.solid else 0.0, base, base + max(0.05, p.h * scale), poly):
            return "solid"
        if p.solid and y is None and lanes and self.on_lane(r, poly=poly):
            return "lane"
        if p.solid and y is None and lanes and self.in_keepout(r):
            return "keepout"
        if not p.solid and y is None and self.cosmetic_blocked(r):
            return "cosmetic"
        return ""

    # ---- primitives (each makes its twin) --------------------------------------------------------------------

    def block(self, x0, y0, z0, x1, y1, z1, color, mirror=None, kind="box", solid=True):
        """A Block op (and its twin). Returns the rectangle."""
        x0, x1 = min(x0, x1), max(x0, x1)
        z0, z1 = min(z0, z1), max(z0, z1)
        self.ops.append({"op": "Block", "x0": R(x0, 3), "y0": R(y0, 3), "z0": R(z0, 3), "x1": R(x1, 3), "y1": R(y1, 3), "z1": R(z1, 3), "color": color})
        self.counts["block"] += 1
        rect = (x0, z0, x1, z1)
        if solid:
            self.occ.append((rect, y0, y1, kind))
        if self._twin(mirror):
            t = self.mrect(rect)
            if any(abs(a - b) > 0.01 for a, b in zip(t, rect)):
                self.block(t[0], y0, t[1], t[2], y1, t[3], color, mirror=False, kind=kind, solid=solid)
        return rect

    def mesh(self, kind, x, z, color, mirror=None, **kw):
        """A cosmetic Mesh op (and its twin). Keyword args are the op's fields (y, width, height, depth, yaw, ...)."""
        op = {"op": "Mesh", "mesh": kind, "x": R(x, 3), "z": R(z, 3)}
        for k, v in kw.items():
            if v is None:
                continue
            op[k] = R(v, 3) if isinstance(v, float) else v
        op["color"] = color
        self.ops.append(op)
        self.counts["mesh"] += 1
        if self._twin(mirror):
            tx, tz = self.mpos(x, z)
            kw2 = dict(kw)
            yaw, pitch, roll = kw.get("yaw", 0) or 0, kw.get("pitch", 0) or 0, kw.get("roll", 0) or 0
            m = self.mirror
            if kind in ("wedge", "ramp"):
                assert not pitch and not roll, "mirrored wedges can only turn about Y"
                kw2["yaw"] = float(((-yaw) if m == "x" else (180 - yaw) if m == "z" else (yaw + 180)) % 360)
            elif m == "x":
                kw2["yaw"], kw2["pitch"], kw2["roll"] = float(-yaw % 360), float(pitch), float(-roll)
            elif m == "z":
                kw2["yaw"], kw2["pitch"], kw2["roll"] = float(-yaw % 360), float(-pitch), float(roll)
            else:
                kw2["yaw"], kw2["pitch"], kw2["roll"] = float((yaw + 180) % 360), float(pitch), float(roll)
            for k in ("yaw", "pitch", "roll"):
                if not kw2.get(k):
                    kw2.pop(k, None)
            if abs(tx - x) > 0.01 or abs(tz - z) > 0.01 or kw2 != {k: v for k, v in kw.items()}:
                self.mesh(kind, tx, tz, color, mirror=False, **kw2)

    def prop(self, name, x, z, yaw=0.0, y=None, scale=1.0, mirror=None, check=True, gap=0.1, lanes=True, margin=None, seed=0):
        """A Prop op (and its twin, both or neither). y = None stands it on the ground (the game snaps it onto a low
        box under it); a number puts it at that height (a roof, a table top). With check, solid props must be free
        (see free()); cosmetic ones must not sit inside something solid. Returns the Placed prop, or None."""
        p = self.lib[name]
        yaw = yaw % 360
        twin = self._twin(mirror) and not (self.on_axis(x, z) and abs(self.myaw_prop(yaw) - yaw) % 360 < 0.5)
        tx, tz = self.mpos(x, z) if twin else (x, z)
        tyaw = self.myaw_prop(yaw) if twin else yaw
        if twin and self.on_axis(x, z):
            twin = False  # on the line but turned: can't be its own twin, place it once
        if check:
            if not self.free(name, x, z, yaw, scale, y, gap, lanes, margin):
                self.skipped += 1
                self.skips.append((self._rule, name, round(x, 2), round(z, 2), self.why(name, x, z, yaw, scale, y, gap, lanes, margin)))
                return None
            if twin and not self.free(name, tx, tz, tyaw, scale, y, gap, lanes, margin):
                self.skipped += 1
                return None
            if twin and rects_overlap(p.footprint(x, z, yaw, scale), p.footprint(tx, tz, tyaw, scale), gap if p.solid else 0):
                self.skipped += 1
                return None
        first = self._emit(name, x, z, yaw, y, scale, seed, False)
        if twin:
            self._emit(name, tx, tz, tyaw, y, scale, seed, True)
            first.has_twin = True
        return first

    def _emit(self, name, x, z, yaw, y, scale, seed, is_twin):
        p = self.lib[name]
        op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
        if round(yaw, 1) % 360:
            op["yaw"] = R(yaw % 360, 1)
        if y is not None and y != 0:
            op["y"] = R(y, 3)
        if scale != 1:
            op["scale"] = R(scale, 3)
        self.ops.append(op)
        r = p.footprint(x, z, yaw, scale)
        base = 0.0 if y is None else y
        if p.solid:
            e = (r, base, base + p.h * scale, "prop")
            self.occ.append(e)
            if abs(yaw % 90) > 0.5:
                self.polys[id(e)] = p.outline(x, z, yaw, scale)
        elif y is None:
            self.cosm.append(r)
        pl = Placed(name, x, z, yaw, y, r, seed, is_twin)
        self.placed.append(pl)
        c = self.counts
        c["prop"] += 1
        c["solid" if p.solid else "cosmetic"] += 1
        c["breakable"] += p.breakable
        self.by_rule[self._rule] = self.by_rule.get(self._rule, 0) + 1
        return pl

    def spawn(self, x, z, mirror=None):
        self.ops.append({"op": "Spawn", "x": R(x), "z": R(z)})
        if self._twin(mirror) and not self.on_axis(x, z):
            tx, tz = self.mpos(x, z)
            self.ops.append({"op": "Spawn", "x": R(tx), "z": R(tz)})

    def waypoint(self, x, z, mirror=None):
        self.ops.append({"op": "Waypoint", "x": R(x), "z": R(z)})
        if self._twin(mirror) and not self.on_axis(x, z):
            tx, tz = self.mpos(x, z)
            self.ops.append({"op": "Waypoint", "x": R(tx), "z": R(tz)})

    def raw(self, op):
        """Any other op, as is (no twin)."""
        self.ops.append(op)

    def rule(self, name):
        """Label what follows (for the per-rule counts in stats())."""
        self._rule = name
        return self

    # ---- placement rules -------------------------------------------------------------------------------------

    def along(self, names, pts, spacing, offset=0.0, side=1, face="line", start=None, jitter=0.0, yaw_jitter=0.0,
              seed=0, y=None, every=1, skip_blocked=True, mirror=None, lanes=False, gap=0.1, alternate=False):
        """Things that follow a line (a kerb, a pavement, a fence): one every `spacing` metres, set back `offset`
        metres to the line's `side` (1 = left of the direction of travel, -1 = right; left of +X is -Z).
        face: "line" (front towards the line: lamps, hydrants, signs, mailboxes, bins at the kerb), "away" (back to the
        line), "along" (+X along the line: parked cars, benches, hedges, fences), "across" (+X away from it),
        "toward" (+X towards it: a street lamp's arm over the road), or a fixed yaw. `names` cycles (or picks at random with seed when it's a list longer than one and `seed` given).
        alternate=True swaps the side every placement (streetlights on both pavements, staggered).
        `lanes=False` by default: things along a road are meant to stand by the route, only off its walking strip
        if a lane covers it. Returns the Placed list."""
        rnd = random.Random(seed)
        out = []
        for k, (x, z, tx, tz) in enumerate(resample(pts, spacing, start)):
            if k % every:
                continue
            sd = side * (-1 if alternate and k % 2 else 1)
            nx, nz = tz * sd, -tx * sd           # left normal for sd = 1 (tangent (1,0) -> normal (0,-1))
            j = rnd.uniform(-jitter, jitter) if jitter else 0.0
            px, pz = x + nx * offset + tx * j, z + nz * offset + tz * j
            if isinstance(face, (int, float)):
                yaw = float(face)
            elif face == "line":
                yaw = face_yaw(-nx, -nz)
            elif face == "away":
                yaw = face_yaw(nx, nz)
            elif face == "along":
                yaw = along_yaw(tx, tz)
            elif face == "across":
                yaw = along_yaw(nx, nz)
            elif face == "toward":       # +X towards the line: a street lamp's arm over the road
                yaw = along_yaw(-nx, -nz)
            else:
                raise ValueError(face)
            if yaw_jitter:
                yaw += rnd.uniform(-yaw_jitter, yaw_jitter)
            name = names[k % len(names)] if seed == 0 or len(names) == 1 else rnd.choice(names)
            pl = self.prop(name, px, pz, yaw, y=y, mirror=mirror, check=skip_blocked, lanes=lanes, gap=gap, seed=seed * 7919 + k)
            if pl:
                out.append(pl)
        return out

    def against(self, rect, side, names, at=None, spacing=None, gap=0.04, outside=True, y=None, seed=0, mirror=None,
                lanes=True, check=True, face="out"):
        """Props backed against one face of a rectangle (a building, a room): side is "N", "S", "E" or "W" (north =
        -Z). outside=True stands them outside the wall facing out (AC units, meters, dumpsters, bins by a back door);
        outside=False inside the room facing in (furniture). `at` = fractions 0..1 along the face (west to east, north
        to south), or `spacing` metres. face: "out" (front away from the wall) or "along" (+X along the wall).
        Returns the Placed list."""
        x0, z0, x1, z1 = rect
        rnd = random.Random(seed)
        if side in "NS":
            a0, a1 = x0, x1
        else:
            a0, a1 = z0, z1
        L = a1 - a0
        if at is None:
            n = max(1, int(L / (spacing or 2.0)))
            at = [(k + 0.5) / n for k in range(n)]
        # outward normal of the face
        nrm = {"N": (0, -1), "S": (0, 1), "W": (-1, 0), "E": (1, 0)}[side]
        if not outside:
            nrm = (-nrm[0], -nrm[1])
        out = []
        for k, f in enumerate(at):
            name = names[k % len(names)] if not seed else rnd.choice(names)
            p = self.lib[name]
            yaw = face_yaw(*nrm) if face == "out" else along_yaw(-nrm[1], nrm[0])
            back = -p.z0 if face == "out" else p.z1   # origin to the side that touches the wall
            a = a0 + L * f
            if side == "N":
                wx, wz = a, z0
            elif side == "S":
                wx, wz = a, z1
            elif side == "W":
                wx, wz = x0, a
            else:
                wx, wz = x1, a
            d = back + gap
            px, pz = wx + nrm[0] * d, wz + nrm[1] * d
            pl = self.prop(name, px, pz, yaw, y=y, mirror=mirror, lanes=lanes, check=check, gap=0.0, seed=seed * 31 + k)
            if pl:
                out.append(pl)
        return out

    def on_roof(self, rect, roof_y, names, count, seed=0, margin=0.6, mirror=None, gap=0.3, yaws=(0, 90, 180, 270)):
        """Roof props on a flat roof rectangle at height roof_y: no overlaps, `margin` in from the edge."""
        rnd = random.Random(seed)
        x0, z0, x1, z1 = rect[0] + margin, rect[1] + margin, rect[2] - margin, rect[3] - margin
        out = []
        tries = 0
        while len(out) < count and tries < count * 40:
            tries += 1
            name = rnd.choice(names)
            p = self.lib[name]
            yaw = rnd.choice(yaws)
            x, z = rnd.uniform(x0, x1), rnd.uniform(z0, z1)
            r = p.footprint(x, z, yaw)
            if r[0] < x0 or r[2] > x1 or r[1] < z0 or r[3] > z1:
                continue
            if self.blocked(r, gap, roof_y + 0.01, roof_y + max(0.05, p.h)):
                continue
            if any(rects_overlap(r, o.rect, gap) for o in out):
                continue
            pl = self.prop(name, x, z, yaw, y=roof_y, mirror=mirror, check=False, seed=seed * 17 + tries)
            if pl:
                if not p.solid:  # keep later roof props off cosmetic ones too
                    self.occ.append((r, roof_y, roof_y + p.h, "roofcosm"))
                    if self._twin(mirror):
                        self.occ.append((self.mrect(r), roof_y, roof_y + p.h, "roofcosm"))
                out.append(pl)
        return out

    def on_top(self, support, names, count=None, seed=0, mirror=None, inset=0.05, yaws=None):
        """Small cosmetic things on top of a placed prop (a table, a counter, a desk): y = its top surface."""
        p = self.lib[support.name]
        rnd = random.Random(seed or support.seed + 1)
        count = count if count is not None else max(1, int(p.w * p.d / 0.35))
        out = []
        used = []
        for k in range(count * 6):
            if len(out) >= count:
                break
            name = rnd.choice(names)
            q = self.lib[name]
            lx = rnd.uniform(p.x0 + inset - q.x0, p.x1 - inset - q.x1)
            lz = rnd.uniform(p.z0 + inset - q.z0, p.z1 - inset - q.z1)
            if lx != lx or lz != lz:
                continue
            if (p.x1 - inset - q.x1) < (p.x0 + inset - q.x0) or (p.z1 - inset - q.z1) < (p.z0 + inset - q.z0):
                continue
            dx, dz = rot(lx, lz, support.yaw)
            yaw = (support.yaw + (rnd.choice(yaws) if yaws else rnd.uniform(-25, 25))) % 360
            r = q.footprint(support.x + dx, support.z + dz, yaw)
            if any(rects_overlap(r, u, 0.02) for u in used):
                continue
            used.append(r)
            base = (support.y or 0.0) + p.top
            pl = self.prop(name, support.x + dx, support.z + dz, yaw, y=base, mirror=mirror if support.has_twin else False, check=False, seed=k)
            if pl:
                out.append(pl)
        return out

    def around(self, anchor, names, count, r0=None, r1=2.0, seed=0, mirror=None, solid_ok=False, falloff=1.5, yaw="random", tries=12):
        """A scatter round an anchor (a Placed prop, or (x, z)): litter and bags round bins, ducks round a pond.
        Distances r0..r1 from the anchor's footprint edge (r0 default 0.05), denser near it (falloff > 1).
        Cosmetic props only unless solid_ok. Returns the Placed list."""
        rnd = random.Random(seed)
        if isinstance(anchor, Placed):
            rect = anchor.rect
        else:
            rect = (anchor[0], anchor[1], anchor[0], anchor[1])
        r0 = 0.05 if r0 is None else r0
        cx, cz = (rect[0] + rect[2]) / 2, (rect[1] + rect[3]) / 2
        hx, hz = (rect[2] - rect[0]) / 2, (rect[3] - rect[1]) / 2
        out = []
        for k in range(count):
            for _ in range(tries):
                name = rnd.choice(names)
                if self.lib[name].solid and not solid_ok:
                    raise ValueError(f"around(): {name} is solid (pass solid_ok=True)")
                a = rnd.uniform(0, math.tau)
                d = r0 + (r1 - r0) * rnd.random() ** falloff
                # a point on the rounded rectangle at distance d from the footprint
                ex = math.cos(a)
                ez = math.sin(a)
                px = cx + ex * (hx + d)
                pz = cz + ez * (hz + d)
                yy = rnd.uniform(0, 360) if yaw == "random" else float(yaw)
                pl = self.prop(name, px, pz, yy, mirror=mirror, seed=seed * 13 + k)
                if pl:
                    out.append(pl)
                    break
        return out

    def scatter(self, names, area, count, seed=0, yaws=None, gap=0.6, mirror=None, margin=None, tries=60, lanes=True, y=None,
                weights=None):
        """A deterministic scatter over `area`: a rectangle (x0, z0, x1, z1) or a polygon [(x, z), ...]. Every
        placement obeys the outline margin, the lanes, the keep-outs and the occupancy (gap = clearance to other
        solid things, so players fit between). yaws: a list to pick from (default any angle for cosmetic, steps of
        90 for solid props). Returns the Placed list."""
        rnd = random.Random(seed)
        poly = area if isinstance(area[0], (list, tuple)) else None
        if poly:
            bx0, bz0 = min(p[0] for p in poly), min(p[1] for p in poly)
            bx1, bz1 = max(p[0] for p in poly), max(p[1] for p in poly)
        else:
            bx0, bz0, bx1, bz1 = area
        out = []
        t = 0
        while len(out) < count and t < count * tries:
            t += 1
            name = rnd.choices(names, weights=weights)[0] if weights else rnd.choice(names)
            x, z = rnd.uniform(bx0, bx1), rnd.uniform(bz0, bz1)
            if poly and not point_in_poly(x, z, poly):
                continue
            p = self.lib[name]
            if yaws is not None:
                yaw = rnd.choice(yaws) if isinstance(yaws, (list, tuple)) else rnd.uniform(-yaws, yaws)
            else:
                yaw = rnd.uniform(0, 360) if not p.solid else rnd.choice((0, 90, 180, 270)) + rnd.uniform(-4, 4)
            pl = self.prop(name, x, z, yaw, y=y, mirror=mirror, gap=gap if p.solid else 0.02, lanes=lanes, margin=margin, seed=seed * 101 + t)
            if pl:
                out.append(pl)
        return out

    # ---- rooms --------------------------------------------------------------------------------------------

    ROOMS = {
        # kind: list of (names to try, wall preference, count, on-top dressing)
        "kitchen": [(["Fridge"], "wall", 1, None), (["Kitchen Sink"], "wall", 1, ["Stacked Plates", "Kettle"]),
                    (["Cooker"], "wall", 1, ["Cooking Pot", "Frying Pan"]),
                    (["Kitchen Counter"], "wall", 2, ["Microwave", "Toaster", "Kettle", "Coffee Maker", "Knife Block"]),
                    (["Dishwasher"], "wall", 1, None), (["Pedal Bin"], "corner", 1, None),
                    (["Dining Table"], "middle", 1, ["Stacked Plates", "Book Pile"]), (["Dining Chair"], "chairs", 4, None)],
        "living": [(["Fabric Sofa"], "wall", 1, None), (["TV Stand"], "wall", 1, None), (["Armchair"], "wall", 1, None),
                   (["Bookshelf"], "wall", 1, None), (["Floor Lamp"], "corner", 1, None), (["Coffee Table"], "middle", 1, ["Book Pile", "Laptop"]),
                   (["Rug", "Striped Rug"], "floor", 1, None), (["Fireplace"], "wall", 1, None), (["Office Plant"], "corner", 1, None),
                   (["Radiator"], "wall", 1, None), (["Toy Blocks", "Teddy Bear", "Sports Balls", "Acoustic Guitar"], "floor", 2, None)],
        "dining": [(["Dining Table"], "middle", 1, ["Stacked Plates", "Cooking Pot"]), (["Dining Chair"], "chairs", 4, None),
                   (["Chest of Drawers"], "wall", 1, ["Table Lamp"]), (["Upright Piano"], "wall", 1, None), (["Fish Tank"], "wall", 1, None)],
        "bedroom": [(["Double Bed", "Single Bed"], "wall", 1, None), (["Bedside Table"], "wall", 1, ["Table Lamp"]), (["Wardrobe"], "wall", 1, None),
                    (["Chest of Drawers"], "wall", 1, ["Book Pile"]), (["Laundry Basket", "Suitcase", "Backpack"], "floor", 2, None),
                    (["Striped Rug"], "floor", 1, None)],
        "garage": [(["Metal Shelving"], "wall", 2, None), (["Kitchen Counter"], "wall", 1, ["Toolbox", "Paint Cans"]),
                   (["Chest Freezer"], "wall", 1, None), (["Tumble Dryer", "Washing Machine"], "wall", 1, None),
                   (["Paint Cans", "Toolbox", "Jerry Can", "Bucket", "Step Ladder", "Car Wheel", "Broom"], "floor", 4, None),
                   (["Bicycle", "Kick Scooter"], "floor", 1, None)],
        "utility": [(["Washing Machine"], "wall", 1, None), (["Tumble Dryer"], "wall", 1, None), (["Ironing Board"], "floor", 1, None),
                    (["Laundry Basket", "Mop and Bucket", "Vacuum Cleaner"], "floor", 2, None)],
        "office": [(["Office Desk"], "wall", 2, ["Computer Monitor", "Desk Phone", "Laptop"]), (["Office Chair"], "chairs", 2, None),
                   (["Filing Cabinet"], "wall", 2, None), (["Metal Shelving", "Bookshelf"], "wall", 1, None), (["Water Cooler"], "corner", 1, None),
                   (["Office Plant"], "corner", 1, None), (["Archive Boxes"], "floor", 1, None)],
    }

    def room(self, rect, kind, doors=(), seed=0, mirror=None, floor_y=None, clear=1.3, wall_gap=0.03, items=None):
        """Furnish a room (inside rectangle `rect`, the inner faces of its walls) from a template in Site.ROOMS (or
        `items`, the same format). doors = [(side, centre along the wall, width)]: the floor `clear` metres in front
        of each door stays empty, and a lane runs between the doors so bots can cross the room. Furniture backs onto
        the walls ("wall"), sits in corners ("corner"), in the middle ("middle"), round the last middle piece
        ("chairs") or on the floor anywhere ("floor", cosmetic). Returns the Placed list."""
        rnd = random.Random(seed)
        x0, z0, x1, z1 = rect
        # door clearances (temporary solid rects so nothing goes there)
        tmp = []
        centres = []
        for side, c, w in doors:
            if side == "N":
                r = (c - w / 2 - 0.2, z0, c + w / 2 + 0.2, z0 + clear); centres.append((c, z0 + 0.5))
            elif side == "S":
                r = (c - w / 2 - 0.2, z1 - clear, c + w / 2 + 0.2, z1); centres.append((c, z1 - 0.5))
            elif side == "W":
                r = (x0, c - w / 2 - 0.2, x0 + clear, c + w / 2 + 0.2); centres.append((x0 + 0.5, c))
            else:
                r = (x1 - clear, c - w / 2 - 0.2, x1, c + w / 2 + 0.2); centres.append((x1 - 0.5, c))
            tmp.append((r, 0.0, 3.0, "door"))
        # a walking line from door to door (or a short way in from a single door) that solid furniture keeps off
        if len(centres) >= 2:
            self.lane(centres, 1.0, mirror=mirror)
        elif centres:
            cx, cz = centres[0]
            mx, mz = (x0 + x1) / 2, (z0 + z1) / 2
            L = math.hypot(mx - cx, mz - cz) or 1.0
            self.lane([(cx, cz), (cx + (mx - cx) / L * 1.5, cz + (mz - cz) / L * 1.5)], 1.0, mirror=mirror)
        self.occ.extend(tmp)
        twin_tmp = []
        if self._twin(mirror):
            twin_tmp = [(self.mrect(r), a, b, k) for r, a, b, k in tmp]
            self.occ.extend(twin_tmp)
        out = []
        last_mid = None
        sides = ["N", "S", "W", "E"]
        for names, where, count, dressing in (items or self.ROOMS[kind]):
            for k in range(count):
                done = None
                for attempt in range(40):
                    name = rnd.choice(names)
                    p = self.lib[name]
                    if where in ("wall", "corner"):
                        side = rnd.choice(sides)
                        inward = {"N": (0, 1), "S": (0, -1), "W": (1, 0), "E": (-1, 0)}[side]
                        yaw = face_yaw(*inward)
                        depth = -p.z0 + wall_gap
                        half = (p.x1 - p.x0) / 2
                        if side in "NS":
                            lo, hi = x0 + half + 0.05, x1 - half - 0.05
                        else:
                            lo, hi = z0 + half + 0.05, z1 - half - 0.05
                        if hi < lo:
                            continue
                        a = rnd.choice((lo, hi)) if where == "corner" else rnd.uniform(lo, hi)
                        if side == "N":
                            px, pz = a, z0 + depth
                        elif side == "S":
                            px, pz = a, z1 - depth
                        elif side == "W":
                            px, pz = x0 + depth, a
                        else:
                            px, pz = x1 - depth, a
                    elif where == "middle":
                        spread = min(1.0, attempt / 12)  # the centre first, then anywhere a metre in from the walls
                        px = (x0 + x1) / 2 + rnd.uniform(-1, 1) * spread * max(0.0, (x1 - x0) / 2 - 1.0)
                        pz = (z0 + z1) / 2 + rnd.uniform(-1, 1) * spread * max(0.0, (z1 - z0) / 2 - 1.0)
                        yaw = 0 if (x1 - x0) >= (z1 - z0) else 90  # long side along the room
                    elif where == "chairs":
                        if last_mid is None:
                            break
                        t = self.lib[last_mid.name]
                        # around the table's long sides
                        lx = rnd.choice((-0.45, 0.45)) * (t.w / 1.4)
                        sgn = rnd.choice((-1, 1))
                        lz = sgn * (t.d / 2 + 0.32)
                        dx, dz = rot(lx, lz, last_mid.yaw)
                        px, pz = last_mid.x + dx, last_mid.z + dz
                        yaw = face_yaw(*rot(0, -sgn, last_mid.yaw)) + rnd.uniform(-8, 8)  # square to the table
                    else:  # floor
                        px, pz = rnd.uniform(x0 + 0.3, x1 - 0.3), rnd.uniform(z0 + 0.3, z1 - 0.3)
                        yaw = rnd.uniform(0, 360)
                    r = p.footprint(px, pz, yaw)
                    if r[0] < x0 - 0.01 or r[2] > x1 + 0.01 or r[1] < z0 - 0.01 or r[3] > z1 + 0.01:
                        continue
                    lanes = where not in ("floor",)
                    if where == "chairs":
                        pl = self.prop(name, px, pz, yaw, mirror=mirror, gap=0.02, lanes=False, margin=0.0)
                    elif p.solid:
                        pl = self.prop(name, px, pz, yaw, mirror=mirror, gap=0.0 if where in ("wall", "corner") else 0.5, lanes=True, margin=0.0)
                    else:
                        pl = self.prop(name, px, pz, yaw, mirror=mirror, margin=0.0)
                    if pl:
                        done = pl
                        break
                if done is None:
                    continue
                out.append(done)
                if where == "middle":
                    last_mid = done
                if dressing:
                    out += self.on_top(done, dressing, count=1 + rnd.randrange(2), seed=seed * 7 + k + len(out), mirror=mirror)
        # drop the door clearances (keep the lanes: later scatters shouldn't block the room either)
        for t in tmp:
            self.occ.remove(t)
        for t in twin_tmp:
            self.occ.remove(t)
        return out

    # ---- output -------------------------------------------------------------------------------------------

    def solid_rects(self, min_h=1.6, max_y0=1.6):
        """Rectangles of solid things tall enough to block sight at eye height (for sightline checks)."""
        return [r for r, y0, y1, k in self.occ if y1 > min_h and y0 < max_y0 and k not in ("door", "roofcosm")]

    def sightline(self, a, b, tall=None, step=0.25):
        """True if the straight line from a to b (eye height) is blocked by a tall solid thing or leaves the outline."""
        tall = self.solid_rects() if tall is None else tall
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        n = max(2, int(L / step))
        for i in range(1, n):
            x = a[0] + (b[0] - a[0]) * i / n
            z = a[1] + (b[1] - a[1]) * i / n
            if not self.inside(x, z):
                return True
            for r in tall:
                if r[0] <= x <= r[2] and r[1] <= z <= r[3]:
                    return True
        return False

    def stats(self):
        """A dict of counts: blocks, meshes, props (solid / cosmetic / breakable), per rule, and per category."""
        cats = {}
        for p in self.placed:
            info = self.lib[p.name]
            key = (info.category, "solid" if info.solid else "cosmetic")
            cats[key] = cats.get(key, 0) + 1
        return dict(self.counts, rules=dict(self.by_rule), categories=cats, skipped=self.skipped)

    def preview(self, png, bounds, scale=8, spawns=(), hills=(), flags=(), extra=None):
        """A top-down PNG: the outline, lanes, solid things (boxes grey, props brown/blue), cosmetic props (light),
        spawns (blue/red/white dots), hills (yellow rings) and flags. bounds = (x0, z0, x1, z1)."""
        from PIL import Image, ImageDraw
        bx0, bz0, bx1, bz1 = bounds
        W, H = int((bx1 - bx0) * scale), int((bz1 - bz0) * scale)
        img = Image.new("RGB", (W, H), (40, 44, 40))
        d = ImageDraw.Draw(img)

        def P(x, z):
            return ((x - bx0) * scale, (z - bz0) * scale)
        if self.outline:
            d.polygon([P(*p) for p in self.outline], fill=(96, 128, 80))
        for op in self.ops:  # flat meshes on the ground (roads, pavements, drives, ponds), in their own colours
            if op["op"] != "Mesh" or op.get("y", 0) > 0.2 or op.get("height", 1) > 0.2 or op["mesh"] not in ("box", "cylinder", "tube"):
                continue
            col = op["color"]
            rgb = tuple(int(col[i:i + 2], 16) for i in (1, 3, 5)) if isinstance(col, str) else tuple(int(v * 255) for v in col)
            x, z = op["x"], op["z"]
            if op["mesh"] == "box":
                w, dd = op.get("width", 1) / 2, op.get("depth", 1) / 2
                pts = [(x + a, z + b) for a, b in (rot(-w, -dd, op.get("yaw", 0)), rot(w, -dd, op.get("yaw", 0)), rot(w, dd, op.get("yaw", 0)), rot(-w, dd, op.get("yaw", 0)))]
                d.polygon([P(*q) for q in pts], fill=rgb)
            else:
                rx = op.get("radius", op.get("width", 1) / 2)
                rz = op.get("radius", op.get("depth", 1) / 2)
                d.ellipse([P(x - rx, z - rz), P(x + rx, z + rz)], outline=rgb if op["mesh"] == "tube" else None, fill=None if op["mesh"] == "tube" else rgb, width=3)
        if extra:
            extra(d, P)
        for pts, w, _ in self.lanes:  # the clear strips (full width, faint) and their centre lines
            d.line([P(*p) for p in pts], fill=(124, 150, 104), width=max(1, int(w * scale)))
            d.line([P(*p) for p in pts], fill=(200, 200, 150), width=1)
        for r in self.cosm:
            d.rectangle([P(r[0], r[1]), P(r[2], r[3])], outline=(220, 220, 200))
        for r, y0, y1, k in self.occ:
            if k == "roofcosm":
                continue
            c = (70, 70, 80) if k == "box" else (60, 40, 20) if k == "trunk" else (170, 110, 60) if y1 - y0 < 1.3 else (120, 70, 150)
            if y0 > 1.7:
                d.rectangle([P(r[0], r[1]), P(r[2], r[3])], outline=(200, 200, 230))
            else:
                d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=c)
        for (x, z), c in spawns:
            d.ellipse([P(x - 0.6, z - 0.6), P(x + 0.6, z + 0.6)], fill=c)
        for x, z, r in hills:
            d.ellipse([P(x - r, z - r), P(x + r, z + r)], outline=(255, 230, 0), width=3)
        for x, z in flags:
            d.rectangle([P(x - 0.6, z - 0.6), P(x + 0.6, z + 0.6)], fill=(255, 255, 255))
        for gx in range(int(bx0 // 10 * 10), int(bx1) + 1, 10):
            d.line([P(gx, bz0), P(gx, bz0 + 1)], fill=(255, 255, 255))
            d.text(P(gx + 0.3, bz0 + 0.5), str(gx), fill=(255, 255, 255))
        for gz in range(int(bz0 // 10 * 10), int(bz1) + 1, 10):
            d.text(P(bx0 + 0.3, gz), str(gz), fill=(255, 255, 255))
        img.save(png)
        return img


# ------------------------------------------------------------------------------------------------ foliage


class FoliageBuilder:
    """Builds a map's "foliage" section: regions, items (trees), paths, exclude and include zones, surfaces.
    Trees placed with tree() also mark their trunk as solid in the Site (so props keep off them)."""

    def __init__(self, site=None, seed=0):
        self.site = site
        self.data = {"seed": seed, "paths": [], "regions": [], "items": []}

    def region(self, type, density, area, mirror=None, **kw):
        """A scatter region; area is (x0, z0, x1, z1), (x, z, r) for a circle, or a polygon."""
        reg = {"type": type, "density": density}
        if isinstance(area[0], (list, tuple)):
            reg["polygon"] = [[R(p[0]), R(p[1])] for p in area]
        elif len(area) == 3:
            reg["circle"] = [R(v) for v in area]
        else:
            reg["rect"] = [R(min(area[0], area[2])), R(min(area[1], area[3])), R(max(area[0], area[2])), R(max(area[1], area[3]))]
        reg.update(kw)
        self.data["regions"].append(reg)
        if self.site and self.site._twin(mirror):
            t = dict(reg)
            if "polygon" in reg:
                t["polygon"] = [[R(v) for v in self.site.mpos(*p)] for p in reg["polygon"]][::-1]
            elif "circle" in reg:
                x, z, r = reg["circle"]
                t["circle"] = [*[R(v) for v in self.site.mpos(x, z)], r]
            else:
                rr = self.site.mrect(reg["rect"])
                t["rect"] = [R(v) for v in rr]
            if t != reg:
                if "seed" in t:
                    t["seed"] = t["seed"] + 1000
                self.data["regions"].append(t)

    def strip(self, type, density, pts, width, mirror=None, **kw):
        """A region along a polyline (a hedge row, a verge, a flower border), `width` wide, as polygons per segment."""
        for i in range(len(pts) - 1):
            (ax, az), (bx, bz) = pts[i], pts[i + 1]
            L = math.hypot(bx - ax, bz - az)
            if L < 1e-6:
                continue
            nx, nz = -(bz - az) / L * width / 2, (bx - ax) / L * width / 2
            poly = [(ax + nx, az + nz), (bx + nx, bz + nz), (bx - nx, bz - nz), (ax - nx, az - nz)]
            self.region(type, density, poly, mirror=mirror, **kw)

    def tree(self, x, z, height, type="tree", mirror=None, trunk=0.35):
        self.data["items"].append({"type": type, "x": R(x), "z": R(z), "height": R(height, 1)})
        if self.site:
            self.site.occ.append(((x - trunk, z - trunk, x + trunk, z + trunk), 0.0, height, "trunk"))
        if self.site and self.site._twin(mirror) and not self.site.on_axis(x, z):
            tx, tz = self.site.mpos(x, z)
            self.tree(tx, tz, height, type, mirror=False, trunk=trunk)

    def path(self, pts, width, density=None, mirror=None):
        p = {"points": [[R(a), R(b)] for a, b in pts], "width": width}
        if density:
            p["density"] = density
        self.data["paths"].append(p)
        if self.site and self.site._twin(mirror):
            tp = [list(self.site.mpos(*q)) for q in pts]
            if tp != [list(q) for q in pts]:
                self.path(tp, width, density, mirror=False)

    def zone(self, kind, shape, mirror=None, **kw):
        """kind "exclude" or "include"; shape "rect" (center, halfX, halfZ), "circle" (center, radius) or "polygon"."""
        z = {"shape": shape}
        z.update(kw)
        self.data.setdefault(kind, []).append(z)
        if self.site and self.site._twin(mirror):
            t = dict(z)
            if "center" in z:
                t["center"] = [R(v) for v in self.site.mpos(*z["center"])]
            if "points" in z:
                t["points"] = [[R(v) for v in self.site.mpos(*p)] for p in z["points"]][::-1]
            if t != z:
                self.data[kind].append(t)

    def surfaces(self, **allow):
        self.data["surfaces"] = allow
