"""Generates the animated props (Props/Animated/<Name>.json) and the "Animated Props" Dev map for Call of Slop.

Usage: python3 tools/propviz/gen_animated.py [Props folder] [Maps/Dev folder]
Every moving or lit part carries an "anim" block (see Props/README.md "Animation"); colliders are listed by hand and
only cover still parts, so animation never changes collision.
"""
import json, math, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gen import B, At, Cyl, HCyl, C, brk, r3  # noqa: E402  (shared box helpers; gen.py only writes files under __main__)

OUT = sys.argv[1] if len(sys.argv) > 1 else "Props"
MAPS = sys.argv[2] if len(sys.argv) > 2 else os.path.join("Maps", "Dev")
CAT = "Animated"
PROPS = []


class Model:
    """Collects parts; add() returns the 1-based part numbers so anim blocks can name a "parent"."""

    def __init__(self):
        self.parts = []

    def add(self, parts, anim=None):
        if isinstance(parts, dict):
            parts = [parts]
        flat = []
        for p in parts:
            flat.extend(p if isinstance(p, list) else [p])
        first = len(self.parts) + 1
        for p in flat:
            if anim is not None:
                p["anim"] = dict(anim)
            self.parts.append(p)
        return list(range(first, first + len(flat)))

    def one(self, part, anim=None):
        return self.add(part, anim)[0]


def A(kind, **kw):
    d = {"type": kind}
    for k, v in kw.items():
        d[k] = r3(v) if isinstance(v, (list, tuple)) and len(v) == 3 and all(isinstance(x, (int, float)) for x in v) else v
    return d


def prop(name, desc, model, collision="solid", collider=None, breakable=None, scatter=None, fade=None):
    d = {"format": "call-of-slop-prop/1", "name": name, "category": CAT, "description": desc, "collision": collision}
    if collider is not None:
        d["collider"] = collider
    if scatter is not None:
        d["scatter"] = scatter
    if fade is not None:
        d["fadeDistance"] = fade
    if breakable is not None:
        d["breakable"] = breakable
    for part in model.parts:
        if "rotation" in part:  # prop files take -360 to 360; keep every angle in -180..180
            part["rotation"] = [round((a + 180.0) % 360.0 - 180.0, 3) for a in part["rotation"]]
    d["parts"] = model.parts
    assert len(model.parts) <= 128, (name, len(model.parts))
    PROPS.append(d)


# ---- Shape helpers ---------------------------------------------------------------------------------

def blades_z(n, length, width, thick, hub, color="grey", start=0.0):
    """n blades in the XY plane round hub (spin axis z)."""
    out = []
    for k in range(n):
        a = start + 360.0 * k / n
        r = math.radians(a)
        out.append(B([length, width, thick], [hub[0] + math.cos(r) * length / 2, hub[1] + math.sin(r) * length / 2, hub[2]], color=color, roll=a))
    return out


def blades_y(n, length, width, thick, hub, color="grey", twist=0.0, start=0.0):
    """n blades in the XZ plane round hub (spin axis y), tilted 'twist' degrees about their length."""
    out = []
    for k in range(n):
        a = start + 360.0 * k / n
        r = math.radians(a)
        out.append(B([length, thick, width], [hub[0] + math.cos(r) * length / 2, hub[1], hub[2] - math.sin(r) * length / 2], color=color, yaw=a, pitch=twist))
    return out


def blades_x(n, length, width, thick, hub, color="grey", start=0.0):
    """n blades in the YZ plane round hub (spin axis x)."""
    out = []
    for k in range(n):
        a = start + 360.0 * k / n
        r = math.radians(a)
        out.append(B([thick, length, width], [hub[0], hub[1] + math.cos(r) * length / 2, hub[2] + math.sin(r) * length / 2], color=color, pitch=a))
    return out


def ring_z(d, w, depth, center, color="steel", n=8):
    """A flat ring in the XY plane (facing z)."""
    r = d / 2
    chord = 2 * r * math.tan(math.pi / n) * 1.04
    out = []
    for k in range(n):
        a = 360.0 * k / n
        rr = math.radians(a)
        out.append(B([chord, w, depth], [center[0] + math.cos(rr) * r, center[1] + math.sin(rr) * r, center[2]], color=color, roll=a + 90))
    return out


def ring_x(d, w, depth, center, color="steel", n=8):
    """A flat ring in the YZ plane (facing x)."""
    r = d / 2
    chord = 2 * r * math.tan(math.pi / n) * 1.04
    out = []
    for k in range(n):
        a = 360.0 * k / n
        rr = math.radians(a)
        out.append(B([depth, w, chord], [center[0], center[1] + math.cos(rr) * r, center[2] + math.sin(rr) * r], color=color, pitch=a + 90))
    return out


def disc_z(d, depth, center, color="white"):
    """An octagonal disc facing z."""
    w = d
    return [B([w, w * 0.4142, depth], center, color=color, roll=a) for a in (0, 45, 90, 135)]


def disc_x(d, depth, center, color="white"):
    w = d
    return [B([depth, w * 0.4142, w], center, color=color, pitch=a) for a in (0, 45, 90, 135)]


def ball(d, center, color="steel"):
    """A chunky 'sphere': a vertical octagon and a lying one."""
    x, y, z = center
    return [B([d, d * 0.72, d * 0.4142], [x, y, z], color=color, yaw=a) for a in (0, 90)] + \
           [B([d * 0.72, d, d * 0.72], [x, y, z], color=color, yaw=45)]


def pole(h, d=0.1, x=0.0, z=0.0, y0=0.0, color="steel"):
    return At([d, h, d], y0, x, z, color=color)


# =============================== Fans and turbines ===============================

def ceiling_fan():
    m = Model()
    m.add([At([0.16, 0.06, 0.16], 0.64, color="white"), At([0.04, 0.4, 0.04], 0.26, color="white")])
    hub = [0, 0.2, 0]
    m.add(Cyl(0.2, 0.1, 0.15, color="white"), A("spin", rpm=120, pivot=hub))
    m.add(blades_y(4, 0.62, 0.15, 0.02, [0, 0.2, 0], color="darkwood", twist=10, start=0), A("spin", rpm=120, pivot=hub))
    m.add(Cyl(0.14, 0.08, 0.06, color="paper"), A("pulse", period=6, color="#fff2c0", off="paper", glow=0.4))
    prop("Ceiling Fan", "A ceiling fan with wooden blades and a light. Place it hanging: its top is 0.7 m up, so use y = ceiling - 0.7.", m,
         collision="cosmetic", fade=60, breakable=brk(30, "soft", count=8, size=(0.04, 0.14), colors=["darkwood", "white"]))


def desk_fan():
    m = Model()
    m.add([At([0.28, 0.04, 0.22], 0.0, color="darkgrey"), At([0.04, 0.3, 0.04], 0.04, 0, -0.04, color="grey")])
    head = m.one(B([0.1, 0.1, 0.12], [0, 0.38, -0.07], color="darkgrey"), A("swing", axis="y", pivot=[0, 0.38, -0.05], angle=35, period=8, jitter=0.1))
    m.add(ring_z(0.3, 0.015, 0.02, [0, 0.38, 0.06], color="chrome") + ring_z(0.3, 0.015, 0.02, [0, 0.38, -0.01], color="chrome"),
          A("follow", parent=head))
    m.add(blades_z(3, 0.13, 0.06, 0.01, [0, 0.38, 0.025], color="blue"), A("spin", axis="z", rpm=240, pivot=[0, 0.38, 0.025], parent=head))
    m.add(disc_z(0.05, 0.03, [0, 0.38, 0.03], color="darkgrey"), A("follow", parent=head))
    prop("Desk Fan", "A little desk fan whose head turns from side to side.", m, collision="cosmetic",
         breakable=brk(30, "soft", count=8, size=(0.03, 0.1), colors=["darkgrey", "blue", "chrome"]),
         scatter={"randomYaw": True, "scale": [0.95, 1.05]})


def pedestal_fan():
    m = Model()
    m.add([At([0.5, 0.05, 0.5], 0.0, color="black"), At([0.05, 1.05, 0.05], 0.05, color="chrome")])
    head = m.one(B([0.16, 0.16, 0.18], [0, 1.2, -0.1], color="black"), A("swing", axis="y", pivot=[0, 1.2, -0.06], angle=40, period=10, jitter=0.1))
    m.add(ring_z(0.5, 0.02, 0.02, [0, 1.2, 0.1], color="chrome") + ring_z(0.5, 0.02, 0.02, [0, 1.2, 0.0], color="chrome")
          + [B([0.5, 0.012, 0.01], [0, 1.2, 0.1], color="chrome"), B([0.012, 0.5, 0.01], [0, 1.2, 0.1], color="chrome")],
          A("follow", parent=head))
    m.add(blades_z(3, 0.22, 0.1, 0.01, [0, 1.2, 0.05], color="#3060a0"), A("spin", axis="z", rpm=220, pivot=[0, 1.2, 0.05], parent=head))
    prop("Pedestal Fan", "A standing fan on a chrome pole, turning its head across the room.", m,
         collider=[C([0.5, 0.06, 0.5]), C([0.08, 1.1, 0.08])], breakable=brk(45, "soft", count=10, size=(0.04, 0.14), colors=["black", "chrome", "lightgrey"]))


def wall_fan():
    m = Model()
    m.add([At([0.8, 0.8, 0.1], 0.0, color="galvanized", mat="metal"), B([0.66, 0.66, 0.02], [0, 0.4, 0.06], color="darkgrey")])
    m.add([B([0.04, 0.8, 0.06], [x, 0.4, 0.08], color="galvanized") for x in (-0.38, 0.38)] + [B([0.8, 0.04, 0.06], [0, y, 0.08], color="galvanized") for y in (0.02, 0.78)])
    hub = [0, 0.4, 0.09]
    m.add(disc_z(0.12, 0.05, hub, color="steel"), A("spin", axis="z", rpm=-180, pivot=hub, jitter=0.15))
    m.add(blades_z(5, 0.3, 0.12, 0.012, hub, color="steel"), A("spin", axis="z", rpm=-180, pivot=hub, jitter=0.15))
    m.add([B([0.7, 0.02, 0.02], [0, y, 0.13], color="black") for y in (0.14, 0.27, 0.4, 0.53, 0.66)])
    prop("Exhaust Fan Box", "A square galvanized exhaust fan with a grille, blades spinning behind it.", m,
         collider=[C([0.8, 0.8, 0.16], [0, 0.4, 0.02])], scatter={"randomYaw": False, "yawStep": 90})


def roof_turbine():
    m = Model()
    m.add(Cyl(0.36, 0.5, 0.0, color="galvanized", mat="metal") + Cyl(0.44, 0.05, 0.48, color="steel"))
    piv = [0, 0.8, 0]
    vanes = []
    for k in range(12):
        a = 360 * k / 12
        r = math.radians(a)
        vanes.append(B([0.03, 0.42, 0.14], [math.cos(r) * 0.26, 0.75, -math.sin(r) * 0.26], color="galvanized" if k % 2 == 0 else "steel", yaw=a + 25))
    m.add(vanes, A("spin", rpm=50, pivot=piv, jitter=0.3, wind=1))
    m.add(Cyl(0.5, 0.05, 0.52, color="galvanized") + Cyl(0.5, 0.05, 0.96, color="galvanized") + Cyl(0.3, 0.08, 1.0, color="steel")
          + [B([0.32, 0.03, 0.03], [0, 1.09, 0], color="darkgrey", yaw=a) for a in (0, 90)],
          A("spin", rpm=50, pivot=piv, jitter=0.3, wind=1))
    prop("Roof Turbine Vent", "A whirlybird roof vent: its round head of curved fins turns in the wind.", m,
         collider=[C([0.44, 0.55, 0.44]), C([0.58, 0.5, 0.58], [0, 0.78, 0])], scatter={"randomYaw": True, "scale": [0.9, 1.2]})


def floor_fan():
    m = Model()
    m.add([At([0.1, 0.5, 0.1], 0.0, -0.35, color="black"), At([0.1, 0.5, 0.1], 0.0, 0.35, color="black"), At([0.8, 0.06, 0.3], 0.0, color="black")])
    hub = [0, 0.85, 0]
    m.add(ring_z(0.86, 0.1, 0.26, hub, color="yellow"))
    m.add([B([0.86, 0.02, 0.02], [0, 0.85, 0.13], color="black"), B([0.02, 0.86, 0.02], [0, 0.85, 0.13], color="black")])
    m.add(disc_z(0.18, 0.12, hub, color="darkgrey"))
    m.add(blades_z(4, 0.38, 0.16, 0.015, [0, 0.85, 0.04], color="#c8c8c0", start=20), A("spin", axis="z", rpm=300, pivot=hub))
    prop("Drum Fan", "A big yellow industrial drum fan on a stand.", m,
         collider=[C([0.9, 1.3, 0.32])], breakable=brk(140, "metal", count=12, size=(0.06, 0.2), colors=["yellow", "black"]))


