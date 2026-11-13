# Laser Cutter, phase 4: notes (2026-11-13)

Phase 4 as the coordinator gave it: a Laser Cutter takes its work as orders from the Panel Designer, the Crafting Terminal and a blueprint's
bill, its order chaining its blank as a press order chains its blank, and the picker that chooses a cut. That is the design's phase 4 (the
designer's Cut-outs) and most of its phase 5 (chains, bills, the terminal, the station, the picker), less what is listed under "Not done".
Protocol 301 (main was 300; the merger renumbers). The user-facing summary is `docs/sandbox/constructs/production.md` "The Laser Cutter".

## What was built

- **Terminal and designer orders** (`ConstructCrafting.cs`, `GameSandbox.cs`): `CraftNamed` and a terminal's order list (wire and save)
  take a cut key; `DefOf` and `NameOf` know one ("Cut hood 160 x 150 cm, steel, 2 cuts", `HullLoft.CutLabel`). A Crafting Terminal lists, per
  linked cutter, the world's cut designs (`CutterMissing`: now with the blank in linked stock or on the bed, through a chain when a linked
  machine makes the blank, else what is missing), and cut parts in linked stores as stock. The Panel Designer is a terminal, so its ORDER is
  the same path.
- **The chain** (`ConstructLogisticsPlan.cs`): a cut order queued at a cutter roots a chain (`Asked`); `CutterInputs` asks for its blank (a
  formed panel, or the plate or flat bar's extrusion), counting a blank already on the bed. `OrderOf` turns a formed-panel kind into a Panel
  Former's named order, and `FindMaker` lets formers take named orders. The former's output rides to the cutter tagged for its link.
- **Routing** (`ConstructLogistics.cs`, 3 lines in `WantAt`): a cutter's want is its chained blank first (the link's id), then the Router
  rule of phase 3 (`BedTag`), and now never a piece a terminal ordered from the same machine (the verification finding, below).
- **Bills** (`WorkOrders.cs`): `Bill.Cut`; a cut step adds its blank to `Bill.Panels` or `Bill.Pieces`; `ToConsole` sends cut lines; a bill line
  `{"step": n, "cut": KEY, "count": n}`; `--bill` prints "Cut panels" (the smallest cutter, its blank, seconds, offcut) and counts them in Total;
  a console's Send reads a cutter's refusal as the one of the order's kind.
