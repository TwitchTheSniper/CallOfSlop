# Production Showcase repair (2026-11-13; main at 9b5a1de2, protocol 304, no wire change)

The faults `verification-2026-11-13c.md` could not get past, each seen in a throwaway window run first
(`dotnet bin\Release\net10.0\CallOfSlop.dll --window-shot OUT.png --save-dir %TEMP%\sr-* --no-splash --spawn-at 140,0,-100
--blueprint "Production Showcase" --do "..."`, host only). The showcase is made by `--gen-parts production_showcase`
(`Source/PartKit/Generators/ProductionShowcase.cs`); it now has 531 steps (471 before) and is 86 by 20 m.

| # | Fault | Seen | Cause | Done |
| --- | --- | --- | --- | --- |
| 1 | The Assembler never took rubber | `logi order Ammo Container 1`: the terminal's row "needs 1 rubber" | Its only feed was a steel Storage Volume | A rubber Storage Volume chuted to it (`StockOf("rubber", 47.25, -13.25)`): the reason is gone |
| 2 | `station stock` for the Buggy: "Wheel: rack full" | 13 in, 13 refused | The hold packs one column per kind along its 4.87 m; 7 of the Buggy's 15 kinds took 4.82 m (`Square Beam 2 m` alone 2 m). Not a layout fault: in real flow parts ride in as the hold empties | No change to the station. `StationProbe.Stock` now says how full the hold is ("7 column(s) of 7 kind(s) over 4.82 of 4.87 m"). The Buggy built 26/26 in 34.3 s, 14 parts by the arms |
| 3 | The wing order stayed Blocked | (the scratch `VT Showcase Air` is gone) | An Airframe Former has no conveyor out: its wing grows up to 6 m along its die (+z) and parts off. The swapped layout had the wheel rack 3 m in front of it | An Airframe Former on the back strip with 6 m of clear floor east of its die. The wing forms and drops beside it (`stock Airframe Former`: "loose near it: Wing Segment x1") |
| 4 | Loader Arm slots 1 to 3 had no power | Under `timed` all four `lifter place` slots, slot 0 too, read "No power" | A placed arm is a body of its own with no battery (`ConstructRecycler.Powered`) | Two Loader Arms on the showcase body at the bay's corners (slots 0 and 1), on its mains. Slots 2 and 3, 6 m out, read "NoPoint": a Scrap arm's 4 m doesn't reach the hold from there, so they were left out |
| 5 | No route from the Assembler to the station | | | The Assembler's Router's way 2: south, along the row's back edge, up between the terminal and the station into Piece in 2 (0.5 m, an Ammo Container is 0.42) |
| 6 | No Laser Cutter | | | Back strip: plate Extruder (0.5 x 0.1 x 1 m steel), Router, cutter, the cutter's Piece Rack; a spare Piece Rack on the Router's way 2; all on the orders console (cables along the front aisle and down the station floor's east edge). First try jammed: the Router said "its input 52: jammed: the conveyor after the junction is full", a 0.6 m conveyor into the cutter can't hold a 1 m blank. Both cutter conveyors are now 2 m |
| 7 | The window tool's waits counted frames | (the cause of 13c's lost joins) | | Only the verbs that wait on the simulation count ticks (`WsWaitTicks`): `wait N` is N ticks of real time (60 a second) and at least min(N, 3) frames, and `put`, `walk` (its retries, its held key and its wait), the lifter waits and `cutter await` follow it. Everything that measures frames keeps frames (the merge review): `--bench`, the tour's `perf`, `design drag`, `station await`; `walk`'s held key is timed in ticks; the lifter verbs' limits are real seconds (`WsLiftWait`: 20 s, 180 s, 180 s); the blueprint's set-down waits 10 s |

## The checks 13c could not run (host, creative, the line started with `touch Start @ Line Console#2`)
- **An Assembler-made container riding to the hold:** a scratch two-part blueprint (Square Beam 2 m + Ammo Container, in `bin/` only,
  deleted): the station ordered the container of the Assembler, which made it from steel and rubber; the Router sent it down way 2
  (`Ammo Container on 41 (Conveyor Router -> Building Station) ... order 2`) and the build went 2/2 at 52.8 s, both parts fitted by the
  back-corner arm. The ride is slow (about 0.4 m/s over 18 m).
- **The Loader Arm's side pick and elbows:** the Buggy, 14 carries by the two arms, 0 IK samples clamped, the elbow printed at mid-move.
  The pin (a player penned in the arm's path) and a joined client's elbows were **not** tried.
- **Gas from a bin:** a scratch Square Beam + Fluid Tank set to hydrogen 100 %, `fill Storage Volume#11 100 hydrogen` (the station's
  copper bin): the tank came out "40 of 40 l hydrogen". Whether the bin lost the unit was not seen (it read 100 after; creative).
- **The cutter:** a terminal order of `Cut Plate (solid 50x10x100 steel | r50,25,30,20,0)` chained its blank from the plate Extruder;
  `cutter await` 18.0 s (the law says 17.0 s plus the bed's take), 1.2 units of offcut, the part in the cutter's rack.

## Handed over (not fixed here)
- **A terminal's wing order never finishes** (logistics): `WorkOrders.Queue` takes a Crafting Terminal's order at an Airframe Former
  (`ConstructCrafting.cs` ~1162), the former makes the wing and drops it, and the row stays "queued at the Airframe Former" for ever:
  a whole-former part has no conveyor to a tray. The terminal should refuse it or say "collect it at the former".
- **The station's note lags** (station): while the ordered container rode way 2 the job's note still read "queued at the Assembler".
- **The cutter's part ends in its rack**: the terminal then says "no route from the Piece Rack: collect it there", which is the truth;
  routing the cut rack to the tray was out of this round's scope.

## Not verified
A joined client on the repaired showcase; the arm's pin; a `timed` (survival-rules) run of the cutter and the station; the full
self-test (not run, by the rules). `--check-construct-parts`: no REFUSED lines. `--look-report`: paint 94 %, dark 6 %, 17 colours
(main's 16 plus the cutter's `#a8365e`).
- After the merge review: `lifter place` with no slot on a body that already has arms (the showcase) defaults to slot 2, since slots 0 and 1 landed on the built-in arms.
