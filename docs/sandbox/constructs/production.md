# Production

Covers refining, extrusion, the Wheel Former, the Press, machine tiers, assembling, build stations. Part of the construct guide: index in ../CONSTRUCTS.md.

## Refining (decision 14)

The step after gathering: raw material goes into production machines, on a base or a big vehicle, and refined material comes
out. Chutes move it between them. [Assembling](#assembling) uses the results. The chain has three steps: raw ore and wood; first tier
materials (a Smelter's metal, copper, gold and glass, a Sawmill's planks and cloth, with stone and sand as they come out of the ground); second tier materials
from a **Foundry**, which mixes two first tier ones.

- **The machines** (`Constructs/Parts/Refining`, attach tool, physics-only; five tiers each, the numbers here are tier 1's, and what each looks like is tier 2's, the plainest: each tier has its own shape, [Machine tiers](#machine-tiers)). All are buildings. The **Smelter** (3.5 m, 5,145 kg, 3 kW): a brick furnace with
  a chimney, a glowing mouth, a hopper on top (its input) and, across an aisle, a tray at the front (its output). The **Sawmill** (3 m, 2,160 kg, 1.8 kW): a blade
  between a trough at the back (input) and a rack at the front (output). The **Foundry** (4 m, 7,680 kg, 3.5 kW): a double furnace with a hopper across its top and twin stacks, with a mixed input. Any part with `storage.refine` is a machine: its
  `machine` names it in the table, `capacity`, `min` and `max` are the input bin, and `storage.bulk` (material `any`) is the
  output bin. Both bins draw their fill, in the material's colour (all bins now do), and both must hold at least a batch (100).
- **The table** (`Constructs/Refining.json`, format `call-of-slop-refining/1`; `RefiningTable`). Each recipe gives `machine`,
  `input`, `unitsIn`, `output`, `unitsOut`, `seconds` and optionally `minTier`. A machine has one recipe per input material: what is in the input
  picks it. Units are 1 to 100, seconds 0.1 to 600 (at speed 1; work left over from a step carries into the next batch), materials are the names in
  `Recycling.json`. A bad file is refused with the reason (`--check-construct-parts` prints it, `RefiningTable.Problem`) and
  the built-in recipes are used. A part naming a machine with no recipes is refused.

| Machine | In | Out | Seconds a batch | Rate |
| --- | --- | --- | --- | --- |
| Smelter | 2 iron ore | 1 metal | 0.5 | 2 metal/s |
| Smelter | 2 copper ore | 1 copper | 0.6 | 1.7 copper/s |
| Smelter | 4 gold ore | 1 gold | 1.0 | 1 gold/s |
| Smelter | 2 sand | 1 glass | 0.5 | 2 glass/s |
| Sawmill | 1 wood | 2 planks | 0.5 | 4 planks/s |
| Sawmill | 3 planks | 1 cloth | 1.0 | 1 cloth/s |

  Iron ore gives the same `metal` as the Recycler. A Drill Module (10 a second) feeds several Smelters (4 ore a second each). A
  chute moves 10 a second, more with a higher tier machine at either end.
- **The Foundry's recipes** (machine `foundry`). A recipe lists `inputs`, 2 to 4 of `{"material", "units"}`, in place of `input` and `unitsIn`, and all of a machine's recipes
  are one kind or the other: a mixed machine's `RefineMix` makes its input `CStore`'s mixed one. Same material twice, a repeat of the same set, or the two kinds on one machine are refused.

| Second tier | In | Out | Seconds | From tier | Chains |
| --- | --- | --- | --- | --- | --- |
| steel | 2 metal + 1 stone | 2 | 1.0 | 1 | iron, stone |
| concrete | 2 stone + 2 sand | 3 | 0.8 | 1 | stone, sand |
| laminate | 2 planks + 1 glass | 2 | 1.0 | 1 | wood, sand |
| rubber | 2 planks + 1 sand | 1 | 1.0 | 1 | wood, sand |
| bronze | 2 copper + 1 metal | 3 | 1.0 | 2 | copper, iron |
| circuits | 2 copper + 1 gold | 2 | 2.0 | 3 | copper, gold |

- **A mixed input.** Each material keeps its own amount, up to the input's capacity (`CStore.MixMat`, `MixAmount`, at most 8 kinds). A material goes in only if some recipe of the machine's tier uses it and everything already there
  (`ConstructRefining.MixTakes`), so what is in the input picks the recipe: metal alone waits for stone or copper, metal and stone makes steel, and sand then stays in its source ("no recipe mixes it with 3 metal"). Run one chute in for
  each input: a port takes several. A pair runs until one side runs out; what is left of the other waits for its fellow.
- **Materials.** `copper`, `gold` and `planks` are ids 13 to 15, and `steel`, `bronze`, `concrete` and `laminate` 16 to 19 (2.5, 3, 3 and 1 kg a unit; `rubber`, `circuits` and `cloth` were salvage only, and now have recipes). The ceiling is 63 materials
  (`RecyclingTable.MaxMaterials`; the inventory has 64 slots): a material id is a byte on the wire, so only arrays grow. A Sawmill keeps its weight (1 wood to 2 planks); a Smelter's slag is lost.
- **Two bins in one store.** `CStore.Amount` and `Material` are the output, so a chute out, F, a spill and the mass treat a
  machine as any bin, and a machine's own `InAmount` and `InMaterial` are the input. Only `ConstructWorld.Feed` adds to the input,
  and only the step takes from it (when a batch is done, so a batch in hand loses nothing if the power goes or the machine breaks).
  A chute into a machine ends at `Feed`, a chute out starts at the output: there are no separate chute ports.
- **F** tips what you carry into the input, if it takes it and has room, else takes a load of 100 from the output; the output is
  never tipped into and the input never taken from.
- **Refused and blocked.** A chute bringing what the machine has no recipe for, or another material than its input holds, moves
  nothing: it stays in the source, and the look line says so for 4 s after the chute last tried (`CStore.Refused`). A
  partial batch waits for more. An output that holds another material or has no room for a batch stops the machine until a chute or F
  empties it. The look line: "Smelter: in 40/200 iron ore, out 12/400 metal - REFINING iron ore to metal - F: take 12 metal".
  All but the refusal are worked out from the store on either side (`ConstructRefining.StateOf`, `Status`, `Look`).
- **Power and Run.** As the Recycler (`ConstructRecycler.Powered`): in survival it needs a working battery whose links reach it,
  with charge, and draws only while it works. A wire into **Run** holds it off (`ConstructGatherer.HeldOff`): it waits, keeping the
  batch's progress, and draws nothing. The machine reads its own input and power, so it works on an anchored base with no seat.
- **Creative** (`CreativeRecycling`, infinite parts on): no power, and fast but not instant (2026-11-11, proposed): the whole load that
  fits (as many batches as the input holds and the output has room for) is one job, worked at `ConstructRefining.CreativeSpeed` (8) times
  the survival rate and never in under `CreativeMinSteps` (1.5 s), busy meanwhile, so its lamp shows Running; then made at once. The
  Run wire still holds it, the mains still cut it ([Production lines](#production-lines)), and a recipe's input still counts: a wrong material is refused as ever. A logic part wired to Run has no power on a flat battery either, so it holds the machine off.
- **Mass.** Both bins count (`Construct.LoadOf`: one load per part, the two weights added at their shared centre, in a box round
  both), by the usual `LoadChanged` path. `LoadKey` also follows where a load sits, so a batch that keeps its weight still moves
  the centre of mass.
- **Spilling.** A broken machine spills up to six Material Bundles for each bin, of that bin's material.
- **The Smelting Station** blueprint: a Smelter on a Concrete Foundation 4x4 m, and a second slab beside it with a Battery and two Materials Bins, chutes from the first bin into the
  Smelter and from the Smelter to the second. Tip ore or sand into the first bin (or chute it in), take the product from the second.
  A battery of 300 kJ runs a Smelter for 100 s of work: 200 metal. The **Foundry Works** blueprint is the same with a Foundry and two bins feeding it: tip metal into one and stone into the other.
- **Chute ports.** The machines have named ports on their outside, `Chute in` and `Chute out`, both on the +x side, so a chute needn't reach a building's middle. A port takes any number of chutes.
- **Network (protocol 114; 117 for the mixed input).** Bit 4 of a store's flags byte: a machine's input follows (u16 amount, u8 material, u8 refused
  material), only when it holds something or a chute is refused, so other stores cost the same 5 bytes (plus their slots). Bit 64: a mixed input follows (u8 kinds, then u8 material and u16 amount). The job's
  progress is server-only.
- **Performance.** The step (`ConstructRefining.Step`) runs at 10 Hz and looks only at constructs with a machine
  (`Construct.HasRefiner`). A recipe is an array lookup behind a dictionary; nothing allocates.

Code: `Source/Constructs/ConstructRefining.cs` (the step, the look line), `ConstructRefiningTable.cs` (the table), `ConstructStorage.cs`
(`Feed`, the fill box, the bytes). **Self-test**: "Refining" and "Refining network" (`Source/App/SelfTestRefining.cs`).

Choices made (the developer may change them): the numbers above; the input can't be taken back out by hand (run it through, or break the
machine); a machine takes one input material at a time, so a stray unit short of a batch waits for its fellows; a refusal shows on the
machine's look line, not the source bin's; no named chute ports; pipes carry no bulk yet, only chutes do; the Sawmill has no blueprint of
its own.

## Extrusion

The developer's mothership loop: a machine that makes long pieces to length, for building panels, frames and shapes. An
**Extruder** pushes bulk material through a die; what comes out is a real, parametric piece; a signal into its **Cut** input, or
its Cut every length, parts it off at the die. Code: `Source/Constructs/ConstructExtrusion.cs` (the pieces), `ConstructExtruder.cs` (the machines),
`ConstructExtrusionTable.cs` (`Constructs/Extrusion.json`). The parts and the Extruder Line blueprint come from `tools/constructs/extrusion_parts.py`.
Self-test: "Extruder", "Extruder network" (`Source/App/SelfTestExtruder.cs`).

- **A piece** is a cross-section swept along a length: an 8 by 8 cell grid (a u64: bit y * 8 + x), a width and height (0.1 to 32 m),
  a length (0.05 to 192 m, in 5 cm steps, so both the 0.1 m and 0.25 m lattices can be hit) and a material. That is all it is
  (`ExtrusionKey`). The grid is meshed into at most 16 solid boxes, the same way on every machine; a solid bar is one box, an
  I-beam three. Presets: Solid bar, Plate, Wall (a skin each side on three studs), Hollow box, I-beam, C-channel, L-angle, T-bar, Cross;
  Custom is the panel's own grid. A section must be connected and mesh into 16 boxes; each refusal says why.
- **Round and Pipe** (the Profile's last two choices). The key's `Shape` is `Round` or `Pipe`, with no grid: Width is the diameter (`W` and `H` both hold it,
  and the die must take it both ways) and a Pipe's **Pipe wall** setting is `Wall`, in cm, 1 to 200, always leaving a 5 cm bore. Mass, health and material use follow the real
  area. **Collision.** A finished Round is a convex hull of its drawn fourteen-sided prism over three hidden boxes; while it grows it is the boxes, since a hull is made once and
  a growing piece is a new def four times a second. A Pipe is 32 hidden boxes round the bore, `ColliderBoxes` set so the tube's outer hull is not made: the bore is open to rays,
  bodies and players, who can walk inside a big one. Boxes don't turn, so the ring is stepped: true on the axes, about 9 % of the radius proud of the circle and into the bore on the diagonals. **Sockets.**
  `base` and `tip` in the middle of each end (a pipe's stand in the bore's air: a weld to them is a weld to the end), and the four faces at the same stations, at the compass points of the round.
  **For later machines.** The key holds all a die needs to read a piece: `Shape`, `W`, `H`, `Wall`, `Mask`, `L`, `Material`.
- **A piece is a real part.** `Extrusion.Def(key)` makes a `ConstructPartDef` the first time a key is asked for and keeps it: ids from
  `Extrusion.RuntimeBase` (0x4000), resolved by `ConstructParts.Def`, never sent. So `Rebuild`, split, join, the renderer, rays, player
  blocking, ghosts, joins, damage and paint read a piece's boxes, sockets, mass and health as they read any part's, with no second path.
  `ConstructParts.All` doesn't list them, so menus, racks and part sweeps see only the files. Bounds are the real section's; collision
  and hits are its solid boxes through `Construct.SolidCount/SolidBox`.
- **Numbers.** `Constructs/Extrusion.json` by material: `unitsPerM3` (mass is that at the material's `kgPerUnit`, so a piece weighs what
  went in), `healthPerM3`, `armourPerCm` of its thinnest wall up to `maxArmour`, and `class` (salvage, hits, burning). A row may name a
  material Recycling.json doesn't have yet (steel, concrete): it is simply not usable until it does. Metal: 200 units/m3, 400 kg/m3.
- **Sockets.** A finished piece has `base` and `tip`, and four faces (`+x0`, `-x0`, `+y0`, `-y0`...) at up to nine stations about 1 m
  apart (further on a long piece), 38 at most, each on the outermost cell of that side nearest the middle. Point and freeform attachments reach everywhere else. A
  piece is physics-only ("extruded"): attach tool, physics gun, cargo volumes.
- **The Extruder** (`Constructs/Parts/Extrusion/Extruder.json`, `storage.extrude`): a bin (`any`, 400) with a die at its +z face, 0.2 m/s, 900 W
  while it runs. Material comes by chute or F; Run and power as a [production machine](#refining-decision-14) (`ConstructGatherer.HeldOff`,
  `ConstructRecycler.Powered`; creative needs no power but still needs material). Its output wire is the length so far, in metres.
  Configure tool: Profile, Width, Height, Pipe wall, Cut every, and the cell grid (four `mask` settings drawn as one grid: click a cell and what is shown
  becomes Custom; a Round or a Pipe shows on it too). A change of setting is for the next piece; the panel previews the section on the die.
- **By size class.** The limits are part data.

  | Machine | Die | Longest piece | Speed | Bin | Power |
  | --- | --- | --- | --- | --- | --- |
  | Extruder (Small) | 0.5 m, Die Frames to 6 x 6 m | 48 m | 0.2 m/s | 400 | 900 W |
  | Medium Extruder | 8 x 8 m | 64 m | 0.4 m/s | 2,000 | 3.6 kW |
  | Large Extruder | 16 x 16 m | 128 m | 0.8 m/s | 8,000 | 14.4 kW |
  | Capital Extruder | 32 x 32 m | 192 m | 1.6 m/s | 32,000 | 57.6 kW |

  The big machines are their die (a block of their class's cells, `sizeClass` sockets) and take no frames. 192 m keeps a piece inside `ConstructParts.MaxPartReach` (200 m). A piece's mass and health stop at `MaxPartMass`
  and `MaxPartHealth` (50,000 t, 16,000,000): past those a huge solid piece still costs what went in, but weighs and takes hits as the limit does. The table holds 48,000 pieces.
- **Die Frames** (`"dieFrame": true`; Die Frame 0.5 x 1 m, Large Die Frame 1 x 2 m). The small Extruder starts with a 0.5 by 0.5 m die. Attach frames to it (and
  to each other) on any side: the die reaches across every working frame joined to it, up to 6 by 6 m, and its centre moves with
  them (`ConstructExtruder.Die`). The section is held to the die, and the look line says to add frames.
- **Growing.** The piece is a part of the machine's own construct, linked to its `die` socket (kind `die`: nothing else fits), its origin
  at its base end, so it rides with the machine (a mothership included), counts in its mass and is solid. Everything is found from that link
  (`ProductOf`), so nothing has to be kept in step through renumbering, splits or the wire. At 10 Hz the step adds up
  growth; each 5 cm swaps the part's def for the longer one and calls `Construct.RefreshShape` (mass, bounds, contact points; no routes, power, drive line or
  guns), and sends a 5-byte `CBodyKind.Extrude`. At 0.2 m/s that is 4 swaps a second. A shape is made once, so a line in steady work allocates only the bodies it cuts off.
  A growing piece has one socket (the die's), so nothing can be fitted to it, and joins onto it are refused. A piece no longer linked
  to a die (taken off, the machine gone) finishes at the next rebuild, with its full sockets.
- **Its path.** Each step checks the new slab, box by box (an I-beam's notch stays a notch): the ground, walls, its own construct and anything
  anchored stop it ("blocked"). A free body is pushed along the path (`pushForce`, 6 kN, at most 0.6 m/s) and growth waits for it to go, so
  nothing is ever grown into; one that doesn't move in 1.5 s stops it ("too heavy to push").
- **Why it stopped** is the store's `Refused` byte (`ExtrudeStall`, on the wire): no power, needs material, can't extrude that, the bin holds another
  material than the piece, the section can't be made, at the cap, blocked, too heavy. The look line has the bin, the die, the section, the length so far, the
  material use and the reason.
- **The cut.** The machine's second input, **Cut**: a rising signal (caught at the logic's rate, so a one-tick pulse counts) parts the whole piece off at the die at its next step. It needs power; it works with Run held off.
  **Cut every** (`cutLength`, metres, 0: off) does the same with no logic when the piece reaches that length, and growth stops there, so lengths are exact at any speed. The piece comes off as a body of its own where it stands,
  moving as the machine moved, keeping its health share and paint, and the part leaves the machine's construct (a full rebuild of that construct, once a cut). The next piece starts from the settings as they stand. Until it has fallen or been taken away a cut piece is
  in the next one's path: it is pushed along as any free body is. Without a cut a piece grows to the cap and stops.
- **On the wire (protocol 143).** A piece in a Full's part list is a mark (0xFFFE) and its key, 10 bytes with a preset section, 18 with a custom one, 10 with a round or a wheel and 12 with a pipe; ids
  are each machine's own, so `StructureHash` mixes the key's hash. `CBodyKind.Extrude` carries width, height and length. A client out of table room (`Extrusion.MaxDefs`, 48,000 pieces in a run) shows a stand-in, and a server
  there stalls with "too many different pieces".
- **Blueprints and saves.** A piece names itself: `"part": "Extrusion i-beam 25x25x400 metal"` (or 16 hex digits for a custom section), `Extrusion round 50x50x400 metal`, `Extrusion pipe3 50x50x400 metal` (a 3 cm wall) or `Extrusion wheel 30x100x0 rubber`
  (30 cm wide, 1 m across) in any step, map list or world save. `base`, `tip`, the face names and a wheel's `axle` are its sockets. **Extruder Line**: a Foundation, an Extruder, a Battery, a Materials Bin chuted into the hopper, and a Timer wired to the Extruder's Cut.
- **The Cut-off Saws are gone** (2026-11-04). A saved world holding one loads without it: the world's log says "left out N x Cut-off Saw", and what only the saw joined to its construct splits off. A blueprint naming one is refused at that step with the part's name, as any unknown part. The saw's job is the Cut input and Cut every.

Choices made (the developer may change them): the cut is at the die and takes the whole piece (a saw could stand anywhere along the path, and left the rest on the die); Cut every moved from the saw to the Extruder rather than going, so a line needs no logic; a def per length rather than per-part data (the alternative needs every reader of boxes and sockets changed, and the
table is cheap: one line is at most 640 lengths of a section); an 8 by 8 grid (a u64, a simple panel; finer needs a second word); 5 cm steps; the piece on the machine's
own construct, not a body of its own (so it uses one of the construct's 16,000 parts, and growing on a free vehicle gives no reaction); a stalled piece keeps its length; the table of limits by class;
one Die Frame turned to suit rather than a wide and a tall one; a Die Frame counts in any turn; until feat/collision merges, a moving piece touches the map and fixed bodies only with its bounds corners, as every body does (something moving into a fixed piece does meet its real boxes: a post stops at an I-beam's web; the compound collider gives the moving piece the same); tiers aren't built, but `speed`, `die`, `maxSection`, `maxLength` and `pushForce` are part data.

### Run-time parts and their slots

Every part made by key at run time (extrusions, lofted bays, cabs, hoods, panes, trims and fittings, ordnance designs, racks, parts of another material) takes a slot of
`Extrusion.MaxDefs` (48,000 ids from `Extrusion.RuntimeBase`), and the shapes its boxes draw (a lofted part's hulls, its lamps) take slots of `PartShapes.Cap` (65,535: a
ushort on every box, gun piece and scene node). A generated vehicle leaves about 9 parts and 165 shapes behind (the first spawns of a process about 30,000 shapes of warm-up),
so a session spawning a couple of hundred, or a tool over every style, used to run out ("too many different part shapes"). `RuntimeParts` (RuntimeParts.cs) frees them:

- **What is kept.** A sweep marks every part held by a construct, a storage slot or a hand (`Held`, `HeldGone`, `MagFor`) in every `ConstructWorld` and `ConstructEdits`
  of the process (both register themselves), and every part made or asked for by its key in the last 2 minutes (`ConstructPartDef.Touched`, stamped under the key table's lock).
  The rest leave their key tables (asked for again, they are made again, with a new id), their slots and their shapes go back.
- **Shapes are counted.** `ConstructParts.Parse` counts the shapes it asks `PartShapes.For` for (`OwnShapes`); a shape asked for outside a parse (weapon parts, an
  extrusion's unit lathes, the renderer's own) is pinned for good. A mesh nobody holds is dropped and the renderer re-uploads (its version goes up).
- **A freed slot is taken again only once every slot has been used**, oldest first, and the old def stays behind its id until then; caches that know parts by id
  (the mirror's partners, the ammo fit cache, the merged-mesh box counts) are emptied at each sweep. Ids never go on the wire or in a save (keys do), so each machine frees on its own.
- **When.** The game sweeps between ticks (`RuntimeParts.Tick` in Program's loop) when either table is past half full, at most every 30 s (or at once past 90%), and logs
  `[parts] swept in N ms`; past 80% one `[parts] warning` line a minute. Tools that make many vehicles sweep with no grace between them (`--body-report`). Nothing is
  allocated on a frame without a sweep.
- `--bench-parts [N] [--no-sweep] [--net K]` checks it: N generated vehicles built and taken out offline, then a host and a client over RakNet (120 ms, 3% loss) with both
  limits lowered so slots are taken again, each client copy compared by part names, and a vehicle kept throughout.

## The Wheel Former

An [Extruder](#extrusion) with a hub for a die: it builds a wheel up on the hub, in place, fed with material the same way, and a Cut signal parts the finished wheel off. Code: `ConstructExtruder.GrowWheel`, `ConstructExtrusion.BuildWheel`; the numbers are the `wheels` rows of `Constructs/Extrusion.json`; the parts come from `tools/constructs/extrusion_parts.py` (`storage.wheel`). Self-test: the Extruder sections.

- **Run modes.** **Width** builds along the axle, **Depth** builds the diameter (depth reads as how far the wheel stands out from its hub: both radii together, so a mode's step adds 5 cm to the diameter). The **Run mode** setting picks one; a wire into the **Mode** input decides instead (on: Depth). Each may be changed at any time; the wheel keeps what it has. The inputs are **Run**, **Cut** and **Mode**. The output wire is the wheel's size in the running mode, in metres, so a Compare wired from it to Run or Cut stops a wheel at a size. A wheel at a limit says so on the look line and waits. The first wheel is paid for in one go, so it fits in a bin.
- **Limits.**

  | Machine | Widest | Biggest across | First wheel | Speed | Bin | Power |
  | --- | --- | --- | --- | --- | --- | --- |
  | Wheel Former (Small) | 1.5 m | 3 m | 0.1 x 0.4 m | 0.1 m/s | 400 | 900 W |
  | Medium Wheel Former | 6 m | 12 m | 0.5 x 2 m | 0.2 m/s | 2,000 | 3.6 kW |
  | Large Wheel Former | 12 m | 24 m | 1 x 4 m | 0.4 m/s | 8,000 | 14.4 kW |
  | Capital Wheel Former | 20 m | 48 m | 2 x 8 m | 0.8 m/s | 32,000 | 57.6 kW |

  Each hub stands high enough for its biggest wheel over the machine's own floor. A wheel that meets the ground, something fixed or its own construct stops with "blocked"; a free body is pushed out from the hub.
- **What it makes** is a run-time part as an extrusion is: key `Shape = Wheel`, `W` its width, `H` its diameter, `L` 0, in 5 cm steps. It is one def per size, at any material in `wheels`: rubber (grip 1.0, flat when broken, like a road tyre), metal, steel, wood, planks, stone, concrete. Mass is the cylinder's volume at the row's `unitsPerM3` (mostly air and rim: a rubber wheel 0.3 x 1 m is 39 kg), and the cost is what went in. A wheel's origin is on its axle face, so width grows away from the hub's plate and only the def changes. Off the machine it has an `axle` socket (the size class's by diameter: under 2 m small, under 7 m medium, under 24 m large), and all a wheel's numbers: `CWheel` reads its radius and width from the def and bounds, its cylinder collider follows, and it has the Tyre settings of every wheel. Loose, it collides as three hidden boxes (as every wheel does). **Growing**, it is the hidden boxes too (`ColliderBoxes`): a growing wheel is a new def every 5 cm, and a cylinder's convex hull made for each cost 6 ms and 60 KB inside the body's collider rebuild (2026-11-11). It is physics-only: the socket weld puts it on a suspension's axle.

Choices made (the developer may change them): depth as the diameter; the `wheels` rows; the limits above; the machines' size; a wheel as one solid material (no rim and tyre of two); no Interact to cut (a Button wired to Cut does it).

## The Press

The user's "complex parts made in the world, not in menus". A **Press** stamps parts out of extruded pieces; it sits beside the Extruder and the Wheel Former, and the [Assembler](#assembling) stays for everything no die covers yet. Code: `Source/Constructs/ConstructPress.cs`, `ConstructPressTable.cs` (`Constructs/Pressing.json`); the parts, the table and the Press Line blueprint come from the part kit's `--gen-parts press_parts`.

- **Modular by size.** Press, Die Lathe and die each come in the four size classes, and a die fits a press of its own class (socket kind `pressdie`, the class of the socket). The Small press is 1.75 m wide with 2 bays and takes pieces to 0.45 m across; Medium, Large and Capital have 3 bays and take 1.8, 7.2 and 28.8 m, at speed x2, x4 and x8. Length sets count, so the player builds the smallest system for the job.

  | Machine | Bays | Across | Stroke speed | Power |
  | --- | --- | --- | --- | --- |
  | Press | 2 | 0.45 m | x1 | 900 W |
  | Medium Press | 3 | 1.8 m | x2 | 3.6 kW |
  | Large Press | 3 | 7.2 m | x4 | 14.4 kW |
  | Capital Press | 3 | 28.8 m | x8 | 57.6 kW |

- **What is in it is a part.** A bay and the die slot are sockets, and a piece or a die in them is a part of the machine's own construct, linked to that socket, as a piece on an Extruder's die is. So it counts in the mass, is solid and drawn, and goes through the wire, world saves and blueprints with no second path; `ConstructPress.PieceIn` and `DieIn` find it from the link. A piece in a bay keeps its `Growing` key (no sockets; nothing welds to it) and stands out of the bay's mouth. It has to fit where it would stand.
- **Fed by hand.** Let a loose piece go with an end in a bay's mouth (`bays[].min/max`, checked at 10 Hz) and it is drawn in. A die: F with it in your hands, in the same way the attach tool would fit it to the slot (the die in there comes back to your hands), or a physics-only die (Large, Capital) let go in the front of the machine. F empty-handed takes out what is nearest: a piece slides out of its mouth, and isn't taken in again for 3 s. Look line: `Press: Button Die - bay 1: wrong material, wants laminate plate - F: take the die out`.
- **Stock always comes out.** What a press, a Die Lathe, an Extruder or a Wheel Former holds comes out in every state: too short, the wrong die or none, no power, held off, a stroke under way. Aim at it (the crosshair lands on the piece, not the machine: `ConstructPress.MachineOf`) and press F, or take it with the physics gun, which draws it out and holds it; on an anchored machine too. F on the machine takes the nearest. It comes back whole, at its length with its health and paint; a stroke takes length only when it ends, so one under way just stops and uses nothing. A piece shorter than a stroke is refused at the mouth ("too short") and stays out; the stub a run leaves stays in its bay until taken. A piece taken out isn't drawn back in until it has left the mouth or been picked up (`Construct.TakenFrom`).
- **Fed by chute.** A chute from an Extruder to a press takes each finished piece whole: at Cut, or at Cut every, the piece waits on the die (`ExtrudeStall.ChuteWaits`) until a chute move finds a bay that will have it, then moves. A chute from a shelf or a Die Rack brings a die of the press's size to an empty slot. A chute of bulk is refused. The press doesn't name its ports: it picks the first free bay that takes the piece. What a stroke makes goes out of the tray by chute as any slots do.
- **Pressing.json.** `rows` is flat: one row per die, material and size band. A row names `die`, `output`, `count`, `seconds` and its `bays`, each with `label`, `sections` (Extrusion presets, `round`, `pipe`), `materials`, `minAcross` and `maxAcross` (the piece's smaller side), and what a stroke takes: `use` (m), for parts of a fixed size, or `useAcross` (times its size across). The first row whose bays are all satisfied runs. A row of several bays names them in order; a **one-bay row** (Bracket, Housing) is every bay's: each bay takes such pieces, and a stroke works the first whose piece is ready (`ConstructPress.BayOf`, 2026-11-11). Rows of a die stand together; a die has no list of its own, so 15 dies times materials times sizes is only more rows. `lathe` rows are chosen as a setting.
- **What the die, the material and the piece do.** The die sets the family; choosing a recipe is swapping it. One die covers every material of its family. Fixed-size parts take their count from length: at `use` 0.1 a 2 m strip is 20 strokes, and a piece with less left than a stroke stops with "too short". Parts with no fixed size follow the section: the rows' bands pick the output, so a 0.15 m bar in a Housing Die makes Housing 0.25 m and a 0.3 m bar Housing 0.5 m.
- **Stroke.** A row's `seconds` at the machine's speed. Power, Run wire and creative as a [production machine](#refining-decision-14); a body that can't leave waits with the pieces untouched. Output in the tray, 12 a slot; what no tray holds (physics-only, or too big: brackets, housings, dies) rides out on a conveyor from its **Piece out** to a Piece Rack, whole strokes only (it waits with "Output conveyor full", "Too big for its conveyor" or "Output conveyor cut"), and drops as a body at the front only when nothing is linked there.
- **The first set** (33 rows):

  | Die | Bays | Makes | By |
  | --- | --- | --- | --- |
  | Button | laminate plate + copper round | Button | length: 10 cm + 5 cm a Button |
  | Board | laminate plate (20 cm up) + copper round | Circuit Board | length |
  | Bracket | metal, steel or bronze L-angle or plate | L Bracket, Corner Bracket, Gusset 0.25 m, Gusset 0.5 m | section |
  | Gear | steel round | Gear Set | length |
  | Piston | steel round + bronze bar | Piston Set | length |
  | Housing (4 sizes) | metal, steel or bronze hollow box or bar | Housing 0.25 to 16 m, 7 sizes, a set in each material: 21 new parts | section, material |
  | Cylinder (4 sizes) | steel pipe + steel plate | Small to Capital Cylinder | section |

  The rest of the Assembler's list stays with the Assembler. **Substitutions:** the game has no plastic or aluminium, so plastic is laminate and aluminium is bronze; a strip, sheet or copper wire is a plate or a 10 cm round (`Extrusion.MinSectionCm`). Laminate and bronze got rows in `Extrusion.json`.
- **Tiers.** Material sets the tier only where the game has a part per tier. Housings do, since they are new. Bracket, Gear Set, Piston Set, Button, Circuit Board and Cylinder have one part each, so those rows take the materials that suit and give that part.
- **Dies.** Parts in `Constructs/Parts/Press/Dies`, `"pressDie": "Button"`, first socket the mount. Small and Medium are items (on a Die Rack or a shelf, in hands); Large and Capital are physics-only. Starter set, free with a survival start (`SurvivalStart.json`): Button, Bracket, Gear. All the rest come from a Die Lathe, not the Assembler.
- **The Die Lathe** (`"press": {"lathe": true}`) is the press's code with one bay and no slot: a setting chooses the die (`"config": {"recipe": "Board Die"}`). It takes a solid bar: metal or steel for a starter, steel otherwise, at least the die's own size across (0.25, 1, 4, 8 m), and a stroke uses that length, so a 1 m block cuts four Small dies. A lathe cuts dies of its own size class ("Wrong size of lathe").
- **Refusals.** A piece is turned away at the bay and stays where it is. The panel and the look line say why (`BayRefuse`: no die, wrong material, section, size, no room for its length, wrong die size, bulk). It rides in `CStore.Blocked` (reason x 4 + bay, 3 for the die slot) and clears after 5 s or when a die goes in.
- **Performance.** `ConstructPress.Step` at 10 Hz looks only at constructs with `HasPress`; a bay's mouth is a `Near` query only while the bay is empty. Nothing allocates but the bodies it makes.
- **Wire (protocol 144).** No new message. Parts and links carry what is in it, `CBodyKind.Extrude` a shorter piece; the stall is `Refused`, the refusal `Blocked`.

Choices made (the developer may change them): one part a press, not a frame you add bays to; a Small press has 2 bays and no recipe uses a third yet; a die only in a press of its own class, never a bigger one; a piece's size is its smaller side; a stroke's cost is a length, not a mass, so a stamped part doesn't weigh what it used; the Housing family is new, with no use yet beyond a box with sockets, and the Assembler can build them too; chutes carry pieces only from an Extruder (a shelf can't hold a piece, since a piece is physics-only); the ram strokes while it stamps (a "work" box, drawn only: [Machine look](look.md#machine-look)).

## Machine tiers

Each of the six machine lines comes in five tiers, one for each step of the threat ladder (zombies, bandits, military, the
sci-fi faction, each giving better loot): **1 Scrap, 2 Workshop, 3 Industrial, 4 Military, 5 Plasma**. Tier 1 has the plain name
(Drill Module, Saw Module, Sawmill, Smelter, Foundry, Assembler); the others are named after their tier ("Industrial Drill Module", "Plasma Smelter").

The gathering modules are vehicle-sized. The drill's bin is a small buffer ([The drill's buffer and shaft](gathering-and-salvage.md#the-drills-buffer-and-shaft)), so the Bin column is the Saw Module's:

| Tier | Size | Solid body | Drill u/s | Saw u/s | Bin | Chute move | Power | Mass, health | Armour |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 Scrap | x1 | 1 m (drill 0.5 m) | 10 | 12 | 400 | 10 | 900 W | x1 | x1 |
| 2 Workshop | x1.25 | 1.25 m (drill 0.63 m), 1.5 m at its pads (drill 1 m) | 20 | 24 | 800 | 20 | 1,620 W | x1.5 | x1.5 |
| 3 Industrial | x1.5 | 1.5 m (drill 0.75 m, 1 m at its pads) | 40 | 48 | 1,400 | 40 | 2,880 W | x2.25 | x2 |
| 4 Military | x2 | 2 m (drill 1 m) | 80 | 96 | 3,200 | 80 | 5,040 W | x4 | x3 |
| 5 Plasma | x2.5 | 2.5 m (drill 1.25 m, 1.5 m at its pads) | 160 | 192 | 6,400 | 160 | 8,640 W | x6.25 | x4 |

Everything else is a building, large and unwieldy at every tier: a truck can't sensibly carry one, so production wants a base, a dedicated heavy carrier or (several of them) a mothership.

| Tier | Speed | Bin, input | Smelter | Sawmill | Foundry | Assembler |
| --- | --- | --- | --- | --- | --- | --- |
| 1 Scrap | x1 | 400, 200 | 3.5 m, 3.5 m tall, 5.1 t, 3 kW | 3 m, 3 m, 2.2 t, 1.8 kW | 4 m, 3.9 m, 7.7 t, 3.5 kW | 2 m, 2 m, 0.6 t, 1.5 kW |
| 2 Workshop | x2 | 800, 400 | 4.5 m, 6.3 m, 8.5 t, 5.4 kW | 4 m, 4.5 m, 3.8 t, 3.2 kW | 5 m, 6.1 m, 12 t, 6.3 kW | 2.5 m, 2.4 m, 0.9 t, 2.7 kW |
| 3 Industrial | x4 | 1,400, 700 | 6 m, 8.4 m, 15 t, 9.6 kW | 5 m, 5.6 m, 6 t, 5.8 kW | 6.5 m, 7.9 m, 20 t, 11 kW | 3.5 m, 3.9 m, 1.8 t, 4.8 kW |
| 4 Military | x8 | 3,200, 1,600 | 8 m, 6.6 m, 27 t, 17 kW | 7 m, 6.4 m, 12 t, 10 kW | 8 m, 6.6 m, 31 t, 20 kW | 4.5 m, 3.6 m, 3 t, 8.4 kW |
| 5 Plasma | x16 | 6,400, 3,200 | 10 m, 13 m, 42 t, 29 kW | 9 m, 9.9 m, 19 t, 17 kW | 10 m, 12 m, 48 t, 34 kW | 6 m, 5.8 m, 5.4 t, 14 kW |

A unit costs 90 J from a tier 1 drill and 54 J from a tier 5, 10% less a tier. A module's bin holds what its volume holds (size cubed, rounded), so
it takes about 40 s to fill at every tier. A machine's mass and health go by its size squared: 420 kg (Smelter), 240 kg (Sawmill), 480 kg (Foundry) or 150 kg (Assembler) a square metre of width, and 800, 500, 900 or 500 health.
Fitting time grows from x1 to x2.

- **A building, not a brick.** Boxes are in units of the machine's width (`BIG_WIDTH`), so every socket lands on the 0.25 m grid and a building has no pads. Each has a floor plate you step onto (2% of its width), stepped solids and
  open space: a Smelter or Foundry has an aisle across it, between the furnace and the tray, wide enough to walk through at every tier; a Sawmill is two open bays and a saw frame; an Assembler is an open frame with a low hopper at the back. Less than 55% of the bounds is solid.
- **Reach.** A part wider than 3.2 m a side (`ConstructEdits.BigPartHalf`) is reached at the point of its bounds nearest the eye, not its centre (`ConstructEdits.ReachPoint`): the attach tool, paint, configure tool, F and the look ray agree, and you use a 10 m machine from beside it. A smaller part keeps the old rule.
- **Spawning.** A part this size spawns clear of the player's corner distance and lifted out of the ground (`SpawnInFront`). The creative physics gun lifts up to 60 t; in survival the cap starts at 5,000 kg ([tool upgrades](../TOOL-UPGRADES.md) raise it to 80 t), so a heavy machine goes on by the attach tool or stays where it was built.

- **Data.** `"tier"` (1 to 5, default 1) in a part file, on any part; `storage.gather.rate`, `storage.refine.speed` (a multiplier on
  a recipe's `seconds`, default 1) and `storage.bulk.chuteLoad` (default 10, at least 10) carry the numbers.
  the part kit's `machine_tiers` (`Source/PartKit/Generators/MachineTiers.cs`, `--gen-parts machine_tiers`) writes all 40 files from one
  table per line and `TierStyle.cs`: fix them, not their output (`--check` compares). The files are `DrillModule.json`, `DrillModuleT2.json` and so on.
- **Size.** Every box, bin and work box grows by the tier's size from the part's origin, the centre of its bottom socket. Collision,
  rays, the outline, ghosts and a player's push all read those boxes, so they follow. Only what hangs below the floor keeps tier 1's
  length: the bit is thicker but still reaches 1.2 m under the part, so a tier 5 drill suits the ground clearance a tier 1 does, and a bit
  still has to be within 25 cm of the ground. A hole is the same at every tier (radius 1.4 m, depth 1.5 m); `DrillShaft`
  reads the bit's world height, so it needs no change. A saw's blade and work box grow in full, so it reaches further
  (3.1 m round the work box's centre at tier 5).
- **On the grid.** Sockets stay on the 0.25 m frame grid: scaled, then snapped. A tier 2 module's 1.25 m body falls between two grid lines, so its
  side and back sockets stand on small solid mounting pads at 0.75 m (a drill's are at 0.5, 0.5, 0.5 and 0.75 m). Bottom sockets stay at the origin, side and back ones on the
  outer faces. A bit overhangs its base at every tier, so a drill goes at a base's edge or on a
  frame. A building needs a 4 m slab at least, and a tier 3 or bigger overhangs that: its bins and battery go on further slabs, clear of it and within a chute segment (8 m) of its ports.
- **The look and the shape.** Each tier of each line has its own solid body, proportions and details, written in the part kit's `TierStyle.cs`
  (`Palette`, and `Look(name, tier)`: `solid`, `detail`, and `hopper` when the body moves the input bin up or down); the numbers stay in `MachineTiers.cs`. A line reads as itself at every tier, and a tier reads the same across lines:
  - **Scrap**: lashed up. Rust in uneven tones, mismatched plates, timber, bolted-on drums. A timber derrick, a slatted crate, a trestle mill, a squat kiln, two odd kilns, a timber frame.
  - **Workshop**: built by a craftsman. Blue paint on cast iron, cream trim, rivets. A pillar drill, a square bin with a motor, a cast mill frame with a flywheel, a brick-bodied furnace with one stack, a matched pair of towers, a machine-tool frame.
  - **Industrial**: tall and busy. Concrete grey and safety yellow, pipes, valves, hazard stripes, ladders. A stepped tower with landings, a raised cab and twin arms, a full-width gantry, a blast furnace with a stepped ramp, a stepped tower pair and pipe bridge, a bridge crane.
  - **Military**: low and hard. Olive armour on slopes, pale stencil bands, sandbags, cages, no bare pipework. A casemate with a thin mast, a hull with a wedge nose, a low blade housing, a bunker furnace, a long bunker with stub stacks, a low shelter.
  - **Plasma**: clean curves. Dark rounded shells, glowing strips and rings, a violet core. A slim pod on a neck, booms with ring blades under a capsule roof, a standing ring round the blade, a ring chamber round the core, twin capsule pods, a slim frame with a ring for the arm.

  Fixed at every tier: the rates, bins, power, mass, health, armour, widths, the bit's reach, every socket and port, the trays, and the aisle. A building's height is a range of its width (`TierHeights` in the self-test); a drill's top is always its shaft socket. A big shape is a solid box drawn as a detail shape, the box itself `"hidden"`, so the collider stays boxes. Every machine says `"collider": "boxes"`, so with the compound collider a wedge or cylinder detail stays a look.
  **"Machine tier looks"** checks that tiers of a line differ pairwise in solid shape (cells of its own size) and in colour, that each keeps the line's signature, and that details stay clear of sockets, ports, bins, trays, aisles and the assembler's open front and near the machine.
  `"glow": true` on a part box draws it unlit and bright (never paintable here). `"front"` on a shaped box (`+x`, `-y`...) says which way its front end points, so a cylinder can stand up or a wedge slope any way; the box is still the one it fills. A part takes up to 256 boxes (it was 32 while each box was a draw; static parts are merged meshes now, docs/render/MERGED-MESHES.md), and a box may reach 16 m from its origin
  (`ConstructParts.MaxPartReach`), for the tier 5 stacks. A box may show its machine working (`"work"`: spin, stroke, glow, smoke; [Machine look](look.md#machine-look)); the tiers here do not use it yet.
- **The gate.** A deposit or a recipe may ask for a tier: `"minTier"` in `Constructs/Resources.json` and `Constructs/Refining.json`,
  default 1. Iron, stone, sand and wood are tier 1; copper is tier 2; gold is tier 3. A drill or saw below it gathers nothing; the look
  line says "gold vein needs a tier 3 drill, this is tier 1" and still gives the nearest deposit it can work. A machine
  below it takes nothing: a chute's load stays in the source, F passes over that material, and the look line says why
  ("chute refused: copper ore stays in the source, it needs a tier 2 smelter, this is tier 1"; an empty one, "takes iron ore, sand (copper
  ore needs tier 2, gold ore needs tier 3)"). A drill taps the strongest deposit under it with anything left, so one over iron
  and gold where gold is stronger refuses, however much iron is there.
- **Rates in a step.** The 10 Hz steps add up exactly: a module's units are thousandths, a machine's work is thousandths of a
  tick, and a fast machine finishes several batches in a step. A step takes no more than the bin has room for, so nothing
  tapped is lost.
- **Chutes.** A move carries the larger `chuteLoad` of its two ends, so a chute keeps up with the machine it serves and two
  plain bins still move 10 a second.
- **The wire.** Nothing changed (protocol 114): a part's tier and the tables are data on every peer, and the refused material was already
  on the wire, so a client works out the same look line.
- **The inventory.** All 30 are in the **Production** category, sections Drills, Saws, Sawmills, Smelters, Foundries and Assemblers, in tier order, with the sub-components in Components.

Not yet (choices made, the developer may change them): tiers cost what their recipe says in survival, but nothing unlocks them, since scanning isn't built;
a full tier 5 bin weighs what it holds (6,400 iron ore is 25.6 t, gold ore 51 t) and nothing
stops a truck taking that on; a drill's hole digs faster at a higher tier but is the same size; the Recycler has no tiers. **Self-test**: `Source/App/SelfTestMachineTiers.cs`,
run from "Gathering", "Gathering network", "Refining" and "Refining network".

## Assembling

The step after refining: an **Assembler** builds parts from what the chains make. Code: `Source/Constructs/ConstructAssembling.cs`, `ConstructAssemblingTable.cs`; generator: the part kit's `--gen-parts assembling`.

- **The part** (`Constructs/Parts/Assembling`, attach tool, physics-only, five tiers: [Machine tiers](#machine-tiers)): an open gantry 2 to 6 m wide, with a low hopper at the back (its input), a tray at the front (its output) and an arm. `storage.assemble` holds `capacity` (the fill's scale), `speed`, `chuteLoad`, `batches` (how many batches of each input it buffers: 4 a tier), and `outAt`, `outDir`, where a body leaves. Its `slots` are the tray.
- **The recipe** is a setting (`"config": [{"key": "recipe"}]`): a choice filled from the table as the parts load, so the configure tool lists every recipe (a filter box stands over a long list) and a blueprint step names one (`"config": {"recipe": "Engine"}`). 0 is none.
- **The table** (`Constructs/Assembling.json`, format `call-of-slop-assembling/1`; `AssemblingTable`). Each recipe gives `output` (a part), `count` (default 1), `materials` (material: units), `parts` (part: count), `seconds` (at speed 1) and optionally `minTier`. At most 6 inputs, one recipe a part. A physics-only part can't be an input, since no chute or hands can bring one, so a chain goes through **components**. A refused file gives no recipes and the reason (`--check-construct-parts`).
- **Components** (`Constructs/Parts/Components`, items, in the parts menu's Production category): Gear Set, Bearing, Piston Set, Copper Coil, Wiring Loom, Circuit Board, Control Unit, Servo Motor, Tyre Carcass, Steel Plate, Lens.
- **Written and generated.** the part kit's `--gen-parts assembling` holds 46 recipes by hand (every component, and what a player reaches for first) and generates 437 for the long tail from the part files, by rule: half a part's mass in its class's material, what its role must have (an engine's Piston Sets, a gearbox's Gear Sets, an actuator's Servo Motors, a logic part's Circuit Board), and a tier from what that needs. Spawners, the Construct Core, a chest's lid and damage stages have none, nor do the weapon builder's generated parts. Fix the tool, not its output (`--check` compares).

| Example (by hand) | Takes | Seconds | From tier |
| --- | --- | --- | --- |
| Gear Set | 5 steel | 4 | 1 |
| Tyre Carcass | 4 rubber + 2 cloth | 5 | 1 |
| Wheel | 2 steel + 1 Tyre Carcass | 5 | 1 |
| Transmission | 8 steel + 2 Gear Set | 12 | 1 |
| Piston Set | 4 steel + 2 bronze | 8 | 2 |
| Wiring Loom | 2 copper + 1 rubber | 3 | 2 |
| Engine | 20 steel + 2 Piston Set + 1 Wiring Loom | 20 | 2 |
| Circuit Board | 2 circuits + 1 laminate | 4 | 3 |
| Battery | 4 copper + 2 circuits | 8 | 3 |

  A tier 1 machine asks only what a tier 1 Assembler can make, so survival can start: a Smelter is 643 steel, 322 concrete, 129 rubber and 2 Gear Sets.
- **Input.** Materials sit in the mixed input, parts in the store's kinds. Chutes bring both, as many at once as you run: a chute from a bin brings a load of a material the recipe uses; a chute from a shelf or a Storage Volume brings one part a second that the recipe wants, passing over others. What it can't use stays in the source, and the look line says so. Each input holds up to `batches` of what a batch asks. F, in order: the held part if the recipe wants it, the materials it wants from what you carry, a part off the tray, then its input back, which clears a machine whose recipe changed.
- **Output.** An item lands on the tray, in a pile of 12 a slot, and a chute, F or a beacon takes it as from any slots; nothing can be set in. A physics-only part, or one too big for a slot, leaves as a body at the output point, its near side at the point, level, and owned as the machine is. If the world or a player is in the way (or there is no room for another construct) the batch waits with its inputs, draws nothing, looks again twice a second and says why. With a conveyor out of its **Piece out** each batch rides to a rack instead, and with a queue of
  **work orders** its head order sets the recipe ([Work orders](#work-orders), protocol 251); a status lamp stands over its screen, which
  lists the queue over Queue, Cancel, Clear, Up and Down, and an Order count setting.
- **Same rules as the machines.** The batch's inputs go when it is done, so power cut or broken loses nothing. It needs power in survival (`ConstructRecycler.Powered`), draws only while it works, and takes a **Run** wire. In creative (`CreativeRecycling`) it builds a batch a step, unpowered. Its input counts in the mass (`LoadOf`), and a broken one spills its input and tray.
- **Beacons.** A network reads the tray and can take from it. It never sees the input or fills the tray (`SlotCheck.OutputOnly`).
- **The look line**: "Workshop Assembler (Engine): in 12 steel + 1 Piston Set, tray 0 - needs 20 steel (a batch: 20 steel + 2 Piston Set + 1 Wiring Loom) - F: tip in your 8 steel". Worked out from the store and the table on either side.
- **The Assembly Bay** blueprint: a Workshop Assembler set to Engine, a slab of bins, shelf, Chest and Battery beside it, and chutes to and from its ports.
- **Network (protocol 118).** The recipe is a setting; the mixed input is the Foundry's (bit 64), the parts input is a store's kinds (bit 16), and bit 128 of the flags byte adds u8 blocked and u16 refused part. The job is server-only, and a body is any other construct.
- **Performance.** `ConstructAssembling.Step` runs at 10 Hz and looks only at constructs with `HasAssembler`; a recipe is an array index; nothing allocates except the body a batch makes.

**In survival ([Creative or Survival](overview-and-playing.md#creative-or-survival)).** The Assembler is, with salvage, how parts come into being: nothing comes from nothing. Tier 1 machines ask only for what a tier 1 Assembler can make, but it can't start from nothing: a first Assembler, Foundry, Smelter and Sawmill come with the start kit (or the map). A Battery needs circuits, circuits need gold, and gold needs tier 3 machines, so the first Batteries come from the map too, or from circuits the Recycler gives for wrecked electronics. In survival the physics gun starts at 1,500 kg (a player's Lift Coils raise it), so a heavy built machine stays where it left until the attach tool fits it.

**Self-test**: "Assembling" and "Assembling network" (`Source/App/SelfTestAssembling.cs`): the table and its refusals; the size table, aisle and solid share; ore to metal and stone to steel, into an assembler by real chutes with a shelf of parts, an Engine leaving as a body; missing input, blocked output, power, Run, tier gate, a refused chute; items on the tray and down a chute; F; reach beside a Plasma Smelter; creative; allocation; a real host, a client and a late joiner. "Refining" drives the Foundry.

Choices made (the developer may change them): the recipes and every number; no named input ports (one port takes every chute, and the input sorts itself); physics-only parts can't be inputs, so an Engine can't go into a Monster Truck recipe; a machine buffers 4 batches a tier; one recipe a part and one output a recipe; outputs fit in a slot or leave as bodies; a parts input isn't drawn, only a level; a batch started under one recipe restarts if the recipe changes; generated parts from the weapon builder have no recipes.

## Build stations

The developer's intent: "encourage players to invest in a nice building area with connection to all their available parts". A build station is a
machine you attach to a base, power and chute to your stores; in its range it gives one building aid. Survival needs the stations; away from the
base you build by hand. Code: `Source/Constructs/BuildAids.cs` (the aids, the view, the one rule), `ConstructBeacons.cs` (states), `ConstructEditsAids.cs`
(server: who has what, parts, the gantry's reach), `ConstructMirror.cs`, `ConstructLine.cs`, `ConstructReplace.cs`, `Source/Game/GameSandboxAids.cs`
(view per player, `CActKind.BuildAid`), `Source/App/Program.SandboxAids.cs` (keys, modes, build bar), `Program.SandboxGantry.cs`, `Source/UI/UiBuildAids.cs` (the picker).

- **A station is an Inventory Beacon with an aid.** Its `storage.beacon` id names the aid, so power, Run, wreck, chute network, owner and range are the beacon's rules
  (`ConstructPartDef.Aid`), and in range it serves its network just as a beacon does: Tab opens on it, parts taken off go back to it. All stations on a build area share
  one battery circuit and chute network. `Constructs/Parts/Stations` is written by `Source/PartKit/Generators/StationParts.cs` (mass, power, shapes; the last box is the lamp).
  Ranges are in `Constructs/Beacons.json`. Recipes are in the part kit's `--gen-parts assembling` (tier 3). At most 256 beacons and stations, 64 networks.

| Station | Aid (key) | Range | Mass, power |
| --- | --- | --- | --- |
| Mirror Table | Mirror (M) | 90 m | 160 kg, 150 W |
| Line Layer | Line and fill (O) | 90 m | 180 kg, 150 W |
| Replacer | Replace (P) | 90 m | 170 kg, 150 W |
| Build Gantry | Gantry camera (U) | 120 m | 420 kg, 300 W |
| Selector | Box select | 90 m | 140 kg, 120 W |
| Blueprint Desk | Save and stamp | 90 m | 150 kg, 120 W |

- **The one rule.** `BuildAids.Free(infiniteParts)`: creative needs no stations (decision below). The server's `ConstructEdits.AidWhy` and the client's `Game.SandboxAidLive` both ask it.
- **Parts.** `ConstructEdits.TakeForAid`: creative makes them; survival takes from the inventory, then the network, then the hands. A physics-only piece (nearly all structure,
  all capital parts) is never stored or carried (decision 21), so an aid takes it from **loose pieces within 60 m**, yours or nobody's: a stock yard beside the build.
  Short: it places what it can and says how many are missing. Aiming MMB at a physics-only part with a mode on (or clicking one in the inventory) picks it for the aid; it needs the attach tool.
- **Mirror (M: off, side to side, front to back, up and down; Page Up / Down: move it).** The plane is the table's `mirrorAxis` and `mirrorOffset` settings (the configure tool shows them too, and they save with
  the part); in creative with no table it is the player's own. It is measured from the built-on construct's drive seat, else its root part, square to that part's axes, so one table serves every construct
  in range. A fit or a removal also happens to the image, in the same server action; a part across the plane is its own image. The image's turn is found by shape (`ConstructMirror.TurnOf`, cached):
  of the 24 turns of the part and of its Left / Right partner by name, the one whose boxes and sockets land on the reflected ones. A part with handedness and no partner takes the nearest turn and the HUD says so; a side with
  no free socket says so. The image of a stroke is not mirrored again. freeform-attached pieces aren't mirrored, and neither are the attach tool's socket and point attachments: only the held-part fit, the Line Layer and removal.
- **Line and fill (O).** Press on a ghost and drag: the run lies in the rest-frame plane square to the view at the press, along its two axes, so a straight drag is a row and a diagonal one a field (at most 400 cells).
  The pitch is the next matching socket along the row if there are sockets to follow, else the part's own opposite sockets, else its size. Each cell goes through `ConstructAid.FindFit` (a pose to a free socket, by the rules of a click), in order, so a cell lands on an existing socket
  or on the cell before it. Nothing is sent until the release, so a press with no drag is one part and a stroke never acts on what it made (the `SnapStroke` lesson); RMB cancels. One `BuildAidOp.Line` a stroke. The start part is stamped with its kind and place. The look line shows the run, what is short and the mass; the ghosts are made again only when the run changes.
- **Replace (P).** Aim with a part in hand (or picked) and click; with none, a click opens the picker: every part of the size class that fits, with what you have. The part keeps its index, so joins, wires and links stay;
  a link moves to the new part's matching socket, a routed link's port to the same name, else type and way. Its setting values carry over by key, its paint stays, its health stays as a share. `ConstructReplace.Plan` says why not: a connection that doesn't meet, something in the way, a store with something in it, a link with nowhere to go.
  It refuses joints that carry something, merged or builder receivers, volumes, hoses, freeform attachments, couplings and world links: take those off and fit the new one. The old part goes back as a part taken off does; a physics-only one is set loose above its place.
- **Build Gantry (U).** Aim at a construct (or nearest within range): the camera leaves the body, which stands parked with its inputs off. The mouse moves a cursor, **Left Alt** and the mouse orbits, the wheel zooms up to the range, the move keys slide the point. Every tool works along the cursor's ray.
  Page Up / Down steps a build plane (0.5 m): a ray that meets nothing crosses it, and the nearest free socket to that point takes the part. The server's reach check passes anything within the gantry's range of the station (`ConstructEdits.GantryReach`), never of the body. It ends on the key, on damage, if the construct goes, or when the station fails or the player leaves its range.
- **The build bar.** Bottom centre with a tool out: one icon per aid (green edge: on here; filled: in use; red edge: a station in reach but off; grey: none near), its key, and a line in the tooltip format for what is running. A refused key says why ("Line Layer: off - no chute network - link a chute to a store"), as does the look line on a station.
- **Network.** `CActKind.BuildAid`, u8 `BuildAidOp`, then its fields. `CBodyKind.Aids`, to its player on a change: state, range and distance of each aid, the mirror plane, the gantry flag. The view is worked out at 4 Hz with the beacons' nodes; nothing allocates in a steady tick and ghosts are made again only on a change.
- **World saves.** Stations are ordinary parts and the plane is a part setting, so a save needs nothing new. A player's own creative plane and the gantry aren't saved.

Decisions (the developer may change them):

- Creative needs no stations (`BuildAids.Free`, one line). The stations can still be built there.
- A station also serves its network as an Inventory Beacon does, so a build area needs no separate beacon. The beacon stays the cheap way (60 m, no aid).
- A mirrored, line or swapped part is instant, on top of the first fit's own time: the stations are what you paid for that speed.
- Loose physics-only pieces are the aids' stock, within 60 m of the eye (`ConstructAid.StockReach`).
- Line reach is the Line Layer's range from the eye, and in creative too. 400 cells a stroke.
- The plane steps 0.125 m, the gantry's plane 0.5 m. Being hit ends the gantry; the body isn't made immune.
- Keys: M, O, P, U, Page Up / Down, Left Alt (binds version 19).

## Build stations: the Selector and the Blueprint Desk

Two of the build stations (`Constructs/Parts/Stations`; the framework, ranges and rules for a station are the stations' own: a station is a beacon whose `storage.beacon` id names its aid, and
creative needs none, `BuildAids.Free`). The **Selector** (aid `Select`) works on a group of parts of one construct; the **Blueprint Desk** (aid `Blueprint`) saves a group and stamps it elsewhere.
Every action asks `ConstructEdits.AidWhy` on the server and `Game.SandboxAidLive` on the client; the reason shows on the look line.

**The selector** is a tool like the others, held model `Constructs/Tools/SelectorTool.json`, `ConstructTool.Selector`. It has no slot on the tool wheel (the wheel's compass is muscle memory): the full stop
(`SelectTool`, rebindable) takes it out or puts it away, the Tab inventory's Tools tab lists it, and stamping brings it out. Its reach is `ConstructEdits.AidReach`, 64 m from the eye.

| Input | What it does |
| --- | --- |
| LMB | Picks the part under the crosshair. Shift: adds it, or takes it away if it is selected |
| LMB, drag | A box in the construct's frame, corner to corner: every part it touches. Shift adds; Shift starting on a selected part takes away |
| LMB, drag from open ground | A box on the world, from just under the drag to 48 m above it, for mass selection: a construct whose middle is in it comes whole, one that only reaches in comes with the parts whose middles are. Shift adds. The ground is never selected or saved. Only what the player may delete, within reach |
| Double-click | The whole connected section: every part reached over links without crossing a joint |
| RMB, drag | A rectangle on the screen from the crosshair; the view holds still. It takes the parts whose middles are in it. Shift as above |
| RMB | Clears the selection |
| M, O, P, U | Move, copy, paint (the paint tool's colour; Reset resets), attach. The stations' switches on these keys are off while the selector is out (`Keybinds.Overlap`) |
| Delete, twice | Deletes the group |
| Comma | Saves it as a blueprint (needs a live Desk) |

**Placing** (move, copy, stamp): a ghost follows the crosshair and rests on what is aimed at, or floats at a distance (the wheel). R turns it, the arrows tip it (`GhostTurn`), LMB places, RMB or Esc stops. Aimed at a part
with a free structural socket on the construct's body it snaps there: the clip mates by its own free sockets, outermost first (K: the next one; R: the next spin). A copy or stamp stays up to place again. What lands free is **frozen
where it is** (`Construct.Frozen`, as the physics gun freezes), so a group doesn't fall as it lands; the physics gun lets it go. What lands on a socket is attached on (`ConstructPlace.FindJoin`, `ConstructWorld.Merge`). A selection in pieces
lands as a body each.

**Delete** in creative destroys the parts (containers spill). In survival they come back: through a station's chute network if it has room; else the hands, the backpack or the ground, for up to `MaxDrops` (16) parts. With more than that which can't
go on a network (physics-only, wrecked, or no network), those stay together as one body where they were, not a heap of 2,000 bodies. A refund past a full network is counted on the player.

**Paying** (survival, a paste or stamp): the ghost's line says how many parts are out of reach, and the server checks before it takes anything: the inventory, then the network (`TakeForAid`), and for a
physics-only part, which storage never holds, loose bodies of that part within reach. Short: the refusal lists what is missing, and if another player got there first what was taken goes back. A move costs nothing.

**The selection** (`PartSelection`) is a stamped list: each part with its type, place and turn, checked against the construct every frame. A part another peer renumbered is found again by its stamp; one that went drops out. The server gets it in chunks of
`MaxChunk` u16 part indices, with a hash of those parts' places and turns (never ids: extrusions differ between machines); a hash that doesn't match refuses: "that construct has changed".

**The clip** (`ConstructClip`) is a group held apart from the world: a construct in a `ConstructWorld` of its own, parts in the order a blueprint builds them, numbers in the rest frame they came from. `ConstructWorld.CopyPiece` makes one from a selection and puts one into the real world (a
design only: settings, paint, links, joints, routed links; not health, rounds or contents). A move doesn't copy: `ConstructWorld.CutOut` takes the parts out as they are, contents and all, through `SplitOff`, and what that cuts off falls away as a wreck as any removal does. The same selection gives the same clip
on server and client, so the ghost is what the server places.

**Time.** Selecting and painting are instant. The one structural edit is one tick; checks, payment, refunds, joins and a blueprint's build run as jobs of `ConstructEdits.SelectTick`, 2 ms a tick over all players. A big group's join links through the part tree (`AutoLinkMany`), so joining stays linear. Warm on a
7,921-part hull, 2,000 parts on the 2-core container under load: the request about 1 ms, the commit about 2 ms, the rebuild about 1.5 ms, `WriteFull` under 1 ms. A ghost draws its first `MaxGroupBoxes` boxes and a frame round the whole; the selection outlines its first `MaxOutlines` parts.

**Blueprints** go in the save folder's `ConstructBlueprints` folder, one file each, named after the blueprint (letters, digits, spaces and `- _ ( ) . '`, at most 32; a name in use asks). A file is a real blueprint in the shipped format, and goes by part name, so it can be dropped in `Constructs/Blueprints`. `ConstructClip.ToBlueprint` writes it: a step per part in the clip's order, on the
A saved blueprint records what is, not what the rules would make now. Each step has an `"at"` (the part's place, relative to step 0), so a part stands exactly there even if its socket link is 2 cm out or it overlaps a neighbour; its links are written out too (`"exactLinks": true`, and a step's `"also"`: `[step, their socket, its socket]` for each further link to an earlier step), and a file with `"exactLinks"` makes no others by parts landing on free sockets, because a clip's order isn't the order the parts went on and a free socket here could take one a later step needs; each link has `"laid": true` and `"hosts"` (each node's step), so only its ends and lengths are checked, never its route again, and a blueprint link can carry `"paint"`. A shipped blueprint needs none of these. All of a link's points are relative to step 0, because a selection doesn't start at the construct's origin. A link with an end outside the selection is left out, and the save window says how many; a refusal names what can't build.
earliest step it hangs on, by the sockets they meet at; a freeform attachment is a `"weld"` step, and a piece no link joins to the rest has no `"socket"`: it just stands at its `"at"`, and lands as a body of its own. A save is built back from its own text before it is written, and must give every part. Save needs only the client's copy, so it is a client-side action; the Desk check there is the client's. All 46 shipped blueprints without cargo round-trip. Contents and merged receivers' gun parts don't go in a blueprint (the save says so).
The Tab inventory's Blueprints tab lists them above the shipped ones, with a filter, Stamp and Delete (a survival inventory shows only these). A stamp goes up to the server as deflated text in `UploadChunk` pieces; the server parses it off the game thread, builds it step by step on a scratch world through the normal rules (`ConstructBuild.BuildSteps`), and refuses a part it doesn't have by name.

**Wire**: `CActKind.BuildAid` with `BuildAidOp.Select`, then a `Game.SelOp` (`GameSandboxSelect.cs`). Server: `ConstructEditsSelect.cs`. Client: `SelectController.cs`, `Program.SandboxSelect.cs`, `ConstructRenderSelect.cs`, `UiSandboxBlueprints.cs`.

Choices made (the developer may change them):

- A selection is one construct, unless a box from open ground or Shift reaches others (at most 256). On several, paint, weld and delete go to each in turn; move, copy and save say they need one construct.
- Free landings freeze; attached ones don't.
- Nothing on the wheel: a tenth segment moves every tool on it.
- Physics-only parts are paid in loose bodies near the player.
- A stamped blueprint's fuel and charge are what fitting new tanks and batteries gives (full), as for any blueprint.

## Production lines

The first step of the ore loop (the user, 2026-11-11: rooms and bases felt empty): machines that a generator can place in a row and a player
reads at a glance and runs from one place. Refiners first; the Assembler, Extruder and Recycler are to follow with the same pieces. Code:
`Source/Constructs/ProductionLines.cs`; the parts come from the part kit's `MachineTiers` and `PartScreens` (the lamp) and
the part kit's `control_parts` (the console).

- **Line state** (`ProductionLines.Raw`, `Of`): Running, Stopped (held off: its console or any wire into Run), Starved (empty or short of a
  batch), Blocked (output full or holding another material), Fault (an input it can't work) and No power. Worked out from the store and the
  construct's power on either peer, so nothing new is on the wire. A new state shows once it has lasted 0.3 s (a store's bytes and its Busy
  can come a packet apart), except the first look and Start.
- **The status lamp.** A box marked `"status": true` (an opaque detail) is drawn unlit in the state's colour: green with a bright band
  running along it (Running), dim white (Stopped), amber blinking 1 Hz (Starved), orange 3 Hz (Blocked), red 3 Hz (Fault), out (No power).
  `PartScreens.Mount(..., lamp: true)` grows the screen's plate by a band and stands the lamp in it, the screen's width and at least 0.1 m
  high, so it reads from 20 m where the operator looks. Machines are drawn part by part (a part with a store is never merged), so it costs
  one draw (six while running).
- **A recipe on a refiner.** The `recipe` setting: Auto (what is in the input picks it, as before) or one recipe its tier makes
  (`ConstructRefining.SetOf`, `RefineChoices`, filled as the parts load by `ConstructRefining.Attach`). A set recipe only filters what goes
  in, at a chute and at F (`Feed`, `MixTakes`, `TipMaterial`): what is already in the input is worked as ever, so a change never strands a
  load. The look line says "set to metal", and a refused chute "chute refused sand: set to metal".
- **The Line Console** (`Constructs/Parts/Controls/LineConsole.json`): a desk with a screen, logically a Switch. Wire its output to each
  machine's Run: those wires are the line (`ProductionLines.Machines` walks the construct's data routes from it). Its screen (panel source
  `line`, 10 rows) shows "Line running 2/3" and a row a machine: a marker on the picked one, its name in full ("Extruder 2" when the line has
  two of a kind, numbered in wire order), its state as a whole word in the lamp's colour (in a column as wide as the longest; when a name
  wouldn't fit beside it the state goes under the name), and a dimmer detail line: why it waits (Starved, Blocked, Fault), its recipe or its
  pieces and orders to go; every machine gets one when the rows allow, else the pick. A name too long slides along on the picked row and
  ends in ".." on the others. Its buttons are **Start/Stop** (flips the switch), **Next** (picks the next machine; the pick is the hidden
  `linePick` setting, and a new pick scrolls into view), **Recipe** (opens the recipe picker on the picked refiner, below; on an
  Extruder or Former it says there is none), and **Up** and **Down** (below). F on it flips it too. A new console starts stopped (the `startOn` setting changes that), so its machines are
  held until Start. On a flat battery it puts nothing out, and its machines say No power.
- **Text size and long lists** (2026-11-11, the user: "scroll on long lists", "smaller text"). Every part that shows text has a **Text size**
  setting (Display group, 50 to 150 %, default 100; `CfgKey.TextSize`, protocol 248): the screen is laid out in more rows and longer lines at a
  smaller size. The console's line page, its bill page and a machine's queue scroll: **Up** and **Down** on the screen page by its rows, and
  the mouse wheel scrolls while the crosshair is on the screen with empty hands. The place in the list is the viewer's own (`PanelRender`'s
  slot, never on the wire), with "3-8 of 14" at the right of the title. An order too long for a line takes two (its length and material
  under it); every other line too long ends in "..". A client is sent the first 32 orders of each list (`WorkOrders.Sent`).
- **The recipe picker** (2026-11-11, the user: "nice icons similar to the creative spawn menu"; `Source/UI/UiRecipePicker.cs`,
  `Program.SandboxRecipe.cs`). The console's Recipe button, and a refiner's or an Assembler's recipe row in the configure panel, open a
  window of tiles like the inventory's: the output's picture, its name and count, the inputs as small icons with their amounts, and the
  time a batch takes on this machine (its speed counted). Auto (a refiner) or None (an Assembler) comes first; the current recipe is
  outlined and marked "set"; a recipe the machine's tier can't make is dim, its reason on hover, and can't be picked. Past 12 tiles it
  has a search box, and an Assembler's list the inventory's category tabs (by the output part's category). A click sets it and closes;
  Esc or Close leaves it. From the console a tick box sets every machine of that kind on the line. The pick from the console is
  `CActKind.LineRecipe` (protocol 250: body, console, machine, the recipe setting's value in two bytes, a flag for all), checked as a
  finger on the console's button (reach to the console, its power, who may) and against the line (the machine wired to it, a value
  its recipe setting has, an Assembler's tier); from the configure panel it is the configure tool's own request. The tiles' texts are
  made once per machine kind; drawing it allocates nothing of its own.
- **Resource icons** (`Source/Render/ResourceIcons.cs`). Every loose material has an icon: a small 3D stack of its family's shape in
  its colour, drawn from boxes and part shapes into the inventory's tile atlas (`SandboxTiles`, slots of `Kind.Resource`, made first
  and in a few frames; no image files). The family is `Recycling.json`'s `"shapes"` (ore, ingot, plank, log, heap, drum, gas, block,
  sheet, roll, board, crate, coil, beam, crystal; else ore for a name ending in ore, a heap otherwise); a dark material is lifted to a
  dim grey of its hue so its shape reads. They show in the recipe picker, the material painter's list and a beacon's Materials tab. A
  window that shows only a few tiles (`SandboxTiles.WantFew`, `Prefer`) has the resources and its own tiles drawn first, not the whole
  atlas.
- **Recycler and gathering modules** (2026-11-11, the user: "quality of life for some of these machines"). Both are line machines: the
  same six states, a status lamp, a row on a console wired to their Run, and a Data out (a gathering module has no screen, so a monitor
  on its Data out is its screen: `MachineScreens.Gatherer`). A Recycler is Starved with nothing in its intake, Blocked when its output is
  full or a wreck waits for room, Running while it breaks one. A Drill or Saw Module is Starved off a deposit or with no tree in reach,
  Blocked with a full bin or one holding another material, at Fault when the deposit or the trees need a higher tier, No power when a
  shaft drill's shaft isn't turning. Why it waits rides the store's `Blocked` byte (`GatherStall`; a Recycler's 1 = output full), sent
  and saved as it was, so no wire change. The look line starts with the state word ("Drill Module: 0/20 - Starved: nothing below;
  nearest deposit ..."). Lamps: the Recycler's over its screen (its part file), a module's on its hopper's or bin's back rim (`MachineTiers.ModuleLamp`).
- **Press and Die Lathe** on lines (2026-11-11): Running while it stamps or cuts, Starved waiting for a piece or a die, Blocked with its tray,
  its path or its Piece out conveyor full or cut, at Fault with a conveyor too narrow or the wrong size of lathe; from its stall byte
  (`ProductionLines.PressRaw`). Its lamp is a bar on the front of the crown's dark cap, over its die screen (`PressParts.StatusLamp`).
- **Console reading** (2026-11-11): the picked machine sits on a lit strip as well as its ">"; Auto reads "auto: next metal" from what is
  in the input ("auto: by its input" while empty); bill and queue sizes drop trailing zeros (0.25x0.25, 1m); at a big Text size the title
  keeps its count before the scroll mark ("On 2/3  1-2/3").
- **The mains** (2026-11-11, proposed). A Battery takes one wire, its isolator. When every working battery on a body has one and every one
  says off (`Construct.MainsOff`, `ConstructPower.Mains`, on the wire in the gear byte's top bit since protocol 247), the circuit gives
  machines nothing: `ConstructRecycler.Powered` is false for every production machine, recycler, gatherer and beacon it reaches, in creative
  too, so their lamps show No power. Logic, lamps and motors stay on the batteries, so the switch that opened it can close it. An unwired
  battery keeps the mains on, so builds from before have no change (creative machines still need no power at all).
- **Cost.** A stopped line costs what a held machine did (one store lookup a step); the console's list is walked only when its screen
  fills (4 Hz, in view) or a button is pressed; nothing allocates.

Proposed (the user's to confirm): the six states and their colours and blinks; the lamp over the screen rather than a beacon on top;
membership by wire; four machines on the screen; Auto as the default recipe; a console starting stopped. Next: the lamp and console for the
Assembler, Extruder and Recycler; a real moving part per machine family while it runs; a standard chute port height (1.0 m, in at the back
left, out at the front right) so a base generator lays machines in a row.

## Production monitors

Quality of life for the lines (the user, 2026-11-11: "when you connect the data out of the line console to a display, it will show its
queues and line status, but in different size displays"). No monitor part: any display shows a line. Code:
`Source/Constructs/ProductionLines.Monitors.cs`; the hook in `PanelRender.Fill`.

- **Data out.** A Line Console's outputs are **Run** (its switch: the line's wires, as before; an old wire is still Run), **Data out**,
  **Alarm**, **Running** and **Made**. Every refiner, Extruder, Wheel Former, Assembler, Press, Die Lathe and Piece Rack has a **Data
  out** too (an Extruder's first output stays its Length). A data link from a Data out to any logic Display part (readouts, signs,
  gauges, graphs, lamp banks; `ProductionLines.CanMonitor`) makes that display a monitor of its source: its own page is set aside
  while the link is intact. Several displays may hang off one source. The link is an ordinary data wire carrying nothing, so saves,
  blueprints, copies and joiners keep it. Recyclers, Drill and Saw Modules and every store (Materials Bin, Storage Volume, Parts Shelf,
  shelves, chests: `StoreMonitor.cs`) have one as well: a store's monitor shows its fill ("300/528 metal, 57%" and a bar, or a shelf's
  slots and each kind with its count, moving on by itself); tiny: "Storage Volume 57% metal".
- **Size.** A monitor's rows are its panel's height in 6 cm rows at its Text size, up to 16; a panel 2 rows or less shows one line as
  big as fits; a list that would be under 18 characters across takes smaller text (`MonitorRows`). So a Number Readout is small, a Text
  Sign medium, a Text Sign at 50 % or a Medium Number Readout and anything bigger a board.
  - **Tiny** (one line): "Line 2/3 running - 14 to go" (or "- 6 made"), in the worst state's colour.
  - **Small** (3 to 6 rows): a row a machine, its name (numbered as on the console, ".." when cut) and its state in full in its lamp's
    colour; a title when every machine fits under it.
  - **Medium** (7 to 14): the title with what it made, then two lines a machine: name and state, then why it waits, its recipe or its orders.
  - **Large** (15 up): "Production line - running n/m", "Made 6   To go 13   Stops at 6", a table (Machine, State, Queue: the head
    order's made/count and the orders after it, Doing: the detail line), then the Piece Racks the line's conveyors end at (through
    junctions and presses) with their fill, when there is room.
  - A list longer than its rows moves on a row every 2 s by itself and rests at each end (a monitor has no buttons).
  - A machine's monitor is its own screen laid out for the display (tiny: "Extruder: Extruding - 8 to go"); a rack's is its fill
    ("62% full, 131 pieces, 4 kinds"), a bar, and its kinds with counts.
- **Outputs.** Alarm is 1 while any machine on the line is Starved, Blocked, Fault or No power; Running while any runs; Made is the pieces
  (a refiner's batches, an Assembler's parts) made since Start, a number for counters, compares and readouts. Worked out on the server
  at 10 Hz (`ProductionLines.Step`); a wire carries them to lamps as any signal. **Stop after** (General, 0 to 9999, 0 off;
  `CfgKey.LineStopAfter`) turns the console off when Made reaches it; Start counts from 0 again.
- **Network (protocol 253).** Made rides at the end of `CBodyKind.Orders` (u8 n, then u16 part and u32 made per console whose count isn't
  0), sent when it changes. Everything else a monitor shows is what the console's own screen reads on either peer. Made isn't saved: a
  loaded world counts from 0.
- **Cost.** A monitor fills with the panels (4 Hz, in view) and lays out only when its card changes; finding its source is one walk of
  the body's links. Measured (a throwaway host and client over RakNet at 80 ms and 2 % loss, removed): 240 fills of the 16-row board
  (a minute at 4 Hz) 0 bytes, 0.054 ms each; every monitor's text equal on both peers, with the line running and with the mains off;
  the Alarm lamp 1 on both peers with the mains off; Stop after 6 stopped the orders line at 6 on both. The server step walks only
  bodies with a console.

Proposed (the user's to confirm): Data out as a port on the source rather than a monitor part; 6 cm rows and the four bands; the list
moving every 2 s; Made counting a refiner's batches; Made not saved. Not done: a monitor on another body (a world link); a line name.

## Conveyors

The large-piece link (the user, 2026-11-11: a chute carrying a 4 m beam "is not visually satisfying and breaks immersion"). There
are two connection classes: the chute, for small things and loose material, and one large-piece link that is laid like any link
and then set to a size. Its first carrier is the roller conveyor; cranes, carts on tracks and arms are to come on the same link.
Code: `Source/Constructs/ConstructConveyor.cs`; drawing in `RouteRender.Conveyor`; the rack from the part kit's `piece_rack_parts`.

- **Ports.** A `"conveyor"` port names its clear `"opening"` ([across, up], metres). Every Extruder and Wheel Former has an
  implicit **Piece out** on its output face, at the base die's bottom edge (opening: its largest section; a Wheel Former's widest by
  biggest across). The **Piece Rack** (pieces up to 1 x 1 m and 6 m long) and **Long Piece Rack** (2 x 2 m, 12 m) have a
  **Piece in** at their -z end and a `storage.pieces` block (`section`, `length`, `count`, `hold`: the box they pack into). A Press or Die Lathe has an implicit
  **Piece in** at each bay's socket ("Piece in", "Piece in 2"...; the line 11 cm under it, opening its largest piece across): a piece
  that arrives goes into the bay by the rules a loose piece left in its mouth meets (`ConstructPress.TakeFromConveyor`), and waits on
  the conveyor while the bay is full or refuses it (the press's look line says why).
  A Press or Die Lathe also has a **Piece out** after its bays (at its `outAt`, facing `outDir`, the machine's width across, up to 4 m):
  what it stamps that no tray holds goes on that conveyor (`ConstructConveyor.PutMade`). So a rider and a rack's kind are any part, a
  piece by its key or a stamped part by its id (`ConstructStorage.WriteDef`, protocol 247); a stamped part riding into a press waits.
- **Junctions** (the part kit's `conveyor_junction_parts`): the **Conveyor Splitter** (Piece in at -z; Piece out, 2, 3 at +z, +x,
  -x) sends each piece to its linked outputs in turn, past one that is cut, too small for the piece or has no room at its start;
  the **Conveyor Join** (Piece in, 2, 3 at -z, +x, -x; Piece out at +z) takes its inputs each in its turn. Large ones pass 2 x 2 m.
  A piece never splits: it crosses whole. Its ports sit on the edges' middles at the deck's line; a piece rides its *lead-in*
  (the input's end, the junction's middle, the output's start) with its front below 0 on the output and turns from the way it
  came to the way it goes as its middle crosses (`ConstructConveyor.LeadIn`, `CRider.ViaPlus1`); the piece behind stops short by
  as much of it as still lies on the input (`Tails`).
- **The link** is `RouteType.Conveyor`: routed, auto-routed, damaged, mended, painted and deleted as every routed link is; bends
  in 45 degree steps; it doesn't branch. A conveyor has one size, its width (user, 2026-11-11): it is clear
  above, so a piece fits when its section's larger side is no wider (`ConstructConveyor.Needs`). A new conveyor opens to the smaller of
  its ends' opening widths, no wider than its turns leave room for (`CRoute.ConvW`, cm; [Auto-routing](power-and-links.md#routed-links-decision-20)).
  `ConvH` stays in the wire, saves and blueprints and is ignored. Aimed at with the linking tool, the look line says
  "Conveyor 0.30 m wide (max 1.00 m) - wheel: width, R: widest"; the wheel sets the width 5 cm a notch, drawn at once, sent once the
  wheel rests, and the Reload key (R) sets it to the widest its ends and turns allow, sent at once (`RouteEditOp.Size`). The size
  change wakes the body: the machine retries the piece on its die at its next step. It is drawn as a roller bed the set width: a dark under-plate, steel-blue side
  rails (the paint), steel rollers every 0.3 m and amber chevrons on the rails pointing the way the pieces go.
- **Riding.** A cut at the die (the Cut input or Cut every) with a conveyor out of Piece out puts the finished piece on it whole:
  the part leaves the construct and becomes a rider (`CRider`: its def, link, front in metres, health share, paint). Riders move at
  0.5 m/s, front first, never closer than 10 cm, and the lead one goes into the rack at the end. The machine waits, the piece on its
  die, when the piece is too wide for the link ("conveyor too narrow: 0.10 m, piece needs 0.30 m - aim the linking tool at it: wheel
  or R to widen"; the screen says "Conveyor too narrow"), when there
  is no room at the start ("blocked: its conveyor is full": a full rack backs the pieces up to the machine) or when the link is cut.
- **Off the link.** A cut or deleted conveyor drops every piece on it as a loose body where it was, moving as the body moved, and so
  does a construct that goes on the server with pieces riding (deleted, destroyed, cleared as a wreck, fallen out of the world:
  `ConstructWorld.Remove`); one that travels takes them in its world copy (`keepRiders`), and a client's removal is the server's
  word. Pieces in a rack are kinds in its store, so a full rack costs no part slots.
- **A rack holds by volume** (`ConstructRackPack.cs`, 2026-11-11). Its room is its hold box, not a count: pieces pack in
  *columns*, one kind each, as long as the piece, laid along the rack; across a column stand *stacks* as many as the piece's width
  lets (2 cm apart), each as high as the hold lets (5 mm between layers). A piece goes on the lowest stack of the first column of
  its kind with room, else a new column opens in the first gap along the rack long enough for it (2 cm between columns); an emptied
  column's gap is reused and the columns after it stay put. So a Piece Rack takes 276 L Brackets or 20 beams of 4 m (0.2 m square),
  a Long Piece Rack 2,350 brackets or 216 such beams, or any mix. `count` stays a hard cap (2,000 and 6,000, above what the holds
  take of the smallest pieces: bounded memory and wire); at most 192 columns and 32 stacks a column. A piece that doesn't fit waits
  on the conveyor: "jammed: rack full", or "piece too long for this rack" / "too big across" (`ConstructConveyor.RackRefusal`).
  The columns are the store's state and go on the wire after its kinds (protocol 249); a save keeps the kinds, and the places are
  packed again on load. The look line and `ConstructConveyor.FillText` (for a console) say "62% full, 131 pieces, 4 kinds".
- **By hand.** A look ray meets a rack's stacks (past its own bars) and its hold, and riding pieces (a look only: shots don't).
  F takes the top piece of the stack looked at into the hands (`Give`: hands, backpack, else dropped; a physics-only piece lies
  beside the rack on the player's side); Shift+F drops up to 40 of that kind loose beside it; the physics gun pulls the top piece
  out where it lay and holds it. F on a riding piece takes it off its conveyor (the physics gun grabs it): the request names the
  conveyor and where the piece's front was, and the server takes the one nearest that (within 1.5 m); the pieces behind close up.
  A conveyor's look line, and its lead piece's, say why it waits ("jammed: rack full").
- **The rack shows what it holds.** Every piece at its packed place at true size within 30 m (plus half the rack), one box a
  column in its first box's colour further off (`RouteRender.DrawRacks`, the links' batch, from the store as heard).
- **Collisions.** The conveyor is a drawn link: shots and blasts hit its segments (health 120); players and bodies pass through it
  as through every routed link, and through riding pieces.
- **Network (protocol 244, riders 245).** A conveyor's size is 4 bytes after its link in a Full. `CBodyKind.Riders` sends a body's
  riders, the whole list, when one is added, arrives, crosses a junction or drops (and after a structure change), each by its
  piece's key, its front as an f32 and the link it came through; clients step them on by the same rule ten times a second
  (`ClientStep`: no arrivals or junctions, those are the server's word) and keep a piece's own front when the server's word is a
  little behind it, so nothing steps back. Measured at 80 ms and 2 % loss: a piece's middle moves at most 2.2 cm in a frame across
  a splitter. A store's slot or kind that is a run-time piece goes on the wire as `Extrusion.WireMark` and its key.
- **Saves.** After a body's behaviour brief: its conveyors' sizes, then its riders (link, front, health, paint, part name). An older
  save ends before them. A piece saved inside a junction loads at its output's start. Blueprints, copies and player blueprints keep
  a conveyor's size (`"size": [w, h]` in metres on a link, within what its ends take where it is built; without it, the limit).
- **Cost.** Nothing per tick: a 10 Hz step walks only bodies with riders, a few float operations a rider and a link's length once
  a step; only a piece in a junction costs a look at its lead-in; nothing allocates in a steady line. Measured on the 2-core box:
  200 riders on 20 links with 5 splitters, 0.05 ms a step (max 0.15 ms); a client's step 0.003 ms. Racks draw only when they hold
  something (their list is made with the caches).

Proposed (the user's to confirm): the wheel on the linking tool for the size (links have no configure panel); 0.5 m/s and a 10 cm
gap; junctions as parts rather than branches of a link; three ways at a junction; the rack storing pieces by kind rather than as
fitted parts, drawing at most 24; F dropping the piece loose by the rack; riders passing through players (not reached: a piece as
a solid box against a player near it is the next step); a piece longer than its whole link refused.

## Work orders

The first step of automated vehicle building (the user, 2026-11-11: "every exact part to be built", a machine working through a queue
of sizes, another material another line). An Extruder or a Wheel Former takes **orders**, each setting its own section, size and length
and how many to make; a Line Console holds a **bill** and deals it to the machines it runs. Code: `Source/Constructs/WorkOrders.cs`
(`WorkOrders`, `ConstructBill`); the machine's side in `ConstructExtruder.Work`; the parts from the part kit's `extrusion_parts` and
`control_parts`.

- **An order** (`WorkOrder`) is a piece's whole key (shape, section, width, height, pipe wall, length, material; a wheel's length 0) and
  a count, 1 to 999; or a catalogue part and a count, which an Assembler takes (below).
- **The Assembler's orders** (2026-11-11). Every Assembler, Thruster Assembler and Tailor takes an order for a catalogue part its family
  has a recipe for at its tier (`AssemblingTable.Of`: one recipe a part); else the reason: "no recipe makes the Rudder", "the Bearing needs
  a tier 2 Assembler (this is tier 1)", "the Capital Jet is made by a Thruster Assembler, not the Assembler", a full queue. With a queue
  its head order sets the recipe (`ConstructAssembling.RecipeOf`), chutes and F feed that recipe, Starved names the missing input, and each
  batch counts its parts made (a recipe that makes 2 may make one more than the order asks); an empty queue waits ("Waiting for orders").
  Queue makes one order of its Recipe setting and Order count. Its screen, lamp, buttons and line state are the Extruder's (below).
- **Piece out on an Assembler.** Every Assembler has an implicit **Piece out** conveyor port at its output point (`outAt`, facing
  `outDir`, the machine's width across). With a conveyor linked, each batch rides it whole to a rack (`ConstructConveyor.PutMade`); a part
  too wide for the conveyor goes to the tray when a slot takes it, else waits ("refused: the part is too wide for its conveyor"); a full
  or cut conveyor holds the batch ("blocked: its conveyor is full"). With none linked, the tray and the output point as before.
  Measured (a throwaway probe, removed; host and client over RakNet at 80 ms and 2 % loss, the showcase in a creative world): the bill's
  three catalogue lines made in order and racked, the rack equal by kind on both peers; a Rudder line kept with "no recipe makes the
  Rudder" on the client's console; an idle Assembler's step 0.0001 ms, 0 bytes; 200 orders queued: the assembling step 0 bytes a call
  (worst 0.02 ms), whole server ticks median 0.014 ms, worst 0.8 ms; a save and a reload kept all 196 orders left.
- **The queue** is the machine's own (`CPart.Orders`), at most 255 orders, so it moves with the part. A machine with a queue, even an
  empty one, works its orders and nothing else: idle with "waiting for orders" when it runs out, so a finished bill spills nothing.
  **Clear** drops the queue and the machine is back on its own settings. The head order replaces the settings: an Extruder grows its
  section and cuts at its length; a Wheel Former grows the width, then the diameter, and cuts. Each cut piece that is the head's counts;
  at its count the head goes and the next begins with the next piece. A piece on the die that isn't the head's (the head was
  cancelled) is cut off as it is. Pieces leave by the Piece out conveyor, or drop as before. Run held (the console stopped) holds the
  queue.
- **Accepting.** `WorkOrders.Refusal` gives one reason and a refused order is never queued: the wrong family (an Extruder takes
  sections, rounds and pipes; a Wheel Former wheels; Fan and Airframe Formers take no orders yet), a size past the die as it stands
  (Die Frames counted) or the machine, a length off the 5 cm step or past `maxLength`, a wheel smaller than the machine's first, a
  section that can't be meshed, a material its table hasn't, a full queue, and **another material**: a machine fed metal refuses an
  order in wood ("the Extruder is fed metal: an order in wood is another line"). An empty bin takes any; it then waits Starved with
  "needs metal for its order" until its chute brings it.
- **At the machine.** Every Extruder and Wheel Former has a status lamp over its screen (the line's colours) and three buttons under
  it: **Queue** (one order of its settings as they stand and its **Order count**: an Extruder's section and Cut every, a Wheel Former's
  Order width and Order diameter; the material its bin holds), **Cancel** (the head) and **Clear**. The screen shows stock, the piece so
  far, the orders and pieces to go and the next three, the head marked.
- **The bill** is the Line Console's. Its fourth button, **Bill**, turns the screen to the bill page, where the buttons are **Add** (one
  line of its Bill line settings: Kind, Profile, Width, Height, Pipe wall, Length, Material, Count), **Send**, **Clear** and **Line**.
  Send deals each line whole, in order, to the console's machines (its wires into their Run) that accept it, round robin on from the
  last that took one; a line none takes stays with the first refusal on the screen (on every peer). The line page lists Extruders and
  Formers with their pieces to go ("own": on their settings), and Assemblers with their parts to go or their recipe. A Send deals catalogue
  lines to the Assemblers and pieces to the Extruders and Formers; a line kept names the reason of a machine of its kind.
- **A blueprint's bill.** `ConstructBill.FromBlueprint` lists everything a blueprint needs: extruded and formed pieces by key, catalogue
  parts by part, each with a count, and its **link stock**: metres of each link type (wire, power cable, pipe, shaft, chute, ammo feed,
  conveyor), measured on the blueprint built in a scratch world. `ToConsole` puts the pieces and the parts a recipe makes on a console's
  bill (the building station will call these). `--bill "NAME|NAME..."` prints it grouped (extruded, formed, catalogue by category with
  the recipe, family and tier that make each part or "no recipe", links) with totals, to `callofslop.log`. Of the shipped blueprints only
  Fabric Flyer and Sky Crane need pieces, and those are fans and wings. What has no recipe (ten vehicles, 2026-11-11): hull lofts (each
  vehicle's own), the weapon builder's gun parts (receivers, barrels, sears, splitters) and the flight surfaces and rotors (wings,
  elevators, ailerons, flaps, rudders, fins, props, rotors). A blueprint's `"bill"` line may name a catalogue part:
  `{"step": n, "part": "Gear Set", "count": 4}`.
- **Network (protocol 246).** `CBodyKind.Orders`, per body when a queue or bill changes, after a Full and to a joiner: each list's
  part, totals, note and its first 32 orders (22 fixed bytes each; three before protocol 248). A client holds only those: all a screen shows.
- **Saves.** After a body's conveyors block (an empty one when it has none), its lists in full: materials and parts by the save's names.
  An older save ends before them. A blueprint carries no machine queues (a blueprint is a design, not a job); a Line Console's bill may ride
  in it: `"bill": [{"step": n, "piece": "Extrusion l-angle 20x20x75 metal", "count": 2}]`, put on that console as it is built
  (`ConstructBill.ParseLine`, `FromLine`; the Production Showcase's). A bill holds orders, never material. Copies and player blueprints don't
  write one yet.
- **Cost.** An idle machine or an empty queue costs a null check (measured: the Extruder step 0.0005 ms, 0 bytes); a 200-order queue on
  two machines 0.03 ms median, 0.4 ms max a step on the 2-core box. A step allocates only what a working Extruder always did: a new
  piece def the first time a key is grown (about 2.5 KB) and the body or rider it cuts. The console's machines are walked only on Send.

Proposed (the user's to confirm): the Line Console holding the bill rather than a new desk part; its wires into Run as the order links;
a machine with a queue working orders only, Clear going back to its settings; an empty bin accepting any material; whole lines dealt
round robin rather than counts split across machines; the queue and bill bounds (255 orders, 999 a line); a cancelled head's piece cut
off as it is; blueprints carrying no orders; an Assembler's order counting parts made by whole batches; with a Piece out conveyor linked, every
batch riding it (a part too wide goes to the tray if it fits there). Next: the Fan and Airframe Formers, and the
building station issuing a blueprint's bill.

## Crafting terminal

The user (2026-11-11): "a crafting terminal, which basically will give you a list of what you can possibly make with what's currently
in the network. Similar to the creative spawn menu... if a player wants to make one or two of a piece to build freely within the
world, it can be simple and easy to use." Code: `Source/Constructs/ConstructCrafting.cs` (the network, the list, orders, the wire,
saves), `ConstructReserve.cs` ([Reservations](#reservations)), `Source/UI/UiCrafting.cs` and `Program.SandboxCrafting.cs` (the
panel); the part from the part kit's `crafting_terminal_parts`.

- **The part** (`Constructs/Parts/Crafting/CraftingTerminal.json`, 2 by 2.5 m, 80 W): a desk with a screen at standing height, a
  sorter cabinet with a chute hopper behind it, and a low tray along its right. It is a Piece Rack with `storage.pieces.crafting`
  (`IsCraftTerminal`): the tray is the rack's hold (0.8 x 0.8 m across, 2.3 m long), so it packs by volume, draws what it holds, and F on
  a stack takes that part, by the rack's code. Ports: **Piece in** (conveyor, the tray's back), **Chute in** (the hopper) and
  **Orders** (a data input). Its screen (panel source `craft`) shows what it sees, the tray and its first four orders.
- **The network** (`ConstructCrafting.NetOf`), on the terminal's own construct: every part joined to it by intact conveyors and
  chutes, either way round, through junctions, machines and stores; and the machines of every Line Console whose output is wired into
  its **Orders** port (`ProductionLines.Machines`), with what those machines are joined to. The wire runs console to terminal, so
  nothing new reaches a machine's Run. Machines are Assemblers, Extruders and Wheel Formers (64); stores are racks, shelves,
  chests, volumes and bins (64); refiners are noted for chains (16). It is cached per terminal under a hash of the body's links and
  part count, looked at when an order is placed or stepped, a panel is drawn or the look line is made: never per tick. The look
  line: "Crafting Terminal: 4 machines, 8 stores, 1 console - tray empty".
- **The list** (`Catalogue`, worked out on either peer from stores, links and queues: nothing of it is on the wire): every part in
  linked stores that isn't reserved ("in stock 6"); every recipe a linked Assembler family makes at its tier; from each Extruder
  four sections (solid bar, hollow box, I-beam, L-angle) at 0.1 and 0.2 m in 1, 2 and 4 m, in the material it is fed, where its die
  takes them; each Wheel Former's first wheel. A row is **now** (a batch's inputs are in the machine or in linked stores), **chain**
  (every missing input is itself made by a linked refiner or Assembler: one level) or **later** (the machine exists, inputs
  missing: "needs 4 rubber, 2 cloth").
- **The panel.** F on the terminal (not on a stack in its tray) opens it: tabs **Make**, **In stock** and **Orders**, a search box,
  and the recipe picker's tiles (picture, name, inputs as resource icons, a badge line: "in stock 3, can make", "can make",
  "can make: chain", or what is missing). A dim tile can't be ordered; its reason is on hover. A click selects; the bar below has
  1, 5, a count box and **Order**. Orders lists each request with its state and **Cancel**. Esc closes; it closes on a local reset,
  when the player walks off or dies.
- **An order** (`CraftOrder`, at most 32 open a terminal; `CActKind.CraftOrder`, checked as a finger on a console: reach, power, who
  may) is fulfilled by a 2 Hz step that walks only bodies with open orders (`ConstructCrafting.Step`):
  1. **Stock first.** It reserves what is free in linked stores. A rack whose **Piece out** (an implicit port at every Piece Rack's
     far end) has a conveyor to the terminal's Piece in, straight or through Joins, sends the pieces as riders, one a step. A store
     with a chute into Chute in sends by that chute's own moves, and only what the terminal's orders reserved there
     (`ChuteInto`). A store with neither: "no route from the Long Piece Rack: collect it there", the parts stay reserved for the
     player, and taking them counts.
  2. **The rest is made.** A work order goes to the first linked machine that accepts it (`WorkOrders.Queue`, so the machine's own
     refusals apply). What it makes lands in the tray directly, or in a linked store, where the waiting order reserves and
     fetches it.
  3. A part that lands in the tray counts for the oldest open order of its kind. A full tray holds the store: "held: the tray is
     full - take parts out". A finished order stays listed for 60 s.
  States on either peer: waiting, fetching from X, queued at X, riding to the tray, in the tray, no route from X, held (why).
  **Cancel** drops the order's reservations and takes its unstarted work order off the machine's queue.
- **Network (protocol 253).** `CActKind.CraftOrder` (body, terminal, the order's 22 bytes) and `CraftCancel` (body, terminal, id);
  `CBodyKind.Craft`, per body on a change, after a Full and to a joiner: each terminal's orders (the order, id, got, state, where, a
  note) and each store's reserved kinds.
- **Saves.** After a body's work orders (an empty block when it has none): each terminal's orders with got, asked and riding counts.
  Reservations aren't written: the orders claim again at the first step after a load. An older save ends before the block.
- **Cost.** A world without open orders: one list check a tick. A blueprint carries no orders.
  Measured (a throwaway probe, removed: host and client over RakNet at 80 ms and 2 % loss, the showcase in a creative world): the
  client ordered 2 Gear Sets in stock (reserved, rode from the rack to the tray), 2 Steel Plates (queued at the Assembler, made, rode
  back), a 1 m solid bar (made by the Extruder, racked where no conveyor returns: "no route from the Long Piece Rack: collect it
  there") and a Tyre Carcass whose inputs are missing (queued at the Assembler, which waits starved); both peers agreed on the tray,
  the reserved counts and every order's state; a save taken mid-order loaded all 4 orders; a cancel took the order and its work order
  off the Assembler's queue. The step with 32 open orders on a terminal: 0.03 ms, 0 bytes; an idle world: under 0.5 microseconds a tick.

- **After the first window run** (2026-11-11, `--window-shot` with its `click`, `esc` and `ui` actions). The part follows the
  [machine look](look.md#machine-look): dark base, teal body, accent rails and keys, white at the ways in, orange at the tray's
  front, the screen's middle at 1.5 m. It has a second conveyor input, **Piece in 2**, on the sorter's side (up to 0.5 m). Make has
  a **Can make now** tick, on by default: only what can be made now or through a chain; unticked it adds what lacks inputs, dim.
  A queued order says what its machine lacks ("queued at the Assembler: needs 4 rubber, 2 cloth"), looked at every fifth step.
  The showcase's Long Piece Rack has a return conveyor along the back aisle to Piece in 2, so extruded pieces come back too.
  Seen in the window: F opens it, twenty opens and Esc closes leave the mouse captured, a clicked tile, Order and Cancel work,
  and the made part lands in the tray.

Not yet: other constructs by world link; splitters on a return path (a splitter deals in turn, so it is no route); chutes of more
than one hop; a Piece Rack as a bigger tray; press products; a custom size entry (the Line Console's bill makes those); reserving a
machine's inputs for an order.

Proposed (the user's to confirm): the console-to-terminal wire as the order link; conveyors and chutes joining either way round;
stock before making; 32 open orders; a done order shown 60 s; reservations worked out again on load; the tray's size.

## Reservations

Shared by the Crafting Terminal and, next, the blueprint building station (`Source/Constructs/ConstructReserve.cs`,
`ConstructWorld.Reserve`). A reservation is a claim on stock: a store, a kind, a count, and its owner (a terminal's store handle and
its order's id). **Available is held less reserved** (`ConstructCrafting.Available`): the list, another terminal's order and a
station's build see only that. One table a world, at most 1,024 claims.

- It dies with its owner (the terminal removed, the order cancelled or done), with its store, and shrinks as its parts leave for the
  owner.
- **By hand.** Taking a reserved part is allowed. At its owner's next step the claim is cut to what the store still holds and the
  order asks again: it reserves elsewhere, or a work order is issued for the shortfall.
- A rack's look line says "62% full, 12 pieces, 2 kinds (4 reserved)" (`CStore.Reserved`). Clients hear each store's reserved
  kinds in `CBodyKind.Craft`, so their lists read the same.
- Never saved: an order is the truth, a reservation its claim, made again after a load.

## Production Showcase

One shipped blueprint, **Production Showcase** (`Constructs/Blueprints/ProductionShowcase.json`, group Production), with every machine
that carries the production systems of 2026-11-11 on one 64 by 14 m floor of Foundations, stocked and wired, so the systems can be
tried the moment it is spawned from the creative blueprint list. Made by the part kit's `--gen-parts production_showcase`
(`Source/PartKit/Generators/ProductionShowcase.cs`), which builds it step by step through the game's own rules (`BlueprintScript`), so
a step the rules refuse fails there with their reason. It reads the part files: run it after the part generators.

- **The layout**, left to right as material flows, a 2 m walkway along the front and one behind the first row, every screen, status lamp
  and sign facing a walkway: **power** (a Small Genset on two Fuel Tanks, a Battery, the main Switch with a Charge Gauge); **stock**
  (Storage Volumes, each beside the machine it feeds); **refining** (Smelter on iron ore, Sawmill on wood, Foundry on metal and stone,
  each chuted from stock into a Materials Bin, one Line Console on their Runs); **line A** (an Extruder fed metal, Piece out onto a
  conveyor to a Conveyor Splitter: one branch to a Press with a Bracket Die on its first bay, the other through a Conveyor Join to a
  Long Piece Rack); **line B** (an Extruder fed planks, its conveyor meeting line A's at the join); **the spur** (an Extruder making a
  0.3 m bar onto a conveyor set to 0.10 m, on purpose, to a Piece Rack); **wheels** (a Wheel Former fed rubber, a conveyor, a Piece
  Rack); **orders** (a second Line Console wired to both Extruders' and the Wheel Former's Runs, its bill already on it: seven sizes of
  metal angle and plate, two of planks, two wheels; 19 pieces; and three catalogue lines, 4 Gear Sets, 4 Steel Plates, 2 Gun Barrels);
  **assembling** (an Assembler fed steel, its Piece out onto a conveyor to a Piece Rack, on the orders console: "Send the bill: the
  Assembler makes these parts"); **crafting** (a Crafting Terminal between the two end racks, the orders console wired into its Orders port, a
  return conveyor from the Assembler's rack to its tray: "F on the screen: order a part. It arrives in the tray").
- **Stock** is the Storage Volume's own creative-only Material and Fill settings (`ConstructBuild.SetConfig`): a creative world fills
  each box as the blueprint builds; a survival world builds it empty, so a blueprint gives survival nothing. They are filled to 24%,
  under the 25% an open side may hold before it pours ([Sealed volumes](power-and-links.md#sealed-volumes)), so they need no walls.
- **The bill** rides in the blueprint's `"bill"` ([Work orders](#work-orders), "Saves").
- **The signs** (one a test): MAIN SWITCH (off = machines No power, genset and lamps off); STOCK; press Start on the refining console;
  Bill, then Send, then Line and Start on the orders console; cut line A's first conveyor to see pieces drop; take a piece off the long
  rack with F; the press stamps brackets from line A's angles; line B meets line A at the join; the spur's conveyor is too small on
  purpose; set the wheel line's conveyor too small with the mouse wheel to see a refusal.
- **Monitors** ([Production monitors](#production-monitors); `Monitors()`, the front post line at x 19 to 22 and 57 to 63.5): a Number
  Readout on the refining console's Data out (small), a Text Sign on the orders console's (medium), and the orders board at the
  right-hand end, a Text Sign at Text size 50 % (no adapter takes a medium display onto the small floor), with a red Lamp on a mast
  beside it on the orders console's Alarm; a sign each.
- **What to expect.** The main switch is wired to the genset and to the battery, whose wire is the [mains](#production-lines): off, every
  machine shows No power (creative too) while the consoles stay lit, and the genset stops. Creative refiners take a load in a few seconds,
  so their lamps show Running. The Press puts its brackets on a conveyor out of its Piece out to a Piece Rack behind it (276 brackets: a full
  rack backs the press up, "Output conveyor full"); either bay takes line A's angles.
- **Measured** (a throwaway probe, removed: host and client over RakNet on the Proving Ground, spawned through the parts menu's call,
  the refining console started and the bill sent, a cold process; 2026-11-11 after fix/showcase-bugs): racks equal by key on both peers;
  the refiners reached Running; the switch off gave No power on all seven machines on both peers and on gave them back; 40 brackets in the
  rack, none loose; bay 2 takes an L-angle. Host ticks over the whole bill: worst 15.9 to 20.7 ms (a few one-offs in its first 6 s, the
  JIT), p99 0.8 to 1.3 ms, 58 MB allocated (both peers), no generation-1 collection; the bill again, warm: worst 4.8 to 6.6 ms, p99
  0.9 to 1.9 ms, no collection. Before the fix the Wheel Former's growing wheel made a convex hull for each 5 cm (see [Wheel Former](#the-wheel-former)):
  ticks of 8 to 83 ms twice a second, 199 MB and a generation-1 collection.
