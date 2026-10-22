#!/usr/bin/env python3
"""Generator for Maps/World/04-Paris.json ("Paris Quarter", World Tour pack, order 504): a hillside Paris quarter.
Usage: python3 tools/world/paris.py [repo root] [preview.png]

The map is mirror-symmetric about x = 0 (Blue west, Red east): every building, block, prop and decal on the west half
has a twin at (-x, z) (buildings turned and mirrored to match), so both teams get the same streets and the same walk
to every hill and flag. The quarter climbs from south to north in three levels, like a Montmartre street:
  A  Rue du Moulin (south street, y 0) and the south row of apartment blocks with their courtyards,
  B  Place de la Fontaine (the raised square, y 3) with the fountain, the metro station under it, the two island
     blocks and the team plazas at each end,
  C  Rue des Lilas (north street, y 6) with the north row (brasseries, bakeries, apartment blocks), the back
     courtyards and, at the top, a belvedere (y 9) under a domed basilica.
Stone retaining walls with stairs join the levels; steep cobbled ramps (Rue de la Butte) climb past each plaza.
Rooftop terraces of neighbouring buildings are joined by planks across the alleys.
"""
import json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Maps/World/04-Paris.json")
PNG = sys.argv[2] if len(sys.argv) > 2 else None

LA, LB, LC, LD = 0.0, 3.0, 6.0, 9.0       # level heights
ops = []          # map ops (both halves)
solids = []       # (x0, z0, x1, z1) footprints of solid things at body height, west half + mirrored (for placement checks)
decals = []


def R(v):
    return round(v, 3)


def mirror_yaw(yaw):
    return (360 - yaw) % 360


def add(op, both=True):
    """Adds an op and (both=True) its mirror twin at (-x, z)."""
    ops.append(op)
    if not both:
        return
    m = dict(op)
    kind = op["op"]
    if kind == "Block":
        m["x0"], m["x1"] = R(-op["x1"]), R(-op["x0"])
    elif kind in ("Prop", "Mesh", "Decal", "Spawn", "Waypoint"):
        if abs(op["x"]) < 1e-6:
            return
        m["x"] = R(-op["x"])
        if "yaw" in op:
            m["yaw"] = R(mirror_yaw(op["yaw"]))
        if kind == "Decal":
            if op.get("onto") == "wall+x":
                m["onto"] = "wall-x"
            elif op.get("onto") == "wall-x":
                m["onto"] = "wall+x"
            if "rotation" in op and op["rotation"] >= 0 and op.get("onto", "floor") == "floor":
                m["rotation"] = R((360 - op["rotation"]) % 360)
    elif kind == "Building":
        m["at"] = [R(-op["at"][0])] + op["at"][1:]
        m["yaw"] = mirror_yaw(op.get("yaw", 0))
        v = dict(op.get("variant", {}))
        v["mirror"] = not v.get("mirror", False)
        m["variant"] = v
        if "seed" in op:
            m["seed"] = op["seed"] + 100
    elif kind == "DecalScatter" or kind == "Clutter":
        m["x0"], m["x1"] = R(-op["x1"]), R(-op["x0"])
        m["seed"] = op.get("seed", 0) + 1000
    ops.append(m)


def solid(x0, z0, x1, z1):
    solids.append((min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1)))
    solids.append((-max(x0, x1), min(z0, z1), -min(x0, x1), max(z0, z1)))


def free(x0, z0, x1, z1, pad=0.0):
    for a in solids:
        if x0 - pad < a[2] and x1 + pad > a[0] and z0 - pad < a[3] and z1 + pad > a[1]:
            return False
    return True


def block(x0, y0, z0, x1, y1, z1, c, world=True, both=True, solid_mark=True):
    op = {"op": "Block", "x0": R(x0), "y0": R(y0), "z0": R(z0), "x1": R(x1), "y1": R(y1), "z1": R(z1), "color": c}
    if world:
        op["anchor"] = "world"
    add(op, both)
    if solid_mark:
        solids.append((min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1)))
        if both:
            solids.append((-max(x0, x1), min(z0, z1), -min(x0, x1), max(z0, z1)))


def mesh(kind, x, z, c, y, world=True, both=True, **kw):
    op = {"op": "Mesh", "mesh": kind, "x": R(x), "z": R(z), "y": R(y), "color": c}
    for k, v in kw.items():
        op[k] = R(v) if isinstance(v, float) else v
    if world:
        op["anchor"] = "world"
    add(op, both)


def prop(name, x, z, yaw=0, y=None, both=True, foot=None, world=False, **kw):
    op = {"op": "Prop", "name": name, "x": R(x), "z": R(z)}
    if yaw:
        op["yaw"] = R(yaw % 360)
    if y is not None:
        op["y"] = R(y)
    if world:
        op["anchor"] = "world"
    op.update(kw)
    add(op, both)
    if foot:
        w, d = foot
        if int(round(yaw)) % 180 == 90:
            w, d = d, w
        solids.append((x - w / 2, z - d / 2, x + w / 2, z + d / 2))
        if both:
            solids.append((-x - w / 2, z - d / 2, -x + w / 2, z + d / 2))


def decal(name, x, z, y=0.0, onto="floor", both=True, world=False, **kw):
    op = {"op": "Decal", "decal": name, "x": R(x), "z": R(z), "y": R(y), "onto": onto}
    op.update({k: (R(v) if isinstance(v, float) else v) for k, v in kw.items()})
    if world:
        op["anchor"] = "world"
    add(op, both)


def scatter(names, x0, z0, x1, z1, count, seed, y=0.0, onto="floor", world=False, **kw):
    op = {"op": "DecalScatter", "decals": names, "x0": R(x0), "z0": R(z0), "x1": R(x1), "z1": R(z1), "count": count, "seed": seed, "y": R(y), "onto": onto}
    op.update(kw)
    if world:
        op["anchor"] = "world"
    add(op)


# ============================================================================ palette
COBBLE = [0.42, 0.40, 0.37]
ASPHALT = [0.27, 0.27, 0.29]
PAVE = [0.62, 0.59, 0.54]
KERB = [0.70, 0.68, 0.63]
STONE = [0.72, 0.67, 0.57]
STONE_D = [0.60, 0.56, 0.48]
LIME = [[0.85, 0.80, 0.69], [0.80, 0.77, 0.71], [0.83, 0.74, 0.58], [0.88, 0.84, 0.75]]
ZINC = [0.42, 0.45, 0.50]
DARK = [0.10, 0.11, 0.13]
WHITE = [0.88, 0.88, 0.84]
GREEN = [0.17, 0.28, 0.21]
WOOD = [0.42, 0.29, 0.18]

