"""Generates the outdoor prop library for Call of Slop: Props/Vehicles, Props/Rooftop, Props/Utility, Props/Signage and
Props/Urban (cars, buses, air conditioners, vents and ducts, utility poles, fire escapes, traffic lights, signs, bus
shelters...), plus the Dev map that shows them all (Maps/Dev/PropGallery2.json, "Prop Gallery 2").

    python3 tools/propviz/gen_outdoor.py [Props] [Maps/Dev/PropGallery2.json]

Uses the box helpers of gen.py (the starter library's generator). Health values follow the material health guide in
Props/README.md: glass/paper 5-20, plastic 25-60, wood 40-150, thin metal 60-180, heavy metal 200-400, concrete,
vehicles and structures unbreakable.
"""
import json, math, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen  # noqa: E402
from gen import B, At, Cyl, HCyl, Ring, C, brk  # noqa: E402

PROPS = gen.PROPS  # gen.prop() appends here; none of gen's own props are built
prop = gen.prop

GL = "#26343d"       # car window glass (dark blue-grey)
HEAD = "#fff2c0"     # headlights
TAIL = "#b01a14"     # tail lights
TRIM = "#1c1c1e"     # rubber trim, grilles, seams
CREAM = "#d8d2bc"    # air conditioner casings
LIT = "#ffe9a8"      # lit lamps / signs

# Per-material breakable presets (see the material health guide in Props/README.md).
def glass(h=15, **kw):
    return brk(h, "glass", count=kw.pop("count", 12), size=kw.pop("size", (0.04, 0.14)), speed=kw.pop("speed", 4), **kw)


def plastic(h=40, **kw):
    return brk(h, "soft", count=kw.pop("count", 9), size=kw.pop("size", (0.06, 0.2)), speed=kw.pop("speed", 3), **kw)


def wood(h=80, **kw):
    return brk(h, "wood", count=kw.pop("count", 12), size=kw.pop("size", (0.06, 0.35)), speed=kw.pop("speed", 4), **kw)


def metal(h=120, **kw):
    return brk(h, "metal", count=kw.pop("count", 10), size=kw.pop("size", (0.06, 0.24)), speed=kw.pop("speed", 3.5), **kw)


SQ = {"randomYaw": False, "yawStep": 90, "yawJitter": 4}          # things that sit square to streets and walls
SQ0 = {"randomYaw": False, "yawStep": 90}                          # exactly square (poles, signs, wall-mounted)
ANY = {"randomYaw": True}


def flat(parts):
    out = []
    for p in parts:
        if isinstance(p, list):
            out.extend(flat(p))
        else:
            out.append(p)
    return out


def tube(p0, p1, t, color="steel", mat=None):
    """A square bar of thickness t from point p0 to point p1 (any direction)."""
    dx, dy, dz = (p1[i] - p0[i] for i in range(3))
    L = math.sqrt(dx * dx + dy * dy + dz * dz)
    h = math.sqrt(dx * dx + dz * dz)
    yaw = math.degrees(math.atan2(-dz, dx)) if h > 1e-6 else 0.0
    roll = math.degrees(math.atan2(dy, h))
    mid = [(p0[i] + p1[i]) / 2 for i in range(3)]
    return B([L, t, t], mid, color=color, mat=mat, yaw=yaw, roll=roll)


def wheel(d, w, x, z, cy=None, tyre="rubber", hub="steel", hubk=0.58):
    """A wheel with its axle along Z: an octagonal tyre and a smaller hub poking out a little."""
    cy = d / 2 if cy is None else cy
    return HCyl(d, w, cy, x, z, color=tyre, alongZ=True) + HCyl(d * hubk, w + 0.02, cy, x, z, color=hub, alongZ=True)


def vring(d, t, w, cx, cy, cz, color="rubber", n=8):
    """A vertical ring in the XY plane (a thin bicycle wheel), axle along Z."""
    r = d / 2 - t / 2
    chord = 2 * (d / 2) * math.tan(math.pi / n) * 1.02
    out = []
    for k in range(n):
        a = 2 * math.pi * k / n + math.pi / n
        out.append(B([t, chord, w], [cx + r * math.cos(a), cy + r * math.sin(a), cz], color=color, roll=math.degrees(a)))
    return out


def tilted(size, local, base, pitch, color, mat=None):
    """A box lying on a plane through `base` tilted by `pitch` about X; `local` is its centre in the plane's own frame."""
    a = math.radians(pitch)
    x, y, z = local
    wy = y * math.cos(a) - z * math.sin(a)
    wz = y * math.sin(a) + z * math.cos(a)
    return B(size, [base[0] + x, base[1] + wy, base[2] + wz], color=color, mat=mat, pitch=pitch)


def flip_upside_down(parts):
    """Turns a model over about the X axis (roof on the ground), then stands it back on y = 0."""
    out = []
    for p in parts:
        q = dict(p)
        o = p["offset"]
        q["offset"] = [o[0], -o[1], -o[2]]
        if "rotation" in p:
            pi, ya, ro = p["rotation"]
            q["rotation"] = gen.r3([pi, -ya, -ro])
        out.append(q)
    lo, _ = bounds({"parts": out})
    for q in out:
        q["offset"] = gen.r3([q["offset"][0], q["offset"][1] - lo[1], q["offset"][2]])
    return out


# ================================================================== Vehicles ==================================================================

def car_parts(L, W, clr, y0=0.3, bh=0.52, cab=(-1.0, 0.85), ch=0.45, wd=0.64, wb=None, front=40, rear=32, inset=0.16,
              tyres=True, rims=None, glass=GL, side_glass=True, bpillar=None, lights=True, wheels=True):
    """A car body: lower body, cabin (glass box, roof, sloped windscreen and rear window), lights, bumpers, mirrors, wheels.
    +X is the front. Returns (parts, roof top height)."""
    cx0, cx1 = cab
    cw = cx1 - cx0
    mid = (cx0 + cx1) / 2
    gw = W - inset
    wb = wb if wb is not None else L / 2 - 0.82
    p = [At([L, bh, W], y0, color=clr),
         At([2 * wb - wd - 0.12, 0.07, W + 0.012], y0 + 0.02, color=TRIM),                       # sill trim between the wheels
         At([0.14, 0.2, W + 0.02], y0 - 0.02, L / 2 + 0.02, color="darkgrey"),                   # bumpers
         At([0.14, 0.2, W + 0.02], y0 - 0.02, -L / 2 - 0.02, color="darkgrey"),
         At([cw, ch, gw], y0 + bh, mid, color=glass if side_glass else clr),                     # cabin
         At([cw - 0.08, 0.07, gw + 0.04], y0 + bh + ch, mid, color=clr)]                         # roof
    for (edge, a, sgn) in ((cx1, front, 1), (cx0, rear, -1)):
        if a <= 0:
            continue
        ar = math.radians(a)
        dx = ch * math.tan(ar)
        ln = ch / math.cos(ar) + 0.03
        c = [edge + sgn * dx / 2, y0 + bh + ch / 2]
        p.append(B([0.05, ln, gw - 0.04], [c[0], c[1], 0], color=glass, roll=sgn * a))
        for z in (gw / 2 - 0.02, -gw / 2 + 0.02):
            p.append(B([0.07, ln, 0.06], [c[0] - sgn * 0.005, c[1], z], color=clr, roll=sgn * a))
    bx = mid if bpillar is None else bpillar
    p.append(At([0.09, ch, gw + 0.02], y0 + bh, bx, color=clr))                                  # B pillar
    p.append(At([0.015, bh - 0.12, W + 0.004], y0 + 0.08, bx, color=TRIM))                       # door seams (both sides in one box)
    p.append(At([0.015, bh - 0.12, W + 0.004], y0 + 0.08, cx1 - 0.02, color=TRIM))
    p.append(At([0.14, 0.03, W + 0.016], y0 + bh - 0.14, bx - 0.22, color=TRIM))                 # handles
    p.append(At([0.14, 0.03, W + 0.016], y0 + bh - 0.14, bx + 0.6, color=TRIM))
    p.append(At([0.12, 0.08, W + 0.2], y0 + bh + 0.04, cx1 - 0.06, color=clr))                   # mirrors
    if lights:
        p += [At([0.03, 0.1, 0.28], y0 + bh - 0.2, L / 2 + 0.002, W / 2 - 0.22, color=HEAD),
              At([0.03, 0.1, 0.28], y0 + bh - 0.2, L / 2 + 0.002, -W / 2 + 0.22, color=HEAD),
              At([0.03, 0.12, W * 0.38], y0 + bh - 0.24, L / 2 + 0.002, color=TRIM),
              At([0.03, 0.1, 0.26], y0 + bh - 0.18, -L / 2 - 0.002, W / 2 - 0.2, color=TAIL),
              At([0.03, 0.1, 0.26], y0 + bh - 0.18, -L / 2 - 0.002, -W / 2 + 0.2, color=TAIL),
              At([0.01, 0.11, 0.4], y0 + 0.03, L / 2 + 0.096, color="white"),
              At([0.01, 0.11, 0.4], y0 + 0.03, -L / 2 - 0.096, color="white")]
    if wheels:
        for x in (wb, -wb):
            for z in (W / 2 - 0.07, -W / 2 + 0.07):
                if tyres:
                    p += wheel(wd, 0.22, x, z)
                elif rims:
                    p += HCyl(rims, 0.2, rims / 2, x, z, color="rust", alongZ=True)
    return p, y0 + bh + ch + 0.07


def car_collider(L, W, y0, bh, cab, ch, inset=0.16):
    mid = (cab[0] + cab[1]) / 2
    return [C([L + 0.18, y0 + bh, W + 0.04]), C([cab[1] - cab[0] + 0.3, ch + 0.07, W - inset], [mid, y0 + bh + (ch + 0.07) / 2, 0])]


CAR_SCATTER = {"randomYaw": False, "yawStep": 90, "yawJitter": 8, "scale": [1, 1]}


def hatchback():
    L, W, y0, bh, cab, ch = 3.9, 1.72, 0.28, 0.52, (-1.72, 0.55), 0.46
    p, _ = car_parts(L, W, "#3d6f9e", y0, bh, cab, ch, wd=0.6, rear=14, bpillar=-0.45)
    p.append(At([0.04, 0.06, 0.9], y0 + bh + ch + 0.07, -1.55, color=TRIM))  # spoiler lip
    prop("Hatchback", "Vehicles", "A small blue hatchback. Solid cover, waist high at the bonnet.", p,
         collider=car_collider(L, W, y0, bh, cab, ch), scatter=CAR_SCATTER)


def sedan_parts(clr, **kw):
    L, W = 4.6, 1.8
    return car_parts(L, W, clr, 0.3, 0.52, (-1.0, 0.85), 0.45, **kw)


SEDAN = (4.6, 1.8, 0.3, 0.52, (-1.0, 0.85), 0.45)


def sedan():
    p, _ = sedan_parts("#8a8f96")
    prop("Sedan", "Vehicles", "A silver four-door saloon car.", p, collider=car_collider(*SEDAN), scatter=CAR_SCATTER)


def taxi():
    p, top = sedan_parts("#e8b818")
    p += [At([0.22, 0.16, 0.56], top, -0.07, color="white"), At([0.23, 0.05, 0.57], top + 0.05, -0.07, color="#e8b818"),
          At([0.18, 0.12, 0.6], top, -0.07, color=TRIM)]
    for i in range(8):  # checker band along both sides
        p.append(At([0.2, 0.08, 1.812], 0.62, -1.4 + i * 0.4, color=TRIM if i % 2 == 0 else "white"))
    prop("Taxi", "Vehicles", "A yellow taxi with a roof sign and a checker band.", p, collider=car_collider(*SEDAN), scatter=CAR_SCATTER)


def police_car():
    p, top = sedan_parts("white")
    p += [At([1.7, 0.4, 1.808], 0.38, -0.1, color="navy"), At([0.04, 0.1, 1.812], 0.66, 1.4, color="navy"),
          At([0.3, 0.1, 0.7], top, -0.05, color=TRIM), At([0.28, 0.09, 0.32], top + 0.01, -0.05, 0.18, color="#c41c1c"),
          At([0.28, 0.09, 0.32], top + 0.01, -0.05, -0.18, color="#1c3cc4"), At([0.6, 0.1, 1.812], 0.56, -0.5, color="white"),
          At([0.04, 0.22, 1.86], 0.26, 2.42, color=TRIM)]  # push bar
    prop("Police Car", "Vehicles", "A white police car with blue doors and a light bar.", p, collider=car_collider(*SEDAN), scatter=CAR_SCATTER)


def suv():
    L, W, y0, bh, cab, ch = 4.7, 1.9, 0.4, 0.6, (-2.25, 0.85), 0.52
    p, top = car_parts(L, W, "#2f4a34", y0, bh, cab, ch, wd=0.76, rear=6, front=36, bpillar=-0.45)
    p += [At([2.6, 0.05, 0.05], top, -0.7, 0.78, color=TRIM), At([2.6, 0.05, 0.05], top, -0.7, -0.78, color=TRIM),
          At([0.1, 0.62, 0.62], 0.62, -L / 2 - 0.08, color="rubber"), At([0.12, 0.3, 0.3], 0.78, -L / 2 - 0.1, color="steel"),  # spare wheel
          At([L - 1.9, 0.1, W + 0.04], y0 - 0.03, -0.05, color=TRIM)]
    prop("SUV", "Vehicles", "A tall green SUV with roof rails and a spare wheel on the back.", p,
         collider=car_collider(L, W, y0, bh, cab, ch), scatter=CAR_SCATTER)


def pickup():
    L, W, y0, bh, cab, ch = 5.3, 1.95, 0.42, 0.6, (-0.35, 1.05), 0.52
    p, top = car_parts(L, W, "#a3311f", y0, bh, cab, ch, wd=0.78, rear=3, front=34, wb=1.75, bpillar=0.35)
    by = y0 + bh
    bx0, bx1 = -L / 2, cab[0] - 0.08
    bl = bx1 - bx0
    p += [At([bl, 0.42, 0.07], by, (bx0 + bx1) / 2, W / 2 - 0.035, color="#a3311f"),
          At([bl, 0.42, 0.07], by, (bx0 + bx1) / 2, -W / 2 + 0.035, color="#a3311f"),
          At([0.07, 0.42, W], by, bx0 + 0.035, color="#a3311f"), At([0.07, 0.46, W - 0.1], by, bx1 - 0.03, color="#a3311f"),
          At([bl - 0.12, 0.012, W - 0.14], by, (bx0 + bx1) / 2, color="#2a2a2a"),
          At([bl, 0.04, 0.1], by + 0.42, (bx0 + bx1) / 2, W / 2 - 0.05, color=TRIM), At([bl, 0.04, 0.1], by + 0.42, (bx0 + bx1) / 2, -W / 2 + 0.05, color=TRIM),
          At([0.1, 0.06, 0.4], by + 0.25, bx0 - 0.005, color=TRIM)]  # tailgate handle
    col = [C([L + 0.18, by, W + 0.04]), C([1.7, ch + 0.07, W - 0.16], [0.35, by + (ch + 0.07) / 2, 0]),
           C([bl, 0.42, 0.08], [(bx0 + bx1) / 2, by + 0.21, W / 2 - 0.04]), C([bl, 0.42, 0.08], [(bx0 + bx1) / 2, by + 0.21, -W / 2 + 0.04]),
           C([0.08, 0.42, W], [bx0 + 0.04, by + 0.21, 0])]
    prop("Pickup Truck", "Vehicles", "A red pickup with an open load bed.", p, collider=col, scatter=CAR_SCATTER)


def van_parts(clr, L=5.1, W=1.98):
    y0, bh, cab, ch = 0.36, 0.55, (-2.5, 1.45), 1.05
    p, top = car_parts(L, W, clr, y0, bh, cab, ch, wd=0.7, front=30, rear=0, side_glass=False, bpillar=0.6, inset=0.08)
    p += [At([0.8, 0.55, W - 0.07], y0 + bh + 0.35, 1.0, color=GL),                    # cab side windows
          At([0.04, 0.58, 0.5], y0 + bh + 0.32, -2.51, 0.48, color=GL), At([0.04, 0.58, 0.5], y0 + bh + 0.32, -2.51, -0.48, color=GL),
          At([0.02, 1.5, 0.02], y0 + 0.05, -2.512, color=TRIM),                          # rear door split
          At([0.012, 1.45, W + 0.004], y0 + 0.1, -0.5, color=TRIM)]                      # sliding door seam
    return p, top, (L, W, y0, bh, cab, ch)


def van():
    p, top, dims = van_parts("white")
    p += [At([2.6, 0.36, 1.985], 1.15, -1.0, color="#2c64b0"), At([1.6, 0.12, 1.99], 1.2, -1.0, color="white")]
    L, W, y0, bh, cab, ch = dims
    prop("Van", "Vehicles", "A white panel van with a blue logo panel.", p,
         collider=[C([L + 0.18, y0 + bh, W + 0.04]), C([cab[1] - cab[0] + 0.3, ch + 0.07, W - 0.08], [(cab[0] + cab[1]) / 2, y0 + bh + (ch + 0.07) / 2, 0])],
         scatter=CAR_SCATTER)


def ambulance():
    p, top, dims = van_parts("white")
    L, W, y0, bh, cab, ch = dims
    p += [At([L - 0.02, 0.14, W + 0.01], 0.95, color="#c41c1c"), At([L - 0.6, 0.06, W + 0.012], 1.12, -0.3, color="#e8b818"),
          At([0.2, 0.6, W + 0.016], 1.25, -1.2, color="#c41c1c"), At([0.6, 0.2, W + 0.016], 1.45, -1.2, color="#c41c1c"),
          At([0.25, 0.12, 0.4], top, 1.1, 0.5, color="#1c3cc4"), At([0.25, 0.12, 0.4], top, 1.1, -0.5, color="#c41c1c"),
          At([0.2, 0.1, 0.2], top, -2.3, 0.7, color="#1c3cc4"), At([0.2, 0.1, 0.2], top, -2.3, -0.7, color="#1c3cc4")]
    prop("Ambulance", "Vehicles", "A white ambulance van with red stripes and beacons.", p,
         collider=[C([L + 0.18, y0 + bh, W + 0.04]), C([cab[1] - cab[0] + 0.3, ch + 0.07, W - 0.08], [(cab[0] + cab[1]) / 2, y0 + bh + (ch + 0.07) / 2, 0])],
         scatter=CAR_SCATTER)


def burnt_car():
    L, W, y0, bh, cab, ch = 4.6, 1.8, 0.2, 0.52, (-1.0, 0.85), 0.45
    p, _ = car_parts(L, W, "#3b2c24", y0, bh, cab, ch, tyres=False, rims=0.44, glass="#121212", lights=False)
    p += [At([1.2, 0.02, 1.4], y0 + bh, 1.6, color="rust"), At([0.9, 0.3, 1.812], 0.3, -0.9, color="rust"),
          At([0.8, 0.02, 1.2], y0 + bh + ch + 0.07, -0.1, color="#1a1a1a"), At([0.6, 0.25, 1.81], 0.35, 1.4, color="#1e1a18"),
          B([0.9, 0.05, 1.5], [1.5, y0 + bh + 0.18, 0], color="#4a3428", roll=-14)]  # bonnet popped up
    prop("Burnt Out Car", "Vehicles", "A burnt-out wreck on bare rims: rust and soot. Solid cover.", p,
         collider=car_collider(L, W, y0, bh, cab, ch), scatter={"randomYaw": True, "scale": [1, 1]})


