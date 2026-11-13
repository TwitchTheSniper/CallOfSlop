# Lifters phase 4: notes (2026-11-13)

Branch `claude/lifters-4-20e9f4`. The design is `arms-and-cranes.md`; its section 9 makes phase 4 **carrying between points** and
phase 5 the logistics network. This branch builds phase 4's first coherent part: arms carry between machines, racks and the new
Lay-down Pads by the Routers' rule (the user's answer 5), with no link laid. It also closes phase 3's untried wiring. Protocol **296**.

## What was built

- **The Lay-down Pad and the Large Lay-down Pad** (`PieceRackParts.cs`, the part kit): an open steel deck, a hazard kerb, the in side's
  white band, four corner posts to the stacking height with metre marks; no bars. A Piece Rack by its `storage.pieces` block: 3.6 x 1.8 m
  by 7.6 m pieces on a 4 x 8 m deck, 7.6 x 3.8 m by 15.6 m on 8 x 16 m. A conveyor may end at its Piece in (it is a rack). Recipes from
  the Assembling generator's rule (regenerated).
- **Points** (`ConstructLifterCarry.cs`, new): every 2 s an arm finds up to 16 points on **any construct in its reach**: racks and pads
  (pick and put; not a station's hold or a terminal's tray), a machine's **Piece out with no conveyor** (Assembler, press, Extruder:
  pick), an Assembler's, a press bay's or a Panel Former's **Piece in with no conveyor** (put).
