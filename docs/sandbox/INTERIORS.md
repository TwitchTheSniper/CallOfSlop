# Interiors: planned rooms for mobile and static bases

Large vehicles (medium to capital) are mobile bases, and their interiors are laid out almost like small maps. This is a
separate system from the vehicle kit: `Source/Interiors/` (namespace `CallOfSlop.Interiors`), data in `Constructs/Interiors/`.
It calls the kit's `BlueprintScript` to place parts through the game's own build rules but never edits kit files. The
planner knows nothing of vehicles: its input is a hull volume and keep-outs, so static bases can use it later.

## The decisions (user, 2026-11-10)

- **Size classes by tonnage**, like ship classes (medium, large, extra large, capital). The bands are open: the ones in
  `SizeClasses.json` are provisional.
- **Hybrid layout**: a plan (rooms, adjacency, flow) is fitted into decks, and the rooms are filled from module sets. The long-term aim
  is fully generated rooms.
- **Grid plus free rooms**: some rooms sit on a uniform grid and the rest follow lines and contours (phase 2). Larger classes
  get slightly roomier rooms and corridors. Rooms have minimum sizes. Space that is too small becomes void, fuel or storage,
  or merges into a neighbour.
- **Loot per room type** stays consistent (a medical bay holds medical things, an armoury guns, ammunition and weapon parts).
- **Fully working rooms**: real machines, storage slots, power and running elevators, not dressing.
- **Combat**: bulkhead sections keep damage, fire and breaches local. Boarding paths have entry points, choke points and
  defensible rooms. Critical rooms (bridge, magazine, engineering/reactor) are weak points.
- **Logistics**: early on, players and bots carry things by hand. Later come push carts, freight elevators, short chutes and a train
  system (a separate track): the interior **reserves** a lane for it.
- First target: the Titan Walker (`Constructs/Blueprints/TitanWalker.json`, docs/sandbox/GIANTS.md).

## Data (`Constructs/Interiors`, loaded by the tools only; a bad file is refused with a reason)

| File | What |
| --- | --- |
| `SizeClasses.json` | Each class's tonnage band (bands must meet), deck rules, programme (the rooms it asks for), `fill` (what a big leftover may become), `chassis` (the rooms planned inside a hollow chassis) and `budget` (the most parts, links, joints and event doors the fitted construct may use: phases 5 and 7). **Provisional.** |
| `RoomTypes.json` | Each room type's minimum size `[along the spine, deep]` in cells, `want` (cells of floor), `layout` (`grid`, or `free`: a rectangle plus a wing, phase 2; system rooms stay on the grid), `deck` (lower/upper/any), `row` (inner beside the spine, outer on the hull side), `end` (bow/stern), `wants`/`avoids`, `critical`, `exterior` (may open outside), `modules`, `loot`. System rooms (stairs, elevator, storage, fuel, spare, dead) are placed by the planner. |
| `ModuleSets.json` | The `shell` parts (slab, stair slab, stair, walls, corners, door wall, door corner, garage wall, hinge, door leaf, adapter pad, the event doors `swingDoor`, `bulkheadDoor`, `doubleDoorL`/`R` (phase 7), and `panels`: the long walls of phase 5, longest first) and, for each set, the parts a room holds (`count`, `perCells`, `loot`, `role`) and `missing`: what it should hold that no part does yet. |
| `LootHooks.json` | A room's loot id mapped to a `Constructs/Loot.json` table: the interiors' own `room ...` tables (phase 3). |

Provisional numbers (phase 1):

| Class | Tonnes | Corridor | Room band depth | Rooms grow | Section (bulkheads) | Margin | Decks | Elevator | Reserved lane |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Medium | 50 to 500 | 1 cell | 2 | 0 | 4 cells | 0 | 1 | 1x1 | no |
| Large | 500 to 5,000 | 1 | 2 | 0 | 6 | 1 | 2 | 1x1 | no |
| Extra large | 5,000 to 20,000 | 1 | 3 | +1 | 8 | 1 | 2 | 1x2 | yes |
| Capital | 20,000 and up | 2 | 3 | +1 | 8 | 1 | 2 | 2x2 | yes |

A cell is 4 m and a storey 3 m in every class, because phase 1 builds with the Building pieces (2.75 m walls on 0.25 m slabs,
a 2 m and 4 m module: docs/sandbox/CONSTRUCTS.md "Building pieces"). The loader refuses anything else.

## The code

- **`InteriorData.cs`**: the loader and its checks: types, ranges, bands that meet, programme and adjacency names, module sets and loot
  hooks that exist. `UnknownParts()` lists the part names this build lacks.
