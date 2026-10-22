#!/usr/bin/env python3
"""Writes the missile-complex buildings used by the World Tour map "Silo" (Maps/World/03-Silo.json):

    Launch Control Centre, Silo Power Plant, Silo Blast Lock, Silo Armoury, Silo Comms Room, Silo Crew Quarters,
    Silo Maintenance Bay, Silo Service Tower, Silo Access Bunker, Concrete Guard Tower

and their furnishing rules (new room types in Buildings/Furnishing, all named silo*). Run from anywhere:

    python3 tools/world/silo_buildings.py [repo root]

The underground ones are ordinary buildings: the map places them in cave chambers with "anchor": "world" (an absolute
height). Dark concrete inside (there are no shadows underground, so the palette does the dimming), metal blast doors,
no outside windows. Their props are the Facility set (Props/Facility, tools/world/silo_props.py) plus shared ones.
"""
import json, os, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
BLD = os.path.join(REPO, "Buildings")
FURN = os.path.join(BLD, "Furnishing")

PALETTES = {
    "dark concrete": {"wall": "#6a6a64", "interior": "#76766c", "floor": "#4e4e4a", "ceiling": "#5a5a54", "trim": "#4a5232",
                      "door": "#4e5444", "roof": "#5e5e58", "stripe": "#c8a820"},
    "green paint": {"wall": "#62665a", "interior": "#6a7462", "floor": "#4a5046", "ceiling": "#5a6052", "trim": "#c8a820",
                    "door": "#3e4636", "roof": "#56584e", "stripe": "#c8a820"},
}


def base(name, theme, desc, seed, storey, extra=None):
    d = {"format": "call-of-slop-building/1", "name": name, "theme": theme, "category": "special", "description": desc,
         "author": "Call of Slop", "seed": seed, "wallThickness": 0.4, "storeyHeight": storey,
         "exterior": {"color": "$wall", "material": "concrete"}, "interior": {"color": "$interior", "material": "concrete"},
         "floor": {"color": "$floor", "material": "concrete"}, "ceiling": {"color": "$ceiling", "material": "concrete"},
         "trim": "$trim", "door": "$door", "palettes": PALETTES}
    if extra:
        d.update(extra)
    return d


def room(id, type, rect, **kw):
    r = {"id": id, "type": type, "rect": rect, "windows": False}
    r.update(kw)
    return r


def door(x, z, w=1.0, metal=True, front=False, **kw):
    d = {"type": "door", "at": [x, z], "width": w}
    if metal:
        d["material"] = "metal"
    if front:
        d["front"] = True
    d.update(kw)
    return d


def arch(x, z, w=1.2, front=False):
    d = {"type": "arch", "at": [x, z], "width": w}
    if front:
        d["front"] = True
    return d


def hole(x, z, w, sill, head):
    return {"type": "hole", "at": [x, z], "width": w, "sill": sill, "head": head}


def window(x, z, w, sill=1.0, head=2.0, health=6):
    return {"type": "window", "at": [x, z], "width": w, "sill": sill, "head": head, "health": health}


def P(name, **kw):
    p = {"name": name}
    p.update(kw)
    return p


def lamps(n_along, length, wall, y=2.3):
    """Caged bulkhead lamps spread along a wall (explicit props)."""
    out = []
    for i in range(n_along):
        a = -length / 2 + length * (i + 0.5) / n_along
        out.append(P("Caged Bulkhead Lamp", wall=wall, along=round(a, 2), y=y))
    return out


BUILDINGS = {}

# ---------------------------------------------------------------------------------------------- Launch Control Centre
ctrl_props = [P("Launch Control Console", wall="N", along=-3.3), P("Launch Control Console", wall="N", along=0.0),
              P("Launch Control Console", wall="N", along=3.3), P("Big Wall Display", wall="N", along=0.0, y=1.95),
              P("Status Board", wall="W", along=-2.2, y=1.5), P("Status Board", wall="E", along=-2.2, y=1.5),
              P("Launch Key Console", at=[0, 0.4], yaw=180), P("Office Chair", at=[-3.3, -2.9], yaw=180),
              P("Office Chair", at=[0.0, -2.9], yaw=180), P("Office Chair", at=[3.3, -2.9], yaw=180),
              P("Radiation Sign", wall="S", along=-4.6, y=1.6), P("Tube Light", at=[-3, -1.5], y=3.9),
              P("Tube Light", at=[3, -1.5], y=3.9), P("Tube Light", at=[-3, 2.5], y=3.9), P("Tube Light", at=[3, 2.5], y=3.9)]
