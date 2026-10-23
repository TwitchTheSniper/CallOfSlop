"""Renders every weapon-builder part to a PNG contact sheet, without the game (Zombies weapon builder).

Usage:
    python3 tools/parts/viz.py [PARTS_DIR] [OUT.png] [options]
        PARTS_DIR   the Parts folder (default: Parts)
        OUT.png     the sheet (default: parts.png)
    --only TEXT ...   only parts whose name or file contains one of the texts
    --category NAME   only one category (barrel, receiver, ...)
    --templates       also draw Parts/_Template (the examples)
    --fragments       draw every fragment in Parts/_Fragments instead of the parts
    --no-sockets      leave out the socket and muzzle markers
    --cols N          tiles per row (default 6), --size PX tile size (default 240)
    --lint            only run the cross-part checks (fits nowhere, sockets nothing fits) and print them

It reads part files the way the game does (comments and trailing commas allowed), resolves "extends" (Parts/_Bases and
other parts) and fragment "use" entries (Parts/_Fragments), and draws each part's boxes (rods as octagons) with its
sockets as coloured markers (labelled by socket name) and muzzles as red dots. The rotation conventions match the game
(PartDef.Rot: Z, then X, then Y turns as Rz*Rx*Ry). Exit code 1 if a file didn't resolve or --lint found problems.
See docs/zombies/WEAPON-BUILDER.md, "Making parts".
"""
import glob
import json
import math
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

COLORS = {}
RARITY = {"common": (0.85, 0.85, 0.85), "uncommon": (0.35, 0.9, 0.4), "rare": (0.35, 0.65, 1.0), "epic": (0.75, 0.45, 1.0),
          "legendary": (1.0, 0.6, 0.15), "wonder": (1.0, 0.3, 0.3)}
DEFAULT_MOUNT = {"receiver": "receiver", "barrel": "barrel", "splitter": "barrel", "feed": "feed", "ammo": "ammo", "stock": "stock",
                 "grip": "", "optic": "optic", "muzzle": "muzzle", "upgrade": "upgrade", "multiplier": "amp", "hilt": "", "head": "head",
                 "meleemod": "mod", "utility": "utility"}
SOCKET_COLORS = {"receiver": (1.0, 0.85, 0.2), "barrel": (1.0, 0.45, 0.1), "feed": (0.2, 0.8, 1.0), "ammo": (0.95, 0.75, 0.3),
                 "stock": (0.6, 0.4, 0.2), "foregrip": (0.9, 0.3, 0.9), "optic": (0.3, 1.0, 0.5), "muzzle": (1.0, 1.0, 1.0),
                 "utility": (0.5, 0.5, 1.0), "upgrade": (1.0, 0.3, 0.5), "amp": (1.0, 0.1, 0.1), "hopper": (0.1, 1.0, 1.0),
                 "head": (0.9, 0.9, 0.3), "mod": (0.7, 0.9, 0.7)}
APPEND = ("model", "sockets", "stats", "muzzles", "flags", "bores")
REMOVE = ("sockets", "stats", "flags", "bores")


class PartError(Exception):
    pass


def load_colors():
    src = open(os.path.join(ROOT, "Source", "Weapons", "GunSpec.cs")).read()
    for m in re.finditer(r'\["(\w+)"\] = new\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)', src):
        COLORS[m.group(1).lower()] = (float(m.group(2)), float(m.group(3)), float(m.group(4)))


