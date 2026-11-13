# Lifters phase 3: notes (2026-11-13)

Branch `claude/lifters-3-a91f9d`. The design is `arms-and-cranes.md`; its section 9 makes phase 3 **pushing players** (3.6, the
user's answer 2). Carrying between machines (`LiftMap`, points, the Lay-down Pad) is phase 4 there, so it is not here.
Protocol **296**.

## What was built

- **Moves on the mover clock.** A move is now a pure function of the server tick, as a map mover's pose is: `CLift.Start` (the
  server tick it began), `Held` (ticks it has waited) and `WaitFrom` (the tick it is stopped at, 0 while it runs);
  `ConstructLifters.AgeAt(l, tick)` gives ticks into the move on either peer. The server keeps `Age` from it at each 10 Hz step;
  the drawing poses at `ConstructLifters.DrawTick` (`Game.MoverRenderTick`, set by Program each frame); a client's prediction
  poses at its command's tick. Phase 1's age-on-arrival and the conveyor-clock drawing are gone.
- **Waits.** A move that waits freezes at `WaitFrom` on both peers and, when it goes on, adds the wait to `Held` (`Freeze`,
  `Thaw`). Two things freeze it: a player it would pin (below) and **no power** (which now stops the drawing on a client where
  the host's arm stopped; phase 2's client jumped to the move's end).
- **The push** (`Source/Constructs/ConstructLifterPush.cs`, called from `Game.SandboxPlayer` after the constructs' own block, on
  the server and in a client's prediction and replay, at the command's tick; bots and seated players are skipped). The piece in
  a hand is an oriented box at its true size. A player it overlaps is stood on it when its top is within 0.4 m of the feet,
  else pushed out sideways by the smallest move (three heights tested), or out another side up to 0.75 m when that one is
  blocked. A push into the map, a construct or another piece in the air is not made: **pinned**. The test judges only what
  the push makes worse (what the player already stands in is not a wall).
- **Pinned: the arm waits.** On the server a pin calls `ConstructLifters.Pin`: the move freezes where it was, the look line
  says "waiting: a player is in the way" (`LiftWhy.Player`), and at each 10 Hz step it looks whether every player is 0.1 m
  clear of the stopped piece (`ConstructWorld.PlayerSpots`); then it goes on. After 10 s it reads "stuck: a player has been in
  the way for over 10 s" (`LiftWhy.PlayerStuck`) and raises **Alarm**; it still goes on by itself when clear. A cancel while
  it waits is handled as before (the claim check runs first). The lift word is sent when the wait starts and ends, never per
  tick.
- **Wire** (protocol 296): the lift word sends `Start`, `Held` and `WaitFrom` as u32 each in place of u16 age and u16 held
  (6 bytes more a lifter, about 59).
- **Window tool** (all under the existing `lifter` step, so the step-name line is unchanged): `lifter stand [me|others]` and
  `lifter pen [me|others]` (host: the players where a carry that has just begun will pass its piece, on clear ground; the pen
  adds four anchored Crate Stacks round them), `lifter free` (host: every player 4 m north), `lifter joined` (host: waits for
  a joined player), `lifter walk` (this peer's player out of the tool's flight); the printout adds pushes and pins on this
  peer, what was last in the way, the wait, and an IK readout (below).

Shared files, a line or two each: `Game/GameSandbox.cs` (the call), `App/Program.cs` (`DrawTick`), `Net/ServerBrowser.cs`,
`App/Program.WindowShot.cs` (inside the `lifter` case, one field). No logistics hook was needed (phase 3 carries nothing new).

## Tried by hand (Proving Ground, `--spawn-at 140,0,-100`, the Production Showcase's station, the Buggy, one Scrap arm)

- **Host, pushed:** a player put on a 0.75 m beam's path was pushed 42 times as it passed, about 1.1 m aside; the arm never
  stopped; the build went on.
- **Host, pinned:** a player in the path of a 2 m beam where the push met something solid: pushed once, then pinned; the arm
  stopped at tick 141, 78 of 151 ticks in ("Carry Player"), stayed there, turned "PlayerStuck" with the Alarm after 10 s, and
  when the player was moved out (`lifter free`) went on (Held 676 ticks), finished the carry, and the station finished the
  Buggy 26/26 (9 parts by the arm). The player was never moved by the arm while pinned.
- **Host and joined client** (`--net host` / `join:127.0.0.1`): the client's player put in a path, in the pen: the server
  pushed it 23 times and the client's own prediction pushed it too (72 counts with replays); the arm did not wait (a way out
  of the pen was found); the host and the client then had that player at the same feet, 160.71, 1.02, 7.41. Same carries
  heard on both.