def car_on_bricks():
    L, W, y0, bh, cab, ch = SEDAN
    p, _ = car_parts(L, W, "#5d2f6b", y0, bh, cab, ch, wheels=False)
    wb = L / 2 - 0.82
    for x in (wb, -wb):
        for z in (W / 2 - 0.3, -W / 2 + 0.3):
            p += [At([0.4, 0.15, 0.2], 0, x, z, color="concrete", mat="concrete"), At([0.2, 0.15, 0.4], 0.15, x, z, color="concrete", mat="concrete")]
        p += HCyl(0.3, 1.5, 0.42, x, 0, color="rust", alongZ=True)
    prop("Car On Bricks", "Vehicles", "A purple saloon with its wheels stolen, propped on cinder blocks.", p,
         collider=car_collider(L, W, y0, bh, cab, ch), scatter=CAR_SCATTER)


def overturned_car():
    L, W, y0, bh, cab, ch = SEDAN
    p, _ = car_parts(L, W, "#4f6a3a", y0, bh, cab, ch)
    p = flip_upside_down(p)
    _, hi = bounds({"parts": p})
    p += [At([L - 0.4, 0.04, W - 0.2], hi[1] - 0.03, color=TRIM), At([0.5, 0.2, 0.5], hi[1] - 0.02, 0.3, color="#2a2a2a")]  # underside, sump
    prop("Overturned Car", "Vehicles", "A green saloon lying on its roof after a crash.", p,
         collider=[C([cab[1] - cab[0] + 0.3, ch + 0.07, W - 0.16], [(cab[0] + cab[1]) / 2, (ch + 0.07) / 2, 0]),
                   C([L + 0.18, bh, W + 0.04], [0, ch + 0.07 + bh / 2, 0])],
         scatter={"randomYaw": True, "scale": [1, 1]})


def bus():
    L, W = 11.0, 2.5
    red = "#b8261e"
    p = [At([L, 1.0, W], 0.38, color=red), At([L - 0.6, 0.95, W - 0.04], 1.38, -0.15, color=GL), At([L, 0.5, W], 2.33, color=red),
         At([L - 0.2, 0.08, W - 0.1], 2.83, color="lightgrey"), At([2.2, 0.28, 1.4], 2.91, -2.5, color="lightgrey"),
         At([0.04, 1.3, W - 0.2], 1.0, L / 2 + 0.01, color=GL), At([0.05, 0.24, 1.7], 2.45, L / 2 + 0.01, color="#ff9a1a"),
         At([0.16, 0.3, W + 0.04], 0.32, L / 2 + 0.04, color="darkgrey"), At([0.16, 0.3, W + 0.04], 0.32, -L / 2 - 0.04, color="darkgrey"),
         At([0.04, 0.14, 0.3], 0.75, L / 2 + 0.03, 0.9, color=HEAD), At([0.04, 0.14, 0.3], 0.75, L / 2 + 0.03, -0.9, color=HEAD),
         At([0.04, 0.2, 0.2], 0.85, -L / 2 - 0.01, 1.0, color=TAIL), At([0.04, 0.2, 0.2], 0.85, -L / 2 - 0.01, -1.0, color=TAIL),
         At([0.04, 0.9, W - 0.4], 1.4, -L / 2 - 0.01, color=GL),
         At([1.1, 1.95, 0.02], 0.42, 4.3, W / 2 + 0.01, color="#1e2a30"), At([0.02, 1.95, 0.025], 0.42, 4.3, W / 2 + 0.012, color="lightgrey"),
         At([1.1, 1.95, 0.02], 0.42, -0.6, W / 2 + 0.01, color="#1e2a30"), At([0.02, 1.95, 0.025], 0.42, -0.6, W / 2 + 0.012, color="lightgrey"),
         At([L - 0.3, 0.1, W + 0.02], 1.25, color="white"),
         At([0.2, 0.16, W + 0.2], 2.0, L / 2 - 0.1, color=TRIM)]  # mirrors
    for x in (-4.5, -3.0, -1.5, 1.0, 2.5, 3.6):  # window pillars
        p.append(At([0.12, 0.95, W + 0.004], 1.38, x, color=red))
    for x, z in ((3.4, 1), (3.4, -1), (-3.1, 1), (-3.1, -1)):
        p += wheel(1.0, 0.32, x, z * (W / 2 - 0.12))
    prop("Bus", "Vehicles", "A red city bus, 11 m long. A big wall of cover.", p, collider=[C([L + 0.24, 2.9, W + 0.04])], scatter=CAR_SCATTER)


def box_truck():
    L, W = 7.4, 2.3
    p = [At([L - 0.3, 0.25, 0.9], 0.5, -0.1, color=TRIM),                                  # chassis
         At([2.0, 1.3, W], 0.6, L / 2 - 1.0, color="white"), At([1.9, 0.75, W - 0.08], 1.9, L / 2 - 1.05, color="white"),
         At([0.05, 0.62, W - 0.2], 1.92, L / 2 - 0.08, color=GL), At([0.7, 0.55, W + 0.004], 1.95, L / 2 - 0.55, color=GL),
         At([5.3, 2.65, 2.44], 0.78, -L / 2 + 2.65, color="#e6e4dc"), At([5.32, 0.4, 2.45], 2.6, -L / 2 + 2.65, color="#2e7d4a"),
         At([0.04, 2.5, 2.3], 0.85, -L / 2 - 0.01, color="lightgrey")]
    for y in (1.3, 1.8, 2.3, 2.8):
        p.append(At([0.05, 0.03, 2.3], y, -L / 2 - 0.015, color="grey"))
    p += [At([0.16, 0.26, W + 0.04], 0.42, L / 2 + 0.02, color="darkgrey"), At([0.03, 0.12, 0.3], 0.9, L / 2 + 0.01, 0.8, color=HEAD),
          At([0.03, 0.12, 0.3], 0.9, L / 2 + 0.01, -0.8, color=HEAD), At([0.03, 0.3, 1.0], 0.9, L / 2 + 0.01, color=TRIM),
          At([0.04, 0.16, 0.16], 0.9, -L / 2 - 0.03, 1.0, color=TAIL), At([0.04, 0.16, 0.16], 0.9, -L / 2 - 0.03, -1.0, color=TAIL),
          At([0.12, 0.4, W + 0.3], 1.9, L / 2 - 0.3, color=TRIM), At([0.8, 0.5, 0.25], 0.55, 1.6, 1.0, color="steel"),
          At([0.4, 0.2, 2.44], 0.5, -L / 2 + 0.2, color="darkgrey")]
    for x, z in ((2.55, 1), (2.55, -1), (-2.4, 1), (-2.4, -1)):
        p += wheel(0.95, 0.3, x, z * (W / 2 - 0.12))
    prop("Box Truck", "Vehicles", "A delivery truck with a white cab and a tall cargo box.", p,
         collider=[C([2.1, 2.7, W + 0.04], [L / 2 - 1.0, 1.35, 0]), C([5.4, 3.43, 2.46], [-L / 2 + 2.65, 1.715, 0])], scatter=CAR_SCATTER)


def truck_cab():
    L, W = 6.0, 2.45
    blue = "#24519a"
    p = [At([L - 0.3, 0.3, 1.0], 0.55, -0.1, color=TRIM),
         At([1.3, 0.9, 2.2], 0.75, 2.3, color=blue), At([2.3, 1.75, W], 0.85, 0.95, color=blue), At([0.05, 0.75, W - 0.2], 1.75, 2.12, color=GL),
         At([0.6, 0.55, W + 0.004], 1.85, 1.6, color=GL), At([1.0, 0.45, W - 0.2], 2.6, 0.7, color=blue), At([0.04, 0.5, 1.4], 0.85, 2.96, color="chrome"),
         At([0.16, 0.3, W + 0.04], 0.45, 2.98, color="chrome"), At([0.03, 0.14, 0.3], 1.1, 2.96, 0.85, color=HEAD), At([0.03, 0.14, 0.3], 1.1, 2.96, -0.85, color=HEAD),
         At([1.2, 0.08, 1.2], 1.15, -2.0, color="darkgrey"), At([0.1, 0.4, W + 0.3], 2.0, 2.0, color=TRIM)]
    p += Cyl(0.14, 2.6, 1.3, -0.25, 1.05, color="chrome") + Cyl(0.14, 2.6, 1.3, -0.25, -1.05, color="chrome")
    p += HCyl(0.6, 1.1, 0.75, -0.2, 0.95, color="chrome") + HCyl(0.6, 1.1, 0.75, -0.2, -0.95, color="chrome")
    for x in (2.25, -1.45, -2.55):
        for z in (1, -1):
            p += wheel(1.0, 0.3, x, z * (W / 2 - 0.13))
    prop("Truck Cab", "Vehicles", "A blue semi-truck tractor without its trailer, chrome stacks and all.", p,
         collider=[C([3.6, 3.05, W], [1.4, 1.525, 0]), C([2.6, 1.25, 2.3], [-1.6, 0.625, 0])], scatter=CAR_SCATTER)


def forklift():
    y = "#e3a21a"
    p = [At([1.7, 0.7, 1.1], 0.22, -0.1, color=y), At([0.55, 0.85, 1.12], 0.2, -0.95, color="darkgrey"),
         At([0.5, 0.12, 0.5], 0.92, -0.35, color=TRIM), At([0.12, 0.45, 0.5], 1.0, -0.6, color=TRIM),
         At([0.08, 0.5, 0.08], 0.92, 0.25, color=TRIM), At([0.4, 0.04, 0.04], 1.4, 0.25, color=TRIM)]  # seat, steering
    for x in (0.55, -0.85):
        for z in (0.5, -0.5):
            p.append(At([0.07, 1.25, 0.07], 0.92, x, z, color=TRIM))
    p += [At([1.5, 0.06, 1.1], 2.17, -0.15, color=TRIM)]
    for k in range(4):
        p.append(At([1.4, 0.03, 0.04], 2.23, -0.15, -0.45 + k * 0.3, color="darkgrey"))
    p += [At([0.09, 2.2, 0.09], 0.12, 0.88, 0.36, color="darkgrey"), At([0.09, 2.2, 0.09], 0.12, 0.88, -0.36, color="darkgrey"),
          At([0.09, 0.08, 0.8], 2.25, 0.88, color="darkgrey"), At([0.1, 0.5, 0.95], 0.1, 1.0, color="darkgrey"),
          At([1.05, 0.05, 0.12], 0.06, 1.55, 0.3, color="darkgrey"), At([1.05, 0.05, 0.12], 0.06, 1.55, -0.3, color="darkgrey"),
          At([0.05, 0.6, 0.12], 0.06, 1.05, 0.3, color="darkgrey"), At([0.05, 0.6, 0.12], 0.06, 1.05, -0.3, color="darkgrey"),
          At([0.12, 0.1, 0.12], 2.23, -0.85, 0.4, color="orange")]
    p += wheel(0.6, 0.22, 0.55, 0.47, tyre="rubber", hub="#e3a21a") + wheel(0.6, 0.22, 0.55, -0.47, hub="#e3a21a")
    p += wheel(0.46, 0.18, -0.85, 0.48, hub="#e3a21a") + wheel(0.46, 0.18, -0.85, -0.48, hub="#e3a21a")
    prop("Forklift", "Vehicles", "A yellow warehouse forklift with its forks down.", p,
         collider=[C([2.3, 1.05, 1.14], [-0.1, 0.525, 0]), C([0.25, 2.3, 0.9], [0.95, 1.15, 0]), C([1.5, 0.1, 1.12], [-0.15, 2.17, 0])],
         scatter={"randomYaw": True, "scale": [1, 1]})


def golf_cart():
    p = [At([2.3, 0.4, 1.15], 0.22, color="white"), At([0.6, 0.3, 1.1], 0.62, 0.75, color="white"), At([0.05, 0.4, 1.0], 0.92, 0.45, color=GL),
         At([0.55, 0.14, 1.0], 0.62, -0.2, color="#2a4a2a"), At([0.12, 0.45, 1.0], 0.76, -0.5, color="#2a4a2a"),
         At([0.6, 0.35, 1.0], 0.62, -0.85, color="#e6e4dc"), At([0.04, 0.4, 0.04], 0.86, 0.35, color=TRIM), At([0.04, 0.04, 0.3], 1.2, 0.3, color=TRIM)]
    for x in (0.45, -0.55):
        for z in (0.5, -0.5):
            p.append(At([0.05, 1.05 if x > 0 else 1.05, 0.05], 0.62, x, z, color="lightgrey"))
    p += [At([1.4, 0.06, 1.25], 1.67, -0.05, color="white")]
    p += [At([0.3, 0.6, 0.3], 0.97, -1.0, 0.25, color="#4a3a2a"), At([0.3, 0.6, 0.3], 0.97, -1.0, -0.25, color="#20304a")]  # golf bags
    for x in (0.75, -0.75):
        for z in (0.52, -0.52):
            p += wheel(0.44, 0.17, x, z, hub="lightgrey")
    prop("Golf Cart", "Vehicles", "A white golf cart with a canopy and two golf bags.", p,
         collider=[C([2.4, 1.0, 1.2], [-0.05, 0.5, 0]), C([1.4, 0.1, 1.25], [-0.05, 1.68, 0])],
         scatter={"randomYaw": True, "scale": [1, 1]}, breakable=metal(320, count=14, size=(0.08, 0.3), colors=["white", "lightgrey", "rubber"]))


def motorbike():
    p = wheel(0.62, 0.14, 0.72, 0) + wheel(0.62, 0.14, -0.72, 0)
    p += [At([0.42, 0.34, 0.28], 0.28, -0.02, color="darkgrey"), At([0.5, 0.2, 0.3], 0.72, 0.18, color="#c41c1c"),
          At([0.6, 0.1, 0.28], 0.76, -0.35, color=TRIM), At([0.35, 0.08, 0.2], 0.82, -0.72, color="#c41c1c"),
          tube([0.0, 0.5, 0], [-0.72, 0.31, 0], 0.06, color="darkgrey"), tube([0.15, 0.45, 0.1], [-0.75, 0.42, 0.16], 0.08, color="chrome"),
          tube([0.72, 0.31, 0.09], [0.52, 0.98, 0.09], 0.05, color="chrome"), tube([0.72, 0.31, -0.09], [0.52, 0.98, -0.09], 0.05, color="chrome"),
          At([0.05, 0.04, 0.72], 1.0, 0.5, color=TRIM), At([0.08, 0.12, 0.16], 0.86, 0.62, color=HEAD),
          B([0.5, 0.04, 0.16], [0.7, 0.66, 0], color="#c41c1c", roll=-8), tube([0.4, 0.62, 0], [0.1, 0.4, 0], 0.08, color="darkgrey"),
          At([0.04, 0.3, 0.04], 0.0, -0.1, -0.16, color="darkgrey")]
    prop("Motorbike", "Vehicles", "A red road motorbike on its side stand.", p, collider=[C([2.1, 1.05, 0.5])],
         scatter={"randomYaw": True, "scale": [1, 1]}, breakable=metal(200, count=12, colors=["#c41c1c", "darkgrey", "chrome"]))


def moped():
    g = "#7fb8a8"
    p = wheel(0.42, 0.11, 0.6, 0, hub="lightgrey") + wheel(0.42, 0.11, -0.55, 0, hub="lightgrey")
    p += [At([0.9, 0.08, 0.32], 0.22, -0.05, color=TRIM), At([0.12, 0.7, 0.42], 0.22, 0.4, color=g), B([0.08, 0.7, 0.12], [0.52, 0.78, 0], color=g, roll=18),
          At([0.75, 0.38, 0.42], 0.32, -0.45, color=g), At([0.6, 0.1, 0.28], 0.7, -0.4, color="#5a3a24"), At([0.05, 0.04, 0.6], 1.1, 0.42, color="chrome"),
          At([0.1, 0.12, 0.14], 1.04, 0.47, color=HEAD), At([0.3, 0.06, 0.18], 0.42, 0.6, color=g), At([0.25, 0.2, 0.3], 0.8, -0.82, color="chrome"),
          At([0.04, 0.25, 0.04], 0.0, -0.1, -0.18, color="darkgrey")]
    prop("Moped Scooter", "Vehicles", "A mint green moped scooter with a brown seat.", p, collider=[C([1.4, 1.1, 0.5])],
         scatter={"randomYaw": True, "scale": [1, 1]}, breakable=metal(150, count=10, colors=[g, "lightgrey", "rubber"]))


def bicycle():
    r = 0.33
    p = vring(0.66, 0.05, 0.04, 0.52, r, 0, color="rubber") + vring(0.66, 0.05, 0.04, -0.52, r, 0, color="rubber")
    f = "#2a6ab0"
    bb = [0.0, 0.3, 0]
    st = [-0.15, 0.82, 0]
    ht = [0.38, 0.84, 0]
    p += [tube(bb, st, 0.035, f), tube(st, ht, 0.035, f), tube(bb, [0.42, 0.7, 0], 0.04, f), tube(bb, [-0.52, r, 0], 0.03, f),
          tube(st, [-0.52, r, 0], 0.03, f), tube(ht, [0.52, r, 0], 0.03, "chrome"), At([0.24, 0.05, 0.1], 0.86, -0.18, color=TRIM),
          At([0.04, 0.04, 0.5], 0.95, 0.36, color=TRIM), tube(ht, [0.36, 0.95, 0], 0.03, "chrome"),
          At([0.05, 0.05, 0.12], 0.27, 0.0, color="darkgrey"), At([0.16, 0.02, 0.06], 0.18, 0.08, 0.08, color=TRIM)]
    p += [At([0.03, 0.06, 0.03], 0.3, 0.52, color="steel"), At([0.03, 0.06, 0.03], 0.3, -0.52, color="steel")]
    prop("Bicycle", "Vehicles", "A blue bicycle standing on its own.", p, collision="cosmetic",
         scatter={"randomYaw": True, "scale": [1, 1]}, breakable=metal(60, count=8, size=(0.04, 0.2), colors=[f, "rubber", "chrome"]))


def kick_scooter():
    p = [At([0.6, 0.04, 0.14], 0.07, color="#d02a7a"), At([0.04, 0.04, 0.14], 0.07, 0.34, color="darkgrey"),
         tube([0.36, 0.08, 0], [0.32, 0.88, 0], 0.035, "steel"), At([0.03, 0.03, 0.4], 0.88, 0.32, color=TRIM)]
    p += HCyl(0.12, 0.03, 0.06, 0.36, 0, color="rubber", alongZ=True) + HCyl(0.12, 0.03, 0.06, -0.3, 0, color="rubber", alongZ=True)
    prop("Kick Scooter", "Vehicles", "A pink kick scooter.", p, collision="cosmetic", scatter=ANY,
         breakable=metal(60, count=6, size=(0.03, 0.12), colors=["#d02a7a", "steel"]))


