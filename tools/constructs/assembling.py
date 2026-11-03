#!/usr/bin/env python3
"""Writes the Sandbox's assembling data: Constructs/Assembling.json and the sub-components in Constructs/Parts/Components.

docs/sandbox/CONSTRUCTS.md, "Assembling". HAND holds the recipes written by hand: every sub-component and the parts a
player reaches for first, each drawing on different production chains. The long tail is generated from the part files
by rule: a part costs half its mass in its class's material (long_tail), its role adds what a part of that kind must
have (a Circuit Board in anything electrical, a Gear Set in a gearbox, a Servo Motor in an actuator), and the materials
in it set the tier. Rules, not a list, so a new part gets a recipe by running this.

Run it from the repo root: python3 -I tools/constructs/assembling.py (it rewrites the files; --check only compares).
Data first: the JSON files are what the game loads; this script only keeps them consistent. Fix it, not its output.
"""

import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zfight  # noqa: E402

FORMAT = "call-of-slop-assembling/1"
ABOUT = ("Assembling (docs/sandbox/CONSTRUCTS.md, 'Assembling'): what an Assembler builds. Written by tools/constructs/assembling.py: fix it, not this file. "
         "'output' names a part, 'count' how many a batch makes (default 1), 'materials' the bulk materials (Recycling.json) and 'parts' the item parts it takes, "
         "'seconds' the work at speed 1, 'minTier' the lowest tier of Assembler (default 1). At most 6 inputs; a physics-only part can't be an input.")

# Components: small item parts, each one step in a chain. (name, file, mass, colour, boxes)
COMPONENTS = [
    ("Gear Set", "GearSet", 6, "#7f8a94", [((-0.12, 0, -0.12), (0.12, 0.08, 0.12)), ((-0.07, 0.08, -0.07), (0.07, 0.15, 0.07))],
     "Steel gears, ready to go into a gearbox or a drive. A component: an Assembler uses it."),
    ("Bearing", "Bearing", 1.5, "#a8763a", [((-0.07, 0, -0.07), (0.07, 0.05, 0.07))],
     "A bronze bearing. A component: an Assembler uses it."),
    ("Piston Set", "PistonSet", 12, "#6f7d8c", [((-0.15, 0, -0.1), (0.15, 0.22, 0.1)), ((-0.11, 0.22, -0.07), (0.11, 0.3, 0.07))],
     "Pistons and rods in steel and bronze: the heart of an engine. A component: an Assembler uses it."),
    ("Copper Coil", "CopperCoil", 4, "#c9794a", [((-0.1, 0, -0.1), (0.1, 0.16, 0.1)), ((-0.13, 0.05, -0.13), (0.13, 0.11, 0.13))],
     "Wound copper wire, for motors and alternators. A component: an Assembler uses it."),
    ("Wiring Loom", "WiringLoom", 3, "#2c2c2e", [((-0.16, 0, -0.04), (0.16, 0.06, 0.04)), ((-0.04, 0.06, -0.12), (0.04, 0.1, 0.12))],
     "Copper wire in rubber. A component: an Assembler uses it."),
    ("Circuit Board", "CircuitBoard", 1, "#3f9b5c", [((-0.12, 0, -0.09), (0.12, 0.02, 0.09)), ((-0.05, 0.02, -0.04), (0.05, 0.05, 0.04))],
     "Circuits on a laminate board. A component: an Assembler uses it."),
    ("Control Unit", "ControlUnit", 4, "#30343a", [((-0.12, 0, -0.1), (0.12, 0.14, 0.1)), ((-0.05, 0.14, -0.05), (0.05, 0.17, 0.05))],
     "A Circuit Board and a Wiring Loom in a case: what a powered machine thinks with. A component: an Assembler uses it."),
    ("Servo Motor", "ServoMotor", 8, "#55705a", [((-0.1, 0, -0.14), (0.1, 0.2, 0.14)), ((-0.04, 0.05, 0.14), (0.04, 0.13, 0.22))],
     "A small electric motor with bearings and a coil: what moves an actuator. A component: an Assembler uses it."),
    ("Tyre Carcass", "TyreCarcass", 9, "#2c2c2e", [((-0.25, 0, -0.1), (0.25, 0.5, 0.1)), ((-0.1, 0.1, -0.11), (0.1, 0.4, 0.11))],
     "Rubber over a cloth carcass, ready for a rim. A component: an Assembler uses it."),
    ("Steel Plate", "SteelPlate", 8, "#6f7d8c", [((-0.25, 0, -0.25), (0.25, 0.03, 0.25))],
     "Rolled steel plate. A component: an Assembler uses it."),
    ("Lens", "Lens", 0.5, "#8fc5d9", [((-0.05, 0, -0.05), (0.05, 0.1, 0.05)), ((-0.04, 0.1, -0.04), (0.04, 0.12, 0.04))],
     "Ground glass in a bronze ring, for scopes, sensors and lamps. A component: an Assembler uses it."),
]