# ============================================================================ outline (west half; mirrored)
WEST = [(0, -52), (-7, -52), (-7, -38.5), (-34, -38.5), (-34, -46), (-37, -46), (-37, -38.5), (-60, -38.5), (-60, -8.5),
        (-69, -8.5), (-69, -2.5), (-78, -2.5), (-78, 2.5), (-69, 2.5), (-69, 8.5), (-60, 8.5), (-60, 38.5), (-30, 38.5),
        (-30, 45), (-27, 45), (-27, 38.5), (-4, 38.5), (-4, 47), (0, 47)]
OUTLINE = WEST[:-1] + [(-x, z) for x, z in reversed(WEST[1:])]
OUTLINE = [(x, z) for x, z in OUTLINE]
# drop duplicates on the axis
pts = []
for p in OUTLINE:
    if not pts or pts[-1] != p:
        pts.append(p)
if pts[0] == pts[-1]:
    pts.pop()
OUTLINE = pts


def inside(x, z):
    c = False
    n = len(OUTLINE)
    for i in range(n):
        x1, z1 = OUTLINE[i]
        x2, z2 = OUTLINE[(i + 1) % n]
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1) + x1:
            c = not c
    return c


# ============================================================================ terrain
TERRAIN = {
    "cell": 1,
    "margin": 18,
    "surface": {"grass": "#6b665c", "dirt": "#55524c", "rock": "#7d776c", "sand": "#8c8476", "variation": 0.05},
    "features": [
        {"type": "hill", "x": 0, "z": 0, "radius": 400, "height": 0.01, "material": "rock"},       # paint the whole quarter stone
        {"type": "ridge", "points": [[-90, -70], [90, -70]], "width": 40, "height": 7},              # the butte rising beyond the north edge
        {"type": "plateau", "x": 0, "z": -56.3, "w": 220, "d": 87.4, "height": LC, "edge": 1.4, "material": "rock"},
        {"type": "plateau", "x": 0, "z": 0, "w": 200, "d": 20.6, "height": LB, "edge": 1.4, "material": "rock"},
        {"type": "plateau", "x": 0, "z": -52, "w": 15.5, "d": 24.0, "height": LD, "edge": 0.8, "material": "rock"},
        {"type": "ramp", "points": [[-55, -6.5, LB], [-55, -14.5, LC]], "width": 8, "edge": 1.5, "material": "dirt"},
        {"type": "ramp", "points": [[55, -6.5, LB], [55, -14.5, LC]], "width": 8, "edge": 1.5, "material": "dirt"},
        {"type": "ramp", "points": [[-55, 6.5, LB], [-55, 14.5, LA]], "width": 8, "edge": 1.5, "material": "dirt"},
        {"type": "ramp", "points": [[55, 6.5, LB], [55, 14.5, LA]], "width": 8, "edge": 1.5, "material": "dirt"},
        # Metro line 2bis: entrance trenches in both team plazas, a zig-zag tunnel and a station hall under the square
        {"type": "cave", "points": [[-65, -3.5], [-53, -3.5, -2.5], [-39, 3.0, -2.5], [-25, -3.0, -2.5], [-12, 0, -2.5], [12, 0, -2.5],
                                    [25, -3.0, -2.5], [39, 3.0, -2.5], [53, -3.5, -2.5], [65, -3.5]], "width": 5, "height": 3.6, "open": "both"},
        {"type": "cave", "points": [[-13, 0, -2.5], [13, 0, -2.5]], "width": 13, "height": 4.6, "open": "none"},
    ],
}

# ============================================================================ buildings (west half, mirrored to the east)
# North row (level C) faces south onto Rue des Lilas: fronts on z = -21, backs on z = -31 (courtyards behind).
# South row (level A) faces north onto Rue du Moulin: fronts on z = 21, backs on z = 31.
BUILDINGS = []


def building(name, cx, cz, yaw, w, d, seed, variant=None, spawns=False):
    op = {"op": "Building", "name": name, "at": [cx, cz], "yaw": yaw, "seed": seed}
    if variant:
        op["variant"] = variant
    if spawns:
        op["spawns"] = True
    add(op)
    if yaw % 180 == 90:
        w, d = d, w
    solid(cx - w / 2 - 0.2, cz - d / 2 - 0.2, cx + w / 2 + 0.2, cz + d / 2 + 0.2)
    BUILDINGS.append((name, cx, cz, yaw, w, d))


building("Haussmann Apartment", -43, -26, 0, 12, 10, 11, {"palette": "cream limestone"}, spawns=True)
building("Haussmann Apartment", -28, -26, 0, 12, 10, 12, {"palette": "honey stone", "mirror": True})
building("Boulangerie", -18.5, -26, 0, 7, 10, 13, {"palette": "sky blue"})
building("Cafe Brasserie", -9, -26, 0, 12, 10, 14, {"palette": "oxblood"}, spawns=True)
building("Haussmann Apartment", -37, 0, 90, 12, 10, 15, {"palette": "grey limestone"}, spawns=True)
building("Haussmann Apartment", -9, 26, 180, 12, 10, 16, {"palette": "grey limestone"})
building("Haussmann Apartment", -21, 26, 180, 12, 10, 17, {"palette": "cream limestone", "mirror": True}, spawns=True)
building("Haussmann Apartment", -36, 26, 180, 12, 10, 18, {"palette": "honey stone"})
building("Boulangerie", -45.5, 26, 180, 7, 10, 19, {"palette": "burgundy", "mirror": True})

# ============================================================================ objectives
BLUE = [(-66, 5.5), (-63, 6.5), (-60, 5.5), (-66, -7.0), (-62.5, -7.2), (-67, 1.2), (-57.5, 6.6), (-73, 0.0), (-59, -7.2)]
RED = [(-x, z) for x, z in BLUE]
HILLS = [{"x": 0.0, "z": 0.0, "radius": 4.6}, {"x": 0.0, "z": -17.0, "radius": 4.0}, {"x": 0.0, "z": 17.0, "radius": 4.0}]
FLAGS = [[-75.5, 0.0], [75.5, 0.0]]
for x, z in BLUE:
    solid(x - 1.3, z - 1.3, x + 1.3, z + 1.3)
for h in HILLS:
    solids.append((h["x"] - h["radius"], h["z"] - h["radius"], h["x"] + h["radius"], h["z"] + h["radius"]))
for x, z in FLAGS:
    solid(x - 1.5, z - 1.5, x + 1.5, z + 1.5)
# the metro trenches and the stair runs stay clear of props
solid(-66, -6.8, -51, -0.2)

# ============================================================================ retaining walls and stairs
GAPS = [(-4.0, 4.0), (-25.5, -22.5), (22.5, 25.5)]   # stair gaps along both walls


def wall_runs(x0, x1):
    """x ranges of a wall from x0 to x1 (west half and the middle) minus the stair gaps."""
    runs, cur = [], x0
    for a, b in sorted(GAPS):
        if b <= cur or a >= x1:
            continue
        if a > cur:
            runs.append((cur, a))
        cur = max(cur, b)
    if cur < x1:
        runs.append((cur, x1))
    return runs


