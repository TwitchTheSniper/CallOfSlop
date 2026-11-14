# Stroke power: notes (2026-11-14)

Branch `claude/stroke-power-136aba`. The user's answer of 2026-11-14 (`sized-gun-engine-parts.md`, the last block of section 14):
"Stroke adds power too": power follows displacement, bore and stroke, and a longer stroke also lowers revs. This replaces answer 5's
"stroke trades revs for torque at the same power". The bore trade (bored-cylinders-notes.md) stays as built. **Protocol 313** (no new
bytes, no part ids move: a long- or short-stroke engine's kW, torque and starter differ on each peer).

## 1. The rule

- **A cylinder's kW = its bore's kW x its stroke ratio** (`EngineRules.CylinderKw`): `At(boreScale).Kw x r`. The bore's kW is main's
  (`Pressed.BoreScale`, `PlainBore`, `BoreOf`: about bore squared, `scale^2.161`), so power goes with bore squared times stroke, the
  displacement. `r` is what it was: the blank's length over its across against its nearest preset's (`Pressed.EngineNumbers`), three
  figures, 0.6 to 1.6; 1 on every file cylinder and every key of a preset's proportions, so **a cylinder at its plain bore and plain
  stroke has exactly main's power**.
- **Revs** are unchanged: the crank's scale's over the engine's stroke (its cylinders' `r` weighted by kW, which now includes `r`).
- **Torque** at the peak is power over peak rpm, so it goes with `r` squared: a 1.6 stroke gives 1.6 times the power at 0.625 of the
  revs, 2.56 times the torque; a 0.6 stroke 0.6 times the power at 1.67 times the revs, 0.36 times the torque.
- **What bounds the stroke**: the crank's throw, the die's 0.6 to 1.6 (`Pressed.MinStroke`, `MaxStroke`). A blank longer than 1.6 is
  more metal, mass and health but no more stroke, so no more power. The block's length along the crank is not involved (a cylinder's
  length stands out from the crank), but a long stroke stands taller: the kit's estimate adds it.
- **What a long stroke costs besides revs**: mass (`Pressed.PieceMass`: by metal, so 1.6 times; it was already so on main), fuel at
  full power (burn is by kW, so 1.6 times the litres a second, the same per kWh), height, and a **fuel system and booster sized to its
  power**: a bank's feed is the fuel system's kW over its largest cylinder's kW by bore and stroke (design 6.4, "starve by power"), so
  a long-stroke bank on its old carburettor is starved by the same share it gained. The model has no heat.
- **Starter**: an engine needs one when its crank's scale times the cube root of its stroke reaches `starterFrom` (8)
  (`EngineRules.StarterAt(scale, stroke)`, `ConstructEngine.NeedsStarter`): a long stroke is more to turn over. At the four classes and
  any stroke in 0.6 to 1.6 the answer is the class's (Medium x 1.17 is 4.7; Large x 0.84 is 13.5), so only a crank of about 6.8 to 8
  with long strokes (or 8 to 9.5 with short ones) changes.
- "Cylinders dictated by size" stands: nothing here changes bore, wall or the presets.

## 2. Readers

