"""Finds model boxes that float: boxes not connected to the rest of their part, or of an assembled build.

Usage:
    python3 tools/parts/floaters.py [PARTS_DIR] [--tol METRES] [--receiver NAME] [--verbose]

Two boxes touch when they overlap or the gap between them is at most --tol (default 0.0101: 1 cm and 0.1 mm for rounding), tested as oriented
boxes (separating axes; rods as boxes; a contoured shape such as a tube or an arc by the proxy boxes tools/parts/shapes.py
gives it, as the self-test does with PartShapes.Proxies). A model's boxes are grouped into connected pieces and every box outside the main
piece is reported (a part on its own: the piece with the most volume; a build: the piece holding the grip's first box).

What is checked (the same as the self-test section "Model connectivity", Source/App/SelfTestModelConnectivity.cs):
  1. every part on its own, drawn as on an empty grip: no "irons" boxes, no "with:SOCKET" boxes;
  2. every root grip assembled: empty; a receiver (--receiver, default the Workhorse) in receiver_1; one in every
     receiver socket; that plus an optic; that plus every other socket filled (stock, foregrip, hopper, utility);
  3. every receiver with every action in its action socket (on the Rifle Grip); and (3b) actions placed under the
     receiver or at its front don't cut into the grip's handle, a foregrip under the gun, or a magazine that leaves
     them room in front of the handle (Rifle Grip, Service Pistol Grip; the self-test section "Action placement");
  4. every standard build in Parts/Builds.
A part sits at the socket point its "placement" names (an action's "under" or "front": a receiver action socket's
"points"), else at the socket's "pos", as in BuildModel. The grip rules match BuildModel: "irons" boxes only with a receiver fitted and no optic, "with:SOCKET" boxes only while
that socket holds a part. ALLOWED lists the intentional separated pieces (keep it in step with the self-test's list).
Exit code 1 if anything not allowed floats.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import viz  # noqa: E402  (the same part reader the contact sheet uses)
import shapes  # noqa: E402  (the contoured shapes' proxy boxes, mirrored from Source/Zombies/PartShapes.cs)

# Intentional separated pieces: part name -> why. Their boxes may float when the part is on its own; in a build the
# part must still connect (that is what the reason says joins them).
ALLOWED = {
    "Bastille LMG Receiver": "cooling fins round the barrel socket: the fitted barrel runs through them",
    "Sawmill Receiver": "cooling fins round the barrel socket: the fitted barrel runs through them",
    "Hammerfall Autocannon Receiver": "cooling fins round the barrel socket: the fitted barrel runs through them",
    "Barbed Wire Wrap": "two wraps of wire round the hilt's handle: the hilt joins them",
    "Nail Ring": "nails driven into the hilt's handle: the hilt joins them",
}
# Grips whose stacked receiver rows are allowed to float on each other (part name -> why).
ALLOWED_ROWS = {
    "Push Cart": "receivers piled in the basket in rows 11 cm apart; only the bottom row stands on the floor",
}


def mmul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def m_trans(p):
    return [[1, 0, 0, p[0]], [0, 1, 0, p[1]], [0, 0, 1, p[2]], [0, 0, 0, 1]]


def m_rot(R):
    return [[R[0][0], R[0][1], R[0][2], 0], [R[1][0], R[1][1], R[1][2], 0], [R[2][0], R[2][1], R[2][2], 0], [0, 0, 0, 1]]


def m_scale(s):
    return [[s[0], 0, 0, 0], [0, s[1], 0, 0], [0, 0, s[2], 0], [0, 0, 0, 1]]


IDENT = m_scale([1, 1, 1])


class Box:
    """An oriented box: centre, three unit axes, half extents. The label says where it came from."""

    def __init__(self, M, label, tag="", part=""):
        # M maps the unit cube (-0.5..0.5) to the box (column vectors).
        self.c = [M[0][3], M[1][3], M[2][3]]
        self.ax, self.h = [], []
        for j in range(3):
            v = [M[0][j], M[1][j], M[2][j]]
            n = math.sqrt(sum(x * x for x in v)) or 1e-9
            self.ax.append([x / n for x in v])
            self.h.append(n / 2)
        self.label, self.tag, self.part = label, tag, part
        self.proxies = None  # a contoured shape: the boxes that stand for its volume (shapes.proxies)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def touches(a, b, tol):
    if not obb_touches(a, b, tol):
        return False
    if a.proxies is None and b.proxies is None:
        return True
    pa = a.proxies if a.proxies is not None else [a]
    pb = b.proxies if b.proxies is not None else [b]
    return any(obb_touches(x, y, tol) for x in pa for y in pb)


def m_rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return [[1, 0, 0, 0], [0, c, -s, 0], [0, s, c, 0], [0, 0, 0, 1]]


def obb_touches(a, b, tol):
    t = [b.c[i] - a.c[i] for i in range(3)]
    for L in a.ax + b.ax + [cross(x, y) for x in a.ax for y in b.ax]:
        n = math.sqrt(dot(L, L))
        if n < 1e-6:
            continue
        L = [x / n for x in L]
        ra = sum(a.h[i] * abs(dot(a.ax[i], L)) for i in range(3))
        rb = sum(b.h[i] * abs(dot(b.ax[i], L)) for i in range(3))
        if abs(dot(t, L)) > ra + rb + tol:
            return False
    return True


def pieces(boxes, tol):
    """Connected groups of boxes (lists of indices)."""
    parent = list(range(len(boxes)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            if find(i) != find(j) and touches(boxes[i], boxes[j], tol):
                parent[find(i)] = find(j)
    groups = {}
    for i in range(len(boxes)):
        groups.setdefault(find(i), []).append(i)
    return list(groups.values())


def floaters(boxes, tol, anchor=None):
    """Boxes outside the main piece: the piece holding box <anchor>, else the one with the most volume."""
    g = pieces(boxes, tol)
    if not g:
        return []
    if anchor is not None:
        main = next(grp for grp in g if anchor in grp)
    else:
        main = max(g, key=lambda grp: sum(boxes[i].h[0] * boxes[i].h[1] * boxes[i].h[2] for i in grp))
    return [boxes[i] for grp in g if grp is not main for i in grp]


# ---- parts and builds ------------------------------------------------------------------------------------------

class Lib:
    def __init__(self, folder):
        self.v = viz.Library(folder)
        self.parts = {}
        for rel, data in self.v.part_files:
            p = viz.lower_keys(self.v.resolve(data, rel))
            p["_rel"] = rel
            p["_boxes"] = self.v.boxes(p.get("model"), rel)
            self.parts[str(p["name"]).strip().lower()] = p
        self.scales = {}
        reg = viz.lower_keys(viz.lenient_json(open(os.path.join(folder, "_Registry.json"), encoding="utf-8").read(), "_Registry.json"))
        for b in reg.get("bores", []):
            b = viz.lower_keys(b)
            self.scales[b["name"].lower()] = b.get("barrelscale", [1, 1, 1])

    def part(self, name):
        return self.parts[name.strip().lower()]

    def part_boxes(self, p, M=IDENT, scale=(1, 1, 1), irons=False, filled=(), label=None):
        """The part's drawn boxes: "irons" only if <irons>, "with:SOCKET" only if SOCKET is in <filled>."""
        out = []
        for i, (shape, size, pos, R, col, alpha, tag, st) in enumerate(p["_boxes"]):
            tag = str(tag or "").lower()
            if tag == "irons" and not irons:
                continue
            if tag.startswith("irons:") and not (irons and tag[6:] in [f.lower() for f in filled]):
                continue
            if tag.startswith("with:") and tag[5:] not in [f.lower() for f in filled]:
                continue
            outer = mmul(M, mmul(m_scale(scale), mmul(m_trans(pos), m_rot(R))))
            m = mmul(outer, m_scale(size))
            box = Box(m, f"{label or p['name']} box #{i + 1} {shape} {list(size)} at {list(pos)}" + (f" [{tag}]" if tag else ""), tag, p["name"])
            if shapes.is_shaped({"shape": shape}):
                box.proxies = [Box(mmul(outer, mmul(m_trans(c), mmul(m_rot_x(rx), m_scale(sz)))), box.label, tag, p["name"])
                               for (c, sz, rx) in shapes.proxies(dict(st, shape=shape, size=[abs(x) for x in size]))]
            out.append(box)
        return out


