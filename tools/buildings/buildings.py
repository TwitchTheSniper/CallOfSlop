#!/usr/bin/env python3
"""Building tools for Call of Slop (see Buildings/README.md). Run from the repository root:

  python3 tools/buildings/buildings.py validate [NAME...]       check every building (or the named ones)
  python3 tools/buildings/buildings.py preview NAME [OUT.png]   floor plans + cutaway views of one building
        [--variant '{"palette": "white", "mirror": true}'] [--seed N]
  python3 tools/buildings/buildings.py sheet [OUT.png]          a contact sheet of every building
  python3 tools/buildings/buildings.py anchors NAME X Z [YAW]   where a building's anchors and doors land on a map

Validation builds each building the way the game does (bldgen.py, a port of Source/Buildings) in its default form and a
few variants (mirrored, turned, other seeds, palettes, floor counts), and checks: the file parses; rooms don't overlap;
every opening sits inside a wall; stairs fit and land in a room; every room can be reached from the front door (through
doors, arches, open-plan joins and stairs); no solid furniture stands in a doorway; furniture stays inside its room;
every prop name exists. The game's self-test ("Buildings" section) runs the same checks and more (bots, doors, glass).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bldgen  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


# ============================================================================== checks

def reachable_rooms(gen, plan):
    """Room graph BFS from the front door's room. Returns (all rooms, reached rooms, front room) as (floor, index) keys."""
    nodes = {(f, i) for f in range(len(gen.floors)) for i in range(len(gen.floors[f]["rooms"]))}
    edges = {n: set() for n in nodes}

    def link(a, b):
        edges[a].add(b)
        edges[b].add(a)
    start = None
    for a in gen.openings:
        if a["o"]["kind"] not in ("door", "arch", "hole"):
            continue
        s, f = a["s"], a["f"]
        if a["o"]["kind"] == "hole" and a["bottom"] > 1.2:
            continue  # too high to climb through
        if s.neg >= 0 and s.pos >= 0:
            link((f, s.neg), (f, s.pos))
        elif a["o"]["kind"] in ("door", "arch") and f == 0:
            room = (f, s.neg if s.neg >= 0 else s.pos)
            if start is None or a["o"].get("front"):
                start = room
    for f in range(len(gen.floors)):
        rooms = gen.floors[f]["rooms"]
        for i in range(len(rooms)):
            for j in range(len(rooms)):
                if i != j and gen.joined(f, i, j):
                    # joined only if they actually touch
                    if any(bldgen.r_overlaps(bldgen.r_inset(a, -0.01), b) for a in rooms[i]["rects"] for b in rooms[j]["rects"]):
                        link((f, i), (f, j))
    for s, f, r, bottom, top in gen.stair_plans:
        a = (f, gen.room_at(f, *bottom))
        if f + 1 < len(gen.floors):
            b = (f + 1, gen.room_at(f + 1, *top))
            if a[1] >= 0 and b[1] >= 0:
                link(a, b)
    if start is None:
        return nodes, set(), None
    seen = {start}
    todo = [start]
    while todo:
        n = todo.pop()
        for m in edges[n]:
            if m not in seen:
                seen.add(m)
                todo.append(m)
    return nodes, seen, start


def check_plan(gen, plan, label):
    problems = []
    nodes, seen, start = reachable_rooms(gen, plan)
    if start is None:
        problems.append(f"{label}: no exterior door on the ground floor")
    else:
        for (f, i) in sorted(nodes - seen):
            problems.append(f"{label}: room \"{gen.floors[f]['rooms'][i]['id']}\" (floor {f}) can't be reached from the front door")
    rooms = {(r["floor"], r["id"]): r for r in plan.rooms}
    for p in plan.props:
        if p["room"] == "exterior" or p["fp"] is None:
            continue
        f = None
        for r in plan.rooms:
            if r["id"] == p["room"] and abs(r["floorY"] - (p["local"][1] - 0)) < 50:
                pass
        # Find the room by id and height.
        cand = [r for r in plan.rooms if r["id"] == p["room"] and r["floorY"] - 0.05 <= p["local"][1] <= r["ceilY"] + 0.05]
        if not cand:
            problems.append(f"{label}: prop {p['def'].name} has no room")
            continue
        r = cand[0]
        fp = p["fp"]
        f = r["floor"]
        room = [x for x in gen.floors[f]["rooms"] if x["id"] == r["id"]][0]
        corners = [(fp[0], fp[1]), (fp[2], fp[1]), (fp[0], fp[3]), (fp[2], fp[3]), ((fp[0] + fp[2]) / 2, (fp[1] + fp[3]) / 2)]
        inside = all(any(bldgen.r_contains(a, x, z, 0.06) for a in room["rects"]) for x, z in corners)
        t = gen.t
        for s in gen.segs[f]:
            band = (s.a - t / 2, s.c - t / 2, s.b + t / 2, s.c + t / 2) if s.along_x else (s.c - t / 2, s.a - t / 2, s.c + t / 2, s.b + t / 2)
            if bldgen.r_overlaps(fp, band, 0.06):
                inside = False
        if not inside:
            problems.append(f"{label}: {p['def'].name} in room \"{r['id']}\" pokes out of the room or into a wall ({fp[0]:.2f}, {fp[1]:.2f}, {fp[2]:.2f}, {fp[3]:.2f})")
        if p["def"].solid and p["local"][1] - r["floorY"] < 0.3:
            for z in r["zones"]:
                if bldgen.r_overlaps(fp, z, 0.06):
                    problems.append(f"{label}: solid {p['def'].name} stands in a doorway of room \"{r['id']}\"")
                    break
    return problems