| Reader | What it does now |
| --- | --- |
| `ConstructEngine.Build` (the built engine) | each cylinder `CylinderKw x PiecePower x share x fuel x boost`; a bank's kW for the feed and boost ratios is its largest `CylinderKw`; burn from that kW |
| `ModEngine.Rpm`, `Apply` | revs over the stroke as before; `EngineTorque = kW / peak`, so by `r` squared |
| `NeedsStarter`, `Apply`'s `NeedsStarter` | `StarterAt(Scale, Stroke)` |
| Readout, look line, starved text | read the engine's kW and feed: unchanged code, new numbers |
| `--press-part` | a cylinder's kW and torque by bore and stroke |
| `--speed-test`, window-shot `engines:` | read `EngineDef` and `ModEngine`: unchanged code |
| Engine Kit `Plans` | plans each bore at strokes 1, 1.33, 1.6 (`EngineKit.Strokes`; 0.8 only when `PeakRpm` above the size's peak is asked); kW by `CylinderKw`; peak rpm, torque and starter by the plan's stroke; the cylinder's extra length in the estimate; `MinLimiterRpm` drops strokes whose limiter is too low |
| Engine Kit fuel system and booster | `FeedFor`: pressed from `ScaleForKw(cylinder kW)` up in 5 cm steps until it feeds the bank whole (a scale's three figures left the first one 99.5% short) |
| Engine Kit `Build` | a starved bank in the built engine is read back as `Fault` (none in the samples) |
| `EngineKitVehicle.Plan` | `MinLimiterRpm` = the Small limiter (7000): the stock gearbox is geared for it, so a vehicle keeps the plain stroke |

## 3. Numbers, main against this branch (`--press-part cylinder.cylinder KEY`)

Mass and health are the same on both (the blank's metal). Revs on a crank of the cylinder's own across.

| Cylinder | Stroke | Mass, health | Main: kW, torque | Branch: kW, torque | Idle, peak, limiter rpm (both) |
| --- | --- | --- | --- | --- | --- |
| Small `pipe10 45x45x45` | 0.6 short | 16.8 kg, 247 hp | 40 kW, 46 N m | 24 kW, 28 N m | 1500, 8333, 11667 |
| Small `pipe10 45x45x75` | 1 plain | 28 kg, 412 hp | 40 kW, 76 N m | 40 kW, 76 N m | 900, 5000, 7000 |
| Small `pipe10 45x45x120` | 1.6 long | 44.8 kg, 660 hp | 40 kW, 122 N m | 64 kW, 196 N m | 563, 3125, 4375 |
| Small `pipe7 45x45x120` (bored 1.24) | 1.6 long | 34.1 kg, 501 hp | 63.7 kW, 195 N m | 101.9 kW, 311 N m | 563, 3125, 4375 |
| Medium `pipe20 180x180x180` | 0.6 short | 336 kg, 9048 hp | 800 kW, 1637 N m | 480 kW, 982 N m | 1000, 4667, 6667 |
| Medium `pipe20 180x180x300` | 1 plain | 560 kg, 15080 hp | 800 kW, 2728 N m | 800 kW, 2728 N m | 600, 2800, 4000 |
| Medium `pipe20 180x180x480` | 1.6 long | 896 kg, 24127 hp | 800 kW, 4365 N m | 1280 kW, 6985 N m | 375, 1750, 2500 |
| Large `pipe80 720x720x720` | 0.6 short | 6720 kg, 579058 hp | 16000 kW, 65481 N m | 9600 kW, 39289 N m | 500, 2333, 3333 |
| Large `pipe80 720x720x1200` | 1 plain | 11200 kg, 965097 hp | 16000 kW, 109135 N m | 16000 kW, 109135 N m | 300, 1400, 2000 |
| Large `pipe80 720x720x1920` | 1.6 long | 17920 kg, 1544156 hp | 16000 kW, 174616 N m | 25600 kW, 279385 N m | 188, 875, 1250 |

The built engine agrees: a kit Bandit Car's Small B6 (six catalogue cylinders, two Small Carburettors), its cylinders swapped for
`pipe10 45x45x120` (`--speed-test`, main / branch): 240 / 240 kW, 733 / 733 N m (the carburettors now feed 62.5%: starved by power);
with carburettors pressed to `hollow 55x55x25`: 240 / **384 kW, 1173 N m** (6 x 64 kW; 458 N m x 1.6^2), 3693 rpm at the limiter in
top gear on both, 22.3 m/s (grip).

`--gen-engine`, seed 0 (main, then branch):

| Request | Main | Branch |
| --- | --- | --- |
| 300 kW, tier 1 | R8 plain, 320 kW, 391 kg | the same |
| 300 kW, tier 5 | V4 performance, 306 kW, 222 kg | B4 performance, the same numbers (the seed's pick among more near plans) |
| 2000 kW, tier 5 | W21 performance supercharged, 2006 kW, 988 kg | R20 racing, 1986 kW, 748 kg |
| 2000 kW, tier 4 | W21, 988 kg | V14 performance long stroke 1.33, turbo, 1992 kW, 5059 N m at 3759 rpm, 896 kg |
| 900 kW, tier 5, W | W9 racing supercharged, 931 kW, 486 kg | W9 performance long 1.33, 915 kW, 2323 N m at 3759 rpm, 496 kg |
| 3000 N m, tier 5 | B14 racing turbo, 1622 kW, 3098 N m at 5000 rpm, 729 kg | I5 racing long 1.6 supercharged, 993 kW, 3035 N m at 3125 rpm, 492 kg |
| 300 kW and 1500 N m, tier 5 | B6 racing turbo, 695 kW, 1328 N m, 365 kg | I4 performance long 1.6, 489 kW, 1494 N m, 379 kg |
| 500 kW under 300 kg, tier 5 | V4 racing supercharged, 497 kW, 277 kg | W6 racing, 497 kW, 277 kg |
| 500 kW under 300 kg, tier 1 | V4 160 kW, short | R4 long 1.6, 256 kW, 292 kg, short |
| 20 MW, tier 5, Medium | B14 bored turbo, 19.8 MW, 13.2 t | V12 bored turbo, 20.4 MW, 11.4 t |
| 20 MW, tier 2 | W21 20.2 MW, 20.6 t | the same |
| 400 MW, tier 5, Large | B12 bored turbo, 411 MW at 1400 rpm, 232 t | B10 bored long 1.33 turbo, 408 MW at 1053 rpm, 217 t |
| 50000 N m, tier 1, Medium | (not run) | I7 long 1.6, 8.96 MW, 48892 N m at 1750 rpm, 13 t |
| 300 kW peak 3000 rpm, tier 2 | (not run) | V4 long 1.6, 307 kW, 939 N m at 3125 rpm, 323 kg |
| 300 kW peak 8000 rpm, tier 2 | (not run) | R8 short 0.8, 307 kW, 469 N m at 6250 rpm, 338 kg |
| 120 kW inline, tier 1 | I3, 120 kW, 237 kg | the same |

No built sample has a starved bank. Planning cost: main 2,640 plans in 4.4 ms (warm request 15 ms); branch about 7,400 in 18 to 27 ms
(request 42 to 49 ms), yielding every 1,024 plans as before.

`--speed-test` of two shipped generators with the kit's engine (`--gen-vehicle NAME --seed 1 --engine-tier T --top-speed V`): Bandit
Car 1 tier 3 (1819 kg, 529 kW, 22.2 m/s, grip) and tier 5 (1694 kg, 596 kW, 24.8 m/s, grip); Bandit Box Truck 1 tier 3 (2745 kg, 529
kW, 22.2 m/s) and tier 5 (2726 kg, 611 kW, 22.2 m/s): **identical on main and the branch**, since a vehicle's request keeps the Small
limiter. `--gen-vehicles`: 238, all identical to the committed files; nothing to regenerate.

## 4. What a saved engine sees

Keys don't change, every key still parses, mass and health are main's. Catalogue cylinders, plain-stroke pressed ones, and every
engine the kit made before (it only made plain strokes) give the same numbers. A long-stroke cylinder gives its stroke times more
kW, but its old fuel system was sized for main's power, so the bank reads starved by the same share and the engine's power is
**unchanged** until a wider fuel system goes on (the measured car above: 240 kW both, then 384 kW fed); its revs were already the
low ones. A short-stroke engine gives less power at the same revs (its fuel system has room to spare). An engine near the starter size
may change whether it needs one. `tools/MIGRATING.md` says so.

## 5. Checks run

Build (0 errors); `--gen-engine-parts --check` 12 files, 0 out of date; `--press-part presets` 0 problems; `--check-construct-parts`
exit 0, no REFUSED lines; `--gen-vehicles` 238 identical; `--gen-engine` samples above with `--plans 1`, estimate against build
within the bored notes' margins (V4 long 1.68 x 3.40 x 0.90 planned as B4, built V4 1.60 x 2.14 x 2.14; Medium I7 17.16 x 3.60 x 8.60
against 17.00 x 3.60 x 8.50); `--again` builds the same blueprint. Not run: self-test sections (batched by the coordinator), a
joined client, a world saved and loaded in a fresh process (a blueprint was used: the numbers come from the keys on load), any
window or picture.

## 6. Open questions (recommendation first)

1. **Is torque by the stroke squared too strong?** A 1.6 stroke gives 2.56 times the torque, and with gearing torque matters less than
   power, so a long stroke is nearly free power for 1.6 times the metal.
   - A (recommended): keep it: the user's words, and its costs are mass, height, fuel, revs and a wider fuel system.
   - B: revs fall by the square root of the stroke (torque by `r^1.5`), so the gain partly shows as revs.
2. **Vehicles keep the plain stroke.** Their stock gearbox is geared for the Small limiter, so the kit's vehicle request holds them to it.
   - A (recommended): leave it until the vehicle kit picks a gearbox for the engine; then let it plan long strokes with a taller final drive.
   - B: allow strokes whose limiter still reaches the wish's top speed in top gear (needs the gearbox's ratios in the plan).
3. **Starter by displacement.** The cube root of the stroke changes it only near the starter size.
   - A (recommended): keep it.
   - B: starter by the crank's scale alone, as before.
4. **Plan cost.** The kit makes three times the plans (18 to 27 ms warm, sliced).
   - A (recommended): leave it; it runs in a generation's slices.
   - B: plan strokes only for the best bores (about half the cost).
5. **A blank past 1.6** is metal for nothing. A (recommended): leave it (old keys load); B: refuse longer cylinder blanks at the die
   for new keys.

## 7. Merge review, round 1 (2026-11-14)

1. **Boosters by bore.** A bank's booster share was its kW over the bank's kW by bore and stroke, so a bank-sized Turbo on 1.6 strokes
   gave 1.25 instead of 1.4 (about 11% less power on load; a Supercharger about 7.5%). Now a booster's share is over the bank's
   largest cylinder's kW at its **bore alone** (`boreKw` in `ConstructEngine.Build`): a booster sized for its bank boosts it whole at
   any stroke; the fuel system still feeds by bore and stroke. The kit presses boosters by the bore (`Bore.OfBoost`). A kit I4 bored
   turbo engine saved by main (`--gen-engine --read`, on main and here), its cylinders swapped for 1.2 m ones: main 309 kW 943 N m;
   here 363 kW 1109 N m, its 50 cm carburettor (a sixth to spare on main) feeding 73.5%; with a 60 cm one, 494 kW. A saved engine never
   loses power: it gives its old kW times the lesser of the stroke and its fuel system's headroom. MIGRATING says so.
2. **The creative Engine tab's Car bay** now holds the request to the Small limiter (`MinLimiterRpm`, as a vehicle's), and the notice
   gives torque at peak rpm and the limiter. Not tried in a running game (read and built only).
3. **The starter by the engine as built.** `NeedsStarter` read the kW-weighted stroke of the cylinders giving power, so wrecking a
   plain one on a mixed engine near `starterFrom` could raise it past and stop the engine. Now it reads `ModEngine.BuiltStroke`, the
   same mean over every cylinder as built (wrecked and unfed too). Settled by reading; no mixed engine near the starter size was built.
4. **The kit's slices.** `Bores` yields before each bore and stroke's pieces are pressed, and presses short strokes only when revs above
   the size's peak are asked. `--slice-vehicle engine` (new) after the game's warm-up: a first Medium tier 5 20 MW request 9 ms of kit
   over 4 slices, longest span 1.6 ms; a first Large 400 MW 29 ms over 6, longest span 2.6 ms. The one long slice (14 to 16 ms) holds
   1.9 ms of kit and a garbage collection ("[vehicles] slice ... a garbage collection ran"); warm requests 6 to 7 ms, longest slice
   3.4 ms. The warm-up still warms a Small tier 3 request only (a Large one there would cost its 30 ms at every start).

Samples after the round (`--gen-engine`, seed 0): boosters now pressed by the bore are smaller, so some picks moved: 2000 kW tier 5
a B12 racing long-stroke supercharged, 1986 kW, 6070 N m at 3125 rpm, 822 kg; 20 MW tier 5 Medium a B10 bored long-stroke turbo,
20.4 MW at 2105 rpm, 10.5 t; 500 kW under 300 kg an I3 racing long-stroke supercharged, 495 kW, 296 kg. No built sample has a starved
bank. `--gen-vehicles` 238 identical; the four kit vehicles' `--speed-test` unchanged; `--gen-engine-parts --check`, `--press-part
presets` and `--check-construct-parts` clean.
