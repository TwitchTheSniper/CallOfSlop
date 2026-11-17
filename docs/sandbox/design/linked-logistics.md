# Linked logistics: factories on several constructs and worlds, joined by cargo ships (design)

The user (TwitchTheSniper), 2026-11-16: split the logistics order system across constructs, so a large ship can be built
from several factories. This is an extension of the smart logistics network (`smart-logistics.md`) and of sub-blueprints
(`sub-blueprints.md`, being built on `claude/sub-blueprints`). It replaces neither: one construct stays one network
(smart-logistics answer Q1), and a link joins two such networks for **planning only**.

The user's decisions (binding):

1. A **Transmitter / Receiver** pair links two constructs' logistics networks for **orders and stock only**: orders, stock
   and sub-jobs (sub-blueprint jobs too) travel over the link and the two networks plan as one. **Goods never teleport.**
2. Goods between linked factories are **hauled by player-built cargo ships**. A factory loads a ship until it is full, then
   the ship flies the full load (fuel is real and burnt).
3. **Progression in three levels, unlocked by parts built:** (a) manual: the player flies, the link says what the far end
   needs and the near end loads it; (b) standing routes: a Route Computer on the ship takes a route in the command view;
   (c) automatic: a Logistics Dispatcher at the factory sends idle registered ships when enough cargo waits.
4. **Loading:** a Cargo Dock (factory) with a matching Cargo Port (ship) for loose material and small pieces, and cranes
   and arms for big pieces and submodules into cargo volumes.
5. Small local drones are out: a later, separate design.

The production system is the user's own; this design leans on it throughout and changes none of its rules for one
construct. Names below are suggestions.

## 1. What it builds on

- **Network and planner:** one joined group a construct (`LogiMap`, `CraftNet`: 64 machines, 64 stores); the 2 Hz planner's
  roots and `ChainEntry` (256 a world, 64 a network, depth 6); claims in `ConstructReserve` (1,024, never saved).
  `ConstructLogistics*.cs`, `ConstructCrafting.cs`, `ConstructReserve.cs`.
- **Docks:** a Docking Clamp is a cargo volume with a `dock` block: it pulls a slow craft in, holds it, refuels and charges
  it, and with `"chute": true` moves a chute load between bins across the two bodies (`ConstructCargo.cs`, big-systems.md).
- **Holds:** Storage Volumes (bulk or parts, 40 m a side, 4 a construct); Cargo Volumes (frozen boxes that carry bodies).
- **Lifters:** Loader Arms and Bridge Cranes with pick and put points; sub-blueprints adds a body carry.
- **Ship orders:** `NavKind` GoTo, Orbit, Ground, flown by a ship with a working Flight Stabilizer; 64 a world, host-only,
  not saved (`GameNav.cs`, COMMAND.md).
- **Worlds:** one Game per planetoid someone is on (free travel). Inside one Game **every body ticks**, near a player or not
  (only lift detail and physics sleep go by distance). A planetoid nobody is on for 30 s is saved and **closed: its machines
  stop**. Ships in space live in the host's main Game, which always runs (FREE-TRAVEL.md, `GameTravel*.cs`).

So on one planetoid both ends are always live; across planetoids the far end may be closed (section 5).

## 2. What the player sees and does

**Level 0, the link (Transmitter + Receiver built).** The shipyard on Glacier has a Building Station building a frigate; the
smelting works on Bluestone has a Receiver, the shipyard a Transmitter, paired. The shipyard's Crafting Terminal list now
shows what Bluestone holds and makes, marked with a planet tag ("Steel Beam - 40 at Bluestone Works"). A frigate job
short of 140 Steel Beam claims or orders them there: "140 Steel Beam - at Bluestone Works: 80 ready, 60 being made - **needs
a ship**". Nothing moves yet.

**Level a, manual (Cargo Dock + Cargo Port).** The player lands a ship with a Cargo Port on Bluestone's Cargo Dock. The
clamp catches it and the dock's screen says "Loading for Glacier Yard: 34 of 140 Steel Beam, 1,200 of 4,000 steel". Routers
send claimed pieces to the dock; the dock passes them through the port into the ship's Storage Volumes; a Bridge Crane
puts big pieces in its Cargo Volume. The ship's card (command view) and its port's look line show the **manifest**. The
player flies to Glacier and docks at the yard's Cargo Dock; it empties what is for the yard; the yard's orders count it in.

