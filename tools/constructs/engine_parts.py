#!/usr/bin/env python3
"""Writes the Sandbox's modular engine pieces (Constructs/Parts/ModularEngine) and their rules (Constructs/ModularEngine.json).

docs/sandbox/CONSTRUCTS.md, "Modular engines". Ten kinds of piece in four sizes; the look is exposed and industrial (the
traction city reference): open crank frames, columns round a visible pumping piston, a spoked flywheel, tall stacks.
Boxes tagged "pump" (metres of stroke) slide up and down with the engine, boxes tagged "spin" turn about the part's z.

SIZES is the one table of scale numbers: linear size x1, x4, x16, x64 (Small, Medium, Large, Capital), with each size's
mass and toughness multipliers and the rules' numbers (power a cylinder, rpm). Run it from the repo root:
python3 -I tools/constructs/engine_parts.py (it rewrites the folder and the rules; --check only compares). Data first:
the JSON files are what the game loads; this script only keeps the families consistent.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zfight  # noqa: E402

OUT = os.path.join("Constructs", "Parts", "ModularEngine")
RULES = os.path.join("Constructs", "ModularEngine.json")
GROUP = "Modular engines"

# name, linear scale, mass x, health x, cylinder kW, idle / peak / limiter rpm, attach seconds x, needs a starter
SIZES = [
    ("Small", 1, 1, 1, 40, 900, 5000, 7000, 1.0, False),
    ("Medium", 4, 20, 4, 800, 600, 2800, 4000, 1.5, False),
    ("Large", 16, 400, 16, 16000, 300, 1400, 2000, 2.5, True),
    ("Capital", 64, 8000, 64, 320000, 150, 700, 1000, 4.0, True),
]

BURN_PER_KW = 0.00044  # litres a second a kW at full throttle and the limiter (the stock Engine's 0.05 l/s at 114 kW)
MISFIRE_BELOW = 0.5  # a cylinder under this share of its health misfires...
MISFIRE_POWER = 0.5  # ...and gives this share of its power
THIRSTY_AT = 1.15  # a burn this many times the plain carburettor's reads "thirsty"
FRUGAL_AT = 0.85  # and this much reads "frugal"

IRON = "#3b3936"
DARK = "#262524"
STEEL = "#8d9096"
RUST = "#7c4a2e"
COPPER = "#a8623a"
BRASS = "#b08d3c"
SOOT = "#1d1b1a"
HOT = "#c9652a"


def r(v):
    v = round(v + 0.0, 4)
    return 0.0 if v == 0 else v


def box(mn, mx, color, detail=False, **extra):
    b = {"min": list(mn), "max": list(mx), "color": color}
    if detail:
        b["detail"] = True
    b.update(extra)
    return b


def at(c, size, color, detail=False, **extra):
    h = [s / 2 for s in size]
    return box([c[k] - h[k] for k in range(3)], [c[k] + h[k] for k in range(3)], color, detail, **extra)


def sock(name, pos, d):
    return {"name": name, "at": list(pos), "dir": d}


def crank():
    """An open bearing frame: end plates, rails and a pan, with the turning shaft, webs and pin in plain view."""
    b = [
        at((0, 0, 0.222), (0.5, 0.5, 0.056), IRON),
        at((0, 0, -0.222), (0.5, 0.5, 0.056), IRON),
        at((0, -0.2, 0), (0.44, 0.1, 0.386), DARK),
    ]
    for x in (-0.205, 0.205):
        b.append(at((x, 0.205, 0), (0.07, 0.07, 0.386), IRON))
    b += [
        at((0, 0, 0), (0.1, 0.1, 0.49), STEEL, True, spin=True),
        at((0, 0.06, 0.09), (0.15, 0.24, 0.05), BRASS, True, spin=True),
        at((0, 0.06, -0.09), (0.15, 0.24, 0.05), BRASS, True, spin=True),
        at((0, 0.12, 0), (0.08, 0.08, 0.13), STEEL, True, spin=True),
    ]
    s = [
        sock("front", (0, 0, 0.25), "+z"),
        sock("back", (0, 0, -0.25), "-z"),
        sock("+y", (0, 0.25, 0), "+y"),
        sock("-y", (0, -0.25, 0), "-y"),
        sock("+x", (0.25, 0, 0), "+x"),
        sock("-x", (-0.25, 0, 0), "-x"),
    ]
    return b, s, [], {"kind": "crank"}, 30, 220, 8


def cylinder():
    """Four columns between a base and a head, the piston and its rod pumping in the open between them."""
    b = [
        at((0, -0.345, 0), (0.44, 0.06, 0.44), IRON),
        at((0, 0.325, 0), (0.46, 0.1, 0.46), RUST),
    ]
    for x in (-0.185, 0.185):
        for z in (-0.185, 0.185):
            b.append(at((x, -0.01, z), (0.06, 0.61, 0.06), IRON))
    b += [
        at((0, -0.27, 0), (0.33, 0.09, 0.33), COPPER, True),
        at((0, 0.03, 0), (0.3, 0.12, 0.3), STEEL, True, pump=0.16),
        at((0, -0.15, 0), (0.08, 0.31, 0.08), STEEL, True, pump=0.08),
        at((0, 0.385, 0), (0.3, 0.03, 0.3), COPPER, True),
        at((0.15, 0.39, 0.15), (0.05, 0.045, 0.05), BRASS, True),
        at((-0.15, 0.39, 0.15), (0.05, 0.045, 0.05), BRASS, True),
        at((0.15, 0.39, -0.15), (0.05, 0.045, 0.05), BRASS, True),
        at((-0.15, 0.39, -0.15), (0.05, 0.045, 0.05), BRASS, True),
    ]
    s = [
        sock("base", (0, -0.375, 0), "-y"),
        sock("head", (0, 0.375, 0), "+y"),
        sock("+x", (0.23, 0.325, 0), "+x"),
        sock("-x", (-0.23, 0.325, 0), "-x"),
    ]
    return b, s, [], {"kind": "cylinder"}, 28, 180, 6


def top_base():
    return [sock("base", (0, -0.125, 0), "-y"), sock("top", (0, 0.125, 0), "+y")]


FUEL_PORT = {"name": "Fuel in", "type": "pipe", "dir": "in", "fluid": "fuel", "at": [0.2, 0.0, 0.0], "face": "+x"}


def carburettor():
    b = [
        at((0, -0.075, 0), (0.4, 0.1, 0.4), BRASS),
        at((0, 0.03, 0), (0.26, 0.19, 0.26), COPPER),
        at((0, 0.1, 0), (0.33, 0.04, 0.33), STEEL, True),
        at((0.15, 0.0, 0), (0.1, 0.1, 0.1), BRASS, True),
    ]
    return b, top_base(), [FUEL_PORT], {"kind": "fuel", "power": 1.0, "thirst": 1.0}, 8, 90, 2


def injector_rail():
    b = [
        at((0, -0.08, 0), (0.42, 0.09, 0.42), IRON),
        at((0, 0.03, 0), (0.12, 0.13, 0.42), COPPER),
    ]
    for z in (-0.14, 0.0, 0.14):
        b.append(at((0.1, 0.0, z), (0.08, 0.1, 0.05), BRASS, True))
        b.append(at((-0.1, 0.0, z), (0.08, 0.1, 0.05), BRASS, True))
    return b, top_base(), [FUEL_PORT], {"kind": "fuel", "power": 1.2, "thirst": 0.8}, 6, 80, 2


def turbo():
    b = [
        at((0, -0.08, 0), (0.4, 0.09, 0.4), IRON),
        at((0, 0.03, 0), (0.3, 0.13, 0.3), RUST),
        at((-0.12, 0.03, 0), (0.1, 0.17, 0.2), HOT, True, shape="cylinder"),
        at((0.12, 0.03, 0), (0.1, 0.2, 0.24), STEEL, True, shape="cylinder"),
    ]
    return b, top_base(), [], {"kind": "boost", "power": 1.4, "thirst": 1.15, "spool": 1.5}, 14, 110, 3


def supercharger():
    b = [
        at((0, -0.08, 0), (0.4, 0.09, 0.4), IRON),
        at((0, 0.03, 0), (0.32, 0.13, 0.34), STEEL),
    ]
    for x in (-0.09, 0.0, 0.09):
        b.append(at((x, 0.1, 0), (0.05, 0.03, 0.3), RUST, True))
    return b, top_base(), [], {"kind": "boost", "power": 1.25, "thirst": 1.25}, 18, 120, 3


def flywheel():
    """A big spoked wheel, turning, between the crank's end and the shaft that leaves it."""
    b = [
        at((0, 0, -0.17), (0.5, 0.5, 0.06), IRON),
        at((0, 0, 0.0), (0.16, 0.16, 0.3), STEEL),
        at((0, 0, 0.17), (0.24, 0.24, 0.06), IRON),
        at((0, 0, 0.0), (0.9, 0.9, 0.09), IRON, True, shape="tube", inner=0.82, segments=24, spin=True),
        at((0, 0, 0.0), (0.82, 0.08, 0.05), STEEL, True, spin=True),
        at((0, 0, 0.0), (0.08, 0.82, 0.05), STEEL, True, spin=True),
    ]
    s = [
        sock("crank", (0, 0, -0.2), "-z"),
        sock("+z", (0, 0, 0.2), "+z"),
        sock("+y", (0, 0.25, -0.17), "+y"),
        sock("-y", (0, -0.25, -0.17), "-y"),
        sock("+x", (0.25, 0, -0.17), "+x"),
        sock("-x", (-0.25, 0, -0.17), "-x"),
    ]
    ports = [
        {"name": "Drive out", "type": "shaft", "dir": "out", "socket": "+z"},
        {"name": "Fuel in", "type": "pipe", "dir": "in", "fluid": "fuel", "at": [0.25, 0.1, -0.17], "face": "+x"},
    ]
    return b, s, ports, None, 35, 260, 8


