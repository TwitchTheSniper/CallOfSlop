"""Generates the starter prop library (Props/<Category>/<Name>.json) for Call of Slop."""
import json, math, os, random, sys

OUT = sys.argv[1] if len(sys.argv) > 1 else "Props"
PROPS = []


def r3(v):
    return [round(float(x), 3) for x in v]


def B(size, off=None, color="grey", mat=None, yaw=0, pitch=0, roll=0, tex=None):
    p = {"size": r3(size)}
    if off is not None:
        p["offset"] = r3(off)
    if yaw or pitch or roll:
        p["rotation"] = r3([pitch, yaw, roll])
    p["color"] = color
    if mat:
        p["material"] = mat
    if tex:
        p["texScale"] = tex
    return p


def At(size, y0=0.0, x=0.0, z=0.0, **kw):
    """A box whose bottom is at y0."""
    return B(size, [x, y0 + size[1] / 2, z], **kw)


def Cyl(d, h, y0=0.0, x=0.0, z=0.0, color="grey", mat=None, k=1.0, yaw=0):
    """A vertical 'cylinder': an octagon d across, the union of four d x 0.414d boxes at 45 degree steps."""
    w = d * k
    return [At([w, h, w * 0.4142], y0, x, z, color=color, mat=mat, yaw=yaw + a) for a in (0, 45, 90, 135)]


def HCyl(d, length, cy, x=0.0, z=0.0, color="grey", mat=None, k=1.0, alongZ=False):
    """A lying octagonal 'cylinder' along X (or Z)."""
    w = d * k
    if alongZ:
        return [B([w, w * 0.4142, length], [x, cy, z], color=color, mat=mat, roll=a) for a in (0, 45, 90, 135)]
    return [B([length, w, w * 0.4142], [x, cy, z], color=color, mat=mat, pitch=a) for a in (0, 45, 90, 135)]


def Ring(d_out, d_in, h, y0=0.0, x=0.0, z=0.0, color="rubber", n=8):
    """A flat ring (a tyre lying down) from n segments."""
    r = (d_out + d_in) / 4
    w = (d_out - d_in) / 2
    chord = 2 * (d_out / 2) * math.tan(math.pi / n) * 1.02
    parts = []
    for k in range(n):
        a = 2 * math.pi * k / n
        parts.append(At([w, h, chord], y0, x + r * math.cos(a), z + r * math.sin(a), color=color, yaw=-math.degrees(a)))
    return parts


def prop(name, category, desc, parts, collision="solid", collider=None, scatter=None, breakable=None, fade=None):
    flat = []
    for p in parts:
        if isinstance(p, list):
            flat.extend(p)
        else:
            flat.append(p)
    d = {"format": "call-of-slop-prop/1", "name": name, "category": category, "description": desc, "collision": collision}
    if collider is not None:
        d["collider"] = collider
    if scatter is not None:
        d["scatter"] = scatter
    if fade is not None:
        d["fadeDistance"] = fade
    if breakable is not None:
        d["breakable"] = breakable
    d["parts"] = flat
    PROPS.append(d)


def C(size, off=None):
    c = {"size": r3(size)}
    if off is not None:
        c["offset"] = r3(off)
    return c


def brk(health, sound, by="all", count=8, size=(0.06, 0.18), speed=4, colors=None, explode=None):
    b = {"health": health, "breakBy": by, "sound": sound, "debris": {"count": count, "size": list(size), "speed": speed}}
    if colors:
        b["debris"]["colors"] = colors
    if explode:
        b["explode"] = explode
    return b


# =============================== Street ===============================

def trash_can():
    body = Cyl(0.56, 0.82, 0.02, color="galvanized", mat="metal")
    bands = Cyl(0.6, 0.035, 0.2, color="steel") + Cyl(0.6, 0.035, 0.5, color="steel")
    rim = Cyl(0.62, 0.05, 0.82, color="steel")
    lid = Cyl(0.64, 0.04, 0.86, color="galvanized") + Cyl(0.44, 0.03, 0.9, color="galvanized")
    handle = [At([0.04, 0.06, 0.04], 0.93, -0.07, color="steel"), At([0.04, 0.06, 0.04], 0.93, 0.07, color="steel"),
              At([0.2, 0.03, 0.04], 0.99, color="darkgrey")]
    side_handles = [At([0.03, 0.05, 0.12], 0.66, 0.3, color="steel"), At([0.03, 0.05, 0.12], 0.66, -0.3, color="steel")]
    base = Cyl(0.5, 0.03, 0.0, color="darkgrey")
    prop("Trash Can", "Street", "A galvanized steel trash can with its lid on.",
         [base, body, bands, rim, lid, handle, side_handles], collider=[C([0.58, 1.0, 0.58])],
         scatter={"randomYaw": True, "scale": [0.95, 1.05]}, breakable=brk(70, "metal", count=10, size=(0.08, 0.2), speed=3.5))


def wheelie_bin(name, color, lid, desc):
    parts = [At([0.56, 0.9, 0.66], 0.06, 0, 0.02, color=color),
             At([0.6, 0.06, 0.74], 0.96, 0, 0.0, color=lid),
             At([0.62, 0.04, 0.08], 0.94, 0, 0.36, color=lid),  # front lip
             At([0.5, 0.05, 0.06], 0.98, 0, -0.39, color="darkgrey"),  # hinge / handle
             At([0.06, 0.05, 0.06], 0.94, 0.22, -0.4, color="darkgrey"), At([0.06, 0.05, 0.06], 0.94, -0.22, -0.4, color="darkgrey"),
             At([0.58, 0.04, 0.68], 0.42, 0, 0.02, color=color),  # moulded rib
             At([0.08, 0.22, 0.22], 0.0, 0.31, -0.26, color="rubber"), At([0.08, 0.22, 0.22], 0.0, -0.31, -0.26, color="rubber"),
             At([0.7, 0.04, 0.04], 0.09, 0, -0.26, color="darkgrey"),  # axle
             At([0.4, 0.06, 0.1], 0.0, 0, 0.26, color=color),  # front foot
             At([0.3, 0.12, 0.01], 0.6, 0, 0.355, color="white")]  # label
    prop(name, "Street", desc, parts, collider=[C([0.62, 1.0, 0.76])],
         scatter={"randomYaw": True, "yawJitter": 20, "scale": [0.95, 1.05]}, breakable=brk(90, "soft", count=8, size=(0.08, 0.22), speed=3))


def recycling_bin():
    parts = [At([0.6, 0.04, 0.42], 0.0, color="blue"),
             At([0.6, 0.42, 0.03], 0.0, 0, 0.195, color="blue"), At([0.6, 0.42, 0.03], 0.0, 0, -0.195, color="blue"),
             At([0.03, 0.42, 0.36], 0.0, 0.285, 0, color="blue"), At([0.03, 0.42, 0.36], 0.0, -0.285, 0, color="blue"),
             At([0.64, 0.03, 0.46], 0.42, color="navy"),
             At([0.3, 0.06, 0.25], 0.04, -0.1, 0.02, color="newsprint"), At([0.28, 0.05, 0.22], 0.1, -0.12, 0.0, color="paper", yaw=12),
             Cyl(0.07, 0.22, 0.04, 0.16, -0.06, color="glass"), Cyl(0.065, 0.2, 0.04, 0.2, 0.07, color="amber"),
             At([0.02, 0.06, 0.02], 0.26, 0.16, -0.06, color="glass"),
             At([0.12, 0.12, 0.01], 0.15, 0, 0.212, color="white")]
    prop("Recycling Bin", "Street", "An open blue recycling box full of papers and bottles.", parts,
         scatter={"randomYaw": True}, breakable=brk(25, "soft", count=8, size=(0.05, 0.14)))


