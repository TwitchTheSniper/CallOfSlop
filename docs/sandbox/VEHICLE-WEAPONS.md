# Vehicle weapons: the gunsmith

The vehicle kit's guns (docs/vehiclekit/VEHICLEKIT.md, "The armoury") are fitted through the game's own build rules, so every gun a kit vehicle carries is a gun a player
could build. This document is the plan for making them big: heavy cannons, missile batteries, rapid heavy guns and exotic weapons, sized by the vehicle that carries them,
with blasts that wreck many parts at once. Phase 1 (2026-11-10) is the survey, the generator core and its first two classes.

## Why the kit's guns came out weak (survey, 2026-11-10)

Measured with `--check-armoury` (41 recipes, damage a shot x rounds, rounds a minute, kg of the gun's segment):

| What | Finding |
| --- | --- |
| Rounds | 37 of 41 recipes are bullet guns (rifle, pistol, shell-shot bores): 16 to 520 damage a shot, a 300 m hitscan range. A 1 m steel plate has 160 hp, so a "heavy" gun takes a plate a shot at best and makes no blast. |
| Blasts | A bullet gun with explosive rounds gets a fixed 2.5 m splash at 1.2x its damage (`ConstructWeapons.Finish`). Only shell guns scale their blast (`ConstructShells.GunNumbers`: 9 m x sqrt(damage / 320), at most 45 m; crater 0.4 x blast, at most 14 m). |
| Artillery | The weapon builder has two artillery receivers (Field Gun, Howitzer) with their tubes, feeds and shells. No recipe and no style used them. They fit on a kit turret as they are: a 1x1 Howitzer throws a 512-damage shell (a 13 m blast), a 3x3 one 4608 (a 34 m blast, crater 14 m). |
| Merges | The game merges receivers up to 6x6 (`ReceiverMerge.json`: damage x N squared, rate / N), the armoury file stops at 3x3, and only two recipes are 3x3. |
| Picks | A gun's chance depends on its class (light, medium, heavy) and least tier, never on the vehicle: a 1 t buggy and a 16 t box truck draw from the same list with the same weights. Most draws land on the many light and medium recipes. |
| Shove | A blast pushes a body by `k x damage x 2.4 / mass` m/s (`ConstructWorld.Blast`): a 500-damage blast moves a 5 t truck 0.24 m/s. |
| Cost | `ConstructWorld.Blast` finds parts nearest first by a fresh scan of the body for each one: a blast reaching m parts of a body of n costs m x n. Fine at a 2.5 m splash, the first thing to fix before 30 m blasts reach whole giant bodies. |
| Not usable yet | The Hellwheel Rotary's six barrel sockets have no place on a construct receiver (`FitGun` refuses "barrel_1"); rotary guns use the Ladder and Cross splitters instead. |

## The generator (phase 1)

`Source/VehicleKit/VehicleGunsmith.cs` makes guns for one vehicle, as `WeaponOption`s in the weapon builder's text, and adds them to the style's list for that vehicle
(`Vehicle.Arsenal`): every pick the kit makes (the grammar's, a role's, a generator's) can draw them, and every recipe and style gun it had before stays in the list.

- **The ceiling comes from the vehicle.** Its mass when the turret goes on (the body is built by then) rates the mount: the gun's estimated mass (its parts' masses x N
  squared) at most `GunMassShare` of the vehicle's, and its recoil impulse (N s) at most `KickPerKg` x the vehicle's mass, so one shot moves it at most that many m/s.
  The biggest merge N that passes both is the ceiling; nothing passes, no gun of that class.
- **Role and tier pick within it.** Tier 1 takes two steps under the ceiling, tier 2 one, tier 3 and 4 the ceiling. A combat role weighs the class up, others down.
- **Classes in phase 1.** Heavy cannons (Field Gun, Howitzer: HE, siege or incendiary shells, big blasts and craters) and rapid heavy guns (Hammerfall autocannon,
  Ladder-splitter rotary cannon, Cross-splitter flak, Thumper chain gun: merged blocks). Each pick is seeded by the vehicle's dice.
- **Only sizes that build.** Each design and size is fitted once a process on a test turret through the game's rules (as `--check-armoury` fits a recipe); a size that
  won't build as one firing gun is left out with a `[vehicles] gunsmith:` line and the next smaller is offered. Found so far: the Bunker Buster wouldn't merge 3x3 (fixed 2026-11-10, "Kit review" below).
- **Share of the draws.** The gunsmith's guns together take about half a fighting role's draws, a quarter of another role's and 35 % of a vehicle with no role's; each class
  takes an equal part of that, split among its designs. Shell guns sit behind a gun shield, rapid guns on a pintle (a shield on every turret filled a double-decker's side
  box past the Silhouette rule).
- **Off switch:** `--no-gunsmith` (the old lists, to measure what it changes), as `--no-armoury` does for the armoury.
- `--gunsmith-report` prints what the gunsmith offers vehicles of 0.8 to 24 t at each tier, with each gun's mass and recoil.

Phase 1 measured: all 123 shipped seeds still generate (71 change; regenerate after merge); a 60-vehicle `--armoury-report --seeds 2` carried 71 guns, about 13 of them the gunsmith's. `--gunsmith-report`: an 800 kg buggy gets single autocannons and chain guns (a 2x2 at tier 3); a 3 t truck a single Field Gun or Howitzer (1500 N s, a
512-damage shell with a 13 m blast) and up to a 4x4 autocannon; a 12 t truck a 2x2 Howitzer at tier 3 (2048 damage, a 23 m blast); 24 t a 3x3 (4608 damage, a 34 m blast,
a 14 m crater).

