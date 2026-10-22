"""Python port of the game's terrain holes and caves (Source/Maps/TerrainVoids.cs, TerrainVoidGen.Build) and of the
column query (TerrainData.Column / GroundBelow), for map tools that put things on cave floors and under cave ceilings.

    v = Voids(terrain, features)       terrain: a tools/cluttered/terrain.py Terrain (the heights without holes/caves);
                                       features: the map's terrain feature list (only "cave" and "hole" are used)
    v.column(x, z) -> (kind, floor, ceiling, top, hollow)
    v.ground_below(x, z, y)            the ground under a point at height y (cave floor inside a cave, surface on top)

Formula for formula the same double arithmetic as the C#, so floors match the game's to the last quantum (1/1024 m);
only the drawing-only daylight is left out.
"""
import math
import numpy as np

QUANTUM = 1.0 / 1024.0
ROOF_MIN = 1.0
MIN_GAP = 0.02
SOLID, OPEN, CAVE = 0, 1, 2


def _smoothstep(e):
    t = min(1.0, max(0.0, e))
    return t * t * (3 - 2 * t)


class Voids:
    def __init__(self, terrain, features):
        self.t = terrain
        h = np.asarray(terrain.h, dtype=np.float64)
        self.h = h
        nz, nx = h.shape
        self.nx, self.nz = nx, nz
        self.cell = float(terrain.cell); self.ox = float(terrain.ox); self.oz = float(terrain.oz)
        floor = np.full((nz, nx), np.nan); ceil = np.full((nz, nx), np.nan)
        forced = np.zeros((nz - 1, nx - 1), bool)
        for f in features:
            typ = f["type"].lower()
            if typ == "cave":
                self._cave(f, floor, ceil)
            elif typ == "hole":
                self._hole(f, floor, ceil, forced)
        ok = ~np.isnan(floor)
        floor[ok] = np.minimum(floor[ok], h[ok])
        kinds = np.zeros((nz - 1, nx - 1), np.uint8)
        for j in range(nz - 1):
            for i in range(nx - 1):
                if forced[j, i]:
                    kinds[j, i] = OPEN
                    continue
                fs = (floor[j, i], floor[j, i + 1], floor[j + 1, i], floor[j + 1, i + 1])
                cs = (ceil[j, i], ceil[j, i + 1], ceil[j + 1, i], ceil[j + 1, i + 1])
                if any(math.isnan(v) for v in fs + cs):
                    continue
                fc = (fs[0] + fs[1] + fs[2] + fs[3]) * 0.25
                cc = (cs[0] + cs[1] + cs[2] + cs[3]) * 0.25
                hc = (h[j, i] + h[j, i + 1] + h[j + 1, i] + h[j + 1, i + 1]) * 0.25
                if cc - fc <= 0.25:
                    continue
                kinds[j, i] = OPEN if cc >= hc - ROOF_MIN else CAVE
        for j in range(nz - 1):
            for i in range(nx - 1):
                if kinds[j, i] == SOLID:
                    continue
                ks = ((j, i), (j, i + 1), (j + 1, i), (j + 1, i + 1))
                have = [floor[k] for k in ks if not math.isnan(floor[k])]
                s = sum(have)
                for k in ks:
                    if math.isnan(floor[k]):
                        floor[k] = min(s / len(have), h[k]) if have else h[k] - 4.0
                    if math.isnan(ceil[k]):
                        ceil[k] = floor[k]
        self.kinds = kinds
        okf = ~np.isnan(floor)
        fq = np.full_like(floor, np.nan); fq[okf] = np.round(floor[okf] / QUANTUM) * QUANTUM
        okc = ~np.isnan(ceil)
        cq = np.full_like(ceil, np.nan); cq[okc] = np.round(np.minimum(ceil[okc], h[okc] - ROOF_MIN) / QUANTUM) * QUANTUM
        self.floor = fq.astype(np.float32).astype(np.float64)
        self.ceil = cq.astype(np.float32).astype(np.float64)

    # ------------------------------------------------------------------ the generator
    def _sample(self, x, z):
        fx = min(max((x - self.ox) / self.cell, 0), self.nx - 1)
        fz = min(max((z - self.oz) / self.cell, 0), self.nz - 1)
        i = min(int(math.floor(fx)), self.nx - 2); j = min(int(math.floor(fz)), self.nz - 2)
        tx, tz = fx - i, fz - j
        H = self.h
        return (H[j, i] * (1 - tx) + H[j, i + 1] * tx) * (1 - tz) + (H[j + 1, i] * (1 - tx) + H[j + 1, i + 1] * tx) * tz

    def _range(self, minX, maxX, minZ, maxZ):
        c = self.cell
        return (max(0, int(math.ceil((minX - self.ox) / c))), min(self.nx - 1, int(math.floor((maxX - self.ox) / c))),
                max(0, int(math.ceil((minZ - self.oz) / c))), min(self.nz - 1, int(math.floor((maxZ - self.oz) / c))))

    @staticmethod
    def _merge(floor, ceil, j, i, f, c):
        of, oc = floor[j, i], ceil[j, i]
        if math.isnan(of) or math.isnan(oc):
            floor[j, i] = f; ceil[j, i] = c
            return
        has_new = c > f; has_old = oc > of
        if has_new and has_old:
            floor[j, i] = min(of, f); ceil[j, i] = max(oc, c)
        elif has_new or (not has_old and c - f > oc - of):
            floor[j, i] = f; ceil[j, i] = c

    def _cave(self, f, floor, ceil):
        src = f["points"]
        w = float(f.get("width", 5)); height = float(f.get("height", 3.5)); depth = float(f.get("depth", 6))
        op = "none" if len(src) < 2 else f.get("open", "both")
        pts = []
        for p, q in enumerate(src):
            x, z = float(q[0]), float(q[1])
            end = (p == 0 and op in ("both", "start")) or (p == len(src) - 1 and op in ("both", "end"))
            y = float(q[2]) if len(q) > 2 else (self._sample(x, z) if end else self._sample(x, z) - depth)
            pts.append((x, z, y))
        half = w / 2; reach = half * 1.5; pad = reach + 2 * self.cell
        i0, i1, j0, j1 = self._range(min(p[0] for p in pts) - pad, max(p[0] for p in pts) + pad, min(p[1] for p in pts) - pad, max(p[1] for p in pts) + pad)
        for j in range(j0, j1 + 1):
            pz = self.oz + j * self.cell
            for i in range(i0, i1 + 1):
                px = self.ox + i * self.cell
                seg, t, d2 = _closest(pts, px, pz)
                u = math.sqrt(d2) / half
                if u > 1.5:
                    continue
                a = pts[seg]; b = pts[min(seg + 1, len(pts) - 1)]
                f0 = a[2] + (b[2] - a[2]) * t
                u2 = u * u
                c = f0 + height * (1 - u2 * u2)
                fl = f0 + height * 0.4 * _smoothstep((u - 0.6) / 0.5)
                self._merge(floor, ceil, j, i, fl, c)

    def _hole(self, f, floor, ceil, forced):
        shape = f.get("shape", "circle")
        x, z, r = float(f.get("x", 0)), float(f.get("z", 0)), float(f.get("radius", 1))
        rz = float(f.get("radiusZ", r)); ang = float(f.get("angle", 0)) * math.pi / 180
        ca, sa = math.cos(ang), math.sin(ang)
        poly = f.get("points") or []
        depth = float(f.get("depth", 4)); floor_at = float(f["floor"]) if "floor" in f else None

        def outside(px, pz):
            if shape == "polygon":
                inside = False; best = 1e300
                n = len(poly); b_ = n - 1
                for a_ in range(n):
                    ax, az, bx, bz = poly[a_][0], poly[a_][1], poly[b_][0], poly[b_][1]
                    if (az > pz) != (bz > pz) and px < (bx - ax) * (pz - az) / (bz - az) + ax:
                        inside = not inside
                    ex, ez = bx - ax, bz - az
                    t = min(1.0, max(0.0, ((px - ax) * ex + (pz - az) * ez) / (ex * ex + ez * ez)))
                    qx, qz = ax + ex * t - px, az + ez * t - pz
                    best = min(best, qx * qx + qz * qz)
                    b_ = a_
                return -math.sqrt(best) if inside else math.sqrt(best)
            if shape == "ellipse":
                dx, dz = px - x, pz - z
                lx, lz = dx * ca + dz * sa, -dx * sa + dz * ca
                return (math.sqrt((lx / r) ** 2 + (lz / rz) ** 2) - 1) * min(r, rz)
            return math.hypot(px - x, pz - z) - r

        if shape == "polygon":
            minX, maxX = min(p[0] for p in poly), max(p[0] for p in poly); minZ, maxZ = min(p[1] for p in poly), max(p[1] for p in poly)
        else:
            big = max(r, rz); minX, maxX, minZ, maxZ = x - big, x + big, z - big, z + big
        c = self.cell; margin = 1.5 * c
        i0, i1, j0, j1 = self._range(minX - margin - c, maxX + margin + c, minZ - margin - c, maxZ + margin + c)
        for j in range(j0, j1 + 1):
            for i in range(i0, i1 + 1):
                o = outside(self.ox + i * c, self.oz + j * c)
                if o > margin:
                    continue
                hollow = not math.isnan(floor[j, i]) and not math.isnan(ceil[j, i]) and ceil[j, i] > floor[j, i]
                if not hollow:
                    floor[j, i] = floor_at if floor_at is not None else self.h[j, i] - depth
                    if o > 0:
                        continue
                if o <= 0:
                    ceil[j, i] = self.h[j, i] + 100
        for j in range(max(0, j0 - 1), min(self.nz - 2, j1) + 1):
            for i in range(max(0, i0 - 1), min(self.nx - 2, i1) + 1):
                if outside(self.ox + i * c + c / 2, self.oz + j * c + c / 2) <= 0:
                    forced[j, i] = True

    # ------------------------------------------------------------------ queries
    def _locate(self, x, z):
        fx = (x - self.ox) / self.cell; fz = (z - self.oz) / self.cell
        i = min(max(int(math.floor(fx)), 0), self.nx - 2); j = min(max(int(math.floor(fz)), 0), self.nz - 2)
        return i, j, min(max(fx - i, 0.0), 1.0), min(max(fz - j, 0.0), 1.0)

    @staticmethod
    def _bilerp(a, j, i, tx, tz):
        return (a[j, i] * (1 - tx) + a[j, i + 1] * tx) * (1 - tz) + (a[j + 1, i] * (1 - tx) + a[j + 1, i + 1] * tx) * tz

    def column(self, x, z):
        i, j, tx, tz = self._locate(x, z)
        top = self._bilerp(self.h, j, i, tx, tz)
        kind = int(self.kinds[j, i])
        if kind == SOLID:
            return kind, top, top, top, False
        fl = self._bilerp(self.floor, j, i, tx, tz)
        if kind == OPEN:
            return kind, fl, math.inf, top, True
        ce = self._bilerp(self.ceil, j, i, tx, tz)
        return kind, fl, ce, top, ce > fl + MIN_GAP

    def ground_below(self, x, z, y):
        kind, fl, ce, top, hollow = self.column(x, z)
        if kind == OPEN or (kind == CAVE and hollow and y < (ce + top) * 0.5):
            return fl
        return top

    def ceiling_above(self, x, z, y):
        kind, fl, ce, top, hollow = self.column(x, z)
        return ce if kind == CAVE and hollow and y < (ce + top) * 0.5 else math.inf


def _closest(pts, px, pz):
    if len(pts) == 1:
        return 0, 0.0, (px - pts[0][0]) ** 2 + (pz - pts[0][1]) ** 2
    best = (0, 0.0, math.inf)
    for k in range(len(pts) - 1):
        ax, az = pts[k][0], pts[k][1]
        abx, abz = pts[k + 1][0] - ax, pts[k + 1][1] - az
        t = min(1.0, max(0.0, ((px - ax) * abx + (pz - az) * abz) / (abx * abx + abz * abz)))
        qx, qz = ax + abx * t - px, az + abz * t - pz
        d2 = qx * qx + qz * qz
        if d2 < best[2]:
            best = (k, t, d2)
    return best
