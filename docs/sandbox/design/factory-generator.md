# The factory generator: factory buildings from what they do (design)

The user, 2026-11-17: before factions build their ships in real factories that players can raid
(`faction-controllers.md`, answer 6 and its prerequisite note), design a generator for factory buildings with
logical layout rules, consistent design principles and fun, interesting layouts, in the spirit of Karl's ship
designer.

Binding decisions it is designed from (the user, 2026-11-11, project doc `claude/base-generator.md`):

1. **Function first, then outwards.** A base is defined by what it does; the function gives the machines and objects it
   needs; a logical layout is generated from that list; the faction's style then changes how the layout and objects are
   arranged. Not style first.
2. **First functions:** resource extraction, refinery and factory, vehicle depot, defended outpost.
3. **Working when near:** built and wired for real, running only while a player is close; far away it sleeps and its
   stock is worked out from time passed (`linked-logistics.md` answer 6).
4. **What a faction may change:** the arrangement (rows, walled ring, sprawl along a road, stacked platforms), the shell
   (open yard with awnings, sheds, halls, dug in), paint, finish and dressing, and machine choice (tier or variant, spare
   capacity, defence).
5. **First build:** an extraction outpost and a small refinery as a pair (the ore loop with a hauler).
6. **Placement:** creative spawn only for now; where bases stand in worlds is decided later (maps are another developer's).

And (user, 2026-11-10/11): large structures are **large hangars and industrial spaces**, not cramped rooms; more
decoration; asymmetric layouts (an aircraft carrier's hangar deck, a cargo ship's engine hall); functional furnishings;
interior materials per faction.

Not touched: the production system is TwitchTheSniper's (SURVIVAL.md); the generator only lays it out through its
rules, and anything needing a change there is raised with them first.

## 1. What it copies from the ship designer and the vehicle kit

Karl's ship designer works because every step goes through the game's own rules, the shape is planned before the
parts, the output is ordinary blueprints, and fast tools look and measure.

| From | Taken whole |
| --- | --- |
| `BlueprintScript` (`VehicleCore.cs`) | Every part, link and setting is a real step through the game's rules; a refusal throws with its reason. The Factory Showcase (`FactoryShowcase.cs`) already lays 118 x 32 m of working factory this way, and its helpers (`Tiles`, `Walkway`, `FlowArrows`, `AreaSign`, `Feed`) move into the kit. |
| ship-design 1, 1b, 1c | Plan before parts (function, flows, zones, plan, then the shell over it); structure as welded pieces that break off one at a time, frames tougher than cladding. |
| ship-design 2b | Wires and data name their ends and are routed by conduit; chutes and conveyors are laid by the kit's own router (3.4), since their path is the layout. |
| `FactionStyle`, `Dice.Fork` | A shape never names a colour or favourite part; seeds forked per stage, generated twice to prove them. |
| QUALITY-TOOLS.md | Diversity, sweeps, sheets and the user's grades (section 5). |
| `InteriorPlanner` | The 4 m cell, 3 m storey, `GridRect`, `InteriorRng`, ASCII plans; its room filler for crew blocks. |
| buildings.md, look.md | The job gives the shape; reads at 50 m; one style per faction; ways in marked and lit; `--check-building`'s walker. |
| `showcase-new-notes.md` | Lessons paid for: `Config` values the loader refuses, links through a Frame Beam, orders-only makers, Large Joins where a wide piece passes. |

## 2. Inputs: the brief

A **factory brief** (`FactoryBrief`, a record; also readable from JSON so a faction controller or a test can write one):

| Field | What |
| --- | --- |
| `function` | An id from `Constructs/Factories/Functions/*.json`: `extraction`, `refinery`, `factory`, `depot`, `outpost` |
| `target` | Material or part keys with a rate per hour (`steel 200/h`), or a blueprint to build (a depot) |
| `size` | `post`, `works`, `plant`, `complex` (2.1) |
| `style` | A `FactoryStyle` name (a power's, from its kind) |
| `tier` | Machine tier ceiling, 1 to 5 (`MachineTiers.cs`) |
| `seed` | u32 |
| `site` | Footprint, slope (graded flat by default), gravity, the side the road or pad comes from, keep-outs, the deposit for extraction |
| `extras` | Defence 0-3, spare capacity 0-50 %, crew size, dock kind (none, road, Cargo Dock, pad) |

Everything defaults from the function and size, so `--gen-factory refinery --style Militia --seed 3` is enough.

### 2.1 Size classes (provisional, `Constructs/Factories/Sizes.json`)

| Class | Footprint | Parts (most) | Machines | Halls | Like |
| --- | --- | --- | --- | --- | --- |
| `post` | up to 32 x 32 m | 1,200 | 1-4 | none or one shed | an extraction post, a guard post |
| `works` | up to 64 x 64 m | 3,500 | 4-14 | one hall and a yard | the first refinery |
| `plant` | up to 128 x 96 m | 8,000 | 12-40 | two to four halls | the Factory Showcase's scale, a vehicle depot |
| `complex` | several bodies | 8,000 a body | 40+ | a site of plants | a faction shipyard (later) |

A part budget well under the construct's 16,000 (parts-and-data "Performance and caps") leaves room for players to
build on it and for the vehicle a depot makes.

## 3. The pipeline

Eight stages, each a pure function of its inputs and its own seed fork. A later stage never changes an earlier one's
decision; one that can't fit fails with the stage and reason, and the sweep tries another seed (as `--sweep-vehicle`).

```
brief -> 1 programme -> 2 flow graph -> 3 zones -> 4 plan (grid, cells, routes) -> 5 shell -> 6 style and dressing
      -> 7 wiring -> 8 checks -> blueprint(s)
```

### 3.1 Function to programme

A **function file** (`Constructs/Factories/Functions/refinery.json`, format `call-of-slop-factory-function/1`) lists, as
data, the roles it needs and how each is sized: intake bins per input kind (hold 30 min), process machines for the
target (plus the brief's spare), output bins (60 min), a dock of the brief's kind, power for the load plus 25 %, its
consoles, a crew room set, and the brief's defence.

The **programme solver** turns the target rates into machine counts with the game's own tables, never its own numbers:
for each wanted output it walks `Refining.json`, `Assembling.json`, `Extrusion.json` and the Press presets back to raw
input, picks the machine for each step (the style's machine choice within the tier: a Workshop Smelter or two plain
Smelters), and counts machines as `ceil(rate / machine rate * (1 + spare))`. Power is the sum of the machines' draw
plus a margin, then a plant from the style's list (gensets, a reactor at tier 4+). The result is a **programme**: a list
of objects, each with a role, a part (or a sub-blueprint, 3.4), its throughput and its ports.

| Function | Core | Around it |
| --- | --- | --- |
| **Extraction** | Drill Modules over the deposit with buffer and shaft | ore bins, a loading point (hauler bay or Cargo Dock), power, crew shed, mast |
| **Refinery** | Smelters, Foundries, Sawmills, a Recycler | intake heaps, refined bins, Smart Terminal and Beacon, refining console |
| **Factory** | Extruders, Presses, Formers, Assemblers, Cutter, Die Lathe, Crafting Terminal | material yard, routers, piece racks, tool room, orders console |
| **Vehicle depot** | Building Stations with bays sized to the target, Loader Arms or a crane bay | part store, feed line (local or linked), apron, fuel, hangar door |
| **Defended outpost** | Turrets, watch platform, walls | magazine, small plant, barracks; wraps another function or stands alone |

A function can **wrap** another: `outpost` around `extraction` adds the defence ring; `plant` sizes of `factory` may
include a `refinery` front end (the Showcase is refinery + factory + depot in one).

### 3.2 The flow graph

Nodes are the programme's objects; an edge is a material flow with a **carrier kind** chosen from what moves and how
much:

Carriers: `chute` (bulk), `conveyor` (pieces, with Routers, Joins, Large Joins), `crane` (pieces or submodules too big
for a conveyor: a bay with rails and a bridge, kept clear under it), `arm` (short hand-overs: hold to station, pad to
build), `vehicle` (between buildings or off site: a lane, hauler bay or pad) and `link` (a Transmitter/Receiver or Relay
with a Cargo Dock).

Each edge has a rate (from 3.1) and a **priority**: the main line (raw to product) is spine traffic and gets the
straightest route; returns (scrap to the Recycler, empty die stock, the Showcase's rack return) and services (fuel, ammo)
are secondary and may go round. The graph is grouped into **cells**: a machine and what only it uses (its feed hopper,
its out router, its console). Cells are the unit the plan moves around.

### 3.3 Zoning

Each node gets a zone, and zones are the plan's first blocks, placed as rectangles along a **spine** in flow order with
an adjacency score: **intake** (heaps, drill heads, tipping bay: the road side and the deposit), **process** (refiners,
presses, stations: in a hall, after intake, before storage), **storage** (bins, racks, the yard: between process and
output, under a crane or arm), **output** (docks, apron, hangar door: the road or pad side, never across intake's lane),
**power** (gensets, fuel, batteries, the main switch: apart, downwind, fenced), **control and crew** (consoles, network
board, mess, bunks: overlooking process, by the main door, away from power), **defence** (turrets, walls, watch
platform, gate: the perimeter and ways in), **docks and links** (Cargo Dock, mast, pad: the edge, clear approach).

### 3.4 The plan: grid, arrangement, cells, routes

**Grid:** the interior planner's 4 m cell and 3 m storey (building pieces are a 2 and 4 m module), a 0.25 m sub-grid
inside a cell for machines and sockets. Halls are taller but stay on the 4 m plan, so walls, columns and roof panels
land on a module.

**Arrangement** (decision 4), the style's weighted pick, bends the spine: `rows` (straight, cells either side: the
Showcase), `ring` (a loop inside a wall, process in the middle), `road` (cells strung in clumps along a track: a
sprawl), `stacked` (intake on top, chutes falling through platforms), `L`/`U` (the spine turned round a yard).

**Cells** go in flow order along the spine, input upstream, output downstream; a cell's footprint is its machine plus
a service margin (1 m clear before its screen, walkways 0.9 m, 1.1 m on the main aisle: buildings.md "Laying out a
building"). Same-recipe machines group into **banks** facing a shared feed (the Showcase's eight refiners).

**Routing:** A* on the sub-grid over three layers (floor, a 2.4 m overhead rack, crane height), main line first. Costs:
turns, layer changes, length; a floor crossing of the main aisle is forbidden, of the vehicle lane overhead only (4.5 m
clear). Overhead runs ride a pipe bridge that is part of the shell. The router writes nodes; wires stay conduit-routed.

**Asymmetry and the set piece** (rules 9, 10) are chosen here, before the shell, so the plan bends to give them room.

**Cells as sub-blueprint uses.** A repeated cell (a refiner bank, a drill head with buffer and shaft, a turret with its
magazine) is its own blueprint, used in the factory (`sub-blueprints.md`: 128 uses a blueprint, live by default), so
salvage, rebuilding at a Building Station and fixes to the cell's generator all deal in named modules.

`--factory-plan` prints each storey as ASCII (zones as letters, cells as numbers, walkways `.`, the lane `=`, routes
`-|+`), the programme with rates and every edge with carrier and length, as `InteriorPlan.Ascii()` does.

### 3.5 The shell: halls, yards, platforms

The shell is built **over** the plan; the machines are its reason. The style picks one per zone (decision 4): `yard`
(hard standing, a fence, awnings over machines that mind the weather), `shed` (one 4-8 m span), `hall` (12 to 24 m clear
span, 8 to 14 m to the eaves, column bays of 6 or 8 m carrying a crane rail, roof lights, big doors at the ends),
`gallery` (a mezzanine at 4-5 m: control room with windows on the floor, offices, walkways over the line), `platform`
(open steel decks on columns, stairs and ladders, chutes falling through) and `dug in` (a berm round a low shell, a ramp
down).

**Big industrial spaces** (user, 2026-11-10/11): a hall is one volume, not rooms. Its clear height comes from what moves
in it (crane hook over the biggest piece; a depot's biggest vehicle plus 2 m), never under 8 m. Crew rooms go in the
gallery or a lean-to, so the floor stays open and the main door sees the far one. References: a carrier's hangar deck
(low, very wide, lifts at the edge, painted lines), a ship's engine hall (tall, gratings at three levels, the machine
reaching through them).

**New parts** (part-kit generator `factory_shell`): the building pieces stop at 2.75 m walls and 4 m beams, so halls need
portal columns and rafters (8 to 24 m spans in welded pieces), 4 x 4 m cladding (corrugated, plated, concrete; window
and louvre variants), roof panels with a roof-light variant, ridge and gutter, sliding and roller hangar doors (4, 8,
12 m, on the event doors), crane rails to the bay spacing, grating mezzanines with rails, a pipe bridge, berm slopes.
Plain parts where possible (look.md "A detail budget"); frames toughest, then cladding, then roof (ship-design 1b).

### 3.6 Style and dressing

A **`FactoryStyle`** is the vehicle kit's `FactionStyle` for buildings; a power's style names both, so its trucks and
sheds match. It holds: arrangement and shell weights; machine choice per role and tier, spare capacity and defence
ranges; a palette by role (cladding, frame, roof, zone floor tints, accent, hazard: three colours plus hazard, look.md);
finishes; interior materials (user, 2026-11-11); dressing slots (lamp colour, sign voice, clutter: drums, pallets, cable
reels, tool racks); the **signature** seen at 50 m (a stack, a mast, a painted roof: buildings.md rule 3); limits
(asymmetry, crooked patched cladding, how much is left open).

**Functional furnishing first:** a tool rack by the press it serves, lockers by the door, a mess off the gallery, a
first-aid post on the main aisle, a fire point every 24 m. Then dressing: the Showcase's zone tints, floor arrows and
numbered lit area signs, hazard paint on crane bays and moving edges, lamps at every way in; few, larger parts, and
paint or a finish wherever that does the job.

### 3.7 Wiring: power, logic and orders

- **Power:** the plant feeds a main switch, then **one switch per zone** (a raid that hits the power hall stops the
  lines, a player can stop one hall); poles or wall runs so no wire passes 8 m (ship-design: a wire reaches 8 m), all
  routed by conduit (`"conduit": true`).
- **Logic:** a start console per zone (the Showcase's "Start runs every maker"), a beacon that blinks while lines run,
  stock gauges on the bins, a network board (Medium Text Sign) in the control room.
- **Orders:** makers on an orders console are `ordersOnly` (production.md "Orders-only machines"); a depot's Building
  Station gets its target blueprint; a dock gets its Transmitter or Receiver and, for a faction, its pairing on spawn.

### 3.8 Checks (every seed, before it is written)

1. **Builds:** every step through the rules; no REFUSED line.
2. **Works:** a headless run spawns it, switches it on, feeds intake from creative stock and runs **90 s of game time**
   (bounded, not a long simulation): every machine starts a job, every main edge carries something, the product count
   moves, the tick stays under 8 ms. An idle machine or dry edge names itself.
3. **Reach:** `--check-building`'s walker reaches every console, screen, seat, door and switch; a hauler-sized box
   reaches every dock, apron and tipping bay along the lane.
4. **Links:** `--conduit-check` clean; no chute or conveyor over the main aisle below 2.4 m.
5. **Looks:** zero coplanar faces between parts (`--check-vehicle`); `--look-report` within the style's band; rule 8.
6. **Budget:** parts, links, joints and generation time inside the size class.

## 4. Layout rules and design principles

1. **Read the flow from the door:** walking the main aisle meets in, make, store, out in order, with numbered signs and
   floor arrows. The main line never doubles back; returns run along the back.
2. **Raw never crosses finished:** intake and output lanes never cross; the dirty end (ore, scrap) and the clean end
   (assembly, the station) are apart.
3. **Every machine reachable standing** (rule widths in 3.4; 2 m where carts pass). Bot crews walk it: the plan hands
   CrewNav (`bot-brain.md`) its aisles, doors and stations as hints, as the interior planner does.
4. **Vehicles have their own lane,** 4 m at least (8 m on a depot's apron), turning room at the ends, crossings marked.
5. **Overhead is for flow, the floor for people:** 2.4 m clear over walkways, 4.5 m over the lane, nothing over a
   crane's bay.
6. **Sight lines:** the control room sees the floor, the main door the far door, a guard post every way in.
7. **Services shown** on racks and pipe bridges (buildings.md "The site" 6); the stack is the tallest thing.
8. **Reads at 50 m:** separate masses (hall, lean-to, tower, stack) with air between, stepped rooflines; the vehicle
   kit's side and top fill limits applied to the site.
9. **Asymmetry on purpose:** one end differs (a tower, a crane gantry over the yard, a one-sided lean-to); banks offset,
   not mirrored; symmetry only where the style asks for order.
10. **One set piece per building:** the biggest machine given space, light and the tall hall (the depot's bay under its
    crane, the furnace bank seen from the gallery, a drill in an open-roofed pit).
11. **No coplanar faces** (`.claude/rules/z-fighting.md`): cladding 3-5 mm proud of its frame, floor tints 3 mm over the
    slab, seams offset as ship-design 1b.
12. **Defensible and raidable:** two or three ways in, choke points before the control room, power and magazine as weak
    points (the interior planner's critical rooms); never one sealed door.

## 5. Variety: seeds, diversity, grading

- **Seeds** fork per stage (`programme`, `zones`, `arrangement`, `plan`, `shell`, `dress`, `paint`): a paint roll never
  moves a wall; each seed is built twice to prove it.
- **Diversity** (`FactoryDiversity`, the vehicle kit's distance with factory terms): plan (zone rectangles and the
  spine's shape on a normalised grid), silhouette (side and top fill in 16 slices, as `VehiclePlan.Profiles`), shell
  mix (share of yard, shed, hall, platform, dug in), parts (kinds, capped), flow (route length over straight-line
  length, crossings, layers used), size. `--factory-diversity "Style function" --seeds N` prints the spread, the alike
  groups, the most different set, and the traits that NEVER VARY, exactly as `--diversity`.
- **Grading:** `docs/factorykit/grades.json` in the same format as the vehicle kit's (`--grade-factory "Militia
  refinery 7" 4 "good gallery, dull roof"`), user grades only, and `--grade-report` reads both files. Shipped seeds per
  style and function are the graded best (`FactoryStyle.Seeds`).

## 6. Output and quality tools

**Output:** ordinary blueprints in `Constructs/Blueprints/Factories/` (group `Factories` in the Blueprints tab), one per
shipped style, function and seed, with cell blueprints in `Constructs/Blueprints/Factories/Cells/` used by them.
Foundations anchor them. One body per building; a `plant` with several buildings is several blueprints placed by a
small **site file** (positions, the yard and road slabs between them; `Constructs/Factories/Sites/*.json`), so no body
nears 16,000 parts and a raid on one hall doesn't rebuild the others' meshes. Because they are blueprints of ordinary
parts and uses, a player can salvage them, copy a cell, or have a Building Station build one (or one cell) like any
other blueprint.

| Command | What |
| --- | --- |
| `--gen-factory FUNCTION [--style S] [--size C] [--tier N] [--seed S] [--brief FILE] [--plan]` | Generate, check (3.8 less the work run), write; `--plan` (also `--factory-plan`) prints stages 1-4 only, under a second |
| `--factory-check NAME [--seconds 90]` | The work run and the reach, link and look checks; exit code = failures |
| `--sweep-factory "Style function" [--seeds N]` | Many seeds, nothing written: failure rate, commonest refusal, times |
| `--factory-diversity`, `--grade-factory`, `--grade-report` | Section 5 |
| `--preview-gallery DIR --factories`, `--preview-shot NAME --views top,iso --cut 4` | One **Factory gallery** Artifact (republished as ship-design 4 does the Ship gallery; its cut slider is a floor plan); the 3 s plan and corner shot |

## 7. Performance

- **Generation is off the tick:** at the command line for shipped seeds, on a worker thread in play from a brief; plan
  stages under 50 ms and the scratch build under 1 s for a `works` on the 2-core box (`--sweep-factory` times them);
  the blueprint cached by brief hash in the save folder's `Cache/`.
- **Spawn is budgeted:** 3,500 parts in one tick would pass 8 ms, so a generated factory spawns in slices of a step count
  measured to stay under 2 ms a tick (it rises over a second or two). If no sliced spawn exists, it is phase 1's one
  engine change, measured with the largest `plant`.
- **Near runs, far sleeps** (decision 3): near a player its machines run as anyone's (the Showcase: 0.14 ms median tick
  while building). Far, the ledger estimates output (`linked-logistics.md` answer 6) from a **rate card** the generator
  writes with each blueprint, so nothing re-solves the chain.
- **Draws:** cladding and roofs as plain parts in few materials keep the merged chunks few draws; lamps only where people
  go (the 4 nearest within 16 m light anything).

## 8. How factions and players use it

**Factions** (the faction controllers' production phase, after relations and admirals; their answer 6): a power holding
a planet founds an `extraction` on each held deposit, a `refinery` per few of those, a `factory` and a `depot` at home.
The controller writes the brief (target from its shipbuilding wants, size from its stock, its style, a seed from the
world seed and site id) and spawns the result; crews staff the control rooms and haul on linked routes; the ledger moves
stock while nobody is near. Players raid for real: break the power hall and the lines stop, take the stores, steal a
half-built ship from the bay. Wrecked works are rebuilt from the same seed before ships come again.

**Players:** creative spawn from the Blueprints tab (decision 6). Later, perhaps, a **Works Planner** panel (question 4).

## 9. Phases (one builder each, the whole phase; a WIP `[skip ci]` after each step)

| Phase | What | Done when |
| --- | --- | --- |
| **1. Kit and ore loop pair** | `Source/FactoryKit` (brief, function files, solver, flow graph, zoner, grid planner and router, `--gen-factory --plan`); the Showcase's helpers moved in; `factory_shell` parts for a shed and a small hall; Militia style; `extraction` (post) and `refinery` (works) with a hauler bay and road, as a pair in a site file; sliced spawn if missing | A creative player spawns the pair, switches on, drills fill bins, a hauler carries ore over, steel comes out; `--factory-check` passes both; 8 seeds of each generate |
| **2. Halls and quality** | Full `factory_shell` (portals to 24 m, cladding, hangar doors, crane rails, galleries, platforms, berms); `rows`, `ring`, `road`, `L`; sweep, diversity, grades, the Factory gallery; a second style | The user grades a sheet of each; no style slot with choices NEVER VARIES |
| **3. Factory and depot** | `factory` and `depot` functions; cells as sub-blueprint uses; `stacked`; the rate card | A generated depot builds a vehicle from a generated factory's parts over a Cargo Dock |
| **4. Outposts and defence** | `outpost` alone and as a wrapper, the raid rules, CrewNav hints | A defended post holds off a test raid and can be broken into |
| **5. Faction production** | Hand-off to the faction controllers' production phase (briefs, spawn at holdings, ledger estimate, rebuild) | (that design's) |

Phases 1 and 2 need nothing from TwitchTheSniper's systems beyond what exists; phase 3's sub-blueprint cells and phase
5's estimate lean on sub-blueprints and linked logistics, so they wait for those to be on main and are raised with
TwitchTheSniper first.

## 10. Open questions for the user (recommendation first)

1. **Where the code lives.** Recommend a new `Source/FactoryKit/` on the vehicle kit's `BlueprintScript`, with its own
   commands (`--gen-factory`), data in `Constructs/Factories/`. Or: a part-kit generator per factory, as the Showcase
   is (simpler, but no briefs at run time, so factions couldn't use it).
2. **Committed seeds or generated in play.** Recommend both: shipped seeds committed for creative (graded), and
   faction works generated in play from a brief and cached. Or: committed only (factions pick from a shipped set: less
   variety, no worker thread).
3. **One body or several.** Recommend one body per building and a site file for a plant (raids and edits stay local;
   no body near 16,000 parts). Or: one body per site, as the Showcase (simpler links, one spawn, but a hall's damage
   rebuilds the whole site's meshes).
4. **A player-facing designer.** Recommend later (after phase 3): a Works Planner panel that rolls seeds from a brief
   and stamps a ghost. Or: never, factories stay hand-built by players and generated only for factions and creative.
5. **Which styles first.** Recommend Militia for phase 1 (the vehicle kit's test style, so its trucks match), then the
   first power kinds from faction-controllers (Traders, Raiders) in phase 2. Or: start from the archived factions'
   building styles (Ministry Brutalism, MacSlop stone and tin), which buildings.md already describes.
6. **The hauler in the ore loop.** Recommend a road and tipping bay for a player-driven ground hauler in phase 1 (works
   now), with a Cargo Dock variant once linked logistics ships are flying on main. Or: a Cargo Dock and route ship from
   the start.
7. **Grading load.** Recommend the user grades one sheet of 12 seeds per style and function at phase 2's end, and ships
   the top three. Or: ship whatever passes the checks and the diversity spread (no grading).
