"""Python mirror of the gun model generator in Source/Weapons/GunBuild.cs (+ the optic code in GunModel.cs).

Keep this FAITHFUL to the C#: same parts, same order, same numbers. The self-test "Gun models" compares
per-weapon part counts and bounds against what this prints (python3 viz.py --table).

build(w, kit) -> Mesh. w = weapon dict from the JSON file (keys as in the file), kit = dict slot -> attachment name.
"""
import math, json, glob, os

PI = math.pi
f32 = float


def V(r, g, b):
    return (r, g, b)


def mul(c, k):
    return (c[0] * k, c[1] * k, c[2] * k)


def lerp3(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


DARK = V(0.21, 0.21, 0.23)
METAL = V(0.58, 0.6, 0.64)
OPTIC_BODY = V(0.36, 0.38, 0.42)
RED_GLOW = V(1, 0.15, 0.1)
CYAN_GLOW = V(0.35, 0.95, 1)
GREEN_GLOW = V(0.3, 1, 0.35)
RUBBER = V(0.12, 0.12, 0.13)
PAD = V(0.35, 0.12, 0.1)
SHELL_RED = V(0.72, 0.15, 0.12)

COLORS = {
    "black": V(0.17, 0.17, 0.19),
    "gunmetal": V(0.29, 0.3, 0.33),
    "steel": V(0.5, 0.52, 0.55),
    "chrome": V(0.76, 0.78, 0.82),
    "brass": V(0.76, 0.6, 0.3),
    "copper": V(0.72, 0.42, 0.26),
    "polymer": V(0.31, 0.31, 0.33),
    "tan": V(0.62, 0.54, 0.4),
    "olive": V(0.33, 0.36, 0.24),
    "wood": V(0.55, 0.36, 0.2),
    "darkwood": V(0.36, 0.22, 0.12),
    "lightwood": V(0.7, 0.5, 0.3),
    "white": V(0.86, 0.86, 0.84),
    "rust": V(0.47, 0.29, 0.18),
    "orange": V(0.95, 0.5, 0.12),
    "yellow": V(0.92, 0.74, 0.15),
}

# ---- The "model" block ---------------------------------------------------------------------
# kind: e = enum, n = number, i = whole number, b = true/false, c = colour.
# scale (numbers only): L = an archetype default is a fraction of the weapon's length, B = a multiple of its bulk,
# '' = used as is. Values written in a weapon file are always used as is (metres, degrees).
PARAMS = [
    ("receiver", "e", ["box", "slab", "round", "slide", "revolver", "bullpup", "lever", "break", "tool", "toy", "sci"]),
    ("receiverLength", "n", (0.03, 1.0, "L")),
    ("receiverHeight", "n", (0.02, 0.4, "B")),
    ("receiverWidth", "n", (0.015, 0.3, "B")),
    ("barrels", "i", (1, 8)),
    ("barrelLayout", "e", ["single", "side", "over", "rotary"]),
    ("barrelStyle", "e", ["round", "square", "fluted"]),
    ("barrelLength", "n", (0.0, 1.5, "L")),
    ("barrelWidth", "n", (0.005, 0.15, "B")),
    ("shroud", "e", ["none", "jacket", "perforated", "heatshield", "suppressor", "coils", "rails"]),
    ("shroudLength", "n", (0.05, 1.0, "")),
    ("handguard", "e", ["none", "rail", "slim", "ribbed", "wood", "quad", "tube", "forend", "pump", "stock"]),
    ("handguardLength", "n", (0.0, 1.2, "L")),
    ("handguardHeight", "n", (0.01, 0.3, "B")),
    ("stock", "e", ["none", "fixed", "crane", "skeleton", "folding", "wire", "wood", "straight", "thumbhole", "chassis", "bullpup", "toy", "spade"]),
    ("stockLength", "n", (0.02, 0.6, "")),
    ("stockDrop", "n", (-15, 30, "")),
    ("cheekRest", "b", None),
    ("grip", "e", ["pistol", "wood", "straight", "birdshead", "none"]),
    ("gripAngle", "n", (-10, 50, "")),
    ("mag", "e", ["box", "curved", "stick", "drum", "tube", "pan", "topbox", "side", "internal", "beltbox", "helical", "grip", "cell", "strip", "striker", "none"]),
    ("magLength", "n", (0.0, 0.8, "")),
    ("magOffset", "n", (-0.5, 0.5, "")),
    ("magAngle", "n", (-45, 45, "")),
    ("foregrip", "e", ["none", "vertical", "stub", "folding", "strap"]),
    ("bipod", "e", ["none", "folded", "deployed"]),
    ("bolt", "e", ["none", "side", "rear", "charging", "top"]),
    ("sights", "e", ["post", "tall", "bead", "aperture", "ghost", "none"]),
    ("muzzle", "e", ["none", "crown", "flash", "brake", "bell", "comp", "tip", "breacher", "ported"]),
    ("carryHandle", "b", None),
    ("topRail", "b", None),
    ("lever", "b", None),
    ("hammer", "b", None),
    ("gasBlock", "b", None),
    ("canister", "b", None),
    ("sideSaddle", "b", None),
    ("ramrod", "b", None),
    ("coils", "i", (0, 10)),
    ("finish", "c", None),
    ("furniture", "c", None),
    ("magColor", "c", None),
    ("accentStyle", "e", ["none", "stripe", "mag", "panel", "glow", "tips", "bands"]),
]
PARAM = {p[0]: p for p in PARAMS}

BASE = dict(receiver="box", receiverLength=0.48, receiverHeight=0.1, receiverWidth=0.068, barrels=1, barrelLayout="single",
            barrelStyle="round", barrelLength=0.7, barrelWidth=0.03, shroud="none", shroudLength=0.6, handguard="rail",
            handguardLength=0.42, handguardHeight=0.085, stock="fixed", stockLength=0.22, stockDrop=0.0, cheekRest=False,
            grip="pistol", gripAngle=17.0, mag="box", magLength=0.0, magOffset=0.0, magAngle=0.0, foregrip="none", bipod="none",
            bolt="charging", sights="post", muzzle="none", carryHandle=False, topRail=False, lever=False, hammer=False,
            gasBlock=False, canister=False, sideSaddle=False, ramrod=False, coils=0, finish="black", furniture="polymer",
            magColor="accent", accentStyle="mag")

# Archetypes: starting points. Numbers with scale L/B are fractions of the weapon's length / multiples of its bulk.
ARCHETYPES = {
    "pistol": dict(receiver="slide", receiverLength=0.95, receiverHeight=0.075, receiverWidth=0.05, barrelLength=0.06, barrelWidth=0.022,
                   handguard="none", handguardLength=0.0, stock="none", gripAngle=14.0, mag="grip", bolt="none", accentStyle="stripe"),
    "revolver": dict(receiver="revolver", receiverLength=0.45, receiverHeight=0.08, receiverWidth=0.05, barrelLength=0.55, barrelWidth=0.024,
                     handguard="none", handguardLength=0.0, stock="none", grip="wood", gripAngle=20.0, mag="none", bolt="none",
                     hammer=True, finish="steel", furniture="wood", accentStyle="none"),
    "derringer": dict(receiver="break", receiverLength=0.42, receiverHeight=0.07, receiverWidth=0.045, barrels=2, barrelLayout="over",
                      barrelLength=0.62, barrelWidth=0.026, handguard="none", handguardLength=0.0, stock="none", grip="birdshead",
                      gripAngle=28.0, mag="none", bolt="none", hammer=True, sights="bead", finish="chrome", furniture="wood", accentStyle="none"),
    "smg": dict(receiver="box", receiverLength=0.55, receiverHeight=0.09, receiverWidth=0.062, barrelLength=0.55, barrelWidth=0.026,
                handguard="slim", handguardLength=0.22, handguardHeight=0.07, stock="folding", mag="stick", accentStyle="mag"),
    "pdw": dict(receiver="bullpup", receiverLength=0.95, receiverHeight=0.11, receiverWidth=0.07, barrelLength=0.25, barrelWidth=0.024,
                handguard="none", handguardLength=0.0, stock="bullpup", mag="topbox", bolt="charging", sights="post", foregrip="strap"),
    "rifle": dict(),
    "battle_rifle": dict(receiver="slab", receiverLength=0.5, receiverHeight=0.11, receiverWidth=0.066, barrelLength=0.78, handguard="slim",
                         handguardLength=0.4, handguardHeight=0.08, mag="box", muzzle="flash", accentStyle="mag"),
    "bullpup": dict(receiver="bullpup", receiverLength=0.8, receiverHeight=0.12, receiverWidth=0.07, barrelLength=0.42, handguard="slim",
                    handguardLength=0.18, handguardHeight=0.08, stock="bullpup", mag="curved"),
    "dmr": dict(receiver="box", receiverLength=0.42, receiverHeight=0.095, receiverWidth=0.064, barrelLength=0.84, handguard="slim",
                handguardLength=0.42, handguardHeight=0.075, stock="fixed", cheekRest=True, mag="box", finish="black", furniture="olive"),
    "lever_rifle": dict(receiver="lever", receiverLength=0.28, receiverHeight=0.085, receiverWidth=0.05, barrelLength=0.8,
                        barrelStyle="square", handguard="forend", handguardLength=0.3, handguardHeight=0.045, stock="straight",
                        stockLength=0.26, stockDrop=6.0, grip="straight", mag="tube", lever=True, hammer=True, bolt="none", sights="post",
                        finish="steel", furniture="wood", accentStyle="none"),
    "bolt_sniper": dict(receiver="round", receiverLength=0.34, receiverHeight=0.085, receiverWidth=0.06, barrelLength=0.62, barrelWidth=0.024,
                        handguard="stock", handguardLength=0.3, handguardHeight=0.055, stock="wood", stockLength=0.26, cheekRest=True,
                        mag="internal", bolt="side", bipod="folded", sights="none", finish="gunmetal", furniture="wood", accentStyle="none"),
    "pump_shotgun": dict(receiver="box", receiverLength=0.36, receiverHeight=0.11, receiverWidth=0.07, barrelLength=0.55, barrelWidth=0.04,
                         handguard="pump", handguardLength=0.3, handguardHeight=0.05, stock="wood", stockLength=0.26, mag="tube",
                         bolt="none", sights="bead", furniture="wood", accentStyle="none"),
    "semi_shotgun": dict(receiver="box", receiverLength=0.38, receiverHeight=0.105, receiverWidth=0.068, barrelLength=0.55, barrelWidth=0.038,
                         handguard="forend", handguardLength=0.34, handguardHeight=0.05, stock="crane", stockLength=0.24, mag="tube",
                         bolt="charging", sights="ghost", topRail=True, accentStyle="none"),
    "double_barrel": dict(receiver="break", receiverLength=0.2, receiverHeight=0.09, receiverWidth=0.07, barrels=2, barrelLayout="side",
                          barrelLength=0.64, barrelWidth=0.034, handguard="forend", handguardLength=0.28, handguardHeight=0.04,
                          stock="straight", stockLength=0.28, stockDrop=7.0, grip="straight", mag="none", bolt="none", sights="bead",
                          hammer=True, finish="steel", furniture="wood", accentStyle="none"),
    "drum_shotgun": dict(receiver="box", receiverLength=0.42, receiverHeight=0.11, receiverWidth=0.07, barrelLength=0.5, barrelWidth=0.034,
                         shroud="perforated", shroudLength=0.75, handguard="none", handguardLength=0.0, stock="folding", stockLength=0.24,
                         mag="striker", foregrip="vertical", bolt="charging", sights="post", accentStyle="mag"),
    "lever_shotgun": dict(receiver="lever", receiverLength=0.3, receiverHeight=0.1, receiverWidth=0.06, barrelLength=0.6, barrelWidth=0.04,
                          handguard="forend", handguardLength=0.24, handguardHeight=0.045, stock="straight", stockLength=0.26, stockDrop=8.0,
                          grip="straight", mag="tube", lever=True, hammer=True, bolt="none", sights="bead", finish="gunmetal",
                          furniture="wood", accentStyle="none"),
    "lmg": dict(receiver="box", receiverLength=0.46, receiverHeight=0.13, receiverWidth=0.09, barrelLength=0.66, barrelWidth=0.04,
                shroud="heatshield", shroudLength=0.4, handguard="quad", handguardLength=0.36, handguardHeight=0.1, stock="skeleton",
                mag="beltbox", carryHandle=True, bipod="folded", finish="olive", furniture="polymer", accentStyle="mag"),
    "rotary": dict(receiver="box", receiverLength=0.42, receiverHeight=0.15, receiverWidth=0.1, barrels=6, barrelLayout="rotary",
                   barrelLength=0.62, barrelWidth=0.017, handguard="none", handguardLength=0.0, stock="spade", mag="beltbox",
                   carryHandle=True, bolt="none", sights="post", finish="gunmetal", accentStyle="mag"),
    "musket": dict(receiver="lever", receiverLength=0.16, receiverHeight=0.07, receiverWidth=0.05, barrelLength=0.95, barrelWidth=0.024,
                   handguard="stock", handguardLength=0.85, handguardHeight=0.045, stock="straight", stockLength=0.3, stockDrop=9.0,
                   grip="straight", mag="none", bolt="none", hammer=True, ramrod=True, sights="bead", muzzle="crown",
                   finish="gunmetal", furniture="wood", accentStyle="bands"),
    "coil_rifle": dict(receiver="sci", receiverLength=0.4, receiverHeight=0.11, receiverWidth=0.07, barrelLength=0.64, barrelWidth=0.03,
                       shroud="rails", shroudLength=0.95, handguard="none", handguardLength=0.0, stock="chassis", stockLength=0.25,
                       mag="cell", coils=4, sights="none", finish="white", furniture="gunmetal", accentStyle="glow"),
    "energy": dict(receiver="sci", receiverLength=0.5, receiverHeight=0.1, receiverWidth=0.07, barrelLength=0.6, barrelWidth=0.03,
                   shroud="coils", shroudLength=0.8, handguard="none", handguardLength=0.0, stock="fixed", mag="cell", coils=4,
                   accentStyle="glow", finish="gunmetal", furniture="polymer"),
    "tool": dict(receiver="tool", receiverLength=0.55, receiverHeight=0.14, receiverWidth=0.075, barrelLength=0.38, barrelWidth=0.03,
                 handguard="none", handguardLength=0.0, stock="none", mag="strip", magAngle=-20.0, bolt="none", sights="none", muzzle="tip",
                 canister=True, finish="accent", furniture="black", magColor="steel", accentStyle="none"),
    "toy": dict(receiver="toy", receiverLength=0.6, receiverHeight=0.12, receiverWidth=0.075, barrelLength=0.45, barrelWidth=0.035,
                barrelStyle="square", handguard="slim", handguardLength=0.25, handguardHeight=0.08, stock="toy", mag="pan", bolt="top",
                sights="tall", muzzle="tip", finish="accent", furniture="white", magColor="white", accentStyle="panel"),
}

SCALE_L = [p[0] for p in PARAMS if p[1] == "n" and p[2][2] == "L"]
SCALE_B = [p[0] for p in PARAMS if p[1] == "n" and p[2][2] == "B"]


def resolve(w):
    """The weapon's model parameters: base defaults, then the archetype, then the file's own values."""
    m = w.get("model")
    if m is None:
        return None
    L = w.get("length", 0.42)
    Bk = w.get("bulk", 1.0)
    p = dict(BASE)
    p.update(ARCHETYPES[m.get("base", "rifle")])
    for k in SCALE_L:
        p[k] = p[k] * L
    for k in SCALE_B:
        p[k] = p[k] * Bk
    for k, v in m.items():
        if k == "base":
            continue
        assert k in PARAM, f"{w['name']}: unknown model key {k}"
        kind = PARAM[k][1]
        if kind == "e":
            assert v in PARAM[k][2], f"{w['name']}: {k}={v}"
        elif kind in ("n", "i"):
            lo, hi = PARAM[k][2][0], PARAM[k][2][1]
            assert lo <= v <= hi, f"{w['name']}: {k}={v} outside {lo}-{hi}"
        p[k] = v
    return p


def color(spec, accent):
    if isinstance(spec, list):
        return tuple(spec)
    if spec == "accent":
        return tuple(accent)
    return COLORS[spec]


# ---- Matrices (System.Numerics, row vectors) --------------------------------------------------

def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def scale(x, y, z):
    return [[x, 0, 0, 0], [0, y, 0, 0], [0, 0, z, 0], [0, 0, 0, 1]]


def rot_x(r):
    c, s = math.cos(r), math.sin(r)
    return [[1, 0, 0, 0], [0, c, s, 0], [0, -s, c, 0], [0, 0, 0, 1]]


def rot_y(r):
    c, s = math.cos(r), math.sin(r)
    return [[c, 0, -s, 0], [0, 1, 0, 0], [s, 0, c, 0], [0, 0, 0, 1]]


def rot_z(r):
    c, s = math.cos(r), math.sin(r)
    return [[c, s, 0, 0], [-s, c, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]


def trans(x, y, z):
    return [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [x, y, z, 1]]


def xform(p, m):
    x, y, z = p
    return (x * m[0][0] + y * m[1][0] + z * m[2][0] + m[3][0],
            x * m[0][1] + y * m[1][1] + z * m[2][1] + m[3][1],
            x * m[0][2] + y * m[1][2] + z * m[2][2] + m[3][2])


# ---- Parts ------------------------------------------------------------------------------------

# Part flags (same bits as GunModel.PartFlags in C#)
F_OPTIC = 1      # part of the optic or its rail
F_SHELL = 2      # optic housing: fades out with the ADS cut-away
F_BLEND = 4      # drawn with alpha blending
F_HOOD = 8       # holo hood: its length shrinks with the cut-away
F_TOP = 16       # removable top part (iron sights, carry handle): gone with any scope
F_NOWRITE = 32   # (unused in python)


class Part:
    __slots__ = ("sx", "sy", "sz", "x", "y", "z", "c", "rx", "rz", "ry", "mode", "alpha", "flags", "node")

    def __init__(self, sx, sy, sz, x, y, z, c, rx, rz, ry, mode, alpha, flags, node):
        self.sx, self.sy, self.sz, self.x, self.y, self.z = sx, sy, sz, x, y, z
        self.c, self.rx, self.rz, self.ry, self.mode, self.alpha, self.flags, self.node = c, rx, rz, ry, mode, alpha, flags, node

    def matrix(self):
        m = scale(self.sx, self.sy, self.sz)
        if self.rz != 0:
            m = mat_mul(m, rot_z(self.rz))
        if self.rx != 0:
            m = mat_mul(m, rot_x(self.rx))
        if self.ry != 0:
            m = mat_mul(m, rot_y(self.ry))
        return mat_mul(m, trans(self.x, self.y, self.z))

    def corners(self):
        m = self.matrix()
        return [xform(((i & 1) - 0.5, ((i >> 1) & 1) - 0.5, ((i >> 2) & 1) - 0.5), m) for i in range(8)]

    def bounds(self):
        cs = self.corners()
        return (tuple(min(c[i] for c in cs) for i in range(3)), tuple(max(c[i] for c in cs) for i in range(3)))


NODES = ["root", "receiver", "barrel", "muzzle", "handguard", "underbarrel", "optic", "magazine", "stock", "grip"]


class Mesh:
    def __init__(self):
        self.parts = []
        self.mount = 0.0      # top of the receiver where an optic's rail sits
        self.iron = 0.0       # iron sight line height
        self.optic_z = 0.0
        self.rail = (0, 0, 0)  # (width, front z, back z)
        self.bore = 0.0
        self.muzzle_z = 0.0
        self.points = {}      # attach points: name -> (x, y, z)


# ---- The builder ------------------------------------------------------------------------------

class B:
    """Builds one gun. Mirrors GunBuild in C#."""

    def __init__(self, w, kit, p):
        self.w, self.kit, self.p = w, kit, p
        self.mesh = Mesh()
        self.node = "receiver"
        self.flags = 0

    def box(self, sx, sy, sz, x, y, z, c, rx=0.0, rz=0.0, ry=0.0, mode=0, alpha=1.0, flags=0):
        self.mesh.parts.append(Part(sx, sy, sz, x, y, z, c, rx, rz, ry, mode, alpha, flags | self.flags, self.node))

    def rod(self, wd, length, x, y, z, c, flags=0):
        """A round-ish rod along Z: two boxes, one turned 45 degrees (reads as an octagon)."""
        self.box(wd, wd, length, x, y, z, c, flags=flags)
        self.box(wd, wd, length, x, y, z, mul(c, 0.93), rz=PI / 4, flags=flags)

    def att(self, slot):
        return self.kit.get(slot)

    def build(self):
        w, p = self.w, self.p
        L = w.get("length", 0.42)
        acc = tuple(w.get("accent", [0.95, 0.5, 0.15]))
        cal = w.get("caliber", 0.5)
        M = color(p["finish"], acc)
        F = color(p["furniture"], acc)
        D = mul(M, 0.55) if sum(M) > 1.2 else mul(DARK, 0.8)  # detail colour: darker than the finish
        A = acc
        magc = color(p["magColor"], acc)
        if p["magColor"] == "accent":
            magc = mul(acc, 0.85)
        if p["accentStyle"] != "mag" and p["magColor"] == "accent":
            magc = mul(M, 0.8) if sum(M) < 2.0 else mul(DARK, 1.1)
        self.M, self.F, self.D, self.A, self.magc = M, F, D, A, magc
        style = p["receiver"]
        RL, RH, RW = p["receiverLength"], p["receiverHeight"], p["receiverWidth"]
        bullpup = style == "bullpup"
        if bullpup:
            back = RL * 0.68
            front = back - RL
        else:
            back = RL * 0.38
            front = -RL * 0.62
        mid = (front + back) / 2
        pistol = style in ("slide", "revolver")
        bore = RH * (0.15 if pistol else 0.12)
        self.RL, self.RH, self.RW, self.front, self.back, self.mid, self.bore = RL, RH, RW, front, back, mid, bore
        if bullpup:
            grip_z = front + RL * 0.24
        elif pistol:
            grip_z = back - 0.025
        else:
            grip_z = back - RL * 0.2
        self.grip_z = grip_z
        mesh = self.mesh

        # ---- Barrel length (with barrel attachments) ----
        BL = p["barrelLength"]
        HL = p["handguardLength"] if p["handguard"] != "none" else 0.0
        barrel_att = self.att("Barrel")
        if barrel_att == "Long Barrel":
            BL += 0.08
        elif barrel_att == "Short Barrel":
            BL -= min(0.06, BL * 0.35)
        BL = max(BL, HL + 0.015, 0.02)
        BW = p["barrelWidth"] * (1.4 if barrel_att == "Heavy Barrel" else 1.0)
        self.BL, self.BW, self.HL = BL, BW, HL
        HH = p["handguardHeight"]
        self.HH = HH

        # ---- Receiver ----
        self.node = "receiver"
        top = self.receiver(style)
        self.top = top

        # ---- Top rail (permanent) ----
        rail_w, rail_f, rail_b = self.rail_span(style)
        if p["topRail"] and not pistol:
            self.box(rail_w, 0.008, rail_b - rail_f, 0, top + 0.004, (rail_f + rail_b) / 2, mul(DARK, 0.9))
            for i in range(4):
                self.box(rail_w * 1.1, 0.003, 0.004, 0, top + 0.0085, rail_f + (rail_b - rail_f) * (0.12 + i * 0.25), mul(DARK, 1.3))
            top += 0.008

        # ---- Barrel(s), shroud, muzzle device ----
        self.node = "barrel"
        bstart = front
        if style == "revolver":
            bstart = self.cyl_z - RL * 0.15  # the barrel screws into the frame right in front of the cylinder
        self.barrels(bstart, BL, BW)
        muzzle_z = bstart - BL
        self.shroud(bstart, BL, BW)

        # ---- Handguard ----
        self.node = "handguard"
        hg_bottom = self.handguard(bstart, HL, HH, BW)
        self.hg_bottom = hg_bottom
        ub_z = bstart - (min(HL * 0.45, 0.24) if HL > 0.06 else min(0.05, BL * 0.5))  # the off hand stays within reach on long forends
        if p["handguard"] == "pump":
            ub_z = bstart - 0.03 - HL * 0.5
        ub_y = hg_bottom

        # ---- Gas block / tube, ramrod, coils ----
        self.node = "barrel"
        self.barrel_extras(bstart, BL, BW)

        # ---- Muzzle device (built in, unless an attachment is fitted) ----
        self.node = "muzzle"
        muzzle_att = self.att("Muzzle")
        if muzzle_att is None or p["muzzle"] in ("bell", "tip"):
            muzzle_z = self.muzzle(muzzle_z, BW)
        muzzle_z = self.muzzle_attachment(muzzle_att, muzzle_z, BW)
        mesh.muzzle_z = muzzle_z

        # ---- Grip, trigger guard ----
        self.node = "grip"
        grip_pt = self.grip(grip_z)
        self.grip_pt = grip_pt

        # ---- Magazine ----
        self.node = "magazine"
        mag_pt = self.magazine(bstart, BL, BW)

        # ---- Stock ----
        self.node = "stock"
        self.stock(style)

        # ---- Extras on the receiver ----
        self.node = "receiver"
        self.receiver_extras(style, top)

        # ---- Underbarrel: built-in foregrip / bipod, or the attachment ----
        self.node = "underbarrel"
        support = self.underbarrel(ub_z, ub_y, bstart, BL)

        # ---- Sights and carry handle (removable top parts) ----
        self.node = "receiver"
        iron = self.sights(top, bstart, BL, BW)

        # ---- Where an optic goes ----
        mesh.rail = (rail_w, rail_f, rail_b)
        mesh.bore = bore
        mesh.mount = self.mount_height(rail_w, rail_f, rail_b)
        mesh.iron = iron
        oz = (rail_f + rail_b) / 2
        if pistol:
            oz = back - RL * 0.36 if style == "slide" else self.cyl_z
        mesh.optic_z = min(max(oz, -0.085), 0.05)
        mesh.points = {
            "muzzle": (0.0, bore, muzzle_z),
            "optic": (0.0, mesh.mount, mesh.optic_z),
            "underbarrel": (0.0, ub_y, ub_z),
            "magazine": mag_pt,
            "stock": (0.0, -RH * 0.05, back),
            "grip": grip_pt,
            "support": support,
        }
        return mesh

    # ---- Receiver styles ------------------------------------------------------------------------

    def receiver(self, style):
        p, M, F, D, A = self.p, self.M, self.F, self.D, self.A
        RL, RH, RW, front, back, mid, bore = self.RL, self.RH, self.RW, self.front, self.back, self.mid, self.bore
        acc_style = p["accentStyle"]
        if style == "box":
            self.box(RW, RH * 0.55, RL, 0, RH * 0.2, mid, M)
            self.box(RW * 0.92, RH * 0.45, RL * 0.8, 0, -RH * 0.28, mid + RL * 0.05, mul(M, 0.85))
            self.box(0.003, RH * 0.2, RL * 0.26, RW / 2 + 0.0015, RH * 0.2, mid - RL * 0.04, D)  # ejection port
            top = RH * 0.475
        elif style == "slab":
            self.box(RW, RH, RL, 0, 0, mid, M)
            self.box(RW * 1.02, RH * 0.05, RL * 0.92, 0, -RH * 0.12, mid, D)  # seam
            self.box(0.003, RH * 0.22, RL * 0.24, RW / 2 + 0.0015, RH * 0.2, mid - RL * 0.05, D)
            top = RH * 0.5
        elif style == "round":
            self.rod(RH * 0.7, RL, 0, bore, mid, M)
            self.box(RW * 0.8, RH * 0.42, RL * 0.62, 0, -RH * 0.25, mid + RL * 0.12, D)
            top = bore + RH * 0.7 * 0.7071
        elif style == "slide":
            self.box(RW * 0.9, RH * 0.45, RL * 0.85, 0, -RH * 0.25, mid + 0.01, F)  # frame
            self.box(RW, RH * 0.55, RL, 0, RH * 0.22, mid, M)  # slide
            for i in range(3):
                self.box(RW * 1.03, RH * 0.35, 0.004, 0, RH * 0.2, back - 0.012 - i * 0.01, D)  # serrations
            top = RH * 0.22 + RH * 0.275
        elif style == "revolver":
            cz = back - RL * 0.42
            self.cyl_z = cz
            self.box(RW * 0.8, RH * 0.55, RL * 0.62, 0, -RH * 0.12, back - RL * 0.31, M)  # frame
            self.rod(RH * 0.72, RL * 0.3, 0, RH * 0.1, cz, mul(M, 1.08))  # cylinder
            for i in range(3):
                self.box(RH * 0.74, 0.006, RL * 0.26, 0, RH * 0.1, cz, mul(M, 0.7), rz=i * PI / 3)  # flutes
            self.box(RW * 0.55, RH * 0.14, RL * 0.62, 0, RH * 0.45, back - RL * 0.36, M)  # top strap
            top = RH * 0.1 + RH * 0.72 * 0.7071
        elif style == "bullpup":
            self.box(RW, RH * 0.72, RL, 0, -RH * 0.12, mid, F)  # shell
            self.box(RW * 0.86, RH * 0.32, RL * 0.62, 0, RH * 0.36, front + RL * 0.31, M)  # upper receiver
            self.box(RW * 1.01, RH * 0.3, RL * 0.3, 0, -RH * 0.2, back - RL * 0.17, mul(F, 0.85))  # butt section
            self.box(RW * 0.95, RH * 0.25, RL * 0.12, 0, -RH * 0.42, front + RL * 0.06, F, rx=0.5)  # chamfered nose
            top = RH * 0.52
        elif style == "lever":
            self.box(RW, RH * 0.78, RL, 0, -RH * 0.06, mid, M)
            self.box(RW * 0.8, RH * 0.2, RL * 0.92, 0, RH * 0.38, mid, M)  # rounded top
            self.box(RW * 1.02, RH * 0.32, RL * 0.4, 0, -RH * 0.05, mid + RL * 0.12, mul(M, 1.15))  # side plate / loading gate
            top = RH * 0.48
        elif style == "break":
            self.box(RW, RH * 0.8, RL, 0, -RH * 0.08, mid, M)  # action block
            self.box(RW * 1.03, RH * 0.45, RL * 0.6, 0, -RH * 0.15, mid + RL * 0.12, mul(M, 1.2))  # side plates
            self.box(RW * 0.4, RH * 0.35, RL * 0.25, 0, -RH * 0.55, front + RL * 0.1, D)  # hinge knuckle
            top = RH * 0.32
        elif style == "tool":
            self.box(RW, RH, RL * 0.7, 0, RH * 0.1, back - RL * 0.35, M)  # motor housing
            self.box(RW * 1.03, RH * 0.18, RL * 0.5, 0, RH * 0.05, back - RL * 0.32, D)  # grip band
            self.box(RW * 0.6, RH * 0.55, RL * 0.32, 0, bore - RH * 0.12, front + RL * 0.16, mul(D, 1.3))  # nose block
            self.box(RW * 0.9, RH * 0.1, RL * 0.68, 0, RH * 0.62, back - RL * 0.35, D)  # top handle bar
            top = RH * 0.67
        elif style == "toy":
            self.box(RW, RH * 0.8, RL, 0, 0, mid, M)
            self.box(RW * 0.8, RH * 0.2, RL * 0.85, 0, RH * 0.45, mid, M)  # rounded top
            self.box(RW * 0.8, RH * 0.2, RL * 0.9, 0, -RH * 0.45, mid, M)  # rounded belly
            self.box(RW * 1.04, RH * 0.42, RL * 0.55, 0, -RH * 0.02, mid + RL * 0.05, F)  # side panel
            top = RH * 0.55
        else:  # sci
            self.box(RW, RH * 0.66, RL, 0, RH * 0.05, mid, M)
            self.box(RW * 0.55, RH * 0.18, RL * 0.9, 0, RH * 0.47, mid, mul(M, 0.9))  # spine
            self.box(RW * 0.86, RH * 0.32, RL * 0.55, 0, -RH * 0.4, mid + RL * 0.12, F)  # belly
            self.box(RW * 0.7, RH * 0.5, RL * 0.14, 0, -RH * 0.2, front + RL * 0.07, F, rx=0.45)  # swept nose
            top = RH * 0.56
        # Accents on the receiver
        if acc_style == "stripe":
            self.box(RW * 1.03, RH * 0.08, RL * 0.7, 0, RH * 0.3, mid - RL * 0.02, A)
        elif acc_style == "panel" and style != "toy":
            self.box(RW * 1.04, RH * 0.3, RL * 0.36, 0, -RH * 0.05, mid + RL * 0.08, A)
        elif acc_style == "glow":
            self.box(RW * 1.04, RH * 0.05, RL * 0.75, 0, RH * 0.12, mid, A, mode=2)
        return top

    def rail_span(self, style):
        """Where an optic's rail sits: width, front z, back z."""
        RL, RW, front, back = self.RL, self.RW, self.front, self.back
        w = max(RW * 0.5, 0.022)
        if style == "bullpup":
            return (w, front + RL * 0.05, front + RL * 0.55)
        if style == "slide":
            oz = back - RL * 0.36
            return (w, oz - 0.025, oz + 0.025)
        if style == "revolver":
            return (w, self.cyl_z - RL * 0.15, back - RL * 0.1)
        if style == "round":
            return (w, front + 0.01, back - 0.01)
        if style == "tool":
            return (w, back - RL * 0.62, back - RL * 0.08)
        return (w, front + RL * 0.08, back - RL * 0.06)

    # ---- Barrels --------------------------------------------------------------------------------

    def one_barrel(self, x, y, start, BL, BW, c):
        st = self.p["barrelStyle"]
        z = start - BL / 2
        if st == "square":
            self.box(BW, BW, BL, x, y, z, c)
        elif st == "fluted":
            self.box(BW, BW, BL, x, y, z, c)
            for i in range(3):
                self.box(BW * 1.12, BW * 0.3, BL * 0.2, x, y, start - BL * (0.25 + i * 0.25), mul(c, 0.7))
        else:
            self.rod(BW, BL, x, y, z, c)

    def barrels(self, start, BL, BW):
        p = self.p
        n, lay = p["barrels"], p["barrelLayout"]
        c = mul(self.M, 0.75) if sum(self.M) > 1.2 else mul(DARK, 0.75)
        if p["finish"] == "accent":
            c = mul(DARK, 0.9)
        bore = self.bore
        if n == 1 or lay == "single":
            self.one_barrel(0, bore, start, BL, BW, c)
        elif lay == "side":
            for i in range(n):
                self.one_barrel((i - (n - 1) / 2) * BW * 0.98, bore, start, BL, BW, c)
        elif lay == "over":
            for i in range(n):
                self.one_barrel(0, bore - i * BW * 0.98, start, BL, BW, c)
        else:  # rotary
            r = BW * 1.35
            for i in range(n):
                a = i * 2 * PI / n
                self.box(BW, BW, BL, math.cos(a) * r, bore + math.sin(a) * r, start - BL / 2, c)
            self.rod(BW * 1.2, BL, 0, bore, start - BL / 2, mul(c, 0.8))  # spindle
            for k in (0.08, 0.55, 0.97):
                self.rod(r * 2 + BW * 1.6, 0.012, 0, bore, start - BL * k, mul(self.M, 0.9))  # clamps

    def shroud(self, start, BL, BW):
        p = self.p
        s = p["shroud"]
        if s == "none":
            return
        bore = self.bore
        ln = BL * p["shroudLength"]
        z = start - ln / 2
        M, D, A = self.M, self.D, self.A
        if s == "jacket":
            self.rod(BW * 2.0, ln, 0, bore, z, mul(M, 0.9))
        elif s == "perforated":
            self.rod(BW * 2.0, ln, 0, bore, z, mul(M, 0.9))
            for i in range(5):
                self.box(BW * 2.1, BW * 0.5, ln * 0.08, 0, bore, start - ln * (0.12 + i * 0.18), mul(DARK, 0.55))
        elif s == "heatshield":
            self.box(BW * 1.7, BW * 0.7, ln, 0, bore + BW * 0.62, z, mul(M, 0.95))
            for i in range(4):
                self.box(BW * 1.75, BW * 0.25, ln * 0.1, 0, bore + BW * 0.7, start - ln * (0.15 + i * 0.22), mul(DARK, 0.55))
        elif s == "suppressor":
            self.rod(BW * 2.4, ln, 0, bore, z, mul(M, 0.85))
            self.rod(BW * 2.55, 0.008, 0, bore, start - ln + 0.004, D)  # end cap
            self.rod(BW * 2.55, 0.008, 0, bore, start - 0.004, D)
        elif s == "coils":
            for i in range(max(p["coils"], 1)):
                self.box(BW * 2.5, BW * 2.5, 0.014, 0, bore, start - ln * (0.1 + i * 0.85 / max(p["coils"], 1)), A, mode=2)
            self.box(BW * 0.5, BW * 2.7, ln, 0, bore, z, mul(M, 0.9))  # spine through the coils
        else:  # rails: twin prongs instead of a barrel jacket, with a glow between them
            self.box(BW * 0.7, BW * 1.3, ln, -BW * 1.0, bore, z, M)
            self.box(BW * 0.7, BW * 1.3, ln, BW * 1.0, bore, z, M)
            self.box(BW * 1.2, BW * 0.35, ln * 0.92, 0, bore, z, A, mode=2)
            for i in range(max(p["coils"], 0)):
                self.box(BW * 3.0, BW * 2.0, 0.016, 0, bore, start - ln * (0.12 + i * 0.8 / max(p["coils"], 1)), mul(M, 0.75))

    def handguard(self, start, HL, HH, BW):
        p, M, F, D, A = self.p, self.M, self.F, self.D, self.A
        RW, bore = self.RW, self.bore
        h = p["handguard"]
        if h == "none" or HL <= 0:
            return bore - BW * 0.6
        z = start - HL / 2
        y = bore - HH * 0.1
        if h == "rail":
            self.box(RW * 0.9, HH, HL, 0, y, z, mul(DARK, 1.2))
            for i in range(4):
                self.box(RW * 0.92, HH * 0.25, HL * 0.1, 0, y, start - HL * (0.2 + i * 0.2), mul(DARK, 0.7))
            return y - HH / 2
        if h == "slim":
            self.box(RW * 0.78, HH, HL, 0, y, z, F)
            for i in range(3):
                self.box(RW * 0.8, HH * 0.3, HL * 0.12, 0, y - HH * 0.1, start - HL * (0.22 + i * 0.27), mul(F, 0.6))
            return y - HH / 2
        if h == "ribbed":
            self.rod(HH, HL, 0, y, z, F)
            for i in range(5):
                self.rod(HH * 1.06, HL * 0.035, 0, y, start - HL * (0.1 + i * 0.2), mul(F, 0.75))
            return y - HH * 0.71
        if h == "wood":
            self.box(RW * 0.86, HH * 0.7, HL, 0, y - HH * 0.15, z, F)  # lower
            self.box(RW * 0.66, HH * 0.3, HL * 0.75, 0, y + HH * 0.36, start - HL * 0.42, mul(F, 0.9))  # upper guard
            return y - HH * 0.5
        if h == "quad":
            self.box(RW * 0.92, HH, HL, 0, y, z, mul(M, 0.9))
            self.box(RW * 0.4, 0.008, HL * 0.96, 0, y - HH / 2 - 0.004, z, mul(DARK, 0.9))  # bottom rail
            self.box(0.008, HH * 0.4, HL * 0.96, RW * 0.46 + 0.004, y, z, mul(DARK, 0.9))  # side rails
            self.box(0.008, HH * 0.4, HL * 0.96, -RW * 0.46 - 0.004, y, z, mul(DARK, 0.9))
            return y - HH / 2 - 0.008
        if h == "tube":
            self.rod(HH, HL, 0, y, z, mul(DARK, 1.15))
            for i in range(4):
                self.box(0.004, HH * 0.3, HL * 0.12, HH * 0.5, y, start - HL * (0.15 + i * 0.22), mul(DARK, 0.6))
                self.box(0.004, HH * 0.3, HL * 0.12, -HH * 0.5, y, start - HL * (0.15 + i * 0.22), mul(DARK, 0.6))
            return y - HH * 0.71
        if h == "forend":
            fy = bore - BW * 0.5 - HH * 0.35
            self.box(RW * 0.82, HH, HL, 0, fy, z, F)
            return fy - HH / 2
        if h == "pump":
            ty = bore - BW * 0.95
            pz = start - 0.03 - HL / 2
            self.box(RW * 0.95, HH, HL, 0, ty, pz, F)
            for i in range(5):
                self.box(RW * 0.97, HH * 0.8, HL * 0.04, 0, ty - HH * 0.05, pz - HL * 0.4 + i * HL * 0.2, mul(F, 0.7))
            return ty - HH / 2
        # stock: a long one-piece forend under the barrel (musket, M14, bolt guns)
        fy = bore - HH * 0.4
        self.box(RW * 0.95, HH, HL, 0, fy, z, F)
        self.box(RW * 0.7, HH * 0.4, HL * 0.12, 0, fy - HH * 0.35, start - HL + HL * 0.06, mul(F, 0.9))  # tip
        if p["accentStyle"] == "bands":
            for k in (0.3, 0.62, 0.94):
                self.box(RW * 1.0, HH * 0.9 + BW, 0.012, 0, fy + BW * 0.4, start - HL * k, mul(COLORS["brass"], 0.95))
        return fy - HH / 2

    def barrel_extras(self, start, BL, BW):
        p, M, D = self.p, self.M, self.D
        bore = self.bore
        if p["gasBlock"]:
            gl = min(BL * 0.55, max(p["handguardLength"], 0.1) + 0.04)
            self.rod(BW * 0.75, gl, 0, bore + BW * 1.05, start - gl / 2, mul(M, 0.9))  # gas tube
            self.box(BW * 1.4, BW * 2.2, 0.025, 0, bore + BW * 0.5, start - gl, mul(M, 0.85))  # gas block
        if p["ramrod"]:
            self.box(0.007, 0.007, BL * 0.88, 0, bore - BW * 0.5 - p["handguardHeight"] * 0.75, start - BL * 0.5, mul(COLORS["steel"], 0.9))
        if p["coils"] > 0 and p["shroud"] not in ("coils", "rails"):
            for i in range(p["coils"]):
                self.box(BW * 2.4, BW * 2.4, 0.012, 0, bore, start - BL * (0.15 + i * 0.7 / p["coils"]), self.A, mode=2)

    def muzzle(self, z, BW):
        p, M, D, A = self.p, self.M, self.D, self.A
        bore = self.bore
        mz = p["muzzle"]
        if mz == "crown":
            self.rod(BW * 1.2, 0.012, 0, bore, z + 0.006, mul(M, 1.1))
            return z
        if mz == "flash":
            self.rod(BW * 1.3, 0.045, 0, bore, z - 0.0225, mul(DARK, 0.9))
            for i in range(3):
                self.box(BW * 1.35, BW * 0.3, 0.01, 0, bore, z - 0.01 - i * 0.013, mul(DARK, 0.5))
            return z - 0.045
        if mz == "brake":
            self.box(BW * 2.8, BW * 1.5, 0.075, 0, bore, z - 0.0375, mul(M, 1.1))
            self.box(BW * 2.9, BW * 0.45, 0.014, 0, bore, z - 0.02, DARK)
            self.box(BW * 2.9, BW * 0.45, 0.014, 0, bore, z - 0.052, DARK)
            return z - 0.075
        if mz == "bell":
            self.rod(BW * 1.7, 0.03, 0, bore, z - 0.015, mul(M, 1.05))
            return z - 0.03
        if mz == "comp":
            self.box(BW * 1.7, BW * 1.5, 0.05, 0, bore, z - 0.025, mul(M, 1.1))
            for i in range(2):
                self.box(BW * 0.8, BW * 0.4, 0.008, 0, bore + BW * 0.6, z - 0.012 - i * 0.018, DARK)
            return z - 0.05
        if mz == "tip":
            self.box(BW * 1.5, BW * 1.5, 0.025, 0, bore, z - 0.0125, COLORS["orange"] if p["receiver"] == "toy" else mul(A, 0.9))
            return z - 0.025
        if mz == "breacher":
            self.rod(BW * 1.3, 0.04, 0, bore, z - 0.02, mul(DARK, 1.1))
            for i in range(3):
                a = i * 2 * PI / 3
                self.box(BW * 0.3, BW * 0.3, 0.02, math.cos(a) * BW * 0.55, bore + math.sin(a) * BW * 0.55, z - 0.05, mul(DARK, 1.2))
            return z - 0.06
        if mz == "ported":
            for i in range(3):
                self.box(BW * 0.5, 0.004, 0.008, 0, bore + BW * 0.5, z + 0.012 + i * 0.016, DARK)
            return z
        return z

    def muzzle_attachment(self, name, z, BW):
        cls = self.w["class"]
        bore = self.bore
        if name == "Suppressor":
            ln = 0.13 if cls == "Pistol" else 0.2
            r = max(BW * 1.25, 0.024)
            self.rod(r * 1.7, ln, 0, bore, z - ln / 2, mul(METAL, 0.75))
            for i in range(1, 4):
                self.rod(r * 1.06 * 1.7, 0.006, 0, bore, z - ln * i / 4, mul(METAL, 0.55))
            return z - ln
        if name == "Compensator":
            self.box(BW * 1.8, BW * 1.6, 0.06, 0, bore, z - 0.03, METAL)
            for i in range(3):
                self.box(BW * 0.9, BW * 0.45, 0.008, 0, bore + BW * 0.6, z - 0.012 - i * 0.016, DARK)
            return z - 0.06
        if name == "Muzzle Brake":
            self.box(BW * 3.2, BW * 1.3, 0.07, 0, bore, z - 0.035, METAL)
            self.box(BW * 3.3, BW * 0.4, 0.012, 0, bore, z - 0.02, DARK)
            self.box(BW * 3.3, BW * 0.4, 0.012, 0, bore, z - 0.048, DARK)
            return z - 0.07
        if name == "Choke":
            self.rod(BW * 0.75 * 1.7, 0.05, 0, bore, z - 0.025, METAL)
            self.rod(BW * 0.8 * 1.7, 0.008, 0, bore, z - 0.012, self.A)
            return z - 0.05
        return z

    # ---- Grip -----------------------------------------------------------------------------------

    def grip(self, gz):
        p, F, D, M = self.p, self.F, self.D, self.M
        RH, RW = self.RH, self.RW
        g = p["grip"]
        a = p["gripAngle"] * PI / 180
        gy = -RH * 0.5
        if self.p["receiver"] == "slide":
            gy = -RH * 0.47
        gc = F if g != "wood" else COLORS["wood"]
        if g == "wood" and p["furniture"] != "polymer":
            gc = F
        GH = 0.115 if self.p["receiver"] in ("slide", "revolver") else 0.105
        # trigger guard + trigger
        if g != "none":
            tz = gz - 0.035 if g != "straight" else self.back - 0.045
            self.box(RW * 0.5, 0.01, 0.05, 0, gy - 0.026, tz, D)
            self.box(RW * 0.5, 0.026, 0.008, 0, gy - 0.013, tz - 0.025, D)
            self.box(0.006, 0.018, 0.006, 0, gy - 0.009, tz + 0.004, mul(METAL, 0.7))
        if g in ("pistol", "wood"):
            cy = gy - GH / 2 + 0.006
            self.box(RW * 0.82, GH, 0.05, 0, cy, gz + math.sin(a) * GH * 0.45, gc, rx=-a)
            return (0.0, cy, gz + math.sin(a) * GH * 0.45)
        if g == "birdshead":
            self.box(RW * 0.8, GH * 0.55, 0.045, 0, gy - GH * 0.25, gz + 0.008, gc, rx=-a)
            self.box(RW * 0.82, GH * 0.4, 0.05, 0, gy - GH * 0.62, gz + 0.03, gc, rx=-a * 1.8)
            return (0.0, gy - GH * 0.4, gz + 0.015)
        if g == "straight":
            # the hand holds the stock's wrist, just behind the receiver
            return (0.0, -RH * 0.3, self.back + 0.05)
        return (0.0, gy - 0.03, gz)

    # ---- Magazine -------------------------------------------------------------------------------

    def magazine(self, bstart, BL, BW):
        p, w = self.p, self.w
        RL, RH, RW, front, back, bore = self.RL, self.RH, self.RW, self.front, self.back, self.bore
        mt = p["mag"]
        att = self.att("Magazine")
        cap = w["mag"]
        size = min(max(cap / 30.0, 0.5), 1.4)
        c = self.magc
        cal = w.get("caliber", 0.5)
        depth = 0.036 + 0.022 * cal
        mz = (back - RL * 0.3 if self.p["receiver"] == "bullpup" else front + RL * 0.22) + p["magOffset"]
        my = -RH * 0.5
        ang = p["magAngle"] * PI / 180
        ext = 1.5 if att == "Extended Mag" else 1.0
        ln = p["magLength"]
        if att == "Drum Mag" and mt in ("box", "curved", "stick", "beltbox", "side", "cell", "topbox", "pan", "helical"):
            mt = "drum"
        if att == "Fast Mag" and mt in ("box", "curved", "stick", "cell"):
            h = (ln if ln > 0 else 0.15 * size)
            self.box(RW * 0.55, h, depth * 0.85, -RW * 0.3, my - h / 2 * math.cos(ang), mz - h / 2 * math.sin(ang), c, rx=ang)
            self.box(RW * 0.55, h, depth * 0.85, RW * 0.3, my - h / 2 * math.cos(ang), mz - h / 2 * math.sin(ang), mul(c, 0.9), rx=ang)
            self.box(RW * 1.25, 0.02, depth * 0.95, 0, my - h * 0.4, mz - h * 0.4 * math.sin(ang), METAL)  # clamp
            return (0.0, my, mz)
        if mt == "box" or mt == "stick" or mt == "cell" or mt == "strip":
            base = {"box": 0.1, "stick": 0.16, "cell": 0.075, "strip": 0.13}[mt]
            h = (ln if ln > 0 else base * size) * ext
            wd = {"box": 0.62, "stick": 0.5, "cell": 0.75, "strip": 0.32}[mt] * RW
            dz = depth * (0.85 if mt == "stick" else 1.0) * (1.2 if mt == "cell" else 1.0)
            if mt == "strip":
                dz = 0.026
            self.box(wd, h, dz, 0, my - h / 2 * math.cos(ang), mz - h / 2 * math.sin(ang), c, rx=ang)
            if mt == "cell":
                self.box(wd * 1.04, h * 0.12, dz * 0.6, 0, my - h * 0.55 * math.cos(ang), mz - h * 0.55 * math.sin(ang), self.A, rx=ang, mode=2)
            if mt == "strip":
                for i in range(4):
                    k = 0.15 + i * 0.22
                    self.box(wd * 1.3, 0.004, dz * 1.1, 0, my - h * k * math.cos(ang), mz - h * k * math.sin(ang), mul(COLORS["steel"], 1.2), rx=ang)
            else:
                self.box(wd * 1.12, 0.01, dz * 1.1, 0, my - h * math.cos(ang), mz - h * math.sin(ang), mul(c, 0.75), rx=ang)  # floor plate
            return (0.0, my, mz)
        if mt == "curved":
            h = (ln if ln > 0 else 0.19 * size) * ext
            y, z = my, mz
            for i in range(3):
                a = ang + 0.1 + i * 0.2
                seg = h / 3
                self.box(RW * 0.62, seg * 1.04, depth, 0, y - seg / 2 * math.cos(a), z - seg / 2 * math.sin(a), c, rx=a)
                y -= seg * math.cos(a)
                z -= seg * math.sin(a)
            return (0.0, my, mz)
        if mt == "drum":
            r = 0.13 if cap < 60 or att == "Drum Mag" else 0.15
            self.box(0.06, r, r, 0, my - 0.02 - r * 0.5, mz, c)
            self.box(0.06, r, r, 0, my - 0.02 - r * 0.5, mz, mul(c, 0.92), rx=PI / 4)
            self.box(0.066, r * 0.3, r * 0.3, 0, my - 0.02 - r * 0.5, mz, mul(METAL, 0.6))
            self.box(0.03, 0.05, 0.035, 0, my - 0.012, mz, mul(c, 0.8))
            return (0.0, my, mz)
        if mt == "tube":
            tl = (ln if ln > 0 else BL * 0.72) * (1.25 if att == "Extended Mag" else 1.0)
            tl = min(tl, BL * 0.97)
            self.rod(BW * 0.8, tl, 0, bore - BW * 0.95, bstart - tl / 2, mul(DARK, 0.95))
            self.box(BW * 0.9, BW * 0.9, 0.01, 0, bore - BW * 0.95, bstart - tl + 0.004, mul(METAL, 0.6))  # cap
            if att == "Fast Mag":
                for i in range(4):  # shell carrier on the side
                    self.box(0.012, 0.03, 0.014, RW / 2 + 0.006, -RH * 0.05, front + RL * (0.2 + i * 0.17), SHELL_RED)
            return (0.0, -RH * 0.5, front + RL * 0.3)
        if mt == "pan":
            r = 0.12 * max(size, 0.8) * (1.15 if att == "Extended Mag" else 1.0)
            py = RH * 0.62 if self.p["receiver"] != "toy" else RH * 0.68
            self.box(r, 0.028, r, 0, py, self.mid, c)
            self.box(r, 0.028, r, 0, py, self.mid, mul(c, 0.93), ry=PI / 4)
            self.box(r * 0.3, 0.034, r * 0.3, 0, py, self.mid, mul(self.A, 0.9))  # hub
            return (0.0, py - 0.014, self.mid)
        if mt == "topbox":
            h = 0.034 * (1.3 if att == "Extended Mag" else 1.0)
            self.box(RW * 0.9, h, RL * 0.62, 0, RH * 0.52 + h / 2, front + RL * 0.34, c, alpha=1.0)
            self.box(RW * 0.92, h * 0.25, RL * 0.5, 0, RH * 0.52 + h * 0.62, front + RL * 0.34, mul(c, 0.7))
            return (0.0, RH * 0.52, front + RL * 0.34)
        if mt == "side":
            h = (ln if ln > 0 else 0.15 * size) * ext
            self.box(h, 0.03, depth, -RW / 2 - h / 2, bore, mz, c)
            return (-RW / 2, bore, mz)
        if mt == "beltbox":
            big = 1.3 if att == "Extended Mag" else 1.0
            bw = RW * 1.45 * big
            bh = 0.12 * big * min(max(size, 0.9), 1.25)
            self.box(bw, bh, 0.14 * big, -RW * 0.35, my - bh / 2 - 0.005, mz, c)
            self.box(bw * 1.02, 0.012, 0.14 * big * 0.8, -RW * 0.35, my - 0.008, mz, mul(c, 0.75))  # lid
            self.box(0.03, 0.016, 0.05, -RW * 0.1, my + 0.006, mz, mul(METAL, 0.6))  # feed
            for i in range(4):
                self.box(0.012, 0.008, 0.02, -RW * 0.6, my + 0.01 + i * 0.012, mz - 0.03, mul(COLORS["brass"], 0.9))  # belt
            return (0.0, my, mz)
        if mt == "helical":
            hl = max(BL * 0.55, 0.15) * (1.2 if att == "Extended Mag" else 1.0)
            hy = self.hg_bottom - 0.025
            self.rod(0.05, hl, 0, hy, front - hl / 2 + 0.02, c)
            for i in range(4):
                self.rod(0.053, 0.006, 0, hy, front + 0.02 - hl * (0.15 + i * 0.22), mul(c, 0.7))
            return (0.0, hy + 0.025, front)
        if mt == "grip":
            # in the pistol grip: only the base plate shows, or a long extension
            gz = self.grip_z + math.sin(p["gripAngle"] * PI / 180) * 0.11
            by = -RH * 0.47 - 0.115
            if att == "Extended Mag" or ln > 0:
                h = (ln if ln > 0 else 0.0) + (0.06 if att == "Extended Mag" else 0.0)
                a2 = p["gripAngle"] * PI / 180
                self.box(RW * 0.7, h, 0.042, 0, by - h / 2 + 0.005, gz + math.sin(a2) * h * 0.5, c, rx=-a2)
                self.box(RW * 0.82, 0.012, 0.05, 0, by - h + 0.004, gz + math.sin(a2) * h, mul(c, 0.8), rx=-a2)
            elif att == "Fast Mag":
                self.box(RW * 1.1, 0.025, 0.06, 0, by, gz, c, rx=-p["gripAngle"] * PI / 180)
            else:
                self.box(RW, 0.016, 0.058, 0, by + 0.005, gz, c, rx=-p["gripAngle"] * PI / 180)
            return (0.0, -RH * 0.47, self.grip_z)
        if mt == "striker":
            dr = 0.135 * (1.12 if att == "Extended Mag" else 1.0)
            dz = front + 0.065
            dy = bore - dr * 0.32
            self.rod(dr, 0.1, 0, dy, dz, c)
            self.rod(dr * 0.4, 0.106, 0, dy, dz, mul(METAL, 0.6))  # hub
            self.box(0.01, 0.01, 0.12, dr * 0.42, dy - dr * 0.1, dz + 0.02, mul(METAL, 0.7))  # winding lever
            return (0.0, dy, dz)
        if mt == "internal":
            if att == "Extended Mag" or att == "Fast Mag":
                h = 0.055 * (1.4 if att == "Extended Mag" else 1.0)
                self.box(RW * 0.6, h, depth, 0, my - h / 2, mz, mul(DARK, 1.3))
            else:
                self.box(RW * 0.72, 0.01, 0.07, 0, my - 0.004, mz, mul(DARK, 1.2))  # floor plate
            return (0.0, my, mz)
        # no magazine: where the hand loads the gun
        if self.p["receiver"] == "revolver":
            return (-RH * 0.36 - 0.012, RH * 0.1, self.cyl_z)  # left side of the cylinder
        if self.p["receiver"] == "break":
            return (0.0, bore + BW * 0.3, front - 0.015)  # the breech
        return (0.0, my, mz)

    # ---- Stock ----------------------------------------------------------------------------------

    def stock(self, style):
        p, F, D, M = self.p, self.F, self.D, self.M
        RH, RW, back = self.RH, self.RW, self.back
        att = self.att("Stock")
        ln = p["stockLength"]
        y = -RH * 0.05
        st = p["stock"]
        drop = p["stockDrop"] * PI / 180
        if att == "No Stock":
            self.box(RW * 0.6, RH * 0.5, 0.03, 0, y, back + 0.015, DARK)
            return
        if att == "Light Stock":
            self.box(0.012, 0.012, ln, 0, y + RH * 0.3, back + ln / 2, METAL)
            self.box(0.012, 0.012, ln, 0, y - RH * 0.3, back + ln / 2, METAL)
            self.box(RW * 0.6, RH * 1.1, 0.02, 0, y, back + ln, mul(METAL, 0.8))
            return
        if att == "Heavy Stock":
            self.box(RW * 1.1, RH * 1.05, ln, 0, y - 0.005, back + ln / 2, mul(METAL, 0.7))
            self.box(RW * 0.9, RH * 0.3, ln * 0.6, 0, y + RH * 0.55, back + ln * 0.55, mul(METAL, 0.6), flags=F_COMB)
            self.box(RW * 1.15, RH * 1.15, 0.03, 0, y - 0.005, back + ln, PAD)
            return
        if st == "none":
            if style not in ("slide", "revolver", "break", "tool"):
                self.box(RW * 0.7, RH * 0.6, 0.02, 0, y, back + 0.01, mul(M, 0.8))  # end cap
            return
        dy = -math.sin(drop) * ln * 0.5  # the stock droops by its angle
        if st == "fixed":
            self.box(RW * 0.6, RH * 0.45, ln * 0.6, 0, y + RH * 0.12, back + ln * 0.3, F)
            self.box(RW * 0.8, RH * 1.0, ln * 0.5, 0, y - 0.01 + dy, back + ln * 0.72, F, rx=drop)
            self.box(RW * 0.82, RH * 1.02, 0.012, 0, y - 0.01 + dy * 1.6, back + ln * 0.97, RUBBER, rx=drop)
        elif st == "crane":
            self.rod(RH * 0.32, ln * 0.85, 0, RH * 0.18, back + ln * 0.42, mul(DARK, 1.1))  # buffer tube
            self.box(RW * 0.75, RH * 0.95, ln * 0.42, 0, y + dy, back + ln * 0.74, F, rx=drop)
            self.box(RW * 0.55, RH * 0.3, ln * 0.3, 0, y + RH * 0.5 + dy, back + ln * 0.72, mul(F, 0.85), rx=drop, flags=F_COMB)  # cheek weld
            self.box(RW * 0.8, RH * 1.0, 0.012, 0, y + dy * 1.6, back + ln * 0.96, RUBBER, rx=drop)
        elif st == "skeleton":
            self.box(0.015, 0.015, ln, 0, y + RH * 0.25, back + ln / 2, mul(DARK, 1.3))
            self.box(0.015, 0.015, ln * 1.05, 0, y - RH * 0.3, back + ln / 2, mul(DARK, 1.3), rx=0.08)
            self.box(RW * 0.7, RH * 0.95, 0.025, 0, y, back + ln, mul(DARK, 1.2))
        elif st == "folding":
            self.box(0.01, 0.01, ln * 0.9, RW * 0.3, y + RH * 0.2, back + ln * 0.45, mul(DARK, 1.4))
            self.box(0.01, 0.01, ln * 0.9, -RW * 0.3, y + RH * 0.2, back + ln * 0.45, mul(DARK, 1.4))
            self.box(RW * 0.7, RH * 0.9, 0.015, 0, y, back + ln * 0.9, mul(DARK, 1.3))
        elif st == "wire":
            self.box(0.008, 0.008, ln, 0, y + RH * 0.3, back + ln / 2, mul(METAL, 0.8))
            self.box(0.008, 0.008, ln * 0.95, 0, y - RH * 0.32, back + ln * 0.48, mul(METAL, 0.8))
            self.box(0.03, RH * 0.75, 0.008, 0, y, back + ln, mul(METAL, 0.8))
        elif st == "wood":
            self.box(RW * 0.9, RH * 0.8, ln, 0, y - 0.02 + dy, back + ln / 2, F, rx=-0.06 + drop)
            self.box(RW * 0.95, RH * 1.1, 0.025, 0, y - 0.03 + dy * 2, back + ln, DARK, rx=drop)
        elif st == "straight":
            self.box(RW * 0.62, RH * 0.5, ln * 0.36, 0, -RH * 0.12, back + ln * 0.18, F)  # wrist
            self.box(RW * 0.8, RH * 0.95, ln * 0.7, 0, -RH * 0.22 + dy * 1.3, back + ln * 0.62, F, rx=drop)
            self.box(RW * 0.82, RH * 1.0, 0.012, 0, -RH * 0.22 + dy * 2.6, back + ln * 0.97, mul(F, 0.55), rx=drop)
        elif st == "thumbhole":
            self.box(RW * 0.75, RH * 0.32, ln * 0.85, 0, y + RH * 0.28, back + ln * 0.5, F, flags=F_COMB)  # comb
            self.box(RW * 0.75, RH * 0.28, ln * 0.7, 0, y - RH * 0.55, back + ln * 0.6, F, rx=-0.12)  # lower bar
            self.box(RW * 0.82, RH * 1.25, ln * 0.28, 0, y - RH * 0.1, back + ln * 0.86, F)  # butt
            self.box(RW * 0.84, RH * 1.27, 0.012, 0, y - RH * 0.1, back + ln, RUBBER)
        elif st == "chassis":
            self.box(RW * 0.5, RH * 0.35, ln * 0.9, 0, y + RH * 0.15, back + ln * 0.45, mul(D, 1.1))  # spine
            self.box(RW * 0.65, RH * 0.32, ln * 0.4, 0, y + RH * 0.45, back + ln * 0.6, mul(D, 1.3), flags=F_COMB)  # cheek riser
            self.box(RW * 0.75, RH * 1.2, 0.03, 0, y - RH * 0.15, back + ln * 0.92, mul(D, 1.2))  # butt
            self.box(0.012, RH * 0.6, 0.012, 0, y - RH * 0.85, back + ln * 0.8, mul(DARK, 0.9))  # monopod
        elif st == "bullpup":
            self.box(RW * 1.02, RH * 0.8, 0.02, 0, -RH * 0.12, back + 0.01, RUBBER)
            if p["cheekRest"]:
                self.box(RW * 0.7, RH * 0.18, self.RL * 0.25, 0, RH * 0.3, back - self.RL * 0.15, mul(F, 0.9), flags=F_COMB)
            return
        elif st == "toy":
            self.box(RW * 1.1, RH * 1.15, ln * 0.6, 0, y, back + ln * 0.3, self.F)
            self.box(RW * 1.12, RH * 0.45, ln * 0.4, 0, y, back + ln * 0.5, self.A)
            self.box(RW * 1.0, RH * 1.2, 0.02, 0, y, back + ln * 0.62, mul(self.M, 0.9))
        elif st == "spade":
            # twin spade handles behind the receiver (no shoulder stock)
            self.box(0.016, 0.016, ln * 0.5, RW * 0.32, y, back + ln * 0.25, mul(DARK, 1.2))
            self.box(0.016, 0.016, ln * 0.5, -RW * 0.32, y, back + ln * 0.25, mul(DARK, 1.2))
            self.box(RW * 0.8, 0.02, 0.02, 0, y, back + ln * 0.5, mul(DARK, 1.2))
            self.box(0.022, RH * 0.8, 0.022, RW * 0.32, y - RH * 0.1, back + ln * 0.5, self.F)
            self.box(0.022, RH * 0.8, 0.022, -RW * 0.32, y - RH * 0.1, back + ln * 0.5, self.F)
        if p["cheekRest"] and st not in ("crane", "thumbhole", "chassis", "bullpup", "spade"):
            self.box(RW * 0.7, RH * 0.25, ln * 0.5, 0, y + RH * 0.55, back + ln * 0.55, mul(F, 0.85), flags=F_COMB)
        if p["accentStyle"] == "tips":
            self.box(RW * 0.84, RH * 0.3, 0.014, 0, y + RH * 0.2, back + ln * 0.97, self.A)

    # ---- Receiver extras ------------------------------------------------------------------------

    def receiver_extras(self, style, top):
        p, M, D, A = self.p, self.M, self.D, self.A
        RL, RH, RW, front, back, bore = self.RL, self.RH, self.RW, self.front, self.back, self.bore
        b = p["bolt"]
        if b == "side":
            self.box(0.055, 0.011, 0.011, RW * 0.5 + 0.026, bore, back - 0.035, mul(METAL, 0.7))
            self.box(0.022, 0.022, 0.022, RW * 0.5 + 0.055, bore - 0.006, back - 0.03, mul(METAL, 0.8))
        elif b == "rear":
            self.rod(0.018, 0.035, 0, bore, back + 0.012, mul(METAL, 0.75))
        elif b == "charging":
            self.box(RW * 0.4, 0.008, 0.02, 0, top - 0.006, back - 0.01, mul(DARK, 1.4))
            self.box(RW * 1.0, 0.008, 0.01, 0, top - 0.006, back - 0.004, mul(DARK, 1.4))
        elif b == "top":
            self.box(0.012, 0.016, 0.03, RW * 0.5 + 0.004, RH * 0.3, front + RL * 0.4, mul(A, 0.9))
        if p["hammer"]:
            hz = back + 0.004 if style not in ("break", "lever") else back - 0.012
            hy = top - 0.012 if style != "revolver" else self.RH * 0.32
            self.box(0.009, 0.022, 0.012, 0, hy, hz, mul(DARK, 1.2), rx=-0.45)
        if p["lever"]:
            ly = -RH * 0.5
            self.box(0.012, 0.012, 0.11, 0, ly - 0.048, back - 0.04, mul(METAL, 0.65))  # loop bottom
            self.box(0.012, 0.045, 0.012, 0, ly - 0.025, back - 0.095, mul(METAL, 0.65))  # loop front
            self.box(0.012, 0.03, 0.012, 0, ly - 0.03, back + 0.012, mul(METAL, 0.65))  # loop rear
        if p["canister"]:
            self.rod(0.05, 0.12, 0, -RH * 0.55, back + 0.05, mul(A, 0.85))
            self.box(0.01, 0.01, 0.18, RW * 0.55, -RH * 0.3, back - 0.04, mul(DARK, 0.6))  # hose
        if p["sideSaddle"]:
            self.box(0.008, RH * 0.5, RL * 0.55, -RW / 2 - 0.004, RH * 0.05, front + RL * 0.4, mul(DARK, 1.2))
            for i in range(4):
                self.box(0.012, RH * 0.38, 0.016, -RW / 2 - 0.01, RH * 0.05, front + RL * (0.2 + i * 0.13), SHELL_RED)

    # ---- Underbarrel ----------------------------------------------------------------------------

    def underbarrel(self, z, y, bstart, BL):
        p, F, D = self.p, self.F, self.D
        att = self.att("Underbarrel")
        grip = self.grip_pt
        if att == "Vertical Grip":
            self.box(0.034, 0.12, 0.034, 0, y - 0.06, z, mul(METAL, 0.8))
            self.box(0.04, 0.012, 0.04, 0, y - 0.12, z, mul(METAL, 0.6))
            return (0.0, y - 0.055, z)
        if att == "Angled Grip":
            self.box(0.036, 0.045, 0.11, 0, y - 0.025, z + 0.01, mul(METAL, 0.8), rx=-0.45)
            return (0.0, y - 0.03, z + 0.01)
        if att == "Laser":
            self.box(0.035, 0.035, 0.07, 0, y - 0.02, z, mul(METAL, 0.7))
            self.box(0.014, 0.014, 0.004, 0, y - 0.02, z - 0.037, RED_GLOW, mode=2)
            self.box(0.004, 0.004, 6.0, 0, y - 0.02, z - 3.04, RED_GLOW, mode=2, alpha=0.35, flags=F_BLEND)
        if att is None:
            fg = p["foregrip"]
            if fg == "vertical":
                self.box(0.032, 0.085, 0.034, 0, y - 0.0425, z, F)
                return (0.0, y - 0.045, z)
            if fg == "stub":
                self.box(0.03, 0.04, 0.03, 0, y - 0.02, z, F)
            if fg == "folding":
                self.box(0.022, 0.07, 0.022, 0, y - 0.033, z, mul(DARK, 1.3), rx=0.35)
                return (0.0, y - 0.04, z - 0.01)
            if fg == "strap":
                self.box(self.RW * 0.6, 0.008, 0.06, 0, y - 0.035, z, mul(DARK, 1.2))
                self.box(self.RW * 0.6, 0.035, 0.008, 0, y - 0.018, z - 0.03, mul(DARK, 1.2))
            bp = p["bipod"]
            if bp == "folded":
                self.box(0.008, 0.008, 0.15, 0.02, y - 0.006, bstart - BL * 0.62 + 0.075, mul(METAL, 0.6))
                self.box(0.008, 0.008, 0.15, -0.02, y - 0.006, bstart - BL * 0.62 + 0.075, mul(METAL, 0.6))
                self.box(0.05, 0.014, 0.02, 0, y - 0.004, bstart - BL * 0.62, mul(DARK, 1.1))
            elif bp == "deployed":
                by = self.bore - self.BW * 0.5 - 0.01
                bz = bstart - BL * 0.8
                self.box(0.04, 0.016, 0.022, 0, by, bz, mul(DARK, 1.1))
                self.box(0.009, 0.16, 0.009, 0.02, by - 0.075, bz - 0.025, mul(METAL, 0.6), rx=0.3, rz=-0.12)
                self.box(0.009, 0.16, 0.009, -0.02, by - 0.075, bz - 0.025, mul(METAL, 0.6), rx=0.3, rz=0.12)
        if p["handguard"] == "pump":
            return (0.0, y, z)
        if p["handguard"] == "none" and self.p["receiver"] in ("slide", "revolver", "break") and p["stock"] == "none":
            # a handgun: the support hand cups the shooting hand
            return (0.0, grip[1] - 0.01, grip[2] - 0.012)
        return (0.0, y, z)

    # ---- Sights ---------------------------------------------------------------------------------

    def sights(self, top, bstart, BL, BW):
        p, D, A = self.p, self.D, self.A
        RL, RH, RW, front, back, bore = self.RL, self.RH, self.RW, self.front, self.back, self.bore
        s = p["sights"]
        pistol = p["receiver"] in ("slide", "revolver")
        rise = 0.01 if pistol else 0.02
        iron = top + rise
        T = F_TOP
        if p["carryHandle"]:
            h = 0.036
            hz0, hz1 = front + RL * 0.12, front + RL * 0.12 + min(0.12, RL * 0.55)
            self.box(0.014, h, 0.012, 0, top + h / 2, hz0, mul(DARK, 1.1), flags=T)
            self.box(0.014, h, 0.012, 0, top + h / 2, hz1, mul(DARK, 1.1), flags=T)
            self.box(0.02, 0.012, hz1 - hz0 + 0.012, 0, top + h, (hz0 + hz1) / 2, mul(DARK, 1.1), flags=T)
            iron = top + h + 0.012
        if s == "none":
            return iron
        # front sight sits at the end of the handguard or near the muzzle
        if style_is(p, "slide"):
            fz = front + 0.01
        elif pistol:
            fz = bstart - BL + 0.012
        elif p["handguard"] in ("none", "forend", "pump", "stock") or self.HL < 0.04:
            fz = bstart - BL + 0.02
        else:
            fz = bstart - self.HL - 0.01
        if p["gasBlock"] and not pistol:
            fz = bstart - min(BL * 0.55, max(p["handguardLength"], 0.1) + 0.04)
        base = bore + BW * 0.45
        if s == "bead":
            if iron - base > 0.016:
                self.box(0.008, iron - 0.008 - base, 0.03, 0, (iron - 0.008 + base) / 2, fz - 0.005, mul(DARK, 1.2), flags=T)  # ramp
            self.box(0.01, 0.01, 0.01, 0, iron - 0.005, fz, A if p["accentStyle"] != "none" else COLORS["brass"], flags=T)
            return iron
        tall = s == "tall"
        bw = 0.024 if tall else 0.014
        if iron - base > 0.004:
            self.box(bw, iron - base, 0.016 if tall else 0.01, 0, (iron + base) / 2, fz, mul(DARK, 1.1), flags=T)  # post
        if tall:
            self.box(0.026, 0.008, 0.02, 0, iron - 0.004, fz, mul(DARK, 0.9), flags=T)  # hood
        if p["carryHandle"]:
            return iron
        rz = back - 0.02 if not pistol else back - 0.012
        if p["receiver"] == "bullpup":
            rz = front + RL * 0.5
        if s == "aperture" or s == "ghost":
            self.box(0.026, 0.006, 0.01, 0, iron - 0.003, rz, mul(DARK, 1.1), flags=T)
            self.box(0.006, iron - top + 0.008, 0.01, -0.01, (iron + top) / 2, rz, mul(DARK, 1.1), flags=T)
            self.box(0.006, iron - top + 0.008, 0.01, 0.01, (iron + top) / 2, rz, mul(DARK, 1.1), flags=T)
            if s == "ghost":
                self.box(0.005, iron - top + 0.014, 0.014, -0.017, (iron + top) / 2 + 0.003, rz, mul(DARK, 1.2), flags=T)
                self.box(0.005, iron - top + 0.014, 0.014, 0.017, (iron + top) / 2 + 0.003, rz, mul(DARK, 1.2), flags=T)
        else:
            self.box(0.03 if not pistol else 0.026, iron - top, 0.012, 0, (iron + top) / 2, rz, mul(DARK, 1.1), flags=T)  # rear notch
        return iron

    # ---- Optic mount ----------------------------------------------------------------------------

    def mount_height(self, rw, rf, rb):
        """The highest permanent part under the rail: the optic's rail sits on it."""
        m = self.top
        for pt in self.mesh.parts:
            if pt.flags & (F_TOP | F_OPTIC | F_BLEND):
                continue
            lo, hi = pt.bounds()
            if hi[0] > -rw / 2 and lo[0] < rw / 2 and hi[2] > rf and lo[2] < rb:
                m = max(m, hi[1])
        return m


F_COMB = 64  # stock comb / cheek rest: drops under an optic's eyepiece


def style_is(p, s):
    return p["receiver"] == s


def has_scope(w):
    """Any scope or sight on top: an optic attachment or a built-in scope."""
    return w.get("optic", "Iron") != "Iron" or builtin_scope(w)


def builtin_scope(w):
    return w.get("optic", "Iron") == "Iron" and w.get("adsZoom", 1.35) >= 1.8 and w["class"] != "Pistol"


def build(w, kit, cut=0.0):
    """The finished box list for a weapon + kit (body, then with an optic: top parts removed and the rest
    kept under the mount, then the optic itself)."""
    p = w["_p"]
    b = B(w, kit, p)
    mesh = b.build()
    scoped = has_scope(w)
    if scoped:
        mesh.parts = [pt for pt in mesh.parts if not (pt.flags & F_TOP)]
        clamp_under_optic(mesh)
    # the optic
    optic_parts(b, w, mesh, cut)
    return mesh


def clamp_under_optic(mesh):
    """With an optic fitted, nothing on the gun may rise into it: parts near it are trimmed to the mount."""
    rw, rf, rb = mesh.rail
    z0, z1 = mesh.optic_z - 0.38, mesh.optic_z + 0.27
    lim = mesh.mount + 0.001
    out = []
    for pt in mesh.parts:
        lo, hi = pt.bounds()
        if hi[1] > lim and hi[2] > z0 and lo[2] < z1 and hi[0] > -0.056 and lo[0] < 0.056 and not (pt.flags & F_BLEND):
            ex = hi[1] - lim
            if pt.rx == 0 and pt.rz == 0 and pt.ry == 0 and lo[1] < lim - 0.004:
                pt.y -= ex / 2
                pt.sy -= ex
            else:
                pt.y -= ex
        out.append(pt)
    mesh.parts = out


# ---- Optics (port of GunModel.DrawOptic) ---------------------------------------------------------

def sight_height(w, mesh):
    o = w.get("optic", "Iron")
    mount = mesh.mount
    table = {"RedDot": 0.035, "MiniDot": 0.026, "Holo": 0.04, "Prism2": 0.042, "Acog": 0.045, "Hybrid3": 0.045, "Lpvo6": 0.05,
             "Scope4": 0.055, "Scope8": 0.055, "Scope12": 0.062}
    if o in table:
        return mount + table[o]
    return mount + 0.05 if builtin_scope(w) else mesh.iron


def ads_z(w, mesh):
    z = mesh.optic_z
    o = w.get("optic", "Iron")
    if o == "RedDot":
        rear, relief = z - 0.012, 0.15
    elif o == "MiniDot":
        rear, relief = z - 0.004, 0.12
    elif o == "Holo":
        rear, relief = z - 0.0005, 0.13
    elif o == "Prism2":
        rear, relief = z + 0.045, 0.06
    elif o == "Hybrid3":
        rear, relief = z + 0.071, 0.055
    elif o == "Lpvo6":
        rear, relief = z + 0.16, 0.05
    elif o == "Scope12":
        rear, relief = z + 0.24, 0.05
    elif o == "Acog":
        rear, relief = z + 0.05, 0.055
    elif o in ("Scope4", "Scope8"):
        rear, relief = z + 0.26 * (1.25 if o == "Scope8" else 1.0) * 0.63, 0.05
    else:
        if builtin_scope(w):
            rear, relief = z + 0.26 * 0.85 * 0.63, 0.05
        else:
            return -0.32
    return -relief - rear


def optic_parts(b, w, mesh, cut=0.0):
    """Adds the optic's parts. Shell parts carry F_SHELL (their alpha fades with the cut-away at draw time)."""
    b.node = "optic"
    b.flags = F_OPTIC
    o = w.get("optic", "Iron")
    z = mesh.optic_z
    has = o != "Iron"
    mount = mesh.mount
    rw, rf, rb = mesh.rail
    if has:
        b.box(rw, 0.008, rb - rf, 0, mount + 0.004, (rf + rb) / 2, mul(DARK, 1.5))  # rail
    sight = sight_height(w, mesh)
    S = F_SHELL

    def shell(sx, sy, sz, x, y, z_, c, rz=0.0, mode=0, alpha=1.0):
        b.box(sx, sy, sz, x, y, z_, c, rz=rz, mode=mode, alpha=alpha, flags=S | (F_BLEND if alpha < 0.999 else 0))

    def shell_cyl(r, ln, x, y, z_, c):
        shell(r * 1.7, r * 1.7, ln, x, y, z_, c)
        shell(r * 1.7, r * 1.7, ln, x, y, z_, mul(c, 0.93), rz=PI / 4)

    def shell_tube(hi, wall, ln, x, y, z_, c):
        oo = hi + wall / 2
        ww = hi * 2 + wall * 2
        shell(ww, wall, ln, x, y + oo, z_, c)
        shell(ww, wall, ln, x, y - oo, z_, mul(c, 0.9))
        shell(wall, ww, ln, x - oo, y, z_, mul(c, 0.95))
        shell(wall, ww, ln, x + oo, y, z_, mul(c, 0.95))

    def tube(hi, wall, ln, x, y, z_, c, flags=0):
        oo = hi + wall / 2
        ww = hi * 2 + wall * 2
        b.box(ww, wall, ln, x, y + oo, z_, c, flags=flags)
        b.box(ww, wall, ln, x, y - oo, z_, mul(c, 0.9), flags=flags)
        b.box(wall, ww, ln, x - oo, y, z_, mul(c, 0.95), flags=flags)
        b.box(wall, ww, ln, x + oo, y, z_, mul(c, 0.95), flags=flags)

    OB = OPTIC_BODY
    if o == "RedDot":
        b.box(0.034, 0.012, 0.05, 0, mount + 0.008, z, OB)
        b.box(0.006, 0.04, 0.012, -0.017, sight - 0.002, z - 0.018, OB)
        b.box(0.006, 0.04, 0.012, 0.017, sight - 0.002, z - 0.018, OB)
        b.box(0.04, 0.006, 0.012, 0, sight + 0.019, z - 0.018, OB)
        b.box(0.028, 0.028, 0.002, 0, sight, z - 0.018, V(0.6, 0.9, 0.7), mode=2, alpha=0.12, flags=F_BLEND)
        shell(0.004, 0.004, 0.002, 0, sight, z - 0.019, RED_GLOW, mode=2)
    elif o == "MiniDot":
        b.box(0.03, 0.012, 0.03, 0, mount + 0.006, z, mul(OB, 0.9))
        b.box(0.004, 0.028, 0.008, -0.013, sight - 0.001, z - 0.008, OB)
        b.box(0.004, 0.028, 0.008, 0.013, sight - 0.001, z - 0.008, OB)
        b.box(0.03, 0.004, 0.008, 0, sight + 0.013, z - 0.008, OB)
        b.box(0.006, 0.004, 0.008, 0.012, mount + 0.014, z + 0.008, mul(METAL, 0.7))
        b.box(0.022, 0.022, 0.0015, 0, sight, z - 0.008, V(0.6, 0.9, 0.7), mode=2, alpha=0.1, flags=F_BLEND)
        shell(0.003, 0.003, 0.002, 0, sight, z - 0.009, GREEN_GLOW, mode=2)
    elif o == "Holo":
        b.box(0.05, 0.018, 0.075, 0, mount + 0.01, z, OB)
        hood = 0.035 + (0.008 - 0.035) * cut
        tube(0.022, 0.006, hood, 0, sight, z - 0.0355 + hood / 2, mul(OB, 1.1), flags=F_HOOD)
        b.box(0.012, 0.014, 0.02, 0.03, mount + 0.015, z + 0.02, mul(OB, 0.8))
        b.box(0.042, 0.042, 0.002, 0, sight, z - 0.034, mul(CYAN_GLOW, 0.6), mode=2, alpha=0.12, flags=F_BLEND)
    elif o == "Prism2":
        shell(0.036, 0.014, 0.06, 0, mount + 0.007, z, mul(OB, 0.85))
        shell(0.028, 0.006, 0.05, 0, mount + 0.016, z, mul(OB, 0.8))
        shell_tube(0.017, 0.007, 0.07, 0, sight, z, OB)
        shell_tube(0.019, 0.004, 0.012, 0, sight, z - 0.041, mul(OB, 1.1))
        shell_tube(0.018, 0.005, 0.01, 0, sight, z + 0.04, RUBBER)
        shell(0.02, 0.008, 0.03, 0, sight + 0.028, z, mul(OB, 0.9))
        shell(0.01, 0.016, 0.016, 0.029, sight, z + 0.005, mul(METAL, 0.7))
        shell(0.034, 0.034, 0.002, 0, sight, z - 0.046, mul(V(1, 0.8, 0.5), 0.5), mode=2, alpha=0.25)
    elif o == "Acog":
        shell(0.026, 0.03, 0.05, 0, mount + 0.015, z, mul(OB, 0.9))
        shell_tube(0.016, 0.006, 0.12, 0, sight, z - 0.01, OB)
        shell_tube(0.021, 0.006, 0.03, 0, sight, z - 0.075, mul(OB, 0.9))
        shell(0.006, 0.006, 0.06, 0, sight + 0.024, z, RED_GLOW, mode=2)
    elif o == "Hybrid3":
        shell(0.03, 0.024, 0.04, 0, mount + 0.012, z - 0.04, mul(OB, 0.85))
        shell_tube(0.016, 0.005, 0.03, 0, sight, z - 0.045, OB)
        shell(0.004, 0.004, 0.002, 0, sight, z - 0.05, RED_GLOW, mode=2)
        shell(0.03, 0.02, 0.03, 0, mount + 0.01, z + 0.03, mul(OB, 0.8))
        shell(0.008, 0.026, 0.026, 0.022, sight - 0.008, z + 0.03, mul(METAL, 0.65))
        shell_tube(0.014, 0.005, 0.06, 0, sight, z + 0.03, mul(OB, 0.95))
        shell_tube(0.016, 0.004, 0.012, 0, sight, z + 0.065, RUBBER)
    elif o == "Lpvo6":
        ln, r = 0.22, 0.017
        shell(0.02, sight - mount, 0.014, 0, (mount + sight) / 2, z - ln * 0.22, mul(OB, 0.8))
        shell(0.02, sight - mount, 0.014, 0, (mount + sight) / 2, z + ln * 0.22, mul(OB, 0.8))
        shell_cyl(r, ln, 0, sight, z, mul(OB, 0.85))
        shell_cyl(r * 1.25, ln * 0.12, 0, sight, z - ln * 0.5, mul(OB, 0.8))
        shell_cyl(r * 1.45, 0.05, 0, sight, z + ln * 0.5 + 0.025, mul(OB, 0.78))
        shell(0.008, 0.03, 0.008, r * 1.4, sight + 0.018, z + ln * 0.5 + 0.01, mul(METAL, 0.75))
        shell(0.016, 0.012, 0.016, 0, sight + r * 1.35, z, mul(METAL, 0.65))
        shell(0.012, 0.016, 0.016, r * 1.35, sight, z, mul(METAL, 0.65))
        shell(0.012, 0.018, 0.018, -r * 1.35, sight, z + 0.01, w_accent(w))
        shell(r * 2.4, r * 2.4, 0.002, 0, sight, z - ln * 0.56, mul(CYAN_GLOW, 0.4), mode=2)
    elif o == "Scope12":
        ln, r = 0.36, 0.02
        for i in (-1, 0, 1):
            shell(0.022, sight - mount, 0.016, 0, (mount + sight) / 2, z + i * ln * 0.24, mul(OB, 0.8))
        shell_cyl(r, ln, 0, sight, z, mul(OB, 0.85))
        shell_cyl(r * 1.4, 0.03, 0, sight, z - ln * 0.5 - 0.015, mul(OB, 0.82))
        shell_cyl(r * 1.8, 0.09, 0, sight, z - ln * 0.5 - 0.075, mul(OB, 0.8))
        shell_cyl(r * 1.3, 0.06, 0, sight, z + ln * 0.5 + 0.03, mul(OB, 0.8))
        shell(0.026, 0.034, 0.026, 0, sight + r * 1.6, z, mul(METAL, 0.7))
        shell(0.03, 0.026, 0.026, r * 1.6, sight, z, mul(METAL, 0.7))
        shell(0.006, 0.05, 0.05, -r * 1.7, sight, z - 0.01, mul(METAL, 0.6))
        shell(r * 3.2, r * 3.2, 0.002, 0, sight, z - ln * 0.5 - 0.121, mul(CYAN_GLOW, 0.4), mode=2)
    elif o in ("Scope4", "Scope8"):
        draw_scope(shell, shell_cyl, 1.25 if o == "Scope8" else 1.0, mount, sight, z)
    elif builtin_scope(w):
        draw_scope(shell, shell_cyl, 0.85, mount, sight, z)
    b.flags = 0


def w_accent(w):
    return tuple(w.get("accent", [0.95, 0.5, 0.15]))


def draw_scope(shell, shell_cyl, k, top, sight, z):
    ln = 0.26 * k
    r = 0.02 * k
    OB = OPTIC_BODY
    shell(0.02, sight - top, 0.016, 0, (top + sight) / 2, z - ln * 0.25, mul(OB, 0.8))
    shell(0.02, sight - top, 0.016, 0, (top + sight) / 2, z + ln * 0.25, mul(OB, 0.8))
    shell_cyl(r, ln, 0, sight, z, mul(OB, 0.85))
    shell_cyl(r * 1.6, ln * 0.22, 0, sight, z - ln * 0.55, mul(OB, 0.8))
    shell_cyl(r * 1.35, ln * 0.16, 0, sight, z + ln * 0.55, mul(OB, 0.8))
    shell(0.018 * k, 0.022 * k, 0.018 * k, 0, sight + r * 1.5, z, mul(METAL, 0.7))
    shell(0.022 * k, 0.018 * k, 0.018 * k, r * 1.5, sight, z, mul(METAL, 0.7))
    shell(r * 2.2, r * 2.2, 0.002, 0, sight, z - ln * 0.66, mul(CYAN_GLOW, 0.4), mode=2)


# ---- Loading the shipped files + attachments -------------------------------------------------------

ROOT = os.environ.get("CALLOFSLOP", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def load_weapons():
    out = []
    for f in sorted(glob.glob(os.path.join(ROOT, "Weapons", "**", "*.json"), recursive=True)):
        with open(f) as fh:
            w = json.load(fh)
        w["_file"] = os.path.relpath(f, os.path.join(ROOT, "Weapons"))
        w["_p"] = resolve(w)
        out.append(w)
    out.sort(key=lambda w: (w.get("order", 1000), w["name"].lower()))
    return out


def load_attachments():
    out = []
    for f in sorted(glob.glob(os.path.join(ROOT, "Attachments", "**", "*.json"), recursive=True)):
        with open(f) as fh:
            a = json.load(fh)
        out.append(a)
    by = {}
    for a in out:
        by.setdefault(a["slot"], []).append(a)
    for s in by:
        by[s].sort(key=lambda a: (a.get("order", 1000), a["name"].lower()))
    return by


def fits(a, w):
    r = a.get("fits", {})
    if r.get("classes") and w["class"] not in r["classes"]:
        return False
    if w["class"] in r.get("notClasses", []):
        return False
    snd = w.get("sound", "Gun")
    if r.get("sounds") and snd not in r["sounds"]:
        return False
    if snd in r.get("notSounds", []):
        return False
    if w["mag"] < r.get("minMag", 0):
        return False
    if r.get("maxMag", 0) and w["mag"] > r["maxMag"]:
        return False
    if r.get("weapons") and w["name"] not in r["weapons"]:
        return False
    if w["name"] in r.get("notWeapons", []):
        return False
    return True


def apply(w, atts):
    """A copy of w with attachments applied (only the stats the model reads)."""
    d = dict(w)
    kit = {}
    for a in atts:
        kit[a["slot"]] = a["name"]
        for e in a.get("effects", []):
            st = e["stat"]
            if "set" in e:
                d[st] = e["set"]
            elif "mul" in e and st in ("adsZoom", "length"):
                d[st] = d.get(st, 0) * e["mul"]
            elif "add" in e and st in ("length",):
                d[st] = d.get(st, 0.42) + e["add"]
    return d, kit
