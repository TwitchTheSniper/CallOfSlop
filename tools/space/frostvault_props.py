#!/usr/bin/env python3
"""Writes the Space pack's cryo-vault prop set, pack-local in Maps/Space/Props/Frostvault/ (category "Frostvault"):
cryo pods (lying and upright), a specimen tank, lab benches, cryo dewars, a server stack, cold-storage lockers, specimen
crates, a biohazard bin, a quarantine glass barrier, a flush hex floor light and a hanging lamp strip, a frost vent and
a coolant manifold. Original designs only (no film or game names or insignia).

    python3 tools/space/frostvault_props.py            (run from the repository root)

Pack-local on purpose: every global prop must be shown on a Dev gallery map, which these needn't be. Health follows the
material guide in Props/README.md: pods, tanks and benches are unbreakable cover; cases and bins break.
"""
import json
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "propviz"))
import gen  # noqa: E402
from gen import B, At, Cyl, C, brk  # noqa: E402

OUT = os.path.join(REPO, "Maps", "Space", "Props", "Frostvault")
CAT = "Frostvault"
AUTHOR = "Call of Slop"

WHITE = "#e9eef2"
PALE = "#cfd8df"
STEEL = "#8a949c"
DARK = "#2f353b"
ICE = "#9fdcff"
ICE2 = "#5fb8ea"
FROST = "#f4fbff"
AMBER = "#ffb24a"
RED = "#ff4a3a"
GREEN = "#6fe0a0"
YELLOW = "#f2c230"

SQ = {"randomYaw": False, "yawStep": 90, "yawJitter": 2}
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


def pods():
    # Lying cryo pod: a dark plinth, a white casing with rounded-ish ends, an ice-blue window that pulses, frost trim.
    p = [At([2.4, 0.3, 1.0], 0, color=DARK, mat="metal"),
         At([2.2, 0.8, 0.92], 0.3, color=WHITE, mat="metal"),
         B([2.3, 0.5, 0.6], [0, 1.1, 0], color=PALE, roll=45),
         At([0.16, 0.9, 1.0], 0.28, -1.1, color=STEEL), At([0.16, 0.9, 1.0], 0.28, 1.1, color=STEEL),
         lit(B([1.3, 0.32, 0.5], [0, 1.16, 0.08], color=ICE, roll=45), glow("pulse", ICE, 3.2, glow_=1.1)),
         At([0.5, 0.06, 0.24], 0.42, 0.95, 0.5, color=DARK),
         lit(At([0.1, 0.04, 0.02], 0.5, 0.85, 0.51, color=GREEN), glow("blink", GREEN, 1.6, duty=0.5, glow_=0.9)),
         At([2.0, 0.03, 0.08], 0.74, 0, 0.47, color=FROST), At([2.0, 0.03, 0.08], 0.74, 0, -0.47, color=FROST)]
    prop("Cryo Pod", "A lying cryogenic pod on a dark plinth: a white casing, an ice-blue window and a green status lamp. Cover.", p,
         collider=[C([2.4, 1.35, 1.0])], scatter=SQ)
    # Upright pod: a tall white column with a frosted window and a base ring.
    p = [At([1.0, 0.25, 1.0], 0, color=DARK, mat="metal"), At([0.84, 2.0, 0.84], 0.25, color=WHITE, mat="metal"),
         At([0.9, 0.12, 0.9], 2.25, color=STEEL),
         lit(At([0.4, 1.2, 0.04], 0.75, 0, 0.42, color=ICE), glow("pulse", ICE, 2.6, glow_=1.1)),
         At([0.5, 0.06, 0.06], 0.6, 0, 0.42, color=DARK),
         lit(At([0.08, 0.05, 0.02], 2.0, 0.3, 0.43, color=AMBER), glow("blink", AMBER, 2.1, duty=0.4, glow_=0.9))]
    prop("Cryo Pod Upright", "An upright cryogenic pod: a white column with a frosted ice-blue window. Faces +Z. Cover.", p,
         collider=[C([1.0, 2.4, 1.0])], scatter=SQ)
    # Specimen tank: a glass column on a base with a green-blue glow and a cap.
    p = [Cyl(1.3, 0.4, 0, color=DARK, mat="metal"), Cyl(1.1, 2.0, 0.4, color="#7fd8d0", k=0.98),
         [lit(q, glow("pulse", GREEN, 4.0, glow_=1.3)) for q in Cyl(0.5, 1.6, 0.6, color=GREEN)],
         Cyl(1.3, 0.3, 2.4, color=STEEL, mat="metal"), Cyl(0.5, 0.3, 2.7, color=DARK)]
    prop("Specimen Tank", "A glass specimen column on a steel base, lit green from inside. Cover; the glass breaks.", p,
         collider=[C([1.2, 2.7, 1.2])], breakable=brk(120, "glass", count=14, size=(0.05, 0.2), colors=["#9fe8e0", "glass"]), scatter=ANY)
    # Cryo dewar: a squat steel cylinder with a neck and valve.
    p = [Cyl(0.8, 1.1, 0, color=STEEL, mat="metal"), Cyl(0.5, 0.25, 1.1, color=PALE), Cyl(0.9, 0.08, 0.5, color=ICE2),
         At([0.1, 0.3, 0.1], 1.3, 0.2, color=DARK), At([0.26, 0.06, 0.26], 1.42, 0.2, color=RED)]
    prop("Vault Dewar", "A liquid-nitrogen dewar: a steel cylinder with a blue band and a red valve wheel.", p,
         collider=[C([0.8, 1.5, 0.8])], breakable=brk(160, "metal", count=10), scatter=ANY)


