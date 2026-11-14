# Bored cylinders: notes (2026-11-14)

Branch `claude/bored-cylinders-d1fe97`. The user's answer of 2026-11-14 (`sized-gun-engine-parts.md`, the last block of section 14): a
wider bore is less metal, so lighter and easier to damage; power follows the cylinder's size. **Protocol 311** (a pressed engine
piece's mass and a pressed cylinder's kW change on both peers; no new bytes, no part ids move). Section 8 is the merge review's round.

## 1. Why a bored engine weighed about twice a plain one

Three causes, measured with `--press-part` on the branch base:

1. **A pressed piece weighed its whole blank, a catalogue piece its file.** The Small Cylinder's own preset (`pipe10 45x45x75`)
   pressed as a key weighs 77.2 kg (steel blank plus three Piston Sets); the catalogue Small Cylinder it stands for is 28 kg. So any
   cylinder one step off a preset weighed about 2.8 times its catalogue neighbour (Medium: 1748 kg against 560). The same held for
   the Engine die: a Small Crank Section 30 kg, pressed one step longer 65 kg; a Small Injector Rail 6 kg, the kit's pressed one 54 kg.
   The kit planned plain engines from catalogue parts and bored ones from pressed parts.
2. **The kit's bore scaled the whole cylinder, wall included.** The racing bore was the preset times 1.38 on every side:
   `pipe15 60x60x105`, a thicker wall and a bigger outside (183 kg), not a wider hole in the same cylinder. Its power came from the
   outside across; on main a thinner wall in the same across gave no power at all.
3. **The bigger outside needed longer crank sections** (`solid 45x45x65`, 70 kg pressed), and in a V, W or radial the 60 cm
   cylinders met at the crank, so those plans were refused (section 4).

Sum for 2000 kW at tier 5: a Small B16 of 16 x 183 kg cylinders, 8 x 70 kg cranks, 54 kg injectors, 39 kg turbos: 3857 kg, against
1531 kg for the plain R32 of catalogue parts.

## 2. The rule chosen

- **Power by bore, anchored on the plain bore** (`Pressed.BoreScale`, `PlainBore`, `BoreOf`). A pressed cylinder's scale is its
  across's scale (main's rule, unchanged) times its bore (across less both walls) over the plain bore for its across: the presets'
  share of their across (Small 0.56, Medium and Large 0.78) read through them in log of across. So a cylinder with the plain wall for
  its across has main's power exactly; a thinner wall is a wider bore and more power by the same law as a wider cylinder (kW goes as
  scale^2.161); a thicker one, less. The bore counts up to `MaxBoreShare` = 0.9 of the across. No tier and no bored flag.
- **Stroke** is unchanged: it trades revs for torque at the same power (answer 5). Torque follows bore and stroke, power the bore.
- **Health** is the blank's, as on main (its metal times the material's): a thinner wall has less in proportion to the metal removed.
  Saved health stays true. A saved run-time piece whose health is above its whole is held to it on load (`WorldSaveBodies`).
- **Mass by metal, anchored on the catalogue** (`Pressed.PieceMass`, every pressed engine piece): the blank's solid volume times what
  a cubic metre of metal weighs in the catalogue piece of its size, read through the presets in log of across. At a preset it is the
  file's part; a bigger piece is heavier because it has more metal; a wider bore is lighter by the metal removed. Never more than
  the blank and bay 2 (`CostMass`). This is the change to the plain pressed cylinder: section 8, item 0.
- **The Engine Kit** bores by thinning the wall (`EngineKit.BoredAt`): the plain preset's across and length with the thickest
  whole-cm wall that gives the grade's power (1.3, 1.6, 2), else the thinnest that adds any, and names the plan by the grade it
  reaches. Its fuel system and booster are pressed wider to the cylinder's scale, at their own length (so a bored bank stands no
  taller); the crank is pressed longer only when one of them is wider than the section. A bored plan's score adds `0.1 x` the share of
  the plain wall's metal (so health) it gives up.

## 3. Numbers, main against this branch

Pressed cylinders (`--press-part cylinder.cylinder KEY`): mass, health, kW. Revs are unchanged for every key. Bore ratios are against
the plain bore of the across (whole-cm walls, so near the asked ratio); a preset key loads as its catalogue part on both (28 kg,
180 hp, 40 kW for the Small one), the rows show the pressed def.

