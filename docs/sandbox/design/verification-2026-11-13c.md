# Verification run, 2026-11-13 (c) (main at cdf2b77f, protocol 300; no protocol change here)

The third run of the day: finishing `verification-2026-11-13b.md`. Every run was a throwaway window run on the Windows PC
(`dotnet bin\Release\net10.0\CallOfSlop.dll --window-shot OUT.png --save-dir %TEMP%\vt3* --no-splash ...`) on the Proving
Ground's crash pad (`--spawn-at 140,0,-100`), usually with the Production Showcase. Host and client: two windows over RakNet on
127.0.0.1, `--net host:28001..28007` / `--net join:127.0.0.1:PORT`, `--no-upnp`, the client started 25 s after the host. Scratch
blueprints in `bin/` only (none committed): `VT Ammo Rig` (Square Beam 2 m + Ammo Container on `+y0`), `VT Fuel Rig` (the beam + a
Fuel Tank), `VT Cut Wall` (a Foundation + `Cut Plate (solid 10x240x300 steel | r150,100,100,200,0)`, a 3 m bar on edge with a
1 x 2 m doorway), `VT Many Cuts` (a Foundation + eight cut bars, eight keys), `VT Showcase Air` (the showcase with its Wheel
Former swapped for an Airframe Former, its conveyor out and width setting dropped).

**Why the last run's clients "could not reach host":** the window tool's `wait N` counts frames, and its runs are uncapped (160 fps
or more), so the host's script ended, and the host quit, before the client joined. The same holds for `put`'s "a minute" of retries
(`Program.WindowShotCuts.cs` 104: 360 tries of 10 frames) and a client `walk`'s wait. With `secs N` (real seconds) on the host every
join here worked. Lesson added to `.claude/rules/tests-and-ci.md`.

