#!/usr/bin/env python3
"""Writes the shop-fitting props (Props/Retail/*.json, global and reusable) and the mall's brand props
(Maps/World/Props/Mall/*.json, pack-local: invented brands' fascia signs, logo plaques, food-stall headers, a coffee
kiosk, shopping bags).

Usage: python3 tools/world/mall_props.py <repo root>

Conventions as in the global library (Props/README.md): the origin is on the floor at the prop's centre, +X is "along",
+Z is the front (the shopper's side); wall units have their back at -Z. Fixtures (shelving, counters, cases) are solid
and mostly unbreakable cover; light stock (plush bins, candy tables, mannequins, glass cases) breaks. Hung things
(ceiling lights, banners) are cosmetic and placed with the Prop op's "y". Signs face +Z with their back on the wall.
Every brand is invented (no real names or logos); the lettering is the 5 x 7 pixel font in pixelkit.py.
"""
import json, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pixelkit as pk

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT_RETAIL = os.path.join(REPO, "Props/Retail")
OUT_MALL = os.path.join(REPO, "Maps/World/Props/Mall")


def R(v):
    return round(v, 4)


def part(size, offset, color, **kw):
    p = {"size": [R(v) for v in size], "offset": [R(v) for v in offset], "color": color}
    p.update(kw)
    return p


def octo(d, h, y, color, x=0.0, z=0.0, **kw):
    """Four boxes making an octagonal prism d across, h tall, its bottom at y."""
    return [part((d, h, d * 0.414), (x, y + h / 2, z), color, rotation=[0, a, 0], **kw) for a in (0, 45, 90, 135)]


def octo_lying(d, length, cx, cy, cz, color, along="x", **kw):
    rot = (lambda a: [a, 0, 0]) if along == "x" else (lambda a: [0, 0, a])
    size = (length, d, d * 0.414) if along == "x" else (d * 0.414, d, length)
    return [part(size, (cx, cy, cz), color, rotation=rot(a), **kw) for a in (0, 45, 90, 135)]


RETAIL, MALL = [], []
SQUARE = {"randomYaw": False, "yawStep": 90, "scale": [1, 1]}
LOOSE = {"randomYaw": True, "scale": [0.9, 1.1]}


def add(where, name, description, parts, collision="solid", collider=None, scatter=None, breakable=None, fade=None):
    assert len(parts) <= 128, (name, len(parts))
    assert len(name) <= 40, name
    d = {"format": "call-of-slop-prop/1", "name": name, "description": description, "collision": collision}
    if collider is not None and collision == "solid":
        d["collider"] = collider
    if scatter is not None:
        d["scatter"] = scatter
    if breakable is not None:
        d["breakable"] = breakable
    if fade is not None:
        d["fadeDistance"] = fade
    d["parts"] = parts
    where.append(d)


def box(w, h, d, x=0.0, z=0.0):
    return {"size": [R(w), R(h), R(d)], "offset": [R(x), R(h / 2), R(z)]}


def brk(health, sound, count=10, size=(0.04, 0.2), colors=None):
    b = {"health": health, "breakBy": "all", "sound": sound, "debris": {"count": count, "size": list(size)}}
    if colors:
        b["debris"]["colors"] = colors
    return b


def _dim(c, k=0.72):
    c = c.lstrip("#")
    return "#" + "".join(f"{int(int(c[i:i + 2], 16) * k):02x}" for i in (0, 2, 4))


# lit letters and light strips: a slow, gentle pulse of glow (a light that never changes fails the animated-prop test)
LIT = lambda c, glow=0.9: {"type": "pulse", "period": 6, "color": c, "off": _dim(c), "glow": glow}
STOCK_BRIGHT = ["#d83a2a", "#f0b81e", "#2a6ad0", "#3aa05a", "#e86ab0", "#8a4ac8", "#ff8a20", "#30b8c8", "#f2efe6"]
WHITE_FIX = "#e9e7e2"
CHROME = "chrome"

# ================================================================ shelving
rnd = random.Random(11)


def wall_unit(L, H, D, levels, stock, color=WHITE_FIX, back="#d8d4cc", header=None):
    ps = [part((L, 0.12, D), (0, 0.06, 0), "darkgrey"), part((L, H - 0.12, 0.04), (0, 0.06 + H / 2, -D / 2 + 0.02), back),
          part((0.04, H, D), (L / 2 - 0.02, H / 2, 0), color), part((0.04, H, D), (-L / 2 + 0.02, H / 2, 0), color)]
    for y in levels:
        ps.append(part((L - 0.08, 0.03, D - 0.06), (0, y, 0.01), color))
        ps += stock(y)
    if header:
        ps.append(part((L, 0.22, 0.05), (0, H + 0.11, D / 2 - 0.05), header))
    return ps


def boxes_row(y, L, D, cols, hmin, hmax, n=None, gap=0.04, zoff=0.0):
    n = n or max(2, int(L / 0.36))
    w = (L - 0.12) / n
    out = []
    for k in range(n):
        h = rnd.uniform(hmin, hmax)
        out.append(part((w - gap, h, D * 0.75), (-L / 2 + 0.06 + w * (k + 0.5), y + 0.015 + h / 2, zoff), rnd.choice(cols)))
    return out


# Generic wall shelving (plain stock in boxes)
for name, cols, desc in [("Retail Wall Shelving", ["#c8b89a", "#e0d8c8", "#8aa0b8", "#b8c8a8", "#d8c0a0"], "plain boxed stock"),
                         ("Toy Shelf Unit", STOCK_BRIGHT, "bright toy boxes, a robot and a rocket on top"),
                         ("Pet Food Shelf", ["#c83a2a", "#3a7ac8", "#e8b020", "#4aa050", "#8a5a3a", "#e8e0c8"], "pet food sacks, tins and treat tubs")]:
    L, H, D = 2.0, 2.1, 0.5
    if name == "Pet Food Shelf":
        st = lambda y: boxes_row(y, L, D, cols, 0.28, 0.4, n=5)
    elif name == "Toy Shelf Unit":
        st = lambda y: boxes_row(y, L, D, cols, 0.2, 0.36, n=6)
    else:
        st = lambda y: boxes_row(y, L, D, cols, 0.18, 0.34)
    ps = wall_unit(L, H, D, [0.14, 0.62, 1.1, 1.58], st, header="#c03030" if name == "Toy Shelf Unit" else None)
    if name == "Toy Shelf Unit":
        ps += [part((0.24, 0.3, 0.2), (-0.5, H + 0.37, 0), "#a8b0b8"), part((0.16, 0.14, 0.14), (-0.5, H + 0.59, 0), "#a8b0b8"),
               part((0.05, 0.05, 0.02), (-0.54, H + 0.6, 0.075), "#ff4040"), part((0.05, 0.05, 0.02), (-0.46, H + 0.6, 0.075), "#ff4040"),
               part((0.12, 0.5, 0.12), (0.5, H + 0.47, 0), "white"), part((0.1, 0.14, 0.1), (0.5, H + 0.78, 0), "#d02020"),
               part((0.26, 0.12, 0.04), (0.5, H + 0.28, 0), "#d02020")]
    add(RETAIL, name, f"Wall shelving with {desc}. Solid, unbreakable: back it against a wall.", ps,
        collider=[box(L, H, D)], scatter=SQUARE)

# Gondola (double sided island)
L, H, D = 2.4, 1.5, 1.0
ps = [part((L, 0.12, D - 0.02), (0, 0.06, 0), "darkgrey"), part((L, H - 0.1, 0.05), (0, 0.06 + (H - 0.1) / 2, 0), "#d8d4cc"),
      part((0.05, H, D), (L / 2 - 0.025, H / 2, 0), WHITE_FIX), part((0.05, H, D), (-L / 2 + 0.025, H / 2, 0), WHITE_FIX)]
for y in (0.14, 0.6, 1.06):
    for s in (1, -1):
        ps.append(part((L - 0.1, 0.03, 0.42), (0, y, s * 0.25), WHITE_FIX))
        ps += boxes_row(y, L, 0.42, ["#c8b89a", "#8aa0b8", "#e0d8c8", "#d87050", "#70a0c0", "#b8c8a8"], 0.16, 0.3, n=5, zoff=s * 0.25)
add(RETAIL, "Retail Gondola", "Double-sided island shelving (1.5 m) with boxed stock on both faces. Solid, unbreakable cover you can see over.", ps,
    collider=[box(L, H, D)], scatter=SQUARE)

# Bookcase and book table
L, H, D = 1.8, 2.2, 0.42
bk = ["#7a2a2a", "#2a4a7a", "#2a6a3a", "#c8a040", "#5a3a6a", "#d8d0c0", "#a04a20", "#304050", "#e07060"]
def books(y, L=L, D=D, n=11):
    out = []
    w = (L - 0.1) / n
    for k in range(n):
        h = rnd.uniform(0.2, 0.32)
        out.append(part((w - 0.01, h, D * 0.7), (-L / 2 + 0.05 + w * (k + 0.5), y + 0.015 + h / 2, 0.0), rnd.choice(bk)))
    return out
ps = wall_unit(L, H, D, [0.14, 0.56, 0.98, 1.4, 1.82], books, color="#6a4a30", back="#5a3e28")
add(RETAIL, "Retail Bookcase", "A tall wooden bookshop case, five shelves of books. Solid, unbreakable.", ps, collider=[box(L, H, D)], scatter=SQUARE)

ps = [part((1.6, 0.06, 0.9), (0, 0.76, 0), "#7a5636", material="wood"), part((1.5, 0.72, 0.8), (0, 0.36, 0), "#5a3e28")]
for k, (x, z) in enumerate([(-0.55, -0.22), (-0.2, 0.2), (0.15, -0.2), (0.5, 0.18), (-0.55, 0.22), (0.5, -0.2)]):
    n = rnd.randint(3, 6)
    yy = 0.79
    for i in range(n):
        h = rnd.uniform(0.03, 0.05)
        ps.append(part((0.26, h, 0.34), (x + rnd.uniform(-0.02, 0.02), yy + h / 2, z), rnd.choice(bk), rotation=[0, rnd.uniform(-8, 8), 0]))
        yy += h
ps.append(part((0.3, 0.4, 0.02), (0, 1.0, 0), "#202020", rotation=[-15, 0, 0]))
add(RETAIL, "Book Display Table", "A low wooden table of new releases in neat stacks, with a chalk sign. Breakable wood.", ps,
    collider=[box(1.6, 0.8, 0.9)], scatter=SQUARE, breakable=brk(90, "wood", 14, colors=["#7a5636", "#2a4a7a", "#c8a040"]))

ps = octo(0.12, 1.7, 0, CHROME) + [part((0.6, 0.04, 0.6), (0, 0.02, 0), "darkgrey")]
for y in (0.5, 0.85, 1.2, 1.55):
    for a in (0, 90, 180, 270):
        dx, dz = math.sin(math.radians(a)) * 0.2, math.cos(math.radians(a)) * 0.2
        ps.append(part((0.24, 0.3, 0.03), (dx, y, dz), rnd.choice(bk), rotation=[0, a, 0]))
add(RETAIL, "Book Spinner Rack", "A spinning wire rack of paperbacks. Cosmetic: walk through it.", ps, collision="cosmetic", scatter=LOOSE)

# ================================================================ clothing
GARM = ["#2a3a5a", "#c83a3a", "#e8e0d0", "#3a6a4a", "#d89a3a", "#6a4a8a", "#3a8ab0", "#202020", "#c8b090", "#e87aa0"]


