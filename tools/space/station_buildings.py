#!/usr/bin/env python3
"""Writes the reusable sky-station buildings (general, for any pack):

    Station Control Hub   two storeys: an operations room, a stair hall and a crew mess; the command deck above; roof access
    Station Hangar Bay    one tall hall with wide arches at both ends, a mezzanine, a workshop and a store
    Station Crew Quarters two storeys of bunk rooms and common rooms; roof access
    Refinery Pump House   a pump room and a valve room with stairs onto the roof

and their furnishing rules (Buildings/Furnishing/station*.json). Cream hull plating outside, grey metal inside, metal
doors. Maps dress them with cosmetic domes and brims (the Space pack's sky station does). Run from anywhere:

    python3 tools/space/station_buildings.py [repo root]
"""
import json
import os
import sys

sys.dont_write_bytecode = True
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
BLD = os.path.join(REPO, "Buildings")
FURN = os.path.join(BLD, "Furnishing")

PALETTES = {
    "cream hull": {"wall": "#cdbfa8", "interior": "#a4aaae", "floor": "#4c5054", "ceiling": "#868b90", "trim": "#8d7458",
                   "door": "#6c6f73", "roof": "#b3a48c"},
    "white hull": {"wall": "#d8d8d2", "interior": "#9ea6ac", "floor": "#45494e", "ceiling": "#80868c", "trim": "#5a6a78",
                   "door": "#5a6066", "roof": "#bfc0bc"},
    "bronze hull": {"wall": "#a88e70", "interior": "#a39a8e", "floor": "#4a4640", "ceiling": "#857d72", "trim": "#5c4a38",
                    "door": "#6a5a48", "roof": "#8d7458"},
}


def base(name, theme, desc, seed, storey, extra=None):
    d = {"format": "call-of-slop-building/1", "name": name, "theme": theme, "category": "special", "description": desc,
         "author": "Call of Slop", "seed": seed, "wallThickness": 0.3, "storeyHeight": storey,
         "exterior": {"color": "$wall", "material": "metal"}, "interior": {"color": "$interior", "material": "metal"},
         "floor": {"color": "$floor", "material": "grid"}, "ceiling": {"color": "$ceiling", "material": "metal"},
         "trim": "$trim", "door": "$door", "palettes": PALETTES}
    if extra:
        d.update(extra)
    return d


def room(id, type, rect, **kw):
    r = {"id": id, "type": type, "rect": rect}
    r.update(kw)
    return r


def door(x, z, w=1.1, front=False, **kw):
    d = {"type": "door", "at": [x, z], "width": w, "material": "metal"}
    if front:
        d["front"] = True
    d.update(kw)
    return d


def arch(x, z, w=1.4, front=False, **kw):
    d = {"type": "arch", "at": [x, z], "width": w}
    if front:
        d["front"] = True
    d.update(kw)
    return d


WIN = {"spacing": 2.6, "width": 1.5, "sill": 1.0, "head": 2.4}
ROOF = {"type": "flat", "parapet": 1.0, "color": {"color": "$roof", "material": "metal"}}


def control_hub():
    return base("Station Control Hub", "A sky-station control hub: consoles and status walls, a crew mess, a command deck above and a fighting roof",
                "Two storeys, 16 x 12 m: the operations room and a crew mess below, the command deck above, stairs onto a railed roof.",
                611, 3.4, {
                    "floors": [
                        {"autoWindows": WIN, "rooms": [
                            room("operations", "stationcontrol", [-8, -6, 3, 6]),
                            room("stair hall", "stationhall", [3, -6, 8, 0], windows=False),
                            room("crew mess", "stationcommon", [3, 0, 8, 6])],
                         "openings": [arch(-2.5, 6, 2.0, front=True), arch(-2.5, -6, 2.0), door(3, -3), door(3, 3), door(5.5, 0, 1.2), door(8, 3)]},
                        {"autoWindows": WIN, "rooms": [
                            room("command deck", "stationcontrol", [-8, -6, 3, 6]),
                            room("upper hall", "stationhall", [3, -6, 8, 6], windows=False)],
                         "openings": [door(3, -2.5, 1.2), door(3, 2.5, 1.2)]}],
                    "stairs": [{"floor": 0, "rect": [6.4, -4.85, 7.5, -1.55], "dir": "+z"},
                               {"floor": 1, "rect": [6.4, 1.2, 7.5, 4.5], "dir": "+z"}],
                    "roof": ROOF,
                    "anchors": [{"type": "spawn", "name": "operations west", "floor": 0, "at": [-6, 0]},
                                {"type": "spawn", "name": "operations east", "floor": 0, "at": [1, -3.5]},
                                {"type": "spawn", "name": "mess", "floor": 0, "at": [5.5, 3]},
                                {"type": "point", "name": "roof", "floor": 1, "at": [-2.5, 0]}]})