def trailer():
    g = "#5b6b4a"
    p = [At([2.2, 0.08, 1.35], 0.42, color="darkgrey"), At([2.2, 0.4, 0.05], 0.5, 0, 0.65, color=g), At([2.2, 0.4, 0.05], 0.5, 0, -0.65, color=g),
         At([0.05, 0.4, 1.35], 0.5, -1.08, color=g), At([0.05, 0.4, 1.35], 0.5, 1.08, color=g),
         tube([1.1, 0.42, 0.55], [2.1, 0.42, 0.04], 0.07, "darkgrey"), tube([1.1, 0.42, -0.55], [2.1, 0.42, -0.04], 0.07, "darkgrey"),
         At([0.18, 0.08, 0.12], 0.4, 2.15, color="steel"), At([0.05, 0.4, 0.05], 0.0, 1.9, color="darkgrey"), At([0.14, 0.04, 0.14], 0.0, 1.9, color="darkgrey"),
         At([0.6, 0.1, 0.25], 0.62, 0, 0.7, color=TRIM), At([0.6, 0.1, 0.25], 0.62, 0, -0.7, color=TRIM),
         At([0.04, 0.06, 0.12], 0.55, -1.11, 0.5, color=TAIL), At([0.04, 0.06, 0.12], 0.55, -1.11, -0.5, color=TAIL)]
    p += wheel(0.55, 0.16, 0, 0.62) + wheel(0.55, 0.16, 0, -0.62)
    prop("Utility Trailer", "Vehicles", "A small open box trailer with a tow hitch.", p,
         collider=[C([2.2, 0.9, 1.4]), C([1.1, 0.45, 0.5], [1.65, 0.225, 0])], scatter={"randomYaw": True, "scale": [1, 1]},
         breakable=metal(300, count=14, size=(0.08, 0.3), colors=[g, "darkgrey"]))


def jerry_can():
    p = [At([0.34, 0.44, 0.17], 0, color="#b8241c"), B([0.3, 0.03, 0.185], [0, 0.22, 0], color="#8e1a14", roll=40),
         B([0.3, 0.03, 0.185], [0, 0.22, 0], color="#8e1a14", roll=-40), At([0.2, 0.05, 0.05], 0.44, -0.04, color="#b8241c"),
         At([0.05, 0.08, 0.05], 0.44, 0.12, color="#b8241c"), At([0.06, 0.03, 0.06], 0.52, 0.12, color="darkgrey")]
    prop("Jerry Can", "Vehicles", "A red fuel can. Shoot it for a small fireball.", p, collision="cosmetic", scatter=ANY,
         breakable={"health": 60, "breakBy": "all", "sound": "metal", "debris": {"count": 8, "size": [0.04, 0.12], "speed": 6, "colors": ["#b8241c", "darkgrey"]},
                    "explode": {"radius": 3.5, "damage": 70}})


def engine_block():
    p = [At([0.6, 0.45, 0.5], 0.08, color="#3a3c3e", mat="metal"), At([0.62, 0.12, 0.44], 0.53, color="#7a2a20"),
         At([0.7, 0.08, 0.6], 0.0, color="darkgrey"), At([0.12, 0.25, 0.25], 0.15, 0.36, color="darkgrey")]
    for x in (-0.2, 0.0, 0.2):
        p.append(At([0.06, 0.06, 0.06], 0.65, x, 0.1, color="steel"))
    p += HCyl(0.28, 0.06, 0.3, -0.33, 0, color="darkgrey")
    prop("Engine Block", "Vehicles", "A car engine block on a scrap pallet.", p, collider=[C([0.72, 0.66, 0.6])], scatter=ANY)


def car_door():
    p = [At([1.05, 0.06, 0.55], 0, color="#3d6f9e"), At([0.85, 0.04, 0.4], 0.0, 0.08, 0.42, color="darkgrey"),
         At([0.85, 0.05, 0.04], 0.0, 0.08, 0.6, color="darkgrey"), At([0.14, 0.02, 0.04], 0.06, -0.3, 0.1, color=TRIM)]
    prop("Car Door", "Vehicles", "A blue car door lying on the ground, window frame empty.", p, collision="cosmetic", scatter=ANY)


def car_wheel():
    p = Ring(0.64, 0.36, 0.2, 0.0, color="rubber") + Cyl(0.38, 0.19, 0.005, color="steel") + Cyl(0.1, 0.02, 0.195, color="darkgrey")
    prop("Car Wheel", "Vehicles", "A loose car wheel lying flat.", p, collision="cosmetic", scatter={"randomYaw": True, "scale": [0.95, 1.05]})


# ================================================================== Rooftop ==================================================================

def louvres(w, h, x, y0, z, n, color=TRIM, depth=0.02):
    return [At([w, 0.02, depth], y0 + (i + 0.5) * h / n, x, z, color=color) for i in range(n)]


def window_ac():
    p = [At([0.66, 0.42, 0.62], 0, color=CREAM), At([0.6, 0.3, 0.012], 0.05, -0.02, 0.31, color="#8d8a7e")]
    p += louvres(0.58, 0.3, -0.02, 0.05, 0.318, 7)
    p += [At([0.14, 0.32, 0.012], 0.05, 0.25, 0.312, color="#bdb8a4"), At([0.06, 0.06, 0.016], 0.25, 0.25, 0.314, color="darkgrey"),
          At([0.012, 0.2, 0.4], 0.1, 0.33, -0.05, color="#8d8a7e"), At([0.012, 0.2, 0.4], 0.1, -0.33, -0.05, color="#8d8a7e"),
          At([0.7, 0.03, 0.2], 0.0, 0, -0.38, color="steel"), At([0.04, 0.04, 0.2], 0.0, 0.3, -0.38, color="steel")]
    prop("Window AC Unit", "Rooftop", "A window air conditioner (the front faces +Z; mount it on a sill with the Prop op's y).", p,
         collider=[C([0.66, 0.42, 0.62])], scatter=SQ0, breakable=metal(120, colors=[CREAM, "#8d8a7e"]))


def condenser():
    p = [At([1.0, 0.08, 1.0], 0, color="darkgrey"), At([0.96, 0.85, 0.96], 0.08, color="#cfcfc8", mat="metal")]
    for s in (1, -1):
        p += [At([0.8, 0.6, 0.012], 0.18, 0, s * 0.484, color="#5d5f60"), At([0.012, 0.6, 0.8], 0.18, s * 0.484, 0, color="#5d5f60")]
    p += Cyl(0.78, 0.04, 0.93, color=TRIM) + Cyl(0.62, 0.012, 0.97, color="#5d5f60")
    for k in range(4):
        p.append(At([0.78, 0.02, 0.02], 0.975, 0, -0.27 + k * 0.18, color="#9a9c9e"))
    p += [At([0.12, 0.1, 0.06], 0.95, 0, color=TRIM), At([0.2, 0.25, 0.012], 0.4, 0.3, 0.49, color="#e8e6dc"),
          HCyl(0.05, 0.4, 0.25, 0.3, -0.5, color="#b07040", alongZ=True)]
    prop("Rooftop Condenser", "Rooftop", "A square air conditioning condenser with a fan grille on top.", p,
         collider=[C([1.0, 1.0, 1.0])], scatter=SQ, breakable=metal(250, count=14, size=(0.08, 0.3), colors=["#cfcfc8", "#5d5f60"]))


def split_ac():
    p = [At([0.86, 0.58, 0.32], 0.06, color="#ecebe6"), At([0.05, 0.06, 0.4], 0.0, 0.35, 0.02, color="darkgrey"),
         At([0.05, 0.06, 0.4], 0.0, -0.35, 0.02, color="darkgrey")]
    # the fan grille: a ring of dark bars on the front face
    for a in (0, 45, 90, 135):
        p.append(B([0.46, 0.19, 0.012], [-0.12, 0.35, 0.163], color="#5d5f60", roll=a))
    for a in (0, 45, 90, 135):
        p.append(B([0.32, 0.13, 0.014], [-0.12, 0.35, 0.165], color="#9a9c9e", roll=a))
    p += [At([0.08, 0.08, 0.016], 0.31, -0.12, 0.168, color="#5d5f60"), At([0.18, 0.4, 0.012], 0.14, 0.3, 0.163, color="#d8d7d0"),
          At([0.012, 0.4, 0.26], 0.12, 0.43, 0, color="#9a9c9e"), HCyl(0.04, 0.3, 0.2, 0.35, -0.2, color="#b07040", alongZ=True)]
    prop("Split AC Unit", "Rooftop", "The outdoor half of a split air conditioner, on its feet (front faces +Z).", p,
         collider=[C([0.88, 0.64, 0.4])], scatter=SQ, breakable=metal(110, colors=["#ecebe6", "#5d5f60"]))


def hvac():
    p = [At([3.1, 0.2, 1.7], 0, color="darkgrey"), At([3.0, 1.15, 1.6], 0.2, color="#bfc1bd", mat="metal"),
         At([3.02, 0.06, 1.62], 1.35, color="#a5a7a3")]
    for x in (-0.75, 0.75):
        p += Cyl(0.8, 0.12, 1.41, x, 0, color="#5d5f60") + Cyl(0.66, 0.012, 1.53, x, 0, color=TRIM)
        for k in range(3):
            p.append(At([0.78, 0.02, 0.02], 1.545, x, -0.2 + k * 0.2, color="#9a9c9e"))
    for x in (-1.1, -0.4, 0.3, 1.0):
        p.append(At([0.5, 0.8, 0.012], 0.35, x, 0.8, color="#8f918d"))
        p += louvres(0.46, 0.7, x, 0.4, 0.808, 6, color="#6f716d")
    p += [At([0.012, 0.9, 1.2], 0.3, 1.5, color="#6f716d"), At([0.3, 0.3, 0.012], 0.6, -1.3, -0.806, color="yellow")]
    prop("Rooftop HVAC Unit", "Rooftop", "A big packaged rooftop air handler with two fans. Solid cover, unbreakable.", p,
         collider=[C([3.1, 1.55, 1.7])], scatter=SQ)


def mushroom_vent():
    p = Cyl(0.3, 0.06, 0.0, color="#9a9c9e") + Cyl(0.2, 0.5, 0.06, color="galvanized", mat="metal") + Cyl(0.44, 0.06, 0.6, color="galvanized") \
        + Cyl(0.3, 0.06, 0.66, color="galvanized") + Cyl(0.26, 0.04, 0.56, color="darkgrey")
    prop("Mushroom Vent", "Rooftop", "A galvanized mushroom-cap roof vent.", p, collider=[C([0.44, 0.72, 0.44])], scatter=ANY,
         breakable=metal(90, count=8, colors=["galvanized"]))


def turbine_vent():
    p = Cyl(0.42, 0.05, 0.0, color="#9a9c9e") + Cyl(0.32, 0.35, 0.05, color="galvanized", mat="metal")
    y = 0.4
    for d, h in ((0.46, 0.08), (0.56, 0.18), (0.5, 0.1), (0.36, 0.06)):
        p += Cyl(d, h, y, color="#c4c6c6")
        y += h
    for a in range(0, 180, 30):
        p.append(B([0.58, 0.3, 0.015], [0, 0.6, 0], color="#a4a6a6", yaw=a))
    p += Cyl(0.08, 0.04, y, color="darkgrey")
    prop("Turbine Vent", "Rooftop", "A whirlybird turbine roof vent.", p, collider=[C([0.56, 0.86, 0.56])], scatter=ANY,
         breakable=metal(90, count=8, colors=["galvanized", "#c4c6c6"]))


def duct_seg(L, w, h, y0, x=0.0, z=0.0, alongZ=False):
    """A rectangular galvanized duct run with flanges every metre."""
    if alongZ:
        p = [At([w, h, L], y0, x, z, color="galvanized", mat="metal")]
        n = max(1, int(L))
        for k in range(n + 1):
            p.append(At([w + 0.04, h + 0.04, 0.03], y0 - 0.02, x, z - L / 2 + k * L / n, color="#9a9c9e"))
    else:
        p = [At([L, h, w], y0, x, z, color="galvanized", mat="metal")]
        n = max(1, int(L))
        for k in range(n + 1):
            p.append(At([0.03, h + 0.04, w + 0.04], y0 - 0.02, x - L / 2 + k * L / n, z, color="#9a9c9e"))
    return p


def duct_stand(x, z, top, w=0.7, alongZ=False):
    if alongZ:
        return [At([w, 0.05, 0.08], top - 0.05, x, z, color="darkgrey"), At([0.05, top - 0.05, 0.05], 0, x - w / 2 + 0.03, z, color="darkgrey"),
                At([0.05, top - 0.05, 0.05], 0, x + w / 2 - 0.03, z, color="darkgrey"), At([w + 0.1, 0.04, 0.2], 0, x, z, color="#8a8a86")]
    return [At([0.08, 0.05, w], top - 0.05, x, z, color="darkgrey"), At([0.05, top - 0.05, 0.05], 0, x, z - w / 2 + 0.03, color="darkgrey"),
            At([0.05, top - 0.05, 0.05], 0, x, z + w / 2 - 0.03, color="darkgrey"), At([0.2, 0.04, w + 0.1], 0, x, z, color="#8a8a86")]


def air_duct():
    p = duct_seg(3.0, 0.6, 0.5, 0.3) + duct_stand(-1.0, 0, 0.3) + duct_stand(1.0, 0, 0.3)
    prop("Air Duct", "Rooftop", "A 3 m run of rectangular air duct on low stands (along X).", p,
         collider=[C([3.04, 0.82, 0.66])], scatter={"randomYaw": False, "yawStep": 90},
         breakable=metal(160, count=12, size=(0.08, 0.35), colors=["galvanized", "#9a9c9e"]))


def duct_elbow():
    p = duct_seg(2.0, 0.6, 0.5, 0.3, x=-0.7) + duct_seg(1.4, 0.6, 0.5, 0.3, x=0.6, z=0.4, alongZ=True)
    p += duct_stand(-1.2, 0, 0.3) + duct_stand(0.6, 0.6, 0.3, alongZ=True)
    p += [At([0.62, 0.52, 0.62], 0.29, 0.6, -0.3, color="#b4b6b6")]
    prop("Air Duct Elbow", "Rooftop", "A rectangular air duct turning a corner on low stands.", p,
         collider=[C([2.0, 0.82, 0.66], [-0.7, 0.41, 0]), C([0.66, 0.82, 1.7], [0.6, 0.41, 0.25])], scatter={"randomYaw": False, "yawStep": 90},
         breakable=metal(160, count=12, size=(0.08, 0.35), colors=["galvanized", "#9a9c9e"]))


def duct_riser():
    p = [At([0.7, 0.08, 0.6], 0, color="#8a8a86"), At([0.6, 1.9, 0.5], 0.08, color="galvanized", mat="metal")]
    for y in (0.08, 0.98, 1.88):
        p.append(At([0.64, 0.03, 0.54], y, color="#9a9c9e"))
    p += duct_seg(1.6, 0.5, 0.5, 1.48, x=1.1) + [At([0.62, 0.52, 0.52], 1.47, 0, color="#b4b6b6")]
    p += [At([0.05, 1.47, 0.05], 0, 1.7, color="darkgrey"), At([0.3, 0.04, 0.3], 0, 1.7, color="#8a8a86")]
    prop("Air Duct Riser", "Rooftop", "An air duct coming up out of the roof and turning along it.", p,
         collider=[C([0.64, 2.0, 0.56]), C([1.6, 0.54, 0.54], [1.1, 1.75, 0])], scatter={"randomYaw": False, "yawStep": 90},
         breakable=metal(170, count=12, size=(0.08, 0.35), colors=["galvanized", "#9a9c9e"]))


def round_duct():
    p = HCyl(0.5, 3.0, 0.55, color="galvanized", mat="metal")
    for x in (-1.5, -0.5, 0.5, 1.5):
        p += HCyl(0.54, 0.04, 0.55, x, color="#9a9c9e")
    for x in (-0.9, 0.9):
        p += [At([0.06, 0.32, 0.5], 0, x, color="darkgrey"), At([0.2, 0.04, 0.6], 0, x, color="#8a8a86")]
    prop("Round Duct", "Rooftop", "A 3 m spiral round air duct on two saddles (along X).", p,
         collider=[C([3.04, 0.82, 0.56])], scatter={"randomYaw": False, "yawStep": 90},
         breakable=metal(140, count=10, size=(0.08, 0.3), colors=["galvanized"]))


def exhaust_fan():
    p = [At([1.0, 0.3, 1.0], 0, color="#8a8a86"), At([0.9, 0.08, 0.9], 0.3, color="galvanized")]
    p += Cyl(0.82, 0.55, 0.38, color="#b4b6b6", mat="metal") + Cyl(0.9, 0.05, 0.93, color="#9a9c9e") + Cyl(0.7, 0.012, 0.98, color=TRIM)
    for k in range(4):
        p.append(At([0.7, 0.02, 0.02], 0.99, 0, -0.24 + k * 0.16, color="#9a9c9e"))
    p += [At([0.2, 0.2, 0.15], 0.5, 0, 0.45, color="darkgrey")]
    prop("Exhaust Fan", "Rooftop", "A rooftop upblast exhaust fan on its curb.", p, collider=[C([1.0, 1.0, 1.0])], scatter=SQ,
         breakable=metal(200, count=12, size=(0.08, 0.3), colors=["galvanized", "#b4b6b6"]))


def brick_chimney():
    p = [At([0.8, 1.8, 0.8], 0, color="brick", mat="brick"), At([0.92, 0.1, 0.92], 1.8, color="concrete", mat="concrete"),
         At([0.86, 0.06, 0.86], 1.5, color="#7a3a2a")]
    for x in (-0.18, 0.18):
        p += Cyl(0.22, 0.32, 1.9, x, 0, color="#b0603a") + Cyl(0.26, 0.04, 2.18, x, 0, color="#9a4a2a") + Cyl(0.14, 0.01, 2.22, x, 0, color=TRIM)
    prop("Brick Chimney", "Rooftop", "A brick chimney stack with two clay pots.", p, collider=[C([0.92, 1.9, 0.92])], scatter=SQ0)


def metal_flue():
    p = Cyl(0.5, 0.06, 0.0, color="#8a8a86") + Cyl(0.3, 2.5, 0.06, color="#9a9c9e", mat="metal")
    for y in (0.8, 1.6):
        p += Cyl(0.34, 0.04, y, color="#7a7c7e")
    p += Cyl(0.32, 0.12, 2.56, color="#7a7c7e") + Cyl(0.56, 0.05, 2.74, color="#9a9c9e") + Cyl(0.3, 0.05, 2.79, color="#9a9c9e")
    p += [At([0.04, 0.04, 0.04], 2.66, 0.12, 0.12, color=TRIM), At([0.04, 0.04, 0.04], 2.66, -0.12, -0.12, color=TRIM)]
    p += [tube([0.15, 2.0, 0], [0.75, 0.0, 0], 0.02, "darkgrey"), tube([-0.15, 2.0, 0], [-0.75, 0.0, 0], 0.02, "darkgrey")]
    prop("Metal Flue", "Rooftop", "A tall steel flue pipe with a rain cap and guy wires.", p, collider=[C([0.34, 2.84, 0.34])], scatter=ANY,
         breakable=metal(150, count=8, size=(0.06, 0.3), colors=["#9a9c9e"]))


