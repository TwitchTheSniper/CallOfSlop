# Smart logistics phase 3: notes (2026-11-12)

Branch `claude/logistics-phase3-cadf97`, Windows PC. Protocol 271 -> 272. Code guide: production.md "Smart logistics" (the
bullets from "Press bays and Panel Former sheet" on). Design: smart-logistics.md sections 6 and 7, the user's answers in section 15.

## Done
- Step 1: a Panel Former's chained order asks for sheet (`FormerInputs`, `SheetKey`, `SheetUnits`); sheet arriving counts for
  its link; a Panel Former is a destination whose link is open. Press bays were already destinations (Press phase 4). Makers with
  a conveyor way to the destination are chosen first.
- Step 2 (part): Logistics settings on racks, Routers and terminals; stock targets from `storeKeep` (scan, low mark, one refill a
  target, 5 s cooldown, never robbing a store, below every order); `storeTakes` and `storePriority` in the storage choice;
  `routerSpill`, `routerHold`; terminals ranked by `feedPriority` then age; targets saved (LOGI fourth part); look line; window
  tool verbs `logi set PART|key|value`, `logi take PART|NAME|N`, `logi targets`, `logi fill PART|NAME|N`.

## Tried by hand (Production Showcase, throwaway save folders)
- Hood ordered at the terminal: one link, 1 sheet (solid 50x10x100 steel) asked of the panel line's Extruder #417.
- Rack #286 filled with 6 Gear Set, `storeKeep` 6: 1 taken, no refill; 2 taken, a refill of 2 asked of Assembler #284.
- saveworld with the refill open, loadworld in a second process: the target and its one link came back, nothing asked twice
  (the first try asked twice: fixed, a target takes up a loaded refill). `storeKeep` 0: target, link and unstarted order gone.
- Host: settings set, look line "keeps 6 of each kind it holds (refills at 4); takes only what it holds; store priority 5".

## Not verified
- **The pieces riding in.** On the showcase, the panel line's Extruder #417 stalls "blocked: no room on its out bed" and
  Assembler #284 idles with a Gear Set order (a plain terminal order for a Gear Set idles there the same way, so it is the
  showcase's state, not this change). So no sheet reached the Former and no refill reached the rack in these runs.
- **A joined client:** three tries, the client said "could not reach host" each time while the host ran (default port and
  27999); not looked into. The client's look line is made from the settings, which ride as settings.
- `routerSpill`, `routerHold`, `storeTakes` and `storePriority` at run time (built, not watched), `feedPriority` between two
  terminals (the showcase has one), tick cost (the scan walks every body's parts every 2 s; the rest is 2 Hz and allocates
  nothing in a steady step; the look line's text allocates on a change only).

## Choices made (questions for the user)
1. **Which sheet a Panel Former orders**: always a 0.5 x 0.1 x 1 m solid bar in the panel's material. Should it ask for what a
   linked Extruder already makes when that is thin enough, or a sheet sized to the panel?
2. **Low mark** is keep less a quarter *rounded up* (the design's example "keep 6 refills after 2 taken, not 1" needs it;
   "keep 20 refills at 15" still holds).
3. **"Keep what it holds" applies to the kinds in the rack when the setting is first seen** (up to 8). A kind put in later is not
   kept until the setting is set to 0 and back. Should new kinds join while the setting is on?
4. **A refill is not rounded to the maker's batch** (asks exactly what is short).
5. **`storeTakes` has no material choice yet** (Anything, Only what it holds now, Nothing). A material needs an option list per
   world's materials: add it, or is "only what it holds" enough?
6. **`routerSpill` also takes pieces that have nowhere to go** (no storage with room) down the pinned lane rather than holding
   them stuck. Right, or should a pinned lane only take what would have gone to storage?
7. **`routerHold` holds for a cut, narrow or full way**, but not for an order that closed (that piece goes to storage).

## Left for the next phase
The terminal's Stock tab and `CActKind.StockTarget`; `feedPriority` on machines and `linePriority` on Line Consoles (7.2's
conflict rule); High chains queued ahead of Normal ones in a maker's queue (7.3); blueprint `"targets"`; store monitor rows; bin
targets (bulk, phase 5); a showcase layout where the sheet Extruder and the Gear Set Assembler run, to watch a refill end to end.

## The user's answers (2026-11-13)
1. A Panel Former's chained order asks for **a sheet sized to the panel** (a change: to build).
3. "Keep what it holds" stays with **the first kinds only** (as built).
4. A refill stays **exact**, not rounded to the batch (as built).
5. `storeTakes` gets **no material choice** (as built).
6. `routerSpill` keeps taking pieces with nowhere to go (as built).
7. `routerHold` keeps sending a closed order's piece to storage (as built).
(2, the low mark's rounding, was not asked: it follows the design's own example.)