def variants_of(d):
    vs = [({}, 0, 0), ({"mirror": True}, 0, 1), ({}, 7, 2), ({}, 12345, 3)]
    for name, _ in d.palettes[1:]:
        vs.append(({"palette": name}, 3, 0))
    if d.max_floors > d.min_floors:
        vs.append(({"floors": d.max_floors if d.max_floors < 6 else d.min_floors + 2}, 0, 0))
        vs.append(({"floors": d.min_floors}, 0, 1))
    vs.append(({"furnish": False, "doors": "open"}, 0, 0))
    return vs


def validate(lib, names):
    errors = list(lib.errors)
    total = 0
    targets = [lib.find(n) for n in names] if names else list(lib.buildings.values())
    for n, d in zip(names or [None] * len(targets), targets):
        if d is None:
            errors.append(f"no building called \"{n}\"")
            continue
        for variant, seed, q in variants_of(d):
            label = f"{d.name} {json.dumps(variant)} seed {seed} turn {q * 90}"
            try:
                gen = bldgen.Generator(lib, d, variant, seed, (0, 0, 0), q)
                plan = gen.run()
            except bldgen.BuildingError as ex:
                errors.append(f"{label}: {ex}")
                continue
            errors += check_plan(gen, plan, label)
            if not variant and not seed and not q:
                total += 1
                furn = sum(1 for p in plan.props if p["room"] != "exterior")
                solid = sum(1 for p in plan.props if p["def"].solid)
                print(f"  {d.name:22} {d.category:11} {plan.floors} floor(s), {len(plan.rooms):2} rooms, {len(plan.shell):4} boxes, {len(plan.doors):2} doors, "
                      f"{len(plan.windows):2} windows, {furn:3} props ({solid} solid), {len(plan.waypoints)} waypoints"
                      + (f"; notes: {len(plan.notes)}" if plan.notes else ""))
                for note in plan.notes:
                    print("      note:", note)
    for e in errors:
        print("ERROR:", e)
    print(f"{total} building(s), {len(lib.rules)} furnishing rule(s), {len(errors)} error(s)")
    return 1 if errors else 0


# ============================================================================== pictures

def shade(rgb, k):
    return tuple(max(0, min(255, int(c * 255 * k))) for c in rgb)


def boxes_for(plan, clip_y=None, min_y=None, with_props=True):
    out = []
    for b in plan.shell:
        x0, y0, z0, x1, y1, z1, rgb, layer, tag = b
        out.append((x0, y0, z0, x1, y1, z1, rgb, 1.0))
    for dd in plan.doors:
        b = dd["box"]
        out.append(b + (dd["color"], 1.0))
    for w in plan.windows:
        b = w["box"]
        out.append(b + ((0.62, 0.8, 0.9), 0.4))
    if with_props:
        for p in plan.props:
            out += prop_boxes(p)
    res = []
    for x0, y0, z0, x1, y1, z1, rgb, a in out:
        if clip_y is not None:
            if y0 >= clip_y:
                continue
            y1 = min(y1, clip_y)
        if min_y is not None and y1 <= min_y:
            continue
        res.append((x0, y0, z0, x1, y1, z1, rgb, a))
    return res


