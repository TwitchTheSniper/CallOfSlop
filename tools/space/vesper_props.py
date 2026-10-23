#!/usr/bin/env python3
"""Writes the Space pack's spaceport bazaar prop set, pack-local in Maps/Space/Props/Vesper/ (category "Vesper"):
a docked shuttle, cargo crates and stacks, neon market stalls, a noodle counter, a holo sign post, a pad light pylon,
a customs scanner gate, a shuttle engine pod, a fuel bowser (it explodes), a scrap pile, a porter bot, a neon awning
and a lantern string. Original designs only (no film or game ships, names or insignia).

    python3 tools/space/vesper_props.py            (run from the repository root)

Pack-local on purpose: every global prop must be shown on a Dev gallery map, which these needn't be.
"""
import json
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "propviz"))
import gen  # noqa: E402
from gen import B, At, Cyl, HCyl, C, brk  # noqa: E402

OUT = os.path.join(REPO, "Maps", "Space", "Props", "Vesper")
CAT = "Vesper"
AUTHOR = "Call of Slop"

HULL = "#b9b2a6"
HULL2 = "#8e877c"
STEEL = "#6c6f73"
DARK = "#2a2c30"
RUST = "#8a5a3a"
PINK = "#ff5fb4"
CYAN = "#4fe3ff"
ORANGE = "#ffa040"
LIME = "#b4ff50"
VIOLET = "#b070ff"
RED = "#ff4a3a"
AMBER = "#ffb24a"
CANVAS = "#c8443a"
CANVAS2 = "#2d7fb8"
WOOD = "#7a5a3c"

SQ = {"randomYaw": False, "yawStep": 90, "yawJitter": 3}
ANY = {"randomYaw": True, "scale": [0.95, 1.05]}
PROPS = []


def prop(name, desc, parts, **kw):
    n = len(gen.PROPS)
    gen.prop(name, CAT, desc, parts, **kw)
    gen.PROPS[-1]["author"] = AUTHOR
    PROPS.extend(gen.PROPS[n:])


def glow(kind, color, period=2.4, glow_=1.2, **kw):
    a = {"type": kind, "color": color, "glow": glow_}
    if kind in ("pulse", "blink", "cycle"):
        a["period"] = period
    a.update(kw)
    return a


def lit(part, anim):
    part = dict(part)
    part["anim"] = anim
    return part


def shuttle():
    """A docked passenger shuttle, 11 m long along +X (nose at +X), on three landing skids."""
    p = [At([7.0, 2.2, 3.2], 0.9, 0.5, color=HULL, mat="metal"),                                  # fuselage
         B([3.0, 1.6, 2.4], [5.0, 2.1, 0], color=HULL, roll=0, yaw=0, pitch=0),                  # nose block
         B([2.2, 1.0, 1.8], [6.4, 2.0, 0], color=HULL2),                                         # nose tip
         B([1.6, 0.7, 1.9], [4.6, 2.75, 0], color="#2e4450"),                                     # cockpit glass
         At([2.6, 0.5, 3.4], 3.1, -0.5, color=HULL2),                                             # spine
         B([4.0, 0.25, 3.0], [-1.0, 1.6, 2.9], color=HULL2, mat="metal"),                        # wings
         B([4.0, 0.25, 3.0], [-1.0, 1.6, -2.9], color=HULL2, mat="metal"),
         B([2.0, 0.9, 1.0], [-2.8, 1.5, 3.3], color=STEEL),                                       # wingtip engines
         B([2.0, 0.9, 1.0], [-2.8, 1.5, -3.3], color=STEEL),
         B([2.2, 1.4, 0.3], [-2.6, 3.6, 0], color=HULL2),                                         # tail fin
         At([0.4, 0.9, 0.4], 0, 4.0, color=DARK), At([0.4, 0.9, 0.4], 0, -2.0, 1.2, color=DARK), At([0.4, 0.9, 0.4], 0, -2.0, -1.2, color=DARK),  # skids
         B([1.2, 1.6, 0.06], [1.0, 1.4, 1.63], color=DARK),                                       # door
         lit(B([0.2, 0.5, 0.5], [-3.9, 1.5, 3.3], color=CYAN), glow("pulse", CYAN, 1.6, glow_=1.5)),
         lit(B([0.2, 0.5, 0.5], [-3.9, 1.5, -3.3], color=CYAN), glow("pulse", CYAN, 1.6, glow_=1.5)),
         lit(B([0.2, 0.2, 0.2], [-2.6, 4.4, 0], color=RED), glow("blink", RED, 1.2, duty=0.3, glow_=1.8)),
         lit(B([0.5, 0.08, 0.5], [6.6, 2.55, 0], color=AMBER), glow("blink", AMBER, 2.0, duty=0.5, glow_=1.0))]
    for k in range(4):
        p.append(B([0.4, 0.4, 0.04], [-1.2 + k * 0.9, 2.3, 1.62], color="#2e4450"))
        p.append(B([0.4, 0.4, 0.04], [-1.2 + k * 0.9, 2.3, -1.62], color="#2e4450"))
    prop("Docked Shuttle", "A docked passenger shuttle on landing skids, nose at +X: a tall hull, stub wings with engine pods and a fin. Solid cover.", p,
         collider=[C([7.0, 2.3, 3.2], [0.5, 2.0, 0]), C([5.2, 1.8, 2.4], [5.0, 2.0, 0]), C([4.0, 1.0, 3.0], [-1.0, 1.6, 2.9]), C([4.0, 1.0, 3.0], [-1.0, 1.6, -2.9]),
                   C([0.4, 1.0, 0.4], [4.0, 0.5, 0]), C([0.4, 1.0, 0.4], [-2.0, 0.5, 1.2]), C([0.4, 1.0, 0.4], [-2.0, 0.5, -1.2])],
         scatter={"randomYaw": False, "yawStep": 90})


