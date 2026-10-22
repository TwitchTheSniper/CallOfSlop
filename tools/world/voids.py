"""Python port of the game's terrain holes and caves (Source/Maps/TerrainVoids.cs, TerrainVoidGen), for map tools.

    v = build(features, terrain)      # terrain from tools/cluttered/terrain.py (heights before voids)
    v.kind(x, z)                      # 0 solid, 1 open (hole), 2 roofed cave
    v.floor(x, z), v.ceiling(x, z)    # bilinear like the game (NaN where no void)

Same formulas and order as the C#: caves and holes in file order, merges, cell kinds, edge-floor borrowing, ceilings
kept 1 m under the surface. Not bit-exact (no float32 quantisation of intermediate steps), good to a millimetre.
"""
import math
import numpy as np

SOLID, OPEN, CAVE = 0, 1, 2
ROOF_MIN = 1.0
QUANTUM = 1.0 / 1024.0


def _ss(e):
    t = np.clip(e, 0.0, 1.0)
    return t * t * (3 - 2 * t)


class Voids:
    def __init__(self, t, kinds, floor, ceil):
        self.t = t
        self.kinds = kinds          # (nz-1, nx-1)
        self.F = floor              # (nz, nx)
        self.C = ceil

    def _loc(self, x, z):
        t = self.t
        fx = min(max((x - t.ox) / t.cell, 0), t.nx - 1)
        fz = min(max((z - t.oz) / t.cell, 0), t.nz - 1)
        i = min(int(fx), t.nx - 2)
        j = min(int(fz), t.nz - 2)
        return i, j, fx - i, fz - j

    def kind(self, x, z):
        i, j, _, _ = self._loc(x, z)
        return int(self.kinds[j, i])

    def _bil(self, A, x, z):
        i, j, tx, tz = self._loc(x, z)
        return (A[j, i] * (1 - tx) + A[j, i + 1] * tx) * (1 - tz) + (A[j + 1, i] * (1 - tx) + A[j + 1, i + 1] * tx) * tz

    def floor(self, x, z):
        return float(self._bil(self.F, x, z))

    def ceiling(self, x, z):
        if self.kind(x, z) == OPEN:
            return math.inf
        return float(self._bil(self.C, x, z))

    def surface(self, x, z):
        return float(self._bil(self.t.h, x, z))


def _closest(pts, X, Z):
    """Closest point on a polyline (ends clamped): segment index, t, distance squared (arrays)."""
    if len(pts) == 1:
        return np.zeros(X.shape, int), np.zeros(X.shape), (X - pts[0][0]) ** 2 + (Z - pts[0][1]) ** 2
    best_d = np.full(X.shape, np.inf)
    best_s = np.zeros(X.shape, int)
    best_t = np.zeros(X.shape)
    for k in range(len(pts) - 1):
        ax, az = pts[k][0], pts[k][1]
        abx, abz = pts[k + 1][0] - ax, pts[k + 1][1] - az
        t = np.clip(((X - ax) * abx + (Z - az) * abz) / (abx * abx + abz * abz), 0, 1)
        d2 = (ax + abx * t - X) ** 2 + (az + abz * t - Z) ** 2
        m = d2 < best_d
        best_d = np.where(m, d2, best_d)
        best_s = np.where(m, k, best_s)
        best_t = np.where(m, t, best_t)
    return best_s, best_t, best_d


