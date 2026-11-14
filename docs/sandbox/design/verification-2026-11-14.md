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
