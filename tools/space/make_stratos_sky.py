#!/usr/bin/env python3
"""Paints the Space pack's sky-station panorama: Maps/Space/Skies/stratos_dusk.png, an equirectangular sky (2:1; the
middle column looks north, -Z; the top row straight up) for a gas giant's upper atmosphere at dusk: a violet zenith, a
low sun in the west behind a band of storm clouds, and a sea of clouds below the horizon, lit orange on the sun's side
and lavender away from it (the map's procedural sky can't draw anything below the horizon, so the image does).

    python3 tools/space/make_stratos_sky.py [out.png] [width]

Deterministic (fixed seeds). Pure numpy + Pillow; about 10 s for 2048 x 1024.
"""
import math
import os
import sys

import numpy as np
from PIL import Image

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, "Maps", "Space", "Skies", "stratos_dusk.png")
W = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
H = W // 2

SUN_AZ = 262.0   # compass degrees (0 = north, 90 = east): a little south of west
SUN_EL = 4.5     # degrees above the horizon


def hexc(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def lattice(seed, n=256):
    rng = np.random.default_rng(seed)
    return rng.random((n, n)).astype(np.float32)


def value2(tab, x, z):
    n = tab.shape[0]
    xi = np.floor(x).astype(np.int64)
    zi = np.floor(z).astype(np.int64)
    fx = x - xi
    fz = z - zi
    fx = fx * fx * (3 - 2 * fx)
    fz = fz * fz * (3 - 2 * fz)
    x0 = xi % n
    z0 = zi % n
    x1 = (x0 + 1) % n
    z1 = (z0 + 1) % n
    a = tab[x0, z0] * (1 - fx) + tab[x1, z0] * fx
    b = tab[x0, z1] * (1 - fx) + tab[x1, z1] * fx
    return a * (1 - fz) + b * fz


def fbm(tab, x, z, octaves=6, lac=2.03, gain=0.5):
    s = np.zeros_like(x, dtype=np.float32)
    amp = 0.5
    total = 0.0
    for o in range(octaves):
        s += amp * value2(tab, x + o * 17.3, z - o * 9.1)
        total += amp
        x = x * lac + 3.7
        z = z * lac - 1.9
        amp *= gain
    return s / total


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def main():
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    az = (U - 0.5) * 2 * math.pi           # atan2(x, -z)
    pol = V * math.pi                       # acos(y)
    y = np.cos(pol)
    r = np.sin(pol)
    x = r * np.sin(az)
    z = -r * np.cos(az)
    sa = math.radians(SUN_AZ)
    se = math.radians(SUN_EL)
    sun = np.array([math.cos(se) * math.sin(sa), math.sin(se), -math.cos(se) * math.cos(sa)])
    cosang = x * sun[0] + y * sun[1] + z * sun[2]
    toward = np.clip((cosang + 1) / 2, 0, 1)            # 1 facing the sun, 0 away
    flat_toward = np.clip((x * sun[0] + z * sun[2]) / np.maximum(r, 1e-4) / math.cos(se), -1, 1) * 0.5 + 0.5

    img = np.zeros((H, W, 3), dtype=np.float32)

    # ---- sky above the horizon -------------------------------------------------------------------------------
    zen = hexc("#1d2346")
    mid = hexc("#5a4c72")
    hor_warm = hexc("#ff9a52")
    hor_cool = hexc("#b87e8e")
    up = np.clip(y, 0, 1)
    hor = hor_cool[None, None, :] * (1 - flat_toward[..., None] ** 2) + hor_warm[None, None, :] * flat_toward[..., None] ** 2
    t1 = smooth(0.0, 0.35, up)[..., None]
    t2 = smooth(0.25, 1.0, up)[..., None]
    sky = hor * (1 - t1) + mid[None, None, :] * t1
    sky = sky * (1 - t2) + zen[None, None, :] * t2
    # Sun glow and disc.
    ang = np.arccos(np.clip(cosang, -1, 1))
    glow = np.exp(-ang / 0.09) * 1.1 + np.exp(-ang / 0.35) * 0.45
    sky += glow[..., None] * hexc("#ffb060")[None, None, :]
    disc = smooth(0.016, 0.011, ang)
    sky = sky * (1 - disc[..., None]) + disc[..., None] * hexc("#fff4dc")[None, None, :] * 1.4

    # Storm clouds on a layer 2 km up (perspective: far clouds bunch up at the horizon).
    tab = lattice(11)
    yy = np.maximum(y, 0.02)
    px = x / yy * 0.9
    pz = z / yy * 0.9
    n = fbm(tab, px * 1.2 + 40, pz * 1.2 + 40, octaves=6)
    n2 = fbm(lattice(12), px * 3.5, pz * 3.5, octaves=4)
    dens = smooth(0.46, 0.68, n * 0.8 + n2 * 0.25) * smooth(0.0, 0.06, y)
    dens *= 1 - 0.55 * smooth(0.55, 0.95, y)                       # thinner overhead
    dens *= 0.3 + 0.7 * smooth(0.03, 0.16, y)                      # the far band melts into haze
    shade = hexc("#2f2840")
    lit_c = hexc("#ff8c48")
    rim = np.clip(np.exp(-ang / 0.6) * 1.6, 0, 1) * smooth(0.55, 0.75, n2 + 0.2 * n)
    cloud = shade[None, None, :] * (1 - rim[..., None]) + lit_c[None, None, :] * rim[..., None]
    cloud = cloud * (0.75 + 0.5 * (1 - smooth(0.5, 0.8, n))[..., None])
    sky = sky * (1 - dens[..., None] * 0.92) + cloud * dens[..., None] * 0.92

    # ---- the cloud sea below the horizon ---------------------------------------------------------------------
    dn = np.maximum(-y, 0.004)
    qx = x / dn * 0.35
    qz = z / dn * 0.35
    s1 = fbm(lattice(21), qx + 100, qz + 100, octaves=7)
    s2 = fbm(lattice(22), qx * 2.7 - 50, qz * 2.7 + 20, octaves=5)
    tops = smooth(0.36, 0.66, s1 * 0.75 + s2 * 0.35)                # 1 on billow tops, 0 in the gaps
    sea_lit = hexc("#ffb27a") * 0.95
    sea_mid = hexc("#c48a9a")
    sea_cool = hexc("#7d7aa8")
    sea_gap = hexc("#3e3a62")
    side = flat_toward[..., None] ** 1.5
    top_c = sea_lit[None, None, :] * side + sea_cool[None, None, :] * (1 - side)
    top_c = top_c * 0.7 + sea_mid[None, None, :] * 0.3
    sea = sea_gap[None, None, :] * (1 - tops[..., None]) + top_c * tops[..., None]
    # Sunlit sheen on the near-horizon clouds towards the sun.
    sheen = np.exp(-ang / 0.25) * smooth(0.0, 0.25, -y + 0.25) * 0.8
    sea += sheen[..., None] * hexc("#ff9a50")[None, None, :] * tops[..., None]
    # Haze into the horizon colour near the horizon (the far sea fades into the glow).
    hz = smooth(0.0, 0.22, -y)[..., None]
    sea = hor * 0.95 * (1 - hz) + sea * hz
    below = (y < 0)[..., None]
    img = np.where(below, sea, sky)

    img = np.clip(img, 0, 1) ** (1 / 1.05)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGB").save(OUT, optimize=True)
    print(f"wrote {OUT} ({W} x {H}); sun at azimuth {SUN_AZ}, elevation {SUN_EL}")


if __name__ == "__main__":
    main()
