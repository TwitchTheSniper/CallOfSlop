#!/usr/bin/env python3
"""The story's planetoids (docs/planets/WORLDS.md): writes Maps/Planetoids/<Name>.json for each world below.

A world is its ground (the "planetoid" features, docs/planets/PLANETS.md), its colours, its sky and three flat places:
the landing ground at the north pole (where spawns go), its faction's home and its data centre's site. The places are
flattened last, so they are level whatever the ground does round them; WORLDS.md lists where they are.

    python3 tools/planets/make_worlds.py            # write every world
    dotnet bin/Release/net10.0/CallOfSlop.dll --check-planetoid Maps/Planetoids/Caledon.json --png caledon.png --at 62,140

Change a world here, never in its map file.
"""
import json
import math
import os

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Maps", "Planetoids")


def noise(amplitude, scale, octaves=4, seed=1, **more):
    return {"type": "noise", "amplitude": amplitude, "scale": scale, "octaves": octaves, "seed": seed, **more}


def mountain(lat, lon, radius, height, seed, ridges=0.6, erosion=0.7, warp=0.35):
    return {"type": "mountain", "lat": lat, "lon": lon, "radius": radius, "height": height, "seed": seed, "ridges": ridges, "erosion": erosion, "warp": warp}


def crater(lat, lon, radius, depth, rim=None):
    f = {"type": "crater", "lat": lat, "lon": lon, "radius": radius, "depth": depth}
    if rim is not None:
        f["rim"] = rim
    return f


def rift(lat, lon, bearing, length, width, depth, floor=0.3, wander=0, seed=1):
    return {"type": "rift", "lat": lat, "lon": lon, "bearing": bearing, "length": length, "width": width, "depth": depth, "floor": floor, "wander": wander, "seed": seed}


def terrace(step, sharp=0.8, **place):
    return {"type": "terrace", "step": step, "sharp": sharp, **place}


def ridge(lat, lon, bearing, length, width, height, peaks=0.5, rough=0.15, wander=0, seed=1):
    return {"type": "ridge", "lat": lat, "lon": lon, "bearing": bearing, "length": length, "width": width, "height": height, "peaks": peaks, "rough": rough, "wander": wander, "seed": seed}


def crevasses(lat, lon, radius, bearing, spacing=60, length=90, width=8, depth=24, share=0.5, spread=20, edge=None, seed=1):
    f = {"type": "crevasses", "lat": lat, "lon": lon, "radius": radius, "bearing": bearing, "spacing": spacing, "length": length, "width": width, "depth": depth, "share": share, "spread": spread, "seed": seed}
    if edge is not None:
        f["edge"] = edge
    return f


def drifts(lat, lon, radius, bearing, amplitude, scale, stretch=6, octaves=2, edge=None, seed=1):
    f = {"type": "drifts", "lat": lat, "lon": lon, "radius": radius, "bearing": bearing, "amplitude": amplitude, "scale": scale, "stretch": stretch, "octaves": octaves, "seed": seed}
    if edge is not None:
        f["edge"] = edge
    return f


def cave(lat, lon, bearing, radius=2.6, chamber=8, cover=14, climb=8, seed=1):
    """A cave dug when a fresh world starts (PLANETS.md "Caves"): a chamber under (lat, lon) and a tunnel out along bearing."""
    return {"type": "cave", "lat": lat, "lon": lon, "bearing": bearing, "radius": radius, "chamber": chamber, "cover": cover, "climb": climb, "seed": seed}


def away(radius, lat, lon, keep):
    """True when (lat, lon) is further than each kept place's own distance (m) from it: keep is [(lat, lon, metres)]."""
    a = (math.cos(math.radians(lat)) * math.sin(math.radians(lon)), math.sin(math.radians(lat)), math.cos(math.radians(lat)) * math.cos(math.radians(lon)))
    for klat, klon, m in keep:
        b = (math.cos(math.radians(klat)) * math.sin(math.radians(klon)), math.sin(math.radians(klat)), math.cos(math.radians(klat)) * math.cos(math.radians(klon)))
        if math.acos(max(-1.0, min(1.0, sum(x * y for x, y in zip(a, b))))) * radius < m:
            return False
    return True


def reach(f):
    """How far from its centre a feature can change the ground (m), as Planetoid.ReachOf works it out."""
    if f["type"] == "ridge":
        return math.hypot(f["length"] / 2, f["width"] / 2 + f.get("wander", 0)) + 2
    return f["radius"] + f.get("edge", f["radius"] * 0.3) + 2


