# Sized gun and engine parts: phase 5 notes (2026-11-13)

Branch `claude/sized-parts-5-507f3c`. Phase 5 of `sized-gun-engine-parts.md` ("Engine pieces off the press"), phase 4's leftover (stroke) and two
findings from verification. **Protocol 304** (four Engine Die part files move ids; `Pressing.json` changed; nothing new on the wire).

## 1. The two findings, each tried before the fix

1. **The Engine Kit read the old per-size starter table.** `EngineKit.cs` (lines 175, 176, 398; the brief said `Source/VehicleKit`, it is
   `Source/EngineKit`) read `EngineRules.NeedsStarter[size]` while the engine itself asks `starterFrom` by scale since phase 4. Tried with
   `starterFrom` 3 in the data copy: `--gen-engine --power 2000 --size medium` planned a Medium I3 with no starter and the game said
   `NoStarter`. Now `EngineRules.StarterAt(scale)` and `StarterFor(size)` are the only readers; the table is gone. The generator no longer
   writes a per-size `"starter"`; a `/1` file's flags still load and become its `starterFrom` (half-way in log below the first flagged
   size). Same run after: the kit fits a starter, no fault.
2. **A small pressed receiver weighed more than it cost.** The merge's mass (file mass x s^2) passes the blank plus its Gear Sets for small
   and hollow blanks: `--press-part sweep receiver` with a new check found 7 of 27 (`receiver.rifle solid 15x15x30` 7.88 kg against
   3.4 kg of blank). Recycling yields by mass (`RecyclingTable.YieldOf`), so that is where it is fixed: `Pressed.ReceiverMass` is the
   merge's mass held to `CostMass` (blank + bay 2), one helper for both build paths. `--press-part` now reports any pressed piece heavier
   than its cost as a problem (every die). Sweep after: 0.

## 2. Built

- **`"likeBy": "nearest"`** (`PressPattern.LikeNearest`, `Knots`, `ByKnot`, `For(k)`): a pattern's presets are its knots; a blank takes the
  nearest one's part (log of across) as its like body. The per-knot patterns are made at load, so a press allocates nothing.
