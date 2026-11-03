"""The shape and look of each machine tier: machine_tiers.py asks this file what a line is at a tier.

docs/sandbox/CONSTRUCTS.md, "Machine tiers". The numbers (rates, bins, power, mass, health, sockets, ports, widths, the bit's
reach) are machine_tiers.py's and don't change here. This file has the bodies, in the same units as the line's sockets
(a module's size, a building's width): each tier of each line has its own solid shape, its own proportions and its own
drawn details, and machine_tiers.py grows them by the tier's size.

- PALETTE: a tier's colours by role.
- look(name, tier): a dict of "solid" (the boxes that collide), "detail" (drawn only) and "hopper" (where the input bin
  sits, y from and to, when the body moves it). A solid made by S(..., shape) draws as that shape, its box staying, hidden, as
  the collider. H() is a hidden solid box with nothing of its own drawn (a step under a big shape).
- The machines' files say "collider": "boxes", so a wedge or a cylinder here never becomes a hull.

The five looks:
- Scrap: lashed-up. Rust, mismatched plates, timber props, bolted-on junk, squat and crooked.
- Workshop: built by a craftsman. Tidy blue paint on cast iron, cream trim, rivets, one proper stack or column.
- Industrial: tall and busy. Concrete grey and safety yellow, big pipes, valves, hazard stripes, ladders, ramps, gantries.
- Military: low and hard. Olive armour on slopes, pale stencil bands, sandbags and cages, no bare pipework.
- Plasma: clean curves. Dark rounded shells, glowing rings and strips, and a violet core you can see.

A line still reads as itself at every tier: the drill's bit and tower, the saw's bin and blades, the mill's two beds and
blade frame, the furnace's hopper, stack and mouth, the foundry's twin stacks and the assembler's open gantry and arm.
Details stay clear of sockets, ports, bins, aisles and the assembler's open front. "Machine tier looks" holds them to it.
"""

SHAPE_KEYS = ("shape", "front", "segments", "inner", "ends", "taper", "shift", "radius", "edges", "angle", "dir")

PALETTE = {
    1: dict(body="#6b4a35", trim="#83563a", frame="#5a4f46", accent="#94702f", edge="#a59c8e", dark="#2f2a26", core="#7a4432", heat="#ff7a1f", motor="#4e5a42", light="#c89a3a",
            plate="#76835f", plate2="#8b857a", timber="#9a7442", rivet="#b3a48c", pipe="#6e6257", stripe="#a8542a", sand="#7c6f5c", coil="#514438"),
    2: dict(body="#2f5f8a", trim="#d8c690", frame="#3a3f46", accent="#d8c690", edge="#c9ced6", dark="#2a2e33", core="#3f6f9c", heat="#ff8a2a", motor="#26496b", light="#d8c690",
            plate="#3a6d9b", plate2="#274f74", timber="#3a3f46", rivet="#d8c690", pipe="#3a3f46", stripe="#d8c690", sand="#2a2e33", coil="#5a6068"),
    3: dict(body="#8f918d", trim="#74777a", frame="#5e6266", accent="#f0c21a", edge="#dfe4ea", dark="#26282b", core="#a3a29a", heat="#ffa030", motor="#c9a21c", light="#ffae20",
            plate="#a7a89f", plate2="#74777a", timber="#5e6266", rivet="#f0c21a", pipe="#b8bcc0", stripe="#16171a", sand="#74777a", coil="#c0301e"),
    4: dict(body="#4b5436", trim="#59603f", frame="#3e4530", accent="#d9d5c0", edge="#a8aeb4", dark="#1e211c", core="#3f4731", heat="#ff4a20", motor="#3a4230", light="#ff3020",
            plate="#5a6442", plate2="#6a6f60", timber="#3a4230", rivet="#2a2e24", pipe="#2a2e24", stripe="#d9d5c0", sand="#9a8c62", coil="#2a2e24"),
    5: dict(body="#1a1f2e", trim="#2c3448", frame="#242b3e", accent="#35e0ff", edge="#6ff0ff", dark="#0e1119", core="#20263a", heat="#b050ff", motor="#2c3448", light="#35e0ff",
            plate="#2c3448", plate2="#39445c", timber="#10131c", rivet="#4a5672", pipe="#10131c", stripe="#35e0ff", sand="#10131c", coil="#b050ff"),
}
GLOWS = {3: ("light",), 4: ("light", "heat"), 5: ("light", "heat", "edge", "accent", "stripe", "coil")}  # the roles that glow at a tier

UNEVEN = (1.0, 1.14, 0.84, 1.08, 0.92, 1.2, 0.88)  # Scrap's solid boxes: no two plates from the same batch


def scrap_tone(colour, i):
    """A tier 1 solid box's colour: the role's, a little lighter or darker by the box."""
    k = UNEVEN[i % len(UNEVEN)]
    return "#" + "".join(f"{min(255, int(int(colour[n:n + 2], 16) * k)):02x}" for n in (1, 3, 5))


def d(mn, mx, role="plate", **shape):
    return {"min": list(mn), "max": list(mx), "role": role, **shape}


def cyl(mn, mx, role="pipe", front="+y", **s):
    return d(mn, mx, role, shape="cylinder", front=front, **s)


def ring(mn, mx, role="coil", front="+y", inner=0.72, **s):
    return d(mn, mx, role, shape="tube", front=front, inner=inner, **s)


def wedge(mn, mx, role="plate", front="+y", taper=(1, 0.3), **s):
    return d(mn, mx, role, shape="wedge", front=front, taper=list(taper), **s)


def ball(mn, mx, role="rivet"):
    return d(mn, mx, role, shape="sphere")


def round_box(mn, mx, role="plate", front="-z", radius=0.3):
    return d(mn, mx, role, shape="rounded", front=front, radius=radius, edges="long")


def chamfer(mn, mx, role="plate", front="-z", radius=0.25):
    return d(mn, mx, role, shape="bevel", front=front, radius=radius, edges="long")


def sandbag(mn, mx):
    return d(mn, mx, "sand", shape="capsule", front="+x")


def rivets(points, size, axis):
    """Round heads on a face: points on the face, `axis` the face's normal (0, 1, 2)."""
    out = []
    for p in points:
        lo = [c - size for c in p]
        hi = [c + size for c in p]
        out.append(ball(lo, hi))
    return out


def hazard(mn, mx, n):
    """A yellow band with n black blocks across it, along its longest side. On a face: the thin side is its depth."""
    size = [b - a for a, b in zip(mn, mx)]
    thin = size.index(min(size))
    along = size.index(max(size))
    out = [d(mn, mx, "accent")]
    step = size[along] / (2 * n + 1)
    for k in range(n):
        lo, hi = list(mn), list(mx)
        lo[along] = mn[along] + step * (2 * k + 1)
        hi[along] = lo[along] + step
        lo[thin] -= 0.002
        hi[thin] += 0.002
        other = 3 - thin - along
        lo[other] += 0.004
        hi[other] -= 0.004
        out.append(d(lo, hi, "stripe"))
    return out


def ladder(x0, x1, y0, y1, z, rungs, depth=0.02):
    """Two rails and rungs standing proud of a z face (z is the wall; depth signed, outwards)."""
    z0, z1 = sorted((z, z + depth))
    w = abs(depth) * 0.6
    out = [d((x0, y0, z0), (x0 + w, y1, z1), "accent"), d((x1 - w, y0, z0), (x1, y1, z1), "accent")]
    for k in range(rungs):
        y = y0 + (y1 - y0) * (k + 0.5) / rungs
        zm = z0 + (z1 - z0) * 0.25
        out.append(d((x0 + w, y, zm), (x1 - w, y + w, zm + abs(depth) * 0.5), "accent"))
    return out


