"""Python port of Call of Slop's terrain generator (Source/Maps/Terrain.cs + TerrainData.cs), formula for formula.

Every step is the same double-precision arithmetic in the same order as the C#, so the quantized heights, the
material ids and the fingerprint (TerrainData.Hash) come out identical. The self-test logs the C# fingerprint of
Maps/Dev/TerrainTest.json; `python3 terrain.py Maps/Dev/TerrainTest.json` prints the Python one to compare.

    t = generate(map_json['terrain'], halfX, halfZ)   -> Terrain
    t.sample(x, z)          bilinear ground height (scalars or numpy arrays)
    t.normal(x, z)          bilinear-patch normal (nx, ny, nz)
    t.slope_deg()           per-sample slope (central differences, as drawn)
    t.walkable()            per-sample: slope <= 42 deg (World.MaxSlopeDeg)
    t.raycast(o, d, tmax)   distance to the ground along a ray (marching, for analysis)
    t.hash                  FNV-1a fingerprint, equal to the C# TerrainData.Hash
"""
import math, struct, sys, json
import numpy as np

QUANTUM = 1.0 / 1024.0
MAX_SLOPE_DEG = 42.0
MIN_WALK_NY = 0.7431448
MATERIALS = {'grass': 0, 'dirt': 1, 'rock': 2, 'sand': 3}


