"""Generates the indoor / everyday prop set: Props/Household, Props/Office, Props/Kitchen, Props/Garden and Props/Misc,
plus the Dev map "Prop Gallery 3" (Maps/Dev/PropGallery3.json) that shows all of them in rows by category.

    python3 tools/propviz/gen_indoor.py [Props folder] [Maps/Dev folder]

Uses the box helpers from gen.py (the starter library's generator). Breakable health follows the material health guide
in Props/README.md: glass, paper and cardboard very low, plastic low, upholstery and wood medium, thin metal higher, heavy
metal, stone and porcelain-on-steel left unbreakable.
"""
import json, math, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen  # noqa: E402
from gen import B, At, Cyl, HCyl, Ring, C, brk  # noqa: E402

OUT = sys.argv[1] if len(sys.argv) > 1 else "Props"
MAP_DIR = sys.argv[2] if len(sys.argv) > 2 else os.path.join("Maps", "Dev")
CATEGORIES = ["Household", "Office", "Kitchen", "Garden", "Misc"]
PROPS = gen.PROPS


def prop(*a, **kw):
    gen.prop(*a, **kw)


def legs4(w, d, h, t=0.05, color="darkwood", y0=0.0, mat=None):
    """Four square legs at the corners of a w x d footprint (outer faces flush with it)."""
    return [At([t, h, t], y0, sx * (w / 2 - t / 2), sz * (d / 2 - t / 2), color=color, mat=mat) for sx in (-1, 1) for sz in (-1, 1)]


def yawed(x, z, yaw):
    a = math.radians(yaw)
    return x * math.cos(a) + z * math.sin(a), -x * math.sin(a) + z * math.cos(a)


def books_row(x0, y0, z, length, depth=0.2, seed=1, max_h=0.26, lean_last=True):
    """A row of book spines along X starting at x0, standing on y0, centred at z."""
    random.seed(seed)
    out = []
    x = x0
    cols = ["#7a2a22", "#2a4a7a", "#3a6a3a", "#c8b070", "#5a3a6a", "#a85a2a", "#222222", "#d8d0c0", "#4a7a8a"]
    while x < x0 + length - 0.05:
        t = random.uniform(0.025, 0.05)
        h = random.uniform(max_h * 0.7, max_h)
        out.append(At([t, h, depth * random.uniform(0.8, 1.0)], y0, x + t / 2, z, color=random.choice(cols)))
        x += t + 0.002
    return out


# Material sounds: "glass" (glass, porcelain, ceramic, terracotta), "soft" (paper, cardboard, plastic, fabric),
# "wood", "metal". Debris colours default to the parts' colours.

# =============================== Household ===============================

def bed(name, w, desc, health, frame="wood", head_col="darkwood", sheet="#c9d4e0", duvet="#5a7ab0"):
    L = 2.05
    fc = head_col
    parts = [At([w, 0.22, L], 0.12, 0, 0, color=fc, mat="wood" if frame == "wood" else "metal"),
             At([w - 0.06, 0.2, L - 0.1], 0.32, 0, 0.02, color=sheet),  # mattress
             At([w - 0.02, 0.06, L * 0.62], 0.5, 0, 0.36, color=duvet),  # duvet
             B([w - 0.02, 0.18, 0.06], [0, 0.44, 0.36 + L * 0.31], color=duvet),  # duvet hanging over the foot
             At([w * 0.4 if w > 1.2 else w * 0.7, 0.1, 0.36], 0.52, -w * 0.22 if w > 1.2 else 0, -L / 2 + 0.3, color="white"),
             At([0.04, 0.95, 0.06], 0.0, -w / 2 + 0.02, -L / 2 - 0.03, color=fc), At([0.04, 0.95, 0.06], 0.0, w / 2 - 0.02, -L / 2 - 0.03, color=fc),
             At([w, 0.45, 0.04], 0.45, 0, -L / 2 - 0.03, color=fc, mat="wood" if frame == "wood" else None),  # headboard
             At([w, 0.25, 0.04], 0.15, 0, L / 2 + 0.02, color=fc)]  # footboard
    if w > 1.2:
        parts.append(At([w * 0.4, 0.1, 0.36], 0.52, w * 0.22, -L / 2 + 0.3, color="white"))
    parts += legs4(w, L + 0.06, 0.12, 0.06, color=fc)
    prop(name, "Household", desc, parts, collider=[C([w, 0.58, L + 0.08], [0, 0.29, 0]), C([w, 0.95, 0.1], [0, 0.475, -L / 2 - 0.03])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4}, breakable=brk(health, "wood", count=16, size=(0.08, 0.4), speed=3))