def parse_build(text):
    """Grip{sock:Part{...},...} -> (name, {socket: subtree})."""
    i = 0

    def node():
        nonlocal i
        j = i
        while i < len(text) and text[i] not in "{},":
            i += 1
        name, kids = text[j:i].strip(), {}
        if i < len(text) and text[i] == "{":
            i += 1
            while text[i] != "}":
                k = text.index(":", i)
                sock = text[i:k].strip()
                i = k + 1
                kids[sock] = node()
                if text[i] == ",":
                    i += 1
            i += 1
        return (name, kids)

    return node()


def _has(lib, tree, cat):
    name, kids = tree
    return lib.part(name)["category"].lower() == cat or any(_has(lib, k, cat) for k in kids.values())


def assemble(lib, tree):
    """Every drawn box of a build tree in gun space, as BuildModel draws it (before the hand anchor shift)."""
    has_receiver = any(lib.part(k[0])["category"].lower() == "receiver" for k in tree[1].values())
    irons = has_receiver and not _has(lib, tree, "optic")
    out = []

    def walk(t, M, recv_bore, path):
        name, kids = t
        p = lib.part(name)
        cat = p["category"].lower()
        if cat == "receiver":
            rb = viz.lower_keys(p.get("receiver", {}))
            recv_bore = rb.get("barrelscale") or lib.scales.get(str(rb.get("bore", "rifle")).lower(), [1, 1, 1])
        scale = [1, 1, 1]
        if cat == "barrel" and recv_bore:
            drawn = lib.scales.get(str(p.get("sizedfor", "rifle")).lower(), [1, 1, 1])
            scale = [recv_bore[k] / drawn[k] for k in range(3)]
        out.extend(lib.part_boxes(p, M, scale, irons, list(kids), f"{path}{p['name']}"))
        for s in p.get("sockets", []):
            s = viz.lower_keys(s)
            if s["name"] in kids:
                pos = placed_at(s, lib.part(kids[s["name"]][0]))
                pos = [pos[k] * scale[k] for k in range(3)]
                SM = mmul(M, mmul(m_trans(pos), m_rot(viz.mat_rot(s.get("rot", [0, 0, 0])))))
                walk(kids[s["name"]], SM, recv_bore, f"{path}{s['name']}:")

    walk(tree, IDENT, None, "")
    return out


