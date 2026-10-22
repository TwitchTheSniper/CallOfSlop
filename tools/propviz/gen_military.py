"""Generates the reusable military prop library for Call of Slop: Props/Military (fortifications, guns, ammunition,
supplies, camp gear) and Props/Military/Vehicles (tanks, planes, cars and trucks, category "Military Vehicles"), plus
the Dev map that shows them all (Maps/Dev/MilitaryProps.json, "Military Props").

    python3 tools/propviz/gen_military.py [Props] [Maps/Dev/MilitaryProps.json]

Uses the box helpers of gen.py and gen_outdoor.py. Original designs only: no real vehicle names, insignia or
markings (the "star" markings are an invented eight-pointed star made of two turned squares). Colour variants (Desert,
Winter) are separate props built from the same functions with another palette.

Health follows the material health guide in Props/README.md. Vehicles, guns and big structures are unbreakable cover;
wooden ammunition crates and pallets break; fuel drums, jerrycan racks and shell crates explode with a fuse.
"""
import json, math, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen  # noqa: E402
import gen_outdoor as go  # noqa: E402
from gen import B, At, Cyl, HCyl, C, brk  # noqa: E402

PROPS = []
AUTHOR = "Call of Slop"
MIL = "Military"
VEH = "Military Vehicles"

# ------------------------------------------------------------------ palettes ------------------------------------------------------------------
OLIVE = {"body": "#4b5326", "dark": "#363d1b", "light": "#5f6834", "canvas": "#6b6a45", "id": ""}
DESERT = {"body": "#b39a68", "dark": "#8c774c", "light": "#c8b282", "canvas": "#bba57a", "id": " Desert"}
WINTER = {"body": "#cfd2ca", "dark": "#8e938a", "light": "#e2e4de", "canvas": "#c4c6bc", "id": " Winter"}
GREY = {"body": "#79818a", "dark": "#5b626a", "light": "#8f979f", "canvas": "#7d7f78", "id": " Grey"}
TRACK = "#2b2a27"
WHEEL = "#3b3e34"
GLASS = "#2a3a44"
CANOPY = "#5a7a8c"
BRASS = "#b8953c"
STEEL = "#4a4c4a"
BURNT = "#2c2826"
RUST = "#6a4430"
WOOD_OD = "#4f5a2c"     # olive-painted wood (ammo crates)
WOOD_OD2 = "#5b6634"
STENCIL = "#d8c040"     # yellow stencil bands
BAGS = ["#9a8c62", "#8a7f58", "#a59670", "#7f7a55"]

SQ = {"randomYaw": False, "yawStep": 90, "yawJitter": 4}
SQ0 = {"randomYaw": False, "yawStep": 90}
ANY = {"randomYaw": True, "scale": [0.95, 1.05]}
BIG = {"randomYaw": False, "yawStep": 90, "yawJitter": 3, "scale": [1, 1]}


def flat(parts):
    out = []
    for p in parts:
        if isinstance(p, list):
            out.extend(flat(p))
        else:
            out.append(p)
    return out


def prop(name, cat, desc, parts, collision="solid", collider=None, scatter=None, breakable=None, fade=None):
    d = {"format": "call-of-slop-prop/1", "name": name, "category": cat, "description": desc, "author": AUTHOR, "collision": collision}
    if collider is not None:
        d["collider"] = collider
    if scatter is not None:
        d["scatter"] = scatter
    if fade is not None:
        d["fadeDistance"] = fade
    if breakable is not None:
        d["breakable"] = breakable
    d["parts"] = flat(parts)
    lo, _ = go.bounds(d)
    if lo[1] < 0:  # tilted posts and turned beams poke a little under the ground: lift the model onto it
        d["parts"] = xf(d["parts"], t=(0, -lo[1], 0))
    PROPS.append(d)
    return d


# ------------------------------------------------------------------ transforms ------------------------------------------------------------------

def mat(pitch=0.0, yaw=0.0, roll=0.0):
    return go.rot(pitch, yaw, roll)


def mmul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def euler(R):
    """The game's (pitch, yaw, roll) for R = Ry(yaw) Rx(pitch) Rz(roll)."""
    sp = max(-1.0, min(1.0, -R[1][2]))
    p = math.asin(sp)
    if abs(math.cos(p)) > 1e-6:
        y = math.atan2(R[0][2], R[2][2])
        r = math.atan2(R[1][0], R[1][1])
    else:  # gimbal lock: put it all in yaw
        y = math.atan2(-R[2][0], R[0][0])
        r = 0.0
    return [math.degrees(p), math.degrees(y), math.degrees(r)]


def xf(parts, R=None, t=(0, 0, 0), pivot=(0, 0, 0)):
    """Turns parts by matrix R about `pivot`, then moves them by t."""
    out = []
    for p in flat(parts):
        q = dict(p)
        o = p.get("offset", [0, p["size"][1] / 2, 0])
        rel = [o[i] - pivot[i] for i in range(3)]
        if R is not None:
            rel = [sum(R[i][k] * rel[k] for k in range(3)) for i in range(3)]
            Rp = mat(*p.get("rotation", [0, 0, 0]))
            e = euler(mmul(R, Rp))
            if any(abs(v) > 1e-4 for v in e):
                q["rotation"] = gen.r3(e)
            else:
                q.pop("rotation", None)
        q["offset"] = gen.r3([rel[i] + pivot[i] + t[i] for i in range(3)])
        out.append(q)
    return out


def ground(parts):
    """Stands the model on y = 0 (lowest point exactly on the ground)."""
    parts = flat(parts)
    lo, _ = go.bounds({"parts": parts})
    return xf(parts, t=(0, -lo[1], 0))


def tube(p0, p1, t, color=STEEL, mat_=None):
    return go.tube(p0, p1, t, color=color, mat=mat_)


def star(d, x, y, z, color="white", axis="y"):
    """The invented eight-pointed star marking: two turned squares, flat on a face (axis = the face normal)."""
    s = d / math.sqrt(2)
    if axis == "y":
        return [B([s, 0.012, s], [x, y, z], color=color), B([s, 0.012, s], [x, y, z], color=color, yaw=45)]
    if axis == "z":
        return [B([s, s, 0.012], [x, y, z], color=color), B([s, s, 0.012], [x, y, z], color=color, roll=45)]
    return [B([0.012, s, s], [x, y, z], color=color), B([0.012, s, s], [x, y, z], color=color, pitch=45)]


def disc_z(d, w, x, y, z, color=WHEEL):
    """A cheap wheel/road wheel seen from the side: two turned squares, axle along Z."""
    s = d / math.sqrt(2) * 1.04
    return [B([s, s, w], [x, y, z], color=color), B([s, s, w], [x, y, z], color=color, roll=45)]


def wheel(d, w, x, z, cy=None, tyre="rubber", hub=WHEEL):
    """An octagonal tyre (axle along Z) with a two-box hub."""
    cy = d / 2 if cy is None else cy
    return HCyl(d, w, cy, x, z, color=tyre, alongZ=True) + disc_z(d * 0.55, w + 0.03, x, cy, z, color=hub)


def ring_x(d, t, w, cx, cy, cz, color, n=8):
    """A hollow octagonal tube along X: n plates of thickness t, `w` long (a fuselage shell, a gun's cooling jacket)."""
    r = d / 2 - t / 2
    chord = 2 * (d / 2) * math.tan(math.pi / n) * 1.02
    out = []
    for k in range(n):
        a = 2 * math.pi * k / n
        out.append(B([w, t, chord], [cx, cy + r * math.sin(a), cz + r * math.cos(a)], color=color, pitch=-math.degrees(a) + 90))
    return out


def wood(h, cols=None, **kw):
    return brk(h, "wood", count=kw.pop("count", 12), size=kw.pop("size", (0.05, 0.3)), speed=kw.pop("speed", 4), colors=cols, **kw)


def metal(h, cols=None, **kw):
    return brk(h, "metal", count=kw.pop("count", 10), size=kw.pop("size", (0.05, 0.22)), speed=kw.pop("speed", 4), colors=cols, **kw)


# ================================================================== Fortifications ==================================================================

def bag(x, y0, z, yaw, c, top, L=0.5, H=0.16, D=0.3):
    p = [B([L * 0.96, H, D], [x, y0 + H / 2, z], color=c, yaw=yaw)]
    if top:
        p.append(B([L * 0.82, 0.04, D * 0.78], [x, y0 + H + 0.008, z], color=c, yaw=yaw))
    return p


def bag_wall(L, courses, rows, rnd, depth=0.3):
    """A running-bond sandbag wall along X centred on the origin; rows deep along Z. Only the top course gets the
    rounded tops (the rest are hidden)."""
    n = max(1, round(L / 0.5))
    bl = L / n
    out = []
    zs = [0.0] if rows == 1 else [-(depth / 2), depth / 2]
    for c in range(courses):
        top = c == courses - 1
        y0 = c * 0.16
        for z in zs:
            if c % 2 == 0:
                xs = [(-L / 2 + bl / 2 + k * bl, bl) for k in range(n)]
            else:
                xs = [(-L / 2 + bl / 4, bl / 2)] + [(-L / 2 + bl + k * bl, bl) for k in range(n - 1)] + [(L / 2 - bl / 4, bl / 2)]
            for x, w in xs:
                out += bag(x, y0, z + rnd.uniform(-0.012, 0.012), rnd.uniform(-3, 3), rnd.choice(BAGS), top, L=w, D=depth)
    return out


def sandbags():
    r = random.Random(11)
    p = bag_wall(1.5, 6, 2, r)
    prop("Sandbag Wall Short", MIL, "A 1.5 m sandbag wall two bags thick, chest high on a crouch (0.96 m). Along X.", p,
         collider=[C([1.5, 0.96, 0.62])], scatter=SQ)
    r = random.Random(12)
    p = bag_wall(3.0, 6, 2, r)
    prop("Sandbag Wall Long", MIL, "A 3 m sandbag wall two bags thick (0.96 m high). Along X; line them up end to end.", p,
         collider=[C([3.0, 0.96, 0.62])], scatter=SQ)
    r = random.Random(13)
    a = xf(bag_wall(1.8, 6, 2, r), t=(0, 0, -0.6))                             # along X, z -0.9..-0.3
    b = xf(bag_wall(1.2, 6, 2, r), R=mat(yaw=90), t=(-0.6, 0, 0.3))           # along Z, x -0.9..-0.3
    prop("Sandbag Corner", MIL, "An L-shaped sandbag corner, 1.8 m each way, two bags thick (0.96 m). Outer corner at -X -Z.", [a, b],
         collider=[C([1.8, 0.96, 0.62], [0, 0.48, -0.6]), C([0.62, 0.96, 1.2], [-0.6, 0.48, 0.3])], scatter=SQ)
    # Nest: front wall at +X, side walls, two rear stubs leaving a 1.2 m way in at -X.
    r = random.Random(14)
    front = xf(bag_wall(2.8, 5, 1, r), R=mat(yaw=90), t=(1.25, 0, 0))
    sides = [xf(bag_wall(2.5, 5, 1, r), t=(-0.15, 0, s * 1.25)) for s in (-1, 1)]
    stubs = [xf(bag_wall(0.5, 5, 1, r), R=mat(yaw=90), t=(-1.25, 0, s * 0.85)) for s in (-1, 1)]
    prop("Sandbag Nest Ring", MIL, "A square sandbag nest 2.8 m across, 0.8 m high, with a 1.2 m way in at the back (-X). Room for two inside.",
         [front, sides, stubs],
         collider=[C([0.32, 0.8, 2.8], [1.25, 0.4, 0]), C([2.5, 0.8, 0.32], [-0.15, 0.4, -1.25]), C([2.5, 0.8, 0.32], [-0.15, 0.4, 1.25]),
                   C([0.32, 0.8, 0.5], [-1.25, 0.4, -0.85]), C([0.32, 0.8, 0.5], [-1.25, 0.4, 0.85])], scatter=SQ0)
    r = random.Random(15)
    p = []
    for (x, z, y0) in [(-0.5, -0.2, 0), (0.0, -0.2, 0), (0.5, -0.2, 0), (-0.45, 0.2, 0), (0.05, 0.22, 0), (0.55, 0.18, 0),
                       (-0.25, 0.0, 0.16), (0.25, 0.0, 0.16), (0.0, 0.05, 0.32), (0.7, 0.5, 0), (-0.8, 0.45, 0)]:
        p += bag(x + r.uniform(-0.04, 0.04), y0, z, r.uniform(-25, 25), r.choice(BAGS), True)
    prop("Sandbag Pile", MIL, "A loose heap of spare sandbags, knee high.", p, collider=[C([1.5, 0.5, 0.75], [0, 0.25, 0])],
         scatter={"randomYaw": True, "scale": [0.9, 1.1]})


def wire():
    # Concertina coil along X: rings in the YZ plane, alternately leaning.
    W = "#8a8a84"
    p = []
    n = 13
    for k in range(n):
        x = -1.38 + k * 2.76 / (n - 1)
        ring = []
        r = 0.4
        for s in range(6):
            a = 2 * math.pi * s / 6 + 0.3 * k
            chord = 2 * r * math.tan(math.pi / 6) * 1.04
            ring.append(B([0.018, chord, 0.018], [0, r * math.sin(a), r * math.cos(a)], color=W, pitch=-math.degrees(a)))
        p += xf(ring, R=mat(roll=16 if k % 2 else -16), t=(x, 0.43, 0))
    for x in (-1.5, 0.0, 1.5):
        p.append(At([0.04, 1.0, 0.04], 0, x, 0.0, color="#5a5a52"))
    p += [B([3.0, 0.014, 0.014], [0, 0.82, 0.05], color=W), B([3.0, 0.014, 0.014], [0, 0.05, 0.3], color=W),
          B([3.0, 0.014, 0.014], [0, 0.05, -0.3], color=W)]
    prop("Barbed Wire Coil", MIL, "A 3 m coil of barbed wire on three iron pickets (along X). Looks only: you push through it.", p,
         collision="cosmetic", fade=75, scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10})
    # Fence line: four posts 1.5 m apart, five strands, crossed bracing wires.
    r = random.Random(21)
    p = []
    xs = [-2.25, -0.75, 0.75, 2.25]
    for x in xs:
        p.append(B([0.09, 1.35, 0.09], [x, 0.62, 0], color="darkwood", mat="wood", roll=r.uniform(-3, 3), pitch=r.uniform(-3, 3)))
    for k, y in enumerate((0.22, 0.45, 0.68, 0.91, 1.14)):
        p.append(B([4.55, 0.012, 0.012], [0, y + r.uniform(-0.02, 0.02), 0.05], color=W, roll=r.uniform(-0.6, 0.6)))
    for a, b in zip(xs, xs[1:]):
        p.append(tube([a, 0.15, 0.06], [b, 1.1, 0.06], 0.012, color=W))
        p.append(tube([a, 1.1, 0.06], [b, 0.15, 0.06], 0.012, color=W))
    for k in range(18):  # barbs
        x = -2.2 + k * 0.26
        p.append(B([0.05, 0.05, 0.012], [x, [0.45, 0.91][k % 2], 0.05], color=W, roll=45))
    prop("Barbed Wire Fence Line", MIL, "4.5 m of barbed wire fence on four wooden posts (along X). Looks only: you push through it.", p,
         collision="cosmetic", fade=75, scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4})


