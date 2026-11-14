# Client faults, 2026-11-14: three doubts from verification settled

What `verification-2026-11-14b.md` (and `-14.md`) could not explain about joined clients, each traced with a host and a joined client
(`--net host:PORT` / `--net join:127.0.0.1:PORT`, the client started 25 s after the host, the Production Showcase at `--spawn-at 140,0,-100`)
before and after the change. Throwaway save folders only.

| # | Doubt | Cause | Where | Fixed in |
| --- | --- | --- | --- | --- |
| 1 | A client's cut panel ordered, never cut | Two: the host's Start after `design bring` did nothing (the tool, item 3a); and **a game fault**: the chained blank was made again and again | `ConstructLogisticsPlan.Arrived`, `Step`, `Serve` | the game, and the tool |
| 2 | The Line Console's Down does nothing | The tool's `panel` dump filled a fresh card, so it read `scroll 0/4` whatever was drawn; in two-window runs the press itself was lost too (3a). A player's Down works on host and client | `Program.WindowShot.cs` `WsFill` | the tool |
| 3 | A client "never left its spawn", "held keys never reached the seat" | The tool, four ways (below). A real client walks, presses panel buttons, sits and drives | `Program.WindowShot.cs` | the tool |

## 1. The client's cut panel

**Before.** Host `disown; design bring 1; touch Start @ Line Console#2; ...`, client `design await; design width 50; design length 50; design cut
r25,25,20,20,5; design order`: the line stayed `Stopped 0/8` (the host's Start lost, 3a). With the line running (after 3a), the order queued at
`Laser Cutter#502` but the cutter stayed `NoMaterial` for good, offline as well as on a client. The chain asked the Panel Former for the blank;
the blank rode `Panel Former#422 -> Long Piece Rack#423` tagged for the cutter (`dest Laser Cutter#502`), the rack took it, and after 70 s the
rack held **two** Formed Panels with a third riding in, for an order of one.

**Cause.** `ConstructLogistics.AtSource` tags a chained piece for its machine "Router or not"; on the showcase the Panel Former's only conveyor
ends at a Long Piece Rack with no way on to the cutter (run 4 found that gap, `Source/PartKit/Generators/ProductionShowcase.cs`). The rack takes
the piece; `ConstructLogisticsPlan.Arrived` ignored a piece that reached a store that isn't its link's ("a piece tagged for another machine"),
so it counted as neither riding, held nor arrived, and `Step` (the maker check, about line 649) saw the maker's order gone with the link
short and asked the Panel Former again: a new blank into the rack every pass, the cutter never fed, no word on the row. A player building a
line whose maker's out has no way to the machine would hit the same.

**Fix (game), after the merge review.** No claim is kept (a first version claimed the stranded blank in `world.Reserve`, which is never
saved, so a load made it again; it also covered stock refills that `Step` never closed). `ConstructLogisticsPlan.Serve` now counts free
pieces of the link's kind that lie in a store on its network with no conveyor way and no lifter's edge on to the machine (`Lying`; a lifter's
pick rack counts as riding already, `AtPicks`) before it asks a maker, from what the stores hold, so a load needs nothing kept. The row says
why: `the Formed Panel (...) waits in the Long Piece Rack: no conveyor or arm from the Long Piece Rack to the Laser Cutter: link one, a hand
can't load it` (a Laser Cutter takes its blank only by conveyor: there is no hand path onto its bed, so "carry it there" would be wrong). And
the plan says it when it asks: while the maker works, the row reads `Formed Panel (...) 0/1 from the Panel Former; no conveyor or arm from the
Long Piece Rack to the Laser Cutter: link one, ...` (`EndsShort`: the maker's only conveyor ends at a rack with no way on). Stock targets are
left as before (a refill never counts a piece elsewhere). When a way is linked later, `FromStock` claims the piece and sends it. No wire change.

**After.** Offline and on host and client: one blank made, `stock: Long Piece Rack: Formed Panel (curve 50x50 ...) x1` and nothing more riding;
both peers' rows read the reason. Saved (`saveworld`) and loaded in a fresh process: after 50 s still one blank, nothing made, the same row.
The blank lifted out of the rack (`logi take`): the Panel Former made exactly one more, then the row waited again (a blank taken away is gone
for this order: no hand can load it). A blank fed to the cutter by conveyor (`cutter feed`) was cut in 4.2 s; both peers read `Collect` and
`stock: Piece Rack: Cut Panel (curve 50x50 c10 r0 t20 steel | r25,25,20,20,5) x1` (tried with the first version; the cut path is unchanged).

**Not done.** The showcase's own gap (no way from the Panel Former to the cutter) is a generator change (`ProductionShowcase.cs`) and
is left to its owner: until then a designer's cut panel on the showcase waits with that reason. A free piece of that kind a player keeps in
an unlinked rack also counts (the row names the rack), and two links of one kind and machine count the same piece.

## 2. The Line Console's Down

**Before.** `panel Line Console#2` after `touch Down` read `Running 5/8  1-4 of 8`, `scroll 0/4`, offline and on both peers.

**Cause.** Up and Down scroll this PC's own view of the list (`PanelRender.Scroll`, `Program.SandboxPointer.cs` about line 221; nothing is
sent, by design). The `panel` dump (`Program.WindowShot.cs` `WsFill`) made a new `PanelCard` with scroll 0, so it never showed the drawn
place. The cursor on Extruder 1 is the line's pick (Next), which Down doesn't move. In two-window runs the press was also lost (3a).

**Fix (tool).** `PanelRender.ViewOf` gives the slot's scroll and follow as last drawn; `WsFill` fills with them.

**After.** Host and client each press Down: `scroll 4/4`, `Running 5/8  5-8 of 8`. Line 2's rows 5 to 8 on the showcase: **Panel Former**
(Stopped, own settings), **Airframe Former** (Running, own settings), **Extruder 3** (Running, own settings), **Laser Cutter** (Stopped, own settings).

## 3. A joined client's steps

Settled with two processes: the client walked (`hold MoveForward 3`: 30 m), pressed Down on the Line Console (scrolled), sat in a Bandit
Buggy 1's Seat (`seated True`) and held MoveForward 4 s: the buggy went 37 m, the host's copy at the same place, as offline. **No game fault.**
The tool's four:

- **a. Focus.** A window that loses the focus to the other process lets the mouse go (`Program.cs` `OnFocus`, about line 636), and
  `SandboxInput` reads no tool, use or panel key while the mouse is free (`Program.Sandbox.cs` line 119). Only `hold` caught it again. The host's
  `touch Start` after the client's window opened did nothing and said nothing. Now `use`, `key`, `wheel`, `seat` (and `touch`, through its
  `key Fire`) hold it again first (`WsGrabMouse`, not while a menu or a talk has the mouse) and print `mouse: held again`.
