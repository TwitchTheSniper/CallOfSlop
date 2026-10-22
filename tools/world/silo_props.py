#!/usr/bin/env python3
"""Writes the Facility props (Props/Facility/*.json): the inside of a Cold War missile complex, used by the World Tour
map "Silo" (Maps/World/03-Silo.json) and its underground buildings, and reusable on any bunker, lab or power plant map.

    python3 tools/world/silo_props.py [repo root]

Consoles, wall screens and mainframes for a launch control centre; a reactor vessel, a turbine, pumps and a control
desk for a power plant; decontamination, armoury and comms furniture; tunnel lamps, cable trays and pipe runs for the
corridors; a blast door portal and leaf; a propellant tank, a ventilation fan, a guard booth and a restricted-area sign.
Props face +Z; wall-mounted ones have their back at -Z (lift them with the Prop op's y).
"""
import json, math, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(REPO, "Props", "Facility")

GREY = "#6c7074"; DARK = "#3a3d40"; PANEL = "#8a8e86"; BEIGE = "#b8b29a"; OLIVE = "#5a6048"; STEEL = "#7e8286"
BLACK = "#1a1c1e"; SCREEN = "#0c2a1a"; GREEN = "#40ff70"; RED = "#ff3030"; AMBER = "#ffb020"; BLUE = "#40b0ff"
YELLOW = "#e8c020"; WHITE = "#e8e8dc"; RUST = "#7a4a2a"; CONC = "#8a8a84"


def b(size, offset=None, color=GREY, material=None, rot=None, anim=None):
    p = {"size": [round(v, 3) for v in size]}
    p["offset"] = [round(v, 3) for v in (offset if offset is not None else [0, size[1] / 2, 0])]
    p["color"] = color
    if material:
        p["material"] = material
    if rot:
        p["rotation"] = [round(v, 2) for v in rot]
    if anim:
        p["anim"] = anim
    return p


def blink(period, duty=0.5, color=None, glow=0.9, phase=None):
    a = {"type": "blink", "period": period, "duty": duty, "glow": glow}
    if color:
        a["color"] = color
    if phase is not None:
        a["phase"] = phase
    return a


def flicker(rate=6, duty=0.9, color=None, glow=1.0):
    a = {"type": "flicker", "rate": rate, "duty": duty, "glow": glow}
    if color:
        a["color"] = color
    return a


def pulse(period, color=None, glow=1.0):
    a = {"type": "pulse", "period": period, "glow": glow}
    if color:
        a["color"] = color
    return a


def octagon(d, h, y0, color, material=None, x=0.0, z=0.0, lying=None):
    """Four boxes making an octagonal prism d across (standing, or lying along x / z)."""
    parts = []
    for k in range(4):
        a = k * 45
        if lying == "x":
            parts.append(b([h, d, 0.414 * d], [x, y0, z], color, material, rot=[a, 0, 0]))
        elif lying == "z":
            parts.append(b([0.414 * d, d, h], [x, y0, z], color, material, rot=[0, 0, a]))
        else:
            parts.append(b([d, h, 0.414 * d], [x, y0 + h / 2, z], color, material, rot=[0, a, 0]))
    return parts


PROPS = []


def prop(name, desc, parts, solid=True, collider=None, breakable=None, scatter=None, fade=None):
    d = {"format": "call-of-slop-prop/1", "name": name, "category": "Facility", "description": desc,
         "collision": "solid" if solid else "cosmetic"}
    if solid and collider is not None:
        d["collider"] = collider
    d["scatter"] = scatter or {"randomYaw": False, "yawStep": 90}
    if breakable:
        d["breakable"] = breakable
    if not solid and fade:
        d["fadeDistance"] = fade
    d["parts"] = parts
    assert len(parts) <= 128, name
    PROPS.append(d)


def metal(health, count=12):
    return {"health": health, "breakBy": "all", "sound": "metal", "debris": {"count": count, "size": [0.05, 0.25]}}


def col(size, offset=None):
    c = {"size": [round(v, 3) for v in size]}
    if offset is not None:
        c["offset"] = [round(v, 3) for v in offset]
    return c


# ------------------------------------------------------------------------------------------- launch control
parts = [b([2.4, 0.78, 0.9], [0, 0.39, 0], BEIGE, "metal"),                      # desk body
         b([2.44, 0.05, 0.98], [0, 0.8, 0.02], DARK),                             # desk top
         b([2.4, 0.9, 0.42], [0, 1.25, -0.3], PANEL, "metal", rot=[-18, 0, 0]),   # sloped upper panel
         b([2.4, 0.18, 0.5], [0, 1.78, -0.42], BEIGE, "metal"),                   # top shelf
         b([2.2, 0.06, 0.02], [0, 0.72, 0.46], DARK)]                             # kick strip
