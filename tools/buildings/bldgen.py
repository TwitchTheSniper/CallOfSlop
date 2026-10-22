#!/usr/bin/env python3
"""bldgen: a Python port of the game's building generator (Source/Buildings/*.cs), for previews and validation
without the game. Reads Buildings/<Name>/building.json, Buildings/Furnishing/*.json and the prop library, and builds the
same plan the game builds: wall boxes with openings, floors, ceilings, stairs, roofs, extras, doors, windows, waypoints
and furniture. Numbers can differ from the game in the last decimals (Python doubles vs C# floats); the layout and the
checks are the same.

Used by buildings.py (validate / preview / sheet). Plain Python 3 (numpy and Pillow only for the pictures).
"""
import glob
import json
import math
import os
import re

SLAB = 0.2
GROUND_SLAB = 0.1
LINING_T = 0.02
STEP_RISE_MAX = 0.36
DOOR_CLEAR_DEPTH = 1.25
CELL = 0.1

MATERIALS = {"none": -1, "plain": -1, "plaster": -1, "paint": -1, "grid": 0, "concrete": 1, "brick": 2, "wood": 3, "metal": 4, "tiles": 5, "dirt": 6}
MATERIAL_NAMES = {-1: "none", 0: "grid", 1: "concrete", 2: "brick", 3: "wood", 4: "metal", 5: "tiles", 6: "dirt"}


class BuildingError(Exception):
    pass


# ============================================================================== RNG (Foliage.Hash / Foliage.Rng)

M32 = 0xFFFFFFFF


def fhash(a):
    a &= M32
    a ^= a >> 16
    a = (a * 0x7feb352d) & M32
    a ^= a >> 15
    a = (a * 0x846ca68b) & M32
    a ^= a >> 16
    return a


class Rng:
    def __init__(self, seed):
        self.s = fhash((seed ^ 0x9e3779b9) & M32) | 1

    def next(self):
        s = self.s
        s ^= (s << 13) & M32
        s ^= s >> 17
        s ^= (s << 5) & M32
        self.s = s
        return (s >> 8) * (1.0 / 16777216.0)


def fnv(s):
    h = 2166136261
    for ch in s.lower():
        h ^= ord(ch)
        h = (h * 16777619) & M32
    return h


# ============================================================================== colours

COLOR_NAMES = {}


def load_color_names(repo):
    src = open(os.path.join(repo, "Source/Props/PropFile.cs")).read()
    for m in re.finditer(r'\["(\w+)"\] = new\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)', src):
        COLOR_NAMES[m.group(1).lower()] = (float(m.group(2)), float(m.group(3)), float(m.group(4)))


def literal_color(c, where):
    if isinstance(c, list) and len(c) == 3:
        return tuple(float(x) for x in c)
    if isinstance(c, str):
        if c.lower() in COLOR_NAMES:
            return COLOR_NAMES[c.lower()]
        if len(c) == 7 and c[0] == "#":
            return tuple(int(c[i:i + 2], 16) / 255 for i in (1, 3, 5))
    raise BuildingError(f"{where}: colours are a name, [r, g, b] or \"#rrggbb\"; got {c!r}")


# ============================================================================== props