corr_lcc = lamps(5, 20, "N", 2.6) + [P("Wall Cable Tray", wall="S", along=-6, y=3.3), P("Wall Cable Tray", wall="S", along=6, y=3.3),
                                     P("Radiation Sign", wall="S", along=-2.2, y=1.7), P("Radiation Sign", wall="S", along=2.2, y=1.7)]
BUILDINGS["LaunchControlCentre"] = base(
    "Launch Control Centre",
    "The heart of a Cold War missile complex, deep underground: beige launch consoles, a big tactical wall display, the two-key launch station, mainframes humming either side",
    "The launch control room (three crew consoles under the wall display, the key station in the middle) between two computer rooms, a corridor with blast doors at both ends and the front.",
    503, 4.6, {
        "floors": [{
            "rooms": [
                room("launch control", "silocontrol", [-6, -7, 6, 3.5], furnish="explicit", props=ctrl_props,
                     floor={"color": "#5a5650", "material": "tiles"}),
                room("west computer room", "silocomputer", [-11, -7, -6, 3.5]),
                room("east computer room", "silocomputer", [6, -7, 11, 3.5]),
                room("corridor", "silocorridor", [-11, 3.5, 11, 7], props=corr_lcc),
            ],
            "openings": [
                door(0, 7, 1.6, front=True), door(-11, 5.25, 1.4), door(11, 5.25, 1.4),
                door(-3, 3.5, 1.4), door(3, 3.5, 1.4), door(-8.5, 3.5, 1.0), door(8.5, 3.5, 1.0),
                arch(-6, -4.5, 1.2), arch(6, -4.5, 1.2), window(-6, 0.0, 2.0, 1.0, 2.1), window(6, 0.0, 2.0, 1.0, 2.1),
            ],
        }],
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "$roof", "material": "concrete"}},
        "anchors": [{"type": "hill", "name": "launch control", "at": [0, -1.5]},
                    {"type": "spawn", "name": "corridor west", "at": [-8, 5.25]}, {"type": "spawn", "name": "corridor east", "at": [8, 5.25]}],
    })

# ---------------------------------------------------------------------------------------------- Silo Power Plant
hall_props = [P("Reactor Vessel", at=[0, 0.25]), P("Reactor Control Desk", wall="S", along=0.0),
              P("Wall Pipe Run", wall="N", along=-3.5, y=4.4), P("Wall Pipe Run", wall="N", along=3.5, y=4.4),
              P("Radiation Sign", wall="S", along=2.0, y=1.8), P("Radiation Sign", wall="W", along=1.5, y=1.8),
              P("Radiation Sign", wall="E", along=1.5, y=1.8), P("Status Board", wall="S", along=3.6, y=1.4),
              P("Caged Bulkhead Lamp", wall="W", along=-2, y=3.0), P("Caged Bulkhead Lamp", wall="E", along=-2, y=3.0),
              P("Caged Bulkhead Lamp", wall="N", along=0, y=5.6)]
turb_props = [P("Turbine Generator", at=[0.3, -0.6], yaw=90), P("Caged Bulkhead Lamp", wall="E", along=-3, y=3.2),
              P("Caged Bulkhead Lamp", wall="E", along=3, y=3.2), P("Wall Pipe Run", wall="E", along=0, y=4.8)]
corr_pp = lamps(5, 22, "S", 2.8) + [P("Wall Cable Tray", wall="N", along=-6, y=3.4), P("Wall Cable Tray", wall="N", along=6, y=3.4),
                                    P("Radiation Sign", wall="N", along=-9.3, y=1.7), P("Radiation Sign", wall="N", along=9.3, y=1.7)]
BUILDINGS["SiloPowerPlant"] = base(
    "Silo Power Plant",
    "The missile complex's own power plant: a small military reactor glowing blue in a tall concrete hall, coolant pumps, a turbine and the switchgear",
    "A tall reactor hall (the vessel in the middle, a catwalk along the back wall, the operator's desk) between the switchgear room and the turbine hall, a corridor in front with blast doors at both ends.",
    504, 7.0, {
        "floors": [{
            "rooms": [
                room("reactor hall", "siloreactor", [-6.5, -8, 6.5, 3.5], props=hall_props,
                     floor={"color": "#565a56", "material": "tiles"}),
                room("switchgear", "siloswitchgear", [-12, -8, -6.5, 3.5]),
                room("turbine hall", "siloturbine", [6.5, -8, 12, 3.5], furnish="explicit", props=turb_props),
                room("corridor", "silocorridor", [-12, 3.5, 12, 8], props=corr_pp),
            ],
            "openings": [
                door(0, 8, 1.6, front=True), door(-12, 5.75, 1.4), door(12, 5.75, 1.4),
                door(-3.2, 3.5, 1.6), door(3.2, 3.5, 1.6), door(-9.25, 3.5, 1.0), door(9.25, 3.5, 1.2),
                arch(-6.5, -0.5, 1.4), arch(6.5, -0.5, 1.4),
            ],
        }],
        "extras": [{"type": "platform", "rect": [-6.3, -7.8, 6.3, -5.6], "height": 3.4, "railing": 1.0,
                    "stairs": {"rect": [4.6, -5.6, 5.8, -1.6], "dir": "-z"}}],
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "$roof", "material": "concrete"}},
        "anchors": [{"type": "hill", "name": "reactor floor", "at": [0, 1.6]},
                    {"type": "spawn", "name": "corridor west", "at": [-8, 5.75]}, {"type": "spawn", "name": "corridor east", "at": [8, 5.75]}],
    })

