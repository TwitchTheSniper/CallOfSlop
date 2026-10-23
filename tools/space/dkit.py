"""Shared helpers for the Space pack's Frostvault and Vesper Strip generators (tools/space/frostvault.py and
tools/space/vesperstrip.py): an op list with counters, symmetric placement (180-degree rotation about the centre, or
a mirror about x = 0), prop footprints read from the prop files, a simple 2D occupancy check, polygon utilities and a
top-down plan drawing.

Import it from a generator in this folder:  sys.path.insert(0, HERE); import dkit
"""
import glob
import json
import math
import os


def r(v, n=3):
    return round(float(v), n)


class Kit:
    """Collects ops and keeps counts. `sym` is "rot" (twin at (-x, -z), yaw + 180) or "mirx" (twin at (-x, z), -yaw)
    or None."""

    def __init__(self, repo, sym="rot"):
        self.repo = repo
        self.sym = sym
        self.ops = []
        self.stats = {"boxes": 0, "colliders": 0, "meshes": 0, "props": 0, "decals": 0, "waypoints": 0, "spawns": 0}
        self.boxes = []  # (x0, z0, x1, z1, y0, y1) solid axis-aligned boxes for the plan and occupancy
        self.foot = []  # (x0, z0, x1, z1) footprints of solid props
        self.spawns = {0: [], 1: []}
        self.props_lib = load_props(repo)

    # ------------------------------------------------------------------ raw ops
    def op(self, d):
        self.ops.append(d)

    def twin(self, x, z, yaw=0.0):
        if self.sym == "rot":
            return -x, -z, (yaw + 180) % 360
        if self.sym == "mirx":
            return -x, z, (-yaw) % 360
        return None

    def block(self, x0, y0, z0, x1, y1, z1, color, sym=True):
        x0, x1 = min(x0, x1), max(x0, x1)
        z0, z1 = min(z0, z1), max(z0, z1)
        y0, y1 = min(y0, y1), max(y0, y1)
        if x1 - x0 < 0.01 or z1 - z0 < 0.01 or y1 - y0 < 0.01:
            return
        self.op({"op": "Block", "x0": r(x0), "y0": r(y0), "z0": r(z0), "x1": r(x1), "y1": r(y1), "z1": r(z1), "color": color})
        self.stats["boxes"] += 1
        self.boxes.append((x0, z0, x1, z1, y0, y1))
        if sym and self.sym:
            if self.sym == "rot":
                self.block(-x1, y0, -z1, -x0, y1, -z0, color, sym=False)
            else:
                self.block(-x1, y0, z0, -x0, y1, z1, color, sym=False)

    def collider(self, x0, y0, z0, x1, y1, z1, sym=True):
        x0, x1 = min(x0, x1), max(x0, x1)
        z0, z1 = min(z0, z1), max(z0, z1)
        y0, y1 = min(y0, y1), max(y0, y1)
        self.op({"op": "Collider", "x0": r(x0), "y0": r(y0), "z0": r(z0), "x1": r(x1), "y1": r(y1), "z1": r(z1)})
        self.stats["colliders"] += 1
        self.boxes.append((x0, z0, x1, z1, y0, y1))
        if sym and self.sym:
            if self.sym == "rot":
                self.collider(-x1, y0, -z1, -x0, y1, -z0, sym=False)
            else:
                self.collider(-x1, y0, z0, -x0, y1, z1, sym=False)

    def wall_x(self, x0, x1, z, t, y0, y1, color, holes=(), sym=True):
        """A wall along X from x0 to x1 filling z..z+t. holes: (centre, width, bottom, top)."""
        d = {"op": "WallX", "x0": r(x0), "x1": r(x1), "z": r(z), "t": r(t), "y0": r(y0), "y1": r(y1), "color": color}
        if holes:
            d["holes"] = [{"center": r(c), "width": r(w), "bottom": r(b), "top": r(tp)} for (c, w, b, tp) in holes]
        self.op(d)
        self.stats["boxes"] += 1 + 2 * len(holes)
        self.boxes.append((min(x0, x1), z, max(x0, x1), z + t, y0, y1))
        if sym and self.sym:
            if self.sym == "rot":
                self.wall_x(-x1, -x0, -z - t, t, y0, y1, color, [(-c, w, b, tp) for (c, w, b, tp) in holes], sym=False)
            else:
                self.wall_x(-x1, -x0, z, t, y0, y1, color, [(-c, w, b, tp) for (c, w, b, tp) in holes], sym=False)

    def wall_z(self, z0, z1, x, t, y0, y1, color, holes=(), sym=True):
        d = {"op": "WallZ", "z0": r(z0), "z1": r(z1), "x": r(x), "t": r(t), "y0": r(y0), "y1": r(y1), "color": color}
        if holes:
            d["holes"] = [{"center": r(c), "width": r(w), "bottom": r(b), "top": r(tp)} for (c, w, b, tp) in holes]
        self.op(d)
        self.stats["boxes"] += 1 + 2 * len(holes)
        self.boxes.append((x, min(z0, z1), x + t, max(z0, z1), y0, y1))
        if sym and self.sym:
            if self.sym == "rot":
                self.wall_z(-z1, -z0, -x - t, t, y0, y1, color, [(-c, w, b, tp) for (c, w, b, tp) in holes], sym=False)
            else:
                self.wall_z(z0, z1, -x - t, t, y0, y1, color, holes, sym=False)

    def slab(self, x0, z0, x1, z1, ytop, thick, color, hole=None, sym=True):
        d = {"op": "Slab", "x0": r(min(x0, x1)), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "z1": r(max(z0, z1)), "yTop": r(ytop), "thick": r(thick), "color": color}
        if hole:
            hx0, hz0, hx1, hz1 = hole
            d["hole"] = {"x0": r(min(hx0, hx1)), "z0": r(min(hz0, hz1)), "x1": r(max(hx0, hx1)), "z1": r(max(hz0, hz1))}
        self.op(d)
        self.stats["boxes"] += 4 if hole else 1
        self.boxes.append((min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1), ytop - thick, ytop))
        if sym and self.sym:
            if self.sym == "rot":
                h2 = (-hole[0], -hole[1], -hole[2], -hole[3]) if hole else None
                self.slab(-x1, -z1, -x0, -z0, ytop, thick, color, h2, sym=False)
            else:
                h2 = (-hole[0], hole[1], -hole[2], hole[3]) if hole else None
                self.slab(-x1, z0, -x0, z1, ytop, thick, color, h2, sym=False)

    def stairs_z(self, x0, x1, z, direction, y0, height, color, sym=True):
        """Solid stairs along Z climbing `height` from z towards +Z (dir 1) or -Z (dir -1). Returns the far end z."""
        n = math.ceil(height / 0.4 - 1e-6)
        self.op({"op": "StairsZ", "x0": r(x0), "x1": r(x1), "z": r(z), "dir": direction, "y0": r(y0), "height": r(height), "color": color})
        self.stats["boxes"] += n
        zend = z + direction * n * 0.45
        self.boxes.append((min(x0, x1), min(z, zend), max(x0, x1), max(z, zend), y0, y0 + height))
        if sym and self.sym:
            if self.sym == "rot":
                self.stairs_z(-x1, -x0, -z, -direction, y0, height, color, sym=False)
            else:
                self.stairs_z(-x1, -x0, z, direction, y0, height, color, sym=False)
        return zend

    def stairs_x(self, z0, z1, x, direction, y0, height, color, sym=True):
        n = math.ceil(height / 0.4 - 1e-6)
        self.op({"op": "StairsX", "z0": r(z0), "z1": r(z1), "x": r(x), "dir": direction, "y0": r(y0), "height": r(height), "color": color})
        self.stats["boxes"] += n
        xend = x + direction * n * 0.45
        self.boxes.append((min(x, xend), min(z0, z1), max(x, xend), max(z0, z1), y0, y0 + height))
        if sym and self.sym:
            if self.sym == "rot":
                self.stairs_x(-z1, -z0, -x, -direction, y0, height, color, sym=False)
            else:
                self.stairs_x(z0, z1, -x, -direction, y0, height, color, sym=False)
        return xend

    def mesh(self, kind, x, z, y, color, sym=True, **kw):
        d = {"op": "Mesh", "mesh": kind, "x": r(x), "z": r(z), "y": r(y), "color": color}
        for k, v in kw.items():
            if v is None:
                continue
            d[k] = r(v) if isinstance(v, float) else v
        self.op(d)
        self.stats["meshes"] += 1
        if sym and self.sym:
            kw2 = dict(kw)
            if self.sym == "rot":
                kw2["yaw"] = (kw.get("yaw", 0) + 180) % 360
                self.mesh(kind, -x, -z, y, color, sym=False, **kw2)
            else:
                kw2["yaw"] = (-kw.get("yaw", 0)) % 360
                if "roll" in kw2:
                    kw2["roll"] = -kw2["roll"]
                self.mesh(kind, -x, z, y, color, sym=False, **kw2)

    def prop(self, name, x, z, yaw=0.0, y=0.0, sym=True, **kw):
        d = {"op": "Prop", "name": name, "x": r(x), "z": r(z)}
        if y:
            d["y"] = r(y)
        if yaw % 360:
            d["yaw"] = r(yaw % 360, 1)
        d.update(kw)
        self.op(d)
        self.stats["props"] += 1
        info = self.props_lib.get(name.replace("global:", ""))
        if info and info["solid"]:
            self.foot.append(footprint(info, x, z, yaw))
        if sym and self.sym:
            tx, tz, tyaw = self.twin(x, z, yaw)
            self.prop(name, tx, tz, tyaw, y, sym=False, **kw)

    def decal(self, name, x, z, y=0.0, onto="floor", sym=True, **kw):
        d = {"op": "Decal", "decal": name, "x": r(x), "z": r(z), "y": r(y), "onto": onto}
        d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
        self.op(d)
        self.stats["decals"] += 1
        if sym and self.sym:
            tx, tz, _ = self.twin(x, z)
            kw2 = dict(kw)
            if self.sym == "rot":
                if "rotation" in kw2 and kw2["rotation"] >= 0:
                    kw2["rotation"] = (kw2["rotation"] + 180) % 360
                onto2 = {"wall+x": "wall-x", "wall-x": "wall+x", "wall+z": "wall-z", "wall-z": "wall+z"}.get(onto, onto)
            else:
                if "rotation" in kw2 and kw2["rotation"] >= 0 and onto == "floor":
                    kw2["rotation"] = (-kw2["rotation"]) % 360
                onto2 = {"wall+x": "wall-x", "wall-x": "wall+x"}.get(onto, onto)
            self.decal(name, tx, tz, y, onto2, sym=False, **kw2)

    def decal_scatter(self, decals, x0, z0, x1, z1, count, seed, onto="floor", y=0.0, **kw):
        d = {"op": "DecalScatter", "decals": decals, "x0": r(min(x0, x1)), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "z1": r(max(z0, z1)),
             "count": int(count), "seed": int(seed), "onto": onto, "y": r(y)}
        d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
        self.op(d)
        self.stats["decals"] += int(count)

    def waypoint(self, x, z, y=0.0, sym=True):
        d = {"op": "Waypoint", "x": r(x), "z": r(z)}
        if y:
            d["y"] = r(y)
        self.op(d)
        self.stats["waypoints"] += 1
        if sym and self.sym:
            tx, tz, _ = self.twin(x, z)
            self.waypoint(tx, tz, y, sym=False)

    def spawn(self, x, z, team, y=0.0, sym=True):
        d = {"op": "Spawn", "x": r(x), "z": r(z)}
        if y:
            d["y"] = r(y)
        self.op(d)
        self.stats["spawns"] += 1
        self.spawns[team].append([r(x), r(y), r(z)])
        if sym and self.sym:
            tx, tz, _ = self.twin(x, z)
            self.spawn(tx, tz, 1 - team, y, sym=False)

    def clutter(self, props, x0, z0, x1, z1, count, seed, mirror=None, **kw):
        d = {"op": "Clutter", "props": props, "x0": r(min(x0, x1)), "z0": r(min(z0, z1)), "x1": r(max(x0, x1)), "z1": r(max(z0, z1)),
             "count": int(count), "seed": int(seed)}
        if mirror is None:
            mirror = self.sym == "rot"
        if mirror:
            d["mirror"] = True
        d.update(kw)
        self.op(d)

    # ------------------------------------------------------------------ occupancy
    def free(self, x0, z0, x1, z1, gap=0.3, ymax=0.3):
        """True when no solid box (standing lower than `ymax`... i.e. anything whose bottom is below ymax) or solid prop
        footprint overlaps the rectangle grown by gap."""
        for (bx0, bz0, bx1, bz1, by0, by1) in self.boxes:
            if by0 > ymax:
                continue
            if bx0 - gap < x1 and bx1 + gap > x0 and bz0 - gap < z1 and bz1 + gap > z0:
                return False
        for (fx0, fz0, fx1, fz1) in self.foot:
            if fx0 - gap < x1 and fx1 + gap > x0 and fz0 - gap < z1 and fz1 + gap > z0:
                return False
        return True