def cargo():
    p = [At([2.0, 1.6, 1.6], 0, color=HULL2, mat="metal"), At([2.06, 0.12, 1.66], 0.1, color=DARK), At([2.06, 0.12, 1.66], 1.4, color=DARK),
         At([0.1, 1.62, 1.66], 0, -0.95, color=DARK), At([0.1, 1.62, 1.66], 0, 0.95, color=DARK),
         At([0.5, 0.4, 0.02], 0.7, 0.4, 0.81, color=ORANGE), lit(At([0.12, 0.08, 0.02], 1.25, -0.6, 0.81, color=LIME), glow("blink", LIME, 2.6, duty=0.5, glow_=0.9))]
    prop("Cargo Crate Large", "A big banded cargo crate with an orange label and a green seal lamp. Cover; breaks under heavy fire.", p,
         collider=[C([2.0, 1.6, 1.6])], breakable=brk(320, "metal", by=["explosions", "streaks"], count=14), scatter=SQ)
    p = [At([1.2, 1.0, 1.0], 0, color=STEEL, mat="metal"), At([1.24, 0.08, 1.04], 0.92, color=DARK), At([0.4, 0.3, 0.02], 0.4, 0.2, 0.51, color=CYAN)]
    prop("Cargo Crate Small", "A grey cargo crate with a cyan label.", p, breakable=brk(140, "metal", count=10), scatter=ANY)
    p = [At([2.0, 1.6, 1.6], 0, color=HULL2, mat="metal"), At([2.06, 0.12, 1.66], 1.4, color=DARK),
         At([1.2, 1.0, 1.0], 1.6, -0.3, 0.1, color=STEEL, mat="metal"), At([1.24, 0.08, 1.04], 2.52, -0.3, 0.1, color=DARK),
         At([0.5, 0.4, 0.02], 0.7, 0.4, 0.81, color=ORANGE)]
    prop("Cargo Crate Stack", "A big cargo crate with a small one on top: tall cover.", p, collider=[C([2.0, 2.6, 1.6])], scatter=SQ)
    p = [At([1.5, 0.3, 1.0], 0, color=DARK), HCyl(1.0, 1.6, 0.85, color=STEEL, mat="metal"), HCyl(0.6, 0.4, 0.85, x=1.0, color=DARK),
         At([0.3, 0.9, 0.3], 0.3, -0.5, 0.4, color=RUST), lit(HCyl(0.45, 0.1, 0.85, x=1.2, color=CYAN)[0], glow("pulse", CYAN, 2.0, glow_=1.4))]
    prop("Shuttle Engine Pod", "A shuttle engine on a repair cradle, its nozzle glowing from the test bench. Cover.", p, collider=[C([1.8, 1.4, 1.0])], scatter=ANY)
    p = [At([2.4, 1.1, 1.3], 0.3, color="#9a3a30", mat="metal"), At([2.5, 0.1, 1.34], 1.4, color=DARK),
         At([0.5, 0.5, 0.5], 0, -0.8, 0.5, color=DARK), At([0.5, 0.5, 0.5], 0, 0.8, 0.5, color=DARK), At([0.5, 0.5, 0.5], 0, -0.8, -0.5, color=DARK), At([0.5, 0.5, 0.5], 0, 0.8, -0.5, color=DARK),
         At([0.12, 0.5, 0.12], 1.4, 0.9, color=STEEL), At([0.5, 0.06, 0.5], 1.9, 0.9, color=STEEL), At([0.7, 0.5, 0.02], 0.6, 0, 0.66, color=AMBER),
         lit(At([0.1, 0.1, 0.02], 1.25, -0.9, 0.66, color=RED), glow("blink", RED, 1.0, duty=0.4, glow_=1.4))]
    prop("Fuel Bowser", "A wheeled shuttle-fuel bowser with an amber hazard plate and a red lamp. It goes up when it breaks.", p,
         collider=[C([2.4, 1.5, 1.3])], breakable=brk(90, "metal", count=16, size=(0.08, 0.3), speed=6, explode={"radius": 6.5, "damage": 130, "fuse": 0.5, "knockback": 11}),
         scatter=SQ)
    p = [At([1.8, 0.6, 1.4], 0, color=RUST), At([0.9, 0.5, 0.6], 0.5, -0.3, 0.2, color=STEEL, yaw=30), At([0.6, 0.4, 0.8], 0.55, 0.5, -0.2, color=DARK, yaw=-20),
         At([1.0, 0.1, 0.2], 1.0, 0, 0.1, color=HULL2, yaw=50), At([0.3, 0.3, 0.3], 0.95, -0.5, -0.4, color=CYAN)]
    prop("Scrap Pile", "A heap of shuttle scrap: plating, a cowling and a coil. Low cover; breaks.", p,
         collider=[C([1.8, 1.1, 1.4])], breakable=brk(110, "metal", count=12, colors=[RUST, STEEL, DARK]), scatter=ANY)


