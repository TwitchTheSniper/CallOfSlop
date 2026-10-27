"""The weapon-builder parts' contoured model shapes, mirrored from Source/Zombies/PartShapes.cs (keep the two in step).

Each shape fits inside its box (the box's "size", centred on its "pos"): cylinder, tube, wedge, bevel, rounded,
capsule, sphere and arc (Parts/README.md, "Shapes"). Two things are made from a box entry's settings:

    mesh(b)     triangles in the box's own space (metres, before its turn): [(a, b, c, na, nb, nc)], with a normal per corner
    proxies(b)  boxes inside the shape that stand for its volume when checking that pieces touch: [(centre, size, rot_x)]

`b` is the box's dict as read from the part file (lower-case keys) plus its final "size". Used by viz.py (the contact
sheet) and floaters.py (the connectivity checker).
"""
import math

NAMES = ("cylinder", "tube", "wedge", "bevel", "rounded", "capsule", "sphere", "arc")


def _v(x, y, z):
    return [float(x), float(y), float(z)]


def add(a, b):
    return [a[0] + b[0], a[1] + b[1], a[2] + b[2]]


def sub(a, b):
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]


def mul(a, k):
    return [a[0] * k, a[1] * k, a[2] * k]


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def norm(a):
    n = math.sqrt(dot(a, a)) or 1e-12
    return [a[0] / n, a[1] / n, a[2] / n]


def _pair(v, default):
    if v is None:
        return default
    if isinstance(v, (int, float)):
        return (float(v), float(v))
    return (float(v[0]), float(v[1]))


# ---- Settings with their defaults (PartShapes.SegmentsOf ...) ------------------------------------------------------

def angle_of(b):
    return float(b.get("angle") or 90)


def segments_of(b):
    s = b.get("segments")
    if s:
        return int(s)
    shape = b["shape"]
    if shape == "arc":
        return max(2, min(24, int(math.ceil(angle_of(b) / 15))))
    if shape == "rounded":
        return 4
    if shape == "bevel":
        return 1
    return 12


def inner_of(b):
    if "inner" in b:
        return float(b["inner"])
    return {"tube": 0.7, "arc": 0.6}.get(b["shape"], 0.0)


def ends_of(b):
    return b.get("ends") or ("open" if b["shape"] == "tube" else "closed")


def taper_of(b):
    return _pair(b.get("taper"), (1.0, 0.3) if b["shape"] == "wedge" else (1.0, 1.0))


def shift_of(b):
    return _pair(b.get("shift"), (0.0, 0.0))


class _Builder:
    def __init__(self):
        self.tris = []
        self.proxies = []

    def tri(self, a, b, c, na, nb, nc):
        if dot(cross(sub(b, a), sub(c, a)), cross(sub(b, a), sub(c, a))) < 1e-14:
            return
        self.tris.append((a, b, c, norm(na), norm(nb), norm(nc)))

    def flat(self, a, b, c, inside):
        n = cross(sub(b, a), sub(c, a))
        if dot(n, n) < 1e-14:
            return
        cen = mul(add(add(a, b), c), 1 / 3)
        if dot(n, sub(cen, inside)) < 0:
            n = mul(n, -1)
        self.tri(a, b, c, n, n, n)

    def flat_quad(self, a, b, c, d, inside):
        self.flat(a, b, c, inside)
        self.flat(a, c, d, inside)

    def quad(self, a, b, c, d, na, nb, nc, nd):
        self.tri(a, b, c, na, nb, nc)
        self.tri(a, c, d, na, nc, nd)

    def proxy(self, centre, size, rot_x=0.0):
        self.proxies.append((list(centre), [max(s, 1e-4) for s in size], rot_x))


