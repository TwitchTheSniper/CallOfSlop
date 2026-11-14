# Logistics leftovers 3: notes (2026-11-14)

Branch `claude/logistics-leftovers-3-1f6f2e`, Windows PC, main at protocol 311. **No wire change, no part ids moved** (protocol stays).
Each fault was seen in a throwaway window run first (`--window-shot ... --save-dir %TEMP%\ll3\... --spawn-at 140,0,-100`), then tried
again the same way. Scratch blueprints (`VT Ammo Rig`, `VT Join Rig`, `VT Wheel Rig`, `VT Pane Rig`, `VT Frame Rig`) lived in `bin/` only and are deleted.

| # | Fault | Result |
| --- | --- | --- |
| 1 | A terminal's wing order never finishes | **Fixed**: refused with the reason, host and joined client |
| 2 | The station's note says "queued at the Assembler" while the part rides | **Fixed** |
| 3 | Orders left open after Done | **Fixed** |
| 4 | Two Panel Formers through a Join into one cutter: the glass blank stopped | **Not reproduced** (below) |
| 5 | A client's order by message reached no order | **Not a fault**: refusals the client was told (below) |
| 6 | "Press stock" waits 3 s of wall clock | **Fixed**: simulated seconds; 5 of 5 passes alone |
| 7 | Two-level Assembler order and a stock target on one machine | **Fault found and fixed** |
| 8 | A pane with cut corners stops a round | **Fixed** |
| 9 | Die Frames to 8 m | **Done** in the generator |

## 1. A machine that drops its piece loose
Seen: `logi order Wing Segment 1` placed; the Airframe Former made the wing (`stock Airframe Former`: "loose near it: Wing Segment x1") and the
order read `queued at the Airframe Former` 60 s later. Cause: an Extruder or Former with no conveyor out of its Piece out (nor a chute into a press)
parts its piece off loose (`ConstructExtruder.Cut`, `CutAs`), which no tray sees. `ConstructCrafting.LooseRefusal` (unless a lifter edge starts at
the machine: an arm lifts it) is asked in `Place`, in the step's machine choice, and the terminal's list rows (`can't make`, with the reason). Now:
`the Airframe Former drops it loose, no conveyor to the tray: order it on its panel, collect it there`; a joined client's `logi send` gets it as a notice.

## 2. The note follows the part
`StepTerminal`: a queued order whose asked parts are all on the conveyors to its tray (`ConstructLogistics.RidingFor`, its tag, not storage) reads
Riding; a Building Station says `riding to the hold` (a fetched part too, which read "riding to the tray"). Tried: `waits for 1 part: Ammo Container
(riding to the hold)` while the container rode way 2, host and client.

## 3. Done closes its orders
`ConstructStation.Finish` calls `CancelOrders` (claims released, unstarted lines off their machines, pad claims). Seen before: `station stock` while the
container was queued, Done with `orders 1` and `queue Assembler#284: Ammo Container 0/1`; after: `orders 0`, the Assembler's queue empty.

## 4. Join into a cutter: not reproduced
`VT Join Rig`: steel and glass Panel Formers (each chuted its sheet) into one Conveyor Join (`Piece in` and `Piece in 2`, then `Piece in 3`), the
Join into a Laser Cutter, its out to a rack. Orders `Cut Pane (r40,30,0 t20 glass)` and a steel `Cut Panel (curve 40x30 ... | r20,15,10,10,0)` (also
100x60), their blanks queued at the formers: both blanks passed the Join, both cut, both in the rack. The Join code (`ConstructConveyor.Pass`,
`Takes`) refuses a piece only when the out conveyor is cut, narrower than it, shorter than it or full. The phase 6 layout is gone; if it comes back,
`logi riders` and `logi why` on that conveyor say which.

## 5. A client's order: not a fault
Two processes on the showcase. Without `disown` the client heard `notice: Owned by SeabTower - defended`; sent before the host's `bring`,
`too far away`; a Gear Set, `the Press holds a Bracket Die`. With `disown` and the client in reach, `logi send x@302 Ammo Container 1` placed
`#1 0/1 Ammo Container: queued at the Assembler` on both peers. The fix-ups run's host closed early and had not disowned.

## 6. Press stock
`SelfTestPressStock.cs` `Await` counts the dt each update moved (at most 0.1 s), with a wall cap only against a hang. Alone, five runs:
18.4, 13.3, 13.1, 13.1, 13.0 s, all passed.

## 7. One Assembler, a Wheel and a Tyre Carcass target
`VT Wheel Rig`: one Assembler (steel, rubber, cloth chuted) out to a Router whose ways go to a Crafting Terminal, a Piece Rack and back into the
Assembler's own Piece in. The rack holds 1 Tyre Carcass, keep 3; `logi order Wheel 2`. Seen: link `Tyre Carcass for Assembler#21 by nobody: nothing
on the network makes it`, the Wheel line heading the queue Starved, the target's refill line behind it: nothing ran, ever. Cause: `FindMaker` never
asked the machine that needs the input (`mk == e.Dest`). Now it may when its own Piece out comes back to its Piece in (`RouteTo(c, mk, mk)`), and
`AheadOfUsers` puts the carcass line first. Tried: both links by Assembler #21, 2 Wheels made, the target at 3, `no refill`.
Left as is: the Wheels went to the rack, since a Wheel is too big for a terminal's tray; the order says `no route from the Piece Rack: collect it
there`, and the Router's reason reads `its destination is full`. A truer "too big for the tray" on both is a follow-up.
**Showcase fault (its owner's):** the Clutch corner (`ChainCorner`) can't work: the Gear Set is pressed now, `no recipe makes the Gear Set`, and
the Press holds a Bracket Die.

## 8. A cut-corner pane is glass
`ConstructWorld.GlassBox`: a hidden box counts as glass when every drawn box of its part is glass (the pane's glass hull over its hidden box). A part
with any other drawn box keeps its hidden boxes solid. `ray` (window tool) now aims at a pane (U,V in cm from its middle). Before: `hits Cut Pane
(r40,30,5 t20 glass) (part 1)`; after: `(part 1, glass)`, as the square pane; its cut corner passes to the floor.

## 9. Die Frames to 8 m
`ExtrusionParts.cs`: the Small Extruder's `maxSection` 8 x 8, its Width and Height sliders to 8 m, the Die Frame's text, `Growth`'s Small row.
Readers go through the data (`Reach`, `MostSection`, `WidenWith`, `TooWideAdvice`). Tried: a 7 x 7 m order on a plain Small reads `attach Die Frames
to its sides (its die reaches 8 x 8 m with them)`, 8.5 m `build a Medium Extruder`. `--check-construct-parts`: 0 REFUSED. A fully framed 8 m die
was not built. MANUAL.md and production.md say 8 m.

## Not verified
A joined client's view of item 3 and 7; item 8 with a real round (the ray's glass flag is what rounds read); `--gen-parts all --check` (CRLF here).

Shared files touched: `ConstructLogistics.cs` (`RidingFor`), `ConstructWorld.cs` (`GlassBox`), `Program.WindowShotCuts.cs` (the pane aim),
`SelfTestPressStock.cs`, `docs/MANUAL.md`, `docs/sandbox/constructs/production.md`, the two generated part files.
