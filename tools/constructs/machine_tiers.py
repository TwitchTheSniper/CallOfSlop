#!/usr/bin/env python3
"""Writes the Sandbox's machine tiers (Constructs/Parts/Gathering and Constructs/Parts/Refining).

docs/sandbox/CONSTRUCTS.md, "Machine tiers". Six lines of machine (Drill Module, Saw Module, Sawmill, Smelter, Foundry,
Assembler), five tiers each, following the threat ladder: 1 Scrap, 2 Workshop, 3 Industrial, 4 Military, 5 Plasma. The
two gathering modules are vehicle-sized (the Drill Module a compact rig with a small buffer); the other four are
buildings, sized in BIG_WIDTH.

Higher tiers are physically larger: every box, bin and work box grows by the tier's SIZE from the part's origin (the
centre of its bottom socket). Only what hangs below the floor keeps tier 1's length: a bit reaches the same 1.2 m under
the part at every tier, so a bigger drill still fits the ground clearance of a vehicle a small one fits (its bit is
thicker, not longer). Sockets stay on the 0.25 m frame grid: scaled, then snapped. Tier 2's 1.25 m body doesn't reach a
grid line, so its side and back sockets stand on small solid mounting pads at 0.75 m.

The curves are the CURVE table below: higher tiers work faster, hold more, are tougher, draw more power and weigh more,
and the power a unit costs falls. Bins hold what their volume holds (SIZE cubed, rounded); mass and health go by SIZE
squared, like a shell. The look of a tier (colours and drawn details) is tier_style.py's.

Run it from the repo root: python3 -I tools/constructs/machine_tiers.py (it rewrites the files; --check only compares).
Data first: the JSON files are what the game loads; this script only keeps the lines consistent. Fix it, not its output.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import screens  # noqa: E402
import tier_style  # noqa: E402
import zfight  # noqa: E402

FORMAT = "call-of-slop-construct-part/1"
GROUP = "Production"

TIERS = [None, "Scrap", "Workshop", "Industrial", "Military", "Plasma"]
PREFIX = [None, "", "Workshop ", "Industrial ", "Military ", "Plasma "]

GRID = 0.25  # the frame step: sockets stay on it
SIZE = {1: 1.0, 2: 1.25, 3: 1.5, 4: 2.0, 5: 2.5}  # linear size by tier

# tier: multipliers on tier 1's numbers.
CURVE = {
    #    rate  power cap   mass  health armour attach
    1: (1,  1.0, 1,   1.0,  1.0,  1.0, 1.0),
    2: (2,  1.8, 2,   1.5,  1.5,  1.5, 1.25),
    3: (4,  3.2, 3.5, 2.25, 2.25, 2.0, 1.5),
    4: (8,  5.6, 8,   4.0,  4.0,  3.0, 1.75),
    5: (16, 9.6, 16,  6.25, 6.25, 4.0, 2.0),
}

def box(mn, mx, role="body"):
    return {"min": list(mn), "max": list(mx), "role": role}


INK = {"key": "ink", "type": "colour", "label": "Screen colour", "group": "Display", "default": "#000000", "tooltip": "The colour of what its screen shows. Black: its own colours."}

MODULE_SOCKETS = [
    {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
    {"name": "+x", "at": [0.5, 0.3, 0], "dir": "+x"},
    {"name": "-x", "at": [-0.5, 0.3, 0], "dir": "-x"},
    {"name": "back", "at": [0, 0.3, -0.5], "dir": "-z"},
]
MODULE_BIN = dict(min=[-0.435, 0.055, -0.435], max=[0.435, 0.64, 0.435])

# A Drill Module is a compact rig: no bin of its own, a small hopper as its buffer (2 s of its rate at every tier), and a
# named Chute out it must be connected through to keep working. A housing, a boom and a tower carry the bit, at the same
# place under the part as ever. The gearhead on top takes the optional drive shaft: its socket is on the grid, and the
# port moves to the middle of the head. tier: (rated rpm of the shaft, N m it takes off it), the data of "Drive in".
DRILL_SHAFT = {1: (1800, 40), 2: (1600, 80), 3: (1400, 160), 4: (1200, 320), 5: (1000, 640)}
DRILL_SOCKETS = [
    {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
    {"name": "+x", "at": [0.25, 0.25, 0], "dir": "+x"},
    {"name": "-x", "at": [-0.25, 0.25, 0], "dir": "-x"},
    {"name": "back", "at": [0, 0.25, -0.25], "dir": "-z"},
    {"name": "drive", "at": [0, 1, 0.75], "dir": "+y"},
]

DRILL = dict(
    file="Gathering/DrillModule", name="Drill Module", section="Drills", mass=200, health=400, armour=6, power=900, attach=4.0,
    sockets=DRILL_SOCKETS, bulk=dict(min=[-0.155, 0.465, -0.155], max=[0.155, 0.615, 0.155]), capacity=20,
    description="Drills a vein: needs power or a shaft, and a chute out.",
    gather=dict(kind="drill", rate=10, min=[-0.1, -1.2, 0.55], max=[0.1, -0.6, 0.8]),
    ports=[
        {"name": "Chute out", "type": "chute", "dir": "out", "at": [0.19, 0.54, 0], "face": "+x"},
        {"name": "Drive in", "type": "shaft", "dir": "in", "socket": "drive", "at": [0, 1, 0.68], "face": "+y"},
    ],
)

SAW = dict(
    file="Gathering/SawModule", name="Saw Module", section="Saws", mass=260, health=400, armour=6, power=900, attach=4.0,
    sockets=MODULE_SOCKETS, bulk=MODULE_BIN, capacity=400,
    description="Cuts trees: needs power. Wood lands in its bin.",
    gather=dict(kind="saw", rate=12, min=[-0.7, 0.1, 0.55], max=[0.7, 1.5, 2.2]),
)

# The big machines: multi-metre buildings at every tier, tier 1 included (docs/sandbox/CONSTRUCTS.md, "Machine tiers"). Their
# boxes are in units of the machine's width (x and z run -0.5 to 0.5), a tier's SIZES[tier] metres; sockets stay on the grid
# because every width is a multiple of 0.5 m. Each is stepped, not a brick: a thin floor plate, a body at the back, and
# an aisle across the machine (the "aisle" tuple, along x) that a player walks through. Mass and health go by width squared.
BIG_WIDTH = {
    "Refining/Smelter": {1: 3.5, 2: 4.5, 3: 6.0, 4: 8.0, 5: 10.0},
    "Refining/Sawmill": {1: 3.0, 2: 4.0, 3: 5.0, 4: 7.0, 5: 9.0},
    "Refining/Foundry": {1: 4.0, 2: 5.0, 3: 6.5, 4: 8.0, 5: 10.0},
    "Assembling/Assembler": {1: 2.0, 2: 2.5, 3: 3.5, 4: 4.5, 5: 6.0},
}


def chute_ports(in_at, in_face, out_at, out_face):
    return [
        {"name": "Chute in", "type": "chute", "dir": "in", "at": list(in_at), "face": in_face},
        {"name": "Chute out", "type": "chute", "dir": "out", "at": list(out_at), "face": out_face},
    ]


SAWMILL = dict(
    file="Refining/Sawmill", name="Sawmill", section="Sawmills", mass=240, health=500, armour=5, power=1800, attach=8.0,
    sockets=[
        {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
        {"name": "+x", "at": [0.5, 0.25, -0.3], "dir": "+x"},
        {"name": "-x", "at": [-0.5, 0.25, -0.3], "dir": "-x"},
        {"name": "back", "at": [0, 0.25, -0.5], "dir": "-z"},
    ],
    ports=chute_ports((0.5, 0.25, -0.33), "+x", (0.5, 0.25, 0.33), "+x"),
    bulk=dict(min=[-0.435, 0.03, 0.165], max=[0.435, 0.44, 0.435]), capacity=400,
    description="Saws wood into planks, planks into cloth. Needs power.",
    refine=dict(machine="sawmill", capacity=200, min=[-0.435, 0.03, -0.435], max=[0.435, 0.44, -0.165]),
)

SMELTER = dict(
    file="Refining/Smelter", name="Smelter", section="Smelters", mass=420, health=800, armour=10, power=3000, attach=9.0,
    sockets=[
        {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
        {"name": "+x", "at": [0.5, 0.25, -0.25], "dir": "+x"},
        {"name": "-x", "at": [-0.5, 0.25, -0.25], "dir": "-x"},
        {"name": "back", "at": [0, 0.25, -0.5], "dir": "-z"},
    ],
    ports=chute_ports((0.5, 0.4, -0.25), "+x", (0.36, 0.12, 0.38), "+x"),
    bulk=dict(min=[-0.335, 0.025, 0.305], max=[0.335, 0.22, 0.455]), capacity=400,
    description="Smelts ore to metal, copper and gold, sand to glass. Needs power.",
    refine=dict(machine="smelter", capacity=200, min=[-0.295, 0.805, -0.395], max=[0.055, 0.98, -0.155]),
)

FOUNDRY = dict(
    file="Refining/Foundry", name="Foundry", section="Foundries", mass=480, health=900, armour=10, power=3500, attach=10.0,
    sockets=[
        {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
        {"name": "+x", "at": [0.5, 0.25, -0.25], "dir": "+x"},
        {"name": "-x", "at": [-0.5, 0.25, -0.25], "dir": "-x"},
        {"name": "back", "at": [0, 0.25, -0.5], "dir": "-z"},
    ],
    ports=chute_ports((0.5, 0.4, -0.25), "+x", (0.38, 0.12, 0.38), "+x"),
    bulk=dict(min=[-0.355, 0.025, 0.305], max=[0.355, 0.22, 0.455]), capacity=400,
    description="Mixes two materials into one: a chute for each. Needs power.",
    refine=dict(machine="foundry", capacity=200, min=[-0.375, 0.725, -0.395], max=[-0.055, 0.88, -0.155]),
)

ASSEMBLER = dict(
    file="Assembling/Assembler", name="Assembler", section="Assemblers", mass=150, health=500, armour=6, power=1500, attach=6.0,
    sockets=[
        {"name": "bottom", "at": [0, 0, 0], "dir": "-y"},
        {"name": "+x", "at": [0.5, 0.25, -0.25], "dir": "+x"},
        {"name": "-x", "at": [-0.5, 0.25, -0.25], "dir": "-x"},
        {"name": "back", "at": [0, 0.25, -0.5], "dir": "-z"},
    ],
    ports=chute_ports((0.5, 0.25, -0.3), "+x", (0.5, 0.25, 0.3), "+x"),
    slots=[
        dict(min=[-0.455, 0.035, 0.015], max=[-0.01, 0.56, 0.48], stack=12, label="tray"),
        dict(min=[0.01, 0.035, 0.015], max=[0.455, 0.56, 0.48], stack=12, label="tray"),
    ],
    description="Builds a recipe set with the configure tool. Needs power.",
    assemble=dict(capacity=200, min=[-0.435, 0.035, -0.435], max=[0.435, 0.32, -0.065], outAt=[0, 0.03, 0.5], outDir="+z"),
    config=[{"key": "recipe", "label": "Recipe", "group": "Production", "tooltip": "What it builds."}],
)

# What a low tier can't work yet (Constructs/Resources.json and Refining.json: "minTier").
DRILL["gated"] = {1: "Copper needs tier 2 and gold tier 3.", 2: "Gold needs tier 3."}
SMELTER["gated"] = {1: "Copper ore needs tier 2 and gold ore tier 3.", 2: "Gold ore needs tier 3."}
FOUNDRY["gated"] = {1: "Bronze needs tier 2 and circuits tier 3.", 2: "Circuits need tier 3."}

LINES = [DRILL, SAW, SAWMILL, SMELTER, FOUNDRY, ASSEMBLER]


def num(v):
    v = round(v + 0.0, 4)
    return int(v) if float(v).is_integer() else v


def vec(v):
    return [num(c) for c in v]


def scaled_point(v, size):
    """A point at a tier: x and z grow; y grows above the floor, and keeps tier 1's value below it (a bit's reach)."""
    return [v[0] * size, v[1] * size if v[1] > 0 else v[1], v[2] * size]


def on_grid(v):
    return int(v / GRID + (0.5 if v >= 0 else -0.5)) * GRID


def out_to_grid(v):
    """The next grid line out from a face."""
    n = abs(v) / GRID
    whole = int(n)
    return (whole if n - whole < 1e-6 else whole + 1) * GRID * (1 if v >= 0 else -1)


def scaled_sockets(line, tier):
    """The sockets at a tier, on the grid, and the solid pads that hold any the body doesn't reach."""
    size = size_of(line, tier)
    if size == 1.0:
        return line["sockets"], []
    out, pads = [], []
    for sk in line["sockets"]:
        at = scaled_point(sk["at"], size)
        axis = "xyz".index(sk["dir"][1])
        if sk["name"] == "bottom":
            out.append(dict(sk, at=vec(at)))
            continue
        face = at[axis]
        snapped = [on_grid(c) for c in at]
        snapped[axis] = out_to_grid(face)
        out.append(dict(sk, at=vec(snapped)))
        if abs(snapped[axis] - face) > 1e-6:
            lo, hi = [c - 0.15 for c in snapped], [c + 0.15 for c in snapped]
            if sk["dir"][0] == "+":
                lo[axis] = face - 0.03
                hi[axis] = snapped[axis]
            else:
                lo[axis] = snapped[axis]
                hi[axis] = face + 0.03
            pads.append(box(lo, hi, "frame"))
    return out, pads


