#!/usr/bin/env python3
"""townkit: reusable placement rules for Cluttered-pack town maps (numpy + Pillow).

A map generator makes one `Town`, builds its streets and buildings with `block`, `mesh` and `prop`, and then calls the
placement rules below, which put things where they belong:

- `street_furniture`: lamps, trees, hydrants, bins, benches, meters, signs and newspaper boxes along a sidewalk, at sensible
  spacing, lamps reaching over the road, never in front of a door or a junction.
- `kerb_parking`: cars parked along a kerb, nose to tail, aligned with the road (a few gaps, a few wrecks).
- `car_park`: bays in rows with painted lines, cars square in their bays, a ticket machine and lamps.
- `bins_by_door`: a dumpster against a back wall beside the back door, wheelie bins, trash bags and litter round them.
- `roof_kit`: AC units, condensers, turbine vents, satellite dishes, chimneys and hatches on a flat roof, kept off the edge.
- `wall_kit`: window AC units, electric meters, vent grilles and neon signs on a facade, at sensible heights.
- `furnish`: rooms by kind (diner, laundromat, barber, arcade, store, office, kitchen, living room, hall), furniture
  against the walls facing into the room, with a clear aisle from the door, desk-top things on the desks.
- `duck_pond`: a pond with ducks on it and on the bank, reeds round it, a bench facing it.
- `hedge_strip`, `tree_row`, `weeds_along`: foliage regions (hedges along yards and walls, street trees, weeds at the
  foot of walls).

The Town keeps an occupancy grid (0.25 m) of everything solid at body height, so nothing overlaps, plus lanes and
keep-out circles (spawns, hills, flags) that solid props stay off. It also ports the game's outline maths (centripetal
Catmull-Rom plus the boundary noise), so "inside the outline" and "distance to the edge" match the game, and has a
top-down preview, a spawn-to-spawn line-of-sight check and a grid walk-time field (for putting hills on the line of
equal walk).
"""
import json, math, random, glob, os
import numpy as np

# ===================================================================================== outline (port of MapBoundary.cs)

def _hash(ix, iz, seed):
    """TerrainGen.Hash: integer hash to -1..1 (uint32 arithmetic)."""
    h = (ix * 374761393 + iz * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFFFF) / float(0xFFFFFF) * 2.0 - 1.0


def catmull_rom(cp, step=0.25):
    """MapBoundary.CatmullRom: a closed centripetal Catmull-Rom curve through the points."""
    n = len(cp); out = []
    for i in range(n):
        p0, p1, p2, p3 = cp[(i - 1) % n], cp[i], cp[(i + 1) % n], cp[(i + 2) % n]
        def knot(a, b):
            return math.sqrt(math.hypot(b[0] - a[0], b[1] - a[1]))
        t0 = 0.0; t1 = t0 + knot(p0, p1); t2 = t1 + knot(p1, p2); t3 = t2 + knot(p2, p3)
        steps = min(2000, max(4, math.ceil(math.hypot(p2[0] - p1[0], p2[1] - p1[1]) / step)))
        for k in range(steps):
            t = t1 + (t2 - t1) * k / steps
            def L(a, b, ta, tb):
                return ((tb - t) / (tb - ta) * a[0] + (t - ta) / (tb - ta) * b[0], (tb - t) / (tb - ta) * a[1] + (t - ta) / (tb - ta) * b[1])
            a1 = L(p0, p1, t0, t1); a2 = L(p1, p2, t1, t2); a3 = L(p2, p3, t2, t3)
            b1 = L(a1, a2, t0, t2); b2 = L(a2, a3, t1, t3)
            out.append(L(b1, b2, t1, t2))
    return out


def _signed_area(pts):
    a = 0.0
    for i in range(len(pts)):
        p = pts[i]; q = pts[(i + 1) % len(pts)]
        a += p[0] * q[1] - q[0] * p[1]
    return a * 0.5


def add_noise(pts, amplitude, scale, seed, octaves=2):
    """MapBoundary.AddNoise: periodic fractal value noise of the arc length along the normals of a CCW outline."""
    scale = min(200.0, max(1.0, scale)); octaves = min(4, max(1, octaves))
    h = max(0.1, min(0.5, scale / (8.0 * (1 << (octaves - 1)))))
    dense = []
    for i in range(len(pts)):
        a = pts[i]; b = pts[(i + 1) % len(pts)]
        k = max(1, math.ceil(math.hypot(b[0] - a[0], b[1] - a[1]) / h))
        for s in range(k):
            dense.append((a[0] + (b[0] - a[0]) * s / k, a[1] + (b[1] - a[1]) * s / k))
    n = len(dense); arc = [0.0] * (n + 1)
    for i in range(n):
        arc[i + 1] = arc[i] + math.hypot(dense[(i + 1) % n][0] - dense[i][0], dense[(i + 1) % n][1] - dense[i][1])
    total = arc[n]; out = []
    for i in range(n):
        pv = dense[(i - 1) % n]; nx = dense[(i + 1) % n]
        dx, dz = nx[0] - pv[0], nx[1] - pv[1]; L = math.hypot(dx, dz)
        nrm = (dz / L, -dx / L) if L > 1e-6 else (1.0, 0.0)
        s = 0.0; norm = 0.0; amp = 1.0
        for o in range(octaves):
            lattice = max(3, int(round(total / (scale / (1 << o)))))
            u = arc[i] / total * lattice; i0 = math.floor(u); f = u - i0; f = f * f * (3 - 2 * f)
            v0 = _hash(i0 % lattice, o, seed); v1 = _hash((i0 + 1) % lattice, o, seed)
            s += amp * (v0 + (v1 - v0) * f); norm += amp; amp *= 0.5
        off = amplitude * s / norm
        out.append((dense[i][0] + nrm[0] * off, dense[i][1] + nrm[1] * off))
    return out


def build_outline(points, noise=None):
    """The outline the game builds for a smooth boundary (before simplification, which stays within 5 cm of it)."""
    pts = catmull_rom(points)
    if _signed_area(pts) < 0:
        pts.reverse()
    if noise and noise.get("amplitude", 0) > 0:
        pts = add_noise(pts, noise["amplitude"], noise.get("scale", 12), noise.get("seed", 1), noise.get("octaves", 2))
    return pts


# ===================================================================================== props library

