#!/usr/bin/env python3
"""Shared helpers for the Space pack's generated maps (tools/space/arboretum.py, reactor.py): an op recorder with
counts, point-symmetric placement (everything Blue has at (x, z) Red has at (-x, -z), turned 180 degrees), mesh and
prop helpers, decal sign tiles (tools/space/canopy_ignition_decals.py) and a top-down plan drawing.
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import canopy_ignition_decals as sdec  # noqa: E402


def r(v, n=3):
    return round(float(v), n)


class Builder:
    def __init__(self):
        self.ops = []
        self.stats = {"boxes": 0, "colliders": 0, "meshes": 0, "props": 0, "decals": 0, "waypoints": 0, "spawns": 0}
        self.spawns = {"blue": [], "red": []}

    def op(self, d):
        self.ops.append(d)

    # ---- symmetry: s = +1 as given, s = -1 turned 180 degrees about the centre ----
    @staticmethod
    def P(x, z, s):
        return (s * x, s * z)

    @staticmethod
    def Y(yaw, s):
        return (yaw + (0 if s > 0 else 180)) % 360

    # ---- ops ----
    def block(self, x0, y0, z0, x1, y1, z1, color, s=1, anchor=None, material=None):
        if s < 0:
            x0, x1, z0, z1 = -x1, -x0, -z1, -z0
        self.stats["boxes"] += 1
        d = {"op": "Block", "x0": r(x0), "y0": r(y0), "z0": r(z0), "x1": r(x1), "y1": r(y1), "z1": r(z1), "color": color}
        if anchor:
            d["anchor"] = anchor
        self.op(d)

    def collider(self, x0, y0, z0, x1, y1, z1, s=1, anchor=None):
        if s < 0:
            x0, x1, z0, z1 = -x1, -x0, -z1, -z0
        if abs(x1 - x0) < 0.02 or abs(y1 - y0) < 0.02 or abs(z1 - z0) < 0.02:
            return
        self.stats["colliders"] += 1
        d = {"op": "Collider", "x0": r(x0), "y0": r(y0), "z0": r(z0), "x1": r(x1), "y1": r(y1), "z1": r(z1)}
        if anchor:
            d["anchor"] = anchor
        self.op(d)

    def mesh(self, kind, x, z, y, color, s=1, anchor=None, **kw):
        x, z = self.P(x, z, s)
        if "yaw" in kw and s < 0:
            kw["yaw"] = kw["yaw"] + 180
        self.stats["meshes"] += 1
        d = {"op": "Mesh", "mesh": kind, "x": r(x), "z": r(z), "y": r(y), "color": color}
        if anchor:
            d["anchor"] = anchor
        for k, v in kw.items():
            if v is None:
                continue
            d[k] = r(v) if isinstance(v, float) else v
        self.op(d)

    def prop(self, name, x, z, yaw=0.0, s=1, y=0.0, **kw):
        x, z = self.P(x, z, s)
        self.stats["props"] += 1
        d = {"op": "Prop", "name": name, "x": r(x), "z": r(z)}
        if y:
            d["y"] = r(y)
        yaw = self.Y(yaw, s)
        if yaw:
            d["yaw"] = r(yaw, 1)
        d.update(kw)
        self.op(d)

    def props(self, items, s=1):
        """items: (name, x, z, yaw) or (name, x, z, yaw, y)."""
        for it in items:
            self.prop(it[0], it[1], it[2], it[3], s=s, y=it[4] if len(it) > 4 else 0.0)

    def decal(self, name, x, z, y=0.0, onto="floor", s=1, **kw):
        x, z = self.P(x, z, s)
        if s < 0:
            if onto in ("wall+x", "wall-x", "wall+z", "wall-z"):
                onto = onto.replace("+", "#").replace("-", "+").replace("#", "-")
            if "rotation" in kw and kw["rotation"] >= 0:
                kw["rotation"] = (kw["rotation"] + 180) % 360
        self.stats["decals"] += 1
        d = {"op": "Decal", "decal": name, "x": r(x), "z": r(z), "y": r(y), "onto": onto}
        d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
        self.op(d)

    def scatter(self, decals, x0, z0, x1, z1, count, seed, s=1, **kw):
        if s < 0:
            x0, x1, z0, z1 = -x1, -x0, -z1, -z0
        self.stats["decals"] += count
        d = {"op": "DecalScatter", "decals": decals, "x0": r(x0), "z0": r(z0), "x1": r(x1), "z1": r(z1), "count": count, "seed": seed}
        d.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items()})
        self.op(d)

    def sign(self, key, cx, z, y, onto, s=1, along="x", reach=2.0):
        """A wall sign cut into square tiles, centred on (cx, z) at height y, along X (or Z for walls facing +-X)."""
        names, tw, th = sdec.tiles(key)
        n = len(names)
        for i, nm in enumerate(names):
            off = (i - (n - 1) / 2) * tw
            # Reading left to right as seen from the side the sign faces.
            if along == "x":
                flip = -1 if onto == "wall+z" else 1
                self.decal(nm, cx + flip * off, z, y, onto=onto, s=s, size=tw, reach=reach)
            else:
                flip = 1 if onto == "wall+x" else -1
                self.decal(nm, cx, z + flip * off, y, onto=onto, s=s, size=tw, reach=reach)

    def waypoint(self, x, z, y=0.0, s=1):
        x, z = self.P(x, z, s)
        self.stats["waypoints"] += 1
        d = {"op": "Waypoint", "x": r(x), "z": r(z)}
        if y:
            d["y"] = r(y)
        self.op(d)

    def spawn(self, x, z, team=None, s=1, y=None):
        """A Spawn op; with a team ("blue" for s = +1, its twin is Red's) it joins that team's list."""
        x, z = self.P(x, z, s)
        self.stats["spawns"] += 1
        d = {"op": "Spawn", "x": r(x), "z": r(z)}
        if y is not None:
            d["y"] = r(y)
        self.op(d)
        if team:
            side = team if s > 0 else ("red" if team == "blue" else "blue")
            self.spawns[side].append([r(x), r(z)] if y is None else [r(x), r(y), r(z)])

    def both(self, fn, *a, **kw):
        """Run a placement function for Blue's side (s = +1) and again turned 180 degrees for Red's (s = -1)."""
        fn(1, *a, **kw)
        fn(-1, *a, **kw)


def ellipse_points(rx, rz, n=32, cx=0.0, cz=0.0):
    return [[r(cx + rx * math.cos(2 * math.pi * k / n)), r(cz + rz * math.sin(2 * math.pi * k / n))] for k in range(n)]


def yaw_towards(dx, dz):
    """Mesh / prop yaw (degrees) that turns a shape's local +Z (a prop's front) towards (dx, dz)."""
    return math.degrees(math.atan2(dx, dz))


def write_map(path, m, stats):
    with open(path, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print(f"wrote {path}: {len(m['ops'])} ops, {stats}")


def plan(path, ops, outline, size=(300, 240), scale=2.2):
    """A top-down PNG of the boxes, props, spawns, waypoints and the outline (for a quick look without the game)."""
    from PIL import Image, ImageDraw
    W, H = int(size[0] * scale), int(size[1] * scale)
    img = Image.new("RGB", (W, H), (30, 30, 40))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * scale, H / 2 + z * scale)
    d.polygon([P(x, z) for x, z in outline], outline=(120, 120, 160))
    for o in ops:
        if o["op"] in ("Block", "Collider"):
            c = (90, 90, 100) if o["op"] == "Collider" else (170, 150, 120)
            d.rectangle([P(o["x0"], o["z0"]), P(o["x1"], o["z1"])], fill=c)
    for o in ops:
        if o["op"] == "Waypoint":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 1.5, z - 1.5, x + 1.5, z + 1.5], fill=(80, 200, 255))
        elif o["op"] == "Spawn":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 3, z - 3, x + 3, z + 3], fill=(80, 255, 80) if o["x"] < 0 else (255, 80, 80))
        elif o["op"] == "Prop":
            x, z = P(o["x"], o["z"])
            d.rectangle([x - 2, z - 2, x + 2, z + 2], fill=(255, 200, 60))
        elif o["op"] == "Mesh":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 1, z - 1, x + 1, z + 1], fill=(120, 120, 200))
    img.save(path)
    print("plan:", path)