def build(features, t):
    H = t.h
    nz, nx = H.shape
    cell = float(t.cell)
    ox, oz = float(t.ox), float(t.oz)
    xs = ox + np.arange(nx) * cell
    zs = oz + np.arange(nz) * cell
    F = np.full((nz, nx), np.nan)
    C = np.full((nz, nx), np.nan)
    forced = np.zeros((nz - 1, nx - 1), bool)

    def sample(x, z):
        return float(t.sample(x, z))

    def rng(minx, maxx, minz, maxz):
        i0 = max(0, int(math.ceil((minx - ox) / cell))); i1 = min(nx - 1, int(math.floor((maxx - ox) / cell)))
        j0 = max(0, int(math.ceil((minz - oz) / cell))); j1 = min(nz - 1, int(math.floor((maxz - oz) / cell)))
        return i0, i1, j0, j1

    for f in features:
        typ = f["type"]
        if typ == "cave":
            src = f["points"]
            w = f.get("width", 5.0); height = f.get("height", 3.5); depth = f.get("depth", 6.0)
            op = "none" if len(src) < 2 else f.get("open", "both")
            pts = []
            for p, q in enumerate(src):
                end = (p == 0 and op in ("both", "start")) or (p == len(src) - 1 and op in ("both", "end"))
                y = q[2] if len(q) > 2 else (sample(q[0], q[1]) if end else sample(q[0], q[1]) - depth)
                pts.append((q[0], q[1], y))
            half = w / 2; reach = half * 1.5; pad = reach + 2 * cell
            i0, i1, j0, j1 = rng(min(p[0] for p in pts) - pad, max(p[0] for p in pts) + pad, min(p[1] for p in pts) - pad, max(p[1] for p in pts) + pad)
            if i1 < i0 or j1 < j0:
                continue
            X, Z = np.meshgrid(xs[i0:i1 + 1], zs[j0:j1 + 1])
            seg, tt, d2 = _closest(pts, X, Z)
            u = np.sqrt(d2) / half
            ay = np.array([p[2] for p in pts])
            by = np.array([pts[min(k + 1, len(pts) - 1)][2] for k in range(len(pts))])
            f0 = ay[seg] + (by[seg] - ay[seg]) * tt
            u2 = u * u
            c = f0 + height * (1 - u2 * u2)
            fl = f0 + height * 0.4 * _ss((u - 0.6) / 0.5)
            sub_f = F[j0:j1 + 1, i0:i1 + 1]; sub_c = C[j0:j1 + 1, i0:i1 + 1]
            use = u <= 1.5
            oldF, oldC = sub_f.copy(), sub_c.copy()
            empty = np.isnan(oldF) | np.isnan(oldC)
            has_new = c > fl
            has_old = ~empty & (oldC > oldF)
            both = use & ~empty & has_new & has_old
            repl = use & (empty | (~both & (has_new | (~has_old & ((c - fl) > (oldC - oldF))))))
            nf = np.where(both, np.minimum(oldF, fl), np.where(repl, fl, oldF))
            nc = np.where(both, np.maximum(oldC, c), np.where(repl, c, oldC))
            F[j0:j1 + 1, i0:i1 + 1] = nf
            C[j0:j1 + 1, i0:i1 + 1] = nc
        elif typ == "hole":
            shape = f.get("shape", "circle")
            x = f.get("x", 0.0); z = f.get("z", 0.0); r = f.get("radius", 1.0); rz = f.get("radiusZ", r)
            ang = math.radians(f.get("angle", 0.0)); ca, sa = math.cos(ang), math.sin(ang)
            poly = f.get("points", [])
            depth = f.get("depth", 4.0)
            floor_at = f.get("floor")

            def outside(PX, PZ):
                if shape == "polygon":
                    inside = np.zeros(PX.shape, bool); best = np.full(PX.shape, np.inf)
                    n = len(poly)
                    for a in range(n):
                        b = (a - 1) % n
                        ax, az = poly[a]; bx, bz = poly[b]
                        cond = ((az > PZ) != (bz > PZ)) & (PX < (bx - ax) * (PZ - az) / ((bz - az) if bz != az else 1e-12) + ax)
                        inside ^= cond
                        ex, ez = bx - ax, bz - az
                        tq = np.clip(((PX - ax) * ex + (PZ - az) * ez) / (ex * ex + ez * ez), 0, 1)
                        best = np.minimum(best, (ax + ex * tq - PX) ** 2 + (az + ez * tq - PZ) ** 2)
                    return np.where(inside, -np.sqrt(best), np.sqrt(best))
                if shape == "ellipse":
                    dx, dz = PX - x, PZ - z
                    lx, lz = dx * ca + dz * sa, -dx * sa + dz * ca
                    return (np.sqrt((lx / r) ** 2 + (lz / rz) ** 2) - 1) * min(r, rz)
                return np.sqrt((PX - x) ** 2 + (PZ - z) ** 2) - r

            if shape == "polygon":
                minx, maxx = min(p[0] for p in poly), max(p[0] for p in poly); minz, maxz = min(p[1] for p in poly), max(p[1] for p in poly)
            else:
                big = max(r, rz); minx, maxx, minz, maxz = x - big, x + big, z - big, z + big
            margin = 1.5 * cell
            i0, i1, j0, j1 = rng(minx - margin - cell, maxx + margin + cell, minz - margin - cell, maxz + margin + cell)
            X, Z = np.meshgrid(xs[i0:i1 + 1], zs[j0:j1 + 1])
            o = outside(X, Z)
            sf = F[j0:j1 + 1, i0:i1 + 1]; sc = C[j0:j1 + 1, i0:i1 + 1]; sh = H[j0:j1 + 1, i0:i1 + 1]
            near = o <= margin
            hollow = ~np.isnan(sf) & ~np.isnan(sc) & (sc > sf)
            setf = near & ~hollow
            if floor_at is None:
                sf[setf] = (sh - depth)[setf]
            else:
                sf[setf] = floor_at
            inside = near & (o <= 0)
            sc[inside] = sh[inside] + 100
            # forced cells: centre inside the shape
            ci0, ci1 = max(0, i0 - 1), min(nx - 2, i1); cj0, cj1 = max(0, j0 - 1), min(nz - 2, j1)
            CX, CZ = np.meshgrid(xs[ci0:ci1 + 1] + cell / 2, zs[cj0:cj1 + 1] + cell / 2)
            forced[cj0:cj1 + 1, ci0:ci1 + 1] |= outside(CX, CZ) <= 0

    F = np.where(np.isnan(F), F, np.minimum(F, H))
    kinds = np.zeros((nz - 1, nx - 1), np.uint8)
    f4 = F[:-1, :-1] + F[:-1, 1:] + F[1:, :-1] + F[1:, 1:]
    c4 = C[:-1, :-1] + C[:-1, 1:] + C[1:, :-1] + C[1:, 1:]
    h4 = (H[:-1, :-1] + H[:-1, 1:] + H[1:, :-1] + H[1:, 1:]) * 0.25
    ok = ~np.isnan(f4 + c4)
    fc, cc = f4 * 0.25, c4 * 0.25
    hol = ok & (cc - fc > 0.25)
    kinds[hol] = np.where(cc[hol] >= h4[hol] - ROOF_MIN, OPEN, CAVE)
    kinds[forced] = OPEN
    # corners of hollow cells need a floor
    js, is_ = np.nonzero(kinds != SOLID)
    for j, i in zip(js, is_):
        ks = [(j, i), (j, i + 1), (j + 1, i), (j + 1, i + 1)]
        vals = [F[a, b] for a, b in ks if not np.isnan(F[a, b])]
        for a, b in ks:
            if np.isnan(F[a, b]):
                F[a, b] = min(sum(vals) / len(vals), H[a, b]) if vals else H[a, b] - 4.0
            if np.isnan(C[a, b]):
                C[a, b] = F[a, b]
    C = np.where(np.isnan(C), C, np.minimum(C, H - ROOF_MIN))
    return Voids(t, kinds, F, C)