## Blasts (phase 1)

`ConstructWorld.Blast` now gathers a body's parts in reach once, sorts them nearest first and walks the list (`BlastBody`), checking each part's identity before it hits it
and looking a moved part up from where it was (Compact keeps the order). A blast over m parts of a body of n costs n plus a sort, not m x n. A blast set off inside a blast
(none today) takes the old walk. Shove, damage and craters already go with a shell's filling (damage x k squared to 1 - distance / radius; push 2.4 x damage x k / mass
m/s; blast 9 m x sqrt(damage / 320) to 45 m, crater 0.4 x blast to 14 m), so the gunsmith's bigger shells blast wider without a rule change.

## Phase 2 (2026-11-10): tried by hand, blasts by shell size, turrets

**By hand.** `--bench-vehicle-guns [BLUEPRINT...] [--shells N] [--merge N] [--map NAME]` (`Source/App/BenchVehicleGuns.cs`, a bench, not a test): committed blueprints in a row on
Proving Ground, each gun listed and fired once, then N shells of each shell gun (or of an N x N Howitzer with `--merge`) landed on the next vehicle, every tick's thread CPU timed.
Every gunsmith gun built and fired (Howitzer and Field Gun shells left the muzzle; autocannon, Bunker Buster 2x2 and 3x3 rounds). Found and fixed:

- **Start-up hang.** A committed blueprint with a shell gun built its guns while the maps loaded (a sandbox map names blueprints), `ConstructShells.GunNumbers` read
  `World.GravityScale`, which starts `World`, which loads the maps: a deadlock in the parallel load (every command that loads maps hung on main), a Lazy recursion in a serial one.
  It reads 1 until `Maps.Loaded`.
- **Wreck storms.** Four 512-damage shells on a 2.5 t truck broke it into 110 one-part wrecks, and their first ticks took 30 to 89 ms. A blast now leaves at most
  `ConstructWorld.BlastDebris(radius)` wrecks (8, plus one each 2 m of radius, to 32: a shell's walk and `ConstructWorld.Blast` alike; a massive blast's count also covers parts broken
  off); past that what it breaks off is gone. The same salvo: worst tick 9 ms, median 0.2 to 0.5 ms; Field Gun salvoes under 4.4 ms.
- **Recoil** reads 0 m/s straight after `Fire`: it is applied in the physics step (`AddRecoil`), so the bench can't see it; not checked.

