#!/usr/bin/env python3
"""Writes Constructs/Blueprints/RV.json, the motorhome blueprint (board item dev-blueprint-rv-rv01).

Run it from the repo root:  python3 -I tools/constructs/make_rv.py   (--check only compares with the committed file).

The blueprint is a build script: every step fits a shipped part onto a free socket of an earlier part, through the
game's own rules (ConstructBuild.Place). This script mirrors those rules (Rot24 turns, the fixed spin order, the 1 cm
grid, the overlap test and AutoLink), so the RV is designed in body coordinates (metres) and the script works out which
socket of which earlier part takes each piece. `put` finds the first socket combination that lands a part exactly
where it is wanted, `near` takes the free spot nearest a wanted place, and `hang` names the sockets (a hinge on a
post, a door on a hinge). Nothing here invents a part: names that are not in Constructs/Parts stop the script.
"""

import glob
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import building_parts as bpm  # noqa: E402 (Rot24 mirror: COLS, apply, apply_dir, turn, upright)

DIRS = bpm.DIRS
OUT = os.path.join("Constructs", "Blueprints", "RV.json")
EPS = 0.01  # the game's overlap slack (ConstructBuild.Blocked)


def load_defs():
    defs = {}
    for f in sorted(glob.glob(os.path.join("Constructs", "Parts", "**", "*.json"), recursive=True)):
        with open(f, encoding="utf-8") as fh:
            d = json.load(fh)
        if "name" not in d:
            continue
        solids = []
        for b in d.get("boxes", []):
            if b.get("detail"):
                continue
            if "min" in b:
                mn, mx = b["min"], b["max"]
            else:
                at = b.get("at", [0, 0, 0])
                mn = [at[k] - b["size"][k] / 2 for k in range(3)]
                mx = [at[k] + b["size"][k] / 2 for k in range(3)]
            solids.append(([(mn[k] + mx[k]) / 2 for k in range(3)], [(mx[k] - mn[k]) / 2 for k in range(3)]))
        alls = []
        for b in d.get("boxes", []):
            if b.get("hidden") or b.get("shape"):
                continue
            if "min" in b:
                mn, mx = b["min"], b["max"]
            else:
                at = b.get("at", [0, 0, 0])
                mn = [at[k] - b["size"][k] / 2 for k in range(3)]
                mx = [at[k] + b["size"][k] / 2 for k in range(3)]
            alls.append(([(mn[k] + mx[k]) / 2 for k in range(3)], [(mx[k] - mn[k]) / 2 for k in range(3)]))
        d["_all"] = alls
        d["_solids"] = solids
        d["_file"] = f
        defs[d["name"]] = d
    return defs


def kind(d, i):
    return d["sockets"][i].get("kind", "struct")


class Placed:
    def __init__(self, name, d, pos, rot, step):
        self.name, self.d, self.pos, self.rot, self.step = name, d, pos, rot, step
        self.used = set()

    def sock(self, i):
        s = self.d["sockets"][i]
        a = bpm.apply(self.rot, s["at"])
        return [self.pos[k] + a[k] for k in range(3)], bpm.apply_dir(self.rot, DIRS.index(s["dir"]))

    def boxes(self):
        for c, h in self.d["_solids"]:
            ac = bpm.apply(self.rot, c)
            ah = [abs(x) for x in bpm.apply(self.rot, h)]
            yield [self.pos[k] + ac[k] for k in range(3)], ah

    def bounds(self):
        lo, hi = [9e9] * 3, [-9e9] * 3
        for c, h in self.boxes():
            for k in range(3):
                lo[k] = min(lo[k], c[k] - h[k])
                hi[k] = max(hi[k], c[k] + h[k])
        return lo, hi


