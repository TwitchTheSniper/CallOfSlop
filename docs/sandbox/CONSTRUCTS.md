# Constructs: the building system of Sandbox

Sandbox is a persistent survival world with factions, played by a small group of friends: players gather, refine and
build bases, vehicles and motherships from modular parts, and fight and trade beside the factions' own builds. This is the
index of the construct guide, its building system. The guide is split by system into the files under `constructs/`, each
a few dozen KB. Read this index, then open only the one file that covers your task; never read all of them. The
milestone-1 record and how to play are in [overview-and-playing](constructs/overview-and-playing.md).

## [How a build should look](constructs/look.md), 12 KB

**Every agent that authors a blueprint, a generator or a building reads this first.** Valid is not finished: paint by
section, finishes, lamps, detail, the authoring fields, a detail budget, and the `--look-report` baseline.

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
- The paint tool (board item pp01)
- The texture paint tool
- The material painter
- The remover tool (board item rt01)
- Building sounds
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

Construct weapons, merged receivers, weapon builder parts in Sandbox, the Gun Workbench.

- Construct weapons (milestone 3)
- Merged receivers
- Weapon builder parts in Sandbox
- The Gun Workbench (wp01, part 2)

## [Flight](constructs/flight.md), 37 KB

Air and lift, flight control, jets, rockets, repulsors, wings, propellers, the Fan and Airframe Formers, flight in survival, pilot safety.

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

Refining, extrusion, the Wheel Former, the Press, machine tiers, assembling, build stations, production lines, conveyors, work orders.

- Refining (decision 14)
- Extrusion
- The Wheel Former
- The Press
- Machine tiers
- Assembling
- Build stations
- Build stations: the Selector and the Blueprint Desk
- Production lines
- Conveyors
- Work orders

## [Storage and cargo](constructs/storage-and-cargo.md), 42 KB

Storage volumes, physical storage, modular shelves, the Smart Terminal, the Inventory Beacon.

- Storage volumes
- Physical storage (decision 18)
- Modular shelves
- The Smart Terminal
- The Inventory Beacon

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
