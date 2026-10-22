#!/usr/bin/env python3
"""Writes the Cluttered pack's mine props (Maps/Cluttered/Props/DeepcutMine/*.json): the mining kit no global prop
covers (mine carts and rail track, a haul truck and an excavator, the headframe with its turning sheave wheels, an
inclined conveyor, a jaw crusher, an ore hopper, timber tunnel sets and mine lamps, timber stacks, explosives, a fuel
tank, a floodlight mast and a danger sign). They're pack-local (Props/README.md, "Pack props"): only the Cluttered
pack's maps see them. 11 Deepcut Mine (tools/cluttered/deepcut.py) uses them.

Usage: python3 tools/cluttered/mine_props.py <repo root>

Conventions (Props/README.md): metres, origin on the ground at the prop's centre, +X is "along", the front faces +Z.
Heavy plant (trucks, the excavator, the headframe, the crusher, the hopper, the tank) is unbreakable cover; timber,
crates and lamps break; the Explosives Crate goes off like a frag grenade when it breaks.
"""
import json, math, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/Cluttered/Props/DeepcutMine")

YELLOW = "#d9a520"      # mining plant yellow
YELLOW_D = "#a87a14"
RUST = "#7a4a2c"
ORE = "#4a4440"
TIMBER = "#6e5434"


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


def wheel_z(d, w, x, y, z, color, hub="darkgrey", anim=None):
    """A wheel with its axle along Z (the disc in the XY plane): four boxes turned 0/45/90/135 about Z, and a hub."""
    ps = [part((d, d * 0.414, w), (x, y, z), color, rotation=[0, 0, a]) for a in (0, 45, 90, 135)]
    ps.append(part((d * 0.45, d * 0.45, w + 0.04), (x, y, z), hub))
    if anim:
        for p in ps:
            p["anim"] = dict(anim)
    return ps


def drum_x(length, d, x, y, z, color, **kw):
    """A cylinder lying along X: four boxes turned 0/45/90/135 about X."""
    return [part((length, d * 0.414, d), (x, y, z), color, rotation=[a, 0, 0], **kw) for a in (0, 45, 90, 135)]


def octagon(d, h, y, color, x=0.0, z=0.0, **kw):
    """Four boxes turned 0/45/90/135 about Y: an upright octagon d across, h tall, centred at height y."""
    return [part((d, h, d * 0.414), (x, y, z), color, rotation=[0, a, 0], **kw) for a in (0, 45, 90, 135)]


PROPS = []

# ---------------------------------------------------------------- rail: mine cart and track
cart = [
    part((1.5, 0.62, 0.96), (0, 0.78, 0), RUST, material="metal"),                 # tub
    part((1.72, 0.12, 1.12), (0, 1.15, 0), "#5a3a24", material="metal"),           # rim
    part((1.1, 0.5, 0.8), (0, 0.6, 0), "#5c3a22"),                                 # tapered lower tub
    part((1.6, 0.14, 0.7), (0, 0.38, 0), "#2a2a2c"),                               # frame
    part((0.3, 0.1, 0.12), (0.92, 0.4, 0), "#2a2a2c"), part((0.3, 0.1, 0.12), (-0.92, 0.4, 0), "#2a2a2c"),  # couplers
    part((0.5, 0.2, 0.4), (-0.25, 1.26, 0.1), ORE), part((0.45, 0.24, 0.42), (0.3, 1.28, -0.12), "#55504a"),
    part((0.35, 0.16, 0.3), (0.1, 1.33, 0.25), "#3e3a36"),                         # ore heaped in the tub
    part((0.4, 0.12, 0.02), (0, 0.9, 0.49), "#c8a040"),                            # number plate
]
for x in (-0.5, 0.5):
    for z in (-0.4, 0.4):
        cart += wheel_z(0.42, 0.08, x, 0.21, z, "#26262a")
