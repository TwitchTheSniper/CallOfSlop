#!/usr/bin/env python3
"""Writes the Sandbox's assembling data: Constructs/Assembling.json and the sub-components in Constructs/Parts/Components.

docs/sandbox/CONSTRUCTS.md, "Assembling". HAND holds the recipes written by hand: every sub-component and the parts a
player reaches for first, each drawing on different production chains. The long tail is generated from the part files
by rule: a part costs half its mass in its class's material (long_tail), its role adds what a part of that kind must
have (a Circuit Board in anything electrical, a Gear Set in a gearbox, a Servo Motor in an actuator), and the materials
in it set the tier. Rules, not a list, so a new part gets a recipe by running this.

Run it from the repo root: python3 -I tools/constructs/assembling.py (it rewrites the files; --check only compares).
Data first: the JSON files are what the game loads; this script only keeps them consistent. Fix it, not its output.
"""

import glob
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zfight  # noqa: E402

FORMAT = "call-of-slop-assembling/1"
ABOUT = ("Assembling (docs/sandbox/CONSTRUCTS.md, 'Assembling'): what an Assembler builds. Written by tools/constructs/assembling.py: fix it, not this file. "
         "'output' names a part, 'count' how many a batch makes (default 1), 'materials' the bulk materials (Recycling.json) and 'parts' the item parts it takes, "
         "'seconds' the work at speed 1, 'minTier' the lowest tier of Assembler (default 1), 'machine' the family that builds it, when not the plain Assembler. At most 6 inputs; a physics-only part can't be an input.")

# Components: small item parts, each one step in a chain. (name, file, mass, colour, boxes)
COMPONENTS = [
    ("Gear Set", "GearSet", 6, "#7f8a94", [((-0.12, 0, -0.12), (0.12, 0.08, 0.12)), ((-0.07, 0.08, -0.07), (0.07, 0.15, 0.07))],
     "Steel gears for gearboxes and drives."),
    ("Bearing", "Bearing", 1.5, "#a8763a", [((-0.07, 0, -0.07), (0.07, 0.05, 0.07))],
     "A bronze bearing."),
    ("Piston Set", "PistonSet", 12, "#6f7d8c", [((-0.15, 0, -0.1), (0.15, 0.22, 0.1)), ((-0.11, 0.22, -0.07), (0.11, 0.3, 0.07))],
     "Pistons and rods for engines."),
    ("Copper Coil", "CopperCoil", 4, "#c9794a", [((-0.1, 0, -0.1), (0.1, 0.16, 0.1)), ((-0.13, 0.05, -0.13), (0.13, 0.11, 0.13))],
     "For motors and alternators."),
    ("Wiring Loom", "WiringLoom", 3, "#2c2c2e", [((-0.16, 0, -0.04), (0.16, 0.06, 0.04)), ((-0.04, 0.06, -0.12), (0.04, 0.1, 0.12))],
     "Copper wire in rubber."),
    ("Circuit Board", "CircuitBoard", 1, "#3f9b5c", [((-0.12, 0, -0.09), (0.12, 0.02, 0.09)), ((-0.05, 0.02, -0.04), (0.05, 0.05, 0.04))],
     "Circuits on a laminate board."),
    ("Control Unit", "ControlUnit", 4, "#30343a", [((-0.12, 0, -0.1), (0.12, 0.14, 0.1)), ((-0.05, 0.14, -0.05), (0.05, 0.17, 0.05))],
     "For powered machines."),
    ("Servo Motor", "ServoMotor", 8, "#55705a", [((-0.1, 0, -0.14), (0.1, 0.2, 0.14)), ((-0.04, 0.05, 0.14), (0.04, 0.13, 0.22))],
     "Moves an actuator."),
    ("Tyre Carcass", "TyreCarcass", 9, "#2c2c2e", [((-0.25, 0, -0.1), (0.25, 0.5, 0.1)), ((-0.1, 0.1, -0.11), (0.1, 0.4, 0.11))],
     "Tyre, ready for a rim."),
    ("Steel Plate", "SteelPlate", 8, "#6f7d8c", [((-0.25, 0, -0.25), (0.25, 0.03, 0.25))],
     "Rolled steel plate."),
    ("Lens", "Lens", 0.5, "#8fc5d9", [((-0.05, 0, -0.05), (0.05, 0.1, 0.05)), ((-0.04, 0.1, -0.04), (0.04, 0.12, 0.04))],
     "For scopes, sensors and lamps."),
    ("Turbine Stage", "TurbineStage", 6, "#9aa3ad", [((-0.14, 0, -0.14), (0.14, 0.1, 0.14)), ((-0.05, 0.1, -0.05), (0.05, 0.16, 0.05))],
     "A ring of hot blades for a jet."),
    ("Thrust Chamber", "ThrustChamber", 8, "#5b5048", [((-0.1, 0, -0.1), (0.1, 0.2, 0.1)), ((-0.15, 0.2, -0.15), (0.15, 0.32, 0.15))],
     "A combustion chamber and its throat, for a rocket."),
    ("Field Coil", "FieldCoil", 5, "#5c6fa8", [((-0.13, 0, -0.13), (0.13, 0.08, 0.13)), ((-0.07, 0.08, -0.07), (0.07, 0.14, 0.07))],
     "A tuned coil that pushes against the ground."),
    ("Levite Core", "LeviteCore", 3, "#a78fd0", [((-0.1, 0, -0.1), (0.1, 0.03, 0.1)), ((-0.07, 0.03, -0.07), (0.07, 0.18, 0.07))],
     "Levite in a cage of circuits: the heart of an Anti-Grav Generator."),
]