| # | Item | Result |
| --- | --- | --- |
| 1 | The Ammo Container in a station's hold (b's fault) | **Fixed; verified on host and client**, except the Assembler's order path (not reached) |
| 2 | b's items 3 to 6 | 3 **verified** (sized sheet partly); 4 **partly**; 5 **verified**; 6 **verified** |
| 3 | Item 7, rank between two terminals | **Skipped** (another builder's this round) |
| 4 | b's items 1 and 2, "partly" | 2's client and container: **verified**; 1's side pick, pin and same-move elbows: **not reached** |

## 1. A Building Station's hold refuses an Ammo Container

**Seen on the host, before the fix** (`--blueprint "Production Showcase" --do "wait 30; timed; near Building Station 2; station
pick VT Ammo Rig; wait 60; station start; wait 30; station stock; wait 200; station; station hold Ammo Container; station give;
wait 60; station"`): `stock: 1 part(s) into the hold, 1 refused (Ammo Container: a rack takes pieces, not stores)`; the station
then ordered the container at the Assembler ("needs 1 rubber"). `station give` with the container in the hands **did** fit it
(2/2) once the beam was in: the hand path fits it straight on (`ConstructStation.Give`), and only falls back to the hold, which
refuses it, when no step can take it yet. b's give came before its parent was fitted.

**Cause:** `ConstructConveyor.RackCheck` (`Source/Constructs/ConstructConveyor.cs` 247) returns 2 for every `PartRole.Storage` part
on every rack, and a station's hold is a Piece Rack. Every way into the hold asks it: an order's rider arriving
(`ConstructConveyor.cs` ~446), a terminal's fetch (`ConstructCrafting.cs` 1064, which then reads "held: the tray is full - take
parts out", the wrong reason), a chute (`ConstructCrafting.cs` 878), a dropped part (`ConstructStation.TakeDropped`), the window
tool's stock. So a blueprint with an Ammo Container (or any store part) could finish only if a player carried one in at the right
moment.

**Fix (smallest):** `RackCheck` lets a Building Station's hold take a store part (`&& !rd.IsBuildStation`). Nothing is lost: what
reaches the hold is new and empty (an order's or a hand's part is a def, no contents). No wire change. Ordinary racks still refuse
stores. (A first change that made `ConstructStation.Finish` cancel the job's open orders was taken out after the merge review:
see the last section.)

**After the fix:**
- Host: `station stock` gave `2 part(s) into the hold`; the build went Done 2/2 with the container from the hold, `orders 0` (with the Finish change since taken out), the
  Assembler's queue empty ("Orders: none").
- Magazine fill (`station set fillAmmo 5`, `fill Storage Volume#10 200 ammo`): "filling: ammo 700/1440", then 1200/1440 rounds as
  the bin ran out (it holds 120 units); the container 120/2880 units. At 100 % the target read 0/28800 rounds.
- Joined client (`host:27999`; the client: `station set fillAmmo 5; station pick VT Ammo Rig; station start`, the host `bring
  Building Station; station stock`): started from the client, Done 2/2 on both peers, the client's screen `VT Ammo Rig / DONE /
  Drive it out / not filled: ammo 1200/1440 rounds`, the same tanks line on both.
- **Not reached:** a container made by the Assembler riding to the hold. The showcase's Assembler (#284) never took rubber in five
  tries (`fill` on Storage Volume#10 and the Materials Bins, with Line Console #2 started: "Needs input / 1 rubber"); its feed was
  not traced. The arrival uses the same `RackCheck`, so it should now pass; not seen.

**Faults seen, not fixed (logistics' area):**
- An **unordered** Ammo Container on the Assembler's out conveyor (`logi feed 1 Ammo Container`) stopped at the Router, `Stuck,
  NoStorage: no Piece Rack on the network has room for it` (`ConstructLogistics.cs` 644), and blocks that conveyor. Any store part
  an Assembler makes for a bill or a line, not an order, will jam its out line. See again: the command above, then `logi riders`.
- A Crafting Terminal's own order for a store part is refused at its tray the same way (`ConstructCrafting.cs` 1064, from reading;
  not run), with the wrong reason "the tray is full".
- A store part riding a conveyor is dropped from a save (`ConstructConveyor.cs` 1213 skips `PartRole.Storage` riders): an ordered
  Ammo Container on its way to a station at a save is lost (from reading).

## 2. b's items 3 to 6

### 3. Logistics (Router buttons, names, plan map, Panel Former order, wing preset): verified, the sized sheet partly
- **Router from a client** (host `bring Conveyor Router`; client `touch Spill: Auto @ Conveyor Router; touch Hold: Off @ Conveyor
  Router; logi name Conveyor Router = South Gate`): both landed; the host's Router screens read `Spill: Ahead`, `Hold: On`.
- **Name on the board, the map and an order's reason** (host `logi order L Bracket 1; logi cut Conveyor Router 3; logi feed 1 L
  Bracket`): both peers' NETWORK BOARD sign (`panel Medium Text Sign`) read `NETWORK - STUCK at South Gate`, `STUCK at South Gate:
  L Bracket - no way to it ...`; the client's plan map `14 conveyors (1 cut) ... 1 stuck`, `map16 0| NETWORK - STUCK at South
  Gate`; the order's note on both: `queued at the Press: held at South Gate: no way to it: a conveyor on the way is cut or missing`.
- **Panel Former's sized sheet** (offline: `logi order Formed Panel (hood 160x140 c14 r18 t20 steel) 1`, the same for a fender
  100x45): `Extrusion solid 50x10x30 steel 0/3 from the Extruder` and `solid 45x10x35 0/1`. **Partly:** in the joined run with Line
  Console #2 started, the hood's strips went to the Long Piece Rack and the order read `no route from the Long Piece Rack: collect
  it there`; they never reached the Panel Former (`ConstructLogisticsPlan.cs`, the maker's Extruder chosen without a route to the
  former, likely). Also: the strips are still the default, while the user's 4c answer asked for a bigger Extruder first and strips
  only as the player's choice (`ConstructLogisticsPlan.SheetFor`, `ConstructLogisticsPlan.cs` 110): not built yet.
- **Wing preset to an Airframe Former** (`VT Showcase Air`, `fill Airframe Former 100 steel`, host `logi order Wing Segment 1`): the
  list showed Wing Segment, Short Wing Segment, Swept Wing L/R `by Airframe Former#278` on both peers; the order went to the former's
  queue (`>0/1 Wing Segment`, steel 100 to 91) and both peers' screen read `Blocked`: the wing didn't leave, likely the swapped
  layout's output point. Without the fill the order was refused "the Airframe Former is fed rubber": correct.

### 4. Cut parts: partly
- **Bar on edge with a doorway, host** (`VT Cut Wall`; `ray PART 150,100`, `50,100`, `150,225`, `260,60`; `walk PART 150,100 3 150;
  side`): the doorway passes, beside, above and past its end hit at 1.95 m of 4; walking through it the player ends 0.61 m behind,
  beside it stops 0.45 m in front.
- **Joined client** (host `put PART 150,100 3`, then `put PART 50,100 3`; client `walk ... ; side`): through, 0.51 m behind; beside,
  0.45 m in front; the client's own rays the same as the host's.
- **Many cut parts on a client** (`VT Many Cuts`, eight keys): the client log `[cuts] 7 new keys off the wire waited behind
  stand-ins: the worst update that asked took 5.8 ms, then 4 pumps made them whole, the worst 2.8 ms`; each ray tried on both peers
  hit the same part at the same distance. (The rays from 1 m crossed into neighbouring bars: a tight scratch layout, not a fault.)
- **Not tried:** a real fired bullet (the `ray` verb is the bullets' construct ray, not a gun's whole shot path) and a vehicle
  driven into a cut wall: no tool verb for either in reach of this run.

### 5. Fluids on clients: verified
`link pipe Fuel Tank#2 > Building Station`, `VT Fuel Rig` stocked and built; a client in from the start, a second client joined
after the fill. The showcase's pool: host 39.7 then 39.5 L; the first client 79.9, 79.8, then **39.8 L** after the fill; the late
joiner **39.7 L**; the build's tank 40.0/40.0 L on all three. The showcase body read `awake, anchored` (not asleep) in every print.

### 6. Run-time parts in the hands, seen by a client: verified
Host `inv hold Pressed Part (bracket.l l-angle 20x20x150 metal)`, `inv give Extrusion solid 50x10x100 steel`: both players' hands
held the pressed part, read by its name and shown name `[L bracket 0.2 x 0.2 x 1.5 m metal] (run-time)` on the host and the client.
The extrusion, physics-only, was `Dropped` for both players (as designed: made parts are bodies, never in the hands); the dropped
body on the client was not looked at. `inv store Piece Rack ...` said "no slots" (a rack holds kinds: the tool's verb is for slots).

## 3. Item 7: skipped
Rank between two terminals is the logistics builder's this round. (b's table numbered it inside item 3, and its item 7 is a mounted
gun's reach past 300 m; that was not tried either.)

## 4. b's "partly" leftovers
- **Item 2:** the joined client and the Ammo Container with its magazine fill: done above. Gas from a bin: not tried.
- **Item 1, the Loader Arm:** `lifter place Scrap 2` (slot 2) with `timed`: its screen read `SCRAP ARM / No power / Moved: 0` on
  host and client, so the Buggy was built by the station alone (20/26 in 55 s) and the forced pick along the hold's side, the pin
  and the host and client elbows on one move were **not reached**. The window tool places slots 1 to 3 without power (slot 0 had it
  in b), or the arm there is off the station's power: in the lifters' area, not looked into. Also seen: `station stock` for the
  Buggy refused 11 parts "Wheel: rack full", the hold's own limit.

## Faults (file and line)
1. **Fixed:** a station's hold refused store parts, `ConstructConveyor.cs` 247 (with the review's three follow-ups: last section).
2. Unordered store parts jam a maker's out conveyor at the Router: `ConstructLogistics.cs` 644 (NoStorage).
3. A terminal's tray refuses a store part with the reason "the tray is full": `ConstructCrafting.cs` 1064, 1075.
4. **Fixed after the review:** a store part riding a conveyor was dropped from a save, `ConstructConveyor.cs` 1213.
5. The hood's sized strips went to the Long Piece Rack with no route to the Panel Former; strips are the default against the user's
   answer: `ConstructLogisticsPlan.cs` 110.
6. Window tool: `wait`, `put` and `walk` wait in frames at uncapped rates (`Program.WindowShotCuts.cs` 104); b's `_wsLiftWaits` cap
   is the same.

## After the merge review (four points)
1. **A store part riding at a save** was dropped by the load (`ConstructConveyor.ReadSave`) while its order kept it as `Sent`. The
   load now keeps any rider whose part this build knows. Tried: `logi feed 1 Ammo Container`, `saveworld vtride`, then
   `loadworld vtride` in a fresh process: `Ammo Container on 26 (Assembler#284 -> Conveyor Router#287) at 1.41` came back. The whole
   "save mid-ride, the build finishes" wasn't run: on the showcase no conveyor leads from the Assembler to the station.
2. **Done cancelling open orders: taken out again.** `ConstructCrafting.Cancel` removes a machine order only when none of it is made,
   so a part the Assembler had begun would come out unordered, and an unordered store part sticks at the Router (fault 2 above).
   As before, an order left open at Done lands its part in the hold as a spare for the next run; it never jams. The open order
   after Done (`orders 1`) is back, a smaller fault for the station's owner: `ConstructStation.Finish` (~1088).
3. **A loaded store set down in the bay** was removed and kept as a bare def (fitted fresh or into the hold), so what it held was
   lost; this predates the hold fix for the fitted path. `ConstructStation.TakeDropped` now leaves a one-part body whose store holds
   anything (bulk, items, a machine's input) lying in the bay. Tried with a new window-tool verb, `station droploaded NAME` (50 units
   of ammo in it): the loaded container stayed loose and the build waited at 1/2; an empty one dropped after it was taken and the
   build went Done.
4. **The wide rule is kept on purpose:** a station builds any blueprint, a factory with Assemblers and a Piece Rack among them, and
   its hold is a count of defs; everything that reaches it is new and empty (an order's rider, the hands, a stock), and a loose
   loaded one is now left alone (3). Other racks still refuse stores. The rack's own size check limits what fits.

Self-test by name, `Assembling, Assembling network, Extruder, Press stock, Construct storage, Custom bins`: 6 sections, 0 failures
(39.5 s).