def scattered(radius, seed, count, keep, make):
    """count features spread over the world by a seeded draw (even in area), each make(rng, lat, lon, k), none reaching
    a kept place."""
    import random
    rng = random.Random(seed)
    out = []
    while len(out) < count:
        lat = round(math.degrees(math.asin(rng.uniform(-1, 1))), 2)
        lon = round(rng.uniform(0, 360), 2)
        f = make(rng, lat, lon, len(out))
        if away(radius, lat, lon, [(a, b, m + reach(f)) for a, b, m in keep]):
            out.append(f)
    return out


# Glacier's places that keep their ground: the colony round the pole with its quarry, stations and data centre (all within
# about 450 m; the new ground starts past 700 m), the Ministry Spaceport about 2 km out along longitude 90, the Server Farm
# on the Mirror and the data centre in the Rent (both flattened last anyway), metres of clear ground round each.
GLACIER_KEEP = [(90, 0, 1600), (76.4, 90, 500), (-10, 320, 900), (15, 170, 300)]


def glacier_terrain():
    """Glacier's rough country (docs/planets/WORLDS.md "Glacier's ground"): near the colony a ridge, a pressure-ridge field
    and a crevasse field, each within a short walk; over the whole world ice ridges with passes, crevasse fields, pressure
    ridges and wind drifts. Nothing reaches within 700 m of the pole or the spaceport's ground."""
    r = 8000
    near = [
        ridge(81.5, 285, 95, 2600, 340, 110, peaks=0.65, rough=0.2, wander=120, seed=61),  # the Spine: west of the colony
        drifts(79.5, 210, 500, 30, 9, 45, stretch=7, edge=250, seed=62),  # the Rucks: pressure ridges, south-west
        crevasses(80, 150, 450, 60, spacing=55, length=100, width=9, depth=24, share=0.5, spread=18, edge=150, seed=63),  # the Teeth
        cave(81.75, 282, 0, chamber=10, cover=16, climb=6, seed=65),  # the Spine's ice cave, opening towards the colony
    ]
    world = []
    world += scattered(r, 101, 9, GLACIER_KEEP, lambda g, lat, lon, k: ridge(
        lat, lon, round(g.uniform(0, 180)), round(g.uniform(2200, 4600)), round(g.uniform(220, 420)), round(g.uniform(50, 150)),
        peaks=round(g.uniform(0.5, 0.8), 2), rough=0.2, wander=round(g.uniform(80, 260)), seed=200 + k))
    world += [cave(f["lat"], f["lon"], (f["bearing"] + 90 + 180 * (k % 2)) % 360, chamber=round(6 + k % 4 * 2), seed=500 + k)
              for k, f in enumerate(world) if f["height"] >= 70]  # (a cave under each tall ridge, out of one flank or the other)
    world += scattered(r, 102, 12, GLACIER_KEEP, lambda g, lat, lon, k: crevasses(
        lat, lon, round(g.uniform(400, 900)), round(g.uniform(0, 180)), spacing=round(g.uniform(45, 75)), length=round(g.uniform(70, 140)),
        width=round(g.uniform(6, 12)), depth=round(g.uniform(15, 35)), share=round(g.uniform(0.4, 0.65), 2), spread=20, seed=300 + k))
    world += scattered(r, 103, 7, GLACIER_KEEP, lambda g, lat, lon, k: drifts(
        lat, lon, round(g.uniform(600, 1400)), round(g.uniform(0, 180)), round(g.uniform(5, 11), 1), round(g.uniform(35, 60)), stretch=7, seed=400 + k))
    # Sastrugi: wind-cut drifts half a metre high on every bit of open snow, fading out short of the colony.
    world.append(drifts(-90, 0, 23500, 60, 0.5, 6, stretch=6, octaves=1, edge=800, seed=64))
    return near + world


def flatten(lat, lon, radius, height, edge):
    return {"type": "flatten", "lat": lat, "lon": lon, "radius": radius, "height": height, "edge": edge}


FINE = noise(0.5, 12, 2, 9)  # the grain under your feet, on every world