def rec(output, seconds, tier=1, count=1, m=None, p=None):
    r = {"output": output}
    if count > 1:
        r["count"] = count
    if m:
        r["materials"] = m
    if p:
        r["parts"] = p
    r["seconds"] = seconds
    if tier > 1:
        r["minTier"] = tier
    return r


# Every one of these draws on at least two chains (ore to metal to steel; wood to planks to rubber and cloth; copper; sand to glass; gold).
HAND = [
    # Components.
    rec("Gear Set", 4, m={"steel": 5}, p=None),
    rec("Bearing", 2, 2, count=2, m={"bronze": 2}),
    rec("Piston Set", 8, 2, m={"steel": 4, "bronze": 2}),
    rec("Copper Coil", 3, 2, m={"copper": 3}),
    rec("Wiring Loom", 3, 2, m={"copper": 2, "rubber": 1}),
    rec("Circuit Board", 4, 3, m={"circuits": 2, "laminate": 1}),
    rec("Control Unit", 6, 3, p={"Circuit Board": 1, "Wiring Loom": 1}, m={"steel": 1}),
    rec("Servo Motor", 6, 2, m={"steel": 2}, p={"Copper Coil": 1, "Bearing": 2}),
    rec("Tyre Carcass", 5, m={"rubber": 4, "cloth": 2}),
    rec("Steel Plate", 2, count=2, m={"steel": 5}),
    rec("Lens", 3, 2, m={"glass": 1, "bronze": 1}),
    rec("Turbine Stage", 6, 2, m={"steel": 4, "bronze": 2}),
    rec("Thrust Chamber", 5, m={"steel": 5, "concrete": 1}),
    rec("Field Coil", 6, 3, m={"copper": 6, "circuits": 1}),
    rec("Levite Core", 12, 4, m={"levite": 4, "circuits": 2}),
    # Drivetrain.
    rec("Engine", 20, 2, m={"steel": 20}, p={"Piston Set": 2, "Wiring Loom": 1}),
    rec("Small Engine", 10, 2, m={"steel": 6}, p={"Piston Set": 1, "Wiring Loom": 1}),
    rec("V8 Engine", 40, 3, m={"steel": 45}, p={"Piston Set": 4, "Wiring Loom": 2, "Bearing": 4}),
    rec("Diesel Engine", 50, 3, m={"steel": 70}, p={"Piston Set": 6, "Wiring Loom": 2, "Bearing": 6}),
    rec("Turbine", 60, 4, m={"steel": 25, "bronze": 10}, p={"Bearing": 8, "Control Unit": 1}),
    rec("Electric Motor", 15, 3, m={"steel": 10}, p={"Copper Coil": 4, "Bearing": 2, "Control Unit": 1}),
    rec("Transmission", 12, m={"steel": 8}, p={"Gear Set": 2}),
    rec("Differential", 12, 2, m={"steel": 10}, p={"Gear Set": 2, "Bearing": 2}),
    rec("Suspension", 5, m={"steel": 3, "rubber": 1}),
    rec("Wheel", 5, m={"steel": 2}, p={"Tyre Carcass": 1}),
    rec("Small Wheel", 3, m={"rubber": 3, "steel": 1}),
    rec("Large Wheel", 8, m={"steel": 4}, p={"Tyre Carcass": 2}),
    rec("Monster Wheel", 30, 3, m={"steel": 20}, p={"Tyre Carcass": 8}),
    rec("Fuel Tank", 6, m={"steel": 6, "rubber": 1}),
    rec("Fuel Line", 2, count=2, m={"rubber": 1, "copper": 1}),
    rec("Alternator", 8, 2, m={"steel": 1}, p={"Copper Coil": 2, "Bearing": 1}),
    # Power and control.
    rec("Battery", 8, 3, m={"copper": 4, "circuits": 2}),
    rec("Battery Pack", 40, 4, m={"copper": 20, "steel": 12}, p={"Control Unit": 1}),
    rec("Seat", 5, m={"cloth": 3, "steel": 3}),
    rec("Driving Module", 3, m={"steel": 3, "rubber": 1}),  # (a first car needs one: no circuit boards, so a tier 1 Assembler makes it)
    rec("Turret Module", 3, m={"steel": 3, "rubber": 1}),
    rec("Lamp", 2, 2, m={"copper": 1}, p={"Lens": 1}),
    rec("Proximity Sensor", 4, 3, p={"Lens": 1, "Circuit Board": 1}),
    # Weapons.
    rec("Gun Barrel", 6, m={"steel": 2}),
    rec("Ammo Box", 5, 2, m={"steel": 3, "bronze": 3}),
    rec("Rifle Receiver", 10, 2, m={"steel": 6}, p={"Gear Set": 1, "Bearing": 2}),
    rec("Gun Scope", 4, 2, m={"steel": 1}, p={"Lens": 2}),
    rec("Mounted MG", 20, 3, m={"steel": 10, "bronze": 4}, p={"Gear Set": 1, "Control Unit": 1}),
    # Flame and harpoon: weapon builder parts, generated at load (ConstructWeaponParts.cs), so they are named here, not read from a file.
    rec("Firebreather Receiver", 10, 2, m={"steel": 6, "bronze": 2}, p={"Gear Set": 1}),
    rec("Flame Valve", 3, m={"steel": 1, "bronze": 1}),
    rec("Flame Nozzle", 4, m={"steel": 2}),
    rec("Fuel Canister", 3, m={"steel": 2}),
    rec("Twin Fuel Tanks", 5, 2, m={"steel": 3, "bronze": 1}),
    rec("Fuel Keg", 7, 2, m={"steel": 6, "bronze": 2}),
    rec("Napalm Gel", 3, 2, m={"rubber": 1, "steel": 1}),
    rec("Whaler Receiver", 10, 2, m={"steel": 7, "bronze": 2}, p={"Bearing": 2}),
    rec("Harpoon Trigger", 3, m={"steel": 1, "bronze": 1}),
    rec("Harpoon Tube", 5, m={"steel": 3}),
    rec("Harpoon Breech", 3, m={"steel": 2}),
    rec("Harpoon Rack", 5, 2, m={"steel": 3, "bronze": 1}),
    rec("Harpoon Drum", 7, 2, m={"steel": 5, "bronze": 2}, p={"Bearing": 1}),
    rec("Barbed Head", 3, m={"steel": 2}),
    # Artillery: weapon builder parts too.
    rec("Field Gun Receiver", 20, 3, m={"steel": 18, "bronze": 4}, p={"Gear Set": 2, "Bearing": 2}),
    rec("Howitzer Receiver", 30, 3, m={"steel": 28, "bronze": 6}, p={"Gear Set": 3, "Bearing": 2, "Control Unit": 1}),
    rec("Sliding Breech", 8, 2, m={"steel": 4, "bronze": 1}),
    rec("Interrupted Breech", 10, 2, m={"steel": 5, "bronze": 2}),
    rec("Howitzer Tube", 12, 2, m={"steel": 12}),
    rec("Long Gun Tube", 16, 3, m={"steel": 20}),
    rec("Loading Tray", 4, m={"steel": 3}),
    rec("Ready Rack", 8, 2, m={"steel": 6, "bronze": 1}),
    rec("Shell Hoist", 12, 3, m={"steel": 10}, p={"Gear Set": 1}),
    rec("High Explosive Shell", 4, 2, m={"steel": 3, "bronze": 1}),
    rec("Siege Shell", 7, 3, m={"steel": 5, "bronze": 2}),
    rec("Smoke Shell", 4, 2, m={"steel": 2, "rubber": 1}),
    rec("Incendiary Shell", 6, 3, m={"steel": 3, "rubber": 2}),
    # Machines and storage.
    rec("Materials Bin", 10, m={"steel": 8, "concrete": 2}),
    rec("Chest", 6, m={"planks": 8, "steel": 2}),
    rec("Recycler", 30, 2, m={"steel": 45}, p={"Gear Set": 4, "Wiring Loom": 2}),
    rec("Inventory Beacon", 25, 3, m={"steel": 16, "copper": 6}, p={"Control Unit": 2}),
    rec("Shelf Bay 0.5", 3, m={"planks": 2, "steel": 1}),
    rec("Shelf Bay 1", 4, m={"planks": 4, "steel": 1}),
    rec("Tall Shelf Bay 1", 5, m={"planks": 4, "steel": 2}),
    rec("Medium Shelf Bay 1", 6, m={"planks": 8, "steel": 3}),
    rec("Medium Shelf Bay 2", 8, m={"planks": 14, "steel": 5}),
    rec("Medium Shelf Bay 4", 12, m={"planks": 30, "steel": 12}),
    rec("Smart Terminal", 15, 2, m={"steel": 12, "glass": 2}, p={"Control Unit": 1, "Circuit Board": 2}),
    # The IT Department's hardware (tools/constructs/office_parts.py): the tech faction's, so circuit boards and tier 3 for what works.
    rec("Computer", 12, 3, m={"steel": 4, "glass": 2}, p={"Circuit Board": 2, "Control Unit": 1}),
    rec("Fax Machine", 10, 3, m={"steel": 3, "laminate": 1}, p={"Circuit Board": 1, "Copper Coil": 1}),
    rec("Server Rack", 15, 3, m={"steel": 8}, p={"Circuit Board": 3, "Wiring Loom": 2}),
    rec("Network Switch", 4, 3, m={"steel": 1, "copper": 1}, p={"Circuit Board": 1}),
    rec("Cable Reel", 3, 2, m={"copper": 3, "planks": 1}),
    rec("Satellite Dish", 4, 2, m={"steel": 2, "copper": 1}),
    # The Call Centre's kit (tools/constructs/callcentre_parts.py): cheap and plasticky, like everything they sell.
    rec("Cubicle Desk", 6, m={"planks": 4, "steel": 2, "laminate": 1}),
    rec("Water Cooler", 4, m={"laminate": 2, "glass": 1}),
    rec("Office Phone", 3, 2, m={"laminate": 1, "copper": 1}),
    rec("Call Headset", 2, 2, m={"laminate": 1, "copper": 1}),
    rec("Gift Card Sign", 2, m={"planks": 1, "laminate": 1}),
    rec("Prize Wheel", 3, m={"planks": 2, "laminate": 1}),
    rec("Loudhailer", 3, 2, m={"steel": 1, "copper": 1}),
    rec("Phone Mast", 4, 2, m={"steel": 2, "copper": 1}),
    # The Clan MacSlop's (tools/constructs/highland_parts.py): pipes are wood and a bag, a cask is staves and hoops.
    rec("Pipe Box", 8, 2, m={"planks": 4, "rubber": 2}, p={"Bearing": 1}),
    rec("Whisky Cask", 5, m={"planks": 10, "steel": 2}),
    # Air weapons (tools/constructs/air_weapons_parts.py): a rack and a bomb early, what steers itself late.
    rec("Light Bomb", 3, 2, m={"steel": 6, "concrete": 2}),
    rec("Bomb Rack", 4, 2, m={"steel": 5}),
    rec("Twin Bomb Rack", 6, 2, m={"steel": 9}),
    rec("AA Gun", 20, 3, m={"steel": 40}, p={"Gear Set": 1}),
    rec("Rocket Pod Mk2", 12, 3, m={"steel": 18, "copper": 3}),
    rec("Missile Rail", 20, 4, m={"steel": 20, "copper": 6}, p={"Control Unit": 1, "Servo Motor": 1}),
    rec("Flare Dispenser", 6, 2, m={"steel": 6, "copper": 1}),
    rec("AA Sight", 8, 3, m={"steel": 3, "glass": 2}, p={"Lens": 1, "Circuit Board": 1}),
    rec("Seeker Head", 12, 4, m={"steel": 3, "glass": 2}, p={"Lens": 2, "Control Unit": 1}),
    # Modular ordnance (tools/constructs/ordnance_parts.py).
    rec("Ordnance Bench", 15, 2, m={"steel": 30, "planks": 6}),
    rec("Impact Fuse", 3, 2, m={"steel": 2, "copper": 1}),
    rec("Delay Fuse", 4, 2, m={"steel": 2, "copper": 2}),
    rec("Warhead HE", 6, 2, m={"steel": 8, "concrete": 2}),
    rec("Warhead Blast", 7, 2, m={"steel": 6, "concrete": 3}),
    rec("Warhead Bunker", 9, 3, m={"steel": 16, "concrete": 2}),
    rec("Bomb Tail Fins", 3, 2, m={"steel": 3}),
    # Modular ordnance in flight: motors, tanks, steering, seekers, rails.
    rec("Solid Motor", 8, 3, m={"steel": 6, "concrete": 3}),
    rec("Boost Motor", 9, 3, m={"steel": 8, "concrete": 4}),
    rec("Liquid Motor", 12, 4, m={"steel": 8, "copper": 3}, p={"Gear Set": 1}),
    rec("Hybrid Motor", 12, 4, m={"steel": 8, "rubber": 3, "copper": 2}),
    rec("Cold Gas Thruster", 6, 3, m={"steel": 4, "copper": 1}),
    rec("Ramjet", 15, 4, m={"steel": 12, "bronze": 4}),
    rec("Turbojet", 18, 4, m={"steel": 10, "bronze": 4}, p={"Bearing": 2}),
    rec("Missile Fuel Tank", 4, 2, m={"steel": 4, "rubber": 1}),
    rec("Long Missile Fuel Tank", 6, 2, m={"steel": 7, "rubber": 1}),
    rec("Stage Separator", 4, 3, m={"steel": 3, "copper": 1}),
    rec("Body Tube", 3, 2, m={"steel": 3}),
    rec("Tail Fins", 3, 2, m={"steel": 3}),
    rec("Canards", 5, 3, m={"steel": 3}, p={"Servo Motor": 1}),
    rec("Gimbal Mount", 8, 4, m={"steel": 4}, p={"Servo Motor": 2}),
    rec("Jet Vanes", 5, 3, m={"steel": 2, "bronze": 2}),
    rec("Reaction Jets", 8, 4, m={"steel": 4, "copper": 2}, p={"Control Unit": 1}),
    rec("Heat Seeker", 10, 4, m={"steel": 2, "glass": 2}, p={"Lens": 1, "Circuit Board": 1}),
    rec("Radar Seeker", 12, 4, m={"steel": 3, "copper": 2}, p={"Circuit Board": 2}),
    rec("Command Link", 8, 3, m={"steel": 2, "copper": 1}, p={"Circuit Board": 1}),
    rec("Laser Seeker", 10, 4, m={"steel": 2, "glass": 1}, p={"Lens": 1, "Circuit Board": 1}),
    rec("GPS Unit", 10, 4, m={"steel": 2, "copper": 1}, p={"Control Unit": 1}),
    rec("Radio Homer", 8, 3, m={"steel": 2, "copper": 2}, p={"Circuit Board": 1}),
    rec("Beacon Homer", 8, 3, m={"steel": 2, "copper": 2}, p={"Circuit Board": 1}),
    rec("Proximity Fuse", 6, 3, m={"steel": 2, "copper": 1}, p={"Circuit Board": 1}),
    rec("Altitude Fuse", 6, 3, m={"steel": 2, "copper": 1}, p={"Circuit Board": 1}),
    rec("Timer Fuse", 4, 2, m={"steel": 2, "copper": 1}),
    rec("Airburst Fuse", 6, 3, m={"steel": 2, "copper": 1}, p={"Circuit Board": 1}),
    rec("Remote Fuse", 6, 3, m={"steel": 2, "copper": 2}, p={"Circuit Board": 1}),
    rec("Cluster Dispenser", 10, 3, m={"steel": 10, "concrete": 3}),
    rec("Fire Bomblets", 10, 3, m={"steel": 8, "concrete": 2, "rubber": 2}),
    rec("Mine Dispenser", 12, 4, m={"steel": 10, "concrete": 3}, p={"Circuit Board": 1}),
    rec("MIRV Bus", 18, 4, m={"steel": 12, "copper": 3}, p={"Control Unit": 1}),
    rec("Rod Bundle", 10, 3, m={"steel": 40}),
    rec("Kinetic Penetrator", 10, 3, m={"steel": 40, "bronze": 4}),
    rec("Tandem Spacer", 3, 3, m={"steel": 2}),
    rec("Glide Wings", 8, 3, m={"steel": 8, "laminate": 2}),
    rec("Pop-out Wings", 8, 3, m={"steel": 6, "laminate": 1}, p={"Servo Motor": 1}),
    rec("Cruise Wings", 8, 3, m={"steel": 6, "laminate": 3}),
    rec("Glide Guidance Kit", 10, 4, m={"steel": 3}, p={"Servo Motor": 1, "Control Unit": 1}),
    rec("Rocket Assist", 8, 3, m={"steel": 6, "concrete": 3}),
    rec("Retard Chute", 4, 2, m={"cloth": 3, "steel": 1}),
    rec("Ballute", 4, 2, m={"rubber": 3, "steel": 1}),
    rec("Ring Tail", 3, 2, m={"steel": 4}),
    rec("Launch Rail", 6, 3, m={"steel": 8, "bronze": 1}),
    rec("Heavy Launch Rail", 12, 4, m={"steel": 30, "bronze": 4}),
    # What a pilot wears (tools/constructs/pilot_parts.py): a parachute early, a jetpack late.
    rec("Parachute", 3, m={"rubber": 2, "planks": 2}),
    rec("Backpack Glider", 4, 2, m={"rubber": 3, "steel": 2}),
    rec("Jetpack", 8, 4, m={"steel": 6, "copper": 3}, p={"Control Unit": 1, "Wiring Loom": 2}),
    rec("Ejection Seat", 5, 2, m={"steel": 6, "rubber": 2}, p={"Wiring Loom": 1}),
    # The build stations: dearer than a beacon (they do its job and more), tier 3 like it: a build area is a mid-game goal.
    rec("Mirror Table", 30, 3, m={"steel": 24, "glass": 12}, p={"Control Unit": 2, "Lens": 2}),
    rec("Line Layer", 30, 3, m={"steel": 30, "copper": 6}, p={"Control Unit": 2, "Servo Motor": 2}),
    rec("Replacer", 30, 3, m={"steel": 26, "copper": 6}, p={"Control Unit": 2, "Servo Motor": 3}),
    rec("Build Gantry", 60, 3, m={"steel": 70, "copper": 12}, p={"Control Unit": 4, "Servo Motor": 4, "Lens": 4}),
    rec("Selector", 30, 3, m={"steel": 20, "circuits": 6}, p={"Control Unit": 3, "Lens": 1}),
    rec("Blueprint Desk", 30, 3, m={"steel": 20, "laminate": 6}, p={"Control Unit": 3, "Circuit Board": 2}),
    rec("Foundation", 15, m={"concrete": 100, "steel": 10}),
    rec("Frame Block", 4, m={"steel": 6}),
    rec("Frame Beam", 6, m={"steel": 14}),
    rec("Panel", 3, m={"steel": 4}),
    rec("Wood Panel", 2, m={"laminate": 4}),
]

