"""Renders contact sheets of the gun models and runs the same model checks as the C# self-test.

python3 viz.py            -> out/stock_<Class>.png, out/kitted_<Class>.png, out/optic_<Class>.png + checks
python3 viz.py --table    -> per-weapon part count and bounds (for the C# self-test comparison)
python3 viz.py --only NAME
"""
import sys, os, math, hashlib
from PIL import Image, ImageDraw, ImageFont
import gm

W_PANEL, H_SIDE, H_TOP = 620, 230, 110
BG = (24, 26, 30)
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")


def record(mesh, cut):
    out = []
    for p in mesh.parts:
        a = p.alpha
        if p.flags & gm.F_SHELL:
            a = p.alpha * (1 - cut)
            if a <= 0.02:
                continue
        clear = p.mode == 2 or a < 0.999
        lo, hi = p.bounds()
        out.append((lo, hi, bool(p.flags & gm.F_OPTIC), clear, p))
    return out


def hull(pts):
    pts = sorted(set(pts))
    if len(pts) <= 2:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def col(c, k=1.0):
    return tuple(max(0, min(255, int(255 * min(1, v) * k))) for v in c)


def draw_view(img, mesh, cut, ox, oy, S, view):
    d = ImageDraw.Draw(img, "RGBA")
    parts = [p for p in mesh.parts if not (p.flags & gm.F_SHELL and p.alpha * (1 - cut) <= 0.02)]
    if view == "side":  # seen from -X (the left side, like the gallery): z to the right, y up; far (+x) first
        key = lambda p: -sum(c[0] for c in p.corners()) / 8
        proj = lambda c: (ox + c[2] * S, oy - c[1] * S)
    else:  # top: z to the right, x down (right side of the gun at the bottom); low first
        key = lambda p: sum(c[1] for c in p.corners()) / 8
        proj = lambda c: (ox + c[2] * S, oy + c[0] * S)
    for p in sorted(parts, key=key):
        if p.sz > 2:  # laser beam
            continue
        poly = hull([tuple(round(v, 2) for v in proj(c)) for c in p.corners()])
        if len(poly) < 3:
            continue
        a = 255 if p.alpha >= 0.999 else int(255 * max(0.25, p.alpha))
        fill = col(p.c) + (a,)
        d.polygon(poly, fill=fill, outline=col(p.c, 0.55) + (min(255, a + 40),))


def panel(w, kit, title, font, S, scoped_note=""):
    mesh = gm.build(w, kit)
    img = Image.new("RGB", (W_PANEL, H_SIDE + H_TOP), BG)
    ox = W_PANEL * 0.66
    draw_view(img, mesh, 0.0, ox, H_SIDE * 0.55, S, "side")
    draw_view(img, mesh, 0.0, ox, H_SIDE + H_TOP * 0.5, S, "top")
    d = ImageDraw.Draw(img)
    d.line([(0, H_SIDE), (W_PANEL, H_SIDE)], fill=(45, 48, 55))
    d.text((6, 4), f"{title}  [{len(mesh.parts)} boxes]", fill=(220, 220, 220), font=font)
    # attach points
    for name, pt in mesh.points.items():
        x, y = ox + pt[2] * S, H_SIDE * 0.55 - pt[1] * S
        c = {"grip": (255, 80, 80), "support": (80, 255, 80), "magazine": (80, 160, 255), "muzzle": (255, 255, 0),
             "optic": (255, 0, 255), "underbarrel": (0, 255, 255), "stock": (255, 160, 0)}[name]
        d.ellipse([x - 2, y - 2, x + 2, y + 2], outline=c)
    return img


