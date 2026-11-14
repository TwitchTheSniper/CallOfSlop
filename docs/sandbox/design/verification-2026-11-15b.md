# Verification run 7, 2026-11-15 (main at 78fc2f18d, protocol 313)

The seventh verification: what run 6 (`verification-2026-11-15.md`) and the client-faults notes (`client-faults-notes.md`) left open,
through the player's path on the host and a joined client. Every run is a throwaway window from `bin\Release\net10.0`:
`dotnet CallOfSlop.dll --window-shot OUT.png --save-dir %TEMP%\...\saves\NAME --no-splash --no-upnp [--blueprint NAME] [--map Glacier
--calm] [--spawn-at 140,0,-100] --do "..."`; host and client are two windows, `--net host:2799N` / `--net join:127.0.0.1:2799N`. Below only
the `--do` scripts are given. Scratch blueprints in `bin/` only (none committed): `VT Cab Rig` (Foundation, Loader Arm, Lifter Cab,
Battery), `VT Medium Grown` (a Medium Extruder, four Medium Die Extensions on `-x.4.5` and four on `+y.4.5`, four Medium Run-out Tables),
`VT Cut Line` (a Panel Former conveyed into a Laser Cutter, the cutter into a Piece Rack, on a row of Foundations), `VT Cut Hand` (the same
with no conveyor from the former), and run 6's `VT Cab Drive`, `VT Select Rig`, `VT Framed Extruder`. No protocol change.

| # | Item | Verdict |
| --- | --- | --- |
| 1 | Crane and Loader Arm wait for a player | **Verified**, host and client |
| 2 | Lifter Cab's hook floor in a planetoid's cave | **Verified**, host and client, after three fixes |
| 3 | Framed Small and grown Medium Extruder at their limits | **Partly**: printed at 16 m; no conveyor takes a piece over 2 m; 128 m read and started, not run to the end |
| 4 | The chained blank on a client | **Verified** on a line with a way (client, save and load); on the Showcase it waits with its reason (generator gap, handed over) |
| 5 | Run 6's partlies | loaded parts **verified**; cab + Driving Module **verified** (rule), partly (no real truck); paste **partly** (client, no ghost); crane time: **the notes were wrong**, fixed; recycler in survival **not**; mixed-stroke engine **not** |
| 6 | A Panel Former's order with no Piece out conveyor | **Verified**: at a terminal (client) and at the former (client) |
| 7 | The whole chain on the Showcase as a client | **Not end to end**: stops at the Showcase's missing way from the Panel Former to the cutter, and it has no glass line; two missing reasons fixed |

## Faults found

| Fault | Cause | Fixed |
| --- | --- | --- |
| A hosted (`--net host`) world on Glacier had **no caves** (nor would a city's metro or a colony's graded bench) | `ResetSandbox` digs them, then `Match.OnMatchStarted` calls `Map.RestoreProps`, which cleared the dug ground (`Source/Game/Match.cs` 249, `Source/Props/MapProps.cs` 663). Offline the order differs, so offline worlds had them | Yes: a fresh Sandbox world clears the old ground before digging (`GameSandbox.cs`, `Map.ClearDugGround`), the match start keeps it on the server and sends the ground log again after `MatchStart` (a client clears its own there). The host then read version 985, a client 492 ops, both with the cave |
| A blueprint asked for in a cave came down on the world's top, 26 m over the player | `ConstructWorld.Ground(q, near)` casts down from 60 m over the ground; in dug ground under a roof it meets the top first (`Source/Constructs/ConstructWorld.cs` 1949) | Yes: under a roof the ray starts under the roof over the point. The cab rig then stood on the cave floor |
| The cab's hook went 0.38 m into a cave's rising floor when moved across, then Ctrl popped it out | the floor rule ran only while lowering (`move.Y < 0`, `ConstructLifterCab.cs` 131) | Yes: on every move. Tried: across, forward and down over the bowl, never under 0.10 m |
| An order whose maker sits behind a stopped Line Console read only "0/1 from the Panel Former" | `ConstructLogisticsPlan.Note` didn't look at Run | Yes: "the Panel Former is held off: press Start on its line console" (host and client) |
| A Laser Cutter with no conveyor in told a player holding its blank only "needs X on its bed" | `ConstructLaserCutter.Lacks` | Yes: "...on its bed: only a conveyor into its Piece in brings it (a hand can't load the bed)" |
| A client's order row lost its second reason | the reader cut a row's note at 200 characters while the writer sends 255 (`ConstructCrafting.cs` 1460) | Yes: 255 (same bytes on the wire) |
| The window tool's `bring` put a client 4 km up on a planetoid | it used the flat map's height | Yes (tool) |
| `lifters-phase6-notes.md`: "about 6.4 s a carry" | the notes; the crane follows `ConstructLifters.Ticks` (handle + across at its speed + up and down at its hoist) | Yes: the notes now give the law and the measured 9.8 to 14 s |

