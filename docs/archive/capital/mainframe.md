# The Mainframe capital ship

**Archived (Karl, 2026-11-20): capital ships are on the back burner** (the game isn't ready for them and they have no purpose
yet; the Zero Day is a medium ship). The ship, its parts and the Capital duel start were taken out of the game; the engine work
it drove stays (merged far detail and occlusion for big ships, reversed float depth, railgun bores and scrap, readouts that
don't wake a body, the 4096-handle store pool, the openings' `"hold"`). To bring it back, restore these from commit `d2c4dbb4`
(main before the removal): `git checkout d2c4dbb4 -- Source/PartKit/Generators/MainframeParts*.cs Constructs/Parts/Mainframe
Constructs/Blueprints/Mainframe.json Story/Starts/CapitalDuel.json Story/Opening/CapitalDuel.json`, then put back its line in
`PartKit.cs`, its rows in `Generators/Assembling.cs` (then `--gen-parts assembling`), "Mainframe" in `Categories.json`'s Ship
name rule, `ConstructPreview.GalleryAboard` (the gallery's berthed Zero Day), and bump the protocol (the part ids move).
The paused lighter variant is `/mnt/project-files/mainframe/lighter-mainframe.patch` (project files); Karl's ruling was
"stop simplifying": keep the detail, get the speed from the renderer.

Karl (2026-11-20): "the first capital ship ... taking aspects from Day Zero but a new, capital-ship style hull ... multiple
reactors, thrusters, multiple levels", an internal berth for a Zero Day, a shuttle bay with a large hangar, four Limpets, a
huge store of robot shells, missile bays, "dozens if not hundreds of airlocks, hatches", "an elevator system within", about
1 km long, and "use the Laser Cutter system to make holes ... walls, floors and hull panels ... push the production system as
far as it will go". Shown in the Ship gallery first; what is built in game next waits on his notes.

Generator: `Source/PartKit/Generators/MainframeParts*.cs` (`--gen-parts mainframe_parts`), which writes
`Constructs/Parts/Mainframe/*.json` (twelve parts) and `Constructs/Blueprints/Mainframe.json`. Change the generator, never its
output. The ship's pieces are blueprint-only (`Categories.json`'s name rule has "Mainframe").

## How it is made

Where the Zero Day is drawn as big ship-named parts, the Mainframe is built the way a player's yard would build it. Its hull,
decks, walls and bulkheads are steel plate off an Extruder ("Extrusion plate ...", 888 of them) and plate a Laser Cutter has
cut ("Cut Plate (...)", 578): corners cut to the hull's lines and openings cut for doors, hatches, lift shafts, airlocks, bays,
missile cells and turret wells. Every sheet is one the production line makes, up to the Capital Laser Cutter's 64 by 32 m bed
with openings up to 32 m (`HullLoft.CutPlateMaxCm`, protocol 383). The ship's own parts are only what a yard can't cut: the
frame nodes the slanting plates hang on, the beam lights, the cell hatches, the reactors, the drives and their works, the
flight computer, the RCS blocks and jets. Everything else is the game's own parts.

The `Writer` (`MainframeParts.Writer.cs`) steps a blueprint the way `BlueprintScript` does: each part on a socket or welded to
what it touches, checked against the part files as the game reads them. `Lay.cs` is the plan, `Hull.cs` the shape, `Bays.cs`
the big spaces, `Interior.cs` rooms, lifts and passages, `Own.cs` the ship's parts, `Carry.cs` the craft it carries.

## The shape

1,026 m from the bow's tip to the bells' lips, 158 m across, 119 m to the missile cells' tops, 65,350 t. The Zero Day's lines
at capital scale: a flat keel and spine, sides flaring to a sharp beam line and leaning in above it, a faceted chisel bow. Each
station of the hull is the midship section scaled about one line, so every facet of every band is a flat trapezoid: one plane,
one frame node, its plates laid flat on it. Pale skin; dark armour slabs proud of it in steps (staggered from facet to facet,
none over an opening); link-blue light along the beam line; a dark armoured transom with the six drives through it.

Ship metres in the generator: x across (+ starboard), y up from the keel, z from the bow tip aft. The blueprint's origin (step
0) is mid-ship on the main deck, (0, 30, 500), so every carried craft lies within the 200 m a carried craft may.

## Zones

