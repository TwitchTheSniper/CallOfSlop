"""Terrain for 09 Beachhead (tools/cluttered/beachhead.py): the heightfield design and a numpy port of the game's
terrain features (Source/Maps/Terrain.cs: grid, plateau/flatten, ramp), so the generator knows the ground height
everywhere, can put boxes at absolute heights, and can measure walks and sight lines over the terrain.

The base shape is one `grid` feature in "set" mode (2 m cells): the beach rising gently from the waterline, dunes, the
bluff (a steep face along most of the coast, a long gentle dune slope in the east) and the flat base plateau at
PLATEAU_Y, with the ground outside the outline rising into hills (except along the shore, where it runs down under the
water). Sharp things are ordinary features after it: the ravine, the switchback road, the bluff paths, the bunker pads,
the drainage tunnel's trench and the hill pads.
"""
import math
import numpy as np

PLATEAU_Y = 12.0
WATER_Y = 0.0
CELL = 0.5          # the map's terrain cell
GRID_CELL = 2.0     # the base grid feature's cell


def smoothstep(e):
    t = np.clip(e, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def interp(table, x):
    """Piecewise-linear lookup in [(x, v), ...] (clamped)."""
    xs = np.array([p[0] for p in table], float)
    vs = np.array([p[1] for p in table], float)
    return np.interp(x, xs, vs)


def _hash(ix, iz, seed):
    h = (ix * 374761393 + iz * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFFFF) / float(0xFFFFFF) * 2.0 - 1.0


def value_noise(x, z, seed):
    """Smooth value noise (numpy arrays), the generator's own (it only shapes the grid feature's numbers)."""
    fx0 = np.floor(x); fz0 = np.floor(z)
    ix = fx0.astype(np.int64); iz = fz0.astype(np.int64)
    fx = x - fx0; fz = z - fz0
    sx = fx * fx * (3 - 2 * fx); sz = fz * fz * (3 - 2 * fz)
    hv = np.vectorize(_hash)
    h00 = hv(ix, iz, seed); h10 = hv(ix + 1, iz, seed); h01 = hv(ix, iz + 1, seed); h11 = hv(ix + 1, iz + 1, seed)
    a = h00 + (h10 - h00) * sx
    b = h01 + (h11 - h01) * sx
    return a + (b - a) * sz


class Terrain:
    """The game's terrain grid for this map (cell 0.5 m, origin at -ext), filled by applying the features in order."""

    def __init__(self, half_x, half_z, margin):
        self.cell = CELL
        self.ext_x = math.ceil((half_x + margin) / CELL) * CELL
        self.ext_z = math.ceil((half_z + margin) / CELL) * CELL
        self.ox, self.oz = -self.ext_x, -self.ext_z
        self.nx = int(round(2 * self.ext_x / CELL)) + 1
        self.nz = int(round(2 * self.ext_z / CELL)) + 1
        self.X = self.ox + np.arange(self.nx) * CELL
        self.Z = self.oz + np.arange(self.nz) * CELL
        self.H = np.zeros((self.nz, self.nx))
        self.features = []

    # ---------------------------------------------------------------- sampling
    def sample(self, x, z):
        """Bilinear height at (x, z), clamped to the grid (the game's TerrainData.Sample)."""
        fx = min(max((x - self.ox) / self.cell, 0.0), self.nx - 1.0)
        fz = min(max((z - self.oz) / self.cell, 0.0), self.nz - 1.0)
        i = min(int(fx), self.nx - 2); j = min(int(fz), self.nz - 2)
        tx = fx - i; tz = fz - j
        H = self.H
        return float((H[j, i] * (1 - tx) + H[j, i + 1] * tx) * (1 - tz) + (H[j + 1, i] * (1 - tx) + H[j + 1, i + 1] * tx) * tz)

    def lowest(self, x0, z0, x1, z1):
        """Lowest ground at the 9 points the game's "ground" anchor looks at (corners, edge midpoints, centre)."""
        xs = (x0, (x0 + x1) / 2, x1); zs = (z0, (z0 + z1) / 2, z1)
        return min(self.sample(x, z) for x in xs for z in zs)

    def highest(self, x0, z0, x1, z1, step=0.5):
        best = -1e9
        x = x0
        while x <= x1 + 1e-6:
            z = z0
            while z <= z1 + 1e-6:
                best = max(best, self.sample(x, z))
                z += step
            x += step
        return best

    def _box(self, x0, x1, z0, z1):
        i0 = max(0, int(math.ceil((x0 - self.ox) / self.cell))); i1 = min(self.nx - 1, int(math.floor((x1 - self.ox) / self.cell)))
        j0 = max(0, int(math.ceil((z0 - self.oz) / self.cell))); j1 = min(self.nz - 1, int(math.floor((z1 - self.oz) / self.cell)))
        return i0, i1, j0, j1

    @staticmethod
    def _level(h, target, mask, mode):
        v = h + (target - h) * mask
        if mode == "fill":
            return np.maximum(h, v)
        if mode == "cut":
            return np.minimum(h, v)
        return v

    # ---------------------------------------------------------------- features (port of Terrain.Apply)
    def grid(self, x0, z0, gc, cols, rows, heights, mode="set"):
        f = {"type": "grid", "x0": x0, "z0": z0, "cell": gc, "cols": cols, "rows": rows, "heights": heights, "mode": mode}
        self.features.append(f)
        hs = np.array(heights, float).reshape(rows, cols)
        i0, i1, j0, j1 = self._box(x0, x0 + (cols - 1) * gc, z0, z0 + (rows - 1) * gc)
        X, Z = np.meshgrid(self.X[i0:i1 + 1], self.Z[j0:j1 + 1])
        fx = (X - x0) / gc; fz = (Z - z0) / gc
        c = np.minimum(np.floor(fx).astype(int), cols - 2); r = np.minimum(np.floor(fz).astype(int), rows - 2)
        tx = fx - c; tz = fz - r
        v = (hs[r, c] * (1 - tx) + hs[r, c + 1] * tx) * (1 - tz) + (hs[r + 1, c] * (1 - tx) + hs[r + 1, c + 1] * tx) * tz
        sub = self.H[j0:j1 + 1, i0:i1 + 1]
        self.H[j0:j1 + 1, i0:i1 + 1] = v if mode == "set" else sub + v
        return f

    def plateau(self, x, z, height=None, w=None, d=None, radius=None, edge=None, mode=None, material=None, kind="plateau"):
        plateau = kind == "plateau"
        f = {"type": kind, "x": round(x, 3), "z": round(z, 3)}
        if radius is not None:
            f["radius"] = round(radius, 3)
        else:
            f["w"] = round(w, 3); f["d"] = round(d, 3)
        target = height if height is not None else self.sample(x, z)
        if height is not None:
            f["height"] = round(height, 3)
        edge = edge if edge is not None else (6 if plateau else 4)
        f["edge"] = edge
        mode = mode or ("fill" if plateau else "set")
        f["mode"] = mode
        if material:
            f["material"] = material
        self.features.append(f)
        circle = radius is not None
        hw = (w or 0) / 2; hd = (d or 0) / 2
        ex = (radius if circle else hw) + edge; ez = (radius if circle else hd) + edge
        i0, i1, j0, j1 = self._box(x - ex, x + ex, z - ez, z + ez)
        if i1 < i0 or j1 < j0:
            return f
        X, Z = np.meshgrid(self.X[i0:i1 + 1] - x, self.Z[j0:j1 + 1] - z)
        if circle:
            out = np.maximum(0, np.hypot(X, Z) - radius)
        else:
            out = np.hypot(np.maximum(np.abs(X) - hw, 0), np.maximum(np.abs(Z) - hd, 0))
        mask = (1 - smoothstep(out / edge)) if edge > 0 else (out <= 0).astype(float)
        sub = self.H[j0:j1 + 1, i0:i1 + 1]
        new = self._level(sub, target, mask, mode)
        self.H[j0:j1 + 1, i0:i1 + 1] = np.where(mask > 0, new, sub)
        return f

    def flatten(self, x, z, **kw):
        return self.plateau(x, z, kind="flatten", **kw)

    def ramp(self, pts, width=4.0, edge=3.0, mode="set", material=None):
        """A ramp/path through pts ([x, z] or [x, z, y]); points without y follow the ground before this feature."""
        full = [[p[0], p[1], p[2] if len(p) > 2 else self.sample(p[0], p[1])] for p in pts]
        f = {"type": "ramp", "points": [[round(v, 3) for v in p] for p in pts], "width": width, "edge": edge}
        if mode != "set":
            f["mode"] = mode
        if material:
            f["material"] = material
        self.features.append(f)
        half = width / 2
        xs = [p[0] for p in full]; zs = [p[1] for p in full]
        i0, i1, j0, j1 = self._box(min(xs) - half - edge, max(xs) + half + edge, min(zs) - half - edge, max(zs) + half + edge)
        X, Z = np.meshgrid(self.X[i0:i1 + 1], self.Z[j0:j1 + 1])
        best_d2 = np.full(X.shape, np.inf); target = np.zeros(X.shape)
        for k in range(len(full) - 1):
            ax, az, ay = full[k]; bx, bz, by = full[k + 1]
            abx, abz = bx - ax, bz - az
            t = np.clip(((X - ax) * abx + (Z - az) * abz) / (abx * abx + abz * abz), 0, 1)
            qx = ax + abx * t; qz = az + abz * t
            d2 = (X - qx) ** 2 + (Z - qz) ** 2
            better = d2 < best_d2
            best_d2 = np.where(better, d2, best_d2)
            target = np.where(better, ay + (by - ay) * t, target)
        d = np.sqrt(best_d2)
        mask = np.where(d <= half, 1.0, (1 - smoothstep((d - half) / edge)) if edge > 0 else 0.0)
        sub = self.H[j0:j1 + 1, i0:i1 + 1]
        new = self._level(sub, target, mask, mode)
        self.H[j0:j1 + 1, i0:i1 + 1] = np.where(mask > 0, new, sub)
        return f

    def finish(self):
        self.H = np.round(self.H * 1024) / 1024

    # ---------------------------------------------------------------- analysis
    def slope_deg(self):
        gz, gx = np.gradient(self.H, self.cell)
        return np.degrees(np.arctan(np.hypot(gx, gz)))


# ==================================================================== walking over the terrain (analysis only)
FULL_SPEED_DEG = 25.0
WALK_LIMIT_DEG = 45.0
SLOW_AT_LIMIT = 0.6


def uphill_factor(rise, run):
    """SlopeRules.UphillTimeFactor: time per metre walking up a slope rise/run (1 up to 25 degrees, 1/0.6 at 45)."""
    if rise <= 0 or run < 1e-3:
        return 1.0
    g = rise / run
    ny = 1 / math.sqrt(1 + g * g)
    full = math.cos(math.radians(FULL_SPEED_DEG)); lim = math.cos(math.radians(WALK_LIMIT_DEG))
    if ny >= full:
        return 1.0
    k = min(1.0, (full - ny) / (full - lim))
    return 1.0 / (1.0 - k * (1.0 - SLOW_AT_LIMIT))


class WalkGrid:
    """8-neighbour walking graph on the terrain samples: a step is allowed when it rises at most 45 degrees (or 0.3 m)
    and neither end is blocked. Two edge weights: the 3D length (what the self-test sums along the nav graph) and
    the walking time in metres at full speed (uphill slower above 25 degrees, like the game)."""

    def __init__(self, T, ok):
        from scipy.sparse import csr_matrix
        self.T = T
        nz, nx = T.H.shape
        idx = -np.ones((nz, nx), np.int64)
        idx[ok] = np.arange(int(ok.sum()))
        self.idx = idx
        self.n = int(ok.sum())
        rows, cols, lens, times = [], [], [], []
        H = T.H; c = T.cell
        for di, dj in ((0, 1), (1, 0), (1, 1), (1, -1), (0, -1), (-1, 0), (-1, -1), (-1, 1)):
            run = c * math.hypot(di, dj)
            a = idx[max(0, -di):nz - max(0, di), max(0, -dj):nx - max(0, dj)]
            b = idx[max(0, di):nz + min(0, di), max(0, dj):nx + min(0, dj)]
            ha = H[max(0, -di):nz - max(0, di), max(0, -dj):nx - max(0, dj)]
            hb = H[max(0, di):nz + min(0, di), max(0, dj):nx + min(0, dj)]
            m = (a >= 0) & (b >= 0)
            dh = (hb - ha)[m]
            okstep = np.abs(dh) <= max(0.3, run * 1.0)             # at most 45 degrees either way (bots don't slide down)
            aa, bb = a[m][okstep], b[m][okstep]; dh = dh[okstep]
            rows.append(aa); cols.append(bb)
            lens.append(np.sqrt(run * run + dh * dh))
            g = np.maximum(dh, 0) / run
            ny = 1 / np.sqrt(1 + g * g)
            full = math.cos(math.radians(FULL_SPEED_DEG)); lim = math.cos(math.radians(WALK_LIMIT_DEG))
            k = np.clip((full - ny) / (full - lim), 0, 1)
            times.append(run / (1 - k * (1 - SLOW_AT_LIMIT)))
        r = np.concatenate(rows); cc = np.concatenate(cols)
        self.len_g = csr_matrix((np.concatenate(lens), (r, cc)), shape=(self.n, self.n))
        self.time_g = csr_matrix((np.concatenate(times), (r, cc)), shape=(self.n, self.n))

    def node(self, x, z):
        T = self.T
        j = int(round((z - T.oz) / T.cell)); i = int(round((x - T.ox) / T.cell))
        best = None
        for r in range(0, 6):
            for jj in range(j - r, j + r + 1):
                for ii in range(i - r, i + r + 1):
                    if 0 <= jj < self.idx.shape[0] and 0 <= ii < self.idx.shape[1] and self.idx[jj, ii] >= 0:
                        d = (jj - j) ** 2 + (ii - i) ** 2
                        if best is None or d < best[0]:
                            best = (d, self.idx[jj, ii])
            if best:
                return best[1]
        return -1

    def fields(self, sources, kind="len"):
        from scipy.sparse.csgraph import dijkstra
        g = self.len_g if kind == "len" else self.time_g
        idx = [self.node(x, z) for x, z in sources]
        return dijkstra(g, directed=True, indices=idx)

    def to_grid(self, row):
        out = np.full(self.idx.shape, np.inf)
        m = self.idx >= 0
        out[m] = row[self.idx[m]]
        return out
