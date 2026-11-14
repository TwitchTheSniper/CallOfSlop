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

(The notes are finished at the end of the work: machine list, numbers, what was proved and not.)
