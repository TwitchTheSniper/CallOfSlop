#!/usr/bin/env python3
"""Writes Props/Zombies/Casino/*.json: the four Zombies casino tables (2026-10-27; docs/zombies/ZOMBIES-MODE.md, "Casino
tables"; the game: Source/Game/ZombiesCasino.cs).

Run from the repository root:  python3 -I tools/zombies/make_casino_tables.py

Every table's felt is at y 0.9 (Program.FeltY: the game lays its cards, chips, wheel pockets and dice on it), the
players stand on the -Z side and the dealer behind (+Z). The game draws where these props leave room:
  - Blackjack Table: a half-round table (a D, the flat side at the back): cards and bets for four seats along the
    curved front edge, the dealer's cards at z 0.25.
  - Roulette Table: a long table with the wheel's bowl on the right (+X, centre x 0.72: the game turns 37 pockets at
    radius 0.3 and y 1.0 and lands the ball) and the betting board painted on the left, cell for cell where the game
    puts the chips (Program.BoardSpot: numbers from x -0.95 in 0.09 steps, rows at z -0.12 / 0 / 0.12, the zero at
    x -1.04, the dozens at z -0.27, the even-money bets at z -0.38, the columns at x 0.13).
  - High-Low Table: a small round card table on a pedestal, with a tall HI / LO sign at the back (the run of cards goes
    across the middle).
  - Dice Table: a tub with high padded rails all round and a studded back wall (the dice bounce to a stop near
    x 0.4), the point boxes along the back (x -0.5 to 0.5, z 0.3).
Round shapes are polygon prisms and rings of rotated boxes (the helpers in make_slot_cabinets.py). Original designs: no
real casino's name, logo or layout art.
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_slot_cabinets import b, bounds, ring_y, ry  # noqa: E402  (the shared box / prism helpers)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Props", "Zombies", "Casino")

FELT_Y = 0.9
WOOD = "#5a3820"
DWOOD = "#3e2614"
LEATHER = "#2a1a14"
BRASS = "#c8961c"
GREEN_FELT = "#1f5a33"
BLUE_FELT = "#1d3a5f"
RED_FELT = "#5a1d24"
RED = "#8a1414"
BLACK = "#141414"
CREAM = "#e8e0c8"

REDS = {1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36}


def spokes(cx, cz, y0, y1, a, color, sides, start, end, material=None):
    """Part of a regular `sides`-gon slab (apothem a) between the angles start..end (degrees; 0 = -Z, the front;
    90 = +X): boxes from the centre out to the edge, one per side, so their union is that slice of the polygon."""
    out = []
    w = 2 * a * math.tan(math.pi / sides) * 1.02
    k = 0
    while True:
        ang = start + (k + 0.5) * 360.0 / sides
        if ang > end + 1e-6:
            break
        r = math.radians(ang)
        mx, mz = math.sin(r) * a / 2, -math.cos(r) * a / 2
        out.append(b([w, y1 - y0, a], [cx + mx, (y0 + y1) / 2, cz + mz], color, ry(-ang), material))
        k += 1
    return out


def rail(cx, cz, y0, y1, a, thick, color, sides, start, end, material=None):
    """The padded rail round that slice: one box along each side of the polygon, `thick` wide."""
    out = []
    edge = 2 * (a + thick / 2) * math.tan(math.pi / sides) * 1.04
    k = 0
    while True:
        ang = start + (k + 0.5) * 360.0 / sides
        if ang > end + 1e-6:
            break
        r = math.radians(ang)
        d = a + thick / 2
        out.append(b([edge, y1 - y0, thick], [cx + math.sin(r) * d, (y0 + y1) / 2, cz - math.cos(r) * d], color, ry(-ang), material))
        k += 1
    return out


def blackjack():
    p = []
    # Two pedestal legs with splayed feet, a stretcher between them.
    for x in (-0.55, 0.55):
        p += ring_y(x, 0.0, 0.05, 0.76, 0.07, DWOOD, 8, "wood")
        p += [b([0.5, 0.05, 0.16], [x, 0.025, 0.0], DWOOD, material="wood"), b([0.16, 0.05, 0.5], [x, 0.025, 0.0], DWOOD, material="wood")]
    p += [b([1.1, 0.06, 0.06], [0, 0.3, 0.0], DWOOD, material="wood")]
    # The top: a half 16-gon at the front (apothem 0.95 round the point z 0.2), a straight strip at the back for the dealer.
    p += spokes(0, 0.2, 0.78, 0.88, 0.95, WOOD, 16, -90, 90, "wood")
    p += [b([1.92, 0.1, 0.25], [0, 0.83, 0.33], WOOD, material="wood")]
    p += spokes(0, 0.2, 0.88, FELT_Y, 0.86, GREEN_FELT, 16, -90, 90)
    p += [b([1.74, 0.02, 0.24], [0, 0.89, 0.32], GREEN_FELT)]
    p += rail(0, 0.2, 0.84, 0.96, 0.86, 0.11, LEATHER, 16, -90, 90)                       # the padded arm rail
    # Betting circles at the four seats (thin gold rings of four boxes), the dealer's chip rack and the card shoe.
    for k in range(4):
        x = -0.66 + 0.44 * k
        z = -0.18 - 0.12 * x * x - 0.06
        for yaw in (0, 45, 90, 135):
            p += [b([0.1, 0.002, 0.012], [x, FELT_Y + 0.001, z], BRASS, ry(yaw))]
    p += [b([0.6, 0.05, 0.12], [0, 0.925, 0.4], DWOOD, material="wood")]
    for k in range(6):
        p += [b([0.07, 0.03, 0.09], [-0.25 + 0.1 * k, 0.94, 0.4], [RED, "#1f3fa0", "#2a8a3a", BLACK, CREAM, BRASS][k])]
    p += [b([0.16, 0.1, 0.22], [0.72, 0.95, 0.32], "#2a2a2a"), b([0.12, 0.02, 0.06], [0.72, 0.99, 0.2], CREAM)]
    # The sign on the dealer's side: a little brass arch reading as a lamp over the table.
    p += [b([0.04, 0.6, 0.04], [-0.86, 1.2, 0.4], BRASS, material="metal"), b([0.04, 0.6, 0.04], [0.86, 1.2, 0.4], BRASS, material="metal")]
    p += [b([1.76, 0.04, 0.04], [0, 1.5, 0.4], BRASS, material="metal"), b([0.5, 0.05, 0.12], [0, 1.47, 0.4], "#d8c070")]
    collider = [{"size": [1.92, 0.96, 1.2], "offset": [0, 0.48, -0.15]}]
    return p, collider, "A half-round blackjack table: a padded arm rail round green felt with four betting circles, the dealer's chip rack and card shoe at the back and a brass lamp arch over it"


def roulette():
    p = []
    # The body: a long cabinet with rounded ends (half octagon slabs at both ends), the top a little wider.
    p += [b([1.4, 0.82, 0.98], [0, 0.41, 0], DWOOD, material="wood")]
    p += spokes(-0.7, 0, 0.0, 0.82, 0.49, DWOOD, 8, 180, 360, "wood")
    p += spokes(0.7, 0, 0.0, 0.82, 0.49, DWOOD, 8, 0, 180, "wood")
    p += [b([1.44, 0.06, 1.06], [0, 0.85, 0], WOOD, material="wood")]
    p += spokes(-0.72, 0, 0.82, 0.88, 0.53, WOOD, 8, 180, 360, "wood")
    p += spokes(0.72, 0, 0.82, 0.88, 0.53, WOOD, 8, 0, 180, "wood")
    p += [b([0.95, 0.02, 0.94], [-0.245, 0.89, 0], GREEN_FELT)]                             # the board's felt
    p += spokes(-0.72, 0, 0.88, 0.9, 0.47, GREEN_FELT, 8, 180, 360)
    p += [b([1.44, 0.05, 0.05], [0, 0.905, -0.53], LEATHER), b([1.44, 0.05, 0.05], [0, 0.905, 0.53], LEATHER)]  # rails
    # The board: the zero, 36 numbers, the dozens, the even-money bets, the columns (where the game puts the chips).
    x0, cw, rz = -0.95, 0.09, 0.12
    p += [b([0.08, 0.002, 0.35], [x0 - cw, FELT_Y + 0.001, 0], "#1d8a3a")]
    for n in range(1, 37):
        col, row = (n - 1) // 3, (n - 1) % 3 - 1
        p += [b([0.082, 0.002, 0.112], [x0 + cw * col, FELT_Y + 0.001, rz * row], RED if n in REDS else BLACK)]
    for d in range(3):
        p += [b([0.35, 0.002, 0.09], [x0 + cw * (1.5 + 4 * d), FELT_Y + 0.001, -0.27], "#2c6e40")]
    for k, c in enumerate(["#2c6e40", "#2c6e40", RED, BLACK, "#2c6e40", "#2c6e40"]):
        p += [b([0.17, 0.002, 0.09], [x0 + cw * (0.5 + 2 * k), FELT_Y + 0.001, -0.38], c)]
    for r in range(3):
        p += [b([0.08, 0.002, 0.112], [x0 + cw * 12, FELT_Y + 0.001, rz * (r - 1)], "#2c6e40")]
    # The wheel's bowl on the right: a wooden ring, a dark floor under the pockets, a brass rim.
    p += ring_y(0.72, 0, 0.88, 0.985, 0.4, DWOOD, 16, "wood")
    p += ring_y(0.72, 0, 0.985, 0.99, 0.36, "#1a1410", 16)
    for k in range(16):                                                                    # the raised rim, a ring of boxes
        ang = (k + 0.5) * 22.5
        r = math.radians(ang)
        p += [b([0.17, 0.05, 0.05], [0.72 + math.sin(r) * 0.43, 1.0, -math.cos(r) * 0.43], BRASS, ry(-ang), "metal")]
    collider = [{"size": [2.56, 0.95, 1.1], "offset": [0, 0.475, 0]}]
    return p, collider, "A long roulette table: a wooden cabinet with rounded ends, the betting board painted on green felt at its left and the wheel's bowl with its brass rim at its right"


def high_low():
    p = []
    p += ring_y(0, 0, 0.0, 0.06, 0.32, DWOOD, 8, "wood")                                   # the foot
    p += ring_y(0, 0, 0.06, 0.8, 0.09, BRASS, 8, "metal")                                  # the pedestal
    p += ring_y(0, 0, 0.8, 0.88, 0.6, WOOD, 12, "wood")                                    # the top
    p += ring_y(0, 0, 0.88, FELT_Y, 0.54, BLUE_FELT, 12)
    for k in range(12):                                                                    # the padded rim
        ang = (k + 0.5) * 30
        r = math.radians(ang)
        p += [b([0.33, 0.06, 0.08], [math.sin(r) * 0.6, 0.91, -math.cos(r) * 0.6], LEATHER, ry(-ang))]
    # The HI / LO sign at the back: a post, a board and two arrows (green up, red down) of rotated boxes.
    p += [b([0.06, 0.9, 0.06], [0, 1.35, 0.62], BRASS, material="metal")]
    p += [b([0.7, 0.42, 0.05], [0, 1.95, 0.62], "#1a1a22")]
    p += [b([0.05, 0.2, 0.01], [-0.17, 1.92, 0.59], "#40d060"), b([0.13, 0.05, 0.01], [-0.21, 2.0, 0.59], "#40d060", [0, 0, 45]), b([0.13, 0.05, 0.01], [-0.13, 2.0, 0.59], "#40d060", [0, 0, -45])]
    p += [b([0.05, 0.2, 0.01], [0.17, 1.98, 0.59], "#e04040"), b([0.13, 0.05, 0.01], [0.13, 1.9, 0.59], "#e04040", [0, 0, -45]), b([0.13, 0.05, 0.01], [0.21, 1.9, 0.59], "#e04040", [0, 0, 45])]
    p += [b([0.74, 0.04, 0.07], [0, 2.17, 0.62], BRASS, material="metal"), b([0.74, 0.04, 0.07], [0, 1.73, 0.62], BRASS, material="metal")]
    p += [b([0.14, 0.08, 0.2], [0.38, 0.95, 0.3], "#2a2a2a")]                              # the card shoe
    collider = [{"size": [1.2, 0.95, 1.2], "offset": [0, 0.475, 0]}]
    return p, collider, "A small round high-low card table on a brass pedestal, blue felt inside a padded rim, with a HI / LO sign of green and red arrows on a post at the back"


def dice():
    p = []
    # The tub: an outer wooden wall with rounded corners (quarter rings), felt inside at 0.9, padded rails on top.
    hx, hz = 0.95, 0.5
    p += [b([2 * hx - 0.3, 0.9, 2 * hz], [0, 0.45, 0], DWOOD, material="wood"), b([2 * hx, 0.9, 2 * hz - 0.3], [0, 0.45, 0], DWOOD, material="wood")]
    for sx, sz, a0 in ((-1, -1, 270), (1, -1, 0), (1, 1, 90), (-1, 1, 180)):
        p += spokes(sx * (hx - 0.15), sz * (hz - 0.15), 0.0, 0.9, 0.15, DWOOD, 16, a0, a0 + 90, "wood")
    p += [b([2 * hx - 0.12, 0.002, 2 * hz - 0.12], [0, FELT_Y + 0.001, 0], RED_FELT)]
    for z in (-1, 1):
        p += [b([2 * hx - 0.3, 0.16, 0.1], [0, 0.98, z * (hz - 0.05)], WOOD, material="wood")]
        p += [b([2 * hx - 0.3, 0.06, 0.14], [0, 1.08, z * (hz - 0.05)], LEATHER)]
    for x in (-1, 1):
        p += [b([0.1, 0.16, 2 * hz - 0.3], [x * (hx - 0.05), 0.98, 0], WOOD, material="wood")]
        p += [b([0.14, 0.06, 2 * hz - 0.3], [x * (hx - 0.05), 1.08, 0], LEATHER)]
    for sx, sz, a0 in ((-1, -1, 270), (1, -1, 0), (1, 1, 90), (-1, 1, 180)):
        p += rail(sx * (hx - 0.15), sz * (hz - 0.15), 0.9, 1.11, 0.08, 0.14, LEATHER, 8, a0, a0 + 90)
    # The studded back wall the dice bounce off: rubber pyramids (boxes turned 45 degrees).
    for k in range(9):
        p += [b([0.06, 0.06, 0.03], [-0.6 + 0.15 * k, 0.97, hz - 0.12], "#3a3a3a", [0, 0, 45])]
    # The point boxes along the back (4, 5, 6, 8, 9, 10: the game puts the puck on the point's).
    for k in range(6):
        p += [b([0.16, 0.002, 0.12], [-0.5 + 0.2 * k, FELT_Y + 0.002, 0.3], CREAM)]
        p += [b([0.12, 0.002, 0.08], [-0.5 + 0.2 * k, FELT_Y + 0.003, 0.3], RED_FELT)]
    # The pass line: a cream band near the players.
    p += [b([1.5, 0.002, 0.05], [0, FELT_Y + 0.002, -0.3], CREAM)]
    p += [b([0.12, 0.04, 0.3], [-0.84, 1.1, 0.0], "#222222")]                             # the stick rest
    collider = [{"size": [2.0, 1.11, 1.1], "offset": [0, 0.555, 0]}]
    return p, collider, "A Bone Dice tub: a wooden table with high padded rails and rounded corners, dark red felt, a studded back wall for the dice, the point boxes and the pass line"


TABLES = [
    ("Blackjack Table", "BlackjackTable.json", blackjack),
    ("Roulette Table", "RouletteTable.json", roulette),
    ("High-Low Table", "HighLowTable.json", high_low),
    ("Dice Table", "DiceTable.json", dice),
]


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, fn, build in TABLES:
        parts, collider, what = build()
        assert len(parts) <= 128, f"{name}: {len(parts)} parts (props take 128)"
        lo, hi = bounds(parts)
        assert lo[0] >= -1.3 and hi[0] <= 1.3 and lo[2] >= -0.8 and hi[2] <= 0.8, f"{name}: bounds {lo} to {hi}"
        prop = {
            "format": "call-of-slop-prop/1",
            "name": name,
            "category": "Zombies",
            "description": f"{what}. A Zombies casino table: players stand on the -Z side; the game draws the cards, chips, wheel and dice on its felt.",
            "collision": "solid",
            "collider": collider,
            "scatter": {"randomYaw": False, "yawStep": 90},
            "parts": parts,
        }
        parts_json = ",\n".join("    " + json.dumps(q) for q in prop.pop("parts"))
        head = "{\n" + ",\n".join(f"  {json.dumps(k)}: {json.dumps(v)}" for k, v in prop.items())
        with open(os.path.join(OUT, fn), "w") as f:
            f.write(head + ',\n  "parts": [\n' + parts_json + "\n  ]\n}\n")
        print(f"{name}: {len(parts)} parts, bounds {lo} to {hi}")
    print(f"wrote {len(TABLES)} tables to {OUT}")


if __name__ == "__main__":
    main()
