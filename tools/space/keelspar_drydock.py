#!/usr/bin/env python3
"""Generator for Maps/Space/05-KeelsparDrydock.json: "Keelspar Drydock", the Space pack's orbital shipyard (medium,
16 players, void map).

    python3 tools/space/keelspar_drydock.py [repo root] [plan.png]

An orbital drydock of the invented Meridian Hullworks: hull MH-07, half built, lies in its cradle along the middle of
the dock with a void under it. Two long quays run either side of the hull (north and south), the bow dock (Blue, west)
and the stern dock (Red, east) close the ends. Four decks: the keel deck inside the hull (18 m), the quays and docks
(20 m), scaffold decks hung along the plated midsection (24 m) and the hull spine with two gantry crane bridges across
it (28 m). Everything is original: no film or game ship, yard, name or insignia.

Built like Stratos Station (FPS first): big flat decks are single Block ops, the round hull and all dressing are
cosmetic meshes over hidden colliders, railings are collider bands under drawn panels, stairs one hidden collider and
one instanced mesh box per step. The map is one half (the north quay, the bow dock, the west gantry, the north
scaffold, the west interior stair) turned 180 degrees about the centre for the other half, so both teams get the same.
"""
import math
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", ".."))
OUT_JSON = os.path.join(REPO, "Maps", "Space", "05-KeelsparDrydock.json")
OUT_PNG = sys.argv[2] if len(sys.argv) > 2 else None
sys.path.insert(0, HERE)
import spacekit_a as K  # noqa: E402
from spacekit_a import block, collider, mesh, mesh_at, slab, prop, decal, decal_scatter, waypoint, waypoint_grid, spawn, building, railing, stairs, gangway, round_collider  # noqa: E402
import keelspar_decals as sdec  # noqa: E402

FALL_KILL = 6.0
QY, KY, SC, SY = 20.0, 18.0, 24.0, 28.0   # quay / keel deck / scaffold / spine heights
HR, HC = 6.5, 21.4                        # hull radius and centre height
PLATED = 24.0                             # the plated midsection: |x| <= 24; open ribs out to 40

DECK = "#9a9590"
DECK2 = "#8a8580"
GRID = "#7d7a76"
HULL = "#cdbfa8"
HULL2 = "#b3a48c"
RIM = "#8d7458"
STEEL = "#6c6f73"
DARK = "#3a3c40"
ORANGE = "#e8701e"
HAZARD = "#f0c040"
LAMP = "#ffd9a0"
INNER = "#5a6066"