def lab():
    p = [At([2.4, 0.08, 0.95], 0.8, color=WHITE, mat="metal"), At([2.2, 0.72, 0.85], 0.08, color=PALE, mat="metal"),
         At([2.3, 0.08, 0.9], 0, color=DARK),
         At([0.5, 0.35, 0.3], 0.88, -0.7, -0.15, color=STEEL), lit(At([0.36, 0.22, 0.02], 0.96, -0.7, 0.0, color=ICE2), glow("flicker", ICE2, rate=3, duty=0.95, glow_=0.9)),
         At([0.25, 0.3, 0.25], 0.88, 0.4, 0.1, color="#7fd8d0"), At([0.2, 0.5, 0.2], 0.88, 0.85, -0.2, color=STEEL),
         At([0.4, 0.03, 0.3], 0.88, -0.1, 0.25, color=DARK)]
    prop("Lab Bench", "A white laboratory bench with a monitor, a beaker and a centrifuge on top. Cover.", p,
         collider=[C([2.4, 0.9, 0.95])], scatter=SQ)
    p = [At([1.2, 2.0, 0.6], 0, color=WHITE, mat="metal")]
    for x in (-0.45, -0.15, 0.15, 0.45):
        p.append(At([0.26, 1.9, 0.02], 0.05, x, 0.3, color=PALE))
        p.append(At([0.04, 0.2, 0.03], 0.95, x + 0.1, 0.31, color=STEEL))
        p.append(lit(At([0.06, 0.04, 0.02], 1.7, x - 0.08, 0.31, color=ICE2), glow("blink", ICE2, 3.0 + x, duty=0.6, glow_=0.8)))
    prop("Cold Storage Locker", "A row of four white cold-storage lockers with blue status lamps. Faces +Z.", p,
         collider=[C([1.2, 2.0, 0.6])], breakable=brk(240, "metal", count=12), scatter=SQ)
    p = [At([0.8, 2.2, 1.0], 0, color=DARK, mat="metal"), At([0.72, 2.0, 0.02], 0.1, 0, 0.5, color="#1c2024")]
    for k in range(8):
        y = 0.25 + k * 0.23
        p.append(At([0.66, 0.05, 0.02], y, 0, 0.51, color=STEEL))
        p.append(lit(At([0.05, 0.05, 0.02], y + 0.08, -0.25 + (k % 4) * 0.16, 0.515, color=[ICE2, GREEN, AMBER, ICE2][k % 4]),
                     glow("blink", [ICE2, GREEN, AMBER, ICE2][k % 4], 0.6 + k * 0.17, duty=0.5, glow_=0.9)))
    prop("Vault Server Stack", "A tall dark server stack with rows of blinking blue, green and amber lamps. Faces +Z.", p,
         collider=[C([0.8, 2.2, 1.0])], breakable=brk(400, "metal", by=["explosions", "streaks", "bullets"], count=14), scatter=SQ)
    p = [At([1.0, 0.8, 0.8], 0, color=WHITE, mat="metal"), At([1.04, 0.1, 0.84], 0.3, color=ICE2), At([1.04, 0.06, 0.84], 0.74, color=STEEL),
         At([0.3, 0.3, 0.02], 0.25, 0, 0.41, color=DARK)]
    prop("Specimen Crate", "A white insulated specimen crate with a blue band and a label plate.", p,
         breakable=brk(90, "metal", count=10, size=(0.06, 0.2)), scatter=ANY)
    p = [At([0.6, 0.85, 0.6], 0, color=YELLOW), At([0.64, 0.06, 0.64], 0.85, color=DARK), At([0.3, 0.3, 0.02], 0.3, 0, 0.31, color=DARK)]
    prop("Biohazard Bin", "A yellow clinical-waste bin with a black lid and a black label.", p,
         breakable=brk(40, "soft", count=8, size=(0.05, 0.15), colors=[YELLOW, "black"]), scatter=ANY)
    # Quarantine barrier: a glass panel between two white posts.
    p = [At([0.12, 1.1, 0.12], 0, -0.95, color=WHITE), At([0.12, 1.1, 0.12], 0, 0.95, color=WHITE),
         At([1.8, 0.95, 0.04], 0.12, color="#b8e4f8"), At([1.9, 0.06, 0.1], 1.08, color=STEEL), At([1.8, 0.1, 0.06], 0.03, color=DARK)]
    prop("Quarantine Barrier", "A low glass quarantine barrier between two white posts: cover you can shoot over.", p,
         collider=[C([2.0, 1.15, 0.12])], scatter=SQ)
    p = [At([1.2, 0.9, 0.5], 0, color=STEEL, mat="metal"), At([1.3, 0.1, 0.6], 0.9, color=DARK)]
    for x in (-0.4, 0.0, 0.4):
        p.append(At([0.16, 1.6, 0.16], 0.9, x, color=PALE))
        p.append(At([0.3, 0.1, 0.3], 1.9, x, color=STEEL))
    p.append(At([1.0, 0.12, 0.12], 2.0, 0, 0, color=ICE2))
    p.append(At([0.2, 0.18, 0.2], 1.1, 0.55, 0.3, color=RED))
    prop("Vault Coolant Manifold", "A coolant manifold: three pale risers on a steel cabinet, joined by a blue header pipe.", p,
         collider=[C([1.2, 2.1, 0.5])], scatter=SQ)


