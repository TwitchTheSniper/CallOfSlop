# Arms and cranes phases 7 and 8: notes (2026-11-14)

Branch `claude/lifters-7-be7af9`. The design is `arms-and-cranes.md` (section 5 "By hand", section 9 row 7, the user's answer 6); phase 6
is `lifters-phase6-notes.md`. **Protocol 312** (`CActKind.Lifter` at the end; each lifter in `CBodyKind.Lifts` gains its orders and, phase 8,
a steered byte; the Lifter Cab part moves ids). Phase 8 (the cab) is its own section below, and with it the design's phase list is done. The player's guide is production.md "Lifters" (Bridge Crane, Hand orders, players in the way, store parts on
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

## Phase 8: the cab

Built (design 3.7, section 9 row 8; the user's answer 6):

- **The Lifter Cab** (`LifterParts.Cab`, `--gen-parts lifter_parts`; Constructs/Parts/Lifters/LifterCab.json; recipe from the Assembling
  generator's seat rule): a seat 0.75 x 1.5 x 1 m, a chair on a swivel in a yellow cab with windows on three sides, a desk with two
  sticks, hazard bands round its foot; `"lifterCab": true` (one field in `ConstructParts.cs`). It steers the nearest lifter on its own
  construct, found on each peer when the body's parts change (`FindCabs`: one cab a lifter, no link).
- **Manned** (`ConstructLifterCab.cs`): the lifter finishes its move, then the cab has it (`Steered`, Why `Manned`): a station claim or a
  hand order isn't started while it is manned. At the 10 Hz step the hook moves by the cab's keys (W/S along the sitter's look, A/D
  across it, Space/Ctrl up and down) at its speed (a crane: travel, hoist), clamped to the reach (an arm: its ring, height and links; a
  crane: its box) and kept off the ground. The keys come from the seat loop (`CabInput`), since the logic tick clears a seat's keys
  before the lifter step.
- **F** (`CActKind.Lifter`, op Grab, the cab's body and part; the server checks the player sits in it): an empty hand takes the nearest
  loose single part within 1.5 m across under the hook (its payload and size, "too heavy: 603 kg, it carries 500 kg", "only a single
  piece: that is a construct"), else a rack's, pad's or press's/Assembler's tray piece near the hook; a full hand puts into a point near it
  that takes it (`PutPiece`, shared with a carry's end now), else sets it down loose (`DropLoose`, its health and paint kept). The result
  is on the look line ("took it", "nothing to take under the hook", "put it in", "set it down").
- **E** (roll right, in a seat) gets out; a piece in the hand goes on by the Routers' rule (`Redirect`), else waits with it (Alarm).
- **Wire**: a steered lifter's move is a straight 10 Hz step (From to To over 6 ticks) in the ordinary lift word, sent while it moves
  (about 60 bytes a word, 10 a second); `Pose` eases it on every peer, so a client's push and drawing use it. A steered byte joins the word.
- **Seated view**: the look line names what it does and the keys; the reach view is drawn and the hook's shadow (a dark square 2 cm over
  the first thing below it) while the player sits there (`LifterRender.CabBody`).
- **Window tool**: `lifter cab [arm|crane]` (host: a cab joined to the rig's arm or crane), `lifter sit` (host through the server's Use; a
  client sends the use key's action after the host's `bring Lifter Cab`), `lifter cabstate [bait NAME]` (bait: that part loose under the
  hook); steering through the real keys with `hold MoveForward 2` and `key Interact`.

Tried by hand:

- **Host, arm**: sat in, W moved the hand 1.4 m, Ctrl lowered it, F took a loose Plate Steel 0.5 m, Space and A carried it over the pad,
  F put it into the pad (the pad held it), E got out (Idle). A Medium Column 4 m: "too heavy: 603 kg, it carries 500 kg".
- **Host, crane**: Ctrl lowered the hook from 1.1 to 5.4 m under the rails, where the ground stopped it; F took the plate; Space and W, then
  F with nothing under it set it down loose.
- **Joined client, crane** (`--net host:60631`/`join`): the client sat in (host's `bring Lifter Cab`), lowered the hook 6.4 m and moved it
  (host and client read the same hand to the centimetre); F found nothing under the hook (the hook had gone under the ground: fixed
  since), a second F took an Engine lying near, and E left it to the crane, which carried it on by its own rule (1 moved). One earlier
  client run's held keys never reached the seat (the hook stood still on both peers); the next two runs worked. Not looked into.
- Build passes; `--gen-parts lifter_parts --check`: up to date; `--check-construct-parts`: no REFUSED lines (the Lifter Cab: OK).

Not tried: a cab with `--lag`; a piece in a steered hand pushing a player (the push poses it as any carry); a cab and a crane on a slope;
the cab seen in a window by anyone; a save with someone in a cab (nobody is seated after a load; a held piece saves as any held piece).

Open questions (each with a recommendation):

5. **Which lifter a cab steers.** A: (recommended, built) the nearest on its own construct, one cab a lifter. B: the linking tool links a
   cab to any lifter (the design's wording), for a cab on another body: a `Route`-kind link and a refusal text more.
6. **E to get out** (roll right): A: (recommended, built) as the design says. B: F gets out as in every other seat and the cab's grab
   moves to G (Seat hook).
7. **The station panel's bill "on the Lay-down Pad"** (phase 5b's leftover, design section 4): not done here: `ConstructStation.cs` and
   `UiStation.cs` belong to the logistics builder this round. Recommend the next station round.

## After review (2026-11-14)

- **One pick rule** (`ConstructLifters.PickWhy`, in the pads' file): every way a lifter picks up a loose body (a carry from a machine's
  out, a hand order's out, the cab's F) takes only a loose single part (`Construct.Loose`) that holds nothing a hand would lose: no
  store contents, rounds, fluid, text, queue or changed settings, else it is refused with its reason ("holds something: empty it first").
  Its health and paint go with it, and only a piece as new (`AsNewInHand`) becomes a count in a rack, pad, tray or station hold
  (`PutFor`, `OrderPut`, `PutBack`); a damaged or painted one goes to a machine or is set down as itself. Tried: a Drill Module with
  10 iron ore under a crane's hook, F: refused, the module left lying.
- **A cab taken off while manned**: the step now runs the cab's check for a lifter still `Steered` whose cab went, and gives it back.
  Tried: `lifter cabstate cut` mid-steer: "its cab was left", Idle.
- **The hook's floor** is the ground under the hook (`ConstructLifters.Altitude`: a planetoid's altitude and up, a terrain's floor under
  the point through its voids), and the cab's moves are flat in the lifter's own frame. Not tried on a planetoid.
- **A cab never drives**: linked to a Driving Module as its drive seat, its keys still move only the hook (the seat loop skips driving).
- **The Welder's join** on a lifter part comes before its panel.
- **The client run whose keys never arrived**: a window that loses focus lets the mouse go, and a client builds its movement buttons
  only while the mouse is held, so the window tool's `hold` in the unfocused client window sent nothing. A player at their own window
  is not affected; `hold` now takes the mouse first. Tried: the failing sequence again on a joined client: lowered, grabbed, set down, out.
- `--gen-parts all --check`: up to date; `--check-construct-parts`: no REFUSED lines.

STATUS: phases 7 and 8 built: hand orders and the cab on host and client, the Showcase's crane works its line; the design's phase list is done.