def rec(output, seconds, tier=1, count=1, m=None, p=None):
    r = {"output": output}
    if count > 1:
        r["count"] = count
    if m:
        r["materials"] = m
    if p:
        r["parts"] = p
    r["seconds"] = seconds
    if tier > 1:
        r["minTier"] = tier
    return r


# Every one of these draws on at least two chains (ore to metal to steel; wood to planks to rubber and cloth; copper; sand to glass; gold).
HAND = [
    # Components.
    rec("Gear Set", 4, m={"steel": 5}, p=None),
    rec("Bearing", 2, 2, count=2, m={"bronze": 2}),
    rec("Piston Set", 8, 2, m={"steel": 4, "bronze": 2}),
    rec("Copper Coil", 3, 2, m={"copper": 3}),
    rec("Wiring Loom", 3, 2, m={"copper": 2, "rubber": 1}),
    rec("Circuit Board", 4, 3, m={"circuits": 2, "laminate": 1}),
    rec("Control Unit", 6, 3, p={"Circuit Board": 1, "Wiring Loom": 1}, m={"steel": 1}),
    rec("Servo Motor", 6, 2, m={"steel": 2}, p={"Copper Coil": 1, "Bearing": 2}),
    rec("Tyre Carcass", 5, m={"rubber": 4, "cloth": 2}),
    rec("Steel Plate", 2, count=2, m={"steel": 5}),
    rec("Lens", 3, 2, m={"glass": 1, "bronze": 1}),
    # Drivetrain.
    rec("Engine", 20, 2, m={"steel": 20}, p={"Piston Set": 2, "Wiring Loom": 1}),
    rec("Small Engine", 10, 2, m={"steel": 6}, p={"Piston Set": 1, "Wiring Loom": 1}),
    rec("V8 Engine", 40, 3, m={"steel": 45}, p={"Piston Set": 4, "Wiring Loom": 2, "Bearing": 4}),
    rec("Diesel Engine", 50, 3, m={"steel": 70}, p={"Piston Set": 6, "Wiring Loom": 2, "Bearing": 6}),
    rec("Turbine", 60, 4, m={"steel": 25, "bronze": 10}, p={"Bearing": 8, "Control Unit": 1}),
    rec("Electric Motor", 15, 3, m={"steel": 10}, p={"Copper Coil": 4, "Bearing": 2, "Control Unit": 1}),
    rec("Transmission", 12, m={"steel": 8}, p={"Gear Set": 2}),
    rec("Differential", 12, 2, m={"steel": 10}, p={"Gear Set": 2, "Bearing": 2}),
    rec("Suspension", 5, m={"steel": 3, "rubber": 1}),
    rec("Wheel", 5, m={"steel": 2}, p={"Tyre Carcass": 1}),
    rec("Small Wheel", 3, m={"rubber": 3, "steel": 1}),
    rec("Large Wheel", 8, m={"steel": 4}, p={"Tyre Carcass": 2}),
    rec("Monster Wheel", 30, 3, m={"steel": 20}, p={"Tyre Carcass": 8}),
    rec("Fuel Tank", 6, m={"steel": 6, "rubber": 1}),
    rec("Fuel Line", 2, count=2, m={"rubber": 1, "copper": 1}),
    rec("Alternator", 8, 2, m={"steel": 1}, p={"Copper Coil": 2, "Bearing": 1}),
    # Power and control.
    rec("Battery", 8, 3, m={"copper": 4, "circuits": 2}),
    rec("Battery Pack", 40, 4, m={"copper": 20, "steel": 12}, p={"Control Unit": 1}),
    rec("Seat", 5, m={"cloth": 3, "steel": 3}),
    rec("Lamp", 2, 2, m={"copper": 1}, p={"Lens": 1}),
    rec("Proximity Sensor", 4, 3, p={"Lens": 1, "Circuit Board": 1}),
    # Weapons.
    rec("Gun Barrel", 6, m={"steel": 2}),
    rec("Ammo Box", 5, 2, m={"steel": 3, "bronze": 3}),
    rec("Rifle Receiver", 10, 2, m={"steel": 6}, p={"Gear Set": 1, "Bearing": 2}),
    rec("Gun Scope", 4, 2, m={"steel": 1}, p={"Lens": 2}),
    rec("Mounted MG", 20, 3, m={"steel": 10, "bronze": 4}, p={"Gear Set": 1, "Control Unit": 1}),
    # Machines and storage.
    rec("Materials Bin", 10, m={"steel": 8, "concrete": 2}),
    rec("Chest", 6, m={"planks": 8, "steel": 2}),
    rec("Recycler", 30, 2, m={"steel": 45}, p={"Gear Set": 4, "Wiring Loom": 2}),
    rec("Inventory Beacon", 25, 3, m={"steel": 16, "copper": 6}, p={"Control Unit": 2}),
    rec("Foundation", 15, m={"concrete": 100, "steel": 10}),
    rec("Frame Block", 4, m={"steel": 6}),
    rec("Frame Beam", 6, m={"steel": 14}),
    rec("Panel", 3, m={"steel": 4}),
    rec("Wood Panel", 2, m={"laminate": 4}),
]

