# Arms and cranes phase 7: notes (2026-11-14)

Branch `claude/lifters-7-be7af9`. The design is `arms-and-cranes.md` (section 5 "By hand", section 9 row 7, the user's answer 6); phase 6
is `lifters-phase6-notes.md`. **Protocol 312** (`CActKind.Lifter` at the end; each lifter in `CBodyKind.Lifts` gains its orders). The
cab is phase 8, not here. The player's guide is production.md "Lifters" (Bridge Crane, Hand orders, players in the way, store parts on
a pad) and "Production Showcase" (the crane bay).

## What was built

- **Hand orders** (`ConstructLifterOrders.cs`): a lifter keeps up to 8 orders (kind, count, done, from point, to point, why it waits).
  An idle lifter looks at them about once a second before any work of its own, in every Works mode (Off stops only its own work), and
  starts the first one it can: the piece taken from a rack or pad (`StationTake`), a machine's out (the loose piece there, else its tray)
  and put to any other point by that point's own rule (a rack or pad: `RackCheck` and stock targets, so store to store and pad to pad
  are allowed; a press bay, an Assembler, a Former, a tray: their wants). A put refused at the end goes on by the Routers' rule and the
  order says "the put point doesn't take it now". Waiting reasons: none there yet, refused, out of reach, can't carry it, part gone.
- **The action** `CActKind.Lifter` (add, cancel by id, clear), checked as a finger on a console (`ConstructEdits.LifterAct`): reach to
  the lifter (a crane's cabinet: the hands' reach plus its depth, drop and 2 m, so from the floor under it), defended, who may configure;
  the points must be among the lifter's points on the server now.
- **The panel** (`UI/UiLifter.cs`, `App/Program.SandboxLifter.cs`): F on an arm, or on a crane's cabinet up to 40 m along the look when
  nothing nearer takes F, opens it. Left: the pick points and what each holds (clickable kinds); right: the put points; a count; Order;
  the orders with Cancel and Clear. The point list is this peer's own (`PanelPoints`, 2 Hz). It joins `MenusOpen`, the Esc chain and
  `SandboxLocalReset`, and closes when the lifter goes or the player walks off.