# ---------------------------------------------------------------------------------------------- Silo Blast Lock
BUILDINGS["SiloBlastLock"] = base(
    "Silo Blast Lock",
    "The entry blast lock of a missile complex: two steel blast doors either side of a tiled decontamination room",
    "Outer lock, decontamination (spray arches, showers, suit racks) and inner lock, four blast doors in a row.",
    505, 3.6, {
        "floors": [{
            "rooms": [
                room("outer lock", "siloairlock", [-6, -3.5, -2, 3.5], props=[P("Caged Bulkhead Lamp", wall="N", along=0, y=2.4)]),
                room("decontamination", "silodecon", [-2, -3.5, 2, 3.5], floor={"color": "#7a8080", "material": "tiles"},
                     wall={"color": "#8a948a", "material": "tiles"}, props=[P("Decon Shower Arch", at=[0, 0], yaw=90)]),
                room("inner lock", "siloairlock", [2, -3.5, 6, 3.5], props=[P("Caged Bulkhead Lamp", wall="S", along=0, y=2.4)]),
            ],
            "openings": [door(-6, 0, 1.4, front=True), door(-2, 0, 1.4), door(2, 0, 1.4), door(6, 0, 1.4)],
        }],
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "$roof", "material": "concrete"}},
    })

# ---------------------------------------------------------------------------------------------- Armoury / Comms (mirror pair)
BUILDINGS["SiloArmoury"] = base(
    "Silo Armoury",
    "The security force armoury of a missile complex: rifle racks, ammunition shelving, the quartermaster's cage",
    "The armoury (racks and shelving), the quartermaster's office and a store; blast doors north, east and west.",
    506, 3.6, {
        "floors": [{
            "rooms": [
                room("armoury", "siloarmoury", [-7, -5, 2, 5], props=lamps(2, 8, "W", 2.3)),
                room("quartermaster", "office", [2, -5, 7, 0]),
                room("store", "silostore", [2, 0, 7, 5]),
            ],
            "openings": [door(-2.5, -5, 1.4, front=True), door(2, -2.5, 1.0), door(2, 2.5, 1.0), door(4.5, 0, 1.0, metal=False),
                         door(7, 2.5, 1.4), door(-7, 0, 1.4)],
        }],
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "$roof", "material": "concrete"}},
        "anchors": [{"type": "spawn", "name": "armoury", "at": [-4.5, 0]}],
    })
BUILDINGS["SiloCommsRoom"] = base(
    "Silo Comms Room",
    "The communications room of a missile complex: radio consoles, patch bays, teletypes and a crypto room",
    "The comms room (radio consoles, patch cabinets, teletypes), the crypto room and a store; a blast door north and one west.",
    507, 3.6, {
        "floors": [{
            "rooms": [
                room("comms", "silocomms", [-2, -5, 7, 5], props=lamps(2, 8, "E", 2.3)),
                room("crypto room", "silocrypto", [-7, -5, -2, 0]),
                room("store", "silostore", [-7, 0, -2, 5]),
            ],
            "openings": [door(2.5, -5, 1.4, front=True), door(-2, -2.5, 1.0), door(-2, 2.5, 1.0), door(-4.5, 0, 1.0, metal=False),
                         door(-7, 2.5, 1.4), door(7, 0, 1.4)],
        }],
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "$roof", "material": "concrete"}},
        "anchors": [{"type": "spawn", "name": "comms", "at": [4.5, 0]}],
    })

