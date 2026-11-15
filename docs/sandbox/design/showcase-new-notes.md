# The Factory Showcase (2026-11-14)

A new production showcase made from scratch (the user: "showing off all the machines in a connected way, so there is a complete
system using all the machines"): one blueprint, **Factory Showcase** (`Constructs/Blueprints/FactoryShowcase.json`, group
Production), one body, in which raw ore, wood, stone and sand go in at the west end and the **Works Runabout**
(`Constructs/Blueprints/WorksRunabout.json`) is built at the east end. Both come from `--gen-parts factory_showcase`
(`Source/PartKit/Generators/FactoryShowcase.cs`). The old Production Showcase is untouched. The player's guide is production.md,
"Factory Showcase".

## The flow

```
raw heaps: iron ore x2, copper ore, stone, wood, sand          Recycler (a dropped wreck) --> metal
  | chutes
Workshop Smelter (ore -> metal) --> Workshop Foundry (metal + stone -> STEEL) ------> yard: steel store, bins of
                                \-> Workshop Foundry (copper + metal -> BRONZE)       rubber, cloth, bronze,
Workshop Smelter (copper ore -> COPPER)   Sawmill (wood -> planks) -> Foundry (RUBBER)  copper, glass
Sawmill (planks -> CLOTH)                 Smelter (sand -> GLASS)                          | chutes
MAIN FLOOR row 1   sheet Extruder + Die Frame -> Panel Former -> Router P -> Join -> Laser Cutter --+
           row 2   glass Panel Former --------------------------------------^                       |
                   Router P way 2 (formed panels), Wheel Former ---> Join ---> Join <---------------+
                   -> Router S -> Join S -> (into the press line's last Large Join)
                   Router S way 2: a rack that feeds back to Join S;  way 3: Router T -> Panel Designer
BACK FLOOR         blank Extruder -> Routers R1..R6, way 3 north into each press: Gear, Tank, Piston, Cylinder, Engine, Bracket
                   Gear press, Cable press (fed by a copper Extruder) -> Join -> Assembler <- its own Router's loop
                   Assembler -> Router A -> Large Join (+ Cylinder press) -> Large Join (+ Engine press) -> last Large Join
                   Piston press -> Cylinder press's kit hopper;  Router A way 2 -> Engine press's kit (Bearings)
                   R6 ahead (rails, bars) -> Join (+ Bracket press) -> Join (+ Tank press) -> last Large Join
                   last Large Join (+ Join S) -> Large Router Q -> the Building Station's back port; way 2 -> Crafting Terminal
                   tool room: bar Extruder (orders only) -> Die Lathe -> die rack -> Die Rack
EAST END           Building Station and its two Loader Arms; Airframe Former -> wing on a Lay-down Pad -> Military Loader Arm
FINISHED           the Works Runabout, built in the bay; a hand-ordered part in the terminal's tray; a spare Bracket Die
```

## The machine list (from `Constructs/Parts`), each one's job in the flow

