# Verification run, 2026-11-14 (b) (main at ee05c247, protocol 311)

The fifth verification: what merged on 2026-11-14 (the Panel Designer's flat plan, arms and cranes phase 6, bored cylinders) and what
run 4 (`verification-2026-11-14.md`) left. Every window run is a throwaway one from `bin\Release\net10.0`:
`dotnet CallOfSlop.dll --window-shot OUT.png --save-dir %TEMP%\...\saves\NAME --no-splash --no-upnp --spawn-at 140,0,-100 [--blueprint NAME] --do "..."`
(below only the `--do` script is given). Host and client: two windows over RakNet on 127.0.0.1 port 27995, `--net host:27995` /
`--net join:127.0.0.1:27995`, the client started 25 s after the host. Scratch blueprints went into `bin/` only (none committed).

| # | Area | Result |
| --- | --- | --- |
| 1 | Designer flat plan | **Partly**: client drags, sliders, the plate's refusal, the cut plate made and held, the old design loaded; the client's cut panel never cut (unexplained, below) |
| 2 | Bridge Crane | **Partly**: carries across its span, saved mid-carry and loaded, client agrees; carry height not measurable; the wait for a pinned player not reached |
| 3 | Bored cylinders | **Verified** for numbers (pieces, engines, old world, `--speed-test`); **not**: a bored engine running (the tool can't seat a driver) |
| 4 | Run 4's leftovers | **Partly**: rows 5-8 still unreadable (Down does nothing); recycler and paste not reachable by the tool; the stall partly, and a **fault fixed** (an Extruder order under 10 cm was taken and stalled with a wrong reason) |

## 1. The Panel Designer's flat plan: partly

Host `--net host:27995` with the Production Showcase: `wait 60; [touch Start @ Line Console#2;] disown; design bring 1; wait 1500; cutter; cutter feed KEY; cutter await; ...`;
client: `wait 400; design open; design width 100; design length 50; design cut r50,25,30,20,5; design plandrag 50,25 40,30; design cut m0,20,20;
design plandrag 20,0.5 35,0.5; design plandrag 0.5,20 0.5,30; design plan; design width 40; design plan; design width 100; design order; ...`.
- **Drags on the joined client**: the opening `r50,25` to `r40,30` (5 cm snap), the corner's along tip 20 to 35 and its across tip 20 to 30
  (`m0,35,30`); each drag moved the key, the edge length and the cut time (3.3 to 5.3 s) and the preview hash. **Verified.**
- **Sliders and plan both ways**: plan to sliders, each drag showed at once in the key and readouts; slider to plan, Width 40 made the plan's
  sheet `50 x 40 cm` with the cuts kept, Width 100 restored the same key and preview hash (11158a92). The per-cut rows themselves have no
  window-tool readout: **partly**.
- **Extruded plate on the client**: `design cut clear; design blank 1; design plateh 10; design width 50; design length 100; design cut
  r40,30,20,20,5; design plandrag 40,30 25,60; design order`: readouts `Extruder 3: yes, it extrudes the plate` (1 and 2 named their other
  feeds); the chain got the blank from the showcase's Extruder and the cutter cut it: the client reads `held 1 x Cut Plate (plate 50x10x100 steel
  | r25,50,20,20,5)`. **Verified** (ordered and made; not walked to the rack and picked up).
- **A plate too big**: Width 150 on the client: every Extruder reads `a section 1.5 x 0.1 m is wider than the Extruder's die (0.5 x 0.5 m): attach
  Die Frames to its sides ...`. **Verified.** (Switching to plate kept the panel's two cuts, as designed; a third opening dragged onto them
  turned both red, `Two cut-outs overlap.`)
- **The client's cut panel: ordered, never cut.** The order queued at the cutter with the client's key (`... | r40,30,30,20,5 m0,35,20`), but in
  four net runs it was never cut: (a) line started before `disown`: the cutter `NoMaterial`, `cutter feed` refused `ConveyorFull`, and the
  chain asked the Panel Former for a 1 m sheet the showcase's 0.5 m Extruders can't make (run 4's known showcase gap); (b) blank fed first,
  `touch Start @ Line Console#2` after `design bring`: the touch found the button but its `key Fire` never ran (and an extra `key Fire` didn't
  start the line either); the cutter `Held off`, row `its line is stopped: press Start on its console`. On the host offline the same steps cut
  every key (corner cuts, the designer's own ORDER, after `disown`) and the row went to `Collect`. So the game's cutter and keys are fine; what
  stops a net host's Start after `design bring` is not found (window tool, `Program.WindowShot.cs` touch, about line 1620, or the console's
  ownership on a net host). **Not verified**; worth a look.
