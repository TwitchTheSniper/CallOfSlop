# Sized gun and engine parts: phase 6 notes (2026-11-13)

Branch `claude/sized-parts-6-428d23`. Phase 6 of `sized-gun-engine-parts.md` ("Presets, bills, docs") and four leftovers from the phase 5
notes (questions 1, 3 and 5, and its untried items). **Protocol 306** (`Pressing.json` changes what a pressed part is on both peers; no
message changes; no part ids move).

## 1. Built

- **The held-back rows leave the Assembler** (`Assembling.PressedNow`): a Barrel or Engine die preset with at most one kind of part
  input. 41 rows: Gun Barrel, Long Gun Barrel, Flame Nozzle, Harpoon Tube, Howitzer Tube, Long Gun Tube, and 35 engine pieces (nine
  kinds in four sizes, less the Capital Smokestack, past the dies). Receivers are assemblies by the sort's rule (role `gun`): the
  Rifle, Heavy and Shell Receiver keep their Assembler recipes beside their presets. `PressParts.PresetParts(dies)` names a die's
  presets.
- **The 26 generated receivers are Receiver die patterns** (`PressParts.AllReceiverPatterns`), after the hand-made three: each preset
  its part's bounds on the 5 cm lattice (across the wider of x and y, length z), solid. Eleven share a blank with an earlier preset
  (a Bucket Launcher's 20x20x40 is a Rifle Receiver's): Auto makes the earlier one, the Pattern setting or an order this one, as
  the barrels already did. A label over 24 characters drops "receiver".
- **`--bill`**: of Bandit Gun Wagon 1 and Bandit Giant Truck 1, before and after (scratch files): the Bucket Launcher Receiver went
  from "no recipe" to pressed. The three left (Full-Auto Sear, Lob Trigger, Single Breech) are an action, a trigger and a breech,
  which the design leaves without a recipe (section 9: feeds, actions, muzzles... unchanged).
- **Presets weigh their cost** (question 5, answer A): `PressParts.Weighty` grows a preset blank (5 cm longer; a plate thicker; past the
  die's length or aspect, the section thicker) until its blank and bay 2, reckoned as `Pressed.CostMass` does, weigh at least the
  catalogue part. 30 presets moved (every Housing to 4 m, the Gear and Piston Sets, three differentials, three plates, the Leaf Spring, three tanks, two L Brackets and a Gusset; housings the most: a Steel Housing 0.5 m is
  `hollow 45x45x115`). A pattern whose preset was the old blank moves with it (`PressParts.Make`), so its scale law stays continuous
  at the catalogue part. The Barrel and Receiver dies are left alone (a Gun Barrel's 5 calibres and a Rifle Receiver's 20 cm are the
  knots of their curves and of `s`): the Barrel die gains a **steel liner** in bay 2 (`0.6 x blank mass` units, at least 1) and the
  Receiver die **one Gear Set per 10 kg** of blank (was 50 kg). `--press-part presets` now fails on any OVER line: 0.
- **Like bodies spread their faces** (question 1, answer A): `Pressed.SpreadLike`, in `BuildLike` before the features. A like body's
  end boxes (those reaching either end along its length) step in from the side bounds by the gap, so a pressed piece the size of its
  neighbour never lays a face on the neighbour's; then any drawn box whose face came within 2 mm of an earlier one's (scaling shrinks a
  like part's own gaps; a one-axis stretch can close them) moves to the nearest clear plane, inward while thick enough. All like bodies,
  not only engine pieces: the generated Kingpin Revolver Receiver's like body had two boxes at 0 mm (the sweep found it once the
  receivers became patterns), and a 10 cm pressed piston had 0.4 mm.
- **The Engine Kit plans bores** (question 3, answer A): `EngineKitParts.Grade.Bore` (1.13, 1.24, 1.38); `EngineKit.Bores` gives the
  plain bore and each allowed grade's: the plain cylinder's preset pressed at the ratio (`pipe10 50x50x85`, `pipe10 55x55x95`, `pipe15
  60x60x105` for Small), its fuel system and booster pressed to the cylinder's scale rounded up (so never starved), and crank sections
  pressed longer when the bore is wider than the section (the same across, so the same revs). The plan's kW reads `EngineRules.At` of
  each cylinder's scale (a file piece's through `ScaleOf`: phase 4 left `PieceScale` 0 on files). `EnginePlan.Crank` names the crank.
  A turbo is told by its spool, not its name.
- Docs: drivetrain-and-engines.md (the sums by scale and stroke, sized pieces, the starved text, decisions 8 and 9), ENGINEKIT.md
  (bores in place of the tier cylinders, the samples run again), WEAPON-BUILDER.md (a pointer), MODS.md (pressed keys in mod
  blueprints; patches to preset parts), MIGRATING.md (the 41 rows; new blanks and kits).