Handed over (cause certain, not fixed here):

- **The Production Showcase has no way from its Panel Former to its Laser Cutter, and no glass line** (`Source/PartKit/Generators/ProductionShowcase.cs`,
  `PanelLine` 470 and `BackStrip` 349: the former's Piece out runs to the Long Piece Rack and on to the Panel Designer; the cutter is fed
  only by the steel plate Extruder through its Router). A designer's cut panel or pane on the Showcase can therefore never be cut. It is a
  layout change in a 616-part generated blueprint (a Router after the former's rack and a Join before the cutter, about 40 m of conveyor
  behind the designer's corner and the Airframe Former's wing), plus a glass sheet supply; it wants its own session and a look at the
  result. Until then the rows say why, on host and client.
- **No conveyor taker is wider than 2 x 2 m** (the Large Lay-down Pad and the Large junctions), while dies grow to 8, 16 and 32 m: a piece
  wider than 2 m can only drop on its machine's out bed. A design question (a Huge pad or rack, or a crane's pick at an out bed).
- `[planet] rendering disabled: 0(295) : error C1503: undefined variable "uCenter"` in every Glacier window on this PC (the planet's
  shader): not looked into.
- The Production Showcase at the briefed `--spawn-at 140,0,-100` reaches past the Proving Ground's edge (its Panel Former at x 197): a
  `touch` there stops the player at the edge. A spot further in, or `--map`, for runs that reach the panel line.

## 1. The crane and the arm wait for a player

The tool could not get a wait before: a 30 cm blank is always nudged out from under (its shortest way out is under `MaxEscape`, 0.75 m).
New: `lifter drop pad2 blank WxHxL` (any laminate plate) and `lifter carrying NAME` (waits until a lifter carries that piece).

- **Crane, host**: `lifter crane; wait 60; lifter pad2; lifter drop pad2 blank 200x10x200; wait 120; lifter panel crane; lifter order 5 4 1
  Extrusion plate 200x10x200 laminate; lifter carrying 200x10x200; lifter stand me put; wait 1200; lifter; panel Medium Crane Bridge; esc;
  hold MoveLeft 2; wait 60; lifter; ...`: the crane stopped at 396/589 ticks, `Player` then after 10 s `PlayerStuck` (its Alarm), screen
  `WAITING / Player in the way`, state Refused; the player pushed 0 times (`too far to nudge`), not moved; walked 3.9 m left with the
  key, and the crane carried on and put the plate on the pad. **Verified.**
