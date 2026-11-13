# Sized gun and engine parts: phase 1 notes (2026-11-13)

Phase 1 of `sized-gun-engine-parts.md` ("Curves and the sized builder part"), built on branch `claude/sized-parts-1-6315bf`.
Protocol 282. The design itself was first revised for the user's answers 6 and 8 (section 14).

## What was built

- **Curves in `PressScale`** (`Source/Constructs/PressDies.cs`): a scale row may give `points` (knots of measure and value),
  `log`, `clamp` and `op`; `by` gains `calibres` (length over across). `PressScale.At` returns the knot itself at a knot.
  A builder stat (any number a part file's `stats` may change) is only scaled by a curve and needs `op` (`mul` or `add`).
- **The sized builder part** (new `Source/Constructs/SizedGunParts.cs`): `Pressed.Build` and `BuildLike` give a pressed
  gun part `ConstructPartDef.GunSized`, a copy of its pattern's builder part named by the key: the curves as stat changes,
  the muzzle, muzzle sockets and hand moved along its length in calibres, `PartDef.SizeAcrossCm` and `SizeLengthCm` set.
  The pressed def's `GunPartName` is its key, and `Parts.Find` sends a `Pressed Part (` name to it, so `BuildReceiver`,
  `BuildRules` and `BuildStats` are unchanged.
- **The Barrel die** (part kit `press_parts`): Small, Medium and Large Barrel Dies and their lathe rows; `round` or `pipe`
  steel, 10 to 120 cm across, 1 to 16 calibres; a crown ring at the muzzle; one pattern, `plain`, a like body of the Gun
  Barrel. Its four curves (range, hip spread, ADS time, mobility) are read from the Stub, Standard and Long Barrel part files
  by the generator; presets: Stub Barrel (`round 10x10x20`), Gun Barrel (`10x10x50`), Long Gun Barrel (`10x10x90`). Their
  Assembler recipes stay until phase 6.
- **`--press-part`** prints a gun part's length in calibres, muzzle, builder weight and each curve's value beside the
  pattern's preset's. **`--check-vehicle`**'s gun note now also gives the gun's range and spread.

## What was tried, and seen

- `--press-part barrel.plain` at 20, 50, 70, 90 and 160 cm (10 cm round): 20, 50 and 90 are the catalogue Stub, Gun and
  Long Gun Barrel; 70 cm prints range x1.2, hip spread x0.925, ADS +0.015, mobility -0.01, muzzle 0.28 m; 160 cm reaches the
  clamps (range x2, spread x0.588). A make takes 0.003 ms after the first (JIT 13 ms), 4 KB; 0 problems (z-fighting, sockets,
  a second make agrees).
- `--press-part presets`: 0 problems (the three barrels come off a Small Press). `sweep barrel --samples 60`: 62 blanks,
  0 problems; 200 new keys made in one go in 6 ms.
- `--gen-parts all --check`, `--check-construct-parts`: clean.
- **Fired** with `--check-vehicle` on copies of the Highland Turret 1 (a Heavy Receiver, a Cross Splitter and four barrels,
  a gunner's seat): pressed barrels of 20, 70 and 160 cm fit, the gun has no problem, and it fires 8 shots a second. Spread:
  Gun Barrel 1.5 deg, Long Gun Barrel 1.241, and the pipe blanks 10x10x50 and 10x10x90 (sized parts, not presets) give exactly
  1.5 and 1.241; 70 cm 1.371, 160 cm 0.789. So at a knot a sized barrel is the catalogue barrel, and between knots it follows
  the curve (spread includes the powder charge's share, which moves with the muzzle as it does for catalogue barrels).
- Self-test sections run by name: "Sandbox weapon parts", "Construct merged receivers", "Press stock": 0 failures.

## Not verified

- A host and a client agreeing on a pressed barrel's numbers in a real two-process run. By the code: `BuildGuns` runs in
  `Construct.Rebuild`, which every peer runs, and the part is made from its key alone, so a client derives the same gun.
- Pressing a barrel on a real press in a world, fitting it by hand and the gun's look line in the window (no window runs).
- The Barrel Dies in survival: no start kit, terminal or Technologies page was checked for them.

## Choices made

1. **Builder weight follows length in calibres**, not the blank's mass over `massScale` as section 3.2's table said: the
   file's weight times calibres over the preset's. A thick barrel on a big receiver then weighs what the merge's barrel
   does, so phase 2's "20 cm barrel on a 2x2 merge = a scaled Gun Barrel" stays exact (`BuildStats` reads weight for recoil,
   mobility and ADS). The construct part's mass is the blank's (a 10x10x50 barrel is 1.96 kg; the Gun Barrel file is 4 kg).
2. **Range clamp 0.55 to 2**, not the design's 0.5: the generator takes the widest value any catalogue barrel file has, and
   that is 0.55.
3. **Lengths go in the Extruder's 5 cm steps everywhere** (the design said 10 cm above 1 m). More keys (up to about 4,500 a
   section across every across and length), but only live parts take a def, within `Extrusion.MaxDefs`.
4. **The handheld model's boxes are the file's**, not stretched: nothing draws a sized builder part yet (handheld builds
   wait for the inventory project, answer 7).
5. **The plain pattern reads its curve at the blank's own calibres.** Section 3.3's `5 x c / c_preset` reading for the other
   patterns is phase 2's work; the code reads `c` for every pattern now.

## Questions for the user (each with a recommendation)

1. **A mounted bullet gun's reach ignores its barrel.** `BuildReceiver` sets `g.MaxRange` from the receiver's `gun.maxRange`
   (300 m); the build's `rangeM` reaches only shells, flame and harpoons. So a longer barrel, catalogue or pressed, gives a
   tighter spread but not more reach, which is not what the design (and the user's answer 4 of the rework) meant.
   - A (recommended): a bullet gun's reach is its receiver's `maxRange` times the build's range over the bare receiver's, so
     a Long Gun Barrel reaches 420 m and a 16-calibre barrel 600 m. Changes catalogue guns' reach: a phase 2 item.
   - B: leave it; on a mounted gun barrel length is spread and handling only.
   - C: only pressed barrels get the reach (catalogue guns unchanged): two rules for one barrel.
2. **The length step.**
   - A (recommended): 5 cm everywhere, as built (the Extruder's lattice; a key is a key).
   - B: 10 cm above 1 m, as the design said: a die rule refusing odd lengths, fewer keys.
3. **The Engine Kit after the tier cylinders go (answer 8, phase 5).** Its tiers 3 to 5 plan with Bored, Performance and
   Racing Cylinders, which a survival player will no longer be able to make.
   - A (recommended): before phase 5 merges, the kit plans a plain cylinder bored 1.13, 1.24 or 1.38 times the crank in
     their place (it needs phases 4 and 5's pressed pieces).
   - B: the kit keeps planning tier cylinders; vehicles it makes carry parts a survival player can't build.
4. **How hard a small carburettor starves a big bank (answer 6, written into section 6.3).**
   - A (recommended): by power: the feed is the carburettor's size's kW over the bank's (a Small one on Medium cylinders
     feeds 5%), so a carburettor must be about the cylinders' size.
   - B: gentler, by scale (across) alone: a Small one on Medium cylinders (a quarter of their scale) feeds 25%.
