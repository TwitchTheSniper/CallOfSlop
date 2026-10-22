#!/usr/bin/env python3
"""Writes the Bocage map's pack-local decals (Maps/World/Decals/Bocage/*.json): farm-track mud and hoof prints, cow pats,
apple blossom under the orchard, lichen on old stone, whitewash wear, the village name board, tricolour bunting and an
invented cider maker's poster (Cidre des Haies: an apple and a hedge, no real brand).

Usage: python3 tools/world/bocage_decals.py <repo root>
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/World/Decals/Bocage")

D = []


def decal(name, desc, size, blend, surfaces, layers, rotation="random", **kw):
    d = {"format": "call-of-slop-decal/1", "name": name, "category": "Bocage", "description": desc, "author": "Call of Slop",
         "size": size, "rotation": rotation, "blend": blend, "surfaces": surfaces}
    d.update(kw)
    d["layers"] = layers
    D.append(d)


decal("Bocage Cow Pat", "A cow pat in the pasture grass.", 0.6, "multiply", "floor", [
    {"shape": "splat", "radius": 0.3, "count": 8, "jag": 0.35, "seed": 501, "color": [0.33, 0.28, 0.18], "alpha": 0.9, "softness": 0.04},
    {"shape": "ring", "radius": 0.18, "width": 0.05, "color": [0.25, 0.2, 0.13], "alpha": 0.7, "softness": 0.03},
    {"shape": "circle", "radius": 0.08, "color": [0.28, 0.23, 0.15], "alpha": 0.8}], sizeVariation=0.3)

hooves = []
for k, (x, y) in enumerate(((0.35, 0.15), (0.6, 0.3), (0.38, 0.45), (0.63, 0.6), (0.36, 0.75), (0.6, 0.9))):
    hooves.append({"shape": "circle", "at": [x - 0.025, y], "radius": 0.035, "color": [0.32, 0.25, 0.17], "alpha": 0.85, "softness": 0.01})
    hooves.append({"shape": "circle", "at": [x + 0.025, y], "radius": 0.035, "color": [0.32, 0.25, 0.17], "alpha": 0.85, "softness": 0.01})
decal("Bocage Hoof Prints", "Cloven hoof prints pressed into a muddy gateway, walking north.", 1.6, "multiply", "floor",
      [{"shape": "splat", "radius": 0.42, "count": 9, "jag": 0.5, "seed": 502, "color": [0.62, 0.55, 0.45], "alpha": 0.6, "softness": 0.1, "noise": 0.4}] + hooves)

decal("Bocage Cart Ruts", "Two muddy wheel ruts and a grassy hump between them, for a farm track (runs north-south).", [2.0, 5.0], "multiply", "floor", [
    {"shape": "line", "at": [0.25, 0.05], "to": [0.27, 0.95], "width": 0.07, "color": [0.4, 0.32, 0.22], "alpha": 0.85, "softness": 0.04, "noise": 0.35, "noiseScale": 10},
    {"shape": "line", "at": [0.75, 0.05], "to": [0.73, 0.95], "width": 0.07, "color": [0.4, 0.32, 0.22], "alpha": 0.85, "softness": 0.04, "noise": 0.35, "noiseScale": 10},
    {"shape": "speckle", "at": [0.5, 0.5], "radius": 0.45, "count": 30, "width": 0.02, "seed": 503, "color": [0.45, 0.38, 0.28], "alpha": 0.6}],
    rotation=0, sizeVariation=0.15)

decal("Bocage Apple Blossom", "Pink-white apple blossom petals fallen in the orchard grass.", 1.6, "alpha", "floor", [
    {"shape": "speckle", "at": [0.5, 0.5], "radius": 0.45, "count": 60, "width": 0.018, "seed": 504, "color": [0.96, 0.9, 0.9], "alpha": 0.95},
    {"shape": "speckle", "at": [0.5, 0.5], "radius": 0.38, "count": 40, "width": 0.016, "seed": 505, "color": [0.94, 0.72, 0.78], "alpha": 0.95}],
    sizeVariation=0.3)

decal("Bocage Lichen", "Yellow and grey lichen rosettes on old granite and limestone.", 1.2, "alpha", ["wall", "floor"], [
    {"shape": "splat", "at": [0.4, 0.55], "radius": 0.16, "count": 9, "jag": 0.6, "seed": 506, "color": [0.78, 0.68, 0.3], "alpha": 0.7, "noise": 0.5, "noiseScale": 12},
    {"shape": "splat", "at": [0.65, 0.35], "radius": 0.12, "count": 8, "jag": 0.6, "seed": 507, "color": [0.7, 0.72, 0.62], "alpha": 0.65, "noise": 0.5, "noiseScale": 12},
    {"shape": "speckle", "at": [0.5, 0.5], "radius": 0.4, "count": 22, "width": 0.03, "seed": 508, "color": [0.8, 0.62, 0.28], "alpha": 0.7}],
    sizeVariation=0.4)

decal("Bocage Whitewash Wear", "Rain-streaked, flaking whitewash at the foot of an old wall.", [2.0, 1.4], "multiply", "wall", [
    {"shape": "splat", "at": [0.5, 0.25], "radius": 0.3, "count": 10, "jag": 0.5, "seed": 509, "color": [0.72, 0.68, 0.6], "alpha": 0.7, "softness": 0.12, "noise": 0.5},
    {"shape": "line", "at": [0.3, 0.9], "to": [0.32, 0.3], "width": 0.03, "color": [0.7, 0.66, 0.6], "alpha": 0.6, "softness": 0.05},
    {"shape": "line", "at": [0.62, 0.95], "to": [0.6, 0.4], "width": 0.025, "color": [0.7, 0.66, 0.6], "alpha": 0.6, "softness": 0.05}],
    rotation=0, sizeVariation=0.25)

sign = [{"shape": "rect", "at": [0.5, 0.5], "size": [0.96, 0.9], "color": [0.95, 0.94, 0.9]},
        {"shape": "rect", "at": [0.5, 0.5], "size": [0.9, 0.78], "color": [0.78, 0.12, 0.1]},
        {"shape": "rect", "at": [0.5, 0.5], "size": [0.84, 0.66], "color": [0.95, 0.94, 0.9]}]
for k, x in enumerate((0.16, 0.25, 0.34, 0.43, 0.52, 0.61, 0.7, 0.79)):   # block letters, a village name in the game's chunky style
    sign.append({"shape": "rect", "at": [x + 0.02, 0.56], "size": [0.065, 0.2], "color": [0.08, 0.08, 0.08]})
sign.append({"shape": "rect", "at": [0.5, 0.33], "size": [0.5, 0.05], "color": [0.08, 0.08, 0.08]})
decal("Bocage Village Sign", "A white French village name board with a red border at the edge of the hamlet (bold block lettering).", [1.4, 0.5], "alpha", "wall",
      sign, rotation=0)

bunting = []
cols = [[0.0, 0.33, 0.64], [0.93, 0.93, 0.9], [0.94, 0.25, 0.21]]
for k in range(8):
    x = 0.08 + k * 0.12
    bunting.append({"shape": "rect", "at": [x, 0.85], "size": 0.16, "rotation": 45, "color": cols[k % 3]})
    bunting.append({"shape": "rect", "at": [x, 0.93], "size": [0.24, 0.16], "erase": True})
bunting.append({"shape": "line", "at": [0.02, 0.85], "to": [0.98, 0.85], "width": 0.012, "color": [0.2, 0.2, 0.2]})
decal("Bocage Tricolour Bunting", "A string of blue, white and red pennants for the village fete.", [3.2, 0.5], "alpha", "wall", bunting, rotation=0)

poster = [{"shape": "rect", "at": [0.5, 0.5], "size": [0.84, 0.94], "color": [0.92, 0.86, 0.66]},
          {"shape": "rect", "at": [0.5, 0.17], "size": [0.84, 0.24], "color": [0.22, 0.38, 0.18]},
          {"shape": "circle", "at": [0.5, 0.6], "radius": 0.22, "color": [0.78, 0.16, 0.12]},
          {"shape": "circle", "at": [0.42, 0.67], "radius": 0.06, "color": [0.92, 0.5, 0.4], "alpha": 0.7},
          {"shape": "line", "at": [0.5, 0.8], "to": [0.52, 0.9], "width": 0.02, "color": [0.35, 0.22, 0.12]},
          {"shape": "rect", "at": [0.6, 0.88], "size": [0.12, 0.05], "rotation": 25, "color": [0.25, 0.5, 0.2]}]
for x in (0.24, 0.34, 0.44, 0.54, 0.64, 0.74):
    poster.append({"shape": "rect", "at": [x + 0.01, 0.17], "size": [0.06, 0.12], "color": [0.95, 0.9, 0.7]})
decal("Bocage Cider Poster", "An old enamel poster for the invented cider maker Cidre des Haies: a red apple over a green band.", [0.8, 1.1], "alpha", "wall",
      poster, rotation=0)


def main():
    os.makedirs(OUT, exist_ok=True)
    for d in D:
        fn = "".join(ch for ch in d["name"].title() if ch.isalnum()) + ".json"
        with open(os.path.join(OUT, fn), "w") as f:
            json.dump(d, f, indent=2)
            f.write("\n")
    print(f"{len(D)} decals written to {OUT}")


if __name__ == "__main__":
    main()