class Blueprint:
    def __init__(self, defs):
        self.defs = defs
        self.parts = []
        self.steps = []
        self.extra = {}

    # -- geometry --------------------------------------------------------------------------------------------------

    def fits_turns(self, d, ds, want):
        key = (d["name"], ds, want)
        cache = self.__dict__.setdefault("_turns", {})
        if key in cache:
            return cache[key]
        cache[key] = self._fits_turns(d, ds, want)
        return cache[key]

    def _fits_turns(self, d, ds, want):
        turns = [rr for rr in range(24) if bpm.apply_dir(rr, DIRS.index(d["sockets"][ds]["dir"])) == want]
        for a in range(len(turns)):
            for b in range(a + 1, len(turns)):
                if bpm.upright(turns[b]) > bpm.upright(turns[a]):
                    turns[a], turns[b] = turns[b], turns[a]
        return turns

    def pose(self, j, t, d, ds, spin):
        tgt = self.parts[j]
        at, dr = tgt.sock(t)
        turns = self.fits_turns(d, ds, dr ^ 1)
        if not turns:
            return None
        rot = turns[spin % len(turns)]
        sa = bpm.apply(rot, d["sockets"][ds]["at"])
        pos = bpm.quant([at[k] - sa[k] for k in range(3)])
        return pos, rot

    def blocked(self, d, pos, rot, ignore=()):
        probe = Placed("?", d, pos, rot, None)
        for c, h in probe.boxes():
            for i, o in enumerate(self.parts):
                if i in ignore:
                    continue
                for oc, oh in o.boxes():
                    if all(abs(c[k] - oc[k]) < h[k] + oh[k] - EPS for k in range(3)):
                        return o.name + " #" + str(i)
        return None

    # -- steps -----------------------------------------------------------------------------------------------------

    def first(self, part, rot=0):
        d = self.defs[part]
        self.parts.append(Placed(part, d, [0.0, 0.0, 0.0], rot, 0))
        self.steps.append({"part": part} if rot == 0 else {"part": part, "spin": rot})
        return 0

    def _commit(self, part, j, t, ds, pos, rot, step):
        d = self.defs[part]
        why = self.blocked(d, pos, rot)
        if why:
            raise ValueError(f"{part} at {pos}: in the way of {why}")
        new = Placed(part, d, pos, rot, len(self.parts))
        self.parts[j].used.add(t)
        new.used.add(ds)
        self.parts.append(new)
        self.steps.append(step)
        me = len(self.parts) - 1
        # AutoLink (ConstructBuild.AutoLink): free struct sockets that meet a free struct socket of an earlier part.
        for s in range(len(d["sockets"])):
            if s in new.used or kind(d, s) != "struct":
                continue
            at, dr = new.sock(s)
            for jj, other in enumerate(self.parts[:-1]):
                done = False
                for tt in range(len(other.d["sockets"])):
                    if tt in other.used or kind(other.d, tt) != "struct":
                        continue
                    oat, odr = other.sock(tt)
                    if odr == dr ^ 1 and sum((oat[k] - at[k]) ** 2 for k in range(3)) < 0.0004:
                        other.used.add(tt)
                        new.used.add(s)
                        done = True
                        break
                if done:
                    break
        return me

    def sock_index(self, d, name):
        return [s["name"] for s in d["sockets"]].index(name)

    def hang(self, part, j, sock, by, spin=0, **extra):
        """A step by socket names."""
        tgt = self.parts[j]
        d = self.defs[part]
        t = self.sock_index(tgt.d, sock)
        ds = self.sock_index(d, by)
        if t in tgt.used:
            raise ValueError(f"{part}: {tgt.name} #{j} socket {sock} is already used")
        pos, rr = self.pose(j, t, d, ds, spin)
        step = {"part": part, "on": j, "socket": sock, "by": by}
        if spin:
            step["spin"] = spin
        step.update(extra)
        return self._commit(part, j, t, ds, pos, rr, step)

    def candidates(self, part, rot=None, parents=None, within=None):
        """Every way to fit `part` on a free struct socket: (parent, target socket, own socket, spin, pos, rot)."""
        d = self.defs[part]
        out = []
        for j, tgt in enumerate(self.parts):
            if parents is not None and j not in parents:
                continue
            if tgt.d.get("role") in ("door",) and False:
                continue
            for t in range(len(tgt.d["sockets"])):
                if t in tgt.used or kind(tgt.d, t) != "struct":
                    continue
                if within is not None:
                    sp = tgt.sock(t)[0]
                    if not all(within[0][k] <= sp[k] <= within[1][k] for k in range(3)):
                        continue
                for ds in range(len(d["sockets"])):
                    if kind(d, ds) != "struct":
                        continue
                    n = len(self.fits_turns(d, ds, tgt.sock(t)[1] ^ 1))
                    for spin in range(n):
                        r = self.pose(j, t, d, ds, spin)
                        if r is None:
                            continue
                        pos, rr = r
                        if rot is not None and rr != rot:
                            continue
                        out.append((j, t, ds, spin, pos, rr))
        return out

    def _step_of(self, part, c):
        j, t, ds, spin, pos, rr = c
        d = self.defs[part]
        step = {"part": part, "on": j, "socket": self.parts[j].d["sockets"][t]["name"], "by": d["sockets"][ds]["name"]}
        if spin:
            step["spin"] = spin
        return step

    def put(self, part, at, rot, parents=None, **extra):
        """Fits `part` with its origin at `at` (metres, body frame) turned `rot`."""
        at = bpm.quant(at)
        for c in self.candidates(part, rot, parents):
            j, t, ds, spin, pos, rr = c
            if all(abs(pos[k] - at[k]) < 0.004 for k in range(3)):
                step = self._step_of(part, c)
                step.update(extra)
                return self._commit(part, j, t, ds, pos, rr, step)
        raise ValueError(f"no socket takes {part} at {at} turned {rot}")

    def put_box(self, part, lo, rot, parents=None, **extra):
        """Fits `part` turned `rot` so its solid boxes' minimum corner is at `lo`."""
        d = self.defs[part]
        probe = Placed(part, d, [0, 0, 0], rot, None)
        plo, _ = probe.bounds()
        return self.put(part, [lo[k] - plo[k] for k in range(3)], rot, parents, **extra)

    def near(self, part, want, rot=None, parents=None, box=None, avoid=None, limit=0.6, rots=None, **extra):
        """Fits `part` where its bounding-box centre is nearest `want` (unblocked, inside `box` if given)."""
        d = self.defs[part]
        best = None
        within = None
        if box is not None:
            within = ([box[0][k] - 2.3 for k in range(3)], [box[1][k] + 2.3 for k in range(3)])
        for c in self.candidates(part, rot, parents, within):
            j, t, ds, spin, pos, rr = c
            if rots is not None and rr not in rots:
                continue
            probe = Placed(part, d, pos, rr, None)
            lo, hi = probe.bounds()
            if box is not None and not all(lo[k] >= box[0][k] - 0.011 and hi[k] <= box[1][k] + 0.011 for k in range(3)):
                continue
            if self.blocked(d, pos, rr):
                continue
            ctr = [(lo[k] + hi[k]) / 2 for k in range(3)]
            dist = math.dist(ctr, want)
            if best is None or dist < best[0] - 1e-9:
                best = (dist, c)
        if best is None or best[0] > limit:
            raise ValueError(f"nothing takes {part} near {want} (best {None if best is None else round(best[0], 2)})")
        c = best[1]
        step = self._step_of(part, c)
        step.update(extra)
        return self._commit(part, c[0], c[1], c[2], c[4], c[5], step)

    # -- output ----------------------------------------------------------------------------------------------------

    def report(self):
        lo, hi = [9e9] * 3, [-9e9] * 3
        mass = 0.0
        for i, p in enumerate(self.parts):
            plo, phi = p.bounds()
            for k in range(3):
                lo[k] = min(lo[k], plo[k])
                hi[k] = max(hi[k], phi[k])
            mass += p.d.get("mass", 0)
        return lo, hi, mass

    def listing(self):
        for i, p in enumerate(self.parts):
            lo, hi = p.bounds()
            print(f"{i:3d} {p.name:26s} rot {p.rot:2d} pos {p.pos}  [{lo[0]:.2f} {lo[1]:.2f} {lo[2]:.2f}] .. [{hi[0]:.2f} {hi[1]:.2f} {hi[2]:.2f}]")