| Machine | Job here |
| --- | --- |
| Storage Volume (raw heaps) | Iron ore (two), copper ore, stone, wood, sand: the raw input, filled in creative, empty in survival |
| Workshop Smelter (iron) | Iron ore to metal (tier 2: its chutes move 20 a second, a tier 1's 10) |
| Workshop Smelter (copper) | Copper ore to copper: wire stock, bronze, the copper Extruder |
| Workshop Foundry (steel) | Metal and stone to steel, the factory's main material (recipe set) |
| Workshop Foundry (bronze) | Copper and metal to bronze, for Bearings and Piston Sets (recipe set) |
| Sawmill (two) | Wood to planks; planks to cloth (the seat) |
| Foundry | Planks and sand to rubber (wheels, Driving Module) |
| Smelter | Sand to glass (the windscreen pane) |
| Materials Bins, a Storage Volume (yard steel) | Refined material between the refiners and the makers |
| Recycler | Scrap in: a wreck dropped in its mouth becomes metal for the steel Foundry |
| Extruder (blank line) | Every press's ordered blank, and the Runabout's extruded rails and bars |
| Extruder with a Die Frame (sheet) | Steel sheet sized to each panel, into the Panel Former (the frame widens its die past 0.5 m) |
| Extruder (copper) | Copper rounds into the Cable press |
| Extruder (tool room) | Orders only: the tool room bill's 0.25 m bar, into the Die Lathe |
| Panel Former (steel) | The hood, and the windscreen frame's blank for the cutter |
| Panel Former (glass) | Formed glass sheet, the pane's blank (fed glass by chute) |
| Laser Cutter | Cuts the windscreen frame's opening and the glass pane |
| Wheel Former | The Runabout's four rubber wheels |
| Airframe Former | The Runabout's spoiler (a Short Wing Segment), dropped on a Lay-down Pad |
| Press, Gear Die | Gear Sets for the gearbox and differential, by conveyor into the Assembler |
| Press, Tank Die | The pressed fuel tank, by conveyor to the station |
| Press, Piston Die | Piston Sets (steel round, bronze kit by chute) into the Cylinder press's kit hopper |
| Press, Cylinder Die | The engine's cylinder (pipe blank, Piston Set kit) |
| Press, Engine Die | The crank, the Small Carburettor and the Small Flywheel (Bearing kits from the Assembler) |
| Press, Bracket Die | The two L Brackets |
| Press, Cable Die | Copper Coils for the Servo Motors, by conveyor into the Assembler |
| Die Lathe, Die Rack, a die stock rack | The tool room: its console's one-bar bill cuts one spare Bracket Die into the Die Rack |
| Workshop Assembler | Seat, Driving Module, Transmission, Differential, Swing Arms, Servo Motors, Steering Pivots and its own Bearings |
| Conveyors, Joins, Routers (small and Large) | Every piece's road; Large ones where a 1.18 m Differential rides |
| Piece Racks | Router S's storage, which feeds back to the station; the tool room's die stock |
| Lay-down Pad, Military Loader Arm | The wing's drop and pick: the arm fits it from the pad onto the build |
| Loader Arms (two) | Fit parts from the station's hold onto the build |
| Building Station | Builds the Works Runabout; asks for every lacking part at once |
| Crafting Terminal | On the last Router before the station: a part ordered there of any maker rides to its tray |
| Panel Designer | On Router T: a designed panel is formed and rides to its tray |
| Smart Terminal, Inventory Beacon | On the yard's steel store: a player pulls steel there; the beacon lists the yard nearby (survival) |
| Line Consoles (three), Number Readout, Text Sign, Medium Text Sign | The refinery, the making line and the tool room; monitors on their Data outs; the network board on Router S |
| Small Genset, Fuel Tanks, Battery, Switch, Charge Gauge, Lamps | Power and the main switch; a fuel tank on the body's pool piped into the station's Fluid in |

**Left out, and why.**
- Medium, Large and Capital classes of every machine (Extruder, Die Extension, their Run-out Tables, Panel Former, Laser Cutter,
  Press, formers), the Bridge Crane, its rails and the Lifter Cab: the user (2026-11-14), "don't make medium or large class machinery.
  Just stick with the one size for now." The crane and the cab only matter for pieces past a conveyor's 2 m, which only those make.
  The small Run-out Table lengthens pieces past 48 m, which nothing here needs.
- Fan Former: nothing the Runabout carries is a fan; a fan with nothing to drive it would be a part for its own sake.
- Tailor and Thruster Assembler: no cloth goods or thrusters in the product; the Workshop Assembler shows the family.
- Parts Shelf, Wheel Rack, a second Assembler: tried and dropped. The Parts Shelf's three bins filled with one stroke's 20 Copper Coils
  and blocked the presses (a press now sends its parts by conveyor); a second Assembler set to Bearings on its own made 298 and ate
  the copper the Cable press needed (the one Assembler makes its own). A Wheel Rack holds catalogue wheels; these are formed.
- Conveyor Splitter: no route for orders (a Splitter deals in turn); the Routers do its job here.
- Job Board: the story's colony work board, not a production machine. Selector, Blueprint Desk and the other build stations: tools.

## The numbers (2026-11-14, the Windows PC, creative, `--spawn-at -30,0,40 --spawn-yaw 0`)

- 981 parts (884 before the look pass: signs, lanes, labels), 158 links, 118 x 32 m; centre (-33, -24) on the Proving Ground, radius 64 m, in the oval's flat infield.
- The Runabout: 30 parts, 3 links, 649 kg; drive check 56 m at 11.1 m/s, turns 325 degrees, stability 2.95.
- Station Start to Done: under four minutes (Done between the second and third two-minute look): 30/30 parts, 3/3 links.
- Server tick while it builds (`station measure`: the last 150 to 260 frames at each look, so windows, not the whole build): median
  0.12 to 0.15 ms, p99 0.21 to 0.39 ms, worst 0.23 to 0.72 ms. The station's own step: worst 2.5 to 3.3 ms in one tick at Start (its
  costing) with 262 KB allocated in that tick (the station's costing, not new here); 34 KB over the rest of the build.
- `--check-construct-parts`: no REFUSED lines. `--gen-parts factory_showcase --check`: up to date. `--look-report`: the factory 96 %
  painted, 18 colours, 4 % dark or grey, 20 lamps; the Runabout 33 % painted, 3 colours (a body red, a dark chassis, a yellow trim).

## What was proved

- **Host** (the window tool through the player's own paths: Start on the consoles by touch, the station's pick and Start): the Works
  Runabout from raw ore to Done, every part made on the floor, followed with `logi riders` and `logi chain`: a rail from the blank
  Extruder through the Router line; the Differential from the Assembler (Gear Sets from the Gear press, Bearings from its own loop)
  through Router A and the Large Joins; the cylinder (pipe blank, Piston Sets from the Piston press with bronze by chute); the wheels
  through Router S; the windscreen frame (Panel Former, then the Laser Cutter) and its pane (glass Panel Former, then the cutter); the
  spoiler (Airframe Former, the pad, the Military Loader Arm). A Driving Module ordered at the Crafting Terminal arrived in its tray;
  the tool room's bill cut one Bracket Die into the Die Rack and stopped.
- **Joined client** (a second window over RakNet): the station at 0/30, 4/30, 18/30, then Done 30/30 with "DONE, Drive it out" on its
  screen; the orders console's screen and its machines; 22 riders replicated.
- **Every machine**: ran for the Runabout, the terminal's order or the tool room's bill: the eight refiners, the four Extruders, both
  Panel Formers, the Laser Cutter, the Wheel Former, the Airframe Former, all seven presses, the Assembler, the Die Lathe, every Router,
  Join and rack, the pad and all three arms, the Building Station and the Crafting Terminal. **Not run**: the Recycler (it needs a
  wreck; the window tool's `recycle` is a server shortcut, not tried); the Panel Designer (not ordered from); the Smart Terminal and the
  Inventory Beacon (a player's pull; the beacon is survival only); the fuel feed to the station (creative fills the tank).

## Game changes made on the way (each with a lesson in `.claude/rules/production.md`)

1. Conveyors run at 1 m/s (the user; protocol 318).
2. A Building Station asks at once for every part it can fit on arrival and orders the next level ahead while the hold keeps room for the largest ready part (the user's choice, "order ahead, keep room"; the Runabout 7.25 min from Start, against 4 all at once and 12 ready-only), and a full hold
   makes room for the next part by moving what no step can take yet to a rack with a road back, or says it is stuck (`MakeRoomFor`).
3. A terminal's or station's ask goes first to a machine with a way to it (`ConstructCrafting.StepTerminal`).
4. A blueprint step may be `"ordersOnly"`: built with an empty queue, the machine never runs its own settings.
5. A press with a Piece out conveyor sends every part on it, counted for its order (`ConstructPress.PutOut`).
6. An Assembler takes a conveyor's part for any order in its queue (`FeedQueuedPart`): one input conveyor no longer deadlocks.
7. A queued order's inputs are asked in full (`ConstructLogisticsPlan.Need`): only its own chains count, and the hopper only at the head.
8. The window tool: `--spawn-yaw D`, and `stock` prints a bin's bulk.
9. Code review: player blueprints keep `ordersOnly` (`ConstructClip`); a press sends tray parts by conveyor only where it leads to a junction, rack, Assembler or press (`TrayPartsRide`); `Need`'s "ahead" reads this order's place in the queue; a terminal's ask caches each machine's way with its net (`CraftNet.MachineWay`).
10. Look review 2: the refinery console starts on (the refinery runs from spawn; the order lines wait for Start); area boards face west on steel posts, their writing in the area's colour; a press-hall board and arrow on the aisle; Floor Arrows (a new part kit dressing part) along the aisle and the press lines; the panel line green, the station teal, the press hall blue; heaps in their own colours with name signs; lamp cables and monitor links up the posts' backs; a medium board on each press; the window tool's `--dusk F` (lamps' dark light on any map) and `--no-hands` hides the NOCLIP note.
11. Look review 3: every sign's text is checked to fit its board whole (3 lines of 10, `Fit`), area boards read "N NAME", press boards two lines; small signs unlit (fewer lamps, a darker bay at dusk); lamp cables up their posts' backs, the station's fuel pipe in the floor; heaps named on medium boards facing the walkway; the Lay-down Pad, its former and arm 1.5 m east, clear of the Runabout's way out (reversing north to the walkway, an arrow on the floor); raw heaps 8 m boxes (the refinery runs from the start and fills about 2,600 steel of hoppers).

## Not verified

- A survival (`timed`) run: power against one Small Genset and a battery, the fuel feed, wire stock from the copper bin.
- The whole-build worst tick (each figure above is a window of frames).
- A save and load mid-build; the Panel Designer's order; the Recycler; a second Runabout (Repeat); the tool room's die fitted to a press.
- The self-test (not run, by the rules). Sections naming the changed code: "Building station", "Crafting terminal", "Smart logistics",
  "Press", "Assembling", "Conveyors" (`--selftest-list`). Conveyor speed doubled: any section that times a ride may need its numbers.

## Seen in passing

What the runs showed that they weren't testing. Not chased, except the first (fixed: its cause was certain and the fix one line).

1. **Fixed.** Every Wheel, Fan and Airframe Former z-fought with the floor under it: the hopper arrow's centre arguments were swapped,
   so it sat half in the floor. `--check-vehicle Constructs/Blueprints/FactoryShowcase.json` ("FAIL z-fighting ... Wheel Former [0.3 x
   0.051 x 0.012 ...] share a +Y face plane"). `Source/PartKit/Generators/ExtrusionParts.cs:391`; the three part files regenerated.
2. `[gl] error (high, id 1281): GL_INVALID_VALUE ... ObjectLabel: unknown buffer object` twice at every start (after `[props] meshes`
   and after `[render] merged meshes ready`), on host and client; the same 0x501 is then reported as their own by `[foliage]`,
   `[frenzyfx]` and `[terrain]` ("GL error 0x501 while setting up"). Any `--window-shot` on the Proving Ground; `callofslop.log`.
   Guess: a debug label set on a buffer name before it is created (the props mesh upload and the merged-mesh renderer).
3. Pictures from high up show two big blue boxes in front of the camera (the first-person hands drawn at the flying camera).
   `--window-shot ... --at -30,70,-5 --look-at -30,0,-6`, scratch `p1_top.png`, `pic_overview.png`. Guess: the view model in the window
   tool's noclip camera (`Program.WindowShot.cs`).
4. `BlueprintScript.Config` writes any value: a refiner's `"recipe": "steel"` passed the generator and the loader refused the whole
   blueprint ("steps[684]: \"recipe\" isn't a value a Workshop Foundry's recipe takes"). `--gen-parts factory_showcase`, then a window
   run. Guess: `Source/VehicleKit/VehicleCore.cs` Config should parse through `ConstructConfig.ParseValues`.
5. A link `BlueprintScript` lays is checked only against the parts placed before it: a sign post placed later in its path passed the
   generator and the loader refused the blueprint ("links[56]: ... it would pass through the Frame Beam"). `--check-vehicle` on the
   output. Guess: `VehicleCore.cs` Link; check every link again when the text is written.
6. (Fixed, look review 2: an orders-only maker with no order now reads "Waiting", white and steady, on the console and the line sign: `ProductionLines.WaitsForOrders`.) A line console shows idle orders-only makers as "Stopped" while its line runs ("Running 0/16", rows "Stopped no orders"): it reads
   as held off. `panel Line Console#3` in any build run. Guess: `Source/Constructs/ProductionLines.cs` near 402 (a word like Waiting).
7. The station's step allocates 262 KB in one tick at Start (its costing). `station measure`: "worst tick 262488 bytes". Guess:
   `ConstructStation` costing (`StationPreview`, the cost slice).
8. One Copper Coil order of 2 drew a round that pressed into about 20 coils a stroke (197 in all before the re-asking was fixed).
   `stock Storage Volume#9` in run b26 ("Copper Coil x197"). Guess: the cable die's coil count scales with the blank in
   `Constructs/Pressing.json` / `Pressed.cs`; worth a look whether a stroke should make what was ordered.
9. `partinfo PART` printed an empty look line when the player stood far from the part, with no word why. `partinfo Workshop Foundry#1`
   without `near` first (run b17). Guess: `Program.WindowShot.cs` partinfo should say "not in view".

## Open questions (with a recommendation)

1. **The Fan Former** isn't in the flow. Recommendation: leave it out until a showcase product flies.
2. **A Gear Set ordered at the Crafting Terminal** is placed but can't arrive (the Gear press's conveyor runs only into the Assembler).
   Recommendation: a terminal refuses an order whose only maker has no way to it, as it refuses a part dropped loose.
3. **Pooled inputs** in one machine's hopper are still not reserved for the order that asked (fix 7 asks enough). Recommendation:
   per-order input claims in the Assembler.
4. **Ordering everything at once** filled the hold early; fixed by ordering only what can be fitted now or next, plus MakeRoomFor (code review).
5. **The old Production Showcase** still has what this one lacks: the Bridge Crane, its rails, the Lifter Cab and its crane bay (left
   out at the user's word), the Run-out Table, a Splitter and the "conveyor too narrow" demo. Recommendation: keep it as the crane and
   conveyor-rules demo until the medium classes come in.