# What a role adds, beyond the mass in material. Parts per 50 kg, at least one.
ROLE_PARTS = {
    "engine": "Piston Set", "transmission": "Gear Set", "differential": "Gear Set", "transfer": "Gear Set", "joint": "Servo Motor",
    "battery": "Control Unit", "alternator": "Copper Coil", "logic": "Circuit Board", "hose": "Wiring Loom",
    "gun": "Gear Set", "suspension": "Bearing", "seat": "Tyre Carcass", "hinge": "Bearing", "door": "Bearing", "wheel": "Tyre Carcass",
    "enginePiece": "Piston Set", "generator": "Copper Coil", "thruster": "Gear Set",
}
# What a part's class costs: (material, share of the part's mass as units of it per kg, second material or None).
CLASS = {
    "steel": ("steel", 0.2, None), "armour": ("steel", 0.25, "bronze"), "wood": ("planks", 1.0, "laminate"), "concrete": ("concrete", 0.17, "steel"),
    "rubber": ("rubber", 0.3, "steel"), "glass": ("glass", 0.2, "steel"), "cloth": ("cloth", 1.0, "steel"), "sand": ("sand", 0.2, "planks"),
}
TIER_OF = {"steel": 1, "concrete": 1, "planks": 1, "laminate": 1, "rubber": 1, "cloth": 1, "glass": 1, "sand": 1, "bronze": 2, "copper": 2, "circuits": 3}
PART_TIER = {"Thrust Chamber": 1, "Turbine Stage": 2, "Field Coil": 3, "Levite Core": 4, "Gear Set": 1, "Tyre Carcass": 1, "Bearing": 2, "Piston Set": 2, "Copper Coil": 2, "Wiring Loom": 2, "Servo Motor": 2, "Circuit Board": 3, "Control Unit": 3}
SKIP_FOLDERS = ("Spawn", "Components", "Press/Dies", "Wings")  # (dies come from a Die Lathe, or the start kit)
SKIP_NAMES = ("Material Bundle", "Construct Core", "Chest Lid", "Lid Hinge", "FMJ Rounds", "AP Rounds", "HE Rounds", "Incendiary Rounds")  # (the old ammo modules: a box's setting does it now)
MAX_UNITS = 2000
CLASS_TIER = {"small": 1, "medium": 2, "large": 3, "capital": 4}  # the lowest Assembler for a size class (tools/constructs/scale.py); a part's cost tops out at MAX_UNITS, its time at 600 s


