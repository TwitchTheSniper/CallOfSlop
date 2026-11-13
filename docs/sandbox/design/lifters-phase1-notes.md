# Lifters phase 1: notes (2026-11-13)

Branch `claude/lifters-1-f8f1dc`. The design is `arms-and-cranes.md` (revised the same day for the user's answers 2 and 6:
pushing players is phase 3, the cab phase 8, eight phases in all). Protocol 286.

## What was built

- **The Loader Arm** (tier 1, Scrap): `Source/PartKit/Generators/LifterParts.cs` (`--gen-parts lifter_parts`), written to
  `Constructs/Parts/Lifters/LoaderArm.json`. A plinth with hazard paint all round, four timber props, a rusty turntable
  with a chain drum, a mast and a shoulder head; 380 kg, 600 W, welder. Its recipe comes from the Assembling generator's
  long tail (`Assembling.json` regenerated: one line added). Inventory: Storage and Logistics, new section Lifters
  (`Categories.json`).
- **The `lifter` block** (`LifterDef`, `ConstructLifters.cs`): kind, base, reach, up, down, payload, piece, speed, handle;
  a bad file is refused with the reason.
- **The step** (`ConstructLifters.Step`, after the stations' in `ConstructWorld`): nothing runs while no station is open
  and no hand is busy. Lifters are found by a look at bodies whose parts changed (`RebuildStamp`, once a second while a
  station is open). At 10 Hz an idle, powered arm asks the open stations it reaches; a move runs to its end; at most one
  fit a tick a world (`FitThisTick`, set by the station's own `FitOne` too).
- **The station side** (a partial of `ConstructStation`, in `ConstructLifters.cs`): `LiftCost` keeps each step's place from
  the costing's scratch build (`StationJob.Plan`); `LiftOffer` (at most 64 unfitted steps looked at) wants a step whose
  parent is in place, whose kind the hold has, whose part the arm carries and whose middle is in its reach; the part leaves
  the hold then; `LiftFit` is the station's own `FitOne`.
- **The wire**: `CBodyKind.Lifts` (at the end), a body's lifters when a move starts or ends and for a joiner, about 44 bytes
  a lifter. A client runs the move on from when it heard it.
- **Drawing** (`Source/Render/LifterRender.cs`, one call from `RouteRender.Draw`): the boom as one beam from the shoulder to
  a wrist over the hand, a gripper bar, and the piece at true size in its paint on the path.
- **Window tool**: `lifter` (every lifter as this peer has it, carries seen, fits by lifters), `lifter place` (host: an arm
  in the station's bay's back corner, turned as the station is), `lifter await` (host: until every part is fitted, with
  the seconds since the start).

## Choices made (each can be changed)

1. **A top-level `lifter` block, not `storage.lifter`.** A store handle is what a carried piece's `Dest` names, which
   phase 4 needs; phase 1 has no use for one and a storage part brings the store machinery with it. Phase 4 moves the
   block under `storage` (the generator writes it; nothing hand-made changes).
2. **An arm serves any station whose hold and bay it reaches, on any construct.** The station's hold and bay are fixed in
   the world while it builds, so an arm on its own foundation beside a station works; the reach test is the only link.
3. **A claimed step is `-2` in `StationJob.PartOf`** (the station's boom, a hand's give and `Ready` all skip it), and it
   counts in `KindFit` while in the air, so the station's orders need no change (its part has left the hold).
4. **The hand picks at the point of the hold nearest it**, a hand's height over the hold's floor, and must reach both that
   point and the step's middle. The empty hand goes back to the hold after a fit.
5. **The age, not a start tick, on the wire.** A client runs a move on from the moment it heard it. Phase 3 needs the
   mover clock's tick for prediction and sends `Start` instead (the design's protocol line says so now).
6. **Shared files touched, one or two lines each:** `ConstructStation.cs` (two classes made partial; `NextStep` skips `-2`;
   `FitOne` sets `FitThisTick`; `Cost` calls `LiftCost`), `ConstructParts.cs` (the key, the field, the parse),
   `ConstructNet.cs` (the enum value, the read case), `ConstructWorld.cs` (the step call), `Game/GameSandbox.cs` (joiner
   and flush), `RouteRender.cs` (the draw call), `Program.WindowShot.cs` (the verb), `PartKit.cs` (the registry),
   `ServerBrowser.cs` (286).

## Not done in phase 1 (for phase 2 unless said)

- **A piece in a hand at a save is lost**: the step loads unfitted and its part is not in the hold (the `LIFT` block).
- The look line, the station panel's Fitters line, the reach view, Busy, Alarm and Moved ports, `liftMode`, `carryHeight`,
  power draw while moving, the five tiers and the IK drawing (the boom is one straight beam).
- `LiftSeen` keeps a small record for every body ever looked at (removed only for bodies that had lifters).
- Not measured: a tick with eight arms, bytes a second in a long build, allocations over 600 ticks (the step allocates
  nothing; a look at a changed body allocates its lifter list).

## Questions for the user (each with a recommendation)

1. **A piece in an arm's hand when the world is saved.** A (recommended): phase 2's `LIFT` block, as designed (it loads in
   the hand). B: a small change now: the station counts it back into its hold on save. C: leave it lost.
2. **Arms and stations on different constructs.** A (recommended): an arm serves any station it reaches, whatever it
   stands on (what phase 1 does). B: only a station on the arm's own construct, as the design's points are.
3. **The Scrap arm's reach.** Standing in the bay's back corner it reached the parts at the near end of a Buggy, not the
   far end. A (recommended): keep 1 to 4 m and let tiers grow it; decide after phase 2's tiers are measured. B: 5 m at
   tier 1 now.
4. **The "no clear way" check** (answer 2 left it open). A (recommended): keep it as a cheap warning at the rebuild in
   phase 4, never a block. B: drop it.
5. **How a cab finds its lifter** (phase 8). A (recommended): linked with the linking tool, as a seat to what it drives;
   a cab on the lifter's own base needs no link. B: only a cab built onto a lifter's base.