- **b. The wrong body.** A client spawns nothing, so `WsFind` took the first body with the name: `Seat` was construct 32 at (-142, -152), a car
  by the spawn, not the host's buggy. Now a client takes the nearest body holding the n-th of that part, and says so when several do.
- **c. Flying a client.** `near`, `from` and the like teleport the local player; the host owns a client's position and takes the jump back
  (shown: `from Seat` left the client at its spawn). Now the first such jump prints a note: the host brings it (`bring PART`) or it walks.
- **d. A hidden middle.** From where `bring` stands it, a seat's middle was behind the buggy's hood (`aim Hull Loft`, `nothing to use there`).
  `aim`/`near` now aim at the first of 26 points of the part's box the crosshair's ray meets (`WsSeenSpot`).

New: `waitnear PART[#n] [M]` waits (90 s at most) until the player stands in reach of the part, so a client acts after the host's `bring`
instead of on a guessed time; `aim`/`near`/`panel` on a client look again for 30 s for a body that hasn't arrived; `ui` prints the eye, what
the crosshair meets and the HUD's look line. A client's seat, as run here: host `disown; secs 45; bring Seat`, client `waitnear Seat; aim Seat;
use; wait 30; ui; hold MoveForward 4; secs 5; where`.

**Not checked.** A client walking south from the buggy (145, -95) reached y -20 at (145, -129); not looked into (likely the Proving Ground's
ground there, not confirmed).

## Files

Game: `Source/Constructs/ConstructLogisticsPlan.cs` (`Serve`, `Lying`, `EndsShort`, `Note`), `Source/Constructs/ConstructLifterNet.cs`
(`LiftsTo`). Tool: `Source/App/Program.WindowShot.cs` (`WsGrabMouse`, `WsFind`, `WsSeenSpot`, `waitnear`, `WsFill`, `ui`, `WsEyeTo`),
`Source/App/Program.WindowShotTalk.cs` (`seat`: one line), `Source/Render/PanelRender.cs` (`ViewOf`).