def water_tower():
    p = []
    for x in (-0.9, 0.9):
        for z in (-0.9, 0.9):
            p.append(At([0.14, 3.2, 0.14], 0, x, z, color="darkgrey", mat="metal"))
            p.append(At([0.3, 0.06, 0.3], 0, x, z, color="#7a7c7e"))
    for y in (1.0, 2.1):
        p += [At([1.94, 0.06, 0.06], y, 0, 0.9, color="darkgrey"), At([1.94, 0.06, 0.06], y, 0, -0.9, color="darkgrey"),
              At([0.06, 0.06, 1.94], y, 0.9, color="darkgrey"), At([0.06, 0.06, 1.94], y, -0.9, color="darkgrey")]
    p += [tube([-0.9, 0.1, 0.92], [0.9, 2.1, 0.92], 0.04, "darkgrey"), tube([0.92, 0.1, -0.9], [0.92, 2.1, 0.9], 0.04, "darkgrey"),
          At([2.5, 0.12, 2.5], 3.2, color="darkwood", mat="wood")]
    p += Cyl(2.4, 2.3, 3.32, color="#7a5636", mat="wood")
    for y in (3.7, 4.4, 5.1):
        p += Cyl(2.46, 0.06, y, color=TRIM)
    y = 5.62
    for d in (2.5, 2.0, 1.4, 0.8):
        p += Cyl(d, 0.18, y, color="#4a3a2a")
        y += 0.18
    p += Cyl(0.2, 0.2, y, color="darkgrey")
    p += [At([0.4, 2.4, 0.04], 3.3, 0, 1.24, color="#5a4028")]  # access hatch strip
    p += [At([0.06, 3.3, 0.06], 0, 1.05, 0.2, color="steel"), At([0.06, 3.3, 0.06], 0, 1.05, -0.2, color="steel")]
    for k in range(10):
        p.append(At([0.04, 0.03, 0.4], 0.3 + k * 0.3, 1.05, color="steel"))
    prop("Water Tower", "Rooftop", "A wooden water tank on a steel stand, the city rooftop classic. 6.5 m tall.", p,
         collider=[C([0.16, 3.2, 0.16], [0.9, 1.6, 0.9]), C([0.16, 3.2, 0.16], [-0.9, 1.6, 0.9]), C([0.16, 3.2, 0.16], [0.9, 1.6, -0.9]),
                   C([0.16, 3.2, 0.16], [-0.9, 1.6, -0.9]), C([2.5, 0.12, 2.5], [0, 3.26, 0]), C([2.4, 3.0, 2.4], [0, 4.82, 0])],
         scatter=SQ0)


def poly_tank():
    p = Cyl(1.2, 1.35, 0.0, color="#1e1f20") + Cyl(1.24, 0.04, 0.3, color="#2a2b2c") + Cyl(1.24, 0.04, 0.8, color="#2a2b2c") \
        + Cyl(1.0, 0.08, 1.35, color="#1e1f20") + Cyl(0.4, 0.08, 1.43, color="#3a3b3c")
    p += [At([0.12, 0.12, 0.2], 0.1, 0, 0.62, color="#3a5a8a")]
    prop("Plastic Water Tank", "Rooftop", "A black plastic rooftop water tank.", p, collider=[C([1.22, 1.5, 1.22])], scatter=ANY,
         breakable=plastic(50, count=10, size=(0.08, 0.3), colors=["#1e1f20"]))


def satellite_dish():
    p = [At([0.3, 0.04, 0.3], 0, color="darkgrey"), At([0.05, 0.75, 0.05], 0.04, color="#9a9c9e"),
         At([0.12, 0.12, 0.1], 0.7, 0, 0.04, color="darkgrey")]
    for a in (0, 45, 90, 135):
        p.append(B([0.62, 0.257, 0.025], [0, 0.86, 0.12], color="#e6e6e2", pitch=-30, roll=a))
    p += [tube([0, 0.62, 0.18], [0, 0.82, 0.55], 0.025, "#9a9c9e"), At([0.07, 0.07, 0.1], 0.8, 0, 0.56, color=TRIM)]
    prop("Satellite Dish", "Rooftop", "A satellite TV dish on a short mast, looking up and to +Z.", p, collision="cosmetic", scatter=SQ,
         breakable=metal(80, count=8, size=(0.04, 0.2), colors=["#e6e6e2", "#9a9c9e"]))


def tv_antenna():
    p = [At([0.4, 0.04, 0.4], 0, color="darkgrey"), At([0.04, 2.2, 0.04], 0.04, color="#9a9c9e"), At([1.4, 0.03, 0.03], 2.0, color="#9a9c9e")]
    for k in range(7):
        x = -0.6 + k * 0.2
        w = 0.9 - k * 0.08
        p.append(At([0.02, 0.02, w], 2.015, x, color="#b4b6b6"))
    p += [At([0.03, 0.03, 0.9], 1.5, 0, color="#9a9c9e")]
    p += [tube([0, 1.2, 0], [0.5, 0.04, 0.5], 0.015, "darkgrey"), tube([0, 1.2, 0], [-0.5, 0.04, 0.5], 0.015, "darkgrey"),
          tube([0, 1.2, 0], [0, 0.04, -0.6], 0.015, "darkgrey")]
    prop("TV Antenna", "Rooftop", "An old roof TV aerial on a guyed mast.", p, collision="cosmetic", scatter=ANY,
         breakable=metal(60, count=6, size=(0.03, 0.2), colors=["#9a9c9e"]))


def radio_mast():
    h = 8.0
    s = 0.45
    p = [At([1.2, 0.2, 1.2], 0, color="concrete", mat="concrete")]
    for x in (-s, s):
        for z in (-s, s):
            p.append(At([0.06, h, 0.06], 0.2, x, z, color="#c8c8c4"))
    for k in range(8):
        y = 0.2 + (k + 0.5) * h / 8
        c = "#c4281c" if k % 2 == 0 else "white"
        p += [At([2 * s + 0.06, 0.05, 0.05], y, 0, s, color=c), At([2 * s + 0.06, 0.05, 0.05], y, 0, -s, color=c),
              At([0.05, 0.05, 2 * s + 0.06], y, s, 0, color=c), At([0.05, 0.05, 2 * s + 0.06], y, -s, 0, color=c)]
        y1 = y + h / 8
        if k < 7:
            p.append(tube([-s, y, s if k % 2 else -s], [s, y1, s if k % 2 else -s], 0.03, "#a8a8a4"))
            p.append(tube([s if k % 2 else -s, y, -s], [s if k % 2 else -s, y1, s], 0.03, "#a8a8a4"))
    p += [At([0.05, 1.6, 0.05], h + 0.2, color="#c8c8c4"), At([0.12, 0.12, 0.12], h + 1.8, color="#ff3020"),
          At([0.12, 0.9, 0.06], h - 1.2, s + 0.1, color="#e6e6e2"), At([0.06, 0.9, 0.12], h - 1.6, 0, -s - 0.1, color="#e6e6e2")]
    for a in (0, 45, 90, 135):  # a microwave dish on the -X side
        p.append(B([0.6, 0.25, 0.03], [-s - 0.12, h - 0.6, 0], color="#e6e6e2", yaw=90, roll=a))
    prop("Radio Mast", "Rooftop", "A red and white lattice radio mast, 10 m with its whip antenna.", p,
         collider=[C([1.2, 0.2, 1.2]), C([0.98, h, 0.98], [0, 0.2 + h / 2, 0])], scatter=SQ0)


def solar_panel():
    tilt = 28
    L, D = 1.7, 1.05
    base = [0, 0.75, 0]
    p = [tilted([L, 0.04, D], [0, 0, 0], base, tilt, "#1d2a4a"), tilted([L + 0.04, 0.03, D + 0.04], [0, -0.025, 0], base, tilt, "#c8c8c4")]
    for k in range(1, 6):
        p.append(tilted([0.012, 0.045, D - 0.02], [-L / 2 + k * L / 6, 0.003, 0], base, tilt, "#7a8aa8"))
    for k in range(1, 3):
        p.append(tilted([L - 0.02, 0.045, 0.012], [0, 0.003, -D / 2 + k * D / 3], base, tilt, "#7a8aa8"))
    zf = D / 2 * math.cos(math.radians(tilt))
    yf = 0.75 - D / 2 * math.sin(math.radians(tilt))
    yb = 0.75 + D / 2 * math.sin(math.radians(tilt))
    for x in (-0.7, 0.7):
        p += [At([0.05, yf - 0.03, 0.05], 0, x, zf - 0.05, color="#a8a8a4"), At([0.05, yb - 0.03, 0.05], 0, x, -zf + 0.05, color="#a8a8a4"),
              At([0.06, 0.04, 2 * zf + 0.1], 0, x, color="#a8a8a4")]
    prop("Solar Panel", "Rooftop", "A solar panel on a tilted frame, facing +Z. Glass: a couple of shots.", p,
         collider=[C([L, yb + 0.02, 2 * zf + 0.02])], scatter=SQ, breakable=glass(15, colors=["#1d2a4a", "#7a8aa8", "#c8c8c4"]))


def skylight():
    p = [At([1.3, 0.25, 1.3], 0, color="#8a8a86"), At([1.36, 0.04, 1.36], 0.25, color="#c8c8c4")]
    p += [B([1.26, 0.03, 0.72], [0, 0.42, 0.3], color="#8ab0c0", pitch=28), B([1.26, 0.03, 0.72], [0, 0.42, -0.3], color="#8ab0c0", pitch=-28),
          At([1.3, 0.04, 0.05], 0.56, color="#c8c8c4")]
    prop("Skylight", "Rooftop", "A ridged glass skylight on a curb.", p, collider=[C([1.36, 0.6, 1.36])], scatter=SQ,
         breakable=glass(20, count=14, colors=["#8ab0c0", "#c8c8c4"]))


def roof_hatch():
    p = [At([1.0, 0.45, 1.0], 0, color="#8a8a86", mat="concrete"), At([1.06, 0.08, 1.06], 0.45, color="#5a5c5e", mat="metal"),
         At([0.3, 0.04, 0.04], 0.53, 0, 0.38, color="yellow"), At([0.06, 0.12, 0.6], 0.42, -0.5, color="darkgrey")]
    prop("Roof Hatch", "Rooftop", "A steel roof access hatch on its curb.", p, collider=[C([1.06, 0.55, 1.06])], scatter=SQ)


def vent_grille():
    p = [At([0.62, 0.62, 0.06], 0, color="#9a9c9e"), At([0.54, 0.54, 0.02], 0.04, 0, 0.03, color=TRIM)]
    for i in range(7):
        p.append(B([0.54, 0.02, 0.06], [0, 0.08 + i * 0.075, 0.04], color="#b4b6b6", pitch=30))
    prop("Vent Grille", "Rooftop", "A louvred wall vent grille (front faces +Z; mount it on a wall with the Prop op's y).", p,
         collision="cosmetic", scatter=SQ0, breakable=metal(60, count=6, size=(0.04, 0.16), colors=["#9a9c9e"]))


def pipe_rack():
    p = []
    for x in (-1.2, 0, 1.2):
        p += [At([0.08, 0.5, 0.06], 0, x, -0.3, color="darkgrey"), At([0.08, 0.5, 0.06], 0, x, 0.3, color="darkgrey"),
              At([0.08, 0.06, 0.72], 0.5, x, color="darkgrey"), At([0.2, 0.04, 0.8], 0, x, color="#8a8a86")]
    p += HCyl(0.16, 3.0, 0.64, 0, -0.18, color="#e3a21a") + HCyl(0.12, 3.0, 0.62, 0, 0.06, color="#9a9c9e") + HCyl(0.1, 3.0, 0.61, 0, 0.24, color="#c4281c")
    p += [At([0.2, 0.22, 0.22], 0.53, 0.6, -0.18, color="#c49218"), At([0.04, 0.2, 0.04], 0.75, 0.6, -0.18, color="darkgrey"),
          At([0.24, 0.03, 0.03], 0.95, 0.6, -0.18, color="#c4281c")]
    prop("Rooftop Pipe Rack", "Rooftop", "Gas, water and fire pipes running along low steel supports (along X).", p,
         collider=[C([3.0, 0.75, 0.8])], scatter={"randomYaw": False, "yawStep": 90},
         breakable=metal(220, count=12, size=(0.06, 0.3), colors=["#e3a21a", "#9a9c9e", "#c4281c"]))


# ================================================================== Utility ==================================================================

def pole_parts(h=9.0):
    p = Cyl(0.3, h, 0.0, color="#5a4430", mat="wood")
    p += [At([2.2, 0.12, 0.12], h - 0.7, color="#5a4430", mat="wood"), At([1.4, 0.1, 0.1], h - 1.5, color="#5a4430", mat="wood"),
          tube([0.0, h - 1.2, 0.08], [0.6, h - 0.72, 0.08], 0.05, "darkgrey"), tube([0.0, h - 1.2, 0.08], [-0.6, h - 0.72, 0.08], 0.05, "darkgrey")]
    for x in (-0.95, -0.4, 0.4, 0.95):
        p += [At([0.08, 0.2, 0.08], h - 0.58, x, color="#9ac0a8"), At([0.12, 0.04, 0.12], h - 0.48, x, color="#9ac0a8")]
    for x in (-0.6, 0.6):
        p += [At([0.08, 0.16, 0.08], h - 1.4, x, color="#9ac0a8")]
    for k in range(6):
        p.append(At([0.25, 0.03, 0.03], 2.2 + k * 0.6, 0, 0, color="darkgrey", yaw=90 * (k % 2)))
    p += [At([0.14, 0.18, 0.02], 1.6, 0, 0.16, color="#e8d040")]
    return p


def utility_pole():
    p = pole_parts()
    prop("Utility Pole", "Utility", "A 9 m wooden power pole with crossarms and insulators.", p, collider=[C([0.32, 9.0, 0.32])], scatter=SQ0)


def pole_transformer():
    p = pole_parts()
    p += Cyl(0.6, 1.0, 6.2, 0, 0.5, color="#8a8e90", mat="metal") + Cyl(0.64, 0.06, 7.2, 0, 0.5, color="#6a6e70")
    p += [At([0.1, 0.6, 0.3], 6.4, 0, 0.2, color="darkgrey"), At([0.08, 0.2, 0.08], 7.26, 0.15, 0.5, color="#9ac0a8"),
          At([0.08, 0.2, 0.08], 7.26, -0.15, 0.5, color="#9ac0a8"), tube([0.15, 7.45, 0.5], [0.4, 8.3, 0.0], 0.02, TRIM),
          tube([-0.15, 7.45, 0.5], [-0.4, 8.3, 0.0], 0.02, TRIM)]
    prop("Pole Transformer", "Utility", "A power pole carrying a can transformer.", p, collider=[C([0.32, 9.0, 0.32])], scatter=SQ0)


def pad_transformer():
    g = "#4a6a48"
    p = [At([1.7, 0.15, 1.5], 0, color="concrete", mat="concrete"), At([1.4, 1.2, 1.2], 0.15, color=g, mat="metal"),
         At([1.44, 0.05, 1.24], 1.35, color="#3d5a3c"), At([0.012, 1.0, 0.012], 0.25, 0, 0.606, color=TRIM)]
    p += louvres(0.5, 0.25, -0.35, 0.95, 0.606, 4, color="#3d5a3c") + louvres(0.5, 0.25, 0.35, 0.95, 0.606, 4, color="#3d5a3c")
    p += [At([0.3, 0.22, 0.012], 0.55, 0.35, 0.607, color="yellow"), At([0.12, 0.1, 0.014], 0.6, 0.35, 0.608, color=TRIM),
          At([0.04, 0.12, 0.04], 0.7, -0.08, 0.62, color="darkgrey"), At([0.04, 0.12, 0.04], 0.7, 0.08, 0.62, color="darkgrey")]
    prop("Pad Transformer", "Utility", "A green pad-mounted transformer box on a concrete base. Solid, unbreakable.", p,
         collider=[C([1.44, 1.4, 1.24])], scatter=SQ)


def junction_box():
    p = [At([0.5, 0.6, 0.22], 0.8, color="#9a9c9e", mat="metal"), At([0.46, 0.56, 0.012], 0.82, 0, 0.11, color="#aeb0b0"),
         At([0.04, 0.08, 0.02], 1.05, 0.2, 0.12, color=TRIM), At([0.2, 0.14, 0.012], 1.2, -0.05, 0.118, color="yellow"),
         At([0.08, 0.06, 0.014], 1.23, -0.05, 0.12, color=TRIM)]
    p += Cyl(0.06, 0.8, 0.0, -0.12, 0, color="#7a7c7e") + Cyl(0.06, 0.8, 0.0, 0.12, 0, color="#7a7c7e")
    p += [At([0.5, 1.4, 0.04], 0.0, 0, -0.13, color="#6a6c6e")]  # backing board
    prop("Junction Box", "Utility", "A steel electrical junction box on its backboard, conduits running down.", p,
         collider=[C([0.5, 1.4, 0.3], [0, 0.7, -0.02])], scatter=SQ0, breakable=metal(200, count=10, colors=["#9a9c9e", "#6a6c6e"]))


def street_cabinet():
    g = "#5a6a5a"
    p = [At([1.1, 0.12, 0.5], 0, color="concrete", mat="concrete"), At([1.0, 1.25, 0.45], 0.12, color=g, mat="metal"),
         At([1.06, 0.08, 0.5], 1.37, color="#4a5a4a"), At([0.012, 1.15, 0.012], 0.17, 0, 0.227, color=TRIM),
         At([0.04, 0.12, 0.03], 0.8, 0.07, 0.235, color="darkgrey"), At([0.04, 0.12, 0.03], 0.8, -0.07, 0.235, color="darkgrey"),
         At([0.24, 0.1, 0.012], 1.15, 0.25, 0.228, color="white")]
    p += louvres(0.3, 0.2, -0.3, 0.25, 0.228, 4, color="#4a5a4a")
    prop("Street Cabinet", "Utility", "A grey-green telecoms street cabinet.", p, collider=[C([1.06, 1.45, 0.5])], scatter=SQ,
         breakable=metal(250, count=12, size=(0.08, 0.28), colors=[g, "#4a5a4a"]))


def utility_pedestal():
    g = "#3d5a3c"
    p = [At([0.34, 0.85, 0.3], 0, color=g, mat="metal"), At([0.38, 0.05, 0.34], 0.85, color="#2d4a2c"),
         At([0.2, 0.08, 0.012], 0.6, 0, 0.156, color="white"), At([0.04, 0.06, 0.02], 0.4, 0.1, 0.16, color=TRIM)]
    prop("Utility Pedestal", "Utility", "A small green cable pedestal by the kerb.", p, collider=[C([0.36, 0.9, 0.32])], scatter=SQ,
         breakable=metal(140, count=8, colors=[g]))