def chassis(bp):
    """The 4x4 truck's chassis and drive line (Constructs/Blueprints/Truck.json), two beams longer. Returns a dict of step numbers."""
    h = bp.hang
    s = {}
    s["b0"] = bp.first("Frame Beam")
    s["b1"] = h("Frame Beam", 0, "x+0", "x-0")
    s["f0"] = h("Frame Beam", 0, "end-", "end+")
    s["f1"] = h("Frame Beam", 1, "end-", "end+")
    s["r0"] = h("Frame Beam", 0, "end+", "end-")
    s["r1"] = h("Frame Beam", 1, "end+", "end-")
    s["r2"] = h("Frame Beam", s["r0"], "end+", "end-")
    s["r3"] = h("Frame Beam", s["r1"], "end+", "end-")
    s["blk0"] = h("Frame Block", s["f0"], "end-", "+z")
    s["blk1"] = h("Frame Block", s["f1"], "end-", "+z")
    s["fdiff"] = h("Wide Differential", s["blk0"], "-z", "+z0")
    s["rdiff"] = h("Live Axle", s["r3"] if False else s["r2"], "end+", "-z0")
    s["engine"] = h("Diesel Engine", s["f1"], "y+1", "-y0")
    s["gear"] = h("Automatic Gearbox", s["engine"], "+z", "-z")
    s["tcase"] = h("Transfer Case", s["gear"], "+z", "in", setting=1)
    # front drive: right-angle box, shafts, right-angle box into the wide differential
    s["fr1"] = h("Right-Angle Gearbox", s["tcase"], "front", "-z", spin=2)
    s["fs1"] = h("Drive Shaft 2 m", s["fr1"], "-y", "end+")
    s["fs2"] = h("Drive Shaft 0.5 m", s["fs1"], "end-", "end+")
    s["fr2"] = h("Right-Angle Gearbox", s["fs2"], "end-", "-z")
    # rear drive
    s["rs1"] = h("Drive Shaft 2 m", s["tcase"], "rear", "end-")
    s["rs2"] = h("Drive Shaft 2 m", s["rs1"], "end+", "end-")
    s["rs4"] = h("Drive Shaft 1 m", s["rs2"], "end+", "end-")
    s["rs5"] = h("Drive Shaft 0.5 m", s["rs4"], "end+", "end-")
    s["rr"] = h("Right-Angle Gearbox", s["rs5"], "end+", "-z")
    s["cvl"] = h("CV Joint", s["fdiff"], "-x", "+x")
    s["cvr"] = h("CV Joint", s["fdiff"], "+x", "-x")
    s["pvl"] = h("Steering Pivot", s["cvl"], "-x", "base")
    s["pvr"] = h("Steering Pivot", s["cvr"], "+x", "base")
    s["lfl"] = h("Leaf Spring", s["pvl"], "leaf", "mount")
    s["lfr"] = h("Leaf Spring", s["pvr"], "leaf", "mount")
    s["lrl"] = h("Stiff Suspension", s["rdiff"], "-x", "mount")
    s["lrr"] = h("Stiff Suspension", s["rdiff"], "+x", "mount")
    for k in ("lfl", "lfr", "lrl", "lrr"):
        s["w" + k[1:]] = h("Large Wheel", s[k], "axle", "axle")
    return s


