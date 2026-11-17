# Engine generators: notes

The user (2026-11-17): fuelled modular engines can be used as generators for stations and bases; modular engines in the construction of
factories and resource points. Branch `claude/engine-generators`. The player's guide is power-and-links.md "Engine generators", the kit's
ENGINEKIT.md "Generator sets".

## What was built

1. **Generator Heads** (part kit `generator_heads`): Small 150 kW, Medium 400, Large 1000, Huge 4000; 90 to 95%; spool 2 to 6 s; all with a
   small-class Drive in 0.5 m over their feet. Role `generator` with `"shaft"` (efficiency) and `"fuel": "none"`, so a head is a plant for
   everything plants do (the circuit's source, its buffer, the run wire, PlantShow; not the reactor hum: a set is heard as its engine) and `ShaftIn` (the drills' shaft feeds find it).
2. **ConstructGenset** (server, inside `ConstructPlant.Step`, after the plants): GenMax, the start/stop rule, the head's level (spool, only
   as fast as the engine carries it, shed when it doesn't), the engine's governed rpm and throttle, the load at the crank (`GenLoad`, in
   `ConstructDrillDrive.Carry` with the other shaft loads, so `Burn` bills it), the head's Out (load 0..1, passed through by the logic tick).
   `GenRuns` joins `DriveOn` and the power step's "wants" and start conditions, as `RotorRuns` does. `Governs` only for an engine nobody drives
   with no driven wheels.
3. **Readout**: the look line on a head (`ConstructGenset.Look`), host and client; the pose carries GenState, GenMax, out and draw (protocol 339).
4. **Engine Kit**: `EngineKitGenset` (`--genset`, `--genset-ship`, `--genset-run`, `--genset-for`), two shipped Militia sets.
5. **Factory kit hook**: `Source/FactoryKit/FactoryGenset.cs`, additive (the kit's rework is on `claude/cargo-parts-factory`).

A set's smoke share (`Construct.SmokeLevel`) follows the head's load, from the engine's own sums (`EngineDef`); a modular engine that no set
runs keeps the old rule (its flywheel file burns no fuel, so it gives no stack smoke share: the engine's own puffs are EngineFx's).

## Measured (`--genset-run`, 2026-11-17, cloud box)

| Set | Out kW | l/kWh full | half | a fifth | Worst tick |
| --- | --- | --- | --- | --- | --- |
| small 120 kW W3 t1 | 102.6 | 1.54 | 2.14 | 2.27 | 0.08 ms |
| medium 307 kW V4 t2 | 268.5 | 1.21 | 1.67 | 1.76 | 0.43 ms |
| large 800 kW B16 t3 | 714 | 1.85 | 2.53 | 2.68 | 0.13 ms |
| huge 2966 kW B16 t5 | 2677 | 1.68 | 2.27 | 2.63 | 0.09 ms |

(The half and a fifth columns include starting and charging the batteries.) Before `Headroom` and shedding, a 307 kW set lugged down to
309 rpm at full load and never came back: the governor asked exactly the engine's peak power, any rpm under the peak was short, and the drill
rule pulled the rpm down with the share it carried. Now the set gives 95% of the engine and sheds load instead of being pulled down.

## Factory kit: how to use the hook

The kit (`FactoryProgramme`, `FactoryScript.PowerPlant`) today puts `Programme.Gensets` copies of the `Small Genset` plant (a fuel plant
part, no engine) in the power rect. To use engine sets instead:

1. **Programme**: `FactoryGenset.For(p.PowerKw, style.Tier, seed, out why)` gives one `Set` for the whole factory's power (its machines'
   draw plus the margin). One set, not a row: a body's engine is its first that runs, so a second engine on the same body would not run.
   If a factory needs more than one engine, the extra sets have to be bodies of their own (a skid each, `EngineKitGenset.Make`), cabled in.
   Null means the tier's Small engine can't give it (past about 1 MW at tier 1, 3 MW at tier 5): fall back to the `Small Genset` plants.
2. **Plan**: reserve `FactoryGenset.Footprint(set)` in the power rect, measured from the head's feet: the engine runs back along the
   script's -z (X0..X1 wide, Z0 behind), the tank and battery rows ahead along +z (to Z1). Feet on the floor's top socket grid (y 0.3, x and
   z on x.25 or x.75).
3. **Script**: `FactoryGenset.Place(s, set, feet, Style, dice)` returns the head, flywheel, tanks and batteries. Wire the main switch to
   `placed.Head` (its run input) instead of each genset, and keep the existing Battery or use the placed ones (any battery on the body is
   the mains: `ConstructPower.Mains` reads all of them, so wire the switch to every one, or to none). A Charge Gauge already on the post
   works; a Number Readout or Dial wired from the head shows its load (0 to 1).
4. **Fuel**: the tanks are Large Fuel Tanks (300 l each): a haul route or a hose fills them. A refinery's own fuel output could be piped
   into the first tank later.

## Not done / open

- Saves: nothing new is saved (the set's state is rebuilt from the batteries and demand on the first tick). A set that was running when
  saved restarts on load if the batteries are under half or something draws.
- A joined client's look line was tried through a written and read pose (`--genset-run`), not over the real transport in a window.
- Heads aren't size-classed: a Medium or bigger engine reaches one by a drive shaft (through an adapter), not bolted on; the kit only
  builds Small-piece engines for sets.
- No heat model (modular engines don't overheat). The head has no sound of its own: the engine's loop, by rpm and load, is the set's.
- Survival: the heads have Assembler recipes (steel and Copper Coils, tier 2); the shipped sets aren't in any start or loot table.