def garments(cx, cz, length, along="x", y_top=1.45, n=None, seed=0):
    r = random.Random(seed)
    n = n or max(3, int(length / 0.11))
    out = []
    step = length / n
    for k in range(n):
        h = r.uniform(0.7, 0.95)
        c = r.choice(GARM)
        o = -length / 2 + step * (k + 0.5)
        if along == "x":
            out.append(part((step * 0.7, h, 0.44), (cx + o, y_top - h / 2, cz), c))
        else:
            out.append(part((0.44, h, step * 0.7), (cx, y_top - h / 2, cz + o), c))
    return out


ps = octo(0.06, 1.5, 0, CHROME) + octo(0.7, 0.04, 0.0, "darkgrey") + octo(1.2, 0.03, 1.48, CHROME)
for a in range(0, 360, 30):
    r = 0.52
    x, z = math.sin(math.radians(a)) * r, math.cos(math.radians(a)) * r
    h = rnd.uniform(0.75, 0.95)
    ps.append(part((0.1, h, 0.44), (x, 1.46 - h / 2, z), rnd.choice(GARM), rotation=[0, a, 0]))
add(RETAIL, "Clothes Rack Round", "A round chrome clothes rail hung all round with shirts and jackets. Solid, breakable (soft).", ps,
    collider=[box(1.2, 1.5, 1.2)], scatter=LOOSE, breakable=brk(55, "soft", 14, colors=GARM[:5]))

ps = [part((1.6, 0.04, 0.5), (0, 0.02, 0), "darkgrey"), part((0.04, 1.55, 0.04), (-0.78, 0.8, 0), CHROME),
      part((0.04, 1.55, 0.04), (0.78, 0.8, 0), CHROME), part((1.6, 0.03, 0.03), (0, 1.56, 0), CHROME)]
ps += garments(0, 0, 1.5, seed=3, y_top=1.53)
add(RETAIL, "Clothes Rail Straight", "A straight chrome clothes rail of hanging shirts and dresses. Solid, breakable (soft).", ps,
    collider=[box(1.6, 1.58, 0.5)], scatter=SQUARE, breakable=brk(50, "soft", 12, colors=GARM[:5]))

L, H, D = 2.0, 2.3, 0.55
ps = [part((L, H, 0.04), (0, H / 2, -D / 2 + 0.02), "#d8d4cc"), part((L, 0.03, 0.03), (0, 1.62, 0.0), CHROME),
      part((L, 0.03, 0.4), (0, 1.95, -0.05), WHITE_FIX)]
ps += garments(0, 0.0, L - 0.1, y_top=1.6, seed=5)
for k in range(5):
    ps.append(part((0.3, 0.16, 0.3), (-0.8 + k * 0.4, 2.05, -0.05), rnd.choice(GARM)))
add(RETAIL, "Clothes Wall Rail", "A wall-mounted rail of hanging clothes with folded stock on a shelf above. Solid, unbreakable: back it against a wall.", ps,
    collider=[box(L, H, D)], scatter=SQUARE)

ps = [part((1.4, 0.06, 0.8), (0, 0.8, 0), "#e8e4dc"), part((1.3, 0.77, 0.7), (0, 0.385, 0), "#c8c0b0")]
for x in (-0.48, -0.16, 0.16, 0.48):
    for z in (-0.18, 0.18):
        c = rnd.choice(GARM)
        n = rnd.randint(3, 6)
        for i in range(n):
            ps.append(part((0.28, 0.035, 0.3), (x, 0.85 + i * 0.037, z), c))
add(RETAIL, "Folded Clothes Table", "A display table of folded jumpers and T-shirts in colour stacks. Breakable.", ps,
    collider=[box(1.4, 0.83, 0.8)], scatter=SQUARE, breakable=brk(80, "wood", 14, colors=GARM[:6]))


def mannequin(top, bottom, shoes="#202020", skin="#e8e4dc", pose=0.0, extra=()):
    ps = [part((0.4, 0.03, 0.4), (0, 0.015, 0), "#303030"), part((0.03, 0.2, 0.03), (0, 0.13, 0), CHROME)]
    ps += [part((0.12, 0.08, 0.24), (s * 0.1, 0.07, 0.03), shoes) for s in (-1, 1)]
    ps += [part((0.13, 0.82, 0.15), (s * 0.1, 0.52, 0), bottom, rotation=[s * pose, 0, 0]) for s in (-1, 1)]
    ps += [part((0.38, 0.2, 0.22), (0, 0.98, 0), bottom), part((0.42, 0.55, 0.24), (0, 1.33, 0), top)]
    ps += [part((0.1, 0.6, 0.11), (s * 0.27, 1.3, 0), top, rotation=[0, 0, s * 6]) for s in (-1, 1)]
    ps += [part((0.08, 0.1, 0.09), (s * 0.3, 0.96, 0), skin) for s in (-1, 1)]
    ps += [part((0.09, 0.1, 0.09), (0, 1.65, 0), skin), part((0.2, 0.25, 0.22), (0, 1.82, 0), skin)]
    ps += list(extra)
    return ps


for nm, (top, bottom, shoes, extra, desc) in {
    "Mannequin Casual": ("#c83a3a", "#2a3a5a", "white", [], "a red jumper and jeans"),
    "Mannequin Formal": ("#202428", "#202428", "#101010", [part((0.06, 0.3, 0.02), (0, 1.38, 0.125), "#a02030"), part((0.14, 0.4, 0.02), (0, 1.4, 0.122), "white")], "a dark suit and tie"),
    "Mannequin Sporty": ("#3a8ab0", "#303030", "#f0f0f0", [part((0.44, 0.06, 0.02), (0, 1.42, 0.123), "white")], "a track top and joggers"),
    "Mannequin Dress": ("#e87aa0", "#e87aa0", "#d0a020", [part((0.46, 0.5, 0.3), (0, 0.72, 0), "#e87aa0")], "a pink summer dress"),
}.items():
    add(RETAIL, nm, f"A shop-window mannequin in {desc}, on a round stand. Solid, breakable (plastic).",
        mannequin(top, bottom, shoes, extra=extra), collider=[box(0.5, 1.95, 0.4)], scatter=LOOSE,
        breakable=brk(40, "soft", 12, colors=["#e8e4dc", top, bottom]))

ps = [part((1.2, 2.3, 0.05), (0, 1.15, -0.575), "#d8d4cc"), part((0.05, 2.3, 1.2), (-0.575, 1.15, 0), "#d8d4cc"),
      part((0.05, 2.3, 1.2), (0.575, 1.15, 0), "#d8d4cc"), part((1.2, 0.05, 1.2), (0, 2.3, 0), "#d8d4cc"),
      part((1.1, 0.03, 0.03), (0, 2.2, 0.56), CHROME), part((0.9, 2.0, 0.03), (-0.1, 1.15, 0.56), "#8a2a3a"),
      part((0.3, 0.05, 0.25), (0, 0.45, -0.4), "#6a4a30"), part((0.4, 0.9, 0.02), (0, 1.4, -0.545), "#c8dce0")]
add(RETAIL, "Changing Room Booth", "A changing cubicle with a curtain, a seat and a mirror. Solid, unbreakable.", ps,
    collider=[box(1.2, 2.3, 0.08, 0, -0.56), box(0.08, 2.3, 1.2, -0.56, 0), box(0.08, 2.3, 1.2, 0.56, 0)], scatter=SQUARE)

# ================================================================ checkout counter (cash wrap)
L, H, D = 2.2, 1.0, 0.8
ps = [part((L, H - 0.05, D), (0, (H - 0.05) / 2, 0), "#3a3a40"), part((L + 0.04, 0.05, D + 0.04), (0, H - 0.025, 0), "#d8d4cc"),
      part((L - 0.1, 0.12, 0.02), (0, 0.06, D / 2 + 0.005), "#202024"),
      part((0.38, 0.12, 0.3), (-0.5, H + 0.06, -0.1), "#2a2a2e"), part((0.32, 0.22, 0.04), (-0.5, H + 0.25, -0.05), "#1a1a1e", rotation=[-15, 0, 0]),
      part((0.28, 0.16, 0.01), (-0.5, H + 0.25, -0.025), "#3070c0", rotation=[-15, 0, 0], anim={"type": "flicker", "rate": 3, "duty": 0.97, "color": "#4a90e0", "off": "#203050", "glow": 0.6}),
      part((0.4, 0.06, 0.3), (-0.5, H + 0.03, 0.15), "#1a1a1e"), part((0.1, 0.16, 0.06), (0.1, H + 0.08, 0.15), "#202020"),
      part((0.08, 0.02, 0.1), (0.1, H + 0.17, 0.15), "#40c060"), part((0.5, 0.3, 0.3), (0.7, H + 0.15, -0.15), "#c8a878"),
      part((0.04, 0.04, 0.12), (0.55, H + 0.32, -0.15), "#202020")]
add(RETAIL, "Shop Checkout Counter", "A shop's cash wrap: a counter with a till and screen, a card reader and a stack of carrier bags. Solid, unbreakable.", ps,
    collider=[box(L, H, D)], scatter=SQUARE)

# ================================================================ shoes
SHOE = ["#202020", "#f0f0f0", "#8a5a3a", "#c83a3a", "#2a4a8a", "#e8b020", "#4a4a4a", "#d0c0a0"]
L, H, D = 2.0, 2.2, 0.4
def shoes_row(y):
    out = []
    for k in range(6):
        x = -0.8 + k * 0.32
        c = rnd.choice(SHOE)
        out += [part((0.11, 0.09, 0.26), (x - 0.06, y + 0.06, 0.02), c), part((0.11, 0.09, 0.26), (x + 0.06, y + 0.06, 0.02), c),
                part((0.24, 0.02, 0.26), (x, y + 0.025, 0.02), "white" if c != "white" else "#303030")]
    return out
ps = wall_unit(L, H, D, [0.3, 0.7, 1.1, 1.5, 1.9], shoes_row, color="#c8c4bc", back="#e8e4dc")
add(RETAIL, "Shoe Wall Display", "A wall of angled shelves showing single pairs of trainers, boots and heels. Solid, unbreakable.", ps,
    collider=[box(L, H, D)], scatter=SQUARE)

ps = [part((1.6, 0.45, 0.5), (0, 0.225, 0), "#5a3e28"), part((1.6, 0.08, 0.52), (0, 0.49, 0), "#2a3a5a"),
      part((0.3, 0.02, 0.12), (0.4, 0.01, 0.5), "#c0c0c0"), part((0.32, 0.12, 0.2), (-0.5, 0.59, 0), "#d8a040"),
      part((0.3, 0.1, 0.18), (-0.5, 0.7, 0.0), "#e8e0d0"), part((0.12, 0.08, 0.26), (0.3, 0.57, 0.05), "#c83a3a")]
add(RETAIL, "Shoe Fitting Bench", "A padded shoe-shop bench with boxes on it and a foot measure on the floor. Solid, breakable wood.", ps,
    collider=[box(1.6, 0.53, 0.52)], scatter=SQUARE, breakable=brk(70, "wood", 10))

ps = []
for i in range(rnd.randint(5, 7)):
    c = rnd.choice(["#d8a040", "#e8e0d0", "#c83a3a", "#202020", "#2a4a8a"])
    ps.append(part((0.34, 0.12, 0.2), (rnd.uniform(-0.02, 0.02), 0.06 + i * 0.121, 0), c, rotation=[0, rnd.uniform(-6, 6), 0]))
    ps.append(part((0.345, 0.02, 0.205), (0, 0.111 + i * 0.121, 0), "white"))
add(RETAIL, "Shoe Box Stack", "A leaning stack of shoe boxes. Cosmetic: walk through it.", ps, collision="cosmetic", scatter=LOOSE)