def lenient_json(text, source):
    """JSON with // and /* */ comments and trailing commas, like the game's reader."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            while i < n and text[i] != "\n":
                i += 1
        elif text.startswith("/*", i):
            i = text.find("*/", i + 2)
            i = n if i < 0 else i + 2
        else:
            out.append(c)
            i += 1
    clean = re.sub(r",(\s*[\]}])", r"\1", "".join(out))
    try:
        return json.loads(clean)
    except json.JSONDecodeError as e:
        raise PartError(f"{source}: not valid JSON ({e})")


def lower_keys(d):
    return {k.lower(): v for k, v in d.items()} if isinstance(d, dict) else d


# ---- rotations -------------------------------------------------------------------------------------------------

def mat_rot(deg):
    """PartDef.Rot: Rz * Rx * Ry (column vectors), angles in degrees [x, y, z]."""
    x, y, z = (math.radians(v) for v in deg)
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
    rx = [[1, 0, 0], [0, cx, -sx], [0, sx, cx]]
    ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    rz = [[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]]
    return mm(rz, mm(rx, ry))


def mm(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def mv(a, v):
    return [sum(a[i][k] * v[k] for k in range(3)) for i in range(3)]


# ---- the library -----------------------------------------------------------------------------------------------

class Library:
    def __init__(self, folder):
        self.folder = folder
        self.fragments, self.bases, self.parts_by_name, self.part_files = {}, {}, {}, []
        self.errors = []
        self.socket_types = list(SOCKET_COLORS)
        self.default_mount = dict(DEFAULT_MOUNT)
        reg = os.path.join(folder, "_Registry.json")
        if os.path.exists(reg):
            try:
                r = lower_keys(lenient_json(open(reg, encoding="utf-8").read(), "_Registry.json"))
                self.socket_types = [s["name"] if isinstance(s, dict) else s for s in r.get("sockettypes", [])]
                for c in r.get("categories", []):
                    self.default_mount[c["name"].lower()] = c.get("mount", DEFAULT_MOUNT.get(c["name"].lower(), "utility"))
            except (PartError, KeyError, TypeError) as e:
                self.errors.append(f"_Registry.json: {e}")
        for f in sorted(glob.glob(os.path.join(folder, "**", "*.json"), recursive=True), key=str.lower):
            rel = os.path.relpath(f, folder).replace("\\", "/")
            top = rel.split("/")[0]
            try:
                data = lenient_json(open(f, encoding="utf-8").read(), rel)
            except PartError as e:
                if not (top in ("Builds", "Tables") or (top.startswith("_") and top not in ("_Fragments", "_Bases", "_Template"))):
                    self.errors.append(str(e))
                continue
            if top == "_Fragments":
                self.fragments[str(data.get("name", "")).strip().lower()] = (rel, data)
            elif top == "_Bases":
                self.bases[str(data.get("name", "")).strip().lower()] = (rel, data)
            elif top in ("Builds", "Tables") or top.startswith("_"):
                continue
            else:
                self.part_files.append((rel, data))
                if isinstance(data, dict) and "name" in data:
                    self.parts_by_name.setdefault(str(data["name"]).strip().lower(), (rel, data))

    def resolve(self, data, source, chain=()):
        """The part with its "extends" chain merged in."""
        data = lower_keys(data)
        if "extends" not in data:
            return {k: v for k, v in data.items() if k != "abstract"}
        base = str(data["extends"]).strip()
        me = str(data.get("name", source))
        if base.lower() in [c.lower() for c in chain] or base.lower() == me.lower() or len(chain) > 8:
            raise PartError(f"{source}: \"extends\" goes round in a circle ({' -> '.join(list(chain) + [me, base])})")
        found = self.bases.get(base.lower()) or self.parts_by_name.get(base.lower())
        if not found:
            raise PartError(f"{source}: \"extends\": no part or base called '{base}'")
        parent = self.resolve(found[1], found[0], tuple(chain) + (me,))
        return merge(parent, data, source)

    def boxes(self, model, where, depth=0):
        """A model list with fragment uses expanded: [(shape, size, pos, R, color, alpha, tag)]."""
        out = []
        for i, b in enumerate(model or []):
            b = lower_keys(b)
            at = f"{where}: model box #{i + 1}"
            if "use" in b:
                name = str(b["use"]).strip()
                frag = self.fragments.get(name.lower())
                if not frag:
                    raise PartError(f"{at}: no model fragment called '{name}'")
                if depth >= 8:
                    raise PartError(f"{at}: fragments nest more than 8 deep (does '{name}' use itself?)")
                sc = b.get("scale", 1)
                sc = [sc] * 3 if isinstance(sc, (int, float)) else sc
                R = mat_rot(b.get("rot", [0, 0, 0]))
                p0 = b.get("pos", [0, 0, 0])
                for (shape, size, pos, Rb, col, alpha, tag) in self.boxes(lower_keys(frag[1]).get("model"), f"{at} -> fragment '{name}' ({frag[0]})", depth + 1):
                    size = [abs(size[k] * sc[k]) for k in range(3)]
                    pos = [v + p0[k] for k, v in enumerate(mv(R, [pos[k] * sc[k] for k in range(3)]))]
                    out.append((shape, size, pos, mm(R, Rb), b.get("color", col), b.get("alpha", alpha), b.get("tag", tag)))
            else:
                if "size" not in b:
                    raise PartError(f"{at}: a box needs a \"size\"")
                out.append((b.get("shape", "box").lower(), b["size"], b.get("pos", [0, 0, 0]), mat_rot(b.get("rot", [0, 0, 0])),
                            b.get("color", "gunmetal"), b.get("alpha", 1.0), b.get("tag", "")))
        return out


def merge(parent, child, where):
    r = json.loads(json.dumps(parent))
    r.pop("abstract", None)
    for k, v in child.items():
        if k.startswith("-"):
            lst = k[1:]
            if lst not in REMOVE:
                raise PartError(f"{where}: \"{k}\" isn't a list you can remove from")
            names = [v] if isinstance(v, str) else v
            for n in names:
                arr = r.get(lst, [])
                idx = [i for i, x in enumerate(arr) if key_of(x, lst).lower() == n.lower()]
                if not idx:
                    raise PartError(f"{where}: \"{k}\": the base has no {lst[:-1]} \"{n}\"")
                arr.pop(idx[0])
    for k, v in child.items():
        if k[0] in "+-" or k in ("extends", "abstract"):
            continue
        if v is None:
            r.pop(k, None)
        elif isinstance(v, dict) and isinstance(r.get(k), dict):
            r[k] = merge_objects(r[k], v)
        else:
            r[k] = json.loads(json.dumps(v))
    for k, v in child.items():
        if not k.startswith("+"):
            continue
        lst = k[1:]
        if lst not in APPEND:
            raise PartError(f"{where}: \"{k}\" isn't a list you can add to")
        arr = r.setdefault(lst, [])
        for item in v:
            idx = [i for i, x in enumerate(arr) if lst == "sockets" and key_of(x, lst).lower() == key_of(item, lst).lower()]
            if idx:
                arr[idx[0]] = item
            else:
                arr.append(item)
    return r


def merge_objects(a, b):
    r = dict(a)
    for k, v in b.items():
        if v is None:
            r.pop(k, None)
        elif isinstance(v, dict) and isinstance(r.get(k), dict):
            r[k] = merge_objects(r[k], v)
        else:
            r[k] = v
    return r


def key_of(x, lst):
    if isinstance(x, dict):
        return str(lower_keys(x).get("name" if lst == "sockets" else "stat", ""))
    return str(x)


# ---- checks ----------------------------------------------------------------------------------------------------

def lint(parts, lib):
    """The game's cross-part checks (Parts.Lint): parts that fit nowhere, sockets nothing fits."""
    problems = []

    def mount(p):
        return str(p.get("mount", lib.default_mount.get(p["category"].lower(), "utility"))).lower()

    def takes(s, p):
        acc = [a.lower() for a in s.get("accepts", [])]
        return s["type"].lower() == mount(p) and (not acc or p["category"].lower() in acc)

    for rel, p in parts:
        if mount(p) and not any(takes(s, p) for _, q in parts for s in q.get("sockets", [])):
            problems.append(f"{p['name']} ({rel}) fits nowhere: no part has a \"{mount(p)}\" socket that takes {p['category'].lower()} parts")
    for rel, q in parts:
        for s in q.get("sockets", []):
            if s["type"].lower() not in lib.socket_types:
                problems.append(f"{q['name']} ({rel}): socket \"{s['name']}\" has an unknown type \"{s['type']}\"")
            elif not any(takes(s, p) for _, p in parts):
                problems.append(f"{q['name']} ({rel}): socket \"{s['name']}\" takes nothing: no part mounts on \"{s['type']}\"")
    return problems


