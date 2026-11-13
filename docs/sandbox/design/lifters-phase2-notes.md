# Lifters phase 2: notes (2026-11-13)

Branch `claude/lifters-2-95f8f4`. The design is `arms-and-cranes.md` (phase 2: look, panel, tiers, saves). Protocol 290. Pushing
players is phase 3 in the revised design, so it is not here.

## What was built

- **Phase 1's leftovers.** A piece in a hand at a save is no longer lost (the `LIFT` block, below). `LiftSeen` no longer keeps a
  record for every body ever looked at (gone bodies without lifters are dropped at each scan), and the lifters' lists are cleared
  with the world (`ConstructWorld.LiftClear`, called from `Clear`). A lifter without power now says so on the wire, so a client's
  drawing stops where the host's arm stopped instead of running on.
- **Five tiers** (`LifterParts.cs`): Loader Arm, Workshop, Industrial, Military and Plasma Loader Arm, the design's table 3.2, each
  grown by the machine tiers' size (x1 to x2.5: plinth, ring, links, shoulder height), with the tier's palette (`TierStyle.Palette`)
  and its own dressing (timber props and chain drum; cream trim, rivets and a motor box; a hydraulic power pack, pipes and striped
  kick rails; armour skirts, a stencil band and sandbags; a round dark drum with a glowing ring and four lit pylons). The drawn half
  takes its colours from the new `lifter.look` keys and stands on `lifter.ring`; a tier 3 or higher arm draws a hydraulic ram on its
  upper arm. Recipes: Assembling's rule gives a part with a `lifter` block Gear Sets (tier 1), Servo Motors (2+) and a Control Unit (3+).
- **Settings**: `liftMode` (Works: Auto, Station only, Carry only, Off) and `carryHeight` (`CfgKey.LiftMode`, `CarryHeight`, at the
  end). The carry rise is fixed into the move when it starts (`CLift.Rise`, sent), so both peers draw the same path.
