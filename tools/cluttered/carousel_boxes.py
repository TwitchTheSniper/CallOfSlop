import json, math, glob
ROOT = __import__("os").path.normpath(__import__("os").path.join(__import__("os").path.dirname(__import__("os").path.abspath(__file__)), "..", ".."))
PROPS = {}
for f in glob.glob(ROOT + "/Props/*/*.json"):
    d = json.load(open(f))
    xs = []; zs = []; ys = []
    for p in d["parts"]:
        s = p["size"]; o = p.get("offset", [0, s[1] / 2, 0])
        xs += [o[0] - s[0] / 2, o[0] + s[0] / 2]; zs += [o[2] - s[2] / 2, o[2] + s[2] / 2]; ys.append(o[1] + s[1] / 2)
    PROPS[d["name"]] = (max(xs) - min(xs), max(zs) - min(zs), max(ys), d["collision"] == "solid")

def extract(mp):
    boxes = []  # (x0,y0,z0,x1,y1,z1,color)
    def B(x0, y0, z0, x1, y1, z1, c="#888"):
        boxes.append((min(x0, x1), y0, min(z0, z1), max(x0, x1), y1, max(z0, z1), c))
    
    props = []; meshes = []; spawns = []; wps = []
    for op in mp["ops"]:
        t = op["op"]
        if t == "Block":
            B(op["x0"], op["y0"], op["z0"], op["x1"], op["y1"], op["z1"], op["color"] if isinstance(op["color"], str) else "#888")
        elif t == "Container":
            hx, hz = (3, 1.25) if op["alongX"] else (1.25, 3)
            B(op["x"] - hx, 0, op["z"] - hz, op["x"] + hx, 2.6, op["z"] + hz, op["color"])
        elif t == "StairsX":
            n = math.ceil(op["height"] / 0.4)
            for k in range(1, n + 1):
                a = op["x"] + op["dir"] * (k - 1) * 0.45; b = op["x"] + op["dir"] * k * 0.45
                B(a, 0, op["z0"], b, k * op["height"] / n, op["z1"], op["color"])
        elif t == "StairsZ":
            n = math.ceil(op["height"] / 0.4)
            for k in range(1, n + 1):
                a = op["z"] + op["dir"] * (k - 1) * 0.45; b = op["z"] + op["dir"] * k * 0.45
                B(op["x0"], 0, a, op["x1"], k * op["height"] / n, b, op["color"])
        elif t == "Watchtower":
            x, z, h, sd = op["x"], op["z"], op["height"], op.get("stairDir", 1)
            for lx in (-1, 1):
                for lz in (-1, 1):
                    B(x + lx * 1.3 - 0.2, 0, z + lz * 1.3 - 0.2, x + lx * 1.3 + 0.2, h, z + lz * 1.3 + 0.2, op["color"])
            B(x - 1.5, h, z - 1.5, x + 1.5, h + 1.25, z + 1.5, op["color"])
            n = math.ceil((h + 0.25) / 0.4); L = n * 0.45
            if sd > 0:
                B(x - 0.6, 0, z + 1.5, x + 0.6, h, z + 1.5 + L, op["color"])
            else:
                B(x - 0.6, 0, z - 1.5 - L, x + 0.6, h, z - 1.5, op["color"])
        elif t == "CoverLine":
            x0, z0, x1, z1 = op["x0"], op["z0"], op["x1"], op["z1"]
            L = math.hypot(x1 - x0, z1 - z0); seg = op.get("segment", 6); gap = op.get("gap", 2)
            d = 0
            while d < L:
                e = min(L, d + seg)
                for u in (d, e):
                    pass
                ax, az = x0 + (x1 - x0) * d / L, z0 + (z1 - z0) * d / L
                bx, bz = x0 + (x1 - x0) * e / L, z0 + (z1 - z0) * e / L
                th = op["thick"] / 2
                B(min(ax, bx) - th, 0, min(az, bz) - th, max(ax, bx) + th, op["height"], max(az, bz) + th, op["color"])
                d = e + gap
        elif t == "Prop" and "name" in op:
            props.append(op)
        elif t == "Mesh":
            meshes.append(op)
        elif t == "Spawn":
            spawns.append((op["x"], op["z"]))
        elif t == "Waypoint":
            wps.append((op["x"], op["z"]))
    
    return boxes, props, meshes, spawns, wps