for i, x in enumerate((-0.75, 0.0, 0.75)):                                         # three CRTs in the panel
    parts.append(b([0.52, 0.4, 0.06], [x, 1.32, -0.12], BLACK, rot=[-18, 0, 0]))
    parts.append(b([0.44, 0.32, 0.02], [x, 1.33, -0.09], SCREEN, rot=[-18, 0, 0], anim=flicker(4 + i, 0.93, "#50ff90", 1.1)))
for i in range(10):                                                                 # lamps along the shelf front
    x = -1.05 + i * 0.233
    parts.append(b([0.07, 0.05, 0.02], [x, 1.76, -0.16], DARK,
                   anim=blink(0.7 + 0.37 * i, 0.5, [RED, GREEN, AMBER, BLUE][i % 4], 1.0)))
for i in range(12):                                                                 # switches on the desk lip
    x = -1.1 + i * 0.2
    parts.append(b([0.05, 0.04, 0.08], [x, 0.85, 0.3], [WHITE, RED, BLACK][i % 3]))
parts.append(b([0.3, 0.03, 0.22], [0.75, 0.84, 0.15], WHITE))                     # a checklist binder
prop("Launch Control Console", "A long beige launch-crew console: three green CRTs, rows of blinking lamps and switches.",
     parts, collider=[col([2.4, 0.9, 0.95], [0, 0.45, 0]), col([2.4, 1.0, 0.5], [0, 1.4, -0.3])], breakable=metal(260, 16))

parts = [b([1.2, 1.0, 0.7], [0, 0.5, 0], PANEL, "metal"),
         b([1.24, 0.06, 0.76], [0, 1.03, 0], DARK),
         b([1.2, 0.5, 0.15], [0, 1.31, -0.28], PANEL, "metal"),
         b([0.36, 0.2, 0.26], [-0.3, 1.16, 0.05], "#a01818"),                     # flip covers over the key switches
         b([0.36, 0.2, 0.26], [0.3, 1.16, 0.05], "#a01818"),
         b([0.06, 0.08, 0.06], [-0.3, 1.1, 0.2], YELLOW), b([0.06, 0.08, 0.06], [0.3, 1.1, 0.2], YELLOW),
         b([0.1, 0.1, 0.03], [-0.3, 1.4, -0.19], DARK, anim=blink(1.0, 0.5, RED, 1.4)),
         b([0.1, 0.1, 0.03], [0.3, 1.4, -0.19], DARK, anim=blink(1.0, 0.5, RED, 1.4, 0.5)),
         b([0.5, 0.12, 0.03], [0, 1.45, -0.19], DARK, anim=pulse(2.4, AMBER, 1.0)),       # LAUNCH ENABLE lamp
         b([0.16, 0.25, 0.16], [0, 1.68, -0.28], DARK),
         b([0.12, 0.14, 0.12], [0, 1.88, -0.28], "#c02020", anim=blink(0.8, 0.5, "#ff4040", 1.6))]
prop("Launch Key Console", "The two-key launch station: red flip covers over the key switches, the LAUNCH ENABLE lamp, a beacon.",
     parts, collider=[col([1.2, 1.4, 0.75], [0, 0.7, 0])], breakable=metal(300, 14))

# Big wall screen: the world map in dark blue, green continents, blinking red targets. Back at -Z.
parts = [b([4.2, 2.4, 0.12], [0, 1.2, 0.06], DARK, "metal"),
         b([4.0, 2.2, 0.02], [0, 1.2, 0.13], "#0a1630", anim=flicker(2, 0.97, "#10224a", 0.5))]
land = [(-1.5, 1.7, 0.7, 0.45), (-1.3, 1.25, 0.4, 0.5), (-1.0, 0.7, 0.3, 0.45), (0.0, 1.75, 0.5, 0.35), (0.15, 1.15, 0.45, 0.6),
        (0.9, 1.65, 1.2, 0.5), (1.2, 1.2, 0.4, 0.3), (1.5, 0.6, 0.5, 0.3)]
for x, y, w, h in land:
    parts.append(b([w, h, 0.01], [x, y, 0.145], "#1f6a3a", anim=pulse(6, "#2a9a50", 0.5)))