def facing(local, world):
    """The turns that send the part's local axis direction (e.g. '+y', the side opposite its back face) to a body direction."""
    return {r for r in range(24) if bpm.apply_dir(r, DIRS.index(local)) == DIRS.index(world)}


def any_rot(bp, part, lo, rots=range(24), **kw):
    """put_box with the first turn that some socket takes."""
    errs = []
    for r in rots:
        try:
            return bp.put_box(part, lo, r, **kw)
        except ValueError as e:
            errs.append(str(e))
    raise ValueError(f"{part} at {lo}: no turn fits")


def thin_axis(defs, part, axis):
    """Turns that put the part's thin (local y) side along body axis 0 (x), 1 (y) or 2 (z)."""
    return [r for r in range(24) if bpm.apply_dir(r, 2) >> 1 == axis]


def build(defs):
    bp = Blueprint(defs)
    s = chassis(bp)
    h = bp.hang
    # floor on three risers over the left rail
    for name, beam, sock in (("a", s["b0"], "y+1"), ("b", s["r0"], "y+0"), ("c", s["r2"], "y+0")):
        s["riser" + name] = h("Frame Block", beam, sock, "-y")
    z0 = {"a": -0.875, "b": 1.125, "c": 3.125}
    for k, z in z0.items():
        s["floor" + k] = bp.put_box("Wood Planks 2 m", (-0.625, 0.75, z), 0)
    def fit(part, lo, hi, tol=0.03, **kw):
        want = [(lo[k] + hi[k]) / 2 for k in range(3)]
        box = ([lo[k] - tol for k in range(3)], [hi[k] + tol for k in range(3)])
        return bp.near(part, want, box=box, limit=0.2, **kw)

    X0, X1 = -0.505, 1.245  # side walls
    LW, RW = facing("-y", "+x"), facing("-y", "-x")  # the back face of a left (right) wall looks into the room
    def wallrow(side, z, part_low, part_high, rots):
        x = X0 if side == "l" else X1
        s[side + "low" + str(z)] = fit(part_low, (x - 0.015, 0.805, z), (x + 0.015, 1.805, z + 2), rots=rots)
        s[side + "high" + str(z)] = fit(part_high, (x - 0.015, 1.805, z), (x + 0.015, 2.805, z + 2), rots=rots)
    wallrow("l", -0.875, "Sheet Steel 1x2 m", "Glass Pane 1x2 m", LW)
    wallrow("l", 1.125, "Sheet Steel 1x2 m", "Glass Pane 1x2 m", LW)
    s["l2"] = fit("Sheet Steel 2 m", (X0 - 0.015, 0.805, 3.125), (X0 + 0.015, 2.805, 5.125), rots=LW)
    wallrow("r", -0.875, "Sheet Steel 1x2 m", "Glass Pane 1x2 m", RW)
    s["front"] = fit("Sheet Steel 1x2 m", (-0.625, 0.805, -0.875), (1.375, 1.805, -0.855))
    s["glass"] = fit("Glass Pane 1x2 m", (-0.625, 1.805, -0.875), (1.375, 2.805, -0.855), tol=0.05)
    s["rear"] = fit("Sheet Steel 2 m", (-0.625, 0.805, 5.105), (1.375, 2.805, 5.135), tol=0.05)
    for k, z in (("0", -0.875), ("2", 3.125)):
        s["roof" + k] = fit("Sheet Steel 2 m", (-0.625, 2.79, z), (1.375, 2.84, z + 2), tol=0.06)
    # over the door the roof stops short: the door (raised a hand on its hinge) stands 0.125 into the roof's level
    s["roof1"] = fit("Sheet Steel 1x2 m", (-0.625, 2.79, 1.125), (0.375, 2.84, 3.125), tol=0.06)
    s["roof1a"] = fit("Sheet Steel 0.5x1 m", (0.375, 2.79, 1.125), (0.875, 2.84, 2.125), tol=0.06)
    s["roof1b"] = fit("Sheet Steel 0.5x1 m", (0.375, 2.79, 2.125), (0.875, 2.84, 3.125), tol=0.06)
    engine_room(bp, s)
    interior(bp, s, fit)
    electrics(bp, s)
    if "--list" in sys.argv:
        bp.listing()
    if "--zfight" in sys.argv:
        for hit in zfight(bp):
            print("z-fight", *hit)
    return bp