def dumpster():
    W, H, D = 1.9, 1.15, 1.1
    parts = [At([W, H, D], 0.16, color="darkgreen", mat="metal"),
             At([W + 0.04, 0.06, D + 0.06], 0.16 + H - 0.03, color="green"),  # top rim
             B([W / 2 - 0.02, 0.05, D + 0.1], [-W / 4, 0.16 + H + 0.06, 0.02], color="black", pitch=-6),
             B([W / 2 - 0.02, 0.05, D + 0.1], [W / 4, 0.16 + H + 0.08, 0.02], color="black", pitch=-9),
             At([0.14, 0.18, D + 0.08], 0.55, -W / 2 - 0.04, color="green"), At([0.14, 0.18, D + 0.08], 0.55, W / 2 + 0.04, color="green"),  # fork pockets
             At([W + 0.02, 0.1, D + 0.04], 0.16, color="green")]
    for x in (-0.6, 0.0, 0.6):
        parts.append(At([0.08, H - 0.1, 0.04], 0.2, x, D / 2 + 0.01, color="green"))  # front ribs
    for x in (-W / 2 + 0.15, W / 2 - 0.15):
        for z in (-D / 2 + 0.12, D / 2 - 0.12):
            parts.append(At([0.08, 0.16, 0.16], 0.0, x, z, color="rubber"))
    parts.append(At([0.5, 0.25, 0.01], 0.7, -0.3, D / 2 + 0.035, color="white"))
    prop("Dumpster", "Street", "A big steel dumpster with two plastic lids. Solid cover.", parts, collider=[C([W + 0.1, 1.38, D + 0.1])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 6})


def bench():
    parts = []
    for z in (-0.12, 0.0, 0.12):
        parts.append(At([1.8, 0.04, 0.1], 0.44, 0, z, color="wood", mat="wood"))
    for y in (0.6, 0.76):
        parts.append(B([1.8, 0.1, 0.035], [0, y, -0.22 - (y - 0.6) * 0.15], color="wood", mat="wood", pitch=-12))
    for x in (-0.75, 0.75):
        parts += [At([0.06, 0.44, 0.06], 0.0, x, 0.14, color="black"), At([0.06, 0.44, 0.06], 0.0, x, -0.16, color="black"),
                  At([0.06, 0.04, 0.4], 0.4, x, -0.01, color="black"), B([0.05, 0.42, 0.05], [x, 0.66, -0.22], color="black", pitch=-12),
                  At([0.06, 0.04, 0.34], 0.62, x * 1.02, 0.0, color="black"), At([0.06, 0.18, 0.04], 0.44, x * 1.02, 0.15, color="black")]
    prop("Bench", "Street", "A park bench: wooden slats on black iron ends.", parts, collider=[C([1.86, 0.48, 0.44], [0, 0.24, 0]), C([1.86, 0.88, 0.12], [0, 0.44, -0.24])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4})


def street_lamp():
    parts = [Cyl(0.34, 0.45, 0.0, color="darkgrey"), Cyl(0.26, 0.08, 0.45, color="black"),
             Cyl(0.13, 4.0, 0.5, color="darkgrey", mat="metal"),
             At([0.9, 0.07, 0.07], 4.38, 0.42, color="darkgrey"), B([0.5, 0.06, 0.06], [0.2, 4.2, 0], color="darkgrey", roll=35),
             At([0.5, 0.12, 0.28], 4.28, 0.9, color="darkgrey"), At([0.42, 0.03, 0.22], 4.25, 0.9, color="#fff2c0"),
             At([0.12, 0.1, 0.12], 4.5, 0, color="black")]
    prop("Street Lamp", "Street", "A tall street lamp on a cast base.", parts, collider=[C([0.34, 0.5]), C([0.16, 4.6, 0.16])] if False else [C([0.34, 0.5, 0.34]), C([0.16, 4.1, 0.16], [0, 2.55, 0])],
         scatter={"randomYaw": False, "yawStep": 90})


def hydrant():
    parts = [Cyl(0.34, 0.06, 0.0, color="red"), Cyl(0.26, 0.5, 0.06, color="red", mat="metal"),
             Cyl(0.3, 0.05, 0.42, color="red"), Cyl(0.22, 0.12, 0.47, color="red"), Cyl(0.08, 0.06, 0.59, color="yellow"),
             HCyl(0.11, 0.4, 0.36, color="red"), Cyl(0.09, 0.04, 0.315, 0.21, 0, color="yellow"),
             B([0.14, 0.14, 0.12], [0, 0.32, 0.17], color="red"), B([0.1, 0.1, 0.05], [0, 0.32, 0.25], color="yellow")]
    # the side caps: small boxes at the ends of the side nozzle
    parts += [B([0.04, 0.09, 0.09], [0.21, 0.36, 0], color="yellow"), B([0.04, 0.09, 0.09], [-0.21, 0.36, 0], color="yellow")]
    prop("Fire Hydrant", "Street", "A red cast-iron fire hydrant.", parts, collider=[C([0.36, 0.66, 0.36])], scatter={"randomYaw": True})


def mailbox():
    parts = [At([0.05, 0.3, 0.05], 0, x, z, color="navy") for x in (-0.2, 0.2) for z in (-0.18, 0.18)]
    parts += [At([0.5, 0.74, 0.44], 0.3, color="blue", mat="metal"), At([0.5, 0.1, 0.38], 1.04, color="blue"), At([0.5, 0.06, 0.26], 1.14, color="blue"),
              At([0.52, 0.03, 0.46], 0.3, color="navy"),
              B([0.34, 0.07, 0.03], [0, 0.92, 0.23], color="navy", pitch=-15), At([0.3, 0.16, 0.01], 0.55, 0, 0.225, color="white"),
              At([0.12, 0.05, 0.01], 0.6, 0, 0.231, color="red")]
    prop("Mailbox", "Street", "A blue street mailbox on four short legs.", parts, collider=[C([0.52, 1.2, 0.46])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 5})


def cone():
    parts = [At([0.42, 0.035, 0.42], 0, color="orange"), At([0.36, 0.02, 0.36], 0.035, color="black")]
    y = 0.055
    for i, (w, c) in enumerate([(0.27, "orange"), (0.23, "orange"), (0.19, "white"), (0.155, "orange"), (0.12, "white"), (0.09, "orange")]):
        h = 0.1
        parts += Cyl(w, h, y, color=c)
        y += h
    parts += Cyl(0.06, 0.04, y, color="orange")
    prop("Traffic Cone", "Street", "An orange traffic cone with white reflective bands.", parts, collider=[C([0.3, 0.7, 0.3])],
         scatter={"randomYaw": True, "scale": [0.95, 1.05]}, breakable=brk(15, "soft", count=6, size=(0.05, 0.12), speed=3))