def sheet(weapons, kits, name, cols=2):
    font = ImageFont.load_default()
    span = max(max(gm.build(w, k).muzzle_z * -1 for w, k in zip(weapons, kits)) + 0.45, 0.5)
    S = W_PANEL * 0.92 / span
    S = min(S, 900)
    rows = (len(weapons) + cols - 1) // cols
    img = Image.new("RGB", (W_PANEL * cols, (H_SIDE + H_TOP) * rows), BG)
    for i, (w, k) in enumerate(zip(weapons, kits)):
        sub = ", ".join(v for v in k.values()) if k else ""
        p = panel(w, k, f"{w['name']} ({w['model']['base'] if 'model' in w else 'class'}) {sub}", font, S)
        img.paste(p, ((i % cols) * W_PANEL, (i // cols) * (H_SIDE + H_TOP)))
        ImageDraw.Draw(img).rectangle([(i % cols) * W_PANEL, (i // cols) * (H_SIDE + H_TOP), (i % cols + 1) * W_PANEL - 1, (i // cols + 1) * (H_SIDE + H_TOP) - 1], outline=(60, 64, 72))
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, f"{name}.png"))
    return os.path.join(OUT, f"{name}.png")


def full_kit(w, atts, i):
    """Like Program.SmokeFullKit: one attachment per slot that fits (rotating with i)."""
    out = []
    for slot in ["Optic", "Muzzle", "Barrel", "Underbarrel", "Magazine", "Stock"]:
        f = [a for a in atts.get(slot, []) if gm.fits(a, w)]
        if f:
            out.append(f[i % len(f)])
    return out


# ---- Checks (mirror SelfTest "Optics" model checks and "Gun models") -----------------------------

def clash(parts):
    optic = [p for p in parts if p[2]]
    if not optic:
        return None
    base = min(p[0][1] for p in optic)
    feet = [p for p in optic if p[0][1] <= base + 0.004]
    e = 0.0005
    for g in parts:
        if g[2] or g[3]:
            continue
        glo, ghi = g[0], g[1]
        for f in feet:
            flo, fhi = f[0], f[1]
            if ghi[0] > flo[0] + e and glo[0] < fhi[0] - e and ghi[2] > flo[2] + e and glo[2] < fhi[2] - e and ghi[1] > base + 0.0015:
                return f"a gun part rises to {ghi[1]:.3f} over the mount (base {base:.3f}) z {glo[2]:.3f}..{ghi[2]:.3f} [{g[4].node} {g[4].sx:.3f}x{g[4].sy:.3f}x{g[4].sz:.3f}]"
        for o in optic:
            olo, ohi = o[0], o[1]
            if olo[1] > base + 0.004 and ghi[0] > olo[0] + 0.001 and glo[0] < ohi[0] - 0.001 and ghi[1] > olo[1] + 0.001 and glo[1] < ohi[1] - 0.001 \
                    and ghi[2] > olo[2] + 0.001 and glo[2] < ohi[2] - 0.001:
                return f"a gun part y {glo[1]:.3f}..{ghi[1]:.3f} z {glo[2]:.3f}..{ghi[2]:.3f} runs into the optic y {olo[1]:.3f}..{ohi[1]:.3f} [{g[4].node}]"
    return None


def checks(weapons, atts):
    fails = []
    sigs = {}
    for w in weapons:
        if "model" not in w:
            continue
        mesh = gm.build(w, {})
        sig = hashlib.md5(repr([(round(p.sx, 4), round(p.sy, 4), round(p.sz, 4), round(p.x, 4), round(p.y, 4), round(p.z, 4), p.c) for p in mesh.parts]).encode()).hexdigest()
        if sig in sigs:
            fails.append(f"{w['name']} has the same model as {sigs[sig]}")
        sigs[sig] = w["name"]
        for p in mesh.parts:
            for v in (p.sx, p.sy, p.sz):
                if not (v > 0 and v < 7):
                    fails.append(f"{w['name']}: bad size {v} on a {p.node} part")
        sh, az = gm.sight_height(w, mesh), gm.ads_z(w, mesh)
        if not (0 < sh <= 0.2) or not (-0.7 <= az <= -0.02):
            fails.append(f"{w['name']} irons: sight {sh} adsz {az}")
        for o in atts["Optic"]:
            d, kit = gm.apply(w, [o])
            m2 = gm.build(d, kit)
            sh, az = gm.sight_height(d, m2), gm.ads_z(d, m2)
            if not (0 < sh <= 0.2) or not (-0.7 <= az <= -0.02):
                fails.append(f"{w['name']} + {o['name']}: sight {sh:.3f} adsz {az:.3f}")
        # Optics model checks: fitting optics only
        fit = [a for s in atts for a in atts[s] if gm.fits(a, w)]
        for o in [a for a in fit if a["slot"] == "Optic"]:
            d, kit = gm.apply(w, [o])
            mesh_open = gm.build(d, kit, 1.0)
            sight = gm.sight_height(d, mesh_open)
            eye = -gm.ads_z(d, mesh_open)
            op = record(mesh_open, 1.0)
            for dx, dy in [(0, 0), (0.004, 0), (-0.004, 0), (0, 0.004), (0, -0.004)]:
                hit = [p for p in op if not p[3] and p[0][0] < dx < p[1][0] and p[0][1] < sight + dy < p[1][1] and p[0][2] < eye]
                if hit:
                    h = hit[0]
                    fails.append(f"{w['name']} + {o['name']}: {'optic' if h[2] else 'gun'} part blocks the sight line ({dx},{dy}) z {h[0][2]:.3f}..{h[1][2]:.3f} [{h[4].node} y {h[0][1]:.3f}..{h[1][1]:.3f}, sight {sight:.3f}]")
                    break
            closed = record(gm.build(d, kit, 0.0), 0.0)
            a1 = [(p[0], p[1]) for p in closed if not p[2]]
            a2 = [(p[0], p[1]) for p in op if not p[2]]
            if a1 != a2:
                fails.append(f"{w['name']} + {o['name']}: cut-away changes non-optic parts")
            others = [None] + [a for a in fit if a["slot"] != "Optic"]
            for a in others:
                d2, kit2 = gm.apply(w, [o] + ([a] if a else []))
                why = clash(record(gm.build(d2, kit2, 0.0), 0.0))
                if why:
                    fails.append(f"{w['name']} [{', '.join(kit2.values())}]: {why}")
                    break
        if w["class"] == "LMG":
            iron_top = max(p[1][1] for p in record(gm.build(w, {}), 0))
            rd = [a for a in atts["Optic"] if a["name"] == "Red Dot"][0]
            d, kit = gm.apply(w, [rd])
            dot_top = max(p[1][1] for p in record(gm.build(d, kit), 0) if not p[2])
            if not iron_top > dot_top + 0.01:
                fails.append(f"{w['name']}: carry handle top {iron_top:.3f} vs {dot_top:.3f} with a red dot")
        # attach points near the model
        lo = [min(p.bounds()[0][i] for p in mesh.parts) for i in range(3)]
        hi = [max(p.bounds()[1][i] for p in mesh.parts) for i in range(3)]
        for k, pt in mesh.points.items():
            if any(pt[i] < lo[i] - 0.05 or pt[i] > hi[i] + 0.05 for i in range(3)):
                fails.append(f"{w['name']}: attach point {k} {pt} is off the model {lo} {hi}")
    return fails


def table(weapons):
    for w in weapons:
        if "model" not in w:
            continue
        mesh = gm.build(w, {})
        lo = [min(p.bounds()[0][i] for p in mesh.parts) for i in range(3)]
        hi = [max(p.bounds()[1][i] for p in mesh.parts) for i in range(3)]
        print(f'("{w["name"]}", {len(mesh.parts)}, {lo[1]:.3f}f, {lo[2]:.3f}f, {hi[1]:.3f}f, {hi[2]:.3f}f),')


if __name__ == "__main__":
    weapons = gm.load_weapons()
    atts = gm.load_attachments()
    if "--table" in sys.argv:
        table(weapons)
        sys.exit(0)
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    classes = ["Pistol", "SMG", "AssaultRifle", "DMR", "Sniper", "Shotgun", "LMG", "Special"]
    if "--nosheets" not in sys.argv:
        for cls in classes:
            ws = [w for w in weapons if w["class"] == cls and (only is None or only in w["name"])]
            if not ws:
                continue
            print(sheet(ws, [{} for _ in ws], f"stock_{cls}"))
            kitted, kits = [], []
            for i, w in enumerate(ws):
                d, k = gm.apply(w, full_kit(w, atts, i + 1))
                kitted.append(d)
                kits.append(k)
            print(sheet(kitted, kits, f"kitted_{cls}"))
    fails = checks([w for w in weapons if only is None or only in w["name"]], atts)
    for f in fails[:60]:
        print("FAIL:", f)
    print(f"{len(fails)} failure(s)")