# ================================================================ sports
ps = [part((1.0, 0.06, 1.0), (0, 0.03, 0), "darkgrey")]
for s in (-1, 1):
    ps += [part((1.0, 0.9, 0.02), (0, 0.48, s * 0.49), "#7a7a80", material="grid"), part((0.02, 0.9, 1.0), (s * 0.49, 0.48, 0), "#7a7a80", material="grid")]
for k in range(14):
    c = rnd.choice(["#f07020", "#f0f0f0", "#e8d020", "#2050c0", "#c03030", "#50a040"])
    d = rnd.uniform(0.2, 0.26)
    ps += octo(d, d, 0.5 + (k // 7) * 0.22 + rnd.uniform(0, 0.05), c, x=rnd.uniform(-0.32, 0.32), z=rnd.uniform(-0.32, 0.32))
add(RETAIL, "Sports Ball Cage", "A wire cage full of basketballs, footballs and volleyballs. Solid, breakable metal.", ps[:128],
    collider=[box(1.0, 1.0, 1.0)], scatter=SQUARE, breakable=brk(120, "metal", 16, colors=["#f07020", "#f0f0f0", "#7a7a80"]))

ps = [part((0.8, 0.2, 1.9), (0, 0.1, 0), "#2a2a2e"), part((0.6, 0.03, 1.6), (0, 0.215, 0.1), "#101010"),
      part((0.05, 1.2, 0.05), (-0.36, 0.8, -0.75), "#5a5a60", rotation=[12, 0, 0]), part((0.05, 1.2, 0.05), (0.36, 0.8, -0.75), "#5a5a60", rotation=[12, 0, 0]),
      part((0.8, 0.25, 0.2), (0, 1.38, -0.86), "#3a3a40"), part((0.5, 0.15, 0.02), (0, 1.42, -0.755), "#40a0e0", rotation=[-30, 0, 0],
      anim={"type": "pulse", "period": 3, "color": "#60c0ff", "off": "#204060", "glow": 0.5}),
      part((0.76, 0.04, 0.04), (0, 1.1, -0.7), "#5a5a60")]
add(RETAIL, "Treadmill", "A running machine with a glowing console. Solid, breakable heavy metal.", ps,
    collider=[box(0.8, 0.3, 1.9), box(0.8, 1.5, 0.3, 0, -0.8)], scatter=SQUARE, breakable=brk(260, "metal", 14))

ps = [part((1.4, 0.05, 0.5), (0, 0.025, 0), "#2a2a2e"), part((0.06, 0.8, 0.5), (-0.67, 0.4, 0), "#2a2a2e"), part((0.06, 0.8, 0.5), (0.67, 0.4, 0), "#2a2a2e"),
      part((1.4, 0.04, 0.12), (0, 0.42, 0.1), "#5a5a60", rotation=[20, 0, 0]), part((1.4, 0.04, 0.12), (0, 0.72, -0.05), "#5a5a60", rotation=[20, 0, 0])]
for k in range(5):
    x = -0.5 + k * 0.25
    for y, z, s in ((0.5, 0.1, 0.9), (0.8, -0.05, 0.7)):
        ps += [part((0.06, 0.12 * s + 0.04, 0.14 * s + 0.04), (x - 0.06, y, z), "#202020"), part((0.06, 0.12 * s + 0.04, 0.14 * s + 0.04), (x + 0.06, y, z), "#202020"),
               part((0.12, 0.03, 0.03), (x, y, z), CHROME)]
add(RETAIL, "Dumbbell Rack", "A two-tier rack of rubber dumbbells. Solid, unbreakable.", ps, collider=[box(1.4, 0.85, 0.5)], scatter=SQUARE)

L, H, D = 2.0, 2.3, 0.5
ps = [part((L, H, 0.04), (0, H / 2, -D / 2 + 0.02), "#d8d4cc")]
for k in range(5):
    x = -0.8 + k * 0.4
    c = rnd.choice(["#c03030", "#2050c0", "#f0f0f0", "#208040", "#e8c020"])
    ps += [part((0.36, 0.5, 0.04), (x, 1.75, -0.2), c), part((0.12, 0.2, 0.04), (x - 0.2, 1.88, -0.2), c, rotation=[0, 0, 30]),
           part((0.12, 0.2, 0.04), (x + 0.2, 1.88, -0.2), c, rotation=[0, 0, -30]), part((0.14, 0.12, 0.01), (x, 1.75, -0.175), "white")]
ps += [part((L - 0.1, 0.03, 0.4), (0, 1.1, -0.05), WHITE_FIX)]
for k in range(7):
    ps += octo(0.2, 0.2, 1.115, rnd.choice(["#f07020", "#f0f0f0", "#e8d020"]), x=-0.8 + k * 0.27, z=-0.05)
ps += [part((L - 0.1, 0.03, 0.4), (0, 0.45, -0.05), WHITE_FIX)]
for k in range(5):
    ps.append(part((0.3, 0.3, 0.3), (-0.75 + k * 0.38, 0.615, -0.05), rnd.choice(["#202020", "#c03030", "#2050c0"])))
add(RETAIL, "Sports Jersey Wall", "A sports shop wall: team jerseys on the top row, balls and boxed kit on the shelves. Solid, unbreakable.", ps,
    collider=[box(L, H, D)], scatter=SQUARE)

# ================================================================ electronics
SCREEN = {"type": "flicker", "rate": 4, "duty": 0.93, "color": "#5aa8f0", "off": "#1a3050", "glow": 0.9}
L, H, D = 2.4, 2.4, 0.35
ps = [part((L, H, 0.05), (0, H / 2, -D / 2 + 0.025), "#2a2a30")]
for r, y in enumerate((0.85, 1.6)):
    for k in range(3):
        x = -0.78 + k * 0.78
        ps += [part((0.72, 0.44, 0.06), (x, y, -0.12), "#101014"), part((0.66, 0.38, 0.01), (x, y, -0.088), "#2050a0",
                anim=dict(SCREEN, color=["#5aa8f0", "#f0a050", "#60e090", "#e070c0", "#f0e060", "#70d0f0"][r * 3 + k], phase=0.13 * (r * 3 + k)))]
ps += [part((L, 0.4, 0.3), (0, 0.2, 0), "#3a3a40"), part((L - 0.4, 0.06, 0.01), (0, 0.3, 0.151), "#c02020")]
add(RETAIL, "TV Wall Display", "An electronics shop wall of six big flat screens showing different pictures. Solid, unbreakable: back it against a wall.", ps,
    collider=[box(L, H, D)], scatter=SQUARE)

def gadget_table(name, desc, items):
    ps = [part((1.6, 0.05, 0.9), (0, 0.9, 0), "#e8e8ec"), part((1.5, 0.88, 0.8), (0, 0.44, 0), "#d0d0d4"),
          part((1.52, 0.05, 0.82), (0, 0.025, 0), "#303034")]
    ps += items
    add(RETAIL, name, desc, ps, collider=[box(1.6, 0.93, 0.9)], scatter=SQUARE)


items = []
for k, x in enumerate((-0.5, 0.0, 0.5)):
    for z in (-0.2, 0.22):
        items += [part((0.32, 0.015, 0.22), (x, 0.935, z + 0.04), "#b8b8c0"), part((0.32, 0.22, 0.012), (x, 1.04, z - 0.07), "#a8a8b0", rotation=[-12, 0, 0]),
                  part((0.28, 0.18, 0.005), (x, 1.04, z - 0.062), "#2050a0", rotation=[-12, 0, 0], anim=dict(SCREEN, phase=0.2 * k + (0.1 if z > 0 else 0)))]
gadget_table("Laptop Display Table", "A white display table of open laptops with their screens lit. Solid, unbreakable.", items)
items = []
for x in (-0.6, -0.3, 0.0, 0.3, 0.6):
    for z in (-0.18, 0.18):
        items += [part((0.08, 0.06, 0.08), (x, 0.955, z), "#303034"), part((0.08, 0.15, 0.012), (x, 1.06, z), "#202024", rotation=[-15, 0, 0]),
                  part((0.07, 0.13, 0.004), (x, 1.06, z + 0.007), "#3060b0", rotation=[-15, 0, 0], anim=dict(SCREEN, rate=2, phase=round((x + z + 1.0) % 1.0, 3)))]
gadget_table("Phone Display Island", "A display island of phones on security stands, screens glowing. Solid, unbreakable.", items)

ps = []
for k, (w, h, y) in enumerate(((0.6, 0.9, 0.0), (0.5, 0.7, 0.9), (0.36, 0.5, 1.6))):
    ps += [part((w, h, 0.5), (0, y + h / 2, 0), "#1a1a1e"), part((w * 0.6, w * 0.6, 0.02), (0, y + h * 0.4, 0.255), "#3a3a40"),
           part((w * 0.3, w * 0.3, 0.02), (0, y + h * 0.4, 0.265), "#606068"), part((w * 0.2, w * 0.2, 0.02), (0, y + h * 0.82, 0.258), "#3a3a40")]
add(RETAIL, "Speaker Display Stack", "A stack of three black hi-fi speakers, biggest at the bottom. Solid, breakable.", ps,
    collider=[box(0.6, 2.1, 0.5)], scatter=SQUARE, breakable=brk(110, "wood", 12, colors=["#1a1a1e", "#3a3a40"]))

# ================================================================ toys
ps = octo(1.1, 0.6, 0, "#e04040") + octo(1.0, 0.04, 0.6, "#f0d040")
for k in range(16):
    c = rnd.choice(["#c89060", "#f0e0c0", "#e87aa0", "#80c0e0", "#a0d070", "#f0f0f0"])
    s = rnd.uniform(0.18, 0.26)
    ps += [part((s, s, s * 0.8), (rnd.uniform(-0.3, 0.3), 0.6 + (k // 8) * 0.12 + s / 2, rnd.uniform(-0.3, 0.3)), c, rotation=[rnd.uniform(-20, 20), rnd.uniform(0, 90), 0]),
           part((s * 0.4, s * 0.4, s * 0.3), (0, 0, 0), c)]
    ps[-1]["offset"] = [ps[-2]["offset"][0], R(ps[-2]["offset"][1] + s * 0.55), ps[-2]["offset"][2]]
add(RETAIL, "Plush Toy Bin", "A round bin heaped with soft toys. Solid, breakable (soft).", ps,
    collider=[box(1.1, 0.75, 1.1)], scatter=LOOSE, breakable=brk(30, "soft", 16, colors=["#e04040", "#c89060", "#e87aa0", "#80c0e0"]))

ps = [part((0.5, 0.25, 0.85), (0, 0.24, 0), "#e02020"), part((0.46, 0.2, 0.4), (0, 0.46, -0.08), "#e02020"),
      part((0.4, 0.14, 0.02), (0, 0.48, 0.13), "#a0d0f0"), part((0.3, 0.05, 0.3), (0, 0.38, -0.15), "#202020")]
for x in (-0.24, 0.24):
    for z in (-0.28, 0.28):
        ps += octo_lying(0.18, 0.08, x, 0.09, z, "#202020")
add(RETAIL, "Ride-On Toy Car", "A little red plastic ride-on car for kids. Cosmetic.", ps, collision="cosmetic", scatter=LOOSE)

# ================================================================ jewellery
GLASS = "#c8dce4"
def glass_case(L, H, D, inner):
    ps = [part((L, H - 0.35, D), (0, (H - 0.35) / 2, 0), "#2a2a30"), part((L, 0.04, D), (0, H - 0.33, 0), "#e8e0d0"),
          part((L, 0.33, 0.02), (0, H - 0.165, D / 2 - 0.01), GLASS), part((L, 0.33, 0.02), (0, H - 0.165, -D / 2 + 0.01), GLASS),
          part((0.02, 0.33, D), (L / 2 - 0.01, H - 0.165, 0), GLASS), part((0.02, 0.33, D), (-L / 2 + 0.01, H - 0.165, 0), GLASS),
          part((L, 0.02, D), (0, H, 0), GLASS), part((L - 0.1, 0.03, 0.02), (0, H - 0.36, D / 2 + 0.005), "#d8b040", anim=LIT("#f0d070", 0.5))]
    return ps + inner


inner = []
for x in (-0.55, -0.18, 0.18, 0.55):
    inner += [part((0.3, 0.03, 0.4), (x, 0.66, 0), "#20203a"), part((0.06, 0.03, 0.06), (x - 0.06, 0.69, -0.05), "#e8d060"),
              part((0.05, 0.05, 0.02), (x + 0.07, 0.7, 0.08), "#f0f0ff"), part((0.08, 0.02, 0.05), (x, 0.69, 0.1), "#e8d060")]
add(RETAIL, "Jewellery Display Case", "A low glass-topped counter of rings, necklaces and watches on velvet, lit gold. Solid, breakable glass.",
    glass_case(1.5, 1.0, 0.6, inner), collider=[box(1.5, 1.0, 0.6)], scatter=SQUARE, breakable=brk(35, "glass", 18, colors=[GLASS, "#2a2a30", "#e8d060"]))
ps = [part((0.8, 0.9, 0.5), (0, 0.45, 0), "#2a2a30"), part((0.8, 1.0, 0.02), (0, 1.4, 0.24), GLASS), part((0.02, 1.0, 0.5), (0.39, 1.4, 0), GLASS),
      part((0.02, 1.0, 0.5), (-0.39, 1.4, 0), GLASS), part((0.8, 1.0, 0.03), (0, 1.4, -0.235), "#20203a"), part((0.8, 0.12, 0.5), (0, 1.96, 0), "#2a2a30"),
      part((0.7, 0.03, 0.02), (0, 1.88, 0.24), "#d8b040", anim=LIT("#f0d070", 0.6))]
for y in (1.05, 1.4, 1.72):
    ps.append(part((0.74, 0.02, 0.4), (0, y, 0), GLASS))
    for x in (-0.22, 0.0, 0.22):
        ps.append(part((0.07, 0.07, 0.03), (x, y + 0.05, -0.05), rnd.choice(["#e8d060", "#f0f0ff", "#c0c0d0", "#d04060"])))
add(RETAIL, "Jewellery Tower Case", "A tall lit glass cabinet of watches and jewellery on three shelves. Solid, breakable glass.", ps,
    collider=[box(0.8, 2.02, 0.5)], scatter=SQUARE, breakable=brk(40, "glass", 18, colors=[GLASS, "#2a2a30", "#e8d060"]))

# ================================================================ pets
ps = [part((2.0, 0.05, 0.5), (0, 0.025, 0), "#6a6a70")]
for x in (-0.98, 0.98):
    ps.append(part((0.04, 2.0, 0.5), (x, 1.0, 0), "#6a6a70"))
for r, y in enumerate((0.25, 0.9, 1.55)):
    ps.append(part((2.0, 0.04, 0.5), (0, y, 0), "#6a6a70"))
    for k, x in enumerate((-0.48, 0.48)):
        ps += [part((0.9, 0.55, 0.42), (x, y + 0.3, 0), "#3a7a9a", anim={"type": "pulse", "period": 4 + k, "color": "#4a9ac0", "off": "#2a6a8a", "glow": 0.5, "phase": 0.3 * r}),
               part((0.9, 0.02, 0.42), (x, y + 0.585, 0), "#202020"), part((0.88, 0.04, 0.4), (x, y + 0.04, 0), "#c8b080")]
        for f in range(2):
            ps.append(part((0.06, 0.04, 0.015), (x - 0.2 + f * 0.35, y + 0.28 + f * 0.12, 0.1), rnd.choice(["#f08020", "#f0d020", "#e04040", "#40a0f0"]),
                           anim={"type": "bob", "axis": "x", "distance": 0.12, "period": 5 + f * 1.3}))
add(RETAIL, "Aquarium Rack", "A steel rack of six glowing fish tanks with fish swimming to and fro. Solid, breakable glass.", ps,
    collider=[box(2.0, 2.1, 0.5)], scatter=SQUARE, breakable=brk(45, "glass", 20, colors=["#3a7a9a", GLASS, "#6a6a70"]))

ps = []
for r in range(3):
    for k in range(2):
        x, y = -0.32 + k * 0.64, r * 0.6
        ps += [part((0.6, 0.55, 0.45), (x, y + 0.3, 0), "#8a8a90", material="grid"), part((0.58, 0.05, 0.43), (x, y + 0.05, 0), "#c8b080"),
               part((0.12, 0.1, 0.18), (x + rnd.uniform(-0.15, 0.15), y + 0.12, 0), rnd.choice(["#c8a070", "#f0f0f0", "#6a4a30", "#d8c8b0"]))]
add(RETAIL, "Small Animal Cages", "Stacked wire cages of rabbits, guinea pigs and hamsters on sawdust. Solid, breakable metal.", ps,
    collider=[box(1.25, 1.8, 0.45)], scatter=SQUARE, breakable=brk(100, "metal", 14))

ps = octo(0.06, 1.2, 0, "#3a3a40") + octo(0.5, 0.03, 0, "#3a3a40") + octo(0.5, 0.6, 1.2, "#c8c8c8", material="grid") + \
     octo(0.5, 0.04, 1.2, "#c8b080") + [part((0.09, 0.12, 0.08), (0.05, 1.45, 0), "#30c060"), part((0.04, 0.03, 0.06), (0.05, 1.5, 0.06), "#f0c020")]
add(RETAIL, "Bird Cage Stand", "A round bird cage on a stand, a budgie inside. Cosmetic.", ps, collision="cosmetic", scatter=LOOSE)

# ================================================================ beauty
L, H, D = 2.0, 2.3, 0.45
ps = [part((L, H, 0.05), (0, H / 2, -D / 2 + 0.025), "#f4ece8"), part((L, 0.9, D), (0, 0.45, 0), "#202024"),
      part((L, 0.04, D), (0, 0.92, 0), "#f0f0f0")]
for y in (1.25, 1.6, 1.95):
    ps.append(part((L - 0.1, 0.02, 0.22), (0, y, -0.1), GLASS))
    for k in range(10):
        ps.append(part((0.06, rnd.uniform(0.08, 0.16), 0.06), (-0.85 + k * 0.19, y + 0.07, -0.1), rnd.choice(["#d03060", "#202020", "#e8c8b0", "#f0a0b0", "#a02040", "#f0e0e0", "#c09060"])))
ps += [part((L - 0.2, 0.04, 0.02), (0, 2.22, -0.18), "#ffffff", anim=LIT("#fff4e8", 1.0))]
add(RETAIL, "Cosmetics Wall Unit", "A beauty counter wall: lipsticks, bottles and compacts on lit glass shelves over a black base. Solid, unbreakable.", ps,
    collider=[box(L, H, D)], scatter=SQUARE)
ps = [part((1.4, 0.9, 0.7), (0, 0.45, 0), "#202024"), part((1.42, 0.04, 0.72), (0, 0.92, 0), "#f0f0f0")]
for x in (-0.4, 0.4):
    ps += [part((0.36, 0.46, 0.03), (x, 1.2, -0.25), "#e8e8ec"), part((0.3, 0.4, 0.01), (x, 1.2, -0.232), "#c8dce4")]
    for b in range(4):
        ps += [part((0.05, 0.05, 0.02), (x - 0.18 + b * 0.12, 1.45, -0.233), "#fff8e0", anim=LIT("#fff8e0", 1.0))]
for k in range(9):
    ps.append(part((0.05, rnd.uniform(0.06, 0.14), 0.05), (-0.6 + k * 0.15, 0.98, 0.12), rnd.choice(["#d03060", "#f0a0b0", "#a02040", "#e8c8b0"])))
add(RETAIL, "Makeup Counter", "An island make-up counter with two light-bulb mirrors and tester pots. Solid, unbreakable.", ps,
    collider=[box(1.4, 0.95, 0.7)], scatter=SQUARE)
ps = octo(0.9, 0.85, 0, "#f4ece8") + octo(0.94, 0.03, 0.85, GLASS)
for k in range(10):
    a = k * 36
    x, z = math.sin(math.radians(a)) * 0.28, math.cos(math.radians(a)) * 0.28
    ps += [part((0.07, 0.13, 0.05), (x, 0.95, z), rnd.choice(["#e8c060", "#f0a0b0", "#a0c0e0", "#c0e0c0"])), part((0.03, 0.03, 0.03), (x, 1.03, z), "#d8b040")]
add(RETAIL, "Perfume Display Table", "A round white plinth of perfume bottles. Solid, breakable glass.", ps,
    collider=[box(0.9, 0.9, 0.9)], scatter=LOOSE, breakable=brk(25, "glass", 16, colors=["#f4ece8", "#e8c060", "#f0a0b0"]))

# ================================================================ candy
L, H, D = 2.0, 2.0, 0.6
ps = [part((L, H, 0.05), (0, H / 2, -D / 2 + 0.025), "#f8e8f0"), part((L, 0.5, D), (0, 0.25, 0), "#e04080")]
for r in range(4):
    y = 0.55 + r * 0.36
    for k in range(5):
        x = -0.8 + k * 0.4
        c = rnd.choice(["#ff4060", "#ffd020", "#40c0ff", "#60e060", "#ff80e0", "#a060ff", "#ff9020", "#ffffff"])
        ps += [part((0.36, 0.3, 0.42 - r * 0.06), (x, y + 0.15, -0.05 + r * 0.0), GLASS), part((0.32, 0.2, 0.38 - r * 0.06), (x, y + 0.11, -0.05), c)]
add(RETAIL, "Candy Bin Wall", "A pick-and-mix wall: rows of clear bins of bright sweets. Solid, unbreakable.", ps, collider=[box(L, H, D)], scatter=SQUARE)
ps = [part((1.2, 0.8, 0.7), (0, 0.4, 0), "#ffffff"), part((1.24, 0.04, 0.74), (0, 0.82, 0), "#e04080")]
for k in range(6):
    x = -0.45 + (k % 3) * 0.45
    z = -0.15 if k < 3 else 0.17
    c = rnd.choice(["#ff4060", "#ffd020", "#40c0ff", "#60e060", "#ff80e0"])
    ps += octo(0.22, 0.3, 0.84, GLASS, x=x, z=z) + [part((0.18, 0.2, 0.18), (x, 0.95, z), c)]
for k in range(5):
    x = -0.5 + k * 0.25
    ps += [part((0.02, 0.35, 0.02), (x, 1.05, 0.33), "white"), part((0.14, 0.14, 0.03), (x, 1.27, 0.33), rnd.choice(["#ff4060", "#40c0ff", "#ffd020"]))]
add(RETAIL, "Candy Display Table", "A sweet-shop table of tall jars and lollipops. Solid, breakable glass.", ps,
    collider=[box(1.24, 0.86, 0.74)], scatter=SQUARE, breakable=brk(25, "glass", 18, colors=["#ffffff", "#ff4060", "#40c0ff"]))

# ================================================================ arcade
ps = [part((0.9, 0.9, 0.9), (0, 0.45, 0), "#c02060"), part((0.9, 1.0, 0.04), (0, 1.4, 0.43), GLASS), part((0.9, 1.0, 0.04), (0, 1.4, -0.43), GLASS),
      part((0.04, 1.0, 0.9), (0.43, 1.4, 0), GLASS), part((0.04, 1.0, 0.9), (-0.43, 1.4, 0), GLASS), part((0.94, 0.3, 0.94), (0, 2.05, 0), "#c02060"),
      part((0.8, 0.18, 0.02), (0, 2.05, 0.475), "#ffe040", anim={"type": "cycle", "period": 1.2, "steps": 4, "on": [0, 2], "color": "#fff080", "off": "#806010", "glow": 1.2}),
      part((0.04, 0.3, 0.04), (0.1, 1.75, 0.05), CHROME, anim={"type": "bob", "axis": "x", "distance": 0.18, "period": 4.2}),
      part((0.14, 0.08, 0.14), (0.1, 1.58, 0.05), CHROME, anim={"type": "follow", "parent": 8})]
for k in range(10):
    ps.append(part((0.14, 0.14, 0.12), (rnd.uniform(-0.3, 0.3), 0.98, rnd.uniform(-0.3, 0.3)), rnd.choice(["#c89060", "#e87aa0", "#80c0e0", "#a0d070"])))
ps += [part((0.12, 0.04, 0.06), (-0.2, 0.92, 0.47), "#202020"), part((0.05, 0.05, 0.03), (0.15, 0.85, 0.47), "#30ff60", anim={"type": "blink", "period": 0.8, "color": "#30ff60", "off": "#0a3010", "glow": 1})]
add(RETAIL, "Claw Machine", "A glass-sided claw crane full of soft toys, its marquee chasing and the claw sliding back and forth. Solid, breakable.", ps,
    collider=[box(0.94, 2.2, 0.94)], scatter=SQUARE, breakable=brk(140, "metal", 16, colors=["#c02060", GLASS, "#c89060"]))
ps = [part((2.0, 0.75, 1.1), (0, 0.375, 0), "#2050c0"), part((1.9, 0.04, 1.0), (0, 0.77, 0), "#f0f4f8"),
      part((1.9, 0.08, 0.05), (0, 0.8, 0.525), "#e04040"), part((1.9, 0.08, 0.05), (0, 0.8, -0.525), "#e04040"),
      part((0.05, 0.08, 1.1), (0.975, 0.8, 0), "#e04040"), part((0.05, 0.08, 1.1), (-0.975, 0.8, 0), "#e04040"),
      part((0.02, 0.005, 1.0), (0, 0.792, 0), "#e04040")]
ps += octo(0.1, 0.03, 0.79, "#202020", x=0.2, z=0.1) + octo(0.16, 0.08, 0.79, "#e04040", x=-0.6) + octo(0.16, 0.08, 0.79, "#e04040", x=0.6)
add(RETAIL, "Air Hockey Table", "An air hockey table, mallets and puck on it. Solid, unbreakable: you can mantle onto it.", ps,
    collider=[box(2.0, 0.85, 1.1)], scatter=SQUARE)
ps = [part((2.0, 1.0, 0.6), (0, 0.5, 0), "#4a2a6a"), part((2.0, 0.04, 0.6), (0, 1.02, 0), "#f0f0f0"), part((1.96, 0.5, 0.02), (0, 0.6, 0.31), GLASS)]
for k in range(8):
    ps.append(part((0.16, 0.16, 0.16), (-0.85 + k * 0.24, 0.45, 0.05), rnd.choice(["#c89060", "#e87aa0", "#80c0e0", "#a0d070", "#ffd020"])))
for k in range(6):
    ps.append(part((0.22, 0.28, 0.2), (-0.7 + k * 0.28, 1.18, -0.15), rnd.choice(["#c89060", "#e87aa0", "#80c0e0", "#a0d070", "#ff4060"])))
add(RETAIL, "Prize Counter", "An arcade prize counter: a glass front of toys and big plush prizes on top. Solid, unbreakable.", ps,
    collider=[box(2.0, 1.05, 0.6)], scatter=SQUARE)

# ================================================================ food court
def chair(x, z, yaw, c):
    a = math.radians(yaw)
    bx, bz = x - math.sin(a) * 0.18, z - math.cos(a) * 0.18
    return [part((0.42, 0.04, 0.42), (x, 0.46, z), c, rotation=[0, yaw, 0]), part((0.42, 0.4, 0.04), (bx, 0.68, bz), c, rotation=[0, yaw, 0]),
            part((0.04, 0.44, 0.04), (x, 0.22, z), CHROME)]


for nm, c, desc in (("Food Court Table Set", "#d84a30", "red"), ("Food Court Table Set Teal", "#2a8a8a", "teal")):
    ps = [part((0.9, 0.04, 0.9), (0, 0.74, 0), "#e8e4dc"), part((0.08, 0.72, 0.08), (0, 0.36, 0), CHROME), part((0.5, 0.03, 0.5), (0, 0.015, 0), "#404044")]
    ps += chair(0, 0.68, 180, c) + chair(0, -0.68, 0, c) + chair(0.68, 0, 270, c) + chair(-0.68, 0, 90, c)
    ps += [part((0.3, 0.02, 0.2), (0.15, 0.77, 0.1), "#c03020"), part((0.07, 0.15, 0.07), (-0.2, 0.83, -0.15), "#e8e0d0")]
    add(RETAIL, nm, f"A square food-court table with four {desc} chairs and a tray. Solid, breakable.", ps,
        collider=[box(0.9, 0.78, 0.9), box(1.8, 0.48, 0.5), box(0.5, 0.48, 1.8)], scatter=SQUARE,
        breakable=brk(90, "metal", 14, colors=["#e8e4dc", c, "#c0c0c0"]))

L, H, D = 3.0, 1.1, 0.8
ps = [part((L, H, D), (0, H / 2, 0), "#d8d4cc"), part((L + 0.06, 0.05, D + 0.06), (0, H + 0.025, 0), "#3a3a40"),
      part((L, 0.2, 0.02), (0, 0.9, D / 2 + 0.01), "#c03020"), part((0.5, 0.3, 0.35), (-0.9, H + 0.2, 0), "#b8b8c0"),
      part((0.4, 0.04, 0.3), (0.2, H + 0.07, 0), "#e8c040"), part((0.1, 0.25, 0.1), (0.9, H + 0.17, 0.1), "#c03030"), part((0.1, 0.25, 0.1), (1.05, H + 0.17, 0.1), "#e8c020")]
add(RETAIL, "Food Stall Counter", "A food-court serving counter with a till, a hot cabinet and sauce bottles. Solid, unbreakable cover.", ps,
    collider=[box(L, H + 0.05, D)], scatter=SQUARE)
L, H, D = 3.0, 2.6, 0.8
ps = [part((L, 0.9, D), (0, 0.45, 0), "#b8b8c0", material="metal"), part((L, 0.04, D), (0, 0.92, 0), "#d0d0d8"),
      part((0.9, 0.06, 0.6), (-0.9, 0.97, 0), "#202020"), part((0.6, 0.3, 0.5), (0.3, 1.09, 0), "#9a9aa4"), part((0.5, 0.05, 0.4), (0.3, 1.25, 0), "#e0a020",
      anim={"type": "pulse", "period": 2.4, "color": "#f0c040", "off": "#806020", "glow": 0.4}),
      part((0.6, 0.6, 0.5), (1.1, 1.24, -0.1), "#d0d0d8"), part((L, 0.5, 0.9), (0, 2.35, -0.05), "#a8a8b0", material="metal"),
      part((L, H, 0.04), (0, H / 2, -D / 2 + 0.02), "#e8e4dc", material="tiles")]
add(RETAIL, "Food Stall Grill Line", "The kitchen line behind a food-court counter: a grill, a fryer and a hood. Solid, unbreakable: back it against a wall.", ps,
    collider=[box(L, 1.0, D)], scatter=SQUARE)
ps = [part((1.4, 1.1, 0.6), (0, 0.55, 0), "#9a9aa4", material="metal"), part((1.2, 0.5, 0.04), (0, 0.8, 0.29), "#202020")]
for k in range(4):
    ps.append(part((0.45, 0.03, 0.35), (0.0, 1.12 + k * 0.04, 0.0), rnd.choice(["#c03020", "#2a8a8a", "#e8c020"])))
ps += [part((0.3, 0.6, 0.02), (0, 1.5, -0.29), "#3a8a3a")]
add(RETAIL, "Tray Return Station", "A steel tray return and bin unit with a stack of trays on top. Solid, unbreakable.", ps, collider=[box(1.4, 1.15, 0.6)], scatter=SQUARE)

# ================================================================ cinema
ps = [part((0.8, 0.9, 0.6), (0, 0.45, 0), "#c02020"), part((0.76, 0.75, 0.56), (0, 1.3, 0), GLASS), part((0.8, 0.15, 0.6), (0, 1.75, 0), "#c02020"),
      part((0.7, 0.35, 0.5), (0, 1.1, 0), "#f0e090"), part((0.8, 0.12, 0.02), (0, 1.75, 0.305), "#ffe040", anim=LIT("#fff080", 1.0)),
      part((0.2, 0.15, 0.2), (0, 1.55, 0), "#808088")]
add(RETAIL, "Popcorn Machine", "A red popcorn cart with a glass cabinet heaped with popcorn. Solid, breakable glass.", ps,
    collider=[box(0.8, 1.82, 0.6)], scatter=SQUARE, breakable=brk(40, "glass", 18, colors=["#c02020", "#f0e090", GLASS]))
ps = [part((2.4, 1.1, 0.7), (0, 0.55, 0), "#3a1a2a"), part((2.44, 0.05, 0.74), (0, 1.12, 0), "#d8b040"), part((2.3, 0.08, 0.02), (0, 0.95, 0.36), "#ffe040", anim=LIT("#fff080", 0.8)),
      part((0.3, 0.3, 0.04), (-0.6, 1.3, -0.1), "#202020", rotation=[-10, 0, 0]), part((0.3, 0.3, 0.04), (0.6, 1.3, -0.1), "#202020", rotation=[-10, 0, 0])]
add(RETAIL, "Box Office Counter", "A cinema box office counter in plum and gold with two ticket screens. Solid, unbreakable.", ps,
    collider=[box(2.4, 1.15, 0.7)], scatter=SQUARE)
ps = []
for k in range(6):
    x = -1.5 + k * 0.6
    ps += [part((0.55, 0.45, 0.55), (x, 0.225, 0), "#8a1a28"), part((0.55, 0.65, 0.12), (x, 0.65, -0.24), "#8a1a28", rotation=[-8, 0, 0]),
           part((0.06, 0.62, 0.55), (x + 0.3, 0.31, 0), "#202020")]
ps.append(part((0.06, 0.62, 0.55), (-1.8, 0.31, 0), "#202020"))
add(RETAIL, "Auditorium Seat Row", "A row of six plush red cinema seats. Solid, unbreakable cover (mantle over it).", ps,
    collider=[box(3.6, 0.95, 0.6)], scatter=SQUARE)
ps = [part((8.0, 3.6, 0.1), (0, 2.5, -0.05), "#e8e8f0", anim={"type": "flicker", "rate": 2, "duty": 0.9, "color": "#f0f0ff", "off": "#9090a8", "glow": 0.7}),
      part((8.4, 0.3, 0.15), (0, 4.45, 0), "#202020"), part((8.4, 0.3, 0.15), (0, 0.55, 0), "#202020"),
      part((0.6, 4.2, 0.2), (-4.4, 2.4, 0.05), "#6a1020"), part((0.6, 4.2, 0.2), (4.4, 2.4, 0.05), "#6a1020")]
add(RETAIL, "Cinema Big Screen", "A big cinema screen (8 m) lit by the film, between red curtains. Cosmetic: hang it on a wall.", ps, collision="cosmetic")
ps = octo(0.08, 0.95, 0, CHROME) + octo(0.3, 0.04, 0, CHROME) + octo(0.08, 0.95, 0, CHROME, x=1.4) + octo(0.3, 0.04, 0, CHROME, x=1.4) + \
     [part((1.4, 0.05, 0.05), (0.7, 0.85, 0), "#a01020", rotation=[0, 0, 0]), part((0.6, 0.05, 0.05), (0.35, 0.8, 0), "#a01020", rotation=[0, 0, -8]),
      part((0.6, 0.05, 0.05), (1.05, 0.8, 0), "#a01020", rotation=[0, 0, 8])]
for p in ps:
    p["offset"][0] = R(p["offset"][0] - 0.7)
add(RETAIL, "Queue Rope Barrier", "Two chrome posts and a red rope for a queue. Cosmetic.", ps, collision="cosmetic", scatter=SQUARE)

# ================================================================ mall common areas
ps = [part((2.0, 0.08, 0.6), (0, 0.46, 0), "#6a4a30", material="wood"), part((2.0, 0.5, 0.08), (0, 0.75, -0.26), "#6a4a30", material="wood")]
for x in (-0.85, 0.85):
    ps += [part((0.08, 0.42, 0.5), (x, 0.21, 0), "#3a3a40"), part((0.08, 0.5, 0.08), (x, 0.7, -0.27), "#3a3a40")]
add(RETAIL, "Mall Bench", "A wooden-slatted mall bench on dark steel legs. Solid, unbreakable.", ps, collider=[box(2.0, 0.5, 0.6)], scatter=SQUARE)

ps = [part((1.4, 0.7, 1.4), (0, 0.35, 0), "#c8c0b0", material="concrete"), part((1.3, 0.04, 1.3), (0, 0.69, 0), "#4a3a28"),
      part((1.44, 0.06, 1.44), (0, 0.72, 0), "#a8a090")]
ps += octo(0.2, 2.6, 0.7, "#8a6a40")
for a in range(0, 360, 45):
    rad = math.radians(a)
    ps.append(part((1.4, 0.06, 0.28), (math.sin(rad) * 0.6, 3.15, math.cos(rad) * 0.6), "#3a7a30", rotation=[0, (a + 90) % 360, -22]))
for a in range(22, 360, 90):
    rad = math.radians(a)
    ps.append(part((1.0, 0.05, 0.24), (math.sin(rad) * 0.42, 3.35, math.cos(rad) * 0.42), "#4a8a38", rotation=[0, (a + 90) % 360, -35]))
ps += [part((0.5, 0.25, 0.5), (0.3, 0.84, 0.3), "#3a7a30"), part((0.4, 0.2, 0.4), (-0.35, 0.82, -0.25), "#4a8a38")]
add(RETAIL, "Mall Planter Palm", "A square stone planter with a potted palm, for atriums and lobbies. Solid (the planter), unbreakable.", ps,
    collider=[box(1.44, 0.75, 1.44)], scatter=SQUARE)
ps = [part((3.0, 0.6, 1.0), (0, 0.3, 0), "#c8c0b0", material="concrete"), part((3.04, 0.06, 1.04), (0, 0.62, 0), "#a8a090")]
for k in range(7):
    s = rnd.uniform(0.4, 0.6)
    ps.append(part((s, s * 0.8, s), (-1.2 + k * 0.4, 0.62 + s * 0.4, rnd.uniform(-0.15, 0.15)), rnd.choice(["#3a7a30", "#4a8a38", "#2f6a2a"]), rotation=[0, rnd.uniform(0, 90), 0]))
ps += [part((0.12, 0.12, 0.12), (-0.6, 1.0, 0.2), "#f0a0c0"), part((0.12, 0.12, 0.12), (0.5, 1.0, -0.2), "#f0e060")]
add(RETAIL, "Mall Planter Box", "A long stone planter of clipped shrubs and a few flowers. Solid, unbreakable: you can see over it.", ps,
    collider=[box(3.04, 0.65, 1.04)], scatter=SQUARE)

ps = [part((1.1, 2.0, 0.3), (0, 1.0, 0), "#3a3a40"), part((1.0, 1.5, 0.02), (0, 1.15, 0.16), "#e8e8ec", anim=LIT("#f4f4f8", 0.5)),
      part((1.0, 0.25, 0.02), (0, 1.75, 0.165), "#2a5a9a")]
for k in range(8):
    ps.append(part((0.18, 0.12, 0.01), (-0.35 + (k % 4) * 0.23, 1.45 - (k // 4) * 0.5, 0.172), rnd.choice(STOCK_BRIGHT[:8])))
ps += [part((0.06, 0.06, 0.012), (0.1, 1.0, 0.172), "#e02020", anim={"type": "blink", "period": 1.2, "color": "#ff3030", "off": "#601010", "glow": 1})]
ps += pk.text_parts("MALL MAP", 0.022, 0, 1.75, 0.18, 0.01, "white")
add(RETAIL, "Mall Directory Board", "A free-standing lit mall directory: a floor plan with coloured shops and a red 'you are here' dot. Solid, unbreakable.", ps,
    collider=[box(1.1, 2.0, 0.3)], scatter=SQUARE)

ps = [part((2.0, 0.9, 0.9), (0, 0.45, 0), "#f0f0f0"), part((2.1, 0.05, 1.0), (0, 0.92, 0), "#2a6ab0")]
for x in (-0.95, 0.95):
    for z in (-0.4, 0.4):
        ps.append(part((0.05, 1.4, 0.05), (x, 1.6, z), CHROME))
ps += [part((2.2, 0.1, 1.1), (0, 2.35, 0), "#2a6ab0"), part((2.1, 0.25, 0.02), (0, 2.2, 0.51), "#f0d040")]
for k in range(10):
    ps.append(part((0.12, 0.05, 0.05), (-0.8 + k * 0.18, 1.3, 0.0), rnd.choice(["#202020", "#a04020", "#2050a0", "#c0a020"])))
ps += [part((1.8, 0.02, 0.5), (0, 1.27, 0), GLASS)]
for k in range(8):
    ps.append(part((0.16, 0.08, 0.1), (-0.7 + k * 0.2, 0.98, 0.1), rnd.choice(["#202020", "#3060c0", "#e04060", "#40b060"])))
add(RETAIL, "Mall Kiosk Cart", "A mall cart kiosk with a canopy, selling sunglasses and phone cases. Solid, unbreakable.", ps,
    collider=[box(2.0, 1.0, 0.9)], scatter=SQUARE)

ps = octo(0.5, 0.95, 0, "#3a3a40", material="metal") + octo(0.54, 0.06, 0.95, "#8a8a90") + [part((0.2, 0.06, 0.06), (0, 0.85, 0.24), "#202020")]
add(RETAIL, "Mall Trash Bin", "A round dark steel mall litter bin. Solid, breakable metal.", ps, collider=[box(0.5, 1.0, 0.5)], scatter=LOOSE,
    breakable=brk(90, "metal", 10))

# The atrium fountain: a wide basin with a tiered centre and jets.
ps = octo(5.0, 0.5, 0, "#c8c0b0", material="concrete") + octo(4.6, 0.04, 0.44, "#3a7aa0")
ps += octo(1.6, 0.9, 0.0, "#c8c0b0", material="concrete") + octo(2.4, 0.08, 0.95, "#c8c0b0") + octo(2.2, 0.03, 0.99, "#3a7aa0")
ps += octo(0.4, 0.7, 1.0, "#c8c0b0") + octo(1.0, 0.06, 1.7, "#c8c0b0")
ps += [part((0.14, 0.9, 0.14), (0, 2.1, 0), "#a8d4f0", anim={"type": "bob", "distance": 0.2, "period": 1.8}),
       part((0.3, 0.12, 0.3), (0, 2.6, 0), "#d0e8ff", anim={"type": "bob", "distance": 0.2, "period": 1.8})]
for k in range(8):
    a = math.radians(k * 45 + 22.5)
    x, z = math.sin(a) * 1.9, math.cos(a) * 1.9
    ps.append(part((0.06, 0.6, 0.06), (x, 0.75, z), "#a8d4f0", anim={"type": "bob", "distance": 0.12, "period": 1.1, "phase": k / 8}))
    ps.append(part((0.1, 0.02, 0.1), (math.sin(a) * 1.5, 0.47, math.cos(a) * 1.5), "white", anim={"type": "pulse", "period": 1.3, "color": "white", "off": "#5090b0", "glow": 0.3, "phase": k / 8}))
add(RETAIL, "Mall Fountain", "A big round atrium fountain (5 m): a stone basin, a tiered centre and arcing jets. Solid (the basin wall is low cover), unbreakable.", ps,
    collider=[box(3.6, 0.5, 3.6), box(1.2, 1.7, 1.2)], scatter=SQUARE)

# Escalator balustrades for a 4.6 m rise over 5.85 m (the steps themselves are solid blocks of the map or building).
RISE, RUN = 4.6, 5.85
ang = math.degrees(math.atan2(RISE, RUN))
slope = math.hypot(RISE, RUN)
ps = []
for s in (-1, 1):
    x = s * 0.72
    ps += [part((0.1, 1.0, slope + 0.6), (x, RISE / 2 + 0.5, 0), "#c8dce4", rotation=[ang, 0, 0]),
           part((0.14, 0.08, slope + 1.4), (x, RISE / 2 + 1.04, 0), "#202020", rotation=[ang, 0, 0]),
           part((0.16, 0.3, slope + 0.6), (x, RISE / 2 - 0.05, 0), "#9a9aa4", rotation=[ang, 0, 0], material="metal"),
           part((0.14, 1.04, 0.9), (x, 0.52, RUN / 2 + 0.55), "#9a9aa4", material="metal"), part((0.14, 1.04, 0.9), (x, RISE + 0.52, -RUN / 2 - 0.55), "#9a9aa4", material="metal")]
ps += [part((1.3, 0.02, 0.6), (0, 0.01, RUN / 2 + 0.4), "#b8b8c0", material="metal"), part((1.3, 0.02, 0.6), (0, RISE + 0.01, -RUN / 2 - 0.4), "#b8b8c0", material="metal")]
ps += [part((1.34, 0.6, slope), (0, RISE / 2 - 0.2, 0.12), "#8a8a94", rotation=[ang, 0, 0], material="metal")]
add(RETAIL, "Escalator Balustrade", "The glass sides, black handrails and steel skirt of a mall escalator climbing 4.6 m over 5.85 m towards -Z. Cosmetic: the steps are separate solid blocks.", ps, collision="cosmetic")

ps = [part((1.2, 0.08, 0.6), (0, 0.04, 0), "#e8e8ec")]
for x in (-0.55, 0.55):
    ps.append(part((0.04, 0.04, 0.5), (x, 0.08, 0), CHROME))
ps += [part((0.6, 0.2, 0.6), (0, 0.18, 0), "#f0f0f0", anim=LIT("#ffffff", 0.6))]
ps = [part((1.6, 0.06, 0.6), (0, 0.06, 0), "#d8d8dc"), part((1.5, 0.03, 0.5), (0, 0.02, 0), "#ffffff", anim=LIT("#fffbf0", 1.1)),
      part((0.03, 0.6, 0.03), (-0.6, 0.39, 0), "#606068"), part((0.03, 0.6, 0.03), (0.6, 0.39, 0), "#606068")]
add(RETAIL, "Mall Ceiling Light", "A suspended light panel on two rods (0.7 m tall: place its top under the ceiling with \"y\"). Cosmetic.", ps, collision="cosmetic")
ps = octo(0.02, 1.2, 0.3, "#303030") + octo(0.5, 0.25, 0.05, "#2a2a2e") + octo(0.3, 0.05, 0.0, "#fff4d8", anim=LIT("#fff4d8", 1.3))
add(RETAIL, "Mall Pendant Light", "A hanging dome pendant lamp on a long cord (1.5 m tall: place its top under the ceiling). Cosmetic.", ps, collision="cosmetic")

ps = [part((3.0, 0.9, 0.04), (0, 0.45, 0), "#d01818"), part((3.0, 0.05, 0.05), (0, 0.92, 0), CHROME)]
ps += pk.text_parts("SALE", 0.09, 0, 0.45, 0.03, 0.015, "white") + [part((0.02, 1.0, 0.02), (-1.3, 1.4, 0), "#303030"), part((0.02, 1.0, 0.02), (1.3, 1.4, 0), "#303030")]
add(RETAIL, "Sale Banner Hanging", "A red hanging SALE banner on two wires (1.9 m tall: place its top under the ceiling with \"y\"). Cosmetic.", ps, collision="cosmetic")
ps = [part((0.7, 1.0, 0.03), (0, 0.5, 0), "#ffffff"), part((0.66, 0.2, 0.031), (0, 0.85, 0), "#d01818")]
ps += pk.text_parts("50%", 0.045, 0, 0.5, 0.035, 0.012, "#d01818") + pk.text_parts("OFF", 0.03, 0, 0.2, 0.035, 0.012, "#202020")
ps = [dict(p, offset=[p["offset"][0], R(p["offset"][1] + 0.4), p["offset"][2]]) for p in ps]
ps += [part((0.04, 0.5, 0.04), (0, 0.25, -0.02), CHROME), part((0.4, 0.03, 0.3), (0, 0.015, 0), "#303030")]
add(RETAIL, "Sale Sign Stand", "A free-standing 50% OFF sign on a chrome post. Cosmetic.", ps, collision="cosmetic", scatter=LOOSE)

ps = []
for k in range(3):
    x = -1.6 + k * 1.6
    ps += [part((1.56, 2.8, 0.06), (x, 1.4, 0), "#2a3a4a"), part((1.56, 0.12, 0.08), (x, 2.86, 0), "#e8c020")]
ps += pk.text_parts("COMING SOON", 0.06, 0, 1.75, 0.045, 0.012, "#e8c020")
ps += pk.text_parts("NEW STORE", 0.04, 0, 1.15, 0.045, 0.012, "white")
ps += [part((4.6, 0.12, 0.07), (0, 0.06, 0.01), "#e8c020")]
add(RETAIL, "Store Hoarding", "A 4.8 m stretch of dark construction hoarding with a yellow COMING SOON - NEW STORE sign, for a shop front being refitted. Solid, unbreakable.", ps,
    collider=[box(4.8, 2.9, 0.1)], scatter=SQUARE)

ps = [part((0.6, 0.9, 0.6), (0, 0.45, 0), "#f0f0f0"), part((0.64, 0.04, 0.64), (0, 0.92, 0), "#d8d4cc"), part((0.3, 0.3, 0.3), (0, 1.09, 0), "#c83a3a", rotation=[0, 30, 0])]
add(RETAIL, "Display Plinth", "A white display plinth with a featured box on top. Solid, breakable.", ps, collider=[box(0.64, 0.95, 0.64)], scatter=LOOSE,
    breakable=brk(60, "wood", 8))
ps = []
for x in (-0.6, 0.6):
    ps += [part((0.12, 1.5, 0.3), (x, 0.75, 0), "#d8d8e0"), part((0.04, 1.2, 0.02), (x, 0.8, 0.16), "#60a0ff", anim={"type": "pulse", "period": 2.0, "color": "#80c0ff", "off": "#203050", "glow": 0.6})]
add(RETAIL, "Security Gate Pair", "The two anti-theft pedestals at a shop doorway (1.2 m apart, light strips glowing). Cosmetic.", ps, collision="cosmetic")
ps = [part((0.5, 0.06, 0.04), (0, 0.03, 0), "#c0c0c0")]
ps = [part((0.36, 0.42, 0.14), (0, 0.21, 0), "#e8e0d0"), part((0.2, 0.12, 0.01), (0, 0.5, 0.0), "#202020")]
add(RETAIL, "Carrier Bag", "A paper shopping bag dropped on the floor. Cosmetic.", ps, collision="cosmetic", scatter=LOOSE, fade=30)

ps = [part((3.0, 0.05, 1.4), (0, 0.025, 0), "#3a3a40")]
for x in (-1.45, 1.45):
    ps += [part((0.06, 1.1, 1.4), (x, 0.55, 0), "#9aa0a8", material="metal"), part((0.06, 0.06, 1.4), (x, 1.12, 0), "#d03020")]
ps += [part((2.96, 0.06, 0.06), (0, 1.12, -0.68), "#9aa0a8"), part((2.96, 0.06, 0.06), (0, 0.6, -0.68), "#9aa0a8"),
       part((1.2, 0.4, 0.03), (0, 1.5, -0.68), "#2050a0"), part((0.06, 0.4, 0.06), (0, 1.3, -0.68), "#9aa0a8")]
ps += pk.text_parts("CARTS", 0.04, 0, 1.5, -0.66, 0.012, "white")
add(RETAIL, "Cart Return Bay", "A car-park cart corral: two steel rails, a back bar and a blue CARTS sign (3 m, open at the front). Solid, unbreakable.", ps,
    collider=[box(0.08, 1.15, 1.4, -1.45, 0), box(0.08, 1.15, 1.4, 1.45, 0), box(3.0, 1.15, 0.08, 0, -0.68)], scatter=SQUARE)

# ================================================================ BRANDS (pack-local, invented)
# (prop name suffix, fascia text, fascia colour, letter colour, logo, logo palette, width)
BRANDS = [
    ("Holloway Finch", "HOLLOWAY & FINCH", "#1d2a3a", "#e8d6a0", "monogram", {"a": "#e8d6a0", "b": "#c8a050"}, 11.0),
    ("Voltwave", "VOLTWAVE", "#101a2a", "#40d0ff", "bolt", {"a": "#ffe040"}, 8.0),
    ("Dog Eared Books", "DOG-EARED BOOKS", "#2a3a2a", "#f0e8d0", "book", {"a": "#c84a2a", "b": "#2a6aa0", "c": "#f0e8d0"}, 7.0),
    ("Sole Purpose", "SOLE PURPOSE", "#f0f0f0", "#202020", "shoe", {"a": "#e04020", "b": "#ffffff", "c": "#202020"}, 7.0),
    ("Full Tilt Sports", "FULL TILT SPORTS", "#c81818", "#ffffff", "ball", {"a": "#202020", "b": "#ffffff"}, 9.0),
    ("Loom Linen", "LOOM & LINEN", "#e8e0d4", "#4a3a30", "needle", {"a": "#8a6a50", "b": "#c84a4a"}, 9.0),
    ("Gilt Trip", "GILT TRIP", "#141418", "#e8c860", "gem", {"a": "#e8c860", "b": "#fff4c0"}, 6.0),
    ("Gumdrop Galaxy", "GUMDROP GALAXY", "#5a2a8a", "#ffe0f8", "candy", {"a": "#ff60b0", "b": "#ffd020", "c": "#ffffff"}, 6.0),
    ("Topsy Toys", "TOPSY TOYS", "#f0d020", "#d02020", "bear", {"a": "#a06030", "b": "#202020", "c": "#202020"}, 9.0),
    ("Token Town", "TOKEN TOWN", "#101010", "#ff40c0", "token", {"a": "#ffd020", "b": "#ff8020"}, 9.0),
    ("Gloss Lab", "GLOSS LAB", "#f8e0e8", "#a01840", "lips", {"a": "#d0204a", "b": "#ffffff"}, 7.0),
    ("Wagtail Pets", "WAGTAIL PETS", "#2a7a5a", "#ffffff", "paw", {"a": "#ffffff", "b": "#ffd060"}, 9.0),
    ("Nest Nook", "NEST & NOOK HOME", "#6a8a7a", "#ffffff", "house", {"a": "#e8a050", "b": "#ffffff", "c": "#4a3020"}, 9.0),
    ("Cellsius", "CELLSIUS", "#e8f0f8", "#2050c0", "phone", {"a": "#2050c0", "b": "#80d0ff", "c": "#ffffff"}, 6.0),
    ("Stardust Cinemas", "STARDUST CINEMAS", "#1a0a2a", "#ffe060", "star", {"a": "#ffe060"}, 10.0),
    ("Hemline", "HEMLINE", "#f4f0ec", "#202020", "needle", {"a": "#202020", "b": "#c0304a"}, 7.0),
    ("Food Court", "FOOD COURT", "#c83020", "#ffffff", "burger", {"a": "#f0c060", "b": "#60c040", "c": "#7a3a10"}, 9.0),
    ("Customer Service", "CUSTOMER SERVICE", "#2a4a7a", "#ffffff", "info", {"a": "#ffffff"}, 7.0),
]
FOOD = [
    ("Wokstar", "WOKSTAR", "#c82020", "#ffe040", "wok", {"a": "#f0f0f0", "b": "#202020", "c": "#ffa020"}),
    ("Bun Deal", "BUN DEAL", "#f0a020", "#5a2a10", "burger", {"a": "#c88030", "b": "#40a030", "c": "#6a3010"}),
    ("Slice of Life", "SLICE OF LIFE", "#2a7a3a", "#ffffff", "pizza", {"a": "#c88030", "b": "#ffd040", "c": "#d02020"}),
]
COFFEE = ("Drip Drop Coffee", "DRIP DROP COFFEE", "#3a2418", "#f0e0c0", "cup", {"a": "#f0f0f0", "b": "#ffffff", "c": "#c89060"})


def fascia(text, w, board, letters, logo, pal, h=0.9):
    """A storefront fascia board w x h, back on the wall (-Z), lit letters and the logo on the left."""
    ps = [part((w, h, 0.12), (0, h / 2, 0.06), board), part((w + 0.06, 0.05, 0.16), (0, h + 0.025, 0.08), "#3a3a40"),
          part((w + 0.06, 0.05, 0.16), (0, -0.025 + 0.05, 0.08), "#3a3a40")]
    lw, lh = pk.logo_size(logo)
    lpx = (h - 0.2) / lh
    logo_w = lw * lpx
    # text pixel size: as big as fits beside the logo
    px = min((h - 0.3) / pk.GLYPH_H, (w - logo_w - 0.6) / (len(text) * 6 - 1))
    tw = pk.text_width(text, px)
    total = logo_w + 0.25 + tw
    x0 = -total / 2
    ps += pk.art_parts(pk.LOGOS[logo], pal, lpx, x0 + logo_w / 2, h / 2, 0.15, 0.025)
    ps += pk.text_parts(text, px, x0 + logo_w + 0.25 + tw / 2, h / 2, 0.15, 0.025, letters, anim=LIT(letters, 0.8))
    return ps


def plaque(logo, pal, board, size=1.2):
    lw, lh = pk.logo_size(logo)
    px = (size - 0.2) / max(lw, lh)
    ps = [part((size, size, 0.06), (0, size / 2, 0.03), board)]
    ps += pk.art_parts(pk.LOGOS[logo], pal, px, 0, size / 2, 0.09, 0.025)
    return ps


for key, text, board, letters, logo, pal, w in BRANDS:
    ps = fascia(text, w, board, letters, logo, pal)
    add(MALL, f"{key} Sign", f"The {text} shop-front fascia: the name in lit letters beside its logo, {w:g} m wide. Hang it over the shop front (back on the wall, \"y\" for the height). Cosmetic.",
        ps, collision="cosmetic", fade=150)
    add(MALL, f"{key} Logo", f"The {text} logo on a square plaque (1.2 m) for the wall inside the shop. Cosmetic.", plaque(logo, pal, board), collision="cosmetic", fade=90)
for key, text, board, letters, logo, pal in FOOD:
    ps = fascia(text, 3.2, board, letters, logo, pal, h=0.7)
    # a menu board under the header
    ps += [part((3.0, 0.7, 0.05), (0, -0.4, 0.04), "#202020")]
    for r in range(3):
        for c in range(3):
            ps.append(part((0.7, 0.06, 0.01), (-1.0 + c * 1.0, -0.2 - r * 0.2, 0.07), "#f0f0f0"))
            ps.append(part((0.16, 0.06, 0.01), (-0.6 + c * 1.0, -0.2 - r * 0.2, 0.07), letters))
    ps = [dict(p, offset=[p["offset"][0], R(p["offset"][1] + 0.8), p["offset"][2]]) for p in ps]
    add(MALL, f"{key} Header", f"The {text} food-court stall header: the lit name and logo over a menu board, 3.2 m wide (1.5 m tall). Wall-mounted, cosmetic.",
        ps, collision="cosmetic", fade=120)
key, text, board, letters, logo, pal = COFFEE
ps = [part((2.4, 1.0, 2.4), (0, 0.5, 0), board), part((2.5, 0.06, 2.5), (0, 1.03, 0), "#c8a070", material="wood"),
      part((2.3, 0.12, 0.02), (0, 0.85, 1.21), "#c8a070")]
for x in (-1.15, 1.15):
    for z in (-1.15, 1.15):
        ps.append(part((0.08, 1.5, 0.08), (x, 1.8, z), "#202020"))
ps += [part((2.6, 0.3, 2.6), (0, 2.65, 0), board), part((0.5, 0.45, 0.4), (0, 1.28, -0.6), "#b8b8c0"), part((0.2, 0.15, 0.15), (-0.5, 1.13, -0.7), "#202020"),
       part((0.6, 0.4, 0.02), (0, 2.25, -1.1), "#202020")]
for s, (dx, dz, rot) in enumerate(((0, 1.32, 0), (0, -1.32, 180))):
    tp = pk.text_parts("DRIP DROP", 0.032, 0, 2.65, 0.0, 0.012, letters, anim=LIT(letters, 0.8))
    for p in tp:
        lx = p["offset"][0]
        a = math.radians(rot)
        p["offset"] = [R(dx + lx * math.cos(a)), p["offset"][1], R(dz - lx * math.sin(a))]
        p["rotation"] = [0, rot, 0]
    ps += tp
ps += pk.art_parts(pk.LOGOS[logo], pal, 0.07, 0, 3.15, 0.05, 0.03)
ps += [dict(p, offset=[p["offset"][0], p["offset"][1], R(-p["offset"][2])], rotation=[0, 180, 0]) for p in pk.art_parts(pk.LOGOS[logo], pal, 0.07, 0, 3.15, 0.05, 0.03)]
ps.append(part((0.6, 0.6, 0.04), (0, 3.15, 0), board))
add(MALL, f"{key} Kiosk", f"The {text} island coffee kiosk: a dark wood counter with an espresso machine under a lit canopy with the name on the front and back. Solid (the counter), unbreakable.",
    ps, collider=[box(2.4, 1.06, 2.4)], scatter=SQUARE)

BAGS = [("Voltwave", "#101a2a", "#40d0ff"), ("Loom Linen", "#e8e0d4", "#4a3a30"), ("Holloway Finch", "#1d2a3a", "#e8d6a0"),
        ("Topsy Toys", "#f0d020", "#d02020"), ("Gilt Trip", "#141418", "#e8c860"), ("Gumdrop Galaxy", "#5a2a8a", "#ff60b0")]
for key, c, c2 in BAGS:
    ps = [part((0.36, 0.42, 0.14), (0, 0.21, 0), c), part((0.2, 0.12, 0.012), (0, 0.27, 0.072), c2), part((0.16, 0.12, 0.012), (0, 0.48, 0), c2)]
    add(MALL, f"{key} Bag", f"A {key} carrier bag dropped on the floor. Cosmetic.", ps, collision="cosmetic", scatter=LOOSE, fade=30)

# The mall's own name over the main entrance and on the pylon by the road.
ps = [part((12.0, 1.5, 0.15), (0, 0.75, 0.075), "#2a3a4a")]
ps += pk.text_parts("SLOPVIEW MALL", 0.15, 0.6, 0.75, 0.2, 0.04, "#ffffff", anim=LIT("#ffffff", 0.9))
ps += pk.art_parts(pk.LOGOS["star"], {"a": "#ffd040"}, 0.17, -5.4, 0.75, 0.2, 0.04)
add(MALL, "Slopview Mall Sign", "SLOPVIEW MALL in big lit letters with a gold star, 12 m wide, for over the main entrance. Cosmetic.", ps, collision="cosmetic", fade=200)
ps = [part((0.6, 8.0, 0.6), (-1.0, 4.0, 0), "#5a5a60"), part((0.6, 8.0, 0.6), (1.0, 4.0, 0), "#5a5a60"), part((3.2, 3.0, 0.5), (0, 6.6, 0), "#2a3a4a"),
      part((3.0, 2.6, 0.02), (0, 5.4, 0.26), "#f0f0f0", anim=LIT("#f4f4f8", 0.5)), part((3.0, 2.6, 0.02), (0, 5.4, -0.26), "#f0f0f0", anim=LIT("#f4f4f8", 0.5))]
for side in (1, -1):
    tp = pk.text_parts("SLOPVIEW", 0.055, 0, 7.4, 0.27 if side > 0 else 0.27, 0.02, "#ffffff", anim=LIT("#ffffff", 0.9))
    tp += pk.text_parts("MALL", 0.08, 0, 6.6, 0.27, 0.02, "#ffd040", anim=LIT("#ffd040", 0.9))
    if side < 0:
        tp = [dict(p, offset=[R(-p["offset"][0]), p["offset"][1], R(-p["offset"][2])], rotation=[0, 180, 0]) for p in tp]
    ps += tp
for k, (key, text, board, letters, logo, pal, w) in enumerate(BRANDS[:8]):
    y = 6.2 - k * 0.32
    ps.append(part((1.4, 0.26, 0.03), (-0.75 + (k % 2) * 1.5, 5.95 - (k // 2) * 0.32 - 0.6, 0.27), board))
    ps.append(part((1.4, 0.26, 0.03), (-0.75 + (k % 2) * 1.5, 5.95 - (k // 2) * 0.32 - 0.6, -0.27), board))
add(MALL, "Slopview Mall Pylon", "The mall's road pylon sign, 8 m tall: SLOPVIEW MALL on both faces over panels for the shops. Solid (the legs), unbreakable.", ps,
    collider=[box(0.6, 8.0, 0.6, -1.0, 0), box(0.6, 8.0, 0.6, 1.0, 0)], scatter=SQUARE)


def write(where, folder, category):
    os.makedirs(folder, exist_ok=True)
    names = set()
    for d in where:
        assert d["name"] not in names, d["name"]
        names.add(d["name"])
        d = dict(d)
        d = {k: d[k] for k in ["format", "name"]} | {"category": category} | {k: v for k, v in d.items() if k not in ("format", "name")}
        fn = "".join(ch for ch in d["name"].title() if ch.isalnum()) + ".json"
        with open(os.path.join(folder, fn), "w") as f:
            head = {k: v for k, v in d.items() if k != "parts"}
            s = json.dumps(head, indent=2)[:-2]
            s += ',\n  "parts": [\n' + ",\n".join("    " + json.dumps(p) for p in d["parts"]) + "\n  ]\n}\n"
            f.write(s)
    print(f"{len(where)} props -> {folder}")


write(RETAIL, OUT_RETAIL, "Retail")


def gallery():
    """Maps/Dev/PropGalleryRetail.json: every Retail prop on its own pad, in rows (the self-test wants every prop on a Dev map)."""
    names = sorted(d["name"] for d in RETAIL)
    per = 10
    gops = [{"op": "Ground", "color": [0.4, 0.4, 0.42]}, {"op": "Perimeter", "height": 3, "color": [0.55, 0.55, 0.58]}]
    hung = ("Mall Ceiling Light", "Mall Pendant Light", "Sale Banner Hanging")
    for i, n in enumerate(names):
        x, z = -40.5 + (i % per) * 9.0, -24.0 + (i // per) * 8.0
        big = n in ("Cinema Big Screen", "Mall Fountain", "Escalator Balustrade", "Store Hoarding")
        gops.append({"op": "Block", "x0": x - 3.6, "y0": 0, "z0": z - 3.4, "x1": x + 3.6, "y1": 0.05, "z1": z + 3.4, "color": [0.55, 0.53, 0.5]} if big else
                    {"op": "Block", "x0": x - 1.8, "y0": 0, "z0": z - 1.8, "x1": x + 1.8, "y1": 0.05, "z1": z + 1.8, "color": [0.55, 0.53, 0.5]})
        gops.append({"op": "Prop", "name": n, "x": x, "z": z} | ({"y": 1.2} if n in hung else {}))
    gops += [{"op": "Spawn", "x": float(x), "z": 32.0} for x in range(-40, 41, 10)] + [{"op": "AutoWaypoints", "spacing": 6}]
    mp = {"format": "call-of-slop-map/1", "name": "Prop Gallery Retail", "description": "Every Retail shop fitting (Props/Retail) on its own pad.",
          "author": "Call of Slop", "order": 961, "halfX": 50, "halfZ": 36, "sky": [0.62, 0.7, 0.8], "ops": gops}
    path = os.path.join(REPO, "Maps/Dev/PropGalleryRetail.json")
    with open(path, "w") as f:
        f.write("{\n" + ",\n".join(f'  "{k}": ' + (("[\n" + ",\n".join("    " + json.dumps(o) for o in v) + "\n  ]") if k == "ops" else json.dumps(v)) for k, v in mp.items()) + "\n}\n")
    print("wrote", path)


def animated_gallery():
    """Adds the animated Retail props to the Dev pack's Animated Props map (Maps/Dev/AnimatedProps.json: the self-test
    wants every animated prop shown there), in rows north of its own, just before its spawns. Re-running replaces them."""
    path = os.path.join(REPO, "Maps/Dev/AnimatedProps.json")
    lines = open(path).read().split("\n")
    names = sorted(d["name"] for d in RETAIL if any("anim" in q for q in d["parts"]))
    mine = set(d["name"] for d in RETAIL)
    lines = [ln for ln in lines if not any(f'"name": "{n}"' in ln for n in mine)]
    at = next(i for i, ln in enumerate(lines) if '"op": "Spawn"' in ln)
    hung = ("Mall Ceiling Light", "Mall Pendant Light")
    new = []
    k = 0
    for n in names:
        if n == "Cinema Big Screen":
            new.append("    " + json.dumps({"op": "Prop", "name": n, "x": 0.0, "z": 31.0}) + ",")
            continue
        x, z = -36.0 + (k % 9) * 9.0, 14.0 + (k // 9) * 7.0
        k += 1
        new.append("    " + json.dumps({"op": "Prop", "name": n, "x": x, "z": z} | ({"y": 1.5} if n in hung else {})) + ",")
    lines[at:at] = new
    open(path, "w").write("\n".join(lines))
    print("animated gallery:", len(new), "retail props on", path)


gallery()
animated_gallery()
write(MALL, OUT_MALL, "Mall")