def load_props(repo, pack_dirs=()):
    """name -> footprint and flags for every prop in Props/ (and the given pack Props folders, which shadow)."""
    lib = {}
    files = glob.glob(os.path.join(repo, "Props/*/*.json"))
    for pd in pack_dirs:
        files += glob.glob(os.path.join(pd, "**/*.json"), recursive=True)
    for f in files:
        d = json.load(open(f))
        if "parts" not in d:
            continue       # Props/Flags/flags.json holds flag designs, not a prop
        xs, zs, ys = [], [], []
        for p in d["parts"]:
            s = p["size"]; o = p.get("offset", [0, s[1] / 2, 0])
            rot = p.get("rotation") or [p.get("pitch", 0), p.get("yaw", 0), p.get("roll", 0)]
            pa, ya, ra = (math.radians(v) for v in rot)
            corners = []
            for sx in (-0.5, 0.5):
                for sy in (-0.5, 0.5):
                    for sz in (-0.5, 0.5):
                        x, y, z = sx * s[0], sy * s[1], sz * s[2]
                        x, y = x * math.cos(ra) - y * math.sin(ra), x * math.sin(ra) + y * math.cos(ra)      # roll about z
                        y, z = y * math.cos(pa) - z * math.sin(pa), y * math.sin(pa) + z * math.cos(pa)      # pitch about x
                        x, z = x * math.cos(ya) + z * math.sin(ya), -x * math.sin(ya) + z * math.cos(ya)     # yaw about y
                        corners.append((o[0] + x, o[1] + y, o[2] + z))
            bottom = min(c[1] for c in corners); ys.append(max(c[1] for c in corners))
            if bottom < 1.9:   # the footprint is what stands at body height (a lamp's arm or an awning overhead doesn't count)
                xs += [c[0] for c in corners]; zs += [c[2] for c in corners]
        if not xs:
            xs, zs = [-0.1, 0.1], [-0.1, 0.1]
        lib[d["name"]] = dict(solid=d.get("collision", "solid") == "solid", brk="breakable" in d, cat=d.get("category", os.path.basename(os.path.dirname(f))),
                              w=max(xs) - min(xs), d=max(zs) - min(zs), h=max(ys), cx=(max(xs) + min(xs)) / 2, cz=(max(zs) + min(zs)) / 2,
                              anim=any("anim" in p for p in d["parts"]))
    return lib


def yaw_facing(dx, dz):
    """Prop yaw (degrees) that turns its front (+Z) to face direction (dx, dz)."""
    return round(math.degrees(math.atan2(dx, dz)) % 360, 1)


def yaw_along(dx, dz):
    """Prop yaw that turns its long +X axis along direction (dx, dz)."""
    return round(math.degrees(math.atan2(-dz, dx)) % 360, 1)


def R(v):
    return round(float(v), 2)


def col(c):
    return [round(float(v), 3) for v in c] if not isinstance(c, str) else c


# ===================================================================================== the town