def side_ladder(z0, z1, y0, y1, x, rungs, depth=0.02):
    """The same on an x face."""
    x0, x1 = sorted((x, x + depth))
    w = abs(depth) * 0.6
    out = [d((x0, y0, z0), (x1, y1, z0 + w), "accent"), d((x0, y0, z1 - w), (x1, y1, z1), "accent")]
    for k in range(rungs):
        y = y0 + (y1 - y0) * (k + 0.5) / rungs
        xm = x0 + (x1 - x0) * 0.25
        out.append(d((xm, y, z0 + w), (xm + abs(depth) * 0.5, y + w, z1 - w), "accent"))
    return out



def S(mn, mx, role="body", shape=None, **s):
    """A solid box; with a shape, drawn as that shape (the box stays, hidden, as the collider)."""
    b = {"min": list(mn), "max": list(mx), "role": role}
    if shape:
        b["look"] = {"shape": shape, **s}
    return b


def H(mn, mx):
    """A solid box that isn't drawn: a step of the collider under a bigger shape."""
    return {"min": list(mn), "max": list(mx), "role": "body", "hidden": True}


def walls(x0, x1, y0, y1, z0, z1, t, role="frame"):
    """Four walls round an open room."""
    return [
        S((x0, y0, z0), (x1, y1, z0 + t), role), S((x0, y0, z1 - t), (x1, y1, z1), role),
        S((x0, y0, z0 + t), (x0 + t, y1, z1 - t), role), S((x1 - t, y0, z0 + t), (x1, y1, z1 - t), role),
    ]


FLOOR = S((-0.5, 0, -0.5), (0.5, 0.02, 0.5))


def tray(hw):
    """The product tray across the aisle (a Smelter's or a Foundry's): the same at every tier, the aisle's far side."""
    return [S((-hw, 0.02, 0.28), (hw, 0.024, 0.5))] + walls(-hw, hw, 0.02, 0.24, 0.26, 0.5, 0.04, "body")[:2] + [
        S((-hw, 0.02, 0.3), (-hw + 0.02, 0.24, 0.46)), S((hw - 0.02, 0.02, 0.3), (hw, 0.24, 0.46))]


def plinths(z, role="frame", top=0.46):
    """The solid service blocks the side and back sockets and the chute port stand on."""
    return [
        S((0.44, 0.02, z - 0.1), (0.5, top, z + 0.1), role), S((-0.5, 0.02, z - 0.1), (-0.44, top, z + 0.1), role),
        S((-0.1, 0.02, -0.5), (0.1, top, -0.44), role),
    ]


# ---- The drill: a bit under a tower, a hopper on a block ------------------------------------------------------------------

BIT = S((-0.07, -1.2, 0.61), (0.07, -0.3, 0.75), "edge")


def module_base(role="body", top=0.46):
    return [S((-0.25, 0, -0.25), (0.25, top, 0.25), role)]


HOPPER_RING = walls(-0.19, 0.19, 0.46, 0.62, -0.19, 0.19, 0.03)


def drill(t):
    if t == 1:  # a timber derrick: four legs round the bit, lashed to a block, a drum for a motor
        legs = [S((x, -0.3, z), (x + 0.07, 0.92, z + 0.07), "timber") for x in (-0.17, 0.1) for z in (0.5, 0.79)]
        return dict(solid=module_base() + HOPPER_RING + [S((-0.1, 0.2, 0.25), (0.1, 0.34, 0.5), "timber")] + legs + [
            S((-0.17, 0.86, 0.5), (0.17, 0.92, 0.86), "timber"), S((-0.13, 0.92, 0.55), (0.13, 1.0, 0.81), "motor"), BIT,
        ], detail=[
            d((-0.17, -0.28, 0.555), (0.17, -0.22, 0.61), "timber"), d((-0.1, 0.1, 0.57), (-0.04, 0.16, 0.79), "timber"),
            d((-0.2, 0.3, -0.262), (-0.08, 0.44, -0.25), "plate"), d((0.05, 0.05, 0.25), (0.22, 0.2, 0.262), "plate2"), d((-0.262, 0.04, 0.08), (-0.25, 0.14, 0.22), "plate"),
            cyl((0.16, 0.5, 0.56), (0.34, 0.82, 0.76), "motor"), d((0.27, 0.82, 0.62), (0.31, 0.98, 0.66), "pipe"),
            cyl((-0.1, -0.62, 0.58), (0.1, -0.5, 0.78), "pipe"), cyl((-0.085, -1.02, 0.595), (0.085, -0.94, 0.765), "dark"),
        ])
    if t == 2:  # a pillar drill: one round column, a head, a table round the bit, a flywheel
        return dict(solid=module_base() + HOPPER_RING + [
            S((-0.12, 0.24, 0.25), (0.12, 0.46, 0.56), "frame"),
            S((-0.09, -0.3, 0.59), (0.09, 0.7, 0.77), "frame", "cylinder", front="+y", segments=20),
            S((-0.16, 0.7, 0.5), (0.16, 0.92, 0.86), "body"), S((-0.13, 0.92, 0.55), (0.13, 1.0, 0.81), "motor"),
            S((-0.17, -0.3, 0.5), (0.17, -0.22, 0.86), "frame"), BIT,
        ], detail=[
            d((-0.168, 0.84, 0.492), (0.168, 0.88, 0.868), "trim"), d((-0.258, 0.4, -0.258), (0.258, 0.44, 0.258), "trim"), d((-0.258, 0.02, -0.258), (0.258, 0.09, 0.258), "frame"),
            cyl((0.17, 0.66, 0.56), (0.21, 0.94, 0.8), "dark", front="+x", segments=20), ring((0.205, 0.7, 0.6), (0.225, 0.9, 0.76), "trim", front="+x", ends="closed"),
            d((-0.235, 0.78, 0.2), (0.235, 0.8, 0.235), "trim"), d((-0.235, 0.62, 0.2), (-0.2, 0.78, 0.235), "frame"), d((0.2, 0.62, 0.2), (0.235, 0.78, 0.235), "frame"),
        ] + rivets([(-0.1, 0.78, 0.864), (0.1, 0.78, 0.864), (-0.1, 0.14, 0.03), (0.1, 0.14, 0.03)][:2], 0.012, 2))
    if t == 3:  # a stepped lattice tower with landings, pipes and a ladder
        return dict(solid=module_base() + HOPPER_RING + [
            S((-0.12, 0.24, 0.25), (0.12, 0.46, 0.5), "frame"),
            S((-0.19, -0.3, 0.5), (0.19, 0.3, 0.89)), S((-0.15, 0.3, 0.52), (0.15, 0.62, 0.84), "body"), S((-0.11, 0.62, 0.56), (0.11, 0.92, 0.8), "trim"),
            S((-0.2, 0.3, 0.49), (0.2, 0.34, 0.9), "accent"), S((-0.16, 0.62, 0.51), (0.16, 0.66, 0.85), "accent"),
            S((-0.13, 0.92, 0.55), (0.13, 1.0, 0.81), "motor"), BIT,
        ], detail=[
            cyl((-0.1, -0.85, 0.58), (0.1, -0.78, 0.78), "frame"), cyl((-0.1, -0.65, 0.58), (0.1, -0.58, 0.78), "frame"), cyl((-0.1, -0.45, 0.58), (0.1, -0.38, 0.78), "frame"),
            *hazard((-0.17, -0.25, 0.892), (0.17, -0.1, 0.902), 3),
            cyl((0.15, 0.34, 0.62), (0.2, 0.62, 0.67), "pipe"), ring((0.145, 0.46, 0.61), (0.205, 0.5, 0.68), "coil", inner=0.5, ends="closed"),
            *side_ladder(0.6, 0.76, 0.34, 0.9, -0.15, 3, -0.02), d((0.11, 0.86, 0.66), (0.17, 0.9, 0.72), "light"),
        ])
    if t == 4:  # a casemate: one low armoured hull over block and bit, a thin mast for the shaft
        return dict(solid=module_base("trim") + HOPPER_RING + [
            S((-0.21, -0.3, 0.25), (0.21, 0.5, 0.9), "body", "bevel", radius=0.25, edges="long", front="+z"),
            S((-0.07, 0.5, 0.61), (0.07, 0.92, 0.75), "frame"), S((-0.13, 0.92, 0.55), (0.13, 1.0, 0.81), "motor"), BIT,
        ], detail=[
            wedge((-0.2, 0.5, 0.5), (0.2, 0.68, 0.61), "plate", front="+y", taper=(0.6, 0.2), shift=(0, 0.3)), wedge((-0.2, 0.5, 0.75), (0.2, 0.68, 0.89), "plate", front="+y", taper=(0.6, 0.2), shift=(0, -0.3)),
            d((-0.213, 0.2, 0.3), (0.213, 0.27, 0.885), "accent"), d((-0.255, 0.3, -0.256), (0.255, 0.36, 0.256), "accent"),
            d((-0.1, -1.02, 0.52), (0.1, -1.0, 0.84), "dark"),
            *[d((x, -1.0, z), (x + 0.015, -0.34, z + 0.015), "dark") for x in (-0.1, 0.085) for z in (0.53, 0.815)],
            d((-0.11, -0.72, 0.52), (0.11, -0.7, 0.84), "dark"),
            sandbag((-0.24, 0.46, 0.195), (0.24, 0.52, 0.245)), d((0.1, 0.42, 0.895), (0.16, 0.46, 0.91), "light"),
        ])
    return dict(solid=[S((-0.25, 0, -0.25), (0.25, 0.46, 0.25), "body", "rounded", radius=0.28, edges="long", front="+y")] + HOPPER_RING + [  # a slim pod on a thin neck, rings up the bit
        S((-0.05, 0.3, 0.25), (0.05, 0.4, 0.56), "frame", "cylinder", front="+z"),
        S((-0.12, -0.3, 0.56), (0.12, 0.92, 0.8), "body", "rounded", radius=0.48, edges="long", front="+y"),
        S((-0.13, 0.92, 0.55), (0.13, 1.0, 0.81), "plate2", "rounded", radius=0.4, edges="long", front="+y"), BIT,
    ], detail=[
        ring((-0.12, -0.42, 0.56), (0.12, -0.36, 0.8), "coil", inner=0.7, segments=24), ring((-0.11, -0.66, 0.57), (0.11, -0.6, 0.79), "stripe", inner=0.72, segments=24), ring((-0.1, -0.9, 0.58), (0.1, -0.84, 0.78), "coil", inner=0.72, segments=24),
        ball((-0.07, 0.24, 0.76), (0.07, 0.42, 0.83), "coil"),
        ring((-0.16, 0.62, 0.52), (0.16, 0.66, 0.84), "stripe", inner=0.86, segments=32), ring((-0.16, 0.06, 0.52), (0.16, 0.1, 0.84), "stripe", inner=0.86, segments=32),
        d((-0.2, 0.2, 0.254), (0.2, 0.235, 0.262), "stripe"), d((-0.264, 0.2, -0.2), (-0.254, 0.235, 0.2), "stripe"), d((0.254, 0.2, -0.2), (0.264, 0.235, 0.2), "stripe"),
    ])