- **A machine holds its piece for an arm.** Where an arm has that machine's out as a point, `ConstructAssembling.Leave` (Assemblers and
  presses) and the Extruder's plain cut put the piece in the machine's out (`ConstructWorld.LiftOuts`, at most 8 of one kind) instead of
  dropping it as a body; full, the machine waits (OutputBlocked, the Extruder's Blocked stall). With no arm serving it, nothing changes.
- **The rule** (about once a second per idle arm, at most 4 looks a step a world): a station's step first (Works: Auto), then a machine's
  held piece to a put point that **wants** it (an Assembler whose recipe uses it, a press bay that takes the blank or a kit part, a
  Panel Former that takes the sheet), else to **storage** (the nearest rack or pad with room that takes it, store priority first); a
  rack's or pad's piece **only to a want** (never storage to storage). One hand at a time is bound for a machine's input. A put refused
  at the end looks for another taker, else the piece waits in the hand (Stuck, Alarm, "nothing in reach takes it"), looked at again
  every 2 s.
- **Works** now means what the design says: Auto, Station only, Carry only, Off. The step runs whenever a world has lifters (a scan
  every second; an idle arm's look about once a second), not only while a station is open.
- **Look line and screen**: "carrying a Gear Set to the Assembler's Piece in", "going back for the next piece", "idle: nothing in reach
  to carry"; the screen's Carrying / Going back / Nothing to carry rows, and Moved counts carries and fits.
- **Wire** (protocol 296): the lift word sends the piece by PartRef (a run-time piece, a press's blank, by its key) and, per lifter, the
  put point's kind, part and bay (4 bytes) for the look line. New LiftWhy values at the end.
- **The arms' reach down** (`LifterParts.cs`): `down` is now the shoulder's height plus 0.3 m (was 1.0 x the tier's size), so a rack or
  pad standing on the same ground is in reach. The five arm files changed (generator only).
- **Phase 2 bug found and fixed**: the logic step set every non-logic part's output to 0 each tick, so a lifter's Busy flickered and its
  power draw was never counted. `ConstructLogic.cs` now keeps a lifter's output as it keeps a Router's (one line).
- **Window tool** (all under the existing `lifter` step; the step-name line is unchanged): `lifter line [TIER] [N]` (an arm, an
  Assembler set to a recipe that uses Gear Sets, a stocked Piece Rack and a pad round it; N lines 25 m apart), `lifter hold NAME` (a
  piece at the line's Assembler's out as if made), `lifter base [TIER]` (an arm with a Battery and two Switches welded on its plinth by
  the attach tool's own ghosts, one wired to the Battery as the mains, one to the arm's Run; a `lifter line` after it is built round
  that arm), `lifter flip mains|run`, `lifter set liftMode|carryHeight V`, `lifter points`, `lifter reset` (the step's cost counters).

## Changes outside the lifter files (one hook each)

No `ConstructLogistics*.cs` file was changed: phase 4's carries need nothing of the network map; phase 5 does.

- `ConstructAssembling.cs` `Leave`: two lines at the top (an arm serving the out holds the piece; full, OutputBlocked).
- `ConstructExtruder.cs`: before the plain cut's `Cut`, a block that hands the piece to the out for an arm (same bookkeeping as a cut).
- `ConstructLogic.cs`: a lifter's output kept (the bug above).
- `ConstructWorld.cs`: `ConstructLifters.Step(this, query)` (a press bay's put needs the world query).
- `PieceRackParts.cs`, `LifterParts.cs`, `Constructs/Assembling.json` (generated), `Net/ServerBrowser.cs`, `App/Program.WindowShot.cs`
  (inside the `lifter` case).

## Tried by hand (Proving Ground, `--spawn-at 140,0,-100`, a creative world, `--save-dir` to a temp folder)

- **Host, a Workshop arm with no link laid**: the arm found its 4 points (the Assembler's out and in, the rack, the pad). It carried
  Gear Sets from the rack into the Assembler one by one (5 went in, its recipe's room), left the rack's two Square Beams where they were
  (storage to storage is never done), and carried two Square Beams put at the Assembler's out (unwanted there) onto the Lay-down Pad.
- **Host and joined client** (`--net host:60417` and `join:127.0.0.1:60417`; port 60000 was taken by another session's run): the client
  heard the carries with the piece by name, the arm's state and its move count matched the host's (7 at the end), and its screen showed
  Working. Pad 2 Square Beams, Assembler 5 Gear Sets on the host.
- **Phase 3's wiring, in the powered base**: mains off froze the arm mid-carry (NoPower, Busy 0, the body drawing 2 W), mains on
  finished the carry; the Run switch off held the arm (Idle, RunOff); Carry height 2.5 gave the next move a 2.5 m rise; Works 1 (Station
  only) stopped carries ("no station"), Works 3 Off; Busy 1 while moving, Moved counting; the body draws 1104 W while the arm moves
  (its 1100 W and the switches) after the fix, 4 W before it.
- **Tick cost, six Plasma arms working** (six lines, 26 carries): the lifter step 3.8 us a tick on average, worst 0.41 ms over 787
  ticks after the first ticks (before `lifter reset`: worst 3.7 ms, the first ticks' JIT and first point finds). The whole server tick
  was not measured.
- `--gen-parts all --check` and `--check-construct-parts` pass; self-test sections run by name: "Compound collision" and "No z-fighting"
  pass (the pads and the arms changed).

## Not tried or not built

- **A press bay's put and the Extruder's held cut** (code paths only: the test line has no press with a die or an Extruder with
  stock); a Panel Former's put; the Alarm from a refused put; a pad fed by a conveyor.
- **Not built (the rest of phase 4)**: a pad taking in a loose piece set down on it (the station's `TakeDropped` made shared); the reach
  view's red points with their reasons ("too heavy", "out of reach"); a held piece drawn at the machine's out (it is invisible until
  picked) and saved (a save drops it: not written; a piece in a hand is saved as before and finds a taker after the load); a former's
  preset part held for an arm; a station's hold or a terminal's tray as a want; the "no clear way" check.
- **Phase 5** (the network): a lifter as a node of `LogiMap`, Routers sending toward its pick points, orders and chains through it, a
  station's large parts by pad.
- Pictures (none looked at), `--look-report` on the pads, saves with a piece in a hand mid-carry between points.

## Choices made (each can be changed)

1. **Points on any construct in reach**, not only the arm's own (the design said its own construct): an arm placed beside machines works
   with no weld, as arms already serve stations on other bodies.
2. **Storage to storage is never done**: a rack's piece moves only to a want, so two racks in reach never trade pieces.
3. **Store priority, then the nearest**, among racks and pads (pads are not preferred).
4. **A machine's out holds 8 pieces of one kind** for arms, then the machine waits.
5. **Down reaches the arm's own floor**: the tiers' `down` grew (the design's 1 m proposal could not reach a rack on the same ground).

## Questions for the user (each a choice with a recommendation, to ask together)

1. **Which constructs an arm serves.** A (recommended): any in its reach (built here). B: only the construct it is welded to.
2. **A piece waiting at a machine's out for an arm.** A (recommended): draw it there and save it, next phase step. B: leave it invisible
   until picked, and dropped beside the machine on a load.
3. **Where an unwanted piece goes.** A (recommended): the rack or pad with the highest store priority, then the nearest (built here). B:
   pads before racks, whatever the distance.
4. Phase 3's two questions (a player who stands in an arm's path holds it up; a piece over a player's head pushes them) were built on
   their recommendations; an answer B to the first would change only the wait in `ConstructLifters`, not this phase's carries.
