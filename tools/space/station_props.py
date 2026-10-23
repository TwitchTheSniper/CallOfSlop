#!/usr/bin/env python3
"""Writes the reusable sky-station prop set: Props/Station (category "Station"), for floating platforms, refinery decks,
landing pads and sci-fi interiors: gas tanks, cargo pods and cases, consoles, deck lights and beacons, an antenna mast,
a sensor dish, pipe runs and risers, a vent stack, a fuel coupling, deck barriers, a volatile gas canister rack, a hover
cargo sled and a parked cloud skimmer. Original designs only (no film or game ships, names or insignia).

    python3 tools/space/station_props.py            (run from the repository root)

Uses the box helpers of tools/propviz/gen.py. Health follows the material health guide in Props/README.md: big metal
structures (tanks, pods, vehicles, masts) are unbreakable cover; cases and consoles are heavy metal; the canister rack
explodes with a short fuse.
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

PROPS = gen.PROPS
CAT = "Station"
AUTHOR = "Call of Slop"

HULL = "#cdbfa8"     # warm cream hull plating
HULL2 = "#b3a48c"    # a shade darker
BRONZE = "#8d7458"   # bronze bands and trim
STEEL = "#6c6f73"
DARK = "#34363a"
AMBER = "#ffb24a"
TEAL = "#6fe0e8"
RED = "#ff4a3a"
SCREEN = "#3fc4d8"
GLASS = "#2e4450"

SQ = {"randomYaw": False, "yawStep": 90, "yawJitter": 3}
ANY = {"randomYaw": True, "scale": [0.95, 1.05]}


def prop(name, desc, parts, **kw):
    gen.prop(name, CAT, desc, parts, **kw)
    PROPS[-1]["author"] = AUTHOR


def glow(kind="pulse", color=AMBER, period=2.4, glow_=1.3, **kw):
    a = {"type": kind, "color": color, "glow": glow_}
    if kind in ("pulse", "blink", "cycle"):
        a["period"] = period
    a.update(kw)
    return a


def lit(part, anim):
    part = dict(part)
    part["anim"] = anim
    return part


# ------------------------------------------------------------------ tanks ------------------------------------------------------------------

def tank_parts(d, h, x=0.0, z=0.0, y0=0.0, color=HULL, band=BRONZE, ladder=True):
    p = []
    p += Cyl(d, h, y0, x, z, color=color, mat="metal")
    p += Cyl(d * 0.82, 0.35, y0 + h, x, z, color=HULL2, mat="metal")          # shoulder
    p += Cyl(d * 0.5, 0.25, y0 + h + 0.35, x, z, color=HULL2, mat="metal")    # crown
    p += Cyl(d * 1.04, 0.16, y0 + 0.3, x, z, color=band)                      # foot band
    p += Cyl(d * 1.04, 0.16, y0 + h * 0.62, x, z, color=band)                 # mid band
    p.append(At([0.18, 0.4, 0.18], y0 + h + 0.6, x, z, color=STEEL))          # relief valve
    if ladder:
        r = d / 2 + 0.06
        p.append(At([0.05, h, 0.05], y0, x - 0.22, z + r, color=STEEL))
        p.append(At([0.05, h, 0.05], y0, x + 0.22, z + r, color=STEEL))
        for k in range(int(h / 0.45)):
            p.append(At([0.44, 0.04, 0.04], y0 + 0.3 + k * 0.45, x, z + r, color=STEEL))
    return p


def tanks():
    prop("Gas Refinery Tank", "A tall cream pressure tank for refined cloud gas: bronze bands, a crown valve and a ladder.",
         tank_parts(2.6, 5.4) + [At([2.9, 0.12, 2.9], 0, color=DARK)],
         collider=[C([2.4, 5.4, 1.1]), C([1.1, 5.4, 2.4]), C([1.8, 5.4, 1.8])], scatter=ANY)
    p = tank_parts(1.7, 3.8, x=-1.1, ladder=False) + tank_parts(1.7, 3.8, x=1.1, ladder=False)
    p.append(At([4.2, 0.22, 2.0], 0, color=DARK))                                  # skid
    p += HCyl(0.3, 0.9, 2.6, color=STEEL)                                          # cross pipes
    p += HCyl(0.3, 0.9, 1.2, color=STEEL)
    p.append(At([0.5, 0.5, 0.5], 1.0, 0, 0, color=BRONZE))                         # valve box
    p.append(lit(At([0.12, 0.12, 0.04], 1.25, 0, 0.27, color=AMBER), glow("blink", AMBER, 1.6, duty=0.2)))
    prop("Twin Gas Tanks", "Two medium pressure tanks on a skid, joined by pipes and a valve box with a blinking lamp.",
         p, collider=[C([3.9, 3.8, 1.7], [0, 2.1, 0]), C([4.2, 0.22, 2.0])], scatter=SQ)
    prop("Squat Pressure Tank", "A short wide pressure tank: chest-high-and-more cover on a deck.",
         tank_parts(2.2, 1.7, ladder=False), collider=[C([2.0, 1.7, 0.9]), C([0.9, 1.7, 2.0]), C([1.5, 1.7, 1.5])], scatter=ANY)


# ------------------------------------------------------------------ cargo ------------------------------------------------------------------

def cargo():
    p = [At([2.4, 1.3, 1.4], 0, color=HULL2, mat="metal")]
    for x in (-1.15, 1.15):
        p.append(At([0.12, 1.36, 1.46], 0, x, color=BRONZE))
    p.append(At([2.46, 0.1, 1.46], 1.25, color=BRONZE))
    p.append(At([2.0, 0.14, 0.04], 0.7, 0, 0.71, color=DARK))                      # handle rail
    p.append(lit(At([0.3, 0.08, 0.03], 1.0, 0.8, 0.715, color=TEAL), glow("pulse", TEAL, 3.0, glow_=0.9)))
    prop("Station Cargo Pod", "A sealed metal cargo pod with bronze end frames and a status light: solid cover.", p,
         collider=[C([2.4, 1.3, 1.4])], scatter=SQ)
    p = [At([0.9, 0.6, 0.62], 0, color=STEEL, mat="metal"), At([0.94, 0.06, 0.66], 0.56, color=DARK),
         At([0.2, 0.08, 0.04], 0.35, 0, 0.32, color=AMBER)]
    prop("Station Supply Case", "A small grey metal supply case with an amber latch.", p,
         breakable=brk(220, "metal", count=10, size=(0.06, 0.2)), scatter=ANY)
    p = [At([1.6, 0.25, 1.1], 0, color=DARK)]
    for i, (x, z, s) in enumerate([(-0.4, 0, 0.62), (0.4, 0.05, 0.6), (0, 0.02, 0.56)]):
        p.append(At([0.9, s, 0.62], 0.25 + (0.6 if i == 2 else 0), x if i < 2 else 0, z, color=STEEL if i != 1 else HULL2, mat="metal"))
    prop("Supply Case Stack", "Three metal supply cases stacked on a low dark skid.", p,
         collider=[C([1.6, 1.4, 1.1])], scatter=SQ)
    # Hover cargo sled: a flat bed on two glowing lift pads.
    p = [At([3.2, 0.35, 1.6], 0.45, color=HULL, mat="metal"), At([3.3, 0.12, 1.66], 0.8, color=BRONZE),
         At([0.6, 0.9, 1.4], 0.92, -1.25, color=HULL2), At([0.62, 0.3, 1.2], 1.2, -1.58, color=GLASS),
         At([1.2, 0.7, 1.2], 0.92, 0.5, color=STEEL, mat="metal")]
    for x in (-1.0, 1.0):
        p.append(At([0.9, 0.3, 1.3], 0.12, x, color=DARK))
        p.append(lit(At([0.8, 0.05, 1.2], 0.08, x, color=TEAL), glow("pulse", TEAL, 1.8, glow_=1.4)))
    prop("Hover Cargo Sled", "A parked hover sled: a cargo bed and a small cab over two glowing lift pads.", p,
         collider=[C([3.2, 1.3, 1.6], [0, 0.65, 0])], scatter=SQ)


# ------------------------------------------------------------------ consoles and lights ------------------------------------------------------------------

def consoles():
    p = [At([1.6, 0.85, 0.7], 0, color=HULL2, mat="metal"), B([1.6, 0.08, 0.62], [0, 0.95, -0.02], color=DARK, pitch=-18),
         B([1.5, 0.55, 0.08], [0, 1.35, -0.3], color=DARK, pitch=-10),
         lit(B([1.36, 0.44, 0.02], [0, 1.36, -0.25], color=SCREEN, pitch=-10), glow("flicker", SCREEN, rate=3, duty=0.94, glow_=1.0)),
         lit(B([0.5, 0.02, 0.2], [-0.4, 1.0, 0.04], color=AMBER, pitch=-18), glow("blink", AMBER, 1.1, duty=0.6, glow_=0.8)),
         lit(B([0.4, 0.02, 0.2], [0.45, 1.0, 0.04], color=TEAL, pitch=-18), glow("pulse", TEAL, 2.2, glow_=0.8))]
    prop("Station Console", "A control desk with a slanted keyboard deck, a flickering cyan screen and blinking keys. Faces +Z.", p,
         collider=[C([1.6, 1.0, 0.7])], breakable=brk(260, "metal", count=10), scatter=SQ)
    p = [At([0.9, 1.9, 0.5], 0, color=STEEL, mat="metal"), At([0.8, 0.9, 0.02], 0.8, 0, 0.26, color=DARK)]
    for k in range(6):
        p.append(lit(At([0.1, 0.05, 0.02], 0.9 + k * 0.13, -0.25 + (k % 3) * 0.25, 0.27, color=[AMBER, TEAL, RED][k % 3]),
                     glow("blink", [AMBER, TEAL, RED][k % 3], 0.7 + k * 0.23, duty=0.5, glow_=0.9)))
    prop("Station Server Cabinet", "A tall equipment cabinet with rows of blinking status lamps. Faces +Z.", p,
         collider=[C([0.9, 1.9, 0.5])], breakable=brk(300, "metal", count=12), scatter=SQ)
    p = [At([0.22, 0.25, 0.22], 0, color=DARK), At([0.1, 3.6, 0.1], 0.25, color=STEEL),
         At([0.7, 0.08, 0.16], 3.8, 0.28, color=STEEL),
         lit(At([0.46, 0.06, 0.2], 3.72, 0.4, color="#fff0c8"), glow("flicker", "#fff0c8", rate=2, duty=0.98, glow_=1.5))]
    prop("Deck Light Post", "A slim deck lamp post with an arm-mounted warm light.", p, collider=[C([0.22, 3.9, 0.22])], scatter=ANY)
    p = [At([0.36, 0.06, 0.36], 0, color=DARK), lit(At([0.26, 0.04, 0.26], 0.06, color=AMBER), glow("blink", AMBER, 2.0, duty=0.3, glow_=1.6))]
    prop("Deck Floor Light", "A flush amber marker light for landing pad rims and walkway edges (looks only).", p,
         collision="cosmetic", fade=90, scatter=ANY)
    p = [At([0.3, 0.3, 0.3], 0, color=DARK), At([0.12, 1.0, 0.12], 0.3, color=HULL2),
         lit(At([0.24, 0.22, 0.24], 1.3, color=RED), glow("blink", RED, 1.4, duty=0.35, glow_=2.0))]
    prop("Landing Beacon", "A short post with a blinking red beacon for landing pads and deck edges.", p,
         collider=[C([0.3, 1.52, 0.3])], scatter=ANY)


# ------------------------------------------------------------------ masts, dishes, pipes ------------------------------------------------------------------

def masts():
    p = [At([1.2, 0.3, 1.2], 0, color=DARK)]
    for x in (-0.35, 0.35):
        for z in (-0.35, 0.35):
            p.append(At([0.08, 9.0, 0.08], 0.3, x * (1 if True else 1), z, color=STEEL))
    for k in range(9):
        p.append(At([0.78, 0.05, 0.05], 0.8 + k, 0, -0.35, color=STEEL))
        p.append(At([0.05, 0.05, 0.78], 1.3 + k, 0.35, 0, color=STEEL))
    p.append(At([0.1, 2.0, 0.1], 9.3, color=STEEL))
    p.append(lit(At([0.26, 0.26, 0.26], 11.3, color=RED), glow("blink", RED, 1.8, duty=0.25, glow_=2.2)))
    p.append(B([1.1, 1.1, 0.12], [0, 7.6, 0.5], color=HULL, pitch=-15))
    p.append(At([0.6, 0.06, 0.06], 5.0, -0.6, 0, color=STEEL))
    prop("Antenna Mast", "An 11 m lattice antenna mast with a dish panel and a blinking red tip light.", p,
         collider=[C([0.8, 9.3, 0.8], [0, 4.95, 0])], scatter=SQ)
    p = [At([0.9, 0.5, 0.9], 0, color=DARK), At([0.3, 1.2, 0.3], 0.5, color=STEEL)]
    dish = [B([2.2, 2.2, 0.1], [0, 2.6, 0.25], color=HULL, pitch=-30), B([1.6, 1.6, 0.12], [0, 2.62, 0.2], color=HULL2, pitch=-30),
            B([0.08, 0.08, 0.9], [0, 2.8, 0.75], color=STEEL, pitch=-30), At([0.5, 0.5, 0.5], 1.7, 0, 0, color=BRONZE)]
    first = len(p) + 1
    p.append(lit(dish[3], {"type": "swing", "axis": "y", "pivot": [0, 1.95, 0], "angle": 70, "period": 14}))
    for part in dish[:3]:
        p.append(lit(part, {"type": "follow", "parent": first}))
    prop("Sensor Dish", "A pedestal sensor dish that slowly sweeps back and forth.", p, collider=[C([0.9, 1.7, 0.9])], scatter=SQ)
    p = []
    for x in (-1.6, 1.6):
        p.append(At([0.2, 0.7, 0.6], 0, x, color=DARK))
    p += HCyl(0.36, 4.0, 0.65, z=-0.15, color=HULL2)
    p += HCyl(0.26, 4.0, 0.62, z=0.22, color=BRONZE)
    p += HCyl(0.5, 0.25, 0.65, x=0.4, z=-0.15, color=DARK)
    p.append(At([0.2, 0.25, 0.2], 0.85, 0.4, -0.15, color=RED))
    prop("Station Pipe Run", "A 4 m run of two fat process pipes on low supports, with a valve: knee-to-hip cover (runs along X).", p,
         collider=[C([4.0, 0.9, 0.9], [0, 0.45, 0])], scatter=SQ)
    p = [At([0.9, 0.2, 0.6], 0, color=DARK)]
    p += Cyl(0.34, 3.2, 0.2, -0.2, 0, color=HULL2)
    p += Cyl(0.26, 3.2, 0.2, 0.22, 0, color=BRONZE)
    p += HCyl(0.4, 0.2, 1.4, x=-0.2, color=DARK)
    p.append(At([0.5, 0.06, 0.06], 1.6, -0.2, 0.2, color=RED))
    prop("Station Pipe Riser", "Two vertical pipes rising 3 m from a base plate, with a valve wheel.", p,
         collider=[C([0.8, 3.4, 0.45])], scatter=SQ)
    p = Cyl(1.1, 2.6, 0, color=HULL2) + Cyl(1.2, 0.2, 2.6, color=BRONZE) + Cyl(0.9, 0.12, 2.8, color=DARK)
    blades = [lit(B([0.8, 0.03, 0.14], [0, 2.84, 0], color=STEEL, yaw=a), {"type": "spin", "axis": "y", "pivot": [0, 2.84, 0], "rpm": 90})
              for a in (0, 60, 120)]
    p += blades
    prop("Vent Stack", "A squat exhaust stack with a turning fan in its throat.", p,
         collider=[C([1.0, 2.8, 0.5]), C([0.5, 2.8, 1.0])], scatter=ANY)
    p = [At([0.6, 1.1, 0.5], 0, color=HULL2, mat="metal"), At([0.64, 0.12, 0.54], 1.1, color=BRONZE),
         At([0.12, 0.5, 0.12], 0.4, 0.38, 0, color=DARK), At([0.5, 0.1, 0.1], 0.2, 0.62, 0.15, color=DARK),
         lit(At([0.3, 0.12, 0.02], 0.8, 0, 0.26, color=TEAL), glow("pulse", TEAL, 2.6, glow_=1.0))]
    prop("Fuel Coupling", "A refuelling pedestal with a coiled hose and a cyan status strip. Faces +Z.", p,
         collider=[C([0.6, 1.2, 0.5])], scatter=SQ)
    p = [At([2.0, 1.1, 0.3], 0, color=HULL2, mat="metal"), At([2.04, 0.1, 0.34], 1.1, color=BRONZE),
         At([2.04, 0.12, 0.34], 0.1, color=DARK), At([0.4, 0.06, 0.02], 0.7, -0.5, 0.16, color=AMBER),
         At([0.4, 0.06, 0.02], 0.7, 0.5, 0.16, color=AMBER)]
    prop("Station Deck Barrier", "A 2 m metal deck barrier, waist high: solid cover (runs along X).", p,
         collider=[C([2.0, 1.2, 0.3])], scatter=SQ)
    p = [At([1.6, 0.12, 0.7], 0, color=DARK)]
    for k in range(4):
        x = -0.6 + k * 0.4
        p += Cyl(0.32, 1.1, 0.12, x, 0, color="#d86a2a" if k % 2 == 0 else "#c8b030")
        p.append(At([0.12, 0.12, 0.12], 1.22, x, 0, color=STEEL))
    p.append(At([1.6, 0.06, 0.06], 0.9, 0, 0.3, color=STEEL))
    prop("Volatile Gas Canister Rack", "A rack of four pressurised gas canisters. Shoot it and it bursts after a short fuse.", p,
         collider=[C([1.6, 1.3, 0.7])], scatter=SQ,
         breakable=brk(90, "metal", count=12, explode={"radius": 5.5, "damage": 110, "fuse": 0.6, "knockback": 14}))


# ------------------------------------------------------------------ the cloud skimmer ------------------------------------------------------------------

def skimmer():
    """A small parked cloud skimmer: a rounded cab on a long flat hull, two lift nacelles on stub wings, landing legs."""
    p = []
    for x in (-2.6, 2.6):
        for z in (-1.0, 1.0):
            p.append(At([0.16, 0.8, 0.16], 0, x, z, color=DARK))
            p.append(At([0.5, 0.06, 0.5], 0, x, z, color=STEEL))
    p.append(At([7.6, 0.7, 2.4], 0.8, 0, 0, color=HULL, mat="metal"))                  # hull
    p.append(At([6.8, 0.25, 2.0], 1.5, -0.2, 0, color=HULL2, mat="metal"))
    p.append(B([1.6, 0.6, 2.2], [4.1, 1.15, 0], color=HULL, roll=-28))                   # nose
    p.append(At([2.6, 1.0, 1.8], 1.75, 1.6, 0, color=HULL, mat="metal"))                 # cab
    p.append(B([1.0, 0.8, 1.6], [3.1, 2.2, 0], color=GLASS, roll=-35))                   # canopy
    p.append(At([2.0, 0.08, 1.6], 2.75, 1.4, 0, color=BRONZE))
    p.append(At([3.0, 0.9, 1.4], 1.75, -2.0, 0, color=HULL2, mat="metal"))               # cargo hump
    p.append(At([7.7, 0.12, 2.46], 1.18, 0, 0, color=BRONZE))                            # waist band
    for z in (-1, 1):
        p.append(At([2.0, 0.2, 2.2], 1.4, -0.6, z * 2.2, color=HULL2))                    # stub wing
        p += HCyl(1.0, 2.6, 1.6, x=-0.6, z=z * 3.4, color=HULL)                            # nacelle
        p += HCyl(0.8, 0.2, 1.6, x=-1.95, z=z * 3.4, color=DARK)
        p.append(lit(B([0.06, 0.6, 0.6], [-2.07, 1.6, z * 3.4], color=AMBER), glow("pulse", AMBER, 3.4, glow_=1.2)))
        p.append(lit(At([0.12, 0.12, 0.12], 1.5, 0.4, z * 4.0, color=RED if z < 0 else "#5aff7a"), glow("blink", RED if z < 0 else "#5aff7a", 1.5, duty=0.2, glow_=1.8)))
    p.append(At([0.7, 0.4, 0.06], 1.0, 2.6, 1.23, color=DARK))                           # hatch
    prop("Parked Cloud Skimmer", "A small parked cloud skimmer (an original design): flat hull, rounded cab, two lift nacelles. Long along X.",
         p, collider=[C([7.6, 1.5, 2.4], [0, 1.55, 0]), C([2.6, 2.0, 1.8], [1.6, 2.0, 0]), C([2.0, 0.9, 8.0], [-0.6, 1.6, 0])], scatter=SQ)


def write(out):
    folder = os.path.join(out, CAT)
    os.makedirs(folder, exist_ok=True)
    for p in PROPS:
        fn = "".join(ch for ch in p["name"] if ch.isalnum()) + ".json"
        with open(os.path.join(folder, fn), "w") as f:
            json.dump(p, f, indent=1)
            f.write("\n")
    print(f"wrote {len(PROPS)} props to {folder}")


if __name__ == "__main__":
    tanks()
    cargo()
    consoles()
    masts()
    skimmer()
    write(os.path.join(REPO, "Props"))
