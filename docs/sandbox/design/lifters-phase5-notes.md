# Lifters phase 5: notes (2026-11-13)

Branch `claude/lifters-5-289724`. The design is `arms-and-cranes.md`. This branch finishes what phase 4 left (an arm loading a
press's bays, Lay-down Pads taking pieces in), tries phase 4's two untried paths, and builds the first step of the design's phase 5
(the network): **pick pads**, the user's "pick pads for larger building areas" (phase 2's answer 3). **No wire change, no new parts:
the protocol stays 300.**

## What was built

- **A press's output is picked** (bug fix, `ConstructLifterCarry.MadeBy`): phase 4 asked the press's rows, which only a lathe has
  now (a press works by sized dies), so an arm never lifted what a press made. Now a press's made piece is one of its die's presets
  or a part pressed from its die's key (`Pressed Part (die.pattern ...)`).
- **A Lay-down Pad takes in a loose piece set down on its deck** (`ConstructLifterPads.cs`, new): about once a second, a one-part
  body at rest whose middle is over the pad's hold and whose lowest corner is on the deck (within 15 cm) goes into the hold as a
  conveyor's delivery would, at most 4 a second a world. Not a part with a store, not a rack, not a lifter, not a damaged part (it
  would come out whole), nor one the pad or its stock target refuses. The pad is marked by `storage.pieces.open` (the part kit's
  `PieceRackParts.cs`; parsed into `ConstructPartDef.IsLayDownPad`). The world keeps a list of the bodies that have a pad
  (`ConstructWorld.PadBodies`), stamped on the `Construct` (`PadStamp`, its own: a host's drawing scans the same world with
  `server: false`), never a record per body (the performance lesson of 2026-11-13).
- **Pick pads**: a Lay-down Pad among an arm's points is a pick pad for the stations that arm serves. A station step whose part the
  pad holds is claimed from the pad (from the hold first when the arm reaches the hold and it has the part) and fitted
  (`ConstructStation.LiftOffer`, `ConstructLifters.Claim`, `TakeFromPad`). The station counts its arms' pick pads' pieces as held,
  but never more than the unfitted steps of that kind one of those arms reaches (`PickPadHolds`; worked out once a look for all
  kinds, 512 steps looked at most), so a pad piece no arm can fit is ordered as before and the build never waits on it.
- **Window tool** (under `lifter`): `lifter press [TIER]` (an arm, a Press with a Button Die and 40 copper in its hopper, a Piece
  Rack of 4 laminate plate blanks, a pad, nothing linked); `lifter drop blank|NAME` (a loose piece let fall over the line's pad);
  `lifter pickpad` (a pad behind the newest arm, stocked with what the station's job still needs); `lifter points` also prints the
  pad as the take-in sees it.

## Changes outside the lifter files (small)

`ConstructParts.cs` (one field), `ConstructStorage.cs` (the `open` key), `Construct.cs` (the pad stamp), `PieceRackParts.cs` and
the two pad files (generated), `Program.WindowShot.cs` (inside the `lifter` case). `ConstructStation.cs` is untouched: its
`LiftHolds` call already reaches the lifter files.

## Tried by hand (Proving Ground, `--spawn-at 140,0,-100`, creative, `--save-dir` to a temp folder)

- **Press line, host**: the arm carried laminate blanks from the rack into the press's bay 1, the press pressed Buttons and dropped
  them at its out, the arm lifted each onto the storage in reach; repeated for every blank (6 carries in 150 s).
- **Host and joined client** (`--net host:60623`, `join:127.0.0.1:60623`, `--no-upnp`): the client heard 4 carries, the arm's state
  matched, and its pad held the pressed Buttons by key (`Pressed Part (button.button plate 30x30x50 laminate)`).
- **Pad take-in**: a blank dropped over the pad, a pressed part a machine dropped there and a loose Engine lying where the pad was put
  all went into its hold. A first version also took a loose Loader Arm standing at its edge (after a load it is no longer anchored):
  fixed by the deck test and by never taking a lifter.
- **An extruded piece in a hand at a save**: saved with a laminate blank in the hand (Carry 30/241), loaded in a fresh process: the
  arm came back Stuck holding the blank, put it into the press bay, and the pressed Button went to storage.
- **Pick pads**: the Production Showcase's station building the Buggy, an Industrial arm at the window tool's slot 2 (it reaches no
  point of the hold) and a pad behind it with 22 of the job's pieces. The arm fitted 18 parts from the pad; the station ordered
  what the pad lacked; 24/26 after 110 s, the last two waiting on Suspensions the Showcase's Press can't make with its die (not
  this branch's).
- **Tick cost**: after the first ticks, the lifter step (with the pad take-in) 2.9 us a tick on average, worst 0.96 ms over 914
  ticks with a take-in and a press put; 16.8 ms once in a run's first ticks (first use).
- Checks: build passes; `--gen-parts all --check` passes; `--check-construct-parts` has no REFUSED lines; sections run by name, all
  pass (0 failures, 106 s): Construct gun recoil, Construct actuators, Compound collision, Interaction sweep, Press stock, Machine
  tier looks, Extruder, Assembling, Construct storage. No section covers lifters by name.

## Not tried or not built

- **A chained order credited when an arm puts a piece into a machine**: read in the code, not run. `Put` calls
  `ConstructLogisticsPlan.Arrived` for an Assembler, a press bay or a Panel Former, matched by body, machine and kind as an untagged
  conveyor delivery is. But no chain can be routed through an arm yet (the planner doesn't know lifters: the network phase), so a
  chain is credited only when an arm happens to put its kind into its machine. Not faked with a hand-made chain entry (the planner
  drops one with no root order).
- A client's player setting a piece on a pad by hand (only the host's drops); a pad fed by a conveyor; the pick pad on a client
  (the fit is the station's own, which phases 1 to 3 tried on clients); two arms sharing one pick pad; a save with pieces on a pick
  pad mid-build.
- The station's own boom still fits only from the hold: a pad piece whose step only the boom reaches is ordered again (by design
  here, see question 2).

## What is next (the design's phase 5, the network)

A lifter as a node of `LogiMap` (its point pairs as edges), so a chained order or a station's order can be routed through it and
a Router sends toward a pick point; then the station's large parts asked for and delivered **to a pick pad** in a fitter's reach
(the design's stop point: a part longer than the hold, ordered, delivered by lifter and fitted, nobody touching it). Phase 6, the
Bridge Crane, after that.

## Questions for the user (each a choice with a recommendation)

1. **What a pad takes in.** A (recommended, built): any loose single part resting on its deck that a rack takes, except parts with
   a store, lifters and damaged parts. B: only pieces (extrusions, pressed and cut parts), never catalogue parts.
2. **Does the station's own boom fit from a pick pad?** A (recommended, built): no, only arms do; a piece only the boom could fit is
   ordered into the hold. B: the boom reaches pads in its bay's footprint too.
3. **Damaged parts on a pad.** A (recommended, built): left lying (a rack holds kinds, so it would come out whole). B: taken in at
   full health.