def placed_at(sock, part):
    """Where <part> sits in socket <sock>: the socket point its "placement" names, else the socket's "pos" (BuildModel)."""
    place = str(part.get("placement", "") or "").strip().lower()
    points = {str(k).lower(): v for k, v in (sock.get("points") or {}).items()}
    return points.get(place, sock.get("pos", [0, 0, 0])) if place and place != "side" else sock.get("pos", [0, 0, 0])


def _inside(b, p):
    d = [p[k] - b.c[k] for k in range(3)]
    return all(abs(dot(d, b.ax[i])) <= b.h[i] + 1e-5 for i in range(3))


def _zr(bs):
    return (min(b.c[2] - sum(b.h[i] * abs(b.ax[i][2]) for i in range(3)) for b in bs),
            max(b.c[2] + sum(b.h[i] * abs(b.ax[i][2]) for i in range(3)) for b in bs))


def action_clearance(lib, order):
    """Actions placed "under" or "front" vs the handle, foregrips that hang under the gun and every magazine that leaves
    room in front of the handle, on the Rifle Grip and the Service Pistol Grip (the self-test section "Action placement").
    Returns (problems, checks, exempt)."""
    receivers = [p for p in order if p["category"].lower() == "receiver"]
    placed = [p for p in order if p["category"].lower() == "action" and str(p.get("placement", "side")).lower() != "side"]
    feeds = [p for p in order if p["category"].lower() == "feed" and str(p.get("mount", "feed")).lower() == "feed"]
    fores = [p for p in order if p["category"].lower() == "grip" and str(p.get("mount", "")).lower() == "foregrip"
             and all(pos[1] + size[1] / 2 <= 0.001 for (_, size, pos, *_rest) in p["_boxes"])]  # as the self-test: unturned extents
    problems = checks = exempt = 0
    for gname in ("Rifle Grip", "Service Pistol Grip"):
        g = lib.part(gname)
        socks = [viz.lower_keys(s) for s in g["sockets"]]
        rs = next(s["name"] for s in socks if s["type"] == "receiver")
        fs = next((s["name"] for s in socks if s["type"] == "foregrip"), None)
        handle = [b for b in lib.part_boxes(g) if _inside(b, g.get("hand", [0, 0, 0]))]
        front = _zr(handle)[0]
        under = [b for f in (fores if fs else []) for b in assemble(lib, (gname, {fs: (f["name"], {})})) if b.part == f["name"]]
        for r in receivers:
            bore = viz.lower_keys(r["receiver"])["bore"]
            mags = [(f["name"], [b for b in assemble(lib, (gname, {rs: (r["name"], {"feed": (f["name"], {})})})) if b.part == f["name"]])
                    for f in feeds if not f.get("bores") or bore in [x.lower() for x in f["bores"]]]
            for a in placed:
                ab = [b for b in assemble(lib, (gname, {rs: (r["name"], {"action": (a["name"], {})})})) if b.part == a["name"]]
                checks += 1
                length = _zr(ab)[1] - _zr(ab)[0]
                hits = [w for w, bs in (("handle", handle), ("foregrip", under)) if any(touches(x, y, -0.001) for x in ab for y in bs)]
                for fn, fb in mags:
                    if any(touches(x, y, -0.001) for x in ab for y in fb):
                        if front - _zr(fb)[1] < length + 0.001:
                            exempt += 1  # reaches back to the handle: no room for a trigger group in front of it
                        else:
                            hits.append(fn)
                if hits:
                    print(f"{a['name']} ({a.get('placement')}) on the {r['name']} [{gname}] cuts into: {', '.join(hits)}")
                    problems += 1
    return problems, checks, exempt