def starter():
    b = [
        at((0, -0.1, 0), (0.3, 0.1, 0.3), IRON),
        at((0, 0.05, 0), (0.22, 0.2, 0.22), COPPER),
        at((0, 0.16, 0), (0.12, 0.03, 0.12), BRASS, True),
    ]
    return b, [sock("base", (0, -0.15, 0), "-y")], [], {"kind": "starter"}, 12, 90, 2


def exhaust():
    b = [
        at((0, -0.08, 0), (0.4, 0.09, 0.4), DARK),
        at((0, 0.035, 0), (0.28, 0.14, 0.28), RUST),
        at((0.17, 0.03, 0), (0.07, 0.12, 0.12), SOOT, True),
    ]
    ports = [{"name": "Exhaust out", "type": "pipe", "dir": "out", "fluid": "exhaust", "at": [0.2, 0.03, 0.0], "face": "+x"}]
    return b, top_base(), ports, {"kind": "exhaust"}, 10, 100, 2


def stack():
    """One 1.5 m length of smokestack, its axis along z (the shapes' axis): stack them end to end; the smoke leaves the top one."""
    b = [
        at((0, 0, 0), (0.3, 0.3, 1.46), SOOT, False, hidden=True),
        at((0, 0, 0), (0.3, 0.3, 1.46), RUST, True, shape="cylinder", segments=12),
        at((0, 0, -0.72), (0.36, 0.36, 0.06), IRON, True, shape="cylinder", segments=12),
        at((0, 0, 0.72), (0.36, 0.36, 0.06), IRON, True, shape="cylinder", segments=12),
        at((0, 0, 0.1), (0.33, 0.33, 0.04), SOOT, True, shape="cylinder", segments=12),
    ]
    s = [
        sock("base", (0, 0, -0.75), "-z"),
        sock("top", (0, 0, 0.75), "+z"),
        sock("+x", (0.15, 0, 0), "+x"),
        sock("-x", (-0.15, 0, 0), "-x"),
        sock("+y", (0, 0.15, 0), "+y"),
        sock("-y", (0, -0.15, 0), "-y"),
    ]
    ports = [{"name": "Exhaust in", "type": "pipe", "dir": "in", "fluid": "exhaust", "at": [0.15, 0.0, -0.5], "face": "+x"}]
    return b, s, ports, {"kind": "stack"}, 20, 120, 2