WORLDS = [
    {
        "name": "Redrock", "order": 1, "radius": 4000, "strength": 9,
        "ore": {"iron vein": 2, "sand bed": 2, "gold vein": 0.5, "helium pocket": 0.5},
        "description": "Redrock, a 4 km red desert in the home system (Systems/Dev.json): stepped mesas in the north, the Gash (a canyon 5 km long) across the middle and a dune sea in the south. A Ministry mining concession, and a practice landing before the first jump.",
        "surface": {"grass": "#b5653a", "dirt": "#9c4f2e", "rock": "#7a3b26", "sand": "#d9a066", "dirtSlope": 20, "rockSlope": 32, "sandBelow": -30},
        "skybox": {"preset": "desert", "zenith": "#b98a5e", "horizon": "#e2b98a", "ground": "#8a5a3c",
                   "sun": {"color": "#fff0d8", "intensity": 1.0}, "clouds": False, "haze": {"color": "#d9a878", "strength": 0.7}},
        "features": [
            noise(85, 1800, 5, 41),
            noise(30, 520, 4, 3, ridged=True),
            mountain(48, 250, 900, 420, 17, ridges=0.4, erosion=0.5),
            crater(-20, 95, 520, 85),
            crater(35, 330, 240, 50),
            crater(-62, 210, 330, 60),
            mountain(-30, 20, 1100, 380, 18, ridges=0.3, erosion=0.4),
            terrace(34, 0.9, lat=-25, lon=30, radius=1500, edge=700),  # the Steps: a stepped massif
            terrace(26, 0.86, lat=55, lon=60, radius=2600, edge=1400),  # mesa country
            rift(8, 190, 72, 5200, 460, 250, floor=0.35, wander=320, seed=5),  # the Gash
            rift(20, 215, 20, 1700, 240, 150, floor=0.25, wander=160, seed=8),  # its side canyon
            rift(-8, 160, 150, 1500, 200, 120, floor=0.25, wander=140, seed=11),
            noise(7, 55, 2, 23, ridged=True, lat=-45, lon=300, radius=1900, edge=900),  # the dune sea
            noise(5, 140, 4, 7),
        ],
        "landing": 14,
        "sites": [("home", 55, 60, 90, 26, 160), ("data", 8, 190, 70, -236, 90)],
    },
    {
        "name": "Cinder", "order": 2, "radius": 5000, "strength": 12,
        "ore": {"copper vein": 2, "gold vein": 1.5, "stone seam": 1.5, "helium pocket": 0, "sand bed": 0.5},
        "limit": (60, "the ash flats swallow anything heavier"),
        "description": "Cinder, a 5 km scorched rock close to the red dwarf Ember (Systems/Ember.json), one face always to its sun: black basalt, craters, lava plains in the low ground and long fissures. The Call Centre works from the Call Floor, a flat-bottomed crater on the twilight line.",
        "surface": {"grass": "#3b3331", "dirt": "#4d3a33", "rock": "#221d1d", "sand": "#a3472a", "dirtSlope": 22, "rockSlope": 34, "sandBelow": -28},
        "skybox": {"preset": "dusk", "zenith": "#1a0d10", "horizon": "#6e2a1c", "ground": "#1c1414", "stars": 1.4,
                   "sun": {"color": "#ff8a5a", "intensity": 1.1, "size": 5, "glow": 0.9}, "clouds": False,
                   "haze": {"color": "#7a2f20", "strength": 0.6}, "ambient": 0.8},
        "features": [
            noise(75, 2600, 5, 61),
            noise(45, 800, 4, 14, ridged=True),
            mountain(-35, 20, 1300, 620, 21, ridges=0.7, erosion=0.4),
            mountain(25, 250, 1500, 520, 22, ridges=0.5, erosion=0.3),  # the day side's shield
            noise(60, 700, 4, 33, ridged=True, lat=0, lon=220, radius=3200, edge=2200),  # the day side is broken ground
            crater(12, 130, 900, 150, 45),  # the Call Floor (its floor is flattened below)
            crater(55, 200, 620, 110),
            crater(-15, 300, 480, 95),
            crater(-55, 150, 760, 130),
            crater(30, 10, 300, 70),
            crater(70, 320, 380, 80),
            crater(-40, 250, 220, 55),
            crater(5, 170, 180, 45),
            crater(-5, 20, 340, 75),
            crater(40, 60, 520, 100),
            crater(-45, 80, 300, 70),
            crater(20, 350, 200, 50),
            crater(-70, 30, 420, 90),
            crater(-10, 50, 260, 120, 30),  # the night side's deep one: the data centre's
            crater(42, 120, 140, 40),
            crater(-25, 110, 160, 42),
            rift(20, 240, 35, 3600, 140, 160, floor=0.2, wander=260, seed=3),
            rift(-35, 330, 110, 2800, 120, 140, floor=0.2, wander=220, seed=7),
            rift(60, 70, 80, 2200, 110, 120, floor=0.2, wander=180, seed=9),
            noise(4, 110, 4, 5),
        ],
        "landing": 10,
        "sites": [("home", 12, 130, 420, -150, 200), ("data", -10, 50, 70, -128, 60)],
    },
    {
        "name": "Glacier", "order": 3, "radius": 8000, "strength": 14,
        "ore": {"helium pocket": 2.5, "copper vein": 1.5, "sand bed": 0, "iron vein": 0.7},
        "description": "Glacier, the 8 km ice world of Frostreach (Systems/Frostreach.json): snowfields, pressure ridges of blue ice, two black nunataks and the Rent, a crevasse 9 km long. The IT Department keeps its Server Farm here, for the cooling.",
        "surface": {"grass": "#e4ecf3", "dirt": "#c4d6e6", "rock": "#5a86ad", "sand": "#a6c5de", "dirtSlope": 18, "rockSlope": 30, "sandBelow": -18,
                    # (made ground is dark cinder with snow blown over it, not the blue of the ice: the colony's bench)
                    "stone": "#3d3f44", "dusting": 0.8,
                    # (wind-scoured patches of bare blue ice on the open snow)
                    "ice": "#9ec3df", "icePatches": 0.22, "iceScale": 90},
        "skybox": {"preset": "alpine", "zenith": "#6f9fd0", "horizon": "#dbe8f2", "ground": "#c9d6e0",
                   "sun": {"color": "#f4f8ff", "intensity": 1.05, "size": 1.2}, "haze": {"color": "#e6eef5", "strength": 0.55}, "ambient": 1.25},
        "storm": {"dusk": 0.85},  # the cold storm outdoors (Maps/README.md "Storm", docs/weather/WEATHER.md "Cold storm")
        "features": [
            noise(80, 4200, 5, 71),
            noise(28, 650, 4, 19, ridged=True),
            mountain(35, 80, 2000, 950, 31, ridges=0.8, erosion=0.5),
            mountain(-50, 230, 1600, 760, 37, ridges=0.8, erosion=0.5),
            mountain(55, 250, 1200, 520, 38, ridges=0.8, erosion=0.5),
            mountain(-25, 40, 1400, 600, 39, ridges=0.8, erosion=0.5),
            noise(22, 260, 3, 27, ridged=True, lat=0, lon=100, radius=3500, edge=2500),  # the Shatter: a field of ice ridges
            terrace(14, 0.9, lat=-45, lon=320, radius=2600, edge=1500),  # the Shelves: ice in steps
            crater(-10, 320, 1100, 60, 20),  # the Mirror: a frozen lake (flattened below)
            rift(15, 170, 60, 9000, 320, 330, floor=0.25, wander=520, seed=13),  # the Rent
            rift(40, 200, 150, 2400, 70, 85, floor=0.15, wander=120, seed=2),
            rift(-20, 140, 20, 2000, 60, 75, floor=0.15, wander=110, seed=4),
            rift(60, 300, 100, 2600, 80, 90, floor=0.15, wander=140, seed=6),
            rift(-60, 60, 40, 2200, 66, 80, floor=0.15, wander=120, seed=8),
            rift(5, 20, 130, 1800, 56, 70, floor=0.15, wander=100, seed=10),
            rift(75, 120, 10, 1600, 60, 70, floor=0.15, wander=90, seed=12),
            *glacier_terrain(),
            noise(3, 90, 3, 5),
        ],
        "landing": 20,
        "sites": [("home", -10, 320, 700, -62, 300), ("data", 15, 170, 80, -318, 70)],
    },
    {
        "name": "Caledon", "order": 4, "radius": 6000, "strength": 11,
        "ore": {"stone seam": 2, "iron vein": 1.5, "sand bed": 0.5},
        "description": "Caledon, the 6 km highland world of Glenmore (Systems/Glenmore.json): steep bens with snow on their tops, long glens between them, heather on the slopes and bog in the low ground. Clan MacSlop's Glen runs through its middle.",
        "surface": {"grass": "#56733f", "dirt": "#75597a", "rock": "#5d5f66", "sand": "#4a4a33", "snow": "#e8eef0", "dirtSlope": 17, "rockSlope": 36, "sandBelow": -70, "snowAbove": 520},
        "skybox": {"preset": "overcast", "zenith": "#7d8a96", "horizon": "#b9c2c8", "ground": "#5b6a55",
                   "sun": {"color": "#fff4dc", "intensity": 0.85}, "clouds": {"coverage": 0.72, "density": 0.6, "altitude": 1600, "speed": 14},
                   "haze": {"color": "#aab6bd", "strength": 0.75}},
        "features": [
            noise(120, 2400, 6, 81),
            noise(110, 1300, 5, 12, ridged=True),  # hills everywhere
            mountain(62, 140, 1700, 940, 43, ridges=0.8, erosion=0.8),  # Ben Slop, the tallest
            mountain(40, 100, 1300, 700, 44, ridges=0.8, erosion=0.8),
            mountain(48, 190, 1200, 640, 45, ridges=0.7, erosion=0.8),
            mountain(10, 300, 1500, 780, 46, ridges=0.8, erosion=0.8),
            mountain(-25, 30, 1400, 720, 47, ridges=0.8, erosion=0.7),
            mountain(-40, 200, 1600, 820, 48, ridges=0.8, erosion=0.8),
            mountain(-65, 110, 1100, 560, 49, ridges=0.7, erosion=0.8),
            mountain(20, 240, 1000, 520, 50, ridges=0.7, erosion=0.8),
            mountain(70, 300, 1200, 600, 52, ridges=0.8, erosion=0.8),
            mountain(-10, 150, 1100, 540, 53, ridges=0.7, erosion=0.8),
            mountain(-55, 320, 1300, 680, 54, ridges=0.8, erosion=0.8),
            mountain(35, 20, 1200, 620, 55, ridges=0.8, erosion=0.8),
            rift(30, 150, 55, 6200, 620, 190, floor=0.4, wander=420, seed=21),  # the Glen
            rift(52, 160, 140, 2600, 420, 150, floor=0.35, wander=260, seed=22),
            rift(-30, 110, 100, 4400, 520, 170, floor=0.4, wander=360, seed=23),
            rift(0, 270, 10, 3200, 460, 160, floor=0.35, wander=300, seed=24),
            noise(4, 100, 4, 5),
        ],
        "landing": 30,
        "sites": [("home", 30, 150, 160, -100, 220), ("data", 60, 152, 70, 640, 140)],
    },
    {
        "name": "Mille", "order": 5, "radius": 3000, "strength": 8,
        "ore": {"levite seam": 3, "gold vein": 2, "sand bed": 1.5, "iron vein": 0.5},
        "limit": (40, "the crust is thin over the cavity"),
        "description": "Mille, the 3 km hollow world of Hearth (Systems/Hearth.json): a golden crust stepped in layers all the way round, 220 m thick, over a cavity that glows like an oven. Its terrace stands empty for now: no faction lives here yet. The crust is thinnest under the Proving Bowl: dig there to get in.",
        "surface": {"grass": "#c99a4e", "dirt": "#a8733a", "rock": "#7d4f28", "sand": "#e8cf8f", "dirtSlope": 16, "rockSlope": 30, "sandBelow": -22},
        "skybox": {"preset": "dusk", "zenith": "#5e6f9a", "horizon": "#f0c48a", "ground": "#8a6a3c",
                   "sun": {"color": "#ffd9a0", "intensity": 1.0, "size": 1.8, "glow": 0.5}, "haze": {"color": "#e8b878", "strength": 0.6}},
        "cavity": {"crust": 220, "glow": "#ffb347", "core": 60},
        "features": [
            noise(42, 900, 5, 91),
            mountain(35, 250, 520, 110, 51, ridges=0.3, erosion=0.5),  # the Loaf
            crater(70, 40, 80, 70, 14),  # the Proving Bowl
            crater(-30, 120, 260, 30),
            terrace(9, 0.72),  # the layers
            noise(2.5, 70, 3, 5),
        ],
        "landing": 9,
        "sites": [("home", 55, 300, 90, 9, 120), ("data", 70, 40, 30, -66, 30)],
    },
    {
        "name": "Slopolis", "order": 6, "radius": 3500, "strength": 10,
        "ore": {"iron vein": 0.5, "copper vein": 0.5},
        "description": "Slopolis, the free port: a 3.5 km world whose north is one city, built by everybody and owned by nobody. Towers in the middle, old brick blocks at the edge, a square at the pole with a column in it, and lamps coming on at the blue hour. Hills and old quarries beyond the last street (docs/planets/CITY.md).",
        "surface": {"grass": "#5d6b4a", "dirt": "#6b5d4c", "rock": "#5a5752", "sand": "#8c7f68", "dirtSlope": 16, "rockSlope": 30, "sandBelow": -30},
        "skybox": {"preset": "dusk", "zenith": "#16224a", "horizon": "#d98a64", "ground": "#1a1a20", "stars": 0.5, "ambient": 0.8,
                   "sun": {"elevation": 4, "azimuth": 250, "color": "#ff9a62", "intensity": 0.55, "size": 2.4, "glow": 0.9},
                   "haze": {"color": "#e07a52", "strength": 0.7}},
        "features": [
            noise(28, 900, 5, 61),
            mountain(55, 120, 700, 260, 63, ridges=0.5, erosion=0.6),
            mountain(48, 300, 600, 200, 64, ridges=0.4, erosion=0.6),
            crater(20, 200, 300, 60),
            noise(5, 120, 3, 8),
        ],
        "landing": 12,
        "sites": [],
        "city": {"radius": 640, "seed": 7, "blockX": 64, "blockZ": 88, "street": 16, "avenue": 26, "avenueEvery": 3, "tallest": 230, "lowest": 14, "coreFalloff": 700, "loop": 330},
        "pole": False,
    },
]