def ac_condenser():
    m = Model()
    m.add([At([0.9, 0.8, 0.9], 0.08, color="lightgrey", mat="metal"), At([0.95, 0.08, 0.95], 0.0, color="darkgrey")])
    m.add([B([0.8, 0.03, 0.01], [0, 0.25 + 0.08 * k, s * 0.452], color="grey") for s in (-1, 1) for k in range(7)]
          + [B([0.01, 0.03, 0.8], [s * 0.452, 0.25 + 0.08 * k, 0], color="grey") for s in (-1, 1) for k in range(7)])
    m.add([B([0.8, 0.03, 0.8], [0, 0.895, 0], color="black")])
    hub = [0, 0.92, 0]
    m.add(blades_y(4, 0.34, 0.12, 0.012, hub, color="black", twist=18), A("spin", rpm=200, pivot=hub, jitter=0.2))
    m.add([B([0.84, 0.015, 0.015], [0, 0.95, z], color="steel") for z in (-0.3, -0.15, 0, 0.15, 0.3)])
    m.add(At([0.06, 0.06, 0.2], 0.3, 0.48, 0.2, color="steel"))
    prop("AC Condenser Fan", "An air-conditioner condenser box with its top fan whirring.", m,
         collider=[C([0.95, 0.96, 0.95])], scatter={"randomYaw": False, "yawStep": 90})


def cooling_unit():
    m = Model()
    m.add([At([2.0, 1.0, 1.0], 0.4, color="steel", mat="metal")] + [At([0.08, 0.4, 0.08], 0.0, x, z, color="darkgrey") for x in (-0.9, 0.9) for z in (-0.4, 0.4)])
    for x in (-0.5, 0.5):
        hub = [x, 1.43, 0]
        m.add(Cyl(0.84, 0.06, 1.4, x=x, color="darkgrey"))
        m.add(blades_y(5, 0.38, 0.12, 0.012, hub, color="black", twist=20, start=10 + x * 40), A("spin", rpm=160, pivot=hub, jitter=0.15))
        m.add([B([0.82, 0.012, 0.012], [x, 1.47, z], color="steel") for z in (-0.25, 0, 0.25)])
    m.add(At([0.3, 0.2, 0.02], 0.7, 0, 0.51, color="white"))
    m.add(B([0.05, 0.05, 0.02], [0.2, 0.8, 0.515], color="green"), A("blink", period=1.6, duty=0.15, color="#40ff60"))
    prop("Cooling Unit", "An industrial cooling unit on legs, two fans on top and a status light.", m,
         collider=[C([2.0, 1.46, 1.0])], scatter={"randomYaw": False, "yawStep": 90})


# =============================== Wind ===============================

def windmill():
    m = Model()
    m.add([At([2.4, 0.5, 2.4], 0.0, color="#7a6a58", mat="brick")])
    body = []
    for k, (w, y0, h) in enumerate([(2.0, 0.5, 2.0), (1.7, 2.5, 2.0), (1.4, 4.5, 1.6)]):
        body += [At([w, h, w * 0.4142], y0, color="#b8ad98", mat="wood", yaw=a) for a in (0, 45, 90, 135)]
    m.add(body)
    m.add([At([1.6, 0.5, 1.6], 6.1, color="darkwood", yaw=45), At([1.2, 0.4, 1.2], 6.6, color="darkwood", yaw=45)])
    m.add([At([0.5, 1.0, 0.06], 0.5, 0, 1.0, color="darkwood")])
    hub = [0, 6.1, 1.05]
    m.add(B([0.3, 0.3, 0.4], hub, color="darkwood"), A("spin", axis="z", rpm=-14, pivot=hub, wind=0.7, jitter=0.15))
    sails = []
    for k in range(4):
        a = 45 + 90 * k
        r = math.radians(a)
        sails.append(B([4.6, 0.12, 0.08], [hub[0] + math.cos(r) * 2.4, hub[1] + math.sin(r) * 2.4, 1.15], color="darkwood", roll=a))
        ox, oy = math.cos(r) * 2.6 + math.cos(r + math.pi / 2) * 0.32, math.sin(r) * 2.6 + math.sin(r + math.pi / 2) * 0.32
        sails.append(B([3.4, 0.55, 0.03], [hub[0] + ox, hub[1] + oy, 1.2], color="#ddd6c4", roll=a))
    m.add(sails, A("spin", axis="z", rpm=-14, pivot=hub, wind=0.7, jitter=0.15))
    prop("Windmill", "A small brick and timber windmill; its four cloth sails turn slowly with the wind.", m,
         collider=[C([2.4, 0.5, 2.4]), C([2.0, 6.5, 2.0])], scatter={"randomYaw": False, "yawStep": 90})


def wind_turbine():
    m = Model()
    m.add([At([0.8, 0.3, 0.8], 0.0, color="concrete", mat="concrete")] + Cyl(0.28, 4.5, 0.3, color="white") + Cyl(0.2, 2.5, 4.8, color="white"))
    m.add([B([0.4, 0.4, 1.0], [0, 7.4, -0.1], color="white"), B([0.36, 0.06, 0.9], [0, 7.62, -0.1], color="lightgrey")])
    hub = [0, 7.4, 0.48]
    m.add(B([0.3, 0.3, 0.3], hub, color="white"), A("spin", axis="z", rpm=30, pivot=hub, wind=0.8, jitter=0.2))
    m.add(blades_z(3, 2.3, 0.22, 0.05, [0, 7.4, 0.52], color="white", start=90), A("spin", axis="z", rpm=30, pivot=hub, wind=0.8, jitter=0.2))
    m.add(B([0.08, 0.06, 0.08], [0, 7.67, -0.5], color="red"), A("blink", period=2, duty=0.2, color="#ff3020", glow=1.2, freezeDistance=200))
    prop("Small Wind Turbine", "A small three-bladed wind turbine on a white mast, red light on top.", m,
         collider=[C([0.8, 0.3, 0.8]), C([0.3, 7.3, 0.3])], scatter={"randomYaw": False})


def weather_vane():
    m = Model()
    m.add([At([0.3, 0.1, 0.3], 0.0, color="darkgrey"), pole(1.9, 0.05, color="black")])
    m.add([B([0.6, 0.015, 0.015], [0, 1.6, 0], color="black"), B([0.015, 0.015, 0.6], [0, 1.6, 0], color="black")])
    m.add([B([0.06, 0.06, 0.02], [0.33, 1.6, 0], color="black"), B([0.02, 0.06, 0.06], [0, 1.6, 0.33], color="black")])
    piv = [0, 2.0, 0]
    m.add([B([0.7, 0.02, 0.02], [0, 2.0, 0], color="black"), B([0.12, 0.1, 0.012], [0.36, 2.0, 0], color="black", roll=45),
           B([0.22, 0.2, 0.01], [-0.32, 2.0, 0], color="black"), B([0.18, 0.14, 0.01], [-0.06, 2.1, 0], color="rust")],
          A("swing", axis="y", pivot=piv, angle=30, period=7, wind=1, jitter=0.2))
    prop("Weather Vane", "A black iron weather vane with a rooster that swings round with the wind.", m,
         collider=[C([0.3, 0.1, 0.3]), C([0.08, 1.9, 0.08])], scatter={"randomYaw": True})


def anemometer():
    m = Model()
    m.add([At([0.25, 0.08, 0.25], 0.0, color="darkgrey"), pole(1.5, 0.05, color="galvanized")])
    piv = [0, 1.6, 0]
    cups = [Cyl(0.04, 0.1, 1.55, color="galvanized")]
    for k in range(3):
        a = 120 * k
        r = math.radians(a)
        cups.append(B([0.32, 0.015, 0.015], [math.cos(r) * 0.16, 1.6, -math.sin(r) * 0.16], color="galvanized", yaw=a))
        cups.append(B([0.08, 0.08, 0.08], [math.cos(r) * 0.32, 1.6, -math.sin(r) * 0.32], color="white", yaw=a))
    m.add(cups, A("spin", rpm=90, pivot=piv, wind=1, jitter=0.3))
    prop("Anemometer", "A wind gauge on a pole, three white cups spinning.", m, collider=[C([0.25, 0.08, 0.25]), C([0.08, 1.5, 0.08])])


def flag_pole(name, colors, desc):
    m = Model()
    m.add([At([0.4, 0.15, 0.4], 0.0, color="concrete"), pole(4.6, 0.08, color="white"), At([0.12, 0.08, 0.12], 4.6, color="yellow")])
    seg = 0.42
    prev = None
    for i in range(3):
        x0 = 0.04 + i * seg
        cols = colors
        piv = [x0, 4.15, 0]
        parts = [B([seg + 0.01, 0.3, 0.02], [x0 + seg / 2, 4.38, 0], color=cols[0]), B([seg + 0.01, 0.3, 0.02], [x0 + seg / 2, 4.08, 0], color=cols[1]),
                 B([seg + 0.01, 0.3, 0.02], [x0 + seg / 2, 3.78, 0], color=cols[2])]
        a = A("swing", axis="y", pivot=piv, angle=14 + i * 4, period=1.6, phase=i * 0.12, wind=1, jitter=0.15)
        if prev is not None:
            a["parent"] = prev
        ids = m.add(parts[0], a)
        a2 = A("follow", parent=ids[0])
        m.add(parts[1:], a2)
        prev = ids[0]
    prop(name, desc, m, collider=[C([0.4, 0.15, 0.4]), C([0.1, 4.6, 0.1])])


def banner():
    m = Model()
    m.add([pole(3.2, 0.08, x=-0.8, color="black"), pole(3.2, 0.08, x=0.8, color="black"), B([1.7, 0.05, 0.05], [0, 3.1, 0], color="black")])
    piv = [0, 3.08, 0]
    top = m.one(B([1.3, 0.9, 0.02], [0, 2.6, 0], color="red"), A("swing", axis="x", pivot=piv, angle=8, period=3.2, wind=1, jitter=0.2))
    m.add([B([0.9, 0.12, 0.025], [0, 2.7, 0], color="yellow"), B([0.12, 0.5, 0.025], [0, 2.55, 0], color="yellow")], A("follow", parent=top))
    bot = m.one(B([1.3, 0.6, 0.02], [0, 1.85, 0], color="red"), A("swing", axis="x", pivot=[0, 2.15, 0], angle=10, period=3.2, phase=0.15, wind=1, jitter=0.2, parent=top))
    m.add([B([0.25, 0.3, 0.021], [x, 1.42, 0], color="red", roll=45) for x in (-0.45, 0.0, 0.45)], A("follow", parent=bot))
    prop("Hanging Banner", "A red and gold cloth banner between two posts, rippling in the wind.", m,
         collider=[C([0.1, 3.2, 0.1], [-0.8, 1.6, 0]), C([0.1, 3.2, 0.1], [0.8, 1.6, 0])], scatter={"randomYaw": False, "yawStep": 90})


def shop_sign():
    m = Model()
    m.add([At([0.3, 0.1, 0.3], 0.0, color="darkgrey"), pole(2.7, 0.08, color="black"), B([1.0, 0.05, 0.05], [0.45, 2.6, 0], color="black"),
           B([0.3, 0.03, 0.03], [0.12, 2.48, 0], color="black", roll=-40)])
    piv = [0.55, 2.58, 0]
    sign = m.one(B([0.8, 0.5, 0.04], [0.55, 2.2, 0], color="darkwood", mat="wood"), A("swing", axis="x", pivot=piv, angle=12, period=2.6, wind=1, jitter=0.25))
    m.add([B([0.01, 0.12, 0.01], [x, 2.52, 0], color="black") for x in (0.25, 0.85)] + [B([0.6, 0.12, 0.05], [0.55, 2.25, 0], color="yellow"),
           B([0.3, 0.06, 0.05], [0.55, 2.1, 0], color="paper")], A("follow", parent=sign))
    prop("Swinging Shop Sign", "A wooden shop sign hanging from a bracket, swinging on its chains.", m,
         collider=[C([0.3, 0.1, 0.3]), C([0.1, 2.7, 0.1])], scatter={"randomYaw": False})