for i, (x, y) in enumerate([(-1.4, 1.6), (-1.1, 1.35), (0.05, 1.8), (0.25, 1.3), (0.8, 1.7), (1.1, 1.55), (1.35, 1.2), (-0.9, 0.8)]):
    parts.append(b([0.07, 0.07, 0.01], [x, y, 0.155], RED, anim=blink(0.9 + 0.2 * i, 0.4, "#ff4040", 1.6)))
for i in range(5):  # status strip along the bottom
    parts.append(b([0.6, 0.1, 0.01], [-1.6 + i * 0.8, 0.22, 0.145], DARK, anim={"type": "cycle", "period": 5, "steps": 5, "on": [i], "color": AMBER, "glow": 1.0}))
prop("Big Wall Display", "A wall-mounted tactical display: a dark blue world map, green continents, blinking red targets.",
     parts, solid=False, fade=90)

parts = [b([2.0, 1.3, 0.1], [0, 0.65, 0.05], DARK, "metal"), b([1.86, 1.16, 0.02], [0, 0.65, 0.11], "#081a10")]
for r in range(5):
    for c in range(3):
        parts.append(b([0.5, 0.12, 0.01], [-0.62 + c * 0.62, 1.05 - r * 0.2, 0.125], DARK,
                       anim={"type": "cycle", "period": 3 + c, "steps": 6, "on": [r, (r + 3) % 6], "color": [GREEN, AMBER, GREEN][c], "glow": 1.0}))
prop("Status Board", "A wall status board: rows of green and amber readiness lamps.", parts, solid=False, fade=70)

parts = [b([1.2, 2.0, 0.8], [0, 1.0, 0], BEIGE, "metal"), b([1.1, 0.7, 0.02], [0, 1.45, 0.41], "#2a2a2a"),
         b([1.2, 0.05, 0.82], [0, 2.02, 0], DARK)]
for x in (-0.27, 0.27):
    parts += [b([0.42, 0.42, 0.02], [x, 1.5, 0.43], "#5a5a5a", rot=[0, 0, 0], anim={"type": "spin", "axis": "z", "rpm": 40 if x < 0 else -40}),
              b([0.12, 0.12, 0.03], [x, 1.5, 0.44], WHITE, anim={"type": "follow", "parent": 0})]
for i in range(8):
    parts.append(b([0.06, 0.04, 0.02], [-0.42 + i * 0.12, 0.95, 0.41], DARK, anim=blink(0.5 + 0.21 * i, 0.5, [RED, GREEN, AMBER][i % 3], 1.0)))
parts.append(b([1.0, 0.5, 0.02], [0, 0.45, 0.41], "#9a9480"))
# fix the follow parents (part numbers are 1-based: the reel discs)
parts[4]["anim"]["parent"] = 4
parts[6]["anim"]["parent"] = 6
prop("Tape Mainframe", "A mainframe cabinet with two spinning tape reels and a strip of blinking lamps.", parts,
     collider=[col([1.2, 2.0, 0.8])], breakable=metal(220))

parts = [b([0.7, 0.75, 0.55], [0, 0.375, 0], "#5a5e60", "metal"), b([0.66, 0.25, 0.5], [0, 0.88, -0.02], BEIGE, "metal"),
         b([0.5, 0.04, 0.3], [0, 0.98, 0.12], DARK, rot=[-15, 0, 0]), b([0.4, 0.3, 0.01], [0, 1.15, -0.1], WHITE),
         b([0.36, 0.55, 0.01], [0, 0.55, 0.33], WHITE, rot=[10, 0, 0])]
prop("Teletype Printer", "A teletype on its stand, a curl of paper hanging out of it.", parts,
     collider=[col([0.7, 1.0, 0.55])], breakable=metal(90, 8))

# ------------------------------------------------------------------------------------------- power plant
parts = octagon(4.0, 4.6, 0, "#9aa0a4", "metal") + octagon(3.4, 0.9, 4.6, "#8a9094", "metal") + octagon(2.2, 0.5, 5.5, "#6a7074", "metal")
parts += [b([4.4, 0.4, 4.4], [0, 0.2, 0], DARK, "metal")]
for k in range(4):  # inspection windows glowing Cherenkov blue
    a = math.radians(k * 90 + 45)
    parts.append(b([0.5, 0.9, 0.06], [math.sin(a) * 1.93, 2.6, math.cos(a) * 1.93], "#2060ff", rot=[0, k * 90 + 45, 0], anim=pulse(3.2, "#60a0ff", 1.8)))
for k in range(4):  # hazard band
    parts.append(b([4.12, 0.25, 1.7], [0, 3.9, 0], YELLOW, rot=[0, k * 45, 0]))
