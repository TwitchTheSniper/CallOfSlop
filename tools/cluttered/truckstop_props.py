#!/usr/bin/env python3
"""Writes the Cluttered pack's truck stop props (Maps/Cluttered/Props/TruckStop/*.json): semi tractors and trailers,
the tire shop's lifts and tools, the diner booth, the motel's neon sign, ice machine and housekeeping cart, lit
billboards and a diesel dispenser, the things no global prop covers. They're pack-local (Props/README.md, "Pack
props"): only the Cluttered pack's maps see them. Truck Stop (tools/cluttered/truckstop.py) uses them.

Usage: python3 tools/cluttered/truckstop_props.py <repo root>

Conventions as in Props/: the origin is on the ground at the centre, long things run along +X, things that face a way
face +Z. Tractors and trailers point their front (the nose, the kingpin end) to +X, so a tractor coupled to a trailer
stands at +X of it. Trailers and tractors are solid, unbreakable cover; their colliders start 0.4 m up, so grenades
roll under a trailer but nobody fits under it, and their tops can be stood on (a flatbed deck is a mantle, 1.4 m).
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/Cluttered/Props/TruckStop")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def box(size, offset):
    return {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset]}


def wheel(x, z, d=1.0, w=0.32, y=None):
    """A wheel facing +-Z at (x, z): two boxes turned 0 and 45 degrees about Z (an eight-point disc) and a hub."""
    y = d / 2 if y is None else y
    return [part((d * 0.83, d * 0.83, w), (x, y, z), "rubber"), part((d * 0.83, d * 0.83, w), (x, y, z), "rubber", rotation=[0, 0, 45]),
            part((d * 0.42, d * 0.42, w + 0.02), (x, y, z), "steel")]


PROPS = []


def add(name, desc, parts, collider=None, breakable=None, scatter=None, collision="solid", category="Truck Stop"):
    d = {"format": "call-of-slop-prop/1", "name": name, "category": category, "description": desc, "author": "Call of Slop", "collision": collision}
    if collider is not None:
        d["collider"] = collider
    d["scatter"] = scatter or {"randomYaw": False, "yawStep": 90, "scale": [1, 1]}
    if breakable:
        d["breakable"] = breakable
    d["parts"] = parts
    PROPS.append(d)


# ---------------------------------------------------------------- semi tractors (nose at +X, 7 m long)
def tractor(body, trim):
    ps = []
    ps.append(part((6.9, 0.3, 1.0), (-0.1, 0.78, 0), "darkgrey", material="metal"))                      # chassis rails
    ps.append(part((2.0, 1.05, 2.2), (2.55, 1.78, 0), body, material="metal"))                             # hood
    ps.append(part((1.9, 0.12, 2.24), (2.55, 2.32, 0), trim))                                              # hood top stripe
    ps.append(part((0.08, 0.95, 1.15), (3.58, 1.75, 0), "chrome"))                                         # grille
    ps.append(part((0.28, 0.32, 2.46), (3.6, 0.9, 0), "chrome"))                                           # bumper
    for z in (-0.82, 0.82):
        ps.append(part((0.06, 0.16, 0.34), (3.62, 1.5, z), "#f4ecc0"))                                     # headlights
    ps.append(part((1.75, 2.05, 2.44), (0.7, 2.33, 0), body, material="metal"))                            # cab
    ps.append(part((1.65, 2.35, 2.44), (-1.0, 2.48, 0), body, material="metal"))                           # sleeper
    ps.append(part((3.42, 0.14, 2.46), (-0.15, 1.36, 0), trim))                                            # cab skirt stripe
    ps.append(part((0.06, 0.78, 2.1), (1.6, 2.86, 0), "glass"))                                            # windscreen
    for z in (-1.235, 1.235):
        ps.append(part((0.85, 0.62, 0.03), (0.95, 2.86, z), "glass"))                                      # door windows
        ps.append(part((0.05, 0.9, 0.04), (1.0, 1.75, z * 1.01), "chrome"))                                # grab bar
        ps.append(part((1.15, 0.55, 0.5), (0.55, 1.0, z * 0.9), "chrome", material="metal"))               # fuel tanks
        ps.append(part((0.18, 2.6, 0.18), (-0.05, 2.6, z * 1.07), "chrome"))                               # stacks
        ps.append(part((0.3, 0.45, 0.05), (1.75, 2.75, z * 1.08), "darkgrey"))                             # mirrors
    for z in (-0.9, -0.3, 0.3, 0.9):
        ps.append(part((0.12, 0.08, 0.12), (1.5, 3.4, z), "amber"))                                        # marker lights
    ps.append(part((0.9, 0.5, 2.3), (-0.6, 3.82, 0), body, material="metal"))                              # roof fairing
    ps.append(part((1.1, 0.12, 1.2), (-2.35, 1.26, 0), "black"))                                           # fifth wheel
    ps.append(part((2.5, 0.12, 2.52), (-2.25, 1.6, 0), "darkgrey"))                                        # rear fenders
    for z in (-1.0, 1.0):
        ps.append(part((0.05, 0.6, 0.5), (-3.45, 0.75, z), "black"))                                       # mud flaps
    for x in (2.55,):
        for z in (-1.0, 1.0):
            ps += wheel(x, z)
    for x in (-1.6, -2.9):
        for z in (-0.95, 0.95):
            ps += wheel(x, z, w=0.5)
    col = [box((2.0, 2.3, 2.5), (2.65, 1.15, 0)),                 # hood (stand on it at 2.3)
           box((3.45, 3.3, 2.5), (-0.1, 2.05, 0)),                # cab and sleeper, from 0.4 m up
           box((1.75, 1.0, 2.5), (-2.65, 0.9, 0))]                # rear deck over the drive wheels
    return ps, col


for nm, body, trim in (("Semi Tractor Red", "#9c2a22", "chrome"), ("Semi Tractor Blue", "#284a7a", "#d8d8d0"), ("Semi Tractor White", "#d8d6cc", "#b02a22")):
    ps, col = tractor(body, trim)
    add(nm, "A long-nose semi tractor with a sleeper cab, chrome stacks and tanks (nose to +X). Unbreakable.", ps, col)


# ---------------------------------------------------------------- trailers (kingpin end at +X, 11.8 m long)
def undercarriage(L, deck_y):
    ps = [part((L - 0.6, 0.25, 0.95), (0, deck_y - 0.3, 0), "darkgrey", material="metal")]
    for x in (-L / 2 + 1.7, -L / 2 + 2.8):
        for z in (-0.95, 0.95):
            ps += wheel(x, z, w=0.5)
    for z in (-0.8, 0.8):
        ps.append(part((0.12, deck_y - 0.3, 0.12), (L / 2 - 2.4, (deck_y - 0.3) / 2, z), "darkgrey"))     # landing gear
        ps.append(part((0.3, 0.06, 0.3), (L / 2 - 2.4, 0.03, z), "black"))
        ps.append(part((0.05, 0.55, 0.5), (-L / 2 + 1.05, 0.6, z * 1.2), "black"))                        # mud flaps
    ps.append(part((0.12, 0.12, 2.3), (-L / 2 + 0.25, 0.55, 0), "#c83a2a"))                                # under-ride bar
    return ps


def box_trailer(name, body, stripe, logo, reefer=False):
    L = 11.2 if reefer else 11.8
    x0 = -0.3 if reefer else 0.0
    ps = [part((L, 2.6, 2.5), (x0, 2.4, 0), body, material="metal"),
          part((L + 0.02, 0.22, 2.52), (x0, 1.42, 0), stripe),
          part((L, 0.06, 2.42), (x0, 3.73, 0), "lightgrey")]
    for z in (-1.255, 1.255):
        ps.append(part((3.6, 1.1, 0.02), (x0 + 1.0, 2.6, z), logo))
        ps.append(part((1.4, 0.3, 0.025), (x0 + 1.0, 2.6, z), "white"))
        for k in range(9):
            ps.append(part((0.05, 2.5, 0.025), (x0 - L / 2 + 0.6 + k * (L - 1.2) / 8, 2.4, z * 1.002), "lightgrey"))  # ribs
    ps.append(part((0.04, 2.5, 0.04), (x0 - L / 2 - 0.02, 2.4, 0), "darkgrey"))                            # rear door seam
    for z in (-0.6, 0.6):
        ps.append(part((0.05, 2.3, 0.05), (x0 - L / 2 - 0.03, 2.4, z), "chrome"))                         # lock bars
    for z in (-1.15, 1.15):
        ps.append(part((0.04, 0.12, 0.12), (x0 - L / 2 - 0.02, 1.35, z), "red"))                          # tail lights
    if reefer:
        ps.append(part((0.55, 1.7, 2.1), (x0 + L / 2 + 0.27, 2.75, 0), "lightgrey", material="metal"))    # fridge unit
        ps.append(part((0.04, 0.9, 1.4), (x0 + L / 2 + 0.56, 2.85, 0), "darkgrey"))
    ps += undercarriage(11.8, 1.1)
    col = [box((11.8, 3.36, 2.5), (0, 2.08, 0))]
    desc = "A refrigerated box trailer, the fridge unit at the front (+X)." if reefer else "A dry van box trailer, kingpin end at +X."
    add(name, desc + " Roof 3.76 m up. Unbreakable.", ps, col)


box_trailer("Box Trailer", "#e2e0d8", "#b23a2c", "#2a5aa0")
box_trailer("Box Trailer Rusty", "#9a8a72", "#6a3a28", "#5a4a3a")
box_trailer("Reefer Trailer", "#ecebe6", "#2a6a9a", "#3a8a5a", reefer=True)


def flatbed(name, cargo, cargo_col):
    ps = [part((11.8, 0.2, 2.5), (0, 1.3, 0), "#6a5238", material="wood"),
          part((11.82, 0.14, 2.52), (0, 1.15, 0), "darkgrey", material="metal")]
    for k in range(10):
        for z in (-1.24, 1.24):
            ps.append(part((0.08, 0.16, 0.06), (-5.4 + k * 1.2, 1.38, z), "steel"))                       # stake pockets
    ps += undercarriage(11.8, 1.1)
    ps += cargo
    col = [box((11.8, 1.0, 2.5), (0, 0.9, 0))] + cargo_col
    add(name, "A flatbed trailer (deck 1.4 m up, a mantle from the ground), kingpin end at +X. Unbreakable.", ps, col)


lumber = []
lcol = []
for cx in (-3.7, 0.0, 3.7):
    lumber.append(part((2.8, 1.05, 2.3), (cx, 1.93, 0), "lightwood", material="wood"))
    for dx in (-1.0, 1.0):
        lumber.append(part((0.06, 1.08, 2.34), (cx + dx, 1.93, 0), "#d8b040"))                             # straps
    lumber.append(part((2.82, 0.08, 2.32), (cx, 1.46, 0), "darkwood"))                                     # dunnage
    lcol.append(box((2.8, 1.05, 2.3), (cx, 1.93, 0)))
flatbed("Flatbed Lumber", lumber, lcol)
pipes = []
for k in range(4):
    for j in range(2):
        pipes.append(part((8.0, 0.5, 0.5), (-0.6, 1.66 + j * 0.48, -0.85 + k * 0.56), "rust" if (k + j) % 2 else "#5a5a58", material="metal"))
for dx in (-3.6, 0.0, 3.0):
    pipes.append(part((0.06, 1.0, 2.36), (dx, 1.9, 0), "#d8b040"))
flatbed("Flatbed Pipes", pipes, [box((8.0, 0.98, 2.3), (-0.6, 1.9, 0))])
mach = [part((2.4, 1.9, 2.2), (-2.6, 2.35, 0), "#c89a30", material="metal"),
        part((1.0, 0.7, 1.6), (-2.6, 3.65, 0), "#c89a30", material="metal"),
        part((0.12, 1.2, 0.12), (-2.0, 4.0, 0.5), "darkgrey"),
        part((2.0, 1.2, 2.0), (2.4, 2.0, 0), "pallet", material="wood"),
        part((2.02, 0.06, 2.02), (2.4, 2.62, 0), "tape")]
flatbed("Flatbed Machinery", mach, [box((2.4, 1.9, 2.2), (-2.6, 2.35, 0)), box((2.0, 1.2, 2.0), (2.4, 2.0, 0))])


# ---------------------------------------------------------------- tire shop
def lift(raised):
    ps = []
    for z in (-1.75, 1.75):
        ps.append(part((0.36, 3.5, 0.36), (0, 1.75, z), "#2a5aa0", material="metal"))
        ps.append(part((0.7, 0.04, 0.7), (0, 0.02, z), "darkgrey"))
        ps.append(part((0.18, 3.3, 0.12), (0, 1.75, z - 0.25 if z > 0 else z + 0.25), "darkgrey"))     # carriage track
    ps.append(part((0.25, 0.25, 3.86), (0, 3.62, 0), "#2a5aa0", material="metal"))                         # overhead bar
    ps.append(part((0.25, 0.4, 0.15), (0.25, 1.3, -1.95), "yellow"))                                       # control box
    ay = 1.85 if raised else 0.14
    for z in (-1.75, 1.75):
        for side in (-1, 1):
            ps.append(part((1.5, 0.1, 0.16), (side * 0.75, ay, z * 0.8), "darkgrey", rotation=[0, side * (-18 if z > 0 else 18), 0]))
    col = [box((0.4, 3.5, 0.4), (0, 1.75, -1.75)), box((0.4, 3.5, 0.4), (0, 1.75, 1.75))]
    if raised:
        car = "#6a7a8a"
        ps += [part((4.4, 0.75, 1.8), (0, 2.38, 0), car, material="metal"),
               part((2.3, 0.6, 1.62), (-0.2, 3.05, 0), car, material="metal"),
               part((2.0, 0.42, 1.64), (-0.2, 3.06, 0), "glass"),
               part((4.2, 0.08, 1.6), (0, 2.0, 0), "darkgrey")]
        for x in (-1.4, 1.4):
            for z in (-0.82, 0.82):
                ps += wheel(x, z, d=0.66, w=0.22, y=2.05)
        col.append(box((4.4, 1.5, 1.85), (0, 2.65, 0)))      # the car: from 1.9 m up, so you walk under it
    # a lift is long along Z (the car drives in along X between the posts)
    add("Car Lift Raised" if raised else "Car Lift", "A two-post car lift" + (" with a car up on it (walk under it)." if raised else ", arms down.") + " Unbreakable.", ps, col)


lift(False)
lift(True)

tc = [part((1.3, 1.0, 0.6), (0, 0.6, 0), "#b02a22", material="metal"),
      part((1.3, 0.45, 0.6), (0, 1.35, 0), "#b02a22", material="metal"),
      part((1.32, 0.04, 0.62), (0, 1.1, 0), "darkgrey")]
for k in range(6):
    tc.append(part((1.1, 0.02, 0.02), (0, 0.28 + k * 0.16, 0.305), "chrome"))
for k in range(2):
    tc.append(part((1.1, 0.02, 0.02), (0, 1.25 + k * 0.17, 0.305), "chrome"))
for x in (-0.55, 0.55):
    for z in (-0.22, 0.22):
        tc.append(part((0.1, 0.1, 0.1), (x, 0.05, z), "black"))
tc.append(part((0.5, 0.15, 0.3), (0.2, 1.65, 0), "darkgrey"))      # toolbox on top
tc.append(part((0.04, 0.04, 0.5), (-0.68, 1.0, 0), "chrome"))      # handle
add("Tool Chest", "A red rolling tool chest with drawers and a tool box on top. Breaks (heavy metal).", tc,
    breakable={"health": 260, "breakBy": "all", "sound": "metal", "debris": {"count": 14, "size": [0.05, 0.25], "colors": ["#b02a22", "chrome", "darkgrey"]}})

tch = [part((0.9, 0.8, 0.8), (0, 0.4, 0), "#c83a2a", material="metal"),
       part((0.75, 0.08, 0.75), (0, 0.84, 0), "darkgrey"),
       part((0.3, 0.06, 0.3), (0, 0.9, 0), "chrome"),
       part((0.16, 1.1, 0.16), (0, 1.35, -0.32), "darkgrey"),
       part((0.6, 0.1, 0.12), (0, 1.85, -0.1), "darkgrey"),
       part((0.08, 0.4, 0.08), (0, 1.62, 0.05), "chrome"),
       part((0.35, 0.05, 0.2), (0.25, 0.03, 0.45), "black")]
add("Tire Changer", "A tire changing machine: turntable, column and mount head. Breaks (heavy metal).", tch,
    breakable={"health": 220, "breakBy": "all", "sound": "metal"})

ac = [part((1.3, 0.5, 0.5), (0, 0.45, 0), "#c83a2a", material="metal"),
      part((0.5, 0.4, 0.4), (0.1, 0.9, 0), "darkgrey", material="metal"),
      part((0.3, 0.3, 0.06), (0.1, 0.9, 0.23), "black"),
      part((0.08, 0.08, 0.08), (0.45, 0.78, 0.0), "chrome")]
ac += wheel(-0.45, -0.27, d=0.3, w=0.08) + wheel(-0.45, 0.27, d=0.3, w=0.08)
ac.append(part((0.12, 0.2, 0.4), (0.55, 0.1, 0), "darkgrey"))
add("Air Compressor", "A wheeled air compressor: red tank, motor and gauge. Breaks (thin metal).", ac,
    breakable={"health": 150, "breakBy": "all", "sound": "metal"})

tr = [part((2.4, 0.06, 0.6), (0, y, 0), "darkgrey") for y in (0.25, 1.05, 1.85)]
for x in (-1.17, 1.17):
    for z in (-0.27, 0.27):
        tr.append(part((0.06, 2.0, 0.06), (x, 1.0, z), "darkgrey"))
for row, y in enumerate((0.62, 1.42)):
    for k in range(5):
        x = -0.96 + k * 0.48
        tr.append(part((0.24, 0.72, 0.58), (x, y, 0), "rubber"))
        tr.append(part((0.25, 0.3, 0.3), (x, y, 0), "darkgrey"))
add("Tire Rack", "A steel rack of truck tires, two shelves. Unbreakable.", tr, collider=[box((2.4, 2.0, 0.6), (0, 1.0, 0))])


# ---------------------------------------------------------------- diner, motel
bs = []
for z in (-0.82, 0.82):
    s = 1 if z > 0 else -1
    bs.append(part((1.2, 0.45, 0.55), (0, 0.23, z), "#9c2a22", material="none"))                            # seat base
    bs.append(part((1.2, 0.1, 0.5), (0, 0.5, z - s * 0.02), "#c8382c"))                                     # cushion
    bs.append(part((1.2, 0.75, 0.16), (0, 0.85, z + s * 0.22), "#c8382c"))                                  # back
    bs.append(part((1.22, 0.06, 0.18), (0, 1.24, z + s * 0.22), "chrome"))
bs.append(part((0.75, 0.04, 0.9), (0, 0.76, 0), "#e8e0c8"))                                                 # table top
bs.append(part((0.77, 0.03, 0.92), (0, 0.735, 0), "chrome"))
bs.append(part((0.1, 0.72, 0.1), (0, 0.37, 0), "chrome"))
bs.append(part((0.12, 0.14, 0.08), (0.2, 0.85, 0.05), "#c8382c"))                                           # ketchup
bs.append(part((0.06, 0.12, 0.06), (0.28, 0.84, -0.05), "#e8c030"))                                         # mustard
add("Diner Booth", "A diner booth: two red vinyl bench seats and a table between them (the booth runs along Z). Breaks.", bs,
    collider=[box((1.2, 1.25, 0.55), (0, 0.625, -0.82)), box((1.2, 1.25, 0.55), (0, 0.625, 0.82)), box((0.75, 0.78, 0.9), (0, 0.39, 0))],
    breakable={"health": 120, "breakBy": "all", "sound": "soft", "debris": {"count": 10, "size": [0.05, 0.3], "colors": ["#c8382c", "chrome", "#e8e0c8"]}})

im = [part((0.9, 1.75, 0.75), (0, 0.9, 0), "#e6e6e0", material="metal"),
      part((0.7, 0.35, 0.02), (0, 1.45, 0.38), "#2a6ab0", anim={"type": "pulse", "period": 4.0, "color": "#6ab8ff", "off": "#1a4a80", "glow": 0.7}),
      part((0.55, 0.4, 0.03), (0, 0.6, 0.38), "darkgrey"),
      part((0.4, 0.06, 0.05), (0, 0.85, 0.4), "chrome"),
      part((0.92, 0.06, 0.77), (0, 1.78, 0), "lightgrey")]
add("Ice Machine", "A motel ice machine with a glowing blue ICE panel. Breaks (heavy metal).", im,
    collider=[box((0.9, 1.8, 0.75), (0, 0.9, 0))],
    breakable={"health": 240, "breakBy": "all", "sound": "metal", "debris": {"count": 12, "size": [0.05, 0.2], "colors": ["#e6e6e0", "#6ab8ff", "white"]}})

hc = [part((1.2, 0.9, 0.5), (0, 0.55, 0), "#d8d4c8", material="none"),
      part((1.2, 0.04, 0.52), (0, 1.02, 0), "darkgrey"),
      part((0.5, 0.3, 0.45), (-0.3, 1.2, 0), "white"),
      part((0.4, 0.25, 0.4), (0.3, 1.17, 0), "#c8d8e8"),
      part((0.35, 0.75, 0.45), (0.78, 0.6, 0), "#3a3a40"),                                                 # trash bag
      part((0.04, 0.04, 0.5), (-0.62, 1.0, 0), "chrome"),
      part((0.06, 0.12, 0.06), (-0.35, 0.95, 0.27), "#40a0e0"), part((0.06, 0.16, 0.06), (-0.2, 0.97, 0.27), "#e0e040")]
for x in (-0.5, 0.5):
    for z in (-0.2, 0.2):
        hc.append(part((0.1, 0.1, 0.1), (x, 0.05, z), "black"))
add("Housekeeping Cart", "A motel housekeeping cart with towels, linen and a trash bag. Breaks.", hc,
    breakable={"health": 60, "breakBy": "all", "sound": "soft", "debris": {"count": 10, "size": [0.05, 0.25], "colors": ["white", "#d8d4c8", "#3a3a40"]}})

pl = []
for z in (-0.3, 0.3):
    pl.append(part((0.05, 1.0, 0.05), (0, 0.5, z), "chrome"))
    pl.append(part((0.6, 0.05, 0.05), (0.3, 1.0, z), "chrome"))
for k in range(3):
    pl.append(part((0.06, 0.03, 0.6), (0, 0.1 + k * 0.3, 0), "chrome"))
add("Pool Ladder", "Chrome pool ladder rails (cosmetic): put it at a pool's edge.", pl, collision="cosmetic")

dd = [part((0.75, 2.1, 0.55), (0, 1.05, 0), "#e8e6e0", material="metal"),
      part((0.77, 0.4, 0.57), (0, 1.95, 0), "#2a7a3a", material="metal"),
      part((0.55, 0.25, 0.02), (0, 1.5, 0.285), "#203a28", anim={"type": "pulse", "period": 3.4, "color": "#9cff7a", "off": "#1a3a20", "glow": 0.8}),
      part((0.55, 0.25, 0.02), (0, 1.5, -0.285), "#203a28", anim={"type": "pulse", "period": 3.4, "phase": 0.5, "color": "#9cff7a", "off": "#1a3a20", "glow": 0.8}),
      part((0.4, 0.15, 0.02), (0, 1.95, 0.29), "yellow"), part((0.4, 0.15, 0.02), (0, 1.95, -0.29), "yellow"),
      part((0.1, 0.3, 0.16), (0.42, 1.1, 0), "black"), part((0.1, 0.3, 0.16), (-0.42, 1.1, 0), "black"),
      part((0.05, 1.0, 0.05), (0.46, 0.55, 0.12), "rubber"), part((0.05, 1.0, 0.05), (-0.46, 0.55, -0.12), "rubber"),
      part((0.85, 0.12, 0.65), (0, 0.06, 0), "darkgrey", material="concrete")]
add("Diesel Dispenser", "A tall diesel dispenser for trucks, green panel and lit price display (faces +Z and -Z). Unbreakable.", dd,
    collider=[box((0.8, 2.1, 0.6), (0, 1.05, 0))])


# ---------------------------------------------------------------- signs
NEON_PINK = "#ff4aa0"
ms = []
for x in (-1.7, 1.7):
    ms.append(part((0.32, 7.6, 0.32), (x, 3.8, 0), "#5a6a6a", material="metal"))
    ms.append(part((0.8, 0.5, 0.8), (x, 0.25, 0), "concrete", material="concrete"))
ms.append(part((4.8, 1.6, 0.4), (0, 6.9, 0), "#1f4a50", material="metal"))                                   # MOTEL board
ms.append(part((4.9, 0.08, 0.44), (0, 7.74, 0), NEON_PINK, anim={"type": "flicker", "rate": 6, "duty": 0.93, "color": NEON_PINK, "off": "#40102a", "glow": 1.0, "freezeDistance": 160}))
ms.append(part((4.9, 0.08, 0.44), (0, 6.06, 0), NEON_PINK, anim={"type": "flicker", "rate": 6, "duty": 0.93, "color": NEON_PINK, "off": "#40102a", "glow": 1.0, "freezeDistance": 160}))
for k in range(5):  # the letters M O T E L light up one by one, then all together
    x = -1.8 + k * 0.9
    on = list(range(k, 5)) + [5, 6]
    for z in (-0.215, 0.215):
        ms.append(part((0.62, 0.95, 0.03), (x, 6.9, z), "#ffd060",
                       anim={"type": "cycle", "period": 4.2, "steps": 8, "on": on, "color": "#ffe090", "off": "#4a3a20", "glow": 1.3, "randomPhase": False, "freezeDistance": 160}))
ms.append(part((3.2, 0.42, 0.3), (-0.2, 5.3, 0), "#c83a2a", material="metal"))                                # arrow shaft
ms.append(part((0.9, 0.9, 0.3), (1.55, 5.3, 0), "#c83a2a", rotation=[0, 0, 45]))                              # arrow head
for k in range(7):  # chasing bulbs along the arrow
    for z in (-0.16, 0.16):
        ms.append(part((0.14, 0.14, 0.03), (-1.6 + k * 0.48, 5.3, z), "#fff0b0",
                       anim={"type": "cycle", "period": 1.4, "steps": 7, "on": [k], "color": "#fff4c0", "off": "#5a4a30", "glow": 1.6, "randomPhase": False, "freezeDistance": 160}))
ms.append(part((2.8, 0.6, 0.24), (0, 4.4, 0), "#f0ece0"))                                                     # VACANCY board
for z in (-0.125, 0.125):
    ms.append(part((2.0, 0.32, 0.02), (0.3, 4.4, z), NEON_PINK, anim={"type": "blink", "period": 2.0, "duty": 0.8, "color": NEON_PINK, "off": "#5a2a40", "glow": 1.2, "freezeDistance": 160}))
    ms.append(part((0.5, 0.32, 0.02), (-1.0, 4.4, z), "#ff3030", anim={"type": "flicker", "rate": 4, "duty": 0.35, "color": "#ff4040", "off": "#3a1010", "glow": 1.2, "freezeDistance": 160}))  # the NO
ms.append(part((0.7, 0.7, 0.2), (0, 8.05, 0), "#ffd030", rotation=[0, 0, 45], anim={"type": "blink", "period": 1.1, "duty": 0.5, "color": "#ffe060", "off": "#5a4a10", "glow": 1.6, "freezeDistance": 160}))
add("Motel Neon Sign", "A tall motel pole sign: MOTEL lit letter by letter, a chasing arrow, a VACANCY panel with a flickering NO. Unbreakable.", ms,
    collider=[box((0.4, 7.6, 0.4), (-1.7, 3.8, 0)), box((0.4, 7.6, 0.4), (1.7, 3.8, 0))])


def billboard(name, bg, ad):
    ps = []
    for x in (-3.2, 3.2):
        ps.append(part((0.45, 5.6, 0.45), (x, 2.8, 0), "#6a6660", material="metal"))
        ps.append(part((0.9, 0.4, 0.9), (x, 0.2, 0), "concrete", material="concrete"))
    ps.append(part((8.4, 3.3, 0.3), (0, 7.2, 0), "#3a3a3a", material="metal"))
    ps.append(part((8.0, 2.9, 0.04), (0, 7.2, 0.17), bg))
    ps.append(part((8.0, 2.9, 0.04), (0, 7.2, -0.17), "#5a5a58"))
    ps += [part(s, (o[0], o[1], 0.2), c) for s, o, c in ad]
    ps.append(part((8.4, 0.08, 0.9), (0, 5.5, 0.55), "darkgrey", material="grid"))                            # catwalk
    for k in range(3):
        x = -2.8 + k * 2.8
        ps.append(part((0.08, 0.08, 1.2), (x, 9.0, 0.6), "darkgrey"))
        ps.append(part((0.4, 0.22, 0.3), (x, 8.95, 1.15), "#fff4d0",
                       anim={"type": "flicker", "rate": 3, "duty": 0.95 if k != 1 else 0.7, "color": "#fff4d0", "off": "#4a4636", "glow": 2.0, "freezeDistance": 160}))
    add(name, "A big roadside billboard on two legs with a catwalk and three flickering lamps (faces +Z). Unbreakable.", ps,
        collider=[box((0.5, 5.6, 0.5), (-3.2, 2.8, 0)), box((0.5, 5.6, 0.5), (3.2, 2.8, 0))])


billboard("Billboard Diner", "#d8a030", [((2.6, 2.2, 0.03), (-2.2, 7.2), "#8a4a20"), ((2.8, 0.5, 0.03), (-2.2, 7.6), "#e0b060"),
                                         ((3.6, 0.7, 0.03), (1.8, 7.9), "#c02a20"), ((3.6, 0.4, 0.03), (1.8, 6.9), "#f4ecd8"), ((2.2, 0.3, 0.03), (1.8, 6.3), "#c02a20")])
billboard("Billboard Motel", "#1f3e6a", [((1.6, 1.6, 0.03), (-2.6, 7.4), "#ffd030"), ((4.2, 0.8, 0.03), (1.4, 7.7), "#f4ecd8"),
                                         ((4.2, 0.35, 0.03), (1.4, 6.8), "#ff4aa0"), ((7.6, 0.18, 0.03), (0.0, 6.0), "#ffd030")])

os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT):
    if f.endswith(".json"):
        os.remove(os.path.join(OUT, f))
for p in PROPS:
    fn = p["name"].replace(" ", "") + ".json"
    with open(os.path.join(OUT, fn), "w") as f:
        f.write(json.dumps(p, indent=1) + "\n")
print(f"wrote {len(PROPS)} props to {OUT}")
