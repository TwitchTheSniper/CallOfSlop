---
paths:
  - "Source/Constructs/ConstructAssembling*.cs"
  - "Source/Constructs/ConstructRefining*.cs"
  - "Source/Constructs/ConstructRecycl*.cs"
  - "Source/Constructs/ConstructExtru*.cs"
  - "Source/Constructs/ConstructPress*.cs"
  - "Source/Constructs/ConstructPanelFormer.cs"
  - "Source/Constructs/ConstructConveyor.cs"
  - "Source/Constructs/ConstructLogistics*.cs"
  - "Source/Constructs/ConstructStorage.cs"
  - "Source/Constructs/ConstructCrafting.cs"
  - "Source/Constructs/ConstructTerminal.cs"
  - "Source/Constructs/ConstructStation.cs"
  - "Source/Constructs/ConstructLifters.cs"
  - "Source/Constructs/ConstructModules.cs"
  - "Source/Constructs/ConstructPower.cs"
  - "Source/Constructs/ConstructLogic.cs"
  - "Source/Constructs/ConstructLinks.cs"
  - "Source/Constructs/ConstructRoutes*.cs"
  - "Source/Constructs/ConstructAutoRoute.cs"
  - "Source/Constructs/ProductionLines*.cs"
  - "Source/Constructs/MachineScreens.cs"
  - "Source/Constructs/RouterScreen.cs"
  - "Source/Constructs/RuntimeParts.cs"
  - "Source/Constructs/Pressed.cs"
  - "Source/Constructs/PressDies.cs"
  - "Source/Constructs/WorkOrders.cs"
  - "Source/Constructs/StockTargets.cs"
  - "Constructs/*.json"
  - "docs/sandbox/constructs/production.md"
  - "docs/sandbox/constructs/power-and-links.md"
  - "docs/sandbox/constructs/storage-and-cargo.md"
---

# Production machines, power, logic and links: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- **Run-time pieces.** What an Extruder, a Press or a Wheel Former makes is a def made at run time whose id is this machine's own: read it through `CPart.D`, `DefOf` or `ConstructParts.Def`, never `ConstructParts.All[id]`; on the wire use `ConstructStorage.WriteDef`/`ReadDef` (its key), never `U16(id)`; in a hash use `ExKey`.
- Most of `Constructs/Parts`, `Assembling.json` and `ModularEngine.json` are the part kit's output: change the generator (`Source/PartKit`), and cap every amount by the file's limits (one recipe over `MaxUnits` makes the whole Assembling table refuse).
- A machine's draw is decided by its store (Busy, Held), never by its signal output (2026-11-11: every working machine drew 0 W once a Data out made it a signal part).
- 2026-11-11: conveyor riders froze on a client 1 s after the server's last word, and a second line on one body put riders out of their link's group (appended, while the step reads them grouped). Anything a client draws moving between messages is stepped on the client by the server's rule; a list read in groups is kept grouped where it is added to.
- 2026-11-11: the Production Showcase ticked at 8-60 ms twice a second while its Wheel Former grew: a growing wheel is a new def every 5 cm, and `ConstructHulls.Of` made a convex hull of its lathe (6 ms, about 60 KB) for each, inside the 296-part body's collider rebuild. A run-time def made per growth step sets `ColliderBoxes` (hidden boxes, no hull) as a growing round does; time a tick spike down to the per-part call (stopwatch per part in `ConstructCollider.Build`) before guessing at its source.
- 2026-11-11: auto-routed links, conveyors worst, arrived with two to four short right-angle jogs: the lattice search may only turn after a run of 1.5 radii and arrives on a 10 cm stub, so an offset of a few cm became a detour. A router judges its result, not only its cost: re-solve the ends in at most two turns, size stubs and runs by the link's width, keep hugging a tie-breaker, and measure with `--route-ends`.
- 2026-11-12: a Conveyor Router's Alarm output read 0 while a piece was stuck: the logic tick sets every non-logic part's next output to 0 unless it is passed through (`ConstructLogic`: the Extruder's Length, the Router's Alarm). A machine that sets `Out` itself also gets a pass-through line in the logic tick, and its output is checked on a wire, not only on the part.
- 2026-11-12: a Router's way lamps never lit on the Production Showcase: a big body draws parts that never change from a cached batch (`ConstructRenderBatch.StaticBoxes`). A new kind of box drawn by state also goes in `StaticBoxes`' list, and is checked on a big body.
- 2026-11-12: the Panel Former's screen couldn't be touched: its board was a look box, which the crosshair's ray passes. A screen with buttons is mounted on a solid box.
- 2026-11-12: a world loaded with every main switch off: `ReadBody` read each part's State, then called `ConstructConfig.Init`, whose `StartState` sets a switch from its Start on setting. A loader that calls an initialiser after reading saved fields keeps the saved ones, and a save is tried in a fresh process.
- Before merging a machine change, run the live sections that name that kind of machine (`--selftest-list | grep`): a screen that became its line state, a new Data out, a new tile after the resources each broke a neighbour's section on main (2026-11-12).
- 2026-11-13: arms never lifted a press's output (lifters phase 4 marked it done): `MadeBy` asked the press's rows, which only a lathe has since presses went to sized dies. A check of what a machine makes asks its current path (`SizedDieOf`: presets and `Pressed` keys) and is tried with the machine running, not read.
- 2026-11-13: a Building Station's build with an Ammo Container never finished: `ConstructConveyor.RackCheck` refused every storage part for every rack, the station's hold included, so the order's part could never arrive and the window tool's stock was refused. A new kind of part a build may need is tried through each way into the station's hold (an order, a hand, a stock), not only the parts the showcase builds.