def jersey():
    L = 2.0
    parts = [At([L, 0.08, 0.62], 0, color="concrete", mat="concrete"), At([L, 0.26, 0.56], 0.08, color="concrete", mat="concrete"),
             At([L, 0.3, 0.38], 0.34, color="concrete", mat="concrete"), At([L, 0.18, 0.24], 0.64, color="concrete", mat="concrete"),
             At([0.08, 0.08, 0.01], 0.62, -0.6, 0.193, color="yellow"), At([0.08, 0.08, 0.01], 0.62, 0.6, 0.193, color="yellow"),
             At([0.3, 0.06, 0.1], 0.0, -0.6, 0, color="darkgrey"), At([0.3, 0.06, 0.1], 0.0, 0.6, 0, color="darkgrey")]
    prop("Road Barrier", "Street", "A concrete jersey barrier. Waist-high, stops bullets.", parts, collider=[C([2.0, 0.82, 0.6])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3})


def sawhorse():
    parts = []
    for i in range(6):
        parts.append(At([0.25, 0.18, 0.03], 0.82, -0.625 + 0.25 * i, 0, color="orange" if i % 2 == 0 else "white"))
    for x in (-0.55, 0.55):
        parts += [B([0.05, 0.92, 0.05], [x, 0.45, 0.13], color="lightwood", pitch=14), B([0.05, 0.92, 0.05], [x, 0.45, -0.13], color="lightwood", pitch=-14),
                  At([0.05, 0.05, 0.3], 0.35, x, 0, color="lightwood")]
    parts.append(At([1.2, 0.04, 0.06], 0.78, 0, 0, color="lightwood"))
    prop("Sawhorse Barricade", "Street", "A wooden road barricade with a striped board.", parts, collider=[C([1.3, 1.0, 0.36])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10}, breakable=brk(35, "wood", count=9, size=(0.06, 0.3), speed=3.5))


def vending():
    parts = [At([0.95, 0.08, 0.75], 0, color="black"), At([0.95, 1.8, 0.75], 0.08, color="red", mat="metal"),
             At([0.6, 1.25, 0.02], 0.55, -0.12, 0.38, color="#1c2c3c"), At([0.62, 1.29, 0.015], 0.53, -0.12, 0.374, color="lightgrey"),
             At([0.24, 0.6, 0.02], 0.95, 0.31, 0.38, color="darkgrey"), At([0.12, 0.08, 0.02], 1.35, 0.31, 0.39, color="#304a2e"),
             At([0.06, 0.12, 0.02], 0.75, 0.31, 0.39, color="black"), At([0.6, 0.16, 0.03], 0.2, -0.12, 0.38, color="black"),
             At([0.95, 0.22, 0.02], 1.62, 0, 0.38, color="white"), At([0.7, 0.1, 0.01], 1.68, 0, 0.392, color="red")]
    for i in range(5):
        parts.append(At([0.52, 0.02, 0.01], 0.75 + i * 0.22, -0.12, 0.392, color="#7d8a95"))
        for j in range(4):
            parts.append(At([0.07, 0.11, 0.01], 0.77 + i * 0.22, -0.32 + j * 0.13, 0.391, color=["red", "blue", "yellow", "green"][(i + j) % 4]))
    prop("Vending Machine", "Street", "A soda vending machine, glass front, coins slot and all.", parts, collider=[C([0.95, 1.88, 0.77], [0, 0.94, 0.01])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 3})


def cart():
    parts = [At([0.5, 0.02, 0.8], 0.42, 0, 0, color="chrome")]
    for x in (-0.25, 0.25):
        parts.append(B([0.02, 0.45, 0.88], [x, 0.66, 0.0], color="chrome", roll=0))
    parts += [At([0.54, 0.45, 0.02], 0.44, 0, 0.42, color="chrome"), At([0.48, 0.42, 0.02], 0.44, 0, -0.42, color="chrome")]
    for y in (0.56, 0.7, 0.84):
        parts += [At([0.54, 0.015, 0.015], y, 0, 0.435, color="steel")]
        for x in (-0.26, 0.26):
            parts.append(At([0.015, 0.015, 0.9], y, x, 0, color="steel"))
    parts += [At([0.56, 0.05, 0.05], 0.98, 0, -0.52, color="red"), B([0.03, 0.2, 0.03], [-0.25, 0.92, -0.47], color="chrome", pitch=-35),
              B([0.03, 0.2, 0.03], [0.25, 0.92, -0.47], color="chrome", pitch=-35),
              At([0.42, 0.02, 0.6], 0.14, 0, 0.05, color="chrome")]
    for x in (-0.2, 0.2):
        for z in (-0.32, 0.34):
            parts += [At([0.03, 0.3, 0.03], 0.1, x, z, color="chrome"), At([0.04, 0.1, 0.1], 0.0, x, z, color="rubber")]
    prop("Shopping Cart", "Street", "An abandoned shopping cart.", parts, collider=[C([0.56, 1.04, 1.0], [0, 0.52, -0.04])], scatter={"randomYaw": True})


def stop_sign():
    parts = [Cyl(0.07, 2.5, 0.0, color="grey", mat="metal")]
    parts += [B([0.64, 0.265, 0.02], [0, 2.2, 0.035], color="white", roll=a) for a in (0, 45, 90, 135)]
    parts += [B([0.58, 0.24, 0.03], [0, 2.2, 0.05], color="red", roll=a) for a in (0, 45, 90, 135)]
    parts += [B([0.4, 0.08, 0.035], [0, 2.2, 0.053], color="white")]
    prop("Stop Sign", "Street", "A stop sign on a steel post.", parts, collider=[C([0.1, 2.5, 0.1])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8})


def street_sign():
    parts = [At([0.08, 2.9, 0.08], 0, color="darkgreen", mat="metal"),
             At([0.9, 0.2, 0.025], 2.55, 0.35, 0.05, color="darkgreen"), At([0.8, 0.06, 0.03], 2.62, 0.35, 0.05, color="white"),
             At([0.025, 0.2, 0.9], 2.3, 0.05, 0.35, color="darkgreen"), At([0.03, 0.06, 0.8], 2.37, 0.05, 0.35, color="white"),
             At([0.1, 0.06, 0.1], 2.9, color="darkgreen")]
    prop("Street Name Sign", "Street", "Green street name signs at a corner.", parts, collider=[C([0.12, 2.9, 0.12])], scatter={"randomYaw": False, "yawStep": 90})


def bollard():
    parts = Cyl(0.18, 0.9, 0.0, color="darkgrey", mat="metal") + Cyl(0.2, 0.08, 0.7, color="yellow") + Cyl(0.2, 0.05, 0.9, color="darkgrey") + Cyl(0.24, 0.03, 0.0, color="black")
    prop("Bollard", "Street", "A steel bollard with a yellow band.", parts, collider=[C([0.22, 0.95, 0.22])], scatter={"randomYaw": False, "yawStep": 45})


# =============================== Industrial ===============================

def oil_drum(name, color, desc, breakable=None, extra=()):
    parts = Cyl(0.58, 0.86, 0.02, color=color, mat="metal") + Cyl(0.62, 0.03, 0.0, color="darkgrey")
    for y in (0.29, 0.58):
        parts += Cyl(0.63, 0.035, y, color=color)
    parts += Cyl(0.62, 0.03, 0.87, color="darkgrey") + Cyl(0.56, 0.01, 0.88, color=color)
    parts += [At([0.06, 0.025, 0.06], 0.89, 0.15, 0.1, color="darkgrey"), At([0.04, 0.02, 0.04], 0.89, -0.14, -0.12, color="darkgrey")]
    parts += list(extra)
    prop(name, "Industrial", desc, parts, collider=[C([0.6, 0.92, 0.6])], scatter={"randomYaw": True, "scale": [0.97, 1.03]}, breakable=breakable)