def manhole():
    p = Cyl(0.9, 0.012, 0.0, color="#3a3836") + Cyl(0.76, 0.02, 0.0, color="#4a4846", mat="metal")
    for k in range(5):
        p.append(At([0.62 - abs(k - 2) * 0.12, 0.024, 0.04], 0.0, 0, -0.24 + k * 0.12, color="#3a3836"))
    p += [At([0.04, 0.026, 0.5], 0.0, color="#3a3836")]
    prop("Manhole Cover", "Utility", "A cast-iron manhole cover set in the road.", p, collision="cosmetic", scatter=ANY, fade=45)


def storm_drain():
    p = [At([1.0, 0.012, 0.5], 0, color="#3a3836"), At([0.9, 0.02, 0.4], 0.0, color="#4a4846", mat="metal")]
    for k in range(9):
        p.append(At([0.04, 0.024, 0.4], 0.0, -0.4 + k * 0.1, color="#1a1918"))
    prop("Storm Drain", "Utility", "A cast-iron storm drain grate (long along X: line it up with a kerb).", p, collision="cosmetic",
         scatter={"randomYaw": False, "yawStep": 90}, fade=45)


def gas_meter():
    y = "#e3c21a"
    p = [At([0.36, 0.3, 0.22], 0.55, color="#c8c8c4"), At([0.16, 0.08, 0.012], 0.72, 0, 0.112, color=TRIM),
         At([0.04, 0.55, 0.04], 0.0, -0.12, color=y), At([0.04, 0.55, 0.04], 0.0, 0.12, color=y),
         At([0.1, 0.06, 0.1], 0.35, -0.12, color=y), At([0.2, 0.03, 0.03], 0.45, -0.12, 0.06, color="#c4281c"),
         At([0.04, 0.3, 0.04], 0.85, 0.12, color=y), At([0.2, 0.04, 0.04], 1.13, 0.2, color=y),
         At([0.5, 0.06, 0.4], 0.0, color="concrete", mat="concrete")]
    prop("Gas Meter", "Utility", "A gas meter on its yellow riser pipes.", p, collider=[C([0.4, 1.17, 0.3], [0, 0.585, 0])], scatter=SQ,
         breakable=metal(100, count=8, colors=["#c8c8c4", y]))


def electric_meter():
    p = [At([0.34, 0.44, 0.16], 1.2, color="#9a9c9e"), At([0.22, 0.22, 0.012], 1.32, 0, 0.082, color="#8ab0c0"),
         At([0.14, 0.04, 0.014], 1.4, 0, 0.083, color=TRIM), At([0.3, 0.1, 0.012], 1.22, 0, 0.082, color="#7a7c7e")]
    p += Cyl(0.05, 1.2, 0.0, 0, 0, color="#7a7c7e") + [At([0.42, 1.8, 0.03], 0.0, 0, -0.095, color="#6a6c6e")]
    prop("Electric Meter", "Utility", "An electric meter box on a wall board, conduit down to the ground.", p, collision="cosmetic", scatter=SQ0,
         breakable=metal(80, count=6, size=(0.04, 0.14), colors=["#9a9c9e", "#8ab0c0"]))


FE = "#2a2c2e"  # fire escape black iron


def fire_escape_stairs():
    H, run, w = 3.0, 3.6, 0.9
    n = 10
    p = [tube([-run / 2, 0.0, w / 2], [run / 2, H, w / 2], 0.06, FE), tube([-run / 2, 0.0, -w / 2], [run / 2, H, -w / 2], 0.06, FE),
         tube([-run / 2, 0.95, w / 2 + 0.03], [run / 2, H + 0.95, w / 2 + 0.03], 0.04, FE),
         tube([-run / 2, 0.95, -w / 2 - 0.03], [run / 2, H + 0.95, -w / 2 - 0.03], 0.04, FE)]
    for k in range(n):
        x = -run / 2 + (k + 0.5) * run / n
        y = (k + 0.5) * H / n
        p.append(At([run / n - 0.04, 0.03, w - 0.06], y - 0.015, x, color="#3a3c3e", mat="grid", tex=4))
    for x in (-run / 2 + 0.05, 0.0, run / 2 - 0.05):
        yb = (x + run / 2) / run * H
        for z in (w / 2 + 0.03, -w / 2 - 0.03):
            p.append(At([0.035, 0.95, 0.035], yb, x, z, color=FE))
    p += [At([0.06, H, 0.06], 0, run / 2, w / 2, color=FE), At([0.06, H, 0.06], 0, run / 2, -w / 2, color=FE)]
    col = []
    for k in range(8):
        x0 = -run / 2 + k * run / 8
        top = (k + 1) * H / 8
        col.append(C([run / 8, top, w], [x0 + run / 16, top / 2, 0]))
    prop("Fire Escape Stairs", "Utility", "A black iron fire escape stair flight, 3 m up along +X (pairs with Fire Escape Landing).", p,
         collider=col, scatter=SQ0)


def fire_escape_landing():
    H, L, D = 3.0, 2.4, 1.2
    p = [At([L, 0.05, D], H - 0.05, color="#3a3c3e", mat="grid", tex=4), At([L, 0.08, 0.06], H - 0.08, 0, D / 2, color=FE),
         At([L, 0.08, 0.06], H - 0.08, 0, -D / 2, color=FE)]
    for x in (-L / 2 + 0.05, L / 2 - 0.05):
        p.append(At([0.07, H - 0.05, 0.07], 0, x, D / 2 - 0.05, color=FE))
        p.append(tube([x, H - 0.9, -D / 2 + 0.05], [x, H - 0.08, D / 2 - 0.1], 0.05, FE))
        p.append(At([0.07, H - 0.05, 0.07], 0, x, -D / 2 + 0.05, color=FE))
    p += [At([L, 0.04, 0.04], H + 0.95, 0, D / 2 - 0.02, color=FE), At([L, 0.03, 0.03], H + 0.45, 0, D / 2 - 0.02, color=FE),
          At([0.04, 0.04, D], H + 0.95, -L / 2 + 0.02, color=FE), At([0.03, 0.03, D], H + 0.45, -L / 2 + 0.02, color=FE)]
    for k in range(7):
        p.append(At([0.03, 0.95, 0.03], H, -L / 2 + 0.02 + k * (L - 0.04) / 6, D / 2 - 0.02, color=FE))
    for k in range(1, 4):
        p.append(At([0.03, 0.95, 0.03], H, -L / 2 + 0.02, -D / 2 + k * D / 4, color=FE))
    prop("Fire Escape Landing", "Utility", "A fire escape landing 3 m up on iron posts, railed on the front and the -X side.", p,
         collider=[C([L, 0.1, D], [0, H - 0.05, 0]), C([0.08, H, 0.08], [-L / 2 + 0.05, H / 2, D / 2 - 0.05]),
                   C([0.08, H, 0.08], [L / 2 - 0.05, H / 2, D / 2 - 0.05]), C([0.08, H, 0.08], [-L / 2 + 0.05, H / 2, -D / 2 + 0.05]),
                   C([0.08, H, 0.08], [L / 2 - 0.05, H / 2, -D / 2 + 0.05]), C([L, 1.0, 0.06], [0, H + 0.5, D / 2 - 0.02]),
                   C([0.06, 1.0, D], [-L / 2 + 0.02, H + 0.5, 0])], scatter=SQ0)


def fire_escape_ladder():
    p = [At([0.05, 2.6, 0.05], 0, -0.22, color=FE), At([0.05, 2.6, 0.05], 0, 0.22, color=FE)]
    for k in range(9):
        p.append(At([0.44, 0.03, 0.03], 0.2 + k * 0.28, color=FE))
    p += [At([0.5, 0.04, 0.04], 2.6, color=FE), At([0.06, 0.06, 0.12], 2.3, -0.22, -0.06, color=FE), At([0.06, 0.06, 0.12], 2.3, 0.22, -0.06, color=FE)]
    prop("Fire Escape Ladder", "Utility", "The black iron drop ladder of a fire escape.", p, collision="cosmetic", scatter=SQ0)


def step_ladder():
    a = "#c8ccd0"
    p = [tube([-0.22, 0.0, 0.35], [-0.18, 1.5, 0.05], 0.05, a), tube([0.22, 0.0, 0.35], [0.18, 1.5, 0.05], 0.05, a),
         tube([-0.22, 0.0, -0.3], [-0.18, 1.45, 0.0], 0.04, a), tube([0.22, 0.0, -0.3], [0.18, 1.45, 0.0], 0.04, a),
         At([0.42, 0.04, 0.22], 1.5, 0, 0.0, color="#d0622a")]
    for k in range(4):
        t = (k + 1) / 5
        p.append(At([0.42, 0.03, 0.1], t * 1.5 - 0.015, 0, 0.35 - t * 0.3, color=a))
    p += [At([0.03, 0.02, 0.6], 0.6, -0.2, 0.02, color="#7a7c7e"), At([0.03, 0.02, 0.6], 0.6, 0.2, 0.02, color="#7a7c7e")]
    for x in (-0.22, 0.22):
        for z in (0.35, -0.3):
            p.append(At([0.07, 0.03, 0.07], 0, x, z, color="rubber"))
    prop("Aluminium Step Ladder", "Utility", "A folding aluminium step ladder with an orange top.", p, collision="cosmetic", scatter=ANY,
         breakable=metal(70, count=8, size=(0.04, 0.3), colors=[a, "#d0622a"]))


def extension_ladder():
    a = "#c8ccd0"
    p = [At([3.6, 0.06, 0.04], 0, 0, -0.2, color=a), At([3.6, 0.06, 0.04], 0, 0, 0.2, color=a),
         At([3.0, 0.05, 0.035], 0.06, 0.4, -0.16, color=a), At([3.0, 0.05, 0.035], 0.06, 0.4, 0.16, color=a)]
    for k in range(12):
        p.append(At([0.03, 0.03, 0.4], 0.015, -1.65 + k * 0.3, color="#a8acb0"))
    p += [At([0.08, 0.06, 0.08], 0, -1.8, 0.2, color="rubber"), At([0.08, 0.06, 0.08], 0, -1.8, -0.2, color="rubber")]
    prop("Extension Ladder", "Utility", "An aluminium extension ladder lying on the ground.", p, collision="cosmetic",
         scatter={"randomYaw": True}, breakable=metal(70, count=8, size=(0.04, 0.4), colors=[a]))


def scaffold_tower():
    W, D, lv = 2.4, 1.2, 2.0
    s = "#9aa0a6"
    p = []
    for x in (-W / 2, W / 2):
        for z in (-D / 2, D / 2):
            p += [At([0.05, 2 * lv + 1.0, 0.05], 0.05, x, z, color=s), At([0.16, 0.05, 0.16], 0, x, z, color="darkgrey")]
    for y in (0.3, lv, 2 * lv, lv + 1.0, 2 * lv + 1.0):
        p += [At([W, 0.045, 0.045], y, 0, D / 2, color=s), At([W, 0.045, 0.045], y, 0, -D / 2, color=s)]
        if y in (0.3, lv, 2 * lv):
            p += [At([0.045, 0.045, D], y, W / 2, color=s), At([0.045, 0.045, D], y, -W / 2, color=s)]
    p += [tube([-W / 2, 0.3, -D / 2], [W / 2, lv, -D / 2], 0.04, s), tube([W / 2, lv, -D / 2], [-W / 2, 2 * lv, -D / 2], 0.04, s),
          tube([W / 2, 0.3, -D / 2], [W / 2, lv, D / 2], 0.04, s)]
    for y in (lv, 2 * lv):
        for k in range(4):
            p.append(At([W - 0.1, 0.05, 0.27], y + 0.045, 0, -0.42 + k * 0.28, color="pallet", mat="wood"))
        p += [At([W - 0.1, 0.15, 0.03], y + 0.095, 0, D / 2 - 0.04, color="#c8a040"), At([W - 0.1, 0.15, 0.03], y + 0.095, 0, -D / 2 + 0.04, color="#c8a040")]
    prop("Scaffold Tower", "Utility", "A two-lift scaffold tower with plank decks at 2 m and 4 m. Climb it.", p,
         collider=[C([W + 0.1, 0.1, D + 0.1], [0, lv + 0.07, 0]), C([W + 0.1, 0.1, D + 0.1], [0, 2 * lv + 0.07, 0]),
                   C([0.08, 2 * lv + 1.05, 0.08], [-W / 2, lv + 0.525, -D / 2]), C([0.08, 2 * lv + 1.05, 0.08], [W / 2, lv + 0.525, -D / 2]),
                   C([0.08, 2 * lv + 1.05, 0.08], [-W / 2, lv + 0.525, D / 2]), C([0.08, 2 * lv + 1.05, 0.08], [W / 2, lv + 0.525, D / 2]),
                   C([W, 1.0, 0.06], [0, 2 * lv + 0.55, D / 2]), C([W, 1.0, 0.06], [0, 2 * lv + 0.55, -D / 2])], scatter=SQ0)


def scaffold_boards():
    p = []
    for k in range(7):
        y = k * 0.05
        p.append(At([3.0, 0.045, 0.23], y + 0.08, (k % 2) * 0.06 - 0.03, (k % 3 - 1) * 0.01, color="pallet" if k % 2 else "wood", mat="wood"))
        p.append(At([0.04, 0.046, 0.231], y + 0.08, 1.45 + (k % 2) * 0.06 - 0.03, color="#7a8a9a"))
    for x in (-1.0, 1.0):
        p.append(At([0.1, 0.08, 0.3], 0, x, color="darkwood", mat="wood"))
    prop("Scaffold Board Stack", "Utility", "A stack of scaffold boards on bearers (long along X).", p, collider=[C([3.1, 0.43, 0.3])],
         scatter={"randomYaw": True}, breakable=wood(90, count=14, size=(0.08, 0.6), colors=["pallet", "wood"]))


def portable_generator():
    y = "#e3a21a"
    p = [At([0.8, 0.04, 0.55], 0.02, color=TRIM), At([0.6, 0.35, 0.4], 0.1, -0.05, color="#3a3c3e"), At([0.5, 0.2, 0.42], 0.45, 0.0, color=y),
         At([0.16, 0.14, 0.012], 0.25, 0.25, 0.205, color="#5a5c5e"), At([0.05, 0.05, 0.02], 0.22, 0.24, 0.215, color="#c4281c")]
    for x in (-0.4, 0.4):
        for z in (-0.27, 0.27):
            p.append(At([0.04, 0.68, 0.04], 0.02, x, z, color=TRIM))
    p += [At([0.84, 0.04, 0.04], 0.7, 0, 0.27, color=TRIM), At([0.84, 0.04, 0.04], 0.7, 0, -0.27, color=TRIM)]
    p += wheel(0.2, 0.06, -0.4, 0.3) + wheel(0.2, 0.06, -0.4, -0.3)
    prop("Portable Generator", "Utility", "A yellow portable petrol generator in its frame.", p, collider=[C([0.86, 0.74, 0.62])], scatter=ANY,
         breakable=metal(160, count=10, colors=[y, "#3a3c3e"]))


def diesel_generator():
    y = "#e3a21a"
    p = [At([3.4, 0.25, 1.3], 0.3, color=TRIM), At([3.2, 1.5, 1.25], 0.55, color=y, mat="metal"), At([3.24, 0.06, 1.29], 2.05, color="#c48a14"),
         At([1.2, 0.012, 1.0], 1.6, 0.9, 0.0, color="#c48a14"), At([0.04, 1.3, 1.26], 0.65, 0.0, color="#c48a14"),
         At([0.04, 1.3, 1.26], 0.65, -1.0, color="#c48a14"), At([0.4, 0.3, 0.012], 1.1, -1.3, 0.632, color="#2a2c2e"),
         At([1.4, 0.06, 0.08], 0.38, 2.3, color=TRIM), At([0.12, 0.3, 0.12], 0.08, 2.9, color=TRIM)]
    p += louvres(0.9, 0.9, 1.0, 0.85, 0.632, 6, color="#c48a14") + Cyl(0.12, 0.5, 2.05, -1.2, 0.3, color="darkgrey")
    p += wheel(0.7, 0.2, 0, 0.75) + wheel(0.7, 0.2, 0, -0.75)
    prop("Diesel Generator", "Utility", "A big yellow trailer-mounted diesel generator. Solid cover, unbreakable.", p,
         collider=[C([3.4, 2.11, 1.36], [-0.05, 1.055, 0])], scatter=SQ)


def gas_valve():
    y = "#e3c21a"
    p = [At([1.0, 0.1, 0.6], 0, color="concrete", mat="concrete")]
    for x in (-0.3, 0.3):
        p += Cyl(0.16, 0.6, 0.1, x, 0, color=y) + [At([0.24, 0.04, 0.24], 0.4, x, color="#b49a14")]
    p += HCyl(0.16, 0.8, 0.8, 0, 0, color=y) + [At([0.24, 0.3, 0.24], 0.65, 0, color="#b49a14"), At([0.05, 0.25, 0.05], 0.95, 0, color="darkgrey")]
    p += [At([0.4, 0.04, 0.04], 1.2, color="#c4281c"), At([0.04, 0.04, 0.4], 1.2, color="#c4281c")]
    prop("Gas Valve", "Utility", "A yellow gas main valve standpipe with a red handwheel.", p, collider=[C([1.0, 1.0, 0.6])], scatter=SQ,
         breakable=metal(300, by=["bullets", "explosions", "streaks"], count=12, colors=[y, "#b49a14"]))


def floodlight_tower():
    y = "#e3a21a"
    p = [At([2.2, 0.9, 1.2], 0.35, color=y, mat="metal"), At([2.24, 0.05, 1.24], 1.25, color="#c48a14"),
         At([0.2, 5.5, 0.2], 1.3, -0.6, color="#9a9c9e"), At([0.14, 1.0, 0.14], 6.8, -0.6, color="#b4b6b6"),
         At([1.6, 0.08, 0.08], 7.8, -0.6, color="darkgrey")]
    for x in (-1.25, -0.85, -0.35, 0.05):
        p += [At([0.34, 0.3, 0.14], 7.9, x, 0.05, color=TRIM), At([0.28, 0.24, 0.015], 7.93, x, 0.125, color=LIT)]
    for x in (-1.0, 1.0):
        p += [At([0.08, 0.4, 0.08], 0, x, 0.65, color=TRIM), At([0.08, 0.4, 0.08], 0, x, -0.65, color=TRIM)]
    p += wheel(0.5, 0.16, 0.2, 0.7) + wheel(0.5, 0.16, 0.2, -0.7) + [At([1.0, 0.08, 0.08], 0.4, 1.5, color=TRIM)]
    prop("Floodlight Tower", "Utility", "A mobile floodlight tower: a trailer with a lamp bar 8 m up.", p,
         collider=[C([2.24, 1.3, 1.4]), C([0.22, 7.0, 0.22], [-0.6, 4.8, 0])], scatter=SQ)


def road_plate():
    p = [At([2.4, 0.025, 1.5], 0, color="#5a5c5e", mat="metal", tex=3), At([0.2, 0.03, 0.2], 0.0, 1.0, 0.6, color="#4a4c4e"),
         At([0.2, 0.03, 0.2], 0.0, -1.0, -0.6, color="#4a4c4e")]
    prop("Road Plate", "Utility", "A steel road plate covering a trench.", p, collision="cosmetic", scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 6}, fade=50)