# ================================================================================================ shared geometry (self-symmetric)
def hull():
    # Plated midsection: a hollow tube (its inside is drawn too), bronze bands, the keel deck and the spine.
    mesh_at("tube", 0, HC, 0, HULL, roll=90, radius=HR, inner=HR - 0.3, height=2 * PLATED, segments=48, material="Metal")
    for x in (-PLATED, -12, 0, 12, PLATED):
        mesh_at("tube", x, HC, 0, RIM, roll=90, radius=HR + 0.12, inner=HR - 0.1, height=0.5, segments=48)
    # Open ribs beyond the plating, with longitudinal stringers.
    for s in (-1, 1):
        for x in (26, 30, 34, 38):
            mesh_at("torus", s * x, HC, 0, RIM, roll=90, radius=HR, inner=0.28, segments=40, detail=8)
        for a in (30, 90, 150, 210, 270, 330):
            t = math.radians(a)
            mesh_at("box", s * 32, HC + HR * math.sin(t), HR * math.cos(t), STEEL, width=16.4, height=0.2, depth=0.2)
        mesh_at("torus", s * 40.2, HC, 0, HULL2, roll=90, radius=HR - 0.2, inner=0.3, segments=40, detail=8)
    # Bow cone (west) and stern engine bells (east): cosmetic, over the void.
    mesh_at("cone", -44.0, HC, 0, HULL, roll=90, width=2 * HR, depth=2 * HR, height=8.0, segments=48, material="Metal")
    mesh_at("cylinder", 42.5, HC, 0, DARK, roll=90, width=2 * HR - 1, depth=2 * HR - 1, height=5.0, segments=48, material="Metal")
    for (z, y) in ((-3.2, HC + 1.8), (3.2, HC + 1.8), (0, HC - 2.4)):
        mesh_at("cone", 47.0, y, z, STEEL, roll=90, width=4.4, depth=4.4, height=5.0, segments=24)
        mesh_at("sphere", 50.0, y, z, ORANGE, width=1.6, height=1.6, depth=1.6, segments=12, detail=6)
    # Keel deck (floor inside the hull, out through the ribs to both ends) and the spine on top.
    block(-40, KY - 0.4, -5.2, 40, KY, 5.2, DECK2, "Metal")
    block(-40, SY - 0.5, -1.5, 40, SY, 1.5, DECK, "Metal")
    # Crown deck in the middle of the spine (hill 1), where the hatch stairs from inside arrive.
    block(-7.0, SY - 0.5, -4.5, 7.0, SY, 4.5, DECK, "Metal")
    for s in (-1, 1):
        mesh("box", 0, s * 3.4, HC + 4.6, STEEL, width=12, height=1.2, depth=0.3)
    # Interior walls (hidden: the tube draws them), with the cradle-arm doors at x = -+12 on both sides, and the ceiling
    # with holes for the two hatch stairs.
    for s in (-1, 1):
        z0, z1 = s * 5.2, s * 5.6
        for (x0, x1) in ((-PLATED, -13.2), (-10.8, 10.8), (13.2, PLATED)):
            collider(x0, KY, z0, x1, 26.2, z1)
        for x in (-12, 12):
            collider(x - 1.2, KY + 2.5, z0, x + 1.2, 26.2, z1)
    collider(-PLATED, 26.2, -5.6, -11.9, 26.6, 5.6)
    collider(-11.9, 26.2, -5.6, -6.9, 26.6, 1.3)
    collider(-6.9, 26.2, -5.6, 6.9, 26.6, 5.6)
    collider(6.9, 26.2, -1.3, 11.9, 26.6, 5.6)
    collider(11.9, 26.2, -5.6, PLATED, 26.6, 5.6)
    # Lamps along the inside and along the spine.
    for x in range(-21, 22, 6):
        mesh("box", x, 0, 26.0, LAMP, width=0.5, height=0.08, depth=1.2)
    # Spine railings, cut at the gantries (x = -+20), the crown deck and the scaffold stairs' landings.
    for s in (-1, 1):
        railing(-40, s * 1.5, SY, 40, s * 1.5, SY, gaps=[(20, 1.7), (60, 1.7), (40, 7.2), (40 - s * 9.8, 1.0)])
        railing(-7, s * 4.5, SY, 7, s * 4.5, SY)
        # The x = -7 edge is open where the west hatch stair arrives (z 1.5..4.5) and where the spine joins; x = +7 the same turned.
        railing(s * 7, -4.5, SY, s * 7, 4.5, SY, gaps=[(4.5, 1.6), (4.5 - s * 3.0, 1.6)])
    # Keel deck kick plates where the ribs are open (knee high: you can still jump over).
    for s in (-1, 1):
        for (x0, x1) in ((-40, -39.5), (-36.5, -33.5), (-30.5, -PLATED), (PLATED, 30.5), (33.5, 36.5), (39.5, 40)):
            collider(x0, KY, s * 5.0, x1, KY + 0.3, s * 5.3)
            mesh("box", (x0 + x1) / 2, s * 5.15, KY, HAZARD, width=x1 - x0, height=0.3, depth=0.3)
    # Hull number and yard logo on the plating, deck plate seams on the keel deck and the spine.
    for s in (-1, 1):
        onto = "wall-z" if s < 0 else "wall+z"  # the point is south (s = -1) or north of the hull, looking at it
        decal("Keelspar Hull Number 1", -16 * s + 1.5 * s, s * -8.5, HC + 1.0, onto=onto, size=2.6, reach=3.5)
        decal("Keelspar Hull Number 2", -16 * s - 1.5 * s, s * -8.5, HC + 1.0, onto=onto, size=2.6, reach=3.5)
        decal("Meridian Hullworks Logo", 17 * s, s * -8.5, HC + 1.4, onto=onto, size=2.6, reach=3.5)
    decal("Meridian Hullworks Logo", 0, 0, SY, size=4.0, rotation=90)
    decal_scatter(["Keelspar Deck Plates"], -39, -4.5, 39, 4.5, 26, seed=5, y=KY, gap=3.0)
    decal_scatter(["Keelspar Weld Scorch", "global:Rust Stain", "global:Oil Stain"], -23, -4.5, 23, 4.5, 18, seed=6, y=KY, gap=1.5)
    for x in (-20, 20):
        decal("Keelspar Crane Mark", x, 0, SY, size=4.5, rotation=0)
    # Waypoints: the keel deck, the spine and the crown.
    waypoint_grid(-39, -4, 39, 4, KY, spacing=9.0)
    for x in range(-38, 39, 8):
        waypoint(x, 0, SY)
    for (x, z) in ((-4, 3), (4, -3), (-4, -3), (4, 3)):
        waypoint(x, z, SY)


