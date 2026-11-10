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
  won't build as one firing gun is left out with a `[vehicles] gunsmith:` line and the next smaller is offered. Found so far: the Bunker Buster won't merge 3x3.
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
Checked as a towed thing (its build, faces, how it stands).

## Next steps (queued)

1. **Blast landing ticks.** 4x4 shells: split a landing's work (the first walk slice, the wrecks it makes) so no landing tick passes 8 ms, or cap the gunsmith at 3x3 until then.
   Ammo and fuel cook-offs chain through the existing queue (not tried with big shells yet).
2. **Missile batteries (not started):** an Ordnance Rack (`ModularRack`) of designed heat-seeking missiles on the turret's hinge leaf beside a spotting gun (a rack is not a gun: only a
   gun aims a turret's joints; a rack fires on its input, wired from the Turret Module), stations and missile size from the same mass ceiling.
3. **Turret loot (not done):** loot tables hold parts, not bodies: a survival turret needs a world spawn of the Turret blueprint (a site) or a kit item that builds one.
   Turret seeds still to sweep across styles (a 4x4 autocannon didn't merge on a ground-level base; the base now starts at the test stand's height).
4. **Exotic:** railguns (Railspike Coil merged), mortars (Hand Mortar, Bucket Launcher merged), flamethrowers (Firebreather merged), bomb drops (ordnance released down).
5. **Giant moving bases (100-500 m):** merges to 6x6 and batteries of them (capital weapons are a separate catalogue: project doc capital-weapons.md).
6. **Gunners and auto-targeting:** AI vehicles crewed by bots in gunner seats now; a full auto-targeting parts set after this.