def prop_boxes(p):
    d = p["def"]
    out = []
    a = math.radians(p["yaw"])
    cs, sn = math.cos(a), math.sin(a)
    s = p["scale"]
    px, py, pz = p["pos"]
    for part in d.parts:
        sz = part["size"]
        off = part.get("offset", [0, sz[1] / 2, 0])
        try:
            rgb = bldgen.literal_color(part.get("color", "grey"), "part")
        except bldgen.BuildingError:
            rgb = (0.5, 0.5, 0.5)
        r = part.get("rotation") or [part.get("pitch", 0), part.get("yaw", 0), part.get("roll", 0)]
        R = bldgen.rot(*r)
        h = [sz[0] / 2, sz[1] / 2, sz[2] / 2]
        ext = [sum(abs(R[i][k]) * h[k] for k in range(3)) for i in range(3)]
        cx, cy, cz = off[0] * s, off[1] * s, off[2] * s
        ex, ey, ez = ext[0] * s, ext[1] * s, ext[2] * s
        wx = px + cx * cs + cz * sn
        wz = pz - cx * sn + cz * cs
        hx = abs(ex * cs) + abs(ez * sn)
        hz = abs(ex * sn) + abs(ez * cs)
        out.append((wx - hx, py + cy - ey, wz - hz, wx + hx, py + cy + ey, wz + hz, rgb, 1.0))
    return out


def iso(boxes, size=(900, 700), yaw=35.0, pitch=32.0, bg=(236, 232, 224)):
    """A z-buffered orthographic view of boxes (x0, y0, z0, x1, y1, z1, rgb, alpha), seen from the south-east and above."""
    import numpy as np
    from PIL import Image
    W, H = size
    if not boxes:
        return Image.new("RGB", size, bg)
    ya, pa = math.radians(yaw), math.radians(pitch)
    cy, sy, cp, sp = math.cos(ya), math.sin(ya), math.cos(pa), math.sin(pa)

    def proj(x, y, z):
        u = x * cy - z * sy
        w = x * sy + z * cy
        v = y * cp - w * sp
        return u, -v, w * cp + y * sp  # depth: larger is nearer the camera
    corners = [proj(x, y, z) for b in boxes for x in (b[0], b[3]) for y in (b[1], b[4]) for z in (b[2], b[5])]
    us = [c[0] for c in corners]
    vs = [c[1] for c in corners]
    sc = min((W - 30) / max(1e-6, max(us) - min(us)), (H - 30) / max(1e-6, max(vs) - min(vs)))
    ox = 15 - min(us) * sc + (W - 30 - (max(us) - min(us)) * sc) / 2
    oy = 15 - min(vs) * sc + (H - 30 - (max(vs) - min(vs)) * sc) / 2
    img = np.zeros((H, W, 3), dtype=np.float32)
    img[:] = np.array(bg, dtype=np.float32) / 255
    zbuf = np.full((H, W), -1e9, dtype=np.float32)
    sun = np.array([-0.4, 0.8, -0.3])
    sun /= np.linalg.norm(sun)
    glass = []

    def tri(p0, p1, p2, color, alpha):
        xs = [p0[0], p1[0], p2[0]]
        ys = [p0[1], p1[1], p2[1]]
        x0, x1 = max(0, int(min(xs))), min(W - 1, int(max(xs)) + 1)
        y0, y1 = max(0, int(min(ys))), min(H - 1, int(max(ys)) + 1)
        if x1 < x0 or y1 < y0:
            return
        den = (p1[1] - p2[1]) * (p0[0] - p2[0]) + (p2[0] - p1[0]) * (p0[1] - p2[1])
        if abs(den) < 1e-9:
            return
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        a = ((p1[1] - p2[1]) * (gx - p2[0]) + (p2[0] - p1[0]) * (gy - p2[1])) / den
        b = ((p2[1] - p0[1]) * (gx - p2[0]) + (p0[0] - p2[0]) * (gy - p2[1])) / den
        c = 1 - a - b
        m = (a >= -1e-4) & (b >= -1e-4) & (c >= -1e-4)
        if not m.any():
            return
        d = a * p0[2] + b * p1[2] + c * p2[2]
        zs = zbuf[y0:y1 + 1, x0:x1 + 1]
        win = m & (d > zs + 1e-4)
        if alpha >= 0.99:
            zs[win] = d[win]
            img[y0:y1 + 1, x0:x1 + 1][win] = color
        else:
            sub = img[y0:y1 + 1, x0:x1 + 1]
            sub[win] = sub[win] * (1 - alpha) + np.array(color) * alpha
    faces = []
    for x0, y0, z0, x1, y1, z1, rgb, alpha in boxes:
        quads = (((0, 1, 0), [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)]),
                 ((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]),
                 ((1, 0, 0), [(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)]),
                 ((0, 0, -1), [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)]),
                 ((-1, 0, 0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)]))
        for n, quad in quads:
            light = 0.55 + 0.45 * max(0.0, float(np.dot(n, sun))) + (0.1 if n[1] == 0 else 0)
            col = tuple(min(1.0, c * light) for c in rgb)
            pr = [proj(*q) for q in quad]
            pr = [(p[0] * sc + ox, p[1] * sc + oy, p[2]) for p in pr]
            (glass if alpha < 0.99 else faces).append((pr, col, alpha))
    for pr, col, alpha in faces:
        tri(pr[0], pr[1], pr[2], col, alpha)
        tri(pr[0], pr[2], pr[3], col, alpha)
    for pr, col, alpha in glass:
        tri(pr[0], pr[1], pr[2], col, alpha)
        tri(pr[0], pr[2], pr[3], col, alpha)
    return Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))