def underworld():
    """Cosmetic structure far below the decks (depth for the drop) and the dock's cradle."""
    for x in (-60, -40, -20, 0, 20, 40, 60):
        for z in (-20, 20):
            mesh("cylinder", x, z, 0.0, STEEL, width=1.4, height=QY - 0.8, depth=1.4, segments=12)
    for z in (-20, 20):
        mesh("box", 0, z, 2.0, DARK, width=150, height=1.2, depth=1.6)
    for x in (-60, -30, 0, 30, 60):
        mesh("box", x, 0, 2.0, DARK, width=1.6, height=1.2, depth=70)
    for x in (-34, -22, -10, 2, 14, 26, 34):
        mesh("box", x, 0, KY - 4.2, DARK, width=2.4, height=3.6, depth=9.0)
        mesh("box", x, 0, 3.2, STEEL, width=1.0, height=KY - 7.5, depth=1.0)
    for s in (-1, 1):
        mesh("box", 0, s * 8.2, QY - 3.5, STEEL, width=100, height=1.0, depth=0.8)


# ================================================================================================ the half (turned for the other)
def north_quay():
    z0, z1 = -29.0, -10.0
    block(-50, QY - 0.8, z0, 50, QY, z1, DECK, "Metal")
    for s in (-1, 1):
        mesh("box", s * 49.2, (z0 + z1) / 2, QY - 1.2, RIM, width=1.6, height=0.5, depth=z1 - z0)
    # Railings: the inner edge cut at the rib gangways (-+32, -+38) and the cradle arms (-+12); the outer edge solid.
    railing(-50, z1, QY, 50, z1, QY, gaps=[(50 + x, 1.5) for x in (-38, -32, 32, 38)] + [(50 + x, 1.4) for x in (-12, 12)])
    railing(-50, z0, QY, 50, z0, QY, gaps=[(32.6, 1.2), (67.4, 1.2)])  # the gantry towers' feet
    # Rib gangways down onto the open keel deck, and the cradle arms into the hull's side doors.
    for x in (-38, -32, 32, 38):
        gangway(x, z1, QY, x, -5.2, KY, 2.6)
    for x in (-12, 12):
        gangway(x, z1, QY, x, -5.3, KY, 2.2)
        mesh("box", x, -7.8, KY - 1.6, STEEL, width=2.6, height=1.2, depth=4.6)
    # Dock office (two storeys, roof access) and the parts store.
    building("Station Crew Quarters", -33, QY, -20, 0, 651, variant={"palette": "white hull"})
    names, tw, th = sdec.tiles("dock_office")
    sign_row(names, tw, -33, -14.9, QY + 2.75, "wall-z")
    building("Refinery Pump House", -12, QY, -20, 0, 652, variant={"palette": "bronze hull"})
    names, tw, th = sdec.tiles("parts_store")
    sign_row(names, tw, -12, -14.9, QY + 3.3, "wall-z")
    names, tw, th = sdec.tiles("north_quay")
    sign_row(names, tw, -45, z0 + 0.1, QY + 1.8, "wall+z", on_rail=True)
    # Scaffold deck hung along the plated hull, with a stair at each end.
    scaffold()
    # Weld bays by the hull edge, plate stores along the back, cover everywhere.
    for (n, x, z, yw) in [("Welding Rig", -4, -12.5, 90), ("Welding Rig", -18, -12.3, 60), ("Welder Bot", -9, -13.5, 0), ("Welder Bot", 4, -12.6, 120),
                          ("Hull Plate Stack", 4, -14.5, 0), ("Hull Plate Stack", 24, -12.8, 0), ("Keel Block", -22, -13, 0), ("Keel Block", 18, -12.5, 0),
                          ("Hull Plate Rack", -8, -27.2, 0), ("Hull Plate Rack", 22, -27.2, 0), ("Hull Plate Rack", 40, -27.2, 0),
                          ("Parts Bin Rack", 2, -27.5, 0), ("Parts Bin Rack", -2, -27.5, 0), ("Parts Bin Rack", 46, -27.5, 0),
                          ("Yard Tool Chest", -5, -17.5, 30), ("Yard Tool Chest", 26, -16, 0), ("Yard Tool Chest", 12, -14, 80),
                          ("Station Cargo Pod", 30, -20, 0), ("Station Cargo Pod", 44, -20, 90), ("Station Cargo Pod", -46, -20, 90),
                          ("Supply Case Stack", 36, -24, 0), ("Supply Case Stack", -44, -26, 90), ("Station Supply Case", 33, -14, 20),
                          ("Station Supply Case", -27, -13, 0), ("Rivet Crate", 28, -24, 0), ("Rivet Crate", 29, -24.9, 40), ("Rivet Crate", -12, -24, 0),
                          ("Station Pipe Run", 40, -13.5, 0), ("Station Pipe Run", -42, -13.5, 0), ("Station Pipe Riser", 37, -27, 0),
                          ("Work Light Mast", -4, -24, 180), ("Work Light Mast", 20, -15, 0), ("Work Light Mast", -40, -24, 180), ("Work Light Mast", 46, -14, 0),
                          ("Deck Light Post", -48, -28, 0), ("Deck Light Post", 10, -28, 0), ("Deck Light Post", 48, -28, 0),
                          ("Cable Spool", 14, -25, 0), ("Gas Cylinders", -14, -27, 0), ("Pipe Stack", 30, -28, 0), ("Oil Drum", 34, -27, 0),
                          ("Pallet Stack", -24, -27, 0), ("Scaffold Tower", 42, -22, 0), ("Hazard Bollard", -29, -11, 0), ("Hazard Bollard", -35, -11, 0),
                          ("Hazard Bollard", 29, -11, 0), ("Hazard Bollard", 35, -11, 0), ("Hazard Bollard", 9, -11, 0), ("Hazard Bollard", 15, -11, 0),
                          ("Station Deck Barrier", -14, -23, 90), ("Station Deck Barrier", 20, -24, 0), ("Station Deck Barrier", -24, -17, 90)]:
        prop(n, x, z, QY, yw)
    decal_scatter(["Keelspar Deck Plates"], -48, -28, 48, -11, 70, seed=7, y=QY, gap=3.2)
    decal_scatter(["Keelspar Weld Scorch", "Keelspar Weld Scorch", "global:Oil Stain", "global:Rust Stain", "global:Scorch Mark"], -26, -16, 26, -11, 16, seed=8, y=QY, gap=1.6)
    for k, x in enumerate((-20, -5, 10, 25)):
        decal(f"Keelspar Bay Number {k + 1}", x, -21, QY, size=2.2, rotation=90)
    decal("global:Hazard Stripes", -32, -11.2, QY, size=2.8, rotation=0)
    decal("global:Hazard Stripes", 32, -11.2, QY, size=2.8, rotation=0)
    waypoint_grid(-48, -28, 48, -11, QY, spacing=9.0, keep=lambda x, z: not (-39.5 < x < -26.5 and -25.5 < z < -14.5) and not (-16.5 < x < -7.5 and -25.5 < z < -14.5))
    for (x, z) in ((-33, -12.5), (-33, -27), (-12, -12.5), (-12, -27), (-26, -20), (-40, -20), (-6, -20), (-18, -20)):
        waypoint(x, z, QY)