def crate(name, s, desc, health):
    t = 0.04
    b = s * 0.11
    parts = [At([s - 2 * t, s - 2 * t, s - 2 * t], t, color="lightwood", mat="wood", tex=1.4)]
    # border boards on all six faces' edges (12 edges), and a diagonal brace on four sides
    for (ax, sg) in [("x", 1), ("x", -1), ("z", 1), ("z", -1)]:
        for y in (b / 2, s - b / 2):
            if ax == "x":
                parts.append(B([t, b, s], [sg * (s / 2 - t / 2), y, 0], color="wood", mat="wood"))
            else:
                parts.append(B([s, b, t], [0, y, sg * (s / 2 - t / 2)], color="wood", mat="wood"))
        for o in (-1, 1):
            if ax == "x":
                parts.append(B([t, s - 2 * b, b], [sg * (s / 2 - t / 2), s / 2, o * (s / 2 - b / 2)], color="wood", mat="wood"))
            else:
                parts.append(B([b, s - 2 * b, t], [o * (s / 2 - b / 2), s / 2, sg * (s / 2 - t / 2)], color="wood", mat="wood"))
        diag = (s - 2 * b) * 1.38
        if ax == "x":
            parts.append(B([t * 0.9, b, diag], [sg * (s / 2 - t / 2), s / 2, 0], color="wood", mat="wood", pitch=45 * sg))
        else:
            parts.append(B([diag, b, t * 0.9], [0, s / 2, sg * (s / 2 - t / 2)], color="wood", mat="wood", roll=45 * sg))
    for x in (-1, 1):
        parts.append(B([b, t, s], [x * (s / 2 - b / 2), s - t / 2, 0], color="wood", mat="wood"))
    parts.append(B([s - 2 * b, t, b], [0, s - t / 2, 0], color="wood", mat="wood"))
    prop(name, "Industrial", desc, parts, collider=[C([s, s, s])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 12, "scale": [0.95, 1.05]},
         breakable=brk(health, "wood", count=12, size=(0.06, s * 0.45), speed=4))


def pallet_parts(y0=0.0, yaw=0.0, x=0.0, z=0.0, color="pallet"):
    out = []
    def P(size, off):
        # rotate the offset by yaw about Y (same convention as the game)
        a = math.radians(yaw)
        ox, oz = off[0] * math.cos(a) + off[2] * math.sin(a), -off[0] * math.sin(a) + off[2] * math.cos(a)
        out.append(B(size, [x + ox, y0 + off[1], z + oz], color=color, mat="wood", yaw=yaw, tex=1.6))
    for zz in (-0.35, -0.175, 0.0, 0.175, 0.35):
        P([1.2, 0.022, 0.1], [0, 0.133, zz])
    for zz in (-0.35, 0.0, 0.35):
        for xx in (-0.55, 0.0, 0.55):
            P([0.1, 0.078, 0.1 if zz == 0 else 0.1], [xx, 0.083, zz])
        P([1.2, 0.022, 0.1], [0, 0.033, zz])
    for xx in (-0.55, 0.0, 0.55):
        P([0.1, 0.022, 0.8], [xx, 0.011, 0])
    return out


def pallet():
    prop("Pallet", "Industrial", "A wooden shipping pallet.", pallet_parts(), collider=[C([1.2, 0.144, 0.8])],
         scatter={"randomYaw": True}, breakable=brk(40, "wood", count=10, size=(0.05, 0.35), speed=3))


def pallet_stack():
    random.seed(4)
    parts = []
    for i in range(5):
        parts += pallet_parts(i * 0.144, random.uniform(-6, 6), random.uniform(-0.04, 0.04), random.uniform(-0.04, 0.04), color="pallet" if i % 2 else "lightwood")
    prop("Pallet Stack", "Industrial", "Five pallets stacked up. Knee-high cover.", parts, collider=[C([1.24, 0.72, 0.84])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 15}, breakable=brk(160, "wood", count=20, size=(0.06, 0.4), speed=3.5))


def pipes():
    parts = []
    for (z, y) in ((-0.17, 0.16), (0.17, 0.16), (0.0, 0.45)):
        parts += HCyl(0.34, 3.0, y, 0, z, color="rust", mat="metal")
        parts += [B([0.02, 0.2, 0.2], [1.505, y, z], color="black"), B([0.02, 0.2, 0.2], [-1.505, y, z], color="black")]
    parts += [At([0.12, 0.08, 0.7], 0.0, -1.0, 0, color="wood"), At([0.12, 0.08, 0.7], 0.0, 1.0, 0, color="wood")]
    prop("Pipe Stack", "Industrial", "Three rusty steel pipes stacked on timber.", parts, collider=[C([3.0, 0.62, 0.68])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10})


def concrete_pipe():
    parts = []
    n = 8
    R, w = 0.65, 0.14
    for k in range(n):
        a = 2 * math.pi * k / n
        parts.append(B([1.8, w, 2 * R * math.tan(math.pi / n) * 1.04], [0, R + 0.02 + R * math.sin(a), R * math.cos(a)], color="concrete", mat="concrete", roll=0, pitch=-math.degrees(a) + 90))
    prop("Concrete Pipe", "Industrial", "A big concrete drain pipe section lying on its side.", parts, collider=[C([1.8, 1.42, 1.42])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 20})


def sandbags():
    parts = []
    random.seed(11)
    rows = 5
    for r in range(rows):
        n = 4 if r % 2 == 0 else 3
        for i in range(n):
            x = (i - (n - 1) / 2) * 0.5
            y = r * 0.16
            yaw = random.uniform(-4, 4)
            c = random.choice(["tan", "sand", "#a89a72"])
            parts.append(At([0.48, 0.16, 0.3], y, x, random.uniform(-0.02, 0.02), color=c, yaw=yaw))
            parts.append(At([0.42, 0.04, 0.24], y + 0.13, x, 0, color=c, yaw=yaw))
    prop("Sandbag Wall", "Industrial", "A short wall of sandbags. Good cover, waist high.", parts, collider=[C([2.02, 0.84, 0.34])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8})


def tyre():
    parts = Ring(0.66, 0.34, 0.22, 0.0) + Cyl(0.36, 0.02, 0.1, color="darkgrey")
    prop("Tyre", "Industrial", "An old car tyre lying flat.", parts, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.92, 1.08]})


def tyre_stack():
    parts = []
    random.seed(3)
    for i in range(4):
        parts += Ring(0.66, 0.34, 0.215, i * 0.22, random.uniform(-0.03, 0.03), random.uniform(-0.03, 0.03))
    prop("Tyre Stack", "Industrial", "Four tyres in a pile.", parts, collider=[C([0.7, 0.88, 0.7])], scatter={"randomYaw": True})