def lantern():
    m = Model()
    m.add([At([0.3, 0.08, 0.3], 0.0, color="black"), pole(2.4, 0.06, color="black"), B([0.7, 0.04, 0.04], [0.3, 2.36, 0], color="black"),
           B([0.25, 0.025, 0.025], [0.08, 2.24, 0], color="black", roll=-45)])
    piv = [0.55, 2.34, 0]
    body = m.one(B([0.012, 0.16, 0.012], [0.55, 2.26, 0], color="black"), A("swing", axis="z", pivot=piv, angle=7, period=2.2, wind=1, jitter=0.2))
    m.add([At([0.2, 0.03, 0.2], 1.86, 0.55, color="black"), At([0.22, 0.05, 0.22], 2.14, 0.55, color="black"), At([0.1, 0.06, 0.1], 2.18, 0.55, color="black")]
          + [At([0.02, 0.26, 0.02], 1.89, 0.55 + dx, dz, color="black") for dx in (-0.09, 0.09) for dz in (-0.09, 0.09)], A("follow", parent=body))
    m.add(At([0.15, 0.22, 0.15], 1.9, 0.55, color="amber"), A("flicker", rate=9, duty=0.9, color="#ffc860", off="#704010", glow=1.0, parent=body))
    prop("Hanging Lantern", "An iron lantern hanging from a post, swinging a little, its flame flickering.", m,
         collider=[C([0.3, 0.08, 0.3]), C([0.08, 2.4, 0.08])], breakable=brk(40, "glass", count=10, size=(0.03, 0.1), colors=["black", "amber", "#ffc860"]))


# =============================== Lights ===============================

def flicker_lamp():
    m = Model()
    m.add([At([0.4, 0.3, 0.4], 0.0, color="darkgrey", mat="metal")] + Cyl(0.16, 4.6, 0.3, color="darkgrey") + [B([0.9, 0.08, 0.08], [0.4, 4.85, 0], color="darkgrey"),
           B([0.5, 0.12, 0.3], [0.85, 4.84, 0], color="darkgrey")])
    m.add(B([0.4, 0.04, 0.22], [0.85, 4.77, 0], color="#d8d0b0"), A("flicker", rate=7, duty=0.82, color="#fff0c0", off="#403c30", glow=1.2, freezeDistance=160))
    prop("Flickering Street Lamp", "A tired street lamp whose bulb keeps flickering.", m, collider=[C([0.4, 0.3, 0.4]), C([0.18, 4.9, 0.18])],
         scatter={"randomYaw": False})


def neon_sign():
    m = Model()
    m.add([pole(2.2, 0.08, x=-0.9, color="black"), pole(2.2, 0.08, x=0.9, color="black"), At([2.1, 0.9, 0.08], 1.5, color="#1a1a22")])
    # "OPEN" in tubes: O, P, E, N as bars.
    def bars(x0, spec, color, anim):
        out = []
        for (dx, dy, w, h) in spec:
            out.append(B([w, h, 0.03], [x0 + dx, 1.95 + dy, 0.06], color=color))
        m.add(out, anim)
    o = [(0, 0.25, 0.3, 0.05), (0, -0.25, 0.3, 0.05), (-0.15, 0, 0.05, 0.55), (0.15, 0, 0.05, 0.55)]
    p = [(-0.12, 0, 0.05, 0.55), (0.02, 0.25, 0.3, 0.05), (0.02, 0.0, 0.3, 0.05), (0.16, 0.13, 0.05, 0.25)]
    e = [(-0.12, 0, 0.05, 0.55), (0.02, 0.25, 0.3, 0.05), (0.0, 0.0, 0.26, 0.05), (0.02, -0.25, 0.3, 0.05)]
    n = [(-0.13, 0, 0.05, 0.55), (0.13, 0, 0.05, 0.55), (0, 0, 0.05, 0.6, )]
    neon = A("blink", period=3.0, duty=0.8, color="#ff40a0", off="#401028", glow=1.6, freezeDistance=150)
    bars(-0.66, o, "#ff40a0", neon)
    bars(-0.22, p, "#ff40a0", neon)
    bars(0.22, e, "#ff40a0", neon)
    m.add([B([0.05, 0.6, 0.03], [0.66 - 0.13, 1.95, 0.06], color="#40e0ff"), B([0.05, 0.6, 0.03], [0.66 + 0.13, 1.95, 0.06], color="#40e0ff"),
           B([0.05, 0.64, 0.03], [0.66, 1.95, 0.06], color="#40e0ff", roll=24)],
          A("flicker", rate=6, duty=0.75, color="#40e0ff", off="#103038", glow=1.6, freezeDistance=150))
    m.add([B([1.9, 0.04, 0.03], [0, y, 0.06], color="#ffd040") for y in (1.58, 2.32)], A("cycle", period=1.2, steps=2, on=[0], color="#ffd040", off="#403010", glow=1.0))
    prop("Neon Open Sign", "A neon OPEN sign on two posts: pink letters blinking, a dodgy blue N, chasing gold bars.", m,
         collider=[C([0.1, 2.4, 0.1], [-0.9, 1.2, 0]), C([0.1, 2.4, 0.1], [0.9, 1.2, 0]), C([2.1, 0.9, 0.1], [0, 1.95, 0])],
         scatter={"randomYaw": False, "yawStep": 90})


def warning_beacon():
    m = Model()
    m.add([At([0.3, 0.1, 0.3], 0.0, color="black"), pole(1.3, 0.06, color="yellow"), At([0.18, 0.06, 0.18], 1.4, color="black")])
    m.add(Cyl(0.16, 0.16, 1.46, color="#c87010"), A("pulse", period=0.8, color="#ffb020", off="#603008", glow=1.0))
    piv = [0, 1.54, 0]
    m.add([B([0.12, 0.1, 0.02], [0, 1.54, 0.04], color="#ffe080")], A("spin", rpm=120, pivot=piv, glow=0.8, color="#ffe080"))
    m.add(At([0.12, 0.03, 0.12], 1.62, color="black"))
    prop("Warning Beacon", "An amber rotating warning beacon on a yellow post.", m, collider=[C([0.3, 0.1, 0.3]), C([0.1, 1.6, 0.1])],
         breakable=brk(35, "glass", count=8, size=(0.03, 0.08), colors=["#ffb020", "yellow", "black"]))


def traffic_light():
    m = Model()
    m.add([At([0.4, 0.15, 0.4], 0.0, color="darkgrey")] + Cyl(0.14, 2.6, 0.15, color="darkgrey") + [At([0.34, 0.95, 0.26], 2.7, color="#202020"),
           At([0.4, 0.03, 0.3], 3.65, color="#202020")])
    m.add([B([0.26, 0.04, 0.12], [0, 3.52 - 0.3 * k + 0.12, 0.16], color="#202020") for k in range(3)])
    lamps = [("red", "#ff2a1a", "#3a0806", [4, 5, 6, 7]), ("amber", "#ffb020", "#3a2606", [3]), ("green", "#30ff60", "#063a10", [0, 1, 2])]
    for k, (_, on, off, steps) in enumerate(lamps):
        m.add(B([0.18, 0.18, 0.03], [0, 3.47 - 0.3 * k, 0.14], color=on), A("cycle", period=16, steps=8, on=steps, color=on, off=off, glow=1.4, freezeDistance=200))
    prop("Cycling Traffic Light", "A traffic light on a pole, cycling green, amber, red.", m, collider=[C([0.4, 0.15, 0.4]), C([0.18, 2.7, 0.18]), C([0.36, 1.0, 0.3], [0, 3.2, 0])],
         scatter={"randomYaw": False, "yawStep": 90})


def crossing_lights():
    m = Model()
    m.add([At([0.4, 0.15, 0.4], 0.0, color="concrete"), pole(3.2, 0.1, color="white")])
    m.add([B([1.3, 0.16, 0.03], [0, 2.9, 0.06], color="white", roll=35), B([1.3, 0.16, 0.03], [0, 2.9, 0.06], color="white", roll=-35),
           B([0.1, 0.1, 0.04], [0, 2.9, 0.08], color="black")])
    m.add([B([0.9, 0.08, 0.08], [0, 2.1, 0], color="black"), B([0.5, 0.25, 0.04], [0, 1.75, 0.05], color="black")])
    for i, x in enumerate((-0.4, 0.4)):
        m.add([B([0.28, 0.28, 0.05], [x, 2.1, 0.06], color="black"), B([0.3, 0.06, 0.18], [x, 2.26, 0.12], color="black")])
        m.add(B([0.2, 0.2, 0.03], [x, 2.1, 0.1], color="#ff2010"), A("blink", period=1.2, duty=0.5, phase=0.5 * i, color="#ff2010", off="#300806", glow=1.6, freezeDistance=180))
    m.add([B([0.06, 0.06, 0.03], [0, 1.75, 0.08], color="white")], A("blink", period=0.6, duty=0.5, color="white", off="grey", glow=0.6))
    prop("Railway Crossing Lights", "A level-crossing sign with two red lamps flashing in turn.", m, collider=[C([0.4, 0.15, 0.4]), C([0.14, 3.2, 0.14])],
         scatter={"randomYaw": False, "yawStep": 90})


def police_light_bar():
    m = Model()
    m.add([At([0.06, 0.8, 0.5], 0.0, x, color="white") for x in (-0.7, 0.7)] + [At([1.6, 0.2, 0.06], 0.6, 0, 0, color="white"),
          B([1.6, 0.06, 0.07], [0, 0.7, 0], color="blue")])
    m.add([At([1.2, 0.05, 0.22], 0.8, color="black")])
    m.add(At([0.52, 0.12, 0.2], 0.85, -0.3, color="#c01818"), A("blink", period=0.5, duty=0.5, color="#ff2020", off="#401010", glow=1.8, freezeDistance=150))
    m.add(At([0.52, 0.12, 0.2], 0.85, 0.3, color="#1830c0"), A("blink", period=0.5, duty=0.5, phase=0.5, color="#3060ff", off="#101840", glow=1.8, freezeDistance=150))
    m.add(At([0.08, 0.12, 0.2], 0.85, 0, color="white"), A("blink", period=0.25, duty=0.3, color="white", off="grey", glow=1.0))
    prop("Police Light Bar", "A police barrier with a red and blue light bar flashing on top.", m,
         collider=[C([1.6, 0.98, 0.1], [0, 0.49, 0])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10},
         breakable=brk(120, "metal", count=12, size=(0.05, 0.16), colors=["white", "blue", "#c01818"]))


def alarm_light():
    m = Model()
    m.add([At([0.3, 0.1, 0.3], 0.0, color="darkgrey"), pole(1.8, 0.06, color="darkgrey"), At([0.26, 0.3, 0.12], 1.65, 0, 0.06, color="lightgrey")])
    m.add(Cyl(0.2, 0.06, 1.95, color="darkgrey"))
    m.add(Cyl(0.18, 0.2, 2.01, color="#a01010"), A("pulse", period=0.7, color="#ff2010", off="#400404", glow=1.4, freezeDistance=150))
    m.add([B([0.14, 0.12, 0.02], [0, 2.1, 0.05], color="#ffe0d0")], A("spin", rpm=180, pivot=[0, 2.1, 0], color="#ffb0a0", glow=1.0))
    m.add(At([0.1, 0.04, 0.1], 2.21, color="darkgrey"))
    prop("Alarm Light", "A red alarm light with a spinning reflector, pulsing on a pole.", m, collider=[C([0.3, 0.1, 0.3]), C([0.12, 2.2, 0.12])])


def lighthouse():
    m = Model()
    tower = []
    for (w, y0, h, col) in [(1.8, 0.0, 1.5, "white"), (1.6, 1.5, 1.5, "red"), (1.4, 3.0, 1.5, "white"), (1.2, 4.5, 1.2, "red")]:
        tower += [At([w, h, w * 0.4142], y0, color=col, yaw=a) for a in (0, 45, 90, 135)]
    m.add(tower)
    m.add([At([1.7, 0.1, 1.7], 5.7, color="darkgrey"), At([1.7, 0.1, 1.7], 5.7, color="darkgrey", yaw=45)])
    m.add([At([0.06, 0.9, 0.06], 5.8, 0.5 * math.cos(math.radians(a)), 0.5 * math.sin(math.radians(a)), color="black") for a in range(0, 360, 60)])
    m.add(Cyl(1.1, 0.12, 6.7, color="darkgrey") + Cyl(0.6, 0.3, 6.82, color="darkgrey") + [At([0.08, 0.3, 0.08], 7.1, color="black")])
    m.add(Cyl(0.5, 0.5, 5.95, color="#ffe8a0"), A("pulse", period=6, color="#fff4c8", off="#c0a050", glow=0.8, freezeDistance=250))
    piv = [0, 6.25, 0]
    m.add([B([0.18, 0.3, 0.3], [0.34, 6.25, 0], color="#fff8d0"), B([0.18, 0.3, 0.3], [-0.34, 6.25, 0], color="#fff8d0")],
          A("spin", rpm=10, pivot=piv, color="#fffae0", glow=1.6, freezeDistance=250))
    prop("Lighthouse", "A small red and white lighthouse; its lamp turns and glows.", m,
         collider=[C([1.8, 5.7, 1.8]), C([1.7, 0.1, 1.7], [0, 5.75, 0])], scatter={"randomYaw": False})