def engine_room(bp, s):
    """Fuel tank beside the left rail, battery on the right rail up front, alternator on the diesel; the tank is piped to the engine."""
    h = bp.hang
    s["tank"] = bp.near("Large Fuel Tank", [-0.5, 0.0, 2.75], box=([-0.8, -0.3, 2.2], [-0.2, 0.3, 3.3]), limit=0.2)
    # (turned so its 0.2 m side is across the beam: its 0.3 m ends would be flush with the beam's bolt plate)
    across = {r for r in range(24) if bpm.apply_dir(r, DIRS.index("-y")) == DIRS.index("-y")
              and bpm.apply_dir(r, DIRS.index("+x")) in (DIRS.index("+z"), DIRS.index("-z"))}
    s["battery"] = bp.near("Battery", [0.5, 0.38, -2.75], box=([0.3, 0.2, -2.95], [0.7, 0.6, -2.55]), rots=across, limit=0.2)
    s["alt"] = h("Alternator", s["engine"], "+x", "-x")
    bp.extra.setdefault("links", []).append({"type": "pipe", "style": "fuel", "from": s["tank"], "to": s["engine"]})


def seat_turns(back):
    """Turns that keep a seat upright with its back toward body direction `back` ('+z' = faces forward... the way -z)."""
    return {r for r in range(24)
            if bpm.apply_dir(r, DIRS.index("-y")) == DIRS.index("-y") and bpm.apply_dir(r, DIRS.index("+z")) == DIRS.index(back)}