class Town:
    def __init__(self, repo, ctrl, noise=None, pack_props=(), res=0.25, pad=6.0):
        self.repo = repo
        self.ctrl = ctrl; self.noise = noise
        self.outline = build_outline(ctrl, noise)
        self.P = load_props(repo, pack_props)
        xs = [p[0] for p in self.outline]; zs = [p[1] for p in self.outline]
        self.x0, self.z0 = math.floor(min(xs) - pad), math.floor(min(zs) - pad)
        self.x1, self.z1 = math.ceil(max(xs) + pad), math.ceil(max(zs) + pad)
        self.res = res
        self.nx = int((self.x1 - self.x0) / res); self.nz = int((self.z1 - self.z0) / res)
        self.occ = np.zeros((self.nz, self.nx), np.uint8)    # 1 = something solid at body height (placement)
        self.walk = np.zeros((self.nz, self.nx), np.uint8)   # 1 = blocks walking (taller than a step)
        self.tall = np.zeros((self.nz, self.nx), np.uint8)   # 1 = blocks sight at eye height
        self.lane = np.zeros((self.nz, self.nx), np.uint8)   # 1 = a walking lane solid props keep off
        self._sdf()
        self.ops = []; self.boxes = []; self.props = []; self.meshes = 0
        self.items = []          # foliage items (hand-placed trees)
        self.regions = []; self.paths = []; self.exclude = []; self.include = []
        self.keepout = []        # (x, z, r)
        self.notes = {}          # counters by rule

    # ---------------------------------------------------------------- geometry helpers
    def _sdf(self):
        """Signed distance to the outline on the grid (positive inside), at 0.5 m, from a decimated outline."""
        step = 2
        pts = np.array(self.outline[::step], float)
        gx = np.arange(self.x0, self.x1, 0.5) + 0.25; gz = np.arange(self.z0, self.z1, 0.5) + 0.25
        X, Z = np.meshgrid(gx, gz)
        d = np.full(X.shape, 1e9); inside = np.zeros(X.shape, bool)
        a = pts; b = np.roll(pts, -1, axis=0)
        for (ax, az), (bx, bz) in zip(a, b):
            dx, dz = bx - ax, bz - az; L = dx * dx + dz * dz
            t = np.clip(((X - ax) * dx + (Z - az) * dz) / (L if L > 0 else 1), 0, 1)
            d = np.minimum(d, np.hypot(ax + t * dx - X, az + t * dz - Z))
            cond = ((az > Z) != (bz > Z)) & (X < ax + (Z - az) * (bx - ax) / ((bz - az) if bz != az else 1e-9))
            inside ^= cond
        self.sd = np.where(inside, d, -d); self.sd_x0 = self.x0; self.sd_z0 = self.z0

    def edge(self, x, z):
        """Signed distance from (x, z) to the outline: positive inside."""
        i = int((z - self.sd_z0) / 0.5); j = int((x - self.sd_x0) / 0.5)
        if i < 0 or j < 0 or i >= self.sd.shape[0] or j >= self.sd.shape[1]:
            return -99.0
        return float(self.sd[i, j])

    def inside(self, x, z, margin=0.0):
        return self.edge(x, z) >= margin

    def area(self):
        return abs(_signed_area(self.outline))

    def _ij(self, x0, z0, x1, z1):
        j0 = max(0, int(math.floor((x0 - self.x0) / self.res))); j1 = min(self.nx, int(math.ceil((x1 - self.x0) / self.res)))
        i0 = max(0, int(math.floor((z0 - self.z0) / self.res))); i1 = min(self.nz, int(math.ceil((z1 - self.z0) / self.res)))
        return i0, i1, j0, j1

    def mark(self, grid, r, v=1):
        i0, i1, j0, j1 = self._ij(*r)
        grid[i0:i1, j0:j1] = v

    def free(self, r, gap=0.0, grid=None):
        i0, i1, j0, j1 = self._ij(r[0] - gap, r[1] - gap, r[2] + gap, r[3] + gap)
        g = self.occ if grid is None else grid
        return not g[i0:i1, j0:j1].any()

    def on_lane(self, r):
        i0, i1, j0, j1 = self._ij(*r)
        return self.lane[i0:i1, j0:j1].any()

    def in_keepout(self, r, extra=0.0):
        for (x, z, rad) in self.keepout:
            dx = max(r[0] - x, 0, x - r[2]); dz = max(r[1] - z, 0, z - r[3])
            if math.hypot(dx, dz) < rad + extra:
                return True
        return False

    def add_lane(self, pts, width):
        """A walking lane: solid props keep off it; it's also a foliage path (nothing grows on it) unless told otherwise."""
        for k in range(len(pts) - 1):
            (ax, az), (bx, bz) = pts[k], pts[k + 1]
            L = math.hypot(bx - ax, bz - az); n = max(1, int(L / 0.25))
            for s in range(n + 1):
                x = ax + (bx - ax) * s / n; z = az + (bz - az) * s / n
                self.mark(self.lane, (x - width / 2, z - width / 2, x + width / 2, z + width / 2))

    # ---------------------------------------------------------------- ops
    def block(self, x0, y0, z0, x1, y1, z1, c):
        x0, x1 = min(x0, x1), max(x0, x1); z0, z1 = min(z0, z1), max(z0, z1)
        self.ops.append({"op": "Block", "x0": R(x0), "y0": R(y0), "z0": R(z0), "x1": R(x1), "y1": R(y1), "z1": R(z1), "color": col(c)})
        self.boxes.append((x0, z0, x1, z1, y0, y1, c))
        r = (x0, z0, x1, z1)
        if y0 < 1.8:
            self.mark(self.occ, r)
        if y0 < 1.7 and y1 > 0.55:
            self.mark(self.walk, r)
        if y0 < 1.6 and y1 > 1.6:
            self.mark(self.tall, r)

    def mesh(self, kind, x, z, c, **kw):
        op = {"op": "Mesh", "mesh": kind, "x": R(x), "z": R(z)}
        for k, v in kw.items():
            op[k] = R(v) if isinstance(v, float) else v
        op["color"] = col(c)
        self.ops.append(op); self.meshes += 1

    def foot(self, name, x, z, yaw):
        p = self.P[name]; a = math.radians(yaw); cs, sn = math.cos(a), math.sin(a)
        hx = abs(p["w"] / 2 * cs) + abs(p["d"] / 2 * sn); hz = abs(p["w"] / 2 * sn) + abs(p["d"] / 2 * cs)
        cx = x + p["cx"] * cs + p["cz"] * sn; cz = z - p["cx"] * sn + p["cz"] * cs
        return (cx - hx, cz - hz, cx + hx, cz + hz)

    def prop(self, name, x, z, yaw=0.0, y=None, check=True, gap=0.02, lanes=False, margin=0.6, tag=None):
        """Places a prop if its footprint is free (solid ones also off lanes when lanes=True, off keep-outs, inside the
        outline by margin). Returns True when placed."""
        if name not in self.P:
            raise KeyError(f"unknown prop {name}")
        p = self.P[name]; yaw = round(yaw % 360, 1)
        r = self.foot(name, x, z, yaw)
        corners = ((r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3]), (x, z))
        if not all(self.inside(cx, cz, margin) for cx, cz in corners):
            return False
        ground = y is None or y < 0.6
        if check and ground:
            if p["solid"]:
                if not self.free(r, gap) or self.in_keepout(r, 0.3) or (lanes and self.on_lane(r)):
                    return False
            elif not self.free(r, 0.0):
                return False
        op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
        if yaw:
            op["yaw"] = R(yaw)
        if y is not None and y > 0:
            op["y"] = R(y)
        self.ops.append(op); self.props.append((name, x, z, yaw, y or 0.0, tag))
        if ground and p["solid"]:
            self.mark(self.occ, r)
            if p["h"] > 0.55:
                self.mark(self.walk, r)
            if p["h"] > 1.6:
                self.mark(self.tall, r)
        self.notes[tag or "other"] = self.notes.get(tag or "other", 0) + 1
        return True

    def clutter(self, names, x0, z0, x1, z1, count, seed, width=0.0, align=False, gap=None):
        op = {"op": "Clutter", "props": list(names), "x0": R(x0), "z0": R(z0), "x1": R(x1), "z1": R(z1), "count": int(count), "seed": int(seed)}
        if width:
            op["width"] = R(width)
        if align:
            op["align"] = True
        if gap is not None:
            op["gap"] = R(gap)
        self.ops.append(op)

    def tree(self, x, z, h, kind="tree", check=True):
        """A hand-placed foliage tree (its trunk is a solid box in the game)."""
        r = (x - 0.45, z - 0.45, x + 0.45, z + 0.45)
        if check and (not self.free(r, 0.3) or not self.inside(x, z, 1.5) or self.in_keepout(r, 1.2)):
            return False
        self.items.append({"type": kind, "x": R(x), "z": R(z), "height": R(h)})
        self.mark(self.occ, r); self.mark(self.walk, (x - 0.25, z - 0.25, x + 0.25, z + 0.25)); self.mark(self.tall, (x - 0.25, z - 0.25, x + 0.25, z + 0.25))
        self.boxes.append((x - 0.25, z - 0.25, x + 0.25, z + 0.25, 0, h, (0.32, 0.22, 0.12)))
        return True

    # ---------------------------------------------------------------- foliage helpers
    def region(self, kind, density, **shape):
        d = {"type": kind, "density": round(density, 3)}
        d.update(shape)
        self.regions.append(d)

    def path(self, pts, width, density=None):
        d = {"points": [[R(a), R(b)] for a, b in pts], "width": R(width)}
        if density is not None:
            d["density"] = density
        self.paths.append(d)

    def exclude_rect(self, x0, z0, x1, z1, density=None):
        d = {"shape": "rect", "center": [R((x0 + x1) / 2), R((z0 + z1) / 2)], "halfX": R(abs(x1 - x0) / 2), "halfZ": R(abs(z1 - z0) / 2)}
        if density:
            d["density"] = density
        self.exclude.append(d)

    # ---------------------------------------------------------------- analysis
    def walk_field(self, sources, cell=0.5):
        """Walk distance (metres, 8-neighbour Dijkstra on a 0.5 m grid, avoiding walk-blocking boxes and props and
        the outside) from the source points. Returns (dist array, cell, origin)."""
        import heapq
        k = int(cell / self.res)
        nz, nx = self.nz // k, self.nx // k
        blk = self.walk[:nz * k, :nx * k].reshape(nz, k, nx, k).max(axis=(1, 3)).astype(bool)
        gx = self.x0 + (np.arange(nx) + 0.5) * cell; gz = self.z0 + (np.arange(nz) + 0.5) * cell
        for i in range(nz):
            for j in range(nx):
                if not blk[i, j] and self.edge(gx[j], gz[i]) < 0.4:
                    blk[i, j] = True
        dist = np.full((nz, nx), np.inf); h = []
        for (x, z) in sources:
            j = int((x - self.x0) / cell); i = int((z - self.z0) / cell)
            dist[i, j] = 0; heapq.heappush(h, (0.0, i, j))
        nb = [(-1, 0, 1), (1, 0, 1), (0, -1, 1), (0, 1, 1), (-1, -1, 1.4142), (-1, 1, 1.4142), (1, -1, 1.4142), (1, 1, 1.4142)]
        while h:
            d, i, j = heapq.heappop(h)
            if d > dist[i, j]:
                continue
            for di, dj, w in nb:
                a, b = i + di, j + dj
                if 0 <= a < nz and 0 <= b < nx and not blk[a, b]:
                    if di and dj and (blk[i, b] or blk[a, j]):
                        continue
                    nd = d + w * cell
                    if nd < dist[a, b]:
                        dist[a, b] = nd; heapq.heappush(h, (nd, a, b))
        return dist, cell

    def walk_to(self, field, x, z):
        dist, cell = field
        return float(dist[int((z - self.z0) / cell), int((x - self.x0) / cell)])

    def sees(self, a, b):
        """Eye-height line of sight between two points (tall boxes and props block, so does the outside)."""
        L = math.hypot(b[0] - a[0], b[1] - a[1]); n = max(1, int(L / 0.2))
        for s in range(1, n):
            x = a[0] + (b[0] - a[0]) * s / n; z = a[1] + (b[1] - a[1]) * s / n
            if not self.inside(x, z, 0.0):
                return False
            i = int((z - self.z0) / self.res); j = int((x - self.x0) / self.res)
            if self.tall[i, j]:
                return False
        return True

    def preview(self, path, marks=(), scale=5):
        from PIL import Image, ImageDraw
        W = int((self.x1 - self.x0) * scale); H = int((self.z1 - self.z0) * scale)
        img = Image.new("RGB", (W, H), (40, 44, 40)); d = ImageDraw.Draw(img)
        P = lambda x, z: ((x - self.x0) * scale, (z - self.z0) * scale)
        d.polygon([P(*p) for p in self.outline[::4]], fill=(120, 130, 100))
        for op in self.ops:
            if op["op"] == "Mesh" and op["mesh"] == "box" and op.get("height", 1) <= 0.12 and op.get("y", 0) < 0.05:
                w, dd = op.get("width", 1), op.get("depth", 1)
                if abs(op.get("yaw", 0)) % 180 == 90:
                    w, dd = dd, w
                c = tuple(int(v * 255) for v in op["color"])
                d.rectangle([P(op["x"] - w / 2, op["z"] - dd / 2), P(op["x"] + w / 2, op["z"] + dd / 2)], fill=c)
        for x0, z0, x1, z1, y0, y1, c in self.boxes:
            if y0 > 3.5:
                continue
            shade = tuple(int(v * 255 * (0.65 if y1 < 1.3 else 1.0)) for v in c)
            d.rectangle([P(x0, z0), P(x1, z1)], fill=shade, outline=(20, 20, 20))
        for name, x, z, yaw, y, tag in self.props:
            if y and y > 0.6:
                continue
            r = self.foot(name, x, z, yaw); p = self.P[name]
            fill = (200, 120, 50) if p["solid"] else (230, 230, 120)
            d.rectangle([P(r[0], r[1]), P(r[2], r[3])], fill=fill if p["solid"] else None, outline=fill)
        for it in self.items:
            d.ellipse([P(it["x"] - 1.5, it["z"] - 1.5), P(it["x"] + 1.5, it["z"] + 1.5)], outline=(40, 160, 40), width=2)
        for kind, x, z, r, c in marks:
            if kind == "circle":
                d.ellipse([P(x - r, z - r), P(x + r, z + r)], outline=c, width=3)
            elif kind == "dot":
                d.ellipse([P(x - r, z - r), P(x + r, z + r)], fill=c)
            elif kind == "line":
                d.line([P(x[0], x[1]), P(z[0], z[1])], fill=c, width=1)
        for gx in range(int(self.x0 // 10 * 10), int(self.x1), 10):
            d.text(P(gx, self.z0 + 1), str(gx), fill=(255, 255, 255))
        for gz in range(int(self.z0 // 10 * 10), int(self.z1), 10):
            d.text(P(self.x0 + 1, gz), str(gz), fill=(255, 255, 255))
        img.save(path)


# ===================================================================================== placement rules

def _along(a, b):
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    return L, ((b[0] - a[0]) / L, (b[1] - a[1]) / L)


def street_furniture(t, a, b, road_side, rng, lamp_every=17.0, lamp_kinds=("Street Lamp", "Flickering Street Lamp"), skip=(),
                     extras=None, trees=True, tree_h=(6.0, 8.5), kerb_off=0.55, back_off=2.2, tag="street"):
    """Furniture along a sidewalk from a to b. road_side is the unit vector from the sidewalk towards the road. Lamps sit at
    the kerb every lamp_every metres with the arm over the road; street trees halfway between lamps (at the kerb, in a
    tree pit); between them, on the building side of the sidewalk, the extras (bins next to benches, hydrants, meters,
    newspaper boxes, mailboxes). skip: (s0, s1) intervals along the line kept clear (doors, junctions, crossings)."""
    L, (ux, uz) = _along(a, b); nx, nz = road_side
    def at(s, off):
        return a[0] + ux * s + nx * off, a[1] + uz * s + nz * off
    def clear(s, w=1.2):
        return all(not (s0 - w < s < s1 + w) for s0, s1 in skip)
    lamp_yaw = yaw_along(nx, nz)           # the arm (+X) reaches over the road
    def first_clear(s, w, limit):
        """The first spot from s on (up to limit metres further) that's clear of the skip intervals."""
        k = 0.0
        while k <= limit:
            if clear(s + k, w):
                return s + k
            k += 0.5
        return None
    s = lamp_every * 0.4; k = 0; placed = 0; lamps = []
    while s < L - 1.0:
        sl = first_clear(s, 1.2, lamp_every * 0.45)
        if sl is not None and sl < L - 1.0:
            x, z = at(sl, -kerb_off)
            if t.prop(lamp_kinds[k % len(lamp_kinds)] if rng.random() < 0.75 else lamp_kinds[-1], x, z, lamp_yaw, tag="lamp"):
                placed += 1; lamps.append(sl)
            s = sl
        s += lamp_every; k += 1
    if trees:
        for i in range(len(lamps) + 1):
            lo = lamps[i - 1] if i > 0 else 0.0; hi = lamps[i] if i < len(lamps) else L
            if hi - lo < 6:
                continue
            st = first_clear((lo + hi) / 2 - 1.0, 1.6, 2.0)
            if st is None or st > L - 2:
                continue
            x, z = at(st, -kerb_off - 0.3)
            if t.tree(x, z, rng.uniform(*tree_h)):
                t.mesh("box", x, z, (0.33, 0.26, 0.18), y=0.0, width=1.3, height=0.12, depth=1.3, material="Dirt")  # tree pit
    # extras on the building side, between lamps
    extras = extras or [("Bench", "Trash Can"), ("Fire Hydrant",), ("Newspaper Box",), ("Mailbox",), ("Post Litter Bin",)]
    s = lamp_every * 0.25
    while s < L - 1.5:
        if clear(s, 1.6):
            group = extras[rng.randrange(len(extras))]
            x, z = at(s, -back_off)
            face = yaw_facing(nx, nz)        # benches and boxes face the road
            if group[0] == "Bench":
                if t.prop("Bench", x, z, face, tag=tag) and len(group) > 1:
                    bx, bz = at(s + 1.4, -back_off)
                    t.prop(group[1], bx, bz, face, tag=tag)
            elif group[0] == "Fire Hydrant":
                x, z = at(s, -kerb_off - 0.2)
                t.prop("Fire Hydrant", x, z, face, tag=tag)
            else:
                t.prop(group[0], x, z, face, tag=tag)
        s += lamp_every * rng.uniform(0.45, 0.8)
    return placed


def kerb_parking(t, a, b, road_side, rng, cars=("Sedan", "Hatchback", "SUV", "Van", "Taxi", "Pickup Truck", "Burnt Out Car", "Car On Bricks"),
                 lane_off=1.2, fill=0.75, skip=(), meters=True, tag="parked"):
    """Cars parked along the kerb (road_side points from the kerb into the road), nose to tail and aligned with the road.
    Gaps at the skip intervals (junctions, crossings, hydrants). A parking meter on the kerb beside some of them."""
    L, (ux, uz) = _along(a, b); nx, nz = road_side
    s = rng.uniform(1, 4); n = 0
    while s < L - 5:
        car = cars[rng.randrange(len(cars))]
        clen = t.P[car]["w"] + 0.9
        if not all(not (s0 < s + clen and s < s1) for s0, s1 in skip):
            s += 0.5
            continue
        if rng.random() < fill:
            x = a[0] + ux * (s + clen / 2) + nx * lane_off; z = a[1] + uz * (s + clen / 2) + nz * lane_off
            yaw = yaw_along(ux, uz) + (180 if rng.random() < 0.5 else 0) + rng.uniform(-3, 3)
            if t.prop(car, x, z, yaw, gap=0.15, tag=tag):
                n += 1
                if meters and rng.random() < 0.5:
                    mx = a[0] + ux * (s + clen / 2) - nx * 0.45; mz = a[1] + uz * (s + clen / 2) - nz * 0.45
                    t.prop("Parking Meter", mx, mz, yaw_facing(nx, nz), tag="meter")
        s += clen + rng.uniform(0.3, 2.5)
    return n


def car_park(t, x0, z0, x1, z1, rng, along_x=True, bay_w=2.7, bay_d=5.2, aisle=6.4, fill=0.65,
             cars=("Sedan", "Hatchback", "SUV", "Van", "Taxi", "Pickup Truck", "Police Car", "Burnt Out Car", "Overturned Car", "Car On Bricks"),
             line_color=(0.85, 0.85, 0.78), tag="carpark"):
    """A car park: double rows of bays (bay_d deep) with aisles between, white bay lines, cars square in their bays (a few
    turned in crooked, burnt out or overturned). Rows run along X when along_x."""
    n = 0
    W = (x1 - x0) if along_x else (z1 - z0); D = (z1 - z0) if along_x else (x1 - x0)
    def P(u, v):
        return (x0 + u, z0 + v) if along_x else (x0 + v, z0 + u)
    v = 0.0; rows = []
    while v + bay_d <= D + 0.01:
        rows.append(v); v += bay_d
        if v + bay_d <= D + 0.01:
            rows.append(v); v += bay_d
        v += aisle
    for ri, rv in enumerate(rows):
        facing = 1 if ri % 2 == 0 else -1
        nb = int(W // bay_w)
        for k in range(nb + 1):
            u = k * bay_w + (W - nb * bay_w) / 2
            cx, cz = P(u, rv + bay_d / 2)
            t.mesh("box", cx, cz, line_color, y=0.0, width=0.1 if along_x else bay_d, height=0.035, depth=bay_d if along_x else 0.1)
        for k in range(nb):
            if rng.random() > fill:
                continue
            u = k * bay_w + (W - nb * bay_w) / 2 + bay_w / 2
            cx, cz = P(u, rv + bay_d / 2)
            car = cars[rng.randrange(len(cars))]
            # cars sit in the bay with their length across the row (along v)
            vx, vz = (0, facing) if along_x else (facing, 0)
            yaw = yaw_along(vx, vz) + (rng.uniform(-12, 12) if rng.random() < 0.15 else rng.uniform(-2, 2))
            if t.prop(car, cx, cz, yaw, gap=0.1, tag=tag):
                n += 1
    return n


def bins_by_door(t, door, wall_dir, out_dir, rng, side=1, dumpster=True, seed=0, tag="bins"):
    """Bins at a back door: door is the door's centre on the wall line, wall_dir the unit vector along the wall, out_dir
    the unit vector out of the building into the alley. A dumpster against the wall beside the door (side = +1/-1 along
    the wall), wheelie bins next to it, trash bags heaped against it, and a Clutter op of litter round it."""
    dx, dz = door; wx, wz = wall_dir; ox, oz = out_dir
    placed = []
    off = 2.4 * side
    if dumpster:
        x = dx + wx * off + ox * 0.75; z = dz + wz * off + oz * 0.75
        if t.prop("Dumpster", x, z, yaw_facing(ox, oz) + rng.uniform(-4, 4), tag=tag):
            placed.append((x, z))
            off += 1.75 * side
    for k in range(rng.randint(1, 3)):
        name = rng.choice(["Wheelie Bin", "Wheelie Bin Grey", "Recycling Bin", "Trash Can"])
        x = dx + wx * off + ox * 0.5; z = dz + wz * off + oz * 0.5
        if t.prop(name, x, z, yaw_facing(ox, oz) + rng.uniform(-10, 10), tag=tag):
            placed.append((x, z))
        off += 0.85 * side
    # bags heaped in front of the bins (cosmetic), and litter spread round
    cx = dx + wx * (2.4 * side + off) / 2 + ox * 1.6; cz = dz + wz * (2.4 * side + off) / 2 + oz * 1.6
    for k in range(rng.randint(3, 6)):
        bx = cx + wx * rng.uniform(-1.6, 1.6) + ox * rng.uniform(-0.5, 0.6); bz = cz + wz * rng.uniform(-1.6, 1.6) + oz * rng.uniform(-0.5, 0.6)
        t.prop("Trash Bag", bx, bz, rng.uniform(0, 360), tag="trash")
    t.clutter(["Litter", "Litter", "Trash Bag", "Plastic Bag", "Soda Cans", "Bottles", "Flattened Cardboard", "Open Cardboard Box", "Newspaper"],
              cx - 3.2, cz - 3.2, cx + 3.2, cz + 3.2, rng.randint(9, 15), seed)
    return placed


def roof_kit(t, x0, z0, x1, z1, y, rng, n=None, kinds=None, edge=0.8, tag="roof"):
    """Rooftop dressing on a flat roof (top at height y): AC condensers, HVAC units, turbine vents, mushroom vents, a
    satellite dish near an edge, a chimney or flue, a hatch. Kept edge metres from the roof edge and from each other."""
    kinds = kinds or [("Rooftop Condenser", 3), ("AC Condenser Fan", 2), ("Roof Turbine Vent", 3), ("Turbine Vent", 2), ("Mushroom Vent", 2),
                      ("Rooftop HVAC Unit", 1), ("Sweeping Satellite Dish", 1), ("Satellite Dish", 2), ("Exhaust Fan", 1), ("Roof Hatch", 1),
                      ("Skylight", 1), ("Brick Chimney", 1), ("TV Antenna", 1), ("Air Duct", 1), ("Plastic Water Tank", 1), ("Split AC Unit", 1)]
    pool = [k for k, w in kinds for _ in range(w)]
    area = (x1 - x0) * (z1 - z0)
    n = n if n is not None else max(2, int(area / 22))
    taken = []; placed = 0
    for _ in range(n * 8):
        if placed >= n:
            break
        name = rng.choice(pool)
        yaw = rng.choice([0, 90, 180, 270]) + (rng.uniform(-8, 8) if "Dish" in name else 0)
        x = rng.uniform(x0 + edge, x1 - edge); z = rng.uniform(z0 + edge, z1 - edge)
        r = t.foot(name, x, z, yaw)
        if r[0] < x0 + edge * 0.6 or r[2] > x1 - edge * 0.6 or r[1] < z0 + edge * 0.6 or r[3] > z1 - edge * 0.6:
            continue
        if any(r[0] - 0.5 < q[2] and q[0] < r[2] + 0.5 and r[1] - 0.5 < q[3] and q[1] < r[3] + 0.5 for q in taken):
            continue
        if t.prop(name, x, z, yaw, y=y, check=False, tag=tag):
            taken.append(r); placed += 1
    return placed


def wall_kit(t, a, b, out, rng, ys=(2.6, 3.3), every=5.0, kinds=(("Window AC Unit", 3), ("Vent Grille", 2), ("Electric Meter", 1), ("Split AC Unit", 1)),
             skip=(), tag="wall"):
    """Things fixed to a facade from a to b (out = unit vector out of the wall): window AC units and vent grilles up the
    wall, electric meters at chest height, split AC units on brackets. skip intervals keep doors and windows clear."""
    L, (ux, uz) = _along(a, b); ox, oz = out
    pool = [k for k, w in kinds for _ in range(w)]
    s = rng.uniform(1.0, every); n = 0
    while s < L - 0.8:
        if all(not (s0 - 0.8 < s < s1 + 0.8) for s0, s1 in skip):
            name = rng.choice(pool)
            y = 1.0 if name == "Electric Meter" else rng.uniform(*ys)
            push = 0.02 if name != "Window AC Unit" else 0.3
            x = a[0] + ux * s + ox * push; z = a[1] + uz * s + oz * push
            if t.prop(name, x, z, yaw_facing(ox, oz), y=y, check=False, tag=tag):
                n += 1
        s += every * rng.uniform(0.6, 1.3)
    return n


# ---------------------------------------------------------------- interiors

class Room:
    """A room's local frame: u runs along the front wall (0..W), v from the front wall into the room (0..D). front is
    the outward unit vector of the front wall, origin the inside corner at u = 0, v = 0."""
    def __init__(self, t, x0, z0, x1, z1, front, wall=0.3):
        self.t = t
        fx, fz = front
        self.front = front
        # inner rectangle
        ix0, iz0, ix1, iz1 = x0 + wall, z0 + wall, x1 - wall, z1 - wall
        self.rect = (ix0, iz0, ix1, iz1)
        if front == (0, 1):    # front wall at z1, room goes -z
            self.o = (ix1, iz1); self.u = (-1, 0); self.v = (0, -1); self.W = ix1 - ix0; self.D = iz1 - iz0
        elif front == (0, -1):
            self.o = (ix0, iz0); self.u = (1, 0); self.v = (0, 1); self.W = ix1 - ix0; self.D = iz1 - iz0
        elif front == (1, 0):
            self.o = (ix1, iz0); self.u = (0, 1); self.v = (-1, 0); self.W = iz1 - iz0; self.D = ix1 - ix0
        else:
            self.o = (ix0, iz1); self.u = (0, -1); self.v = (1, 0); self.W = iz1 - iz0; self.D = ix1 - ix0

    def xz(self, u, v):
        return self.o[0] + self.u[0] * u + self.v[0] * v, self.o[1] + self.u[1] * u + self.v[1] * v

    def yaw(self, facing):
        """facing: 'front' (towards the front wall), 'back', 'left' (towards u = 0), 'right'."""
        d = {"front": (-self.v[0], -self.v[1]), "back": self.v, "left": (-self.u[0], -self.u[1]), "right": self.u}[facing]
        return yaw_facing(*d)

    def put(self, name, u, v, facing="front", y=None, jitter=0.0, rng=None, tag="interior"):
        x, z = self.xz(u, v)
        yaw = self.yaw(facing) + ((rng.uniform(-jitter, jitter)) if (rng and jitter) else 0)
        return self.t.prop(name, x, z, yaw, y=y, check=y is None or y < 0.6, gap=0.04, margin=0.3, tag=tag)

    def depth_of(self, name):
        p = self.t.P[name]
        return p["d"]


def furnish(t, room, kind, rng, door_u=None, back_door_u=None):
    """Furniture for a room of a given kind, against the walls and facing in, keeping a 1.3 m aisle from the front door
    (at u = door_u) to the back of the room and to the back door. Returns the number of props placed."""
    W, D = room.W, room.D
    n0 = len(t.props)
    # keep the aisles clear while furnishing
    aisles = []
    def reserve(u0, v0, u1, v1):
        a = room.xz(u0, v0); b = room.xz(u1, v1)
        r = (min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1]))
        sub = t.occ[t._ij(*r)[0]:t._ij(*r)[1], t._ij(*r)[2]:t._ij(*r)[3]]
        aisles.append((r, sub.copy())); t.mark(t.occ, r, 2)
    if door_u is not None:
        reserve(door_u - 0.75, 0, door_u + 0.75, D)
    if back_door_u is not None:
        reserve(back_door_u - 0.75, max(0, D - 2.0), back_door_u + 0.75, D)
        if door_u is not None:
            lo, hi = min(door_u, back_door_u), max(door_u, back_door_u)
            reserve(lo - 0.6, D * 0.45, hi + 0.6, D * 0.45 + 1.3)
    def put(*a, **k):
        return room.put(*a, rng=rng, **k)
    def on_top(name, u, v, y, facing="front"):
        return put(name, u, v, facing, y=y)

    def along_wall(names, v_wall, facing, u0, u1, gap=0.05):
        """Line up props along a wall parallel to the front at depth v_wall (their backs to that wall)."""
        u = u0
        for nm in names:
            w = t.P[nm]["w"]
            if u + w > u1:
                break
            if put(nm, u + w / 2, v_wall, facing):
                pass
            u += w + gap
        return u

    def side_wall(names, left, v0, v1, gap=0.05):
        """Props along a side wall (u = 0 if left else W), backs to it, facing into the room."""
        v = v0
        for nm in names:
            p = t.P[nm]; w = p["w"]; dpt = p["d"]
            if v + w > v1:
                break
            u = dpt / 2 + 0.05 if left else W - dpt / 2 - 0.05
            put(nm, u, v + w / 2, "right" if left else "left")
            v += w + gap
        return v

    if kind == "diner":
        # counter with stools along the back, kitchen behind it; booths of tables and chairs by the windows; a jukebox TV
        cv = D - 2.2
        u = 1.0
        while u + 1.3 < W - 1.0:
            put("Kitchen Counter", u + 0.6, cv, "front")
            put("Bar Stool", u + 0.6, cv - 0.9, "back")
            u += 1.3
        along_wall(["Fridge", "Cooker", "Cooker", "Kitchen Sink", "Fridge", "Chest Freezer"], D - 0.45, "front", 0.6, W - 0.6, gap=0.1)
        for k in range(int((W - 1.5) // 3.2)):
            tu = 1.6 + k * 3.2
            if door_u is not None and abs(tu - door_u) < 1.9:
                continue
            put("Dining Table", tu, 1.6, "front")
            put("Dining Chair", tu - 0.65, 1.6, "right"); put("Dining Chair", tu + 0.65, 1.6, "left")
            on_top("Stacked Plates", tu, 1.6, 0.76)
        put("Flickering TV", 0.6, cv - 2.0, "right")
        put("Glowing Vending Machine", W - 0.55, 3.6, "left")
        for k in range(2):
            on_top("Ceiling Fan", W * (k + 1) / 3, D * 0.45, 3.4)
    elif kind == "laundromat":
        # washers and dryers along both side walls, a bench down the middle, a folding table, a change machine
        side_wall(["Running Washer", "Running Washer", "Washing Machine", "Running Washer", "Running Washer", "Washing Machine", "Running Washer", "Running Washer"], True, 1.2, D - 0.4)
        side_wall(["Tumble Dryer", "Tumble Dryer", "Tumble Dryer", "Tumble Dryer", "Tumble Dryer", "Tumble Dryer", "Tumble Dryer"], False, 1.2, D - 0.4)
        put("Bench", W / 2 - 0.9 if door_u and door_u > W / 2 else W / 2 + 0.9, D * 0.4, "front")
        put("Table", W / 2, D - 1.0, "front")
        on_top("Laundry Basket", W / 2, D - 1.0, 0.78)
        put("Laundry Basket", W / 2 + 1.1, D - 1.8, "front"); put("Ironing Board", W / 2 - 1.4, D - 1.9, "front")
        put("Vending Machine", W / 2 + 1.6, D - 0.5, "front")
        on_top("Ceiling Fan", W / 2, D / 2, 3.4)
    elif kind == "barber":
        # chairs facing mirrors on one wall, sinks between, waiting armchairs and a coffee table by the window
        for k in range(int((D - 3.0) // 1.6)):
            v = 2.4 + k * 1.6
            put("Standing Mirror", 0.3, v, "right")
            put("Office Chair", 1.3, v, "left")
            if k % 2 == 0:
                put("Bathroom Sink", 0.35, v + 0.8, "right")
        put("Armchair", W - 0.6, 1.4, "left"); put("Armchair", W - 0.6, 2.5, "left")
        put("Coffee Table", W - 1.7, 1.9, "left"); on_top("Book Pile", W - 1.7, 1.9, 0.45)
        put("Coat Stand", W - 0.5, 3.5, "left"); put("Floor Lamp", W - 0.4, 0.5, "left")
        put("Reception Desk", W - 1.8, D - 1.0, "front")
        put("Flickering TV", W - 0.5, D - 2.6, "left")
        put("Pedestal Fan", 0.5, 1.0, "back")
    elif kind == "arcade":
        # rows of cabinets back to back down the middle, slots along a side wall, a change machine and a vending machine
        side_wall(["Slot Machine"] * 12, False, 1.0, D - 0.6, gap=0.1)
        side_wall(["Arcade Cabinet"] * 10, True, 1.4, D - 0.6, gap=0.08)
        mid = W / 2 + (1.0 if door_u is not None and door_u < W / 2 else -1.0)
        v = 2.0
        while v < D - 2.0:
            put("Arcade Cabinet", mid - 0.42, v, "left"); put("Arcade Cabinet", mid + 0.42, v, "right")
            v += 0.85
        put("Glowing Vending Machine", W / 2, D - 0.5, "front")
        put("Ticket Machine", W / 2 + 1.4, D - 0.4, "front")
    elif kind == "store":
        # shelving aisles running front to back, fridges and freezers along the back wall, a counter by the door
        along_wall(["Fridge", "Fridge", "Fridge", "Chest Freezer", "Chest Freezer", "Fridge", "Fridge"], D - 0.45, "front", 0.4, W - 0.4, gap=0.05)
        u = 2.6
        while u < W - 1.6:
            if door_u is None or abs(u - door_u) > 1.6:
                v = 2.6
                while v < D - 2.2:
                    put("Metal Shelving", u, v, "left" if (int(u) % 2) else "right")
                    v += 1.25
            u += 2.2
        put("Reception Desk", 1.4, 1.2, "back") if door_u is None or door_u > 3.4 else put("Reception Desk", W - 1.4, 1.2, "back")
        put("ATM", W - 0.5, 1.2 + 2.0, "left")
        put("Produce Crates", 0.6, D - 2.0, "right"); put("Potato Sacks", 0.6, D - 3.2, "right")
        put("Archive Boxes", W - 0.7, D - 2.2, "left")
    elif kind == "office":
        # desks with chairs and monitors in pairs, filing cabinets and shelving along the walls, a copier and a cooler
        for row in range(int((D - 3.0) // 2.6)):
            v = 2.2 + row * 2.6
            for cu in (W * 0.3, W * 0.72):
                if door_u is not None and abs(cu - door_u) < 1.6:
                    continue
                if put("Office Desk", cu, v, "front"):
                    put("Office Chair", cu, v + 0.8, "front")
                    on_top("Computer Monitor", cu - 0.25, v + 0.05, 0.76)
                    if rng.random() < 0.5:
                        on_top("Desk Phone", cu + 0.45, v, 0.76)
        side_wall(["Filing Cabinet", "Filing Cabinet", "Metal Shelving", "Filing Cabinet", "Water Cooler", "Photocopier"], True, 1.0, D - 0.4, gap=0.1)
        side_wall(["Office Plant", "Whiteboard", "Metal Lockers", "Archive Boxes", "Server Rack"], False, 1.0, D - 0.4, gap=0.2)
    elif kind == "kitchen":
        along_wall(["Fridge", "Kitchen Counter", "Cooker", "Kitchen Sink", "Kitchen Counter", "Dishwasher"], D - 0.4, "front", 0.4, W - 0.4, gap=0.02)
        on_top("Microwave", 1.85, D - 0.35, 0.9); on_top("Kettle", 3.2, D - 0.35, 0.9)
        put("Dining Table", W / 2, D / 2 - 0.4, "front")
        put("Dining Chair", W / 2 - 0.65, D / 2 - 0.4, "right"); put("Dining Chair", W / 2 + 0.65, D / 2 - 0.4, "left")
        put("Dining Chair", W / 2, D / 2 - 1.1, "back")
        put("Pedal Bin", 0.3, D - 1.3, "right")
    elif kind == "living":
        put("Fabric Sofa", W / 2, D - 0.55, "front")
        put("Coffee Table", W / 2, D - 1.9, "front"); on_top("Book Pile", W / 2 + 0.2, D - 1.9, 0.45)
        put("TV Stand", W / 2, 0.45, "back")
        put("Rug", W / 2, D - 1.9, "front")
        put("Armchair", W - 0.7, D - 2.0, "left"); put("Floor Lamp", W - 0.4, D - 0.4, "left")
        put("Bookshelf", 0.25, D / 2, "right"); put("Upright Piano", 0.4, D / 2 + 1.6, "right")
    elif kind == "hall":
        # town hall: rows of chairs facing a stage table, flags, a grandfather clock, a piano
        put("Conference Table", W / 2, D - 1.6, "front")
        put("Flag on a Pole", W / 2 - 2.6, D - 0.6, "front"); put("Flag on a Pole", W / 2 + 2.6, D - 0.6, "front")
        for row in range(4):
            v = D - 4.2 - row * 1.2
            for k in range(-3, 4):
                cu = W / 2 + k * 0.75 + (0.6 if k >= 0 else -0.6)
                if v > 3.0:
                    put("Plastic Chair", cu, v, "back")
        put("Grandfather Clock", 0.4, D - 1.2, "right"); put("Upright Piano", W - 0.4, D - 1.6, "left")
        put("Reception Desk", W - 1.6, 1.6, "back"); put("Bookshelf", 0.25, 2.2, "right"); put("Bookshelf", 0.25, 3.2, "right")
    for r, sub in aisles:
        i0, i1, j0, j1 = t._ij(*r)
        cur = t.occ[i0:i1, j0:j1]
        cur[cur == 2] = 0
    return len(t.props) - n0


def duck_pond(t, cx, cz, rx, rz, rng, water=(0.26, 0.42, 0.45), tag="pond"):
    """A pond: a water disc (a 20 cm mesh, so nothing grows in it), a muddy bank, ducks on the water and on the bank, a
    family of ducklings, reeds (shrub and grass regions round the edge) and a bench facing it from the path side."""
    t.mesh("cylinder", cx, cz, (0.36, 0.30, 0.2), width=rx * 2 + 1.6, height=0.06, depth=rz * 2 + 1.6, segments=32, material="Dirt")
    t.mesh("cylinder", cx, cz, water, y=0.0, width=rx * 2, height=0.2, depth=rz * 2, segments=32)
    t.exclude.append({"shape": "ellipse", "center": [R(cx), R(cz)], "radiusX": R(rx - 0.3), "radiusZ": R(rz - 0.3)})
    n = 0
    for k in range(rng.randint(5, 8)):
        a = rng.uniform(0, math.tau); f = rng.uniform(0.2, 0.75)
        if t.prop("Duck on a Pond", cx + math.cos(a) * rx * f, cz + math.sin(a) * rz * f, rng.uniform(0, 360), y=0.2, check=False, tag=tag):
            n += 1
    for name in ["Mallard Duck", "Hen Duck", "Duck Family", "Mallard Duck", "Hen Duck", "Duck Decoy"]:
        a = rng.uniform(0, math.tau)
        x = cx + math.cos(a) * (rx + 0.6); z = cz + math.sin(a) * (rz + 0.6)
        if t.prop(name, x, z, yaw_facing(cx - x, cz - z) + rng.uniform(-60, 60), tag=tag):
            n += 1
    ring = [(cx + math.cos(a) * (rx + 1.4), cz + math.sin(a) * (rz + 1.4)) for a in [i * math.tau / 16 for i in range(16)]]
    inner = [(cx + math.cos(a) * (rx - 0.2), cz + math.sin(a) * (rz - 0.2)) for a in [i * math.tau / 16 for i in range(16)]]
    poly = [[R(x), R(z)] for x, z in ring] + [[R(x), R(z)] for x, z in reversed(inner)] + [[R(ring[0][0]), R(ring[0][1])]]
    t.region("grass", 9.0, polygon=[[R(x), R(z)] for x, z in ring], scale=[1.3, 2.1], color=[0.42, 0.52, 0.26], patchiness=0.5)
    t.region("shrub", 1.6, polygon=[[R(x), R(z)] for x, z in ring], color=[0.34, 0.46, 0.2])
    return n


def hedge_strip(t, a, b, width=1.2, density=2.2, height=(1.1, 1.6), color=(0.22, 0.36, 0.16)):
    """A hedge (dense bushes) along a line: a thin polygon of bush foliage. Not solid (foliage never is)."""
    L, (ux, uz) = _along(a, b); nx, nz = -uz, ux; w = width / 2
    poly = [[R(a[0] + nx * w), R(a[1] + nz * w)], [R(b[0] + nx * w), R(b[1] + nz * w)], [R(b[0] - nx * w), R(b[1] - nz * w)], [R(a[0] - nx * w), R(a[1] - nz * w)]]
    t.region("bush", density, polygon=poly, scale=list(height), color=list(color), patchiness=0.05)


def weeds_along(t, a, b, width=0.8, density=4.0):
    """Weeds and tufts at the foot of a wall or kerb (a strip of grass and shrubs)."""
    L, (ux, uz) = _along(a, b); nx, nz = -uz, ux; w = width / 2
    poly = [[R(a[0] + nx * w), R(a[1] + nz * w)], [R(b[0] + nx * w), R(b[1] + nz * w)], [R(b[0] - nx * w), R(b[1] - nz * w)], [R(a[0] - nx * w), R(a[1] - nz * w)]]
    t.region("grass", density, polygon=poly, patchiness=0.6, scale=[0.7, 1.3], color=[0.45, 0.5, 0.25])
    t.region("shrub", density * 0.12, polygon=poly, patchiness=0.6)


def tree_row(t, a, b, spacing, rng, h=(6.0, 9.0), kind="tree", jitter=0.6):
    L, (ux, uz) = _along(a, b); s = spacing / 2; n = 0
    while s < L:
        x = a[0] + ux * s + rng.uniform(-jitter, jitter); z = a[1] + uz * s + rng.uniform(-jitter, jitter)
        if t.tree(x, z, rng.uniform(*h), kind):
            n += 1
        s += spacing * rng.uniform(0.85, 1.15)
    return n


def edge_run(t, s0, s1, style, rng, inset=0.9, spacing=None, color=(0.22, 0.36, 0.16)):
    """Dresses the outline between arc lengths s0 and s1 (metres along the game's outline): 'fence' lines chain-link or
    wooden fence panels just inside it, turned along it; 'hedge' puts a tall leafy hedge just outside it (cosmetic
    meshes) and bushes along the inside; 'backs' puts cosmetic building backs (brick walls with windows) just outside."""
    pts = t.outline; n = len(pts)
    arc = [0.0]
    for i in range(1, n + 1):
        arc.append(arc[-1] + math.hypot(pts[i % n][0] - pts[i - 1][0], pts[i % n][1] - pts[i - 1][1]))
    total = arc[-1]
    def at(s):
        s %= total
        lo, hi = 0, n
        while hi - lo > 1:
            m = (lo + hi) // 2
            if arc[m] <= s:
                lo = m
            else:
                hi = m
        a = pts[lo]; b = pts[(lo + 1) % n]; f = (s - arc[lo]) / max(1e-6, arc[lo + 1] - arc[lo])
        dx, dz = b[0] - a[0], b[1] - a[1]; L = math.hypot(dx, dz)
        ux, uz = dx / L, dz / L
        # CCW outline: the inward normal is to the left of the direction of travel
        return a[0] + dx * f, a[1] + dz * f, ux, uz, -uz, ux
    if s1 < s0:
        s1 += total
    count = 0
    if style in ("fence", "picket", "panel"):
        name = {"fence": "Chain Link Fence", "picket": "Picket Fence", "panel": "Fence Panel"}[style]
        L = t.P[name]["w"]; sp = spacing or L * 0.98
        s = s0
        while s + sp <= s1:
            x, z, ux, uz, ix, iz = at(s + sp / 2)
            x += ix * inset; z += iz * inset
            if t.prop(name, x, z, yaw_along(ux, uz), check=True, gap=-0.05, margin=-2.5, tag="edge"):
                count += 1
            s += sp
    elif style == "hedge":
        s = s0
        while s < s1:
            seg = min(4.0, s1 - s)
            x, z, ux, uz, ix, iz = at(s + seg / 2)
            h = rng.uniform(2.4, 3.2)
            t.mesh("box", x - ix * 0.9, z - iz * 0.9, color, width=seg + 0.6, height=h, depth=1.6, yaw=yaw_along(ux, uz), material="Dirt")
            s += seg; count += 1
        # bushes along the inside of the hedge
        s = s0
        while s < s1:
            seg = min(10.0, s1 - s)
            xa, za, ux, uz, ix, iz = at(s); xb, zb, _, _, ix2, iz2 = at(s + seg)
            poly = [[R(xa + ix * 0.4), R(za + iz * 0.4)], [R(xb + ix2 * 0.4), R(zb + iz2 * 0.4)], [R(xb + ix2 * 1.6), R(zb + iz2 * 1.6)], [R(xa + ix * 1.6), R(za + iz * 1.6)]]
            t.region("bush", 1.4, polygon=poly, scale=[1.0, 1.6], color=list(color), patchiness=0.15)
            s += seg
    elif style == "backs":
        s = s0
        while s < s1:
            seg = min(rng.uniform(7, 12), s1 - s)
            x, z, ux, uz, ix, iz = at(s + seg / 2)
            h = rng.uniform(5.5, 9.5); c = rng.choice([(0.55, 0.36, 0.28), (0.62, 0.52, 0.42), (0.48, 0.42, 0.38), (0.66, 0.60, 0.50)])
            yaw = yaw_along(ux, uz)
            t.mesh("box", x - ix * 3.2, z - iz * 3.2, c, width=seg + 0.4, height=h, depth=5.6, yaw=yaw, material="Brick")
            for k in range(int(seg // 2.6)):
                wu = -seg / 2 + 1.3 + k * 2.6
                for fl in (1.2, 4.2) if h > 7 else (1.2,):
                    t.mesh("box", x + ux * wu - ix * 0.38, z + uz * wu - iz * 0.38, (0.12, 0.14, 0.16), y=fl, width=1.1, height=1.4, depth=0.06, yaw=yaw)
            s += seg; count += 1
    return count, total