- **Engine numbers from the key** (`Pressed.EngineNumbers`, `EngineScale`): `PieceScale` runs through the knots (across against their
  class's scale) in log-log, the end segment past the ends, three figures; `PieceSize` the nearest class. A cylinder's new
  `PieceStroke` = (length/across) over its nearest preset's, 0 (read 1) at a preset's proportions.
- **Stroke** (phase 4's leftover): `ModEngine.Stroke`, the cylinders' stroke weighted by kW (exactly 1 when every one is 1);
  `ModEngine.Rpm` = the crank scale's rpm over it, used by `Apply` (and so torque) and the Engine Kit's read-back.
- **Cylinder die**: like body along y, likeBy nearest, presets on the files' own bounds (Small `pipe10 45x45x75`, Medium
  `pipe20 180x180x300`, Large `pipe80 720x720x1200`), aspect 0.6 to 1.6 of the presets' (1 to 2.67), across from 30 cm, bay 2 Piston
  Sets `1 + 20 x volume` (at most 20) in place of rubber, no die features (the model is the file's).
- **Engine die** (new; Small to Capital die parts and lathe rows): crank, flywheel, carburettor, injector, turbo, supercharger, starter,
  exhaust, stack; like bodies, likeBy nearest, presets each size's file bounds held to its class's press; bay 2 as design 6.2 (Bearings,
  a Circuit Board, a Copper Coil); across 20 cm to 28.8 m. A size past the dies (Capital Cylinder, Capital Smokestack) has no preset.
- **`--press-part`** prints an engine piece's scale, nearest class, like part, kW, stroke ratio, revs and torque (a crank: its revs and
  whether it needs a starter).
- **Assembler**: Small, Medium and Large Cylinder leave it for the Cylinder die (Capital stays: no die presses it). The 12 tier cylinders
  lose their recipes and leave the build catalogue through a new part-file flag `"retired"` (`ConstructPartDef.Retired`: loads and runs,
  not `Offered`, no recipe); `legacy` would have failed the routed-links section's "no legacy parts in shipped blueprints".
- Window tool: `engines` prints each modular engine as this peer derives it.

## 3. Tried by hand

Throwaway runs (`--save-dir` to a temp folder; the test blueprint copied into `bin/` and removed after).
- `--press-part`: `pipe10 45x45x75` is the Small Cylinder (40 kW); `60x60x100` 74.1 kW at scale 1.33, peak 4438 rpm; `100x100x165` 217.6
  kW (like Medium); `60x60x95` stroke 0.95, peak 4671 (shorter stroke, more revs); `60x60x45` refused with the aspect range.
- An engine of two pressed cranks (`solid 55x55x65`), two pressed `pipe10 60x60x100` cylinders, a catalogue Small Carburettor, Small
  Flywheel and Fuel Tank, on a host (`--net host:28533`) and a **joined client**: both read `I2 80 kW - bank 1 starved: the Small
  Carburettor feeds 54% of its cylinders; scale 1.22, peak 4601 rpm, 166 N m; fault None`.
- Sweeps: Cylinder 28 blanks, Engine 60, Receiver 27: 0 problems; `--press-part presets` 0 problems; `--check-construct-parts` exit 0,
  no REFUSED lines; `--gen-parts all --check` and `--gen-engine-parts --check` clean; `--gen-vehicles`: 238, all identical to the
  committed files (no committed vehicle moves).
- Self-test sections by name: Modular engine, Press stock, Assembling, Construct salvage, Sandbox parts menu, Sandbox inventory, Sandbox
  Tab inventory, Sandbox weapon parts, Construct merged receivers, Construct routed links, Extruder: 0 failures.

## 4. Not tried

- The engine turning in a window (it needs a seat and drive line; only its derived numbers were read on both peers). No picture judged.
- `--bill` of a vehicle holding a tier cylinder; `--speed-test` on shipped vehicles (their cylinders are file pieces: stroke 1).
- A Large-scale pressed mix and its starter; a pressed turbo's boost text.

## 5. Open questions (recommendation first)

1. **Size coincidences between pieces.** A 50 cm pressed crank's end flange lands exactly on a Small Flywheel's 50 cm face (z-fighting in
   `--check-vehicle`). Any stretched like body can meet a neighbour's size.
   - A (recommended): nudge a like body's end boxes in by the gap in `BuildLike` when the part is an engine piece (a later small change).
   - B: leave it; a player picks another size.
2. **Wide bores need a longer crank.** Cylinders wider than the crank section collide with their neighbours, so a bored engine needs
   longer crank sections (the design's "length = pitch"). A (recommended): keep it, and say so in the look line when the build refuses;
   B: let cylinders overhang.
3. **The Engine Kit still plans tier cylinders** (phase 1 question 3), which survival can no longer make.
   - A (recommended): next phase, the kit plans a plain cylinder bored 1.13 / 1.24 / 1.38 times its crank in their place.
   - B: leave the kit as it is.
4. **The engine pieces other than cylinders** keep their Assembler recipes beside their presets until phase 6. A (recommended): as the
   design says; B: move them now.

## 6. Merge review (2026-11-13)

1. **Old keys load.** The Cylinder die keeps main's band (across 20 to 2880 cm, 1 to 16 times as long as across), so every key made before
   still parses; the stroke ratio is held to 0.6 to 1.6 instead (`Pressed.MinStroke`, `MaxStroke`). Tried: a world saved on main (built
   from `origin/main`) with a Small Crank, `pipe5 30x30x90` and `pipe10 60x60x100` pressed cylinders, a carburettor, flywheel and tank,
   loaded on this branch: both parts are there; their bodies are now like bodies (the Small Cylinder stretched), so their model, sockets,
   mass and bay 2 differ, and the engine re-derives: main read each at Small's 40 kW; here `B2 17 kW` (the 30 cm one 16.7 kW, stroke 1.6,
   peak 3125 rpm; the 60 cm one's bank has no fuel system). A made part in a player's inventory that no longer builds now logs a line
   (`GameWorldSave`); construct parts already did ("left out N x NAME").
2. **Assembler rows**: `tools/MIGRATING.md` says which 15 rows went and what a pressed cylinder saved before does on load.
3. **Presets heavier than their cost.** The engine dies' presets are lengthened by 5 cm steps until the blank alone weighs at least the
   file part (Small Flywheel `round 45x45x45`, Small Turbo `hollow 40x40x40`, Supercharger and Starter likewise); `--press-part presets`
   holds them as problems (0). It also lists 35 **older** presets (housings, plates, the Gear Set, Piston Set, differentials, tanks,
   barrels, the Heavy Receiver) as OVER lines without failing.

## Question 5 (from the review)

5. **The 35 older presets that come out heavier than their blank and bay 2** (a Gear Set 6 kg from 1.33 kg of steel).
   - A (recommended): a press phase lengthens or thickens their preset blanks the same way (their keys move, so presses set to Auto make
     the new blank's preset; a save keeps the parts it holds).
   - B: recycling a catalogue part pays by its Assembler recipe's cost rather than its mass.
