# Vehicle kit: spawning in play

Spawn context, how it scales, dynamic spawn and behaviour data. Part of the vehicle kit guide: index in VEHICLEKIT.md.

## Spawn context

Roadmap phase 7 (`VehicleContext.cs`, `Source/Game/GameSandboxVehicleContext.cs`): the place a vehicle is spawned nudges its build, while the faction's style still decides how it
looks. A desert Bandit truck is a Bandit truck on bigger tyres in the sandier half of its palette; an IT van in the snow has chains.

**The record** (`SpawnContext`): `Biome` (neutral, desert, snow, swamp, forest, road), `Gravity` (times the world's standard), `Air` (density, 1 at sea level, 0 off world),
`Wood` and `Ore` (0 to 1: how much of the place is forest, how much metal ore lies under it), `Rain` (the weather's, 0 to 1) and `Read` (how the server read it, for the log).
It *matters* when the biome isn't neutral, gravity is under 0.75 or over 1.3, the air is under 0.2, or wood or ore is 0.5 or more; anything else is neutral, and **a neutral context
makes exactly today's vehicles**: every hook below is a no-op unless `SpawnContext.Current` matters. Measured: `--gen-vehicles` all 123 identical; on the branch's base and on
this branch the same 8 convoys (40 members, every style) and 8 dynamic kit-1, kit-3 and kit-4 seeds wrote the same text, but for the one vehicle the chassis fix changed (Bandit
convoy 3's leader, "Families and convoys"). No kit version: no existing seed changes meaning.

**Reading it** (`SpawnContexts.At(ground)`, server only, at the request): five ground samples (the point and four 8 m round it) through `TerrainData.Sample`, `SampleNormal`,
`Locate` and `Material` (dug and streamed ground read right; no loop over the grid), the resource field's deposit under each (`KindAt`: a forest for wood, iron, copper, gold or
levite for ore), `GravityFrames.GravityAt` over `World.Gravity` (the host's setting and a planetoid's field included), `ConstructAir.Density`, and `Weather.RainHere` (the
weather's own reading, at the player, who is near: the parts menu sets vehicles down in front of them; 8 mm/h is 1). The biome is the first that holds: **snow** within 20 m
under the surface's snow line or above it; **desert** on sand (the material, or under the sand line) under 3 of the 5 samples; **swamp** within 4 m of the terrain's lowest ground,
nearly flat, in rain of 0.4 and more (maps have no water yet); **forest** in a wood (3 of 5 samples) or on rough ground (rock or slopes over 15 degrees under 3 of 5); **road** on flat
bare dirt; else neutral. A map with no terrain (Construct Yard) or a planetoid reads neutral ground (gravity and air still count). 0.01 to 0.08 ms a reading on this box. A
probe over 100-odd points of each map read Adobe Mesa 86 neutral, 20 rough, 4 desert; Alpine Pass 48 rough, 5 desert (its snow line is above the play area); Proving Ground road 10, forest 21 (8 of them a wood), ore under 18.

**What it leans** (each only among the style's own parts and colours, or a spring's own class as kit 3's swaps are; none rolls a die of its own: a weighted pick takes the same
one roll the plain pick did, so the rest of the seed rolls as it does in a neutral context):

| Context | Running gear | Body and kit |
| --- | --- | --- |
| desert | tyres: the biggest the style's gear has that may go there (a front up to 0.56 m, a rear under a body never bigger, a Dually Wheel at the rear where the style has it); a wide front wing weighs four times | body and panel paint from the style's colours nearest sand |
| snow | Spiked Wheels (the kit's chains) where the style's gear has them, no bigger; second axles 1.6 times | paint nearest snow |
| swamp | the biggest tyres, the longest-travel spring of each corner's class (Hydraulic Suspension); second axles twice | paint nearest mud |
| forest | the biggest tyres, long-travel springs (high clearance); the rear axle half a metre further in and the set-back front station four times as likely (a short wheelbase); second axles 0.6 times | segments lean short (-0.45); paint nearest the woods |
| road | Slick Wheels where the style has them; the shortest-travel spring of the class (a lower, stiffer ride); one axle twice as likely, second axles 0.4 times | segments lean a little long |
| low gravity (under 0.75) | second axles twice (more ground contact); a wide front wing | plates lean to the lightest of the style's (sheet over plate steel) |
| high gravity (over 1.3) | the strongest spring of the class (Heavy Suspension, Heavy Double Wishbone, Heavy Swing Arm); second axles 1.8 times | room sections lean low (tall 0.4 times, low 2.5 times); segments a little short |
| no air (under 0.2) | | every engine slot is the Electric Motor, a Battery Pack on a free side of it or its gearbox (else on the frame), no fuel line (the tanks stay, still a weak point) |
| wood (0.5 and up) | | plate slots that list Wood Planks lean to them (up to six times) |
| ore (0.5 and up) | | plate slots lean to their metal (plate steel, tread, armour: up to four times) |

A kit-1 shape (the Technical, the trailers, the Tanker Rig) has no chassis layer: its recipe pick leans the same way (`VehicleContext.Pick`), then its tyres and
springs as above. A swap never makes a chassis whose corners hang differently (`VehicleChassis.Level`). How strongly a style leans is its row in `VehicleContext.ByStyle`
(additive: `Styles.cs` is untouched): the Bandits 1.2 (they build from whatever is lying about), IT and the Call Centre 0.7, the British 0.6 and only their panels take the
ground's colours (the bus stays red unless a listed colour is near the ground's), Highland's panels only (the Saltire stays). A style not named takes `VehicleContext.Default`.

**Not done, for want of parts:** dust filters (the only "Filter" is a logic part), cold-weather kit, floats (no buoyant part, and maps have no water), and tracks: no style
lists a track, so "tracked where the style has tracks" never fires. Gearing isn't touched: every style lists one gearbox and no ratio is configurable on it, so a road vehicle is
faster by its tyres and its single axle. Off world the Electric Motor is 10 cm lower than an engine and half a V8's length, so a hood stands on a Drone
Battery on the motor's top (`Vehicle.HoodOn`: within 2 cm of where it always stands), and plated hoods (decked at the engine's height) and cab-overs weigh more in the grammar
(`VehicleContext.Module`) and a kit-1 hood slot; a moulded hood that still meets the gearbox fails as it is built and the seed plans again, and a request that makes nothing
electric is refused with "no air here: no ... could be made to run without it" rather than given a committed seed that burns fuel. The full check takes an Electric Motor's
battery for its fuel.

**A family's model in another context** keeps its family: the same body and chassis (modules, axle stations and outline to the decimetre), so a convoy reads as one fleet
wherever it is met. Only the context's kit changes: tyres within 5 cm of the model's own size and no bigger (Large Wheel to Slick on a road, Off-Road to Spiked in snow), paint
from its colours nearest the ground, plate materials (wood, ore, light: the same plates, another material) and the electric drive off world; no chassis table, length, body
range or spring lean (`SpawnContext.KitOnly`, set when the request is a model's). Its key stays the same: the context is the request's, not the model's.
`--context "Bandit/command/3/553571/mk1/scheme 292943" --sweep` prints, for each context, the kit that changed and whether the body and chassis stayed the family's.

**What it carries.** A vehicle made in a context that matters is named for it ("Bandit Box Truck 4711 [desert, ore]", after its role and model labels), so it never reads as a
committed seed or as the neutral vehicle; its blueprint carries a second comment, `// context desert, gravity 1.00, air 1.00, wood 0.00, ore 1.00, rain 0.00; changed chassis
table leaned (desert), tyres Large Wheel/Dually Wheel, deck plates leaned (ore), rust paint desert (#8c5230/#94603a), ...` (`Vehicle.ContextLine`, `Context`, `ContextNotes`),
and the spawn's "generated" line ends `; context ... (read: sand under 4 of 5 samples)` (`VehicleGenJob.ContextNotes`). Nothing is on the wire.

**The API.** Every request takes it (`context: null` is neutral): `Request(style, shape, ..., context: SpawnContexts.At(ground))`, `Request(style, role, tier, ...)`,
`RequestModel(model, ..., context)`, `RequestConvoy(..., context)` (one context for the whole convoy, read at its anchor), and the kit's own `VehicleRegistry.Make(g, seed, ...,
context)`, `VehicleFamilies.Make(model, context)`, `new VehicleGenJob(g, seed, layers, role, context)`. The parts menu's three requests read the ground they set the vehicle on.
Rerolls and fallbacks keep the context; off world, a request with the host's committed seeds only is refused at once, and a fallback to a committed file is refused.

**Measured** (2026-11-07, `--context ... --sweep --check`; the 2-core box shared with other agents, so costs are rough):

| Run | Made | Full check | Identity | What changed |
| --- | --- | --- | --- | --- |
| Bandit Box Truck 2 and 3, every context | 20 of 20 | 20 of 20 | 0.93 to 0.97 a context | the chassis table in 14 of the 20, tyres in 9 (Large, Spiked, Slick), springs in 5, paint in 5, plates in 5, a low profile in 2 |
| IT raider tier 2, seeds 1 and 2 (Technicals, kit 1), every context but no air | 18 of 18 | 13 of 18 | 0.94 to 1.00 a context | the recipe pick in 12 of the 18, tyres in 9, springs in 8, paint in 4, plates in 4; the five failures are seed 1's top fill (85 %, at most 84 %), which "wood" fails with nothing changed: not the context's |
| Bandit Box Truck 2 to 9, no air | 8 of 8 | 4 of 4 tried | 0.92 to 0.95 | Electric Motor, Battery Pack, a Drone Battery riser under the hood |
| IT raider tier 2, seeds 1 to 4, no air | 3 of 4 | | 0.95 | one Technical's Snub Hood still met the gearbox: refused |
| Model Highland/carrier/2/5/mk2 (a War Bus), every context | 10 of 10 | 10 of 10 | | body and chassis its family's in all ten; tyres (Spiked, Slick at the rear), panel paint, plates, the electric drive |
| Model Bandit/command/3/553571/mk1 (Box Truck), every context | 10 of 10 | | | the same body and chassis (modules, axles, outline to 0.1 m) in all ten |

Generation in a context costs what it does neutral plus the drive check (15 to 50 ms a plan); a context's own work is a few dictionary lookups a part pick. In slices
(`VehicleGenJob` with a context, 2.5 ms a slice) a desert-and-ore Box Truck wrote the same hash as made directly.

## How it scales

Measured on this box (2 cores) with a mix of the 19 shipped vehicles on Proving Ground, driven directly in a `ConstructWorld`; no clients:

| Vehicles | Parts | Spawn each | Memory | Parked ms/tick | All driving ms/tick (worst tick) |
| --- | --- | --- | --- | --- | --- |
| 10 | 1,568 | 9.4 ms | 1 MB | 0.08 | 0.50 (1.3) |
| 50 | 7,820 | 8.5 ms | 5 MB | 0.44 | 2.53 (4.2) |
| 100 | 15,640 | 8.2 ms | 9 MB | 0.59 | 2.14 (4.9) |
| 200 | 31,203 | 8.1 ms | 19 MB | 1.67 | 4.90 (7.6) |

Parked vehicles sleep, so a map of them costs little; a spawn is 4 to 16 ms, so spread big groups over ticks. A blueprint file is 9 to 21 KB. Generating one takes 0.2 to 1.1 s (the socket search, on the same thread), so it can't run in a tick.

**Files or generated.** Keep shipped vehicles as a few seeds a shape, for review and the menu. Don't commit hundreds: the game builds each blueprint once at start-up to check it (about 8 ms each), and the Blueprints tab's spawn message carries the index as a byte (`CActKind.Blueprint`), so 255 files is a hard limit (204 blueprints ship today). Variety comes from dynamic spawn instead: any seed, made on the server when it is asked for, with nothing on anyone's disk.

## Dynamic spawn

The server makes a faction vehicle when it is asked for, from (style, shape, seed), by running the generator in the game (`Source/Game/GameSandboxVehicles.cs`, `Source/VehicleKit/VehicleSpawn.cs`).

```csharp
game.VehicleSpawn.Request("Bandit", "Box Truck", seed: null, ground, yaw, owner: 255, frontOf: 255, done: (built, name, why, owner) => ..., layers: new(Wear: 99));
```

`seed` null: the server picks one (1 to 999,999). `layers` (optional): kit 3's per-layer seeds ("Layered variety"), kept if the seed rerolls. `ground` and `yaw` say where it stands, or `frontOf` names a player it is set down in front of, as they stand when it is ready (the parts menu's way). The return is why it was refused, or null: queued. `done` hears how it went, on the tick. At most 8 wait, 2 from one player.

**The parts menu.** The Blueprints tab has "Faction vehicles: new seed" (on by default) and a seed box (0: any); by role and by convoy: "Families and convoys". With it on, a faction vehicle's tile (a blueprint named "Style Shape seed") asks for a new one of that style and shape (`CActKind.Vehicle`: the style's and the shape's names and a seed; protocol 186); off, the tile spawns the committed file as before (`CActKind.Blueprint`, which tests and every other blueprint still use). The host also has "Server generates them": off (`VehicleSeeds.Committed`), every request gets a committed seed of its style and shape, the asked one when it ships, so a session plays only what CI checks.

**Only the server generates.** A built vehicle is an ordinary construct: clients get it as bodies (`CBody`), as they get anything built, so nothing about the seed is on the wire and a client never runs the kit. That is simpler and safer than clients regenerating: no machine-to-machine determinism to trust, no fallback to send a blueprint when a hash disagrees, and nothing changes when a client's build differs in a way the protocol doesn't catch. The text's FNV-1a hash is in the log line (`[vehicles] generated Bandit Box Truck 733786 (hash ...)`), and a seed always writes the same text: in game and in `--gen-vehicle` alike (the first shipped seed of every shape in every shipped style, made in slices this way, matched its committed file byte for byte). It saves and loads with the world and is salvaged like anything else, because that is all it is.

**Off the tick, but never beside it.** A generation costs 5 to 900 ms (measured on this box, warm: a Fuel Trailer 5, a Box Truck 180 to 400, a War Bus 420 to 560, the Double Decker 900), far past the 8 ms tick. `VehicleGenJob` runs it on a thread of its own, but that thread only runs inside `Step`, while the tick that called it waits: the tick gives it `VehicleSpawner.SliceMs` (2.5 ms) and it hands back at the next `VehicleGenJob.Yield`, which the kit's own steps call (each socket try in `Put`, after each part, weld, wire and link; between steps of the game's own build code, never inside one). So the two threads never run at once, nothing the game shares (the snap build's caches, the weapon builder, the statics `World.Enter` swaps) is touched by two threads, and no part of the game had to be made thread-safe. The cost: a generation's time comes off the tick's, a slice at a time, so a Box Truck takes 100 to 600 ticks (2 to 10 s; a seed that plans three times is the long end) and a Double Decker about 250. A slice that hasn't handed back `VehicleGenJob.LateMs` (50 ms) past its budget is late: logged, and the job is dropped at its next yield (the request falls back). The tick still waits for that yield, however long (a line every 10 s names where it is): it never runs beside a generation thread, and `Abandon` waits the same way. A dropped job throws at every later yield, so a handler that swallowed the first throw can't run on beside the game. (Until 2026-11-10 a job stuck 10 s was abandoned and its thread ran on with the game till its next yield.) Never yield under a lock the game thread can take (`HullLoft.Parse` holds one while it makes a part): the game would wait on the lock while the kit waits for its turn. `--slice-vehicle NAME [--seeds N] [--spans K]` makes seeds as a spawn does and lists the longest and costliest spans by their yields. `Yield` costs one thread-static read anywhere else, so the command line and the self-tests run as before.

**Short spans.** A slice ends at the first yield after its 2.5 ms, so a slice is its budget plus the span it was in. Measured span by span (a stopwatch from each yield to the next, labelled by call site; 2026-11-06), the long ones were: `Put`'s scan of every free socket of every part before its first try (about 1 ms on a 300-part body, it now yields per part); a part's attach and the construct's rebuild after it (now a yield between them, and the same in `Weld` and `Wire`); `Fill`, the grammar's silhouette measure, 4 ms warm and 20 ms cold (`VehiclePlan.Outline`: it now yields per part and every 128 triangles, keeps its face and triangle buffers for the next call instead of a megabyte or two on the large object heap each plan, and makes its two projection delegates once instead of twice a triangle); a plan again, whose new world and plan ran with the failed plan's measure (yields either side of it); and the blueprint text (a yield every 64 steps), its hash and its parse, each a span of its own. The rebuild itself was never the cost (about 0.1 to 0.6 ms on a big body). Warm, no span averages over 0.2 ms and the longest ones are 1 to 3 ms; past that on this box it is the machine, not the kit (below). The output is the same: every committed seed `identical`, and a sliced job's hash equal to a direct run's for kit 1 and kit 2 seeds of every shape tried.

**The first one.** Without a warm-up the first generation in a process paid for compiling the kit and the build code it calls: about a second in all, with spans of 40 to 80 ms (the first rebuild of a construct with guns, the first link). In the command line it also loads the parts (1.8 s), which a game has done long before. `VehicleGenJob.Warm` runs a Bandit Technical once, at once, in a world of its own (written, hashed, parsed, measured as a spawn's is) and plans a grammar Box Truck without building it, when the server starts its first Sandbox match (`ResetSandbox`): 700 to 900 ms on this box, once a process, logged as `[vehicles] warmed the kit in N ms`. A first spawn's longest span after it is 3 to 6 ms (the grammar's builder, compiled on its first use).

**Generation time** (2026-11-10, perf/kit-gen). `--slice-vehicle NAME --profile [--seeds N] [--first S]` makes seeds straight through on one thread (no slices, so no hand-offs in the times) and prints where a generation's time went by stage (`KitProfile`, VehicleKitProfile.cs: off unless a probe turns it on; stages nest, so it reads as a tree). Before, one generation (mean of seeds 1 to 6, ms; this box at load 5 to 10):

| Stage | Bandit Box Truck | Highland War Bus | IT Technical | British Car |
| --- | --- | --- | --- | --- |
| whole generation | 467 | 1148 | 681 | 335 |
| build (the plan's parts) | 336 | 950 | 503 | 173 |
| `Put`'s scan, tries in it (calls) | 278 (143) | 725 (200) | 402 (65) | 78 (47) |
| `Put`'s tries, `ConstructBuild.Place` (calls) | 40 (4604) | 79 (6223) | 21 (1851) | 8 (1310) |
| `Onto` | 14 | 45 | 16 | 16 |
| rebuild after a step | 11 | 47 | 21 | 12 |
| lofted parts made | 6 | 77 | 25 | 36 |
| drive gate / identity gate / brief | 24 / 38 / 66 | 35 / 86 / 65 | 38 / 95 / 42 | 32 / 60 / 66 |
| fill, text, parse | 7 | 19 | 14 | 10 |

`Put`'s scan was the cost, and not the rules it asks: every try out of reach (most of them) wrote the refusal's text at once, with a pass over every part's boxes for its blocker, though only a refusal ever reads it; and it asked whether each socket was taken by walking the part's links, socket by socket (a lofted bay has hundreds of both). Now (`BlueprintScript.Put`): parts and sockets too far for the part's centre to land within reach + 0.5 m of the place asked for are passed over (the part's own arm from socket to centre, plus a part's farthest socket, `SocketRadius`); a try out of reach is only remembered as the nearest, never asked of the rules; the refusal (with the nearest try's reason and blocker) is written only when `Put` refuses; a part's taken sockets come from one walk of its links (`UsedSockets`). Same parts, sockets and order, so the same picks and the same refusal text: `--gen-vehicles` all 193 identical, `--gen-interior` of the Titan Walker byte for byte as before. Tries a generation: Box Truck 4604 to 238, War Bus 6223 to 341. `--gen-vehicles` took 90 s of CPU before and 38 s after (one run each, loaded box).

Sliced as a spawn makes them (`--slice-vehicle NAME --seeds 5`, the same seeds, old and new build one after the other at load 12 to 30; medians, ms): Bandit Box Truck 897 to 279, Highland War Bus 1127 to 704, IT Technical 306 to 207, British Car 305 to 195. What is left in a War Bus: the brief (its rays), lofted bays (made under `HullLoft`'s lock the first time a key is asked for: already cached by key across plans and seeds), the identity and drive gates, the rebuild after each step (about 1 ms a step on a 180-part body; the construct's journal only starts at 1024 parts, so it isn't used here).

The warm-up's gunsmith test fits (35 of them) cost about 0.3 s now: they build turrets through `Put` and were slow for the same reason, so they stay in `Warm` (no cache file: a fit depends on the build rules as well as the parts, which a key of the parts' data can't see). The rest of a command-line warm-up is loading and compiling (6 to 10 s on this box loaded, much more cold); in a game the parts are loaded long before.

Long spans still seen sliced on a loaded box (a span's length here is mostly the machine): `VehiclePlan.AddBody` on one part with many shaped boxes (7 to 50 ms), `Onto`'s fit and z-fight test with its attach (13 to 70 ms), a drive-check physics tick (30 to 75 ms). The silhouette profile's raster (`VehiclePlan.Profiles`, 30 to 85 ms in one span) now yields every 128 triangles and makes its two projections once.

**An honest slice.** A slice over `VehicleGenJob.OverrunMs` (4 ms) is logged, the first 10 and then every 100th: `[vehicles] slice 7.4 ms in Bandit Box Truck 733786 (budget 2.5, the kit ran 7.4 of it): longest span 5.7 ms, VehicleCore.cs:226 to VehicleCore.cs:157`, with ", a garbage collection ran" when one did. `Yield` takes its caller's file and line from the compiler (constants: nothing is allocated), so the two yields named are either side of the long span. "The kit ran" is the slice's spans added up: when it is near the slice, a span needs a yield inside it (or the machine took the CPU in it); when it is much less (2.5 of 5, 0.3 of 5), the thread woke late. The "generated" line gives the generation's longest span and where it ran.

**Setting it down, a slice a tick.** Building the blueprint (`ConstructBuild.BuildOnGround`, all at once) cost 6 to 20 ms for a big vehicle (a Box Truck 6 to 10, a War Bus 7 to 10, the Double Decker 12 to 19, measured on a tick on this box; 37 ms when first measured), past the tick's 8 ms. Two changes:

- Plain waste, gone: a step looked its part up by walking every part's name (now a dictionary, `ConstructParts.Find`), each wire rebuilt the whole body (now once after all of them, `AddWire(rebuild: false)`), and each part's auto-link allocated a list and walked every touching part's links before checking where their sockets were. That halved it (a Double Decker about 7 ms in all), with every blueprint building byte for byte as before (all 204, two poses each, compared with the old build).
- What is left runs in slices (`ConstructBuild.GroundJob`). The steps run a `VehicleSpawner.SliceMs` (2.5 ms) slice a tick on a `ConstructWorld` of their own, which nothing simulates, saves or sends; no step is over 1 ms (a freeform weld's rebuild), so a slice ends within about a millisecond of its budget. Then, on a tick of its own, the body joins the real world whole (`ConstructWorld.Adopt`: a new id, its parts' tags and stores from this world, the caps) and what follows the steps runs there at once (`FinishBuild`: wires, routed links, hoses, ropes, tracks, the rebuild, cargo; then the loot roll and standing it on the ground): 1 to 3 ms. Where it stands is chosen at the join, so "in front of the player" is where they are then. A tow's steps follow its tower's the same way and it joins on the same tick. Anything a fresh body doesn't hold (a merge that popped a part off, a store holding parts) isn't adopted: it is built again at once (logged as `[build]`), correct at a tick's cost.

Measured in game (host and a `--net-client` over RakNet at 80 ms and 2% loss, Construct Yard), the worst server tick while setting one down: Box Truck 4.0 ms (2 ticks of steps, join 3.1 ms), War Bus 3.0 ms (3 ticks, join 1.1 ms), Double Decker 2.6 ms (3 ticks, join 1.3 ms), the RV 2.7 ms; the client's copy had the same parts, links, joints, routes and mass. A blueprint of at most `VehicleSpawner.ImmediateSteps` (48) steps, every hand-made one but the RV (under 2 ms), is still set down at once.

The parts menu's big blueprints (a faction vehicle's file with "new seed" off, the RV) queue in the same `VehicleSpawner` (`RequestBlueprint`: the same limits, "placing the ..." then "the ... is down"), so they arrive a few ticks later rather than on the click. The survival start kit and a map's own constructs are still built at once: that is the match's start, not a tick in play. Self-tests and the kit's checks build at once too (`BuildOnGround`, unchanged).

**What a spawn costs a tick** (2026-11-06; an offline host on Construct Yard, the game's own `Update` timed tick by tick while a request is generated and set down; the same throwaway probe on main before and on this branch after, on this 2-core box with other agents building, load 3 to 5):

| Vehicle (kit) | Worst tick before | Ticks over 8 ms before | Worst tick after | Ticks over 8 ms after |
| --- | --- | --- | --- | --- |
| Bandit Box Truck 733786 (2, three plans) | 42.2 ms | 51 of 419 | 6.1 to 8.5 ms | 0 to 2 of 490 to 640 |
| Bandit War Bus 5001 (2) | 24.0 ms | 23 of 280 | 7.9 to 10.8 ms | 0 to 1 of 400 to 480 |
| Bandit Flatbed Fort 5002 (2) | 12.0 ms | 8 of 195 | 6.0 to 10.9 ms | 0 to 4 of 280 to 400 |
| Bandit Double Decker 5003 (1) | 12.0 ms | 9 of 156 | 4.9 to 7.3 ms | 0 of 250 |

Idle, the same tick is 0.1 ms. The slices take longer in all (a slice now ends sooner after its budget, so a generation takes more of them). The garbage collector paused 0 to 2 ms in a whole generation. What is left over 4 ms is this box: a bare thread hand-off here takes 13 us at the median and up to 4.6 ms in its tail (2,000 hand-offs, load 3), and a tick has two; the overrun lines say so ("the kit ran 0.3 of 5.0"). Spinning the tick while it waits, rather than sleeping, was tried and made it far worse here (the kit's thread then has one core less to run on), so it waits as before. A machine with free cores should see slices of the budget plus a span: 2.5 to 4 ms.

**Which kit.** A request's seed is made by its own kit ("Shape grammar"): a committed seed as its file, anything else by the grammar where the shape has a recipe. The
"generated" line names it, one line a spawn (`[vehicles] generated Bandit Box Truck 733786 (hash ..., kit 2: body room+bed, cab caged, ...) in 1305 ms over 489 ticks, longest slice 5.4 ms, longest span 3.3 ms (...)`, `VehicleGenJob.Kit` and `Modules`), and the vehicle's blueprint
text carries it in its kit line. A grammar seed that fails as it is built plans again inside the generation (up to four plans), before the rerolls below.

**Seeds that fail.** A seed whose generator throws (some seeds have nowhere to stand a part; the sweep skips them) or whose text doesn't load rerolls up to 3 times, each new seed derived from the last (`VehicleSpawnKit.Reroll`: the same seed always rerolls the same way), then a committed seed of that style and shape stands in (`VehicleSpawnKit.Committed`). A style that ships nothing (Militia) has nothing to fall back to: the player is told. Failures are logged, the first 10 and then every 100th. The full check (`VehicleChecks`: driving, turning, firing for simulated seconds) doesn't run at spawn; the drive check below does, so a seed that builds but stands crooked or doesn't move is made again. `--sweep-vehicle` gives a shape's pass rate; run it before calling a shape fit for dynamic spawn.

**The drive check** (`VehicleDriveCheck.cs`, `Vehicle.DriveGate`). Every vehicle no file holds (a grammar plan, a kit-4 plan, a kit-1 seed that ships nowhere, anything in a spawn context
that matters) is built from its own blueprint text in a world of its own, settled for 60 ticks on a flat floor, measured (at most 8 degrees off level, as the full check), then
given 75 ticks of full throttle through its seat: its centre of mass must go 1 m forward (a working Box Truck goes 2 to 6 m) and it mustn't tip past 25 degrees. A refusal throws
as a refused part does, so the grammar takes its next plan (as the identity gate does: a plan that fails it never counts as built), kit 4 its next plan or derived seed, and a kit-1
seed rerolls in the spawner. A committed seed is never checked (CI checks it), so `--gen-vehicles` is untouched. It yields as the kit does: the build goes a step at a time
(`ConstructBuild.GroundJob` on a world of its own, 0.4 ms at a time, then `Join`), the physics a tick at a time. The physics runs inside a world context of its own (flat, plain
straight-down gravity at the spawn context's strength, no gravity parts, no planetoid), entered round each span and left before each yield, so the game's statics (gravity, its
gravity parts' tables, the ride bodies) are never touched and a seed checks the same in game as on the command line (a sliced job's hash matched a direct run's). The kit line
doesn't change: what it measured is `Vehicle.DriveCheck`, and a refusal is logged (`[vehicles] Bandit Box Truck 2 [recovery, tier 1]: the drive check refused it (a 1.25 s
throttle pulse moved it 0.43 m (at least 1; rpm 3040)); planning again`). Measured 2026-11-07: `--kit-report --roles --seeds 2` over every style, role and tier (384 vehicles, 557
plans checked): 9 plans refused (1.6 %), each a real failure the full check confirms, run with `--no-drive-gate`: an IT Scrap Tanker 2 for a tanker whose wheels spun on the spot
at every tier (0 m in 6 s, "forward takes it backwards"), a Militia War Bus 1 for a boss at every tier (0.7 m a pulse; 14.4 m in the check's 6 s), a Bandit Box Truck 2 for recovery
at tier 1 (0.4 m; 6.8 m in 6 s). Each was planned again into a plan that passes it (the full check on those not run). The old leader of Bandit convoy 3 fails it (10.4 degrees). 8 ms mean a plan over the matrix
(Technicals are quick), 15 to 50 ms for a Box Truck warm, 126 ms worst (cold); in slices, no span of its own showed among a generation's longest.

A towed vehicle (the Tanker Rig's trailer) comes with it: a committed trailer when its blueprint ships, else made by its own generator in the same job. A trailer (`VehicleCheckOptions.Towed`) is never pulsed: it has no engine, so the gate
passes it (`DriveGate(towed: true)`, `VehicleRegistry.Towed`). Before 2026-11-09 it was pulsed and refused ("no engine"), and the rig picked its trailer from the Fuel Trailer
class's own seeds rather than the style's shipped ones, so British Tanker Rig 19 towed an unshipped "British Fuel Trailer 2": its check (`--gen-vehicle`) and its spawn failed
while `--gen-vehicles`, which never builds a tow, reported it identical. That looked like a seed depending on what ran before it; it was not. The rig now picks from the style's
shipped trailer seeds (rig 19 tows trailer 3), and `--gen-vehicles --order reverse` or `--order shuffle:N`, with `--between`, shows each seed's text is the same in any order.

## Behaviour data

Roadmap phase 8 (`VehicleBrief.cs`, `VehicleBriefMake.cs`, `Source/App/Program.VehicleBrief.cs`): every generated vehicle carries a **brief**, what an AI driver or gunner
needs to know about it, worked out once when it is made, so whoever writes the AI never has to analyse a vehicle. It is data only: nothing reads it yet but the debug view.
It changes nothing that is built: every committed seed, kit 3 and kit 4 seed, model and context makes the same text and hash as before (`--gen-vehicles`: all 123 identical;
a kit-4 role seed made in slices gives the same hash as made directly).

**Where it is.** `Construct.Brief` (a `VehicleBrief`), on the server only. It is never sent to clients (nothing on the wire, no protocol change): an AI runs where the
physics does. It saves with the world, at the end of the body's bytes after its tracks (docs/sandbox/WORLD-SAVES.md), 600 to 950 bytes a vehicle; an old save has none, an
older build stops reading before it, and a brief that doesn't read is left off with a log line, never the body. A brief of a later `FormatVersion` is skipped. Format 2 (phase 9) adds `Stowed` at the end; a format 1 brief reads with none.

**When it is made.** In the generation's own sliced job (`VehicleGenJob.MakeBrief`, after the blueprint text and its hash, so it can't change them), from a body built from
that very text: the drive check's ("The drive check"), when the plan kept was checked with the same steps (kit 4 notes its role after the check, so the names differ but the
steps don't: `VehicleBriefMake.SameBuild`), else a drive check run for it, whose verdict isn't a gate. It reads only the body's rest frame, so the pulse the check drove
doesn't matter. Every ray yields, so it adds slices, not long ones; 20 to 110 ms of a generation on the 2-core box under load (a Highland Box Truck 49, an IT War Bus 104; the rays dominate). When the vehicle is down,
the spawner puts the brief on its construct (`VehicleSpawner.Brief`): each part reference takes the built body's tag after its def is checked against the part at its index
(a brief that doesn't fit is left off, logged), with its convoy place and model. The warm-up compiles the path once. `VehicleGenJob.BriefsOn = false` turns it off.

**What has none:** a committed seed that stands in for a failed one, a blueprint from the parts menu, a tow, a player's own build, and anything in an old save.
`VehicleBriefMake.Rebuild(construct, name, style)` makes one from any construct (no role, no drive numbers, no loot list), at once on the calling thread: run it off the
tick, or accept 20 to 110 ms.

**Part references** (`BriefRef`): the part's stable `CPart.Tag` (the server's; saved), its index when last found (a hint), its def, and a point on it in the body's rest frame
(`construct.ToWorld(r.At)` for anything rigid with the body; a turret's points are at rest). Parts break, are shot off and reload in another order, so before reading
indices call `brief.Refresh(construct)`: nothing to do unless the construct was rebuilt since (`RebuildStamp`); then each reference checks its hint and otherwise finds its tag
(a scan of the body's parts). It allocates nothing either way (measured: 0 B; 1000 refreshes with good hints 0.24 ms). Then `VehicleBrief.Has(c, r)` (there and not broken)
and `VehicleBrief.Index(r)` (-1: gone).

**Directions** are the vehicle's own: `Forward` is the drive line's forward (`Construct.DriveFwd`, level), bearing 0 is dead ahead and bearings go clockwise seen from above
(90 is its right; `Way(bearing)` gives the body-frame direction). Gun arcs use 32 bins of 11.25 degrees, weak points 16 of 22.5. Sides are 0 front (-45 to 45), 1 right, 2 back,
3 left (`SideOf`).

| Field | What it holds | Where it comes from |
| --- | --- | --- |
| `Name`, `Style`, `Shape`, `Role`, `Tier`, `Kit`, `Model` | Who it is: the role and tier (kit 4), the family model's key | the kit |
| `Slot`, `Convoy`, `Formation`, `ConvoyIndex`, `Lane`, `Leader` | A convoy member's place (leader, escort, cargo), its convoy's seed, column or wedge, its place behind the leader (0 the leader), its lane (-1, 0, 1), the leader's construct id | the spawner, when it is set down |
| `Mass`, `Length`, `Width`, `Height` | kg; metres along, across and up | the body |
| `TopSpeed` | the cheap estimate (`VehicleRoles.Speed`, as `RoleFit`) | the engine's power, mass, frontal box |
| `Measured`, `PulseMoved`, `PulseSpeed`, `Accel` | the drive check's 1.25 s throttle pulse from rest: metres, the speed it reached, and 2d/t2 | the drive check |
| `Steer`, `MaxSteer`, `TurnRadius`, `Wheelbase`, `Track`, `Wheels`, `Driven` | steered wheels (lock in degrees: a radian at most, the steering pivots' limits) or skid steer (turns on the spot, radius 0); the turning radius is wheelbase over tan(lock) plus half the track | the wheels and steering joints |
| `Clearance`, `ComHeight`, `TipSide`, `TipEnd` | the lowest fixed part over the ground; the centre of mass's height; the static tip angles sideways and end on (the wheels' spread round the centre of mass over its height) | the body at rest |
| `Climb` | degrees: a hint, the lesser of what 60 % of peak power pulls at 3 m/s and the driven wheels' grip (0.8 of their share) | estimate |
| `Fuel`, `Burn`, `Electric`, `Charge`, `Endurance`, `Range` | litres and litres a second at full throttle (the engines' `BurnRate`), or an electric drive's kJ; minutes at full throttle, and km at the estimated top speed | the parts |
| `Guns[]` | each gun: its part (`At` the muzzle), `Mount` (turret, hinge or fixed), `Class` (light, medium, heavy: flame, harpoons, shells and big blasts heavy, full-auto medium), `Band` and `Range` (full-damage range: close under 25 m, short under 60, medium under 150), `MaxRange`, `Dps`; its turntable's range (`FullCircle` or `YawMin`/`YawMax` from rest) and the hinge's `ElevMin`/`ElevMax`; `Clear` (the bins it can turn to and fire clear of its own vehicle at some elevation) and `Level` (clear at the elevation nearest level, for a target on the ground); `Pivot`; `Seat` (index into `Seats`) or `Input` (a logic part wired into it) | the guns (`CGun`), the aim chain's joints and their limits, and rays from the muzzle swept round the turntable at five elevations through the vehicle's own drawn parts (what turns with the gun doesn't count) |
| `Engage` | the range to fight at: the guns' full-damage ranges, weighed by damage (a median) | the guns |
| `BestBin`, `BestSide`, `BestDps` | the bearing where the most gun damage reaches clear (a tie goes to a broadside), its side and how much | the guns' `Clear` |
| `Weak[]` | each weak point: its part, its kind (the style's rule label, "fuel drum", "open driver", "antenna", "pipe box"..., else fuel, tyres, ammo, engine), `Bearings` (a standing player 20 m off hits it first from there) and `Top` (seen from straight above) | the style's identity rules (`VehicleIdentity.SignatureOf(style).WeakPoints`) plus the plain ones, the identity check's own rays |
| `KeepAway` | the side to keep from the enemy: where its weak points show most, fuel and ammo weighed 3, the engine and driver 2, the rest 1 | the weak points |
| `Seats[]` | each seat: what it controls (drive, guns: a bit per gun, winch, signals: horns, lamps, sirens, a radio, beacons; other wired parts), and `Open`: its sitter can be shot (seen from above, or from four bearings) | the drive seat, the guns' seats, the wires one module on |
| `Entries[]` | doors, ladders and hatches, with where they are | the parts |
| `Loot[]`, `Cargo` | the containers that rolled loot, their table and its tier ("bandit 3" is 3, a style's own 2); loose cargo pieces | the blueprint's loot list |
| `Stowed[]` | its own cargo ("Cargo and spares"): each stowed stack (`Container` and `Slot`) or loose piece (`At`, rest frame; the body is carried by this vehicle), its kind, part and count, `CooksOff` (a loose box of rounds) and `Guns` (bit k: gun k can use it: a magazine it takes, rounds, a flame gun's fuel) | the blueprint's `"stored"` and `"cargo"` lists |
| `Hitches[]`, `Winches[]`, `FuelOpen` | tow points (`At` the coupling point) and their kind; winches; how many fuel tanks can be hit | the parts and the weak points |

What it doesn't know: anything that changes while it lives (damage, ammo, fuel left, who sits where: read the construct), a gun's arc against other vehicles or the ground,
and the climb and range are hints, not drives. A weak point's exposure and a gun's clear bins are judged on the vehicle as made: a plate shot off later can expose more.

**Seeing it.** `--brief` prints a vehicle's (made as the spawn makes it, or any blueprint file); its last line gives the cost and the save bytes' round trip. In a dev build,
on the host, with the look-at inspector open (F6), the faction vehicle under the crosshair shows its brief: each gun's clear bins as spokes from its pivot (green clear at
level, amber only lifted), the engagement range as a ring on the ground with the best bearing drawn across it, the weak points (red hittable, grey covered), a "keep away"
arrow, and the brief as text down the left. A client sees nothing (the brief is the server's). The "generated" log line ends with the brief's summary ("brief: 2 guns
engage 31 m, best right; 10 weak points, keep the back away; 4 seats; top 23 m/s, wheels steer").

**Checked** (2026-11-07, by hand: an offline host on Construct Yard through the real spawner, the box at load 9 to 10): a Bandit raider (a Gun Wagon) and IT convoy 11 (a
command War Bus, a raider Box Truck, a tanker Scrap Tanker) each got a brief that fitted its body, the convoy's with its slots, place and leader; a gun's part marked broken
read as gone; taking part 0 off moved every reference down by one and they followed their tags (0 B); the world saved and loaded with 4 briefs and the same text. No brief
span was among a generation's long ones.