- **Crane, client** (host `... lifter joined; ... lifter stand others put; wait 1500; ...`; client `lifter walk; lifter carrying 200x10x200;
  wait 1260; lifter; hold MoveLeft 2; wait 120; lifter`): the client's copy read `Carry PlayerStuck ... waiting at tick 2112`, then held
  894 ticks, the client walked 11 m away, the crane went on; the pad holds the plate. **Verified.** (A first try lost the walk: the
  script's `esc` opened the pause menu when no panel was open.)
- **Loader Arm**: it carries at most 1 m across (a 1.2 m plate's order is refused with the notice `too big across: it takes 1 m`; the tool's
  `lifter orders` said "ordered", meaning sent). With a 1 m plate on open ground the arm's swing nudged a client 0.9 m over 20 pushes and
  put it: a nudge, as the rule says. Penned (`lifter pen others put`, four Crate Stacks): the arm stopped at 156/207, `PlayerStuck`, screen
  `WAITING / Player in the way` on host and client, the player moved 0.02 m; let out (`lifter free`), it put the plate. **Verified** host
  and client (the walk-away in the pen by the tool's `free`, not keys: penned, no key gets out).

## 2. The Lifter Cab's hook floor in a cave

New verbs: `cave [N]` (Glacier's N-th cave as this peer's dug ground has it; the host stands on its floor) and `spawnbp NAME` (the parts
menu's call where the player stands). Cave 1 lies at 9.34, 70.11 (well away from the top), up `<0.93, 0.16, 0.34>`, 26 m of rock over its
floor.

- Host, `--map Glacier --calm`: `cave 1; wait 60; spawnbp VT Cab Rig; wait 120; sit Lifter Cab; wait 300; hands; hold CrouchHold 4; secs
  4.5; hands; hold MoveLeft 2; secs 1; hands; ...; hold Jump 2; secs 2.5; hands`: the rig at first came down on the world's top over the
  cave (fault 2); fixed, it stands on the cave floor. Ctrl lowered the hook along the body's up from 1.07 to 0.35 m (the arm's reach) and
  to 0.10 m where the floor rises; across the bowl it rode the floor at 0.10 m (fault 3 before); Space raised it 3 m. Never negative after
  the fix, never a jump upward. **Verified.**
- Client (`--net host`): the host's world had no cave at all (fault 1: dug ground `any False, generation 1`). Fixed: host and client both
  read the cave; host `cave 1; ...; spawnbp VT Cab Rig; disown; lifter joined; bring Lifter Cab`, client `cave 1; waitnear Lifter Cab; sit
  Lifter Cab; ...`: Ctrl took the hook to 0.36 then 0.10 m along the local up. **Verified** (this client run was before fault 3's fix,
  which is the server's step; the host run after it).

## 3. The Extruders at their limits

- **Framed Small**: run 6 printed 8 m pipe sections. "The piece leaves by its conveyor": no part takes a conveyor wider than 2 m (the
  limit is the smaller opening, `ConstructConveyor.Limit`; the widest Piece in is the Large Lay-down Pad's 2 x 2 m), so an 8 m section can
  only drop on the out bed. **Not possible as built**; handed over above.
- **Medium with Die Extensions and Run-out Tables** (`--blueprint "VT Medium Grown"`): four extensions on its side made the die 16 x 8 m
  and the advice for a taller section named them again ("weld Medium Die Extensions ... reaches 16 x 16 m"); four more on the top made it
  16 x 16 m; four tables, 128 m (`takes it` at 128 m, `129 m ... build a Large Extruder` at 129). Through the configure tool and the
  screen's Queue: a 16 m wide plate and a 16 m tall plate printed and dropped; a 12 m pipe printed; a 15.5 or 16 m pipe stops `blocked:
  something fixed in the piece's path (a wall, the ground)`, the map's ground under the die's far half (the grown die's foot is flush with
  the machine's base; the tool's `extr` now names what blocked). A 10 x 10 cm bar ordered at 128 m extruded at 0.4 m/s (10.25 m after
  25 s); a whole 128 m piece takes 5 minutes and was not run. **Partly.**

## 4. The chained blank on a joined client

- **Host** (`--blueprint "VT Cut Line"`; `fill Panel Former 500 steel; near Laser Cutter 2; logi send x@10 Cut Panel (curve 50x50 c10 r0 t20
  steel | r25,25,20,20,5) 1; ...`): one Formed Panel made, conveyed, cut, `Piece Rack: Cut Panel (...) x1`, nothing else made. **Verified.**
- **Client** orders it (`waitnear Laser Cutter; logi send x@10 ...`; the tool's `logi` now finds a cutter's body on a client): the same on
  both peers, one blank, the panel in the rack. **Verified.**
- **Save and load mid-chain**: the host saved while the blank lay on the bed (cutting); a fresh process `loadworld CUTMID`: `bed blank 64%`,
  the cut finished, one panel in the rack, no blank made again. **Verified.**
- **The blank by hand**: on `VT Cut Hand` (no conveyor from the former) the order starves; a blank picked up with the physics gun and
  brought to the cutter is not taken (no hand path onto the bed). The look line said only "needs X on its bed" (fault 5); now it says only a
  conveyor brings it. **Verified** (host).
- **On the Showcase** (client designs and orders at the Panel Designer): one blank made, then the row waits, "the Formed Panel (...) waits
  in the Long Piece Rack: no conveyor or arm from the Long Piece Rack to the Laser Cutter: link one, a hand can't load it", on both peers.

## 5. Run 6's partlies

- **Loaded store parts** (`lifter crane; lifter pad2; lifter cab crane; sit Lifter Cab; ... lifter cabstate baitfull Ammo Container; key
  Interact; ...; lifter drop pad2 loaded Drill Module; lifter panel crane; lifter order 5 4 1 Drill Module`): from the cab `CabNotNew`, "what
  is under the hook holds something: empty or reset it first"; by hand order "no such pick ...: holds something: empty it first (a pad keeps
  kinds and counts, not what is in them)", and the pad's row says the same. **Verified** (host).
- **A cab wired to a Driving Module** (`VT Cab Drive`; `sit Lifter Cab; hold MoveForward 2; secs 2.5; hands; seated; hold MoveLeft+MoveBack
  2; ...`): W moved the hook 2.6 m, A+S 4.3 m; throttle and steer 0, the body still. Decided as the code has it (`GameSandbox.cs` 375: a Lifter
  Cab never drives, even linked to a Driving Module): the cab's keys move the hook, a truck is driven from its own seat, so the two never move
  together. **Verified** for the rule; no truck with a crane was built (run 6: the arm's foot meets the car's parts).
- **Survival paste on a client** (`VT Select Rig`; host `inv survival on; skip induction; disown; lifter joined; bring Selector; ...; inv give`
  the two bars and a shelf, `inv store Parts Shelf Battery`; client `skip induction; waitnear Selector; clip copy Foundation 2-5; clip paste 6
  1 180; ...`): first `4 of 4 parts missing: Battery x1, Parts Shelf x1, Extrusion solid 10x10x100 steel x1, Extrusion solid 10x10x50 steel
  x1`; with the parts there `4 parts placed, frozen where they are`, two loose bars taken. (Without the client's own `skip induction` the
  Selector was refused: "Warden Pettigrew hasn't issued you the selector tool yet", the right reason.) **Partly**, as in run 6: `clip`
  skips `SelectController` (the box drag, the ghost, the cost preview).
- **The crane's carry time**: the crane is right; the notes were wrong (fixed above).
- **The recycler in survival** (`--blueprint "Recycling Station"`; `inv survival on; skip induction; from Recycler 0,2.5,2.5; extr build Small
  Cylinder 1 0; aim Small Cylinder@new; key PrimaryWeapon; hold Fire 5; ...; tool PhysicsGun; aim Small Cylinder@w; hold Fire 12; aimintake
  1.2; wheel 2 ...`): the hand gun wrecked it (`health 0 of 180`), the physics gun held it, but it rested against the recycler's side at
  1.75 m and never got over the mouth in three tries, so nothing was fed. **Not reached** (run 6's creative try fed it from above; why the
  survival hold stops there was not found).
- **The mixed-stroke engine near the starter limit, run**: **not done** (no runnable rig: run 6's `VT Mixed I4` has no tank, seat or module
  and the Medium sockets refuse Small parts; building one was left for budget).

## 6. A Panel Former's own order with no Piece out conveyor

- **At a terminal** (Showcase with its Long Piece Rack removed, `logi remove x@423`; client `waitnear Crafting Terminal; logi send x@302 Formed
  Panel (curve 150x100 c10 r0 t20 steel) 1; craft Crafting Terminal`): taken; while the console was stopped the row said "its line is stopped:
  press Start on its console"; after the host's Start: `made: on the Panel Former's out bed`, the panel there. **Verified** (client).
- **At the former** (`VT Cut Hand`; host `bring Panel Former`; client `waitnear Panel Former; touch Queue @ Panel Former`): one formed panel
  on its out bed, 7 steel used, on both peers. **Verified** (client). (On the Showcase the host's touch hit the arena's edge: see above.)

## 7. The whole chain on the Showcase, as a joined client

Client: `design await; design width 50; design length 50; design cut r25,25,20,20,5; design cut glaze 0; design order; ...; craft Panel
Designer`; host: `disown; design bring 1; secs 50; touch Start @ Line Console#2; ...`. What the player saw, step by step:

1. The design and its glazed opening: `panes [1 x Cut Pane (r20,20,5 t20 glass)]`; ORDER queued two orders, the panel and the pane.
2. The panel's row: "0/1 from the Panel Former" and nothing else while the Line Console was stopped. **A missing reason**: fixed ("the Panel
   Former is held off: press Start on its line console").
3. After Start: one blank made, then "waits in the Long Piece Rack: no conveyor or arm ... to the Laser Cutter: link one, a hand can't
   load it". The reason is right; the Showcase can't do it (handed over).
4. The pane's row: "waiting on Extrusion solid 20x10x35 glass: the Extruder is fed metal: an order in glass is another line": right; the
   Showcase has no glass line.
5. On the client the panel's row was cut at 200 characters, losing step 3's reason once step 2's was added: fixed (255).
6. The cut, the pane, and a Building Station fitting the panel with an arm or the crane were not reached: the chain stops at 3 and 4.

## The tool's new steps

`Program.WindowShotPlay.cs`: `cave [N]`, `spawnbp NAME`. `Program.WindowShot.cs`: `lifter carrying NAME`, `NAME@new`, `bring` on a planetoid.
`LifterPressProbe.cs`: `lifter drop pad2 blank WxHxL`. `Program.WindowShotExtr.cs`: the `extr` dump names what last blocked a step
(`ConstructExtruder.LastBlock`). `Program.WindowShotLogi.cs`: `logi` on a client finds a body with a cutter or a former.

## Not run

Self-test sections (the coordinator batches them; candidates for this branch: the planet dig, terrain dig, laser cutter, crafting,
logistics and lifter sections). `--lag`. A truck with a crane. A whole 128 m piece.