def spool():
    parts = HCyl(1.0, 0.07, 0.5, -0.3, color="wood", mat="wood") + HCyl(1.0, 0.07, 0.5, 0.3, color="wood", mat="wood")
    parts += HCyl(0.5, 0.56, 0.5, 0, color="darkwood") + HCyl(0.62, 0.5, 0.5, 0, color="#2e2e2e")
    parts += [B([0.075, 0.12, 0.12], [x, 0.5, 0], color="steel") for x in (-0.34, 0.34)]
    # stand it so its axis runs along X: rotate parts to put axis along X (HCyl runs along X, so length is the axle)
    fixed = []
    for p in parts:
        fixed.append(p)
    prop("Cable Spool", "Industrial", "A wooden cable drum with black cable wound on it.", fixed, collider=[C([0.75, 1.0, 1.0])],
         scatter={"randomYaw": True}, breakable=brk(110, "wood", count=14, size=(0.08, 0.35), speed=3))


def gas_cylinders():
    parts = []
    for (x, z, c) in ((-0.17, 0.0, "red"), (0.17, 0.05, "lightgrey"), (0.0, -0.22, "red")):
        parts += Cyl(0.24, 1.15, 0.0, x, z, color=c, mat="metal") + Cyl(0.18, 0.08, 1.15, x, z, color=c) + Cyl(0.08, 0.1, 1.23, x, z, color="steel")
    parts += [At([0.7, 0.05, 0.05], 0.8, 0, 0.16, color="black"), At([0.05, 0.05, 0.5], 0.8, -0.33, -0.05, color="black")]
    prop("Gas Cylinders", "Industrial", "Three gas bottles chained together.", parts, collider=[C([0.6, 1.3, 0.56], [0, 0.65, -0.05])], scatter={"randomYaw": True})


# =============================== Debris ===============================

def bricks_loose():
    random.seed(21)
    parts = []
    for i in range(6):
        c = random.choice(["brick", "#8a3e2a", "#a5503a"])
        x, z = random.uniform(-0.35, 0.35), random.uniform(-0.3, 0.3)
        if i < 4:
            parts.append(At([0.215, 0.065, 0.1], 0, x, z, color=c, yaw=random.uniform(0, 180)))
        else:
            parts.append(B([0.11, 0.065, 0.1], [x, 0.04, z], color=c, yaw=random.uniform(0, 180), roll=random.uniform(-15, 15)))
    parts.append(B([0.215, 0.065, 0.1], [0.05, 0.085, 0.02], color="brick", yaw=30, roll=18))
    prop("Loose Bricks", "Debris", "A few loose bricks and halves on the ground.", parts, collision="cosmetic", scatter={"randomYaw": True})


def brick_pile():
    random.seed(5)
    parts = []
    layers = [(3, 4), (3, 3), (2, 3), (1, 2)]
    y = 0.0
    for li, (nx, nz) in enumerate(layers):
        for i in range(nx):
            for j in range(nz):
                x = (i - (nx - 1) / 2) * 0.23 + random.uniform(-0.02, 0.02)
                z = (j - (nz - 1) / 2) * 0.11 + random.uniform(-0.01, 0.01)
                c = random.choice(["brick", "#8a3e2a", "#a5503a", "#7a3526"])
                parts.append(At([0.215, 0.065, 0.1], y, x, z, color=c, yaw=random.uniform(-6, 6) + (90 if li % 2 else 0) * 0))
        y += 0.068
    for k in range(4):
        parts.append(B([0.215, 0.065, 0.1], [random.uniform(-0.5, 0.5), 0.05, random.uniform(-0.35, 0.35)], color="brick", yaw=random.uniform(0, 180), roll=random.uniform(-20, 20)))
    prop("Brick Pile", "Debris", "A neat-ish stack of bricks with a few fallen off.", parts, collider=[C([0.72, 0.28, 0.48])],
         scatter={"randomYaw": True}, breakable=brk(80, "soft", by=["explosions", "streaks"], count=14, size=(0.08, 0.18), speed=4, colors=["brick", "#8a3e2a"]))


def cinder():
    def block(x, z, yaw, y0=0.0, roll=0, broken=False):
        L = 0.24 if broken else 0.39
        out = [B([L, 0.19, 0.19], [x, y0 + 0.095, z], color="concrete", mat="concrete", yaw=yaw, roll=roll, tex=2)]
        a = math.radians(yaw)
        for k in ((-0.09, 0.09) if not broken else (-0.04,)):
            ox, oz = k * math.cos(a), -k * math.sin(a)
            out.append(B([0.12, 0.005, 0.12], [x + ox, y0 + 0.19, z + oz], color="darkgrey", yaw=yaw, roll=roll))
        return out
    parts = block(-0.15, 0.0, 10) + block(0.25, 0.2, 75, broken=True) + block(0.1, -0.25, -20, broken=True, roll=25)
    parts += [B([0.08, 0.06, 0.07], [0.42, 0.03, -0.1], color="concrete", yaw=40), B([0.06, 0.04, 0.05], [-0.4, 0.02, 0.22], color="grey", yaw=10)]
    prop("Cinder Blocks", "Debris", "Two broken cinder blocks and a whole one.", parts, collision="cosmetic", scatter={"randomYaw": True})


def rubble(name, n, spread, height, desc, solid):
    random.seed(len(name) * 7 + n)
    parts = []
    for i in range(n):
        s = random.uniform(0.12, 0.42) * (1.0 if solid else 0.6)
        x, z = random.uniform(-spread, spread), random.uniform(-spread * 0.8, spread * 0.8)
        d = math.hypot(x / spread, z / spread)
        y = max(0.0, (1 - d) * height - s * 0.3)
        parts.append(B([s * random.uniform(0.8, 1.6), s * random.uniform(0.5, 0.9), s * random.uniform(0.8, 1.4)], [x, y + s * 0.3, z],
                       color=random.choice(["concrete", "#7f7c76", "#9a958c", "grey"]), mat="concrete", yaw=random.uniform(0, 180),
                       pitch=random.uniform(-20, 20), roll=random.uniform(-20, 20), tex=1.5))
    if solid:
        for i in range(3):
            parts.append(B([0.02, random.uniform(0.6, 1.0), 0.02], [random.uniform(-0.4, 0.4), height * 0.7, random.uniform(-0.3, 0.3)], color="rust",
                           pitch=random.uniform(-35, 35), roll=random.uniform(-35, 35)))
        parts.append(B([0.6, 0.08, 0.5], [0.2, height * 0.55, -0.1], color="concrete", mat="concrete", yaw=25, pitch=18))
        prop(name, "Debris", desc, parts, collider=[C([spread * 1.6, height * 0.9, spread * 1.3])], scatter={"randomYaw": True, "scale": [0.85, 1.2]})
    else:
        prop(name, "Debris", desc, parts, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.8, 1.2]})


def plank():
    parts = [B([2.4, 0.035, 0.18], [0, 0.03, 0], color="lightwood", mat="wood", roll=1.2),
             At([0.08, 0.036, 0.004], 0.03, 0.6, 0.0, color="darkgrey")]
    prop("Plank", "Debris", "A loose wooden plank.", parts, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.8, 1.1]})