def hangar():
    plat = {"type": "platform", "rect": [-9.6, -6.6, -6.4, 6.6], "height": 3.4, "railing": 1,
            "color": {"color": "$floor", "material": "grid"}, "stairs": {"rect": [-6.4, 4.0, -4.0, 5.2], "dir": "-x"}}
    return base("Station Hangar Bay", "A sky-station hangar: a tall open bay for skimmers and cargo, a mezzanine, a workshop and a parts store",
                "One tall storey, 20 x 14 m: a vehicle bay with 5 m arches at both ends, a mezzanine on the west wall, a workshop and a store.",
                612, 6.6, {
                    "floors": [{"autoWindows": {"spacing": 3.0, "width": 1.8, "sill": 3.8, "head": 5.6}, "rooms": [
                        room("bay", "stationhangar", [-10, -7, 6, 7]),
                        room("workshop", "stationworkshop", [6, -7, 10, 0]),
                        room("parts store", "stationstore", [6, 0, 10, 7], windows=False)],
                        "openings": [arch(-1, 7, 5.0, front=True, head=4.6), arch(-1, -7, 5.0, head=4.6), door(6, -3.5, 1.2), door(6, 3.5, 1.2), door(8, -7, 1.1)]}],
                    "roof": {"type": "flat", "parapet": 0.8, "color": {"color": "$roof", "material": "metal"}},
                    "extras": [plat],
                    "anchors": [{"type": "spawn", "name": "bay", "floor": 0, "at": [0, 0]}]})


def quarters():
    return base("Station Crew Quarters", "Sky-station crew quarters: bunk rooms with lockers, a common room and a lounge, stairs onto the roof",
                "Two storeys, 12 x 10 m: bunk rooms west, a common room and a lounge east, stairs onto a railed roof.",
                613, 3.2, {
                    "floors": [
                        {"autoWindows": WIN, "rooms": [
                            room("bunk room", "stationbunks", [-6, -5, 0, 5]),
                            room("common room", "stationcommon", [0, -5, 6, 5])],
                         "openings": [door(0, 1.5), arch(3, 5, 1.6, front=True), door(2.0, -5)]},
                        {"autoWindows": WIN, "rooms": [
                            room("upper bunks", "stationbunks", [-6, -5, 0, 5]),
                            room("lounge", "stationlounge", [0, -5, 6, 5])],
                         "openings": [door(0, -2.0)]}],
                    "stairs": [{"floor": 0, "rect": [4.6, -3.9, 5.7, -0.9], "dir": "+z"},
                               {"floor": 1, "rect": [4.6, 0.7, 5.7, 3.7], "dir": "+z"}],
                    "roof": ROOF,
                    "anchors": [{"type": "spawn", "name": "common", "floor": 0, "at": [2, 2]}]})


def pump_house():
    return base("Refinery Pump House", "A cloud-gas refinery pump house: big pumps, pipe runs and valve gear, stairs up to a railed roof",
                "One storey, 8 x 10 m: a pump room and a valve room with stairs onto the roof.",
                614, 3.6, {
                    "floors": [{"autoWindows": {"spacing": 2.6, "width": 1.2, "sill": 1.5, "head": 2.6}, "rooms": [
                        room("pump room", "stationpumps", [-4, -5, 4, 2], autoWindows={"spacing": 4.0, "width": 1.0, "sill": 2.2, "head": 3.0}),
                        room("valve room", "stationhall", [-4, 2, 4, 5], windows=False)],
                        "openings": [door(2.6, 5, 1.2, front=True), door(2.6, 2, 1.2), door(-4, -1.5, 1.2)]}],
                    "stairs": [{"floor": 0, "rect": [-1.3, 2.9, 2.2, 4.0], "dir": "-x"}],
                    "roof": ROOF})