# ---------------------------------------------------------------------- props library
def load_props(repo, extra_dirs=()):
    """Name -> {solid, w, d, h, cx, cz} from Props/*/*.json and any pack prop folders given."""
    lib = {}
    files = glob.glob(os.path.join(repo, "Props", "*", "*.json")) + glob.glob(os.path.join(repo, "Props", "*", "*", "*.json"))
    for d in extra_dirs:
        files += glob.glob(os.path.join(d, "*.json")) + glob.glob(os.path.join(d, "*", "*.json"))
    for f in files:
        try:
            with open(f) as fh:
                d = json.load(fh)
        except (OSError, ValueError):
            continue
        if "parts" not in d or "name" not in d:
            continue
        xs, zs, ys = [], [], []
        for p in d["parts"]:
            s = p["size"]
            o = p.get("offset", [0, s[1] / 2, 0])
            turned = "rotation" in p or "yaw" in p
            rr = math.hypot(s[0], s[2]) if turned else None
            hx = (rr if rr else s[0]) / 2
            hz = (rr if rr else s[2]) / 2
            xs += [o[0] - hx, o[0] + hx]
            zs += [o[2] - hz, o[2] + hz]
            ys.append(o[1] + s[1] / 2)
        lib[d["name"]] = dict(solid=d.get("collision", "solid") == "solid", brk="breakable" in d, w=max(xs) - min(xs), d=max(zs) - min(zs),
                              h=max(ys), cx=(max(xs) + min(xs)) / 2, cz=(max(zs) + min(zs)) / 2)
    return lib