- **Building Station** (`ConstructStation.cs`): `OrderFor` gives a cut key its order; its kit list puts cut parts under PANELS.
- **The designer's Cut-outs** (`UiPanelDesigner.cs`): under the sliders, up to six cuts, each a row of sliders (an opening's middle along and
  across, long, wide, corners cut back; a corner's which, along, across), + Opening, + Corner, Mirror (a twin of the last across the centre
  line), Clear, x per cut. The cut key is checked by `HullLoft.CutRefusal` (its words in red; ORDER held), each linked cutter's answer is listed
  ("Laser Cutter 1: yes, 6.7 s a cut", too large, too tall, none linked). Save and ORDER use the cut key; a cut design loads back with its cuts.
  No run-time part is made for a key the sliders pass through (the readouts use `HullLoft.CutJob` and the preview's bounds).
- **The preview shows the cuts** (`HullLoft.PanelPreview`): `Want(shape, cutKey)` builds the cut part through the same `PanelJson` with its
  plan, adds the opening sockets as `CutFirst` does, and gives each piece its facet's normal. Its hash equals the made part's.
- **The Design button and picker** (`LaserCutterParts.cs`, `UiRecipePicker.cs`, `Program.SandboxRecipe.cs`, `PanelTouch.cs`): the cutters'
  boards gain Design (`machine.recipe`); it opens the picker on the world's cut designs (dim when this cutter's bed can't take it), and a
  pick sends `CraftNamed` with the cutter as its terminal, which the server queues there (reach and ownership checked; the Order count).
- `PanelDesigns.Refusal` accepts a cut key that passes its rules. Window tool: `design cut rU,V,A,B,C | mK,A,B | clear | drop N`; the
  `design` report shows the cut key, refusals and the preview's cut key; `design held` and `design parts` count cut parts.

Shared files touched (owned by others): `ConstructLogistics.cs` (3 lines in `WantAt`), `ConstructLogisticsPlan.cs` (`Asked`, `Need`,
`OrderOf`, `FindMaker`'s filter, the new `CutterInputs`), `ConstructCrafting.cs` (cutter rows and readers), `WorkOrders.cs` (bill), `ConstructStation.cs` (2 lines).

## The verification finding (`ConstructLogistics.WantAt`)

A cutter's want answered `BedTag` for any piece that was its head order's blank, so a Router could send it a formed panel that a terminal
had ordered from that former for itself. Fixed in `WantAt`: a cutter wants a piece only when its own chain asked for it, or when no
terminal order asked that machine for that kind (`OlderAsked(c, maker, d, uint.MaxValue)`). **Not confirmed by a run before the fix and not
tried with a Router after it**: the reading of the code is the evidence. The chain-tagged path (no Router) was tried and works.

## Tried by hand (this PC)

Scratch blueprints in `bin` only (removed): a Panel Former, a small Laser Cutter, a Panel Designer and a Crafting Terminal on foundations,
conveyors former to cutter and cutter to the designer (or the terminal), 400 steel in the former. Proving Ground.

- **Designer, host**: a hood with a 40 x 30 cm window (5 cm corners) and a 20 cm cut corner drawn by the window tool's cut verbs, saved,
  ORDERed: the order queued at the cutter, its chain asked the former for the formed hood, the former formed it, it rode to the cutter, was
  cut (1.57 m of edge, 6.7 s by the law, offcut +0.5 steel) and arrived in the designer's tray (order 1/1). The design is in the world's list.
- **Designer, joined client** (`--net host` / `join`): the client drew, saved and ORDERed the same; the host got the order and the design;
  both peers held the cut panel in the tray with hash `1b0a78dc`, the client's preview the same hash.
- **Crafting Terminal** (offline): `logi order KEY` placed the cut order; it queued at the cutter and chained the former's blank. The cut part
  then waited at the cutter, "conveyor too narrow" (the scratch conveyor to the terminal was the default width): a layout matter.
- **Design picker** (offline): it opened on the cutter with the world's design ("Door Window, 1.6 m cut"); a pick queued it (orders 2).
- `--bill` on a blueprint of one cut panel: its formed blank under Formed panels, the cut panel under Cut panels (Laser Cutter, 6.7 s, offcut 0.5 steel).
- Build passes; `--gen-parts all` leaves no change; `--check-construct-parts` has no REFUSED line but the scratch files' own while they were
  being written. Sections by name: Interaction sweep, Wheel orders, Display screens, Panel pointer at speed, Press stock, Extruder, Extruder
  network: 0 failures.

## Not done, not verified

- The design's drawn plan of the sheet (cuts dragged on a flat plan with 5 cm snap, the tools as drag handles): the cuts are slider rows instead.
- Cut plates in the designer (their blank is an extrusion, not a former's panel): ordered through bills, the window tool and a terminal by
  key only. A cut plate design can't be saved from the designer.
- Not tried: a Router line (the finding's fix); a cut plate's chain to an Extruder (the code path is the press blank's); a Building Station
  build with a cut part; the picker and the terminal's rows on a joined client; a Line Console's Send of cut lines; a slider dragged with
  cuts on by mouse (only the window tool's verbs); the terminal's Make list drawn (only its code path by the order).
- The Production Showcase's cutter.

## Questions for the user (asked together; the first option is the recommendation)

1. **How the designer places cuts.**
   - A (recommended): keep the slider rows (shipped now) and add the flat plan with drag handles as its own phase, beside them.
   - B: replace the slider rows with the flat plan only.
2. **What the cutter's Design button does.**
   - A (recommended): a pick orders that design at the cutter at its Order count, as built (no setting to keep in step with the world's list).
   - B: a pick sets a Design setting that Queue then orders.
3. **Cut plates in the designer.**
   - A (recommended): a "Blank: formed panel / extruded plate" choice in the designer, the plate's length, width and thickness from the sliders.
   - B: leave cut plates to bills and blueprints.
