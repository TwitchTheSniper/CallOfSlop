#!/usr/bin/env python3
"""Writes the test models in Models/ (barrel.glb, traffic_cone.glb, lamp_post.gltf).

Pure Python 3 (no numpy, no Blender), deterministic: running it again gives byte-identical files.
Usage:  python3 tools/models/make_models.py [output folder, default: Models next to the repo root]

The models are deliberately tiny and exercise the parts of glTF 2.0 the game's loader supports:
  barrel.glb        binary .glb, two materials, one mesh used by two nodes (the hoops), a bulged lathe body
  traffic_cone.glb  binary .glb with an embedded PNG base colour texture (orange and white bands) and texture coordinates
  lamp_post.gltf    text .gltf with the buffer embedded as a base64 data URI, a 5-node hierarchy (TRS and one "matrix" node)
Base colour factors are written linear (glTF's rule); the game converts them back to the sRGB values listed here.
"""
import base64
import json
import math
import os
import struct
import sys
import zlib


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def colour(r, g, b, a=1.0):
    return [round(srgb_to_linear(r), 6), round(srgb_to_linear(g), 6), round(srgb_to_linear(b), 6), a]


class Geo:
    """An indexed triangle list: positions, normals, uvs and uint16 indices, outward-facing counter-clockwise triangles."""

    def __init__(self):
        self.p, self.n, self.uv, self.i = [], [], [], []

    def vert(self, p, n, uv):
        self.p.append(p)
        self.n.append(n)
        self.uv.append(uv)
        return len(self.p) - 1

    def tri(self, a, b, c):
        # Orient by the vertex normals so every triangle faces outward whatever order it was listed in.
        pa, pb, pc = self.p[a], self.p[b], self.p[c]
        e1 = [pb[k] - pa[k] for k in range(3)]
        e2 = [pc[k] - pa[k] for k in range(3)]
        fn = [e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]]
        area = math.sqrt(sum(x * x for x in fn))
        if area < 1e-9:
            return
        nn = [self.n[a][k] + self.n[b][k] + self.n[c][k] for k in range(3)]
        if sum(fn[k] * nn[k] for k in range(3)) < 0:
            b, c = c, b
        self.i += [a, b, c]


def lathe(geo, strips, segments, y_to_v=None):
    """Revolves profile strips around +Y. Each strip is a list of (r, y, nr, ny); bands join neighbouring points.
    A strip whose points all share one y is a flat cap and gets planar uvs."""
    for strip in strips:
        flat = all(abs(pt[1] - strip[0][1]) < 1e-9 for pt in strip)
        rmax = max(pt[0] for pt in strip) or 1.0
        rows = []
        for k, (r, y, nr, ny) in enumerate(strip):
            row = []
            for j in range(segments + 1):
                a = 2 * math.pi * (j % segments) / segments
                s, c = math.sin(a), math.cos(a)
                pos = [round(r * s, 6), round(y, 6), round(r * c, 6)]
                nl = math.hypot(nr, ny) or 1.0
                nrm = [round(nr / nl * s, 6), round(ny / nl, 6), round(nr / nl * c, 6)]
                if flat:
                    uv = [round(0.5 + r * s / (2 * rmax), 6), round(0.5 + r * c / (2 * rmax), 6)]
                else:
                    uv = [round(j / segments, 6), round(y_to_v(y) if y_to_v else y, 6)]
                row.append(geo.vert(pos, nrm, uv))
            rows.append(row)
        for k in range(len(rows) - 1):
            for j in range(segments):
                a, b, c, d = rows[k][j], rows[k][j + 1], rows[k + 1][j + 1], rows[k + 1][j]
                geo.tri(a, b, c)
                geo.tri(a, c, d)


def box(geo, mn, mx):
    faces = [((1, 0, 0), (0, 1, 0), (0, 0, 1)), ((-1, 0, 0), (0, 0, 1), (0, 1, 0)), ((0, 1, 0), (0, 0, 1), (1, 0, 0)),
             ((0, -1, 0), (1, 0, 0), (0, 0, 1)), ((0, 0, 1), (1, 0, 0), (0, 1, 0)), ((0, 0, -1), (0, 1, 0), (1, 0, 0))]
    c = [(mn[k] + mx[k]) / 2 for k in range(3)]
    h = [(mx[k] - mn[k]) / 2 for k in range(3)]
    for n, u, v in faces:
        idx = []
        for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            p = [round(c[k] + n[k] * h[k] + u[k] * su * h[k] + v[k] * sv * h[k], 6) for k in range(3)]
            idx.append(geo.vert(p, list(map(float, n)), [(su + 1) / 2, (sv + 1) / 2]))
        geo.tri(idx[0], idx[1], idx[2])
        geo.tri(idx[0], idx[2], idx[3])


def cylinder_strips(r_bottom, r_top, y0, y1):
    """A capped (possibly tapered) cylinder: bottom cap, side, top cap as separate strips (hard edges)."""
    dr, dy = r_bottom - r_top, y1 - y0
    side_n = (dy, dr)  # outward normal of the slanted side in (r, y)
    return [
        [(0.0, y0, 0.0, -1.0), (r_bottom, y0, 0.0, -1.0)],
        [(r_bottom, y0) + side_n, (r_top, y1) + side_n],
        [(r_top, y1, 0.0, 1.0), (0.0, y1, 0.0, 1.0)],
    ]