# ---- The saw: a bin, an arm, a blade bar across the front ------------------------------------------------------------------

BAR = S((-0.5, 0.24, 1.0), (0.5, 0.32, 1.36), "edge")


def discs(role, xs, r=0.165):
    return [cyl((x - r, 0.326, 1.18 - r), (x + r, 0.345, 1.18 + r), role, segments=24) for x in xs]


def saw_floor(role="body"):
    return S((-0.5, 0, -0.5), (0.5, 0.05, 0.5), role)


def saw(t):
    if t == 1:  # a slatted crate on skids, a crooked boom, two mismatched blades
        slats = []
        for y0, y1 in ((0.05, 0.3), (0.38, 0.66)):
            slats += walls(-0.5, 0.5, y0, y1, -0.5, 0.5, 0.06, "timber")
        return dict(solid=[saw_floor()] + slats + [
            S((-0.5, 0.3, -0.5), (-0.44, 0.38, -0.44), "timber"), S((0.44, 0.3, 0.44), (0.5, 0.38, 0.5), "timber"),
            S((-0.09, 0.2, 0.5), (0.09, 0.36, 1.0), "frame"), BAR,
        ], detail=discs("edge", (-0.3,)) + discs("plate2", (0.28,), 0.14) + [
            d((-0.34, 0.1, 0.5), (-0.1, 0.42, 0.512), "plate"), d((0.5, 0.42, 0.1), (0.512, 0.62, 0.42), "plate2"), d((-0.1, 0.42, -0.512), (0.3, 0.62, -0.5), "plate"),
            cyl((0.1, 0.36, 0.58), (0.3, 0.6, 0.78), "motor"),
            d((-0.5, 0.0, -0.53), (-0.42, 0.04, 0.53), "timber"), d((0.42, 0.0, -0.53), (0.5, 0.04, 0.53), "timber"),
        ])
    if t == 2:  # a square painted bin, a motor on a pedestal, a straight arm under a cast guard
        return dict(solid=[saw_floor()] + walls(-0.5, 0.5, 0.05, 0.7, -0.5, 0.5, 0.06, "body") + [
            S((-0.09, 0.2, 0.5), (0.09, 0.36, 1.0), "frame"), S((-0.14, 0.36, 0.6), (0.14, 0.56, 0.88), "motor"), BAR,
        ], detail=discs("edge", (-0.33, 0, 0.33)) + [
            d((-0.506, 0.66, -0.506), (0.506, 0.69, -0.438), "trim"), d((-0.506, 0.66, 0.438), (0.506, 0.69, 0.506), "trim"),
            d((-0.508, 0.03, 0.494), (0.508, 0.12, 0.508), "frame"), d((-0.508, 0.03, -0.508), (0.508, 0.12, -0.494), "frame"), d((-0.52, 0.355, 0.98), (0.52, 0.375, 1.02), "trim"),
            d((-0.52, 0.355, 1.0), (-0.5, 0.42, 1.36), "frame"), d((0.5, 0.355, 1.0), (0.52, 0.42, 1.36), "frame"), d((-0.52, 0.42, 1.0), (0.52, 0.44, 1.02), "trim"),
        ] + rivets([(x, 0.55, 0.512) for x in (-0.4, -0.2, 0.2, 0.4)], 0.012, 2))
    if t == 3:  # a raised loading cab at the back, twin arms and a yellow gearbox
        return dict(solid=[saw_floor()] + walls(-0.5, 0.5, 0.05, 0.7, -0.5, 0.5, 0.06, "body") + [
            S((-0.5, 0.7, -0.5), (0.5, 0.8, -0.34), "trim"),
            S((-0.34, 0.2, 0.5), (-0.2, 0.36, 1.0), "frame"), S((0.2, 0.2, 0.5), (0.34, 0.36, 1.0), "frame"), S((-0.2, 0.2, 0.86), (0.2, 0.36, 1.0), "accent"), BAR,
        ], detail=discs("edge", (-0.33, 0, 0.33)) + [
            *hazard((-0.52, 0.355, 0.97), (0.52, 0.4, 1.0), 4), cyl((-0.45, 0.46, 0.505), (0.45, 0.53, 0.575), "pipe", front="+x"),
            ring((0.3, 0.44, 0.565), (0.36, 0.55, 0.59), "coil", front="+z", inner=0.6, ends="closed"), *side_ladder(-0.3, -0.06, 0.02, 0.76, -0.5, 3, -0.03),
            d((0.3, 0.8, -0.44), (0.36, 0.84, -0.38), "light"),
        ])
    if t == 4:  # a low hull with a closed nose: the arm is a wide armoured wedge, sandbags on the rim
        return dict(solid=[saw_floor()] + walls(-0.5, 0.5, 0.05, 0.66, -0.5, 0.5, 0.06, "body") + [
            S((-0.5, 0.1, 0.5), (0.5, 0.36, 1.0), "body", "wedge", front="+z", taper=(0.96, 0.4), shift=(0, -0.28)), BAR,
        ], detail=[
            wedge((-0.52, 0.32, 1.0), (0.52, 0.46, 1.38), "plate", front="+z", taper=(0.92, 0.3), shift=(0, -0.3)), d((-0.512, 0.44, -0.512), (0.512, 0.5, -0.5), "accent"), d((-0.512, 0.44, 0.5), (0.512, 0.5, 0.512), "accent"), d((-0.512, 0.44, -0.5), (-0.5, 0.5, 0.5), "accent"), d((0.5, 0.44, -0.5), (0.512, 0.5, 0.5), "accent"),
            sandbag((-0.46, 0.66, -0.5), (0.46, 0.75, -0.42)), sandbag((-0.46, 0.66, 0.42), (0.46, 0.75, 0.5)), d((-0.5, 0.2, 1.37), (0.5, 0.31, 1.385), "dark"),
            d((0.16, 0.34, 0.6), (0.22, 0.4, 0.66), "light"),
        ])
    return dict(solid=[saw_floor("trim")] + walls(-0.5, 0.5, 0.05, 0.7, -0.5, 0.5, 0.06, "body") + [  # a capsule roof over the bin's back, two slim booms, a core between them, rings for blades
        S((-0.44, 0.7, -0.48), (0.44, 0.82, -0.14), "plate2", "rounded", radius=0.48, edges="long", front="+x"),
        S((-0.46, 0.22, 0.5), (-0.36, 0.34, 1.0), "plate2", "rounded", radius=0.48, edges="long", front="+z"), S((0.36, 0.22, 0.5), (0.46, 0.34, 1.0), "plate2", "rounded", radius=0.48, edges="long", front="+z"),
        S((-0.5, 0.24, 1.0), (0.5, 0.32, 1.36), "edge", "rounded", radius=0.45, edges="long", front="+x"),
    ], detail=[
        ring((-0.495, 0.33, 1.015), (-0.165, 0.37, 1.345), "coil", inner=0.88, segments=32), ring((-0.165, 0.33, 1.015), (0.165, 0.37, 1.345), "coil", inner=0.88, segments=32), ring((0.165, 0.33, 1.015), (0.495, 0.37, 1.345), "coil", inner=0.88, segments=32),
        ball((-0.09, 0.2, 0.62), (0.09, 0.38, 0.8), "coil"), d((-0.36, 0.27, 0.68), (0.36, 0.29, 0.74), "stripe"),
        d((-0.42, 0.705, 0.47), (0.42, 0.72, 0.5), "stripe"), d((-0.42, 0.705, -0.5), (0.42, 0.72, -0.47), "stripe"), d((-0.36, 0.3, 0.5), (0.36, 0.33, 0.512), "stripe"),
        d((-0.33, 0.1, -0.512), (-0.31, 0.64, -0.5), "stripe"), d((0.31, 0.1, -0.512), (0.33, 0.64, -0.5), "stripe"), d((-0.31, 0.56, -0.512), (0.31, 0.58, -0.5), "stripe"),
    ])


