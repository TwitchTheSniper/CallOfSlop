# Press rework phase 5: single-piece recipes leave the Assembler (2026-11-13)

Branch `claude/press-phase-5-d1ecf1`, Windows PC. Protocol 276 -> 277. Design: `press-rework.md` section 2 (the sort), section 6
(the Assembler after the rework) and section 12 (the user's answers, which cut the die list down).

## What moved

A recipe leaves the Assembler when **a die already presses that very part from a standard blank** (a preset in
`Constructs/Pressing.json`, written by `Source/PartKit/Generators/PressParts.cs`). The sort lives in
`Source/PartKit/Generators/Assembling.cs` (`Moved`, `PressedNow`), so it is data by rule, not a hand-edited table: 66 rows.

| Family | Rows | Die |
| --- | --- | --- |
| Brackets: L Bracket, Corner Bracket, Gusset 0.25 m, Gusset 0.5 m | 4 | Bracket |
| Housings (every size and material) | 21 | Housing |
| Plates: Steel Plate, Sheet/Plate Steel 1 m, Tread Plate 1 m, Mesh Grate 1 m, Armour Plate 1 m | 6 | Plate |
| Kits: Gear Set, Circuit Board, Piston Set | 3 | Gear, Board, Piston |
| Logic inputs: Button, Switch | 2 | Button |
| Gearboxes: 5-Speed, CVT, Automatic, Reversing, Limited-Slip Diff, Locking Diff | 6 | Drive |
| Actuators: Piston 1x1 Medium, Motor Hinge, Turntable, Free Pivot 1x1 | 4 | Actuator |
| Springs: Leaf Spring, Rigid Mount, Light Suspension | 3 | Spring |
| Cables: Wiring Loom, Copper Coil, Power/Data Lead Reel, Fluid Hose Reel, Fuel Line | 6 | Cable |
| Control: AND, NOT, Timer, Delay, Compare, Speed Sensor | 6 | Control |
| Tanks: Fuel Tank, Small Fuel Tank, Fuel Drum, Fluid Tank, Small Fluid Tank | 5 | Tank |

Phase 3b's dies had patterns but no presets; this phase adds 31 (`PressParts.Presets3b`), so each moved part has a blank and a
press class that takes it. `--press-part presets`: 0 problems.

## What stayed, and why

- **Engine cylinders** (Small, Medium, Large, Capital) and every other `enginePiece`: sized gun and engine parts are their own
  phase (design answer 4). The Cylinder die's presets stand, but the recipes stay.
- **Frame Beam**: the basic building piece. Building pieces keep their Assembler recipes (answer 3), and the Beam die's three
  patterns are all like the Frame Beam, so pressing it would move a building piece.
- **Differential and Proximity Sensor**: two kinds of part input, so assemblies by the sort's own rule (section 2, rule 3).
- **Suspension, Drive Shaft 1 m, U Joint, CV Joint, Stub Axle, Wheel Hub**: tier 1 Assembler parts whose die takes **Bearings**
  in bay 2, and the Bearing is a tier 2 Assembler part. Pressing them would put a first vehicle's drivetrain behind tier 2.
  Open question 1 below.
- Everything the design already left alone: the 380 multi-part rows, the Thruster Assembler's and the Tailor's.

## How a moved row keeps a save working

A row is **not deleted**: it becomes `{"output": "...", "pressed": true}` in place. An Assembler's recipe setting is saved as
`1 +` the row's place in its family's list (`WorldSaveBodies` writes settings by key, the value a float), so deleting 66 rows
would have moved every saved Assembler after the first of them onto another recipe. Markers keep every index.

- `AssemblingTable.Recipes` and `ByOutput` leave the markers out, so nothing builds them and no order routes to an Assembler.
- `ByMachine` keeps them (indices), and `AssembleRecipe.Pressed` is the flag. `AsmState.Pressed` is the new state: the look line
  reads `the Gear Set is pressed now: a Gear Die and a steel round 0.15 x 0.15 x 0.15 m: set another recipe, or order it at a
  Press`, the machine screen `Pressed now / Gear Set / Use a Press: Gear Die`, and a Line Console's state is Fault.
- The choice **label keeps its plain name** ("Gear Set", not "Gear Set (pressed)"): a blueprint's or a mod's
  `"config": {"recipe": "Gear Set"}` still resolves. The suffix was tried and refused such a blueprint outright.
- The picker shows the part under a "Pressed" category, dimmed, with the die in its tooltip; the Technologies page and the
  learnt-technology notice say "your Presses press X" for a part a die presses (`UiTechnologies.MakersLine`).
- A terminal's chain finds a press as a pressed kit's maker (`ConstructCrafting.MachineMakes`), so an order for a Clutch asks a
  linked Gear Die press for its Gear Set.

## The pressed gearbox's speed (verification item 1)

A **fault, now fixed**. The Drive and Shaft dies scaled `maxTorque` with `"ref": 900`, a stand-in used when the like part's own
value is 0. Every catalogue gearbox and differential is **unrated** (`maxTorque` absent = no limit), so the stand-in gave every
pressed gearbox a 900 N m cap where its catalogue twin had none: `ConstructDrive` then capped the drivetrain. The stand-in is
gone from both dies (a rated like part, such as the U Joint's 4,000 N m, still scales as `d^3 x strength`).

- Before: `Pressed Part (drive.auto solid 50x50x50 steel)` in the 4x4 Truck drove 41.7 m at 11.5 m/s; the catalogue Automatic
  Gearbox 77.8 m at 17.9 m/s.
- After: the same pressed gearbox drives **77.5 m at 17.9 m/s**. `--press-part drive.auto "solid 50x50x50 steel"` now prints
  `maxTorque 0`.

## Tried by hand (throwaway saves, scratch blueprints removed afterwards)

- **A moved part pressed**: a scratch bench (Press + Bracket Die + battery), `feed Press 1 Extrusion l-angle 15x15x15 steel` ->
  an **L Bracket** lies loose by the press. With a Gear Die and `round 15x15x15 steel` the **Gear Set** lands on the press's tray.
- **An Assembler recipe that uses a moved part, end to end**: a bench of Press (Gear Die) chuted into a Workshop Assembler set to
  the **Clutch** (2 steel + 1 Gear Set), fed steel by bin. The pressed Gear Set rode the chute into the Assembler and the
  Assembler's tray held **Clutch x1**.
- **An order for a moved part routes to a press**: on the Production Showcase, `logi order L Bracket 1` reads
  `queued at the Press: Extrusion l-angle 15x15x15 steel 0/1 from the Extruder` (before this phase it went to an Assembler).
- **An old save on a moved recipe**: an Assembler whose setting names the Gear Set reads `Pressed now / Gear Set / Use a Press:
  Gear Die`, and says the same after `saveworld` and `loadworld`.
- `--check-construct-parts` and `--gen-parts all --check`: clean. Sections run by name (the ones that cover this): **Press stock,
  Assembling, Assembling network, Wheel orders, Display screens**: 0 failures.
- Three tests picked a moved part as their Assembler recipe and had to be pointed at one that stays (`Wall Clock`,
  `Fuel Canister`); the Assembling section's "can be fed or built" check now also accepts a die's preset. No section was added.

## Not verified

- The showcase order end to end: the L Bracket order was accepted and routed to the press, but the blank rides the showcase's
  conveyors at about 0.12 m/s, so the stroke was not watched there (the phase 4 notes say the same of that layout). The stroke
  itself was watched on the scratch bench instead.
- A survival start played through: checked by data only. A Starter Assembler (tier 1) builds a Press (104 steel) and an Extruder
  (76 steel), the start kit already carries the Button, Bracket and Gear Dies, and the Extruder cuts `round 15x15x15 steel`, so
  ore -> metal -> steel -> Press + Extruder -> Gear Set holds without new start items. Nothing was added to `SurvivalStart.json`.
- Host and client were not compared for a pressed-marker Assembler (the state is read from the table both sides hold).
- `docs/mods/MODS.md` was not touched: the Assembling format is still `call-of-slop-assembling/1`, since `"pressed"` is one new
  optional key, not a new format.

## Open questions for the user

1. **Answered 2026-11-13: move them, bay 2 is a Gear Set.** The Shaft die's five patterns and the Spring die's Suspension pattern
   take Gear Sets in bay 2 in place of Bearings (shafts `0 + 0.5 x length`, at least 1; the Suspension `0 + 2 x across`), and the
   six rows are markers like the other 66 (72 now). The Bearing stays tier 2: bronze is a tier 2 foundry's, so a tier 1 recipe
   would have had no material. The Suspension's old recipe took rubber, not a Gear Set: it takes the Gear Set as the others do.
   `--press-part shaft "round 20x20x100 steel"`: `bay 2: 1 Gear Set`. Not tried in a window. The question was:
   **The Bearing tier.** Six drivetrain parts (Suspension, Drive Shaft 1 m, U Joint, CV Joint, Stub Axle, Wheel Hub) stayed on
   the Assembler only because their die wants Bearings, a tier 2 part, where the recipe was tier 1. Should the Bearing become a
   tier 1 part (it is 2 bronze), or should those six move and cost a tier?
2. **The engine cylinders.** Small to Capital Cylinder have a working Cylinder die and preset today, and stayed only because
   engine pieces are the sized-engine phase's. Move them now, or wait for that phase?
3. **The Frame Beam.** The Beam die presses girders, columns and struts, all like the Frame Beam. Keep it an Assembler part as a
   building piece, or let the Beam die make it and leave only `Parts/Building` and `Parts/Capital` behind?
4. **Marker rows for good?** The markers keep saved recipe indices safe for ever, but they also keep 66 dead rows in
   `Assembling.json`. Should a later phase drop them once a by-name migration for the recipe setting exists (design section 8)?

## The user's answers (2026-11-13) and phase 5b

Branch `claude/press-phase-5b-7e6ac4`. Protocol 277 -> 281 (no new bytes: the recipe values mean other rows).

1. **The Bearing tier: move the six, bay 2 a Gear Set.** Done on main before 5b (`74e8e0a0`), as answer 1 above says.
2. **The engine cylinders: wait** for the sized gun and engine parts phase. Left alone: the Cylinder die's presets stand, and its
   rows stay on the Assembler (so `PressedLine` is null for them and the Technologies page says the Assemblers build them).
3. **The Frame Beam: the Beam die presses it.** A preset `Frame Beam <- beam.girder, i-beam 45x45x200 steel` (`PressParts.Presets`;
   the 50 cm girder blank wants a Medium Press, tier 2, so the preset is held to a Small Press's 45 cm), and `"Frame Beam"` joined
   `Assembling.Moved`. `Parts/Building` and `Parts/Capital` stay on the Assembler. **The start, traced before moving it:** no
   recipe, chained order or start takes a Frame Beam as an input (it was only ever an output); the Press (104 steel, 21 rubber),
   Extruder (76 steel, 15 rubber) and Die Lathe are tier 1 Assembler parts of steel and rubber alone; the tier 1 Extruder's 6 x 6 m
   section and 48 m length take the blank; the Beam die is a tier 1 die (a Die Lathe cuts it). Blueprints that hold Frame Beams
   (vehicles, the showcase, the turrets) need them in stock like any part. The fix: **a Beam Die in `SurvivalStart.json`**, beside
   the Button, Bracket and Gear Dies, so a first frame is Extruder + Press, no Die Lathe first.
4. **Drop the marker rows now, no migration.** The generator removes the 73 rows; `"pressed"` is no longer a key of the format
   (a file naming it is refused). What leaned on a marker reads the presets: `AssemblingTable.PressedLine(def)` and `PressedDieOf`
   (a preset part with no Assembler recipe); the picker's dimmed "Pressed" tiles come from `PressTable.PresetByKey` for the plain
   Assembler family; `UiTechnologies.MakersLine` asks `PressedDieOf` (it used to ask for any preset, which named the cylinders as
   pressed); a blueprint's or mod's `"config": {"recipe": "Gear Set"}` loads with the recipe None and
   `[blueprint] FILE: steps[n]: the Gear Set is pressed now: ...: its Assembler's recipe is None` (`ConstructConfig.ParseValues`).
   No shipped blueprint, map, story file or generator names a moved recipe. `tools/MIGRATING.md` tells players to set saved
   Assemblers again. Removed: the loader's marker branch, `AsmState.Pressed` in `StateOf` and the look line, the Line Console's
   Fault for it, `ProductionLines.RecipeRefusal`'s and `WorkOrders`' pressed-row refusals, `AssembleRecipe.PressedText`.
   **Also fixed:** `ConstructLoot.Refuse` refused every pressed part ("no recipe to show its tier") since phase 5, so the loot
   tables silently lost their Gear Sets, Circuit Boards, Copper Coils and so on; a part a die presses is taken again.

**Dead code left in other sessions' files** (they were being edited at the same time), for a later tidy:
- `ConstructCrafting.cs` line ~389: `if (r.Pressed || ...)` in `MachineMakes`: `AssembleRecipe.Pressed` is now `=> false`.
- `MachineScreens.cs` line ~124: the `AsmState.Pressed` "Pressed now / Use a Press" card: nothing sets that state.
- To remove with them: `AssembleRecipe.Pressed` and `PressedDie()` (ConstructAssemblingTable.cs) and the `AsmState.Pressed`
  member (ConstructAssembling.cs; it is not saved or sent, so it may go).
- `GameStoryTech.cs` line 74 still says "Your Assemblers build it now" for a taught technology whatever makes it (not a marker
  use; it predates phase 5).

**Tried (throwaway save, scratch blueprint removed):** a Press with a Beam Die fed `Extrusion i-beam 45x45x200 steel` printed
`a Press built a Frame Beam` and `loose near it: Frame Beam x1`; the same blueprint's Workshop Assembler named `"recipe": "Gear Set"`:
it loaded with the line above and its screen read `No recipe set`. The Workshop Assembler's picker opened with a "Pressed" category
and no moved part among its recipe tiles. `--check-construct-parts`, `--gen-parts all --check` and `--press-part presets` clean;
sections Press stock, Assembling, Assembling network, Wheel orders, Display screens: 0 failures.

**Not verified:** the "Pressed" tab's own tiles (the click on it did not land in the scripted shot); a survival start played
through to a first pressed Frame Beam (checked by data and the bench only); an old save opened on this build; host and client.
