#!/usr/bin/env python3
"""Writes the Sandbox's actuator size families (Constructs/Parts/Actuators): pistons, motor hinges and turntables.

Board item dev-sandbox-actuator-sizes-as01; docs/sandbox/CONSTRUCTS.md, "Actuator sizes and strength". Each family comes
in three widths (1x1, 2x2 and 3x3 cells of the 0.25 m frame step) and three lengths (short, medium, long):
- a piston's width sets its force and how much side load it takes, its length its stroke (and so its top speed);
- a motor hinge's or turntable's width sets its torque, its length its speed (like a gear ratio: a longer housing holds
  a bigger motor, so it turns faster);
- mass, health, armour, power and fitting time grow with the size.

The rating (the most force or torque it holds rigidly) is the top of its Strength setting, and the top of its Speed
setting is the size's top speed; both start from a tenth of that. Above its rating a joint gives way (non-linearly,
"give" is how far it has given at twice the rating) and recovers at its speed when the load drops
(ConstructPhysics.cs, "the strength model"). "overloadDamage" (hp a second at twice the rating) stays 0: sustained
overload doesn't hurt the part unless the developer turns it on.

The three parts that were there before (Piston, Motor Hinge, Turntable) are one size each (2x2 long, 1x1 medium, 2x2
short): they keep their names, shapes, sockets and default settings, so existing blueprints and constructs still fit.

Run it from the repo root: python3 -I tools/constructs/actuator_parts.py (it rewrites the folder; --check only
compares). Data first: the JSON files are what the game loads; this script only keeps the families consistent.
"""

import json
import os
import sys

C = 0.25  # the frame step (one cell)
OUT = os.path.join("Constructs", "Parts", "Actuators")
LENGTHS = {1: "Short", 2: "Medium", 3: "Long"}
GOLD = "#c7a23a"


def r(v):
    v = round(v + 0.0, 4)
    return 0.0 if v == 0 else v


def vec(*v):
    return [r(x) for x in v]


def sock(name, at, d):
    return {"name": name, "at": vec(*at), "dir": d}


def sides(hx, hz, y=0.0):
    return [sock("+x", (hx, y, 0), "+x"), sock("-x", (-hx, y, 0), "-x"), sock("+z", (0, y, hz), "+z"), sock("-z", (0, y, -hz), "-z")]


def num(key, label, group, unit, lo, hi, step, tip, default=None):
    e = {"key": key, "label": label, "group": group, "unit": unit, "min": r(lo), "max": r(hi), "step": step, "tooltip": tip}
    if default is not None:
        e["default"] = r(default)
    return e


def size_word(w, l):
    return f"{w}x{w} {LENGTHS[l].lower()}"


# ---- Pistons -----------------------------------------------------------------------------------------------------

PISTON_FORCE = {1: 6000, 2: 24000, 3: 60000}  # N: the rating (the top of its Force setting)
PISTON_SIDE = {1: 2000, 2: 8000, 3: 20000}  # N across its axis before it gives
PISTON_STROKE = {1: 0.5, 2: 1.0, 3: 2.0}  # m