def footprint(info, x, z, yaw):
    a = math.radians(yaw)
    cs, sn = math.cos(a), math.sin(a)
    hx = abs(info["w"] / 2 * cs) + abs(info["d"] / 2 * sn)
    hz = abs(info["w"] / 2 * sn) + abs(info["d"] / 2 * cs)
    cx = x + info["cx"] * cs + info["cz"] * sn
    cz = z - info["cx"] * sn + info["cz"] * cs
    return (cx - hx, cz - hz, cx + hx, cz + hz)


# ---------------------------------------------------------------------- polygons
def catmull(pts, step=0.25):
    """Closed centripetal Catmull-Rom through pts (the game's "smooth" boundary), sampled every `step` metres."""
    n = len(pts)
    out = []
    for i in range(n):
        p0, p1, p2, p3 = pts[(i - 1) % n], pts[i], pts[(i + 1) % n], pts[(i + 2) % n]

        def tj(ti, a, b):
            return ti + max(1e-4, math.hypot(b[0] - a[0], b[1] - a[1])) ** 0.5
        t0 = 0.0
        t1 = tj(t0, p0, p1)
        t2 = tj(t1, p1, p2)
        t3 = tj(t2, p2, p3)
        seg = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
        k = max(2, int(seg / step))
        for s in range(k):
            t = t1 + (t2 - t1) * s / k

            def lerp(a, b, ta, tb):
                return [(tb - t) / (tb - ta) * a[0] + (t - ta) / (tb - ta) * b[0], (tb - t) / (tb - ta) * a[1] + (t - ta) / (tb - ta) * b[1]]
            a1 = lerp(p0, p1, t0, t1)
            a2 = lerp(p1, p2, t1, t2)
            a3 = lerp(p2, p3, t2, t3)
            b1 = lerp(a1, a2, t0, t2)
            b2 = lerp(a2, a3, t1, t3)
            out.append(lerp(b1, b2, t1, t2))
    return out


