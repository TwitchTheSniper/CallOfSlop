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
   Router, its state on the board's top lines and its settings with the configure tool only. (Asked of the coordinator at the start;
   no answer during the phase.)
2. **The board is a list on every size for now**; the plan map (9.2) is the next step. Is the list enough on a big sign, or is the
   map wanted?
3. **"Router 1, 2 ..."** are numbered in the map's node order (stable while the links don't change), not by anything the player sets.
   Should a Router take a name (the sign tool's text) instead?

## Left for the next phase
`PanelDraw.Map` (layout, conveyors, machines, stores, pieces as dots, the stuck ring, caption); a NETWORK BOARD sign on the
showcase wired to its Router; the client's button press; a scratch layout for `routerSpill`/`routerHold` and verification items 6 and 7.