**Blasts by shell size.** The 45 m blast and 14 m crater caps (`WeaponParts.json`) are now a single gun's: a merged gun's grow with sqrt(N) (`ConstructShells.ShellSize`).
A 4x4 Howitzer's 8192-damage shell blasts 46 m and digs 18 m (27 m siege); a 6x6's (not offered yet) 68 m and 27 m (34 m cap), still a plain blast under the 100 m massive-blast line.
A shell crater of 8 m or more is dug in bands like a massive blast's (`Game.BandedCrater`, `MegaGround`): at once, an 18 m crater took 50 to 1000 ms.
Measured, four 4x4 shells a vehicle on five vehicles 30 m apart: medians 0.3 to 1 ms, but landing ticks still 10 to 37 ms, one 133 ms (the 400-part walk, wrecks made, bands
started): **4x4 shells are over the 8 ms tick** and the gunsmith still offers them to 24 t vehicles. Caps today: blast 45 x sqrt(N) m, crater 14 x sqrt(N) m, 400 parts walked a tick
(`ShellPartsPerTick`), 256 bodies a blast, 8-32 wrecks a blast, 1024 shells in the air.

**Turrets.** `Generators/Turret.cs`: a random complete turret from a seed (a steel base plate, the kit's turret: turntable, elevation hinge, gun, the gunner's seat and Turret Module),
its gun the gunsmith's for a mount rated as a 0.8 to 16 t vehicle's at tier 1 to 4, or a quarter of the time one of the style's own. It is in
`VehicleRegistry.Everywhere` (every shipped style ships seeds 1 and 2; roles and convoys never draw it), so creative gets a tile per style and "New seed" makes another.
Checked as a towed thing (its build, faces, how it stands); a 1 m base plate with an outrigger plate on each side (without them heavy guns tipped it 8 to 17 degrees).
`--sweep-vehicle` Bandit and Highland Turret: 8 of 8 seeds pass each. Its blueprint files (10, seeds 1 and 2 of five styles) come with the next regeneration of `Constructs/Blueprints`.

## Phase 3 (2026-11-10): landing ticks, missile launchers