# What a role adds, beyond the mass in material. Parts per 50 kg, at least one.
ROLE_PARTS = {
    "engine": "Piston Set", "transmission": "Gear Set", "differential": "Gear Set", "transfer": "Gear Set", "joint": "Servo Motor",
    "battery": "Control Unit", "alternator": "Copper Coil", "logic": "Circuit Board", "hose": "Wiring Loom",
    "gun": "Gear Set", "suspension": "Bearing", "seat": "Tyre Carcass", "hinge": "Bearing", "door": "Bearing", "wheel": "Tyre Carcass",
    "enginePiece": "Piston Set", "generator": "Copper Coil", "thruster": "Gear Set",
}
# What a part's class costs: (material, share of the part's mass as units of it per kg, second material or None).
CLASS = {
    "steel": ("steel", 0.2, None), "armour": ("steel", 0.25, "bronze"), "wood": ("planks", 1.0, "laminate"), "concrete": ("concrete", 0.17, "steel"),
    "rubber": ("rubber", 0.3, "steel"), "glass": ("glass", 0.2, "steel"), "cloth": ("cloth", 1.0, "steel"), "sand": ("sand", 0.2, "planks"),
}
TIER_OF = {"steel": 1, "concrete": 1, "planks": 1, "laminate": 1, "rubber": 1, "cloth": 1, "glass": 1, "sand": 1, "bronze": 2, "copper": 2, "circuits": 3}
PART_TIER = {"Gear Set": 1, "Tyre Carcass": 1, "Bearing": 2, "Piston Set": 2, "Copper Coil": 2, "Wiring Loom": 2, "Servo Motor": 2, "Circuit Board": 3, "Control Unit": 3}
SKIP_FOLDERS = ("Spawn", "Components")
SKIP_NAMES = ("Material Bundle", "Construct Core", "Chest Lid", "Lid Hinge")
MAX_UNITS = 2000
CLASS_TIER = {"small": 1, "medium": 2, "large": 3, "capital": 4}  # the lowest Assembler for a size class (tools/constructs/scale.py); a part's cost tops out at MAX_UNITS, its time at 600 s


def is_item(p):
    return p.get("tool", "wrench") != "welder" and p.get("mass", 0) <= 40 and not p.get("physicsOnly")