def grip_configs(lib, g, recv):
    """The grip assemblies checked: (label, children)."""
    socks = [viz.lower_keys(s) for s in g.get("sockets", [])]
    recvs = [s["name"] for s in socks if s["type"] == "receiver"]
    one = {recvs[0]: (recv, {})}
    every = {n: (recv, {}) for n in recvs}
    optic = {s["name"]: ("Red Dot", {}) for s in socks if s["type"] == "optic"}
    fill = {"stock": "Fixed Stock", "foregrip": "Vertical Foregrip", "hopper": "Manifold Hopper", "utility": "Laser Sight"}
    rest = {s["name"]: (fill[s["type"]], {}) for s in socks if s["type"] in fill and fill[s["type"]].lower() in lib.parts}
    out = [("empty", {}), ("one receiver", one), ("every receiver", every)]
    if optic:
        out += [("every receiver + optic", {**every, **optic})]
    if rest:
        out += [("everything", {**every, **optic, **rest})]
    return out


def report(title, fl):
    by = {}
    for b in fl:
        by.setdefault(b.label.split(" box #")[0], []).append(b)
    print(f"{title}: {len(fl)} floating box(es)")
    for part, bs in by.items():
        print(f"      {part}: " + ("; ".join("box " + x.label.split(' box ')[1] for x in bs) if len(bs) <= 2 else f"{len(bs)} boxes"))


def main(argv):
    folder, tol, recv = os.path.join(viz.ROOT, "Parts"), 0.0101, "Workhorse Receiver"
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--tol":
            tol, i = float(argv[i + 1]), i + 1
        elif a == "--receiver":
            recv, i = argv[i + 1], i + 1
        elif not a.startswith("--"):
            folder = a
        i += 1
    lib = Lib(folder)
    problems = 0
    print(f"== 1. parts on their own (tolerance {tol * 100:.1f} cm) ==")
    order = sorted(lib.parts.values(), key=lambda p: (p["category"].lower() != "grip", p["category"].lower(), p["name"]))
    for p in order:
        fl = floaters(lib.part_boxes(p), tol)
        if fl:
            why = ALLOWED.get(p["name"])
            report(f"{p['category']:<9} {p['name']} ({p['_rel']})" + (f"  [allowed: {why}]" if why else ""), fl)
            problems += 0 if why else 1
    print(f"\n== 2. assembled grips (receiver: {recv}) ==")
    roots = [p for p in order if p["category"].lower() == "grip" and "frame" in p]
    for g in roots:
        for what, kids in grip_configs(lib, g, recv):
            # an allowed part's own separated pieces (fins with no barrel through them yet) don't count here
            fl = [b for b in floaters(assemble(lib, (g["name"], kids)), tol, 0) if b.part not in ALLOWED]
            rows = ALLOWED_ROWS.get(g["name"])
            if rows:
                # only the upper rows may float (the receivers themselves, not what they carry or the grip's own boxes)
                bottom = {viz.lower_keys(s)["name"] for s in g["sockets"] if viz.lower_keys(s)["type"] == "receiver"
                          and abs(viz.lower_keys(s)["pos"][1] - min(viz.lower_keys(x)["pos"][1] for x in g["sockets"] if viz.lower_keys(x)["type"] == "receiver")) < 1e-4}
                fl = [b for b in fl if not (b.label.split(":")[0].startswith("receiver_") and b.label.split(":")[0] not in bottom)]
            if fl:
                report(f"{g['name']} [{what}]", fl)
                problems += 1
    print("\n== 3. every receiver x every action (on the Rifle Grip) ==")
    receivers = [p for p in order if p["category"].lower() == "receiver"]
    actions = [p for p in order if p["category"].lower() == "action"]
    for r in receivers:
        for a in actions:
            tree = ("Rifle Grip", {"receiver_1": (r["name"], {"action": (a["name"], {})})})
            fl = [b for b in floaters(assemble(lib, tree), tol, 0) if b.part not in ALLOWED]
            if fl:
                report(f"{a['name']} ({a.get('placement') or 'side'}) on the {r['name']}", fl)
                problems += 1
    print(f"   {len(receivers)} receivers x {len(actions)} actions checked")
    print("\n== 3b. under/front actions clear the handle, foregrips and magazines ==")
    bad, checks, exempt = action_clearance(lib, order)
    problems += bad
    print(f"   {checks} checks; {exempt} overlaps with feeds that reach back to the handle (allowed)")
    print("\n== 4. standard builds ==")
    for f in sorted(os.listdir(os.path.join(folder, "Builds"))):
        d = viz.lower_keys(viz.lenient_json(open(os.path.join(folder, "Builds", f), encoding="utf-8").read(), f))
        fl = floaters(assemble(lib, parse_build(d["build"])), tol, 0)
        if fl:
            report(f"{d['name']} ({f})", fl)
            problems += 1
    print(f"\n{problems} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