for a, b in wall_runs(-51, 0):
    # south wall: B (y 3) over Rue du Moulin (y 0), with a 1 m parapet on the square's side
    block(a, -0.4, 10.3, b, LB + 1.0, 11.0, STONE)
    mesh("box", (a + b) / 2, 10.65, STONE_D, LB + 1.0, width=b - a + 0.1, height=0.12, depth=0.9, material="Concrete")
    # north wall: C (y 6) over the square (y 3)
    block(a, LB - 0.4, -12.4, b, LC + 1.0, -11.0, STONE)
    mesh("box", (a + b) / 2, -11.7, STONE_D, LC + 1.0, width=b - a + 0.1, height=0.12, depth=1.6, material="Concrete")
# the mirror of runs crossing x = 0 is handled by add(); runs end at 0 only at a gap, so nothing doubles up


def stairs_down_south(x0, x1, ztop, ytop, steps, rise=0.333, run=0.36, inner=True):
    """A flight from ytop at ztop down towards +z (onto lower ground): a landing, then steps (solid blocks to the ground)."""
    block(x0, ytop - 3.4, ztop - 0.75, x1, ytop, ztop, STONE_D)
    for k in range(steps):
        top = ytop - rise * (k + 1)
        if top <= 0.05:
            break
        block(x0, top - 3.0, ztop + run * k, x1, top, ztop + run * (k + 1), STONE_D)
    # cheek walls
    block(x0 - 0.35, ytop - 3.4, ztop - 0.7, x0, ytop + 0.9, ztop + run * steps * 0.7, STONE)
    if inner:
        block(x1, ytop - 3.4, ztop - 0.7, x1 + 0.35, ytop + 0.9, ztop + run * steps * 0.7, STONE)


def stairs_up_north(x0, x1, zbot, ybot, steps, rise=0.333, run=0.36, inner=True):
    """A flight climbing towards -z from ybot at zbot, then a landing at the top (ybot + steps * rise)."""
    for k in range(steps):
        top = ybot + rise * (k + 1)
        block(x0, ybot - 0.4, zbot - run * (k + 1), x1, top, zbot - run * k, STONE_D)
    ztop = zbot - run * steps
    block(x0, ybot - 0.4, -12.6, x1, ybot + rise * steps, ztop, STONE_D)
    block(x0 - 0.35, ybot - 0.4, -12.4, x0, ybot + rise * steps + 0.9, zbot - run * 2, STONE)
    if inner:
        block(x1, ybot - 0.4, -12.4, x1 + 0.35, ybot + rise * steps + 0.9, zbot - run * 2, STONE)


# central grand stairs (8 m wide), side stairs (3 m)
stairs_down_south(-4.0, 0.0, 11.0, LB, 9, inner=False)
stairs_up_north(-4.0, 0.0, -8.0, LB, 9, inner=False)
stairs_down_south(-25.5, -22.5, 11.0, LB, 9)
stairs_up_north(-25.5, -22.5, -8.0, LB, 9)
# the central flights are 8 m wide in one piece: join the two halves' handrail posts (lamps) instead of cheek walls in the middle
for z in (11.6, -9.0):
    prop("Lantern Street Lamp", -4.6, z, foot=(0.4, 0.4))

# ============================================================================ belvedere stairs (level C -> D at the north jut)
for k in range(9):
    top = LC + 0.333 * (k + 1)
    block(-3.0, LC - 0.4, -38.6 - 0.36 * (k + 1), 0.0, top, -38.6 - 0.36 * k, STONE_D)
block(-3.35, LC - 0.4, -41.9, -3.0, LD + 0.9, -38.6, STONE)
block(-7.0, LC - 0.4, -40.2, -3.35, LD + 1.0, -39.6, STONE)        # belvedere parapet wall over the courtyard
# belvedere railing along its front over the stairs side gaps and benches
prop("Metro Railing", -5.2, -40.6, y=0.0, foot=(3.1, 0.2))
prop("Bench", -4.5, -48.5, yaw=0, foot=(1.8, 0.6))
prop("Lantern Street Lamp", -6.2, -45.0, foot=(0.4, 0.4))
prop("Pigeons", -2.5, -46.0)

# ============================================================================ streets: pavements, kerbs, markings
# Rue des Lilas (C): north pavement -21..-19.4, roadway -19.4..-14.4, south pavement -14.4..-12.4
# Rue du Moulin (A): north pavement 11..13, roadway 13..19, south pavement 19..21


def pavement(x0, z0, x1, z1, y, h=0.14):
    block(x0, y - 0.3, z0, x1, y + h, z1, PAVE)


for a, b in ((-51.0, -3.0),):
    pavement(a, -21.0, b, -19.4, LC)
    pavement(a, -14.4, b, -12.4, LC)
    pavement(a, 19.0, b, 21.0, LA)
    pavement(a, 11.0, b, 13.0, LA)
# the central passage (x -3..3) north of Rue des Lilas: cobbles flush with the road
mesh("box", -1.5, -29.8, COBBLE, LC, width=3.0, height=0.04, depth=17.4, material="Tiles")
mesh("box", -1.5, 29.8, COBBLE, LA, width=3.0, height=0.04, depth=17.4, material="Tiles")
# road surfaces (cosmetic, flush on the terrain)
mesh("box", -27, -16.9, ASPHALT, LC, width=48.0, height=0.03, depth=5.0, material="Concrete")
mesh("box", -27, 16.0, ASPHALT, LA, width=48.0, height=0.03, depth=6.0, material="Concrete")
# French markings: white dashed edge lines (no centre line on a one-way street), zebra crossings, bike symbols
for x in range(-49, -3, 4):
    decal("Lane Dash", x + 0.5, -18.9, y=LC, rotation=90, size=2.0)
    decal("Lane Dash", x + 2.5, 18.5, y=LA, rotation=90, size=2.0)
for x in (-34.5, -1.5):
    decal("Crosswalk Bars", x, -16.9, y=LC, rotation=0, size=4.6)
    decal("Crosswalk Bars", x, 16.0, y=LA, rotation=0, size=5.4)
decal("French Bike Lane Symbol", -42.0, -15.4, y=LC, rotation=0)
decal("French Bike Lane Symbol", -12.0, 13.9, y=LA, rotation=180)
decal("French Livraison Marking", -22.0, -18.4, y=LC, rotation=0)
decal("Lane Dash", -22.0, -19.15, y=LC, rotation=90, size=3.0)
decal("French Livraison Marking", -40.0, 18.2, y=LA, rotation=180)
for x in (-46.0, -30.0, -14.0):
    decal("Manhole Cover", x, -16.6, y=LC)
    decal("Manhole Cover", x + 5, 16.4, y=LA)
for x in range(-49, -3, 6):
    decal("Drain Grate", x, -14.6, y=LC, rotation=0)
    decal("Drain Grate", x + 3, 13.2, y=LA, rotation=0)