# =============================== Rotating machines ===============================

def radar_dish():
    m = Model()
    m.add([At([1.2, 0.2, 1.2], 0.0, color="concrete", mat="concrete")] + [At([0.1, 2.2, 0.1], 0.2, x, z, color="steel") for x in (-0.45, 0.45) for z in (-0.45, 0.45)]
          + [At([1.1, 0.1, 1.1], 2.4, color="steel"), At([0.4, 0.3, 0.4], 2.5, color="darkgrey")])
    piv = [0, 2.8, 0]
    dish = [B([0.15, 0.4, 0.15], [0, 3.0, 0], color="darkgrey"), B([2.6, 0.5, 0.06], [0, 3.4, 0.15], color="lightgrey", pitch=-15),
            B([2.4, 0.3, 0.05], [0, 3.82, 0.2], color="lightgrey", pitch=-35), B([2.4, 0.3, 0.05], [0, 3.0, 0.16], color="lightgrey", pitch=10),
            B([0.06, 0.06, 0.7], [0, 3.4, 0.45], color="steel"), B([0.12, 0.12, 0.12], [0, 3.4, 0.8], color="darkgrey")]
    m.add(dish, A("spin", rpm=12, pivot=piv))
    m.add(B([0.06, 0.06, 0.06], [0, 4.0, 0.2], color="red"), A("blink", period=1.5, duty=0.15, color="#ff3020", glow=1.2, parent=len(m.parts) - 5))
    prop("Radar Dish", "A rotating radar antenna on a steel tower.", m, collider=[C([1.2, 0.2, 1.2]), C([1.1, 2.6, 1.1], [0, 1.5, 0])],
         scatter={"randomYaw": False})


def satellite_dish():
    m = Model()
    m.add([At([0.8, 0.15, 0.8], 0.0, color="concrete"), At([0.14, 1.2, 0.14], 0.15, color="lightgrey"), At([0.3, 0.2, 0.3], 1.35, color="grey")])
    piv = [0, 1.5, 0]
    yoke = m.one(B([0.12, 0.2, 0.12], [0, 1.62, 0], color="grey"), A("swing", axis="y", pivot=piv, angle=50, period=14, jitter=0.2))
    disc = []
    for (d, z, y) in [(1.5, 0.12, 2.0), (1.2, 0.07, 2.0)]:
        disc += [B([d, d * 0.4142, 0.05], [0, y, z], color="white", roll=a, pitch=-25) for a in (0, 45, 90, 135)]
    disc += [B([0.04, 0.04, 0.8], [0, 2.1, 0.55], color="grey", pitch=-25), B([0.12, 0.12, 0.12], [0, 2.25, 0.88], color="darkgrey"),
             B([0.2, 0.3, 0.1], [0, 1.82, 0.0], color="grey")]
    m.add(disc, A("follow", parent=yoke))
    prop("Sweeping Satellite Dish", "A big white satellite dish on a post that slowly sweeps from side to side.", m,
         collider=[C([0.8, 0.15, 0.8]), C([0.3, 1.55, 0.3])], scatter={"randomYaw": True})


def street_clock():
    m = Model()
    m.add([At([0.5, 0.2, 0.5], 0.0, color="darkgreen")] + Cyl(0.14, 2.6, 0.2, color="darkgreen") + [At([0.7, 0.7, 0.3], 2.8, color="darkgreen"),
           At([0.3, 0.12, 0.2], 3.5, color="darkgreen"), At([0.1, 0.1, 0.1], 3.62, color="yellow")])
    for s in (1, -1):
        z = s * 0.16
        m.add(disc_z(0.56, 0.02, [0, 3.15, z], color="paper"))
        m.add([B([0.03, 0.06, 0.01], [0.24 * math.cos(math.radians(a)), 3.15 + 0.24 * math.sin(math.radians(a)), z + s * 0.012], color="black", roll=a + 90) for a in range(0, 360, 90)])
        hub = [0, 3.15, z]
        m.add(B([0.025, 0.2, 0.01], [0, 3.24, z + s * 0.02], color="black"), A("spin", axis=[0, 0, -s], rpm=0.5, pivot=hub))
        m.add(B([0.02, 0.24, 0.01], [0, 3.26, z + s * 0.03], color="black"), A("spin", axis=[0, 0, -s], rpm=6, pivot=hub))
        m.add(B([0.008, 0.25, 0.006], [0, 3.25, z + s * 0.04], color="red"), A("spin", axis=[0, 0, -s], rpm=60, pivot=hub))
    prop("Ticking Street Clock", "A green two-faced street clock on a post; the hands go round (a little faster than real time).", m,
         collider=[C([0.5, 0.2, 0.5]), C([0.16, 2.6, 0.16], [0, 1.5, 0]), C([0.7, 0.85, 0.3], [0, 3.2, 0])], scatter={"randomYaw": False, "yawStep": 90})


def ferris_wheel():
    m = Model()
    hub = [0, 2.8, 0]
    m.add([At([3.2, 0.2, 1.8], 0.0, color="concrete", mat="concrete")])
    for z in (-0.6, 0.6):
        m.add([B([0.12, 3.0, 0.12], [-0.8, 1.45, z], color="white", roll=-16), B([0.12, 3.0, 0.12], [0.8, 1.45, z], color="white", roll=16)])
    m.add(B([0.1, 0.1, 1.4], hub, color="steel"))
    wheel = []
    R = 2.2
    for z in (-0.4, 0.4):
        wheel += ring_z(2 * R, 0.08, 0.06, [0, hub[1], z], color="red", n=12)
        wheel += [B([R, 0.04, 0.04], [math.cos(math.radians(a)) * R / 2, hub[1] + math.sin(math.radians(a)) * R / 2, z], color="white", roll=a) for a in range(0, 360, 30)]
    wid = m.add(wheel, A("spin", axis="z", rpm=3, pivot=hub, jitter=0.2))
    gcolors = ["yellow", "blue", "green", "orange", "#9040c0", "red"]
    for k in range(6):
        a = math.radians(90 + 60 * k)
        px, py = math.cos(a) * R, hub[1] + math.sin(a) * R
        # Each gondola turns back against the wheel about its hanger, so it stays upright while the wheel carries it round.
        g = A("spin", axis="z", rpm=-3, pivot=[px, py, 0], jitter=0.2, parent=wid[0])
        m.add([B([0.04, 0.3, 0.04], [px, py - 0.15, 0], color="steel"), B([0.6, 0.35, 0.5], [px, py - 0.45, 0], color=gcolors[k]),
               B([0.66, 0.05, 0.56], [px, py - 0.25, 0], color="white")], g)
    lights = []
    for k in range(12):
        a = math.radians(30 * k)
        lights.append((k, B([0.08, 0.08, 0.08], [math.cos(a) * R, hub[1] + math.sin(a) * R, 0.46], color="#ffe080")))
    for k, part in lights:
        m.add(part, A("cycle", period=1.2, steps=3, on=[k % 3], color="#fff0a0", off="#806020", glow=1.2, parent=wid[0]))
    prop("Ferris Wheel Model", "A small fairground ferris wheel: it turns slowly, gondolas hang level, rim lights chase round.", m,
         collider=[C([3.2, 0.2, 1.8]), C([0.5, 2.9, 1.4], [0, 1.45, 0])], scatter={"randomYaw": False, "yawStep": 90})


def carousel():
    m = Model()
    piv = [0, 0, 0]
    m.add(Cyl(3.0, 0.2, 0.0, color="darkgrey"))
    plat = m.add(Cyl(2.8, 0.1, 0.2, color="#c04040") + Cyl(0.4, 2.4, 0.3, color="#e0c060"), A("spin", rpm=5, pivot=piv))
    m.add(Cyl(3.0, 0.3, 2.7, color="#e04060") + Cyl(2.2, 0.3, 3.0, color="white") + Cyl(0.8, 0.4, 3.3, color="#e04060"), A("follow", parent=plat[0]))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        x, z = math.cos(a) * 1.0, -math.sin(a) * 1.0
        yaw = 45 + 90 * k + 90
        m.add(At([0.05, 2.45, 0.05], 0.3, x, z, color="chrome"), A("follow", parent=plat[0]))
        horse = A("bob", distance=0.18, period=2.4, phase=0.25 * k, parent=plat[0])
        hc = ["white", "#5a3a20", "#c0a080", "black"][k]
        m.add([B([0.62, 0.3, 0.22], [x, 1.1, z], color=hc, yaw=yaw), B([0.14, 0.34, 0.14], [x + 0.28 * math.cos(math.radians(yaw)), 1.32, z - 0.28 * math.sin(math.radians(yaw))], color=hc, yaw=yaw, roll=-25),
               B([0.24, 0.12, 0.12], [x + 0.36 * math.cos(math.radians(yaw)), 1.5, z - 0.36 * math.sin(math.radians(yaw))], color=hc, yaw=yaw),
               B([0.3, 0.06, 0.24], [x, 1.27, z], color="red", yaw=yaw)]
              + [B([0.06, 0.4, 0.06], [x + dx * math.cos(math.radians(yaw)), 0.8, z - dx * math.sin(math.radians(yaw))], color=hc, yaw=yaw) for dx in (-0.22, 0.22)], horse)
    m.add([B([0.1, 0.1, 0.1], [math.cos(math.radians(a)) * 1.48, 2.85, -math.sin(math.radians(a)) * 1.48], color="#ffe080") for a in range(0, 360, 30)],
          A("cycle", period=0.9, steps=2, on=[0], color="#fff4b0", off="#806020", glow=1.0, parent=plat[0]))
    prop("Carousel", "A small merry-go-round: the platform turns and the horses ride up and down.", m,
         collider=[C([3.0, 0.3, 3.0]), C([0.5, 3.0, 0.5])], scatter={"randomYaw": True})


def pumpjack():
    m = Model()
    m.add([At([4.4, 0.25, 1.0], 0.0, color="concrete", mat="concrete")])
    m.add([B([0.14, 2.5, 0.14], [0.3, 1.45, z], color="#c0a020", roll=12) for z in (-0.3, 0.3)] + [B([0.14, 2.5, 0.14], [-0.3, 1.45, z], color="#c0a020", roll=-12) for z in (-0.3, 0.3)])
    piv = [0, 2.7, 0]
    beam = m.one(B([3.6, 0.3, 0.24], [0.2, 2.75, 0], color="#c0a020"), A("swing", axis="z", pivot=piv, angle=14, period=5, jitter=0.15))
    m.add([B([0.3, 0.9, 0.3], [2.05, 2.5, 0], color="#c0a020", roll=-10), B([0.2, 0.3, 0.32], [2.0, 2.0, 0], color="darkgrey"),
           B([0.04, 1.6, 0.04], [2.15, 1.25, 0], color="black")], A("follow", parent=beam))
    m.add([At([0.9, 1.0, 0.9], 0.25, -1.7, color="darkgrey"), At([0.6, 0.4, 0.5], 0.25, -1.0, color="grey")])
    crank = [-1.7, 1.0, 0]
    m.add([B([1.3, 0.3, 0.12], [-1.7, 1.0, z], color="red") for z in (-0.52, 0.52)] + [B([0.4, 0.5, 0.14], [-2.25, 1.0, z], color="darkgrey") for z in (-0.52, 0.52)],
          A("spin", axis="z", rpm=12, pivot=crank, jitter=0.15))
    m.add([At([0.3, 0.6, 0.3], 0.25, 2.15, color="darkgrey"), At([0.5, 0.1, 0.5], 0.8, 2.15, color="darkgrey")])
    prop("Oil Pumpjack", "A nodding-donkey oil pump: the walking beam rocks while the counterweights turn.", m,
         collider=[C([4.4, 0.25, 1.0]), C([1.0, 1.25, 1.0], [-1.7, 0.75, 0]), C([0.8, 2.5, 0.8], [0, 1.4, 0])], scatter={"randomYaw": False, "yawStep": 90})


def conveyor():
    m = Model()
    m.add([B([3.0, 0.1, 0.06], [0, 0.85, z], color="#e0b020") for z in (-0.38, 0.38)] + [At([0.08, 0.8, 0.08], 0.0, x, z, color="darkgrey") for x in (-1.3, 1.3) for z in (-0.38, 0.38)]
          + [B([2.8, 0.04, 0.04], [0, 0.3, z], color="darkgrey") for z in (-0.38, 0.38)])
    for i in range(10):
        x = -1.35 + i * 0.3
        hub = [x, 0.86, 0]
        m.add([B([0.11, 0.11, 0.7], hub, color="chrome", roll=a) for a in (0, 45)], A("spin", axis="z", rpm=-90, pivot=hub))
    m.add([At([0.5, 0.3, 0.4], 0.92, -0.6, color="cardboard"), At([0.4, 0.25, 0.36], 0.92, 0.7, color="cardboard"), At([0.42, 0.02, 0.06], 1.22, -0.6, color="tape")])
    prop("Conveyor Rollers", "A roller conveyor with its rollers turning, a couple of boxes on top.", m,
         collider=[C([3.0, 0.92, 0.84])], scatter={"randomYaw": False, "yawStep": 90})


