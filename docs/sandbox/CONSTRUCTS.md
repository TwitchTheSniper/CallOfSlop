# Constructs: the building system of Sandbox

Sandbox is a persistent survival world with factions, played by a small group of friends: players gather, refine and
build bases, vehicles and motherships from modular parts, and fight and trade beside the factions' own builds. This is the
index of the construct guide, its building system. The guide is split by system into the files under `constructs/`, each
a few dozen KB. Read this index, then open only the one file that covers your task; never read all of them. The
milestone-1 record and how to play are in [overview-and-playing](constructs/overview-and-playing.md).

## [How a build should look](constructs/look.md), 15 KB

**Every agent that authors a blueprint, a generator or a building reads this first.** Valid is not finished: paint by
section, finishes, lamps, detail, the authoring fields, a detail budget, and the `--look-report` baseline.

## [How a building should look](constructs/buildings.md), 9 KB

Read before authoring a building: what every building needs (the job gives the shape, an outline that reads at 50 m, a
marked way in), then each faction's style. The Ministry of Slop: Brutalism, cold-war fittings, the colony's street.

## [The rust bucket](constructs/ship.md), 3 KB

The walkable two-deck ship of the rust bucket start: its hull parts, rooms, engines and how it flies.

## [The Magpie mining ship](constructs/miner.md), 4 KB

The first Survival ship: a lofted two-deck miner with four drills on rams through its keel, ore hoppers on its flanks; how it flies.

## [The Zero Day torpedo frigate](constructs/torpedo-frigate.md), 9 KB

The IT Department's warship: a faceted lofted hull, two decks the length of the ship, torpedo sponsons, a spinal railgun, PDCs, flares; its AI crew, which flies and fights any stabilised craft by itself (ConstructShipAi.cs).

## [Space stations](constructs/station.md), 3 KB

The station kit (SpaceStationParts): Caledon Highport's pieces, how a station is laid out and generated, and the Station gallery.

## [The Shuttle](constructs/shuttle.md), 4 KB

The Zero Day's boarding craft, carried on a cradle on its spine: hull grapples grip an enemy hull, a breaching cutter burns through it (ConstructBreach.cs), and a blueprint's `"carry"` list (craft built held in its docks).

## [The Limpet](constructs/limpet.md), 4 KB

The Zero Day's 8-person boarding pod, docked under its keel: the command view's Board order flies it to another craft's door (it seals on it) or hull (it cuts through), Game/GameNavBoard.cs.

## [The Dray container hauler](constructs/hauler.md), 3 KB

A cargo ship round a hold of fourteen container slots, an overhead crane on rails and a belly well with sliding doors, two big drives with their engines in a drive room aft, and attitude jets; no legs, lamps or fittings yet.

## [The Packet spine freighter](constructs/freighter.md), 3 KB

The Traders' freighter: a glass-nosed crew cab, a hull-only spine with three cargo racks a side (two standard 2.5 x 2.5 x 6.5 m containers each, open outboard), engineering and two drive nacelles on pylons.

## [The Haywain hauler](constructs/haywain.md), 3 KB

The farmers' medium hauler in a combine harvester's clothes: a glass cab room standing out on the nose, a crew block, six standard containers in a bay open to the sky inside a raised tank coaming, an engine box with exhaust stacks and two low drive pods.

## [The Dibber lander](constructs/dibber.md), 3 KB

The farmers' small vertical lander: an eight-sided capsule on end on four jointed legs, a glass cab low on the nose, two decks (cockpit and belly hatch below, systems above), one belly jet and a trim jet under each leg.

## [The Satchel courier](constructs/courier.md), 2 KB

The Traders' small fast courier: two standard containers side by side in a bay open to the sky, so a dock crane lifts them out; cab forward, engineering aft, two drive nacelles.

## [The Docket tug](constructs/tug.md), 3 KB

The Traders' tug: a stubby head (push bow, crew cab, a raised glass wheelhouse, four stacked drive nacelles) towing an open truss string of three cells, each taking one standard container on twist locks for a dock crane.

## [The Grapnel salvage tug](constructs/grapnel.md), 3 KB

The Salvagers' salvage tug from Karl's picture: boxy yellow hull, glass cockpit, three tool mounts (sockets) on the chin, tank racks on the flanks, two hull drives on the stern; the Satchel's cab and container bay.

## [Ship works](design/ship-works.md), 48 KB (the design)

Engines and turrets with insides: a ship's drive over 500 kN has works behind it through the hull (Drive Core, Fuel Pump,
Coolant Exchanger: one cylinder bolted on behind the nozzle's Hull Mount) or is a pod with them in its cowl; what a drive pushes is capped by its working works
(ConstructWorks.cs, Constructs/ShipWorks.json, the `ship_works` generator); breakdowns on every crewed ship. The drives
are in; turret rings, hoists, rocket and ion drives and lasers are its later phases. A code guide replaces it in its last phase.

Each file below lists its `## ` sections with their headings exactly as they were in the old single file, so a code
comment such as `CONSTRUCTS.md "Extrusion"` is found by searching this index for the heading. To look inside a file,
`grep -n '^## ' docs/sandbox/constructs/FILE.md` and read the section you need. New text goes into the file that holds
its system; a new `## ` section is also added to its list here.

## [Overview and playing](constructs/overview-and-playing.md), 84 KB

The milestone-1 record, how to play it, what a construct is, Creative or Survival, factions.

- Playing it
- What a construct is
- Factions
- Creative or Survival

## [Building and tools](constructs/building-and-tools.md), 82 KB