# ---- drawing ---------------------------------------------------------------------------------------------------

def color_of(c, accent):
    if isinstance(c, list):
        return tuple(c)
    c = str(c).lower()
    if c == "accent":
        return accent
    if c.startswith("#"):
        return tuple(int(c[i:i + 2], 16) / 255 for i in (1, 3, 5))
    return COLORS.get(c, (1, 0, 1))


def triangles(boxes, accent):
    import numpy as np
    out = []
    corners = np.array([[x, y, z] for x in (-0.5, 0.5) for y in (-0.5, 0.5) for z in (-0.5, 0.5)])
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    r45 = mat_rot([0, 0, 45])
    for (shape, size, pos, R, col, alpha, tag) in boxes:
        c = np.array(color_of(col, accent))
        copies = [(R, c)] + ([(mm(R, r45), c * 0.93)] if shape == "rod" else [])
        for (Rm, cc) in copies:
            P = (np.array(Rm) @ (corners * np.array(size)).T).T + np.array(pos)
            centre = np.array(pos)
            for f in faces:
                a, b, q, d = (P[i] for i in f)
                n = np.cross(b - a, d - a)
                ln = np.linalg.norm(n)
                if ln < 1e-12:
                    continue
                n = n / ln
                if np.dot(n, (a + q) / 2 - centre) < 0:
                    n = -n
                shade = cc * (1.3 if tag == "glow" else 1.0)
                out.append((a, b, q, n, shade))
                out.append((a, q, d, n, shade))
    return out