# Rue de la Butte (the steep cobbled ramps) is the terrain's painted "dirt": grit, oil and leaves on it
scatter(["Gravel Scatter", "Oil Stain", "Leaf Litter", "Floor Cracks"], -59, -14, -51, 14, 10, 31)

# ============================================================================ the square (level B): Place de la Fontaine
mesh("box", -8.0, 0.0, [0.64, 0.60, 0.53], LB, width=16.0, height=0.05, depth=20.0, material="Tiles")
mesh("cylinder", 0, 0, [0.70, 0.67, 0.60], LB, width=12.0, height=0.07, depth=12.0, segments=32, material="Concrete", both=False)
prop("Tiered Square Fountain", 0.0, 0.0, both=False)
solids.append((-2.6, -2.6, 2.6, 2.6))
# a ring of plane trees and benches round the fountain
TREES = []
for (x, z) in ((-9.0, -6.5), (-9.0, 6.5), (-14.0, 0.0)):
    TREES.append((x, z, LB))
    solid(x - 0.5, z - 0.5, x + 0.5, z + 0.5)
    decal("Round Sewer Grate", x, z, y=LB, size=1.3)
    decal("Leaf Litter", x + 0.8, z + 0.5, y=LB)
for (x, z, yaw) in ((-6.2, -3.6, 300), (-6.2, 3.6, 240), (-3.5, -6.4, 330), (-3.5, 6.4, 210)):
    prop("Bench", x, z, yaw=yaw, foot=(1.6, 0.6))
for (x, z) in ((-6.8, 0.0), (-12.0, -9.0), (-12.0, 9.0)):
    prop("Lantern Street Lamp", x, z, foot=(0.4, 0.4))
prop("Advertising Column", -15.0, -7.0, foot=(1.2, 1.2))
prop("Cast Iron Drinking Fountain", -12.5, 5.2, foot=(0.9, 0.9))
prop("Post Litter Bin", -6.0, -1.8)
# cafe terrace of Le Petit Zinc on the square's west side (tables and parasols, chairs round them)
for (x, z) in ((-19.5, -6.8), (-22.0, -6.8), (-24.5, -6.8), (-19.5, 6.8), (-22.0, 6.8)):
    prop("Bistro Table", x, z, foot=(0.62, 0.62))
    prop("Bistro Chair", x - 0.55, z, yaw=90)
    prop("Bistro Chair", x + 0.55, z, yaw=270)
    prop("Cafe Parasol", x, z + 0.1)
    decal("Cigarette Butts", x + 0.4, z + 0.6, y=LB)
decal("Paper Litter", -22, -4.5, y=LB)
# the island blocks' surroundings: kiosk, flower stall, scooters, a parked delivery van
prop("News Kiosk", -27.5, -2.5, foot=(2.3, 2.3))
prop("Flower Stall", -27.0, 4.2, yaw=90, foot=(1.9, 1.2))
prop("Newspaper Box", -29.6, -4.8)
for (x, z, yaw) in ((-45.5, -7.8, 90), (-44.0, -7.8, 90), (-45.0, 8.0, 270)):
    prop("Moped Scooter", x, z, yaw=yaw, foot=(1.4, 0.5))
prop("Bike Rack", -47.5, 8.2, foot=(1.8, 0.5))
prop("Bicycle", -47.5, 8.4, yaw=8)
prop("Van", -16.8, -8.6, yaw=0, foot=(4.8, 2.0))
decal("French Livraison Marking", -16.8, -8.6, y=LB, rotation=0)
decal("Oil Drips", -16.5, -8.6, y=LB, rotation=90)
for x in (-31.0, -43.0):
    prop("Cast Iron Bollard", x, -9.6, foot=(0.2, 0.2))
    prop("Cast Iron Bollard", x, 9.6, foot=(0.2, 0.2))
scatter(["Floor Cracks", "Water Stain", "Dirt Patch", "Paper Litter", "Cigarette Butts", "Leaf Litter"], -50, -10, -3, 10, 26, 41, y=LB, gap=2.0)

# ============================================================================ team plazas (level B) with the metro entrances
mesh("box", -60.0, 0.0, [0.60, 0.57, 0.52], LB, width=18.0, height=0.04, depth=17.0, material="Tiles")
# railings along the open metro trench, the arch at its mouth, a sign
for x in (-63.5, -60.5, -57.5, -54.5):
    prop("Metro Railing", x, -6.7, foot=(3.1, 0.2))
    prop("Metro Railing", x, -0.3, foot=(3.1, 0.2))
prop("Metro Entrance Arch", -66.4, -3.5, yaw=90, foot=(0.3, 3.4))
block(-66.5, LB + 2.2, -4.35, -66.32, LB + 2.62, -2.65, GREEN, solid_mark=False)
decal("French Sign Metro", -66.2, -3.5, y=LB + 2.42, onto="wall-x", size=1.5)
prop("Lantern Street Lamp", -67.5, -7.4, foot=(0.4, 0.4))
prop("Lantern Street Lamp", -51.5, 7.6, foot=(0.4, 0.4))
prop("Advertising Column", -52.0, -7.6, foot=(1.2, 1.2))
prop("Post Box", -67.8, 7.6)
prop("Bench", -63.0, 7.9, yaw=180, foot=(1.6, 0.6))
prop("Planter", -69.8, -1.8, foot=(1.0, 1.0))
prop("Planter", -69.8, 1.8, foot=(1.0, 1.0))
prop("Flag on a Pole", -76.8, 1.9, flag="france")
prop("Flag on a Pole", -76.8, -1.9, flag="france")
decal("Puddle", -61, 3, y=LB)
scatter(["Floor Cracks", "Dirt Patch", "Cigarette Butts", "Paper Litter"], -69, -8, -51, 8, 10, 51, y=LB, gap=2.0)

# the metro station under the square: platform edges, pillars, benches, ticket machines, lamps, posters
STATION_Y = -2.5
for x in (-9.5, -4.5):
    block(x - 0.35, STATION_Y, -0.35, x + 0.35, STATION_Y + 4.2, 0.35, [0.86, 0.84, 0.78])
mesh("box", -6.5, 0.0, [0.86, 0.84, 0.78], STATION_Y, width=13.0, height=0.03, depth=4.2, material="Tiles")
prop("Bench", -7.0, -1.3, yaw=180, y=STATION_Y, world=True, foot=(1.6, 0.6))
prop("Bench", -7.0, 1.3, yaw=0, y=STATION_Y, world=True, foot=(1.6, 0.6))
prop("Ticket Machine", -11.8, -1.6, yaw=0, y=STATION_Y, world=True, foot=(0.8, 0.6))
prop("Glowing Vending Machine", -11.8, 1.7, yaw=180, y=STATION_Y, world=True, foot=(1.0, 0.8))
prop("Lantern Street Lamp", -2.0, -1.8, y=STATION_Y, world=True, foot=(0.4, 0.4))
for x in (-10.0, -5.0, -1.0):
    decal("Poster Diagonal" if x < -6 else "Poster Circles", x, 2.0, y=STATION_Y + 1.5, onto="wall+z", world=True, reach=5)
    decal("Torn Poster", x - 1.2, -2.0, y=STATION_Y + 1.4, onto="wall-z", world=True, reach=5)
