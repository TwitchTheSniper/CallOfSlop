# The Factory Showcase (2026-11-14)

A new production showcase made from scratch (the user: "showing off all the machines in a connected way, so there is a complete
system using all the machines"): one blueprint, **Factory Showcase** (`Constructs/Blueprints/FactoryShowcase.json`, group
Production), one body, in which raw ore, wood, stone and sand go in at the west end and the **Works Runabout**
(`Constructs/Blueprints/WorksRunabout.json`) is built at the east end. Both come from `--gen-parts factory_showcase`
(`Source/PartKit/Generators/FactoryShowcase.cs`). The old Production Showcase is untouched.

## The flow

```
raw heaps (iron ore, copper ore, stone, wood, sand)            Recycler (a wreck) --+
   |  chutes                                                                        v
Workshop Smelter: ore -> metal --> Workshop Foundry: metal + stone -> STEEL ------------------+
   |                          \--> Workshop Foundry: copper + metal -> BRONZE                 |
Workshop Smelter: copper ore -> COPPER                                                         |
Sawmill: wood -> planks --> Foundry: planks + sand -> RUBBER ;  Sawmill: planks -> CLOTH         |
Smelter: sand -> GLASS                                                                          |
   |  every material into the yard (bins)                                                       |
   v                                                                                             v
PANELS: sheet Extruder (+ Die Frame) -> Panel Former -> Router P -+-> Join -> Laser Cutter --+   press hall bin
        glass Panel Former (glass by chute) ----------------------+  (cut panels, panes)     |      |
        Router P way 2 (formed panels) ---------------------------------------------------+   |      v
WHEELS: Wheel Former (rubber) ------------------------------------------------------+   |   |   blank Extruder -> Routers R1-R5
                                                                                    v   v   v      | way 3 into each press:
                                              Join -> Join -> Router S --> Building Station        |  Gear  -> tray -> Parts Shelf
                                                                 | way 2: Long Piece Rack         |  Tank  -> tray -> station chute
                                                                 | way 3: Router T -> Crafting Terminal, Panel Designer
ASSEMBLY: Assembler B (bearings) <-> Assembler A (seat, controls, gearbox, diff, arms, pivots)   |  Bracket (die from the tool room's lathe) -> tray -> station
          Parts Shelf (Gear Sets, Copper Coils) -> both                                          |  Cylinder, Engine -> Join line
          A -> Router A -> Join line -------------------------------------------------> station  (back port)
copper Extruder -> Cable press -> tray -> Parts Shelf
tool room: bar Extruder -> Die Lathe -> Die Rack -> chute -> Bracket press's empty die slot
```

## The machine list (from `Constructs/Parts`), each one's job in the flow