def plank_pile():
    random.seed(9)
    parts = []
    y = 0.0
    for layer in range(4):
        for k in range(3 if layer % 2 == 0 else 2):
            parts.append(At([2.2 + random.uniform(-0.2, 0.2), 0.04, 0.18], y, random.uniform(-0.1, 0.1), (k - 1) * 0.22 + random.uniform(-0.03, 0.03),
                            color=random.choice(["lightwood", "wood", "pallet"]), mat="wood", yaw=random.uniform(-7, 7) + (0 if layer % 2 == 0 else 4)))
        y += 0.04
    parts += [B([2.2, 0.04, 0.18], [0.3, 0.12, 0.42], color="wood", mat="wood", yaw=24, roll=4)]
    parts += [At([0.1, 0.08, 0.7], 0.0, x, 0.0, color="darkwood") for x in (-0.8, 0.8)]
    prop("Plank Pile", "Debris", "A pile of timber planks on two bearers.", parts, collider=[C([2.3, 0.3, 0.7], [0, 0.15, 0])],
         scatter={"randomYaw": True}, breakable=brk(70, "wood", count=12, size=(0.06, 0.45), speed=3))


def newspaper():
    parts = [At([0.58, 0.004, 0.4], 0.003, 0, 0, color="newsprint"), At([0.56, 0.004, 0.39], 0.007, 0.04, 0.03, color="paper", yaw=14)]
    for k in range(5):
        parts.append(At([0.2, 0.002, 0.012], 0.011, -0.1 + 0.03, -0.12 + k * 0.035, color="darkgrey", yaw=14))
        parts.append(At([0.2, 0.002, 0.012], 0.011, 0.15, -0.1 + k * 0.035 + 0.06, color="grey", yaw=14))
    parts += [At([0.22, 0.002, 0.07], 0.011, -0.04, 0.16, color="black", yaw=14), B([0.3, 0.004, 0.4], [-0.3, 0.06, 0.05], color="newsprint", roll=-28)]
    prop("Newspaper", "Debris", "An open newspaper, one page lifting in the breeze.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=30)


def newspaper_stack():
    random.seed(13)
    parts = []
    y = 0.0
    for i in range(9):
        parts.append(At([0.3, 0.025, 0.42], y, random.uniform(-0.015, 0.015), random.uniform(-0.015, 0.015), color=random.choice(["newsprint", "paper", "#c9c4b4"]), yaw=random.uniform(-5, 5)))
        y += 0.025
    parts += [At([0.31, 0.012, 0.012], y, 0, 0.1, color="tan"), At([0.31, 0.012, 0.012], y, 0, -0.1, color="tan"),
              At([0.012, y, 0.012], 0, 0.155, 0.1, color="tan"), At([0.012, y, 0.012], 0, 0.155, -0.1, color="tan"),
              At([0.012, y, 0.012], 0, -0.155, 0.1, color="tan"), At([0.012, y, 0.012], 0, -0.155, -0.1, color="tan"),
              At([0.3, 0.006, 0.42], y, 0.05, 0.36, color="newsprint", yaw=30)]
    prop("Newspaper Stack", "Debris", "A bundle of old newspapers tied with string.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(5, "soft", by=["explosions", "streaks", "melee"], count=10, size=(0.08, 0.2), speed=3, colors=["newsprint", "paper"]))


def bottle_parts(x, z, color, lying=False, yaw=0.0):
    if not lying:
        return Cyl(0.075, 0.18, 0.0, x, z, color=color) + Cyl(0.05, 0.04, 0.18, x, z, color=color) + Cyl(0.03, 0.07, 0.22, x, z, color=color) + [At([0.032, 0.015, 0.032], 0.29, x, z, color="steel")]
    a = math.radians(yaw)
    def off(d):
        return [x + d * math.cos(a), 0.033, z - d * math.sin(a)]
    return [B([0.18, 0.066, 0.066], off(0), color=color, yaw=yaw), B([0.18, 0.066, 0.066], off(0), color=color, yaw=yaw, roll=45),
            B([0.1, 0.028, 0.028], off(0.13), color=color, yaw=yaw)]


def bottle():
    prop("Bottle", "Debris", "A green glass bottle.", bottle_parts(0, 0, "glass"), collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(1, "glass", count=6, size=(0.015, 0.04), speed=2.5), fade=35)


def bottles():
    parts = bottle_parts(-0.05, 0.02, "glass") + bottle_parts(0.06, -0.04, "amber") + bottle_parts(0.02, 0.09, "#cfe3dd") + bottle_parts(0.15, 0.12, "glass", lying=True, yaw=40)
    prop("Bottles", "Debris", "A few empty bottles, one knocked over.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(1, "glass", count=12, size=(0.015, 0.045), speed=2.5), fade=40)


def cans():
    parts = []
    for (x, z, c) in ((0.0, 0.0, "red"), (0.1, 0.06, "blue")):
        parts += Cyl(0.066, 0.12, 0.0, x, z, color=c) + [At([0.05, 0.005, 0.05], 0.12, x, z, color="chrome")]
    parts += [B([0.12, 0.066, 0.066], [-0.12, 0.033, 0.08], color="green", yaw=70), B([0.12, 0.066, 0.066], [-0.12, 0.033, 0.08], color="green", yaw=70, roll=45),
              B([0.07, 0.03, 0.07], [0.12, 0.015, -0.12], color="red", yaw=20)]
    prop("Soda Cans", "Debris", "Empty soda cans, one crushed flat.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=30)


def plastic_bag():
    parts = [At([0.3, 0.18, 0.22], 0.0, color="white", yaw=8), At([0.24, 0.1, 0.18], 0.15, 0.02, 0, color="white", yaw=-6),
             At([0.1, 0.08, 0.06], 0.24, 0.0, 0, color="white"), At([0.02, 0.06, 0.08], 0.29, -0.03, 0, color="white"), At([0.02, 0.06, 0.08], 0.29, 0.04, 0, color="white"),
             At([0.12, 0.002, 0.08], 0.12, 0.0, 0.111, color="red", yaw=8)]
    prop("Plastic Bag", "Debris", "A tied-off white plastic bag.", parts, collision="cosmetic", scatter={"randomYaw": True},
         breakable=brk(1, "soft", count=6, size=(0.04, 0.1), speed=2), fade=35)


def trash_bag():
    random.seed(17)
    parts = [At([0.5, 0.32, 0.42], 0, color="black", yaw=4), At([0.42, 0.18, 0.36], 0.3, 0.02, 0.0, color="black", yaw=-10),
             At([0.3, 0.1, 0.26], 0.46, 0.0, 0.02, color="black", yaw=20), At([0.08, 0.1, 0.08], 0.55, 0.0, 0.02, color="black"),
             At([0.46, 0.2, 0.46], 0.06, 0.0, 0.0, color="#1a1a1c", yaw=40), At([0.03, 0.08, 0.1], 0.64, -0.02, 0.02, color="black", yaw=15)]
    prop("Trash Bag", "Debris", "A stuffed black bin bag.", parts, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.85, 1.15]},
         breakable=brk(5, "soft", count=12, size=(0.04, 0.12), speed=3, colors=["black", "paper", "newsprint", "cardboard", "red"]))


def litter():
    random.seed(23)
    parts = []
    for i in range(10):
        s = random.uniform(0.05, 0.16)
        parts.append(B([s, 0.004, s * random.uniform(0.5, 1.2)], [random.uniform(-0.5, 0.5), 0.003 + i * 0.0004, random.uniform(-0.45, 0.45)],
                       color=random.choice(["paper", "white", "newsprint", "red", "yellow", "blue", "cardboard"]), yaw=random.uniform(0, 180), roll=random.uniform(-8, 8)))
    parts += [B([0.06, 0.04, 0.04], [0.2, 0.02, -0.2], color="white", yaw=30), B([0.08, 0.03, 0.05], [-0.25, 0.015, 0.1], color="red", yaw=-15)]
    prop("Litter", "Debris", "Scraps of paper, wrappers and a paper cup.", parts, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.8, 1.3]}, fade=30)


