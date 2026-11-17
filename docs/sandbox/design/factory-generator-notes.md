# Factory generator: build notes

Notes kept by the builders of `factory-generator.md` (the design and the user's answers of 2026-11-17). Newest state first.

## State (2026-11-17, second builder): phase 2 begun

- **The shed shell** (part kit `factory_shell`): Portal Column 8 m (an H column), Portal Rafter 5.5/7.5/9.5/11.5/13.5 m (seats under each end, top
  sockets over them), Roof Sheet 4 x (span + 0.5) m (corrugated, 3 mm over the rafters). `FactoryScript.Shed` frames each process zone: columns along
  the road's edge and the band's back, 4 m bays from a metre in (the zone board stands at the west edge; its Medium Text Sign is 4 m wide along z), a
  rafter across each pair, a sheet over each bay; open sided, 8 m eaves over the 6.3 m refiners. Militia rolls yard 40 %, shed 60 %. Refineries 1-12
  build with 0 coplanar faces; `--check-building` passes; the haul check still runs. Inventory: Buildings > Roofs.
- **The grading sheet** (the user grades it, answer 7): `/home/claude/factory-grading/index.html` (with `models/`), from `--preview-gallery
  /home/claude/factory-grading --only "..."` over Militia Extraction Posts 1-3, 5-13 (seed 4 is past the post class) and Militia Refineries 1-12,
  generated into `bin/.../Constructs/Blueprints` with `--gen-factory FUNCTION --seeds 12 --out` first, plus the Ore Hauler. The top three of each ship
  (`FactoryStyle.Seeds`). Not opened in a browser here.

### Phase 2 still to do (in order)

1. Halls: cladding (4 x 4 m corrugated, plated, window and louvre), hangar doors, crane rails, galleries and platforms, berms (3.5); a shed's
   gable ends; lamps under a roof.
2. Arrangements past `rows`: `ring`, `road`, `L` (3.4). `FactoryPlan.Make` throws for anything but rows.
3. Quality tools: `--sweep-factory` (failure rate, commonest refusal, times; `--gen-factory F --seeds N` is a start), `--factory-diversity`,
   `--grade-factory` and `docs/factorykit/grades.json`, and the Factory gallery as a republished Artifact (`--preview-gallery --only` works today).
4. A second style. Then phases 3 (factory, depot, cells as sub-blueprints, `stacked`) and 4 (outposts and defence).

## State (2026-11-17, second builder): phase 1 done but the whole-site spawn in play

### What came in

- **The drill deck** (Open 1 fixed): an extraction's back row in the drill zone carries four Concrete Floor 2x2 m slabs a tile (each by its b7_7 corner on
  the tile's top33 socket, so it stands 0.125 m west and back: over the back edge, never on a neighbour's top), the top at 1.30 m; a Workshop Drill's
  tower hangs 1.2 m under its foot to 0.10 m over the ground past the back edge, inside `GroundSlack`. Three step tiles (one, two, three slabs: 0.25 m
  rises) in the row in front climb to it at either end (the seed's). Drills stand on the slabs' corner sockets (`FactoryPlan.DeckX`). Cables, chutes
  and lamp wires are lifted over whatever they cross (`FactoryScript.Lift`: a node pair 0.15 m on the low side of each step). `--check-building` passes.
- **Ore bunkers** (part kit `factory_shell`, `Constructs/Parts/Factory`, inventory Production > Bunkers): Ore Bunker 2 m, 3 m and the low Tipping
  Bunker 3 m (top 1 m up), open concrete bins with chute ports on the rim. They replace every Storage Volume the kit laid: **a sized Storage Volume in
  the open spills over its open sides once it is past a quarter full** (ConstructSeal: an open face's lowest corner under the level pours), so the
  refinery's heaps and loading bay ran out as Material Bundles (the haul check found 3 bundles at the loading bay, and "made -480").
- **The Tipper Body** (part kit `scoop_parts`): a scoop with its mouth up, 2 x 0.6 x 2.5 m, 600 units whatever its air, sockets `hingeL`/`hingeR`
  under each side wall; on a Motor Hinge 3x3 Medium it tips sideways and pours over that rim into the store under it (ConstructScoops' tipping).
- **The Militia Ore Hauler** (vehicle kit, `Generators/OreHauler.cs`, an Extra; Militia isn't a shipped style, so `MilitiaOreHauler1.json` is written by
  `--gen-vehicle "Militia Ore Hauler" --seed 1`): a heavy chassis, a Tipper Body on a Motor Hinge on the hinge sill's left wing (min 0, max 75: a
  positive angle swings it out to the left), a TIP switch on the right wing's end, the drum on a sill behind. 12 of 12 seeds pass the vehicle check.
- **The loading skip** (`FactoryScript.Skip`): the extraction's loading object is a Tipper Body on a Motor Hinge on a 2 m Frame Beam post just behind
  the road; the buffer's chute fills it; LOAD on the post tips it over the road into a hauler's bed (lip about 3.2 m up).
- **The haul check** `--haul-check [--post N] [--refinery N] [--hauler N]`: post, refinery and hauler on one headless world placed by the site; buffer
  fed, skip filled by chute (600), hauler set under the skip, LOAD (600 into the bed), hauler moved to the Tipping Bunker (**teleported: the drive is
  a player's, not simulated**), TIP (600 out, 0 spilt), refinery makes steel (298 in 60 s; the stone a Foundry wants is creative stock). It passes.
- **Site files** `Constructs/Factories/Sites/MilitiaOreLoop1-3.json` (`call-of-slop-factory-site/1`), written by `--gen-factory site` from the plans:
  the post at 0, the refinery its length plus 12 m east on the same road line, an Ore Hauler's bed middle under the skip.
- **Variety**: extraction posts roll the deck's steps at either end, the plant at the west or east end (40 %), and up to twice the style's spare in
  drills (a third drill makes some seeds 34 m long: past the post class, so they fail the plan stage, which is the design's way; seed 4 of 1-12).
- **`--seeds N`** on `--gen-factory` (seeds 1 to N, a sweep). The work run's tick leaves first-call compiling out (`JitInfo`), and prints a slow
  tick's collections and allocation.

### Measured (2-core box, alone)

- Refinery 1 work run: 0.06 ms mean, 1.42 ms worst. Refinery 3: three runs 0.28-0.64 ms worst, no tick over 8 ms; one earlier run (before the JIT
  split) had one 19.9 ms tick at 6 s with no collection in the run's own count. **The 9-18 ms ticks are not seen since the bunkers**: the likely cause
  was the Storage Volumes spilling (each spill a new Material Bundle body: allocation, a body joining the world) plus first-call compiling; not proven.
- Window run (xvfb, `--window-shot --blueprint "Militia Refinery 1" --net host`): it spawns ("is down"), runs from the start, 1000 ore filled into
  the Tipping Bunker made 240 steel in 20 s; a **joined client** (second process, `--net join:127.0.0.1`) reads the post's skip (600 ore), bunker and
  Line Console screens as the host has them.

### Open (in order)

1. **A whole site spawned in play** from the Blueprints tab (the site file is data and the haul check reads it; each blueprint still spawns alone, as
   the Militia Ore Loop file's `about` tells a player where to put them). Needs a small game-side change in the spawn path (GroundJob joins several bodies).
2. The drive between them is untested by any check (the haul check teleports). A window run that sits a player in the hauler and drives the road
   (`sit`, `hold MoveForward`) would cover it.
3. The refinery's loading bay is an Ore Bunker 2 m: the steel waits there for a player (F) until the factory function or a Cargo Dock takes it.

## State (2026-11-17, first builder): phase 1 part done (history)

### What is in

- **`Source/FactoryKit/`**, on the vehicle kit's `BlueprintScript`:
  - `FactoryData.cs`: the brief (`FactoryBrief`, also from JSON with `--brief FILE`), size classes (`Constructs/Factories/Sizes.json`),
    function files (`Constructs/Factories/Functions/extraction.json`, `refinery.json`, format `call-of-slop-factory-function/1`).
  - `FactoryStyle.cs`: `FactoryStyle` and `FactoryStyles.Militia` (palette by role, zone floor tints, tier 2 with a chance of an older
    tier-1 machine in a bank, spare 0-25 %, margins, sandbag fence, the radio mast as signature, shipped seeds 1-3 of each function).
  - `FactoryProgramme.cs`: the solver. It walks `Constructs/Refining.json` back from the target to raw input, picks each step's machine at
    the tier (`MachineOf`), counts machines `ceil(rate / machine rate * (1 + spare))` with the part's own `speed`, and power from the parts'
    `power` draw plus the margin in Small Gensets (`PlantRate`). Extraction counts drills from their `GatherRate`. Edges carry rates and a
    main-line flag.
  - `FactoryPlan.cs`: the `rows` arrangement. Bands across the spine: road z 0 to -4, aisle -4 to -6, the machine band (deepest machine plus
    the seed's margin, whole tiles), a 2 m back strip. Blocks along it in flow order: intake (no aisle: tipping hopper by the road, the
    hauler's ore heap behind it, other heaps two to a column), the banks, refined stock, the loading hopper by the road, power (at the end,
    or behind the storage: the seed's; never behind an extraction's drills). Chute lanes in the back strip by interval colouring (chutes
    whose spans don't overlap share a line, 0.15 m apart, 11 lines). Everything on the floor's socket lattice (`Snap`). `Ascii()` prints it.
  - `FactoryScript.cs`: the builder. Foundations grown tile by tile (as the Showcase), painted by zone; cells; chutes along their lanes
    (`ChuteVia`, the Showcase's); the plant (gensets on fuel tanks, battery, main switch and charge gauge, sandbags); a Line Console at the
    aisle's west end wired into every machine's and drill's Run (starts on); numbered lit zone boards; the mast; lane markings and arrows
    on the road. The Showcase's helpers were copied in (Showcase itself untouched, so its committed output stays byte-identical); moving
    the Showcase onto the kit is left for later.
  - `FactoryCli.cs`: `--gen-factory FUNCTION [--style S] [--size C] [--tier N] [--seed S] [--brief FILE] [--plan] [--out DIR] [--check]`.
    Without `--seed` it does the style's shipped seeds. Each seed is built twice and compared; the file goes to `Constructs/Blueprints`
    (the loader reads that folder only, not subfolders, so not `Blueprints/Factories/` as the design said; group `Factories` in the tab);
    then the vehicle check as a towed body (builds, coplanar faces, stands; part budget from the size class). `--check` compares with
    the committed files instead of writing.
  - `FactoryCheck.cs`: `--factory-check "Militia refinery 1" [--seconds 90]`: spawns it as the game does (GroundJob in 2 ms slices, then
    the join), feeds the heaps, tipping hopper (and an extraction's buffer) 2000 units each, runs the game time on a creative headless
    world, and reports every main edge, every machine, the product, and the tick in thread CPU time (the first 3 s left out: JIT).
- **Shipped seeds**: `Constructs/Blueprints/MilitiaRefinery1-3.json` (the kit's output; regenerate with `--gen-factory refinery`, check
  with `--check`). The extraction posts are not committed until Open 1 is fixed (they spawn floating); `--gen-factory extraction` writes them. Seeds 1-8 of each generate and pass the build check, 0 coplanar faces.
- `SelfTest.ThreadCpu` made `internal` so the check can read thread CPU time.

### Measured (2-core cloud box, another builder compiling beside it)

- Generation: about 100-300 ms a seed warm (the first in a run 4-5 s: loading parts). Plan stages alone: well under a second.
- Refinery 1: 349 parts, 10 slices of 2 ms, join 21 ms; 90 s run: every edge carries, all four refiners work, 2,052 steel in the loading
  hopper; tick 0.19 ms mean, 4.8 ms worst. Refinery 3: works, but two ticks of 12-14 ms CPU (at 4 s and 15 s). Extraction 3: two ticks
  of 9-18 ms CPU at 32 s and 84 s, the rest 0.35 ms. No GC between them: not yet explained (see Open).
- `--check-building`: Refinery 1 ok. Extraction Post 1 FAILS (see Open, first item).
- `--look-report` Refinery 1: 94 % painted, 18 colours, 26 % dark or grey.

### Open (phase 1 still to do, in order)

1. **The extraction post floats.** A Workshop Drill Module's tower is solid to 1.2 m under its foot, and `BuildOnGround` (spawn and
   the checks) stands a body on its lowest point, so the post's floor ends about 1.2 m up and nobody reaches it (`--check-building`).
   Fix: a drill gantry, a raised deck at the back edge (posts and a platform, or the drill on a frame) so the tower hangs to the ground and
   the bit is within 25 cm of it while the floor stays on the ground. Then `--check-building` again.
2. **The hauler and the ore loop.** Findings: an event door doesn't re-seal a Storage Volume (seals are worked out on rebuilds and joint
   moves only), so a hopper with a door under it can't be a loading chute gate. What works today with no engine change: a chute world
   link made by hand (linking tool) from the loading hopper to a parked truck's bed (a Storage Volume) and from the bed to the tipping
   hopper; it snaps when the truck moves. A tipper (a bed on a hinge, tipped past the open top's edge) pours into a store under its lip
   (`ConstructScoops.PourVolume`, any construct's store counts). Recommended: a vehicle-kit `Ore Hauler` shape (Militia) with a hinged
   Storage Volume bed on a lever, and the tipping hopper as a low box beside the road the bed's lip reaches; the loading point a scoop
   (a Digging Bucket fed by chute, `ConstructScoops`: a scoop given ore by chute holds it) on a hinge over the road, tipped by a lever.
   Then the loop end to end in a window run.
3. **The shell.** Phase 1 ships the `yard` shell only (floor, zone tints, sandbags, mast, lamps). The building pieces stop at 2.75 m and the
   Workshop refiners are 6.3 m tall, so a shed or hall over them needs the `factory_shell` parts (portal columns, rafters, cladding, roof):
   do them with phase 2's full set.
4. **The site file** (`Constructs/Factories/Sites/*.json`: the pair placed together) and spawning a site from the Blueprints tab: not done;
   each blueprint spawns alone (the game's sliced spawn already takes them: `VehicleSpawner.AtOnce` is false past its step count).
5. **The tick spikes** above: find what takes 10-18 ms of CPU on a quiet tick (the F9 profiler or `slow-frame` skill), then judge the
   join's 21-66 ms (the existing spawner's, for any blueprint this size).
6. Not tried: a creative spawn in a window run, a joined client.

### Phase 2 notes for later

- Variety is thin: margins, power at the end or behind, heap order, mast end, older machines and paint. Extraction seeds 2, 3, 4 and 6
  build alike (142 parts). More arrangements (`ring`, `road`, `L`) and shells are phase 2's.
- Lanes are interval-coloured, not A*; overhead runs and crossings of the aisle are not planned yet (nothing crosses it in `rows`).

- Open (coordinator, 2026-11-17): `--gen-vehicles` does not compare the committed `MilitiaOreHauler1.json`, because Militia is not in `Styles.Shipped` (only shipped styles are compared). A generator change can leave the hauler stale silently; give the factory kit its own compare (e.g. `--gen-factory hauler --check`) or a way to list a non-shipped style's committed vehicles.

## The user's grading of the first Militia sheet (2026-11-17)

No seed ships yet: the user doesn't love any of them. The layouts are okay, but:
- **Spacing is a weird mix:** gaps between machines, banks, walkways and walls vary for no reason. Use one consistent spacing system (module grid, fixed aisle widths, set clearances by purpose) so a building reads as planned.
- **Signposts are unnecessarily large and all over the buildings:** far fewer signs, smaller, placed where they inform (a zone entrance, a machine's own label), never as filler.

New parts the user wants for cargo and storage (they also simplify the kit's buildings):
1. **Larger bin sizes** (more sizes of storage bins/bunkers).
2. **A walled cargo volume option:** the custom cargo volume can have walls by default (a setting), giving up some of the allowed space, so storage no longer needs to be enclosed by hand.
3. **A large flat grate:** cargo tipped onto it goes into the chute network (an intake for tipping trucks, buckets and players' dumps).
4. **An output spigot like a combine harvester's unloading auger:** a station can unload into an open-top truck or bin (swings out, pours loose material and small pieces).
