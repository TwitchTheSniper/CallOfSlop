#!/usr/bin/env python3
"""Writes Maps/Dev/BuildingsLondon.json ("London Street"), the Dev pack's showcase for the London set: the Railway
Arches (with its deck reached by the stair bay), two Bay Fronted Terraces in different palettes, and every prop in
Props/British on its own pad along the street. Run from the repository root:  python3 tools/world/london_showcase.py
"""
import json
import os

BRITISH = ["Black Cab", "Double Decker Bus", "Belisha Beacon", "Canal Narrowboat", "Canal Lock Gate", "Railway Track",
           "Commuter Train Car", "Railway Signal", "Iron Railings", "Heritage Lamp Post", "Ticket Gate", "Mooring Ring Bollard"]


def main():
    ops = [
        {"op": "Ground", "color": [0.36, 0.42, 0.3]},
        {"op": "Perimeter", "height": 4, "color": [0.55, 0.5, 0.45]},
        {"op": "Block", "x0": -56, "y0": 0, "z0": -1.5, "x1": 56, "y1": 0.03, "z1": 5.5, "color": [0.23, 0.23, 0.24]},
        {"op": "Block", "x0": -56, "y0": 0, "z0": -4.0, "x1": 56, "y1": 0.12, "z1": -1.5, "color": [0.56, 0.55, 0.52]},
        {"op": "Block", "x0": -56, "y0": 0, "z0": 5.5, "x1": 56, "y1": 0.12, "z1": 8.0, "color": [0.56, 0.55, 0.52]},
        {"op": "Building", "name": "Railway Arches", "at": [-10, -14], "yaw": 0, "seed": 1},
        {"op": "Building", "name": "Bay Fronted Terrace", "at": [-24, 20.6], "yaw": 180, "seed": 2},
        {"op": "Building", "name": "Bay Fronted Terrace", "at": [-4, 20.6], "yaw": 180, "seed": 3, "variant": {"palette": "white stucco", "mirror": True}},
        {"op": "Building", "name": "Bay Fronted Terrace", "at": [16, 20.6], "yaw": 180, "seed": 4, "variant": {"palette": "red brick"}},
    ]
    # the props on pads along the north pavement and the road, long ones on the road
    x = 16.0
    for name in BRITISH:
        if name in ("Double Decker Bus", "Commuter Train Car", "Canal Narrowboat"):
            continue
        ops.append({"op": "Block", "x0": x - 1.4, "y0": 0.12, "z0": -14.2, "x1": x + 1.4, "y1": 0.17, "z1": -11.0, "color": [0.48, 0.48, 0.5]})
        ops.append({"op": "Prop", "name": name, "x": x, "z": -12.6, "y": 0.17})
        x += 4.0
    ops.append({"op": "Prop", "name": "Double Decker Bus", "x": -30, "z": 1.0})
    ops.append({"op": "Prop", "name": "Commuter Train Car", "x": 30, "z": 2.6})
    ops.append({"op": "Prop", "name": "Canal Narrowboat", "x": 34, "z": 13.0})
    ops.append({"op": "Prop", "name": "Railway Track", "x": -10, "z": -12.8, "y": 5.4})
    ops.append({"op": "Prop", "name": "Railway Track", "x": -16, "z": -12.8, "y": 5.4})
    for s in ([-50, -24], [-50, 30], [50, -24], [50, 30], [-40, 10], [40, 10], [0, 10], [-20, 10], [20, 10], [0, -24]):
        ops.append({"op": "Spawn", "x": s[0], "z": s[1]})
    ops.append({"op": "AutoWaypoints", "spacing": 6})
    m = {
        "format": "call-of-slop-map/1",
        "name": "London Street",
        "description": "The London set from the Buildings and Props folders: the railway arches (stairs up to the deck), bay-fronted terraces and the British props.",
        "author": "Call of Slop",
        "order": 975,
        "halfX": 58,
        "halfZ": 34,
        "sky": [0.62, 0.66, 0.7],
        "flagPool": "none",
        "ops": ops,
        "foliage": {"seed": 3, "regions": [{"type": "grass", "density": 2.5, "rect": [-56, 9, 56, 33]}]},
    }
    with open(os.path.join("Maps", "Dev", "BuildingsLondon.json"), "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")
    print("Maps/Dev/BuildingsLondon.json written")


if __name__ == "__main__":
    main()
