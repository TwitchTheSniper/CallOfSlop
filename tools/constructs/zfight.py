"""No coplanar faces (z-fighting) for the construct part generators (CLAUDE.md, "No coplanar faces").

Two drawn boxes of one part that have a face each on the same plane, facing the same way, with overlapping
rectangles, flicker through each other. `resolve(boxes, sockets)` nudges the smaller face of each pair by a few
millimetres so one is clearly in front (an inset, or proud when the face lies inside the other's face). The same
rule as Source/App/ZFight.cs, which the "No z-fighting" self-test uses to check the generated files.

Boxes are the generators' dicts ({"min": [...], "max": [...], "detail": true, "hidden": true, "shape": ...});
a hidden or contoured box is skipped. A face that carries a socket stays put (the socket sits on it).
"""

TOL = 0.0005  # planes closer than this are the same plane
MIN_AREA = 1e-5
STEP = 0.003  # the nudge, metres (x1 for the first conflicting face, x2 for the next...)


def _drawn(b):
    return not b.get("hidden") and not b.get("shape")


def _faces(boxes):
    """Every drawn box face as (box index, axis, sign, plane)."""
    out = []
    for i, b in enumerate(boxes):
        if not _drawn(b):
            continue
        for a in range(3):
            out.append((i, a, 1, b["max"][a]))
            out.append((i, a, -1, b["min"][a]))
    return out


def _overlap(b1, b2, a):
    area = 1.0
    for k in range(3):
        if k == a:
            continue
        lo = max(b1["min"][k], b2["min"][k])
        hi = min(b1["max"][k], b2["max"][k])
        if hi - lo <= 0:
            return 0.0
        area *= hi - lo
    return area


def _face_area(b, a):
    area = 1.0
    for k in range(3):
        if k != a:
            area *= b["max"][k] - b["min"][k]
    return area


def hits(boxes):
    """Coplanar same-facing overlapping pairs: (i, j, axis, sign, area), skipping faces covered by a third box."""
    found = []
    faces = _faces(boxes)
    for x in range(len(faces)):
        i, a, s, p = faces[x]
        for y in range(x + 1, len(faces)):
            j, a2, s2, p2 = faces[y]
            if j == i or a2 != a or s2 != s or abs(p2 - p) > TOL:
                continue
            area = _overlap(boxes[i], boxes[j], a)
            if area < MIN_AREA:
                continue
            if _covered(boxes, i, j, a, s, p):
                continue
            found.append((i, j, a, s, area))
    return found


def _covered(boxes, i, j, a, s, p):
    """A third box sits against the shared patch (a point just outside its middle is inside it)."""
    mid = [0.0, 0.0, 0.0]
    for k in range(3):
        if k == a:
            mid[k] = p + s * 0.001
        else:
            lo = max(boxes[i]["min"][k], boxes[j]["min"][k])
            hi = min(boxes[i]["max"][k], boxes[j]["max"][k])
            mid[k] = (lo + hi) / 2
    for n, b in enumerate(boxes):
        if n in (i, j) or not _drawn(b):
            continue
        if all(b["min"][k] - 1e-5 <= mid[k] <= b["max"][k] + 1e-5 for k in range(3)):
            return True
    return False


def _locked(boxes, sockets):
    """Faces (box, axis, sign) a socket sits on: they must not move."""
    locked = set()
    for sk in sockets or []:
        d = sk["dir"]
        a = "xyz".index(d[1])
        s = 1 if d[0] == "+" else -1
        for i, b in enumerate(boxes):
            if b.get("detail") or not _drawn(b):
                continue
            plane = b["max"][a] if s > 0 else b["min"][a]
            if abs(plane - sk["at"][a]) < 1e-3 and all(b["min"][k] - 1e-3 <= sk["at"][k] <= b["max"][k] + 1e-3 for k in range(3) if k != a):
                locked.add((i, a, s))
    return locked


def resolve(boxes, sockets=None, step=STEP):
    """Nudges `boxes` (in place) until no pair is coplanar. Returns the number of boxes changed."""
    locked = _locked(boxes, sockets)
    solids = [b for b in boxes if not b.get("detail")]
    extreme = {}
    for a in range(3):
        extreme[(a, 1)] = max(b["max"][a] for b in solids) if solids else 0
        extreme[(a, -1)] = min(b["min"][a] for b in solids) if solids else 0
    changed = set()
    for passes in range(12):
        found = hits(boxes)
        if not found:
            break
        adj = {}
        contained = set()
        for i, j, a, s, area in found:
            ki, kj = (i, a, s), (j, a, s)
            adj.setdefault(ki, []).append(kj)
            adj.setdefault(kj, []).append(ki)
            if area >= _face_area(boxes[i], a) * 0.98:
                contained.add(ki)
            if area >= _face_area(boxes[j], a) * 0.98:
                contained.add(kj)
        order = sorted(adj, key=lambda k: (k not in locked, bool(boxes[k[0]].get("detail")), -_face_area(boxes[k[0]], k[1]), k[0], k[1], k[2]))
        level = {}
        for k in order:
            used = {level[n] for n in adj[k] if n in level}
            lv = 0
            while lv in used:
                lv += 1
            level[k] = lv
        for k, lv in level.items():
            if lv == 0 or k in locked:
                continue
            i, a, s = k
            b = boxes[i]
            d = step * lv * (1 + 0.37 * passes)
            thick = b["max"][a] - b["min"][a]
            proud = k in contained or thick - d < 0.002
            plane = b["max"][a] if s > 0 else b["min"][a]
            if proud and not b.get("detail") and abs(plane - extreme[(a, s)]) < 1e-4:
                if thick - d < 0.002:
                    continue  # a solid face on the part's bounds can't come out (sockets sit on the bounds) or go in: leave it
                proud = False
            move = d if proud else -d  # outward when proud, inward when inset
            if s > 0:
                b["max"][a] = round(b["max"][a] + move, 4)
            else:
                b["min"][a] = round(b["min"][a] - move, 4)
            changed.add(i)
    return len(changed)
