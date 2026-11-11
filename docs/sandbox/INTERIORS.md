# Interiors: planned rooms for mobile and static bases

History (phase records): INTERIORS-HISTORY.md. Read it only when you need why something was built this way.

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