RULES = {
    "stationcontrol": ("A station control room: consoles round the walls, chairs at them, server cabinets and a status wall.", [
        {"id": "console", "props": ["Station Console"], "place": "wall", "count": [3, 5]},
        {"props": ["Office Chair"], "place": "front", "of": "console", "distance": 0.1, "chance": 0.8},
        {"props": ["Big Wall Display", "Status Board"], "place": "wall", "count": [1, 2]},
        {"props": ["Station Server Cabinet"], "place": "corner", "count": [1, 2]},
        {"props": ["Station Supply Case", "Toolbox"], "place": "floor", "count": [0, 2]}]),
    "stationhall": ("A station stair hall or corridor: a cabinet, a bulkhead lamp, a case.", [
        {"props": ["Station Server Cabinet", "Metal Lockers"], "place": "wall", "count": [0, 1]},
        {"props": ["Station Supply Case"], "place": "floor", "count": [0, 1]}]),
    "stationcommon": ("A station crew mess: a table and chairs, a fridge, a water cooler, cases.", [
        {"id": "table", "props": ["Dining Table"], "place": "centre"},
        {"props": ["Dining Chair"], "place": "around", "of": "table", "count": [2, 4]},
        {"props": ["Fridge", "Water Cooler"], "place": "wall", "count": [1, 2]},
        {"props": ["Metal Lockers"], "place": "wall", "count": [0, 1]},
        {"props": ["Duffel Bag", "Backpack", "Station Supply Case"], "place": "floor", "count": [0, 2]}]),
    "stationlounge": ("A station lounge: a sofa, armchairs, a coffee table and a wall display.", [
        {"id": "sofa", "props": ["Fabric Sofa"], "place": "wall", "prefer": "far"},
        {"props": ["Coffee Table"], "place": "front", "of": "sofa", "distance": 0.4},
        {"props": ["Armchair"], "place": "wall", "count": [1, 2]},
        {"props": ["Big Wall Display", "Status Board"], "place": "wall", "chance": 0.7},
        {"props": ["Duffel Bag", "Backpack"], "place": "floor", "count": [0, 2]}]),
    "stationbunks": ("A station bunk room: bunk beds and lockers along the walls, kit bags on the floor.", [
        {"props": ["Bunk Bed"], "place": "wall", "count": [2, 3]},
        {"props": ["Metal Lockers"], "place": "wall", "count": [1, 2]},
        {"props": ["Duffel Bag", "Backpack", "Station Supply Case"], "place": "floor", "count": [1, 3]}]),
    "stationpumps": ("A refinery pump room: pumps, pipe risers, a ventilation unit and a control console.", [
        {"props": ["Coolant Pump"], "place": "wall", "count": [1, 2]},
        {"props": ["Station Pipe Riser"], "place": "wall", "count": [1, 2]},
        {"props": ["Ventilation Fan Unit", "Station Server Cabinet"], "place": "wall", "count": [1, 2]},
        {"props": ["Station Console"], "place": "wall", "chance": 0.7},
        {"props": ["Station Supply Case", "Toolbox"], "place": "floor", "count": [0, 2]}]),
    "stationhangar": ("A station hangar bay: cargo pods and case stacks along the walls, a hover sled, fuel couplings.", [
        {"props": ["Hover Cargo Sled"], "place": "centre", "chance": 0.8},
        {"props": ["Station Cargo Pod", "Supply Case Stack"], "place": "wall", "count": [3, 5]},
        {"props": ["Fuel Coupling"], "place": "wall", "count": [1, 2]},
        {"props": ["Station Supply Case", "Toolbox"], "place": "floor", "count": [1, 3]}]),
    "stationworkshop": ("A station workshop: shelving, a console, cabinets, tool boxes.", [
        {"props": ["Metal Shelving"], "place": "wall", "count": [1, 2]},
        {"props": ["Station Console"], "place": "wall"},
        {"props": ["Station Server Cabinet"], "place": "corner", "chance": 0.7},
        {"props": ["Toolbox", "Station Supply Case"], "place": "floor", "count": [1, 3]}]),
    "stationstore": ("A station parts store: shelving, supply cases and case stacks.", [
        {"props": ["Metal Shelving"], "place": "wall", "count": [1, 3]},
        {"props": ["Supply Case Stack", "Station Cargo Pod"], "place": "wall", "count": [1, 2]},
        {"props": ["Station Supply Case"], "place": "floor", "count": [1, 3]}]),
}


def main():
    blds = {"StationControlHub": control_hub(), "StationHangarBay": hangar(), "StationCrewQuarters": quarters(),
            "RefineryPumpHouse": pump_house()}
    for folder, d in blds.items():
        os.makedirs(os.path.join(BLD, folder), exist_ok=True)
        with open(os.path.join(BLD, folder, "building.json"), "w") as f:
            json.dump(d, f, indent=1)
            f.write("\n")
    for t, (desc, items) in RULES.items():
        with open(os.path.join(FURN, t + ".json"), "w") as f:
            json.dump({"format": "call-of-slop-furnishing/1", "type": t, "description": desc, "items": items}, f, indent=1)
            f.write("\n")
    print(f"wrote {len(blds)} buildings and {len(RULES)} furnishing rules")


if __name__ == "__main__":
    main()