def _lerp2(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def _lathe(bl, h, n, inner, ends, taper, shift):
    front_c = (shift[0] * h[0] * 2, shift[1] * h[1] * 2)
    smooth = n >= 8

    def A(i):
        return math.pi / 2 - math.pi / n + 2 * math.pi * i / n

    def outer(i, front, k):
        a = A(i)
        if front:
            return _v(front_c[0] + math.cos(a) * h[0] * taper[0] * k, front_c[1] + math.sin(a) * h[1] * taper[1] * k, -h[2])
        return _v(math.cos(a) * h[0] * k, math.sin(a) * h[1] * k, h[2])

    for k in ([1.0, inner] if inner > 0 else [1.0]):
        sign = -1.0 if k < 1 else 1.0
        for i in range(n):
            p0, p1, q0, q1 = outer(i, False, k), outer(i + 1, False, k), outer(i, True, k), outer(i + 1, True, k)
            if not smooth:
                c = mul(add(add(p0, p1), add(q0, q1)), 0.25)
                t = 0.5 - c[2] / (2 * h[2])
                ax = _lerp2((0, 0), front_c, t)
                axis = _v(ax[0], ax[1], c[2])
                bl.flat_quad(p0, p1, q1, q0, axis if sign > 0 else add(c, sub(c, axis)))
                continue

            def N(j, front):
                a = A(j)
                tng = _v(-math.sin(a) * h[0], math.cos(a) * h[1], 0)
                line = sub(outer(j, True, k), outer(j, False, k))
                nn = cross(tng, line)
                radial = _v(math.cos(a) / h[0], math.sin(a) / h[1], 0)
                if dot(nn, radial) < 0:
                    nn = mul(nn, -1)
                return mul(nn, sign)
            bl.quad(p0, p1, q1, q0, N(i, False), N(i + 1, False), N(i + 1, True), N(i, True))

    for front in (False, True):
        z = -h[2] if front else h[2]
        nrm = _v(0, 0, -1 if front else 1)
        centre = _v(front_c[0], front_c[1], z) if front else _v(0, 0, z)
        closed = ends == "closed" or ends == ("front" if front else "back")
        if inner > 0:
            for i in range(n):
                bl.quad(outer(i, front, 1), outer(i + 1, front, 1), outer(i + 1, front, inner), outer(i, front, inner), nrm, nrm, nrm, nrm)
        if closed:
            kk = inner if inner > 0 else 1.0
            for i in range(n):
                bl.tri(centre, outer(i, front, kk), outer(i + 1, front, kk), nrm, nrm, nrm)

    slabs = 1 if taper == (1.0, 1.0) and shift == (0.0, 0.0) else 4
    ap = math.cos(math.pi / n)
    for s in range(slabs):
        z0, z1 = h[2] - 2 * h[2] * s / slabs, h[2] - 2 * h[2] * (s + 1) / slabs
        t0, t1 = 0.5 - z0 / (2 * h[2]), 0.5 - z1 / (2 * h[2])
        e0, e1 = _lerp2((1, 1), taper, t0), _lerp2((1, 1), taper, t1)
        sc = (min(e0[0], e1[0]) * ap, min(e0[1], e1[1]) * ap)
        c = _lerp2((0, 0), front_c, (t0 + t1) * 0.5)
        _cross_section(bl, _v(c[0], c[1], (z0 + z1) * 0.5), _v(h[0] * sc[0], h[1] * sc[1], (z0 - z1) * 0.5), inner)
    if inner > 0:
        for front in (False, True):
            if ends == "closed" or ends == ("front" if front else "back"):
                sc = ((taper if front else (1, 1))[0] * ap, (taper if front else (1, 1))[1] * ap)
                c = front_c if front else (0, 0)
                zc = -h[2] * 0.97 if front else h[2] * 0.97
                _cross_section(bl, _v(c[0], c[1], zc), _v(h[0] * sc[0], h[1] * sc[1], h[2] * 0.03), 0)


def _cross_section(bl, c, hx, inner):
    COS, SIN = 0.924, 0.383
    if inner <= 0:
        bl.proxy(c, (2 * hx[0] * COS, 2 * hx[1] * SIN, 2 * hx[2]))
        bl.proxy(c, (2 * hx[0] * SIN, 2 * hx[1] * COS, 2 * hx[2]))
        bl.proxy(c, (2 * hx[0] * 0.707, 2 * hx[1] * 0.707, 2 * hx[2]))
        return
    ri = inner
    y1 = ri + (1 - ri) * 0.6
    half = math.sqrt(max(0, 1 - y1 * y1))
    for s in (1, -1):
        bl.proxy(add(c, _v(0, s * (ri + y1) * 0.5 * hx[1], 0)), (2 * half * hx[0], (y1 - ri) * hx[1], 2 * hx[2]))
        bl.proxy(add(c, _v(s * (ri + y1) * 0.5 * hx[0], 0, 0)), ((y1 - ri) * hx[0], 2 * half * hx[1], 2 * hx[2]))
    rm = (ri + 1) * 0.5
    cc = rm / math.sqrt(2)
    hh = max(0.01, min(1 / math.sqrt(2) - cc, cc - ri / math.sqrt(2)))
    for sx in (1, -1):
        for sy in (1, -1):
            bl.proxy(add(c, _v(sx * cc * hx[0], sy * cc * hx[1], 0)), (2 * hh * hx[0], 2 * hh * hx[1], 2 * hx[2]))


def _wedge(bl, h, taper, shift):
    fc = (shift[0] * h[0] * 2, shift[1] * h[1] * 2)

    def P(x, y, front):
        if front:
            return _v(fc[0] + x * h[0] * taper[0], fc[1] + y * h[1] * taper[1], -h[2])
        return _v(x * h[0], y * h[1], h[2])
    inside = _v(fc[0] * 0.5, fc[1] * 0.5, 0)
    bl.flat_quad(P(-1, -1, False), P(1, -1, False), P(1, 1, False), P(-1, 1, False), inside)
    bl.flat_quad(P(-1, -1, True), P(1, -1, True), P(1, 1, True), P(-1, 1, True), inside)
    bl.flat_quad(P(1, -1, False), P(1, 1, False), P(1, 1, True), P(1, -1, True), inside)
    bl.flat_quad(P(-1, -1, False), P(-1, 1, False), P(-1, 1, True), P(-1, -1, True), inside)
    bl.flat_quad(P(-1, 1, False), P(1, 1, False), P(1, 1, True), P(-1, 1, True), inside)
    bl.flat_quad(P(-1, -1, False), P(1, -1, False), P(1, -1, True), P(-1, -1, True), inside)
    for s in range(4):
        t0, t1 = s / 4, (s + 1) / 4
        lo, hi = [], []
        for t in (t0, t1):
            c = _lerp2((0, 0), fc, t)
            e = _lerp2((1, 1), taper, t)
            lo.append((c[0] - h[0] * e[0], c[1] - h[1] * e[1]))
            hi.append((c[0] + h[0] * e[0], c[1] + h[1] * e[1]))
        mn = (max(lo[0][0], lo[1][0]), max(lo[0][1], lo[1][1]))
        mx = (min(hi[0][0], hi[1][0]), min(hi[0][1], hi[1][1]))
        zc = h[2] - 2 * h[2] * (t0 + t1) * 0.5
        bl.proxy(_v((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, zc), (max(mx[0] - mn[0], 1e-4), max(mx[1] - mn[1], 1e-4), 2 * h[2] / 4))


def _rounded(bl, h, radius, long_only, segments, flat):
    min_side = min(h[0], h[1]) if long_only else min(h)
    r = min(radius * 2 * min_side, min_side * 0.999)
    rv = [r, r, 0.0 if long_only else r]
    e = [h[i] - rv[i] for i in range(3)]
    bands = 1 if flat else max(1, (segments + 1) // 2)

    def samples(half, ri):
        if ri <= 0:
            return [-half, half]
        inner = half - ri
        out = [-(inner + ri * (1 if flat else math.tan(math.pi / 4 * k / bands))) for k in range(bands, 0, -1)]
        out += [-inner, inner]
        out += [inner + ri * (1 if flat else math.tan(math.pi / 4 * k / bands)) for k in range(1, bands + 1)]
        return out
    s = [samples(h[i], rv[i]) for i in range(3)]

    def mp(p, face):
        q = [max(-e[i], min(e[i], p[i])) for i in range(3)]
        d = sub(p, q)
        if dot(d, d) < 1e-14:
            return p, face
        if flat:
            l1 = abs(d[0]) + abs(d[1]) + abs(d[2])
            return add(q, mul(d, r / l1)), face
        n = norm(d)
        return add(q, mul(n, r)), n

    if flat:
        _chamfer(bl, h, e)
    for axis in range(3 if not flat else 0):
        u, v = (axis + 1) % 3, (axis + 2) % 3
        for sign in (-1, 1):
            face = [0.0, 0.0, 0.0]
            face[axis] = float(sign)
            for i in range(len(s[u]) - 1):
                for j in range(len(s[v]) - 1):
                    def corner(di, dj):
                        p = [0.0, 0.0, 0.0]
                        p[axis] = sign * h[axis]
                        p[u] = s[u][i + di]
                        p[v] = s[v][j + dj]
                        return p
                    (a, na), (b, nb), (c, nc), (d, nd) = (mp(corner(0, 0), face), mp(corner(1, 0), face), mp(corner(1, 1), face),
                                                         mp(corner(0, 1), face))
                    if flat:
                        bl.flat_quad(a, b, c, d, _v(0, 0, 0))
                    else:
                        bl.quad(a, b, c, d, na, nb, nc, nd)
    bl.proxy(_v(0, 0, 0), (2 * h[0], 2 * e[1], 2 * e[2]))
    bl.proxy(_v(0, 0, 0), (2 * e[0], 2 * h[1], 2 * e[2]))
    if not long_only:
        bl.proxy(_v(0, 0, 0), (2 * e[0], 2 * e[1], 2 * h[2]))
    q = r * 0.5 if flat else r / math.sqrt(2)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bl.proxy(_v(sx * (e[0] + q * 0.5), sy * (e[1] + q * 0.5), 0), (q, q, 2 * e[2]))


def _chamfer(bl, h, e):
    """A bevelled box face by face (PartShapes.Chamfer): faces cut back to the inner box, edge strips, corner triangles."""
    o = _v(0, 0, 0)
    for a in range(3):
        b, c = (a + 1) % 3, (a + 2) % 3
        eb, ec = e[b], e[c]
        for sa in (-1, 1):
            def P(sb, sc):
                p = [0.0, 0.0, 0.0]
                p[a], p[b], p[c] = sa * h[a], sb * eb, sc * ec
                return p
            bl.flat_quad(P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), o)
            for sb in (-1, 1):
                def Q(tb, tc):
                    p = [0.0, 0.0, 0.0]
                    p[a] = sa * (h[a] if tb > 0 else e[a])
                    p[b] = sb * (eb if tb > 0 else h[b])
                    p[c] = tc
                    return p
                bl.flat_quad(Q(1, -ec), Q(1, ec), Q(-1, ec), Q(-1, -ec), o)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                bl.flat(_v(sx * h[0], sy * e[1], sz * e[2]), _v(sx * e[0], sy * h[1], sz * e[2]), _v(sx * e[0], sy * e[1], sz * h[2]), o)


def _capsule(bl, h, n, sphere):
    cap = h[2] if sphere else min(max(h[0], h[1]), h[2])
    zc = h[2] - cap
    rings = max(2, n // 4)
    prof = []
    for k in range(rings + 1):
        t = math.pi / 2 * k / rings
        prof.append((zc + cap * math.cos(t), math.sin(t), zc))
    for k in range(1 if sphere else 0, rings + 1):
        t = math.pi / 2 * (rings - k) / rings
        prof.append((-zc - cap * math.cos(t), math.sin(t), -zc))

    def A(i):
        return math.pi / 2 - math.pi / n + 2 * math.pi * i / n

    def P(ri, i):
        return _v(math.cos(A(i)) * h[0] * prof[ri][1], math.sin(A(i)) * h[1] * prof[ri][1], prof[ri][0])

    def N(ri, i):
        p = P(ri, i)
        z, s, c = prof[ri]
        if s < 1e-4:
            return _v(0, 0, 1 if z > 0 else -1)
        dz = (z - c) / (cap * cap) if (abs(z) > zc + 1e-6 or sphere) else 0.0
        return _v(p[0] / (h[0] * h[0]), p[1] / (h[1] * h[1]), dz)
    for ri in range(len(prof) - 1):
        for i in range(n):
            bl.quad(P(ri, i), P(ri, i + 1), P(ri + 1, i + 1), P(ri + 1, i), N(ri, i), N(ri, i + 1), N(ri + 1, i + 1), N(ri + 1, i))
    ap = math.cos(math.pi / n)
    if zc > 1e-4:
        _cross_section(bl, _v(0, 0, 0), _v(h[0] * ap, h[1] * ap, zc), 0)
    for sign in (1, -1):
        for k in range(2):
            z0, z1 = cap * k / 2, cap * (k + 1) / 2
            sc = max(math.sqrt(max(0, 1 - (z1 / cap) ** 2)) * ap, 0.25 * ap)
            _cross_section(bl, _v(0, 0, sign * (zc + (z0 + z1) * 0.5)), _v(h[0] * sc, h[1] * sc, (z1 - z0) * 0.5), 0)


def _arc(bl, h, n, inner, angle, direction):
    a0 = math.radians(direction - angle / 2)
    a1 = math.radians(direction + angle / 2)

    def D(a):
        return (-math.cos(a), -math.sin(a))
    lo, hi = [1e9, 1e9], [-1e9, -1e9]
    for i in range(721):
        d = D(a0 + (a1 - a0) * i / 720)
        for p in (d, (d[0] * inner, d[1] * inner)):
            lo = [min(lo[0], p[0]), min(lo[1], p[1])]
            hi = [max(hi[0], p[0]), max(hi[1], p[1])]
    span = (max(hi[0] - lo[0], 1e-4), max(hi[1] - lo[1], 1e-4))
    mid = ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2)

    def fit(p):
        return ((p[0] - mid[0]) / span[0] * 2 * h[1], (p[1] - mid[1]) / span[1] * 2 * h[2])

    def at(i, k, x):
        d = D(a0 + (a1 - a0) * i / n)
        q = fit((d[0] * k, d[1] * k))
        return _v(x, q[0], q[1])
    centre = fit((0, 0))
    full = angle >= 359.9

    def normal(i, k):
        prev = at(n - 1 if (i == 0 and full) else max(i - 1, 0), k, 0)
        nxt = at(1 if (i == n and full) else min(i + 1, n), k, 0)
        t = sub(nxt, prev)
        nn = _v(0, -t[2], t[1])
        p = at(i, k, 0)
        if dot(nn, _v(0, p[1] - centre[0], p[2] - centre[1])) < 0:
            nn = mul(nn, -1)
        return mul(nn, -1) if k < 1 else nn
    for i in range(n):
        bl.quad(at(i, 1, -h[0]), at(i + 1, 1, -h[0]), at(i + 1, 1, h[0]), at(i, 1, h[0]), normal(i, 1), normal(i + 1, 1), normal(i + 1, 1), normal(i, 1))
        if inner > 0:
            bl.quad(at(i, inner, -h[0]), at(i + 1, inner, -h[0]), at(i + 1, inner, h[0]), at(i, inner, h[0]),
                    normal(i, inner), normal(i + 1, inner), normal(i + 1, inner), normal(i, inner))
        for x in (-h[0], h[0]):
            nx = _v(1 if x > 0 else -1, 0, 0)
            bl.quad(at(i, 1, x), at(i + 1, 1, x), at(i + 1, inner, x), at(i, inner, x), nx, nx, nx, nx)
        o0, o1, i0, i1 = at(i, 1, 0), at(i + 1, 1, 0), at(i, inner, 0), at(i + 1, inner, 0)
        om, im = mul(add(o0, o1), 0.5), mul(add(i0, i1), 0.5)
        oc = math.sqrt(dot(sub(o1, o0), sub(o1, o0)))
        ic = math.sqrt(dot(sub(i1, i0), sub(i1, i0)))
        along = ic if ic > oc * 0.4 else oc * 0.4
        thick = math.sqrt(dot(sub(om, im), sub(om, im)))
        tz = sub(o1, o0)
        rot = math.atan2(tz[1], -tz[2])
        bl.proxy(mul(add(om, im), 0.5), (2 * h[0], thick * 0.85, along * 0.9), rot)
    if not full:
        for i in (0, n):
            ref = at(1, (1 + inner) / 2, 0) if i == 0 else at(n - 1, (1 + inner) / 2, 0)
            bl.flat_quad(at(i, 1, -h[0]), at(i, 1, h[0]), at(i, inner, h[0]), at(i, inner, -h[0]), ref)


def _build(b):
    shape = b["shape"]
    h = [abs(float(x)) * 0.5 for x in b["size"]]
    bl = _Builder()
    if shape in ("cylinder", "tube"):
        _lathe(bl, h, segments_of(b), inner_of(b), ends_of(b), taper_of(b), shift_of(b))
    elif shape == "wedge":
        _wedge(bl, h, taper_of(b), shift_of(b))
    elif shape in ("bevel", "rounded"):
        _rounded(bl, h, float(b.get("radius") or 0.25), (b.get("edges") or "all") == "long",
                 1 if shape == "bevel" else segments_of(b), shape == "bevel")
    elif shape in ("capsule", "sphere"):
        _capsule(bl, h, segments_of(b), shape == "sphere")
    elif shape == "arc":
        _arc(bl, h, segments_of(b), inner_of(b), angle_of(b), float(b.get("dir") or 0))
    return bl


_CACHE = {}


def _cached(b):
    key = (b["shape"], tuple(round(float(x), 6) for x in b["size"]),
           tuple((k, str(b.get(k))) for k in ("segments", "inner", "ends", "taper", "shift", "radius", "edges", "angle", "dir")))
    if key not in _CACHE:
        _CACHE[key] = _build(b)
    return _CACHE[key]


def is_shaped(b):
    return b.get("shape", "box") in NAMES


def mesh(b):
    """Triangles (a, b, c, na, nb, nc) in the box's own space (metres, centred on the box, before its turn)."""
    return _cached(b).tris


def proxies(b):
    """Proxy boxes (centre, size, turn about X in radians) in the box's own space."""
    return _cached(b).proxies


def triangles(b):
    shape = b.get("shape", "box")
    if shape == "rod":
        return 24
    if shape in NAMES:
        return len(mesh(b))
    return 12