parts += [b([0.5, 1.2, 0.5], [0, 6.3, 0], DARK, "metal"), b([0.12, 0.12, 0.12], [0, 6.95, 0], RED, anim=blink(1.2, 0.5, RED, 1.5))]
for x, z in ((2.4, 0), (-2.4, 0)):
    parts += octagon(0.5, 3.5, 0.4, STEEL, "metal", x=x, z=z)
prop("Reactor Vessel", "A small military power reactor: a steel pressure vessel with glowing blue inspection ports and a hazard band.",
     parts, collider=[col([4.0, 4.6, 4.0], [0, 2.3, 0]), col([3.4, 1.4, 3.4], [0, 5.3, 0])])

parts = [b([6.0, 1.8, 2.0], [0, 1.2, 0], "#4a6a5a", "metal"), b([6.4, 0.3, 2.4], [0, 0.15, 0], DARK, "metal"),
         b([2.0, 2.2, 2.2], [-2.6, 1.4, 0], "#4a6a5a", "metal"), b([1.2, 0.9, 1.4], [2.8, 2.5, 0], "#3a5a4a", "metal"),
         b([6.0, 0.15, 2.05], [0, 2.1, 0], YELLOW)]
parts += octagon(1.4, 5.8, 1.6, "#5a7a6a", "metal", lying="x")
parts[-4]["anim"] = {"type": "shake", "axis": [0.3, 1, 0.2], "distance": 0.006, "period": 0.06}
for p in parts[-3:]:
    p["anim"] = {"type": "follow", "parent": len(parts) - 3}
parts.append(b([0.3, 0.2, 0.03], [2.8, 2.6, 0.72], DARK, anim=blink(1.6, 0.7, GREEN, 1.2)))
prop("Turbine Generator", "A steam turbine and generator set on a steel skid, humming.", parts,
     collider=[col([6.4, 2.6, 2.4], [0, 1.3, 0])])

parts = [b([3.0, 0.85, 0.9], [0, 0.425, 0], "#7a8270", "metal"), b([3.04, 0.05, 0.96], [0, 0.875, 0], DARK),
         b([3.0, 1.2, 0.2], [0, 1.5, -0.38], "#7a8270", "metal"), b([2.8, 1.0, 0.02], [0, 1.5, -0.27], "#d8d8c8")]
for i in range(6):  # mimic diagram lines
    parts.append(b([2.4, 0.03, 0.01], [0, 1.15 + i * 0.13, -0.255], ["#2060c0", "#c02020", "#208040"][i % 3]))
for i in range(9):
    parts.append(b([0.07, 0.07, 0.01], [-1.2 + i * 0.3, 1.2 + (i % 3) * 0.25, -0.25], DARK, anim=blink(0.8 + 0.3 * i, 0.6, [GREEN, RED, AMBER][i % 3], 1.1)))
for i in range(6):
    parts.append(b([0.18, 0.12, 0.02], [-1.0 + i * 0.4, 0.9, 0.2], WHITE, rot=[-10, 0, 0]))
prop("Reactor Control Desk", "The reactor operator's desk: a white mimic panel of the coolant loops, lamps and meters.", parts,
     collider=[col([3.0, 0.9, 0.95], [0, 0.45, 0]), col([3.0, 1.2, 0.25], [0, 1.5, -0.38])], breakable=metal(240, 14))

parts = octagon(1.0, 1.4, 0.3, "#3a6aa0", "metal") + [b([1.6, 0.3, 1.2], [0, 0.15, 0], DARK, "metal")]
parts += octagon(0.7, 1.1, 0.75, "#4a4e52", "metal", x=0.0, z=0.0, lying="x")
for p in parts[5:9]:
    p["offset"][0] = 1.0
parts += [b([0.3, 1.8, 0.3], [-0.55, 1.2, 0], STEEL, "metal"), b([0.3, 0.3, 1.4], [-0.55, 2.0, 0.35], STEEL, "metal")]
for p in parts[:4]:
    p["anim"] = {"type": "shake", "axis": [0.4, 1, 0.3], "distance": 0.005, "period": 0.07}
prop("Coolant Pump", "A blue coolant pump and its motor, pipes climbing out of it, vibrating.", parts,
     collider=[col([2.4, 1.8, 1.2], [0.3, 0.9, 0])])

