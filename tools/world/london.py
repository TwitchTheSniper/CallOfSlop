#!/usr/bin/env python3
"""Generator for Maps/World/08-London.json: the World Tour's London neighbourhood (medium, UK, 16 players).

Usage: python3 tools/world/london.py [repo root] [preview.png]

An East London neighbourhood on a grey afternoon. From north to south:
  - the canal (outside the playable outline, cut below the streets) with moored narrowboats and a lock in the middle;
  - the towpath, 2.4 m below street level behind a brick retaining wall, with ramps up at the lock, at Canal Street
    and at each end;
  - canal-side yards: a boatyard and a council tower block in each half, the lock yard in the middle;
  - the railway viaduct (two "Railway Arches" buildings and an iron bridge over the station hall in the middle): its
    deck, 5.4 m up, has two tracks, a short platform with a shelter and parked trains; it runs out onto a grassy
    embankment at each end, which the teams climb from their spawns;
  - Arches Lane along the arches' front (a garage, a workshop, an arch bar, the station's tiled entrance);
  - the high street (UK left-hand traffic, a zebra crossing with Belisha beacons, double yellows, bus stops, a
    double-decker, black cabs) with a barber, a corner shop and a corner pub on the north side;
  - Victorian terraces with bay windows and a raised park with railings on the south side, rising gently to the back
    alley, with dead-end lanes jutting out of the outline.
Canal Street crosses the map at x = +-28, through an arch, down to the towpath and up to the south edge.

Everything is mirrored about x = 0 (placement.Site(mirror="x")): Blue holds the west half, Red the east. Buildings are
mirrored with their "mirror" variant (yaw -> -yaw). Building shells come from the Python building generator
(tools/buildings/bldgen.py), so props keep out of their walls and the sight-line check sees them.
"""
import json
import math
import os
import random
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "cluttered"))
sys.path.insert(0, os.path.join(REPO, "tools", "buildings"))
import placement as pl  # noqa: E402
import bldgen  # noqa: E402
import terrain as tport  # noqa: E402
sys.path.insert(0, HERE)
import london_decals as ldec  # noqa: E402