def generator():
    m = Model()
    m.add([At([1.6, 0.12, 0.8], 0.0, color="darkgrey")])
    sh = A("shake", axis=[0.4, 1, 0.2], distance=0.008, period=0.07, jitter=0.2)
    body = m.add([At([1.4, 0.6, 0.7], 0.12, color="orange", mat="metal"), At([1.4, 0.06, 0.74], 0.72, color="darkgrey"), At([0.3, 0.3, 0.3], 0.78, -0.4, color="black"),
                  At([0.5, 0.2, 0.3], 0.78, 0.25, color="grey"), At([0.4, 0.3, 0.02], 0.3, 0.3, 0.355, color="black")], sh)
    m.add([At([0.08, 0.5, 0.08], 0.85, 0.55, 0, color="rust"), At([0.12, 0.08, 0.12], 1.35, 0.55, color="black")], A("follow", parent=body[0]))
    m.add(At([0.18, 0.12, 0.04], 1.1, 0.55, 0, color="#404040"), A("bob", distance=0.03, period=0.5, parent=body[0]))
    m.add(B([0.05, 0.05, 0.02], [0.5, 0.5, 0.37], color="#30ff60"), A("blink", period=1.0, duty=0.5, color="#40ff60", off="#063a10", glow=1.0, parent=body[0]))
    m.add(B([0.05, 0.05, 0.02], [0.4, 0.5, 0.37], color="#ffb020"), A("flicker", rate=5, duty=0.6, color="#ffb020", off="#3a2606", glow=1.0, parent=body[0]))
    prop("Running Generator", "A diesel generator chugging away: the body shakes, the exhaust flap rattles, lights blink.", m,
         collider=[C([1.6, 0.85, 0.8])], scatter={"randomYaw": True, "yawStep": 90},
         breakable=brk(400, "metal", by=["explosions", "streaks"], count=16, size=(0.08, 0.3), colors=["orange", "darkgrey", "black"]))


def cement_mixer():
    m = Model()
    m.add([At([0.08, 0.08, 1.2], 0.32, x, 0, color="darkgrey") for x in (-0.3, 0.3)] + [B([0.1, 0.36, 0.36], [x, 0.2, -0.4], color="rubber") for x in (-0.4, 0.4)]
          + [At([0.08, 0.5, 0.08], 0.0, 0, 0.5, color="darkgrey"), At([0.06, 0.4, 0.06], 0.4, 0, -0.3, color="orange"), At([0.3, 0.3, 0.3], 0.4, 0, -0.45, color="orange"),
             B([0.6, 0.06, 0.1], [0, 0.2, -0.4], color="steel")])
    # The drum is an octagon along z, tilted 35 degrees up (pitch), turning about that tilted axis.
    ax = [0, math.sin(math.radians(35)), math.cos(math.radians(35))]
    c = [0, 0.95, 0.05]
    drum = []
    for (d, L, off) in [(0.75, 0.55, 0.0), (0.55, 0.25, 0.38), (0.38, 0.12, 0.55)]:
        cy, cz = c[1] + ax[1] * off, c[2] + ax[2] * off
        drum += [B([d, d * 0.4142, L], [0, cy, cz], color="orange", roll=a, pitch=-35) for a in (0, 45, 90, 135)]
    drum += [B([0.05, 0.04, 0.6], [0.38, c[1], c[2]], color="darkgrey", pitch=-35), B([0.05, 0.04, 0.6], [-0.38, c[1], c[2]], color="darkgrey", pitch=-35)]
    m.add(drum, A("spin", axis=ax, rpm=24, pivot=c))
    prop("Cement Mixer", "A small orange cement mixer, its tilted drum turning.", m, collider=[C([1.2, 1.3, 0.8], [0, 0.65, 0])],
         breakable=brk(260, "metal", by=["explosions", "streaks"], count=12, size=(0.06, 0.22), colors=["orange", "darkgrey"]))


def washer():
    m = Model()
    sh = A("shake", axis=[1, 0.3, 0.2], distance=0.01, period=0.11, jitter=0.25)
    body = m.add([At([0.6, 0.85, 0.6], 0.0, color="white"), At([0.6, 0.06, 0.6], 0.85, color="lightgrey"), At([0.5, 0.1, 0.02], 0.72, 0, 0.3, color="lightgrey")]
                 + ring_z(0.38, 0.04, 0.03, [0, 0.42, 0.31], color="chrome"), sh)
    m.add(disc_z(0.32, 0.02, [0, 0.42, 0.305], color="#506070"), A("follow", parent=body[0]))
    hub = [0, 0.42, 0.31]
    m.add([B([0.28, 0.05, 0.01], [0, 0.42, 0.318], color="#e0e8f0"), B([0.05, 0.28, 0.01], [0, 0.42, 0.318], color="#c0d0e0")], A("spin", axis="z", rpm=90, pivot=hub, parent=body[0]))
    m.add(B([0.04, 0.03, 0.01], [0.18, 0.77, 0.312], color="#30ff60"), A("blink", period=2, duty=0.7, color="#40ff60", off="#063a10", glow=0.8, parent=body[0]))
    prop("Running Washer", "A front-loading washing machine on its spin cycle: the drum turns and the whole thing shudders.", m,
         collider=[C([0.62, 0.9, 0.62])], scatter={"randomYaw": False, "yawStep": 90},
         breakable=brk(140, "metal", count=10, size=(0.05, 0.18), colors=["white", "lightgrey", "chrome"]))


def water_wheel():
    m = Model()
    m.add([At([2.6, 0.3, 0.3], 0.0, z=z, color="concrete", mat="concrete") for z in (-0.6, 0.6)] + [At([0.2, 1.7, 0.2], 0.3, 0, z, color="darkwood") for z in (-0.6, 0.6)])
    m.add(At([2.4, 0.06, 1.0], 0.02, color="#2a5070"))
    hub = [0, 1.9, 0]
    w = []
    for z in (-0.35, 0.35):
        w += [B([0.08, 2.8, 0.12], [0, 1.9, z], color="darkwood", roll=a) for a in (0, 45, 90, 135)]
    w += [B([0.5, 0.06, 0.8], [math.cos(math.radians(a)) * 1.3, 1.9 + math.sin(math.radians(a)) * 1.3, 0], color="wood", roll=a + 90) for a in range(0, 360, 30)]
    w += [B([0.2, 0.2, 1.3], hub, color="darkgrey")]
    m.add(w, A("spin", axis="z", rpm=-6, pivot=hub, jitter=0.15))
    prop("Water Wheel", "A wooden paddle wheel turning slowly on its frame.", m, collider=[C([2.6, 0.3, 0.3], [0, 0.15, -0.6]), C([2.6, 0.3, 0.3], [0, 0.15, 0.6]),
         C([0.24, 2.0, 0.24], [0, 1.0, -0.6]), C([0.24, 2.0, 0.24], [0, 1.0, 0.6])], scatter={"randomYaw": False, "yawStep": 90})


def barber_pole():
    m = Model()
    m.add([At([0.3, 0.1, 0.2], 1.0, 0, -0.06, color="chrome"), At([0.06, 1.0, 0.06], 0.0, 0, -0.14, color="black"), At([0.3, 0.08, 0.3], 0.0, 0, -0.14, color="black")])
    m.add(Cyl(0.22, 0.8, 1.1, color="white"))
    piv = [0, 1.5, 0]
    stripes = []
    for k in range(8):
        a = 45 * k + 22.5
        r = math.radians(a)
        stripes.append(B([0.012, 0.74, 0.06], [math.cos(r) * 0.105, 1.5, -math.sin(r) * 0.105], color="red" if k % 2 == 0 else "blue", yaw=a, pitch=28))
    m.add(stripes, A("spin", rpm=40, pivot=piv))
    m.add([At([0.26, 0.08, 0.26], 1.9, color="chrome"), At([0.12, 0.08, 0.12], 1.98, color="chrome")])
    m.add(Cyl(0.18, 0.04, 1.06, color="#fff0d0"), A("pulse", period=3, color="#fff8e0", off="#c0b080", glow=0.6))
    prop("Barber Pole", "A spinning red, white and blue barber pole on a stand.", m, collider=[C([0.3, 2.06, 0.32], [0, 1.03, -0.06])],
         breakable=brk(40, "glass", count=8, size=(0.03, 0.1), colors=["white", "red", "blue"]))


def rotating_sign():
    m = Model()
    m.add([At([0.5, 0.2, 0.5], 0.0, color="concrete")] + Cyl(0.18, 3.2, 0.2, color="steel"))
    piv = [0, 4.0, 0]
    board = m.one(B([1.6, 0.8, 0.12], [0, 4.0, 0], color="#d02020"), A("spin", rpm=8, pivot=piv))
    m.add([B([1.3, 0.24, 0.13], [0, 4.1, 0], color="yellow"), B([0.8, 0.12, 0.13], [0, 3.82, 0], color="white"), B([1.7, 0.06, 0.14], [0, 4.43, 0], color="white"),
           B([1.7, 0.06, 0.14], [0, 3.57, 0], color="white")], A("follow", parent=board))
    m.add([B([0.06, 0.06, 0.135], [-0.78 + 0.13 * k, 4.43, 0], color="#ffe080") for k in range(13)][::2],
          A("cycle", period=1.0, steps=2, on=[0], color="#fff4b0", off="#806020", glow=1.0, parent=board))
    prop("Rotating Diner Sign", "A red diner sign that turns slowly on top of its pole, bulbs chasing.", m,
         collider=[C([0.5, 0.2, 0.5]), C([0.2, 3.4, 0.2])], scatter={"randomYaw": True})


def piston_pump():
    m = Model()
    m.add([At([1.4, 0.2, 0.8], 0.0, color="darkgrey", mat="metal"), At([0.4, 1.1, 0.4], 0.2, -0.4, color="steel"), At([0.5, 0.1, 0.5], 1.3, -0.4, color="darkgrey"),
           At([0.24, 0.8, 0.24], 0.2, 0.45, 0.0, color="darkgrey")])
    m.add([At([0.12, 0.9, 0.12], 1.05, -0.4, color="chrome"), At([0.3, 0.12, 0.2], 1.95, -0.4, color="red")], A("bob", distance=0.25, period=1.5, jitter=0.1))
    hub = [0.45, 1.05, 0.2]
    m.add([B([0.7, 0.7, 0.06], hub, color="red", roll=a) for a in (0, 45)] + [B([0.1, 0.1, 0.08], [0.7, 1.05, 0.22], color="darkgrey")],
          A("spin", axis="z", rpm=-40, pivot=hub, jitter=0.1))
    prop("Piston Pump", "A steam-style pump: the piston rod goes up and down beside a turning flywheel.", m, collider=[C([1.4, 1.4, 0.8])],
         scatter={"randomYaw": False, "yawStep": 90})


def pinwheel():
    m = Model()
    m.add([At([0.02, 0.9, 0.02], 0.0, color="lightwood")])
    hub = [0, 0.9, 0.03]
    vanes = []
    for k, col in enumerate(["red", "yellow", "blue", "green"]):
        a = 90 * k
        r = math.radians(a)
        vanes.append(B([0.16, 0.1, 0.005], [math.cos(r) * 0.09, 0.9 + math.sin(r) * 0.09, 0.03], color=col, roll=a + 20))
    vanes.append(B([0.03, 0.03, 0.02], hub, color="white"))
    m.add(vanes, A("spin", axis="z", rpm=150, pivot=hub, wind=1, jitter=0.4))
    prop("Pinwheel", "A child's paper pinwheel on a stick, spinning in the wind.", m, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.9, 1.2]},
         breakable=brk(5, "soft", count=4, size=(0.02, 0.06)))


def sprinkler():
    m = Model()
    m.add([At([0.2, 0.04, 0.2], 0.0, color="green"), At([0.04, 0.2, 0.04], 0.04, color="darkgreen")])
    piv = [0, 0.26, 0]
    m.add([B([0.5, 0.025, 0.025], piv, color="darkgreen"), B([0.04, 0.05, 0.04], [0.25, 0.27, 0], color="black"), B([0.04, 0.05, 0.04], [-0.25, 0.27, 0], color="black")],
          A("spin", rpm=40, pivot=piv, jitter=0.2))
    m.add([B([0.04, 0.12, 0.04], [0.28, 0.36, 0], color="#a0d0ff"), B([0.04, 0.12, 0.04], [-0.28, 0.36, 0], color="#a0d0ff")], A("spin", rpm=40, pivot=piv, jitter=0.2))
    prop("Lawn Sprinkler", "A rotating garden sprinkler, its arms spraying as they turn.", m, collision="cosmetic", fade=45)


