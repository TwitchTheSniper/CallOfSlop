# Sized gun and engine parts: phase 2 notes (2026-11-13)

Phase 2 of `sized-gun-engine-parts.md` ("Barrels on guns"), built on branch `claude/sized-parts-2-80b7c6`, plus the phase 1
question the user answered (question 1, A). No protocol bump: no wire format, no part file and no id moved (the new presets
are rows of `Pressing.json` naming catalogue parts that already exist).

## What was built

- **A mounted bullet gun's reach follows its barrels** (phase 1 question 1, answer A). `BuildStats.ReceiverResult.BarrelReach`:
  each barrel path's range after its barrel and muzzle device over the range before the barrel stage, averaged over the paths.
  `ConstructWeapons.BuildReceiver` sets `g.MaxRange = gun.maxRange x BarrelReach`, for catalogue and pressed barrels alike.
  Flame, harpoon and shell guns set their own reach afterwards, as before.
- **The bore band** (design section 5). The Barrel die states `"fit": [0.7, 1.5]` (`PressDie.FitLo/FitHi`). A sized builder part
  carries its bore class `SizeBore` (its across over its pattern's preset's) and the band. `SizedGunParts.FitWhy` refuses a pressed barrel
  outside the band of the receiver's bore. The receiver's bore is its merge size N, or 1 for a plain receiver; a pressed receiver's scale
  joins it in phase 3. It is checked for a barrel on the receiver and for each barrel on a splitter. The text is the design's:
  `the barrel is 0.10 m across: this receiver takes 0.14 to 0.30 m`.
- **Pressed gun parts are never rescaled on a merge** (design 4.3). `ConstructMerge.DeriveScales` gives a part with `GunSized` the scale 1,
  and 1 now also marks a part as found. Catalogue parts hung on it, such as a muzzle device, still take N.
- **Every other barrel is a pattern** (design 3.3), 18 patterns with one preset each: big iron, bloat, blunderbuss bell, cannon sleeve,
  coil, flame nozzle, flechette sleeve, fluted match, harpoon tube, heatsink, heavy bore, launcher tube, marksman, needle, ported, sawn-off,
  scattermouth, squeeze bore. Each takes its own builder part's effects, plus the plain curves read at `5 x c / c_preset` (a scale row's new
  `"at": 5`). The preset blank follows design 3.3's rule (across 10 cm x its `sizedFor` bore's `barrelScale` width, length 250 cm per
  metre of muzzle, both to 5 cm). The artillery `tube` pattern has its own curves through the Howitzer Tube (`round 20x20x105`) and the Long
  Gun Tube (`20x20x165`). The Barrel of Barrels stays a catalogue part.
- **Window tool action `guns`**: each gun of the spawned body as this peer derives it (problem, reach, spread, damage, rate). On a
  client, which spawns nothing, it reads the body with the most guns.

## Shipped guns whose reach changed

Every receiver gun with a barrel or muzzle device that changes range. In the vehicle armoury (`--check-armoury`, before and after), 24
of 41 recipes changed; the other 17 (flame, harpoon and shell guns, and plain Gun Barrels) are unchanged:

| Reach now | Recipes (was 300 m) |
| --- | --- |
| 600 m (Coil Barrel x2) | coil-lance, spark-carbine, spark-block |
| 507 m (x1.69: heavy-bore guns; the parts behind it not traced) | chaingun-block, twin-chaingun, belt-chaingun, elephant-gun |
| 450 m | marksman-cannon |
| 420 m (Long Gun Barrel) | service-cannon, forked-tongue, squad-lmg |
| 240 m | auto-shotgun |
| 224 m | line-dance |
| 210 m (Stub Barrel) | mismatch-cannon, nine-rifle-cannon, totem-flak, hydra |
| 197 m | fire-escape, hammerfall |
| 180 m | dragon-sweeper |

The shipped vehicles carry these recipes. A hand-built gun changes the same way: a Long Gun Barrel reaches 420 m, a Stub 210 m.

## What was tried, and seen

- `--press-part presets`: 0 problems. All 23 barrel presets come off a Small Press, and each resolves to its catalogue part. Two share a
  blank with another pattern and need the Pattern setting: Sawn-Off (`10x10x20`, Auto makes the Stub Barrel) and Squeeze Bore (`10x10x75`,
  Auto makes the Cannon Sleeve).
- `--press-part`: needle `10x10x160` (16 calibres, preset 11) gives range x1.23 and spread x0.915. Coil `10x10x160` gives range x1, held
  there because the Coil is already at the catalogue's x2; spread x0.812. Tube `20x20x135` gives range x1.25 and spread x0.9.
