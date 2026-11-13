# The Laser Cutter: cut-outs in panels (design)

Design only (2026-11-13; revised the same day for the user's answers 3 and 9, which section 13 keeps as the record).
Every number below is a proposal or an estimate unless it says where it is quoted from. Written for an agent to build in
six phases of 60 to 90 minutes.

The queue item is a name with no description. The only earlier words about it are two "Next" lines in
`docs/sandbox/constructs/production.md`: the Panel Former's ("the designer window, the Laser Cutter (cut-outs)") and the
Panel Designer's ("player-added sockets, cut-outs with the Laser Cutter"). This design takes those as the user's intent
and says what the machine should be.

Sources read: `production.md` (Extrusion, Run-time parts, The Press and its two sub-sections, The Panel Former, Machine
tiers, Panel Designer), `gathering-and-salvage.md` (Salvage, Salvage haul), `docs/sandbox/design/press-rework.md`,
`arms-and-cranes.md`, `panel-designer-3-notes.md`, and the code named in section 1 (`HullLoft.Panels.cs` read from
`MakePanel` to `PanelJson`; the rest by their headers and entry points).

## 1. What exists today, and the gap

| Thing | What it makes | What it cannot make | Files |
| --- | --- | --- | --- |
| Extruder | A section swept along a length, cut to length at the die (the Cut input, Cut every; exact in 5 cm steps). The Cut-off Saws were removed on 2026-11-04: the cut is the Extruder's | A hole across the piece, a mitred end | `ConstructExtrusion.cs`, `ConstructExtruder.cs` |
| Press, Plate die | An extruded plate blank (10 cm thick at least: `Extrusion.MinSectionCm`) with a rolled lip and ribs, treads or rivets added | Anything taken away: every feature stands proud or is let in | `Pressed.cs`, `PressDies.cs` |
| Panel Former | A named panel from its bin: seven presets, a fold (`bend`), a sheet ruled between two profiles (`loft`), 12 to 40 mm thick, 20 to 600 cm a side. Always the whole sheet: a rectangle, or the hood's taper | An opening, a notch, a cut corner. A panel is a grid of at most 8 by 6 facets with up to four hidden boxes as its collision | `HullLoft.Panels.cs`, `ConstructPanelFormer.cs` |
| Panel Designer | Draws those panels, saves designs, orders them | Has no way to say "a window here" | `UiPanelDesigner.cs`, `PanelDesigns.cs`, `HullLoft.PanelPreview.cs` |
| Taking things apart | The attach tool takes a part off (60% of its fitting time); a wrecked part is hauled to a Recycler, which gives its material by mass | Nothing cuts a part into two parts | `ConstructEdits*`, `ConstructRecycler.cs` |

So every machine adds shape and none removes it. A door skin has no window, an armour plate no firing port, a deck no
hatch, a wall no doorway, a gusset no lightening holes, and a panel's outline is the former's rectangle whatever stands
beside it. That is the gap: **taking material out of a sheet.**

## 2. Three things a cutter could be

| | A. Cut-outs in panels | B. A saw for stock | C. Cutting parts and wrecks apart |
| --- | --- | --- | --- |
| What it is | A machine that takes a formed panel (flat or shaped) or a flat extruded piece (plate, a flat bar) and cuts openings, notches and corners out of it, to a pattern drawn in the Panel Designer | A machine that cuts extruded stock to length or to a mitre | A machine, or a tool on an arm, that cuts a finished part or a wreck into pieces |
| New to the game | Yes: nothing removes material today | Length: no (the Extruder cuts exactly). Mitre: yes, but boxes do not turn, so a mitred end is a drawn wedge on a square collider | Yes, but it needs "half a part" to be a thing: every catalogue part would need a rule for what each half is, does and weighs |
| What the player gets | Windows, ports, hatches, doorways, arches, vents, lightening holes, shaped outlines: bodywork and bases that read as made, and openings that work (rays, bodies and players pass) | Picture-frame corners | A salvage scene. The loot loop already has its rule (what you take off whole is what survives; the rest goes to a Recycler by mass) |
| Fits the chain | Exactly: the Former makes the blank, the cutter is the next machine, the key carries both, as a pressed part's key carries its extrusion | It would bring back the saw the user took out | Outside the production chain: it is a gathering feature |
| Effort (estimate) | 5 phases | 2 phases, for a look | Large and open: 8 or more phases, and decisions about every part family |

**Recommendation: A.** It is what the two "Next" lines ask for, it is the only one of the three that gives players
something no machine and no tool gives now, and it rides on code that exists (a panel's key, its facets, its slices, its
designer, a named work order).

**Left out, on purpose:**

- **B.** A mitre, if wanted later, is an end setting on the Extruder ("End: square or mitre", a drawn wedge), not a machine.
- **C.** Salvage stays the attach tool and the Recycler. A cutting head on a Loader Arm is a possible later use of the
  cutter's look, not of its code.
- Pressed plates (a Press's output): their features stand proud of the blank, so a cut through them needs the die's own
  shapes. Later, if asked. Extruded flat stock is cut from phase 1 (section 4.4).
- Cut-outs in the vehicle kit's own lofted parts (`Hull Loft (...)`: cabs, bays): their keys are the kit's, their shapes
  are not a sheet. Later, if at all.
- Free polygons and true curves (section 4.2 says why; question 4).
- Engraving, text or marks on a panel: the paint tools' job.

### 2.1 How it differs from the Panel Former, so the two never do one job

| | Panel Former | Laser Cutter |
| --- | --- | --- |
| Does | Gives a sheet its **shape**: crown, rake, fold, loft, size, thickness | Takes material **out** of a formed sheet: openings, notches, cut corners |
| Stock | Bulk material in its bin (or extruded sheet counted into it) | One formed panel or one flat extruded piece at a time, on its bed; no bin of stock (answer 3) |
| Never | Removes anything; a panel leaves it whole | Bends, sizes or thickens anything; it cannot make a panel, only cut one |
| Output | `Formed Panel (...)` | `Cut Panel (...)`, `Cut Plate (...)` (from an extrusion), `Cut Pane (...)` (glass, section 4.5), and the offcut as bulk material |
| Costs | Material by mass, a short stroke | Power and time by the length of the cut; no material |

A flat sheet is the Former's (`curve` with crown and rake 0). The cutter refuses to "make" a panel with no cuts ("nothing
to cut: a Panel Former's order"), and the Former refuses a cut panel ("a Laser Cutter's order").

## 3. What the player does

1. **Build the line.** A Panel Former (or an Extruder making plate), a conveyor from its Piece out to the **Laser Cutter**'s Piece in, a conveyor from
   the cutter's Piece out to a Piece Rack; a chute from the cutter's **Offcut** port to a Materials Bin (or back to the
   Former's hopper); power; a Panel Designer or a Crafting Terminal linked as today (a Line Console's wire into Orders,
   or conveyors either way round).
2. **Draw it.** F on the Panel Designer. Pick a preset or a saved design and set its sliders as today. Press **Cut-outs**:
   the middle shows the sheet laid flat from above with a 5 cm grid beside the turning preview. Pick a tool (Rectangle,
   Round, Slot, Corner), drag it onto the sheet, drag its handles to size it; **Mirror** keeps a twin across the centre
   line; Delete removes the selected cut. The preview shows the cut panel as it will be made. The readouts say: cuts 3
   of 6, facets and collision boxes against their limits, each opening's clear size, the mass before and after, the
   offcut that comes back, the key, and each linked cutter: "Laser Cutter 1: yes" or why not.
3. **Save and order.** Name it, Save design, set a count, ORDER. The order goes to the first linked cutter that takes
   it; the cutter's order asks a linked Panel Former for its blank, or a linked Extruder when the blank is a plate (a
   chain, as a press's order asks an Extruder).
4. **Watch it run.** The Former forms the blank; it rides to the cutter's bed; the head tracks over it, glowing, for the
   cut's time; the cut panel rides out to the rack (or to the designer's tray when a conveyor returns it); the offcut
   lands in the cutter's scrap bin and leaves by chute.
5. **By hand, with no line.** Queue an order at the cutter (its screen: Queue orders its Design setting, a world design
   picked with the Recipe button), let a loose panel of the right kind go on its in bed, and take the cut panel off the
   out bed. F on the machine takes back what lies on it, in every state.
6. **Build with it.** A cut panel welds by its edge sockets as the panel it was cut from (a cut plate by the
   extrusion's face sockets), and each opening has a socket in its middle for a pane (section 4.5), a hatch, a lamp or a vent. A blueprint keeps it by its key; a Building Station's bill orders it.

## 4. The cut panel

### 4.1 Its key is its name

One family with formed panels: a named run-time part, made from its name the first time it is asked for
(`HullLoft.Parse`, the one door), its id each machine's own, never in `ConstructParts.All`, on the wire and in saves by
its key (CLAUDE.md, Gotchas, "Extrusions").

```
Cut Panel (PANEL | CUTS)
Cut Panel (door 110x120 c6 r8 t20 steel | r70,60,40,50,5)                 a door skin with a window
Cut Panel (curve 200x100 c0 r0 t30 steel | r100,50,10,40,3 m0,30,30 m1,30,30)   a flat plate, a firing slot, two cut corners
```

| Word | What | Rule |
| --- | --- | --- |
| `PANEL` | the blank: a formed panel's key less its prefix and brackets, exactly as a machine writes it | any kind (`bend` and `loft` too); `"Formed Panel (" + PANEL + ")"` must itself be a panel. So the key holds the blank a chain must ask for |
| `rU,V,A,B,C` | an **opening**: its middle `U` cm along the sheet from its back edge and `V` cm across from its left edge, `A` long by `B` wide, each corner cut back by `C` | whole cm; `A`, `B` 5 to 600; `C` 0 to half the smaller side. `C` 0 is a rectangle; `C` near 0.29 of the side is an eight-sided "round"; a long one is a slot. It may reach past an edge: that is a notch |
| `mK,A,B` | a **cut corner**: corner `K` (0 back left, 1 back right, 2 nose right, 3 nose left), `A` cm along and `B` cm across cut off as a triangle | whole cm, 5 up to the sheet's side less 5; at most one a corner |

- **The sheet's frame.** Cuts are placed on the flat sheet, `L` by `W`, before the former's shape: `(U, V)` maps to the
  panel's own `(s, x)` (`s = U / L`, `x = 2V / W - 1`), so a cut follows a crown, a fold or a loft, and narrows with a
  tapering panel. One frame for every kind; no cut has a kind-specific rule.
- **At most 6 cuts**, written in one order: the openings by `U`, then `V`, then `A`, `B`, `C`; then the cut corners by `K`. A key spelt another way, past
  a range or breaking a rule below is refused ("not a cut panel"), never clamped: every peer makes the same part or none.
- **Rules a key must pass** (`HullLoft.CutRefusal`, each with its own words for the designer): each cut lies at least
  partly on the sheet; cuts do not overlap; two cuts, or a cut and an edge it does not reach, keep 3 cm of sheet between
  them; no cut reaches two opposite edges ("the cut would part the sheet": with the webs above, that is the only way the
  sheet can come apart, so no flood fill is needed); at least a quarter of the sheet's area is left; the facets and boxes of 4.2 and 4.3
  stay within their limits.
- **Length.** The longest loft key is 102 characters and its material (quoted: `panel-designer-3-notes.md`); six cuts add
  up to 132 (estimate, from the widest numbers). `ConstructParts.MaxRuntimeName` is 128 today, and its comment says a
  name is "never a fixed-width field (a save's names are length-prefixed, a packet's text is at most 255 bytes)". Phase 1
  raises it to **240** and checks every reader; a key longer than that is refused ("too many cuts for this panel's key"),
  which only a loft with six three-digit cuts can reach. Risk 1 has the fallback.
- The key's hash is the name's, as a loft's; `StructureHash` mixes that. No world store, no hash-with-no-body case.

### 4.2 What is drawn

New file `Source/Constructs/HullLoft.Cuts.cs` (partial `HullLoft`: `CutPrefix`, `CutSet`, `CutOf`, `CutKey`,
`CutRefusal`, the clipper). `PanelShape` gains a `CutSet` (six packed `ulong`s, so the shape stays a value), and
`PanelJson` asks the clipper for each facet's pieces instead of emitting the facet whole.

- **The blank's facets are the canvas.** The panel's grid (`PanelGrid`, `PanelRowS`) is made exactly as today. Each cut is
  a convex polygon in whole centimetres (an opening has up to eight corners, a cut corner three). For each facet, its
  rectangle in the sheet's frame less the cuts that touch it is split into convex pieces of at most six corners (bands
  between the cuts' corners, within that facet only). A facet no cut touches is emitted as today, byte for byte.
- **A piece's corners lie on the facet's own flat quad** (bilinear in its four corners, and in their shared offsets), not
  on the curved surface. So the cut panel is the uncut panel's drawn surface with pieces removed: no cracks where a
  split facet meets a whole one, and the seams close as they do now.
- **Slivers.** `Builder.Facet` drops a piece thinner than the loader's centimetre. A cut edge within 2 cm of a facet's
  line is moved onto that line, in the drawing, the mass and the collision alike, so they agree and nothing vanishes.
- **Straight edges only.** Every cut edge is along the sheet, across it, or a diagonal. A "round" hole is eight-sided.
  That keeps the arithmetic in whole centimetres up to the last step (the same floats on every peer), keeps pieces
  convex (a facet is a hull shape), and keeps the key short. Free polygons would need a hash key with the design's body
  stored per world and sent before any body naming it: the route the loft avoided. Question 4.
- **Limit: 96 facets** a cut panel (a panel's is 48). Quoted from the code: "a part's first make is 0.1 ms a facet".
  Estimates: a flat sheet is one facet, so six scattered openings come to about 45 pieces; a door skin with one window
  about 25 more than the door; a fully curved 8 by 6 panel takes two or three large cuts before the limit. Over it, the
  designer says "too many facets (112 of 96): fewer cut-outs, or straighter ones".
- **Made in slices**, as a panel: `MakePanel` gives a new key its solid boxes, sockets, bounds and mass at once with a
  slab over each box, and `PumpPanels` makes the pieces 6 a slice inside its 2 ms and swaps them in
  (`RuntimeParts.Redrawn`). A tool makes it whole; both ways give the same part (`PanelHash`).

### 4.3 Collision, sockets, mass

- **Collision opens where the sheet is open** (question 5). Today a panel has up to four hidden boxes, one a strip of its
  length. A cut panel's strips are split, in the sheet's frame, into rectangles round the openings; each rectangle's box
  is the bounds of the surface over it, with the existing whole-centimetre inset (`In`) on every rim, so no hidden face
  shares a plane with a drawn one. An opening opens by the rectangle inside its cut corners, so nothing unseen blocks a
  ray and a little drawn metal at the corners lets one through. Openings under 10 cm a side are drawn and weighed only.
  **Limit: 24 solid boxes** (estimate: six openings that share no line come to about 19); over it, "too many collision
  boxes: line the cut-outs up, or use fewer".
- Rays, bodies and players then pass a big enough opening with no second path: they read the part's solid boxes
  (`Construct.SolidCount`, `SolidBox`). A doorway wants 0.8 by 1.9 m clear; the designer's readout gives each opening's
  clear size, which on a steep or sharply curved part of a panel is less than it looks (risk 3).
- **Sockets.** The blank's edge sockets, by the same names (`e0..`, `w0..`, `s0..`, `n0..`, `back`), so a blueprint that
  welded by `e2` still does; one whose place was cut away is left out, and a step naming it is refused by its name as any
  missing socket. Each opening of 10 cm or more adds **`o0`..`o5`** in its middle at the surface's height, facing the
  side the surface faces there (it stands in the opening's air, as a pipe's `base` stands in its bore). At most 31.
- **Mass and health** from what is left: the pieces' area by the thickness at the material's `unitsPerM3` and weight, and
  `healthPerM3`, as a panel. Armour is the thickness's, unchanged. The Recycler gives its material by its mass
  (`FormedMaterial`, `FormedUnits`).
- Still a welder part, never in the menus (`"stage"`); paint and health share carry over from the blank.

### 4.4 Cut extruded stock (answer 3)

The cutter also takes a piece straight off an Extruder, so plate never has to go through a Panel Former.

```
Cut Plate (BLANK | CUTS)
Cut Plate (plate 200x20x300 steel | r150,100,60,60,17)        a 5 cm steel plate with a round port
Cut Plate (solid 120x10x240 steel | r60,60,40,80,0 m2,20,20)  a 10 cm flat bar with a hatch and a cut corner
```

- **`BLANK`** is the piece as an Extruder names it less `Extrusion ` (`Pressed.BlankText`, as a pressed part's key carries
  its blank), spelt exactly as a machine writes it; never growing. `CUTS` are section 4.1's, in the same sheet frame.
- **Which sections.** Flat stock only, the laser cutting through its thickness:
  - **plate** (the Extruder's Plate preset: the middle two rows of the grid, so `H / 4` thick): `H` up to 40 (10 cm thick);
  - **solid** lying flat (a flat bar, the Panel Former's "sheet"): `H` up to 10 and no more than `W`.
  - Refused with a reason, never cut: an L-angle, T-bar, I-beam, C-channel, cross, wall or hollow section ("a Laser
    Cutter cuts flat stock: plate or a flat bar, not an L-angle"), a custom grid or an airfoil (the same words), a round
    or a pipe ("a pipe is not flat stock"), a wheel, fan, blade or wing (not stock at all), stock thicker than 10 cm
    ("too thick to cut: 12.5 cm, a Laser Cutter cuts 10 cm"). Cutting a hole across a section's web is a later idea, not
    this design.
- **Size.** 20 to 600 cm long and wide (the largest bed: section 5); a longer piece is cut to length at the Extruder
  first ("cut it to length at the Extruder: a Laser Cutter's bed is 6 m").
- **The frame.** The sheet is the piece's own: `U` along its length (its `z`, 0 at its `base` end), `V` across its width
  (its `x`, from `-W/2`), its middle plane at `y = 0` as the extrusion's. So a cut plate is the flat case of the panel's
  canvas: one facet, `L` by `W`, the extrusion's thickness, cut by the same clipper.
- **Collision.** A plate is flat, so an opening opens truly (risk 3 does not apply): its one solid box becomes rectangles
  round the openings, as a panel's strips (phase 2). Until then, the blank's box.
- **Sockets.** The extrusion's own (`base`, `tip`, `+x0`, `-y2` ...: `Extrusion.Sockets`), at the same places, so a
  blueprint that welded the plate welds the cut plate in its place; a socket whose spot is cut away is left out
  (phase 2), and each opening adds its `o` socket.
- **Mass, health, armour** by the extrusion's rules, from what is left: area left by thickness at the material's
  `unitsPerM3` and weight, `healthPerM3`, armour by the thickness. The offcut is the rest, in that material.
- **One family on the wire and in saves** with cut panels: a named run-time part, `HullLoft.Parse` the one door, its name
  after `HullLoft.WireKind`.

### 4.5 The Pane (answer 9)

A window wants glass that fits it. The cutter cuts a **Pane** to an opening's own outline from glass sheet.

```
Cut Pane (rA,B,C tT glass)            a pane A by B cm, corners cut back by C, T mm thick
Cut Pane (r40,50,5 t12 glass)         the pane for the door's window of section 4.1
```

- **The key is the opening's outline** (`A`, `B`, `C` as an opening's), its thickness in mm (12 to 40, the Former's) and
  `glass` (the only material a pane takes). It is flat, one convex piece of at most eight corners, whatever the panel it
  goes into: on a crowned panel it sits flat in the opening; the designer says so when the curve over the opening is
  more than 2 cm.
- **Its blank** is the smallest glass sheet that holds it: `Formed Panel (curve LxW c0 r0 tT glass)`, `L` and `W` the
  outline rounded up to 5 cm and at least 20 (the Former forms glass already: `Extrusion.json` lists it), or an extruded
  glass plate of that size. The rest comes back as glass offcut. The chain asks for that blank as for a cut panel's.
- **It welds** by one socket, `back`, in its middle on its back face, to an opening's `o` socket (the opening's middle at
  the surface). Its outline is the opening's less 2 mm all round, so its rim never shares a plane with the cut's. It is
  glass: it draws in the glass material, stops bodies, and breaks as glass does (`health` from its volume; armour 0, as
  `Extrusion.json`'s glass row).
- **The designer** gets a check box beside each opening, **Glazed**: ORDER then orders the panel and one pane per glazed
  opening, and a blueprint step welds each to its `o` socket. A pane is its own part, so a bill lists it and a broken
  pane is replaced alone.

## 5. The machine

Part data `storage.cut` (`speed`, `maxLength`, `maxWidth`, `maxHeight`, `at` the out bed's middle, `scrap` the offcut
bin's size); `IsLaserCutter`; new `Source/Constructs/ConstructLaserCutter.cs`, shaped as `ConstructPanelFormer.cs`
(`Refusal`, `Order`, `Work`, `TakeFromConveyor`, `Lacks`, `Parse`). A line machine: Run wire, power, status lamp, line
state and console list as the Extruder family's.

| Machine | Largest panel (bounds: longest x next x height) | Speed | Draw while cutting | Offcut bin | Size (estimate) |
| --- | --- | --- | --- | --- | --- |
| Laser Cutter | 2.5 x 1.8 x 0.6 m (the Panel Former's) | x1 | 2.4 kW | 200 | 3.5 x 2.5 m |
| Medium Laser Cutter | 6 x 4.5 x 2 m (the Medium Panel Former's) | x2 | 9.6 kW | 1,200 | 7.5 x 5.5 m |

- **Sizes follow the Panel Former, not the Press.** Its blanks are a former's, and no panel passes 6 m; a Press's four
  classes would give two machines nothing can feed. A Large cutter comes with a Large former (on the Former's own
  "Next" list). It has no tiers: the two sizes are its ladder, as the former's (question 7).
- **The bed is a store, not a bay.** The blank waits in a one-kind slot (`storage.pieces`, 4 panels of the head order's
  blank: estimate), as a panel waits in the Panel Designer's tray, and is drawn lying on the bed, look only
  (`RouteRender`, as the plinth). Not a part of the construct as a press's piece is: nothing has to fit, nothing is solid,
  and the wire and saves are a store's own. A panel taller than `maxHeight` (a 90 degree fold with a long leg) is
  refused: "too tall for the bed: 0.9 m, the Laser Cutter takes 0.6 m".
- **Blanks.** The head order's blank: a formed panel (a cut panel's), an extruded plate or flat bar (a cut plate's), or a
  glass sheet (a pane's, formed or extruded). The bed's size is the same limit for each.
- **Fed** by conveyor into **Piece in** (it takes only the head order's blank; anything else waits on the conveyor, "not
  its order"), or by hand: a loose panel let go on the in bed is taken in (the mouth check a press's bay makes, 10 Hz,
  only while a slot is free).
- **Orders only** (question 8). `WorkOrder.Name` is the cut part's key (named shape 255, as a panel's). Refusals: "not a
  cut part", "nothing to cut: a Panel Former's order", "panel too large for this cutter: ...", "too tall for the bed",
  a full queue. An Extruder, a Press, an Assembler and a Panel Former each refuse a cut key: "a Laser Cutter's order".
  Waiting says what it lacks: "needs Formed Panel (door 110x120 c6 r8 t20 steel)" or "needs Extrusion plate 200x20x300
  steel". A pane's stroke is its outline's length.
- **The cut is a timed stroke**: `2 s + 3 s a metre of cut edge x (thickness / 20 mm)`, over the machine's speed
  (proposal). A door's window (1.8 m of edge, 20 mm) is 7.4 s on the small cutter. Power is drawn only while it cuts;
  no power, Run held off or a full offcut bin stops it with the blank untouched (the blank goes when the stroke ends,
  as a press's length).
- **The offcut** (question 6): the area cut away, by the thickness, in units of the panel's material, rounded down, into
  the offcut bin (`any`, bulk), which a chute empties from the **Offcut** port. A bin holding another material, or
  full, stalls it: "offcut bin full" (`ExtrudeStall`, one new value). So a cut costs power and time, and no material.
- **Out**: the cut panel rides out of **Piece out** to a rack, waiting as a former's panel does (too narrow, full, cut),
  or with nothing linked drops on the out bed as a loose part.
- **Look** ([Machine look](../constructs/look.md#machine-look) already names a Laser Cutter among the machines with a
  screen): a flat slatted bed, a gantry across it carrying a small bare head with a glowing lens (`"glow"`, `"work"`:
  stroke along the gantry, glow while cutting), a fume hood, the offcut hopper under the bed's end with a hazard edge,
  the control board on its left (screen, lamp, Queue, Cancel, Clear, Design, Up, Down). Its own body colour, apart from
  the former's `#5d7f3c` and the designer's `#3f6f9a` (check with `--look-report`). Parts from a new part kit generator,
  `--gen-parts laser_cutter_parts`; recipes from the Assembling generator; Production category, beside the formers.
- **Performance.** `ConstructLaserCutter.Step` at 10 Hz looks only at constructs with a cutter (`HasCutter`), and the
  mouth is a `Near` query only while a slot is free and an order waits. Nothing allocates but the part it makes.

## 6. The designer

No new desk: the Panel Designer designs the whole part, and one design is one key.

- **`UiPanelDesigner.cs`** gains the **Cut-outs** mode of section 3: a flat plan of the sheet (ImGui draw list: the
  outline, the facet lines faint, cuts as handles), the four tools, Mirror, Delete, 5 cm snap (1 cm with Shift), and the
  readouts. Changing a slider keeps the cuts where they fit and marks the ones that no longer do in red with the
  refusal's words; ORDER and Save are off until none is red.
- **The preview is unchanged code**: `HullLoft.PanelPreview` makes the part from the key through `PanelJson`, so the cut
  shows as it will be made, sliced, with no key left behind.
- **Designs** (`PanelDesigns.cs`): a design's key may be a cut panel's (`Refusal` accepts both prefixes). The library
  file, the world's list, `CActKind.PanelDesign`, `CBodyKind.Designs` and the `DSGN` section carry it as text already;
  the lengths they read follow `MaxRuntimeName`.
- **ORDER** is `CActKind.CraftNamed` with the cut key: the first linked cutter that accepts it queues it. With no cutter
  linked: "no Laser Cutter linked: this design has cut-outs".

## 7. Orders, bills, the terminal, the network

| Where | What changes | Code |
| --- | --- | --- |
| Work orders | A cut key is a named order; the refusal texts above | `WorkOrders.cs`, `ConstructLaserCutter.Refusal` |
| Chains | `CutterInputs`: an order queued at a cutter asks for its blank, `Formed Panel (PANEL)` at a linked Panel Former that takes it (its size, its bin's material), or `Extrusion BLANK` at a linked Extruder (as `PressInputs` asks), tagged, riding to the cutter's Piece in and counted on arrival; beside `PressInputs` and `FormerInputs` | `ConstructLogisticsPlan.cs` |
| Routing | The cutter is a destination with a want (its head order's blank) and a source (Piece out), as a press | `ConstructLogistics.cs` |
| Crafting Terminal | Cut panels in linked stores listed "in stock"; on Make, per linked cutter, the world's cut designs: `now` when the blank is in linked stock or on the bed, `through a chain` when a linked former can form it | `ConstructCrafting.cs` (a `CutterMissing` beside `PressMissing`), `UiCrafting.cs` |
| Bills | `Bill.Cut` (keys and counts, panes too); each adds its blank to `Bill.Panels` or its piece to the Extruder's lines; `ToConsole` and a console's Send deal them to its cutters. Blueprint bill line: `{"step": n, "cut": "Cut Panel (...)", "count": 2}`. `--bill` prints "Cut panels": the cutter that takes each, its blank, seconds, the offcut returned | `WorkOrders.cs` (`ConstructBill`), `VehicleKit/VehicleCore.cs` (`BlueprintScript.BillPanel`'s twin) |
| Building Station | `ConstructStation.OrderFor` gives a cut key its cutter order | `ConstructStation.cs` |
| Recipe picker | The cutter's Design button opens it on the world's cut designs | `UiRecipePicker*.cs` |
| Wire | No new message: a cut panel, plate or pane in a Full, a rack's kinds or a conveyor's riders goes as a formed panel does (`Extrusion.WireMark`, `HullLoft.WireKind`, its name); a named order as today. Protocol bumps: phase 1 (a name older builds refuse, the longer name), phase 3 (new parts, the stall value) | `ServerBrowser.ProtocolVersion` (277 now; the merger renumbers) |
| Saves | By name everywhere (bodies, stores, orders, designs). No new save file | none |

## 8. Limits that protect the tick and the wire

| Limit | Value | Why |
| --- | --- | --- |
| Cuts a panel | 6 | the key's length and the box count |
| Facets | 96 | quoted 0.1 ms a facet; made 6 a slice inside `PumpPanels`'s 2 ms, so a new key is whole in about 16 updates (estimate) and never in one |
| Solid boxes | 24 | a body's collider and every ray read them; a panel has 4 today |
| Sockets | 31 | 25 a panel, 6 openings |
| Key | 240 characters | a packet's text is at most 255 bytes |
| A new key's first part | about 0.3 ms (quoted for a panel; to be measured for 24 boxes) | made at once: boxes, sockets, mass |
| Run-time slots | one of `Extrusion.MaxDefs` a key, up to 96 of `PartShapes.Cap` | freed by `RuntimeParts` as any named part; the preview leaves none |
| The machine | a 10 Hz step, constructs with a cutter only, 0 bytes | as the former: its step's median was 0.0003 ms (quoted) |

**To measure and report in phase 2** (a throwaway probe, removed): the first make of the worst key (six cuts on the
largest loft), whole and deferred; twenty new cut keys in one packet, the worst server and client update (the panel's
figure was 8.2 ms deferred: a cut panel's first part has more boxes); bytes of a Full holding 40 cut panels.

## 9. What it reuses, by file

| File | What is reused | What is added |
| --- | --- | --- |
| `Source/Constructs/HullLoft.Panels.cs` | `PanelOf`, `PanelKey`, `PanelGrid`, `PanelPoint`, `PanelNormal`, `MakePanel`, `PumpPanels`, `PanelJson`, `PanelCli`, `PanelHash` | the cut prefix at the door, `PanelJson`'s call to the clipper, strips split round openings, `--form-panel` printing cuts, boxes, clear openings and the offcut |
| `Source/Constructs/HullLoft.cs` | `Parse`, `Adopt`, `Builder.Facet`, `Hide`, `Sock`, `WireKind` | `IsFormedName` knows the prefix |
| `Source/Constructs/HullLoft.PanelPreview.cs` | all of it | nothing |
| `Source/Constructs/ConstructPanelFormer.cs` | the shape of the machine: `Refusal`, `Order`, `Work`, `Idle`, `TakeFromConveyor`, `Lacks`, `Sides`, `Fits`, `TierFor` | its refusal of a cut key |
| `Source/Constructs/ConstructPress.cs` | the mouth check for a loose piece, "stock always comes out", the head order ruling what is taken (`BayRefuse.Order`) | its refusal of a cut key |
| `Source/Constructs/PanelDesigns.cs`, `Source/UI/UiPanelDesigner.cs`, `Source/App/Program.SandboxPanelDesigner.cs` | designs, the window, the preview's viewport, sliders, ORDER | the Cut-outs mode |
| `Source/Constructs/WorkOrders.cs`, `ConstructCrafting.cs`, `ConstructLogisticsPlan.cs`, `ConstructStation.cs` | named orders, `CraftNamed`, chains, `OrderFor` | section 7's rows |
| `Source/Constructs/RuntimeParts.cs` | the sweep (a cut panel is a `HullLoft` name: nothing to add) | nothing |
| `Source/PartKit/Generators/PanelFormerParts.cs` | the bed, bridge and control board builders | `LaserCutterParts.cs` beside it; `ProductionShowcase.cs` gets a cutter after its former |

## 10. Phases

Each: its own branch and worktree, WIP commits with `[skip ci]`, the final push without, a build that passes, a by-hand
run reported with what was not verified. No self-test sections are added. `production.md` gets "The Laser Cutter" (listed
in `CONSTRUCTS.md`) at the end of phases 3 and 5.

| # | Title | Work | Files | Stop point | Try by hand |
| --- | --- | --- | --- | --- | --- |
| 1 | **The cut panel and cut plate, drawn** | `HullLoft.Cuts.cs`: both keys (parse, write, the order of cuts; a cut plate's blank and its refused sections), the cut set, the rules, the clipper (pieces within each facet, on its quad; a plate is one flat facet), mass and health from what is left; collision and sockets still the blank's. `MaxRuntimeName` 240 and its readers. `--form-panel "Cut Panel (...)"` and `"Cut Plate (...)"`. Protocol bump | new `HullLoft.Cuts.cs`; `HullLoft.Panels.cs`, `HullLoft.cs`, `ConstructParts.cs`, `Net/ServerBrowser.cs` | `--form-panel` on eight sample keys (flat, door, hood, bend, loft, a plate, a flat bar, refused ones: an L-angle among them) prints clean, and each made in slices equals itself made whole. **Shippable alone**: cut panels exist for blueprints and creative | `--form-panel`; a scratch blueprint with two cut panels in `--window-shot`, and `--net host` with `join`: the same names and hashes; a creative save and load |
| 2 | **Openings that work** | Strips split round openings (a cut plate's one box too), the box limit, the inset on rims; `o` sockets, dropped edge sockets; the facet limit's and box limit's words; `--form-panel` prints clear openings; the measurements of section 8 | `HullLoft.Cuts.cs`, `HullLoft.Panels.cs` | A player walks through a doorway cut in a flat sheet and a shot passes a 10 by 40 cm slot, on host and client | A scratch blueprint: a wall panel with a doorway and a slot, a door skin with a pane welded to `o0`; `--zfight-props` style check by eye on the rims at 1 m and 30 m; the probe's numbers to a file |
| 3 | **The machine** | `storage.cut`, `IsLaserCutter`, `ConstructLaserCutter.cs` (orders, the bed's slot taking a formed panel or a flat piece, Piece in, the hand feed, the stroke, the offcut bin and port, Piece out, look line, screen, console row), the other machines' refusals, both parts from `laser_cutter_parts`, recipes, `Categories.json`, the bed's drawn blank. Protocol bump | new `ConstructLaserCutter.cs`, `PartKit/Generators/LaserCutterParts.cs`, `Constructs/Parts/LaserCutter`; `ConstructParts.cs`, `WorkOrders.cs`, `ConstructExtruder.cs`, `ConstructPress.cs`, `ConstructPanelFormer.cs`, `ConstructConveyor.cs`, `MachineScreens.cs`, `Render/RouteRender.cs`, `Generators/Assembling.cs`, docs | A former, a conveyor, a cutter and a rack: an order queued at the cutter by the window tool is cut from a formed blank and racked, the offcut in its bin, on host and client; a cut plate from a hand-fed extruded plate | That line in a window; a hand-fed blank; a hand-fed L-angle (refused); a wrong panel ("not its order"); the offcut bin full; power cut mid-stroke (the blank stays); save and load with a blank on the bed and an order queued; `--gen-parts laser_cutter_parts --check`, `--check-construct-parts`, `--look-report` |
| 4 | **Cut-outs in the designer** | The Cut-outs mode, tools, Mirror, snap, readouts, red cuts; designs with cut keys; ORDER to a cutter; the window tool's `design cut ...` verbs | `UI/UiPanelDesigner.cs`, `App/Program.SandboxPanelDesigner.cs`, `PanelDesigns.cs`, `ConstructCrafting.cs`, the window tool | A door with a window is drawn by mouse, saved, ordered and arrives cut, the preview's hash the rack's | By mouse in the window; a client designs and orders, the host holds the same hash; a late joiner gets the design; a slider dragged with cuts on (cuts turn red and back) |
| 5 | **Chains, bills, the terminal, the station** | `CutterInputs` (a former's or an Extruder's blank); the cutter in `LogiMap`; `Bill.Cut`, the bill line, `--bill`; the terminal's rows; `OrderFor`; the picker on a cutter; the Production Showcase's cutter; the code guide | `ConstructLogisticsPlan.cs`, `ConstructLogistics.cs`, `ConstructCrafting.cs`, `UI/UiCrafting.cs`, `WorkOrders.cs` (`ConstructBill`), `VehicleKit/VehicleCore.cs`, `ConstructStation.cs`, `UI/UiRecipePicker*.cs`, `Generators/ProductionShowcase.cs`, docs | One ORDER at the designer, with only bulk steel in the former's bin, ends with a cut panel in the tray and nobody touching anything | The Showcase in a window, host and client; `--bill` on a scratch blueprint with two cut panels; a Building Station build of it |
| 6 | **The Pane** | `Cut Pane (...)`: its key and part (one flat piece, the opening's outline less 2 mm, glass, its `back` socket), its blank and offcut at the cutter; the designer's Glazed box and ORDER's extra panes; a pane in `Bill.Cut` and a blueprint step welding it to `o0` | `HullLoft.Cuts.cs`, `ConstructLaserCutter.cs`, `UI/UiPanelDesigner.cs`, `WorkOrders.cs` | A door skin with a glazed window ordered once arrives with its pane, and the pane welds into the opening by its socket | In a window: the order, the pane on `o0`, a shot breaking the pane alone; `--form-panel "Cut Pane (...)"` |

Order: 2 needs 1; 3 needs 1; 4 needs 1 (and 3 to see an order through); 5 needs 3; 6 needs 2 (the `o` sockets), 3 and 4. Phases 2 and 3 touch different files
and can run side by side; 4 is the only one in `UiPanelDesigner.cs`. Phase 5 touches `ConstructLogistics*.cs`,
`ConstructCrafting.cs` and `ConstructStation.cs`, which smart logistics phase 4 and the station's phase 2 are editing
now: start it only when those are on main.

## 11. Risks

1. **The longer name.** (A cut plate's key is short: its blank is at most 29 characters.) If something does hold a run-time name in 128 bytes (phase 1 finds out before anything else),
   the fallback is a key that must fit 128: about four cuts on a preset, one or two on a loft, the designer counting
   characters as it counts facets. The design otherwise stands.
2. **The same part on every peer.** Pieces are computed in whole centimetres and mapped through the facet's corners by
   one expression; a shared corner is computed once a make and used by both pieces. The panel's "made in slices equals
   made whole" check (`PanelHash`) is run on every sample key in phase 1, and host against client in a window.
3. **Openings on steep or curved sheet.** A collision box is the axis-aligned bounds of its patch, so beside an opening
   on a slope the neighbours' boxes lean into it: the clear opening is smaller than the drawn one, and on a fold past
   45 degrees it can close. The designer's readout says the clear size and "closed: drawn only" when it is nothing, so
   the player is told, not surprised. Flat and gently crowned panels (walls, doors, decks, plates) open truly.
4. **Rims and z-fighting.** A cut makes new drawn faces beside hidden boxes. The inset rule that keeps a panel's edges
   apart is applied on every rim; phase 2 looks at rims near and far. Fix the generator's rule, never a key's output.
5. **Facets on fully curved panels.** A big cut on an 8 by 6 panel touches many facets; the limit will refuse some
   designs a player thinks are simple (a wheel arch in a deeply crowned fender). If phase 1's counts are worse than the
   estimates, raise the limit before raising the slice (144 facets are 24 slices), and say so in the report.
6. **Armour with holes.** A firing port lets rays out and in. That is the point, and it is the player's trade; no
   balance rule is proposed.
7. **A cut-away socket in an old blueprint.** A design changed after a blueprint welded to an edge socket the new cut
   removes is a different key, so the old blueprint still builds the old part: nothing breaks. Noted because it looks
   like a risk and is not one.
8. **Numbers.** The sizes, powers, times, the bed's four slots, the offcut bins and the 3 cm web are first guesses.

## 12. Questions for the user

They will be asked together; each stands on its own. The first option is the recommendation and what the phases assume.

1. **What should the Laser Cutter be?**
   - A (recommended): a machine that cuts **openings, notches and corners out of panels** (windows, firing ports,
     hatches, doorways, lightening holes, shaped outlines), drawn in the Panel Designer. Nothing in the game removes
     material today.
   - B: a **saw for extruded stock**: lengths (the Extruder already cuts these exactly) and mitred ends (a look only).
   - C: a machine or tool that **cuts finished parts and wrecks apart** for salvage: a far bigger job, outside the
     production chain.
2. **Its own machine, or a head on the Panel Former?**
   - A (recommended): its **own machine**, after the former on the line, with its own orders, power draw and offcut
     bin. A former is not held up while a panel is cut, and the cutter can later take other blanks.
   - B: a **cutting head fitted to a Panel Former**: the former forms and cuts in one stroke. About two phases less
     work (estimate), one machine fewer on the floor, and no second step to watch.
3. **Where does a flat cut plate come from?**
   - A (recommended): the **Panel Former forms a flat sheet** (12 to 40 mm) and the cutter cuts it: one path for every
     cut part.
   - B: the cutter also has **its own sheet bin** and cuts flat plates straight from material, with no former. Simpler
     for a small base, but then two machines make flat sheet.
   - C: A now, and later the cutter also takes **thick extruded plate** (10 cm and up) from an Extruder, for heavy
     armour with ports: one more phase.
4. **What shapes can be cut?**
   - A (recommended): **straight edges and diagonals**: rectangles, eight-sided "round" holes, slots, cut corners, up
     to six a panel. The design fits in the part's name, so it saves, travels and bills like every panel.
   - B: **free outlines** drawn point by point, with true curves. Needs designs stored per world and sent to every
     player before any part that uses one: about two more phases (estimate) and a new kind of thing to lose.
   - C: **rectangles only**: the least work, no round holes or cut corners.
5. **Do openings open for real?**
   - A (recommended): yes: shots, bodies and players pass any opening of **10 cm or more**. Doorways, windows and
     firing ports work; armour with a hole has a hole.
   - B: only **large** ones (50 cm and up: doors, hatches, windows); small holes are looks and weight.
   - C: no: cut-outs are **looks and weight only**; the panel still blocks everything.
6. **What happens to the material cut away?**
   - A (recommended): it **all comes back** as bulk material in the cutter's offcut bin, for a chute to take. A cut
     costs power and time.
   - B: **half** comes back.
   - C: it is **lost**.
7. **How many sizes?**
   - A (recommended): **two**, matching the two Panel Formers (2.5 x 1.8 m and 6 x 4.5 m), and no tiers.
   - B: **four** size classes like the Press. The two bigger ones would have nothing to cut until a Large former
     exists.
   - C: two sizes, each in the machines' **five tiers** (speed and power by tier): ten parts and their recipes.
8. **How is a cutter told what to cut?**
   - A (recommended): **by orders only**, as a Panel Former: from the designer, a terminal, a console's bill, a
     Building Station, or its own Queue button.
   - B: also a standing **Design setting**: with no order queued it cuts every matching panel that arrives, so a line
     runs with no terminal.
9. **A socket in each opening?**
   - A (recommended): yes: each opening of 10 cm or more gets **one socket in its middle**, so a pane, hatch, lamp or
     vent welds into it. This is the first of the "player-added sockets" the Panel Designer's list asks for.
   - B: yes, and add a **Pane** the cutter cuts to an opening's own outline from glass sheet, so windows are glazed by
     the same design: one more phase.
   - C: no sockets: weld into an opening by the freeform tool.

## 13. The user's answers (2026-11-13)

1. It **cuts openings, notches and corners out of panels** (A).
2. **Its own machine** (A).
3. **Not A, B or C as written:** "I wanted to be able to cut pieces made in the panel former or in an extruder." So the
   cutter takes a formed panel **and an extruded piece** from the start, not thick plate "later"; it has no sheet bin of its own.
   The phases must say which extruded sections can be cut (plate and sheet at least) and how a cut extrusion is keyed.
4. **Straight edges and diagonals**, up to six a panel (A).
5. Openings of **10 cm and more open for real** (A).
6. The offcut **all comes back** as bulk material (A).
7. **Two sizes, no tiers** (A).
8. **By orders only** (A).
9. **A socket in each opening, and a Pane** cut to the opening's outline from glass sheet (B): one more phase.