The configure tool, fusing, free placement, painting, the remover, snappy/socket/point/freeform attach, building sounds.

- The configure tool
- Fusing
- New constructs (free placement)
- Spawning with a ghost
- The paint tool (board item pp01)
- The texture paint tool
- The material painter
- The remover tool (board item rt01)
- Building sounds
- Machine sounds
- Snappy building (decision 24, board item sb01)
- Centre sockets (board item cs02)
- Joint sides (board item js01)
- Freeform attach (board item fw01, decision 25)
- Socket attach (board item sw01, decision 26)
- Point attach (board item pw01)

## [Parts and data](constructs/parts-and-data.md), 48 KB

Part files and data, physics-only parts, structural parts and size classes, building pieces, caps.

- The data
- Physics-only parts and cargo volumes (decision 21)
- Structural parts (the scale chart)
- Size classes (the capital parts)
- Building pieces
- Performance and caps

## [Joints, logic and control](constructs/joints-and-logic.md), 46 KB

Actuators, linear and flexible joints, mechanisms, control modules, screens and panels, cameras, drones, robot shells.

- Machine screens
- Display panels
- Drones
- Robot shells
- Touch panels
- Video Screens and Music Players
- Cameras and screens
- Actuator sizes and strength
- Linear joints
- Pieces of a mechanism
- Free rotation
- Flexible joints
- Control modules

## [Drivetrain and engines](constructs/drivetrain-and-engines.md), 55 KB

Drivetrain part families, suspension, modular engines, tracks, big drive.

- Drivetrain parts (the families)
- Suspension types
- Modular engines
- Tracks
- Big drive
- All-wheel drive and round wheels

## [Power and links](constructs/power-and-links.md), 57 KB

Links between constructs and power, routed links, world links.

- Links between constructs and power (milestone 3)
- Routed links (decision 20)
- World links (board item wl01)

## [Weapons](constructs/weapons.md), 93 KB

Construct weapons (a craft's radar and lock among them), merged receivers, weapon builder parts in Sandbox, the Gun Workbench.

- Construct weapons (milestone 3)
- Merged receivers
- Weapon builder parts in Sandbox
- The Gun Workbench (wp01, part 2)
- Capital battles: railgun bores through whole ships, spall, holes drawn on every PC, break-up, scrap (design/capital-battles.md)

## [Flight](constructs/flight.md), 37 KB

Air and lift, flight control, jets, rockets, repulsors, wings, propellers, the Fan and Airframe Formers, flight in survival, pilot safety. Spacewalks (space suit, MMU and its dock, breath in vacuum) are [EVA.md](EVA.md).

- Pilot safety
- Air and lift
- Flight control
- Jets, rockets and repulsors
- Wings
- Propellers and rotors
- The Fan Former
- The Airframe Former
- Flight in survival

## [Gathering and salvage](constructs/gathering-and-salvage.md), 34 KB

Salvage and the inventory, the salvage haul, gathering, scoops.

- Salvage and the inventory (milestone 3)
- Salvage haul (decision 31)
- Gathering (decision 14)
- Scoops

## [Production](constructs/production.md), 82 KB

Refining, extrusion, the Wheel Former, the Press, the Panel Former, the Laser Cutter, machine tiers, assembling, build stations, production lines, production monitors, conveyors, smart logistics, work orders, the crafting terminal, the Panel Designer, reservations, the building station, lifters, the Production Showcase.

- Refining (decision 14)
- Extrusion
- The Wheel Former
- The Press
- The Panel Former (formed panels by name, presets, `--form-panel`, lofted hull parts get a recipe)
- The Laser Cutter (cut panels and plates by order, the cutting time law, offcut in tenths)
- Machine tiers
- Assembling
- Build stations
- Build stations: the Selector and the Blueprint Desk
- Production lines (the Line Console, the recipe picker, resource icons)
- Production monitors (Data out to any display; Alarm, Running, Made, Stop after)
- Conveyors
- Smart logistics (Conveyor Routers send pieces where they are wanted, else to storage)
- Work orders
- Crafting terminal
- Panel Designer (presets with sliders, the preview, saved and shared designs)
- Reservations
- Building station
- Lifters (the Loader Arm in five tiers: a Building Station's extra hands; settings, ports, the reach view, saves)
- Production Showcase

## [Storage and cargo](constructs/storage-and-cargo.md), 42 KB

Storage volumes, physical storage, modular shelves, the Smart Terminal, the Inventory Beacon, in-wall bays.

- Storage volumes
- Physical storage (decision 18)
- Modular shelves
- The Smart Terminal
- The Inventory Beacon
- In-wall bays (Karl, 2026-11-20)

## [Big systems](constructs/big-systems.md), 20 KB

Doors and event doors, large systems (motherships, carriers), carrier work.

- Doors
- Event doors
- Large systems
- Carrier work

## [Spawning bots and zombies](constructs/spawning-npcs.md), 10 KB

Spawning bots and zombies in Sandbox.

- Spawning bots and zombies

## [Network and scale](constructs/network-and-scale.md), 26 KB

The network protocol for constructs, thousands of constructs in a world.

- Network (protocol 82; 84 for free placement; 86 for the drivetrain's rpm; 87 for glass shattering; 88 for the physics gun)
- Scale: thousands of constructs

## [Self-tests and history](constructs/self-tests-and-history.md), 57 KB

The construct self-tests, milestone records and decisions.

- Self-tests
- Decisions made for milestone 1 (open questions it touches; the developer may change them)
- Milestone 2: joints, logic and the drivetrain
- Next: milestone 3 and later
- Milestone 1's list, as milestone 2 left it