parts = []
for i, (y, d, c) in enumerate(((0.25, 0.28, "#9a9a92"), (0.65, 0.22, "#c03a2a"), (1.0, 0.18, "#2a6ab0"), (1.3, 0.14, YELLOW))):
    parts += octagon(d, 4.0, y, c, "metal", z=0.22, lying="x")
for x in (-1.6, 0.0, 1.6):
    parts.append(b([0.08, 1.55, 0.3], [x, 0.78, 0.15], DARK))
prop("Wall Pipe Run", "Four lagged and painted pipes running 4 m along a wall on brackets.", parts, solid=False, fade=70)

parts = [b([0.6, 0.6, 0.02], [0, 0.3, 0.01], YELLOW), b([0.14, 0.14, 0.01], [0, 0.32, 0.025], BLACK)]
for k in range(3):
    a = math.radians(90 + k * 120)
    parts.append(b([0.16, 0.13, 0.01], [math.cos(a) * 0.17, 0.32 + math.sin(a) * 0.17, 0.025], BLACK, rot=[0, 0, k * 120]))
parts.append(b([0.5, 0.06, 0.01], [0, 0.07, 0.025], BLACK))
prop("Radiation Sign", "A yellow radiation trefoil sign for a wall.", parts, solid=False, fade=45)

# ------------------------------------------------------------------------------------------- decon, armoury, comms
parts = [b([0.12, 2.4, 0.12], [-0.8, 1.2, 0], WHITE, "metal"), b([0.12, 2.4, 0.12], [0.8, 1.2, 0], WHITE, "metal"),
         b([1.72, 0.12, 0.12], [0, 2.4, 0], WHITE, "metal"), b([1.8, 0.04, 1.2], [0, 0.02, 0], "#9aa0a0", "tiles")]
for y in (0.6, 1.2, 1.8):
    for x in (-0.74, 0.74):
        parts.append(b([0.06, 0.06, 0.06], [x, y, 0.06], "#c0c8d0"))
parts.append(b([0.3, 0.3, 0.02], [0, 2.4, 0.07], YELLOW))
prop("Decon Shower Arch", "A walk-through decontamination spray arch with nozzles on both posts.", parts, solid=False, fade=60)

parts = [b([1.6, 0.06, 0.5], [0, 1.9, 0], STEEL, "metal"), b([0.06, 1.9, 0.5], [-0.77, 0.95, 0], STEEL, "metal"),
         b([0.06, 1.9, 0.5], [0.77, 0.95, 0], STEEL, "metal"), b([1.5, 0.04, 0.04], [0, 1.75, 0], DARK)]
for i, x in enumerate((-0.5, 0.0, 0.5)):
    parts += [b([0.42, 0.9, 0.3], [x, 1.2, 0.02], "#d8b020"), b([0.24, 0.24, 0.22], [x, 1.78, 0.02], "#d8b020"),
              b([0.18, 0.1, 0.02], [x, 1.78, 0.14], "#203040"), b([0.42, 0.55, 0.26], [x, 0.45, 0.02], "#c8a018")]
prop("Hazmat Suit Rack", "A steel rack of yellow hazmat suits with their hoods.", parts,
     collider=[col([1.6, 1.95, 0.5])], breakable=metal(90, 10))

parts = [b([2.2, 0.76, 0.8], [0, 0.38, 0], OLIVE, "metal"), b([2.24, 0.05, 0.84], [0, 0.785, 0], DARK)]
for i, x in enumerate((-0.7, 0.0, 0.7)):
    parts += [b([0.62, 0.9, 0.45], [x, 1.26, -0.15], "#4a5040", "metal"), b([0.5, 0.18, 0.02], [x, 1.5, 0.08], BLACK),
              b([0.4, 0.06, 0.01], [x, 1.5, 0.095], AMBER, anim=pulse(1.5 + i * 0.4, AMBER, 1.0))]
    for k in range(3):
        parts.append(b([0.09, 0.09, 0.03], [x - 0.18 + k * 0.18, 1.18, 0.09], WHITE))
parts += [b([0.25, 0.08, 0.2], [0.4, 0.84, 0.2], BLACK), b([0.3, 0.02, 0.22], [-0.5, 0.82, 0.15], WHITE),
          b([0.06, 0.4, 0.06], [-1.0, 1.95, -0.3], DARK), b([0.1, 0.1, 0.03], [-0.6, 1.62, 0.08], DARK, anim=blink(0.6, 0.3, RED, 1.2))]
prop("Radio Console", "An olive comms desk: three stacked radio sets with glowing dials, a headset and a message pad.", parts,
     collider=[col([2.2, 0.8, 0.85], [0, 0.4, 0]), col([2.2, 1.0, 0.5], [0, 1.25, -0.15])], breakable=metal(200, 14))