def render(boxes, markers, accent, size=240, view=(-35, 25)):
    import numpy as np
    from PIL import Image
    sun = np.array([-0.4, 0.8, 0.3])
    sun = sun / np.linalg.norm(sun)
    T = triangles(boxes, accent)
    yaw, elev = map(math.radians, view)
    fwd = -np.array([math.sin(yaw) * math.cos(elev), math.sin(elev), math.cos(yaw) * math.cos(elev)])
    right = np.cross(fwd, [0, 1, 0])
    right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    pts = [p for t in T for p in t[:3]] + [np.array(m[0]) for m in markers]
    if not pts:
        pts = [np.zeros(3)]
    pts = np.array(pts)
    sx, sy = pts @ right, pts @ up
    span = max(sx.max() - sx.min(), sy.max() - sy.min(), 0.08) * 1.2
    cx, cy = (sx.max() + sx.min()) / 2, (sy.max() + sy.min()) / 2
    scale = size / span
    img = np.ones((size, size, 3)) * np.array([0.36, 0.42, 0.5])
    zb = np.full((size, size), 1e9)

    def project(p):
        return np.array([(p @ right - cx) * scale + size / 2, size / 2 - (p @ up - cy) * scale, p @ fwd])

    for (a, b, c, n, colr) in T:
        light = colr * (0.45 + 0.55 * max(float(np.dot(n, sun)), 0) + 0.12 * (1 - abs(n[1])))
        P = [project(p) for p in (a, b, c)]
        xs, ys = [p[0] for p in P], [p[1] for p in P]
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
    im = Image.fromarray((img * 255).astype(np.uint8))
    return im, [(project(np.array(p))[:2], col, label) for (p, col, label) in markers]


def part_markers(p):
    marks = []
    for s in p.get("sockets", []):
        marks.append((s.get("pos", [0, 0, 0]), SOCKET_COLORS.get(s["type"].lower(), (1, 1, 1)), s["name"]))
    for m in p.get("muzzles", []):
        marks.append((m, (1.0, 0.1, 0.1), ""))
    if "hand" in p:
        marks.append((p["hand"], (1.0, 0.8, 0.6), "hand"))
    return marks