def floor_plan(gen, plan, f, size=520):
    from PIL import Image, ImageDraw
    img = Image.new("RGB", (size, size), (250, 250, 246))
    dr = ImageDraw.Draw(img, "RGBA")
    rects = [rc for fl in gen.floors for room in fl["rooms"] for rc in room["rects"]]
    x0 = min(r[0] for r in rects) - 2.5
    z0 = min(r[1] for r in rects) - 2.5
    x1 = max(r[2] for r in rects) + 2.5
    z1 = max(r[3] for r in rects) + 2.5
    sc = (size - 30) / max(x1 - x0, z1 - z0)

    def m(x, z):
        return (15 + (x - x0) * sc, 15 + (z - z0) * sc)
    lv = gen.level[f]
    for room in gen.floors[f]["rooms"]:
        fs = room["floor"] or gen.d.floor
        for rc in room["rects"]:
            dr.rectangle([m(rc[0], rc[1]), m(rc[2], rc[3])], fill=shade(gen.col(fs[0]), 1.05))
    for zr in [z for r in plan.rooms if r["floor"] == f for z in r["zones"]]:
        dr.rectangle([m(zr[0], zr[1]), m(zr[2], zr[3])], outline=(220, 120, 40, 160))
    for b in plan.shell:
        bx0, by0, bz0, bx1, by1, bz1, rgb, layer, tag = b
        if by0 <= lv + 1.0 <= by1 and tag in ("ext", "int", "lining", "stairs", "railing", "extra"):
            dr.rectangle([m(bx0, bz0), m(bx1, bz1)], fill=shade(rgb, 0.55) if tag in ("ext", "int") else shade(rgb, 0.8))
        elif tag == "stairs" and by0 <= lv + 0.2 and lv - 0.2 <= by1:
            dr.rectangle([m(bx0, bz0), m(bx1, bz1)], fill=shade(rgb, 0.8), outline=(0, 0, 0, 120))
    for p in plan.props:
        if p["fp"] is None or not (lv - 0.05 <= p["local"][1] < lv + 2.5):
            continue
        fp = p["fp"]
        dr.rectangle([m(fp[0], fp[1]), m(fp[2], fp[3])], fill=(150, 100, 60, 170) if p["def"].solid else (90, 150, 90, 110), outline=(60, 40, 20, 200))
    for dd in plan.doors:
        if dd["floor"] == f:
            b = dd["box"]
            dr.rectangle([m(b[0] - 0.05, b[2] - 0.05), m(b[3] + 0.05, b[5] + 0.05)], fill=(230, 120, 30))
            h = dd["hinge"]
            dr.ellipse([m(h[0] - 0.12, h[2] - 0.12), m(h[0] + 0.12, h[2] + 0.12)], fill=(120, 40, 0))
            o = dd["openDir"]
            dr.line([m(h[0], h[2]), m(h[0] + o[0] * 0.9, h[2] + o[1] * 0.9)], fill=(230, 120, 30), width=1)
    for w in plan.windows:
        if w["floor"] == f:
            b = w["box"]
            dr.rectangle([m(b[0] - 0.04, b[2] - 0.04), m(b[3] + 0.04, b[5] + 0.04)], fill=(70, 170, 220))
    for wp in plan.waypoints:
        if abs(wp[1] - lv) < 0.3:
            c = m(wp[0], wp[2])
            dr.ellipse([c[0] - 3, c[1] - 3, c[0] + 3, c[1] + 3], fill=(200, 30, 30))
    for room in gen.floors[f]["rooms"]:
        big = max(room["rects"], key=lambda r: (r[2] - r[0]) * (r[3] - r[1]))
        c = m((big[0] + big[2]) / 2, (big[1] + big[3]) / 2)
        dr.text((c[0] - 20, c[1] - 6), f"{room['id']}\n({room['type']})", fill=(20, 20, 60))
    dr.text((6, 2), f"floor {f}  (N up: -Z; 1 m = {sc:.0f} px)", fill=(0, 0, 0))
    return img