def png(width, height, rows):
    """Minimal RGB PNG writer (filter 0)."""
    raw = b"".join(b"\x00" + bytes(row) for row in rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


class Doc:
    """Collects buffer views, accessors, meshes, materials and nodes into one buffer."""

    def __init__(self):
        self.bin = bytearray()
        self.views, self.accessors, self.meshes, self.materials, self.nodes = [], [], [], [], []
        self.images, self.textures, self.samplers = [], [], []

    def _view(self, data, target=None):
        while len(self.bin) % 4:
            self.bin.append(0)
        view = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data)}
        if target:
            view["target"] = target
        self.bin += data
        self.views.append(view)
        return len(self.views) - 1

    def _accessor(self, values, comps, ctype, kind, target, minmax=False):
        fmt = {5126: "f", 5123: "H"}[ctype]
        flat = [x for v in values for x in (v if comps > 1 else [v])]
        view = self._view(struct.pack("<%d%s" % (len(flat), fmt), *flat), target)
        acc = {"bufferView": view, "componentType": ctype, "count": len(values), "type": kind}
        if minmax:
            acc["min"] = [min(v[k] for v in values) for k in range(comps)]
            acc["max"] = [max(v[k] for v in values) for k in range(comps)]
        self.accessors.append(acc)
        return len(self.accessors) - 1

    def material(self, name, rgba, metallic=0.0, roughness=0.8, texture=None):
        pbr = {"baseColorFactor": rgba, "metallicFactor": metallic, "roughnessFactor": roughness}
        if texture is not None:
            pbr["baseColorTexture"] = {"index": texture}
        self.materials.append({"name": name, "pbrMetallicRoughness": pbr})
        return len(self.materials) - 1

    def texture_png(self, data):
        view = self._view(data)
        self.images.append({"bufferView": view, "mimeType": "image/png", "name": "bands"})
        if not self.samplers:
            self.samplers.append({"magFilter": 9728, "minFilter": 9987, "wrapS": 10497, "wrapT": 33071})
        self.textures.append({"sampler": 0, "source": len(self.images) - 1})
        return len(self.textures) - 1

    def mesh(self, name, parts):
        prims = []
        for geo, mat in parts:
            prims.append({
                "attributes": {
                    "POSITION": self._accessor(geo.p, 3, 5126, "VEC3", 34962, minmax=True),
                    "NORMAL": self._accessor(geo.n, 3, 5126, "VEC3", 34962),
                    "TEXCOORD_0": self._accessor(geo.uv, 2, 5126, "VEC2", 34962),
                },
                "indices": self._accessor(geo.i, 1, 5123, "SCALAR", 34963),
                "material": mat,
                "mode": 4,
            })
        self.meshes.append({"name": name, "primitives": prims})
        return len(self.meshes) - 1

    def node(self, name, mesh=None, children=None, **trs):
        n = {"name": name}
        if mesh is not None:
            n["mesh"] = mesh
        if children:
            n["children"] = children
        n.update(trs)
        self.nodes.append(n)
        return len(self.nodes) - 1

    def json(self, uri=None):
        while len(self.bin) % 4:
            self.bin.append(0)
        buf = {"byteLength": len(self.bin)}
        if uri:
            buf["uri"] = uri
        roots = [i for i in range(len(self.nodes)) if not any(i in n.get("children", []) for n in self.nodes)]
        doc = {
            "asset": {"version": "2.0", "generator": "Call of Slop tools/models/make_models.py"},
            "scene": 0,
            "scenes": [{"name": "Scene", "nodes": roots}],
            "nodes": self.nodes,
            "meshes": self.meshes,
            "materials": self.materials,
            "accessors": self.accessors,
            "bufferViews": self.views,
            "buffers": [buf],
        }
        if self.textures:
            doc["textures"], doc["images"], doc["samplers"] = self.textures, self.images, self.samplers
        return doc

    def write_glb(self, path):
        js = json.dumps(self.json(), separators=(",", ":")).encode()
        js += b" " * ((4 - len(js) % 4) % 4)
        total = 12 + 8 + len(js) + 8 + len(self.bin)
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, total))
            f.write(struct.pack("<II", len(js), 0x4E4F534A) + js)
            f.write(struct.pack("<II", len(self.bin), 0x004E4942) + bytes(self.bin))

    def write_gltf(self, path):
        uri = "data:application/octet-stream;base64," + base64.b64encode(bytes(self.bin)).decode()
        with open(path, "w", newline="\n") as f:
            json.dump(self.json(uri), f, indent=1)
            f.write("\n")