- **With `--lag=150` on the client:** the same run; the client's prediction pushed its player (177 counts with replays), and
  host and client again had it at the same feet (160.71, 1.02, 7.41) at the end.
- **The elbow, host and client** (the printout's IK readout: the wrist's wanted distance over every 3rd tick of each move, posed
  as the drawing poses it, and the elbow at the move's middle tick): for the moves both peers printed, the elbows agree within
  5 mm (the wire's cm rounding; e.g. host 0.383, 3.199, 1.727 and client 0.382, 3.200, 1.725). Over 16 moves on the client and
  the host's, no sample was clamped: the wrist stayed 1.72 to 3.87 m from the shoulder, inside the links' 0.70 to 4.41 m, so the
  arm is never stretched past straight or folded through itself. A picture was not looked at.
- **Tick cost, four arms working** (Scrap, Workshop, Industrial, Plasma at one station, host): the lifter step 2.1 to 5.1 us a
  tick on average, worst 1.0 to 1.8 ms (ticks with a fit, which rebuild the body: the station's own cost, as in phase 2); the
  push 0.97 us a player tick on average with nobody near a piece, 5.6 us in the run where a player was pushed 30 times, worst
  single calls 0.6 and 2.2 ms (probably the first call's JIT; not confirmed). Before the fix below, a pinned player cost 38 us a
  tick (the room checks against the 471-part Showcase every tick). The whole server tick was not measured this phase.
- **A player pinned in the bay held the build**: with four arms and the player standing pinned for 110 s, nothing was fitted
  (the arm waited, and the station's own boom did not fit either; whether the station has its own player rule was not looked
  into). The design's rule; question 1 below.

## Not tried

- **Phase 2's wiring:** the arm's Works and Carry height settings, the Run, Busy, Alarm and Moved ports and its power draw in a
  powered base with a switch. `lifter place` stands the arm on its own foundation, outside the base's power, and welding it
  into the Showcase's network needs a socket step the window tool does not have; the power freeze is a code path only.
- A client pinned (the client case found a way out); a piece rising under a player (the step); two pieces at once; a player
  crouching or prone (the server's clear test uses a standing body); several players at once.
- The look line and the Alarm lamp with my own eyes (no pictures).

## Choices made (each can be changed)

1. **The push runs after the constructs' block in the Sandbox's player step**, not inside `ClassicMover` and `SweptMover` as
   the design said: constructs (and their pieces) are met there, on both movers, with the command's tick already at hand
   (`_sepAtTick`). Nothing in the movers changed.
2. **A pin freezes the move at the pinning command's tick** (both peers pose it there) rather than backing it off a tick: the
   piece may overlap the pinned player by one tick's travel (5 cm for the Scrap arm, 20 cm for the Plasma).
3. **Out another side, 0.75 m at most**, before calling it a pin: a player beside a wall slides along it rather than stopping
   the arm; further than that would be a teleport.
4. **What the player already stands in is not a wall** for the pin test (seen: a bay floor plate tested as "in a construct").
   A player already inside the map can then be pushed further in; the mover's own unstick takes them out.
5. **The wait ends when everyone is 0.1 m clear of the stopped piece**, tested at 10 Hz with a standing body.
6. **A stopped piece pushes nobody** (a wait for a player or for power): the player it waits for walks out of it, and a pin
   costs no room checks a tick. A player can walk into a stopped piece.

## Questions for the user (each with a recommendation)

1. **A player who stands still in an arm's path on a shared base holds it up forever.** A (recommended): keep it (the design's
   rule; the Alarm says so after 10 s and the base's permissions are the answer). B: after the Alarm, the piece passes through
   that player as it does through walls.
2. **The piece over a player's head.** A piece passing just over a standing player's head pushes them out sideways. A
   (recommended): keep it. B: a piece that meets only the head stops the arm instead (it waits, as for a pin).
3. **Phase 2's three questions are still open** (the arm's machine screen, a spare part after a load, the Industrial arm that
   cannot reach the hold); this phase changed nothing they depend on.