def piston(w, l, legacy=False):
    k = w * w * (l + 1)
    stroke = PISTON_STROKE[l]
    force = PISTON_FORCE[w]
    top = stroke * 1.0  # m/s at the top of its Speed setting
    if legacy:
        name, s, h = "Piston", 0.4, 0.3
        speed, strength = 0.8, 14000
    else:
        name, s, h = f"Piston {w}x{w} {LENGTHS[l]}", C * w, C * l
        speed, strength = 0.4 * stroke, force
    d = {
        "format": "call-of-slop-construct-part/1",
        "name": name,
        "description": f"Pushes what sits on its top out along it, up to {stroke:g} m: a signal of 1 extends it fully, 0 pulls it in. "
        f"Interact works it when nothing is wired in. Size {size_word(w, l)}: holds up to {force} N rigidly ({PISTON_SIDE[w]} N across it); more and it gives way.",
        "group": "Joints",
        "section": "Pistons",
        "role": "joint",
        "tool": "wrench",
        "attachSeconds": 1.5 + 0.5 * w,
        "mass": r(2.5 * k),
        "health": 100 + 10 * k,
        "armour": 4 + w,
        "material": "steel",
        "color": "#6d747c",
        "power": r(10 * k / 16),
        "boxes": [
            {"size": vec(s, h, s)},
            {"size": vec(s + 0.02, 0.06, s + 0.02), "at": vec(0, h / 2 - 0.027, 0), "color": GOLD, "detail": True},  # 3 mm proud of the body top (no coplanar faces)
        ],
        "sockets": [sock("base", (0, -h / 2, 0), "-y"), sock("top", (0, h / 2, 0), "+y")] + sides(s / 2, s / 2),
        "joint": {
            "type": "piston", "axis": "+y", "min": 0, "max": stroke, "leaf": "top", "speed": r(speed), "strength": strength,
            "give": r(0.25 * stroke), "sideLoad": PISTON_SIDE[w], "overloadDamage": 0,
        },
        "config": [
            num("min", "Pulled in to", "Travel", "m", 0, stroke, 0.01, "How far out it rests when pulled in."),
            num("max", "Pushed out to", "Travel", "m", 0, stroke, 0.01, "How far out it goes when pushed out (a signal of 1)."),
            num("speed", "Speed", "Motor", "m/s", 0.05, top, 0.05, f"How fast it slides (this size: up to {top:g} m/s)."),
            num("strength", "Force", "Motor", "N", force / 10, force, 100,
                f"Its rating: what it holds rigidly (this size: up to {force} N). More load and it gives way; a lower setting caps the power it draws."),
        ],
    }
    return d


# ---- Motor hinges ------------------------------------------------------------------------------------------------

HINGE_TORQUE = {1: 4000, 2: 12000, 3: 30000}  # N m
TURN_SPEED = {1: 45, 2: 90, 3: 180}  # deg/s at the top of its Speed setting (hinges and turntables)


def motor_hinge(w, l, legacy=False):
    k = w * w * (l + 1)
    torque = HINGE_TORQUE[w]
    top = TURN_SPEED[l]
    if legacy:
        name, s, h = "Motor Hinge", 0.2, 0.5
        speed, strength = 90, 3000
    else:
        name, s, h = f"Motor Hinge {w}x{w} {LENGTHS[l]}", C * w, C * l
        speed, strength = top, torque
    d = {
        "format": "call-of-slop-construct-part/1",
        "name": name,
        "description": f"A motorised hinge: wire a signal into it (1 opens to max, 0 shuts, -1 to min), or Interact opens and shuts it. "
        f"Size {size_word(w, l)}: holds up to {torque} N m rigidly; more and it gives way.",
        "group": "Joints",
        "section": "Motor hinges",
        "role": "joint",
        "tool": "wrench",
        "attachSeconds": 1.5 + 0.5 * w,
        "mass": r(4 * k),
        "health": 100 + 20 * k,
        "armour": 4 + w,
        "material": "steel",
        "color": "#4f6a7a",
        "power": r(10 * k / 3),
        "boxes": [
            {"size": vec(s, h, s)},
            {"size": vec(s + 0.02, min(0.12, h * 0.4), s + 0.02), "at": vec(0, h / 4, 0), "color": GOLD, "detail": True},
        ],
        "sockets": [sock("base", (-s / 2, 0, 0), "-x"), sock("leaf", (s / 2, 0, 0), "+x")],
        "joint": {
            "type": "motor", "axis": "+y", "pivot": vec(s / 2, 0, 0), "min": -100, "max": 100, "leaf": "leaf",
            "speed": speed, "strength": strength, "give": 20, "overloadDamage": 0,
        },
        "config": [
            num("min", "Lowest angle", "Limits", "deg", -100, 100, 1, "The far end a signal of -1 turns it to."),
            num("max", "Highest angle", "Limits", "deg", -100, 100, 1, "The far end a signal of 1 (or opening it) turns it to."),
            num("speed", "Speed", "Motor", "deg/s", 5, top, 1, f"How fast it turns (this size: up to {top} deg/s)."),
            num("strength", "Torque", "Motor", "N m", torque / 10, torque, 50,
                f"Its rating: what it holds rigidly (this size: up to {torque} N m). More load and it gives way; a lower setting caps the power it draws."),
        ],
    }
    return d