def main(argv):
    args, opts, i = [], argv, 0
    while i < len(argv):
        a = argv[i]
        if a == "--only":
            i += 1
            while i < len(argv) and not argv[i].startswith("--"):
                i += 1
            continue
        if a in ("--category", "--cols", "--size"):
            i += 2
            continue
        if not a.startswith("--"):
            args.append(a)
        i += 1
    folder = args[0] if args else os.path.join(ROOT, "Parts")
    out = args[1] if len(args) > 1 else "parts.png"

    def opt(name, default=None):
        return opts[opts.index(name) + 1] if name in opts and opts.index(name) + 1 < len(opts) else default

    lib = Library(folder)
    errors = list(lib.errors)
    entries = []  # (rel, name, label, boxes, markers, accent)
    resolved = []
    files = list(lib.part_files)
    if "--templates" in opts:
        for f in sorted(glob.glob(os.path.join(folder, "_Template", "*.json"))):
            rel = os.path.relpath(f, folder).replace("\\", "/")
            try:
                files.append((rel, lenient_json(open(f, encoding="utf-8").read(), rel)))
            except PartError as e:
                errors.append(str(e))
    if "--fragments" in opts:
        for key, (rel, data) in sorted(lib.fragments.items()):
            try:
                bx = lib.boxes([{"use": data["name"]}], rel)
                entries.append((rel, data["name"], "fragment", bx, [], RARITY["common"]))
            except PartError as e:
                errors.append(str(e))
    else:
        for rel, data in files:
            try:
                p = lib.resolve(data, rel)
                bx = lib.boxes(p.get("model"), f"{rel} ('{p.get('name')}')")
                resolved.append((rel, p))
                entries.append((rel, p["name"], f"{p['category'].lower()}, {p['rarity'].lower()}", bx,
                                [] if "--no-sockets" in opts else part_markers(p), RARITY.get(str(p["rarity"]).lower(), (1, 1, 1))))
            except (PartError, KeyError, TypeError) as e:
                errors.append(str(e) if isinstance(e, PartError) else f"{rel}: missing or bad key {e}")
    for e in errors:
        print("ERROR:", e)
    if "--lint" in opts:
        problems = lint([(r, p) for r, p in resolved if not r.startswith("_")], lib)
        for x in problems:
            print("PROBLEM:", x)
        print(f"{len(resolved)} part(s), {len(lib.fragments)} fragment(s), {len(lib.bases)} base(s): {len(errors)} error(s), {len(problems)} problem(s)")
        return 1 if errors or problems else 0

    only = []
    if "--only" in opts:
        i = opts.index("--only") + 1
        while i < len(opts) and not opts[i].startswith("--"):
            only.append(opts[i].lower())
            i += 1
    cat = (opt("--category") or "").lower()
    entries = [e for e in entries if (not only or any(o in e[1].lower() or o in e[0].lower() for o in only)) and (not cat or e[2].startswith(cat))]
    from PIL import Image, ImageDraw
    load_colors()
    S = int(opt("--size", 240))
    cols = max(1, min(int(opt("--cols", 6)), len(entries) or 1))
    rows = max(1, (len(entries) + cols - 1) // cols)
    sheet = Image.new("RGB", (cols * S, rows * (S + 28)), (30, 30, 30))
    d = ImageDraw.Draw(sheet)
    for i, (rel, name, label, bx, marks, accent) in enumerate(entries):
        im, pm = render(bx, marks, accent, S)
        x, y = (i % cols) * S, (i // cols) * (S + 28)
        sheet.paste(im, (x, y))
        for (px, py), col, lab in pm:
            c = tuple(int(v * 255) for v in col)
            d.rectangle([x + px - 3, y + py - 3, x + px + 3, y + py + 3], outline=(0, 0, 0), fill=c)
            if lab:
                d.text((x + px + 5, y + py - 6), lab, fill=c)
        d.text((x + 4, y + S + 2), name, fill=(255, 255, 255))
        d.text((x + 4, y + S + 14), f"{label} - {len(bx)} box(es)", fill=(170, 170, 170))
    sheet.save(out)
    print(f"{len(entries)} rendered to {out}" + (f", {len(errors)} error(s)" if errors else ""))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