def hedgehog():
    p = []
    L = 0.95
    e = math.degrees(math.asin(1 / math.sqrt(3)))  # each beam's elevation: three mutually square beams standing on three ends
    h = L * math.sin(math.radians(e))
    for k in range(3):
        beam = [B([2 * L, 0.15, 0.022], [0, 0, 0.064], color="#5c4c3c", mat="metal"), B([2 * L, 0.022, 0.15], [0, 0.064, 0], color="#5c4c3c", mat="metal"),
                B([0.06, 0.03, 0.17], [L - 0.04, 0.0, 0.06], color=RUST), B([0.06, 0.03, 0.17], [-L + 0.04, 0.0, 0.06], color=RUST)]
        R = mmul(mat(yaw=120 * k), mmul(mat(roll=e), mat(pitch=45)))
        p += xf(beam, R=R, t=(0, h + 0.02, 0))
    p += [B([0.26, 0.26, 0.26], [0, h + 0.02, 0], color="#4a3e32", yaw=20, pitch=30), At([0.5, 0.02, 0.5], 0, color="#5a4a36", yaw=10)]
    prop("Steel Hedgehog", MIL, "A tank-stopping hedgehog: three crossed steel angle beams, rusty. Stops a person too.", p,
         collider=[C([0.9, 1.0, 0.9])], scatter={"randomYaw": True, "scale": [0.9, 1.1]})


def tank_trap():
    con = "#9a978e"
    p = [At([1.1, 0.5, 1.1], 0, color=con, mat="concrete"), At([0.94, 0.25, 0.94], 0.5, color=con, mat="concrete"),
         At([0.72, 0.14, 0.72], 0.75, color="#a3a097", mat="concrete")]
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(At([0.16, 0.5, 0.16], 0, sx * 0.53, sz * 0.53, color="#8e8b82", yaw=45))
    p += [At([0.06, 0.12, 0.06], 0.89, -0.15, 0, color=RUST), At([0.06, 0.12, 0.06], 0.89, 0.15, 0, color=RUST),
          At([0.36, 0.05, 0.06], 1.0, 0, 0, color=RUST), At([0.3, 0.2, 0.02], 0.12, 0.2, 0.555, color="#6e6c64"),
          At([1.3, 0.015, 1.3], 0, color="#5a4a36", yaw=8)]
    prop("Concrete Tank Trap", MIL, "A cast concrete anti-tank block with a stepped top and a rusty lifting loop.", p,
         collider=[C([1.1, 0.89, 1.1])], scatter={"randomYaw": False, "yawStep": 15, "yawJitter": 6})


def stake_barrier():
    p = HCyl(0.2, 3.0, 0.55, color="#6a5638", mat="wood")
    for k in range(5):
        x = -1.2 + k * 0.6
        for s in (-1, 1):
            a = 45 * s
            p.append(B([0.08, 1.7, 0.08], [x, 0.55, 0], color="#7a6444", mat="wood", pitch=a))
            tip = [0, 0.85 * math.cos(math.radians(a)) + 0.55, 0.85 * math.sin(math.radians(a))]
            p.append(B([0.05, 0.12, 0.05], [x, tip[1], -tip[2]], color="#a08a62", pitch=a))
    prop("Wooden Stake Barrier", MIL, "A 3 m log bristling with crossed sharpened stakes (along X).", p,
         collider=[C([3.0, 1.15, 1.0], [0, 0.575, 0])], scatter=SQ, breakable=wood(120, ["#7a6444", "#6a5638"], count=16))


# ================================================================== Guns ==================================================================

def spoked_wheel(d, w, x, y, z, rim, hub):
    p = go.vring(d, 0.09, w, x, y, z, color=rim, n=8)
    for a in (0, 45, 90, 135):
        p.append(B([d * 0.9, 0.045, 0.04], [x, y, z], color=hub, roll=a))
    p += disc_z(0.22, w + 0.08, x, y, z, color=hub)
    return p


