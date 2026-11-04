"""No coplanar faces (z-fighting) for models made of boxes (CLAUDE.md, "No coplanar faces").

Two drawn boxes of one model that have a face each on the same plane, facing the same way, with overlapping area,
flicker through each other. This is the same rule as Source/App/ZFight.cs, which the "No z-fighting" self-test uses to
check shipped data, and the way to correct it: move the smallest thing by the smallest amount.

    resolve(boxes, sockets)        construct parts: the generators' dicts ({"min": [...], "max": [...], "detail": true, ...})
    settle_props(parts)            prop parts ({"size", "offset", "rotation", "color", ...}), any rotation
    gap_for(size of the patch)     how far apart two faces need to be

How far (gap_for): the window's depth buffer is 24-bit and the near plane 0.05 m, so two planes at distance z stay apart
only if they are more than z * z / (0.05 * 2^24) = 1.2e-6 z^2 m apart: 1 mm at 29 m, 2 mm at 41 m, 5 mm at 65 m, 10 mm at
92 m, 40 mm at 183 m. The size of the shared patch (the square root of its area) says how far off the flicker can still
be seen: at 90 m a pixel is about 10 cm, so a hand-sized patch or a thin strip has gone by then. Hence 2 mm on small
patches, up to 10 mm on 8 m ones, and for capital pieces 1/800 of the patch, at most 50 mm (a 32 m plate: 40 mm, about
0.1 % of it).

What moves: the less seen face (a detail before a solid, the smaller before the bigger), never a face with a socket on
it, nor the underside of a prop standing on the ground. A face lying inside the other one comes proud (an inset would
hide it), as does one pressed against a neighbour (an inset would open a gap). Others are inset. A piece too thin
to inset moves whole, so it keeps its thickness. Corrections never grow from pass to pass: each face tries one gap, then
two, then three, in either direction, and takes the first that leaves no hit.
"""

import math

TOL = 0.00055  # planes closer than this are the same plane (ZFight.PlaneTolerance, plus a margin: the game works in floats)
MIN_AREA = 0.9e-5  # ZFight.MinArea, with the same margin
SLIVER = 0.00045  # a patch thinner than this is two boxes meeting edge to edge (the game: 0.5 mm, here with the margin)
AO_TOP = 0.45  # props: the baked ground shade stops changing at this height (PropMeshes.AddPart)
THIN = 4  # a piece thinner than this many gaps moves whole instead of being inset


def gap_for(size):
    """The correction for a shared patch of `size` metres, the square root of its area (the module's comment says why)."""
    if size <= 0.6:
        return 0.002
    if size <= 1.5:
        return 0.003
    if size <= 4:
        return 0.005
    if size <= 8:
        return 0.010
    return min(0.05, round(size / 800, 3))


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def norm(a):
    n = math.sqrt(dot(a, a))
    return [v / n for v in a]


def axes_of(rot):
    """The box's three axes as world vectors: rotation [pitch, yaw, roll] applied as yaw * pitch * roll (PropPart.Rotation)."""
    p, y, r = [math.radians(v) for v in rot]
    cp, sp, cy, sy, cr, sr = math.cos(p), math.sin(p), math.cos(y), math.sin(y), math.cos(r), math.sin(r)
    ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    rx = [[1, 0, 0], [0, cp, -sp], [0, sp, cp]]
    rz = [[cr, -sr, 0], [sr, cr, 0], [0, 0, 1]]

    def mul(a, b):
        return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]

    m = mul(mul(ry, rx), rz)
    return [[m[row][k] for row in range(3)] for k in range(3)]


