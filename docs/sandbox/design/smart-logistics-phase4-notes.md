# Smart logistics phase 4: notes (2026-11-13)

Branch `claude/smart-logistics-4-56a775`, Windows PC. **No protocol change** (stays 277): the board is each peer's own, from what it
already holds, and the Router's settings ride with the body as every setting does. Code guide: production.md "Smart logistics"
(the bullets "The network board", "The Router's screen", "Window tool"). Design: smart-logistics.md sections 3.2, 3.3, 9 and 12.

## Done
- **The network board** (`ConstructLogisticsBoard.cs`): a Router's third output, Data out, linked to any display (the monitor
  rule) shows that Router's network by the monitor bands: one line; a title, stuck spots, orders, stores; and from 7 rows the counts
  and, under headings, the stuck Routers with piece and reason, each open order with its state and chain lines (maker, or why nobody
  makes it), the machines, the stores (fill, keep), the Routers (last piece, settings). Allocates nothing once each piece kind's name
  is cached. The Router's Alarm and Moving outputs were already there (phase 1).
- **The Router's screen** (`RouterScreen.cs`, part kit `conveyor_junction_parts`): two low plates, one on each side face, under the
  side conveyor beds; Spill and Hold buttons change `routerSpill` and `routerHold` at the screen.
- Window tool: the board printed by `panel Conveyor Router`, `touch ... /N` for a screen, low screens and a joined client's touch.

## Tried by hand (Production Showcase, throwaway save folders)
- Host, `--spawn-at 140,0,-100`: `logi order L Bracket 1`, `logi order Clutch 1`, the Router's way 1 cut, `logi spawn 8`. The board
  (16 rows): `NETWORK - STUCK at Router 1`, `STUCK Router 1: Gear Set - no Piece Rack on the network has room for it`,
  `L Bracket 0/1 queued at Press: ...` with its chain line `Extrusion l-angle 15x15x15 steel 0/1 - Extruder`, `Clutch 0/1 queued
  at Assembler: waiting on Gear Set ...`, the five machines (Stopped, Press Starved), both racks, the Router. The one-line form:
  `STUCK at Router 1: Gear Set - no Piece Rack ...`. Router screen: `STUCK / Holds Gear Set / <reason> / Spill Auto, no hold`.
- Host: `touch Spill: Auto` then `touch Hold: Off` at the Router's screen: the screen and its buttons read `Spill Ahead, holds`.
- Host and joined client (`--net host:27999` / `join:127.0.0.1:27999`, spawn 120,0,-100 so the Router is inside the arena): the
  client's board and Router screen read the same as the host's, line for line.

## Not verified
- **A joined client's press on the Router's buttons.** The tool's press reached the screen once and the host answered "too far
  away" (the client's move hadn't reached it); after the tool waits for the client to land and aims again, the press gave no
  refusal and no change. The buttons sit 2 to 8 cm above the ground, so the ray from a standing eye may meet the ground or a
  conveyor bed first. Not looked into further (time); see question 1.
- **`routerSpill` and `routerHold` moving pieces.** On the showcase every machine is stopped and the pieces `logi spawn` makes have
  no order, so with way 1 cut the Router held a Gear Set stuck (NoStorage: the other rack is not on a way from it) whatever the
  Spill setting; no piece was watched going out a pinned way, nor an ordered piece waiting under Hold. A scratch layout was not
  built (time).