# ---- Turntables --------------------------------------------------------------------------------------------------

TURNTABLE_TORQUE = {1: 3000, 2: 12000, 3: 36000}  # N m


def turntable(w, l, legacy=False):
    k = w * w * (l + 1)
    torque = TURNTABLE_TORQUE[w]
    top = TURN_SPEED[l]
    if legacy:
        name, s, h = "Turntable", 0.6, 0.2
        speed, strength = 45, 6000
    else:
        name, s, h = f"Turntable {w}x{w} {LENGTHS[l]}", C * w, 0.1 + 0.1 * l
        speed, strength = top, torque
    d = {
        "format": "call-of-slop-construct-part/1",
        "name": name,
        "description": f"Turns what sits on it round and round: a signal of 1 turns it one way at full speed, -1 the other, 0 holds it. "
        f"Size {size_word(w, l)}: holds up to {torque} N m rigidly; more and it gives way.",
        "group": "Joints",
        "section": "Turntables",
        "role": "joint",
        "tool": "wrench",
        "attachSeconds": 1.5 + 0.5 * w,
        "mass": r(5.625 * k),
        "health": 100 + 25 * k,
        "armour": 6 + w,
        "material": "steel",
        "color": "#5b6168",
        "power": r(10 * k / 8),
        "boxes": [
            {"size": vec(s, h, s)},
            {"size": vec(s - 0.1, 0.04, s - 0.1), "at": vec(0, h / 2, 0), "color": GOLD, "detail": True},
        ],
        "sockets": [sock("base", (0, -h / 2, 0), "-y"), sock("top", (0, h / 2, 0), "+y")] + sides(s / 2, s / 2),
        "joint": {
            "type": "turntable", "axis": "+y", "pivot": vec(0, h / 2, 0), "leaf": "top", "speed": speed, "strength": strength,
            "give": 20, "overloadDamage": 0,
        },
        "config": [
            num("speed", "Speed", "Motor", "deg/s", 5, top, 1, f"How fast it turns at a full signal (this size: up to {top} deg/s)."),
            num("strength", "Torque", "Motor", "N m", torque / 10, torque, 100,
                f"Its rating: what it holds rigidly (this size: up to {torque} N m). More load and it gives way; a lower setting caps the power it draws."),
        ],
    }
    return d


# The parts that were there before, as the size they are (they keep their names, shapes and defaults).
LEGACY = {"Pistons": (2, 3), "MotorHinges": (1, 2), "Turntables": (2, 1)}


def everything():
    out = []
    for folder, make in (("Pistons", piston), ("MotorHinges", motor_hinge), ("Turntables", turntable)):
        for w in (1, 2, 3):
            for l in (1, 2, 3):
                out.append((folder, make(w, l, legacy=LEGACY[folder] == (w, l))))
    return out


def file_name(name):
    return "".join(ch for ch in name if ch.isalnum()) + ".json"


def main():
    check = "--check" in sys.argv
    want = {}
    names = set()
    for folder, d in everything():
        assert d["name"] not in names, d["name"]
        assert len(d["name"]) <= 32, d["name"]
        names.add(d["name"])
        want[os.path.join(OUT, folder, file_name(d["name"]))] = json.dumps(d, indent=2) + "\n"
    have = {}
    for root, _, files in os.walk(OUT):
        for f in files:
            if f.endswith(".json"):
                path = os.path.join(root, f)
                with open(path, encoding="utf-8") as fh:
                    have[path] = fh.read()
    stale = sorted(set(have) - set(want))
    changed = sorted(p for p in want if have.get(p) != want[p])
    if check:
        for p in stale:
            print(f"stale: {p}")
        for p in changed:
            print(f"out of date: {p}")
        print(f"{len(want)} actuator parts; {len(changed)} out of date, {len(stale)} stale")
        return 1 if stale or changed else 0
    for p in stale:
        os.remove(p)
    for p in changed:
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(want[p])
    print(f"{len(want)} actuator parts written ({len(changed)} changed, {len(stale)} removed) in {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