def field_cannon(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    p = spoked_wheel(1.2, 0.12, 0, 0.6, 0.85, "#3a3326", c) + spoked_wheel(1.2, 0.12, 0, 0.6, -0.85, "#3a3326", c)
    p += [B([0.12, 0.12, 1.84], [0, 0.6, 0], color=dk),
          B([0.05, 1.05, 1.6], [0.3, 1.15, 0], color=c, roll=-8, mat="metal"), B([0.05, 0.25, 1.6], [0.26, 1.72, 0], color=c, roll=20),
          B([0.05, 0.35, 1.5], [0.32, 0.45, 0], color=dk), B([0.06, 0.14, 0.2], [0.33, 1.05, 0.0], color="#1e1e1c"),
          B([1.3, 0.26, 0.32], [0.15, 1.0, 0], color=dk), B([0.4, 0.32, 0.32], [-0.62, 1.04, 0], color=c)]
    p += HCyl(0.15, 2.3, 1.08, 1.6, 0, color=c) + HCyl(0.21, 0.16, 1.08, 2.7, 0, color=dk) + HCyl(0.15, 1.3, 0.9, 0.7, 0, color=lt)
    for s in (-1, 1):
        p.append(tube([-0.3, 0.62, 0.15 * s], [-2.9, 0.12, 0.6 * s], 0.13, color=c))
        p.append(B([0.08, 0.32, 0.36], [-2.92, 0.16, 0.62 * s], color=dk))
        p.append(B([0.3, 0.06, 0.06], [-2.6, 0.32, 0.56 * s], color=STEEL))
    p += [B([0.2, 0.16, 0.08], [-0.2, 1.25, 0.22], color="#1e1e1c"), disc_z(0.2, 0.03, -0.25, 0.95, 0.24, color=STEEL)]
    prop("Field Cannon" + pal["id"], MIL, "A towed field gun on spoked wheels with a gun shield and split trail. Muzzle to +X.", p,
         collider=[C([0.5, 1.75, 1.75], [0.12, 0.875, 0]), C([2.7, 0.45, 1.3], [-1.6, 0.225, 0])], scatter=BIG)


def at_gun(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    p = wheel(0.85, 0.22, 0, 0.85, tyre="rubber", hub=c) + wheel(0.85, 0.22, 0, -0.85, tyre="rubber", hub=c)
    p += [B([0.12, 0.12, 1.6], [0, 0.43, 0], color=dk),
          B([0.05, 0.75, 1.75], [0.35, 0.95, 0], color=c, roll=-14, mat="metal"), B([0.05, 0.3, 0.8], [0.38, 1.35, 0.5], color=c, roll=-14),
          B([0.05, 0.3, 0.8], [0.38, 1.35, -0.5], color=c, roll=-14), B([0.06, 0.12, 0.16], [0.4, 0.98, 0], color="#1e1e1c"),
          B([1.1, 0.22, 0.28], [0.1, 0.78, 0], color=dk), B([0.42, 0.3, 0.3], [-0.6, 0.82, 0], color=c)]
    p += HCyl(0.11, 3.1, 0.86, 2.0, 0, color=c) + HCyl(0.17, 0.32, 0.86, 3.55, 0, color=dk)
    p += [B([0.08, 0.1, 0.32], [3.62, 0.86, 0], color=dk), B([0.08, 0.1, 0.32], [3.48, 0.86, 0], color=dk)]   # muzzle brake baffles
    for s in (-1, 1):
        p.append(tube([-0.3, 0.45, 0.14 * s], [-2.6, 0.1, 0.75 * s], 0.12, color=c))
        p.append(B([0.08, 0.28, 0.32], [-2.62, 0.14, 0.77 * s], color=dk))
    prop("Anti-Tank Gun" + pal["id"], MIL, "A low towed anti-tank gun with a sloped shield and a long barrel with a muzzle brake. Muzzle to +X.", p,
         collider=[C([0.55, 1.5, 1.8], [0.15, 0.75, 0]), C([2.4, 0.4, 1.5], [-1.45, 0.2, 0])], scatter=BIG)


def howitzer(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    p = []
    for z in (1.15, -1.15):
        p += wheel(1.25, 0.36, 0, z, tyre="rubber", hub=c)
    p += [B([0.18, 0.18, 2.0], [0, 0.63, 0], color=dk), B([1.6, 0.5, 1.1], [0, 0.95, 0], color=c, mat="metal"),
          B([0.5, 0.5, 0.2], [0, 1.4, 0.45], color=dk), B([0.5, 0.5, 0.2], [0, 1.4, -0.45], color=dk),
          B([0.05, 0.9, 2.0], [0.7, 1.2, 0], color=c, roll=-6), B([0.25, 0.2, 0.2], [-0.2, 1.6, 0.62], color="#1e1e1c")]
    gun = HCyl(0.26, 4.2, 0, 2.0, 0, color=c) + HCyl(0.34, 0.3, 0, 4.0, 0, color=dk) + HCyl(0.2, 2.0, -0.24, 0.9, 0, color=lt) \
        + [B([0.9, 0.5, 0.5], [-0.3, 0, 0], color=dk), B([0.6, 0.12, 0.42], [-0.3, 0.3, 0], color=c)]
    p += xf(gun, R=mat(roll=28), t=(0, 1.55, 0))
    for s in (-1, 1):
        p.append(tube([-0.5, 0.75, 0.3 * s], [-4.0, 0.14, 1.0 * s], 0.2, color=c))
        p.append(B([0.1, 0.45, 0.6], [-4.05, 0.22, 1.02 * s], color=dk))
        p.append(B([0.12, 0.12, 0.7], [-3.7, 0.3, 0.92 * s], color=STEEL))
    p += HCyl(0.22, 0.6, 0.3, -0.2, 0, color=dk)
    prop("Howitzer" + pal["id"], MIL, "A heavy towed howitzer, barrel raised, trail legs spread with their spades dug in. Muzzle to +X.", p,
         collider=[C([1.7, 1.75, 2.7], [0, 0.875, 0]), C([3.4, 0.5, 2.2], [-2.45, 0.25, 0])], scatter=BIG)


def mortar_pit():
    r = random.Random(31)
    p = []
    n = 14
    R = 1.35
    for course in range(2):
        for k in range(n):
            a = 2 * math.pi * (k + 0.5 * course) / n
            p += bag(R * math.cos(a), course * 0.16, R * math.sin(a), -math.degrees(a) + 90, r.choice(BAGS), course == 1, L=0.62)
    p += [At([0.36, 0.05, 0.36], 0, color="#3a3d30"), tube([0, 0.05, 0], [0.42, 1.0, 0], 0.09, color="#3e4428"),
          HCyl(0.11, 0.08, 0.98, 0.42, 0, color="#2a2c22")[:2],
          tube([0.32, 0.72, 0], [0.62, 0.0, 0.28], 0.035, color="#3e4428"), tube([0.32, 0.72, 0], [0.62, 0.0, -0.28], 0.035, color="#3e4428"),
          B([0.1, 0.08, 0.12], [0.22, 0.6, 0.1], color="#1e1e1c")]
    for k in range(3):
        z = -0.7 + k * 0.14
        p += [B([0.3, 0.07, 0.07], [-0.6, 0.035, z], color="#4a5a2a"), B([0.08, 0.06, 0.06], [-0.79, 0.035, z], color="#7a7a6c")]
    p += [At([0.6, 0.3, 0.35], 0, -0.55, 0.55, color=WOOD_OD, mat="wood"), At([0.2, 0.05, 0.01], 0.15, -0.55, 0.73, color=STENCIL),
          At([2.3, 0.01, 2.3], 0, color="#5a4a36", yaw=12)]
    s = 0.34
    prop("Mortar Pit", MIL, "A ring of sandbags 3 m across (two courses, step over them) around a mortar on its baseplate, with shells and a crate.", p,
         collider=[C([s, 0.32, 1.9], [1.35, 0.16, 0]), C([s, 0.32, 1.9], [-1.35, 0.16, 0]), C([1.9, 0.32, s], [0, 0.16, 1.35]),
                   C([1.9, 0.32, s], [0, 0.16, -1.35])], scatter={"randomYaw": True, "scale": [1, 1]})


def machine_gun():
    g = "#33362c"
    p = [tube([0, 0.42, 0], [0.42, 0.0, 0.0], 0.04, color=g), tube([0, 0.42, 0], [-0.3, 0.0, 0.34], 0.04, color=g),
         tube([0, 0.42, 0], [-0.3, 0.0, -0.34], 0.04, color=g), B([0.12, 0.08, 0.1], [0, 0.45, 0], color=g),
         B([0.52, 0.16, 0.13], [-0.02, 0.56, 0], color="#2a2c26"), B([0.1, 0.14, 0.12], [-0.3, 0.56, 0], color="#2a2c26")]
    p += HCyl(0.1, 0.7, 0.57, 0.58, 0, color="#3a3d34") + HCyl(0.035, 0.24, 0.57, 1.04, 0, color="#1e1e1c")
    p += [B([0.02, 0.008, 0.008], [0.4 + k * 0.1, 0.625, 0], color="#1a1a18") for k in range(4)]
    p += [B([0.08, 0.04, 0.03], [-0.38, 0.56, 0.06], color="#1e1e1c"), B([0.08, 0.04, 0.03], [-0.38, 0.56, -0.06], color="#1e1e1c"),
          At([0.28, 0.2, 0.12], 0.0, 0.05, 0.3, color="#4a5a2a"), B([0.16, 0.025, 0.05], [0.02, 0.5, 0.2], color=BRASS, roll=-20),
          B([0.02, 0.06, 0.02], [0.2, 0.67, 0], color="#1e1e1c")]
    prop("Mounted Machine Gun", MIL, "A heavy machine gun on a low tripod with an ammo box and a belt fed in. Muzzle to +X; put it in a nest.", p,
         collider=[C([0.6, 0.62, 0.5], [0.1, 0.31, 0])], scatter=SQ,
         breakable=metal(160, ["#33362c", "#3a3d34", BRASS], count=10))


# ================================================================== Ammunition ==================================================================

def crate_parts(L, H, D, x=0.0, y0=0.0, z=0.0, body=WOOD_OD, yaw=0, band=True):
    p = [At([L, H, D], y0, x, z, color=body, mat="wood", tex=2),
         At([0.04, H + 0.02, D + 0.02], y0, x - L / 2 + 0.06, z, color="#3a4420"), At([0.04, H + 0.02, D + 0.02], y0, x + L / 2 - 0.06, z, color="#3a4420"),
         At([L - 0.04, 0.03, D + 0.01], y0 + H - 0.02, x, z, color=WOOD_OD2)]
    if band:
        p += [At([L * 0.34, 0.06, 0.01], y0 + H * 0.55, x, z + D / 2 + 0.003, color=STENCIL),
              At([L * 0.22, 0.03, 0.01], y0 + H * 0.3, x, z + D / 2 + 0.003, color="#d8d8c8")]
    if yaw:
        p = xf(p, R=mat(yaw=yaw), pivot=(x, 0, z))
    return p


def ammo():
    L, H, D = 0.9, 0.42, 0.45
    p = crate_parts(L, H, D) + [At([0.03, 0.04, 0.16], H * 0.55, L / 2 + 0.01, 0, color="tan"),
                                At([0.03, 0.04, 0.16], H * 0.55, -L / 2 - 0.01, 0, color="tan"),
                                At([0.05, 0.06, 0.02], H - 0.06, 0.25, D / 2 + 0.008, color=STEEL), At([0.05, 0.06, 0.02], H - 0.06, -0.25, D / 2 + 0.008, color=STEEL)]
    cols = [WOOD_OD, "#3a4420", STENCIL]
    prop("Ammo Crate Wooden", MIL, "An olive-painted wooden ammunition crate with rope handles and a yellow stencil band.", p,
         collider=[C([0.92, 0.44, 0.47])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 12},
         breakable=wood(50, cols, count=12))
    p = []
    for x in (-0.47, 0.47):
        for z in (-0.24, 0.24):
            p += crate_parts(0.9, 0.42, 0.45, x, 0, z, band=z > 0)
    p += crate_parts(0.9, 0.42, 0.45, -0.3, 0.42, -0.22, yaw=6, band=False) + crate_parts(0.9, 0.42, 0.45, 0.52, 0.42, 0.2, yaw=-4)
    p += crate_parts(0.9, 0.42, 0.45, 0.1, 0.84, 0.0, yaw=88)
    prop("Stacked Ammo Crates", MIL, "Seven wooden ammunition crates stacked three high (1.26 m).", p,
         collider=[C([1.9, 0.84, 0.94]), C([0.6, 0.42, 0.95], [0.1, 1.05, 0])], scatter=SQ, breakable=wood(130, cols, count=24, size=(0.06, 0.4)))
    # Metal transit case.
    c = "#55603a"
    p = [At([0.8, 0.4, 0.5], 0.02, color=c, mat="metal"), At([0.82, 0.05, 0.52], 0.26, color="#4a5432"),
         At([0.76, 0.06, 0.46], 0.42, color=c), At([0.84, 0.02, 0.54], 0.0, color="#3a4228")]
    for x in (-0.25, 0.25):
        p += [At([0.07, 0.08, 0.02], 0.25, x, 0.265, color=STEEL), At([0.07, 0.08, 0.02], 0.25, x, -0.265, color=STEEL)]
    p += [At([0.02, 0.04, 0.18], 0.3, 0.41, 0, color="#2a2c26"), At([0.02, 0.04, 0.18], 0.3, -0.41, 0, color="#2a2c26"),
          At([0.3, 0.08, 0.01], 0.1, 0, 0.255, color="#e0e0d4"), At([0.12, 0.03, 0.01], 0.12, 0.25, 0.256, color=STENCIL)]
    prop("Ammo Case Metal", MIL, "A ribbed olive steel transit case with four latches and end handles.", p,
         collider=[C([0.84, 0.48, 0.54])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 15},
         breakable=metal(140, [c, STEEL], count=10))
    # Small steel ammo can (cosmetic).
    p = [At([0.29, 0.17, 0.11], 0, color="#4f5a2c"), At([0.3, 0.025, 0.12], 0.17, color="#46502a"),
         At([0.12, 0.012, 0.02], 0.2, color="#2a2c26"), At([0.04, 0.05, 0.015], 0.13, 0.12, 0.06, color=STEEL),
         At([0.16, 0.03, 0.005], 0.07, 0, 0.056, color=STENCIL)]
    prop("Steel Ammo Can", MIL, "A small olive steel ammunition can with a carry handle. Looks only.", p, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=metal(12, ["#4f5a2c", STEEL], count=5, size=(0.03, 0.08)))
    # Shell crate: lid open, four shells inside. Explodes.
    L, H, D = 1.0, 0.36, 0.56
    p = [At([L, 0.04, D], 0, color=WOOD_OD, mat="wood"), At([L, H, 0.03], 0, 0, D / 2 - 0.015, color=WOOD_OD, mat="wood"),
         At([L, H, 0.03], 0, 0, -D / 2 + 0.015, color=WOOD_OD, mat="wood"), At([0.03, H, D], 0, L / 2 - 0.015, 0, color="#3a4420"),
         At([0.03, H, D], 0, -L / 2 + 0.015, 0, color="#3a4420"), At([0.3, 0.06, 0.01], 0.18, 0, D / 2 + 0.004, color=STENCIL),
         B([L, 0.03, D], [0, H + D / 2 * 0.94, -D / 2 - 0.1], color=WOOD_OD2, pitch=-75, mat="wood")]
    for k in range(4):
        z = -0.19 + k * 0.127
        p += HCyl(0.11, 0.5, 0.14, -0.16, z, color=BRASS)[:2] + HCyl(0.1, 0.32, 0.14, 0.24, z, color="#4a5a2a")[:2] \
            + [B([0.08, 0.05, 0.05], [0.43, 0.14, z], color="#9a9a8c")]
    p.append(At([0.04, 0.06, D - 0.06], 0.04, 0.0, 0, color="#3a4420"))
    prop("Shell Crate", MIL, "An open crate of four artillery shells. A hit sets it smoking; a second later it goes off.", p,
         collider=[C([1.0, 0.4, 0.58], [0, 0.2, 0])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 12},
         breakable=wood(70, [WOOD_OD, BRASS, "#4a5a2a"], count=16, speed=7,
                        explode={"radius": 6, "damage": 140, "fuse": 1.2, "knockback": 10}))
    p = crate_parts(1.3, 0.32, 0.5) + [At([0.03, 0.04, 0.16], 0.17, 0.66, 0, color="tan"), At([0.03, 0.04, 0.16], 0.17, -0.66, 0, color="tan"),
                                       At([0.5, 0.02, 0.2], 0.32, -0.25, 0.0, color="#3a4420")]
    prop("Rifle Crate", MIL, "A long, flat wooden crate for rifles, rope handles at the ends.", p,
         collider=[C([1.32, 0.34, 0.52])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 12}, breakable=wood(60, cols, count=12))
    # Upright shells on a low wooden rack.
    p = [At([0.66, 0.06, 0.44], 0, color="darkwood", mat="wood"), At([0.7, 0.02, 0.06], 0.06, 0, 0.2, color="#3a3426"),
         At([0.7, 0.02, 0.06], 0.06, 0, -0.2, color="#3a3426")]
    for i in range(3):
        for j in range(2):
            x, z = -0.22 + i * 0.22, -0.1 + j * 0.2
            p += Cyl(0.15, 0.44, 0.08, x, z, color=BRASS)[:2] + Cyl(0.14, 0.2, 0.52, x, z, color="#4a5a2a")[:2] \
                + Cyl(0.07, 0.07, 0.72, x, z, color="#a0a092")[:2] + [At([0.155, 0.02, 0.155], 0.48, x, z, color="#c4a24a", yaw=22.5)]
    prop("Artillery Shell Stack", MIL, "Six artillery shells standing on a low wooden rack, brass cases and olive heads.", p,
         collider=[C([0.66, 0.79, 0.44])], scatter={"randomYaw": True, "scale": [0.95, 1.05]})
    r = random.Random(41)
    p = []
    for k in range(16):
        p.append(B([0.06, 0.016, 0.016], [r.uniform(-0.6, 0.6), 0.008, r.uniform(-0.5, 0.5)], color=BRASS, yaw=r.uniform(0, 180)))
    for k in range(2):
        x, z, yw = r.uniform(-0.4, 0.4), r.uniform(-0.4, 0.4), r.uniform(0, 180)
        p += xf(HCyl(0.12, 0.42, 0.06, 0, 0, color=BRASS)[:2], R=mat(yaw=yw), t=(x, 0, z))
    p += [B([0.3, 0.01, 0.06], [0.2, 0.005, -0.3], color="#3a3a32", yaw=30), At([0.25, 0.04, 0.12], 0, -0.45, 0.4, color="#4a5a2a", yaw=50)]
    prop("Spent Shell Casings", MIL, "Brass casings, two big shell cases, a spent belt link and an empty can on the ground. Looks only.", p,
         collision="cosmetic", fade=30, scatter={"randomYaw": True})


def pallet_parts(x=0.0, z=0.0, y0=0.0, col="#6a6448"):
    p = []
    for zz in (-0.45, 0.0, 0.45):
        p.append(At([1.2, 0.09, 0.1], y0 + 0.025, x, z + zz, color="#5a553c", mat="wood"))
    for k in range(7):
        p.append(At([0.13, 0.025, 1.0], y0 + 0.115, x - 0.53 + k * 0.1767, z, color=col, mat="wood"))
    for xx in (-0.53, 0.0, 0.53):
        p.append(At([0.14, 0.025, 1.0], y0, x + xx, z, color=col, mat="wood"))
    return p


def pallets():
    prop("Military Pallet", MIL, "A heavy grey-green wooden pallet, 1.2 x 1.0 m. Step on it.", pallet_parts(),
         collider=[C([1.2, 0.14, 1.0])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10},
         breakable=wood(40, ["#6a6448", "#5a553c"], count=10))
    p = pallet_parts()
    for layer in range(2):
        for x in (-0.3, 0.3):
            for z in (-0.25, 0.25):
                p += crate_parts(0.58, 0.36, 0.48, x, 0.14 + layer * 0.36, z, band=(z > 0 and layer == 1))[:4]
    for x in (-0.3, 0.3):
        p += [At([0.05, 0.012, 1.02], 0.86, x, 0, color="#2a2c26"), At([0.05, 0.72, 0.012], 0.14, x, 0.505, color="#2a2c26"),
              At([0.05, 0.72, 0.012], 0.14, x, -0.505, color="#2a2c26")]
    p += [At([0.4, 0.06, 0.01], 0.72, 0, 0.495, color=STENCIL)]
    prop("Pallet of Ammo Crates", MIL, "Eight ammunition crates strapped on a pallet (0.86 m high). Waist-high cover.", p,
         collider=[C([1.2, 0.87, 1.02])], scatter=SQ, breakable=wood(140, [WOOD_OD, "#6a6448", "#2a2c26"], count=24, size=(0.06, 0.4)))


# ================================================================== Camp ==================================================================

def camo_net():
    r = random.Random(51)
    cols = ["#4b5326", "#5a5a32", "#3a4220", "#6a5a3a", "#545e2c"]
    p = []
    poles = [(-2.2, -1.7), (-2.2, 1.7), (2.2, -1.7), (2.2, 1.7)]
    for x, z in poles:
        p.append(At([0.08, 2.4, 0.08], 0, x, z, color="darkwood", mat="wood"))
    p.append(At([0.09, 2.85, 0.09], 0, 0, 0, color="darkwood", mat="wood"))
    for i in range(6):
        for j in range(5):
            x = -2.5 + i * 1.0
            z = -2.0 + j * 1.0
            k = (x / 2.6) ** 2 + (z / 2.1) ** 2
            y = 2.42 + 0.42 * max(0.0, 1 - k)
            p.append(B([1.15, 0.025, 1.12], [x + r.uniform(-0.1, 0.1), y, z + r.uniform(-0.1, 0.1)], color=r.choice(cols),
                       yaw=r.uniform(-20, 20), pitch=r.uniform(-8, 8), roll=r.uniform(-8, 8)))
    for x, z in poles:  # drapes hanging at the corners and sides
        p.append(B([0.9, 0.7, 0.025], [x * 1.08, 2.05, z * 1.06], color=r.choice(cols), yaw=90 if abs(x) > abs(z) else 0,
                   pitch=r.uniform(-12, 12)))
    for z in (-2.25, 2.25):
        p.append(B([2.2, 0.5, 0.025], [0, 2.18, z], color=r.choice(cols), pitch=10 if z > 0 else -10))
    for x, z in poles:
        p.append(tube([x, 2.38, z], [x * 1.45, 0.02, z * 1.45], 0.015, color="tan"))
        p.append(At([0.05, 0.12, 0.05], 0, x * 1.45, z * 1.45, color="#5a4a30"))
    prop("Camo Net Canopy", MIL, "A 5 x 4 m camouflage net on poles, 2.4-2.85 m up: shade for a gun, a truck or a nest. Only the poles block.", p,
         collider=[C([0.1, 2.4, 0.1], [x, 1.2, z]) for x, z in poles] + [C([0.1, 2.4, 0.1], [0, 1.2, 0])], scatter=SQ)


def field_radio():
    c = "#4a5230"
    p = [At([0.42, 0.48, 0.24], 0, color=c, mat="metal"), At([0.44, 0.03, 0.26], 0.48, color="#3a4226"),
         At([0.36, 0.3, 0.01], 0.12, 0, 0.125, color="#2a2c26")]
    for k, (x, y) in enumerate(((-0.1, 0.34), (0.05, 0.34), (0.12, 0.2), (-0.08, 0.2))):
        p.append(At([0.04, 0.04, 0.03], y, x, 0.135, color="#111111" if k % 2 else "#d8d8c8"))
    p += [At([0.12, 0.05, 0.01], 0.38, 0.1, 0.13, color="#c8e0a0"), At([0.04, 0.02, 0.06], 0.5, 0.15, 0.06, color="#1e1e1c"),
          tube([0.15, 0.52, 0.06], [0.2, 1.75, 0.0], 0.012, color="#2a2a26"),
          At([0.05, 0.2, 0.05], 0.0, -0.3, 0.1, color="#1e1e1c", yaw=30), At([0.07, 0.07, 0.05], 0.2, -0.3, 0.1, color="#1e1e1c"),
          tube([-0.3, 0.22, 0.1], [-0.18, 0.3, 0.12], 0.012, color="#1e1e1c"),
          At([0.05, 0.4, 0.03], 0.05, -0.12, -0.13, color="#5a5a3a"), At([0.05, 0.4, 0.03], 0.05, 0.12, -0.13, color="#5a5a3a")]
    prop("Field Radio Pack", MIL, "A backpack field radio with its whip antenna up and the handset beside it. Looks only.", p,
         collision="cosmetic", fade=45, scatter={"randomYaw": True})


def gable(x, w, hw, hr, color, t=0.04, steps=4):
    """A stepped triangular gable end at x: wall `hw` high and `w` wide, rising `hr` more to the ridge."""
    p = [At([t, hw, w], 0, x, 0, color=color)]
    for k in range(steps):
        f = (k + 0.5) / steps
        p.append(At([t, hr / steps, w * (1 - f)], hw + k * hr / steps, x, 0, color=color))
    return p


def tents():
    can, can2 = "#6b6a45", "#5d5e3b"
    L, W, hw, hr = 4.0, 3.0, 0.9, 1.4
    ang = math.degrees(math.atan2(hr, W / 2))
    ln = math.hypot(hr, W / 2) + 0.06
    p = [At([L, hw, 0.04], 0, 0, W / 2, color=can), At([L, hw, 0.04], 0, 0, -W / 2, color=can)]
    for s in (-1, 1):
        p.append(B([L + 0.2, 0.04, ln], [0, hw + hr / 2, s * W / 4], color=can2, pitch=s * ang))
    p += gable(-L / 2, W, hw, hr, can) + gable(L / 2, W, hw, hr, can)
    p += [At([0.02, 1.5, 0.9], 0, L / 2 + 0.01, 0, color="#2a2a20"), At([0.06, 1.2, 0.18], 0.4, L / 2 + 0.04, 0.55, color=can2),
          At([0.06, 0.06, L + 0.3], hw + hr - 0.03, 0, 0, color="darkwood", yaw=90)]
    for x in (-L / 2 - 0.05, L / 2 + 0.05):
        p.append(At([0.05, hw + hr + 0.15, 0.05], 0, x, 0, color="darkwood"))
        p.append(tube([x, hw + hr, 0], [x + (1.2 if x > 0 else -1.2), 0.02, 0], 0.015, color="tan"))
    for x in (-1.4, 0, 1.4):
        for s in (-1, 1):
            p.append(tube([x, hw, s * W / 2], [x, 0.02, s * (W / 2 + 0.7)], 0.012, color="tan"))
    p.append(At([L + 0.6, 0.01, W + 0.8], 0, color="#5a4a36"))
    prop("Field Tent", MIL, "A 4 x 3 m canvas ridge tent (2.3 m to the ridge), door at +X, guy ropes out. Closed: cover, not a room.", p,
         collider=[C([L, hw, W]), C([L, 0.7, 1.6], [0, hw + 0.35, 0]), C([L, 0.5, 0.55], [0, hw + 0.95, 0])], scatter=SQ)
    # Command tent: walk-in, door gap at +X, a map table inside.
    L, W, hw, hr = 6.0, 5.0, 2.1, 1.0
    ang = math.degrees(math.atan2(hr, W / 2))
    ln = math.hypot(hr, W / 2) + 0.1
    p = [At([L, hw, 0.06], 0, 0, W / 2, color=can), At([L, hw, 0.06], 0, 0, -W / 2, color=can),
         At([0.06, hw, W], 0, -L / 2, 0, color=can), At([0.06, hw, 1.7], 0, L / 2, 1.65, color=can), At([0.06, hw, 1.7], 0, L / 2, -1.65, color=can),
         At([0.06, 0.3, 1.6], hw - 0.3, L / 2, 0, color=can2)]
    for s in (-1, 1):
        p.append(B([L + 0.3, 0.05, ln], [0, hw + hr / 2, s * W / 4], color=can2, pitch=s * ang))
        p.append(At([L + 0.3, 0.25, 0.04], hw - 0.2, 0, s * (W / 2 + 0.06), color=can2))
        for x in (-1.6, 1.4):
            p.append(At([1.0, 0.6, 0.02], 1.0, x, s * (W / 2 + 0.035), color="#3a3a2a"))
    for x in (-L / 2, L / 2):
        for k in range(3):
            f = (k + 0.5) / 3
            p.append(At([0.06, hr / 3, W * (1 - f)], hw + k * hr / 3, x, 0, color=can))
    p += [At([0.08, 1.4, 0.3], 0.5, L / 2 + 0.06, 0.95, color=can2), At([0.08, 1.4, 0.3], 0.5, L / 2 + 0.06, -0.95, color=can2)]
    # inside: map table, two folding chairs, a radio and a lantern
    p += [At([1.8, 0.04, 0.9], 0.74, -0.8, 0, color="#7a6a4a", mat="wood")]
    for x in (-1.6, 0.0):
        for z in (-0.38, 0.38):
            p.append(At([0.04, 0.74, 0.04], 0, x, z, color=STEEL))
    p += [At([1.2, 0.006, 0.7], 0.78, -0.8, 0, color="#d8cfa8"), At([0.3, 0.008, 0.2], 0.787, -1.0, 0.1, color="#6a8a5a"),
          At([0.04, 0.12, 0.04], 0.78, -0.3, 0.3, color="#ffe9a8")]
    for z in (-0.75, 0.75):
        p += [At([0.4, 0.04, 0.4], 0.45, -0.8, z, color="#4a5230"), At([0.4, 0.4, 0.04], 0.49, -0.8, z + (0.2 if z > 0 else -0.2), color="#4a5230"),
              At([0.03, 0.45, 0.4], 0, -0.98, z, color=STEEL), At([0.03, 0.45, 0.4], 0, -0.62, z, color=STEEL)]
    p += crate_parts(0.9, 0.42, 0.45, -2.4, 0, 1.9, band=False)[:2] + crate_parts(0.9, 0.42, 0.45, -2.4, 0, -1.9)[:2]
    p += [At([0.42, 0.48, 0.24], 0.42, -2.4, 1.9, color="#4a5230"), At([0.03, 2.8, 0.03], 0, 0, 0, color="darkwood")]
    p.append(At([L + 1.0, 0.01, W + 1.0], 0, color="#5a4a36"))
    prop("Command Tent", MIL, "A 6 x 5 m walk-in command tent (2.1 m walls), door at +X, with a map table, chairs and a radio inside.", p,
         collider=[C([L, hw, 0.08], [0, hw / 2, W / 2]), C([L, hw, 0.08], [0, hw / 2, -W / 2]), C([0.08, hw, W], [-L / 2, hw / 2, 0]),
                   C([0.08, hw, 1.7], [L / 2, hw / 2, 1.65]), C([0.08, hw, 1.7], [L / 2, hw / 2, -1.65]), C([1.8, 0.78, 0.9], [-0.8, 0.39, 0])],
         scatter=SQ0)


def drum_lying(x, y, z, col, band):
    p = HCyl(0.58, 0.86, y, x, z, color=col, alongZ=True)
    p += HCyl(0.6, 0.03, y, x, z - 0.28, color=band, alongZ=True)[:2] + HCyl(0.6, 0.03, y, x, z + 0.28, color=band, alongZ=True)[:2]
    p += [B([0.08, 0.08, 0.02], [x + 0.15, y + 0.12, z + 0.435], color="#2a2c26")]
    return p


def fuel():
    fr = "#3a3c36"
    p = []
    for lvl in range(2):
        y = 0.12 + 0.29 + lvl * 0.62
        for k in range(3):
            p += drum_lying(-0.63 + k * 0.63, y, 0, "#4d5a2a" if (k + lvl) % 3 else "#5a3a28", "#d8d8c8")
    for x in (-0.98, 0.98):
        for z in (-0.4, 0.4):
            p.append(At([0.07, 1.42, 0.07], 0, x, z, color=fr))
    for lvl in range(2):
        for z in (-0.4, 0.4):
            p.append(At([2.0, 0.07, 0.07], 0.05 + lvl * 0.62, 0, z, color=fr))
    p.append(At([2.0, 0.05, 0.07], 1.38, 0, 0.4, color=fr))
    p.append(At([0.3, 0.2, 0.01], 1.15, 0.98, 0.44, color="#c42a1c", yaw=0))
    prop("Fuel Drum Rack", MIL, "A steel rack of six fuel drums lying in two rows. Shoot it and run: it burns for a second, then goes up.", p,
         collider=[C([2.04, 1.42, 0.92])], scatter=SQ,
         breakable=metal(170, ["#4d5a2a", "#5a3a28", fr, "orange"], count=18, size=(0.08, 0.35), speed=8,
                         explode={"radius": 7.5, "damage": 160, "fuse": 1.2, "knockback": 12}))
    col = "#4d5a2a"
    p = Cyl(0.58, 0.86, 0.01, color=col, mat="metal") + Cyl(0.6, 0.03, 0.29, color="#46522a")[:2] + Cyl(0.6, 0.03, 0.58, color="#46522a")[:2]
    p += Cyl(0.6, 0.03, 0.86, color="#3e4826")[:2] + [At([0.42, 0.012, 0.42], 0.87, color="#46522a", yaw=22.5),
                                                       At([0.07, 0.03, 0.07], 0.88, 0.15, 0.1, color="#2a2c26"), At([0.05, 0.03, 0.05], 0.88, -0.16, -0.1, color="#2a2c26"),
                                                       At([0.24, 0.1, 0.01], 0.45, 0, 0.29, color="#d8d8c8")]
    prop("Military Fuel Drum", MIL, "An olive steel fuel drum standing upright. Explodes a moment after it's holed.", p,
         collider=[C([0.6, 0.9, 0.6])], scatter={"randomYaw": True, "scale": [0.97, 1.03]},
         breakable=metal(60, [col, "orange", "#2a2c26"], count=10, size=(0.06, 0.24), speed=8,
                         explode={"radius": 5, "damage": 110, "fuse": 0.8, "knockback": 9}))
    p = []
    for x in (-0.68, 0.68):
        for z in (-0.18, 0.18):
            p.append(At([0.05, 1.05, 0.05], 0, x, z, color=fr))
    for y in (0.05, 0.55):
        p.append(At([1.42, 0.04, 0.42], y, color=fr))
    p.append(At([1.42, 0.05, 0.03], 1.0, 0, -0.18, color=fr))
    for lvl in range(2):
        for k in range(4):
            x = -0.5 + k * 0.33
            y0 = 0.09 + lvl * 0.5
            c = "#4d5a2a" if (k + lvl) % 4 else "#5c5236"
            p += [At([0.16, 0.42, 0.34], y0, x, 0, color=c),
                  B([0.012, 0.44, 0.012], [0.0, 0.0, 0.0], color="#3e4826"),  # placeholder replaced below
                  At([0.1, 0.04, 0.03], y0 + 0.42, x, -0.06, color="#2a2c26"), At([0.04, 0.05, 0.04], y0 + 0.42, x, 0.11, color="#2a2c26")]
            # the embossed X on the can's side (+Z face)
            p[-3] = B([0.01, 0.4, 0.012], [x, y0 + 0.21, 0.173], color="#3e4826", roll=20)
            p.append(B([0.01, 0.4, 0.012], [x, y0 + 0.21, 0.173], color="#3e4826", roll=-20))
    prop("Jerrycan Rack", MIL, "A steel shelf of eight jerrycans. Explodes about a second after it's shot through.", p,
         collider=[C([1.42, 1.05, 0.42])], scatter=SQ,
         breakable=metal(90, ["#4d5a2a", "orange", fr], count=14, size=(0.06, 0.25), speed=8,
                         explode={"radius": 5.5, "damage": 120, "fuse": 1.0, "knockback": 10}))


def searchlight():
    g, dk = "#4a5230", "#2e3220"
    p = [At([0.9, 0.08, 0.9], 0.12, color=dk), At([0.12, 0.12, 0.12], 0, 0.38, 0.38, color=dk), At([0.12, 0.12, 0.12], 0, -0.38, 0.38, color=dk),
         At([0.12, 0.12, 0.12], 0, 0.38, -0.38, color=dk), At([0.12, 0.12, 0.12], 0, -0.38, -0.38, color=dk)]
    p += Cyl(0.16, 0.95, 0.2, color=g)[:2] + [At([0.4, 0.3, 0.3], 0.2, -0.2, 0, color=g)]
    head = [At([0.16, 0.06, 0.16], 1.15, color=dk), B([0.1, 0.6, 0.06], [0, 1.5, 0.52], color=g), B([0.1, 0.6, 0.06], [0, 1.5, -0.52], color=g),
            B([0.16, 0.06, 1.1], [0, 1.2, 0], color=g)]
    head += HCyl(0.9, 0.8, 1.6, 0.05, 0, color=g) + HCyl(0.95, 0.08, 1.6, 0.45, 0, color=dk)[:2] + HCyl(0.6, 0.1, 1.6, -0.38, 0, color=dk)[:2]
    head += [B([0.3, 0.05, 0.05], [0.0, 2.08, 0], color=dk)]
    p += head
    # Still (an animated prop would have to join the Animated Props gallery): a pale lens, unlit.
    p += HCyl(0.8, 0.02, 1.6, 0.5, 0, color="#f4ecd0")
    p += [tube([-0.2, 0.3, 0.15], [-0.9, 0.02, 0.6], 0.03, color="#1e1e1c")]
    prop("Field Searchlight", MIL, "A big drum searchlight on a stand, aimed to +X.", p,
         collider=[C([0.9, 1.2, 0.9], [0, 0.6, 0])], scatter=SQ0)


def guard_tower():
    w = "#6a5638"
    p = []
    H = 4.0
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(tube([sx * 1.25, 0.0, sz * 1.25], [sx * 1.0, H, sz * 1.0], 0.2, color=w))
    for s in (-1, 1):
        for a, b in (((-1.18, 0.3), (1.05, 3.6)), ((1.18, 0.3), (-1.05, 3.6))):
            p.append(tube([a[0], a[1], s * 1.14], [b[0], b[1], s * 1.04], 0.08, color="#5a4a30"))
            p.append(tube([s * 1.14, a[1], a[0]], [s * 1.04, b[1], b[0]], 0.08, color="#5a4a30"))
    p += [At([2.6, 0.15, 2.6], H, color=w, mat="wood")]
    for s in (-1, 1):
        p.append(At([2.6, 1.05, 0.06], H + 0.15, 0, s * 1.27, color="#7a6444", mat="wood"))
        p.append(At([0.06, 1.05, 2.6], H + 0.15, s * 1.27, 0, color="#7a6444", mat="wood"))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(At([0.1, 1.2, 0.1], H + 1.2, sx * 1.25, sz * 1.25, color=w))
    for s in (-1, 1):
        p.append(B([3.2, 0.06, 1.75], [0, H + 2.75, s * 0.8], color="#4a4a3a", pitch=s * 22))
    p += [At([0.08, 4.0, 0.06], 0, -1.6, 0.25, color=w), At([0.08, 4.0, 0.06], 0, -1.6, -0.25, color=w)]
    for k in range(9):
        p.append(At([0.06, 0.04, 0.5], 0.4 + k * 0.42, -1.6, 0, color="#5a4a30"))
    p += [At([0.4, 0.4, 0.5], H + 1.2, 1.2, 0, color="#4a5230")]
    prop("Guard Tower", MIL, "A 4 m wooden watchtower with a planked, roofed lookout and a ladder at -X (6.8 m tall). Bring a Block to stand up there.", p,
         collider=[C([0.3, H, 0.3], [sx * 1.12, H / 2, sz * 1.12]) for sx in (-1, 1) for sz in (-1, 1)] + [C([2.6, 1.2, 2.6], [0, H + 0.6, 0])],
         scatter=SQ0)


# ================================================================== Tanks ==================================================================

def track(L, H, w, z, nw, wd, pal, skirt=0.0):
    """One track run along X (a stadium shape), road wheels on its outer face, a fender on top; optional side skirt
    covering the top `skirt` fraction."""
    s = 1 if z > 0 else -1
    p = [At([L - H, H, w], 0, 0, z, color=TRACK, mat="grid", tex=3)]
    p += HCyl(H, w, H / 2, (L - H) / 2, z, color=TRACK, alongZ=True)[:2] + HCyl(H, w, H / 2, -(L - H) / 2, z, color=TRACK, alongZ=True)[:2]
    span = L - H * 0.9
    for k in range(nw):
        x = -span / 2 + span * (k + 0.5) / nw
        p += disc_z(wd, 0.06, x, wd / 2 + 0.06, z + s * (w / 2 + 0.01), color=WHEEL)
    p += disc_z(H * 0.8, 0.07, (L - H) / 2, H / 2, z + s * (w / 2 + 0.015), color=pal["dark"])
    p.append(At([L + 0.1, 0.04, w + 0.06], H, 0, z, color=pal["body"]))
    if skirt > 0:
        p.append(At([L - 0.2, H * skirt, 0.04], H * (1 - skirt) + 0.02, 0, z + s * (w / 2 + 0.06), color=pal["body"], mat="metal"))
    return p


def medium_tank(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    L, W, TH, TW = 6.0, 2.8, 1.0, 0.55
    p = track(L, TH, TW, W / 2 - TW / 2, 5, 0.58, pal) + track(L, TH, TW, -(W / 2 - TW / 2), 5, 0.58, pal)
    p += [At([5.3, 0.75, W - 2 * TW], 0.35, color=dk), At([4.4, 0.58, W], 1.02, -0.35, color=c, mat="metal"),
          B([1.15, 0.12, W - 0.1], [2.25, 1.15, 0], color=c, roll=-38), B([0.6, 0.5, W - 0.3], [2.55, 0.68, 0], color=dk, roll=-60),
          At([1.6, 0.04, 1.8], 1.6, -2.0, 0, color=dk), At([1.4, 0.03, 0.12], 1.64, -2.0, 0.4, color="#1e1e1c"),
          At([1.4, 0.03, 0.12], 1.64, -2.0, -0.4, color="#1e1e1c"), B([0.1, 0.6, W - 0.2], [-2.6, 1.3, 0], color=c, roll=15)]
    p += HCyl(0.12, 0.3, 1.25, -2.8, 0.7, color=RUST)[:2] + HCyl(0.12, 0.3, 1.25, -2.8, -0.7, color=RUST)[:2]
    p += [At([0.4, 0.07, 0.4], 1.6, 1.5, 0.75, color=dk), At([0.4, 0.07, 0.4], 1.6, 1.5, -0.75, color=dk),     # driver hatches
          At([0.18, 0.18, 0.14], 1.3, 2.75, 1.1, color="#fff2c0"), At([0.18, 0.18, 0.14], 1.3, 2.75, -1.1, color="#fff2c0")]
    for k in range(3):
        p.append(B([0.28, 0.06, 0.55], [2.1 + k * 0.16, 1.32 - k * 0.12, -0.75], color=TRACK, roll=-38))
    # turret
    tx, ty = 0.15, 1.6
    p += Cyl(1.95, 0.72, ty, tx, 0, color=c, mat="metal") + Cyl(1.55, 0.12, ty + 0.72, tx, 0, color=c)
    p += [At([0.4, 0.55, 0.85], ty + 0.06, tx + 1.05, 0, color=dk), At([0.9, 0.5, 1.2], ty + 0.1, tx - 1.05, 0, color=c)]
    p += HCyl(0.13, 2.6, ty + 0.36, tx + 2.5, 0, color=c) + HCyl(0.18, 0.22, ty + 0.36, tx + 3.75, 0, color=dk)[:2]
    p += Cyl(0.55, 0.22, ty + 0.84, tx - 0.35, 0.35, color=dk)[:2] + [At([0.42, 0.04, 0.42], ty + 0.84, tx + 0.25, -0.4, color=dk)]
    p += [tube([tx - 0.35, ty + 1.06, 0.35], [tx + 0.3, ty + 1.2, 0.35], 0.05, color="#1e1e1c"), tube([tx - 0.6, ty + 0.84, -0.5], [tx - 0.9, ty + 2.9, -0.5], 0.015, color="#1e1e1c")]
    p += HCyl(0.28, 1.0, ty + 0.4, tx - 1.55, 0, color=pal["canvas"], alongZ=True)[:2]
    p += star(0.55, tx + 0.15, ty + 0.4, 0.99, axis="z") + star(0.55, tx + 0.15, ty + 0.4, -0.99, axis="z")
    p += [At([0.6, 0.3, 0.3], 1.06, -1.0, W / 2 + 0.02, color=dk), At([0.25, 0.38, 0.14], 1.06, -2.2, 1.0, color=lt), At([0.25, 0.38, 0.14], 1.06, -2.2, 0.75, color=lt)]
    prop("Medium Tank (WWII)" + pal["id"], VEH, "A WWII-era medium tank: cast round turret, sloped glacis, five road wheels a side. Gun to +X; climb on the hull.",
         ground(p), collider=[C([6.0, 1.6, 2.86]), C([1.95, 0.84, 1.95], [0.15, 2.02, 0])], scatter=BIG)


def heavy_tank(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    L, W, TH, TW = 7.0, 3.4, 1.15, 0.72
    p = track(L, TH, TW, W / 2 - TW / 2, 6, 0.75, pal, skirt=0.45) + track(L, TH, TW, -(W / 2 - TW / 2), 6, 0.75, pal, skirt=0.45)
    p += [At([6.4, 0.7, W - 2 * TW], 0.4, color=dk), At([6.2, 0.55, W], 1.15, -0.15, color=c, mat="metal"),
          B([0.12, 0.9, W - 0.1], [3.08, 1.12, 0], color=c, roll=-12), At([2.0, 0.05, 2.4], 1.7, -2.2, 0, color=dk),
          At([0.25, 0.25, 0.25], 1.45, 2.95, 1.2, color="#fff2c0"), At([0.25, 0.25, 0.25], 1.45, 2.95, -1.2, color="#fff2c0"),
          At([0.35, 0.12, 0.2], 1.7, 2.6, 0.9, color="#1e1e1c"), At([0.45, 0.08, 0.45], 1.7, 2.2, -0.9, color=dk)]
    for k in range(4):
        p.append(At([0.55, 0.3, 0.06], 1.25, -2.4 + k * 0.6, W / 2 + 0.02, color=TRACK))
    tx, ty = 0.2, 1.7
    p += [At([2.7, 0.95, 2.5], ty, tx - 0.15, 0, color=c, mat="metal"), B([0.5, 0.9, 2.3], [tx + 1.3, ty + 0.45, 0], color=c, roll=-18),
          B([2.4, 0.9, 0.12], [tx - 0.1, ty + 0.45, 1.3], color=c, yaw=6), B([2.4, 0.9, 0.12], [tx - 0.1, ty + 0.45, -1.3], color=c, yaw=-6),
          At([0.5, 0.65, 1.0], ty + 0.12, tx + 1.55, 0, color=dk), At([2.6, 0.06, 2.4], ty + 0.95, tx - 0.15, 0, color=lt)]
    p += HCyl(0.18, 4.2, ty + 0.45, tx + 3.85, 0, color=c) + HCyl(0.26, 0.5, ty + 0.45, tx + 2.1, 0, color=dk)[:2]
    p += [At([0.4, 0.26, 0.42], ty + 0.32, tx + 6.0, 0, color=dk), At([0.3, 0.3, 0.08], ty + 0.3, tx + 6.0, 0.26, color=dk),
          At([0.3, 0.3, 0.08], ty + 0.3, tx + 6.0, -0.26, color=dk)]
    p += Cyl(0.65, 0.3, ty + 0.95, tx - 0.6, 0.55, color=dk)[:2] + [At([0.5, 0.05, 0.5], ty + 0.97, tx - 0.4, -0.6, color=dk),
                                                                    tube([tx - 1.2, ty + 0.95, 0.9], [tx - 1.4, ty + 3.2, 0.9], 0.015, color="#1e1e1c")]
    for s in (-1, 1):
        p += [At([0.5, 0.25, 0.06], ty + 0.4, tx - 0.6, s * 1.37, color=TRACK), At([0.5, 0.25, 0.06], ty + 0.4, tx - 0.05, s * 1.37, color=TRACK)]
    p += star(0.6, tx - 1.0, ty + 0.5, 1.37, axis="z") + star(0.6, tx - 1.0, ty + 0.5, -1.37, axis="z")
    prop("Heavy Tank" + pal["id"], VEH, "A boxy heavy tank: thick flat armour, side skirts, a big turret and a long gun with a muzzle brake. Gun to +X.",
         ground(p), collider=[C([7.0, 1.7, 3.5]), C([3.0, 0.95, 2.6], [0.25, 2.18, 0])], scatter=BIG)


def mbt(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    L, W, TH, TW = 7.6, 3.6, 0.95, 0.65
    p = track(L, TH, TW, W / 2 - TW / 2, 7, 0.7, pal, skirt=0.6) + track(L, TH, TW, -(W / 2 - TW / 2), 7, 0.7, pal, skirt=0.6)
    p += [At([7.0, 0.6, W - 2 * TW], 0.38, color=dk), At([6.6, 0.5, W], 0.98, -0.4, color=c, mat="metal"),
          B([1.6, 0.1, W - 0.05], [3.45, 1.15, 0], color=c, roll=-14), B([0.7, 0.6, W - 0.4], [3.6, 0.6, 0], color=dk, roll=-55),
          At([2.2, 0.05, 2.8], 1.48, -2.5, 0, color=dk), At([0.5, 0.06, 0.6], 1.48, 2.4, 0, color=dk),
          At([0.3, 0.12, 0.2], 1.2, 3.95, 1.4, color="#fff2c0"), At([0.3, 0.12, 0.2], 1.2, 3.95, -1.4, color="#fff2c0")]
    for k in range(3):
        p.append(At([1.9, 0.02, 0.1], 1.53, -2.5, -0.9 + k * 0.9, color="#1e1e1c"))
    tx, ty = -0.4, 1.48
    p += [At([3.4, 0.72, 3.2], ty, tx, 0, color=c, mat="metal"), At([1.5, 0.62, 2.9], ty + 0.06, tx - 2.3, 0, color=c),
          B([1.5, 0.7, 0.14], [tx + 1.95, ty + 0.36, 0.95], color=c, yaw=28), B([1.5, 0.7, 0.14], [tx + 1.95, ty + 0.36, -0.95], color=c, yaw=-28),
          B([1.0, 0.7, 1.15], [tx + 2.05, ty + 0.36, 0], color=c), B([1.6, 0.08, 3.0], [tx + 0.9, ty + 0.73, 0], color=lt, roll=-5),
          At([0.45, 0.55, 0.55], ty + 0.1, tx + 2.65, 0, color=dk)]
    p += HCyl(0.19, 5.2, ty + 0.4, tx + 5.3, 0, color=c) + HCyl(0.27, 0.6, ty + 0.4, tx + 4.3, 0, color=dk)[:2] \
        + HCyl(0.22, 0.25, ty + 0.4, tx + 7.82, 0, color=dk)[:2]
    for x in (tx + 3.3, tx + 5.6):
        p += HCyl(0.215, 0.06, ty + 0.4, x, 0, color=dk)[:2]
    p += [At([0.45, 0.38, 0.45], ty + 0.72, tx + 0.2, 0.85, color=dk), B([0.04, 0.22, 0.36], [tx + 0.44, ty + 0.92, 0.85], color=GLASS),
          At([0.4, 0.3, 0.3], ty + 0.72, tx + 1.2, -0.95, color=dk), B([0.04, 0.2, 0.26], [tx + 1.41, ty + 0.87, -0.95], color=GLASS),
          At([0.55, 0.05, 0.55], ty + 0.72, tx - 0.7, -0.8, color=dk), tube([tx + 0.2, ty + 1.15, 0.85], [tx + 1.0, ty + 1.2, 0.85], 0.06, color="#1e1e1c")]
    for s in (-1, 1):
        for k in range(2):
            p.append(B([0.32, 0.12, 0.12], [tx + 1.35, ty + 0.62, s * (1.45 + k * 0.14)], color=dk, roll=25))
        p.append(At([1.4, 0.04, 0.04], ty + 0.75, tx - 2.3, s * 1.5, color=STEEL))
        p.append(At([0.04, 0.4, 0.04], ty + 0.45, tx - 3.05, s * 1.5, color=STEEL))
    p.append(At([0.04, 0.4, 2.9], ty + 0.45, tx - 3.05, 0, color=STEEL))
    p += [At([0.6, 0.3, 0.9], ty + 0.62, tx - 2.4, 0.6, color=pal["canvas"]), At([0.5, 0.25, 0.6], ty + 0.62, tx - 2.4, -0.7, color="#4a4a3a"),
          tube([tx - 1.5, ty + 0.68, -1.2], [tx - 1.7, ty + 3.2, -1.2], 0.015, color="#1e1e1c"),
          tube([tx - 1.5, ty + 0.68, 1.2], [tx - 1.6, ty + 2.6, 1.2], 0.015, color="#1e1e1c")]
    p += [At([0.6, 0.35, 0.02], 0.55, 2.0, W / 2 + 0.07, color=lt), At([0.6, 0.35, 0.02], 0.55, 2.0, -W / 2 - 0.07, color=lt)]
    prop("Main Battle Tank" + pal["id"], VEH, "A modern main battle tank: low angular turret, side skirts, sights, smoke launchers, a long sleeved gun. Gun to +X.",
         ground(p), collider=[C([7.6, 1.48, 3.7], [0, 0.74, 0]), C([4.9, 0.78, 3.2], [-1.15, 1.87, 0])], scatter=BIG)


def light_tank(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    L, W, TH, TW = 4.4, 2.3, 0.8, 0.42
    p = track(L, TH, TW, W / 2 - TW / 2, 4, 0.62, pal) + track(L, TH, TW, -(W / 2 - TW / 2), 4, 0.62, pal)
    p += [At([3.9, 0.6, W - 2 * TW], 0.3, color=dk), At([3.6, 0.52, W - 0.1], 0.82, -0.1, color=c, mat="metal"),
          B([0.7, 0.1, W - 0.2], [1.9, 1.05, 0], color=c, roll=-30), At([0.5, 0.25, 0.35], 1.34, 1.2, 0.5, color=c),
          B([0.03, 0.08, 0.2], [1.46, 1.48, 0.5], color="#1e1e1c"), At([0.16, 0.16, 0.12], 0.95, 2.05, 0.85, color="#fff2c0"),
          At([0.16, 0.16, 0.12], 0.95, 2.05, -0.85, color="#fff2c0"), At([1.2, 0.04, 1.4], 1.34, -1.3, 0, color=dk)]
    for k in range(6):  # rivets
        p += [At([0.04, 0.04, 0.02], 1.1, -1.6 + k * 0.6, W / 2 - 0.04, color=lt), At([0.04, 0.04, 0.02], 1.1, -1.6 + k * 0.6, -W / 2 + 0.04, color=lt)]
    tx, ty = -0.2, 1.34
    p += Cyl(1.3, 0.58, ty, tx, 0, color=c, mat="metal") + [At([1.15, 0.05, 1.15], ty + 0.58, tx, 0, color=lt, yaw=22.5),
                                                            At([0.3, 0.36, 0.5], ty + 0.1, tx + 0.7, 0, color=dk)]
    p += HCyl(0.09, 1.5, ty + 0.3, tx + 1.55, 0, color=c) + HCyl(0.05, 0.6, ty + 0.3, tx + 1.05, 0.17, color="#1e1e1c")[:2]
    p += [At([0.4, 0.05, 0.4], ty + 0.6, tx - 0.15, 0.1, color=dk), tube([tx - 0.4, ty + 0.6, -0.4], [tx - 0.6, ty + 2.4, -0.4], 0.012, color="#1e1e1c")]
    p += star(0.42, 0.0, 0.9, W / 2 + 0.005, axis="z") + star(0.42, 0.0, 0.9, -W / 2 - 0.005, axis="z")
    p += [At([0.5, 0.25, 0.25], 0.82, -2.0, 0.7, color=pal["canvas"]), At([0.18, 0.36, 0.13], 0.82, -2.05, -0.6, color=lt)]
    prop("Light Tank" + pal["id"], VEH, "A small riveted light tank with a one-man turret and a short gun. Gun to +X; easy to vault onto.",
         ground(p), collider=[C([4.4, 1.34, 2.36], [0, 0.67, 0]), C([1.3, 0.6, 1.3], [-0.2, 1.64, 0])], scatter=BIG)


def wrecked_tank():
    pal = {"body": "#4a3e34", "dark": BURNT, "light": "#5a4636", "canvas": "#3a3028", "id": ""}
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    L, W, TH, TW = 6.0, 2.8, 1.0, 0.55
    p = track(L, TH, TW, -(W / 2 - TW / 2), 5, 0.58, pal)
    z = W / 2 - TW / 2
    for k in range(5):
        p += disc_z(0.58, 0.06, -2.3 + k * 1.15, 0.35, z + 0.29, color="#3a322c")
    p += [At([3.2, 0.05, 0.5], 0, -4.2, z + 0.4, color=TRACK, yaw=8), At([1.6, 0.05, 0.5], 0, 2.1, z + 0.05, color=TRACK, yaw=-4)]
    p += [At([5.3, 0.75, W - 2 * TW], 0.35, color=dk), At([4.4, 0.58, W], 1.02, -0.35, color=c, mat="metal"),
          B([1.15, 0.12, W - 0.1], [2.25, 1.15, 0], color=c, roll=-38), B([0.6, 0.5, W - 0.3], [2.55, 0.68, 0], color=dk, roll=-60),
          At([1.6, 0.04, 1.8], 1.6, -2.0, 0, color=dk), B([0.1, 0.6, W - 0.2], [-2.6, 1.3, 0], color=c, roll=15)]
    p += Cyl(1.5, 0.02, 1.6, 0.15, 0, color="#141210")[:2] + Cyl(1.7, 0.03, 1.6, 0.15, 0, color=RUST)[:2]
    p += [At([0.35, 0.3, 0.02], 1.15, 0.8, W / 2 + 0.005, color="#141210"), At([0.25, 0.22, 0.02], 0.6, -1.2, -W / 2 - 0.005, color="#141210"),
          B([0.3, 0.25, 0.02], [1.9, 1.3, 1.0], color="#141210", roll=-38), At([0.7, 0.3, 0.35], 1.6, -1.0, 0.6, color=RUST, yaw=20)]
    tur = Cyl(1.95, 0.72, 0, 0, 0, color=c, mat="metal") + Cyl(1.55, 0.12, 0.72, 0, 0, color=lt) \
        + [At([0.4, 0.55, 0.85], 0.06, 1.05, 0, color=dk), At([0.9, 0.5, 1.2], 0.1, -1.05, 0, color=c)] \
        + HCyl(0.13, 2.4, 0.36, 2.4, 0, color=lt) + [At([0.5, 0.3, 0.02], 0.25, 0, 0.98, color="#141210")]
    p += xf(tur, R=mmul(mat(yaw=-55), mat(roll=7, pitch=9)), t=(0.6, 0.2, 3.3))
    r = random.Random(61)
    for k in range(8):
        p.append(At([r.uniform(0.15, 0.4), r.uniform(0.05, 0.15), r.uniform(0.15, 0.4)], 0, r.uniform(-3, 3), r.uniform(1.7, 2.8), color=r.choice([RUST, BURNT, "#3a3430"]), yaw=r.uniform(0, 90)))
    p.append(At([7.0, 0.012, 5.5], 0, 0, 1.0, color="#1c1a18"))
    prop("Wrecked Tank", VEH, "A burnt-out medium tank: turret blown off and lying beside the hull (at +Z), one track broken and run off. Cover.",
         p, collider=[C([6.0, 1.6, 2.86]), C([2.0, 1.1, 2.0], [0.6, 0.55, 3.25])], scatter=BIG)


# ================================================================== Planes ==================================================================

def fighter(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    a = 8.0
    sa, ca = math.sin(math.radians(a)), math.cos(math.radians(a))
    h = 1.57
    air = HCyl(1.3, 1.4, 0, 2.4, 0, color=c) + HCyl(1.34, 0.16, 0, 3.08, 0, color=dk) + HCyl(1.1, 0.2, 0, 1.6, 0, color=dk)[:2]
    air += [B([0.4, 0.34, 0.34], [3.32, 0, 0], color=lt, pitch=45)]
    for k in range(3):
        th = math.radians(90 + 120 * k)
        air.append(B([0.05, 1.45, 0.22], [3.24, 0.74 * math.sin(th), 0.74 * math.cos(th)], color="#1e1e1c", pitch=-math.degrees(th) + 90))
    air += HCyl(1.2, 3.0, 0, 0.3, 0, color=c) + HCyl(1.0, 2.0, -0.05, -2.2, 0, color=c) + HCyl(0.72, 1.8, -0.02, -4.0, 0, color=c) \
        + HCyl(0.46, 1.5, 0.05, -5.5, 0, color=c)
    air += [B([1.3, 0.45, 0.62], [0.05, 0.68, 0], color=CANOPY), B([0.06, 0.48, 0.66], [0.3, 0.68, 0], color=dk),
            B([0.06, 0.48, 0.66], [-0.3, 0.68, 0], color=dk), B([1.0, 0.36, 0.5], [-1.05, 0.58, 0], color=c, roll=-6)]
    air += [B([2.1, 0.22, 2.9], [0.55, -0.38, 1.9], color=c), B([2.1, 0.22, 2.9], [0.55, -0.38, -1.9], color=c)]
    for s in (-1, 1):
        air += [B([1.65, 0.16, 2.7], [0.45, -0.38 + 2.0 * 0.0875, s * 4.6], color=c, pitch=s * -5),
                B([1.0, 0.12, 0.45], [0.35, -0.38 + 3.4 * 0.0875, s * 6.05], color=c, pitch=s * -5),
                B([0.5, 0.02, 2.3], [-0.2, -0.24 + 2.0 * 0.0875, s * 4.6], color=dk, pitch=s * -5),
                B([0.4, 0.06, 0.06], [1.6, -0.36, s * 2.4], color="#1e1e1c"), B([0.4, 0.06, 0.06], [1.6, -0.36, s * 2.7], color="#1e1e1c")]
        air += star(0.9, 0.5, -0.27 + 2.1 * 0.0875 + 0.02, s * 4.4, color="white")[:2]
        air += star(0.42, 0.5, -0.27 + 2.1 * 0.0875 + 0.03, s * 4.4, color="#1f3a6a")[:2]
        air += star(0.62, -3.0, 0.0, s * 0.44, color="white", axis="z")
    air += [B([1.3, 1.25, 0.1], [-5.75, 0.75, 0], color=c, roll=8), B([0.4, 1.0, 0.11], [-6.3, 0.6, 0], color=dk, roll=8),
            B([1.1, 0.1, 3.4], [-5.7, 0.08, 0], color=c), B([0.2, 0.08, 0.1], [1.5, 0.35, 0.62], color=RUST), B([0.2, 0.08, 0.1], [1.5, 0.35, -0.62], color=RUST),
            B([0.04, 0.6, 0.04], [-1.6, 0.8, 0], color="#1e1e1c", roll=-15)]
    p = xf(air, R=mat(roll=a), t=(0, h, 0))

    def world(x, y):
        return (x * ca - y * sa, h + x * sa + y * ca)
    for s in (-1, 1):
        ax, ay = world(1.1, -0.45)
        p.append(tube([ax, ay, s * 1.7], [ax + 0.1, 0.36, s * 1.85], 0.1, color=dk))
        p += wheel(0.7, 0.22, ax + 0.1, s * 1.95, tyre="rubber", hub=lt)
        p.append(B([0.5, 0.75, 0.03], [ax + 0.05, 0.85, s * 1.65], color=c, roll=5))
    tx, ty = world(-5.9, -0.2)
    p += [tube([tx, ty, 0], [tx - 0.1, 0.14, 0], 0.06, color=dk)] + disc_z(0.28, 0.1, tx - 0.1, 0.14, 0, color="rubber")
    prop("Fighter Plane (WWII)" + pal["id"], VEH, "A parked single-engine WWII-style fighter, tail down, 9.7 m long, 12.6 m span. Nose to +X; vault onto the wing roots.",
         ground(p), collider=[C([4.8, 1.25, 1.3], [1.0, 1.85, 0]), C([4.4, 1.1, 0.9], [-3.5, 1.1, 0]), C([2.1, 0.32, 12.4], [0.6, 1.27, 0]),
                              C([1.1, 0.3, 3.4], [-5.6, 0.95, 0])], scatter=BIG)


def bomber(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    y = 2.45
    p = HCyl(1.6, 1.6, y - 0.05, 5.9, 0, color=CANOPY) + HCyl(1.0, 0.4, y - 0.05, 6.85, 0, color=CANOPY)[:2] \
        + HCyl(1.9, 2.2, y, 4.2, 0, color=c) + HCyl(1.95, 6.0, y, 0.2, 0, color=c) + HCyl(1.5, 2.6, y + 0.1, -4.0, 0, color=c) \
        + HCyl(0.9, 2.2, y + 0.25, -6.2, 0, color=c)
    p += [B([1.2, 0.45, 1.1], [4.25, y + 0.95, 0], color=CANOPY, roll=-10), B([0.06, 0.5, 1.12], [4.4, y + 0.95, 0], color=dk)]
    p += Cyl(0.9, 0.42, y + 0.92, -1.0, 0, color=CANOPY)[:2] + [B([0.8, 0.05, 0.05], [-0.4, y + 1.15, 0.12], color="#1e1e1c"),
                                                                 B([0.8, 0.05, 0.05], [-0.4, y + 1.15, -0.12], color="#1e1e1c")]
    p += [B([3.0, 0.36, 5.2], [0.6, y + 0.25, 0], color=c)]
    for s in (-1, 1):
        p += [B([2.4, 0.26, 6.9], [0.4, y + 0.25 + 0.18, s * 6.0], color=c, pitch=s * -3), B([1.3, 0.16, 0.6], [0.2, y + 0.25 + 0.36, s * 9.6], color=c, pitch=s * -3),
              B([0.6, 0.02, 4.6], [-0.6, y + 0.48, s * 6.0], color=dk, pitch=s * -3)]
        z = s * 3.2
        p += HCyl(1.2, 3.6, y - 0.05, 1.5, z, color=c) + HCyl(1.25, 0.2, y - 0.05, 3.2, z, color=dk)[:2] + [B([0.4, 0.3, 0.3], [3.45, y - 0.05, z], color=lt, pitch=45)]
        for k in range(3):
            th = math.radians(90 + 120 * k)
            p.append(B([0.05, 1.5, 0.22], [3.38, y - 0.05 + 0.76 * math.sin(th), z + 0.76 * math.cos(th)], color="#1e1e1c", pitch=-math.degrees(th) + 90))
        p += [tube([1.6, y - 0.6, z], [1.7, 0.55, z], 0.16, color=dk)] + wheel(1.1, 0.34, 1.7, z, tyre="rubber", hub=lt) \
            + [B([1.6, 0.9, 0.04], [1.6, 1.5, z + 0.3], color=c), B([1.6, 0.9, 0.04], [1.6, 1.5, z - 0.3], color=c)]
        p += star(1.3, 0.3, y + 0.62 + 0.06, s * 7.2, color="white")[:2] + star(0.6, 0.3, y + 0.62 + 0.07, s * 7.2, color="#1f3a6a")[:2]
        p += star(0.9, -3.2, y + 0.1, s * 0.76, color="white", axis="z")
        p += [B([1.7, 1.9, 0.12], [-6.5, y + 1.25, s * 3.0], color=c, roll=8), B([0.5, 1.5, 0.13], [-7.15, y + 1.15, s * 3.0], color=dk, roll=8)]
    p += [B([1.7, 0.14, 6.2], [-6.5, y + 0.35, 0], color=c), tube([5.5, y - 0.8, 0], [5.5, 0.35, 0], 0.12, color=dk)]
    p += wheel(0.7, 0.24, 5.5, 0, tyre="rubber", hub=lt)
    p += [B([3.0, 0.02, 0.9], [0.4, y - 0.97, 0], color=dk), B([0.04, 0.02, 0.9], [0.4, y - 0.98, 0], color="#1e1e1c")]
    prop("Twin-Engine Bomber" + pal["id"], VEH, "A parked twin-engine bomber: glazed nose, a top turret, twin tail fins, 14.5 m long, 19.8 m span. Nose to +X.",
         ground(p), collider=[C([13.6, 1.95, 1.95], [-0.1, 2.45, 0]), C([3.0, 0.45, 19.6], [0.5, 2.75, 0]),
                              C([3.6, 1.2, 1.2], [1.5, 2.4, 3.2]), C([3.6, 1.2, 1.2], [1.5, 2.4, -3.2]),
                              C([1.7, 1.9, 6.3], [-6.5, 3.45, 0])], scatter=BIG)


def jet(pal=GREY):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    y = 1.75
    p = HCyl(0.5, 0.9, y, 7.1, 0, color=dk)[:2] + HCyl(0.85, 1.4, y, 6.0, 0, color=c) + HCyl(1.25, 2.2, y, 4.2, 0, color=c)
    p += [B([1.8, 0.55, 0.85], [4.0, y + 0.72, 0], color=CANOPY), B([0.06, 0.58, 0.9], [3.6, y + 0.72, 0], color=dk),
          B([1.0, 0.5, 0.8], [2.8, y + 0.6, 0], color=c, roll=-12)]
    p += [B([8.6, 1.3, 2.0], [-1.4, y, 0], color=c, mat="metal"), B([8.0, 0.25, 1.6], [-1.6, y + 0.72, 0], color=lt)]
    for s in (-1, 1):
        p += [B([3.0, 1.0, 0.7], [2.0, y - 0.1, s * 1.25], color=c), B([0.08, 0.9, 0.6], [3.52, y - 0.1, s * 1.25], color="#141414")]
        p += [B([4.6, 0.2, 1.8], [-2.2, y - 0.15, s * 1.85], color=c), B([3.4, 0.16, 1.5], [-2.9, y - 0.15, s * 3.45], color=c),
              B([2.2, 0.12, 1.2], [-3.6, y - 0.15, s * 4.7], color=c), B([4.3, 0.12, 0.8], [-1.6, y - 0.15, s * 3.4], color=c, yaw=s * 38)]
        p += [B([1.8, 0.1, 1.5], [-6.4, y + 0.05, s * 1.7], color=c, yaw=s * 18), B([0.08, 0.06, 0.06], [0, 0, 0], color=c)][:1]
        p += [tube([-2.3, y - 0.4, s * 3.6], [-0.3, y - 0.4, s * 3.6], 0.16, color="#d8d8d0"), B([0.3, 0.3, 0.02], [-2.2, y - 0.4, s * 3.6], color=dk),
              B([0.3, 0.02, 0.3], [-2.2, y - 0.4, s * 3.6], color=dk), tube([-3.8, y - 0.25, s * 5.25], [-2.3, y - 0.25, s * 5.25], 0.12, color="#d8d8d0")]
        p += star(0.8, -3.0, y - 0.04, s * 3.4, color=dk)
        p += [tube([-1.6, y - 0.6, s * 1.4], [-1.5, 0.38, s * 1.55], 0.1, color=dk)] + wheel(0.76, 0.26, -1.5, s * 1.65, tyre="rubber", hub=lt)
    p += [B([2.6, 2.4, 0.14], [-5.9, y + 1.75, 0], color=c, roll=32), B([0.7, 1.6, 0.15], [-6.9, y + 1.6, 0], color=dk, roll=32)]
    p += HCyl(1.05, 1.0, y, -6.2, 0, color="#3a3a3a") + HCyl(0.85, 0.1, y, -6.72, 0, color="#161616")[:2]
    p += [tube([5.0, y - 0.6, 0], [5.0, 0.36, 0], 0.09, color=dk)] + wheel(0.62, 0.2, 5.0, 0, tyre="rubber", hub=lt)
    p += [tube([0.2, y - 0.9, 0], [-2.4, y - 0.9, 0], 0.5, color=lt), tube([7.45, y, 0], [8.4, y, 0], 0.04, color="#1e1e1c")]
    prop("Jet Fighter" + pal["id"].replace(" Grey", ""), VEH, "A parked single-seat jet fighter: swept wings, side intakes, one tail fin, missiles under the wings. 15.9 m long. Nose to +X.",
         ground(p), collider=[C([14.4, 1.4, 2.0], [-0.1, 1.75, 0]), C([2.8, 1.0, 3.2], [2.0, 1.65, 0]), C([5.0, 0.28, 10.4], [-2.6, 1.52, 0]),
                              C([2.6, 2.4, 0.3], [-6.0, 3.5, 0])], scatter=BIG)


def transport(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    y = 2.15
    p = ring_x(3.4, 0.1, 11.2, 0.4, y, 0, c)                                       # hollow cargo hold, x -5.2..6.0
    p += [At([0.08, 3.3, 3.2], y - 1.65, 6.0, 0, color=dk), At([10.6, 0.04, 2.4], 0.5, 0.5, 0, color="#3e3e36", mat="grid", tex=2)]
    for k in range(4):
        p.append(At([0.08, 3.2, 0.06], 0.5, -4.0 + k * 2.6, 1.55, color=dk))
        p.append(At([0.08, 3.2, 0.06], 0.5, -4.0 + k * 2.6, -1.55, color=dk))
    p += crate_parts(0.9, 0.42, 0.45, 3.0, 0.54, 1.0)[:2] + crate_parts(0.9, 0.42, 0.45, 3.0, 0.96, 1.0, band=False)[:1] \
        + [At([1.2, 0.14, 1.0], 0.54, 1.0, -0.9, color="#6a6448")]
    p += HCyl(3.2, 2.6, y + 0.05, 7.3, 0, color=c) + HCyl(2.0, 1.2, y - 0.2, 9.05, 0, color=c)
    p += [B([1.0, 0.5, 2.2], [8.1, y + 1.1, 0], color=GLASS, roll=-25), B([1.1, 0.4, 0.04], [8.0, y + 0.8, 1.53], color=GLASS),
          B([1.1, 0.4, 0.04], [8.0, y + 0.8, -1.53], color=GLASS)]
    p += [B([5.4, 1.5, 2.8], [-7.6, y + 2.25, 0], color=c, roll=11), B([2.8, 0.12, 2.6], [-6.5, 0.85, 0], color=dk, roll=-22, mat="grid"),
          B([3.8, 3.4, 0.25], [-9.0, y + 4.3, 0], color=c, roll=22), B([1.0, 2.6, 0.26], [-10.3, y + 4.4, 0], color=dk, roll=22),
          B([2.4, 0.22, 8.4], [-10.0, y + 6.0, 0], color=c)]
    p += [B([4.0, 0.55, 4.6], [1.6, y + 1.95, 0], color=c)]
    for s in (-1, 1):
        p += [B([3.4, 0.45, 9.6], [1.4, y + 1.95, s * 7.1], color=c), B([0.8, 0.03, 7.6], [-0.1, y + 2.18, s * 7.4], color=dk)]
        for z in (s * 4.2, s * 7.8):
            p += HCyl(1.1, 3.6, y + 1.5, 2.0, z, color=c) + [B([0.5, 0.36, 0.36], [4.0, y + 1.5, z], color=lt, pitch=45)]
            for k in range(4):
                th = math.radians(45 + 90 * k)
                p.append(B([0.05, 1.7, 0.26], [3.9, y + 1.5 + 0.86 * math.sin(th), z + 0.86 * math.cos(th)], color="#1e1e1c", pitch=-math.degrees(th) + 90))
        p += [At([4.4, 1.25, 0.7], 0.55, 0.6, s * 1.95, color=c)]
        p += HCyl(1.0, 0.36, 0.5, -0.2, s * 2.0, color="rubber", alongZ=True)[:2] + HCyl(1.0, 0.36, 0.5, 1.4, s * 2.0, color="rubber", alongZ=True)[:2]
        p += star(1.2, -2.0, y + 0.2, s * 1.76, color="white", axis="z")[:2] + star(1.6, 1.6, y + 2.2, s * 9.0, color="white")[:2]
    p += [tube([8.0, y - 1.2, 0], [8.0, 0.42, 0], 0.14, color=dk)] + wheel(0.84, 0.3, 8.0, 0, tyre="rubber", hub=lt)
    prop("Transport Plane" + pal["id"], VEH, "A parked four-engine high-wing transport, 20 m long, 23.6 m span, cargo ramp down at the back: walk into the hold. Nose to +X.",
         ground(p), collider=[C([11.2, 0.12, 3.0], [0.4, 0.49, 0]), C([11.2, 3.5, 0.7], [0.4, 2.15, 1.9]), C([11.2, 3.5, 0.7], [0.4, 2.15, -1.9]),
                              C([11.2, 0.25, 3.4], [0.4, 3.95, 0]), C([3.8, 3.2, 3.2], [7.75, 2.15, 0]), C([4.0, 0.55, 23.6], [1.5, 4.35, 0]),
                              C([4.6, 2.4, 2.8], [-7.6, 4.5, 0]), C([3.4, 2.8, 8.4], [-9.6, 7.3, 0])], scatter=BIG)


# ================================================================== Cars and trucks ==================================================================

def jeep(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    p = [At([2.7, 0.5, 1.3], 0.42, -0.2, 0, color=c, mat="metal"), At([1.05, 0.28, 1.12], 0.72, 1.2, 0, color=c),
         At([0.06, 0.58, 1.1], 0.42, 1.73, 0, color=dk), At([0.12, 0.12, 1.5], 0.3, 1.8, 0, color=dk)]
    for k in range(5):
        p.append(At([0.02, 0.4, 0.05], 0.5, 1.765, -0.3 + k * 0.15, color="#1e1e1c"))
    p += [At([0.04, 0.12, 0.12], 0.88, 1.765, 0.42, color="#fff2c0"), At([0.04, 0.12, 0.12], 0.88, 1.765, -0.42, color="#fff2c0")]
    for s in (-1, 1):
        p += [At([1.0, 0.04, 0.32], 0.92, 1.15, s * 0.72, color=c), B([0.4, 0.04, 0.32], [0.55, 0.76, s * 0.72], color=c, roll=35),
              At([0.7, 0.04, 0.3], 0.92, -1.15, s * 0.72, color=c), At([0.04, 0.5, 0.04], 0.95, 0.55, s * 0.64, color=dk)]
        for x in (1.05, -1.05):
            p += wheel(0.72, 0.24, x, s * 0.72, tyre="rubber", hub=dk)
    p += [At([0.04, 0.04, 1.32], 1.45, 0.55, 0, color=dk), At([0.03, 0.42, 1.2], 1.0, 0.56, 0, color=GLASS)]
    for z in (0.33, -0.33):
        p += [At([0.42, 0.12, 0.45], 0.8, -0.05, z, color="#3a3a2c"), At([0.1, 0.42, 0.45], 0.92, -0.3, z, color="#3a3a2c")]
    p += [At([0.45, 0.12, 1.1], 0.8, -1.15, 0, color="#3a3a2c"), At([0.1, 0.4, 1.1], 0.92, -1.4, 0, color="#3a3a2c"),
          tube([0.42, 0.98, 0.33], [0.25, 1.2, 0.33], 0.04, color="#1e1e1c")]
    p += xf(disc_z(0.36, 0.03, 0, 0, 0, color="#1e1e1c"), R=mat(roll=-60), t=(0.22, 1.22, 0.33))
    p += HCyl(0.72, 0.24, 0.9, -1.72, 0, color="rubber") + [At([0.18, 0.4, 0.13], 0.5, -1.62, 0.5, color=lt)]
    p += star(0.55, 1.2, 1.006, 0, color="white") + [tube([-1.5, 0.92, -0.55], [-1.6, 2.6, -0.55], 0.012, color="#1e1e1c")]
    prop("Light Utility Jeep" + pal["id"], VEH, "A small open-topped military utility car: flat fenders, a slatted grille, a spare wheel on the back. Front to +X.",
         ground(p), collider=[C([3.6, 1.1, 1.66], [0, 0.55, 0])], scatter=BIG)


def cab_parts(pal, x0, W=2.3):
    """A flat-fronted truck cab and bonnet with its back at x0, front wheels at x0 + 1.75. Returns parts."""
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    p = [At([1.6, 1.25, W], 1.05, x0 + 0.8, 0, color=c, mat="metal"), At([1.1, 0.7, 1.6], 1.05, x0 + 2.1, 0, color=c),
         At([0.06, 0.7, 1.5], 1.05, x0 + 2.66, 0, color=dk), At([0.15, 0.2, W + 0.1], 0.75, x0 + 2.72, 0, color=dk),
         B([0.05, 0.55, W - 0.25], [x0 + 1.62, 2.0, 0], color=GLASS, roll=-6), At([0.8, 0.45, 0.03], 1.7, x0 + 0.95, W / 2 + 0.005, color=GLASS),
         At([0.8, 0.45, 0.03], 1.7, x0 + 0.95, -W / 2 - 0.005, color=GLASS), At([1.5, 0.06, W + 0.04], 2.3, x0 + 0.8, 0, color=dk)]
    for k in range(5):
        p.append(At([0.02, 0.55, 0.06], 1.12, x0 + 2.7, -0.5 + k * 0.25, color="#1e1e1c"))
    for s in (-1, 1):
        p += [At([0.05, 0.16, 0.18], 1.4, x0 + 2.7, s * 0.95, color="#fff2c0"), At([0.9, 0.06, 0.38], 1.35, x0 + 1.9, s * (W / 2 - 0.1), color=c),
              B([0.45, 0.06, 0.38], [x0 + 2.45, 1.2, s * (W / 2 - 0.1)], color=c, roll=-40), At([0.04, 0.22, 0.14], 2.0, x0 + 1.55, s * (W / 2 + 0.16), color="#1e1e1c")]
    p += star(0.55, x0 + 0.9, 1.7, W / 2 + 0.01, axis="z")[:2] + star(0.55, x0 + 0.9, 1.7, -W / 2 - 0.01, axis="z")[:2]
    return p


def wheels_truck(xs, W, d=1.05, w=0.38, hub=WHEEL):
    p = []
    for x in xs:
        for s in (-1, 1):
            p += wheel(d, w, x, s * (W / 2 - w / 2), tyre="rubber", hub=hub)
    return p


def cargo_truck(pal=OLIVE):
    c, dk, lt, cv = pal["body"], pal["dark"], pal["light"], pal["canvas"]
    W = 2.4
    p = [At([6.4, 0.25, 0.9], 0.65, -0.1, 0, color="#2a2c26")] + cab_parts(pal, 0.75, W)
    p += [At([4.3, 0.12, W], 1.2, -1.45, 0, color=dk, mat="wood"), At([4.3, 0.45, 0.06], 1.32, -1.45, W / 2 - 0.03, color=c),
          At([4.3, 0.45, 0.06], 1.32, -1.45, -W / 2 + 0.03, color=c), At([0.06, 0.45, W], 1.32, -3.6, 0, color=c)]
    p += [At([4.3, 1.2, W + 0.04], 1.75, -1.45, 0, color=cv), At([4.3, 0.28, 2.0], 2.95, -1.45, 0, color=cv),
          B([4.3, 0.3, 0.3], [-1.45, 2.95, 1.0], color=cv, pitch=45), B([4.3, 0.3, 0.3], [-1.45, 2.95, -1.0], color=cv, pitch=45)]
    for k in range(4):
        p.append(At([0.06, 1.42, W + 0.06], 1.75, -3.2 + k * 1.17, 0, color=pal["dark"] if pal is not WINTER else "#a8aaa2"))
    p += [At([0.02, 1.0, 1.8], 1.8, -3.61, 0, color="#1e1e18"), HCyl(0.25, 2.2, 2.85, -3.65, 0, color=cv, alongZ=True)[0],
          At([0.4, 0.4, 0.5], 0.75, 0.0, 1.05, color=dk), At([0.12, 0.5, 0.02], 0.3, -3.5, 1.0, color="#1e1e1c"),
          At([0.12, 0.5, 0.02], 0.3, -3.5, -1.0, color="#1e1e1c"), tube([0.75, 1.6, -1.05], [0.75, 2.9, -1.05], 0.09, color="#2a2a26")]
    p += wheels_truck([2.5, -1.2, -2.5], W)
    prop("Covered Cargo Truck" + pal["id"], VEH, "A six-wheeled army cargo truck with a canvas tilt over the bed (3.2 m tall). Front to +X.",
         ground(p), collider=[C([2.9, 2.35, 2.4], [2.15, 1.175, 0]), C([4.5, 3.1, 2.46], [-1.45, 1.55, 0])], scatter=BIG)


def armoured_car(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    p = [At([4.6, 0.7, 2.0], 0.5, color=c, mat="metal"), At([3.6, 0.5, 1.7], 1.2, -0.2, color=c),
         B([0.9, 0.08, 1.9], [1.95, 1.3, 0], color=c, roll=-30), B([0.6, 0.08, 1.9], [-2.25, 1.3, 0], color=c, roll=35),
         B([3.6, 0.6, 0.08], [-0.2, 1.4, 0.92], color=c, pitch=-20), B([3.6, 0.6, 0.08], [-0.2, 1.4, -0.92], color=c, pitch=20),
         At([0.5, 0.06, 0.06], 1.36, 1.6, 0.0, color="#141414"), At([0.03, 0.05, 0.4], 1.0, 2.3, 0, color="#141414")]
    for s in (-1, 1):
        for x in (1.5, -1.5):
            p += wheel(1.1, 0.36, x, s * 1.05, tyre="rubber", hub=dk)
            p.append(At([1.3, 0.06, 0.45], 1.12, x, s * 1.05, color=c))
        p += [At([0.14, 0.14, 0.1], 1.1, 2.32, s * 0.7, color="#fff2c0"), At([0.4, 0.04, 0.02], 1.4, 0.4, s * 0.98, color="#141414")]
    tx, ty = -0.2, 1.7
    p += Cyl(1.3, 0.5, ty, tx, 0, color=c, mat="metal") + [At([1.1, 0.05, 1.1], ty + 0.5, tx, 0, color=lt, yaw=22.5),
                                                           At([0.25, 0.3, 0.42], ty + 0.08, tx + 0.7, 0, color=dk)]
    p += HCyl(0.08, 1.2, ty + 0.25, tx + 1.35, 0, color=dk) + [At([0.4, 0.05, 0.4], ty + 0.52, tx - 0.15, 0.1, color=dk),
                                                                tube([tx - 0.4, ty + 0.52, -0.4], [tx - 0.55, ty + 2.4, -0.4], 0.012, color="#1e1e1c")]
    p += HCyl(1.0, 0.3, 1.0, -2.45, 0, color="rubber")[:2] + star(0.5, 0.2, 0.85, 1.006, axis="z") + star(0.5, 0.2, 0.85, -1.006, axis="z")
    p += [At([0.6, 0.12, 0.12], 1.25, -0.8, 1.0, color=lt), At([0.6, 0.2, 0.25], 1.25, -1.5, -0.95, color=pal["canvas"])]
    prop("Armoured Car" + pal["id"], VEH, "A four-wheeled armoured scout car with sloped plates and a small turret. Front to +X; vault onto the hull.",
         ground(p), collider=[C([4.8, 1.7, 2.46], [0, 0.85, 0]), C([1.3, 0.55, 1.3], [-0.2, 1.97, 0])], scatter=BIG)


def tanker(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    W = 2.4
    p = [At([6.8, 0.25, 0.9], 0.65, -0.3, 0, color="#2a2c26")] + cab_parts(pal, 0.95, W)
    p += [At([0.6, 1.2, 2.0], 0.9, 0.5, 0, color=dk), At([0.5, 0.06, 0.6], 2.1, 0.5, 0, color=STEEL)]
    p += HCyl(2.0, 4.6, 2.05, -1.95, 0, color=c, mat="metal") + HCyl(1.9, 0.1, 2.05, -4.3, 0, color=dk)[:2] + HCyl(1.9, 0.1, 2.05, 0.4, 0, color=dk)[:2]
    for x in (-3.4, -1.95, -0.5):
        p += HCyl(2.05, 0.08, 2.05, x, 0, color=dk)[:2]
        p += Cyl(0.5, 0.1, 3.02, x, 0, color=STEEL)[:2]
    p += [At([4.4, 0.04, 0.5], 3.05, -1.95, 0, color="#3a3c36", mat="grid", tex=3)]
    for x in (-4.0, -1.95, 0.1):
        p += [At([0.04, 0.5, 0.04], 3.09, x, 0.3, color=STEEL)]
    p += [At([4.1, 0.04, 0.04], 3.57, -1.95, 0.3, color=STEEL), At([0.06, 1.5, 0.4], 1.0, -4.45, 0.5, color=STEEL),
          At([0.4, 0.18, 2.0], 0.95, -4.35, 0, color=dk), At([2.0, 0.12, 0.01], 1.7, -1.95, 1.0, color=STENCIL),
          At([2.0, 0.12, 0.01], 1.7, -1.95, -1.0, color=STENCIL), B([0.02, 0.36, 0.36], [-4.36, 2.1, -0.4], color="#c42a1c", pitch=45)]
    p += HCyl(0.6, 0.5, 0.7, -0.4, 0, color="#1e1e1c", alongZ=True)[:2]
    p += wheels_truck([2.7, -1.6, -2.9], W)
    prop("Fuel Tanker Truck" + pal["id"], VEH, "A six-wheeled army fuel tanker: round tank with a top walkway and hatches. Solid cover; it doesn't blow up. Front to +X.",
         ground(p), collider=[C([2.9, 2.35, 2.4], [2.35, 1.175, 0]), C([5.4, 3.1, 2.4], [-1.95, 1.55, 0])], scatter=BIG)


def sidecar(pal=OLIVE):
    c, dk, lt = pal["body"], pal["dark"], pal["light"]
    z = -0.32
    p = []
    for x in (0.7, -0.7):
        p += HCyl(0.64, 0.12, 0.32, x, z, color="rubber", alongZ=True) + disc_z(0.3, 0.14, x, 0.32, z, color=dk)
        p.append(B([0.6, 0.04, 0.16], [x, 0.66, z], color=c, roll=-10 if x > 0 else 10))
    p += [tube([0.7, 0.32, z], [0.45, 0.95, z], 0.05, color=dk), tube([0.45, 0.95, z], [-0.35, 0.72, z], 0.06, color=dk),
          tube([-0.7, 0.32, z], [-0.25, 0.5, z], 0.05, color=dk), At([0.45, 0.3, 0.26], 0.32, 0.0, z, color="#3a3a34"),
          At([0.5, 0.18, 0.28], 0.8, 0.15, z, color=c), At([0.4, 0.08, 0.24], 0.88, -0.25, z, color="#2a2a22"),
          At([0.05, 0.05, 0.62], 1.02, 0.47, z, color="#1e1e1c"), At([0.1, 0.14, 0.14], 0.82, 0.62, z, color="#fff2c0"),
          tube([-0.05, 0.3, z - 0.15], [-0.75, 0.38, z - 0.18], 0.06, color=STEEL)]
    sz = 0.42
    p += [At([1.25, 0.45, 0.6], 0.33, -0.15, sz, color=c, mat="metal"), B([0.4, 0.35, 0.56], [0.6, 0.5, sz], color=c, roll=-35),
          At([0.6, 0.06, 0.46], 0.78, -0.3, sz, color="#1e1e18"), At([0.04, 0.22, 0.5], 0.78, 0.2, sz, color=GLASS),
          At([0.12, 0.3, 0.5], 0.78, -0.6, sz, color="#3a3a2c")]
    p += HCyl(0.5, 0.1, 0.25, -0.15, 0.78, color="rubber", alongZ=True) + [tube([-0.15, 0.25, 0.7], [-0.15, 0.4, 0.1], 0.04, color=dk)]
    p += [tube([0.3, 0.8, sz], [0.45, 1.0, sz], 0.03, color="#1e1e1c"), B([0.5, 0.06, 0.06], [0.6, 1.0, sz], color="#1e1e1c")]
    p += star(0.3, -0.1, 0.6, sz + 0.305, axis="z")
    prop("Sidecar Motorcycle" + pal["id"], VEH, "A military motorcycle with a sidecar on its right (+Z) and a light gun on the sidecar. Front to +X.",
         ground(p), collider=[C([1.8, 1.05, 1.5], [-0.05, 0.525, 0.05])], scatter=BIG)


ALL = [sandbags, wire, hedgehog, tank_trap, stake_barrier,
       field_cannon, at_gun, howitzer, mortar_pit, machine_gun,
       ammo, pallets, camo_net, field_radio, tents, fuel, searchlight, guard_tower,
       medium_tank, lambda: medium_tank(DESERT), heavy_tank, mbt, lambda: mbt(DESERT), lambda: mbt(WINTER), light_tank, wrecked_tank,
       fighter, bomber, jet, transport,
       jeep, lambda: jeep(DESERT), lambda: jeep(WINTER), cargo_truck, lambda: cargo_truck(DESERT), lambda: cargo_truck(WINTER),
       armoured_car, lambda: armoured_car(DESERT), tanker, sidecar]

FOLDERS = {MIL: "Military", VEH: os.path.join("Military", "Vehicles")}

# Rows in the gallery map: (label colour, names in order).
GROUPS = [
    ("#8a7a4a", ["Sandbag Wall Short", "Sandbag Wall Long", "Sandbag Corner", "Sandbag Nest Ring", "Sandbag Pile", "Barbed Wire Coil",
                 "Barbed Wire Fence Line", "Steel Hedgehog", "Concrete Tank Trap", "Wooden Stake Barrier"]),
    ("#6a6a3a", ["Field Cannon", "Anti-Tank Gun", "Howitzer", "Mortar Pit", "Mounted Machine Gun", "Field Searchlight", "Guard Tower"]),
    ("#4a6a3a", ["Ammo Crate Wooden", "Stacked Ammo Crates", "Ammo Case Metal", "Steel Ammo Can", "Shell Crate", "Rifle Crate", "Artillery Shell Stack",
                 "Spent Shell Casings", "Military Pallet", "Pallet of Ammo Crates", "Fuel Drum Rack", "Military Fuel Drum", "Jerrycan Rack", "Field Radio Pack"]),
    ("#5a5a5a", ["Camo Net Canopy", "Field Tent", "Command Tent"]),
    ("#3a5a6a", ["Medium Tank (WWII)", "Medium Tank (WWII) Desert", "Heavy Tank", "Light Tank", "Wrecked Tank"]),
    ("#3a5a6a", ["Main Battle Tank", "Main Battle Tank Desert", "Main Battle Tank Winter"]),
    ("#4a4a7a", ["Light Utility Jeep", "Light Utility Jeep Desert", "Light Utility Jeep Winter", "Sidecar Motorcycle", "Armoured Car", "Armoured Car Desert"]),
    ("#4a4a7a", ["Covered Cargo Truck", "Covered Cargo Truck Desert", "Covered Cargo Truck Winter", "Fuel Tanker Truck"]),
    ("#6a3a3a", ["Fighter Plane (WWII)", "Jet Fighter"]),
    ("#6a3a3a", ["Twin-Engine Bomber", "Transport Plane"]),
]


def write_prop(p, out):
    folder = os.path.join(out, FOLDERS[p["category"]])
    os.makedirs(folder, exist_ok=True)
    fn = "".join(ch for ch in p["name"] if ch.isalnum()) + ".json"
    lines = ["{"]
    for k in [k for k in p if k != "parts"]:
        lines.append(f'  "{k}": {json.dumps(p[k])},')
    lines.append('  "parts": [')
    for i, part in enumerate(p["parts"]):
        lines.append("    " + json.dumps(part) + ("," if i < len(p["parts"]) - 1 else ""))
    lines.append("  ]")
    lines.append("}")
    with open(os.path.join(folder, fn), "w") as fh:
        fh.write("\n".join(lines) + "\n")


def gallery(props, path):
    """Military Props: every prop on its own pad, rows by group (north to south)."""
    by = {p["name"]: p for p in props}
    shown = [n for _, names in GROUPS for n in names]
    assert sorted(shown) == sorted(by), (set(by) - set(shown), set(shown) - set(by))
    gap, aisle = 2.0, 4.0
    rows = []
    for colr, names in GROUPS:
        items = []
        for n in names:
            lo, hi = go.bounds(by[n])
            items.append((by[n], lo, hi))
        rows.append((colr, items))
    widths = [sum(hi[0] - lo[0] + 1.0 for _, lo, hi in r) + gap * (len(r) - 1) for _, r in rows]
    half_x = math.ceil(max(widths) / 2 + 8)
    depth = [max(hi[2] - lo[2] for _, lo, hi in r) + 1.0 for _, r in rows]
    total = sum(depth) + aisle * (len(rows) - 1)
    half_z = math.ceil(total / 2 + 12)
    ops = []
    z = -total / 2
    for (colr, row), d, w in zip(rows, depth, widths):
        zc = z + d / 2
        x = -w / 2
        ops.append({"op": "Block", "x0": round(x - 2.0, 2), "y0": 0, "z0": round(zc - d / 2, 2), "x1": round(x - 1.0, 2), "y1": 0.06,
                    "z1": round(zc + d / 2, 2), "color": colr})
        for q, lo, hi in row:
            pw = hi[0] - lo[0] + 1.0
            px = x + pw / 2 - (lo[0] + hi[0]) / 2
            pz = zc - (lo[2] + hi[2]) / 2
            ops.append({"op": "Block", "x0": round(x, 2), "y0": 0, "z0": round(zc - d / 2, 2), "x1": round(x + pw, 2), "y1": 0.04,
                        "z1": round(zc + d / 2, 2), "color": [0.46, 0.47, 0.42]})
            ops.append({"op": "Prop", "name": q["name"], "x": round(px, 2), "z": round(pz, 2), "yaw": 0})
            x += pw + gap
        z += d + aisle
    spawns = []
    for k in range(-4, 5):
        spawns += [{"op": "Spawn", "x": k * 10, "z": -(half_z - 5)}, {"op": "Spawn", "x": k * 10, "z": half_z - 5}]
    m = {"format": "call-of-slop-map/1", "name": "Military Props",
         "description": "The military props (Props/Military) on their own pads: fortifications, guns, ammunition, camp, tanks, vehicles and planes, north to south.",
         "author": "Call of Slop", "order": 954, "halfX": half_x, "halfZ": half_z, "sky": [0.6, 0.64, 0.68], "flagPool": "none",
         "ops": [{"op": "Ground", "color": [0.4, 0.42, 0.34]}, {"op": "Perimeter", "height": 4, "color": [0.5, 0.5, 0.46]}] + ops + spawns
                + [{"op": "AutoWaypoints", "spacing": 6}]}
    lines = ["{"]
    for k in [k for k in m if k != "ops"]:
        lines.append(f'  "{k}": {json.dumps(m[k])},')
    lines.append('  "ops": [')
    for i, op in enumerate(m["ops"]):
        lines.append("    " + json.dumps(op) + ("," if i < len(m["ops"]) - 1 else ""))
    lines.append("  ]")
    lines.append("}")
    with open(path, "w") as fh:
        fh.write("\n".join(lines) + "\n")
    return half_x, half_z


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "Props"
    gpath = sys.argv[2] if len(sys.argv) > 2 else os.path.join("Maps", "Dev", "MilitaryProps.json")
    for f in ALL:
        f()
    names = set()
    for p in PROPS:
        assert p["name"] not in names, p["name"]
        assert len(p["name"]) <= 40 and p["name"].isascii(), p["name"]
        assert 1 <= len(p["parts"]) <= 128, (p["name"], len(p["parts"]))
        assert len(p.get("collider", [])) <= 8, p["name"]
        names.add(p["name"])
        lo, hi = go.bounds(p)
        assert lo[1] > -0.03 and all(hi[i] - lo[i] <= 24 for i in range(3)), (p["name"], lo, hi)
        write_prop(p, out)
    hx, hz = gallery(PROPS, gpath)
    print(len(PROPS), "props:", ", ".join(f"{c} {sum(1 for p in PROPS if p['category'] == c)}" for c in FOLDERS), f"| gallery {2 * hx} x {2 * hz} m")
    for p in PROPS:
        lo, hi = go.bounds(p)
        print(f"  {p['name']:<30} {len(p['parts']):>3} parts {len(p['parts']) * 12:>5} tris {len(p.get('collider', [])):>2} boxes  "
              f"{hi[0] - lo[0]:5.1f} x {hi[1] - lo[1]:4.1f} x {hi[2] - lo[2]:5.1f} m")