# ================================================================== Signage ==================================================================

def post(h, d=0.08, color="#9a9c9e"):
    return [At([d, h, d], 0, color=color, mat="metal"), At([d + 0.06, 0.03, d + 0.06], 0, color="darkgrey")]


def traffic_head(x, y0, z, face=1):
    """A three-lamp traffic signal head facing +Z (face=1) or -Z."""
    p = [At([0.32, 0.92, 0.24], y0, x, z, color="#1e2022"), At([0.42, 1.02, 0.02], y0 - 0.05, x, z - face * 0.13, color=TRIM)]
    for i, c in enumerate(("#2fd05a", "#e8a020", "#e02a1a")):
        y = y0 + 0.16 + i * 0.3
        p += [At([0.2, 0.2, 0.02], y - 0.1, x, z + face * 0.125, color=c), At([0.26, 0.03, 0.14], y + 0.11, x, z + face * 0.19, color="#1e2022")]
    return p


def traffic_light():
    p = post(3.2, 0.12, "#2a2c2e") + traffic_head(0, 2.3, 0.18) + [At([0.12, 0.2, 0.14], 2.6, 0, 0.06, color="#2a2c2e"),
                                                                 At([0.14, 0.18, 0.1], 1.1, 0, 0.1, color="#e8c020")]
    prop("Traffic Light", "Signage", "A traffic signal on a post, facing +Z.", p, collider=[C([0.14, 3.2, 0.14])], scatter=SQ0)


def traffic_arm():
    p = post(5.6, 0.2, "#7a7c7e") + [tube([0.0, 5.3, 0], [4.3, 5.5, 0], 0.14, "#7a7c7e"), tube([0.0, 4.6, 0], [1.6, 5.35, 0], 0.07, "#7a7c7e")]
    p += traffic_head(2.4, 4.3, 0.15) + traffic_head(4.0, 4.3, 0.15)
    p += [At([0.06, 0.2, 0.06], 5.22, 2.4, 0.05, color="#2a2c2e"), At([0.06, 0.2, 0.06], 5.3, 4.0, 0.05, color="#2a2c2e"),
          At([0.9, 0.25, 0.03], 4.85, 3.2, 0.03, color="#2a7a3a"), At([0.7, 0.06, 0.035], 4.95, 3.2, 0.03, color="white")]
    prop("Traffic Light Arm", "Signage", "A mast-arm traffic signal: two heads hanging over the road (arm along +X, facing +Z).", p,
         collider=[C([0.22, 5.6, 0.22])], scatter=SQ0)


def ped_signal():
    p = post(2.8, 0.1, "#2a2c2e") + [At([0.32, 0.6, 0.22], 2.1, 0, 0.16, color="#1e2022"), At([0.22, 0.22, 0.02], 2.42, 0, 0.27, color="#e04a1a"),
                                     At([0.22, 0.22, 0.02], 2.15, 0, 0.27, color="#e8f0f0"), At([0.16, 0.24, 0.1], 1.0, 0, 0.1, color="#e8c020"),
                                     At([0.06, 0.06, 0.02], 1.15, 0, 0.155, color=TRIM)]
    prop("Pedestrian Signal", "Signage", "A crossing signal with a push button, facing +Z.", p, collider=[C([0.12, 2.8, 0.12])], scatter=SQ0)


def parking_meter():
    p = post(1.0, 0.07, "#4a4c4e") + [At([0.26, 0.36, 0.2], 1.0, color="#7a8088", mat="metal"), At([0.2, 0.12, 0.02], 1.17, 0, 0.1, color="#b0c8c0"),
                                      At([0.2, 0.06, 0.18], 1.36, color="#7a8088"), At([0.05, 0.03, 0.02], 1.08, 0, 0.105, color=TRIM),
                                      At([0.08, 0.06, 0.02], 1.06, 0.07, 0.105, color="#c4281c")]
    prop("Parking Meter", "Signage", "A coin parking meter on a short post.", p, collider=[C([0.28, 1.42, 0.22])], scatter=SQ,
         breakable=metal(120, count=8, colors=["#7a8088", "#4a4c4e"]))


def bus_stop_sign():
    p = post(2.7, 0.07) + [At([0.5, 0.5, 0.03], 2.1, 0.0, 0.05, color="#c4281c"), At([0.36, 0.14, 0.035], 2.28, 0.0, 0.05, color="white"),
                           At([0.36, 0.08, 0.035], 2.15, 0.0, 0.05, color="white"), At([0.36, 0.5, 0.08], 1.2, 0, 0.07, color="#d8d8d4"),
                           At([0.3, 0.42, 0.012], 1.24, 0, 0.11, color="#f0f0e8")]
    prop("Bus Stop Sign", "Signage", "A bus stop flag sign with a timetable case.", p, collider=[C([0.1, 2.7, 0.1])], scatter=SQ0,
         breakable=metal(100, count=8, colors=["#c4281c", "#9a9c9e"]))


def billboard():
    W, H, y0 = 6.0, 3.0, 3.0
    p = [At([0.3, y0 + H, 0.3], 0, -1.8, -0.1, color="#6a6c6e", mat="metal"), At([0.3, y0 + H, 0.3], 0, 1.8, -0.1, color="#6a6c6e", mat="metal"),
         At([0.6, 0.1, 0.6], 0, -1.8, -0.1, color="concrete"), At([0.6, 0.1, 0.6], 0, 1.8, -0.1, color="concrete"),
         At([W + 0.2, H + 0.2, 0.12], y0 - 0.1, 0, 0.1, color="#4a4c4e"), At([W, H, 0.02], y0, 0, 0.17, color="#f0e8d0"),
         At([W * 0.55, H, 0.025], y0, -W * 0.225, 0.172, color="#d8402a"), At([1.6, 1.6, 0.03], y0 + 0.7, -1.7, 0.175, color="#f0c020"),
         At([2.2, 0.35, 0.03], y0 + 2.2, 1.5, 0.175, color="#1e2a5a"), At([2.2, 0.18, 0.03], y0 + 1.6, 1.5, 0.175, color="#1e2a5a"),
         At([1.2, 0.18, 0.03], y0 + 1.25, 1.0, 0.175, color="#1e2a5a"), At([1.0, 0.5, 0.03], y0 + 0.4, 2.0, 0.175, color="#d8402a"),
         At([W + 0.2, 0.05, 0.7], y0 - 0.25, 0, 0.45, color="#4a4c4e", mat="grid", tex=4), At([W + 0.2, 0.04, 0.04], y0 + 0.2, 0, 0.78, color="#4a4c4e")]
    for x in (-2.2, 0, 2.2):
        p += [tube([x, y0 - 0.2, 0.75], [x, y0 - 0.5, 1.3], 0.05, "#4a4c4e"), At([0.4, 0.14, 0.2], y0 - 0.6, x, 1.35, color=TRIM)]
    prop("Billboard", "Signage", "A 6 x 3 m roadside billboard on two steel legs, with a catwalk and lamps. Unbreakable.", p,
         collider=[C([0.3, y0, 0.3], [-1.8, y0 / 2, -0.1]), C([0.3, y0, 0.3], [1.8, y0 / 2, -0.1]), C([W + 0.2, H + 0.2, 0.16], [0, y0 + H / 2, 0.1])],
         scatter=SQ0)


def sandwich_board():
    p = [B([0.6, 0.95, 0.03], [0, 0.46, 0.12], color="darkwood", pitch=-14), B([0.6, 0.95, 0.03], [0, 0.46, -0.12], color="darkwood", pitch=14),
         B([0.5, 0.75, 0.01], [0, 0.48, 0.137], color="#2a302c", pitch=-14), B([0.36, 0.05, 0.012], [0, 0.72, 0.135], color="white", pitch=-14),
         B([0.3, 0.04, 0.012], [0, 0.55, 0.155], color="#f0c020", pitch=-14), B([0.34, 0.04, 0.012], [0, 0.42, 0.185], color="white", pitch=-14),
         At([0.6, 0.06, 0.06], 0.9, color="darkwood")]
    prop("Sandwich Board", "Signage", "A wooden A-frame chalkboard sign outside a cafe.", p, collision="cosmetic", scatter=ANY,
         breakable=wood(40, count=8, size=(0.05, 0.25), colors=["darkwood", "#2a302c"]))


def shop_sign():
    p = [At([2.4, 0.6, 0.18], 0, color="#1e3a6a"), At([2.44, 0.04, 0.2], 0.58, color="#14284a"), At([2.44, 0.04, 0.2], 0.0, color="#14284a")]
    for i in range(7):
        p.append(At([0.2, 0.32 if i % 3 else 0.26, 0.02], 0.14, -0.9 + i * 0.3, 0.1, color=LIT))
    prop("Shop Sign", "Signage", "A lit box sign over a shop front (front faces +Z; mount it with the Prop op's y).", p, collision="cosmetic",
         scatter=SQ0, breakable=plastic(40, count=10, colors=["#1e3a6a", LIT]))


def hanging_sign():
    p = [At([0.12, 2.3, 0.12], 0, -0.35, color="darkwood", mat="wood"), At([0.3, 0.08, 0.3], 0, -0.35, color="#3a2a1a"),
         At([1.0, 0.06, 0.06], 2.1, 0.1, color=FE), tube([-0.3, 1.75, 0], [0.2, 2.1, 0], 0.04, FE),
         At([0.02, 0.1, 0.02], 2.0, -0.05, color=FE), At([0.02, 0.1, 0.02], 2.0, 0.45, color=FE),
         At([0.65, 0.5, 0.04], 1.5, 0.2, color="#7a2a20", mat="wood"), At([0.69, 0.04, 0.05], 1.98, 0.2, color="#d4a83a"),
         At([0.4, 0.2, 0.05], 1.65, 0.2, color="#d4a83a"), At([0.69, 0.04, 0.05], 1.47, 0.2, color="#d4a83a")]
    prop("Hanging Shop Sign", "Signage", "A painted wooden pub-style sign hanging from an iron bracket on a post.", p,
         collider=[C([0.14, 2.3, 0.14], [-0.35, 1.15, 0])], scatter=SQ0, breakable=wood(50, count=8, size=(0.05, 0.25), colors=["#7a2a20", "#d4a83a", "darkwood"]))


def neon_sign():
    p = [At([1.3, 0.55, 0.04], 0, color="#1a1a1e")]
    pink, blue = "#ff4fa8", "#4fc8ff"
    # O P E N in tubes
    p += [At([0.04, 0.3, 0.03], 0.12, -0.52, 0.035, color=pink), At([0.04, 0.3, 0.03], 0.12, -0.34, 0.035, color=pink),
          At([0.2, 0.04, 0.03], 0.12, -0.43, 0.035, color=pink), At([0.2, 0.04, 0.03], 0.38, -0.43, 0.035, color=pink),
          At([0.04, 0.3, 0.03], 0.12, -0.2, 0.035, color=pink), At([0.16, 0.04, 0.03], 0.38, -0.13, 0.035, color=pink),
          At([0.16, 0.04, 0.03], 0.25, -0.13, 0.035, color=pink), At([0.04, 0.13, 0.03], 0.25, -0.06, 0.035, color=pink),
          At([0.04, 0.3, 0.03], 0.12, 0.08, 0.035, color=pink), At([0.16, 0.04, 0.03], 0.38, 0.15, 0.035, color=pink),
          At([0.12, 0.04, 0.03], 0.25, 0.13, 0.035, color=pink), At([0.16, 0.04, 0.03], 0.12, 0.15, 0.035, color=pink),
          At([0.04, 0.3, 0.03], 0.12, 0.32, 0.035, color=pink), At([0.04, 0.3, 0.03], 0.12, 0.5, 0.035, color=pink),
          B([0.04, 0.34, 0.03], [0.41, 0.27, 0.035], color=pink, roll=30),
          At([1.2, 0.03, 0.03], 0.04, 0, 0.035, color=blue), At([1.2, 0.03, 0.03], 0.48, 0, 0.035, color=blue)]
    prop("Neon Sign", "Signage", "A pink and blue OPEN neon sign on a black board (front faces +Z). Glass: one shot.", p,
         collision="cosmetic", scatter=SQ0, breakable=glass(10, count=12, colors=[pink, blue, "#1a1a1e"]))


def plate_sign(name, desc, plate_parts, h=2.4, health=100):
    p = post(h, 0.07) + plate_parts
    prop(name, "Signage", desc, p, collider=[C([0.1, h, 0.1])], scatter=SQ0, breakable=metal(health, count=8, size=(0.05, 0.22)))


def speed_sign():
    y = 2.0
    plate_sign("Speed Limit Sign", "A white speed limit sign on a post.",
               [At([0.5, 0.65, 0.025], y, 0, 0.05, color="white"), At([0.46, 0.03, 0.03], y + 0.52, 0, 0.05, color=TRIM),
                At([0.32, 0.05, 0.03], y + 0.44, 0, 0.05, color=TRIM),
                # "3": three bars and an upright on its right
                At([0.12, 0.04, 0.03], y + 0.3, -0.1, 0.05, color=TRIM), At([0.1, 0.04, 0.03], y + 0.2, -0.09, 0.05, color=TRIM),
                At([0.12, 0.04, 0.03], y + 0.1, -0.1, 0.05, color=TRIM), At([0.05, 0.24, 0.03], y + 0.1, -0.035, 0.05, color=TRIM),
                # "0"
                At([0.05, 0.24, 0.03], y + 0.1, 0.05, 0.05, color=TRIM), At([0.05, 0.24, 0.03], y + 0.1, 0.15, 0.05, color=TRIM),
                At([0.12, 0.04, 0.03], y + 0.1, 0.1, 0.05, color=TRIM), At([0.12, 0.04, 0.03], y + 0.3, 0.1, 0.05, color=TRIM)], h=2.7)


def triangle(cx, cy, z, size, color, t, down=True):
    """An equilateral triangle outline (pointing down if down) from three bars, size across."""
    r = size / math.sqrt(3)
    pts = [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))) for a in ((90, 210, 330) if not down else (270, 30, 150))]
    out = []
    for i in range(3):
        a, b = pts[i], pts[(i + 1) % 3]
        out.append(tube([a[0], a[1], z], [b[0], b[1], z], t, color))
    return out


def yield_sign():
    cy = 2.15
    p = []
    for k, (s, c, zz) in enumerate(((0.9, "#c4281c", 0.05), (0.62, "white", 0.06), (0.4, "white", 0.06), (0.2, "white", 0.06))):
        p += triangle(0, cy, zz, s, c, 0.13 if k == 0 else 0.1)
    plate_sign("Yield Sign", "A red and white yield sign.", p, h=2.4)


def one_way_sign():
    y = 2.0
    plate_sign("One Way Sign", "A black and white one-way arrow sign.",
               [At([0.9, 0.3, 0.025], y, 0, 0.05, color=TRIM), At([0.55, 0.08, 0.03], y + 0.11, -0.08, 0.052, color="white"),
                B([0.2, 0.06, 0.03], [0.24, y + 0.19, 0.052], color="white", roll=-40), B([0.2, 0.06, 0.03], [0.24, y + 0.11, 0.052], color="white", roll=40)],
               h=2.4)


def no_parking_sign():
    y = 2.2
    p = [B([0.6, 0.248, 0.025], [0, y, 0.05], color="#c4281c", roll=a) for a in (0, 45, 90, 135)]
    p += [B([0.48, 0.2, 0.03], [0, y, 0.055], color="#1e4a9a", roll=a) for a in (0, 45, 90, 135)]
    p += [B([0.56, 0.07, 0.035], [0, y, 0.058], color="#c4281c", roll=45)]
    plate_sign("No Parking Sign", "A round blue no-parking sign with a red ring.", p, h=2.5)


def crossing_sign():
    y = 2.0
    p = [At([0.6, 0.6, 0.025], y, 0, 0.05, color="#1e4a9a")]
    p += triangle(0, y + 0.27, 0.06, 0.5, "white", 0.05, down=False)
    p += [At([0.06, 0.16, 0.03], y + 0.18, 0, 0.065, color=TRIM), At([0.06, 0.06, 0.03], y + 0.35, 0, 0.065, color=TRIM),
          B([0.04, 0.12, 0.03], [-0.04, y + 0.12, 0.065], color=TRIM, roll=-25), B([0.04, 0.12, 0.03], [0.04, y + 0.12, 0.065], color=TRIM, roll=25)]
    plate_sign("Pedestrian Crossing Sign", "A blue pedestrian crossing sign.", p, h=2.7)


def road_works_sign():
    o = "#e8701a"
    p = [B([0.06, 1.05, 0.04], [-0.3, 0.5, 0.15], color="#9a9c9e", pitch=-12), B([0.06, 1.05, 0.04], [0.3, 0.5, 0.15], color="#9a9c9e", pitch=-12),
         B([0.06, 1.05, 0.04], [-0.3, 0.5, -0.15], color="#9a9c9e", pitch=12), B([0.06, 1.05, 0.04], [0.3, 0.5, -0.15], color="#9a9c9e", pitch=12)]
    p += [B([0.6, 0.6, 0.02], [0, 0.72, 0.13], color=o, pitch=-12, roll=45),
          B([0.5, 0.5, 0.012], [0, 0.72, 0.145], color=TRIM, pitch=-12, roll=45), B([0.44, 0.44, 0.012], [0, 0.72, 0.152], color=o, pitch=-12, roll=45),
          B([0.06, 0.18, 0.012], [0, 0.72, 0.16], color=TRIM, pitch=-12), B([0.12, 0.05, 0.012], [0.02, 0.83, 0.135], color=TRIM, pitch=-12)]
    p += [At([0.3, 0.12, 0.8], 0.0, -0.3, color=TRIM), At([0.3, 0.12, 0.8], 0.0, 0.3, color=TRIM)]  # sandbag feet
    prop("Road Works Sign", "Signage", "An orange diamond road works sign on a folding stand.", p, collision="cosmetic", scatter=SQ,
         breakable=metal(80, count=8, size=(0.05, 0.25), colors=[o, "#9a9c9e"]))


def arrow_board():
    p = [At([1.8, 0.35, 1.3], 0.35, color="#e3a21a"), At([0.15, 2.0, 0.15], 0.7, -0.3, color="#4a4c4e"),
         At([2.4, 1.2, 0.12], 2.3, -0.3, color=TRIM), At([2.44, 0.05, 0.16], 3.5, -0.3, color="#e3a21a")]
    for k in range(5):
        p.append(At([0.16, 0.16, 0.02], 2.82, -1.2 + k * 0.32, 0.07, color=LIT))
    p += [At([0.16, 0.16, 0.02], 3.02, 0.7, 0.07, color=LIT), At([0.16, 0.16, 0.02], 2.62, 0.7, 0.07, color=LIT),
          At([0.16, 0.16, 0.02], 3.18, 0.5, 0.07, color=LIT), At([0.16, 0.16, 0.02], 2.46, 0.5, 0.07, color=LIT),
          At([1.0, 0.08, 0.08], 0.38, 1.4, color=TRIM)]
    p += wheel(0.55, 0.16, 0, 0.72) + wheel(0.55, 0.16, 0, -0.72)
    prop("Arrow Board", "Signage", "A road works trailer with a lit arrow board (pointing +X, facing +Z).", p,
         collider=[C([1.8, 0.75, 1.4]), C([2.44, 1.25, 0.2], [-0.3, 2.9, 0]), C([0.16, 1.6, 0.16], [-0.3, 1.5, 0])], scatter=SQ)