- **Ports**: in Run; out Busy (`Out`), Alarm (`Out1`), Moved (port 2, `CPart.Mem` through `ConstructModules.Output`; not saved).
- **Power** (`ConstructPower`): a lifter draws only while Busy.
- **Look line** (`ConstructLifters.Look`, from `Program.Sandbox.cs` beside the Router's, built only when its key changes), the
  **reach view** (`LifterRender.DrawReach`: amber rings at the floor and top of the reach, a green or red post over each building
  station's hold), and the station panel's **Fitters line** (`ConstructLifters.FittersLine`, made once a second while open).
- **The LIFT save block** (`WorldSaveBodies.cs`): after LOGI, per hand holding an unfitted piece: part, piece by the save's names,
  paint. On load the piece is in the hand, Stuck; every 2 s the arm looks for a building station in reach whose hold takes it and
  carries it back there (`PutBack`, `Stow`); the station then fits it as any part. The reader now walks tagged blocks in a loop
  (an older build reads LOGI and skips the rest; a LIFT-only body skips it by its length).
- **Wire** (protocol 290): the lift word adds the carry rise, the reason (`LiftWhy`), the step, the station body and the move count.

**Files outside the lifter files, one hook each:** `Source/UI/UiStation.cs` line 197 (the Fitters line, before the Close button);
`ConstructStation.cs` untouched; `ConstructNet*.cs` untouched; `ConstructParts.cs` (lifter's DataIn/DataOut and tier), `ConstructConfig.cs`
(two keys and their owner line), `ConstructModules.cs` (Moved), `ConstructPower.cs` (draw while busy), `ConstructWorld.cs` (`Clear`),
`WorldSaveBodies.cs`, `WorldSnapshot.cs`, `Program.Sandbox.cs` (look line), `Program.WindowShot.cs` (`lifter place [TIER] [SLOT]`,
`lifter holding`), `Generators/Assembling.cs`, `ServerBrowser.cs`.

## Tried by hand (Proving Ground, `--spawn-at 140,0,-100`, the Production Showcase's station, the Buggy, hold stocked)

- **Four tiers at one station, host**: Scrap (slot 0), Workshop (1), Industrial (2, 6 m along the bay) and Plasma (3). 26/26 fitted
  36.6 s after the start; 15 by the arms (Scrap 6, Workshop 8, Plasma 1); the Industrial arm reported "no station in reach" (its
  slot is past its reach of the hold), which is right. Phase 1's figure with one Scrap arm was 44 s.
- **Tick cost, several arms working** (3 and 4 arms, host): the lifter step 3.5 us a tick on average, worst 0.94 ms (that worst is a
  tick with a fit, which rebuilds the body: the station's own cost); the whole server tick over 150 measured frames: median 0.12 ms,
  p99 0.91 ms, worst 2.56 ms.
- **Save and load mid-carry**: saved with a Square Beam 2 m in the Scrap arm's hand (48 of 151 ticks into its carry), loaded in a
  fresh process: the arm held it (Stuck, Holding), put it back into the hold ("put its Square Beam 2 m back into the station's
  hold"), and the build finished 26/26, 15 by the arms.
- **Host and joined client** (`--net host`, `join:127.0.0.1`), three arms: the client heard all 15 carries, and every arm's state,
  reason and move count at the end matched the host's (6, 7, 2). The client's station line read 25/26 at its last look, its note
  "Fuel Tank (riding to the tray)", while the host reached 26/26: by the note the client looked before the station's own last
  fit, but a late look was not tried, so it is not verified.
- `--gen-parts all --check` and `--check-construct-parts` pass. No self-test sections run (none covers this code).

**Not tried**: the Works and Carry height settings and the Run, Busy, Alarm and Moved ports in a real wiring (code paths only);
power draw while moving (not measured); the reach view, look line, Fitters line and the five looks with my own eyes (no pictures:
the rule); `--look-report` on the arms; a lifter removed while holding a loaded piece; eight arms at one station; bytes a second.

## Choices made (each can be changed)

1. **Tiers in `LifterParts.cs`, not `MachineTiers.cs`.** MachineTiers writes the six machine lines' own file layout; the arms share
   its sizes and `TierStyle.Palette`, which is what the design's line asks for, without a seventh line there.
2. **"Carry only" and "Off" both keep an arm from station work** until carrying between machines exists (phase 4).
3. **Moved counts fits since the world loaded** (`CPart.Mem`, never saved).
4. **A loaded piece goes back into the hold**, not straight onto the build: a claim needs the station's plan, which the load costs
   again. The station may order one more of that kind while the piece is in the hand (seen: one spare left in the hold at the end).
5. **The reach view** draws for the lifter under the crosshair only; the station panel shows the Fitters line, not the arms' rings.

## Questions for the user (each with a recommendation)

1. **The machine screen** (the design's "machine screen" for an arm). A (recommended): leave it to the logistics owner's
   `MachineScreens.cs` once phase 4 gives arms points to list; the look line covers it now. B: a small screen on the plinth now.
2. **A spare part after a load with a piece in a hand.** A (recommended): accept it (one part, rare). B: count a held piece as in
   the hold for the station's orders.
3. **The Industrial arm's 6 m along the bay could not reach the hold.** A (recommended): leave the hold as the only pick point
   until phase 4's pads give arms further along the bay something to pick from. B: let an arm pick from any point of the hold's
   side, not only its nearest corner.
4. **Phase 1's open questions** (arms on other constructs, the Scrap arm's reach, the "no clear way" check, how a cab finds its
   lifter) are still open; this phase assumed the recommendations.

## The user's answers (2026-11-13), built in phase 3's branch

1. **The machine screen: B, a small screen now.** A small screen on the plinth shows what the arm is doing; its code stays in
   the lifter files (a panel source of their own, `lifter`, as the Panel Designer desk has its own), not in `MachineScreens.cs`.
2. **The spare part after a load: fix it.** A piece in an arm's hand counts as in the hold for the station's orders, so a save
   and load never orders a part twice.
3. **The pick point: both, in time.** Now: an arm picks from any point along the hold's side it reaches. Later: pick pads for
   larger building areas, where pulling parts from the hold's side no longer makes sense (the network phase).

Phase 1's open questions keep their recommendations.