**Landing ticks.** Measured with `--bench-vehicle-guns --merge 4` and timers round each stage: a 4x4 salvo's cost was not the walk but what it set off. (1) Parts breaking off:
320 broke in one tick (50 ms; 0.03 to 0.16 ms each: removal, split, a wreck). A broken part now costs `ShellBreakCost` (4) more of the walk's 400-part budget, and a tick's
walks stop once `ShellWalkMs` (3 ms) has gone by after a break (`ConstructWorld.WalkBlast`). (2) The shove: an 8192-damage blast gave a 20 kg wreck 880 m/s; speed is
clamped only after the contacts, so that tick's ground search reached 15 m round each piece (100 to 220 ms ticks). A plain blast now pushes at most `MegaShoveMax`
(30 m/s) and never past `ConstructPhysics.MaxSpeed` or the speed a body had (`ConstructWorld.Shove`, shells and `ConstructWorld.Blast` alike). Measured with `--gap 100`
(vehicles out of each other's blasts): warm 4x4 salvos on full trucks, worst tick 6.3 to 6.4 ms, medians 0.3 to 0.4 ms; the process's first salvo still reaches 12 to 25 ms
(first breaks, first wreck contacts and first crater bands: JIT, by the second salvo they are gone). The gunsmith keeps 4x4. Small blasts push less than before (a wreck
from a 512-damage shell went up to 61 m/s, now 30), which may move seeded digests.

**Missile launchers.** A fourth class, `MissileBattery`: a spotting gun (the Chain Cannon) on the turret and an Ordnance Rack on the arm's side, hooks out and its front
the gun's way, a radar-seeking missile (`Ordnance:M25:Radar Seeker+Impact Fuse+Warhead HE+Tail Fins+Solid Motor`) on each station, wired from the Turret Module so the
gunner's fire lets them go (`Vehicle.MissileRack`; `WeaponOption.Rack` and `.Ordnance`). Its mass (gun, rack, missiles) is rated as a gun's; it has no recoil of its own.
Offered from 1.5 t (130 kg). `--bench-vehicle-guns --battery N` puts the gunsmith's stand with one first in the row, held 3 m up; each rack's next vehicle is set 70 m ahead
and the rack emptied: the Launch Rack's missile locked the truck, flew and wrecked it (114 parts to none; that tick 23 ms, not yet told apart from JIT).
Where the body is in a station's way (cab-over tankers, a double-decker), the station stays empty and the gun still fights: no undo for the rack once on (4 of 193 seeds).
The standalone Turret never takes one (0.5 m up its missile drops into the ground). `--gen-vehicles`: 36 of 193 committed vehicles differ, none fail.

## Phase 4 (2026-11-10): multi-station batteries, a flame projector

**Multi-station batteries.** The gunsmith's ladder is back to four sizes: Launch Rack (1), Twin Launch Rack (1 x 2), Launch Rack 2x2 (rows fore and aft) and Launch Rack 1x6, all 25 cm
by 3 m presets from the part kit's `air_weapons_parts`; a six-station rack stands out on a 1 m beam, the others on half a metre. The misses were never in the flight: on
`--bench-vehicle-guns --battery N` (now printing each missile's lock and where it ended) every missile of the 2-, 4- and 6-station racks locked the truck 70 m ahead and
burst within 3 m of it (one of six went off on the first missile's wreckage 15 m short). What failed was the loading: stations were refused by a centimetre's rounding.
A designed missile's hidden lug box poked 1 cm past its lug into the rack's face, and its body box was exactly the station pitch wide; a part's place rounds to the
centimetre and a 25 cm missile hangs 17.5 cm under its lug, so a station took its missile or not by luck (all six of a 1x6 empty; a double-decker's twin rack empty).
The lug box now stops at the lug and the body box is 5 mm inside the missile's radius (`OrdnanceDesign`); the kit's refusal names the real blocker before the rack it
hangs on (`VehicleCore.Blocker`). Offered by mass: twin from 3 t (tier 3) or 6 t, 2x2 from 6 t at tier 2 and up; the 1x6 is over every vehicle's 0.4 mass share so far
(it is in the ladder for heavier ones). Salvo ticks on the bench: worst 14 to 21 ms with the process cold (JIT not yet told apart).

**Flame Projector** (the first exotic class, `Kind.Exotic`): a Firebreather receiver merged 2x2 to 4x4 (23 to 93 kg), tags heavy, close, flame, from tier 2. It joins
the gunsmith's draw for every vehicle; roles don't pick by tag yet, so "by role" is still to do (mortars for artillery roles, flame for close ones).
`--gen-vehicles`: 35 of 193 committed vehicles differ (10 now carry a Firebreather), none fail; two stations stayed empty where the body was in the way.

## Kit review (2026-11-10)

- **Bay guns.** The grammar picks a gun bay's or hutch's gun while it plans, before anything is built, when the parts weighed next to nothing: the gunsmith offered nothing and
  only roof and flank guns (picked as built) could be its. A plan now gives its estimated mass (`ShapeGrammar.EstimateMass`: a chassis, each segment by kind and length,
  fitted to 124 built vehicles and taken a tenth low) as `Vehicle.PlanMass`, which `Vehicle.Arsenal` rates every pick by. After the build each gunsmith gun is checked against
  the vehicle as built (`VehicleGunsmith.CheckBuilt`): one over its mount refuses the plan (the next plan), never a smaller gun swapped in. `--armoury-report --seeds 2`
  (60 vehicles, new "smith" column): 10 of 85 guns the gunsmith's before, 30 of 85 after.
- **A budget a vehicle.** Each gun alone is held to `GunMassShare` (12 %); all of them together now to `GunBudgetShare` (20 %) of the mass and `PullPerKg` (1.5 N a kg)
  of sustained recoil (impulse x shots a second, measured on the test stand: a single Flak Block pulls 750 N, a 4x4 3000 N), spent as guns are picked (`Vehicle.SpendGun`):
  the gunsmith steps a gun down a size, or leaves it out, to fit what is left. Style guns count by their parts' mass only (they aren't fitted on a stand).
- **Roles read tags.** A role names the gun jobs it wants (2.5x weight) and avoids (0.05x): a scout avoids heavy, flame, boss and anti-armour, a tanker and a carrier flame
  and heavy, a gun platform wants heavy, anti-armour and aa, command aa (`VehicleRole.WantTags`, `AvoidTags`; `RoleAsk.TagWeight`).
- **Exotics by tier.** An exotic is offered from its own tier only (not one early), at most N = tier, and one size per 2 t of vehicle from 2x2: an 800 kg buggy gets no flame
  projector, 3 t a 2x2, 6 t a 3x3 at tier 3 and a 4x4 at tier 4.
- **Bunker Buster.** Its receiver is 0.206 m tall: a part's place rounds to the centimetre, so a column built socket to socket drifted 4 mm a row and the third row sat 8 mm
  off the merge grid (6 mm tolerance). Generated receivers are now whole centimetres a side (`ConstructWeaponParts.MergeGrid`): it merges 3x3 and 4x4. This moves every
  generated receiver's sockets by up to 2.5 mm.
- `--gen-vehicles` after these: 139 of 193 committed vehicles differ (regenerate after merge), none fail.
- **Spawn safety** (VEHICLEKIT.md, "Dynamic spawn"): a late slice is dropped but the game never runs beside the kit's thread; the gunsmith's test fits run in the warm-up.
- **Seeds that failed** (`--sweep-vehicle --seeds 20`, each 19 of 20): a lofted bay refused for a hidden box 9.95 mm thick (the loft's own test was 9.9 mm, the loader's 10 mm on
  the rounded numbers: `HullLoft.Builder.Thin` now drops boxes and facets under it); Highland War Bus 16, two 1 m panniers on bays under a metre whose faceted shells met
  (the overlap test now uses the drawn bounds, `Vehicle.PannierClear`): passes. Bandit Box Truck 3 still fails the driver's view: its armoured chop-top cab, with or without
  the gunsmith (body styles' to fix).
- **Generation time** (step 5, done on perf/kit-gen, VEHICLEKIT.md "Generation time"): `Put`'s scan was the cost, and not its tries: every try out of reach wrote
  the refusal's text, with a pass over every part's boxes for its blocker, though only a refusal reads it. `Put` now passes over parts and sockets too far to matter, asks
  the rules only within reach and writes the refusal only when it refuses, and reads a part's taken sockets once; every committed seed stays identical. The gunsmith's test
  fits cost about 0.3 s now (they were slow for the same reason), so they stay in the warm-up.

## Next steps (queued)

1. **Racks that aim.** A rack on the elevation hinge's leaf would let the gunner lift it (only a gun aims a turret's joints today), and let ground turrets carry one.
2. **Exotic:** the Flame Projector is in (phase 4); next mortars (Hand Mortar, Bucket Launcher merged) for artillery roles, and picking by role tag (flame for close roles); then
   railguns (Railspike Coil merged) and bomb drops (ordnance released down).
3. **Gunners (not started, phase 4 ran out of time):** AI vehicles crewed by bots in gunner seats that aim and fire at players (reuse the bot and turret aim code); then the auto-targeting parts set.
4. **Turret loot (not done):** loot tables hold parts, not bodies: a survival turret needs a world spawn of the Turret blueprint (a site) or a kit item that builds one.
   Turret seeds swept for two styles only.
5. Ammo and fuel cook-offs chain through the existing queue (not tried with 4x4 shells). The first-salvo ticks (12 to 25 ms) and the cold missile salvo ticks (14 to 21 ms) still want a warm-up pass (warm, then measure) to tell JIT from real cost.
6. **Giant moving bases (100-500 m):** merges to 6x6 and batteries of them (capital weapons are a separate catalogue: project doc capital-weapons.md).