## 2. Tried by hand

Throwaway runs with `--save-dir` to a scratch folder; test blueprints copied into `bin/` and removed after.
- **An engine of pressed pieces turning, in a window**: a pressed crank `solid 45x45x65`, a pressed cylinder `pipe15 60x60x105`, a pressed
  injector `solid 55x55x35`, a Small Flywheel, a Fuel Tank, a Switch wired to the flywheel and a transmission, shaft, power take-off
  and turntable (the self-test's rig): `engines` read `I1 89 kW, frugal; scale 1, stroke 1.05, peak 4762, limiter 6667; fault None;
  running True`, rpm 0, then 2889, then 6333 four seconds apart. `--check-vehicle`: 0 coplanar faces between parts.
- **A Large pressed mix and its starter**, in a window: a pressed crank `solid 720x720x850`, a pressed cylinder `pipe80 720x720x1250`, a
  catalogue Medium Carburettor, a Large Flywheel, a pressed starter `round 480x480x485`, a Large Fuel Bunker and Battery Bank, a
  Switch through an adapter: "the engine started"; `I1 800 kW - bank 1 starved: the Medium Carburettor feeds 5% of its cylinders;
  scale 16 (large), stroke 1.04, peak 1346 rpm`. No coplanar faces. (No driveline: it turned no load.)
- **`--speed-test` before and after** (the branch base `9b5a1de2` built in a scratch worktree): Bandit Car 1 and Bandit Box Truck 1 carry
  the fixed V8 Engine part, so they are identical (24.6 and 19.3 m/s). A kit vehicle, `--gen-vehicle BanditCar --seed 1
  --engine-tier 4 --top-speed 33`: before, ten Small Performance Cylinders, 640 kW, 2074 kg, 18.8 m/s; after, ten plain Small
  Cylinders (bored ones are heavier than the car's budget), 480 kW, 1944 kg, 19.5 m/s; both limited by grip. At tier 5 the car, the
  box truck and the giant truck all picked plain pieces too.
- `--gen-engine`: tier 1 and 2 samples unchanged (300 kW tier 1 R8 320 kW 391 kg; 20 MW tier 2 Medium W21); 2000 kW tier 5 builds a
  Small B16 of racing bores, 1991 kW planned and built.
- Checks: `--gen-parts all --check` clean; `--gen-engine-parts --check` 12 files, 0 out of date; `--gen-vehicles` 238 identical;
  `--check-construct-parts` exit 0, no REFUSED lines; `--press-part presets` 0 problems; `--press-part sweep` over barrel, receiver,
  housing, gear, piston, drive, plate, spring, tank, bracket, shaft, cable, control, actuator, engine, cylinder: 0 problems.
- Self-test sections by name: Modular engine, Modular engine network, Press stock, Assembling, Assembling network, Construct salvage,
  Sandbox parts menu, Sandbox inventory, Model connectivity, Mods; and Sandbox weapon parts, Construct merged receivers, Construct
  routed links, Sandbox Tab inventory, Extruder: 0 failures.

## 3. Not tried

- A terminal order for a Gun Barrel reaching a press (read in code: `AssemblingTable` and `Crafting` give a catalogue part with no
  recipe its preset's spec; not run).
- A joined client reading the pressed engines (phase 5 tried it for the same code path; `SpreadLike` runs on both peers as the rest
  of `BuildLike` does).
- The kit's V, W and radial plans of wide bores are refused at build (the banks meet at the crank) and give way to the next plan; a
  request that only such a plan could meet falls back further than it should. Not measured how long a refused build takes.
- No picture judged (none asked).

## 4. Open questions (recommendation first)

1. **Bored engines are heavy.** A pressed piece weighs its blank, so a racing bore bank weighs about twice a plain one of the same
   power (2000 kW: 3857 kg bored against 1531 kg plain at tier 4), and the vehicle kit never picks one. The old tier cylinders were
   lighter than plain for their power.
   - A (recommended): leave it; bore is the way to more power per cylinder, not per kilogram, and tiers 3 to 5 keep their boosters.
   - B: let a cylinder pattern weigh less than its blank (a bored-out hollow), so bores are light again.
2. **Presets that grew a long way** (a Housing 0.25 m is now `hollow 25x25x95`; the Gear Set `round 20x20x50`). The catalogue part's
   mass sets it.
   - A (recommended): keep it, as the data says what the part weighs.
   - B: lower those catalogue parts' masses to their old blanks' (a vehicle with housings gets lighter).