def sign_row(names, tw, cx, z, y, onto, on_rail=False):
    n = len(names)
    for i, nm in enumerate(names):
        off = (i - (n - 1) / 2) * tw
        x = cx + off if onto == "wall-z" else cx - off
        if on_rail:
            mesh("box", x, z, y - 0.5, DARK, width=tw, height=1.0, depth=0.12)
            decal(nm, x, z - 0.4 if onto == "wall+z" else z + 0.4, y, onto=onto, size=tw, reach=1.5)
        else:
            decal(nm, x, z, y, onto=onto, size=tw, reach=2.0)


def scaffold():
    """The north scaffold deck at 24 m along the plated hull, a stair at each end from the quay, and a stair at x = 10
    up over the hull curve onto the spine."""
    block(-PLATED, SC - 0.3, -10.0, PLATED, SC, -6.2, GRID, "Grid")
    for x in range(-22, 23, 4):
        mesh("box", x, -8.1, SC - 1.6, STEEL, width=0.2, height=1.3, depth=3.6)
        mesh("cylinder", x, -9.8, QY, STEEL, width=0.14, height=SC - QY - 0.3, depth=0.14, segments=8)
    railing(-PLATED, -10.0, SC, PLATED, -10.0, SC)
    for s in (-1, 1):
        # Flights on the quay beside its edge, up to a landing joined to the scaffold's end.
        x, z, y = stairs(s * 30.0, -11.2, QY, SC - QY, -s, 0, width=1.6, riser=1 / 3, tread=0.35)
        block(min(s * 24, s * 25.8), SC - 0.3, -12.0, max(s * 24, s * 25.8), SC, -6.2, GRID, "Grid")
        railing(s * 25.8, -10.4, SC, s * 25.8, -6.2, SC)
        railing(s * 24, -12.0, SC, s * 25.8, -12.0, SC)
        railing(s * 24, -12.0, SC, s * 24, -10.0, SC)
        waypoint(s * 25, -9, SC)
    railing(-PLATED, -6.2, SC, PLATED, -6.2, SC, gaps=[(PLATED + 9.8, 1.2)])
    # The stair up the hull's flank to the spine at x = 10 (its landing hangs over the hull curve).
    stairs(9.8, -6.6, SC, SY - SC, 0, 1, width=1.6, riser=1 / 3, tread=0.35)
    block(9.0, SY - 0.5, -2.6, 10.6, SY, -1.5, DECK, "Metal")
    railing(9.0, -2.6, SY, 9.0, -1.5, SY)
    railing(10.6, -2.6, SY, 10.6, -1.5, SY)
    for s in (-1, 1):
        mesh("box", 9.8 + s * 0.85, -4.5, SC, STEEL, width=0.12, height=4.2, depth=0.12)
    for (n, x, z, yw) in [("Welding Rig", -20, -8, 0), ("Welding Rig", 16, -8.2, 0), ("Rivet Crate", -4, -7.2, 0), ("Rivet Crate", 20, -9, 30),
                          ("Yard Tool Chest", -12, -8.5, 0), ("Station Supply Case", 5, -7.5, 0), ("Hazard Bollard", 9, -9.5, 0), ("Hazard Bollard", 11, -9.5, 0)]:
        prop(n, x, z, SC, yw)
    decal_scatter(["Keelspar Weld Scorch", "global:Rust Stain"], -22, -9.5, 22, -6.8, 10, seed=9, y=SC, gap=2.0)
    names, tw, th = sdec.tiles("weld_bay")
    for i, nm in enumerate(names):
        decal(nm, -14 + (i - (len(names) - 1) / 2) * tw, -5.9, SC + 1.6, onto="wall+z", size=tw, reach=2.0)
    for x in range(-21, 22, 6):
        waypoint(x, -8.1, SC)