| z (m) | Zone | What is in it |
| --- | --- | --- |
| 0-90 | Bow | the nose, behind a bulkhead at 30 m; no rooms yet |
| 90-160 | Crew | three storeys of berthing, cabins, messes, galleys, heads, a medbay, rec rooms; two long-range sensors on the spine over it (two more over the bridge) |
| 160-230 | Marines | three storeys: ready rooms, armouries, suit rooms, briefing rooms, a brig |
| 230-300 | Forward missile field | two blocks of 80 cells under the spine, cell hatches cut through the skin |
| 300-420 | Hangar over the boarding deck | three Shuttles on pads under roof doors; flight control and stores; four Limpet pockets in the lower flanks |
| 420-580 | Command over the Zero Day's berth | three storeys: offices, server rooms, the CIC, the bridge on top; the berth below, keel open under it |
| 580-700 | Shell vault over the yard | 384 Robot Shells in their docks; under it the yard: laser cutters, extruders, panel formers, assemblers, recyclers |
| 700-790 | Aft missile field | two more blocks of 80 cells |
| 790-860 | Reactors | four 400 MW fusion reactors; four fuel bunkers on the keel under them |
| 860-930 | Engine room | capacitor and battery banks, the drives' desks |
| 930- | Drives | six drives through the transom, each with its works (core, pump, exchanger) on its axis |

Decks (tops, from the keel): yard 3.9 m, boarding deck 15, main deck 30, upper 34.5, top 39, citadel roof 43.5, hangar deck 52.
A corridor runs the length of the main deck; flank passages run 300-860 m on both sides.

## Doors, airlocks and lifts

- 243 bulkhead doors (every room, every corridor and the bulkheads' doorways), 58 docking hatches (an airlock every 20 m along
  both flank passages, and one over each Limpet's roof), 40 cell hatches over the missile cells, three hangar roof doors.
- Seven lifts: a Freight Lift Car on a Linear Actuator Large 32 m stood on end against a bulkhead, its shaft cut through every
  deck it passes (crew, marines, command, boarding deck to main deck, main deck to hangar from both ends, yard to vault).

## Flight and fuel

A Mainframe Flight Computer (a flight stabilizer with a capital gyro, set to the ship keys with mouse aim) stands on the bridge
and flies from the Bridge Helm, the seat a stabilizer takes when nothing is wired to it. Six 40 MN drive bells (about 3.7 m/s2 at full thrust) and eight RCS blocks at the spine's and
keel's edges, fore and aft, each with three 8 MN attitude jets (out, up or down, forward): far apart along the ship they turn
it about every axis, slide it and slow it. None points aft: a Ship thruster over 500 kN that pushes the craft forward is a main
drive and needs works and an engine (`ShipWorks.JetMost`); the vehicle check says so. Fuel: the drive mounts' header tanks (6 x 300 kl), the RCS blocks' (8 x 50 kl) and four
Capital Fuel Bunkers (4 x 4,000 kl), 18,200 kl in all; the bells burn 480 l/s at full thrust. `--check-construct-parts` says
"doesn't drive: no engine" as it does for the Zero Day (a thruster ship has no wheels' engine).

## Carried craft

The blueprint's `carry` list holds three Shuttles (on the hangar's pads, each held by a Docking Clamp) and four Limpets (in
their pockets, each under a clamp with a docking hatch over its roof). Every shipped blueprint's carried craft are loaded after
all the shipped ones are read (`ConstructBuild.LoadBlueprints`), since a carry list names another ship.

The berth is built empty in game: a carried craft can't carry its own yet (`ConstructCargo.BuildCarry`), and a Zero Day
carries its Shuttle and Limpet. The berth's Docking Clamp Large holds a Zero Day that comes into its box. The gallery shows
one there (`MainframeParts.GalleryBerthed`), with its own Shuttle and Limpet.

## Cost

5,684 steps, 6,448 parts with the carried craft, 81 kinds of part. `--check-construct-parts` takes about 45 s with it, and
`--check-vehicle` on it about 70 s: 0 coplanar faces among 57,057 boxes, and only the checks every ship fails (wheels, a ground
drive, the part and mass limits of a car) besides its ten PDCs, which nothing fires yet.
In the Ship gallery it is 1.2 million triangles: past 300,000 a model is written as one template per kind of part and its
placed copies (`ConstructPreview.Instanced`, 5.4 MB), the page expands them as it loads, its near plane follows the camera and
its grid's cells grow with the model. The biggest shares: 320 missiles, 384 robot shells and their docks, 296 server racks.
How it runs as one body in a world (ticks, colliders, the client's first copy) is not measured yet.

## Not yet

- Weapons aren't wired: the ten PDCs have no gunner's seat or wire and no ammo feed, and the 320 missiles in their cells are
  placed, not linked to a launch control (the blueprint writer has no links yet). The Zero Day's wiring is the pattern.
- The berth's Zero Day and the Shuttles' and Limpets' own craft wait on nested carrying.
- Power and air: the reactors, batteries and life support stand in their rooms with no cables or pipes laid between them.
- Running it as a body in a world: ticks, colliders and a joined client's first copy at this size are not measured.
