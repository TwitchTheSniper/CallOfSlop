#!/usr/bin/env python3
"""Writes Maps/Dev/TwinWorlds.json: two small worlds stitched by a walkable neck (phase 5, docs/space/GRAVITY.md).

Gravity is a "sum": each world a round body (a point mass outside, a uniform ball inside) and the neck a tetrahedral
body of the same density, so up turns smoothly as you walk from one world over the neck onto the other. Run from the
repo root: python3 -I tools/space/make_twin_worlds.py
"""
import json
import math
import os

R = 60.0          # each world's radius (m)
GAP = 40.0        # surface to surface along the axis
NECK_R = 14.0     # the neck's radius
SURFACE = 9.81    # gravity on each world's surface (m/s^2)
D = R + GAP / 2   # centres at x = -D and +D
NECK_END = D - R * 0.9  # the neck runs into each world a little (no seam to fall through)
SIDES, RINGS = 16, 6
NECK_DENSITY = 3.0  # times the worlds' density: a heavier neck holds walkers on it (it feels under 30 degrees steep)


def r3(v):
    return [round(v[0], 3), round(v[1], 3), round(v[2], 3)]


def neck_mesh():
    """The neck as prisms of a cylinder along x, each split into three tetrahedra (GravityShapes.Neck in C#)."""
    verts, tets = [], []
    centres, rim = [], []
    for k in range(RINGS + 1):
        x = -NECK_END + 2 * NECK_END * k / RINGS
        centres.append(len(verts))
        verts.append([x, 0.0, 0.0])
        row = []
        for s in range(SIDES):
            a = 2 * math.pi * s / SIDES
            row.append(len(verts))
            verts.append([x, NECK_R * math.cos(a), NECK_R * math.sin(a)])
        rim.append(row)
    for k in range(RINGS):
        for s in range(SIDES):
            s2 = (s + 1) % SIDES
            a, b, c = centres[k], rim[k][s], rim[k][s2]
            a2, b2, c2 = centres[k + 1], rim[k + 1][s], rim[k + 1][s2]
            tets += [[a, b, c, a2], [b, c, a2, b2], [c, a2, b2, c2]]
    return verts, tets


def volume(verts, tets):
    v = 0.0
    for t in tets:
        p0, p1, p2, p3 = (verts[i] for i in t)
        a = [p1[i] - p0[i] for i in range(3)]
        b = [p2[i] - p0[i] for i in range(3)]
        c = [p3[i] - p0[i] for i in range(3)]
        v += abs(a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0])) / 6
    return v


def team_spawns(cx):
    """Eight spawns in a ring round a world's top, 40 degrees down from it."""
    out = []
    for k in range(8):
        a = 2 * math.pi * k / 8
        lat = math.radians(50)
        d = [math.cos(lat) * math.cos(a), math.sin(lat), math.cos(lat) * math.sin(a)]
        out.append(r3([cx + d[0] * (R + 0.05), d[1] * (R + 0.05), d[2] * (R + 0.05)]))
    return out


def main():
    verts, tets = neck_mesh()
    mu_ball = SURFACE * R * R
    rho = mu_ball / (4 / 3 * math.pi * R ** 3)  # GM per cubic metre
    mu_neck = rho * NECK_DENSITY * volume(verts, tets)
    rock, metal, neck = [0.5, 0.44, 0.38], [0.6, 0.62, 0.66], [0.42, 0.4, 0.36]
    ops = [
        {"op": "Planet", "radius": R, "x": -D, "color": rock, "detail": 5, "material": "Dirt"},
        {"op": "Mesh", "mesh": "icosphere", "x": D, "z": 0, "y": -R, "width": 2 * R, "height": 2 * R, "depth": 2 * R, "detail": 5,
         "color": [0.46, 0.42, 0.4], "material": "Dirt", "solid": True},
        # The neck: a cylinder standing on its bottom at x = -NECK_END, rolled to lie along +x.
        {"op": "Mesh", "mesh": "cylinder", "x": -NECK_END, "z": 0, "y": 0, "width": 2 * NECK_R, "depth": 2 * NECK_R, "height": 2 * NECK_END,
         "segments": 32, "roll": -90, "color": neck, "material": "Dirt", "solid": True},
        {"op": "PlanetWaypoints", "spacing": 10},
    ]
    # Spawns round the west world's sunward side, and markers on both worlds' tops.
    for lon in range(0, 360, 45):
        ops.append({"op": "SurfaceSpawn", "lat": 55, "lon": lon})
    ops.append({"op": "SurfaceBlock", "lat": 90, "lon": 0, "w": 4, "h": 0.6, "d": 4, "color": metal, "material": "Metal"})
    ops.append({"op": "Mesh", "mesh": "box", "x": D, "z": 0, "y": R - 0.3, "width": 4, "height": 0.9, "depth": 4, "color": metal, "material": "Metal", "solid": True})
    ops.append({"op": "Mesh", "mesh": "box", "x": 0, "z": 0, "y": NECK_R - 0.3, "width": 3, "height": 0.6, "depth": 1.2, "color": [0.8, 0.7, 0.2], "material": "Metal", "solid": True})
    m = {
        "format": "call-of-slop-map/1",
        "name": "Twin Worlds",
        "description": "Two small worlds stitched together by a neck you can walk across: gravity is the sum of both worlds and the neck (Dev map for phase 5's summed gravity).",
        "author": "Call of Slop",
        "order": 990,
        "halfX": 200,
        "halfZ": 120,
        "maxPlayers": 8,
        "gravity": {
            "type": "sum",
            "axis": [0, 0, -1],
            "sources": [
                {"type": "point", "name": "West", "center": [-D, 0, 0], "radius": R, "surface": SURFACE},
                {"type": "point", "name": "East", "center": [D, 0, 0], "radius": R, "surface": SURFACE},
                {"type": "tetra", "name": "Neck", "mu": round(mu_neck, 3), "vertices": [r3(v) for v in verts], "tetras": tets},
            ],
        },
        "skybox": {"preset": "night", "zenith": "#05060c", "horizon": "#05060c", "ground": "#05060c",
                   "sun": {"direction": [0.5, 0.7, 0.5], "intensity": 1.2, "size": 2.2}, "stars": 1.8, "clouds": False, "ambient": 1.9},
        "sandbox": {"resources": {"generate": False}},
        # One team on each world, each flag on its world's top (the generated ones crowd one world).
        "teamSpawns": [team_spawns(-D), team_spawns(D)],
        "flags": [[-D, R + 0.05, 0.0], [D, R + 0.05, 0.0]],
        "ops": ops,
    }
    out = os.path.join("Maps", "Dev", "TwinWorlds.json")
    with open(out, "w", newline="\n") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print(f"wrote {out}: neck {len(tets)} tetras, GM {mu_neck:.1f} (each world {mu_ball:.1f})")


if __name__ == "__main__":
    main()