# ---- The sawmill: two beds and a blade frame between them --------------------------------------------------------------------

def beds(role="trim", top=0.45):
    return walls(-0.5, 0.5, 0.02, top, -0.5, -0.1, 0.06, role) + walls(-0.5, 0.5, 0.02, top, 0.1, 0.5, 0.06, role)


BLADES = [d((-0.015, 0.52, -0.28), (0.015, 0.98, 0.28), "edge"), d((-0.018, 0.46, -0.18), (0.018, 1.03, 0.18), "edge"), d((-0.12, 0.72, -0.03), (0.12, 0.78, 0.03), "dark")]
LOW_BLADES = [d((-0.015, 0.46, -0.24), (0.015, 0.82, 0.24), "edge"), d((-0.018, 0.46, -0.16), (0.018, 0.86, 0.16), "edge"), d((-0.12, 0.62, -0.03), (0.12, 0.68, 0.03), "dark")]


def sawmill(t):
    if t == 1:  # a timber trestle: splayed posts, a motor on the floor with a belt up to the arbor
        return dict(solid=[FLOOR] + beds("timber", 0.36) + [
            S((-0.3, 0.02, -0.08), (-0.24, 0.4, 0.08), "timber"), S((-0.25, 0.4, -0.08), (-0.19, 0.72, 0.08), "timber"), S((-0.2, 0.72, -0.08), (-0.14, 0.93, 0.08), "timber"),
            S((0.24, 0.02, -0.08), (0.3, 0.4, 0.08), "timber"), S((0.19, 0.4, -0.08), (0.25, 0.72, 0.08), "timber"), S((0.14, 0.72, -0.08), (0.2, 0.93, 0.08), "timber"),
            S((-0.24, 0.93, -0.12), (0.24, 0.99, 0.12), "frame"), S((0.3, 0.02, -0.09), (0.44, 0.2, 0.09), "motor"),
        ], detail=[d((-0.015, 0.45, -0.22), (0.015, 0.9, 0.22), "edge"), d((-0.018, 0.45, -0.14), (0.018, 0.92, 0.14), "edge"), d((-0.19, 0.62, -0.03), (0.19, 0.68, 0.03), "dark"),
                   d((0.2, 0.2, -0.02), (0.22, 0.64, 0.02), "dark"), d((-0.5, 0.02, -0.512), (-0.1, 0.26, -0.5), "plate"), d((0.04, 0.1, -0.512), (0.42, 0.32, -0.5), "plate2"), d((-0.3, 0.08, 0.5), (0.1, 0.3, 0.512), "plate2"),
                   d((-0.28, 0.99, -0.14), (0.3, 1.03, 0.16), "plate2")])
    if t == 2:  # a cast frame with a beam, the motor on the frame, a flywheel on the arbor
        return dict(solid=[FLOOR] + beds("body") + [
            S((-0.18, 0.02, -0.1), (-0.12, 1.05, 0.1)), S((0.12, 0.02, -0.1), (0.18, 1.05, 0.1)), S((-0.2, 1.05, -0.16), (0.2, 1.12, 0.16), "accent"), S((0.18, 0.6, -0.12), (0.46, 0.88, 0.12), "motor"),
        ], detail=BLADES + [
            d((-0.506, 0.45, -0.506), (0.506, 0.47, -0.437), "trim"), d((-0.506, 0.45, 0.437), (0.506, 0.47, 0.506), "trim"),
            cyl((-0.34, 0.54, -0.14), (-0.28, 0.82, 0.14), "dark", front="-x", segments=24), ring((-0.345, 0.58, -0.1), (-0.275, 0.78, 0.1), "trim", front="-x", ends="closed"), d((-0.28, 0.66, -0.03), (-0.18, 0.72, 0.03), "frame"),
            d((-0.206, 0.02, -0.126), (-0.1, 0.12, 0.126), "frame"), d((0.1, 0.02, -0.126), (0.206, 0.12, 0.126), "frame"),
        ] + rivets([(-0.15, 0.9, 0.1), (0.15, 0.9, 0.1), (-0.15, 0.3, 0.1), (0.15, 0.3, 0.1)], 0.012, 2))
    if t == 3:  # a gantry the width of the mill: tall outer legs, an overhead beam, the motor hung from it
        return dict(solid=[FLOOR] + beds("trim") + [
            S((-0.5, 0.02, -0.07), (-0.42, 1.12, 0.07), "accent"), S((0.42, 0.02, -0.07), (0.5, 1.12, 0.07), "accent"), S((-0.42, 1.04, -0.1), (0.42, 1.12, 0.1), "frame"),
            S((-0.16, 0.88, -0.1), (0.16, 1.04, 0.1), "motor"), S((-0.06, 0.02, -0.08), (0.06, 0.42, 0.08), "frame"),
        ], detail=BLADES + [
            *hazard((-0.505, 0.455, -0.505), (0.505, 0.475, -0.44), 3), d((-0.505, 0.455, 0.44), (0.505, 0.475, 0.505), "accent"),
            cyl((0.16, 0.94, -0.04), (0.42, 1.02, 0.04), "pipe", front="+x"),
            *side_ladder(-0.07, 0.07, 0.48, 1.04, -0.42, 4, 0.02), d((0.3, 1.12, -0.03), (0.36, 1.16, 0.03), "light"),
        ])
    if t == 4:  # a low armoured housing over the blade, sandbagged beds
        return dict(solid=[FLOOR] + beds("trim", 0.4) + [
            S((-0.34, 0.02, -0.1), (-0.12, 0.84, 0.1), "body", "bevel", radius=0.3, edges="long", front="+x"), S((0.12, 0.02, -0.1), (0.34, 0.84, 0.1), "body", "bevel", radius=0.3, edges="long", front="+x"),
            S((-0.34, 0.84, -0.14), (0.34, 0.92, 0.14), "plate"),
        ], detail=LOW_BLADES + [
            wedge((-0.34, 0.92, -0.14), (0.34, 1.0, 0.14), "plate2", front="+y", taper=(0.7, 0.4)), d((-0.345, 0.5, -0.105), (-0.12, 0.57, 0.105), "accent"), d((0.12, 0.5, -0.105), (0.345, 0.57, 0.105), "accent"),
            sandbag((-0.5, 0.4, -0.5), (0.5, 0.46, -0.44)), sandbag((-0.5, 0.4, 0.44), (0.5, 0.46, 0.5)), sandbag((-0.5, 0.4, -0.16), (0.5, 0.46, -0.1)), sandbag((-0.5, 0.4, 0.1), (0.5, 0.46, 0.16)),
            d((0.26, 0.7, 0.1), (0.32, 0.74, 0.115), "light"),
        ])
    return dict(solid=[FLOOR] + beds("body") + [  # the blade in a standing ring, two slim pylons
        H((-0.42, 0.02, -0.05), (-0.34, 0.8, 0.05)), H((0.34, 0.02, -0.05), (0.42, 0.8, 0.05)), H((-0.34, 0.86, -0.05), (0.34, 1.1, 0.05)),
    ], detail=[
        ring((-0.44, 0.26, -0.05), (0.44, 1.14, 0.05), "plate2", front="+z", inner=0.86, segments=48),
        ring((-0.36, 0.34, -0.06), (0.36, 1.06, 0.06), "coil", front="+z", inner=0.92, segments=48),
        d((-0.015, 0.5, -0.26), (0.015, 0.94, 0.26), "stripe"), ball((-0.1, 0.64, -0.08), (0.1, 0.84, 0.08), "coil"),
        d((-0.46, 0.452, -0.48), (0.46, 0.468, -0.46), "stripe"), d((-0.46, 0.452, 0.46), (0.46, 0.468, 0.48), "stripe"), d((-0.46, 0.452, -0.14), (0.46, 0.468, -0.12), "stripe"), d((-0.46, 0.452, 0.12), (0.46, 0.468, 0.14), "stripe"),
    ])