def bunk_bed():
    w, L = 1.0, 2.0
    parts = []
    for sx in (-1, 1):
        for sz in (-1, 1):
            parts.append(At([0.05, 1.7, 0.05], 0, sx * (w / 2 - 0.025), sz * (L / 2 - 0.025), color="#3a4a5a", mat="metal"))
    for y in (0.25, 1.15):
        parts += [At([w, 0.06, L], y, color="#3a4a5a"), At([w - 0.06, 0.16, L - 0.06], y + 0.06, color="#c9d4e0"),
                  At([w - 0.04, 0.05, 1.2], y + 0.22, 0, 0.35, color="#8a3a3a" if y < 1 else "#3a6a5a"), At([0.6, 0.08, 0.3], y + 0.22, 0, -0.75, color="white")]
    for y in (0.6, 1.5):
        parts.append(At([0.03, 0.03, L], y, w / 2 - 0.02, 0, color="#3a4a5a"))  # side rails
    parts += [At([w, 0.04, 0.03], 1.55, 0, -L / 2 + 0.02, color="#3a4a5a"), At([w, 0.04, 0.03], 1.55, 0, L / 2 - 0.02, color="#3a4a5a")]
    for k in range(4):  # ladder at the foot end
        parts.append(At([0.03, 0.03, 0.36], 0.45 + k * 0.28, -w / 2 - 0.03, L / 2 - 0.25, color="#3a4a5a"))
    parts += [At([0.03, 1.6, 0.03], 0, -w / 2 - 0.03, L / 2 - 0.43, color="#3a4a5a"), At([0.03, 1.6, 0.03], 0, -w / 2 - 0.03, L / 2 - 0.07, color="#3a4a5a")]
    prop("Bunk Bed", "Household", "A steel-framed bunk bed with a ladder at the foot.", parts, collider=[C([w + 0.08, 1.7, L], [-0.04, 0.85, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(320, "metal", count=16, size=(0.06, 0.35), speed=3))


def wardrobe():
    W, H, D = 1.2, 2.0, 0.6
    parts = [At([W, H - 0.08, D], 0.08, color="#8a6440", mat="wood"), At([W + 0.04, 0.05, D + 0.04], H, color="#6a4a2e"),
             At([W - 0.04, 0.08, D - 0.04], 0.0, color="#5a3e26")]
    for x in (-W / 4, W / 4):
        parts.append(At([W / 2 - 0.03, H - 0.2, 0.02], 0.12, x, D / 2 + 0.005, color="#9a7450", mat="wood"))
        parts.append(At([W / 2 - 0.13, H - 0.5, 0.01], 0.27, x, D / 2 + 0.018, color="#8a6440"))
    parts += [At([0.02, 0.18, 0.03], 1.0, -0.04, D / 2 + 0.03, color="chrome"), At([0.02, 0.18, 0.03], 1.0, 0.04, D / 2 + 0.03, color="chrome")]
    prop("Wardrobe", "Household", "A tall two-door wooden wardrobe. Full-height cover.", parts, collider=[C([W + 0.04, H + 0.05, D + 0.06], [0, (H + 0.05) / 2, 0.01])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(180, "wood", count=16, size=(0.08, 0.5), speed=3.5))


def chest_of_drawers():
    W, H, D = 0.9, 0.95, 0.48
    parts = [At([W, H - 0.06, D], 0.06, color="#b08a5a", mat="wood"), At([W + 0.03, 0.03, D + 0.03], H, color="#8a6440"),
             At([W - 0.04, 0.06, D - 0.04], 0, color="#6a4a2e")]
    for i in range(4):
        y = 0.1 + i * 0.21
        parts += [At([W - 0.06, 0.18, 0.02], y, 0, D / 2 + 0.005, color="#c09a6a", mat="wood"),
                  At([0.04, 0.03, 0.03], y + 0.08, -0.2, D / 2 + 0.025, color="#444444"), At([0.04, 0.03, 0.03], y + 0.08, 0.2, D / 2 + 0.025, color="#444444")]
    parts += [At([0.18, 0.2, 0.12], H + 0.03, 0.25, -0.05, color="#e0d8c8"), At([0.3, 0.04, 0.2], H + 0.03, -0.2, 0.0, color="#4a6a8a", yaw=10)]
    prop("Chest of Drawers", "Household", "A four-drawer pine dresser with a few things on top.", parts, collider=[C([W + 0.03, H + 0.03, D + 0.05])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 5}, breakable=brk(110, "wood", count=14, size=(0.06, 0.35), speed=3.5))


def bookshelf():
    W, H, D = 0.9, 1.9, 0.32
    c = "#6a4a2e"
    parts = [At([0.03, H, D], 0, -W / 2 + 0.015, color=c, mat="wood"), At([0.03, H, D], 0, W / 2 - 0.015, color=c, mat="wood"),
             At([W, H, 0.015], 0, 0, -D / 2 + 0.008, color="#5a3e26"), At([W, 0.03, D], H - 0.03, color=c)]
    for i, y in enumerate((0.0, 0.08, 0.5, 0.94, 1.38)):
        parts.append(At([W - 0.06, 0.025 if y > 0 else 0.08, D - 0.02], y, 0, 0.005, color=c))
        if y > 0:
            parts += books_row(-W / 2 + 0.04, y + 0.025, 0.0, W - 0.08 - (0.2 if i % 2 else 0.0), depth=0.22, seed=i * 3 + 1, max_h=0.32)
    parts.append(B([0.04, 0.26, 0.22], [W / 2 - 0.15, 0.08 + 0.025 + 0.12, 0.0], color="#7a2a22", roll=22))
    prop("Bookshelf", "Household", "A tall wooden bookshelf, full of books.", parts, collider=[C([W, H, D])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4},
         breakable=brk(110, "wood", count=18, size=(0.05, 0.3), speed=3.5, colors=["#6a4a2e", "#7a2a22", "#2a4a7a", "#c8b070", "#3a6a3a"]))


def bedside_table():
    parts = [At([0.45, 0.5, 0.4], 0.05, color="#a07a50", mat="wood"), At([0.47, 0.03, 0.42], 0.55, color="#8a6440"),
             At([0.41, 0.18, 0.02], 0.3, 0, 0.205, color="#b08a5a"), At([0.06, 0.03, 0.03], 0.38, 0, 0.225, color="chrome")]
    parts += legs4(0.43, 0.38, 0.05, 0.04, color="#6a4a2e")
    # a small lamp and a book on top
    parts += Cyl(0.12, 0.02, 0.58, -0.1, -0.05, color="#444444") + [At([0.02, 0.2, 0.02], 0.6, -0.1, -0.05, color="#444444")] + Cyl(0.2, 0.15, 0.78, -0.1, -0.05, color="#e8dcc0")
    parts.append(At([0.15, 0.03, 0.2], 0.58, 0.12, 0.05, color="#2a4a7a", yaw=-12))
    prop("Bedside Table", "Household", "A small bedside cabinet with a lamp and a book.", parts, collider=[C([0.47, 0.58, 0.42])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8}, breakable=brk(45, "wood", count=10, size=(0.04, 0.2), speed=3))


def fabric_sofa():
    col, dark = "#4a5a78", "#3a4862"
    parts = [At([2.0, 0.22, 0.9], 0.1, color=dark), At([1.66, 0.14, 0.66], 0.32, 0, 0.1, color=col)]
    for x in (-0.55, 0.0, 0.55):
        parts.append(At([0.54, 0.05, 0.62], 0.46, x, 0.1, color=col))
        parts.append(B([0.52, 0.42, 0.14], [x, 0.7, -0.24], color=col, pitch=-10))
    parts += [B([2.0, 0.6, 0.18], [0, 0.6, -0.36], color=dark, pitch=-6),
              At([0.17, 0.5, 0.9], 0.1, -0.915, 0, color=dark), At([0.17, 0.5, 0.9], 0.1, 0.915, 0, color=dark),
              At([0.19, 0.04, 0.9], 0.6, -0.915, 0, color=col), At([0.19, 0.04, 0.9], 0.6, 0.915, 0, color=col),
              B([0.38, 0.36, 0.12], [-0.7, 0.66, -0.14], color="#c8a050", pitch=-20, yaw=12)]
    for x in (-0.9, 0.9):
        for z in (-0.38, 0.38):
            parts.append(At([0.06, 0.1, 0.06], 0, x, z, color="darkwood"))
    prop("Fabric Sofa", "Household", "A blue three-seater sofa with a cushion.", parts,
         collider=[C([2.0, 0.6, 0.9], [0, 0.3, 0]), C([2.0, 0.92, 0.24], [0, 0.46, -0.34])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 6}, breakable=brk(130, "soft", count=16, size=(0.08, 0.3), speed=3))


def armchair():
    col, dark = "#8a4a3a", "#6a3a2e"
    parts = [At([0.9, 0.24, 0.86], 0.1, color=dark), At([0.58, 0.14, 0.62], 0.34, 0, 0.08, color=col),
             B([0.6, 0.48, 0.16], [0, 0.72, -0.28], color=col, pitch=-10), B([0.9, 0.62, 0.16], [0, 0.62, -0.36], color=dark, pitch=-6),
             At([0.16, 0.48, 0.86], 0.1, -0.37, 0, color=dark), At([0.16, 0.48, 0.86], 0.1, 0.37, 0, color=dark),
             At([0.18, 0.04, 0.86], 0.58, -0.37, 0, color=col), At([0.18, 0.04, 0.86], 0.58, 0.37, 0, color=col)]
    for x in (-0.4, 0.4):
        for z in (-0.36, 0.36):
            parts.append(At([0.06, 0.1, 0.06], 0, x, z, color="darkwood"))
    prop("Armchair", "Household", "A deep red upholstered armchair.", parts, collider=[C([0.9, 0.62, 0.86], [0, 0.31, 0]), C([0.9, 0.94, 0.22], [0, 0.47, -0.34])],
         scatter={"randomYaw": True}, breakable=brk(90, "soft", count=12, size=(0.06, 0.25), speed=3))


def coffee_table():
    parts = [At([1.1, 0.04, 0.6], 0.4, color="#7a5232", mat="wood"), At([1.0, 0.02, 0.5], 0.12, color="#6a4428")]
    parts += legs4(1.04, 0.54, 0.4, 0.05, color="#5a3a22")
    parts += [At([0.28, 0.02, 0.2], 0.44, -0.25, 0.05, color="#c03030", yaw=10), At([0.26, 0.02, 0.19], 0.46, -0.24, 0.04, color="#e0e0d0", yaw=-5)]
    parts += Cyl(0.09, 0.1, 0.44, 0.3, -0.1, color="white") + [At([0.03, 0.05, 0.015], 0.47, 0.36, -0.1, color="white")]
    parts += [At([0.3, 0.06, 0.2], 0.14, 0.15, 0.0, color="#2a4a7a"), At([0.28, 0.05, 0.19], 0.2, 0.15, 0.0, color="#c8b070", yaw=6)]
    prop("Coffee Table", "Household", "A low wooden coffee table with magazines and a mug.", parts, collider=[C([1.1, 0.44, 0.6])],
         scatter={"randomYaw": True}, breakable=brk(60, "wood", count=10, size=(0.05, 0.3), speed=3))


def tv_stand():
    parts = [At([1.4, 0.45, 0.42], 0.05, color="#2a2a2c", mat="wood"), At([1.42, 0.03, 0.44], 0.5, color="#1c1c1e")]
    parts += [At([0.42, 0.32, 0.01], 0.1, x, 0.212, color="#3a3a3e") for x in (-0.46, 0.46)]
    parts += [At([0.4, 0.08, 0.3], 0.12, 0, 0.02, color="#111111"), At([0.04, 0.01, 0.01], 0.17, 0.12, 0.172, color="#30c030"),
              At([0.36, 0.06, 0.26], 0.25, 0, 0.02, color="#e8e8e8")]
    parts += legs4(1.36, 0.38, 0.05, 0.05, color="#111111")
    # flat-screen TV on a foot
    parts += [At([0.3, 0.02, 0.2], 0.53, color="#111111"), At([0.06, 0.12, 0.04], 0.55, 0, -0.02, color="#111111"),
              At([1.25, 0.72, 0.05], 0.62, 0, -0.02, color="#141416"), At([1.19, 0.66, 0.005], 0.65, 0, 0.008, color="#1e2a3a")]
    prop("TV Stand", "Household", "A low media unit with a flat-screen TV and a games console.", parts, collider=[C([1.42, 1.34, 0.44])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4},
         breakable=brk(80, "wood", count=14, size=(0.04, 0.3), speed=3.5, colors=["#2a2a2c", "#141416", "#1e2a3a", "#e8e8e8"]))


def crt_tv():
    parts = [At([0.62, 0.5, 0.5], 0.0, 0, -0.02, color="#3a3a38"), At([0.5, 0.38, 0.03], 0.07, -0.03, 0.235, color="#2a3830"),
             At([0.62, 0.5, 0.03], 0.0, 0, 0.225, color="#4a4a46"), At([0.42, 0.3, 0.3], 0.1, 0, -0.38, color="#3a3a38"),
             At([0.06, 0.32, 0.02], 0.08, 0.26, 0.245, color="#2e2e2c"), At([0.03, 0.03, 0.01], 0.35, 0.26, 0.255, color="#a0a0a0"),
             At([0.03, 0.03, 0.01], 0.3, 0.26, 0.255, color="#a0a0a0"),
             B([0.01, 0.4, 0.01], [-0.12, 0.66, -0.15], color="chrome", roll=25), B([0.01, 0.4, 0.01], [0.12, 0.66, -0.15], color="chrome", roll=-25),
             At([0.08, 0.04, 0.08], 0.5, 0, -0.15, color="#222222")]
    prop("CRT Television", "Household", "A chunky old tube TV with rabbit-ear aerials.", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(12, "glass", count=12, size=(0.03, 0.12), speed=3.5, colors=["#3a3a38", "#2a3830", "#9ab0a8"]))


def floor_lamp():
    parts = Cyl(0.3, 0.03, 0.0, color="#2a2a2a") + [At([0.03, 1.45, 0.03], 0.03, color="#b89a50")]
    parts += Cyl(0.42, 0.28, 1.4, color="#efe2c0") + Cyl(0.36, 0.01, 1.68, color="#fff4d8") + Cyl(0.08, 0.06, 1.36, color="#b89a50")
    prop("Floor Lamp", "Household", "A standing lamp with a cream shade.", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(4, "glass", count=8, size=(0.03, 0.1), speed=3, colors=["#efe2c0", "#b89a50", "white"]))


def table_lamp():
    parts = Cyl(0.16, 0.18, 0.0, color="#3a6a8a") + Cyl(0.1, 0.06, 0.18, color="#3a6a8a") + [At([0.02, 0.1, 0.02], 0.24, color="#b89a50")]
    parts += Cyl(0.3, 0.2, 0.3, color="#f0e8d0") + Cyl(0.24, 0.005, 0.5, color="#fff8e0")
    prop("Table Lamp", "Household", "A ceramic table lamp with a shade (put it on a table or the floor).", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(2, "glass", count=6, size=(0.02, 0.08), speed=2.5))


def rug(name, w, d, base, border, desc, pattern):
    parts = [At([w, 0.008, d], 0, color=border), At([w - 0.12, 0.003, d - 0.12], 0.008, color=base)]
    if pattern == "diamond":
        parts += [At([w * 0.3, 0.002, w * 0.3], 0.011, color=border, yaw=45), At([w * 0.16, 0.002, w * 0.16], 0.0125, color="#d8c8a0", yaw=45)]
    else:
        for k in range(5):
            parts.append(At([w - 0.12, 0.002, 0.06], 0.011, 0, -d / 2 + 0.25 + k * (d - 0.5) / 4, color=border))
    parts += [At([0.02, 0.004, d], 0.0, -w / 2 - 0.02, 0, color="#e0d8c0"), At([0.02, 0.004, d], 0.0, w / 2 + 0.02, 0, color="#e0d8c0")]  # tassels
    prop(name, "Household", desc, parts, collision="cosmetic", scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10}, fade=60)


def dining_table():
    parts = [At([1.8, 0.05, 0.9], 0.72, color="#8a5a32", mat="wood"), At([1.7, 0.08, 0.8], 0.64, color="#6a4428")]
    parts += legs4(1.66, 0.76, 0.64, 0.07, color="#6a4428")
    parts += Cyl(0.26, 0.015, 0.77, -0.5, 0.2, color="white") + Cyl(0.26, 0.015, 0.77, 0.5, -0.2, color="white")
    parts += Cyl(0.1, 0.22, 0.77, 0.0, 0.0, color="#3a6a8a") + [At([0.08, 0.12, 0.08], 0.99, 0.0, 0.0, color="#d04040")]
    prop("Dining Table", "Household", "A long wooden dining table, set for two.", parts,
         collider=[C([1.8, 0.13, 0.9], [0, 0.705, 0])] + [C([0.07, 0.64, 0.07], [x, 0.32, z]) for x in (-0.795, 0.795) for z in (-0.365, 0.365)],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 6}, breakable=brk(90, "wood", count=14, size=(0.05, 0.4), speed=3.5))


def dining_chair():
    c = "#6a4428"
    parts = [At([0.44, 0.05, 0.44], 0.44, color=c, mat="wood"), At([0.4, 0.04, 0.38], 0.49, 0, 0.02, color="#8a2a2a")]
    parts += legs4(0.42, 0.42, 0.44, 0.04, color=c)
    parts += [At([0.04, 0.5, 0.04], 0.49, -0.19, -0.19, color=c), At([0.04, 0.5, 0.04], 0.49, 0.19, -0.19, color=c),
              At([0.42, 0.12, 0.03], 0.86, 0, -0.195, color=c, mat="wood"), At([0.04, 0.3, 0.02], 0.55, -0.07, -0.195, color=c),
              At([0.04, 0.3, 0.02], 0.55, 0.07, -0.195, color=c)]
    prop("Dining Chair", "Household", "A wooden dining chair with a red seat pad.", parts, collider=[C([0.44, 0.53, 0.44]), C([0.44, 0.99, 0.06], [0, 0.495, -0.19])],
         scatter={"randomYaw": True}, breakable=brk(35, "wood", count=8, size=(0.04, 0.22), speed=3.5))


def washing_machine(name, desc, dryer=False):
    parts = [At([0.6, 0.85, 0.6], 0, color="white", mat="metal"), At([0.6, 0.12, 0.02], 0.73, 0, 0.3, color="#e0e0e0"),
             At([0.18, 0.06, 0.01], 0.76, -0.16, 0.312, color="#c0c8d0"), At([0.06, 0.06, 0.02], 0.76, 0.16, 0.315, color="#888888")]
    parts += [At([0.4, 0.36, 0.01], 0.2, 0, 0.302, color="#a0a8b0", yaw=a) for a in (0,)]
    parts += [B([0.38, 0.16, 0.02], [0, 0.38, 0.305], color="#c0c4c8", roll=a) for a in (0, 45, 90, 135)]
    parts += [B([0.3, 0.12, 0.02], [0, 0.38, 0.312], color="#1e2a3a" if not dryer else "#3a3a3a", roll=a) for a in (0, 45, 90, 135)]
    parts += [At([0.56, 0.05, 0.02], 0.0, 0, 0.305, color="#cccccc")]
    prop(name, "Household", desc, parts, collider=[C([0.6, 0.85, 0.62])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4},
         breakable=brk(220, "metal", count=14, size=(0.06, 0.25), speed=3.5))


def ironing_board():
    parts = [B([1.2, 0.03, 0.36], [0, 0.85, 0], color="#c0d0e0"), B([0.25, 0.031, 0.25], [0.6, 0.85, 0], color="#c0d0e0", yaw=45),
             B([0.04, 0.95, 0.03], [-0.1, 0.43, 0], color="#888888", roll=30), B([0.04, 0.95, 0.03], [-0.1, 0.43, 0], color="#888888", roll=-30),
             At([0.03, 0.03, 0.34], 0.0, -0.33, 0, color="#666666"), At([0.03, 0.03, 0.34], 0.0, 0.13, 0, color="#666666"),
             At([0.26, 0.12, 0.12], 0.865, -0.35, 0, color="#3a7ab0"), At([0.08, 0.05, 0.05], 0.985, -0.42, 0, color="#3a7ab0"),
             At([0.3, 0.004, 0.3], 0.865, 0.2, 0.03, color="white", yaw=8)]
    prop("Ironing Board", "Household", "An ironing board with an iron and a shirt.", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(45, "metal", count=8, size=(0.04, 0.3), speed=3))


def toilet():
    w = "white"
    parts = [At([0.26, 0.3, 0.32], 0, 0, 0.0, color=w), At([0.38, 0.12, 0.5], 0.28, 0, 0.04, color=w), At([0.3, 0.01, 0.4], 0.4, 0, 0.06, color="#a8c0d0"),
             At([0.4, 0.03, 0.52], 0.4, 0, 0.04, color="#f0f0ec"),
             At([0.42, 0.38, 0.18], 0.38, 0, -0.26, color=w), At([0.44, 0.04, 0.2], 0.76, 0, -0.26, color="#f0f0ec"),
             At([0.06, 0.02, 0.03], 0.8, 0.12, -0.26, color="chrome"), B([0.4, 0.42, 0.03], [0, 0.62, -0.17], color="#f0f0ec", pitch=-8)]
    prop("Toilet", "Household", "A white porcelain toilet with the seat up.", parts, collider=[C([0.44, 0.8, 0.72], [0, 0.4, -0.04])],
         scatter={"randomYaw": False, "yawStep": 90}, breakable=brk(45, "glass", count=12, size=(0.04, 0.15), speed=3.5))


def bath_sink():
    parts = Cyl(0.18, 0.72, 0.0, color="white") + [At([0.56, 0.16, 0.44], 0.72, color="white"), At([0.42, 0.02, 0.28], 0.87, 0, 0.03, color="#a8c0d0"),
                                                    At([0.58, 0.02, 0.46], 0.88, color="#f2f2ee")]
    parts += [At([0.03, 0.12, 0.03], 0.89, 0, -0.17, color="chrome"), At([0.03, 0.03, 0.1], 0.98, 0, -0.13, color="chrome"),
              At([0.05, 0.04, 0.04], 0.9, -0.1, -0.17, color="chrome"), At([0.05, 0.04, 0.04], 0.9, 0.1, -0.17, color="chrome"),
              At([0.08, 0.04, 0.05], 0.9, 0.2, -0.12, color="#e0a0c0")]
    prop("Bathroom Sink", "Household", "A pedestal washbasin with a tap and a bar of soap.", parts, collider=[C([0.58, 0.9, 0.46])],
         scatter={"randomYaw": False, "yawStep": 90}, breakable=brk(35, "glass", count=10, size=(0.04, 0.14), speed=3.5))


def bathtub():
    L, W, H = 1.7, 0.75, 0.58
    parts = [At([L, 0.08, W], 0.06, color="white"), At([L, H - 0.14, 0.06], 0.14, 0, W / 2 - 0.03, color="white"),
             At([L, H - 0.14, 0.06], 0.14, 0, -W / 2 + 0.03, color="white"), At([0.06, H - 0.14, W], 0.14, L / 2 - 0.03, color="white"),
             At([0.06, H - 0.14, W], 0.14, -L / 2 + 0.03, color="white"),
             At([L + 0.04, 0.04, 0.08], H, 0, W / 2 - 0.02, color="#f2f2ee"), At([L + 0.04, 0.04, 0.08], H, 0, -W / 2 + 0.02, color="#f2f2ee"),
             At([0.08, 0.04, W - 0.12], H, L / 2 - 0.02, 0, color="#f2f2ee"), At([0.08, 0.04, W - 0.12], H, -L / 2 + 0.02, 0, color="#f2f2ee"),
             At([L - 0.12, 0.3, W - 0.12], 0.14, color="#9ac0d8"),
             At([0.04, 0.16, 0.04], H + 0.04, -L / 2 + 0.06, 0, color="chrome"), At([0.12, 0.03, 0.04], H + 0.17, -L / 2 + 0.1, 0, color="chrome")]
    for x in (-L / 2 + 0.12, L / 2 - 0.12):
        for z in (-W / 2 + 0.1, W / 2 - 0.1):
            parts.append(At([0.08, 0.08, 0.08], 0, x, z, color="#b89a50"))
    parts += [At([0.12, 0.02, 0.1], 0.44, 0.2, 0.05, color="#f0d040"), At([0.06, 0.06, 0.06], 0.46, 0.18, 0.05, color="#f0d040")]  # a little floating duck
    prop("Bathtub", "Household", "A cast-iron clawfoot bath (with a rubber duck in it). Heavy cover, doesn't break.", parts,
         collider=[C([L + 0.04, H + 0.04, W + 0.04])], scatter={"randomYaw": False, "yawStep": 90})


def shower():
    S, H = 0.9, 2.0
    parts = [At([S, 0.1, S], 0, color="white"), At([S - 0.1, 0.01, S - 0.1], 0.1, color="#c0c8d0"),
             At([S, H, 0.03], 0.1, 0, -S / 2 + 0.015, color="#d8dcd8", mat="tiles"), At([0.03, H, S], 0.1, -S / 2 + 0.015, 0, color="#d8dcd8", mat="tiles"),
             At([0.012, H - 0.1, S - 0.04], 0.12, S / 2 - 0.01, 0, color="#a8c8c8"), At([S - 0.04, H - 0.1, 0.012], 0.12, 0, S / 2 - 0.01, color="#a8c8c8"),
             At([0.03, H - 0.1, 0.03], 0.12, S / 2 - 0.015, S / 2 - 0.015, color="chrome"), At([0.03, 0.03, S], H + 0.05, S / 2 - 0.015, 0, color="chrome"),
             At([S, 0.03, 0.03], H + 0.05, 0, S / 2 - 0.015, color="chrome"),
             At([0.02, 0.5, 0.02], 1.4, -0.3, -S / 2 + 0.05, color="chrome"), At([0.14, 0.03, 0.14], 1.9, -0.3, -S / 2 + 0.11, color="chrome")]
    prop("Shower Cubicle", "Household", "A corner shower with glass doors.", parts, collider=[C([S, H + 0.1, S])],
         scatter={"randomYaw": False, "yawStep": 90}, breakable=brk(15, "glass", count=20, size=(0.04, 0.2), speed=4, colors=["#a8c8c8", "#d8dcd8", "white"]))


def laundry_basket():
    parts = [At([0.5, 0.36, 0.36], 0, color="#d8d8d0")]
    for k in range(4):
        parts.append(At([0.51, 0.02, 0.37], 0.06 + k * 0.08, color="#b8b8b0"))
    parts += [At([0.46, 0.04, 0.32], 0.33, color="#d8d0c0"), At([0.16, 0.06, 0.14], 0.35, -0.12, 0.0, color="#3a5a9a", yaw=20),
              At([0.18, 0.05, 0.12], 0.36, 0.1, 0.06, color="#c03030", yaw=-15), At([0.14, 0.05, 0.12], 0.37, 0.05, -0.08, color="#3a8a4a", yaw=35),
              B([0.3, 0.02, 0.12], [0.18, 0.3, 0.2], color="#e8e0d0", pitch=-50)]
    prop("Laundry Basket", "Household", "A plastic basket overflowing with clothes.", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(10, "soft", count=10, size=(0.04, 0.12), speed=2.5))


def radiator():
    parts = []
    for k in range(10):
        parts.append(At([0.06, 0.6, 0.08], 0.12, -0.36 + k * 0.08, 0, color="#e8e8e4", mat="metal"))
    parts += [At([0.84, 0.04, 0.06], 0.66, color="#e0e0dc"), At([0.84, 0.04, 0.06], 0.14, color="#e0e0dc"),
              At([0.04, 0.12, 0.04], 0.0, -0.36, 0.0, color="#d0d0cc"), At([0.04, 0.12, 0.04], 0.0, 0.36, 0.0, color="#d0d0cc"),
              At([0.04, 0.06, 0.06], 0.2, 0.45, 0, color="chrome")]
    prop("Radiator", "Household", "A white cast-iron radiator. Put it against a wall.", parts, collision="cosmetic",
         scatter={"randomYaw": False, "yawStep": 90})


def coat_stand():
    parts = [At([0.04, 1.75, 0.04], 0.04, color="#5a3a22", mat="wood")]
    for a in (0, 90, 180, 270):
        x, z = yawed(0.2, 0, a)
        parts.append(B([0.42, 0.04, 0.05], [x * 0.5, 0.02, z * 0.5], color="#5a3a22", yaw=a))
        hx, hz = yawed(0.1, 0, a + 45)
        parts.append(B([0.16, 0.025, 0.025], [hx, 1.68, hz], color="#5a3a22", yaw=a + 45, roll=-30))
    parts += [At([0.26, 0.6, 0.14], 1.05, 0.12, 0.0, color="#3a3a4a"), At([0.2, 0.14, 0.2], 1.62, -0.1, 0.0, color="#7a5a3a"),
              At([0.24, 0.015, 0.24], 1.62, -0.1, 0.0, color="#7a5a3a")]
    prop("Coat Stand", "Household", "A wooden coat stand with a coat and a hat.", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(25, "wood", count=8, size=(0.04, 0.3), speed=3))


def standing_mirror():
    parts = [At([0.5, 1.5, 0.04], 0.1, 0, 0, color="#6a4428"), At([0.42, 1.4, 0.01], 0.15, 0, 0.022, color="#b8c8d0"),
             At([0.3, 0.6, 0.004], 0.6, 0.05, 0.028, color="#d0dce4", yaw=0),
             B([0.04, 1.5, 0.04], [0, 0.72, -0.25], color="#6a4428", pitch=-20),
             At([0.5, 0.04, 0.12], 0.0, 0, 0, color="#6a4428")]
    prop("Standing Mirror", "Household", "A full-length mirror on a stand.", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(3, "glass", count=14, size=(0.03, 0.12), speed=3, colors=["#b8c8d0", "#d0dce4", "#6a4428"]))


def piano():
    W, H, D = 1.5, 1.25, 0.6
    c = "#1c1410"
    parts = [At([W, H, D * 0.6], 0, 0, -D * 0.2, color=c, mat="wood"), At([W, 0.04, D * 0.62], H, 0, -D * 0.2, color="#2a1e18"),
             At([0.06, 0.75, D], 0, -W / 2 + 0.03, 0, color=c), At([0.06, 0.75, D], 0, W / 2 - 0.03, 0, color=c),
             At([W - 0.12, 0.08, 0.2], 0.68, 0, 0.18, color="white"), At([W - 0.12, 0.06, 0.04], 0.74, 0, 0.27, color=c)]
    for k in range(18):
        parts.append(At([0.025, 0.02, 0.1], 0.76, -W / 2 + 0.12 + k * 0.071, 0.13, color="black"))
    parts += [At([W - 0.1, 0.04, 0.04], 0.04, 0, 0.27, color=c), At([0.04, 0.68, 0.04], 0.0, -W / 2 + 0.08, 0.28, color=c),
              At([0.04, 0.68, 0.04], 0.0, W / 2 - 0.08, 0.28, color=c), At([0.3, 0.22, 0.01], 0.9, 0, 0.0, color="#e8e0c8"),
              At([0.1, 0.02, 0.03], 0.06, -0.1, 0.12, color="#b89a50"), At([0.1, 0.02, 0.03], 0.06, 0.1, 0.12, color="#b89a50")]
    prop("Upright Piano", "Household", "A black upright piano with sheet music.", parts, collider=[C([W, H + 0.04, D], [0, (H + 0.04) / 2, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(250, "wood", count=20, size=(0.06, 0.4), speed=3.5))


def fireplace():
    parts = [At([1.5, 0.12, 0.5], 0, color="#7a7670", mat="concrete"), At([0.3, 1.0, 0.36], 0.12, -0.6, -0.07, color="#9a4a34", mat="brick"),
             At([0.3, 1.0, 0.36], 0.12, 0.6, -0.07, color="#9a4a34", mat="brick"), At([1.5, 0.3, 0.36], 1.12, 0, -0.07, color="#9a4a34", mat="brick"),
             At([0.9, 1.0, 0.1], 0.12, 0, -0.2, color="#2a2420"), At([1.66, 0.06, 0.46], 1.42, 0, -0.05, color="#5a3a22", mat="wood"),
             B([0.5, 0.1, 0.12], [0, 0.2, 0.0], color="#5a3a22", yaw=15), B([0.5, 0.1, 0.12], [0, 0.26, 0.0], color="#4a3020", yaw=-20),
             At([0.3, 0.16, 0.12], 0.26, 0, 0.0, color="#e07020"), At([0.16, 0.14, 0.08], 0.36, 0.02, 0.0, color="#f0c040"),
             At([0.14, 0.2, 0.06], 1.48, -0.5, -0.05, color="#b89a50"), At([0.3, 0.24, 0.03], 1.48, 0.3, -0.18, color="#6a7a5a")]
    prop("Fireplace", "Household", "A brick fireplace with a wooden mantel and a fire going. Doesn't break.", parts,
         collider=[C([1.5, 1.42, 0.5], [0, 0.71, 0]), C([1.66, 0.06, 0.46], [0, 1.45, -0.05])], scatter={"randomYaw": False, "yawStep": 90})


def vacuum():
    parts = [At([0.36, 0.22, 0.26], 0.04, color="#c03040"), At([0.3, 0.06, 0.22], 0.26, color="#a02030"), At([0.12, 0.04, 0.16], 0.32, 0, -0.02, color="#222222")]
    parts += Cyl(0.08, 0.06, 0.0, -0.12, 0.09, color="black") + Cyl(0.08, 0.06, 0.0, 0.12, 0.09, color="black")
    parts += [B([0.04, 0.6, 0.04], [0.32, 0.16, 0.0], color="#888888", roll=-80), B([0.3, 0.05, 0.08], [0.62, 0.025, 0.0], color="#333333"),
              B([0.03, 0.4, 0.03], [0.16, 0.28, 0.12], color="#222222", roll=-70)]
    prop("Vacuum Cleaner", "Household", "A red cylinder vacuum cleaner with its hose out.", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(20, "soft", count=8, size=(0.03, 0.12), speed=3))


def fish_tank():
    parts = [At([1.0, 0.75, 0.42], 0, color="#2a2a2c", mat="wood"), At([0.46, 0.6, 0.01], 0.08, -0.24, 0.21, color="#3a3a3c"),
             At([0.46, 0.6, 0.01], 0.08, 0.24, 0.21, color="#3a3a3c"),
             At([1.0, 0.04, 0.42], 0.75, color="#111111"), At([0.96, 0.46, 0.38], 0.79, color="#5aa0c0"), At([1.0, 0.03, 0.42], 1.25, color="#111111"),
             At([0.9, 0.04, 0.34], 0.79, color="#d8c8a0"), At([0.04, 0.2, 0.04], 0.83, -0.3, -0.08, color="#2a7a3a"), At([0.04, 0.26, 0.04], 0.83, -0.25, -0.1, color="#3a8a3a"),
             At([0.08, 0.04, 0.02], 1.0, 0.1, 0.05, color="#f08020"), At([0.06, 0.03, 0.02], 0.92, 0.25, -0.05, color="#f0d040"),
             At([0.1, 0.08, 0.08], 0.83, 0.3, -0.05, color="#7a7a7a")]
    prop("Fish Tank", "Household", "An aquarium on a cabinet, with fish.", parts, collider=[C([1.0, 1.28, 0.42])],
         scatter={"randomYaw": False, "yawStep": 90}, breakable=brk(8, "glass", count=20, size=(0.03, 0.15), speed=4, colors=["#5aa0c0", "#a0d0e0", "#2a2a2c", "#f08020"]))


def baby_cot():
    W, L = 0.66, 1.26
    c = "#f0ece0"
    parts = [At([W, 0.08, L], 0.3, color=c), At([W - 0.04, 0.1, L - 0.04], 0.38, color="#d8e8f0"),
             At([0.3, 0.12, 0.2], 0.48, 0, 0.2, color="#c8a070"), At([0.12, 0.1, 0.1], 0.48, 0.08, 0.38, color="#c8a070")]
    for sx in (-1, 1):
        for sz in (-1, 1):
            parts.append(At([0.05, 0.95, 0.05], 0, sx * (W / 2 - 0.025), sz * (L / 2 - 0.025), color=c))
    for z in (-L / 2 + 0.025, L / 2 - 0.025):
        parts.append(At([W, 0.05, 0.04], 0.9, 0, z, color=c))
        for k in range(5):
            parts.append(At([0.025, 0.5, 0.025], 0.4, -W / 2 + 0.13 + k * 0.1, z, color=c))
    for x in (-W / 2 + 0.025, W / 2 - 0.025):
        parts.append(At([0.04, 0.05, L], 0.9, x, 0, color=c))
        for k in range(10):
            parts.append(At([0.025, 0.5, 0.025], 0.4, x, -L / 2 + 0.12 + k * 0.113, color=c))
    prop("Baby Cot", "Household", "A white wooden cot with a teddy bear.", parts, collider=[C([W, 0.95, L])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 5}, breakable=brk(50, "wood", count=12, size=(0.03, 0.25), speed=3))


# =============================== Office ===============================

def office_desk():
    parts = [At([1.6, 0.04, 0.8], 0.72, color="#d8ccb0"), At([0.04, 0.72, 0.76], 0, -0.78, 0, color="#9a9a9e"), At([0.04, 0.72, 0.76], 0, 0.78, 0, color="#9a9a9e"),
             At([1.52, 0.4, 0.02], 0.3, 0, -0.36, color="#9a9a9e"),
             At([0.42, 0.62, 0.7], 0.0, 0.52, 0.0, color="#b8b8bc", mat="metal")]
    for i in range(3):
        parts += [At([0.4, 0.18, 0.01], 0.03 + i * 0.2, 0.52, 0.355, color="#c8c8cc"), At([0.12, 0.02, 0.02], 0.17 + i * 0.2, 0.52, 0.365, color="#555555")]
    # monitor, keyboard, mug, papers
    parts += [At([0.24, 0.02, 0.18], 0.76, -0.1, -0.2, color="#222222"), At([0.05, 0.25, 0.04], 0.78, -0.1, -0.22, color="#222222"),
              At([0.56, 0.34, 0.03], 0.92, -0.1, -0.2, color="#18181a"), At([0.52, 0.3, 0.005], 0.94, -0.1, -0.183, color="#2a4a6a"),
              At([0.44, 0.02, 0.15], 0.76, -0.1, 0.1, color="#2a2a2c"), At([0.06, 0.02, 0.1], 0.76, 0.25, 0.12, color="#2a2a2c"),
              At([0.22, 0.01, 0.3], 0.76, 0.5, 0.0, color="white", yaw=-8), At([0.21, 0.01, 0.29], 0.77, 0.52, 0.02, color="paper", yaw=5)]
    parts += Cyl(0.08, 0.1, 0.76, -0.6, 0.15, color="#c03030")
    prop("Office Desk", "Office", "An office desk with a monitor, keyboard, drawers and paperwork.", parts,
         collider=[C([1.6, 0.76, 0.8])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4},
         breakable=brk(120, "wood", count=16, size=(0.05, 0.35), speed=3.5, colors=["#d8ccb0", "#9a9a9e", "#18181a", "white"]))


def office_chair():
    parts = []
    for a in range(0, 360, 72):
        x, z = yawed(0.22, 0, a)
        parts.append(B([0.32, 0.04, 0.05], [x * 0.5, 0.08, z * 0.5], color="#222222", yaw=a))
        parts.append(At([0.06, 0.06, 0.06], 0.0, x, z, color="black"))
    parts += Cyl(0.06, 0.34, 0.1, color="#888888")
    parts += [At([0.5, 0.08, 0.48], 0.44, color="#2a2a30"), At([0.46, 0.04, 0.44], 0.52, 0, 0.01, color="#3a3a44"),
              B([0.46, 0.55, 0.07], [0, 0.88, -0.24], color="#2a2a30", pitch=-8), B([0.42, 0.48, 0.03], [0, 0.88, -0.2], color="#3a3a44", pitch=-8),
              At([0.06, 0.24, 0.05], 0.48, 0, -0.26, color="#222222"),
              At([0.05, 0.2, 0.05], 0.52, -0.25, 0.0, color="#222222"), At([0.05, 0.2, 0.05], 0.52, 0.25, 0.0, color="#222222"),
              At([0.07, 0.03, 0.26], 0.72, -0.25, 0.02, color="#222222"), At([0.07, 0.03, 0.26], 0.72, 0.25, 0.02, color="#222222")]
    prop("Office Chair", "Office", "A black swivel office chair on five castors.", parts, collider=[C([0.56, 0.6, 0.56]), C([0.5, 1.16, 0.12], [0, 0.58, -0.24])],
         scatter={"randomYaw": True}, breakable=brk(45, "soft", count=10, size=(0.04, 0.2), speed=3.5))


def monitor():
    parts = [At([0.24, 0.02, 0.18], 0.0, 0, 0.0, color="#222222"), At([0.05, 0.25, 0.04], 0.02, 0, -0.03, color="#222222"),
             At([0.56, 0.34, 0.03], 0.14, 0, -0.01, color="#18181a"), At([0.52, 0.3, 0.005], 0.16, 0, 0.007, color="#2a4a6a"),
             At([0.03, 0.01, 0.005], 0.145, 0.24, 0.007, color="#30c0f0")]
    prop("Computer Monitor", "Office", "A flat computer monitor on its stand (for desks).", parts, collision="cosmetic",
         scatter={"randomYaw": True, "yawJitter": 20}, breakable=brk(5, "glass", count=8, size=(0.02, 0.1), speed=3, colors=["#18181a", "#2a4a6a"]))


def pc_tower():
    parts = [At([0.2, 0.44, 0.45], 0.02, color="#1e1e22"), At([0.005, 0.36, 0.36], 0.06, 0.1, 0.0, color="#2a3a4a"),
             At([0.18, 0.06, 0.01], 0.36, 0, 0.226, color="#2a2a2e"), At([0.02, 0.02, 0.01], 0.4, 0.05, 0.232, color="#30f060"),
             At([0.03, 0.03, 0.01], 0.4, -0.05, 0.232, color="#888888")]
    parts += [At([0.04, 0.02, 0.06], 0.0, x, z, color="black") for x in (-0.07, 0.07) for z in (-0.18, 0.18)]
    prop("PC Tower", "Office", "A black desktop computer case with a glass side.", parts, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=brk(25, "metal", count=8, size=(0.03, 0.12), speed=3))


def laptop():
    parts = [At([0.34, 0.02, 0.24], 0.0, color="#9a9ea4"), At([0.3, 0.003, 0.11], 0.02, 0, 0.0, color="#2a2a2c"),
             At([0.08, 0.002, 0.05], 0.02, 0, 0.085, color="#7a7e84"),
             B([0.34, 0.22, 0.012], [0, 0.11, -0.13], color="#9a9ea4", pitch=-12), B([0.31, 0.19, 0.003], [0, 0.112, -0.122], color="#3a5a8a", pitch=-12)]
    prop("Laptop", "Office", "An open laptop.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(4, "soft", count=6, size=(0.02, 0.08), speed=2.5))


def photocopier():
    parts = [At([0.62, 0.6, 0.62], 0.06, color="#d8d8d0"), At([0.66, 0.44, 0.62], 0.66, color="#e4e4dc"), At([0.6, 0.06, 0.56], 1.1, color="#555555"),
             At([0.56, 0.02, 0.52], 1.1, 0, 0.0, color="#a0c0c8"), B([0.6, 0.04, 0.56], [0, 1.2, -0.12], color="#555555", pitch=-30),
             At([0.24, 0.04, 0.16], 1.06, 0.2, 0.32, color="#333333"), At([0.08, 0.05, 0.05], 1.06, 0.12, 0.38, color="#30f060"),
             At([0.3, 0.08, 0.3], 0.86, 0.45, 0.0, color="#c8c8c0"), At([0.28, 0.02, 0.28], 0.94, 0.47, 0.0, color="white")]
    for i in range(3):
        parts += [At([0.6, 0.18, 0.01], 0.08 + i * 0.19, 0, 0.31, color="#c8c8c0"), At([0.2, 0.03, 0.02], 0.2 + i * 0.19, 0, 0.32, color="#666666")]
    parts += [At([0.06, 0.06, 0.06], 0.0, x, z, color="black") for x in (-0.26, 0.26) for z in (-0.26, 0.26)]
    prop("Photocopier", "Office", "A big office photocopier with an output tray.", parts, collider=[C([0.7, 1.22, 0.64], [0.02, 0.61, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4}, breakable=brk(170, "metal", count=14, size=(0.05, 0.25), speed=3.5))


def desk_printer():
    parts = [At([0.44, 0.2, 0.36], 0.0, color="#3a3a3e"), At([0.38, 0.02, 0.2], 0.2, 0, -0.04, color="#2a2a2e"),
             B([0.3, 0.004, 0.22], [0, 0.26, -0.14], color="white", pitch=-30), At([0.3, 0.004, 0.18], 0.08, 0, 0.24, color="white"),
             At([0.34, 0.02, 0.14], 0.06, 0, 0.22, color="#2a2a2e"), At([0.04, 0.01, 0.01], 0.17, 0.16, 0.182, color="#30f060")]
    prop("Desk Printer", "Office", "An inkjet printer with paper in the tray.", parts, collision="cosmetic",
         scatter={"randomYaw": True, "yawJitter": 20}, breakable=brk(15, "soft", count=8, size=(0.03, 0.1), speed=3))


def water_cooler():
    parts = [At([0.32, 0.95, 0.32], 0, color="#e8e8e4"), At([0.3, 0.12, 0.02], 0.7, 0, 0.165, color="#555555"),
             At([0.04, 0.05, 0.04], 0.74, -0.06, 0.18, color="#3060d0"), At([0.04, 0.05, 0.04], 0.74, 0.06, 0.18, color="#d03030"),
             At([0.18, 0.02, 0.06], 0.45, 0, 0.19, color="#888888")]
    parts += Cyl(0.27, 0.36, 0.97, color="#80b0e0") + Cyl(0.2, 0.05, 1.33, color="#80b0e0") + Cyl(0.1, 0.06, 0.95, color="#5a90c8")
    parts += Cyl(0.29, 0.02, 1.1, color="#70a0d8") + Cyl(0.29, 0.02, 1.2, color="#70a0d8")
    prop("Water Cooler", "Office", "An office water cooler with a big blue bottle.", parts, collider=[C([0.34, 1.38, 0.34])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10},
         breakable=brk(30, "soft", count=12, size=(0.04, 0.14), speed=3.5, colors=["#80b0e0", "#e8e8e4", "#a0d0f0"]))


def whiteboard():
    parts = [At([1.5, 0.9, 0.03], 0.8, color="#a8acb0"), At([1.44, 0.84, 0.005], 0.83, 0, 0.017, color="#f4f4f0"),
             At([1.4, 0.03, 0.08], 0.78, 0, 0.04, color="#a8acb0"),
             At([0.5, 0.012, 0.004], 1.4, -0.3, 0.021, color="#2040c0"), At([0.4, 0.012, 0.004], 1.33, -0.32, 0.021, color="#2040c0"),
             At([0.3, 0.012, 0.004], 1.2, 0.2, 0.021, color="#c02020", yaw=0), B([0.25, 0.012, 0.004], [0.3, 1.1, 0.021], color="#20a040", roll=25),
             At([0.12, 0.02, 0.02], 0.81, 0.3, 0.05, color="#c02020"), At([0.12, 0.02, 0.02], 0.81, 0.45, 0.05, color="black")]
    for x in (-0.7, 0.7):
        parts += [At([0.04, 1.75, 0.04], 0.0, x, 0, color="#888888"), At([0.06, 0.04, 0.6], 0.0, x, 0, color="#666666")]
        parts += [At([0.05, 0.05, 0.05], 0.0, x, z, color="black") for z in (-0.27, 0.27)]
    prop("Whiteboard", "Office", "A whiteboard on a wheeled stand, covered in scribbles.", parts, collider=[C([1.5, 1.75, 0.1], [0, 0.875, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10}, breakable=brk(70, "metal", count=10, size=(0.05, 0.3), speed=3))


def server_rack():
    W, H, D = 0.62, 2.0, 0.9
    parts = [At([W, H, D], 0, color="#1a1a1e", mat="metal"), At([W - 0.06, H - 0.14, 0.01], 0.07, 0, D / 2 + 0.002, color="#24242a")]
    for i in range(12):
        y = 0.12 + i * 0.145
        parts.append(At([W - 0.1, 0.1, 0.01], y, 0, D / 2 + 0.008, color="#2e2e36"))
        for k in range(4):
            parts.append(At([0.015, 0.012, 0.005], y + 0.07, -0.2 + k * 0.03, D / 2 + 0.014, color=["#30f060", "#30f060", "#f0c030", "#30c0f0"][(i + k) % 4]))
    parts += [At([0.02, 0.4, 0.03], 1.0, W / 2 - 0.05, D / 2 + 0.02, color="#888888"), At([0.4, 0.04, 0.4], H, 0, 0, color="#2a2a2e")]
    prop("Server Rack", "Office", "A tall rack of blinking servers. Solid steel cover.", parts, collider=[C([W, H + 0.04, D + 0.04])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 2}, breakable=brk(420, "metal", by=["explosions", "streaks", "bullets"], count=18, size=(0.05, 0.3), speed=4))


def lockers():
    parts = []
    for i in range(3):
        x = -0.6 + i * 0.4 + 0.2
        parts += [At([0.38, 1.8, 0.48], 0.06, x, 0, color="#4a6a8a", mat="metal"), At([0.34, 1.72, 0.01], 0.1, x, 0.245, color="#5a7a9a"),
                  At([0.03, 0.12, 0.03], 1.0, x + 0.13, 0.26, color="chrome")]
        for k in range(4):
            parts.append(At([0.2, 0.012, 0.005], 1.55 + k * 0.04, x, 0.252, color="#2a3a4a"))
    parts += [At([1.22, 0.06, 0.5], 0.0, color="#2a2a2e"), At([1.22, 0.03, 0.5], 1.86, color="#3a5a7a")]
    prop("Metal Lockers", "Office", "A bank of three steel lockers.", parts, collider=[C([1.22, 1.89, 0.52])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(280, "metal", count=16, size=(0.06, 0.35), speed=3.5))


def partition():
    parts = [At([1.6, 1.4, 0.06], 0.06, color="#6a7a8a"), At([1.62, 0.04, 0.08], 1.46, color="#9a9ea4"),
             At([0.04, 1.42, 0.08], 0.06, -0.8, 0, color="#9a9ea4"), At([0.04, 1.42, 0.08], 0.06, 0.8, 0, color="#9a9ea4"),
             At([0.3, 0.06, 0.4], 0.0, -0.7, 0, color="#555555"), At([0.3, 0.06, 0.4], 0.0, 0.7, 0, color="#555555"),
             At([0.2, 0.25, 0.005], 1.0, -0.3, 0.033, color="#f0e060", yaw=4), At([0.15, 0.15, 0.005], 1.1, 0.1, 0.033, color="#f08080"),
             At([0.3, 0.2, 0.005], 0.8, 0.4, 0.033, color="white")]
    prop("Office Partition", "Office", "A fabric cubicle partition with notes pinned on it.", parts, collider=[C([1.62, 1.5, 0.1], [0, 0.75, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(60, "soft", count=12, size=(0.06, 0.3), speed=3))


def archive_boxes():
    parts = []
    for (x, y, z, yaw) in ((-0.22, 0.0, 0.0, 2), (0.22, 0.0, 0.02, -3), (0.0, 0.28, 0.0, 6), (0.5, 0.0, 0.32, 30)):
        parts += [At([0.4, 0.28, 0.32], y, x, z, color="#d8ccb0", yaw=yaw), At([0.42, 0.06, 0.34], y + 0.24, x, z, color="#c8bca0", yaw=yaw)]
        lx, lz = yawed(0, 0.165, yaw)
        parts += [At([0.12, 0.06, 0.004], y + 0.1, x + lx, z + lz, color="white", yaw=yaw), At([0.08, 0.03, 0.004], y + 0.04, x + lx, z + lz, color="#555555", yaw=yaw)]
    prop("Archive Boxes", "Office", "Cardboard archive boxes full of old files.", parts, collider=[C([0.86, 0.58, 0.38], [0, 0.29, 0]), C([0.42, 0.3, 0.42], [0.5, 0.15, 0.32])],
         scatter={"randomYaw": True}, breakable=brk(20, "soft", count=14, size=(0.05, 0.2), speed=3, colors=["#d8ccb0", "white", "paper"]))


def conference_table():
    L, W = 3.0, 1.2
    parts = [At([L, 0.05, W], 0.72, color="#5a3a22", mat="wood"), At([L - 0.1, 0.02, W - 0.1], 0.77, color="#6a4428")]
    for x in (-L / 2 + 0.5, L / 2 - 0.5):
        parts += [At([0.12, 0.7, 0.7], 0.02, x, 0, color="#3a3a3e"), At([0.5, 0.04, 0.9], 0.0, x, 0, color="#2a2a2e")]
    parts += [At([0.2, 0.01, 0.28], 0.79, -0.8, 0.3, color="white", yaw=5), At([0.2, 0.01, 0.28], 0.79, 0.6, -0.3, color="white", yaw=-8),
              At([0.3, 0.02, 0.22], 0.79, 0.0, 0.0, color="#222222"), At([0.08, 0.06, 0.08], 0.79, 1.0, 0.25, color="#333333")]
    parts += Cyl(0.08, 0.1, 0.79, -1.1, -0.3, color="white")
    prop("Conference Table", "Office", "A long boardroom table with papers and a speakerphone.", parts,
         collider=[C([L, 0.07, W], [0, 0.755, 0]), C([0.5, 0.72, 0.9], [-L / 2 + 0.5, 0.36, 0]), C([0.5, 0.72, 0.9], [L / 2 - 0.5, 0.36, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(200, "wood", count=18, size=(0.06, 0.5), speed=3.5))


def office_plant():
    parts = Cyl(0.32, 0.36, 0.0, color="#e8e8e4") + Cyl(0.34, 0.03, 0.36, color="#d8d8d4") + [At([0.26, 0.02, 0.26], 0.36, color="#4a3a2a")]
    random.seed(41)
    parts += [At([0.05, 0.5, 0.05], 0.37, color="#5a4a2a")]
    for k in range(9):
        a = k * 40
        x, z = yawed(0.14, 0, a)
        parts.append(B([0.3, 0.04, 0.1], [x, 0.7 + (k % 3) * 0.15, z], color=["#2f6a32", "#3d7a3a", "#4a8a3a"][k % 3], yaw=a, roll=random.uniform(-35, -15)))
    prop("Office Plant", "Office", "A leafy pot plant in a white pot.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(10, "glass", count=10, size=(0.03, 0.12), speed=3, colors=["#e8e8e4", "#3d7a3a", "#4a3a2a"]))


def shredder():
    parts = [At([0.36, 0.5, 0.26], 0.0, color="#2a2a2e"), At([0.38, 0.12, 0.28], 0.5, color="#1a1a1e"), At([0.24, 0.005, 0.01], 0.62, 0, 0.0, color="black"),
             At([0.04, 0.02, 0.04], 0.62, 0.12, 0.06, color="#30f060"), At([0.34, 0.35, 0.005], 0.06, 0, 0.13, color="#8a9aa8")]
    random.seed(77)
    for k in range(6):
        parts.append(At([0.02, 0.01, 0.12], 0.06 + 0.04 * k, random.uniform(-0.12, 0.12), 0.06, color="white", yaw=random.uniform(-40, 40)))
    prop("Paper Shredder", "Office", "A bin-style paper shredder, half full.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(18, "soft", count=12, size=(0.02, 0.08), speed=3, colors=["#2a2a2e", "white", "paper"]))


def desk_phone():
    parts = [At([0.2, 0.05, 0.2], 0.0, color="#2a2a2e"), B([0.18, 0.04, 0.12], [0, 0.07, -0.02], color="#2a2a2e", pitch=12),
             At([0.22, 0.04, 0.06], 0.08, 0, -0.06, color="#1a1a1e"), At([0.1, 0.004, 0.06], 0.08, 0.0, 0.06, color="#88a0b0")]
    for i in range(3):
        for j in range(3):
            parts.append(At([0.02, 0.006, 0.015], 0.05, -0.03 + i * 0.03, 0.02 + j * 0.025, color="#bbbbbb"))
    prop("Desk Phone", "Office", "An office telephone (for desks).", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(3, "soft", count=5, size=(0.02, 0.06), speed=2.5))


def metal_shelving():
    W, H, D = 1.2, 1.8, 0.5
    parts = [At([0.04, H, 0.04], 0, sx * (W / 2 - 0.02), sz * (D / 2 - 0.02), color="#8a8e94", mat="metal") for sx in (-1, 1) for sz in (-1, 1)]
    for i, y in enumerate((0.1, 0.6, 1.1, 1.6)):
        parts.append(At([W, 0.03, D], y, color="#9a9ea4", mat="metal"))
        random.seed(i * 5 + 2)
        x = -W / 2 + 0.06
        while x < W / 2 - 0.3:
            w = random.uniform(0.2, 0.36)
            h = random.uniform(0.14, 0.34)
            c = random.choice(["cardboard", "#d8ccb0", "#3a5a9a", "#c03030", "cardboard"])
            parts.append(At([w, h, D * random.uniform(0.6, 0.85)], y + 0.03, x + w / 2, random.uniform(-0.04, 0.04), color=c, yaw=random.uniform(-4, 4)))
            x += w + random.uniform(0.02, 0.1)
    prop("Metal Shelving", "Office", "Steel storage shelving loaded with boxes and tubs.", parts, collider=[C([W, H, D])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(160, "metal", count=18, size=(0.05, 0.3), speed=3.5))


def safe():
    parts = [At([0.6, 0.75, 0.6], 0.04, color="#2a2e2a", mat="metal"), At([0.5, 0.65, 0.02], 0.09, 0, 0.305, color="#343a34"),
             At([0.04, 0.5, 0.04], 0.16, -0.22, 0.32, color="#222222")]
    parts += [B([0.12, 0.05, 0.03], [0.05, 0.5, 0.325], color="#b89a50", roll=a) for a in (0, 45, 90, 135)]
    parts += [At([0.16, 0.03, 0.04], 0.3, 0.05, 0.33, color="chrome"), At([0.56, 0.04, 0.56], 0.0, color="#1a1a1a")]
    prop("Safe", "Office", "A heavy steel safe. Doesn't break.", parts, collider=[C([0.6, 0.79, 0.64], [0, 0.395, 0.02])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8})


def reception_desk():
    parts = [At([2.4, 1.05, 0.12], 0, 0, 0.3, color="#e8e4dc"), At([2.5, 0.04, 0.32], 1.05, 0, 0.26, color="#5a3a22", mat="wood"),
             At([0.12, 1.05, 0.7], 0, -1.14, 0.0, color="#e8e4dc"), At([0.12, 1.05, 0.7], 0, 1.14, 0.0, color="#e8e4dc"),
             At([2.2, 0.04, 0.6], 0.72, 0, -0.02, color="#d8ccb0"), At([2.4, 0.18, 0.01], 0.6, 0, 0.365, color="#3a5a8a"),
             At([0.5, 0.32, 0.03], 0.78, -0.4, -0.05, color="#18181a"), At([0.46, 0.28, 0.005], 0.8, -0.4, -0.034, color="#2a4a6a"),
             At([0.2, 0.08, 0.12], 1.09, 0.6, 0.26, color="#c8b070"), At([0.08, 0.02, 0.06], 1.09, 0.3, 0.24, color="#222222")]
    prop("Reception Desk", "Office", "A reception counter with a computer behind it.", parts, collider=[C([2.5, 1.09, 0.76], [0, 0.545, 0.0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 2}, breakable=brk(220, "wood", count=18, size=(0.06, 0.45), speed=3.5))


# =============================== Kitchen ===============================

def fridge():
    W, H, D = 0.7, 1.8, 0.68
    parts = [At([W, H, D], 0.03, color="#e8e8e4", mat="metal"), At([W - 0.02, 1.08, 0.03], 0.06, 0, D / 2 + 0.015, color="#f0f0ec"),
             At([W - 0.02, 0.62, 0.03], 1.18, 0, D / 2 + 0.015, color="#f0f0ec"),
             At([0.03, 0.4, 0.04], 0.7, W / 2 - 0.07, D / 2 + 0.05, color="#b8bcc0"), At([0.03, 0.25, 0.04], 1.25, W / 2 - 0.07, D / 2 + 0.05, color="#b8bcc0"),
             At([0.08, 0.1, 0.005], 1.4, -0.15, D / 2 + 0.033, color="#f0d040"), At([0.12, 0.16, 0.005], 1.0, -0.1, D / 2 + 0.033, color="white", yaw=5),
             At([0.05, 0.05, 0.006], 1.55, 0.05, D / 2 + 0.033, color="#d03030"), At([W - 0.04, 0.03, D - 0.04], 0.0, color="#333333")]
    prop("Fridge", "Kitchen", "A tall white fridge-freezer with magnets and a note.", parts, collider=[C([W, H + 0.03, D + 0.08], [0, (H + 0.03) / 2, 0.04])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(300, "metal", count=16, size=(0.06, 0.35), speed=3.5))


def counter(name, desc, sink=False, health=200):
    W, H, D = 1.2, 0.88, 0.62
    parts = [At([W, H - 0.1, D - 0.04], 0.1, 0, -0.02, color="#f0ece0"), At([W - 0.04, 0.1, D - 0.1], 0.0, 0, -0.04, color="#2a2a2a"),
             At([W + 0.02, 0.04, D], H - 0.02, color="#4a4a4e", mat="tiles", tex=2)]
    for x in (-W / 4, W / 4):
        parts += [At([W / 2 - 0.02, 0.6, 0.02], 0.12, x, D / 2 - 0.03, color="#e8e4d8"), At([0.12, 0.02, 0.03], 0.62, x, D / 2 - 0.01, color="chrome")]
    parts.append(At([W - 0.02, 0.1, 0.02], 0.74, 0, D / 2 - 0.03, color="#e0dccf"))
    if sink:
        parts += [At([0.5, 0.005, 0.4], H + 0.02, -0.2, 0.0, color="#b8bcc0"), At([0.44, 0.004, 0.34], H + 0.026, -0.2, 0.0, color="#8a8e94"),
                  At([0.03, 0.22, 0.03], H + 0.02, -0.2, -0.24, color="chrome"), At([0.03, 0.03, 0.16], H + 0.22, -0.2, -0.17, color="chrome"),
                  At([0.2, 0.1, 0.06], H + 0.02, 0.35, -0.18, color="#3080d0")]
        parts += Cyl(0.24, 0.015, H + 0.02, 0.35, 0.08, color="white") + Cyl(0.22, 0.015, H + 0.035, 0.35, 0.08, color="white")
    else:
        parts += [At([0.36, 0.03, 0.26], H + 0.02, -0.25, 0.05, color="#c8a070", mat="wood"), At([0.06, 0.04, 0.06], H + 0.05, -0.3, 0.05, color="#e05030"),
                  At([0.14, 0.24, 0.1], H + 0.02, 0.4, -0.2, color="#5a3a22")]
        parts += [At([0.012, 0.08, 0.03], H + 0.26, 0.37 + k * 0.03, -0.2, color="#333333") for k in range(3)]
    prop(name, "Kitchen", desc, parts, collider=[C([W + 0.02, H + 0.02, D])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 2},
         breakable=brk(health, "wood", count=16, size=(0.05, 0.35), speed=3.5, colors=["#f0ece0", "#4a4a4e", "#2a2a2a"]))


def stove():
    W, H, D = 0.6, 0.9, 0.62
    parts = [At([W, H - 0.04, D], 0.04, color="#e8e8e4", mat="metal"), At([W, 0.02, D], H, color="#222222"),
             At([W - 0.08, 0.38, 0.02], 0.12, 0, D / 2 + 0.005, color="#2a2a2c"), At([W - 0.16, 0.24, 0.006], 0.19, 0, D / 2 + 0.017, color="#3a2a1a"),
             At([W - 0.1, 0.03, 0.04], 0.54, 0, D / 2 + 0.03, color="#b8bcc0"), At([W, 0.1, 0.03], 0.74, 0, D / 2 + 0.01, color="#d8d8d4"),
             At([W, 0.18, 0.04], H + 0.02, 0, -D / 2 + 0.02, color="#d8d8d4"), At([W, 0.04, 0.04], 0.0, 0, 0.0, color="#333333")]
    for k in range(4):
        parts.append(At([0.04, 0.04, 0.03], 0.77, -0.21 + k * 0.14, D / 2 + 0.035, color="#333333"))
    for x in (-0.14, 0.14):
        for z in (-0.14, 0.14):
            parts += Cyl(0.18, 0.012, H + 0.02, x, z, color="#555555")
    parts += Cyl(0.24, 0.16, H + 0.035, 0.14, 0.14, color="#b8bcc0") + Cyl(0.26, 0.02, H + 0.195, 0.14, 0.14, color="#a0a4a8")
    prop("Cooker", "Kitchen", "A freestanding oven and hob with a pot on it.", parts, collider=[C([W, H + 0.2, D + 0.06], [0, (H + 0.2) / 2, 0.03])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 2}, breakable=brk(260, "metal", count=16, size=(0.05, 0.3), speed=3.5))


def microwave():
    parts = [At([0.5, 0.3, 0.38], 0.01, color="#2a2a2e"), At([0.34, 0.24, 0.01], 0.04, -0.06, 0.195, color="#18181c"),
             At([0.3, 0.2, 0.004], 0.06, -0.06, 0.201, color="#3a3020"), At([0.1, 0.26, 0.01], 0.03, 0.19, 0.195, color="#3a3a3e"),
             At([0.06, 0.02, 0.005], 0.24, 0.19, 0.202, color="#30f060")]
    parts += [At([0.04, 0.02, 0.04], 0.0, x, z, color="black") for x in (-0.2, 0.2) for z in (-0.14, 0.14)]
    prop("Microwave", "Kitchen", "A black microwave (for counters).", parts, collision="cosmetic", scatter={"randomYaw": True, "yawJitter": 20},
         breakable=brk(50, "metal", count=8, size=(0.03, 0.12), speed=3))


def kettle():
    parts = Cyl(0.16, 0.02, 0.0, color="#222222") + Cyl(0.15, 0.2, 0.02, color="#b8bcc0") + Cyl(0.11, 0.02, 0.22, color="#222222")
    parts += [B([0.03, 0.14, 0.04], [-0.1, 0.13, 0], color="#222222"), At([0.06, 0.03, 0.04], 0.22, -0.07, 0, color="#222222"),
              B([0.06, 0.04, 0.03], [0.09, 0.16, 0], color="#b8bcc0", roll=40)]
    prop("Kettle", "Kitchen", "A steel electric kettle.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(15, "metal", count=5, size=(0.02, 0.06), speed=2.5))


def toaster():
    parts = [At([0.28, 0.18, 0.16], 0.01, color="#c8ccd0"), At([0.2, 0.005, 0.03], 0.19, 0, -0.035, color="#222222"), At([0.2, 0.005, 0.03], 0.19, 0, 0.035, color="#222222"),
             At([0.12, 0.04, 0.012], 0.17, 0, -0.035, color="#c89050"), At([0.03, 0.02, 0.04], 0.12, 0.155, 0, color="#222222"),
             At([0.26, 0.012, 0.14], 0.0, color="#222222")]
    prop("Toaster", "Kitchen", "A two-slot toaster with toast popping up.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(15, "metal", count=5, size=(0.02, 0.06), speed=2.5))


def cooking_pot():
    parts = Cyl(0.32, 0.22, 0.0, color="#9a9ea4") + Cyl(0.34, 0.02, 0.22, color="#b8bcc0") + Cyl(0.06, 0.03, 0.24, color="#222222")
    parts += [At([0.06, 0.03, 0.04], 0.17, 0.19, 0, color="#222222"), At([0.06, 0.03, 0.04], 0.17, -0.19, 0, color="#222222")]
    prop("Cooking Pot", "Kitchen", "A big steel stock pot with its lid on.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(35, "metal", count=6, size=(0.03, 0.1), speed=2.5))


def frying_pan():
    parts = Cyl(0.28, 0.012, 0.0, color="#2a2a2c") + Cyl(0.28, 0.04, 0.012, color="#333336", k=1.0)
    parts += Cyl(0.24, 0.002, 0.04, color="#1e1e20")
    parts += [B([0.24, 0.025, 0.035], [0.25, 0.04, 0], color="#2a2a2c", roll=8), B([0.06, 0.008, 0.06], [-0.05, 0.043, 0.03], color="#f0e8c0"),
              B([0.03, 0.009, 0.03], [-0.05, 0.044, 0.03], color="#f0b020")]
    prop("Frying Pan", "Kitchen", "A cast-iron frying pan with a fried egg in it. Doesn't break.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=35)


def plates():
    parts = []
    for i in range(7):
        parts += Cyl(0.26, 0.016, i * 0.017, 0, 0, color="white" if i % 2 else "#f0ece4")
    parts += Cyl(0.16, 0.06, 0.0, 0.25, 0.08, color="#3a6a9a") + Cyl(0.12, 0.05, 0.0, 0.22, -0.16, color="white")
    parts += Cyl(0.075, 0.1, 0.0, -0.22, 0.14, color="#c8e0e0")
    prop("Stacked Plates", "Kitchen", "A stack of plates, a bowl, a cup and a glass.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(1, "glass", count=12, size=(0.02, 0.07), speed=3, colors=["white", "#f0ece4", "#3a6a9a"]))


def produce_crate(name, fruit, fruit2, desc):
    random.seed(len(name))
    W, H, D = 0.6, 0.3, 0.4
    parts = [At([W - 0.04, 0.02, D - 0.04], 0.0, color="#c8a070")]
    for z in (-D / 2 + 0.01, D / 2 - 0.01):
        parts += [At([W, 0.08, 0.02], 0.02, 0, z, color="#c8a070", mat="wood"), At([W, 0.08, 0.02], 0.18, 0, z, color="#c8a070", mat="wood")]
    for x in (-W / 2 + 0.01, W / 2 - 0.01):
        parts += [At([0.02, H, D], 0.0, x, 0, color="#b08a5a", mat="wood")]
    parts += [At([0.04, H, 0.04], 0.0, sx * (W / 2 - 0.04), sz * (D / 2 - 0.04), color="#a07a50") for sx in (-1, 1) for sz in (-1, 1)]
    for i in range(5):
        for j in range(3):
            c = fruit if random.random() > 0.12 else fruit2
            parts.append(At([0.09, 0.08, 0.09], 0.2 + random.uniform(0, 0.03), -0.22 + i * 0.11, -0.11 + j * 0.11, color=c, yaw=random.uniform(0, 90)))
    parts.append(At([0.16, 0.08, 0.004], 0.1, 0, D / 2 + 0.002, color="white"))
    prop(name, "Kitchen", desc, parts, collider=[C([W, H, D])], scatter={"randomYaw": True},
         breakable=brk(30, "wood", count=12, size=(0.04, 0.2), speed=3, colors=["#c8a070", fruit, fruit, fruit2]))


def produce_stack():
    random.seed(8)
    parts = []
    W, H, D = 0.6, 0.3, 0.4
    for (x, y, z, yaw, fruit) in ((0.0, 0.0, 0.0, 0, "#c02a20"), (0.02, 0.3, 0.0, 4, "#f08a20"), (0.0, 0.6, 0.02, -5, "#5aa030"), (0.64, 0.0, 0.05, 8, "#e0c040")):
        parts += [At([W, H - 0.04, D], y, x, z, color="#c8a070", mat="wood", yaw=yaw), At([W + 0.01, 0.04, D + 0.01], y + H - 0.04, x, z, color="#b08a5a", yaw=yaw)]
        if y >= 0.6 or x > 0.5:
            for i in range(4):
                ox, oz = yawed(-0.2 + i * 0.13, 0.0, yaw)
                parts.append(At([0.1, 0.06, 0.3], y + H - 0.02, x + ox, z + oz, color=fruit, yaw=yaw))
    prop("Produce Crates", "Kitchen", "Stacked crates of tomatoes, oranges, limes and lemons.", parts,
         collider=[C([0.64, 0.9, 0.44], [0.01, 0.45, 0.01]), C([0.64, 0.34, 0.46], [0.64, 0.17, 0.05])], scatter={"randomYaw": True},
         breakable=brk(80, "wood", count=18, size=(0.04, 0.25), speed=3.5, colors=["#c8a070", "#c02a20", "#f08a20", "#5aa030"]))


def potato_sacks():
    parts = []
    for (x, z, yaw, h) in ((-0.22, 0.0, 5, 0.55), (0.22, 0.04, -8, 0.5), (0.0, 0.0, 80, 0.3)):
        y0 = 0.0 if h > 0.4 else 0.5
        if h > 0.4:
            parts += [At([0.4, h, 0.32], 0, x, z, color="#b09a6a", yaw=yaw), At([0.32, 0.08, 0.24], h, x, z, color="#a08a5a", yaw=yaw),
                      At([0.12, 0.08, 0.1], h + 0.08, x, z, color="#a08a5a", yaw=yaw), At([0.2, 0.14, 0.004], h * 0.4, x, z + 0.161, color="#6a3a2a", yaw=yaw)]
        else:
            parts += [At([0.6, 0.25, 0.34], y0, 0.0, 0.0, color="#b8a272", yaw=yaw)]
    parts += [At([0.08, 0.07, 0.08], 0.0, 0.42, 0.25, color="#a87a4a"), At([0.07, 0.06, 0.08], 0.0, 0.5, 0.18, color="#b0844e", yaw=30)]
    prop("Potato Sacks", "Kitchen", "Hessian sacks of potatoes, a couple spilled out.", parts, collider=[C([0.84, 0.75, 0.6], [0, 0.375, 0])],
         scatter={"randomYaw": True}, breakable=brk(40, "soft", by=["explosions", "streaks", "melee", "bullets"], count=14, size=(0.05, 0.12), speed=3,
                                                    colors=["#b09a6a", "#a87a4a", "#a08a5a"]))


def dishwasher():
    W, H, D = 0.6, 0.85, 0.6
    parts = [At([W, H, D], 0.0, color="#b8bcc0", mat="metal"), At([W - 0.02, 0.7, 0.02], 0.05, 0, D / 2 + 0.01, color="#c8ccd0"),
             At([W - 0.02, 0.1, 0.02], 0.75, 0, D / 2 + 0.01, color="#2a2a2e"), At([0.4, 0.03, 0.04], 0.68, 0, D / 2 + 0.03, color="#8a8e94"),
             At([0.03, 0.03, 0.005], 0.8, 0.22, D / 2 + 0.022, color="#30c0f0"), At([0.03, 0.03, 0.005], 0.8, 0.17, D / 2 + 0.022, color="#30f060")]
    prop("Dishwasher", "Kitchen", "A stainless steel dishwasher.", parts, collider=[C([W, H, D + 0.06], [0, H / 2, 0.03])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(200, "metal", count=14, size=(0.05, 0.25), speed=3.5))


def pedal_bin():
    parts = Cyl(0.3, 0.6, 0.02, color="#b8bcc0") + Cyl(0.32, 0.03, 0.62, color="#c8ccd0") + Cyl(0.18, 0.02, 0.65, color="#a8acb0")
    parts += Cyl(0.3, 0.02, 0.0, color="#222222") + [At([0.1, 0.03, 0.08], 0.0, 0, 0.18, color="#222222")]
    prop("Pedal Bin", "Kitchen", "A steel kitchen pedal bin.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(50, "metal", count=8, size=(0.04, 0.14), speed=3))


def coffee_maker():
    parts = [At([0.22, 0.04, 0.26], 0.0, color="#222222"), At([0.22, 0.34, 0.08], 0.0, 0, -0.09, color="#222222"), At([0.22, 0.06, 0.26], 0.34, color="#222222")]
    parts += Cyl(0.14, 0.14, 0.04, 0, 0.04, color="#a8c8c8") + Cyl(0.12, 0.08, 0.05, 0, 0.04, color="#3a2414")
    parts += [At([0.03, 0.08, 0.03], 0.07, 0.09, 0.04, color="#222222"), At([0.02, 0.02, 0.005], 0.3, 0.06, -0.048, color="#f03030")]
    prop("Coffee Maker", "Kitchen", "A filter coffee machine with a full jug.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(8, "glass", count=6, size=(0.02, 0.07), speed=2.5, colors=["#222222", "#a8c8c8", "#3a2414"]))


def knife_block():
    parts = [B([0.12, 0.22, 0.16], [0, 0.1, 0], color="#8a5a32", pitch=-15)]
    for k in range(4):
        parts.append(B([0.015, 0.08, 0.03], [-0.04 + k * 0.027, 0.24, -0.03], color="#222222", pitch=-15))
    prop("Knife Block", "Kitchen", "A wooden knife block (for counters).", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(10, "wood", count=5, size=(0.02, 0.06), speed=2.5), fade=35)


def bar_stool():
    parts = Cyl(0.38, 0.06, 0.72, color="#c03030") + Cyl(0.34, 0.02, 0.7, color="#888888") + Cyl(0.05, 0.68, 0.02, color="chrome")
    parts += Cyl(0.36, 0.02, 0.0, color="#555555") + Ring(0.36, 0.28, 0.02, 0.32, color="chrome")
    prop("Bar Stool", "Kitchen", "A chrome bar stool with a red seat.", parts, collider=[C([0.36, 0.78, 0.36])], scatter={"randomYaw": True},
         breakable=brk(60, "metal", count=6, size=(0.03, 0.15), speed=3))


def chest_freezer():
    W, H, D = 1.2, 0.86, 0.65
    parts = [At([W, H - 0.04, D], 0.04, color="#f0f0ec", mat="metal"), At([W + 0.02, 0.08, D + 0.02], H, color="#e4e4e0"),
             At([0.3, 0.04, 0.05], H - 0.02, 0, D / 2 + 0.02, color="#888888"), At([0.06, 0.03, 0.005], 0.7, 0.45, D / 2 + 0.003, color="#30f060"),
             At([W - 0.04, 0.04, D - 0.04], 0.0, color="#333333")]
    prop("Chest Freezer", "Kitchen", "A big white chest freezer. Waist-high cover.", parts, collider=[C([W + 0.02, H + 0.08, D + 0.06], [0, (H + 0.08) / 2, 0.01])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4}, breakable=brk(280, "metal", count=16, size=(0.06, 0.35), speed=3.5))


# =============================== Garden ===============================

def lawnmower():
    parts = [At([0.5, 0.2, 0.55], 0.08, color="#3a8a3a", mat="metal"), At([0.46, 0.04, 0.5], 0.28, color="#2e7a2e"),
             At([0.2, 0.14, 0.2], 0.32, 0, 0.02, color="#2a2a2c"), At([0.14, 0.06, 0.14], 0.46, 0, 0.02, color="#c03030"),
             At([0.36, 0.26, 0.3], 0.08, 0, -0.42, color="#3a3a3e")]
    for x in (-0.27, 0.27):
        parts += HCyl(0.18, 0.05, 0.09, x, 0.2, color="black") + HCyl(0.18, 0.05, 0.09, x, -0.2, color="black")
        parts.append(B([0.03, 0.9, 0.03], [x * 0.8, 0.62, -0.5], color="#555555", pitch=-30))
    parts.append(At([0.48, 0.03, 0.03], 1.0, 0, -0.73, color="#222222"))
    prop("Lawnmower", "Garden", "A green petrol lawnmower with a grass box.", parts, collider=[C([0.6, 0.6, 1.0], [0, 0.3, -0.2])],
         scatter={"randomYaw": True}, breakable=brk(110, "metal", count=10, size=(0.04, 0.2), speed=3.5))


def wheelbarrow():
    parts = [B([0.62, 0.32, 0.9], [0, 0.5, 0.05], color="#3060a0", mat="metal"), At([0.56, 0.04, 0.8], 0.66, 0, 0.05, color="#6a5a42", mat="dirt"),
             B([0.7, 0.03, 0.96], [0, 0.67, 0.05], color="#2a5090")]
    parts += HCyl(0.36, 0.1, 0.18, 0, 0.62, color="black") + HCyl(0.16, 0.12, 0.18, 0, 0.62, color="#888888")
    for x in (-0.24, 0.24):
        parts += [B([0.04, 0.04, 1.3], [x, 0.42, -0.08], color="#555555", pitch=12), At([0.04, 0.34, 0.04], 0.0, x, -0.3, color="#555555"),
                  At([0.05, 0.05, 0.16], 0.52, x, -0.78, color="#222222")]
    prop("Wheelbarrow", "Garden", "A blue wheelbarrow full of soil.", parts, collider=[C([0.7, 0.7, 1.5], [0, 0.35, -0.12])],
         scatter={"randomYaw": True}, breakable=brk(100, "metal", count=12, size=(0.04, 0.2), speed=3.5, colors=["#3060a0", "#6a5a42", "#555555"]))


def gnome():
    parts = [At([0.18, 0.04, 0.16], 0.0, color="#6a6a5a"), At([0.06, 0.06, 0.06], 0.04, -0.04, 0.02, color="#4a3020"), At([0.06, 0.06, 0.06], 0.04, 0.04, 0.02, color="#4a3020"),
             At([0.14, 0.1, 0.1], 0.08, 0, 0, color="#3060a0"), At([0.16, 0.12, 0.12], 0.17, 0, 0, color="#3a8a3a"), At([0.17, 0.02, 0.13], 0.18, 0, 0, color="#4a3020"),
             At([0.1, 0.09, 0.09], 0.29, 0, 0.0, color="#f0c8a0"), At([0.1, 0.08, 0.04], 0.26, 0, 0.04, color="white"), At([0.03, 0.03, 0.03], 0.32, 0, 0.055, color="#e09080"),
             At([0.12, 0.06, 0.11], 0.37, 0, 0, color="#c02020"), At([0.08, 0.06, 0.07], 0.43, 0, -0.01, color="#c02020"), B([0.04, 0.07, 0.04], [0, 0.51, -0.03], color="#c02020", pitch=-25),
             B([0.03, 0.1, 0.03], [0.09, 0.2, 0.02], color="#3a8a3a", roll=-30)]
    prop("Garden Gnome", "Garden", "A grinning garden gnome with a red hat.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(3, "glass", count=8, size=(0.02, 0.06), speed=3, colors=["#c02020", "#3a8a3a", "#3060a0", "#f0c8a0"]), fade=40)


def hose_reel():
    parts = [At([0.04, 0.5, 0.04], 0, -0.2, 0, color="#3a8a3a"), At([0.04, 0.5, 0.04], 0, 0.2, 0, color="#3a8a3a"),
             At([0.44, 0.04, 0.3], 0, color="#2e7a2e")]
    parts += HCyl(0.42, 0.03, 0.36, -0.15, color="#3a8a3a") + HCyl(0.42, 0.03, 0.36, 0.15, color="#3a8a3a") + HCyl(0.32, 0.27, 0.36, color="#e0c020")
    parts += [At([0.06, 0.06, 0.06], 0.33, 0.24, 0, color="#2e7a2e"), B([0.04, 0.04, 0.6], [0.05, 0.03, 0.35], color="#e0c020", yaw=20),
              B([0.04, 0.04, 0.4], [0.2, 0.03, 0.75], color="#e0c020", yaw=60), At([0.06, 0.06, 0.1], 0.0, 0.38, 0.86, color="#3a8a3a")]
    prop("Hose Reel", "Garden", "A garden hose on a green reel.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(20, "soft", count=8, size=(0.03, 0.12), speed=3))


def kettle_bbq():
    parts = []
    for a in (0, 120, 240):
        x, z = yawed(0.22, 0, a)
        parts.append(B([0.03, 0.7, 0.03], [x * 0.85, 0.34, z * 0.85], color="#222222", yaw=a, roll=10 if a == 0 else 0))
        parts.append(At([0.05, 0.03, 0.05], 0.0, x, z, color="#222222"))
    parts += Cyl(0.5, 0.18, 0.62, color="#1e1e20", mat="metal") + Cyl(0.56, 0.03, 0.8, color="#2a2a2c") + Cyl(0.48, 0.16, 0.83, color="#1e1e20")
    parts += Cyl(0.3, 0.06, 0.99, color="#1e1e20") + [At([0.14, 0.03, 0.03], 1.05, color="#5a3a22"), At([0.03, 0.04, 0.03], 1.02, -0.05, 0, color="#888888"),
                                                       At([0.03, 0.04, 0.03], 1.02, 0.05, 0, color="#888888")]
    parts += [At([0.36, 0.02, 0.36], 0.3, color="#555555")]
    prop("Kettle BBQ", "Garden", "A black kettle barbecue on three legs.", parts, collider=[C([0.56, 1.06, 0.56])], scatter={"randomYaw": True},
         breakable=brk(90, "metal", count=10, size=(0.04, 0.18), speed=3.5))


def gas_bbq():
    parts = [At([1.0, 0.75, 0.5], 0.08, color="#2a2a2e", mat="metal"), At([0.44, 0.6, 0.02], 0.14, -0.23, 0.255, color="#3a3a3e"),
             At([0.44, 0.6, 0.02], 0.14, 0.23, 0.255, color="#3a3a3e"), At([1.4, 0.04, 0.55], 0.83, color="#b8bcc0"),
             B([0.9, 0.3, 0.5], [0, 1.0, -0.05], color="#b8bcc0", mat="metal"), At([0.7, 0.03, 0.03], 1.1, 0, 0.24, color="#222222"),
             At([0.36, 0.02, 0.4], 0.87, 0.5, 0, color="#888888")]
    for k in range(4):
        parts.append(At([0.05, 0.05, 0.03], 0.74, -0.3 + k * 0.2, 0.26, color="#222222"))
    parts += [At([0.06, 0.08, 0.08], 0.0, x, z, color="black") for x in (-0.45, 0.45) for z in (-0.2, 0.2)]
    prop("Gas BBQ", "Garden", "A steel gas barbecue with side shelves.", parts, collider=[C([1.4, 1.17, 0.55])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 6}, breakable=brk(150, "metal", count=12, size=(0.05, 0.25), speed=3.5))


def patio_table():
    parts = Cyl(0.9, 0.03, 0.72, color="#e8e8e4") + Cyl(0.06, 0.72, 0.0, color="#888888") + Cyl(0.5, 0.03, 0.0, color="#888888")
    parts += [At([0.035, 1.5, 0.035], 0.75, color="#888888")]
    for a in range(0, 360, 45):
        x, z = yawed(0.55, 0, a)
        parts.append(B([1.2, 0.02, 0.5], [x, 2.05, z], color="#c03030" if a % 90 == 0 else "#e8e0d0", yaw=a, roll=-18))
    parts += Cyl(0.1, 0.08, 2.18, color="#c03030")
    prop("Patio Table", "Garden", "A round garden table with a striped parasol.", parts, collider=[C([0.9, 0.75, 0.9])], scatter={"randomYaw": True},
         breakable=brk(80, "metal", count=12, size=(0.05, 0.3), speed=3, colors=["#e8e8e4", "#c03030", "#e8e0d0", "#888888"]))


def patio_chair():
    c = "#2a3a2e"
    parts = [At([0.46, 0.03, 0.44], 0.44, color=c)]
    parts += legs4(0.44, 0.42, 0.44, 0.03, color=c)
    parts += [B([0.46, 0.4, 0.03], [0, 0.68, -0.22], color=c, pitch=-10), At([0.03, 0.03, 0.44], 0.64, -0.215, 0, color=c), At([0.03, 0.03, 0.44], 0.64, 0.215, 0, color=c),
              At([0.03, 0.2, 0.03], 0.44, -0.215, 0.2, color=c), At([0.03, 0.2, 0.03], 0.44, 0.215, 0.2, color=c),
              At([0.4, 0.05, 0.38], 0.47, 0, 0.01, color="#c8b070"), B([0.4, 0.34, 0.05], [0, 0.7, -0.19], color="#c8b070", pitch=-10)]
    prop("Patio Chair", "Garden", "A metal garden chair with a cushion.", parts, collider=[C([0.46, 0.52, 0.44]), C([0.46, 0.88, 0.08], [0, 0.44, -0.21])],
         scatter={"randomYaw": True}, breakable=brk(50, "metal", count=8, size=(0.03, 0.2), speed=3))


def sun_lounger():
    parts = [At([0.6, 0.04, 1.3], 0.32, 0, 0.25, color="white"), B([0.6, 0.04, 0.7], [0, 0.56, -0.67], color="white", pitch=40),
             At([0.54, 0.03, 1.24], 0.36, 0, 0.25, color="#3a8ac0"), B([0.54, 0.03, 0.64], [0, 0.59, -0.65], color="#3a8ac0", pitch=40)]
    parts += [At([0.04, 0.32, 0.04], 0.0, x, z, color="white") for x in (-0.27, 0.27) for z in (-0.3, 0.85)]
    parts += [B([0.6, 0.004, 0.4], [0.0, 0.39, 0.4], color="#f0a030", yaw=8)]
    prop("Sun Lounger", "Garden", "A white plastic sun lounger with a towel.", parts, collider=[C([0.6, 0.38, 1.3], [0, 0.19, 0.25]), C([0.6, 0.82, 0.5], [0, 0.41, -0.7])],
         scatter={"randomYaw": True}, breakable=brk(35, "soft", count=10, size=(0.04, 0.25), speed=3))


def picket_fence():
    parts = [At([2.0, 0.06, 0.03], 0.25, 0, -0.03, color="white"), At([2.0, 0.06, 0.03], 0.75, 0, -0.03, color="white")]
    for k in range(11):
        x = -0.95 + k * 0.19
        parts += [At([0.09, 0.9, 0.025], 0.0, x, 0.0, color="#f0f0ea", mat="wood"), B([0.064, 0.064, 0.025], [x, 0.9, 0.0], color="#f0f0ea", roll=45)]
    prop("Picket Fence", "Garden", "A 2 m section of white picket fence.", parts, collider=[C([2.0, 0.94, 0.08], [0, 0.47, -0.01])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 2}, breakable=brk(40, "wood", count=12, size=(0.04, 0.4), speed=3.5))


def fence_panel():
    parts = [At([0.1, 1.85, 0.1], 0.0, -0.95, 0, color="#5a4428", mat="wood"), At([0.1, 1.85, 0.1], 0.0, 0.95, 0, color="#5a4428", mat="wood"),
             At([0.12, 0.04, 0.12], 1.85, -0.95, 0, color="#4a3820"), At([0.12, 0.04, 0.12], 1.85, 0.95, 0, color="#4a3820"),
             At([1.8, 0.12, 0.03], 0.0, 0, 0.0, color="#6a5030", mat="wood")]
    for k in range(10):
        parts.append(At([1.8, 0.18, 0.02], 0.12 + k * 0.165, 0, 0.012 if k % 2 else -0.004, color="#8a6a40" if k % 2 else "#7a5e38", mat="wood"))
    parts.append(At([1.8, 0.05, 0.05], 1.8, 0, 0.0, color="#5a4428"))
    prop("Fence Panel", "Garden", "A 2 m larch-lap wooden fence panel between two posts. Blocks sight.", parts, collider=[C([2.0, 1.89, 0.12])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 1}, breakable=brk(70, "wood", count=16, size=(0.05, 0.5), speed=3.5))


def flower_pot(name, desc, flower, scale=1.0, health=8):
    d = 0.34 * scale
    parts = Cyl(d * 0.75, 0.04 * scale, 0.0, color="#a8583a") + Cyl(d * 0.85, 0.24 * scale, 0.04 * scale, color="#b8603e") + Cyl(d, 0.05 * scale, 0.28 * scale, color="#a8583a")
    parts += [At([d * 0.8, 0.02, d * 0.8], 0.31 * scale, color="#4a3a2a")]
    random.seed(len(name))
    for k in range(7):
        a = k * 51
        x, z = yawed(d * 0.22, 0, a)
        h = random.uniform(0.15, 0.3) * scale
        parts.append(At([0.02, h, 0.02], 0.32 * scale, x, z, color="#3a7a2a"))
        parts.append(At([0.07 * scale, 0.05 * scale, 0.07 * scale], 0.32 * scale + h, x, z, color=flower if k % 3 else "#f0e040", yaw=a))
        parts.append(B([0.12 * scale, 0.02, 0.05 * scale], [x * 1.3, 0.36 * scale, z * 1.3], color="#4a8a3a", yaw=a, roll=-20))
    prop(name, "Garden", desc, parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(health, "glass", count=8, size=(0.02, 0.08), speed=3, colors=["#b8603e", "#a8583a", "#4a3a2a", flower]))


def pot_group():
    parts = []
    for (x, z, s, fl) in ((-0.2, 0.0, 1.0, "#d03060"), (0.18, 0.08, 0.7, "#8040c0"), (0.05, -0.25, 0.55, "#f0f0f0")):
        d = 0.34 * s
        parts += Cyl(d * 0.85, 0.28 * s, 0.0, x, z, color="#b8603e") + Cyl(d, 0.05 * s, 0.28 * s, x, z, color="#a8583a")
        parts += [At([d * 0.8, 0.12 * s, d * 0.8], 0.31 * s, x, z, color="#3a7a2a"), At([d * 0.5, 0.06 * s, d * 0.5], 0.43 * s, x, z, color=fl, yaw=30)]
    prop("Flower Pots", "Garden", "Three terracotta pots of flowers.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(10, "glass", count=12, size=(0.02, 0.08), speed=3, colors=["#b8603e", "#a8583a", "#3a7a2a", "#d03060"]))


def watering_can():
    parts = [At([0.26, 0.22, 0.14], 0.0, color="#3a8a3a"), At([0.04, 0.12, 0.04], 0.22, -0.08, 0, color="#3a8a3a"),
             At([0.18, 0.03, 0.03], 0.31, 0.0, 0, color="#3a8a3a"), At([0.03, 0.12, 0.03], 0.2, 0.08, 0, color="#3a8a3a"),
             B([0.3, 0.03, 0.03], [0.24, 0.17, 0], color="#3a8a3a", roll=40), At([0.06, 0.06, 0.06], 0.25, 0.36, 0, color="#2e7a2e")]
    prop("Watering Can", "Garden", "A green plastic watering can.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(8, "soft", count=5, size=(0.03, 0.08), speed=2.5), fade=40)


def garden_shed():
    W, D, H = 2.2, 1.8, 2.0
    c = "#7a5a38"
    parts = [At([W, 0.12, D], 0.0, color="#5a4428"), At([W, H - 0.12, D], 0.12, color=c, mat="wood", tex=2)]
    for k in range(9):
        parts.append(At([W + 0.01, 0.02, D + 0.01], 0.3 + k * 0.2, color="#6a4c2e"))
    parts += [B([W + 0.3, 0.05, D / 2 + 0.3], [0, H + 0.25, D / 4 + 0.08], color="#2a2a2c", pitch=29),
              B([W + 0.3, 0.05, D / 2 + 0.3], [0, H + 0.25, -D / 4 - 0.08], color="#2a2a2c", pitch=-29),
              At([W, 0.5, D * 0.5], H, 0, 0, color=c)]  # gable fill
    parts += [At([0.8, 1.7, 0.03], 0.15, -0.4, D / 2 + 0.01, color="#8a6a44", mat="wood"), At([0.04, 0.04, 0.05], 0.95, -0.1, D / 2 + 0.03, color="#222222"),
              At([0.6, 0.45, 0.03], 1.0, 0.55, D / 2 + 0.01, color="#a8c0c8"), At([0.66, 0.05, 0.05], 0.98, 0.55, D / 2 + 0.02, color="white"),
              At([0.04, 0.45, 0.04], 1.0, 0.55, D / 2 + 0.02, color="white")]
    prop("Garden Shed", "Garden", "A wooden garden shed with a felt roof. Big, solid cover.", parts, collider=[C([W, H + 0.1, D + 0.04], [0, (H + 0.1) / 2, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3}, breakable=brk(450, "wood", by=["explosions", "streaks"], count=24, size=(0.1, 0.7), speed=4))


def compost_bin():
    parts = [At([0.7, 0.85, 0.7], 0.0, color="#1e1e20"), At([0.6, 0.12, 0.6], 0.85, color="#2a2a2c"), At([0.4, 0.06, 0.4], 0.97, color="#1e1e20"),
             At([0.3, 0.25, 0.02], 0.05, 0, 0.355, color="#2a2a2c")]
    for k in range(5):
        parts.append(At([0.72, 0.03, 0.72], 0.1 + k * 0.15, color="#2a2a2c"))
    prop("Compost Bin", "Garden", "A black plastic compost bin.", parts, collider=[C([0.72, 1.03, 0.72])], scatter={"randomYaw": True},
         breakable=brk(55, "soft", count=12, size=(0.05, 0.2), speed=3, colors=["#1e1e20", "#4a3a2a", "#3a7a2a"]))


def bird_bath():
    parts = Cyl(0.4, 0.08, 0.0, color="#a8a49c", mat="concrete") + Cyl(0.18, 0.6, 0.08, color="#b0aca4", mat="concrete")
    parts += Cyl(0.66, 0.1, 0.68, color="#a8a49c", mat="concrete") + Cyl(0.54, 0.01, 0.75, color="#8ab0c8")
    parts += [At([0.08, 0.06, 0.12], 0.78, 0.22, 0, color="#6a5a4a"), At([0.05, 0.05, 0.05], 0.82, 0.22, 0.06, color="#6a5a4a"), At([0.02, 0.01, 0.03], 0.84, 0.22, 0.1, color="#e0a020")]
    prop("Bird Bath", "Garden", "A stone bird bath with a sparrow on the rim. Doesn't break.", parts, collider=[C([0.66, 0.78, 0.66])], scatter={"randomYaw": True})


def raised_bed():
    W, D, H = 1.8, 0.9, 0.36
    parts = [At([W, H, 0.06], 0, 0, D / 2 - 0.03, color="#8a6a40", mat="wood"), At([W, H, 0.06], 0, 0, -D / 2 + 0.03, color="#8a6a40", mat="wood"),
             At([0.06, H, D], 0, W / 2 - 0.03, 0, color="#7a5e38", mat="wood"), At([0.06, H, D], 0, -W / 2 + 0.03, 0, color="#7a5e38", mat="wood"),
             At([W - 0.12, 0.3, D - 0.12], 0.0, color="#4a3a2a", mat="dirt")]
    random.seed(52)
    for i in range(6):
        for j in range(2):
            x, z = -0.7 + i * 0.28, -0.18 + j * 0.36
            parts += [At([0.14, 0.1, 0.14], 0.3, x, z, color=random.choice(["#3a8a3a", "#4a9a3a", "#2f6a32"]), yaw=random.uniform(0, 90)),
                      At([0.08, 0.08, 0.08], 0.38, x, z, color="#5aa040", yaw=random.uniform(0, 90))]
    prop("Raised Bed", "Garden", "A wooden raised vegetable bed with lettuces.", parts, collider=[C([W, H, D])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8},
         breakable=brk(70, "wood", by=["explosions", "streaks", "melee", "bullets"], count=14, size=(0.05, 0.3), speed=3, colors=["#8a6a40", "#4a3a2a", "#3a8a3a"]))


def garden_tools():
    parts = [B([1.4, 0.03, 0.03], [0, 0.03, 0], color="#a07a50", yaw=10), B([0.4, 0.02, 0.05], [-0.72, 0.02, 0.13], color="#666666", yaw=100),
             B([0.02, 0.03, 0.04], [-0.72, 0.05, 0.13], color="#666666", yaw=100)]
    for k in range(7):
        parts.append(B([0.012, 0.012, 0.06], [-0.72 + 0.03 * math.cos(math.radians(10)), 0.01, 0.13 - 0.18 + k * 0.06], color="#555555", yaw=10))
    parts += [B([1.1, 0.03, 0.03], [0.1, 0.03, 0.3], color="#a07a50", yaw=-15), B([0.22, 0.02, 0.28], [0.75, 0.02, 0.48], color="#888888", yaw=-15),
              B([0.16, 0.03, 0.04], [-0.48, 0.035, 0.15], color="#222222", yaw=-15)]
    prop("Garden Tools", "Garden", "A rake and a spade dropped on the ground.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=40)


def doghouse():
    W, D, H = 0.8, 1.0, 0.65
    parts = [At([W, H, D], 0.0, color="#b08a5a", mat="wood"), B([W / 2 + 0.2, 0.04, D + 0.12], [-W / 4 - 0.05, H + 0.15, 0], color="#8a2a22", roll=36),
             B([W / 2 + 0.2, 0.04, D + 0.12], [W / 4 + 0.05, H + 0.15, 0], color="#8a2a22", roll=-36),
             At([W * 0.6, 0.25, D], H, 0, 0, color="#b08a5a"),
             At([0.36, 0.44, 0.02], 0.0, 0, D / 2 + 0.005, color="#1a1410"), At([0.2, 0.06, 0.01], 0.62, 0, D / 2 + 0.01, color="white")]
    parts += Cyl(0.18, 0.06, 0.0, 0.4, 0.7, color="#c03030") + [At([0.12, 0.02, 0.12], 0.04, 0.4, 0.7, color="#7a5a3a")]
    prop("Dog Kennel", "Garden", "A wooden dog kennel with a red roof and a food bowl.", parts, collider=[C([W + 0.1, H + 0.3, D + 0.1], [0, (H + 0.3) / 2, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 15}, breakable=brk(70, "wood", count=12, size=(0.05, 0.3), speed=3.5))


def paddling_pool():
    parts = Ring(1.6, 1.3, 0.25, 0.0, color="#3a8ad0", n=12) + Ring(1.6, 1.3, 0.04, 0.25, color="#f0f0f0", n=12)
    parts += Cyl(1.36, 0.18, 0.0, color="#8ac8e8") + Cyl(1.36, 0.01, 0.0, color="#3a8ad0")
    parts += [At([0.14, 0.1, 0.1], 0.18, 0.2, 0.1, color="#f0d040"), At([0.06, 0.06, 0.06], 0.24, 0.23, 0.1, color="#f0d040"), At([0.03, 0.02, 0.02], 0.24, 0.27, 0.1, color="#f08020")]
    prop("Paddling Pool", "Garden", "An inflatable paddling pool (with a rubber duck).", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(5, "soft", count=12, size=(0.04, 0.2), speed=3, colors=["#3a8ad0", "#f0f0f0", "#8ac8e8"]))


def compost_bags():
    parts = []
    for (y, x, z, yaw, c) in ((0.0, 0.0, 0.0, 3, "#2a6a2a"), (0.0, 0.05, 0.42, -4, "#2a6a2a"), (0.16, 0.02, 0.2, 80, "#6a3a2a")):
        parts += [At([0.75, 0.16, 0.4], y, x, z, color=c, yaw=yaw), At([0.65, 0.04, 0.32], y + 0.16, x, z, color=c, yaw=yaw)]
    parts += [At([0.3, 0.004, 0.2], 0.2, 0.05, 0.45, color="#f0e040", yaw=-4)]
    prop("Compost Bags", "Garden", "A pile of plastic compost sacks.", parts, collider=[C([0.84, 0.36, 0.86], [0.02, 0.18, 0.2])], scatter={"randomYaw": True},
         breakable=brk(25, "soft", by=["explosions", "streaks", "melee", "bullets"], count=14, size=(0.04, 0.14), speed=3, colors=["#2a6a2a", "#4a3a2a", "#6a3a2a"]))


# =============================== Misc (ducks, animals, everyday things) ===============================

def duck_parts(body, head, bill, s=1.0, x=0.0, z=0.0, yaw=0.0, y0=0.0, wing=None, neck_ring=None, tail=None):
    """A sitting duck facing +X: body, head, bill, a wing and a tail. s scales it (1 = a mallard about 0.5 m long)."""
    def P(size, off, color, **kw):
        ox, oz = yawed(off[0] * s, off[2] * s, yaw)
        return B([v * s for v in size], [x + ox, y0 + off[1] * s, z + oz], color=color, yaw=yaw + kw.get("yaw", 0), pitch=kw.get("pitch", 0), roll=kw.get("roll", 0))
    out = [P([0.42, 0.18, 0.24], [0, 0.11, 0], body), P([0.34, 0.06, 0.2], [0.0, 0.03, 0], body),
           P([0.14, 0.16, 0.12], [0.16, 0.24, 0], head), P([0.13, 0.12, 0.12], [0.2, 0.33, 0], head),
           P([0.1, 0.03, 0.07], [0.3, 0.32, 0], bill),
           P([0.025, 0.025, 0.006], [0.23, 0.36, 0.061], "#111111"), P([0.025, 0.025, 0.006], [0.23, 0.36, -0.061], "#111111"),
           P([0.12, 0.08, 0.16], [-0.24, 0.17, 0], tail or body, roll=-30)]
    if wing:
        out += [P([0.28, 0.1, 0.02], [-0.03, 0.15, 0.125], wing, roll=8), P([0.28, 0.1, 0.02], [-0.03, 0.15, -0.125], wing, roll=8)]
    if neck_ring:
        out.append(P([0.145, 0.02, 0.125], [0.17, 0.27, 0], neck_ring))
    return out


def rubber_duck():
    parts = duck_parts("#f6d020", "#f6d020", "#f07818", s=0.3)
    prop("Rubber Duck", "Misc", "A little yellow rubber duck.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(1, "soft", count=4, size=(0.02, 0.05), speed=2.5), fade=30)


def giant_duck():
    parts = duck_parts("#f6d020", "#f6d020", "#f07818", s=4.0)
    prop("Giant Rubber Duck", "Misc", "A two-metre inflatable rubber duck. Ridiculous, but it's cover.", parts,
         collider=[C([1.8, 0.85, 0.96], [0, 0.42, 0]), C([0.6, 0.65, 0.5], [0.72, 1.2, 0])],
         scatter={"randomYaw": True}, breakable=brk(60, "soft", count=20, size=(0.1, 0.5), speed=5, colors=["#f6d020", "#f07818"]))


def duck_decoy():
    parts = duck_parts("#6a5a48", "#2a5a3a", "#d8c040", s=0.9, wing="#4a3a2a", neck_ring="white", tail="#2a2a2a")
    parts.append(At([0.32, 0.03, 0.18], 0.0, color="#4a3a2a"))
    for p in parts[:-1]:
        p["offset"][1] += 0.03
    prop("Duck Decoy", "Misc", "A carved and painted wooden mallard decoy.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(5, "wood", count=6, size=(0.02, 0.08), speed=2.5), fade=40)


def mallard():
    parts = duck_parts("#8a8a86", "#1e5a2e", "#e0c030", wing="#5a4a3a", neck_ring="white", tail="#222222")
    parts += [At([0.03, 0.03, 0.03], 0.0, 0.03, 0.05, color="#f08020"), At([0.03, 0.03, 0.03], 0.0, 0.03, -0.05, color="#f08020"),
              At([0.12, 0.04, 0.2], 0.08, -0.05, 0, color="#6a4a3a")]  # brown breast band
    for p in parts[:-3]:
        p["offset"][1] += 0.03
    prop("Mallard Duck", "Misc", "A drake mallard sitting on the ground.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=45)


def hen_duck():
    parts = duck_parts("#8a6a48", "#7a5a3a", "#d08030", wing="#6a4a30", tail="#6a4a30")
    for p in parts:
        p["offset"][1] += 0.03
    parts += [At([0.03, 0.03, 0.03], 0.0, 0.03, 0.05, color="#f08020"), At([0.03, 0.03, 0.03], 0.0, 0.03, -0.05, color="#f08020")]
    prop("Hen Duck", "Misc", "A brown female mallard.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=45)


def duck_family():
    random.seed(71)
    parts = duck_parts("#8a6a48", "#7a5a3a", "#d08030", wing="#6a4a30", tail="#6a4a30", x=0.35)
    for k in range(4):
        parts += duck_parts("#e0c860", "#e0c860", "#5a4a30", s=0.35, x=-0.05 - k * 0.24, z=0.08 * (1 if k % 2 else -1), yaw=random.uniform(-15, 15))
    prop("Duck Family", "Misc", "A mother duck leading four ducklings in a line.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=45)


def duck_pond():
    parts = [At([3.0, 0.01, 2.0], 0.0, color="#3a5a3a"), At([2.8, 0.012, 1.8], 0.0, color="#3a6a8a"), At([2.2, 0.014, 1.3], 0.0, color="#4a7a9a", yaw=8)]
    for (x, z, yaw) in ((-1.3, 0.0, 0), (1.3, 0.2, 0), (0.0, 0.9, 90), (0.2, -0.9, 90)):
        parts.append(At([0.5, 0.06, 1.2] if yaw == 0 else [1.6, 0.06, 0.4], 0.0, x, z, color="#7a7a72", mat="concrete"))
    random.seed(64)
    for k in range(10):  # reeds
        x, z = -1.25 + random.uniform(-0.12, 0.12), -0.6 + k * 0.12
        parts.append(At([0.025, random.uniform(0.4, 0.8), 0.025], 0.0, x, z, color=random.choice(["#4a7a3a", "#5a8a3a", "#6a8a4a"])))
    parts += [At([0.03, 0.12, 0.03], 0.6, -1.25, 0.0, color="#5a3a22"), At([0.03, 0.12, 0.03], 0.7, -1.25, -0.24, color="#5a3a22")]
    parts += duck_parts("#8a8a86", "#1e5a2e", "#e0c030", wing="#5a4a3a", neck_ring="white", tail="#222222", x=0.3, z=0.2, yaw=150, y0=-0.04)
    parts += duck_parts("#8a6a48", "#7a5a3a", "#d08030", wing="#6a4a30", tail="#6a4a30", x=-0.4, z=-0.25, yaw=20, y0=-0.04)
    parts += [At([0.3, 0.004, 0.3], 0.014, 0.8, -0.4, color="#4a8a3a"), At([0.22, 0.004, 0.22], 0.014, -0.6, 0.45, color="#4a8a3a")]
    prop("Duck Pond", "Misc", "A small paved pond with reeds, lily pads and two ducks (flat: you walk over it).", parts, collision="cosmetic",
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10}, fade=90)


def cat():
    c = "#d08030"
    parts = [At([0.32, 0.16, 0.16], 0.0, color=c), At([0.14, 0.22, 0.14], 0.08, 0.1, 0, color=c), At([0.13, 0.12, 0.13], 0.28, 0.13, 0, color=c),
             At([0.04, 0.05, 0.03], 0.4, 0.13, 0.04, color=c), At([0.04, 0.05, 0.03], 0.4, 0.13, -0.04, color=c),
             At([0.02, 0.02, 0.006], 0.34, 0.195, 0.03, color="#30a040"), At([0.02, 0.02, 0.006], 0.34, 0.195, -0.03, color="#30a040"),
             At([0.02, 0.03, 0.02], 0.31, 0.2, 0, color="#e09080"), At([0.06, 0.03, 0.08], 0.0, 0.17, 0, color="white"),
             B([0.24, 0.035, 0.035], [-0.2, 0.02, 0.08], color=c, yaw=-30), At([0.02, 0.02, 0.17], 0.08, 0.0, 0, color="#a05a20")]
    prop("Cat", "Misc", "A ginger cat sitting and watching.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=40)


def dog():
    c, d = "#c8a070", "#8a6a40"
    parts = [At([0.6, 0.22, 0.28], 0.0, color=c), At([0.2, 0.16, 0.2], 0.04, 0.36, 0, color=c), At([0.12, 0.08, 0.12], 0.04, 0.5, 0, color=c),
             At([0.04, 0.04, 0.04], 0.1, 0.57, 0, color="#111111"), At([0.06, 0.12, 0.03], 0.08, 0.34, 0.11, color=d), At([0.06, 0.12, 0.03], 0.08, 0.34, -0.11, color=d),
             At([0.02, 0.02, 0.006], 0.16, 0.46, 0.06, color="#111111"), At([0.02, 0.02, 0.006], 0.16, 0.46, -0.06, color="#111111"),
             At([0.22, 0.06, 0.08], 0.0, 0.42, 0.1, color=c), At([0.22, 0.06, 0.08], 0.0, 0.42, -0.1, color=c),
             B([0.3, 0.05, 0.05], [-0.42, 0.06, 0.06], color=c, yaw=-25), At([0.04, 0.03, 0.29], 0.16, 0.25, 0, color="#c02020")]
    prop("Dog", "Misc", "A sleepy labrador lying down.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=45)


def chicken():
    parts = [At([0.26, 0.2, 0.18], 0.1, color="#e8e0d0"), B([0.08, 0.1, 0.12], [-0.14, 0.3, 0], color="#e8e0d0", roll=-30),
             At([0.1, 0.16, 0.1], 0.26, 0.12, 0, color="#e8e0d0"), At([0.06, 0.06, 0.03], 0.42, 0.12, 0, color="#d02020"),
             At([0.05, 0.03, 0.04], 0.34, 0.19, 0, color="#e0a020"), At([0.03, 0.05, 0.02], 0.28, 0.17, 0, color="#d02020"),
             At([0.02, 0.1, 0.02], 0.0, 0.0, 0.04, color="#e0a020"), At([0.02, 0.1, 0.02], 0.0, 0.0, -0.04, color="#e0a020"),
             At([0.02, 0.02, 0.006], 0.36, 0.15, 0.05, color="#111111"), At([0.02, 0.02, 0.006], 0.36, 0.15, -0.05, color="#111111")]
    prop("Chicken", "Misc", "A white hen.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=40)


def pigeons():
    parts = []
    random.seed(19)
    for (x, z) in ((-0.4, -0.25), (0.35, 0.1), (-0.05, 0.4)):
        yaw = random.uniform(0, 360)
        def P(size, off, color, roll=0):
            ox, oz = yawed(off[0], off[2], yaw)
            return B(size, [x + ox, off[1], z + oz], color=color, yaw=yaw, roll=roll)
        parts += [P([0.2, 0.1, 0.1], [0, 0.08, 0], "#8a8a96"), P([0.07, 0.08, 0.07], [0.09, 0.17, 0], "#6a6a7a"), P([0.04, 0.02, 0.02], [0.14, 0.17, 0], "#3a3a3a"),
                  P([0.1, 0.02, 0.07], [-0.13, 0.08, 0], "#4a4a56", roll=-15), P([0.08, 0.04, 0.105], [0.05, 0.12, 0], "#5a8a7a"),
                  P([0.015, 0.04, 0.015], [0.0, 0.02, 0.02], "#c05050"), P([0.015, 0.04, 0.015], [0.0, 0.02, -0.02], "#c05050")]
    prop("Pigeons", "Misc", "Three pigeons pecking about.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=35)


def book_pile():
    random.seed(31)
    parts = []
    y = 0.0
    cols = ["#7a2a22", "#2a4a7a", "#3a6a3a", "#c8b070", "#5a3a6a", "#a85a2a"]
    for i in range(6):
        w, d, t = random.uniform(0.18, 0.26), random.uniform(0.13, 0.18), random.uniform(0.025, 0.05)
        yaw = random.uniform(-15, 15)
        parts += [At([w, t, d], y, random.uniform(-0.02, 0.02), random.uniform(-0.02, 0.02), color=random.choice(cols), yaw=yaw),
                  At([w - 0.01, t - 0.008, 0.004], y + 0.004, 0, d / 2 * 0.0 + 0.0, color="paper", yaw=yaw) if False else
                  At([w - 0.012, t - 0.01, d - 0.01], y + 0.005, 0.004, 0, color="paper", yaw=yaw)]
        y += t
    parts += [B([0.24, 0.03, 0.17], [0.3, 0.015, 0.1], color="#2a4a7a", yaw=40), B([0.12, 0.01, 0.17], [0.3, 0.035, 0.1], color="paper", yaw=40, roll=-15)]
    prop("Book Pile", "Misc", "A pile of books, one left open.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(4, "soft", count=8, size=(0.03, 0.1), speed=2.5, colors=["paper", "#7a2a22", "#2a4a7a", "#c8b070"]), fade=35)


def toy_blocks():
    random.seed(37)
    parts = []
    cols = ["#d03030", "#3060d0", "#f0c020", "#30a040"]
    for (x, y, z) in ((0, 0, 0), (0.11, 0, 0), (0.055, 0.1, 0), (-0.2, 0, 0.15), (0.25, 0, -0.18), (-0.1, 0, -0.2)):
        parts.append(At([0.1, 0.1, 0.1], y, x, z, color=random.choice(cols), yaw=random.uniform(-20, 20)))
    parts += [At([0.04, 0.006, 0.04], 0.2, 0.055, 0, color="white")]
    prop("Toy Blocks", "Misc", "A child's wooden building blocks.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(3, "wood", count=6, size=(0.02, 0.06), speed=2.5), fade=30)


def teddy():
    c = "#9a6a3a"
    parts = [At([0.2, 0.22, 0.16], 0.04, color=c), At([0.12, 0.1, 0.1], 0.06, 0, 0.04, color="#c8a070"),
             At([0.18, 0.16, 0.15], 0.26, color=c), At([0.08, 0.06, 0.06], 0.3, 0, 0.09, color="#c8a070"), At([0.03, 0.02, 0.02], 0.34, 0, 0.12, color="#111111"),
             At([0.06, 0.06, 0.04], 0.4, -0.07, 0, color=c), At([0.06, 0.06, 0.04], 0.4, 0.07, 0, color=c),
             At([0.02, 0.02, 0.006], 0.36, -0.04, 0.076, color="#111111"), At([0.02, 0.02, 0.006], 0.36, 0.04, 0.076, color="#111111"),
             At([0.08, 0.08, 0.18], 0.0, -0.07, 0.08, color=c), At([0.08, 0.08, 0.18], 0.0, 0.07, 0.08, color=c),
             B([0.06, 0.16, 0.06], [-0.13, 0.17, 0.02], color=c, roll=25), B([0.06, 0.16, 0.06], [0.13, 0.17, 0.02], color=c, roll=-25),
             At([0.2, 0.03, 0.02], 0.24, 0, 0.08, color="#c02030")]
    prop("Teddy Bear", "Misc", "A sitting teddy bear with a red ribbon.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(3, "soft", count=8, size=(0.02, 0.06), speed=2.5, colors=[c, "#c8a070", "white"]), fade=35)


def mop_bucket():
    parts = [At([0.4, 0.04, 0.34], 0.04, color="#e0c020")] + [At([0.04, 0.06, 0.04], 0.0, x, z, color="black") for x in (-0.16, 0.16) for z in (-0.13, 0.13)]
    parts += [At([0.38, 0.26, 0.02], 0.08, 0, 0.16, color="#e0c020"), At([0.38, 0.26, 0.02], 0.08, 0, -0.16, color="#e0c020"),
              At([0.02, 0.26, 0.3], 0.08, 0.18, 0, color="#e0c020"), At([0.02, 0.26, 0.3], 0.08, -0.18, 0, color="#e0c020"),
              At([0.34, 0.01, 0.3], 0.26, 0, 0, color="#8a9a7a"), At([0.14, 0.14, 0.32], 0.34, 0.1, 0, color="#555555"),
              B([0.025, 1.2, 0.025], [-0.08, 0.72, 0.0], color="#3060d0", roll=10), At([0.14, 0.12, 0.14], 0.12, -0.02, 0, color="#d8d0c0")]
    prop("Mop and Bucket", "Misc", "A yellow mop bucket with a mop in it.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(15, "soft", count=8, size=(0.03, 0.12), speed=3))


def broom():
    parts = [B([1.3, 0.025, 0.025], [0, 0.03, 0], color="#c8a070", yaw=8), B([0.08, 0.05, 0.3], [-0.68, 0.03, 0.1], color="#c03030", yaw=8),
             B([0.08, 0.04, 0.3], [-0.74, 0.02, 0.1], color="#e0c060", yaw=8)]
    prop("Broom", "Misc", "A broom lying on the floor.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=35)


def paint_cans():
    parts = []
    for (x, z, c, h) in ((0.0, 0.0, "#3060d0", 0.2), (0.2, 0.05, "#e8e8e4", 0.2), (0.1, -0.18, "#30a040", 0.14)):
        parts += Cyl(0.18, h, 0.0, x, z, color="#b8bcc0") + [At([0.15, h * 0.6, 0.15], h * 0.2, x, z, color=c) if False else At([0.13, h * 0.5, 0.005], h * 0.25, x, z + 0.088, color=c)]
        parts += Cyl(0.16, 0.01, h, x, z, color=c)
    parts += [B([0.18, 0.004, 0.14], [-0.2, 0.002, 0.15], color="#3060d0", yaw=20), B([0.25, 0.015, 0.06], [-0.2, 0.01, -0.12], color="#a07a50", yaw=60)]
    prop("Paint Cans", "Misc", "Three tins of paint and a spill.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(20, "metal", count=8, size=(0.03, 0.08), speed=3, colors=["#b8bcc0", "#3060d0", "#e8e8e4", "#30a040"]), fade=40)


def toolbox():
    parts = [At([0.5, 0.2, 0.22], 0.0, color="#c02020", mat="metal"), At([0.5, 0.04, 0.22], 0.2, color="#a01818"),
             At([0.03, 0.08, 0.03], 0.24, -0.1, 0, color="#222222"), At([0.03, 0.08, 0.03], 0.24, 0.1, 0, color="#222222"), At([0.24, 0.03, 0.04], 0.32, 0, 0, color="#222222"),
             At([0.06, 0.04, 0.01], 0.17, -0.15, 0.112, color="chrome"), At([0.06, 0.04, 0.01], 0.17, 0.15, 0.112, color="chrome"),
             B([0.24, 0.025, 0.03], [0.4, 0.015, 0.1], color="#e0c020", yaw=30), B([0.08, 0.03, 0.04], [0.5, 0.02, 0.17], color="#555555", yaw=30)]
    prop("Toolbox", "Misc", "A red steel toolbox with a screwdriver next to it.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(50, "metal", count=8, size=(0.03, 0.12), speed=3))


def suitcase():
    parts = [At([0.48, 0.7, 0.24], 0.04, color="#2a3a5a"), At([0.5, 0.04, 0.26], 0.4, color="#1e2a44")]
    for x in (-0.1, 0.1):
        parts.append(At([0.012, 0.66, 0.245], 0.06, x, 0, color="#1e2a44"))
    parts += [At([0.03, 0.2, 0.03], 0.74, -0.08, -0.06, color="#888888"), At([0.03, 0.2, 0.03], 0.74, 0.08, -0.06, color="#888888"), At([0.2, 0.03, 0.04], 0.94, 0, -0.06, color="#222222"),
              At([0.04, 0.04, 0.04], 0.0, -0.2, -0.08, color="black"), At([0.04, 0.04, 0.04], 0.0, 0.2, -0.08, color="black"),
              At([0.14, 0.08, 0.005], 0.5, 0.12, 0.122, color="#f0d040")]
    prop("Suitcase", "Misc", "A navy wheeled suitcase with a luggage tag.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(25, "soft", count=10, size=(0.04, 0.14), speed=3, colors=["#2a3a5a", "white", "#c03030", "#3a6a3a"]))


def guitar():
    parts = [At([0.36, 0.1, 0.44], 0.0, -0.3, 0, color="#c8904a", yaw=0), At([0.3, 0.1, 0.34], 0.0, 0.0, 0, color="#c8904a"),
             At([0.32, 0.005, 0.42], 0.1, -0.3, 0, color="#d8a05a"), At([0.26, 0.005, 0.3], 0.1, 0.0, 0, color="#d8a05a")]
    parts += Cyl(0.1, 0.006, 0.1, -0.12, 0, color="#1a1410")
    parts += [At([0.04, 0.012, 0.1], 0.1, -0.36, 0, color="#3a2414"), At([0.5, 0.03, 0.06], 0.07, 0.38, 0, color="#3a2414"), At([0.14, 0.03, 0.08], 0.07, 0.69, 0, color="#3a2414")]
    for k in range(3):
        parts.append(At([0.82, 0.003, 0.004], 0.106, 0.1, -0.012 + k * 0.012, color="#d8d8d8"))
    prop("Acoustic Guitar", "Misc", "An acoustic guitar lying on its back.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(8, "wood", count=8, size=(0.02, 0.12), speed=3), fade=40)


def duffel_bag():
    parts = HCyl(0.32, 0.65, 0.16, color="#3a4a2a") + [At([0.66, 0.02, 0.06], 0.31, color="#2a3a1e"),
                                                     At([0.04, 0.12, 0.04], 0.3, -0.12, 0, color="#222222"), At([0.04, 0.12, 0.04], 0.3, 0.12, 0, color="#222222"),
                                                     At([0.28, 0.03, 0.04], 0.41, 0, 0, color="#222222"), At([0.02, 0.33, 0.33], 0.0, 0.33, 0, color="#2a3a1e"),
                                                     At([0.02, 0.33, 0.33], 0.0, -0.33, 0, color="#2a3a1e")]
    prop("Duffel Bag", "Misc", "An olive canvas duffel bag.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(12, "soft", count=8, size=(0.04, 0.12), speed=3))


def backpack():
    parts = [At([0.32, 0.42, 0.18], 0.0, color="#c03030"), At([0.28, 0.2, 0.06], 0.06, 0, 0.12, color="#a02020"), At([0.33, 0.08, 0.19], 0.4, color="#a02020"),
             At([0.08, 0.06, 0.03], 0.48, 0, 0.0, color="#222222"), At([0.05, 0.36, 0.03], 0.04, -0.09, -0.105, color="#222222"), At([0.05, 0.36, 0.03], 0.04, 0.09, -0.105, color="#222222"),
             At([0.18, 0.02, 0.01], 0.22, 0, 0.152, color="#222222")]
    prop("Backpack", "Misc", "A red school backpack.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(10, "soft", count=8, size=(0.03, 0.1), speed=3, colors=["#c03030", "white", "#2a4a7a"]), fade=40)


def step_ladder():
    parts = []
    for x in (-0.22, 0.22):
        parts += [B([0.05, 1.5, 0.03], [x, 0.72, 0.18], color="#b8bcc0", pitch=-12), B([0.04, 1.45, 0.03], [x, 0.7, -0.2], color="#b8bcc0", pitch=14)]
    for k in range(4):
        y = 0.3 + k * 0.33
        parts.append(At([0.44, 0.03, 0.12], y, 0, 0.3 - 0.3 * (y / 1.44) * 0.95 - 0.02, color="#9a9ea4"))
    parts += [At([0.5, 0.06, 0.24], 1.44, 0, 0.0, color="#c03030"), At([0.4, 0.02, 0.3], 0.7, 0, 0.0, color="#888888")]
    parts += [At([0.06, 0.03, 0.06], 0.0, x, z, color="black") for x in (-0.22, 0.22) for z in (-0.37, 0.33)]
    prop("Step Ladder", "Misc", "An aluminium step ladder, opened out.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(60, "metal", count=8, size=(0.04, 0.3), speed=3))


def sports_balls():
    parts = []
    for (x, z, d, c, band) in ((0.0, 0.0, 0.24, "#e07020", "#222222"), (0.3, 0.1, 0.22, "white", "#222222"), (-0.25, 0.15, 0.07, "#d0e040", None)):
        parts += Cyl(d, d * 0.6, d * 0.2, x, z, color=c) + HCyl(d, d * 0.6, d / 2, x, z, color=c) + HCyl(d, d * 0.6, d / 2, x, z, color=c, alongZ=True)
        if band:
            parts.append(B([d * 1.01, 0.012, d * 0.3], [x, d / 2, z], color=band, yaw=30))
    prop("Sports Balls", "Misc", "A basketball, a football and a tennis ball.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(5, "soft", count=6, size=(0.03, 0.08), speed=3), fade=35)


def bucket():
    parts = Cyl(0.24, 0.26, 0.0, color="#3060d0") + Cyl(0.27, 0.02, 0.26, color="#2050b0") + [At([0.2, 0.01, 0.2], 0.22, color="#8ab0d0")]
    parts += [B([0.27, 0.012, 0.012], [0, 0.34, 0], color="#888888"), B([0.012, 0.1, 0.012], [-0.13, 0.29, 0], color="#888888"), B([0.012, 0.1, 0.012], [0.13, 0.29, 0], color="#888888")]
    prop("Bucket", "Misc", "A blue plastic bucket of water.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(8, "soft", count=6, size=(0.03, 0.1), speed=3, colors=["#3060d0", "#8ab0d0"]), fade=40)


# ---------------------------------------------------------------------------------------------------------------

ALL = [
    # Household
    lambda: bed("Double Bed", 1.5, "A wooden double bed, made up with a blue duvet.", 200),
    lambda: bed("Single Bed", 0.95, "A single bed with a green duvet.", 140, head_col="#a07a50", duvet="#4a8a5a"),
    bunk_bed, wardrobe, chest_of_drawers, bookshelf, bedside_table, fabric_sofa, armchair, coffee_table, tv_stand, crt_tv, floor_lamp, table_lamp,
    lambda: rug("Rug", 2.4, 1.6, "#8a2a2a", "#3a2a4a", "A big patterned rug (flat).", "diamond"),
    lambda: rug("Striped Rug", 1.8, 1.0, "#d8c8a0", "#3a5a8a", "A striped runner rug (flat).", "stripes"),
    dining_table, dining_chair,
    lambda: washing_machine("Washing Machine", "A white front-loading washing machine."),
    lambda: washing_machine("Tumble Dryer", "A white tumble dryer.", dryer=True),
    ironing_board, toilet, bath_sink, bathtub, shower, laundry_basket, radiator, coat_stand, standing_mirror, piano, fireplace, vacuum, fish_tank, baby_cot,
    # Office
    office_desk, office_chair, monitor, pc_tower, laptop, photocopier, desk_printer, water_cooler, whiteboard, server_rack, lockers, partition, archive_boxes,
    conference_table, office_plant, shredder, desk_phone, metal_shelving, safe, reception_desk,
    # Kitchen
    fridge, lambda: counter("Kitchen Counter", "A kitchen base unit with a chopping board and a knife block.", health=180),
    lambda: counter("Kitchen Sink", "A kitchen unit with a steel sink, washing-up liquid and plates.", sink=True, health=180),
    stove, microwave, kettle, toaster, cooking_pot, frying_pan, plates,
    lambda: produce_crate("Apple Crate", "#c02a20", "#5aa030", "A wooden crate of red apples."),
    lambda: produce_crate("Orange Crate", "#f08a20", "#e0c040", "A wooden crate of oranges."),
    lambda: produce_crate("Cabbage Crate", "#6aa040", "#8ac060", "A wooden crate of cabbages."),
    produce_stack, potato_sacks, dishwasher, pedal_bin, coffee_maker, knife_block, bar_stool, chest_freezer,
    # Garden
    lawnmower, wheelbarrow, gnome, hose_reel, kettle_bbq, gas_bbq, patio_table, patio_chair, sun_lounger, picket_fence, fence_panel,
    lambda: flower_pot("Flower Pot", "A terracotta pot of red flowers.", "#d03030"),
    lambda: flower_pot("Large Flower Pot", "A big terracotta pot of purple flowers.", "#8040c0", scale=1.8, health=15),
    pot_group, watering_can, garden_shed, compost_bin, bird_bath, raised_bed, garden_tools, doghouse, paddling_pool, compost_bags,
    # Misc
    rubber_duck, giant_duck, duck_decoy, mallard, hen_duck, duck_family, duck_pond, cat, dog, chicken, pigeons, book_pile, toy_blocks, teddy,
    mop_bucket, broom, paint_cans, toolbox, suitcase, guitar, duffel_bag, backpack, step_ladder, sports_balls, bucket,
]


def write_prop(p):
    folder = os.path.join(OUT, p["category"])
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


def footprint(p):
    """Rough model bounds [minx, maxx, minz, maxz, maxy] from the parts (ignores rotation's growth beyond the box diagonal)."""
    xs, zs, ys = [], [], []
    for part in p["parts"]:
        s = part["size"]
        o = part.get("offset", [0, s[1] / 2, 0])
        r = 0.5 * math.sqrt(s[0] ** 2 + s[2] ** 2) if part.get("rotation") else None
        hx = r if r else s[0] / 2
        hz = r if r else s[2] / 2
        xs += [o[0] - hx, o[0] + hx]
        zs += [o[2] - hz, o[2] + hz]
        ys.append(o[1] + s[1] / 2)
    return min(xs), max(xs), min(zs), max(zs), max(ys)


def gallery():
    """Maps/Dev/PropGallery3.json: every prop of these categories on its own pad, a row (or two) per category, north to south."""
    CAT_COLORS = {"Household": "#a8603b", "Office": "#3b6ea8", "Kitchen": "#c8c8c0", "Garden": "#3b8a45", "Misc": "#d8b020"}
    ops = [{"op": "Ground", "color": [0.36, 0.37, 0.38]}, {"op": "Perimeter", "height": 4, "color": [0.55, 0.55, 0.57]}]
    half_x, half_z = 46, 24
    max_row = 2 * half_x - 14
    z = -half_z + 7.0
    for cat in CATEGORIES:
        props = sorted([p for p in PROPS if p["category"] == cat], key=lambda p: p["name"].lower())
        x = -max_row / 2
        row_depth = 0.0
        first = True
        for p in props:
            x0, x1, z0, z1, _ = footprint(p)
            w = max(x1 - x0, 0.8) + 0.6
            d = max(z1 - z0, 0.8) + 0.6
            if x + w > max_row / 2:
                z += row_depth + 1.6
                x = -max_row / 2
                row_depth = 0.0
                first = True
            if first:
                ops.append({"op": "Block", "x0": round(x - 1.6, 2), "y0": 0, "z0": round(z - 1.2, 2), "x1": round(x - 0.6, 2), "y1": 0.06, "z1": round(z + 1.2, 2), "color": CAT_COLORS[cat]})
                first = False
            cx = x + w / 2
            cz = z + d / 2 - 1.2
            ops.append({"op": "Block", "x0": round(x + 0.1, 2), "y0": 0, "z0": round(cz - d / 2, 2), "x1": round(x + w - 0.1, 2), "y1": 0.05, "z1": round(cz + d / 2, 2),
                        "color": [0.5, 0.5, 0.52]})
            ops.append({"op": "Prop", "name": p["name"], "x": round(cx - (x0 + x1) / 2, 2), "z": round(cz - (z0 + z1) / 2, 2), "yaw": 0})
            x += w + 0.4
            row_depth = max(row_depth, d)
        z += row_depth + 3.2
    assert z < half_z - 4, f"gallery rows run to z = {z:.1f}, past the map edge ({half_z})"
    for sx in range(-36, 37, 12):
        ops.append({"op": "Spawn", "x": sx, "z": -half_z + 3})
        ops.append({"op": "Spawn", "x": sx, "z": half_z - 3})
    ops.append({"op": "AutoWaypoints", "spacing": 6})
    m = {"format": "call-of-slop-map/1", "name": "Prop Gallery 3",
         "description": "The indoor and everyday props on their own pads, in rows by category (Household, Office, Kitchen, Garden, Misc from north to south).",
         "author": "Call of Slop", "order": 952, "halfX": half_x, "halfZ": half_z, "sky": [0.6, 0.66, 0.72], "ops": ops}
    lines = ["{"]
    for k in [k for k in m if k != "ops"]:
        lines.append(f'  "{k}": {json.dumps(m[k])},')
    lines.append('  "ops": [')
    for i, o in enumerate(ops):
        lines.append("    " + json.dumps(o) + ("," if i < len(ops) - 1 else ""))
    lines.append("  ]")
    lines.append("}")
    with open(os.path.join(MAP_DIR, "PropGallery3.json"), "w") as fh:
        fh.write("\n".join(lines) + "\n")
    return z


if __name__ == "__main__":
    PROPS.clear()
    random.seed(1)
    for f in ALL:
        f()
    names = set()
    for p in PROPS:
        assert p["name"].lower() not in names, p["name"]
        assert p["category"] in CATEGORIES, p
        assert len(p["parts"]) <= 128, (p["name"], len(p["parts"]))
        names.add(p["name"].lower())
        write_prop(p)
    end = gallery()
    by = {c: sum(1 for p in PROPS if p["category"] == c) for c in CATEGORIES}
    print(len(PROPS), "props", by, f"gallery rows end at z = {end:.1f}")