- **Wire and save**: the orders and the carry's order id after each lifter in the lift word; in the save, the `LIFT` block's second
  half (i32 n, 18 bytes each; a point on the lifter's own body as body 0). An older build skips it.
- **The Production Showcase's crane bay** (`ProductionShowcase.CraneBay`): new floor x 0 to 22, z -14 to -22; a Medium Crane Bridge on
  two runs of four Medium rail sections, 8 m apart, on Medium Columns (4 m + 2 m) standing on Adapters Medium to Small; under it an
  unwired Extruder (metal angles), a Press with a Bracket Die and a Lay-down Pad; Carry height 1 m; a sign. The blueprint is now 86 x 22 m.
- **Window tool** (under `lifter`): `panel [arm|crane]`, `order FROM TO N KIND`, `orders`, `pad2` (a second pad in the crane rig),
  `stationcrane` (a Medium crane over the station's bay, its arms Off), `press largepad`, `drop [pad2] [loaded] NAME|blank [CM]`;
  `edges` and `cranecut` find the Showcase's crane; the player lines print the ride frame.

## Phase 6's leftovers, tried

1. **A large piece lowered where a player stands**: it was wrong. A player standing on any construct (here a prop, a pad, a floor slab)
   is in its ride frame, and the push returned at once for them, so a Medium crane lowered a 2 m plate straight through the player and
   put it. Now a lifter waits for a rider its piece reaches (`PinRiders`; the rider is never moved). Tried: Player, then PlayerStuck
   (Alarm) after 10 s, the feet unchanged to the centimetre; after `lifter free` it put the plate. A player on the ground was already right.
2. **A pick near a run's end, origin off-centre**: a 4 m blank from a second pad carried and came back clean (the pick spot was not at
   the run's end, so the case did not show). By reading, the empty hook's way back ran to the piece's origin and was tested against the
   runs; now its end is clamped onto the runs and its start (where the last put was tested) is not tested again. Not reproduced.
3. **The rail walk's cost on a large body** (the Showcase, 616 parts, its crane bay's Extruder running, 40 s): it was wrong too. The key
   hashed the body's part count, which the Extruder's growing piece changes, so the rails were walked 26 times in 40 s (137.9 us average,
   2.2 ms the first). Without it: **1 walk at load (2.2 ms, its first call), 13 us after a section was cut; the key check 3.9 us average,
   162 us worst over 278 rebuilds.** Lifter step 4.8 us a tick average, worst 3.7 ms (a fit's tick, with JIT) over 3237 ticks.
4. **A crane at a Building Station**: a Medium crane over the Showcase's bay (its two arms Off) fitted 2 of the Swing Arm Buggy's parts in
   50 s (a Fuel Tank in its hand at the end). **An order through a crane**: the Showcase's map has the crane bay's 4 edges (Extruder to
   Press 15.9 m, Extruder to pad, Press to pad, pad to Press); no order crossed it, since nothing past the pad leads to a terminal.
- **Store parts on a pad** (the standing answer): a Lay-down Pad takes a store part as the hold does (`RackCheck`), and a loose one
  only when it holds nothing. Tried: an empty Drill Module set down on a Large pad went in; one with 10 iron ore in it lay there,
  "holds something: empty it first" (logged once a body). A Medium pad refuses a Drill Module as too big: it is 2.2 m tall and the pad
  stacks 1.85 m. Also fixed: the pad's "on the deck" test read the part's bounds' corners, so a Drill Module on its side (its bit down)
  was never on the deck; it reads the solid boxes now.
- **The outs take turns**: on the Showcase the press's brackets never left its out, since every look found an angle at the Extruder's
  out that the press wanted first. An idle lifter's look now starts after the out it last carried from. In 55 s: 2 angles into the
  press, 1 bracket onto the pad.

## Tried by hand (Proving Ground, creative, `--save-dir` to a temp folder)

- **Host, arm**: the press rig with the arm Off: an order of 2 laminate blanks, rack to pad: both carried, the order gone.
- **Host, crane**: a 2 m plate pad to pad (the push test above); a 4 m blank from the second pad.
- **Joined client** (`--net host:60631` / `join:127.0.0.1:60631`, `--no-upnp`, the host's `bring Loader Arm`): in the crane rig with the
  arm and the crane both Off, the client ordered 1 blank at the crane (rack to pad) and 1 at the arm: the pad went 0, 1, 2 on host and
  client alike.
- **Save and load** in a fresh process: an order of 9 with 2 done came back with 2 done, went to 3 and then waited "none there yet".
- **The Showcase**: its crane works its line in a window run of 55 s (above). `--check-construct-parts`: no REFUSED lines; build passes.

## Not tried

- The panel driven by real mouse clicks (the window tool sets the same selection and presses the same button the next frame).
- A crane fitting a station's large part from a pick pad, and a terminal's order whose way crosses a crane.
- A hand order to a press bay or an Assembler on a client; Large and Capital cranes' orders; `--lag`.
- A pick at a run's very end (item 2) reproduced before the fix.
- The Showcase's crane bay seen in a window by anyone (its columns and pad look); the self-test sections.

## Open questions (each with a recommendation)

1. **Orders in Off.** A: (recommended, built) hand orders run whatever Works says; Off stops only its own work. B: Off stops orders too.
2. **A crane's panel from the floor.** A: (recommended, built) F on the cabinet from anywhere under its rails (a pendant). B: only from
   the cab (phase 8) or within the hands' reach.
3. **A refused put.** A: (recommended, built) the piece goes on by the Routers' rule and the order waits. B: the piece goes back to where
   it came from.
4. **Repeat orders** ("keep this press fed"): A: (recommended) not now; the lifter's own rule already does it. B: an order that never ends.

STATUS: phase 7 built: hand orders on host and client at an arm and a crane, the Showcase's crane works its line; three phase 6 faults fixed.
