"""Writes Maps/Space/02-Planet.json ("Pocket Planet"): a small planet with point gravity.

Run from the repo root:  python tools/planet/make_pocket_planet.py
Everything is placed by latitude/longitude (headings are degrees from geographic north, towards lat 90) with the planet ops (Planet, SurfaceBlock, SurfaceMesh, Crater,
SurfaceScatter, SurfaceSpawn, SurfaceWaypoint); objective points (team spawns, hills, flags) are written as world
coordinates, worked out here with the same formula as Map.SurfaceDir.
"""
import json
import math
import os

R = 56.0            # planet radius (m): small enough to see the curve, big enough for 16 players
C = (0.0, 0.0, 0.0)  # centre


def surf(lat, lon, above=0.05):
    a, o = math.radians(lat), math.radians(lon)
    d = (math.cos(a) * math.sin(o), math.sin(a), math.cos(a) * math.cos(o))
    r = R + above
    return [round(C[0] + d[0] * r, 3), round(C[1] + d[1] * r, 3), round(C[2] + d[2] * r, 3)]


ROCK = [0.42, 0.38, 0.36]
DUST = [0.55, 0.47, 0.40]
METAL = [0.62, 0.64, 0.68]
BLUE = [0.22, 0.38, 0.62]
RED = [0.62, 0.24, 0.20]
GLOW = [0.85, 0.80, 0.45]

ops = [
    {"op": "Planet", "radius": R, "color": DUST, "detail": 5, "material": "Dirt"},
]


def block(lat, lon, w, h, d, color, heading=0.0, y=0.0, material="Concrete", sink=0.3):
    op = {"op": "SurfaceBlock", "lat": round(lat, 3), "lon": round(lon, 3), "w": w, "h": h, "d": d, "color": color,
          "heading": round(heading, 2), "material": material}
    if y:
        op["y"] = y
    if sink != 0.3:
        op["sink"] = sink
    ops.append(op)


def mesh(kind, lat, lon, color, **kw):
    op = {"op": "SurfaceMesh", "mesh": kind, "lat": round(lat, 3), "lon": round(lon, 3), "color": color}
    op.update(kw)
    ops.append(op)


def offset(lat, lon, north_m, east_m):
    """The lat/lon a few metres north/east of a point (small offsets, fine away from the poles)."""
    return lat + math.degrees(north_m / R), lon + math.degrees(east_m / (R * max(math.cos(math.radians(lat)), 0.05)))


# ---- Pole outposts: a low-walled courtyard round each flag, four gateways, corner bunkers ----
def outpost(pole, color):
    sign = 1 if pole > 0 else -1
    ring_lat = sign * (90 - math.degrees(9.0 / R))  # 9 m from the pole
    for k in range(8):
        lon = k * 45 + 22.5
        # wall segments between the gateways (gaps on the 0/90/180/270 lines)
        block(ring_lat, lon, 5.2, 1.3, 0.8, color, material="Metal")  # heading 0: the wall runs east-west, round the pole
    for k in range(4):
        lon = k * 90 + 45
        lat2 = sign * (90 - math.degrees(14.0 / R))
        # corner bunkers: a solid block with a slit-high cap, cover facing out
        block(lat2, lon, 3.2, 2.4, 3.2, color, heading=lon, material="Concrete")
        block(lat2, lon, 3.6, 0.35, 3.6, METAL, heading=lon, y=2.4, sink=0.0, material="Metal")
    # the antenna mast beside the flag (a landmark you see over the horizon)
    mast_lat = sign * (90 - math.degrees(3.5 / R))
    mesh("cylinder", mast_lat, 135, METAL, width=0.35, depth=0.35, height=11, y=-0.2, material="Metal")
    mesh("sphere", mast_lat, 135, GLOW, width=0.8, height=0.8, depth=0.8, y=10.8, solid=False)


outpost(90, BLUE)
outpost(-90, RED)

# ---- The frame's axis points (lat 0, lon 0 and 180): two great monoliths stand on them ----
for lon in (0, 180):
    mesh("box", 0, lon, [0.20, 0.20, 0.24], width=3.2, depth=3.2, height=12, y=-0.5, heading=lon + 20, material="Metal")
    mesh("box", 0, lon, GLOW, width=3.4, depth=3.4, height=0.25, y=11.5, heading=lon + 20, solid=False)
    for k in range(6):
        a = k * 60
        la, lo = offset(0, lon, 5.5 * math.cos(math.radians(a)), 5.5 * math.sin(math.radians(a)))
        mesh("icosphere", la, lo, ROCK, width=2.2, height=1.6, depth=1.9, detail=1, y=-0.4, heading=a)