parts = [b([0.9, 2.0, 0.6], [0, 1.0, 0], "#5a5e60", "metal"), b([0.8, 1.6, 0.02], [0, 1.1, 0.31], DARK)]
for r in range(8):
    parts.append(b([0.7, 0.05, 0.02], [0, 0.45 + r * 0.2, 0.325], ["#c02020", "#2060c0", YELLOW, "#20a040"][r % 4]))
for r in range(4):
    parts.append(b([0.05, 0.05, 0.02], [0.32, 0.6 + r * 0.35, 0.33], DARK, anim=blink(0.4 + r * 0.17, 0.5, GREEN, 1.0)))
prop("Patch Panel Cabinet", "A tall comms cabinet: a patch bay of coloured cords and blinking link lamps.", parts,
     collider=[col([0.9, 2.0, 0.6])], breakable=metal(150))

parts = [b([1.5, 0.08, 0.45], [0, 0.04, 0], STEEL, "metal"), b([1.5, 0.08, 0.2], [0, 1.6, -0.12], STEEL, "metal"),
         b([0.06, 1.7, 0.45], [-0.72, 0.85, 0], STEEL, "metal"), b([0.06, 1.7, 0.45], [0.72, 0.85, 0], STEEL, "metal"),
         b([1.4, 0.06, 0.3], [0, 0.35, 0.02], DARK)]
for i in range(7):
    x = -0.6 + i * 0.2
    parts += [b([0.06, 1.0, 0.1], [x, 0.85, 0.02], "#2a2a28", rot=[-6, 0, 0]), b([0.05, 0.3, 0.12], [x, 0.55, 0.06], "#5a3a22", rot=[-6, 0, 0])]
prop("Steel Rifle Rack", "A locking steel rack of seven rifles standing upright.", parts,
     collider=[col([1.5, 1.7, 0.45])], breakable=metal(120, 10))

parts = [b([0.05, 2.0, 0.6], [-0.9, 1.0, 0], OLIVE, "metal"), b([0.05, 2.0, 0.6], [0.9, 1.0, 0], OLIVE, "metal"),
         b([1.8, 2.0, 0.04], [0, 1.0, -0.28], OLIVE, "metal")]
for y in (0.05, 0.7, 1.35, 1.98):
    parts.append(b([1.8, 0.05, 0.6], [0, y, 0], OLIVE, "metal"))
for y in (0.1, 0.75, 1.4):
    for k in range(4):
        x = -0.65 + k * 0.43
        parts += [b([0.3, 0.2, 0.18], [x, y + 0.12, 0.05], "#4a5530"), b([0.1, 0.03, 0.04], [x, y + 0.235, 0.05], DARK)]
prop("Armoury Shelf", "Olive steel shelving stacked with ammunition cans.", parts,
     collider=[col([1.8, 2.0, 0.6])], breakable=metal(140, 12))

# ------------------------------------------------------------------------------------------- tunnels and doors
parts = [b([0.22, 0.3, 0.06], [0, 0.15, 0.03], DARK, "metal"), b([0.16, 0.2, 0.12], [0, 0.15, 0.1], "#ffd890", anim=flicker(5, 0.94, "#ffe0a0", 1.6))]
for x in (-0.08, 0.08):
    parts.append(b([0.02, 0.24, 0.16], [x, 0.15, 0.1], DARK))
parts += [b([0.2, 0.02, 0.16], [0, 0.27, 0.1], DARK), b([0.2, 0.02, 0.16], [0, 0.03, 0.1], DARK)]
prop("Caged Bulkhead Lamp", "A caged bulkhead lamp for a tunnel or bunker wall, flickering warm.", parts, solid=False, fade=80)

parts = [b([1.5, 0.08, 0.16], [0, 0.04, 0], "#d0d0c8", "metal"), b([1.4, 0.05, 0.08], [0, -0.0, 0], "#fff8e0", anim=flicker(3, 0.97, "#fffbe8", 1.4)),
         b([0.02, 0.4, 0.02], [-0.6, 0.28, 0], DARK), b([0.02, 0.4, 0.02], [0.6, 0.28, 0], DARK)]
parts[1]["offset"][1] = 0.025
prop("Tube Light", "A hanging fluorescent tube fitting (its top is 0.48 m up: hang it under a ceiling).", parts, solid=False, fade=70)