def gantry(gx):
    """A gantry crane across the dock at x = gx: a 3 m bridge at spine height from a stair tower on each quay, meeting
    the spine, under a crane beam with a trolley and hook."""
    block(gx - 1.5, SY - 0.5, -29, gx + 1.5, SY, 29, DECK, "Metal")
    for s in (-1, 1):
        railing(gx + s * 1.5, -29, SY, gx + s * 1.5, 29, SY, gaps=[(29, 1.7)])
        tower(gx, s)
        # Legs on each quay, under the beam.
        block(gx - 3.6, QY, s * 27 - 1, gx - 1.6, 36.0, s * 27 + 1, ORANGE, "Metal")
        mesh("box", gx - 2.6, s * 27, 36.0, HAZARD, width=2.4, height=0.3, depth=2.4)
    mesh("box", gx - 2.6, 0, 36.0, ORANGE, width=2.2, height=1.6, depth=62, material="Metal")
    mesh("box", gx + 0.2, 0, 36.2, STEEL, width=2.0, height=0.6, depth=58)
    for z in (-26, 26):
        mesh("box", gx + 0.2, z, 36.8, HAZARD, width=2.2, height=0.3, depth=2.2)
    # Trolley and hook over the hull.
    tz = -8.0 if gx < 0 else 8.0
    mesh("box", gx - 1.2, tz, 34.6, STEEL, width=3.6, height=1.4, depth=2.2)
    mesh("cylinder", gx - 1.2, tz, 31.2, DARK, width=0.12, height=3.4, depth=0.12, segments=8)
    mesh("box", gx - 1.2, tz, 30.2, HAZARD, width=0.8, height=1.0, depth=0.5)
    mesh_at("torus", gx - 1.2, 29.6, tz, DARK, pitch=90, radius=0.45, inner=0.1, segments=16, detail=6)
    for z in range(-27, 28, 5):
        waypoint(gx, z, SY)
    for s in (-1, 1):
        decal("global:Hazard Stripes", gx, s * 27.5, SY, size=2.8, rotation=0)
        decal("Keelspar Crane Mark", gx - 2.6, s * 19, QY, size=4.5, rotation=0)