# file stem, name, what it is, model
KINDS = [
    ("Crank", "Crank Section", "Snap sections end to end: the crank's length is the engine's. Cylinders go on its four sides.", crank),
    ("Cylinder", "Cylinder", "Goes on a crank section's side. Each one adds power; the sides you use make it inline, V, boxer or radial.", cylinder),
    ("Carburettor", "Carburettor", "Fuel system: one on any cylinder feeds its whole bank. Simple, full power.", carburettor),
    ("InjectorRail", "Injector Rail", "Fuel system: one on any cylinder feeds its whole bank. 20% more power, a fifth less fuel.", injector_rail),
    ("Turbo", "Turbo", "Bolts on a fuel system or a head: 40% more power on that bank, a moment's lag, a bit thirstier.", turbo),
    ("Supercharger", "Supercharger", "Bolts on a fuel system or a head: 25% more power on that bank at once, thirsty.", supercharger),
    ("Flywheel", "Flywheel", "The engine's output: goes on the end of the crank. A gearbox or a shaft takes the drive from it. Wire its run and throttle like an engine.", flywheel),
    ("Starter", "Starter Motor", "Large and Capital engines need one, on a crank's side or the flywheel, and a battery to turn it.", starter),
    ("Exhaust", "Exhaust Outlet", "Bolts on a head or a fuel system. A smokestack stands on top of it, or takes a pipe from it. Black smoke under load.", exhaust),
    ("Smokestack", "Smokestack", "1.5 m of stack: stand it on an exhaust outlet, stack more on top, or pipe an outlet into it.", stack),
]


