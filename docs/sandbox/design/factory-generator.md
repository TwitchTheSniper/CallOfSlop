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
3. **Working when near:** built and wired for real (powered machines, drills filling storage, lines producing), running
   only while a player is close; far away it sleeps and its stock is worked out from time passed (linked logistics'
   ledger, answer 6 of `linked-logistics.md`).
4. **What a faction may change:** the arrangement (rows, walled ring, sprawl along a road, stacked platforms), the shell
   (open yard with awnings, sheds, halls, dug in), paint, finish and dressing, and machine choice (tier or variant, spare
   capacity, defence).
5. **First build:** an extraction outpost and a small refinery as a pair (the ore loop with a hauler).
6. **Placement:** creative spawn only for now; where bases stand in worlds is decided later (maps are another developer's).

And (user, 2026-11-10/11): large structures are **large hangars and industrial spaces**, not cramped rooms; more
decoration; asymmetric layouts (an aircraft carrier's hangar deck, a cargo ship's engine hall); functional furnishings;
interior materials per faction.

Not touched: the production system itself (machines, recipes, stations, routers) is TwitchTheSniper's and this
generator only lays it out through its own rules (SURVIVAL.md, "Ships are built through TwitchTheSniper's production
system"). Anything below that needs a change there is listed as such and raised with them first.

## 1. What it copies from the ship designer and the vehicle kit

Karl's ship designer works because it never trusts itself: every step goes through the game's own build rules, the
shape is planned before the parts, the output is ordinary blueprints, and there are fast tools to look and to measure.
The factory generator takes each of those whole.

| From | What the factory generator takes |
| --- | --- |
| `BlueprintScript` (`Source/VehicleKit/VehicleCore.cs`) | Every part, link and setting is a real step through `ConstructBuild.Place`, `CanWire`, `BuildLink`; a refused step throws with the reason. The Factory Showcase (`Source/PartKit/Generators/FactoryShowcase.cs`) already lays 118 x 32 m of working factory this way. |
| ship-design skill 1, 1c | **Plan before parts.** Ships: job, crew, rooms, then the loft, then the rooms fitted to it. Factories: function, flows, zones, then the plan, then the shell over it. |
| ship-design 1b | **Pieces that break off one at a time.** A hall is columns, wall panels, roof panels and girders, each its own part, welded where they touch; frames tougher than cladding. A raid opens a wall, it does not delete a hall. |
| ship-design 2b, power-and-links "Ship links and walls" | **Name the ends, let the build route them** (`"conduit": true`): power and data are routed along walls and ceilings. Material links (chutes, conveyors) are laid by the generator's own router (section 3.4), since their path *is* the layout. |
| `FactionStyle` (`Styles.cs`) | **A shape never names a colour or a favourite part.** A `FactoryStyle` holds palette, slots, weights and limits; a new power needs only a style. |
| `Dice` and `Fork` | Seeded per stage, nothing from `Random` or hashes; each seed generated twice to prove it. |
| QUALITY-TOOLS.md | `--diversity`, `--kit-report`, `--vehicle-sheet`, `grades.json`: the same three for factories (section 6). |
| `InteriorPlanner` (`Source/Interiors`) | A 4 m cell, a 3 m storey, `GridRect`, `InteriorRng`, ASCII plans, a planner that knows only a volume and keep-outs. The factory plan reuses the grid types and the room filler for its crew block. |
| buildings.md | The job gives the shape; reads at 50 m; one style per faction; ways in marked; lit and switched; `--check-building`'s walker. |
| Factory Showcase notes (`showcase-new-notes.md`) | Lessons already paid for: `Config` values the loader refuses, links through a Frame Beam, orders-only makers, a Large Join where a Differential passes, a router with no way to its maker. Its `Tiles`, `Walkway`, `FlowArrows`, `AreaSign`, `Feed` helpers move into the kit. |

## 2. Inputs: the brief

A **factory brief** (`FactoryBrief`, a record; also readable from JSON so a faction controller or a test can write one):

| Field | What | Example |
| --- | --- | --- |
| `function` | A function id from `Constructs/Factories/Functions/*.json` | `extraction`, `refinery`, `factory`, `depot`, `outpost` |
| `target` | What it must produce and how fast: material or part keys with a rate per hour, or a blueprint to build (a depot) | `iron ore 600/h`; `steel 200/h, glass 40/h`; `Works Runabout` |
| `size` | Size class: `post`, `works`, `plant`, `complex` (a footprint and part budget band, section 2.1) | `works` |
| `style` | A `FactoryStyle` name (a power's, from its kind) | `Militia`, `Traders` |
| `tier` | Machine tier ceiling, 1 to 5 (`MachineTiers.cs`) | 2 |
| `seed` | u32 | 7 |
| `site` | Footprint (w x d m), the slope it must sit on (graded flat by default), gravity, the side the road or pad comes in from, keep-outs, a deposit position for extraction | 64 x 48 m, road from the east, iron vein at (12, -8) |
| `extras` | Defence level 0-3, spare capacity (0 to 50 %), crew size, dock kind (none, road, Cargo Dock, pad) | defence 1, spare 20 %, Cargo Dock |

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

Eight stages, each a pure function of its inputs and its own fork of the seed, each printable on its own
(`--factory-plan` prints stages 1 to 4 as text without building anything, in under a second). A later stage never
changes an earlier one's decision; if it can't fit, it fails with the stage and reason, and the sweep picks another seed
(as `--sweep-vehicle` does).

```
brief -> 1 programme -> 2 flow graph -> 3 zones -> 4 plan (grid, cells, routes) -> 5 shell -> 6 style and dressing
      -> 7 wiring -> 8 checks -> blueprint(s)
```

### 3.1 Function to programme

A **function file** (`Constructs/Factories/Functions/refinery.json`) says what the function is made of, as data:

```json
{
  "format": "call-of-slop-factory-function/1",
  "name": "refinery",
  "takes": ["ore", "stone", "wood", "sand"],
  "makes": "refined",
  "needs": [
    { "role": "intake",  "what": "bulk store",      "per": "input kind", "hold": "30 min" },
    { "role": "process", "what": "refiner",         "for": "target",     "spare": "brief" },
    { "role": "storage", "what": "bulk store",      "per": "output kind","hold": "60 min" },
    { "role": "output",  "what": "dock",            "kind": "brief" },
    { "role": "power",   "what": "plant",           "for": "load", "margin": 0.25 },
    { "role": "control", "what": "console",         "kinds": ["refining", "orders"] },
    { "role": "crew",    "what": "rooms",           "set": "works crew" },
    { "role": "defence", "what": "brief" }
  ]
}
```

The **programme solver** turns the target rates into machine counts with the game's own tables, never its own numbers:
for each wanted output it walks `Refining.json`, `Assembling.json`, `Extrusion.json` and the Press presets back to raw
input, picks the machine for each step (the style's machine choice within the tier: a Workshop Smelter or two plain
Smelters), and counts machines as `ceil(rate / machine rate * (1 + spare))`. Power is the sum of the machines' draw
plus a margin, then a plant from the style's list (gensets, a reactor at tier 4+). The result is a **programme**: a list
of objects, each with a role, a part (or a sub-blueprint, 3.4), its throughput and its ports.

| Function | Its core | Its objects |
| --- | --- | --- |
| **Extraction** | Drill Modules (tier) over the deposit, each with its buffer and shaft (gathering-and-salvage "The drill's buffer and shaft") | ore bins (Storage Volumes), a loading point (chute to a hauler bay or a Cargo Dock), power, a shed for the crew, lights, a mast |
| **Refinery** | Refiners by recipe (Smelter, Foundry, Sawmill, Recycler) | intake heaps, refined-material bins, a Smart Terminal and Inventory Beacon, the refining console, power, crew block, dock |
| **Factory** | Extruders, Presses, Formers, Assemblers, Laser Cutter, Panel Former, Die Lathe, the Crafting Terminal | a refined-material yard, routers and joins, piece racks, a tool room, orders console, crew, dock |
| **Vehicle depot** | One or more Building Stations with bays sized to the target, Loader Arms or a crane bay, a Lay-down Pad | a part store, the feed line from a factory (local or linked), vehicle apron and road, fuel and charge, a hangar door |
| **Defended outpost** | Turrets, a watch platform, walls | an ammo store, a small power plant, a barracks, a beacon; it wraps another function or stands alone |

A function can **wrap** another: `outpost` around `extraction` adds the defence ring; `plant` sizes of `factory` may
include a `refinery` front end (the Showcase is refinery + factory + depot in one).

### 3.2 The flow graph

Nodes are the programme's objects; an edge is a material flow with a **carrier kind** chosen from what moves and how
much:

| Carrier | When | Laid as |
| --- | --- | --- |
| `chute` | bulk material (ore, metal, sand) | a chute link, high where it crosses walkways (2.4 m clear) |
| `conveyor` | pieces up to the conveyor's width | conveyor links, Routers, Joins, Large Joins where a wide piece passes |
| `crane` | pieces or submodules over a conveyor's size or weight | a crane bay: rails along a bay, a bridge (`Medium/Large Crane Bridge`), the bay kept clear under it |
| `arm` | a short hand-over (hold to station, pad to build) | a Loader Arm at the corner of a bay |
| `vehicle` | between buildings, or out of the site | a road or apron lane, a hauler bay, a pad |
| `link` | out of the site to another network | a Transmitter/Receiver or Relay, and a Cargo Dock |

Each edge has a rate (from 3.1) and a **priority**: the main line (raw to product) is spine traffic and gets the
straightest route; returns (scrap to the Recycler, empty die stock, the Showcase's rack return) and services (fuel, ammo)
are secondary and may go round. The graph is grouped into **cells**: a machine and what only it uses (its feed hopper,
its out router, its console). Cells are the unit the plan moves around.

### 3.3 Zoning

Every node gets a zone; zones are the plan's first blocks:

| Zone | Holds | Wants | Avoids |
| --- | --- | --- | --- |
| **intake** | raw heaps, drill heads, the hauler's tipping bay | road side, the deposit | crew, output |
| **process** | refiners, presses, formers, assemblers, stations | intake before it, storage after it; a hall | the road (noise, nothing to see) |
| **storage** | bins, racks, the yard | between process and output, reachable by a crane or arm | |
| **output** | docks, the apron, the hangar door | the road or pad side, open sky over a Cargo Dock | intake's lane (no crossing) |
| **power** | gensets, fuel tanks, batteries, the main switch | apart: one end, downwind; its own fence | crew |
| **control and crew** | consoles, the network board, mess, bunks, lockers, washroom | **overlooks process** (a gallery or a raised office with windows on the hall floor), by the main door | power |
| **defence** | turrets, walls, a watch platform, the gate | the perimeter and the ways in | |
| **docks and links** | Cargo Dock, Transmitter/Relay mast, pad | the edge, clear approach | |

The zoner places zones as rectangles along a **spine** in flow order (intake to output), with the adjacency above
scored; the arrangement (3.4) bends the spine.

### 3.4 The plan: grid, arrangement, cells, routes

**The grid** is the interior planner's: a 4 m cell, a 3 m storey (building pieces are a 2 and 4 m module), with a 0.25 m
sub-grid inside a cell for machines and sockets (the vehicle kit's lattice). Halls are taller (3.5) but stay on the 4 m
plan grid, so a wall, a column and a roof panel always land on a module.

**The arrangement** is the style's first choice (decision 4), a weighted pick:

| Arrangement | The spine | Reads as |
| --- | --- | --- |
| `rows` | straight, the hall's length; cells either side in rows | a works: the Showcase |
| `ring` | a loop inside a wall; process in the middle, storage and crew round it | a walled fort, a compound |
| `road` | a road, cells strung along it in clumps with gaps | a sprawl, shacks along a track |
| `stacked` | vertical: intake on top, gravity down through platforms (chutes fall, no lifts) | a tower works, a hillside plant |
| `L` / `U` | a straight spine turned once or twice round a yard | a yard with sheds on two or three sides |

**Placing cells.** In each zone, cells go in flow order along the spine, each one's input port facing upstream and
output downstream; a cell's footprint is its machine's bounds plus a **service margin** (1 m clear in front of its
screen and gauge, 0.9 m walkways, 1.1 m where two people pass: buildings.md "Laying out a building"). Same-recipe
machines group into **banks** (two rows facing a shared feed, the Showcase's eight refiners) so one feed line serves
several. A bank of more than four turns into a repeated sub-blueprint use (3.4, output).

**Routing.** A grid router (A* on the 0.25 m sub-grid, three layers: floor, 2.4 m overhead rack, 5 m+ crane height)
lays every edge, main line first. Costs: a turn, a crossing of a walkway on the floor (forbidden on the main aisle), a
layer change, length. Chutes and conveyors may ride the overhead rack (a pipe bridge of Steel Beams on columns, part of
the shell); a crossing of the vehicle lane is only overhead (at least 4.5 m clear). The router writes nodes, so the
links are `"laid": true` only where a wall must be passed (2b's rule for routed ports still holds for wires).

**Asymmetry and the set piece** (section 4, rules 9 and 10) are decided here, before the shell: one cell is chosen as
the set piece, and the plan is bent so it has room and a sight line.

**Output: cells as sub-blueprint uses.** A cell that repeats (a refiner bank, a drill head with its buffer and shaft, a
turret with its magazine) is written once as its own blueprint and used in the factory blueprint as a **use**
(`sub-blueprints.md`: depth 8, 128 uses a blueprint). A player who salvages a refiner bank, or a faction that rebuilds
one at a Building Station, deals with a named module, and a fix to the cell's generator updates every factory that uses
it (live uses, answer 4 there).

**Print it.** `--factory-plan` writes the plan as ASCII per storey (zones as letters, cells as numbers, walkways `.`,
the vehicle lane `=`, routes `-|+`), the programme with rates, and the flow edges with their carriers and lengths:
the same habit as `InteriorPlan.Ascii()`.