def long_tail(p):
    """A part's recipe by rule."""
    mass = float(p["mass"])
    role = p.get("role", "frame")
    material, share, second = CLASS.get(p.get("material", "steel"), CLASS["steel"])
    units = min(MAX_UNITS, max(1, round(mass * share)))
    m = {material: units}
    if second:
        m[second] = min(MAX_UNITS, max(1, units // 4))
    extra = {}
    if role in ROLE_PARTS:
        extra[ROLE_PARTS[role]] = min(20, max(1, round(mass / 50)))
    if role in ("logic", "battery", "alternator", "hose") and "copper" not in m:
        m["copper"] = min(MAX_UNITS, max(1, round(mass / 3)))
    if role == "storage" and p.get("tier"):
        # A machine of a tier is built from the tier below it in spirit: steel, a control unit from tier 3 up.
        m = {"steel": min(MAX_UNITS, round(mass / 8)), "concrete": min(MAX_UNITS, round(mass / 16))}
        extra = {"Gear Set": min(20, 2 * p["tier"])}
        if p["tier"] >= 2:
            extra["Wiring Loom"] = min(20, p["tier"])
        else:
            m["rubber"] = min(MAX_UNITS, round(mass / 40)) # (a tier 1 machine asks nothing a tier 1 machine can't make, so survival can start)
        if p["tier"] >= 3:
            extra["Control Unit"] = min(20, p["tier"] - 2)
    if len(m) + len(extra) < 2 and mass >= 3:
        # Two chains at least: anything of steel has a little rubber or concrete in its fixings.
        m["bronze" if mass < 12 else "rubber" if role not in ("frame",) else "concrete"] = min(MAX_UNITS // 4, max(1, round(mass / 25))) # (fixings stay fixings on a 240 t crank)
    size = p.get("sizeClass", "small")
    tier = max([TIER_OF.get(k, 1) for k in m] + [PART_TIER.get(k, 1) for k in extra] + [min(5, p.get("tier", 1)), CLASS_TIER[size]])
    seconds = min(180.0 if size == "small" else 600.0, round(2 + mass * 0.03, 1))
    return rec(p["name"], seconds, tier, m=m, p=extra or None)


def parts():
    out = []
    for f in sorted(glob.glob(os.path.join("Constructs", "Parts", "**", "*.json"), recursive=True)):
        with open(f, encoding="utf-8") as h:
            p = json.load(h)
        p["_folder"] = os.path.relpath(os.path.dirname(f), os.path.join("Constructs", "Parts")).replace("\\", "/")
        out.append(p)
    return out


def component(c):
    name, file, mass, colour, shapes, description = c
    boxes = [{"min": list(lo), "max": list(hi)} for lo, hi in shapes]
    sockets = [{"name": "bottom", "at": [0, 0, 0], "dir": "-y"}]
    zfight.resolve(boxes, sockets)
    return {
        "format": "call-of-slop-construct-part/1", "name": name, "description": description, "group": "Production", "section": "Components",
        "role": "panel", "tool": "wrench", "attachSeconds": 1.0, "mass": mass, "health": 40, "material": "steel", "color": colour,
        "boxes": [{"min": [round(v, 4) for v in b["min"]], "max": [round(v, 4) for v in b["max"]]} for b in boxes], "sockets": sockets,
    }


def write(path, text, check):
    if check:
        if not os.path.exists(path) or open(path, encoding="utf-8").read() != text:
            print(f"out of date: {path}")
            return 1
        return 0
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return 0


def main():
    check = "--check" in sys.argv
    bad = 0
    for c in COMPONENTS:
        bad += write(os.path.join("Constructs", "Parts", "Components", c[1] + ".json"), json.dumps(component(c), indent=2) + "\n", check)
    known = parts()
    by_name = {p["name"]: p for p in known}
    hand = {r["output"] for r in HAND}
    tail = []
    for p in known:
        name = p["name"]
        if name in hand or p.get("stage") or name in SKIP_NAMES or p["_folder"].startswith(SKIP_FOLDERS):
            continue
        tail.append(long_tail(p))
    recipes = HAND + tail
    for r in recipes:
        if r["output"] not in by_name:
            print(f"no part named {r['output']}")
            bad += 1
        for q in r.get("parts", {}):
            if q not in by_name or not is_item(by_name[q]):
                print(f"{r['output']}: {q} isn't an item part")
                bad += 1
        if len(r.get("materials", {})) + len(r.get("parts", {})) > 6:
            print(f"{r['output']}: too many inputs")
            bad += 1
    lines = ",\n".join("    " + json.dumps(r) for r in recipes)
    text = '{\n  "format": "%s",\n  "about": %s,\n  "recipes": [\n%s\n  ]\n}\n' % (FORMAT, json.dumps(ABOUT), lines)
    bad += write(os.path.join("Constructs", "Assembling.json"), text, check)
    print(f"assembling: {'checked' if check else 'written'}, {len(HAND)} by hand, {len(tail)} generated, {bad} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