**Level b, standing routes (Route Computer).** In the command view the player picks the ship, right-clicks a Cargo Dock
marker, **ROUTE**: load at Bluestone Works until full (or N minutes), fly to Glacier Yard, unload, return. It loops by
itself; its card: "ROUTE: loading at Bluestone Works, 72% full, leaves when full or in 6:10".

**Level c, automatic (Logistics Dispatcher).** Route-Computer ships docked once at a factory with a Dispatcher can be
registered to it. It sends an idle one when enough cargo waits for a linked destination, or the oldest line has waited too
long, and shows a fleet list: ship, state, load, fuel, ETA.

## 3. The parts

All are part-kit output (`Source/PartKit`, a `logistics_link_parts` generator), in the Tab inventory under Logistics.
Numbers are proposals for the builder to tune.

| Part | Size, mass | Power | Ports and settings | Recipe (rough) |
| --- | --- | --- | --- | --- |
| **Logistics Transmitter** | 1 x 1 x 2 m mast with a whip antenna, 120 kg | 200 W | Data out **Linked** (1 while its pair answers), **Waiting** (lines for a ship); settings: pair code (shown), name | Assembler tier 2: 4 Circuit Board, Router Antenna, 20 steel |
| **Logistics Receiver** | same mast, a ring on top, 120 kg | 200 W | Data out Linked; settings: pair code (typed), name, **Accept** (All, Stock only, Off) | as the Transmitter |
| **Logistics Relay** (both kinds as one part, a setting) | 3 x 3 x 5 m dish, 2 t | 2 kW | as above | Assembler tier 3: 12 Circuit Board, 2 Router Antenna, 200 steel |
| **Cargo Dock** (Medium; Large later) | an 8 x 8 m pad, 0.6 m deep, a clamp ring and a 2 x 2 m hatch in its middle, 6 t; anchors | 1 kW, 5 kW while it moves goods | factory side: Piece in, Piece out (conveyor), Chute in, Chute out; the clamp's own `dock` block (pull, settle, refuel, charge); a staging hold of 24 slots and 2,000 units of one bulk material; Data out Docked, Busy | Assembler tier 2: Docking Clamp Medium, 2 Power Board, 400 steel |
| **Cargo Port** (ship) | 2 x 2 x 0.4 m hatch ring, 80 kg | 100 W | ship side: Chute in, Chute out, Piece in, Piece out, joined by the ship's own links to its holds | Assembler tier 1: 2 Circuit Board, 40 steel |
| **Route Computer** (ship) | 0.6 m cube console, 40 kg | 150 W | needs a working Flight Stabilizer on the same construct; Data out On route, Loading, Fuel short; panel: its route | Assembler tier 2: 6 Circuit Board, 20 steel |
| **Logistics Dispatcher** (factory) | 1.2 x 0.8 m desk with a monitor, 300 kg | 300 W | needs a Transmitter or Receiver on its network; panel: fleet list, thresholds; Data out Ships out, Waiting | Assembler tier 3: 10 Circuit Board, Power Board, 60 steel |

**Unlocks are the parts.** Level 0 is having a powered pair; level a is a Cargo Dock and a ship with a Cargo Port; b is a
Route Computer; c a Dispatcher. Nothing else gates them, so a Creative player has all of them at once.

**Reach (proposed, Q2).** The small Transmitter and Receiver link ends **on the same planetoid** (the same Game), at any
distance. The Relay links ends **anywhere in the star system** (other planetoids, ships, stations in orbit). A ship jumped to
another system loses its links until it comes back. A link is not line-of-sight: radio is cheap here; the price is power and
the Relay's recipe.

**Pairing (Q3).** Each Transmitter is given a random `LinkId` (u32) when built, saved as a part setting, so it survives
copies, splits, travel packing and saves; a copy of a blueprint gets a new one. Its panel shows it as a six-letter code
("KX4-MRB"). A Receiver's panel takes a code; the host checks it and pairs (`LinkId` saved on the Receiver too). The code
is the permission: whoever can read the transmitter's panel can pair with it. One Transmitter pairs with any number of
Receivers (a yard fed by several works); a Receiver pairs with one Transmitter. A network may hold up to 4 link ends.
**Direction:** orders and sub-jobs go from the Transmitter's network to the Receivers'; stock reports come back. So a
Transmitter sits at the consumer (the shipyard), a Receiver at each supplier. Two-way trade is two pairs.
Unpairing: clear the code, or either part broken, removed or unpowered for 10 s (section 7).