def lights():
    p = [B([0.5, 0.05, 0.29], [0, 0.025, 0], color=DARK, yaw=0), B([0.5, 0.05, 0.29], [0, 0.025, 0], color=DARK, yaw=60), B([0.5, 0.05, 0.29], [0, 0.025, 0], color=DARK, yaw=120),
         lit(B([0.36, 0.03, 0.2], [0, 0.055, 0], color=ICE), glow("pulse", ICE, 4.0, glow_=1.5)),
         lit(B([0.36, 0.03, 0.2], [0, 0.055, 0], color=ICE, yaw=60), glow("pulse", ICE, 4.0, glow_=1.5)),
         lit(B([0.36, 0.03, 0.2], [0, 0.055, 0], color=ICE, yaw=120), glow("pulse", ICE, 4.0, glow_=1.5))]
    prop("Hex Floor Light", "A flush hexagonal floor light that pulses ice blue (looks only).", p, collision="cosmetic", fade=80, scatter=ANY)
    p = [At([2.0, 0.08, 0.16], 0.1, color=STEEL), lit(At([1.9, 0.04, 0.12], 0.06, color=FROST), glow("flicker", FROST, rate=2, duty=0.97, glow_=1.4)),
         At([0.04, 0.3, 0.04], 0.18, -0.8, color=DARK), At([0.04, 0.3, 0.04], 0.18, 0.8, color=DARK)]
    prop("Lamp Strip", "A hanging cold-white lamp strip on two short drops: place it with y just under a ceiling (looks only).", p,
         collision="cosmetic", fade=90, scatter=SQ)
    p = [At([0.8, 0.8, 0.06], 0, color=STEEL, mat="metal")]
    for k in range(5):
        p.append(At([0.7, 0.06, 0.03], 0.1 + k * 0.14, 0, 0.03, color=DARK))
    p.append(At([0.9, 0.14, 0.05], 0.72, 0, 0.02, color=FROST))
    p.append(At([0.3, 0.1, 0.05], 0.78, -0.25, 0.03, color=FROST))
    prop("Frost Vent", "A wall vent grille with frost grown over its top louvres: stands on y = 0 with its back at -Z, lift it with y (looks only).", p,
         collision="cosmetic", fade=70, scatter=SQ)
    # A biohazard sign board on a stand.
    p = [At([0.6, 0.06, 0.4], 0, color=DARK), At([0.06, 1.3, 0.06], 0.06, color=STEEL),
         At([0.7, 0.7, 0.04], 1.2, 0, 0.0, color="#1c1c1c"), At([0.62, 0.62, 0.02], 1.24, 0, 0.03, color="#f08a20"),
         At([0.2, 0.2, 0.02], 1.45, 0, 0.045, color="#1c1c1c")]
    prop("Hazard Board", "A black-and-orange hazard board on a steel stand. Faces +Z.", p, collider=[C([0.7, 1.9, 0.4])],
         breakable=brk(60, "metal", count=8, size=(0.05, 0.16)), scatter=ANY)


def write():
    os.makedirs(OUT, exist_ok=True)
    for p in PROPS:
        fn = "".join(ch for ch in p["name"] if ch.isalnum()) + ".json"
        with open(os.path.join(OUT, fn), "w") as f:
            json.dump(p, f, indent=1)
            f.write("\n")
    print(f"wrote {len(PROPS)} props to {OUT}")


def main():
    pods()
    lab()
    lights()
    write()


if __name__ == "__main__":
    main()
