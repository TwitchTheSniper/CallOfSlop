# Sized gun and engine parts (design)

Design only (2026-11-12; revised 2026-11-13 for the user's answers in section 14, chiefly 6 and 8). No code changed,
nothing built, nothing run. Written for an agent to build in six phases of 60
to 90 minutes. It follows `press-rework.md` (sections are cited as "rework 4.2") and the user's answer 4 in its section
12: gun parts and engine pieces are **sized from the start**; a longer barrel shoots further, a bigger cylinder gives more
power. The Barrel, Receiver and Engine dies wait for this.

Sources read: `press-rework.md`, `press-phase3-notes.md`, `Source/Constructs/PressDies.cs`, `Pressed.cs`,
`ConstructWeapons.cs` (`BuildReceiver`), `ConstructMerge.cs` (`ScaleGun`, `PartScale`), `ConstructEngine.cs`,
`Source/Zombies/BuildStats.cs`, `PartDef.cs`, `PartFile.cs`, `PowderCharge.cs`, `Parts/_Registry.json`, every file in
`Parts/Barrel` and `Parts/Receiver`, `Constructs/ReceiverMerge.json`, `ModularEngine.json`, `WeaponParts.json`,
`docs/sandbox/constructs/weapons.md`, `drivetrain-and-engines.md` ("Modular engines"), `docs/vehiclekit/ENGINEKIT.md`.
Not verified: the whole bounds of the engine part files (preset blanks below are examples), and that a client runs
`BuildReceiver` for its own HUD (assumed, as merges are "derived on every peer"); phase 1 checks both.

## 1. What exists today

| Thing | How it works now | Files |
| --- | --- | --- |
| Builder part | One JSON file per part (`call-of-slop-part/1`): category, weight, sockets, boxes, `muzzles`, and `stats` (relative effects: `mul`, `add`, `set`, `grow`). 24 barrels, 29 receivers. `Parts.Find(name)` is a scan of `Parts.All` | `Parts/<Category>`, `Source/Zombies/PartDef.cs`, `PartFile.cs` |
| A gun's numbers | `BuildStats.ReceiverStats`: the receiver's bore gives the calibre (`_Registry.json`), then receiver, ammo, feed, action, upgrades; then each barrel path on its own copy (`ApplyBarrel`, scaled by the bore's `barrelStrength`). The result is a `WeaponDef` | `BuildStats.cs`, `BuildRules.cs` |
| Barrel length | Already read in one place: `PowderCharge.BarrelLength` is the barrel's muzzle Z (Standard 0.20 m, Stub 0.08, Long 0.36) and sets how much powder a barrel burns | `PowderCharge.cs` |
| Builder parts in Sandbox | Every builder part has a construct def: 16 hand-made (`Gun Barrel` wraps `Standard Barrel`, `Rifle Receiver` wraps `Workhorse Receiver`...), 327 generated at load at twice the handheld size. A gun part's def names its builder part (`GunPartName`); a receiver gun names `GunReceiver`, `GunAction`, `GunKick` | `ConstructWeaponParts.cs`, `Constructs/WeaponParts.json`, `Constructs/Parts/Weapons` |
| A construct gun | `ConstructWeapons.BuildReceiver`, at a construct's rebuild (never per tick): finds the gun parts bolted to the receiver, makes a `BuildNode` tree from `Parts.Find(d.GunPartName)`, calls `ReceiverStats`, fills the `CGun`. A problem is a text on the look line (`g.Problem`) | `ConstructWeapons.cs` |
| Merged receivers | An NxN square of one receiver type is one gun: damage N^2, rate 1/N, kick N^2, rounds a shot N^2, magazine N^2, spread and range unchanged (`ReceiverMerge.json` `powers`, `ConstructMerge.ScaleGun(ref g, w, int n)`). Gun parts on its centre sockets are drawn N times bigger (`PartScale`, a byte per part). Derived, never on the wire | `ConstructMerge.cs` |
| Modular engine | Pieces that touch are one engine, found at the rebuild (`ConstructEngine.Build`). A piece has a kind (`EnginePiece`) and a **size class** (`PieceSize` 0 to 3). Power: sum over cylinders of `rules.Kw[e.Size] x PiecePower x share x fuel x boost`; idle, peak and limiter rpm are `rules.Idle/Peak/Max[e.Size]`; a piece of another class is `EngineFault.MixedSizes`, and `SnapBuild` refuses to fit it | `ConstructEngine.cs`, `ConstructDriveParts.cs` (`ParseEnginePiece`), `SnapBuild.cs` |
| Engine data | `ModularEngine.json`: four `sizes` (scale 1, 4, 16, 64; 40, 800, 16,000, 320,000 kW; peak 5000, 2800, 1400, 700 rpm; a starter from Large). 40 piece files plus 12 tier cylinders (`power` 1.3, 1.6, 2) | part kit `--gen-parts engine_parts`, `Source/EngineKit/EngineKitParts.cs` |
| Engine Kit | Plans engines from the catalogue pieces by class and reads the numbers back from the live `ModEngine` | `Source/EngineKit/EngineKit.cs` |
| Pressed part | `Pressed Part (DIE.PATTERN PROFILE WxHxL MATERIAL)`, a named run-time def made once from its key; numbers by `Pressed.Scaled` (a power law from the pattern's one preset, three significant figures); a preset key **is** the catalogue part. `"body": "like"` stretches the like part's boxes and sockets to the blank (`Pressed.BuildLike`) | `Pressed.cs`, `PressDies.cs`, `PartKit/Generators/PressParts.cs` |

The gap: `Pressed.ScaledFields` knows seven construct fields. A gun's numbers live in a **builder** `PartDef` found by
name, and an engine's in a table indexed by class. Neither can see a blank's size.

## 2. The approach in four rules

1. **A sized gun part is a sized builder part.** A pressed barrel or receiver owns a run-time `PartDef` made once with it:
   a copy of its pattern's builder part with the effects, muzzle and weight worked out from the blank. `Parts.Find` learns
   the `Pressed Part (` prefix, and the pressed construct def's `GunPartName` (or `GunReceiver`) is its own key. So
   `BuildReceiver`, `BuildRules`, `BuildStats`, `ConstructAmmo` and `ConstructLoot` keep calling `Parts.Find(...)` and
   need no change at their call sites.
2. **An engine piece has a scale, not a class.** `ConstructPartDef.PieceScale` (a float: 1, 4, 16, 64 for the files, from
   the blank for a pressed piece) replaces `PieceSize` in the sums. `EngineRules.At(scale)` reads the four `sizes` rows as
   knots.
3. **Curves run through today's parts.** A number that depends on size is a curve whose knots are catalogue parts
   (Stub, Standard and Long Barrel; the four engine sizes; a 1x1, 2x2 ... 6x6 merge). At a knot the curve gives the
   file's number, and a preset is the file part anyway (rework 4.4), so nothing shipped changes.
4. **A barrel fits its receiver within a band; an engine takes anything.** A barrel must suit its receiver's bore within
   a stated band; outside it the gun says why, in the words rework section 6 uses for the Assembler. An engine refuses
   no size (answer 6): each cylinder gives its own bore's power, and a piece too small for its work costs power, which
   the engine's look line names. Neither ever clamps silently.

## 3. Barrels

### 3.1 What the die offers

Barrel die. Blank: a `round` or `pipeN` in steel. Two dimensions the player chooses:

| Dimension | From the key | Range | Step | What it does |
| --- | --- | --- | --- | --- |
| **Across** `d` | W (= H) | 10 to 120 cm | 5 cm | the bore class: which receivers it fits (section 5) |
| **Length**, read in **calibres** `c = L / d` | L | 1 to 16 calibres | L in 10 cm steps (5 cm below 1 m) | range, spread, handling, powder burn |

Calibres, not metres, so a barrel twice as thick and twice as long is the same barrel on a gun twice the size: that is
what a 2x2 merged receiver does to a Gun Barrel today. The pattern (a setting on the press, user answer 1) picks the
barrel's character. Bay 2: none; a scope pattern takes 1 Lens (rework 3.2). The die exists in Small, Medium and Large
(up to 1.2 m across is a Medium press's; length to 19.2 m).

### 3.2 The length curve

The plain pattern has three presets, placed so calibres / 5 equals the builder file's muzzle length / 0.20 m:

| Preset | Blank | Calibres | Builder part | rangeM | hipSpreadDeg | adsTime | mobility |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Stub | `round 10x10x20 steel` | 2 | Stub Barrel | x0.70 | x1.15 | -0.02 | +0.02 |
| Standard | `round 10x10x50 steel` | 5 | Standard Barrel (`Gun Barrel`) | x1 | x1 | 0 | 0 |
| Long | `round 10x10x90 steel` | 9 | Long Barrel (`Long Gun Barrel`) | x1.40 | x0.85 | +0.03 | -0.02 |

Between knots the value is a straight line in calibres; past the ends it follows the end segment, then stops at the
clamp. The generator writes the points from the three part files, so tuning Long Barrel moves the curve.

| Stat | Value at `c` calibres | Clamp | Example: 7, 12, 16 calibres |
| --- | --- | --- | --- |
| `rangeM` (mul) | 2: 0.70, 5: 1.00, 9: 1.40 | 0.50 to 2.00 | 1.20, 1.70, 2.00 |
| `hipSpreadDeg` (mul) | 2: 1.15, 5: 1.00, 9: 0.85 | 0.55 to 1.80 | 0.925, 0.7375, 0.5875 |
| `adsTime` (add, s) | 2: -0.02, 5: 0, 9: +0.03 | -0.04 to +0.06 | +0.015, +0.0525, +0.06 |
| `mobility` (add) | 2: +0.02, 5: 0, 9: -0.02 | -0.04 to +0.04 | -0.01, -0.035, -0.04 |
| muzzle length (m, for `PowderCharge`) | `0.04 x c` | 0.04 to 0.64 | 0.28, 0.48, 0.64 |
| builder `Weight` (kg, handheld only) | the pressed part's mass / `massScale` | 0.05 up | |

The clamps are the widest values any catalogue barrel has (Sawn-Off, Coil, Marksman, Cannon Sleeve), so no sized barrel
leaves the range the builder is balanced for. The powder charge needs nothing new: a longer muzzle length raises
`PowderCharge.BurnLimit` by today's formula. A construct's mass is the blank's (rework 4.2 step 6).

### 3.3 The other barrels are patterns

Every other catalogue barrel is a **pattern with one preset**: its own effects (Needle: size x0.5, speed x1.7 ...) times
the plain curve read at `5 x c / c_preset`, so its preset comes out with its file's numbers and a longer Needle Barrel
gains range as a plain one does. A stat the pattern sets and the curve also moves is multiplied (mul) or added (add).

| Group | Patterns (builder parts) | Preset blank rule |
| --- | --- | --- |
| Plain | plain (Stub, Standard, Long) | section 3.2 |
| Bore | needle, squeeze, heavy bore, bloat, cannon sleeve, flechette | across = 10 cm x its `sizedFor` bore's `barrelScale` width, to the lattice; length = 250 cm per metre of its muzzle length |
| Match | fluted match, marksman, big iron, ported, heatsink, sawn-off, coil | same |
| Shell and special | scattermouth, blunderbuss bell, launcher tube, flame nozzle, harpoon tube | same; flame and harpoon reach scale with `c` by `flameReachPerStep` and `harpoonReachPerStep` as a merge step does today |
| Artillery | tube: **two knots**, Howitzer Tube and Long Gun Tube (range x1.5, spread x0.8) | its own curve, same form |
| Not sized | Barrel of Barrels (a bundle: an assembly, stays a catalogue part) | none |

### 3.4 The sized builder part

New file `Source/Constructs/SizedGunParts.cs` (static class `SizedGunParts`: `Make`, `FitWhy`, `Line`). `Pressed.Make`
calls `SizedGunParts.Make(pattern, blank)` when the like part is a gun part or a receiver gun and stores the result in
a new field `ConstructPartDef.GunSized`:

1. `var p = builderPart.ShallowCopy()` with `Name` = the pressed key, `SourceFile` = null.
2. `Effects`: the pattern's own, combined with the curve's values as new `AttachmentEffect` rows (same field, op, min
   and max as the file's), each value rounded to three significant figures.
3. `Muzzles` and the `muzzle` socket: Z = `-0.04 x c` (per muzzle, scaled in X and Y for a bundle); `Hand` scaled with it.
4. `Boxes`: the builder part's boxes stretched by (d ratio, d ratio, L ratio) against the preset, for a handheld model
   later (section 9); `Weight` as the table.
5. New fields `PartDef.SizeAcrossCm`, `SizeLengthCm` (0 on every file part), which the fit check reads.

`Parts.Find(name)`: `name.StartsWith(Pressed.Prefix)` first, then `ConstructParts.Find(name)?.GunSized`; else today's
scan. The pressed construct def's `GunPartName` is its key, so `ConstructWeaponParts.NameOf` and every caller agree.

## 4. Receivers

### 4.1 What the die offers

Receiver die. Blank: `solid` or `hollow`, steel. **One** dimension: across. The length must be the preset's own
proportion (a Rifle Receiver is `solid 20x20x40`, so its blanks are 15x15x30, 25x25x50 ...; anything else is refused:
`a Rifle Receiver blank is twice as long as it is across: 0.25 x 0.25 x 0.50 m`). Bay 2: 1 Gear Set a stroke plus
`0.02 x mass` (max 20), as the Drive die. Patterns: one per receiver (the three hand-made ones first; the 26 generated
ones are data).

| Dimension | Range | Step | What it does |
| --- | --- | --- | --- |
| **Scale** `s` = across / the preset's across | 0.75 to 6 (a Rifle Receiver: 15 to 120 cm) | 5 cm of across (0.25 of `s` for a Rifle Receiver) | the size of the round |

### 4.2 The law is the merge's

`ReceiverMerge.json`'s `powers`, read with a real number: `value = file value x s^power`.

| Number | Power | s = 0.75 | 1 (preset) | 1.5 | 2 | 6 |
| --- | --- | --- | --- | --- | --- | --- |
| damage | 2 | x0.56 | x1 | x2.25 | x4 | x36 |
| fire rate | -1 | x1.33 | x1 | x0.67 | x0.5 | x0.17 |
| kick | 2 | x0.56 | x1 | x2.25 | x4 | x36 |
| rounds a shot (at least 1) | 2 | 1 | 1 | 2 | 4 | 36 |
| magazine | 2 | x0.56 | x1 | x2.25 | x4 | x36 |
| spread, range | 0 | x1 | x1 | x1 | x1 | x1 |

So a Rifle Receiver pressed at 40x40x80 with a 20 cm barrel of 5 calibres has **exactly the numbers of today's 2x2 merged
Rifle Receiver with a Gun Barrel**, and the preset is the file part. The top, 6, is the merge's `maxSize`. Code:
`ConstructPartDef.GunScale` (1 on files), `ConstructMerge.ScaleGun(ref CGun, WeaponDef, float n)` and
`ReceiverMergeRules.Times(float n, float power)`; `BuildReceiver` calls `ScaleGun` when `GunScale x N != 1`. The sound
steps (`g.MergeN`) take `round(s)`. The mass is the merge's: the file's times s to `partMass` (the user, 2026-11-13; phase 3 notes).

### 4.3 Sized receivers and merging

- A pressed receiver **never merges** (the merge finder takes file receivers only), so sizes cannot be multiplied
  through both doors: 36 times a rifle's damage stays the ceiling. Catalogue receivers merge as today (answer 3).
- Catalogue gun parts on a pressed receiver (magazines, actions, optics, catalogue barrels, and what hangs on them) are
  drawn `round(s)` times bigger through the existing `PartScale`, as on a merge. A pressed gun part is never rescaled
  (`PartScale` 1), on a pressed receiver or a merged one: its size is its own.

## 5. Barrel to receiver: the bore band

A receiver's **bore** is `B = s x N` (its scale times its merge size; 1 for every plain catalogue receiver). A barrel's
bore class is `b = d / d_preset` (a 10 cm plain barrel is 1; a 20 cm one 2). `SizedGunParts.FitWhy` in `BuildReceiver`:

| Case | Rule | Text on the gun's look line |
| --- | --- | --- |
| Pressed barrel | `0.7 <= b / B <= 1.5` | `the barrel is 0.10 m across: this receiver takes 0.14 to 0.30 m` |
| Catalogue barrel on a merged file receiver | scaled by the merge, as today | none |
| Catalogue barrel on a pressed receiver | drawn `round(s)` times bigger; fits when `0.7 <= round(s) / s <= 1.5` (always true) | none |
| Barrels on one splitter | each checked against the receiver | the same text, naming the first |

Inside the band nothing changes with the mismatch (answer 4). Muzzle devices are catalogue parts
and follow the catalogue rule. The band is data (`Pressing.json`, the Barrel die's `"fit": [0.7, 1.5]`).

## 6. Engines

### 6.1 The engine reads scale

- `ConstructPartDef.PieceScale`: `rules.Scale[PieceSize]` for a file piece; for a pressed piece the blank's across over
  the Small preset's across. `PieceSize` stays as the nearest class (the Engine Kit's words, the sound bank's pick).
- **The crank's scale** is the largest crank section's `PieceScale`: sections of any sizes join into one crank, and the
  biggest sets the revs (it is the heaviest to turn).
- `EngineRules.At(float scale)` gives kW, idle, peak and limiter rpm by a straight line between the `sizes` rows in
  log-log (knots at 1, 4, 16, 64); past the ends, the end segment. At a knot it is the row. kW is 20 times a step, so
  between knots it is `40 x scale^2.161`; rpm falls by 0.56 then 0.5 a step.
- A starter is needed when the crank's scale reaches `starterFrom` (new in the file, 8: half-way in log between Medium
  and Large). A starter of any size turns any engine over.
- `ModEngine` gains `Scale` (the crank's) and `Stroke`; `EngineFx` and `EngineSounds` read `e.Scale` where they
  read `rules.Scale[e.Size]`.

### 6.2 What the dies offer

| Piece | Die and pattern | Blank | The player's dimensions | Bay 2 |
| --- | --- | --- | --- | --- |
| Cylinder | **Cylinder** die (exists), new pattern `engine` | `pipeN` | **bore** = across; **stroke** = length | Piston Set, `1 + 20 x volume` |
| Crank Section | **Engine** die (new), `crank` | `solid`, `round` | across = the engine's scale; length = the section's (its cylinders' pitch) | Bearing, `2 + 0.5 x length` |
| Flywheel | Engine, `flywheel` | `round` | across | Bearing, 1 |
| Carburettor, Injector Rail | Engine, `carburettor`, `injector` | `hollow`, `solid` | across (proportions the preset's) | none; injector: Circuit Board 1 |
| Turbo, Supercharger | Engine, `turbo`, `supercharger` | `hollow`, `round` | across | Bearing, 2 |
| Starter Motor | Engine, `starter` | `round` | across | Copper Coil, 1 |
| Exhaust Outlet, Smokestack | Engine, `exhaust`, `stack` | `pipeN` | across; a stack's length is free | none |

Each pattern has **four presets**, the Small to Capital files, which are its knots (a Small Cylinder is about
`pipe5 45x45x75 steel`; the generator takes each from the file's bounds). The body is `"body": "like"` on the **nearest**
preset's file (by log of scale), so a cylinder 3.5 times Small is the Medium model stretched to 0.875. Ranges: across
from 0.7 of the Small preset to the Capital preset (a press of its class; rework 3.1), in steps of 5 cm, or 1/16 of
across where that is more (to the lattice), so a size is never more than about 6% from its neighbour and the count of
sizes stays near 16 an octave. Recipes with two or more kinds of part input (the sort rule, rework section 2) stay
Assembler recipes.

**The tier cylinders go (answer 8).** Bored, Performance and Racing Cylinders (12 files, `power` 1.3, 1.6, 2) are replaced
by bore: a plain cylinder 1.13, 1.24 or 1.38 times its crank's scale gives the same 1.3, 1.6 or 2 times the power
(`ratio^2.161`), for the steel and Piston Sets of its size. Their Assembler recipes are removed and they leave the build
catalogue (`Categories.json`); their part files stay and still load, so an old blueprint, a save or a shipped vehicle
holding one builds and runs with today's numbers (section 8). The Engine Kit plans with them until it learns to plan a
bigger bore (section 9).

### 6.3 The sums

| Number | Formula | Anchor |
| --- | --- | --- |
| A cylinder's kW | `At(boreScale).Kw x PiecePower x share x fuel x boost`, with `boreScale` = its across / the Small preset's | the four cylinder files give 40, 800, 16,000, 320,000 |
| Stroke ratio `r` | (its length / the Small preset's length) / its own bore scale; 0.6 to 1.6 (the die's range) | 1 for every file cylinder |
| Engine rpm (idle, peak, limiter) | `At(crankScale).Rpm / r`, `r` the cylinders' mean weighted by their kW | the table's rows at `r` = 1 |
| A bank's feed | `min(1, At(fuel scale).Kw / At(bank scale).Kw)`, the bank's scale its largest cylinder's bore scale; the bank's kW times it | 1 when the fuel system is the cylinders' size, as on every file engine |
| A booster's gain | `1 + (PiecePower - 1) x min(1, At(booster scale).Kw / At(bank scale).Kw)` | the file's multiplier at its own size |
| Torque at the peak | power / peak rpm, as today: so it rises with `r` | unchanged at `r` = 1 |
| Fuel | `kW x PieceThirst x thirsts x burnPerKw`, as today | unchanged |
| Fuel system, booster | their multipliers, unscaled | unchanged |
| Mass, health | the blank's, as every pressed part | preset: the file's |

Bore gives power; stroke trades revs for torque at the same power (a long-stroke engine pulls from low revs, a short one
spins). A bore above the crank's scale is a "bored" cylinder, the way to more power now that the tier cylinders are gone:
1.38 times the crank's scale gives twice the power for about twice the steel and Piston Sets. Nothing caps it but the
press: a cylinder of 4 times the crank's scale bolts on and gives a Medium cylinder's 800 kW at a Small crank's revs.

### 6.4 Mixed sizes: anything bolts on (answer 6)

No size is refused. `EngineFault.MixedSizes` is no longer raised (the value stays in the enum, which is never on the
wire) and `SnapBuild` no longer refuses a piece of another class. What a mismatch does instead:

| Piece | Of another size | Look line when it costs power |
| --- | --- | --- |
| Cylinder | gives its own bore's kW; its stroke joins the engine's weighted `r` | none |
| Crank section | joins the crank; the largest sets the crank's scale and so the revs | none |
| Carburettor, Injector Rail | feeds its bank by the feed ratio (6.3): a Small carburettor on a bank of Medium cylinders feeds 5% | `bank 1 starved: the 0.30 m carburettor feeds 5% of 0.90 m cylinders` |
| Turbo, Supercharger | its gain scaled by the same ratio; a bigger one gives no more than its own multiplier | `bank 1: the turbo is small for its cylinders (40% of its boost)` |
| Flywheel, Starter, Exhaust, Stack | work at any size (their mass and look are their own) | none |

A starved engine still runs (`Complete` as today); the text is the first starved bank's, shown where the fault would be.
File pieces of one class behave exactly as today (every ratio 1).

## 7. Data

- **`PressScale` gains a curve.** `{"field": "rangeM", "by": "calibres", "points": [[2, 0.7], [5, 1], [9, 1.4]], "clamp": [0.5, 2], "op": "mul"}`.
  `by` gains `calibres` (length over across) beside length, across, area, volume, mass. With `points` the value is the
  interpolated one (`"log": true` for the engine's); without, today's power law. `Pressed.Scalable` gains the builder
  stat names (checked against `WeaponDef`'s fields as a part file's `stats` are) and `kw`, `rpm`.
- **`Pressing.json`** stays format `/2`: new dies Barrel, Receiver, Engine; the Cylinder die's `engine` pattern; a
  pattern may list several presets (`"presets": [...]`, the knots) and `"likeBy": "nearest"`; a die may state `"fit"`,
  `"step"` and `"aspect": "preset"`. Written by `--gen-parts press_parts` only; the curve points are read from the part
  files by the generator.
- **`ModularEngine.json`** becomes format `/2`: adds `starterFrom`. Format 1 is read with the default. No fit band. Its generator is the part kit's `engine_parts`.
- **`ReceiverMerge.json`**: unchanged; its powers now also serve a real `s`.
- Die parts: `Barrel Die`, `Receiver Die`, `Engine Die` in the classes their bounds reach, and their lathe rows, as
  `PressParts.WorkingDies3b` made the last seven.

## 8. Keys, wire and saves

| What | On the wire | A save or blueprint |
| --- | --- | --- |
| A pressed barrel, receiver or engine piece | nothing new: a named run-time part (kind 245 and its name), as every pressed part since protocol 258 | its name |
| A gun's numbers, a merge, an engine's numbers | nothing: derived from parts and links at the rebuild on every peer | nothing |
| A preset's product | the catalogue part's id | the catalogue name |
| A handheld build (`Msg.ZBuild`, `workbench.txt`) | unchanged now. A pressed key has none of the build text's marks (`{ } : , \|`), so a build can name one later (section 9) | unchanged |

- The key holds everything: die, pattern, blank. Bore, calibres, scale, stroke and every number follow from it, so two
  peers cannot disagree. Curves use integers from the key and round to three significant figures (`Pressed.Sig3`).
- **Protocol.** One bump in each phase that adds part files (die parts move ids) or changes a formula's data
  (`ModularEngine.json` /2): phases 1, 3, 4 and 5 below. The merger renumbers.
- **Old saves, blueprints, shipped vehicles, builds.** They name catalogue parts, which are presets: the same file part,
  the same id on this build, the same numbers. No migration and no file changes; `--gen-vehicles` must stay clean. The
  tier cylinders are the one kind of catalogue part with no recipe after phase 5: they still load and run as before, so
  a blueprint holding one builds where parts are free (creative, a shipped vehicle) and, in survival, waits for the part
  as any part without a recipe does.
- **Mods.** A mod's builder part or engine piece is a file part as before. A mod's `ModularEngine.json` in format 1 loads.

## 9. What happens to the fixed parts

| Today | After |
| --- | --- |
| `Gun Barrel`, `Long Gun Barrel`, the 22 generated barrel items | presets of the Barrel die (and the knots of its curve). Items, as today |
| `Rifle`, `Heavy`, `Shell Receiver`, the 26 generated receivers | presets of the Receiver die |
| The 40 modular engine pieces | presets of the Cylinder and Engine dies, four a pattern, where the sort rule makes them single-piece (the four plain cylinders leave the Assembler for the Cylinder die in phase 5); the rest keep their Assembler recipe |
| The 12 tier cylinders | removed from play (answer 8): no recipe, out of the build catalogue, phase 5. The part files stay so old blueprints, saves and shipped vehicles keep working; the Engine Kit still plans with them for its tiers 3 to 5 until it plans bored cylinders (later work) |
| Gun and engine single-piece recipes held back from rework phase 5 (9 `gunpart` rows, 36 `enginePiece` rows) | become presets in phase 6 here; generated builder barrels and receivers, which had no recipe, get one |
| Feeds, actions, muzzle devices, optics, stocks, grips, ammo (about 290 generated items) | unchanged: catalogue items, no size, still no recipe. Not in this design |
| The fixed `Engine` part and the other `engine` role parts | assemblies: unchanged |
| The Engine Kit and the vehicle generators | plan with catalogue pieces as today, the tier cylinders included; reading back uses `e.Scale`. Planning sized pieces (a bored plain cylinder in place of a tier one) is later work |
| Handheld builds at the Gun Workbench | catalogue parts only. An odd-sized part is a physical piece until inventories carry parts by key (user answer 2); question 7 |

## 10. Performance

- **No per-tick cost.** A sized builder part and a piece's scale are made once, in `Pressed.Make`. Guns are worked out in
  `ConstructWeapons.BuildReceiver` and engines in `ConstructEngine.Build`, both at a construct's rebuild as today; the
  tick reads `CGun` and `EngineDef` and nothing else. A curve is at most four knots; `At(scale)` is one interpolation
  per cylinder per rebuild.
- **`Parts.Find`** is a scan of 343 names per call today. The prefix test comes first and a pressed key is one dictionary
  lookup (`ConstructParts.Find`), so sized parts are cheaper to find than file parts.
- **First make.** A copy of a `PartDef`, at most 12 effect rows and the part's boxes: phase 1 prints it in
  `--press-part` and holds it under 0.05 ms beside the construct def's own 0.04 ms.
- **Distinct keys.** The die's step and proportion rules bound them:

| Die | Sizes a pattern | Why |
| --- | --- | --- |
| Receiver | at most 22 | across only, 15 to 120 cm by 5 |
| Barrel | about 1,100 | 23 acrosses x at most 16 calibres in 10 cm steps (fewer when thick: a step is a larger share of a calibre) |
| Engine pieces | about 100 (one free dimension), about 1,000 (cylinder: bore x stroke; crank: across x length) | 16 sizes an octave, six octaves |

  Live defs are what stands in the world: each sized builder part belongs to its pressed def, takes no slot of its own,
  and is freed with it (`Pressed.Forget`, the `RuntimeParts` sweep) inside `Extrusion.MaxDefs` (48,000). Past that the
  press refuses as an Extruder does. Nothing here raises a cap: 256 guns, 32 modular engines, `MaxMuzzles` stay.
- **Allocations.** None new in a tick. `BuildReceiver` allocates its tree at a rebuild as today.

## 11. Phases

Each phase: its own branch and worktree, WIP commits with `[skip ci]`, the final push without; no self-test sections
added (tests only when asked); tried by hand as listed, with what was not verified in the report. Archived sections that
cover the code are run by name: "Sandbox weapon parts", "Construct merged receivers", "Modular engine". Docs are updated
in phases 3 and 6 only. Start after the press's phase 4 has merged (`Pressed.cs` and `PressParts.cs` are shared).

| # | Title | Work | Files | Stop point | Try by hand |
| --- | --- | --- | --- | --- | --- |
| 1 | **Curves and the sized builder part** | `PressScale` `points`, `calibres`; `SizedGunParts.Make`; `ConstructPartDef.GunSized`; `Parts.Find`'s prefix door; the Barrel die with the plain pattern, its three presets, die parts and lathe rows; `--press-part` prints a gun part's effects beside its preset's. Check that clients run `BuildReceiver`. Protocol bump | new `Source/Constructs/SizedGunParts.cs`; `PressDies.cs`, `Pressed.cs`, `ConstructParts.cs`, `Zombies/PartDef.cs`, `Zombies/PartFile.cs`, `PartKit/Generators/PressParts.cs`, `Pressing.json`, die part files, `Net/ServerBrowser.cs` | `--press-part barrel.plain "round 10x10x50 steel"` is the Gun Barrel; `10x10x70` prints range x1.2; `sweep barrel` clean; a blueprint with a pressed barrel on a Rifle Receiver fires | `--press-part` for 20, 50, 70, 90, 160 cm; a window shot of the gun's look line; host and client agree on its range |
| 2 | **Barrels on guns** | The bore band (`FitWhy`, the look line's text); pressed gun parts exempt from `PartScale` on a merge; the bore, match, shell, special and artillery patterns with their presets (the generated barrel items) | `ConstructWeapons.cs`, `ConstructMerge.cs`, `SizedGunParts.cs`, `PressParts.cs`, `Pressing.json` | A 20 cm barrel on a 2x2 merged Rifle Receiver has a scaled Gun Barrel's numbers; a 10 cm one is refused with its text; every barrel preset resolves to its catalogue part (`--press-part presets barrel`) | Press a barrel on the Production Showcase and fit it; the two merge cases; a splitter with one pressed and one catalogue barrel |
| 3 | **Sized receivers** | `GunScale`; `ScaleGun` and `Times` with a real number; the Receiver die (three hand-made patterns, then the generated ones as data), its proportion rule and bay 2; pressed receivers never merge; catalogue gun parts take `round(s)`; bore `s x N` in the band. Protocol bump. Docs: weapons.md, production.md | `ConstructMerge.cs`, `ConstructWeapons.cs`, `ConstructParts.cs`, `Pressed.cs`, `PressParts.cs`, `Pressing.json`, die part files, `ServerBrowser.cs`, docs | A 40x40x80 Rifle Receiver with a 20x20x100 barrel prints the numbers of a 2x2 merge with a Gun Barrel | Both guns side by side, fired at a plate; a 15 cm receiver; four pressed receivers in a square stay four guns; a client's look lines |
| 4 | **The engine reads scale** | `PieceScale`, `EngineRules.At`, `ModularEngine.json` /2, the crank's scale, the feed and boost ratios and the starved texts, `Build` and `SnapBuild` no longer refusing mixed classes, `ModEngine.Scale` and `Stroke`, rpm by the weighted `r`, `EngineFx`, `EngineSounds`, the kit's read-back. No die yet: the one change a player sees is that pieces of two classes now make one engine. Protocol bump | `ConstructEngine.cs`, `ConstructParts.cs`, `ConstructDriveParts.cs`, `SnapBuild.cs`, `Render/EngineFx.cs`, `Audio/EngineSounds.cs`, `EngineKit/EngineKit.cs`, the part kit's engine generator, `ModularEngine.json`, `ServerBrowser.cs` | `--gen-engine` prints ENGINEKIT.md's sample table unchanged; `--gen-vehicles` and `--gen-engine-parts --check` clean | `--speed-test` on two shipped vehicles against the doc's figures; a Medium cylinder on a Small crank with a Medium carburettor runs at 800 kW and a Small crank's revs; with a Small carburettor it says the bank is starved |
| 5 | **Engine pieces off the press** | The Cylinder die's `engine` pattern and the Engine die: four presets a pattern, `likeBy` nearest, `PieceScale` from the key, bay 2; `--press-part` prints kW, rpm and the stroke ratio. The four plain cylinders' recipes leave the Assembler for the Cylinder die; the 12 tier cylinders lose their recipes and leave `Categories.json` (answer 8). Protocol bump | `Pressed.cs`, `PressDies.cs`, `PressParts.cs`, `Pressing.json`, die part files, `PartKit/Generators/Assembling.cs`, `Assembling.json`, `Categories.json`, `ServerBrowser.cs` | A 60 cm bore on a Small crank gives more kW than the catalogue cylinder; a 100 cm one runs with its own kW and is starved by a Small carburettor; a 100 cm stroke lowers the peak rpm; `--bill` of a vehicle with a tier cylinder says it has no recipe | An engine of a pressed crank, two pressed cylinders, a catalogue carburettor and flywheel run in a window; its look line; a client agrees |
| 6 | **Presets, bills, docs** | The Assembling generator's sort gives the held-back `gunpart` and `enginePiece` rows and the generated barrels and receivers their presets; `--bill` shows no "no recipe" for them; docs: drivetrain-and-engines.md, ENGINEKIT.md, WEAPON-BUILDER.md (a pointer), MODS.md | `PartKit/Generators/Assembling.cs`, `PressParts.cs`, `Assembling.json`, `Pressing.json`, docs | `--gen-parts assembling --check` and `press_parts --check` clean; `--press-part presets` clean | `--bill` of two armed vehicles before and after, to files; a terminal order for a Gun Barrel reaches a press |

Order: 1, 2, 3 in turn; 4 then 5; 6 last and after rework phase 5. Phase 4 touches none of 1 to 3's files and may run
beside them. Phases 1 to 3 and 5 share `Pressed.cs`, `PressParts.cs` and `Pressing.json`: never two at once.

## 12. Risks

- **Balance moves from tiers to steel.** A big bore or a 6x receiver costs material, kits by mass and a press of its
  class, not a tier. 36 times a rifle's damage is already reachable with 36 receivers; one part makes it cheaper. The
  die's class (Large dies need a Large lathe) is the only gate, as the rework already noted for `minTier`.
- **`ConstructMerge` is intricate** (settling, popped parts, centre sockets). Phases 2 and 3 change who is rescaled and
  who merges; they are the likeliest to run long. Fallback: phase 3 ships pressed receivers at whole scales only (1 to
  6), where `PartScale` needs no rounding rule.
- **Catalogue parts on a part-scale receiver** (s = 1.25, 1.75) are drawn at `round(s)`: a magazine looks a little
  small or large. A look problem only; pressed magazines would be the cure and are not in this design.
- **Look beside a preset.** The catalogue Gun Barrel and a barrel 10 cm longer differ in model (file boxes against a
  stretched copy). `"body": "like"` keeps them close; one picture review when the user asks.
- **The lattice is coarse for small guns.** The least blank is 10 cm across, so no pressed barrel suits a receiver below
  s = 0.75, and a Rifle Receiver's scale moves in quarters.
- **Three figures of rounding** on rpm and kW can differ from an Engine Kit plan's own sum by under 0.5%; the kit reads
  back from the live engine, so its reports stay true.
- **Sound and effects by class.** `EngineSounds.Pick` and the gun report's steps take the nearest class or `round(s)`:
  a 2.4 and a 1.6 receiver sound the same.
- **Anything bolts on (answer 6)** puts a Capital cylinder on a Small crank within reach: 320 MW through the smallest
  crank, at its revs. Nothing models a crank's strength; the cost is the press's class and the steel. If it proves silly,
  a crank's torque limit is the lever, not a fit band.
- **The Engine Kit's tiers 3 to 5** lean on the tier cylinders, which a survival player can no longer make after phase
  5. Vehicles it generated still run; a player copying one in survival lacks those parts until the kit plans bored
  cylinders.
- **The "Modular engine" section** may pin `MixedSizes` for two classes; phase 4 runs it by name and updates the case.
- **Self-tests pinned to numbers** ("Modular engine", the vehicle speed figures) fail if phase 4 moves a float by a last
  digit: `At` must return the row itself at a knot, not an interpolation that lands beside it.
- **Not verified here**: clients running `BuildReceiver`; engine file bounds; whether `ReceiverMergeRules.Times` and
  `g.PerShot` assume whole numbers anywhere else.

## 13. Questions for the user

One at a time; the recommended option is first. All eight are answered (section 14): the text above follows the
answers, and the questions are kept as the record of what was asked.

1. **A barrel's character (Needle, Heavy Bore, Bloat, Marksman...): where does it come from?**
   - A (recommended): a **pattern** on the press; size adds length (range, spread, handling) on top. Every catalogue
     barrel keeps its exact numbers.
   - B: from the blank itself: a thick-walled pipe is a Needle Barrel, a thin one a Bloat Barrel (bore share sets round
     size, speed and damage). Fewer patterns, but the lattice gives only three or four bores at 10 cm.
   - C: from the barrel's across against its receiver (a thin barrel on a big receiver squeezes the round).
2. **How does a receiver's size change the gun?**
   - A (recommended): **the merge's law**: damage, kick, magazine and rounds a shot by size squared, fire rate divided
     by size; a receiver pressed twice the size is exactly today's 2x2 merge. Sizes 0.75 to 6.
   - B: a gentler law of its own (damage by size, rate by its square root), so merging stays the stronger choice.
   - C: receivers are not sized: presets only; only barrels are.
3. **Sized receivers and merged receivers both make a big gun. Keep both?**
   - A (recommended): both; a pressed receiver never merges, catalogue receivers merge as today.
   - B: pressed receivers merge too, with size times merge held to 6.
   - C: merging goes: an NxN square in an old blueprint becomes one pressed receiver of that size at load.
4. **A barrel that is too thin or too thick for its receiver:**
   - A (recommended): **refused** outside 0.7 to 1.5 of the receiver's bore, with the range in the text.
   - B: any barrel fits; a mismatch costs spread (too thick) or damage (too thin).
   - C: any barrel fits and is drawn to the receiver's size, as on a merge today (its across then means nothing).
5. **What does an engine cylinder's stroke (its length) do?**
   - A (recommended): **bore gives power; stroke trades revs for torque** at the same power.
   - B: power follows displacement (bore squared times stroke); rpm stays the crank's.
   - C: nothing: a cylinder has one size and fixed proportions, as the catalogue's.
6. **How strict is an engine about mixed sizes?**
   - A (recommended): a **band**: every piece within 0.7 to 1.4 of the crank, cylinders' strokes within 10% of each
     other; outside, the engine names the piece and the range.
   - B: anything bolts on: each cylinder gives its own power, a small carburettor starves a big bank.
   - C: exact match only, as classes are today.
7. **Hand-held guns (the Gun Workbench) and sized parts:**
   - A (recommended): wait for the inventory project (user answer 2): until slots carry parts by key, hand-held builds
     use catalogue parts; this design keeps the build text ready for keys.
   - B: now: the workbench takes pressed pieces set down on it (a rule of its own, replaced later).
8. **Bored, Performance and Racing Cylinders (x1.3, x1.6, x2 power):**
   - A (recommended): stay catalogue parts with Assembler recipes; a bigger bore is the press's way to more power, a
     tier cylinder the Assembler's. They become piece recipes (a pressed cylinder in, sized out) after rework phase 6.
   - B: three more patterns of the Cylinder die, sized like the plain one.
   - C: removed: bore replaces them (old blueprints keep the files).

## 14. The user's answers (2026-11-13)

1. A barrel's character is **a pattern on the press** (A).
2. A receiver's size follows **the merge's law** (A).
3. **Both stay**: a pressed receiver never merges, catalogue receivers merge as today (A).
4. A barrel outside 0.7 to 1.5 of the receiver's bore is **refused** (A).
5. **Bore gives power; stroke trades revs for torque** (A).
6. **Anything bolts on** (B, not the recommendation): no band. Each cylinder gives its own power and a small carburettor
   starves a big bank. Principle 4 ("fit is a band") no longer holds for engines; it still holds for a barrel on a receiver.
7. Hand-held guns **wait for the inventory project** (A).
8. Bored, Performance and Racing Cylinders are **removed** (C, not the recommendation): bore replaces them; old blueprints
   keep the files, and their Assembler recipes go.

### Answer, 2026-11-14 (bored cylinders)

The user, on bored cylinders making an engine weigh about twice a plain one: "bored engine cylinders don't make sense being heavier. It literally has less material in a bored cylinder to produce more power. So it should be lighter, if anything, but easier to damage. And with the new engine size options, power has become more of a function of cylinder size, I believe."

- A wider bore in a cylinder of the same outer size is less metal: lighter, and easier to damage (less health).
- Power follows the cylinder's size (bore and stroke), not a tier or a bored flag.
- A bored engine is never heavier than the plain one of the same outer size.
