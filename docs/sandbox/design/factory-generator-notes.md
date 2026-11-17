# Factory generator: build notes

Notes kept by the builders of `factory-generator.md` (the design and the user's answers of 2026-11-17). Newest state first.

## State (2026-11-17, first builder): phase 1 part done

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
