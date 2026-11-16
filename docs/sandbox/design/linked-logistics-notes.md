# Linked logistics: builder's notes (status)

Branch `claude/linked-logistics`, protocol 326. Spec: `linked-logistics.md` (the user's answers at its end win). What a player gets:
production.md "Linked logistics".

## Leftovers from the sub-blueprints review (done)
- `UseFrameRender`'s cache is keyed by body id: it is cleared when the world it was made for changes, and on `SandboxLocalReset`.
- A save made mid-carry writes a carried submodule at its carry's end (its spot): the hand's carry isn't saved, so a load never leaves
  it frozen in the air; the main station's next look takes it in its bay (`ConstructLifters.CarryEnd`, `CaptureInto`).
- A lifter set to Station only carries a body only to a station whose bay its reach takes in (`ConstructLifters.ServesStation`).

## Done
- Parts (part kit `logistics_link_parts`, recipes in `assembling`): Logistics Transmitter, Receiver, Relay (a small-class part, so it
  stands on Foundations), Cargo Dock (a terminal-like staging hold with Piece in/out and Chute in/out; it needs a Docking Clamp of
  the same construct: the clamp holds, the dock trades), Cargo Port (1 x 1 m), Route Computer, Logistics Dispatcher. `"logiLink"` block.
- Codes (24 bits, 6 of 16 letters), pairing by typed code (the panel), two-way pairs, reach (one Game; Relays across worlds), 4 ends a
  body, down 10 s unpairs; a copied end gets a fresh code (twin check).
- The ledger (static, the host's): ends and reports (stock 2 s, makes 10 s from the dock's catalogue), lines, asleep worlds (no post
  for 5 s), estimates for lines to asleep worlds, settlement on wake (32 units a second), LINK section in the main world save.
- Planning: terminal and station orders (`StepTerminal`, after local makers), chained inputs (`FindMaker`), `Place` takes a kind only a
  linked network has; supplier side is an order at its Cargo Dock with its claims under owner 0xFFF0; lost goods come off the order
  (asked again); taken goods don't.
- Docks: exchange 2 parts a second each way, manifests on the port (LLNK block, CBodyKind.Link), ship lost (port broken, wreck, or
  unseen 15 s in any open world), taken, stalled after 10 min, payload limit (NavDrive thrust, strongest world, 20% spare).
- Routes: same-planetoid legs flown and docked for real; other-world legs flown through space (an Orbit nav order) and settled on the
  ledger from low orbit; leaving rules per stop; fuel wait; suspend on pilot keys; ROUTE row and button on the command view's card.
- Dispatcher: registration on docking (owner or faction), thresholds, one-off runs (home, there, home), fetch from suppliers without one.
- Linked sub-jobs: Sub-jobs Off/Auto/Linked; offer before the 30 s fallback (10 s for the far end to start); remote build; the body is
  handed to the root when its ship docks at the root's construct or it is set down within 60 m of the root's bay.
- Showcase (`linked_logistics_showcase`): Linked Works, Linked Yard, Linked Hauler. Headless: `--linked-check`. Window verb `linked`.

## Tried
- `--linked-check` (all pass): pair, offers, order, dock fills by chute, clamp holds the hauler, load 4 / unload 4 into the yard's dock
  and chuted to the terminal (order got 4), ship lost then ordered again, cargo taken not reordered, Dispatcher registers and sends, a
  linked sub-job (Submodule Skiff's pod built at the works, handed over, welded), LLNK and LINK round trips, Relays across two worlds,
  an asleep world's stock estimated, a route stop there settled from orbit, the world's stores debited on wake.
- Window tool (Proving Ground, host; `linked` verb): spawn the three, pair through the panel's message, fill, order 3 at the yard:
  line posted, the works' dock filled by chute (3 ready, "needs a ship"); route started: the Docking Clamp's pull took the hauler in, it
  loaded the 3 (manifest, "Coming: 3 on ship 23" at the yard's dock), the leaving rule let it go.
- Self-test sections by name, all passed: Constructs, Construct storage, Sandbox body sync, Packets, Save folder, Construct configure,
  Sandbox Tab inventory, Sandbox inventory, Frame allocations, Assembling, No z-fighting, Mods, Sandbox cargo volumes, Interaction sweep.

## Not done / not verified
- Bulk calls (loose material over a link): not built; only catalogue parts travel (run-time pieces don't: their ids are this
  machine's).
- A route ship landing for real on another open world: other-world stops are settled from orbit (the hand-over of an unmanned ship
  between Games isn't there). A route ship in a planetoid's own Game climbing out to space: not tried.
- Cranes and arms putting into a docked ship's Cargo Volume as a put point (lifter integration): not built; a submodule goes into the
  volume by hand (physics gun) or a carry the lifter already does.
- Route flight (merge review, 2026-11-16): the hauler that "didn't lift" was overweight for four ducted fans (386 kg: its rotors
  saturated and it flipped) and, in the check, flat (its battery ran down in earlier waits; the showcase's works have no battery to
  charge it at the clamp). Lighter now (Port 20 kg, Route Computer 15 kg under the port); a departure engages the autopilot and lets go at
  once with a 4 m lift-off, and leaves the dock behind by distance before a clamp's pull retakes it. Headless (`--linked-check`) a
  Dispatcher run flew for real: off the works' dock by its rule, 120 m to the yard, down into its clamp, unloaded. The return leg is not
  reliable: as the yard's clamp let it go the hauler tipped over (a marginal rotorcraft near the ground). Not a bug found on main; a
  thruster hauler with margin would be the robust showcase. A trip to another planet on the trip planner: not tried.
- A joined client's view: not tried over `--net`.
- No measurement of tick cost at the design's sizes (4 links, 128-kind reports, 32 route ships).

## Merge review (2026-11-16, after main's trips between planets)
- Merged main; protocol 327 (both history lines). IsStore's comment moved out of the expression (lesson in .claude/rules/production.md).
  Route legs through space go through `Game.NavApply` (the command view's own check and reset of trip state); the route panel shows
  the trip phases (Short, Wait, NoDrive, between planets); a pilot taking the controls suspends the route; dropped report kinds logged.
- Sections run by name: Assembling, Assembling network, Refining network, Extruder, Inventory beacon, Construct storage, Construct
  configure, Packets, Sandbox body sync, Space launch, Frame allocations, Constructs, Save folder, Sandbox Tab inventory, Sandbox
  inventory, No z-fighting, Mods, Sandbox cargo volumes, Interaction sweep: passed. Mode switch reset failed on Player fields this
  branch doesn't touch (Uniform, RenderRideBody, RenderRideRel, RenderRideFeet: from main).
- Not done: the joined client's two-window run.