| Key | Bore | Main | Branch |
| --- | --- | --- | --- |
| Small `pipe10 45x45x75` | plain (25 cm) | 77.2 kg, 412 hp, 40 kW | 28.0 kg, 412 hp, 40 kW |
| Small `pipe8` | 1.16 | 70.9 kg, 349 hp, 40 kW | 23.7 kg, 349 hp, 55.1 kW |
| Small `pipe7` | 1.24 | 67.3 kg, 313 hp, 40 kW | 21.3 kg, 313 hp, 63.7 kW |
| Small `pipe5` | 1.40 | 47.6 kg, 236 hp, 40 kW | 16.0 kg, 236 hp, 82.8 kW |
| Medium `pipe20 180x180x300` | plain (140) | 1748 kg, 15080 hp, 800 kW | 560 kg, 15080 hp, 800 kW |
| Medium `pipe11` | 1.13 | 1116 kg, 8760 hp, 800 kW | 325 kg, 8760 hp, 1037 kW |
| Medium `pipe9` (the 0.9 cap: 1.24 and 1.38 can't be had) | 1.16 | 965 kg, 7252 hp, 800 kW | 269 kg, 7252 hp, 1097 kW |
| Large `pipe80 720x720x1200` | plain (560) | 96750 kg, 965097 hp, 16000 kW | 11200 kg, 965097 hp, 16000 kW |
| Large `pipe43` | 1.13 | 55113 kg, 548730 hp, 16000 kW | 6368 kg, 548730 hp, 20886 kW |
| Large `pipe36` (the cap) | 1.16 | 46655 kg, 464151 hp, 16000 kW | 5387 kg, 464151 hp, 21896 kW |
| Small `pipe10 50x50x85` (phase 6's 1.13) | 1.05 | 101.4 kg, 534 hp, 50.1 kW | 34.6 kg, 534 hp, 56.2 kW |
| Small `pipe10 55x55x95` (phase 6's 1.24) | 1.10 | 115.2 kg, 672 hp, 61.5 kW | 41.8 kg, 672 hp, 74.1 kW |
| Small `pipe15 60x60x105` (phase 6's 1.38) | 0.86 | 183.3 kg, 1113 hp, 74.1 kW | 66.7 kg, 1113 hp, 51.1 kW |

`--gen-engine`, seed 0:

| Request | Main | Branch |
| --- | --- | --- |
| 300 kW, tier 5 | Small V6 plain, supercharged, 300 kW, 385 kg | Small V4 performance, injected, 306 kW, 222 kg |
| 2000 kW, tier 4 | Small R32 plain, turbo, 2150 kW, 1531 kg (R24, W24 of bores refused) | Small W21 performance, supercharged, 2006 kW, 988 kg |
| 2000 kW, tier 5 | Small B16 racing (pressed wide), turbo, 1991 kW, 3857 kg (W18 refused) | the same W21, 988 kg |
| 900 kW, tier 5, W | W15 plain supercharged, 900 kW, 797 kg | W9 racing, supercharged, 931 kW, 486 kg |
| 900 kW, tier 5, radial | R16 plain turbo, 896 kW, 811 kg | R8 racing, turbo, 927 kW, 435 kg |
| 900 kW, tier 5, V | V16 plain turbo, 896 kW, 907 kg | the same (by score and seed) |
| 500 kW under 300 kg, tier 5 | V4 269 kW, 267 kg, "short of 500 kW" | V4 racing, supercharged, 497 kW, 277 kg |
| 20 MW, tier 5, Medium | W15 plain turbo, 20.2 MW, 15.7 t | B14 "Bored" (its best, x1.37), turbo, 19.8 MW, 13.2 t |
| 400 MW, tier 5, Large | B16 plain turbo, 430 MW, 366 t | B12 "Bored", turbo, 411 MW, 232 t |
| tiers 1 and 2 | (R8 320 kW 391 kg; Medium W21 20.2 MW 20.6 t) | the same |

`--speed-test` of two shipped vehicles' generators with the kit's engine (`--gen-vehicle NAME --seed 1 --engine-tier T --top-speed V`;
the committed files carry fixed engines and are unchanged):

| Vehicle | Main | Branch |
| --- | --- | --- |
| Bandit Car 1, tier 3, 33 m/s | 1944 kg, 480 kW plain, 19.5 m/s, grip | 1819 kg, 529 kW, 22.2 m/s, grip |
| Bandit Car 1, tier 5, 33 m/s | 1944 kg, 480 kW plain, 19.5 m/s, grip | 1694 kg, 596 kW, 24.8 m/s, grip |
| Bandit Box Truck 1, tier 3, 30 m/s | 2870 kg, 480 kW, 21.3 m/s, power against drag | 2745 kg, 529 kW, 22.2 m/s |
| Bandit Box Truck 1, tier 5, 30 m/s | 2870 kg, 480 kW, 21.3 m/s | 2726 kg, 611 kW, 22.2 m/s |

`--gen-vehicles`: 238, all identical to the committed files (no committed vehicle moves; nothing to regenerate).

## 4. The V, W and radial fault

Before: the kit's bored cylinder was wider than the crank (60 cm on a 45 cm crank), so two banks at right angles put their cylinders
in each other at the crank ("the Pressed Part (cylinder.cylinder pipe15 60x60x105 steel) at [-0.27, 0, 0] ... is in the way"), and
`Build` gave way to the next plan (R24, W24, W18 refused in the samples above). Now a bored cylinder keeps the plain one's across, so
nothing meets at the crank: the W and radial samples build bored at the first try, with no "better plans wouldn't build" note.

## 5. What a saved engine sees

Keys don't change and every key made before still parses; health is main's, so a saved health keeps its meaning. Worlds saved by the
branch base (`saveworld`), loaded here in a fresh process (`loadworld`), the same 41 or 43 parts by key, the same revs:
- the kit's old Small B16 Bored (16 `pipe10 50x50x85`, injectors pressed at 1.13): 962 kW on main, 981 kW here;
- an old Small B16 Performance (16 `pipe10 55x55x95`, injectors at 1.24): 1180 kW on main, 1222 kW here. Their cylinders give 12% and
  20% more, but the old pressed injectors were sized for main's power and now feed about 87% and 86% of it ("starved", by power:
  answer 6). The look line read "no fuel: link a fuel tank" (the blueprints carry no tank), which comes before the starved text;
- the old racing B16 (`pipe15 60x60x105`, thicker than plain): its cylinders give 51 kW, was 74 (first round's load: 1991 to 1482 kW
  under the first law).
Pressed pieces weigh a third to a half of what they did; recycling one gives back the new mass. `tools/MIGRATING.md` says so.

## 6. Checks run

`--gen-engine-parts --check` 12 files, 0 out of date; `--press-part presets` 0 problems; `--press-part sweep cylinder` 28 blanks and
`sweep engine` 60 blanks, 0 problems; `--check-construct-parts` exit 0, no REFUSED lines; `--gen-vehicles` 238 identical. A plan's
size estimate against the built engine, bored and boosted Small: V4 1.54 x 1.70 x 1.70 against 1.51 x 1.68 x 1.68 m; W21
4.29 x 3.00 x 1.95 against 4.26 x 3.06 x 1.98; R8 1.82 x 3.00 x 3.00 against 1.70 x 3.26 x 3.26 (a radial's height 8% under; vehicles
take no radial: nothing points down in a bay). Not run: self-test sections (batched by the coordinator), a joined client, an engine
of thin bores turning in a window, any picture.

## 7. Open questions (recommendation first)

1. **"Power follows bore and stroke."** Read here as torque from bore and stroke, power from bore: stroke still trades revs for
   torque at the same power, as answer 5 says (the coordinator puts this one to the user).
   - A (recommended): keep answer 5 as built.
   - B: power by displacement (bore squared times stroke), revs still falling with stroke.
2. **How thin a wall.** The bore counts up to 0.9 of the across, so a Small cylinder reaches about x2.8 (`pipe2`) at 6.9 kg, with a
   fifth of the plain health.
   - A (recommended): keep 0.9 and let health be the cost, as the user said ("easier to damage").
   - B: refuse walls under a share of the across at the die (new keys only; old ones still load).
3. **The Small preset is thick-walled** (bore 0.56 of its across, Medium and Large 0.78), so a Small cylinder bores to x2 and more, a
   Medium or Large to about x1.37: a tier 4 or 5 Medium engine is a "Bored" one at best.
   - A (recommended): leave it: past that, a bigger cylinder is the way to more power, the user's "function of cylinder size".
   - B: thicken the Medium and Large presets' walls (their catalogue parts change; every vehicle with one moves).
4. **Survival has no gate on bores.** A thinner wall is a Small Press job, so a new player's first engine can be a racing one.
   - A (recommended): leave it; the cost is toughness and fuel (thirst is by power).
   - B: a thin-walled cylinder needs a Medium Press or a better die.
5. **Pressed health against catalogue health.** A pressed piece's health is its blank's (412 hp at the Small Cylinder's size), the
   catalogue part's its file's (180), as on main; only mass is now anchored on the catalogue.
   - A (recommended): leave it as main has it (saves' health stays true).
   - B: anchor health on the catalogue too, with saved health rescaled on load (needs a world format step).

## 8. Merge review, round 1 (2026-11-14)

0. **The plain cylinder.** The first round read power through the presets' bores (so a plain-wall cylinder off a preset had other
   power than main's) and took mass and health from the catalogue (health 1113 to 307 for `pipe15 60x60x105`). Now power at the plain
   bore is main's exactly and health is main's for every key; mass is still anchored on the catalogue, the root cause in section 1,
   so a plain pressed cylinder weighs about a third of main's (table above), and recycling it gives back that.
1. **Saved health above maximum**: moot now health is main's; a run-time piece's saved health above its whole is held to it on load.
2. **MIGRATING** gives the old kit keys' true numbers and their fuel systems' feed (section 5).
3. **Size estimate**: pressed fuel systems and boosters keep their length (wider, no taller) and the estimate adds how much longer a
   pressed one is than the catalogue's; plan and build agree within 5% for V, W and boxer, 8% for a radial's height (section 6).
4. **Grade names** are what the cylinder reaches: a Medium or Large tier 5 bore is "Bored", and no "Racing" Medium exists.
5. **Recycling** gives back the piece's new mass: in MIGRATING.
