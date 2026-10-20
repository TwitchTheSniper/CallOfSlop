"""Renders prop files to a PNG contact sheet (software z-buffer, same rotation conventions as the game)."""
import json, math, sys, glob, os
import numpy as np
from PIL import Image, ImageDraw

COLORS = {}


def load_colors(cs_path):
    import re
    src = open(cs_path).read()
    for m in re.finditer(r'\["(\w+)"\] = new\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)', src):
        COLORS[m.group(1).lower()] = (float(m.group(2)), float(m.group(3)), float(m.group(4)))


def col(c):
    if isinstance(c, list):
        return tuple(c)
    if c.startswith("#"):
        return tuple(int(c[i:i + 2], 16) / 255 for i in (1, 3, 5))
    return COLORS[c.lower()]


def rot(pitch, yaw, roll):
    p, y, r = map(math.radians, (pitch, yaw, roll))
    Ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    Rx = np.array([[1, 0, 0], [0, math.cos(p), -math.sin(p)], [0, math.sin(p), math.cos(p)]])
    Rz = np.array([[math.cos(r), -math.sin(r), 0], [math.sin(r), math.cos(r), 0], [0, 0, 1]])
    return Ry @ Rx @ Rz


def tris(prop):
    out = []
    for part in prop["parts"]:
        s = np.array(part["size"]) / 2
        off = np.array(part.get("offset", [0, part["size"][1] / 2, 0]))
        R = rot(*part.get("rotation", [0, 0, 0]))
        c = np.array(col(part.get("color", "grey")))
        corners = np.array([[x, y, z] for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]) * s
        P = (R @ corners.T).T + off
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        for f in faces:
            a, b, cc, d = (P[i] for i in f)
            n = np.cross(b - a, d - a)
            ln = np.linalg.norm(n)
            if ln < 1e-12:
                continue
            n = n / ln
            # make normal point away from part centre
            if np.dot(n, (a + cc) / 2 - off) < 0:
                n = -n
            y = (a[1] + b[1] + cc[1] + d[1]) / 4
            ao = 0.72 + 0.28 * min(max(y / 0.45, 0), 1) ** 2 * (3 - 2 * min(max(y / 0.45, 0), 1))
            out.append((a, b, cc, n, c * ao))
            out.append((a, cc, d, n, c * ao))
    return out


SUN = np.array([-0.4, 0.8, -0.3]); SUN /= np.linalg.norm(SUN)


def render(prop, size=260, view=(35, 30)):
    T = tris(prop)
    yaw, elev = map(math.radians, view)
    # camera: looking from direction (sin yaw cos elev, sin elev, cos yaw cos elev)
    fwd = -np.array([math.sin(yaw) * math.cos(elev), math.sin(elev), math.cos(yaw) * math.cos(elev)])
    right = np.cross(fwd, [0, 1, 0]); right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    pts = np.array([p for t in T for p in t[:3]])
    sx = pts @ right; sy = pts @ up
    span = max(sx.max() - sx.min(), sy.max() - sy.min(), 0.3) * 1.15
    cx, cy = (sx.max() + sx.min()) / 2, (sy.max() + sy.min()) / 2
    scale = size / span
    img = np.ones((size, size, 3)) * np.array([0.36, 0.42, 0.5])
    zb = np.full((size, size), 1e9)
    for (a, b, c, n, colr) in T:
        light = colr * (0.45 + 0.55 * max(np.dot(n, SUN), 0) + 0.12 * (1 - abs(n[1])))
        P = [np.array([(p @ right - cx) * scale + size / 2, size / 2 - (p @ up - cy) * scale, p @ fwd]) for p in (a, b, c)]
        xs = [p[0] for p in P]; ys = [p[1] for p in P]
        x0, x1 = max(int(min(xs)), 0), min(int(max(xs)) + 1, size - 1)
        y0, y1 = max(int(min(ys)), 0), min(int(max(ys)) + 1, size - 1)
        if x1 < x0 or y1 < y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ax, ay, az), (bx, by, bz), (qx, qy, qz) = P
        den = (by - qy) * (ax - qx) + (qx - bx) * (ay - qy)
        if abs(den) < 1e-9:
            continue
        w0 = ((by - qy) * (X - qx) + (qx - bx) * (Y - qy)) / den
        w1 = ((qy - ay) * (X - qx) + (ax - qx) * (Y - qy)) / den
        w2 = 1 - w0 - w1
        m = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        z = w0 * az + w1 * bz + w2 * qz
        sub = zb[y0:y1 + 1, x0:x1 + 1]
        m &= z < sub
        sub[m] = z[m]
        img[y0:y1 + 1, x0:x1 + 1][m] = np.clip(light, 0, 1)
    return Image.fromarray((img * 255).astype(np.uint8))


if __name__ == "__main__":
    load_colors(sys.argv[1])
    files = sorted(glob.glob(os.path.join(sys.argv[2], "**", "*.json"), recursive=True))
    only = sys.argv[4:] if len(sys.argv) > 4 else None
    props = [json.load(open(f)) for f in files]
    if only:
        props = [p for p in props if any(o.lower() in p["name"].lower() for o in only)]
    S = 240
    cols = 6
    rows = (len(props) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * S, rows * (S + 16)), (30, 30, 30))
    d = ImageDraw.Draw(sheet)
    for i, p in enumerate(props):
        im = render(p, S)
        x, y = (i % cols) * S, (i // cols) * (S + 16)
        sheet.paste(im, (x, y))
        d.text((x + 4, y + S + 2), p["name"], fill=(255, 255, 255))
    sheet.save(sys.argv[3])
    print(len(props), "rendered")