# ---- Equator: four hills between the monoliths, each a ring of low cover with a raised pad ----
HILL_LONS = [45, 135, 225, 315]
for lon in HILL_LONS:
    block(0, lon, 5, 0.5, 5, [0.30, 0.30, 0.34], heading=lon, material="Tiles")  # the pad (a step up)
    for k in range(6):
        a = k * 60 + 30
        la, lo = offset(0, lon, 7.5 * math.cos(math.radians(a)), 7.5 * math.sin(math.radians(a)))
        block(la, lo, 2.6, 1.1, 0.7, ROCK, heading=a, material="Concrete")  # across the way out from the pad

# ---- Trench lines: broken berms along latitude +-28 (cover on the way to the equator) ----
for sign in (1, -1):
    lat = sign * 28
    for k in range(18):
        lon = k * 20 + (5 if sign > 0 else 15)
        block(lat, lon, 6.0, 1.15, 1.2, DUST, material="Dirt")  # along the parallel

# ---- Towers on the 45th parallels: watch posts with stairs (steps of blocks) up to a railed deck ----
for sign, lon in ((1, 90), (1, 270), (-1, 90), (-1, 270)):
    lat = sign * 50
    block(lat, lon, 3.6, 3.0, 3.6, METAL, material="Metal")  # the deck block (3 m up)
    # stairs: five 0.5 m steps going north from the tower's foot (towards the near pole for the south ones too)
    for i in range(5):
        la, lo = offset(lat, lon, -sign * (2.6 + i * 0.85), 0)
        block(la, lo, 1.6, 3.0 - 0.5 * (i + 1), 0.9, METAL, material="Metal")
    for k in range(4):  # rails
        a = k * 90
        la, lo = offset(lat, lon, 1.7 * math.cos(math.radians(a)), 1.7 * math.sin(math.radians(a)))
        if (sign > 0 and k == 2) or (sign < 0 and k == 0):
            continue  # the side the stairs come up
        block(la, lo, 3.6 if k % 2 == 0 else 0.2, 1.0, 0.2 if k % 2 == 0 else 3.6, METAL, y=3.0, sink=0.0, material="Metal")

# ---- Craters: rings of rock round the mid-latitudes ----
for (lat, lon, r) in ((35, 0, 7), (-35, 180, 7), (40, 200, 5.5), (-40, 20, 5.5), (18, 100, 4.5), (-18, 280, 4.5), (60, 315, 4), (-60, 135, 4)):
    ops.append({"op": "Crater", "lat": lat, "lon": lon, "radius": r, "color": ROCK, "rim": 1.3, "seed": int(abs(lat) * 10 + lon)})

# ---- Spawns: 8 per team on a ring 16 m from each pole ----
blue_sp, red_sp = [], []
for sign, out in ((1, blue_sp), (-1, red_sp)):
    lat = sign * (90 - math.degrees(17.0 / R))
    for k in range(8):
        lon = k * 45 + (0 if sign > 0 else 22.5)
        ops.append({"op": "SurfaceSpawn", "lat": round(lat, 3), "lon": lon})
        out.append(surf(lat, lon))

# ---- Boulders all over (kept clear of spawns), and roaming waypoints ----
ops.append({"op": "SurfaceScatter", "mesh": "icosphere", "count": 110, "color": ROCK, "minSize": 0.9, "maxSize": 2.6, "seed": 7,
            "latMin": -72, "latMax": 72, "clear": 5})
ops.append({"op": "PlanetWaypoints", "spacing": 12})

hills = [{"x": p[0], "y": p[1], "z": p[2], "radius": 6} for p in (surf(0, lon, 0.55) for lon in HILL_LONS)]

planet = {
    "format": "call-of-slop-map/1",
    "name": "Pocket Planet",
    "description": "A tiny world you can walk all the way round. Gravity pulls to its centre; the horizon hides who's coming.",
    "author": "Call of Slop",
    "order": 2,
    "halfX": 90,
    "halfZ": 90,
    "maxPlayers": 16,
    "gravity": {"type": "point", "center": list(C), "strength": 18, "axis": [0, 0, -1]},
    "skybox": {
        "preset": "night",
        "zenith": "#05060c", "horizon": "#05060c", "ground": "#05060c",
        "sun": {"direction": [0.8, 0.12, 0.59], "intensity": 1.15, "size": 2.2},
        "moon": {"direction": [-0.55, 0.45, -0.7], "size": 26, "color": "#c9a27a", "intensity": 0.4},
        "stars": 1.8,
        "clouds": False,
        "ambient": 1.9,
    },
    "teamSpawns": [blue_sp, red_sp],
    "hills": hills,
    "flags": [surf(90, 0, 0.05), surf(-90, 0, 0.05)],
    "ops": ops,
}

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.normpath(os.path.join(here, "..", "..", "Maps", "Space", "02-Planet.json"))
with open(out, "w", newline="\n") as f:
    f.write(json.dumps(planet, indent=2) + "\n")
print(f"wrote {out}: {len(ops)} ops")
