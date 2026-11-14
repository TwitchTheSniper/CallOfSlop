# The Engine Kit (Source/EngineKit)

A request for an engine (power, torque, fit, mass, layout, tier, seed) and a built modular engine back: the modular engine's
own pieces (docs/sandbox/CONSTRUCTS.md "Modular engines") put together through the game's build rules (the vehicle kit's
`BlueprintScript`), with the numbers the game itself works out for it. It is in C#: the first native generator, which the
Python part generators are meant to follow.

## Why cars are slow

`--speed-test FILE... [--seconds N]` drives blueprints flat out on the drive check's flat floor (default 40 s) and prints what
they are and what tops them out (`Source/EngineKit/EngineKitSpeed.cs`). Measured 2026-11-11 on the committed blueprints:

| Vehicle | Mass kg | Engine | Peak kW | Gears x final | Wheel r m | Top m/s | 0-10 s | 0-20 s | Gear at top | rpm at top | Gearing's top m/s | Drag kW at top |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BanditCar1 | 1653 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 24.6 | 2.0 | 6.5 | 3/4 | 5512 | 39.2 | 147 |
| ITCar1 | 1451 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 25.4 | 2.0 | 6.6 | 3/4 | 5707 | 39.2 | 143 |
| HighlandCar1 | 1500 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 24.4 | 1.3 | 4.5 | 3/4 | 5473 | 39.2 | 131 |
| BritishTechnical2 | 1439 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 26.1 | 1.4 | 4.4 | 4/4 | 4087 | 39.2 | 153 |
| ScamTechnical1 | 1595 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 24.4 | 1.5 | 4.8 | 3/4 | 5478 | 39.2 | 139 |
| BanditBoxTruck1 | 2475 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 19.3 | 1.6 | - | 3/4 | 4334 | 39.2 | 107 |
| ITBoxTruck1 | 2492 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.1 | 0.55 | 20.8 | 2.1 | 8.9 | 2/4 | 4730 | 53.7 | 134 |
| BanditWarBus1 | 3448 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 17.5 | 2.5 | - | 2/4 | 5468 | 39.2 | 111 |
| ITWarBus1 | 4401 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 15.2 | 3.4 | - | 2/4 | 4729 | 39.2 | 92 |
| ITBuggy1 | 1272 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.1 | 0.55 | 25.7 | 1.1 | 3.1 | 3/4 | 4190 | 53.7 | 129 |

