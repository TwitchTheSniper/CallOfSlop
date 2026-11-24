# Engine trains (Sean, 2026-11-24)

A ship's torch drive is built from pieces welded one behind the other on the bell's axis: the design is the "Ship engines with interior parts" doc (https://claude.ai/artifact/H9zjS1PjyaydegKPdBCf57). This page is the state in game and what is left.

## Phase 1: the minimum viable engine (this branch)

Generator: `Source/PartKit/Generators/EngineTrainParts.cs` (`--gen-parts engine_train`), writing `Constructs/Parts/EngineTrain/` and the `Module Engine XS` blueprint.

| Part | What it is | Works |
| --- | --- | --- |
| Drive Nozzle XS | The bell, 240 kN, stood on a 1 m Round Module Plug's centre socket (`mount`); its `train` socket faces back through the plug's hole | Thruster that needs `core` and `injector` |
| Reaction Chamber XS | 0.6 m across, 1.1 m long; coils and quartz ports glow with the burn, two dials sweep with the heat | `core`, serves 240 kN |
| Fuel Injector XS | Ring manifold with eight lamps that run round with the burn, four valves that stroke, a sight glass; the only `Fuel in` of the engine | `injector` (new `WorksKind`), serves 240 kN |

The rules (`ConstructWorks`, `ConstructThrust`):

- The bell finds its pieces as it finds any ship works: through the head's own links (the train welded behind it), then by the old in-line search. Without its train it runs at the bare share (a quarter).
- Fuel reaches the bell only by a pipe into the Fuel Injector's `Fuel in` (`ConstructWorks.TrainFed`). A tank touching the injector feeds nothing.
- With the pipe gone (shot, or taken off) the bell runs on the injector's buffer, `injectorBuffer` seconds at full throttle (10 s, `Constructs/ShipWorks.json`), then stops. The buffer refills while fed. It is not saved: a world loaded with the pipe cut has an empty buffer. A joined client doesn't run the buffer down: it shows an unfed bell that still burns as running on its buffer.
- Only a bell that needs an injector takes a plug that is not on an engine socket as its mount and looks for its pieces through its own welds; older bells find their works as before.
- The Look line and `--check-vehicle`'s works report say whether the train is fed.
- The bell needs no `Fuel in` of its own (`ConstructParts.PortProblems` exempts a thruster that needs an injector).
- A 1 m round socket takes 250 kN, so the XS bell is 240 kN.

Looks (`Source/Render/ConstructRender.DriveFx.cs`): drawn only, from the bell's level that every peer has. Heat eases 3 s up and 25 s down; the box keys are `glow` (heat colour), `chase` (new: the injector's running lamps), `spin` (dials) and `stroke` (valves); one burst of sparks at light-off. Nothing on the wire, no per-tick work.

Press: the three housings are pressed patterns (`PressParts.ToPress.cs`: `reactionchamber`, `fuelinjector`, `drivenozzle`).

Tools: `--engine-check FILE|NAME [--seconds S] [--cut-at S]` builds a blueprint headless, burns it and prints level, cap, feed and buffer once a second; `--cut-at` removes the pipes into the injector at that time.

Fitting: `Module Engine XS` passes the fit rules of a Round Module Socket 1 m in Opening style (size, turn, room, mass and thrust). A window run with the socket standing on the ground refuses only because the engine would go into the ground; a fit on a raised hull plate was not tried in a window.

## Next

1. Custom length and diameter on the Press for every piece (serves by length times diameter squared, mass by length times diameter).
2. The other pieces of the doc one at a time: Gimbal Ring, Reheat Ring, Cooling Jacket, Armour Collar, Igniter, Throttle Valve, Mixture Controller, Fuel Buffer, Fuel Filter, Turbopump Drive, Fuel Selector, Sensor Ring, Spacer, Bore Adapter.
3. The zone rules (hot, feed, supply; at most 16 pieces) and bigger bores.
4. Renaming Drive Core to Reaction Chamber waits: it changes many blueprints.