def tower(gx, s):
    """The stair tower from the quay (20 m) to a gantry's bridge (28 m) on the z-side `s`: two flights along Z beside
    the bridge's end, a landing between, a top landing joined to the bridge."""
    xa, xb = gx + 2.6, gx + 4.2  # flight A (up from the quay) and flight B (up to the bridge) centre lines
    za, zb = s * 29.0, s * 24.84
    stairs(xa, za, QY, 4.0, 0, -s, width=1.6, steps=13)
    block(gx + 1.7, SC - 0.5, min(zb, s * 22.8), gx + 5.1, SC, max(zb, s * 22.8), DECK, "Metal")
    railing(gx + 1.7, s * 22.8, SC, gx + 5.1, s * 22.8, SC)
    railing(gx + 5.1, s * 22.8, SC, gx + 5.1, zb, SC)
    railing(gx + 1.7, s * 22.8, SC, gx + 1.7, zb, SC)
    stairs(xb, zb, SC, 4.0, 0, s, width=1.6, steps=13)
    block(gx - 1.5, SY - 0.5, min(za, s * 31), gx + 5.1, SY, max(za, s * 31), DECK, "Metal")
    railing(gx - 1.5, s * 31, SY, gx + 5.1, s * 31, SY)
    railing(gx + 5.1, s * 31, SY, gx + 5.1, za, SY)
    railing(gx + 1.5, za, SY, gx + 3.4, za, SY)
    railing(gx - 1.5, s * 31, SY, gx - 1.5, za, SY)
    waypoint(gx + 3.4, s * 23.8, SC)
    waypoint(gx + 1.0, s * 30, SY)
    # A column under the tower and a sign.
    mesh("box", gx + 3.4, s * 26.9, QY, STEEL, width=0.3, height=SY - QY - 0.5, depth=0.3)
    mesh("box", gx + 3.4, s * 23.0, QY, STEEL, width=0.3, height=SC - QY - 0.5, depth=0.3)


def bow_dock():
    """Blue's end platform (x -70..-50): the control hub, the fabrication hangar, spawns, the flag, the ramps down from
    the hull."""
    x0, x1, z0, z1 = -70.0, -50.0, -32.0, 32.0
    block(x0, QY - 0.8, z0, x1, QY, z1, DECK, "Metal")
    mesh("box", -69.2, 0, QY - 1.2, RIM, width=1.6, height=0.5, depth=64)
    railing(x0, z0, QY, x0, z1, QY)
    railing(x0, z0, QY, x1, z0, QY)
    railing(x0, z1, QY, x1, z1, QY)
    railing(x1, -10, QY, x1, 10, QY, gaps=[(10, 1.5)])
    railing(x1, z0, QY, x1, -29, QY)
    railing(x1, 29, QY, x1, z1, QY)
    # Buildings: the hub (spawn anchors, north), the fab shop hangar (south).
    building("Station Control Hub", -61, QY, -17, 0, 653)
    building("Station Hangar Bay", -60, QY, 17, 0, 654, variant={"palette": "bronze hull"})
    names, tw, th = sdec.tiles("bow_dock")
    sign_row(names, tw, -61, -10.9, QY + 3.1, "wall-z")
    names, tw, th = sdec.tiles("fab_shop")
    sign_row(names, tw, -60, 9.9, QY + 6.15, "wall-z")
    # Ramps: the spine ramp down from the hull's end (28 -> 20) over the keel gangway (18 -> 20).
    gangway(-40, 0, SY, -58, 0, QY, 3.0, color=DECK)
    gangway(-40, 0, KY, -50, 0, QY, 3.2)
    for x in (-44, -50, -56):
        mesh("box", x, 0, QY if x > -50 else QY - 0.8, STEEL, width=0.3, height=SY - 8 * (-40 - x) / 18 - 0.4 - (QY if x > -50 else QY - 0.8), depth=0.3)
    # The flag and the spawns.
    FLAGS.append([-64.0, QY, 0.0])
    for (x, z) in [(-66, -28), (-58, -28), (-52, -28), (-67, 28), (-53, 29), (-56, 25.5), (-68, -6), (-68, 6), (-52, -8), (-52, 8), (-56, 6), (-56, -6)]:
        spawn(0, x, z, QY)
    for (n, x, z, yw) in [("Yard Tug", -60, 28, 0), ("Station Cargo Pod", -54, -30, 0), ("Station Cargo Pod", -66, 30, 0), ("Supply Case Stack", -68, -29, 0),
                          ("Keel Block", -63, -27, 0), ("Hull Plate Rack", -68, 10, 90), ("Parts Bin Rack", -68, -10, 90),
                          ("Hull Plate Stack", -53, -13, 90), ("Welder Bot", -53, 25, 0), ("Yard Tool Chest", -52, 12, 0), ("Rivet Crate", -64, 31, 0),
                          ("Work Light Mast", -69, -30, 0), ("Work Light Mast", -69, 30, 0), ("Work Light Mast", -51, -30, 180), ("Work Light Mast", -51, 30, 180),
                          ("Deck Light Post", -60, -31, 0), ("Deck Light Post", -60, 31, 0), ("Hazard Bollard", -50.8, -9, 0), ("Hazard Bollard", -50.8, 9, 0),
                          ("Hazard Bollard", -50.8, -3, 0), ("Hazard Bollard", -50.8, 3, 0), ("Station Deck Barrier", -62, -8, 90), ("Station Deck Barrier", -62, 8, 90),
                          ("Station Console", -67, -4, 90), ("Fuel Coupling", -52, 30, 270), ("Antenna Mast", -68, -26, 0)]:
        prop(n, x, z, QY, yw)
    decal("Meridian Hullworks Logo", -64, 0, QY, size=4.0, rotation=90)
    decal_scatter(["Keelspar Deck Plates"], -69, -31, -51, 31, 50, seed=10, y=QY, gap=3.2)
    decal_scatter(["global:Oil Stain", "Keelspar Weld Scorch", "global:Rust Stain"], -69, 24, -51, 31, 8, seed=11, y=QY, gap=2.0)
    waypoint_grid(-69, -31, -51, 31, QY, spacing=9.0, keep=lambda x, z: not (-69.5 < x < -52.5 and -23.5 < z < -10.5) and not (-70.5 < x < -49.5 and 9.5 < z < 24.5))
    for (x, z) in ((-61, -9), (-56, 8), (-64, 8), (-56, 26), (-64, 26), (-56, -26), (-61, -26)):
        waypoint(x, z, QY)