- Verification items 6 (loop and depth guards with two Assemblers through a Router) and 7 (rank between two terminals): not tried.
- The map (design 9.2), tick cost (the board runs only at a panel's fill, in view; not measured), a 16-row board in the real window.

## Choices made (questions for the user)
1. **Where the Router's screen goes.** Q6 took away the post the design put it on. Chosen: a low plate on each side face, under
   the conveyor line (knee height; a conveyor on that side hides it from a standing player). Its buttons are near the ground and a
   joined client's press was not seen to land. Alternatives: (B) a short screen post at one corner after all; (C) no screen on the
   Router, its state on the board's top lines and its settings with the configure tool only. **Answered (user, 2026-11-13): A**, the
   low plate on each side face below the conveyor line, as built. Whether a client's press lands on its low buttons is still open.
2. **The board is a list on every size for now**; the plan map (9.2) is the next step. Is the list enough on a big sign, or is the
   map wanted?
3. **"Router 1, 2 ..."** are numbered in the map's node order (stable while the links don't change), not by anything the player sets.
   Should a Router take a name (the sign tool's text) instead?

## Left for the next phase
`PanelDraw.Map` (layout, conveyors, machines, stores, pieces as dots, the stuck ring, caption); a NETWORK BOARD sign on the
showcase wired to its Router; the client's button press; a scratch layout for `routerSpill`/`routerHold` and verification items 6 and 7.

## Phase 4b (2026-11-13, branch `claude/smart-logistics-4b-71acf2`, no protocol change)

**1. A joined client's press on the Router's buttons: lands; nothing in the game needed changing.** The phase 4 failure was the window
tool: a joined client can't fly itself (the host owns its position and snaps a teleport back), so its `touch` pressed from its spawn
300 m away. Now a client's `touch` presses from where it stands when that is in reach and in front, else fails saying the host must
`bring` it first; `bring` stands a player 1 m from a low screen (in a finger's reach); `touch` prints the ray (`touch ray: button
1.88 m, ground or wall 2.34 m, hit Conveyor Router ... panel 0 button 1`). Host and client (`--net host:28417` / `join:127.0.0.1:28417`,
spawn 120,0,-100): standing 1 m off the side face, eye 1.95 m, the ray meets the Router's button at 1.9 m, the ground only at 2.3 m.
The host logged both presses done and the screens read `Spill: Ahead`, `Hold: On` on both peers (3 runs of 4; the first, before
a log line was added, sent nothing that changed, and it wasn't seen again). Not tried: crouching (a lower eye meets the face at a flatter
angle, so the ground is no nearer), and a conveyor on that side (its bed hangs over the plate: use the other side's screen, as designed).
Tool note: a window run's `wait N` counts frames, which run well over 60 a second, so a host waiting for a client needs thousands.

**2. The Router settings at run time: both behave as `StockTargets.cs` says.** New window-tool verbs: `logi feed N [NAME]` puts pieces
with no order onto the conveyors into a Router (the showcase's own lines stall before the Router), `logi mend PART WAY` repairs a cut
way. On the showcase (its Router has way 1 to the Piece Rack and way 3 to the Crafting Terminal, no way 2):
- `routerSpill` 3: an unordered Gear Set left by way 3; back on Auto the next went by way 1 to the rack.
- `logi order L Bracket 1`, `routerHold` 1, way 3 cut, an L Bracket fed: it took order 1 at its source, waited at the Router marked
  stuck (`no way to it: a conveyor on the way is cut or missing`), alarm 1, the Router's screen `STUCK / Holds L Bracket`; mended, it
  went on by way 3 with its order. (A piece takes some seconds to reach the Router's end of its input: look after that.)
- Seen, not changed: a piece pinned down a spill way keeps its storage destination (the Piece Rack) though that way leads to the
  terminal; it hadn't reached the end of the 11.5 m lane in the run, so what the terminal does with it is not seen. A held piece's reason
  stays in its flags after it goes on (the rider note doesn't show it).

**3. A Panel Former's sheet sized to the panel: not done (time).** What it needs: `FormerInputs` gets the world, body and network
(its caller `Need` has them); the key is a solid section the panel's width (5 cm steps), 10 cm thick, cut so one piece holds the panel's
units (`HullLoft.FormedUnits`) with less than a 5 cm step to spare (units per cm from one def, so only one new extrusion def per panel
size); if no Extruder on the network takes it (`WorkOrders.Refusal`: `ExtMaxSection`, the welded die, `ExtMaxLength`), the width
halves down to what one takes and the count goes up; if none takes any, the chain line says the Extruder's refusal, as now.

**4. The plan map: skipped.** Question 2 above has no answer recorded yet.

**5. A NETWORK BOARD sign on the Showcase, 6. verification items 6 and 7: not done (time).**

`--check-construct-parts` and `--gen-parts all --check` pass. No self-test sections run (no game code changed outside the window tool).