def scaled(v, s):
    return [r(x * s) for x in v]


def build(size, stem, name, what, model):
    title, s, mass_x, hp_x, _kw, _idle, _peak, _max, secs_x, _start = size
    boxes, sockets, ports, piece, mass, health, armour = model()
    for b in boxes:
        b["min"] = scaled(b["min"], s)
        b["max"] = scaled(b["max"], s)
        if "pump" in b:
            b["pump"] = r(b["pump"] * s)
    socks = [{"name": k["name"], "at": scaled(k["at"], s), "dir": k["dir"]} for k in sockets]
    zfight.resolve(boxes, socks)
    part = {
        "format": "call-of-slop-construct-part/1",
        "name": f"{title} {name}",
        "description": what,
        "group": GROUP,
        "section": title,
        "tool": "wrench",
        "attachSeconds": round(1.0 * secs_x, 2),
        "mass": mass * mass_x,
        "health": health * hp_x,
        "armour": armour * (1 + (hp_x - 1) // 8),
        "material": "steel",
        "color": IRON,
        "centreSockets": False,
        "boxes": boxes,
        "sockets": socks,
    }
    if ports:
        part["ports"] = [dict(p, **({"at": scaled(p["at"], s)} if "at" in p else {})) for p in ports]
    if piece is None:
        part["role"] = "engine"
        part["engine"] = {"modular": title.lower()}
    else:
        part["role"] = "enginePiece"
        part["enginePiece"] = dict(piece, size=title.lower())
    return part


def rules():
    return {
        "format": "call-of-slop-modular-engine/1",
        "about": "Written by tools/constructs/engine_parts.py. An engine's peak power is the sum over its cylinders of kW x the bank's fuel system x its booster.",
        "burnPerKw": BURN_PER_KW,
        "misfireBelow": MISFIRE_BELOW,
        "misfirePower": MISFIRE_POWER,
        "thirstyAt": THIRSTY_AT,
        "frugalAt": FRUGAL_AT,
        "sizes": [
            {"name": t.lower(), "scale": s, "kw": kw, "idleRpm": idle, "peakRpm": peak, "maxRpm": top, "starter": start}
            for t, s, _m, _h, kw, idle, peak, top, _a, start in SIZES
        ],
    }


def main():
    check = "--check" in sys.argv
    files = {RULES: rules()}
    for size in SIZES:
        for stem, name, what, model in KINDS:
            files[os.path.join(OUT, f"{size[0]}{stem}.json")] = build(size, stem, name, what, model)
    bad = 0
    if not check:
        os.makedirs(OUT, exist_ok=True)
    for path, data in files.items():
        text = json.dumps(data, indent=2) + "\n"
        if check:
            if not os.path.exists(path) or open(path, encoding="utf-8").read() != text:
                print("out of date:", path)
                bad += 1
        else:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
    print(f"{len(files)} files" + (f", {bad} out of date" if check else " written"))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