# ---------------------------------------------------------------------------------------------- Crew Quarters / Maintenance Bay (mirror pair)
BUILDINGS["SiloCrewQuarters"] = base(
    "Silo Crew Quarters",
    "Where the missile crews live between alerts: a mess with a galley, a bunk room, lockers and a washroom",
    "The mess and galley, the bunk room, a locker room and a washroom; blast doors south, east and west.",
    508, 3.4, {
        "floors": [{
            "rooms": [
                room("mess", "silomess", [0, -5, 8, 5], props=lamps(2, 8, "E", 2.3)),
                room("bunk room", "silobunks", [-8, -5, 0, 1]),
                room("washroom", "restroom", [-8, 1, -3, 5], floor={"color": "#7a8080", "material": "tiles"}, wall={"color": "#8a948a", "material": "tiles"}),
                room("locker room", "silolockers", [-3, 1, 0, 5]),
            ],
            "openings": [door(4, 5, 1.4, front=True), door(8, -2, 1.4), door(-8, -2, 1.4), door(0, -2, 1.0, metal=False), door(0, 3, 1.0, metal=False),
                         door(-3, 3, 0.9, metal=False), door(-1.5, 1, 1.0, metal=False)],
        }],
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "$roof", "material": "concrete"}},
        "anchors": [{"type": "spawn", "name": "mess", "at": [4, 0]}],
    })
BUILDINGS["SiloMaintenanceBay"] = base(
    "Silo Maintenance Bay",
    "The missile complex's maintenance bay: a workshop, a fuel and propellant store, the tool crib",
    "The workshop, the fuel store (drums, tanks, a flammables cabinet), a parts room and the tool crib; blast doors south, west and east.",
    509, 3.4, {
        "floors": [{
            "rooms": [
                room("workshop", "siloworkshop", [-8, -5, 0, 5], props=lamps(2, 8, "W", 2.3)),
                room("fuel store", "silofuel", [0, -5, 8, 1]),
                room("tool crib", "silostore", [3, 1, 8, 5]),
                room("parts room", "silostore", [0, 1, 3, 5]),
            ],
            "openings": [door(-4, 5, 1.4, front=True), door(-8, -2, 1.4), door(8, -2, 1.4), door(0, -2, 1.0, metal=False), door(0, 3, 1.0, metal=False),
                         door(3, 3, 0.9, metal=False), door(1.5, 1, 1.0, metal=False)],
        }],
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "$roof", "material": "concrete"}},
        "anchors": [{"type": "spawn", "name": "workshop", "at": [-4, 0]}],
    })

# ---------------------------------------------------------------------------------------------- Silo Service Tower
BUILDINGS["SiloServiceTower"] = base(
    "Silo Service Tower",
    "A steel-and-concrete service tower standing in a missile silo shaft: switchback stairs, an opening on every level facing the missile",
    "A narrow stair tower, four levels and a flat roof: each level has an opening onto the shaft and firing holes; stairs climb to the roof.",
    510, 4.5, {
        "wallThickness": 0.3, "floorCount": 4,
        "exterior": {"color": "#5e6266", "material": "metal"}, "interior": {"color": "#6a6e70", "material": "metal"},
        "floor": {"color": "#4a4e50", "material": "grid"},
        "floors": [
            {"rooms": [room("bottom landing", "silotower", [-2.5, -3.5, 2.5, 3.5], props=[P("Caged Bulkhead Lamp", wall="W", along=-2.2, y=2.4)])],
             "openings": [arch(2.5, -2.4, 1.2, front=True), hole(2.5, 2.6, 1.0, 1.1, 1.8), hole(0, -3.5, 1.2, 1.1, 1.8)]},
            {"repeat": True,
             "rooms": [room("landing", "silotower", [-2.5, -3.5, 2.5, 3.5], props=[P("Caged Bulkhead Lamp", wall="W", along=-2.2, y=2.4)])],
             "openings": [arch(2.5, -2.4, 1.2), hole(2.5, 2.6, 1.0, 1.1, 1.8), hole(0, -3.5, 1.2, 1.1, 1.8)]},
        ],
        "stairs": [
            {"floor": 0, "rect": [0.95, -1.5, 2.05, 1.6], "dir": "-z"},
            {"floor": 1, "rect": [-2.05, -1.5, -0.95, 1.6], "dir": "+z", "alt": {"rect": [0.95, -1.5, 2.05, 1.6], "dir": "-z"}},
        ],
        "roof": {"type": "flat", "parapet": 0.1, "color": {"color": "#4a4e50", "material": "grid"}},
    })

