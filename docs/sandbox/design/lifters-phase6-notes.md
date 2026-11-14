# Arms and cranes phase 6: notes (2026-11-14)

Branch `claude/lifters-6-656d68`. The design is `arms-and-cranes.md` (3.4, section 9 row 6, the user's answers in section 12); phases 5 and 5b
are `lifters-phase5-notes.md` and `lifters-phase5b-notes.md`. **Protocol 310** (new part files move ids; `LiftWhy` gains `NoRail`,
`NoFarRail`, `RailsShort` at the end). Hand orders, the Showcase and the cab are phases 7 and 8, not here.

## What was built

- **Parts** (the part kit's `LifterParts.cs`, `--gen-parts lifter_parts`; `Constructs/Parts/Lifters/Cranes`; section "Bridge cranes" in
  Joints and Motion > Cranes and lifts): **Crane Rail** Medium 4 m, Large 16 m, Capital 32 m (a runway I-beam one cell across with a steel
  rail on top, hazard edges; `"craneRail": true`; a hidden full-cell box is the collider and carries the sockets) and **Medium, Large and
  Capital Crane Bridge** (the lifter part, `kind: bridge`: a drive and power cabinet with its screen, bolted to the outer side of a rail by its
  +x socket). The design's table: span 8/32/64 m, drop 8/24/48 m, 30/300/3000 t, pieces 7.2 x 16, 28.8 x 64, 28.8 x 128 m, travel 2/1.5/1 m/s,
  hoist 1/0.75/0.5 m/s; handling 3/5/8 s and power 6/30/120 kW are mine. Recipes from `Assembling.cs` (Gear Sets, Bearings, a Control Unit).
- **The rail walk** (`ConstructCraneRails.cs`): on a rebuild of a body with a bridge, its sections of the bridge's class in the bridge's frame
  (parallel within 3 degrees), the near run (its top line at the bridge's `base` within 10 cm) grown end to end (2 cm), the far run a span
  across, and the reach box: across the span less a margin, along what both runs share (at most 300 m each way), from the hook's highest
  point (`depth` under the rails) down its drop. It goes into a copy of the def per bridge part, which `ConstructLifters.DefOf(c, part)`
  hands out; every lifter path (points, carries, station claims, map edges, pick pads, the push of players) reads that, so a crane does all
  an arm does with no other change. Nothing is kept for a body without a bridge; nothing is saved or sent.
- **The path**: hoist to the carry height, travel and traverse together in a straight line, lower; `Ticks` at the travel and hoist speeds
  (a move may last 10 minutes, for a Capital crane). Players are pushed by the piece as by an arm's (phase 3, unchanged).
- **Stops**: a crane not on a rail, with no far rail, or whose rails no longer reach an end of the move it is in (a section taken out
  mid-carry) stops where it is with its piece in its hand, Alarm on, the reason on its look line and screen, and goes on when the rails
  are back. A chained order's row names it ("waiting on the Bridge Crane: it is off its rails").
- **Drawing** (`LifterRender.DrawCrane`): end trucks on both rails, a yellow double girder at the hook's place along the run, the trolley
  over the hook, two cable falls and an orange hook block; the reach view is the box's edges in amber.
- **Window tool** (under `lifter`, `LifterCraneProbe.cs`): `crane [Medium|Large] [N]` (the phase 5 press line, its arm set to Off, under a
  crane of two pinned runs 6 m up, one body), `cranepoints`, `cranecut` (a far-run section out), `cranecut pad` (once, while it carries, the
  near-run section over its put point), `cranes N` (N more Medium cranes with nothing under them).

## Shared files touched

`ConstructParts.cs` (one field, one key), `ServerBrowser.cs`, `Categories.json` (one rule), `Assembling.cs` and `Assembling.json`
(generated), `Program.WindowShot.cs` (inside the `lifter` case), `LifterRender.cs`. The rest is in the lifter files.

## Tried by hand (Proving Ground, creative, `--save-dir` to a temp folder)

- **Host**: the Medium crane over the press line found 5 points (the press's out and two bays, the rack, the pad) and carried laminate
  blanks from the rack into the press's bay and the pressed Buttons to storage: 6 carries, 3 Buttons on the pad, about 6.4 s a carry.
- **Joined client** (`--net host:60631`, `join:127.0.0.1:60631`, `--no-upnp`): it heard the carries, held the same crane state and read
  2 Buttons on the pad. Two later client runs on other ports could not reach the host at all; on 60631 again it joined.
- **A rail section removed mid-carry** (the near run's section under the bridge): the crane stopped holding its blank, "not on a rail",
  Alarm on, its move frozen at the same tick on host and client. A far-run section out shortened the shared run (-2 to 10 m); the
  carries inside it went on.
- **Cost**: 7 cranes and an arm, 332 bodies (300 loose pieces): the lifter step **2.1 us a tick on average, worst 140 us over 1801 ticks**;
  the push 26 us a player tick on average, 223 us worst (9 samples). First ticks of a run (JIT) up to 4.2 ms once. The rail walk runs only on
  a rebuild of a body with a bridge (128 sections at most, stack only).
- Checks: build passes; `--gen-parts lifter_parts` and `assembling` regenerate; `--check-construct-parts`: no REFUSED lines.

## Not tried, or not built

- **A crane fitting at a Building Station, and a crane in an order** (a chain or a station's large part through it): the code is the arm's,
  through the same def, but not run. The crane in the rig is a body of its own, and the logistics map's lifter edges are per body, so a
  crane carries for the network only when its rails are on the machines' body (as an arm's plinth must be).
- **Phase 5b's three leftovers** (an unpowered arm beside a powered Router; a station's large part with a conveyor and no Router; a pad
  reached only across an arm): not run this round (the rigs have no power source to switch one machine off with).
- Large and Capital cranes in a window (only built and checked); saves with a piece in a crane's hand (the `LIFT` block is the arm's).
- A bridge whose +x points away from the far run finds no far rail: it must be bolted on the run's outer side, facing across.
- Pictures were for my own checking only.

## Open questions (each with a recommendation)

1. **Columns.** A: (recommended) none of the crane's own; rails stand on whatever the player builds (the design's answer). B: a Crane Column part a class.
2. **A bridge on the inner side or turned.** A: (recommended) it finds its far run on either side by trying -x too (a small change to `Walk`). B: outer side only, as now.
3. **A crane on another body** (rails on a hall, machines on the floor body): A: (recommended) as arms: it serves points on any body for carries and stations, the map's edges only on its own. B: map edges across bodies (a bigger change to `LogiMap`).
4. **Handling and power** (3/5/8 s, 6/30/120 kW, mine): A: keep. B: the user's figures.

STATUS: phase 6 built to its stop point on the press line; station fit, orders through a crane and phase 5b's leftovers not tried.

## After review (2026-11-14)

- **Carry height**: a crane's rise was clamped to its `up` (0), so it carried at the higher end's height. Now its Carry height runs to its
  drop (default: all the way, capped at the hook's top). Tried: a blank's hand rose from 5.8 to 2.1 m under the rails' top mid-move.
- **Rails too short** tests the piece's middle, as its reach was (a fit's place: its origin, as the plan's).
- **Pushes are nudges**: a shortest way out longer than `MaxEscape` (0.75 m) no longer moves the player; the lifter waits for them (Alarm
  after 10 s). Tried with a crane's small piece lowered onto a player (stood on the put point, then penned): nudged twice, no pin; a piece
  big enough to need the wait was not in the rig (not tried).
- **Drawn while its hand is near**: culled by the nearer of the cabinet and the piece; a loaded crane's piece rests on its hook (`RestOf`);
  the station panel's Fitters line counts cranes ("Bridge Crane", at its slower of travel and hoist).
- **The rail walk** runs only when the rails' or bridges' places change (`CraneKey`), not on every shape refresh (an Extruder's step).
  Not timed on a large body.
- **An order through an arm** (verification 4, item 7): the press's Buttons stayed in its own tray. The arm now lifts from a press's or an
  Assembler's output tray at its out by the same rule (a want first, else storage). Tried: `lifter net Industrial; logi order Button 2`:
  2/2 Buttons in the terminal's tray, 4 carries. `lifter net` now stocks the Button's own blank (10 x 10 x 10 cm laminate plate).