# =============================== Swinging and bobbing ===============================

def rocking_chair():
    m = Model()
    sw = A("swing", axis="x", pivot=[0, 0.75, 0], angle=7, period=2.6, jitter=0.2)
    parts = [B([0.06, 0.06, 0.95], [s * 0.26, 0.05, 0], color="darkwood", pitch=0) for s in (-1, 1)]
    parts += [At([0.05, 0.4, 0.05], 0.06, s * 0.24, z, color="darkwood") for s in (-1, 1) for z in (-0.2, 0.2)]
    parts += [At([0.56, 0.05, 0.48], 0.46, color="wood"), B([0.56, 0.7, 0.04], [0, 0.85, -0.28], color="wood", pitch=-12)]
    parts += [B([0.04, 0.62, 0.04], [s * 0.26, 0.83, -0.27], color="darkwood", pitch=-12) for s in (-1, 1)]
    parts += [B([0.05, 0.04, 0.42], [s * 0.27, 0.68, 0.0], color="darkwood") for s in (-1, 1)]
    parts += [B([0.4, 0.06, 0.4], [0, 0.51, 0.0], color="red")]
    m.add(parts, sw)
    prop("Rocking Chair", "A wooden rocking chair with a red cushion, rocking gently by itself.", m, collider=[C([0.6, 0.5, 0.9])],
         scatter={"randomYaw": True}, breakable=brk(60, "wood", count=12, size=(0.04, 0.3), colors=["wood", "darkwood"]))


def hammock():
    m = Model()
    m.add([At([0.12, 1.5, 0.12], 0.0, x, 0, color="darkwood") for x in (-1.5, 1.5)] + [At([0.4, 0.06, 0.4], 0.0, x, 0, color="darkwood") for x in (-1.5, 1.5)])
    piv = [0, 1.3, 0]
    sw = A("swing", axis="x", pivot=piv, angle=9, period=3.4, wind=0.6, jitter=0.2)
    bed = m.one(B([1.6, 0.06, 0.7], [0, 0.65, 0], color="#d08030"), sw)
    m.add([B([0.75, 0.04, 0.6], [s * 1.0, 0.85, 0], color="#d08030", roll=s * 22) for s in (-1, 1)]
          + [B([0.5, 0.02, 0.02], [s * 1.35, 1.17, z], color="tan", roll=s * 40) for s in (-1, 1) for z in (-0.25, 0.25)]
          + [B([1.4, 0.065, 0.12], [0, 0.66, 0.2], color="#2060a0")], A("follow", parent=bed))
    prop("Hammock", "A striped hammock between two posts, swaying.", m, collider=[C([0.16, 1.5, 0.16], [-1.5, 0.75, 0]), C([0.16, 1.5, 0.16], [1.5, 0.75, 0])],
         scatter={"randomYaw": True})


def wind_chimes():
    m = Model()
    m.add([At([0.4, 0.06, 0.4], 0.0, color="darkwood"), At([0.04, 1.7, 0.04], 0.06, -0.16, color="darkwood"), B([0.5, 0.04, 0.04], [0.06, 1.74, 0], color="darkwood")])
    piv = [0.15, 1.72, 0]
    top = m.one(B([0.22, 0.03, 0.22], [0.15, 1.6, 0], color="lightwood"), A("swing", axis="z", pivot=piv, angle=6, period=2.4, wind=1, jitter=0.2))
    m.add(B([0.006, 0.11, 0.006], [0.15, 1.66, 0], color="black"), A("follow", parent=top))
    for k in range(5):
        a = math.radians(72 * k)
        x, z = 0.15 + math.cos(a) * 0.08, math.sin(a) * 0.08
        L = 0.3 + 0.06 * k
        m.add([B([0.03, L, 0.03], [x, 1.57 - L / 2, z], color="chrome")],
              A("swing", axis=[math.cos(a + 1.2), 0, math.sin(a + 1.2)], pivot=[x, 1.585, z], angle=12, period=1.1 + 0.13 * k, phase=0.2 * k, wind=1, parent=top))
    m.add(B([0.08, 0.1, 0.01], [0.15, 1.08, 0], color="lightwood"), A("swing", axis="x", pivot=[0.15, 1.58, 0], angle=18, period=1.9, wind=1, parent=top))
    prop("Wind Chimes", "Chrome wind chimes on a stand, each tube swinging on its own.", m, collision="cosmetic", fade=50,
         breakable=brk(15, "metal", count=6, size=(0.02, 0.1), colors=["chrome", "lightwood"]))


def grandfather_clock():
    m = Model()
    m.add([At([0.55, 0.25, 0.35], 0.0, color="darkwood", mat="wood"), At([0.46, 1.25, 0.28], 0.25, color="darkwood", mat="wood"), At([0.56, 0.5, 0.34], 1.5, color="darkwood", mat="wood"),
           At([0.6, 0.1, 0.38], 2.0, color="darkwood"), At([0.3, 0.1, 0.2], 2.1, color="darkwood")])
    m.add([At([0.32, 1.0, 0.01], 0.38, 0, 0.142, color="#20160c"), B([0.36, 0.04, 0.02], [0, 1.4, 0.15], color="#c8a040"), B([0.36, 0.04, 0.02], [0, 0.36, 0.15], color="#c8a040")])
    m.add(disc_z(0.38, 0.02, [0, 1.75, 0.175], color="paper"))
    hub = [0, 1.75, 0.19]
    m.add(B([0.02, 0.12, 0.01], [0, 1.8, 0.19], color="black"), A("spin", axis="-z", rpm=0.2, pivot=hub))
    m.add(B([0.016, 0.16, 0.01], [0, 1.82, 0.195], color="black"), A("spin", axis="-z", rpm=2, pivot=hub))
    piv = [0, 1.36, 0.155]
    m.add([B([0.02, 0.8, 0.01], [0, 0.96, 0.155], color="#c8a040")] + disc_z(0.14, 0.02, [0, 0.56, 0.16], color="#d8b050"), A("swing", axis="z", pivot=piv, angle=8, period=2, randomPhase=False))
    prop("Grandfather Clock", "A tall wooden pendulum clock: the brass pendulum swings, the hands move.", m, collider=[C([0.56, 2.2, 0.36])],
         scatter={"randomYaw": False, "yawStep": 90}, breakable=brk(110, "wood", count=14, size=(0.05, 0.3), colors=["darkwood", "#c8a040", "paper"]))


def metronome():
    m = Model()
    m.add([At([0.14, 0.03, 0.1], 0.0, color="darkwood"), B([0.1, 0.2, 0.08], [0, 0.12, 0], color="darkwood"), B([0.05, 0.06, 0.06], [0, 0.24, 0], color="darkwood"),
           B([0.07, 0.02, 0.005], [0, 0.08, 0.041], color="#c8a040")])
    piv = [0, 0.05, 0.045]
    m.add([B([0.008, 0.22, 0.006], [0, 0.16, 0.048], color="chrome"), B([0.025, 0.025, 0.012], [0, 0.2, 0.05], color="#c8a040")],
          A("swing", axis="z", pivot=piv, angle=28, period=1.0, jitter=0.3))
    prop("Metronome", "A wooden pyramid metronome ticking from side to side.", m, collision="cosmetic", breakable=brk(20, "wood", count=6, size=(0.02, 0.06)))


def globe():
    m = Model()
    m.add([At([0.22, 0.03, 0.22], 0.0, color="darkwood"), At([0.03, 0.12, 0.03], 0.03, color="darkwood")])
    tilt = 23.5
    ax = [math.sin(math.radians(tilt)), math.cos(math.radians(tilt)), 0]
    c = [0, 0.3, 0]
    m.add([B([0.02, 0.36, 0.02], c, color="#c8a040", roll=-tilt)])
    sphere = []
    for (d, y, col) in [(0.28, 0.0, "#2050a0")]:
        sphere += [B([d, d * 0.72, d * 0.4142], c, color=col, yaw=a, roll=-tilt) for a in (0, 45, 90, 135)]
    sphere += [B([0.12, 0.08, 0.06], [0.1, 0.33, 0.07], color="green", roll=-tilt), B([0.08, 0.1, 0.05], [-0.06, 0.26, 0.11], color="#40a040", roll=-tilt),
               B([0.1, 0.06, 0.05], [-0.1, 0.36, -0.08], color="sand", roll=-tilt)]
    m.add(sphere, A("spin", axis=ax, rpm=8, pivot=c))
    prop("Spinning Globe", "A desk globe on a wooden stand, turning on its tilted axis.", m, collision="cosmetic", breakable=brk(25, "soft", count=6, size=(0.03, 0.08)))


def newtons_cradle():
    m = Model()
    m.add([At([0.34, 0.03, 0.2], 0.0, color="black")] + [At([0.012, 0.24, 0.012], 0.03, x, z, color="chrome") for x in (-0.15, 0.15) for z in (-0.08, 0.08)]
          + [B([0.31, 0.012, 0.012], [0, 0.27, z], color="chrome") for z in (-0.08, 0.08)])
    for i in range(5):
        x = -0.08 + 0.04 * i
        balls = [B([0.003, 0.15, 0.003], [x, 0.195, z * 0.5], color="lightgrey") for z in (-0.08, 0.08)] + [B([0.04, 0.04, 0.04], [x, 0.11, 0], color="chrome"), B([0.04, 0.04, 0.04], [x, 0.11, 0], color="chrome", yaw=45)]
        if i == 0:
            m.add(balls, A("swing", axis="-z", pivot=[x, 0.27, 0], angle=35, period=1.4, oneWay=True))
        elif i == 4:
            m.add(balls, A("swing", axis="z", pivot=[x, 0.27, 0], angle=35, period=1.4, phase=0.5, oneWay=True))
        else:
            m.add(balls)
    prop("Newtons Cradle", "A desk toy: the end balls swing out and click back in turn.", m, collision="cosmetic", breakable=brk(20, "metal", count=6, size=(0.02, 0.05)))


def swing_set():
    m = Model()
    for x in (-1.2, 1.2):
        m.add([B([0.08, 2.3, 0.08], [x, 1.1, z * 0.28], color="red", pitch=-z * 14) for z in (-1, 1)])
    m.add([B([2.6, 0.08, 0.08], [0, 2.2, 0], color="red")])
    for i, x in enumerate((-0.5, 0.5)):
        piv = [x, 2.17, 0]
        m.add([B([0.015, 1.7, 0.015], [x + dx, 1.33, 0], color="steel") for dx in (-0.2, 0.2)] + [B([0.5, 0.04, 0.2], [x, 0.48, 0], color="rubber")],
              A("swing", axis="x", pivot=piv, angle=10, period=3.0, phase=0.3 * i, wind=0.5, jitter=0.15))
    prop("Swing Set", "A red playground swing set, the empty seats swaying.", m,
         collider=[C([0.12, 2.3, 0.7], [-1.2, 1.15, 0]), C([0.12, 2.3, 0.7], [1.2, 1.15, 0])], scatter={"randomYaw": True})


def bell_frame():
    m = Model()
    m.add([At([0.14, 2.0, 0.14], 0.0, x, color="darkwood") for x in (-0.6, 0.6)] + [B([1.4, 0.14, 0.18], [0, 2.0, 0], color="darkwood"), At([1.6, 0.1, 0.5], 0.0, color="concrete")])
    piv = [0, 1.92, 0]
    bell = []
    for (d, y, h) in [(0.5, 1.2, 0.12), (0.42, 1.32, 0.3), (0.3, 1.62, 0.14)]:
        bell += [At([d, h, d * 0.4142], y, color="#b08a3a", yaw=a) for a in (0, 45, 90, 135)]
    bell += [At([0.2, 0.16, 0.06], 1.76, color="black"), At([0.06, 0.3, 0.06], 1.04, color="#806020")]
    m.add(bell, A("swing", axis="x", pivot=piv, angle=18, period=2.4, jitter=0.15))
    prop("Bell Frame", "A bronze bell swinging in a timber frame.", m, collider=[C([0.16, 2.06, 0.18], [-0.6, 1.03, 0]), C([0.16, 2.06, 0.18], [0.6, 1.03, 0]), C([1.6, 0.1, 0.5])],
         scatter={"randomYaw": False, "yawStep": 90})