# ---- The smelter: a furnace with a mouth over the aisle, a hopper and a stack ---------------------------------------------------

def hop(y0, y1, role="frame", x0=-0.34, x1=0.1):
    """The input's hopper: walls round the room the input bin fills."""
    return walls(x0, x1, y0, y1, -0.44, -0.11, 0.04, role)


def smelter(t):
    floor = [FLOOR] + tray(0.36)
    if t == 1:  # a squat kiln, lashed together, a short crooked flue
        return dict(hopper=(0.685, 0.86), solid=floor + plinths(-0.25, "trim", 0.4)[:2] + hop(0.68, 0.88, "timber") + [
            S((-0.44, 0.02, -0.5), (0.44, 0.5, -0.05), "core"), S((-0.36, 0.5, -0.46), (0.36, 0.68, -0.09)), S((0.14, 0.68, -0.38), (0.3, 1.0, -0.22), "dark"),
        ], detail=[
            d((-0.3, 0.16, -0.06), (0.3, 0.4, -0.04), "heat"), d((-0.4, 0.42, -0.05), (-0.08, 0.49, -0.04), "plate"), d((0.14, 0.04, -0.05), (0.42, 0.14, -0.04), "plate2"),
            d((-0.45, 0.06, -0.47), (-0.44, 0.17, -0.37), "plate"), d((-0.42, 0.02, -0.53), (-0.37, 0.72, -0.5), "timber"), d((0.37, 0.02, -0.53), (0.42, 0.66, -0.5), "timber"),
            d((-0.46, 0.44, -0.53), (0.46, 0.49, -0.5), "timber"), cyl((0.39, 0.5, -0.21), (0.49, 0.66, -0.11), "motor"), d((0.1, 1.0, -0.42), (0.28, 1.06, -0.2), "coil"), d((0.13, 0.8, -0.39), (0.31, 0.88, -0.21), "plate2"),
        ])
    if t == 2:  # a brick-bodied furnace: one proper stack, a cream-banded door
        return dict(solid=floor + hop(0.8, 1.0) + [
            S((-0.5, 0.02, -0.5), (0.5, 0.6, -0.05), "core"), S((-0.38, 0.6, -0.46), (0.38, 0.8, -0.09)), S((0.16, 0.8, -0.38), (0.32, 1.34, -0.22), "dark"), S((0.14, 1.34, -0.4), (0.34, 1.4, -0.2)),
        ], detail=[
            d((-0.3, 0.18, -0.05), (0.3, 0.42, -0.03), "heat"), d((-0.508, 0.03, -0.508), (0.508, 0.1, -0.042), "frame"), d((-0.508, 0.54, -0.508), (0.508, 0.57, -0.042), "trim"), d((-0.386, 0.77, -0.466), (0.386, 0.79, -0.084), "trim"),
            ring((0.135, 1.0, -0.405), (0.345, 1.04, -0.195), "trim", inner=0.74, ends="closed"), ring((0.135, 1.2, -0.405), (0.345, 1.24, -0.195), "trim", inner=0.74, ends="closed"),
            d((-0.33, 0.43, -0.047), (0.33, 0.46, -0.03), "trim"), d((-0.33, 0.14, -0.05), (-0.3, 0.46, -0.028), "frame"), d((0.3, 0.14, -0.05), (0.33, 0.46, -0.028), "frame"),
        ] + rivets([(x, 0.5, -0.044) for x in (-0.44, -0.38, 0.38, 0.44)], 0.012, 2))
    if t == 3:  # a tall blast furnace: a narrow shaft, a stepped charging ramp up its side, a downcomer pipe
        return dict(hopper=(1.065, 1.24), solid=floor + hop(1.06, 1.26) + [
            S((0.44, 0.02, -0.35), (0.5, 0.46, -0.15), "frame"), S((-0.1, 0.02, -0.5), (0.1, 0.46, -0.44), "frame"),
            S((-0.36, 0.02, -0.46), (0.12, 0.5, -0.05), "core"), S((-0.34, 0.5, -0.46), (0.1, 1.06, -0.09), "core"),
            S((-0.5, 0.02, -0.34), (-0.36, 0.42, -0.14), "trim"), S((-0.44, 0.42, -0.34), (-0.34, 0.74, -0.14), "trim"),
            S((0.18, 0.02, -0.38), (0.32, 1.4, -0.24), "dark"), S((0.1, 0.84, -0.34), (0.18, 0.96, -0.28), "dark"),
        ], detail=[
            d((-0.3, 0.14, -0.05), (0.08, 0.4, -0.03), "heat"), *hazard((-0.36, 0.42, -0.044), (0.12, 0.48, -0.034), 3), *side_ladder(-0.36, -0.2, 0.5, 1.06, 0.1, 3, 0.016),
            ring((0.16, 1.06, -0.4), (0.34, 1.1, -0.22), "accent", inner=0.8, ends="closed"), ring((0.16, 0.6, -0.4), (0.34, 0.64, -0.22), "accent", inner=0.8, ends="closed"),
            wedge((-0.5, 0.42, -0.33), (-0.44, 0.74, -0.15), "plate2", front="+y", taper=(0.2, 1), shift=(0.4, 0)), d((0.14, 0.2, -0.46), (0.17, 0.26, -0.4), "light"),
        ])
    if t == 4:  # a low bunker: the hopper let into the roof, a sloped front, a stub flue, sandbags
        return dict(hopper=(0.465, 0.64), solid=floor + hop(0.46, 0.66, "trim") + [
            S((-0.5, 0.02, -0.5), (0.5, 0.46, -0.05), "core", "bevel", radius=0.12, edges="long", front="+x"), S((0.16, 0.46, -0.38), (0.32, 0.82, -0.22), "dark", "bevel", radius=0.3, edges="long", front="+y"),
        ], detail=[
            d((-0.3, 0.14, -0.05), (0.3, 0.3, -0.03), "heat"), d((-0.52, 0.3, -0.05), (0.52, 0.34, -0.03), "accent"),
            wedge((-0.5, 0.46, -0.2), (-0.34, 0.6, -0.05), "plate", front="+y", taper=(0.6, 0.4)), wedge((0.34, 0.46, -0.2), (0.5, 0.6, -0.05), "plate", front="+y", taper=(0.6, 0.4)),
            ring((0.13, 0.6, -0.41), (0.35, 0.66, -0.19), "plate2", inner=0.7, ends="closed"), sandbag((-0.5, 0.02, -0.56), (-0.1, 0.07, -0.505)), sandbag((0.1, 0.02, -0.56), (0.5, 0.07, -0.505)), sandbag((-0.46, 0.07, -0.56), (-0.14, 0.12, -0.505)), sandbag((0.14, 0.07, -0.56), (0.46, 0.12, -0.505)),
            d((0.38, 0.4, -0.03), (0.44, 0.44, -0.02), "light"),
        ])
    return dict(hopper=(1.045, 1.2), solid=floor + hop(1.04, 1.22, "body") + plinths(-0.25, "trim", 0.46) + [  # a standing ring round a violet core, a slim emitter for a stack
        H((-0.32, 0.02, -0.38), (0.32, 0.12, -0.12)), H((-0.48, 0.3, -0.38), (-0.38, 0.8, -0.12)), H((0.38, 0.3, -0.38), (0.48, 0.8, -0.12)), H((-0.34, 0.96, -0.46), (0.34, 1.04, -0.09)),
        S((0.16, 1.0, -0.38), (0.32, 1.32, -0.22), "plate2", "rounded", radius=0.48, edges="long", front="+y"),
    ], detail=[
        ring((-0.49, 0.05, -0.39), (0.49, 1.03, -0.11), "plate2", front="+z", inner=0.8, segments=48), ring((-0.39, 0.15, -0.26), (0.39, 0.93, -0.06), "stripe", front="+z", inner=0.94, segments=48),
        ball((-0.2, 0.34, -0.36), (0.2, 0.74, -0.04), "coil"), ring((0.13, 1.06, -0.41), (0.35, 1.1, -0.19), "stripe", inner=0.82, segments=32), ring((0.13, 1.2, -0.41), (0.35, 1.24, -0.19), "coil", inner=0.82, segments=32),
        d((-0.36, 0.236, 0.5), (0.36, 0.246, 0.51), "stripe"),
    ])


