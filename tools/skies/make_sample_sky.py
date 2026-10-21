"""Makes the sample image sky in Maps/Dev/Skies: one golden-hour sky over a mountain range, written twice so both
image paths are covered: as a cubemap (sample_px.png ... sample_nz.png, 6 x 128x128) and as an equirect HDR panorama
(sample.hdr, 256x128, Radiance RGBE with run-length encoding). Standard library only, deterministic.

The PNGs hold the colour as shown; the .hdr holds the linear value the game's HDR tone mapping (1 - exp(-c * exposure))
turns back into the same colour at exposure 1, so both skies look alike in the game.

    python tools/skies/make_sample_sky.py            (from the repository root)
"""
import math
import os
import struct
import zlib

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Maps", "Dev", "Skies")
FACE = 128
PANO_W, PANO_H = 256, 128

SUN_AZ, SUN_EL = math.radians(250), math.radians(11)  # compass azimuth (0 = north = -Z, 90 = east = +X)
SUN = (math.cos(SUN_EL) * math.sin(SUN_AZ), math.sin(SUN_EL), -math.cos(SUN_EL) * math.cos(SUN_AZ))


def lerp(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def smooth(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def hash2(ix, iy, seed):
    h = (ix * 374761393 + iy * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def vnoise(x, y, seed, wrap):
    ix, iy = math.floor(x), math.floor(y)
    fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a = hash2(ix % wrap, iy, seed)
    b = hash2((ix + 1) % wrap, iy, seed)
    c = hash2(ix % wrap, iy + 1, seed)
    d = hash2((ix + 1) % wrap, iy + 1, seed)
    return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy


def fbm(x, y, seed, wrap, octaves=4):
    v, amp, tot = 0.0, 0.5, 0.0
    for o in range(octaves):
        v += vnoise(x, y, seed + o, wrap) * amp
        tot += amp
        x, y, wrap, amp = x * 2, y * 2, wrap * 2, amp * 0.5
    return v / tot


def sky(d):
    """Displayed colour (0-1) and an extra HDR boost for the sun, in direction d (unit)."""
    x, y, z = d
    az = math.atan2(x, -z) % (2 * math.pi)
    el = math.asin(max(-1.0, min(1.0, y)))
    zen, hor, low = (0.13, 0.23, 0.48), (0.98, 0.68, 0.45), (0.20, 0.17, 0.22)
    c = lerp(hor, zen, math.sqrt(max(y, 0.0))) if y >= 0 else lerp(hor, low, smooth(0, 0.25, -y))
    s = max(0.0, x * SUN[0] + y * SUN[1] + z * SUN[2])
    glow = s ** 8 * 0.45 + s ** 90 * 0.5
    c = lerp(c, (1.0, 0.82, 0.55), min(glow, 1.0))
    # Streaky clouds between 6 and 30 degrees up, lit warm on the sun's side.
    u = az / (2 * math.pi) * 24
    cl = fbm(u, el * 9.0, 11, 24)
    band = smooth(0.06, 0.16, el) * (1 - smooth(0.32, 0.55, el))
    a = smooth(0.52, 0.68, cl) * band * 0.85
    cloud = lerp((0.42, 0.30, 0.38), (1.0, 0.72, 0.50), 0.35 + 0.65 * s ** 3)
    c = lerp(c, cloud, a)
    # Mountains: a hazy far range up to ~9 degrees and a darker near ridge in front of it.
    far = 0.02 + 0.24 * fbm(az / (2 * math.pi) * 7, 0.5, 5, 7, 5) ** 1.5
    near = 0.008 + 0.10 * fbm(az / (2 * math.pi) * 13, 3.5, 23, 13, 5) ** 1.3
    if el < near:
        c = (0.17, 0.14, 0.21)
    elif el < far:
        c = lerp((0.30, 0.23, 0.33), hor, 0.15 + 0.3 * smooth(far - 0.08, far, el))
    boost = 9.0 * smooth(math.cos(math.radians(1.3)), math.cos(math.radians(1.0)), s) if el > far else 0.0
    return tuple(min(v, 0.98) for v in c), boost


def face_dir(face, sc, tc):
    # OpenGL cube map faces: +X, -X, +Y, -Y, +Z, -Z (row 0 = top of the face)
    return [(1, -tc, -sc), (-1, -tc, sc), (sc, 1, tc), (sc, -1, -tc), (sc, -tc, 1), (-sc, -tc, -1)][face]


def norm(v):
    l = math.sqrt(sum(a * a for a in v))
    return tuple(a / l for a in v)


def write_png(path, w, h, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)

    def chunk(t, data):
        c = struct.pack(">I", len(data)) + t + data
        return c + struct.pack(">I", zlib.crc32(t + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


def rgbe(r, g, b):
    m = max(r, g, b)
    if m < 1e-32:
        return (0, 0, 0, 0)
    mant, e = math.frexp(m)
    f = mant * 256.0 / m
    return (int(r * f), int(g * f), int(b * f), e + 128)


def rle(values):
    out, i, n = bytearray(), 0, len(values)
    while i < n:
        j = i
        while j < n and j - i < 127 and values[j] == values[i]:
            j += 1
        if j - i >= 3:
            out += bytes((128 + j - i, values[i]))
            i = j
            continue
        j = i
        while j < n and j - i < 128 and not (j + 2 < n and values[j] == values[j + 1] == values[j + 2]):
            j += 1
        out += bytes((j - i,)) + bytes(values[i:j])
        i = j
    return bytes(out)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f, name in enumerate(["px", "nx", "py", "ny", "pz", "nz"]):
        rows = []
        for j in range(FACE):
            row = []
            for i in range(FACE):
                d = norm(face_dir(f, (i + 0.5) / FACE * 2 - 1, (j + 0.5) / FACE * 2 - 1))
                (r, g, b), boost = sky(d)
                if boost > 0:
                    r, g, b = 1.0, 0.97, 0.88
                row += [round(r * 255), round(g * 255), round(b * 255)]
            rows.append(row)
        write_png(os.path.join(OUT, f"sample_{name}.png"), FACE, FACE, rows)
    body = bytearray(f"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {PANO_H} +X {PANO_W}\n".encode())
    for j in range(PANO_H):
        theta = (j + 0.5) / PANO_H * math.pi
        line = []
        for i in range(PANO_W):
            phi = ((i + 0.5) / PANO_W - 0.5) * 2 * math.pi
            d = (math.sin(phi) * math.sin(theta), math.cos(theta), -math.cos(phi) * math.sin(theta))
            (r, g, b), boost = sky(d)
            lin = [-math.log(1 - v) for v in (r, g, b)]  # the game shows 1 - exp(-c)
            line.append(rgbe(*(v + boost for v in lin)))
        body += bytes((2, 2, PANO_W >> 8, PANO_W & 255))
        for ch in range(4):
            body += rle([p[ch] for p in line])
    open(os.path.join(OUT, "sample.hdr"), "wb").write(bytes(body))
    for n in sorted(os.listdir(OUT)):
        print(f"{n}: {os.path.getsize(os.path.join(OUT, n))} bytes")


if __name__ == "__main__":
    main()