def cardboard(name, size, desc, health, solid=True, open_flaps=False):
    w, h, d = size
    parts = [At([w, h, d], 0, color="cardboard"),
             At([w + 0.004, 0.006, 0.05], h, 0, 0, color="tape"), At([0.05, h * 0.3, d + 0.004], h * 0.7, w / 2 - 0.0, 0, color="cardboard") if False else At([0.04, 0.003, d + 0.004], h, 0, 0, color="#8a6a44"),
             At([0.002 + w, 0.05, 0.004], h * 0.55, 0, d / 2, color="#5a4430"), At([w * 0.3, h * 0.25, 0.003], h * 0.25, -w * 0.2, d / 2 + 0.002, color="white")]
    if open_flaps:
        parts = [At([w, h, d], 0, color="cardboard"), B([w, 0.006, d / 2], [0, h + d / 4 * 0.5, d / 2 + d / 8], color="cardboard", pitch=-60),
                 B([w, 0.006, d / 2], [0, h + d / 4 * 0.5, -d / 2 - d / 8], color="cardboard", pitch=60), At([w - 0.02, 0.002, d - 0.02], h - 0.04, color="#6e5232")]
    kw = {}
    if solid:
        kw["collider"] = [C([w, h, d])]
    else:
        kw["collision"] = "cosmetic"
    prop(name, "Debris", desc, parts, scatter={"randomYaw": True, "yawJitter": 15, "scale": [0.92, 1.08]},
         breakable=brk(health, "soft", count=8, size=(0.05, max(w, d) * 0.4), speed=3, colors=["cardboard", "#8a6a44"]), **kw)


def cardboard_stack():
    parts = []
    for (s, y, x, z, yaw) in (([0.8, 0.6, 0.6], 0.0, 0.0, 0.0, 0), ([0.55, 0.42, 0.45], 0.6, -0.06, 0.03, 8), ([0.36, 0.26, 0.3], 1.02, 0.02, -0.02, -12),
                              ([0.55, 0.42, 0.45], 0.0, 0.7, 0.12, 20)):
        w, h, d = s
        parts += [At(s, y, x, z, color="cardboard", yaw=yaw), At([w + 0.004, 0.006, 0.05], y + h, x, z, color="tape", yaw=yaw)]
    prop("Cardboard Stack", "Debris", "A stack of moving boxes and one beside it.", parts, collider=[C([0.82, 1.28, 0.62]), C([0.6, 0.42, 0.55], [0.7, 0.21, 0.12])],
         scatter={"randomYaw": True}, breakable=brk(40, "soft", count=14, size=(0.08, 0.3), speed=3, colors=["cardboard", "#8a6a44", "tape"]))


def flat_cardboard():
    parts = [At([1.0, 0.008, 0.7], 0, color="cardboard", yaw=5), At([0.9, 0.008, 0.6], 0.008, 0.1, 0.08, color="#b08a5a", yaw=-12),
             At([0.6, 0.008, 0.45], 0.016, -0.12, -0.05, color="cardboard", yaw=30), At([0.5, 0.004, 0.03], 0.016, 0.1, 0.08, color="#5a4430", yaw=-12)]
    prop("Flattened Cardboard", "Debris", "Flattened boxes left on the ground.", parts, collision="cosmetic", scatter={"randomYaw": True}, fade=40)


# =============================== Furniture ===============================

def chair(name, color, mat, desc, sound):
    parts = [At([0.44, 0.04, 0.42], 0.44, color=color, mat=mat)]
    for x in (-0.19, 0.19):
        for z in (-0.18, 0.18):
            parts.append(At([0.04, 0.44, 0.04], 0, x, z, color=color, mat=mat))
    parts += [B([0.44, 0.34, 0.03], [0, 0.66, -0.2], color=color, mat=mat, pitch=-8),
              B([0.04, 0.24, 0.03], [-0.19, 0.55, -0.2], color=color, pitch=-8), B([0.04, 0.24, 0.03], [0.19, 0.55, -0.2], color=color, pitch=-8)]
    if mat == "wood":
        parts += [At([0.38, 0.03, 0.03], 0.15, 0, 0.18, color=color), At([0.38, 0.03, 0.03], 0.15, 0, -0.18, color=color)]
    prop(name, "Furniture", desc, parts, collider=[C([0.46, 0.48, 0.44]), C([0.46, 0.86, 0.08], [0, 0.43, -0.2])],
         scatter={"randomYaw": True}, breakable=brk(30, sound, count=8, size=(0.04, 0.2), speed=3.5))


def table():
    parts = [At([1.2, 0.05, 0.75], 0.71, color="wood", mat="wood"), At([1.12, 0.08, 0.67], 0.63, color="darkwood")]
    for x in (-0.53, 0.53):
        for z in (-0.3, 0.3):
            parts.append(At([0.06, 0.71, 0.06], 0, x, z, color="darkwood"))
    parts += [B([0.2, 0.004, 0.28], [0.25, 0.763, 0.1], color="newsprint", yaw=15)] + bottle_parts(-0.3, -0.1, "amber")
    for p in parts[-4:]:
        p["offset"][1] += 0.76
    prop("Table", "Furniture", "A wooden table with a paper and a bottle left on it.", parts,
         collider=[C([1.2, 0.13, 0.75], [0, 0.695, 0])] + [C([0.06, 0.63, 0.06], [x, 0.315, z]) for x in (-0.53, 0.53) for z in (-0.3, 0.3)],
         scatter={"randomYaw": True}, breakable=brk(70, "wood", count=12, size=(0.05, 0.35), speed=3.5))


def sofa():
    parts = [At([1.9, 0.2, 0.85], 0.08, color="#6a4a3a"), At([1.86, 0.12, 0.62], 0.28, 0, 0.08, color="#7c5a46"),
             At([0.6, 0.06, 0.6], 0.4, -0.6, 0.08, color="#82604c", yaw=3), At([0.6, 0.06, 0.6], 0.4, 0.0, 0.08, color="#7a5844"), At([0.6, 0.06, 0.6], 0.4, 0.6, 0.08, color="#82604c", yaw=-4),
             B([1.9, 0.55, 0.22], [0, 0.62, -0.32], color="#6a4a3a", pitch=-8),
             At([0.18, 0.32, 0.85], 0.28, -0.88, 0, color="#5e4234"), At([0.18, 0.32, 0.85], 0.28, 0.88, 0, color="#5e4234"),
             At([0.3, 0.004, 0.2], 0.46, 0.2, 0.15, color="#4a3529", yaw=30)]
    for x in (-0.85, 0.85):
        for z in (-0.35, 0.35):
            parts.append(At([0.07, 0.08, 0.07], 0, x, z, color="darkwood"))
    prop("Old Sofa", "Furniture", "A worn-out sofa someone dumped.", parts, collider=[C([1.96, 0.6, 0.86], [0, 0.3, 0]), C([1.96, 0.94, 0.26], [0, 0.47, -0.32])],
         scatter={"randomYaw": True}, breakable=brk(120, "soft", count=14, size=(0.08, 0.3), speed=3))