def boom_gate():
    m = Model()
    m.add([At([0.4, 1.0, 0.4], 0.0, color="yellow", mat="metal"), At([0.44, 0.06, 0.44], 1.0, color="darkgrey"), At([0.2, 0.2, 0.02], 0.6, 0, 0.21, color="black")])
    piv = [0.15, 0.9, 0]
    arm = [B([3.6, 0.1, 0.06], [2.0, 0.9, 0], color="white")] + [B([0.3, 0.102, 0.062], [0.5 + 0.6 * k, 0.9, 0], color="red") for k in range(6)]
    arm += [B([0.4, 0.16, 0.16], [-0.08, 0.9, 0], color="darkgrey")]
    m.add(arm, A("swing", axis="z", pivot=piv, angle=80, period=10, oneWay=True))
    m.add(B([0.08, 0.08, 0.08], [0, 1.08, 0], color="red"), A("blink", period=0.8, duty=0.5, color="#ff3020", off="#401008", glow=1.2))
    prop("Boom Gate", "A car park barrier arm that lifts up and comes down again, a red light blinking.", m, collider=[C([0.44, 1.06, 0.44])],
         scatter={"randomYaw": False, "yawStep": 90})


def hanging_hook():
    m = Model()
    m.add([At([0.2, 3.2, 0.2], 0.0, x, color="#e0b020", mat="metal") for x in (-1.2, 1.2)] + [B([2.6, 0.24, 0.24], [0, 3.3, 0], color="#e0b020"),
           B([0.4, 0.3, 0.3], [0, 3.05, 0], color="darkgrey")] + [At([0.5, 0.08, 0.5], 0.0, x, color="darkgrey") for x in (-1.2, 1.2)])
    piv = [0, 2.92, 0]
    m.add([B([0.03, 1.3, 0.03], [0, 2.27, 0], color="steel"), B([0.2, 0.2, 0.14], [0, 1.55, 0], color="#e0b020"), B([0.06, 0.18, 0.06], [0, 1.37, 0], color="darkgrey"),
           B([0.16, 0.05, 0.06], [0.05, 1.28, 0], color="darkgrey")], A("swing", axis="z", pivot=piv, angle=5, period=3.4, wind=0.5, jitter=0.2))
    prop("Gantry Hook", "A yellow gantry with a chain hook gently swinging under it.", m,
         collider=[C([0.24, 3.4, 0.24], [-1.2, 1.7, 0]), C([0.24, 3.4, 0.24], [1.2, 1.7, 0]), C([2.6, 0.3, 0.3], [0, 3.3, 0])], scatter={"randomYaw": False, "yawStep": 90})


def freight_hoist():
    m = Model()
    m.add([At([0.12, 3.0, 0.12], 0.0, x, z, color="darkgrey") for x in (-0.7, 0.7) for z in (-0.6,)] + [B([1.6, 0.14, 0.14], [0, 3.0, -0.6], color="darkgrey"),
           At([1.6, 0.06, 1.4], 0.0, color="#404040", mat="grid")])
    lift = 1.0  # the platform's rest height: it rides 0.8 m either way
    m.add([At([1.3, 0.08, 1.0], lift, 0, 0.0, color="#808080", mat="grid")] + [At([0.04, 0.9, 0.04], lift + 0.08, x, 0.48, color="yellow") for x in (-0.63, 0.63)]
          + [B([1.3, 0.04, 0.04], [0, lift + 0.96, 0.48], color="yellow"), At([0.5, 0.4, 0.4], lift + 0.08, 0.2, -0.1, color="cardboard"),
             At([0.03, 3.0 - lift - 0.1, 0.03], lift + 0.08, 0, -0.45, color="black")],
          A("bob", distance=0.8, period=12, phase=0.25, jitter=0.1))
    m.add(B([0.08, 0.08, 0.06], [0, 2.8, -0.52], color="#ffb020"), A("blink", period=1.0, duty=0.5, color="#ffb020", off="#3a2606", glow=1.2))
    prop("Freight Hoist", "An open goods lift with a crate, slowly riding up and down its frame.", m, collision="solid",
         collider=[C([1.6, 0.06, 1.4]), C([1.6, 3.07, 0.16], [0, 1.535, -0.6])], scatter={"randomYaw": False, "yawStep": 90})


def buoy():
    m = Model()
    keel = m.one(B([0.3, 0.3, 0.3], [0, 0.15, 0], color="darkgrey"), A("bob", distance=0.12, period=3.2, jitter=0.2))
    hull = m.one(At([0.02, 0.02, 0.02], 0.3, color="red"), A("swing", axis=[1, 0, 0.4], pivot=[0, 0.3, 0], angle=7, period=4.1, wind=0.6, parent=keel))
    m.add(Cyl(1.0, 0.5, 0.2, color="red") + Cyl(0.6, 0.6, 0.7, color="red") + [At([0.1, 0.5, 0.1], 1.3, color="darkgrey"), At([0.64, 0.12, 0.64], 1.0, color="white")],
          A("follow", parent=hull))
    m.add(At([0.16, 0.18, 0.16], 1.8, color="#ffe080"), A("blink", period=4, duty=0.12, color="#fff0a0", off="#806020", glow=1.6, freezeDistance=200, parent=hull))
    prop("Buoy", "A red marker buoy bobbing and rocking, its light flashing every few seconds.", m, collider=[C([1.0, 1.2, 1.0])], scatter={"randomYaw": True})


def bobber():
    m = Model()
    m.add([B([0.5, 0.01, 0.5], [0, 0.005, 0], color="#2a5070"), B([0.3, 0.012, 0.3], [0, 0.006, 0], color="#3a6890", yaw=45)])
    m.add([At([0.08, 0.06, 0.08], 0.0, color="white"), At([0.08, 0.05, 0.08], 0.06, color="red"), At([0.015, 0.08, 0.015], 0.11, color="red")],
          A("bob", distance=0.02, period=1.3, jitter=0.4))
    prop("Fishing Bobber", "A red and white float bobbing in a little pool of water.", m, collision="cosmetic", fade=30)


def duck_on_water():
    m = Model()
    m.add([B([0.9, 0.01, 0.7], [0, 0.005, 0], color="#2a5070"), B([0.7, 0.012, 0.5], [0, 0.006, 0], color="#3a6890", yaw=30)])
    bob = m.one(B([0.32, 0.12, 0.2], [0, 0.06, 0], color="#6a5030"), A("bob", distance=0.02, period=1.8, jitter=0.3))
    m.add([B([0.18, 0.06, 0.18], [-0.12, 0.12, 0], color="#6a5030", roll=15), B([0.02, 0.012, 0.11], [0.12, 0.13, 0], color="white")], A("follow", parent=bob))
    # The head dips now and then (one-way swing about the neck).
    m.add([B([0.1, 0.12, 0.1], [0.12, 0.2, 0], color="darkgreen"), B([0.06, 0.03, 0.04], [0.19, 0.2, 0], color="orange")],
          A("swing", axis="-z", pivot=[0.1, 0.13, 0], angle=35, period=3.1, oneWay=True, parent=bob))
    prop("Duck on a Pond", "A mallard duck bobbing on a small patch of pond water.", m, collision="cosmetic", fade=45)


def bird_perch():
    m = Model()
    m.add([At([0.3, 0.05, 0.3], 0.0, color="darkwood"), At([0.04, 1.2, 0.04], 0.05, color="darkwood"), B([0.4, 0.03, 0.03], [0, 1.24, 0], color="darkwood")])
    body = m.one(B([0.14, 0.09, 0.08], [0.05, 1.31, 0], color="#c03020"), A("bob", distance=0.012, period=0.6, jitter=0.3))
    m.add([B([0.1, 0.03, 0.06], [-0.05, 1.32, 0], color="#902010", roll=-20), B([0.012, 0.04, 0.012], [0.05, 1.255, 0.02], color="orange")], A("follow", parent=body))
    m.add([B([0.07, 0.07, 0.07], [0.12, 1.37, 0], color="#c03020"), B([0.04, 0.02, 0.02], [0.17, 1.37, 0], color="yellow"), B([0.072, 0.02, 0.072], [0.12, 1.39, 0], color="black")],
          A("swing", axis="z", pivot=[0.1, 1.34, 0], angle=25, period=1.7, oneWay=True, parent=body))
    prop("Bird on a Perch", "A little red bird on a perch, bobbing and pecking.", m, collision="cosmetic", fade=40)


def laundry_line():
    m = Model()
    m.add([At([0.08, 1.9, 0.08], 0.0, x, color="steel") for x in (-1.6, 1.6)] + [B([3.2, 0.012, 0.012], [0, 1.85, 0], color="white")])
    items = [(-1.1, [0.5, 0.6], "blue"), (-0.45, [0.45, 0.4], "white"), (0.15, [0.35, 0.7], "#3060a0"), (0.65, [0.5, 0.45], "red"), (1.2, [0.3, 0.3], "yellow")]
    for i, (x, (w, h), col) in enumerate(items):
        m.add([B([w, h, 0.012], [x, 1.85 - h / 2, 0], color=col)] + [B([0.02, 0.05, 0.03], [x + dx, 1.85, 0], color="lightwood") for dx in (-w / 2 + 0.03, w / 2 - 0.03)],
              A("swing", axis="x", pivot=[x, 1.85, 0], angle=16, period=2.0 + 0.2 * i, phase=0.17 * i, wind=1, jitter=0.15))
    prop("Laundry Line", "A washing line with clothes flapping in the wind.", m, collider=[C([0.12, 1.9, 0.12], [-1.6, 0.95, 0]), C([0.12, 1.9, 0.12], [1.6, 0.95, 0])],
         scatter={"randomYaw": True})


def chimney_smoke():
    m = Model()
    m.add([At([0.8, 1.6, 0.8], 0.0, color="brick", mat="brick"), At([0.9, 0.1, 0.9], 1.6, color="concrete"), At([0.3, 0.3, 0.3], 1.7, 0.15, 0.1, color="darkgrey")])
    for k in range(4):
        s = 0.28 + 0.1 * k
        m.add(B([s, s, s], [0.15 + 0.05 * k, 2.2 + 0.4 * k, 0.1], color=["#909090", "#a0a0a0", "#b0b0b0", "#c4c4c4"][k], yaw=20 * k),
              A("bob", axis=[0.3, 1, 0], distance=0.2 + 0.05 * k, period=3.0 + 0.4 * k, phase=0.25 * k, wind=0.8))
    prop("Smoking Chimney", "A brick chimney stack puffing chunky grey smoke.", m, collider=[C([0.9, 1.7, 0.9])], scatter={"randomYaw": True, "yawStep": 90})


def fountain():
    m = Model()
    m.add([At([2.0, 0.4, 2.0 * 0.4142], 0.0, color="concrete", mat="concrete", yaw=a) for a in (0, 45, 90, 135)])
    m.add([At([1.7, 0.02, 1.7 * 0.4142], 0.36, color="#3a6890", yaw=a) for a in (0, 45, 90, 135)])
    m.add(Cyl(0.3, 0.6, 0.4, color="concrete") + Cyl(0.7, 0.1, 1.0, color="concrete"))
    m.add([At([0.08, 0.5, 0.08], 1.05, color="#a0d0ff"), At([0.16, 0.08, 0.16], 1.55, color="#d0e8ff")], A("bob", distance=0.15, period=1.6, jitter=0.2))
    m.add([At([0.05, 0.25, 0.05], 0.95, x, z, color="#a0d0ff") for (x, z) in ((0.42, 0), (-0.42, 0), (0, 0.42), (0, -0.42))], A("bob", distance=0.06, period=0.9, phase=0.3))
    m.add([At([0.06, 0.02, 0.06], 0.38, 0.6 * math.cos(math.radians(a)), 0.6 * math.sin(math.radians(a)), color="white") for a in range(0, 360, 45)],
          A("pulse", period=1.2, color="white", off="#6090b0", glow=0.2))
    prop("Fountain", "A round stone fountain: the water jet bobs and the ripples sparkle.", m, collider=[C([2.0, 0.4, 2.0]), C([0.4, 1.0, 0.4])], scatter={"randomYaw": True})


# =============================== Screens and machines ===============================

def tv():
    m = Model()
    m.add([At([1.0, 0.45, 0.4], 0.0, color="darkwood", mat="wood"), At([0.3, 0.05, 0.2], 0.45, color="black"), At([0.06, 0.1, 0.06], 0.5, color="black"),
           At([0.9, 0.55, 0.06], 0.6, color="black")])
    m.add(B([0.82, 0.47, 0.01], [0, 0.875, 0.032], color="#6080c0"), A("flicker", rate=12, duty=0.7, color="#9ab8ff", off="#304070", glow=0.9))
    m.add(B([0.82, 0.08, 0.011], [0, 0.95, 0.033], color="#c0d8ff"), A("bob", axis="y", distance=0.18, period=0.37))
    m.add(B([0.04, 0.02, 0.01], [0.38, 0.62, 0.032], color="red"), A("blink", period=3, duty=0.9, color="#ff3020", off="#401008", glow=0.6))
    prop("Flickering TV", "A flat TV on a wooden stand, its screen flickering with a rolling bar.", m, collider=[C([1.0, 1.15, 0.42])],
         scatter={"randomYaw": False, "yawStep": 90}, breakable=brk(35, "glass", count=12, size=(0.03, 0.12), colors=["black", "#6080c0", "darkwood"]))