def interior(bp, s, fit):
    """Cab, door, furniture and electrics, mounted on stubs off the wall (the floor planks have no free top sockets)."""
    h = bp.hang
    # stubs: 0.5 m beams lying across, bottom flush with the floor, on the wall's lowest row
    s["stubL"] = fit("Square Beam 0.5 m", (-0.505, 0.8, -0.5), (0.02, 1.07, 0.0), tol=0.03)
    s["stubR"] = fit("Square Beam 0.5 m", (0.72, 0.8, -0.5), (1.25, 1.07, 0.0), tol=0.03)
    s["seatL"] = bp.near("Seat", [-0.12, 1.53, -0.25], box=([-0.52, 1.0, -0.8], [0.2, 2.1, 0.3]), rots={0}, limit=0.5)
    # the door: a hinge on the cab window's rear edge, the door, a jamb post on the floor's corner, panels to close the gap
    s["hinge"] = bp.near("Hinge", [1.25, 1.93, 1.23], box=([1.1, 1.6, 1.1], [1.4, 2.3, 1.4]), limit=0.1)
    s["door"] = h("Door", s["hinge"], "leaf", "hang")
    s["jamb"] = bp.near("Square Beam 2 m", [1.25, 1.81, 3.0], box=([1.1, 0.8, 2.85], [1.4, 2.82, 3.15]), limit=0.1)
    s["rwl"] = fit("Sheet Steel 0.5x1 m", (1.23, 0.805, 2.375), (1.28, 1.805, 2.875))
    s["rwh"] = fit("Glass Pane 0.5x1 m", (1.23, 1.805, 2.375), (1.28, 2.805, 2.875), tol=0.05)
    s["rear_r"] = fit("Sheet Steel 2 m", (1.23, 0.805, 3.125), (1.28, 2.805, 5.125), tol=0.05)
    # dinette on the left of the first room: two seats facing over a table
    s["stubD1"] = fit("Square Beam 0.5 m", (-0.505, 0.8, 1.1), (0.02, 1.07, 1.4))
    s["seatD1"] = bp.near("Seat", [-0.12, 1.53, 1.25], box=([-0.52, 1.0, 0.9], [0.2, 2.1, 1.6]), rots=seat_turns("-z"), limit=0.5)
    s["stubT"] = fit("Square Beam 0.5 m", (-0.505, 1.28, 1.55), (0.02, 1.57, 1.95))
    s["table"] = bp.near("Wood Planks 0.5 m", [-0.12, 1.58, 1.85], box=([-0.52, 1.3, 1.45], [0.2, 1.7, 2.2]), limit=0.5)


    s["stubD2"] = fit("Square Beam 0.5 m", (-0.505, 0.8, 2.35), (0.02, 1.07, 2.65))
    s["seatD2"] = bp.near("Seat", [-0.12, 1.53, 2.5], box=([-0.52, 1.0, 2.1], [0.2, 2.1, 2.95]), rots=seat_turns("+z"), limit=0.5)
    # kitchen: a chest-cupboard on the left, a parts shelf on the rear wall, a bunk over the left-rear corner
    s["stubK"] = fit("Square Beam 0.5 m", (-0.505, 0.8, 0.35), (0.02, 1.07, 0.65))
    s["chestK"] = bp.near("Chest", [-0.115, 1.33, 0.5], box=([-0.52, 1.0, 0.0], [0.3, 1.7, 1.0]), limit=0.5)


    s["stubS"] = fit("Square Beam 0.5 m", (0.745, 0.8, 4.35), (1.26, 1.07, 4.65))
    s["shelf"] = bp.near("Parts Shelf", [0.875, 1.66, 4.5], box=([0.5, 1.0, 3.8], [1.26, 2.4, 5.15]), limit=0.6)
    s["stubB1"] = fit("Square Beam 0.5 m", (-0.505, 1.29, 3.6), (0.02, 1.57, 3.9))
    s["stubB2"] = fit("Square Beam 0.5 m", (-0.505, 1.29, 4.85), (0.02, 1.57, 5.15))

    s["bunk"] = bp.near("Wood Planks 1x2 m", [0.0, 1.58, 4.1], box=([-0.52, 1.45, 3.0], [0.6, 1.7, 5.15]), limit=0.6)
    # boarding: a ladder and a step plate hung from the floor's right edge in front of the door
    s["ladder"] = bp.near("Boarding Ladder", [1.42, 0.16, 1.75], box=([1.3, -0.6, 1.4], [1.6, 0.8, 2.1]), limit=0.3)
    s["seatR"] = bp.near("Seat", [0.99, 1.53, -0.25], box=([0.6, 1.0, -0.8], [1.27, 2.1, 0.3]), rots={0}, limit=0.5)