# ---------------------------------------------------------------------------------------------- Silo Access Bunker
BUILDINGS["SiloAccessBunker"] = base(
    "Silo Access Bunker",
    "A concrete blockhouse over a stairwell down into a missile complex: firing slits, a roof position behind a parapet, a blast lock at the bottom",
    "A guard post at ground level (firing slits all round, stairs onto the roof) over a stair hall and a blast lock; the lower floor is meant to be buried.",
    511, 3.0, {
        "floors": [
            {"height": 4.0,
             "rooms": [room("blast lock", "siloairlock", [-4.3, -4.3, 4.3, -0.5], props=[P("Caged Bulkhead Lamp", wall="N", along=2.5, y=2.4)]),
                       room("stair hall", "silostairs", [-4.3, -0.5, 4.3, 4.3], props=[P("Caged Bulkhead Lamp", wall="S", along=2.5, y=2.6)])],
             "openings": [door(0, -4.3, 1.4), door(2.6, -0.5, 1.2)]},
            {"rooms": [room("guard post", "siloguardpost", [-4.3, -4.3, 4.3, 4.3])],
             "openings": [door(0, 4.3, 1.2, front=True), hole(-2.6, -4.3, 1.8, 1.1, 1.5), hole(2.6, -4.3, 1.4, 1.1, 1.5),
                          hole(-4.3, -1.8, 1.8, 1.1, 1.5), hole(4.3, -1.8, 1.8, 1.1, 1.5), hole(-2.7, 4.3, 1.4, 1.1, 1.5),
                          hole(2.7, 4.3, 1.4, 1.1, 1.5), hole(-4.3, 2.6, 1.2, 1.1, 1.5), hole(4.3, 2.6, 1.2, 1.1, 1.5)]},
        ],
        "stairs": [{"floor": 0, "rect": [-2.6, 1.3, 0.6, 2.5], "dir": "+x"},
                   {"floor": 1, "rect": [2.3, -3.3, 3.4, -0.4], "dir": "-z"}],
        "roof": {"type": "flat", "parapet": 1.0, "color": {"color": "$roof", "material": "concrete"}},
        "extras": [{"type": "prop", "name": "Sandbag Wall", "at": [-2.2, 0, -3.6], "onRoof": True},
                   {"type": "prop", "name": "Sandbag Wall", "at": [-3.6, 0, 1.5], "yaw": 90, "onRoof": True},
                   {"type": "prop", "name": "Mushroom Vent", "at": [-2.5, 0, 2.8], "onRoof": True},
                   {"type": "prop", "name": "Restricted Area Sign", "at": [3.4, 0, 5.4]}],
        "anchors": [{"type": "point", "name": "roof", "floor": 1, "at": [0, 0]}],
    })

# ---------------------------------------------------------------------------------------------- Guard Tower
BUILDINGS["ConcreteGuardTower"] = base(
    "Concrete Guard Tower",
    "A Cold War perimeter guard tower: a plain concrete shaft, a glazed-open lookout on top, a searchlight on the roof",
    "A three-level concrete tower: a door at the foot, switchback stairs, a lookout with wide openings all round and stairs onto the roof.",
    512, 3.0, {
        "wallThickness": 0.3,
        "exterior": {"color": "#9a9a92", "material": "concrete"}, "interior": {"color": "#a8a89c", "material": "concrete"},
        "palettes": {"grey concrete": {"wall": "#9a9a92", "interior": "#a8a89c", "floor": "#6e6e68", "ceiling": "#9a9a90", "trim": "#4a5232",
                                       "door": "#4a5232", "roof": "#6a6a64", "stripe": "#c8a820"},
                     "olive": {"wall": "#7a7c66", "interior": "#9a9c86", "floor": "#5e6252", "ceiling": "#8a8c7a", "trim": "#c8a820",
                               "door": "#3e4636", "roof": "#5a5c4e", "stripe": "#c8a820"}},
        "floors": [
            {"rooms": [room("foot", "siloguardpost", [-2.5, -2.5, 2.5, 2.5])],
             "openings": [door(-1.2, 2.5, 1.0, front=True), hole(-2.5, -1.0, 0.8, 1.2, 1.6)]},
            {"rooms": [room("stair", "silotower", [-2.5, -2.5, 2.5, 2.5])], "openings": [hole(-2.5, 0, 0.8, 1.2, 1.6), hole(2.5, 0, 0.8, 1.2, 1.6)]},
            {"rooms": [room("lookout", "siloguardpost", [-2.5, -2.5, 2.5, 2.5])],
             "openings": [hole(0, -2.5, 3.2, 1.0, 2.3), hole(0, 2.5, 3.2, 1.0, 2.3), hole(-2.5, 0, 3.2, 1.0, 2.3), hole(2.5, 0, 3.2, 1.0, 2.3)]},
        ],
        "stairs": [{"floor": 0, "rect": [1.0, -1.4, 2.05, 1.3], "dir": "-z"},
                   {"floor": 1, "rect": [-2.05, -1.4, -1.0, 1.3], "dir": "+z"},
                   {"floor": 2, "rect": [1.0, -1.4, 2.05, 1.3], "dir": "-z"}],
        "roof": {"type": "flat", "parapet": 1.0, "color": {"color": "$roof", "material": "concrete"}},
        "extras": [{"type": "prop", "name": "Floodlight Tower", "at": [-1.4, 0, -1.4], "scale": 0.35, "onRoof": True}],
    })