def monitor():
    m = Model()
    m.add([At([0.25, 0.02, 0.18], 0.0, color="darkgrey"), At([0.04, 0.2, 0.03], 0.02, 0, -0.03, color="darkgrey"), At([0.56, 0.34, 0.04], 0.16, color="black")])
    m.add(B([0.52, 0.3, 0.005], [0, 0.33, 0.021], color="#101830"))
    carrier = m.one(B([0.002, 0.002, 0.002], [0, 0.33, 0.0], color="black"), A("bob", axis="y", distance=0.1, period=2.3, randomPhase=True))
    m.add(B([0.08, 0.05, 0.006], [0, 0.33, 0.024], color="#40e0a0"), A("pulse", period=4, color="#ff60c0", off="#40e0a0", glow=0.8, parent=carrier))
    m.add(B([0.002, 0.002, 0.002], [0, 0.33, 0.0], color="black"),
          A("bob", axis="x", distance=0.2, period=3.1, parent=carrier))
    # The logo moves with both carriers: bob along y (carrier 1) and x (carrier 2) make a bouncing screen saver.
    m.parts[-2]["anim"]["parent"] = len(m.parts)
    m.parts[-1]["anim"]["parent"] = carrier
    m.add(B([0.02, 0.015, 0.006], [0.24, 0.17, 0.021], color="#30ff60"), A("blink", period=2, duty=0.5, color="#40ff60", off="#063a10", glow=0.5))
    prop("Monitor Screen Saver", "A computer monitor with a glowing logo bouncing round the screen.", m, collision="cosmetic",
         breakable=brk(25, "glass", count=8, size=(0.02, 0.08), colors=["black", "#101830"]))


def arcade():
    m = Model()
    m.add([At([0.7, 1.1, 0.75], 0.0, color="#202040"), B([0.7, 0.9, 0.45], [0, 1.45, -0.15], color="#202040"), At([0.74, 0.25, 0.6], 1.9, 0, -0.08, color="#202040"),
           B([0.72, 0.08, 0.35], [0, 1.12, 0.22], color="black", pitch=10)])
    m.add([B([0.04, 1.6, 0.76], [s * 0.37, 1.1, -0.0], color="#d03070") for s in (-1, 1)])
    m.add(B([0.56, 0.44, 0.02], [0, 1.5, 0.08], color="#203060", pitch=-12), A("flicker", rate=14, duty=0.8, color="#4080ff", off="#102040", glow=0.8))
    m.add(B([0.66, 0.18, 0.02], [0, 2.02, 0.23], color="#ffd040"), A("pulse", period=1.6, color="#fff080", off="#806010", glow=1.2))
    for i, (x, col) in enumerate(((-0.18, "#ff3030"), (0.0, "#30a0ff"), (0.15, "#30ff60"), (0.25, "#ffd030"))):
        m.add(B([0.05, 0.03, 0.05], [x, 1.18, 0.25], color=col), A("cycle", period=1.0, steps=4, on=[i], color=col, off="#202020", glow=1.0))
    m.add([B([0.03, 0.12, 0.03], [-0.25, 1.22, 0.24], color="black"), B([0.06, 0.06, 0.06], [-0.25, 1.29, 0.24], color="red")], A("swing", axis="z", pivot=[-0.25, 1.17, 0.24], angle=15, period=0.9, jitter=0.4))
    prop("Arcade Cabinet", "An arcade machine: flickering screen, glowing marquee, buttons lighting in turn.", m, collider=[C([0.76, 2.15, 0.76])],
         scatter={"randomYaw": False, "yawStep": 90}, breakable=brk(160, "wood", count=14, size=(0.05, 0.25), colors=["#202040", "#d03070", "#203060"]))


def slot_machine():
    m = Model()
    m.add([At([0.6, 0.9, 0.55], 0.0, color="#801818"), At([0.6, 0.7, 0.45], 0.9, 0, -0.05, color="#a02020"), At([0.62, 0.15, 0.5], 1.6, 0, -0.03, color="#c8a040"),
           B([0.5, 0.3, 0.02], [0, 1.3, 0.18], color="black")])
    for i, x in enumerate((-0.15, 0.0, 0.15)):
        hub = [x, 1.3, 0.12]
        m.add([B([0.12, 0.2, 0.2], hub, color="white", pitch=a) for a in (0, 45)] + [B([0.13, 0.05, 0.21], hub, color=["red", "#30a030", "#c8a040"][i])],
              A("spin", axis="x", rpm=200 + 37 * i, pivot=hub))
    m.add([B([0.05, 0.4, 0.05], [0.34, 1.25, 0.0], color="chrome"), B([0.1, 0.1, 0.1], [0.34, 1.48, 0], color="red")])
    for k in range(6):
        m.add(B([0.07, 0.07, 0.05], [-0.25 + 0.1 * k, 1.68, 0.2], color="#ffe080"), A("cycle", period=0.9, steps=3, on=[k % 3], color="#fff4b0", off="#806020", glow=1.2))
    m.add(B([0.4, 0.06, 0.02], [0, 1.05, 0.18], color="#ffd040"), A("blink", period=0.6, duty=0.5, color="#ffff80", off="#806010", glow=0.8))
    prop("Slot Machine", "A one-armed bandit with spinning reels and chasing bulbs.", m, collider=[C([0.66, 1.75, 0.6])], scatter={"randomYaw": False, "yawStep": 90},
         breakable=brk(150, "metal", count=12, size=(0.04, 0.2), colors=["#a02020", "#c8a040", "white"]))


def vending_glow():
    m = Model()
    m.add([At([1.0, 1.9, 0.7], 0.0, 0, -0.05, color="#1a5aa0", mat="metal")]
          + [At([0.1, 1.9, 0.12], 0.0, x, 0.36, color="#1a5aa0") for x in (-0.45, 0.38)] + [At([1.0, 0.25, 0.12], 0.0, 0, 0.36, color="#1a5aa0"), At([1.0, 0.15, 0.12], 1.75, 0, 0.36, color="#1a5aa0")])
    m.add([B([0.56, 0.02, 0.1], [-0.12, 0.45 + 0.28 * k, 0.37], color="darkgrey") for k in range(5)])
    cols = ["red", "#30a030", "orange", "white", "#3060c0"]
    m.add([At([0.06, 0.16, 0.06], 0.46 + 0.28 * k, -0.37 + 0.1 * j, 0.36, color=cols[(j + k) % 5]) for k in range(5) for j in range(6)])
    m.add(B([0.72, 1.5, 0.01], [-0.04, 1.0, 0.305], color="#a0c0e0"), A("pulse", period=5, color="#d0e8ff", off="#6080a0", glow=0.35))
    m.add(At([0.86, 0.12, 0.02], 1.76, 0, 0.42, color="white"), A("flicker", rate=3, duty=0.93, color="#f0f8ff", off="#808890", glow=0.9))
    m.add([At([0.18, 0.4, 0.02], 0.9, 0.36, 0.405, color="black"), At([0.1, 0.06, 0.02], 1.35, 0.36, 0.41, color="#30ff60")])
    prop("Glowing Vending Machine", "A drinks machine with a softly glowing front and a buzzing sign.", m, collider=[C([1.0, 1.9, 0.82])],
         scatter={"randomYaw": False, "yawStep": 90})


# =============================== Build ===============================

BUILDERS = [ceiling_fan, desk_fan, pedestal_fan, wall_fan, roof_turbine, floor_fan, ac_condenser, cooling_unit,
            windmill, wind_turbine, weather_vane, anemometer,
            lambda: flag_pole("Flag on a Pole", ["red", "white", "blue"], "A tall flagpole with a striped flag waving in the wind."),
            lambda: flag_pole("Pennant Pole", ["yellow", "black", "yellow"], "A flagpole flying a yellow and black pennant."),
            banner, shop_sign, lantern, flicker_lamp, neon_sign, warning_beacon, traffic_light, crossing_lights, police_light_bar, alarm_light, lighthouse,
            radar_dish, satellite_dish, street_clock, ferris_wheel, carousel, pumpjack, conveyor, generator, cement_mixer, washer, water_wheel, barber_pole,
            rotating_sign, piston_pump, pinwheel, sprinkler,
            rocking_chair, hammock, wind_chimes, grandfather_clock, metronome, globe, newtons_cradle, swing_set, bell_frame, boom_gate, hanging_hook,
            freight_hoist, buoy, bobber, duck_on_water, bird_perch, laundry_line, chimney_smoke, fountain,
            tv, monitor, arcade, slot_machine, vending_glow]


def footprint(p):
    xs, zs = [], []
    for part in p["parts"]:
        s = part["size"]
        o = part.get("offset", [0, s[1] / 2, 0])
        r = max(s[0], s[2]) / 2 if part.get("rotation") else None
        hx = r if r else s[0] / 2
        hz = r if r else s[2] / 2
        xs += [o[0] - hx, o[0] + hx]
        zs += [o[2] - hz, o[2] + hz]
    return min(xs), max(xs), min(zs), max(zs)


def gallery(props):
    """The Dev map: every animated prop on its own pad, in rows, small ones on low plinths."""
    ops = [{"op": "Ground", "color": [0.34, 0.36, 0.36]}, {"op": "Perimeter", "height": 4, "color": [0.5, 0.52, 0.55]}]
    row_w = 72.0
    x, z, row_d = -row_w / 2, -16.0, 0.0
    for p in props:
        x0, x1, z0, z1 = footprint(p)
        w, d = x1 - x0 + 1.6, z1 - z0 + 1.6
        w, d = max(w, 2.0), max(d, 2.0)
        if x + w > row_w / 2:
            x = -row_w / 2
            z += row_d + 2.6
            row_d = 0.0
        cx = x + w / 2 - (x0 + x1) / 2
        cz = z + d / 2 - (z0 + z1) / 2
        small = (x1 - x0) < 0.7 and (z1 - z0) < 0.7
        ops.append({"op": "Block", "x0": round(x, 2), "y0": 0, "z0": round(z, 2), "x1": round(x + w, 2), "y1": 0.05, "z1": round(z + d, 2), "color": [0.48, 0.48, 0.5]})
        if small:
            ops.append({"op": "Block", "x0": round(cx - 0.45, 2), "y0": 0, "z0": round(cz - 0.45, 2), "x1": round(cx + 0.45, 2), "y1": 0.75, "z1": round(cz + 0.45, 2), "color": [0.62, 0.6, 0.56]})
        ops.append({"op": "Prop", "name": p["name"], "x": round(cx, 2), "z": round(cz, 2), "yaw": 0})
        x += w + 0.8
        row_d = max(row_d, d)
    for sx in (-36, -24, -12, 0, 12, 24, 36):
        ops.append({"op": "Spawn", "x": sx, "z": -36})
        ops.append({"op": "Spawn", "x": sx, "z": 36})
    ops.append({"op": "AutoWaypoints", "spacing": 6})
    return {"format": "call-of-slop-map/1", "name": "Animated Props",
            "description": "Every animated prop (fans, flags, lights, machines...) on its own pad, in rows; small ones on plinths.",
            "author": "Call of Slop", "order": 955, "halfX": 42, "halfZ": 40, "sky": [0.56, 0.62, 0.7], "ops": ops}


def dump(obj, indent=2):
    """JSON with one part per line (like the starter props), readable in diffs."""
    if isinstance(obj, dict) and "parts" in obj:
        head = {k: v for k, v in obj.items() if k != "parts"}
        lines = json.dumps(head, indent=indent)[:-2] + ',\n  "parts": [\n'
        lines += ",\n".join("    " + json.dumps(p) for p in obj["parts"])
        return lines + "\n  ]\n}\n"
    if isinstance(obj, dict) and "ops" in obj:
        head = {k: v for k, v in obj.items() if k != "ops"}
        lines = json.dumps(head, indent=indent)[:-2] + ',\n  "ops": [\n'
        lines += ",\n".join("    " + json.dumps(p) for p in obj["ops"])
        return lines + "\n  ]\n}\n"
    return json.dumps(obj, indent=indent) + "\n"


if __name__ == "__main__":
    for b in BUILDERS:
        b()
    names = [p["name"] for p in PROPS]
    assert len(set(n.lower() for n in names)) == len(names), "duplicate names"
    folder = os.path.join(OUT, CAT)
    os.makedirs(folder, exist_ok=True)
    for p in PROPS:
        with open(os.path.join(folder, p["name"].replace(" ", "") + ".json"), "w", newline="\n") as f:
            f.write(dump(p))
    with open(os.path.join(MAPS, "AnimatedProps.json"), "w", newline="\n") as f:
        f.write(dump(gallery(PROPS)))
    print(len(PROPS), "animated props written to", folder)
