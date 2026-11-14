# Laser Cutter, phase 6: the Pane (notes, 2026-11-13)

Phase 6 of `laser-cutter.md` (section 4.5, the user's answer 9): glass cut to an opening's outline, ordered with its panel from the Panel
Designer, welded into the opening by its socket. Also the phase 4 leftovers, tried now. Protocol 305 (main was 304; the merger renumbers).
The user-facing summary is `docs/sandbox/constructs/production.md` "The Laser Cutter", the Pane bullet.

## What was built

- **The part** (new `Source/Constructs/HullLoft.Pane.cs`): `Cut Pane (rA,B,C tT glass)`. `A` cm along (the panel's z), `B` across (x),
  corners cut back `C`, `T` mm (12 to 40, a formed panel's), material always glass. `A`, `B` 10 to 600 (an opening under 10 cm has no
  socket), `C` at most half the smaller side; a key past a range or spelt another way is refused, never clamped. The outline is the
  opening's less 7 mm all round (was 2: see the fixes below). Its back face is on y = 0 and its one socket `back` is there, so welded to `o0` its back face lies on the
  opening's middle plane and its front `T` mm out on the side the sheet faces. A rectangle (`C` 0) is one solid glass box, as the shipped
  Glass Panes: rounds pass it and break it. With cut corners it is an eight-cornered glass hull (detail, drawn in the blended pass by
  `ConstructRender.DrawGlass`) over a hidden box of the outline's bounds; a round stops on that box and breaks it (see question 2). Mass and
  health from the outline's area by the glass row of `Extrusion.json`, armour 0, `HasGlass` (it shatters).
- **Every reader of a cut key**: `IsCutName` takes the pane's prefix, so orders, bills, racks, terminals, the station and the wire carry
  it with no new path. The ones that cut a key apart got a pane branch: `CutRefusal`, `CutJob` (blank `PaneBlank`, edge its perimeter,
  thickness `T`), `MakeCut` (made at once, never sliced), `CutMaterial`, `CutLabel` ("Pane 40 x 30 cm (corners 5), 20 mm glass"),
  `ReadCut` (refuses a pane, which has no plan), `FromWire` (never a stand-in: a pane is one or two boxes). `--form-panel "Cut Pane (...)"`
  prints its edge, blank and offcut.
- **Its blank**: the smallest flat formed glass sheet that holds it, `Formed Panel (curve LxW c0 r0 tT glass)`, sides rounded up to 5 cm
  and at least 20. The chain asks it of a linked Panel Former as any cut panel's blank. The rest comes back as glass offcut (often 0: a pane
  near its sheet's size, and both masses are clamped at their minimums).
- **A fix found on the way** (`HullLoft.Panels.cs`, `PanelNumbers`): no glass sheet could be formed at all, since armour was
  `Math.Clamp(x, 1, MaxArmour)` and glass's MaxArmour is 0 (the clamp threw). Glass panels now have armour 0. Lesson in `.claude/rules/constructs.md`.
- **The designer's Glazed box** (`UiPanelDesigner.cs`): a check box under each opening's sliders, off for an opening under 10 cm. ORDER
  orders the panel and, per panel, one pane per glazed opening (equal panes in one order). The readout under the cuts says what comes with
  each panel and, for a glazed opening the sheet curves over by more than 2 cm, "its pane sits flat". Glazing is not saved with a design (a
  design is its cut key) and resets when a design is loaded. Window tool: `design cut glaze N`.
- **Window tool**: `ray PART U,V M D` (host) deals D damage to the part the ray hits and lists the body's parts after; `logi` on a joined
  client also finds the body by its Crafting Terminal.

Shared files touched: `UiPanelDesigner.cs`, `HullLoft.Cuts.cs`, `HullLoft.Panels.cs` (one line), `HullLoft.FirstParts.cs` (one condition),
`HullLoft.PanelPreview.cs`, `Program.WindowShot.cs` (the cut verb), `Program.WindowShotCuts.cs`, `Program.WindowShotLogi.cs` (one line),
`ServerBrowser.cs`. None of the logistics, crafting, station, lifter or showcase files.

## Tried by hand (this PC, Proving Ground, scratch blueprints in `bin` only, removed)

- `--form-panel` on `Cut Pane (r40,50,5 t12 glass)`, `(r40,50,0 t20 ...)`, `(r20,20,10 ...)` (a diamond), `(r200,90,20 t30 ...)`:
  clean, made twice equal; `... steel` refused "a pane is cut from glass"; `Formed Panel (curve 40x50 c0 r0 t12 glass)` now forms.
- **A blueprint step welding panes**: a flat cut panel with two openings, `Cut Pane (r40,50,5 t30 glass)` on `o0` and `(r40,30,0 ...)` on
  `o1`, both by `back`: built, 0 coplanar faces in `--check-vehicle`; in a window both draw see-through with their cut corners. A ray at each
  opening hits its pane (the rectangle as glass); damage breaks each pane alone (the panel stays), and the ray then passes the opening.
- **Host**: a Panel Former, a cutter and the designer by conveyors. A 100 x 60 cm panel with a glazed 40 x 30 window ORDERed once: the cutter
  queued the cut panel and the pane, the former formed the panel's blank by chain, the glass blank fed by hand (`cutter feed`: one former
  holds one material), both cut (5.8 s each by the law) and both reached the designer's tray.
- **Joined client** (`--net host:27961` / `join`): the client opened the designer, drew the same glazed window and ORDERed; the host got
  both orders (`CraftNamed` with the pane's key), cut both, and its tray held the panel and the pane. The client's cutter read the same two orders.
- **Phase 4 leftovers**:
  - A Line Console's **Send** of a bill with cut lines (`{"step": n, "cut": ...}` for a panel and a pane, and the panel's blank line):
    the cutter got both orders, the former the blank, both cut into the tray. A bill written by hand without the blank line leaves the cut
    order starving (no chain is rooted by a console order); a bill built from a blueprint's steps adds blanks itself (`AddBlank`), as
    `--bill` on the scratch blueprint showed: the panes under Cut panels, their glass blanks under Formed panels.
  - A **Building Station** build of a cut panel with its pane: the station ordered the cut panel at the cutter (the pane waits until its
    panel is fitted), the chain asked the former for the blank; with `station stock` it fitted the panel, then the pane on `o0`: Done,
    both parts by key, hashes as made.
  - The **picker and the terminal's rows on a joined client**: the terminal listed the world's cut design ("make 2 ... its blank from the
    Panel Former"), the cutter's Design picker opened with it, and a pick queued it at the host's cutter.
  - A **slider dragged by mouse** with a cut on: the Length slider moved through several keys, the preview following, no error. My drag
    coordinates (the screen is scaled) reached only the top of the slider, so the cut turning red and back by mouse is still not seen.
- Build passes; `--check-construct-parts` 0 REFUSED; `--gen-parts all --check` no change. Sections by name: Interaction sweep, Display
  screens, Panel pointer at speed, Press stock, Extruder network, Construct configure, Sandbox inventory, Construct glass, Wheel orders,
  Assembling network: 0 failures.

## Not done, not verified

- The design's flat plan with drag handles and saving a cut plate from the designer (questions with the user, as briefed); the Showcase's cutter.
- Two formers (steel and glass) into one cutter through a Conveyor Join: in my layout the glass blank stopped on the conveyor before the
  Join. Not looked into (logistics code); one former with the glass blank fed by hand was used instead.
- A cut panel ORDERed with **no glass in any linked former**: the pane order waits for its blank (the former refuses it or waits for glass). Not read on screen.
- The station left its cut-panel order open after `station stock` filled the hold by hand (orders 1 at Done): the station's own rule, not changed.
- A pane on a crowned or bent panel's opening (the "sits flat" note is read from the key, not seen in a window); a pane in a cut plate's
  opening; the Recycler's return for a pane.
- A Building Station whose output conveyor reaches its hold from the cutter (my layout sent cut parts to the designer).

## Questions for the user (asked together; the first option is the recommendation)

1. **Glazing in a saved design.**
   - A (recommended): leave it at ORDER, as built: a design stays its cut key, and the Glazed boxes are set each time.
   - B: save the glazed openings with the design (a second field in the world's design list and the library file: a wire and save change).
2. **A pane with cut corners and bullets.** It is drawn as a glass hull over a hidden box, and the hit code treats only a solid glass box as
   glass, so a round stops on it (and breaks it) instead of passing through as through a square pane.
   - A (recommended): let a hidden box count as glass when its part's material is glass (one condition in `ConstructWorld.Raycast`; the six
     shipped parts with both glass and hidden boxes are not glass parts, so they are unchanged).
   - B: keep it: one shot breaks the pane either way.
3. **Where the pane sits.** Welded by its back to `o0`, it fills the front half of the opening (and stands proud by `T` less half the
   sheet's thickness).
   - A (recommended): keep it (one socket, the design's rule).
   - B: centre it in the sheet's thickness (the pane's socket moved to its middle plane, which the build rules allow only on a face: it
     would need a pad).

## Fixes after review (2026-11-13)

1. **Glazed only where a pane fits** (`HullLoft.PaneWhy`, `PaneFor` takes the sheet): an opening of 10 cm or more lying wholly inside the
   sheet. One through an edge (whose socket may be gone, and whose pane would overhang) has its box off, the reason in its tooltip, and
   "no pane" in red if it was glazed before a slider moved it; ORDER never orders a pane without its socket. Tried: `design cut glaze 2` on
   an opening through the nose edge answered "this opening runs through the sheet's edge ..." and its pane was left out.
2. **A pane clear of its rim on any sheet**: an opening's `o` socket is now on the whole centimetre (a part's place is), so a pane lands
   exactly on it, and the pane is 7 mm short of the outline (`PaneGap`, was 2): the socket is at most 5 mm from the opening's true middle,
   leaving 2 mm. Tried: on a 105 cm sheet the socket and the pane are both at x -0.010 (the true middle -0.005; before, the socket was at
   -0.005 and the pane landed at 0.000, 5 mm off with a 2 mm gap); on a crowned sheet both at y 0.270. `--check-vehicle` on each: 0 coplanar
   faces, 0 slivers. This moves the `o` sockets of existing cut keys by up to 5 mm (same on every peer).
3. **Panes always match the panels**: with glazed openings, the Order count is capped at 999 over the most equal panes a panel takes
   (two equal glazed openings: 499 panels), and the readout says so. Not seen on screen (the readout was scrolled out of the picture).