def direction_sign():
    W, H, y0 = 3.2, 1.6, 2.2
    p = [At([0.12, y0 + H, 0.12], 0, -1.1, color="#9a9c9e"), At([0.12, y0 + H, 0.12], 0, 1.1, color="#9a9c9e"),
         At([W, H, 0.05], y0, 0, 0.09, color="#1e6a3a"), At([W - 0.08, 0.03, 0.055], y0 + 0.04, 0, 0.09, color="white"),
         At([W - 0.08, 0.03, 0.055], y0 + H - 0.07, 0, 0.09, color="white"),
         At([1.6, 0.16, 0.06], y0 + 1.1, -0.4, 0.09, color="white"), At([1.2, 0.16, 0.06], y0 + 0.65, -0.6, 0.09, color="white"),
         At([0.5, 0.36, 0.06], y0 + 0.8, 1.05, 0.09, color="#1e4a9a"), At([0.3, 0.12, 0.065], y0 + 0.92, 1.05, 0.09, color="white"),
         tube([0.6, y0 + 0.3, 0.12], [0.6, y0 + 0.65, 0.12], 0.06, "white"), tube([0.6, y0 + 0.65, 0.12], [0.75, y0 + 0.52, 0.12], 0.05, "white"),
         tube([0.6, y0 + 0.65, 0.12], [0.45, y0 + 0.52, 0.12], 0.05, "white")]
    prop("Direction Sign", "Signage", "A big green road direction sign on two posts. Unbreakable.", p,
         collider=[C([0.14, y0, 0.14], [-1.1, y0 / 2, 0]), C([0.14, y0, 0.14], [1.1, y0 / 2, 0]), C([W, H, 0.14], [0, y0 + H / 2, 0.05])],
         scatter=SQ0)


def pylon_sign():
    p = [At([0.9, 0.3, 0.5], 0, color="concrete", mat="concrete"), At([0.4, 4.0, 0.3], 0.3, color="#6a6c6e"),
         At([1.6, 1.4, 0.4], 4.3, color="#c4281c"), At([1.4, 0.5, 0.42], 4.75, color="white"), At([0.9, 0.24, 0.44], 4.88, color="#c4281c"),
         At([1.6, 1.6, 0.36], 2.6, color=TRIM)]
    for k in range(3):
        p += [At([0.5, 0.36, 0.38], 2.72 + k * 0.48, -0.45, color="#ffffff"), At([0.7, 0.36, 0.38], 2.72 + k * 0.48, 0.38, color="#e04a1a")]
    prop("Pylon Sign", "Signage", "A tall petrol station price sign. Unbreakable.", p, collider=[C([1.6, 5.7, 0.44])], scatter=SQ0)


def street_clock():
    g = "#1e3a2a"
    p = Cyl(0.4, 0.3, 0.0, color=g) + Cyl(0.18, 3.0, 0.3, color=g, mat="metal") + Cyl(0.26, 0.1, 3.3, color="#d4a83a")
    p += [At([0.7, 0.7, 0.3], 3.4, color=g)]
    for z in (0.155, -0.155):
        p += [B([0.56, 0.232, 0.012], [0, 3.75, z], color="#f0ece0", roll=a) for a in (0, 45, 90, 135)]
        p += [At([0.03, 0.2, 0.014], 3.72, 0, z * 1.02, color=TRIM), B([0.03, 0.15, 0.014], [0.05, 3.72, z * 1.03], color=TRIM, roll=-60)]
    p += [At([0.3, 0.12, 0.3], 4.1, color="#d4a83a"), At([0.1, 0.12, 0.1], 4.22, color="#d4a83a")]
    prop("Street Clock", "Signage", "A green cast-iron post clock with two faces. Unbreakable.", p, collider=[C([0.4, 4.34, 0.4])], scatter=SQ0)


# ================================================================== Urban ==================================================================

def bus_shelter():
    W, D, H = 3.6, 1.5, 2.5
    fr = "#3a3c3e"
    p = []
    for x in (-W / 2, W / 2):
        for z in (-D / 2, D / 2 - 0.25):
            p.append(At([0.08, H, 0.08], 0, x, z, color=fr))
    p += [At([W + 0.3, 0.1, D + 0.2], H, 0, -0.1, color=fr), At([W + 0.2, 0.03, D + 0.1], H + 0.1, 0, -0.1, color="#8ab0c0"),
          At([W, 2.0, 0.03], 0.2, 0, -D / 2, color="#9ac0d0"), At([0.03, 2.0, D - 0.3], 0.2, -W / 2, -0.12, color="#9ac0d0"),
          At([0.12, 2.0, 1.0], 0.2, W / 2, -0.2, color="#2a2c2e"), At([0.02, 1.8, 0.9], 0.3, W / 2 + 0.065, -0.2, color="#f0e0b0"),
          At([0.02, 1.0, 0.6], 0.6, W / 2 + 0.07, -0.25, color="#d8402a"), At([W - 0.4, 0.05, 0.36], 0.45, 0, -D / 2 + 0.25, color="#9a9c9e"),
          At([0.05, 0.45, 0.05], 0, -1.0, -D / 2 + 0.25, color=fr), At([0.05, 0.45, 0.05], 0, 1.0, -D / 2 + 0.25, color=fr),
          At([1.2, 0.25, 0.04], H - 0.3, -0.8, D / 2 - 0.25, color="#c4281c")]
    prop("Bus Shelter", "Urban", "A glass bus shelter with a bench and an ad panel (open side faces +Z). Unbreakable.", p,
         collider=[C([W, 2.0, 0.06], [0, 1.2, -D / 2]), C([0.06, 2.0, D - 0.3], [-W / 2, 1.2, -0.12]), C([0.14, 2.0, 1.0], [W / 2, 1.2, -0.2]),
                   C([W + 0.3, 0.12, D + 0.2], [0, H + 0.06, -0.1]), C([W - 0.4, 0.5, 0.36], [0, 0.25, -D / 2 + 0.25])], scatter=SQ0)


def phone_booth():
    r = "#c41e1a"
    p = [At([1.0, 0.1, 1.0], 0, color=r), At([0.96, 2.2, 0.96], 0.1, color=r, mat="metal"), At([1.0, 0.16, 1.0], 2.3, color=r),
         At([0.8, 0.12, 0.8], 2.46, color=r), At([0.5, 0.08, 0.5], 2.58, color=r)]
    for (ax, s) in (("z", 1), ("z", -1), ("x", 1)):
        for col in range(3):
            for row in range(6):
                u = -0.25 + col * 0.25
                y = 0.55 + row * 0.25
                if ax == "z":
                    p.append(At([0.2, 0.2, 0.01], y, u, s * 0.481, color="#9ac0d0"))
                else:
                    p.append(At([0.01, 0.2, 0.2], y, s * 0.481, u, color="#9ac0d0"))
    p += [At([0.6, 0.12, 0.012], 2.33, 0, 0.502, color="white"), At([0.012, 0.12, 0.6], 2.33, 0.502, color="white"),
          At([0.05, 0.2, 0.04], 1.2, 0.36, 0.5, color=TRIM)]
    prop("Phone Booth", "Urban", "A red telephone box, glazed on three sides. Only explosions and strikes break it.", p,
         collider=[C([1.0, 2.62, 1.0])], scatter=SQ0,
         breakable=metal(400, by=["explosions", "streaks"], count=24, size=(0.06, 0.3), colors=[r, "#9ac0d0"]))


def payphone():
    p = post(1.1, 0.1, "#5a5c5e") + [At([0.5, 0.8, 0.4], 1.1, 0, 0.0, color="#7a7c7e", mat="metal"), At([0.3, 0.45, 0.2], 1.15, 0, 0.12, color="#3a3c3e"),
                                     At([0.07, 0.24, 0.06], 1.3, -0.08, 0.25, color=TRIM), At([0.12, 0.14, 0.02], 1.42, 0.06, 0.225, color="#a8a8a0"),
                                     At([0.46, 0.1, 0.012], 1.75, 0, 0.205, color="#2a6ab0")]
    prop("Payphone", "Urban", "A payphone in a steel hood on a post.", p, collider=[C([0.5, 1.9, 0.42])], scatter=SQ0,
         breakable=metal(150, count=10, colors=["#7a7c7e", "#3a3c3e"]))


def newsstand():
    g = "#2e5a3a"
    p = [At([2.4, 1.4, 1.6], 0, color=g, mat="wood"), At([2.5, 0.08, 1.7], 1.4, color="#1e4a2a"), At([2.0, 0.6, 0.03], 1.48, 0, -0.78, color=g),
         At([2.6, 0.06, 0.8], 2.1, 0, -0.1, color="#1e4a2a"), At([2.4, 0.5, 0.05], 1.48, 0, 0.8, color="#f0e8d0"),
         B([2.6, 0.04, 0.9], [0, 2.0, 1.1], color="#c4281c", pitch=18), B([2.6, 0.16, 0.02], [0, 1.84, 1.52], color="#c4281c")]
    for k in range(6):
        p.append(B([0.32, 0.4, 0.02], [-1.0 + k * 0.4, 0.98, 0.82], color=["#d8402a", "#f0c020", "#2a6ab0", "white", "#d02a7a", "#f0c020"][k], pitch=-12))
    for k in range(4):
        p.append(At([0.5, 0.06, 0.32], 0.5 + (k % 2) * 0.06, -0.8 + k * 0.5, 0.85, color="newsprint"))
    p += [At([2.4, 0.3, 0.36], 0.0, 0, 0.98, color="#1e4a2a")]
    for x in (-1.2, 1.2):
        p.append(At([0.05, 2.0, 0.05], 0.0, x, 1.15, color=TRIM))
    prop("Newsstand", "Urban", "A green wooden newspaper kiosk with magazines and a striped awning (front +Z).", p,
         collider=[C([2.5, 2.1, 1.7], [0, 1.05, 0]), C([2.4, 0.36, 0.4], [0, 0.18, 1.0])], scatter=SQ0,
         breakable=wood(140, count=20, size=(0.08, 0.5), colors=[g, "newsprint", "#c4281c"]))


def newspaper_box():
    b = "#2a6ab0"
    p = [At([0.45, 0.06, 0.4], 0, color="darkgrey"), At([0.05, 0.3, 0.05], 0.06, 0.15, 0.13, color="darkgrey"), At([0.05, 0.3, 0.05], 0.06, -0.15, 0.13, color="darkgrey"),
         At([0.05, 0.3, 0.05], 0.06, 0.15, -0.13, color="darkgrey"), At([0.05, 0.3, 0.05], 0.06, -0.15, -0.13, color="darkgrey"),
         At([0.45, 0.65, 0.4], 0.36, color=b, mat="metal"), At([0.47, 0.06, 0.42], 1.01, color="#1e4a8a"),
         At([0.34, 0.3, 0.012], 0.6, 0, 0.2, color="#c8d8e0"), At([0.28, 0.2, 0.014], 0.64, 0, 0.2, color="newsprint"),
         At([0.1, 0.06, 0.02], 0.94, 0.12, 0.205, color="#9a9c9e"), At([0.3, 0.05, 0.03], 0.5, 0, 0.21, color="#1e4a8a")]
    prop("Newspaper Box", "Urban", "A blue coin-operated newspaper box.", p, collider=[C([0.47, 1.07, 0.42])], scatter=SQ,
         breakable=metal(110, count=10, colors=[b, "newsprint"]))


def atm():
    p = [At([0.75, 1.6, 0.7], 0, color="#5a6068", mat="metal"), At([0.79, 0.25, 0.8], 1.6, 0, 0.04, color="#2a3a6a"),
         At([0.6, 0.16, 0.012], 1.64, 0, 0.445, color=LIT), B([0.64, 0.5, 0.2], [0, 1.25, 0.38], color="#3a4048", pitch=-20),
         B([0.4, 0.3, 0.012], [0, 1.28, 0.475], color="#4ab0d8", pitch=-20), At([0.62, 0.05, 0.25], 0.95, 0, 0.45, color="#3a4048"),
         At([0.24, 0.02, 0.16], 1.0, 0.0, 0.47, color="#9a9c9e"), At([0.2, 0.04, 0.02], 0.85, 0, 0.36, color=TRIM),
         At([0.1, 0.03, 0.02], 1.12, 0.22, 0.43, color=TRIM)]
    prop("ATM", "Urban", "A freestanding cash machine. Heavy steel: only explosions and strikes break it.", p,
         collider=[C([0.79, 1.85, 0.8], [0, 0.925, 0.04])], scatter=SQ,
         breakable=metal(400, by=["explosions", "streaks"], count=18, size=(0.06, 0.3), colors=["#5a6068", "#2a3a6a"]))


def bike_rack():
    p = []
    for x in (-0.9, -0.3, 0.3, 0.9):
        p += [At([0.05, 0.75, 0.05], 0, x, -0.3, color="#9a9c9e"), At([0.05, 0.75, 0.05], 0, x, 0.3, color="#9a9c9e"),
              At([0.05, 0.05, 0.65], 0.75, x, color="#9a9c9e")]
    p += [At([2.0, 0.04, 0.12], 0, color="darkgrey")]
    prop("Bike Rack", "Urban", "A row of four steel hoop bike stands (along X).", p,
         collider=[C([0.06, 0.8, 0.66], [x, 0.4, 0]) for x in (-0.9, -0.3, 0.3, 0.9)], scatter={"randomYaw": False, "yawStep": 90})


def picnic_table():
    p = []
    for z in (-0.18, -0.06, 0.06, 0.18):
        p.append(At([1.8, 0.05, 0.11], 0.72, 0, z, color="wood", mat="wood"))
    for s in (1, -1):
        for z in (0.48, 0.6):
            p.append(At([1.8, 0.04, 0.11], 0.42, 0, s * z, color="wood", mat="wood"))
    for x in (-0.65, 0.65):
        p += [B([0.06, 0.85, 0.08], [x, 0.37, 0.18], color="darkwood", pitch=28), B([0.06, 0.85, 0.08], [x, 0.37, -0.18], color="darkwood", pitch=-28),
              At([0.06, 0.06, 1.4], 0.36, x, color="darkwood"), At([0.06, 0.05, 0.5], 0.67, x, color="darkwood")]
    prop("Park Picnic Table", "Urban", "A wooden park picnic table with bench seats.", p,
         collider=[C([1.8, 0.77, 0.5], [0, 0.385, 0]), C([1.8, 0.44, 0.28], [0, 0.22, 0.54]), C([1.8, 0.44, 0.28], [0, 0.22, -0.54])],
         scatter={"randomYaw": True}, breakable=wood(100, count=16, size=(0.06, 0.5), colors=["wood", "darkwood"]))


def hot_dog_cart():
    p = [At([1.4, 0.7, 0.7], 0.3, color="#e6e4dc", mat="metal"), At([1.44, 0.04, 0.74], 1.0, color="chrome"),
         At([0.5, 0.12, 0.4], 1.04, -0.3, color="chrome"), At([0.3, 0.1, 0.3], 1.04, 0.35, color="#c4281c"),
         At([1.4, 0.18, 0.012], 0.75, 0, 0.356, color="#c4281c"), At([0.7, 0.1, 0.014], 0.79, 0, 0.358, color="#f0c020"),
         At([0.04, 1.2, 0.04], 1.04, 0.0, 0.0, color="chrome"), At([0.6, 0.1, 0.04], 0.9, 0.95, color="chrome")]
    for k, a in enumerate((0, 45, 90, 135)):  # umbrella: each panel a few mm higher so the overlaps don't flicker
        p.append(B([1.6, 0.06, 0.66], [0, 2.26 + k * 0.006, 0], color="#c4281c" if a % 90 == 0 else "#f0c020", yaw=a))
    p += [At([0.08, 0.08, 0.08], 2.29, color="white"), At([0.06, 0.3, 0.06], 0.0, 0.6, color="darkgrey")]
    p += wheel(0.5, 0.08, -0.4, 0.4, hub="lightgrey") + wheel(0.5, 0.08, -0.4, -0.4, hub="lightgrey")
    prop("Hot Dog Cart", "Urban", "A street food cart with a red and yellow umbrella.", p, collider=[C([1.5, 1.1, 0.8])], scatter=ANY,
         breakable=metal(150, count=14, size=(0.06, 0.3), colors=["#e6e4dc", "#c4281c", "#f0c020"]))


def ticket_machine():
    p = [At([0.5, 1.5, 0.38], 0, color="#2a5a8a", mat="metal"), At([0.54, 0.12, 0.42], 1.5, color="#1e4a7a"),
         At([0.3, 0.22, 0.012], 1.15, 0, 0.196, color="#4ab0d8"), At([0.3, 0.2, 0.012], 0.85, 0, 0.196, color="#3a3c3e"),
         At([0.12, 0.04, 0.02], 0.7, 0, 0.2, color=TRIM), At([0.2, 0.06, 0.012], 0.4, 0, 0.196, color=TRIM),
         At([0.4, 0.08, 0.012], 1.38, 0, 0.196, color="#f0c020"), At([0.6, 0.05, 0.48], 0, color="darkgrey")]
    prop("Ticket Machine", "Urban", "A parking pay-and-display ticket machine.", p, collider=[C([0.54, 1.62, 0.48])], scatter=SQ,
         breakable=metal(300, count=12, colors=["#2a5a8a", "#3a3c3e"]))


def post_box():
    r = "#c41e1a"
    p = Cyl(0.5, 0.12, 0.0, color=TRIM) + Cyl(0.44, 1.2, 0.12, color=r, mat="metal") + Cyl(0.5, 0.08, 1.32, color=r) + Cyl(0.36, 0.1, 1.4, color=r)
    p += [At([0.26, 0.04, 0.06], 1.12, 0, 0.21, color=TRIM), At([0.14, 0.18, 0.012], 0.75, 0, 0.222, color="white"),
          At([0.1, 0.1, 0.014], 0.5, 0, 0.22, color="#d4a83a")]
    prop("Post Box", "Urban", "A red cast-iron pillar post box. Unbreakable.", p, collider=[C([0.5, 1.5, 0.5])], scatter=SQ0)


def drinking_fountain():
    p = [At([0.4, 0.85, 0.4], 0, color="concrete", mat="concrete"), At([0.6, 0.12, 0.6], 0.85, color="#8a8a86", mat="concrete"),
         At([0.44, 0.03, 0.44], 0.96, color="#5a6a70"), At([0.05, 0.1, 0.05], 0.97, 0.12, color="chrome"),
         At([0.08, 0.05, 0.04], 0.75, 0, 0.21, color="chrome"), At([0.5, 0.06, 0.5], 0, color="#8a8a86")]
    prop("Drinking Fountain", "Urban", "A concrete park drinking fountain. Unbreakable.", p, collider=[C([0.6, 0.99, 0.6])], scatter=ANY)