def size_of(line, tier):
    """A tier's linear size: the modules' multiplier on tier 1, or a big machine's width in metres."""
    return BIG_WIDTH[line["file"]][tier] if line["file"] in BIG_WIDTH else SIZE[tier]


def built_box(tier, b, index, detail=False):
    role = b["role"]
    out = {"min": vec(b["min"]), "max": vec(b["max"])}
    colour = tier_style.PALETTE[tier][role]
    if tier == 1 and not detail:
        colour = tier_style.scrap_tone(colour, index)
    if colour != tier_style.PALETTE[tier]["body"]:
        out["color"] = colour
    if detail:
        out["detail"] = True
    if role in tier_style.GLOWS.get(tier, ()):
        out["glow"] = True
        out["paintable"] = False
    for k in tier_style.SHAPE_KEYS:
        if k in b:
            out[k] = b[k]
    return out


def part(line, tier):
    rate, power, cap, mass, health, armour, attach = CURVE[tier]
    size = size_of(line, tier)
    big = line["file"] in BIG_WIDTH
    sockets, pads = scaled_sockets(line, tier)
    if big:
        mass = health = size * size

    def grown(b):
        return {**b, "min": scaled_point(b["min"], size), "max": scaled_point(b["max"], size)}

    shape = tier_style.look(line["name"], tier)
    boxes, drawn = [], []
    for i, b in enumerate(shape["solid"]):
        solid = built_box(tier, grown(b), i)
        if "look" in b:  # the box stays, hidden, as the collider: the shape is drawn over it
            drawn.append(built_box(tier, grown({**b, **b["look"]}), i, True))
        if "look" in b or b.get("hidden"):
            solid.update(hidden=True)
        boxes.append(solid)
    boxes += [built_box(tier, b, 0) for b in pads]
    boxes += drawn + [built_box(tier, grown(b), 0, True) for b in shape["detail"]]
    panel = None
    if big:  # (a building has a screen; a gathering module's state is its look line, and it works out of sight on an arm)
        keep_clear = [(s["at"], s["dir"]) for s in sockets] + [(scaled_point(p["at"], size), p["face"]) for p in line.get("ports", [])]
        if "assemble" in line:
            keep_clear.append((scaled_point(line["assemble"]["outAt"], size), line["assemble"]["outDir"]))
        plate, panel = screens.mount(boxes, keep_clear)
        screens.fit(boxes, plate)
    zfight.resolve(boxes, sockets)
    boxes = [{**b, "min": vec(b["min"]), "max": vec(b["max"])} for b in boxes]
    head = f"{TIERS[tier]}, tier {tier} of 5. "
    if "gated" in line and tier < 3:
        head += line["gated"][tier] + " "
    out = {
        "format": FORMAT,
        "name": PREFIX[tier] + line["name"],
        "description": head + line["description"].replace("the Sawmill", "the mill").replace("the Smelter", "the furnace"),
        "group": GROUP,
        "section": line["section"],
        "tier": tier,
        "role": "storage",
        "tool": "welder",
        "attachSeconds": num(line["attach"] * attach),
        "mass": num(line["mass"] * mass),
        "health": num(line["health"] * health),
        "armour": num(line["armour"] * armour),
        "material": "steel",
        "power": num(line["power"] * power),
        "collider": "boxes",  # (the details are looks: a wedge or a cylinder among them must never become a hull)
        "color": tier_style.PALETTE[tier]["body"],
        "boxes": boxes,
        "sockets": sockets,
    }
    if "ports" in line:
        out["ports"] = [dict(p, at=vec(scaled_point(p["at"], size))) for p in line["ports"]]
    if panel:
        out["panels"] = [panel]
        out["config"] = line.get("config", []) + [INK]
    elif "config" in line:
        out["config"] = line["config"]

    def fill(b):
        return dict(min=vec(scaled_point(b["min"], size)), max=vec(scaled_point(b["max"], size)))

    if "assemble" in line:
        a = line["assemble"]
        slots = [{"min": fill(s)["min"], "max": fill(s)["max"], "stack": s["stack"], "label": s["label"]} for s in line["slots"]]
        block = {"capacity": num(a["capacity"] * cap), "min": fill(a)["min"], "max": fill(a)["max"]}
        if tier > 1:
            block["speed"] = rate
            block["chuteLoad"] = 10 * rate
        block.update(batches=4 * tier, outAt=vec(scaled_point(a["outAt"], size)), outDir=a["outDir"])
        out["storage"] = {"slots": slots, "assemble": block}
        return out
    bulk = {"material": "any", "capacity": num(line["capacity"] * cap)}
    if tier > 1:
        bulk["chuteLoad"] = 10 * rate
    storage = {"bulk": {**bulk, **fill(line["bulk"])}}
    if "gather" in line:
        g = line["gather"]
        storage["gather"] = {"kind": g["kind"], "rate": g["rate"] * rate, **fill(g)}
        if line is DRILL:
            storage["gather"]["ratedRpm"], storage["gather"]["shaftTorque"] = DRILL_SHAFT[tier]
    else:
        f = line["refine"]
        if "hopper" in shape:  # (the body moved it: the same room, higher or lower)
            f = dict(f, min=[f["min"][0], shape["hopper"][0], f["min"][2]], max=[f["max"][0], shape["hopper"][1], f["max"][2]])
        storage["refine"] = {"machine": f["machine"], "capacity": num(f["capacity"] * cap)}
        if tier > 1:
            storage["refine"]["speed"] = rate
        storage["refine"].update(fill(f))
    if "ports" in line:
        out["ports"] = [dict(p, at=vec(scaled_point(p["at"], size))) for p in line["ports"]]
    out["storage"] = storage
    return out