parts = [b([4.0, 0.04, 0.4], [0, 0.0 + 0.02, 0.2], "#8a8e90", "metal"), b([4.0, 0.12, 0.03], [0, 0.08, 0.39], "#8a8e90", "metal")]
for k, c in enumerate(("#1a1a1a", "#2a2a2a", "#3a2a1a", "#1a2a3a", "#1a1a1a")):
    parts += octagon(0.05 + 0.01 * (k % 2), 4.0, 0.07, c, None, z=0.06 + k * 0.07, lying="x")
for x in (-1.5, 0.0, 1.5):
    parts.append(b([0.04, 0.3, 0.42], [x, 0.0, 0.21], DARK))
for p in parts:
    p["offset"][1] += 0.16
prop("Wall Cable Tray", "A 4 m steel cable tray full of black cables, on wall brackets.", parts, solid=False, fade=60)

parts = [b([2.8, 3.0, 0.45], [1.4, 1.5, 0], "#5a6048", "metal"), b([2.5, 2.7, 0.06], [1.4, 1.5, 0.25], "#4a5038", "metal")]
for k in range(4):
    parts.append(b([0.18, 0.1, 0.5], [2.55, 0.6 + k * 0.6, 0], STEEL, "metal"))
for y in (0.4, 2.6):
    parts.append(b([0.3, 0.4, 0.55], [0.05, y, 0], DARK, "metal"))
parts += [b([0.5, 0.5, 0.08], [1.4, 1.5, 0.3], DARK), b([0.08, 0.6, 0.08], [1.4, 1.5, 0.35], STEEL, rot=[0, 0, 45]),
          b([2.5, 0.25, 0.01], [1.4, 2.6, 0.29], YELLOW), b([2.5, 0.25, 0.01], [1.4, 0.4, 0.29], YELLOW)]
prop("Blast Door Leaf", "A massive steel blast door leaf standing open on its hinge (the hinge at x = 0, the leaf along +X).", parts,
     collider=[col([2.8, 3.0, 0.5], [1.4, 1.5, 0])])

parts = [b([1.1, 4.2, 1.4], [-2.05, 2.1, 0], CONC, "concrete"), b([1.1, 4.2, 1.4], [2.05, 2.1, 0], CONC, "concrete"),
         b([5.2, 1.2, 1.4], [0, 3.6, 0], CONC, "concrete"), b([5.4, 0.25, 1.6], [0, 4.32, 0], "#7a7a74", "concrete")]
for x in (-1.55, 1.55):
    parts.append(b([0.12, 3.0, 1.42], [x, 1.5, 0], YELLOW))
parts += [b([3.0, 0.12, 1.42], [0, 3.0, 0], YELLOW),
          b([0.22, 0.22, 0.12], [-2.05, 3.35, 0.72], DARK), b([0.16, 0.16, 0.06], [-2.05, 3.35, 0.79], RED, anim=blink(1.4, 0.5, RED, 1.4)),
          b([0.22, 0.22, 0.12], [2.05, 3.35, 0.72], DARK), b([0.16, 0.16, 0.06], [2.05, 3.35, 0.79], RED, anim=blink(1.4, 0.5, RED, 1.4, 0.5)),
          b([1.6, 0.4, 0.04], [0, 3.6, 0.72], "#e8e0c8"), b([1.4, 0.1, 0.02], [0, 3.62, 0.745], "#202020")]
prop("Blast Door Portal", "A thick concrete door frame 3 m wide and 3 m high with hazard stripes and red lamps (the opening is walkable).",
     parts, collider=[col([1.1, 4.2, 1.4], [-2.05, 2.1, 0]), col([1.1, 4.2, 1.4], [2.05, 2.1, 0]), col([3.0, 1.45, 1.4], [0, 3.72, 0])])

# ------------------------------------------------------------------------------------------- yard and plant
parts = octagon(2.0, 5.0, 1.15, "#d8d8d0", "metal", lying="x")
for p in parts:
    p["offset"][1] = 1.25
parts += octagon(1.4, 0.3, 1.25, "#c8c8c0", "metal", x=-2.6, lying="x") + octagon(1.4, 0.3, 1.25, "#c8c8c0", "metal", x=2.6, lying="x")
for x in (-1.6, 1.6):
    parts.append(b([0.4, 0.5, 1.8], [x, 0.25, 0], DARK, "metal"))
parts += [b([5.0, 0.22, 0.02], [0, 1.25, 1.0], "#c02020"), b([0.8, 0.5, 0.02], [0, 1.6, 1.0], WHITE),
          b([0.15, 0.6, 0.15], [0, 2.4, 0], STEEL), b([0.3, 0.15, 0.3], [0, 2.75, 0], "#a02020")]