def cigarette_bin():
    p = post(0.6, 0.06, "#3a3c3e") + Cyl(0.24, 0.45, 0.6, color="#3a3c3e", mat="metal") + Cyl(0.26, 0.04, 1.05, color="#9a9c9e")
    p += [At([0.12, 0.04, 0.04], 0.95, 0, 0.11, color=TRIM)]
    prop("Cigarette Bin", "Urban", "A steel cigarette butt bin on a post.", p, collider=[C([0.26, 1.09, 0.26])], scatter=SQ,
         breakable=metal(80, count=8, size=(0.04, 0.16), colors=["#3a3c3e", "#9a9c9e"]))


def litter_bin_post():
    g = "#1e4a2a"
    p = post(1.0, 0.08, g) + [At([0.42, 0.55, 0.32], 0.35, 0, 0.18, color=g, mat="metal"), At([0.44, 0.04, 0.34], 0.9, 0, 0.18, color="#d4a83a"),
                              At([0.3, 0.1, 0.012], 0.75, 0, 0.342, color=TRIM), At([0.16, 0.12, 0.012], 0.5, 0, 0.342, color="#d4a83a")]
    prop("Post Litter Bin", "Urban", "A green litter bin bolted to a post.", p, collider=[C([0.44, 1.0, 0.44], [0, 0.5, 0.1])], scatter=SQ,
         breakable=metal(90, count=8, colors=[g, "#d4a83a"]))


def heras_fence():
    L, H = 3.4, 2.0
    s = "#b4b8bc"
    p = [At([0.6, 0.15, 0.22], 0, -L / 2, color="#3a3c3e"), At([0.6, 0.15, 0.22], 0, L / 2, color="#3a3c3e"),
         At([0.05, H, 0.05], 0.1, -L / 2 + 0.03, color=s), At([0.05, H, 0.05], 0.1, L / 2 - 0.03, color=s),
         At([L, 0.05, 0.05], H + 0.05, 0, color=s), At([L, 0.05, 0.05], 0.15, 0, color=s)]
    for k in range(1, 14):
        p.append(At([0.012, H - 0.1, 0.012], 0.18, -L / 2 + k * L / 14, color="#9a9ea2"))
    for y in (0.6, 1.1, 1.6):
        p.append(At([L - 0.06, 0.012, 0.012], y, 0, color="#9a9ea2"))
    p += [At([0.08, 0.12, 0.12], 1.5, L / 2, color="#3a3c3e")]
    prop("Construction Fence Panel", "Urban", "A 3.4 m temporary mesh fence panel on rubber feet (along X).", p,
         collider=[C([L, H + 0.1, 0.08], [0, (H + 0.1) / 2, 0]), C([0.6, 0.15, 0.22], [-L / 2, 0.075, 0]), C([0.6, 0.15, 0.22], [L / 2, 0.075, 0])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 6}, breakable=metal(100, count=10, size=(0.05, 0.4), colors=[s, "#3a3c3e"]))


def chain_link():
    L, H = 3.0, 2.2
    s = "#9aa0a4"
    p = Cyl(0.08, H, 0.0, -L / 2, 0, color=s) + Cyl(0.08, H, 0.0, L / 2, 0, color=s) + [At([L, 0.05, 0.05], H - 0.05, 0, color=s)]
    for k in range(1, 15):
        p.append(At([0.008, H - 0.1, 0.008], 0.05, -L / 2 + k * L / 15, color="#8a9094"))
    for k in range(1, 11):
        p.append(At([L, 0.008, 0.008], k * (H - 0.1) / 11, 0, color="#8a9094"))
    p += [At([L, 0.012, 0.012], 0.05, 0, color=s)]
    prop("Chain Link Fence", "Urban", "A 3 m chain link fence section between two posts (along X). Unbreakable.", p,
         collider=[C([L + 0.08, H, 0.08])], scatter={"randomYaw": False, "yawStep": 90})


def crowd_barrier():
    L = 2.3
    s = "#b4b8bc"
    p = [At([L, 0.05, 0.05], 1.05, color=s), At([L, 0.05, 0.05], 0.2, color=s), At([0.05, 1.0, 0.05], 0.1, -L / 2, color=s),
         At([0.05, 1.0, 0.05], 0.1, L / 2, color=s)]
    for k in range(1, 14):
        p.append(At([0.02, 0.85, 0.02], 0.22, -L / 2 + k * L / 14, color=s))
    for x in (-L / 2 + 0.1, L / 2 - 0.1):
        p += [tube([x, 0.12, 0.0], [x, 0.02, 0.35], 0.04, s), tube([x, 0.12, 0.0], [x, 0.02, -0.35], 0.04, s), At([0.06, 0.1, 0.06], 0.02, x, color=s)]
    prop("Crowd Barrier", "Urban", "A steel pedestrian crowd barrier (along X).", p, collider=[C([L + 0.05, 1.1, 0.1])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8}, breakable=metal(150, count=10, size=(0.04, 0.4), colors=[s]))


def concrete_block():
    p = [At([1.6, 0.8, 0.8], 0, color="concrete", mat="concrete")]
    for x in (-0.4, 0.4):
        p += Cyl(0.3, 0.12, 0.8, x, 0, color="#8a8986", mat="concrete")
    p += [At([1.0, 0.04, 0.04], 0.6, 0, 0.42, color="#7a7976")]
    prop("Concrete Block", "Urban", "An interlocking concrete block with two studs on top. Unbreakable cover.", p,
         collider=[C([1.6, 0.8, 0.8])], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 5})


def water_barrier():
    p = [At([1.8, 0.2, 0.55], 0, color="#c4281c"), At([1.8, 0.35, 0.42], 0.2, color="#c4281c"), At([1.8, 0.3, 0.3], 0.55, color="white"),
         At([1.8, 0.12, 0.22], 0.85, color="#c4281c"), At([0.12, 0.1, 0.12], 0.97, -0.6, color="#c4281c"), At([0.12, 0.1, 0.12], 0.97, 0.6, color="#c4281c"),
         At([0.2, 0.18, 0.56], 0.05, -0.9, color="#a41c14"), At([0.2, 0.18, 0.56], 0.05, 0.9, color="#a41c14")]
    prop("Water Barrier", "Urban", "A red and white plastic water-filled road barrier (along X).", p, collider=[C([1.9, 0.97, 0.56])],
         scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 4}, breakable=plastic(60, count=12, size=(0.08, 0.3), colors=["#c4281c", "white"]))


def speed_bump():
    p = [At([3.6, 0.04, 0.5], 0, color=TRIM), At([3.6, 0.03, 0.34], 0.04, color=TRIM)]
    for k in range(6):
        p.append(At([0.3, 0.072, 0.36], 0.0, -1.5 + k * 0.6, color="#e8c020"))
    prop("Speed Bump", "Urban", "A rubber speed bump with yellow stripes (across X). Walk straight over it.", p, collider=[C([3.6, 0.07, 0.5])],
         scatter={"randomYaw": False, "yawStep": 90})


def boom_barrier():
    p = [At([0.4, 1.0, 0.4], 0, color="#e3c21a", mat="metal"), At([0.44, 0.06, 0.44], 1.0, color="#3a3c3e"),
         At([0.22, 0.22, 0.2], 0.78, 0.25, color="#3a3c3e")]
    for k in range(8):
        p.append(At([0.48, 0.1, 0.07], 0.84, 0.6 + k * 0.48, color="#c4281c" if k % 2 == 0 else "white"))
    p += [At([0.08, 0.84, 0.08], 0, 4.1, color="#3a3c3e"), At([0.18, 0.06, 0.12], 0.8, 4.1, color="#3a3c3e")]
    prop("Boom Barrier", "Urban", "A car park boom gate: a red and white arm 4 m along +X from its post.", p,
         collider=[C([0.44, 1.06, 0.44]), C([4.0, 0.12, 0.08], [2.42, 0.89, 0])], scatter=SQ0,
         breakable=metal(150, count=12, size=(0.05, 0.4), colors=["#c4281c", "white", "#e3c21a"]))


def portable_toilet():
    b = "#2a6ab0"
    p = [At([1.15, 0.1, 1.15], 0, color="#1e4a8a"), At([1.1, 2.1, 1.1], 0.1, color=b), At([1.16, 0.08, 1.16], 2.2, color="white"),
         At([0.9, 0.06, 0.9], 2.28, color="white"), At([0.85, 1.9, 0.02], 0.2, 0, 0.56, color="#3a7ac0"), At([0.04, 0.16, 0.04], 1.1, 0.33, 0.58, color="#9a9c9e"),
         At([0.16, 0.06, 0.012], 1.3, 0.33, 0.571, color="#c4281c"), At([0.4, 0.06, 0.02], 1.9, 0, 0.571, color="#1e4a8a")]
    for x in (-0.42, 0.42):
        p.append(At([0.04, 2.1, 0.04], 0.1, x, 0.55, color="#1e4a8a"))
    prop("Portable Toilet", "Urban", "A blue plastic portable toilet (door faces +Z).", p, collider=[C([1.16, 2.34, 1.16])], scatter=SQ,
         breakable=plastic(60, count=16, size=(0.1, 0.4), colors=[b, "white"]))


def builders_skip():
    y = "#e3b81a"
    p = [B([3.4, 0.08, 1.6], [0, 0.04, 0], color=y)]
    p += [At([2.6, 0.04, 1.5], 0.08, color=y, mat="metal")]
    p += [B([0.08, 1.15, 1.6], [1.52, 0.62, 0], color=y, roll=-18, mat="metal"), B([0.08, 1.15, 1.6], [-1.52, 0.62, 0], color=y, roll=18, mat="metal"),
          At([3.3, 1.05, 0.08], 0.1, 0, 0.78, color=y, mat="metal"), At([3.3, 1.05, 0.08], 0.1, 0, -0.78, color=y, mat="metal"),
          At([3.8, 0.08, 0.12], 1.1, 0, 0.79, color="#c49a14"), At([3.8, 0.08, 0.12], 1.1, 0, -0.79, color="#c49a14"),
          At([0.12, 0.25, 0.2], 0.85, 1.35, 0.88, color="#c49a14"), At([0.12, 0.25, 0.2], 0.85, -1.35, 0.88, color="#c49a14"),
          At([0.12, 0.25, 0.2], 0.85, 1.35, -0.88, color="#c49a14"), At([0.12, 0.25, 0.2], 0.85, -1.35, -0.88, color="#c49a14"),
          At([2.4, 0.4, 1.3], 0.12, -0.2, color="#7a6a5a", mat="dirt"), At([0.8, 0.3, 0.6], 0.5, 0.6, 0.2, color="concrete", mat="concrete"),
          At([1.2, 0.08, 0.2], 0.55, -0.6, -0.3, color="pallet", yaw=20)]
    prop("Builders Skip", "Urban", "A yellow builder's skip full of rubble. Solid cover, unbreakable.", p, collider=[C([3.9, 1.2, 1.72])], scatter=SQ)


def market_stall():
    p = []
    for x in (-1.2, 1.2):
        for z in (-0.7, 0.7):
            p.append(At([0.06, 2.2 if z < 0 else 2.0, 0.06], 0, x, z, color="darkwood"))
    p += [At([2.5, 0.06, 1.3], 0.85, color="wood", mat="wood"), At([2.5, 0.8, 0.04], 0.05, 0, 0.62, color="#c8b088"),
          B([2.7, 0.03, 1.7], [0, 2.18, 0], color="white", pitch=6)]
    for k in range(5):
        p.append(B([0.27, 0.035, 1.7], [-1.08 + k * 0.54, 2.19, 0], color="#2e7d4a", pitch=6))
    for k, c in enumerate(("#c4281c", "#e88a1a", "#f0c020", "#3a8a3a")):
        p += [At([0.5, 0.12, 0.35], 0.91, -0.9 + k * 0.6, 0.15, color="#a8885a"), At([0.44, 0.08, 0.3], 1.03, -0.9 + k * 0.6, 0.15, color=c, mat="dirt")]
    prop("Market Stall", "Urban", "A wooden market stall with a green striped canopy and crates of produce.", p,
         collider=[C([2.5, 0.91, 1.3], [0, 0.455, 0])], scatter=SQ, breakable=wood(120, count=20, size=(0.06, 0.5), colors=["wood", "#2e7d4a", "white"]))


ALL = [hatchback, sedan, taxi, police_car, suv, pickup, van, ambulance, burnt_car, car_on_bricks, overturned_car, bus, box_truck, truck_cab,
       forklift, golf_cart, motorbike, moped, bicycle, kick_scooter, trailer, jerry_can, engine_block, car_door, car_wheel,
       window_ac, condenser, split_ac, hvac, mushroom_vent, turbine_vent, air_duct, duct_elbow, duct_riser, round_duct, exhaust_fan,
       brick_chimney, metal_flue, water_tower, poly_tank, satellite_dish, tv_antenna, radio_mast, solar_panel, skylight, roof_hatch,
       vent_grille, pipe_rack,
       utility_pole, pole_transformer, pad_transformer, junction_box, street_cabinet, utility_pedestal, manhole, storm_drain, gas_meter,
       electric_meter, fire_escape_stairs, fire_escape_landing, fire_escape_ladder, step_ladder, extension_ladder, scaffold_tower,
       scaffold_boards, portable_generator, diesel_generator, gas_valve, floodlight_tower, road_plate,
       traffic_light, traffic_arm, ped_signal, parking_meter, bus_stop_sign, billboard, sandwich_board, shop_sign, hanging_sign, neon_sign,
       speed_sign, yield_sign, one_way_sign, no_parking_sign, crossing_sign, road_works_sign, arrow_board, direction_sign, pylon_sign, street_clock,
       bus_shelter, phone_booth, payphone, newsstand, newspaper_box, atm, bike_rack, picnic_table, hot_dog_cart, ticket_machine, post_box,
       drinking_fountain, cigarette_bin, litter_bin_post, heras_fence, chain_link, crowd_barrier, concrete_block, water_barrier, speed_bump,
       boom_barrier, portable_toilet, builders_skip, market_stall]

CATEGORIES = ["Vehicles", "Rooftop", "Utility", "Signage", "Urban"]


# ================================================================== Output ==================================================================

def rot(pitch, yaw, roll):
    p, y, r = map(math.radians, (pitch, yaw, roll))
    Ry = [[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]]
    Rx = [[1, 0, 0], [0, math.cos(p), -math.sin(p)], [0, math.sin(p), math.cos(p)]]
    Rz = [[math.cos(r), -math.sin(r), 0], [math.sin(r), math.cos(r), 0], [0, 0, 1]]

    def mm(a, b):
        return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mm(mm(Ry, Rx), Rz)


def bounds(p):
    lo = [1e9] * 3
    hi = [-1e9] * 3
    for part in p["parts"]:
        s = [v / 2 for v in part["size"]]
        o = part.get("offset", [0, part["size"][1] / 2, 0])
        R = rot(*part.get("rotation", [0, 0, 0]))
        for i in range(3):
            ext = sum(abs(R[i][k]) * s[k] for k in range(3))
            lo[i] = min(lo[i], o[i] - ext)
            hi[i] = max(hi[i], o[i] + ext)
    return lo, hi


def write_prop(p, out):
    folder = os.path.join(out, p["category"])
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


CAT_COLORS = {"Vehicles": "#3b5ea8", "Rooftop": "#a8763b", "Utility": "#3ba85e", "Signage": "#a83b3b", "Urban": "#7a3ba8"}


def gallery(props, path):
    """Prop Gallery 2: every prop on its own pad, in rows by category (north to south), wrapping long rows."""
    half_x = 60
    row_w = 2 * half_x - 14
    gap = 1.2
    ops = []
    z = None
    rows = []  # (category, [(prop, lo, hi)])
    for cat in CATEGORIES:
        items = sorted([p for p in props if p["category"] == cat], key=lambda q: q["name"])
        row, used = [], 0.0
        for q in items:
            lo, hi = bounds(q)
            w = hi[0] - lo[0] + 0.6
            if row and used + w + gap > row_w:
                rows.append((cat, row))
                row, used = [], 0.0
            row.append((q, lo, hi))
            used += w + gap
        if row:
            rows.append((cat, row))
    depth = [max(hi[2] - lo[2] for _, lo, hi in r) + 0.6 for _, r in rows]
    aisle = 3.2
    total = sum(depth) + aisle * (len(rows) - 1)
    half_z = math.ceil(total / 2 + 10)
    z = -total / 2
    for (cat, row), d in zip(rows, depth):
        zc = z + d / 2
        x = -row_w / 2 + 2.2
        ops.append({"op": "Block", "x0": round(x - 2.0, 2), "y0": 0, "z0": round(zc - d / 2, 2), "x1": round(x - 1.0, 2), "y1": 0.06,
                    "z1": round(zc + d / 2, 2), "color": CAT_COLORS[cat]})
        for q, lo, hi in row:
            w = hi[0] - lo[0] + 0.6
            px = x + w / 2 - (lo[0] + hi[0]) / 2
            pz = zc - (lo[2] + hi[2]) / 2
            ops.append({"op": "Block", "x0": round(x, 2), "y0": 0, "z0": round(zc - d / 2, 2), "x1": round(x + w, 2), "y1": 0.05,
                        "z1": round(zc + d / 2, 2), "color": [0.5, 0.5, 0.52]})
            ops.append({"op": "Prop", "name": q["name"], "x": round(px, 2), "z": round(pz, 2), "yaw": 0})
            x += w + gap
        z += d + aisle
    spawns = []
    for k in range(-4, 5):
        spawns += [{"op": "Spawn", "x": k * 12, "z": -(half_z - 4)}, {"op": "Spawn", "x": k * 12, "z": half_z - 4}]
    m = {"format": "call-of-slop-map/1", "name": "Prop Gallery 2",
         "description": "The outdoor props on their own pads, in rows by category (Vehicles, Rooftop, Utility, Signage, Urban from north to south).",
         "author": "Call of Slop", "order": 952, "halfX": half_x, "halfZ": half_z, "sky": [0.58, 0.64, 0.72],
         "ops": [{"op": "Ground", "color": [0.36, 0.37, 0.38]}, {"op": "Perimeter", "height": 4, "color": [0.55, 0.55, 0.57]}] + ops + spawns
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
    return half_x, half_z, len(rows)


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "Props"
    gpath = sys.argv[2] if len(sys.argv) > 2 else os.path.join("Maps", "Dev", "PropGallery2.json")
    for f in ALL:
        f()
    names = set()
    for p in PROPS:
        assert p["name"] not in names, p["name"]
        assert len(p["parts"]) <= 128, (p["name"], len(p["parts"]))
        assert p["category"] in CATEGORIES, p["name"]
        names.add(p["name"])
        lo, hi = bounds(p)
        assert lo[1] > -0.03 and hi[1] - lo[1] <= 12 and hi[0] - lo[0] <= 12 and hi[2] - lo[2] <= 12, (p["name"], lo, hi)
        write_prop(p, out)
    hx, hz, nrows = gallery(PROPS, gpath)
    print(len(PROPS), "props:", ", ".join(f"{c} {sum(1 for p in PROPS if p['category'] == c)}" for c in CATEGORIES),
          f"| gallery {2 * hx} x {2 * hz} m, {nrows} rows")
