"""A machine's built-in screen: a dark plate on the flattest free spot of its model, and the "panels" entry that draws on it.

docs/sandbox/CONSTRUCTS.md, "Machine screens". Tiers and sizes have different models, so a screen is placed by looking at each one, not
by a table: mount() takes the drawn boxes as seen from each side, slides a screen-sized window over them, and picks the spot that is
flattest, near the height a player reads at, and clear of every socket, port and die on that side. The plate fills what lies under the
window and stands a little proud of the highest thing there, so it never cuts into a detail and shares no face with it.
"""

FACES = {"-z": (2, -1), "+z": (2, 1), "+x": (0, 1), "-x": (0, -1)}
PLATE = "#14181c"
ASPECT = 2.4  # width over height: four rows of 16 characters
STEPS_U, STEPS_Y = 13, 7  # sample points across the window


def seen(boxes, axis, sign, u, y):
    """How far out the model stands at (u, y) seen from this side, or None where there is nothing."""
    other = 2 - axis
    best = None
    for b in boxes:
        if b.get("hidden") or b.get("glass"):
            continue
        if b["min"][other] <= u <= b["max"][other] and b["min"][1] <= y <= b["max"][1]:
            at = b["max"][axis] if sign > 0 else -b["min"][axis]
            best = at if best is None else max(best, at)
    return best


def window(boxes, axis, sign, cu, cy, w, h):
    lo, hi = None, None
    for i in range(STEPS_U):
        for j in range(STEPS_Y):
            at = seen(boxes, axis, sign, cu + (i / (STEPS_U - 1) - 0.5) * w, cy + (j / (STEPS_Y - 1) - 0.5) * h)
            if at is None:
                return None
            lo = at if lo is None else min(lo, at)
            hi = at if hi is None else max(hi, at)
    return lo, hi


def mount(boxes, keep_clear, faces=("-z", "-x", "+x", "+z"), rows=4, range_m=30, width=None):
    """Returns (plate box, panel). keep_clear: [(at, face)] of sockets, ports and dies; a window on that side stays off them."""
    drawn = [b for b in boxes if not b.get("hidden")]
    lo = [min(b["min"][a] for b in drawn) for a in range(3)]
    hi = [max(b["max"][a] for b in drawn) for a in range(3)]
    across = max(hi[0] - lo[0], hi[2] - lo[2])
    w = width or round(min(across * 0.38, 3.6), 2)
    h = round(w / ASPECT, 2)
    border = round(h * 0.06, 3)
    pw, ph = w + 2 * border, h + 2 * border  # (the plate)
    target = min(max(1.6, (hi[1] - lo[1]) * 0.36), 3)  # (on a capital machine too, it is read from the ground)
    best = None
    for order, face in enumerate(faces):
        axis, sign = FACES[face]
        other = 2 - axis
        clear = [at for at, f in keep_clear if f == face]
        span = hi[other] - lo[other] - pw
        for iu in range(-8, 9):
            cu = (lo[other] + hi[other]) / 2 + iu / 8 * span / 2
            for iy in range(0, 25):
                cy = lo[1] + ph / 2 + 0.02 + iy / 24 * (hi[1] - lo[1] - ph - 0.04)
                if any(abs(at[other] - cu) < pw / 2 + 0.05 and abs(at[1] - cy) < ph / 2 + 0.05 for at in clear):
                    continue
                seen_at = window(boxes, axis, sign, cu, cy, pw, ph)
                if seen_at is None:
                    continue
                cost = (seen_at[1] - seen_at[0]) * 1.5 + abs(cy - target) * 1.0 + abs(cu - (lo[other] + hi[other]) / 2) * 0.1 + order * 0.1
                if best is None or cost < best[0]:
                    best = (cost, face, cu, cy, seen_at)
    assert best is not None, "no room for a screen"
    _, face, cu, cy, (near, far) = best
    axis, sign = FACES[face]
    other = 2 - axis
    proud = max(0.012, h * 0.03)
    outer = far + proud
    inner = near - max(0.02, h * 0.04)
    mn, mx = [0, 0, 0], [0, 0, 0]
    mn[other], mx[other] = cu - pw / 2, cu + pw / 2
    mn[1], mx[1] = cy - ph / 2, cy + ph / 2
    mn[axis], mx[axis] = (inner, outer) if sign > 0 else (-outer, -inner)
    plate = {"min": [round(v, 4) for v in mn], "max": [round(v, 4) for v in mx], "color": PLATE, "detail": True}
    at = [0, 0, 0]
    at[other], at[1] = round(cu, 4), round(cy, 4)
    at[axis] = round(sign * (outer + 0.004), 4)
    return plate, {"source": "machine", "at": at, "size": [round(w, 3), round(h, 3)], "face": face, "up": "+y", "rows": rows, "range": range_m}


def fit(boxes, plate, most=32):
    """Adds the plate to a part's boxes: what it hides goes, and if the part is at its limit of boxes (each is a draw call), so does its smallest detail."""
    def inside(b):
        return all(plate["min"][a] <= b["min"][a] and b["max"][a] <= plate["max"][a] for a in range(3))

    def size(b):
        return (b["max"][0] - b["min"][0]) * (b["max"][1] - b["min"][1]) * (b["max"][2] - b["min"][2])

    boxes[:] = [b for b in boxes if not (b.get("detail") and inside(b))]
    while len(boxes) >= most:
        boxes.remove(min((b for b in boxes if b.get("detail")), key=size))
    boxes.append(plate)