class Box:
    """One drawn box: centre, half sizes, three unit axes, and what may be done to it."""

    __slots__ = ("c", "h", "ax", "look", "moving", "detail", "fixed", "no_out", "whole", "ref")

    def __init__(self, c, h, ax=None, look=None, moving=False, detail=False, whole=True, ref=None):
        self.c, self.h = list(c), list(h)
        self.ax = ax or [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
        self.look, self.moving, self.detail, self.whole, self.ref = look, moving, detail, whole, ref
        self.fixed = set()  # faces (axis, sign) that must stay
        self.no_out = set()  # faces that may go in but not out

    def reach(self):
        return math.sqrt(dot(self.h, self.h))

    def face(self, a, s):
        """(normal, plane, 4 corners)."""
        n = [s * v for v in self.ax[a]]
        ca = [self.c[i] + n[i] * self.h[a] for i in range(3)]
        u, w = (a + 1) % 3, (a + 2) % 3
        du = [v * self.h[u] for v in self.ax[u]]
        dw = [v * self.h[w] for v in self.ax[w]]
        corners = [[ca[i] + su * du[i] + sw * dw[i] for i in range(3)] for su, sw in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        return n, dot(n, self.c) + self.h[a], corners

    def contains(self, p, tol):
        d = [p[i] - self.c[i] for i in range(3)]
        return all(abs(dot(d, self.ax[k])) <= self.h[k] + tol for k in range(3))

    def area(self, a):
        return 4 * self.h[(a + 1) % 3] * self.h[(a + 2) % 3]

    def snapshot(self):
        return list(self.c), list(self.h)

    def restore(self, snap):
        self.c, self.h = list(snap[0]), list(snap[1])


def _clip(n, a, b):
    """The overlap of two coplanar rectangles: (area, centre, polygon)."""
    e1 = norm(cross(n, [0, 1, 0])) if abs(n[1]) < 0.9 else norm(cross(n, [1, 0, 0]))
    e2 = cross(n, e1)
    o = a[0]

    def flat(p):
        d = [p[i] - o[i] for i in range(3)]
        return dot(d, e1), dot(d, e2)

    subject = [flat(p) for p in a]
    clip = [flat(p) for p in b]
    signed = sum(clip[k][0] * clip[(k + 1) % 4][1] - clip[k][1] * clip[(k + 1) % 4][0] for k in range(4))
    sgn = 1 if signed >= 0 else -1

    def side(c0, c1, p):
        return ((c1[0] - c0[0]) * (p[1] - c0[1]) - (c1[1] - c0[1]) * (p[0] - c0[0])) * sgn

    for k in range(4):
        if not subject:
            break
        c0, c1 = clip[k], clip[(k + 1) % 4]
        src, subject = subject, []
        for m, p in enumerate(src):
            q = src[(m + 1) % len(src)]
            ps, qs = side(c0, c1, p), side(c0, c1, q)
            t = ps / (ps - qs) if ps != qs else 0
            cut = (p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t)
            if ps >= 0:
                subject.append(p)
                if qs < 0:
                    subject.append(cut)
            elif qs >= 0:
                subject.append(cut)
    if len(subject) < 3:
        return 0, None, None
    xs, ys = [p[0] for p in subject], [p[1] for p in subject]
    if max(xs) - min(xs) < SLIVER or max(ys) - min(ys) < SLIVER:
        return 0, None, None
    area = abs(sum(subject[k][0] * subject[(k + 1) % len(subject)][1] - subject[k][1] * subject[(k + 1) % len(subject)][0] for k in range(len(subject)))) / 2
    cx, cy = sum(xs) / len(xs), sum(ys) / len(ys)

    def world(p):
        return [o[i] + e1[i] * p[0] + e2[i] * p[1] for i in range(3)]

    return area, world((cx, cy)), [world(p) for p in subject]


def _covered(boxes, i, j, n, centre, poly):
    for k in range(-1, len(poly)):
        p = centre if k < 0 else [poly[k][q] + (centre[q] - poly[k][q]) * 0.1 for q in range(3)]
        out = [p[q] + n[q] * 0.001 for q in range(3)]
        if not any(m not in (i, j) and b.contains(out, 1e-5) for m, b in enumerate(boxes)):
            return False
    return True


def _same_look_hidden(a, b, ca, cb, shade):
    """Two faces of one plain look draw the same pixels. In a prop the baked ground shade can differ below AO_TOP."""
    if a.look is None or a.look != b.look:
        return False
    if not shade:
        return True
    ya, yb = [p[1] for p in ca], [p[1] for p in cb]
    if min(ya) >= AO_TOP and min(yb) >= AO_TOP:
        return True
    return abs(min(ya) - min(yb)) < 0.001 and abs(max(ya) - max(yb)) < 0.001


def find(boxes, on_ground=False, shade=False, only=None):
    """Coplanar same-facing overlapping pairs not covered by a third box: dicts with the two faces, area and patch edge."""
    found = []
    n = len(boxes)
    for i in range(n) if only is None else [only]:
        a = boxes[i]
        if a.moving:
            continue
        for j in range(n) if only is not None else range(i + 1, n):
            b = boxes[j]
            if j == i or b.moving:
                continue
            ra, rb = a.reach(), b.reach()
            d = [a.c[k] - b.c[k] for k in range(3)]
            if dot(d, d) > (ra + rb + TOL) ** 2:
                continue
            for aa in range(3):
                for sa in (1, -1):
                    na, da, ca = a.face(aa, sa)
                    for ab in range(3):
                        x = dot(a.ax[aa], b.ax[ab])
                        if abs(x) < 0.9999:
                            continue
                        sb = sa if x > 0 else -sa
                        db = dot(na, b.c) + b.h[ab]
                        if abs(da - db) > TOL:
                            continue
                        if on_ground and na[1] < -0.9999 and -da <= TOL:
                            continue
                        cb = b.face(ab, sb)[2]
                        area, centre, poly = _clip(na, ca, cb)
                        if area < MIN_AREA:
                            continue
                        if _same_look_hidden(a, b, ca, cb, shade) or _covered(boxes, i, j, na, centre, poly):
                            continue
                        found.append({"a": (i, aa, sa), "b": (j, ab, sb), "area": area})
    return found


def _touching(boxes, i, a, s):
    """Is this face pressed against a neighbour's opposite face? (An inset would open a gap there.)"""
    box = boxes[i]
    n, d, ca = box.face(a, s)
    for j, b in enumerate(boxes):
        if j == i or b.moving:
            continue
        for ab in range(3):
            x = dot(box.ax[a], b.ax[ab])
            if abs(x) < 0.9999:
                continue
            sb = -s if x > 0 else s
            db = dot(n, b.c) - b.h[ab]
            if abs(d - db) > TOL:
                continue
            if _clip(n, ca, b.face(ab, sb)[2])[0] >= MIN_AREA:
                return True
    return False


def _options(boxes, i, a, s, gap, contained, free=False):
    """What may be done to this face, most wanted first: (how, distance outward)."""
    box = boxes[i]
    if (a, s) in box.fixed and not free:
        return []
    thick = box.h[a] * 2
    both_free = box.whole and (a, -s) not in box.fixed
    out_first = contained or _touching(boxes, i, a, s)
    res = []
    for mult in (1, 2, 3):
        d = gap * mult
        for out in ((True, False) if out_first else (False, True)):
            if out and (a, s) in box.no_out:
                continue
            if not out and not both_free and thick - d < 0.002:
                continue
            thin = thick < THIN * d
            if thin and both_free:
                res.append(("whole", d if out else -d))
            elif out or thick - d >= 0.002:
                res.append(("face", d if out else -d))
    return res


def _apply(box, a, s, how, d):
    if how == "whole":
        for k in range(3):
            box.c[k] += s * box.ax[a][k] * d
    else:
        box.h[a] += d / 2
        for k in range(3):
            box.c[k] += s * box.ax[a][k] * d / 2


def _rank(boxes, f):
    """Which of two faces stays: bigger rank stays (a fixed face, then a solid over a detail, then the bigger face)."""
    i, a, s = f
    b = boxes[i]
    return ((a, s) in b.fixed, not b.detail, b.area(a), -i)


def solve(boxes, on_ground=False, shade=False):
    """Moves faces until `find` is empty. Returns the number of hits it couldn't clear."""
    for _ in range(40 + 4 * len(boxes)):
        hits = find(boxes, on_ground, shade)
        if not hits:
            return 0
        progress = False
        hits.sort(key=lambda h: (-h["area"], h["a"], h["b"]))
        for h in hits:
            order = sorted((h["a"], h["b"]), key=lambda f: _rank(boxes, f))  # the less seen face first
            gap = gap_for(math.sqrt(h["area"]))
            both_fixed = all((f[1], f[2]) in boxes[f[0]].fixed for f in order)  # one face holding the socket's plane is enough
            best = None
            for f in order:
                i, a, s = f
                box = boxes[i]
                before = len(find(boxes, on_ground, shade, only=i))
                contained = h["area"] >= box.area(a) * 0.98
                snap = box.snapshot()
                for how, d in _options(boxes, i, a, s, gap, contained, both_fixed and f == order[0]):
                    _apply(box, a, s, how, d)
                    left = len(find(boxes, on_ground, shade, only=i))
                    box.restore(snap)
                    if left < before and (best is None or left < best[0]):
                        best = (left, f, how, d)
                    if left == 0:
                        break
                if best and best[0] == 0:
                    break
            if best:
                i, a, s = best[1]
                _apply(boxes[i], a, s, best[2], best[3])
                progress = True
                break  # the hit list is stale now
        if not progress:
            return len(hits)
    return len(find(boxes, on_ground, shade))


# ---- construct parts: the generators' {"min", "max"} dicts ----


def _drawn(b):
    return not b.get("hidden") and not b.get("shape")


def _locked(boxes, sockets):
    """Faces (box, axis, sign) a socket sits on: they must not move."""
    locked = set()
    for sk in sockets or []:
        d = sk["dir"]
        a = "xyz".index(d[1])
        s = 1 if d[0] == "+" else -1
        for i, b in enumerate(boxes):
            if b.get("detail") or not _drawn(b):
                continue
            plane = b["max"][a] if s > 0 else b["min"][a]
            if abs(plane - sk["at"][a]) < 1e-3 and all(b["min"][k] - 1e-3 <= sk["at"][k] <= b["max"][k] + 1e-3 for k in range(3) if k != a):
                locked.add((i, a, s))
    return locked


def _construct_boxes(boxes, sockets):
    locked = _locked(boxes, sockets)
    solids = [b for b in boxes if not b.get("detail") and _drawn(b)]
    out = []
    index = {}
    for i, b in enumerate(boxes):
        if not _drawn(b):
            continue
        c = [(b["min"][k] + b["max"][k]) / 2 for k in range(3)]
        h = [(b["max"][k] - b["min"][k]) / 2 for k in range(3)]
        z = Box(c, h, detail=bool(b.get("detail")), whole=bool(b.get("detail")), ref=b)
        for a in range(3):
            for s in (1, -1):
                if (i, a, s) in locked:
                    z.fixed.add((a, s))
                if not z.detail and solids:
                    plane = b["max"][a] if s > 0 else b["min"][a]
                    limit = max(q["max"][a] for q in solids) if s > 0 else min(q["min"][a] for q in solids)
                    if abs(plane - limit) < 1e-4:
                        z.no_out.add((a, s))  # a solid never grows past the part's bounds (sockets, the collider)
        index[len(out)] = i
        out.append(z)
    return out


def hits(boxes):
    """Coplanar pairs of a construct part's boxes: (i, j, axis, sign, area), `boxes` indexes; empty when clean."""
    zs = _construct_boxes(boxes, None)
    return [(boxes.index(zs[h["a"][0]].ref), boxes.index(zs[h["b"][0]].ref), h["a"][1], h["a"][2], h["area"]) for h in find(zs)]


def resolve(boxes, sockets=None):
    """Corrects `boxes` in place until no pair is coplanar. Returns the number of boxes changed."""
    zs = _construct_boxes(boxes, sockets)
    if not find(zs):
        return 0
    start = [z.snapshot() for z in zs]
    solve(zs)
    changed = 0
    for z, snap in zip(zs, start):
        if (z.c, z.h) == (snap[0], snap[1]):
            continue
        changed += 1
        z.ref["min"] = [round(z.c[k] - z.h[k], 4) for k in range(3)]
        z.ref["max"] = [round(z.c[k] + z.h[k], 4) for k in range(3)]
    return changed


# ---- props: part dicts, any rotation ----


MOVING = ("follow", "spin", "swing", "bob", "shake")


def _prop_look(q):
    """The key of a plain look: parts with one draw the same pixels (PropMeshes), None for anything else."""
    if q.get("material") or q.get("glass") or q.get("anim"):
        return None
    c = q.get("color")
    return (c.lower() if isinstance(c, str) else tuple(c) if c else None, q.get("glow", 0))


def prop_boxes(parts):
    zs = []
    for q in parts:
        if "size" not in q or q.get("corners"):
            continue
        anim = q.get("anim")
        moving = bool(anim) and str(anim.get("type", "")).lower() in MOVING
        z = Box(q.get("offset", [0, 0, 0]), [v / 2 for v in q["size"]], axes_of(q.get("rotation", [0, 0, 0])), _prop_look(q), moving, ref=q)
        if abs(z.c[1] - sum(abs(z.ax[k][1]) * z.h[k] for k in range(3))) < 1e-4:  # stands on the ground: stays down
            for a in range(3):
                if abs(z.ax[a][1]) > 0.9999:
                    z.fixed.add((a, -1 if z.ax[a][1] > 0 else 1))
        zs.append(z)
    return zs


def settle_props(parts):
    """Corrects prop parts in place until no pair is coplanar. Returns (parts changed, hits left)."""
    zs = prop_boxes(parts)
    if not find(zs, True, True):
        return 0, 0
    start = [z.snapshot() for z in zs]
    left = solve(zs, True, True)
    changed = 0
    for z, snap in zip(zs, start):
        if (z.c, z.h) == (snap[0], snap[1]):
            continue
        changed += 1
        z.ref["size"] = [round(v * 2, 5) for v in z.h]
        z.ref["offset"] = [round(v, 5) for v in z.c]
    return changed, left