def filing_cabinet():
    parts = [At([0.47, 1.32, 0.62], 0, color="#7f8a86", mat="metal")]
    for i in range(4):
        y = 0.06 + i * 0.32
        parts += [At([0.43, 0.28, 0.01], y, 0, 0.31, color="#8d9894"), At([0.14, 0.03, 0.03], y + 0.2, 0, 0.32, color="chrome"), At([0.08, 0.04, 0.005], y + 0.12, 0, 0.317, color="white")]
    prop("Filing Cabinet", "Furniture", "A grey four-drawer filing cabinet.", parts, collider=[C([0.48, 1.32, 0.64])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8})


# =============================== Nature ===============================

def planter():
    parts = [At([1.0, 0.5, 1.0], 0, color="concrete", mat="concrete"), At([1.06, 0.06, 1.06], 0.5, color="lightgrey"),
             At([0.88, 0.03, 0.88], 0.48, color="#4a3a2a", mat="dirt"),
             At([0.6, 0.45, 0.55], 0.5, color="green", mat="dirt"), At([0.45, 0.35, 0.5], 0.75, 0.08, 0.05, color="#2f6a32", mat="dirt", yaw=30),
             At([0.3, 0.3, 0.3], 0.5, -0.25, 0.22, color="#3d7a3a", mat="dirt", yaw=10)]
    prop("Planter", "Nature", "A concrete planter with a bushy shrub.", parts, collider=[C([1.06, 0.56, 1.06])], scatter={"randomYaw": False, "yawStep": 90})


def log():
    parts = HCyl(0.44, 2.4, 0.22, color="#5b4129", mat="wood")
    parts += HCyl(0.38, 0.02, 0.22, 1.2, color="#b08a5a") + HCyl(0.38, 0.02, 0.22, -1.2, color="#b08a5a")
    parts += [
              B([0.08, 0.3, 0.08], [0.4, 0.44, 0.1], color="#5b4129", roll=30)]
    prop("Log", "Nature", "A fallen log.", parts, collider=[C([2.42, 0.44, 0.44])], scatter={"randomYaw": True})


def stump():
    parts = Cyl(0.62, 0.42, 0.0, color="#5b4129", mat="wood") + Cyl(0.56, 0.01, 0.42, color="#b08a5a")
    for a in (0, 120, 240):
        r = math.radians(a)
        parts.append(B([0.4, 0.12, 0.12], [0.35 * math.cos(r), 0.05, -0.35 * math.sin(r)], color="#5b4129", yaw=a, roll=-15))
    prop("Tree Stump", "Nature", "An old tree stump with roots.", parts, collider=[C([0.62, 0.44, 0.62])], scatter={"randomYaw": True, "scale": [0.85, 1.2]})


def hay():
    parts = [At([1.1, 0.5, 0.55], 0, color="#c8ac5c", mat="dirt", tex=2), At([1.12, 0.48, 0.5], 0.01, color="#d2b866", mat="dirt", tex=2)]
    for x in (-0.3, 0.3):
        parts += [At([0.02, 0.51, 0.56], 0, x, 0, color="#8a6a3a"), At([0.02, 0.01, 0.56], 0.5, x, 0, color="#8a6a3a")]
    prop("Hay Bale", "Nature", "A square bale of hay.", parts, collider=[C([1.12, 0.5, 0.56])], scatter={"randomYaw": True},
         breakable=brk(60, "soft", by=["explosions", "streaks", "melee", "bullets"], count=14, size=(0.05, 0.16), speed=3, colors=["#c8ac5c", "#d2b866"]))


def explosive_barrel():
    hazard = [At([0.3, 0.2, 0.01], 0.42, 0, 0.3, color="yellow"), At([0.14, 0.1, 0.012], 0.47, 0, 0.3, color="black"),
              At([0.01, 0.2, 0.3], 0.42, 0.3, 0, color="yellow"), At([0.012, 0.1, 0.14], 0.47, 0.3, 0, color="black")]
    oil_drum("Explosive Barrel", "red", "A red fuel drum. Shoot it and stand well back.",
             breakable={"health": 40, "breakBy": "all", "sound": "metal", "debris": {"count": 14, "size": [0.08, 0.25], "speed": 7, "colors": ["red", "darkgrey", "black"]},
                        "explode": {"radius": 5.5, "damage": 110}}, extra=hazard)


ALL = [trash_can, lambda: wheelie_bin("Wheelie Bin", "darkgreen", "green", "A green wheelie bin with a lid and two wheels."),
       lambda: wheelie_bin("Wheelie Bin Grey", "darkgrey", "grey", "A grey wheelie bin."),
       recycling_bin, dumpster, bench, street_lamp, hydrant, mailbox, cone, jersey, sawhorse, vending, cart, stop_sign, street_sign, bollard,
       lambda: oil_drum("Oil Drum", "blue", "A blue steel oil drum."), lambda: oil_drum("Rusty Drum", "rust", "A rusty old drum."), explosive_barrel,
       lambda: crate("Wooden Crate", 0.9, "A braced wooden crate.", 70), lambda: crate("Small Crate", 0.55, "A small wooden crate.", 40),
       pallet, pallet_stack, pipes, concrete_pipe, sandbags, tyre, tyre_stack, spool, gas_cylinders,
       bricks_loose, brick_pile, cinder, lambda: rubble("Rubble Pile", 18, 0.6, 0.45, "A heap of broken concrete with rebar sticking out.", True),
       lambda: rubble("Rubble", 9, 0.5, 0.05, "Scattered concrete chunks.", False), plank, plank_pile, newspaper, newspaper_stack, bottle, bottles, cans,
       plastic_bag, trash_bag, litter,
       lambda: cardboard("Cardboard Box Small", [0.36, 0.26, 0.3], "A small taped cardboard box.", 8, solid=False),
       lambda: cardboard("Cardboard Box", [0.55, 0.42, 0.45], "A taped cardboard box.", 15),
       lambda: cardboard("Cardboard Box Large", [0.8, 0.62, 0.6], "A big cardboard box.", 25),
       lambda: cardboard("Open Cardboard Box", [0.55, 0.4, 0.45], "An open, empty cardboard box.", 10, solid=False, open_flaps=True),
       cardboard_stack, flat_cardboard,
       lambda: chair("Plastic Chair", "white", None, "A white plastic garden chair.", "soft"),
       lambda: chair("Wooden Chair", "wood", "wood", "A plain wooden chair.", "wood"),
       table, sofa, filing_cabinet, planter, log, stump, hay]

if __name__ == "__main__":
    for f in ALL:
        f()
    names = set()
    for p in PROPS:
        assert p["name"] not in names, p["name"]
        names.add(p["name"])
        folder = os.path.join(OUT, p["category"])
        os.makedirs(folder, exist_ok=True)
        fn = "".join(ch for ch in p["name"] if ch.isalnum()) + ".json"
        with open(os.path.join(folder, fn), "w") as fh:
            # compact parts: one per line
            lines = ["{"]
            keys = [k for k in p if k != "parts"]
            for k in keys:
                lines.append(f'  "{k}": {json.dumps(p[k])},')
            lines.append('  "parts": [')
            for i, part in enumerate(p["parts"]):
                lines.append("    " + json.dumps(part) + ("," if i < len(p["parts"]) - 1 else ""))
            lines.append("  ]")
            lines.append("}")
            fh.write("\n".join(lines) + "\n")
    print(len(PROPS), "props")
