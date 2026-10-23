#!/usr/bin/env python3
"""Writes a prop- and decal-heavy stress map for the streaming study (docs/perf/STREAMING.md).

    python3 tools/perf/stress_grid.py OUT.json [--props N] [--decals N] [--half H] [--walls W]

The map is a 2H x 2H m yard: a ground slab, a perimeter, a W x W grid of low wall boxes (drawn one by one, like
hand-placed map boxes), N props scattered with Clutter ops (2,000 per op, the op's limit) and N decals with
DecalScatter ops (also 2,000 per op; the map's limit is 6,000). Defaults: 400 x 400 m, 10,000 props, 5,000 decals,
40 x 40 walls. It is NOT shipped in Maps/Dev: every all-maps self-test (nav graph build under 3 s, edge walks,
collision parity, map round trips) would run on it. Load it with --check-map, or point a scratch test at it.
"""
import json
import sys

def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    out = argv[1]
    opts = {"props": 10000, "decals": 5000, "half": 200, "walls": 40}
    i = 2
    while i + 1 < len(argv):
        key = argv[i].lstrip("-")
        opts[key] = int(argv[i + 1])
        i += 2
    half, walls = opts["half"], opts["walls"]
    ops = [
        {"op": "Ground", "color": [0.42, 0.42, 0.44]},
        {"op": "Perimeter", "height": 6, "color": [0.62, 0.62, 0.64]},
    ]
    # Low walls on a grid, leaving 8 m lanes, so there is cover and plenty of boxes to draw one by one.
    pitch = (2 * half - 16) / walls
    for ix in range(walls):
        for iz in range(walls):
            x = -half + 8 + ix * pitch + pitch / 2
            z = -half + 8 + iz * pitch + pitch / 2
            along_x = (ix + iz) % 2 == 0
            w, d = (pitch * 0.45, 0.4) if along_x else (0.4, pitch * 0.45)
            ops.append({"op": "Block", "x0": x - w / 2, "y0": 0, "z0": z - d / 2, "x1": x + w / 2, "y1": 1.2 + (ix * 7 + iz * 3) % 4 * 0.4, "z1": z + d / 2,
                        "color": [0.5 + (ix % 3) * 0.1, 0.45, 0.4]})
    ops.append({"op": "RingSpawns", "count": 32, "frac": 0.85})
    ops.append({"op": "AutoWaypoints", "spacing": 10})
    # Props: quadrant strips, 2,000 per Clutter op (the op's limit).
    pools = [["Street"], ["Industrial", "Debris"], ["Furniture", "Garden"], ["Household", "Office"], ["Street", "Debris", "Industrial"]]
    left = opts["props"]
    k = 0
    while left > 0:
        n = min(2000, left)
        strips = max(1, -(-opts["props"] // 2000))
        z0 = -half + 6 + (2 * half - 12) * k / strips
        z1 = -half + 6 + (2 * half - 12) * (k + 1) / strips
        ops.append({"op": "Clutter", "props": pools[k % len(pools)], "x0": -half + 6, "z0": z0, "x1": half - 6, "z1": z1, "count": n, "seed": 100 + k})
        left -= n
        k += 1
    # Decals: floor scatters, 2,000 per op.
    decals = ["Oil Stain", "Puddle", "Floor Cracks", "Leaf Litter", "Tyre Tracks", "Dirt Patch", "Rust Stain", "Skid Marks"]
    left = opts["decals"]
    k = 0
    while left > 0:
        n = min(2000, left)
        ops.append({"op": "DecalScatter", "decals": decals, "x0": -half + 4, "z0": -half + 4, "x1": half - 4, "z1": half - 4, "count": n, "seed": 500 + k, "minSize": 0.8, "maxSize": 2.5})
        left -= n
        k += 1
    m = {
        "format": "call-of-slop-map/1",
        "name": "Stress Grid",
        "description": f"Streaming study stress map: {opts['props']} props, {opts['decals']} decals, {walls * walls} wall boxes on {2 * half} x {2 * half} m. Not a playable map.",
        "author": "Call of Slop",
        "order": 999,
        "halfX": half,
        "halfZ": half,
        "maxPlayers": 32,
        "sky": [0.36, 0.42, 0.5],
        "ops": ops,
    }
    with open(out, "w") as f:
        json.dump(m, f, indent=1)
    print(f"wrote {out}: {len(ops)} ops, {walls * walls} walls, {opts['props']} props, {opts['decals']} decals, {2 * half} x {2 * half} m")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv))
