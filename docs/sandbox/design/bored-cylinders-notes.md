# Bored cylinders: notes (2026-11-14)

Branch `claude/bored-cylinders-d1fe97`. The user's answer of 2026-11-14 (`sized-gun-engine-parts.md`, the last block of section 14): a
wider bore is less metal, so lighter and easier to damage; power follows the cylinder's size. **Protocol 312** (a pressed engine
piece's mass, health and a cylinder's kW change on both peers; no new bytes, no part ids move).

## 1. Why a bored engine weighed about twice a plain one

Three causes, measured with `--press-part` on the branch base:

1. **A pressed piece weighed its whole blank, a catalogue piece its file.** The Small Cylinder's own preset (`pipe10 45x45x75`)
   pressed as a key weighs 77.2 kg and holds 412 hp (steel blank plus three Piston Sets); the catalogue Small Cylinder it stands for
   is 28 kg and 180 hp. So any cylinder one step off a preset weighed about 2.8 times its catalogue neighbour (Medium: 1748 kg against
   560). The same held for the Engine die: a Small Crank Section 30 kg, pressed one step longer 65 kg; Small Injector Rail 6 kg,
   the kit's pressed one 54 kg. The kit planned plain engines from catalogue parts and bored ones from pressed parts.
2. **The kit's bore scaled the whole cylinder, wall included.** The racing bore was the preset times 1.38 on every side:
   `pipe15 60x60x105`, a thicker wall and a bigger outside (183 kg), not a wider hole in the same cylinder. Its power came from the
   outside across.
3. **The bigger outside needed longer crank sections** (`solid 45x45x65`, 70 kg pressed), and in a V, W or radial the 60 cm
   cylinders met at the crank, so those plans were refused (section 4).

Sum for 2000 kW at tier 5: a Small B16 of 16 x 183 kg cylinders, 8 x 70 kg cranks, 54 kg injectors, 39 kg turbos: 3857 kg, against
1531 kg for the plain R32 of catalogue parts.

## 2. The rule chosen

- **Power by bore.** A pressed cylinder's scale is read from its **bore**, its across less both walls (`Pressed.BoreOf`), through the
  presets' bores (Small 25 cm, Medium 140, Large 560: each preset its file's scale) by a straight line in log-log, the end segment past
  the ends (`Pressed.BoreScale`); kW is `EngineRules.At` of that scale, as before. One rule, no tier and no bored flag. A thinner wall
  in the same across is a wider bore and more power. The bore counts up to `MaxBoreShare` = 0.9 of the across (a wall under a
  twentieth of the across is lighter and weaker but gives nothing more).
