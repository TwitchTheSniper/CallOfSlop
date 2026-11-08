#!/usr/bin/env python3
"""Writes the tool upgrades: Constructs/ToolUpgrades.json and the module parts in Constructs/Parts/Upgrades.

docs/sandbox/TOOL-UPGRADES.md. Each stat (ToolStat in Source/Constructs/ToolUpgrades.cs, in that order) has a base value,
what every survivor starts with, and levels, each made by fitting one module part (F on it lying loose). An Assembler
builds the modules: RECIPES below, which tools/constructs/assembling.py takes into Constructs/Assembling.json, so run
that after this. Some are taught by factions ("teaches" in Story/Factions), which this script doesn't write.

Run it from the repo root: python3 -I tools/constructs/upgrade_parts.py (it rewrites the files; --check only compares).
Data first: the JSON files are what the game loads; this script only keeps them consistent. Fix it, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zfight  # noqa: E402

FORMAT = "call-of-slop-tool-upgrades/1"
ABOUT = ("Tool upgrades (docs/sandbox/TOOL-UPGRADES.md). Written by tools/constructs/upgrade_parts.py: fix it, not this file. "
         "Each stat: 'base' is where every survivor starts, 'creative' what creative gives, 'levels' each made by fitting its "
         "module 'part' (F on it, lying loose), in order of value; a higher Mk may be fitted over a lower. A function is base 0 "
         "and one level of 1. The stats' order is ToolStat's (Source/Constructs/ToolUpgrades.cs).")

# (stat, name, about, base, creative, colour, [(part, file, value, mass)])
STATS = [
    ("lift", "Lift", "the heaviest thing the physics gun takes, in kg; its pull rises with it (30 N a kg)", 1500, 20000000, "#5c6fa8",
     [("Lift Coil Mk 2", "LiftCoilMk2", 3000, 3), ("Lift Coil Mk 3", "LiftCoilMk3", 6000, 4),
      ("Lift Coil Mk 4", "LiftCoilMk4", 12000, 5), ("Lift Coil Mk 5", "LiftCoilMk5", 25000, 6)]),
    ("reach", "Reach", "how far the physics gun's beam reaches, in metres", 12, 30, "#8fc5d9",
     [("Beam Lens Mk 2", "BeamLensMk2", 15, 1), ("Beam Lens Mk 3", "BeamLensMk3", 18, 1.5),
      ("Beam Lens Mk 4", "BeamLensMk4", 22, 2), ("Beam Lens Mk 5", "BeamLensMk5", 26, 2.5)]),
    ("drillBit", "Drill bit", "the ground drill's scoop, times the digging table's radius", 1, 1, "#a8763a",
     [("Drill Bit Mk 2", "DrillBitMk2", 1.25, 4), ("Drill Bit Mk 3", "DrillBitMk3", 1.5, 5), ("Drill Bit Mk 4", "DrillBitMk4", 1.8, 6)]),
    ("drillMotor", "Drill motor", "how fast the ground drill scoops, times the digging table's pace", 1, 1, "#7f8a94",
     [("Drill Motor Mk 2", "DrillMotorMk2", 1.35, 4), ("Drill Motor Mk 3", "DrillMotorMk3", 1.75, 5), ("Drill Motor Mk 4", "DrillMotorMk4", 2.25, 6)]),
    ("fitter", "Fitting speed", "how fast timed fits, removals and welds go, times their usual pace", 1, 1, "#c9794a",
     [("Fitter Servo Mk 2", "FitterServoMk2", 1.5, 2), ("Fitter Servo Mk 3", "FitterServoMk3", 2, 3), ("Fitter Servo Mk 4", "FitterServoMk4", 3, 4)]),
    ("tractor", "Tractor beam", "the physics gun catches things out to twice its reach and reels them in", 0, 1, "#a78fd0",
     [("Tractor Emitter", "TractorEmitter", 1, 3)]),
    ("throw", "Throw", "with the physics gun holding something, Reload throws it along the look", 0, 1, "#d0a040",
     [("Kinetic Coil", "KineticCoil", 1, 4)]),
]

# What an Assembler takes for each module (assembling.py's rec(): output, seconds, tier, materials, parts).
RECIPES = [
    ("Lift Coil Mk 2", 20, 2, {"copper": 6, "steel": 4}, {"Copper Coil": 1}),
    ("Lift Coil Mk 3", 40, 3, {"copper": 10, "steel": 8}, {"Field Coil": 1, "Control Unit": 1}),
    ("Lift Coil Mk 4", 80, 4, {"copper": 16, "gold": 4}, {"Field Coil": 2, "Control Unit": 1}),
    ("Lift Coil Mk 5", 160, 4, {"copper": 20, "gold": 8, "levite": 6}, {"Levite Core": 1, "Field Coil": 2}),
    ("Beam Lens Mk 2", 15, 2, {"glass": 4, "bronze": 2}, {"Lens": 1}),
    ("Beam Lens Mk 3", 30, 3, {"glass": 6, "bronze": 4}, {"Lens": 2, "Circuit Board": 1}),
    ("Beam Lens Mk 4", 60, 3, {"glass": 10, "copper": 4}, {"Lens": 3, "Control Unit": 1}),
    ("Beam Lens Mk 5", 120, 4, {"glass": 12, "gold": 4}, {"Lens": 4, "Control Unit": 1}),
    ("Drill Bit Mk 2", 15, 1, {"steel": 10}, None),
    ("Drill Bit Mk 3", 30, 2, {"steel": 16, "bronze": 4}, None),
    ("Drill Bit Mk 4", 60, 3, {"steel": 24, "bronze": 6}, {"Bearing": 2}),
    ("Drill Motor Mk 2", 15, 2, {"steel": 6, "copper": 4}, {"Gear Set": 1}),
    ("Drill Motor Mk 3", 30, 2, {"steel": 8, "copper": 6}, {"Gear Set": 1, "Servo Motor": 1}),
    ("Drill Motor Mk 4", 60, 3, {"steel": 10, "copper": 8}, {"Servo Motor": 2, "Control Unit": 1}),
    ("Fitter Servo Mk 2", 15, 2, {"steel": 4, "copper": 2}, {"Servo Motor": 1}),
    ("Fitter Servo Mk 3", 30, 3, {"steel": 6}, {"Servo Motor": 2, "Control Unit": 1}),
    ("Fitter Servo Mk 4", 60, 4, {"steel": 8, "gold": 2}, {"Servo Motor": 3, "Control Unit": 2}),
    ("Tractor Emitter", 60, 3, {"copper": 10, "glass": 6}, {"Field Coil": 1, "Lens": 2, "Control Unit": 1}),
    ("Kinetic Coil", 45, 3, {"steel": 12, "copper": 8}, {"Copper Coil": 2, "Servo Motor": 1}),
]


def module(stat, name, colour, mark, mass, about):
    """A palm-sized module: a base plate, a body and a cap, with one rib a Mk (a function: a lens on top)."""
    boxes = [
        {"min": [-0.1, 0, -0.07], "max": [0.1, 0.03, 0.07]},
        {"min": [-0.08, 0.03, -0.055], "max": [0.08, 0.11, 0.055], "color": colour},
    ]
    if mark == 0:
        boxes.append({"min": [-0.035, 0.11, -0.035], "max": [0.035, 0.15, 0.035], "color": "#e8e8e0"})
    for k in range(mark):
        x = -0.06 + k * 0.03
        boxes.append({"min": [x, 0.11, -0.04], "max": [x + 0.016, 0.13, 0.04], "color": "#e8e8e0"})
    sockets = [{"name": "bottom", "at": [0, 0, 0], "dir": "-y"}]
    zfight.resolve(boxes, sockets)
    for b in boxes:
        b["min"] = [round(v, 4) for v in b["min"]]
        b["max"] = [round(v, 4) for v in b["max"]]
    return {
        "format": "call-of-slop-construct-part/1", "name": name,
        "description": f"A tool upgrade: {about}. F on it, lying loose, fits it to your tools.",
        "group": "Production", "section": "Tool upgrades", "role": "panel", "tool": "wrench", "attachSeconds": 1.0,
        "mass": mass, "health": 40, "material": "steel", "color": "#30343a", "boxes": boxes, "sockets": sockets,
    }


def write(path, text, check):
    if check:
        if not os.path.exists(path) or open(path, encoding="utf-8").read() != text:
            print(f"out of date: {path}")
            return 1
        return 0
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return 0


def main():
    check = "--check" in sys.argv
    bad = 0
    stats = []
    for stat, name, about, base, creative, colour, levels in STATS:
        function = base == 0 and len(levels) == 1 and levels[0][2] == 1
        for k, (part, file, value, mass) in enumerate(levels):
            mark = 0 if function else k + 2
            body = module(stat, part, colour, mark, mass, about)
            bad += write(os.path.join("Constructs", "Parts", "Upgrades", file + ".json"), json.dumps(body, indent=2) + "\n", check)
        stats.append({"stat": stat, "name": name, "about": about, "base": base, "creative": creative,
                      "levels": [{"part": part, "value": value} for part, _, value, _ in levels]})
    names = {p for s in STATS for p, _, _, _ in s[6]}
    for r in RECIPES:
        if r[0] not in names:
            print(f"a recipe for {r[0]}, which isn't a module")
            bad += 1
    missing = names - {r[0] for r in RECIPES}
    for n in sorted(missing):
        print(f"no recipe for {n}")
        bad += 1
    lines = ",\n".join("    " + json.dumps(s) for s in stats)
    text = '{\n  "format": "%s",\n  "about": %s,\n  "stats": [\n%s\n  ]\n}\n' % (FORMAT, json.dumps(ABOUT), lines)
    bad += write(os.path.join("Constructs", "ToolUpgrades.json"), text, check)
    print(f"tool upgrades: {'checked' if check else 'written'}, {len(names)} modules, {bad} problems (run assembling.py for their recipes)")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
