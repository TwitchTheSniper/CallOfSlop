# Lifters phase 5b: notes (2026-11-13)

Branch `claude/lifters-5b-37e647`. The design is `arms-and-cranes.md` (section 5, the table's phase 5 row; section 9, phase 5);
phase 5's first half is `lifters-phase5-notes.md`. This branch finishes the design's phase 5 (the network). **Protocol 309** (a lift
word's put point may now be a Crafting Terminal's tray, `LiftPointKind.Tray`, a new value at the end); the map's lifter edges are
worked out on each peer from the parts and send nothing. The Bridge Crane (phase 6) is not here.

## What was built

- **A lifter as a node of the logistics map** (`ConstructLifterNet.cs`, `ConstructLogistics.Build` and `Column`). On a rebuild of a
  body with lifter parts, each whole lifter set to Auto or Carry only gives an edge for each pair of its points on that body from a
  rack, a pad or a machine's Piece out to a put point (a rack only to a pad: storage is never carried to storage), 128 at most, with
  its metres (shoulder to each end) plus a handling (2 m) and its piece width as the edge's narrowest. Its ends join one network.
  A way may go on from a lifter's pick rack or pad by the lifter only (`LiftPick`, its own distances in the column), so a Router's
  next hop toward a press, an Assembler, a Panel Former, a terminal's tray or a pick pad goes to the conveyor into the arm's pick
  point. A way that crosses a lifter is marked (`ViaLift`) and is never chosen for overflow. The edges are folded into the map's
  link hash and into a terminal's network key; a terminal's network walk crosses them (`Joined`), so it reaches the machines an
  arm serves; a store joined by a lifter alone is not its stock (it would be claimed to be collected by hand).
- **A Router sends to a pick point**, and from there the arm carries on: phase 4's rule over its points, now with two more wants:
  a **terminal's tray** (an open order of that kind still waits for it, less what rides or is carried there; the put counts for the
  order as a conveyor's delivery does) and a **pick pad** for a station's large part. A rack's piece goes to a want only if nobody
  has claimed it (`Available`).
- **Chains and fetches through an arm**: a chained order's blank fetched from stock rides to the Router, to the pick rack (or the
  pad), the arm puts it into the press's bay and the chain is credited there once (`Arrived` on the put: the rider's tag was lost at
  the rack, so nothing was credited there); the press's Buttons go from its out to the terminal's tray by the arm. Pieces waiting at a
  pick point for the machine count as riding for the chain (`AtPicks`), or it sends more.
- **A station's large parts** (too big for its hold, `RackCheck` 3): the station orders them as before; its riders are not sent to
  the hold but to a Lay-down Pad that one of its arms fits from (`PadWant`: the build lacks the kind, less what is on its arms' pick
  pads, rides or is carried there; tag the order, or `PadClaim` with no order); a fetch from stock rides there; an arm carries one
  there from a machine's out or a rack; a player may set it down on the pad. A piece landing on the pad counts for the order as one
  landing in the hold does (`PadArrived`). The arm fits from the pad (phase 5a).
- **Pads in `ConstructReserve`**: what the arms' pick pads hold that the build counts as held is claimed for it (owner the station's
  hold, `OwnerId` `PadClaim`), kept to the count at 2 Hz and changed only when the count changes; released on cancel, failure and done.
  A terminal's own claim sweep skips them.
