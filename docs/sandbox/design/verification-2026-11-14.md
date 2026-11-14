# Verification run, 2026-11-14 (main at 40478f3f, protocol 309; no protocol change here)

The fourth verification: what merged on 2026-11-13 and 14, each area's "not tried" list tried through the window tool on the Windows
PC. Every run is a throwaway window run from `bin\Release\net10.0`:
`dotnet CallOfSlop.dll --window-shot OUT.png --save-dir %TEMP%\...\saves\NAME --no-splash --no-upnp --spawn-at 140,0,-100 [--blueprint "Production Showcase"] --do "..."`
(below only the `--do` script is given). Host and client: two windows over RakNet on 127.0.0.1, `--net host:PORT` / `--net join:127.0.0.1:PORT`,
the client started 25 s after the host. Scratch blueprints in `bin/` only (none committed): `VT Ammo Rig` (Square Beam 2 m + Ammo
Container on `+y0`), `VT Gas Rig` (the beam + a Fluid Tank set to hydrogen 100 %).

| # | Area | Result |
| --- | --- | --- |
| 1 | Production Showcase repair | **Verified** on host and client (line 2's rows 5 to 8 not read; line 1 reached only from another spawn) |
| 2 | Smart logistics fix-ups 2 | **Verified** on host (set-off pile, save while riding, rank); client reads the Alarm |
| 3 | Laser Cutter phase 6 | |
| 4 | Sized parts phase 6 | |
| 5 | Inventories phase 4 | |
| 6 | Extruder expansion | |
| 7 | Lifters phase 5b | |

## 1. The repaired Production Showcase: verified

- **Line 1 (Smelter, Sawmill, Foundry).** `wait 60; timed; touch Start @ Line Console#1; wait 30; panel Line Console#1` at
  `--spawn-at 140,0,-100`: the touch hit nothing (`note: <192.1, 0.0, -28.9> is outside Proving Ground's arena ...: the player stops at the edge`,
  `touch ray: ... no part`) and the line stayed `Line stopped 0/3`. **At the brief's spawn point Line Console #1 is past the Proving
  Ground's arena edge, so a player can't reach it.** At `--spawn-at 100,0,-100` (the showcase's centre 7.5 m higher, on the slope) the same
  script gave `Line running 3/3`, all three Running. Not a code fault; the brief's spawn point (or the showcase's footprint) needs
  moving for line 1. The rest of the showcase is in reach from 140.
- **Line 2.** `timed; touch Start @ Line Console#2; wait 2700; panel Line Console#2`: `Running 4/8`: Extruder 1 and 2 Running,
  Wheel Former `Blocked at full size` (its rack full of wheels), Assembler `Stopped no orders`; Panel Former `Waiting for orders`,
  Airframe Former `Forming Wing`, Laser Cutter `Waiting for orders`, Press `Wants a blank` (its die is a Bracket Die), Assembler #2 `No recipe set`.
  Machines without an order or recipe wait, as they should. **Not read:** rows 5 to 8 of the console: `touch Down @ Line Console#2`
  (hit, button 5 "Down", seven times with waits) left the screen at `1-4 of 8` and the cursor on Extruder 1. Not looked into.
- **An Assembler-made container to the hold.** `timed; touch Start @ Line Console#2; near Building Station 2; station pick VT Ammo Rig;
  wait 60; station start; wait 1800; station; logi riders; ...; wait 3000; station`: `Ammo Container on 41 (Conveyor Router#288 ->
  Building Station#370) ... (order 2)`, then `Done "VT Ammo Rig" parts 2/2`. Joined client (host `timed; touch Start @ Line Console#2; disown`,
  the client picks and starts): the same rider, `await done: reached after 3117 frames`, Done 2/2 on both peers; the host's lifter count
  2 fitted by the arms. The station's note still reads `queued at the Assembler` while the container rides (handed over in the repair notes; still so).
- **Loader Arm side pick.** `... station pick Buggy; wait 60; station start; wait 30; station stock; wait 600; lifter; station await done; lifter`:
  `stock: 15 part(s) into the hold, 11 refused (Wheel: rack full)`, then `Done "Buggy" parts 26/26 links 3/3`, `14 carries seen, 14 fitted by lifters`,
  both arms (`part 376`, `part 377`) `moves 7` each: the second arm's picks are along the hold's side. Pinned 0, pushed 0.
- **Gas from a bin.** `fill Storage Volume#11 100 hydrogen; ... station pick VT Gas Rig; station start; station stock; station await done; station tanks`:
  `linked bins: Storage Volume#11 99 hydrogen ...`, `Fluid Tank#1 40 of 40 l hydrogen`: the bin gave its unit (100 to 99). (`stock Storage Volume#11`
  reads "nothing" for a volume store before and after: the window tool's `stock` doesn't read a Storage Volume's materials.)
- **The showcase's cutter.** `logi order Cut Plate (solid 50x10x100 steel | r50,25,30,20,0) 1; cutter await; panel Laser Cutter`: `placed`,
  `Laser Cutter#502 cut for 18.0 s`, its screen `Offcut 1 steel +0.2`, `Orders: none`.

## 2. Smart logistics fix-ups 2: verified

- **The set-off pile.** `wait 60; logi feed 12 Ammo Container; wait 2400; logi riders; panel Conveyor Router; panel Conveyor Router#2;
  stock Conveyor Router; stock Conveyor Router#2; logi haul Conveyor Router|8; logi haul Conveyor Router#2|8; wait 1200; ...` (the feed
  spreads over both Routers, #288 and #501): each Router had `loose near it: Ammo Container x4`, a fifth `Stuck reason NoStorage` on its in
  conveyor and a sixth behind it; the screens `STUCK / Holds Ammo Container / pile beside it full: take it away`, the board `NETWORK - STUCK at Router 1`.
  Hauled 4 each: the waiting ones set off (2 each), the screen `ALARM / Set off Ammo Container / no Piece Rack takes it`, riders 0.
  Hauled the last 2 each: `Waiting for pieces`, logic out 0. Joined client (host feeds and hauls, the client panels Router #288): `STUCK / Holds
  Ammo Container / no Piece Rack on the network has roo..`, then `ALARM / A piece set off / no Piece Rack takes it`, then `Waiting for pieces`
  (the client's reason text is the general one, by design: no wire change).
- **A save while an ordered container rides; rank.** Process 1: `timed; touch Start @ Line Console#2; logi set Crafting Terminal|feedPriority|2;
  near Building Station 2; station pick VT Ammo Rig; wait 60; station start; wait 1200; logi order Ammo Container 2; wait 60; logi riders;
  logi chain; saveworld VT4S`: `Ammo Container on 41 ... at 2.40: dest Building Station#370 ... (order 2)`, `queue Assembler#284 ...
  Ammo Container 0/2 chain 0 rank 4`. Process 2 (no blueprint): `loadworld VT4S; wait 30; timed; logi riders; logi chain; station await done; station`:
  `Ammo Container on 41 ... at 2.70 ... (order 2)` (the rider kept), the queue `Ammo Container 1/2 chain 0 rank 4` (the High terminal's rank
  kept), the build `Done "VT Ammo Rig" parts 2/2`, the terminal's order `InTray`.

## 3. Laser Cutter phase 6: verified on the host; the joined client's glazed order not reached

- **A glazed opening ordered** (the showcase's designer): `timed; touch Start @ Line Console#2; near Panel Designer 2; design open; design crown 0;
  design width 45; design length 50; design cut r25,22,30,20,4; design cut glaze 0; design order; wait 60; esc; cutter feed Formed Panel (curve 30x20 c0 r0 t20 glass);
  cutter await; wait 120; cutter feed Formed Panel (curve 50x45 c0 r0 t20 steel); cutter await; wait 600; logi chain; stock Piece Rack#5`:
  `panes [1 x Cut Pane (r30,20,4 t20 glass)]`, the cutter queued the panel and the pane, cut the pane in 4.9 s and the panel in 5.0 s, both
  in Piece Rack #5 (the cutter's), the designer's rows `Collect`. Blanks fed by hand, because on the showcase:
  - **The cut panel's blank never reaches the cutter.** The chain asks the Panel Former (`#1 Formed Panel (curve 50x45 ...) for Laser Cutter#502 by
    Panel Former#422`), the former makes it, and it rides `Panel Former#422 -> Long Piece Rack#423` (`dest Laser Cutter#502`, order 1): that
    rack has no way on to the cutter, which is fed only by its own plate Extruder. Showcase layout (`Source/PartKit/Generators/ProductionShowcase.cs`),
    the same gap as fix-ups 2's point 3. The glass blank has no maker at all (`the Extruder is fed metal`), as the phase 6 notes say.
  - A 105 cm sheet can't be fed: `cutter feed Formed Panel (curve 100x105 c0 r0 t20 steel)`: `not put (ConveyorSmall)`, and its chain
    reads `waiting on sheet 1.05 m wide: the widest Extruder here makes 0.5 m ...` (correct).
- **The pane welded in.** No `--blueprint`: `inv drop Cut Pane (r30,20,4 t20 glass); wait 120; from Cut Pane (r30,20,4 t20 glass) 5,1,0; inv drop Cut Panel (curve 50x45 c0 r0 t20 steel | r25,22,30,20,4);
  wait 120; fitpart Cut Pane (r30,20,4 t20 glass) | Cut Panel (curve 50x45 c0 r0 t20 steel | r25,22,30,20,4) o0; inv bodies; design parts`: `fitted`, one body of
  2 parts, the pane at `-0.0100,0.0000,0.2500`, on `o0`. (The rack's pieces could not be taken: `logi take` and `fitpart` split on `|`, which every
  cut key holds, so loose copies were dropped. A dropped pane under the ground at the arena's edge is lost at once; on open ground it stays.)
- **The 105 cm panel's pane clear of its opening.** The same with `Cut Panel (curve 100x105 c0 r0 t20 steel | r50,52,40,30,5)` and
  `Cut Pane (r40,30,5 t20 glass)`: fitted, `o0` and the pane both at x `-0.0100` (the opening's true middle -0.005; the pane 7 mm short
  all round leaves 2 mm). Scratch blueprints of that panel with the pane on `o0` by `back`, flat and crowned (`c10`): `--check-vehicle`:
  `coplanar faces between parts: 0 among 9 boxes, and 0 slivers` (15 boxes crowned, 0 and 0).
- **The pane count in the designer.** `design cut glaze 0` then `glaze 1` on two equal openings: `panes [1 x ...]`, then `panes [2 x Cut Pane (r30,20,4 t20 glass)]`;
  an opening across the panel's middle on a 45 cm sheet: `no opening there a pane fits`. The 499 cap's readout line was not read (the tool prints the pane list only).
- **Joined client: not reached. The Panel Designer won't stay open on a joined client** in four runs (ports 28015 to 28019): `design await`
  (aim and use, 400 tries) never opened it; `design open` printed `open` and the next line `design: closed; ... links 0 former(s); fits []; library 0`;
  `near Panel Designer 1.5; use` left `designer closed`. So the client's ORDER sent nothing (host `cutter: ... orders 0`). The phase 6 notes saw a
  client open it. Likely `DrawDesignerInMatch`'s close test (`Source/App/Program.SandboxPanelDesigner.cs` 31 to 33: `_mouseCaptured`, or the
  local player not alive yet) on the client; not traced (the designer is a builder's file this round). See again: host
  `wait 60; disown; design bring 1; wait 6000; cutter`, client `wait 1500; near Panel Designer 1.5; use; wait 30; ui`.

## 4. Sized parts phase 6: verified (the recycler not driven)

- **A world from before phase 6.** The commit before the merge (`19a8d9c5d`, `c34119294^1`) built in a scratch worktree. There:
  `logi set Press|recipe|3` (`Pattern: gusset`), four pressed pieces dropped (`inv drop Pressed Part (engine.crank solid 45x45x65 steel)`, `(cylinder.cylinder pipe15
  60x60x105 steel)`, `(barrel.plain round 10x10x70 steel)`, `(housing.housing hollow 45x45x115 steel)`; the preset keys of phase 6's list,
  such as `piston.set round 10x10x20`, are already catalogue parts there), `saveworld OLD6`. Here: `loadworld OLD6; logi get Press|recipe; panel Press`:
  25 bodies, the four pieces by the same keys, `recipe = 3`, `Pattern: gusset` (the saved pattern keeps its meaning).
- **Size, numbers and mass**, `--press-part KEY` on both builds: identical boxes (drawn, solid), sockets, mass (70.31, 183.33, 2.75, 50.94 kg),
  health, armour, bounds and the engine numbers; only the geometry hash of the crank, cylinder and barrel differs (`SpreadLike`), the housing's the same.
- **Preset yield against cost:** `--press-part presets`: `Piston Set ... weighs 12 kg, more than its blank and bay 2 (6.79 kg): the recycler gives back 6.79 kg of it`,
  the same for the Gear Set (1.33 of 6 kg) and Gun Barrel (1.96 of 4 kg); before phase 6 the same lines read `it recycles for more than it cost`.
  `RecyclingTable.YieldOf` (`ConstructRecycling.cs` 197) caps at `CostCapOf`. **Not driven:** a wrecked preset part put in a Recycler's
  intake (the window tool has no verb that sets a wreck in an intake).

## 5. Inventories phase 4: verified on the host; the paste itself not driven; the client partly

Scratch blueprint `VT Beacon Rack`: a Foundation with an Inventory Beacon (`top00`), a Battery (`top30`), a Piece Rack (`top03` by `-y.0.0`),
a second Foundation on `z-0` with a Line Layer, chutes beacon -> rack and Line Layer -> rack. `N` = `Extrusion solid 50x10x100 steel`. Script:
`wait 60; inv survival on; inv rack 5 Piece Rack N; near Inventory Beacon 2; wait 60; inv beacon; tool Welder; wait 10; inv mode line; inv line 2 Foundation N;
wait 180; stock Piece Rack; inv drop N; wait 120; inv takeback N; wait 300; stock Piece Rack; inv bodies; inv select need 0 0 Foundation; wait 30;
inv select delete 0 6 Foundation; wait 300; stock Piece Rack; inv bodies; inv count; inv select need 0 0 Foundation`.
- **Building from made parts on a rack (survival):** `Line: 2 placed`, the rack 5 -> 3, the body's made parts the two bars. (Without the Line
  Layer: `needs a Line Layer in range`; without its chute: `Line Layer: off - no chute network`; without the Welder: `needs the attach tool`. All right.)
- **A refund back to a rack:** a loose made bar taken apart with the remove edit: `Solid bar 0.5 x 0.1 m into the network (Piece Rack)`, rack 3 -> 4.
- **A selection delete:** the body's 6 shipped parts kept, the 2 laid bars deleted: `2 parts back on the network`, rack 4 -> 6, the body 6 parts
  with no made part, no loose bar: neither duplicated nor lost.
- **A survival paste paid from made parts: partly.** The paste's survival check on a copy of the 8-part body with the two bars in it:
  `6 of 8 parts missing: Foundation x2, Piece Rack x1, Battery x1, Inventory Beacon x1, ...`: the two bars count as there (4 on the rack). The
  paste itself (the parts taken) is not something the window tool can do; not driven.
- **Joined client** (host `disown; inv survival on; inv rack 5 ...; inv players 1; bring Inventory Beacon`; the client `inv survival on; near Inventory Beacon 2;
  tool Welder; inv mode line; inv line 2 Foundation N`): the client's row `5 on the network`; its stroke refused `needs the attach tool`, since
  survival's tools are the story's (`Warden Pettigrew hasn't issued you the attach tool yet`), so a client's survival stroke is not reachable
  this way. The host's `tool Welder` passed (the host's test state). Not a fault of this branch.
- A blueprint that doesn't build (my first try, the Line Layer on the rack's spot) made the window run fail `no blueprint "VT Beacon Rack"`
  with no REFUSED or reason line in `callofslop.log`; only `--check-vehicle` named the step. Small; a lesson for the blueprint loader's log.
