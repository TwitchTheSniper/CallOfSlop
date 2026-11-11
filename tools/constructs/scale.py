#!/usr/bin/env python3
"""The Sandbox's part size classes, and what every generator of big parts shares.

Import it from any generator in tools/constructs/ (the part kit ports it: Source/PartKit/PartScale.cs):

    from scale import SMALL, MEDIUM, LARGE, CAPITAL, CLASSES, Part, box, block_sockets, write_parts, fmt

The size classes (docs/sandbox/CONSTRUCTS.md, "Size classes"): the same families at four linear scales, each on a
lattice that multiplies the small class's 0.25 m cell by 4.

    class    scale  cell   plate (kg/m2)  a frame member  what it builds
    small    x1     0.25   40             0.25 to 2 m     a buggy, a plane (today's parts; they stay as they are)
    medium   x4     1      80             1 to 8 m        a truck, a tank, a bomber
    large    x16    4      200            4 to 32 m       a hangar, a big walker, a frigate
    capital  x64    16     500            16 to 128 m     a carrier's hull (300 m, 100,000 t)

The rules every part of a class keeps (the "Capital parts" self-test checks them in every turn):
- A part is made of cells of its class. Its origin is the centre of one cell. Every socket sits on a cell face, in the
  middle of the cell's face (Part.sock adds nothing: block_sockets works them out), so two sockets that meet carry on
  the class's lattice through every part.
- A socket has a class (the part's, or the "class" key on the socket): a socket snaps only to one of its own class, so a
  capital hull offers 16 m steps and nothing small sticks to it directly. An adapter joins two classes: its back socket
  is of one class, its front sockets of another. The socket weld and the freeform weld don't look at classes.
- Set a part's class with Part(..., cls=CAPITAL): it writes "sizeClass" in the file, and every socket takes that class
  unless Part.sock is given cls=.
- A part has at most 256 boxes and 255 sockets, and reaches at most 200 m from its origin (ConstructParts.MaxBoxes,
  MaxSockets, MaxPartReach). A static part's boxes are drawn as merged meshes (docs/render/MERGED-MESHES.md), but they
  still cost collision work and memory: spend them where they show, and keep a hollow part to its walls.
- The inventory: Part's group defaults to the class's group ("Medium", "Large", "Capital"), the section is the family.
  Small parts stay in the old groups.

Data first: the JSON files are what the game loads. Fix a family here or in its generator, never in its output.
"""

import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zfight  # noqa: E402 (no coplanar faces: tools/constructs/zfight.py)


class Cls:
    """One size class. x: the linear scale against small; cell: its lattice step (m); kg_m2: a plate of the class."""

    def __init__(self, key, name, x, cell, kg_m2, plate, seconds, health_m2, armour):
        self.key = key  # the "sizeClass" / socket "class" value
        self.name = name  # the inventory group
        self.x = x
        self.cell = cell
        self.half = cell / 2
        self.kg_m2 = kg_m2  # kg per m2 of its plate
        self.plate = plate  # the plate's thickness (m)
        self.seconds = seconds  # time to fit a typical part (attachSeconds: at most 60)
        self.health_m2 = health_m2  # health per m2 of plate
        self.armour = armour  # the plate's armour (a flat amount off every hit)

    def n(self, metres):
        """Cells in a length (it must be a whole number of them)."""
        k = metres / self.cell
        assert abs(k - round(k)) < 1e-6, (self.key, metres)
        return int(round(k))

    def lo(self):
        """Cell 0's low face."""
        return -self.half

    def hi(self, n):
        """The high face of n cells."""
        return n * self.cell - self.half


SMALL = Cls("small", "", 1, 0.25, 40, 0.05, 2.5, 120, 20)
MEDIUM = Cls("medium", "Medium", 4, 1.0, 80, 0.1, 4.0, 180, 24)
LARGE = Cls("large", "Large", 16, 4.0, 200, 0.2, 8.0, 240, 32)
CAPITAL = Cls("capital", "Capital", 64, 16.0, 500, 0.5, 15.0, 300, 40)
CLASSES = (SMALL, MEDIUM, LARGE, CAPITAL)

DIRS = {"+x": (0, 1), "-x": (0, -1), "+y": (1, 1), "-y": (1, -1), "+z": (2, 1), "-z": (2, -1)}


def scaled(v, c):
    """A small-class length at class c."""
    return v * c.x


def r(v):
    """Metres to 4 decimals (5 mm steps stay exact)."""
    v = round(v + 0.0, 4)
    return 0.0 if v == 0 else v


def vec(*v):
    return [r(x) for x in v]


def fmt(v):
    """A length for a part's name: 16, 0.5."""
    return f"{v:g}"


def box(mn, mx, color=None, detail=False, hidden=False, glass=False, **shape):
    """A box from its corners (None when it would be thinner than 1 cm: Part.add skips it)."""
    if min(mx[a] - mn[a] for a in range(3)) < 0.0099:
        return None
    b = {"min": vec(*mn), "max": vec(*mx)}
    if color:
        b["color"] = color
    if detail:
        b["detail"] = True
    if hidden:
        b["hidden"] = True
    if glass:
        b["glass"] = True
    b.update(shape)
    return b


