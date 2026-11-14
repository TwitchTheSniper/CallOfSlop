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

**4. The plan map: built after the user's answer below** (`ConstructLogisticsMap.cs` `NetPlan`, `PanelRender.MapPlot`, `PanelDraw.Map`
at the end of the enum; production.md "The plan map"). A board of 15 rows up on a panel 0.8 m wide or more draws the map; smaller
ones keep the list. Tried on the showcase (L Bracket order, Hold on, way 3 cut, three pieces fed): the plan read 14 conveyors (1 cut),
5 machines, 2 stores, the terminal, the Router, a splitter and 3 joins, the Router ringed; in the real window the new sign showed the
lines, the tagged squares, the store boxes, the red dashed cut way, white dots and the blinking ring, with `STUCK at Router 1: L Bracket
- no way to it ...` and `3 moving  1 order  storage 0%` under it. Not built: keep-mark ticks on stores, the Router's chosen-way stub,
dots gliding between fills (they step at each fill), a 1,024-quad cap on the static layer (lines are capped, nodes and dots are not).
Not measured: the fill's cost on a big network (one pass over the riders and the network's nodes, in view only).

**5. A NETWORK BOARD sign on the Showcase: done** (generator `NetworkBoard`): a Medium Text Sign (4 x 2 m, 16 rows) on an Adapter
Medium to Small on a Frame Beam in the back aisle at x 44.25, wired from the Router's Data out along the aisle edge (z -8.55).
`--gen-parts production_showcase` rewrote the blueprint; `--check-construct-parts` and `--gen-parts all --check` pass.

**6. Verification items 6 and 7: not done (time).** **Router names** (the user's answer below): not built; left for the next phase.

No self-test sections run. No protocol change: the map is each viewer's own, from what its peer already holds.

**The user's answers (2026-11-13), after phase 4b:** (question 2) the plan map **is wanted**: on big signs, the list staying on small
ones. (Question 3) a Router **takes a name** the player sets with the sign tool's text; an unnamed one keeps its number, and the board
and the order lines use the name ("held at North Gate").

## Phase 4c (2026-11-13, branch `claude/smart-logistics-4c-5ca7c5`, protocol 291)

All tried in throwaway window runs on the Production Showcase (`--spawn-at 140,0,-100`, temp save folders); scratch data in `bin/` only,
restored after (its `Assembling.json` hash checked equal to the committed one).

**1. A Panel Former's chained order asks for sheet sized to the panel** (`ConstructLogisticsPlan.SheetFor`). A solid section the panel's
width (5 cm steps), 10 cm thick, as long as one panel's units need; when no Extruder on the network takes that width, the panel is split
across into the fewest equal strips the widest one takes (by its most section and its die as welded), each holding its share, so the count
goes up; a piece past the best one's most length is cut there. None takes any: the full-width sheet, and the Extruder's refusal on the line.
Seen: the showcase's sheet Extruder takes 0.5 m; a Hood 160 x 150 asked for 3 x `solid 50x10x40`, a Door skin 110 x 120 x2 for 6 x
`solid 40x10x25`. With the bins filled and Line Console #2 started, the hood's three strips were made, rode to the Panel Former and it
went Running; the door's were on the conveyor. (Splitting into strips rather than halving: halving 150 cm gave 75 then 35 cm, past a 50 cm
die that took 50.)

**2. Fan and Airframe Formers as makers** (the formers' notes' change, `ConstructCrafting.cs`, and `FindMaker`, `Kin`, the piece line
`Prop`/`Span` in `MachineScreens.cs`). A scratch copy of the showcase with its Wheel Former swapped for an Airframe Former: the terminal's
list showed Wing Segment, Short Wing Segment and Swept Wing L/R by the Airframe Former (make 2 "needs steel: a linked bin holds some",
make 1 once filled); `logi order Wing Segment 1` and `logi order station Wing Segment 1` both went to the former's queue. A chained
(depth 1+) wing or rotor can't happen today: catalogue wings are physics-only, so no Assembler recipe may take one (the table refuses it);
the chain path is there for when one can. No Fan Former preset exists (rotors stayed off the list), so the Fan side is untried.

