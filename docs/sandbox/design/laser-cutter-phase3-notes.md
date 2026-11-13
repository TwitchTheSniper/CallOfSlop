# Laser Cutter, phase 3: notes (2026-11-13)

Phase 3 of `laser-cutter.md`, its first coherent part: the machine in two sizes, a blank in, the cut, the cut part out, the offcut
counted in tenths and handed into its bin, its recipe and tier. Protocol 295 (main was 294; the merger renumbers). The user-facing
summary is `docs/sandbox/constructs/production.md` "The Laser Cutter".

## What was built

- `Source/Constructs/ConstructLaserCutter.cs` (new): `storage.cut` (`kw`, `at`, `maxWidth`, `maxLength`, `maxHeight`) on an `any` bin
  (the offcut bin); `Refusal`, `Work` (10 Hz, from the Extruder family's step), `TakeFromConveyor`, `Seconds` (the law), `Fits`, `TierFor`,
  `Lacks`, `Status`, `Look`, and a per-key `Job` cache (blank, edge, thickness, offcut tenths, the blank's bounds) read once from
  `HullLoft.CutJob` (new, `HullLoft.Cuts.cs`).
- `Source/PartKit/Generators/LaserCutterParts.cs` (new): `--gen-parts laser_cutter_parts` writes `Constructs/Parts/LaserCutter/`
  (Laser Cutter 480 kg, Medium Laser Cutter 5,600 kg, `"collider": "boxes"`). Recipes come from the Assembling generator's rule by mass
  (steel 96 + rubber 19; steel 1,120 + rubber 224, minimum tier 2): `Assembling.cs` is not edited.
- Hooks beside the formers' (nothing of theirs changed): `ConstructExtruder` (step, status, look line), `ConstructConveyor` (Piece in),
  `WorkOrders` (refusals both ways, Queue), `MachineScreens` (its own screen), `ProductionLines` (the new stall's word),
  `ConstructCrafting` (what it lacks), `ConstructStorage` (`cut`), `ConstructParts` (`IsLaserCutter`, `CutKw`, `CutMaxHeight`),
  `ExtrudeStall.OffcutFull` (16). Former-only paths exclude the cutter: `ConstructPanelFormer.TierFor`, `ConstructLogisticsPlan`'s
  former inputs, a bill's order kind, QueueHere's re-material.
- Window tool: `cutter`, `cutter order KEY [N]`, `cutter feed KEY`, `cutter await` (`Program.WindowShotCutter.cs`; `"cutter"` added to
  `WsKnown`).

## Choices made

1. **A member of the Panel Former's family.** The cutter is `IsPanelFormer` as well as `IsLaserCutter`, so it is a line machine (Run
   wire, power, lamp, console list, conveyor out, out bed) through the former's paths, and each former hook asks `IsLaserCutter` first.
2. **The bed holds one blank**, kept as the store's job (`JobAmount` 1, `JobMaterial`), saved and loaded with the store. The design's
   four-blank slot (`storage.pieces`) is left out: one blank is all a stroke needs, and it costs no new wire or save bytes.
3. **The blank is known by the head order.** So when the head order goes (Cancel, Clear) with an uncut blank on the bed, the blank drops
   onto the out bed as a loose part (`GiveBack`), and is never cut to the next order. Other ways an order leaves a queue (a bill re-dealt,
   a terminal's cancel) were not looked at. See question 1.
4. **Offcut tenths** ride in the store's input amount and material (`InAmount`, `InMaterial`), which are already saved and sent: no new
   bytes. Tenths of another material round to the nearest unit into the bin before the new material's units.
5. **The offcut is checked before the cut part leaves**: a full bin stalls with the cut done and the part on the bed, so nothing is lost.
6. **Edge length** is each cut's outline clipped to the sheet, less the stretches lying on the sheet's own edges (a cut corner counts
   only its diagonal).
7. **No design setting yet**: Queue repeats the head order (its Order count), and with no order says where orders come from. A Design
   button and the picker are phase 5.
8. **Body colour `#a8365e`** (crimson), apart from the Panel Former's green, the designer's blue and the table's family colours.
   `--look-report` was not run on it.

## Tried by hand (this PC)

Window tool, Proving Ground at 140,0,-100, a scratch blueprint (in `bin` only, removed): six foundations, a Panel Former, a conveyor, a
Laser Cutter, nothing on its out port (cut parts drop on the out bed).

- **Offline**: a cut order at the cutter, the former fed 400 steel and ordered the blank; the blank formed, rode the conveyor, was taken
  onto the bed and cut. Edge 1.61 m (a 40 x 30 cm opening with 3 cm corners and a 20 cm cut corner): **20 mm panel 6.9 s, 40 mm 11.7 s,
  5 cm plate 14.1 s, 10 cm plate 26.2 s**, each the law's figure. The plates were put on the conveyor (`cutter feed`). Offcut 0.6, 1.1,
  1.4, 2.8: the bin went 0 +0.6, 1 +0.7, 3 +0.1, 5 +0.9. Refused as meant: a formed panel at the cutter, a cut key at the former, a 4 x 2 m
  blank at the small cutter ("too large for this cutter: 3.98 x 1.98 m"). A wrong blank (a 10 cm plate for a 5 cm plate's order) waited on
  the conveyor; the screen said "Needs its blank".
- **Host and a joined client** (`--net host:27632` / `join`): the client timed the same cuts, 6.9 s and 26.2 s, read the same offcut
  (3 steel +0.4), found both cut parts by name and filled the same screen. `saveload` on the host: the same parts.
- **Cancel mid-stroke** (offline, Cancel on its screen through the crosshair at 20% of a 10 cm plate's cut): the order went, the bed
  emptied and the plate lay loose by the cutter.
- `--gen-parts all --check`, `--check-construct-parts`: pass. Self-test by name: Compound collision, Display screens, Interaction sweep,
  Press stock, Extruder, Wheel orders, No z-fighting: 0 failures.

## Not done, not verified

- The rest of phase 3 as the design lists it: a loose blank let go on the in bed (the mouth check); F on the machine taking back a blank;
  a chute emptying the Offcut port; the offcut bin full (the stall was not reached by hand); power cut mid-stroke; save and load with a
  blank on the bed and an order queued (the store's job is saved, untried); the bed's blank drawn lying on it (`RouteRender`); the
  Medium Laser Cutter in a run (only its part check); a hand-fed L-angle's refusal (its key is refused by phase 1's rule; not ordered here).
- Phases 4 and 5: orders from the Panel Designer, the Crafting Terminal and bills; the chain asking a former or an Extruder for the blank;
  the Building Station; the recipe picker on the cutter; the Production Showcase's cutter.
- `--look-report`; Linux CI.

## Questions for the user (asked together; the first option is the recommendation)

1. **A blank on the bed when its order is cancelled.**
   - A (recommended): it drops onto the out bed as a loose part, so the next order's blank can come in (as built).
   - B: it stays until F takes it back (F on the machine is not built yet).
   - C: it is kept for the next order that wants it, and anything else waits.
2. **How many blanks the bed holds.**
   - A (recommended): one, as built: the next waits on the conveyor.
   - B: four of the head order's blank, as the design proposed (a slot on the store, more bytes on the wire).
3. **The Queue button with no design setting** (until phase 5's picker).
   - A (recommended): it repeats the head order, as built.
   - B: it does nothing until a design is picked.
