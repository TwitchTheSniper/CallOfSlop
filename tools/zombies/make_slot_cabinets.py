#!/usr/bin/env python3
"""Writes Props/Zombies/SlotMachines/*.json: one slot cabinet per weapon-builder part category (Zombies slot machines,
2026-10-26; own body shapes 2026-10-27; docs/zombies/ZOMBIES-MODE.md, "Slot machines").

Run from the repository root:  python3 -I tools/zombies/make_slot_cabinets.py

Every cabinet shares the same FACE, in the same places as on the classic "Reel Cabinet" (Props/Zombies/ReelCabinet.json),
because the game draws on it (Source/App/Program.ZombiesSlots.cs): the reel window at y 1.3 (reels at z -0.29), the
category band at y 1.6, the topper lights' marquee at y 1.8 (lamps at z -0.258), the heat meter at y 0.76 and the bet
lamps at y 0.66 (z -0.31), the button shelf at y 0.97 and the pull lever on the right. The face is a plate whose front is
at z -0.255, so a body must stay behind z -0.2 where the face is (|x| < 0.36, y 0.6 to 1.92).

The BODY behind and round the face is the category's own silhouette (original shapes): a standing barrel, a scope
lying across the top, a giant cartridge, a drum and a curved magazine, a sloped stock wedge, a raked pistol grip
with its trigger guard, a flared muzzle, a boxy receiver with an ejection port, a Y fork, a tool chest, a spur gear,
a stepped "x2" tower, a sword hilt, an axe head on a chopping block, a nail-studded club and a bolt on a spring.
Round shapes are regular polygon prisms: a 2k-gon with apothem a is exactly the union of k boxes 2a long and
2a*tan(180/2k) wide, turned 180/k degrees apart about the prism's axis (see prism()).

Prop part rotations are [pitch, yaw, roll] in degrees applied as yaw * pitch * roll (PropDef.Rotation,
Quaternion.CreateFromYawPitchRoll), about each part's own centre: this script builds every part from a rotation
matrix and converts it (euler()), so tilted prisms (the splitter's arms) need no hand-worked angles.
Each body stays inside the classic footprint (about 0.9 x 0.7 m, the lever and a knob or two a little past it).
"""
import json
import math
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Props", "Zombies", "SlotMachines")

GUN = "#3a3d42"     # gunmetal
STEEL = "#8a9099"
DARK = "#16181b"
BRASS = "#c8961c"
DBRASS = "#9a7316"
COPPER = "#b06a36"
WOOD = "#6b4524"
DWOOD = "#4a2e17"
GLASS = "#5a9ad8"
CHROME = "chrome"
BLACK = "black"


# ---- rotation helpers (column vectors; R = Ry(yaw) Rx(pitch) Rz(roll), as Quaternion.CreateFromYawPitchRoll) ----