- `--check-vehicle` on copies of the Bandit Flatbed Fort 1, which has a 2x2 merged Courier Carbine Receiver:
  - Gun Barrel: 6 shots a second, range 300 m, spread 0.704 deg.
  - Pressed `round 20x20x100` and `round 30x30x150` (bore 2 and 3 on bore 2): exactly the same numbers.
  - Pressed `pipe2 10x10x50` and `round 10x10x70`: refused, with `the barrel is 0.10 m across: this receiver takes 0.14 to 0.30 m`.
  - `round 35x35x175`: refused, with `0.35 m ... 0.14 to 0.30 m`.
  - Long Gun Barrel: range 420 m, spread 0.598.
- Splitter: the Highland Turret 1 (a Heavy Receiver and a Cross Splitter) with one pressed needle `10x10x160` and three Gun Barrels
  fires 8 shots a second, range 319.8 m.
- **Host and client in two processes** (`--window-shot --net host` and `--net join:127.0.0.1`), with the Fort's 2x2 gun on a pressed
  `round 20x20x140` barrel: both print `Courier Carbine Receiver: fires, reach 360 m, spread 0.6512 deg, damage 92, every 10 ticks`.
  Phase 1's open question is closed.
- **The Barrel Die in survival**: the Die Lathe has an Assembler recipe (steel 62, rubber 12). Its lathe rows include the Small, Medium
  and Large Barrel Die (Small: a solid steel block 0.25 to 0.45 m across). The dies are in the Production > Dies section, and no faction
  technology locks a die. Like every die except Button, Bracket, Gear and Beam, it isn't in the start kit. So a survival player can make
  one, and no data was missing.
- `--gen-parts all --check`, `--check-construct-parts`: clean. Self-test sections run by name: "Construct merged receivers", "Powder
  charge", "Sandbox weapon parts", "Press stock", "Construct gun recoil", "Sandbox gun bench": 0 failures.

## Not verified

- A barrel refused on a splitter in a real build: on a plain receiver only a barrel that is too thick is out of the band, and in a
  Cross Splitter a thick one collides with its neighbours before the gun is built. The code path is the same `FitWhy`.
- A catalogue muzzle device on a pressed barrel on a merge (drawn N times bigger on an unscaled barrel): not built, no picture.
- Pressing a barrel on a real press in a world and fitting it by hand. The Production Showcase run wasn't done; the numbers came from
  `--check-vehicle` and the window tool.
- Flame and harpoon reach by length: their pressed patterns move `rangeM` by the curve, which sets their reach. Design 3.3's
  `flameReachPerStep` reading was not built (choice 3).

## Choices made

1. **"The bare receiver" is everything but the barrel paths.** The reach ratio is the barrel and muzzle device stage only. The powder
   charge and the round's speed are left out, because `Finish` already applies them to reach, and counting them here would count them
   twice. So muzzle devices move reach too: a Suppressor x0.88, a Loudener x1.1.
2. **A pattern barrel's curve is held to the catalogue's widest product.** For a pattern, each curve's clamp is the widest catalogue value
   over the pattern's own change. So the Coil Barrel (x2 range) gains no reach by length, but it still loses reach when short. Design
   section 3.2 keeps every sized barrel inside the range the builder is balanced for, and this keeps that for patterns too.
3. **Flame and harpoon reach follow `rangeM` only.** A longer Flame Nozzle or Harpoon Tube reaches further through the curve. Nothing
   reads a separate per-step rule.
4. **No protocol bump.** The new presets are data rows naming existing parts, and no ids move.

## Questions for the user (each with a recommendation; they can be asked together)

1. **Muzzle devices now change a mounted gun's reach** (a Suppressor cuts it to 264 m, a Loudener raises it to 330 m), as they already did
   a hand-held gun's range.
   - A (recommended): keep it. The build's range is one number, and the barrel and its device are one path.
   - B: only the barrel counts. Muzzle devices keep their spread and sound effects, but their range change is ignored on a mounted gun.
2. **Shared preset blanks.** Sawn-Off and Stub share `round 10x10x20`; Squeeze Bore and Cannon Sleeve share `round 10x10x75`. With the
   press on Auto, the first pattern wins, so a terminal order for a Sawn-Off needs the Pattern setting.
   - A (recommended): leave it. The orders path (phase 6) names the pattern in the key anyway.
   - B: move one preset of each pair by 5 cm (Sawn-Off `10x10x15`, Squeeze Bore `10x10x80`), so every barrel has its own blank.
3. **Four heavy-bore armoury guns now reach 507 m, and the Coil guns reach 600 m**, against 300 m before.
   - A (recommended): keep it. It follows the builder's own numbers.
   - B: cap a receiver gun's reach (for example at 1.5 x `maxRange`).