def stalls():
    for name, canvas, neon in (("Bazaar Stall Red", CANVAS, PINK), ("Bazaar Stall Blue", CANVAS2, CYAN)):
        p = [At([2.6, 0.9, 1.0], 0, 0, 0.4, color=WOOD, mat="wood"), At([2.7, 0.06, 1.1], 0.9, 0, 0.4, color=DARK),
             At([0.08, 2.3, 0.08], 0, -1.25, -0.6, color=STEEL), At([0.08, 2.3, 0.08], 0, 1.25, -0.6, color=STEEL),
             At([0.08, 2.5, 0.08], 0, -1.25, 0.85, color=STEEL), At([0.08, 2.5, 0.08], 0, 1.25, 0.85, color=STEEL),
             B([2.8, 0.06, 1.9], [0, 2.45, 0.15], color=canvas, pitch=-8),
             At([2.6, 0.5, 0.3], 0.96, 0, 0.1, color=HULL2), At([0.5, 0.3, 0.3], 0.96, -0.8, 0.0, color=ORANGE), At([0.4, 0.25, 0.25], 0.96, 0.6, 0.2, color=LIME),
             lit(At([2.4, 0.06, 0.06], 2.3, 0, 0.95, color=neon), glow("flicker", neon, rate=4, duty=0.96, glow_=1.6)),
             At([2.6, 1.3, 0.04], 0.95, 0, -0.62, color=HULL2)]
        prop(name, f"A market stall with a striped canopy, goods on the counter and a {('pink' if neon == PINK else 'cyan')} neon strip along its front. Faces +Z. Cover.", p,
             collider=[C([2.6, 1.0, 1.0], [0, 0.5, 0.4]), C([2.6, 1.3, 0.1], [0, 1.6, -0.62])], scatter=SQ)
    p = [At([3.0, 1.0, 0.9], 0, color=STEEL, mat="metal"), At([3.1, 0.08, 1.0], 1.0, color=DARK), At([1.0, 0.4, 0.5], 1.08, -0.8, color=STEEL),
         At([0.5, 0.4, 0.5], 1.08, 0.4, color=DARK), lit(At([0.3, 0.5, 0.3], 1.5, 0.4, color="#e8e8f0"), {"type": "bob", "distance": 0.12, "period": 1.4, "glow": 0.3}),
         At([0.06, 2.6, 0.06], 0, -1.4, -0.4, color=STEEL), At([0.06, 2.6, 0.06], 0, 1.4, -0.4, color=STEEL), At([3.0, 0.08, 1.2], 2.6, 0, 0, color=DARK),
         lit(At([0.4, 0.5, 0.4], 2.0, -1.0, 0.2, color=ORANGE), glow("flicker", ORANGE, rate=6, duty=0.9, glow_=1.4)),
         lit(At([0.4, 0.5, 0.4], 2.0, 1.0, 0.2, color=ORANGE), glow("flicker", ORANGE, rate=6, duty=0.9, glow_=1.4)),
         lit(At([2.4, 0.08, 0.06], 2.45, 0, 0.6, color=LIME), glow("flicker", LIME, rate=4, duty=0.96, glow_=1.5))]
    prop("Noodle Counter", "A steaming noodle counter under a roof with two paper lanterns and a lime neon strip. Faces +Z. Cover.", p,
         collider=[C([3.0, 1.1, 0.9])], scatter=SQ)
    p = [At([0.5, 0.1, 0.5], 0, color=DARK), At([0.12, 3.2, 0.12], 0.1, color=STEEL),
         lit(At([1.2, 0.8, 0.04], 2.3, 0, 0, color=CYAN), glow("flicker", CYAN, rate=5, duty=0.92, glow_=1.6)),
         lit(At([1.1, 0.08, 0.05], 2.1, 0, 0, color=PINK), glow("pulse", PINK, 2.0, glow_=1.3)),
         lit(At([0.3, 0.3, 0.05], 2.75, -0.35, 0.0, color=DARK), glow("blink", VIOLET, 1.5, duty=0.5, glow_=1.2))]
    prop("Holo Sign Post", "A slim post with a flickering cyan holo-panel and a pink underline. Faces +Z.", p, collider=[C([0.5, 3.3, 0.5])],
         breakable=brk(70, "metal", count=8, size=(0.05, 0.16)), scatter=ANY)
    p = [At([0.8, 0.25, 0.8], 0, color=DARK), At([0.3, 6.0, 0.3], 0.25, color=STEEL, mat="metal"), At([1.2, 0.2, 1.2], 6.2, color=STEEL)]
    for k, col in enumerate((RED, AMBER, CYAN)):
        p.append(lit(At([0.3, 0.3, 0.3], 6.4 + k * 0.35, 0.5 - k * 0.5, color=col), glow("blink", col, 1.0 + k * 0.4, duty=0.4, glow_=1.8)))
    for y in (2.0, 3.5, 5.0):
        p.append(lit(At([0.34, 0.25, 0.34], y, color=ORANGE), glow("pulse", ORANGE, 3.0, glow_=1.0)))
    prop("Pad Light Pylon", "A 6 m landing-pad pylon with amber bands and blinking top lamps. Cover.", p, collider=[C([0.8, 6.6, 0.8])], scatter=ANY)
    p = [At([0.5, 2.8, 0.5], 0, -1.5, color=STEEL, mat="metal"), At([0.5, 2.8, 0.5], 0, 1.5, color=STEEL, mat="metal"),
         At([3.5, 0.4, 0.5], 2.8, color=STEEL, mat="metal"), lit(At([2.4, 0.12, 0.08], 2.9, 0, 0.26, color=CYAN), glow("pulse", CYAN, 1.4, glow_=1.5)),
         lit(At([0.08, 2.2, 0.08], 0.3, -1.2, 0, color=LIME), glow("cycle", LIME, 2.0, steps=4, on=[0, 1], glow_=1.2)),
         lit(At([0.08, 2.2, 0.08], 0.3, 1.2, 0, color=LIME), glow("cycle", LIME, 2.0, steps=4, on=[2, 3], glow_=1.2)),
         At([0.6, 0.6, 0.3], 0.9, -1.5, 0.3, color=DARK), At([0.4, 0.3, 0.02], 1.1, -1.5, 0.46, color=AMBER)]
    prop("Customs Scanner Gate", "A customs scanner arch: two posts with sweeping green beams under a cyan lintel. Walk through it (only the posts are solid).", p,
         collider=[C([0.5, 2.8, 0.5], [-1.5, 1.4, 0]), C([0.5, 2.8, 0.5], [1.5, 1.4, 0])], scatter=SQ)
    p = [At([0.9, 0.5, 0.8], 0, color=DARK), At([0.8, 0.7, 0.7], 0.5, color=STEEL, mat="metal"), At([0.5, 0.4, 0.5], 1.2, color=HULL2),
         lit(At([0.3, 0.1, 0.04], 1.35, 0, 0.26, color=CYAN), glow("pulse", CYAN, 1.2, glow_=1.4)),
         At([0.15, 0.6, 0.15], 0.6, 0.5, 0.0, color=DARK), At([0.15, 0.6, 0.15], 0.6, -0.5, 0.0, color=DARK),
         At([0.9, 0.2, 0.9], 0.02, color=DARK), At([0.4, 0.3, 0.3], 0.9, 0, -0.45, color=ORANGE)]
    prop("Porter Bot", "A squat porter robot with a cyan visor and an orange pack on its back. Faces +Z. Breaks.", p,
         collider=[C([0.9, 1.6, 0.8])], breakable=brk(120, "metal", count=12, colors=[STEEL, DARK, CYAN]), scatter=ANY)