**3. Routers take a name** (protocol 291). A Router holds one text slot (`TextSlots` 1 for `"router"` parts), set in the configure
tool's **Name** box; F with Hands on a Router still presses its screen (`ControlFrame` skips Routers). The board (title, stuck lines,
Routers list, the map caption), a conveyor's held line and an order's note while a piece of it is held (`HeldNote`) use it; unnamed keeps
`Router N`. Seen: host `NETWORK - STUCK at North Gate`, `STUCK North Gate: L Bracket - ...`, ` North Gate: stuck`, `its input 26: held
at North Gate: ...`; a joined client set `South Gate` and both peers' boards read it; a world saved and loaded kept `North Gate`.
Not seen: an order's note reading "held at" (the fed pieces had no order; the code path is the stuck rider's own tag).

**4. The board's leftovers.** Keep marks: a store with a keep shows an amber tick where that keep of each kind it holds would fill
(seen: keep 20 Gear Sets on a rack of 480: fill 0.59, mark 0.02). The Router's chosen-way stub: a bright stub along the conveyor it last
sent along (seen: `way stub <1.00, 0.00>`). Fill cost: 1-2 us a fill and 0 bytes on the showcase (17 nodes, 14 conveyors, 130 riders);
**not measured on a big network** (none to hand; the fill is linear in nodes, riders and a Router's conveyors). Window tool: the map's
node lines print the marks and stubs, and `map fill:` times 200 fills.

**5. Verification items 6 and 7.** Item 6 **works**, on the showcase's own two Assemblers through its Router with scratch recipes: a
Clutch <-> Reduction Gearbox loop gave link 2 `recipe loop: the Clutch needs itself` (maker nobody); a seven-deep chain (Clutch, Chain
Drive 1:1, 2:1, Drive Shafts, Right-Angle and Reduction Gearbox, Wheel) grew one level a step, the makers alternating, and link 7 read
`waiting on Wheel: chain too deep (6 levels)`, which the order's note showed. Seen: with the loop, the order's note names the first link
("the Assembler makes it"), not the blocked one below it. Item 7 (rank between two terminals): **not done** (time).

Checks: build; `--check-construct-parts` and `--gen-parts all --check` pass; self-test by name `Display screens, Wheel orders`: 0
failures. Files outside the logistics set touched: `ConstructParts.cs` (one line: a Router's text slot), `Program.SandboxControls.cs`
(one condition), `UiSandboxConfig.cs` (the box's heading and tooltip), `PanelRender.cs` (the map's two marks), `Program.WindowShot*.cs`
(tool lines; the step-name line untouched), `ServerBrowser.cs`.

### Questions for the user (phase 4c)
1. **Order note with a blocked link below.** An order whose chain is blocked two levels down (a loop) shows the first link's "the
   Assembler makes it". (a) Keep (the chain lines below show the rest); (b) the note shows the deepest blocked link first. **Recommended: (b).**
2. **Strips for a wide panel** split across into equal widths the widest Extruder takes. (a) Keep; (b) prefer one long narrow strip per
   panel (fewer pieces, less like a sheet). **Recommended: (a).**
3. **Naming with F in hand.** A Router's name is set only in the configure tool's box, since F with Hands presses its screen buttons.
   (a) Keep; (b) F on the Router's body (off its screens) opens the typing box as on a sign. **Recommended: (a).**

### The user's answers to the phase 4c questions (2026-11-13)

2. **Strips for a wide panel.** In the user's words: "When possible, it should be encouraged that the player just builds a larger
   extruder or adds expansion pieces to the extruder to allow them to print larger things. When those options aren't available or
   not possible, then you should have the option to keep equal width strips that you'll assemble later." So, to build: when a
   panel is wider than the linked Extruder's die, the order first says so and points at a bigger Extruder (or expansion pieces
   for one, which do not exist yet: a design item); splitting into equal-width strips is an option the player chooses, not what
   happens by default. Where the choice lives (the Panel Former's setting, or the order) is open.
3. **A Router's name** is set with the configure tool only; no Name button on its screen (as built).
1. (Which link an order's note names when its chain is blocked lower down) was not answered: the recommendation stands for now,
   the deepest blocked link and its reason.
