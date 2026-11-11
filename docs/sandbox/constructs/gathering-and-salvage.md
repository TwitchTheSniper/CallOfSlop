# Gathering and salvage

Covers salvage and the inventory, the salvage haul, gathering, scoops. Part of the construct guide: index in ../CONSTRUCTS.md.

## Salvage and the inventory (milestone 3)

Decision 7, the loot loop: what you salvage is what survives, so disabling an enemy by shooting its cheap exposed parts
leaves more to take. Board item dev-sandbox-m3, part 2 of 4 (the developer approved it 2026-10-29).

- **The inventory.** Every player has one on the server (`SandboxInventory`, Source/Constructs/ConstructInventory.cs):
  a count of each part kind (by part id, up to 9,999 each), the **materials** they carry (loads taken from bins; up to 9,999 each; scrap is gone, see [Salvage haul](#salvage-haul-decision-31))
  (resources come later; always empty now, but the wire carries them). Each player's client hears its own whole
  inventory when it changes (protocol 81), so the HUD and the inventory window show it; nobody else's is sent. It resets
  with the match like the constructs (nothing is saved yet). It survives death; a leaving player's empties (their id may
  be someone else's next). The read API: `Game.SandboxInventory(playerId)` (counts in `Parts[partId]` or `Count(id)`,
  `Materials`, `TotalMaterials`, `TotalParts`, `Kinds`, `Infinite`, and `Version`, which goes up on every change so a window can
  rebuild its text only then). On a client only the local player's is known.
- **Owners.** A construct a player starts (sets its first part down, or spawns from the parts menu) is **theirs**, and
  what is built onto it stays theirs; the map's constructs are nobody's. A piece that falls away keeps its owner. A
  leaving player's constructs become nobody's.
- **Salvage.** With the tool the part needs (the attach tool) out, hold aim on a part: the same timed removal as taking
  a part off (60% of its fitting time), but it goes **into the inventory**, not the hand. Anyone may salvage a **wreck**
  (a piece cut off, or a broken-off part), a **loose part**, a construct **nobody owns**, or someone else's construct
  once it is **disabled** (any part of it wrecked: broken off or stopped working; `Construct.Damaged`). Someone else's
  working construct is refused: "that construct is someone else's: disable it first (shoot a part off)". Nobody may take
  parts off while someone drives it. The look line says "hold aim to salvage it" (or "to take it off as a wrecked part", or "someone else's:
  disable it to salvage").
- **Wrecked parts (not scrap).** A part that was destroyed (a wrecked part: broken off as debris, or stopped working in
  place) is **not** salvaged on site any more (decision 31, [Salvage haul](#salvage-haul-decision-31)): taking it off leaves
  a physical wrecked part, and a lone wrecked part can't be salvaged again.
- **Your own constructs.** With infinite parts on (Construct Yard), taking a part off your own construct puts it in your
  hand as before. With them off, it goes back into the inventory.
- **Building from the inventory** (the map's `"infiniteParts": false`). The rack hands out the next of its kinds that
  your inventory holds, and the parts menu only a part you hold ("no Piston in your inventory (salvage one)"); either
  takes one out of the inventory into your hands (what you held goes back first). Fitting or setting it down uses it,
  and the next of the same kind comes out into your hand at once, so a row of them needs no trips back. Dropping (T)
  sets the part down as a loose part (picked up again with F); dying puts a held part back into the inventory. With
  infinite parts on the rack and the menu stay endless and nothing is counted.
- **The HUD** while holding a part shows its count ("Wheel: ... 3 more in your inventory", "the last one in your
  inventory", or "Inventory: 3 (parts are endless here)"), and a line with the totals ("Inventory: 7 parts (4 kinds), 25
  material") whenever the inventory holds anything.
- **Debris expiry.** A **wreck** (a piece cut off, or a broken-off part) is cleared away once nobody has been within
  `debrisNear` metres of it (12 by default; or its own size) and nobody has been taking a part off it for `debrisSeconds`
  (180 by default; 0: never), checked every half second. Constructs players build and loose parts they set down never
  expire. The cap on bodies (64 in all) still stops new debris appearing when the world is full.

Choices made (the developer may change them): salvage always goes into the inventory, even with infinite parts on (so
the loot loop and the counts work on the yard); a construct counts as **disabled** as soon as any of its parts is
wrecked (there is no repair yet, so it stays salvageable); after a fit the next of the same kind comes into the hand
automatically; the debris defaults (3 minutes, 12 m); inventories survive death but not leaving or a new match; the
scrap rates by material.

## Salvage haul (decision 31)

Board item dev-sandbox-salvage-haul-sh01, the developer's decision 31 (2026-10-31). It replaces the on-site scrap above and is the
first piece of decision 14's refining chain.

- **No on-site scrap.** There is no scrap count on the player, no "salvage for N scrap", no Scrap Bin. Taking a wrecked
  part off a construct (the attach tool, the same timed edit) leaves a **wrecked part**: a single-part body that is
  darkened, damaged and carries the part's mass and material. It is a body whatever its size (a lamp or an engine); the
  physics gun, a hoist, a cargo volume or a vehicle carry it. It **can't be fitted** to anything (a wrecked part has no
  sockets to build on) and a lone wrecked part can't be salvaged again ("a wrecked part already: haul it to a Recycler").
  Debris that broke off is the same thing. Wrecked parts clear away like other debris. An **intact** part taken off is
  still a usable part (decision 7); the contested-construct rule for enemy wrecks is unchanged.
- **The Recycler** (`Constructs/Parts/Storage/Recycler.json`, attach tool, 220 kg: physics-only like any attach tool part): a steel
  hopper with a bin for its output. Leave a wrecked part in its **intake** (the box in `storage.recycler`) once nothing
  holds it (the physics gun has let go; it is not riding in a cargo volume) and it is taken in, one at a time, ten times a
  second. Intact parts and bodies with more than one part are never taken in. It processes for
  `baseSeconds + mass * secondsPerKg` (2 s + 0.04 s a kg, at most 15), then the material arrives in its bin; the part's
  **class** (its role's entry in the table's `roles`, else its `material`) and mass give the units. If its bin holds another
  material, or has no room, the part stays in the intake until it does. A **chute** (routed link) from the Recycler to a
  Materials Bin carries the output on (the Recycling Station blueprint does this).
- **Power.** In survival (infinite parts off) it needs the electrical system: a working battery whose links reach it with
  charge in it ("without power nothing happens"), and draws its `power` (600 W) only while it works. In **creative**
  (infinite parts on) it works instantly and needs no power.
- **The table** (`Constructs/Recycling.json`, format `call-of-slop-recycling/1`; `RecyclingTable`): `materials` are the bulk
  materials in id order (the wire id is 1-based: metal, wood, rubber, circuits, stone, glass, cloth, sand, the ores, dirt, then the refined copper, gold and planks), with their
  `colours` (the bin's fill); `classes` map a class to what it `gives` and `perKg`; `roles` map a part role to a class (logic,
  battery and alternator parts are electronics: circuits); the timing keys. Steel and armour give metal (0.5 and 0.6 a kg:
  a 120 kg engine, 60), wood wood, rubber rubber, concrete stone, electronics circuits (1 a kg), at least 1 a part. A bad
  file is refused with the reason and the built-in defaults are used. Material names are the later assembling step's inputs.
- **Bins hold one material at a time.** The **Materials Bin** (the old Scrap Bin, 600 units) takes any one; the first deposit
  sets it and it frees when empty (`CStore.Material`). A chute only moves like to like. F tips in the materials you carry, or
  takes a load of 100 into your carried materials (`SandboxInventory.Materials`, up to 9,999 each).
- **A load has weight.** What a bin holds counts in its construct's mass, centre of mass and inertia, at the fill's own place
  and on the segment the bin rides on, for every part with `storage.bulk`. `kgPerUnit` in the table gives each material's
  weight and `defaultKgPerUnit` any other's (metal 2, wood 1, rubber 1.5, circuits 1, stone 3, glass 2.5, cloth 0.5, sand 2,
  iron ore 4): 600 metal is 1,200 kg, and a full buggy leans to its bin and drives a third of the way in the same time. A
  weight outside 0.01 to 100, or one for an unknown material, refuses the file with the reason. See "Bin loads" below.
- **Spilling.** A broken bin spills up to six **Material Bundles**: wrecked parts of 100 units whose Setting is the material
  (`"yield"` in the part file); feed them to a Recycler to get the material back.
- **Old scrap.** Scrap counts from older inventories load as metal (one for one) in up to three Materials Bins set down
  beside the player, the rest carried (`ConstructLegacyScrap.Import`, `Game.SandboxImportLegacyScrap`). Nothing saves
  the Sandbox inventory yet, so there is no live caller; the function is what a world or inventory load calls.
- **Network (protocol 106).** The inventory message drops its scrap; a store (`CBody` Store) ends with its material (u8)
  and a flags byte (u8: 1 busy, 2 held off by its Run wire, protocol 111; 4 a machine's input follows, protocol 114). The wrecked part and its darkening are the existing `CBody` (Broken flag). The job itself (ticks
  left, units) is server-only.
- **Performance.** The Recycler step looks only at constructs with a Recycler (`Construct.HasRecycler`), counts a job's ticks
  and scans the intakes at 10 Hz; nothing allocates except the log line when a part goes in or comes out.

Code: `Source/Constructs/ConstructRecycler.cs` (the step), `ConstructRecycling.cs` (the table), `ConstructLegacyScrap.cs`,
`ConstructGive.DropWreck`, `ConstructStorage.cs` (materials in bins). **Self-test**: "Salvage haul" and "Salvage haul network"
(`Source/App/SelfTestSalvageHaul.cs`).

Choices made (the developer may change them): a wrecked part is a body at every size (the hands and backpack hold parts by
id, not wrecked state, so small wrecks are not hand-carried yet: use the physics gun or a cargo volume); the Recycler takes
a wreck from the physics gun only once let go; one part at a time; the intake is the hopper and the bin's inside; creative is
instant and unpowered; the rates above; bins take one material at a time.

## Gathering (decision 14)

The first step of the resource chain: raw material comes out of the world on its own, and it comes grouped by type (the
developer, 2026-11-02: plentiful, but a forest has a lot of trees and an iron vein is quite large).

- **On a planetoid** the field lies on the six faces of the cube round the world, underground kinds only, and a map's
  `"mix"` makes a kind commoner or rarer there (docs/planets/WORLDS.md "Ore"; `ResourceSphere.cs`). Code that asks the
  field passes a world point (`KindAt(Vector3, ...)`, `TappableAt`, `Tap`, `NearestUnderground`); the (x, z) calls answer
  nothing on a round world.
- **The field** (`Constructs/Resources.json`, format `call-of-slop-resources/1`; `ResourceTable`, `ResourceField`,
  `Map.Resources`). Each kind scatters large deposits, one `chance` per `cell` metres square: `forest` (wood, 60 a tree),
  `iron vein` (long, thin, 60,000 iron ore), `copper vein`, the rarer `gold vein`, `stone seam` and `sand bed`. Deposits are generated from the map's name, so
  every peer holds the same field and no map file changes. A world smaller than `minWorldHalf` (150 m) generates none, so
  the Construct Yard stays bare.
- **A map's say** (`"sandbox": {"resources": {...}}`, `SandboxResources`): `"generate": false` turns generation off, `"seed"`
  picks another layout, and `"deposits": [{"kind", "x", "z", "radius", "aspect", "yaw"}]` adds its own (`aspect` and `yaw`
  are optional). An unknown kind refuses the map.
- **Trees** stand on a 6 m lattice inside a forest, 80% of the cells, never on a solid box's footprint. A tree is known by
  its lattice cell (`ResourceField.TreeKey`), so only felled trees are stored or sent. They draw through the foliage
  renderer (`DrawAround`: 800 m around the camera, at most 30,000, rebuilt after a 150 m move or a felled tree), each with
  a drawn `FoliageType.Trunk`, because map trees get their trunks from solid boxes and these have none. A felled tree
  leaves a drawn `FoliageType.Stump` (map files can place neither), and a saw passes it by.
- **Solid trees** (`ResourceTrunks.cs`, `ConstructTrunks.cs`). A trunk is an upright cylinder, 5 cm of radius per metre of
  tree, up to the drawn trunk's top (62% of the tree; the canopy isn't solid). It is not in `Map.Solid`: trees aren't map
  data, so the content hash, map bake, pins and nav graph never see them. `PhysicsWorld.Forest` (the game's live world
  only, `WithTrees`) answers rays, body, point, box, capsule and `Gather` queries through `QueryLayers.Trunks`
  (`ShapeKind.Trunk`, its index the tree key): a look at the lattice cells the query crosses, behind a coarse forest mask
  and a small table of "in a forest" answers, with no allocation and nothing that loops over the world. So bullets (they
  stop, no decal yet), grenades, placing parts, the capsule mover and ragdolls all meet trunks with no code of their own,
  and a felled tree simply stops answering. The classic mover pushes a body round a trunk (`PushOut`). Each
  trunk near a moving body meets the body's solid boxes (wheels included) as a speculative contact (`ConstructPhysics.TrunkContacts`, server only: `ConstructWorld.Resources`),
  also for parts beyond a joint. Bot navigation leaves trunks out (`QueryLayers.Nav`, the nav build's as-built copy);
  bots slide round them with the mover.
- **Regrowth.** A felled tree grows back after its kind's `regrowSeconds` in `Constructs/Resources.json` (default 1800, 0
  means never). The server counts it in `ConstructGatherer.Step` (`ResourceField.Cut` queues it); a tree waits, trying again
  every 10 s, while a player or a construct stands where the trunk would be.
- **Saw Module and Drill Module** (`Constructs/Parts/Gathering`, attach tool, 900 W at tier 1, five tiers: [Machine tiers](production.md#machine-tiers);
  `storage.gather` holds `kind`, `rate` and the work box; `ConstructGatherer.cs`). The numbers here are tier 1's. The saw cuts the nearest standing tree in its work box, 12 wood a second. The drill,
  with its bit within 25 cm of the ground over an underground deposit, taps it at 10 units a second. The saw fills its own
  bin of 400; the drill has only a small buffer ([The drill's buffer and shaft](#the-drills-buffer-and-shaft)). F takes a load and a chute carries it on, as with the Recycler. A bin holds one material, so a drill stops
  over a different deposit until it is emptied. In survival they need the electrical system and draw power only while
  they work; in creative they need none (a drill may run from a drive shaft instead). The look line says what a module is doing, and an idle drill names the nearest
  deposit.
- **A drill digs a hole** (`DrillShaft`, `ConstructDrillShaft.cs`). One `DigKind.Scoop` through `Game.Dig` for every 60 units
  tapped (6 s) on a body that isn't moving, so the usual `Msg.TerrainDig` carries it to every peer and joiner and no wire
  format changed. `ConstructWorld.DrillDig` is the narrow hook: the game sets it, and with `TerrainDigLive` false (no
  terrain, Destruction off, or a test's world) the drill still taps and digs nothing. Decided, the developer may change it:
  the hole itself is the only state. Its depth is measured from the surface as built (`TerrainData.BaseGroundTop`,
  `TerrainEdits.TryBuiltTop`) and it ends 1.5 m down, after at most 6 scoops, however long the drill runs. A bit counts as
  touching within 25 cm of the ground, and also over a hole: not above the surface as built with the floor at most 2.25 m
  below (`DrillShaft.Touches`), so digging never moves the ground away. A drill lifted out stops; brought back, or a
  second drill set there, it resumes with no more ops. 1.5 m is chosen so a player who falls in mantles out: a deeper hole
  needs shelves wider than a half-metre column, which is a trap. Bounds: one scoop a gather step across the world, and
  drills stop at half of `TerrainEdits.MaxOps`, leaving blasts the rest. Each scoop is a little lower and narrower than the
  last, so the holes leave no overhang on open ground. An anchored construct never moves, so one over a hole stays where it
  is, hanging; a free one wakes and settles in (`Game.AfterDig`), and a loose drill keeps digging where it comes to rest.
- **Outcrops** (`ResourceOutcrops.cs`, `"outcrop"` in `Resources.json`: `tint`, `spacing`, `size`). Each underground deposit
  shows a long streak of rock clusters down its long axis, 3 to 6 rocks a cluster, iron ore rust, copper orange, gold yellow, stone grey, sand pale.
  They are worked out from the seed like the forest lattice, so they are the same on every peer and nothing is sent. Drawn
  through the foliage renderer as `FoliageType.Outcrop` (at most 16,000, on top of the trees' budget, in the same rebuild),
  never under a solid box's lattice cell, never where a stronger deposit is what a drill would find, and a map file can't
  place them (`Foliage.IsGenerated`). They aren't solid, stay when a deposit runs out (only the server knows what is
  left), don't follow dug ground until the list is next rebuilt, and like all foliage don't draw at foliage quality Off.
- **Network (protocol 111).** `Msg.ResourceFell` (u16 count, u32 tree keys) goes out as trees fall and to each joiner, and
  `Msg.ResourceGrew` as they grow back, so a joiner hears only what is felled now. How much a deposit holds stays on the
  server. A new match puts trees, deposits and regrowth timers back (`Map.RestoreProps`); nothing is saved with a world yet.
- **Performance.** The step runs at 10 Hz and looks only at constructs with a module (`Construct.HasGatherer`). Drilling
  allocates nothing; a part-cut tree takes one dictionary entry, a felled tree one more. A collision question costs a mask
  lookup per lattice cell outside a forest and a hash inside one.
- **The Run input.** Saw and Drill Modules, the Recycler and production machines have one data input, **Run** (`DataIn`, port byte 255, listed in
  the linking tool's picker; a blueprint wires it with `"links"` or `"wires"`). Nothing wired: they run as before. Wired: only
  while the signal into Run is on (`ConstructGatherer.HeldOff`, at least 0.5, adding the wires as an engine's run port does).
  A cut cable still counts as wired and carries nothing, so it holds the module off until it is mended; a deleted link is
  unwired again. It reads the part's own input, so it works on an anchored base with no seat. Held off, a module gathers
  nothing and draws nothing; a Recycler takes nothing in, and a job in hand waits without drawing, then finishes. The look
  line ends "held off: its Run wire is off". The flag is on the wire: bit 2 of a store's busy byte (protocol 112).

### The drill's buffer and shaft

The developer's request (2026-11-03): drills are more modular. A Drill Module is a compact rig (`machine_tiers.py`, `DRILL`): a 0.5 m housing with a
small hopper, a boom and a tower with a gearhead, its bit at the same place under the part as before, 200 kg at tier 1 (it was 320). It stays in the
same sockets, and side and back sockets stand 0.25 m from its middle, on pads at tiers 2, 3 and 5. Saw Modules are as they were.

- **The buffer.** 5% of the old bin at every tier: 20, 40, 70, 160 and 320 units, under 2 s of its rate. Full, it stops, draws nothing and takes no torque,
  and the look line says what to do: "buffer full: connect its Chute out to a bin, a Storage Volume or a machine", "its chute is cut", or "its chute has
  nowhere to put this" when the chute's end is full. F still takes a load.
- **Chute out.** A named chute port, out only, on the hopper's +x side (`"ports"`; a part that names only a chute output takes no chute in, by a link or a
  hose: `ConstructRoutes.IsInput`, `IsWorldInput`). It goes to anything that takes a chute: a Materials Bin, a Storage Volume, a machine's input, and through a
  hose to another construct. Its load is the tier's `chuteLoad` (10 to 160 a move, one move a second), so a chute keeps up with its drill at every tier.
- **Drive in.** A named shaft input on the gearhead (socket `drive`), facing up. Two ways to run: electric power as before, or a drive shaft. A shaft link ends
  there, or a drive part (a gearbox, a clutch, an engine's Drive out) bolts to that socket. It may come from an engine directly, with no transmission (the
  engine then needs nothing else: `Construct.HasPowertrain` holds for an engine with a drill on it), or anywhere on the drive line past the gearbox, through
  transfer parts, a power take-off's outlet or a differential's output (`Construct.BuildShaftFeeds`, `FeedShafts`: the reductions, what is left after each part's
  losses, the clutches' gates). A shaft can't cross a joint or leave the construct, as ever.
- **The rule (decision; the developer may change it).** With a shaft linked, the shaft alone runs the drill: it draws no electric power and needs no battery, and a shaft that
  isn't turning stops it with a reason, even with a charged battery there. "Linked" is the part's own link: a cut shaft is still linked, as a cut Run cable is. So a player chooses by what they connect, and nothing
  switches silently. Delete the shaft and it is on electric power again. In creative the rule is the same: creative waives electric power, not a shaft the player linked. The look line: "stopped: the
  engine on its drive shaft isn't running", "the clutch on its drive shaft is out", "its drive shaft is cut", "its drive shaft doesn't reach a working engine". The **Run** input holds it either way.
- **The rate.** Rated speed and load per tier (`storage.gather.ratedRpm`, `shaftTorque`; `DRILL_SHAFT` in the generator). The rate is the tier's rate times shaft rpm over rated rpm (at most 1), times the
  share of the load the engine can carry (`Construct.ShaftShare`).

| Tier | Rated rpm | Shaft torque | Buffer | Chute load a second |
| --- | --- | --- | --- | --- |
| 1 Scrap | 1,800 | 40 N m | 20 | 10 |
| 2 Workshop | 1,600 | 80 N m | 40 | 20 |
| 3 Industrial | 1,400 | 160 N m | 70 | 40 |
| 4 Military | 1,200 | 320 N m | 160 | 80 |
| 5 Plasma | 1,000 | 640 N m | 320 | 160 |

- **The load.** A working drill takes its torque off the engine, through its reductions and losses (`ConstructDrillDrive.Load`, at 10 Hz; `Carry`, each tick), never more than the engine gives at that
  rpm and throttle (a shut throttle still gives a quarter of its torque, `IdleTorque`). The wheels get what is left, fuel (or charge) goes by the load as well as the throttle, and an engine that can't carry
  it is pulled down and the drill slows: the look line says "overloaded: a lower gear or a stronger engine". A Plasma drill straight on a V8 is overloaded; through a Transmission's third gear (2.1:1) it isn't.
- **An engine runs for a driver or a signal** as ever, so on a base wire a switch into its `run` port and a lever into `throttle`. A lever gives idle or full throttle: a number from the logic sets speeds between.
- **Network (protocol 117).** Bit 64 of a store's flags byte: a shaft-driven drill's state follows (u8 `ShaftState`, u16 rpm rounded to 10, u8 share of its rate), so the look line reads the same on every peer. The drive line itself isn't sent: each peer works
  it out in `Rebuild`, and only the server runs the step.
- **Performance.** Part of the 10 Hz step: a drill looks through its construct's links (a few hundred entries at most) and allocates nothing. The list of feeds is made only for a construct with a drill on a drive line. With no drill on the line the engine's numbers are as before.

Code: `Source/Constructs/ConstructDrillDrive.cs`. **Self-test**: "Gathering" and "Gathering network" (`Source/App/SelfTestDrillDrive.cs`), "Link auto type" (the linking tool's whole chain into Drive in and out of Chute out).

Choices made (the developer may change them): the numbers above; the drill keeps a Chute out rather than leaving the module with no store at all; the buffer has no chute in; two drills on one engine share it by the sum of their loads; a drill turns the same either way (a reverse gear doesn't stop it); a sleeping free body with a running engine keeps the last rpm it had. **Saw Modules** could take the same split: `GatherRpm` and the rate are not drill-only in the step, so it is a data change plus a look line, but a saw's bin is what makes a tree a single trip's work, so it is a design choice, not done here.

### Bin loads

A bin's load is read from its store (`Construct.LoadOf`, through `StorePool`), never kept a second time, so every rebuild is
right after a split, an attachment, a spill or a part taken off. A deposit or a withdrawal, the two places a server's bulk changes,
only raises `ConstructWorld.LoadChanged`. `ConstructWorld.LoadStep`, at most four times a second (`LoadEvery`), compares each
body that has a bin (`HasBin`) with its last build (`LoadKey`, weighted by part, so a load moved between two bins of one body
counts as a change) and redoes only the mass properties (`Construct.RefreshMass`: the same `BuildMass` and `BuildSegMass` a
rebuild uses, no drive line, links or power). Physics reads `Mass`, `InvMass`, `Com` and the inertia every step, so the
wheels, the engine's load and tipping all follow; the pose stays and the velocity stays with the centre of mass. The server
wakes a free body it changed, so a parked truck settles under its load. Clients run the same step from the stores they hear
(their mass shows in the physics gun's refusal and the look line, their centre of mass in the culling), and nothing about
mass goes on the wire. An empty bin changes no number, so the seeded digests stand. A carried body's load reaches its
carrier through the cargo step. Blueprints hold no bin contents and nothing saves a world yet, so a load isn't saved.
Self-test: "Bin loads", "Module Run input" and "Modules network" (`Source/App/SelfTestModules.cs`).

Not yet (choices made, the developer may change them): a stump isn't solid and a tree doesn't die or take damage from a crash;
a trunk takes no bullet decal; a drawn tree over dug ground keeps its height until the next redraw (its collider follows the ground); bots don't plan
round trees; a drill's hole is one depth, with no way to sink a deeper shaft; parts stored in slots add no mass (only a bin's bulk does).
The "Gathering" and "Gathering network" self-test sections cover the field, both modules, holes, outcrops and the wire
(`Source/App/SelfTestGathering.cs`, `SelfTestGatheringDrill.cs`); "Terrain digging" climbs out of a drill's hole with both movers.

By hand, the ground drill digs the ground itself into carried dirt, stone and sand (`dirt` is a bulk material in
`Constructs/Recycling.json`) and puts it back: [Sculpting](../TERRAIN-DIGGING.md#sculpting).

## Scoops

A scoop is a bucket for an arm: a bin with a cutting edge. Push the edge into the ground and it digs out a chunk, and what it cut lands in the bucket as bulk material. Tip it and that runs out over its lowest rim, into a store underneath if there is one that takes it, else onto the world. Parts in `Constructs/Parts/Scoops`, written by `tools/constructs/scoop_parts.py`; code in `Source/Constructs/ConstructScoops.cs`, the game's side in `Source/Game/GameSandboxScoops.cs`.

**The styles.** Four, in the four size classes (16 parts; the inventory has them under Production, Scoops):

| Style | Small width | Holds (small) | What it does |
| --- | --- | --- | --- |
| Digging Bucket | 1.25 m | 7 | The all-rounder: teeth, two round bites side by side. Slowed to 0.4 in stone. |
| Grading Bucket | 1.75 m | 7 | Wide and shallow, a plain lip: three flat bites across, each a `DigKind.Crater`. Holds the same on a lighter cut, poor in stone (0.12). Tips soonest. |
| Trenching Bucket | 0.75 m | 4 | Narrow, deep and strong: one round bite the width of its edge. 0.6 in stone. |
| Rock Bucket | 1.25 m | 6 | Bars and big teeth: full speed in stone, and feels its hardness least. Dirt and sand mostly fall through the bars (`keep`: 0.3 and 0.1). |

Medium is 3 m (the Trenching 1 m, the Grading 5 m), Large 12 m, Capital 48 m. Capacity is the fill box's volume at 10 units a cubic metre (`Constructs/Digging.json`), so what a bucket carries is what it dug and it fills back the same ground. Mass goes with the plate. A scoop goes on by its `back`, `top` or `under` socket.

**The data.** A scoop is a storage part with `bulk` (material `any`) and a `scoop` block, in the part's frame: `edge` (two points), `rim` (two points: the far side of the mouth), `cuts` (the way the edge points), `mouth` (the open side), `reach` (how deep the edge counts), `cutForce` (N, on dirt), `biteRadius`, `bites`, `flat`, `spillDegrees`, `pourSeconds`, `stoneRate`, `keep`. The interior is the bulk fill box. Being a bin gives it the rest: F takes a load or tips in what you carry, chutes, mass, the drawn fill rising with the load, the look line, spilling when it breaks, the wire, saves. Blueprints carry the part, empty, like any bin. Beacons put nothing in one. The ground's numbers are the `scoops` block of `Constructs/Digging.json`.

**Digging** (each tick, server, awake bodies only). The edge's real motion is read from where it was a tick ago, so a hinge, a piston, a rail and a whole vehicle are the same thing. A parked scoop moves nothing and digs nothing.

- *The ground gives way.* While the edge is in the ground (`Engaged`) the scoop's boxes stay out of the ground's contacts (`ConstructPhysicsContacts`, `ConstructCollider.Leaf.Scoop`) and one row at the edge stands for them (`ScoopRows`): a contact whose impulse is capped at the ground's strength (`Contact.Cap`), with a friction limit of its own (`Contact.Drag`). Under `cutForce` it stands on the ground; past it, it moves. So a parked bucket rests, a strong arm goes in and a weak one stalls. `cutForce` is never under 2.5 times the bucket's weight.
- *Bites.* Moving at least 3 cm/s with a fifth of that along the way the edge points, it sweeps edge width x depth x speed. Swept ground adds up to a bite: one to three balls just ahead of the edge, taken out by the usual `Game.Dig`. Each is measured first (`TerrainEdits.Measure`), so what lands in the bucket is what left the ground, down to fractions kept in `CStore.JobAmount`. Pushed faster than it bites (at most one bite in 0.25 s, four in a tenth of a second across the world), the ground holds it harder.
- *What it gets.* The ground's own material, through the table: dirt, stone under 3 m or on rock, sand, or what a fill laid. A scoop holds one material, the most of its first bite. Later bites give only that; a bite that is less than half that stops it ("this ground isn't dirt: tip it out first"), and the ground holds it as if it were full. A scoop given ore by hand or chute doesn't dig.
- *Full.* It stops digging. The ground holds it at `fullForce` (8) times its cut force against going deeper, and lets it go coming out. An edge that isn't leading is the same. A scoop that isn't in the ground meets it with its boxes, like any part.
- *Wedged.* Ground goes in half-metre columns, and a bucket can lodge on one between the points it feels at. A joint it hangs on pushing past 1.5 times its rating within 30 cm of the ground counts as being in it, so an arm can always bring a full bucket out. Bedrock and the map's edge can't be cut: it meets those as a solid.
- *Limits.* Scoops stop at 75% of the terrain log (`scoops.logShare`). A loose scoop, a broken one, and any scoop where ground can't be dug (`Game.TerrainDigLive` false: Destruction off, no terrain) is solid and doesn't dig. A planetoid digs through the same calls; that hasn't been run.

**Tipping** (10 Hz, sleeping bodies too while there is a load). Where gravity's up stands against `mouth`: past `spillDegrees` the contents leave at up to capacity / `pourSeconds`, a fifth of that just past the angle, all of it about 20 degrees past. With no gravity nothing falls out, and with its lip in the ground nothing can.

A ray goes straight down from the middle of the lowest side of the mouth, up to 80 m:

- A store it hits first takes it: a bin, a machine's input (`Feed`), an Assembler's, or a Storage Volume, whose box counts though it isn't solid, as long as the ray reaches the box first. A truck bed is a Storage Volume sized to the bed, or a bin on the deck. Its own construct counts, so a machine can load itself. What a full or unsuitable store won't take carries on as below.
- Open ground takes **dirt, stone and sand as fill** (`DigKind.FillDirt`...), a lump at a time (an eighth of the bucket, at least 4 units), on the lowest spot just round where it lands, so a pour spreads into a heap. It is ground like any other: scoops and the ground drill dig it back for what it was. The drill's rules apply (nobody buried, 16 m over the surface). A fill never holds more than went in; the last few units of a load, too few to fill anything, are lost.
- Anything else: **Material Bundles** fall from the lip with the scoop's velocity. That is on a construct that won't take it, a building, ground that refused the fill, ore, or Destruction off. A bundle is worth what went in (`CPart.Rounds`; the Recycler gives that back; a broken bin's bundles too now, which were worth 100 however little was in them). They are debris: they clear away, and only a Recycler takes them back. A Cargo Volume carries them once they are in it, as any loose body.

**The look line.** `Digging Bucket: 12/40 dirt, digging - full: tip it out - F: take 12 dirt`. `Busy` says digging, and `CStore.Blocked` says why it stopped or where a pour is going, both on the wire with the store. Nothing new on the wire, so the protocol stands.

**Cost.** 96 scoops awake near the ground: about 0.34 ms a tick in the container, nothing allocated. A sleeping one costs a test, or a look at its angle at 10 Hz while it holds a load. Every scoop takes a store handle, and a world has 128, like every store.

Choices made (the developer may change them): 10 units a cubic metre, the sculpting rate, so scoops and the drill balance, which makes a cubic metre of dirt 15 kg (`kgPerUnit` 1.5), so a load barely weighs on an arm; one material a scoop; the part of a bite that isn't what it holds is lost, as is what a skeleton bucket drops; fill for ground kinds, bundles for the rest; a pour lands at once, with nothing drawn falling; the fill draws as a box rising from the bucket's floor and doesn't tilt with it; no clamshell, since one needs moving jaws and has to hold its load upside down; a scoop needs no power.