# ---------------------------------------------------------------------------------------------- furnishing rules
RULES = {
    "silocontrol": ("The launch control room (normally explicit): consoles, chairs.",
                    [{"id": "c", "props": ["Launch Control Console"], "place": "wall", "count": [1, 3]},
                     {"props": ["Office Chair"], "place": "front", "of": "c", "distance": 0.2, "count": [1, 3]}]),
    "silocomputer": ("A computer room: tape mainframes along the walls, a teletype, patch cabinets, strip lights.",
                     [{"props": ["Tape Mainframe"], "place": "wall", "count": [2, 4], "prefer": "far"},
                      {"props": ["Patch Panel Cabinet", "Server Rack"], "place": "wall", "count": [1, 2]},
                      {"props": ["Teletype Printer"], "place": "wall", "chance": 0.8},
                      {"props": ["Archive Boxes", "Filing Cabinet"], "place": "corner", "chance": 0.7},
                      {"props": ["Tube Light"], "place": "ceiling", "count": 2, "y": 0.0}]),
    "silocorridor": ("A complex corridor: bare concrete, a fire point, junction boxes.",
                     [{"props": ["Junction Box"], "place": "wall", "count": [1, 2]},
                      {"props": ["Gas Bottle", "Bucket", "Mop and Bucket"], "place": "corner", "chance": 0.6},
                      {"props": ["Tube Light"], "place": "ceiling", "count": 3, "y": 0.0}]),
    "siloreactor": ("A reactor hall round its vessel: coolant pumps against the walls, gas bottles, a toolbox.",
                    [{"props": ["Coolant Pump"], "place": "wall", "count": [1, 2]},
                     {"props": ["Gas Cylinders", "Toolbox", "Cable Spool"], "place": "corner", "count": [1, 2]}]),
    "siloswitchgear": ("Switchgear: transformers, switchboards, a standby generator, cable reels.",
                       [{"props": ["Pad Transformer"], "place": "wall", "count": [1, 2], "prefer": "far"},
                        {"props": ["Running Generator"], "place": "wall", "chance": 0.8},
                        {"props": ["Junction Box", "Electric Meter"], "place": "wall", "count": [2, 3]},
                        {"props": ["Patch Panel Cabinet", "Server Rack"], "place": "wall", "count": [1, 2]},
                        {"props": ["Cable Spool", "Gas Cylinders"], "place": "corner", "count": [1, 2]},
                        {"props": ["Tube Light"], "place": "ceiling", "count": 2, "y": 0.0}]),
    "siloturbine": ("A turbine hall (normally explicit).", [{"props": ["Turbine Generator"], "place": "centre"}]),
    "siloairlock": ("A blast lock between two blast doors: gas masks, a hazmat rack, a bench.",
                    [{"props": ["Hazmat Suit Rack"], "place": "wall", "chance": 0.8},
                     {"props": ["Bench"], "place": "wall", "chance": 0.7},
                     {"props": ["Bucket"], "place": "corner", "chance": 0.5}]),
    "silodecon": ("Decontamination: showers, a sink, suit racks, bins for contaminated kit.",
                  [{"props": ["Shower Cubicle"], "place": "wall", "count": [1, 2]},
                   {"props": ["Bathroom Sink"], "place": "wall"},
                   {"props": ["Hazmat Suit Rack"], "place": "wall", "chance": 0.7},
                   {"props": ["Laundry Basket", "Trash Bag", "Chemical Drum"], "place": "corner", "count": [1, 2]}]),
    "siloarmoury": ("An armoury: steel rifle racks, ammunition shelving, crates on the floor.",
                    [{"props": ["Steel Rifle Rack"], "place": "wall", "count": [2, 3], "prefer": "far"},
                     {"props": ["Armoury Shelf"], "place": "wall", "count": [2, 3]},
                     {"props": ["Metal Lockers"], "place": "wall", "count": [1, 2]},
                     {"props": ["Wooden Crate", "Small Crate"], "place": "floor", "count": [1, 3]},
                     {"props": ["Table"], "place": "centre", "chance": 0.7},
                     {"props": ["Tube Light"], "place": "ceiling", "count": 2, "y": 0.0}]),
    "silostore": ("A store: steel shelving, crates, archive boxes, a ladder.",
                  [{"props": ["Metal Shelving", "Armoury Shelf"], "place": "wall", "count": [1, 3]},
                   {"props": ["Wooden Crate", "Archive Boxes", "Cardboard Stack", "Small Crate"], "place": "wall", "count": [1, 2]},
                   {"props": ["Toolbox", "Cardboard Box", "Paint Cans"], "place": "floor", "count": [1, 3]},
                   {"props": ["Step Ladder", "Broom"], "place": "corner", "chance": 0.7}]),
    "silocomms": ("A comms room: radio consoles, patch cabinets, a teletype.",
                  [{"id": "r", "props": ["Radio Console"], "place": "wall", "count": [2, 3], "prefer": "far"},
                   {"props": ["Office Chair"], "place": "front", "of": "r", "distance": 0.15, "count": [2, 3]},
                   {"props": ["Patch Panel Cabinet"], "place": "wall", "count": [1, 3]},
                   {"props": ["Teletype Printer"], "place": "wall", "count": [1, 2]},
                   {"props": ["Tube Light"], "place": "ceiling", "count": 2, "y": 0.0}]),
    "silocrypto": ("The crypto room: a safe, a desk with a teletype, filing cabinets.",
                   [{"id": "d", "props": ["Office Desk"], "place": "wall"},
                    {"props": ["Office Chair"], "place": "front", "of": "d", "distance": -0.1},
                    {"props": ["Computer Monitor", "Desk Phone"], "place": "on", "of": "d", "y": 0.77},
                    {"props": ["Safe"], "place": "corner"},
                    {"props": ["Filing Cabinet"], "place": "wall", "count": [1, 2]},
                    {"props": ["Paper Shredder"], "place": "wall", "chance": 0.7}]),
    "silomess": ("The crew mess: tables and chairs, the galley counter, a fridge, vending machines.",
                 [{"id": "t", "props": ["Table"], "place": "centre"},
                  {"props": ["Plastic Chair"], "place": "around", "of": "t", "count": [3, 4]},
                  {"id": "k", "props": ["Kitchen Counter"], "place": "wall", "count": [2, 3], "prefer": "far"},
                  {"props": ["Kettle", "Coffee Maker", "Stacked Plates", "Microwave"], "place": "on", "of": "k", "y": 0.9, "count": [1, 3]},
                  {"props": ["Cooker"], "place": "wall"},
                  {"props": ["Fridge"], "place": "corner"},
                  {"props": ["Glowing Vending Machine", "Water Cooler"], "place": "wall", "count": [1, 2]},
                  {"props": ["Tube Light"], "place": "ceiling", "count": 2, "y": 0.0}]),
    "silobunks": ("The crew bunk room: steel bunks, lockers, kit bags.",
                  [{"props": ["Bunk Bed"], "place": "wall", "count": [3, 4], "prefer": "far"},
                   {"props": ["Metal Lockers"], "place": "wall", "count": [1, 2]},
                   {"props": ["Duffel Bag", "Backpack", "Book Pile"], "place": "floor", "count": [1, 3]},
                   {"props": ["Tube Light"], "place": "ceiling", "count": 1, "y": 0.0}]),
    "silolockers": ("A locker room: lockers along the walls, a bench.",
                    [{"props": ["Metal Lockers"], "place": "wall", "count": [2, 3]},
                     {"props": ["Bench"], "place": "centre", "chance": 0.6},
                     {"props": ["Duffel Bag", "Backpack"], "place": "floor", "chance": 0.6}]),
    "siloworkshop": ("The maintenance workshop: work tables, shelving, generators and drums, an engine on the floor.",
                     [{"id": "t", "props": ["Table"], "place": "wall", "count": [1, 2]},
                      {"props": ["Toolbox", "Paint Cans"], "place": "on", "of": "t", "y": 0.75, "count": [1, 2]},
                      {"props": ["Metal Shelving"], "place": "wall", "count": [1, 2]},
                      {"props": ["Diesel Generator", "Portable Generator"], "place": "wall", "chance": 0.8},
                      {"props": ["Engine Block", "Tyre Stack", "Cable Spool"], "place": "floor", "count": [1, 2]},
                      {"props": ["Oil Drum", "Gas Cylinders"], "place": "corner", "count": [1, 2]},
                      {"props": ["Tube Light"], "place": "ceiling", "count": 2, "y": 0.0}]),
    "silofuel": ("The fuel and propellant store: a propellant tank, drums, a flammables cabinet.",
                 [{"props": ["Propellant Tank"], "place": "wall", "prefer": "far"},
                  {"props": ["Oil Drum", "Red Fuel Barrel", "Chemical Drum"], "place": "wall", "count": [2, 3]},
                  {"props": ["Flammables Cabinet"], "place": "wall"},
                  {"props": ["Jerry Can"], "place": "floor", "count": [1, 2]},
                  {"props": ["Tube Light"], "place": "ceiling", "count": 1, "y": 0.0}]),
    "silotower": ("A service tower landing: a cable reel or a toolbox.",
                  [{"props": ["Cable Spool", "Toolbox", "Gas Cylinders"], "place": "corner", "count": 1}]),
    "silostairs": ("A stair hall: a junction box, a bucket.",
                   [{"props": ["Junction Box"], "place": "wall"}, {"props": ["Bucket", "Mop and Bucket"], "place": "corner", "chance": 0.7}]),
    "siloguardpost": ("A guard post: a desk with a field telephone, a chair, a locker, ammunition.",
                      [{"id": "d", "props": ["Office Desk"], "place": "wall"},
                       {"props": ["Office Chair"], "place": "front", "of": "d", "distance": -0.1},
                       {"props": ["Desk Phone"], "place": "on", "of": "d", "y": 0.77},
                       {"props": ["Metal Lockers"], "place": "wall", "chance": 0.7},
                       {"props": ["Armoury Shelf", "Wooden Crate"], "place": "wall", "chance": 0.6}]),
}