# ---- The foundry: two furnaces, a mouth between them, twin stacks, a long hopper ---------------------------------------------

def foundry_hop(y0, y1, role="frame"):
    h = walls(-0.42, 0.42, y0, y1, -0.44, -0.11, 0.04, role)
    return h + [S((-0.05, y0, -0.4), (0.05, y1, -0.15), role)]


def foundry(t):
    floor = [FLOOR] + tray(0.38)
    twin = [S((-0.5, 0.02, -0.5), (-0.12, 0.62, -0.05), "core"), S((0.12, 0.02, -0.5), (0.5, 0.62, -0.05), "core")]
    back = S((-0.12, 0.02, -0.5), (0.12, 0.3, -0.12), "dark")
    if t == 1:  # two squat kilns under one timber beam, odd stacks
        return dict(hopper=(0.585, 0.74), solid=floor + foundry_hop(0.58, 0.76, "timber") + [
            S((-0.5, 0.02, -0.48), (-0.1, 0.5, -0.06), "core"), S((0.1, 0.02, -0.48), (0.5, 0.46, -0.06), "core"), back, S((-0.46, 0.5, -0.46), (0.46, 0.58, -0.09)),
            S((-0.5, 0.58, -0.44), (-0.42, 0.98, -0.34), "dark"), S((0.42, 0.58, -0.3), (0.5, 0.84, -0.2), "dark"),
        ], detail=[
            d((-0.11, 0.05, -0.12), (0.11, 0.28, -0.1), "heat"), d((-0.46, 0.3, -0.05), (-0.2, 0.48, -0.04), "plate"), d((0.18, 0.08, -0.05), (0.44, 0.3, -0.04), "plate2"), d((-0.2, 0.32, -0.07), (0.2, 0.38, -0.055), "timber"),
            d((-0.44, 0.02, -0.51), (-0.39, 0.58, -0.48), "timber"), d((0.39, 0.02, -0.51), (0.44, 0.58, -0.48), "timber"), d((-0.5, 0.42, -0.52), (0.5, 0.47, -0.49), "timber"), d((-0.52, 0.98, -0.46), (-0.4, 1.02, -0.32), "coil"), d((0.4, 0.84, -0.32), (0.52, 0.88, -0.18), "plate2"),
        ])
    if t == 2:  # a matched pair of painted towers, two round stacks on the bridge, a cream arch
        return dict(solid=floor + foundry_hop(0.72, 0.9) + twin + [back, S((-0.46, 0.62, -0.46), (0.46, 0.72, -0.09)),
            S((-0.3, 0.9, -0.34), (-0.16, 1.22, -0.2), "dark", "cylinder", front="+y", segments=20), S((0.16, 0.9, -0.34), (0.3, 1.22, -0.2), "dark", "cylinder", front="+y", segments=20),
        ], detail=[
            d((-0.11, 0.05, -0.12), (0.11, 0.28, -0.1), "heat"), d((-0.508, 0.03, -0.508), (-0.114, 0.1, -0.042), "frame"), d((0.114, 0.03, -0.508), (0.508, 0.1, -0.042), "frame"),
            d((-0.508, 0.54, -0.508), (-0.114, 0.57, -0.042), "trim"), d((0.114, 0.54, -0.508), (0.508, 0.57, -0.042), "trim"), d((-0.14, 0.3, -0.135), (0.14, 0.34, -0.1), "trim"),
            ring((-0.32, 1.17, -0.36), (-0.14, 1.21, -0.18), "trim", inner=0.74, ends="closed"), ring((0.14, 1.17, -0.36), (0.32, 1.21, -0.18), "trim", inner=0.74, ends="closed"),
        ])
    if t == 3:  # stepped: a tall tower left, a low one right, stacks at both ends, a pipe bridge
        return dict(hopper=(0.925, 1.08), solid=floor + foundry_hop(0.92, 1.1) + [
            S((-0.5, 0.02, -0.5), (-0.12, 0.92, -0.05), "core"), S((0.12, 0.02, -0.5), (0.5, 0.46, -0.05), "core"), S((0.12, 0.46, -0.46), (0.46, 0.92, -0.09), "trim"), back, S((-0.12, 0.62, -0.46), (0.12, 0.92, -0.09)),
            S((-0.5, 0.92, -0.5), (-0.42, 1.22, -0.44), "dark"), S((0.46, 0.46, -0.3), (0.5, 1.22, -0.2), "dark"),
        ], detail=[
            d((-0.11, 0.05, -0.12), (0.11, 0.28, -0.1), "heat"), *hazard((-0.508, 0.06, -0.04), (-0.12, 0.12, -0.03), 2), *hazard((0.12, 0.06, -0.04), (0.508, 0.12, -0.03), 2),
            cyl((-0.42, 1.14, -0.3), (0.46, 1.2, -0.24), "pipe", front="+x"), ring((-0.035, 1.11, -0.325), (0.035, 1.23, -0.215), "coil", front="+x", inner=0.6, ends="closed"), *side_ladder(-0.42, -0.2, 0.46, 0.92, 0.46, 2, 0.016), d((0.38, 0.46, -0.05), (0.44, 0.52, -0.02), "light"),
        ])
    if t == 4:  # one low bunker: a long roof with sloped flanks, stub stacks at the ends
        return dict(hopper=(0.525, 0.68), solid=floor + foundry_hop(0.52, 0.7, "trim") + [
            S((-0.5, 0.02, -0.5), (0.5, 0.42, -0.22), "core"), S((-0.5, 0.02, -0.22), (-0.16, 0.26, -0.05), "core"), S((0.16, 0.02, -0.22), (0.5, 0.26, -0.05), "core"), S((-0.42, 0.42, -0.46), (0.42, 0.52, -0.09), "body", "bevel", radius=0.2, edges="long", front="+x"),
            S((-0.5, 0.42, -0.44), (-0.42, 0.82, -0.3), "dark", "bevel", radius=0.3, edges="long", front="+y"), S((0.42, 0.42, -0.44), (0.5, 0.82, -0.3), "dark", "bevel", radius=0.3, edges="long", front="+y"),
        ], detail=[
            d((-0.13, 0.05, -0.235), (0.13, 0.26, -0.215), "heat"), d((-0.512, 0.32, -0.512), (0.512, 0.37, -0.215), "accent"), wedge((-0.5, 0.26, -0.22), (-0.16, 0.42, -0.05), "plate", front="+z", taper=(1, 0.1), shift=(0, -0.45)), wedge((0.16, 0.26, -0.22), (0.5, 0.42, -0.05), "plate", front="+z", taper=(1, 0.1), shift=(0, -0.45)),
            sandbag((-0.5, 0.02, -0.56), (-0.1, 0.07, -0.505)), sandbag((0.1, 0.02, -0.56), (0.5, 0.07, -0.505)), sandbag((-0.46, 0.07, -0.56), (-0.14, 0.12, -0.505)), d((0.3, 0.18, -0.05), (0.36, 0.22, -0.04), "light"),
        ])
    return dict(hopper=(0.925, 1.08), solid=floor + foundry_hop(0.92, 1.1, "body") + plinths(-0.25, "trim", 0.46)[2:] + [  # twin capsule pods joined by a coil round a violet core
        S((-0.5, 0.02, -0.46), (-0.14, 0.92, -0.09), "core", "rounded", radius=0.45, edges="long", front="+y"), S((0.14, 0.02, -0.46), (0.5, 0.92, -0.09), "core", "rounded", radius=0.45, edges="long", front="+y"),
        S((-0.42, 1.1, -0.32), (-0.3, 1.22, -0.2), "plate2", "cylinder", front="+y", segments=24),
        S((0.3, 1.1, -0.32), (0.42, 1.22, -0.2), "plate2", "cylinder", front="+y", segments=24),
    ], detail=[
        ring((-0.5, 0.56, -0.47), (-0.14, 0.6, -0.08), "stripe", inner=0.9, segments=32), ring((0.14, 0.56, -0.47), (0.5, 0.6, -0.08), "stripe", inner=0.9, segments=32),
        ring((-0.5, 0.26, -0.47), (-0.14, 0.3, -0.08), "stripe", inner=0.9, segments=32), ring((0.14, 0.26, -0.47), (0.5, 0.3, -0.08), "stripe", inner=0.9, segments=32),
        ball((-0.1, 0.06, -0.26), (0.1, 0.26, -0.06), "coil"), ring((-0.15, 0.02, -0.12), (0.15, 0.32, -0.09), "stripe", front="+z", inner=0.86, segments=32),
    ])