- **`InteriorPlan.cs`**: the plan: `GridRect` (cells, inclusive), `PlanRoom` (a rectangle on a deck; a free room adds `Wings`,
  and `Cells()` and `Area` count them), `PlanDoor` (a side and a slot: -1 for the side's door corner,
  otherwise the 4 m middle piece, which lines up with the neighbour's across a shared edge), `PlanBulkhead`, `PlanEntry`, the
  reserved route, and `Ascii()` (deck maps and the room list).
- **`InteriorPlanner.cs`** (`HullVolume`, `InteriorRng` SplitMix64, `InteriorPlanner.Plan` and `Check`):
  1. Cells from the hull size. Keep-out boxes block the cells they reach, on the decks their height reaches.
  2. A corridor spine runs down the middle on every deck. On the lowest deck the reserved logistics lane runs beside it
     (classes with `reserveRoute`).
  3. The strips either side are split into an inner band (on the corridor) and an outer band (on the hull side) when both are at
     least 2 cells deep.
  4. Stairwells (two when the hull is two sections long) and the freight elevator shaft go in the north inner band, with the same
     rectangle on every deck and inside one section.
  5. The programme rooms, critical and big ones first: the deck by preference, then every free place in a band deep enough,
     inside one section. The place is scored by end, band, wanted and avoided neighbours and packing, with a small seeded
     jitter. The widest that fits wins.
  6. Leftovers: a run of free columns 1 or 2 cells wide merges into a neighbour in the same band and section. Otherwise it
     becomes spare space (4 or more cells), fuel (1 cell, lowest deck), storage, or void (no door).
  7. Doors: every room on the corridor or lane gets one. Exterior rooms on the lowest deck's hull side get one outside (the
     hangar's is a wide ramp opening). Rooms that want each other get one where they share at least 2 cells in one section.
     Critical rooms never get extra doors. Unreached rooms then get a door through a reached neighbour, or outside on the
     lowest deck.
  8. Bulkheads cross the corridor at each section edge. Entry points are the lowest deck's spine ends, the ramp and the
     outside doors.
  9. `Check`: rooms placed, minimum sizes, reachability from outside (doors, stairs and the freight lift between decks),
     doors on critical rooms.
- **`InteriorBuilder.cs`** (`InteriorBuilder : BlueprintScript`):
  1. `From` replays a blueprint's socket steps and wires through the build rules. It refuses welds, links, hoses, connectors and tracks.
  2. `Measure` works out the class from the parts' mass. It lays the grid on the root part's top-socket lattice with the class
     margin, so a slab's first socket lands on a pad. Every other part's bounds become a keep-out, and the bow comes from the
     helm's facing.
  3. `Build` places, in this order:
     - adapter pads (`Adapter Capital to Small`) on the root's capital sockets under the grid;
     - slabs, in passes from the pads and from neighbours;
     - each deck's room walls along the outline of its cells, one wall between two rooms (phase 5: the room on the south or
       east side of an edge builds it, and a shaft always builds its own); plain runs take the longest Steel Panel Wall that fits
       before a doorway, shafts 4 m pieces; corners, and a 2 m piece at each end that meets an inner corner or a shared edge;
     - door walls and door corners, each with an event door on its jamb (phase 7: no hinge, no joint): a `Bulkhead Door` that slides into
       the wall piece beside it on bulkheads, critical rooms and lift landings, a `Steel Double Door L`/`R` pair where a hangar or the mess
       is on either side, a `Steel Swing Door` elsewhere and on every door corner (one jamb). Critical rooms' and bulkheads' doors go on at
       once, the rest last (the deck's, then the void's) while the class's door budget lasts; Garage Wall openings (ramps, the lane) stay open;
     - bulkheads (a door in the spine, a Garage Wall opening over the lane);
     - closed spine ends on upper decks;
     - stairs: a Steel Straight Stair a cell in from the stairwell's west wall, rising +z under a Stair Floor's opening. If the
       rules refuse it, it is welded on and counted.
     - the next level's slabs, on the walls below;
     - each room's things from its module set (generated rooms' last, while the budget lasts), one to a free slab socket (block middles first; the gunner's station and Fire Director side by side), with the loot hook on
       containers marked `loot`, and the helm and gunner wired where the rules allow; a fuel plant (`role: plant`) gets a Fuel
       Tank fitted to it, and a room's lamps a Switch on the first lamp, wired to each;
     - the freight lift ("Phase 2 record");
     - the bulkhead seals (phase 7): a Switch per bulkhead line beside the bridge's lights switch, wired into each of the line's doors' Lock
       port, its NOT Gate into their Open port;
     - the power report: every electrical part no working battery or plant reaches, and plants no fuel reaches.
  4. Refusals, notes and missing parts are listed, then the budget by kind (`budget walls: ... parts, ... links, ... joints,
     ... wires, ... t`; a link counts for the later of its two parts) and the totals against the engine's limits.
- **`Program.Interiors.cs`**: `--gen-interior <blueprint name or file> [--seed S] [--out DIR] [--plan] [--chassis hollow|armoured]`
  (`--chassis` swaps the blueprint's root for the hollow chassis or armoured hull in the written file only: "Phase 4 record"). Writes `<Name> Fitted`
  (default `Constructs/Blueprints`) and prints the check. It loads the file back through `ConstructBuild.BuildOnGround` and
  compares part counts. With `--seed` it also plans twice and compares. `--plan` prints the ASCII maps. Console output goes to
  `callofslop.log` next to the exe, as for every tool.

- **`Program.InteriorWalk.cs`** (phase 6): `--interior-walk [fitted file] [--of base blueprint] [--seed S] [--damage] [--walk S]`, a
  scripted player walk-through of a fitted construct, headless (see "Phase 6 record"). Output in `callofslop.log`.

Nothing runs per tick. The planner and builder run offline, at generation time.

## Phase 1 record (branch `feat/interiors-1`)

`Constructs/Blueprints/TitanWalkerFitted.json` is `--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1`, written
separately so the walker session can keep regenerating `TitanWalker.json`. Here is what the run gave:

- **Sizing:** 29,289 t, so Capital. The grid is 30 x 14 cells (120 x 56 m) on 2 decks.
- **Rooms:** all 12 programme rooms are placed and every minimum size is met. Shafts: 2 stairwells and a 2x3 elevator shaft.
  28 leftovers: 25 spare, 1 fuel, 1 storage, 1 void.
- **Reachability:** 45 of 45 rooms can be reached. Critical rooms each have one door.
- **Bulkheads and entries:** 6 bulkheads and 6 entry points.
- **Parts:** 2,281 placed, none refused, none welded, 51 door leaves hung, 28 loot containers. The file has 2,292 steps, and
  loading it back gives 2,292 parts.
- **Time:** planning took 0.1 s and building 3.3 s.
- **Mass:** about 3,100 t added (+10.6%).

What was tried by hand: the run above (seed 1, twice: the plans match); the ASCII maps read through for room placement,
doors and the lane; the written file loaded back by the real loader.

Not tried: drawing it (no window run); spawning it in a world and walking it with the added mass (the legs carry a load
limit: the walk speed may drop); a player walking the corridors, stairs and doors; other seeds and other blueprints;
the medium, large and extra-large classes; a client over the network. The z-fighting check was not run on the fitted
walker. Two neighbours' walls stand back to back, so their touching faces are coplanar but hidden.

Known gaps:
- The bridge's helm can't be wired to the walker's Driving Module, because a module takes one input and the hip helm has it.
- The gunner's station is placed more than 8 m from its Fire Director.
- Power isn't linked (phase 2: it is, through the frame).
- The elevator is a reserved shaft with closed slabs (phase 2 builds the lift).
- The hangar has a 3 m opening, not a ramp.
- Most of the hull is spare space, because the programme is small for a 128 m deck.
- The interior stands on the chassis deck's top. The 14 m void inside the chassis grillage has no sockets to build on.

Missing parts (listed by the run, not invented):
- a small-class freight lift (phase 2: the Freight Lift Car on a Linear Actuator);
- a reactor or generator and a power board (phase 2: the Small Genset; a power board is still missing);
- an engineer's panel and a chart table;
- a small-class shell magazine, hangar bay and ramp;
- a conveyor or chute;
- a locker, a gun rack and a medical station;
- a medicine cabinet, a mess table and a galley stove.

## Phase 2 record (branch `feat/interiors-2`)

**The freight lift.** `tools/constructs/interior_parts.py` writes `Constructs/Parts/Interior/FreightLiftCar.json` (the
Freight Lift Car: a 3.2 x 3.5 m tread-plate floor, a yoke with a `mount` socket, side rails at 1 m, a `button` socket and nine
floor sockets, 568 kg, `walk: floor`). Everything else is existing parts. In a shaft at least 3 cells deep through two decks
the builder:
- leaves out the upper deck's slab over the shaft's middle block on its west side (the car's block);
- stands a **Linear Actuator 3x3 4 m** on end on the shaft's west wall (its `base.4` on the wall's face socket at 1.25 m, so its
  foot is on the floor), carriage facing into the shaft, and sets it: low stop 12%, high stop 87% (0.48 m and 3.48 m: a storey
  of 3 m apart), speed 1 m/s; its rating is 60 kN, so car and load up to about 5.5 t;
- hangs the car on the carriage, its floor 2 cm over each deck's floor at the stops;
- wires the calls: a Toggle into the rail's `Set` (Position: 0 down, 1 up); the car's Button into the Toggle; the lower
  landing's Button through an AND with the Toggle (it calls the car down only while it is up), the upper's through an AND with
  a NOT of the Toggle. The gates sit low on the shaft's west wall, the landing buttons at hand height by the landing block.

Riders and loose things ride the car as they ride any deck on a joint (CONSTRUCTS.md "Linear joints"). Other shafts (one
deck, more than two decks, or shallower than 3 cells) are noted and stay closed.

**Power.** The circuit is the frame (CONSTRUCTS.md "Power"): the interior stands on the walker's deck, so the walker's own
Battery, Engineering's three Batteries and its new **Small Genset** (100 kW, with a Fuel Tank fitted to it as its day tank)
reach everything. Doors are passive hinges and need none. Each room with lamps gets a Switch. The run lists what has no power
path: on the Titan Walker, nothing (24 electrical parts on the circuit, 1,700 kJ held, the genset fed).

**Free rooms.** `layout: free` is on for the Bridge and the Mess. A free room's rectangle is placed as a grid room's, sized
for about 60% of its want; its wing is a rectangle in the other band of its strip (inner and outer), along its edge on that
side, flush with its bow or stern end, at least 2 cells wide and narrower than the room, so the outline is an L (a T once a
wing grows past it). Narrow leftovers beside a wing merge into the wing. Doors, sections and minimum sizes go by the
rectangle; walls follow the whole outline; the room's things stand on every cell. Angled walls aren't possible with the
Building pieces (they stand on the 0.25 m socket lattice), so "following the hull" is a stepped outline.

**The run** (`--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1`, written to `TitanWalkerFitted.json`): the
Bridge is 3x3 at 0,8 plus a 2x3 wing at 0,11 (an L at the bow), the Mess 3x3 at 4,3 plus a 4x3 wing at 4,0 (an L, after a
2-cell leftover merged); 12 of 12 rooms, all minimum sizes met, 45 of 45 rooms reached, 2,299 parts placed, none refused or
welded, 51 door leaves; the file loads back as 2,310 parts from 2,310 steps.

**Tried by hand:** the runs above (the build rules took every lift, gate, switch, plant and wall part); the power report on
the built construct (`ConstructPower.Rebuild`); the ASCII maps for the L-shaped rooms.

**Not tried:** the lift moving. A throwaway headless run (the fitted walker built on a flat test floor, ticked, buttons
pressed) hung in `World`'s static constructor on its first tick, which is main's known static-init problem; nothing of the
lift's motion, its load, the call logic in play, riders, or the genset running was seen. Also not tried: drawing it (z-fighting
between the car and the shaft walls, the rail stub 1 m above the upper floor), a player walking the L rooms, other seeds and
blueprints, a client.

**Open questions:** whether a 60 kN rail is enough freight (a Medium rail needs medium sockets); call buttons inside the
landing rather than on the shaft wall; a lift for three or more decks (a stack of rails and a counter instead of a Toggle);
the lamps start off (a Switch per room) rather than on.

## Phase 3 record (branch `feat/interiors-3`)

User decisions (2026-11-10): fill big hulls both ways (a growing room list per class, then generated rooms); the chassis is the
interior; real per-room loot, values listed here for the user to change.

**Loot.** Nine tables in `Constructs/Loot.json` (hand-written; the hooks in `LootHooks.json`). Each has tier 3 and the rarities
common, uncommon and rare. The loader refuses an item it doesn't take, with the reason, and `--gen-interior` prints those lines.

| Hook / table | Rolls | Empty | Items (weight) |
| --- | --- | --- | --- |
| personal / `room personal` (quarters, locker room) | 1-2 | 25% | Lamp 4, Battery 2, Box Magazine 3, Stick Mag 3, Red Dot 2, Copper Coil 2, Wiring Loom 3, Text Sign 1, Key Switch 1, Peashooter Receiver 1 |
| food / `room food` (mess) | 1-3 | 10% | Water Cooler 5, Filter 4, Lamp 3, Small Fuel Tank 3, Pipe Box 2, Battery 1 |
| medical / `room medical` | 1-3 | 10% | Filter 5, Water Cooler 3, Lamp 4, Battery 4, Drone Battery 2, Proximity Sensor 2, Computer 1, Number Readout 2 |
| stores / `room stores` (storage, hangar) | 2-4 | 5% | Gear Set 8, Bearing 6, Tyre Carcass 4, Wiring Loom 6, Copper Coil 5, Wire Stock 5, Piston Set 3, Lamp 4, Battery 3, Small Fuel Tank 3, Fuel Line 3 |
| armoury / `room armoury` | 2-4 | 0% | receivers: Rifle 4, Courier Carbine 4, Farmhand Pump 4, Plinker 3; Gun Barrel 5, Long Gun Barrel 3, Sawn-Off 2, Stub 2; Suppressor 2, Muzzle Brake 3, Flash Hider 3, Red Dot 3, Holographic 2; Extended Mag 3, Box Magazine 5, Stick Mag 4; FMJ Rounds 6, AP Rounds 3 |
| ammunition / `room ammunition` (magazine) | 2-4 | 0% | Ammo Box 8, Ammo Reel 4, Stick Mag 3, Extended Mag 2, FMJ Rounds 6, AP Rounds 4, Box Magazine 4, Impact Fuse 3, Delay Fuse 2, Timer Fuse 2, Napalm Gel 1 |
| engineering / `room engineering` | 2-3 | 5% | Battery 5, Copper Coil 5, Field Coil 3, Wiring Loom 6, Circuit Board 3, Control Unit 2, Fuel Line 4, Small Fuel Tank 3, Piston Set 3, Bearing 4, Gear Set 4, Charge Gauge 2, Fuel Gauge 2 |
| workshop / `room workshop` | 2-4 | 5% | Gear Set 6, Bearing 5, Piston Set 4, Wire Stock 5, Wiring Loom 4, Copper Coil 4, Circuit Board 2, Drill Bit Mk 2 2, Drill Bit Mk 3 1, Small Engine 2, Gun Barrel 2, Bead Rail 2 |
| bridge / `room bridge` | 1-2 | 20% | Computer 3, Fax Machine 2, Radio Homer 2, Number Readout 3, Numpad 2, Code Lock 2, Server Rack 1, Box Magazine 3, Courier Carbine Receiver 1 |

Counts are 1-2 or 1-3 for small things and 1 for receivers and machines. Left out because the tables refused them: Heavy Receiver,
Gun Scope, HE Rounds, Incendiary Rounds and Belt Feed are epic (a tier-4 table with `epic` could take them, for the capital class),
GPS Unit needs a tier 4 Assembler, and Cable Reel is physics-only. No medical or food items exist yet (no medkit, bandage or
ration part), so the medical and food tables hold the nearest things. The bridge and hangar now have a loot Chest. The rooms'
light Switches are built on (`startOn`).

**Room lists by class** (`SizeClasses.json`, provisional):

| Class | Programme | Fill |
| --- | --- | --- |
| Medium | bridge, quarters, storage, mess | storage |
| Large | + production, armoury, medical, workshop | storage, workshop |
| Extra large | bridge, 3 quarters, production, 2 storage, hangar, armoury, medical, mess, engineering, magazine, workshop, locker room | storage, workshop, fuel bunker |
| Capital | bridge, 4 quarters, production, 3 storage, hangar, 2 armouries, medical, mess, engineering, magazine, 2 workshops, locker room | storage, workshop, fuel bunker, quarters |

New room types: **Workshop** (2x2 min, want 9, lower deck; a Press, a Die Lathe, a Die Rack, Parts Shelf and Chest on loot,
a lamp), **Fuel bunker** (2x2, want 6, lower deck, hull side; Fuel Tanks, one per 2 cells), **Locker room** (2x2, want 6, upper;
Chests on loot, seats, a lamp).

**Generated rooms** (`InteriorPlanner.Generate`). A leftover run at least as wide as the spare room's minimum (4 cells) becomes a
fill room when the class has a fill list. It is chosen by score: deck preference +2 (wrong deck -4); row preference +2 (wrong row
-2); each touching room it or that room wants +4, avoids -6; -3 for each of that type already generated on the deck; a seeded
jitter up to 1. A type holding a Fuel Tank stays on the lowest deck. A run of at least twice the type's want and twice its
minimum width is split in half and each half chosen again. A run that fits no type stays spare. Narrow leftovers still merge or
become storage, fuel or void.

**The chassis is the interior: the route.** The Titan Walker's root, the Capital Chassis Deck 128x64 m, is one part: two 1 m
plates with a 14 m hollow between them, cut by 4 cross beams and 2 edge beams. It has no sockets inside, and its top plate has
no opening, so nothing can be built in the hollow or reached from the deck. Phase 3 plans it (`InteriorBuilder.ChassisVoid`):
the hollow inside the edge beams (125 x 14 x 61 m), its inner beams as keep-outs, 4 storeys, the class's `chassis` rooms
(engineering, magazine, two storage, workshop, fuel bunker, production), filled from the class fill list less the crew rooms.
`--gen-interior ... --plan` prints it as "PLANNED ONLY". On seed 1: 7 of 7 rooms placed; 178 of 181 rooms reached (3 storage
rooms against the north edge of the upper void storeys have no door), with 4 stairwells and a lift shaft through all 4 storeys.

To build it, these were needed (all done in phase 4, below):
1. **A hollow chassis set** (a new generator, not `capital_parts.py`): the same deck with floor sockets on the bottom plate's top
   on the small 0.25 m lattice (or capital sockets for the adapter pads), hatch openings in the top plate over the stair and lift
   shafts, and doorways through the cross beams on the spine.
2. The walker session swaps the Titan Walker's root to it (TitanWalker.json and its generator are theirs).
3. The builder builds the void plan first (adapter pads on the inner sockets, 4 storeys), runs its stairs and lift up through
   the hatches into the top interior, and moves engineering and the magazine down (they leave the top programme).
4. A lift for more than two decks (a rail per storey or a long rail with several stops).

**The run** (`--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1`, in `TitanWalkerFitted.json`): 19 of 19 rooms and
50 or so generated rooms on 2 decks; all minimum sizes met; 76 of 76 rooms reached; 3,089 parts placed, none refused or welded;
98 door leaves; 187 loot containers; the file loads back as 3,100 parts from 3,100 steps; power: 138 electrical parts on the
circuit, none off. Planning took 0.2 s and building 9 s.

**Tried by hand (headless, after main's World start-up fix):** the fitted walker built on a flat test floor and ticked
(a throwaway run, not kept). The lift spawns part-way up and settles at the low stop within 5 s. The car's button sends it up
3.00 m, the lower landing's call brings it down 3.00 m, and the upper landing's call sends it up 3.00 m. All 46 lamps are
powered and lit, and all 45 switches are on.

**Not tried:** drawing anything; a player walking it, riding the lift or opening the loot (the loot rolls weren't checked in
containers); the walker walking with the added mass (not measured this phase; the legs have a load limit); other seeds
and blueprints; the smaller classes; a client.

**Note for Assembling.json:** phase 3 added no recipes.

## Phase 4 record (branch `feat/interiors-4`)

User decision (2026-11-10): the chassis is the interior. The parts that make up a giant's chassis can be interior parts, and a
heavily armoured walker gets an armoured hull with the interior inside it.

**The hollow chassis set** (`tools/constructs/interior_parts.py`, `Constructs/Parts/Interior/Capital`). There are two parts.
- **Capital Hollow Chassis 128x64 m**: the Capital Chassis Deck 128x64 m's outside. It has the same footprint and 16 m depth. Its
  44 under, top and side sockets have the same names and places, so the legs' `-z0`... `+z7` mounts and the deck's `top` sockets
  carry over. It also has the same mass (28,160 t), health and armour (80). The hollow is built for the interior.
  - The end and edge beams stand just outside the interior's 4 m grid (the one `Measure` lays on the top sockets). The two inner
    cross beams each fill one grid column (11 and 19), with a full-height portal where the 8 m corridor runs through.
  - The top plate has hatches over the planner's two stairwells and the lift shaft (grid 7,3 and 22,3, 1x3; 16,3, 2x3). The
    hatches are 5 cm larger than the cells.
  - It has 12 capital `floor{storey}.{bay}` sockets, one per bay on each of the 4 storeys. Each is in the middle of a corridor
    block. The upper ones sit on small brackets just under the slab, because a socket must sit on a solid face.
- **Capital Armoured Hull 128x64 m**: the same frame with belt armour drawn on the long sides. It has 3.5x the plate (39,424 t)
  and armour 160.

Both are opted out of centre sockets. They have recipes in Assembling.json (added by hand). "Centre sockets" names neither: its 24
failures are main's leg weld sockets.

**Building the void.** A root with floor sockets inside its hollow gets the void built (`InteriorBuilder.VoidHull`):
- The void is laid on the deck's own grid, with the same corner and size, so the shafts land under the hatches. Its storeys are
  levels -5 to -2 (the deck's deck 0 is level 0).
- The class's `chassis` rooms go below and leave the deck's programme. The void plan is **sealed**: it has no outside doors.
  From the second pass, an unreached room gets a door through a bulkhead line. In a sealed plan it can also get one onto a shaft's
  landing. Rooms are kept whole (no split, runs up to 3 cells merge) to keep the wall count down.
- A **mezzanine** (level -1) is built only in the shafts that have the deck's shafts over them, up through the hatches.
- Stair flights alternate between a stairwell's first two blocks, so no flight stands over the one below. The void's stairs run
  through the mezzanine into the deck's stairwells.
- The 3 doorless storage rooms of phase 3 are gone. The void's rooms are now 108 of 108 reached.

**The lift for 3+ decks: stacked cars**, one car a storey. Every gap from level -5 to 1 has its own 4 m rail and car. The cars
alternate between the shaft's middle block and its first block, and the slab is left out over each car. Each car has the phase 2
calls: its own button, plus a lower and an upper landing button. Riders change cars at each landing. Corners over a car's block
are fitted to the walls beside them.

**Limits met.** A construct holds at most 255 joints and 48,000 socket links (`Construct.MaxJoints`, `MaxLinks` = 3x
`MaxParts`).
- **Joints:** in the void, only critical rooms and bulkheads get a door leaf first. The rest get one last, while 32 joints stay
  free (9 stay open).
- **Links:** the void has half the things per cell and no slab or wall in its dead space. **The fitted walker uses 46,883 of
  48,000 links**, so players can add only about 200 parts.

**The run** (`--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1 --chassis hollow`, in `TitanWalkerFitted.json`;
its root is the hollow chassis):
- Rooms: the deck has 13 of 13 programme rooms and 77 of 77 reached. The void has 7 of 7 and 108 of 108 reached.
- Parts: 7,350 placed, none refused or welded. There are 217 door leaves, 488 loot containers and 12 stair flights.
- Lift: 6 of 6 cars.
- Power: 304 electrical parts, none off.
- The file loads back as 7,361 parts from 7,361 steps. Building took 37 s.

**Tried by hand (headless, a throwaway run, not kept):**
- **Build:** it loads as 7,361 parts, 223 joints and 46,883 links. It weighs 37,705 t, against 32,698 t for phase 3's fitted
  walker and 29,289 t bare.
- **Lift:** all 6 cars went up 2.98 m on their car buttons, down on the lower landings' calls, and up again on the upper
  landings'.
- **Power:** all 304 electrical parts are on, and 82 of 82 lamps are lit.
- **Walk:** forward from rest for 6 s, with no wrecks. Over the last 3 s the bare walker made 0.23 m/s, phase 3's fitted walker
  0.20 m/s and the hollow-fitted walker 0.09 m/s. All three crept and sank in this crude run (the bare one is also tilted 15
  degrees), so read the numbers as relative.

**Not tried:** a player walking the rooms, stairs, portals and mezzanine; riding and changing cars; drawing anything
(z-fighting between the stair flights and the stair-floor openings, the hatch edges and the deck's shaft walls); other seeds;
the armoured hull fitted; a client.

**Known gaps:**
- Landing buttons end up at the wall's foot on half the landings (Mount takes the nearest free socket). The phase 2 lift does
  this too. (Fixed in phase 5.)
- The car-to-car change is by hand: there is no single car through all storeys (a small rail is 4 m at most).
- The gunner's station is now more than 8 m from its Fire Director. (Fixed in phase 5.)
- The link limit leaves little room. (Phase 5: 50%.)

**For the walker session:** to swap the base, change the Titan Walker's step 0 to `Capital Hollow Chassis 128x64 m` (or
`Capital Armoured Hull 128x64 m`). The socket names, leg mounts and top sockets are unchanged, so nothing else moves. Then
re-run `--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1` (the `--chassis` flag is then not needed).

## Phase 5 record (branch `feat/interiors-5`)

User decision (2026-11-10): make interiors leaner now; a higher `Construct.MaxLinks` comes later with the 500 m scale work.

**Where the links went** (phase 4's fitted walker, 46,883 links): walls 24,478 (2,528 pieces) and slabs 18,629 (2,838). About
8,500 were wall faces linking to the wall back to back with them, two rooms' walls along every shared edge. A 4 m wall made ten
links: 4 to the slab under it, 4 to the slab over it, 3 to its neighbour.

**What changed:**
- **Shared walls.** One wall stands between two rooms (`InteriorBuilder.Shares`). A doorway in a shared wall, with its leaf, is built by
  the room that builds the wall.
- **Steel Panel Wall 4, 8, 12 and 16 m** (`tools/constructs/interior_parts.py`, `Constructs/Parts/Interior/Walls`, recipes in
  Assembling.json by hand): the Steel Wall 4 m's plate and section, with one bottom and one top socket at each end and one end socket
  a side; face sockets every 2 m for players. Plain runs take the longest that fits; shafts keep 4 m pieces (the lift hangs on them).
  Slabs are unchanged (4x4 m): a long floor is the next cut if one is needed (slabs now hold 14,244 links).
- **Budget** (`SizeClasses.json` `budget`, `Budget` in InteriorData.cs): the most parts, links and joints the whole fitted construct
  may use. Capital: 9,600 parts, 28,800 links (60%), 191 joints, so players keep 6,400 parts, 19,200 links and 64 joints. The
  other classes (provisional): medium 2,000 / 6,000 / 48, large 4,000 / 12,000 / 96, extra large 8,000 / 24,000 / 160. Leaves other than
  critical rooms' and bulkheads' go on last, deck first; generated rooms are furnished last. Whatever does not fit is left out and
  reported ("doorways left open", "generated rooms left bare"). Walls and slabs are never trimmed. Tried with the capital link budget
  set to 23,000: it stopped at 23,000 links with 114 generated rooms bare and 68 doorways open.
- The void's furniture is back to full density (phase 4 halved it).
- Landing buttons are fitted first, on face sockets 1 to 1.8 m over their floor; the gates then go on what is left.
- The gunner's station and the Fire Director stand side by side (the second goes on the free spot nearest the first): wired.

**The run** (`--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1 --chassis hollow`, in `TitanWalkerFitted.json`): 13 of 13
deck rooms, 77 of 77 reached; void 7 of 7, 108 of 108 reached; 6,576 parts placed, none refused or welded; 185 door leaves (41
doorways open); 707 loot containers; 6 of 6 lift cars; the file loads back as 6,587 parts. Building took 52 s.

| | Phase 4 | Phase 5 |
| --- | --- | --- |
| Links | 46,883 (98%) | 24,162 (50%) |
| Parts | 7,361 | 6,587 |
| Joints | 223 (32 free) | 191 (64 free) |
| Walls | 2,528 pieces, 24,478 links | 1,545 pieces, 6,957 links |
| Loot containers | 488 | 707 |
| Mass | 37,705 t | 36,557 t |

**Tried by hand (headless, a throwaway run, not kept):**
- **Build:** loads as 6,587 parts, 191 joints, 24,162 links, 139 wires.
- **Walls:** every outline edge of every room checked for a wall or doorway at four points along it: none missing on any level
  (the check found the gaps when a corner's 2 m piece was left out on purpose).
- **Lift:** both landing buttons of every car stand 1.38 m over their floor. All 6 cars went up 2.99 m on their car buttons, came down
  on the lower landings' calls and went up on the upper landings'.
- **Power:** 304 of 304 electrical parts on, 82 of 82 lamps lit.
- **Walk and mass:** the walk controller's own top speed (`WalkTop`, from mass over what the legs carry) is 3.59 m/s bare (29,289 t),
  3.48 phase 3 (32,698 t), 3.32 phase 4 (37,705 t) and 3.36 now (36,557 t). Every walker uses half or less of what its legs carry
  (72,000 t), so mass slows it by at most 7%. **The 0.09 against 0.23 m/s of phase 4's crude run came from the run (all sank and
  crept), not the mass.** The walk itself was not run again.

**Not tried:** drawing anything (the panels' ends meet as the 4 m pieces do); a player walking it; damage to a panel (fewer links: a
hit panel may come off sooner); other seeds; the armoured hull; the smaller classes' budgets on a real build; a client.

**Known gaps:** 41 doorways have no leaf (the joint budget); slabs are now the main link cost; the helm still can't be wired to the
Driving Module (one input).

## Phase 6 record (branch `feat/interiors-6`)

User decision (2026-11-10): a walk-through and look check of the fitted Titan Walker.

**Regenerated on the walker's current blueprint** (`--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1`, no `--chassis`:
its root is the hollow chassis, its helm on the deck, and it has the Capital Battery Bank).
- **The bridge takes over the walker's helm** (`InteriorBuilder.HelmChain`, `CarryHelm`). The blueprint's helm chain is a seat wired
  into a Driving Module, plus what hangs on it (the module and its Battery). It is left out of the replay, along with the adapter it
  stood on. The bridge's helm item becomes that helm: it faces the way the old one did, it stands on the bridge's free spot nearest the
  leg the module is wired to (a wire reaches 8 m), and the module, the Battery and the wires are fitted back on it. It counts as the
  vehicle's in the budget. There is no second helm. It is the drive seat.
- **Budget:** 6,462 of 9,600 parts, 23,619 of 28,800 links (49% of the engine's), 191 of 191 joints, 137 wires, 38,228 t. All 13 + 7
  programme rooms are placed and all rooms are reached. None refused or welded. 185 door leaves, 33 doorways open, 700 loot
  containers. It loads back as 6,462 parts.
- **The walker's "Centre sockets" self-test section passes** (0 failures).

**The walk-through** (`--interior-walk`). The fitted walker is built frozen 60 m over the Proving Ground. A player uses the server's own
movement (`World.Simulate`, `ConstructWorld.BlockPlayer`, the ride frame) and is steered to points:
- every doorway both ways, 1.6 m out each side: 452 crossings on levels -5 to 1;
- every corridor end to end through its bulkheads, on 6 levels;
- all 10 stair flights, up and down;
- each of the 6 lift cars, ridden up on its button, then walked off at the landing;
- a shut leaf, to check it stops a player.

The leaves are opened through `ConstructEdits.Use`, and all 185 open. Result after the fixes: **nothing sticks, falls or is pushed.**

Then the walker is set on the ground and driven forward for 4 s from the carried helm. It stands with its deck 30 m up on the bank,
and walks 11.6 m (2.9 m/s from rest). The worst tick is 8 to 10 ms (27 to 93 ms while it settles). A rider in the Bridge and one on a
lift car stay on their frames with 0.00 m drift.

**Found and fixed (builder and planner):**
- **27 of 185 door leaves hung flat**, as a waist-high shelf across the doorway. Left to `SmartRotation.BestSpin`, the leaf took a
  spin that laid it down. Opened, it pushed a player standing beside it 3 m through the floor (see "For other owners"). `Hang` now
  takes the first spin that stands the leaf's tallest side up.
- **A bulkhead door opened onto a keep-out.** The walker's Capital Battery Bank stands across the spine at grid x 19 to 23 on both
  decks, so the x 24 bulkhead's door led onto the bank (deck 0) or onto a 2.2 m drop with no slab (deck 1). Where a keep-out is beside
  the bulkhead on the spine, the builder now builds a plain wall.
- **Lift doorways onto a car's rail.** The cars ride the shaft's west column, on its north blocks. The void storage room's door in the
  shaft's north wall opened onto the parked car's side rail. A lift shaft's north and south doorways now go in their piece's east
  half. The planner never puts a landing door in a lift shaft's west wall, and prefers stairwells.
- **Furniture in front of doorways.** A Bunk stood 1.6 m inside a door, and a player walking in landed on it. A room's things now keep
  their footprint plus 1.6 m from any doorway's middle on their floor (`Blocks`).

**Look check:** the "No z-fighting" self-test section passes (the panels, the hollow chassis, the armoured hull and the lift car are
among the part files it checks). `interior_parts.py` rewrites its 7 parts unchanged. The built interior was checked with
`--check-vehicle` (`VehicleChecks.ZFighting`): **0 coplanar faces between parts among 19,201 boxes** (555 slivers under its area
limit). No screenshots were taken.

**Damage** (`--interior-walk --damage`, 60 a round):
- Shot until they broke, a 16 m panel took 566 rounds and a 12 m panel 425 (their health is 14,140 and 10,620, the same per metre as a
  4 m wall). A slab took 90 rounds. Each came off alone, as one wreck, and nothing else was cut off.
- Blasts of 4 m radius: at 3,000 and 8,000, a 16 m panel held while 4 m walls broke (8,000 took 10 wall parts and a door). At 16,000 it
  broke: a 16 m breach plus two slabs.
- Every breach stayed in its bulkhead section. No room, and so no panel, crosses a bulkhead line.
- A 3,000 blast on a lift shaft's wall took a lift car and its call logic (15 parts).

**For other owners (not fixed here):**
- **Walker (`leg_parts.py` `blueprint()`):** the Capital Battery Bank on `top5.1` covers the chassis's stair hatch at grid 22,3. The
  deck's second stairwell moves to 24,3, off the hatch, so the void's east stair stack stops at the top storey (10 flights, not
  12). The bank also cuts the deck's spine on both decks, though deck 1 east of it is still reached by its stairs. A top socket off
  the spine and the hatches would fix both.
- **Construct movement (`ConstructWorld.BlockPlayer`):** a player overlapping a joint-moved part (a door leaf swinging into them) is
  pushed out vertically, through a 0.25 m slab, 2.6 to 3.7 m up or down. It is seen only with the flat leaves, now gone, but any hinged
  panel can do it.
- **Blast balance:** blast damage is per part, so a long panel is about 4 times as hard to blast through as 4 m walls, and then opens a
  4 times wider breach. This is a design question for the panels or the blast code.
- **Freight Lift Car (mine, not changed):** its rails are on the north and south sides and it is open at the east end. Boarding is
  through the shaft's east column.

**Tight but working:** the stair flights in a stairwell's first block start 0.875 m from its end wall. The player is 0.8 m wide and
can step on from the side.

**Not tried:** a real client; drawing; doors opened by a player's aim rather than `Use` from a computed eye; other seeds; the armoured
hull fitted; the medium to extra-large classes on a real build; walking into every room's far corners (only doorways, corridors,
stairs and lifts were walked).

## Phase 7 record (branch `feat/interiors-7`)

User decision (2026-11-10): the builder switches to event doors (docs/sandbox/CONSTRUCTS.md "Event doors"), every doorway gets one, and
the bulkheads seal.

**What changed:**
- **Event doors, no joints** (`InteriorBuilder.Hang`, `Upright`). Each door goes straight on its frame's jamb socket, standing upright:
  - a **Bulkhead Door 1.5x2.25 m** (sliding) on the spine's bulkheads, on critical rooms' doorways and on lift landings. It hangs on the
    jamb on the side of the 2 m wall piece built beside the door wall, so it slides into that wall. A landing's door slides west, away from
    the car's column.
  - a **Steel Double Door L/R** pair where a hangar or the mess is on either side. The leaves meet and link, so F on one works both.
  - a **Steel Swing Door 1.5x2.25 m** everywhere else and on every Door Corner. A corner has one jamb, so it never takes a sliding or
    double door, even on a critical room.
  - Garage Wall openings (the hangar's ramps, the lane's bulkhead openings) stay open: no event door is 3.75 m wide.
- **Budget** (`SizeClasses.json`, `Budget.Doors`): a `doors` budget per class (door parts: a double door is two; at most
  `Construct.MaxDoors`, 1,024). Medium 96, large 192, extra large 384, capital 512. Joints, now only the lift cars', drop to 8, 16, 24 and
  32, so the capital class leaves the players **223 of 255 joints** (6 used) and 288 more doors.
- **Bulkhead seals** (`InteriorBuilder.Seals`). For each bulkhead line (grid x), a Switch goes beside the bridge's lights switch, with a NOT
  Gate on it. The Switch is wired into the Lock port of each of the line's bulkhead doors, on every level, and the NOT Gate into their
  Open port. Switch on: the NOT's output falls, so the doors shut, and they lock (F refused, "it's locked"). Switch off: they open and
  unlock. The wires are laid straight through the decks, with nodes at most 6 m apart. A line that would leave the players fewer than 64
  of the 254 wires a construct holds is left unwired and noted.
- **Fixed in the event door code (`ConstructDoors.cs`, small):** every door that settled refreshed the whole body's shape on its own.
  On the 6,322-part fitted walker, 6 bulkhead doors sealing in one tick took a 16.5 ms tick, and all 224 doors 435 ms. Now the doors
  that settle in a tick refresh the body once (`Settle(..., refresh: false)` then `Refresh`), on the server's step and on a client's
  events. Six doors: 7 to 8 ms; all 224: 65 to 81 ms.

**The run** (`--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1`, `TitanWalkerFitted.json`): 220 doorways doored (190 swing,
26 sliding, 4 double), **0 left open**; 3 bulkhead lines, 16 of 16 bulkhead doors wired; none refused or welded; 700 loot containers;
loads back as 6,322 parts.

| | Phase 6 | Phase 7 |
| --- | --- | --- |
| Parts | 6,462 | 6,322 |
| Links | 23,619 | 23,485 |
| Joints | 191 (185 door hinges, 6 lifts) | 6 (lifts) |
| Wires (routes) | 137 | 172 (35 for the seals) of 254 |
| Doors | 185 leaves, 35 doorways open | 224 door parts in 220 doorways, 0 open |
| Mass | 38,228 t | 38,234 t |

**The walk-through** (`--interior-walk`, then `--interior-walk --walk 4`): every one of the 224 door parts opened on Use (a double door's
partner opens with it). Every doorway was crossed both ways, 448 crossings, with nothing stuck or pushed. Every corridor, stair flight
and lift car passed. A shut swing door stopped the player. The bulkhead line's Switch on the bridge shut and locked all 6 of its doors:
F on one was refused ("it's locked") and the player was stopped. Switched off, all 6 opened and unlocked. The walker still stands (deck
29.2 m up) and walks 11.6 m in 4 s (2.9 m/s) from the bridge's helm. A rider in the Bridge and one on a lift car stay with 0.00 m drift.

**For other owners (not fixed here):**
- **Event doors (`ConstructDoors.cs`):** one door settling on the fitted walker still makes an 8 to 10 ms tick, over the 8 ms rule. A body
  refresh costs 2 ms, and the next tick's rebuild of the part tree costs the rest. Every door opening at once costs 65 to 81 ms, about 0.3
  ms a door beyond the one refresh. A cheaper settle, one that moves the leaf's boxes in the tree rather than rebuilding it, would fix both. (Fixed on `fix/door-settle-cost`: CONSTRUCTS.md "Event doors", "Settling".)
- **Walker:** the battery bank on `top5.1` still covers a chassis stair hatch (phase 6).

**Not tried:** a client (drawing a moving leaf, the wire events); a swing door's leaf against furniture when open (the leaf is not checked
against what it sweeps into); other seeds; the smaller classes' door budgets on a real build; a player using the seal switches by aim.

### Fitted walker

The Titan Walker with this interior, driven from the bridge with riders, doors and lifts in use, and its tick time, are recorded in
docs/sandbox/GIANTS.md, "Fitted walker". In short: worst ticks 1 to 3 ms after the press fix (a Press or Die Lathe scanned every link
of the construct to find its die and pieces), riders, doors and lifts work while it walks, and stabilisers have no room yet.

## Phase 8 record (branch `feat/interior-rooms`)

User decisions that apply (2026-11-10): rooms are fully working (real machines, storage, power), each room type keeps its loot, the
contents include armouries, production, storage, crew quarters, weapon stations, medical and logistics; critical rooms are weak points.

**Parts** (`tools/constructs/interior_parts.py`, `Constructs/Parts/Interior/Furniture`, small class, group Interior, recipes by
`assembling.py`'s rules): Locker, Gun Rack, Medicine Cabinet, Tool Rack, Mess Table, Chart Table, Galley Stove and Repair Station are
stores (storage slots: loot and players' things); the Power Board (engineering) and the Engineer's Panel (bridge) read the circuit's
charge as the Charge Gauge does; the **Medical Station** is a seat that heals whoever sits in it, 4 health a second
(`"seat": {"heal": 4}`, `ConstructPartDef.SeatHeal`, the server's seat loop: `Game.SandboxSeatHeal`; health goes out in the snapshot as
before, no wire change). Inventory: Storage and Logistics, Seats and Logic (Categories.json).

**Gaps listed by the run** (furniture placed, behaviour not this phase): the repair station doesn't mend parts in place; the galley stove
doesn't cook (no food items); the engineer's panel has no damage board; the medicine cabinet holds the nearest things (no medkit or
bandage); the hangar's ramp (a medium floor and hinge). A fuel pump is not needed: a pipe from a tank feeds a plant directly.

**Class lookup.** A module item's `part` may be a list, biggest size class first; `SizeClasses.json` `partClass` is the biggest a class's
rooms may hold (medium and large: small; extra large and capital: medium). The builder takes the first that is allowed, has an adapter
(`shell.adapters`) and fits a block (2.5 m high, 3.6 m across): a bigger part stands on its adapter pad turned face down on the slab. The
capital Hangar now has a Hangar Bay Medium and the Magazine a Shell Magazine Medium. An item with no part that fits is listed once.

**Feeds** (`ModuleSets.json` `feeds`, `InteriorBuilder.Feeds`): a chute from the nearest storage room's Materials Bin into each
production room's Assembler ("Chute in"), and a pipe from the nearest fuel bunker's or fuel store's Fuel Tank ("Fuel out") into each
engineering room's Small Genset ("Fuel in"), laid at ceiling height (x, then z, then down; a node every 7.5 m at most; laid, so through
walls and slabs). A construct holds 254 links (routes) in all: the feeds go before generated rooms' light switches, and both stop where
the players would keep fewer than 64.

**Room mix.** `fill` is a weighted programme (`FillEntry`: weight, cap on the share, where deck, chassis or any, perQuarters). Narrow
leftovers (2 or 3 cells) draw from it too, where a type fits; storage only when none does. The score adds 20 x (the type's target share
less its share so far), and a mess or medical bay is wanted per 4 quarters; over its cap a type is skipped unless every type is. Critical
rooms (engineering, magazine) are never generated in a sealed hull or in an inner band (their one door would trap the rooms behind them).
New room type **Gun station** (outer row: a Crew Station and Fire Director wired, a Gun Rack, ammunition loot); magazines want gun
stations and avoid fuel.

**Floor panels** (`Steel Floor Panel 8x8 m` and `16x16 m`, `Constructs/Parts/Interior/Floors`, `shell.floorPanels`): the 8 m panel has
every slab socket; the 16 m one (255 sockets at most) keeps tops on its 4 m blocks' edge lines, bottoms on its rim and edge sockets at
each block's ends. `Slabs` lays the biggest panel first on its own lattice, inside one bulkhead section, over blocks that all take a plain
slab; a panel that finds nothing to stand on when the passes stop is broken into the next size down, at last 4x4 m slabs. Furniture takes
only its block's sockets of a panel.

**The runs** (`--gen-interior Constructs/Blueprints/TitanWalker.json --seed N`, seeds 1 to 3, on main's walker after the merge): every
programme room placed, every room reached on deck and in the void, 0 refused on seeds 1 and 2 (seed 3: one Repair Station with no room
left); 38 to 39 feeds laid; 3 seal lines, 18 of 18 bulkhead doors wired; power 0 electrical parts off, every plant fed.

| | Before (main, seed 1) | Seed 1 | Seed 2 | Seed 3 |
| --- | --- | --- | --- | --- |
| Parts | 6,322 | 4,248 | 4,171 | 4,219 |
| Links | 23,485 | 16,020 | 15,769 | 16,074 |
| Slabs | 2,760 parts, 13,814 links | 651, 5,932 | 651, 5,905 | 651, 5,962 |
| Storage / Workshop / Production | 79 / 45 / 1 | 36 / 31 / 36 | 36 / 30 / 36 | 36 / 31 / 36 |
| Engineering / Magazine / Gun station | 1 / 1 / 0 | 2 / 1 / 2 | 3 / 1 / 4 | 2 / 1 / 4 |
| Medical / Mess / Quarters | 1 / 1 / 16 | 4 / 6 / 18 | 4 / 7 / 16 | 5 / 7 / 17 |

A deck over a keep-out now loses those cells too (main's walker put crew quarters' walls over its battery bank, with no slab under
them). Seeds differ in layout as well as counts: about 126 of the 186 lines of the deck maps differ between any two. A 500 m hull: slabs at
a 16 m panel a 256 m2 square are about 6 links a panel, so a 500 x 60 m deck is roughly 120 panels a storey.

**Build time.** 69 s before; 20 to 22 s now. Furnishing tried 21,000 placements to fit 1,300 things
(each refusal costs about a millisecond: the build rules' overlap test and an exception); it now skips spots where a solid box of the
room already stands and spots an item has already failed on. What is left is the kit's `C.Rebuild()` after every placed part
(`BlueprintScript.Commit`), quadratic in parts: a runtime spawn of a fitted giant would want a batch mode (rebuild once at the end) in
the kit, and the generation spread over ticks as `VehicleGenJob.Yield` already allows.

**Walk-through** (`--interior-walk` on seed 1): every door opened, every doorway, corridor, stair flight and lift car passed; the seal
switch shut and locked its line's 6 doors. (The first walk found the seals unwired: the feeds had spent the links first. The seals now
go first.)

**Not tried:** a player using the new furniture (sitting in a medical station, opening a locker), the chutes and pipes carrying in play,
drawing anything (z-fighting between floor panels and the 4x4 m slabs beside them, the adapter pad face down on a slab), the smaller
classes, a client.

**Known gaps:** about 60 generated rooms per seed keep their lamps dark: the 254-link cap is spent on the lift, the seals, the feeds
and the other rooms' switches, in that order (a lamp lights only from a wire). One magazine per seed: a critical room is generated only
in an outer band of the open deck, and the magazine prefers the inner row. Lockers are many (130 on seed 1: two per quarters' cells).

## Phase 9 record (branch `feat/interior-halls`)

User feedback (2026-11-10): the rooms were all small and people-sized; big hulls want hangars and industrial spaces (a carrier's
hangar deck, a cargo ship's engine room), decoration in every room, and layouts that are not mirrored.

**Step 0, the drive seat (regression).** After the walker's plant and bank moved (feat/walker-fixes) and phase 8's programme, the bridge
stood far from the one leg the blueprint's Driving Module was wired to, and the module went unwired: the fitted walker had no drive seat.
A Driving Module into any leg drives the walker, so the carried helm now stands on the bridge's spot nearest any base part of the wired
name and wires the nearest (`InteriorBuilder.Twins`). `--gen-interior` builds the base and the written file and fails loudly
(`drive: FAIL ...`, a non-zero exit) when the base has a drive seat and the fitted one has none; it did fail once in development, when the
halls pushed the bridge amidships. The halls now keep off the bow section on the open deck (the crew's: the bridge stays by the legs).

**Halls** (`SizeClasses.json` `halls`, `InteriorPlanner.Halls`, `InteriorBuilder.HallFloors`, `Railings`):
- A hall is a room through several storeys: `{"room", "storeys", "share" (of the hull's length), "minW", "where" (deck, chassis, any)}`.
  They are planned first, after the shafts: the longest that fits up to the share (at most two bulkhead sections), across a whole strip
  from the hull side to the spine (or the strip's outer band where the shafts stand), each on the side with less hall so far. A hall of a
  programme room takes its place in the programme. Its floor room is on the lowest storey (module set `<room>Hall` when there is one);
  each storey above is a **gallery** room over the same cells, out to the spine on that deck.
- A gallery's floor is laid only under the walls it builds itself (its hull side, its corridor side, an end it doesn't share) and down
  the hall's west column; the rest is open, so the hall is one volume (6 m, 9 m or 12 m). Its edge over the hall is a waist-high
  railing (`shell.railing`: Steel Half Wall 4 m, 2 m pieces at inner corners). A flight of stairs a storey climbs the west column, two
  blocks apart, each through a Stair Floor (welded: no socket of a slab or a panel wall meets a flight away from a stairwell's 4 m walls).
- A hall's corridor door is an open bay (a Garage Wall opening, no leaf), except a critical one's. Rooms behind a hall may open off it.
- Capital: on the deck a hangar (30%), a production hall (25%), a store hall (20%) and a workshop hall (20%), 2 storeys; in the hollow
  chassis an engine hall (engineering, 3 storeys), a cargo hold (storage, 4 storeys, 12 m), a second hold (3), a production hall,
  a workshop hall and a fuel hall. Extra large: a hangar and a production hall. `smallShare` is the target most of the room floor small
  rooms may take (0.5 for extra large and capital), reported per plan.
- New module sets: `hangarHall` (Hangar Bay Medium, repair station, tool racks, crate stacks), `engineeringHall` (Medium Genset or Small,
  batteries, power board, hydraulic pump, fluid tanks; fed by a pipe), `storageHall`, `productionHall` (two Assemblers fed by chutes, press,
  die lathe), `gallery` (crates, a locker, a tool rack, a lamp).

**Asymmetry.** Halls go on the side with less hall floor, at different lengths each side, so port and starboard differ. The report has a
symmetry score: the share of room cells whose mirror cell across the centreline holds a room of the same kind.

**Seed 1, deck 1 (the upper deck of the open interior), before (`--no-halls`) and after** (capital letters on deck 1 are galleries over
deck 0's halls; `#` the walker's plant and bank):

```
Deck 1:
                                                               
   ttt uuu LLLLLLL vvvvvvv+www+yyy zzz+000+111+222 33333 ##### 
   t t u u L L L L v v v v w w y y z z 0 0 1 1 2 2 3 3 3 # # # 
   ttt uuu LLLLLLL vvvvvvv www yyy zzz 000 111 222 33333 ##### 
   t t u u L L L L v v v v w w y y z z 0 0 1 1 2 2 3 3 3 # # # 
   ttt uuu LLLLLLL vvvvvvv www yyy zzz 000 111 222 33333 ##### 
   +   +   L L L   +                   +   +       +     # # # 
   FFFFFFF+LLLLL S mmm+nnn+ooo+ppp EEE qqq+rrr S x sssss ##### 
   F F F F L L L S m m n n o o p p E E q q r r S x s s s       
   FFFFFFF LLLLL S mmm nnn ooo ppp EEE qqq rrr S x sssss x x x 
   F F F F L L L S m m n n o o p p E E q q r r S x s s s       
   FFFFFFF LLLLL S mmm nnn ooo ppp EEE qqq rrr S x sssss x x x 
     +     +     + +   +   +   +   +   +   +   +   +           
   ...............|...............|...............|........... 
   . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . 
   ...............|...............|...............|........... 
   +       +         +       +       +     +   +   +           
   AAAAA CCCCCCCCC GGGGGGG+MMMMMMM 4444444+555 666 777 x x x x 
   A A A C C C C C G G G G M M M M 4 4 4 4 5 5 6 6 7 7         
   AAAAA CCCCCCCCC GGGGGGG MMMMMMM 4444444 555 666 777 x x x x 
   A A A C C C C C G G G G M M M M 4 4 4 4 5 5 6 6 7 7         
   AAAAA CCCCCCCCC GGGGGGG MMMMMMM 4444444 555 666 777 ####### 
   A A     +       +   +     +     +   +   +   +       # # # # 
   AAA DDDDDDDDDDD 888 999+AAAAAAA BBB CCC+DDDDDDD ########### 
   A A D D D D D D 8 8 9 9 A A A A B B C C D D D D # # # # # # 
   AAA DDDDDDDDDDD 888 999 AAAAAAA BBB CCC DDDDDDD ########### 
   A A D D D D D D 8 8 9 9 A A A A B B C C D D D D # # # # # # 
   AAA DDDDDDDDDDD 888 999 AAAAAAA BBB CCC DDDDDDD ########### 
                                                               

Deck 0:
```

```
Deck 1:
                                                               
   NNNNNNN RRRRRRR BBBBBBBBBBBBBBB kkk+IIIIIIIIIII lllll ##### 
   N N N N R R R R B B B B B B B B k k I I I I I I l l l # # # 
   NNNNNNN RRRRRRR BBBBBBBBBBBBBBB kkk IIIIIIIIIII lllll ##### 
   N N N N R R R R B B B B B B B B k k I I I I I I l l l # # # 
   NNNNNNN RRRRRRR BBBBBBBBBBBBBBB kkk IIIIIIIIIII lllll ##### 
     +     R R R   B B B B B B B B                 +     # # # 
   MMMMMMM+RRRRR S BBBBBBBBBBBBBBB EEE TTTTTTT S x jjjjj ##### 
   M M M M R R R S B B B B B B B B E E T T T T S x j j j       
   MMMMMMM RRRRR S BBBBBBBBBBBBBBB EEE TTTTTTT S x jjjjj x x x 
   M M M M R R R S B B B B B B B B E E T T T T S x j j j       
   MMMMMMM RRRRR S BBBBBBBBBBBBBBB EEE TTTTTTT S x jjjjj x x x 
     +     +     +       +         +     +     +   +           
   ...............|...............|...............|........... 
   . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . 
   ...............|...............|...............|........... 
   +       +           +             +         +   +           
   JJJJJ KKKKKKKKK GGGGGGGGGGG DDDDDDDDDDDDDDD mmm nnn x x x x 
   J J J K K K K K G G G G G G D D D D D D D D m m n n         
   JJJJJ KKKKKKKKK GGGGGGGGGGG DDDDDDDDDDDDDDD mmm nnn x x x x 
   J J J K K K K K G G G G G G D D D D D D D D m m n n         
   JJJJJ KKKKKKKKK GGGGGGGGGGG DDDDDDDDDDDDDDD mmm nnn ####### 
   J J     +       G G G G G G D D D D D D D D +       # # # # 
   JJJ LLLLLLLLLLL GGGGGGGGGGG DDDDDDDDDDDDDDD ooo ########### 
   J J L L L L L L G G G G G G D D D D D D D D o o # # # # # # 
   JJJ LLLLLLLLLLL GGGGGGGGGGG DDDDDDDDDDDDDDD ooo ########### 
   J J L L L L L L G G G G G G D D D D D D D D o o # # # # # # 
   JJJ LLLLLLLLLLL GGGGGGGGGGG DDDDDDDDDDDDDDD ooo ########### 
                                                               

Deck 0:
```

| Seed 1 | Before | After |
| --- | --- | --- |
| Halls (deck) | none | Hangar 32x24x6 m, Production 32x20x6 m, Store 24x20x6 m, Workshop 24x12x6 m |
| Halls (chassis) | none | Engine hall 32x24x9 m, Cargo hold 28x24x12 m, hold 28x24x9 m, Production 32x24x6 m, Workshop 24x24x6 m, Fuel 16x24x6 m |
| Small-room share of the floor (deck / chassis) | 100% / 100% | 49% / 48% (seeds 2, 3: 49/48, 47/47) |
| Symmetry (deck / chassis) | 9% / 25% | 6% / 15% (seeds 2, 3: 7/14, 10/18) |
| Rooms (deck / chassis) | 68 / 108 | 44 / 87 |
| Parts / links / joints / wires | 4,247 / 16,019 / 6 / 152 | 3,935 / 15,833 / 20 / 162 |
| Slabs / walls (parts) | 651 / 1,581 | 907 / 1,269 |
| Doors | 220 doorways | 148 |
| Plan / build | 0.3 s / 14 s | 0.3 s / 44 to 52 s |

Joints: 14 are the welded hall flights. Build time tripled: the halls' big floors give the furnishing many more spots to try; not chased.

**Tried by hand:** `--gen-interior` on seeds 1 to 3: every room reached on deck and in the chassis, 0 refused, the drive seat the bridge's
helm; `--interior-walk --walk 4` on seed 1 (before the stair column moved west): every door opened, every doorway, corridor, lift car and
stairwell passed, and the walker walked 11.6 m in 4 s (2.9 m/s) from the bridge's helm. That run found one hall flight whose foot the
railing blocked (its column was then the east end); the column is now the west end, not walked again.

**Not tried:** a player climbing the hall stairs (the walk-through knows stairwells and finds hall flights only by part), drawing anything
(railings, galleries, the open volumes), `--check-construct-parts`, the extra-large class on a real build, a client.

**Not done (step 2 and 3 of the brief):** an off-centre spine, an off-centre lift, free-form L/T/notched rooms beyond the bridge's and
mess's; decoration (pipes, ducts, cable trays, ribs, signs, hoists) and the dark lamps (26 to 42 generated rooms a seed: the 254-wire cap).
Columns under the wide spans aren't built (the Steel Column is a storey high).

**For the coordinator:** regenerate `Constructs/Blueprints/TitanWalkerFitted.json` after merging
(`--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1`).

### Next (phase 10 and on)

Lamps that need no wire (or a higher link cap); the kit's batch rebuild; a wide event door for Garage Wall openings; a single lift car through all storeys; medical and food items; tier-4 room tables for the capital class; damage and fire kept within bulkhead sections (game code); the train lane joined to the logistics
track; static bases from a plot; joining the vehicle kit; the missing parts listed by the run.