- **Fixed on the way**: a pad's take-in crashed the server on any loose part that is not a tank (`ConstructFluid.Held` read slot
  -1); a piece resting on the pad's rails (up to 50 cm over its floor) was refused as "not on the deck"; after a load mid-build the
  station ordered what its pick pad held (its first orders ran before the lifters' scan, and arms loaded with a piece were skipped).
- **Window tool** (under `lifter`): `net [TIER] [pad]` (one body: an arm, a Press with a Button Die, a pick rack, a pad, a Crafting
  Terminal, a Router and a stock rack of 6 blanks; `pad`: the Router feeds the pad too), `edges`, `steps` (each unfitted step as the
  newest arm sees it), `bigparts`, `bigrack NAME`, `pickpad empty`, `crowd N`; `points` prints the take-in's reason per loose body and
  the costs of the scan, an arm's look and a put. `logi net` lists the lifter edges and marks a way "by a lifter". `inv letgo`: this
  peer's player drops what it holds (the Drop action), for a joined client.

## Changes outside the lifter files (small)

`ConstructLogistics.cs` (the map's lifter fields, `Build`, `Column` and `Onward`; in the rest one line each: `WantAt` for a pad and a
station's large part, the two want loops take a pick pad, `Assign` skips a lifter way for overflow, `Open` for a pad's rider),
`ConstructLogisticsPlan.cs` (`Riding` counts `AtPicks`, a world parameter), `ConstructCrafting.cs` (`KeyOf`, the network walk, a store
joined by a lifter only, the claim sweep skips pad claims, a station's large fetch rides to the pick pad), `ConstructConveyor.cs`
(`Deliver` at a pad), `ConstructStation.cs` (pad claims in `Orders`, released in `CancelOrders` and `Finish`), `ServerBrowser.cs`.

## Tried by hand (Proving Ground, `--spawn-at 140,0,-100`, creative, `--save-dir` to a temp folder)

- **Chain through an arm** (`lifter net Industrial`, an order of 2 Buttons): the fetch rode with the press as destination, the
  Router sent it to the pick rack ("way 1 ... by a lifter"), the arm put 2 blanks into the bay and 2 Buttons into the tray; the
  order was done with exactly 2 (after the `AtPicks` fix: 4 carries, 4 blanks left in stock; before it, a third blank was sent).
- **A pad fed by a conveyor** (`lifter net Industrial pad`, the Router's way to the pick rack cut): the blanks rode onto the pad,
  the arm took them to the press, the order was done.
- **Joined client** (`--net host` and `join`, `--no-upnp`): the client's map had the same 8 lifter edges and ways "by a lifter",
  it heard every carry, and its tray read 2 Buttons; for the station build below it saw the build done and all 20 carries.
- **Large parts at a station** (the Production Showcase, "Heating Branch 3 m": two Heating Mains, both too big for the hold, an
  Industrial arm at slot 1, a pick pad stocked): both fitted from the pad, done 2/2. The pad's piece was claimed ("reserved 1").
- **Two arms sharing a pad** (the Buggy, Industrial arms at slots 0 and 1, one pad): 10 fits each, done 26/26; 7 claimed on the pad.
- **A save mid-build with 10 pieces on the pick pad** (`station saveworld`, then `station loadworld` in a fresh process): the pad
  came back with its pieces, the station waited for the same 3 parts as before the save, the arms fitted 10 and the Buggy was done
  with the hold as before (after the fix: before it, 2 extra parts were ordered).
- **A part set down on the pad**: a Swept Wing dropped on it (host) went into the hold (after the two take-in fixes); a **joined
  client** standing on the pad dropped a Square Beam from its hands with its own Drop action (`inv letgo`): the host took it in and
  the client's pad read it.
- **Cost, 424 bodies** (400 loose pieces round the Showcase, a build under way, an arm and a pick pad): the pad take-in 13 us
  average, 23 us worst (once a second); the pick pad count 7 us, 25 us worst (2 Hz while building); the lifter scan 10 us, 49 us
  worst; an idle arm's look 12 us, 111 us worst; the whole lifter step 2.5 us a tick on average, worst 136 us. On the press line: a
  put 0.59 ms average, 3.6 ms worst (a press bay's own take-in, as a conveyor's delivery costs); the map's build with 8 lifter edges
  0.04 ms, 5 columns 0.007 ms (1.7 ms the client's first, JIT).
- Checks: build passes; `--check-construct-parts` has no REFUSED lines; sections run by name, all pass (0 failures, 142 s): Construct
  gun recoil, Construct actuators, Interaction sweep, Nudge tool, Sandbox physics gun, Press stock, Extruder, Extruder network,
  Assembling, Assembling network, Construct storage, Compound collision.

## Not tried, or not built

- The window tool's `key SandboxDrop` on a joined client left the part in its hands; the Drop action sent directly (`inv letgo`)
  worked. The key path itself is not this branch's and was not looked into.
- **A Router sending a station's large part to a pick pad**: the code path is the press chain's (`WantAt` with `PadWant`), tried for
  pieces bound for a press, not for a station's order; no shipped machine near the Showcase's station makes a large part.
- **Most large parts are Foundations** (`lifter bigparts`: 45 of 12 steps or fewer that fit the bay), which the hands' loose rule
  (`Construct.Loose`) and so a pad's take-in exclude; a dropped Heating Main anchors itself. They reach a pad by conveyor, an arm,
  or a fetch. A large storage part (a Drill Module) is refused by every pad (`RackCheck` 2) and so can't be built from one.
- The station panel's bill doesn't yet say "on the Lay-down Pad" (design section 4).
- The "Crashed Jet" blueprint's steps lie about 4 m under its body's origin, out of every arm's reach (seen with `lifter steps`;
  the station fits them itself). Not this branch's.

## Questions for the user (each with a recommendation)

1. **Foundations on a pad by hand.** A (recommended): leave them out (a foundation anchors where it is set down, the hands' rule);
   they come by conveyor, arm or fetch. B: a pad takes in a dropped foundation too.
2. **A large store part** (Drill Module, Ammo Container) for a station: A (recommended): a pick pad takes one that is new and empty,
   as the station's hold does (a pad's piece goes only to a fit). B: such a build stays the station's own, by hand.
3. **Carrying into a terminal's tray** (built here so a terminal's order served by an arm arrives): A (recommended, built): an arm
   fills a tray for an order that asked for it; B: trays only by conveyor.

## After review (2026-11-13)

- **Works setting changes the map** (a setting is no rebuild): the map keeps each lifter's Works setting and is made again when one
  changes (`NoteModes`, `ModesChanged`). Tried with `lifter set liftMode`: the host's Router lost all 8 lifter edges at Off and had
  them back at Auto; a joined client read 8, then 0, then 8.
- **An arm that can't work holds a chain up by name**: pieces at its pick point count as riding only if nobody claimed them
  (`Available`), and the chained order's row says why they don't move ("waiting on the Loader Arm: no power", its Run held off, its
  Works setting, stuck, broken; `PickBlocked`). Tried with `timed` (the rig unpowered): 2 blanks at the pick rack, 4 left in stock,
  the row read "waiting on the Loader Arm: no power".
- **A station's large fetch goes to a pad only when the pad takes the piece and the conveyor's way reaches it** (straight, through
  Joins or a Router with a way there; `PickPadFor`, `WayReaches`); a chute or no way: as before. Tried: the Showcase's Long Piece Rack
  (no route to the station) with an arm and a pad joined into the station's body: the order said "collect it there" and the piece
  stayed in stock. Not tried: a conveyor straight into the hold with a pad on the body, and the case where it does reach the pad.
- **Cost on the biggest body with a terminal** (the 474-part Showcase with an arm joined in): its lifter edges 36 us to work out (a
  distance cut and a body-frame test first); kept when nothing they follow changed (`EdgeKey`): the whole map made again 18 us; a
  terminal's network look 0.6 us (`HasLifter` kept on the body until its parts change).
- The pick pad count's pre-scan tick and its pads list live on the world (`PadScanTick`, `PadSeen`), not in statics.
- **Merged with main at protocol 308** (this change is 309). A Router's `CouldWait` (main's fix-ups) counts no store reachable only
  across a lifter (overflow is never carried store to store), so a piece whose only place lies across an arm is set off beside the
  Router. Tried (`lifter ride 300 shut`: the pick rack takes nothing as storage, only the pad across the arm would): set off at once,
  Alarm on, with the arm working and with it set to Off; a joined client read the Alarm. An unpowered arm with a powered Router was
  not tried (the rig has no power source); the rule doesn't read the arm's state. Main's network takers skip pads (`RackStore.IsPad`);
  a pick rack's count taken from under an arm moves `AtPicks` (by `Available`) and the pad claims (kept at 2 Hz) with it.