- **A design saved before the flat plan**: the commit before the designer merge (`06aeb1eb7^1`) built in a throwaway worktree; there `design cut
  r50,25,30,20,5; design cut m0,20,20; design name Old Plan; design save; saveworld OLDDES`. Here `loadworld OLDDES`, the designer's list
  scrolled (`drag 368,250 368,560`) and "This world's designs: Old Plan" clicked (`click 200,530`): `design plan: blank panel; sheet 50 x 100 cm;
  [0] r50,25,30,20,5; [1] m0,20,20; ... Laser Cutter 1: yes, 4.2 s a cut`, and `cutter order` of its key queued. **Verified.**

## 2. The Bridge Crane: partly

- **Carrying across its span.** `wait 60; lifter crane; wait 300; lifter; wait 200; lifter; ...`: `Medium Crane Bridge body 35: 10 rail
  sections of 4 m, top 6.0 m; reach across 0.7-7.3, along -10.0 to 10.0, rails ok`; the crane `Carry Carrying ... Extrusion plate 30x30x50
  laminate`, then `Return`, then the pressed Button (`button.button plate 30x30x50 laminate`) carried out (2 to 3 carries in 70 s). Points:
  `Out@22.0 PressBay@22.0/1 PressBay@22.0/2 Rack@23.0 Rack@24.0`. A blank's carry takes 842 ticks (14 s), a Button's 918, a return about 770;
  the notes' "about 6.4 s a carry" is not what this rig shows (not a fault by itself: the notes may count a carry differently).
- **Carry height: not measured.** `lifter points` sampled every 100 ticks through a carry reads `rise 8.00; ... Carry height 8.0` the whole
  way (the setting and its cap, not the hand's height); no window-tool readout gives the hand's height, and a side picture
  (`from Medium Crane Bridge 14,0,0; shot`) shows the bridge and trolley but not the hanging piece clearly enough to read a height.
- **A player where the piece lowers.** `lifter crane; wait 120; lifter pen me put; wait 300; lifter points; wait 500; lifter points; lifter free`:
  `pen: 1 player(s) to <-162.20, 0.33, -154.12>, 4 Crate Stacks round them; Extrusion plate 30x30x50 laminate: its put point, the piece's
  box 0.30 x 0.50 m`; the carry ended (`put: 1 calls`) with `players pushed 0 times, pinned 0`, `Alarm 0`. The put point is a press
  bay: the piece goes into the bay's store, not down onto the player, so neither the nudge nor the wait comes into it. A piece large enough
  to need the wait (or a put onto open ground) is not in the rig: **not reached**, as in the builder's notes.
- **A save mid-carry.** `... wait 600; saveworld CR1` at 498/842 ticks of a blank's carry; a fresh process `loadworld CR1; lifter; wait 300;
  lifter; ...`: 25 bodies; the crane first `Stuck Holding ... Extrusion plate 30x30x50 laminate 6/6`, then `Carry Carrying ... 186/577`
  (it picked the carry up again from where its hand was), then `Return`; the rack 3 (as saved), the Button out of the press. **Verified.**
- **A client far along the rails.** Host `lifter crane; wait 3000; lifter; ...`, client `wait 300; lifter; from Medium Crane Bridge 45,3,0;
  wait 300; lifter`: the client holds the same crane state (`Carry Carrying moves 1 Pressed Part (button...) 488/918`, the host
  at the same carries). 60 m out along the rails is past the Proving Ground's arena edge (`the player stops at the edge`), so 45 m was
  the farthest; the window tool's camera faced away from the crane there, so what is drawn at that range was not seen. **Partly.**

## 3. Bored cylinders: verified for the numbers; the engine running not

- **Pieces of one outer size, plain and bored** (`--press-part cylinder.cylinder "KEY"`, the game's own press rule):

  | Key | Mass | Health | kW |
  | --- | --- | --- | --- |
  | `pipe10 45x45x75 steel` (plain) | 28 kg | 412 hp | 40 |
  | `pipe5 45x45x75 steel` (bore 35 cm) | 16 kg | 236 hp | 82.8 |
  | `pipe20 180x180x300 steel` (plain) | 560 kg | 15080 hp | 800 |
  | `pipe11 180x180x300 steel` (bore 158 cm) | 325.33 kg | 8760 hp | 1036.8 |

  Every figure equals the notes' table (section 3).
- **Whole engines through the kit's build** (`--gen-engine ...`, BlueprintScript, the game's build rules): `--power 300 --tier 1 --layout v
  --boost turbo`: `Small V8: 320 kW, 455 kg, 2.40 x 1.70 x 1.70 m ... Small Cylinder` (plain; `no turbo below tier 4`); `--power 300 --tier 5`:
  `Small V4 Performance: 306 kW, 222 kg, 1.51 x 1.68 x 1.68 m, pipe7 45x45x75`; `--power 2000 --tier 4`: `Small W21 Performance
  Supercharged 2006 kW, 988 kg, 4.26 x 3.06 x 1.98 m`; `--power 900 --tier 5 --layout radial`: `Small R8 Racing Turbo 927 kW, 435 kg`. The
  same cross-section (1.70 against 1.68 m) plain and bored; the rows equal the notes' table.
- **An engine pressed before the change.** The commit before the merge (`dbae7eabd`, `c33337aec^1`) built in a throwaway worktree;
  there `--gen-engine --power 2000 --tier 5` gave the old `Small B16 Racing Turbo: 1991 kW, 3857 kg ... pipe15 60x60x105`; spawned
  (`wait 60; engines; saveworld OLDB16`): `B16 1991 kW - no fuel: link a fuel tank ... fault NoTank`. Here `loadworld OLDB16; engines`:
  21 bodies, the same 43 pieces by the same keys, `B16 1374 kW - no fuel: link a fuel tank; ... 1373.6 kW, 2754 N m; fault NoTank`
  (the cylinder thicker than plain: 74 to 51 kW each, as the notes' section 5 says). Health: the key's piece reads 1113 hp on this build
  (`--press-part ... pipe15 60x60x105`), the notes' main figure, so nothing is held down on load. **Misfire not read**: the blueprint has
  no tank, and "no fuel" comes before the starved or misfire text.
- **`--speed-test`** on two shipped generators' vehicles with the kit's tier-5 engine (`--gen-vehicle "Bandit Car" --seed 1 --engine-tier 5
  --top-speed 33`, `"Bandit Box Truck" ... --top-speed 30`): `1694 kg, 596 kW, top 24.8 m/s, grip` and `2726 kg, 611 kW, 22.2 m/s,
  power against drag`: the notes' figures.
- **A bored engine running in a window: not verified.** The Bandit Car above (V6 596 kW, pressed `pipe` cylinders) spawned
  (`--blueprint "VT Bored Car"`): `engines: 0 V6 596 kW, frugal ... fault None; running False`. Getting a driver into its Seat failed
  three ways (`near Seat 1.5; aim Seat; use`, `aim Seat; use`, `from Seat 0,0.6,1.2; use`; `seat NAME` finds only the colony's buildings),
  so `key MoveForward` never reached the engine: `running False, rpm now 0` throughout. A window-tool gap, not a game fault as far as seen.

## 4. Run 4's leftovers: partly, one fault fixed

- **Line 2's rows 5 to 8: not read.** `touch Start @ Line Console#2; wait 120; panel Line Console#2; touch Down @ Line Console#2; wait 30; panel ...` (four
  Downs, each followed by its `key Fire`): every touch hit `panel 0 button 5 "Down"`, the screen stayed `Running 5/8  1-4 of 8`, the cursor on
  Extruder 1, `scroll 0/4`. Rows 1-4: Extruder 1 and 2 and the Wheel Former running, the Assembler `Stopped no recipe`. The same as run 4: a
  Down press on the Line Console does not move its cursor or page. Not traced (a Line Console's buttons are `ProductionLines`; a tool
  press may reach only button 0). **Not verified; possible fault.**
