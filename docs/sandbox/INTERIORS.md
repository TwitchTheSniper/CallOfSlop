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
| `SizeClasses.json` | Each class's tonnage band (bands must meet), deck rules and programme (the rooms it asks for). **Provisional.** |
| `RoomTypes.json` | Each room type's minimum size `[along the spine, deep]` in cells, `want` (cells of floor), `layout` (`grid`, or `free`: a rectangle plus a wing, phase 2; system rooms stay on the grid), `deck` (lower/upper/any), `row` (inner beside the spine, outer on the hull side), `end` (bow/stern), `wants`/`avoids`, `critical`, `exterior` (may open outside), `modules`, `loot`. System rooms (stairs, elevator, storage, fuel, spare, dead) are placed by the planner. |
| `ModuleSets.json` | The `shell` parts (slab, stair slab, stair, walls, corners, door wall, door corner, garage wall, hinge, door leaf, adapter pad) and, for each set, the parts a room holds (`count`, `perCells`, `loot`, `role`) and `missing`: what it should hold that no part does yet. |
| `LootHooks.json` | A room's loot id mapped to a `Constructs/Loot.json` table. **Provisional:** Loot.json has faction tables only, so every hook points at `trader`. |

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
     - each deck's room walls along the outline of its cells (every room is closed by its own corner pieces and 4 m pieces, so
       neighbours stand wall to wall, a 0.5 m bulkhead, and shared doorways line up; a free room's inner corner gets a 2 m piece
       at each wall that meets it);
     - door walls and door corners with a Door Hinge and a Steel Flush Door (one leaf per shared doorway);
     - bulkheads (a door in the spine, a Garage Wall opening over the lane);
     - closed spine ends on upper decks;
     - stairs: a Steel Straight Stair a cell in from the stairwell's west wall, rising +z under a Stair Floor's opening. If the
       rules refuse it, it is welded on and counted.
     - the next level's slabs, on the walls below;
     - each room's things from its module set, one to a free slab socket (block middles first), with the loot hook on
       containers marked `loot`, and the helm and gunner wired where the rules allow; a fuel plant (`role: plant`) gets a Fuel
       Tank fitted to it, and a room's lamps a Switch on the first lamp, wired to each;
     - the freight lift ("Phase 2 record");
     - the power report: every electrical part no working battery or plant reaches, and plants no fuel reaches.
  4. Refusals, notes and missing parts are listed.
- **`Program.Interiors.cs`**: `--gen-interior <blueprint name or file> [--seed S] [--out DIR] [--plan]`. Writes `<Name> Fitted`
  (default `Constructs/Blueprints`) and prints the check. It loads the file back through `ConstructBuild.BuildOnGround` and
  compares part counts. With `--seed` it also plans twice and compares. `--plan` prints the ASCII maps. Console output goes to
  `callofslop.log` next to the exe, as for every tool.

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

### Next (phase 3 and on)

**Spare space (user, 2026-11-10: both).** Each size class gets its own room list (bigger classes add rooms: extra quarters,
storage, workshops), and what is left after that becomes generated rooms (storage, fuel, workshops) chosen by what fits and the
neighbours. Not started in phase 2.

Also: per-room loot tables; damage and fire kept within bulkhead sections (game code); the train lane joined to the logistics
track; static bases from a plot; joining the vehicle kit; the missing parts listed by the run.