def is_item(p):
    return p.get("tool", "wrench") != "welder" and p.get("mass", 0) <= 40 and not p.get("physicsOnly")


def long_tail(p):
    """A part's recipe by rule."""
    mass = float(p["mass"])
    role = p.get("role", "frame")
    material, share, second = CLASS.get(p.get("material", "steel"), CLASS["steel"])
    units = min(MAX_UNITS, max(1, round(mass * share)))
    m = {material: units}
    if second:
        m[second] = min(MAX_UNITS, max(1, units // 4))
    extra = {}
    if role in ROLE_PARTS:
        rail = "free" if p.get("joint", {}).get("type") == "swivel" else p.get("joint", {}).get("rail", {}).get("drive")
        extra[{"free": "Bearing", "shaft": "Gear Set"}.get(rail, ROLE_PARTS[role])] = min(20, max(1, round(mass / 50)))
    if role in ("logic", "battery", "alternator", "hose") and "copper" not in m:
        m["copper"] = min(MAX_UNITS, max(1, round(mass / 3)))
    if role == "storage" and p.get("tier"):
        # A machine of a tier is built from the tier below it in spirit: steel, a control unit from tier 3 up.
        m = {"steel": min(MAX_UNITS, round(mass / 8)), "concrete": min(MAX_UNITS, round(mass / 16))}
        extra = {"Gear Set": min(20, 2 * p["tier"])}
        if p["tier"] >= 2:
            extra["Wiring Loom"] = min(20, p["tier"])
        else:
            m["rubber"] = min(MAX_UNITS, round(mass / 40)) # (a tier 1 machine asks nothing a tier 1 machine can't make, so survival can start)
        if p["tier"] >= 3:
            extra["Control Unit"] = min(20, p["tier"] - 2)
    if len(m) + len(extra) < 2 and mass >= 3:
        # Two chains at least: anything of steel has a little rubber or concrete in its fixings.
        m["bronze" if mass < 12 else "rubber" if role not in ("frame",) else "concrete"] = min(MAX_UNITS // 4, max(1, round(mass / 25))) # (fixings stay fixings on a 240 t crank)
    size = p.get("sizeClass", "small")
    tier = max([TIER_OF.get(k, 1) for k in m] + [PART_TIER.get(k, 1) for k in extra] + [min(5, p.get("tier", 1)), CLASS_TIER[size]])
    seconds = min(180.0 if size == "small" else 600.0, round(2 + mass * 0.03, 1))
    return rec(p["name"], seconds, tier, m=m, p=extra or None)


# Flight parts by machine (docs/sandbox/CONSTRUCTS.md "Flight in survival"). Wings and control surfaces have no recipe: an Airframe
# Former cuts its own, and the part files are found on wrecks. These go to one family of Assembler, built from their own components.
THRUSTER_FOLDERS = ("Boost/Jets", "Boost/Rockets", "Boost/Repulsors", "BigDrive/Thrusters")
TURBINES = ("Turbojet", "Jet", "Lift Fan")
CORES = {"Repulsor": "Field Coil", "Anti-Grav Generator": "Field Coil", "Rocket Engine": "Thrust Chamber", "Solid Booster": "Thrust Chamber"}


def machine_of(p):
    """Which family builds a part: "" is the plain Assembler."""
    if p["_folder"] in THRUSTER_FOLDERS:
        return "thruster"
    if p["_folder"] == "Air" and p.get("role") == "fluidtank" and p.get("material") == "cloth":
        return "tailor"
    return ""


def flight_recipe(p):
    """A rule's recipe, moved to its machine: a thruster's bulk is its casing, and the parts that make it work come from the Assembler."""
    r = long_tail(p)
    r["machine"] = machine_of(p)
    if r["machine"] != "thruster":
        return r
    name, mass = p["name"], float(p["mass"])
    core = next((v for k, v in CORES.items() if name.endswith(k)), "Turbine Stage" if name.endswith(TURBINES) else None)
    if core is None:
        return r  # (a Decoupler: bolts)
    n = min(20, max(1, round(mass / 30)))
    extra = {core: n}
    if core == "Turbine Stage":
        extra["Bearing"] = n
    if "Solid Booster" not in name:
        extra["Wiring Loom"] = min(20, max(1, n // 2))
    if core == "Field Coil":
        extra["Control Unit"] = min(20, max(1, n // 4))
    if "Anti-Grav" in name:
        extra["Levite Core"] = n
    r["parts"] = extra
    r["materials"] = {"steel": r["materials"].get("steel", min(MAX_UNITS, max(1, round(mass * 0.2))))}
    tier = max(r.get("minTier", 1), max(PART_TIER.get(k, 1) for k in extra), 4 if "Anti-Grav" in name else 1)
    r["minTier"] = tier
    return r


def parts():
    out = []
    for f in sorted(glob.glob(os.path.join("Constructs", "Parts", "**", "*.json"), recursive=True)):
        with open(f, encoding="utf-8") as h:
            p = json.load(h)
        if p.get("toolOnly") or p.get("role") == "rotor":
            continue  # (a cylinder's head: the tool that makes it pays; a rotor comes off a Fan Former, not an Assembler)
        p["_folder"] = os.path.relpath(os.path.dirname(f), os.path.join("Constructs", "Parts")).replace("\\", "/")
        out.append(p)
    return out


def component(c):
    name, file, mass, colour, shapes, description = c
    boxes = [{"min": list(lo), "max": list(hi)} for lo, hi in shapes]
    sockets = [{"name": "bottom", "at": [0, 0, 0], "dir": "-y"}]
    zfight.resolve(boxes, sockets)
    return {
        "format": "call-of-slop-construct-part/1", "name": name, "description": description, "group": "Production", "section": "Components",
        "role": "panel", "tool": "wrench", "attachSeconds": 1.0, "mass": mass, "health": 40, "material": "steel", "color": colour,
        "boxes": [{"min": [round(v, 4) for v in b["min"]], "max": [round(v, 4) for v in b["max"]]} for b in boxes], "sockets": sockets,
    }


def write(path, text, check):
    if check:
        if not os.path.exists(path) or open(path, encoding="utf-8").read() != text:
            print(f"out of date: {path}")
            return 1
        return 0
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return 0


def builder_names():
    """The weapon builder's parts: generated into Sandbox items at load, so a HAND recipe may name one with no file in Constructs/Parts."""
    names = set()
    for f in glob.glob(os.path.join("Parts", "*", "*.json")):
        if not os.path.basename(os.path.dirname(f)).startswith(("_", "Builds", "Tables")):
            with open(f, encoding="utf-8") as h:
                m = re.search(r'"name"\s*:\s*"([^"]+)"', h.read())
            if m:
                names.add(m.group(1))
    return names


def main():
    check = "--check" in sys.argv
    bad = 0
    for c in COMPONENTS:
        bad += write(os.path.join("Constructs", "Parts", "Components", c[1] + ".json"), json.dumps(component(c), indent=2) + "\n", check)
    known = parts()
    by_name = {p["name"]: p for p in known}
    hand = {r["output"] for r in HAND}
    tail = []
    for p in known:
        name = p["name"]
        if name in hand or p.get("stage") or name in SKIP_NAMES or p["_folder"].startswith(SKIP_FOLDERS):
            continue
        tail.append(flight_recipe(p) if machine_of(p) else long_tail(p))
    tail.sort(key=lambda r: r.get("machine", ""))  # (stable: a family's recipes are in file order, and the plain Assembler's keep their places)
    recipes = HAND + tail
    builder = builder_names()
    for r in recipes:
        if r["output"] not in by_name and r["output"] not in builder:
            print(f"no part named {r['output']}")
            bad += 1
        for q in r.get("parts", {}):
            if q not in by_name or not is_item(by_name[q]):
                print(f"{r['output']}: {q} isn't an item part")
                bad += 1
        if len(r.get("materials", {})) + len(r.get("parts", {})) > 6:
            print(f"{r['output']}: too many inputs")
            bad += 1
    lines = ",\n".join("    " + json.dumps(r) for r in recipes)
    text = '{\n  "format": "%s",\n  "about": %s,\n  "recipes": [\n%s\n  ]\n}\n' % (FORMAT, json.dumps(ABOUT), lines)
    bad += write(os.path.join("Constructs", "Assembling.json"), text, check)
    print(f"assembling: {'checked' if check else 'written'}, {len(HAND)} by hand, {len(tail)} generated, {bad} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