def local_of(rot, v):
    """The vector `v` (body frame) in the frame of a part turned `rot`."""
    cols = [bpm.apply(rot, e) for e in ([1, 0, 0], [0, 1, 0], [0, 0, 1])]
    return [sum(cols[a][b] * v[b] for b in range(3)) for a in range(3)]


def electrics(bp, s):
    """Lamps and their wiring, the cargo bay and the awning."""
    h = bp.hang
    P = bp.parts
    # headlamps on the front blocks, tail lamps on top of the live axle
    s["head_l"] = h("Lamp", s["blk0"], "+y", "-y")
    s["head_r"] = h("Lamp", s["blk1"], "+y", "-y")
    s["tail_l"] = h("Lamp", s["rdiff"], "top2", "-y")
    s["tail_r"] = h("Lamp", s["rdiff"], "top3", "-y")
    # the driver's aim key toggles them: seat -> input reader -> toggle -> lamps
    s["reader"] = h("Input Reader", s["seatL"], "+x", "-x", setting=1)
    s["toggle"] = h("Toggle", s["stubL"], "+z", "-z")
    wires = [[s["seatL"], s["reader"]], [s["reader"], s["toggle"]]]
    wires += [[s["toggle"], s[k]] for k in ("head_l", "head_r", "tail_l", "tail_r")]
    # the ceiling lamps on a switch by the door
    s["switch"] = h("Switch", s["stubD1"], "+z", "-z")
    for k, at in (("lamp_a", (0.3, 2.7, 2.0)),):
        s[k] = bp.near("Lamp", list(at), box=([at[0] - 0.4, 2.55, at[2] - 0.5], [at[0] + 0.4, 2.82, at[2] + 0.5]), limit=0.6)
        wires.append([s["switch"], s[k]])
    # the cargo bay under the bunk
    s["cargo"] = bp.near("Cargo Volume", [0.0, 1.45, 4.1], box=([-0.45, 1.3, 3.4], [0.45, 1.56, 4.8]), limit=0.7)
    cp = P[s["cargo"]]
    size = [0.9, 0.5, 1.7]
    centre = [0.0, 1.1, 4.1]
    off = local_of(cp.rot, [centre[k] - cp.pos[k] for k in range(3)])
    sz = [abs(v) for v in local_of(cp.rot, size)]
    bp.steps[s["cargo"]]["config"] = {"startOn": True, "sizeX": round(sz[0], 1), "sizeY": round(sz[1], 1), "sizeZ": round(sz[2], 1),
                                       "atX": round(off[0], 2), "atY": round(off[1], 2), "atZ": round(off[2], 2)}
    # the awning: a motor hinge on the roof's right edge, worked by a switch on the passenger stub
    s["motor"] = bp.near("Motor Hinge", [1.48, 2.81, 3.75], box=([1.3, 2.6, 3.4], [1.7, 3.1, 4.1]), rots={5, 6}, limit=0.3)
    s["awning"] = h("Sheet Steel 0.5x1 m", s["motor"], "leaf", "-x0")
    s["switch2"] = h("Switch", s["stubR"], "+z", "-z")
    wires.append([s["switch2"], s["motor"]])
    bp.extra["wires"] = wires
    paint(bp, s)