OUT_JSON = os.path.join(REPO, "Maps", "World", "08-London.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
R = pl.R

# ================================================================================================ levels and bands
TOW_Y = -2.4          # towpath (world height)
WATER_Y = -2.88       # canal water surface
CANAL_BED = -3.5
DECK_Y = 5.4          # viaduct deck (Railway Arches roof top)
UNDER = DECK_Y - 0.7  # underside of the iron bridge
TERRACE_Y = 0.3       # the south terraces' pad
ALLEY_Y = 0.75        # the back alley behind them
PARK_Y = 1.5          # the raised park

Z_CANAL = -40.2       # the outline along the towpath edge
Z_TOW_S = -35.3       # foot of the retaining wall (towpath's south edge)
VIA_Z0, VIA_Z1 = -19.3, -9.7   # viaduct outer faces (building centre z = -14.5)
VIA_CZ = -14.5
ARCH_X = 28.0         # Railway Arches centre (east half), spans 8..48
BANK_X = 48.5         # the embankment starts here
LANE_Z0, LANE_Z1 = -9.5, -4.0  # Arches Lane
HS_PAVE_N = (8.0, 10.5)        # high street north pavement
HS_ROAD = (10.5, 17.5)
HS_PAVE_S = (17.5, 20.0)
CS_X0, CS_X1 = 24.5, 31.5      # Canal Street (east half)
SA_X = 3.85                    # Station Approach half width
ALLEY_Z0, ALLEY_Z1 = 38.6, 42.6

# ================================================================================================ outline
EAST = [
    (0.0, Z_CANAL), (72.0, Z_CANAL), (72.0, -35.8), (65.0, -35.8),
    (65.6, -30.0), (66.4, -22.0), (66.2, -14.0), (65.7, -6.0), (65.2, 1.0), (65.0, 8.6),
    (75.0, 8.6), (75.0, 19.6), (65.0, 19.6),
    (65.0, 37.6), (65.6, 42.6),
    (59.6, 42.6), (59.6, 49.5), (55.4, 49.5), (55.4, 42.6),
    (CS_X1, 42.6), (CS_X1 - 0.5, 51.5), (CS_X0 + 0.5, 51.5), (CS_X0, 42.6),
    (14.4, 42.6), (14.4, 46.8), (10.2, 46.8), (10.2, 42.6),
    (0.0, 42.6),
]


def full_outline():
    east = [p for p in EAST]
    west = [(-x, z) for x, z in reversed(EAST)]
    pts = [(R(x), R(z)) for x, z in east] + [(R(x), R(z)) for x, z in west[1:-1]]
    return pts


OUTLINE = full_outline()
class PropLib(pl.PropLib):
    """placement.PropLib, skipping JSON files in Props/ that aren't props (the flag designs)."""

    def __init__(self, repo, pack_dir=None):
        import glob
        self.props = {}
        for root in [os.path.join(repo, "Props")] + ([os.path.join(pack_dir, "Props")] if pack_dir else []):
            for f in sorted(glob.glob(os.path.join(root, "**", "*.json"), recursive=True)):
                d = json.load(open(f))
                if isinstance(d, dict) and "name" in d and "parts" in d:
                    self.props[d["name"]] = pl.PropInfo(d, f)


lib = PropLib(REPO, os.path.join(REPO, "Maps", "World"))
site = pl.Site(lib, outline=OUTLINE, mirror="x", margin=1.0, cosmetic_margin=0.3)
blib = bldgen.Library(REPO)

# ================================================================================================ terrain
FEATURES = []


def feat(**kw):
    FEATURES.append(kw)


def mirrored_rect_plateau(x0, z0, x1, z1, h, mode, edge, material=None):
    """A rectangular plateau (and its twin across x = 0, unless it straddles the axis)."""
    for sx in ((1,) if x0 < 0 < x1 else (1, -1)):
        a, b = sorted((x0 * sx, x1 * sx))
        f = {"type": "plateau", "x": R((a + b) / 2), "z": R((z0 + z1) / 2), "w": R(b - a), "d": R(z1 - z0), "height": h, "edge": edge, "mode": mode}
        if material:
            f["material"] = material
        FEATURES.append(f)


def mirrored_flatten(x0, z0, x1, z1, h, edge, material=None):
    for sx in ((1,) if x0 < 0 < x1 else (1, -1)):
        a, b = sorted((x0 * sx, x1 * sx))
        f = {"type": "flatten", "x": R((a + b) / 2), "z": R((z0 + z1) / 2), "w": R(b - a), "d": R(z1 - z0), "height": h, "edge": edge, "mode": "set"}
        if material:
            f["material"] = material
        FEATURES.append(f)


def mirrored_ramp(points, width, edge, material=None, mode="set"):
    for sx in ((1, -1) if any(abs(p[0]) > 0.01 for p in points) else (1,)):
        f = {"type": "ramp", "points": [[R(p[0] * sx), R(p[1])] + ([R(p[2])] if len(p) > 2 else []) for p in points], "width": width, "edge": edge, "mode": mode}
        if material:
            f["material"] = material
        FEATURES.append(f)


def build_terrain():
    feat(type="noise", amplitude=0.12, scale=14, seed=8)
    # the south rise: the ground climbs towards the back alley and the lanes beyond it
    feat(type="ridge", points=[[-140, 53], [140, 53]], width=32, height=3.2)
    # the embankments at both ends: the railway runs out of the arches onto a grassy bank
    mirrored_rect_plateau(BANK_X, VIA_Z0, 90.0, VIA_Z1, DECK_Y, "fill", 15.0)
    # level pads (the town is built on them): canal-side yards, the viaduct, Arches Lane, the shops, the high street
    mirrored_flatten(-BANK_X - 0.4, -35.0, BANK_X + 0.4, 8.0, 0.0, 0.05)
    feat(type="flatten", x=0, z=14.0, w=160, d=12.0, height=0.0, edge=1.0, mode="set")
    # the south side: terraces on a low pad, the back alley a little higher, the raised park
    mirrored_flatten(15.6, 21.6, 64.0, 38.3, TERRACE_Y, 1.2)
    feat(type="flatten", x=0, z=40.6, w=136, d=4.0, height=ALLEY_Y, edge=0.8, mode="set")
    feat(type="plateau", x=0, z=31.0, w=26.4, d=19.2, height=PARK_Y, edge=0.4, mode="set", material="grass")
    # the canal cut, below the towpath; the lock narrows it in the middle
    feat(type="plateau", x=0, z=-47.0, w=300, d=11.6, height=CANAL_BED, edge=0.4, mode="cut")
    feat(type="plateau", x=0, z=-50.2, w=15.0, d=7.0, height=TOW_Y, edge=0.3, mode="set", material="dirt")  # lockside, far bank
    feat(type="plateau", x=0, z=-38.2, w=300, d=5.8, height=TOW_Y, edge=0.35, mode="set", material="dirt")  # the towpath
    # ramps from the towpath up to the streets: the lock steps, Canal Street (a sloping street), the ends
    mirrored_ramp([[0, -37.6, TOW_Y], [0, -28.8, 0.0]], 3.6, 0.8, material="sand")
    mirrored_ramp([[(CS_X0 + CS_X1) / 2, -37.6, TOW_Y], [(CS_X0 + CS_X1) / 2, -26.6, 0.0]], 6.4, 0.8, material="sand")
    mirrored_ramp([[58.5, -37.6, TOW_Y], [58.5, -29.5]], 4.0, 1.2, material="dirt")
    # Canal Street south: up the rise to the dead-end lane
    mirrored_ramp([[(CS_X0 + CS_X1) / 2, 20.5, 0.0], [(CS_X0 + CS_X1) / 2, 38.6, ALLEY_Y], [(CS_X0 + CS_X1) / 2, 51.0]], 6.6, 0.6, material="sand")
    # the square's steps: up from the zebra crossing, and down to the back alley
    mirrored_ramp([[0, 19.9, 0.0], [0, 24.4, PARK_Y]], 3.6, 0.3, material="sand")
    mirrored_ramp([[0, 42.3, ALLEY_Y], [0, 39.0, PARK_Y]], 3.0, 0.3, material="sand")
    # the paths up beside the park
    mirrored_ramp([[14.9, 20.4, 0.0], [14.9, 38.6, ALLEY_Y]], 2.4, 0.4, material="sand")


build_terrain()
TERRAIN = {
    "cell": 0.5, "margin": 24,
    "surface": {"grass": "#56703c", "dirt": "#6e6450", "rock": "#5e564c", "sand": "#7c7a74", "dirtSlope": 26, "rockSlope": 40, "variation": 0.07},
    "features": FEATURES,
}
HALF_X = max(abs(p[0]) for p in OUTLINE)
HALF_Z = max(abs(p[1]) for p in OUTLINE)
TER = tport.generate(TERRAIN, HALF_X, HALF_Z)


def ground(x, z):
    return float(TER.sample(x, z))


# ================================================================================================ buildings
BUILDINGS = []   # (name, x, z, yaw, variant, seed, plan) both halves


def building(name, x, z, yaw=0, variant=None, seed=0, spawns=False, mirror=True, y=None):
    """A named Building op (and its twin at -x with yaw -> -yaw and the mirror flag flipped). Its shell boxes and props
    go into the site's occupancy (props keep out of the walls; the sight check sees them)."""
    variant = dict(variant or {})
    places = [(x, z, yaw % 360, variant)]
    if mirror and abs(x) > 0.01:
        v2 = dict(variant)
        v2["mirror"] = not variant.get("mirror", False)
        places.append((-x, z, (-yaw) % 360, v2))
    d = blib.find(name)
    plans = []
    for px, pz, pyaw, v in places:
        gy = ground(px, pz) if y is None else y
        plan = bldgen.generate(blib, d, v, seed, (px, 0, pz), int(round(pyaw / 90)) % 4)
        op = {"op": "Building", "name": name, "at": [R(px), R(pz)], "yaw": int(pyaw)}
        if seed:
            op["seed"] = seed
        if spawns:
            op["spawns"] = True
        if v:
            op["variant"] = v
        site.raw(op)
        for b in plan.shell:
            x0, y0, z0, x1, y1, z1 = b[:6]
            if x1 - x0 < 0.02 or z1 - z0 < 0.02:
                continue
            site.occ.append(((x0, z0, x1, z1), y0 + gy, y1 + gy, "box"))
        for p in plan.props:  # the building's solid props (furniture, extras) take room too
            if p["def"].solid:
                px0, pz0, px1, pz1 = world_prop_rect(p)
                y0 = p["pos"][1] + gy
                site.occ.append(((px0, pz0, px1, pz1), y0, y0 + (p["def"].max[1] - p["def"].min[1]) * p["scale"], "prop"))
        for fp in plan.footprint:
            site.cosm.append(fp)   # nothing cosmetic scattered inside either
        BUILDINGS.append((name, px, pz, pyaw, v, seed, plan, gy))
        plans.append(plan)
    return plans


def world_prop_rect(p):
    d = p["def"]
    a = math.radians(p["yaw"])
    c, sn = math.cos(a), math.sin(a)
    pts = []
    for lx in (d.min[0] * p["scale"], d.max[0] * p["scale"]):
        for lz in (d.min[2] * p["scale"], d.max[2] * p["scale"]):
            pts.append((p["pos"][0] + lx * c + lz * sn, p["pos"][2] - lx * sn + lz * c))
    return (min(q[0] for q in pts), min(q[1] for q in pts), max(q[0] for q in pts), max(q[1] for q in pts))


def footprint_union(plan):
    xs = [r[0] for r in plan.footprint] + [r[2] for r in plan.footprint]
    zs = [r[1] for r in plan.footprint] + [r[3] for r in plan.footprint]
    return (min(xs), min(zs), max(xs), max(zs))


def place_buildings():
    # the viaduct: two arch sections, the west one mirrored (its stair bay at the station end)
    building("Railway Arches", ARCH_X, VIA_CZ, 0, {"palette": "london stock"}, seed=508)
    # the high street's north side: barber and corner shop either side of Station Approach, the corner pub at Canal Street
    building("Barber Shop", 8.2, 4.5, 0, {"palette": "navy paint"}, seed=11)
    building("Corner Shop", 18.6, 4.0, 0, {"palette": "green grocer"}, seed=12)
    building("Pub", 40.0, 3.0, 0, {"palette": "red lion"}, seed=13)
    # canal-side: the council tower block, facing the canal
    building("Apartment Block", 39.95, -27.0, 180, {"palette": "grey concrete", "floors": 5}, seed=14)
    # the south side: a townhouse by the park, then two bay-fronted terraces
    building("Victorian Townhouse", 20.0, 28.5, 180, {"palette": "london stock"}, seed=15)
    building("Bay Fronted Terrace", 39.75, 27.6, 180, {"palette": "london stock"}, seed=16)
    building("Bay Fronted Terrace", 55.6, 27.6, 180, {"palette": "red brick"}, seed=17)


place_buildings()


# ================================================================================================ output helpers

def write_map(extra):
    m = {
        "format": "call-of-slop-map/1",
        "name": "London",
        "description": "An East London neighbourhood: a canal towpath, a railway viaduct with arches and a station, a high street with a corner pub, bay-fronted terraces and a park.",
        "author": "Call of Slop",
        "order": 508,
        "maxPlayers": 16,
        "flagPool": ["uk"],
        "sky": "#a3a8ac",
    }
    m.update(extra)
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    with open(OUT_JSON, "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")


# DRESSING
# ================================================================================================ wrappers

def P(name, x, z, yaw=0.0, yw=None, mirror=None, check=True, lanes=True, gap=0.1, scale=1.0, seed=0, margin=None):
    """A prop (and its twin). yw = an absolute world height (deck, roof, water: anchor "world"); else it stands on
    the ground (the game puts it on the lowest ground under it, or a low box)."""
    n0 = len(site.ops)
    r = site.prop(name, x, z, yaw, y=yw, mirror=mirror, check=check, lanes=lanes, gap=gap, scale=scale, seed=seed, margin=margin)
    if yw is not None:
        for op in site.ops[n0:]:
            if op["op"] == "Prop":
                op["y"] = R(yw, 3)
                op["anchor"] = "world"
    return r


def B(x0, y0, z0, x1, y1, z1, color, mirror=None, solid=True):
    """A Block at absolute world heights (and its twin)."""
    n0 = len(site.ops)
    site.block(x0, y0, z0, x1, y1, z1, color, mirror=mirror, solid=solid)
    for op in site.ops[n0:]:
        op["anchor"] = "world"


def M(kind, x, z, color, yw=0.0, mirror=None, **kw):
    """A cosmetic Mesh standing at absolute world height yw (and its twin)."""
    n0 = len(site.ops)
    site.mesh(kind, x, z, color, mirror=mirror, y=yw, **kw)
    for op in site.ops[n0:]:
        op["anchor"] = "world"
        if op.get("y") == 0:
            op.pop("y")


DECALS = []


def D(decal, x, z, y=None, onto="floor", size=0, rotation=-1, mirror=True, tint=None, world=False, **kw):
    """A Decal op (and its twin at -x: rotation mirrored, wall+x <-> wall-x). y defaults to the ground at (x, z)
    as an absolute height."""
    def one(px, rot_, ont):
        op = {"op": "Decal", "decal": decal, "x": R(px), "z": R(z)}
        yy = (ground(px, z) if y is None else y)
        op["y"] = R(yy, 3)
        op["anchor"] = "world"
        if ont != "floor":
            op["onto"] = ont
        if size:
            op["size"] = size
        if rot_ != -1:
            op["rotation"] = R(rot_ % 360, 1)
        if tint:
            op["tint"] = tint
        op.update(kw)
        DECALS.append(op)
    one(x, rotation, onto)
    if mirror and abs(x) > 0.01:
        o2 = {"wall+x": "wall-x", "wall-x": "wall+x"}.get(onto, onto)
        r2 = rotation if rotation == -1 else (-rotation if onto in ("floor", "ceiling") else rotation)
        one(-x, r2, o2)


def SCATTER(decals, x0, z0, x1, z1, count, seed, mirror=True, y=None, **kw):
    for sx in ((1, -1) if mirror else (1,)):
        a, b = sorted((x0 * sx, x1 * sx))
        op = {"op": "DecalScatter", "decals": decals, "x0": R(a), "z0": R(z0), "x1": R(b), "z1": R(z1), "count": count, "seed": seed + (0 if sx > 0 else 500)}
        if y is not None:
            op["y"] = y
            op["anchor"] = "world"
        op.update(kw)
        DECALS.append(op)


CLUTTER = []


def CL(props, x0, z0, x1, z1, count, seed, width=0, align=False, gap=0.15):
    """A Clutter op in the east half with "mirror": true (the game makes the west twins)."""
    op = {"op": "Clutter", "props": props, "x0": R(x0), "z0": R(z0), "x1": R(x1), "z1": R(z1), "count": count, "seed": seed, "mirror": True}
    if width:
        op["width"] = width
    if align:
        op["align"] = True
    if gap != 0.15:
        op["gap"] = gap
    CLUTTER.append(op)


def keep_doors():
    """Keep solid props off every building doorway (both sides) and the stair tops."""
    for name, px, pz, pyaw, v, seed, plan, gy in BUILDINGS:
        for d in plan.doors:
            if d["floor"] != 0:
                continue
            ox, _, oz = d["other"]
            site.keep.append((ox, oz, 1.1))
            ix, _, iz = d["swing"]
            site.keep.append((ix, iz, 0.9))


# ================================================================================================ lanes (bot routes)

def lanes():
    for pts, w in [
        ([(0, -37.8), (71, -37.8)], 1.6),                  # towpath
        ([(0, -6.8), (47.5, -6.8)], 1.8),                  # Arches Lane
        ([(0, 9.3), (74, 9.3)], 1.0), ([(0, 18.8), (74, 18.8)], 1.0),   # high street pavements
        ([(0, 14.0), (74, 14.0)], 1.2),                    # the crown of the road
        ([(0, -33), (0, 24)], 2.0),                        # the middle: lock yard, station hall, Station Approach, zebra
        ([(28, -37.6), (28, 50.5)], 2.2),                  # Canal Street
        ([(0, 40.6), (65, 40.6)], 1.2),                    # back alley
        ([(14.9, 20.4), (14.9, 38.8)], 1.0),               # path by the park
        ([(8, -24), (24, -24)], 1.4), ([(31.8, -20.6), (48, -20.6)], 1.0),   # through the boatyard, behind the tower
        ([(57.5, -36), (58, 22)], 1.4),                    # the spawn end: towpath up the bank to the high street
    ]:
        site.lane(pts, w)


# ================================================================================================ ground surfaces

PAVE = "#8e8b84"
ROAD = "#3b3b3d"
SETTS = "#66605a"


def surfaces():
    site.rule("surfaces")
    # the high street (one strip each, across the whole map: on the axis, built once)
    B(-75.5, -0.3, HS_ROAD[0], 75.5, 0.03, HS_ROAD[1], ROAD, mirror=False, solid=False)
    B(-75.5, -0.3, HS_PAVE_N[0], 75.5, 0.13, HS_PAVE_N[1], PAVE, mirror=False, solid=False)
    B(-75.5, -0.3, HS_PAVE_S[0], 75.5, 0.13, HS_PAVE_S[1], PAVE, mirror=False, solid=False)
    # Arches Lane (setts) and Station Approach
    B(-48.3, -0.3, VIA_Z1, 48.3, 0.03, LANE_Z1, SETTS, mirror=False, solid=False)
    B(-SA_X, -0.3, LANE_Z1, SA_X, 0.06, HS_PAVE_N[0], PAVE, mirror=False, solid=False)
    # Canal Street between the corner shop and the pub, and north of the arches
    B(CS_X0, -0.3, LANE_Z1, CS_X1, 0.03, HS_PAVE_N[0], ROAD, solid=False)
    B(CS_X0, -0.3, -26.4, CS_X1, 0.03, VIA_Z0, ROAD, solid=False)
    # the back alley behind the terraces
    B(-65.6, ALLEY_Y - 0.3, ALLEY_Z0, 65.6, ALLEY_Y + 0.03, ALLEY_Z1, "#5c5852", mirror=False, solid=False)
    # the station hall floor (tiles) and the boatyard slab
    B(-7.7, -0.3, VIA_Z0, 7.7, 0.04, VIA_Z1, "#bdb6a2", mirror=False, solid=False)
    B(8.6, -0.3, -34.6, 24.2, 0.02, -19.9, "#77746c", solid=False)


# ================================================================================================ canal, lock, towpath

def canal():
    site.rule("canal")
    # water, the coping along the towpath edge, the lock chamber's far wall and the far bank's wall
    M("box", 0, -47.0, "#3b4a42", yw=WATER_Y - 0.05, mirror=False, width=260, depth=12.4, height=0.05)
    M("box", 0, -40.55, "#9a968c", yw=TOW_Y - 0.6, mirror=False, width=150, depth=0.7, height=0.62, material="Concrete")
    M("box", 0, -46.6, "#7a5e48", yw=CANAL_BED, mirror=False, width=15.2, depth=0.6, height=TOW_Y - CANAL_BED + 0.02, material="Brick")
    M("box", 0, -53.0, "#6e5440", yw=CANAL_BED, mirror=False, width=260, depth=0.8, height=3.5 + 1.2, material="Brick")
    # lock gates: two at each end on the towpath side (beams on the towpath), two on the lockside
    for x in (6.2,):
        P("Canal Lock Gate", x, -40.35, 0, check=False)
        P("Canal Lock Gate", x, -46.75, 180, yw=TOW_Y, check=False)
    for x in (2.0, 4.4):
        P("Mooring Ring Bollard", x, -39.9, 0, check=False)
    # narrowboats moored along the towpath and on the far side
    rnd = random.Random(5)
    for x, z, yaw in ((16.5, -42.35, 0), (36.0, -42.35, 180), (55.0, -42.35, 0), (26.0, -51.2, 180), (47.0, -51.2, 0), (68.0, -42.35, 180)):
        P("Canal Narrowboat", x, z, yaw, yw=WATER_Y - 0.32, check=False)
    # mooring bollards and rings along the edge, lamp posts and benches against the wall
    for x in [9.0 + 4.5 * k for k in range(14)]:
        if abs(x - 28) < 4 or abs(x - 58.5) < 3:
            continue
        P("Mooring Ring Bollard", x, -39.95, 0, check=False)
    site.along(["Heritage Lamp Post"], [(4, -35.75), (71, -35.75)], 16, face="line", side=1, lanes=False)
    for x in (12.0, 21.0, 46.0, 64.0):
        P("Bench", x, -35.9, 180, check=False)
        P("Trash Can", x + 1.4, -35.85, 0, check=False)
    # chicanes: brick piers that narrow the towpath (they also cut the long sight line along it)
    B(38.2, TOW_Y, -40.2, 39.0, TOW_Y + 2.1, -37.6, "#6e4a38")
    B(42.0, TOW_Y, -37.9, 42.8, TOW_Y + 2.1, -35.3, "#6e4a38")
    # the lock: a hut on the lockside (outside), balance-beam rests, and a hump of setts over the tail
    M("box", 0, -50.6, "#4a5a3a", yw=TOW_Y, mirror=False, width=3.2, depth=2.4, height=2.4, material="Wood")
    M("wedge", 0, -50.6, "#3a3a3e", yw=TOW_Y + 2.4, mirror=False, width=3.6, height=0.7, depth=2.8)
    # bikes, a stack of crates spilled out of the boatyard, a life-belt post (a pennant stands in)
    for x, yaw in ((33.0, 90), (34.0, 80)):
        P("Bicycle", x, -36.0, yaw, check=False)
    P("Wooden Crate", 18.8, -36.1, 10, check=False)
    P("Small Crate", 19.8, -36.0, 40, check=False)
    # the retaining wall between the towpath and the yards, cut where the ramps come up
    gaps = [(0, 2.6), (28.0, 4.0), (58.5, 3.2)]
    xs = [0.0]
    segs = []
    a = 0.0
    for c, hw in gaps:
        if c - hw > a:
            segs.append((a, c - hw))
        a = c + hw
    segs.append((a, 66.0))
    for x0, x1 in segs:
        if x1 - x0 < 0.5:
            continue
        B(x0, TOW_Y - 0.1, -35.55, x1, 0.55, -35.0, "#6e4a38", mirror=True)
        M("box", (x0 + x1) / 2, -35.28, "#c8c0b0", yw=0.55, width=x1 - x0 + 0.04, depth=0.66, height=0.08, material="Concrete")
    # railings along the wall top by the tower block and the boatyard
    for x in [33.5 + 2.5 * k for k in range(6)]:
        P("Iron Railings", x, -34.75, 0, check=False)


# ================================================================================================ viaduct deck, bridge, station hall

def viaduct():
    site.rule("viaduct")
    iron, iron2 = "#2e3a46", "#283039"
    # the iron bridge over the station hall: deck, parapet girders, cross girders under the deck
    B(-7.7, UNDER, VIA_Z0, 7.7, DECK_Y, VIA_Z1, iron2, mirror=False)
    B(-7.7, DECK_Y, VIA_Z0, 7.7, DECK_Y + 1.25, VIA_Z0 + 0.6, iron, mirror=False)
    B(-7.7, DECK_Y, VIA_Z1 - 0.6, 7.7, DECK_Y + 1.25, VIA_Z1, iron, mirror=False)
    for x in (-6.0, -3.0, 0.0, 3.0, 6.0):  # rivet ribs on the girder faces
        for z in (VIA_Z0 - 0.03, VIA_Z1 + 0.03):
            M("box", x, z, "#3a4652", yw=UNDER, mirror=False, width=0.16, depth=0.06, height=DECK_Y + 1.25 - UNDER)
    M("box", 0, VIA_Z1 + 0.04, "#3a4652", yw=DECK_Y + 1.0, mirror=False, width=15.4, depth=0.1, height=0.12)
    M("box", 0, VIA_Z0 - 0.04, "#3a4652", yw=DECK_Y + 1.0, mirror=False, width=15.4, depth=0.1, height=0.12)
    # the hall: tiled facades north and south with an entrance in each, iron columns inside
    for zf, zb in ((VIA_Z1 - 0.6, VIA_Z1), (VIA_Z0, VIA_Z0 + 0.6)):
        B(3.0, 0, zf, 7.7, UNDER, zb, "#6a2420")
        B(-3.0, 3.0, zf, 3.0, UNDER, zb, "#6a2420", mirror=False)
    for z in (-17.0, -12.0):
        B(-0.2, 0, z - 0.2, 0.2, UNDER, z + 0.2, iron, mirror=False)
        M("cylinder", 0, z, "#2e3a46", mirror=False, width=0.5, depth=0.5, height=UNDER, material="Metal")
    # Tube-style frontage on Arches Lane: oxblood tiles, a cream band, a canopy over the doors, the name board
    zf = VIA_Z1 + 0.03
    M("box", 5.35, zf, "#7a2a22", width=4.7, depth=0.06, height=UNDER, material="Tiles")
    M("box", 0, zf, "#7a2a22", yw=3.0, mirror=False, width=6.0, depth=0.06, height=UNDER - 3.0, material="Tiles")
    M("box", 0, zf + 0.02, "#e4dcc4", yw=3.0, mirror=False, width=15.4, depth=0.06, height=0.22, material="Tiles")
    M("box", 0, zf + 0.9, "#20242c", yw=2.9, mirror=False, width=7.4, depth=1.8, height=0.16, material="Metal")
    M("box", 0, zf + 0.05, "#1a2a52", yw=3.45, mirror=False, width=6.6, depth=0.08, height=0.9)
    # inside: ticket gates across the hall, machines, a kiosk, benches
    for x in (1.15, 3.45, 5.75):
        P("Ticket Gate", x, -14.6, 0, lanes=False, gap=0.05)
    P("Ticket Machine", 7.2, -11.5, 270, lanes=False)
    P("Ticket Machine", 7.2, -17.6, 270, lanes=False)
    P("Vending Machine", 6.9, -13.0, 270, lanes=False)
    P("Bench", 2.6, -18.55, 0, lanes=False)
    # deck: track along both lines, onto the embankments
    for k in range(11):
        x = 3.0 + 6.0 * k
        for z in (-15.0, -11.9):
            P("Railway Track", x, z, 0, yw=DECK_Y, check=False)
    # the platform (north side) with a gap at each stair head, its canopy and shelter
    B(-9.0, DECK_Y, -18.7, 9.0, DECK_Y + 0.45, -16.45, "#8a8680", mirror=False)
    B(17.0, DECK_Y, -18.7, 30.0, DECK_Y + 0.45, -16.45, "#8a8680")
    M("box", 0, -16.52, "#e8d040", yw=DECK_Y + 0.45, mirror=False, width=18.0, depth=0.14, height=0.02)
    M("box", 23.5, -16.52, "#e8d040", yw=DECK_Y + 0.45, width=13.0, depth=0.14, height=0.02)
    for x in (3.0, 7.5):
        B(x - 0.1, DECK_Y + 0.45, -18.4, x + 0.1, DECK_Y + 3.3, -18.2, "#1e3a2a")
    M("box", 0, -17.6, "#2a4a36", yw=DECK_Y + 3.3, mirror=False, width=18.4, depth=2.8, height=0.14, material="Metal")
    M("box", 0, -16.25, "#e4dcc4", yw=DECK_Y + 3.0, mirror=False, width=18.4, depth=0.08, height=0.3)
    P("Bus Shelter", 0, -17.55, 180, yw=DECK_Y + 0.45, mirror=False, check=False)
    for x in (5.5, 20.0, 27.0):
        P("Bench", x, -18.45, 0, yw=DECK_Y + 0.45, check=False)
    for x in (18.0, 25.0):
        P("Post Litter Bin", x, -18.5, 0, yw=DECK_Y + 0.45, check=False)
    P("Ticket Machine", 18.0, -18.45, 0, yw=DECK_Y + 0.45, check=False)
    # trains: two cars standing at the platform's far side (south line), a car on each north line by the embankment
    P("Commuter Train Car", 10.0, -11.9, 0, yw=DECK_Y + 0.38, check=False)
    P("Commuter Train Car", 38.0, -15.0, 0, yw=DECK_Y + 0.38, check=False)
    P("Commuter Train Car", 60.0, -15.0, 0, yw=DECK_Y + 0.38, check=False)
    # signals, relay cabinets, cable troughs, spare sleepers along the deck
    for x, z, yaw in ((31.5, -10.55, 90), (46.0, -18.85, 270)):
        P("Railway Signal", x, z, yaw, yw=DECK_Y, check=False)
    for x in (34.0, 43.0, 52.5):
        P("Street Cabinet", x, -10.6, 180, yw=DECK_Y, check=False)
    for x in (12.0, 24.5, 36.5, 48.5):
        M("box", x, -18.92, "#6a6a66", yw=DECK_Y, width=12.0, depth=0.4, height=0.28, material="Concrete")
        M("box", x, -10.08, "#6a6a66", yw=DECK_Y, width=12.0, depth=0.4, height=0.28, material="Concrete")
    P("Plank Pile", 44.0, -13.4, 0, yw=DECK_Y, check=False)
    P("Pigeons", 21.0, -13.4, 0, yw=DECK_Y, check=False)
    # the abutment where the arches meet the embankment: brick from the ground to the deck, so the deck runs on level
    B(48.3, 0.0, VIA_Z0, 49.6, DECK_Y, VIA_Z1, "#7a6650")
    # brick retaining walls where the embankment's flanks meet the level town (cosmetic faces on the terrain step)
    for z0, z1 in ((-34.8, -19.3), (VIA_Z1, 7.8)):
        n = max(1, int(math.ceil((z1 - z0) / 2.5)))
        for k in range(n):
            a_, b_ = z0 + (z1 - z0) * k / n, z0 + (z1 - z0) * (k + 1) / n
            h = max(ground(49.3, a_), ground(49.3, b_), ground(49.3, (a_ + b_) / 2)) + 0.25
            if h > 0.35:
                M("box", 48.78, (a_ + b_) / 2, "#6e5442", yw=-0.1, width=0.56, depth=b_ - a_ + 0.02, height=h + 0.1, material="Brick")
    # parapets along the embankment top (the building's own end there; posts and a low wall on the bank)
    for z0, z1 in ((VIA_Z0, VIA_Z0 + 0.5), (VIA_Z1 - 0.5, VIA_Z1)):
        B(48.3, DECK_Y - 0.4, z0, 54.0, DECK_Y + 0.9, z1, "#857256")


# ================================================================================================ the streets

def high_street():
    site.rule("high street")
    # lamps: heritage columns on both pavements, staggered
    site.along(["Heritage Lamp Post"], [(6, 10.15), (74, 10.15)], 17, face="line", side=-1, lanes=False)
    site.along(["Heritage Lamp Post"], [(14, 17.85), (74, 17.85)], 17, face="line", side=1, lanes=False)
    # the zebra crossing at Station Approach: beacons at both kerbs, the stripes, zig-zags, LOOK RIGHT
    for z in (10.25, 17.75):
        P("Belisha Beacon", 2.6, z, 0, lanes=False, check=False)
    for z in (11.4, 16.6):
        D("Crosswalk Bars", 0, z, y=0.03, size=4.0, rotation=90, mirror=False)
        D("Crosswalk Bars", 0, z + (1.2 if z < 14 else -1.2), y=0.03, size=4.0, rotation=90, mirror=False)
    for k in range(6):
        x = 4.2 + 1.3 * k
        for z in (10.75, 17.25):
            D("Lane Dash", x, z, y=0.03, size=1.4, rotation=90 + (28 if k % 2 else -28))
    # centre line and double yellow lines along both kerbs (none at the bus stop and the crossing)
    for k in range(12):
        x = 10.0 + 6.0 * k
        D("Lane Dash", x, 14.0, y=0.03, size=2.2, rotation=90)
    for k in range(16):
        x = 9.5 + 4.0 * k
        if 12.0 < x < 24.0 or CS_X0 - 1 < x < CS_X1 + 1:
            continue
        D("Double Yellow Lines", x, 10.75, y=0.03, size=3.9)
        D("Double Yellow Lines", x, 17.25, y=0.03, size=3.9)
    # give way lines at Canal Street's mouths, a bus stop box, manholes and drains
    D("Stop Line", 28.0, 10.6, y=0.03, size=3.4, rotation=0)
    D("Stop Line", 28.0, 17.4, y=0.03, size=3.4, rotation=0)
    D("Loading Bay Box", 17.5, 12.0, y=0.03, size=4.0)
    for x, z in ((8.0, 14.6), (33.0, 13.4), (52.0, 14.4), (21.0, 13.2), (63.0, 13.8)):
        D("Manhole Cover", x, z, y=0.03)
    for x in (11.0, 27.0, 44.0, 60.0):
        D("Drain Grate", x, 10.65, y=0.03, rotation=0)
        D("Drain Grate", x + 3.0, 17.35, y=0.03, rotation=0)
    SCATTER(["Oil Stain", "Asphalt Patch", "Floor Cracks", "Puddle", "Oil Drips"], 4, 10.8, 74, 17.2, 26, 31, y=0.03, gap=2.5)
    SCATTER(["Floor Cracks", "Chewing Gum" if False else "Water Stain", "Cigarette Butts", "Leaf Litter"], 4, 8.1, 74, 10.4, 16, 32, y=0.13, gap=2.0)
    SCATTER(["Floor Cracks", "Water Stain", "Cigarette Butts", "Leaf Litter"], 4, 17.6, 74, 19.9, 16, 33, y=0.13, gap=2.0)
    # vehicles: the bus at its stop, black cabs, parked cars (eastbound on the north kerb, westbound on the south)
    P("Double Decker Bus", 17.5, 12.1, 0, lanes=False, check=False)
    P("Bus Shelter", 17.0, 9.15, 180, lanes=False, check=False)
    P("Bus Stop Sign", 22.6, 10.25, 180, lanes=False, check=False)
    P("Black Cab", 36.0, 11.75, 0, lanes=False, check=False)
    P("Black Cab", 54.5, 15.6, 180, lanes=False, check=False)
    P("Double Decker Bus", 44.0, 15.8, 180, lanes=False, check=False)
    for name, x in (("Hatchback", 41.5), ("Van", 54.0)):
        P(name, x, 11.65, 0, lanes=False, check=False)
    for name, x in (("Sedan", 7.5), ("Hatchback", 34.5), ("Black Cab", 12.8)):
        P(name, x, 16.4, 180, lanes=False, check=False)
    # Station Approach: phone boxes, a post box, a newsstand, bikes, bollards
    P("Phone Booth", 2.9, 7.2, 90, lanes=False)
    P("Post Box", 3.1, 9.8, 0, lanes=False, check=False)
    P("Newsstand", 2.6, -1.5, 270, lanes=False)
    P("Bike Rack", 3.2, 3.0, 90, lanes=False)
    P("Bicycle", 3.0, 3.0, 95, lanes=False, check=False)
    for z in (-3.6,):
        P("Bollard", 1.4, z, 0, lanes=False, check=False)
    # shop fronts: market stall with produce at the corner shop, A-boards, flower tubs, bins, a bench by the pub
    P("Market Stall", 16.4, 9.6, 0, lanes=False, check=False)
    P("Produce Crates", 15.6, 9.7, 0, check=False)
    P("Cabbage Crate", 17.2, 9.8, 0, check=False)
    P("Sandwich Board", 12.9, 9.4, 30, check=False)
    P("Sandwich Board", 35.5, 9.4, -20, check=False)
    P("Hanging Banner", 33.4, 8.25, 0, yw=3.4, check=False)
    P("Post Litter Bin", 25.2, 9.9, 0, lanes=False, check=False)
    P("Post Litter Bin", 23.0, 18.0, 180, lanes=False, check=False)
    P("Phone Booth", 46.5, 18.9, 270, lanes=False)
    # street trees in pits on the south pavement
    for x in (9.5, 32.2, 47.6, 63.2):
        M("box", x, 18.75, "#4a4038", yw=0.135, width=1.1, depth=1.1, height=0.02)


def arches_lane():
    site.rule("arches lane")
    # vehicles at the garage and the workshop, a skip, tyres, pallets, bins at the bar
    P("Van", 21.0, -5.4, 0, lanes=False, check=False)
    P("Car On Bricks", 34.6, -7.6, 180, lanes=False)
    P("Builders Skip", 45.0, -5.0, 0, lanes=False)
    P("Tyre Stack", 32.6, -9.0, 0, lanes=False)
    P("Tyre", 33.4, -8.9, 0, lanes=False, check=False)
    P("Pallet Stack", 43.0, -9.0, 0, lanes=False)
    P("Oil Drum", 41.6, -9.05, 0, lanes=False)
    P("Engine Block", 36.7, -9.0, 0, lanes=False, check=False)
    P("Scaffold Tower", 13.2, -5.4, 0, lanes=False)
    P("Dumpster", 10.4, -4.75, 0, lanes=False)
    P("Wheelie Bin", 15.6, -4.5, 0, lanes=False)
    P("Wheelie Bin Grey", 16.4, -4.5, 0, lanes=False)
    P("Heritage Lamp Post", 25.0, -9.0, 0, lanes=False, check=False)
    P("Heritage Lamp Post", 47.0, -4.4, 0, lanes=False, check=False)
    # the yards behind the shops: low walls with gates onto the lane
    B(4.2, 0, -4.3, 9.4, 1.6, -4.0, "#6a4a3a")
    B(10.6, 0, -4.3, 13.4, 1.6, -4.0, "#6a4a3a")
    B(14.6, 0, -4.3, 21.6, 1.6, -4.0, "#6a4a3a")
    B(32.9, 0, -4.3, 37.0, 1.6, -4.0, "#6a4a3a")
    B(38.2, 0, -4.3, 47.7, 1.6, -4.0, "#6a4a3a")
    P("Recycling Bin", 6.0, -3.3, 180, lanes=False)
    P("Wheelie Bin", 7.2, -3.3, 180, lanes=False)
    P("Gas Cylinders", 20.6, -1.0, 0, lanes=False)
    P("Cardboard Stack", 17.4, -1.2, 0, lanes=False)
    # graffiti and grime on the arches and the yard walls, oil under the cars
    for x, dec in ((18.3, "Graffiti Throw-Up"), (39.2, "Graffiti Tag"), (44.8, "Graffiti Burst"), (6.2, "Poster Circles"), (30.4, "Torn Poster")):
        D(dec, x, -9.0, y=1.6, onto="wall-z", size=2.4 if "Graffiti" in dec else 0)
    for x in (12.0, 20.0, 36.0, 44.0):
        D("Grime Streaks", x, -9.0, y=3.8, onto="wall-z", size=2.4)
    SCATTER(["Oil Stain", "Floor Cracks", "Puddle", "Oil Drips", "Tyre Tracks"], 8.5, -9.4, 47.5, -4.2, 18, 41, y=0.03, gap=2.0)


def canal_street():
    site.rule("canal street")
    # north: down the slipway to the towpath; parked van, bollards at the top
    P("Box Truck", 29.9, -24.0, 90, lanes=False, check=False)
    for x in (25.4, 30.6):
        P("Bollard", x, -27.0, 0, lanes=False, check=False)
    # south: up the hill to the dead end, cars parked on the west kerb facing downhill, road works at the top
    P("Hatchback", 25.6, 26.0, 270, lanes=False, check=False)
    P("Sedan", 25.6, 32.5, 270, lanes=False, check=False)
    P("Construction Fence Panel", 28.0, 51.0, 0, lanes=False, check=False)
    P("Road Works Sign", 26.5, 49.4, 180, lanes=False, check=False)
    P("Traffic Cone", 29.5, 49.6, 0, check=False)
    P("Traffic Cone", 30.3, 48.8, 0, check=False)
    P("Builders Skip", 29.8, 46.4, 90, lanes=False, check=False)
    for z in (24.0, 36.0):
        D("Double Yellow Lines", 30.9, z, size=3.9, rotation=90)


def south_side():
    site.rule("south side")
    # the park: railings on the wall top (gaps at the steps), trees, paths, benches, a memorial, swings
    for k in range(10):
        x = -11.7 + 2.6 * k
        if abs(x) < 2.0:
            continue
        if x > 0:
            P("Iron Railings", x, 21.55, 0, yw=PARK_Y, check=False)
    for z in [23.0 + 2.6 * k for k in range(7)]:
        P("Iron Railings", 13.05, z, 90, yw=PARK_Y, check=False)
    for k in range(5):
        x = 1.6 + 2.6 * k
        if x < 12.5:
            P("Iron Railings", x + 0.4, 40.45, 0, yw=PARK_Y, check=False)
    M("box", 7.6, 21.5, "#c8c0ac", yw=PARK_Y - 0.02, width=11.4, depth=0.5, height=0.08, material="Concrete")
    M("box", 13.2, 31.0, "#c8c0ac", yw=PARK_Y - 0.02, width=0.5, depth=19.4, height=0.08, material="Concrete")
    # gravel paths: the middle walk and a cross walk round the memorial
    M("box", 0, 31.0, "#9a8e78", yw=PARK_Y + 0.005, mirror=False, width=2.6, depth=18.6, height=0.03, material="Dirt")
    M("box", 0, 31.0, "#9a8e78", yw=PARK_Y + 0.005, mirror=False, width=25.0, depth=2.2, height=0.03, material="Dirt")
    B(-0.8, PARK_Y, 30.2, 0.8, PARK_Y + 0.6, 31.8, "#a8a294", mirror=False)
    B(-0.35, PARK_Y + 0.6, 30.65, 0.35, PARK_Y + 3.6, 31.35, "#a8a294", mirror=False)
    M("box", 0, 31.0, "#8a8476", yw=PARK_Y + 3.6, mirror=False, width=0.9, depth=0.9, height=0.25, material="Concrete")
    M("cone", 0, 31.0, "#8a8476", yw=PARK_Y + 3.85, mirror=False, width=0.6, depth=0.6, height=0.7, segments=4, material="Concrete")
    for x, z, yaw in ((4.0, 29.4, 180), (8.5, 29.4, 180), (4.0, 32.6, 0), (8.5, 32.6, 0)):
        P("Bench", x, z, yaw, yw=PARK_Y, check=False)
    P("Drinking Fountain", 2.4, 25.0, 0, yw=PARK_Y)
    P("Swing Set", 8.6, 37.4, 0, yw=PARK_Y)
    P("Post Litter Bin", 2.0, 36.0, 0, yw=PARK_Y)
    P("Heritage Lamp Post", 1.8, 23.0, 0, yw=PARK_Y, check=False)
    P("Heritage Lamp Post", 11.8, 39.4, 0, yw=PARK_Y, check=False)
    # the path beside the park and the back alley: bins, fly-tipping, a burnt-out car, garages' doors
    P("Wheelie Bin", 16.2, 37.8, 270, lanes=False)
    P("Old Sofa", 47.0, 41.3, 180, yw=ALLEY_Y, lanes=False)
    P("Trash Bag", 48.4, 41.3, 0, yw=ALLEY_Y, check=False)
    P("Shopping Cart", 18.6, 41.3, 30, yw=ALLEY_Y, lanes=False, check=False)
    P("Burnt Out Car", 57.5, 46.4, 90, lanes=False, check=False)
    P("Builders Skip", 12.3, 44.6, 90, lanes=False, check=False)
    P("Construction Fence Panel", 57.5, 49.3, 0, lanes=False, check=False)
    P("Fence Panel", 12.3, 46.6, 0, lanes=False, check=False)
    for x in (22.0, 37.0, 52.0, 62.0):
        P("Heritage Lamp Post", x, 42.2, 180, yw=ALLEY_Y, check=False)
    SCATTER(["Floor Cracks", "Puddle", "Leaf Litter", "Oil Stain"], 1, 38.8, 65, 42.4, 14, 51, y=ALLEY_Y + 0.03, gap=2.0)
    for x, dec in ((33.0, "Graffiti Scribble"), (50.0, "Graffiti Tag")):
        D(dec, x, 38.0, y=ALLEY_Y + 0.9, onto="wall-z", size=2.0)


def canal_side():
    site.rule("canal side")
    # the boatyard: a narrowboat on cradles, the yard office, timber, crates, gas, a scaffold, a chain-link fence
    for x in (11.0, 16.5, 22.0):
        P("Cinder Blocks", x, -29.0, 0, check=False)
    P("Canal Narrowboat", 16.5, -29.0, 0, yw=0.25, scale=0.78, check=False)
    P("Garden Shed", 10.2, -21.4, 180, lanes=False)
    P("Pallet Stack", 12.8, -21.1, 0)
    P("Gas Bottle Cage", 22.4, -21.0, 0)
    P("Plank Pile", 20.0, -33.4, 0)
    P("Cable Spool", 9.8, -33.2, 0)
    P("Scaffold Board Stack", 14.0, -33.4, 0)
    P("IBC Tote", 23.2, -32.6, 0)
    P("Paint Cans", 18.8, -21.2, 0, check=False)
    P("Tyre Stack", 23.6, -26.0, 0)
    for z in (-33.0, -29.9):
        P("Chain Link Fence", 24.15, z, 90, lanes=False, check=False)
    # the lock yard: benches, planters, a lamp, bike racks, bollards at the head of the steps
    P("Bench", 5.5, -20.6, 0, lanes=False)
    P("Planter", 5.5, -23.0, 0)
    P("Planter", 5.5, -32.0, 0)
    P("Heritage Lamp Post", 2.4, -29.4, 0, lanes=False, check=False)
    P("Bike Rack", 6.6, -27.0, 90, lanes=False)
    P("Bollard", 2.0, -28.4, 0, check=False)
    # behind the tower block: bins, and the arches' back doors
    P("Recycling Bin", 46.4, -20.7, 0, lanes=False)
    P("Trash Bag", 45.5, -20.6, 0, check=False)
    SCATTER(["Floor Cracks", "Oil Stain", "Puddle", "Gravel Scatter", "Paint Spill"], 8.8, -34.4, 24, -20, 12, 61, y=0.02, gap=2.0)
    SCATTER(["Floor Cracks", "Puddle", "Leaf Litter", "Moss Patch"], 0, -34.6, 7.8, -19.6, 6, 62, y=0.0, gap=2.0)


def spawn_end():
    site.rule("spawn end")
    # the high street's end: road works closing it at the jut, a flag, the pub garden on the bank foot
    for z in (10.0, 12.0, 14.0, 16.0, 18.0):
        P("Water Barrier", 74.0, z, 90, lanes=False, check=False)
    P("Arrow Board", 71.6, 13.8, 270, lanes=False, check=False)
    P("Road Works Sign", 70.0, 18.6, 270, lanes=False, check=False)
    P("Traffic Cone", 69.6, 12.4, 0, check=False)
    P("Traffic Cone", 69.8, 16.2, 0, check=False)
    P("Flag on a Pole", 64.0, 9.4, 0, lanes=False, check=False)
    # the towpath's end: a gate
    P("Fence Panel", 71.6, -38.0, 90, lanes=False, check=False)
    P("Fence Panel", 71.6, -36.6, 90, lanes=False, check=False)
    P("Bench", 66.5, -35.9, 180, check=False)


# ================================================================================================ signs (pack decals)

def sign(key, cx, y, cz, onto, rotation=0):
    """Lay a tiled sign (tools/world/london_decals.py) centred at (cx, y, cz): wall signs are projected `onto` a wall
    from 0.35 m in front of it, floor signs onto the ground. Tiles run left to right as seen by someone reading it."""
    names, tw, th = ldec.tiles(key)
    n = len(names)
    if onto == "floor":
        ux, uz = (1, 0) if rotation % 360 == 0 else (-1, 0)
    else:
        ux, uz = {"wall-z": (1, 0), "wall+z": (-1, 0), "wall-x": (0, -1), "wall+x": (0, 1)}[onto]
    nx, nz = {"wall-z": (0, 1), "wall+z": (0, -1), "wall-x": (1, 0), "wall+x": (-1, 0), "floor": (0, 0)}[onto]
    for i, nm in enumerate(names):
        off = (i - (n - 1) / 2) * tw
        x, z = cx + ux * off + nx * 0.35, cz + uz * off + nz * 0.35
        D(nm, x, z, y=y, onto=onto, rotation=rotation, mirror=False, reach=1.0)


def more_dressing():
    site.rule("dressing")
    # ducks and a swan-white mallard on the canal, pigeons on the deck, the square and the station roof
    for x, z in ((10.0, -44.5), (11.2, -45.3), (30.5, -47.8), (44.0, -44.0), (62.0, -48.5)):
        P("Mallard Duck", x, z, (x * 37) % 360, yw=WATER_Y - 0.02, check=False)
    P("Duck Family", 22.0, -45.0, 70, yw=WATER_Y - 0.02, check=False)
    for x, z, yw in ((33.0, -13.4, DECK_Y), (5.0, -16.9, DECK_Y + 0.45), (8.5, 26.0, PARK_Y), (2.6, 4.6, None), (26.5, -22.0, None)):
        P("Pigeons", x, z, 0, yw=yw, check=False)
    # cafe tables outside the arch bar, a moped at the garage, bikes chained to the railings
    for x in (17.0, 22.6):
        P("Patio Table", x, -7.9, 0, lanes=False)
        P("Patio Chair", x - 0.8, -7.9, 90, lanes=False, check=False)
        P("Patio Chair", x + 0.8, -7.9, 270, lanes=False, check=False)
    P("Moped Scooter", 31.6, -8.8, 200, lanes=False, check=False)
    P("Motorbike", 39.6, -8.9, 160, lanes=False, check=False)
    for x in (36.0, 38.6):
        P("Bicycle", x, -34.35, 0, check=False)
    # back yards of the terraces: bins, bikes, toys, gnomes, BBQs (cosmetic ones only where the yard is tight)
    for bx in (39.75, 55.6):
        for k, cx in enumerate((-5.0, 0.0, 5.0)):
            x = bx - cx
            P(["Wheelie Bin", "Wheelie Bin Grey", "Recycling Bin"][k], x - 1.8, 37.0, 180, lanes=False)
            P(["Kettle BBQ", "Paddling Pool", "Bicycle"][k], x + 1.0, 35.2, 30 * k, lanes=False, check=False)
            P("Garden Gnome" if k != 1 else "Flower Pots", x - 1.6, 33.6, 0, check=False)
    # the townhouse's side passage and the tower block's forecourt
    P("Bicycle", 24.0, 34.0, 90, check=False)
    P("Wheelie Bin Grey", 24.0, 36.0, 90, lanes=False)
    P("Bench", 36.0, -33.6, 0, lanes=False, check=False)
    P("Bench", 44.0, -33.6, 0, lanes=False, check=False)
    P("Post Litter Bin", 40.0, -33.8, 0, lanes=False, check=False)
    # the deck: litter, sleeper ends, a dropped hi-vis bag
    for x, z in ((14.0, -13.3), (27.0, -13.5), (40.0, -13.2), (52.0, -13.4), (58.0, -10.9)):
        P("Litter", x, z, (x * 53) % 360, yw=DECK_Y, check=False)
    for x, z in ((24.0, -18.9), (35.0, -10.8)):
        P("Soda Cans", x, z, 0, yw=DECK_Y, check=False)
    P("Duffel Bag", 30.5, -13.3, 40, yw=DECK_Y, check=False)
    # a UK flag on the tower block's roof edge and over the pub, a flagpole in the square
    P("Flag on a Pole", 9.0, 38.6, 0, yw=PARK_Y, lanes=False, check=False, mirror=True)
    # decals: puddles everywhere (it has been raining), grime and posters, the playground's chalk
    SCATTER(["Puddle", "Puddle", "Water Stain", "Leaf Litter"], 0.4, 21.8, 12.8, 40.2, 14, 71, y=PARK_Y + 0.02, gap=2.5)
    SCATTER(["Puddle", "Dirt Patch", "Footprints"], 1.0, -40.0, 71, -35.7, 22, 72, y=TOW_Y + 0.02, gap=3.0)
    SCATTER(["Puddle", "Oil Stain", "Floor Cracks"], 0.5, -18.8, 7.4, -10.2, 6, 73, y=0.05, gap=1.6, mirror=False)
    SCATTER(["Leaf Litter", "Puddle", "Floor Cracks", "Moss Patch"], 8.4, -18.6, 47.6, -10.4, 16, 74, y=DECK_Y + 0.02, gap=2.5)
    for x in (14.0, 22.0, 30.0, 38.0, 46.0):
        D("Grime Streaks", x, -19.7, y=3.4, onto="wall+z", size=2.6)
        D("Soot Plume", x + 4.0, -19.7, y=4.2, onto="wall+z", size=2.4)
    for x, dec in ((11.5, "Graffiti Throw-Up"), (26.0, "Graffiti Burst"), (41.0, "Graffiti Scribble"), (52.0, "Graffiti Tag"), (63.0, "Graffiti Throw-Up")):
        D(dec, x, -36.0, y=TOW_Y + 1.1, onto="wall+z", size=2.2)
    for x, z, onto in ((6.0, -19.7, "wall+z"), (33.0, -19.7, "wall+z")):
        D("Torn Poster", x, z, y=1.6, onto=onto)
    D("Chalk Marks", 7.0, 37.0, y=PARK_Y + 0.02, size=1.6)
    for x in (34.0, 41.0, 46.0, 50.0, 57.0, 62.0):
        D("Brick Patch", x, 21.5, y=1.6 + TERRACE_Y, onto="wall+z")


def signs():
    sign("station", 0.0, 3.9, -9.58, "wall-z")
    for sx in (1, -1):
        sign("barber", sx * 10.2, 3.025, 8.22, "wall-z")
        sign("shop", sx * 18.6, 3.72, 8.125, "wall-z")
        sign("pub", sx * 43.6, 2.775, 8.24, "wall-z")
        sign("bar", sx * 20.75, 3.6, -9.58, "wall-z")
        sign("garage", sx * 36.2, 3.85, -9.58, "wall-z")
        sign("workshop", sx * 44.2, 3.2, -9.7, "wall-z")
        sign("plate_lane", sx * 5.6, 2.5, -9.62, "wall-z")
        sign("towpath", sx * 9.0, TOW_Y + 1.25, -35.55, "wall+z")
    sign("plate_canal", 23.23, 2.5, 6.4, "wall-x")
    sign("plate_canal", -23.23, 2.5, 6.4, "wall+x")
    sign("plate_high", 4.07, 2.5, 6.8, "wall+x")
    sign("plate_high", -4.07, 2.5, 6.8, "wall-x")
    sign("look", 0.0, 0.03, 10.95, "floor", rotation=180)
    sign("look", 0.0, 0.03, 17.05, "floor", rotation=0)
    for sx in (1, -1):
        sign("busstop", sx * 17.5, 0.03, 10.95, "floor", rotation=180)
    for x, z, onto, nm in ((7.1, -13.0, "wall+x", "London Poster Tea"), (-7.1, -16.0, "wall-x", "London Poster Gig"),
                           (12.0, -9.6, "wall-z", "London Poster Gig"), (-30.6, -9.6, "wall-z", "London Poster Tea"),
                           (24.2, -2.0, "wall-x", "London Poster Tea")):
        D(nm, x + {"wall+x": -0.3, "wall-x": 0.3}.get(onto, 0), z + (0.3 if onto == "wall-z" else 0), y=1.7, onto=onto, mirror=False, reach=1.0)


# ================================================================================================ foliage

def foliage():
    fb = pl.FoliageBuilder(site, seed=508)
    fb.surfaces(Concrete=0, Tiles=0.04, Sand=0.06, Brick=0, Metal=0, Wood=0, Grid=0, Plain=0.05, Dirt=0.5)
    # the embankments: long grass, brambles and buddleia, a few self-seeded trees
    fb.region("grass", 4.5, (48.6, -34.0, 66.0, 7.6), patchiness=0.25, scale=[1.0, 1.6])
    fb.region("shrub", 0.9, (48.6, -33.0, 66.0, 6.0))
    fb.region("bush", 0.25, (50.0, -32.0, 66.0, 4.0), scale=[1.0, 1.7])
    for x, z, h in ((53.5, -28.0, 7.5), (61.0, -24.5, 8.5), (63.0, 2.5, 7.0), (51.0, 3.2, 6.0)):
        fb.tree(x, z, h)
    # the park: lawn, flower beds, London planes
    fb.region("grass", 5.0, (0.3, 21.8, 12.9, 40.2), patchiness=0.1, scale=[0.7, 1.0], color="#4e7a36")
    fb.region("shrub", 1.2, (10.2, 22.6, 12.8, 28.0), color="#5a6a30")
    for x, z, h in ((6.2, 25.0, 11.0), (11.0, 33.5, 12.0), (5.0, 38.0, 10.0)):
        fb.tree(x, z, h)
    # street trees in their pits on the high street, a tree in the lock yard
    for x in (9.5, 32.2, 47.6, 63.2):
        fb.tree(x, 18.75, 9.5)
    fb.tree(6.4, -30.0, 7.0)
    # towpath verge (weeds at the foot of the wall), the bank behind the tower, yards and back gardens
    fb.region("grass", 3.0, (1.0, -36.0, 71.0, -35.55), scale=[0.9, 1.4])
    fb.region("shrub", 0.4, (1.0, -36.2, 71.0, -35.55))
    fb.region("grass", 3.0, (15.8, 33.0, 64.0, 38.3), patchiness=0.4)
    fb.region("grass", 1.0, (4.0, -3.9, 47.8, 0.8), patchiness=0.5)
    # weeds and buddleia on the viaduct deck along the parapets and between the sleepers
    fb.zone("include", "rect", center=[28.0, -18.4], halfX=20.0, halfZ=0.3, onTop=True)
    fb.zone("include", "rect", center=[28.0, -10.6], halfX=20.0, halfZ=0.3, onTop=True)
    fb.region("grass", 2.5, (8.3, -18.7, 47.7, -18.1))
    fb.region("grass", 2.5, (8.3, -10.9, 47.7, -10.3))
    fb.region("bush", 0.12, (8.3, -18.7, 47.7, -18.1), scale=[0.8, 1.2], color="#6a5a8a")
    # keep the towpath itself, the roads and the canal clear
    fb.path([(-72, -37.9), (72, -37.9)], 2.8)
    return fb.data


# ================================================================================================ objectives, spawns

TEAM_E = [(68.0, 11.6), (68.5, 16.2), (71.5, 11.2), (71.5, 16.6), (61.5, 12.2), (61.5, 16.0), (66.0, -37.4), (69.0, -37.6)]
FFA = [(45.0, -36.8), (16.0, -24.5), (38.0, -6.8), (44.0, 40.6), (8.5, 34.0), (35.5, 14.0)]


def objectives():
    for x, z in TEAM_E:
        site.keepout(x, z, 2.5)
        site.spawn(x, z)
    for x, z in FFA:
        site.keepout(x, z, 2.0)
        site.spawn(x, z)
    hills = [{"x": 0.0, "z": -37.8, "radius": 4.5}, {"x": 0.0, "z": -13.9, "y": DECK_Y + 0.05, "radius": 4.0}, {"x": 0.0, "z": 27.0, "radius": 4.5}]
    for h in hills:
        site.keepout(h["x"], h["z"], h["radius"] + 0.5, mirror=False)
    flags = [[-64.5, 0.12, 14.0], [64.5, 0.12, 14.0]]
    for x, _, z in flags:
        site.keepout(x, z, 2.0, mirror=False)
    team = [[[R(-x), R(z)] for x, z in TEAM_E], [[R(x), R(z)] for x, z in TEAM_E]]
    return {"teamSpawns": team, "hills": hills, "flags": flags}


def waypoints():
    # the deck, the platform, the towpath and the park need nodes (the game's nav graph finds most of the rest)
    for x in (2.0, 12.0, 20.0, 34.0, 44.0, 54.0, 62.0):
        site.waypoint(x, -13.4)
    for x in (4.0, 20.0, 34.0, 50.0, 64.0):
        site.waypoint(x, -37.8)
    for x, z in ((6.0, 30.0), (10.0, 36.0), (6.0, 23.4)):
        site.waypoint(x, z)
    # the embankments, up from the high street end and from the towpath, onto the deck (exact twins, so both teams'
    # climbs are the same to the bots)
    for x, z in ((58.0, 6.0), (58.0, 2.0), (58.0, -2.0), (58.0, -6.0), (58.0, -9.0), (57.0, -12.0), (52.0, -14.5),
                 (58.5, -33.0), (58.0, -29.0), (58.0, -25.0), (58.0, -21.5), (57.0, -17.0)):
        site.waypoint(x, z)
    # the station: hall, the arch into each stair bay, Station Approach
    for x, z in ((0.0, -12.0), (0.0, -16.5), (6.0, -13.9), (0.0, -2.0), (0.0, 4.0)):
        site.waypoint(x, z)
    site.raw({"op": "AutoWaypoints", "spacing": 6})


# ================================================================================================ main

def dress():
    keep_doors()
    obj = objectives()
    lanes()
    surfaces()
    canal()
    viaduct()
    high_street()
    arches_lane()
    canal_street()
    canal_side()
    south_side()
    spawn_end()
    more_dressing()
    signs()
    fol = foliage()
    waypoints()
    # litter (cosmetic, scattered by the game) on every street
    CL(["Litter", "Soda Cans", "Newspaper", "Plastic Bag", "Bottle", "Cardboard Box Small"], 4, 8.2, 74, 19.8, 170, 1)
    CL(["Litter", "Soda Cans", "Trash Bag", "Flattened Cardboard", "Bottles", "Plastic Bag"], 4, -9.4, 47.5, -4.2, 110, 2)
    CL(["Litter", "Soda Cans", "Bottle", "Newspaper", "Plastic Bag"], 2, -40.0, 71, -35.8, 90, 3)
    CL(["Litter", "Trash Bag", "Soda Cans", "Flattened Cardboard", "Plastic Bag"], 2, 38.8, 65, 42.4, 100, 4)
    CL(["Rubble", "Loose Bricks", "Plank", "Litter", "Soda Cans"], 8.8, -34.4, 24, -20, 60, 5)
    CL(["Litter", "Newspaper", "Soda Cans"], 0.5, -18.9, 7.4, -10.2, 12, 6)
    CL(["Litter", "Newspaper", "Soda Cans", "Plastic Bag"], 0.5, -4.0, 3.6, 8.0, 18, 7)
    CL(["Litter", "Soda Cans", "Bottle", "Plastic Bag", "Rubble"], 48.8, -33.0, 65, 6.0, 70, 8)
    CL(["Litter", "Soda Cans", "Trash Bag", "Newspaper"], 4.2, -3.9, 47.5, 0.6, 60, 9)
    CL(["Litter", "Soda Cans", "Bottle"], 14.2, 20.5, 16.0, 38.4, 20, 10)
    CL(["Litter", "Soda Cans", "Bottle", "Plastic Bag", "Newspaper"], 24.6, -34.6, 31.4, 51.0, 60, 11)
    CL(["Litter", "Soda Cans", "Bottle", "Cardboard Box Small", "Trash Bag"], 8.6, -34.4, 47.6, -19.8, 70, 12)
    ops = site.ops + DECALS + CLUTTER
    return dict(obj, ops=ops, foliage=fol)


if __name__ == "__main__":
    extra = dress()
    extra["skybox"] = {"preset": "overcast", "sun": {"time": 15.5, "latitude": 51.5}, "clouds": {"coverage": 0.85, "speed": 9, "heading": 80},
                       "haze": {"color": "#b4b8b8", "strength": 0.3}}
    extra["boundary"] = {"shape": "polygon", "points": [[x, z] for x, z in OUTLINE], "edge": "none", "barrierHeight": 2.6}
    extra["terrain"] = TERRAIN
    write_map(extra)
    st = site.stats()
    print("buildings", len(BUILDINGS), "ops", len(extra["ops"]), "decal ops", len(DECALS), "terrain", round(TER.min, 2), round(TER.max, 2))
    print("counts", {k: v for k, v in st.items() if k not in ("rules", "categories")})
    print("rules", st["rules"])
    print("building props", sum(len(b[6].props) for b in BUILDINGS), "shell boxes", sum(len(b[6].shell) for b in BUILDINGS), "clutter ops", len(CLUTTER), "clutter asked", sum(c["count"] for c in CLUTTER) * 2)
    for s in site.skips[:60]:
        print("  skipped", s)
    tall = site.solid_rects(min_h=1.6, max_y0=1.6)
    seen = [(b, r) for b in TEAM_E for r in TEAM_E if not site.sightline((-b[0], b[1]), r, tall)]
    print("spawn sight lines Blue->Red:", len(seen), seen[:8])
    for x, z in TEAM_E + FFA:
        g = ground(x, z)
        hit = [k for r, y0, y1, k in site.occ if r[0] - 0.4 <= x <= r[2] + 0.4 and r[1] - 0.4 <= z <= r[3] + 0.4 and y0 < g + 1.8 and y1 > g + 0.3]
        if hit:
            print("  spawn", (x, z), "touches", hit)
    if OUT_PNG:
        site.preview(OUT_PNG, (-78, -56, 78, 54), scale=6, spawns=[((x, z), (220, 60, 60)) for x, z in TEAM_E] + [((-x, z), (60, 110, 230)) for x, z in TEAM_E])
