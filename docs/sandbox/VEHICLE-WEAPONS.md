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

Phase 1 measured: all 123 shipped seeds still generate (71 change; regenerate after merge); a 60-vehicle `--armoury-report --seeds 2` drew 13 gunsmith guns of 145. `--gunsmith-report`: an 800 kg buggy gets single autocannons and chain guns (a 2x2 at tier 3); a 3 t truck a single Field Gun or Howitzer (1500 N s, a
512-damage shell with a 13 m blast) and up to a 4x4 autocannon; a 12 t truck a 2x2 Howitzer at tier 3 (2048 damage, a 23 m blast); 24 t a 3x3 (4608 damage, a 34 m blast,
a 14 m crater).

## Blasts (phase 1)

`ConstructWorld.Blast` now gathers a body's parts in reach once, sorts them nearest first and walks the list (`BlastBody`), checking each part's identity before it hits it
and looking a moved part up from where it was (Compact keeps the order). A blast over m parts of a body of n costs n plus a sort, not m x n. A blast set off inside a blast
(none today) takes the old walk. Shove, damage and craters already go with a shell's filling (damage x k squared to 1 - distance / radius; push 2.4 x damage x k / mass
m/s; blast 9 m x sqrt(damage / 320) to 45 m, crater 0.4 x blast to 14 m), so the gunsmith's bigger shells blast wider without a rule change.

## Next steps (queued)

1. **Bigger blasts.** Tune shove and part damage for the biggest shells by trying them on real vehicles; lift the 45 m blast and 14 m crater caps for giant guns
   (`WeaponParts.json`), measured against the 8 ms tick. Report the caps (blast budget, `MaxShells` 1024, the 8 ms tick) as they scale; cook-offs of ammo and fuel chain through
   the existing cook-off queue (fuel tanks already go up: commit 6530762a).
2. **Missile batteries:** the modular ordnance (ConstructOrdnance, OrdnanceDesign, OrdnanceFlight, ModularRack) as a class, racks sized by the same ceiling.
3. **Exotic:** railguns (Railspike Coil merged), mortars (Hand Mortar, Bucket Launcher merged), flamethrowers (Firebreather merged), bomb drops (ordnance released down).
4. **Giant moving bases (100-500 m):** merges to 6x6 and batteries of them; needs the blast walk fix first.
5. **Standalone turret generator:** a random turret as a creative spawn and as survival loot.
6. **Gunners and auto-targeting:** AI vehicles crewed by bots in gunner seats now; a full auto-targeting parts set after this.