- **The recycler with a wrecked preset part: not driven.** The showcase has no Recycler and the window tool has no verb that puts a wreck into
  a Recycler's intake (as run 4 found). The yield cap itself was read in run 4 (`--press-part presets`).
- **A survival paste paid from made parts: not driven.** No window-tool verb pastes; only the paste's check was read in run 4.
- **The Extruder's mid-piece stall: partly; a fault found and fixed on the way.** `extr build Extruder 0 8; extr build Run-out Table 6 3; fitpart
  Run-out Table | Extruder outBed; fill Extruder 2000 steel; extr queue Extruder 10 10 5000; wait 600; extr shoot Run-out Table; ...`: longest
  56 m with the table, 48 m once shot, and the 50 m piece grows on past the shot (2.0 m at the shot, 26.1 m 2.2 minutes later, 0.2 m/s;
  `Extruding`, never cut short). It reaches 48 m only after 4 minutes, over this run's limit: the stall itself not waited out.
  - **Fault (fixed):** my first order was 5 x 5 cm: `extr Extruder 5 5 5000` read `takes it` and the order queued, then the Extruder stalled
    for good with `too many different pieces in this game`. No section under `Extrusion.MinSectionCm` (10 cm) has a def (`Extrusion.Valid`,
    `ConstructExtrusion.cs` 391), and `WorkOrders.Refusal` (`WorkOrders.cs`, the Extruder's branch) checked the die's top but no bottom, so the
    order was taken and `Extrusion.Def` returned null every step (`ConstructExtruder.cs` 383, shown by line 895's fallback text). Fixed after review: `WorkOrders.Refusal` now refuses every key `Extrusion.Valid` refuses, by `Extrusion.KeyRefusal` (too thin or wide, a round or pipe not square, a pipe's wall, a section with a wall), which covers a client's `CraftOrder` too (`ConstructCrafting.Place` asks it per machine); a stalled order already queued names its key's reason (`ConstructExtruder.StallShape`). Tried: `extr queue Extruder 5 5 500`: `a section 0.05 x 0.05 m: sections are 0.1 to 32 m a side`; 10 x 10 takes it. Players' sliders may
    clamp above 10 cm already; a terminal bill, a mod or a client message could send one.

## Not run

Self-test sections (batched by the coordinator). Builders' files were not changed.