FLAGS = []


def interior_stair():
    """The west hatch stair inside the hull: from the keel deck (18 m) up two flights beside the spine to the crown
    deck (28 m), under a hatch housing."""
    x, z, y = stairs(-18.5, 3.0, KY, 5.0, 1, 0, width=3.0, steps=15)
    block(-13.7, 23.0 - 0.3, 1.5, -11.7, 23.0, 4.5, DECK2, "Metal")
    waypoint(-12.7, 3.0, 23.0)
    stairs(-11.7, 3.0, 23.0, 5.0, 1, 0, width=3.0, steps=15)
    mesh("box", -9.4, 3.0, 25.4, HULL2, width=5.4, height=2.8, depth=3.6, material="Metal")
    mesh("box", -9.4, 3.0, 28.2, RIM, width=5.6, height=0.2, depth=3.8)
    collider(-13.7, 23.0, 1.5, -11.7, 24.0, 1.62)  # the landing's railing on the spine side (a low lip)
    for (n, x, z, yw) in [("Station Cargo Pod", -4, -3.2, 0), ("Station Supply Case", -16, -3.5, 20), ("Welding Rig", -9, -3.4, 90),
                          ("Station Console", -22, 3.2, 180), ("Station Pipe Riser", -15, -4.5, 0), ("Rivet Crate", -2, -4.3, 0),
                          ("Hull Plate Stack", -30, 3.2, 0), ("Keel Block", -36, -3.2, 0), ("Welder Bot", -33, 2.6, 0), ("Yard Tool Chest", -27, -3.8, 0),
                          ("Hazard Bollard", -39.2, 4.6, 0), ("Hazard Bollard", -39.2, -4.6, 0)]:
        prop(n, x, z, KY, yw)
    names, tw, th = sdec.tiles("no_step")
    for i, nm in enumerate(names):
        decal(nm, -9.4 + (i - (len(names) - 1) / 2) * tw, 1.3, 26.9, onto="wall+z", size=tw, reach=1.0)


# ================================================================================================ turning the half
def rotated(ops):
    out = []
    flip = {"wall+x": "wall-x", "wall-x": "wall+x", "wall+z": "wall-z", "wall-z": "wall+z"}
    for o in ops:
        d = dict(o)
        k = d["op"]
        if k in ("Block", "Collider"):
            d["x0"], d["x1"] = K.r(-o["x1"]), K.r(-o["x0"])
            d["z0"], d["z1"] = K.r(-o["z1"]), K.r(-o["z0"])
        elif k in ("Mesh", "Prop", "Decal", "Waypoint", "Spawn"):
            d["x"], d["z"] = K.r(-o["x"]), K.r(-o["z"])
            if k in ("Mesh", "Prop"):
                d["yaw"] = K.r((o.get("yaw", 0) + 180) % 360, 1)
            if k == "Decal":
                if "rotation" in o:
                    d["rotation"] = K.r((o["rotation"] + 180) % 360, 1)
                if o.get("onto") in flip:
                    d["onto"] = flip[o["onto"]]
        elif k in ("DecalScatter", "Clutter"):
            d["x0"], d["x1"] = K.r(-o["x1"]), K.r(-o["x0"])
            d["z0"], d["z1"] = K.r(-o["z1"]), K.r(-o["z0"])
            d["seed"] = o.get("seed", 0) + 100
        elif k == "Building":
            at = o["at"]
            d["at"] = [K.r(-at[0]), at[1], K.r(-at[2])] if len(at) == 3 else [K.r(-at[0]), K.r(-at[1])]
            d["yaw"] = (o.get("yaw", 0) + 180) % 360
            d["seed"] = o.get("seed", 1) + 100
        out.append(d)
    return out