def barrel(path):
    d = Doc()
    paint = d.material("Paint", colour(0.62, 0.14, 0.10), roughness=0.6)
    steel = d.material("Steel", colour(0.46, 0.48, 0.50), metallic=0.8, roughness=0.4)
    # Body: a slightly bulged lathe, smooth sides, flat lids.
    prof = [(0.0, 0.27), (0.11, 0.287), (0.44, 0.30), (0.77, 0.287), (0.88, 0.27)]
    side = []
    for k, (y, r) in enumerate(prof):
        y0, r0 = prof[max(k - 1, 0)]
        y1, r1 = prof[min(k + 1, len(prof) - 1)]
        side.append((r, y, y1 - y0, -(r1 - r0)))
    body = Geo()
    lathe(body, [[(0.0, 0.0, 0.0, -1.0), (0.27, 0.0, 0.0, -1.0)], side, [(0.27, 0.88, 0.0, 1.0), (0.0, 0.88, 0.0, 1.0)]],
          20, lambda y: y / 0.88)
    body_mesh = d.mesh("BarrelBody", [(body, paint)])
    # Hoop: a short ring (outer wall, top, inner wall, bottom), shared by two nodes.
    hoop = Geo()
    ro, ri, h = 0.306, 0.28, 0.035
    lathe(hoop, [[(ro, -h, 1.0, 0.0), (ro, h, 1.0, 0.0)], [(ro, h, 0.0, 1.0), (ri, h, 0.0, 1.0)],
                 [(ri, h, -1.0, 0.0), (ri, -h, -1.0, 0.0)], [(ri, -h, 0.0, -1.0), (ro, -h, 0.0, -1.0)]], 20)
    hoop_mesh = d.mesh("Hoop", [(hoop, steel)])
    b = d.node("Body", body_mesh)
    h1 = d.node("HoopLow", hoop_mesh, translation=[0.0, 0.25, 0.0])
    h2 = d.node("HoopHigh", hoop_mesh, translation=[0.0, 0.63, 0.0])
    d.node("Barrel", children=[b, h1, h2])
    d.write_glb(path)


def traffic_cone(path):
    d = Doc()
    rows = []
    for y in range(64):
        white = 18 <= y < 27 or 36 <= y < 42
        rows.append([245, 245, 240] * 16 if white else [235, 95, 20] * 16)
    tex = d.texture_png(png(16, 64, rows))
    orange = d.material("ConeBands", [1.0, 1.0, 1.0, 1.0], roughness=0.7, texture=tex)
    black = d.material("Rubber", colour(0.08, 0.08, 0.08), roughness=0.9)
    cone = Geo()
    lathe(cone, cylinder_strips(0.15, 0.025, 0.03, 0.72), 16, lambda y: (0.72 - y) / 0.69)
    base = Geo()
    box(base, (-0.19, 0.0, -0.19), (0.19, 0.03, 0.19))
    m = d.mesh("TrafficCone", [(cone, orange), (base, black)])
    d.node("TrafficCone", m)
    d.write_glb(path)


def lamp_post(path):
    d = Doc()
    green = d.material("PaintedMetal", colour(0.14, 0.26, 0.19), metallic=0.6, roughness=0.5)
    glass = d.material("Glass", colour(1.0, 0.94, 0.68), roughness=0.2)
    base = Geo()
    lathe(base, cylinder_strips(0.18, 0.11, 0.0, 0.3), 8, lambda y: y)
    pole = Geo()
    lathe(pole, cylinder_strips(0.06, 0.05, 0.0, 3.2), 12, lambda y: y / 3.2)
    arm = Geo()
    box(arm, (0.0, -0.03, -0.03), (0.9, 0.03, 0.03))
    shade, bulb = Geo(), Geo()
    lathe(shade, cylinder_strips(0.22, 0.05, -0.18, 0.0), 12, lambda y: -y / 0.18)
    lathe(bulb, cylinder_strips(0.12, 0.12, -0.22, -0.18), 12, lambda y: y)
    m_base = d.mesh("Base", [(base, green)])
    m_pole = d.mesh("Pole", [(pole, green)])
    m_arm = d.mesh("Arm", [(arm, green)])
    m_head = d.mesh("Head", [(shade, green), (bulb, glass)])
    # Head hangs off the end of the arm; written as a column-major "matrix" (a translation) to exercise that path.
    head = d.node("Head", m_head, matrix=[1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0.9, -0.03, 0, 1])
    s, c = math.sin(math.radians(10) / 2), math.cos(math.radians(10) / 2)
    arm_n = d.node("Arm", m_arm, children=[head], translation=[0.0, 3.0, 0.0], rotation=[0.0, 0.0, round(s, 6), round(c, 6)])
    pole_n = d.node("Pole", m_pole, children=[arm_n], translation=[0.0, 0.3, 0.0])
    base_n = d.node("Base", m_base)
    d.node("LampPost", children=[base_n, pole_n])
    d.write_gltf(path)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "Models")
    os.makedirs(out, exist_ok=True)
    barrel(os.path.join(out, "barrel.glb"))
    traffic_cone(os.path.join(out, "traffic_cone.glb"))
    lamp_post(os.path.join(out, "lamp_post.gltf"))
    for name in ("barrel.glb", "traffic_cone.glb", "lamp_post.gltf"):
        print(name, os.path.getsize(os.path.join(out, name)), "bytes")


if __name__ == "__main__":
    main()