What limits them: **power against the air drag, nothing else.** No vehicle reaches its limiter (gearing would allow 39 to 54 m/s
in top gear), and no speed cap is near (`ConstructPhysics.MaxSpeed` is 60 m/s). Every shipped car, technical, truck and bus has
the same 218 kW V8 (the stock 114 kW Engine is in the styles' lists too) and the same Automatic Gearbox. The arcade air drag
(`ConstructPhysics.AirDrag`, 0.006 v^2 m/s^2, the same for every body under 20 t: `AirDragOf`) takes 0.006 v^3 W a kilogram, so a
top speed goes with the cube root of power a kilogram: twice the speed needs eight times the power. Of the V8's 218 kW, 130 to
150 kW go into drag at the top speed of a car, the rest into rolling resistance and the drive line's losses; heavier vehicles
also sit in 2nd or 3rd near the limiter, where the petrol curve has fallen.

What a top speed costs (drag only, add about half again for rolling and losses):

| Target | 1.5 t car | 2.5 t truck | 4.5 t bus |
| --- | --- | --- | --- |
| 25 m/s | 140 kW | 230 kW | 420 kW |
| 30 m/s | 245 kW | 405 kW | 730 kW |
| 35 m/s | 385 kW | 645 kW | 1160 kW |

The user (2026-11-11) is happy for today's small, low-tech vehicles to stay on the slow side: **no shipped vehicle was retuned.**
The faster, more powerful vehicles come with higher tech tiers and bigger vehicles, through the Engine Kit below. Suggested
targets for the tiers: tier 1-2 as now (cars 24-26 m/s, trucks 19-21, buses 15-18); tier 3 cars 30, trucks 24, buses 20;
tier 4-5 cars 35+, trucks 28, buses 24, which is 2.5 to 5 times today's power a kilogram.

## The request

`EngineRequest` (`Source/EngineKit/EngineKit.cs`); zero means "any".

| Field | Meaning |
| --- | --- |
| `PowerKw`, `TorqueNm` | the peak power wanted, or the crank's torque at the size's peak rpm, or both |
| `PeakRpm` | picks the size: Small peaks at 5000 rpm, Medium 2800, Large 1400, Capital 700 |
| `Fit` | the box it must go in: length along the crank, width, height (m) |
| `MaxMass` | kg |
| `Tier` | 1 to 5: which pieces it may use (below) |
| `Layout` | Auto, Inline (one bank up), V (up and right: the sockets are square, so 90 degrees), Flat (left and right), W (three banks), Radial (four, one under the crank) |
| `Boost` | Auto, None, Supercharger, Turbo |
| `Size` | -1 any, or Small to Capital |
| `Fuel` | modular engines burn fuel: anything else is built as petrol, with a note |
| `Exhausts`, `Paint`, `Name`, `Seed` | an outlet on every head without a fuel system (looks); a colour for the crank and flywheel (a faction's); the blueprint's name; the seed |

`EngineKit.Request(r)` returns an `EngineResult`: the blueprint text, the numbers the game's own `ConstructEngine` works out for what
was built (kW, torque at the peak, idle, peak and limiter rpm, fuel burn), mass, size as built (every drawn box), layout ("V8"),
pieces, and a note saying why it falls short when it does (short of power within the limits, nothing fits the box: the nearest,
a booster past the tier, another fuel). Deterministic for a request and seed.

## The rules

1. **Plan.** Every size, layout, crank count (1 to 8), cylinder, fuel system and booster the request and tier allow is summed the way
   `ConstructEngine` sums it (a few hundred to a few thousand plans, 0.1 ms warm): power, mass from the parts' own files, and the
   size from the pieces' shapes.
2. **Score.** Lower is better: the power's miss (a shortfall counts three times an excess), the torque's and the rpm's when asked,
   mass (0.15 per 2.5 kg a kW), bulk (lightly), a little for using pieces below the tier, and a seeded hair. Plans over the box or the
   mass go after every one within them.
3. **Seed.** The seed picks among the plans within `SeedBand` (0.04) of the best: a seed varies the layout and pieces, not the power.
4. **Build.** The chosen plan is built through `BlueprintScript` (the vehicle kit's: every piece placed by the game's own build rules,
   `ConstructBuild.Place`): the cranks end to end, a cylinder on each bank's side of each crank, the fuel system on each bank's first
   head and the booster on it, an exhaust outlet on the other heads, the flywheel on the front crank's end, and from Large up a starter
   on a free crank side (else on the flywheel). A plan a piece is refused for (or where the game counts other banks) gives way to the
   next, up to `MaxBuildTries`.
5. **Read back.** The numbers come from the live construct's `ModEngine`, not the plan: what the game will run.

A built engine needs a fuel tank (touching it or piped to its "Fuel in") and a gearbox on the flywheel's `+z`; the result's fault
leaves out "no fuel tank".

### Tiers

| Tier | Cylinder | Fuel | Booster |
| --- | --- | --- | --- |
| 1 | Cylinder (x1) | Carburettor (x1) | - |
| 2 | | Injector Rail (x1.2, x0.8 fuel) | - |
| 3 | bored to about x1.3 (a thinner wall) | | Supercharger (x1.25) |
| 4 | bored to about x1.6 | | Turbo (x1.4, 1.5 s lag) |
| 5 | bored to about x2 | | |

A tier may use every piece below it. **Bores** (bored cylinders, 2026-11-14; docs/sandbox/design/bored-cylinders-notes.md): the
Bored, Performance and Racing Cylinders are retired (design answer 8), so a tier 3 to 5 bore is the plain cylinder's preset
pressed with a thinner wall, the same across and length: a wider bore, more power, less metal (`EngineKit.BoredAt`: the thickest
whole-cm wall that gives the grade's `Power`, else the thinnest that adds any; the plan is named by the grade it reaches). A Small
one is `pipe8`, `pipe7` or `pipe5 45x45x75` (x1.38, x1.59, x2.07; 23.7, 21.3 and 16.0 kg against the plain 28). A Medium preset's
wall is already thin (its bore 0.78 of its across, the Small's 0.56, and the bore counts up to 0.9), so a Medium or Large bore
reaches about x1.37 at most and is a "Bored" one. Its fuel system and booster are pressed wider to the cylinder's scale, rounded
up, at their own length (so the bank is never starved and stands no taller); the crank sections are pressed longer only when one of
those is wider than the section (`solid 45x45x65` under a Small racing bank's 60 cm injector: the same across, so the same revs).
Cylinders keep their across, so a V, W or radial of bores builds. A bored plan's score pays a little for the health it gives up
(`0.1 x` the share of the plain wall's metal removed), so the kit leans on it where it needs the power or the mass.

**Strokes** (stroke power, 2026-11-14; docs/sandbox/design/stroke-power-notes.md): a cylinder's kW is its bore's times its stroke
ratio (length over across against the preset's, 0.6 to 1.6) and the revs are the crank's over it, so a long stroke gives more power
at lower revs and torque by the square. Each bore is planned at strokes 1, 1.33 and 1.6 (`EngineKit.Strokes`: the preset's length
times that, to 5 cm), and 0.8 only when the request asks for revs above the size's peak (a short stroke spins faster for less
power). The fuel system is pressed wide enough to feed the cylinder's kW whole, by bore and stroke, and the booster to its kW at its
bore alone, which is what the engine scales a booster's share by (`FeedFor`: in 5 cm steps until they do, so no bank is built starved; the kit reads a starved bank back as the game's fault text). A plan's torque, peak rpm and starter
are its own stroke's; a long stroke's cylinders stand taller in the estimate. `MinLimiterRpm` holds a request to strokes that keep
a limiter: a vehicle's request sets the Small limiter, since its stock gearbox is geared for it (a 1.6 stroke's 4375 rpm would cap a
car's top gear at 26 m/s), so vehicles keep the plain stroke. A long stroke wins where torque or mass is asked. Samples
(`--gen-engine`, seed 0, 2026-11-14, with strokes):

| Request | Built |
| --- | --- |
| 300 kW, tier 1 | Small R8 (two cranks, four banks) 320 kW, 391 kg, 1.40 x 2.49 x 2.49 m |
| 300 kW, tier 5 | Small B4 performance bore, injected, 306 kW, 222 kg |
| 2000 kW, tier 4 | Small V14 performance bore, long stroke (1.33), turbo, 1992 kW, 5059 N m at 3759 rpm, 896 kg (before strokes: W21, 988 kg) |
| 2000 kW, tier 5 | Small R20 racing bore, injected, 1986 kW, 748 kg (before strokes: W21, 988 kg) |
| 900 kW, tier 5, W | Small W9 performance bore, long stroke (1.33), injected, 915 kW, 2323 N m at 3759 rpm, 496 kg |
| 3000 N m, tier 5 | Small I5 racing bore, long stroke (1.6), supercharged, 993 kW, 3035 N m at 3125 rpm, 492 kg (before strokes: B14, 1622 kW, 729 kg) |
| 300 kW peak 3000 rpm, tier 2 | Small V4 long stroke (1.6), injected, 307 kW, 939 N m at 3125 rpm, 323 kg |
| 300 kW peak 8000 rpm, tier 2 | Small R8 short stroke (0.8), injected, 307 kW, 469 N m at 6250 rpm, 338 kg |
| 20000 kW, tier 2 | Medium W21 20.2 MW, 20.6 t, 15.6 x 10 x 6.8 m |
| 20000 kW, tier 5, Medium | Medium V12 bored, injected, turbo, 20.4 MW, 11.4 t |
| 400 MW, tier 5, Large | Large B10 bored, long stroke (1.33), turbo, 407 MW at 1053 rpm, 217 t (before strokes: B12, 232 t) |
| 50000 N m, tier 1, Medium | Medium I7 long stroke (1.6), 8.96 MW, 48892 N m at 1750 rpm, 13 t |
| 500 kW under 300 kg, tier 5 | Small W6 racing bore, 497 kW, 277 kg |
| 500 kW under 300 kg, tier 1 | Small R4 long stroke (1.6) 256 kW, 292 kg, with "256 kW is short of 500 kW: the most within the limits at this tier" (before strokes: V4 160 kW) |
| 120 kW inline in 2.5 x 0.8 x 1.6 m | Small I3 120 kW at 1.70 m high, with "nothing ... meets the limits; nearest" (an inline's bank and the flywheel under the crank need 1.7 m) |

## The parts

The kit uses the modular engine's 40 pieces and their rules (`Constructs/ModularEngine.json`), catalogue or pressed (a pressed key
is a part name a blueprint step may give: `Pressed Part (cylinder.cylinder pipe15 60x60x105 steel)`). The retired tier 3 to 5
cylinders in each size, `Constructs/Parts/EngineKit/{Size}{Bored,Performance,Racing}Cylinder.json`, are still written by C#:
`EngineKitParts.cs`, `--gen-engine-parts [--check]` (from the repo root; then build, or copy the folder next to the DLL), so a save,
an old blueprint or a shipped vehicle holding one keeps working. Each is the plain cylinder's file with the same boxes and sockets, a
head of the tier's colour, `"tier"`, `"retired": true`, more mass and health, and `enginePiece.power` and `thirst`
(`ConstructEngine.Build` multiplies its kW and burn by them).

**Survival.** The plain pieces come off a Press (the Cylinder and Engine dies; their presets are the files): only the Capital Cylinder
and Capital Smokestack, past what a die presses, have an Assembler recipe. The retired cylinders have none.

## Callers

- **Command line**: `--gen-engine [--preset small|medium|large|huge] [--power KW] [--torque NM] [--rpm R] [--fit LxWxH] [--mass KG]
  [--fuel F] [--layout inline|v|flat|w|radial] [--boost none|supercharger|turbo] [--size small|medium|large|capital] [--tier 1-5]
  [--seed S] [--plans N] [--again] [--out FILE]`. Prints (to `callofslop.log`, as the other tools do) the request, the built engine's
  numbers and the note; `--plans N` lists the best plans, `--again` times a warm second request and checks it builds the same
  blueprint. `--gen-engine --read FILE` builds a blueprint on a flat floor and prints each engine's numbers as the game works them out (a saved or old engine's, before and after a change). The presets are 120, 300, 800 and 3000 kW. A first request in a process costs about 2 s of warm-up (JIT); a warm one 15
  to 50 ms (about 7,400 plans in 18 to 27 ms, three times main's 2,640 since strokes; it yields every 1,024 plans and before each bore and stroke's pieces are pressed; `--slice-vehicle engine [--power KW] [--tier T] [--size 0-3]` times one in 2.5 ms slices: a first Medium or Large tier 5 request in a process runs 9 to 29 ms of kit over 4 to 6 slices, its longest span 2.6 ms; the one long slice measured, 16 ms, was a garbage collection with 1.9 ms of kit in it).
- **The vehicle kit / dynamic spawn**: an `EngineWish` (below, "In a vehicle"): `--gen-vehicle NAME --engine-tier N [--top-speed V]
  [--accel A]`, `--slice-vehicle NAME --engine-tier N [--top-speed V]` (as the game's spawn makes it, in slices), and
  `new VehicleGenJob(..., engine: wish)` in the game. Nothing in the game passes one yet.
- **The player** (creative): the inventory's **Engine** tab (`UI/UiSandboxEngine.cs`): power (presets 120 / 300 / 800 / 3000 kW), tech
  tier, layout, booster, size, an optional bay (length, width, height; "Car bay" is `EngineKitVehicle.Bay`) and a seed; MAKE ENGINE sends
  `CActKind.MakeEngine` (protocol 242; fixed fields: u16 kW, u8 tier, layout, boost, size, 3 x u16 bay in cm, u32 seed). The host
  (`Game/GameSandboxEngines.cs`; with the Car bay it holds the request to the Small limiter, as a vehicle's, since a car's stock gearbox is geared for it) refuses with a reason anything out of range (1 to 50,000 kW, tier 1-5, a bay side 0 or 0.3-100 m),
  outside creative or in a seat, and queues the request on the vehicle spawner (`VehicleSpawner.RequestEngine`): the kit runs in the
  generation's slices (`VehicleGenJob(EngineRequest)`, 1-8 ms over 1-3 ticks warm) and the engine is set down in front of the player
  as their construct, never rerolled. The player's notice gives its name, kW, torque at its peak rpm and its limiter, mass and size, "it needs a fuel tank and a gearbox", and
  the kit's note. Clients never run the kit: the engine arrives as bodies. Tried 2026-11-11 with a host and a client over the real
  transport (80 ms, 2% loss): a 120 kW tier 1 inline in the car bay, 300 kW tier 3, 2000 kW tier 5 V turbo and 20 MW tier 2 (a Medium
  W21, set down in slices), each the same parts and hash on both ends, the engine complete but for its tank; the first big set-down
  in a process joined in 56-120 ms (compiling), the next in 0.4 ms.

## In a vehicle

`EngineWish` (`Source/EngineKit/EngineKitVehicle.cs`): a tech tier, a top speed on the flat (0: the tier's, 25 / 27 / 30 / 33 / 36 m/s
for tiers 1 to 5), an acceleration (m/s^2 at half the top speed; 0: none asked) and the vehicle's mass without its engine (0: measured).

**How a generator opts in.** It doesn't change: a wish is made current around a generation (`using (EngineWish.Use(wish))`, as
`VehicleGenJob` does with its `engine` argument and `--gen-vehicle` with `--engine-tier`), and `Vehicle.Powertrain` reads it. Any shape
that calls `Powertrain` with no engine named gets a kit engine; aircraft are left alone. With no wish (every shipped seed, every spawn
today) nothing changes: `--gen-vehicles` says all 238 identical.

What happens (`VehicleRegistry.WithKitEngine`, `Vehicle.KitEngine.cs`):

1. With no dry mass in the wish, the vehicle is made once with its stock engine and weighed; its engine's mass comes off.
2. `EngineKitVehicle.Plan`: power = the larger of 1.5 x 0.006 v^3 W a kg (drag plus rolling and losses) and m a (v/2) / 0.85, for the
   dry mass plus the engine's (two rounds: the engine's mass changes the power). Small pieces, no bank pointing down, in
   `EngineKitVehicle.Bay` (3.0 m of crank ahead of the flywheel, 1.9 m wide, 1.75 m high); short of power, the note says so.
3. The flywheel stands on the engine sill by its `-y` where the stock engine would (centre at the same height), the gearbox on its `+z`,
   the cranks run forward, the fuel pipe goes to its "Fuel in" and the Driving Module's wire to it. No hood: the cylinders stand open.
   A refused piece fails the generation (the spawner rerolls; the command line prints why).
4. Named with the wish (`Bandit War Bus 1 [engine tier 4, 33 m/s]`, never a committed file), a kit line note, and the spawn's drive check
   (`DriveGate`). The first request's warm-up is in `VehicleGenJob.Warm` (11-21 ms after the Technical, measured on a loaded box).

### Measured (2026-11-11, `--speed-test`, 40 s on the drive check's hard floor)

| Vehicle | Engine | kW (x V8) | Mass kg | Top m/s | 0-10 s | 0-20 s | Limit |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Bandit Technical 1 (stock) | V8 | 218 | 1598 | 23.9 | 1.4 | 4.9 | power against drag |
| tier 3, 30 m/s | V8 bored | 416 (1.9x) | 1713 | 26.1 | 1.9 | 6.3 | grip |
| tier 4, 33 m/s | V8 performance, injected | 614 (2.8x) | 1731 | 25.9 | 2.0 | 6.4 | grip |
| tier 5, 36 m/s | V8 racing, injected | 768 (3.5x) | 1754 | 25.5 | 2.0 | 6.7 | grip |
| tier 5, 45 m/s | | 960 (4.4x) | 1876 | 22.6 | 2.6 | 9.6 | grip |
| Bandit War Bus 1 (stock) | V8 | 218 | 3448 | 17.5 | 2.5 | - | power against drag |
| tier 3, 22 m/s | V6 bored, injected | 374 (1.7x) | 3021 | 21.9 | 1.9 | 5.6 | power against drag: as predicted |
| tier 3, 30 m/s | V10 bored | 624 (2.9x) | 3244 | 26.8 | 1.8 | 5.2 | grip |
| tier 4 / 5 | V10 | 768 / 960 (3.5x / 4.4x) | 3272 / 3300 | 26.8 | 1.8 | 5.2 | grip |

Every one built, passed the drive check, started, and burnt fuel (960 kW: 0.35 l/s, so the bus's 60 l lasts three minutes flat out).
There is no cooling to test: modular engines don't overheat (CONSTRUCTS.md "Modular engines", decision 5).

**What a bigger engine needs downstream: grip, not gearboxes.** Up to about 1.7x the V8 the kit's prediction holds. Past it the
two driven tyres are the limit: on Off-Road Wheels (grip 0.85) a two-wheel drive tops out at 26 to 27 m/s whatever the engine, and
more power only spins them (0-10 m/s gets slower); the bay's long, front-heavy engine also takes weight off the driven rear axle (the
45 m/s Technical is slower than the 30). `--speed-test` now names it: "grip (the engine asks 37.6 kN of driven tyres that hold
21.7 kN)". No gearbox or differential part has a torque limit (`maxTorque` is unset on all of them), and the Automatic Gearbox's
gearing allows 39-42 m/s, so nothing there stops a kit engine. For the tier 4-5 speeds a fast vehicle needs all its wheels driven
(a front differential through steering pivots, or a driven tandem) or a grippier road tyre (Slick Wheel 1.4, but no off-road grip),
and a bigger tank. That is the next step for the fast shapes; no shipped part was changed.

### Fuel (2026-11-11, `Vehicle.KitTanks`)

A kit engine burns much more than a V8: 960 kW is 0.35-0.41 l/s, so a stock 60 l drum lasts under three minutes. After a kit-engined vehicle is
made, the kit adds tanks until the construct holds `EngineWish.EnduranceMinutes` at full power. That is 15 minutes, proposed
(`DefaultEnduranceMinutes`); `--endurance MIN` changes it. Large Fuel Tanks (150 l) go on first; when 40 l or less is still missing, a Fuel Tank
(40 l) goes on instead. There are at most 7 tanks. Every shape uses the same ordered places:

1. Saddle tanks on a rail's outer face at axle height, between the axles. The middle of the wheelbase goes first, then left before right (the laid
   branch shafts run on the right).
2. The tail rails behind the last axle, under the bed or the boot.

Each tank stands a wheel's radius plus 10 cm clear of every axle, below the floor, so it can't block a door or the driver's view. A place the build
rules refuse (a hull skirt, a sill's wing, a step) is passed over. The construct's fuel is one pool, so only the shape's own tank is piped. The kit
note says what was added. At tier 5 (36 m/s, all-wheel drive):

| Vehicle | Burn l/s | Fuel l | Added | Full power |
| --- | --- | --- | --- | --- |
| Bandit Technical 1 | 0.324 | 360 | 2 behind | 18.5 min |
| Bandit War Bus 1 | 0.406 | 400 | 3 behind (hull skirts between the axles) | 16.4 min |
| Highland Giant Truck 8 | 0.406 | 400 | 2 between, 1 behind | 16.4 min |

## Next steps

1. **Fast shapes.** All-wheel drive: the kit asks for it by itself over 200 kW a tonne (proposed; tiers 3-5 with no acceleration asked),
   keeping two-wheel drive where the layout refuses; `--awd` forces it, `--2wd` stops it (drivetrain-and-engines.md "All-wheel drive and
   round wheels": 26.7-34.2 m/s at tier 5 against 23-26.8). Done: the bigger tank ("Fuel" above). A tier 3+ shape (or the wish) drives every axle and carries a bigger tank (above: grip is the limit past 1.7x the
   V8); a game caller passes a wish (a faction's tier, a role's) to `VehicleGenJob`.
2. **Player action, more.** Done for creative (above). Next: an option to come with a fuel tank and gearbox fitted, so it runs at
   once; survival gets nothing yet (its engines come from Assembled pieces).
3. **Bigger engines for mediums and giants.** Medium pieces are 4x linear, so a Medium engine is 3 to 16 m long and 6 to 10 m wide:
   right for giants, too big for a 6 to 10 m truck. A tier 4-5 Small engine (up to 4.3 MW as a radial of 8 cranks) covers the gap;
   if mediums need more in less room, add a "Small x2" size (scale 2, about 300 kW a cylinder) to the rules and the kit.
4. **Gearing for fast vehicles.** The Automatic Gearbox's top gear (1.7) with a 4.1-4.6 final drive allows 39-54 m/s; enough for
   35 m/s cars. Its upshift waits for 90% of the limiter, past the petrol peak: a kit car could set its own.
5. **Faction flavour.** Only `Paint` today; a style could prefer layouts (a Bandit's exposed V, a Highland inline) by scoring.
6. **engine_parts.py is retired.** The part kit's `--gen-parts engine_parts` (`Source/PartKit/Generators/EngineParts.cs`) writes the 40 plain
   pieces and `ModularEngine.json`, byte for byte as the Python did. The tier cylinders read the plain cylinder's file, so they follow it.