def boundary_points():
    half = [(-77, 0), (-76, -16), (-73, -30), (-66, -38), (-54, -37), (-40, -34), (-20, -33), (0, -33), (20, -33), (40, -34),
            (54, -37), (66, -38), (73, -30), (76, -16), (77, 0)]
    pts = list(half) + [(-x, -z) for (x, z) in half[1:-1]]
    return [[x, z] for (x, z) in pts]


def build():
    hull()
    underworld()
    shared = list(K.OPS)
    K.OPS.clear()
    north_quay()
    gantry(-20)
    bow_dock()
    interior_stair()
    half = list(K.OPS)
    other = rotated(half)
    K.OPS.clear()
    K.OPS.extend(shared + half + other)
    # Red's spawns and flag are Blue's turned.
    K.SPAWNS[1] = [[K.r(-x), y, K.r(-z)] for (x, y, z) in K.SPAWNS[0]]
    FLAGS.append([K.r(-FLAGS[0][0]), FLAGS[0][1], K.r(-FLAGS[0][2])])


def main():
    K.reset()
    build()
    m = {
        "format": "call-of-slop-map/1",
        "name": "Keelspar Drydock",
        "description": "An orbital shipyard: a half-built hull in its cradle between two quays, gantry cranes over the spine, and the void under it all.",
        "author": "Call of Slop",
        "order": 605,
        "destructible": False,
        "fallKill": FALL_KILL,
        "maxPlayers": 16,
        "skybox": {"preset": "night", "zenith": "#03040c", "horizon": "#0a0d1a", "ground": "#121620", "stars": 2.0, "clouds": False,
                   "sun": {"elevation": 32, "azimuth": 205, "intensity": 1.15, "color": "#fff2dc", "size": 1.1, "glow": 0.3},
                   "moon": {"direction": [0.55, 0.14, 0.82], "size": 28, "color": "#9fb6d4", "intensity": 0.25},
                   "ambient": 0.95, "fog": {"max": 0.35}},
        "boundary": {"shape": "smooth", "points": boundary_points(), "edge": "none", "barrierHeight": 1.0},
        "teamSpawns": [K.SPAWNS[0], K.SPAWNS[1]],
        "hills": [{"x": 0, "z": 0, "y": SY, "radius": 4.5},
                  {"x": 0, "z": -20, "y": QY, "radius": 4.5},
                  {"x": 0, "z": 20, "y": QY, "radius": 4.5}],
        "flags": FLAGS,
        "ops": K.OPS,
    }
    K.write_map(OUT_JSON, m)
    if OUT_PNG:
        plan(OUT_PNG)


def plan(path):
    from PIL import Image, ImageDraw
    S = 4
    W, H = 170 * S, 90 * S
    img = Image.new("RGB", (W, H), (20, 20, 30))
    d = ImageDraw.Draw(img)

    def P(x, z):
        return (W / 2 + x * S, H / 2 + z * S)
    d.polygon([P(x, z) for x, z in boundary_points()], outline=(120, 120, 160))
    for o in K.OPS:
        if o["op"] in ("Block", "Collider"):
            c = int(60 + (o["y1"] - 16) * 12)
            d.rectangle([P(o["x0"], o["z0"]), P(o["x1"], o["z1"])], fill=(c, c, c - 10) if o["op"] == "Block" else (c // 2, c // 2, c))
    for o in K.OPS:
        if o["op"] == "Waypoint":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 2, z - 2, x + 2, z + 2], fill=(80, 200, 255))
        if o["op"] == "Spawn":
            x, z = P(o["x"], o["z"])
            d.ellipse([x - 3, z - 3, x + 3, z + 3], fill=(80, 255, 80) if o["x"] < 0 else (255, 80, 80))
        if o["op"] == "Prop":
            x, z = P(o["x"], o["z"])
            d.rectangle([x - 2, z - 2, x + 2, z + 2], fill=(255, 200, 60))
    img.save(path)
    print("plan:", path)


if __name__ == "__main__":
    main()