## 4. How the networks plan as one

### 4.1 The remote view

Each link end keeps, in the host's **link ledger** (section 5), a **report** of its network, refreshed at 0.5 Hz while its
Game runs:

| In a report | Bound |
| --- | --- |
| Offered stock: kind (by key, as `ConstructStorage.WriteDef`), free count (held less reserved), at the network's docks or anywhere | 128 kinds |
| What it makes: kinds whose maker accepts the order (`WorkOrders.Refusal == null`), and for each, the best queue length | 128 kinds |
| Building Stations with Sub-jobs Linked (4.4): idle or not, bay size | 8 |
| Dock state: docks, ships docked, staging fill | 4 docks |
| Its Game open or asleep, and when the report was made | |

A Receiver end's report covers its whole network; a Transmitter end sees the union of its Receivers' reports. Reports are
copies (fixed struct arrays), never references into another Game's bodies (FREE-TRAVEL.md "Rules for new code").

### 4.2 Planner moves, extended

The four moves of smart-logistics 5.2 gain a remote step each, taken **after** the local one, so a local answer always wins:

| Move | Local (today) | Remote (new) |
| --- | --- | --- |
| 1 Count | `short = N - in D - riding - reserved - asked` | also less **in transit to D**: manifest lines on ships bound for this network for D's root (4.3), and remote lines already posted |
| 2 Stock | reserve free stock in the network's stores | still short: post a **remote claim** `{link, kind, count, for root, for dest}` on a Receiver whose report offers the kind. The Receiver's side turns it into a local `ConstructReserve` owned by its **Cargo Dock** (owner = the dock's store handle, id = the claim's id) and assigns the pieces `Dest` = the dock (the Routers carry them there). Bulk is not claimed by unit (smart-logistics Q3): a bulk need posts a **bulk call** (material, units) and the far dock's staging bin fills by chute while the call is open |
| 3 Make | queue a work order at a local maker | still short and no local maker: post a **remote order** to a Receiver whose report makes the kind. On that network it becomes a **root** owned by the Receiver (a terminal-like owner; class and age stamp are the far root's, so it queues fairly), Dest = its Cargo Dock. Its chain is planned there by the ordinary moves |
| 4 Explain | "nothing on the network makes it" | "nothing on the network or linked networks makes it"; and the new waits: "at Bluestone Works: needs a ship", "Bluestone Works is asleep (nobody there): 3 orders wait for it" |

Remote lines are capped: 64 a link, 128 a world. One step posts at most 8 (as chain entries). A remote line is answered by
the far end on its own next planner step, so a remote level costs one more step, not more work in one.

**Choosing a supplier.** With several Receivers offering a kind: one on the same planetoid first, then the one with the
most free stock, then the shortest queue. Never split one line across suppliers (fewer manifests, fewer trips).

### 4.3 Transit and manifests

A **manifest** belongs to a ship (the construct with a Cargo Port; stored on the port part, so it moves with the ship's
parts). A line: `{link id, line id (claim or remote order), kind, count loaded, count delivered, dest network (link end),
dest store or station, use id for a submodule}`. At most 64 lines a ship. A ship's hold may also carry loose cargo with no
line (the player's own); nothing plans with that.

| State of a line | Counted at the destination as |
| --- | --- |
| Claimed, at the far dock's staging hold or riding to it | remote, "ready at Bluestone Works" |
| Loaded on a ship | **in transit** (move 1), with the ship's ETA when it is on a route ("coming on HAULER 2, about 6 min") |
| Unloaded at the destination dock | ordinary stock; the line closes, its reservation becomes local (the root's) as it lands in the dock's staging hold |

Transit time is not planned against (no deadlines): it is shown. A line loaded but on a ship that has stopped (no route,
parked, no fuel) for 10 minutes shows "stalled on HAULER 2" in the Fault colour; it is not re-ordered (goods never teleport,
and ordering again would double them): the player moves the ship or cancels the line (its goods then become loose cargo).

### 4.4 Sub-blueprint jobs over a link (extends sub-blueprints answer 2)

Answer 2 (takers: stations on the same construct that the main station's network reaches) stays the default. The extension
is a third value of the station's Sub-jobs setting:

| Sub-jobs | A use with no taker on the root's own network... |
| --- | --- |
| Off | is never taken by this station |
| Auto (today) | is taken by this station if it is on the root's network |
| **Linked** (new) | as Auto, and this station may also take uses dispatched over a link to its network |

The root's dispatch (sub-blueprints section 3) looks at local candidates first, exactly as today. Only when none is free and
the root's network has a Transmitter whose Receivers' reports list an idle Linked station whose bay fits, the use is offered
there **before** the 30 s self-build fallback fires; the 30 s timer pauses while a remote offer is pending (at most 10 s for
the far end's next step). A remote sub-job builds as any sub-job; its body is "done - waiting for a ship" and its delivery
is a manifest line with the use id: a crane or arm puts the body in a docked ship's **Cargo Volume** (frozen, so it rides),
the ship flies, and at the root's dock a lifter picks it from the volume and carries it to its spot (the sub-blueprints body
carry with a pick point in a docked ship). The big fit and its checks are unchanged; a body that arrives damaged is the
"repair in the bay" case (answer 5). With no lifter that reaches the dock, the by-hand fallback holds (free the body at the
dock, any player moves it). A running build is not changed by any of this: a station must be set to Linked by the player.

## 5. The link ledger and worlds that are asleep

The **link ledger** lives in the host's main Game (it always runs, and holds space): one entry per `LinkId` with both ends'
reports, the remote lines (claims, orders, bulk calls, sub-job offers), and a **mailbox** per end. Each Game posts its ends'
reports and its new lines to the ledger at the end of its update and reads its mailbox at the start of its next (in memory,
copies, main thread: the same hand-off as `MeetLeaveSend`). Same-planetoid links use the same path, so there is one code path.

**An end whose Game is closed** (a planetoid with nobody on it) is **asleep**: its report stays as last made, marked asleep
with its age; lines posted to it wait in its mailbox (64 at most) and apply when its Game opens. Its machines make nothing
meanwhile, as today. The waiting side says so ("Bluestone Works asleep since 14:02: 2 orders wait"). This keeps Karl's rule
that nothing is simulated that isn't running.

**A route ship arriving at an asleep world** (proposed, Q5): the host **opens the planetoid for the visit** as it does when a
player lands (`OpenPlace`), hands the ship over (`TravelPackShip`, as an empty ship; a ship waiting at the edge already keeps
a place open), and the ship flies down, docks, loads or unloads, and leaves for real; the place closes 30 s after it leaves.
The machines there run while it is open, which also drains its mailbox. At most **2 planetoids opened by route ships** at
once beyond those players are on; a third ship holds at the sky's edge ("waiting for a landing slot"). The alternative, a
coarse hand-off at the sky's edge with fuel charged by an estimate, is in the open questions.

**Keeping a factory running** without anybody there (Q6) is not proposed by default: a Dispatcher could keep its planetoid's
Game open while it has open linked lines, at the cost of that Game's tick.

## 6. Ships, docks and routes

### 6.1 Docking and the exchange

A Cargo Dock is a Docking Clamp (the same `dock` block: pull, settle, hold, refuel, charge) with a staging hold and a
**bridge**: while a ship is held and one of its Cargo Ports lies within 1.5 m of the dock's hatch, the dock and that port
exchange goods, one chute move a second each way (one part, or a load of 160 bulk; `ConstructRoutes.ChuteMove` between the
two stores, as the clamp's bin chute already does across bodies). Nothing crosses as a conveyor rider: conveyors stay one
construct's.

- **Loading:** pieces bound for a manifest line ride to the dock (`Dest` = dock) and land in its staging hold; the bridge
  passes them to the port; the port's own links (chute or conveyor on the ship) carry them to the ship's Storage Volumes and
  racks. Bulk fills the staging bin by chute and crosses the same way.
- **Unloading:** the reverse: the ship's holds feed the port, the bridge feeds the dock, the dock's Piece out and Chute out
  feed the network, and Routers take pieces to their roots.
- **Big pieces and submodules:** a lifter whose reach holds the docked ship's Cargo Volume treats it as a put point while
  loading and a pick point while unloading (lifter phase 5's points; the volume is frozen so what is put stays). The
  manifest line is written when the lifter's put completes.
- **Fuel and charge:** the dock refuels and charges as a clamp does, from the factory's tanks and batteries.

### 6.2 Full load, timeout and payload

A ship is **full** when any of these holds: every hold the port reaches is 95% full (by its own measure: a Storage Volume's
usable box, a rack's slots); its **payload limit** is reached; nothing more is waiting at this dock for any destination on
its route. The payload limit is worked out by the Route Computer: the most mass its drive can lift off the strongest-gravity
world on its route with 20% spare thrust (`Construct.Mass`, the DRIVE row's thrust). So a heavy-world route makes smaller
loads, which is the gate SURVIVAL.md already wants ("a costly lift-off ... needs a powerful ship").

Leaving rules, set per route: **Full** (wait for full), **Full or timeout** (default, 10 min; 1 to 60), **Now**. Level a has
no rule: the player leaves when they like, and the dock's screen says how full the ship is.

### 6.3 Fuel

Burns are real (`SpaceThrust`, the autopilot's lift), as a pilot's are. Before leaving a dock the Route Computer estimates
the leg: dV from the ship card's BURN (lift-off hover time on gravity worlds, transfer, landing) and adds 25% spare. Short of
it: it waits, refuelling at the dock ("fuel short: needs about 2,400 l, has 1,500"), and Data out **Fuel short** is 1. A
ship that runs dry on the way is a stalled ship (NoDrive, as today) and its lines show stalled. Each trip's fuel used is
kept on the Route Computer (last 8 trips) and shown on the card and the Dispatcher: the cost of a route is visible.

### 6.4 Route orders

`NavKind.Route` (at the end of the enum): the ship flies its legs by today's orders: from a sky, `Ground` to above the next
dock if it is on the same planetoid, else climb, `Orbit` the destination body low, enter its sky (free travel hand-over),
`Ground` to above the dock. New **Dock** phase: hover 30 m over the dock, turn to its heading, sink at 2 m/s until the
clamp's pull catches it (the clamp draws it in, as today). **Undock**: the clamp lets go and the ship climbs on its hover.
A route has 2 to 4 stops; each stop has load and unload rules. A pilot touching the flight keys suspends the route
(hands on the controls win), and **RESUME** on the card takes it up again.

Unlike today's orders, routes are **saved** (on the Route Computer part, a `ROUT` block) and survive map changes; a route
ship counts against `Game.MaxNavOrders` (64) only while it flies a leg. At most 32 route ships a world.

### 6.5 The Dispatcher

1 Hz, for each linked destination: `waiting` = mass and volume staged or claimed at this factory's docks for it. A
registered ship (at most 8 a Dispatcher; registering needs it docked here once, same owner or faction) is **sent** when it is
idle and `waiting >= threshold x its free hold` (default 75%), or the oldest waiting line is older than the timeout (default
15 min). Sending gives the ship a one-off route (load here, unload there, come back) with the leaving rule Full or timeout.
Idle ships park on a dock or a pad named in the Dispatcher. A Dispatcher at a **consumer** may also send its ships to fetch
from a supplier's dock when that dock reports waiting cargo for it and the supplier has no Dispatcher.

## 7. When it goes wrong

| Case | What happens |
| --- | --- |
| Link broken (a part shot off, unpowered 10 s, code cleared) | Its remote claims release; remote orders not started cancel at the far end; started ones finish into the far dock as stock (no waste); manifest lines already loaded keep their destination, the dock there takes them as ordinary stock. Rows: "link to Bluestone Works lost" |
| One end asleep | Section 5: reports go stale but stay, lines wait in the mailbox |
| Ship destroyed with cargo | Its holds spill as storage does (bundles, loose parts, logged); its lines close as **lost**; the destination's move 1 no longer counts them, so they are ordered again on the next step ("12 Steel Beam lost with HAULER 2: ordered again") |
| A player takes cargo from a hold | The line shrinks by what was taken (contested-store rule as for any store); the destination asks again for the shortfall |
| Dock blocked (something on the pad, a ship that isn't moving away) | The arriving route ship holds 40 m over it ("dock busy"); after 2 minutes it tries another Cargo Dock of the same network, else waits |
| Wrong ship (no Cargo Port, port not over the hatch, not registered) | Held, refuelled and charged as by a clamp; no exchange; the dock's screen says why |
| Pairing a Receiver on the Transmitter's own network | Refused ("that receiver is on this network") |
| Staging hold full and no ship | Pieces back up as any full destination does (smart-logistics Q7: they wait at their Router, marked stuck) |
| Ship hijacked or flown off by a player | Its route is suspended; its lines stay in transit, then stalled after 10 minutes |

## 8. Wire, saves, performance

**Protocol.** One bump per phase that touches the wire (the merger renumbers after sub-blueprints' 324):

- Link parts: pair code and name are part settings (no new bytes); Linked, Waiting are ordinary data outputs.
- `CBodyKind.Logi` gains a third list: per link end on the body, its remote lines (`u16 line, kind, u16 count, u8 state,
  u8 reason`, at most 64) and the far end's name and asleep flag, on change at most 2 Hz. Clients show remote rows from it;
  the reports themselves stay on the host.
- `CBodyKind.Manifest` (new, at the end): a ship's lines, on change and to a joiner.
- `NavKind.Route` at the end; the nav order message gains the route's stops (dock body, part, rules) and the leg and phase.
  `CActKind.RouteSet`, `CActKind.Pair`, `CActKind.Register` at the end.

**Saves.** In a body: `MANI` (manifest lines, kinds by name) and `ROUT` (route, stops by `LinkId` plus dock part tag,
state, last 8 trips' fuel), length-prefixed, written only with content; older builds skip them. In the main world save, a
`LINK` section: the ledger's lines and mailboxes and the last report of each end (so an asleep end is still known after a
restart). Reservations are not saved (as today: posted again from lines). A planetoid's kept save carries its bodies' blocks.
No new save file: `SavePaths.SaveFiles` and `SaveFolders` are unchanged. Blueprints carry the parts and settings, never a
`LinkId` (a stamped Transmitter gets a fresh one) or a manifest.

**Performance** (8 ms a tick on the 2-core box, no allocations in a steady tick, bounded per body):

Bounds: a report 0.5 Hz per end, spread by `LinkId`, from the network's cached lists (under 0.05 ms); the ledger hand-off
copies at most 4 ends a Game; the planner's remote steps sit inside its 2 Hz step, 8 new lines a step a network; the bridge
one chute move each way a second per docked ship; payload and fuel estimates on docking and on a change of hold mass; the
Dispatcher 1 Hz over 8 ships and 4 destinations; route ships cost today's nav work only while a leg flies (32 a world).
All tables are fixed arrays, pooled; names are made only when a reason changes. Measure with 4 links, 128-kind reports,
32 route ships and 2 opened planetoids, and the worst tick while a ship opens a planetoid (today's landing cost).

## 9. Phases (one builder, the whole feature; a WIP commit `[skip ci]` after each)

1. **Parts and pairing.** The five parts and the Relay from the part kit; `LinkId`, the code, pairing, unpairing, the
   Linked output; the ledger in the main Game with reports and the hand-off (same-planetoid and other-planetoid ends);
   the `LINK` section. The terminal list shows remote stock and makes, tagged.
2. **Remote planning.** Remote claims, remote orders (roots owned by the Receiver), bulk calls, transit in move 1, the new
   explain lines, link-broken handling, asleep ends and mailboxes. Protocol bump (`Logi` third list). Goods still go
   nowhere: rows say "needs a ship".
3. **Level a: docks and manifests.** Cargo Dock staging hold and bridge, Cargo Port, manifests (`MANI`, `CBodyKind.Manifest`),
   unloading into the network, lifter put and pick at a docked Cargo Volume, payload limit, the dock's screen and look
   lines, the ship card's CARGO row naming lines. Ship lost, cargo taken.
4. **Sub-jobs over a link.** The Linked setting, remote offers before the 30 s fallback, body lines and delivery
   (after sub-blueprints is on main).
5. **Level b: routes.** Route Computer, `NavKind.Route` with the Dock and Undock phases, the command view's ROUTE order and
   dock markers, leaving rules, fuel estimate and refuel wait, `ROUT`, opening an asleep planetoid for a visit (cap 2).
6. **Level c: the Dispatcher.** Registration, thresholds, fleet list, fetch runs.
7. **A showcase.** A blueprint pair (a small works with a Receiver and dock, a yard with a Transmitter and dock) and a
   small hauler with a port, a Route Computer and one Storage Volume, for trying it by hand on Glacier.

Tried by hand each phase on a host and a joined client (the `--window-shot` verbs where a picture checks something); no new
self-test sections (rule of 2026-11-03), any probe removed.

## 10. Open questions for the user (recommendation first)

1. **"Concord".** No part, ship or faction called Concord exists in the repo. **Recommended:** treat it as any player-built
   ship with a Cargo Port; if Concord is a ship you want built, a showcase hauler blueprint of that name in phase 7.
2. **Reach of the pair.** **Recommended:** the small pair on the same planetoid, the Relay anywhere in the star system,
   nothing across jumps. Alternative: one part, system-wide from the start.
3. **Pairing.** **Recommended:** a six-letter code shown on the Transmitter and typed into the Receiver (works across
   worlds, and the code is the permission). Alternatives: pick from a list of your faction's Receivers; or the linking tool
   (click one, then the other, the first end kept while you travel).
4. **Direction.** **Recommended:** orders flow Transmitter to Receiver only (the consumer holds the Transmitter); two-way
   trade is two pairs. Alternative: a symmetric pair where both ends may order from each other.
5. **A route ship arriving at a world nobody is on.** **Recommended:** open that planetoid for the visit and dock for real,
   at most 2 opened this way at once. Alternative: a coarse hand-off at the sky's edge (cargo moved through the ledger,
   fuel charged by an estimate), cheaper but not what the player would see.
6. **Remote factories with nobody there.** **Recommended:** they sleep (Karl's rule; orders wait in the mailbox and run
   when anybody, a route ship included, opens the world). Alternative: a Dispatcher keeps its world running while it has
   linked work, at that world's tick cost.
7. **Full load.** **Recommended:** default leaving rule "Full or 10 minutes", with full meaning 95% of the holds or the
   payload limit for the heaviest world on the route. Alternative: full by holds only, with no payload limit.
8. **Sub-jobs over links.** **Recommended:** a third Sub-jobs value, Linked, off by default; a remote offer comes before the
   30 s self-build and pauses its timer. Alternative: never send sub-jobs over a link; ship only parts.
9. **Cargo taken by a player.** **Recommended:** allowed, as any store (the contested rule); the line shrinks and is asked
   again. Alternative: holds on a ship on route refuse anyone but the owner's faction.
10. **Ship destroyed with cargo.** **Recommended:** lines lost and ordered again automatically. Alternative: the root waits
    with "lost on HAULER 2: order again?" for the player to confirm.

## The user's answers (2026-11-16)

These override the text above where they differ.

1. **"Concord"** was a slip for "constructed": any player-built ship with a hold and a Cargo Port hauls. No named ship class.
2. **Reach:** a Transmitter/Receiver pair links factories on one planetoid; a Logistics Relay links across the star system; nothing links across a jump.
3. **Pairing:** by the Transmitter's six-letter code typed into the Receiver.
4. **Direction: two-way.** One pair links both networks fully; each side can order from the other and supply it. (Not the one-way rule above: a pair is symmetric for orders and stock; the names Transmitter and Receiver only say which end shows the code.)
5. **A closed world is estimated, not opened.** A route ship arriving at a planetoid with no player on it does not open that world: its docking, loading and unloading are settled on the ledger (cargo moved by the manifest, times from the dock's rates), and the ship's leg there is simulated.
6. **Remote factories: estimated production.** While its world is closed, a factory's output is estimated from its machines' rates, inputs, power and orders, and credited to the ledger (and to its stores when the world opens). Bound the estimate (no output past its inputs or store room) and keep it cheap.
7. **Leaving conditions are the player's.** Per route stop, the player sets when the ship leaves: a time waited at the dock, a fill percentage of its whole cargo capacity, or an order completed; any combination, first one met. The defaults may be the design's (full = 95%, or 10 minutes).
8. **Linked sub-jobs:** yes, a station's Sub-jobs setting gets a value Linked, off by default; a linked station is offered a sub-job before the 30 s fallback, and the finished module rides a cargo ship.
9. **A player may take cargo** from a hold on a route; it is not ordered again: the order stays short until the player reorders.
10. **Cargo lost with a destroyed ship is ordered again automatically;** the wreck's cargo can still be salvaged.

## Queued next (user, 2026-11-16)

After the sub-blueprints and linked logistics builds are on main: **a linked logistics layer on the navigation screen** (the command view, docs/space/COMMAND.md). A separate layer the player turns on and off. It shows only nodes of currently linked logistics networks (paired Transmitters/Receivers and Relays, with the factories they join); unlinked networks and parts are not drawn.

Built on `claude/linked-nav-layer` (protocol 330): docs/space/COMMAND.md "The logistics layer".
