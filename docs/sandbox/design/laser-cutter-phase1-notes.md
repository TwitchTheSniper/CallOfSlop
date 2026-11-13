# Laser Cutter, phase 1: notes (2026-11-13)

Phase 1 of `laser-cutter.md` as revised for the user's answers 3 and 9: cut panels and cut plates exist as parts
(blueprints, creative, saves, the wire). There is no machine yet (phase 3). Protocol 283.

## What was built

- `Source/Constructs/HullLoft.Cuts.cs` (new): the two keys, `Cut Panel (PANEL | CUTS)` and `Cut Plate (BLANK | CUTS)`;
  reading and writing them in their one order; the rules (`CutRefusal`); the clipper; a cut plate's part file.
- `HullLoft.Panels.cs`: `PanelJson` takes a `CutPlan` and draws each touched facet's pieces (whole facets and pieces
  are one run of items, so slices are ranges of it); `FormedMaterial` knows cut keys; `--form-panel` takes a cut key and
  prints its cuts, pieces, key length, blank and offcut, and checks the 96-piece limit.
- `HullLoft.cs`, `ConstructParts.cs`: cut keys come through `HullLoft.Parse` (the one door), `ConstructParts.Find` and
  `HullLoft.Read` on the wire. `ConstructParts.MaxRuntimeName` is 240 (was 128).
- `ServerBrowser.ProtocolVersion` 283.

## Choices made

1. **Two prefixes, one family.** `Cut Plate (` carries an Extruder's blank text (`Pressed.BlankText`'s words), `Cut Panel (`
   a formed panel's. Both go through the same parse, rules, clipper and job queue.
2. **What a cut plate is.** The flat case of the panel canvas: a one-facet flat sheet, the extrusion's length and width,
   its middle plane at y = 0, its thickness the plate's (H / 4) or the flat bar's (H). Collision is the extrusion's exact
   box and its sockets are `Extrusion.Sockets` at the same places, so a blueprint that welded the plate welds the cut plate.
   Mass, health and armour follow the extrusion's rules from the area left.
3. **Which stock.** Plate up to H 40 (10 cm thick), a solid bar lying flat (H at most 10 and at most W), 20 to 600 cm a
   side; everything else refused with its own words (an L-angle, a pipe, too thick, too long, still growing).
4. **No `CutSet` in `PanelShape`.** A key is read once into a `CutPlan` class; `PanelShape` and every formed panel's
   code path are unchanged (an uncut panel is made exactly as before). The design's 4.2 now says so.
5. **The clipper.** Cut outlines (doubles, cm) are snapped to facet lines within 2 cm, clipped per facet, cut into bands
   at their corners, and a band's free spans that carry on straight into the next band are one piece. Pieces map
   bilinearly onto the facet's quad and offsets.
6. **Connected sheet without a flood fill.** With 3 cm webs between cuts and from edges they don't reach, the sheet can
   only part if one cut reaches two opposite edges: that is the rule.
7. **A Panel Former never takes a cut key.** `HullLoft.IsFormedName` is unchanged, so formers, chains and the terminal
   ignore cut keys until phases 3 and 5 add the cutter.

## Tried by hand (this PC)

- `--form-panel` on 15 good and refused keys: flat (3 cuts, 9 pieces), door skin with a window (16), hood (3 openings,
  68), bend (14), loft (52), a six-cut crowned panel (47), a 6 m roof bow with six cuts (78 of 96), a notch, a plate with
  a round port (8), a flat bar with a hatch and a cut corner (5). Every one: "a second make from the name, in slices ...
  the same part". First part 0.3 to 0.7 ms, the whole part 0.7 to 7.1 ms in slices. Refused as meant: L-angle, pipe,
  15 cm plate, 9 m plate, a cut reaching both sides, overlaps, two cuts on one corner, under a quarter left, within 3 cm
  of an edge, no cuts, a misordered key.
- Window tool, Proving Ground at 140,0,-100, a scratch blueprint (removed) of a cut plate with a cut panel and a cut door
  skin welded on top: it spawned; the cut corners, slot and window are drawn; `saveload` gave the same three names;
  `saveworld` then `loadworld` in a second run gave the same three names. Host and join (`--net host:27611`): the client
  found all three parts by name.
- `--check-construct-parts`, `--gen-parts all --check`: pass. Self-test `Press stock` by name: pass.

## Not verified

- The client's part hashes (it found the names; nothing prints its hash).
- Rays, bodies and players through openings: phase 2 by design (collision is still the blank's).
- A cut-away edge socket still exists (phase 2 drops it).
- Many new cut keys in one packet against the 8 ms tick (phase 2's measurements).
- A cut plate's sockets take the part's size class from the part file, not the extrusion's 0: welding a cut plate to
  what a plain plate welds to was not tried.
- Linux CI.

## Questions for the user (asked together; the first option is the recommendation)

1. **Offcut rounding.** Small cuts return 0 units today (a 40 by 50 cm window in a 20 mm steel door is under one unit).
   - A (recommended): round down, as now: a cut never makes material.
   - B: round to the nearest unit.
   - C: at least one unit for every cut.
2. **The thickest stock a Laser Cutter cuts.**
   - A (recommended): 10 cm (plate up to 40 cm high in the Extruder, a flat bar up to 10 cm).
   - B: 5 cm, so heavy armour stays whole.
   - C: 20 cm, with a slower stroke.
3. **A flat bar standing on edge** (taller than wide).
   - A (recommended): refused, "lay the bar flat".
   - B: cut through its width as well.