decal("Grime Streaks", -7.5, 2.0, y=STATION_Y + 2.0, onto="wall+z", world=True, reach=5)
decal("Hazard Stripes", -6.5, -2.1, y=STATION_Y, world=True, rotation=0, size=6.0)
decal("Hazard Stripes", -6.5, 2.1, y=STATION_Y, world=True, rotation=0, size=6.0)
scatter(["Paper Litter", "Water Stain", "Footprints", "Cigarette Butts"], -12, -3, -1, 3, 10, 61, y=STATION_Y, world=True)
scatter(["Water Stain", "Mud Patch", "Paper Litter"], -50, -6, -14, 5, 10, 62, y=STATION_Y, world=True)

# ============================================================================ Rue des Lilas (north street, level C)
for x in (-47.0, -35.5, -24.0, -13.0):
    prop("Lantern Street Lamp", x, -13.0, foot=(0.4, 0.4))
for x in (-40.5, -29.0):
    prop("Lantern Street Lamp", x, -20.4, foot=(0.4, 0.4))
CARS = [("Hatchback", -45.0, -18.6, 0), ("Sedan", -30.5, -18.6, 180), ("Taxi", -26.0, 15.0, 0), ("Hatchback", -46.0, 14.2, 180)]
for name, x, z, yaw in CARS:
    prop(name, x, z, yaw=yaw, foot=(4.3, 1.9))
    decal("Oil Drips", x, z, y=LC if z < 0 else LA, rotation=90)
for (x, z, yaw) in ((-38.6, -19.9, 0), (-37.5, -19.9, 0), (-16.0, -19.9, 0), (-33.0, 12.7, 180), (-6.0, 20.0, 90)):
    prop("Moped Scooter", x, z, yaw=yaw + 90, foot=(0.5, 1.4))
for x in (-49.5, -36.5, -23.0, -4.0):
    prop("Cast Iron Bollard", x, -19.2, foot=(0.2, 0.2))
for x in (-43.0, -21.0):
    prop("Post Litter Bin", x, -13.2)
prop("Newspaper Box", -10.5, -13.2)
prop("Cast Iron Drinking Fountain", -51.5, -13.5, foot=(0.9, 0.9))
# brasserie terraces in the central passage (both brasseries' corner fronts face it)
for z in (-23.0, -25.6, -28.2):
    prop("Bistro Table", -2.25, z, foot=(0.62, 0.62))
    prop("Bistro Chair", -2.25, z - 0.6, yaw=0)
    prop("Bistro Chair", -2.25, z + 0.6, yaw=180)
    decal("Cigarette Butts", -1.6, z + 0.3, y=LC)
prop("Sandwich Board", -1.2, -20.6, yaw=200)
# the Boulangerie's morning queue: crates and a bike out front
prop("Bicycle", -21.5, -20.3, yaw=90)
decal("Paper Litter", -18.0, -20.0, y=LC)
scatter(["Floor Cracks", "Water Stain", "Cigarette Butts", "Paper Litter", "Leaf Litter", "Oil Stain"], -51, -21, -3, -12.4, 22, 71, y=LC, gap=2.0)

# ============================================================================ Rue du Moulin (south street, level A)
for x in (-47.0, -35.0, -21.0, -11.0):
    prop("Lantern Street Lamp", x, 11.4, foot=(0.4, 0.4))
for x in (-41.0, -28.5, -15.0):
    prop("Lantern Street Lamp", x, 20.5, foot=(0.4, 0.4))
for (x, z) in ((-50.0, 12.2), (-30.0, 12.2), (-17.0, 12.2)):
    TREES.append((x, z, LA))
    solid(x - 0.5, z - 0.5, x + 0.5, z + 0.5)
    decal("Round Sewer Grate", x, z, y=LA, size=1.3)
prop("Bench", -40.0, 11.7, yaw=0, foot=(1.6, 0.6))
prop("Advertising Column", -8.0, 12.3, foot=(1.2, 1.2))
prop("Post Litter Bin", -24.5, 20.4)
prop("Phone Booth", -33.0, 20.3, yaw=180, foot=(1.0, 1.0))
prop("Box Truck", -12.0, 15.2, yaw=180, foot=(6.5, 2.4))
for x in (-48.5, -42.0, -27.5, -15.5, -3.6):
    prop("Cast Iron Bollard", x, 19.3, foot=(0.2, 0.2))
scatter(["Floor Cracks", "Water Stain", "Cigarette Butts", "Paper Litter", "Leaf Litter", "Oil Stain", "Puddle"], -51, 11, -3, 21, 24, 81, y=LA, gap=2.0)

# ============================================================================ courtyards (behind both rows)
# north (level C, z -38.5..-31) and south (level A, z 31..38.5): split by garden walls with gateways, dressed with bins,
# bikes, planters, washing lines, a few trees; the dead-end alleys run out through the outline at x = +-35.5 / +-28.5.
for (zc0, zc1, y, side) in ((-38.5, -31.0, LC, -1), (31.0, 38.5, LA, 1)):
    for xw in (-50.5, -16.0):
        g0, g1 = (zc0 + 1.0, zc0 + 2.6) if side < 0 else (zc1 - 2.6, zc1 - 1.0)
        block(xw - 0.2, y - 0.3, zc0, xw + 0.2, y + 2.6, g0, STONE)
        block(xw - 0.2, y - 0.3, g1, xw + 0.2, y + 2.6, zc1, STONE)
    for (x, z) in ((-44.5, (zc0 + zc1) / 2), (-23.5, (zc0 + zc1) / 2)):
        TREES.append((x, z, y))
        solid(x - 0.6, z - 0.6, x + 0.6, z + 0.6)
    zb = zc1 - 0.6 if side < 0 else zc0 + 0.6
    zf = zc0 + 0.6 if side < 0 else zc1 - 0.6
    by = 0 if side < 0 else 180
    prop("Wheelie Bin Grey", -40.0, zb, yaw=by, foot=(0.7, 0.8))
    prop("Wheelie Bin", -39.2, zb, yaw=by, foot=(0.7, 0.8))
    prop("Recycling Bin", -25.4, zb, yaw=by, foot=(0.8, 0.8))
    prop("Bicycle", -31.5, zf, yaw=90)
    prop("Large Flower Pot", -19.0, zf, foot=(0.7, 0.7))
    prop("Planter", -11.0, zf, foot=(1.0, 1.0))
    prop("Laundry Line", -46.5, zf + (1.2 if side < 0 else -1.2), yaw=90, foot=(0.3, 0.3))
    prop("Wooden Crate", -13.0, zb, foot=(0.8, 0.8))
    prop("Plastic Chair", -12.0, (zc0 + zc1) / 2 + 1.0, yaw=40)
    for (x, n) in ((-40.0, "Trash Bag"), (-38.8, "Cardboard Box"), (-25.8, "Soda Cans"), (-12.6, "Bottles")):
        prop(n, x + 0.3, zb + (-0.8 if side < 0 else 0.8), yaw=x * 13)
    scatter(["Moss Patch", "Water Stain", "Dirt Patch", "Puddle", "Leaf Litter", "Cracked Tiles"], -59, zc0, -3, zc1, 16, 91 if side < 0 else 92, y=y, gap=2.0)
    scatter(["Graffiti Tag", "Graffiti Scribble", "Peeling Paint", "Grime Streaks", "Wall Cracks", "Water Damage", "Mould"], -59, zc0, -3, zc1, 14,
            93 if side < 0 else 94, y=y, onto="wall")