def smoothstep(e):
    t = np.clip(e, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def falloff(t, kind):
    t = np.asarray(t, dtype=np.float64)
    if kind == 'linear':
        v = 1 - t
    elif kind == 'dome':
        v = np.sqrt(np.maximum(1 - t * t, 0.0))
    elif kind == 'gauss':
        v = (np.exp(-4 * t * t) - math.exp(-4)) / (1 - math.exp(-4))
    else:
        v = 1 - smoothstep(t)
    return np.where(t >= 1, 0.0, v)


def hash2(ix, iz, seed):
    """TerrainGen.Hash: 32-bit integer hash in -1..1 (ix, iz: int64 arrays)."""
    M = np.uint64(0xFFFFFFFF)
    h = (ix.astype(np.int64) * 374761393 + iz.astype(np.int64) * 668265263 + np.int64(seed) * 1442695041)
    h = h.astype(np.uint64) & M  # wrap like C# unchecked int -> uint
    h = ((h ^ (h >> np.uint64(13))) * np.uint64(1274126177)) & M
    h = h ^ (h >> np.uint64(16))
    return (h & np.uint64(0xFFFFFF)).astype(np.float64) / float(0xFFFFFF) * 2.0 - 1.0


def value_noise(x, z, seed):
    fx0 = np.floor(x)
    fz0 = np.floor(z)
    ix = fx0.astype(np.int64)
    iz = fz0.astype(np.int64)
    fx = x - fx0
    fz = z - fz0
    sx = fx * fx * (3 - 2 * fx)
    sz = fz * fz * (3 - 2 * fz)
    h00 = hash2(ix, iz, seed)
    h10 = hash2(ix + 1, iz, seed)
    h01 = hash2(ix, iz + 1, seed)
    h11 = hash2(ix + 1, iz + 1, seed)
    a = h00 + (h10 - h00) * sx
    b = h01 + (h11 - h01) * sx
    return a + (b - a) * sz


def fbm(x, z, scale, octaves, persistence, lacunarity, seed):
    s = np.zeros_like(x)
    amp_sum = 0.0
    amp = 1.0
    freq = 1 / scale
    for o in range(octaves):
        s = s + amp * value_noise(x * freq, z * freq, seed + o * 1013)
        amp_sum += amp
        amp *= persistence
        freq *= lacunarity
    return s / amp_sum if amp_sum > 0 else s * 0


def closest(pts, px, pz, extend_ends):
    """TerrainGen.Closest over arrays: (seg, t, dist2, qx, qz). First segment wins ties (strict <)."""
    best_d2 = np.full(px.shape, np.finfo(np.float64).max)
    seg = np.zeros(px.shape, dtype=np.int64)
    bt = np.zeros(px.shape)
    bqx = np.zeros(px.shape)
    bqz = np.zeros(px.shape)
    last = len(pts) - 2
    for k in range(last + 1):
        ax, az = pts[k][0], pts[k][1]
        abx, abz = pts[k + 1][0] - ax, pts[k + 1][1] - az
        t = ((px - ax) * abx + (pz - az) * abz) / (abx * abx + abz * abz)
        if not extend_ends or k > 0:
            t = np.maximum(t, 0.0)
        if not extend_ends or k < last:
            t = np.minimum(t, 1.0)
        qx = ax + abx * t
        qz = az + abz * t
        d2 = (px - qx) * (px - qx) + (pz - qz) * (pz - qz)
        better = d2 < best_d2
        best_d2 = np.where(better, d2, best_d2)
        seg = np.where(better, k, seg)
        bt = np.where(better, t, bt)
        bqx = np.where(better, qx, bqx)
        bqz = np.where(better, qz, bqz)
    return seg, bt, best_d2, bqx, bqz


def level(h, target, mask, mode):
    v = h + (target - h) * mask
    if mode == 'fill':
        return np.maximum(h, v)
    if mode == 'cut':
        return np.minimum(h, v)
    return v


def col(c):
    if isinstance(c, str):
        c = c.lstrip('#')
        return (int(c[0:2], 16) / 255, int(c[2:4], 16) / 255, int(c[4:6], 16) / 255)
    return tuple(float(v) for v in c)


class Surface:
    def __init__(self, d=None):
        d = {k.lower(): v for k, v in (d or {}).items()}
        self.grass = np.float32(col(d.get('grass', [0.36, 0.50, 0.25])))
        self.dirt = np.float32(col(d.get('dirt', [0.47, 0.39, 0.28])))
        self.rock = np.float32(col(d.get('rock', [0.47, 0.46, 0.44])))
        self.sand = np.float32(col(d.get('sand', [0.70, 0.64, 0.47])))
        self.dirt_slope = float(d.get('dirtslope', 24))
        self.rock_slope = float(d.get('rockslope', 38))
        self.sand_below = d.get('sandbelow')
        self.variation = float(d.get('variation', 0.06))


class Terrain:
    def __init__(self, nx, nz, cell, ox, oz, h, mats, surface):
        self.nx, self.nz, self.cell, self.ox, self.oz = nx, nz, cell, ox, oz
        self.h = h  # (nz, nx) float64 (exact float32 values)
        self.mats = mats  # (nz, nx) uint8
        self.surface = surface
        self.xs = ox + np.arange(nx) * cell
        self.zs = oz + np.arange(nz) * cell
        self.hash = self._hash()

    @property
    def min(self):
        return float(self.h.min())

    @property
    def max(self):
        return float(self.h.max())

    def _locate(self, x, z):
        fx = np.clip((np.asarray(x, dtype=np.float64) - self.ox) / self.cell, 0, self.nx - 1)
        fz = np.clip((np.asarray(z, dtype=np.float64) - self.oz) / self.cell, 0, self.nz - 1)
        i = np.minimum(np.floor(fx).astype(np.int64), self.nx - 2)
        j = np.minimum(np.floor(fz).astype(np.int64), self.nz - 2)
        return i, j, fx - i, fz - j

    def sample(self, x, z):
        i, j, tx, tz = self._locate(x, z)
        h = self.h
        return (h[j, i] * (1 - tx) + h[j, i + 1] * tx) * (1 - tz) + (h[j + 1, i] * (1 - tx) + h[j + 1, i + 1] * tx) * tz

    def normal(self, x, z):
        i, j, tx, tz = self._locate(x, z)
        h = self.h
        dx = ((h[j, i + 1] - h[j, i]) * (1 - tz) + (h[j + 1, i + 1] - h[j + 1, i]) * tz) / self.cell
        dz = ((h[j + 1, i] - h[j, i]) * (1 - tx) + (h[j + 1, i + 1] - h[j, i + 1]) * tx) / self.cell
        n = np.stack([-dx, np.ones_like(dx), -dz], -1)
        return n / np.linalg.norm(n, axis=-1, keepdims=True)

    def gradient(self):
        return grad(self.h, self.cell)

    def slope_deg(self):
        gx, gz = self.gradient()
        return np.degrees(np.arctan(np.sqrt(gx * gx + gz * gz)))

    def walkable(self):
        """Per sample, by central differences (smooth; what the drawing and materials use)."""
        return self.slope_deg() <= MAX_SLOPE_DEG

    def cell_slope_deg(self):
        """Per cell (nz-1, nx-1): the steepest the game's bilinear collision normal gets in that cell (at its corners).
        This is what decides walk-up-or-blocked in World.MoveAxis, so use it for walkability."""
        h, c = self.h, self.cell
        dx0 = (h[:-1, 1:] - h[:-1, :-1]) / c  # along x on the cell's north edge (j)
        dx1 = (h[1:, 1:] - h[1:, :-1]) / c  # along x on the south edge (j+1)
        dz0 = (h[1:, :-1] - h[:-1, :-1]) / c  # along z on the west edge (i)
        dz1 = (h[1:, 1:] - h[:-1, 1:]) / c  # along z on the east edge (i+1)
        g = np.maximum.reduce([dx0 * dx0 + dz0 * dz0, dx1 * dx1 + dz0 * dz0, dx0 * dx0 + dz1 * dz1, dx1 * dx1 + dz1 * dz1])
        return np.degrees(np.arctan(np.sqrt(g)))

    def cell_walkable(self):
        return self.cell_slope_deg() <= MAX_SLOPE_DEG

    def walkable_at(self, x, z):
        return self.normal(x, z)[..., 1] >= MIN_WALK_NY

    def colors(self):
        """Per-sample drawing colour, like TerrainData.VertexColor (float32 maths there; close enough here)."""
        s = self.surface
        gx, gz = grad(self.h, self.cell)  # VertexNormal uses central differences too
        slope = np.degrees(np.arctan(np.sqrt(gx * gx + gz * gz)))
        rock = smoothstep((slope - (s.rock_slope - 4)) / 8)
        dirt = smoothstep((slope - (s.dirt_slope - 4)) / 8) * (1 - rock)
        sand = smoothstep((s.sand_below + 0.75 - self.h) / 1.5) * (1 - rock - dirt) if s.sand_below is not None else np.zeros_like(rock)
        grass = np.maximum(0, 1 - rock - dirt - sand)
        c = grass[..., None] * s.grass + dirt[..., None] * s.dirt + rock[..., None] * s.rock + sand[..., None] * s.sand
        rule = np.where(slope >= s.rock_slope, 2, np.where(slope >= s.dirt_slope, 1, np.where((s.sand_below is not None) & (self.h <= (s.sand_below or 0)), 3, 0)))
        painted = self.mats != rule
        pal = np.array([s.grass, s.dirt, s.rock, s.sand])
        pc = pal[self.mats] * (1 - rock[..., None] * 0.6) + s.rock * (rock[..., None] * 0.6)
        c = np.where(painted[..., None], pc, c)
        X, Z = np.meshgrid(self.xs, self.zs)
        vary = 1 + s.variation * value_noise(X / 9.0, Z / 9.0, 4242)
        return np.clip(c * vary[..., None], 0, 1)

    def raycast(self, o, d, tmax, step=None):
        """Distances along rays (rows of o, d; d normalised) to the ground, capped at tmax. Fine marching + bisection."""
        o = np.atleast_2d(o).astype(np.float64)
        d = np.atleast_2d(d).astype(np.float64)
        tmax = np.broadcast_to(np.asarray(tmax, dtype=np.float64), (len(o),)).copy()
        step = step or self.cell * 0.5
        n = int(np.ceil(tmax.max() / step)) + 1
        out = tmax.copy()
        hit = np.zeros(len(o), dtype=bool)
        prev_t = np.zeros(len(o))
        for k in range(n + 1):
            t = np.minimum(k * step, tmax)
            p = o + d * t[:, None]
            under = (self.sample(p[:, 0], p[:, 2]) >= p[:, 1]) & ~hit
            if under.any():
                lo, hi = prev_t[under], t[under]
                oo, dd = o[under], d[under]
                for _ in range(12):
                    mid = (lo + hi) / 2
                    pm = oo + dd * mid[:, None]
                    u = self.sample(pm[:, 0], pm[:, 2]) >= pm[:, 1]
                    hi = np.where(u, mid, hi)
                    lo = np.where(u, lo, mid)
                out[under] = np.where(k == 0, 0.0, hi)
                hit |= under
            prev_t = t
            if hit.all() or (t >= tmax).all():
                break
        return out

    def _hash(self):
        h = 2166136261
        def mix_bytes(bs, h):
            for b in bs:
                h ^= b
                h = (h * 16777619) & 0xFFFFFFFF
            return h
        h = mix_bytes(struct.pack('<II', self.nx, self.nz), h)
        h = mix_bytes(self.h.astype('<f4').tobytes(), h)
        h = mix_bytes(self.mats.astype(np.uint8).tobytes(), h)
        return h


def grad(h, cell):
    """TerrainGen.Gradient over the whole grid (central differences, one-sided at the edges)."""
    gx = np.empty_like(h)
    gz = np.empty_like(h)
    gx[:, 1:-1] = (h[:, 2:] - h[:, :-2]) / (2 * cell)
    gx[:, 0] = (h[:, 1] - h[:, 0]) / cell
    gx[:, -1] = (h[:, -1] - h[:, -2]) / cell
    gz[1:-1, :] = (h[2:, :] - h[:-2, :]) / (2 * cell)
    gz[0, :] = (h[1, :] - h[0, :]) / cell
    gz[-1, :] = (h[-1, :] - h[-2, :]) / cell
    return gx, gz


def _lower(d):
    return {k.lower(): v for k, v in d.items()}


def generate(spec, half_x, half_z):
    """TerrainGen.Generate."""
    s = _lower(spec)
    cell = float(s.get('cell', 1))
    margin = float(s.get('margin', 16))
    base = float(s.get('base', 0))
    ext_x = math.ceil((float(np.float32(half_x)) + margin) / cell) * cell
    ext_z = math.ceil((float(np.float32(half_z)) + margin) / cell) * cell
    ox, oz = -ext_x, -ext_z
    nx = int(round(2 * ext_x / cell)) + 1
    nz = int(round(2 * ext_z / cell)) + 1
    xs = ox + np.arange(nx) * cell
    zs = oz + np.arange(nz) * cell
    X, Z = np.meshgrid(xs, zs)
    H = np.full((nz, nx), base, dtype=np.float64)
    paint = np.full((nz, nx), 255, dtype=np.uint8)

    def gsample(x, z):
        fx = min(max((x - ox) / cell, 0), nx - 1)
        fz = min(max((z - oz) / cell, 0), nz - 1)
        i = min(int(math.floor(fx)), nx - 2)
        j = min(int(math.floor(fz)), nz - 2)
        tx, tz = fx - i, fz - j
        return (H[j, i] * (1 - tx) + H[j, i + 1] * tx) * (1 - tz) + (H[j + 1, i] * (1 - tx) + H[j + 1, i + 1] * tx) * tz

    def do_paint(f, cond):
        m = f.get('material')
        if m is not None:
            paint[cond] = MATERIALS[m]

    for f in spec.get('features', []):
        f = _lower(f)
        typ = f['type'].lower()
        num = lambda k, dflt: float(f[k.lower()]) if k.lower() in f else dflt
        if typ in ('hill', 'basin'):
            x, z, r = num('x', 0), num('z', 0), num('radius', 1)
            rz = num('radiusZ', r)
            amount = num('height', 0) if typ == 'hill' else -num('depth', 0)
            dx = (X - x) / r
            dz = (Z - z) / rz
            v = falloff(np.sqrt(dx * dx + dz * dz), f.get('falloff', 'smooth'))
            H = H + amount * v
            do_paint(f, v >= 0.5)
        elif typ in ('ridge', 'valley'):
            pts = [[float(c) for c in p] for p in f['points']]
            half = num('width', 1) / 2
            sign = 1.0 if typ == 'ridge' else -1.0
            amount = num('height', 0) if typ == 'ridge' else num('depth', 0)
            seg, t, d2, _, _ = closest(pts, X, Z, False)
            v = falloff(np.sqrt(d2) / half, f.get('falloff', 'smooth'))
            a = np.array([p[2] if len(p) > 2 else amount for p in pts])
            local = a[seg] + (a[seg + 1] - a[seg]) * t
            H = np.where(v > 0, H + sign * local * v, H)
            do_paint(f, v >= 0.5)
        elif typ in ('plateau', 'flatten'):
            x, z = num('x', 0), num('z', 0)
            plateau = typ == 'plateau'
            target = num('height', 0) if 'height' in f else gsample(x, z)
            edge = num('edge', 6 if plateau else 4)
            mode = f.get('mode', 'fill' if plateau else 'set')
            dx, dz = X - x, Z - z
            if 'radius' in f:
                outside = np.maximum(0.0, np.sqrt(dx * dx + dz * dz) - num('radius', 0))
            else:
                ax = np.maximum(np.abs(dx) - num('w', 0) / 2, 0.0)
                az = np.maximum(np.abs(dz) - num('d', 0) / 2, 0.0)
                outside = np.sqrt(ax * ax + az * az)
            mask = 1 - smoothstep(outside / edge) if edge > 0 else np.where(outside <= 0, 1.0, 0.0)
            H = np.where(mask > 0, level(H, target, mask, mode), H)
            do_paint(f, mask >= 0.5)
        elif typ == 'cliff':
            pts = [[float(c) for c in p] for p in f['points']]
            height = num('height', 0)
            face, back = num('face', 2), num('back', 25)
            left = f.get('side', 'left') == 'left'
            taper = num('taper', 8)
            cum = [0.0]
            for k in range(1, len(pts)):
                ddx, ddz = pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]
                cum.append(cum[-1] + math.sqrt(ddx * ddx + ddz * ddz))
            total = cum[-1]
            seg, t, d2, qx, qz = closest(pts, X, Z, True)
            P = np.array(pts)
            C = np.array(cum)
            abx = P[seg + 1, 0] - P[seg, 0]
            abz = P[seg + 1, 1] - P[seg, 1]
            u = C[seg] + t * (C[seg + 1] - C[seg])
            side = (X - qx) * abz - (Z - qz) * abx
            d = np.sqrt(d2)
            w = np.where(side >= 0, d, -d) * (1 if left else -1)
            prof = np.where(w >= 0, 1 - smoothstep(w / back), 1 - smoothstep(-w / face))
            if taper > 0:
                tp = smoothstep(np.minimum(np.clip(u / taper, 0, 1), np.clip((total - u) / taper, 0, 1)))
            else:
                tp = np.where((u >= 0) & (u <= total), 1.0, 0.0)
            v = prof * tp
            H = np.where(v > 0, H + height * v, H)
            do_paint(f, v >= 0.5)
        elif typ in ('ramp', 'path'):
            pts = [[float(p[0]), float(p[1]), float(p[2]) if len(p) > 2 else gsample(float(p[0]), float(p[1]))] for p in f['points']]
            half = num('width', 4) / 2
            edge = num('edge', 3)
            mode = f.get('mode', 'set')
            seg, t, d2, _, _ = closest(pts, X, Z, False)
            d = np.sqrt(d2)
            mask = np.where(d <= half, 1.0, (1 - smoothstep((d - half) / edge)) if edge > 0 else 0.0)
            ys = np.array([p[2] for p in pts])
            target = ys[seg] + (ys[seg + 1] - ys[seg]) * t
            H = np.where(mask > 0, level(H, target, mask, mode), H)
            do_paint(f, mask >= 0.5)
        elif typ == 'noise':
            amp = num('amplitude', 0)
            n = fbm(X, Z, num('scale', 24), int(num('octaves', 4)), num('persistence', 0.5), num('lacunarity', 2), int(num('seed', 1)))
            if 'radius' in f:
                xx, zz = X - num('x', 0), Z - num('z', 0)
                mask = 1 - smoothstep(np.sqrt(xx * xx + zz * zz) / num('radius', 1))
                H = np.where(mask > 0, H + amp * n * mask, H)
            else:
                H = H + amp * n * 1
        elif typ == 'smooth':
            mask = None
            if 'radius' in f:
                xx, zz = X - num('x', 0), Z - num('z', 0)
                mask = 1 - smoothstep(np.sqrt(xx * xx + zz * zz) / num('radius', 1))
            for _ in range(int(num('passes', 1))):
                L = np.concatenate([H[:, :1], H[:, :-1]], 1)
                R = np.concatenate([H[:, 1:], H[:, -1:]], 1)
                tmp = (L + 2 * H + R) * 0.25
                U = np.concatenate([tmp[:1], tmp[:-1]], 0)
                D = np.concatenate([tmp[1:], tmp[-1:]], 0)
                out = (U + 2 * tmp + D) * 0.25
                H = out if mask is None else H + (out - H) * mask
        elif typ == 'grid':
            x0, z0, gc = num('x0', 0), num('z0', 0), num('cell', 1)
            cols, rows = int(num('cols', 2)), int(num('rows', 2))
            hs = np.array(f['heights'], dtype=np.float64)
            fx = (X - x0) / gc
            fz = (Z - z0) / gc
            inside = (fx >= 0) & (fz >= 0) & (fx <= cols - 1) & (fz <= rows - 1)
            c = np.clip(np.minimum(np.floor(fx), cols - 2), 0, cols - 2).astype(np.int64)
            r = np.clip(np.minimum(np.floor(fz), rows - 2), 0, rows - 2).astype(np.int64)
            tx, tz = fx - c, fz - r
            v = (hs[r * cols + c] * (1 - tx) + hs[r * cols + c + 1] * tx) * (1 - tz) + (hs[(r + 1) * cols + c] * (1 - tx) + hs[(r + 1) * cols + c + 1] * tx) * tz
            H = np.where(inside, v if f.get('mode', 'add') == 'set' else H + v, H)
        else:
            raise ValueError(f'unknown terrain feature type {typ}')

    if s.get('min') is not None:
        H = np.maximum(H, float(s['min']))
    if s.get('max') is not None:
        H = np.minimum(H, float(s['max']))
    H = np.round(H / QUANTUM) * QUANTUM
    H = H.astype(np.float32).astype(np.float64)

    surf = Surface(s.get('surface'))
    gx, gz = grad(H, cell)
    s2 = gx * gx + gz * gz
    t_r = math.tan(surf.rock_slope * math.pi / 180.0)
    t_d = math.tan(surf.dirt_slope * math.pi / 180.0)
    rock2, dirt2 = t_r * t_r, t_d * t_d
    mats = np.where(s2 >= rock2, 2, np.where(s2 >= dirt2, 1, 0)).astype(np.uint8)
    if surf.sand_below is not None:
        mats = np.where((mats == 0) & (H <= float(surf.sand_below)), 3, mats).astype(np.uint8)
    mats = np.where(paint != 255, paint, mats).astype(np.uint8)
    return Terrain(nx, nz, np.float32(cell), np.float32(ox), np.float32(oz), H, mats, surf)


def from_map(path):
    data = json.load(open(path))
    if 'terrain' not in data:
        return None, data
    return generate(data['terrain'], data['halfX'], data['halfZ']), data


if __name__ == '__main__':
    for p in sys.argv[1:]:
        t, data = from_map(p)
        if t is None:
            print(f'{p}: no terrain')
            continue
        steep = (~t.walkable()).mean() * 100
        print(f"{data['name']}: {t.nx} x {t.nz} samples every {t.cell} m, heights {t.min:.2f} to {t.max:.2f} m, "
              f"{steep:.1f}% too steep, fingerprint {t.hash:08x}")
        for x, z in [(0, 0), (10.25, -7.5), (-20, 15)]:
            print(f'  ground at ({x}, {z}) = {float(t.sample(x, z)):.4f}')
