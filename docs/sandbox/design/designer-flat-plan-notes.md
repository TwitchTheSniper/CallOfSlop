# Panel Designer: the flat plan and the Blank choice (notes, 2026-11-14)

The user's two answers after Laser Cutter phase 4 (`laser-cutter.md`, "Answers, 2026-11-13 (after phase 4)"): "Flat plan next", and
"Blank: formed panel / extruded plate". No wire change (protocol stays 309): a cut plate's key and its order existed since phase 1 and 4.

## What was built

- **The flat plan** (new `Source/UI/UiPanelDesignerPlan.cs`), under the turning preview in the designer's middle column: the sheet seen from
  above, L along (back edge left, nose right) by W across (its left edge at the bottom), a 5 cm grid (10 or 25 cm when the sheet is too big
  to show 5), the panel's facet lines faint, the cuts drawn as holes (a notch past an edge reads as one).
  - **Tools**: Rectangle, Round (eight sides, 0.29 of the side), Slot (10 cm wide), Corner (the first corner not yet cut), Mirror (a twin of
    the selected cut, else the last, across the centre line, glazed as it), Delete (also the Delete or Backspace key over the plan), and the
    selected opening's Glazed box. A new cut lands in the sheet's middle on the 5 cm grid, selected.
  - **Handles**: press a cut to select it and drag it to move its middle; an opening's four side handles size it (the opposite side moves with
    it: a cut is its middle and its size); a corner's two tips size its two legs. 5 cm snap, 1 cm with Shift. Every value is whole cm inside
    the ranges `CutRefusal` takes (opening sides 5 cm to the sheet's side, its middle on the sheet, corners 5 cm to the side less 5, an
    opening's corner cut-back kept within half its smaller side), so the plan never makes a value refused for its own range.
  - **Red with the words at the handle**: a refused key marks the cuts its words are about (`BadCuts`: a cut is to blame when the key passes
    without it; else each refused alone; else all) and draws the refusal beside the first red one (the selected one when red). The twelve
    key reads run only when the mouse is up; while a drag or slider is held the dragged cut (or all) is marked, and the release reads them.
  - **The sliders agree both ways**: the plan writes `_pdCuts` before the right column is drawn, so a row shows a drag in the same frame; a
    row moved shows on the plan next frame. Clicking a row's label selects that cut on the plan; red rows are red as on the plan.
- **The Blank choice** (`UiPanelDesigner.cs`): "Blank: formed panel / extruded plate" at the top of the sliders. A plate has Length and Width
  (20 to 600 cm, 5 cm steps) and its Section height (10 to 160 cm on the slider, a quarter of it thick: the Extruder's Plate section); its
  key is `Cut Plate (plate WxHxL MAT | cuts)` from `Extrusion.Name`. The plan, cuts, Glazed, Save, the world's list and ORDER are the cut
  panel's. With no cuts a plate is not a design ("an Extruder's piece"): ORDER is off and Save says so. Its readouts: the plate's blank and
  thickness, mass as cut, the linked Extruders and Laser Cutters. A preset tile switches back to a formed panel. A saved cut-plate design
  loads back as a plate; a flat bar's design (a bill's or a terminal's) is not drawn ("ordered by its key").
- **The preview of a cut plate** (`HullLoft.PanelPreview.Start`): it no longer drops a plate's plan, so the preview builds the plate through
  the same `PanelJson`/`PlateJson` as the real part (hash equal); only a bar on edge (which the designer never makes) is still shown uncut.
  The plate's key and its sheet still come from the same frame (`PreviewCut` hashes the plate's height and material as well).
- Smaller: the Glazed boxes' reasons are made with the readouts, no longer per frame (`_pdNoPane`); refusal words are capitalised sentences.
- **Window tool**: `design plandrag U1,V1 U2,V2` and `design planclick U,V` (real mouse through ImGui, at sheet cm), `design plan` (the cuts,
  selected, red, why, readouts, key, preview hash), `design blank 0|1`, `design plateh N`, `design select N`. `Ui.ToolMouse`
  (`UiPlatformFrame.cs`): once the tool clicks or drags, the real cursor outside the window is no longer fed over its moves (it undid every
  scripted drag; probably also what phase 6 saw as "the screen is scaled").

Shared files touched: `UiPanelDesigner.cs`, `HullLoft.PanelPreview.cs` (3 lines), `UiPlatformFrame.cs` (2 lines), `Program.WindowShot.cs`.
None of the cutter, logistics, crafting, lifter or wire files.

## Tried by hand (this PC, host, Production Showcase on Proving Ground)

- Drags by the real mouse: an opening moved 75,50 to 50,60; its nose side dragged to 90 (long 40 to 80); a corner's along tip 30 to 50 and
  across tip 20 to 40; an opening dragged onto the corner turned both red with "Two cut-outs overlap." at its handle and the preview fell
  back to the blank; dragged back, both clear and the preview, readouts and cutter's answer followed. A side dragged past the nose made a
  notch (passes). Picture checked by eye (the plan, handles, red words, the rows showing the dragged values).
- Sliders: Width 40 with cuts on turned the opening red ("An opening's middle lies on the sheet."), back to 100 cleared it; Crown 30 then a
  preset and a 40-frame Length drag with cuts on: no error. Blank switched to plate and back with cuts on, plate height 40: no error. Esc closed it.
- **Cut panel ordered**: 100 x 50 cm curve, a 50 x 20 opening and a cut corner by drags, ORDER: queued at the Laser Cutter, its chain asked
  the Panel Former; the showcase's former had no sheet, so its blank was fed by `cutter feed`: cut (7.1 s by the law, offcut +2 tenths).
- **Cut plate ordered**: plate 50 x 10 x 100 cm (2.5 cm), an opening with 5 cm corners dragged to 40,30, saved ("Plan Plate" in the world's
  list) and ORDERed: queued at the cutter as `Cut Plate (plate 50x10x100 steel | r40,30,20,20,5)`, its chain asked the Extruder; the blank
  fed by `cutter feed` was cut and went to the cutter's Piece Rack ("no route from the Piece Rack: collect it there"). Preview hash 6f47a6c5.
- Build passes.

## Not done, not verified

- **A joined client**: not seen. In every two-window run here the client's player stayed at its spawn (-166, 0, -154), 280 m from the
  showcase, whatever the host's `bring` or the client's `near` did, so the designer closed on the distance check and `design await` never
  opened it. (The first runs also joined another session's host on 27961; 27993 was free.) The client side of this change is UI only and
  sends the same `CraftNamed` and `PanelDesign` as phases 4 and 6, which were tried on a client then; a cut plate's key goes by the same path.
- The panel's blank coming from the showcase's Panel Former (it had no sheet: a showcase matter) and the plate's from its Extruder by chain:
  both blanks were fed by hand. The chain code is phase 4's, unchanged.
- The cut part reaching the designer's own tray (the showcase's cutter outputs to its rack).
- Delete and Backspace keys over the plan, Shift's 1 cm snap, the Round and Slot buttons by mouse (by code reading only; the tool added cuts by key).
- A loft's tapering nose on the plan (the plan draws the L by W frame the cuts are placed in; the taper is the preview's).

## Questions (the first option is the recommendation)

1. **The preview while a cut is red.** It shows the uncut blank (phase 4's rule).
   - A (recommended): keep the last good cut panel shown until the cuts pass again, so a drag through a refused spot doesn't flash.
   - B: keep it as built.
2. **A plate's widths.** The plate's Width and Length go in 5 cm steps (the Extruder's lengths do; its widths could be any cm).
   - A (recommended): keep 5 cm steps for both, as the plan's snap.
   - B: whole cm widths.
3. **Flat bars in the designer.** Only the Extruder's Plate section is a Blank; a flat bar (solid) is cut through bills and terminals by key.
   - A (recommended): leave it so.
   - B: a third Blank, "flat bar", either way up.

## Sections that cover it (for the merge round)

Interaction sweep, Display screens, Panel pointer at speed, Construct glass.