# the dead-end alleys out of the courtyards
prop("Dumpster", -35.5, -44.6, foot=(1.9, 1.1))
prop("Pallet Stack", -35.5, -42.4, foot=(1.3, 1.3))
prop("Dumpster", -28.5, 43.6, yaw=180, foot=(1.9, 1.1))
prop("Cardboard Stack", -28.5, 41.6, foot=(1.0, 1.0))
decal("Graffiti Throw-Up", -37.0, -44.0, y=LC + 0.6, onto="wall-x", size=2.4)
decal("Graffiti Burst", -27.0, 42.0, y=LA + 1.0, onto="wall+x")
prop("Planter", -2.0, 45.6, foot=(1.0, 1.0))
prop("Bench", -1.4, 44.0, yaw=90, foot=(1.6, 0.6))
decal("Water Damage", -4.0, 45.0, y=LA + 1.2, onto="wall-x")

# ============================================================================ rooftop catwalks
PLANK = [0.48, 0.36, 0.22]
# Haussmann terraces (floor 5) are at ground + 17.1 m, their parapets 1 m higher; steps on each terrace reach the planks.


def terrace_bridge(xa, xb, z0, z1, ground):
    """A plank across an alley between two terraces (xa < xb are the facing wall faces), with steps up to the parapets."""
    deck = ground + 17.1
    top = deck + 1.0
    block(xa - 0.2, top - 0.12, z0, xb + 0.2, top, z1, PLANK, solid_mark=False)
    block(xa - 1.15, deck, z0, xa - 0.3, deck + 0.5, z1, STONE_D, solid_mark=False)
    block(xb + 0.3, deck, z0, xb + 1.15, deck + 0.5, z1, STONE_D, solid_mark=False)


# north row: terrace strip is z -22.45..-21.15; alley x -37..-34 between the two apartment blocks
terrace_bridge(-37.0, -34.0, -22.3, -21.5, LC)
# south row (turned 180): terrace strip z 21.15..22.45; a plank across the alley x -30..-27, steps over the party parapet at x -15
terrace_bridge(-30.0, -27.0, 21.5, 22.3, LA)
block(-15.85, LA + 17.1, 21.4, -15.3, LA + 17.6, 22.2, STONE_D, solid_mark=False)
block(-14.7, LA + 17.1, 21.4, -14.15, LA + 17.6, 22.2, STONE_D, solid_mark=False)
# the sky bridge over the central passage between the two brasserie roofs (roof at ground + 10.7, parapet + 1)
block(-3.2, LC + 11.58, -29.6, 3.2, LC + 11.7, -28.6, PLANK, solid_mark=False, both=False)
for x in (-4.25, 3.35):
    block(x, LC + 10.7, -29.6, x + 0.9, LC + 11.2, -28.6, STONE_D, solid_mark=False, both=False)
for x in (-3.0, 3.0):
    mesh("box", x * 0.98, -29.1, WOOD, LC + 11.7, width=0.08, height=1.0, depth=0.08, both=False)

# ============================================================================ shop signs and street plaques
# Haussmann shop fascias: local (-2.4, 3.75, 5.3); north row (yaw 0) at (cx, -26); island (yaw 90) at (-37, 0);
# south row (yaw 180) at (cx, 26).
SIGN = {(-43, -26): "Paris Sign EpicerieSaintAubin", (-28, -26): "Paris Sign LibrairieMoulin", (-37, 0): "Paris Sign PetitZinc",
        (-9, 26): "Paris Sign PharmacieButte", (-21, 26): "Paris Sign BonFromage", (-36, 26): "Paris Sign HotelLilas"}
for name, cx, cz, yaw, w, d in BUILDINGS:
    if name != "Haussmann Apartment":
        continue
    lx = -2.4
    mirrored = (cx, cz) in ((-28, -26), (-21, 26))
    if mirrored:
        lx = -lx
    sgn = SIGN[(cx, cz)]
    if yaw == 0:
        decal(sgn, cx + lx, cz + 5.9, y=LC + 3.75, onto="wall-z", size=3.4)
    elif yaw == 180:
        decal(sgn, cx - lx, cz - 5.9, y=LA + 3.75, onto="wall+z", size=3.4)
    elif yaw == 90:
        decal(sgn, cx + 5.9, cz - lx, y=LB + 3.75, onto="wall-x", size=3.4)
decal("Paris Sign MaisonLefevre", -18.5, -20.2, y=LC + 3.31, onto="wall-z", size=3.4)
decal("French Sign Boulangerie", -45.5, 20.3, y=LA + 3.31, onto="wall+z", size=3.2)
decal("Paris Sign ChezMargot", -6.6, -20.2, y=LC + 3.75, onto="wall-z", size=3.4)
decal("French Sign Brasserie", -2.2, -23.8, y=LC + 3.75, onto="wall-x", size=3.0)
decal("French Sign Tabac", -27.6, -1.6, y=LB + 1.2, onto="wall-x", size=1.4)
for (x, z, y, onto, sgn) in ((-50.5, -20.6, LC + 3.0, "wall-z", "Paris Plaque RueDesLilas"), (-3.4, -20.6, LC + 3.0, "wall-z", "Paris Plaque RueDesLilas"),
                             (-2.6, -21.8, LC + 3.0, "wall-x", "Paris Plaque PassageDesArtistes"), (-48.6, 20.6, LA + 3.0, "wall+z", "Paris Plaque RueDuMoulin"),
                             (-31.6, 6.4, LB + 3.0, "wall+z", "Paris Plaque PlaceDeLaFontaine"), (-14.4, 20.6, LA + 3.0, "wall+z", "Paris Plaque RueDuMoulin")):
    decal(sgn, x, z, y=y, onto=onto)