PROPS.append({"name": "Mine Cart", "description": "A rusty side-tipping mine cart heaped with ore, on its rail wheels. Waist-high cover.",
              "collision": "solid", "collider": [{"size": [1.8, 1.25, 1.12]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 3},
              "parts": cart})

track = []
for k in range(5):
    track.append(part((0.22, 0.09, 1.4), (-1.2 + k * 0.6, 0.045, 0), "#4a3a2a", material="wood"))
for z in (-0.46, 0.46):
    track.append(part((3.0, 0.1, 0.07), (0, 0.14, z), "#6a6460", material="metal"))
    track.append(part((3.0, 0.03, 0.12), (0, 0.105, z), "#4a4644"))
PROPS.append({"name": "Rail Track", "description": "A 3 m length of narrow-gauge mine track (rails on timber sleepers), running along X.",
              "collision": "cosmetic", "fadeDistance": 70, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": track})

# ---------------------------------------------------------------- heavy plant: haul truck, excavator
ht = [
    part((8.4, 0.7, 2.6), (0, 1.55, 0), "#2a2a2c", material="metal"),              # chassis
    part((2.4, 1.6, 4.2), (3.3, 2.7, 0), YELLOW, material="metal"),                # engine / front deck
    part((2.6, 0.12, 4.6), (3.3, 3.56, 0), YELLOW_D),                              # deck plate
    part((1.6, 1.4, 1.5), (3.5, 4.32, -1.35), YELLOW, material="metal"),           # cab
    part((0.04, 0.7, 1.3), (4.31, 4.5, -1.35), "glass"), part((1.4, 0.7, 0.04), (3.5, 4.5, -2.11), "glass"),
    part((1.8, 0.5, 0.12), (4.6, 2.5, 0), "#3a3a3a"),                              # grille
    part((0.12, 0.3, 0.5), (4.62, 3.1, 1.6), "#fff4c8"), part((0.12, 0.3, 0.5), (4.62, 3.1, -1.6), "#fff4c8"),  # headlights
    part((0.3, 1.6, 0.3), (2.6, 4.4, 1.6), "#2a2a2c"),                              # exhaust stack
    # the dump body: floor, sides and front wall, tipped up a little at the front, over the cab as a canopy
    part((6.6, 0.25, 4.6), (-0.9, 3.0, 0), YELLOW, material="metal", rotation=[0, 0, 6]),
    part((6.6, 1.7, 0.2), (-0.9, 3.9, 2.25), YELLOW, material="metal", rotation=[0, 0, 6]),
    part((6.6, 1.7, 0.2), (-0.9, 3.9, -2.25), YELLOW, material="metal", rotation=[0, 0, 6]),
    part((0.25, 2.0, 4.6), (2.35, 4.4, 0), YELLOW_D, material="metal"),
    part((1.6, 0.18, 4.6), (3.25, 5.45, 0), YELLOW_D, material="metal"),           # canopy
    part((5.6, 0.6, 3.8), (-1.2, 3.55, 0), ORE, rotation=[0, 0, 6]),               # load
    part((2.0, 0.5, 2.4), (-1.6, 4.0, 0.2), "#57514b"),
    part((0.4, 0.4, 3.0), (-4.3, 2.0, 0), "#2a2a2c"),                              # rear axle housing
    part((0.6, 0.12, 0.6), (3.3, 3.66, 1.9), "#e03020"),                            # beacon base
]
for x, z in ((3.3, 1.75), (3.3, -1.75)):
    ht += wheel_z(2.3, 0.75, x, 1.15, z, "rubber", hub=YELLOW_D)
for x in (-2.8,):
    for z in (1.55, 2.25, -1.55, -2.25):
        ht += wheel_z(2.3, 0.68, x, 1.15, z, "rubber", hub=YELLOW_D)
PROPS.append({"name": "Haul Truck", "description": "A big yellow mine dump truck, loaded with ore. About 9 m long, solid cover you can't climb.",
              "collision": "solid", "collider": [{"size": [8.6, 2.4, 4.6]}, {"size": [8.4, 2.8, 4.6], "offset": [-0.2, 3.8, 0]}],
              "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 6}, "parts": ht})

ex = [
    part((4.6, 0.9, 0.85), (0, 0.45, 1.25), "#2a2a2c", material="metal"), part((4.6, 0.9, 0.85), (0, 0.45, -1.25), "#2a2a2c", material="metal"),
    part((4.4, 0.08, 0.9), (0, 0.93, 1.25), "#3a3a3c"), part((4.4, 0.08, 0.9), (0, 0.93, -1.25), "#3a3a3c"),
    part((2.0, 0.5, 1.8), (0, 1.1, 0), "#333335"),                                  # undercarriage / slew ring
    part((3.4, 1.4, 2.7), (-0.4, 2.05, 0), YELLOW, material="metal"),               # house
    part((1.1, 1.2, 2.7), (-2.4, 1.95, 0), YELLOW_D, material="metal"),             # counterweight
    part((1.3, 1.5, 1.0), (0.9, 3.45, 0.85), YELLOW, material="metal"),             # cab
    part((0.04, 1.0, 0.8), (1.56, 3.55, 0.85), "glass"), part((1.1, 0.9, 0.04), (0.9, 3.55, 1.36), "glass"),
    part((0.25, 0.8, 0.25), (-1.4, 3.1, -0.8), "#2a2a2c"),                          # exhaust
    # boom (up and forward), stick (down), bucket
    part((5.0, 0.6, 0.55), (2.9, 4.0, -0.3), YELLOW, material="metal", rotation=[0, 0, 35]),
    part((3.6, 0.45, 0.45), (6.3, 3.6, -0.3), YELLOW, material="metal", rotation=[0, 0, -62]),
    part((1.2, 0.9, 1.3), (7.0, 1.05, -0.3), "#4a4a4c", material="metal", rotation=[0, 0, 20]),
    part((0.25, 1.9, 0.18), (3.6, 4.3, 0.05), "#c0c0c0", rotation=[0, 0, 35]),      # hydraulic rams
]
PROPS.append({"name": "Excavator", "description": "A tracked yellow excavator with its boom out and the bucket on the ground (+X).",
              "collision": "solid", "collider": [{"size": [4.6, 2.8, 3.4], "offset": [-0.2, 1.4, 0]}, {"size": [1.3, 1.4, 1.4], "offset": [7.0, 0.7, -0.3]}],
              "scatter": {"randomYaw": True}, "parts": ex})

# ---------------------------------------------------------------- headframe: a 12 m tower and its head (props are at most 12 m)
STEEL = "#7c4a2e"   # red-oxide painted steel
tw = []
for x in (-2.2, 2.2):
    for z in (-2.2, 2.2):
        tw.append(part((0.45, 11.9, 0.45), (x, 5.95, z), STEEL, material="metal"))
for y in (3.5, 7.5, 11.4):
    for z in (-2.2, 2.2):
        tw.append(part((4.8, 0.3, 0.3), (0, y, z), STEEL, material="metal"))
    for x in (-2.2, 2.2):
        tw.append(part((0.3, 0.3, 4.8), (x, y, 0), STEEL, material="metal"))
for z in (-2.2, 2.2):   # cross bracing
    tw.append(part((5.6, 0.18, 0.18), (0, 5.5, z), "#6a3e26", rotation=[0, 0, 45]))
    tw.append(part((5.6, 0.18, 0.18), (0, 9.5, z), "#6a3e26", rotation=[0, 0, -45]))
for zs in (1.4, -1.4):  # back stays (the struts that take the rope pull), leaning towards -X
    tw.append(part((0.45, 11.7, 0.45), (-4.25, 5.75, zs), STEEL, material="metal", rotation=[0, 0, -20]))
for xr, zr in ((1.62, 0.9), (-1.62, -0.9)):   # the ropes down the shaft (to the cage)
    tw.append(part((0.06, 10.4, 0.06), (xr, 6.8, zr), "#9a9a9a"))
PROPS.append({"name": "Headframe Tower", "description": "The lower 11.9 m of a steel mine headframe over a shaft 4 m square, with its back stays. Put a Headframe Head on top.",
              "collision": "solid",
              "collider": [{"size": [0.45, 11.9, 0.45], "offset": [x, 5.95, z]} for x in (-2.2, 2.2) for z in (-2.2, 2.2)]
              + [{"size": [0.6, 3.0, 0.5], "offset": [-5.8, 1.5, 1.4]}, {"size": [0.6, 3.0, 0.5], "offset": [-5.8, 1.5, -1.4]}],
              "scatter": {"randomYaw": False, "yawStep": 90}, "parts": tw})

hd = []
for x in (-2.2, 2.2):
    for z in (-2.2, 2.2):
        hd.append(part((0.4, 6.0, 0.4), (x * 0.82, 3.0, z * 0.82), STEEL, material="metal"))
hd.append(part((4.4, 0.3, 4.4), (0, 5.9, 0), "#5a3420", material="metal"))       # sheave deck
hd.append(part((4.8, 0.08, 0.08), (0, 7.0, 2.35), "#aaaaaa")); hd.append(part((4.8, 0.08, 0.08), (0, 7.0, -2.35), "#aaaaaa"))  # rails
for x in (-2.35, 2.35):
    hd.append(part((0.08, 1.1, 0.08), (x, 6.6, 2.35), "#aaaaaa")); hd.append(part((0.08, 1.1, 0.08), (x, 6.6, -2.35), "#aaaaaa"))
# two sheave wheels side by side, turning in opposite directions (one cage up, one down)
for z, rpm in ((0.9, 14), (-0.9, -14)):
    spin = {"type": "spin", "axis": "z", "pivot": [0, 7.6, z], "rpm": rpm, "freezeDistance": 160}
    hd += [part((3.4, 0.16, 0.12), (0, 7.6, z), "#3a3a3c", rotation=[0, 0, a], anim=dict(spin)) for a in (0, 45, 90, 135)]  # spokes
    for k in range(8):   # the rim: eight segments round the hub
        a = k * 45 + 22.5
        hd.append(part((1.36, 0.18, 0.16), (1.62 * math.cos(math.radians(a)), 7.6 + 1.62 * math.sin(math.radians(a)), z), "#2e2e30",
                       rotation=[0, 0, (a + 90) % 360], anim=dict(spin)))
    hd.append(part((0.5, 0.5, 0.3), (0, 7.6, z), "#c8a040", anim=dict(spin)))
    hd.append(part((0.6, 1.4, 0.2), (0, 6.6, z), "#4a2a18"))                   # bearing block
for xr, zr in ((1.62, 0.9), (-1.62, -0.9)):   # ropes from the sheaves down into the tower
    hd.append(part((0.06, 7.6, 0.06), (xr, 3.8, zr), "#9a9a9a"))
hd.append(part((0.25, 0.25, 0.25), (2.4, 6.3, 2.4), "#ff3020",
               anim={"type": "blink", "period": 1.6, "duty": 0.3, "color": "#ff5030", "off": "#401010", "glow": 1.8, "freezeDistance": 200}))
PROPS.append({"name": "Headframe Head", "description": "The top of a mine headframe: the sheave deck and two sheave wheels turning. Goes 11.9 m up, on a Headframe Tower.",
              "collision": "cosmetic", "fadeDistance": 320, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": hd})

# ---------------------------------------------------------------- conveyor, crusher, hopper
L, RISE = 11.0, 4.5
ang = math.degrees(math.atan2(RISE, L))
blen = math.hypot(L, RISE)
cv = []
y_at = lambda x: 1.0 + (x + L / 2) / L * RISE
cv.append(part((blen, 0.12, 1.0), (0, y_at(0) + 0.3, 0), "#26262a", rotation=[0, 0, ang]))         # belt
for z in (-0.6, 0.6):
    cv.append(part((blen, 0.32, 0.1), (0, y_at(0) + 0.2, z), "#5a5e62", material="metal", rotation=[0, 0, ang]))   # stringers
    cv.append(part((blen, 0.05, 0.05), (0, y_at(0) + 1.15, z * 1.25), YELLOW, rotation=[0, 0, ang]))              # handrails
for x in (-3.6, 0.5, 4.4):   # trestles
    h = y_at(x)
    for z in (-0.65, 0.65):
        cv.append(part((0.16, h, 0.16), (x, h / 2, z), "#5a5e62", material="metal"))
    cv.append(part((0.14, 0.14, 1.5), (x, h * 0.45, 0), "#5a5e62"))
# ore lumps riding the belt (shaking), head and tail drums turning
shake = {"type": "shake", "axis": [0.9, 0.4, 0.2], "distance": 0.02, "period": 0.11, "freezeDistance": 60}
for k in range(9):
    x = -5.2 + k * 1.25
    cv.append(part((0.34, 0.2, 0.36), (x, y_at(x) + 0.46, 0.15 * ((-1) ** k)), ORE if k % 2 else "#5c564e",
                   rotation=[0, 0, ang], anim=dict(shake, phase=round((k * 0.13) % 1, 3))))
for x in (-L / 2 + 0.2, L / 2 - 0.2):
    spin = {"type": "spin", "axis": "z", "pivot": [x, y_at(x) + 0.3, 0], "rpm": 60, "freezeDistance": 60}
    cv += [part((0.5, 0.2, 1.1), (x, y_at(x) + 0.3, 0), "#3a3a3c", rotation=[0, 0, a], anim=dict(spin)) for a in (0, 45, 90, 135)]
cv.append(part((1.1, 0.9, 0.7), (L / 2 - 0.6, y_at(L / 2) - 0.4, -0.95), "#2f5a8a", material="metal",
               anim={"type": "shake", "axis": [0.3, 1, 0.2], "distance": 0.006, "period": 0.07}))     # drive motor
PROPS.append({"name": "Inclined Conveyor", "description": "An 11 m belt conveyor climbing 4.5 m along +X on three trestles, its belt running with ore. You can walk under it.",
              "collision": "solid",
              "collider": [{"size": [0.3, y_at(x), 1.5], "offset": [x, y_at(x) / 2, 0]} for x in (-3.6, 0.5, 4.4)]
              + [{"size": [3.0, 0.5, 1.3], "offset": [-4.2, 1.2, 0]}],
              "scatter": {"randomYaw": False, "yawStep": 90}, "parts": cv})

cr = [
    part((3.6, 0.6, 3.0), (0, 0.3, 0), "#8a8680", material="concrete"),             # plinth
    part((2.6, 2.2, 2.0), (0, 1.7, 0), "#3c5a7a", material="metal"),                # crusher body
    part((2.2, 0.6, 2.4), (0, 3.0, 0), "#2f4a66", material="metal"),                # feed mouth
    part((1.6, 0.5, 1.6), (0, 3.15, 0), ORE),                                       # rock in the jaws
    part((0.9, 0.5, 0.5), (1.8, 1.0, 0), "#3a3a3c"),                                # discharge chute
    part((1.0, 0.9, 0.8), (-1.9, 1.05, 0.9), "#2f5a8a", material="metal",
         anim={"type": "shake", "axis": [0.3, 1, 0.2], "distance": 0.008, "period": 0.06}),  # motor
]
for z in (1.15, -1.15):
    cr += wheel_z(1.8, 0.18, -0.2, 1.9, z, "#2a2a2c", hub="#c8a040",
                  anim={"type": "spin", "axis": "z", "pivot": [-0.2, 1.9, z], "rpm": 90, "freezeDistance": 70})
cr.append(part((0.6, 0.6, 2.6), (0.4, 1.7, 0), "#2a3e55", anim={"type": "shake", "axis": [1, 0.4, 0], "distance": 0.015, "period": 0.12}))  # swing jaw
PROPS.append({"name": "Jaw Crusher", "description": "A blue jaw crusher on a concrete plinth: flywheels turning, the jaw shaking rock down to gravel.",
              "collision": "solid", "collider": [{"size": [3.6, 0.6, 3.0]}, {"size": [2.6, 2.9, 2.6], "offset": [0, 2.05, 0]}],
              "scatter": {"randomYaw": False, "yawStep": 90}, "parts": cr})

hp = []
for x in (-1.6, 1.6):
    for z in (-1.6, 1.6):
        hp.append(part((0.35, 5.0, 0.35), (x, 2.5, z), "#6a6e72", material="metal"))
for z in (-1.6, 1.6):
    hp.append(part((3.6, 0.15, 0.15), (0, 2.0, z), "#5a5e62", rotation=[0, 0, 30]))
hp += [
    part((4.2, 2.4, 4.2), (0, 6.4, 0), "#8a6a3a", material="metal"),                # bin
    part((4.4, 0.2, 4.4), (0, 7.7, 0), "#5a4424"),
    part((3.0, 1.0, 3.0), (0, 4.8, 0), "#7a5a30", material="metal"),                # cone
    part((1.4, 1.0, 1.4), (0, 3.9, 0), "#6a4a28", material="metal"),                # chute
    part((3.8, 0.3, 3.8), (0, 7.7, 0), ORE),                                        # ore in the bin
    part((0.8, 0.8, 0.04), (0, 6.6, 2.12), "#e0c040"),                              # warning plate
]
PROPS.append({"name": "Ore Hopper", "description": "An ore bin on four steel legs, its chute 3.4 m up: you can walk under it.",
              "collision": "solid", "collider": [{"size": [0.35, 5.0, 0.35], "offset": [x, 2.5, z]} for x in (-1.6, 1.6) for z in (-1.6, 1.6)],
              "scatter": {"randomYaw": False, "yawStep": 90}, "parts": hp})

# ---------------------------------------------------------------- tunnel timber, lamps, timber stacks
ts = [
    part((0.24, 3.0, 0.24), (-2.06, 1.5, 0), TIMBER, material="wood"), part((0.24, 3.0, 0.24), (2.06, 1.5, 0), TIMBER, material="wood"),
    part((4.6, 0.26, 0.28), (0, 3.0, 0), "#5e4628", material="wood"),               # cap
    part((0.5, 0.18, 0.22), (-1.85, 2.8, 0), "#5e4628", material="wood", rotation=[0, 0, 40]),   # knee braces
    part((0.5, 0.18, 0.22), (1.85, 2.8, 0), "#5e4628", material="wood", rotation=[0, 0, -40]),
]
PROPS.append({"name": "Timber Set", "description": "A tunnel timber set: two posts and a cap 4.4 m across and 3 m high (the tunnel runs along Z).",
              "collision": "cosmetic", "fadeDistance": 90, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": ts})

ml = [
    part((0.03, 0.5, 0.03), (0, 0.35, 0), "#1c1c1e"),                                # cable down from the cap
    part((0.16, 0.08, 0.16), (0, 0.1, 0), "#2a2a2c"),
    part((0.12, 0.14, 0.12), (0, 0.02, 0), "#ffe8a0",
         anim={"type": "flicker", "rate": 6, "duty": 0.93, "color": "#ffe8a0", "off": "#5a5030", "glow": 2.2, "freezeDistance": 90}),
    part((0.14, 0.02, 0.14), (0, -0.05, 0), "#2a2a2c"),
]
PROPS.append({"name": "Mine Lamp", "description": "A caged bulb on a short cable, flickering now and then. Its top is 0.6 m up: hang it from a ceiling with the Prop op's y.",
              "collision": "cosmetic", "fadeDistance": 120, "scatter": {"randomYaw": True},
              "breakable": {"health": 5, "breakBy": "all", "sound": "glass", "debris": {"count": 5, "size": [0.02, 0.06]}}, "parts": ml})

tb = []
for row, y in enumerate((0.14, 0.42, 0.7)):
    for k in range(4 - row):
        tb.append(part((2.6, 0.26, 0.26), (0, y, -0.45 + k * 0.3 + row * 0.15), TIMBER if (k + row) % 2 else "#806040", material="wood"))
tb += [part((0.12, 0.85, 1.25), (-1.0, 0.42, 0), "#3a2a18"), part((0.12, 0.85, 1.25), (1.0, 0.42, 0), "#3a2a18")]
PROPS.append({"name": "Timber Stack", "description": "Square mine timbers stacked between two chocks, ready for the tunnel crews. Waist-high cover.",
              "collision": "solid", "collider": [{"size": [2.6, 0.84, 1.25]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 8},
              "breakable": {"health": 130, "breakBy": "all", "sound": "wood", "debris": {"count": 16, "size": [0.08, 0.5]}}, "parts": tb})

# ---------------------------------------------------------------- explosives (they go off), danger sign
ec = [
    part((0.8, 0.55, 0.55), (0, 0.275, 0), "#8a6a3a", material="wood"),
    part((0.82, 0.06, 0.57), (0, 0.56, 0), "#5a4424"),
    part((0.5, 0.2, 0.01), (0, 0.32, 0.28), "#d02818"), part((0.5, 0.2, 0.01), (0, 0.32, -0.28), "#d02818"),
    part((0.12, 0.12, 0.012), (0.3, 0.42, 0.281), "#f0e8d0"),
]
PROPS.append({"name": "Explosives Crate", "description": "A wooden crate of blasting explosives with red labels. It goes off when it breaks: keep your distance.",
              "collision": "solid", "collider": [{"size": [0.8, 0.6, 0.55]}], "scatter": {"randomYaw": False, "yawStep": 90, "yawJitter": 10},
              "breakable": {"health": 40, "breakBy": "all", "sound": "wood", "debris": {"count": 12, "size": [0.05, 0.3], "colors": ["#8a6a3a", "#d02818"]},
                            "explode": {"radius": 6.0, "damage": 125}}, "parts": ec})

ds = [
    part((0.08, 1.8, 0.08), (0, 0.9, -0.03), "#888888"),
    part((0.9, 0.6, 0.03), (0, 1.55, 0.02), "#c82818"),
    part((0.8, 0.14, 0.035), (0, 1.62, 0.02), "#f2f2ea"),
    part((0.8, 0.08, 0.035), (0, 1.42, 0.02), "#f2f2ea"),
]
PROPS.append({"name": "Danger Sign", "description": "A red DANGER - EXPLOSIVES sign on a post (faces +Z).",
              "collision": "cosmetic", "fadeDistance": 80, "scatter": {"randomYaw": False, "yawStep": 90}, "parts": ds})

# ---------------------------------------------------------------- fuel tank, floodlight mast
ft = drum_x(6.0, 2.4, 0, 1.75, 0, "#d8d4c8", material="metal")
ft += [part((0.3, 1.0, 2.2), (x, 0.5, 0), "#4a4a4c") for x in (-2.0, 2.0)]                       # cradles
ft += [part((0.1, 2.2, 2.2), (x, 1.75, 0), "#b8b4a8") for x in (-3.02, 3.02)]                     # end caps
ft += [part((0.6, 0.3, 0.6), (1.0, 3.05, 0), "#5a5a5c"), part((0.3, 0.6, 0.3), (-1.5, 3.2, 0), "#5a5a5c"),
       part((2.6, 0.5, 0.02), (0, 1.8, 1.25), "#c82818"), part((2.2, 0.3, 0.025), (0, 1.8, 1.26), "#f2f2ea")]
PROPS.append({"name": "Fuel Tank", "description": "A 6 m diesel tank lying on two cradles, with a red DIESEL band. Solid, unbreakable cover.",
              "collision": "solid", "collider": [{"size": [6.1, 2.95, 2.3]}], "scatter": {"randomYaw": False, "yawStep": 90}, "parts": ft})

fm = [
    part((1.6, 0.25, 1.6), (0, 0.125, 0), "#4a4a4c"),
    part((0.24, 9.0, 0.24), (0, 4.6, 0), "#9a9c9e", material="metal"),
    part((2.4, 0.12, 0.12), (0, 9.1, 0), "#3a3a3c"),
]
for x in (-0.9, -0.3, 0.3, 0.9):
    fm.append(part((0.45, 0.4, 0.2), (x, 9.35, 0.08), "#1c1c1e"))
    fm.append(part((0.38, 0.32, 0.02), (x, 9.35, 0.19), "#fff2c0",
                   anim={"type": "flicker", "rate": 1.5, "duty": 0.97, "color": "#fff6d0", "off": "#806a40", "glow": 2.4, "freezeDistance": 220}))
PROPS.append({"name": "Floodlight Mast", "description": "A 9 m lighting mast with four lamps facing +Z, burning day and night.",
              "collision": "solid", "collider": [{"size": [0.3, 9.4, 0.3], "offset": [0, 4.7, 0]}], "scatter": {"randomYaw": False, "yawStep": 90}, "parts": fm})


os.makedirs(OUT, exist_ok=True)
for p in PROPS:
    d = {"format": "call-of-slop-prop/1", "name": p["name"], "category": "DeepcutMine", "description": p["description"],
         "collision": p.get("collision", "solid")}
    for k in ("collider", "fadeDistance", "scatter", "breakable"):
        if k in p:
            d[k] = p[k]
    d["parts"] = p["parts"]
    assert len(d["parts"]) <= 128, (p["name"], len(d["parts"]))
    assert len(d.get("collider", [])) <= 8, p["name"]
    for q in d["parts"]:
        assert all(0.001 <= s <= 12 for s in q["size"]), (p["name"], q)
        assert all(-360 <= a <= 360 for a in q.get("rotation", [0, 0, 0])), (p["name"], q)
        an = q.get("anim", {})
        assert 0 <= an.get("phase", 0) <= 1 and 10 <= an.get("freezeDistance", 80) <= 400, (p["name"], an)
    path = os.path.join(OUT, p["name"].replace(" ", "") + ".json")
    with open(path, "w") as f:
        f.write("{\n")
        for k in d:
            if k == "parts":
                f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(q) for q in d["parts"]) + "\n  ]\n")
            else:
                f.write(f'  "{k}": {json.dumps(d[k])},\n')
        f.write("}\n")
print(f"{len(PROPS)} mine props -> {OUT}")