def inside(poly, x, z):
    c = False
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        if (az > z) != (bz > z) and x < ax + (z - az) * (bx - ax) / (bz - az):
            c = not c
    return c


def edge_dist(poly, x, z):
    best = 1e9
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        dx, dz = bx - ax, bz - az
        ll = dx * dx + dz * dz
        t = 0 if ll == 0 else max(0.0, min(1.0, ((x - ax) * dx + (z - az) * dz) / ll))
        best = min(best, math.hypot(ax + t * dx - x, az + t * dz - z))
    return best


def area(poly):
    a = 0.0
    n = len(poly)
    for i in range(n):
        ax, az = poly[i]
        bx, bz = poly[(i + 1) % n]
        a += ax * bz - bx * az
    return abs(a) / 2


def regular_polygon(radius, sides, phase_deg=0.0):
    return [[r(radius * math.cos(math.radians(phase_deg + 360 * i / sides))), r(radius * math.sin(math.radians(phase_deg + 360 * i / sides)))] for i in range(sides)]


# ---------------------------------------------------------------------- plan
def plan(kit, outline, path, extents, hills=(), flags=(), scale=4):
    from PIL import Image, ImageDraw
    hx, hz = extents
    W, H = int(2 * hx * scale), int(2 * hz * scale)
    img = Image.new("RGB", (W, H), (24, 26, 34))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * scale, H / 2 + z * scale)
    d.polygon([P(x, z) for x, z in outline], outline=(120, 130, 160), fill=(44, 48, 60))
    for (x0, z0, x1, z1, y0, y1) in sorted(kit.boxes, key=lambda b: b[5]):
        c = int(min(255, 90 + y1 * 30))
        d.rectangle([P(x0, z0), P(x1, z1)], fill=(c, c, c - 10) if y0 < 0.5 else (c - 30, c - 10, c + 20))
    for (x0, z0, x1, z1) in kit.foot:
        d.rectangle([P(x0, z0), P(x1, z1)], fill=(220, 170, 60))
    for o in kit.ops:
        if o["op"] == "Waypoint":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 2, z - 2, x + 2, z + 2], fill=(80, 200, 255))
    for team, col in ((0, (80, 160, 255)), (1, (255, 90, 80))):
        for (x, _y, z) in kit.spawns[team]:
            px, pz = P(x, z)
            d.ellipse([px - 4, pz - 4, px + 4, pz + 4], fill=col)
    for h in hills:
        px, pz = P(h["x"], h["z"])
        rr = h.get("radius", 4.5) * scale
        d.ellipse([px - rr, pz - rr, px + rr, pz + rr], outline=(255, 240, 80), width=3)
    for f in flags:
        px, pz = P(f[0], f[-1])
        d.rectangle([px - 5, pz - 5, px + 5, pz + 5], fill=(255, 255, 255))
    img.save(path)
    print("plan:", path)