prop("Propellant Tank", "A white horizontal propellant tank on saddles, a red band round its middle.", parts,
     collider=[col([5.6, 2.3, 2.0], [0, 1.15, 0])])

parts = [b([2.4, 2.4, 1.2], [0, 1.4, 0], "#6a7068", "metal"), b([2.6, 0.2, 1.4], [0, 0.1, 0], DARK, "metal"),
         b([2.0, 2.0, 0.02], [0, 1.4, 0.61], "#2a2a2a")]
hub = len(parts) + 1
parts.append(b([0.3, 0.3, 0.1], [0, 1.4, 0.63], DARK, anim={"type": "spin", "axis": "z", "pivot": [0, 1.4, 0.63], "rpm": 90}))
for k in range(5):
    a = k * 72
    r = math.radians(a)
    parts.append(b([0.9, 0.22, 0.03], [math.cos(r) * 0.5, 1.4 + math.sin(r) * 0.5, 0.64], "#9a9a90", rot=[0, 0, a],
                   anim={"type": "follow", "parent": hub}))
for y in (0.6, 1.0, 1.4, 1.8, 2.2):
    parts.append(b([2.0, 0.04, 0.03], [0, y, 0.68], STEEL))
prop("Ventilation Fan Unit", "A big ducted ventilation fan in a steel housing, its blades turning behind a guard.", parts,
     collider=[col([2.6, 2.6, 1.4], [0, 1.3, 0])])

parts = [b([1.7, 0.2, 1.7], [0, 0.1, 0], CONC, "concrete"), b([1.5, 1.0, 1.5], [0, 0.7, 0], "#d8d8cc", "metal"),
         b([1.6, 0.25, 1.6], [0, 2.55, 0], "#4a5232", "metal"), b([1.8, 0.08, 1.8], [0, 2.71, 0], DARK)]
for x, z, w, d in ((-0.72, 0, 0.06, 1.5), (0.72, 0, 0.06, 1.5), (0, -0.72, 1.5, 0.06), (0, 0.72, 1.5, 0.06)):
    parts.append(b([w, 1.2, d], [x, 1.8, z], "#8ab0c0"))
for x, z in ((-0.72, -0.72), (0.72, -0.72), (-0.72, 0.72), (0.72, 0.72)):
    parts.append(b([0.1, 1.25, 0.1], [x, 1.825, z], "#d8d8cc"))
parts += [b([0.2, 0.2, 0.2], [0, 2.85, 0], AMBER, anim=pulse(2.0, AMBER, 1.2)), b([0.6, 0.06, 0.4], [0, 1.22, 0.55], DARK)]
prop("Guard Booth", "A sentry booth at a gate: glazed all round, an olive roof and an amber lamp.", parts,
     collider=[col([1.6, 2.7, 1.6], [0, 1.35, 0])], breakable={"health": 300, "breakBy": ["explosions", "streaks"], "sound": "metal", "debris": {"count": 16, "size": [0.05, 0.3]}})

parts = [b([0.1, 2.2, 0.1], [-0.7, 1.1, 0], STEEL, "metal"), b([0.1, 2.2, 0.1], [0.7, 1.1, 0], STEEL, "metal"),
         b([1.7, 1.0, 0.04], [0, 1.6, 0.06], WHITE), b([1.7, 0.3, 0.05], [0, 1.95, 0.065], "#b01818"),
         b([1.2, 0.06, 0.01], [0, 1.95, 0.09], WHITE), b([1.3, 0.06, 0.01], [0, 1.55, 0.085], "#202020"),
         b([1.1, 0.06, 0.01], [0, 1.4, 0.085], "#202020"), b([1.3, 0.06, 0.01], [0, 1.25, 0.085], "#202020")]
prop("Restricted Area Sign", "A RESTRICTED AREA sign on two posts: a red header, warning lines below.", parts,
     collider=[col([1.5, 2.2, 0.15], [0, 1.1, 0])], breakable={"health": 100, "breakBy": "all", "sound": "metal", "debris": {"count": 8, "size": [0.05, 0.3]}})


def main():
    os.makedirs(OUT, exist_ok=True)
    for d in PROPS:
        fn = d["name"].replace(" ", "") + ".json"
        with open(os.path.join(OUT, fn), "w") as f:
            json.dump(d, f, indent=1)
            f.write("\n")
    print(f"{len(PROPS)} props written to {OUT}")


if __name__ == "__main__":
    main()