# facade grime and old damage on every street front
scatter(["Grime Streaks", "Water Damage", "Wall Cracks", "Plaster Patch", "Peeling Paint", "Soot Plume"], -51, -21.6, -3, -20.4, 16, 101, y=LC + 1.0, onto="wall")
scatter(["Grime Streaks", "Water Damage", "Wall Cracks", "Plaster Patch", "Peeling Paint", "Torn Poster", "Poster Circles"], -51, 20.4, -3, 21.6, 16, 102, y=LA + 1.0, onto="wall")
scatter(["Grime Streaks", "Water Damage", "Moss Patch", "Wall Cracks", "Torn Poster", "Graffiti Scribble"], -51, 10.0, -3, 11.5, 10, 103, y=LA + 0.5, onto="wall")
scatter(["Grime Streaks", "Moss Patch", "Wall Cracks", "Poster Diagonal"], -51, -11.5, -3, -10.4, 10, 104, y=LB + 0.5, onto="wall")

# ============================================================================ the city round the outline (outside the map)
OUT_BLOCKS = [  # (x0, z0, x1, z1, height above its ground, ground)
    (-61, -52, -37.2, -38.7, 19.0, LC), (-33.8, -52, -7.2, -38.7, 21.0, LC), (-37.2, -56, -33.8, -46.2, 20.0, LC),
    (-74, -38.7, -60.2, -12.6, 18.0, LC), (-74, -12.6, -60.2, -8.7, 18.0, LB), (-82, -16, -69.2, -2.7, 17.0, LB),
    (-92, -8, -78.2, 8, 16.0, LB), (-82, 2.7, -69.2, 16, 17.0, LB), (-74, 8.7, -60.2, 12.0, 20.0, LA), (-74, 12.0, -60.2, 52, 20.0, LA),
    (-61, 38.7, -30.2, 52, 19.0, LA), (-30.2, 45.2, -26.8, 56, 18.0, LA), (-26.8, 38.7, -4.2, 52, 21.0, LA), (-4.2, 47.2, 0, 58, 18.0, LA),
    (-20, -66, -7.2, -38.7, 12.0, LD),
]
for i, (x0, z0, x1, z1, h, g) in enumerate(OUT_BLOCKS):
    col = LIME[i % len(LIME)]
    block(x0, g - 1.0, z0, x1, g + h, z1, col)
    block(x0 - 0.2, g + h, z0 - 0.2, x1 + 0.2, g + h + 0.4, z1 + 0.2, [0.86, 0.82, 0.72])
    mesh("box", (x0 + x1) / 2, (z0 + z1) / 2, ZINC, g + h + 0.4, width=x1 - x0 - 1.6, height=2.6, depth=z1 - z0 - 1.6, material="Metal")
    # window panels on the faces that look into the map
    for face in ("N", "S", "E", "W"):
        if face in "NS":
            z = z0 - 0.03 if face == "N" else z1 + 0.03
            if not inside((x0 + x1) / 2, z - 1.0 if face == "N" else z + 1.0):
                continue
            n = int((x1 - x0) // 2.6)
            for k in range(n):
                xx = x0 + (x1 - x0) * (k + 0.5) / n
                for fy in range(1, int(h // 3.2)):
                    mesh("box", xx, z, DARK, g + 1.0 + fy * 3.2, width=1.1, height=1.9, depth=0.06)
        else:
            x = x0 - 0.03 if face == "W" else x1 + 0.03
            if not inside(x - 1.0 if face == "W" else x + 1.0, (z0 + z1) / 2):
                continue
            n = int((z1 - z0) // 2.6)
            for k in range(n):
                zz = z0 + (z1 - z0) * (k + 0.5) / n
                for fy in range(1, int(h // 3.2)):
                    mesh("box", x, zz, DARK, g + 1.0 + fy * 3.2, width=0.06, height=1.9, depth=1.1)
    prop("Chimney Pot Stack", (x0 + x1) / 2 - 2.0, (z0 + z1) / 2, y=g + h + 3.0, world=True, yaw=90 if (z1 - z0) > (x1 - x0) else 0)
# the belvedere's balustrade at the end of the north jut, looking out at the basilica\nblock(-7.2, LD - 0.5, -52.7, 7.2, LD + 1.05, -52.2, STONE, both=False)\n# the basilica on the butte beyond the belvedere: a white dome, a drum and a tall square tower (a landmark from everywhere)
BG = 7.0   # the butte's ground under the basilica
mesh("box", 0, -76, [0.92, 0.90, 0.84], BG, both=False, width=30, height=16, depth=22, material="Concrete")
mesh("cylinder", 0, -76, [0.94, 0.92, 0.86], BG + 16, both=False, width=12, height=6, depth=12, segments=24, material="Concrete")
mesh("sphere", 0, -76, [0.95, 0.93, 0.88], BG + 19, both=False, width=12.5, height=13, depth=12.5, segments=24, material="Concrete")
mesh("cylinder", 0, -76, [0.94, 0.92, 0.86], BG + 31.5, both=False, width=2.4, height=3.5, depth=2.4, segments=12)
mesh("cone", 0, -76, [0.94, 0.92, 0.86], BG + 35, both=False, width=2.8, height=3.0, depth=2.8, segments=12)
for sx in (-10, 10):
    mesh("cylinder", sx, -71, [0.94, 0.92, 0.86], BG + 16, both=False, width=4, height=3, depth=4, segments=16)
    mesh("sphere", sx, -71, [0.95, 0.93, 0.88], BG + 18.5, both=False, width=4.4, height=5, depth=4.4, segments=16)
mesh("box", 0, -90, [0.90, 0.88, 0.82], BG, both=False, width=6, height=40, depth=6, material="Concrete")
mesh("sphere", 0, -90, [0.95, 0.93, 0.88], BG + 40, both=False, width=5, height=5.5, depth=5, segments=16)

# ============================================================================ spawns (free for all), waypoints
FFA = [(-66, 5.5), (-60, -7.2), (-73, 0.0), (-46, -16.5), (-30, -16.0), (-20, 16.0), (-44, 16.5), (-14, -4.0), (-24, 1.0), (-30, -34.5), (-46, 34.5),
       (-2, -36.0), (-2, 43.0), (-54, 30.0), (-54, -30.0)]
for x, z in FFA:
    add({"op": "Spawn", "x": x, "z": z})
# a few waypoints in tight spots (courtyard pockets, the station, the belvedere)
for (x, z) in ((-35.5, -42.0), (-28.5, 42.0), (-3.5, -45.0), (-2.0, 45.5), (-55.0, -24.0), (-55.0, 24.0), (-1.5, -32.0), (-1.5, 32.0)):
    add({"op": "Waypoint", "x": x, "z": z})
add({"op": "AutoWaypoints", "spacing": 6}, both=False)

# ============================================================================ foliage
items = []
for (x, z, y) in TREES:
    for sx in (1, -1):
        items.append({"type": "tree", "x": R(sx * x), "z": R(z), "height": 8.5 if y > 0 else 9.5})
FOLIAGE = {
    "seed": 504,
    "items": items,
    "regions": [
        {"type": "grass", "density": 2.5, "rect": [-59, -38.5, 59, -31.2], "patchiness": 0.7, "color": [0.34, 0.42, 0.22]},
        {"type": "grass", "density": 2.5, "rect": [-59, 31.2, 59, 38.5], "patchiness": 0.7, "color": [0.34, 0.42, 0.22]},
        {"type": "shrub", "density": 0.5, "rect": [-59, -38.4, 59, -36.8], "patchiness": 0.5},
        {"type": "shrub", "density": 0.5, "rect": [-59, 36.8, 59, 38.4], "patchiness": 0.5},
        {"type": "grass", "density": 1.2, "rect": [-7, -52, 7, -40.5], "patchiness": 0.6},
        {"type": "bush", "density": 0.25, "rect": [-7, -52, 7, -47], "scale": [0.8, 1.2]},
    ],
    "surfaces": {"Concrete": 0, "Tiles": 0.04},
}

# ============================================================================ placement check: solid props clear of buildings and walls
def level_at(z):
    return LC if z < -12.4 else (LB if abs(z) <= 10.3 else LA)


bad = []
for o in ops:
    if o["op"] != "Prop" or o.get("anchor") == "world":
        continue
    x, z = o["x"], o["z"]
    for name, cx, cz, yaw, w, d in BUILDINGS:
        for sx in (1, -1):
            if abs(x - sx * cx) < w / 2 + 0.1 and abs(z - cz) < d / 2 + 0.1:
                bad.append((o["name"], x, z, name))
    g = level_at(z)
    for b2 in ops:
        if b2["op"] == "Block" and b2["x0"] - 0.1 < x < b2["x1"] + 0.1 and b2["z0"] - 0.1 < z < b2["z1"] + 0.1 and b2["y1"] > g + 0.3 and b2["y0"] < g + 1.8:
            bad.append((o["name"], x, z, "block"))
for b3 in bad:
    print("  PLACEMENT:", b3)

# ============================================================================ the file
MAP = {
    "format": "call-of-slop-map/1",
    "name": "Paris Quarter",
    "description": "A hillside Paris quarter: apartment blocks you climb floor by floor to the rooftop terraces, brasseries, bakeries, the raised fountain square with a metro station under it, stairs, courtyards and alleys.",
    "author": "Call of Slop",
    "order": 504,
    "maxPlayers": 16,
    "flagPool": ["france"],
    "sky": "#c9c2b6",
    "skybox": {"preset": "clear", "sun": {"time": 17.2, "latitude": 48.9}, "clouds": {"coverage": 0.45, "speed": 6},
               "haze": {"color": "#e8d6b8", "strength": 0.35}, "horizon": "#d4cfc4"},
    "boundary": {"shape": "polygon", "points": [[R(x), R(z)] for x, z in OUTLINE], "edge": "none", "barrierHeight": 30, "groundMargin": 4},
    "terrain": TERRAIN,
    "teamSpawns": [[[x, z] for x, z in BLUE], [[x, z] for x, z in RED]],
    "hills": HILLS,
    "flags": FLAGS,
    "ops": ops,
    "foliage": FOLIAGE,
}
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w") as f:
    f.write(json.dumps(MAP, indent=1) + "\n")
n_dec = sum(1 for o in ops if o["op"] == "Decal") + sum(o["count"] for o in ops if o["op"] == "DecalScatter")
print(f"{OUT}: {len(ops)} ops, {sum(1 for o in ops if o['op'] == 'Block')} blocks, {sum(1 for o in ops if o['op'] == 'Prop')} props, "
      f"{sum(1 for o in ops if o['op'] == 'Mesh')} meshes, ~{n_dec} decals, {sum(1 for o in ops if o['op'] == 'Building')} buildings, {len(items)} trees")

if PNG:
    sys.path.insert(0, os.path.join(REPO, "tools/cluttered"))
    import numpy as np
    from PIL import Image, ImageDraw
    import terrain as tr
    t = tr.generate(dict(TERRAIN, features=[f for f in TERRAIN["features"] if f["type"] != "cave"]), 80, 60)
    S = 6
    W, H = 200 * S, 140 * S
    xs = np.linspace(-100, 100, W)
    zs = np.linspace(-70, 70, H)
    X, Z = np.meshgrid(xs, zs)
    hgt = t.sample(X, Z)
    img = np.zeros((H, W, 3), np.uint8)
    v = np.clip(hgt / 12.0, 0, 1)
    img[..., 0] = (80 + 140 * v).astype(np.uint8)
    img[..., 1] = (80 + 120 * v).astype(np.uint8)
    img[..., 2] = (90 + 60 * v).astype(np.uint8)
    im = Image.fromarray(img)
    d = ImageDraw.Draw(im)
    P = lambda x, z: ((x + 100) * S, (z + 70) * S)
    d.polygon([P(x, z) for x, z in OUTLINE], outline=(255, 0, 0))
    for o in ops:
        if o["op"] == "Block":
            d.rectangle([P(o["x0"], o["z0"]), P(o["x1"], o["z1"])], outline=(40, 40, 40), fill=(150, 140, 120) if o["y1"] - o["y0"] > 2 else (120, 110, 100))
    for name, cx, cz, yaw, w, dd in BUILDINGS:
        for sx in (1, -1):
            x = cx * sx
            d.rectangle([P(x - w / 2, cz - dd / 2), P(x + w / 2, cz + dd / 2)], outline=(0, 0, 160), fill=(200, 190, 170))
            d.text(P(x - w / 2 + 0.5, cz), name[:9], fill=(0, 0, 0))
    for o in ops:
        if o["op"] == "Prop":
            d.ellipse([P(o["x"] - 0.4, o["z"] - 0.4), P(o["x"] + 0.4, o["z"] + 0.4)], fill=(0, 120, 0))
        if o["op"] == "Spawn":
            d.ellipse([P(o["x"] - 0.8, o["z"] - 0.8), P(o["x"] + 0.8, o["z"] + 0.8)], fill=(255, 255, 0))
    for x, z in BLUE:
        d.ellipse([P(x - 0.8, z - 0.8), P(x + 0.8, z + 0.8)], fill=(0, 80, 255))
    for x, z in RED:
        d.ellipse([P(x - 0.8, z - 0.8), P(x + 0.8, z + 0.8)], fill=(255, 40, 40))
    for h in HILLS:
        d.ellipse([P(h["x"] - h["radius"], h["z"] - h["radius"]), P(h["x"] + h["radius"], h["z"] + h["radius"])], outline=(255, 128, 0), width=3)
    im.save(PNG)
    for (x, z) in ((0, 0), (0, -17), (0, 17), (-55, -10), (-55, 10), (-55, -14.5), (-43, -26), (-37, 0), (0, -46), (-60, 0), (-30, -35), (-30, 35)):
        print(f"  ground at ({x}, {z}): {float(t.sample(np.array([x]), np.array([z]))[0]):.2f}")
