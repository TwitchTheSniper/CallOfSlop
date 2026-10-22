#!/usr/bin/env python3
"""Writes Maps/Dev/BuildingsVillage.json ("Village Buildings"): the Dev pack's showcase of the English-village set,
the Stone Cottage, the Thatched Cottage and the Village Shop along a lane, and every prop in Props/Countryside on its
own pad across the lane. (The self-test wants every building on a Dev Buildings* map and every prop on a Dev map.)

Usage: python3 tools/world/village_showcase.py
"""
import glob
import json
import os

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
names = sorted(json.load(open(f))["name"] for f in glob.glob(os.path.join(REPO, "Props", "Countryside", "*.json")))
ops = [
    {"op": "Ground", "color": [0.37, 0.5, 0.25]},
    {"op": "Perimeter", "height": 3, "color": [0.72, 0.62, 0.42]},
    {"op": "Block", "x0": -40, "y0": 0, "z0": -2.5, "x1": 40, "y1": 0.03, "z1": 2.5, "color": [0.27, 0.27, 0.29]},
    {"op": "Building", "name": "Stone Cottage", "at": [-24, -12], "yaw": 0, "spawns": True, "seed": 3},
    {"op": "Building", "name": "Thatched Cottage", "at": [-8, -12], "yaw": 0, "spawns": True, "seed": 4, "variant": {"palette": "old thatch"}},
    {"op": "Building", "name": "Village Shop", "at": [10, -12.5], "yaw": 0, "spawns": True, "seed": 5},
    {"op": "Building", "name": "Stone Cottage", "at": [28, -12], "yaw": 0, "seed": 6, "variant": {"palette": "weathered stone", "mirror": True}},
]
# the props: two rows of pads south of the lane
for i, n in enumerate(names):
    row, col = divmod(i, 9)
    x, z = -32 + col * 8, 9 + row * 9
    ops.append({"op": "Block", "x0": x - 2.6, "y0": 0, "z0": z - 2.4, "x1": x + 2.6, "y1": 0.05, "z1": z + 2.4, "color": [0.6, 0.58, 0.52]})
    y = 1.9 if n == "Hanging Flower Basket" else 0
    p = {"op": "Prop", "name": n, "x": x, "z": z + (-2.3 if n == "Hanging Flower Basket" else 0)}
    if y:
        p["y"] = y
    ops.append(p)
    if n == "Hanging Flower Basket":   # a bit of wall to hang it on
        ops.append({"op": "Block", "x0": x - 1, "y0": 0, "z0": z - 2.45, "x1": x + 1, "y1": 2.6, "z1": z - 2.3, "color": [0.72, 0.62, 0.42]})
ops += [{"op": "Spawn", "x": -36, "z": 0}, {"op": "Spawn", "x": 36, "z": 0}, {"op": "Spawn", "x": 0, "z": 0}, {"op": "Spawn", "x": -18, "z": 0},
        {"op": "AutoWaypoints", "spacing": 6}]
m = {"format": "call-of-slop-map/1", "name": "Village Buildings",
     "description": "The English village set: the stone and thatched cottages and the village shop along a lane, and the countryside props.",
     "author": "Call of Slop", "order": 968, "halfX": 40, "halfZ": 30, "sky": [0.66, 0.74, 0.82], "flagPool": "none", "ops": ops,
     "foliage": {"seed": 3, "regions": [{"type": "grass", "density": 2.5, "rect": [-39, -29, 39, -3]}]}}
with open(os.path.join(REPO, "Maps", "Dev", "BuildingsVillage.json"), "w") as f:
    f.write("{\n" + ",\n".join(f'  "{k}": ' + (("[\n" + ",\n".join("    " + json.dumps(o) for o in v) + "\n  ]") if k == "ops" else json.dumps(v)) for k, v in m.items()) + "\n}\n")
print(len(names), "props,", sum(1 for o in ops if o["op"] == "Building"), "buildings")