- **Stroke** is unchanged (length over across against the nearest preset's): it trades revs for torque at the same power (answer 5).
  So torque follows bore and stroke, and power follows bore.
- **Mass and health by metal** (`Pressed.PieceMass`, every pressed engine piece): the blank's solid volume times what a cubic metre
  of metal weighs and holds in the catalogue piece of its size, read through the pattern's presets in log of across (ends held). At
  a preset it is the file's part exactly; a bigger piece is heavier because it has more metal; a wider bore in the same across is
  lighter and has less health. Held to the blank and bay 2 (`CostMass`), so recycling never pays more than went in (the per-volume
  figures are 0.2 to 0.55 of steel's, so the hold never bites today).
- **The Engine Kit** bores by thinning the wall (`EngineKit.BoredAt`): the plain preset's across and length with the thickest
  whole-cm wall that gives the grade's power (1.3, 1.6, 2; `Grade.Bore` is gone), else the thinnest that adds any. Its fuel system
  and booster are pressed to the cylinder's scale as before; the crank is pressed longer only when one of those is wider than the
  section. A bored plan's score adds `0.1 x` the share of the plain cylinder's health it gives up, so toughness counts a little.

## 3. Numbers, before and after

Pressed cylinders (`--press-part cylinder.cylinder KEY`), mass, health, kW (revs unchanged for every key):

| Key | Before | After |
| --- | --- | --- |
| Small `pipe10 45x45x75` (the preset) | 77.2 kg, 412 hp, 40 kW (a preset loads as the 28 kg file) | 28 kg, 180 hp, 40 kW |
| Small `pipe8 45x45x75` | 70.9 kg, 349 hp, 40 kW | 23.7 kg, 152 hp, 52.1 kW (x1.30) |
| Small `pipe6 45x45x75` | 63.6 kg, 276 hp, 40 kW | 18.7 kg, 120 hp, 64.8 kW (x1.62) |
| Small `pipe4 45x45x75` | 43.3 kg, 193 hp, 40 kW | 13.1 kg, 84 hp, 79.0 kW (x1.98) |
| Small `pipe15 60x60x105` (phase 6's racing bore) | 183.3 kg, 1113 hp, 74.1 kW | 66.7 kg, 307 hp, 55.1 kW |
| Small `pipe10 60x60x100` | 138.5 kg, 785 hp, 74.1 kW | 47.1 kg, 217 hp, 90.6 kW |
| Medium `pipe20 180x180x300` (preset) | 1748 kg pressed | 560 kg, 720 hp, 800 kW |
| Medium `pipe10 180x180x300` | 1041 kg, 8011 hp, 800 kW | 297.5 kg, 383 hp, 1067 kW (x1.33) |
| Large `pipe80 720x720x1200` (preset) | 96750 kg pressed | 11200 kg, 2880 hp, 16000 kW |
| Large `pipe40 720x720x1200` | 51511 kg, 512708 hp, 16000 kW | 5950 kg, 1530 hp, 21388 kW (x1.34) |

In each across, mass and health fall with the bore at all three scales (before, a wider bore in one across gave no power at all); a bigger cylinder of the same wall share is heavier.

`--gen-engine`, seed 0:

| Request | Before | After |
| --- | --- | --- |
| 300 kW, tier 5 | Small V6 plain, supercharged, 300 kW, 385 kg | Small I3 racing bore, supercharged, 296 kW, 259 kg |
| 2000 kW, tier 4 | Small R32 plain, turbo, 2150 kW, 1531 kg (R24 and W24 of bores refused) | Small R32 bored, injected, 2000 kW, 1374 kg |
| 2000 kW, tier 5 | Small B16 racing (pressed wide), turbo, 1991 kW, 3857 kg (W18 refused) | Small W21 performance, supercharged, 2006 kW, 967 kg |
| 900 kW, tier 5, V | V16 plain turbo, 896 kW, 907 kg | V10 racing, 933 kW, 450 kg |
| 900 kW, tier 5, W | W15 plain supercharged, 900 kW, 797 kg | W12 performance, 917 kW, 514 kg |
| 900 kW, tier 5, radial | R16 plain turbo, 896 kW, 811 kg | R8 racing turbo, 885 kW, 441 kg |
| 500 kW under 300 kg, tier 5 | V4 269 kW, 267 kg, "short of 500 kW" | V4 racing turbo, 522 kW, 265 kg |
| 20 MW, tier 5, Medium | W15 plain turbo, 20.2 MW, 15.7 t | B14 bored turbo, 19.8 MW, 13.4 t |
| 400 MW, tier 5, Large | B16 plain turbo, 430 MW, 366 t | B14 bored turbo, 400 MW, 270 t |
| tiers 1 and 2 | (R8 320 kW 391 kg; Medium W21 20.2 MW 20.6 t) | the same |

`--speed-test` of two shipped vehicles' generators with the kit's engine (`--gen-vehicle NAME --seed 1 --engine-tier T --top-speed V`;
the committed files carry fixed engines and are unchanged):

| Vehicle | Before | After |
| --- | --- | --- |
| Bandit Car 1, tier 3, 33 m/s | 1944 kg, 480 kW plain, 19.5 m/s, grip | 1822 kg, 500 kW bored, 22.2 m/s, grip |
| Bandit Car 1, tier 5, 33 m/s | 1944 kg, 480 kW plain, 19.5 m/s, grip | 1673 kg, 560 kW racing, 25.3 m/s, grip |
| Bandit Box Truck 1, tier 3, 30 m/s | 2870 kg, 480 kW plain, 21.3 m/s, power against drag | 2748 kg, 500 kW, 22.0 m/s, power against drag |
| Bandit Box Truck 1, tier 5, 30 m/s | 2870 kg, 480 kW plain, 21.3 m/s | 2760 kg, 632 kW, 22.2 m/s, 0-20 in 4.5 s (was 7.5) |

`--gen-vehicles`: 238, all identical to the committed files (no committed vehicle moves; nothing to regenerate).

## 4. The V, W and radial fault

Before: the kit's bored cylinder was wider than the crank (60 cm on a 45 cm crank), so two banks at right angles put their cylinders
in each other at the crank ("the Pressed Part (cylinder.cylinder pipe15 60x60x105 steel) at [-0.27, 0, 0] ... is in the way"), and
`Build` gave way to the next plan (R24, W24, W18 refused in the samples above). Now a bored cylinder keeps the plain one's across, so
nothing meets at the crank: every V, W and radial sample above builds at the first plan, with no "better plans wouldn't build" note.

## 5. What a saved engine sees

Keys don't change and every key made before still parses. Tried: a world saved by the branch base (`saveworld`) holding the kit's
old Small B16 (16 `pipe15 60x60x105` cylinders, 8 `solid 45x45x65` cranks, pressed injectors and turbos), loaded here in a fresh
process (`loadworld`): the same 43 parts by key; the engine reads 1482 kW (was 1991: those cylinders' bore is 30 cm, scale 1.16),
the same revs (peak 4762, limiter 6667); its pressed pieces are about 40% of their old mass (a cylinder 183 to 67 kg, a crank 70 to
39 kg, an injector 54 to 12 kg) and health. Catalogue pieces and pieces pressed at a preset are unchanged. Nothing is dropped.
`tools/MIGRATING.md` says so.

## 6. Checks run

`--gen-engine-parts --check` 12 files, 0 out of date; `--press-part presets` 0 problems; `--press-part sweep cylinder` 28 blanks and
`sweep engine` 60 blanks, 0 problems (none heavier than its cost); `--check-construct-parts` exit 0, no REFUSED lines;
`--gen-vehicles` 238 identical. Not run: self-test sections (batched by the coordinator), a joined client, an engine of thin bores
turning in a window (only its derived numbers), any picture.

## 7. Open questions (recommendation first)

1. **"Power follows bore and stroke."** Read here as torque from bore and stroke, power from bore: stroke still trades revs for
   torque at the same power, as answer 5 says. Taking stroke into power too (displacement: power grows with a longer stroke) would
   undo answer 5.
   - A (recommended): keep answer 5 as built.
   - B: power by displacement (bore squared times stroke), revs still falling with stroke, so a long stroke gives more torque and more power.
2. **How thin a wall.** The bore counts up to 0.9 of the across, so a Small cylinder reaches about x2.3 (92 kW, `pipe2`) at 6.9 kg. A thinner wall
   only saves metal and health (a `pipe2 45x45x75` is 6.9 kg, 44 hp: one shot from most guns).
   - A (recommended): keep 0.9 and let health be the cost, as the user said ("easier to damage").
   - B: refuse walls under a share of the across at the die (new keys only; old ones still load).
3. **The Small preset is thick-walled** (bore 0.56 of its across, Medium and Large 0.78), so a Small cylinder bores to about x2 and
   a Medium or Large to about x1.35.
   - A (recommended): leave it: past that, a bigger cylinder is the way to more power, which is the user's "function of cylinder size".
   - B: thicken the Medium and Large presets' walls (their catalogue parts change shape; every vehicle with one moves).
4. **Survival has no gate on bores.** A thinner wall is a Small Press job, so a new player's first engine can be a racing one.
   - A (recommended): leave it; the cost is toughness and fuel (thirst is by power).
   - B: a thin-walled cylinder needs a Medium Press or a better die.