def one(v):
    return json.dumps(v, ensure_ascii=True)


def dump(p):
    """One key a line, one box, socket and bin a line."""
    lines = []
    for k, v in p.items():
        if k in ("boxes", "sockets", "ports", "config"):
            lines.append(f'  "{k}": [\n' + ",\n".join("    " + one(i) for i in v) + "\n  ]")
        elif k == "storage":
            lines.append('  "storage": {\n' + ",\n".join(f'    "{n}": {one(b)}' for n, b in v.items()) + "\n  }")
        else:
            lines.append(f'  "{k}": {one(v)}')
    return "{\n" + ",\n".join(lines) + "\n}\n"


def path(line, tier):
    return os.path.join("Constructs", "Parts", line["file"] + (f"T{tier}" if tier > 1 else "") + ".json")


def main():
    check = "--check" in sys.argv
    bad = 0
    for line in LINES:
        for tier in range(1, 6):
            p = part(line, tier)
            left = zfight.hits(p["boxes"])
            if left:
                print(f"z-fight left in {p['name']}: {left}")
                bad += 1
            thin = [b for b in p["boxes"] if min(hi - lo for lo, hi in zip(b["min"], b["max"])) < 0.0105]
            if thin:
                print(f"thinner than 1 cm in {p['name']}: {thin}")
                bad += 1
            os.makedirs(os.path.dirname(path(line, tier)), exist_ok=True)
            if len(p["boxes"]) > 32:
                print(f"too many boxes: {p['name']}")
                bad += 1
            text, file = dump(p), path(line, tier)
            if check:
                if not os.path.exists(file) or open(file, encoding="utf-8").read() != text:
                    print(f"out of date: {file}")
                    bad += 1
            else:
                with open(file, "w", encoding="utf-8", newline="\n") as f:
                    f.write(text)
    print("machine tiers: " + ("checked" if check else "written") + f", {bad} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