def dressing():
    p = [At([3.0, 0.08, 1.4], 2.6, 0, 0.7, color=DARK), At([3.0, 0.3, 0.08], 2.5, 0, 1.36, color=CANVAS),
         lit(At([2.9, 0.06, 0.06], 2.84, 0, 1.4, color=PINK), glow("flicker", PINK, rate=4, duty=0.95, glow_=1.6)),
         At([0.06, 0.6, 0.06], 2.68, -1.4, 1.3, color=STEEL, roll=0, pitch=-40), At([0.06, 0.6, 0.06], 2.68, 1.4, 1.3, color=STEEL, pitch=-40)]
    prop("Neon Awning", "A shop-front awning on two struts with a pink neon edge: stands on y = 0 with its back at -Z (looks only).", p,
         collision="cosmetic", fade=90, scatter=SQ)
    p = [At([4.0, 0.03, 0.03], 3.2, color=DARK)]
    for k, col in enumerate((PINK, CYAN, ORANGE, LIME, VIOLET, PINK, CYAN)):
        x = -1.8 + k * 0.6
        p.append(At([0.02, 0.25, 0.02], 2.95, x, color=DARK))
        p.append(lit(At([0.26, 0.32, 0.26], 2.63, x, color=col), {"type": "swing", "axis": "z", "pivot": [x, 3.2, 0], "angle": 7, "period": 2.2 + k * 0.2, "wind": 1, "glow": 1.3}))
    prop("Lantern String", "A string of seven coloured paper lanterns swinging under a line 3.2 m up (looks only).", p, collision="cosmetic", fade=90, scatter=SQ)
    p = [At([0.6, 0.06, 0.6], 0, color=DARK), lit(At([0.46, 0.04, 0.46], 0.06, color=CYAN), glow("blink", CYAN, 1.6, duty=0.5, glow_=1.6))]
    prop("Pad Edge Light", "A flush cyan pad-edge marker light (looks only).", p, collision="cosmetic", fade=100, scatter=ANY)
    p = [At([0.8, 0.5, 0.8], 0, color=DARK), At([0.7, 0.9, 0.7], 0.5, color=STEEL, mat="metal"), At([0.4, 0.5, 0.4], 1.4, color=DARK),
         At([0.9, 0.08, 0.9], 1.9, color=STEEL), lit(At([0.72, 0.1, 0.72], 1.98, color=VIOLET), glow("pulse", VIOLET, 2.4, glow_=1.3))]
    prop("Plaza Lamp", "A short plaza lamp with a glowing violet top disc. Cover.", p, collider=[C([0.8, 2.1, 0.8])], scatter=ANY)
    p = [At([1.4, 0.06, 1.4], 0, color=DARK), At([0.14, 2.9, 0.14], 0.06, color=STEEL), At([1.2, 0.9, 0.1], 2.1, color=DARK),
         lit(At([1.0, 0.7, 0.03], 2.2, 0, 0.06, color=ORANGE), glow("flicker", ORANGE, rate=3, duty=0.94, glow_=1.4)),
         lit(At([1.0, 0.7, 0.03], 2.2, 0, -0.06, color=ORANGE), glow("flicker", ORANGE, rate=3, duty=0.94, glow_=1.4))]
    prop("Departures Board", "A two-sided glowing orange departures board on a post. Cover (the post).", p, collider=[C([0.3, 3.0, 0.3])], scatter=ANY)


def write():
    os.makedirs(OUT, exist_ok=True)
    for p in PROPS:
        fn = "".join(ch for ch in p["name"] if ch.isalnum()) + ".json"
        with open(os.path.join(OUT, fn), "w") as f:
            json.dump(p, f, indent=1)
            f.write("\n")
    print(f"wrote {len(PROPS)} props to {OUT}")


def main():
    shuttle()
    cargo()
    stalls()
    dressing()
    write()


if __name__ == "__main__":
    main()