CREAM, STRIPE, TRIM, TIMBER = "#efe6c8", "#c8602a", "#2c2f33", "#b08a5a"


def paint(bp, s):
    """Cream body with an orange stripe along the lower walls and the door, dark trim."""
    def put(keys, colour):
        for k in keys:
            bp.steps[s[k]]["paint"] = colour
    put([k for k in s if k.startswith("llow") or k.startswith("rlow")] + ["rwl", "door", "awning"], STRIPE)
    put(["l2", "rear", "rear_r", "front", "roof0", "roof1", "roof1a", "roof1b", "roof2"] + [k for k in s if k.startswith("lhigh")], CREAM)
    put(["jamb", "stubL", "stubR", "stubD1", "stubD2", "stubT", "stubK", "stubS", "stubB1", "stubB2", "riserb", "riserc", "ladder"], TRIM)
    put(["table", "bunk"], TIMBER)


def zfight(bp):
    """Same-facing coplanar overlapping faces among every drawn box of the assembly (ZFight.Find without its covered-face test)."""
    boxes = []
    for i, pl in enumerate(bp.parts):
        for c, h in pl.d["_all"]:
            ac = bpm.apply(pl.rot, c)
            ah = [abs(x) for x in bpm.apply(pl.rot, h)]
            boxes.append((i, pl.name, [pl.pos[k] + ac[k] for k in range(3)], ah))
    hits = []
    for a in range(len(boxes)):
        ia, na, ca, ha = boxes[a]
        for b in range(a + 1, len(boxes)):
            ib, nb, cb, hb = boxes[b]
            if ia == ib or any(abs(ca[k] - cb[k]) > ha[k] + hb[k] + 0.001 for k in range(3)):
                continue
            for ax in range(3):
                for sg in (1, -1):
                    da = sg * ca[ax] + ha[ax]
                    db = sg * cb[ax] + hb[ax]
                    if abs(da - db) > 0.0005:
                        continue
                    o = [k for k in range(3) if k != ax]
                    area = 1.0
                    for k in o:
                        area *= max(0.0, min(ca[k] + ha[k], cb[k] + hb[k]) - max(ca[k] - ha[k], cb[k] - hb[k]))
                    if area >= 1e-5:
                        hits.append((f"{ia}:{na}", f"{ib}:{nb}", "xyz"[ax] + ("+" if sg > 0 else "-"), round(da * sg, 3), round(area, 4)))
    return hits


def document(bp):
    doc = {"format": "call-of-slop-construct-blueprint/1", "name": "RV", "description": DESCRIPTION, "steps": bp.steps}
    doc.update(bp.extra)
    return json.dumps(doc, indent=2) + "\n"


DESCRIPTION = ("A 10 m motorhome you can walk about in: a diesel on the 4x4 truck's drive line (automatic gearbox, transfer case, "
               "right-angle gearboxes, a wide front axle with steering pivots and a locked live axle behind, leaf springs, four large wheels), "
               "a fuel tank piped to the engine, a battery and an alternator. Cab with two seats; a hinged door (F) with a ladder and a step; "
               "a dinette, a cupboard chest, a parts shelf, a bunk with a cargo bay under it. Aim switches the head and tail lamps, a switch "
               "by the door lights the ceiling, another swings the awning out.")


def main():
    defs = load_defs()
    bp = build(defs)
    text = document(bp)
    lo, hi, mass = bp.report()
    print(f"{len(bp.steps)} steps, {mass:.0f} kg, {hi[0]-lo[0]:.2f} x {hi[1]-lo[1]:.2f} x {hi[2]-lo[2]:.2f} m")
    if "--check" in sys.argv:
        have = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        print("up to date" if have == text else "out of date")
        return 0 if have == text else 1
    with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    if "--bin" in sys.argv:
        with open(os.path.join("bin", "Release", "net10.0", OUT), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