def pick(n, m):
    """Up to m of the indices 0..n-1, evenly spread, both ends kept."""
    if n <= m:
        return list(range(n))
    if m == 1:
        return [n // 2]
    return sorted({round(i * (n - 1) / (m - 1)) for i in range(m)})


class Part:
    """One generated part file. p = Part(...); p.add(box(...)); p.sock(...); p.stats(...); write_parts collects them."""

    def __init__(self, name, cls, section, desc, role="frame", tool="welder", material="steel", color="#7c838c", group=None):
        self.cls = cls
        self.d = {
            "format": "call-of-slop-construct-part/1",
            "name": name,
            "description": desc,
            "group": cls.name if group is None else group,
            "section": section,
            "role": role,
            "tool": tool,
        }
        if cls.key != "small":
            self.d["sizeClass"] = cls.key
        self.material = material
        self.color = color
        self.boxes = []
        self.sockets = []
        self.names = set()

    def add(self, b):
        if b is not None:
            self.boxes.append(b)
        return self

    def sock(self, name, at, d, cls=None, kind=None):
        """A socket; cls: another class than the part's (an adapter's other side)."""
        assert name not in self.names, name
        self.names.add(name)
        s = {"name": name, "at": vec(*at), "dir": d}
        if cls is not None and cls.key != self.cls.key:
            s["class"] = cls.key
        if kind:
            s["kind"] = kind
        self.sockets.append(s)

    def stats(self, mass, health, armour, seconds=None):
        self.d["mass"] = r(max(0.5, round(mass, 1)))
        self.d["health"] = int(round(health))
        self.d["armour"] = armour
        self.d["material"] = self.material
        self.d["color"] = self.color
        self.d["attachSeconds"] = self.cls.seconds if seconds is None else seconds
        return self

    def set(self, **keys):
        """Any other key of the part file (a role's block: engine, joint, storage, walk...)."""
        self.d.update(keys)
        return self

    def json(self):
        assert 0 < len(self.boxes) <= 256, (self.d["name"], len(self.boxes))
        assert 0 < len(self.sockets) <= 64, (self.d["name"], len(self.sockets))
        assert len(self.d["name"]) <= 32, self.d["name"]
        zfight.resolve(self.boxes, self.sockets)  # no two drawn faces on one plane (CLAUDE.md, "No coplanar faces")
        d = dict(self.d)
        d["boxes"] = self.boxes
        d["sockets"] = self.sockets
        return d


def block_sockets(p, nx, ny, nz, faces="xyz", limit=None, skip=(), step=1):
    """Sockets on the faces of an nx x ny x nz block of the part's cells (origin: the centre of cell 0,0,0): one per cell
    face, every step cells, at most limit[dir] = (u, v) of them a face (evenly spread, the ends kept)."""
    c = p.cls
    lim = limit or {}
    dims = (nx, ny, nz)
    for axis, a in enumerate("xyz"):
        if a not in faces:
            continue
        u, v = [k for k in range(3) if k != axis]
        for sign, sname in ((-1, "-"), (1, "+")):
            d = sname + a
            if d in skip:
                continue
            for i in pick(dims[u], lim.get(d, (4, 4))[0]):
                for j in pick(dims[v], lim.get(d, (4, 4))[1]):
                    if (i % step or j % step) and step > 1:
                        continue
                    at = [0.0, 0.0, 0.0]
                    at[axis] = c.hi(dims[axis]) if sign > 0 else c.lo()
                    at[u] = i * c.cell
                    at[v] = j * c.cell
                    nm = d if dims[u] * dims[v] == 1 else f"{d}.{i}.{j}"
                    p.sock(nm, at, d)


def file_name(name):
    s = name.replace(".", "p").replace(":", "to").replace("-", "")
    return "".join(ch for ch in s if ch.isalnum()) + ".json"


def write_parts(out, parts, argv=None):
    """Writes [(folder, Part)...] under out (the whole folder: anything else in it is removed). --check only compares.
    Returns the exit code."""
    check = "--check" in (sys.argv if argv is None else argv)
    names = set()
    want = {}
    for folder, p in parts:
        d = p.json()
        assert d["name"] not in names, d["name"]
        names.add(d["name"])
        path = os.path.join(out, folder, file_name(d["name"]))
        assert path not in want, path
        want[path] = json.dumps(d, indent=2) + "\n"
    have = {}
    for root, _, files in os.walk(out):
        for f in files:
            if f.endswith(".json"):
                path = os.path.join(root, f)
                with open(path, encoding="utf-8") as fh:
                    have[path] = fh.read()
    stale = sorted(set(have) - set(want))
    changed = sorted(p for p in want if have.get(p) != want[p])
    if check:
        for p in stale:
            print(f"stale: {p}")
        for p in changed:
            print(f"out of date: {p}")
        print(f"{len(want)} parts; {len(changed)} out of date, {len(stale)} stale")
        return 1 if stale or changed else 0
    for p in stale:
        os.remove(p)
    for p in changed:
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(want[p])
    print(f"{len(want)} parts written ({len(changed)} changed, {len(stale)} removed) in {out}")
    return 0