def lat_of(radius, metres):
    """The latitude of a ring this many metres from the north pole along the ground."""
    return round(90 - math.degrees(metres / radius), 5)


def build(w):
    r, h = w["radius"], w["landing"]
    top = r + h
    features = list(w["features"]) + [FINE]
    for _, lat, lon, rad, height, edge in w["sites"]:
        features.append(flatten(lat, lon, rad, height, edge))
    if "city" in w:  # the city's ground: level to its last street and a little over (CITY.md)
        features.append(flatten(90, 0, w["city"]["radius"] + 80, h, 300))
    else:
        features.append(flatten(90, 0, 70, h, 150))
    planetoid = {"radius": r, "cell": 2, "strength": w["strength"], "surface": w["surface"]}
    if "cavity" in w:
        planetoid["cavity"] = w["cavity"]
    if "limit" in w:  # the heaviest ship that may come down (WORLDS.md "Landing limits")
        planetoid["landing"] = {"tonnes": w["limit"][0], "why": w["limit"][1]}
    planetoid["features"] = features

    def y(x, z, lift=0.0):
        return round(math.sqrt(top * top - x * x - z * z) + lift, 3)

    half = min(r + 2600, 12600)
    ops = [{"op": "SurfaceSpawn", "lat": lat_of(top, 14), "lon": 22.5 + 45 * k} for k in range(8)]
    if w.get("pole", True):
        ops.append({"op": "SurfaceBlock", "lat": 90, "lon": 0, "w": 1.2, "h": 6, "d": 1.2, "color": "#8a8f99", "material": "Concrete"})  # the pole's post
    for kind, lat, lon, _, _, _ in w["sites"]:  # a survey post on each site, so it can be found
        ops.append({"op": "SurfaceBlock", "lat": lat, "lon": lon, "w": 0.8, "h": 4, "d": 0.8, "color": "#d9c14a" if kind == "home" else "#4ab0d9", "material": "Concrete"})
    head = {
        "format": "call-of-slop-map/1", "name": w["name"], "description": w["description"], "author": "Call of Slop", "order": w["order"],
        "halfX": half, "halfZ": half, "maxPlayers": 8, "skybox": w["skybox"]}
    if "storm" in w:
        head["storm"] = w["storm"]
    return {
        **head, "planetoid": planetoid, **({"city": {"lat": 90, "lon": 0, "height": h, **w["city"]}} if "city" in w else {}),
        "sandbox": {"resources": {"mix": w["ore"]}},  # each world's own ground (WORLDS.md "Ore")
        "teamSpawns": [[[x, y(x, z, 0.24), z] for x, z in ((-6, s * 40), (6, s * 40), (-12, s * 36), (12, s * 36))] for s in (-1, 1)],
        "hills": [{"x": s * 30, "y": y(30, 30), "z": s * 30, "radius": 6} for s in (1, -1)],
        "flags": [[0, y(0, 45, 0.5), -45], [0, y(0, 45, 0.5), 45]],
        "ops": ops,
    }


def main():
    os.makedirs(OUT, exist_ok=True)
    for w in WORLDS:
        path = os.path.join(OUT, w["name"] + ".json")
        with open(path, "w", newline="\n") as f:
            json.dump(build(w), f, indent=2)
            f.write("\n")
        print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