# ---- The assembler: an open gantry with a low hopper at the back and an arm over the tray ---------------------------------------

def asm_floor():
    return S((-0.5, 0, -0.5), (0.5, 0.03, 0.5))


def asm_hopper(role="dark"):
    return walls(-0.46, 0.46, 0.03, 0.34, -0.46, -0.04, 0.02, role)


def assembler(t):
    if t == 1:  # two timber frames with crossbars, no rails: a wonky motor on a beam
        posts = [S((x, 0.03, z), (x + 0.04, 0.86, z + 0.05), "timber") for x in (-0.5, 0.46) for z in (-0.5, 0.45)]
        return dict(solid=[asm_floor()] + asm_hopper("trim") + posts + [
            S((-0.5, 0.86, -0.5), (0.5, 0.92, -0.42), "timber"), S((-0.5, 0.86, 0.42), (0.5, 0.92, 0.5), "timber"), S((-0.5, 0.15, -0.46), (-0.46, 0.33, -0.04), "timber"), S((0.46, 0.15, -0.46), (0.5, 0.33, -0.04), "timber"),
            S((-0.5, 0.92, -0.08), (0.5, 0.98, 0.08), "timber"), S((-0.06, 0.62, -0.1), (0.06, 0.92, 0.1), "motor"),
        ], detail=[
            d((-0.02, 0.56, -0.02), (0.02, 0.62, 0.02), "edge"), d((-0.3, 0.06, -0.482), (0.1, 0.28, -0.47), "plate"), d((0.04, 0.34, -0.47), (0.44, 0.4, -0.04), "plate2"),
            cyl((0.07, 0.72, -0.08), (0.2, 0.9, 0.05), "motor"), d((0.07, 0.9, 0.0), (0.2, 0.92, 0.05), "plate2"),
        ])
    if t == 2:  # a machine tool: a full frame, cream rails, the arm on a rail
        posts = [S((x, 0.03, z), (x + 0.04, 0.9, z + 0.04), "frame") for x in (-0.5, 0.46) for z in (-0.5, 0.46)]
        return dict(solid=[asm_floor()] + asm_hopper() + posts + walls(-0.5, 0.5, 0.9, 0.96, -0.5, 0.5, 0.06, "body") + [
            S((-0.5, 0.15, -0.46), (-0.46, 0.35, 0.46), "frame"), S((0.46, 0.15, -0.46), (0.5, 0.35, 0.46), "frame"), S((-0.06, 0.62, -0.1), (0.06, 0.9, 0.1), "motor"),
        ], detail=[
            d((-0.44, 0.92, -0.02), (0.44, 0.94, 0.02), "accent"), d((-0.02, 0.56, -0.02), (0.02, 0.62, 0.02), "edge"), d((-0.504, 0.96, -0.504), (0.504, 0.98, -0.436), "trim"), d((-0.504, 0.96, 0.436), (0.504, 0.98, 0.504), "trim"),
            d((-0.075, 0.64, -0.115), (0.075, 0.68, 0.115), "trim"), d((-0.5, 0.965, -0.03), (0.5, 1.0, 0.03), "frame"),
            d((-0.512, 0.03, -0.512), (-0.458, 0.09, -0.448), "frame"), d((0.458, 0.03, -0.512), (0.512, 0.09, -0.448), "frame"), d((-0.512, 0.03, 0.448), (-0.458, 0.09, 0.512), "frame"), d((0.458, 0.03, 0.448), (0.512, 0.09, 0.512), "frame"),
        ])
    if t == 3:  # a bridge crane: tall yellow legs, a heavy beam across, the arm hung from a trolley
        posts = [S((x, 0.03, z), (x + 0.04, 1.0, z + 0.06), "accent") for x in (-0.5, 0.46) for z in (-0.5, 0.44)]
        return dict(solid=[asm_floor()] + asm_hopper() + posts + [
            S((-0.5, 1.0, -0.5), (-0.42, 1.1, 0.5), "frame"), S((0.42, 1.0, -0.5), (0.5, 1.1, 0.5), "frame"), S((-0.42, 1.02, -0.09), (0.42, 1.1, 0.09), "accent"), S((-0.5, 0.15, -0.44), (-0.46, 0.35, 0.44), "frame"), S((0.46, 0.15, -0.44), (0.5, 0.35, 0.44), "frame"),
            S((-0.1, 0.9, -0.12), (0.1, 1.02, 0.12), "motor"), S((-0.05, 0.62, -0.05), (0.05, 0.9, 0.05), "frame"),
        ], detail=[
            d((-0.02, 0.56, -0.02), (0.02, 0.62, 0.02), "edge"), *hazard((-0.505, 0.5, 0.444), (-0.49, 1.0, 0.5), 3), *hazard((0.49, 0.5, 0.444), (0.505, 1.0, 0.5), 3),
            cyl((0.32, 0.34, -0.5), (0.38, 1.02, -0.44), "pipe"), ring((0.3, 0.7, -0.52), (0.4, 0.74, -0.42), "coil", inner=0.5, ends="closed"), d((0.3, 1.1, -0.46), (0.36, 1.14, -0.4), "light"),
        ])
    if t == 4:  # a low armoured shelter: thick posts, a sloped roof, the arm caged under it
        posts = [S((x, 0.03, z), (x + 0.04, 0.72, z + 0.08), "body", "bevel", radius=0.25, edges="long", front="+y") for x in (-0.5, 0.46) for z in (-0.5, 0.42)]
        return dict(solid=[asm_floor()] + asm_hopper("trim") + posts + [
            S((-0.5, 0.72, -0.5), (0.5, 0.8, 0.5), "body"), S((-0.5, 0.15, -0.42), (-0.46, 0.35, 0.42), "frame"), S((0.46, 0.15, -0.42), (0.5, 0.35, 0.42), "frame"), S((-0.06, 0.6, -0.1), (0.06, 0.72, 0.1), "motor"),
        ], detail=[
            d((-0.02, 0.56, -0.02), (0.02, 0.6, 0.02), "edge"), wedge((-0.5, 0.8, -0.5), (0.5, 0.89, 0.5), "plate", front="+y", taper=(0.8, 0.7)), d((-0.512, 0.732, -0.512), (0.512, 0.77, 0.512), "accent"),
            d((-0.46, 0.36, -0.512), (0.46, 0.72, -0.5), "plate"), sandbag((-0.5, 0.03, -0.56), (0.5, 0.09, -0.505)), sandbag((-0.5, 0.09, -0.56), (0.5, 0.15, -0.505)),
            *[d((x, 0.62, z), (x + 0.015, 0.72, z + 0.015), "dark") for x in (-0.1, 0.085) for z in (-0.14, 0.125)], d((0.36, 0.42, -0.515), (0.42, 0.46, -0.5), "light"),
        ])
    return dict(solid=[asm_floor()] + asm_hopper("body") + [  # one arch over the tray, a ring for the arm
        H((-0.5, 0.03, -0.5), (-0.46, 0.9, -0.42)), H((0.46, 0.03, -0.5), (0.5, 0.9, -0.42)), H((-0.5, 0.03, 0.42), (-0.46, 0.9, 0.5)), H((0.46, 0.03, 0.42), (0.5, 0.9, 0.5)),
        H((-0.5, 0.15, -0.42), (-0.46, 0.33, -0.04)), H((0.46, 0.15, -0.42), (0.5, 0.33, -0.04)),
        S((-0.5, 0.9, -0.5), (0.5, 0.96, 0.5), "trim", "rounded", radius=0.45, edges="long", front="+x"), S((-0.06, 0.66, -0.1), (0.06, 0.9, 0.1), "plate2", "rounded", radius=0.48, edges="long", front="+y"),
    ], detail=[
        *[round_box((x, 0.03, z), (x + 0.04, 0.9, z + 0.08), "plate2", front="+y", radius=0.48) for x in (-0.5, 0.46) for z in (-0.5, 0.42)],
        ring((-0.24, 0.62, -0.24), (0.24, 0.66, 0.24), "stripe", inner=0.86, segments=32), ball((-0.05, 0.57, -0.05), (0.05, 0.67, 0.05), "coil"),
        d((-0.42, 0.965, 0.44), (0.42, 0.98, 0.47), "stripe"), d((-0.42, 0.965, -0.47), (0.42, 0.98, -0.44), "stripe"), d((-0.512, 0.36, -0.49), (-0.5, 0.88, -0.47), "stripe"), d((0.5, 0.36, -0.49), (0.512, 0.88, -0.47), "stripe"),
        d((-0.42, 0.34, -0.452), (0.42, 0.352, -0.438), "stripe"),
    ])


LINES = {"Drill Module": drill, "Saw Module": saw, "Sawmill": sawmill, "Smelter": smelter, "Foundry": foundry, "Assembler": assembler}


def look(name, tier):
    """What a line is at a tier: {"solid": [...], "detail": [...], "hopper": (y0, y1) or absent}."""
    return LINES[name](tier)