def showcase():
    """Maps/Dev/BuildingsSilo.json: every building above (and every Facility prop) on one Dev map, the access bunker
    buried in a pit with a trench to its lower door, as the Silo map uses it. The self-test sends bots to every room."""
    ops = [{"op": "Ground", "color": [0.36, 0.42, 0.3]}]
    place = [("Launch Control Centre", -36, -28, {}), ("Silo Power Plant", 0, -28, {}), ("Silo Blast Lock", 34, -28, {}),
             ("Silo Armoury", -42, 2, {}), ("Silo Comms Room", -20, 2, {}), ("Silo Crew Quarters", 3, 2, {}),
             ("Silo Maintenance Bay", 27, 2, {"variant": {"palette": "green paint"}}), ("Silo Service Tower", -40, 28, {}),
             ("Concrete Guard Tower", 4, 28, {}), ("Concrete Guard Tower", 16, 28, {"variant": {"palette": "olive"}})]
    for name, x, z, kw in place:
        op = {"op": "Building", "name": name, "at": [x, z], "spawns": True}
        op.update(kw)
        ops.append(op)
    ops.append({"op": "Building", "name": "Silo Access Bunker", "at": [-19.5, -4.1, 28.5], "anchor": "world"})
    ops.append({"op": "Spawn", "x": -19.5, "z": 34.0})
    import glob as _g
    props = sorted(json.load(open(f))["name"] for f in _g.glob(os.path.join(REPO, "Props", "Facility", "*.json")))
    for i, n in enumerate(props):
        x = -52 + (i % 14) * 7.5
        z = 40 + (i // 14) * 7
        ops.append({"op": "Prop", "name": n, "x": x, "z": z, "y": 1.6 if n in WALL_PROPS else 0})
    for x, z in ((-54, -12), (54, -12), (-54, 16), (54, 16), (0, 16), (0, -12), (40, 34), (-28, 44), (28, 46), (54, 44)):
        ops.append({"op": "Spawn", "x": x, "z": z})
    ops.append({"op": "AutoWaypoints", "spacing": 6})
    m = {"format": "call-of-slop-map/1", "name": "Silo Buildings",
         "description": "The missile-complex buildings of the World Tour map Silo and the Facility props: control centre, power plant, locks, side rooms, towers.",
         "author": "Call of Slop", "order": 962, "halfX": 62, "halfZ": 52, "sky": [0.58, 0.68, 0.8],
         "terrain": {"cell": 1, "margin": 8, "features": [
             {"type": "hole", "shape": "polygon", "points": [[-24, 24], [-15, 24], [-15, 33], [-24, 33]], "floor": -4.1},
             {"type": "cave", "points": [[-19.5, 24.2, -4.1], [-19.5, 19.0, -4.1], [-19.5, 9.0]], "width": 4.6, "height": 3.6, "open": "end"}]},
         "ops": ops}
    with open(os.path.join(REPO, "Maps", "Dev", "BuildingsSilo.json"), "w") as f:
        json.dump(m, f, indent=1)
        f.write("\n")


WALL_PROPS = {"Big Wall Display", "Status Board", "Wall Pipe Run", "Radiation Sign", "Caged Bulkhead Lamp", "Tube Light", "Wall Cable Tray"}


def main():
    for folder, d in BUILDINGS.items():
        os.makedirs(os.path.join(BLD, folder), exist_ok=True)
        with open(os.path.join(BLD, folder, "building.json"), "w") as f:
            json.dump(d, f, indent=1)
            f.write("\n")
    for t, (desc, items) in RULES.items():
        with open(os.path.join(FURN, t + ".json"), "w") as f:
            json.dump({"format": "call-of-slop-furnishing/1", "type": t, "description": desc, "items": items}, f, indent=1)
            f.write("\n")
    showcase()
    print(f"{len(BUILDINGS)} buildings, {len(RULES)} rules")


if __name__ == "__main__":
    main()
