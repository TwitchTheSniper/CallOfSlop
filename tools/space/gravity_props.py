#!/usr/bin/env python3
"""Writes the Space pack's gravity-yard props (Props/Space/): the salvage and gravity-plating set used by Keelhaul Yard
and Twin Shard (tools/space/keelhaul.py, tools/space/twinshard.py). All original: an invented breaker's yard.

    python3 tools/space/gravity_props.py          (run from the repository root)
"""
import json
import os

OUT = os.path.join("Props", "Space")

STEEL = "#6c6f73"
DARK = "#34363a"
HULL = "#8f8a7c"
BRASS = "#8d7458"
WARN = "#ffb24a"
GLOW = "#7fe6ff"
RED = "#d84a3a"


def box(size, offset, color, material=None):
    d = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    if material:
        d["material"] = material
    return d


def save(name, desc, parts, collider=None, collision="solid", scatter=None, breakable=None):
    os.makedirs(OUT, exist_ok=True)
    d = {"format": "call-of-slop-prop/1", "name": name, "category": "Space", "description": desc, "author": "Call of Slop",
         "collision": collision}
    if collider:
        d["collider"] = collider
    if scatter:
        d["scatter"] = scatter
    if breakable:
        d["breakable"] = breakable
    d["parts"] = parts
    fn = os.path.join(OUT, name.replace(" ", "") + ".json")
    with open(fn, "w") as f:
        json.dump(d, f, indent=1)
        f.write("\n")
    print("wrote", fn)


def gravity_plating_node():
    parts = [
        box([2.0, 0.25, 2.0], [0, 0.125, 0], HULL, "metal"),
        box([1.6, 0.08, 1.6], [0, 0.29, 0], DARK),
        box([0.9, 0.5, 0.9], [0, 0.5, 0], STEEL, "metal"),
        box([0.5, 0.12, 0.5], [0, 0.81, 0], GLOW),
    ]
    for (x, z) in ((-0.8, -0.8), (0.8, -0.8), (-0.8, 0.8), (0.8, 0.8)):
        parts.append(box([0.18, 0.3, 0.18], [x, 0.4, z], BRASS))
    for (x, z) in ((-0.6, 0), (0.6, 0), (0, -0.6), (0, 0.6)):
        parts.append(box([0.2, 0.04, 0.2], [x, 0.31, z], WARN))
    save("Gravity Plating Node", "A floor-mounted gravity plating emitter: a 2 m plate with a glowing core (knee-high cover).", parts,
         collider=[{"size": [2.0, 0.75, 2.0]}], scatter={"randomYaw": False, "yawStep": 90})


def salvage_cutter_rig():
    parts = [
        box([2.6, 0.3, 1.4], [0, 0.15, 0], DARK, "metal"),
        box([0.5, 2.6, 0.5], [-0.9, 1.45, 0], STEEL, "metal"),
        box([2.2, 0.25, 0.25], [0.2, 2.6, 0], STEEL),
        box([0.3, 0.9, 0.3], [1.2, 2.1, 0], BRASS),
        box([0.2, 0.2, 0.2], [1.2, 1.55, 0], RED),
        box([0.9, 0.9, 0.9], [0.4, 0.75, 0.1], HULL, "metal"),
        box([0.6, 0.4, 0.3], [0.4, 1.4, 0.1], DARK),
        box([0.3, 0.05, 0.3], [0.4, 1.63, 0.1], WARN),
    ]
    save("Salvage Cutter Rig", "A plasma cutting rig on a skid: a mast with a swung-out torch arm over a generator box (head-high cover).", parts,
         collider=[{"size": [2.6, 1.4, 1.4], "offset": [0, 0.7, 0]}, {"size": [0.5, 2.8, 0.5], "offset": [-0.9, 1.4, 0]}],
         scatter={"randomYaw": True})


def mooring_clamp():
    parts = [
        box([1.6, 0.4, 1.6], [0, 0.2, 0], DARK, "metal"),
        box([1.0, 1.4, 1.0], [0, 1.1, 0], STEEL, "metal"),
        box([0.3, 1.0, 1.3], [-0.6, 2.2, 0], BRASS),
        box([0.3, 1.0, 1.3], [0.6, 2.2, 0], BRASS),
        box([1.5, 0.2, 0.3], [0, 2.55, 0], HULL),
        box([0.3, 0.3, 0.3], [0, 1.9, 0.55], WARN),
    ]
    save("Mooring Clamp", "A hull mooring clamp: two brass jaws on a steel pedestal (head-high cover, 1.6 m square).", parts,
         collider=[{"size": [1.6, 2.7, 1.6]}], scatter={"randomYaw": False, "yawStep": 90})


def hull_plate_stack():
    parts = []
    for i in range(5):
        parts.append(box([3.0 - i * 0.1, 0.22, 2.0 - i * 0.08], [i * 0.05, 0.11 + i * 0.24, -i * 0.04], HULL if i % 2 == 0 else STEEL, "metal"))
    parts.append(box([0.8, 0.1, 0.2], [0.6, 1.25, 0.3], WARN))
    parts.append(box([0.2, 1.3, 0.2], [-1.3, 0.65, -0.8], DARK))
    parts.append(box([0.2, 1.3, 0.2], [1.3, 0.65, -0.8], DARK))
    save("Hull Plate Stack", "Cut hull plates stacked between two posts: waist-high cover, 3 m long.", parts,
         collider=[{"size": [3.0, 1.3, 2.0]}], scatter={"randomYaw": True, "scale": [0.9, 1.1]})


def warning_beacon():
    parts = [
        box([0.5, 0.15, 0.5], [0, 0.075, 0], DARK),
        box([0.12, 2.2, 0.12], [0, 1.25, 0], STEEL),
        box([0.3, 0.3, 0.3], [0, 2.5, 0], WARN),
        box([0.36, 0.08, 0.36], [0, 2.7, 0], DARK),
    ]
    save("Gravity Warning Beacon", "A post with an amber beacon marking a gravity change (post only is solid).", parts,
         collider=[{"size": [0.2, 2.7, 0.2]}], scatter={"randomYaw": False})


def salvage_scrap():
    parts = [
        box([0.9, 0.12, 0.5], [0, 0.06, 0], HULL, "metal"),
        box([0.5, 0.1, 0.7], [0.3, 0.17, 0.1], STEEL),
        box([0.3, 0.3, 0.3], [-0.25, 0.2, -0.1], DARK),
        box([0.6, 0.05, 0.15], [0.1, 0.27, -0.2], BRASS),
    ]
    save("Salvage Scrap", "A heap of cut plate and brackets (looks only).", parts, collision="cosmetic",
         scatter={"randomYaw": True, "scale": [0.7, 1.3]})


if __name__ == "__main__":
    gravity_plating_node()
    salvage_cutter_rig()
    mooring_clamp()
    hull_plate_stack()
    warning_beacon()
    salvage_scrap()
