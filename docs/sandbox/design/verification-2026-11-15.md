# Verification run, 2026-11-15 (main at 8b929cb7a, protocol 313)

The sixth verification: what merged on 2026-11-14 (arms and cranes phases 7 and 8, stroke power, logistics leftovers 3) and what run 5
(`verification-2026-11-14b.md`) left, through the player's chain on the host and on a joined client. Every window run is a throwaway one
from `bin\Release\net10.0`: `dotnet CallOfSlop.dll --window-shot OUT.png --save-dir %TEMP%\...\saves\NAME --no-splash --no-upnp
[--spawn-at 140,0,-100] [--blueprint NAME] --do "..."` (below only the `--do` script and the extra flags are given). Host and client: two
windows over RakNet, `--net host:2799N` / `--net join:127.0.0.1:2799N`, started together (the client retries until the host listens). Scratch
blueprints lived in `bin/` only (none committed): `VT Select Rig`, `VT Cab Drive`, `VT Stroke S/P/L`, `VT Bored L`, `VT Bored P5`,
`VT Mixed I4`, and on the old build `VT Old Turbo`.

| # | Area | Result |
| --- | --- | --- |
| 0 | Window tool: the missing steps | **Done**: `sit`, `seated`, `getout`, `recycle`, `clip`, `partinfo`, `hands`, `cabto`, `damage`, `makeengine` (`Program.WindowShotPlay.cs`) |
| 1 | Arms and cranes phases 7 and 8 | **Verified** for orders, the Showcase crane, the cab on host and client, shot off, wired to a Driving Module; **partly** for loaded pieces; **not** the planetoid floor (tool gap) |
| 2 | Stroke power and bored cylinders | **Verified** (numbers, built and run, `--speed-test`, the Car bay, an old saved engine); **partly** the starter case (read, not run) |
| 3 | Logistics leftovers 3 | **Verified** the wing and the cutter (saved queued, loaded); **partly** the 8 m die (refusals only); **not** a former's own order |
| 4 | Run 5's leftovers | **Verified** the recycler and the survival paste, the carry height and time; **not** the crane's wait (the pen doesn't hold) |

Faults: one fixed (`--check-vehicle` crashed on a bad blueprint), one doc line fixed (production.md's Die Frames "6 by 6 m"), one window-tool
gap fixed (`--blueprint` didn't say why a blueprint was refused), two possible faults reported (below).

## 0. The verbs added

All in `Source/App/Program.WindowShotPlay.cs`, one line each in `WsKnown` and `WsRun`, listed in README.md "Testing without playing":

- `sit PART[#n]`: `CActKind.Use` on that seat, the use key's own action (the host stands its player 1.3 m over the seat's feet first; a
  client is brought by the host's `bring`); `seated`: the seat, speed, engine running, rpm, gear, throttle, seat keys and the look line;
  `getout`: E in a Lifter Cab, the use key elsewhere. Driving is the existing `hold MoveForward 5; secs 2`.
- `recycle NAME|KEY [@ RECYCLER]` (host): the part dropped loose in the intake's middle and wrecked through `DamagePart` (a round's path;
  a part that breaks off leaves its debris body there), then left to the Recycler's own 10 Hz scan; it prints mass, cost cap and yield.
  `recycle read`: the bin, the job, the line state, the body's other stores (a chute's Materials Bin), the wrecks near.
- `clip copy PART [A-B]` / `clip paste [DIST [UP [TURN]]]` / `clip cost`: the Selector tool taken out (`CActKind.Tool`), the selection
  sent in its chunks (`SandboxSelectSend`), `SelAct.Copy`, then `SandboxPaste` (the server's reach, aid, limit and survival need
  checks); `clip cost` diffs the inventory and the loose bodies near.
- `partinfo PART`: mass, health, armour, yield, cost cap, recipe, each modular engine of the body (kW, torque at peak, idle, peak, limiter,
  stroke and built stroke, cylinders wrecked and misfiring, starter needed), then the look line from 2 m (the crosshair's own).
- `hands`: each lifter's state and its piece where it is drawn now (`Pose` at `AgeNow`), its height over the ground along the up there
  (`ConstructLifters.Altitude`). `cabto PART`: seated in a cab, the look turned toward the part (the mouse's turn) and W tapped until the
  hook is within 0.25 m of over it. `damage PART HP` (host): `DamagePart`, armour first. `makeengine KW TIER [car|any]`: the Engine tab's
  `SandboxMakeEngine` with the tab's defaults (no placing ghost).
- Also: `--blueprint NAME` now names the refusal when a blueprint was refused at load (it said only "no blueprint").

## 1. Arms and cranes phases 7 and 8

- **Hand order at a Loader Arm** (host): `lifter crane; wait 60; lifter pad2; lifter set liftMode 3; wait 300; lifter panel arm; lifter order 3 4 1
  Extrusion plate 30x30x50 laminate; wait 600; lifter orders`: the rack 3 to 2, the pad `holds 1 Extrusion plate 30x30x50 laminate`, the order
  gone. **Verified.**
- **Hand order at a Bridge Crane** (host): `... lifter panel crane; lifter order 3 5 1 Extrusion plate 30x30x50 laminate; wait 900 x3`: the
  crane finished its own carry and return first (about 40 s), then pad 2 `holds 1`, the order gone. **Verified.**
- **Joined client** (`--net host:27996`; host `lifter crane; lifter pad2; lifter cab crane; lifter joined; bring Lifter Cab; wait 2700; lifter
  cabstate bait Plate Steel 1 m`; client `wait 1500; sit Lifter Cab; wait 1500; hold CrouchHold 3; ... key Interact; ... getout; lifter panel
  crane; lifter order 3 5 1 Extrusion plate 30x30x50 laminate`): the client sat, lowered the hook to 0.10 m, `took it`, `put it in`, got
  out; its order queued and the host's pad 2 then held the blank. **Verified.**
- **The Showcase's crane working its line** (`--blueprint "Production Showcase"`, `hands` every 0.5 s for 45 s): an angle from the
  Extruder's out (0.82 m) to the press bay (1.04 m), a bracket from the press out (0.42 m) to the pad (0.61 m), each followed by its
  return. **Verified.**
- **The Lifter Cab** (host, `lifter crane; lifter cab crane; sit Lifter Cab; wait 1500; hold CrouchHold 3; lifter cabstate bait Plate Steel 1 m;
  key Interact; hold Jump 1; cabto Lay-down Pad; key Interact; getout`): `Manned` after the crane's own move; Ctrl lowered the hook to
  0.10 m over the ground; F `took it`; `cabto` brought the hook within 0.22 m over the pad; F, and the pad `holds Plate Steel 1 m x1`;
  E out, the crane `Idle`. **Verified.** The look line said `set it down` though the piece ended in the pad (the pad took the loose piece
  in), not `put it in`: words only.
  - The cab waits for the crane's carry **and** its return before it is manned (about 25 s on this rig); the notes say "finishes its
    move". Not a fault as far as I can tell; worth a line in the notes.
- **A loaded store part**: from the cab, `lifter cabstate baitfull Drill Module` (10 iron ore) and F: `what is under the hook holds something:
  empty or reset it first`, left lying; an empty Ammo Container: `took it`, then `put it in`. By a hand order: `lifter drop loaded Drill Module`
  over the pad: the pad refuses it (lies loose by it), so the panel offers no such pick and `lifter order 4 5 1 Drill Module` reads `no such
  pick`. **Partly**: refused both ways, but the panel's words don't say why (the pad's "holds something" is not shown on the panel).
- **The cab shot off mid-carry** (`... key Interact; hold Jump 1; damage Lifter Cab 100000`): the cab wrecked, the sitter out (`seated: no`),
  the crane `Carry Carrying` with the plate, then `Return` (it carried it on by its own rule). **Verified.**
- **The hook's floor on a story planetoid away from its top**: on Caledon's top (`--map Caledon`) the hook stops 0.10 m over the ground. At
  45 degrees down its side (`--at 0,4268,4268`) the window tool's crane rig is built in world Y (`LifterCraneProbe`): `not on a Crane Rail`,
  the hook 437 m over the ground. **Not reached**: a tool gap (the rig needs building along the local up), not a game fault seen.
  (`--start castaway` puts a window run on a flat map, up 0,1,0.)
- **A cab wired through a Driving Module** (`VT Cab Drive`: a Foundation, a Loader Arm, a Lifter Cab wired to a Driving Module, a Battery;
  `sit Lifter Cab; hold MoveForward 2; hold MoveLeft 2; hold MoveBack+MoveRight 2`): W, A and S+D moved the hook 2.6, 4.4 and 4.6 m; the
  body's throttle and steer stayed 0. **Partly**: the rig has no drivetrain, so the cab isn't a drive seat there; a truck with a crane
  (Bandit Car 1 with an arm and a cab) wouldn't build at any socket tried (the arm's foot meets the car's parts).
- **The Welder's join (F) aimed at a Loader Arm** (`tool Welder; near Loader Arm 2; aim Loader Arm; key Interact`): the lifter panel opened.
  That is the code's order (`Program.Sandbox.cs` 370 and 432: the join only when `FindJoin` finds a construct the arm touches, then the
  panel). **Partly**: an arm touching another construct wasn't built.

## 2. Stroke power and bored cylinders

- **Short, plain and long strokes of one bore, and plain against bored of one outer size**, through the blueprint build rules: the kit's
  Bandit Car V8 (`--gen-vehicle "Bandit Car" --seed 1 --engine-tier 3`) with its eight cylinders' key swapped, each spawned and driven
  (`partinfo ...; sit Seat#1; hold MoveForward 5; secs 2; seated; engines`):

  | Cylinder | Stroke | kg, hp each | Engine kW | N m at peak | idle / peak / limiter | Running, 2 s of W |
  | --- | --- | --- | --- | --- | --- | --- |
  | `pipe10 45x45x45` | 0.6 | 16.8, 247 | 192 (8 x 24) | 220 | 1500 / 8333 / 11667 | 7.8 m/s, 6283 rpm |
  | `pipe10 45x45x75` | 1 | (the Small Cylinder) | 320 (8 x 40) | 611 | 900 / 5000 / 7000 | |
  | `pipe10 45x45x120` | 1.6 | 44.8, 660 | 512 (8 x 64) | 1565 | 562 / 3125 / 4375 | 6.7 m/s, 2987 rpm |
  | `pipe7 45x45x120` (bored) | 1.6 | 34.05, 501 | 518, starved: the carburettor feeds 63.6% | 1584 | 563 / 3125 / 4375 | 7.9 m/s, 3499 rpm |
  | `pipe5 45x45x75` (bored) | 1 | 16, 236 | 518, fed 78.3% | 990 | 900 / 5000 / 7000 | 9.9 m/s, 4402 rpm |

  Every figure equals the stroke notes' table (section 3) and run 5's bored table; a long stroke on the kit's carburettor is starved by
  power as the notes say. **Verified** (the plain one's `partinfo` missed it by name: the key is the catalogue part).
- **`--speed-test Constructs/Blueprints/BanditCar1.json Constructs/Blueprints/BanditBoxTruck1.json`**: 1653 kg, 218 kW, 24.6 m/s (grip);
  2475 kg, 218 kW, 19.3 m/s (power against drag). **Verified** (shipped files; the kit's figures are run 5's and unchanged).
- **The creative Engine tab's Car bay, 800 to 2000 kW** (`makeengine 800 5 car; makeengine 1200 5 car; makeengine 2000 5 car; makeengine 2000
  5 any`): every Car bay request gives `Small V8 Performance: 611 kW, 1167 N m at 5000 rpm (limiter 7000) ... 611 kW is short of 800 kW: the
  most within the limits`; with no bay, `Small B12 Racing Long-stroke Supercharged: 1986 kW, 6070 N m at 3125 rpm (limiter 4375)`. The limiter
  held to the Small one and the notice gives torque at peak and the limiter. **Verified.**
- **A mixed-stroke engine near the starter limit losing one cylinder** (`VT Mixed I4`: four `engine.crank solid 310x310x380` sections, scale
  6.89, three `pipe30 310x310x825` cylinders (1.6) and one `pipe30 310x310x515` (1.0); `--gen-engine --read`: stroke 1.496, `starter no`,
  6.89 x 1.496^(1/3) = 7.87 of 8): `damage Pressed Part (cylinder.cylinder pipe30 310x310x515 steel) 100000000`: 15.2 MW to 12.6 MW,
  `1 wrecked`, revs by the working stroke (peak 1426 to 1333 rpm), built stroke still 1.496, `needs one False` (the old rule would read
  6.89 x 1.6^(1/3) = 8.06). **Partly**: read through the readout; the engine had no tank, seat or module (the Medium sockets refuse Small
  parts), so "it keeps running" was not watched.
  - **Possible fault**: each wrecked cylinder's carburettor became an engine of its own: `engines: 1 no crank: a cylinder goes on a crank
    section` (a second, then a third after a second wreck). `ConstructEngine.Build` (`ConstructEngine.cs` 316-354) joins pieces only
    through working ones, so a fuel system left on a wrecked cylinder is a lone group with `HasEngine`. Seen: the look line on that
    carburettor would say "no crank", and it takes one of `MaxEngines`. To see it: the steps above, then `engines`.
- **A boosted long-stroke engine saved before the change** (the commit before the merge, `2fa3c1205`, built in a throwaway worktree: the
  kit's `--gen-engine --power 600 --tier 5 --layout inline --boost turbo`, its seven `pipe7 45x45x75` swapped for `pipe7 45x45x120`; spawned,
  `engines; saveworld OLDTURBO`): `I7 624 kW, thirsty ... stroke 1.6, 1907 N m`. Here `loadworld OLDTURBO; engines`: the same 23 parts,
  `I7 635 kW ... 1940 N m`, `bank 1 starved: the carburettor feeds 63.6%`: 624 x 1.6 x 0.636 = 635, the old power times the lesser of its
  stroke and its fuel system's headroom, as MIGRATING says. **Verified** (no power lost; 1.8% more, the carburettor's headroom).

## 3. Logistics leftovers 3

- **A wing ordered at a terminal** (Showcase, host `logi list Wing; logi order Wing Segment 1`; client after `disown; bring Crafting Terminal`:
  `logi send x@302 Wing Segment 1`): the rows `make 3 ... the Airframe Former drops it loose, no conveyor to the tray: order it on its panel,
  collect it there`; the host's order refused with it; the client's notice the same. **Verified.**
- **A cut panel ordered at a cutter with no Piece out conveyor, saved queued and loaded** (`extr build Laser Cutter 0 6; cutter order Cut
  Plate (plate 50x10x100 steel | r25,50,20,20,5); saveworld CUTQ`; fresh process `loadworld CUTQ; cutter; cutter feed Extrusion plate
  50x10x100 steel; cutter await; cutter`): queued and `NoMaterial` before the save and after the load, the blank laid on the bed, a cut of
  3.2 s, then `orders 0` and `cut parts loose: Cut Plate (... | r25,50,20,20,5) x1`. **Verified** on the host.
- **A formed panel ordered at a Panel Former with no Piece out conveyor**: **not tried**: no window-tool verb queues a panel key at a
  former (`extr queue` makes Extruder sections), and the terminal's way is the review's own tried case.
- **A Small Extruder with Die Frames to 8 m** (`extr build Extruder 0 8; extr Extruder 700 700 1000; ... 800 800 1000; ... 850 850 1000`): 7
  and 8 m read `attach Die Frames to its sides (its die reaches 8 x 8 m with them)`, 8.5 m `build a Medium Extruder`; `Piece out opens 8 x 8
  m`. **Partly**: the framed 8 m die wasn't built (about thirty frames by `fitpart`), so no 8 m section was printed. production.md's Die
  Frames paragraph still said "up to 6 by 6 m": fixed to 8.

## 4. Run 5's leftovers

- **The recycler fed a wrecked preset part** (`--blueprint "Recycling Station"`, creative: `recycle Gear Set; wait 30; recycle read; recycle
  Small Cylinder; wait 60; recycle read`): Gear Set 6 kg, cost cap 1.33 kg, yields 1 metal, taken in; Small Cylinder 28 kg, cost cap 77 kg,
  yields 14 metal; bin 5 and the chuted Materials Bin 10: 15 = 1 + 14. The yield is the lesser of mass and cost cap. **Verified** (creative,
  instant; survival's timed job not watched).
- **A survival paste paid from made parts** (`VT Select Rig`: a Selector, a Battery, a Parts Shelf on its chute, two made bars; `inv survival on;
  skip induction; clip copy Foundation 2-5; clip paste 6 1 180` first: `4 of 4 parts missing: Battery x1, Parts Shelf x1, Extrusion solid
  10x10x100 steel x1, Extrusion solid 10x10x50 steel x1`; then `inv give` the two bars and a shelf (they lie loose: a made part is moved
  with the physics gun), `inv store Parts Shelf Battery`, `clip paste 6 1 180`): `4 parts placed, frozen where they are`; the shelf's
  Battery and the three loose bodies were taken. **Verified** on the host.
- **The crane's wait for a player where a large piece lowers** (`lifter crane; ... lifter drop pad2 blank 400; lifter order 5 4 1 Extrusion plate
  30x30x400 laminate; lifter stand me put`): the player stood on the ground at the put point; pushed 60 times (2.4 m), the 4 m blank put.
  With `lifter pen me put` (four Crate Stacks round them): pushed 65 times, feet up 0.77 m onto the crates, pinned 0, the blank put. **Not
  reached**: the pen doesn't hold a player against the push, so the wait never comes. A pen of taller walls (or the player on the pad, the
  ride-frame case the notes fixed) is the next try; not a game fault seen.
- **Carry height and time** (Showcase, `hands` every 0.5 s): carried at 2.04 m over the ground for ends at 0.82 and 1.04 m, and at 1.61 m for
  ends at 0.42 and 0.61 m: the Carry height setting (1 m) over the higher end. A carry from lift to set-down about 7.5 to 8 s, each return
  about 5 s. Against the notes' "about 6.4 s a carry": close, the notes likely count the travel without the lift and lower. **Verified**
  for the height; **partly** for the time.

## Not run

Self-test sections (the coordinator batches them). `--lag`. A joined client's survival paste, recycler and cutter.