def rx(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    return [[1, 0, 0], [0, c, -s], [0, s, c]]


def ry(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    return [[c, 0, s], [0, 1, 0], [-s, 0, c]]


def rz(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    return [[c, -s, 0], [s, c, 0], [0, 0, 1]]


def mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


IDENT = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]


def euler(r):
    """[pitch, yaw, roll] in degrees with r == Ry(yaw) Rx(pitch) Rz(roll)."""
    sx = max(-1.0, min(1.0, -r[1][2]))
    x = math.asin(sx)
    if abs(sx) < 0.99999:
        z = math.atan2(r[1][0], r[1][1])
        y = math.atan2(r[0][2], r[2][2])
    else:
        z = 0.0
        y = math.atan2(-r[2][0], r[0][0])
    e = [math.degrees(x), math.degrees(y), math.degrees(z)]
    back = mul(mul(ry(e[1]), rx(e[0])), rz(e[2]))
    assert all(abs(back[i][j] - r[i][j]) < 1e-6 for i in range(3) for j in range(3)), "euler round trip"
    return e


def b(size, offset, color, rot=None, material=None):
    """A box. rot: a rotation matrix, or [pitch, yaw, roll] degrees."""
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    if rot is not None:
        e = euler(rot) if isinstance(rot[0], list) else rot
        e = [round(v, 2) + 0.0 for v in e]
        e = [0.0 if abs(v) < 0.005 else v for v in e]
        if any(e):
            p["rotation"] = e
    if material:
        p["material"] = material
    return p


# The prism's own axis is local Y; AX_X / AX_Z turn it onto X or Z.
AX_Y = IDENT
AX_X = rz(90)
AX_Z = rx(90)


def prism(centre, axis, a, length, color, sides=12, material=None, spin=0.0):
    """A regular `sides`-gon prism (sides even) with apothem a, `length` along `axis` (a rotation taking local Y onto
    the prism's axis), one flat face facing local -Z before `axis` turns it (spin turns it about its axis)."""
    k = sides // 2
    w = 2 * a * math.tan(math.pi / sides)
    out = []
    for i in range(k):
        r = mul(axis, ry(spin + i * 180.0 / k))
        out.append(b([2 * a, length, w], centre, color, r, material))
    return out


def ring_y(x, z, y0, y1, a, color, sides=12, material=None):
    return prism([x, (y0 + y1) / 2, z], AX_Y, a, y1 - y0, color, sides, material)


def ring_x(y, z, x0, x1, a, color, sides=12, material=None):
    return prism([(x0 + x1) / 2, y, z], AX_X, a, x1 - x0, color, sides, material)


def ring_z(x, y, z0, z1, a, color, sides=12, material=None):
    return prism([x, y, (z0 + z1) / 2], AX_Z, a, z1 - z0, color, sides, material)


def along(p0, p1, a, color, sides=8, material=None):
    """A prism from point p0 to p1 (any direction)."""
    d = [p1[i] - p0[i] for i in range(3)]
    length = math.sqrt(sum(v * v for v in d))
    u = [v / length for v in d]
    # A rotation taking +Y onto u: tilt by the angle between them about (Y x u).
    ax = [u[2], 0.0, -u[0]]  # Y x u
    s = math.sqrt(ax[0] ** 2 + ax[2] ** 2)
    c = u[1]
    if s < 1e-9:
        r = IDENT if c > 0 else rx(180)
    else:
        kx, kz = ax[0] / s, ax[2] / s
        t = 1 - c
        r = [[t * kx * kx + c, -kz * s, t * kx * kz],
             [kz * s, c, -kx * s],
             [t * kx * kz, kx * s, t * kz * kz + c]]
    mid = [(p0[i] + p1[i]) / 2 for i in range(3)]
    return prism(mid, r, a, length, color, sides, material)


# ---- the shared face (positions as on the classic Reel Cabinet: the game draws its reels and lamps there) ----

def face(colour, plate, tray=False):
    parts = [
        b([0.74, 1.32, 0.055], [0, 1.26, -0.2275], plate, material="metal"),        # the face plate (front z -0.255)
        b([0.72, 0.18, 0.004], [0, 1.8, -0.257], "#2a1608"),                       # marquee (topper lights at z -0.258)
        b([0.7, 0.07, 0.02], [0, 1.6, -0.264], colour),                             # the category band
        b([0.68, 0.36, 0.02], [0, 1.3, -0.265], CHROME),                            # reel bezel
        b([0.62, 0.3, 0.02], [0, 1.3, -0.275], BLACK),                              # reel window (reels at z -0.29)
        b([0.76, 0.06, 0.16], [0, 0.97, -0.32], CHROME),                            # button shelf
        b([0.12, 0.03, 0.06], [-0.22, 1.01, -0.34], "#b02020"),
        b([0.12, 0.03, 0.06], [0.0, 1.01, -0.34], "#20a040"),
        b([0.12, 0.03, 0.06], [0.22, 1.01, -0.34], "#2050c0"),
        b([0.66, 0.3, 0.06], [0, 0.74, -0.27], plate, material="metal"),          # meter panel (bet lamps at z -0.31)
        b([0.52, 0.1, 0.02], [0, 0.76, -0.305], BLACK),                             # heat meter window
        b([0.035, 1.3, 0.05], [-0.38, 1.26, -0.235], colour),                       # side trims of the face
        b([0.035, 1.3, 0.05], [0.38, 1.26, -0.235], colour),
        b([0.1, 0.14, 0.14], [0.44, 1.18, -0.2], "darkgrey", material="metal"),     # the pull lever
        b([0.04, 0.44, 0.04], [0.47, 1.43, -0.2], CHROME),
        b([0.1, 0.1, 0.1], [0.47, 1.67, -0.2], "#c01818"),
    ]
    if tray:
        parts += [b([0.46, 0.14, 0.05], [0, 0.3, -0.25], plate), b([0.42, 0.07, 0.12], [0, 0.3, -0.33], CHROME)]
    return parts


def plinth(color="darkgrey", w=0.86, d=0.64, h=0.1, z=0.03):
    return [b([w, h, d], [0, h / 2, z], color, material="metal")]


# ---- the bodies ----

def receiver(c):
    p = plinth()
    p += [b([0.72, 0.92, 0.58], [0, 0.56, 0.06], "#5a1f1c", material="metal")]           # lower receiver
    p += [b([0.08, 0.5, 0.5], [-0.4, 0.42, 0.06], "#4a1917")]                            # magwell flare
    p += [b([0.84, 0.95, 0.56], [0, 1.47, 0.08], GUN, material="metal")]                 # upper receiver, wide and chunky
    p += [b([0.86, 0.08, 0.58], [0, 0.98, 0.08], c)]                                     # the seam
    p += [b([0.012, 0.26, 0.36], [0.426, 1.46, 0.12], DARK)]                             # ejection port (right side)
    p += [b([0.08, 0.1, 0.3], [0.4, 1.46, 0.12], STEEL)]                                 # the bolt carrier in it
    p += [b([0.016, 0.24, 0.36], [0.47, 1.28, 0.12], GUN, rz(-50))]                      # dust cover, swung open
    p += ring_x(1.66, 0.3, 0.42, 0.48, 0.05, STEEL, 8)                                   # forward assist
    p += [b([0.3, 0.05, 0.62], [0, 1.97, 0.08], DARK)]                                   # top rail
    for i in range(7):
        p += [b([0.34, 0.035, 0.04], [0, 2.01, -0.17 + i * 0.083], DARK)]
    p += [b([0.08, 0.05, 0.08], [0, 1.88, 0.37], STEEL), b([0.28, 0.05, 0.05], [0, 1.88, 0.41], c)]  # charging handle
    p += [b([0.86, 0.04, 0.04], [0, 1.94, -0.18], c)]
    return p, face(c, "#2b2e33", tray=True)


def barrel(c):
    p = ring_y(0, 0.06, 0, 0.12, 0.36, "darkgrey", material="metal")
    p += ring_y(0, 0.06, 0.12, 2.0, 0.3, GUN, material="metal")                          # the standing barrel
    for yaw in (50, 90, 130, 180, 230, 270, 310):                                       # flutes
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        p += [b([0.04, 1.5, 0.03], [0.302 * s, 1.05, 0.06 - 0.302 * co], DARK, ry(-yaw))]
    for y0 in (0.3, 1.94):
        p += ring_y(0, 0.06, y0, y0 + 0.06, 0.32, STEEL, material="metal")             # heat bands
    p += ring_y(0, 0.06, 2.0, 2.12, 0.33, c, material="metal")                           # the muzzle crown
    p += ring_y(0, 0.06, 2.12, 2.125, 0.17, DARK)                                        # the bore
    p += [b([0.07, 0.16, 0.05], [0, 2.2, -0.2], STEEL), b([0.14, 0.04, 0.08], [0, 2.14, -0.2], STEEL)]  # front sight
    return p, face(c, "#2b2e33", tray=True)


def splitter(c):
    p = ring_y(0, 0.06, 0, 0.12, 0.34, "darkgrey", material="metal")
    p += ring_y(0, 0.06, 0.12, 1.2, 0.27, GUN, material="metal")                         # the trunk
    p += ring_y(0, 0.06, 1.1, 1.42, 0.31, c, material="metal")                           # the manifold
    for sx in (-1, 1):                                                                   # the two arms of the Y
        p += along([sx * 0.06, 1.3, 0.06], [sx * 0.36, 2.22, 0.06], 0.13, GUN, 12, "metal")
        p += along([sx * 0.34, 2.16, 0.06], [sx * 0.39, 2.31, 0.06], 0.16, c, 12, "metal")  # nozzle caps
        p += along([sx * 0.385, 2.3, 0.06], [sx * 0.39, 2.315, 0.06], 0.09, DARK, 8)
    p += ring_y(0, 0.06, 0.5, 0.56, 0.29, STEEL)
    return p, face(c, "#20302e", tray=True)


def feed(c):
    p = [b([0.12, 0.08, 0.5], [x, 0.04, 0.04], "darkgrey") for x in (-0.3, 0.3)]          # feet
    p += ring_x(0.42, 0.04, -0.4, 0.4, 0.36, GUN, material="metal")                      # the drum, on its edge
    for sx in (-1, 1):
        p += ring_x(0.42, 0.04, sx * 0.4, sx * 0.44, 0.15, c, material="metal")          # hub caps
    p += [b([0.04, 0.05, 0.3], [-0.46, 0.42, 0.04], STEEL), b([0.04, 0.12, 0.05], [-0.46, 0.5, 0.17], STEEL)]  # winding key
    for i, (zc, tilt) in enumerate(((0.06, 0), (0.1, 6), (0.17, 12))):                 # the curved box magazine
        p += [b([0.62, 0.5, 0.42], [0, 0.95 + i * 0.44, zc], GUN, rx(tilt), "metal")]
        for sx in (-1, 1):                                                               # witness holes
            p += [b([0.01, 0.05, 0.05], [sx * 0.312, 0.95 + i * 0.44, zc + 0.12], DARK)]
    p += [b([0.66, 0.06, 0.48], [0, 0.72, 0.06], STEEL)]                                 # the joint
    for sx in (-1, 1):                                                                   # feed lips
        p += [b([0.08, 0.08, 0.34], [sx * 0.2, 2.13, 0.26], STEEL, rx(12))]
    p += ring_z(0, 2.2, 0.06, 0.4, 0.08, BRASS, 8, "metal")                              # the top round
    p += ring_z(0, 2.2, -0.0, 0.06, 0.06, COPPER, 8)
    return p, face(c, "#2b2e33")


def ammo(c):
    p = ring_y(0, 0.07, 0, 0.08, 0.34, DBRASS, material="metal")                         # rim
    p += ring_y(0, 0.07, 0.08, 0.16, 0.27, DBRASS, material="metal")                     # extractor groove
    p += ring_y(0, 0.07, 0.16, 1.72, 0.31, BRASS, material="metal")                      # the case
    p += ring_y(0, 0.07, 1.72, 1.8, 0.26, BRASS, material="metal")                       # shoulder
    p += ring_y(0, 0.07, 1.8, 1.86, 0.21, BRASS, material="metal")
    p += ring_y(0, 0.07, 1.86, 2.0, 0.18, BRASS, material="metal")                       # neck
    for y0, y1, a in ((2.0, 2.12, 0.175), (2.12, 2.22, 0.155), (2.22, 2.31, 0.125), (2.31, 2.38, 0.09), (2.38, 2.43, 0.05)):
        p += ring_y(0, 0.07, y0, y1, a, COPPER, material="metal")                        # the bullet
    p += ring_y(0, 0.07, 2.04, 2.07, 0.182, "#7a4422")                                   # cannelure
    p += ring_y(0, 0.07, 0.4, 0.46, 0.315, c)                                            # a coloured band
    return p, face(c, "#3a2c10", tray=True)


def stock(c):
    body = "#5a3a20"
    p = [b([0.8, 0.12, 0.72], [0, 0.06, 0.04], BLACK)]                                   # butt pad
    for z in (-0.18, 0.04, 0.26):
        p += [b([0.82, 0.03, 0.04], [0, 0.08, z], "#222222")]
    p += [b([0.74, 0.62, 0.66], [0, 0.43, 0.05], body)]                                  # the toe of the stock
    p += [b([0.72, 1.3, 0.22], [0, 1.37, -0.1], body)]                                   # the front column
    p += [b([0.72, 0.47, 0.23], [0, 0.975, 0.115], body)]                                # (fills behind it, under the slope)
    # The wedge: a slab whose top face runs from (z -0.02, y 2.0) down to (z 0.38, y 0.74).
    p0, p1, t = (-0.02, 2.0), (0.38, 0.74), 0.2
    dz, dy = p1[0] - p0[0], p1[1] - p0[1]
    ln = math.hypot(dz, dy)
    uz, uy = dz / ln, dy / ln
    nz, ny = -uy, uz          # inward normal (towards the front and down)
    if nz > 0:
        nz, ny = -nz, -ny
    mz, my = (p0[0] + p1[0]) / 2 + nz * t / 2, (p0[1] + p1[1]) / 2 + ny * t / 2
    tilt = math.degrees(math.atan2(-uz, -uy))  # turn local Y onto the slope (pointing up the slope)
    p += [b([0.72, ln, t], [0, my, mz], body, rx(tilt))]
    p += [b([0.3, ln * 0.8, 0.06], [0, my - ny * t / 2 + 0.02 * -ny, mz - nz * t / 2 + 0.02 * -nz], c, rx(tilt))]  # cheek comb
    p += ring_x(0.5, 0.36, -0.06, 0.06, 0.05, STEEL, 8)                                  # sling swivel
    p += [b([0.74, 0.04, 0.04], [0, 2.0, -0.2], c)]
    return p, face(c, "#2a1a0e")


def grip(c):
    p = plinth("darkgrey", 0.86, 0.66, 0.06)
    p += [b([0.6, 0.12, 0.52], [0, 0.12, 0.26], GUN, material="metal")]                  # the base plate
    # The raked grip column: top at (y 1.95, z 0.0), bottom at (y 0.12, z 0.28).
    p += [b([0.56, 1.86, 0.46], [0, 1.03, 0.14], "#2f5a2a", rx(-8.7), "metal")]
    for y in (0.5, 0.8, 1.1):                                                            # finger grooves
        p += ring_x(y, 0.14 + (1.03 - y) * 0.153 - 0.23, -0.27, 0.27, 0.05, "#244620", 8)
    for sx in (-1, 1):                                                                   # stippled side panels
        p += [b([0.012, 1.1, 0.3], [sx * 0.285, 1.0, 0.16], c, rx(-8.7))]
    p += [b([0.66, 0.34, 0.74], [0, 2.08, 0.02], GUN, material="metal")]                 # the frame over it
    p += [b([0.5, 0.1, 0.24], [0, 2.0, 0.42], GUN)]                                      # the beavertail
    p += [b([0.5, 0.1, 0.32], [0, 0.61, -0.1], GUN, material="metal")]                   # the trigger guard's bar
    p += [b([0.1, 0.5, 0.06], [0, 1.25, -0.02], STEEL, rx(-12))]                         # the trigger
    p += [b([0.12, 0.06, 0.12], [0, 1.52, -0.05], STEEL)]
    return p, face(c, "#202a1e")


def optic(c):
    p = plinth()
    p += [b([0.5, 0.84, 0.4], [0, 0.52, 0.06], GUN, material="metal")]                  # pedestal
    p += [b([0.72, 0.08, 0.36], [0, 0.98, 0.06], DARK)]                                  # the rail
    for x in (-0.18, 0.18):                                                              # scope rings
        p += ring_x(1.28, 0.06, x - 0.04, x + 0.04, 0.29, DARK)
        p += [b([0.08, 0.2, 0.12], [x, 1.04, 0.06], DARK)]
    p += ring_x(1.28, 0.06, -0.3, 0.3, 0.26, "#1f3550", material="metal")               # the main tube
    p += ring_x(1.28, 0.06, -0.47, -0.3, 0.34, "#1f3550", material="metal")              # objective bell
    p += ring_x(1.28, 0.06, -0.33, -0.27, 0.3, c)
    p += ring_x(1.28, 0.06, -0.48, -0.47, 0.28, GLASS)                                   # the lenses
    p += ring_x(1.28, 0.06, 0.3, 0.44, 0.3, "#1f3550", material="metal")                 # eyepiece
    p += ring_x(1.28, 0.06, 0.44, 0.45, 0.23, GLASS)
    p += ring_y(0, 0.1, 1.5, 1.72, 0.09, c)                                              # elevation turret
    p += ring_y(0, 0.1, 1.72, 1.76, 0.1, STEEL)
    return p, face(c, "#1a2433")


def muzzle(c):
    p = []
    for i, a in enumerate((0.42, 0.38, 0.34, 0.3, 0.27)):                               # the flared skirt
        p += ring_y(0, 0.06, i * 0.12, i * 0.12 + 0.12, a, GUN if i % 2 == 0 else "#2e3135", material="metal")
    p += ring_y(0, 0.06, 0.6, 1.96, 0.26, GUN, material="metal")                         # the stem
    for i, a in enumerate((0.28, 0.31, 0.345, 0.38, 0.415)):                            # the flared bell on top
        p += ring_y(0, 0.06, 1.96 + i * 0.1, 2.06 + i * 0.1, a, c if i == 4 else GUN, material="metal")
    p += ring_y(0, 0.06, 2.46, 2.465, 0.3, DARK)                                         # the bore
    for yaw in range(0, 360, 45):                                                        # ports round the bell
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        p += [b([0.08, 0.06, 0.02], [0.37 * s, 2.24, 0.06 - 0.37 * co], DARK, ry(-yaw))]
    return p, face(c, "#2b2e33", tray=True)


def utility(c):
    red = "#a8262a"
    p = []
    for x in (-0.36, 0.36):                                                              # casters
        for z in (-0.2, 0.26):
            p += ring_x(0.06, z, x - 0.025, x + 0.025, 0.06, BLACK, 8)
            p += [b([0.08, 0.06, 0.08], [x, 0.14, z], STEEL)]
    p += [b([0.86, 0.92, 0.6], [0, 0.63, 0.04], red, material="metal")]                 # the rolling chest
    for y in (0.24, 0.42):                                                               # its drawers
        p += [b([0.8, 0.012, 0.01], [0, y + 0.08, -0.265], DARK), b([0.4, 0.03, 0.04], [0, y, -0.28], CHROME)]
    p += [b([0.03, 0.04, 0.4], [-0.45, 0.9, 0.04], CHROME)]                              # side handle
    p += [b([0.8, 0.84, 0.48], [0, 1.51, 0.04], red, material="metal")]                  # the top box
    p += [b([0.84, 0.08, 0.52], [0, 1.97, 0.04], "#8a1e22")]                             # its lid
    for x in (-0.3, 0.3):
        p += [b([0.06, 0.08, 0.02], [x, 1.93, -0.225], CHROME)]                          # latches
        p += [b([0.04, 0.16, 0.04], [x * 0.85, 2.09, 0.04], DARK)]                       # the carry handle
    p += [b([0.55, 0.04, 0.06], [0, 2.18, 0.04], DARK)]
    p += [b([0.4, 0.025, 0.06], [0.0, 2.025, 0.2], STEEL, ry(20))]                       # a spanner on the lid
    p += [b([0.07, 0.025, 0.1], [0.2, 2.025, 0.13], STEEL, ry(20))]
    p += [b([0.86, 0.04, 0.6], [0, 1.08, 0.04], c)]
    return p, face(c, "#3a1416")


def upgrade(c):
    purple = "#4a3366"
    p = ring_y(0, 0.06, 0, 0.12, 0.38, "darkgrey", material="metal")
    p += ring_y(0, 0.06, 0.12, 1.96, 0.31, purple, material="metal")                     # a spur gear on its axle
    for i in range(12):                                                                  # its teeth
        yaw = 15 + i * 30
        if -40 < ((yaw + 180) % 360) - 180 < 40:
            continue  # (the face covers the front ones)
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        p += [b([0.1, 1.84, 0.1], [0.34 * s, 1.04, 0.06 - 0.34 * co], c, ry(-yaw), "metal")]
    p += ring_y(0, 0.06, 1.96, 2.0, 0.2, STEEL)                                          # the axle cap
    # An upright gear on top, meshing with the column.
    p += ring_z(0, 2.28, 0.0, 0.12, 0.22, c, material="metal")
    for i in range(10):
        a = i * 36
        s, co = math.sin(math.radians(a)), math.cos(math.radians(a))
        p += [b([0.08, 0.08, 0.12], [0.25 * s, 2.28 + 0.25 * co, 0.06], c, rz(-a), "metal")]
    p += ring_z(0, 2.28, -0.01, 0.13, 0.07, STEEL)
    p += [b([0.1, 0.1, 0.1], [0, 2.05, 0.06], STEEL)]
    return p, face(c, "#24193a", tray=True)


def multiplier(c):
    p = [b([0.88, 0.5, 0.66], [0, 0.25, 0.03], "#3a3320", material="metal")]             # a stepped tower
    p += [b([0.8, 0.72, 0.52], [0, 0.86, 0.06], "#4a4128", material="metal")]
    p += [b([0.72, 0.74, 0.44], [0, 1.59, 0.02], "#5a4f30", material="metal")]
    for y in (0.5, 1.22):
        p += [b([0.82 if y < 1 else 0.74, 0.04, 0.6 if y < 1 else 0.48], [0, y, 0.05], c)]
    for sx in (-1, 1):                                                                   # chevrons on the sides
        for k in range(2):
            p += [b([0.012, 0.06, 0.24], [sx * 0.402, 0.75 + k * 0.15, 0.0], c, rx(30)),
                  b([0.012, 0.06, 0.24], [sx * 0.402, 0.75 + k * 0.15, 0.2], c, rx(-30))]
    # "x2" standing on top, facing the front.
    p += [b([0.72, 0.06, 0.12], [0, 1.99, 0.02], DARK)]
    # (Seen from the front, +x is on the player's left: the x stands at +x and the 2 is drawn mirrored in x.)
    p += [b([0.3, 0.06, 0.08], [0.18, 2.2, 0.02], c, rz(45)), b([0.3, 0.06, 0.08], [0.18, 2.2, 0.02], c, rz(-45))]
    two = [([0.2, 0.06], [-0.18, 2.42]), ([0.06, 0.18], [-0.25, 2.33]), ([0.2, 0.06], [-0.18, 2.22]),
           ([0.06, 0.18], [-0.11, 2.12]), ([0.2, 0.06], [-0.18, 2.04])]
    p += [b([s[0], s[1], 0.08], [o[0], o[1], 0.02], c) for s, o in two]
    return p, face(c, "#2e2a1a", tray=True)


def hilt(c):
    p = ring_y(0, 0.06, 0, 0.1, 0.38, DBRASS, material="metal")                           # the pommel
    p += ring_y(0, 0.06, 0.1, 0.32, 0.34, BRASS, material="metal")
    p += ring_y(0, 0.06, 0.32, 0.4, 0.28, DBRASS, material="metal")
    p += ring_y(0, 0.06, 0.4, 1.9, 0.26, "#4a2a18")                                      # the wrapped grip
    for i in range(6):
        p += ring_y(0, 0.06, 0.5 + i * 0.24, 0.56 + i * 0.24, 0.27, c)
    p += [b([0.9, 0.16, 0.34], [0, 2.0, 0.06], BRASS, material="metal")]                  # the cross guard
    for sx in (-1, 1):
        p += [b([0.1, 0.18, 0.3], [sx * 0.42, 2.12, 0.06], BRASS, rz(-sx * 25), "metal")]  # curled quillons
    p += [b([0.36, 0.5, 0.08], [0, 2.33, 0.06], STEEL, material="metal")]                # the blade
    p += [b([0.255, 0.255, 0.08], [0, 2.58, 0.06], STEEL, rz(45), "metal")]              # its point
    p += [b([0.04, 0.6, 0.085], [0, 2.38, 0.06], "#5c6470")]                             # the fuller
    return p, face(c, "#2a1a0e")


def head(c):
    p = ring_y(0, 0.06, 0, 0.62, 0.38, "#4a3220")                                        # a chopping block
    p += ring_y(0, 0.06, 0.62, 0.66, 0.36, "#b08a58")                                    # its cut top
    p += ring_y(0, 0.06, 0.66, 2.62, 0.2, WOOD)                                          # the haft
    h = 2.24                                                                             # the axe head's centre
    p += [b([0.36, 0.44, 0.26], [0, h, 0.06], GUN, material="metal")]                     # its eye
    p += [b([0.26, 0.5, 0.14], [-0.28, h, 0.06], GUN, material="metal")]                  # its cheek
    p += [b([0.12, 0.62, 0.12], [-0.38, h - 0.04, 0.06], "#5a5e66", material="metal")]
    p += [b([0.12, 0.22, 0.12], [-0.38, h - 0.4, 0.06], "#5a5e66", rz(-20), "metal")]   # the beard
    p += [b([0.04, 0.74, 0.13], [-0.45, h - 0.09, 0.06], c, material="metal")]          # the bright edge
    p += [b([0.18, 0.3, 0.22], [0.26, h, 0.06], GUN, material="metal")]                   # the poll
    p += ring_y(0, 0.06, 2.62, 2.68, 0.21, DWOOD)
    return p, face(c, "#2b2e33")


def melee_mod(c):
    p = ring_y(0, 0.08, 0, 0.12, 0.32, DWOOD)                                             # the knob of the handle
    p += ring_y(0, 0.08, 0.12, 0.6, 0.22, WOOD)
    for i in range(3):                                                                   # the grip wrap
        p += ring_y(0, 0.08, 0.16 + i * 0.14, 0.24 + i * 0.14, 0.235, c, 8)
    for i, a in enumerate((0.25, 0.28, 0.3, 0.32)):                                     # the club, fattening upward
        p += ring_y(0, 0.08, 0.6 + i * 0.42, 1.02 + i * 0.42, a, WOOD)
    p += ring_y(0, 0.08, 2.28, 2.4, 0.27, WOOD)                                          # its rounded end
    p += ring_y(0, 0.08, 2.4, 2.46, 0.18, WOOD)
    for row, y in enumerate((1.1, 1.65, 2.2)):                                           # nails, all round but the front
        for yaw in range(60, 301, 40):
            yy = yaw + (20 if row % 2 else 0)
            if yy > 300:
                continue
            s, co = math.sin(math.radians(yy)), math.cos(math.radians(yy))
            r = 0.3 + row * 0.02
            p += [b([0.025, 0.025, 0.18], [(r + 0.06) * s, y, 0.08 - (r + 0.06) * co], STEEL, ry(-yy))]
    for yaw in (0, 90, 180, 270):                                                       # nails on top
        s, co = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        p += [b([0.025, 0.18, 0.025], [0.14 * s, 2.49, 0.08 - 0.14 * co], STEEL, mul(ry(-yaw), rx(-25)))]
    return p, face(c, "#24301c")


def action(c):
    p = ring_y(0, 0.07, 0, 0.1, 0.36, "darkgrey", 8, "metal")
    p += ring_y(0, 0.07, 0.1, 1.0, 0.18, STEEL, material="metal")                        # the guide rod
    for i in range(6):                                                                   # the coil spring
        y = 0.18 + i * 0.13
        p += [b([0.62, 0.04, 0.26], [0, y, 0.07], "#b8bec6", [6, i * 30, 0]),
              b([0.26, 0.04, 0.62], [0, y, 0.07], "#b8bec6", [6, i * 30, 0]),
              b([0.5, 0.04, 0.5], [0, y, 0.07], "#b8bec6", [6, i * 30 + 45, 0])]
    p += ring_y(0, 0.07, 0.9, 1.98, 0.28, GUN, material="metal")                         # the bolt body
    p += ring_y(0, 0.07, 1.7, 1.78, 0.29, c)
    for sx in (-1, 1):                                                                   # locking lugs
        p += [b([0.14, 0.14, 0.2], [sx * 0.24, 1.96, 0.07], STEEL, material="metal")]
    p += ring_y(0, 0.07, 1.98, 2.14, 0.11, STEEL)                                        # the bolt face
    p += ring_y(0, 0.07, 2.14, 2.22, 0.03, "#d0d0d0", 8)                                 # firing pin
    p += [b([0.26, 0.07, 0.07], [-0.36, 1.5, 0.12], STEEL, rz(-20))]                     # the bolt handle
    p += ring_x(1.45, 0.12, -0.5, -0.4, 0.07, c, 8)                                      # its knob
    return p, face(c, "#2a1a22", tray=False)


# name (the category's display name), colour (matches Game.SlotCategoryColor), the body's description, its builder.
CABINETS = [
    ("Receiver", "#d94d40", "a chunky boxy receiver with a top rail, a charging handle and its ejection port open on the right", receiver),
    ("Barrel", "#8c99ad", "a fluted gun barrel standing on end, with its muzzle crown, bore and front sight on top", barrel),
    ("Splitter", "#4dccbf", "a Y fork: a trunk and a manifold splitting into two arms with nozzle caps", splitter),
    ("Feed", "#d9a633", "a drum magazine on its edge with a curved box magazine rising from it, a round in its feed lips", feed),
    ("Ammo", "#f2bf4d", "a giant cartridge: rim, brass case, shoulder and a copper bullet on top", ammo),
    ("Stock", "#9a6b40", "a rifle stock standing on its butt pad, its top a sloped wedge with a cheek comb", stock),
    ("Grip", "#73bf59", "a raked pistol grip with finger grooves under a frame, the trigger in its open guard", grip),
    ("Optic", "#59a6ff", "a rifle scope lying across it in rings, with round lens ends and a turret", optic),
    ("Muzzle", "#ff8033", "a flared muzzle: a flared skirt, a stem and a ported bell on top", muzzle),
    ("Utility", "#ff4040", "a red tool chest on casters with drawers, a top box, a carry handle and a spanner on the lid", utility),
    ("Receiver Upgrade", "#bf80ff", "a spur gear standing on its axle with an upright gear meshing on top", upgrade),
    ("Damage Multiplier", "#ffd940", "a stepped tower with chevrons and a big x2 on top", multiplier),
    ("Hilt", "#b3804d", "a sword hilt: pommel, wrapped grip, curled cross guard and a blade stub", hilt),
    ("Head", "#ccd9e6", "an axe head on its haft, standing in a chopping block", head),
    ("Melee Mod", "#99e666", "a nail-studded club with a bright grip wrap", melee_mod),
    ("Action", "#f27399", "a rifle bolt on a coil spring, with its lugs, firing pin and bolt handle", action),
]


# The PartCategory names (and Parts/<Category> folders) of the display names above that differ from them.
KEYS = {"Receiver Upgrade": "Upgrade", "Damage Multiplier": "Multiplier", "Melee Mod": "MeleeMod"}


def main():
    # Every part category needs a cabinet: a new Parts/<Category> folder must get a body here (the self-test
    # "Zombies category slots" fails too while one is missing).
    folders = {d for d in os.listdir(os.path.join(ROOT, "Parts")) if not d.startswith("_") and d not in ("Builds", "Tables")
               and os.path.isdir(os.path.join(ROOT, "Parts", d))}
    mine = {KEYS.get(n, n) for n, *_ in CABINETS}
    if folders != mine:
        raise SystemExit(f"part categories without a cabinet: {sorted(folders - mine)}; cabinets without a category: {sorted(mine - folders)}")
    os.makedirs(OUT, exist_ok=True)
    for name, colour, what, build in CABINETS:
        body, front = build(colour)
        parts = body + front
        assert len(parts) <= 128, f"{name}: {len(parts)} parts (props take 128)"
        top = max(p["offset"][1] + p["size"][1] / 2 for p in parts)  # (near enough for a collider: turned parts are small)
        prop = {
            "format": "call-of-slop-prop/1",
            "name": f"{name} Reel Cabinet",
            "category": "Zombies",
            "description": f"A Zombies slot machine that only pays {name.lower()} parts: the Reel Cabinet's face (reel window, heat meter, bet lamps, topper lights and lever) on {what}. The front faces -Z; the game draws the reels, the heat meter and the topper lights on it.",
            "collision": "solid",
            "collider": [{"size": [0.9, round(min(top, 2.6), 2), 0.7]}],
            "scatter": {"randomYaw": False, "yawStep": 90},
            "parts": parts,
        }
        fn = name.replace(" ", "") + "ReelCabinet.json"
        parts_json = ",\n".join("    " + json.dumps(q) for q in prop.pop("parts"))
        head = "{\n" + ",\n".join(f"  {json.dumps(k)}: {json.dumps(v)}" for k, v in prop.items())  # (parts last, one per line)
        with open(os.path.join(OUT, fn), "w") as f:
            f.write(head + ',\n  "parts": [\n' + parts_json + "\n  ]\n}\n")
    print(f"wrote {len(CABINETS)} cabinets to {OUT}")


if __name__ == "__main__":
    main()