def preview(lib, name, out, variant=None, seed=0):
    from PIL import Image, ImageDraw
    d = lib.find(name)
    if d is None:
        print(f"no building called \"{name}\"")
        return 1
    gen = bldgen.Generator(lib, d, variant or {}, seed, (0, 0, 0), 0)
    plan = gen.run()
    panels = [iso(boxes_for(plan), (620, 520))]
    for f in range(plan.floors):
        clip = gen.level[f] + 1.5
        panels.append(iso(boxes_for(plan, clip_y=clip), (620, 520), yaw=35))
        panels.append(floor_plan(gen, plan, f))
    cols = 3
    rows = (len(panels) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * 640, rows * 540 + 30), (255, 255, 255))
    dr = ImageDraw.Draw(sheet)
    dr.text((10, 8), f"{d.name}: {d.theme}  |  {len(plan.shell)} boxes, {len(plan.doors)} doors, {len(plan.windows)} windows, {len(plan.props)} props", fill=(0, 0, 0))
    for i, p in enumerate(panels):
        sheet.paste(p, (10 + (i % cols) * 640, 30 + (i // cols) * 540))
    sheet.save(out)
    for n in plan.notes:
        print("note:", n)
    print(f"wrote {out}")
    return 0


def sheet(lib, out):
    from PIL import Image, ImageDraw
    items = list(lib.buildings.values())
    cols = 4
    rows = (len(items) + cols - 1) // cols
    img = Image.new("RGB", (cols * 420, rows * 360), (255, 255, 255))
    dr = ImageDraw.Draw(img)
    for i, d in enumerate(items):
        try:
            plan = bldgen.generate(lib, d)
            pic = iso(boxes_for(plan, with_props=False), (400, 320))
            pic2 = iso(boxes_for(plan, clip_y=plan.levels[0] + 1.5), (200, 160))
            pic.paste(pic2, (200, 160))
        except bldgen.BuildingError as ex:
            pic = Image.new("RGB", (400, 320), (255, 220, 220))
            ImageDraw.Draw(pic).text((10, 10), str(ex)[:300], fill=(0, 0, 0))
        img.paste(pic, ((i % cols) * 420 + 10, (i // cols) * 360 + 30))
        dr.text(((i % cols) * 420 + 10, (i // cols) * 360 + 10), f"{d.name} ({d.category})", fill=(0, 0, 0))
    img.save(out)
    print(f"wrote {out} ({len(items)} buildings)")
    return 0


def anchors(lib, name, x, z, yaw=0):
    d = lib.find(name)
    q = int(round(yaw / 90)) % 4
    plan = bldgen.generate(lib, d, {}, 0, (x, 0, z), q)
    for a in d.anchors:
        print("anchor", a)
    for dd in plan.doors:
        print(f"door floor {dd['floor']} {'front ' if dd['front'] else ''}at ({(dd['box'][0] + dd['box'][3]) / 2:.2f}, {(dd['box'][2] + dd['box'][5]) / 2:.2f}); "
              f"outside/inside points {tuple(round(v, 2) for v in dd['other'])} {tuple(round(v, 2) for v in dd['swing'])}")
    fp = plan.footprint
    print("footprint", [tuple(round(v, 2) for v in r) for r in fp])
    return 0


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0
    lib = bldgen.Library(REPO)
    cmd = argv[1]
    if cmd == "validate":
        return validate(lib, argv[2:])
    if cmd == "preview":
        args = argv[2:]
        variant, seed = None, 0
        if "--variant" in args:
            i = args.index("--variant")
            variant = json.loads(args[i + 1])
            del args[i:i + 2]
        if "--seed" in args:
            i = args.index("--seed")
            seed = int(args[i + 1])
            del args[i:i + 2]
        name = args[0]
        out = args[1] if len(args) > 1 else f"preview_{name.replace(' ', '')}.png"
        return preview(lib, name, out, variant, seed)
    if cmd == "sheet":
        return sheet(lib, argv[2] if len(argv) > 2 else "buildings_sheet.png")
    if cmd == "anchors":
        return anchors(lib, argv[2], float(argv[3]), float(argv[4]), float(argv[5]) if len(argv) > 5 else 0)
    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