| Machine | Job here |
| --- | --- |
| Storage Volume (raw heaps) | Iron ore (two), copper ore, stone, wood, sand: the raw input, filled in creative, empty in survival |
| Workshop Smelter (iron) | Iron ore to metal, for steel and bronze (tier 2: its chutes move 20 a second) |
| Workshop Smelter (copper) | Copper ore to copper: wire stock for the station, bronze, the copper Extruder |
| Workshop Foundry (steel) | Metal and stone to steel, the factory's main material (recipe set to steel) |
| Workshop Foundry (bronze) | Copper and metal to bronze, for Bearings and Piston Sets (recipe set to bronze) |
| Sawmill (two) | Wood to planks; planks to cloth (the seat) |
| Foundry | Planks and sand to rubber (wheels, Driving Module) |
| Smelter | Sand to glass (the windscreen pane) |
| Materials Bins, Storage Volumes (yard, press hall, parts store) | Refined material between refiners and makers; the parts store holds Gear Sets, Copper Coils and Bearings |
| Recycler | Scrap in: a wreck dropped in its mouth becomes metal for the steel Foundry |
| Extruder (blank line) | Every press's ordered blank, and the Runabout's extruded rails and bars |
| Extruder with a Die Frame (sheet) | Steel sheet sized to each panel, into the Panel Former (the frame widens its die to take 0.8 m sheet) |
| Extruder (copper) | Copper rounds into the Cable press |
| Extruder (tool room) | 0.25 m steel bars, on its own settings, into the Die Lathe |
| Panel Former (steel) | The hood, and the windscreen frame's blank for the cutter |
| Panel Former (glass) | Formed glass sheet, the pane's blank (fed glass by chute) |
| Laser Cutter | Cuts the windscreen frame's opening and the glass pane |
| Wheel Former | The Runabout's four rubber wheels |
| Airframe Former | The Runabout's rear spoiler (a Short Wing Segment), dropped on a Lay-down Pad |
| Press, Gear Die | Gear Sets for the gearbox and differential (tray to the parts store) |
| Press, Tank Die | The fuel tank (tray to the station) |
| Press, Piston Die | Piston Sets (steel round, bronze kit by chute), riding into the Cylinder press's kit hopper |
| Press, Cylinder Die | The engine's cylinder (pipe blank, Piston Set kit) |
| Press, Engine Die | The crank, carburettor and Small Flywheel (Bearing kits from the parts store) |
| Press, Bracket Die | The L Brackets; its die comes from the tool room |
| Press, Cable Die | Copper Coils for the Servo Motors |
| Die Lathe, Die Rack | Cut Bracket Dies from bars; the rack chutes a die into the Bracket press's empty slot |
| Workshop Assembler A | Seat, Driving Module, Transmission, Differential, Swing Arms, Servo Motors, Steering Pivots |
| Workshop Assembler B | The bearing shop (set to Bearings, on no console), tray to the parts store |
| Conveyors, Joins, Routers (small and Large) | Every piece's road; Large junctions where a 1.18 m Differential rides |
| Piece Racks | Router storage that feeds back to the station; the tool room's die stock |
| Lay-down Pad, Military Loader Arm | The wing's drop and pick: the arm fits it from the pad onto the build |
| Loader Arms (two) | Fit parts from the station's hold onto the build |
| Building Station | Builds the Works Runabout; orders every lacking part at once |
| Crafting Terminal, Panel Designer | Hand orders: a part, or a designed panel, made on the line and delivered by Router T |
| Smart Terminal, Inventory Beacon | On the parts store's chutes: a player pulls a spare there; the beacon lists it nearby (survival) |
| Line Consoles (two), Number Readout, Text Signs, Medium Text Sign | Start the refinery and the making line; monitors on their Data outs; the network board on Router S |
| Small Genset, Fuel Tanks, Battery, Switch, Charge Gauge, Lamps | Power, the main switch, the station's fuel (a tank piped into its Fluid in) |

**Left out, and why.**
- Medium, Large and Capital classes of every machine (Extruder, Die Extension, Run-out Tables of those classes, Panel Former,
  Laser Cutter, Press, formers), the Bridge Crane, its rails and the Lifter Cab: the user (2026-11-14): "don't make medium or large
  class machinery. Just stick with the one size for now." The crane and the cab work only with pieces past a conveyor's 2 m, which
  only those classes make. (The small Run-out Table is left out too: it only lengthens pieces past 48 m, which nothing here needs.)
- Fan Former: nothing the Runabout carries is a fan; a fan with nothing to drive it would be a part for its own sake.
- Tailor and Thruster Assembler: no cloth goods or thrusters in the product; the Workshop Assembler shows the family.
- Wheel Rack, Parts Shelf: the Parts Shelf's three bins filled with one stroke's 20 Copper Coils and blocked the presses' trays (tried
  and replaced by a Storage Volume, which packs by volume); a Wheel Rack holds catalogue wheels, and these are formed.
- Job Board: the story's colony work board, not a production machine.
- Selector, Blueprint Desk, Build Gantry and the other build stations: building tools, not production.