def rot(pitch, yaw, roll):
    p, y, r = map(math.radians, (pitch, yaw, roll))
    Ry = [[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]]
    Rx = [[1, 0, 0], [0, math.cos(p), -math.sin(p)], [0, math.sin(p), math.cos(p)]]
    Rz = [[math.cos(r), -math.sin(r), 0], [math.sin(r), math.cos(r), 0], [0, 0, 1]]

    def mul(A, B):
        return [[sum(A[i][k] * B[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mul(mul(Ry, Rx), Rz)


class PropDef:
    def __init__(self, d, source, pack=None):
        self.name = d["name"]
        self.source = source
        self.pack = pack
        self.solid = d.get("collision", "solid") != "cosmetic"
        self.parts = d["parts"]
        mn = [1e9, 1e9, 1e9]
        mx = [-1e9, -1e9, -1e9]
        for p in self.parts:
            s = p["size"]
            off = p.get("offset", [0, s[1] / 2, 0])
            r = p.get("rotation") or [p.get("pitch", 0), p.get("yaw", 0), p.get("roll", 0)]
            R = rot(*r)
            h = [s[0] / 2, s[1] / 2, s[2] / 2]
            ext = [sum(abs(R[i][k]) * h[k] for k in range(3)) for i in range(3)]
            for i in range(3):
                mn[i] = min(mn[i], off[i] - ext[i])
                mx[i] = max(mx[i], off[i] + ext[i])
        self.min = mn
        self.max = mx

    @property
    def key(self):
        return self.name if self.pack is None else self.pack + "/" + self.name


def re_strip(f):
    return f


def load_prop_dir(folder, pack=None):
    out = {}
    for f in sorted(glob.glob(os.path.join(folder, "**/*.json"), recursive=True)):
        try:
            d = json.load(open(re_strip(f)))
            if not isinstance(d, dict) or "parts" not in d or "name" not in d:
                continue  # not a prop file (e.g. Props/Flags/flags.json)
            p = PropDef(d, f, pack)
            out[p.name.lower()] = p
        except Exception as ex:  # noqa: BLE001
            print(f"  prop skipped: {f}: {ex}")
    return out


def world_bounds(p, x, z, yaw_deg, scale):
    """Map.WorldBounds on the plan: (x0, z0, x1, z1) of a prop at (x, z) turned yaw_deg."""
    a = math.radians(yaw_deg)
    cs, sn = math.cos(a), math.sin(a)
    cx = (p.min[0] + p.max[0]) / 2 * scale
    cz = (p.min[2] + p.max[2]) / 2 * scale
    hx = (p.max[0] - p.min[0]) / 2 * scale
    hz = (p.max[2] - p.min[2]) / 2 * scale
    ex = abs(hx * cs) + abs(hz * sn)
    ez = abs(hx * sn) + abs(hz * cs)
    wx = x + cx * cs + cz * sn
    wz = z - cx * sn + cz * cs
    return (wx - ex, wz - ez, wx + ex, wz + ez)


# ============================================================================== rects

def r_overlaps(a, b, eps=0.001):
    return b[0] < a[2] - eps and b[2] > a[0] + eps and b[1] < a[3] - eps and b[3] > a[1] + eps


def r_inset(r, m):
    return (r[0] + m, r[1] + m, r[2] - m, r[3] - m)


def r_union(a, b):
    return (min(a[0], b[0]), min(a[1], b[1]), max(a[2], b[2]), max(a[3], b[3]))


def r_minus(r, c):
    if not r_overlaps(r, c):
        return [r]
    out = []
    cz0, cz1 = max(r[1], c[1]), min(r[3], c[3])
    if c[1] > r[1] + 0.001:
        out.append((r[0], r[1], r[2], c[1]))
    if c[3] < r[3] - 0.001:
        out.append((r[0], c[3], r[2], r[3]))
    if c[0] > r[0] + 0.001:
        out.append((r[0], cz0, c[0], cz1))
    if c[2] < r[2] - 0.001:
        out.append((c[2], cz0, r[2], cz1))
    return out


def r_subtract(rects, cuts):
    lst = list(rects)
    for c in cuts:
        lst = [p for r in lst for p in r_minus(r, c) if p[2] - p[0] > 0.01 and p[3] - p[1] > 0.01]
    return lst


def r_contains(r, x, z, eps=0.0):
    return r[0] - eps <= x <= r[2] + eps and r[1] - eps <= z <= r[3] + eps


# ============================================================================== parsing (BuildingFile.cs)

def keys(d, where, *allowed):
    if not isinstance(d, dict):
        raise BuildingError(f"{where}: expected an object")
    for k in d:
        if k not in allowed:
            raise BuildingError(f"{where}: unknown key \"{k}\" (it takes: {', '.join(allowed)})")


def need(d, k, where):
    if k not in d:
        raise BuildingError(f"{where}: missing \"{k}\"")
    return d[k]


def rng_check(v, lo, hi, where):
    if not isinstance(v, (int, float)) or v < lo or v > hi:
        raise BuildingError(f"{where}: {v} is outside {lo} to {hi}")
    return float(v)


def ascii_text(s, where, mx):
    if not isinstance(s, str):
        raise BuildingError(f"{where}: expected text")
    if len(s) > mx:
        raise BuildingError(f"{where}: at most {mx} characters")
    if any(ord(c) < 32 or ord(c) > 126 for c in s):
        raise BuildingError(f"{where}: plain ASCII only")
    return s


def rect(v, where):
    if not isinstance(v, list) or len(v) != 4:
        raise BuildingError(f"{where}: a rect is [x0, z0, x1, z1]")
    r = (min(v[0], v[2]), min(v[1], v[3]), max(v[0], v[2]), max(v[1], v[3]))
    if r[2] - r[0] < 0.5 or r[3] - r[1] < 0.5:
        raise BuildingError(f"{where}: a rect is at least 0.5 m each way, got {v}")
    return r


def material(m, where):
    m = str(m).lower()
    if m not in MATERIALS:
        raise BuildingError(f"{where}: material \"{m}\" is brick, concrete, wood, metal, tiles, dirt, grid or none")
    return MATERIALS[m]


DIRS = {"+x": (1, 0), "x": (1, 0), "e": (1, 0), "east": (1, 0), "-x": (-1, 0), "w": (-1, 0), "west": (-1, 0),
        "+z": (0, 1), "z": (0, 1), "s": (0, 1), "south": (0, 1), "-z": (0, -1), "n": (0, -1), "north": (0, -1)}


class Def:
    pass


def parse_building(text, source, folder=""):
    try:
        r = json.loads(re.sub(r"//[^\n]*", "", text))
    except json.JSONDecodeError as ex:
        raise BuildingError(f"{source}: not valid JSON ({ex})")
    w = source
    keys(r, w, "format", "name", "theme", "category", "description", "author", "seed", "wallThickness", "storeyHeight", "doorHeight",
         "exterior", "interior", "floor", "ceiling", "trim", "door", "palettes", "floors", "floorCount", "stairs", "roof", "extras", "anchors")
    if "format" in r and r["format"] != "call-of-slop-building/1":
        raise BuildingError(f"{w}: \"format\" is \"call-of-slop-building/1\"")
    d = Def()
    d.name = ascii_text(need(r, "name", w), f"{w}: name", 40)
    d.folder = folder
    w = f"{source} ({d.name})"
    d.theme = ascii_text(r.get("theme", ""), f"{w}: theme", 200)
    d.category = r.get("category", "residential").lower()
    if d.category not in ("residential", "commercial", "industrial", "special"):
        raise BuildingError(f"{w}: \"category\" is residential, commercial, industrial or special")
    d.description = ascii_text(r.get("description", ""), f"{w}: description", 300)
    d.author = ascii_text(r.get("author", ""), f"{w}: author", 60)
    d.seed = int(r.get("seed", 0))
    d.t = rng_check(r.get("wallThickness", 0.25), 0.1, 0.6, f"{w}: wallThickness")
    d.storey = rng_check(r.get("storeyHeight", 3.0), 2.4, 8, f"{w}: storeyHeight")
    d.door_height = rng_check(r.get("doorHeight", 2.1), 1.9, 4, f"{w}: doorHeight")
    d.palettes = []
    for pname, cols in (r.get("palettes") or {}).items():
        if not isinstance(cols, dict):
            raise BuildingError(f"{w}: palette \"{pname}\" is an object of colours")
        d.palettes.append((pname, {k.lower(): literal_color(v, f"{w}: palette {pname}, {k}") for k, v in cols.items()}))
    allkeys = sorted({k for _, c in d.palettes for k in c})
    for pname, c in d.palettes:
        for k in allkeys:
            if k not in c:
                raise BuildingError(f"{w}: palette \"{pname}\" has no \"{k}\" (every palette needs the same keys)")

    def color(c, where):
        if isinstance(c, str) and len(c) > 1 and c[0] == "$":
            k = c[1:].lower()
            if not d.palettes:
                raise BuildingError(f"{where}: \"{c}\" names a palette colour, but the building has no \"palettes\"")
            if k not in d.palettes[0][1]:
                raise BuildingError(f"{where}: no palette has \"{k}\"")
            return ("$", k, d.palettes[0][1][k])
        return ("=", None, literal_color(c, where))

    def surface(e, name, fallback):
        where = f"{w}: {name}"
        if isinstance(e, dict):
            keys(e, where, "color", "material")
            c = color(e["color"], where) if "color" in e else (fallback[0] if fallback else ("=", None, (0.6, 0.6, 0.6)))
            lay = material(e["material"], where) if "material" in e else (fallback[1] if fallback else -1)
            return (c, lay)
        return (color(e, where), fallback[1] if fallback else -1)

    d.color = color
    d.exterior = (("$", "wall", (0.62, 0.58, 0.52)), 1)
    d.interior = (("$", "interior", (0.86, 0.83, 0.76)), -1)
    d.floor = (("$", "floor", (0.5, 0.42, 0.32)), 3)
    d.ceiling = (("$", "ceiling", (0.9, 0.9, 0.88)), -1)
    d.trim = (("$", "trim", (0.9, 0.9, 0.9)), -1)
    d.door = ("$", "door", (0.45, 0.3, 0.2))
    if "exterior" in r: d.exterior = surface(r["exterior"], "exterior", d.exterior)
    if "interior" in r: d.interior = surface(r["interior"], "interior", d.interior)
    if "floor" in r: d.floor = surface(r["floor"], "floor", d.floor)
    if "ceiling" in r: d.ceiling = surface(r["ceiling"], "ceiling", d.ceiling)
    if "trim" in r: d.trim = surface(r["trim"], "trim", d.trim)
    if "door" in r: d.door = color(r["door"], f"{w}: door")

    floors = need(r, "floors", w)
    if not isinstance(floors, list) or not floors:
        raise BuildingError(f"{w}: \"floors\" is a list of at least one floor")
    d.floors = [parse_floor(fe, d, surface, color, f"{w}: floor {i}") for i, fe in enumerate(floors)]
    if sum(1 for f in d.floors if f["repeat"]) > 1:
        raise BuildingError(f"{w}: only one floor can be \"repeat\"")
    d.min_floors = len(d.floors)
    d.max_floors = 12 if any(f["repeat"] for f in d.floors) else len(d.floors)
    d.default_floors = len(d.floors)
    if "floorCount" in r:
        d.default_floors = int(r["floorCount"])
        if not d.min_floors <= d.default_floors <= d.max_floors:
            raise BuildingError(f"{w}: floorCount is {d.min_floors} to {d.max_floors}")
    d.stairs = []
    for i, se in enumerate(r.get("stairs", [])):
        sw = f"{w}: stairs {i}"
        keys(se, sw, "floor", "rect", "dir", "color", "railing", "alt")
        dd = str(need(se, "dir", sw)).lower()
        if dd not in DIRS:
            raise BuildingError(f"{sw}: \"dir\" is +x, -x, +z or -z")
        st = {"floor": int(need(se, "floor", sw)), "rect": rect(need(se, "rect", sw), sw), "dir": DIRS[dd],
              "surface": surface(se["color"], "stairs color", None) if "color" in se else None, "railing": se.get("railing", True), "alt": None}
        if "alt" in se:
            keys(se["alt"], f"{sw}: alt", "rect", "dir")
            st["alt"] = (rect(need(se["alt"], "rect", sw), sw), DIRS[str(need(se["alt"], "dir", sw)).lower()])
        d.stairs.append(st)
    ro = r.get("roof", {})
    keys(ro, f"{w}: roof", "type", "ridge", "pitch", "overhang", "color", "parapet", "dir")
    rt = ro.get("type", "flat").lower()
    if rt == "lean-to":
        rt = "shed"
    if rt not in ("flat", "gable", "hip", "shed", "none"):
        raise BuildingError(f"{w}: roof \"type\" is flat, gable, hip, shed or none")
    d.roof = {"type": rt, "ridge": ro.get("ridge", "").lower(), "pitch": rng_check(ro.get("pitch", 30), 5, 60, f"{w}: roof pitch"),
              "overhang": rng_check(ro.get("overhang", 0.3), 0, 1.5, f"{w}: roof overhang"), "parapet": rng_check(ro.get("parapet", 0.6), 0, 2, f"{w}: parapet"),
              "surface": surface(ro["color"], "roof color", None) if "color" in ro else None,
              "dir": DIRS[str(ro.get("dir", "+z")).lower()]}
    d.extras = []
    for i, ee in enumerate(r.get("extras", [])):
        ew = f"{w}: extras {i}"
        t = str(need(ee, "type", ew)).lower()
        x = {"type": t}
        if t == "block":
            keys(ee, ew, "type", "min", "max", "color")
            a, b = need(ee, "min", ew), need(ee, "max", ew)
            x["min"] = [min(a[k], b[k]) for k in range(3)]
            x["max"] = [max(a[k], b[k]) for k in range(3)]
            x["surface"] = surface(ee["color"], "block color", None) if "color" in ee else None
        elif t == "prop":
            keys(ee, ew, "type", "name", "at", "yaw", "scale", "onRoof")
            x["name"] = need(ee, "name", ew)
            x["onRoof"] = ee.get("onRoof", False)
            at = need(ee, "at", ew)
            x["at"] = [at[0], at[1], at[2]] if len(at) == 3 else [at[0], 0, at[1]]
            x["yaw"] = ee.get("yaw", 0)
            x["scale"] = ee.get("scale", 1)
        elif t in ("porch", "awning"):
            keys(ee, ew, "type", "rect", "height", "roof", "posts", "color")
            x["rect"] = rect(need(ee, "rect", ew), ew)
            x["height"] = rng_check(ee.get("height", 2.6), 2, 8, f"{ew}: height")
            x["roof"] = ee.get("roof", True)
            x["posts"] = ee.get("posts", t == "porch")
            x["surface"] = surface(ee["color"], "porch color", None) if "color" in ee else None
        elif t in ("platform", "mezzanine"):
            x["type"] = "platform"
            keys(ee, ew, "type", "rect", "floor", "height", "railing", "color", "stairs")
            x["rect"] = rect(need(ee, "rect", ew), ew)
            x["floor"] = int(ee.get("floor", 0))
            x["height"] = rng_check(need(ee, "height", ew), 1, 8, f"{ew}: height")
            x["railing"] = rng_check(ee.get("railing", 1.0), 0, 2, f"{ew}: railing")
            x["surface"] = surface(ee["color"], "platform color", None) if "color" in ee else None
            x["stairs"] = None
            if "stairs" in ee:
                st = ee["stairs"]
                keys(st, f"{ew}: stairs", "rect", "dir")
                dd = str(need(st, "dir", ew)).lower()
                if dd not in DIRS:
                    raise BuildingError(f"{ew}: stairs \"dir\" is +x, -x, +z or -z")
                x["stairs"] = (rect(need(st, "rect", ew), ew), DIRS[dd])
        elif t == "balcony":
            keys(ee, ew, "type", "rect", "floor", "railing", "color")
            x["rect"] = rect(need(ee, "rect", ew), ew)
            x["floor"] = int(need(ee, "floor", ew))
            x["railing"] = rng_check(ee.get("railing", 1.0), 0, 2, f"{ew}: railing")
            x["surface"] = surface(ee["color"], "balcony color", None) if "color" in ee else None
        else:
            raise BuildingError(f"{ew}: \"type\" is block, prop, porch, balcony or awning")
        d.extras.append(x)
    d.anchors = []
    for i, ae in enumerate(r.get("anchors", [])):
        aw = f"{w}: anchors {i}"
        keys(ae, aw, "type", "name", "floor", "at")
        if ae["type"] not in ("spawn", "hill", "flag", "point"):
            raise BuildingError(f"{aw}: \"type\" is spawn, hill, flag or point")
        d.anchors.append({"type": ae["type"], "name": ae.get("name", ""), "floor": ae.get("floor", 0), "at": need(ae, "at", aw)})
    d.local_props = {}
    d.local_rules = {}
    return d


def parse_floor(fe, d, surface, color, w):
    keys(fe, w, "height", "rooms", "openings", "autoWindows", "repeat")
    f = {"height": rng_check(fe["height"], 2.4, 10, f"{w}: height") if "height" in fe else 0, "repeat": fe.get("repeat", False),
         "auto": parse_auto(fe["autoWindows"], f"{w}: autoWindows") if "autoWindows" in fe else None, "rooms": [], "openings": []}
    rooms = need(fe, "rooms", w)
    if not rooms:
        raise BuildingError(f"{w}: \"rooms\" is a list of at least one room")
    for i, re_ in enumerate(rooms):
        rw = f"{w}, room {i}"
        keys(re_, rw, "id", "type", "rect", "rects", "floor", "wall", "ceiling", "furnish", "props", "windows", "autoWindows", "openTo", "seed", "rules")
        rid = ascii_text(need(re_, "id", rw), rw, 40)
        rw = f"{w}, room \"{rid}\""
        rects = []
        if "rect" in re_:
            rects.append(rect(re_["rect"], rw))
        for x in re_.get("rects", []):
            rects.append(rect(x, rw))
        if not rects:
            raise BuildingError(f"{rw}: needs \"rect\"")
        room = {"id": rid, "type": str(need(re_, "type", rw)).lower(), "rects": rects,
                "floor": surface(re_["floor"], f"room {rid} floor", None) if "floor" in re_ else None,
                "wall": surface(re_["wall"], f"room {rid} wall", None) if "wall" in re_ else None,
                "ceiling": surface(re_["ceiling"], f"room {rid} ceiling", None) if "ceiling" in re_ else None,
                "furnish": re_.get("furnish", "auto").lower(), "windows": re_.get("windows", True),
                "auto": parse_auto(re_["autoWindows"], f"{rw}: autoWindows") if "autoWindows" in re_ else None,
                "openTo": [o.lower() for o in re_.get("openTo", [])], "seed": int(re_.get("seed", 0)), "rules": re_.get("rules"), "props": []}
        if room["furnish"] == "props":
            room["furnish"] = "explicit"
        if room["furnish"] not in ("auto", "none", "explicit"):
            raise BuildingError(f"{rw}: \"furnish\" is auto, none or explicit")
        for j, pe in enumerate(re_.get("props", [])):
            pw = f"{rw}, props {j}"
            keys(pe, pw, "name", "at", "wall", "along", "yaw", "y", "scale")
            room["props"].append({"name": need(pe, "name", pw), "at": pe.get("at", [0, 0]), "wall": pe.get("wall", "").upper(), "along": pe.get("along", 0),
                                  "yaw": pe.get("yaw", 0), "y": pe.get("y", 0), "scale": pe.get("scale", 1)})
        f["rooms"].append(room)
    ids = [r["id"].lower() for r in f["rooms"]]
    if len(set(ids)) != len(ids):
        raise BuildingError(f"{w}: two rooms share an id")
    for i, oe in enumerate(fe.get("openings", [])):
        ow = f"{w}, opening {i}"
        keys(oe, ow, "type", "at", "width", "sill", "head", "material", "hinge", "swing", "open", "front", "health", "color")
        t = str(need(oe, "type", ow)).lower()
        t = {"opening": "arch", "doorway": "arch", "breach": "hole"}.get(t, t)
        if t not in ("door", "window", "arch", "hole"):
            raise BuildingError(f"{ow}: \"type\" is door, window, arch or hole")
        if t != "door" and any(k in oe for k in ("hinge", "swing", "material", "open")):
            raise BuildingError(f"{ow}: \"hinge\", \"swing\", \"material\" and \"open\" are for doors")
        at = need(oe, "at", ow)
        o = {"kind": t, "x": at[0], "z": at[1], "width": oe.get("width", 1.2 if t == "window" else 1.0),
             "bottom": oe.get("sill", 0.9 if t in ("window", "hole") else 0.0), "top": oe.get("head", 2.2 if t == "window" else -1),
             "material": oe.get("material", "wood"), "hingeRight": oe.get("hinge", "left") == "right", "swingOut": oe.get("swing", "in") == "out",
             "open": oe.get("open", False), "front": oe.get("front", False), "health": oe.get("health", 0),
             "color": color(oe["color"], ow) if "color" in oe else None, "auto": False}
        f["openings"].append(o)
    return f


def parse_auto(e, w):
    keys(e, w, "spacing", "width", "sill", "head", "health")
    a = {"spacing": e.get("spacing", 3.0), "width": e.get("width", 1.2), "sill": e.get("sill", 0.9), "head": e.get("head", 2.2), "health": e.get("health", 0)}
    if a["head"] <= a["sill"] + 0.3:
        raise BuildingError(f"{w}: \"head\" must be at least 0.3 m above \"sill\"")
    if a["width"] > a["spacing"] - 0.3:
        raise BuildingError(f"{w}: \"width\" must be at least 0.3 m less than \"spacing\"")
    return a


PLACES = ("wall", "corner", "centre", "center", "floor", "on", "beside", "front", "around", "ceiling")


def parse_rule(text, source, fallback):
    r = json.loads(re.sub(r"//[^\n]*", "", text))
    keys(r, source, "format", "type", "description", "items")
    rule = {"type": r.get("type", fallback).lower(), "source": source, "items": []}
    ids = set()
    for i, it in enumerate(need(r, "items", source)):
        w = f"{source}: items {i}"
        keys(it, w, "id", "props", "place", "count", "chance", "of", "y", "prefer", "gap", "distance", "area", "turn", "scale")
        place = str(need(it, "place", w)).lower()
        if place not in PLACES:
            raise BuildingError(f"{w}: \"place\" is {', '.join(PLACES)}")
        place = "centre" if place == "center" else place
        c = it.get("count", 1)
        cmin, cmax = (c[0], c[1]) if isinstance(c, list) else (c, c)
        item = {"id": it.get("id", ""), "props": list(need(it, "props", w)), "place": place, "min": cmin, "max": cmax, "chance": it.get("chance", 1.0),
                "of": it.get("of", ""), "y": it.get("y"), "prefer": it.get("prefer", "any"), "gap": it.get("gap", 0.08), "distance": it.get("distance", 0.1),
                "area": it.get("area", [0, 1e9]), "turn": it.get("turn", 0), "scale": it.get("scale", 1.0)}
        if not item["props"]:
            raise BuildingError(f"{w}: \"props\" needs at least one name")
        if item["id"]:
            if item["id"] in ids:
                raise BuildingError(f"{w}: another item is already called \"{item['id']}\"")
            ids.add(item["id"])
        if item["of"] and item["of"] not in ids:
            raise BuildingError(f"{w}: \"of\" names \"{item['of']}\", but no earlier item has that id")
        if place in ("on", "beside", "front", "around") and not item["of"]:
            raise BuildingError(f"{w}: \"{place}\" needs \"of\"")
        rule["items"].append(item)
    return rule


# ============================================================================== library

class Library:
    def __init__(self, repo):
        self.repo = repo
        load_color_names(repo)
        self.props = load_prop_dir(os.path.join(repo, "Props"))
        self.rules = {}
        self.buildings = {}
        self.errors = []
        for f in sorted(glob.glob(os.path.join(repo, "Buildings/Furnishing/*.json"))):
            try:
                rule = parse_rule(open(f).read(), os.path.relpath(f, repo), os.path.splitext(os.path.basename(f))[0])
                self.rules[rule["type"]] = rule
            except (BuildingError, json.JSONDecodeError) as ex:
                self.errors.append(str(ex))
        for f in sorted(glob.glob(os.path.join(repo, "Buildings/*/building.json"))):
            folder = os.path.basename(os.path.dirname(f))
            if folder.startswith("_"):
                continue
            try:
                d = parse_building(open(f).read(), os.path.relpath(f, repo), folder)
                d.local_props = load_prop_dir(os.path.join(os.path.dirname(f), "Props"), "Buildings/" + folder)
                d.local_rules = {}
                for rf in sorted(glob.glob(os.path.join(os.path.dirname(f), "Furnishing/*.json"))):
                    rule = parse_rule(open(rf).read(), os.path.relpath(rf, repo), os.path.splitext(os.path.basename(rf))[0])
                    d.local_rules[rule["type"]] = rule
                self.buildings[d.name.lower()] = d
            except BuildingError as ex:
                self.errors.append(str(ex))

    def find_prop(self, d, name):
        if name.lower().startswith("global:"):
            return self.props.get(name[7:].strip().lower())
        return d.local_props.get(name.lower()) or self.props.get(name.lower())

    def find(self, name):
        return self.buildings.get(name.lower()) or next((b for b in self.buildings.values() if b.folder.lower() == name.lower()), None)


# ============================================================================== generator (BuildingGen.cs / BuildingFurnish.cs)

class Seg:
    __slots__ = ("along_x", "c", "a", "b", "neg", "pos", "holes")

    def __init__(self, along_x, c, a, b, neg, pos):
        self.along_x, self.c, self.a, self.b, self.neg, self.pos = along_x, c, a, b, neg, pos
        self.holes = []

    @property
    def exterior(self):
        return self.neg < 0 or self.pos < 0


def build_segs(items, joined):
    groups = {}
    for r, o in items:
        for along_x, c, a, b, pos in ((True, r[1], r[0], r[2], True), (True, r[3], r[0], r[2], False), (False, r[0], r[1], r[3], True), (False, r[2], r[1], r[3], False)):
            groups.setdefault((1 if along_x else 0, round(c * 1000)), []).append((c, a, b, o, pos))
    segs = []
    for (ax, _), edges in sorted(groups.items()):
        pts = sorted({p for e in edges for p in (e[1], e[2])})
        cur = None
        for k in range(len(pts) - 1):
            mid = (pts[k] + pts[k + 1]) / 2
            neg = pos = -1
            for e in edges:
                if e[1] < mid < e[2]:
                    if e[4]:
                        pos = e[3]
                    else:
                        neg = e[3]
            wall = neg != pos and not (neg >= 0 and pos >= 0 and joined(neg, pos))
            if not wall:
                cur = None
                continue
            if cur is not None and cur.neg == neg and cur.pos == pos and abs(cur.b - pts[k]) < 1e-4:
                cur.b = pts[k + 1]
            else:
                cur = Seg(ax == 1, edges[0][0], pts[k], pts[k + 1], neg, pos)
                segs.append(cur)
    return segs


class Plan:
    def __init__(self):
        self.shell = []      # (x0, y0, z0, x1, y1, z1, rgb, layer, tag)
        self.doors = []      # dict
        self.windows = []    # dict
        self.props = []      # dict
        self.waypoints = []
        self.rooms = []
        self.stairs = []
        self.notes = []
        self.footprint = []
        self.floors = 0
        self.levels = []
        self.roof_y = 0


class Generator:
    def __init__(self, lib, d, variant=None, seed=0, origin=(0, 0, 0), quarter=0):
        self.lib, self.d = lib, d
        self.v = dict(variant or {})
        self.seed = seed if seed else d.seed
        self.origin = origin
        self.q = quarter % 4
        self.mirror = bool(self.v.get("mirror", False))
        pal = self.v.get("palette")
        self.palette = d.palettes[0][1] if d.palettes else {}
        if pal:
            m = [c for n, c in d.palettes if n.lower() == pal.lower()]
            if not m:
                raise BuildingError(f"building \"{d.name}\" has no palette \"{pal}\"")
            self.palette = m[0]
        self.t = d.t
        self.plan = Plan()
        self.rng_state = fhash((self.seed * 2654435761) & M32 ^ 0xb11d1)

    def rand(self):
        self.rng_state = fhash((self.rng_state + 0x9e3779b9) & M32)
        return (self.rng_state >> 8) / 16777216.0

    def col(self, c):
        kind, key, val = c
        return self.palette.get(key, val) if kind == "$" else val

    # ---- frame
    def P(self, x, y, z):
        if self.mirror:
            x = -x
        wx, wz = [(x, z), (z, -x), (-x, -z), (-z, x)][self.q]
        return (self.origin[0] + wx, y, self.origin[2] + wz)

    def V(self, dx, dz):
        if self.mirror:
            dx = -dx
        return [(dx, dz), (dz, -dx), (-dx, -dz), (-dz, dx)][self.q]

    def yaw_w(self, deg):
        return ((-deg if self.mirror else deg) + self.q * 90) % 360

    def box_w(self, x0, y0, z0, x1, y1, z1):
        a = self.P(x0, y0, z0)
        b = self.P(x1, y1, z1)
        return (min(a[0], b[0]), min(a[1], b[1]), min(a[2], b[2]), max(a[0], b[0]), max(a[1], b[1]), max(a[2], b[2]))

    def add(self, x0, y0, z0, x1, y1, z1, s, tag="shell"):
        if x1 - x0 < 0.005 or y1 - y0 < 0.005 or z1 - z0 < 0.005:
            return
        self.plan.shell.append(self.box_w(x0, y0, z0, x1, y1, z1) + (self.col(s[0]), s[1], tag))

    def add_along(self, ax, a, b, c0, c1, y0, y1, s, tag="shell"):
        if ax:
            self.add(a, y0, c0, b, y1, c1, s, tag)
        else:
            self.add(c0, y0, a, c1, y1, b, s, tag)

    # ---- run
    def run(self):
        d = self.d
        self.expand_floors()
        self.roof = self.v.get("roof", d.roof["type"])
        n = len(self.floors)
        self.level = [0.0] * (n + 1)
        self.level[0] = self.origin[1] + GROUND_SLAB
        for f in range(n):
            self.level[f + 1] = self.level[f] + (self.floors[f]["height"] or d.storey)
        self.plan.floors = n
        self.plan.levels = list(self.level)
        self.plan.roof_y = self.level[n]
        self.holes_in = [[] for _ in range(n + 1)]
        self.reserved = [[] for _ in range(n + 1)]
        self.openings = []
        self.segs = []
        self.low_ceil = {}
        self.platform_stairs = []
        for f in range(n):
            self.check_rooms(f)
            self.segs.append(build_segs(self.room_items(f), lambda a, b, f=f: self.joined(f, a, b)))
            for a, b, mid, nn, ln in self.open_joints(f):
                if a < b:
                    self.plan.waypoints += [self.P(mid[0] - nn[0] * 0.9, self.level[f], mid[1] - nn[1] * 0.9), self.P(mid[0] + nn[0] * 0.9, self.level[f], mid[1] + nn[1] * 0.9)]
        for f in range(n):
            for o in self.floors[f]["openings"]:
                self.attach(f, o, False)
        for f in range(n):
            self.auto_windows(f)
        self.plan_stairs()
        for f in range(n):
            for s in self.segs[f]:
                self.emit_wall(f, s)
            self.emit_floor(f)
        self.emit_roof()
        for a in self.openings:
            self.emit_opening(a)
        self.emit_stairs()
        self.emit_extras()
        for a in d.anchors:
            f = self.floor_of(a["floor"])
            self.plan.waypoints  # anchors are not waypoints
        for f in range(n):
            for r in range(len(self.floors[f]["rooms"])):
                if self.v.get("furnish", True):
                    self.furnish(f, r)
                else:
                    self.room_record(f, r, self.pois(f, r), None)
        for room in self.floors[0]["rooms"]:
            for rc in room["rects"]:
                b = self.box_w(rc[0] - self.t / 2, 0, rc[1] - self.t / 2, rc[2] + self.t / 2, 1, rc[3] + self.t / 2)
                self.plan.footprint.append((b[0], b[2], b[3], b[5]))
        return self.plan

    def expand_floors(self):
        d = self.d
        want = self.v.get("floors") or d.default_floors
        if want < d.min_floors or want > d.max_floors:
            raise BuildingError(f"building \"{d.name}\" has {d.min_floors} to {d.max_floors} floors, not {want}")
        extra = want - len(d.floors)
        self.floors, self.orig, copy = [], [], []
        for i, f in enumerate(d.floors):
            for k in range(1 + extra if f["repeat"] else 1):
                self.floors.append(f)
                self.orig.append(i)
                copy.append(k)
        self.stairs = []
        for e in range(len(self.floors)):
            for s in d.stairs:
                if s["floor"] == self.orig[e]:
                    if copy[e] % 2 == 1 and s["alt"]:
                        s = dict(s, rect=s["alt"][0], dir=s["alt"][1])
                    self.stairs.append((s, e))

    def floor_of(self, orig):
        return self.orig.index(orig) if orig in self.orig else min(orig, len(self.floors) - 1)

    def where(self, f):
        return f"building \"{self.d.name}\", floor {f}"

    def room_items(self, f):
        return [(rc, i) for i, room in enumerate(self.floors[f]["rooms"]) for rc in room["rects"]]

    def check_rooms(self, f):
        items = self.room_items(f)
        rooms = self.floors[f]["rooms"]
        for i in range(len(items)):
            for j in range(i + 1, len(items)):
                if r_overlaps(items[i][0], items[j][0]):
                    raise BuildingError(f"{self.where(f)}: rooms \"{rooms[items[i][1]]['id']}\" and \"{rooms[items[j][1]]['id']}\" overlap")
        ids = {r["id"].lower() for r in rooms}
        for r in rooms:
            for o in r["openTo"]:
                if o not in ids:
                    raise BuildingError(f"{self.where(f)}: room \"{r['id']}\" is openTo \"{o}\", which isn't a room on this floor")

    def joined(self, f, a, b):
        ra, rb = self.floors[f]["rooms"][a], self.floors[f]["rooms"][b]
        return rb["id"].lower() in ra["openTo"] or ra["id"].lower() in rb["openTo"]

    def open_joints(self, f):
        out = []
        rooms = self.floors[f]["rooms"]
        for a in range(len(rooms)):
            for b in range(len(rooms)):
                if a == b or not self.joined(f, a, b):
                    continue
                for ra in rooms[a]["rects"]:
                    for rb in rooms[b]["rects"]:
                        if abs(ra[2] - rb[0]) < 0.01 or abs(ra[0] - rb[2]) < 0.01:
                            lo, hi = max(ra[1], rb[1]), min(ra[3], rb[3])
                            if hi - lo >= 1:
                                east = abs(ra[2] - rb[0]) < 0.01
                                out.append((a, b, (ra[2] if east else ra[0], (lo + hi) / 2), (1 if east else -1, 0), hi - lo))
                        elif abs(ra[3] - rb[1]) < 0.01 or abs(ra[1] - rb[3]) < 0.01:
                            lo, hi = max(ra[0], rb[0]), min(ra[2], rb[2])
                            if hi - lo >= 1:
                                south = abs(ra[3] - rb[1]) < 0.01
                                out.append((a, b, ((lo + hi) / 2, ra[3] if south else ra[1]), (0, 1 if south else -1), hi - lo))
        return out

    def room_at(self, f, x, z):
        for i, room in enumerate(self.floors[f]["rooms"]):
            for r in room["rects"]:
                if r[0] < x < r[2] and r[1] < z < r[3]:
                    return i
        return -1

    def clear_height(self, f):
        return self.level[f + 1] - self.level[f] - SLAB

    def attach(self, f, o, soft):
        w = f"{self.where(f)}: {o['kind']} at ({o['x']}, {o['z']})"
        seg = None
        for s in self.segs[f]:
            on = (abs(s.c - o["z"]) < 0.06 and s.a < o["x"] < s.b) if s.along_x else (abs(s.c - o["x"]) < 0.06 and s.a < o["z"] < s.b)
            if on:
                seg = s
                break
        if seg is None:
            if soft:
                return False
            raise BuildingError(f"{w}: not on a wall (walls run along the room rect edges)")
        along = o["x"] if seg.along_x else o["z"]
        half = o["width"] / 2
        margin = self.t / 2 + 0.05
        if along - half < seg.a + margin - 0.001 or along + half > seg.b - margin + 0.001:
            if soft:
                return False
            raise BuildingError(f"{w}: {o['width']} m wide doesn't fit its wall ({seg.a:.2f} to {seg.b:.2f}, minus {margin:.2f} m at each end)")
        bottom = 0.0 if o["kind"] in ("door", "arch") else o["bottom"]
        top = o["top"] if o["top"] > 0 else self.d.door_height
        clear = self.clear_height(f)
        if top > clear - 0.05:
            if soft:
                return False
            raise BuildingError(f"{w}: its top ({top}) is above the ceiling ({clear:.2f} m clear)")
        if top < bottom + 0.3:
            if soft:
                return False
            raise BuildingError(f"{w}: head must be at least 0.3 m above sill")
        for h in seg.holes:
            if along - half < h[1] + 0.099 and along + half > h[0] - 0.099:
                if soft:
                    return False
                raise BuildingError(f"{w}: overlaps another opening in the same wall")
        seg.holes.append((along - half, along + half, bottom, top))
        self.openings.append({"o": o, "s": seg, "f": f, "along": along, "bottom": bottom, "top": top})
        return True

    def auto_windows(self, f):
        floor = self.floors[f]
        for s in self.segs[f]:
            if not s.exterior:
                continue
            ri = s.neg if s.neg >= 0 else s.pos
            room = floor["rooms"][ri]
            spec = room["auto"] or floor["auto"]
            if spec is None or not room["windows"]:
                continue
            lo = s.a + self.t / 2 + 0.35
            hi = s.b - self.t / 2 - 0.35
            usable = hi - lo
            if usable < spec["width"]:
                continue
            count = 1 + int(math.floor((usable - spec["width"]) / spec["spacing"]))
            span = (count - 1) * spec["spacing"]
            start = (lo + hi) / 2 - span / 2
            head = min(spec["head"], self.clear_height(f) - 0.1)
            for k in range(count):
                u = start + k * spec["spacing"]
                o = {"kind": "window", "width": spec["width"], "bottom": spec["sill"], "top": head, "health": spec["health"], "auto": True,
                     "x": u if s.along_x else s.c, "z": s.c if s.along_x else u, "front": False, "color": None}
                self.attach(f, o, True)

    def room_wall(self, f, r):
        return (self.floors[f]["rooms"][r]["wall"] if r >= 0 else None) or self.d.interior

    def room_floor(self, f, r):
        return (self.floors[f]["rooms"][r]["floor"] if r >= 0 else None) or self.d.floor

    def room_ceiling(self, f, r):
        return (self.floors[f]["rooms"][r]["ceiling"] if r >= 0 else None) or self.d.ceiling

    def roof_surface(self):
        return self.d.roof["surface"] or (("$", "roof", (0.36, 0.33, 0.31)), 5)

    def emit_wall(self, f, s):
        t = self.t
        y0 = self.origin[1] if f == 0 else self.level[f]
        y1 = self.level[f + 1]
        lf = self.level[f]
        holes = sorted(((h[0], h[1], lf + h[2], lf + h[3]) for h in s.holes))
        open_top = f == len(self.floors) - 1 and self.roof == "none"
        self.cut_wall(s.along_x, s.a - t / 2, s.b + t / 2, s.c - t / 2, s.c + t / 2, y0, y1 if (s.exterior or open_top) else y1 - SLAB, holes,
                      self.d.exterior if s.exterior else self.d.interior, "ext" if s.exterior else "int")
        lining_top = y1 if open_top else y1 - SLAB
        for side in (0, 1):
            r = s.neg if side == 0 else s.pos
            if r < 0:
                continue
            dr = -1 if side == 0 else 1
            c0 = s.c + dr * t / 2
            c1 = c0 + dr * LINING_T
            a, b = s.a, s.b
            if not self.room_continues(f, r, s, a - 0.05, dr):
                a += t / 2
            if not self.room_continues(f, r, s, b + 0.05, dr):
                b -= t / 2
            self.cut_wall(s.along_x, a, b, min(c0, c1), max(c0, c1), lf, lining_top, holes, self.room_wall(f, r), "lining")

    def room_continues(self, f, r, s, along, dr):
        across = s.c + dr * 0.05
        x, z = (along, across) if s.along_x else (across, along)
        return self.room_at(f, x, z) == r

    def cut_wall(self, ax, a0, b0, c0, c1, y0, y1, holes, surf, tag):
        cursor = a0
        for h in holes:
            ha, hb = max(h[0], a0), min(h[1], b0)
            if hb <= ha:
                continue
            self.add_along(ax, cursor, ha, c0, c1, y0, y1, surf, tag)
            self.add_along(ax, ha, hb, c0, c1, y0, max(y0, h[2]), surf, tag)
            self.add_along(ax, ha, hb, c0, c1, min(y1, h[3]), y1, surf, tag)
            cursor = hb
        self.add_along(ax, cursor, b0, c0, c1, y0, y1, surf, tag)

    def emit_floor(self, f):
        rooms = self.floors[f]["rooms"]
        lf = self.level[f]
        t = self.t
        for r, room in enumerate(rooms):
            for rc in room["rects"]:
                if f == 0:
                    self.add(rc[0], self.origin[1], rc[1], rc[2], lf, rc[3], self.room_floor(f, r), "floor")
                else:
                    for p in r_subtract([rc], self.holes_in[f]):
                        self.add(p[0], lf - SLAB, p[1], p[2], lf, p[3], self.room_floor(f, r), "floor")
                top = f == len(self.floors) - 1
                if not top or self.roof != "none":
                    cy = self.level[f + 1] - SLAB
                    for p in r_subtract([r_inset(rc, t / 2)], self.holes_in[f + 1]):
                        self.add(p[0], cy - LINING_T, p[1], p[2], cy, p[3], self.room_ceiling(f, r), "ceiling")
        if f < len(self.floors) - 1:
            above = [rc for room in self.floors[f + 1]["rooms"] for rc in room["rects"]]
            exposed = r_subtract([rc for room in rooms for rc in room["rects"]], above)
            y = self.level[f + 1]
            for p in exposed:
                self.add(p[0], y - SLAB, p[1], p[2], y, p[3], self.roof_surface(), "roof")
            if exposed:
                items = [(p, 0) for p in exposed] + [(a, 1) for a in above]
                for s in build_segs(items, lambda a, b: False):
                    if (s.neg == 0 and s.pos < 0) or (s.pos == 0 and s.neg < 0):
                        self.add_along(s.along_x, s.a - t / 2, s.b + t / 2, s.c - t / 2, s.c + t / 2, y, y + 1.0, self.d.exterior, "parapet")

    def emit_roof(self):
        top = len(self.floors) - 1
        y = self.level[top + 1]
        rects = [rc for room in self.floors[top]["rooms"] for rc in room["rects"]]
        t = self.t
        if self.roof == "none":
            return
        if self.roof != "flat" and self.holes_in[top + 1]:
            raise BuildingError(f"building \"{self.d.name}\": stairs onto the roof need a flat roof")
        for p in r_subtract(rects, self.holes_in[top + 1]):
            self.add(p[0], y - SLAB, p[1], p[2], y, p[3], self.roof_surface(), "roof")
        if self.roof == "flat":
            parapet = max(self.d.roof["parapet"], 0.1)
            for s in self.segs[top]:
                if s.exterior:
                    self.add_along(s.along_x, s.a - t / 2, s.b + t / 2, s.c - t / 2, s.c + t / 2, y, y + parapet, self.d.exterior, "parapet")
            return
        b = rects[0]
        for rc in rects[1:]:
            b = r_union(b, rc)
        b = r_inset(b, -(t / 2 + self.d.roof["overhang"]))
        W, D = b[2] - b[0], b[3] - b[1]
        ridge = self.d.roof["ridge"]
        ridge_x = ridge == "x" or (ridge == "" and W >= D)
        tan = math.tan(math.radians(self.d.roof["pitch"]))
        surf = self.roof_surface()
        if self.roof == "shed":
            dx, dz = self.d.roof["dir"]
            span = W if dx != 0 else D
            h = span * tan
            n = max(2, min(24, round(h / 0.25)))
            for i in range(n):
                keep = span * (1 - i / n)
                if dx > 0:
                    r = (b[2] - keep, b[1], b[2], b[3])
                elif dx < 0:
                    r = (b[0], b[1], b[0] + keep, b[3])
                elif dz > 0:
                    r = (b[0], b[3] - keep, b[2], b[3])
                else:
                    r = (b[0], b[1], b[2], b[1] + keep)
                self.add(r[0], y + i * h / n, r[1], r[2], y + (i + 1) * h / n, r[3], surf, "roof")
            return
        hs0 = (D if ridge_x else W) / 2
        hl0 = (W if ridge_x else D) / 2
        height = hs0 * tan
        layers = max(2, min(24, round(height / 0.25)))
        dh = height / layers
        cx, cz = (b[0] + b[2]) / 2, (b[1] + b[3]) / 2
        for i in range(layers):
            hs = hs0 * (1 - i / layers)
            hl = max(hl0 - (hs0 - hs), hs) if self.roof == "hip" else hl0
            if ridge_x:
                self.add(cx - hl, y + i * dh, cz - hs, cx + hl, y + (i + 1) * dh, cz + hs, surf, "roof")
            else:
                self.add(cx - hs, y + i * dh, cz - hl, cx + hs, y + (i + 1) * dh, cz + hl, surf, "roof")

    def plan_stairs(self):
        n = len(self.floors)
        t = self.t
        self.stair_plans = []
        for s, f in self.stairs:
            r = s["rect"]
            w = f"{self.where(f)}: stairs at {list(r)}"
            cx, cz = (r[0] + r[2]) / 2, (r[1] + r[3]) / 2
            room = self.room_at(f, cx, cz)
            if room < 0 or not any(r[0] >= x[0] + t / 2 - 0.01 and r[2] <= x[2] - t / 2 + 0.01 and r[1] >= x[1] + t / 2 - 0.01 and r[3] <= x[3] - t / 2 + 0.01
                                   for x in self.floors[f]["rooms"][room]["rects"]):
                raise BuildingError(f"{w}: must lie inside one room, clear of its walls")
            dx, dz = s["dir"]
            length = (r[2] - r[0]) if dx != 0 else (r[3] - r[1])
            rise = self.level[f + 1] - self.level[f]
            steps = math.ceil(rise / STEP_RISE_MAX - 1e-9)
            if length / steps < 0.2:
                raise BuildingError(f"{w}: too short for {steps} steps (needs at least {steps * 0.2:.1f} m along dir)")
            low = (r[0] if dx > 0 else r[2] if dx < 0 else cx, r[1] if dz > 0 else r[3] if dz < 0 else cz)
            high = (r[2] if dx > 0 else r[0] if dx < 0 else cx, r[3] if dz > 0 else r[1] if dz < 0 else cz)
            bottom = (low[0] - dx * 0.7, low[1] - dz * 0.7)
            top = (high[0] + dx * 0.7, high[1] + dz * 0.7)
            if self.room_at(f, *bottom) < 0:
                raise BuildingError(f"{w}: the foot of the stairs {bottom} isn't inside a room (leave 0.7 m in front of the first step)")
            if (self.room_at(f + 1, *top) if f + 1 < n else self.room_at(f, *top)) < 0:
                raise BuildingError(f"{w}: the top of the stairs {top} doesn't land in a room on the floor above (leave 0.7 m beyond the last step)")
            if f + 1 >= n and self.roof != "flat":
                raise BuildingError(f"{w}: stairs onto the roof need a flat roof")
            self.holes_in[f + 1].append(r)
            width = (r[3] - r[1]) if dx != 0 else (r[2] - r[0])
            self.reserved[f].append(grow(r, bottom, width))
            self.reserved[f + 1].append(grow(r_inset(r, -0.1), top, width))
            self.stair_plans.append((s, f, r, bottom, top))

    def emit_stairs(self):
        n = len(self.floors)
        for s, f, r, bottom, top in self.stair_plans:
            lf = self.level[f]
            rise = self.level[f + 1] - lf
            steps = math.ceil(rise / STEP_RISE_MAX - 1e-9)
            dx, dz = s["dir"]
            length = (r[2] - r[0]) if dx != 0 else (r[3] - r[1])
            run = length / steps
            room = self.room_at(f, (r[0] + r[2]) / 2, (r[1] + r[3]) / 2)
            surf = s["surface"] or self.room_floor(f, room)
            for k in range(1, steps + 1):
                y1 = self.level[f + 1] if k == steps else lf + k * rise / steps
                if dx > 0 or dz > 0:
                    o = r[0] if dx != 0 else r[1]
                    a, b = o + (k - 1) * run, o + k * run
                else:
                    o = r[2] if dx != 0 else r[3]
                    a, b = o - k * run, o - (k - 1) * run
                if dx != 0:
                    self.add(a, lf, r[1], b, y1, r[3], surf, "stairs")
                else:
                    self.add(r[0], lf, a, r[2], y1, b, surf, "stairs")
            pb = self.P(bottom[0], lf, bottom[1])
            pt = self.P(top[0], self.level[f + 1], top[1])
            self.plan.waypoints += [pb, pt]
            self.plan.stairs.append((pb, pt))
            if not s["railing"]:
                continue
            upper = f + 1 if f + 1 < n else f
            yr = self.level[f + 1]
            if dx != 0:
                sides = [(True, r[1], r[0], r[2], -1), (True, r[3], r[0], r[2], 1), (False, r[0] if dx > 0 else r[2], r[1], r[3], -1 if dx > 0 else 1)]
            else:
                sides = [(False, r[0], r[1], r[3], -1), (False, r[2], r[1], r[3], 1), (True, r[1] if dz > 0 else r[3], r[0], r[2], -1 if dz > 0 else 1)]
            for ax, c, a0, b0, o in sides:
                if self.near_wall(upper, ax, c, a0, b0):
                    continue
                c0 = c - 0.05 if o < 0 else c
                self.add_along(ax, a0, b0, c0, c0 + 0.05, yr, yr + 1.0, self.d.trim, "railing")

    def near_wall(self, f, ax, c, a, b):
        for s in self.segs[f]:
            if s.along_x == ax and abs(s.c - c) < self.t / 2 + 0.12 and min(b, s.b) - max(a, s.a) > (b - a) * 0.5:
                return True
        return False

    def emit_opening(self, at):
        o, s, f = at["o"], at["s"], at["f"]
        lf = self.level[f]
        sign = (1 if s.pos >= 0 else -1) if s.exterior else 1
        if o.get("swingOut"):
            sign = -sign
        n = (0, sign) if s.along_x else (sign, 0)
        centre = (at["along"], s.c) if s.along_x else (s.c, at["along"])
        half = o["width"] / 2
        if o["kind"] in ("door", "arch"):
            sw = (centre[0] + n[0] * 0.9, centre[1] + n[1] * 0.9)
            ot = (centre[0] - n[0] * 0.9, centre[1] - n[1] * 0.9)
            self.plan.waypoints += [self.P(sw[0], lf, sw[1]), self.P(ot[0], lf, ot[1])]
        if o["kind"] == "door":
            right = (-n[1], n[0])
            hs = 1 if o["hingeRight"] else -1
            hinge = (centre[0] + right[0] * hs * (half - 0.02), centre[1] + right[1] * hs * (half - 0.02))
            lw = o["width"] - 0.04
            h = at["top"] - at["bottom"] - 0.02
            th = 0.06
            a, b = at["along"] - lw / 2, at["along"] + lw / 2
            box = self.box_w(a, lf, s.c - th / 2, b, lf + h, s.c + th / 2) if s.along_x else self.box_w(s.c - th / 2, lf, a, s.c + th / 2, lf + h, b)
            mat = o["material"]
            health = o["health"] or {"metal": 400, "glass": 60}.get(mat, 150)
            colr = self.col(o["color"]) if o["color"] else ((0.62, 0.78, 0.86) if mat == "glass" else self.col(self.d.door))
            doors = self.v.get("doors", "closed")
            start_open = True if doors == "open" else (self.rand() < 0.5 if doors == "random" else o["open"])
            self.plan.doors.append({"box": box, "hinge": self.P(hinge[0], lf, hinge[1]), "closedDir": self.V(-right[0] * hs, -right[1] * hs), "openDir": self.V(*n),
                                    "material": mat, "health": health, "color": colr, "open": start_open, "front": o["front"], "floor": f,
                                    "swing": self.P(centre[0] + n[0] * 0.9, lf, centre[1] + n[1] * 0.9), "other": self.P(centre[0] - n[0] * 0.9, lf, centre[1] - n[1] * 0.9)})
        elif o["kind"] == "window":
            a, b = at["along"] - half + 0.01, at["along"] + half - 0.01
            box = self.box_w(a, lf + at["bottom"], s.c - 0.02, b, lf + at["top"], s.c + 0.02) if s.along_x else self.box_w(s.c - 0.02, lf + at["bottom"], a, s.c + 0.02, lf + at["top"], b)
            self.plan.windows.append({"box": box, "health": o.get("health") or 1, "floor": f, "sill": at["bottom"], "exterior": s.exterior})
            if s.exterior and at["bottom"] > 0.2:
                out = -1 if s.pos >= 0 else 1
                c0 = s.c + out * self.t / 2
                c1 = c0 + out * 0.08
                self.add_along(s.along_x, at["along"] - half - 0.05, at["along"] + half + 0.05, min(c0, c1), max(c0, c1), lf + at["bottom"] - 0.06, lf + at["bottom"], self.d.trim, "sill")

    def inside_footprint(self, x, z):
        return any(r_contains(r_inset(rc, -self.t / 2 - 0.05), x, z) for room in self.floors[0]["rooms"] for rc in room["rects"])

    def emit_extras(self):
        base = self.origin[1]
        l0 = self.level[0]
        for x in self.d.extras:
            t = x["type"]
            if t == "block":
                self.add(x["min"][0], base + x["min"][1], x["min"][2], x["max"][0], base + x["max"][1], x["max"][2], x["surface"] or self.d.trim, "extra")
            elif t == "prop":
                p = self.lib.find_prop(self.d, x["name"])
                if p is None:
                    raise BuildingError(f"building \"{self.d.name}\": extras: unknown prop \"{x['name']}\"")
                py = self.level[len(self.floors)] + x["at"][1] if x["onRoof"] else base + x["at"][1]
                self.emit_prop(p, x["at"][0], py, x["at"][2], x["yaw"], x["scale"], "exterior")
            elif t in ("porch", "awning"):
                r = x["rect"]
                surf = x["surface"] or self.d.trim
                if t == "porch":
                    self.add(r[0], base, r[1], r[2], l0, r[3], x["surface"] or self.d.floor, "extra")
                top = base + x["height"]
                if x["roof"]:
                    self.add(r[0] - 0.1, top - 0.15, r[1] - 0.1, r[2] + 0.1, top, r[3] + 0.1, surf, "extra")
                if x["posts"] and x["roof"]:
                    for px, pz in ((r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3])):
                        if self.inside_footprint(px, pz):
                            continue
                        cx = min(max(px, r[0] + 0.08), r[2] - 0.08)
                        cz = min(max(pz, r[1] + 0.08), r[3] - 0.08)
                        self.add(cx - 0.08, l0 if t == "porch" else base, cz - 0.08, cx + 0.08, top - 0.15, cz + 0.08, surf, "extra")
            elif t == "platform":
                self.emit_platform(x)
            elif t == "balcony":
                for f in range(len(self.floors)):
                    if self.orig[f] == x["floor"]:
                        self.emit_balcony(x, f)

    def emit_balcony(self, x, f):
        if True:
            if True:
                y = self.level[f]
                r = x["rect"]
                self.add(r[0], y - SLAB, r[1], r[2], y, r[3], x["surface"] or self.d.floor, "extra")
                if x["railing"] <= 0.01:
                    return
                for ax, c, a, b, o in ((True, r[1], r[0], r[2], -1), (True, r[3], r[0], r[2], 1), (False, r[0], r[1], r[3], -1), (False, r[2], r[1], r[3], 1)):
                    mid = (a + b) / 2
                    px, pz = (mid, c + o * (self.t / 2 + 0.1)) if ax else (c + o * (self.t / 2 + 0.1), mid)
                    if self.room_at(f, px, pz) >= 0 or self.near_wall(f, ax, c, a, b):
                        continue
                    c0 = c if o < 0 else c - 0.05
                    self.add_along(ax, a, b, c0, c0 + 0.05, y, y + x["railing"], self.d.trim, "railing")

    def emit_platform(self, x):
        f = self.floor_of(x["floor"])
        lf = self.level[f]
        top = lf + x["height"]
        r = x["rect"]
        w = f"{self.where(f)}: platform at {list(r)}"
        if top > self.level[f + 1] - SLAB - 1.9:
            raise BuildingError(f"{w}: too high, no headroom under the ceiling (needs 1.9 m)")
        surf = x["surface"] or self.d.floor
        self.add(r[0], top - 0.2, r[1], r[2], top, r[3], surf, "platform")
        self.low_ceil.setdefault(f, []).append((r, x["height"] - 0.25))
        landing = 0
        if x["stairs"]:
            s, (dx, dz) = x["stairs"]
            steps = math.ceil(x["height"] / STEP_RISE_MAX - 1e-9)
            length = (s[2] - s[0]) if dx != 0 else (s[3] - s[1])
            if length / steps < 0.2:
                raise BuildingError(f"{w}: its stairs are too short for {steps} steps")
            run = length / steps
            for k in range(1, steps + 1):
                y1 = top if k == steps else lf + k * x["height"] / steps
                if dx > 0 or dz > 0:
                    o = s[0] if dx != 0 else s[1]
                    a, b = o + (k - 1) * run, o + k * run
                else:
                    o = s[2] if dx != 0 else s[3]
                    a, b = o - k * run, o - (k - 1) * run
                if dx != 0:
                    self.add(a, lf, s[1], b, y1, s[3], surf, "stairs")
                else:
                    self.add(s[0], lf, a, s[2], y1, b, surf, "stairs")
            cx, cz = (s[0] + s[2]) / 2, (s[1] + s[3]) / 2
            low = (s[0] if dx > 0 else s[2] if dx < 0 else cx, s[1] if dz > 0 else s[3] if dz < 0 else cz)
            high = (s[2] if dx > 0 else s[0] if dx < 0 else cx, s[3] if dz > 0 else s[1] if dz < 0 else cz)
            bottom = (low[0] - dx * 0.7, low[1] - dz * 0.7)
            up = (high[0] + dx * 0.6, high[1] + dz * 0.6)
            if not r_contains(r, *up):
                raise BuildingError(f"{w}: its stairs must arrive on it (0.6 m past the top step is {up})")
            if self.room_at(f, *bottom) < 0:
                raise BuildingError(f"{w}: the foot of its stairs {bottom} isn't inside a room")
            landing = 1 if dz > 0 else 2 if dz < 0 else 3 if dx > 0 else 4
            pb, pt = self.P(bottom[0], lf, bottom[1]), self.P(up[0], top, up[1])
            self.plan.waypoints += [pb, pt]
            self.plan.stairs.append((pb, pt))
            self.reserved[f].append(grow(s, bottom, (s[3] - s[1]) if dx != 0 else (s[2] - s[0])))
            self.platform_stairs.append((f, bottom))
        for px, pz in ((r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3])):
            if self.near_wall(f, True, pz, px - 0.2, px + 0.2) or self.near_wall(f, False, px, pz - 0.2, pz + 0.2):
                continue
            cx = min(max(px, r[0] + 0.1), r[2] - 0.1)
            cz = min(max(pz, r[1] + 0.1), r[3] - 0.1)
            self.add(cx - 0.1, lf, cz - 0.1, cx + 0.1, top - 0.2, cz + 0.1, self.d.trim, "post")
            self.reserved[f].append((cx - 0.3, cz - 0.3, cx + 0.3, cz + 0.3))
        if x["railing"] <= 0.01:
            return
        for eid, ax, c0, a, b, o in ((1, True, r[1], r[0], r[2], -1), (2, True, r[3], r[0], r[2], 1), (3, False, r[0], r[1], r[3], -1), (4, False, r[2], r[1], r[3], 1)):
            if eid == landing or self.near_wall(f, ax, c0, a, b):
                continue
            cc = c0 if o < 0 else c0 - 0.05
            self.add_along(ax, a, b, cc, cc + 0.05, top, top + x["railing"], self.d.trim, "railing")

    def emit_prop(self, p, x, y, z, yaw, scale, room, fp=None):
        # Mirrored buildings mirror the prop's footprint (its bounds centre), not just its origin (BuildingFurnish.EmitProp).
        cx, cz = (p.min[0] + p.max[0]) / 2 * scale, (p.min[2] + p.max[2]) / 2 * scale
        lx, lz = turn(cx, cz, math.radians(yaw))
        centre = self.P(x + lx, y, z + lz)
        wy = self.yaw_w(yaw)
        wx, wz = turn(cx, cz, math.radians(wy))
        wp = (centre[0] - wx, y, centre[2] - wz)
        self.plan.props.append({"def": p, "pos": wp, "yaw": wy, "scale": scale, "room": room, "local": (x, y, z, yaw), "fp": fp})

    # ---- furnishing
    def faces(self, f, r):
        out = []
        for s in self.segs[f]:
            if s.neg == r:
                out.append((s.along_x, s.c - self.t / 2, -1, s.a, s.b, s))
            if s.pos == r:
                out.append((s.along_x, s.c + self.t / 2, 1, s.a, s.b, s))
        return out

    def door_zones(self, f, r):
        out = []
        for a in self.openings:
            if a["f"] != f or a["o"]["kind"] not in ("door", "arch") or (a["s"].neg != r and a["s"].pos != r):
                continue
            dr = 1 if a["s"].pos == r else -1
            face = a["s"].c + dr * self.t / 2
            far = face + dr * DOOR_CLEAR_DEPTH
            half = a["o"]["width"] / 2 + 0.2
            c0, c1 = min(face, far), max(face, far)
            out.append((a["along"] - half, c0, a["along"] + half, c1) if a["s"].along_x else (c0, a["along"] - half, c1, a["along"] + half))
        for a, b, mid, nn, ln in self.open_joints(f):
            if b != r:
                continue
            far = (mid[0] + nn[0] * DOOR_CLEAR_DEPTH, mid[1] + nn[1] * DOOR_CLEAR_DEPTH)
            side = (nn[1] * 0.7, nn[0] * 0.7)
            p0 = (mid[0] - side[0], mid[1] - side[1])
            p1 = (far[0] + side[0], far[1] + side[1])
            out.append((min(p0[0], p1[0]), min(p0[1], p1[1]), max(p0[0], p1[0]), max(p0[1], p1[1])))
        return out

    def pois(self, f, r):
        out = []
        for a in self.openings:
            if a["f"] != f or a["o"]["kind"] not in ("door", "arch") or (a["s"].neg != r and a["s"].pos != r):
                continue
            dr = 1 if a["s"].pos == r else -1
            c = a["s"].c + dr * 0.9
            out.append((a["along"], c) if a["s"].along_x else (c, a["along"]))
        for a, b, mid, nn, ln in self.open_joints(f):
            if b == r:
                out.append((mid[0] + nn[0] * 0.9, mid[1] + nn[1] * 0.9))
        for s, sf, rr, bottom, top in self.stair_plans:
            if sf == f and self.room_at(f, *bottom) == r:
                out.append(bottom)
            if sf + 1 == f and self.room_at(f, *top) == r:
                out.append(top)
        for pf, bottom in self.platform_stairs:
            if pf == f and self.room_at(f, *bottom) == r:
                out.append(bottom)
        return out

    def room_record(self, f, r, pois, grid):
        room = self.floors[f]["rooms"][r]
        fy = self.level[f]
        ceil = self.level[f + 1] - SLAB
        pr = {"id": room["id"], "type": room["type"], "floor": f, "floorY": fy, "ceilY": ceil,
              "inside": [r_inset(rc, self.t / 2) for rc in room["rects"]], "zones": self.door_zones(f, r), "placed": 0, "skipped": 0}
        if pois:
            wp = pois[0]
        else:
            big = max(room["rects"], key=lambda x: (x[2] - x[0]) * (x[3] - x[1]))
            wp = ((big[0] + big[2]) / 2, (big[1] + big[3]) / 2)
            if grid is not None:
                best = 1e18
                c = wp
                for k in range(grid.nz):
                    for i in range(grid.nx):
                        v = grid.solid[k][i]
                        if v in (1, 3):
                            continue
                        p = (grid.cx(i), grid.cz(k))
                        if not grid.walkable((p[0] - 0.45, p[1] - 0.45, p[0] + 0.45, p[1] + 0.45)):
                            continue
                        dd = (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2
                        if dd < best:
                            best, wp = dd, p
            self.plan.waypoints.append(self.P(wp[0], fy, wp[1]))
        pr["waypoint"] = self.P(wp[0], fy, wp[1])
        pr["local_wp"] = wp
        self.plan.rooms.append(pr)
        return pr

    def furnish(self, f, r):
        room = self.floors[f]["rooms"][r]
        pois = self.pois(f, r)
        bounds = room["rects"][0]
        for rc in room["rects"][1:]:
            bounds = r_union(bounds, rc)
        grid = Grid(bounds)
        for rc in room["rects"]:
            grid.set(rc, 0, False, grid.solid)
        t = self.t
        for s in self.segs[f]:
            band = (s.a - t / 2, s.c - t / 2 - 0.03, s.b + t / 2, s.c + t / 2 + 0.03) if s.along_x else (s.c - t / 2 - 0.03, s.a - t / 2, s.c + t / 2 + 0.03, s.b + t / 2)
            grid.set(band, 1, False, grid.solid)
        grid.flat = [[1 if v == 1 else 0 for v in row] for row in grid.solid]
        for z in self.door_zones(f, r):
            grid.set(z, 2, True, grid.solid, True)
        for z in self.reserved[f]:
            grid.set(z, 2, True, grid.solid, True)
        for i in range(1, len(pois)):
            walkway(grid, pois[0], pois[i])
        placed, skipped = self.furnish_items(f, r, grid, pois, bounds)
        pr = self.room_record(f, r, pois, grid)
        pr["placed"], pr["skipped"] = placed, skipped
        pr["grid"] = grid
        if skipped:
            self.plan.notes.append(f"{self.d.name}: room \"{room['id']}\" (floor {f}): {skipped} furnishing item(s) found no space")

    def furnish_items(self, f, r, grid, pois, bounds):
        self.furnish_floor = f
        room = self.floors[f]["rooms"][r]
        placed_n = skipped = 0
        faces = self.faces(f, r)
        windows = []
        for a in self.openings:
            if a["f"] != f or a["o"]["kind"] not in ("window", "hole"):
                continue
            for i, fc in enumerate(faces):
                if fc[5] is a["s"]:
                    windows.append((i, a["along"] - a["o"]["width"] / 2, a["along"] + a["o"]["width"] / 2, a["bottom"]))
        fy = self.level[f]
        ceil = self.level[f + 1] - SLAB
        rng = Rng(fhash(((self.seed * 2246822519) & M32) ^ fnv(room["id"]) ^ ((f * 7919) & M32) ^ ((room["seed"] * 3266489917) & M32)))
        placed = {}
        for p in room["props"]:
            d = self.lib.find_prop(self.d, p["name"])
            if d is None:
                raise BuildingError(f"{self.where(f)}, room \"{room['id']}\": unknown prop \"{p['name']}\"")
            if p["wall"]:
                ax, at, dr = {"N": (True, bounds[1] + t2(self), 1), "S": (True, bounds[3] - t2(self), -1), "W": (False, bounds[0] + t2(self), 1), "E": (False, bounds[2] - t2(self), -1)}[p["wall"]]
                yaw = face_yaw(ax, dr) + p["yaw"]
                back = -d.min[2] * p["scale"] + 0.04
                along = ((bounds[0] + bounds[2]) / 2 if ax else (bounds[1] + bounds[3]) / 2) + p["along"]
                across = at + dr * back
                pos = (along, across) if ax else (across, along)
            else:
                pos = ((bounds[0] + bounds[2]) / 2 + p["at"][0], (bounds[1] + bounds[3]) / 2 + p["at"][1])
                yaw = p["yaw"]
            fp = world_bounds(d, pos[0], pos[1], yaw, p["scale"])
            grid.set(fp, 3, False, grid.flat if is_flat(d, p["scale"]) else grid.solid, True)
            self.emit_prop(d, pos[0], fy + p["y"], pos[1], yaw, p["scale"], room["id"], fp)
            placed_n += 1
        if room["furnish"] != "auto":
            return placed_n, skipped
        rtype = (room["rules"] or room["type"]).lower()
        rule = self.d.local_rules.get(rtype) or self.lib.rules.get(rtype)
        if rule is None:
            return placed_n, skipped
        area = sum((x[2] - x[0]) * (x[3] - x[1]) for x in room["rects"])
        for item in rule["items"]:
            if area < item["area"][0] or area > item["area"][1]:
                continue
            if item["chance"] < 1 and rng.next() >= item["chance"]:
                continue
            count = item["min"] + int(math.floor(rng.next() * (item["max"] - item["min"] + 1)))
            count = min(count, item["max"])
            lst = None
            if item["id"]:
                lst = placed[item["id"]] = []
            parents = placed.get(item["of"]) if item["of"] else None
            for k in range(count):
                name = item["props"][min(len(item["props"]) - 1, int(rng.next() * len(item["props"])))]
                d = self.lib.find_prop(self.d, name)
                if d is None:
                    raise BuildingError(f"{rule['source']}: unknown prop \"{name}\" (furnishing building \"{self.d.name}\")")
                piece = self.place(item, d, k, grid, faces, windows, pois, parents, bounds, ceil - fy, rng)
                if piece is None:
                    skipped += 1
                    continue
                if item["place"] not in ("on", "ceiling"):
                    grid.set(piece["fp"], 3, False, grid.flat if is_flat(d, piece["scale"]) else grid.solid, True)
                if lst is not None:
                    lst.append(piece)
                self.emit_prop(d, piece["pos"][0], fy + piece["y"], piece["pos"][1], piece["yaw"], piece["scale"], room["id"], piece["fp"])
                placed_n += 1
        return placed_n, skipped

    def place(self, item, d, k, grid, faces, windows, pois, parents, bounds, clear, rng):
        s = item["scale"]
        flat = is_flat(d, s)
        gap = item["gap"] if d.solid else 0.02
        top = d.max[1] * s

        low = self.low_ceil.get(self.furnish_floor)

        def fits(fp):
            if not grid.fits_apart(fp, gap, flat):
                return False
            if low:
                for r, clear_under in low:
                    if top > clear_under and r_overlaps(fp, r, 0.001):
                        return False
            return True

        def piece(pos, yaw, y=0.0, fp=None):
            return {"def": d, "pos": pos, "yaw": yaw, "scale": s, "y": y, "fp": fp or world_bounds(d, pos[0], pos[1], yaw, s)}
        pl = item["place"]
        if pl in ("wall", "corner"):
            if top > clear - 0.05:
                return None
            cands = []
            for fi, fc in enumerate(faces):
                u = fc[3]
                while u <= fc[4] + 0.001:
                    score = rng.next() * 0.75
                    if item["prefer"] != "any" and pois:
                        p = (u, fc[1]) if fc[0] else (fc[1], u)
                        dd = min(math.hypot(p[0] - q[0], p[1] - q[1]) for q in pois)
                        score += dd if item["prefer"] == "far" else -dd
                    cands.append((score, fi, u))
                    u += 0.1
            cands.sort(key=lambda c: -c[0])
            for _, fi, u in cands:
                fc = faces[fi]
                yaw = face_yaw(fc[0], fc[2]) + item["turn"]
                back = -d.min[2] * s + 0.04
                across = fc[1] + fc[2] * back
                pos = (u, across) if fc[0] else (across, u)
                fp = world_bounds(d, pos[0], pos[1], yaw, s)
                if not fits(fp):
                    continue
                fa, fb = (fp[0], fp[2]) if fc[0] else (fp[1], fp[3])
                if any(w[0] == fi and top > w[3] + 0.2 and fa < w[2] + 0.05 and fb > w[1] - 0.05 for w in windows):
                    continue
                if pl == "corner":
                    depth = fc[1] + fc[2] * 0.3
                    ea = grid.at(fa - 0.25, depth) if fc[0] else grid.at(depth, fa - 0.25)
                    eb = grid.at(fb + 0.25, depth) if fc[0] else grid.at(depth, fb + 0.25)
                    if ea != 1 and eb != 1:
                        continue
                return piece(pos, yaw, 0.0, fp)
            return None
        if pl == "centre":
            c = ((bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2)
            room_long_x = bounds[2] - bounds[0] >= bounds[3] - bounds[1]
            prop_long_x = d.max[0] - d.min[0] >= d.max[2] - d.min[2]
            yaw0 = (0 if room_long_x == prop_long_x else 90) + item["turn"]
            for ring in range(6):
                for i in range(1 if ring == 0 else 8):
                    ang = i * math.pi / 4
                    pos = (c[0] + math.cos(ang) * ring * 0.2, c[1] + math.sin(ang) * ring * 0.2)
                    for yaw in (yaw0, yaw0 + 90):
                        fp = world_bounds(d, pos[0], pos[1], yaw, s)
                        if fits(fp):
                            return piece(pos, yaw, 0.0, fp)
            return None
        if pl == "floor":
            for _ in range(80):
                pos = (bounds[0] + rng.next() * (bounds[2] - bounds[0]), bounds[1] + rng.next() * (bounds[3] - bounds[1]))
                yaw = math.floor(rng.next() * 4) * 90 if d.solid else rng.next() * 360
                fp = world_bounds(d, pos[0], pos[1], yaw + item["turn"], s)
                if fits(fp):
                    return piece(pos, yaw + item["turn"], 0.0, fp)
            return None
        if pl == "on":
            if not parents:
                return None
            par = parents[k % len(parents)]
            y = par["y"] + (item["y"] if item["y"] is not None else par["def"].max[1] * par["scale"])
            if y + top > clear - 0.02:
                return None
            yaw = par["yaw"] + item["turn"]
            pc = ((par["fp"][0] + par["fp"][2]) / 2, (par["fp"][1] + par["fp"][3]) / 2)
            ch = world_bounds(d, pc[0], pc[1], yaw, s)
            mx = max(0, ((par["fp"][2] - par["fp"][0]) - (ch[2] - ch[0])) / 2 * 0.8)
            mz = max(0, ((par["fp"][3] - par["fp"][1]) - (ch[3] - ch[1])) / 2 * 0.8)
            pos = (pc[0] + (rng.next() * 2 - 1) * mx, pc[1] + (rng.next() * 2 - 1) * mz)
            return piece(pos, yaw, y)
        if pl == "beside":
            if not parents:
                return None
            par = parents[(k // 2) % len(parents)]
            axis = side_axis(par["yaw"])
            fwd = facing(par["yaw"])
            pd, ps = par["def"], par["scale"]
            back_shift = pd.min[2] * ps - d.min[2] * s
            for tt in range(2):
                side = 1 if (k + tt) % 2 == 0 else -1
                along = pd.max[0] * ps + 0.05 - d.min[0] * s if side > 0 else pd.min[0] * ps - 0.05 - d.max[0] * s
                pos = (par["pos"][0] + axis[0] * along + fwd[0] * back_shift, par["pos"][1] + axis[1] * along + fwd[1] * back_shift)
                yaw = par["yaw"] + item["turn"]
                fp = world_bounds(d, pos[0], pos[1], yaw, s)
                if grid.fits_apart(fp, 0.02, flat):
                    return piece(pos, yaw, 0.0, fp)
            return None
        if pl == "front":
            if not parents:
                return None
            par = parents[k % len(parents)]
            fwd = facing(par["yaw"])
            dist = par["def"].max[2] * par["scale"] + item["distance"] + d.max[2] * s
            pos = (par["pos"][0] + fwd[0] * dist, par["pos"][1] + fwd[1] * dist)
            yaw = par["yaw"] + 180 + item["turn"]
            fp = world_bounds(d, pos[0], pos[1], yaw, s)
            return piece(pos, yaw, 0.0, fp) if grid.fits_apart(fp, 0.02, flat) else None
        if pl == "around":
            if not parents:
                return None
            slots = []
            for par in parents:
                pd, ps = par["def"], par["scale"]
                axis = side_axis(par["yaw"])
                fwd = facing(par["yaw"])
                w = (pd.max[0] - pd.min[0]) * ps
                dd = (pd.max[2] - pd.min[2]) * ps
                sides = [(fwd, axis, pd.max[2] * ps, w), ((-fwd[0], -fwd[1]), axis, -pd.min[2] * ps, w), (axis, fwd, pd.max[0] * ps, dd), ((-axis[0], -axis[1]), fwd, -pd.min[0] * ps, dd)]
                sides.sort(key=lambda x: -x[3])
                for nrm, tan, out_d, ln in sides:
                    for o in ((-ln / 4, ln / 4) if ln >= 1.5 else (0.0,)):
                        dist = out_d + item["distance"] + d.max[2] * s
                        pos = (par["pos"][0] + nrm[0] * dist + tan[0] * o, par["pos"][1] + nrm[1] * dist + tan[1] * o)
                        yaw = math.degrees(math.atan2(-nrm[0], -nrm[1])) + item["turn"]
                        slots.append((pos, yaw))
            for pos, yaw in slots:
                fp = world_bounds(d, pos[0], pos[1], yaw, s)
                if grid.fits_apart(fp, 0.02, flat):
                    return piece(pos, yaw, 0.0, fp)
            return None
        if pl == "ceiling":
            gap_under = item["y"] if item["y"] is not None else 0.0
            y = clear - gap_under - top
            if y < 2.0:
                return None
            long_x = bounds[2] - bounds[0] >= bounds[3] - bounds[1]
            span = (bounds[2] - bounds[0] if long_x else bounds[3] - bounds[1]) - 1
            n = max(1, item["max"])
            tt = 0.5 if n == 1 else k / (n - 1)
            c = ((bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2)
            frac = 0.5 if n == 1 else 0.15 + 0.7 * tt
            pos = (bounds[0] + 0.5 + span * frac, c[1]) if long_x else (c[0], bounds[1] + 0.5 + span * frac)
            if grid.at(*pos) == 1:
                return None
            return piece(pos, item["turn"], y)
        return None


def turn(x, z, a):
    cs, sn = math.cos(a), math.sin(a)
    return (x * cs + z * sn, -x * sn + z * cs)


def t2(g):
    return g.t / 2


def face_yaw(ax, dr):
    return (0 if dr > 0 else 180) if ax else (90 if dr > 0 else 270)


def is_flat(d, scale):
    return d.max[1] * scale < 0.06 and not d.solid


def facing(yaw):
    a = math.radians(yaw)
    return (math.sin(a), math.cos(a))


def side_axis(yaw):
    a = math.radians(yaw)
    return (math.cos(a), -math.sin(a))


def grow(r, p, width):
    hw = max(0.5, width / 2) + 0.1
    return r_union(r, (p[0] - hw, p[1] - 0.6, p[0] + hw, p[1] + 0.6))


def walkway(g, a, b):
    ln = math.hypot(b[0] - a[0], b[1] - a[1])
    steps = max(1, math.ceil(ln / 0.1))
    for s in range(steps + 1):
        p = (a[0] + (b[0] - a[0]) * s / steps, a[1] + (b[1] - a[1]) * s / steps)
        g.set((p[0] - 0.5, p[1] - 0.5, p[0] + 0.5, p[1] + 0.5), 2, True, g.solid, True)


class Grid:
    def __init__(self, b):
        self.x0, self.z0 = b[0], b[1]
        self.nx = max(1, math.ceil((b[2] - b[0]) / CELL - 1e-9))
        self.nz = max(1, math.ceil((b[3] - b[1]) / CELL - 1e-9))
        self.solid = [[1] * self.nx for _ in range(self.nz)]
        self.flat = [[1] * self.nx for _ in range(self.nz)]

    def cx(self, i):
        return self.x0 + (i + 0.5) * CELL

    def cz(self, k):
        return self.z0 + (k + 0.5) * CELL

    def range(self, r):
        i0 = max(0, math.ceil((r[0] - self.x0) / CELL - 0.5))
        i1 = min(self.nx - 1, math.floor((r[2] - self.x0) / CELL - 0.5))
        k0 = max(0, math.ceil((r[1] - self.z0) / CELL - 0.5))
        k1 = min(self.nz - 1, math.floor((r[3] - self.z0) / CELL - 0.5))
        return i0, i1, k0, k1

    def touching(self, r):
        i0 = max(0, math.floor((r[0] - self.x0) / CELL + 0.001))
        i1 = min(self.nx - 1, math.ceil((r[2] - self.x0) / CELL - 0.001) - 1)
        k0 = max(0, math.floor((r[1] - self.z0) / CELL + 0.001))
        k1 = min(self.nz - 1, math.ceil((r[3] - self.z0) / CELL - 0.001) - 1)
        return i0, i1, k0, k1

    def set(self, r, v, only_free, layer, touching=False):
        i0, i1, k0, k1 = self.touching(r) if touching else self.range(r)
        for k in range(k0, k1 + 1):
            row = layer[k]
            for i in range(i0, i1 + 1):
                if not only_free or row[i] == 0:
                    row[i] = v

    def fits(self, r, flat):
        if r[0] < self.x0 - 0.001 or r[1] < self.z0 - 0.001 or r[2] > self.x0 + self.nx * CELL + 0.001 or r[3] > self.z0 + self.nz * CELL + 0.001:
            return False
        i0, i1, k0, k1 = self.range(r)
        if i1 < i0 or k1 < k0:
            return False
        for k in range(k0, k1 + 1):
            for i in range(i0, i1 + 1):
                if flat:
                    if self.solid[k][i] == 1 or self.flat[k][i] != 0:
                        return False
                elif self.solid[k][i] != 0:
                    return False
        return True

    def fits_apart(self, r, gap, flat):
        if not self.fits(r, flat):
            return False
        if gap <= 0:
            return True
        layer = self.flat if flat else self.solid
        i0, i1, k0, k1 = self.range(r_inset(r, -gap))
        return all(layer[k][i] != 3 for k in range(k0, k1 + 1) for i in range(i0, i1 + 1))

    def walkable(self, r):
        i0, i1, k0, k1 = self.range(r)
        if i1 < i0 or k1 < k0:
            return False
        return all(self.solid[k][i] not in (1, 3) for k in range(k0, k1 + 1) for i in range(i0, i1 + 1))

    def at(self, x, z):
        i = math.floor((x - self.x0) / CELL)
        k = math.floor((z - self.z0) / CELL)
        if i < 0 or k < 0 or i >= self.nx or k >= self.nz:
            return 1
        return self.solid[k][i]


def generate(lib, d, variant=None, seed=0, origin=(0, 0, 0), quarter=0):
    return Generator(lib, d, variant, seed, origin, quarter).run()
